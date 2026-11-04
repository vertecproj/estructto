"""Revisão geral de 04/11/2026 (docs/MEMBROS.md, seção "Outras mudanças"): ligação leve não sai gigante, cantoneira
fletida verificada, B1 em 1ª ordem, H/300 em galpão sem pavimentos, Realista com esforços reais."""
import logging
import math
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes, materiais
from prossen_estrutural.aco import nbr8800
from prossen_estrutural.modelo import Barra, CargaNodal, CasoCarga, No


ACO = "A-572 GR.50"
ENG, LIV = (True,) * 6, (False,) * 6


class TestLigacaoLeve(unittest.TestCase):
    def test_gusset_de_diagonal_leve_nao_vira_o_maior_da_lista(self):
        d = dict(secao="L", dim_a=38.1, dim_b=38.1, t_perfil=3.18, apoio="viga-pilar", theta=45)
        sug = P.sugerir_ligacao("gusset_contraventamento", d, {"N": 10})
        self.assertLessEqual(sug["t_gusset"], 8.0)            # antes: 25 mm, 9 parafusos, gusset 617x639
        r = P.verificar_ligacao("gusset_contraventamento", dict(d, **{k: v for k, v in sug.items() if v is not None
                                                                      and k not in ("tipo_ligacao", "motivo")}), {"N": 10})
        g = r["geometria"]["chapas"][0]
        self.assertLess(max(g.get("largura", 0), g.get("altura", 0), g.get("comprimento", 0)) * 1000 or 0, 400)

    def test_barra_usa_forca_real_e_ligacao_o_minimo(self):
        d = dict(secao="L", dim_a=38.1, dim_b=38.1, t_perfil=3.18, apoio="viga-pilar", theta=45)
        d.update({k: v for k, v in P.sugerir_ligacao("gusset_contraventamento", d, {"N": 10}).items()
                  if v is not None and k not in ("tipo_ligacao", "motivo")})
        r = P.verificar_ligacao("gusset_contraventamento", d, {"N": 10})
        esc = next(v for v in r["verificacoes"] if v["nome"].startswith("barra: escoamento"))
        self.assertAlmostEqual(esc["solicitante"], 10.0)
        self.assertTrue(any("45 kN" in a for a in r["avisos"]))


class TestCantoneiraFletida(unittest.TestCase):
    def test_flexao_cantoneira_aisc_f10(self):
        sec = secoes.secao_de_spec({"perfil": 'L 2.1/2" x 1/4"'})
        mat = materiais.aco(ACO)
        r = nbr8800.flexao_x(sec, mat, 0.0)
        My = sec.props["Wx"] * mat.fy
        self.assertAlmostEqual(r["MRd"], 1.5 * My / 1.10, places=6)   # b/t = 10 < λp: escoamento governa
        r4 = nbr8800.flexao_x(sec, mat, 4.27)
        self.assertLess(r4["MRd"], r["MRd"])                           # FLT de abas iguais
        self.assertIn("F10", r4["item"])


def _pilar_fletido(pdelta):
    m = P.novo_modelo()
    P.adicionar(m, "material", {"aco": ACO})
    P.adicionar(m, "secao", {"perfil": "W200x46.1"})
    m.add(No("A", 0, 0, 0, ENG))
    m.add(No("T", 0, 0, 4, LIV))
    m.add(Barra("P", "A", "T", "W200x46.1", ACO, params={"papel": "pilar"}))
    m.add(CasoCarga("G1", "G"))
    m.add(CasoCarga("V1", "V"))
    m.peso_proprio = None
    m.add(CargaNodal("G1", "T", (0, 0, -300.0, 0, 0, 0)))
    m.add(CargaNodal("V1", "T", (5.0, 0, 0, 0, 0, 0)))
    P.configurar(m, {"pdelta": pdelta})
    return m


class TestSegundaOrdem(unittest.TestCase):
    def test_b1_em_primeira_ordem(self):
        c = P.calcular(_pilar_fletido(False))
        d = P.verificacao_barra(c, "P")["detalhes"]
        self.assertIn("B1", d)
        b1 = d["B1"]["Mx"]
        sec, mat = c.modelo.secoes["W200x46.1"], c.modelo.materiais[ACO]
        Ne = math.pi ** 2 * mat.E * sec.Iy / 4.0 ** 2
        self.assertAlmostEqual(b1["Ne_kN"], Ne, delta=1e-6 * Ne)
        self.assertGreater(b1["B1"], 1.0)
        self.assertNotIn("B1", P.verificacao_barra(P.calcular(_pilar_fletido(True)), "P")["detalhes"])

    def test_topo_de_pilar_h300_sem_pavimentos(self):
        c = P.calcular(_pilar_fletido(False))
        h = [d for d in c.deslocamentos["horizontais"] if d["tipo"] == "topo_pilar"]
        self.assertTrue(h)
        self.assertAlmostEqual(h[0]["limite_m"], 4.0 / 300)
        self.assertIn("segunda_ordem_sem_pavimentos", [a["codigo"] for a in c.avisos])


