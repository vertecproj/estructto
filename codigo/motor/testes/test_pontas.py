import math
import unittest

from prossen_estrutural import secoes
from prossen_estrutural.aco.pontas import pontas_do_no

PILAR = secoes.catalogo("W310x38.7").props
VIGA = secoes.catalogo("W250x25.3").props
Z, X, Y = [0, 0, 1], [1, 0, 0], [0, 1, 0]


def _m(barra, d, perfil, eixo_d, L=5.0, continuo=False, ext="i", **kw):
    return dict(barra=barra, extremo=ext, dir=d, comprimento=L, perfil=perfil, eixo_d=eixo_d,
                continuo=continuo, **kw)


class TestPontas(unittest.TestCase):
    def assertVec(self, a, b):
        for x, y in zip(a, b):
            self.assertAlmostEqual(x, y, places=6)

    def test_viga_na_mesa_do_pilar(self):
        # pilar com alma no plano XZ (altura d ao longo de X): viga em X chega na mesa
        ms = [_m(1, Z, PILAR, X, continuo=True), _m(2, [0, 0, -1], PILAR, X, continuo=True, ext="j"),
              _m(3, X, VIGA, Z)]
        p = pontas_do_no([0, 0, 3], ms, "viga_pilar_mesa")
        self.assertEqual([q["barra"] for q in p], [3])
        self.assertVec(p[0]["plano"]["ponto"], [0.310 / 2 + 0.012, 0, 3])
        self.assertVec(p[0]["plano"]["normal"], [-1, 0, 0])
        self.assertEqual(p[0]["recortes"], [])

    def test_viga_na_alma(self):
        # viga em Y chega na alma do pilar (d do pilar em X): mesas não colidem
        ms = [_m(1, Z, PILAR, X, continuo=True), _m(3, Y, VIGA, Z)]
        p = pontas_do_no([0, 0, 3], ms, "viga_pilar_alma")
        self.assertVec(p[0]["plano"]["ponto"], [0, 0.0058 / 2 + 0.012, 3])
        self.assertEqual(p[0]["recortes"], [])
        # viga secundária na alma de viga mestra, topos alinhados: mesa superior colide → cope
        e = (0.310 - VIGA["h"]) / 2
        ms = [_m(1, X, PILAR, Z, continuo=True), _m(3, Y, VIGA, Z, desloc=[0, 0, e])]
        p = pontas_do_no([0, 0, 3], ms, "viga_viga")
        self.assertEqual(p[0]["recortes"], [dict(tipo="cope", face="sup", altura=PILAR["tf"] + 0.012,
                                                 comprimento=0.165 / 2 + 0.012, raio=0.010)])

    def test_diagonal_no_gusset(self):
        L2 = secoes.secao_cantoneira(0.0508, 0.0508, 0.00635).props
        s = math.sqrt(0.5)
        L = 0.6   # 0,3·L = 0,18 < recuo: sem limite
        ms = [_m(1, X, VIGA, Z, continuo=True), _m(2, [-1, 0, 0], VIGA, Z, continuo=True, ext="j"),
              _m(5, [s, 0, s], L2, [-s, 0, s], L=L)]
        p = pontas_do_no([0, 0, 0], ms, "gusset_contraventamento")
        recuo = (VIGA["h"] / 2) / s + 0.0508 / 2 + 0.012
        self.assertGreater(recuo, 0.3 * L)
        self.assertVec(p[0]["plano"]["ponto"], [recuo * s, 0, recuo * s])
        self.assertVec(p[0]["plano"]["normal"], [-s, 0, -s])

    def test_esquadria_tubos(self):
        T = secoes.secao_tubo_retangular(0.1, 0.1, 0.003).props
        p = pontas_do_no([0, 0, 0], [_m(1, X, T, Z), _m(2, Z, T, X)], "esquadria")
        s = math.sqrt(0.5)
        self.assertEqual([q["recortes"][0]["tipo"] for q in p], ["esquadria"] * 2)
        self.assertVec(p[0]["plano"]["normal"], [-s, 0, s])
        self.assertVec(p[1]["plano"]["normal"], [s, 0, -s])
        self.assertVec(p[0]["plano"]["ponto"], [0, 0, 0])

    def test_boca_peixe(self):
        rec = secoes.secao_tubo_circular(0.1016, 0.004).props
        ram = secoes.secao_tubo_circular(0.0508, 0.003).props
        p = pontas_do_no([0, 0, 0], [_m(1, X, rec, Z, continuo=True), _m(2, Z, ram, X)], "boca_peixe")
        self.assertEqual(p[0]["recortes"][0]["tipo"], "boca_peixe")
        self.assertAlmostEqual(p[0]["recortes"][0]["raio"], 0.0508)

    def test_barra_curta(self):
        ms = [_m(1, Z, PILAR, X, continuo=True), _m(3, X, VIGA, Z, L=0.15)]
        with self.assertRaisesRegex(ValueError, "barra curta demais para a ligação"):
            pontas_do_no([0, 0, 3], ms, "viga_pilar_mesa")


if __name__ == "__main__":
    unittest.main()
