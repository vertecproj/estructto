"""Testes de concreto (NBR 6118:2023; itens não confirmados ficam na 2014 — ver COBERTURA).
Valores de referência de livro (com página) ou derivados à mão no próprio teste com as
fórmulas da norma. As apostilas UNESP/Bastos citadas são as revisões que seguem a NBR 6118:2023."""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural.concreto import nbr6118 as c  # noqa: E402
from prossen_estrutural import secoes as s, materiais as m  # noqa: E402

TOL = 0.02


class TestViga(unittest.TestCase):
    def test_flexao_simples(self):
        # Bastos (UNESP), Flexão Normal Simples – Vigas, ex. 1: bw=20, h=50, d=47 cm,
        # C20, CA-50, Mk=100 kN·m (Md=140). Resultado: x≈18,1 cm, As≈8,10 cm², domínio 3.
        r = c.dimensionar_viga(0.20, 0.50, 0.47, 140, 50, 20)
        self.assertAlmostEqual(r["As"], 8.10, delta=8.10 * TOL)
        self.assertAlmostEqual(r["x"] * 100, 18.13, delta=18.13 * TOL)
        self.assertEqual(r["dominio"], 3)
        self.assertEqual((r["barras"]["n"], r["barras"]["phi"]), (3, 20.0))

    def test_armadura_dupla(self):
        # Carvalho & Figueiredo cap. 3 (armadura dupla): seção com M > Mlim -> A's > 0
        r = c.flexao_simples(0.20, 0.47, 250, 20, 500, d2=0.03)
        self.assertGreater(r["As2"], 0)
        self.assertAlmostEqual(r["x_d"], 0.45, places=6)
        # equilíbrio: 0,85fcd·b·0,8x + A's·σ's = As·fyd
        fcd, fyd = 2 / 1.4, 50 / 1.15
        x = 0.45 * 47
        es2 = 3.5e-3 * (x - 3) / x
        Nc = 0.85 * fcd * 20 * 0.8 * x + r["As2"] * min(fyd, 21000 * es2)
        self.assertAlmostEqual(Nc, r["As"] * fyd, delta=Nc * 1e-6)

    def test_t_mesa_comprimida(self):
        # Pinheiro (USP) cap. 10: viga T com LN na mesa equivale a retangular bf×d
        t = c.flexao_simples(0.20, 0.47, 140, 20, 500, bf=0.80, hf=0.10)
        r = c.flexao_simples(0.80, 0.47, 140, 20, 500)
        self.assertAlmostEqual(t["As"], r["As"], places=6)

    def test_as_min(self):
        self.assertAlmostEqual(c.rho_min(30), 0.00150)
        self.assertAlmostEqual(c.rho_min(40), 0.00179)

    def test_cisalhamento_modelo_I(self):
        # Bastos (UNESP), Dimensionamento de vigas ao esforço cortante: bw=20, d=45, C20:
        # VRd2 = 0,27·0,92·1,4286·20·45 = 319,4 kN ; Vc0 = 0,6·0,1105·20·45 = 59,7 kN
        r = c.cisalhamento(0.20, 0.45, 150, 20)
        self.assertAlmostEqual(r["VRd2"], 319.4, delta=319.4 * TOL)
        self.assertAlmostEqual(r["Vc"], 59.7, delta=59.7 * TOL)
        esperado = (150 - 59.67) / (0.9 * 45 * 43.48) * 100  # cm²/m
        self.assertAlmostEqual(r["Asw_s"], esperado, delta=esperado * TOL)
        # mínima: ρsw = 0,2·fctm/fywk = 0,2·2,21/500 -> 1,77 cm²/m em bw=20
        self.assertAlmostEqual(r["Asw_s_min"], 1.77, delta=1.77 * TOL)

    def test_cisalhamento_modelo_II(self):
        r1 = c.cisalhamento(0.20, 0.45, 150, 20, modelo=1)
        r2 = c.cisalhamento(0.20, 0.45, 150, 20, modelo=2, theta=45)
        self.assertAlmostEqual(r1["VRd2"], r2["VRd2"], places=6)  # θ=45° -> mesma biela
        r3 = c.cisalhamento(0.20, 0.45, 150, 20, modelo=2, theta=30)
        self.assertLess(r3["Asw_s_calc"], r2["Asw_s_calc"])

    def test_fissuracao(self):
        # 17.3.3.2: φ=16, σs=250 MPa, ρr=0,02, C25 (fctm=2,565 MPa)
        w1 = 16 / (12.5 * 2.25) * 250 / 210000 * 3 * 250 / 2.565
        w2 = 16 / (12.5 * 2.25) * 250 / 210000 * (4 / 0.02 + 45)
        self.assertAlmostEqual(c.abertura_fissura(16, 250, 0.02, 25), min(w1, w2), places=9)

    def test_flecha(self):
        # Carvalho & Figueiredo cap. 4 (Branson): b=20, h=50, d=46, As=8 cm², C25, L=6 m
        r = c.flecha_viga(0.20, 0.50, 0.46, 8.0, 80, 6.0, 17.8, 25)
        Eci = 5600 * 5 / 10
        Ecs = (0.8 + 0.2 * 25 / 80) * Eci  # kN/cm²
        Ic = 20 * 50 ** 3 / 12
        Mr = 1.5 * 0.3 * 25 ** (2 / 3) / 10 * Ic / 25
        ae = 21000 / Ecs
        x = (-ae * 8 + math.sqrt((ae * 8) ** 2 + 2 * 20 * ae * 8 * 46)) / 20
        I2 = 20 * x ** 3 / 3 + ae * 8 * (46 - x) ** 2
        r3 = (Mr / 8000) ** 3
        Ieq = r3 * Ic + (1 - r3) * I2
        fi = 5 / 384 * 0.178 * 600 ** 4 / (Ecs * Ieq) * 10
        self.assertAlmostEqual(r["f_imediata"], fi, delta=fi * TOL)
        self.assertAlmostEqual(r["alfa_f"], 2 - 0.68 * 0.996 * 1, delta=1e-3)  # ξ(∞)−ξ(1)=1,32
        self.assertAlmostEqual(r["f_total"], fi * 2.32, delta=fi * 2.32 * TOL)


class TestPilar(unittest.TestCase):
    def test_segunda_ordem_curvatura(self):
        # Bastos (UNESP), Pilares — pilar intermediário: 20×50, le=2,80 m, C25, Nd=1000 kN
        # λ=48,5 ; M1d,min=21 kN·m ; 1/r=0,005/(0,2·(ν+0,5)) ; Md,tot≈39,5 kN·m
        r = c.dimensionar_pilar(0.50, 0.20, 1000, 0, 0, 2.8, 25)
        dx = r["direcoes"]["x"]
        self.assertAlmostEqual(dx["lam"], 48.5, delta=0.5)
        self.assertAlmostEqual(dx["M1d_min"], 21.0, places=6)
        self.assertAlmostEqual(dx["Md_tot"], 39.49, delta=39.49 * TOL)

    def test_segunda_ordem_kappa(self):
        # 15.8.3.3.3: a solução deve satisfazer Md = M1/(1 − λ²/(120κ/ν)), κ = 32(1+5Md/(h·Nd))ν
        Nd, h, le, b = 1000, 0.20, 2.8, 0.50
        M = c.segunda_ordem(Nd, 21, h, le, 25, b, metodo="kappa")
        nu = Nd / (b * h * 1e4 * 2.5 / 1.4)
        lam = le * math.sqrt(12) / h
        kappa = 32 * (1 + 5 * M / (h * Nd)) * nu
        self.assertAlmostEqual(M, 21 / (1 - lam ** 2 / (120 * kappa / nu)), delta=1e-6)

    def test_compressao_centrada(self):
        # N = 0,85fcd·Ac + As·σs(εc2=2‰): 20×50, C25, Nd=1800 -> As=(1800−1517,9)/42 = 6,72 cm²
        As = c.flexo_compressao(0.50, 0.20, 1800, 0.0, 25)
        self.assertAlmostEqual(As, 6.72, delta=6.72 * TOL)

    def test_flexo_compressao_equilibrio(self):
        As = c.flexo_compressao(0.20, 0.50, 800, 120, 25)
        p = c.parametros(25)
        ys = c._barras_perimetro(20, 50, 4, 2, 0)
        M = c._MRd(800, 20, 50, ys, As / 4, p, 50 / 1.15)
        self.assertAlmostEqual(M / 100, 120, delta=120 * 0.005)

    def test_obliqua_e_limites(self):
        r = c.dimensionar_pilar(0.30, 0.30, 1200, 40, 40, 3.0, 30)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["interacao_obliqua"], 1.0)
        self.assertGreaterEqual(r["As"], r["As_min"])
        self.assertAlmostEqual(r["As_min"], max(0.15 * 1200 / 43.48, 0.004 * 900), places=3)


