import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural.modelo import CasoCarga
from prossen_estrutural.acoes import vento, sismo, combinacoes
from prossen_estrutural.acoes.combinacoes import gerar_combinacoes, psi

CFG_V = {"V0": 35.0, "S1": 1.0, "categoria": "IV", "classe": "B", "S3": 1.0,
         "direcoes": ["+X", "-X", "+Y", "-Y"]}
DIM = {"lx": 20.0, "ly": 12.0}
PAV = [("P1", 3.0), ("P2", 6.0), ("P3", 9.0), ("P4", 12.0)]


class TestVento(unittest.TestCase):
    def test_s2_tabela2(self):
        # NBR 6123:1988 tabela 2 de S2 (procedimento mantido em 2023)
        self.assertAlmostEqual(vento.s2("II", "A", 10), 1.00, 2)
        self.assertAlmostEqual(vento.s2("I", "A", 10), 1.10, 2)
        self.assertAlmostEqual(vento.s2("IV", "B", 10), 0.83, 2)
        self.assertAlmostEqual(vento.s2("II", "B", 30), 1.08, 2)
        # categoria V, classe C, z ≤ 5 m: tabela 2 = 0,67 (= linha de 10 m; 5.3.3: S2 constante até 10 m)
        self.assertAlmostEqual(vento.s2("V", "C", 5), 0.67, 2)

    def test_s3_2023(self):
        # NBR 6123:2023 tabela 4 (comparativo calculistadeaco.com.br, slide "Fator S3")
        self.assertEqual([vento.s3(g) for g in (1, 2, 3, 4, 5)], [1.11, 1.06, 1.00, 0.95, 0.83])
        self.assertAlmostEqual(vento.s3(3, vedacao=True), 0.92, 12)  # nota da tabela 4

    def test_s1_talude(self):
        # NBR 6123 5.2: θ = 10°, z = 0: S1 = 1 + 2,5·tg(7°) = 1,3070 (cálculo à mão)
        self.assertAlmostEqual(vento.s1("talude", 0.0, 10.0, 10.0), 1.3070, 3)
        self.assertEqual(vento.s1("plano"), 1.0)
        for ruim in (dict(terreno="taludee"), dict(terreno="talude", d=0), dict(terreno="talude", d=-5.0)):
            with self.assertRaises(ValueError, msg=str(ruim)):
                vento.s1(**ruim)
        self.assertEqual(vento.s1("vale"), 0.9)
        self.assertAlmostEqual(vento.s1("talude", 0.0, 10.0, 50.0), 1.0 + 2.5 * 0.31, 9)  # θ ≥ 45°

    def test_q(self):
        # Vk = 40 m/s -> q = 0,613·1600 = 980,8 N/m²
        self.assertAlmostEqual(vento.pressao_dinamica(40, 1, "II", "A", 10, 1), 0.9808, 4)

    def test_ca_figura4(self):
        # leituras à mão da fig. 4 (NBR 6123 6.3, baixa turbulência), tolerância de leitura ±0,04:
        # (l1/l2; h/l1): (2; 4) sobre a isolinha 1,5; (1; 2) entre 1,2 e 1,3; (0,6; 1) entre 1,0 e 0,9
        self.assertAlmostEqual(vento.coef_arrasto(20, 10, 80), 1.50, delta=0.04)
        self.assertAlmostEqual(vento.coef_arrasto(10, 10, 20), 1.26, delta=0.04)
        self.assertAlmostEqual(vento.coef_arrasto(12, 20, 12), 0.93, delta=0.04)
        self.assertTrue(0.7 < vento.coef_arrasto(10, 20, 30) < 2.2)
        # fora do ábaco: borda (l1/l2 < 0,26 e h/l1 < 0,5)
        self.assertEqual(vento.coef_arrasto(1, 100, 0.1), vento.coef_arrasto(0.26, 1, 0.13))

    def test_forcas(self):
        f = vento.forcas_por_pavimento([3, 6, 9], 10, 10, 40, Ca=1.0)
        self.assertEqual(len(f), 3)
        self.assertLess(f[2][1], f[1][1])  # cobertura com meia faixa

    def test_forcas_pavimento_a_mao(self):
        # edifício 20×12 m, 4×3 m, V0 = 35, cat. IV, classe B, S1 = S3 = 1, vento +X (l1 = 12), Ca = 0,93.
        # À mão: S2 = 0,85·0,98·(z/10)^0,125 (z < 5 -> 5): 0,76386; 0,78147; 0,82210; 0,85220;
        # q = 0,613·(35·S2)²/1000 = 0,43816; 0,45859; 0,50751; 0,54536 kN/m²;
        # F = Ca·q·l1·h_infl (3; 3; 3; 1,5 m) = 14,6695; 15,3536; 16,9915; 9,1293 kN
        cfg = dict(CFG_V, Ca=0.93)
        caso = vento.casos_vento(cfg, PAV, DIM)[0]
        esperado = [14.6695, 15.3536, 16.9915, 9.1293]
        self.assertEqual(caso["caso"], "V+X")
        for f, e in zip(caso["forcas"], esperado):
            self.assertAlmostEqual(f["F_kN"] / e, 1.0, delta=0.005)
        self.assertEqual([f["z"] for f in caso["forcas"]], [3.0, 6.0, 9.0, 12.0])
        p = caso["parametros"]
        self.assertAlmostEqual(p["q_topo_kN_m2"] / 0.54536, 1.0, delta=0.005)
        self.assertAlmostEqual(p["Vk_topo_m_s"] / 29.8271, 1.0, delta=0.001)
        self.assertEqual((p["l1"], p["l2"], p["Ca"], p["V0"], p["S1"], p["S3"]), (12.0, 20.0, 0.93, 35.0, 1.0, 1.0))
        self.assertEqual((p["categoria"], p["classe"]), ("IV", "B"))

    def test_quatro_direcoes_sinais_e_ca(self):
        casos = {c["caso"]: c for c in vento.casos_vento(CFG_V, PAV, DIM)}
        self.assertEqual(list(casos), ["V+X", "V-X", "V+Y", "V-Y"])
        dirs = {"V+X": [1, 0, 0], "V-X": [-1, 0, 0], "V+Y": [0, 1, 0], "V-Y": [0, -1, 0]}
        for n, c in casos.items():
            self.assertEqual(c["direcao"], [float(v) for v in dirs[n]])
            for f in c["forcas"]:
                self.assertGreaterEqual(f["F_kN"], 0.0)  # módulo; sentido em direcao / F_vet_kN
                self.assertAlmostEqual(f["F_vet_kN"][0], f["F_kN"] * dirs[n][0], 9)
                self.assertAlmostEqual(f["F_vet_kN"][1], f["F_kN"] * dirs[n][1], 9)
        # sentidos opostos: mesma intensidade
        for a, b in (("V+X", "V-X"), ("V+Y", "V-Y")):
            self.assertEqual([f["F_kN"] for f in casos[a]["forcas"]], [f["F_kN"] for f in casos[b]["forcas"]])
        # Ca da fig. 4 para os dois sentidos: X: l1/l2 = 0,6, h/l1 = 1 (~0,93); Y: l1/l2 = 1,67, h/l1 = 0,6
        # (1,166: leitura do verificador, curvas 1,2 em h/l1 = 0,742 e 1,3 em 1,373)
        self.assertAlmostEqual(casos["V+X"]["parametros"]["Ca"], 0.93, delta=0.04)
        self.assertAlmostEqual(casos["V+Y"]["parametros"]["Ca"], 1.166, delta=0.03)
        self.assertEqual((casos["V+Y"]["parametros"]["l1"], casos["V+Y"]["parametros"]["l2"]), (20.0, 12.0))

    def test_avisos(self):
        self.assertEqual(vento.casos_vento(CFG_V, PAV, DIM)[0]["avisos"], [])
        alto = [(f"P{i}", 3.0 * i) for i in range(1, 41)]  # 120 m: T1 > 1 s
        cods = [a["codigo"] for a in vento.casos_vento(CFG_V, alto, DIM)[0]["avisos"]]
        self.assertIn("vento_dinamico", cods)
        cfg = dict(CFG_V, categoria="I")  # zg = 250 m
        cods = [a["codigo"] for a in vento.casos_vento(cfg, [("T", 260.0)], DIM)[0]["avisos"]]
        self.assertIn("vento_s2_fora_faixa", cods)

    def test_validacao(self):
        for ruim in ({"V0": 0}, {"V0": -3}, {"categoria": "VI"}, {"classe": "D"}, {"S3": 0},
                     {"direcoes": ["+Z"]}, {"Ca": -1}, {"S1": 0}):
            with self.assertRaises(ValueError, msg=str(ruim)):
                vento.casos_vento(dict(CFG_V, **ruim), PAV, DIM)
        with self.assertRaises(ValueError):
            vento.casos_vento(CFG_V, [("P1", 0.0)], DIM)  # só a base
        with self.assertRaises(ValueError):
            vento.casos_vento(CFG_V, PAV, {"lx": 20.0})
        with self.assertRaises(ValueError):
            vento.casos_vento(CFG_V, [("A", 3.0), ("B", 3.0)], DIM)

    def test_edicao_cobertura(self):
        self.assertEqual(vento.EDICAO, "NBR 6123:2023")
        self.assertTrue(all({"norma", "edicao", "item", "verificacao", "limitacao"} <= set(c) for c in vento.COBERTURA))
        self.assertTrue(any(c["limitacao"] for c in vento.COBERTURA))


