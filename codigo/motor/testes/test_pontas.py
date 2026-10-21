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


def _dist(v):
    return math.sqrt(sum(x * x for x in v))


def _unit3(v):
    n = _dist(v)
    return [x / n for x in v]


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
                                                 comprimento=0.165 / 2 - PILAR["tw"] / 2 - 0.012 + 0.012,
                                                 raio=0.010)])   # do plano de corte

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

    def test_angulo_no_gusset(self):
        L2 = secoes.secao_cantoneira(0.0508, 0.0508, 0.00635).props
        s = math.sqrt(0.5)
        ms = [_m(1, X, VIGA, Z, continuo=True), _m(5, [s, 0, s], L2, [-s, 0, s])]
        r = pontas_do_no([0, 0, 0], ms, "gusset_contraventamento")[0]["recortes"]
        self.assertEqual(r[0]["tipo"], "angulo")
        self.assertVec(r[0]["normal"], [0, 0, -1])     # paralelo à face superior da viga
        self.assertAlmostEqual(r[0]["folga"], 0.012)
        self.assertAlmostEqual(r[0]["distancia"], VIGA["h"] / 2 + 0.012)

    def test_esquadria_tres_tubos(self):
        T = secoes.secao_tubo_retangular(0.1, 0.1, 0.003).props
        s = math.sqrt(0.5)
        p = pontas_do_no([0, 0, 0], [_m(1, X, T, Z), _m(2, Z, T, X), _m(3, [-s, 0, s], T, [s, 0, s])], "esquadria")
        self.assertEqual([q["recortes"][0]["vizinho"] for q in p], [2, 3, 2])
        self.assertVec(p[2]["plano"]["normal"], [x for x in _unit3([s, 0, 1 - s])])

    def test_boca_peixe_inclinada(self):
        rec = secoes.secao_tubo_circular(0.1016, 0.004).props
        ram = secoes.secao_tubo_circular(0.0508, 0.003).props
        self.assertAlmostEqual(rec["h"], 0.1016)   # tubo circular: h = diâmetro
        s = math.sqrt(0.5)
        p = pontas_do_no([0, 0, 0], [_m(1, X, rec, Z, continuo=True), _m(2, [s, 0, s], ram, [-s, 0, s])],
                         "boca_peixe")
        r = p[0]["recortes"][0]
        self.assertAlmostEqual(r["angulo"], math.pi / 4)
        self.assertAlmostEqual(r["raio"], 0.0508)
        t = _dist(p[0]["plano"]["ponto"])
        self.assertLess(t, 0.0508 / s)               # fundo da sela antes do eixo do ramal tocar o receptor
        with self.assertRaisesRegex(ValueError, "barra curta"):
            pontas_do_no([0, 0, 0], [_m(1, X, rec, Z, continuo=True), _m(2, [s, 0, s], ram, [-s, 0, s], L=0.08)],
                         "boca_peixe")

    def test_barra_curta_esquadria(self):
        T = secoes.secao_tubo_retangular(0.1, 0.1, 0.003).props
        with self.assertRaisesRegex(ValueError, "barra curta"):
            pontas_do_no([0, 0, 0], [_m(1, X, T, Z, L=0.04), _m(2, Z, T, X)], "esquadria")

    def test_cope_inf_e_duplo(self):
        e = (0.310 - VIGA["h"]) / 2
        ms = [_m(1, X, PILAR, Z, continuo=True), _m(3, Y, VIGA, Z, desloc=[0, 0, -e])]
        self.assertEqual([c["face"] for c in pontas_do_no([0, 0, 3], ms, "viga_viga")[0]["recortes"]], ["inf"])
        # viga mais alta que a mestra: as duas mesas colidem
        ms = [_m(1, X, VIGA, Z, continuo=True), _m(3, Y, PILAR, Z)]
        self.assertEqual([c["face"] for c in pontas_do_no([0, 0, 3], ms, "viga_viga")[0]["recortes"]],
                         ["sup", "inf"])
        # viga larga (bf 165) na alma de pilar estreito W150x22,5: bordas das mesas colidem
        P150 = secoes.catalogo("W150x22.5").props
        ms = [_m(1, Z, P150, X, continuo=True), _m(3, Y, PILAR, Z)]
        r = pontas_do_no([0, 0, 3], ms, "viga_pilar_alma")[0]["recortes"]
        self.assertEqual(sorted(c["face"] for c in r), ["dir", "esq"])
        self.assertAlmostEqual(r[0]["altura"], P150["tf"] + 0.012)

    def test_cope_params(self):
        e = (0.310 - VIGA["h"]) / 2
        ms = [_m(1, X, PILAR, Z, continuo=True), _m(3, Y, VIGA, Z, desloc=[0, 0, e])]
        c = pontas_do_no([0, 0, 3], ms, "viga_viga",
                         dict(cope_folga_alt=0.02, cope_folga_comp=0.03, raio_cope=0.002))[0]["recortes"][0]
        self.assertAlmostEqual(c["altura"], PILAR["tf"] + 0.02)
        self.assertAlmostEqual(c["comprimento"], 0.165 / 2 - PILAR["tw"] / 2 - 0.012 + 0.03)
        self.assertAlmostEqual(c["raio"], 0.010)

    def test_erros_claros(self):
        T = secoes.secao_tubo_retangular(0.1, 0.1, 0.003).props
        casos = [([_m(3, X, VIGA, Z)], "viga_pilar_mesa", "sem barra contínua"),
                 ([_m(1, Z, T, X, continuo=True), _m(3, Y, VIGA, Z)], "viga_pilar_alma", "sem tw"),
                 ([_m(1, X, T, Z)], "esquadria", "ao menos duas"),
                 ([_m(1, X, T, Z), _m(2, X, T, Z)], "esquadria", "colineares"),
                 ([_m(1, X, VIGA, Z, continuo=True), _m(5, X, T, Z)], "gusset_contraventamento", "paralela")]
        for ms, tipo, msg in casos:
            with self.assertRaisesRegex(ValueError, msg):
                pontas_do_no([0, 0, 0], ms, tipo)

    def test_barra_curta(self):
        ms = [_m(1, Z, PILAR, X, continuo=True), _m(3, X, VIGA, Z, L=0.15)]
        with self.assertRaisesRegex(ValueError, "barra curta demais para a ligação"):
            pontas_do_no([0, 0, 3], ms, "viga_pilar_mesa")


if __name__ == "__main__":
    unittest.main()
