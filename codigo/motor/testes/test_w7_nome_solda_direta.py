"""W7-UI: o padrão se chama "Solda direta" (não "da alma": ela solda também as mesas); o id do tipo não muda (arquivos .pcon)."""
import unittest

from prossen_estrutural.aco import ligacoes_flexiveis as F


class NomeSoldaDireta(unittest.TestCase):
    def test_nome_visivel_e_id_estavel(self):
        self.assertIn("solda_direta_alma", F.TIPOS)
        self.assertEqual(F.TIPOS["solda_direta_alma"]["nome"], "Solda direta")


if __name__ == "__main__":
    unittest.main()
