"""v0.6 B8: o cálculo roda com SÓ o peso próprio (sem cargas do usuário); o peso próprio nasce ligado."""
import json
import os
import unittest

from prossen_estrutural import projeto
from prossen_estrutural.web import servidor

EX = os.path.join(os.path.dirname(__file__), "..", "exemplos", "viga_biapoiada.json")


def _sem_cargas(com_chave):
    d = json.load(open(EX, encoding="utf-8"))
    for k in ("cargas_nodais", "cargas_distribuidas", "cargas_pontuais"):
        d[k] = []
    d["casos"] = {}
    d["peso_proprio"] = None
    if not com_chave:
        del d["peso_proprio"]
    return d


class TestSoPesoProprio(unittest.TestCase):
    def test_arquivo_antigo_sem_a_chave_liga_pp(self):
        m = projeto.de_dict(_sem_cargas(False))
        self.assertEqual(m.peso_proprio, "PP")
        self.assertIn("PP", m.casos)

    def test_pp_desligado_de_proposito_continua_desligado(self):
        m = projeto.de_dict(_sem_cargas(True))
        self.assertIsNone(m.peso_proprio)

    def test_calcula_so_com_pp(self):
        r = projeto.calcular(projeto.de_dict(_sem_cargas(False)))
        nomes = list(r.resultados)
        self.assertIn("ELU G", nomes)
        self.assertIn("ELS QP", nomes)
        self.assertTrue(r.verificacao, "sem verificação das barras")

    def test_modelo_novo_da_interface_nasce_com_pp(self):
        m = servidor._com_peso_proprio(projeto.novo_modelo("x"), {})
        self.assertEqual((m.peso_proprio, list(m.casos)), ("PP", ["PP"]))
        m2 = servidor._com_peso_proprio(projeto.novo_modelo("x"), {"peso_proprio": False})
        self.assertIsNone(m2.peso_proprio)


if __name__ == "__main__":
    unittest.main()
