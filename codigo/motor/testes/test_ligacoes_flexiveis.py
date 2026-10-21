"""Testes de aco/ligacoes_flexiveis.py (NBR 8800:2008, cap. 6; 5.7.3/5.7.4). Tolerância 1 %.

Fontes (páginas do livro; PDF em referencias/livros/):
  [PF] Pfeil & Pfeil, "Estruturas de aço — dimensionamento prático", 8ª ed.: probl. 9.10.2 (p. 255–258) e
       9.10.6 (p. 261–263). Página do PDF = página do livro + 21.
  [GD] Gerdau, "Ligações para Estruturas de Aço", 7ª ed., 2018 (docs/v3/ligacoes/CBCA.md): tabela LCPP 30-B
       (p. 33), exemplos 3.3.1/3.3.2 (p. 22), coeficientes C da LCHS (p. 96–97, linha R20).
Linhas do [GD] marcadas como não reproduzidas em CBCA.md §3.3 NÃO são usadas.
"""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural.aco import ligacoes as lg  # noqa: E402
from prossen_estrutural.aco import ligacoes_flexiveis as lf  # noqa: E402

MPa = 1000.0
T = lf.TIPOS


def rel(a, b):
    return abs(a - b) / abs(b)


def ver(res, prefixo):
    achados = [v for v in res["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"verificação '{prefixo}' ausente: {[v['nome'] for v in res['verificacoes']]}"
    return achados[0]


def capacidade(tipo, dados, N=0.0):
    """V resistente: todas as verificações de força são lineares em V (taxa ∝ V)."""
    res = T[tipo]["verificar"](dados, {"V": 100.0, "N": N})
    pior = max((v for v in res["verificacoes"] if v["unidade"] != "mm"), key=lambda v: v["taxa"])
    return 100.0 / pior["taxa"], pior["nome"], res


# Pfeil 9.10.2: VS 450x51 (tw = 6,3) em CS 300x62, 2L76x9,1 kg/m (t = 8 mm), A325 rosca fora, E60, A36
PFEIL_9_10_2 = dict(perfil_viga=dict(d=450, bf=200, tw=6.3, tf=9.5), perfil_suporte=dict(d=300, bf=300, tw=8, tf=9.5),
                    apoio="mesa_pilar", aco_viga="A-36", aco_suporte="A-36", cantoneira="L76x76x8",
                    ligacao_viga="soldada", d=15.875, rosca_no_plano=False, n=3, s=50, e_v=50, dist_topo_furo=100,
                    g_apoio=44, folga=11, b_solda=5, eletrodo="E60")
# Gerdau LCPP 30-B (p. 33): W360 em viga, 3 x 3/4" A325N, L76x76x6,4x230, s = 75, borda 40, gabarito 45
LCPP_30B = dict(perfil_suporte="W530x92", apoio="alma_viga", cantoneira="L76x76x6.35", d=19.05, n=3, s=75, e_v=40,
                dist_topo_furo=75, g_viga=45, g_apoio=45, folga=10, excentricidade_apoio=False)


class TestComponentes(unittest.TestCase):
    def test_coeficiente_centro_instantaneo_gerdau(self):
        # [GD] 5.4 (p. 96–97), AISC tabela 7-7: s = ec = 75 mm, 2..6 parafusos → C = 0,88 1,75 2,81 3,90 4,98
        for n, c in zip(range(2, 7), (0.88, 1.75, 2.81, 3.90, 4.98)):
            ys = [(i - (n - 1) / 2) * 0.075 for i in range(n)]
            self.assertLess(rel(lf.coeficiente_ci(ys, 0.075), c), 0.01, n)

    def test_coeficiente_ci_sem_excentricidade(self):
        self.assertEqual(lf.coeficiente_ci([-0.03, 0.0, 0.03], 0.0), 3.0)

    def test_escoamento_local_alma_pfeil_9_10_6(self):
        # [PF] 9.10.6 (p. 262): (2,5·1,2 + a')·25·0,58 ≥ 130 → a' = 60 mm (1,10/γa1 = 1, NBR 5.7.3.2)
        self.assertLess(rel(lf.escoamento_local_alma(0.0597, 0.012, 0.0058, 250 * MPa), 130.0), 0.01)

    def test_enrugamento_alma_formulas(self):
        # NBR 5.7.4 à mão: W360x32,9 (d 349, tw 5,8, tf 8,5), fy 250, ln = 50 mm (ln/d = 0,143 ≤ 0,2)
        d, tw, tf, fy, ln = 0.349, 0.0058, 0.0085, 250 * MPa, 0.05
        raiz = math.sqrt(200e6 * fy * tf / tw)
        r = (tw / tf) ** 1.5
        esp = 0.33 * tw ** 2 * (1 + 3 * ln / d * r) * raiz / 1.1
        self.assertLess(rel(lf.enrugamento_alma(ln, d, tw, tf, fy), esp), 1e-9)
        ln2 = 0.1                                               # ln/d = 0,287 > 0,2
        esp2 = 0.33 * tw ** 2 * (1 + (4 * ln2 / d - 0.2) * r) * raiz / 1.1
        self.assertLess(rel(lf.enrugamento_alma(ln2, d, tw, tf, fy), esp2), 1e-9)
        esp3 = 0.66 * tw ** 2 * (1 + 3 * ln / d * r) * raiz / 1.1
        self.assertLess(rel(lf.enrugamento_alma(ln, d, tw, tf, fy, extremidade=False), esp3), 1e-9)
        # comprimento necessário devolve o menor ln que atende
        a = lf.comprimento_apoio_necessario(120.0, d, tw, tf, 0.0205, fy)
        self.assertLess(rel(min(lf.escoamento_local_alma(a, 0.0205, tw, fy), lf.enrugamento_alma(a, d, tw, tf, fy)),
                            120.0), 1e-6)

    def test_solda_c_pfeil_9_10_2(self):
        # [PF] 9.10.2 (p. 256–257): 2 filetes de 6,5 cm + 20 cm; x̄ = 1,30 cm (6,5²/33 = 1,28); Ip = 2095t cm⁴
        g = lf.solda_c(0.065, 0.20)
        self.assertLess(rel(g["xb"], 0.065 ** 2 / 0.33), 1e-9)
        self.assertLess(rel(g["Ip"] * 1e6, 2095.0), 0.01)

    def test_solda_consolo_pfeil_9_10_6(self):
        # [PF] 9.10.6 (p. 262–263): cordões 12,7 + 2·20 cm: ȳ = 7,59 cm; τv = 130/52,7 = 2,47 kN/cm.
        # O livro dá I = 2122t com 2·20·1,2²; correto 2·20·(10 − 7,59)² → I = 2297t (cálculo à mão).
        g = lf.solda_consolo(0.127, 0.20)
        self.assertLess(rel(g["yb"], 0.0759), 0.01)
        self.assertLess(rel(130 / g["Lt"] / 100, 2.47), 0.01)
        yb = 0.2 ** 2 / 0.527
        I = 0.127 * yb ** 2 + 2 * (0.2 ** 3 / 12 + 0.2 * (0.1 - yb) ** 2)
        self.assertLess(rel(g["I"], I), 1e-9)
        self.assertLess(rel(g["I"] * 1e6, 2297.3), 0.001)

    def test_alavanca_a_mao(self):
        # Kulak & Fisher cap. 17: T = 30 kN, t = 12, p = 75, b = 37, a = 31, db = 19,05, dh = 20,55, fy = 250
        T_, t, p, b, a, db, dh, fy = 30.0, 0.012, 0.075, 0.037, 0.031, 0.01905, 0.02055, 250 * MPa
        al = lf.alavanca(T_, t, p, b, a, db, dh, fy)
        b1, a1, delta = b - db / 2, a + db / 2, 1 - dh / p
        M = p * t * t * fy / (4 * 1.1)
        alfa = (T_ * b1 / M - 1) / delta
        self.assertTrue(0 < alfa < 1)
        Q = alfa * delta * M / a1
        self.assertLess(rel(al["Q"], Q), 1e-9)
        self.assertLess(rel(al["B"], T_ + Q), 1e-9)
        # mesma coisa pela forma fechada de Kulak: B = T[1 + δα/(1 + δα)·b'/a']
        self.assertLess(rel(al["B"], T_ * (1 + delta * alfa / (1 + delta * alfa) * b1 / a1)), 1e-9)
        self.assertLess(rel(al["Tmax"], M * (1 + delta) / b1), 1e-9)
        self.assertEqual(lf.alavanca(1.0, 0.03, p, b, a, db, dh, fy)["Q"], 0.0)   # chapa grossa: sem alavanca

    def test_recortes_gerdau_3_3(self):
        # [GD] 3.3.1 (E1, p. 22): W360x51 em W530x92 → Lh = 99, Lv = 26 mm
        v, s = lf._perfil("W360x51", "v"), lf._perfil("W530x92", "s")
        r = lf.recortes_viga_viga(v, s, 0.010)
        self.assertAlmostEqual(r["recorte_sup_c"] * 1e3, 99, delta=0.5)
        self.assertAlmostEqual(r["recorte_sup_h"] * 1e3, 26, delta=0.5)
        self.assertEqual(r["recorte_inf_h"], 0.0)
        # [GD] 3.3.2 (E2): W360x39 em W360x39 → dois recortes, Lh = 61, Lv = 21 mm
        v = s = lf._perfil("W360x39", "v")
        r = lf.recortes_viga_viga(v, s, 0.010)
        for k, esp in (("recorte_sup_c", 61), ("recorte_sup_h", 21), ("recorte_inf_c", 61), ("recorte_inf_h", 21)):
            self.assertAlmostEqual(r[k] * 1e3, esp, delta=0.5, msg=k)


class TestCantoneiraDupla(unittest.TestCase):
    def test_pfeil_9_10_2(self):
        res = T["cantoneira_dupla_alma"]["verificar"](PFEIL_9_10_2, {"V": 210.0})
        va = res["valores"]
        self.assertLess(rel(va["Fdy_apoio"], 35.0), 0.01)          # p. 256
        self.assertLess(rel(va["Fdx_apoio"], 46.2), 0.01)
        self.assertLess(rel(va["Fd_apoio"], 58.0), 0.01)
        self.assertLess(rel(ver(res, "parafusos no apoio: cisalhamento")["resistente"], 60.5), 0.01)
        self.assertLess(rel(va["Fc_esmagamento_cantoneira"], 91.0), 0.01)   # 2,4·d·t·fu/γa2
        self.assertLess(rel(va["solda_e1_mm"], 63.0), 0.01)          # p. 256: e1 = 76 − 13
        self.assertLess(rel(va["solda_q"] / 100, 5.75), 0.01)        # p. 257: 5,75 kN/cm
        self.assertLess(rel(ver(res, "cantoneiras: escoamento")["resistente"] / 2, 218.0), 0.01)   # p. 257
        self.assertLess(rel(va["Fr_cantoneiras_apoio"] / 2, 186.7), 0.01)   # p. 258
        self.assertLess(rel(va["W_liq_aba_cm3"], 45.4), 0.01)         # p. 258
        fl = ver(res, "cantoneiras: flexão")
        self.assertLess(rel(fl["solicitante"], 4.62), 0.01)          # Md = 105·4,4 = 462 kN·cm
        self.assertLess(rel(fl["solicitante"] / va["W_liq_aba_cm3"] * 100, 10.2), 0.01)   # σ = 10,2 kN/cm²
        # ruptura: o livro desconta d nominal (216 kN); o motor desconta dh + 2 mm (NBR 5.2.4.1, como [GD])
        rup = ver(res, "cantoneiras: ruptura")["resistente"] / 2
        self.assertLess(rel(rup, 0.6 * 400e3 * (0.20 - 3 * 0.019375) * 0.008 / 1.35), 1e-6)
        # o motor verifica o metal base da alma (tw = 6,3) junto às soldas das 2 cantoneiras; o livro não:
        mb = ver(res, "alma da viga: metal base")
        self.assertLess(rel(mb["solicitante"], 2 * va["solda_q"]), 1e-9)
        self.assertFalse(mb["ok"])

    def test_gerdau_lcpp_30b_sem_recorte(self):
        # [GD] p. 33, Vd sem recortes: W360x32,9 → 265; W360x51,0 → 329 (pressão de contato na alma, 2,4·d·tw·fu);
        # W360x79,0 → 339 (colapso por rasgamento das cantoneiras; 342,3 com t = 6,35 mm, R21 do CBCA.md)
        for perfil, esp, gov in (("W360x32.9", 265, "pressão de contato na alma"),
                                 ("W360x51", 329, "pressão de contato na alma"),
                                 ("W360x79", 339, "cantoneiras: colapso por rasgamento")):
            V, nome, _ = capacidade("cantoneira_dupla_alma", dict(LCPP_30B, perfil_viga=perfil))
            self.assertLess(rel(V, esp), 0.01, (perfil, V, nome))
            self.assertTrue(nome.startswith(gov), (perfil, nome))

    def test_gerdau_exemplo_3_3_1_viga_viga(self):
        # [GD] 3.3.1 (p. 22): W360x51 em W530x92, Vd = 202 kN, um recorte → atende (tabela: 266)
        d = {k: v for k, v in LCPP_30B.items() if k != "apoio"}
        res = T["viga_viga_recorte"]["verificar"](dict(d, perfil_viga="W360x51"), {"V": 202.0})
        self.assertTrue(res["ok"], [v for v in res["verificacoes"] if not v["ok"]])
        self.assertAlmostEqual(res["valores"]["recorte_sup_c_mm"], 99, delta=0.5)
        self.assertAlmostEqual(res["valores"]["recorte_sup_h_mm"], 26, delta=0.5)
        nomes = [v["nome"] for v in res["verificacoes"]]
        for n in ("alma da viga recortada: colapso por rasgamento", "viga recortada (superior): início de escoamento",
                  "viga recortada (superior): flambagem local"):
            self.assertTrue(any(x.startswith(n) for x in nomes), n)
        cope = [r for r in res["geometria"]["recortes"] if r["tipo"] == "cope"]
        self.assertEqual(len(cope), 1)
        self.assertEqual(cope[0]["parametros"]["posicao"], "topo")

    def test_bloco_alma_recortada_a_mao(self):
        # 6.5.6: W360x51, recorte 26 mm; Agv = (225 − 25,6)·7,2; Anv = Agv − 2,5·22,55·7,2; Ant = (35 − 11,275)·7,2
        d = {k: v for k, v in LCPP_30B.items() if k != "apoio"}
        res = T["viga_viga_recorte"]["verificar"](dict(d, perfil_viga="W360x51"), {"V": 100.0})
        dct = res["valores"]["recorte_sup_h_mm"] / 1e3
        tw, dn = 0.0072, 0.02255
        Agv = (0.225 - dct) * tw
        esp = lg.colapso_rasgamento(Agv, Agv - 2.5 * dn * tw, (0.035 - dn / 2) * tw, 345e3, 450e3)
        self.assertLess(rel(ver(res, "alma da viga recortada")["resistente"], esp), 1e-9)

    def test_flambagem_recorte_a_mao(self):
        # AISC 13ª ed. p. 9-7, um recorte: W360x32,9, c = 170, dc = 30 (Fcr < fy) e escoamento fy·W/γa1
        d = dict(LCPP_30B, perfil_viga="W360x32.9", recorte_sup_h=30, recorte_sup_c=170)
        res = T["cantoneira_dupla_alma"]["verificar"](d, {"V": 100.0})
        h0, c, dv, tw = 0.349 - 0.030, 0.170, 0.349, 0.0058
        f = 2 * c / dv
        k = 2.2 * (h0 / c) ** 1.65 if c / h0 <= 1 else 2.2 * h0 / c
        Fcr = min(345e3, 26210 * 6.894757e3 * (tw / h0) ** 2 * f * k)
        W = res["valores"]["W_recorte_superior_cm3"] / 1e6
        self.assertLess(rel(ver(res, "viga recortada (superior): flambagem")["resistente"], Fcr * W / 1.1), 1e-9)
        self.assertLess(rel(ver(res, "viga recortada (superior): flambagem")["solicitante"], 100 * 0.180), 1e-9)
        # W da seção T (mesa 127 x 8,5 + alma 5,8 x (319 − 8,5)) à mão
        A1, A2 = 0.127 * 0.0085, tw * (h0 - 0.0085)
        yc = (A1 * 0.00425 + A2 * (0.0085 + (h0 - 0.0085) / 2)) / (A1 + A2)
        I = (0.127 * 0.0085 ** 3 / 12 + A1 * (yc - 0.00425) ** 2 + tw * (h0 - 0.0085) ** 3 / 12
             + A2 * (0.0085 + (h0 - 0.0085) / 2 - yc) ** 2)
        self.assertLess(rel(W, I / max(yc, h0 - yc)), 1e-9)

    def test_dois_recortes_flambagem_a_mao(self):
        d = dict(LCPP_30B, perfil_viga="W360x39", recorte_sup_h=21, recorte_sup_c=61, recorte_inf_h=21,
                 recorte_inf_c=61)
        res = T["cantoneira_dupla_alma"]["verificar"](d, {"V": 100.0})
        h0, c, dv, tw = 0.353 - 0.042, 0.061, 0.353, 0.0065
        Fcr = min(345e3, 0.62 * math.pi * 200e6 * tw ** 2 / (c * h0) * (3.5 - 7.5 * 0.021 / dv))
        self.assertLess(rel(ver(res, "viga recortada (dois recortes): flambagem")["resistente"],
                            Fcr * tw * h0 ** 2 / 6 / 1.1), 1e-9)

    def test_tracao_com_alavanca(self):
        res = T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W360x51"), {"V": 150.0, "N": 60.0})
        al = res["valores"]["alavanca"]
        self.assertAlmostEqual(al["B"] - al["Q"], 60.0 / 6)
        it = ver(res, "parafusos no apoio: tração + cisalhamento")
        Rs = res["valores"]["Fd_apoio"]
        esp = (al["B"] / lg.tracao_parafuso(0.01905, 825e3)) ** 2 + \
            (Rs / lg.cisalhamento_parafuso(0.01905, 825e3, True, True)) ** 2
        self.assertLess(rel(it["solicitante"], esp), 1e-9)
        # com N > 0 a borda da ponta da alma entra na pressão de contato (lf = 35 − 10,275 mm)
        self.assertLess(rel(ver(res, "pressão de contato na alma")["resistente"],
                            lg.pressao_contato(0.035 - 0.010275, 0.0072, 0.01905, 450e3)), 1e-9)

    def test_gerdau_exemplo_3_3_2_dois_recortes(self):
        # [GD] 3.3.2 (p. 22): W360x39 em W360x39, Vd = 245 kN, LCSP (soldada), dois recortes Lh = 61, Lv = 21
        d = {k: v for k, v in LCPP_30B.items() if k != "apoio"}
        res = T["viga_viga_recorte"]["verificar"](dict(d, perfil_viga="W360x39", perfil_suporte="W360x39",
                                                       ligacao_viga="soldada"), {"V": 245.0})
        self.assertEqual(len([r for r in res["geometria"]["recortes"] if r["tipo"] == "cope"]), 2)
        self.assertTrue(any(v["nome"].startswith("viga recortada (dois recortes)") for v in res["verificacoes"]))
        self.assertTrue(any(v["nome"].startswith("solda cantoneira") for v in res["verificacoes"]))


class TestChapaSimples(unittest.TestCase):
    BASE = dict(perfil_viga="W310x38.7", perfil_suporte="W310x97", apoio="mesa_pilar", d=19.05, s=75, e_v=40,
                a=75, e_h=40, t_chapa=6.3)

    def test_gerdau_r20_corte_dos_parafusos(self):
        # [GD] 5.3 (p. 96) / CBCA.md R20: 0,4·Ab·fub·C/1,35 com C = 0,88 e 1,75 → 61,3 e 121,9 kN (tabela: 60 e 120)
        for n, esp in ((2, 61.3), (3, 121.9)):
            res = T["chapa_simples_alma"]["verificar"](dict(self.BASE, n=n, dist_topo_furo=80), {"V": 50.0})
            self.assertLess(rel(ver(res, "parafusos: cisalhamento")["resistente"], esp), 0.01, n)

    def test_verificacoes_a_mao(self):
        res = T["chapa_simples_alma"]["verificar"](dict(self.BASE, n=3, dist_topo_furo=80), {"V": 100.0})
        t, Lc, dn = 0.0063, 0.230, 0.02255
        self.assertLess(rel(ver(res, "chapa: escoamento")["resistente"], 0.6 * 250e3 * Lc * t / 1.1), 1e-9)
        self.assertLess(rel(ver(res, "chapa: ruptura")["resistente"], 0.6 * 400e3 * (Lc - 3 * dn) * t / 1.35), 1e-9)
        self.assertLess(rel(ver(res, "chapa: flexão V·a (início")["solicitante"], 100 * 0.075), 1e-9)
        self.assertLess(rel(ver(res, "chapa: flexão V·a (início")["resistente"], 250e3 * t * Lc ** 2 / 6 / 1.1), 1e-9)
        self.assertLess(rel(ver(res, "solda chapa–apoio")["solicitante"], 100 / (2 * Lc)), 1e-9)
        Agv = (0.150 + 0.040) * t
        self.assertLess(rel(ver(res, "chapa: colapso")["resistente"],
                            lg.colapso_rasgamento(Agv, Agv - 2.5 * dn * t, (0.040 - dn / 2) * t, 250e3, 400e3)), 1e-9)

    def test_com_normal_metodo_elastico(self):
        res = T["chapa_simples_alma"]["verificar"](dict(self.BASE, n=3, dist_topo_furo=80), {"V": 90.0, "N": 30.0})
        R = math.hypot(30.0, 10.0 + 90 * 0.075 * 0.075 / (2 * 0.075 ** 2))
        self.assertLess(rel(ver(res, "parafusos: cisalhamento")["solicitante"], R), 1e-9)

    def test_ductilidade(self):
        # AISC/[GD] 5.1.2: basta a chapa OU a alma ter t ≤ db/2 + 2 mm; W360x122 (tw = 13) com chapa de 16 reprova
        ok = T["chapa_simples_alma"]["verificar"](dict(self.BASE, n=3, dist_topo_furo=80, t_chapa=16), {"V": 50.0})
        self.assertTrue(ver(ok, "ductilidade")["ok"])
        res = T["chapa_simples_alma"]["verificar"](dict(self.BASE, perfil_viga="W360x122", n=3, dist_topo_furo=80,
                                                        t_chapa=16), {"V": 50.0})
        self.assertFalse(ver(res, "ductilidade")["ok"])
        self.assertFalse(res["ok"])


class TestChapaExtremidade(unittest.TestCase):
    def test_verificacoes_a_mao(self):
        # W250x28,4 em W310x97 (mesa), 4 x 3/4", chapa 200 x 6,3, g = 120, Vd = 79, Nd = 9,3 ([GD] 4.3, E3)
        d = dict(perfil_viga="W250x28.4", perfil_suporte="W310x97", n=2, s=75, e_v=40, dist_topo_chapa=30)
        res = T["chapa_extremidade_flexivel"]["verificar"](d, {"V": 79.0, "N": 9.3})
        self.assertTrue(res["ok"], [v for v in res["verificacoes"] if not v["ok"]])
        t, Lp, dn = 0.0063, 0.155, 0.02255
        self.assertLess(rel(ver(res, "parafusos: cisalhamento")["solicitante"], 79 / 4), 1e-9)
        self.assertLess(rel(ver(res, "chapa: escoamento")["resistente"], 2 * 0.6 * 250e3 * Lp * t / 1.1), 1e-9)
        self.assertLess(rel(ver(res, "chapa: ruptura")["resistente"], 2 * 0.6 * 400e3 * (Lp - 2 * dn) * t / 1.35), 1e-9)
        self.assertLess(rel(ver(res, "alma da viga: escoamento")["resistente"], 0.6 * 345e3 * Lp * 0.0064 / 1.1), 1e-9)
        self.assertLess(rel(ver(res, "solda chapa–alma")["solicitante"], math.hypot(79, 9.3) / (2 * Lp)), 1e-9)
        al = res["valores"]["alavanca"]
        self.assertLess(rel(al["b1"], (0.120 - 0.0064) / 2 - 0.01905 / 2), 1e-9)
        self.assertLess(rel(ver(res, "parafusos: tração")["solicitante"], 9.3 / 4 + al["Q"]), 1e-9)

    def test_largura_maior_que_mesa_reprova(self):
        d = dict(perfil_viga="W250x28.4", perfil_suporte="W250x28.4", n=2, dist_topo_chapa=30)
        res = T["chapa_extremidade_flexivel"]["verificar"](d, {"V": 50.0})
        self.assertFalse(ver(res, "chapa: largura na mesa do pilar")["ok"])


class TestApoio(unittest.TestCase):
    def test_consolo_enrijecido_pfeil_9_10_6(self):
        # [PF] 9.10.6 (p. 261–263): W360x32,9, Rd = 130 kN, MR250, t0 = 12,5, b = 100, cordões 12,7 + 2·20, E60
        d = dict(perfil_viga="W360x32.9", perfil_suporte=dict(d=300, bf=300, tw=8, tf=9.5), aco_viga="A-36",
                 aco_suporte="A-36", aco_cantoneira="A-36", assento="enrijecido", t_enrijecedor=12.5,
                 b_enrijecedor=100, h_enrijecedor=200, largura_assento=127, t_assento=12.5, b_solda=5, eletrodo="E60")
        res = T["apoio_cantoneira"]["verificar"](d, {"V": 130.0})
        self.assertLess(rel(ver(res, "enrijecedor: escoamento")["resistente"], 341.0), 0.01)   # 20·1,25·0,6·25/1,10
        self.assertLess(rel(res["valores"]["solda_yb_mm"], 75.9), 0.01)
        self.assertLess(rel(res["valores"]["solda_tv"] / 100, 2.47), 0.01)
        # τh = V·e·ȳ/I com e = b − a'/2 (eq. 9.5) e o I correto (o livro: 3,25/t com I = 2122t)
        e = res["valores"]["e_mm"] / 1e3
        g = lf.solda_consolo(0.127, 0.2)
        self.assertLess(rel(res["valores"]["solda_th"], 130 * e * g["yb"] / g["I"]), 1e-9)
        self.assertLess(rel(e, 0.100 - min(max(res["valores"]["a_necessario_mm"] / 1e3, 0.0205), 0.090) / 2), 1e-9)
        # enrugamento (5.7.4) com ln = 90 mm reprova por pouco; o livro achou a' = 40 mm (não reproduzido:
        # pela fórmula da extremidade 0,33·tw²…, a' ≈ 96 mm)
        self.assertFalse(ver(res, "alma da viga: enrugamento")["ok"])
        self.assertTrue(ver(res, "alma da viga: escoamento local")["ok"])

    def test_cantoneira_a_mao(self):
        d = dict(perfil_viga="W310x38.7", perfil_suporte="W310x97", aco_viga="A-36", cantoneira="L102x152x19",
                 comprimento=150, folga=10)
        res = T["apoio_cantoneira"]["verificar"](d, {"V": 120.0})
        k, tw, tf = 0.0097 + 0.010, 0.0058, 0.0097
        ln = 0.102 - 0.010
        self.assertLess(rel(ver(res, "alma da viga: escoamento local")["resistente"],
                            1.10 * (2.5 * k + ln) * 250e3 * tw / 1.1), 1e-9)
        a1 = max(res["valores"]["a_necessario_mm"] / 1e3, k)
        eA = max(0.010 + a1 / 2 - 0.019 - 0.010, 0.0)
        fl = ver(res, "assento: flexão")
        self.assertLess(rel(fl["resistente"], 0.150 * 0.019 ** 2 / 4 * 250e3 / 1.1), 1e-9)
        self.assertAlmostEqual(fl["solicitante"], 120 * eA, places=9)
        self.assertLess(rel(ver(res, "parafusos no apoio: cisalhamento")["solicitante"], 120 / 4), 1e-9)
        self.assertTrue(res["ok"], [v for v in res["verificacoes"] if not v["ok"]])

    def test_cantoneira_soldada(self):
        d = dict(perfil_viga="W310x38.7", perfil_suporte="W310x97", aco_viga="A-36", cantoneira="L102x152x12.7",
                 ligacao_apoio="soldada", b_solda=8)
        res = T["apoio_cantoneira"]["verificar"](d, {"V": 120.0})
        e = res["valores"]["e_mm"] / 1e3
        self.assertLess(rel(ver(res, "solda cantoneira–apoio")["solicitante"],
                            math.hypot(120 / 0.304, 6 * 120 * e / (2 * 0.152 ** 2))), 1e-9)


class TestGeometria(unittest.TestCase):
    """Parafusos dentro das chapas/cantoneiras e bordas mínimas (tabela 14) respeitadas."""

    def _local(self, ch, ponto):
        o = ch["origem"]
        dlt = [ponto[i] - o[i] for i in range(3)]
        return (sum(dlt[i] * ch["u"][i] for i in range(3)), sum(dlt[i] * ch["v"][i] for i in range(3)),
                sum(dlt[i] * ch["normal"][i] for i in range(3)))

    def _parafusos_na_chapa(self, res, emin_mm):
        ch = res["geometria"]["chapas"][0]
        larg, alt = ch["contorno"][2]
        n_dentro = 0
        for p in res["geometria"]["parafusos"]:
            # ponto do fuste no plano médio da chapa
            o, e = p["origem"], p["eixo"]
            for s_ in (0.0, ch["espessura"] / 2, p["pega"] - ch["espessura"] / 2):
                q = [o[i] + s_ * e[i] for i in range(3)]
                a, b, c = self._local(ch, q)
                if abs(c - ch["espessura"] / 2) < 1e-6:   # plano médio (origem do contrato na face, G6)
                    break
            else:
                continue
            n_dentro += 1
            self.assertGreaterEqual(a, emin_mm - 1e-6)
            self.assertLessEqual(a, larg - emin_mm + 1e-6)
            self.assertGreaterEqual(b, emin_mm - 1e-6)
            self.assertLessEqual(b, alt - emin_mm + 1e-6)
        return n_dentro

    def test_chapa_simples(self):
        res = T["chapa_simples_alma"]["verificar"](dict(TestChapaSimples.BASE, n=3, dist_topo_furo=80), {"V": 50.0})
        self.assertEqual(self._parafusos_na_chapa(res, 32.0), 3)
        ch = res["geometria"]["chapas"][0]
        self.assertEqual([abs(x) for x in ch["normal"]], [0, 1, 0])   # chapa no plano da alma
        self.assertEqual(len(res["geometria"]["soldas"]), 2)

    def test_chapa_extremidade(self):
        d = dict(perfil_viga="W250x28.4", perfil_suporte="W310x97", n=2, dist_topo_chapa=30)
        res = T["chapa_extremidade_flexivel"]["verificar"](d, {"V": 50.0})
        self.assertEqual(self._parafusos_na_chapa(res, 32.0), 4)

    def test_cantoneira_dupla_furos_nas_abas(self):
        res = T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W360x51"), {"V": 100.0})
        g = res["geometria"]
        self.assertEqual(len(g["cantoneiras"]), 2)
        self.assertEqual(len(g["parafusos"]), 9)          # 6 no apoio + 3 na alma
        c = g["cantoneiras"][0]
        z0, Lc = c["origem"][2], c["comprimento"]
        b1, b2 = c["abas"]
        for p in g["parafusos"]:
            z = p["origem"][2]
            self.assertGreaterEqual(z - z0, 32.0 - 1e-6)            # borda vertical ≥ 32 mm (tab. 14, serra)
            self.assertLessEqual(z - z0, Lc - 32.0 + 1e-6)
            if p["eixo"] == [-1, 0, 0]:                              # aba no apoio
                y = abs(p["origem"][1]) - 7.2 / 2
                self.assertGreaterEqual(b2 - y, 26.0 - 1e-6)           # ponta da aba (laminada)
            else:                                                     # aba na viga
                x = p["origem"][0] - c["origem"][0]
                self.assertGreaterEqual(b1 - x, 26.0 - 1e-6)
                self.assertAlmostEqual(p["pega"], 7.2 + 2 * 6.35)
        # alma da viga entre as cantoneiras; nó na face: x da face = tw da viga suporte / 2
        self.assertAlmostEqual(c["origem"][0], 10.2 / 2)
        self.assertEqual(json.loads(json.dumps(res))["ok"], res["ok"])

    def test_apoio_parafusos_na_aba_vertical(self):
        d = dict(perfil_viga="W310x38.7", perfil_suporte="W310x97", aco_viga="A-36", cantoneira="L102x152x12.7")
        res = T["apoio_cantoneira"]["verificar"](d, {"V": 100.0})
        c = res["geometria"]["cantoneiras"][0]
        topo = c["origem"][2]
        for p in res["geometria"]["parafusos"]:
            prof = topo - p["origem"][2]
            self.assertGreaterEqual(prof, 12.7 + 1.5 * 19.05 - 1e-6)
            self.assertLessEqual(prof, 152 - 26 + 1e-6)
            self.assertLessEqual(abs(p["origem"][1]), c["comprimento"] / 2 - 32 + 1e-6)
        self.assertEqual(len(res["geometria"]["cantoneiras"]), 2)    # assento + superior


class TestContrato(unittest.TestCase):
    def test_registro(self):
        for tipo, t in T.items():
            self.assertTrue({"nome", "familia", "liga", "descricao", "entradas", "verificar", "sugerir"} <= set(t), tipo)
            self.assertEqual(t["familia"], "flexivel")
            for k, c in t["entradas"].items():
                self.assertTrue({"rotulo", "unidade", "padrao"} <= set(c), (tipo, k))
                self.assertLessEqual(len(c["rotulo"]), 28, (tipo, k))
                self.assertIn(c["unidade"], ("mm", "kN", "MPa", "-"))

    def test_tipos_ligacao_e_despacho(self):
        tipos = {t["tipo"]: t for t in lg.tipos_ligacao()}
        self.assertTrue(set(T) <= set(tipos))
        res = lg.verificar_ligacao("cantoneira_dupla_alma", dict(LCPP_30B, perfil_viga="W360x51"), {"V": 100})
        self.assertTrue({"ok", "taxa", "verificacoes", "avisos", "geometria"} <= set(res))
        for v in res["verificacoes"]:
            self.assertTrue({"nome", "item", "solicitante", "resistente", "unidade", "taxa", "ok"} <= set(v))
            self.assertTrue(v["item"])
        json.dumps(lg.tipos_ligacao())
        json.dumps(res)

    def test_erros_de_entrada(self):
        with self.assertRaises(ValueError) as cm:
            T["cantoneira_dupla_alma"]["verificar"]({}, {"V": 10})
        self.assertIn("perfil_viga", str(cm.exception))
        with self.assertRaises(ValueError):
            T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W999x1"), {"V": 10})
        with self.assertRaises(ValueError):
            T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W360x51", cantoneira="XYZ"), {"V": 1})
        with self.assertRaises(ValueError) as cm:
            T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W360x51", s=20), {"V": 1})
        self.assertIn("geometria impossível", str(cm.exception))
        with self.assertRaises(ValueError):
            T["chapa_simples_alma"]["verificar"](dict(TestChapaSimples.BASE, n=3, apoio="teto"), {"V": 1})

    def test_esforcos_de_momento_geram_aviso(self):
        res = T["cantoneira_dupla_alma"]["verificar"](dict(LCPP_30B, perfil_viga="W360x51"), {"V": 50, "M": 3})
        self.assertTrue(any("ignorados" in a for a in res["avisos"]))

    def test_sugerir_atende(self):
        perfis = {"viga": "W360x51", "pilar": "W310x97", "apoio": "mesa_pilar"}
        for tipo, V in (("cantoneira_dupla_alma", 200), ("chapa_simples_alma", 150),
                        ("chapa_extremidade_flexivel", 150), ("apoio_cantoneira", 150)):
            dados = T[tipo]["sugerir"](perfis, {"V": V})
            res = T[tipo]["verificar"](dados, {"V": V})
            self.assertTrue(res["ok"], (tipo, dados, [v["nome"] for v in res["verificacoes"] if not v["ok"]]))
        dados = T["viga_viga_recorte"]["sugerir"]({"viga": "W360x51", "suporte": "W530x92"}, {"V": 200})
        self.assertTrue(T["viga_viga_recorte"]["verificar"](dados, {"V": 200})["ok"])

    def test_sugerir_aumenta_parafusos(self):
        perfis = {"viga": "W530x92", "pilar": "W310x97"}
        n1 = T["cantoneira_dupla_alma"]["sugerir"](perfis, {"V": 100})["n"]
        n2 = T["cantoneira_dupla_alma"]["sugerir"](perfis, {"V": 450})["n"]
        self.assertGreaterEqual(n2, n1)


if __name__ == "__main__":
    unittest.main()
