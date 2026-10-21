"""Testes de aco/ligacoes_contraventamento.py (NBR 8800:2008). Tolerância 1 % contra o livro.

Fontes (páginas do livro; página do PDF = livro + 21):
  [PFEIL] W. Pfeil e M. Pfeil, "Estruturas de aço: dimensionamento prático", 8ª ed., LTC:
          2.3.5 tirante rosqueado (p.57); 3.5.2 tirante 2L parafusado no gusset (p.79–81);
          8.7.1 treliça de cobertura, nó B5–B6–D2–M3 com gusset de 8 mm (p.233–239);
          2.3.7 Ct do U ligado pela alma (p.58–59); 3.5.3 Ct do U 306 (p.81–82).
  Cálculos à mão (UFM, flambagem do gusset, garfo/pino, cantoneira simples) estão no próprio teste.
Divergências do livro documentadas em docs/v3/ligacoes/IMPL-CONTRAVENTAMENTO.md.
"""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes  # noqa: E402
from prossen_estrutural.aco import ligacoes as lg  # noqa: E402
from prossen_estrutural.aco import ligacoes_contraventamento as C  # noqa: E402
from prossen_estrutural.aco.nbr8800 import ne_cantoneira_uma_aba  # noqa: E402
from prossen_estrutural import materiais  # noqa: E402

KPA = 1000.0   # kN/m² por MPa


def rel(a, b):
    return abs(a - b) / abs(b)