class TestCombinacoes(unittest.TestCase):
    def setUp(self):
        casos = [CasoCarga("G", "G"), CasoCarga("Q", "Q", "residencial"),
                 CasoCarga("V+X", "V", "vento"), CasoCarga("V-X", "V", "vento")]
        self.cb = gerar_combinacoes({c.nome: c for c in casos})

    def test_numero(self):
        elu = [c for c in self.cb if c[2] == "ELU"]
        # só G; Q princ. (V nenhum/±) = 3; V± princ. (Q ausente/acomp.) = 4; tudo com G 1,4 e 1,0 (5.1.3.1)
        self.assertEqual(len(elu), 2 * (1 + 3 + 4))
        for _, f, _ in self.cb:
            self.assertFalse("V+X" in f and "V-X" in f)

    def test_fatores(self):
        elu = [f for _, f, e in self.cb if e == "ELU"]
        self.assertIn({"G": 1.4, "Q": 1.4, "V+X": 1.4 * 0.6}, elu)
        self.assertIn({"G": 1.4, "Q": 1.4 * 0.5, "V-X": 1.4}, elu)
        self.assertIn({"G": 1.0, "V-X": 1.4}, elu)
        qp = [f for _, f, e in self.cb if e == "ELS_QP"]
        self.assertEqual(qp, [{"G": 1.0, "Q": 0.3}])

    def test_psi_tabela6(self):
        # NBR 8681:2003 tabela 6 / NBR 6118 tabela 11.2
        self.assertEqual(psi(CasoCarga("V", "V", "vento")), (0.6, 0.3, 0.0))
        self.assertEqual(psi(CasoCarga("Q", "Q", "residencial")), (0.5, 0.4, 0.3))
        self.assertEqual(psi(CasoCarga("Q", "Q", "escritorio")), (0.7, 0.6, 0.4))
        self.assertEqual(psi(CasoCarga("Q", "Q", "garagem")), (0.8, 0.7, 0.6))

    def test_subtipo_q_desconhecido_recusado(self):
        # revisão 2026-10-20 I-2: "bibliotca" não pode virar "comercial" (ψ0 0,7 em vez de 0,8)
        casos = {"G": CasoCarga("G", "G"), "Q": CasoCarga("Q", "Q", "bibliotca")}
        with self.assertRaises(ValueError) as cm:
            gerar_combinacoes(casos)
        self.assertIn("biblioteca", str(cm.exception))  # lista os válidos
        with self.assertRaises(ValueError):
            psi(CasoCarga("Q", "Q", "bibliotca"))
        with self.assertRaises(ValueError):
            gerar_combinacoes({"G": CasoCarga("G", "G"), "Q": CasoCarga("Q", "Q")}, tipo_uso="bibliotca")
        # subtipo vazio = tipo_uso (tabela 6: biblioteca ψ0 = 0,8) e, sem ele, comercial (0,7)
        def psi0(**kw):
            cb = gerar_combinacoes({"G": CasoCarga("G", "G"), "Q1": CasoCarga("Q1", "Q"), "Q2": CasoCarga("Q2", "Q")}, **kw)
            return next(f for n, f, e in cb if n == "ELU Q1 princ. + Q2")["Q2"]
        self.assertAlmostEqual(psi0(tipo_uso="biblioteca"), 1.4 * 0.8, 9)
        self.assertAlmostEqual(psi0(), 1.4 * 0.7, 9)

    def test_limite_casos_q(self):
        ok = {f"Q{i}": CasoCarga(f"Q{i}", "Q") for i in range(12)}
        gerar_combinacoes(ok)
        ok["Q12"] = CasoCarga("Q12", "Q")
        with self.assertRaises(ValueError):
            gerar_combinacoes(ok)

    def test_tipo_edificacao(self):
        # NBR 8681:2003 tab. 2 (permanentes agrupadas) e tab. 5 (variáveis conjuntas). Tipo 1 (q > 5 kN/m²):
        # normal γg 1,35 / γq 1,5; excepcional γg 1,15. Ex.: G = 5, Q = 6 kN/m² → 1,35·5 + 1,5·6 = 15,75.
        casos = {c.nome: c for c in (CasoCarga("G", "G"), CasoCarga("Q", "Q", "biblioteca"),
                                     CasoCarga("V+X", "V", "vento"), CasoCarga("SX", "S"))}
        cb = gerar_combinacoes(casos, tipo_edificacao="tipo1")
        elu = [f for _, f, e in cb if e == "ELU"]
        self.assertIn({"G": 1.35, "Q": 1.5}, elu)
        f = {"G": 1.35, "Q": 1.5}
        self.assertAlmostEqual(f["G"] * 5 + f["Q"] * 6, 15.75, 12)
        self.assertIn({"G": 1.35, "Q": 1.5, "V+X": 1.5 * 0.6}, elu)
        self.assertIn({"G": 1.0, "Q": 1.5 * 0.8, "V+X": 1.5}, elu)
        exc = [f for _, f, e in cb if e == "ELU_EXC"]
        self.assertIn({"G": 1.15, "SX": 1.0, "Q": 0.6}, exc)
        # tipo 2 (tab. 2: 1,40; tab. 5: 1,4) = comportamento padrão (None)
        self.assertEqual(gerar_combinacoes(casos, tipo_edificacao="tipo2"), gerar_combinacoes(casos))
        with self.assertRaises(ValueError):
            gerar_combinacoes(casos, tipo_edificacao="tipo3")

    def test_edicao_cobertura(self):
        self.assertEqual(combinacoes.EDICAO, "NBR 8681:2003")
        self.assertTrue(all({"norma", "edicao", "item", "verificacao", "limitacao"} <= set(c) for c in combinacoes.COBERTURA))