class TestRealistaEsforcosReais(unittest.TestCase):
    def test_geometrias_com_calculo_usam_envoltoria(self):
        m = P.carregar("exemplos/galpao_aco.json")
        c = P.calcular(m)
        P.ligacoes_identificar(m)
        g = next(g for g in m.ligacoes["grupos"].values() if g["tipo"] == "viga_pilar_mesa")
        it = dict(tipo="chapa_topo_estendida", dados={}, perfis=dict(perfil_viga="HP250x62", perfil_pilar="W360x51"),
                  membros=g["membros"])
        r = P.geometrias_ligacoes([it], m, c)[0]
        self.assertFalse(r["esforcos_tipicos"], r.get("erro"))
        self.assertGreater(r["esforcos"]["M"], 30.0)    # M real da tesoura (~76 kN·m), não os 30 típicos
        r0 = P.geometrias_ligacoes([dict(it, membros=None)], m, c)[0]
        self.assertTrue(r0["esforcos_tipicos"])


if __name__ == "__main__":
    unittest.main()


class TestPainelInclinadoCype(unittest.TestCase):
    def test_carga_de_escada_desce_na_area_real(self):
        """Painel inclinado (escada) com sobrecarga: gravitacional na área real (ΣFz = −q·A), como o CYPE; antes a
        carga ia pela normal 'positiva' e subia (PLATAFORMA_REV4: −109 kN contra −145 kN do CYPE)."""
        from prossen_estrutural import importar_cype as ic
        m = P.novo_modelo()
        P.adicionar(m, "material", {"aco": ACO})
        P.adicionar(m, "secao", {"perfil": "W150x13"})
        m.add(CasoCarga("Q1", "Q"))
        c, s = math.cos(math.radians(35)), math.sin(math.radians(35))
        L = 3.0
        for nid, (x, y, z) in {"A": (0, 0, 0), "B": (0, 1, 0), "C": (L * c, 0, L * s), "D": (L * c, 1, L * s)}.items():
            m.add(No(nid, x, y, z, ENG))
        m.add(Barra("AC", "A", "C", "W150x13", ACO))
        m.add(Barra("BD", "B", "D", "W150x13", ACO))
        ux, uy = (c, 0.0, s), (0.0, 1.0, 0.0)   # plano da escada; faixas na direção de ux (vão entre AC e BD: y)
        poly = [(0.0, 0.0), (L, 0.0), (L, 1.0), (0.0, 1.0)]
        painel = [None, 0.0, 0.0, 0.0, *ux, *uy, poly, 0.0, 1.0, [(None, "ref", 0, 2.0 / ic.T_KN)], [], 0]
        ic._paineis([painel], m, lambda ref, ctx: "Q1", [])
        fz = sum((cd.q[2] + (cd.q2 or cd.q)[2]) / 2 * L for cd in m.cargas_distribuidas) + \
            sum(cp.F[2] for cp in m.cargas_pontuais)
        self.assertAlmostEqual(fz, -2.0 * L * 1.0, delta=1e-6)
        self.assertTrue(all(abs(cd.q[0]) < 1e-12 for cd in m.cargas_distribuidas))


class TestImperfeicoesNocionais(unittest.TestCase):
    """NBR 8800 4.9.7.1.1, ligado por padrão (decisão do usuário, 04/11/2026): ELU só gravitacional ganha ±X/±Y com
    0,3 % das cargas gravitacionais de cálculo; nada fica no modelo depois do cálculo."""

    def _portico(self):
        m = P.novo_modelo()
        P.adicionar(m, "material", {"aco": ACO})
        P.adicionar(m, "secao", {"perfil": "W200x46.1"})
        for nid, x, z in (("A", 0, 0), ("B", 6, 0), ("C", 0, 4), ("D", 6, 4)):
            m.add(No(nid, x, 0, z, ENG if z == 0 else LIV))
        for bid, a, b in (("P1", "A", "C"), ("P2", "B", "D"), ("V", "C", "D")):
            m.add(Barra(bid, a, b, "W200x46.1", ACO))
        m.add(CasoCarga("G1", "G"))
        m.add(CasoCarga("Q1", "Q"))
        m.add(CargaNodal("G1", "C", (0, 0, -100.0, 0, 0, 0)))
        m.add(CargaNodal("Q1", "D", (0, 0, -50.0, 0, 0, 0)))
        return m

    def test_equilibrio_e_modelo_intacto(self):
        m = self._portico()
        casos, nodais = set(m.casos), len(m.cargas_nodais)
        c = P.calcular(m)
        self.assertEqual((set(m.casos), len(m.cargas_nodais)), (casos, nodais))
        nomes = [x["nome"] for x in c.combinacoes if x.get("imperfeicao") == "+X"]
        self.assertTrue(nomes)
        comb = next(x for x in c.combinacoes if x["nome"] == nomes[0])
        f = comb["fatores"]
        pp = m.peso_proprio
        peso = sum(m.materiais[b.material].peso * m.secoes[b.secao].A * P._comprimento(m, b) for b in m.barras.values())
        grav = f.get("G1", 0) * 100 + f.get("Q1", 0) * 50 + (f.get(pp, 0) * peso if pp else 0)
        rx = sum(r[0] for r in c.resultados[nomes[0]].reacoes.values())
        self.assertAlmostEqual(abs(rx), 0.003 * grav, delta=1e-6 * grav)
        self.assertIn("imperfeicoes_nocionais", [a["codigo"] for a in c.avisos])

    def test_desligado(self):
        m = self._portico()
        P.configurar(m, {"imperfeicoes": False})
        c = P.calcular(m)
        self.assertFalse(any(x.get("imperfeicao") for x in c.combinacoes))
