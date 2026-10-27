"""v0.6 A3: tubo redondo soldado em tubo redondo (aco/ligacoes_tubos.py) e a classificação tubo–tubo no agrupamento."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco.ligacoes_tubos import sela
from prossen_estrutural.modelo import Barra, No, Secao

DADOS = dict(d_ramal=114.3, t_ramal=6.0, D_receptor=168.3, t_receptor=6.4, theta=45, b_solda=5)


class TestTuboTubo(unittest.TestCase):
    def test_registro_e_geometria(self):
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=100, V=0))
        g = r["geometria"]
        self.assertEqual((g["chapas"], g["parafusos"]), ([], []))
        p = g["pontas"][0]
        self.assertEqual([c["tipo"] for c in p["recortes"]], ["boca_peixe"])
        self.assertAlmostEqual(p["recortes"][0]["raio"], 0.16830 / 2)
        # solda = curva da sela: todo ponto na superfície do receptor (eixo (cos45, 0, sen45) pelo nó)
        a = [math.cos(math.pi / 4), 0, math.sin(math.pi / 4)]
        for q in g["soldas"][0]["pontos"]:
            q = [x / 1000 for x in q]
            t = sum(x * y for x, y in zip(q, a))
            self.assertAlmostEqual(math.dist(q, [t * x for x in a]), 0.16830 / 2, places=6)

    def test_solda_a_mao(self):
        # perímetro da sela > π·d (sela alongada a 45°); q = N/Lw; FwRd E70 perna 5: 0,6·0,707·5·485/1,35 = 762 N/mm
        Lw = sum(math.dist(a, b) for a, b in zip(sela(0.05715, 0.08415, math.pi / 4),
                                                  sela(0.05715, 0.08415, math.pi / 4)[1:] + sela(0.05715, 0.08415, math.pi / 4)[:1]))
        self.assertGreater(Lw, math.pi * 0.1143)
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=0))
        v = next(x for x in r["verificacoes"] if x["nome"].startswith("solda ramal"))
        self.assertAlmostEqual(v["solicitante"], 200 / Lw, places=3)
        self.assertAlmostEqual(v["resistente"], 0.6 * 0.707 * 0.005 * 485e3 / 1.35, places=1)
        self.assertTrue(any("NBR 16239" in a for a in r["avisos"]))

    def test_momento_entra_na_solda(self):
        # à mão: projeção da sela = círculo r = 57,15 mm ⇒ W = π r² = 1,0261e-2 m²; M = 2 kN·m ⇒ q = 194,9 kN/m
        from prossen_estrutural.aco.ligacoes_tubos import modulo_sela
        W = modulo_sela(sela(0.05715, 0.08415, math.pi / 4))
        self.assertAlmostEqual(W, math.pi * 0.05715 ** 2, delta=0.005 * W)   # polígono de 72 lados
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=0, V=0, M=2))
        v = next(x for x in r["verificacoes"] if x["nome"].startswith("solda ramal"))
        self.assertAlmostEqual(v["solicitante"], 2 / (math.pi * 0.05715 ** 2), delta=0.5)
        # com N e V: q = hypot(N/Lw + M/W, V/Lw) > só N
        r2 = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=50, M=2))
        r0 = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=50))
        q = lambda rr: next(x for x in rr["verificacoes"] if x["nome"].startswith("solda ramal"))["solicitante"]
        self.assertGreater(q(r2), q(r0) + 190)

    def test_biaxial_e_torcao(self):
        q = lambda esf: next(x for x in L.registro("tubo_tubo_soldado")["verificar"](DADOS, esf)["verificacoes"]
                             if x["nome"].startswith("solda ramal"))["solicitante"]
        self.assertAlmostEqual(q(dict(M=1.41421356, Mz=1.41421356)), q(dict(M=2)), places=3)
        self.assertAlmostEqual(q(dict(V=30, Vz=40)), q(dict(V=50)), places=6)
        # T = 1 kN·m só: q = T/(Lw·r)
        Lw = sum(math.dist(a, b) for a, b in zip(sela(0.05715, 0.08415, math.pi / 4),
                                                  sela(0.05715, 0.08415, math.pi / 4)[1:] + sela(0.05715, 0.08415, math.pi / 4)[:1]))
        self.assertAlmostEqual(q(dict(T=1)), 1 / (Lw * 0.05715), places=3)

    def test_momento_sem_W_reprova(self):
        from unittest import mock
        with mock.patch("prossen_estrutural.aco.ligacoes_tubos.modulo_sela", return_value=0.0):
            r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=10, M=1))
        self.assertFalse(r["ok"])
        self.assertTrue(any("W_sela" in x["nome"] or "módulo resistente" in a for x in r["verificacoes"] for a in r["avisos"]))

    def test_beta_maior_que_1_reprova(self):
        r = L.registro("tubo_tubo_soldado")["verificar"](dict(DADOS, d_ramal=200), dict(N=10))
        self.assertFalse(r["ok"])
        # W7: "ramal: β" (era "geometria: β"): o projetista agrupa pelo nome e "geometria" caía no chip "Chapa", que
        # esta ligação não tem
        self.assertFalse(next(x for x in r["verificacoes"] if x["nome"].startswith("ramal: β"))["ok"])

    def test_classificacao_tubo_tubo(self):
        m = P.novo_modelo()
        for s in (secoes.secao_tubo_circular(0.1683, 0.0064, "TC168"), secoes.secao_tubo_circular(0.1143, 0.006, "TC114")):
            m.add(Secao(s.nome, s.A, s.Iy, s.Iz, s.J, s.props))
        m.add(No("N1", 0, 0, 0, apoio=(True,) * 6)); m.add(No("N2", 0, 0, 3)); m.add(No("N3", 0, 0, 6))
        m.add(No("N4", 2, 0, 1, apoio=(True,) * 6))
        m.add(Barra("P1", "N1", "N2", "TC168", "A-572 GR.50")); m.add(Barra("P2", "N2", "N3", "TC168", "A-572 GR.50"))
        m.add(Barra("D1", "N2", "N4", "TC114", "A-572 GR.50"))
        tipos = {(x["no"], x["barra"]): x["tipo"] for x in lm.membros(m)}
        self.assertEqual(tipos[("N2", "D1")], "tubo_tubo_soldado")


class TestNoTubular(unittest.TestCase):
    """W7: nó T/Y pela NBR 16239 (tabelas 2 e 4; = EN 1993-1-8:2005 tab. 7.2/7.5 com γM5 = 1). Conta à mão para o padrão
    de fábrica: ramal TC 114,3x5,6 a 45° no receptor TC 168,3x6,4, A-572 GR.50 (fy 345 MPa), γa1 = 1,10.
    β = 114,3/168,3 = 0,67914; γ = 168,3/12,8 = 13,1484 (γ^0,2 = 1,6741; √γ = 3,6261); fy0·t0² = 345000·0,0064² = 14,1312 kN;
    sen 45° = 0,70711.
    Modo A: N = 1,6741·14,1312·(3,08 + 15,62·0,46124)/(1,1·0,70711) = 1,6741·14,1312·10,2845/0,77782 = 312,8 kN.
    Modo D: N = 0,66·345000·0,0064/1,1·π·0,1143·(1 + 0,70711)/(2·0,5) = 1324,8·0,35908·1,70711 = 812,1 kN.
    Mip,A = 5,34·14,1312·0,1143·3,6261·0,67914/0,77782 = 27,31; Mip,D = 1324,8·0,1143²·(1 + 3·0,70711)/(4·0,5) = 27,01 kN·m.
    Mop,A = 2,97·14,1312·0,1143/(0,77782·(1 − 0,81·0,67914)) = 4,7971/0,34994 = 13,71 kN·m.
    N = 100 kN, M = 5 kN·m (no plano): 100/312,8 + (5/27,01)² = 0,3197 + 0,0343 = 0,354.
    Ramal: A = π/4·(114,3² − 103,1²) = 1912,4 mm², NRd = 1912,4e-6·345000/1,1 = 599,8 kN (n = 0,167 < 0,2);
    Z = (114,3³ − 103,1³)/6 = 66227 mm³, D/t = 20,4 ≤ 0,07E/fy = 40,6 ⇒ MRd = 66227e-9·345000/1,1 = 20,77 kN·m;
    0,167/2 + 5/20,77 = 0,324. Solda: garganta ≥ 1,1·5,6 = 6,16 mm (fy 345 ≤ 350) ⇒ perna 6,16/0,707 = 8,71 → 10 mm."""

    E = dict(N=100.0, V=0.0, Vz=0.0, M=5.0, Mz=0.0, T=0.0)

    def _r(self, dados=None, esf=None):
        return L.registro("tubo_tubo_soldado")["verificar"](dict(dados or {}), esf or self.E)

    def _v(self, r, inicio):
        return next(x for x in r["verificacoes"] if x["nome"].startswith(inicio))

    def test_formulas_a_mao(self):
        from prossen_estrutural.aco.ligacoes_tubos import no_tubular
        no = no_tubular(0.1683, 0.0064, 0.1143, math.pi / 4, 345000.0)
        self.assertAlmostEqual(no["N_A"], 312.8, delta=0.1)
        self.assertAlmostEqual(no["N_D"], 812.1, delta=0.1)
        self.assertAlmostEqual(no["Mip_A"], 27.31, delta=0.01)
        self.assertAlmostEqual(no["Mip_D"], 27.01, delta=0.01)
        self.assertAlmostEqual(no["Mop_A"], 13.71, delta=0.01)
        # NBR 16239 (coef. × 1,1 e γa1 = 1,1) = EN 1993-1-8:2005 tab. 7.2 com γM5 = 1,0
        b, g = 0.1143 / 0.1683, 0.1683 / 0.0128
        en = g ** 0.2 * 345000 * 0.0064 ** 2 * (2.8 + 14.2 * b * b) / math.sin(math.pi / 4)
        self.assertAlmostEqual(no["N_A"], en, places=6)
        # punção só com d1 ≤ d0 − 2t0
        self.assertNotIn("N_D", no_tubular(0.1683, 0.0064, 0.160, math.pi / 4, 345000.0))

    def test_kp_receptor_comprimido(self):
        from prossen_estrutural.aco.ligacoes_tubos import kp, props_tubo
        self.assertAlmostEqual(kp(-0.5), 1 - 0.15 - 0.075)
        self.assertEqual(kp(0.3), 1.0)
        # N0 = −500 kN no TC 168,3x6,4: σ = −500/A0; np = σ/fy0; modo A cai por kp
        A0 = props_tubo(0.1683, 0.0064)["A"]
        np_ = -500 / A0 / 345000
        r = self._r(dict(N_receptor=-500))
        self.assertAlmostEqual(r["valores"]["kp"], kp(np_), places=9)
        self.assertAlmostEqual(self._v(r, "receptor: plastificação")["resistente"], 312.8 * kp(np_), delta=0.1)

    def test_padrao_de_fabrica(self):
        r = self._r()
        self.assertTrue(r["ok"])
        self.assertAlmostEqual(self._v(r, "receptor: plastificação")["resistente"], 312.8, delta=0.1)
        self.assertAlmostEqual(self._v(r, "receptor: interação")["solicitante"], 0.354, delta=0.001)
        self.assertAlmostEqual(self._v(r, "ramal: seção")["solicitante"], 0.324, delta=0.001)
        self.assertAlmostEqual(self._v(r, "solda: garganta")["solicitante"], 6.16, places=6)
        self.assertEqual(self._v(r, "solda: garganta")["item"], "NBR 16239 6.1.5.7")
        resist = [x["taxa"] for x in r["verificacoes"] if x["unidade"] in ("kN", "kN/m") or x["nome"].startswith(("receptor: inter", "ramal"))]
        self.assertTrue(0.3 < max(resist) < 0.7, resist)   # folga razoável nas resistências
        self.assertFalse(any("dimensão máxima" in x["nome"] for x in r["verificacoes"]))   # 6.2.6.2.2 não vale (junta em T)
        # o padrão antigo (Fz 100 kN, My 60 kN·m) passa do momento plástico do ramal: reprova pela seção do ramal
        self.assertFalse(self._v(self._r(esf=dict(V=100, M=60)), "ramal: seção")["ok"])

    def test_validade(self):
        r = self._r(dict(theta=25))
        self.assertFalse(self._v(r, "ramal: ângulo")["ok"])
        r = self._r(dict(perfil_viga="TC 33,4x3,2"))   # β = 0,198 < 0,2
        v = self._v(r, "ramal: β")
        self.assertFalse(v["ok"])
        self.assertEqual((v["solicitante"], v["item"]), (0.2, "NBR 16239 6.2.1"))
        with self.assertRaises(ValueError):
            self._r(dict(perfil_viga="W310x38.7"))   # não é tubo circular

    def test_exemplo_campos_e_sugerir(self):
        t = next(x for x in P.tipos_ligacao() if x["tipo"] == "tubo_tubo_soldado")
        c = {x["nome"]: x for x in t["campos"]}
        self.assertIn("TC 168,3x6,4", c["perfil_suporte"]["perfis"])
        self.assertTrue(all(secoes.catalogo(n).props["tipo"] == "tubo_circular" for n in c["perfil_viga"]["perfis"][::40]))
        self.assertEqual((c["theta"]["secao"], c["N_receptor"]["secao"]), ("Tubos", "Tubos"))
        ex = t["exemplo"]
        self.assertEqual((ex["dados"]["perfil_viga"], ex["esforcos"]["M"], ex["dados"]["b_solda"]), ("TC 114,3x5,6", 5.0, 10.0))
        self.assertTrue(P.verificar_ligacao("tubo_tubo_soldado", ex["dados"], ex["esforcos"])["ok"])
        self.assertEqual(P.sugerir_ligacao("tubo_tubo_soldado", dict(perfil_viga="TC 114,3x5,6"), self.E)["b_solda"], 10)
        o = P.otimizar_ligacao("tubo_tubo_soldado", dict(ex["dados"], b_solda=4), ex["esforcos"])
        self.assertTrue(o["ok"])
        self.assertEqual(o["dados"]["b_solda"], 10)

    def test_contrato_tubos(self):
        r = self._r()
        g = r["geometria"]
        tb = {t["nome"]: t for t in g["tubos"]}
        self.assertEqual(set(tb), {"ramal", "receptor"})
        ra, rc = tb["ramal"], tb["receptor"]
        self.assertEqual((ra["grupo"], ra["campo"], rc["grupo"], rc["campo"]), ("viga", "perfil_viga", "suporte", "perfil_suporte"))
        self.assertEqual((ra["d"], ra["t"], rc["d"], rc["t"], rc["corte"]), (114.3, 5.6, 168.3, 6.4, None))
        a = [math.cos(math.pi / 4), 0, math.sin(math.pi / 4)]
        self.assertEqual((ra["corte"]["eixo"], ra["corte"]["r"]), (rc["eixo"], 84.15))
        # receptor centrado no nó; ramal começa no ponto mais baixo da sela (61,9 mm a 45°; √(R² − r²) = 61,8 só a 90°)
        self.assertAlmostEqual(math.dist([x + rc["comprimento"] / 2 * e for x, e in zip(rc["origem"], rc["eixo"])], [0, 0, 0]), 0, places=2)
        self.assertAlmostEqual(ra["origem"][0], min(p[0] for p in g["soldas"][0]["pontos"]), places=2)
        self.assertAlmostEqual(ra["origem"][0], 61.9, delta=0.1)
        dist_eixo = lambda p, o, e: math.dist(p, [oi + sum((pj - oj) * ej for pj, oj, ej in zip(p, o, e)) * ei for oi, ei in zip(o, e)])
        for p in g["soldas"][0]["pontos"]:
            self.assertAlmostEqual(dist_eixo(p, rc["origem"], rc["eixo"]), 84.15, places=4)   # na superfície do receptor
            self.assertAlmostEqual(math.hypot(p[1], p[2]), 57.15, places=4)                   # na superfície do ramal
            self.assertTrue(ra["origem"][0] - 1e-6 <= p[0] <= ra["origem"][0] + ra["comprimento"])   # dentro do trecho
            self.assertGreaterEqual(dist_eixo(p, ra["corte"]["origem"], ra["corte"]["eixo"]), ra["corte"]["r"] - 1e-3)
            t = sum(x * y for x, y in zip(p, a))
            self.assertLess(abs(t), rc["comprimento"] / 2)   # o receptor cobre a sela

    def test_perfil_tubo_manda_tubos(self):
        g = L.registro("perfil_tubo_soldado")["verificar"](dict(PT), dict(N=10))["geometria"]
        self.assertEqual([t["nome"] for t in g["tubos"]], ["receptor"])   # I: só o receptor é tubo
        g = L.registro("perfil_tubo_soldado")["verificar"](dict(PT, forma_ramal="tubo_circular", h_ramal=168.3, tw_ramal=6.4),
                                                           dict(N=10))["geometria"]
        self.assertEqual([t["nome"] for t in g["tubos"]], ["ramal", "receptor"])
        self.assertEqual(g["tubos"][0]["corte"]["r"], 161.9)
        g = L.registro("perfil_tubo_soldado")["verificar"](dict(PT, forma_receptor="tubo_retangular", D_receptor=200),
                                                           dict(N=10))["geometria"]
        self.assertEqual(g["tubos"], [])   # contrato `tubos` só tem tubo circular


PT = dict(forma_ramal="I", h_ramal=310, b_ramal=165, tw_ramal=5.8, tf_ramal=9.7, forma_receptor="tubo_circular",
          D_receptor=323.8, t_receptor=9.5, theta=90, b_solda=5)


def _ver(dados, esf):
    return L.registro("perfil_tubo_soldado")["verificar"](dict(PT, **dados), esf)


class TestPerfilTubo(unittest.TestCase):
    """Perfil (I, U, tubo, retangular) encostado num tubo: filete no contorno + recorte (docs/v3/LIGACOES-TUBO-PERFIL.md)."""

    def test_conta_a_mao_I_em_tubo_redondo(self):
        """I 310×165×5,8×9,7 em Ø323,8, 90°. Linha de solda = contorno (12 lados): Lw = 2·165 + 2·(165−5,8) + 4·9,7 +
        2·(310−2·9,7) = 1268,4 mm. I_forte ≈ 2·0,165·0,155² + 0,3184·0,1453² + 4·0,0097·(…) + 2·0,2906³/12;
        N = 50, M = 30, V = 100: qn = 50/1,2684 + 30·0,155/I; qv = 100/Lv_h, Lv_h = 4·9,7 + 2·290,6 = 620 mm.
        Resistência E70, b = 5: 0,6·0,707·5·485/1,35 = 762,0 kN/m (NBR 8800 6.2.5 tabela 8)."""
        r = _ver({}, dict(N=50, V=100, M=30))
        self.assertAlmostEqual(r["valores"]["Lw_mm"], 1268.4, places=6)
        I = (2 * 0.165 * 0.155 ** 2 + 2 * 0.1592 * 0.1453 ** 2
             + 4 * 0.0097 * (0.155 ** 2 + 0.155 * 0.1453 + 0.1453 ** 2) / 3 + 2 * 0.2906 ** 3 / 12)
        q = math.hypot(50 / 1.2684 + 30 * 0.155 / I, 100 / 0.620)
        v = next(x for x in r["verificacoes"] if x["nome"].startswith("solda perfil–tubo"))
        self.assertAlmostEqual(v["solicitante"], q, places=6)
        self.assertAlmostEqual(v["resistente"], 0.6 * 0.707 * 5 * 485 / 1.35, places=3)
        self.assertTrue(r["ok"])
        # W8: a parede do tubo passou a ser verificada (EN 1993-1-8 tabela 7.4); antes o teste conferia o aviso
        # "parede do tubo … (NBR 16239) não verificados", que saiu
        self.assertTrue(any(v["nome"].startswith("receptor: plastificação") for v in r["verificacoes"]))
        g = r["geometria"]
        p = g["pontas"][0]
        # fundo da boca de peixe: canto da mesa (±82,5) no cilindro R = 161,9 → √(161,9² − 82,5²)
        self.assertAlmostEqual(p["plano"]["ponto"][0], math.sqrt(0.1619 ** 2 - 0.0825 ** 2), places=9)
        self.assertEqual([c["tipo"] for c in p["recortes"]], ["boca_peixe"])
        pts = g["soldas"][0]["pontos"]
        # W8: os 12 lados são subdivididos em trechos ≤ 10 mm (antes só os 12 cantos + fecho: a corda de cada mesa
        # ficava dentro do tubo e o cordão sumia no 3D). Mesas 165 → 17, (165 − 5,8)/2 = 79,6 → 8, tf 9,7 → 1, alma 290,6 → 30
        self.assertEqual(len(pts), 2 * 17 + 4 * 8 + 4 * 1 + 2 * 30 + 1)
        for x, y, z in pts:   # todos no cilindro (eixo z, raio 161,9 mm)
            self.assertAlmostEqual(math.hypot(x, y), 161.9, places=6)

    def test_tubo_retangular_corte_na_face(self):
        r = _ver(dict(forma_receptor="tubo_retangular", D_receptor=200, B_receptor=200, theta=60), dict(N=50))
        p = r["geometria"]["pontas"][0]
        self.assertEqual(p["recortes"], [])
        self.assertAlmostEqual(p["plano"]["ponto"][0], 0.1 / math.cos(math.radians(30)), places=9)
        self.assertAlmostEqual(p["plano"]["normal"][0], -math.cos(math.radians(30)), places=9)

    def test_beta_maior_que_1_reprova(self):
        r = _ver(dict(b_ramal=400), dict(N=10))
        self.assertFalse(r["ok"])
        self.assertEqual(r["geometria"]["soldas"], [])

    def test_giro_90_tubo_no_plano_da_largura(self):
        """Giro 90°: o tubo fica no plano da largura b; a mesa (165) cabe, β = h/D = 310/323,8 (altura atravessa o tubo)."""
        r = _ver(dict(giro=90), dict(N=10))
        self.assertAlmostEqual(r["valores"]["beta"], 310 / 323.8, places=9)

    def _modelo(self, tubo):
        m = P.novo_modelo()   # material e casos do gerador; nós e barras à mão
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50", "secoes": {"pilar": "W310x38.7", "viga": "W310x38.7"},
                              "vaos_x": [6], "alturas": [3]})
        m.nos.clear()
        m.barras.clear()
        for s in (tubo, secoes.secao_I(0.31, 0.165, 0.0058, 0.0097, nome="I310")):
            m.add(Secao(s.nome, s.A, s.Iy, s.Iz, s.J, s.props))
        m.add(No("N1", 0, 0, 0, apoio=(True,) * 6)); m.add(No("N2", 0, 0, 3)); m.add(No("N3", 0, 0, 6))
        m.add(No("N4", 4, 0, 3)); m.add(No("N5", 4, 0, 0, apoio=(True,) * 6))
        m.add(Barra("P1", "N1", "N2", tubo.nome, "A-572 GR.50")); m.add(Barra("P2", "N2", "N3", tubo.nome, "A-572 GR.50"))
        m.add(Barra("V1", "N2", "N4", "I310", "A-572 GR.50"))    # viga no pilar-tubo
        m.add(Barra("D1", "N5", "N2", "I310", "A-572 GR.50"))    # diagonal 53° com o pilar-tubo
        m.add(Barra("P3", "N5", "N4", "I310", "A-572 GR.50"))
        from prossen_estrutural.modelo import CargaNodal
        m.add(CargaNodal("Q1", "N3", (10.0, 0.0, -50.0, 0.0, 0.0, 0.0)))
        return m

    def test_modelo_viga_e_diagonal_em_pilar_tubo(self):
        for tubo in (secoes.secao_tubo_circular(0.3238, 0.0095, "TC324"),
                     secoes.secao_tubo_retangular(0.25, 0.25, 0.0095, nome="TR250")):
            m = self._modelo(tubo)
            tipos = {(x["no"], x["barra"]): x["tipo"] for x in lm.membros(m)}
            self.assertEqual(tipos[("N2", "V1")], "perfil_tubo_soldado")
            self.assertEqual(tipos[("N2", "D1")], "perfil_tubo_soldado")
            P.ligacoes_identificar(m)
            gs = [g for g in m.ligacoes["grupos"].values() if g["tipo"] == "perfil_tubo_soldado"]
            self.assertEqual(len(gs), 2)   # viga 90° e diagonal 53°: assinaturas diferentes
            dd = {g["membros"][0]["barra"]: g["projeto"]["dados"] for g in gs}
            self.assertEqual((dd["V1"]["theta"], dd["D1"]["theta"]), (90, 53))
            self.assertEqual((dd["V1"]["h_ramal"], dd["V1"]["forma_receptor"]), (310.0, tubo.props["tipo"]))
            self.assertAlmostEqual(dd["V1"]["giro"], 0.0)
            c = P.calcular(m)
            for gid, g in m.ligacoes["grupos"].items():
                if g["tipo"] == "perfil_tubo_soldado":
                    r = P.ligacoes_dimensionar(m, c, gid)
                    self.assertEqual(r["erros"], [], r["erros"])
                    self.assertGreater(r["taxa_max"], 0.0)


if __name__ == "__main__":
    unittest.main()
