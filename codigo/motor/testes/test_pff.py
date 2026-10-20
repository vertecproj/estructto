"""Testes de aco/nbr14762.py (NBR 14762:2010, método da largura efetiva) e de dados/perfis_pff.csv.

Referências (valores esperados tirados das fontes, nunca do código):
[CBCA] Silva, Pierin & Silva, "Estruturas compostas por perfis formados a frio — dimensionamento pelo
       método das larguras efetivas e aplicação conforme ABNT NBR 14762:2010 e ABNT NBR 6355:2012",
       CBCA/Instituto Aço Brasil, 2014, Anexo D, Exemplo 4.12 (longarina S13, Ue 300x85x25x3,00,
       fy = 24 kN/cm², E = 20000 kN/cm², G = 7700 kN/cm², Lx = 1029 cm, Ly = Lz = 257,25 cm, Cb = 1,45).
       Valores do manual transcritos em R. J. F. de Araújo, "Desenvolvimento de ferramenta para avaliação
       de perfis U enrijecidos segundo a NBR 14762:2010 pelo MRD", monografia, UFMG, 2025, Tabelas 16–20:
       Nex = 364,02 kN, Ney = 399,95 kN, Nexz = Ne = 276,48 kN, Aef = 11,78 cm², Nc,Rd = 136,59 kN,
       Ief = 1845,89 cm⁴, MRd,esc = 2551,45 kN·cm, MRd,FLT = 2523,42 kN·cm.
       O manual usa raio interno ri = t (conferido: com ri = t as forças de flambagem batem a 0,1 %).
       Só a compressão é usada: o Ief do manual (programa DimPerfil) não se reproduz à mão por 9.2 —
       mesa λp = 0,47 e alma bef1 + bef2 ≥ bc → seção totalmente efetiva, MRd = 2840 kN·cm; a própria
       monografia obtém 2850 kN·cm por outro método.
[UFAL-C] L. B. Santos, "PFF — barras submetidas à compressão axial", notas de aula, UFAL (eduCAPES 572684),
       exercício MLE: Ue 250x85x25x2,00, ri = t, fy = 300 MPa, KxLx = 240 cm, KyLy = KzLz = 120 cm:
       χ = 0,890; def = 2,10; ds = 1,54; bef,mesa = 6,91; bef,alma = 9,42 cm; Aef = 5,64 cm²;
       Nc,Rd = 0,890·5,64·30/1,20 = 125,5 kN.
[UFAL-F] L. B. Santos, "PFF — barras submetidas à flexão simples", notas de aula, UFAL (eduCAPES 572685),
       exemplo de Silva et al. (2014): Ue 100x50x17x1,20, fy = 250 MPa, Lb = 400 cm, propriedades da fonte
       (A = 2,71 cm², Ix = 44,14 cm⁴, Wx = 8,83 cm³, Iy = 10,12 cm⁴, It = 0,013 cm⁴, Cw = 246,61 cm⁶,
       x0 = 4,28 cm, r0 = 6,19 cm): Cb = 1,31 → Me = 92,99 kN·cm, λ0 = 1,541, χFLT = 0,421,
       MRd,FLT = 84,53 kN·cm; Cb = 1,0 → 64,53 kN·cm. Cortante (h/t = 79,33, regime inelástico): VRd = 13,45 kN.
[TBS]  T. B. Soares, "Projeto de um edifício de pequeno porte estruturado em perfis formados a frio",
       monografia, UFMG, 2018, item 4.5.1.1: Ue 300x85x25x2,25 (meio Cx 300x170x25x2,25), fy = 340 MPa,
       E = 200 GPa, ri = t: largura efetiva da mesa 9.2.3 (k = 2,93; bef = 6,60 cm) e Wef = 89,78 cm³.
[JMM]  J. M. Maia, "Dimensionamento da estrutura principal de um galpão utilizando perfis de aço formados
       a frio", monografia, UFMG, item 9.8.3: alma h = 241 mm, t = 2,25 mm → VRd = 38,89 kN;
       h = 360 mm, t = 3,75 mm → VRd = 120,52 kN (fy = 350 MPa, E = 200 GPa, kv = 5).
[AFO]  A. F. F. Oliveira, "Galpão em perfis formados a frio", monografia, UFMG, 2015, item 7.1 F.1:
       Ue 200x100x25x3,00 (bf/bw = 0,50; bw/t = 66,7; D/bw = 0,125 ≥ 0,09) dispensa a distorcional.
[UFAL] L. B. Santos, "PFF — barras submetidas à flexão composta", notas de aula, UFAL (eduCAPES 572686),
       exercício 1: 90/301,21 + 2000/3503,90 = 0,87 (interação linear 9.9).
"""
import csv
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes as s  # noqa: E402
from prossen_estrutural.modelo import Material, MPa, Secao  # noqa: E402
from prossen_estrutural.aco import nbr14762 as pff  # noqa: E402

