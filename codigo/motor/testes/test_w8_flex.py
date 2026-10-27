"""W8-FLEX (docs/v3/W8-FLEX.md): pré-dimensionamento das ligações flexíveis com o critério do W8-RIG — a primeira
candidata com taxa ≤ 0,90 em todas as verificações (inclusive disposições construtivas); senão a primeira que passa.

Padrão de fábrica: W310×38,7 (tw 5,8) na mesa do W250×73, V = 100 kN, ¾" A325. Antes:
  cantoneira dupla 0,91: ponta da viga g_viga − folga = 45 − 10 = 35 contra 32 (tabela 14, serra) → L102, g 65: 55 (0,58)
  apoio em cantoneira 0,91: borda lateral (150 − 80)/2 = 35 contra 32 → comprimento 80 + 2·40 = 160: 40 (0,80)
  chapa simples 0,93: ponta da viga a − folga = 75 − 10 = 65 contra 12·tw = 69,6 (6.3.12) →
      a = 10 + ⌊0,9·69,6/5⌋·5 = 70: 60/69,6 = 0,86."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes_flexiveis as F


def _fabrica():
    return {t["tipo"]: t["exemplo"] for t in P.tipos_ligacao()}


class TestPadroesDeFabrica(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ex = _fabrica()

    def _taxa(self, tipo):
        e = self.ex[tipo]
        r = P.verificar_ligacao(tipo, e["dados"], e["esforcos"])
        self.assertTrue(r["ok"], tipo)
        return r["taxa"], e["dados"]

    def test_tres_padroes_abaixo_de_090(self):
        for tipo in ("cantoneira_dupla_alma", "apoio_cantoneira", "chapa_simples_alma", "viga_viga_recorte"):
            with self.subTest(tipo=tipo):
                self.assertLessEqual(self._taxa(tipo)[0], 0.90)

    def test_outros_flexiveis_nao_pioram(self):
        self.assertLessEqual(self._taxa("chapa_extremidade_flexivel")[0], 0.80 + 1e-9)
        self.assertLessEqual(self._taxa("solda_direta_alma")[0], 0.75 + 1e-9)

    def test_escolhas_do_padrao(self):
        t, d = self._taxa("cantoneira_dupla_alma")
        self.assertEqual((d["cantoneira"], d["g_viga"], d["g_apoio"], d["d"]), ("L102x102x6.35", 65, 65, 19.05))
        self.assertAlmostEqual(t, 50.03 / 60.34, 3)   # governa a pressão de contato na aba do apoio
        t, d = self._taxa("apoio_cantoneira")
        self.assertEqual((d["comprimento"], d["g_colunas"]), (160, 80))
        t, d = self._taxa("chapa_simples_alma")
        self.assertEqual(d["a"], 70)
        self.assertAlmostEqual(t, 60 / 69.6, 3)        # borda máxima da ponta da viga (6.3.12)


class TestBusca(unittest.TestCase):
    def test_alma_do_pilar_fica_na_l76_que_passa(self):
        """A L102 não cabe entre as mesas do W250×73 (209,8 > 200,6 mm): nenhuma candidata chega a 0,90 → a primeira
        que passa (comportamento antigo)."""
        perfis = dict(perfil_viga="W310x38.7", perfil_suporte="W250x73", apoio="alma_pilar")
        d = F._sug_cd(perfis, dict(V=100))
        r = F.TIPOS["cantoneira_dupla_alma"]["verificar"](d, dict(V=100))
        self.assertEqual(d["cantoneira"], "L76x76x6.35")
        self.assertTrue(r["ok"])
        self.assertGreater(r["taxa"], 0.90)

    def test_alma_grossa_mantem_a_75(self):
        """W310×44,5 (tw 6,6): 0,9·12·6,6 = 71,3 ≥ 65 → a = 75 (Gerdau/AISC)."""
        d = F._sug_cs(dict(perfil_viga="W310x44.5", perfil_suporte="W250x73"), dict(V=100))
        self.assertEqual(d["a"], 75)

    def test_com_folga(self):
        self.assertEqual([F._com_folga(x) for x in (32, 26, 29, 36)], [40, 30, 35, 40])


if __name__ == "__main__":
    unittest.main()
