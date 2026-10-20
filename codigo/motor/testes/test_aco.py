"""Testes de aco/nbr8800.py (os de PFF/NBR 14762 estão em test_pff.py).

TestExemplosPublicados: exemplos resolvidos de fontes públicas (página citada em cada teste),
com as propriedades da própria fonte. Demais classes: cálculo manual pelas fórmulas da
NBR 8800:2008 (aritmética no comentário de cada teste), com W 200 x 26,6 da tabela Gerdau
(folder Segmento Industrial 10/18: A=34,2 cm², Ix=2611 cm⁴, Iy=330 cm⁴, ry=3,10 cm,
Wx=252,3 cm³; Zx≈282 cm³, J e Cw das dimensões) e aço ASTM A36.
FLM e FLA: nenhum exemplo resolvido público confiável foi encontrado (2026-10-20); ficam por
cálculo manual da tabela G.1.
"""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes as s, materiais as m  # noqa: E402
from prossen_estrutural.aco import nbr8800 as n  # noqa: E402
from prossen_estrutural.modelo import Modelo, No, Barra, CasoCarga, CargaNodal  # noqa: E402
from prossen_estrutural.solver import analisar  # noqa: E402


def rel(a, b):
    return abs(a - b) / abs(b)


class TestNBR8800(unittest.TestCase):
    def setUp(self):
        self.w = s.catalogo("W200x26.6")
        self.a36 = m.aco("A-36")

    def test_tracao(self):
        # 5.2.2: Nt,Rd = A·fy/1,10 = 34,2·25/1,10 = 777,3 kN; ruptura com An=0,85A, Ct=1: 0,85·34,2·40/1,35 = 861,3
        r = n.tracao(self.w, self.a36, An=0.85 * self.w.A)
        self.assertLess(rel(r["NtRd"], 777.3), 0.01)
        self.assertLess(rel(r["Nt_ruptura"], 861.3), 0.01)

    def test_compressao_W200(self):
        # KLx=KLy=KLz=3 m. Q=1 (bf/2tf=7,9 < 15,8; h/tw=29,3 < 42,1).
        # Ney = π²·E·Iy/L² = π²·20000·330/300² = 723,8 kN (governa: Nez ≈ 1420 kN); λ0 = √(34,2·25/723,8) = 1,087
        # χ = 0,658^(λ0²) = 0,610; Nc,Rd = 0,610·855/1,10 = 474,1 kN
        r = n.compressao(self.w, self.a36, 3, 3, 3)
        self.assertEqual(r["Q"], 1.0)
        self.assertLess(rel(r["chi"], 0.610), 0.01)
        self.assertLess(rel(r["NcRd"], 474.1), 0.01)

    def test_flexao_FLT_W200(self):
        # Lb=3 m, Cb=1: λ=96,8; λp=1,76√(E/fy)=49,8; λr=162,6 -> regime inelástico
        # Mpl=Zx·fy=70,58 kN·m, Mr=0,7·fy·Wx=44,15 kN·m; Mn=Mpl-(Mpl-Mr)(λ-λp)/(λr-λp)=59,56; MRd=54,15 kN·m
        r = n.flexao_x(self.w, self.a36, 3.0, 1.0)
        self.assertLess(rel(r["MRd"], 54.15), 0.01)
        self.assertLess(rel(r["lambda_FLT"][2], 162.6), 0.01)
        # Lb=1 m < λp·ry: plastificação, MRd = Zx·fy/1,10 = 64,16 kN·m
        self.assertLess(rel(n.flexao_x(self.w, self.a36, 1.0)["MRd"], 64.16), 0.01)

    def test_Cb_carga_uniforme(self):
        # Viga biapoiada com carga uniforme: Cb = 12,5/(2,5+3·0,75+4+3·0,75) = 1,136 (Pfeil, cap. 6)
        self.assertAlmostEqual(n.Cb(1.0, 0.75, 1.0, 0.75), 1.136, places=3)

    def test_cortante_W200(self):
        # 5.4.3.1: h/tw=29,3 < λp=69,6 -> Vpl=0,6·d·tw·fy=0,6·20,7·0,58·25=180,1 kN; VRd=163,7 kN
        self.assertLess(rel(n.cortante_y(self.w, self.a36)["VRd"], 163.7), 0.01)

    def test_interacao(self):
        # 5.5.1.2 com N=-200 kN, Mx=30 kN·m: 200/474,1 + 8/9·30/54,15 = 0,914
        r = n.verificar_barra_aco(self.w, self.a36, dict(N=-200, Mx=30, Vy=50), 3, 3, 3)
        self.assertEqual(r["criterio"], "interacao")
        self.assertLess(rel(r["taxa_max"], 0.914), 0.01)
        # N/NRd < 0,2: N/(2NRd) + M/MRd
        self.assertAlmostEqual(n.interacao(-50, 500, 20, 100, 0, 1), 0.05 + 0.2)

    def test_Q_mesa_esbelta_soldado(self):
        # Perfil soldado h=500, b=300, tw=6,3, tf=8, A-36: bf/2tf=18,75; kc=4/√(484/6,3)=0,456
        # limites grupo 5: 0,64√(E·kc/fy)=12,2 ; 1,17√(...)=22,4 -> Qs = 1,415-0,65·18,75/√(E·kc/fy) = 0,773
        I = s.secao_I(0.5, 0.3, 0.0063, 0.008)
        Q, Qs, Qa = n.fator_Q(I, self.a36)
        self.assertLess(rel(Qs, 0.773), 0.01)
        self.assertLess(Qa, 1.0)  # alma h/tw = 76,8 > 42,1


def secao_fonte(nome, A, Ix, Iy, J, Cw, Wx, Zx, **extra):
    """Seção I com as propriedades impressas na fonte (unidades m); Wy/Zy não usados."""
    return s._monta(nome, "I", A, Ix, Iy, J, Wx, Wx / 3, Zx, Zx / 3, Cw, **extra)


