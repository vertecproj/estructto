"""Regressão — concreto armado (NBR 6118:2023) e materiais. Verificação independente (ficha 014).

Valores esperados vêm da memória independente `verificacao/memorias/concreto-memoria.py` e
`concreto-fibras.py` (fórmulas da norma, só stdlib, sem importar o código verificado), de tabelas
da norma ou de solução fechada escrita no próprio teste. Nenhum esperado foi copiado do código.
Achados VC-concreto-NNN: relatório `verificacao/2026-10-20-concreto.md`. Teste marcado
@unittest.expectedFailure = falha hoje; o dono remove o decorador ao corrigir.
"""
import importlib.util
import math
import os
import sys
import unittest

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, RAIZ)
from prossen_estrutural.concreto import nbr6118 as c  # noqa: E402
from prossen_estrutural import materiais as m, secoes as s, solver as sv  # noqa: E402
from prossen_estrutural.modelo import (Modelo, No, Barra, CasoCarga, CargaNodal,  # noqa: E402
                                       CargaDistribuida, MPa)


def _carrega(nome):
    p = os.path.join(RAIZ, "verificacao", "memorias", nome)
    sp = importlib.util.spec_from_file_location(nome.replace("-", "_")[:-3], p)
    mod = importlib.util.module_from_spec(sp)
    sp.loader.exec_module(mod)
    return mod


MEM = _carrega("concreto-memoria.py")
FIB = _carrega("concreto-fibras.py")
SEM_ELS = {"qp": None, "freq": None}
TOL = 0.02


def _vb(qd, L, n=13, N=0.0, Vy=0.0, T=0.0, Mz=0.0):
    """Viga biapoiada, carga uniforme qd, convenção do solver (My < 0 no vão)."""
    xs = [L * i / (n - 1) for i in range(n)]
    return [(x, N, Vy, qd * (L / 2 - x), T, -qd * x * (L - x) / 2, Mz) for x in xs]


def _ver(sec, fck, linhas, papel, L, params=None, els=None):
    return c.verificar_barra_concreto(sec, m.concreto(fck), [{"combinacao": "C", "linhas": linhas}],
                                      els or SEM_ELS, L, papel, params or {})


def _crit(r, nome):
    return next(v for v in r["detalhes"]["verificacoes"] if v["criterio"] == nome)


