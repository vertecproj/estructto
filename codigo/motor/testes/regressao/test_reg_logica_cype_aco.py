"""Regressão — lógica do CYPE 2016 × nosso aço (NBR 8800 / NBR 14762 / ligações). Relatório:
verificacao/logica-cype-aco.md.

Esperados vêm da memória independente `verificacao/memorias/logica-cype-aco.py` (fórmulas da norma e
regras do CYPE descritas em linguagem de engenharia; não importa prossen_estrutural) ou de solução
fechada no próprio teste. Nenhum esperado foi copiado do código verificado.
@unittest.expectedFailure = divergência em que o CYPE está certo pela norma e o nosso código ainda não
(id LCA-NNN); o dono remove o decorador ao corrigir. Os demais testes protegem pontos em que o nosso
código já segue a norma (iguais ao CYPE ou mais corretos que ele).
"""
import importlib.util
import math
import os
import sys
import unittest

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, RAIZ)
from prossen_estrutural import materiais, secoes  # noqa: E402
from prossen_estrutural.aco import nbr8800 as n, nbr14762 as pf  # noqa: E402

_spec = importlib.util.spec_from_file_location(
    "logica_cype_aco", os.path.join(RAIZ, "verificacao", "memorias", "logica-cype-aco.py"))
M = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(M)

A36 = materiais.aco("A-36")
FY = A36.fy


class TestIguaisOuMelhores(unittest.TestCase):
    """Pontos em que o nosso código coincide com o CYPE ou segue a norma melhor que ele."""

    def test_compressao_I_igual_ao_cype(self):
        # NBR 8800 5.3 / E.1.1 (dupla simetria: menor entre Nex, Ney, Nez) — CYPE nblcomp03 + seccion
        s = secoes.catalogo("W200x26.6")
        p = s.props
        ne = M.ne_dupla_simetria(p["Ix"], p["Iy"], p["Cw"], p["J"], p["r0"], 3.0, 3.0, 3.0)
        esperado = M.nc_rd(s.A, FY, ne)                       # ≈ 474,2 kN
        self.assertAlmostEqual(n.compressao(s, A36, 3.0, 3.0, 3.0)["NcRd"], esperado, delta=0.01 * esperado)

    def test_flexao_I_FLT_FLM_FLA_igual_ao_cype(self):
        # tabela G.1; Mcr pela expressão geral do CYPE (k = kw = 1, zg = 0) ≡ G.1
        s = secoes.catalogo("W310x38.7")
        p = s.props
        esperado = M.mrd_I_laminado(FY, p["Zx"], p["Wx"], p["Iy"], p["ry"], p["J"], p["Cw"],
                                    p["bt_mesa"], p["ht_alma"], 4.0, 1.0)  # ≈ 109,8 kN·m
        self.assertAlmostEqual(n.flexao_x(s, A36, 4.0, 1.0)["MRd"], esperado, delta=0.01 * esperado)

    def test_cb_calculado_pela_norma_melhor_que_c1_informado(self):
        # 5.4.2.3: biapoiada com carga uniforme → Cb = 12,5/11 = 1,136 (o CYPE usa o C1 informado, padrão 1,0)
        import numpy as np
        xs = np.linspace(0, 4, 13)
        cb = n.cb_de_diagrama(xs, [x * (4 - x) for x in xs])
        self.assertAlmostEqual(cb, 12.5 / 11, places=6)
        s = secoes.catalogo("W310x38.7")
        p = s.props
        esperado = M.mrd_I_laminado(FY, p["Zx"], p["Wx"], p["Iy"], p["ry"], p["J"], p["Cw"],
                                    p["bt_mesa"], p["ht_alma"], 4.0, 12.5 / 11)  # ≈ 124,8 kN·m
        self.assertAlmostEqual(n.flexao_x(s, A36, 4.0, cb)["MRd"], esperado, delta=0.01 * esperado)

    def test_tracao_com_ruptura_da_secao_liquida(self):
        # 5.2.2 b): o CYPE só faz A·fy/γa1 (778,0 kN); a norma exige também Ct·An·fu/γa2
        s = secoes.catalogo("W200x26.6")
        esperado = min(s.A * FY / 1.10, 0.85 * 0.75 * s.A * A36.fu / 1.35)  # 646,6 kN
        self.assertAlmostEqual(n.tracao(s, A36, An=0.75 * s.A, Ct=0.85)["NtRd"], esperado, delta=1e-6)
        self.assertLess(esperado, M.nt_rd_cype(s.A, FY))

    def test_interacao_5_5_1_2_igual_ao_cype(self):
        for N, NR, Mx, MxR, My, MyR in ((100, 400, 30, 100, 2, 40), (20, 400, 30, 100, 2, 40)):
            self.assertAlmostEqual(n.interacao(N, NR, Mx, MxR, My, MyR), M.interacao(N, NR, Mx, MxR, My, MyR),
                                   places=12)


class TestDivergencias(unittest.TestCase):
    """CYPE certo pela norma; o nosso código ainda não."""

    def test_LCA_001_torcao_tubo_circular_5_5_2_2(self):
        s = secoes.secao_tubo_circular(0.1683, 0.0064)
        trd, _ = M.trd_tubo_circular(0.1683, 0.0064, 3.0, FY)       # ≈ 35,9 kN·m
        r = n.verificar_barra_aco(s, A36, dict(T=40.0), 3.0, 3.0, 3.0)
        self.assertFalse(r["ok"])
        self.assertAlmostEqual(r["taxa_max"], 40.0 / trd, delta=0.01 * 40.0 / trd)

    def test_LCA_002_torcao_tubo_retangular_5_5_2_3(self):
        s = secoes.secao_tubo_retangular(0.200, 0.100, 0.006)
        trd, _ = M.trd_tubo_retangular(0.100, 0.200, 0.006, FY)     # ≈ 29,7 kN·m
        r = n.verificar_barra_aco(s, A36, dict(T=35.0), 3.0, 3.0, 3.0)
        self.assertFalse(r["ok"])
        self.assertAlmostEqual(r["taxa_max"], 35.0 / trd, delta=0.02 * 35.0 / trd)

    def test_LCA_003_pff_distorcional_nao_dispensada_precisa_ser_verificada(self):
        ue = secoes.secao_de_spec(dict(forma="Ue", h=0.150, b=0.100, D=0.010, t=0.0015))
        r = pf.verificar_pff(ue, A36, dict(N=-40.0), 1.0, 1.0, 1.0, 1.0)
        self.assertFalse(r["detalhes"]["compressao"]["distorcional"]["dispensada"])
        # 9.7.3: Nc,Rd,dist tem de entrar na verificação; com a expressão simplificada do CYPE
        # (Nc,Rd,dist ≈ 31 kN) a barra reprova. Sem Ndist calculado, não pode sair "ok".
        self.assertTrue("distorcional" in r["detalhes"]["taxas"] or not r["ok"])


if __name__ == "__main__":
    unittest.main()