class TestExemplosPublicados(unittest.TestCase):
    def setUp(self):
        self.a572 = m.aco("A-572 GR.50")
        self.mr250 = m.aco("MR-250")

    def cbca_w310(self, Cw):
        # [CBCA-G] Pravia, Drehmer & Mesacasa Jr., "Galpões para usos gerais", Manual de Construção
        # em Aço, IABr/CBCA, 4ª ed., 2010, pp. 35–42: W 310 x 38,7, A572 Gr50 (fy=345 MPa), d=310,
        # bf=165, tf=9,7, tw=5,8, h=271 mm; A=4970 mm², Ix=8,581e7, Iy=7,27e6 mm⁴, J=132000 mm⁴,
        # W=553600, Zx=615400 mm³. Cw: 1,63728e11 mm⁶ (Nez) e 1,639023e11 mm⁶ (FLT), como na fonte.
        return secao_fonte("W310x38.7", 4970e-6, 8.581e-5, 7.27e-6, 1.32e-7, Cw * 1e-18, 553.6e-6, 615.4e-6,
                           h=0.310, b=0.165, tw=0.0058, tf=0.0097, soldado=False,
                           bt_mesa=165 / 19.4, ht_alma=271 / 5.8)

    def test_cbca_FLT_elastica_Cb(self):
        # [CBCA-G] pp. 37–39, pilar D-E: Lb = 6 m, M linear 82,01 → 0 (MA=61,51, MB=41,00, MC=20,50):
        # Cb = 1,667; λ=157,07; λp=42,38; λr=119,27; Mcr=145,64 kN·m; MRd = 132,40 kN·m;
        # FLM λ/λp/λr = 8,50/9,15/23,88; FLA 46,72/90,53/137,24
        cb = n.Cb(82.01, 61.51, 41.00, 20.50)
        self.assertLess(rel(cb, 1.667), 0.01)
        r = n.flexao_x(self.cbca_w310(1.639023e11), self.a572, 6.0, cb)
        for obtido, esperado in zip(r["lambda_FLT"], (157.07, 42.38, 119.27)):
            self.assertLess(rel(obtido, esperado), 0.01)
        for obtido, esperado in zip(r["lambda_FLM"] + r["lambda_FLA"], (8.50, 9.15, 23.88, 46.72, 90.53, 137.24)):
            self.assertLess(rel(obtido, esperado), 0.01)
        self.assertLess(rel(r["Mcr_FLT"], 145.64), 0.01)
        self.assertLess(rel(r["MRd"], 132.40), 0.01)

    def test_cbca_FLT_inelastica_Cb(self):
        # [CBCA-G] p. 41, viga C-D: Lb = 2538 mm, Mmáx=82,01, MA=55,76, MB=29,93, MC=15,99 → Cb = 1,90;
        # λ=66,44; Mr=133,69 kN·m; Cb·Mn > Mpl → MRd = Mpl/1,10 = 193,00 kN·m
        cb = n.Cb(82.01, 55.76, 29.93, 15.99)
        self.assertLess(rel(cb, 1.90), 0.01)
        r = n.flexao_x(self.cbca_w310(1.639023e11), self.a572, 2.538, cb)
        self.assertLess(rel(r["lambda_FLT"][0], 66.44), 0.01)
        self.assertLess(rel(r["MRd"], 193.00), 0.01)

    def test_cbca_compressao_Q(self):
        # [CBCA-G] p. 38: KL = 6 m nos três eixos; alma: bef=221,15 mm, Aef=4680,85 mm² → Q = Qa = 0,94;
        # Nex=4705,06, Ney=398,62, Nez=1022,23 kN (r0=136,84 mm); λ0=2,011; χ=0,2169; Nc,Rd=317,8 kN
        r = n.compressao(self.cbca_w310(1.63728e11), self.a572, 6.0, 6.0, 6.0)
        for k, esperado in (("Q", 0.94), ("Nex", 4705.06), ("Ney", 398.62), ("Nez", 1022.23),
                            ("lambda0", 2.011), ("chi", 0.2169), ("NcRd", 317.8)):
            self.assertLess(rel(r[k], esperado), 0.01, k)
        # p. 41 (viga C-D): Lx=7615,7, Ly=Lz=2538 mm: Nex=2920,44; Ney=2227,82; Nez=3222,23;
        # λ0=0,8506; χ=0,7387; Nc,Rd=1082,38 kN
        r = n.compressao(self.cbca_w310(1.63728e11), self.a572, 7.6157, 2.538, 2.538)
        for k, esperado in (("Nex", 2920.44), ("Ney", 2227.82), ("Nez", 3222.23), ("lambda0", 0.8506),
                            ("chi", 0.7387), ("NcRd", 1082.38)):
            self.assertLess(rel(r[k], esperado), 0.01, k)

    def test_cbca_interacao_ramo_menor_0_2(self):
        # [CBCA-G] p. 39: NSd=42,97, Nc,Rd=317,8 (0,135 < 0,2); MSd=82,01, MRd=132,40 → 0,69 (5.5.1.2 b)
        self.assertLess(rel(n.interacao(-42.97, 317.8, 82.01, 132.40, 0, 1), 0.69), 0.01)

    def test_ufpr_compressao_torcao(self):
        # [UFPR] M. A. Argenta, "Estruturas Metálicas, cap. 5 Compressão", UFPR, 2017, ex. 5.1.8 pp. 38–39
        # (web.archive.org/web/20191201100817/http://www.estruturas.ufpr.br/wp-content/uploads/2017/03/Capitulo5-Compressao.pdf):
        # CS 250x52, MR250; Ag=66,0 cm², Ix=7694, Iy=2475 cm⁴, J=18 cm⁴, Cw=38656 cm⁶; KxLx=840,
        # KyLy=320, KzLz=400 cm; Q=1 → Nex=2152,96; Ney=4772,19; Nez=1209,01 kN (torção governa);
        # λ0=1,17; χ=0,56; Nc,Rd=840 kN (χ arredondado na fonte: 0,564 dá 847, a 0,8 %)
        sec = secao_fonte("CS250x52", 66.0e-4, 7694e-8, 2475e-8, 18e-8, 38656e-12, 2 * 7694e-8 / 0.25, 1.0e-3,
                          h=0.25, b=0.25, tw=0.008, tf=0.0095, soldado=True, bt_mesa=250 / 19, ht_alma=231 / 8)
        r = n.compressao(sec, self.mr250, 8.40, 3.20, 4.00)
        for k, esperado in (("Q", 1.0), ("Nex", 2152.96), ("Ney", 4772.19), ("Nez", 1209.01), ("Ne", 1209.01),
                            ("lambda0", 1.17), ("chi", 0.56), ("NcRd", 840.0)):
            self.assertLess(rel(r[k], esperado), 0.01, k)

    def test_ufmg_flexo_torcao_2L(self):
        # [UFMG-SA] S. R. Antunes (orient. R. H. Fakury), "Dimensionamento de estruturas de aço de um
        # edifício comercial segundo a NBR 8800/2008", UFMG, ex. 6 pp. 24–26
        # (repositorio.ufmg.br/bitstream/1843/BUOS-9CTJ8G/1/trabalho_final.pdf):
        # 2L 63,5x8, A36, L=319,2 cm; Ag=18,96 cm², Ix=70, Iy=165,77 cm⁴ (valor usado em Ney), J=3,97 cm⁴,
        # Cw=0, x0=0, y0=1,483 cm → Nex=135,6; Ney=321,12; Nez=2117; Neyz=312,89 kN (anexo E, flexo-torção);
        # Q=1; λ0=1,87; χ=0,251; Nc,Rd=108,15 kN
        sec = s._monta("2L63,5x8", "2L", 18.96e-4, 70e-8, 165.77e-8, 3.97e-8, 1e-6, 1e-6, 1e-6, 1e-6, 0.0,
                       0.0, 1.483e-2)
        sec.props["r0"] = 0.038  # r0 = 3,8 cm impresso na fonte (ela usa rx=1,93, ry=2,92 arredondados)
        a36 = m.aco("A-36")
        r = n.compressao(sec, a36, 3.192, 3.192, 3.192)
        for k, esperado in (("Nex", 135.6), ("Ney", 321.12), ("Nez", 2117), ("Ne", 135.6), ("lambda0", 1.87),
                            ("chi", 0.251), ("NcRd", 108.15)):
            self.assertLess(rel(r[k], esperado), 0.01, k)
        # Neyz independe de Nex: com KLx curto, a raiz de flexo-torção passa a governar
        self.assertLess(rel(n.forca_flambagem(sec, a36, 0.5, 3.192, 3.192)["Ne"], 312.89), 0.01)

    def test_ufpr_cortante_inelastico(self):
        # [UFPR] Argenta, "cap. 7 Corte", ex. 7.1.3 pp. 63–64
        # (web.archive.org/web/20240714095250/https://estruturas.ufpr.br/wp-content/uploads/2017/05/Capitulo7-Corte.pdf):
        # VS 550x64, MR250: h/tw = 531/6,3 = 84,286; kv=5; λp=69,570; λr=86,646; Aw=d·tw=34,65 cm²;
        # Vpl=519,75 kN; VRd = (λp/λ)·Vpl/1,10 = 390,00 kN
        r = n.cortante_y(s.secao_I(0.55, 0.25, 0.0063, 0.0095), self.mr250)
        for obtido, esperado in zip(r["lambda_"], (84.286, 69.570, 86.646)):
            self.assertLess(rel(obtido, esperado), 0.01)
        self.assertLess(rel(r["Vpl"], 519.75), 0.01)
        self.assertLess(rel(r["VRd"], 390.00), 0.01)

    def test_cbca_B2(self):
        # [CBCA-E] I. H. Bellei & H. N. Bellei, "Edifícios de pequeno porte estruturados em aço",
        # IABr/CBCA, 4ª ed., 2011, pp. 40–41 (pórtico, h=300 cm, Rs=0,85):
        # 1º andar Δh=0,350 cm, ΣN=1353,6, ΣH=22,5 kN → B2 = 1,09
        self.assertLess(rel(n.coef_B2(1353.6, 0.0035, 22.5, 3.0), 1.09), 0.01)
        # [UFSJ] Medeiros & Resende, TCC UFSJ 2023, tab. 12: Δh=0,72 cm, h=500 cm, ΣN=30,82, ΣH=4,789 → 1,011
        self.assertLess(rel(n.coef_B2(30.82, 0.0072, 4.789, 5.0), 1.011), 0.01)


