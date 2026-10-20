"""Verificação independente da carga distribuída linear (CargaDistribuida.q2), commit 21d3623.

Valores esperados derivados à mão (método das forças / tabelas clássicas), não copiados do solver:
- biengastada, triangular 0 -> w: M_0 = wL²/30, M_L = wL²/20 (Euler). Timoshenko: parte simétrica wL²/24
  (independe de Φ) e antissimétrica wL²/(120(1+Φ)) -> M_L - M_0 = wL²/(60(1+Φ)).
- flecha no meio (Timoshenko): wL⁴/(768EI) + M_bi(L/2)/(G·As) = wL⁴/(768EI) + wL²/(16 G As).
- propped (engaste no 0, apoio no máximo): M_eng = 7wL²/120; (engaste no máximo): M_eng = wL²/15.
- balanço, 0 no engaste e w na ponta: M = wL²/3, flecha 11wL⁴/(120EI); w no engaste e 0 na ponta: wL⁴/(30EI).
- barra inclinada (0,0,0)-(3,0,4), carga global vertical 0 -> w: axial 0,8w, perpendicular 0,6w.
"""
import os
import sys
import unittest

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from test_solver import base, ponto, E, G, I, ENG  # noqa: E402
from prossen_estrutural.modelo import No, Barra, CargaDistribuida  # noqa: E402
from prossen_estrutural.solver import analisar  # noqa: E402

LIVRE = (False,) * 6
APOIO = (True, True, True, True, False, False)


def viga(apoio0, apoio1, q, q2, L=6.0, params=None, props=None, fim=None):
    m = base()
    m.add(No("n0", 0, 0, 0, apoio=apoio0))
    m.add(No("n1", *(fim or (L, 0, 0)), apoio=apoio1))
    m.add(Barra("b0", "n0", "n1", "s", "aco", params=dict(params or {})))
    if props:
        m.secoes["s"].props = props
    m.add(CargaDistribuida("G", "b0", q, q2=q2))
    return analisar(m, "G")


class TestVerifCargaLinear(unittest.TestCase):
    L, w = 6.0, 12.0

    def test_timoshenko_biengastada(self):
        L, w, Az = self.L, self.w, 6e-4
        ph = 12 * E * I / (G * Az * L * L)
        r = viga(ENG, ENG, (0, 0, 0), (0, 0, -w), params={"cortante": True}, props={"Ay": 2e-4, "Az": Az})
        m0, m1 = abs(r.reacoes["n0"][4]), abs(r.reacoes["n1"][4])
        self.assertAlmostEqual(m0 + m1, w * L * L / 12, delta=1e-9)
        self.assertAlmostEqual(m1 - m0, w * L * L / (60 * (1 + ph)), delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n0"][2] + r.reacoes["n1"][2], w * L / 2, delta=1e-9)
        _, fle = ponto(r, "b0", L / 2)
        esperado = w * L**4 / (768 * E * I) + w * L * L / (16 * G * Az)
        self.assertAlmostEqual(fle[0][3] / -esperado, 1.0, delta=1e-9)

    def test_plano_xy_soma_igual_uniforme(self):
        """Eixo y local (carga global Y): triangular 0->w + w->0 = uniforme w (Euler e Timoshenko)."""
        for params, props in (({}, None), ({"cortante": True}, {"Ay": 2e-4, "Az": 6e-4})):
            a = viga(ENG, ENG, (0, 0, 0), (0, self.w, 0), params=params, props=props)
            b = viga(ENG, ENG, (0, self.w, 0), (0, 0, 0), params=params, props=props)
            u = viga(ENG, ENG, (0, self.w, 0), None, params=params, props=props)
            for n in ("n0", "n1"):
                np.testing.assert_allclose(np.add(a.reacoes[n], b.reacoes[n]), u.reacoes[n], atol=1e-9)
            for ra, rb, ru in zip(a.esforcos["b0"], b.esforcos["b0"], u.esforcos["b0"]):
                np.testing.assert_allclose(np.add(ra[1:], rb[1:]), ru[1:], atol=1e-9)
            for ra, rb, ru in zip(a.flechas["b0"], b.flechas["b0"], u.flechas["b0"]):
                np.testing.assert_allclose(np.add(ra[1:], rb[1:]), ru[1:], atol=1e-15)
            self.assertGreater(abs(a.reacoes["n1"][5]), abs(a.reacoes["n0"][5]))  # maior momento no lado de w

    def test_propped(self):
        L, w = self.L, self.w
        r = viga(ENG, APOIO, (0, 0, 0), (0, 0, -w))
        self.assertAlmostEqual(abs(r.reacoes["n0"][4]), 7 * w * L * L / 120, delta=1e-9)
        r = viga(APOIO, ENG, (0, 0, 0), (0, 0, -w))
        self.assertAlmostEqual(abs(r.reacoes["n1"][4]), w * L * L / 15, delta=1e-9)

    def test_balanco(self):
        L, w = self.L, self.w
        r = viga(ENG, LIVRE, (0, 0, 0), (0, 0, -w))
        self.assertAlmostEqual(abs(r.reacoes["n0"][4]), w * L * L / 3, delta=1e-9)
        self.assertAlmostEqual(r.deslocamentos["n1"][2], -11 * w * L**4 / (120 * E * I), delta=1e-12)
        r = viga(ENG, LIVRE, (0, 0, -w), (0, 0, 0))
        self.assertAlmostEqual(r.deslocamentos["n1"][2], -w * L**4 / (30 * E * I), delta=1e-12)
        # meio do balanço (0 no engaste): EIv = w x²(20L³ - 10L²x + x³)/(120L) com x = L/2
        r = viga(ENG, LIVRE, (0, 0, 0), (0, 0, -w))
        _, fle = ponto(r, "b0", L / 2)
        x = L / 2
        self.assertAlmostEqual(fle[0][3] / (-w * x * x * (20 * L**3 - 10 * L * L * x + x**3) / (120 * L * E * I)),
                               1.0, delta=1e-9)

    def test_inclinada_biengastada(self):
        w = self.w
        r = viga(ENG, ENG, (0, 0, 0), (0, 0, -w), fim=(3, 0, 4))
        np.testing.assert_allclose(r.reacoes["n0"][:3], (0.48, 0, 9.64), atol=1e-9)
        np.testing.assert_allclose(r.reacoes["n1"][:3], (-0.48, 0, 20.36), atol=1e-9)
        self.assertAlmostEqual(r.reacoes["n0"][4], -6.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][4], 9.0, delta=1e-9)

    def test_trelica_flecha_meio(self):
        """Treliça biapoiada: flecha interna no meio 5wL⁴/(768EI) da triangular."""
        L, w = self.L, self.w
        r = viga((True,) * 6, (True,) * 3 + (False,) * 3, (0, 0, 0), (0, 0, -w), params={"trelica": True})
        _, fle = ponto(r, "b0", L / 2)
        self.assertAlmostEqual(fle[0][3] / (-5 * w * L**4 / (768 * E * I)), 1.0, delta=1e-9)

    def test_q2_igual_q_igual_uniforme(self):
        a = viga(ENG, APOIO, (1.0, 2.0, -3.0), None)
        b = viga(ENG, APOIO, (1.0, 2.0, -3.0), (1.0, 2.0, -3.0))
        for n in ("n0", "n1"):
            np.testing.assert_allclose(a.reacoes[n], b.reacoes[n], rtol=0, atol=1e-12)


if __name__ == "__main__":
    unittest.main()