# ===================================================================== materiais (8.2, 8.3)
class TestMateriais(unittest.TestCase):
    def test_fctm_eci_ecs_todas_as_classes(self):
        """8.2.5 e 8.2.8 (2023) — Bastos, Fundamentos do CA (UNESP set/2023) Eq. 2.5, 2.8, 2.11–2.13."""
        for fck in range(20, 95, 5):
            p = m.props_concreto(fck)
            self.assertAlmostEqual(p["fctm"], MEM.fctm(fck), delta=1e-9, msg=fck)
            self.assertAlmostEqual(p["Eci"], MEM.eci(fck), delta=1e-6, msg=fck)
            self.assertAlmostEqual(p["Ecs"], MEM.ecs(fck), delta=1e-6, msg=fck)
            self.assertAlmostEqual(p["fctk_inf"], 0.7 * MEM.fctm(fck), delta=1e-9)
            self.assertAlmostEqual(p["fctk_sup"], 1.3 * MEM.fctm(fck), delta=1e-9)

    def test_tabela_8_1_arredondada(self):
        """Tabela 8.1 da NBR 6118:2014 (Eci, Ecs em GPa, arredondados; removida em 2023 por ser
        redundante com as fórmulas — TCC UFU §2.6)."""
        for fck, (Ei, Es_) in MEM.TAB_8_1.items():
            p = m.props_concreto(fck)
            self.assertLessEqual(abs(p["Eci"] / 1000 - Ei), 0.5 + 1e-9, msg=fck)
            self.assertLessEqual(abs(p["Ecs"] / 1000 - Es_), 0.5 + 1e-9, msg=fck)

    def test_material_concreto(self):
        """E = Ecs (8.2.8), G = Ecs/2,4 (8.2.9, ν = 0,2), peso 25 kN/m³ (8.2.2), kN/m²."""
        mat = m.concreto(30)
        self.assertAlmostEqual(mat.E, MEM.ecs(30) * MPa, delta=1e-3)
        self.assertAlmostEqual(mat.G, MEM.ecs(30) / 2.4 * MPa, delta=1e-3)
        self.assertEqual(mat.peso, 25.0)
        self.assertEqual(mat.fck, 30 * MPa)
        # material_de_spec (ESPEC §6.3): calcário αE = 0,9 → Ecs = αi·0,9·5600√30
        ms = m.material_de_spec({"concreto": 30, "alfa_E": 0.9})
        self.assertAlmostEqual(ms.E, MEM.ecs(30, 0.9) * MPa, delta=1e-3)
        self.assertEqual(m.material_de_spec({"armadura": "CA-50"}).fy, 500 * MPa)

    def test_acos_astm_nbr7007(self):
        """ASTM (ksi → MPa, valores NBR 8800 Tabela A.1 arredondados): A572 Gr.42 42/60 ksi,
        A992 50/65, A588 50/70, A242 50/70 (t ≤ 19 mm), A500 Gr.B redondo 42/58.
        NBR 7007 (catálogo ArcelorMittal): MR-250 250/400, AR-350 350/450, AR-350-COR 350/485, AR-415 415/520."""
        ref = {"A-572 GR.42": (290, 415), "A-992": (345, 450), "A-588": (345, 485), "A-242 T<=19": (345, 485),
               "A-500 GR.B": (290, 400), "MR-250": (250, 400), "AR-350": (350, 450),
               "AR-350-COR": (350, 485), "AR-415": (415, 520), "A-36": (250, 400)}
        for nome, (fy, fu) in ref.items():
            a = m.aco(nome)
            self.assertEqual((a.fy, a.fu), (fy * MPa, fu * MPa), msg=nome)

    def test_parametros_bloco(self):
        """17.2.2 e 8.2.10.1 (2023): λ, αc, ηc, εc2, εcu, n, x/d lim para todas as classes."""
        for fck in range(20, 95, 5):
            p, e = c.parametros(fck), MEM.bloco(fck)
            for k_cod, k_mem in (("lam", "lam"), ("alfa_c", "ac"), ("eta_c", "eta"), ("ec2", "ec2"),
                                 ("ecu", "ecu"), ("n", "n"), ("xd_lim", "xd")):
                self.assertAlmostEqual(p[k_cod], e[k_mem], delta=1e-12, msg=f"C{fck} {k_cod}")
            self.assertAlmostEqual(p["fcd"], fck / 14, delta=1e-12)

    def test_rho_min_tabela_17_3(self):
        for fck, rho in MEM.RHO_MIN.items():
            self.assertAlmostEqual(c.rho_min(fck), rho / 100, delta=1e-12, msg=fck)


