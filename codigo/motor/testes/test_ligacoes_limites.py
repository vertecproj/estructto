import unittest

from prossen_estrutural.aco.ligacoes_limites import limites, T_COMERCIAL

W = dict(h=0.310, b=0.165, tw=0.0058, tf=0.0097)    # apoio: bf 165 mm, h livre 310-19,4
VIGA = dict(h=0.250, b=0.100, tw=0.005, tf=0.008)


class Limites(unittest.TestCase):
    def test_chapas_comerciais(self):
        for tipo, p in (("chapa_topo_ajustada", "tp"), ("gusset_contraventamento", "t_gusset"),
                        ("chapa_simples_alma", "t_chapa")):
            f = limites(tipo, {})[p]
            mn, mx, passos = f['min'], f['max'], f['passos']
            self.assertEqual((mn, mx), (4.75, 50.0))
            self.assertEqual(passos, T_COMERCIAL)
            self.assertIn(25.0, passos)

    def test_chapa_topo_ate_bf_apoio(self):
        f = limites("chapa_topo_ajustada", dict(perfil=VIGA, perfil_apoio=W))["bp"]
        self.assertAlmostEqual(f["max"], 165.0)
        self.assertEqual(f["unidade"], "mm")
        self.assertLess(f["min"], f["max"])

    def test_espacamento_3d_e_borda_tabela14(self):
        lim = limites("chapa_simples_alma", {}, dict(d=19.05))
        self.assertAlmostEqual(lim["s"]["min"], 3 * 19.05)         # 6.3.9
        self.assertEqual(lim["s"]["max"], 300.0)                   # 6.3.12
        self.assertEqual(lim["e_v"]["min"], 32.0)                  # Tab. 14: 3/4" cortada com serra
        self.assertEqual(lim["e_v"]["max"], 150.0)
        self.assertEqual(limites("chapa_simples_alma", {}, dict(d=22.225))["e_v"]["min"], 38.0)   # 7/8"

    def test_gusset_comprimentos_livres_e_minimo(self):
        m = dict(perfil_apoio=W, comp_viga=4.0, comp_pilar=3.2)
        lim = limites("gusset_contraventamento", m, dict(apoio="viga-pilar"))
        self.assertEqual(lim["Lh"]["max"], 4000.0)
        self.assertEqual(lim["Lv"]["max"], 3200.0)
        self.assertGreaterEqual(lim["Lh"]["min"], 50.0)   # mínimo geométrico, nunca o 0 automático
        alma = limites("gusset_contraventamento", m, dict(gusset_na_alma=True))
        self.assertAlmostEqual(alma["Lv"]["max"], 310 - 19.4 - 24)

    def test_unidades_coerentes(self):
        pb = limites("placa_base", dict(perfil=W), dict(borda=0.05))
        self.assertEqual(pb["H"]["unidade"], "m")
        self.assertAlmostEqual(pb["H"]["min"], 0.410)
        self.assertEqual(pb["esp"]["unidade"], "m")
        self.assertEqual(pb["diametro_mm"]["unidade"], "mm")
        self.assertAlmostEqual(pb["borda"]["min"] * 1000, limites("chapa_simples_alma", {}, dict(d=16))["e_v"]["min"])

    def test_t_limita_espacamento_e_borda(self):
        lim = limites("chapa_simples_alma", {}, dict(d=19.05, t=6.3))
        self.assertAlmostEqual(lim["s"]["max"], 151.2)
        self.assertAlmostEqual(lim["e_v"]["max"], 75.6)

    def test_tipo_desconhecido(self):
        self.assertEqual(limites("nao_existe", {}), {})


if __name__ == "__main__":
    unittest.main()