class TestLaje(unittest.TestCase):
    def test_marcus_quadrada(self):
        # Marcus, laje quadrada apoiada nos 4 bordos: m ≈ p·l²/27,4 (Pinheiro/USP, cap. 11)
        r = c.dimensionar_laje(4.0, 4.0, 0.10, 10.0, 25)
        self.assertAlmostEqual(r["direcoes"]["x"]["Mk"], 10 * 16 / 27.4, delta=0.5844 * TOL)
        self.assertAlmostEqual(r["direcoes"]["x"]["Mk"], r["direcoes"]["y"]["Mk"], places=9)

    def test_unidirecional(self):
        r = c.dimensionar_laje(3.0, 7.0, 0.10, 8.0, 25)
        self.assertEqual(r["tipo"], "unidirecional")
        self.assertAlmostEqual(r["direcoes"]["x"]["Mk"], 8 * 9 / 8)


# ------------------------------------------------------------------ interface nova (ESPEC §7.2)
def _viga_biapoiada(qd, L, n=13):
    """Linhas (x, N, Vy, Vz, T, My, Mz) de uma viga biapoiada com carga uniforme qd (kN/m),
    na convenção do solver (My < 0 no vão)."""
    xs = [L * i / (n - 1) for i in range(n)]
    return [(x, 0.0, 0.0, qd * (L / 2 - x), 0.0, -qd * x * (L - x) / 2, 0.0) for x in xs]


SEM_ELS = {"qp": None, "freq": None}


class TestEdicao2023(unittest.TestCase):
    def test_eta_c(self):
        # NBR 6118:2023 8.2.10.1 / 17.2.2 e): ηc = 1 (fck ≤ 40) e (40/fck)^(1/3) — Bastos,
        # Fundamentos do Concreto Armado (UNESP, set/2023), p. 23, Eq. 2.18.
        self.assertEqual(c.parametros(40)["eta_c"], 1.0)
        self.assertAlmostEqual(c.parametros(50)["eta_c"], 0.9283, places=4)

    def test_flexao_com_eta_c(self):
        # σcd = αc·ηc·fcd (Bastos, Flexão Normal Simples — Vigas, UNESP out/2023, p. 13, Eq. 16)
        r = c.flexao_simples(0.20, 0.47, 140, 50, 500)
        sc = 0.85 * (40 / 50) ** (1 / 3) * 5 / 1.4  # kN/cm²
        mu = 14000 / (sc * 20 * 47 ** 2)
        x = 47 / 0.8 * (1 - math.sqrt(1 - 2 * mu))
        As = 14000 / (50 / 1.15 * (47 - 0.4 * x))
        self.assertAlmostEqual(r["x"] * 100, x, places=6)
        self.assertAlmostEqual(r["As"], As, places=6)

    def test_fctm_grupo_II(self):
        # NBR 6118:2023 8.2.5: fct,m = 2,12·ln[1 + 0,1(fck + 8)] (Bastos 2023, p. 20, Eq. 2.8)
        self.assertAlmostEqual(c.parametros(70)["fctm"] * 10, 2.12 * math.log(1 + 0.1 * 78), places=9)

    def test_edicao_e_cobertura(self):
        self.assertTrue(c.EDICAO.startswith("NBR 6118:2023"))
        chaves = {"norma", "edicao", "item", "verificacao", "limitacao"}
        self.assertTrue(all(set(x) == chaves for x in c.COBERTURA))
        self.assertIn("2014", {x["edicao"] for x in c.COBERTURA})  # laje (não confirmada)


class TestRamos(unittest.TestCase):
    def test_xd_no_limite(self):
        # Md = Mlim → x/d = 0,45 sem armadura dupla; acima → A's > 0 sem salto em As (14.6.4.3)
        Mlim = c.flexao_simples(0.20, 0.47, 100, 25, 500)["Mlim"]
        r1 = c.flexao_simples(0.20, 0.47, Mlim, 25, 500)
        r2 = c.flexao_simples(0.20, 0.47, Mlim * (1 + 1e-6), 25, 500)
        self.assertAlmostEqual(r1["x_d"], 0.45, places=6)
        self.assertEqual(r1["As2"], 0.0)
        self.assertGreater(r2["As2"], 0.0)
        self.assertAlmostEqual(r2["As"], r1["As"], delta=r1["As"] * 1e-5)

    def test_lambda_igual_lambda1(self):
        # 15.8.2: λ ≤ λ1 dispensa 2ª ordem; logo acima de λ1 entra M2 (pilar 20×50, Nd=1000, C25)
        h = 0.20
        le1 = 35 * h / math.sqrt(12)  # λ = 35 = λ1 (e1 = 0)
        r1 = c.dimensionar_pilar(0.50, h, 1000, 0, 0, le1, 25)["direcoes"]["x"]
        r2 = c.dimensionar_pilar(0.50, h, 1000, 0, 0, le1 * 1.001, 25)["direcoes"]["x"]
        self.assertAlmostEqual(r1["lam"], 35.0, places=9)
        self.assertEqual(r1["Md_tot"], r1["M1d"])
        self.assertGreater(r2["Md_tot"], r2["M1d"])

    def test_parabola_retangulo_pilar(self):
        # 8.2.10.1: integração analítica da parábola-retângulo == integração numérica (2000 fatias)
        p = c.parametros(30)
        ys = c._barras_perimetro(50, 20, 4, 2, 0)
        for x in (5.0, 12.0, 19.0, 26.0):
            N, M = c._esforcos_secao(x, 50, 20, ys, 0.0, p, 50 / 1.15)
            k = c._curvatura(x, 20, ys, p)
            n_, fc = 2000, 0.85 * p["eta_c"] * p["fcd"]
            Nn = Mn = 0.0
            for i in range(n_):
                y = (i + 0.5) * 20 / n_
                e = k * (x - y)
                sc = 0 if e <= 0 else (fc if e >= p["ec2"] else fc * (1 - (1 - e / p["ec2"]) ** p["n"]))
                Nn += sc * 50 * 20 / n_
                Mn += sc * 50 * 20 / n_ * (10 - y)
            self.assertAlmostEqual(N, Nn, delta=abs(Nn) * 1e-4)
            self.assertAlmostEqual(M, Mn, delta=abs(Mn) * 1e-3 + 1e-6)