class TestCombinacoesCompletas(unittest.TestCase):
    """1G + 2Q (residencial) + 4V (grupo único: +X, -X, +Y, -Y) + 1S (SX), variabilidade 'grandes'.

    Contagem à mão (grupos exclusivos: Q1, Q2 e o grupo de vento; selecionar no máximo um de cada).
    NBR 8681:2003 5.1.3.1 / tab. 2: G com γ desfavorável E favorável (1,0) em toda combinação
    (VC-logica-acoes-002, igual ao CYPE); 5.1.3.3: na sísmica cada Q entra com ψ2 ou ausente (003).
    - ELU: só G = 1; Q principal: Q1 está em 2·5 = 10 seleções (Q2 dentro/fora × vento nenhum ou 4)
      e Q2 também -> 20; vento principal: 4 ventos × 4 subconjuntos de Q = 16. (1 + 20 + 16) × 2 G = 74.
    - ELU_EXC (SX, ±1,0, G 1,2 e 1,0, Q1/Q2 com ψ2 ou ausentes): 2 × 2 × 4 = 16 (vento ausente: ψ2 = 0).
    - ELS_QP: 1. ELS_FREQ (vento secundário some, ψ2 = 0): Q1 princ. 2 + Q2 princ. 2 + vento princ.
      4 × 4 = 16 -> 20. ELS_RARA (todos os ψ1 ≠ 0): Σ|seleção| = 20 + 16 = 36.
    Total 74 + 16 + 1 + 20 + 36 = 147. Com SX e SY: ELU_EXC = (4 simples + 8 de 100%+30%) × 2 G × 4 = 96.
    """

    @staticmethod
    def _casos(extra=("SX",)):
        c = [CasoCarga("G", "G"), CasoCarga("Q1", "Q", "residencial"), CasoCarga("Q2", "Q", "residencial")]
        c += [CasoCarga(n, "V", "vento") for n in ("V+X", "V-X", "V+Y", "V-Y")]
        c += [CasoCarga(n, "S") for n in extra]
        return {x.nome: x for x in c}

    def test_contagem_por_estado(self):
        cb = gerar_combinacoes(self._casos())
        n = {e: sum(1 for c in cb if c[2] == e) for e in ("ELU", "ELU_EXC", "ELS_QP", "ELS_FREQ", "ELS_RARA")}
        self.assertEqual(n, {"ELU": 74, "ELU_EXC": 16, "ELS_QP": 1, "ELS_FREQ": 20, "ELS_RARA": 36})
        self.assertEqual(len(cb), 147)

    def test_nomes_unicos_e_fatores_unicos(self):
        for extra in (("SX",), ("SX", "SY")):
            cb = gerar_combinacoes(self._casos(extra))
            nomes = [c[0] for c in cb]
            self.assertEqual(len(nomes), len(set(nomes)))
            chaves = [(c[2], tuple(sorted(c[1].items()))) for c in cb]
            self.assertEqual(len(chaves), len(set(chaves)))

    def test_nenhuma_com_dois_ventos(self):
        vs = {"V+X", "V-X", "V+Y", "V-Y"}
        for nome, f, e in gerar_combinacoes(self._casos(("SX", "SY"))):
            self.assertLessEqual(len(vs & set(f)), 1, nome)

    def test_sismo_so_em_excepcional_com_gama_1_e_q_psi2(self):
        for nome, f, e in gerar_combinacoes(self._casos(("SX", "SY"))):
            tem_s = bool({"SX", "SY"} & set(f))
            self.assertEqual(tem_s, e == "ELU_EXC", nome)
            if tem_s:
                self.assertFalse({"V+X", "V-X", "V+Y", "V-Y"} & set(f), nome)  # vento nunca com sismo
                self.assertIn(f.get("Q1", 0.3), (0.3,))  # ψ2 residencial ou ausente (5.1.3.3)
                self.assertIn(f.get("Q2", 0.3), (0.3,))
                self.assertTrue(f["G"] in (1.2, 1.0), nome)
        exc = [f for _, f, e in gerar_combinacoes(self._casos()) if e == "ELU_EXC"]
        for g in (1.2, 1.0):
            for s in (1.0, -1.0):
                self.assertIn({"G": g, "SX": s, "Q1": 0.3, "Q2": 0.3}, exc)
                self.assertIn({"G": g, "SX": s, "Q2": 0.3}, exc)
                self.assertIn({"G": g, "SX": s}, exc)

    def test_sismo_ortogonal_100_30(self):
        # NBR 15421:2006 8.5: 100% numa direção + 30% na outra
        cb = gerar_combinacoes(self._casos(("SX", "SY")))
        exc = [f for _, f, e in cb if e == "ELU_EXC"]
        self.assertEqual(len(exc), 96)
        for g in (1.2, 1.0):
            self.assertIn({"G": g, "SX": 1.0, "SY": 0.3, "Q1": 0.3, "Q2": 0.3}, exc)
            self.assertIn({"G": g, "SX": -1.0, "SY": -0.3, "Q1": 0.3, "Q2": 0.3}, exc)
            self.assertIn({"G": g, "SY": 1.0, "SX": -0.3, "Q1": 0.3, "Q2": 0.3}, exc)

    def test_vento_psi_e_gama(self):
        elu = [f for _, f, e in gerar_combinacoes(self._casos()) if e == "ELU"]
        # vento secundário ψ0 = 0,6; Q principal 1,4; G 1,4 (NBR 8681:2003 tabelas 2, 5 e 6)
        self.assertIn({"G": 1.4, "Q1": 1.4, "V+X": 1.4 * 0.6}, elu)
        # permanente favorável 1,0 com qualquer principal (VC-logica-acoes-002); Q secundária ψ0 = 0,5
        self.assertIn({"G": 1.0, "Q1": 1.4 * 0.5, "V-Y": 1.4}, elu)
        self.assertIn({"G": 1.0, "Q1": 1.4}, elu)
        self.assertIn({"G": 1.0}, elu)
        rara = [f for _, f, e in gerar_combinacoes(self._casos()) if e == "ELS_RARA"]
        self.assertIn({"G": 1.0, "V+X": 1.0, "Q1": 0.4}, rara)  # principal integral, secundária ψ1
        freq = [f for _, f, e in gerar_combinacoes(self._casos()) if e == "ELS_FREQ"]
        self.assertIn({"G": 1.0, "V+X": 0.3, "Q1": 0.3}, freq)  # vento principal ψ1, Q secundária ψ2