class TestFLMeFLA(unittest.TestCase):
    """Sem exemplo público confiável: cálculo manual pela tabela G.1 (NBR 8800:2008), A36, E=200 GPa."""

    def test_FLM_soldado_inelastica(self):
        # I soldado 500x300x6,3x8: Ix=3,50027e8 mm⁴, W=1,40011e6 mm³, Z=1,54975e6 mm³; kc=4/√(484/6,3)=0,4564;
        # λ=300/16=18,75; λp=0,38√(E/fy)=10,748; λr=0,95√(E·kc/(0,7fy))=21,696 → inelástica;
        # Mpl=387,44; Mr=0,7·fy·W=245,02; Mn=387,44−142,42·(8,002/10,948)=283,34 kN·m; MRd=257,58 kN·m
        r = n.flexao_x(s.secao_I(0.5, 0.3, 0.0063, 0.008), m.aco("A-36"), 0.01)
        self.assertLess(rel(r["lambda_FLM"][2], 21.696), 0.01)
        self.assertLess(rel(r["Mn_FLM"], 283.34), 0.01)
        self.assertLess(rel(r["MRd"], 257.58), 0.01)

    def test_FLA_soldado_inelastica(self):
        # I soldado 600x300x5x16: W=2,98365e6, Z=3,20648e6 mm³; λ=568/5=113,6; λp=3,76√(E/fy)=106,35;
        # λr=5,70√(E/fy)=161,22 → inelástica; Mpl=801,62; Mr=fy·W=745,91; Mn=794,26 kN·m; MRd=722,05 kN·m
        # (mesa: bf/2tf=9,375 < λp=10,75; FLT com Lb=1 cm: plastificação)
        r = n.flexao_x(s.secao_I(0.6, 0.3, 0.005, 0.016), m.aco("A-36"), 0.01)
        self.assertLess(rel(r["Mn_FLA"], 794.26), 0.01)
        self.assertLess(rel(r["MRd"], 722.05), 0.01)


class TestFronteiras(unittest.TestCase):
    """Continuidade nas mudanças de ramo (±0,5 %)."""

    def setUp(self):
        self.w = s.catalogo("W200x26.6")
        self.a36 = m.aco("A-36")

    def test_lambda0_1_5(self):
        # 5.3.3: 0,658^(1,5²) = 0,3895 e 0,877/1,5² = 0,3898
        w, a = self.w, self.a36
        KL = 1.5 * math.pi / math.sqrt(w.A * a.fy / (a.E * w.props["Iy"]))  # λ0 = 1,5 com Ney
        r1 = n.compressao(w, a, 0.5, KL * (1 - 1e-7), 0.5)
        r2 = n.compressao(w, a, 0.5, KL * (1 + 1e-7), 0.5)
        self.assertAlmostEqual(r1["lambda0"], 1.5, places=5)
        self.assertLess(rel(r1["chi"], r2["chi"]), 0.005)

    def test_FLT_lambda_p_e_r(self):
        w, a = self.w, self.a36
        ry = w.props["ry"]
        _, lp, lr = n.flexao_x(w, a, 3.0)["lambda_FLT"]
        for lam in (lp, lr):
            m1 = n.flexao_x(w, a, lam * ry * (1 - 1e-7))["MRd"]
            m2 = n.flexao_x(w, a, lam * ry * (1 + 1e-7))["MRd"]
            self.assertLess(rel(m1, m2), 0.005, lam)
        # em λr o momento elástico Mcr iguala Mr = 0,7·fy·Wx (tabela G.1)
        r = n.flexao_x(w, a, lr * ry)
        self.assertLess(rel(r["Mcr_FLT"], 0.7 * a.fy * w.props["Wx"]), 0.005)

    def test_FLM_lambda_r(self):
        # perfil I laminado com mesa ajustada para bf/2tf = λr (FLT e FLA compactas)
        a = self.a36
        lr = 0.83 * math.sqrt(a.E / (0.7 * a.fy))
        mr = [n.flexao_x(s.secao_I(0.4, 2 * 0.01 * lr * f, 0.01, 0.01, soldado=False), a, 0.01)["MRd"]
              for f in (1 - 1e-7, 1 + 1e-7)]
        self.assertLess(rel(*mr), 0.005)

    def test_cortante_lambda_r(self):
        # 5.4.3.1: em λr, (λp/λ)·Vpl e 1,24(λp/λ)²·Vpl diferem 0,44 %
        a = self.a36
        lr = 1.37 * math.sqrt(5 * a.E / a.fy)
        v = [n.cortante_y(s.secao_I(0.8, 0.3, 0.78 / (lr * f), 0.01), a)["VRd"] for f in (1 - 1e-7, 1 + 1e-7)]
        self.assertLess(rel(*v), 0.005)


