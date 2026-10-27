"""W9-FT (docs/v3/W9-FLANGE-TUBO.md): emenda flangeada de tubo redondo (Igarashi, CIDECT DG1) e de tubo quadrado/
retangular (T-stub com efeito alavanca, CIDECT DG3), viga e pilar.

Contas à mão:

1) Igarashi (Cao & Packer, AISC Engineering Journal 1997 1º trim., tabela 1 e tabela 3, p. 19–20), polegadas e kips:
   k1 = ln(Dp/(D − t)), k3 = k1 + 2, f3 = [k3 + √(k3² − 4k1)]/(2k1);
   força total nos parafusos = [1 − 1/f3 + 1/(f3·ln(Df/Dp))]·T;  tf ≥ √(2T/(π·Fy·f3)).
   J-2: D = 7, t = 0,312, Dp = 10, Df = 13, T = 295 kips, parafusos 1" A325 (φRn = 0,75·90·0,7854 = 53,01 kips):
   k1 = ln(10/6,688) = 0,40227; f3 = 5,5216; colchete = 1,5092; n = 295·1,5092/53,01 = 8,40 (tabela 3: 8,4).
   J-1 (510 kips, 1¼"): n = 9,27 (9,3). tf da tabela 3 é ~1,8 % maior que a fórmula (J-2: 0,972 contra 0,99; diferença
   constante de 3,6 % em t², não explicada no artigo) — o teste aceita 2,5 %.

2) Tubo redondo, à mão (SI): TC 168,3 × 6,4, flange A-36 tp = 16, Dp = 250, Df = 330, 6 parafusos ¾" A325, N = 200 kN:
   r3 = 80,95; k1 = ln(125/80,95) = 0,434483; f3 = 5,15690; Df,ef = min(330; 2·250 − 168,3 = 331,7) = 330;
   colchete = 1 − 1/5,15690 + 1/(5,15690·ln 1,32) = 1,504533.
   flange: NRd = π·f3·tp²·fy/(2·γa1) = π·5,15690·0,016²·250 000/2,2 = 471,30 kN → taxa 200/471,30 = 0,4244.
   parafusos: Ft,Rd = 0,75·Ab·fub/γa2 = 0,75·2,85023e-4·825 000/1,35 = 130,635 kN; Σ = 6·130,635 = 783,81 kN;
   Sd = 200·1,504533 = 300,91 kN → 0,3839.

3) Tubo retangular, à mão: metalon 100 × 100 × 3 (CF-26), 2 lados com 2 parafusos ⅝" A325, e_parede = 30, e_borda = 35,
   tp = 12,5 (A-36), M = 6 kN·m. 6.1.5.2: braço entre as linhas = 100 + 2·30 = 160 mm → 6/0,16 = 37,5 < 45 kN → × 1,2:
   M = 7,2 kN·m. Parafusos em z = ±80 mm, Σz² = 4·0,08² = 0,0256 m² → F = 7,2·0,08/0,0256 = 22,5 kN.
   T-stub (DG3, b' = b − db/2 + t0): y = ±25 mm, Bf = 2·(25 + 35) = 120 mm, p = 60 mm, b' = 30 + 3 − 7,9375 = 25,0625 mm,
   a' = min(35; 1,25·33) + 7,9375 = 42,9375 mm, δ = 1 − 17,375/60 = 0,710417, Mp = p·tp²·fy/(4γa1) = 0,532670 kN·m;
   α = (22,5·0,0250625/0,53267 − 1)/δ = 0,082533; Q = α·δ·Mp/a' = 0,72738 kN; B = 23,227 kN ≤ Ft,Rd = 90,72 kN (⅝").
   Tmax = Mp(1 + δ)/b' = 36,354 kN → flange 22,5/36,354 = 0,6189.
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_tubos as T

TC, TR = "emenda_flangeada_tubo_circular", "emenda_flangeada_tubo_retangular"


def _ex(tipo):
    return {t["tipo"]: t for t in P.tipos_ligacao()}[tipo]


def _v(r, inicio):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(inicio))


class TestIgarashi(unittest.TestCase):
    def test_cao_packer_tabela_3(self):
        for D, t, Df, Dp, Tk, db, n_tab, tf_tab in ((10, .375, 18, 14, 510, 1.25, 9.3, 1.26),
                                                     (7, .312, 13, 10, 295, 1.0, 8.4, .99),
                                                     (3, .203, 7, 5, 80, .625, 5.8, .60)):
            ig = T.igarashi(D, t, Dp, Df)
            phi_rn = 0.75 * 90 * math.pi / 4 * db * db
            self.assertAlmostEqual(ig["fator"] * Tk / phi_rn, n_tab, delta=0.05)
            tf = math.sqrt(2 * Tk / (math.pi * 36 * ig["f3"]))
            self.assertAlmostEqual(tf / tf_tab, 1.0, delta=0.025)
        ig = T.igarashi(7, .312, 10, 13)
        self.assertAlmostEqual(ig["f3"], 5.5216, 3)
        self.assertAlmostEqual(ig["fator"], 1.5092, 3)

    def test_df_maior_que_2dp_menos_d_nao_ajuda(self):
        # Cao & Packer 4.1: o método foi feito com Df − Dp = Dp − D; flange maior é tratado como Df = 2Dp − D (a favor)
        self.assertAlmostEqual(T.igarashi(168.3, 6.4, 250, 400)["fator"], T.igarashi(168.3, 6.4, 250, 331.7)["fator"])
        self.assertGreater(T.igarashi(168.3, 6.4, 250, 300)["fator"], T.igarashi(168.3, 6.4, 250, 331.7)["fator"])


class TestCircular(unittest.TestCase):
    D = dict(perfil_viga="TC 168,3x6,4", uso="pilar", tp=16, D_flange=330, D_furacao=250, n=6, d=19.05,
             tipo_parafuso="A325", solda="filete", b_solda=10, aco_tubo="A-572 GR.50", aco_flange="A-36")

    def test_conta_a_mao(self):
        r = L.verificar_ligacao(TC, self.D, dict(N=200.0))
        fl, pf = _v(r, "flange: flexão"), _v(r, "parafusos: tração com efeito alavanca")
        self.assertAlmostEqual(fl["resistente"], 471.30, delta=0.05)
        self.assertAlmostEqual(fl["taxa"], 0.4244, 3)
        self.assertAlmostEqual(pf["solicitante"], 300.91, delta=0.05)
        self.assertAlmostEqual(pf["resistente"], 783.81, delta=0.05)
        self.assertTrue(r["ok"], [v for v in r["verificacoes"] if not v["ok"]])

    def test_momento_vira_tracao_equivalente(self):
        # N_eq = N + 4·M/Dp: M = 25 kN·m, N = −150 → −150 + 4·25/0,25 = 250 kN
        r = L.verificar_ligacao(TC, self.D, dict(N=-150.0, M=25.0))
        self.assertAlmostEqual(r["valores"]["N_eq_kN"], 250.0, 6)
        r2 = L.verificar_ligacao(TC, self.D, dict(N=-150.0, M=15.0, Mz=20.0))   # √(15² + 20²) = 25
        self.assertAlmostEqual(r2["valores"]["N_eq_kN"], 250.0, 6)

    def test_so_compressao_sem_tracao_nos_parafusos(self):
        r = L.verificar_ligacao(TC, self.D, dict(N=-300.0))
        nomes = {v["nome"] for v in r["verificacoes"]}
        self.assertNotIn("flange: flexão (Igarashi, tração equivalente)", nomes)
        self.assertTrue(any(n.startswith("compressão: contato") for n in nomes))

    def test_padrao_de_fabrica_ate_090(self):
        ex = _ex(TC)["exemplo"]
        self.assertEqual(ex["dados"]["perfil_viga"], "TC 168,3x6,4")
        self.assertEqual(ex["dados"]["uso"], "pilar")
        self.assertLess(ex["esforcos"]["N"], 0)
        r = P.verificar_ligacao(TC, ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertGreater(r["taxa"], 0.5)   # não superdimensionado

    def test_geometria(self):
        ex = _ex(TC)["exemplo"]
        d = ex["dados"]
        g = P.verificar_ligacao(TC, d, ex["esforcos"])["geometria"]
        self.assertEqual(len(g["tubos"]), 2)
        a, b = g["tubos"]
        self.assertEqual((a["eixo"], b["eixo"]), ([1.0, 0.0, 0.0], [-1.0, 0.0, 0.0]))
        self.assertAlmostEqual(a["origem"][0], d["tp"])
        self.assertAlmostEqual(b["origem"][0], -d["tp"])
        self.assertAlmostEqual(a["d"], 168.3)
        self.assertEqual(len(g["chapas"]), 2)
        for c in g["chapas"]:
            self.assertAlmostEqual(max(math.hypot(*p) for p in c["contorno"]), d["D_flange"] / 2, 2)
            self.assertAlmostEqual(c["espessura"], d["tp"])
        self.assertEqual(len(g["parafusos"]), d["n"])
        for p in g["parafusos"]:
            self.assertAlmostEqual(math.hypot(p["origem"][1], p["origem"][2]), d["D_furacao"] / 2, 3)
            self.assertAlmostEqual(p["origem"][0], -d["tp"])
            self.assertAlmostEqual(p["pega"], 2 * d["tp"])
            self.assertGreater(p["comprimento"], 2 * d["tp"])
        self.assertEqual(len(g["soldas"]), 2)
        self.assertEqual({round(s["pontos"][0][0], 3) for s in g["soldas"]}, {d["tp"], -d["tp"]})
        self.assertEqual(g["vista"], dict(x=[0.0, 0.0, 1.0], z=[1.0, 0.0, 0.0]))   # pilar em pé
        self.assertTrue(any("Ø" in ln["texto"] for ln in g["linhas"]))
        gv = P.verificar_ligacao(TC, dict(d, uso="viga"), ex["esforcos"])["geometria"]
        self.assertNotIn("vista", gv)

    def test_parede_fina_e_limites(self):
        d = dict(self.D, perfil_viga="Tubo Ø168,3x2,00")
        r = L.verificar_ligacao(TC, d, dict(N=50.0))
        self.assertFalse(_v(r, "tubo: parede t0 ≥ 2,5 mm")["ok"])
        self.assertTrue(any(v["item"].startswith("NBR 14762") for v in r["verificacoes"]))
        with self.assertRaises(ValueError):
            L.verificar_ligacao(TC, dict(self.D, perfil_viga="TQ 100x100x5"), {})

    def test_furacao_dentro_do_tubo_impossivel(self):
        with self.assertRaises(ValueError):
            L.verificar_ligacao(TC, dict(self.D, D_furacao=170), {})


class TestRetangular(unittest.TestCase):
    D = dict(perfil_viga="Tubo 100x100x3,00", uso="viga", lados="2 lados", n=2, tp=12.5, e_parede=30, e_borda=35,
             d=15.875, tipo_parafuso="A325", solda="filete", b_solda=5, aco_tubo="CF-26", aco_flange="A-36")

    def test_conta_a_mao(self):
        r = L.verificar_ligacao(TR, self.D, dict(M=6.0))
        fl, pf = _v(r, "flange: flexão"), _v(r, "parafusos: tração com efeito alavanca")
        self.assertAlmostEqual(pf["solicitante"], 23.227, delta=0.002)
        self.assertAlmostEqual(pf["resistente"], 90.72, delta=0.01)
        self.assertAlmostEqual(fl["solicitante"], 22.5, 6)
        self.assertAlmostEqual(fl["resistente"], 36.354, delta=0.002)
        self.assertTrue(any("6.1.5.2" in a for a in r["avisos"]))

    def test_flange_fina_reprova_pela_alavanca(self):
        r = L.verificar_ligacao(TR, dict(self.D, tp=9.5), dict(M=6.0))   # Tmax = 21,0 < 22,5 kN
        self.assertFalse(_v(r, "flange: flexão")["ok"])

    def test_quatro_lados(self):
        r = L.verificar_ligacao(TR, dict(self.D, lados="4 lados", n=1), dict(N=100.0))
        g = r["geometria"]
        self.assertEqual(len(g["parafusos"]), 4)
        ys = sorted(round(p["origem"][1], 3) for p in g["parafusos"])
        self.assertEqual(ys, [-80.0, 0.0, 0.0, 80.0])
        # N/n = 25 kN por parafuso; p = face/n = 100 mm
        self.assertAlmostEqual(_v(r, "flange: flexão")["solicitante"], 25.0, 6)
        c = g["chapas"][0]["contorno"]
        self.assertAlmostEqual(max(p[0] for p in c) - min(p[0] for p in c), 100 + 2 * (30 + 35))

    def test_padrao_de_fabrica_ate_090(self):
        ex = _ex(TR)["exemplo"]
        self.assertTrue(ex["dados"]["perfil_viga"].startswith("Tubo 100x100"))
        self.assertEqual(ex["dados"]["uso"], "viga")
        r = P.verificar_ligacao(TR, ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertGreater(r["taxa"], 0.5)

    def test_geometria_paredes_do_tubo(self):
        ex = _ex(TR)["exemplo"]
        d = ex["dados"]
        g = P.verificar_ligacao(TR, d, ex["esforcos"])["geometria"]
        self.assertEqual(g["tubos"], [])
        mb = {m["barra"]: m for m in g["membros"]}
        self.assertEqual(set(mb), {"viga", "pilar"})
        for lado, sinal in (("viga", 1), ("pilar", -1)):
            pc = mb[lado]["pecas"]
            self.assertEqual(len(pc), 4)
            xs = [p[0] * sinal for q in pc for p in q["base"]]
            self.assertAlmostEqual(min(xs), d["tp"])
        self.assertEqual(len(g["parafusos"]), 2 * d["n"])
        self.assertEqual(len(g["chapas"]), 2)
        self.assertNotIn("vista", g)
        gp = P.verificar_ligacao(TR, dict(d, uso="pilar"), ex["esforcos"])["geometria"]
        self.assertEqual(gp["vista"]["z"], [1.0, 0.0, 0.0])

    def test_lista_do_seletor(self):
        e = {c["nome"]: c for c in _ex(TR)["campos"]}["perfil_viga"]
        self.assertIn("Tubo 100x100x3,00", e["perfis"])
        self.assertIn("TQ 100x100x5", e["perfis"])
        self.assertNotIn("TC 168,3x6,4", e["perfis"])
        self.assertTrue(e["resugerir"])
        c = {c["nome"]: c for c in _ex(TC)["campos"]}["perfil_viga"]
        self.assertIn("TC 168,3x6,4", c["perfis"])
        self.assertNotIn("TQ 100x100x5", c["perfis"])


class TestBiblioteca(unittest.TestCase):
    def test_achado_buscando_flange(self):
        for tipo in (TC, TR):
            t = _ex(tipo)
            self.assertIn("flange", t["nome"].lower())
            self.assertEqual(t["familia"], "emenda")
            self.assertEqual(set(t["liga"]), {"emenda-viga", "emenda-pilar"})
            self.assertTrue(t["sugerir"])

    def test_sugerir_respeita_alvo(self):
        for tipo, perfil, esf in ((TC, "TC 114,3x6,4", dict(N=80.0, M=8.0)), (TC, "TC 273x8", dict(N=-400, M=60.0)),
                                  (TR, "TQ 150x150x8", dict(M=40.0, V=30.0)), (TR, "Tubo 200x100x3,00", dict(M=10.0))):
            with self.subTest(perfil=perfil):
                d = P.sugerir_ligacao(tipo, dict(perfil_viga=perfil), esf)
                r = P.verificar_ligacao(tipo, d, esf)
                self.assertTrue(r["ok"])
                self.assertLessEqual(r["taxa"], 0.90)


if __name__ == "__main__":
    unittest.main()