CM = 0.01
KNCM2 = 1e4  # kN/cm² -> kN/m²


def rel(a, b):
    return abs(a - b) / abs(b)


def ue_arredondado(h, b, D, t, ri, n=12):
    """Ue com cantos de raio interno ri pela linha média (método linear), para comparar com fontes
    que usam a geometria NBR 6355. Propriedades por secoes._parede_fina (polilinha com arcos)."""
    rm, hm, bm, Dm = ri + t / 2, h - t, b - t, D - t / 2
    pts = [(bm, hm / 2 - Dm)]

    def arco(cx, cy, a0, a1):
        pts.extend((cx + rm * math.cos(a0 + (a1 - a0) * k / n), cy + rm * math.sin(a0 + (a1 - a0) * k / n))
                   for k in range(n + 1))
    arco(bm - rm, hm / 2 - rm, 0, math.pi / 2)
    arco(rm, hm / 2 - rm, math.pi / 2, math.pi)
    arco(rm, -hm / 2 + rm, math.pi, 1.5 * math.pi)
    arco(bm - rm, -hm / 2 + rm, 1.5 * math.pi, 2 * math.pi)
    pts.append((bm, -hm / 2 + Dm))
    g = s._parede_fina(pts, t)
    P = g["pts"]
    xs = [p[0] for p in P]
    return s._monta("Ue arredondado", "Ue", g["A"], g["Ix"], g["Iy"], g["J"],
                    g["Ix"] / (max(abs(p[1]) for p in P) + t / 2), g["Iy"] / (max(max(xs), -min(xs)) + t / 2),
                    0.0, 0.0, g["Cw"], g["x0"], g["y0"], h=h, b=b, D=D, t=t, ri=ri)


CBCA_MAT = Material("CBCA 4.12", 20000 * KNCM2, 7700 * KNCM2, fy=24 * KNCM2, fu=37 * KNCM2)