class TestCb(unittest.TestCase):
    """5.4.2.3: Cb = 12,5Mmáx/(2,5Mmáx + 3MA + 4MB + 3MC), Rm = 1."""

    xs = [6.0 * k / 12 for k in range(13)]

    def test_uniforme_biapoiada(self):
        M = [10 * x * (6 - x) / 2 for x in self.xs]  # qx(L−x)/2
        self.assertAlmostEqual(n.cb_de_diagrama(self.xs, M), 1.136, places=3)

    def test_constante(self):
        self.assertAlmostEqual(n.cb_de_diagrama(self.xs, [-50.0] * 13), 1.0)

    def test_linear(self):
        # 0 a M: 12,5/(2,5 + 0,75 + 2 + 2,25) = 1,667
        self.assertAlmostEqual(n.cb_de_diagrama(self.xs, [7 * x for x in self.xs]), 12.5 / 7.5)

    def test_trecho(self):
        # trecho [0, 3] de uma viga com momento linear 0 → M em [0, 6]: também 0 → M(3) no trecho
        self.assertAlmostEqual(n.cb_de_diagrama(self.xs, [7 * x for x in self.xs], 0.0, 3.0), 12.5 / 7.5)
        with self.assertRaises(ValueError):
            n.cb_de_diagrama(self.xs, [1.0] * 13, 2.0, 7.0)


class TestEsforcosNbr(unittest.TestCase):
    def test_viga_duas_direcoes(self):
        # balanço de 2 m ao longo de X global (y local = Z×x = Y, z local = Z):
        # gravidade P = 10 kN (−Z) → Mx(NBR) = P·L = 20 kN·m, Vy(NBR) = 10 kN (paralela à alma);
        # lateral H = 3 kN (+Y)   → My(NBR) = H·L = 6 kN·m,  Vx(NBR) = 3 kN
        mod = Modelo()
        mod.add(m.aco("A-36"))
        mod.add(s.catalogo("W200x26.6"))
        mod.add(CasoCarga("G", "G"))
        mod.add(No("a", 0, 0, 0, apoio=(True,) * 6))
        mod.add(No("b", 2, 0, 0))
        mod.add(Barra("v", "a", "b", "W200x26.6", "A-36"))
        mod.add(CargaNodal("G", "b", (0, 3, -10, 0, 0, 0)))
        e = n.esforcos_nbr(analisar(mod, "G").esforcos["v"][0])  # engaste
        self.assertAlmostEqual(abs(e["Mx"]), 20.0, places=6)
        self.assertAlmostEqual(abs(e["My"]), 6.0, places=6)
        self.assertAlmostEqual(abs(e["Vy"]), 10.0, places=6)
        self.assertAlmostEqual(abs(e["Vx"]), 3.0, places=6)
        self.assertAlmostEqual(e["N"], 0.0, places=9)
        self.assertEqual(set(e), {"N", "Mx", "My", "Vy", "Vx", "T"})

    def test_mapeamento(self):
        self.assertEqual(n.esforcos_nbr((0.5, 1, 2, 3, 4, 5, 6)), dict(N=1, Mx=5, My=6, Vy=3, Vx=2, T=4))


class TestVerificarLista(unittest.TestCase):
    def setUp(self):
        self.w = s.catalogo("W200x26.6")
        self.a36 = m.aco("A-36")

    def test_lista_igual_pior_ponto(self):
        pts = [dict(N=-50, Mx=5 * k, My=0.5, Vy=20 - 4 * k, Vx=1) for k in range(7)]
        pts[2]["N"] = 80  # um ponto tracionado
        unit = [n.verificar_barra_aco(self.w, self.a36, p, 3, 3, 3, Cb=1.1) for p in pts]
        r = n.verificar_barra_aco(self.w, self.a36, pts, 3, 3, 3, Cb=1.1)
        k = max(range(7), key=lambda i: unit[i]["taxa_max"])
        self.assertEqual(r["indice"], k)
        self.assertAlmostEqual(r["taxa_max"], unit[k]["taxa_max"], places=12)
        self.assertEqual(r["criterio"], unit[k]["criterio"])
        json.dumps(r, allow_nan=False)  # só tipos JSON
        self.assertEqual(set(r), {"taxa_max", "criterio", "item", "ok", "indice", "detalhes", "avisos"})

    def test_dict_continua(self):
        r = n.verificar_barra_aco(self.w, self.a36, dict(Mx=10), 3, 3, 3)
        self.assertEqual(r["indice"], 0)
        self.assertEqual(r["avisos"], [])
        self.assertNotIn("compressao", r["detalhes"])  # sem N: tração/compressão não calculadas
        self.assertNotIn("tracao", r["detalhes"])

    def test_avisos(self):
        # alma esbelta: h/tw = 484/2,5 = 194 > λr = 5,70√(E/fy) = 161 (anexo H não implementado)
        I = s.secao_I(0.5, 0.25, 0.0025, 0.008)
        r = n.verificar_barra_aco(I, self.a36, dict(Mx=50), 3, 3, 3)
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "nao_verificado")
        self.assertTrue(any("anexo H" in a for a in r["avisos"]))
        # perfil sem dados (seção explícita sem props)
        x = s.secao_de_spec(dict(nome="X", A=1e-3, Iy=1e-6, Iz=1e-7, J=1e-8))
        r = n.verificar_barra_aco(x, self.a36, dict(N=-10), 3, 3, 3)
        self.assertFalse(r["ok"])
        self.assertTrue(any("sem dados" in a for a in r["avisos"]))
        # esbeltez KL/r = 8/0,031 = 258 > 200 (5.3.4): taxa 258/200 em detalhes; reprova sem definir
        # taxa_max (ESPEC §7.2: só critérios de esforço definem taxa_max)
        r = n.verificar_barra_aco(self.w, self.a36, dict(N=-1), 8, 8, 3)
        self.assertFalse(r["ok"])
        self.assertNotEqual(r["criterio"], "esbeltez")
        self.assertLess(rel(r["detalhes"]["taxas"]["esbeltez"], 8 / self.w.props["ry"] / 200), 1e-9)
        self.assertTrue(any("5.3.4" in a for a in r["avisos"]))

    def test_esbeltez_tracao_sem_K(self):
        # NBR 8800 5.2.8.1: L/r ≤ 300 com L = comprimento destravado, SEM K (o K é da compressão, 5.3.4).
        # W 200x26,6: ry = 3,10 cm. Barra L = 4 m, K = 2 (Lx = Ly = 8 m).
        ry = self.w.props["ry"]
        t = lambda r: r["detalhes"]["esbeltez"]  # noqa: E731
        # tração pura: L/ry = 4/0,031 = 129,0 → 129,0/300 = 0,430 (antes: KL/ry/300 = 258/300 = 0,860)
        r = n.verificar_barra_aco(self.w, self.a36, dict(N=10), 8, 8, 4, L=4)
        self.assertLess(rel(t(r)["valor"], 4 / ry), 1e-9)
        self.assertEqual((t(r)["limite"], t(r)["item"]), (300, "5.2.8.1"))
        self.assertLess(rel(r["detalhes"]["taxas"]["esbeltez"], 4 / ry / 300), 1e-9)
        # Lt (trecho entre travamentos) prevalece sobre L: (x 4 m, y 2 m) → max(4/rx, 2/ry) = 2/0,031 = 64,5
        r = n.verificar_barra_aco(self.w, self.a36, dict(N=10), 8, 8, 4, L=4, Lt=(4, 2))
        self.assertLess(rel(t(r)["valor"], max(4 / self.w.props["rx"], 2 / ry)), 1e-9)
        # tração e compressão na mesma combinação: governa KL/r/200 = 258/200 = 1,290 (> 129/300)
        r = n.verificar_barra_aco(self.w, self.a36, [dict(N=10), dict(N=-1)], 8, 8, 4, L=4)
        self.assertEqual(t(r)["limite"], 200)
        self.assertLess(rel(r["detalhes"]["taxas"]["esbeltez"], 8 / ry / 200), 1e-9)
        self.assertFalse(r["ok"])


