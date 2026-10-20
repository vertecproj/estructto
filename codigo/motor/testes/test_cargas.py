"""NBR 6120:2019 — valores conferidos contra o texto da norma reproduzido em cópias públicas
(tabela 1 item 5.3; tabela 10 item 6.2; item 6.4; tabela 13 item 6.6.1) e notas de aula da UFAL."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural.acoes import cargas
from prossen_estrutural.acoes.combinacoes import PSI


class TestCargas(unittest.TestCase):
    def test_pesos_especificos_tabela1(self):
        # NBR 6120:2019 tabela 1: concreto armado 25; concreto simples 24; bloco cerâmico vazado
        # com paredes vazadas 12; aço 77–78,5 (adotado 78,5)
        self.assertEqual(cargas.peso_especifico("concreto_armado")["valor_kN_m3"], 25.0)
        self.assertEqual(cargas.peso_especifico("concreto_simples")["valor_kN_m3"], 24.0)
        self.assertEqual(cargas.peso_especifico("bloco_ceramico_vazado")["valor_kN_m3"], 12.0)
        aco = cargas.peso_especifico("aco")
        self.assertEqual(aco["valor_kN_m3"], 78.5)
        self.assertEqual(aco["faixa_kN_m3"], [77.0, 78.5])
        self.assertEqual(cargas.peso_especifico("argamassa_cimento_areia")["valor_kN_m3"], 21.0)
        self.assertEqual(cargas.peso_especifico("madeira_conifera_c30")["valor_kN_m3"], 6.0)
        self.assertEqual(cargas.peso_especifico("concreto_armado")["item"], "5.3 tabela 1")

    def test_cargas_uso_tabela10(self):
        # tabela 10: dormitórios 1,5; escritórios (salas de uso geral) 2,5; lojas 4; garagem Ia 3 (tabela 13);
        # despensa/área de serviço 2; corredor de uso comum 3; sala com estantes de livros 6
        self.assertEqual(cargas.carga_uso("residencial_dormitorio")["q_kN_m2"], 1.5)
        self.assertEqual(cargas.carga_uso("residencial_despensa_servico_lavanderia")["q_kN_m2"], 2.0)
        self.assertEqual(cargas.carga_uso("escritorio_sala_geral")["q_kN_m2"], 2.5)
        self.assertEqual(cargas.carga_uso("loja")["q_kN_m2"], 4.0)
        self.assertEqual(cargas.carga_uso("garagem_automoveis")["q_kN_m2"], 3.0)
        self.assertEqual(cargas.carga_uso("garagem_automoveis")["item"], "6.6.1 tabela 13")
        self.assertEqual(cargas.carga_uso("residencial_corredor_comum")["q_kN_m2"], 3.0)
        self.assertEqual(cargas.carga_uso("biblioteca_estantes")["q_kN_m2"], 6.0)
        self.assertEqual(cargas.carga_uso("escada_sem_acesso_publico")["q_kN_m2"], 2.5)
        self.assertEqual(cargas.carga_uso("escada_com_acesso_publico")["q_kN_m2"], 3.0)
        self.assertEqual(cargas.carga_uso("cobertura_laje_manutencao")["q_kN_m2"], 1.0)

    def test_cobertura_6_4(self):
        # 6.4: q = 0,5·α, α = 1 (i ≤ 2%), 2 − 0,5·i (2–3%), 0,5 (i ≥ 3%); 0,25 ≤ q ≤ 0,50
        self.assertEqual(cargas.carga_cobertura(1.0)["q_kN_m2"], 0.50)
        self.assertEqual(cargas.carga_cobertura(2.0)["q_kN_m2"], 0.50)
        self.assertAlmostEqual(cargas.carga_cobertura(2.5)["q_kN_m2"], 0.375, 12)
        self.assertEqual(cargas.carga_cobertura(3.0)["q_kN_m2"], 0.25)
        self.assertEqual(cargas.carga_cobertura(30.0)["q_kN_m2"], 0.25)
        with self.assertRaises(ValueError):
            cargas.carga_cobertura(0.5)

    def test_lista_e_erros(self):
        lista = cargas.lista_cargas_uso()
        self.assertGreaterEqual(len(lista), 20)
        self.assertEqual(len({d["uso"] for d in lista}), len(lista))
        for d in lista:
            self.assertGreater(d["q_kN_m2"], 0)
            self.assertIn(d["grupo_psi"], PSI)  # compatível com combinacoes
            self.assertTrue(d["item"])
        with self.assertRaises(ValueError):
            cargas.carga_uso("hangar")
        with self.assertRaises(ValueError):
            cargas.peso_especifico("unobtainium")

    def test_edicao_cobertura(self):
        self.assertEqual(cargas.EDICAO, "NBR 6120:2019")
        self.assertTrue(all({"norma", "edicao", "item", "verificacao", "limitacao"} <= set(c) for c in cargas.COBERTURA))


if __name__ == "__main__":
    unittest.main()
