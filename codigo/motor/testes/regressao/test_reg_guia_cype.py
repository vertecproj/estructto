"""Regressão das divergências achadas na conferência cruzada com o CYPE 2016 — verificador-calculos.

Relatório: `verificacao/2026-10-20-guia-cype.md`. Só divergências confirmadas pela norma (o CYPE é guia de
cobertura, não fonte de valor). Testes que falham hoje: `expectedFailure` + id; o dono remove o decorador
ao corrigir.
"""
import re
import unittest
from pathlib import Path

from prossen_estrutural import materiais, projeto as P, secoes
from prossen_estrutural.aco import nbr8800

EX = Path(__file__).resolve().parents[2] / "exemplos"


def _edificio_nos_moveis():
    """edificio_concreto com as cargas distribuídas (G1 e Q1) ×3,5: γz(Y) ≈ 1,15 (nós móveis)."""
    m = P.carregar(EX / "edificio_concreto.json")
    for c in m.cargas_distribuidas:
        c.q = [x * 3.5 for x in c.q]
    return m


class TestNosMoveisSemMajoracao(unittest.TestCase):
    """VC-guia-001. NBR 6118:2014 15.7.2: com 1,1 < γz ≤ 1,3 os esforços finais (1ª + 2ª ordem) podem ser
    obtidos majorando as ações horizontais da combinação por 0,95·γz; sem isso (e sem análise de 2ª ordem
    com a rigidez de 15.7.3) a verificação das barras usa esforços de 1ª ordem, contra a segurança.
    O CYPE aplica fatores de amplificação às hipóteses de vento antes do dimensionamento (bloco 21,
    `cypecomb_maximos_factores_de_amplificacion`; bloco 20, `estab_global` no front)."""

    @classmethod
    def setUpClass(cls):
        cls.c = P.calcular(_edificio_nos_moveis(), {"pdelta": False})

    def test_pre_condicao_nos_moveis(self):
        gz = self.c.estabilidade["gama_z"]["Y"]
        self.assertEqual(gz["classificacao"], "nos_moveis")
        self.assertGreater(gz["valor"], 1.1)
        self.assertLessEqual(gz["valor"], 1.3)

    def test_majoracao_aplicada_ou_avisada(self):  # VC-guia-001 corrigido (projeto._amplificar)
        """Contrato mínimo (ESPEC §6.5, nada silencioso): aviso de que os esforços de 1ª ordem não foram
        majorados por 0,95·γz (ou a majoração aplicada e registrada)."""
        textos = list(self.c.estabilidade.get("avisos") or [])
        textos += [a.get("mensagem", "") for a in self.c.avisos if isinstance(a, dict)]
        self.assertTrue(any(re.search(r"0[,.]95\s*·?\s*γz|majora", t) for t in textos),
                        "γz entre 1,1 e 1,3 sem majoração 0,95·γz nem aviso")


class TestFaixasDeParametros(unittest.TestCase):
    """VC-guia-002. Parâmetros do usuário fora da faixa da norma entram no cálculo sem limite.
    O CYPE limita Cb (C1) a 1,0–3,0 (`nblcomp_rango_coef_c1_momento_critico`, bloco 10)."""

    def setUp(self):
        self.s = secoes.catalogo("W310x38.7")
        self.m = materiais.aco("A-572 GR.50")

    def _MRd(self, cb):
        r = nbr8800.verificar_barra_aco(self.s, self.m, {"N": 0, "Mx": 60, "My": 0, "Vy": 0, "Vx": 0, "T": 0},
                                        8, 8, 8, Cb=cb)
        return r["detalhes"]["flexao_x"]["MRd"]

    def test_cb_limitado_a_3(self):
        """NBR 8800:2008 5.4.2.3: Cb ≤ 3,0. Cb = 5 informado deve dar o mesmo MRd de Cb = 3 (ou ser
        recusado). Hoje: 193,0 × 154,3 kN·m (+25 %)."""
        try:
            m5 = self._MRd(5.0)
        except ValueError:
            return
        self.assertAlmostEqual(m5, self._MRd(3.0), delta=1e-6 * m5)

    def test_ct_limitado_a_1(self):
        """NBR 8800:2008 5.2.5: Ct ≤ 1,0. Com An = 30 cm², N = 900 kN, A572 Gr.50:
        Nt,Rd(ruptura) = 1,0·30·45/1,35 = 1000 kN → taxa ≥ 0,90 com qualquer Ct válido.
        Ct = 1,5 hoje dá taxa 0,60 (recusar ou limitar)."""
        try:
            r = nbr8800.verificar_barra_aco(self.s, self.m, {"N": 900, "Mx": 0, "My": 0, "Vy": 0, "Vx": 0, "T": 0},
                                            3, 3, 3, An=0.003, Ct=1.5)
        except ValueError:
            return
        self.assertGreaterEqual(r["taxa_max"], 0.90 - 1e-9)


class TestFyPorEspessura(unittest.TestCase):
    """VC-guia-003. ASTM A242/A242M (tabela de propriedades de tração): fy = 345 MPa só até 19 mm (3/4");
    315 MPa de 19 a 38 mm; 290 MPa de 38 a 100 mm. O CYPE separa o A-242 por espessura (bloco 19, `acelam11`).
    `materiais.ACOS` tem um valor único (345): pelo precedente de VC-aco-011 (um valor por aço → o menor),
    fy ≤ 315 MPa enquanto não houver seleção por espessura."""

    def test_a242_fy_unico_conservador(self):
        self.assertLessEqual(materiais.aco("A-242").fy / 1000, 315.0)


if __name__ == "__main__":
    unittest.main()