class TestServico(unittest.TestCase):
    def test_limite_deslocamento(self):
        # NBR 8800:2008 tabela C.1 (notas de aula UFAL/eduCAPES; Portal do Projetista)
        # VC-aco-006: alvenaria solidarizada = linha "vigas de piso" (L/350) + nota h (≤ 15 mm)
        casos = {"viga_cobertura": (6, 6 / 250), "viga_piso": (7, 7 / 350), "viga_alvenaria": (4, 4 / 350),
                 "viga_pilar": (6, 6 / 500), "terca": (6, 6 / 180), "balanco_cobertura": (2, 4 / 250),
                 "balanco": (2, 4 / 350), "topo_pilar": (12, 12 / 400), "entre_pisos": (3, 3 / 500),
                 "galpao_topo": (8, 8 / 300)}
        for el, (L, esp) in casos.items():
            r = n.limite_deslocamento(el, L)
            self.assertAlmostEqual(r["limite_m"], esp, msg=el)
            self.assertIn(r["estado"], ("ELS_FREQ", "ELS_RARA", "ELS_QP"))
            self.assertIn("C.1", r["item"])
        self.assertEqual(n.limite_deslocamento("viga_piso", 7)["razao"], "L/350")
        self.assertAlmostEqual(n.limite_deslocamento("viga_alvenaria", 10)["limite_m"], 0.015)  # 15 mm < L/350
        with self.assertRaises(ValueError):
            n.limite_deslocamento("laje", 5)

    def test_B2(self):
        # Anexo D: ΣN = 5000 kN, Δh = 10 mm, ΣH = 200 kN, h = 3 m, Rs = 0,85:
        # Δh·ΣN/(Rs·h·ΣH) = 50/510 = 0,09804 → B2 = 1/0,90196 = 1,1087 (média deslocabilidade)
        B2 = n.coef_B2(5000, 0.01, 200, 3.0)
        self.assertAlmostEqual(B2, 1.10870, places=4)
        self.assertEqual(n.deslocabilidade(B2), "media")
        self.assertAlmostEqual(n.coef_B2(5000, 0.01, 200, 3.0, Rs=1.0), 1 / (1 - 50 / 600))
        self.assertEqual([n.deslocabilidade(b) for b in (1.0, 1.1, 1.4, 1.41)], ["pequena", "pequena", "media", "grande"])
        with self.assertRaises(ValueError):
            n.coef_B2(5000, 0.01, 0, 3.0)

    def test_cobertura(self):
        self.assertTrue(n.EDICAO.startswith("NBR 8800:"))
        for c in n.COBERTURA:
            self.assertEqual(set(c), {"norma", "edicao", "item", "verificacao", "limitacao"})


class TestCorrecoes018(unittest.TestCase):
    """Ficha 018 (achados VC-aco-*): valores esperados derivados à mão nos comentários."""

    def setUp(self):
        self.a36 = m.aco("A-36")
        self.L = s.secao_cantoneira(0.100, 0.100, 0.010)

    def test_cantoneira_eixos_principais(self):
        # abas iguais: u (Imax) é o eixo de simetria a 45°; centro de cisalhamento no canto, sobre u (v0 = 0)
        p = self.L.props
        self.assertAlmostEqual(p["alfa_principal"], math.pi / 4)
        self.assertAlmostEqual(p["v0"], 0.0)
        self.assertAlmostEqual(abs(p["u0"]), math.hypot(p["x0"], p["y0"]))

    def test_E14_uma_aba(self):
        # E.1.4 a), L 100x100x10 (cantos vivos), L = 2 m: yg = 28,684 mm; Ix1 = 8333 + 1000·23,684² + 607 500
        # + 900·26,316² = 1,80005e6 mm⁴; rx1 = 30,78 mm; L/rx1 = 65 ≤ 80 → KL = 72·30,78 + 0,75·2000 = 3716 mm;
        # Ne = π²·200 000·1,80005e6/3716² = 257,3 kN (= VC-aco-001)
        r = n.compressao(self.L, self.a36, 2.0, 2.0, 2.0, ligacao_uma_aba=True)
        self.assertLess(rel(r["E14"]["KxLx"], 3.716), 0.002)
        self.assertLess(rel(r["Ne"], 257.3), 0.003)
        # sem a ligação: eixos principais + aviso
        self.assertTrue(any("E.1.4" in a for a in n.compressao(self.L, self.a36, 2.0, 2.0)["avisos"]))

    def test_tubo_retangular_FLM_Wef(self):
        # G.1 caixão, TR 200x200x3 (A36): b/t = (200 − 9)/3 = 63,67 > λr = 1,40√(E/fy) = 39,6.
        # F.3.2 (ca = 0,38, σ = fy): bef = 1,92·3·28,284·(1 − 0,38·28,284/63,67) = 135,42 mm; perda
        # ΔA = (191 − 135,42)·3 = 166,7 mm² a d = 98,5 mm; A = 2364; I = (200⁴ − 194⁴)/12 = 15 294 292 mm⁴;
        # e = 7,475 mm; Ief = 13 553 782 mm⁴; Wef = Ief/107,475 = 126 111 mm³; W = 152 943 mm³ →
        # Mcr = fy·Wef²/W = 25,997 kN·m → MRd = 23,63 kN·m (FLA: 63,67 < 2,42√(E/fy) = 68,4 → Mpl = 43,66)
        T = s.secao_tubo_retangular(0.200, 0.200, 0.003)
        self.assertLess(rel(n.flexao_x(T, self.a36, 0.0)["MRd"], 23.63), 0.005)

    def test_secoes_fora_da_G1_reprovam(self):
        # cantoneira simples fletida: sem item na tabela G.1 (2008) → nao_verificado, ok = False
        r = n.verificar_barra_aco(self.L, self.a36, dict(Mx=1.0), 2, 2, 2)
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "nao_verificado")
        # cortante de cantoneira: Aw = b·t, kv = 1,2 → 0,6·100·10·0,25/1,1 = 136,4 kN, com aviso
        v = n.cortante_y(self.L, self.a36)
        self.assertLess(rel(v["VRd"], 136.36), 0.001)
        self.assertIn("aviso", v)

    def test_Q_menor_que_1_aviso_2024(self):
        # VC-aco-009: I 300x300x8x5 laminado, b/t = 30 → Q < 1 → aviso de edição
        r = n.compressao(s.secao_I(0.3, 0.3, 0.008, 0.005, soldado=False), self.a36, 3, 3)
        self.assertTrue(any("2024" in a for a in r["avisos"]))

    def test_Cb_balanco(self):
        # 5.4.2.3: trecho em balanço com extremidade livre → Cb = 1,0
        self.assertEqual(n.cb_de_diagrama([0, 3, 6], [0, -10, -40], balanco=True), 1.0)