class TestVerificarBarra(unittest.TestCase):
    def test_viga_flexao_bastos(self):
        """Bastos, Flexão Normal Simples — Vigas (UNESP, out/2023), ex. 1, p. 18–20:
        bw=20, h=50, d=47 cm, C20, CA-50, Md = 140 kN·m → x = 18,1 cm, As = 8,10 cm², domínio 3.
        Esforços montados à mão: viga biapoiada L = 5 m, qd = 8·140/25 = 44,8 kN/m."""
        sec, mat = s.secao_retangular(0.20, 0.50), m.concreto(20)
        elu = [{"combinacao": "ELU 1", "linhas": _viga_biapoiada(44.8, 5.0)}]
        r = c.verificar_barra_concreto(sec, mat, elu, SEM_ELS, 5.0, "viga",
                                       {"d_m": 0.47, "caa": "I", "cobrimento_m": 0.025})
        self.assertAlmostEqual(r["armadura"]["As_cm2"], 8.10, delta=8.10 * TOL)
        xd = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "x_d")
        self.assertAlmostEqual(xd["x_d"] * 47, 18.1, delta=18.1 * TOL)
        self.assertEqual(xd["dominio"], 3)
        self.assertEqual(xd["indice"], 6)  # meio do vão
        self.assertEqual(r["combinacao"], "ELU 1")
        self.assertTrue(r["ok"])
        json.dumps(r)

    def test_viga_cortante_bastos(self):
        """Bastos, Dimensionamento de vigas à força cortante (UNESP, NBR 6118:2023), ex. 1,
        p. 41–43: bw=14, h=50, d=46 cm, C25, CA-50, VSd = 153,0 kN (L = 5 m, qd = 61,2 kN/m).
        Modelo I: VRd2 = 279,5 kN; Vc = 49,6 kN; Asw/s = 5,73 cm²/m."""
        sec, mat = s.secao_retangular(0.14, 0.50), m.concreto(25)
        elu = [{"combinacao": "ELU", "linhas": _viga_biapoiada(61.2, 5.0)}]
        r = c.verificar_barra_concreto(sec, mat, elu, SEM_ELS, 5.0, "viga",
                                       {"d_m": 0.46, "caa": "I", "cobrimento_m": 0.025})
        v = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "cisalhamento_VRd2")
        self.assertAlmostEqual(v["resistente"], 279.5, delta=279.5 * TOL)
        self.assertAlmostEqual(v["solicitante"], 153.0, delta=153.0 * TOL)
        self.assertAlmostEqual(v["Vc"], 49.6, delta=49.6 * TOL)
        self.assertAlmostEqual(r["armadura"]["Asw_s_cm2_m"], 5.73, delta=5.73 * TOL)
        self.assertIn(v["indice"], (0, 12))
        json.dumps(r)

    def test_pilar_bastos(self):
        """Bastos, Pilares de Concreto Armado (UNESP, jun/2024, NBR 6118:2023), ex. 13.1.1,
        p. 79–82: 20×50, le = 280 cm, C30, d' = 4 cm, Nd = 1400 kN (pilar intermediário).
        λ(20 cm) = 48,4; M1d,mín = 29,4 kN·m; Md,tot = 53,2 kN·m; dir. 50 cm: λ = 19,4,
        Md,tot = 42,0 kN·m. As = 10,84 cm² (ábaco A-4 de Venturini, ω = 0,22 lido no gráfico
        com 2 algarismos; a integração exata dá ω ≈ 0,21: tolerância de 5% só nesta grandeza)."""
        sec, mat = s.secao_retangular(0.50, 0.20), m.concreto(30)  # h = 20 cm no plano de My
        linhas = [(x, -1400.0, 0.0, 0.0, 0.0, 0.0, 0.0) for x in (0.0, 1.4, 2.8)]
        r = c.verificar_barra_concreto(sec, mat, [{"combinacao": "ELU", "linhas": linhas}],
                                       SEM_ELS, 2.8, "pilar", {"caa": "I", "cobrimento_m": 0.025})
        dx = r["detalhes"]["pilar"]["direcoes"]["x"]
        dy = r["detalhes"]["pilar"]["direcoes"]["y"]
        self.assertAlmostEqual(dx["lam"], 48.4, delta=48.4 * TOL)
        self.assertAlmostEqual(dx["M1d_min"], 29.4, delta=29.4 * TOL)
        self.assertAlmostEqual(dx["Md_tot"], 53.2, delta=53.2 * TOL)
        self.assertAlmostEqual(dy["lam"], 19.4, delta=19.4 * TOL)
        self.assertAlmostEqual(dy["Md_tot"], 42.0, delta=42.0 * TOL)
        self.assertAlmostEqual(r["armadura"]["As_pilar_cm2"], 10.84, delta=10.84 * 0.05)
        self.assertTrue(r["ok"])
        json.dumps(r)

    def test_pilar_esbelto_aviso(self):
        sec, mat = s.secao_retangular(0.50, 0.20), m.concreto(30)
        linhas = [(x, -500.0, 0.0, 0.0, 0.0, 0.0, 0.0) for x in (0.0, 3.0, 6.0)]
        r = c.verificar_barra_concreto(sec, mat, [{"combinacao": "ELU", "linhas": linhas}],
                                       SEM_ELS, 6.0, "pilar")
        self.assertTrue(any("90" in a for a in r["avisos"]))  # λ = 103,9 > 90
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "esbeltez")

    def test_pilar_tracionado(self):
        # tração → tirante As = Nt/fyd (aviso): Nt = 200 kN → 200/43,48 = 4,60 cm²
        sec, mat = s.secao_retangular(0.30, 0.30), m.concreto(30)
        linhas = [(x, 200.0, 0.0, 0.0, 0.0, 0.0, 0.0) for x in (0.0, 3.0)]
        r = c.verificar_barra_concreto(sec, mat, [{"combinacao": "ELU", "linhas": linhas}],
                                       SEM_ELS, 3.0, "pilar")
        self.assertAlmostEqual(r["armadura"]["As_pilar_cm2"], 200 / (50 / 1.15), places=6)
        self.assertTrue(any("tração" in a for a in r["avisos"]))

    def test_secao_nao_retangular(self):
        with self.assertRaises(ValueError):
            c.verificar_barra_concreto(s.secao_circular(0.4), m.concreto(30),
                                       [{"combinacao": "E", "linhas": [(0, 0, 0, 0, 0, 0, 0)]}],
                                       SEM_ELS, 3.0, "pilar")

    def test_els_flecha_e_fissura(self):
        """Flecha: f = f_elástica·E·Ic/(Ecs·Ieq)·(1 + αf), Branson (17.3.2.1.1) e
        αf = Δξ/(1 + 50ρ') com ρ' = 0 (a favor da segurança) e t0 = 1 mês:
        ξ(1) = 0,68·0,996¹·1^0,32 (Tabela 17.1) → αf = 2 − 0,67728 = 1,32272.
        Seção 20×50, d = 46, C25 (E do material = Ecs), Ma = 80 kN·m (qp), f_el = 5 mm."""
        sec, mat = s.secao_retangular(0.20, 0.50), m.concreto(25)
        q = 80 * 8 / 36
        elu = [{"combinacao": "ELU", "linhas": _viga_biapoiada(1.4 * q, 6.0)}]
        qp = {"combinacao": "QP", "linhas": _viga_biapoiada(q, 6.0), "flecha_elastica_m": -0.005}
        fr = {"combinacao": "FREQ", "linhas": _viga_biapoiada(q, 6.0)}
        r = c.verificar_barra_concreto(sec, mat, elu, {"qp": qp, "freq": fr}, 6.0, "viga", {"d_m": 0.46})
        As = r["armadura"]["As_cm2"]
        Ecs = (0.8 + 0.2 * 25 / 80) * 5600 * 5 / 10  # kN/cm²
        Ic = 20 * 50 ** 3 / 12
        Mr = 1.5 * 0.3 * 25 ** (2 / 3) / 10 * Ic / 25
        ae = 21000 / Ecs
        x = (-ae * As + math.sqrt((ae * As) ** 2 + 2 * 20 * ae * As * 46)) / 20
        I2 = 20 * x ** 3 / 3 + ae * As * (46 - x) ** 2
        r3 = (Mr / 8000) ** 3
        Ieq = r3 * Ic + (1 - r3) * I2
        f = 0.005 * Ic / Ieq * (1 + 2 - 0.68 * 0.996)
        v = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha")
        self.assertAlmostEqual(v["solicitante"], f, delta=f * 1e-6)
        self.assertAlmostEqual(v["resistente"], 6.0 / 250, places=12)
        self.assertEqual(v["combinacao"], "QP")
        w = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "fissuracao_wk")
        self.assertEqual(w["resistente"], 0.3)  # CAA II (padrão), Tabela 13.4
        self.assertEqual(w["combinacao"], "FREQ")
        json.dumps(r)

    def test_torcao_na_verificacao(self):
        sec, mat = s.secao_retangular(0.35, 0.50), m.concreto(25)
        linhas = [(x, 0.0, 0.0, 83.4, 68.08, 129.39 * (x - 1.5) / 1.5, 0.0) for x in (0.0, 0.75, 1.5)]
        r = c.verificar_barra_concreto(sec, mat, [{"combinacao": "ELU", "linhas": linhas}],
                                       SEM_ELS, 1.5, "viga")
        crit = {v["criterio"] for v in r["detalhes"]["verificacoes"]}
        self.assertIn("torcao_interacao", crit)
        self.assertGreater(r["armadura"]["Asl_torcao_cm2"], 0)
        json.dumps(r)


class TestCorrecoes018(unittest.TestCase):
    def test_vc_flexo_tracao_LN_na_secao(self):
        # 17.4.2.2 c: N = 300, Md = 100 → e = 0,333 m > d − h/2 = 0,205 m: LN corta a seção, Vc = Vc0
        r0 = c.cisalhamento(0.20, 0.455, 100, 25)
        r = c.cisalhamento(0.20, 0.455, 100, 25, N=300, Md=100, h=0.50)
        self.assertAlmostEqual(r["Vc"], r0["Vc"], places=12)
        self.assertEqual(c.cisalhamento(0.20, 0.455, 100, 25, N=300, Md=50, h=0.50)["Vc"], 0.0)

    def test_ca60_bitolas_de_fio(self):
        # NBR 7480: CA-60 é fio (φ ≤ 10 mm)
        self.assertLessEqual(c.escolher_barras(5.0, 0.20, fyk=600)["phi"], 10.0)
        self.assertGreater(c.escolher_barras(5.0, 0.20)["phi"], 10.0)

    def test_alfa_b_por_plano(self):
        # 15.8.2: αb por plano; λ1 = (25 + 12,5·e1/h)/αb com e1 = 40/1000 m
        r = c.dimensionar_pilar(0.50, 0.20, 1000, 40, 40, 5.0, 25, alfa_b=(0.4, 1.0))["direcoes"]
        self.assertAlmostEqual(r["x"]["lam1"], (25 + 12.5 * 0.04 / 0.20) / 0.4, delta=1e-9)
        self.assertEqual(r["y"]["alfa_b"], 1.0)


