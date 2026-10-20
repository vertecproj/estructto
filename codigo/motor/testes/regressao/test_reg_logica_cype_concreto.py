"""Regressão — lógica do CYPE 2016 × nosso concreto (NBR 6118). Relatório: verificacao/logica-cype-concreto.md.

Esperados vêm da memória independente `verificacao/memorias/logica-cype-concreto.py` (fórmulas da norma
e regras do CYPE descritas em linguagem de engenharia; não importa prossen_estrutural) ou de solução
fechada no próprio teste. Nenhum esperado foi copiado do código verificado.
@unittest.expectedFailure = divergência em que o CYPE está certo pela norma e o nosso código ainda não
(id LC-NNN); o dono remove o decorador ao corrigir. Os demais testes protegem pontos em que o nosso código
já segue a norma (iguais ao CYPE ou mais corretos que ele).
"""
import importlib.util
import math
import os
import sys
import unittest

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, RAIZ)
from prossen_estrutural.concreto import nbr6118 as c  # noqa: E402
from prossen_estrutural import materiais as m, secoes as s  # noqa: E402

_p = os.path.join(RAIZ, "verificacao", "memorias", "logica-cype-concreto.py")
_sp = importlib.util.spec_from_file_location("logica_cype_concreto", _p)
MEM = importlib.util.module_from_spec(_sp)
_sp.loader.exec_module(MEM)
SEM_ELS = {"qp": None, "freq": None}


def _ver(b, h, fck, linhas, papel, L, params=None):
    return c.verificar_barra_concreto(s.secao_retangular(b, h, "S"), m.concreto(fck),
                                      [{"combinacao": "C", "linhas": linhas}], SEM_ELS, L, papel, params or {})


def _pilar(b, h, N, L=3.0, fck=25, params=None):
    return _ver(b, h, fck, [(0.0, -N, 0, 0, 0, 0, 0), (L, -N, 0, 0, 0, 0, 0)], "pilar", L, params)