class TestFicha026(unittest.TestCase):
    """Ficha 026 (revisão 2026-10-20-motores): entradas impossíveis → ValueError com o campo; nenhum
    resultado do aço, PFF ou ligações com inf/NaN (JSON estrito, allow_nan=False)."""

    def setUp(self):
        from prossen_estrutural.aco import ligacoes as lg, nbr14762 as pff
        self.lg, self.pff = lg, pff
        self.a36 = m.aco("A-36")
        self.L = s.secao_cantoneira(0.100, 0.100, 0.010)
        self.w = s.catalogo("W200x26.6")
        self.chapa = dict(h=0.23, t=0.008, d_mm=20, n=3, s=0.075, tw=0.0063, e=0.07)

    def test_I3_chapa_simples_s_menor_que_furo(self):
        # d = 20 mm → furo 21,5 mm, dn = 23,5 mm (6.3.6 + 5.2.4.1): s = 20 mm sobrepõe os furos
        with self.assertRaisesRegex(ValueError, r"chapa_simples.*\bs\b.*23[.,]5"):
            self.lg.verificar_ligacao("chapa_simples", dict(self.chapa, s=0.020), {"V": 50})

    def test_I3_bordas_menores_que_meio_furo(self):
        # eh = 10 mm < dn/2 = 11,75 mm: furo rompe a borda
        with self.assertRaisesRegex(ValueError, r"\beh\b"):
            self.lg.verificar_ligacao("chapa_simples", dict(self.chapa, eh=0.010), {"V": 50})
        t = dict(b=0.2, t=0.008, d_mm=20, n_linhas=1, n_por_linha=2, s=0.07, e1=0.010)
        with self.assertRaisesRegex(ValueError, r"\be1\b"):
            self.lg.verificar_ligacao("ligacao_tracao", t, {"N": 50})
        with self.assertRaisesRegex(ValueError, r"\bg\b"):
            self.lg.verificar_ligacao("ligacao_tracao", dict(t, e1=0.04, n_linhas=2, g=0.020), {"N": 50})

    def test_campo_inteiro_infinito(self):
        t = dict(b=0.2, t=0.008, d_mm=20, n_linhas=float("inf"), n_por_linha=2, s=0.07, e1=0.04)
        with self.assertRaisesRegex(ValueError, "n_linhas"):
            self.lg.verificar_ligacao("ligacao_tracao", t, {"N": 50})
        with self.assertRaisesRegex(ValueError, "n_linhas"):
            self.lg.verificar_ligacao("ligacao_tracao", dict(t, n_linhas=float("nan")), {"N": 50})

    def test_comprimentos_e_esforcos_invalidos(self):
        with self.assertRaisesRegex(ValueError, "Lx"):
            n.verificar_barra_aco(self.w, self.a36, dict(N=-10), 0.0, 3, 3)
        with self.assertRaisesRegex(ValueError, "Mx"):
            n.verificar_barra_aco(self.w, self.a36, dict(N=-10, Mx=float("nan")), 3, 3, 3)
        ue = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        with self.assertRaisesRegex(ValueError, "Lz"):
            self.pff.verificar_pff(ue, self.a36, dict(N=-5.0), 3.0, 1.5, 0.0, 1.5)
        with self.assertRaisesRegex(ValueError, "N"):
            self.pff.verificar_pff(ue, self.a36, dict(N=float("inf")), 3.0, 1.5, 1.5, 1.5)

    def test_cantoneira_momento_residual(self):
        # Mx = 1e-11 kN·m (ruído numérico) ≤ 1e-6·Wx·fy: desprezado; Nc só. Mx = 1 kN·m segue reprovando
        r = n.verificar_barra_aco(self.L, self.a36, dict(N=-10, Mx=1e-11), 2, 2, 2)
        self.assertNotEqual(r["criterio"], "nao_verificado")
        self.assertTrue(r["ok"])
        r = n.verificar_barra_aco(self.L, self.a36, dict(N=-10, Mx=1.0), 2, 2, 2)
        self.assertEqual(r["criterio"], "nao_verificado")
        self.assertTrue(any("cantoneira" in a and "não implementada" in a for a in r["avisos"]))

    def test_resultados_json_estrito(self):
        estrito = lambda r: json.dumps(r, allow_nan=False)  # noqa: E731
        # Lb = 0 → Mcr = ∞ internamente; vai como null
        estrito(n.verificar_barra_aco(self.w, self.a36, dict(N=-100, Mx=30, My=2, Vy=20, Vx=1), 3, 3, 0.0))
        estrito(n.verificar_barra_aco(self.w, self.a36, dict(N=100, Mx=30), 3, 3, 3))
        estrito(n.verificar_barra_aco(self.L, self.a36, dict(N=-10, Mx=1.0), 2, 2, 2))
        ue = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        estrito(self.pff.verificar_pff(ue, self.a36, dict(N=-5.0, Mx=1.0, My=0.1, Vy=2.0), 3.0, 1.5, 1.5, 0.0))
        estrito(self.pff.verificar_pff(ue, self.a36, dict(N=5.0, Mx=1.0), 3.0, 1.5, 1.5, 1.5))
        for tipo, d, e in [
                ("parafuso", dict(d_mm=16, tipo_parafuso="A307", t=0.0063, lf=0.03), {"Ft": 10, "Fv": 10}),
                ("parafuso", dict(d_mm=16, t=0.0063, lf=0.03), {}),
                ("solda_filete", dict(b=0.005, L=0.1, t_base=0.008), {"F": 50}),
                ("chapa_simples", self.chapa, {"V": 0.0}),
                ("ligacao_tracao", dict(b=0.2, t=0.008, d_mm=19, n_linhas=2, n_por_linha=3, s=0.07, g=0.1,
                                        e1=0.04), {"N": 300})]:
            estrito(self.lg.verificar_ligacao(tipo, d, e))

    def test_json_unico(self):
        self.assertIs(self.pff._json, n._json)
        self.assertEqual(n._json({1: (float("inf"), math.nan, 2.0)}), {"1": [None, None, 2.0]})