class TestSismo(unittest.TestCase):
    def test_espectro(self):
        Sa = sismo.espectro(0.15, "C")  # Ca 1,2  Cv 1,7 -> ags0 0,18  ags1 0,255
        self.assertAlmostEqual(Sa(0.0), 0.18, 6)
        self.assertAlmostEqual(Sa(0.3), 0.45, 6)
        self.assertAlmostEqual(Sa(1.0), 0.255, 6)
        self.assertAlmostEqual(sismo.coef_terreno(0.125, "D")[0], 1.55, 6)

    def test_cs(self):
        self.assertAlmostEqual(sismo.coef_sismico(0.15, "C", 5, 1, 0.5), 0.09, 6)
        self.assertAlmostEqual(sismo.coef_sismico(0.15, "C", 5, 1, 2.0), 0.0255, 6)

    def test_ta_exemplo_publicado(self):
        # Camilla Lopes (UFMG, 2017), exemplo NBR 15421: aço momento-resistente, hn = 10 m -> Ta = 0,456 s;
        # ag = 0,15g, classe C, R = 1: Cs = 0,45; H = 0,45·600 = 270 kN (tabela 11)
        self.assertAlmostEqual(sismo.periodo_aproximado(10.0, "aco_portico"), 0.456, delta=0.001)
        # 9.2: pórticos de aço contraventados com treliças: CT = 0,0731, x = 0,75 (Miranda 2011)
        self.assertEqual(sismo.CT_X["aco_contraventado_trelica"], (0.0731, 0.75))
        F = sismo.forcas_equivalentes([200, 200, 200], [4, 7, 10], 3, "C", 1, 1.0, "aco_portico")
        self.assertAlmostEqual(sum(F), 270.0, delta=0.5)

    def test_forcas(self):
        F = sismo.forcas_equivalentes([1000, 1000], [3, 6], 3, "C", 5)  # Ta < 0,5 -> k=1
        self.assertAlmostEqual(sum(F), 0.09 * 2000, 6)
        self.assertAlmostEqual(F[1] / F[0], 2.0, 6)
        self.assertEqual(sismo.forcas_equivalentes([1000], [3], 0, "C", 5), [0.0])

    CFG = {"zona": 2, "classe_terreno": "D", "categoria_utilizacao": "I", "R": 3.0, "sistema": "concreto_portico"}
    PAV = [("P1", 3.0), ("P2", 6.0), ("P3", 9.0), ("P4", 12.0)]

    def test_casos_zona2_terreno_d_a_mao(self):
        # zona 2 (ag = 0,10g), classe D: Ca = 1,6, Cv = 2,4 -> ags0 = 0,16, ags1 = 0,24; I = 1, R = 3
        # Ta = 0,0466·12^0,9 = 0,4362 s (< 0,5 -> k = 1); Cs = min(2,5·0,16/3 = 0,13333; 0,24/(0,4362·3) = 0,1834)
        # H = 0,13333·4000 = 533,33 kN; F = H·z/Σz = 53,33; 106,67; 160,00; 213,33 kN
        casos = {c["caso"]: c for c in sismo.casos_sismo(self.CFG, self.PAV, [1000.0] * 4)}
        self.assertEqual(sorted(casos), ["SX", "SY"])
        self.assertEqual(casos["SX"]["direcao"], [1.0, 0.0, 0.0])
        self.assertEqual(casos["SY"]["direcao"], [0.0, 1.0, 0.0])
        p = casos["SX"]["parametros"]
        self.assertAlmostEqual(p["ag"], 0.10, 9)
        self.assertAlmostEqual(p["Ca"], 1.6, 9)
        self.assertAlmostEqual(p["Cv"], 2.4, 9)
        self.assertAlmostEqual(p["Ta"] / 0.4362, 1.0, delta=0.001)
        self.assertAlmostEqual(p["T"], p["Ta"], 12)
        self.assertEqual(p["k"], 1.0)
        self.assertAlmostEqual(p["Cs"] / 0.133333, 1.0, delta=0.005)
        self.assertAlmostEqual(p["H_kN"] / 533.33, 1.0, delta=0.005)
        self.assertEqual((p["R"], p["I"]), (3.0, 1.0))
        for f, e in zip(casos["SX"]["forcas"], [53.333, 106.667, 160.0, 213.333]):
            self.assertAlmostEqual(f["F_kN"] / e, 1.0, delta=0.005)
        self.assertEqual([f["F_kN"] for f in casos["SX"]["forcas"]], [f["F_kN"] for f in casos["SY"]["forcas"]])
        self.assertIn("sismo_edicao_2006", [a["codigo"] for a in casos["SX"]["avisos"]])

    def test_casos_T_modal_limitado_por_cup(self):
        # NBR 15421:2006 tabela 9: Cup por zona (2: 1,7; 3: 1,6; 4: 1,5). Zona 2, T modal = 1,0 s > Cup·Ta:
        # T usado = 1,7·0,43617 = 0,74149 s; Cs = min(0,13333; 0,24/(0,74149·3) = 0,10789);
        # k = 1 + (0,74149 - 0,5)/2 = 1,12075; H = 0,10789·4000 = 431,6 kN; F = H·z^k/Σz^k
        c = sismo.casos_sismo(self.CFG, self.PAV, [1000.0] * 4, T=1.0)[0]
        p = c["parametros"]
        self.assertEqual(p["Cup"], 1.7)
        self.assertAlmostEqual(p["T"], 0.7415, delta=0.0005)
        self.assertAlmostEqual(p["k"], 1.1207, delta=0.0005)
        self.assertAlmostEqual(p["Cs"] / 0.10789, 1.0, delta=0.005)
        k = 1.12075
        s = sum(z ** k for z in (3, 6, 9, 12))
        for f, z in zip(c["forcas"], (3, 6, 9, 12)):
            self.assertAlmostEqual(f["F_kN"] / (431.56 * z ** k / s), 1.0, delta=0.005)
        self.assertIn("sismo_T_limitado", [a["codigo"] for a in c["avisos"]])
        # zonas 3 e 4 (tabela 9) e ag_g informado: fica o menor Cup entre a zona e a faixa de ag (conservador)
        self.assertEqual([sismo.cup(z) for z in (2, 3, 4)], [1.7, 1.6, 1.5])
        self.assertEqual(sismo.parametros_sismicos([12.0], 3, "D", 3.0)["Cup"], 1.6)
        self.assertEqual(sismo.parametros_sismicos([12.0], 2, "D", 3.0, ag=0.12)["Cup"], 1.6)
        self.assertEqual(sismo.parametros_sismicos([12.0], 4, "D", 3.0, ag=0.08)["Cup"], 1.5)

    def test_zona0_vazio_e_zona1(self):
        self.assertEqual(sismo.casos_sismo(dict(self.CFG, zona=0), self.PAV, [1000.0] * 4), [])
        c = sismo.casos_sismo(dict(self.CFG, zona=1), self.PAV, [100.0, 200.0, 300.0, 400.0])[0]
        for f, w in zip(c["forcas"], [100, 200, 300, 400]):
            self.assertAlmostEqual(f["F_kN"], 0.01 * w, 12)  # 7.3.2
        self.assertAlmostEqual(c["parametros"]["H_kN"], 10.0, 9)

    def test_validacao(self):
        for ruim in ({"zona": 5}, {"zona": 2.5}, {"classe_terreno": "F"}, {"categoria_utilizacao": "IV"},
                     {"R": 0}, {"R": None}, {"sistema": "x"}, {"ag_g": -0.1}):
            with self.assertRaises(ValueError, msg=str(ruim)):
                sismo.casos_sismo(dict(self.CFG, **ruim), self.PAV, [1000.0] * 4)
        with self.assertRaises(ValueError):
            sismo.casos_sismo(self.CFG, self.PAV, [1000.0] * 3)
        for inf in (float("inf"), float("-inf"), float("nan")):  # JSON aceita Infinity/NaN
            with self.assertRaises(ValueError, msg=str(inf)):
                sismo.casos_sismo(dict(self.CFG, zona=inf), self.PAV, [1000.0] * 4)
        with self.assertRaises(ValueError):
            sismo.casos_sismo(self.CFG, self.PAV, [0.0] * 4)

    def test_fracao_Q(self):
        # NBR 15421:2006: 25% da sobrecarga em armazenagem/estacionamento; adotado max(ψ2, 25%)
        self.assertEqual(sismo.fracao_Q_sismo("residencial"), 0.3)
        self.assertEqual(sismo.fracao_Q_sismo("escritorio"), 0.4)
        self.assertEqual(sismo.fracao_Q_sismo("garagem"), 0.6)
        self.assertGreaterEqual(sismo.fracao_Q_sismo("biblioteca"), 0.25)
        with self.assertRaises(ValueError):
            sismo.fracao_Q_sismo("vento")

    def test_edicao_cobertura(self):
        self.assertEqual(sismo.EDICAO, "NBR 15421:2006")
        self.assertTrue(all({"norma", "edicao", "item", "verificacao", "limitacao"} <= set(c) for c in sismo.COBERTURA))
        self.assertTrue(any("2023" in c["limitacao"] for c in sismo.COBERTURA))