class TestLarguraEfetiva(unittest.TestCase):
    """9.2.2: λp = (b/t)/(0,95·√(kE/σ)); bef = b(1 − 0,22/λp)/λp ≤ b; bef = b se λp ≤ 0,673."""

    def test_AA(self):
        # b = 100 mm, t = 1 mm, k = 4, σ = 250 MPa, E = 200 GPa:
        # λp = 100/(0,95·√(4·200000/250)) = 100/53,740 = 1,8608; bef = 100·(1 − 0,22/1,8608)/1,8608 = 47,39 mm
        bef, lp = pff.largura_efetiva(0.100, 0.001, 4.0, 250 * MPa, 200e3 * MPa)
        self.assertLess(rel(lp, 1.8608), 0.001)
        self.assertLess(rel(bef, 0.04739), 0.001)

    def test_AL(self):
        # b = 20 mm, t = 1 mm, k = 0,43, σ = 250 MPa: λp = 20/(0,95·√(0,43·800)) = 20/17,620 = 1,1351
        # bef = 20·(1 − 0,22/1,1351)/1,1351 = 14,20 mm
        bef, lp = pff.largura_efetiva(0.020, 0.001, 0.43, 250 * MPa, 200e3 * MPa)
        self.assertLess(rel(lp, 1.1351), 0.001)
        self.assertLess(rel(bef, 0.014205), 0.001)

    def test_totalmente_efetivo(self):
        # λp = 0,673 exato → bef = b; logo acima, bef ≤ b e contínuo
        E, sig, t, k = 200e3 * MPa, 250 * MPa, 0.001, 4.0
        b = 0.673 * 0.95 * math.sqrt(k * E / sig) * t
        self.assertEqual(pff.largura_efetiva(b * (1 - 1e-9), t, k, sig, E)[0], b * (1 - 1e-9))
        bef = pff.largura_efetiva(b * (1 + 1e-6), t, k, sig, E)[0]
        self.assertLessEqual(bef, b * (1 + 1e-6))  # bef ≤ b (a fórmula dá 1,0002·b logo acima de 0,673)
        self.assertLess(rel(bef, b), 0.001)

    def test_mesa_com_enrijecedor_TBS(self):
        # [TBS] mesa b = 76 mm, enrijecedor d = 20,5 mm (D = 25), t = 2,25 mm, σ = fy = 340 MPa:
        # λp0 = 2,24; Ia = 0,334 cm⁴; Is = 0,162 cm⁴; n = 1/3; k = 2,93; λp = 0,856; bef = 6,60 cm; ds = 0,994 cm
        r = pff.mesa_enrijecida(0.076, 0.0205, 0.025, 0.00225, 340 * MPa, 340 * MPa, 200e3 * MPa)
        self.assertLess(rel(r["lambda_p0"], 2.24), 0.01)
        self.assertLess(rel(r["k"], 2.93), 0.01)
        self.assertLess(rel(r["bef"], 0.0660), 0.01)
        self.assertLess(rel(r["ds"], 0.00994), 0.01)


class TestExemploCBCA(unittest.TestCase):
    """[CBCA] Exemplo 4.12, Ue 300x85x25x3,00 com ri = t."""

    @classmethod
    def setUpClass(cls):
        cls.sec = ue_arredondado(0.300, 0.085, 0.025, 0.003, 0.003)

    def test_compressao(self):
        r = pff.compressao(self.sec, CBCA_MAT, 10.29, 2.5725, 2.5725)
        self.assertLess(rel(r["Nex"], 364.02), 0.01)
        self.assertLess(rel(r["Ney"], 399.95), 0.01)
        self.assertLess(rel(r["Ne"], 276.48), 0.01)
        self.assertLess(rel(r["Aef"], 11.78 * CM ** 2), 0.01)
        self.assertLess(rel(r["NcRd"], 136.59), 0.01)
        self.assertEqual(r["item"], "9.7.2")

    def test_distorcional_fora_da_tabela(self):
        # bf/bw = 0,283 < 0,4 (fora das tabelas 11 e 14) → não dispensa; dentro dos limites da expressão
        # simplificada do AISI (ho/t = 100, bo/t = 28,3, D/t = 8,3, ho/bo = 3,5, D/bo = 0,29) → calculada
        r = pff.flexao_x(self.sec, CBCA_MAT, 2.5725, Cb=1.45)
        self.assertFalse(r["distorcional"]["dispensada"])
        self.assertIn("MRd_dist", r["distorcional"])
        self.assertFalse(any("não verificada" in a for a in r["avisos"]))


class TestCompressaoUFAL(unittest.TestCase):
    def test_Ue250_MLE(self):
        sec = ue_arredondado(0.250, 0.085, 0.025, 0.002, 0.002)
        self.assertLess(rel(sec.A, 9.14 * CM ** 2), 0.01)
        mat = Material("fy300", 200e3 * MPa, 77e3 * MPa, fy=300 * MPa, fu=400 * MPa)
        r = pff.compressao(sec, mat, 2.40, 1.20, 1.20)
        self.assertLess(rel(r["chi"], 0.890), 0.01)
        self.assertLess(rel(r["Aef"], 5.64 * CM ** 2), 0.01)
        self.assertLess(rel(r["NcRd"], 125.5), 0.01)
        mesa = next(v for k, v in r["elementos"].items() if k.startswith("mesa"))
        self.assertLess(rel(mesa["bef"], 0.0691), 0.01)
        self.assertLess(rel(mesa["ds"], 0.0154), 0.01)