class TestTorcaoCortanteTubo(unittest.TestCase):
    """LCA-001/002/004/005/006 e VC-guia-002 (verificacao/logica-cype-aco.md). Esperados por solução fechada das
    expressões citadas (NBR 8800:2008 5.5.2.2/5.5.2.3 e 5.4.3.6 — iguais às do AISC 360-05 H3.1 e G6); A-36,
    E = 200 GPa, G = 77 GPa."""

    def setUp(self):
        self.a36 = m.aco("A-36")
        self.fy, self.E = 250e3, 200e6

    def test_tubo_circular_5_5_2_2(self):
        # TC 168,3x6,4 (D/t = 26,3): WT = π(D − t)²t/2 = 2,6346e-4 m³; τcr = 0,6fy → TRd = 35,93 kN·m
        D, t = 0.1683, 0.0064
        tc = s.secao_tubo_circular(D, t)
        WT = math.pi * (D - t) ** 2 * t / 2
        r = n.torcao(tc, self.a36, L=3.0)
        self.assertLess(rel(r["TRd"], 0.6 * self.fy * WT / 1.10), 1e-9)
        self.assertLess(rel(r["TRd"], 35.93), 0.001)
        # TC 323,8x2,7 (D/t = 120): sem L só 0,60E/(D/t)^1,5 = 91,29 MPa; com L = 3 m o termo
        # 1,23E/(√(L/D)(D/t)^1,25) = 203 MPa → 0,6fy = 150 MPa governa
        D, t = 0.3238, 0.0027
        tc = s.secao_tubo_circular(D, t)
        self.assertLess(rel(n.torcao(tc, self.a36)["tau_cr"], 0.60 * self.E / (D / t) ** 1.5), 1e-9)
        t1 = 1.23 * self.E / (math.sqrt(3.0 / D) * (D / t) ** 1.25)
        self.assertGreater(t1, 0.6 * self.fy)
        self.assertLess(rel(n.torcao(tc, self.a36, L=3.0)["tau_cr"], 0.6 * self.fy), 1e-9)
        # L = 40 m: 1,23E/(√(L/D)(D/t)^1,25) = 55,7 < 91,3 MPa → o maior dos dois (91,3) vale
        self.assertLess(rel(n.torcao(tc, self.a36, L=40.0)["tau_cr"], 0.60 * self.E / (D / t) ** 1.5), 1e-9)

    def test_tubo_retangular_5_5_2_3(self):
        # TR 200x100x6: WT = 2(B − t)(H − t)t − 4,5(4 − π)t³ = 2,1800e-4 m³; h/t = (200 − 18)/6 = 30,3
        # ≤ 2,45√(E/fy) = 69,3 → 0,6fy → TRd = 29,73 kN·m
        tr = s.secao_tubo_retangular(0.200, 0.100, 0.006)
        WT = 2 * 0.094 * 0.194 * 0.006 - 4.5 * (4 - math.pi) * 0.006 ** 3
        self.assertLess(rel(n.torcao(tr, self.a36)["TRd"], 0.6 * self.fy * WT / 1.10), 1e-9)
        # TR 300x300x3,5: h/t = (300 − 10,5)/3,5 = 82,7 em (69,3; 86,8] → 0,6fy·2,45√(E/fy)/(h/t)
        tr = s.secao_tubo_retangular(0.300, 0.300, 0.0035)
        ht, r = (0.300 - 3 * 0.0035) / 0.0035, math.sqrt(self.E / self.fy)
        self.assertTrue(2.45 * r < ht <= 3.07 * r)
        self.assertLess(rel(n.torcao(tr, self.a36)["tau_cr"], 0.6 * self.fy * 2.45 * r / ht), 1e-9)
        # TR 400x400x3: h/t = 130,3 > 3,07√(E/fy) → 0,458π²E/(h/t)² (AISC 360-05 H3-4)
        tr = s.secao_tubo_retangular(0.400, 0.400, 0.003)
        ht = (0.400 - 0.009) / 0.003
        self.assertLess(rel(n.torcao(tr, self.a36)["tau_cr"], 0.458 * math.pi ** 2 * self.E / ht ** 2), 1e-9)

    def test_torcao_reprova_e_interage_nos_tubos(self):
        tc = s.secao_tubo_circular(0.1683, 0.0064)
        TRd = 0.6 * self.fy * math.pi * (0.1683 - 0.0064) ** 2 * 0.0064 / 2 / 1.10
        r = n.verificar_barra_aco(tc, self.a36, dict(T=40.0), 3, 3, 3)
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "torcao")
        self.assertLess(rel(r["taxa_max"], 40.0 / TRd), 1e-9)
        # T > 0,2·TRd com N e M: (N/NRd + M/MRd) + (V/VRd + T/TRd)² (AISC 360-05 H3-6)
        e = dict(N=-200.0, Mx=10.0, Vy=20.0, T=15.0)
        r = n.verificar_barra_aco(tc, self.a36, e, 3, 3, 3)
        d = r["detalhes"]
        esp = (200 / d["compressao"]["NcRd"] + 10 / d["flexao_x"]["MRd"])             + (20 / d["cortante_y"]["VRd"] + 15 / d["torcao"]["TRd"]) ** 2
        self.assertLess(rel(d["taxas"]["torcao_interacao"], esp), 1e-9)
        # T ≤ 0,2·TRd: torção desprezada na interação (só T/TRd)
        r = n.verificar_barra_aco(tc, self.a36, dict(e, T=0.1 * TRd), 3, 3, 3)
        self.assertNotIn("torcao_interacao", r["detalhes"]["taxas"])
        self.assertLess(rel(r["detalhes"]["taxas"]["torcao"], 0.1), 1e-9)

    def test_secao_aberta_torcao_e_von_mises(self):
        # W 200x26,6: TRd = 0,6fy·(J/tmáx)/γa1 (τ de torção uniforme = T·t/J; critério do CYPE e de AISC
        # 360-05 H3.3 b) → C8: T = 1,2 kN·m reprova
        w = s.catalogo("W200x26.6")
        p = w.props
        TRd = 0.6 * self.fy * p["J"] / max(p["tw"], p["tf"]) / 1.10
        r = n.verificar_barra_aco(w, self.a36, dict(T=1.2), 3, 3, 3)
        self.assertFalse(r["ok"])
        self.assertLess(rel(r["taxa_max"], 1.2 / TRd), 1e-9)
        self.assertNotIn("von_mises", r["detalhes"]["taxas"])   # torção sozinha: sem Von Mises
        # C9: N = −100, Mx = 30, Vy = 60, T = 0,9 (> 0,2·TRd) → envelope de Von Mises (somas dos máximos):
        # σ = N/A + Mx/Wx + σw, σw = T·a·ωn/Cw, a = √(E·Cw/(G·J)); τ = Vy·(Zx/2)/(Ix·tw) + T·tmáx/J
        e = dict(N=-100.0, Mx=30.0, Vy=60.0, T=0.9)
        r = n.verificar_barra_aco(w, self.a36, e, 3, 3, 3)
        a = math.sqrt(self.E * p["Cw"] / (77e6 * p["J"]))
        sig = 100 / w.A + 30 / p["Wx"] + 0.9 * a * (p["h"] - p["tf"]) * p["b"] / 4 / p["Cw"]
        tau = 60 * p["Zx"] / 2 / (p["Ix"] * p["tw"]) + 0.9 * max(p["tw"], p["tf"]) / p["J"]
        esp = math.sqrt(sig ** 2 + 3 * tau ** 2) / (self.fy / 1.10)
        self.assertLess(rel(r["detalhes"]["taxas"]["von_mises"], esp), 1e-9)
        self.assertFalse(r["ok"])
        self.assertTrue(any("Von Mises" in x for x in r["avisos"]))

    def test_cortante_tubo_circular_5_4_3_6(self):
        # C5: TC 323,8x2,7 (D/t = 120), Lv = 3 m: 1,60E/(√(Lv/D)(D/t)^1,25) = 264,6 MPa → 0,6fy = 150 MPa;
        # VRd = 0,5·τcr·Ag/γa1 = 185,7 kN. Sem Lv: só 0,78E/(D/t)^1,5 = 118,7 MPa (a favor)
        D, t = 0.3238, 0.0027
        tc = s.secao_tubo_circular(D, t)
        self.assertLess(rel(n.cortante_y(tc, self.a36, Lv=3.0)["VRd"], 0.5 * 0.6 * self.fy * tc.A / 1.10), 1e-9)
        self.assertLess(rel(n.cortante_y(tc, self.a36, Lv=3.0)["VRd"], 185.7), 0.001)
        self.assertLess(rel(n.cortante_y(tc, self.a36)["tau_cr"], 0.78 * self.E / (D / t) ** 1.5), 1e-9)
        # verificar_barra_aco: Lv padrão = L; tubo com costura → espessura de cálculo 0,93t (AISC 360-05 B3.12)
        r = n.verificar_barra_aco(tc, self.a36, dict(Vy=100.0), 3, 3, 3, L=3.0)
        self.assertLess(rel(r["detalhes"]["cortante_y"]["VRd"], 185.7), 0.001)
        r = n.verificar_barra_aco(tc, self.a36, dict(Vy=100.0), 3, 3, 3, L=3.0, tubo_com_costura=True)
        tcd = s.secao_tubo_circular(D, 0.93 * t)
        self.assertLess(rel(r["detalhes"]["cortante_y"]["VRd"], 0.5 * 0.6 * self.fy * tcd.A / 1.10), 1e-9)
        self.assertTrue(any("0,93" in x for x in r["avisos"]))

    def test_cb_e_ct_fora_da_faixa(self):
        # NBR 8800 5.4.2.3 (Cb ≤ 3,0) e 5.2.5 (Ct ≤ 1,0): entrada inválida
        w = s.catalogo("W200x26.6")
        with self.assertRaisesRegex(ValueError, "Cb"):
            n.verificar_barra_aco(w, self.a36, dict(Mx=10), 3, 3, 3, Cb=3.5)
        with self.assertRaisesRegex(ValueError, "Ct"):
            n.verificar_barra_aco(w, self.a36, dict(N=10), 3, 3, 3, Ct=1.2)
        with self.assertRaisesRegex(ValueError, "Ct"):
            n.tracao(w, self.a36, Ct=0.0)
        self.assertTrue(n.verificar_barra_aco(w, self.a36, dict(Mx=10), 3, 3, 3, Cb=3.0)["ok"])


