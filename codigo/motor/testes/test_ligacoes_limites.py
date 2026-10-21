import unittest

from prossen_estrutural.aco.ligacoes_limites import limites, T_COMERCIAL

W = dict(h=0.310, b=0.165, tw=0.0058, tf=0.0097)    # apoio: bf 165 mm, h livre 310-19,4
VIGA = dict(h=0.250, b=0.100, tw=0.005, tf=0.008)


class Limites(unittest.TestCase):
    def test_chapas_comerciais(self):
        for tipo, p in (("chapa_topo_ajustada", "tp"), ("gusset_contraventamento", "t_gusset"),
                        ("chapa_simples_alma", "t_chapa")):
            mn, mx, passos = limites(tipo, [])[p]
            self.assertEqual((mn, mx), (4.75, 50.0))
            self.assertEqual(passos, T_COMERCIAL)
            self.assertIn(25.0, passos)

    def test_chapa_topo_ate_bf_apoio(self):
        mn, mx, _ = limites("chapa_topo_ajustada", [dict(perfil=VIGA, perfil_apoio=W)])["bp"]
        self.assertAlmostEqual(mx, 165.0)
        self.assertLess(mn, mx)

    def test_menor_apoio_vence(self):
        w2 = dict(W, b=0.120)
        _, mx, _ = limites("chapa_topo_ajustada", [dict(perfil_apoio=W), dict(perfil_apoio=w2)])["bp"]
        self.assertAlmostEqual(mx, 120.0)

    def test_espacamento_3d_e_borda_tabela14(self):
        lim = limites("chapa_simples_alma", [], dict(d=19.05))
        self.assertAlmostEqual(lim["s"][0], 3 * 19.05)         # 6.3.9
        self.assertEqual(lim["s"][1], 300.0)                   # 6.3.12
        self.assertEqual(lim["e_v"][0], 32.0)                  # Tab. 14: 3/4" cortada com serra
        self.assertEqual(lim["e_v"][1], 150.0)
        self.assertEqual(limites("chapa_simples_alma", [], dict(d=22.225))["e_v"][0], 38.0)   # 7/8"

    def test_gusset_folga_as_mesas(self):
        _, mx, _ = limites("gusset_contraventamento", [dict(perfil_apoio=W)])["Lv"]
        self.assertAlmostEqual(mx, 310 - 19.4 - 24)

    def test_placa_base_pilar_mais_2_bordas(self):
        lim = limites("placa_base", [dict(perfil=W)], dict(borda=0.05))
        self.assertAlmostEqual(lim["H"][0], 0.310 + 0.10)
        self.assertAlmostEqual(lim["B"][0], 0.165 + 0.10)
        self.assertEqual(lim["esp"][2][0], 0.00475)

    def test_tipo_desconhecido(self):
        self.assertEqual(limites("nao_existe", []), {})


if __name__ == "__main__":
    unittest.main()