FYD = 50 / 1.15  # kN/cm², CA-50


def _barras(txt):
    """'6 ϕ 32' → (6, 32.0, área cm²)."""
    n, phi = txt.split(" ϕ ")
    return int(n), float(phi), int(n) * math.pi * float(phi) ** 2 / 400


class TestCorrecoes026(unittest.TestCase):
    """Revisão 2026-10-20 (B-2, I-1 e MENOR do concreto)."""

    def test_flexo_tracao_grande_excentricidade(self):
        """B-2. Viga 20×50, C25, CA-50, CAA II: d' = 3 + 0,5 + 1,0 = 4,5 cm, d = 45,5 cm.
        Nd = +150 kN (tração), Md = 100 kN·m. a = d − h/2 = 20,5 cm; e = 100/150 = 66,7 cm > a → grande exc.
        Ms = 100 − 150·0,205 = 69,25 kN·m = 6925 kN·cm.
        σcd = 0,85·2,5/1,4 = 1,51786 kN/cm²; μ = 6925/(1,51786·20·45,5²) = 6925/62846,9 = 0,11019
        x = 45,5/0,8·(1 − √(1 − 2·0,11019)) = 56,875·0,117034 = 6,656 cm (x/d = 0,1463)
        z = 45,5 − 0,4·6,656 = 42,837 cm; As(Ms) = 6925/(43,478·42,837) = 3,718 cm²
        As = 3,718 + 150/43,478 = 3,718 + 3,450 = 7,168 cm² (flexão simples, N = 0: 5,538 cm²)."""
        r = c.flexo_tracao(0.20, 0.50, 0.455, 150, 100, 25, 500, d2=0.045)
        self.assertEqual(r["excentricidade"], "grande")
        self.assertAlmostEqual(r["Ms"], 69.25, places=9)
        self.assertAlmostEqual(r["x_d"], 0.1463, delta=0.1463 * 0.005)
        self.assertAlmostEqual(r["As"], 7.168, delta=7.168 * 0.005)
        self.assertEqual((r["As_oposta"], r["As2"]), (0.0, 0.0))
        self.assertIn("17.2.2", r["item"])
        # mesma viga pela verificação de barra (My < 0 traciona a face inferior)
        ln = [(x, 150.0, 0.0, 0.0, 0.0, -100.0, 0.0) for x in (0.0, 2.5, 5.0)]
        v = c.verificar_barra_concreto(s.secao_retangular(0.20, 0.50), m.concreto(25),
                                       [{"combinacao": "ELU", "linhas": ln}], SEM_ELS, 5.0, "viga")
        self.assertAlmostEqual(v["armadura"]["As_cm2"], 7.168, delta=7.168 * 0.005)
        self.assertTrue(any("flexo-tração" in a for a in v["avisos"]))
        json.dumps(v)

    def test_flexo_tracao_pequena_excentricidade(self):
        """B-2. Mesma seção, Nd = 600 kN, Md = 100 kN·m: e = 16,67 cm ≤ a = 20,5 cm → pequena exc.
        (seção toda tracionada). a' = h/2 − d' = 20,5 cm, a + a' = 41 cm.
        As = 600·(20,5 + 16,667)/(41·43,478) = 22300/1782,6 = 12,51 cm²
        As' = 600·(20,5 − 16,667)/(41·43,478) = 2300/1782,6 = 1,290 cm² (face oposta, tracionada)."""
        r = c.flexo_tracao(0.20, 0.50, 0.455, 600, 100, 25, 500, d2=0.045)
        self.assertEqual(r["excentricidade"], "pequena")
        self.assertAlmostEqual(r["As"], 12.510, delta=0.005)
        self.assertAlmostEqual(r["As_oposta"], 1.290, delta=0.005)
        # na viga: a face oposta (superior) recebe As' como armadura tracionada
        ln = [(x, 600.0, 0.0, 0.0, 0.0, -100.0, 0.0) for x in (0.0, 5.0)]
        v = c.verificar_barra_concreto(s.secao_retangular(0.20, 0.50), m.concreto(25),
                                       [{"combinacao": "ELU", "linhas": ln}], SEM_ELS, 5.0, "viga")
        self.assertAlmostEqual(v["armadura"]["As_cm2"], 12.510, delta=0.005)
        As_min = 0.0015 * 1000  # Tabela 17.3, C25: 0,150 %·Ac
        self.assertAlmostEqual(v["armadura"]["As_neg_cm2"], max(1.290, As_min), delta=0.005)

    def test_flexo_tracao_continua(self):
        """B-2, sem salto. Em e = a os dois ramos dão As = Nd/fyd e As' = 0: Nd = 200 kN,
        Md = 200·0,205 = 41 kN·m → As = 200/43,478 = 4,600 cm². E na viga (Md = 100 kN·m) a passagem
        pelo antigo limiar 0,1·fcd·Ac = 0,1·1,7857·1000 = 178,6 kN não pode saltar: dAs/dN ≈
        1/fyd·(1 − a/z) ≈ 0,0115 cm²/kN → |As(179) − As(178)| < 0,02 cm²."""
        for f in (1 - 1e-9, 1 + 1e-9):
            r = c.flexo_tracao(0.20, 0.50, 0.455, 200, 41 * f, 25, 500, d2=0.045)
            self.assertAlmostEqual(r["As"], 200 / FYD, delta=1e-6)
            self.assertAlmostEqual(r["As_oposta"] + r["As2"], 0.0, delta=1e-6)

        def As(N):
            ln = [(0.0, N, 0.0, 0.0, 0.0, -100.0, 0.0), (5.0, N, 0.0, 0.0, 0.0, -100.0, 0.0)]
            return c.verificar_barra_concreto(s.secao_retangular(0.20, 0.50), m.concreto(25),
                                              [{"combinacao": "E", "linhas": ln}], SEM_ELS, 5.0,
                                              "viga")["armadura"]["As_cm2"]
        a0, a1, a2 = As(0.0), As(178.0), As(179.0)
        self.assertAlmostEqual(a0, 5.538, delta=0.01)
        self.assertGreater(a1, a0)
        self.assertLess(abs(a2 - a1), 0.02)

    def test_vc_flexo_tracao_na_viga_abaixo_do_antigo_limiar(self):
        """B-2 (17.4.2.2 c): N = +100 kN (< 178,6), M = 0 → tirante, LN fora da seção, Vc = 0:
        Asw/s = Vd/(0,9·d·fywd) = 100/(0,9·45,5·43,478)·100 = 5,617 cm²/m."""
        ln = [(x, 100.0, 0.0, 100.0, 0.0, 0.0, 0.0) for x in (0.0, 5.0)]
        r = c.verificar_barra_concreto(s.secao_retangular(0.20, 0.50), m.concreto(25),
                                       [{"combinacao": "E", "linhas": ln}], SEM_ELS, 5.0, "viga")
        v = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "cisalhamento_VRd2")
        self.assertEqual(v["Vc"], 0.0)
        self.assertAlmostEqual(r["armadura"]["Asw_s_cm2_m"], 100 / (0.9 * 45.5 * FYD) * 100, delta=1e-6)

    def test_barras_pilar_nao_menores_que_As(self):
        """I-1. Pilar 30×30, C25, Nd = 1500 kN, Md = 120 kN·m, L = le = 4 m (λ = 46,2 > λ1 = 35, entra
        M2): As ≈ 37 cm² > 4φ32 = 4·π·3,2²/4 = 32,17 cm² → 4 barras não bastam; a indicação tem de ter
        n·π·φ²/4 ≥ As e φ ≤ 300/8 = 37,5 mm."""
        ln = [(x, -1500.0, 0.0, 0.0, 0.0, 120.0, 0.0) for x in (0.0, 4.0)]
        r = c.verificar_barra_concreto(s.secao_retangular(0.30, 0.30), m.concreto(25),
                                       [{"combinacao": "E", "linhas": ln}], SEM_ELS, 4.0, "pilar")
        As = r["armadura"]["As_pilar_cm2"]
        self.assertGreater(As, 4 * math.pi * 3.2 ** 2 / 4)
        n, phi, Aef = _barras(r["armadura"]["barras"])
        self.assertGreater(n, 4)
        self.assertGreaterEqual(Aef, As)
        self.assertLessEqual(phi, 300 / 8)  # 18.4.2.1: φl ≤ b/8
        d = c.dimensionar_pilar(0.30, 0.30, 1500, 120, 0, 4.0, 25)
        self.assertGreaterEqual(d["n_barras"] * math.pi * d["phi_l"] ** 2 / 400, d["As"])
        self.assertEqual(d["n_barras"], 2 * d["nb"] + 2 * d["nh"])

    def test_barras_pilar_tirante_e_compressao(self):
        """I-1 com duas combinações: a indicação vale para o maior As, qualquer que seja a ordem. Tirante
        Nt = 400 kN, Mx = My = 60 kN·m em 30×30 (d' = 4,5 cm, z = 21 cm), com a disposição da compressão
        (n = 2nb + 2nh barras iguais; barra de canto: Nt/n + Mx/(z·nb) + My/(z·(nh + 2))). Com nb = 3, nh = 0:
        As = (400 + 6·60/(0,21·3) + 6·60/(0,21·2))/43,478 = (400 + 571,4 + 857,1)/43,478 = 42,06 cm²."""
        comp = [(x, -1500.0, 0.0, 0.0, 0.0, 120.0, 0.0) for x in (0.0, 4.0)]
        trac = [(x, 400.0, 0.0, 0.0, 0.0, 60.0, 60.0) for x in (0.0, 4.0)]
        for ordem in ((comp, trac), (trac, comp)):
            r = c.verificar_barra_concreto(s.secao_retangular(0.30, 0.30), m.concreto(25),
                                           [{"combinacao": f"E{k}", "linhas": l} for k, l in enumerate(ordem)],
                                           SEM_ELS, 4.0, "pilar")
            nb, nh = r["detalhes"]["nb"], r["detalhes"]["nh"]
            n = 2 * nb + 2 * nh
            esp = (400 + n * 60 / (0.21 * nb) + n * 60 / (0.21 * (nh + 2))) / FYD
            if (nb, nh) == (3, 0):
                self.assertAlmostEqual(esp, 42.06, delta=0.01)
            self.assertAlmostEqual(r["armadura"]["As_pilar_cm2"], esp, delta=1e-6)
            self.assertGreaterEqual(_barras(r["armadura"]["barras"])[2], esp)

    def test_barras_pilar_sem_disposicao_reprova(self):
        """I-1. 20×20, d' = 4 cm, φl fixado em 10 mm: espaçamento livre mínimo (18.4.2.2) =
        máx(20; 10; 1,2·19) = 22,8 mm; vão entre centros extremos 120 mm → n − 1 ≤ 120/(22,8 + 10) = 3,66
        → no máximo 4 barras por face, 12 no total = 12·0,785 = 9,42 cm². Nd = 1200 kN exige, só na
        compressão centrada, As > (1200 − 0,85·1,7857·400)/42 = (1200 − 607,1)/42 = 14,1 cm² > 9,42 (com
        M1d,mín = 1200·0,021 = 25,2 kN·m, mais ainda, mas ≤ 8%·400 = 32 cm², emenda por luva) → sem indicação
        e reprovado."""
        r = c.dimensionar_pilar(0.20, 0.20, 1200, 0, 0, 1.0, 25, phi_l=10.0, emenda="luva")
        self.assertGreater(r["As"], 14.1)
        self.assertLessEqual(r["As"], r["As_max"])
        self.assertFalse(r["ok"])
        self.assertIsNone(r["phi_l"])
        self.assertTrue(any("18.4.2" in a for a in r["alertas"]))

    def test_flecha_com_fator_EI(self):
        """MENOR. O solver usa E·(fator_EI·Ic): f = f_el·E·fator·Ic/(Ecs·Ieq)·(1 + αf). Com fator = 0,5 e
        f_el dobrada (10 mm) a flecha corrigida é a mesma de fator = 1 e f_el = 5 mm."""
        sec, mat = s.secao_retangular(0.20, 0.50), m.concreto(25)
        q = 80 * 8 / 36
        elu = [{"combinacao": "ELU", "linhas": _viga_biapoiada(1.4 * q, 6.0)}]

        def f(fel, fator):
            qp = {"combinacao": "QP", "linhas": _viga_biapoiada(q, 6.0), "flecha_elastica_m": fel}
            r = c.verificar_barra_concreto(sec, mat, elu, {"qp": qp, "freq": None}, 6.0, "viga",
                                           {"fator_EI": fator})
            return next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha")["solicitante"]
        self.assertAlmostEqual(f(0.010, 0.5), f(0.005, 1.0), delta=1e-12)

    def test_flecha_viga_momento_negativo(self):
        """MENOR. Branson usa |Ma|: Ma = −80 kN·m (balanço) fissura igual a +80."""
        a = c.flecha_viga(0.20, 0.50, 0.46, 6.0, 80, 6.0, 17.8, 25)
        b = c.flecha_viga(0.20, 0.50, 0.46, 6.0, -80, 6.0, 17.8, 25)
        self.assertAlmostEqual(a["Ieq"], b["Ieq"], places=9)
        self.assertLess(a["Ieq"], a["Ic"])