class TestAlturaCarga(unittest.TestCase):
    """LCA-017: altura de aplicação da carga no Mcr (FLT de I/U). Expressão de 3 fatores do NCCI SN003b-EN-EU
    (Access Steel, eq. 2, k = kw = 1): Mcr = C1·π²EIy/L²·[√(Cw/Iy + L²GJ/(π²EIy) + (C2·zg)²) − C2·zg], zg > 0
    para carga dirigida ao centro de cisalhamento aplicada acima dele (desestabilizante); C1 → Cb da NBR 8800
    5.4.2.3; C2 da tabela 3.2 do SN003b (máximo 1,645 com k = 1). G/(π²E) = 0,039 como na tabela G.1."""

    def setUp(self):
        self.w, self.a36 = s.catalogo("W200x26.6"), m.aco("A-36")

    def mcr(self, Lb, c2zg, cb=1.0):
        p, E = self.w.props, self.a36.E
        a = p["Cw"] / p["Iy"] * (1 + 0.039 * p["J"] * Lb ** 2 / p["Cw"])
        return cb * math.pi ** 2 * E * p["Iy"] / Lb ** 2 * (math.sqrt(a + c2zg ** 2) - c2zg)

    def test_topo_elastico(self):
        # Lb = 6 m: λ = 600/3,10 = 194 > λr → regime elástico, Mn = Mcr; topo sem C2 → C2 = 1,645 (a favor)
        h = self.w.props["h"]
        r0 = n.flexao_x(self.w, self.a36, 6.0)
        r = n.flexao_x(self.w, self.a36, 6.0, ponto_aplicacao="topo")
        self.assertGreater(r["lambda_FLT"][0], r["lambda_FLT"][2])
        self.assertLess(rel(r0["Mcr_FLT"], self.mcr(6.0, 0.0)), 1e-9)
        self.assertLess(rel(r["Mcr_FLT"], self.mcr(6.0, 1.645 * h / 2)), 1e-9)
        self.assertLess(rel(r["Mn_FLT"], r["Mcr_FLT"]), 1e-9)
        self.assertLess(r["Mcr_FLT"], 0.75 * r0["Mcr_FLT"])
        # C2 informado (SN003b tab. 3.2, carga uniforme biapoiada: C2 = 0,454) e Cb
        r = n.flexao_x(self.w, self.a36, 6.0, cb=1.13, ponto_aplicacao="topo", C2=0.454)
        self.assertLess(rel(r["Mcr_FLT"], self.mcr(6.0, 0.454 * h / 2, 1.13)), 1e-9)
        self.assertEqual((r["zg"], r["C2"]), (h / 2, 0.454))

    def test_base_e_inelastico(self):
        h = self.w.props["h"]
        r0 = n.flexao_x(self.w, self.a36, 6.0)
        # base sem C2: benefício ignorado (C2 = 0, a favor) — igual ao centro
        self.assertEqual(n.flexao_x(self.w, self.a36, 6.0, ponto_aplicacao="base")["Mcr_FLT"], r0["Mcr_FLT"])
        r = n.flexao_x(self.w, self.a36, 6.0, ponto_aplicacao="base", C2=0.454)
        self.assertLess(rel(r["Mcr_FLT"], self.mcr(6.0, -0.454 * h / 2)), 1e-9)
        self.assertGreater(r["MRd"], r0["MRd"])
        # regime inelástico (λp < λ ≤ λr) com carga no topo: Mn ≤ Mcr(zg)
        Lb = 2.2
        r = n.flexao_x(self.w, self.a36, Lb, ponto_aplicacao="topo")
        lam, lp, lr = r["lambda_FLT"]
        self.assertTrue(lp < lam <= lr)
        self.assertLessEqual(r["Mn_FLT"], self.mcr(Lb, 1.645 * h / 2) * (1 + 1e-12))
        with self.assertRaisesRegex(ValueError, "ponto_aplicacao"):
            n.flexao_x(self.w, self.a36, 6.0, ponto_aplicacao="meio")

    def test_verificar_barra(self):
        r0 = n.verificar_barra_aco(self.w, self.a36, dict(Mx=20.0), 6, 6, 6)
        r = n.verificar_barra_aco(self.w, self.a36, dict(Mx=20.0), 6, 6, 6, ponto_aplicacao="topo")
        self.assertGreater(r["taxa_max"], r0["taxa_max"])
        self.assertAlmostEqual(r["detalhes"]["flexao_x"]["C2"], 1.645)
        tr = s.secao_tubo_retangular(0.2, 0.1, 0.006)
        r = n.verificar_barra_aco(tr, self.a36, dict(Mx=20.0), 6, 6, 6, ponto_aplicacao="topo")
        self.assertTrue(any("ponto_aplicacao" in a for a in r["avisos"]))


if __name__ == "__main__":
    unittest.main()
