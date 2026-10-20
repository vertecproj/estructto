"""Regressão — aço NBR 8800:2008, seções e catálogo Gerdau (verificador-calculos, ficha 013, 2026-10-20).

Valores esperados derivados à mão, sem o código (memórias em verificacao/memorias/aco-*.py) ou tirados
de fonte publicada. Relatório: verificacao/2026-10-20-aco.md (ids VC-aco-NNN).
Testes marcados expectedFailure reproduzem achados abertos: o dono remove o decorador ao corrigir.

Fontes:
[NBR]  ABNT NBR 8800:2008 (itens citados em cada teste; cópia não oficial consultada em 2026-10-20 —
       confirmar no texto oficial).
[G18]  Gerdau, folder "Perfis Estruturais Gerdau – Segmento Industrial", 10/18, Tabela de Bitolas (p. 3),
       https://gsn.gerdau.com/sites/gsn_gerdau/files/downloadable_files/Folder%20Perfis%20Estruturais%20Gerdau%20-%20Segmento%20Industrial.pdf
[MEM]  memória independente verificacao/memorias/aco-nbr8800.py e aco-secoes.py (fórmulas fechadas de
       parede fina; "spandrel" das concordâncias com área (1 − π/4)r², momento estático (5/6 − π/4)r³,
       inércia (1 − 5π/16)r⁴ em relação à face).
[S1]   NBR 8800:2008 Anexo A, tabelas A.1/A.2; [S2] NBR 14762:2010 tabela 1; [S3] Anuário Brasileiro da
       Siderurgia, "Informações Técnicas"; [S6] ficha "Aços de qualidade estrutural resistentes à corrosão
       atmosférica" (industriahoje.com.br) — ver relatório.
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from prossen_estrutural import secoes as s, materiais as m  # noqa: E402
from prossen_estrutural.aco import nbr8800 as n  # noqa: E402
from prossen_estrutural.modelo import Modelo, No, Barra, CasoCarga, CargaNodal  # noqa: E402
from prossen_estrutural.solver import analisar  # noqa: E402


def rel(a, b):
    return abs(a - b) / abs(b)


A36 = None


def setUpModule():
    global A36
    A36 = m.aco("A-36")


class TestRegSecoes(unittest.TestCase):
    """[MEM] propriedades por fórmula fechada (mm → m)."""

    def test_W200_com_concordancias(self):
        # W 200 x 26,6: d 207, bf 133, tw 5,8, tf 8,4, R = (h − d')/2 = (190 − 170)/2 = 10 mm [G18]
        # A = 2·133·8,4 + 190,2·5,8 + (4 − π)·10² = 3423,4 mm²; Ix = 26 111 686; Iy = 3 299 348 mm⁴;
        # Zx = 282 303; Zy = 76 334 mm³; Cw = tf·bf³·(d − tf)²/24 = 3,24774e10 mm⁶
        w = s.catalogo("W200x26.6")
        p = w.props
        for obt, esp in ((w.A, 3423.4e-6), (p["Ix"], 26111686e-12), (p["Iy"], 3299348e-12),
                         (p["Zx"], 282303e-9), (p["Zy"], 76334e-9), (p["Cw"], 3.24774e10 * 1e-18)):
            self.assertLess(rel(obt, esp), 0.002)

    def test_formas_fechadas(self):
        # tubo retangular 200x100x6: A = 3456 mm²; J (Bredt, linha média) = 4Am²t/p = 1,38563e7 mm⁴
        T = s.secao_tubo_retangular(0.200, 0.100, 0.006)
        self.assertLess(rel(T.A, 3456e-6), 1e-6)
        self.assertLess(rel(T.props["J"], 1.38563e7 * 1e-12), 1e-4)
        # tubo circular 168,3x7: I = π(D⁴ − d⁴)/64 = 1,15579e7 mm⁴, J = 2I
        C = s.secao_tubo_circular(0.1683, 0.007)
        self.assertLess(rel(C.props["J"], 2.31158e7 * 1e-12), 1e-4)
        # L 100x100x10: A = 1900 mm²; Imin = 734 254 mm⁴ (eixos principais)
        L = s.secao_cantoneira(0.100, 0.100, 0.010)
        self.assertLess(rel(L.props["Imin"], 734254e-12), 1e-4)
        # Ue 150x60x20x2 cantos vivos (Yu & LaBoube, ap. B): Cw = 1,49857e9 mm⁶; x0 = 46,83 mm; J = 805,33 mm⁴
        ue = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        self.assertLess(rel(ue.props["Cw"], 1.49857e9 * 1e-18), 1e-4)
        self.assertLess(rel(abs(ue.props["x0"]), 46.828e-3), 1e-4)


class TestRegCatalogoGerdau(unittest.TestCase):
    """10 perfis sorteados contra [G18] (A, Wx, rx, Wy, ry impressos; tolerância 1 %)."""

    TAB = {  # nome: (d, bf, tw, tf, A cm², Ix, Wx, rx, Iy, Wy, ry)
        "W150x13": (148, 100, 4.3, 4.9, 16.6, 635, 85.8, 6.18, 82, 16.4, 2.22),
        "W200x26.6": (207, 133, 5.8, 8.4, 34.2, 2611, 252.3, 8.73, 330, 49.6, 3.10),
        "W250x32.7": (258, 146, 6.1, 9.1, 42.1, 4937, 382.7, 10.83, 473, 64.8, 3.35),
        "W310x38.7": (310, 165, 5.8, 9.7, 49.7, 8581, 553.6, 13.14, 727, 88.1, 3.82),
        "W360x44.6": (352, 171, 6.9, 9.8, 57.7, 12258, 696.5, 14.58, 818, 95.7, 3.77),
        "W410x60": (407, 178, 7.7, 12.8, 76.2, 21707, 1066.7, 16.88, 1205, 135.4, 3.98),
        "W460x74": (457, 190, 9.0, 14.5, 94.9, 33415, 1462.4, 18.77, 1661, 174.8, 4.18),
        "W530x85": (535, 166, 10.3, 16.5, 107.7, 48453, 1811.3, 21.21, 1263, 152.2, 3.42),
        "W610x125": (612, 229, 11.9, 19.6, 160.1, 99184, 3241.3, 24.89, 3933, 343.5, 4.96),
        "HP250x62": (246, 256, 10.5, 10.7, 79.6, 8728, 709.6, 10.47, 2995, 234.0, 6.13),
    }

    def test_dez_perfis(self):
        cm = 0.01
        for nome, (d, bf, tw, tf, A, Ix, Wx, rx, Iy, Wy, ry) in self.TAB.items():
            sec = s.catalogo(nome)
            p = sec.props
            for obt, esp in ((p["h"], d / 1e3), (p["b"], bf / 1e3), (p["tw"], tw / 1e3), (p["tf"], tf / 1e3)):
                self.assertAlmostEqual(obt, esp, places=9, msg=nome)
            for k, obt, esp in (("A", sec.A, A * cm ** 2), ("Ix", p["Ix"], Ix * cm ** 4), ("Wx", p["Wx"], Wx * cm ** 3),
                                ("rx", p["rx"], rx * cm), ("Iy", p["Iy"], Iy * cm ** 4), ("Wy", p["Wy"], Wy * cm ** 3),
                                ("ry", p["ry"], ry * cm)):
                self.assertLess(rel(obt, esp), 0.01, f"{nome} {k}")


class TestRegNBR8800(unittest.TestCase):
    """Valores da memória [MEM] pelas fórmulas da NBR 8800:2008 (A36: fy 250, E 200 GPa)."""

    def setUp(self):
        self.w = s.catalogo("W200x26.6")

    def test_Q_grupos_4_e_5(self):
        # F.2 grupo 4 (laminado): b/t = 18,75 → Qs = 1,415 − 0,74·18,75·√(250/200000) = 0,92445;
        # b/t = 30 > 1,03√(E/fy) = 29,13 → Qs = 0,69E/(fy(b/t)²) = 0,61333.
        # Grupo 5 (soldado 500x400x6,3x8): kc = 4/√(484/6,3) = 0,4564; b/t = 25 > 1,17√(E·kc/fy) = 22,35
        #   → Qs = 0,90E·kc/(fy(b/t)²) = 0,52573
        self.assertLess(rel(n.fator_Q(s.secao_I(0.3, 0.3, 0.008, 0.008, soldado=False), A36)[1], 0.92445), 1e-4)
        self.assertLess(rel(n.fator_Q(s.secao_I(0.3, 0.3, 0.008, 0.005, soldado=False), A36)[1], 0.61333), 1e-4)
        self.assertLess(rel(n.fator_Q(s.secao_I(0.5, 0.4, 0.0063, 0.008), A36)[1], 0.52573), 1e-4)

    def test_FLT_ramos(self):
        # G.1 FLT, W 200 x 26,6 com J = (2bf·tf³ + (d − tf)tw³)/3 = 6,547 cm⁴, Cw = 32 477 cm⁶:
        # λp = 49,78; λr = 162,25. Lb = 1 m → plastificação 64,160; 3 m, Cb 1 → 54,151 (inelástica);
        # 3 m, Cb 1,3 → limitado a Mpl: 64,160; 6 m, Cb 1 → Mcr = 35,13 → 31,934; 6 m, Cb 2 → 63,868 kN·m
        for Lb, cb, esp in ((1, 1, 64.160), (3, 1, 54.151), (3, 1.3, 64.160), (6, 1, 31.934), (6, 2, 63.868)):
            self.assertLess(rel(n.flexao_x(self.w, A36, Lb, cb)["MRd"], esp), 0.005, (Lb, cb))

    def test_FLM_elastica(self):
        # G.1 nota 6, laminado: I 300x300x10x5, b/t = 30 > λr = 0,83√(E/(0,7fy)) = 28,06 → Mcr = 0,69E·W/λ²
        Ie = s.secao_I(0.300, 0.300, 0.010, 0.005, soldado=False)
        self.assertLess(rel(n.flexao_x(Ie, A36, 0.01)["Mn_FLM"], 0.69 * 200e6 * Ie.props["Wx"] / 900), 1e-6)
        # soldado 500x400x6,3x8: Mcr = 0,90E·kc·W/λ², kc = 0,45644, λ = 25
        So = s.secao_I(0.5, 0.4, 0.0063, 0.008)
        kc = 4 / math.sqrt(0.484 / 0.0063)
        self.assertLess(rel(n.flexao_x(So, A36, 0.01)["Mn_FLM"], 0.90 * 200e6 * kc * So.props["Wx"] / 625), 1e-6)

    def test_flexao_y_limite_1_5W(self):
        # 5.4.2.2: MRd ≤ 1,50·W·fy/γa1 → W200: 1,5·49,614·25/1,1/100 = 16,914 kN·m (< Zy·fy/1,1 = 17,349)
        self.assertLess(rel(n.flexao_y(self.w, A36)["MRd"], 16.914), 0.002)

    def test_cortante_elastico_e_mesas(self):
        # 5.4.3.1.1 elástico: I 800x300x5x16, λ = 768/5 = 153,6 > λr = 86,65; λp = 69,57;
        # VRd = 1,24(λp/λ)²·0,6·800·5·250/1,1 = 138,75 kN
        self.assertLess(rel(n.cortante_y(s.secao_I(0.8, 0.3, 0.005, 0.016), A36)["VRd"], 138.75), 0.002)
        # 5.4.3.5: Aw = 2bf·tf = 2234,4 mm², kv = 1,2, λ = 7,92 < λp → 0,6·2234,4·0,25/1,1 = 304,69 kN
        self.assertLess(rel(n.cortante_x(self.w, A36)["VRd"], 304.69), 0.002)

    def test_interacao_fronteira_e_tracao(self):
        # 5.5.1.2: N/NRd = 0,2 exato → ramo a): 0,2 + 8/9·(20/54,151) = 0,5283 (NcRd = 474,3; KL = 3 m)
        r = n.verificar_barra_aco(self.w, A36, dict(N=-0.2 * 474.24, Mx=20), 3, 3, 3)
        self.assertLess(rel(r["detalhes"]["taxas"]["interacao"], 0.5283), 0.002)
        # tração: NRd = Nt,Rd = 34,234·25/1,1 = 778,05 kN → 300/778,05 + 8/9·20/54,151 = 0,7139
        r = n.verificar_barra_aco(self.w, A36, dict(N=300, Mx=20), 3, 3, 3)
        self.assertLess(rel(r["detalhes"]["taxas"]["interacao"], 0.7139), 0.002)

    def test_Cb(self):
        # 5.4.2.3, biengastada com carga uniforme: Mmáx = qL²/12, MA = MC = qL²/96, MB = qL²/24 → 2,381;
        # diagrama com quartos nulos → 12,5/2,5 = 5 → limitado a 3,0
        xs = [6.0 * k / 12 for k in range(13)]
        self.assertAlmostEqual(n.cb_de_diagrama(xs, [10 * (3 * x - x * x / 2 - 3) for x in xs]), 2.3810, places=3)
        self.assertAlmostEqual(n.cb_de_diagrama(xs, [1.0 if k in (0, 12) else 0.0 for k in range(13)]), 3.0)

    def test_esforcos_nbr_viga_em_Y_e_pilar(self):
        # balanço de 2 m ao longo de Y: P = 10 kN (−Z) → Mx = 20; H = 3 kN (+X) → My = 6 kN·m
        # pilar de 3 m (rot = 0, y local = Y global = eixo forte): Hx = 4 → Mx = 12; Hy = 1 → My = 3 kN·m
        for nj, F, esp in (((0, 2, 0), (3, 0, -10, 0, 0, 0), (20, 6, 10, 3)),
                           ((0, 0, 3), (4, 1, 0, 0, 0, 0), (12, 3, 4, 1))):
            mod = Modelo()
            mod.add(A36)
            mod.add(s.catalogo("W200x26.6"))
            mod.add(CasoCarga("G", "G"))
            mod.add(No("a", 0, 0, 0, apoio=(True,) * 6))
            mod.add(No("b", *nj))
            mod.add(Barra("v", "a", "b", "W200x26.6", "A-36"))
            mod.add(CargaNodal("G", "b", F))
            e = n.esforcos_nbr(analisar(mod, "G").esforcos["v"][0])
            for k, v in zip(("Mx", "My", "Vy", "Vx"), esp):
                self.assertAlmostEqual(abs(e[k]), v, places=6, msg=(nj, k))


class TestRegExemplosPublicados(unittest.TestCase):
    """FLM inelástica, FLA e interação ≥ 0,2 contra exemplos publicados (pendência da onda 1)."""

    def test_FLM_laminado_calculistadeaco(self):
        # [CALC-5] calculistadeaco.com.br, "Aula 5 – Barras flexionadas", slides 23–29 (NBR 8800:2008):
        # W 250 x 17,9, A572 Gr50, E = 20 500 kN/cm² (valor do autor); Zx = 211, Wx = 182,6 cm³;
        # λ = 9,53, λp = 9,26, λr = 24,18 → MRd = 6570,5 kN·cm
        from prossen_estrutural.modelo import Material, MPa
        mat = Material("A572 E205", 205e3 * MPa, 78.8e3 * MPa, fy=345 * MPa, fu=450 * MPa)
        r = n.flexao_x(s.catalogo("W250x17.9"), mat, 1e-4)
        for obt, esp in zip(r["lambda_FLM"], (9.53, 9.26, 24.18)):
            self.assertLess(rel(obt, esp), 0.005)
        self.assertLess(rel(r["MRd"], 65.705), 0.005)

    def test_FLM_HP310_UFMG(self):
        # [UFMG-SA] S. R. Antunes (orient. Fakury), UFMG, pp. 33–36, pilar HP 310 x 93,0 (inferido pelas
        # dimensões), A572 Gr50: λ = 11,75, λp = 9,15, λr = 23,88, Mpl = 50 035,35, Mr = 31 370,85 kN·cm.
        # A fonte imprime MRk = 44 975,8 (erro de conta): a interpolação com os números dela dá 46 741 kN·cm
        # → MRd = 424,9 kN·m (FLT com Cb = 2,17 não governa).
        r = n.flexao_x(s.catalogo("HP310x93"), m.aco("A-572 GR.50"), 3.2, 2.17)
        self.assertLess(rel(r["Mn_FLM"], 467.41), 0.005)
        self.assertLess(rel(r["MRd"], 424.9), 0.005)

    def test_FLM_soldado_UFMG(self):
        # [UFMG-SA] pp. 27–30, VS 650 x 98, fy = 300 MPa: λ = 12, λp = 9,81, kc = 0,4525.
        # A fonte imprime λr = 43,6 (divide por kc em vez de multiplicar); pela nota 6 da tabela G.1,
        # λr = 0,95√(E·kc/(0,7fy)) = 19,72 → Mn = 95 160 − 35 394·(12 − 9,81)/(19,72 − 9,81) = 87 345 kN·cm.
        # Cortante (p. 30): λ = 78,13, λp = 63,5, λr = 79 → VRd = 691,6 kN
        from prossen_estrutural.modelo import Material, MPa
        mat = Material("USI300", 200e3 * MPa, 77e3 * MPa, fy=300 * MPa, fu=400 * MPa)
        v = s.secao_I(0.65, 0.3, 0.008, 0.0125)
        self.assertLess(rel(n.flexao_x(v, mat, 1e-4)["Mn_FLM"], 873.45), 0.005)
        self.assertLess(rel(n.cortante_y(v, mat)["VRd"], 691.6), 0.005)

    def test_FLA_inelastica_memoria(self):
        # sem exemplo publicado (pesquisa 2026-10-20). [MEM] I 600x300x5x16, A36: λ = 113,6, λp = 106,35,
        # λr = 161,22; W = 2983,6, Z = 3206,5 cm³ → Mn = 794,26 kN·m (G.1, Mr = fy·W)
        self.assertLess(rel(n.flexao_x(s.secao_I(0.6, 0.3, 0.005, 0.016), A36, 0.01)["Mn_FLA"], 794.26), 0.002)

    def test_interacao_maior_0_2_UFMG(self):
        # [UFMG-HS] H. P. R. Siqueira (orient. Lavall), UFMG 2024, pp. 54–57, pilar W 250 x 80 (inferido),
        # A572 Gr50, L = Lb = 3,5 m, Cb = 1: Nc,Rd = 2588,73 kN; Mx,Rd = 329,02 kN·m (J da fonte 75,02 cm⁴;
        # o catálogo usa 71,19, sem concordâncias); NSd = 1434,68, MSd = 67,71 →
        # 1434,68/2588,73 + 8/9·67,71/329,02 = 0,737
        w = s.catalogo("W250x80")
        a = m.aco("A-572 GR.50")
        self.assertLess(rel(n.compressao(w, a, 3.5, 3.5, 3.5)["NcRd"], 2588.73), 0.005)
        self.assertLess(rel(n.flexao_x(w, a, 3.5)["MRd"], 329.02), 0.005)
        r = n.verificar_barra_aco(w, a, dict(N=-1434.68, Mx=67.71, Vy=48.18), 3.5, 3.5, 3.5)
        self.assertEqual(r["criterio"], "interacao")
        self.assertLess(rel(r["taxa_max"], 0.737), 0.005)


class TestRegMateriais(unittest.TestCase):
    """fy/fu (MPa) contra [S1] (NBR 8800 A.1/A.2), [S2] (NBR 14762 tab. 1) e [S3]."""

    def test_aços(self):
        tab = {"A-572 GR.42": (290, 415), "A-992": (345, 450), "A-588": (345, 485), "A-242 T<=19": (345, 485),
               "A-500 GR.B": (290, 400), "CG-26": (255, 410), "CG-28": (275, 440), "CF-24": (240, 370),
               "CF-26": (260, 400), "CF-28": (280, 440), "CF-30": (300, 490), "ZAR-250": (250, 360),
               "ZAR-280": (280, 380), "ZAR-345": (345, 430), "USI-SAC-300": (300, 400), "USI-SAC-350": (350, 500),
               "MR-250": (250, 400), "AR-350": (350, 450), "AR-350-COR": (350, 485), "AR-415": (415, 520)}
        for nome, (fy, fu) in tab.items():
            a = m.aco(nome)
            self.assertAlmostEqual(a.fy, fy * 1000, msg=nome)
            self.assertAlmostEqual(a.fu, fu * 1000, msg=nome)

    def test_VC_aco_011_COS_AR_COR_500(self):
        # [S6] nota 2: fy = 355 MPa até 16 mm e 375 MPa de 16,01 a 50,8 mm. Um só valor por aço →
        # o menor (a favor da segurança) é 355.
        self.assertAlmostEqual(m.aco("COS-AR-COR-500").fy, 355e3)


class TestRegAchados(unittest.TestCase):
    """Achados abertos (expectedFailure até a correção)."""

    def test_VC_aco_001_cantoneira_eixos_principais(self):
        # E.1.2/E.1.3: x, y são os eixos CENTRAIS PRINCIPAIS. L 100x100x10, KL = 2 m:
        # flexão em torno do eixo de menor inércia: Ne ≤ π²E·Imin/KL² = π²·200e6·7,34254e-7/4 = 362,3 kN
        # (E.1.4, cantoneira ligada por uma aba, dá ainda menos: KL = 72rx1 + 0,75L = 3,716 m → 257 kN).
        # Código: 694 kN (eixos paralelos às abas) → Nc,Rd 324 kN contra 249,5 kN (+30 %).
        L = s.secao_cantoneira(0.100, 0.100, 0.010)
        self.assertLessEqual(n.forca_flambagem(L, A36, 2.0, 2.0, 2.0)["Ne"], 362.4)
        self.assertLess(rel(n.compressao(L, A36, 2.0, 2.0, 2.0)["NcRd"], 249.5), 0.01)

    def test_VC_aco_002_U_x0_Cw(self):
        # U 200x75x8x12 (d, bf, tw, tf): G.1 nota 1 dá Cw = tf(bf − 0,5tw)³(d − tf)²/12 ·
        # [3(bf − 0,5tw)tf + 2(d − tf)tw]/[6(bf − 0,5tw)tf + (d − tf)tw] = 1,06385e10 mm⁶;
        # parede fina: e = 3b'²tf/(6b'tf + h'tw) = 27,43 mm, x̄ = 18,86 mm → x0 = 46,29 mm.
        # Código usa espessura média (tf + tw)/2 nos dois: x0 = 39,9 mm (−14 %), Cw −5 %.
        U = s.secao_U(0.200, 0.075, 0.008, 0.012)
        self.assertLess(rel(abs(U.props["x0"]), 46.29e-3), 0.02)
        self.assertLess(rel(U.props["Cw"], 1.06385e10 * 1e-18), 0.01)

    def test_VC_aco_003_U_FLT(self):
        # G.1 (FLT de "seções U não sujeitas a momento de torção"): U 200x75x8x12, A36, Lb = 4 m, Cb = 1:
        # Iy = 173,79 cm⁴, ry = 2,328 cm, J = 11,64 cm⁴, Cw = 10 638,5 cm⁶, Wx = 195,61 cm³, Zx = 231,15 cm³
        # λ = 171,9 > λp = 49,8, < λr = 229,3 → MRd = 37,97 kN·m. Código: Zx·fy/1,1 = 52,54 (+38 %).
        U = s.secao_U(0.200, 0.075, 0.008, 0.012)
        self.assertLess(rel(n.flexao_x(U, A36, 4.0)["MRd"], 37.97), 0.02)

    def test_VC_aco_004_cortante_tubos(self):
        # 5.4.3.2: tubo retangular, Aw = 2·h·tw com h = altura da parte PLANA das almas (paralelas à força).
        # TR 200x100x6 (cantos vivos: parte plana = lado − 2t): Vx usa as paredes de 100 mm →
        # VRd,x ≤ 0,6·2·(100 − 12)·6·0,25/1,1 = 144,0 kN; Vy ≤ 0,6·2·188·6·0,25/1,1 = 307,6 kN. Código: 327,3 e 327,3.
        T = s.secao_tubo_retangular(0.200, 0.100, 0.006)
        self.assertLessEqual(n.cortante_x(T, A36)["VRd"], 144.1)
        self.assertLessEqual(n.cortante_y(T, A36)["VRd"], 307.7)
        # 5.4.3.6: tubo circular, VRd = 0,5·τcr·Ag/γa1 com τcr ≤ 0,6fy → VRd ≤ 0,3·Ag·fy/γa1
        C = s.secao_tubo_circular(0.1683, 0.007)
        self.assertLessEqual(n.cortante_y(C, A36)["VRd"], 0.3 * C.A * 250e3 / 1.1 * (1 + 1e-9))

    def test_VC_aco_007_kc_maximo(self):
        # F.2: 0,35 ≤ kc ≤ 0,76 (código 0,763). Soldado 300x300x10x8: h/tw = 28,4 → kc = 0,7506 (sem efeito);
        # h/tw = 20 (I 216x150x10x8) → kc = 0,894 → 0,76: λr(FLM) = 0,95√(E·0,76/(0,7fy)) = 27,998
        I = s.secao_I(0.216, 0.150, 0.010, 0.008)
        self.assertLess(rel(n.flexao_x(I, A36, 0.01)["lambda_FLM"][2], 27.998), 1e-4)


if __name__ == "__main__":
    unittest.main()