# ============================================================ protegem (nosso = norma)
class TestIguaisOuMelhores(unittest.TestCase):
    def test_flexao_simples_retangular_x_parabola(self):
        """C1: 20×50, d = 45,5 cm, C25, Md = 150 kN·m. CYPE integra parábola-retângulo; nós usamos o
        retangular (17.2.2 e). Diferença ≤ 2% (tolerância de concreto)."""
        As_pr, _ = MEM.As_flexao_simples(0.20, 0.455, 150.0, 25, 500)
        self.assertAlmostEqual(c.flexao_simples(0.20, 0.455, 150.0, 25, 500)["As"] / (As_pr * 1e4), 1.0, delta=0.02)

    def test_vc_zero_na_tracao_de_pequena_excentricidade(self):
        """C4: 20×50, N = 200 kN (tração), M = 30 kN·m → e = 0,15 m ≤ d − h/2 = 0,205 m (domínio 1, LN fora
        da seção): Vc = 0 (17.4.2.2 c). O CYPE usa a seção bruta (e > h/6 → Vc = Vc0 = 70 kN)."""
        self.assertEqual(MEM.vc_nbr(0.20, 0.455, 0.50, 25, -200.0, 30.0), 0.0)
        self.assertGreater(MEM.vc_cype(0.20, 0.455, 0.50, 25, -200.0, 30.0), 69.0)
        r = c.cisalhamento(0.20, 0.455, 100.0, 25, N=200.0, Md=30.0, h=0.50)
        self.assertEqual(r["Vc"], 0.0)

    def test_vc_flexo_compressao_nao_passa_da_norma(self):
        """C3: pilar 30×50, C30, N = 1500 kN, M = 80 kN·m. NBR 17.4.2.2 b: Vc = Vc0(1 + M0/Md) ≤ 2Vc0 =
        237,2 kN; o nosso usa Vc0 = 118,6 kN (a favor da segurança, declarado em COBERTURA)."""
        v0 = MEM.vc0(0.30, 0.455, 30)
        self.assertAlmostEqual(v0, 0.6 * 0.7 * 0.3 * 30 ** (2 / 3) * 1e3 / 1.4 * 0.30 * 0.455, places=6)
        r = c.cisalhamento(0.30, 0.455, 150.0, 30, N=-1500.0, Md=80.0, h=0.50)
        self.assertAlmostEqual(r["Vc"], v0, delta=0.02 * v0)
        self.assertLessEqual(r["Vc"], MEM.vc_nbr(0.30, 0.455, 0.50, 30, 1500.0, 80.0))

    def test_torcao_TRd2_e_interacao(self):
        """C5: 20×50, C25, Td = 10, Vd = 80, he = A/u: TRd2 = 0,5·αv2·fcd·Ae·he = 31,63 kN·m (17.5.1.5);
        Td/TRd2 + Vd/VRd2 = 0,519 (17.7.2.2) — mesma regra no CYPE (nbhcomp07a/07e)."""
        he = 0.2 * 0.5 / 1.4
        Ae = (0.2 - he) * (0.5 - he)
        TRd2 = 0.5 * 0.9 * 25e3 / 1.4 * Ae * he
        VRd2 = MEM.vrd2(0.20, 0.455, 25)
        t = c.torcao(0.20, 0.50, 10.0, 25, 0.045, Vd=80.0, VRd2=VRd2, he=he)
        self.assertAlmostEqual(t["TRd2"], TRd2, delta=1e-6 * TRd2)
        self.assertAlmostEqual(t["interacao"], 10 / TRd2 + 80 / VRd2, places=9)

    def test_pilar_esbelto_curvatura(self):
        """C7: 20×50, plano de b = 0,20, Nd = 800, M1 = 40, le = 4,5 m, C25: λ = 77,9 > λ1 = 35;
        ν = 0,448 → 1/r = mín(0,005/(h(ν+0,5)), 0,005/h) = 0,025 → Md,tot = 40 + 800·4,5²/10·0,025 = 80,50."""
        r = c.dimensionar_pilar(0.2, 0.5, 800.0, 0.0, 40.0, 4.5, 25, dl=0.04)
        self.assertAlmostEqual(r["direcoes"]["y"]["Md_tot"], 40 + 800 * 4.5 ** 2 / 10 * 0.025, places=6)

    def test_lambda1_com_teto_90(self):
        """C8: e1/h = 6 → CYPE λ1 = 25 + 12,5·6 = 100 (sem teto; dispensaria a 2ª ordem com λ = 95).
        NBR 15.8.2: 35 ≤ λ1 ≤ 90. Nosso: λ1 = 90 e λ = 95 > 90 reprova (fora do pilar-padrão)."""
        self.assertAlmostEqual(MEM.lambda1_cype(1.2, 0.2), 100.0, places=12)
        le = 95 * 0.2 / math.sqrt(12)
        r = c.dimensionar_pilar(0.2, 0.4, 100.0, 0.0, 120.0, le, 25, dl=0.04)
        self.assertEqual(r["direcoes"]["y"]["lam1"], 90.0)
        self.assertTrue(any("90" in a for a in r["alertas"]))

    def test_obliqua_aproximada_a_favor_da_seguranca(self):
        """C9b: 30×30, C30, Nd = 1200, Mx = My = 45 kN·m, 4 barras de canto. O CYPE integra a seção em
        2D; a envoltória α = 1,2 (17.2.5.2) do nosso código deve dar utilização ≥ a exata."""
        r = c.dimensionar_pilar(0.3, 0.3, 1200.0, 45.0, 45.0, 1.0, 30, dl=0.04)
        mx, my = r["direcoes"]["x"]["Md_tot"], r["direcoes"]["y"]["Md_tot"]
        u = MEM.capacidade_obliqua(0.3, 0.3, 1200.0, mx, my, r["As"] * 1e-4, 30, 500, 0.04, nf=20, nth=120)
        self.assertLessEqual(u, r["interacao_obliqua"] * 1.02)

    def test_flecha_branson(self):
        """C11: biapoiada L = 5 m, 20×50, d = 45,5, As = 3φ16, C25, q_qp = 15 kN/m: Branson (17.3.2.1.1) e
        αf = ξ(∞) − ξ(1 mês) (17.3.2.1.2) — mesma base do compflecha."""
        As = 3 * math.pi * 1.6 ** 2 / 4
        fi, ft, _, Mr = MEM.flecha_branson(0.2, 0.5, 0.455, As * 1e-4, 15 * 25 / 8, 5.0, 15.0, 25)
        r = c.flecha_viga(0.2, 0.5, 0.455, As, 15 * 25 / 8, 5.0, 15.0, 25)
        self.assertAlmostEqual(r["f_imediata"], fi * 1e3, delta=0.01 * fi * 1e3)
        self.assertAlmostEqual(r["f_total"], ft * 1e3, delta=0.01 * ft * 1e3)
        self.assertAlmostEqual(r["Mr"], Mr, delta=1e-3 * Mr)