class TestAlinhamentoCYPE(unittest.TestCase):
    """Rodada de alinhamento com o CYPE (verificacao/logica-cype-acoes-estabilidade.md)."""

    @staticmethod
    def _c(*defs):
        return {n: CasoCarga(n, t, s) for n, t, s in defs}

    def test_g_favoravel_com_q_principal_e_so_g(self):
        # NBR 8681:2003 5.1.3.1 / tab. 2: γg = 1,0 quando a permanente é favorável, qualquer que seja a principal
        elu = [f for _, f, e in gerar_combinacoes(self._c(("G", "G", ""), ("Q", "Q", "residencial"))) if e == "ELU"]
        for f in ({"G": 1.4}, {"G": 1.0}, {"G": 1.4, "Q": 1.4}, {"G": 1.0, "Q": 1.4}):
            self.assertIn(f, elu)
        self.assertEqual(len(elu), 4)

    def test_sismo_sem_q(self):
        # NBR 8681:2003 5.1.3.3 (variáveis só quando desfavoráveis); NBR 8800:2008 tab. 2 nota e (ψ2 = 0 com sismo)
        exc = [f for _, f, e in gerar_combinacoes(self._c(("G", "G", ""), ("Q", "Q", "residencial"), ("SX", "S", "")))
               if e == "ELU_EXC"]
        self.assertEqual(len(exc), 8)  # ±SX × γg (1,2; 1,0) × Q (ψ2 = 0,3; ausente)
        self.assertIn({"G": 1.0, "SX": 1.0}, exc)
        self.assertIn({"G": 1.2, "SX": -1.0, "Q": 0.3}, exc)

    def test_torcao_acidental_nas_combinacoes(self):
        # NBR 15421:2006 (distribuição horizontal das forças): momento acidental com os dois sinais junto da
        # direção principal; com as duas direções simultâneas basta o da direção mais crítica (a de 100%)
        L = gerar_combinacoes(self._c(("G", "G", ""), ("SX", "S", ""), ("SY", "S", ""),
                                      ("SX_TOR", "S", ""), ("SY_TOR", "S", "")))
        exc = [f for _, f, e in L if e == "ELU_EXC"]
        self.assertEqual(len(exc), 12 * 2 * 2)  # 12 ações sísmicas × ±torção × 2 γg
        self.assertIn({"G": 1.2, "SX": 1.0, "SX_TOR": 1.0}, exc)
        self.assertIn({"G": 1.2, "SX": 1.0, "SX_TOR": -1.0}, exc)
        self.assertIn({"G": 1.0, "SY": -1.0, "SX": 0.3, "SY_TOR": 1.0}, exc)
        for f in exc:
            self.assertEqual(sum(k.endswith("_TOR") for k in f), 1, f)  # nunca sem torção, nunca as duas
            self.assertTrue({"SX", "SY"} & set(f), f)  # torção nunca sozinha
        self.assertFalse(any(k.endswith("_TOR") for _, f, e in L if e != "ELU_EXC" for k in f))

    def test_psi_cobertura_nbr8800(self):
        # NBR 8800:2008 tab. 2: "bibliotecas, arquivos, depósitos, oficinas e garagens e sobrecargas em coberturas"
        self.assertEqual(psi(CasoCarga("Q", "Q", "cobertura")), (0.8, 0.7, 0.6))

    def test_acoes_separadas_nbr8800(self):
        # NBR 8800:2008 tab. 1, combinações normais: PP de estrutura metálica 1,25; elementos construtivos em geral
        # e equipamentos 1,50 (favoráveis 1,00); vento 1,40; demais variáveis (uso) 1,50; temperatura 1,20.
        # Excepcionais: 1,10 / 1,30. Caso 2 do dossiê: "V0 princ. + Q" = G 1,5; Q 1,5·0,8 = 1,20; V 1,4.
        casos = self._c(("PP", "G", "metalica"), ("G2", "G", ""), ("Q", "Q", "cobertura"), ("V0", "V", "vento"),
                        ("SX", "S", ""))
        L = gerar_combinacoes(casos, tipo_edificacao="separadas")
        elu = [f for _, f, e in L if e == "ELU"]
        self.assertIn({"PP": 1.25, "G2": 1.5, "Q": 1.5, "V0": 1.4 * 0.6}, elu)
        self.assertIn({"PP": 1.25, "G2": 1.5, "Q": 1.5 * 0.8, "V0": 1.4}, elu)
        self.assertIn({"PP": 1.0, "G2": 1.0, "V0": 1.4}, elu)
        exc = [f for _, f, e in L if e == "ELU_EXC"]
        self.assertIn({"PP": 1.10, "G2": 1.30, "SX": 1.0, "Q": 0.6}, exc)
        self.assertIn({"PP": 1.0, "G2": 1.0, "SX": -1.0}, exc)
        T = gerar_combinacoes(self._c(("G", "G", ""), ("T", "Q", "temperatura")), tipo_edificacao="separadas")
        self.assertIn({"G": 1.5, "T": 1.2}, [f for _, f, e in T if e == "ELU"])
        with self.assertRaises(ValueError):
            gerar_combinacoes(self._c(("G", "G", "concreto_x")), tipo_edificacao="separadas")


