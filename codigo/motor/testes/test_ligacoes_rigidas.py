"""Testes de aco/ligacoes_rigidas.py (ligações rígidas e emendas, NBR 8800:2008). Tolerância 1 %.

Fontes (ver docs/v3/ligacoes/IMPL-RIGIDAS.md):
  [CBCA]  Baião Filho & Viana Silva, "Ligações para Estruturas de Aço", Gerdau, 7ª ed., 2018, cap. 6 (LMPA),
          p. 113–132; reproduções em docs/v3/ligacoes/CBCA.md §3 (R11–R18). As tabelas LMPA/LMSS só batem com
          fy = 35 kN/cm² (aço AR-350 aqui) e com o parafuso de 1" calculado com Ab de d = 25 mm (4,909 cm²).
  [Pfeil] Pfeil & Pfeil, "Estruturas de Aço — dimensionamento prático", 8ª ed., 9.10.1 (emenda de viga,
          p. 253–255) e 6.5.9 (forças localizadas, p. 195–196).
  [Kulak] Kulak, Fisher & Struik, 2ª ed., cap. 17 (T-stub, Struik–de Back) — cálculo à mão abaixo.
"""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural.aco import ligacoes as lg  # noqa: E402
from prossen_estrutural.aco import ligacoes_rigidas as lr  # noqa: E402

MPa = 1000.0
VS600 = dict(d=600, bf=300, tw=8, tf=12.5, nome="VS600x95")   # Pfeil 9.10.1 (perfil soldado, r = 0)


def rel(a, b):
    return abs(a - b) / abs(b)