class TestTorcao(unittest.TestCase):
    def test_bastos_torcao(self):
        """Bastos, Torção em Vigas de Concreto Armado (UNESP, mai/2024, NBR 6118:2023), ex. 1,
        viga V1, p. 34–36: 35×50, C25, CA-50, TSd = 68,08 kN·m, he = 10 cm, θ = 38°,
        VSd = 83,4 kN, VRd2 = 679,5 kN → TRd2 = 77,97 kN·m; interação 0,996; Asl/ue = 10,02 cm²/m;
        A90/s = 6,12 cm²/m; mínimos 1,03 e 3,58 cm²/m."""
        r = c.torcao(0.35, 0.50, 68.08, 25, c1=0.04125, Vd=83.4, VRd2=679.5, theta=38, he=0.10)
        self.assertAlmostEqual(r["TRd2"], 77.97, delta=77.97 * TOL)
        self.assertAlmostEqual(r["interacao"], 0.996, delta=0.996 * TOL)
        self.assertAlmostEqual(r["Asl_ue"], 10.02, delta=10.02 * TOL)
        self.assertAlmostEqual(r["A90_s"], 6.12, delta=6.12 * TOL)
        self.assertAlmostEqual(r["Asl_ue_min"], 1.03, delta=1.03 * TOL)
        # 3,58 cm²/m é o mínimo do estribo fechado (2 ramos, ρsw·bw — 17.5.1.2): por ramo, metade
        self.assertAlmostEqual(2 * r["A90_s_min"], 3.58, delta=3.58 * TOL)
        self.assertTrue(r["ok"])

    def test_he_limites(self):
        # 17.5.1.4.1: 2c1 ≤ he ≤ A/u. Biela com o menor he; armaduras com o maior (a favor da segurança)
        r = c.torcao(0.35, 0.50, 50, 25, c1=0.04125)
        self.assertAlmostEqual(r["he_biela_m"], 2 * 0.04125, places=12)
        self.assertAlmostEqual(r["he_m"], 0.35 * 0.50 / (2 * 0.85), places=12)


class TestGamaZ(unittest.TestCase):
    PAV = [(3.0, 0.002), (6.0, 0.005), (9.0, 0.008), (12.0, 0.010)]

    def _pav(self, k):
        return [{"z_m": z, "H_kN": 10.0, "P_kN": 1000.0, "delta_m": k * d} for z, d in self.PAV]

    def test_manual(self):
        # M1 = 10·(3+6+9+12) = 300 kN·m ; ΔM = 1000·(0,002+0,005+0,008+0,010) = 25 kN·m
        r = c.gama_z(self._pav(1))
        self.assertAlmostEqual(r["M1_kNm"], 300.0, delta=1e-9)
        self.assertAlmostEqual(r["dM_kNm"], 25.0, delta=1e-9)
        self.assertAlmostEqual(r["gama_z"], 1 / (1 - 25 / 300), delta=1e-9)
        self.assertEqual(r["classificacao"], "nos_fixos")
        self.assertEqual(r["item"], "15.5.3")
        json.dumps(r)

    def test_classes(self):
        r = c.gama_z(self._pav(2))  # 1/(1 − 50/300) = 1,2
        self.assertAlmostEqual(r["gama_z"], 1.2, delta=1e-9)
        self.assertEqual(r["classificacao"], "nos_moveis")
        self.assertAlmostEqual(r["majoracao"], 0.95 * 1.2, delta=1e-9)
        r = c.gama_z(self._pav(3))  # 1/(1 − 75/300) = 1,333
        self.assertEqual(r["classificacao"], "fora_do_limite")
        self.assertTrue(r["avisos"])
        with self.assertRaises(ValueError):
            c.gama_z(self._pav(12))  # ΔM = M1 → instabilidade

    def test_menos_de_4_pavimentos(self):
        r = c.gama_z(self._pav(1)[:3])
        self.assertTrue(any("quatro" in a for a in r["avisos"]))