class TestVentoGalpao(unittest.TestCase):
    """NBR 6123:1988 tab. 4 (paredes) e tab. 5 (telhado de duas águas), 6.2.5 (Cpi). Tabelas conferidas na
    reprodução de Storte (UFU, pp. 31-32); zonas e aplicação por pórtico no exemplo de Oliveira
    (UFMG/DEES, galpão em PFF, 2015, folhas 9-13)."""

    UFMG = {"vao": 15.0, "altura_pilar": 6.0, "altura_cumeeira": 6.0 + 7.5 * math.tan(math.radians(10.0)),
            "n_modulos": 9, "espacamento": 6.0}
    CFG = {"V0": 35.0, "categoria": "III", "classe": "B", "permeabilidade": "quatro_faces"}

    def _caso(self, nome, geo=None, cfg=None):
        return {c["caso"]: c for c in vento.casos_vento_galpao(cfg or self.CFG, geo or self.UFMG)}[nome]

    def test_tabela4_e_5_valores(self):
        # tab. 4, h/b ≤ 1/2: 1 ≤ a/b ≤ 3/2 → A1 -0,8, A2 -0,5, A +0,7, B -0,4; 2 ≤ a/b ≤ 4 → -0,8/-0,4/+0,7/-0,5
        self.assertEqual(vento.TAB4_PAREDES[(1, 1)], {"A1": -0.8, "A2": -0.5, "A": 0.7, "B": -0.4})
        self.assertEqual(vento.TAB4_PAREDES[(1, 2)], {"A1": -0.8, "A2": -0.4, "A": 0.7, "B": -0.5})
        self.assertEqual(vento.TAB4_PAREDES[(2, 2)], {"A1": -0.9, "A2": -0.4, "A": 0.7, "B": -0.6})
        # tab. 5, h/b ≤ 1/2, α = 90°: EF por θ = 0..60° (dossiê: mesmos valores no nbr.dll do CYPE)
        self.assertEqual(vento.TAB5_TELHADO[1]["EF"], [-0.8, -0.9, -1.2, -1.0, -0.4, 0.0, 0.3, 0.7])
        self.assertEqual(vento.TAB5_TELHADO[1]["GH"], [-0.4] * 6 + [-0.5, -0.6])
        self.assertEqual(vento.TAB5_TELHADO[2]["EG"], [-1.0, -0.9] + [-0.8] * 6)
        # interpolação linear em θ: 12,5° entre 10° (-1,2) e 15° (-1,0) → -1,1
        self.assertAlmostEqual(vento.coef_telhado(1, 12.5)["EF"], -1.1, 12)

    def test_cpi_6_2_5(self):
        nomes = [c["caso"] for c in vento.casos_vento_galpao(self.CFG, self.UFMG)]
        self.assertEqual(len(nomes), 8)  # 4 direções × Cpi ∈ {0; -0,3} (6.2.5 b)
        self.assertIn("V+X Cpi=-0.3", nomes)
        cfg2 = dict(self.CFG, permeabilidade="duas_faces_opostas")  # 6.2.5 a: +0,2 / -0,3 (envoltória)
        self.assertEqual(sorted({c["Cpi"] for c in vento.casos_vento_galpao(cfg2, self.UFMG)}), [-0.3, 0.2])
        cfg3 = dict(self.CFG, cpi=[0.8])  # abertura dominante (6.2.5 c), valor do usuário
        self.assertEqual({c["Cpi"] for c in vento.casos_vento_galpao(cfg3, self.UFMG)}, {0.8})

    def test_vento_0_graus_por_portico_ufmg(self):
        # Oliveira (UFMG 2015) folha 13, vento 0°, Cpi = 0: pórtico 1 (Lp 3) -0,80; 2: -0,80; 3: paredes -0,60,
        # cobertura -0,70; 4 e 5: -0,40 / -0,60; 6 e 7: paredes -0,20 (a' = 12 m, a/2 = 27 m)
        c = self._caso("V+Y Cpi=0")
        esp = {0: (-0.8, -0.8), 1: (-0.8, -0.8), 2: (-0.6, -0.7), 3: (-0.4, -0.6), 4: (-0.4, -0.6), 5: (-0.2, -0.2),
               6: (-0.2, -0.2)}
        q = c["parametros"]["q_kN_m2"]
        for p in c["porticos"]:
            if p["portico"] not in esp:
                continue
            parede, telhado = esp[p["portico"]]
            for barra, ce in (("pilar_esq", parede), ("pilar_dir", parede), ("agua_esq", telhado),
                              ("agua_dir", telhado)):
                self.assertAlmostEqual(p["barras"][barra]["Ce"], ce, 9, (p["portico"], barra))
                self.assertAlmostEqual(p["barras"][barra]["w_kN_m"], ce * q * p["largura_m"], 9)
        self.assertEqual(c["porticos"][0]["largura_m"], 3.0)
        self.assertAlmostEqual(c["parametros"]["a1_m"], 12.0, 12)  # max(b/3, a/4) ≤ 2h
        m = self._caso("V-Y Cpi=0")  # simétrico: o penúltimo pórtico fica na zona E/G
        self.assertAlmostEqual(m["porticos"][-2]["barras"]["agua_dir"]["Ce"], -0.8, 9)

    def test_vento_90_graus_dossie_caso2(self):
        # Dossiê caso 2: vão 20, pilar 6, cumeeira 8 (θ = 11,31°), 6 módulos de 5 m, V0 40, cat. III, classe B.
        # q(8 m) = 0,613·(40·0,94·0,98·0,8^0,105)² = 0,794 kN/m²; h/b = 0,3, a/b = 1,5 → EF = -1,2 + 0,2·1,31/5
        # = -1,148, GH = -0,4; pórtico interno, Cpi 0: -4,56 / -1,59 kN/m; sucção vertical ≈ 61,5 kN (Cpi +0,2: 77,3)
        geo = {"vao": 20.0, "altura_pilar": 6.0, "altura_cumeeira": 8.0, "n_modulos": 6, "espacamento": 5.0}
        cfg = {"V0": 40.0, "categoria": "III", "classe": "B"}
        q = 0.613 * (40 * 0.94 * 0.98 * 0.8 ** 0.105) ** 2 / 1000
        ef = -1.2 + 0.2 * (math.degrees(math.atan(2 / 10)) - 10) / 5
        L = math.hypot(10, 2)  # comprimento da água
        for cpi, vert in ((0.0, 61.5), (0.2, 77.3)):
            c = {x["caso"]: x for x in vento.casos_vento_galpao(dict(cfg, cpi=[cpi]), geo)}[f"V+X Cpi={cpi:g}"]
            self.assertAlmostEqual(c["parametros"]["q_kN_m2"], q, 9)
            b = c["porticos"][3]["barras"]
            self.assertAlmostEqual(b["agua_esq"]["Ce"], ef, 9)
            self.assertAlmostEqual(b["agua_dir"]["Ce"], -0.4, 9)
            self.assertAlmostEqual(b["pilar_esq"]["Ce"], 0.7, 9)  # tab. 4, a/b 1,5: A +0,7; B -0,4
            self.assertAlmostEqual(b["pilar_dir"]["Ce"], -0.4, 9)
            self.assertAlmostEqual(b["agua_esq"]["w_kN_m"], (ef - cpi) * q * 5.0, 9)
            Fz = sum(b[a]["w_vet_kN_m"][2] * L for a in ("agua_esq", "agua_dir"))  # sucção vertical (para cima)
            self.assertAlmostEqual(Fz, vert, delta=0.15)
        self.assertAlmostEqual(b["agua_esq"]["w_kN_m"], -5.35, delta=0.01)
        c0 = {x["caso"]: x for x in vento.casos_vento_galpao(dict(cfg, cpi=[0.0]), geo)}
        b = c0["V+X Cpi=0"]["porticos"][3]["barras"]
        self.assertAlmostEqual(b["agua_esq"]["w_kN_m"], -4.56, delta=0.01)
        self.assertAlmostEqual(b["agua_dir"]["w_kN_m"], -1.59, delta=0.01)
        # sentidos: parede a barlavento empurrada para +X; água esq. em sucção puxa para fora (-X, +Z)
        self.assertGreater(b["pilar_esq"]["w_vet_kN_m"][0], 0)
        self.assertLess(b["agua_esq"]["w_vet_kN_m"][0], 0)
        self.assertGreater(b["agua_esq"]["w_vet_kN_m"][2], 0)
        bm = c0["V-X Cpi=0"]["porticos"][3]["barras"]  # espelho
        self.assertAlmostEqual(bm["agua_dir"]["Ce"], ef, 9)
        self.assertAlmostEqual(bm["pilar_dir"]["w_vet_kN_m"][0], -b["pilar_esq"]["w_vet_kN_m"][0], 9)

    def test_fora_do_escopo(self):
        base = {"vao": 20.0, "altura_pilar": 6.0, "altura_cumeeira": 8.0, "n_modulos": 6, "espacamento": 5.0}
        for ruim in (dict(altura_pilar=31.0, altura_cumeeira=33.0),  # h/b > 3/2
                     dict(n_modulos=1),  # comprimento < vão
                     dict(altura_cumeeira=30.0),  # θ > 60°
                     dict(altura_cumeeira=5.0)):  # cumeeira abaixo do beiral
            with self.assertRaises(ValueError, msg=str(ruim)):
                vento.casos_vento_galpao(self.CFG, dict(base, **ruim))
        with self.assertRaises(ValueError):
            vento.casos_vento_galpao(dict(self.CFG, permeabilidade="x"), base)

    def test_cobertura_declarada(self):
        txt = " ".join(c["item"] + c["limitacao"] for c in vento.COBERTURA).lower()
        self.assertIn("tabela 5", txt)
        self.assertIn("6.2.5", txt)