def ue100_silva():
    """[UFAL-F] Ue 100x50x17x1,20 com as propriedades da fonte."""
    cm4, A, Ix, Iy = CM ** 4, 2.71 * CM ** 2, 44.14 * CM ** 4, 10.12 * CM ** 4
    p = dict(tipo="Ue", h=0.100, b=0.050, D=0.017, t=0.0012, ri=0.0012, A=A, Ix=Ix, Iy=Iy, Wx=8.83 * CM ** 3,
             Wy=3.15 * CM ** 3, J=0.013 * cm4, Cw=246.61 * CM ** 6, x0=-4.28 * CM, y0=0.0, r0=6.19 * CM,
             rx=math.sqrt(Ix / A), ry=math.sqrt(Iy / A))
    return Secao("Ue 100x50x17x1,20", A, Ix, Iy, p["J"], p)


class TestFLT_UFAL(unittest.TestCase):
    mat = Material("fy250", 20000 * KNCM2, 7700 * KNCM2, fy=25 * KNCM2, fu=40 * KNCM2)

    def test_Cb_131(self):
        r = pff.flexao_x(ue100_silva(), self.mat, 4.00, Cb=1.31)
        self.assertLess(rel(r["Me"], 0.9299), 0.01)
        self.assertLess(rel(r["lambda0"], 1.541), 0.01)
        self.assertLess(rel(r["chi_FLT"], 0.421), 0.01)
        self.assertLess(rel(r["MRd_FLT"], 0.8453), 0.01)
        self.assertEqual(r["criterio"], "FLT")

    def test_Cb_1(self):
        self.assertLess(rel(pff.flexao_x(ue100_silva(), self.mat, 4.00)["MRd"], 0.6453), 0.01)

    def test_cortante_inelastico(self):
        r = pff.cortante(ue100_silva(), self.mat)
        self.assertEqual(r["regime"], "inelastico")
        self.assertLess(rel(r["h_t"], 79.33), 0.001)
        self.assertLess(rel(r["VRd"], 13.45), 0.01)


class TestFlexaoTBS(unittest.TestCase):
    def test_Wef(self):
        # [TBS] Ue 300x85x25x2,25, fy = 340 MPa, ri = t: Wef = 89,78 cm³; MRd = 89,78·34/1,1 = 2775 kN·cm
        sec = ue_arredondado(0.300, 0.085, 0.025, 0.00225, 0.00225)
        mat = Material("F35", 200e3 * MPa, 77e3 * MPa, fy=340 * MPa, fu=450 * MPa)
        r = pff.flexao_x(sec, mat, 0.0)
        self.assertLess(rel(r["Wef"], 89.78 * CM ** 3), 0.015)
        self.assertLess(rel(r["MRd_esc"], 89.78e-6 * 340e3 / 1.1), 0.015)