class TestLimites(unittest.TestCase):
    def test_tabela_13_3(self):
        # NBR 6118:2023 Tabela 13.3 (Bastos, Vigas de Concreto Armado, UNESP abr/2024, p. 57–58)
        L = 7.0
        casos = {"aceitabilidade": (L / 250, "L/250"), "apos_construcao": (L / 350, "L/350"),
                 "balanco": (2 * L / 250, "2L/250"), "horizontal_topo": (L / 1700, "H/1700"),
                 "entre_pisos": (L / 850, "h/850"), "alvenaria": (min(L / 500, 0.010), "L/500")}
        for el, (lim, razao) in casos.items():
            r = c.limite_deslocamento(el, L)
            self.assertAlmostEqual(r["limite_m"], lim, places=12, msg=el)
            self.assertEqual(r["razao"], razao)
            self.assertIn("13.3", r["item"])
        self.assertEqual(c.limite_deslocamento("entre_pisos", 3)["estado"], "ELS_FREQ")
        self.assertEqual(c.limite_deslocamento("aceitabilidade", 3)["estado"], "ELS_QP")
        with self.assertRaises(ValueError):
            c.limite_deslocamento("viga_piso", 5)


def _verif(b, h, linhas, papel, L=3.0, fck=25, params=None):
    return c.verificar_barra_concreto(s.secao_retangular(b, h), m.concreto(fck),
                                      [{"combinacao": "C", "linhas": linhas}], SEM_ELS, L, papel, params or {})


def _crit_item(r, trecho):
    return [v for v in r["detalhes"]["verificacoes"] if trecho in v["item"]]


class TestAlinhamentoCYPE(unittest.TestCase):
    """Verificações que o CYPE 2016 faz (verificacao/logica-cype-concreto.md, LC-001 a LC-008) com fórmula da
    NBR 6118:2014 e valores derivados à mão aqui."""

    def test_as_min_por_md_min(self):
        """17.3.5.2.1: Md,mín = 0,8·W0·fctk,sup. 20×25, d = 19,5 cm, C40, CA-50:
        W0 = 0,2·0,25²/6 = 2,0833e-3 m³; fctk,sup = 1,3·0,3·40^(2/3) = 4,5616 MPa → Md,mín = 7,603 kN·m.
        Retangular à mão (λ = 0,8, σcd = 0,85fcd): μ = Md/(σcd·b·d²), x = d/0,8·(1 − √(1 − 2μ)),
        As = Md/(fyd(d − 0,4x)) = 0,916 cm²; o código integra a parábola-retângulo (≤ 2% do retangular).
        0,15%·Ac = 0,75 cm² não governa."""
        Md = 0.8 * 0.2 * 0.25 ** 2 / 6 * 1.3 * 0.3 * 40 ** (2 / 3) * 1e3
        sc = 0.85 * 4.0 / 1.4
        mu = Md * 100 / (sc * 20 * 19.5 ** 2)
        x = 19.5 / 0.8 * (1 - math.sqrt(1 - 2 * mu))
        As_ret = Md * 100 / (50 / 1.15 * (19.5 - 0.4 * x))
        r = c.as_min_flexao(0.2, 0.25, 0.195, 40, 500)
        self.assertAlmostEqual(r["Md_min"], Md, places=9)
        self.assertAlmostEqual(r["Md_min"], 7.603, delta=1e-3)
        self.assertAlmostEqual(r["As_calc"], As_ret, delta=As_ret * TOL)
        self.assertAlmostEqual(r["As_min"], max(r["As_calc"], 0.75), places=12)
        self.assertIn("17.3.5.2.1", r["item"])
        # 20×50, d = 45,5, C25: Md,mín = 22,23 kN·m → As ≈ 1,13 < 0,15%·Ac = 1,5 cm² (taxa absoluta governa)
        self.assertAlmostEqual(c.as_min_flexao(0.2, 0.5, 0.455, 25, 500)["As_min"], 1.5, places=12)

    def test_wk_acri_por_barra(self):
        """17.3.3.2: wk da barra de canto com Acri próprio. 20×50, d = 45,5, 3φ16, M = 60 kN·m, C25, η1 = 2,25,
        c = 3 cm, φt = 5 mm → eixos das barras a 4,3 / 10 / 15,7 cm da face lateral. Canto: largura
        4,3 + mín(7,5φ = 12; 5,7/2) = 7,15 cm; altura mín(12; h − d = 4,5) + mín(12; d − x) = 16,5 cm."""
        As = 3 * math.pi * 1.6 ** 2 / 4
        a, bb = 10.0, 15 * As  # estádio II, αe = 15: b·x²/2 + αe·As·x − αe·As·d = 0
        x = (-bb + math.sqrt(bb ** 2 + 4 * a * bb * 45.5)) / (2 * a)
        I = 20 * x ** 3 / 3 + bb * (45.5 - x) ** 2
        sig = 15 * 6000 * (45.5 - x) / I * 10  # MPa
        rho = (math.pi * 1.6 ** 2 / 4) / (7.15 * 16.5)
        fctm = 0.3 * 25 ** (2 / 3)
        k = 16 / (12.5 * 2.25) * sig / 210000
        esperado = min(k * 3 * sig / fctm, k * (4 / rho + 45))
        wk, sigma, phi = c._wk(60.0, As, 0.2, 0.5, 0.455, 0.03, 25, 2.25)
        self.assertEqual(phi, 16.0)
        self.assertAlmostEqual(sigma, sig, places=6)
        self.assertAlmostEqual(wk, esperado, places=9)

    def test_gama_n(self):
        """13.2.3, Tabela 13.1: γn = 1,95 − 0,05·b (b em cm, 14 ≤ b < 19); 1,0 com b ≥ 19."""
        self.assertAlmostEqual(c.gama_n(0.14), 1.25, places=12)
        self.assertAlmostEqual(c.gama_n(0.17), 1.10, places=12)
        self.assertEqual(c.gama_n(0.19), 1.0)
        self.assertEqual(c.gama_n(0.30), 1.0)

    def test_pilar_14_com_gama_n(self):
        """Pilar 14×40, Nd = 500 kN, L = 3 m: os esforços entram multiplicados por γn = 1,25 → As igual ao do
        pilar dimensionado com Nd = 625 kN e a mesma disposição (2b = 28 cm < 31 cm entre cantos → nh ≥ 1)."""
        r = _verif(0.14, 0.40, [(0, -500.0, 0, 0, 0, 0, 0), (3, -500.0, 0, 0, 0, 0, 0)], "pilar")
        ref = c.dimensionar_pilar(0.14, 0.40, 625.0, 0.0, 0.0, 3.0, 25, dl=0.045)
        self.assertEqual(r["detalhes"]["gama_n"], 1.25)
        self.assertEqual((r["detalhes"]["nb"], r["detalhes"]["nh"]), (ref["nb"], ref["nh"]))
        self.assertGreaterEqual(ref["nh"], 1)
        self.assertAlmostEqual(r["armadura"]["As_pilar_cm2"], ref["As"], delta=1e-6 * ref["As"])
        v = _crit_item(r, "13.2.3")
        self.assertTrue(v and all(x["ok"] for x in v))

    def test_dimensoes_minimas_pilar(self):
        """13.2.3: b ≥ 14 cm e Ac ≥ 360 cm²; 14.4.2.4: h/b > 5 é pilar-parede (fora do escopo)."""
        ln = [(0, -100.0, 0, 0, 0, 0, 0), (3, -100.0, 0, 0, 0, 0, 0)]
        for b, h in ((0.13, 0.40), (0.14, 0.25), (0.15, 0.80)):
            r = _verif(b, h, ln, "pilar")
            self.assertFalse(r["ok"], (b, h))
            self.assertFalse(all(x["ok"] for x in _crit_item(r, "13.2.3") + _crit_item(r, "14.4.2.4")), (b, h))
        self.assertTrue(_verif(0.20, 0.40, ln, "pilar")["ok"])

    def test_as_max_pilar_4_por_cento(self):
        """17.3.5.3.2: 8% Ac inclui a sobreposição da emenda → com traspasse (padrão) As ≤ 4% Ac fora dela;
        emenda = "luva" libera 8%. 20×20, C25, Nd = 1100 kN, L = 2,5 m → As entre 4% (16) e 8% (32 cm²)."""
        ln = [(0, -1100.0, 0, 0, 0, 0, 0), (2.5, -1100.0, 0, 0, 0, 0, 0)]
        r = _verif(0.20, 0.20, ln, "pilar", L=2.5)
        As = r["armadura"]["As_pilar_cm2"]
        self.assertTrue(16 < As < 32, As)
        v = next(x for x in r["detalhes"]["verificacoes"] if x["criterio"] == "As_pilar_max")
        self.assertAlmostEqual(v["taxa"], As / 16, places=9)
        self.assertFalse(r["ok"])
        r2 = _verif(0.20, 0.20, ln, "pilar", L=2.5, params={"emenda": "luva"})
        self.assertTrue(r2["ok"])
        with self.assertRaises(ValueError):
            _verif(0.20, 0.20, ln, "pilar", params={"emenda": "solda?"})

    def test_espacamento_maximo_barras_pilar(self):
        """18.4.2.2: eixos ≤ mín(2b, 400 mm). 20×60, d' = 4,5 cm: face de 60 → 51 cm entre cantos > 40 →
        2 vãos → 3 barras por face (nh = 1); face de 20 → 11 cm (nb = 2). 30×120: 2b = 60 > 40 → 111/40 → 3
        vãos (nh = 2); face de 30 → 21 cm (nb = 2)."""
        self.assertEqual(c._disposicao_minima(0.20, 0.60, 0.045), (2, 1))
        self.assertEqual(c._disposicao_minima(0.30, 1.20, 0.045), (2, 2))
        self.assertEqual(c._disposicao_minima(0.60, 0.60, 0.045, 3, 0), (3, 1))
        r = _verif(0.20, 0.60, [(0, -1500.0, 0, 0, 0, 0, 0), (3, -1500.0, 0, 0, 0, 0, 0)], "pilar")
        self.assertGreaterEqual(r["detalhes"]["n_barras"], 6)
        self.assertGreaterEqual(r["detalhes"]["pilar"]["n_barras"], 6)

    def test_armadura_de_pele(self):
        """17.3.5.2.3: h > 60 cm → 0,10%·Ac,alma por face (≤ 5 cm²/m), s ≤ d/3 e 20 cm.
        20×70: 1,4 cm²/face; d = 65,5 → d/3 = 21,8 → s = 20 cm; trecho entre as barras 70 − 9 = 61 cm →
        4 vãos → 3 barras/face; 1,4/3 = 0,467 cm² → φ8. 60×100: 6 > 5·1,0 → 5 cm²/face."""
        ln = [(0, 0, 0, 50, 0, -100, 0), (5, 0, 0, -50, 0, -100, 0)]
        r = _verif(0.20, 0.70, ln, "viga", L=5.0)
        v = _crit_item(r, "17.3.5.2.3")
        self.assertEqual(len(v), 1)
        self.assertAlmostEqual(v[0]["As_face_cm2"], 1.4, places=9)
        self.assertAlmostEqual(v[0]["s_max_m"], 0.20, places=12)
        self.assertEqual((v[0]["n_face"], v[0]["phi_mm"]), (3, 8.0))
        self.assertTrue(v[0]["ok"])
        self.assertAlmostEqual(c.armadura_pele(0.60, 1.00, 0.955)["As_face_cm2"], 5.0, places=9)
        self.assertEqual(_crit_item(_verif(0.20, 0.60, ln, "viga", L=5.0), "17.3.5.2.3"), [])

    def test_espacamento_estribos(self):
        """18.3.3.2: VRd2 = 0,27·0,9·(25/1,4)·0,2·0,455 = 394,9 kN (20×50, C25).
        Vd = 300 > 0,67·VRd2 → s ≤ 0,3d = 13,65 cm (≤ 20); Vd/VRd2 = 0,76 > 0,2 → st ≤ 0,6d = 27,3 cm.
        Vd = 50 → s ≤ 0,6d = 27,3 (≤ 30); st ≤ d = 45,5 (≤ 80). φt ≤ bw/10 = 20 mm."""
        r = c.cisalhamento(0.20, 0.455, 300.0, 25)
        self.assertAlmostEqual(r["VRd2"], 0.27 * 0.9 * 2.5 / 1.4 * 20 * 45.5, places=6)
        self.assertAlmostEqual(r["s_max_m"], 0.1365, places=12)
        self.assertAlmostEqual(r["st_max_m"], 0.273, places=12)
        self.assertAlmostEqual(r["phi_t_max_mm"], 20.0, places=12)
        r = c.cisalhamento(0.20, 0.455, 50.0, 25)
        self.assertAlmostEqual(r["s_max_m"], 0.273, places=12)
        self.assertAlmostEqual(r["st_max_m"], 0.455, places=12)
        v = _crit_item(_verif(0.2, 0.5, [(0, 0, 0, 300, 0, -10, 0), (5, 0, 0, -300, 0, -10, 0)], "viga", 5.0),
                       "18.3.3.2")
        self.assertEqual(len(v), 1)
        self.assertAlmostEqual(v[0]["s_max_m"], 0.3 * 0.455, places=9)
        self.assertEqual(v[0]["n_ramos"], 2)  # 20 − 2·3 − 0,5 = 13,5 cm entre ramos ≤ 27,3

    def test_largura_minima_viga(self):
        """13.2.2: viga b ≥ 12 cm; até 10 cm em casos excepcionais (aviso); < 10 cm reprova."""
        ln = [(0, 0, 0, 5, 0, -5, 0), (3, 0, 0, -5, 0, -5, 0)]
        self.assertTrue(all(x["ok"] for x in _crit_item(_verif(0.12, 0.40, ln, "viga"), "13.2.2")))
        r = _verif(0.10, 0.40, ln, "viga", params={"cobrimento_m": 0.02})
        self.assertTrue(all(x["ok"] for x in _crit_item(r, "13.2.2")))
        self.assertTrue(any("13.2.2" in a for a in r["avisos"]))
        r = _verif(0.09, 0.40, ln, "viga", params={"cobrimento_m": 0.02})
        self.assertFalse(all(x["ok"] for x in _crit_item(r, "13.2.2")))