def ver(res, prefixo):
    achados = [v for v in res["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"verificação '{prefixo}' ausente: {[v['nome'] for v in res['verificacoes']]}"
    return achados[0]


class TestTStub(unittest.TestCase):
    """Cálculo à mão (Kulak eqs. 17.10–17.12 com resistências da NBR):
    p = 100, t = 16, b = 40, a = 40 mm, d = 19,05 (3/4"), furo 20,55 mm, fy = 250 MPa, T = 60 kN.
      b' = 40 − 9,525 = 30,475 mm;  a' = min(40; 1,25·40) + 9,525 = 49,525 mm;  δ = 1 − 20,55/100 = 0,7945
      Mp = 100·16²/4·250/1,1 = 1 454 545 N·mm = 1,45455 kN·m
      α = (T·b'/Mp − 1)/δ = (1,82850/1,45455 − 1)/0,7945 = 0,32359
      Q = (T·b' − Mp)/a' = (1,82850 − 1,45455)/0,049525 = 7,551 kN;  B = 67,551 kN
      T_chapa = Mp(1 + δ)/b' = 1,45455·1,7945/0,030475 = 85,65 kN
      Ft,Rd = 0,75·(π·19,05²/4)·825/1,35 = 130,6 kN (CBCA tab. 1); Ft,Rd·b' > Mp →
      T_paraf = (Ft,Rd·a' + Mp)/(a' + b') = (130,6·0,049525 + 1,45455)/0,080 = 99,03 kN."""

    def setUp(self):
        self.FtRd = lg.tracao_parafuso(0.01905, 825 * MPa)
        self.r = lr.tstub(60.0, 0.100, 0.016, 0.040, 0.040, 0.01905, 0.02055, 250 * MPa, self.FtRd)

    def test_ft_rd_cbca(self):
        self.assertLess(rel(self.FtRd, 130.6), 0.001)

    def test_valores(self):
        r = self.r
        self.assertAlmostEqual(r["b1"], 0.030475, places=9)
        self.assertAlmostEqual(r["a1"], 0.049525, places=9)
        self.assertAlmostEqual(r["delta"], 0.7945, places=6)
        self.assertLess(rel(r["Mp"], 1.454545), 1e-5)
        self.assertLess(rel(r["alfa"], 0.32359), 1e-3)
        self.assertLess(rel(r["Q"], 7.551), 1e-3)
        self.assertLess(rel(r["B"], 67.551), 1e-3)
        self.assertLess(rel(r["T_chapa"], 85.65), 1e-3)
        self.assertLess(rel(r["T_parafuso"], 99.03), 1e-3)
        self.assertAlmostEqual(r["Tcap"], min(r["T_chapa"], r["T_parafuso"]))

    def test_chapa_espessa_sem_alavanca(self):
        r = lr.tstub(60.0, 0.100, 0.040, 0.040, 0.040, 0.01905, 0.02055, 250 * MPa, self.FtRd)
        self.assertEqual(r["Q"], 0.0)
        self.assertAlmostEqual(r["T_parafuso"], self.FtRd)   # Ft,Rd·b' ≤ Mp: parafuso sem Q

    def test_consistencia_capacidade(self):
        # chapa de 22 mm (0 < α < 1 na capacidade do parafuso): B = T + Q = Ft,Rd na capacidade
        c = lr.tstub(0.0, 0.100, 0.022, 0.040, 0.040, 0.01905, 0.02055, 250 * MPa, self.FtRd)
        self.assertLess(c["T_parafuso"], c["T_chapa"])
        r = lr.tstub(c["T_parafuso"], 0.100, 0.022, 0.040, 0.040, 0.01905, 0.02055, 250 * MPa, self.FtRd)
        self.assertTrue(0 < r["alfa"] < 1)
        self.assertLess(rel(r["B"], self.FtRd), 1e-9)

    def test_geometria_impossivel(self):
        with self.assertRaises(ValueError):
            lr.tstub(10.0, 0.1, 0.016, 0.009, 0.04, 0.01905, 0.02055, 250 * MPa, self.FtRd)


class TestChapaTopoCBCA(unittest.TestCase):
    """CBCA LMPA, W460×89,0 (p. 113–114 e tabela p. 132): chapa 31,5 × 220 × 563, 6 × 1" A325N, pf = 40,
    de = 40, gabarito 110, sobra 20 mm. Md,max = 401 kN·m (R17) e Nd = 360 kN no caso 2 (R18)."""
    DADOS = dict(perfil_viga="W460x89", aco_viga="AR-350", d_mm=25.4, tp=31.5, bp=220, g=110, pf=40, de=40,
                 borda_laminada=True)

    def test_r17_tracao_nos_parafusos_401(self):
        # Hipótese do manual (CBCA.md §3.2): Ab de d = 25 mm (4,909 cm²) e fub = 82,5 → T = 225,0 kN
        FtRd = lg.tracao_parafuso(0.025, 825 * MPa)
        self.assertLess(rel(FtRd, 225.0), 0.001)
        linhas, zb, zt = lr._linhas_chapa(0.463, 0.0177, 0.0105, 0.040, 0.040, 0.110, 0.220, "superior",
                                          0.020, 0.025, False, 0.0)
        zc = -(0.463 / 2 - 0.0177 / 2)
        tr = [L for L in linhas if L["z"] > 0]
        for L in tr:
            L["h"] = L["z"] - zc
            L["Tcap"] = lr.tstub(0.0, L["p"], 0.0315, L["b"], L["a"], 0.025, 0.0265, 250 * MPa, FtRd)["Tcap"]
            self.assertAlmostEqual(L["Tcap"], FtRd)          # chapa de 31,5: sem efeito alavanca
        self.assertAlmostEqual(sum(L["h"] for L in tr), 2 * (0.463 - 0.0177))   # Σh = 2(H − tf)
        self.assertLess(rel(lr.momento_resistente_linhas(tr), 401.0), 0.01)

    def test_r18_caso2_normal(self):
        # caso 2: 0,8·Mmax com Nd = 2·(0,2·Mmax)/(H − tf) = 360 kN → mesmo momento nos parafusos (401)
        self.assertLess(rel(2 * 0.2 * 401 / (0.463 - 0.0177), 360.0), 0.01)
        self.assertLess(rel(lr.momento_efetivo(0.8 * 401, 360.2, 0.463 - 0.0177), 401.0), 0.01)
        r = lr.verificar("chapa_topo_estendida", self.DADOS, {"M": 0.8 * 401, "N": 360.2, "V": 102})
        self.assertLess(rel(r["intermediarios"]["Mef"], 401.0), 0.01)

    def test_motor_com_1_polegada(self):
        # d = 25,4 mm (Ab = 5,07 cm²): o manual daria 413,9 (CBCA.md R17); o T-stub com bp,ef = bf + 25 = 217
        # mostra alavanca pequena (Ft,Rd·b' > Mp) e reduz para ≈ 409 kN·m.
        r = lr.verificar("chapa_topo_estendida", self.DADOS, {"M": 290, "N": 245, "V": 102})
        MRd = r["intermediarios"]["MRd"]
        self.assertLess(MRd, 413.9)
        self.assertGreater(MRd, 401.0)
        self.assertTrue(r["ok"], [v for v in r["verificacoes"] if not v["ok"]])
        self.assertLess(rel(r["intermediarios"]["altura_chapa"], 563.0), 1e-9)   # CBCA p. 114

    def test_inversao_simetrica_623(self):
        r = lr.verificar("chapa_topo_estendida", dict(self.DADOS, estendida="ambas"), {"M": -290, "V": 102})
        self.assertAlmostEqual(r["intermediarios"]["altura_chapa"], 623.0)   # CBCA p. 114: 8 × 1", 623 mm
        self.assertEqual(len(r["geometria"]["parafusos"]), 8)

    def test_r15_solda_mesas_metal_base(self):
        # W150×13 (bf = 100, H − tf = 143,1 mm), S1 = 5 mm, fy = 35: 2·0,6·b·fy·bf·(H − tf)/1,1 = 27,3 ≈ 27
        mb = lg.resistencia_filete(0.005, 485 * MPa, 350 * MPa, 450 * MPa)
        self.assertLess(rel(2 * 0.100 * 0.1431 * mb["metal_base"], 27.0), 0.015)
        self.assertLess(mb["metal_solda"], mb["metal_base"])   # o motor verifica também o metal da solda

    def test_r12_momento_viga_w610(self):
        r = lr.verificar("chapa_topo_estendida", dict(perfil_viga="W610x155", aco_viga="AR-350", d_mm=25.4,
                                                       tp=31.5), {"M": 100})
        self.assertLess(rel(ver(r, "viga: momento resistente")["resistente"], 1511.0), 0.01)


class TestEmendaVigaPfeil(unittest.TestCase):
    """Pfeil 9.10.1 (p. 253–255): VS 600×95, MR250, A325 rosca no plano, Vd = 168 kN, Md = 328 kN·m."""
    DADOS = dict(perfil_viga=VS600, aco_viga="MR-250", d_mesa_mm=22.225, t_tala_mesa=12.5, n_por_linha_mesa=3,
                 s_mesa=70, e1_mesa=40, e_mesa_perfil=40, d_alma_mm=15.875, t_tala_alma=6.3, h_tala_alma=510,
                 n_linhas_alma=4, n_colunas_alma=2, s_alma=150, sh_alma=70, e_emenda_alma=30, e_tala_alma=30,
                 folga=0)

    @classmethod
    def setUpClass(cls):
        Md = 0.5 * 2889e-6 * 250 * MPa / 1.1
        cls.Md = Md
        cls.r = lr.verificar("emenda_viga", cls.DADOS, {"M": Md, "V": 168})
        cls.i = cls.r["intermediarios"]

    def test_esforco_de_projeto(self):
        self.assertLess(rel(self.Md, 328.0), 0.01)

    def test_divisao_do_momento(self):
        self.assertLess(rel(self.i["Mw"], 55.9), 0.01)
        self.assertLess(rel(self.i["Mf"], 272.1), 0.01)
        self.assertLess(rel(self.i["Ft"], 453.0), 0.01)   # 272,1/0,60

    def test_mesa(self):
        self.assertLess(rel(ver(self.r, "tala da mesa: ruptura")["resistente"], 920.6), 0.01)
        # 7/8" A325 corte simples, NBR 0,4·Ab·fub/γa2 = 94,8 kN (CBCA tab. 2); Pfeil usa 0,7·0,6 = 0,42
        self.assertLess(rel(self.i["FvRd_mesa"], 94.8), 0.01)

    def test_alma(self):
        self.assertAlmostEqual(self.i["sum_r2"] * 1e4, 2348.0, places=6)   # Σx² + Σy² (cm²)
        self.assertLess(rel(self.i["M0"], self.i["Mw"] + 168 * 0.065), 1e-9)   # a = 30 + 70/2 = 65 mm
        self.assertLess(rel(self.i["M0"], 55.9 + 168 * 0.065), 0.01)       # livro: 55,2 + … (erro de cópia)
        self.assertLess(rel(self.i["Fw"], 71.2), 0.01)                     # livro: 70,5 com M0 = 66,1; cita 71,2
        self.assertLess(rel(ver(self.r, "talas da alma: flexão")["resistente"], 186.0), 0.01)
        self.assertLess(rel(ver(self.r, "alma do perfil: ruptura")["resistente"], 711.0), 0.01)
        self.assertLess(rel(2 * self.i["FvRd_alma"], 2 * 48.4), 0.01)     # Pfeil 101,6 com 0,42

    def test_minimo_50(self):
        r = lr.verificar("emenda_viga", dict(self.DADOS, minimo_50=True), {"M": 10, "V": 10})
        self.assertLess(rel(r["intermediarios"]["Mf"] + r["intermediarios"]["Mw"], 328.4), 0.01)


class TestPilarLocal(unittest.TestCase):
    def test_pfeil_6_5_9_escoamento_e_enrugamento(self):
        # W530×92 (tw = 10,2, tf = 15,6, r = 12, d = 533), MR250, a' = 100 mm, carga de extremidade:
        # Pfeil p. 196: escoamento 431 kN (= 1,10·Rn/γa1 = Rn) e enrugamento 353 kN
        self.assertLess(rel(lr.escoamento_local_alma(0.0102, 0.0156 + 0.012, 0.100, 250 * MPa, True), 431.0), 0.01)
        self.assertLess(rel(lr.enrugamento_alma(0.0102, 0.0156, 0.533, 0.100, 250 * MPa, True), 353.0), 0.01)

    def test_painel_mao(self):
        # W310×97, A572-50: 0,6·345·308·9,9/1,1 = 573,8 kN; com Nc = 0,6·Ny: × (1,4 − 0,6) = 0,8
        V = lr.cisalhamento_painel(0.308, 0.0099, 345 * MPa)
        self.assertLess(rel(V, 573.8), 0.001)
        self.assertLess(rel(lr.cisalhamento_painel(0.308, 0.0099, 345 * MPa, 0.6, 1.0), 0.8 * V), 1e-9)

    def test_flexao_mesa_e_flambagem(self):
        self.assertLess(rel(lr.flexao_local_mesa(0.0154, 345 * MPa), 6.25 * 0.0154 ** 2 * 345e3 / 1.1), 1e-12)
        h = 0.308 - 2 * (0.0154 + 0.016)
        self.assertLess(rel(lr.flambagem_alma_compressao(0.0099, h, 345 * MPa),
                            24 * 0.0099 ** 3 * math.sqrt(200e6 * 345e3) / (h * 1.1)), 1e-12)

    def test_tipo_painel_enrijecedor_e_reforco(self):
        base = dict(perfil_viga="W460x89", perfil_pilar="W310x97")
        sem = lr.verificar("painel_alma_pilar", base, {"M": 300})
        self.assertFalse(sem["ok"])
        com = lr.verificar("painel_alma_pilar", dict(base, t_enr_pilar=16, t_reforco=9.5), {"M": 300})
        self.assertTrue(com["ok"], [v for v in com["verificacoes"] if not v["ok"]])
        nomes = [c["nome"] for c in com["geometria"]["chapas"]]
        self.assertEqual(nomes.count("enrijecedor do pilar"), 4)
        self.assertEqual(nomes.count("chapa de reforço da alma"), 1)


class TestTiposEGeometria(unittest.TestCase):
    CASOS = [
        ("chapa_topo_ajustada", dict(perfil_viga="W310x38.7", d_mm=19.05, tp=19, perfil_pilar="W310x97",
                                     n_linhas_extras=1), {"M": 50, "V": 60}),
        ("chapa_topo_estendida", dict(perfil_viga="W460x89", d_mm=22.225, tp=22.4, enrijecedor_chapa=True,
                                      t_enrijecedor=12.5, estendida="ambas"), {"M": -150, "V": 80}),
        ("flange_plate", dict(perfil_viga="W460x89", perfil_pilar="W310x97", d_mm=22.225, t_mesa=19,
                              n_por_linha=4, t_enr_pilar=12.5), {"M": 200, "V": 100}),
        ("flange_plate", dict(perfil_viga="W460x89", modo="soldada", t_mesa=19, b_chapa=160), {"M": 200, "V": 100}),
        ("painel_alma_pilar", dict(perfil_viga="W460x89", perfil_pilar="W310x97"), {"M": 150}),
        ("emenda_viga", TestEmendaVigaPfeil.DADOS, {"M": 200, "V": 100}),
        ("emenda_pilar", dict(perfil_pilar="W310x97", h_tala_alma=220, n_linhas_alma=3, s_alma=70), {"N": -1500,
                                                                                                    "M": 80, "V": 50}),
        ("emenda_pilar", dict(perfil_pilar="W310x97", h_tala_alma=220, n_linhas_alma=3, s_alma=70, contato=False),
         {"N": 300, "M": 40, "V": 50}),
    ]

    def test_contrato_resultado(self):
        for tipo, d, e in self.CASOS:
            with self.subTest(tipo=tipo):
                r = lr.verificar(tipo, d, e)
                json.dumps(r)
                self.assertEqual({"ok", "taxa", "verificacoes", "avisos", "geometria"} - set(r), set())
                self.assertAlmostEqual(r["taxa"], max(v["taxa"] for v in r["verificacoes"]))
                self.assertEqual(r["ok"], all(v["ok"] for v in r["verificacoes"]))
                for v in r["verificacoes"]:
                    self.assertEqual({"nome", "item", "solicitante", "resistente", "unidade", "taxa", "ok"}, set(v))
                    self.assertTrue(v["item"].startswith(("NBR 8800", "CBCA", "AISC", "Pfeil")), v["item"])
                g = r["geometria"]
                self.assertEqual({"chapas", "cantoneiras", "parafusos", "soldas", "recortes"}, set(g))
                for c in g["chapas"]:
                    self.assertGreaterEqual(len(c["contorno"]), 3)
                    self.assertGreater(c["espessura"], 0)
                    self.assertAlmostEqual(sum(a * b for a, b in zip(c["normal"], c["u"])), 0.0)
                    self.assertAlmostEqual(math.hypot(*c["normal"]), 1.0)
                for p in g["parafusos"]:
                    self.assertAlmostEqual(math.hypot(*p["eixo"]), 1.0)
                    self.assertGreater(p["comprimento"], 0)
                    self.assertTrue(all(math.isfinite(x) for x in p["origem"]))
                self.assertTrue(g["parafusos"] or g["soldas"] or tipo == "painel_alma_pilar")

    def test_parafusos_dentro_da_chapa_de_topo(self):
        r = lr.verificar("chapa_topo_estendida", TestChapaTopoCBCA.DADOS, {"M": 290})
        ch = r["geometria"]["chapas"][0]
        ys = [p[0] for p in ch["contorno"]]
        zs = [p[1] for p in ch["contorno"]]
        for p in r["geometria"]["parafusos"]:
            self.assertTrue(min(ys) < p["origem"][1] < max(ys))
            self.assertTrue(min(zs) < p["origem"][2] < max(zs))
            self.assertEqual(p["eixo"], [-1, 0, 0])
        self.assertEqual(len(r["geometria"]["parafusos"]), 6)

    def test_emenda_simetrica(self):
        r = lr.verificar("emenda_viga", TestEmendaVigaPfeil.DADOS, {"M": 200, "V": 100})
        xs = sorted(p["origem"][0] for p in r["geometria"]["parafusos"])
        self.assertAlmostEqual(sum(xs), 0.0, places=6)            # mesmo número de parafusos de cada lado
        self.assertEqual(len(r["geometria"]["parafusos"]), 2 * 2 * 2 * 3 + 2 * 8)
        self.assertLess(rel(r["intermediarios"]["comprimento_tala_alma"], 2 * (30 + 70 + 30)), 1e-9)

    def test_esquema_registrado(self):
        tipos = {t["tipo"]: t for t in lg.tipos_ligacao()}
        for nome in lr.TIPOS:
            self.assertIn(nome, tipos)
            t = tipos[nome]
            self.assertIn(t["familia"], ("rigida", "emenda"))
            for c in t["campos"]:
                self.assertTrue({"nome", "unidade", "descricao", "padrao", "rotulo"} <= set(c))
                self.assertLessEqual(len(c["rotulo"]), 28)
                self.assertNotIn(c["nome"], ("b", "eh"))   # rótulos fixos de projeto.py
        json.dumps(lg.tipos_ligacao())

    def test_entradas_invalidas(self):
        with self.assertRaises(ValueError) as cm:
            lr.verificar("chapa_topo_ajustada", dict(tp=-1, d_mm="x", tipo_parafuso="Z"), {"M": 1})
        for nome in ("perfil_viga", "tp", "d_mm", "tipo_parafuso"):
            self.assertIn(nome, str(cm.exception))
        with self.assertRaises(ValueError):
            lr.verificar("emenda_viga", dict(perfil_viga="W999x1"), {})
        with self.assertRaises(ValueError):
            lr.verificar("chapa_topo_estendida", dict(perfil_viga="W460x89", enrijecedor_chapa=True), {"M": 1})
        with self.assertRaises(ValueError):
            lr.verificar("inexistente", {}, {})

    def test_esforcos_fora_do_plano_avisam(self):
        r = lr.verificar("painel_alma_pilar", dict(perfil_viga="W460x89", perfil_pilar="W310x97"),
                         {"M": 50, "Mz": 5})
        self.assertTrue(any("Mz" in a for a in r["avisos"]))

    def test_sugerir_passa(self):
        perf = dict(perfil_viga="W460x89", perfil_pilar="W310x97")
        e = {"M": 200, "V": 120}
        for tipo in ("chapa_topo_estendida", "flange_plate", "painel_alma_pilar", "emenda_viga", "emenda_pilar"):
            with self.subTest(tipo=tipo):
                d = lr.TIPOS[tipo]["sugerir"](perf, e)
                r = lr.verificar(tipo, d, e)
                self.assertTrue(r["ok"], (d, [v["nome"] for v in r["verificacoes"] if not v["ok"]]))


if __name__ == "__main__":
    unittest.main()