class TestCortante(unittest.TestCase):
    """9.8.3 (kv = 5): [JMM] dois casos no regime elástico; continuidade nas fronteiras."""

    mat = Material("fy350", 200e3 * MPa, 77e3 * MPa, fy=350 * MPa, fu=450 * MPa)

    def test_JMM(self):
        # Ue com ri = t: altura plana da alma = h − 4t → 250 − 9 = 241 mm; 375 − 15 = 360 mm
        for (h, b, D, t), V in (((0.250, 0.085, 0.025, 0.00225), 38.89), ((0.375, 0.100, 0.038, 0.00375), 120.52)):
            sec = s.secao_Ue(h, b, D, t)
            sec.props["ri"] = t
            r = pff.cortante(sec, self.mat)
            self.assertLess(rel(r["VRd"], V), 0.01)
            self.assertEqual(r["regime"], "elastico")

    def test_fronteiras(self):
        # em h/t = 1,08√(E kv/fy): 0,6·fy·h·t vs 0,65t²√(kv fy E) → 0,648 vs 0,65 (0,3 %);
        # em h/t = 1,40√(...): 0,65 vs 0,905/1,40 = 0,6464 (0,55 %)
        r0 = math.sqrt(200e3 * 5 / 350)
        t = 0.002
        for lim in (1.08, 1.40):
            v = []
            for f in (1 - 1e-7, 1 + 1e-7):
                sec = s.secao_U_pf(lim * r0 * f * t + 2 * t, 0.05, t)
                v.append(pff.cortante(sec, self.mat)["VRd"])
            self.assertLess(rel(*v), 0.006)


class TestDistorcional(unittest.TestCase):
    def test_dispensa_AFO(self):
        sec = s.secao_Ue(0.200, 0.100, 0.025, 0.003)
        d = pff.dispensa_distorcional(sec, "compressao")
        self.assertTrue(d["dispensada"])
        self.assertLess(abs(d["D_bw_min"] - 0.09), 0.006)   # [AFO] 0,09 (tabela 11 interpolada)

    def test_U_simples_nao_se_aplica(self):
        self.assertIsNone(pff.dispensa_distorcional(s.secao_U_pf(0.15, 0.05, 0.002), "flexao")["dispensada"])

    # LCA-003. σdist pela expressão simplificada do AISI para C sem restrição rotacional (AISI S100-2007 C4.2(a) e
    # C3.1.4(a); desde 2010 no Commentary do S100-16, eq. C-2.3.1.3-1/-4 e C-2.3.3.3-1/-4, limite inferior):
    # σ = kd·π²E/(12(1 − ν²))·(t/bo)², compressão kd = 0,1·(bo·D/(ho·t))^1,4 ∈ [0,05; 8]; flexão
    # kd = 0,6·(bo·D/(ho·t))^0,7 ∈ [0,5; 8]. χdist: 9.7.3 (0,561; 0,25; 1,2) e 9.8.2.3 (0,673; 0,22).
    MAT = Material("ZAR-345", 200e3 * MPa, 77e3 * MPa, fy=345 * MPa, fu=430 * MPa)

    def test_distorcional_aisi_Ue150x60x20x2(self):
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        x, base = 0.060 * 0.020 / (0.150 * 0.002), math.pi ** 2 * 200e6 / (12 * 0.91) * (0.002 / 0.060) ** 2
        # compressão: π²E/(12(1 − ν²))·(t/bo)² = 200,85 MPa; kd = 0,1·4^1,4 = 0,6964; σ = 139,9 MPa; λ = 1,570
        sd = 0.1 * x ** 1.4 * base
        lam = math.sqrt(345e3 / sd)
        chi = (1 - 0.25 / lam ** 1.2) / lam ** 1.2
        r = pff.distorcional(sec, self.MAT, "compressao")
        self.assertLess(rel(r["sigma_dist"], sd), 1e-9)
        self.assertLess(rel(r["sigma_dist"], 139.88e3), 0.001)
        self.assertLess(rel(r["NRd_dist"], chi * sec.A * 345e3 / 1.20), 1e-9)
        # flexão: kd = 0,6·4^0,7 = 1,5834; σ = 318,0 MPa; λ = 1,042 → χ = (1 − 0,22/λ)/λ
        sd = 0.6 * x ** 0.7 * base
        lam = math.sqrt(345e3 / sd)
        r = pff.distorcional(sec, self.MAT, "flexao")
        self.assertLess(rel(r["sigma_dist"], 318.02e3), 0.001)
        self.assertLess(rel(r["MRd_dist"], (1 - 0.22 / lam) / lam * sec.props["Wx"] * 345e3 / 1.10), 1e-9)
        # informado (análise de estabilidade elástica): Mdist = W·fy → λ = 1 → χ = 0,78
        r = pff.distorcional(sec, self.MAT, "flexao", sec.props["Wx"] * 345e3)
        self.assertAlmostEqual(r["chi_dist"], 0.78, places=12)

    def test_chi_dist_fronteiras(self):
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        N561 = sec.A * 345e3 / 0.561 ** 2          # λ = 0,561 → χ = 1
        self.assertAlmostEqual(pff.distorcional(sec, self.MAT, "compressao", N561)["chi_dist"], 1.0, places=9)
        self.assertAlmostEqual(pff.distorcional(sec, self.MAT, "compressao", sec.A * 345e3)["chi_dist"], 0.75)

    def test_verificar_distorcional_fora_dos_limites_reprova(self):
        # C10 (logica-cype-aco.md): Ue 150x100x10x1,5 → ho/bo = 1,5 < 2: fora da expressão simplificada e não
        # dispensada pela tabela 11 → sem Ndist informado, nao_verificado (nunca aprovar com aviso)
        ue = s.secao_Ue(0.150, 0.100, 0.010, 0.0015)
        r = pff.verificar_pff(ue, self.MAT, dict(N=-40.0), 1.0, 1.0, 1.0, 1.0)
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "nao_verificado")
        self.assertTrue(any("ho/bo" in a for a in r["avisos"]))
        # Ndist informado: 9.9 com Nc,Rd,dist = χdist·A·fy/1,20
        Nd = 20.0  # λ = 2,71 → χ = 0,279 → Nc,Rd,dist ≈ 34 kN < 40 kN
        lam = math.sqrt(ue.A * 345e3 / Nd)
        NRd = (1 - 0.25 / lam ** 1.2) / lam ** 1.2 * ue.A * 345e3 / 1.20
        r = pff.verificar_pff(ue, self.MAT, dict(N=-40.0), 1.0, 1.0, 1.0, 1.0, Ndist=Nd)
        self.assertLess(rel(r["detalhes"]["taxas"]["distorcional"], 40.0 / NRd), 1e-9)
        self.assertEqual(r["criterio"], "distorcional")
        self.assertFalse(r["ok"])

    def test_verificar_flexao_distorcional_na_interacao(self):
        # Ue 150x60x20x2: compressão dispensada (tabela 11), flexão não (tabela 14) → 9.9 com MRd,dist
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        r = pff.verificar_pff(sec, self.MAT, dict(N=-5.0, Mx=1.0), 3.0, 1.5, 1.5, 1.5)
        d = r["detalhes"]
        self.assertTrue(d["compressao"]["distorcional"]["dispensada"])
        MRd = min(d["flexao_x"]["MRd"], d["flexao_x"]["distorcional"]["MRd_dist"])
        self.assertLess(rel(d["taxas"]["distorcional"], 5.0 / d["compressao"]["NcRd"] + 1.0 / MRd), 1e-9)