class TestPendenciasMedias(unittest.TestCase):
    """LC-017 (flecha ativa), LC-018 (le, 15.6), LC-019 (torção, 18.3.4) e edições conferidas na 2023."""

    XI1 = 0.68 * 0.996 ** 1 * 1 ** 0.32  # Tabela 17.1 (17.3.2.1.2): ξ(1 mês) = 0,67728
    XI6 = 0.68 * 0.996 ** 6 * 6 ** 0.32  # ξ(6 meses) = 1,17780

    def test_flecha_ativa_funcao(self):
        """f∞ = Σ fi·(1 + (2 − ξ(t0i))/(1 + 50ρ')); até ta só as cargas antes dela fluem de t0 a ta;
        ativa = f∞ − f(ta). fi,antes = 4 mm (t0 = 1), fi,pós = 2 mm (ta = 6):
        f∞ = 4·2,32272 + 2·1,82219 = 12,9353; f(ta) = 4·(1 + 1,17780 − 0,67728) = 6,0021; ativa = 6,9332."""
        r = c.flecha_ativa(4.0, 2.0, 1.0, 6.0)
        self.assertAlmostEqual(r["f_inf"], 12.9353, places=3)
        self.assertAlmostEqual(r["f_ate_t"], 6.0021, places=3)
        self.assertAlmostEqual(r["f_ativa"], 6.9332, places=3)
        self.assertAlmostEqual(r["f_ativa"], 4 * (2 - self.XI6) + 2 * (3 - self.XI6), places=12)
        k = 1 + 50 * 0.005  # ρ' = 0,5%
        r = c.flecha_ativa(4.0, 2.0, 1.0, 6.0, rho2=0.005)
        self.assertAlmostEqual(r["f_ativa"], 4 * (2 - self.XI6) / k + 2 * (1 + (2 - self.XI6) / k), places=12)
        self.assertIn("17.3.2.1.2", r["item"])
        with self.assertRaises(ValueError):
            c.flecha_ativa(4.0, 2.0, 6.0, 1.0)  # alvenaria antes da retirada do escoramento

    def test_flecha_ativa_na_verificacao(self):
        """Mesma viga de test_els_flecha_e_fissura (20×50, d = 46, C25, Ma = 80, f_el = 5 mm), alvenaria
        feita aos 6 meses e 40% da carga quase permanente aplicada depois dela: Ieq(48) para a parcela
        anterior (Ma,antes = 0,6·80), Ieq(80) para o total; fi,pós = fi,tot − fi,antes.
        Ativa ≤ min(L/500, 10 mm) = 10 mm; total a prazo infinito ≤ L/250 (aceitabilidade)."""
        sec, mat = s.secao_retangular(0.20, 0.50), m.concreto(25)
        q = 80 * 8 / 36
        elu = [{"combinacao": "ELU", "linhas": _viga_biapoiada(1.4 * q, 6.0)}]
        qp = {"combinacao": "QP", "linhas": _viga_biapoiada(q, 6.0), "flecha_elastica_m": -0.005}
        par = {"d_m": 0.46, "elemento_desloc": "alvenaria", "t_alvenaria_meses": 6.0, "fracao_qp_pos_alvenaria": 0.4}
        r = c.verificar_barra_concreto(sec, mat, elu, {"qp": qp, "freq": None}, 6.0, "viga", par)
        As = r["armadura"]["As_cm2"]
        Ecs = (0.8 + 0.2 * 25 / 80) * 5600 * 5 / 10
        Ic = 20 * 50 ** 3 / 12
        Mr = 1.5 * 0.3 * 25 ** (2 / 3) / 10 * Ic / 25
        ae = 21000 / Ecs
        x = (-ae * As + math.sqrt((ae * As) ** 2 + 2 * 20 * ae * As * 46)) / 20
        I2 = 20 * x ** 3 / 3 + ae * As * (46 - x) ** 2

        def ieq(M):
            r3 = (Mr / M) ** 3
            return r3 * Ic + (1 - r3) * I2
        fi_tot = 0.005 * Ic / ieq(8000)
        fi_a = 0.005 * 0.6 * Ic / ieq(4800)
        fi_p = fi_tot - fi_a
        ativa = fi_a * (2 - self.XI6) + fi_p * (3 - self.XI6)
        total = fi_a * (3 - self.XI1) + fi_p * (3 - self.XI6)
        v = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha")
        self.assertAlmostEqual(v["solicitante"], ativa, delta=ativa * 1e-6)
        self.assertAlmostEqual(v["resistente"], 0.010, places=12)
        t = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha_total")
        self.assertAlmostEqual(t["solicitante"], total, delta=total * 1e-6)
        self.assertAlmostEqual(t["resistente"], 6.0 / 250, places=12)
        self.assertLess(ativa, total)
        # sem t_alvenaria_meses: flecha total contra L/500 (como antes, a favor da segurança)
        par.pop("t_alvenaria_meses")
        r = c.verificar_barra_concreto(sec, mat, elu, {"qp": qp, "freq": None}, 6.0, "viga", par)
        v = next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha")
        self.assertAlmostEqual(v["solicitante"], fi_tot * (3 - self.XI1), delta=1e-9)
        json.dumps(r)

    def test_comprimento_equivalente(self):
        """15.6: le = menor entre l0 + h e l. l = 3,0; l0 = 2,4: h = 0,40 → 2,80; h = 0,20 → 2,60;
        l0 = 2,8, h = 0,4 → 3,2 > l → 3,0."""
        self.assertAlmostEqual(c.comprimento_equivalente(3.0, 2.4, 0.40)["le"], 2.80, places=12)
        self.assertAlmostEqual(c.comprimento_equivalente(3.0, 2.8, 0.40)["le"], 3.00, places=12)
        self.assertAlmostEqual(c.comprimento_equivalente(3.0)["le"], 3.00, places=12)  # sem l0: le = l
        with self.assertRaises(ValueError):
            c.comprimento_equivalente(3.0, -0.1, 0.4)
        ln = [(0, -600.0, 0, 0, 0, -20, 0), (3, -600.0, 0, 0, 0, 20, 0)]
        r = _verif(0.20, 0.40, ln, "pilar", params={"l0_x": 2.4, "l0_y": 2.4})
        self.assertAlmostEqual(r["detalhes"]["le_x_m"], 2.80, places=12)  # plano de h = 0,40
        self.assertAlmostEqual(r["detalhes"]["le_y_m"], 2.60, places=12)  # plano de b = 0,20
        r = _verif(0.20, 0.40, ln, "pilar", params={"l0_x": 2.4, "le_x": 3.5})
        self.assertAlmostEqual(r["detalhes"]["le_x_m"], 3.5, places=12)  # le informado prevalece
        self.assertAlmostEqual(r["detalhes"]["le_y_m"], 3.0, places=12)  # sem l0: le = L

    def test_torcao_disposicoes(self):
        """18.3.4: barras longitudinais de torção a no máximo 350 mm ao longo de ue, uma em cada canto
        (17.5.1.6). 35×50, he = 10: ue = 2·(25 + 40) = 130 cm → ⌈130/35⌉ = 4. 40×120, he = A/u = 15:
        ue = 2·(25 + 105) = 260 → ⌈7,43⌉ = 8."""
        r = c.torcao(0.35, 0.50, 68.08, 25, c1=0.04125, he=0.10)
        self.assertEqual((r["n_Asl_min"], r["sl_max_m"]), (4, 0.35))
        r = c.torcao(0.40, 1.20, 50, 25, c1=0.05)
        self.assertAlmostEqual(r["ue_m"], 2.60, places=12)
        self.assertEqual(r["n_Asl_min"], 8)
        self.assertIn("18.3.4", r["item"])
        ln = [(0, 0, 0, 50, 10, -40, 0), (5, 0, 0, -50, 10, -40, 0)]
        v = _crit_item(_verif(0.20, 0.50, ln, "viga", L=5.0), "18.3.4")
        self.assertEqual(len(v), 1)
        self.assertGreaterEqual(v[0]["n_Asl_min"], 4)

    def test_itens_conferidos_na_2023(self):
        """Bastos/UNESP (edições que seguem a NBR 6118:2023): 17.3.5.2.1 e 17.3.5.2.3 (Flexão, out/2023,
        p. 8–9), 18.3.3.2 (Cortante, p. 34–35), 13.2.3 (Pilares, jun/2024, p. 74), 15.6 (Pilares, p. 42),
        18.3.4 (Torção, mai/2024, p. 27–28). 13.2.2 sem fonte pública da 2023 → fica na 2014."""
        ed = {}
        for x in c.COBERTURA:
            for it in x["item"].replace(",", " ").split():
                ed.setdefault(it, x["edicao"])
        for it in ("17.3.5.2.1", "17.3.5.2.3", "18.3.3.2", "13.2.3", "15.6", "18.3.4"):
            self.assertEqual(ed.get(it), "2023", it)
        self.assertEqual(ed.get("13.2.2"), "2014")