class TestSismoTab6Drift(unittest.TestCase):
    """NBR 15421:2006 tab. 6 (R, Ω0, Cd), deslocamentos δx = Cd·δxe/I e limites de Δx por categoria, torção
    acidental 5%. Valores conferidos na reprodução de Santos (UFRJ/Poli, orient. S. H. C. Santos e S. S. Lima),
    tabelas 4.6 e 5.2."""

    def test_tabela6(self):
        for sist, rcd in (("pilar_parede_concreto_especial", (5, 2.5, 5)), ("portico_concreto_usual", (3, 3, 2.5)),
                          ("portico_concreto_especial", (8, 3, 5.5)), ("portico_aco_usual", (3.5, 3, 3)),
                          ("contraventado_aco_usual", (3.25, 2, 3.25)), ("dual_usual_pp_usual", (4.5, 2.5, 4)),
                          ("pendulo_invertido", (2.5, 2, 2.5))):
            t = sismo.TAB6[sist]
            self.assertEqual((t["R"], t["Omega0"], t["Cd"]), rcd, sist)
        self.assertEqual(len(sismo.TAB6), 17)

    def test_R_e_Cd_pelo_sistema(self):
        cfg = {"zona": 2, "classe_terreno": "D", "sistema_resistente": "portico_concreto_usual"}
        p = sismo.casos_sismo(cfg, [("P1", 3.0)], [1000.0])[0]["parametros"]
        self.assertEqual((p["R"], p["Cd"], p["Omega0"]), (3, 2.5, 3))
        with self.assertRaises(ValueError):
            sismo.casos_sismo(dict(cfg, sistema_resistente="x"), [("P1", 3.0)], [1000.0])

    def test_drift(self):
        # δx = Cd·δxe/I; Δx = δx(i) - δx(i-1) ≤ 0,020/0,015/0,010·hsx (cat. I/II/III). Cd 2,5, cat. II (I 1,25):
        # δxe = 2/5/9 mm → δx = 4/10/18 mm → Δ = 4/6/8 mm ≤ 0,015·3000 = 45 mm
        r = sismo.drift_sismico([0.002, 0.005, 0.009], [3.0, 6.0, 9.0], 2.5, "II")
        self.assertEqual([round(x["delta_x_m"], 9) for x in r], [0.004, 0.010, 0.018])
        self.assertEqual([round(x["Delta_m"], 9) for x in r], [0.004, 0.006, 0.008])
        self.assertTrue(all(abs(x["limite_m"] - 0.045) < 1e-12 and x["ok"] for x in r))
        r = sismo.drift_sismico([0.002, 0.005, 0.040], [3.0, 6.0, 9.0], 2.5, "III")  # I 1,5; limite 30 mm
        self.assertAlmostEqual(r[2]["Delta_m"], 2.5 * 0.035 / 1.5, 12)
        self.assertFalse(r[2]["ok"])
        self.assertAlmostEqual(r[2]["limite_m"], 0.030, 12)
        self.assertEqual(sismo.LIMITE_DRIFT, {"I": 0.020, "II": 0.015, "III": 0.010})

    def test_torcao_acidental(self):
        # momento acidental = F·e, e = 5% da dimensão perpendicular à força: SX → 0,05·ly; SY → 0,05·lx
        cfg = {"zona": 2, "classe_terreno": "D", "categoria_utilizacao": "I", "R": 3.0}
        casos = sismo.casos_sismo(cfg, PAV, [1000.0] * 4)
        tor = {t["caso"]: t for t in sismo.torcao_acidental(casos, {"lx": 20.0, "ly": 12.0})}
        self.assertEqual(sorted(tor), ["SX_TOR", "SY_TOR"])
        Fx = [f["F_kN"] for f in casos[0]["forcas"]]
        for m, f in zip(tor["SX_TOR"]["momentos"], Fx):
            self.assertAlmostEqual(m["Mz_kNm"], 0.6 * f, 9)
        self.assertAlmostEqual(tor["SY_TOR"]["momentos"][0]["Mz_kNm"], 1.0 * Fx[0], 9)  # 53,33 kN·m
        self.assertAlmostEqual(tor["SX_TOR"]["parametros"]["e_m"], 0.6, 12)
        self.assertEqual(sismo.torcao_acidental([], {"lx": 20.0, "ly": 12.0}), [])



