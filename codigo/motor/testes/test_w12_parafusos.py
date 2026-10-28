"""W12 (docs/v3/W12-PARAFUSOS.md): catálogo de parafusos em polegada (A325/A307, Indufix/Allenfix), comprimento comercial,
lista de compra no resultado e diâmetros do otimizador pela família da especificação."""
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L, ligacoes_limites as Q, parafusos as PF

POL = 25.4 / 1000


class TestCatalogo(unittest.TestCase):
    def test_comprimento_polegada(self):
        # ⅞" A325, pega 25 mm: 25 + 4,49 (F436) + 22,47 (porca pesada) + 2 fios (5,64) = 57,6 mm = 2,27" → 2½"
        self.assertAlmostEqual(PF.comprimento(0.025, 0.022225, "A325"), 2.5 * POL)
        # menor comprimento de linha: ½" A325 1", A307 1¼" (IDX-P007/P008)
        self.assertAlmostEqual(PF.comprimento(0.003, 0.0127, "A325"), 1.0 * POL)
        self.assertAlmostEqual(PF.comprimento(0.003, 0.0127, "A307"), 1.25 * POL)
        self.assertTrue(PF.sob_encomenda(PF.comprimento(0.140, 0.0254), 0.0254))   # > 6"

    def test_metrico(self):
        self.assertAlmostEqual(PF.comprimento(0.030, 0.020, "ISO 8.8"), 0.060)   # 30 + 3 + 18 + 5 = 56 → 60
        self.assertEqual(PF.descricao(0.020, 0.060, "ISO 8.8"), "M20 × 60 ISO 8.8")

    def test_descricao(self):
        self.assertEqual(PF.descricao(0.022225, 2.5 * POL, "A325"), "⅞\" × 2½\" A325")
        self.assertEqual(PF.descricao(0.015875, 2.0 * POL, "A307"), "⅝\" × 2\" A307")

    def test_familias(self):
        self.assertTrue(all(PF.polegada(d) for d in PF.diametros("A325")))
        self.assertFalse(any(PF.polegada(d) for d in PF.diametros("ISO 8.8")))
        pol = L._da_familia(Q.D_PARAFUSO, dict(tipo_parafuso="A325"))
        iso = L._da_familia(Q.D_PARAFUSO, dict(tipo_parafuso="ISO 10.9"))
        self.assertIn(15.875, pol); self.assertNotIn(16.0, pol)
        self.assertIn(16.0, iso); self.assertNotIn(15.875, iso)


class TestResultado(unittest.TestCase):
    def test_lista_parafusos_e_pega(self):
        for t in P.tipos_ligacao():
            ex = t.get("exemplo")
            if not ex:
                continue
            r = L.verificar_ligacao(t["tipo"], ex["dados"], ex["esforcos"])
            geo = (r.get("geometria") or {}).get("parafusos") or []
            parafusos = [b for b in geo if b.get("campo") != "d_pino"]
            if not parafusos:
                self.assertNotIn("lista_parafusos", r, t["tipo"])
                continue
            lp = r["lista_parafusos"]
            self.assertEqual(sum(x["quantidade"] for x in lp), len(parafusos), t["tipo"])
            for b in parafusos:   # o 3D põe a porca na pega: a pega sempre vem e cabe no comprimento
                self.assertIn("pega", b, t["tipo"])
                self.assertGreater(b["comprimento"], b["pega"], t["tipo"])
            tipo_p = ex["dados"].get("tipo_parafuso", "A325")
            if tipo_p in PF.POLEGADA:   # A325 de fábrica: só polegada
                self.assertTrue(all(PF.polegada(x["d_mm"]) for x in lp), (t["tipo"], lp))
            self.assertLessEqual(r.get("taxa", r.get("taxa_max", 0)), 0.90 + 1e-9, t["tipo"])

    def test_aviso_metrico_com_a325(self):
        r = L.verificar_ligacao("parafuso", dict(d_mm=20, tipo_parafuso="A325", t=0.0095, lf=0.03), {"Ft": 10, "Fv": 10})
        self.assertTrue(any("polegada" in a for a in r["avisos"]), r["avisos"])


if __name__ == "__main__":
    unittest.main()