class TestPilarMuitasCombinacoes(unittest.TestCase):
    def test_termina_rapido_e_igual_sem_atalho(self):
        """Revisor: P-Δ + sismo deixava a suíte >6 min em _esforcos_secao (pilar dimensionado do zero em cada
        uma das ~124 combinações, ~20 integrações cada). _pilar agora pula as combinações já cobertas pela maior
        As (_pilar_atende, 2 integrações). 120 combinações de flexão oblíqua: < 5 s e o mesmo resultado de
        dimensionar todas."""
        import time
        elu = [{"combinacao": f"C{k}", "linhas": [(0.0, -800.0 - 10.0 * k, 0, 0, 0, 40.0 + (k * 37) % 80, (k * 13) % 40),
                                                  (3.0, -800.0 - 10.0 * k, 0, 0, 0, -5.0, 3.0)]} for k in range(120)]
        args = (s.secao_retangular(0.25, 0.50), m.concreto(30), elu, SEM_ELS, 3.0, "pilar")
        t = time.perf_counter()
        r = c.verificar_barra_concreto(*args)
        self.assertLess(time.perf_counter() - t, 5.0)
        orig = c._pilar_atende
        c._pilar_atende = lambda *a, **k: False  # sem atalho: dimensiona todas
        try:
            r2 = c.verificar_barra_concreto(*args)
        finally:
            c._pilar_atende = orig
        self.assertAlmostEqual(r["armadura"]["As_pilar_cm2"], r2["armadura"]["As_pilar_cm2"], places=9)
        self.assertEqual((r["armadura"]["barras"], r["ok"], r["criterio"], r["combinacao"]),
                         (r2["armadura"]["barras"], r2["ok"], r2["criterio"], r2["combinacao"]))
        self.assertAlmostEqual(r["taxa_max"], r2["taxa_max"], places=12)


if __name__ == "__main__":
    unittest.main()