# ============================================================ divergências: CYPE certo pela norma
class TestDivergencias(unittest.TestCase):
    def test_LC_001_as_min_por_md_min_quando_d_h_menor_que_08(self):
        """LC-001: viga 20×25, CAA III (d = 19,5 cm, d/h = 0,78), C40. 17.3.5.2.1: As,mín pelo Md,mín =
        0,8·W0·fctk,sup = 7,60 kN·m, respeitada a taxa absoluta 0,15% → 0,928 cm². A Tabela 17.3 (0,179% =
        0,895 cm²) vale para d/h = 0,8. O CYPE faz pelo Md,mín (nbhcomp02, constantes 0,8 e 0,39)."""
        r = _ver(0.2, 0.25, 40, [(0, 0, 0, 1, 0, -1.0, 0), (4, 0, 0, -1, 0, -1.0, 0)], "viga", 4.0,
                 {"caa": "III"})
        esperado, _ = MEM.as_min_cype(0.2, 0.25, r["detalhes"]["d_m"], 40, 500)
        self.assertGreaterEqual(r["detalhes"]["As_min_cm2"], 0.99 * esperado * 1e4)

    def test_LC_002_wk_com_acri_por_barra(self):
        """LC-002: 20×50, d = 45,5, 3φ16, Mfreq = 60 kN·m, C25, CA-50. 17.3.3.2: wk por barra com Acri
        (retângulo até 7,5φ do eixo, limitado pelas faces e pela meia distância às vizinhas): a barra de canto
        dá wk = 0,188 mm. O nosso usa uma faixa única bw·(h − d + 7,5φ) → 0,177 mm (−5,7%)."""
        As = 3 * math.pi * 1.6 ** 2 / 4
        x, I = MEM.estadio2(0.2, 0.455, As * 1e-4, 15)
        sig = 15 * 60 * (0.455 - x) / I
        a1 = math.pi * 0.016 ** 2 / 4
        esperado = max(MEM.wk(16, sig, a1 / a, 25, 2.25) for a in MEM.acri_por_barra(0.2, 0.5, 0.455, 3, 0.016, 0.043))
        wk, _, phi = c._wk(60.0, As, 0.2, 0.5, 0.455, 0.03, 25, 2.25)
        self.assertEqual(phi, 16.0)
        self.assertGreaterEqual(wk, 0.98 * esperado)

    def test_LC_003_gama_n_pilar_entre_14_e_19_cm(self):
        """LC-003: pilar 14×40 (13.2.3, Tabela 13.1): γn = 1,95 − 0,05·14 = 1,25 multiplica as ações. O CYPE
        aplica (nb07horm_con_factor_amplificacion_esfuerzos_por_ancho_min_seccion). Esperado: As(Nd) =
        As(1,25·Nd) sem γn. O "sem" chama dimensionar_pilar direto (não aplica γn), com os parâmetros que
        verificar_barra_concreto usa por padrão: dl = cnom(CAA II) 30 + estribo 5 + φ/2 10 = 45 mm, CA-50, le = L."""
        self.assertAlmostEqual(MEM.gama_n(0.14), 1.25, places=12)
        sem = c.dimensionar_pilar(0.14, 0.40, 1.25 * 500.0, 0, 0, 3.0, 25, dl=0.045)["As"]
        com = _pilar(0.14, 0.40, 500.0)["armadura"]["As_pilar_cm2"]
        self.assertGreaterEqual(com, 0.99 * sem)
        self.assertLessEqual(com, 1.01 * sem)  # e não mais que isso (γn aplicado uma vez só)

    def test_LC_004_dimensao_minima_de_pilar(self):
        """LC-004: pilar 13×40 (b < 14 cm): 13.2.3 não permite. O CYPE verifica dimensão mínima e área ≥ 360 cm²
        (nbhcomp01_cumple_dimensiones_seccion_pilar / _area_seccion). Esperado: verificação 13.2.3 reprovada."""
        r = _pilar(0.13, 0.40, 100.0)
        v = [x for x in r["detalhes"]["verificacoes"] if "13.2.3" in x["item"]]
        self.assertTrue(v and not all(x.get("ok", x["taxa"] <= 1) for x in v))

    def test_LC_005_as_max_pilar_4_por_cento_fora_da_emenda(self):
        """LC-005: pilar 20×20, C25, Nd = 1100 kN, L = 2,5 m → As ≈ 6,7% Ac. 17.3.5.3.2: 8% inclui a
        sobreposição na emenda; com traspasse (padrão) As ≤ 4% fora dela. O CYPE verifica 4% "fora da zona
        de emendas". Esperado: reprovado."""
        r = _pilar(0.20, 0.20, 1100.0, L=2.5)
        self.assertGreater(r["armadura"]["As_pilar_cm2"], 0.04 * 400)
        self.assertFalse(r["ok"])

    def test_LC_006_espacamento_maximo_barras_pilar(self):
        """LC-006: pilar 20×60, Nd = 1500: 4 barras de canto ficam a 51 cm na face de 60 cm; 18.4.2.2 pede
        s ≤ mín(2b, 40 cm) = 40 cm. O CYPE verifica (nbhcomp01: 2·b ≤ 0,40 m). Esperado: ≥ 6 barras ou reprova."""
        r = _pilar(0.20, 0.60, 1500.0)
        self.assertTrue(r["detalhes"]["pilar"]["n_barras"] >= 6 or not r["ok"])

    def test_LC_007_armadura_de_pele(self):
        """LC-007: viga 20×70 (h > 60 cm): 17.3.5.2.3 pede Asp = 0,10%·Ac,alma por face (1,4 cm²),
        s ≤ d/3 e ≤ 20 cm. O CYPE verifica (nbhcomp02, constante 0,001; nbhcomp01, d/3 ≤ 0,20 m)."""
        r = _ver(0.2, 0.7, 25, [(0, 0, 0, 50, 0, -100, 0), (5, 0, 0, -50, 0, -100, 0)], "viga", 5.0)
        self.assertTrue(any("17.3.5.2.3" in v["item"] for v in r["detalhes"]["verificacoes"]))

    def test_LC_008_espacamento_maximo_estribos_viga(self):
        """LC-008: viga 20×50 com Vd = 300 kN > 0,67·VRd2 = 264,6 kN: 18.3.3.2 pede s ≤ 0,3d ≤ 20 cm (senão 0,6d ≤ 30 cm)
        e st ≤ d ≤ 80 cm / 0,6d ≤ 35 cm. O CYPE verifica (nbhcomp03, constantes 0,67/0,2/300/200/800/350)."""
        self.assertAlmostEqual(MEM.smax_estribo(300.0, MEM.vrd2(0.2, 0.455, 25), 0.455), 0.1365, places=9)
        r = _ver(0.2, 0.5, 25, [(0, 0, 0, 300, 0, -10, 0), (5, 0, 0, -300, 0, -10, 0)], "viga", 5.0)
        self.assertTrue(any("18.3.3.2" in v["item"] for v in r["detalhes"]["verificacoes"]))


if __name__ == "__main__":
    unittest.main()