def linha(r, prefixo):
    achados = [v for v in r["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"'{prefixo}' ausente: {[v['nome'] for v in r['verificacoes']]}"
    return achados[0]


# Pfeil 3.5.2 (p.79–81): 2L 63×6,3 (Ag = 7,68 cm², x̄ = 1,8 cm), A36, gusset 6,3 mm, 5 parafusos A307 φ12,7 em
# corte duplo, s = 40 mm, bordas 25 mm. g_aba = 32 mm reproduz Ant = 2,90 cm² do livro.
P352 = dict(secao="2L", dim_a=63, t_perfil=6.3, A_perfil=768, xg_perfil=18, aco_perfil="A-36", ligacao="parafusada",
            n_linhas=1, n_por_linha=5, s=40, e1=25, e_gusset=25, g_aba=32, d_mm=12.7, tipo_parafuso="A307",
            t_gusset=6.3, aco_gusset="A-36", apoio="viga-pilar", theta=45, eb=150, ec=150)


class TestPfeil352(unittest.TestCase):
    def setUp(self):
        self.r = C.verificar_gusset(P352, {"N": 150})

    def test_parafusos(self):
        # corte: 0,4·1,27·41,5·2·5/1,35 = 156 kN; contato no gusset: 40,3 + 4·56,9 = 267,9 kN
        self.assertLess(rel(linha(self.r, "parafusos: grupo")["resistente"], 156.0), 0.01)
        self.assertLess(rel(5 * linha(self.r, "parafusos: cisalhamento")["resistente"], 156.0), 0.01)
        self.assertLess(rel(linha(self.r, "parafusos: contato no gusset")["resistente"], 267.9), 0.01)

    def test_barra(self):
        self.assertLess(rel(linha(self.r, "barra: escoamento")["resistente"], 349.0), 0.01)   # 15,36·25/1,1
        rup = linha(self.r, "barra: ruptura")
        self.assertIn("Ct = 0.887", rup["nome"])                                             # 1 − 1,8/16
        self.assertLess(rel(rup["resistente"], 350.0), 0.01)                                 # Ae = 11,82 cm²
        self.assertLess(rel(linha(self.r, "barra: colapso")["resistente"], 338.0), 0.01)     # bloco 338 kN

    def test_resistencia_governante(self):
        # Rd = 156 kN (corte dos parafusos) → com N = 156 a taxa é 1
        r = C.verificar_gusset(P352, {"N": 156})
        self.assertTrue(r["criterio"].startswith("parafusos"))
        self.assertLess(abs(r["taxa"] - 1.0), 0.01)
        self.assertTrue(r["item"].startswith("NBR 8800 6.3.3.2"))


# Pfeil 8.7.1 (p.233–239): banzo 2L 76×9,1 kg/m (Ag = 11,48 cm², x̄ = 2,21 cm), MR250, E60, gusset 8 mm,
# filetes b = 5 mm com l1 = 100 e l2 = 40 mm.
B6 = dict(secao_1="2L", dim_a_1=76, t_perfil_1=7.94, A_perfil_1=1148, xg_perfil_1=22.1, ligacao_1="soldada",
          l1_1=100, l2_1=40, b_solda_1=5, theta_1=0.0, aco_perfil_1="MR-250")


class TestPfeil871(unittest.TestCase):
    def setUp(self):
        dados = dict(B6, t_gusset=8, aco_gusset="MR-250", eletrodo="E60", banzo="emendado",
                     secao_2="2L", dim_a_2=76, t_perfil_2=7.94, A_perfil_2=1148, xg_perfil_2=22.1, ligacao_2="soldada",
                     l1_2=100, l2_2=40, b_solda_2=5, theta_2=180.0, aco_perfil_2="MR-250")
        self.r = C.verificar_no_trelica(dados, {"N": 91.8, "N2": 91.8})

    def test_whitmore(self):
        # largura 7,6 + 2·7,0·tg 30° = 15,7 cm (l = média 70 mm); 15,7·0,8·25/1,1 = 285 kN
        self.assertLess(rel(C.largura_whitmore(0.076, 0.070), 0.157), 0.01)
        self.assertLess(rel(linha(self.r, "gusset 1: Whitmore, escoamento")["resistente"], 285.0), 0.01)

    def test_ct_banzo_tracionado(self):
        # Ct = 1 − 2,21/7,0 = 0,68; Ae = 15,7 cm² → 465 kN
        rup = linha(self.r, "barra 1: ruptura")
        self.assertIn("Ct = 0.684", rup["nome"])
        self.assertLess(rel(rup["resistente"], 465.0), 0.01)

    def test_filetes_balanceados(self):
        # B6: F = 104/2 kN, b = 5 mm: l1 = 57 mm, l2 = 23 mm (livro com 0,7b; aqui 0,707b → −1 %)
        Fw = lg.resistencia_filete(0.005, 415 * KPA, 250 * KPA, 400 * KPA)["FwRd"]
        l1, l2 = C.filetes_balanceados(52.0, 0.0221, 0.076, Fw)
        self.assertLess(rel(l1, 0.057), 0.01)
        self.assertLess(rel(l2, 0.023), 0.01)
        # D2: força mínima 45 kN → 22,5 kN por cantoneira, b = 3 mm, distâncias 2,29 e 0,91 cm do livro:
        # l1 = 41, l2 = 16 mm (livro arredonda ao mm)
        Fw = lg.resistencia_filete(0.003, 415 * KPA, 250 * KPA, 400 * KPA)["FwRd"]
        l1, l2 = C.filetes_balanceados(22.5, 0.0091, 0.0320, Fw)
        self.assertAlmostEqual(l1 * 1e3, 41, delta=0.5)
        self.assertAlmostEqual(l2 * 1e3, 16, delta=0.5)

    def test_bloco_gusset(self):
        # Livro: (0,60·25·2·10 + 40·7,5)·0,80/1,35 = 355 kN, com as duas linhas de cisalhamento = l1 = 100 mm
        self.assertLess(rel(lg.colapso_rasgamento(2 * 0.1 * 0.008, 2 * 0.1 * 0.008, 0.075 * 0.008, 250 * KPA,
                                                  400 * KPA), 355.0), 0.01)
        # Motor: linhas de comprimento l1 + l2 = 140 mm e Ant = 76·8 (à mão): (0,6·25·11,2 + 40·6,08)/1,35
        esperado = (0.6 * 25 * 11.2 + 40 * 6.08) / 1.35
        self.assertLess(rel(linha(self.r, "gusset 1: colapso")["resistente"], esperado), 1e-6)

    def test_minimo_45kN(self):
        dados = dict(B6, t_gusset=8, aco_gusset="MR-250", eletrodo="E60", e_banzo=60, theta_1=60.0)
        r = C.verificar_no_trelica(dados, {"N": 24.2})
        self.assertAlmostEqual(linha(r, "barra 1: escoamento")["solicitante"], 45.0)
        self.assertTrue(any("45 kN" in a for a in r["avisos"]))


class TestTirante(unittest.TestCase):
    def test_pfeil_235(self):
        # Pfeil 2.3.5 (p.57): A36, Nd = 1,4·150 = 210 kN → Ag ≥ 9,45 cm² (ruptura) → φ 34,9 mm (Ag = 9,58 cm²)
        r = C.verificar_tirante(dict(D_barra=34.925, t_chapa=12.5), {"N": 210})
        self.assertTrue(r["ok"])
        self.assertLess(rel(r["Ab_mm2"], 958), 0.01)
        self.assertLess(rel(linha(r, "barra: tração na rosca")["resistente"], 0.75 * 9.58 * 40 / 1.35), 0.01)
        self.assertLess(rel(linha(r, "barra: escoamento")["resistente"], 9.58 * 25 / 1.1), 0.01)
        self.assertEqual(C.sugerir_tirante({}, {"N": 210})["D_barra"], 34.925)
        self.assertFalse(C.verificar_tirante(dict(D_barra=31.75, t_chapa=12.5), {"N": 210})["ok"])

    def test_compressao_reprova(self):
        r = C.verificar_tirante(dict(D_barra=20, t_chapa=8), {"N": -10})
        self.assertFalse(r["ok"])

    def test_garfo_pino(self):
        # à mão (RC08: pino pela 6.4, não pela 6.3.3.2): pino A325 φ19,05, fy = 63,5 kN/cm² (tab. A.3), 2 planos:
        # 2·0,6·0,75·2,850·63,5/1,10 = 148,1 kN (6.4.2.2); M = N·t/8 = 60·0,95/8 = 7,125 kN·cm contra
        # 1,2·(π·1,905³/32)·63,5/1,10 = 47,0 kN·cm (6.4.2.1); gusset 9,5 mm A36,
        # borda 2d = 38,1 mm: lf = 38,1 − 20,55/2 = 27,8 mm → min(1,2·2,78; 2,4·1,905)·0,95·40/1,35 = 94,0 kN
        r = C.verificar_tirante(dict(D_barra=19.05, t_chapa=9.5, extremidade="garfo_pino"), {"N": 60})
        self.assertLess(rel(linha(r, "pino: cis")["resistente"], 2 * 0.6 * 0.75 * math.pi * 1.905 ** 2 / 4 * 63.5 / 1.1),
                        1e-3)
        self.assertLess(rel(linha(r, "pino: flex")["solicitante"], 60 * 0.0095 / 8), 1e-3)
        self.assertLess(rel(linha(r, "pino: flex")["resistente"], 1.2 * math.pi * 1.905 ** 3 / 32 * 63.5 / 1.1 / 100),
                        1e-3)
        lf = 3.81 - (1.905 + 0.15) / 2
        self.assertLess(rel(linha(r, "gusset: contato")["resistente"],
                            min(1.2 * lf, 2.4 * 1.905) * 0.95 * 40 / 1.35), 1e-3)
        self.assertTrue(r["geometria"]["parafusos"])


class TestFuncoes(unittest.TestCase):
    def test_ct(self):
        self.assertAlmostEqual(C.coef_Ct("U", 0.020, 0.075)[0], 1 - 2.0 / 7.5)       # Pfeil 2.3.7: 0,73
        self.assertAlmostEqual(C.coef_Ct("U", 0.0177, 0.144)[0], 1 - 1.77 / 14.4)    # Pfeil 3.5.3: 0,877
        self.assertEqual(C.coef_Ct("L", 0.005, 0.200)[0], 0.90)                      # teto 0,90 (5.2.5)
        self.assertIn("0,60", C.coef_Ct("L", 0.030, 0.050)[1])
        D = 0.1
        self.assertEqual(C.coef_Ct("tubo_circular", 0, 0.14, D=D)[0], 1.0)           # lc ≥ 1,3D
        self.assertAlmostEqual(C.coef_Ct("tubo_circular", 0, 0.11, D=D)[0], 1 - D / math.pi / 0.11)
        self.assertIsNone(C.coef_Ct("tubo_circular", 0, 0.09, D=D)[0])

    def test_flambagem_chapa(self):
        Ag, t = 0.2 * 0.010, 0.010
        r = C.flambagem_chapa(Ag, t, 0.065, 250 * KPA)          # KL/r = 22,5 ≤ 25 → escoamento (6.5.4)
        self.assertEqual(r["chi"], 1.0)
        r = C.flambagem_chapa(Ag, t, 0.65 * 0.2, 250 * KPA)     # à mão: KL/r = 45,03; λ0 = 0,5068; χ = 0,8981
        self.assertAlmostEqual(r["KL_r"], 0.13 / (0.01 / math.sqrt(12)), places=6)
        self.assertLess(rel(r["chi"], 0.658 ** (0.5068 ** 2)), 1e-3)
        self.assertLess(rel(r["NcRd"], r["chi"] * Ag * 250 * KPA / 1.1), 1e-9)

    def test_forca_uniforme(self):
        P, th = 100.0, math.radians(45)
        u = C.forca_uniforme(P, th, 0.15, 0.15, 0.30, 0.30)
        # à mão: β = 150, α = 150·1 − 150 + 150 = 150 = ᾱ; r = √(300² + 300²) = 424,3; Hc = Vb = 150/424,3·P
        self.assertAlmostEqual(u["alfa"], 0.15)
        self.assertAlmostEqual(u["Hc"], 150 / math.hypot(300, 300) * P)
        self.assertAlmostEqual(u["Mb"], 0.0, places=9)
        u = C.forca_uniforme(P, math.radians(35), 0.20, 0.12, 0.50, 0.40)
        self.assertAlmostEqual(u["Hb"] + u["Hc"], P * math.sin(math.radians(35)))   # equilíbrio
        self.assertAlmostEqual(u["Vb"] + u["Vc"], P * math.cos(math.radians(35)))
        self.assertAlmostEqual(u["alfa"] - u["beta"] * math.tan(math.radians(35)),
                               0.20 * math.tan(math.radians(35)) - 0.12)
        self.assertAlmostEqual(u["Mb"], u["Vb"] * abs(u["alfa"] - 0.25))


class TestGusset(unittest.TestCase):
    def test_compressao_thornton(self):
        r = C.verificar_gusset(P352, {"N": -150})
        g = r["geometria"]
        th = math.radians(45)
        L1 = g["recuo"] + 25 - max(150 / math.cos(th), 150 / math.sin(th))   # mm, do Whitmore à borda apoiada
        esb = 0.65 * L1 / (6.3 / math.sqrt(12))
        self.assertIn(f"KL/r = {esb:.1f}", linha(r, "gusset: compressão")["nome"])
        self.assertFalse(any(v["nome"].startswith("barra: ruptura") for v in r["verificacoes"]))

    def test_tubo_e_apoios(self):
        for ap in C.APOIOS:
            r = C.verificar_gusset(dict(secao="tubo_circular", dim_a=101.6, t_perfil=4.75, ligacao="soldada", l1=150,
                                        b_solda=4, t_gusset=9.5, apoio=ap, theta=40, b_interface=8), {"N": 150})
            self.assertTrue(any(v["nome"].startswith("interface") for v in r["verificacoes"]), ap)
            self.assertEqual(r["geometria"]["recortes"][0]["tipo"], "rasgo")
        with self.assertRaises(ValueError):   # tubo parafusado: fora do escopo
            C.verificar_gusset(dict(secao="tubo_circular", dim_a=100, t_perfil=5, t_gusset=8), {"N": 10})

    def test_interface_pilar_momento(self):
        # à mão: θ = 30°, ec = 150, gusset do pilar a partir de eb = 0 com Lv = 400 → a linha da diagonal corta a
        # face em v0 = 150/tg30 = 259,8 mm; M = N·sen30·|200 − 259,8|
        r = C.verificar_gusset(dict(P352, apoio="pilar", theta=30, eb=0, Lv=400), {"N": 100})
        self.assertAlmostEqual(r["forcas_interface"]["Mc"], 100 * 0.5 * abs(0.2 - 0.15 / math.tan(math.radians(30))))


class TestCantoneiraSimples(unittest.TestCase):
    D = dict(dim_a=50.8, t_perfil=4.76, n_por_linha=2, s=40, e1=25, e_gusset=25, d_mm=12.7, tipo_parafuso="A307",
             t_gusset=6.3)

    def test_minimo_e_dica(self):
        r = C.verificar_cantoneira_simples(dict(self.D, comprimento=1500), {"N": -30})
        self.assertAlmostEqual(linha(r, "parafusos: grupo")["solicitante"], 45.0)      # mínimo de 45 kN
        e14 = ne_cantoneira_uma_aba(secoes.secao_cantoneira(0.0508, 0.0508, 0.00476), materiais.aco("A-36"), 1.5)
        self.assertAlmostEqual(r["dica_barra"]["E14"]["KxLx_mm"], e14["KxLx"] * 1e3)
        self.assertEqual(r["dica_barra"]["ligacao_uma_aba"], dict(aba="maior", trelica="plana", L=1.5))
        self.assertTrue(linha(r, "barra: compressão com a excentricidade")["item"].endswith("E.1.4"))

    def test_tracao_ct(self):
        # à mão: ec = ȳ da cantoneira (cantos vivos), lc = s = 40 mm → Ct = 1 − ec/lc (≤ 0,90)
        r = C.verificar_cantoneira_simples(self.D, {"N": 40})
        a, t = 0.0508, 0.00476
        A1, A2 = a * t, (a - t) * t
        ec = (A1 * t / 2 + A2 * (t + (a - t) / 2)) / (A1 + A2)
        self.assertAlmostEqual(A1 + A2, secoes.secao_cantoneira(a, a, t).A)
        self.assertAlmostEqual(r["dica_barra"]["Ct"], min(1 - ec / 0.040, 0.9))
        self.assertAlmostEqual(r["dica_barra"]["ec_mm"], ec * 1e3)


class TestContrato(unittest.TestCase):
    def test_registro(self):
        tipos = {t["tipo"]: t for t in lg.tipos_ligacao()}
        self.assertTrue(set(C.TIPOS) <= set(tipos))
        for nome, t in C.TIPOS.items():
            self.assertEqual(t["familia"], "contraventamento")
            self.assertTrue(callable(t["verificar"]) and callable(t["sugerir"]))
            for c in tipos[nome]["campos"]:
                self.assertTrue({"nome", "unidade", "descricao", "padrao", "rotulo"} <= set(c))
                self.assertLessEqual(len(c["rotulo"]), 28, c["rotulo"])
        json.dumps(lg.tipos_ligacao())

    def test_resultado_e_geometria(self):
        casos = [("gusset_contraventamento", P352, {"N": 100}),
                 ("no_trelica_gusset", dict(B6, t_gusset=8, e_banzo=60, theta_1=50.0), {"N": 80}),
                 ("tirante_barra_redonda", dict(D_barra=20, t_chapa=8), {"N": 50}),
                 ("cantoneira_simples_trelica", TestCantoneiraSimples.D, {"N": 40})]
        for tipo, d, e in casos:
            r = lg.verificar_ligacao(tipo, d, e)   # despacho pelo ligacoes.py
            self.assertTrue({"ok", "taxa", "verificacoes", "avisos", "geometria", "taxa_max", "criterio"} <= set(r))
            self.assertTrue({"chapas", "cantoneiras", "parafusos", "soldas", "recortes"} <= set(r["geometria"]))
            for v in r["verificacoes"]:
                self.assertTrue(v["item"].startswith("NBR 8800 "), v)
                self.assertEqual(v["ok"], v["taxa"] <= 1 + 1e-9)
            for ch in r["geometria"]["chapas"]:
                self.assertTrue({"nome", "contorno", "origem", "normal", "u", "espessura"} <= set(ch))
            json.dumps(r)

    def test_sugerir(self):
        casos = [("gusset_contraventamento", dict(secao="2L", dim_a=63.5, t_perfil=6.35, perfil_viga="W310x38.7"),
                  {"N": 300}),
                 ("gusset_contraventamento", dict(secao="tubo_retangular", dim_a=100, dim_b=100, t_perfil=6.3), {"N": -250}),
                 ("no_trelica_gusset", dict(secao_1="2L", dim_a_1=63.5, t_perfil_1=6.35, secao_2="2L", dim_a_2=44.45,
                                            t_perfil_2=3.2, ligacao_2="soldada", e_banzo=60), {"N": 85, "N2": -24}),
                 ("tirante_barra_redonda", {}, {"N": 120}),
                 ("cantoneira_simples_trelica", dict(dim_a=50.8, t_perfil=4.76), {"N": 40})]
        for tipo, p, e in casos:
            d = C.TIPOS[tipo]["sugerir"](p, e)
            self.assertTrue(C.verificar(tipo, d, e)["ok"], (tipo, p))
        d = C.sugerir_gusset(dict(secao="2L", dim_a=63.5, t_perfil=6.35, perfil_viga="W310x38.7"), {"N": 300})
        self.assertAlmostEqual(d["eb"], secoes.catalogo("W310x38.7").props["h"] / 2 * 1e3)

    def test_erros(self):
        with self.assertRaises(ValueError) as cm:
            C.verificar_gusset({}, {"N": 10})
        self.assertIn("dim_a", str(cm.exception))
        self.assertIn("t_gusset", str(cm.exception))
        with self.assertRaises(ValueError):
            C.verificar_gusset(dict(P352, apoio="laje"), {"N": 10})
        with self.assertRaises(ValueError):    # geometria impossível: borda ≤ dn/2
            C.verificar_gusset(dict(P352, e1=5), {"N": 10})


if __name__ == "__main__":
    unittest.main()