# ===================================================================== viga
class TestViga(unittest.TestCase):
    def test_flexao_C70_eta_c(self):
        """C70: σcd = αc·ηc·fcd = 0,765·0,8298·5,0 kN/cm²; λ = 0,75 (solução fechada)."""
        x, As = MEM.flexao(20, 47, 14000, 70)
        r = c.flexao_simples(0.20, 0.47, 140, 70, 500)
        self.assertAlmostEqual(r["x"] * 100, x, delta=1e-6)
        self.assertAlmostEqual(r["As"], As, delta=1e-6)
        # Mlim com x/d = 0,35 (14.6.4.3, fck > 50)
        lx = 0.75 * 0.35 * 47
        Mlim = 0.765 * (40 / 70) ** (1 / 3) * 5.0 * 20 * lx * (47 - lx / 2) / 100
        self.assertAlmostEqual(r["Mlim"], Mlim, delta=Mlim * 1e-9)

    def test_as_min_e_as_max(self):
        """17.3.5.2.1: As,mín = 0,15%·bw·h (C25) = 1,5 cm²; 17.3.5.2.4: As,máx = 4% Ac = 40 cm²."""
        r = _ver(s.secao_retangular(0.20, 0.50), 25, _vb(1.0, 5.0), "viga", 5.0)
        self.assertAlmostEqual(r["armadura"]["As_cm2"], 1.5, delta=1e-9)
        self.assertAlmostEqual(r["detalhes"]["As_max_cm2"], 40.0, delta=1e-9)
        # estribo mínimo 17.4.1.1.1: 0,2·fctm/fywk·bw = 0,2·2,565/500·20 cm → cm²/m
        self.assertAlmostEqual(r["armadura"]["Asw_s_cm2_m"], 0.2 * MEM.fctm(25) / 500 * 20 * 100, delta=1e-9)

    def test_cisalhamento_ca60_fywd_435(self):
        """17.4.2.2: fywd ≤ 435 MPa; ρsw,mín com fywk real (17.4.1.1.1). Bastos (força cortante,
        UNESP/2023) ex. 1 com CA-60 nos estribos."""
        VRd2, Vc, Asw, Amin = MEM.cis_I(14, 46, 153, 25, fywk=600)
        r = c.cisalhamento(0.14, 0.46, 153, 25, fywk=600)
        self.assertAlmostEqual(r["VRd2"], VRd2, delta=1e-9)
        self.assertAlmostEqual(r["Vc"], Vc, delta=1e-9)
        self.assertAlmostEqual(r["Asw_s_calc"], Asw, delta=1e-9)
        self.assertAlmostEqual(r["Asw_s_min"], Amin, delta=1e-9)

    def test_cisalhamento_modelo_II_30(self):
        """17.4.2.3 com θ = 30°: VRd2 = 0,54αv2fcd·bw·d·sen²θ·cotθ; Vc1 interpolado; Asw/s com cotθ."""
        VRd2, Vc1, Asw = MEM.cis_II(20, 45, 150, 20, 30)
        r = c.cisalhamento(0.20, 0.45, 150, 20, modelo=2, theta=30)
        self.assertAlmostEqual(r["VRd2"], VRd2, delta=1e-9)
        self.assertAlmostEqual(r["Vc"], Vc1, delta=1e-9)
        self.assertAlmostEqual(r["Asw_s_calc"], Asw, delta=1e-9)

    def test_xi(self):
        """17.3.2.1.2 (Tabela 17.2): ξ(t) = 0,68·0,996^t·t^0,32 (t ≤ 70 meses); 2 acima."""
        for t in (0.5, 1, 6, 70, 71, 120):
            self.assertAlmostEqual(c.xi(t), MEM.xi(t), delta=1e-12, msg=t)

    def test_wk_ca50_e_ca60_eta1(self):
        """17.3.3.2 com σs no estádio II (αe = 15) e Acri de CADA barra (retângulo ≤ 7,5φ do eixo, limitado
        pelas faces e pela meia distância às vizinhas); vale a pior barra (maior Acri). η1 = 2,25 (CA-50) e
        1,0 (CA-60) — NBR 6118:2023 Tabela 8.2 (Bastos 2023 Tabela 2.3; TCC UFU §2.9).
        CA-50: 2φ20 numa camada, c_lat = 30 + 5 + 10 = 45 mm → barras a 4,5 e 15,5 cm; Acri = (4,5 + 5,5)·(4 + 15)
        = 190 cm² (conferido à mão: ρ = 3,1416/190, σs = 318,8 MPa → w2 = 0,3098 mm).
        CA-60: 7φ10 em 2 camadas, 4 na de baixo (só ela entra: o código ignora o limite pela camada de cima,
        a favor da segurança — ponytail documentado): c_lat = 40 mm, s = 40 mm, canto Acri = (4 + 2)·(4 + 7,5) = 69 cm²."""
        q = 80 * 8 / 36
        for fyk, eta1, n_cam, phi_esp in ((500, 2.25, 2, 20.0), (600, 1.0, 4, 10.0)):
            els = {"qp": None, "freq": {"combinacao": "F", "linhas": _vb(q, 6.0)}}
            r = _ver(s.secao_retangular(0.20, 0.50), 25, _vb(1.4 * q, 6.0), "viga", 6.0,
                     {"d_m": 0.46, "fyk_MPa": fyk}, els)
            As, w = r["armadura"]["As_cm2"], _crit(r, "fissuracao_wk")
            phi, b, d, h, ae = w["phi_mm"], 20, 46, 50, 15
            self.assertEqual(phi, phi_esp, msg=fyk)
            x = (-ae * As + math.sqrt((ae * As) ** 2 + 2 * b * ae * As * d)) / b
            I2 = b * x ** 3 / 3 + ae * As * (d - x) ** 2
            ss = ae * 8000 * (d - x) / I2 * 10
            c_lat = (30 + 5 + phi / 2) / 10  # cm: cnom (CAA II) + estribo 5 mm + φ/2
            a1 = math.pi * (phi / 10) ** 2 / 4
            acri = MEM.acri_barras(b, h, d, n_cam, phi / 10, c_lat)
            if fyk == 500:
                self.assertAlmostEqual(max(acri), 190.0, delta=1e-9)
            else:
                self.assertAlmostEqual(max(acri), 69.0, delta=1e-9)
            esp = max(MEM.wk(phi, ss, a1 / a, 25, eta1) for a in acri)
            self.assertAlmostEqual(w["solicitante"], esp, delta=esp * 1e-6, msg=fyk)

    def test_combinacao_determinante(self):
        """A pior combinação (B, qd = 40) define armadura, combinação e ponto (meio do vão)."""
        sec, mat = s.secao_retangular(0.20, 0.50), m.concreto(25)
        elu = [{"combinacao": n, "linhas": _vb(q, 5.0)} for n, q in (("A", 20), ("B", 40), ("C", 30))]
        r = c.verificar_barra_concreto(sec, mat, elu, SEM_ELS, 5.0, "viga")
        _, As = MEM.flexao(20, 45.5, 40 * 25 / 8 * 100, 25)
        self.assertAlmostEqual(r["armadura"]["As_cm2"], As, delta=As * 1e-9)
        self.assertEqual(_crit(r, "x_d")["combinacao"], "B")
        self.assertEqual(_crit(r, "x_d")["indice"], 6)

    def test_flecha_alfa_f_com_rho_linha(self):
        """17.3.2.1.2: αf = [ξ(∞) − ξ(1)]/(1 + 50ρ'), ρ' = As'/(b·d) = 2/(20·46)."""
        r = c.flecha_viga(0.20, 0.50, 0.46, 8.0, 80, 6.0, 17.8, 25, As2=2.0, d2=0.04)
        self.assertAlmostEqual(r["alfa_f"], (2 - MEM.xi(1)) / (1 + 50 * 2.0 / (20 * 46)), delta=1e-12)
        self.assertAlmostEqual(r["f_total"], r["f_imediata"] * (1 + r["alfa_f"]), delta=1e-9)

    def test_torcao_bastos(self):
        """17.5.1.5/17.5.1.6 re-derivado (Bastos, Torção, UNESP mai/2024, ex. 1): 35×50, C25, he = 10,
        θ = 38°, Td = 68,08 kN·m → TRd2 = 77,97 kN·m, A90/s = 6,12 cm²/m, Asl/ue = 10,02 cm²/m."""
        TRd2, A90, Asl, ue = MEM.torcao(35, 50, 6808, 25, 10, 38)
        r = c.torcao(0.35, 0.50, 68.08, 25, c1=0.04125, theta=38, he=0.10)
        self.assertAlmostEqual(r["TRd2"], TRd2 / 100, delta=1e-9)
        self.assertAlmostEqual(r["A90_s_calc"], A90, delta=1e-9)
        self.assertAlmostEqual(r["Asl_ue_calc"], Asl, delta=1e-9)
        self.assertAlmostEqual(r["ue_m"], ue / 100, delta=1e-12)

    def test_secao_nao_retangular(self):
        with self.assertRaises(ValueError):
            _ver(s.secao_I(0.3, 0.15, 0.006, 0.01), 25, _vb(10, 5.0), "viga", 5.0)