class TestTorcaoPFF(unittest.TestCase):
    """LCA-004/005 no PFF: τ = T·t/J ≤ 0,6fy/γ (γ = 1,10, 9.8.3) e Von Mises quando T > 0,2·TRd."""

    def test_torcao_Ue(self):
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        mat = Material("ZAR-345", 200e3 * MPa, 77e3 * MPa, fy=345 * MPa, fu=430 * MPa)
        TRd = 0.6 * 345e3 * sec.J / 0.002 / 1.10
        r = pff.verificar_pff(sec, mat, dict(T=1.5 * TRd), 3.0, 1.5, 1.5, 1.5)
        self.assertEqual(r["criterio"], "torcao")
        self.assertLess(rel(r["taxa_max"], 1.5), 1e-9)
        self.assertFalse(r["ok"])
        r = pff.verificar_pff(sec, mat, dict(Mx=0.5, T=0.5 * TRd), 3.0, 1.5, 1.5, 1.5)
        self.assertIn("von_mises", r["detalhes"]["taxas"])

    def test_ct_maior_que_1(self):
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        with self.assertRaisesRegex(ValueError, "Ct"):
            pff.tracao(sec, Material("x", 200e6, 77e6, fy=345e3, fu=430e3), Ct=1.2)


class TestVerificar(unittest.TestCase):
    def setUp(self):
        self.sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        self.mat = Material("ZAR-345", 200e3 * MPa, 77e3 * MPa, fy=345 * MPa, fu=430 * MPa)

    def test_lista_pior_ponto(self):
        pts = [dict(N=-5.0, Mx=1.0), dict(N=-5.0, Mx=4.0, Vy=3.0), dict(N=-5.0, Mx=2.0)]
        r = pff.verificar_pff(self.sec, self.mat, pts, 3.0, 1.5, 1.5, 1.5)
        so = pff.verificar_pff(self.sec, self.mat, pts[1], 3.0, 1.5, 1.5, 1.5)
        self.assertEqual(r["indice"], 1)
        self.assertAlmostEqual(r["taxa_max"], so["taxa_max"])
        self.assertEqual(set(r), {"taxa_max", "criterio", "item", "ok", "indice", "detalhes", "avisos"})
        import json
        json.dumps(r["detalhes"], allow_nan=False)  # só tipos JSON

    def test_tracao_Ct(self):
        # 9.6.2: escoamento A·fy/1,10; ruptura fora da ligação An0·fu/1,35; na ligação Ct·An·fu/1,65
        A = self.sec.A
        r = pff.tracao(self.sec, self.mat, An=0.9 * A, Ct=0.75)
        self.assertAlmostEqual(r["Nt_escoamento"], A * 345e3 / 1.10)
        self.assertAlmostEqual(r["Nt_ruptura_ligacao"], 0.75 * 0.9 * A * 430e3 / 1.65)
        self.assertAlmostEqual(r["NtRd"], 0.75 * 0.9 * A * 430e3 / 1.65)
        v = pff.verificar_pff(self.sec, self.mat, dict(N=10.0), 3.0, 1.5, 1.5, 1.5, An=0.9 * A, Ct=0.75)
        self.assertAlmostEqual(v["taxa_max"], 10.0 / r["NtRd"])
        self.assertEqual(v["item"], "9.6.2")

    def test_edicao_cobertura(self):
        self.assertEqual(pff.EDICAO, "NBR 14762:2010")
        for c in pff.COBERTURA:
            self.assertEqual(set(c), {"norma", "edicao", "item", "verificacao", "limitacao"})
        self.assertTrue(any("distorcional" in c["limitacao"] for c in pff.COBERTURA))


