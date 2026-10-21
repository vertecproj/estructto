"""Correções da revisão (wt-fix-doc): `unir` grupos de ligação com ids repetidos e params.cardinal da barra."""
import copy
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P

ACO = "A-572 GR.50"
W = {"pilar": "W310x38.7", "viga": "W360x51"}


def _portico():
    m = P.novo_modelo()
    P.gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": W, "vaos_x": [6, 6], "alturas": [3, 3]})
    P.ligacoes_identificar(m)
    return m


class TestUnirRepetidos(unittest.TestCase):
    def test_ids_repetidos_nao_quebram_nem_mudam_pela_metade(self):
        m = _portico()
        gs = m.ligacoes["grupos"]
        a, b = sorted(k for k, g in gs.items() if g["tipo"] == "viga_pilar_mesa")
        total = len(gs[a]["membros"]) + len(gs[b]["membros"])
        r = P.ligacoes_editar(m, {"acao": "unir", "grupos": [a, b, b, a]})
        self.assertEqual(r["grupo"], a)
        self.assertNotIn(b, m.ligacoes["grupos"])
        self.assertEqual(len(m.ligacoes["grupos"][a]["membros"]), total)

    def test_so_um_id_repetido_e_recusado_sem_mudar(self):
        m = _portico()
        antes = copy.deepcopy(m.ligacoes)
        a = next(iter(m.ligacoes["grupos"]))
        with self.assertRaises(P.ErroEntrada):
            P.ligacoes_editar(m, {"acao": "unir", "grupos": [a, a]})
        self.assertEqual(m.ligacoes, antes)


class TestCardinal(unittest.TestCase):
    def test_cardinal_valido_sem_aviso_e_invalido_recusado(self):
        m = _portico()
        bid = next(iter(m.barras))
        P.editar(m, "barra", bid, {"params": {"cardinal": 8}})
        self.assertEqual(m.barras[bid].params["cardinal"], 8)
        avisos = [a for a in P.validar(m) if a.get("codigo") == "param_desconhecido"]
        self.assertEqual(avisos, [])
        for ruim in (0, 11, "8", True, 2.5):
            with self.assertRaises(P.ErroEntrada):
                P.editar(m, "barra", bid, {"params": {"cardinal": ruim}})
        self.assertEqual(m.barras[bid].params["cardinal"], 8)


if __name__ == "__main__":
    unittest.main()