# ===================================================================== pilar
class TestPilar(unittest.TestCase):
    def test_bastos_13_1_1_integracao(self):
        """Bastos, Pilares (UNESP jun/2024) ex. 13.1.1: 20×50, C30, d' = 4, Nd = 1400, Md,tot = 53,19.
        As exato por integração independente (concreto-fibras.py, 4 barras de canto) = 10,38 cm²
        (o ábaco do livro dá 10,84: a diferença de 4,3% é leitura de ábaco — PEDIDOS-onda1)."""
        linhas = [(x, -1400.0, 0.0, 0.0, 0.0, 0.0, 0.0) for x in (0.0, 1.4, 2.8)]
        r = _ver(s.secao_retangular(0.50, 0.20), 30, linhas, "pilar", 2.8, {"caa": "I", "cobrimento_m": 0.025})
        As = r["armadura"]["As_pilar_cm2"]
        fcd, fyd = 30 / 14, 500 / 11.5
        Md = MEM.pilar_dir(1400, 0, 0.20, 2.8, 0.50, 30)["Mtot"] * 100
        bars = lambda A: [(4.0, A / 2), (16.0, A / 2)]  # noqa: E731
        self.assertGreaterEqual(FIB.MRd(1400, 50, 20, bars(As * 1.01), fcd, fyd), Md)
        self.assertLess(FIB.MRd(1400, 50, 20, bars(As * 0.99), fcd, fyd), Md)

    def test_lambda1_alfa_b_e_limites(self):
        """15.8.2: λ1 = (25 + 12,5·e1/h)/αb, 35 ≤ λ1 ≤ 90; 15.8.3.3.2: Md,tot = αb·M1d,A + Nd·le²/10·1/r."""
        dx = c.dimensionar_pilar(0.50, 0.20, 1000, 40, 0, 3.0, 25)["direcoes"]["x"]
        e = MEM.pilar_dir(1000, 40, 0.20, 3.0, 0.50, 25)
        self.assertAlmostEqual(dx["lam1"], 35.0)
        self.assertAlmostEqual(dx["Md_tot"], e["Mtot"], delta=1e-9)
        dx = c.dimensionar_pilar(0.50, 0.20, 1000, 40, 0, 5.0, 25, alfa_b=0.4)["direcoes"]["x"]
        self.assertAlmostEqual(dx["lam1"], (25 + 12.5 * 0.2) / 0.4, delta=1e-9)
        r1 = 0.005 / (0.20 * (1000 / (1000 * 25 / 14) + 0.5))
        self.assertAlmostEqual(dx["Md_tot"], 0.4 * 40 + 1000 * 25 / 10 * r1, delta=1e-9)
        dx = c.dimensionar_pilar(0.50, 0.20, 100, 1200, 0, 3.0, 25)["direcoes"]["x"]
        self.assertEqual(dx["lam1"], 90)

    def test_portico_solver_eixos(self):
        """Pórtico plano XZ pelo solver (pilares 20×40 com h no plano do pórtico, viga 20×50):
        a viga deve dar As = flexão simples de máx(−My) e As_neg = flexão de máx(+My); o pilar,
        direção x com h = 0,40 (M1d,mín = Nd(0,015 + 0,03·0,40)) e As = integração independente."""
        M = Modelo()
        F = (True,) * 6
        for i, x, z, ap in (("A", 0, 0, F), ("B", 0, 3, (False,) * 6), ("C", 6, 3, (False,) * 6), ("D", 6, 0, F)):
            M.add(No(i, x, 0, z, ap))
        mat = m.concreto(25)
        M.add(mat)
        sp, sb = s.secao_retangular(0.20, 0.40, "P"), s.secao_retangular(0.20, 0.50, "V")
        M.add(sp)
        M.add(sb)
        for bid, ni, nj, sc in (("P1", "A", "B", "P"), ("V1", "B", "C", "V"), ("P2", "D", "C", "P")):
            M.add(Barra(bid, ni, nj, sc, mat.nome))
        M.add(CasoCarga("G", "permanente"))
        M.add(CasoCarga("V", "vento"))
        M.add(CargaDistribuida("G", "V1", (0, 0, -30)))
        M.add(CargaNodal("V", "B", (20, 0, 0, 0, 0, 0)))
        res = sv.analisar(M, {"G": 1.4, "V": 1.4})
        lv = [tuple(map(float, ln)) for ln in res.esforcos["V1"]]
        rv = c.verificar_barra_concreto(sb, mat, [{"combinacao": "C", "linhas": lv}], SEM_ELS, 6.0, "viga")
        _, Asp = MEM.flexao(20, 45.5, max(-ln[5] for ln in lv) * 100, 25)
        _, Asn = MEM.flexao(20, 45.5, max(ln[5] for ln in lv) * 100, 25)
        self.assertAlmostEqual(rv["armadura"]["As_cm2"], Asp, delta=Asp * 1e-9)
        self.assertAlmostEqual(rv["armadura"]["As_neg_cm2"], Asn, delta=Asn * 1e-9)
        lp = [tuple(map(float, ln)) for ln in res.esforcos["P1"]]
        rp = c.verificar_barra_concreto(sp, mat, [{"combinacao": "C", "linhas": lp}], SEM_ELS, 3.0, "pilar")
        Nd, Mx = -min(ln[1] for ln in lp), max(abs(ln[5]) for ln in lp)
        dx = rp["detalhes"]["pilar"]["direcoes"]["x"]
        self.assertAlmostEqual(dx["h"], 0.40)
        self.assertAlmostEqual(dx["M1d_min"], Nd * (0.015 + 0.03 * 0.40), delta=1e-9)
        self.assertAlmostEqual(dx["M1d"], Mx, delta=1e-9)
        As = rp["armadura"]["As_pilar_cm2"]
        bars = lambda A: [(4.5, A / 2), (35.5, A / 2)]  # noqa: E731
        fcd, fyd = 25 / 14, 500 / 11.5
        self.assertGreaterEqual(FIB.MRd(Nd, 20, 40, bars(As * 1.01), fcd, fyd), Mx * 100)
        self.assertLess(FIB.MRd(Nd, 20, 40, bars(As * 0.99), fcd, fyd), Mx * 100)


