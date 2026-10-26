"""Solda direta com as mesas soldadas (26/10/2026, pedido do usuário: W310 em W250 com solda só na alma).

Conta à mão, W 310 x 38,7 (d 310, bf 165, tw 5,8, tf 9,7 mm) na mesa de W 250 x 73, V = 100 kN, N = 50 kN, filete 5 mm E70:
  alma entre as mesas Lw = 310 − 2·9,7 = 290,6 mm; mesas: cada uma bf por fora + (bf − tw) por dentro = 2·(2·165 − 5,8) = 648,4 mm;
  contorno L = 2·290,6 + 648,4 = 1229,6 mm.
  V só na alma: 100 / (2·0,2906) = 172,06 kN/m; N no contorno todo: 50 / 1,2296 = 40,66 kN/m;
  alma: √(172,06² + 40,66²) = 176,8 kN/m ≤ FwRd 762 kN/m (0,6·0,707·5·485/1,35)."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P


def _ver(r):
    return {v["nome"]: v for v in r["verificacoes"]}


class TestSoldaMesas(unittest.TestCase):
    BASE = dict(perfil_viga="W 310 x 38,7", perfil_suporte="W 250 x 73,0", apoio="mesa_pilar", b_solda=5)

    def test_viga_i_solda_as_mesas_por_padrao(self):
        r = P.verificar_ligacao("solda_direta_alma", dict(self.BASE), {"V": 100.0, "N": 50.0})
        v = _ver(r)
        self.assertAlmostEqual(r["valores"]["Lw_mm"], 290.6, places=1)
        self.assertAlmostEqual(r["valores"]["L_contorno_mm"], 1229.6, places=1)
        self.assertAlmostEqual(v["solda alma–apoio (V, N; 2 filetes)"]["solicitante"], math.hypot(100 / 0.5812, 50 / 1.2296), delta=0.1)
        self.assertAlmostEqual(v["solda das mesas (N no contorno)"]["solicitante"], 50 / 1.2296, delta=0.05)
        self.assertTrue(any("momento" in a for a in r["avisos"]))
        s = r["geometria"]["soldas"]
        self.assertEqual(len(s), 1)
        p = s[0]["pontos"]
        self.assertEqual(len(p), 13)   # contorno do I: 12 cantos + fechamento
        ys = [q[1] for q in p]
        zs = [q[2] for q in p]
        self.assertAlmostEqual(max(ys) - min(ys), 165.0, 3)   # passa pela largura da mesa (geometria em mm)
        self.assertAlmostEqual(max(zs) - min(zs), 310.0, 3)

    def test_nao_solda_so_a_alma(self):
        r = P.verificar_ligacao("solda_direta_alma", dict(self.BASE, soldar_mesas="não"), {"V": 100.0, "N": 50.0})
        self.assertAlmostEqual(r["valores"]["Lw_mm"], 310.0, places=1)   # regra antiga: Lw = h
        self.assertNotIn("solda das mesas (N no contorno)", _ver(r))
        self.assertEqual(len(r["geometria"]["soldas"][0]["pontos"]), 5)

    def test_u_auto_fica_como_antes(self):
        r = P.verificar_ligacao("solda_direta_alma", dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="mesa_pilar", b_solda=5),
                                {"V": 45.0})
        self.assertAlmostEqual(r["valores"]["Lw_mm"], 50.0, places=1)
        self.assertEqual(len(r["geometria"]["soldas"][0]["pontos"]), 5)

    def test_u_com_mesas(self):
        r = P.verificar_ligacao("solda_direta_alma", dict(perfil_viga="UPN 120", perfil_suporte="W 200 x 26,6", apoio="mesa_pilar",
                                                          b_solda=5, soldar_mesas="sim"), {"V": 10.0})
        p = r["geometria"]["soldas"][0]["pontos"]
        self.assertEqual(len(p), 9)   # contorno do U: 8 cantos + fechamento
        self.assertIn("solda das mesas (N no contorno)", _ver(r))

    def test_cabe_na_mesa_do_pilar(self):
        r = P.verificar_ligacao("solda_direta_alma", dict(self.BASE, perfil_suporte="W 150 x 13,0"), {"V": 10.0})
        v = _ver(r)
        self.assertGreater(v["solda: largura na mesa do pilar"]["taxa"], 1.0)   # mesa 165 + 2·5 > bf 100 do W150


if __name__ == "__main__":
    unittest.main()