class TestRecalque(unittest.TestCase):
    """NBR 8681:2003 tab. 3, ações permanentes indiretas (recalques): normal 1,2/0, excepcional 0/0, ELS 1,0."""

    def test_gama_epsilon(self):
        casos = {"G": CasoCarga("G", "G"), "R": CasoCarga("R", "G", "recalque"), "SX": CasoCarga("SX", "S")}
        d = {n: f for n, f, e in gerar_combinacoes(casos)}
        self.assertEqual(d["ELU G"], {"G": 1.4, "R": 1.2})
        self.assertEqual(d["ELU G fav"], {"G": 1.0})
        self.assertEqual(d["ELS QP"], {"G": 1.0, "R": 1.0})
        exc = [f for n, f, e in gerar_combinacoes(casos) if e == "ELU_EXC"]
        self.assertTrue(exc and all("R" not in f for f in exc))  # 0/0
        # só recalque: combinação própria
        self.assertEqual(dict((n, f) for n, f, e in gerar_combinacoes({"R": casos["R"]}))["ELU G"], {"R": 1.2})


if __name__ == "__main__":
    unittest.main()


class TestDetalhePorPavimentoECombinacao(unittest.TestCase):
    def test_s2_vk_q_do_primeiro_pavimento_a_mao(self):
        # P1 z = 3 m (< 5 m usa 5 m), cat. IV, classe B, V0 = 35: S2 = 0,85·0,98·0,5^0,125 = 0,76386;
        # Vk = 35·S2 = 26,735 m/s; q = 0,613·Vk²/1000 = 0,43816 kN/m²
        f = vento.casos_vento(dict(CFG_V, Ca=0.93), PAV, DIM)[0]["forcas"][0]
        self.assertAlmostEqual(f["S2"], 0.76386, delta=5e-5)
        self.assertAlmostEqual(f["Vk_m_s"], 26.735, delta=5e-3)
        self.assertAlmostEqual(f["q_kN_m2"], 0.43816, delta=5e-5)

    def test_gamma_psi_principal_e_secundaria(self):
        from prossen_estrutural import projeto as P
        m = P.novo_modelo()
        for c in (CasoCarga("G", "G"), CasoCarga("T", "Q", "temperatura"), CasoCarga("S", "Q", "cobertura"),
                  CasoCarga("V+X", "V", "vento")):
            m.casos[c.nome] = c
        cb = {c["nome"]: c["detalhe"] for c in P.combinacoes(m)}
        # ELU vento principal (γ 1,4, ψ 1), temperatura secundária (γ 1,2 · ψ0 0,6 = 0,72)
        d = cb["ELU V+X princ. + T"]
        self.assertAlmostEqual(d["V+X"]["gamma"], 1.4); self.assertAlmostEqual(d["V+X"]["psi"], 1.0)
        self.assertAlmostEqual(d["T"]["gamma"], 1.2); self.assertAlmostEqual(d["T"]["psi"], 0.6)
        self.assertAlmostEqual(d["T"]["produto"], 0.72)
        # ambiguidade do 1,2: temperatura principal (1,2·1) x sobrecarga de cobertura secundária (1,4·0,8 = 1,12)
        # e, em tipo1, γq 1,5·ψ0 0,8 = 1,2: produto 1,2 = γ 1,5 · ψ 0,8, não γ 1,2 · ψ 1
        m.config = {"tipo_edificacao": "tipo1"}
        d = {c["nome"]: c["detalhe"] for c in P.combinacoes(m)}["ELU V+X princ. + S"]
        self.assertAlmostEqual(d["S"]["produto"], 1.2)
        self.assertAlmostEqual(d["S"]["gamma"], 1.5); self.assertAlmostEqual(d["S"]["psi"], 0.8)
        # ELS frequente: γ 1, ψ1 na principal, ψ2 na secundária
        d = {c["nome"]: c["detalhe"] for c in P.combinacoes(m)}["ELS FREQ V+X princ. + S"]
        self.assertAlmostEqual(d["V+X"]["psi"], 0.3); self.assertAlmostEqual(d["S"]["psi"], 0.6)
        self.assertEqual(d["S"]["gamma"], 1.0)
