"""Regressão — perfis formados a frio NBR 14762:2010 e catálogo NBR 6355 (verificador-calculos, ficha 013).

Esperados re-derivados à mão por um método da largura efetiva independente
(verificacao/memorias/pff-mle.py, só stdlib) ou tirados das fontes. Relatório: verificacao/2026-10-20-pff.md.

Fontes:
[UFAL-C] L. B. Santos, "PFF — compressão axial", notas de aula UFAL, eduCAPES 572684 (Ue 250x85x25x2,00).
[CBCA]   Silva, Pierin & Silva, manual CBCA 2014, Anexo D, ex. "4.12 S13 longarina" (Ue 300x85x25x3,00):
         DimPerfil: A = 15,0 cm², Ix = 1952,67 cm⁴, Iy = 134,08 cm⁴, x0 = 5,495 cm; Nc,Rd = 136,59 kN.
[TBS]    T. B. Soares, monografia UFMG 2018, item 4.5.1.1 (Ue 300x85x25x2,25, Wef = 89,78 cm³).
[N6355]  ABNT NBR 6355:2012, anexo A (ri = t para t ≤ 6,3 mm; áreas tabeladas) — cópia não oficial
         consultada em 2026-10-20 (pdfcoffee.com); áreas conferidas pela linha média com ri = t.
[T11]    Tabelas 11 e 14 da NBR 14762:2010: [UFAL-C] slide 58, [UFAL-F] slide 36 (eduCAPES 572685),
         V. P. Silva (Instituto de Engenharia, 2010) slides 26–27, NBR 14762:2001 tabelas D.1/D.2.
[MEM]    memória independente pff-mle.py (valores com 4 algarismos).
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from prossen_estrutural import secoes as s  # noqa: E402
from prossen_estrutural.modelo import Material, MPa, Secao  # noqa: E402
from prossen_estrutural.aco import nbr14762 as p  # noqa: E402

CM = 0.01


def rel(a, b):
    return abs(a - b) / abs(b)


def mat(fy, fu=None):
    return Material(f"fy{fy}", 200e3 * MPa, 77e3 * MPa, fy=fy * MPa, fu=(fu or 1.3 * fy) * MPa)


def ue_ind(h, b, D, t, A, Ix, Iy, J, Cw, x0, Wx, Wy):
    """Secao Ue com propriedades brutas da memória [MEM] (ri = t, cantos em arco; cm → m)."""
    A, Ix, Iy = A * CM ** 2, Ix * CM ** 4, Iy * CM ** 4
    pr = dict(tipo="Ue", h=h / 1e3, b=b / 1e3, D=D / 1e3, t=t / 1e3, ri=t / 1e3, A=A, Ix=Ix, Iy=Iy,
              J=J * CM ** 4, Cw=Cw * CM ** 6, x0=-x0 * CM, y0=0.0, Wx=Wx * CM ** 3, Wy=Wy * CM ** 3,
              rx=math.sqrt(Ix / A), ry=math.sqrt(Iy / A))
    pr["r0"] = math.sqrt(pr["rx"] ** 2 + pr["ry"] ** 2 + pr["x0"] ** 2)
    return Secao(f"Ue {h}x{b}x{D}x{t}", A, Ix, Iy, pr["J"], pr)


UE250 = (250, 85, 25, 2.0, 9.1369, 869.6, 89.04, 0.1218, 11186.1, 6.0823, 69.569, 14.6767)
UE300_3 = (300, 85, 25, 3.0, 15.0081, 1952.6, 134.01, 0.4502, 23474.3, 5.5120, 130.173, 21.2746)
UE300_225 = (300, 85, 25, 2.25, 11.3670, 1493.1, 104.38, 0.1918, 18431.0, 5.6026, 99.541, 16.5754)
UE150 = (150, 60, 20, 2.0, 5.9369, 207.2, 30.04, 0.0792, 1446.6, 4.6590, 27.627, 7.3716)


class TestRegLarguraEfetiva(unittest.TestCase):
    """O núcleo do MLE do código, alimentado com propriedades brutas corretas, reproduz livro e memória."""

    def test_UFAL_compressao(self):
        # [UFAL-C]: KxLx = 2,40, KyLy = KzLz = 1,20 m, fy = 300: χ = 0,890; Aef = 5,64 cm²; Nc,Rd = 125,5 kN
        # [MEM]: χ = 0,8880; Aef = 5,644 cm²; Nc,Rd = 125,30 kN
        r = p.compressao(ue_ind(*UE250), mat(300), 2.40, 1.20, 1.20)
        self.assertLess(rel(r["Aef"], 5.644 * CM ** 2), 0.005)
        self.assertLess(rel(r["NcRd"], 125.30), 0.005)

    def test_CBCA_compressao(self):
        # [CBCA]: Nexz = 276,48; Aef = 11,781 cm²; Nc,Rd = 136,59 kN (fy = 240, Lx = 10,29, Ly = Lz = 2,5725 m)
        r = p.compressao(ue_ind(*UE300_3), mat(240), 10.29, 2.5725, 2.5725)
        self.assertLess(rel(r["Ne"], 276.48), 0.005)
        self.assertLess(rel(r["NcRd"], 136.59), 0.005)

    def test_CBCA_flexao_totalmente_efetiva(self):
        # [MEM] mesa: λp0 = 1,353, Is/Ia ≥ 1, k = 3,54, λp = 0,47 → efetiva; alma ψ ≈ −1, k = 24, λp = 0,714,
        # bef,1 + bef,2 = 209 mm > bc = 144 mm → efetiva ⇒ Wef = W = 130,17 cm³, MRd = 130,17·24/1,1 = 2840 kN·cm.
        # O manual (DimPerfil) dá Wx,ef = 116,94 cm³ sem mostrar o cálculo: não reproduzível à mão (ver relatório).
        r = p.flexao_x(ue_ind(*UE300_3), mat(240), 0.0)
        self.assertLess(rel(r["MRd_esc"], 28.401), 0.005)

    def test_TBS_Wef(self):
        # [TBS]: Wef = 89,78 cm³ (fy = 340) — [MEM] 89,78
        r = p.flexao_x(ue_ind(*UE300_225), mat(340), 0.0)
        self.assertLess(rel(r["Wef"], 89.78 * CM ** 3), 0.005)

    def test_UFAL_flexao_U125_iterativo(self):
        # [UFAL-F] L. B. Santos, "PFF — flexão simples", eduCAPES 572685, slides 61–72: U 125x50x1,20, ri = t,
        # fy = 38 kN/cm², A = 2,65 cm², Ix = 63,82 cm⁴ (Wx = 10,21 cm³); MLE iterativo: mesa bef = 1,58 cm,
        # alma bef,1 = 2,373 e bef,2 = 4,368 cm, LN a 7,392 cm da fibra comprimida → Wef = 6,18 cm³ (0,61·Wx)
        sec = s.secao_U_pf(0.125, 0.050, 0.0012)
        sec.props.update(ri=0.0012, A=2.65e-4, Ix=63.82e-8, Wx=63.82e-8 / 0.0625)
        sec = Secao("U 125x50x1,20", 2.65e-4, 63.82e-8, sec.Iz, sec.J, sec.props)
        self.assertLess(rel(p.flexao_x(sec, mat(380), 0.0)["Wef"], 6.18 * CM ** 3), 0.005)

    def test_interacao_independente(self):
        # substitui o teste auto-referente test_pff.TestVerificar.test_interacao_linear_UFAL:
        # Ue 150x60x20x2 (ri = t), fy = 345, Lx = 3, Ly = Lz = Lb = 1,5 m; [MEM] Nc,Rd = 88,553 kN,
        # MRd,FLT = 8,1882 kN·m → 9.9: 5/88,553 + 1/8,1882 = 0,1786
        r = p.verificar_pff(ue_ind(*UE150), mat(345, 430), dict(N=-5.0, Mx=1.0), 3.0, 1.5, 1.5, 1.5)
        self.assertLess(rel(r["detalhes"]["taxas"]["interacao"], 0.1786), 0.005)

    def test_cortante_tres_ramos(self):
        # 9.8.3 (kv = 5, h = h_nom − 4t): h/t = 107,1 > 1,40√(Ekv/fy) → 0,905E·kv·t³/h/1,1 = 38,885 kN;
        # h/t = 71,0 (fy 345) inelástico → 0,65t²√(kv·fy·E)/1,1 = 43,903; h/t = 96 (fy 250) → 77,131 kN
        for (h, t, fy), V in (((0.250, 0.00225, 350), 38.885), ((0.150, 0.002, 345), 43.903),
                              ((0.300, 0.003, 250), 77.131)):
            sec = s.secao_Ue(h, 0.085, 0.025, t)
            sec.props["ri"] = t
            self.assertLess(rel(p.cortante(sec, mat(fy))["VRd"], V), 0.002)


class TestRegTabelas(unittest.TestCase):
    def test_gamas(self):
        # 9.6.2 (γ 1,10 / 1,35 / 1,65), 9.7.2 (1,20), 9.8 (1,10): [UFAL-T] slide 5 e NBR 14762:2010
        self.assertEqual((p.G_ESC, p.G_RUP0, p.G_RUP, p.G_COMP, p.G_FLEX), (1.10, 1.35, 1.65, 1.20, 1.10))

    def test_tabelas_11_e_14(self):
        # [T11] células (bf/bw, bw/t) → D/bw mínimo. A célula (1,4; 250) da tabela 11 é 0,06 em duas fontes
        # e 0,07 na UFAL: 0,07 é o valor mais exigente (a favor da segurança).
        casos = (("compressao", 1.0, 100, 0.12), ("compressao", 0.4, 250, 0.02), ("compressao", 0.8, 50, 0.22),
                 ("compressao", 1.4, 250, 0.07), ("compressao", 1.2, 200, 0.07), ("compressao", 2.0, 125, 0.12),
                 ("flexao", 0.6, 50, 0.25), ("flexao", 1.0, 100, 0.11), ("flexao", 1.8, 50, 0.19),
                 ("flexao", 1.4, 100, 0.10), ("flexao", 0.8, 125, 0.09), ("flexao", 2.0, 250, 0.05))
        for caso, eta, bwt, esp in casos:
            t = 0.001
            sec = s.secao_Ue(bwt * t, eta * bwt * t, 0.3 * bwt * t, t)
            self.assertAlmostEqual(p.dispensa_distorcional(sec, caso)["D_bw_min"], esp, places=9,
                                   msg=(caso, eta, bwt))


class TestRegAchadosPFF(unittest.TestCase):
    def test_VC_pff_001_areas_NBR6355(self):
        # [N6355] áreas (cm²) com ri = t; o catálogo monta cantos vivos (+1,1 a +1,7 %)
        tab = {"Ue 150x60x20x2,65": 7.75, "Ue 200x75x25x3,00": 11.41, "Ue 250x85x25x3,00": 13.51,
               "Ue 300x85x25x3,00": 15.01, "U 100x50x2,00": 3.87, "U 150x50x3,00": 7.20, "U 50x25x2,00": 1.87}
        for nome, A in tab.items():
            self.assertLess(rel(s.catalogo(nome).A, A * CM ** 2), 0.005, nome)

    def test_VC_pff_001_resistencias_catalogo(self):
        # caminho real (catalogo → verificar): Ue 150x60x20x2,00, fy = 345, KL = 3/1,5/1,5 m.
        # [MEM] com a geometria NBR 6355 (ri = t): Ne = 176,33 kN; MRd,esc = 8,511 kN·m.
        # Código (cantos vivos, ri = 0): 180,75 (+2,5 %) e 8,715 (+2,4 %) — contra a segurança.
        sec = s.catalogo("Ue 150x60x20x2,00")
        self.assertLess(rel(p.compressao(sec, mat(345), 3.0, 1.5, 1.5)["Ne"], 176.33), 0.01)
        self.assertLess(rel(p.flexao_x(sec, mat(345), 0.0)["MRd_esc"], 8.511), 0.01)

    def test_VC_pff_003_borda_da_tabela(self):
        # Ue 100x40x17x2,00: bf/bw = 40/100 = 0,4 (primeira linha da tabela 11) e bw/t = 50 → D/bw,min = 0,08
        # [T11]. Em ponto flutuante 0,04/0,1 = 0,39999999999999997 < 0,4 → "fora da faixa" (aviso indevido).
        d = p.dispensa_distorcional(s.catalogo("Ue 100x40x17x2,00"), "compressao")
        self.assertIsNotNone(d["D_bw_min"])
        self.assertAlmostEqual(d["D_bw_min"], 0.08, places=9)


if __name__ == "__main__":
    unittest.main()