class TestCatalogoPFF(unittest.TestCase):
    def test_linhas_montam(self):
        caminho = os.path.join(os.path.dirname(s.__file__), "dados", "perfis_pff.csv")
        with open(caminho, encoding="utf-8") as f:
            texto = f.read().splitlines()
        self.assertTrue(texto[0].startswith("#"))
        linhas = list(csv.DictReader(l for l in texto if not l.startswith("#")))
        self.assertGreater(len(linhas), 50)
        nomes = set()
        for lin in linhas:
            h, b, D, t = (float(lin[k]) / 1000 for k in ("h", "b", "D", "t"))
            sec = s.secao_Ue(h, b, D, t) if lin["tipo"] == "Ue" else s.secao_U_pf(h, b, t)
            self.assertGreater(sec.A, 0, lin["nome"])
            esperado = (f"Ue {lin['h']}x{lin['b']}x{lin['D']}x{lin['t']}" if lin["tipo"] == "Ue"
                        else f"U {lin['h']}x{lin['b']}x{lin['t']}")
            self.assertEqual(lin["nome"], esperado.replace(".", ","))
            nomes.add(lin["nome"])
        self.assertEqual(len(nomes), len(linhas))


class TestDistorcionalFlexaoY(unittest.TestCase):
    """9.8.2.3 em torno de y (Ue com enrijecedores comprimidos). Sem tabela de dispensa nem expressão própria
    para o eixo de menor inércia: usa a tabela 11 e o σdist da compressão (AISI S100-2007 C4.2(a)), limite
    inferior porque na flexão em y a mesa tem gradiente (máximo na borda enrijecida) e a alma fica tracionada
    (mais restrição à rotação). Mdist,y = Wy,c·σdist; χdist pela 9.8.2.3 (0,673 / 0,22).
    Ue 200x75x20x2,00 (ri = 0), fy = 250 MPa, E = 200 GPa: bf/bw = 0,375 < 0,4 (fora da tabela 11);
    x = bo·D/(ho·t) = 75·20/(200·2) = 3,75; kd = 0,1·3,75^1,4 = 0,6363;
    σdist = 0,6363·π²·200000/(12·0,91)·(2/75)² = 0,6363·180762·7,111e-4 = 81,79 MPa;
    λdist = √(250/81,79) = 1,7484; χdist = (1 − 0,22/1,7484)/1,7484 = 0,5000."""

    mat = Material("fy250", 200e3 * MPa, 77e3 * MPa, fy=250 * MPa, fu=400 * MPa)

    def test_aisi_compressao_conservador(self):
        sec = s.secao_Ue(0.200, 0.075, 0.020, 0.002)
        p = sec.props
        Wyc = p["Iy"] / (p["b"] - p["t"] / 2 - p["xg"])   # fibra extrema do lado dos enrijecedores
        d = pff.flexao_y(sec, self.mat)["distorcional"]
        self.assertLess(rel(d["sigma_dist"], 81.79 * MPa), 0.001)
        self.assertLess(rel(d["chi_dist"], 0.5000), 0.001)
        self.assertLess(rel(d["MRd_dist"], d["chi_dist"] * Wyc * 250 * MPa / 1.10), 1e-9)
        My = 0.5 * d["MRd_dist"]
        r = pff.verificar_pff(sec, self.mat, dict(My=My), 1, 1, 1, 0.0)
        MyRd = r["detalhes"]["flexao_y"]["MRd"]
        self.assertLess(rel(r["detalhes"]["taxas"]["distorcional"], My / min(MyRd, d["MRd_dist"])), 1e-9)
        self.assertFalse(any("distorcional não verificada" in a for a in r["avisos"]))
        # Mdist,y informado = Wy,c·fy → λ = 1, χ = 0,78
        d = pff.flexao_y(sec, self.mat, Mdist=Wyc * 250 * MPa)["distorcional"]
        self.assertLess(rel(d["chi_dist"], 0.78), 1e-9)
        r = pff.verificar_pff(sec, self.mat, dict(My=My), 1, 1, 1, 0.0, Mdist_y=Wyc * 250 * MPa)
        self.assertLess(rel(r["detalhes"]["flexao_y"]["distorcional"]["chi_dist"], 0.78), 1e-9)

    def test_dispensa_e_nao_calculada(self):
        # Ue 150x60x20x2: bf/bw = 0,4; bw/t = 75 → D/bw,mín = 0,06 (tabela 11) ≤ 0,133 → dispensada
        sec = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
        r = pff.verificar_pff(sec, self.mat, dict(My=0.5), 1, 1, 1, 0.0)
        self.assertTrue(r["detalhes"]["flexao_y"]["distorcional"]["dispensada"])
        self.assertNotIn("distorcional", r["detalhes"]["taxas"])
        # Ue 150x100x10x1,5: tabela 11 não dispensa (D/bw = 0,067 < 0,073) e ho/bo = 1,5 < 2 (fora do AISI)
        sec = s.secao_Ue(0.150, 0.100, 0.010, 0.0015)
        r = pff.verificar_pff(sec, self.mat, dict(My=0.1), 1, 1, 1, 0.0)
        self.assertFalse(r["ok"])
        self.assertEqual(r["criterio"], "nao_verificado")


if __name__ == "__main__":
    unittest.main()