# ===================================================================== γz e limites
class TestGamaZLimites(unittest.TestCase):
    def test_gama_z_irregular(self):
        """15.5.3 à mão: M1 = 12·3 + 12·6 + 12·9 + 6·12 = 288; ΔM = 900·(0,003+0,007+0,011) + 600·0,014
        = 27,3; γz = 1/(1 − 27,3/288) = 1,10472 → nós móveis, majoração 0,95γz (15.7.2)."""
        pav = [(3, 12, 900, .003), (6, 12, 900, .007), (9, 12, 900, .011), (12, 6, 600, .014)]
        r = c.gama_z([{"z_m": z, "H_kN": H, "P_kN": P, "delta_m": d} for z, H, P, d in pav])
        M1, dM, gz = MEM.gama_z(pav)
        self.assertAlmostEqual(r["M1_kNm"], 288.0, delta=1e-9)
        self.assertAlmostEqual(r["dM_kNm"], 27.3, delta=1e-9)
        self.assertAlmostEqual(r["gama_z"], gz, delta=1e-12)
        self.assertEqual(r["classificacao"], "nos_moveis")
        self.assertAlmostEqual(r["majoracao"], 0.95 * gz, delta=1e-12)

    def test_tabela_13_3_estado(self):
        """Tabela 13.3: movimento lateral com combinação frequente (ψ1 = 0,30); demais quase permanente."""
        self.assertEqual(c.limite_deslocamento("horizontal_topo", 30)["estado"], "ELS_FREQ")
        self.assertAlmostEqual(c.limite_deslocamento("alvenaria", 6.0)["limite_m"], 0.010)
        self.assertAlmostEqual(c.limite_deslocamento("alvenaria", 4.0)["limite_m"], 4.0 / 500)


