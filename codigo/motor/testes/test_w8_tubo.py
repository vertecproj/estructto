"""W8 (docs/v3/W8-TUBO.md): parede do tubo receptor no perfil–tubo soldado (EN 1993-1-8:2005 tabelas 7.3 e 7.4), perna
de solda acima da mínima nos padrões (perfil–tubo e solda direta) e o perfil desenhado com a ponta na sela.

Contas à mão com o padrão: I 310 × 165 × 5,8 × 9,7 num tubo Ø323,8 × 9,5, A-572 GR.50 (fy = 345 MPa), θ = 90°, giro 0
(alma no plano do eixo do tubo, mesas atravessadas = tabela 7.4, 1ª linha), γM5 = 1,0:
  fy0·t0² = 345 000 · 0,0095² = 31,136 kN; β = 165/323,8 = 0,5096; η = 310/323,8 = 0,9574
  N1,Rd = 31,136 · (4 + 20·0,5096²) · (1 + 0,25·0,9574) = 31,136 · 9,1933 · 1,2393 = 354,76 kN
  Mip,Rd = h1·N1,Rd/(1 + 0,25η) = 0,31 · 31,136 · 9,1933 = 88,74 kN·m; Mop,Rd = 0,5·0,165·354,76 = 29,27 kN·m
  V = 60 kN, M = 40 kN·m: interação (40/88,74)² = 0,203
  punção: Iy = (0,165·0,31³ − 0,1592·0,2906³)/12 = 8,405e-5 m⁴, Wy = Iy/0,155, σ = 40/Wy = 73,8 MPa;
          σ·tf = 73,8·9,7 = 715,5 kN/m ≤ 2·t0·0,66·fy0/γa1 = 2·9,5·0,66·345/1,1 = 3933 kN/m (0,18)
  perna: mínima 5 mm (tabela 10, menor entre a mesa 9,7 e a parede 9,5) → sugerir escolhe 6 (taxa 0,83)."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_tubos as T

PT = dict(forma_ramal="I", h_ramal=310, b_ramal=165, tw_ramal=5.8, tf_ramal=9.7, forma_receptor="tubo_circular",
          D_receptor=323.8, t_receptor=9.5, theta=90, b_solda=6)
F0 = 345000 * 0.0095 ** 2


def _ver(dados=None, esf=None):
    return L.registro("perfil_tubo_soldado")["verificar"](dict(PT, **(dados or {})), esf or dict(V=60, M=40))


def _v(r, ini):
    return next(x for x in r["verificacoes"] if x["nome"].startswith(ini))


class TestParedeDoTubo(unittest.TestCase):
    def test_parede_conta_a_mao(self):
        r = _ver()
        b, e = 165 / 323.8, 310 / 323.8
        N1 = F0 * (4 + 20 * b * b) * (1 + 0.25 * e)
        self.assertAlmostEqual(N1, 354.76, delta=0.01)
        self.assertAlmostEqual(r["valores"]["N1_Rd_kN"], N1, places=6)
        self.assertAlmostEqual(r["valores"]["Mip_Rd"], 0.31 * N1 / (1 + 0.25 * e), places=6)
        self.assertAlmostEqual(r["valores"]["Mop_Rd"], 0.5 * 0.165 * N1, places=6)
        self.assertAlmostEqual(_v(r, "receptor: interação")["solicitante"], (40 / 88.736) ** 2, delta=1e-4)
        Iy = (0.165 * 0.31 ** 3 - 0.1592 * 0.2906 ** 3) / 12
        p = _v(r, "receptor: punção")
        self.assertAlmostEqual(p["solicitante"], 40 / (Iy / 0.155) * 0.0097, places=6)
        self.assertAlmostEqual(p["resistente"], 2 * 0.0095 * 0.66 * 345000 / 1.1, places=6)
        self.assertEqual(_v(r, "receptor: plastificação")["item"], "EN 1993-1-8 tabela 7.4 (plastificação da face)")
        self.assertAlmostEqual(_v(r, "receptor: β = b1/d0")["resistente"], b, places=9)
        self.assertTrue(r["ok"])
        self.assertFalse(any("não verificad" in a for a in r["avisos"]))

    def test_kp_com_compressao_no_tubo(self):
        """N0 = −1500 kN: A0 = π/4·(0,3238² − 0,3048²) = 9,380e-3 m², σ0 = −159,9 MPa, np = −0,4635,
        kp = 1 − 0,3·0,4635·1,4635 = 0,7965 → N1,Rd = 0,7965·354,76 = 282,6 kN; θ = 60°: N·sen θ."""
        r = _ver(dict(N_receptor=-1500, theta=60), dict(N=-50))
        A0 = math.pi / 4 * (0.3238 ** 2 - 0.3048 ** 2)
        n = 1500 / A0 / 345000
        k = 1 - 0.3 * n * (1 + n)
        self.assertAlmostEqual(r["valores"]["kp"], k, places=9)
        v = _v(r, "receptor: plastificação")
        self.assertAlmostEqual(v["resistente"], 354.756 * k, delta=0.01)
        self.assertAlmostEqual(v["solicitante"], 50 * math.sin(math.radians(60)), places=9)
        self.assertTrue(any("θ < 90°" in a for a in r["avisos"]))

    def test_chapa_longitudinal_e_transversal(self):
        """Chapa 200 × 12,5 (tabela 7.3). Giro 0 (ao longo do tubo): N1,Rd = 5·31,136·(1 + 0,25·200/323,8) = 179,7 kN,
        Mip,Rd = 0,2·N1,Rd, Mop,Rd = 0 (Mz reprova). Giro 90 (atravessada): N1,Rd = 31,136·(4 + 20·(200/323,8)²) = 362,1 kN."""
        ch = dict(forma_ramal="retangular", h_ramal=200, b_ramal=12.5)
        r = _ver(ch, dict(N=50, M=10))
        self.assertAlmostEqual(r["valores"]["N1_Rd_kN"], 5 * F0 * (1 + 0.25 * 200 / 323.8), places=6)
        self.assertAlmostEqual(r["valores"]["Mip_Rd"], 0.2 * r["valores"]["N1_Rd_kN"], places=9)
        self.assertFalse(_ver(ch, dict(N=50, Mz=1))["ok"])   # Mop,Rd = 0
        self.assertTrue(_v(_ver(ch, dict(N=50, Mz=1)), "receptor: Mop numa chapa longitudinal")["taxa"] > 1e9)
        r = _ver(dict(ch, giro=90), dict(N=50))
        self.assertAlmostEqual(r["valores"]["N1_Rd_kN"], F0 * (4 + 20 * (200 / 323.8) ** 2), places=6)
        self.assertAlmostEqual(r["valores"]["N1_Rd_kN"], 362.13, delta=0.01)

    def test_tubo_retangular_no_tubo(self):
        """TR 150 × 100 × 6,3 (tabela 7.4, 3ª linha): N igual ao do I, Mip,Rd = h1·N1,Rd (sem dividir por 1 + 0,25η);
        punção com t0 (não 2t0). β = 100/323,8 = 0,309 < 0,4: fora da validade, reprova."""
        r = _ver(dict(forma_ramal="tubo_retangular", h_ramal=150, b_ramal=100, tw_ramal=6.3), dict(N=50))
        N1 = F0 * (4 + 20 * (100 / 323.8) ** 2) * (1 + 0.25 * 150 / 323.8)
        self.assertAlmostEqual(r["valores"]["N1_Rd_kN"], N1, places=6)
        self.assertAlmostEqual(r["valores"]["Mip_Rd"], 0.15 * N1, places=9)
        self.assertAlmostEqual(_v(r, "receptor: punção")["resistente"], 0.0095 * 0.66 * 345000 / 1.1, places=6)
        self.assertFalse(_v(r, "receptor: β = b1/d0")["ok"])
        self.assertEqual(_v(r, "solda: garganta")["item"], "NBR 16239 6.1.5.7")   # parede tubular do perfil

    def test_fora_das_tabelas_avisa(self):
        r = _ver(dict(giro=90), dict(N=10))   # I com a alma atravessada: a tabela 7.4 não cobre
        self.assertFalse(any(x["nome"].startswith("receptor: plastificação") for x in r["verificacoes"]))
        self.assertTrue(any("fora das tabelas 7.3/7.4" in a for a in r["avisos"]))
        r = _ver(dict(forma_receptor="tubo_retangular", D_receptor=300, B_receptor=300), dict(N=10))
        self.assertTrue(any("tubo retangular" in a and "não verificada" in a for a in r["avisos"]))


class TestPernaAcimaDaMinima(unittest.TestCase):
    def test_padrao_perfil_tubo(self):
        ex = {t["tipo"]: t for t in P.tipos_ligacao()}["perfil_tubo_soldado"]["exemplo"]
        self.assertEqual(ex["dados"]["b_solda"], 6)
        self.assertEqual((ex["esforcos"]["V"], ex["esforcos"]["M"]), (60.0, 40.0))
        r = P.verificar_ligacao("perfil_tubo_soldado", ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertAlmostEqual(_v(r, "solda: dimensão mínima")["taxa"], 5 / 6, places=9)
        self.assertFalse(any("dimensão máxima" in x["nome"] for x in r["verificacoes"]))   # junta em T (como no tubo–tubo)
        resist = [x["taxa"] for x in r["verificacoes"] if x["unidade"] in ("kN", "kN/m") or "interação" in x["nome"]]
        self.assertTrue(0.15 < max(resist) < 0.6, resist)

    def test_sugerir_perna_mais_grossa_se_a_solda_pede(self):
        s = L.registro("perfil_tubo_soldado")["sugerir"](dict(PT), dict(V=150, M=90))
        r = _ver(dict(b_solda=s["b_solda"]), dict(V=150, M=90))
        self.assertTrue(_v(r, "solda perfil–tubo")["ok"])
        self.assertFalse(_ver(dict(b_solda=[b for b in L._B_MM if b < s["b_solda"]][-1]), dict(V=150, M=90))["ok"])

    def test_solda_direta_padrao(self):
        """W310×38,7 na mesa do W250×73: mínima 3 mm (tabela 10, alma 5,8), máxima 5,8 (borda da alma) → 4 mm; a solda da
        alma fica em 172,1/609,6 = 0,28 e o padrão em 0,75 (era 1,00 com 3 mm = mínima)."""
        ex = {t["tipo"]: t for t in P.tipos_ligacao()}["solda_direta_alma"]["exemplo"]
        self.assertEqual(ex["dados"]["b_solda"], 4)
        r = P.verificar_ligacao("solda_direta_alma", ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertAlmostEqual(r["taxa"], 0.75, places=9)
        self.assertAlmostEqual(_v(r, "solda alma–apoio")["resistente"], 0.6 * 0.707 * 4 * 485 / 1.35 * 1, delta=0.1)

    def test_solda_direta_acima_da_minima_dentro_da_maxima(self):
        for viga, sup in (("W310x38.7", "W250x73"), ("W460x52", "W310x97"), ("UPN 120", "W 200 x 26,6")):
            s = P.sugerir_ligacao("solda_direta_alma", dict(perfil_viga=viga, perfil_suporte=sup, apoio="mesa_pilar"), {"V": 30})
            r = P.verificar_ligacao("solda_direta_alma", s, {"V": 30})
            mn, mx = _v(r, "solda: dimensão mínima"), _v(r, "solda: dimensão máxima")
            self.assertTrue(r["ok"], viga)
            self.assertLessEqual(mx["solicitante"], mx["resistente"], viga)
            if viga == "UPN 120":   # alma 7 mm: mínima 5, máxima 7 − 1,5 = 5,5 → não cabe perna comercial acima da mínima
                self.assertEqual((mn["solicitante"], s["b_solda"]), (5.0, 5))
            else:
                self.assertLess(mn["solicitante"], mn["resistente"], viga)   # perna > mínima


class TestDesenhoPerfilNaSela(unittest.TestCase):
    def test_pecas_do_I_encostam_no_tubo(self):
        g = _ver()["geometria"]
        viga = next(m for m in g["membros"] if m["barra"] == "viga")
        self.assertEqual(len(viga["pecas"]), 3)   # 2 mesas + alma
        R = 161.9
        for pc in viga["pecas"]:
            ponta = pc["base"][:-2]
            for x, y, z in ponta:
                self.assertAlmostEqual(math.hypot(x, y), R, delta=1e-3)   # na superfície do tubo (eixo z pelo nó)
            self.assertAlmostEqual(pc["base"][-1][0], R + 1.25 * 310, delta=1e-3)
        mesa = next(pc for pc in viga["pecas"] if pc["normal"] == [0.0, 0.0, 1.0])
        self.assertEqual(mesa["espessura"], 9.7)
        self.assertAlmostEqual(min(p[0] for p in mesa["base"]), math.sqrt(R ** 2 - 82.5 ** 2), delta=1e-3)   # ponta da mesa
        self.assertEqual([t["nome"] for t in g["tubos"]], ["receptor"])

    def test_receptor_retangular_vai_como_chapas(self):
        g = _ver(dict(forma_receptor="tubo_retangular", D_receptor=300, B_receptor=250), dict(N=10))["geometria"]
        pilar = next(m for m in g["membros"] if m["barra"] == "pilar")
        self.assertEqual(len(pilar["pecas"]), 4)
        frente = pilar["pecas"][0]   # parede voltada para o perfil: plano médio a D/2 − t/2 do eixo
        self.assertTrue(all(abs(p[0] - (150 - 4.75)) < 1e-6 for p in frente["base"]))
        viga = next(m for m in g["membros"] if m["barra"] == "viga")
        self.assertTrue(all(abs(p[0] - 150) < 1e-6 for pc in viga["pecas"] for p in pc["base"][:-2]))   # face plana


if __name__ == "__main__":
    unittest.main()
