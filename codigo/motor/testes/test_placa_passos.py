"""Placa de base: cada passo na sua pior combinação e o n da solda (pedidos do Bruno, origin/main be0a9e2 e 0cefecc)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural import projeto as P
from prossen_estrutural.aco import placa_base as pb

ESF = [{"nome": "tracao", "N": 60, "V": 5, "M": 0}, {"nome": "compr_momento", "N": -400, "V": 10, "M": 40},
       {"nome": "corte", "N": -10, "V": 60, "M": 2}]
CORPO = {"dados": {"pelo_perfil": True}, "secao": "W 250 x 32,7"}


class TestPassos(unittest.TestCase):
    def test_cada_passo_na_sua_pior_combinacao(self):
        r = P.placa_base_verificar(None, None, dict(CORPO, esforcos=ESF))
        ps = r["passos"]
        self.assertNotEqual(ps["fixacao"]["combinacao"], ps["chapa"]["combinacao"])  # trocar a fixação não esconde a chapa
        for passo, x in ps.items():
            so = next(e for e in ESF if e["nome"] == x["combinacao"])
            r1 = P.placa_base_verificar(None, None, dict(CORPO, esforcos=[so]))  # a combinação sozinha
            eixo = r1["passos"][passo]["eixo"]
            self.assertEqual(eixo, x["eixo"])
            esperado = r1["passos"][passo]["verificacoes"]
            self.assertEqual([v["nome"] for v in x["verificacoes"]], [v["nome"] for v in esperado])
            for a, b in zip(x["verificacoes"], esperado):
                self.assertAlmostEqual(a["taxa"], b["taxa"], 12)
            self.assertTrue(all(P._passo_placa(v["nome"]) == passo for v in x["verificacoes"]))
        self.assertIn("seguranca", ps["solda"]["solda"])

    def test_n_da_solda(self):
        """n = 0,577·FEXX/τ ≥ 0,577/0,30: mesma condição que τ ≤ 0,30·FEXX (só informativo)."""
        tc = {"tipo": "tubo_circular", "h": 0.1143, "b": 0.1143, "t": 0.0073, "tw": 0.0073, "tf": 0.0073}
        for e in (dict(N=-8.87, V=.5, M=2.32), dict(N=-80, V=20, M=25)):
            r = pb.verificar_solda_contorno(tc, dict(solda=.005, eletrodo="E70", esp=.016), e)
            s = r["seguranca"]
            self.assertAlmostEqual(s["n"], .577 * (145 / .30) / (r["tau"] / 1000), 9)  # FEXX = 145/0,30 = 483 MPa
            self.assertAlmostEqual(s["n"], r["NEquiv"], 12)
            self.assertAlmostEqual(s["n_min"], .577 / .30, 12)
            self.assertEqual(s["passa"], r["Ap"] <= 1 + 1e-9)
            self.assertTrue(s["informativo"])


if __name__ == "__main__":
    unittest.main()