# ===================================================================== achados (falham hoje)
class TestAchados(unittest.TestCase):
    def test_VC_concreto_001_viga_flexo_compressao_armadura_reportada(self):
        """VC-concreto-001: viga 20×50, C25, Nd = 600 kN (compressão > 0,1fcdAc), Md = 125 kN·m.
        Armadura simétrica exata (integração independente): 2,016 cm² POR FACE (4,03 total).
        A armadura reportada (As_cm2 inferior + As2_cm2 superior) tem de resistir a Md com Nd."""
        r = _ver(s.secao_retangular(0.20, 0.50), 25, _vb(40, 5.0, N=-600), "viga", 5.0)
        a = r["armadura"]
        bars = [(4.5, a["As2_cm2"]), (45.5, a["As_cm2"])]
        self.assertGreaterEqual(FIB.MRd(600, 20, 50, bars, 25 / 14, 500 / 11.5), 12500 * 0.99)

    def test_VC_concreto_002_estribo_Vy_com_torcao(self):
        """VC-concreto-002: com T ≠ 0 o estribo do cortante Vy (plano de b) é descartado.
        Vy = 200 kN em bw = h = 50, d = b − d' = 15,5 cm (C25) exige Asw/s do modelo I."""
        ln = [(x, 0, 200.0, 10.0, 5.0, -10.0, 0) for x in (0, 2.5, 5)]
        r = _ver(s.secao_retangular(0.20, 0.50), 25, ln, "viga", 5.0)
        _, _, Asw, Amin = MEM.cis_I(50, 15.5, 200, 25)
        self.assertGreaterEqual(r["armadura"]["Asw_s_cm2_m"], max(Asw, Amin) * 0.999)

    def test_VC_concreto_003_Vc_nulo_na_tracao(self):
        """VC-concreto-003: 17.4.2.2 c — Vc = 0 em elemento tracionado com LN fora da seção.
        N = +300 kN, M = 0, Vd = 100 kN, 20×50 (d = 45,5): Asw/s = 100/(0,9·45,5·43,48) = 5,62 cm²/m."""
        ln = [(x, 300.0, 0, 100.0, 0, 0.0, 0) for x in (0, 2.5, 5)]
        r = _ver(s.secao_retangular(0.20, 0.50), 25, ln, "viga", 5.0)
        self.assertGreaterEqual(r["armadura"]["Asw_s_cm2_m"], 100 / (0.9 * 45.5 * 500 / 11.5) * 100 * 0.999)

    def test_VC_concreto_004_tirante_flexao_obliqua(self):
        """VC-concreto-004: pilar 30×30 tracionado Nt = 200 kN com Mx = My = 30 kN·m; 4 barras de canto,
        z = 0,30 − 2·0,045 = 0,21 m: força na barra mais tracionada = Nt/4 + Mx/(2z) + My/(2z) →
        As total = (Nt + 2Mx/z + 2My/z)/fyd = 17,74 cm² (equilíbrio da seção fissurada)."""
        ln = [(x, 200.0, 0, 0, 0, 30.0, 30.0) for x in (0, 3)]
        r = _ver(s.secao_retangular(0.30, 0.30), 25, ln, "pilar", 3.0)
        esp = (200 + 2 * 30 / 0.21 + 2 * 30 / 0.21) / (50 / 1.15)
        self.assertGreaterEqual(r["armadura"]["As_pilar_cm2"], esp * 0.999)

    def test_VC_concreto_005_tirante_as_min(self):
        """VC-concreto-005: 17.3.5.3.1 — As,mín de pilar = máx(0,15Nd/fyd, 0,4%Ac) também no
        pilar tracionado: 30×30 → 3,6 cm² (Nt = 50 kN dá só 1,15 cm²)."""
        ln = [(x, 50.0, 0, 0, 0, 0, 0) for x in (0, 3)]
        r = _ver(s.secao_retangular(0.30, 0.30), 25, ln, "pilar", 3.0)
        self.assertGreaterEqual(r["armadura"]["As_pilar_cm2"], 0.004 * 900 - 1e-9)

    def test_VC_concreto_006_alfa_b_com_momento_minimo(self):
        """VC-concreto-006: 15.8.2 — αb = 1,0 quando M1d,A < M1d,mín. Pilar 20×50, Nd = 1000, Mdx = 10
        (< 21), le = 5 m, C25, αb informado 0,4: λ1 = 35, Md,tot = 21 + 1000·25/10·(1/r) = 79,96 kN·m."""
        dx = c.dimensionar_pilar(0.50, 0.20, 1000, 10, 0, 5.0, 25, alfa_b=0.4)["direcoes"]["x"]
        e = MEM.pilar_dir(1000, 10, 0.20, 5.0, 0.50, 25, alfa_b=1.0)
        self.assertAlmostEqual(dx["Md_tot"], e["Mtot"], delta=e["Mtot"] * TOL)

    def test_VC_concreto_007_taxa_max_lista_da_espec(self):
        """VC-concreto-007: ESPEC §7.2 — taxa_max é a maior entre x/d, V, As/As,máx, T, wk, flecha,
        λ (cobrimento e classe são requisitos de entrada, não razões de esforço). Viga pouco carregada
        (qd = 1 kN/m, 20×50): taxa_max = máx das razões de esforço ≪ 1, não 1,0 de 'cobrimento'."""
        r = _ver(s.secao_retangular(0.20, 0.50), 25, _vb(1.0, 5.0), "viga", 5.0)
        self.assertLess(r["taxa_max"], 0.5)
        self.assertNotIn(r["criterio"], ("cobrimento", "classe_concreto"))

    def test_VC_concreto_008_viga_com_Mz_nao_ok(self):
        """VC-concreto-008: viga com Mz = 60 kN·m (plano de b) não verificado não pode sair ok = True."""
        r = _ver(s.secao_retangular(0.20, 0.50), 25, _vb(10, 5.0, Mz=60.0), "viga", 5.0)
        self.assertFalse(r["ok"])

    def test_VC_concreto_009_alfa_b_fora_da_faixa(self):
        """VC-concreto-009: 15.8.2 — 0,4 ≤ αb ≤ 1,0; αb = 0,2 deve ser recusado (ValueError)."""
        with self.assertRaises(ValueError):
            c.dimensionar_pilar(0.50, 0.20, 1000, 40, 0, 5.0, 25, alfa_b=0.2)


if __name__ == "__main__":
    unittest.main()
