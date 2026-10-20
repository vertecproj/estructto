"""Regressão de ações (vento, sismo, combinações, cargas) — verificador-calculos, ficha 012 (2026-10-20).

Esperados de tabelas/fórmulas das normas conferidas em fonte pública pelo verificador (lista em
`verificacao/memorias/acoes-casos.py`), nunca da saída do código. Achados em `verificacao/2026-10-20-acoes.md`;
testes que falham hoje estão marcados `@unittest.expectedFailure` com o id do achado.
"""
import importlib.util
import math
import pathlib
import unittest

from prossen_estrutural.acoes import cargas, combinacoes, sismo, vento
from prossen_estrutural.modelo import CasoCarga

_MEM = pathlib.Path(__file__).resolve().parents[2] / "verificacao" / "memorias" / "acoes-casos.py"
_spec = importlib.util.spec_from_file_location("acoes_casos", _MEM)
mem = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mem)   # memória independente (só stdlib), fonte dos valores esperados

PAV = [("P1", 3.0), ("P2", 6.0), ("P3", 9.0), ("P4", 12.0)]
W = [1000.0] * 4


class TestVentoS2(unittest.TestCase):
    def test_tabela2(self):
        """NBR 6123:1988 tabela 2 (mantida em 2023: mesmo procedimento, tabela 1 com bm/p), linhas
        ≤5, 10, 15, 30, 50, 100, 200 m, 5 categorias × 3 classes, exceto categoria V abaixo de 10 m
        (ver VC-acoes-001). Tolerância: meia unidade do arredondamento da tabela (0,0051); z = 200 m
        categoria I classe C: tabela 1,32 × fórmula 1,312 (arredondamento da própria tabela) → 0,01."""
        for z, linha in mem.TAB2.items():
            for i, t in enumerate(linha):
                cat, cls = mem.CATS[i // 3], mem.CLS[i % 3]
                if cat == "V" and z < 10:
                    continue
                tol = 0.01 if (z, cat, cls) == (200, "I", "C") else 0.0051
                self.assertAlmostEqual(vento.s2(cat, cls, z), t, delta=tol, msg=f"z={z} {cat}{cls}")

    def test_categoria_V_constante_ate_10m(self):
        """VC-acoes-001 (CRÍTICO): NBR 6123:1988 5.3.3 — na categoria V, S2 é constante até z = 10 m;
        tabela 2: z ≤ 5 m, cat. V = 0,74 / 0,72 / 0,67 (= valores de 10 m)."""
        for cls, t in zip("ABC", (0.74, 0.72, 0.67)):
            for z in (3.0, 5.0, 8.0):
                self.assertAlmostEqual(vento.s2("V", cls, z), t, delta=0.0051, msg=f"{cls} z={z}")


class TestVentoFatores(unittest.TestCase):
    def test_pressao_dinamica(self):
        """NBR 6123 4.2: q = 0,613·Vk² N/m² → kN/m²; V0 = 35, IV-B, z = 12 m, S1 = S3 = 1:
        S2 = 0,85·0,98·1,2^0,125 → q = 0,545358 kN/m²."""
        self.assertAlmostEqual(vento.pressao_dinamica(35.0, 1.0, "IV", "B", 12.0, 1.0) / 0.5453582376427063, 1, places=9)

    def test_s3_2023(self):
        """NBR 6123:2023 tabela 4: 1,11 / 1,06 / 1,00 / 0,95 / 0,83; vedações 0,92·S3 (nota)."""
        self.assertEqual([vento.s3(g) for g in range(1, 6)], [1.11, 1.06, 1.00, 0.95, 0.83])
        self.assertAlmostEqual(vento.s3(3, vedacao=True), 0.92, places=12)

    def test_s1(self):
        """NBR 6123 5.2: S1 = 1 + (2,5 − z/d)·tg(θ − 3°) (6°–17°); 1 + (2,5 − z/d)·0,31 (≥ 45°);
        interpolação 17°–45° e 3°–6°; vale 0,9. z = 10, d = 20."""
        t17 = 1 + 2 * math.tan(math.radians(14))
        casos = [(10, 1 + 2 * math.tan(math.radians(7))), (45, 1.62), (30, t17 + 13 / 28 * (1.62 - t17)),
                 (4.5, 1 + 0.5 * 2 * math.tan(math.radians(3))), (2, 1.0)]
        for th, esp in casos:
            self.assertAlmostEqual(vento.s1("talude", 10, 20, th), esp, places=9, msg=f"θ={th}")
        self.assertEqual(vento.s1("vale"), 0.9)


class TestCoefArrasto(unittest.TestCase):
    """Ca da fig. 4 (NBR 6123:1988, vento de baixa turbulência) lido pelo verificador na reprodução pública
    (escala log, pixels; ±0,02) e leituras publicadas (Manual CBCA 2011). Tolerância 0,03."""

    def test_pontos_lidos(self):
        for (r, h) in [(0.6, 1.0), (1.0, 1.0), (1.0, 5.0), (1.0, 10.0), (2.0, 1.0)]:
            self.assertAlmostEqual(vento.coef_arrasto(1.0, 1.0 / r, h), mem.CA_LIDO[(r, h)], delta=0.03, msg=(r, h))
        self.assertAlmostEqual(vento.coef_arrasto(9.0, 15.0, 12.0), 0.95, delta=0.03)   # CBCA: l1/l2 0,6; h/l1 1,33

    def test_base_do_abaco(self):
        """VC-acoes-002 (ALTO, a favor da segurança): na faixa h/l1 ≤ ~0,8 a tabela digitalizada fica
        até +0,10 acima da figura (ex.: l1/l2 = 1, h/l1 = 0,5: curva 1,0 passa no ponto; código 1,10)."""
        for (r, h) in [(1.0, 0.5), (0.6, 0.5), (0.4, 0.5), (1.667, 0.6)]:
            self.assertAlmostEqual(vento.coef_arrasto(1.0, 1.0 / r, h), mem.CA_LIDO[(r, h)], delta=0.03, msg=(r, h))


class TestCasosVento(unittest.TestCase):
    CFG = {"V0": 35.0, "S1": 1.0, "categoria": "IV", "classe": "B", "S3": 1.0}

    def test_ficha005_direcao_X(self):
        """Ficha 005: 20 × 12 m, 4 × 3 m, V0 = 35, IV-B. +X: l1 = 12, l2 = 20, Ca(0,6; 1,0) = 0,927 (lido);
        Fa = Ca·q(z)·l1·h_infl (NBR 6123 4.2.3) → 14,62 / 15,30 / 16,94 / 9,10 kN (0,5%). Sinais por direção."""
        res = {c["caso"]: c for c in vento.casos_vento(self.CFG, PAV, {"lx": 20.0, "ly": 12.0})}
        _, esp = mem.forcas_ficha005("+X")
        for caso, sinal in (("V+X", [1, 0, 0]), ("V-X", [-1, 0, 0])):
            c = res[caso]
            self.assertEqual(list(c["direcao"]), sinal)
            self.assertAlmostEqual(c["parametros"]["l1"], 12.0)
            for f, (z, F) in zip(c["forcas"], esp):
                self.assertAlmostEqual(f["F_kN"] / F, 1.0, delta=0.005, msg=f"{caso} z={z}")
        self.assertEqual(list(res["V-Y"]["direcao"]), [0, -1, 0])

    def test_ficha005_direcao_Y(self):
        """VC-acoes-002: +Y: l1 = 20, l2 = 12, Ca(1,667; 0,6) = 1,166 (lido) → 30,65 / 32,08 / 35,51 / 19,08 kN;
        o código usa Ca = 1,214 (+4%)."""
        c = [c for c in vento.casos_vento(self.CFG, PAV, {"lx": 20.0, "ly": 12.0}) if c["caso"] == "V+Y"][0]
        for f, (z, F) in zip(c["forcas"], mem.forcas_ficha005("+Y")[1]):
            self.assertAlmostEqual(f["F_kN"] / F, 1.0, delta=0.005, msg=f"z={z}")

    def test_ca_dado_formula_exata(self):
        """Com Ca informado (1,3): F = 1,3·0,613·(35·S2(z))²/1000·12·h_infl, h_infl = 3/3/3/1,5 (1e-9)."""
        cfg = dict(self.CFG, Ca=1.3, direcoes=["+X"])
        c = vento.casos_vento(cfg, PAV, {"lx": 20.0, "ly": 12.0})[0]
        for f, (z, hinf) in zip(c["forcas"], [(3, 3.0), (6, 3.0), (9, 3.0), (12, 1.5)]):
            esp = 1.3 * mem.q_kN(35.0, 1.0, mem.s2_formula("IV", "B", z), 1.0) * 12.0 * hinf
            self.assertAlmostEqual(f["F_kN"] / esp, 1.0, places=9)

    def test_validacao(self):
        for ruim in ({"V0": 0, "categoria": "II", "classe": "A"}, {"V0": 30, "categoria": "VI", "classe": "A"},
                     {"V0": 30, "categoria": "II", "classe": "D"}):
            with self.assertRaises(ValueError):
                vento.casos_vento(ruim, PAV, {"lx": 10.0, "ly": 10.0})


class TestSismo(unittest.TestCase):
    def _sx(self, cfg, T=None):
        return [c for c in sismo.casos_sismo(cfg, PAV, W, T) if c["caso"] == "SX"][0]

    def test_zona2_terreno_D(self):
        """NBR 15421:2006, ag = 0,10g (zona 2, limite superior), D: Ca = 1,6, Cv = 2,4; Ta = 0,0466·12^0,9 =
        0,4362 s; Cs = 2,5·0,16/3 = 0,1333 (< 0,24/(Ta·3)); k = 1; Fx = 53,33/106,67/160/213,33 kN (0,5%)."""
        e = mem.sismo(0.10, "D", 3.0, 1.0, [3, 6, 9, 12], W)
        c = self._sx({"zona": 2, "classe_terreno": "D", "R": 3.0})
        p = c["parametros"]
        for k_c, k_m in (("Ca", "Ca"), ("Cv", "Cv"), ("Ta", "Ta"), ("T", "T"), ("Cs", "Cs"), ("k", "k"), ("H_kN", "H")):
            self.assertAlmostEqual(p[k_c] / e[k_m], 1.0, delta=0.005, msg=k_c)
        for f, F in zip(c["forcas"], e["F"]):
            self.assertAlmostEqual(f["F_kN"] / F, 1.0, delta=0.005)

    def test_importancia_III(self):
        """Tabela 1 (I = 1,5): Cs = 2,5·0,16/(3/1,5) = 0,20."""
        self.assertAlmostEqual(self._sx({"zona": 2, "classe_terreno": "D", "R": 3.0,
                                         "categoria_utilizacao": "III"})["parametros"]["Cs"], 0.20, places=9)

    def test_zonas_0_e_1(self):
        """7.3.1: zona 0 sem requisito ([]); 7.3.2: zona 1 → Fx = 0,01·wx = 10 kN em cada piso, SX e SY."""
        self.assertEqual(sismo.casos_sismo({"zona": 0, "classe_terreno": "D", "R": 3.0}, PAV, W), [])
        r = sismo.casos_sismo({"zona": 1, "classe_terreno": "D", "R": 3.0}, PAV, W)
        self.assertEqual(sorted(c["caso"] for c in r), ["SX", "SY"])
        for c in r:
            for f in c["forcas"]:
                self.assertAlmostEqual(f["F_kN"], 10.0, places=9)

    def test_interpolacao_ca_cv(self):
        """Tabela 3, interpolação linear em ag entre 0,10g e 0,15g: classe E, 0,125g → Ca 2,30, Cv 3,45."""
        Ca, Cv = sismo.coef_terreno(0.125, "E")
        self.assertAlmostEqual(Ca, 2.30, places=9)
        self.assertAlmostEqual(Cv, 3.45, places=9)

    def test_espectro(self):
        """6.3: Sa = ags0(18,75·T·Ca/Cv + 1) até 0,08Cv/Ca; 2,5·ags0 até 0,4Cv/Ca; ags1/T depois (D, 0,10g)."""
        sa = sismo.espectro(0.10, "D")
        for T, esp in ((0.0, 0.16), (0.05, 0.16 * (18.75 * 0.05 / 1.5 + 1)), (0.3, 0.40), (1.0, 0.24), (2.0, 0.12)):
            self.assertAlmostEqual(sa(T), esp, places=9, msg=T)

    def test_cup_por_zona_4(self):
        """VC-acoes-003 (CRÍTICO): Cup da tabela 9 é por ZONA (2: 1,7; 3: 1,6; 4: 1,5 — França 2021, Miranda
        2011). Zona 4, classe A, T modal 2,0 s: T = 1,5·Ta = 0,6542 s → Cs = 0,0611, H = 244,6 kN; o código
        usa Cup(ags1 = 0,12g) = 1,66 → H = 221,0 kN (−9,6%)."""
        e = mem.sismo(0.15, "A", 3.0, 1.0, [3, 6, 9, 12], W, zona=4, T_modal=2.0)
        p = self._sx({"zona": 4, "classe_terreno": "A", "R": 3.0}, T=2.0)["parametros"]
        self.assertAlmostEqual(p["H_kN"] / e["H"], 1.0, delta=0.005)

    def test_cup_por_zona_2(self):
        """VC-acoes-003: zona 2, classe D, T modal 1,0 s: Cup = 1,7 → T = 0,7415 s, Cs = 0,1079, H = 431,6 kN;
        o código usa 1,5 (H = 489,1 kN, +13%, a favor da segurança nesse caso)."""
        e = mem.sismo(0.10, "D", 3.0, 1.0, [3, 6, 9, 12], W, zona=2, T_modal=1.0)
        p = self._sx({"zona": 2, "classe_terreno": "D", "R": 3.0}, T=1.0)["parametros"]
        self.assertAlmostEqual(p["T"] / e["T"], 1.0, delta=0.005)

    def test_fracao_Q(self):
        """w inclui ≥ 25% da carga variável em armazenagem/estacionamento (NBR 15421:2006; Miranda 2011)."""
        for uso in ("biblioteca", "garagem"):
            self.assertGreaterEqual(sismo.fracao_Q_sismo(uso), 0.25)


class TestCombinacoes(unittest.TestCase):
    def _casos(self):
        c = {"G": CasoCarga("G", "G"), "Q1": CasoCarga("Q1", "Q", "residencial"),
             "Q2": CasoCarga("Q2", "Q", "comercial"), "S": CasoCarga("S", "S")}
        for d in ("+X", "-X", "+Y", "-Y"):
            c[f"V{d}"] = CasoCarga(f"V{d}", "V", "vento")
        return c

    def test_contagem_1G_2Q_4V_1S(self):
        """Contagem à mão pela convenção documentada (seleção de ≤ 1 caso por grupo, uma principal):
        pares = Σ n_g·Π(1 + n_h) = 1·2·5 + 1·2·5 + 4·2·2 = 36 (grupos Q1, Q2, V).
        ELU = (36 + 1 (só G)) × 2 (G desfavorável e favorável em toda combinação, VC-logica-acoes-002) = 74;
        ELS_RARA = 36; ELS_FREQ = 20 (vento secundário tem ψ2 = 0 e some: Q1 2 + Q2 2 + V 16);
        ELS_QP = 1; ELU_EXC = ±S × (γg 1,2 ou 1,0) × (Q1, Q2 com ψ2 ou ausentes, VC-logica-acoes-003) = 16."""
        cs = combinacoes.gerar_combinacoes(self._casos())
        cont = {}
        for _, _, e in cs:
            cont[e] = cont.get(e, 0) + 1
        self.assertEqual(mem.contagem_elu_normal([1, 1, 4]), 36)
        self.assertEqual(cont, {"ELU": 74, "ELS_RARA": 36, "ELS_FREQ": 20, "ELS_QP": 1, "ELU_EXC": 16})

    def test_regras_nbr8681(self):
        """NBR 8681:2003: ELU normal γg = 1,4 (tab. 2, edificação tipo 2) ou 1,0 favorável; principal γq = 1,4
        (tab. 4/5); secundárias γq·ψ0 (tab. 6: residencial 0,5, comercial 0,7, vento 0,6); nunca dois ventos;
        S só em ELU_EXC com ±1,0, γg = 1,2 (tab. 2 excepcional) ou 1,0, Q com ψ2 (0,3/0,4), sem vento; nomes únicos."""
        cs = combinacoes.gerar_combinacoes(self._casos())
        nomes = [n for n, _, _ in cs]
        self.assertEqual(len(nomes), len(set(nomes)))
        psi0 = {"Q1": 0.5, "Q2": 0.7, "V": 0.6}
        for n, f, e in cs:
            self.assertLessEqual(sum(1 for k in f if k.startswith("V")), 1, n)
            if e == "ELU":
                self.assertIn(f["G"], (1.4, 1.0), n)
                self.assertNotIn("S", f, n)
                vals = {k: v for k, v in f.items() if k != "G"}
                if not vals:
                    continue
                princ = [k for k, v in vals.items() if abs(v - 1.4) < 1e-12]
                self.assertEqual(len(princ), 1, n)
                for k, v in vals.items():
                    if k != princ[0]:
                        self.assertAlmostEqual(v, 1.4 * psi0[k[0] if k.startswith("V") else k], places=12, msg=n)
            elif e == "ELU_EXC":
                self.assertIn(abs(f["S"]), (1.0,), n)
                self.assertIn(f["G"], (1.2, 1.0), n)
                self.assertAlmostEqual(f.get("Q1", 0.3), 0.3, places=12)  # ψ2 ou ausente (VC-logica-acoes-003)
                self.assertAlmostEqual(f.get("Q2", 0.4), 0.4, places=12)
                self.assertFalse(any(k.startswith("V") for k in f), n)
            else:
                self.assertNotIn("S", f, n)

    def test_sx_sy_30_porcento(self):
        """NBR 15421:2006 8.5: 100% numa direção + 30% na ortogonal."""
        c = {"G": CasoCarga("G", "G"), "SX": CasoCarga("SX", "S"), "SY": CasoCarga("SY", "S")}
        fs = [f for _, f, e in combinacoes.gerar_combinacoes(c) if e == "ELU_EXC"]
        self.assertTrue(any(abs(f.get("SX", 0)) == 1.0 and abs(f.get("SY", 0)) == 0.3 for f in fs))
        self.assertFalse(any(abs(f.get("SX", 0)) == 1.0 and abs(f.get("SY", 0)) == 1.0 for f in fs))


class TestCargas(unittest.TestCase):
    def test_pesos(self):
        """NBR 6120:2019 tabela 1 (calculistadeaco.com.br; UFAL PFF notas de aula): concreto armado 25,
        concreto simples 24, bloco cerâmico maciço 18, aço 77–78,5 kN/m³."""
        for m, v in (("concreto_armado", 25.0), ("concreto_simples", 24.0), ("bloco_ceramico_macico", 18.0)):
            self.assertEqual(cargas.peso_especifico(m)["valor_kN_m3"], v)
        self.assertTrue(77.0 <= cargas.peso_especifico("aco")["valor_kN_m3"] <= 78.5)

    def test_usos(self):
        """NBR 6120:2019 tabela 10 (UFAL, PFF — Ações e Segurança, slide 6): dormitórios/sala 1,5;
        corredores de uso comum 3,0; escritórios 2,5 kN/m²."""
        for u, v in (("residencial_dormitorio", 1.5), ("residencial_sala_copa_cozinha", 1.5),
                     ("residencial_corredor_comum", 3.0), ("escritorio_sala_geral", 2.5)):
            self.assertEqual(cargas.carga_uso(u)["q_kN_m2"], v)


if __name__ == "__main__":
    unittest.main()
