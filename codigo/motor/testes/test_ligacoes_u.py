"""G5: ligações com perfis U (UPN, U laminado, Ue) e conflitos de nó (docs/v3/LIGACOES-U.md)."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_flexiveis as F
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.modelo import Barra, No

PLATAFORMA = os.path.join(os.path.dirname(__file__), "..", "..", "..", "testes-aceite", "PLATAFORMA.ed3")
ESF = {"V": 50.0, "N": 0.0}
DIAG = 'L 2" x 3/16"'
MIN_OK = 40   # medido: 74 nós com proposta, 40 ok (esforços do WPF: N 50, V 50, M 30)


def _sug_ver(tipo, perfis, esf=ESF):
    """Contrato do sugerir: `tipo_ligacao` na resposta troca o tipo verificado (G5: solda direta)."""
    d = dict(perfis)
    s = dict(P.sugerir_ligacao(tipo, perfis, esf))
    tipo = s.pop("tipo_ligacao", tipo)
    s.pop("motivo", None)
    d.update(s)
    r = P.verificar_ligacao(tipo, d, esf)
    r["_tipo"] = tipo
    return r


class TestFlexiveisU(unittest.TestCase):
    def test_shear_tab_upn(self):
        """UPN 120 (h 120, alma reta tabelada d = 82 mm, DIN 1026) na alma de W200, V = 45 kN (mínimo de 6.1.5.2,
        viga de piso, sem isenção). Chapa simples com 2 parafusos de 1/2" (o que cabe em 82 mm): o cisalhamento dos
        parafusos reprova (grupo excêntrico). O sugerir troca para solda direta da alma, que passa."""
        pf = dict(perfil_viga="UPN 120", perfil_suporte="W 200 x 26,6", apoio="alma_viga")
        esf = {"V": 45.0}
        r = P.verificar_ligacao("chapa_simples_alma", F._sug_cs(pf, esf), esf)
        self.assertFalse(r["ok"])
        self.assertIn("parafusos: cisalhamento", max(r["verificacoes"], key=lambda v: v["taxa"])["nome"])
        (ch,) = r["geometria"]["chapas"]
        self.assertLessEqual(ch["contorno"][2][1], 82.0 + 1e-6)          # altura da chapa ≤ alma reta
        zt = 60.0                                                       # topo da viga no z local (mm)
        for p in r["geometria"]["parafusos"]:
            self.assertGreaterEqual(zt - p["origem"][2], 19.0 - 1e-6)    # abaixo do início da alma reta
            self.assertLessEqual(zt - p["origem"][2], 120.0 - 19.0)      # acima da mesa inferior
        nomes = {v["nome"]: v for v in r["verificacoes"]}
        self.assertTrue(nomes["parafusos: ponta dentro da mesa do U"]["ok"])
        self.assertIn("e_centro_cisalhamento_mm", r["valores"])
        s = _sug_ver("chapa_simples_alma", pf, esf)
        self.assertEqual(s["_tipo"], "solda_direta_alma")
        self.assertTrue(s["ok"])

    def test_abertura_y_espelha(self):
        pf = dict(perfil_viga="UPN 120", perfil_suporte="W 200 x 26,6", apoio="alma_viga")
        c = F._sug_cs(pf, {"V": 10.0})
        ra, rb = (P.verificar_ligacao("chapa_simples_alma", dict(c, abertura_y=s), {"V": 10.0}) for s in (1, -1))
        self.assertEqual((ra["geometria"]["abertura_y"], rb["geometria"]["abertura_y"]), (1, -1))
        self.assertAlmostEqual(ra["geometria"]["chapas"][0]["origem"][1], -rb["geometria"]["chapas"][0]["origem"][1])
        for pa, pb in zip(ra["geometria"]["parafusos"], rb["geometria"]["parafusos"]):
            self.assertAlmostEqual(pa["origem"][1], -pb["origem"][1])
            self.assertEqual(pa["eixo"][1], -pb["eixo"][1])

    def test_alma_reta_tabelada(self):
        vg = F._perfil("UPN 120", "v")
        self.assertAlmostEqual((vg["d"] - 2 * (vg["tf"] + vg["r"])) / F.MM, 82.0)
        vg = F._perfil("Ue 150x60x20x2,00", "v")   # sem d tabelado: h − 2tf − 2r, r = ri + t
        self.assertAlmostEqual(vg["r"], 2 * vg["tf"])

    def test_composto_por_dict(self):
        """2UPN 80 em caixão (s = 100): o WPF manda as medidas; o apoio só precisa de d, bf, tw, tf."""
        caixao = dict(d=80, bf=190, tw=6, tf=8, nome="2UPN 80 duplo em tubo soldado s=100")
        r = _sug_ver("chapa_simples_alma", dict(perfil_viga="UPN 120", perfil_suporte=caixao, apoio="mesa_pilar"),
                     {"V": 45.0})
        self.assertTrue(r["ok"])

    def test_solda_direta_upn50(self):
        """Conta à mão (docstring de ligacoes_flexiveis._solda_direta): E70, filete 5 mm, 2 × 84 mm →
        0,6·0,707·5·485/1,35 = 762 N/mm; 2·84·0,762 = 128 kN.
        UPN 50 (h 50, tw 5) na mesa de UPN 100, V = 45 kN, A-572 GR.50: Lw = h = 50 mm; a alma do UPN 50 governa,
        0,6·345·50·5/1,10 = 47,0 kN → taxa 0,957."""
        r = P.verificar_ligacao("solda_direta_alma", dict(perfil_viga="UPN 120", perfil_suporte="W 200 x 26,6",
                                                          apoio="mesa_pilar", b_solda=5, comprimento_solda=84),
                                {"V": 1.0, "N": 0.0})
        self.assertAlmostEqual(r["verificacoes"][0]["resistente"] * 2 * 0.084, 128.0, delta=1.0)
        pf = dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="mesa_pilar")
        s = _sug_ver("chapa_simples_alma", pf, {"V": 45.0})
        self.assertEqual(s["_tipo"], "solda_direta_alma")
        self.assertTrue(s["ok"])
        nomes = {v["nome"]: v for v in s["verificacoes"]}
        self.assertAlmostEqual(nomes["alma da viga: escoamento por cisalhamento (Aw = h·tw)"]["taxa"], 45 / 47.045,
                               delta=0.002)
        g = s["geometria"]
        self.assertEqual((g["chapas"], g["parafusos"], len(g["soldas"])), ([], [], 2))
        self.assertTrue(g["pontas"])
        o = L.otimizar("chapa_simples_alma", pf, {"V": 45.0})
        self.assertEqual((o["ok"], o["tipo"]), (True, "solda_direta_alma"))

    def test_ci_nao_abaixo_do_elastico(self):
        for ys, e in (([-0.0375, 0.0375], 0.075), ([-0.019, 0.019], 0.05), ([-0.075, 0, 0.075], 0.075)):
            el = 1 / math.hypot(1 / len(ys), e * max(map(abs, ys)) / sum(y * y for y in ys))
            self.assertGreaterEqual(F.coeficiente_ci(ys, e), el - 1e-12)

    def test_ue_aceito(self):
        r = _sug_ver("chapa_simples_alma", dict(perfil_viga="Ue 150x60x20x2,00", perfil_suporte="W 200 x 26,6",
                                                apoio="alma_viga"), {"V": 5.0})
        self.assertIn("chapas", r["geometria"])

    def test_chapa_maior_que_face(self):
        with self.assertRaises(P.ErroEntrada) as cm:
            P.sugerir_ligacao("chapa_extremidade_flexivel", dict(perfil_viga="UPN 120", perfil_suporte="UPN 50",
                                                                 apoio="mesa_pilar"), ESF)
        msg = str(cm.exception)
        self.assertIn("chapa de topo", msg)
        self.assertIn("face do apoio 38 mm", msg)

    def test_rigida_u_sem_tipo(self):
        with self.assertRaises(P.ErroEntrada) as cm:
            P.verificar_ligacao("chapa_topo_estendida", dict(perfil_viga="UPN 120", perfil_pilar="W 200 x 26,6"),
                                {"V": 10, "M": 5})
        self.assertIn("ligação rígida com perfil U não suportada", str(cm.exception))
        # no modelo: viga U rígida no pilar → membro sem_tipo com motivo
        m = _modelo_viga_pilar("UPN 120")
        (mb,) = [x for x in lm.membros(m) if x["tipo"].startswith("viga_pilar")]
        self.assertTrue(mb["sem_tipo"])
        self.assertEqual(mb["motivo"], "ligação rígida com perfil U não suportada")


class TestGussetU(unittest.TestCase):
    def test_gusset_upn(self):
        perfis = dict(secao="L", dim_a=50, dim_b=50, t_perfil=5, apoio="pilar", theta=45, perfil_pilar="UPN 100")
        r = _sug_ver("gusset_contraventamento", perfis, {"N": 50})
        g = r["geometria"]
        self.assertAlmostEqual(math.hypot(*g["apoio_dir"]), 1.0)
        self.assertIn(g["lado"], (1, -1))
        # a chapa fica do lado `lado` · (normal × apoio_dir): o canto mais longe do apoio está desse lado
        n, a = g["chapas"][0]["normal"], g["apoio_dir"]
        lado = [n[1] * a[2] - n[2] * a[1], n[2] * a[0] - n[0] * a[2], n[0] * a[1] - n[1] * a[0]]
        ponta = max(g["chapas"][0]["contorno"], key=lambda p: p[0])     # (x, z) local
        self.assertGreater(g["lado"] * (lado[0] * ponta[0] + lado[2] * ponta[1]), 0)


def _modelo_viga_pilar(sec_viga):
    m = P.novo_modelo()
    for nome in (sec_viga, "W 200 x 26,6"):
        m.add(secoes.catalogo(nome))
    mat = "A-36"   # só entra na assinatura
    m.add(No("A", 0, 0, 0, apoio=(True,) * 6))
    m.add(No("B", 0, 0, 3))
    m.add(No("C", 4, 0, 3))
    m.add(Barra("P1", "A", "B", "W 200 x 26,6", mat, params={"papel": "pilar"}))
    m.add(Barra("V1", "B", "C", sec_viga, mat, params={"papel": "viga"}))
    return m


class TestNos(unittest.TestCase):
    def test_nos_proximos(self):
        """Cumeeira (emenda) em A e gusset da diagonal em B, a 186 mm de A na mesma tesoura: uma ligação só."""
        m = P.novo_modelo()
        m.add(secoes.catalogo("UPN 120"))
        m.add(secoes.catalogo(DIAG))
        mat = "A-36"   # só entra na assinatura
        m.add(No("E", -5, 0, 4.5, apoio=(True,) * 6))
        m.add(No("A", 0, 0, 5))
        m.add(No("B", 0.186, 0, 5 - 0.186 * 0.1))
        m.add(No("D", 5, 0, 4.5, apoio=(True,) * 6))
        m.add(No("F", 0.186, 4, 5 - 0.186 * 0.1))
        m.add(Barra("R1", "E", "A", "UPN 120", mat, params={"papel": "viga"}))
        m.add(Barra("R2", "A", "B", "UPN 120", mat, params={"papel": "viga"}))
        m.add(Barra("R3", "B", "D", "UPN 120", mat, params={"papel": "viga"}))
        m.add(Barra("X1", "B", "F", DIAG, mat, params={"trelica": True, "papel": "diagonal"}))
        mot = {}
        mbs = lm.membros(m, mot)
        nos = {x["no"] for x in mbs if x["tipo"] in ("emenda", "gusset_contraventamento")}
        self.assertEqual(len(nos), 1, mbs)
        (outro,) = {"A", "B"} - nos
        self.assertIn("nós a <200 mm: ligação única", mot[outro])

    def test_barra_curta_exclui_interferencia(self):
        f = L.com_pontas("chapa_simples_alma", lambda d: L.verificar_ligacao("chapa_simples_alma", d, ESF))
        t = next(t for t in P.tipos_ligacao() if t["tipo"] == "chapa_simples_alma")
        d = dict(t["exemplo"]["dados"], contexto_pontas=dict(tipo_no="viga_pilar_mesa", comprimento=0.05))
        r = f(d)
        self.assertEqual(r["motivo"], "barra curta demais para a ligação")
        self.assertIs(r["excluir_interferencia"], True)


# ---------------------------------------------------------------- PLATAFORMA (aceite; arquivo fora do git)

def _tipo_proposto(tipo, assin):
    """Espelho de Render3DMotor.TipoProposto (C#)."""
    rigida = "rigida" in assin
    return {"base": None, "viga_pilar_mesa": "chapa_topo_estendida" if rigida else "chapa_simples_alma",
            "viga_pilar_alma": "chapa_simples_alma", "viga_viga": "chapa_simples_alma",
            "gusset_contraventamento": "gusset_contraventamento"}.get(tipo)


def _perfis_gusset(pr, assin):
    """Espelho de Render3DMotor.PerfisGusset."""
    if pr.get("tipo") not in ("tubo_circular", "tubo_retangular", "L", "U"):
        return None
    papel = assin[3] if len(assin) > 3 else ""
    ang = int(assin[4]) if len(assin) > 4 and assin[4].isdigit() else 45
    apoio = ("base" if papel == "base" else "viga" if papel == "viga" else
             "viga-pilar" if papel.startswith("pilar termina") else "pilar")
    o = dict(secao=pr["tipo"], dim_a=pr["h"] * 1000, t_perfil=(pr.get("t") or pr["tw"]) * 1000, apoio=apoio,
             theta=max(5, min(85, 90 - ang if apoio == "viga" else ang)))
    if pr["tipo"] != "tubo_circular":
        o["dim_b"] = pr["b"] * 1000
    if pr["tipo"] == "U" and pr.get("tf"):
        o["tf_perfil"] = pr["tf"] * 1000
    return o


def plataforma_propostas(m):
    """(nós com proposta do motor, erros) pelo mesmo caminho do WPF (sugerir + verificar com geometria)."""
    nos, ok, erros = set(), set(), []
    for mb in lm.membros(m):
        tl = _tipo_proposto(mb["tipo"], mb["assinatura"].split("|"))
        if tl is None or mb.get("sem_tipo"):
            continue
        sec = m.barras[mb["barra"]].secao
        sup = m.barras[mb["apoio"]].secao if mb.get("apoio") else None
        if tl == "gusset_contraventamento":
            perfis = _perfis_gusset(m.secoes[sec].props, mb["assinatura"].split("|"))
            if perfis is None:
                continue
            if sup:
                perfis["perfil_pilar"] = sup
        else:
            perfis = dict(perfil_viga=sec, perfil_suporte=sup, perfil_pilar=sup)
        try:
            r = _sug_ver(tl, perfis, {"N": 50, "V": 50, "M": 30})
            g = r.get("geometria") or {}
            if g.get("chapas") or g.get("cantoneiras") or g.get("soldas"):
                nos.add(mb["no"])
                if r["ok"]:
                    ok.add(mb["no"])
        except P.ErroEntrada as e:
            erros.append(str(e))
    return nos, ok, erros


@unittest.skipUnless(os.path.exists(PLATAFORMA), "testes-aceite/PLATAFORMA.ed3 ausente")
class TestPlataforma(unittest.TestCase):
    def test_plataforma_u(self):
        from prossen_estrutural import importar_cype
        m = importar_cype.ler_ed3(PLATAFORMA)
        nos, ok, erros = plataforma_propostas(m)
        print(f"\nPLATAFORMA: {len(nos)} nós com proposta, {len(ok)} com proposta ok, {len(erros)} recusas")
        self.assertFalse([e for e in erros if "não é perfil I/H" in e or "só perfis I/H" in e])
        self.assertGreaterEqual(len(nos), 40, erros[:5])   # antes de G5: 24 nós (só "tem proposta")
        self.assertGreaterEqual(len(ok), MIN_OK)


if __name__ == "__main__":
    unittest.main()
