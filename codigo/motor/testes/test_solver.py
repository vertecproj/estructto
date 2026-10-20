"""Testes do solver contra soluções analíticas clássicas (resistência dos materiais)."""
import math
import os
import sys
import unittest

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural.modelo import (Modelo, No, Material, Secao, Barra, CasoCarga,
                                       CargaNodal, CargaDistribuida, CargaPontual, Pavimento)
from prossen_estrutural.solver import (analisar, combinar, modal, ErroMecanismo, eixos_locais,
                                       pesos_nodais)

E, G = 200e6, 77e6
A, I, J = 0.01, 1e-4, 2e-4
ENG = (True,) * 6
ROT = (False, False, False, True, True, True)


def base(peso=0.0, A_=A):
    m = Modelo()
    m.add(Material("aco", E, G, peso))
    m.add(Secao("s", A_, I, I, J))
    m.add(CasoCarga("G", "G"))
    m.add(CasoCarga("Q", "Q"))
    return m


def viga(m, pts, apoios, pref="n"):
    for k, (p, a) in enumerate(zip(pts, apoios)):
        m.add(No(f"{pref}{k}", *p, apoio=a))
    for k in range(len(pts) - 1):
        m.add(Barra(f"b{pref}{k}", f"{pref}{k}", f"{pref}{k+1}", "s", "aco"))


class TestSolver(unittest.TestCase):
    def test_biapoiada_distribuida(self):
        L, q, n = 6.0, 10.0, 4
        m = base()
        livre = (False,) * 6
        apoios = [(True, True, True, True, False, False)] + [livre] * (n - 1) + [(False, True, True, False, False, False)]
        viga(m, [(L * k / n, 0, 0) for k in range(n + 1)], apoios)
        for b in m.barras:
            m.add(CargaDistribuida("G", b, (0, 0, -q)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["n2"][2], -5 * q * L**4 / (384 * E * I), delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n0"][2], q * L / 2, places=6)
        self.assertAlmostEqual(r.reacoes[f"n{n}"][2], q * L / 2, places=6)
        Mmax = max(abs(p[5]) for b in r.esforcos.values() for p in b)
        self.assertAlmostEqual(Mmax, q * L**2 / 8, places=6)
        # flecha interna ao elemento (meio da 1ª barra, x = L/8)
        x = L / 8
        w = q * x * (L**3 - 2 * L * x**2 + x**3) / (24 * E * I)
        self.assertAlmostEqual(r.flechas["bn0"][len(r.flechas["bn0"]) // 2][3], -w, delta=1e-10)

    def test_engastada_carga_ponta(self):
        L, P = 3.0, 5.0
        m = base()
        viga(m, [(0, 0, 0), (L, 0, 0)], [ENG, (False,) * 6])
        m.add(CargaNodal("G", "n1", (0, 0, -P, 0, 0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["n1"][2], -P * L**3 / (3 * E * I), delta=1e-10)
        self.assertAlmostEqual(abs(r.esforcos["bn0"][0][5]), P * L, places=6)

    def test_fator_EI(self):
        # NBR 6118 15.7.3 (rigidez reduzida p/ γz): fator multiplica só E·Iy e E·Iz.
        # Flecha de engastada P·L³/(3·f·E·I); axial P·L/(EA) e giro T·L/(GJ) inalterados.
        L, P, q, f = 3.0, 5.0, 2.0, 0.4
        m = base()
        viga(m, [(0, 0, 0), (L, 0, 0)], [ENG, (False,) * 6])
        m.barras["bn0"].params["fator_EI"] = f
        m.add(CargaNodal("G", "n1", (P, P, -P, P, 0, 0)))
        m.add(CargaDistribuida("Q", "bn0", (0, 0, -q)))
        u = analisar(m, "G").deslocamentos["n1"]
        self.assertAlmostEqual(u[0], P * L / (E * A), delta=1e-12)
        self.assertAlmostEqual(u[1], P * L**3 / (3 * f * E * I), delta=1e-10)
        self.assertAlmostEqual(u[2], -P * L**3 / (3 * f * E * I), delta=1e-10)
        self.assertAlmostEqual(u[3], P * L / (G * J), delta=1e-12)
        # flecha interna (solução particular) também escala: q·x²(6L²-4Lx+x²)/(24·f·EI)
        x = L / 2
        w = q * x**2 * (6 * L**2 - 4 * L * x + x**2) / (24 * f * E * I)
        r = analisar(m, "Q")
        self.assertAlmostEqual(r.flechas["bn0"][len(r.flechas["bn0"]) // 2][3], -w, delta=1e-10)
        for ruim in (0.0, -1.0, float("nan"), float("inf")):
            m.barras["bn0"].params["fator_EI"] = ruim
            with self.assertRaisesRegex(ValueError, "bn0"):
                analisar(m, "G")

    def test_viga_girada_no_espaco(self):
        L, P = 4.0, 7.0
        d = np.array([1.0, 2.0, -0.7]); d /= np.linalg.norm(d)
        p = np.cross(d, [0.3, -0.5, 0.8]); p /= np.linalg.norm(p)  # perpendicular à barra
        for rot in (0.0, 0.6):
            m = base()
            viga(m, [(0, 0, 0), tuple(L * d)], [ENG, (False,) * 6])
            m.barras["bn0"].rot = rot
            m.add(CargaNodal("G", "n1", (*(P * p), 0, 0, 0)))
            u = analisar(m, "G").deslocamentos["n1"][:3]
            np.testing.assert_allclose(u, P * L**3 / (3 * E * I) * p, atol=1e-10)

    def test_portico_L(self):
        # pilar vertical H engastado + viga L em balanço, carga P na ponta
        H, L, P = 3.0, 2.0, 4.0
        m = base(A_=1e3)  # área grande: despreza deformação axial
        m.add(No("a", 0, 0, 0, apoio=ENG)); m.add(No("b", 0, 0, H)); m.add(No("c", L, 0, H))
        m.add(Barra("p", "a", "b", "s", "aco")); m.add(Barra("v", "b", "c", "s", "aco"))
        m.add(CargaNodal("G", "c", (0, 0, -P, 0, 0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["c"][2], -(P * L**3 / 3 + P * L**2 * H) / (E * I), delta=1e-10)
        self.assertAlmostEqual(r.deslocamentos["b"][0], P * L * H**2 / (2 * E * I), delta=1e-10)
        self.assertAlmostEqual(r.reacoes["a"][4], -P * L, places=6)  # momento de base

    def test_trelica_isostatica(self):
        # treliça triangular no plano xz, nós com giros e uy restritos
        L, h, P = 4.0, 3.0, 10.0
        m = base()
        m.add(No("a", 0, 0, 0, apoio=(True, True, True, False, False, False)))
        m.add(No("b", L, 0, 0, apoio=(False, True, True, False, False, False)))
        m.add(No("c", L / 2, 0, h, apoio=(False, True, False, False, False, False)))
        for bid, i, j in (("ab", "a", "b"), ("ac", "a", "c"), ("bc", "b", "c")):
            m.add(Barra(bid, i, j, "s", "aco", params={"trelica": True}))
        m.add(CargaNodal("G", "c", (0, 0, -P, 0, 0, 0)))
        r = analisar(m, "G")
        lc = np.hypot(L / 2, h)
        self.assertAlmostEqual(r.esforcos["ac"][0][1], -P / 2 * lc / h, places=6)
        self.assertAlmostEqual(r.esforcos["ab"][0][1], P / 2 * (L / 2) / h, places=6)
        self.assertAlmostEqual(r.reacoes["a"][2], P / 2, places=6)
        # carga num GL sem rigidez -> mecanismo
        m.add(CargaNodal("Q", "c", (0, 0, 0, 1.0, 0, 0)))
        with self.assertRaisesRegex(ErroMecanismo, "nó c, GL rx"):
            analisar(m, "Q")

    def test_torcao(self):
        L, T = 2.5, 3.0
        m = base()
        viga(m, [(0, 0, 0), (0, L, 0)], [ENG, (False,) * 6])
        m.add(CargaNodal("G", "n1", (0, 0, 0, 0, T, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["n1"][4], T * L / (G * J), delta=1e-12)
        self.assertAlmostEqual(r.esforcos["bn0"][3][4], T, places=6)

    def test_mecanismo(self):
        m = base()
        viga(m, [(0, 0, 0), (2, 0, 0)], [(True, True, True, False, False, False), (False,) * 6])
        m.add(CargaNodal("G", "n1", (0, 0, -1, 0, 0, 0)))
        with self.assertRaisesRegex(ErroMecanismo, r"nó n[01], GL (u[yz]|r[xyz])"):  # giros de corpo rígido
            analisar(m, "G")

    def test_frequencia_biapoiada(self):
        L, n, gama = 8.0, 40, 78.5
        m = base(peso=gama)
        m.peso_proprio = "G"
        livre = (False,) * 6
        apoios = [(True, True, True, True, False, False)] + [livre] * (n - 1) + [(False, True, True, False, False, False)]
        viga(m, [(L * k / n, 0, 0) for k in range(n + 1)], apoios)
        f, modos, part = modal(m, 3, {"G": 1.0})
        mu = gama * A / 9.80665
        f1 = np.pi / (2 * L**2) * np.sqrt(E * I / mu)
        self.assertAlmostEqual(f[0] / f1, 1.0, delta=0.02)
        self.assertGreater(max(part[0][1:]), 0.75)  # ~8/π² da massa no 1º modo

    def test_equilibrio_portico_3d(self):
        m = base(peso=78.5)
        m.peso_proprio = "G"
        nos = {"a": (0, 0, 0), "b": (5, 0, 0), "c": (5, 4, 0), "d": (0, 4, 0)}
        for k, p in nos.items():
            m.add(No(k, *p, apoio=ENG)); m.add(No(k + "t", p[0], p[1], 3.0))
        for k in nos:
            m.add(Barra("P" + k, k, k + "t", "s", "aco", rot=0.3))
        for i, j in (("a", "b"), ("b", "c"), ("c", "d"), ("d", "a"), ("a", "c")):
            m.add(Barra("V" + i + j, i + "t", j + "t", "s", "aco"))
        m.add(CargaDistribuida("G", "Vab", (1.0, 2.0, -15.0)))
        m.add(CargaDistribuida("G", "Vac", (0, 0, -8.0)))
        m.add(CargaNodal("G", "ct", (12.0, -5.0, -20.0, 1, 2, 3)))
        r = analisar(m, "G")
        # carga total
        tot = np.array([12.0, -5.0, -20.0])
        tot += 5 * np.array([1.0, 2.0, -15.0]) + np.hypot(5, 4) * np.array([0, 0, -8.0])
        comp = 4 * 3 + 5 + 4 + 5 + 4 + np.hypot(5, 4)
        tot[2] -= 78.5 * A * comp
        reac = sum(v[:3] for v in r.reacoes.values())
        np.testing.assert_allclose(reac, -tot, atol=1e-6)
        # combinação linear e P-Δ (compressão aumenta deslocamento lateral)
        c = combinar({"G": r}, {"G": 1.4})
        np.testing.assert_allclose(c.deslocamentos["ct"], 1.4 * r.deslocamentos["ct"])
        rp = analisar(m, "G", pdelta=True)
        self.assertGreater(abs(rp.deslocamentos["ct"][0]), abs(r.deslocamentos["ct"][0]))


# ---------------------------------------------------------------- extensões (ficha 001)

KIP, POL, PE = 4.4482216152605, 0.0254, 0.3048  # kN, m, m
KSI = KIP / POL**2                               # kN/m²


def _pos(m, n):
    return np.array([m.nos[n].x, m.nos[n].y, m.nos[n].z], float)


def cargas_totais(m, fatores):
    """Resultante das cargas aplicadas (força e momento em relação à origem), só pela geometria."""
    F, M = np.zeros(3), np.zeros(3)
    for c in m.cargas_nodais:
        g = fatores.get(c.caso, 0.0)
        f = g * np.array(c.F[:3], float)
        F += f
        M += np.cross(_pos(m, c.no), f) + g * np.array(c.F[3:], float)
    trechos = [(c.caso, c.barra, np.array(c.q, float)) for c in m.cargas_distribuidas]
    if m.peso_proprio in fatores:
        for bid, b in m.barras.items():
            trechos.append((m.peso_proprio, bid, np.array(
                [0, 0, -m.secoes[b.secao].A * m.materiais[b.material].peso])))
    for caso, bid, q in trechos:
        b = m.barras[bid]
        pi, pj = _pos(m, b.ni), _pos(m, b.nj)
        f = fatores.get(caso, 0.0) * q * np.linalg.norm(pj - pi)
        F += f
        M += np.cross((pi + pj) / 2, f)
    for c in m.cargas_pontuais:
        b = m.barras[c.barra]
        pi, pj = _pos(m, b.ni), _pos(m, b.nj)
        f = fatores.get(c.caso, 0.0) * np.array(c.F, float)
        F += f
        M += np.cross(pi + c.a * (pj - pi) / np.linalg.norm(pj - pi), f)
    return F, M


def checar_equilibrio(tc, m, r, fatores, tol=1e-9):
    """ΣF aplicadas + ΣR = 0 nas 6 componentes (momentos em relação à origem)."""
    F, M = cargas_totais(m, fatores)
    RF = sum((np.asarray(v[:3]) for v in r.reacoes.values()), np.zeros(3))
    RM = sum((np.asarray(v[3:]) + np.cross(_pos(m, n), v[:3]) for n, v in r.reacoes.items()),
             np.zeros(3))
    esc = max(np.linalg.norm(F), np.linalg.norm(M), 1.0)
    tc.assertLess(np.linalg.norm(F + RF) / esc, tol)
    tc.assertLess(np.linalg.norm(M + RM) / esc, tol)


def ponto(r, barra, x):
    """Linhas de esforço e de flecha na abscissa x."""
    esf = [p for p in r.esforcos[barra] if abs(p[0] - x) < 1e-12]
    fle = [p for p in r.flechas[barra] if abs(p[0] - x) < 1e-12]
    return esf, fle


class TestCargaPontual(unittest.TestCase):
    L, a, P, Px = 6.0, 2.0, 10.0, 3.0

    def biapoiada(self):
        m = base()
        m.add(No("n0", 0, 0, 0, apoio=(True, True, True, True, False, False)))
        m.add(No("n1", self.L, 0, 0, apoio=(False, True, True, False, False, False)))
        m.add(Barra("b0", "n0", "n1", "s", "aco"))
        m.add(CargaPontual("G", "b0", self.a, (self.Px, 0, -self.P)))
        return m

    def test_viga_biapoiada(self):
        """Resistência dos materiais (Gere & Timoshenko, Mecânica dos Sólidos, apêndice G,
        viga simples com carga concentrada): M(a) = P·a·b/L; flecha em a = P·a²·b²/(3EIL);
        R = P·b/L e P·a/L. Tol. 1e-9 rel."""
        L, a, P = self.L, self.a, self.P
        b = L - a
        m = self.biapoiada()
        r = analisar(m, "G")
        esf, fle = ponto(r, "b0", a)
        self.assertEqual(len(esf), 2)  # a⁻ e a⁺
        for e in esf:
            self.assertAlmostEqual(e[5] / (-P * a * b / L), 1.0, delta=1e-9)  # My < 0 no vão
        self.assertAlmostEqual(fle[0][3] / (-P * a**2 * b**2 / (3 * E * I * L)), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n0"][2] / (P * b / L), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][2] / (P * a / L), 1.0, delta=1e-9)
        # salto de V no ponto (Vz: -P·b/L → +P·a/L) e axial só entre ni e a
        self.assertAlmostEqual(esf[1][3] - esf[0][3], P, delta=1e-9 * P)
        self.assertAlmostEqual(esf[0][1], self.Px, delta=1e-9)
        self.assertAlmostEqual(esf[1][1], 0.0, delta=1e-9)
        self.assertAlmostEqual(r.esforcos["b0"][0][1], self.Px, delta=1e-9)
        self.assertAlmostEqual(r.esforcos["b0"][-1][1], 0.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_igual_a_duas_barras_com_carga_nodal(self):
        """Mesma carga como nó intermediário + carga nodal: mesmos deslocamentos e reações."""
        r1 = analisar(self.biapoiada(), "G")
        m = base()
        m.add(No("n0", 0, 0, 0, apoio=(True, True, True, True, False, False)))
        m.add(No("na", self.a, 0, 0))
        m.add(No("n1", self.L, 0, 0, apoio=(False, True, True, False, False, False)))
        m.add(Barra("b0", "n0", "na", "s", "aco")); m.add(Barra("b1", "na", "n1", "s", "aco"))
        m.add(CargaNodal("G", "na", (self.Px, 0, -self.P, 0, 0, 0)))
        r2 = analisar(m, "G")
        for n in ("n0", "n1"):
            np.testing.assert_allclose(r1.deslocamentos[n], r2.deslocamentos[n], rtol=1e-9, atol=1e-15)
            np.testing.assert_allclose(r1.reacoes[n], r2.reacoes[n], rtol=1e-9, atol=1e-9)
        _, fle = ponto(r1, "b0", self.a)
        np.testing.assert_allclose(fle[0][1:], r2.deslocamentos["na"][:3], rtol=1e-9, atol=1e-15)

    def test_barra_inclinada_componente_axial(self):
        """Carga em barra inclinada (balanço) tem componente axial: equilíbrio global e
        esforços nulos entre o ponto de carga e a ponta livre."""
        m = base()
        m.add(No("a", 0, 0, 0, apoio=ENG)); m.add(No("b", 3, 2, 4))
        m.add(Barra("ab", "a", "b", "s", "aco", rot=0.4))
        m.add(CargaPontual("G", "ab", 2.0, (1.0, -2.0, -15.0)))
        r = analisar(m, "G")
        checar_equilibrio(self, m, r, {"G": 1.0})
        np.testing.assert_allclose(r.esforcos["ab"][-1][1:], 0.0, atol=1e-9)
        ex = np.array([3, 2, 4]) / np.sqrt(29)  # trecho ni..a: N = F·ex (equilíbrio do trecho até a ponta)
        self.assertAlmostEqual(r.esforcos["ab"][0][1], np.dot([1.0, -2.0, -15.0], ex), delta=1e-9)

    def test_fora_da_barra(self):
        m = self.biapoiada()
        m.cargas_pontuais[0].a = 7.0
        with self.assertRaises(ValueError):
            analisar(m, "G")


class TestTrelicaCargaDeVao(unittest.TestCase):
    def test_trelica_com_peso_proprio(self):
        """Barra de treliça com carga de vão trabalha como biapoiada (sem momento nos nós):
        banzo ab horizontal com q = γA → M no meio = qL²/8 (resistência dos materiais);
        reações e equilíbrio globais. Tol. 1e-9."""
        L, h, gama = 4.0, 3.0, 78.5
        m = base(peso=gama)
        m.peso_proprio = "G"
        m.add(No("a", 0, 0, 0, apoio=(True, True, True, False, False, False)))
        m.add(No("b", L, 0, 0, apoio=(False, True, True, False, False, False)))
        m.add(No("c", L / 2, 0, h, apoio=(False, True, False, False, False, False)))
        for bid, i, j in (("ab", "a", "b"), ("ac", "a", "c"), ("bc", "b", "c")):
            m.add(Barra(bid, i, j, "s", "aco", params={"trelica": True}))
        m.add(CargaPontual("G", "ab", 1.0, (0, 0, -8.0)))
        r = analisar(m, "G")
        checar_equilibrio(self, m, r, {"G": 1.0})
        q = gama * A
        (meio,), _ = ponto(r, "ab", L / 2)
        self.assertAlmostEqual(meio[5] / -(q * L**2 / 8 + 8.0 * 1.0 * (L - L / 2) / L), 1.0, delta=1e-9)
        for e in r.esforcos.values():
            self.assertAlmostEqual(e[0][5], 0.0, delta=1e-9)
            self.assertAlmostEqual(e[-1][5], 0.0, delta=1e-9)


    def test_flecha_trelica_biapoiada(self):
        """Flecha de barra de treliça = reta entre os nós + viga biapoiada (sem giro nodal):
        5qL⁴/384EI no meio e, para P em a, P·a·(L−x)·(L² − a² − (L−x)²)/(6EIL) para x ≥ a
        (AISC Manual 15ª ed., tab. 3-23, casos 1 e 8). Nós girados por uma viga paralela
        (momento M em i) não podem entrar na flecha da treliça. Tol. 1e-9."""
        L, q, P, a, M = 4.0, 3.0, 8.0, 1.0, 20.0
        m = base()
        m.add(No("i", 0, 0, 0, apoio=(True, True, True, True, False, False)))
        m.add(No("j", L, 0, 0, apoio=(False, True, True, False, False, False)))
        m.add(Barra("v", "i", "j", "s", "aco"))
        m.add(Barra("t", "i", "j", "s", "aco", params={"trelica": True}))
        m.add(CargaDistribuida("G", "t", (0, 0, -q)))
        m.add(CargaPontual("G", "t", a, (0, 0, -P)))
        m.add(CargaNodal("G", "i", (0, 0, 0, 0, M, 0)))
        r = analisar(m, "G")
        self.assertGreater(abs(r.deslocamentos["i"][4]), 1e-4)  # nó gira
        _, (fle,) = ponto(r, "t", L / 2)
        x = L / 2
        w = 5 * q * L**4 / 384 + P * a * (L - x) * (L**2 - a**2 - (L - x)**2) / (6 * L)
        self.assertAlmostEqual(fle[3] / (-w / (E * I)), 1.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})


class TestRotulas(unittest.TestCase):
    def test_biengastada_rotula_em_j(self):
        """Viga engastada-apoiada (rótula em j), q uniforme (Gere & Timoshenko, apêndice G,
        viga engastada-apoiada): M_i = qL²/8 (tração em cima → My > 0), M_j = 0,
        R_i = 5qL/8, R_j = 3qL/8, flecha no meio = qL⁴/(192EI). Tol. 1e-9."""
        L, q = 5.0, 10.0
        m = base()
        m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", L, 0, 0, apoio=ENG))
        m.add(Barra("b", "i", "j", "s", "aco", params={"rotula_j": True}))
        m.add(CargaDistribuida("G", "b", (0, 0, -q)))
        r = analisar(m, "G", n_pontos=11)
        e = r.esforcos["b"]
        self.assertAlmostEqual(e[0][5] / (q * L**2 / 8), 1.0, delta=1e-9)
        self.assertAlmostEqual(e[-1][5], 0.0, delta=1e-9 * q * L**2)
        self.assertAlmostEqual(e[-1][6], 0.0, delta=1e-9 * q * L**2)
        self.assertAlmostEqual(r.reacoes["i"][2] / (5 * q * L / 8), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["j"][2] / (3 * q * L / 8), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["j"][4], 0.0, delta=1e-9)
        self.assertAlmostEqual(r.flechas["b"][5][3] / (-q * L**4 / (192 * E * I)), 1.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_duas_rotulas_mantem_axial_e_torcao(self):
        """Barra rotulada nas duas extremidades: u = PL/EA e θ = TL/GJ."""
        L, P, T = 2.0, 50.0, 3.0
        m = base()
        m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", L, 0, 0))
        m.add(Barra("b", "i", "j", "s", "aco", params={"rotula_i": True, "rotula_j": True}))
        m.add(CargaNodal("G", "j", (P, 0, 0, T, 0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["j"][0] / (P * L / (E * A)), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.deslocamentos["j"][3] / (T * L / (G * J)), 1.0, delta=1e-9)

    def test_no_so_com_barras_rotuladas(self):
        """Nó central só com barras rotuladas: giros ry/rz fixados; P no nó → duas vigas
        engastadas-rotuladas em paralelo, k = 2·3EI/L³; momento no nó → mecanismo."""
        L, P = 3.0, 6.0
        m = base()
        m.add(No("e", 0, 0, 0, apoio=ENG)); m.add(No("c", L, 0, 0)); m.add(No("d", 2 * L, 0, 0, apoio=ENG))
        m.add(Barra("b1", "e", "c", "s", "aco", params={"rotula_j": True}))
        m.add(Barra("b2", "c", "d", "s", "aco", params={"rotula_i": True}))
        m.add(CargaNodal("G", "c", (0, 0, -P, 0, 0, 0)))
        m.add(CargaNodal("Q", "c", (0, 0, 0, 0, 1.0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["c"][2] / (-P * L**3 / (6 * E * I)), 1.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})
        with self.assertRaisesRegex(ErroMecanismo, "nó c, GL ry"):
            analisar(m, "Q")


def dois_porticos(I1, I2, orfao=True, diafragma=True):
    """Dois pilares engastados (pórticos planos xz em y=0 e y=D), topo em z=H no pavimento P1."""
    H, D = 3.0, 6.0
    m = Modelo()
    m.add(Material("aco", E, G, 0.0))
    m.add(Secao("s1", A, I1, I1, J)); m.add(Secao("s2", A, I2, I2, J))
    m.add(CasoCarga("G", "G")); m.add(CasoCarga("Q", "Q"))
    for k, y in (("1", 0.0), ("2", D)):
        m.add(No("b" + k, 0, y, 0, apoio=ENG)); m.add(No("t" + k, 0, y, H))
        m.add(Barra("p" + k, "b" + k, "t" + k, "s" + k, "aco"))
    if orfao:
        m.add(No("c", 0, D / 2, H))  # nó no centro, só ligado pelo diafragma
    m.add(Pavimento("P1", H, diafragma))
    return m, H, D


class TestDiafragma(unittest.TestCase):
    def test_excentricidade_gira_pavimento(self):
        """Pilares em balanço k_i = 3EI_i/H³ e torção kt = GJ/H, ligados por diafragma; H no
        centro c = (0, D/2). Trabalho virtual (h = D/2, s = k1+k2, d = k1-k2):
        s·u + d·h·θ = H;  d·h·u + (s·h² + 2kt)·θ = 0;  F1 = k1(u + hθ), F2 = k2(u − hθ). Tol. 1e-6."""
        I1, I2, Hf = 1e-4, 3e-4, 10.0
        m, H, D = dois_porticos(I1, I2)
        m.add(CargaNodal("G", "c", (Hf, 0, 0, 0, 0, 0)))
        r = analisar(m, "G")
        k1, k2, kt, h = 3 * E * I1 / H**3, 3 * E * I2 / H**3, G * J / H, D / 2
        s, d = k1 + k2, k1 - k2
        det = s * (s * h * h + 2 * kt) - (d * h)**2
        u, th = Hf * (s * h * h + 2 * kt) / det, -d * h * Hf / det
        np.testing.assert_allclose(r.pavimentos["P1"], [u, 0.0, th], rtol=1e-6, atol=1e-14)
        self.assertAlmostEqual(r.reacoes["b1"][0] / (-k1 * (u + h * th)), 1.0, delta=1e-6)
        self.assertAlmostEqual(r.reacoes["b2"][0] / (-k2 * (u - h * th)), 1.0, delta=1e-6)
        self.assertAlmostEqual(r.deslocamentos["t1"][5] / th, 1.0, delta=1e-6)
        self.assertAlmostEqual(r.deslocamentos["t1"][1], 0.0, delta=1e-14)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_sem_excentricidade_nao_gira(self):
        m, H, D = dois_porticos(2e-4, 2e-4)
        m.add(CargaNodal("G", "c", (10.0, 0, 0, 0, 0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.pavimentos["P1"][2], 0.0, delta=1e-15)
        self.assertAlmostEqual(r.reacoes["b1"][0], -5.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["b2"][0], -5.0, delta=1e-9)

    def test_sem_diafragma_media(self):
        """Sem diafragma: pavimentos = média de ux, uy, rz dos nós do pavimento (ESPEC §5)."""
        m, H, D = dois_porticos(1e-4, 3e-4, orfao=False, diafragma=False)
        m.add(CargaNodal("G", "t1", (10.0, 2.0, 0, 0, 0, 1.0)))
        r = analisar(m, "G")
        med = np.mean([r.deslocamentos[n][[0, 1, 5]] for n in ("t1", "t2")], axis=0)
        np.testing.assert_allclose(r.pavimentos["P1"], med, rtol=1e-12)
        self.assertAlmostEqual(r.deslocamentos["t2"][0], 0.0, delta=1e-15)  # pilares independentes

    def test_centro_ignora_no_apoiado(self):
        """VC-solver-001: nó apoiado no nível do pavimento não entra no vínculo nem no centro.
        4 pilares em balanço (y = 0: Iy 2e-4; y = 4: 5e-5), H = 100 no canto (0, 0); centro (3, 2):
        u = 8,1156e-3 m, θ = 7,3698e-4 rad (sistema 3×3 à mão, verificacao/memorias/solver-casos.py)."""
        m = Modelo()
        m.add(Material("aco", 2e8, 8e7, 0.0)); m.add(CasoCarga("Q", "Q"))
        m.add(Secao("A", 0.01, 2e-4, 1e-4, 1e-6)); m.add(Secao("B", 0.01, 5e-5, 1e-4, 1e-6))
        for i, (x, y) in enumerate([(0, 0), (6, 0), (0, 4), (6, 4)], 1):
            m.add(No(f"b{i}", x, y, 0, ENG)); m.add(No(f"t{i}", x, y, 3.0))
            m.add(Barra(f"p{i}", f"b{i}", f"t{i}", "A" if y == 0 else "B", "aco"))
        m.add(No("ap", 3, 6, 3.0, ENG))  # moveria o centro para (3; 2,8)
        m.add(Pavimento("P1", 3.0))
        m.add(CargaNodal("Q", "t1", (100.0, 0, 0, 0, 0, 0)))
        u, v, th = analisar(m, "Q").pavimentos["P1"]
        self.assertAlmostEqual(u / 0.00811562397641664, 1.0, delta=1e-6)
        self.assertAlmostEqual(v, 0.0, delta=1e-12)
        self.assertAlmostEqual(th / 0.0007369800196528006, 1.0, delta=1e-6)


def portico_modal(diafragma=True, pavimento=True):
    """1 andar, 4 pilares h = 3 em (±3, ±2) relativo ao centro (3, 2), biengastados (topo com rx, ry
    e uz impedidos), massa W/g = 100/g em cada topo (carga nodal no caso G)."""
    h, Iy, Iz = 3.0, 2e-4, 1e-4
    m = Modelo()
    m.add(Material("aco", E, G, 0.0)); m.add(Secao("s", A, Iy, Iz, J)); m.add(CasoCarga("G", "G"))
    for i, (x, y) in enumerate([(0, 0), (6, 0), (0, 4), (6, 4)], 1):
        m.add(No(f"b{i}", x, y, 0, ENG))
        m.add(No(f"t{i}", x, y, h, (False, False, True, True, True, False)))
        m.add(Barra(f"p{i}", f"b{i}", f"t{i}", "s", "aco"))
        m.add(CargaNodal("G", f"t{i}", (0, 0, -100.0, 0, 0, 0)))
    if pavimento:
        m.add(Pavimento("P1", h, diafragma))
    return m, h, Iy, Iz


class TestModal(unittest.TestCase):
    def test_diafragma_portico_1_andar(self):
        """VC-solver-002: piso rígido com 3 GL (ux, uy, rz no centro). Pilar biengastado:
        k = 12EI/h³ (Timoshenko); pilar vertical: ux usa Iy, uy usa Iz (eixos locais do topo de
        solver.py). ω² = Σk/Σm em x e y (Clough & Penzien, 1 GL); torção: Kθ = Σ(kx·dy² + ky·dx²)
        + Σ GJ/h, Jm = Σ m·(dx² + dy²). Tolerância 1%; participação ≈ 100% na direção do modo."""
        m, h, Iy, Iz = portico_modal()
        f, modos, part = modal(m, 3, {"G": 1.0})
        mi = 100.0 / 9.80665
        kx, ky, kt = 12 * E * Iy / h**3, 12 * E * Iz / h**3, G * J / h
        fy = np.sqrt(ky / mi) / (2 * np.pi)                       # 4 ky / 4 m
        fx = np.sqrt(kx / mi) / (2 * np.pi)
        ft = np.sqrt((kx * 4 + ky * 9 + kt) / (mi * 13)) / (2 * np.pi)  # dx = 3, dy = 2
        np.testing.assert_allclose(f, [fy, ft, fx], rtol=0.01)
        np.testing.assert_allclose(part[0], [0, 1, 0], atol=0.01)
        np.testing.assert_allclose(part[1], [0, 0, 0], atol=0.01)
        np.testing.assert_allclose(part[2], [1, 0, 0], atol=0.01)
        # modo de torção: todos os topos giram igual, ux = −dy·rz, uy = dx·rz
        md = modos[1]
        for i, (dx, dy) in enumerate([(-3, -2), (3, -2), (-3, 2), (3, 2)], 1):
            np.testing.assert_allclose(md[f"t{i}"][[0, 1, 5]], md["t1"][5] * np.array([-dy, dx, 1.0]),
                                       atol=1e-9 * abs(md["t1"][5]))

    def test_sem_diafragma_igual_sem_pavimento(self):
        """Pavimento sem diafragma não muda nada no modal (transformação identidade)."""
        a = modal(portico_modal(pavimento=False)[0], 4, {"G": 1.0})
        b = modal(portico_modal(diafragma=False)[0], 4, {"G": 1.0})
        np.testing.assert_array_equal(a[0], b[0])
        np.testing.assert_array_equal(np.array(a[2]), np.array(b[2]))

    def test_modos_superiores_biapoiada(self):
        """Biapoiada plana (L = 6, I = 8,581e-5, m̄ = 10/g), 20 elementos, massa concentrada:
        f_n = n²·f1, f1 = (π/2L²)·√(EI/m̄) (Clough & Penzien), tolerância 2%. Os modos da
        biapoiada discreta uniforme são senos φ_k = sin(nπk/N); participação na massa livre
        (N−1 nós) = (Σφ)²/((N−1)·Σφ²) = 2·cot²(nπ/2N)/(N(N−1)) para n ímpar, 0 para n par."""
        Es, Is, L, N, q = 2e8, 8.581e-5, 6.0, 20, 10.0
        m = Modelo()
        m.add(Material("aco", Es, 8e7, 0.0)); m.add(Secao("s", 0.01, Is, Is, 1e-6))
        m.add(CasoCarga("G", "G"))
        plano = (True, True, False, True, False, True)  # só uz e ry (plano xz)
        for k in range(N + 1):
            m.add(No(f"n{k}", L * k / N, 0, 0, (True, True, True, True, False, True) if k in (0, N) else plano))
        for k in range(N):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k + 1}", "s", "aco"))
            m.add(CargaDistribuida("G", f"b{k}", (0, 0, -q)))
        f, modos, part = modal(m, 3, {"G": 1.0})
        f1 = np.pi / (2 * L**2) * np.sqrt(Es * Is / (q / 9.80665))
        np.testing.assert_allclose(f, [f1, 4 * f1, 9 * f1], rtol=0.02)
        for n in (1, 2, 3):
            p = 2 / np.tan(n * np.pi / (2 * N))**2 / (N * (N - 1)) if n % 2 else 0.0
            self.assertAlmostEqual(part[n - 1][2], p, delta=1e-6)
            self.assertAlmostEqual(part[n - 1][0] + part[n - 1][1], 0.0, delta=1e-12)


def portico_3d():
    """Pórtico espacial com diafragma, rótula, cargas nodais, distribuídas, pontuais e peso próprio."""
    m = base(peso=78.5)
    m.peso_proprio = "G"
    nos = {"a": (0, 0, 0), "b": (5, 0, 0), "c": (5, 4, 0), "d": (0, 4, 0)}
    for k, p in nos.items():
        m.add(No(k, *p, apoio=ENG)); m.add(No(k + "t", p[0], p[1], 3.0))
        m.add(Barra("P" + k, k, k + "t", "s", "aco", rot=0.3))
    for i, j in (("a", "b"), ("b", "c"), ("c", "d"), ("d", "a")):
        m.add(Barra("V" + i + j, i + "t", j + "t", "s", "aco"))
    m.barras["Vbc"].params["rotula_i"] = True
    m.add(Pavimento("P1", 3.0))
    m.add(CargaDistribuida("G", "Vab", (0.5, 0, -15.0)))
    m.add(CargaPontual("G", "Vcd", 1.5, (0, 0, -7.0)))
    m.add(CargaPontual("Q", "Vbc", 1.0, (0, 3.0, -20.0)))
    m.add(CargaNodal("Q", "ct", (12.0, -5.0, -20.0, 1, 2, 3)))
    return m


class TestCombinacaoPesosEixos(unittest.TestCase):
    def test_analisar_dict_igual_combinar(self):
        """Superposição linear: analisar({G: 1.4, Q: 1.5}) = combinar(analisar por caso). Tol. 1e-9."""
        m = portico_3d()
        fat = {"G": 1.4, "Q": 1.5}
        rd = analisar(m, fat)
        rc = combinar({c: analisar(m, c) for c in fat}, fat)
        for nome in ("deslocamentos", "reacoes", "esforcos", "flechas", "pavimentos"):
            a, b = getattr(rd, nome), getattr(rc, nome)
            self.assertEqual(set(a), set(b), nome)
            for k in a:
                va, vb = np.asarray(a[k], float), np.asarray(b[k], float)
                np.testing.assert_allclose(va, vb, rtol=1e-9, atol=1e-9 * max(1e-12, np.abs(va).max()))
        self.assertIn("P1", rd.pavimentos)
        checar_equilibrio(self, m, rd, fat)
        np.testing.assert_allclose(analisar(m, "G").deslocamentos["ct"],
                                   analisar(m, {"G": 1.0}).deslocamentos["ct"], rtol=1e-12)

    def test_pdelta_com_dict(self):
        """P-Δ da combinação analisada como um caso: compressão aumenta o deslocamento lateral."""
        m = portico_3d()
        fat = {"G": 1.4, "Q": 1.5}
        r, rp = analisar(m, fat), analisar(m, fat, pdelta=True)
        self.assertGreater(abs(rp.pavimentos["P1"][0]), abs(r.pavimentos["P1"][0]))

    def test_pesos_nodais(self):
        """Σ pesos = Σ cargas verticais ponderadas (para baixo, positivo); biapoiada: qL/2 por nó."""
        m = portico_3d()
        fat = {"G": 1.4, "Q": 1.5}
        p = pesos_nodais(m, fat)
        self.assertEqual(set(p), set(m.nos))
        self.assertAlmostEqual(sum(p.values()) / -cargas_totais(m, fat)[0][2], 1.0, delta=1e-9)
        m = base()
        m.add(No("i", 0, 0, 0)); m.add(No("j", 4, 0, 0))
        m.add(Barra("b", "i", "j", "s", "aco"))
        m.add(CargaDistribuida("G", "b", (0, 0, -3.0)))
        p = pesos_nodais(m, {"G": 2.0})
        self.assertEqual(p, {"i": 12.0, "j": 12.0})
        self.assertIsInstance(p["i"], float)

    def test_eixos_locais(self):
        """Convenção (ESPEC §3): y = Z × x; vertical: y = Y; rot gira (y, z) em torno de x."""
        m = base()
        m.add(No("o", 0, 0, 0)); m.add(No("x", 2, 0, 0)); m.add(No("z", 0, 0, 3))
        m.add(Barra("h", "o", "x", "s", "aco")); m.add(Barra("v", "o", "z", "s", "aco"))
        m.add(Barra("r", "o", "x", "s", "aco", rot=np.pi / 2))
        esperados = {"h": ([1, 0, 0], [0, 1, 0], [0, 0, 1]),
                     "v": ([0, 0, 1], [0, 1, 0], [-1, 0, 0]),
                     "r": ([1, 0, 0], [0, 0, 1], [0, -1, 0])}
        for bid, esp in esperados.items():
            ex = eixos_locais(m, bid)
            self.assertIsInstance(ex[0], list)
            self.assertIsInstance(ex[0][0], float)
            np.testing.assert_allclose(ex, esp, atol=1e-15)


class TestAISC(unittest.TestCase):
    """AISC 360-16, Comentário C2 (benchmarks de 2ª ordem), W14x48 (A = 14,1 in², Ix = 484 in⁴,
    Iy = 51,4 in⁴, J = 1,45 in⁴, E = 29 000 ksi), L = 28 ft, 8 elementos; valores também
    conferidos no teste público do Pynite (Testing/test_AISC_PDelta_benchmarks.py).
    Problema plano (xz): uy, rx, rz restritos em todos os nós. Tol.: M 3 %, deslocamento 5 %."""
    PLANO = (False, True, False, True, False, True)

    def modelo(self):
        m = Modelo()
        m.add(Material("A992", 29000 * KSI, 11200 * KSI))
        m.add(Secao("W14x48", 14.1 * POL**2, 484 * POL**4, 51.4 * POL**4, 1.45 * POL**4))
        m.add(CasoCarga("G", "G"))
        return m

    def test_caso1_biarticulado(self):
        L, n, w = 28 * PE, 8, 0.2 * KIP / PE
        for P, M_esp, d_esp in ((0, 235, 0.197), (150, 269, 0.224), (300, 313, 0.261), (450, 375, 0.311)):
            m = self.modelo()
            for k in range(n + 1):
                ap = list(self.PLANO)
                if k == 0:
                    ap[0] = ap[2] = True
                if k == n:
                    ap[2] = True
                m.add(No(f"n{k}", L * k / n, 0, 0, apoio=tuple(ap)))
            for k in range(n):
                m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "W14x48", "A992"))
                m.add(CargaDistribuida("G", f"b{k}", (0, 0, -w)))
            m.add(CargaNodal("G", f"n{n}", (-P * KIP, 0, 0, 0, 0, 0)))
            r = analisar(m, "G", pdelta=True)
            M = abs(r.esforcos[f"b{n // 2 - 1}"][-1][5]) / (KIP * POL)
            d = abs(r.deslocamentos[f"n{n // 2}"][2]) / POL
            self.assertAlmostEqual(M / M_esp, 1.0, delta=0.03, msg=f"P={P}")
            self.assertAlmostEqual(d / d_esp, 1.0, delta=0.05, msg=f"P={P}")

    def test_momento_continuo_no_no_em_pdelta(self):
        """Equilíbrio do trecho na posição deformada (termo N·Δw): em P-Δ o momento no nó
        intermediário é o mesmo nos dois elementos (McGuire cap. 9: Kg consistente satisfaz
        ΣM = N·Δw no elemento). Tol. 1e-6 rel."""
        L, n, w, P = 28 * PE, 8, 0.2 * KIP / PE, 450 * KIP
        m = self.modelo()
        for k in range(n + 1):
            ap = list(self.PLANO)
            ap[2] = k in (0, n)
            ap[0] = k == 0
            m.add(No(f"n{k}", L * k / n, 0, 0, apoio=tuple(ap)))
        for k in range(n):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "W14x48", "A992"))
            m.add(CargaDistribuida("G", f"b{k}", (0, 0, -w)))
        m.add(CargaNodal("G", f"n{n}", (-P, 0, 0, 0, 0, 0)))
        r = analisar(m, "G", pdelta=True)
        for k in range(1, n):
            esq, dir_ = r.esforcos[f"b{k-1}"][-1][5], r.esforcos[f"b{k}"][0][5]
            self.assertAlmostEqual(esq / dir_, 1.0, delta=1e-6, msg=f"nó n{k}")

    def test_caso2_engastado(self):
        L, n = 28 * PE, 8
        for P, M_esp, d_esp in ((0, 336, 0.901), (100, 469, 1.33), (150, 598, 1.75), (200, 848, 2.56)):
            m = self.modelo()
            for k in range(n + 1):
                m.add(No(f"n{k}", 0, 0, L * k / n, apoio=ENG if k == 0 else self.PLANO))
            for k in range(n):
                m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "W14x48", "A992"))
            m.add(CargaNodal("G", f"n{n}", (1.0 * KIP, 0, -P * KIP, 0, 0, 0)))
            r = analisar(m, "G", pdelta=True)
            M = abs(r.reacoes["n0"][4]) / (KIP * POL)
            d = abs(r.deslocamentos[f"n{n}"][0]) / POL
            self.assertAlmostEqual(M / M_esp, 1.0, delta=0.03, msg=f"P={P}")
            self.assertAlmostEqual(d / d_esp, 1.0, delta=0.05, msg=f"P={P}")


class TestEstabilidade(unittest.TestCase):
    """Pilar em balanço, base engastada, topo livre, L = 3 m, EI = 2000 kN·m², H = 1 kN no topo.
    Pcr = π²EI/(4L²) (Timoshenko & Gere, Theory of Elastic Stability, 2ª ed., §2.1).
    Topo exato: δ = δ0·3(tan u − u)/u³, u = kL, k = √(P/EI), δ0 = HL³/3EI (Timoshenko & Gere
    §1.11, viga-coluna em balanço com carga lateral na ponta)."""
    L, EI, H, n = 3.0, 2000.0, 1.0, 8

    def pilar(self, P):
        m = base()
        m.secoes["s"].Iy = m.secoes["s"].Iz = self.EI / E
        for k in range(self.n + 1):
            m.add(No(f"n{k}", 0, 0, self.L * k / self.n, apoio=ENG if k == 0 else (False,) * 6))
        for k in range(self.n):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "s", "aco"))
        m.add(CargaNodal("G", f"n{self.n}", (self.H, 0, -P, 0, 0, 0)))
        return m

    def pcr(self):
        return np.pi**2 * self.EI / (4 * self.L**2)

    def test_abaixo_da_critica_amplifica(self):
        """P = 0,5·Pcr: δ/δ0 = 1/(1 − P/Pcr) = 2 (tol. 2 %) e = solução exata (tol. 0,5 %)."""
        P = 0.5 * self.pcr()
        m = self.pilar(P)
        d0 = analisar(m, "G").deslocamentos[f"n{self.n}"][0]
        d = analisar(m, "G", pdelta=True).deslocamentos[f"n{self.n}"][0]
        self.assertAlmostEqual(d0 / (self.H * self.L**3 / (3 * self.EI)), 1.0, delta=1e-9)
        self.assertAlmostEqual((d / d0) / (1 / (1 - 0.5)), 1.0, delta=0.02)
        u = self.L * np.sqrt(P / self.EI)
        self.assertAlmostEqual((d / d0) / (3 * (np.tan(u) - u) / u**3), 1.0, delta=0.005)

    def test_acima_da_critica_erro(self):
        """P = 1,5·Pcr e 3·Pcr: K + Kg indefinida → ErroMecanismo citando a combinação
        (antes convergia com ux de sinal invertido; revisão 2026-10-20 B-1)."""
        for f in (1.5, 3.0):
            m = self.pilar(f * self.pcr() / 1.4)  # combinação 1,4·G leva a f·Pcr
            with self.assertRaisesRegex(ErroMecanismo, r"acima da crítica.*1\.4·G.*predominante: nó n\d, GL (u[xy]|r[xy])"):
                analisar(m, {"G": 1.4}, pdelta=True)

    def test_caso_inexistente(self):
        m = self.pilar(1.0)
        with self.assertRaisesRegex(KeyError, "X"):
            analisar(m, "X")
        with self.assertRaisesRegex(KeyError, "X"):
            analisar(m, {"G": 1.0, "X": 1.0})


class TestTrechoRigido(unittest.TestCase):
    L, q = 6.0, 10.0
    AP_I = (True, True, True, True, False, False)
    AP_J = (False, True, True, False, False, False)

    def biapoiada(self, ti, tj):
        m = base()
        m.add(No("i", 0, 0, 0, apoio=self.AP_I)); m.add(No("j", self.L, 0, 0, apoio=self.AP_J))
        m.add(Barra("b", "i", "j", "s", "aco", params={"trecho_rigido_i": ti, "trecho_rigido_j": tj}))
        m.add(CargaDistribuida("G", "b", (0, 0, -self.q)))
        return m

    def test_biapoiada_simetrica(self):
        """Biapoiada (vão L entre nós) com trechos rígidos t nas duas pontas, q em todo o vão.
        Isostática: M(x) = q(Lx - x²)/2 (Gere & Timoshenko, apêndice G). Flecha no meio por trabalho
        virtual só no trecho flexível: δ = q/(2EI)·[5L⁴/192 - L·t³/3 + t⁴/4] (t = 0 → 5qL⁴/384EI).
        Saída de [t, L - t]. Tol. 1e-9."""
        L, q, t = self.L, self.q, 0.3
        m = self.biapoiada(t, t)
        r = analisar(m, "G", n_pontos=11)
        e, f = r.esforcos["b"], r.flechas["b"]
        self.assertAlmostEqual(e[0][0], t, delta=1e-12)
        self.assertAlmostEqual(e[-1][0], L - t, delta=1e-12)
        self.assertAlmostEqual(e[0][5] / (-q * (L * t - t * t) / 2), 1.0, delta=1e-9)
        self.assertAlmostEqual(e[5][5] / (-q * L * L / 8), 1.0, delta=1e-9)
        d = q / (2 * E * I) * (5 * L**4 / 192 - L * t**3 / 3 + t**4 / 4)
        self.assertAlmostEqual(f[5][0], L / 2, delta=1e-12)
        self.assertAlmostEqual(f[5][3] / -d, 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["i"][2] / (q * L / 2), 1.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_biapoiada_assimetrica_carga_no_trecho_rigido(self):
        """ti ≠ tj e carga pontual P dentro do trecho rígido i (a < ti): reações e momentos nas faces
        por estática da viga simples de vão L: R_i = qL/2 + P(L-a)/L; M_face_i = R_i·ti - q·ti²/2 - P(ti-a);
        V_face_i = -(R_i - q·ti - P). Tol. 1e-9."""
        L, q, ti, tj, a, P = self.L, self.q, 0.3, 0.5, 0.1, 7.0
        m = self.biapoiada(ti, tj)
        m.add(CargaPontual("G", "b", a, (0, 0, -P)))
        r = analisar(m, "G")
        e = r.esforcos["b"]
        Ri, Rj = q * L / 2 + P * (L - a) / L, q * L / 2 + P * a / L
        self.assertAlmostEqual(r.reacoes["i"][2] / Ri, 1.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["j"][2] / Rj, 1.0, delta=1e-9)
        self.assertAlmostEqual(e[0][5] / -(Ri * ti - q * ti * ti / 2 - P * (ti - a)), 1.0, delta=1e-9)
        self.assertAlmostEqual(e[-1][5] / -(Rj * tj - q * tj * tj / 2), 1.0, delta=1e-9)
        self.assertAlmostEqual(e[0][3] / -(Ri - q * ti - P), 1.0, delta=1e-9)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_portico_equivalente_a_barras_muito_rigidas(self):
        """Pórtico 3D com nó rígido (trechos rígidos em pilar e vigas, rot ≠ 0, rótula na face, cargas
        distribuídas, pontuais no trecho flexível e no rígido, nodal e peso próprio) = o mesmo pórtico com
        os trechos modelados como barras de rigidez ×1e4 (equivalência; o resíduo é a flexibilidade dessas
        barras, ~6e-6; tol. 3e-5). Esforços e flechas do trecho flexível iguais aos da barra flexível do modelo B."""
        def modelo(rigido):
            m = base(peso=78.5)
            m.peso_proprio = "G"
            m.add(Material("rig", E, G, 78.5 / 1e4))
            m.add(Secao("r", A * 1e4, I * 1e4, I * 1e4, J * 1e4))
            m.add(No("a", 0, 0, 0, apoio=ENG)); m.add(No("t", 0, 0, 3.0))
            m.add(No("c", 4, 1, 3.0, apoio=(True, True, True, False, False, False)))
            m.add(No("d", 0, -3, 3.5, apoio=ENG))
            barras = {"P": ("a", "t", 0.0, 0.0, 0.15, {}), "V1": ("t", "c", 0.4, 0.2, 0.1, {}),
                      "V2": ("t", "d", 0.0, 0.25, 0.12, {"rotula_j": True})}
            mapa = {}
            for bid, (ni, nj, rot, ti, tj, extra) in barras.items():
                pi, pj = _pos(m, ni), _pos(m, nj)
                Lb = np.linalg.norm(pj - pi)
                ex = (pj - pi) / Lb
                if rigido:
                    m.add(Barra(bid, ni, nj, "s", "aco", rot=rot,
                                params={"trecho_rigido_i": ti, "trecho_rigido_j": tj, **extra}))
                    mapa[bid] = (bid, 0.0, Lb)
                    continue
                x = 0.0
                for k, tt in (("_i", ti), ("_j", tj)):
                    if tt:
                        no = bid + k
                        p = pi + ex * (ti if k == "_i" else Lb - tj)
                        m.add(No(no, *p))
                cad = [ni] + ([bid + "_i"] if ti else []) + ([bid + "_j"] if tj else []) + [nj]
                segs = [ti] * bool(ti) + [Lb - ti - tj] + [tj] * bool(tj)
                for k, (n1, n2) in enumerate(zip(cad, cad[1:])):
                    flex = abs(x - ti) < 1e-12 and abs(segs[k] - (Lb - ti - tj)) < 1e-12
                    sid = bid if flex else f"{bid}_r{k}"
                    m.add(Barra(sid, n1, n2, "s" if flex else "r", "aco" if flex else "rig", rot=rot,
                                params=extra if flex else {}))
                    mapa[sid] = (bid, x, segs[k])
                    x += segs[k]
            # cargas definidas sobre a barra original (posição a a partir de ni)
            cargas_d = {"V1": (1.0, -2.0, -10.0), "V2": (0, 3.0, -6.0), "P": (2.0, 0, 0)}
            cargas_p = [("V1", 0.05, (0, 1.5, -8.0)), ("V1", 2.0, (1.0, 0, -12.0)),
                        ("V2", 1.0, (0, 0, -5.0)), ("P", 2.95, (4.0, -1.0, 0))]
            for sid, (bid, x0, Ls) in mapa.items():
                if bid in cargas_d:
                    m.add(CargaDistribuida("G", sid, cargas_d[bid]))
            for bid, a, F in cargas_p:  # no 1º segmento que contém a
                sid, (_, x0, _) = next((s_, v) for s_, v in mapa.items()
                                       if v[0] == bid and v[1] <= a <= v[1] + v[2])
                m.add(CargaPontual("G", sid, a - x0, F))
            m.add(CargaNodal("G", "t", (5.0, -3.0, -20.0, 1.0, 2.0, -1.5)))
            return m, mapa

        mA, _ = modelo(True)
        mB, mapaB = modelo(False)
        rA, rB = analisar(mA, "G"), analisar(mB, "G")
        checar_equilibrio(self, mA, rA, {"G": 1.0})
        escu = max(np.abs(v).max() for v in rB.deslocamentos.values())
        for n in mA.nos:
            np.testing.assert_allclose(rA.deslocamentos[n], rB.deslocamentos[n], atol=3e-5 * escu)
        for n in rA.reacoes:
            np.testing.assert_allclose(rA.reacoes[n], rB.reacoes[n], atol=3e-5 * 50)
        for bid in ("P", "V1", "V2"):
            x0 = mapaB[bid][1]
            eA, eB = np.array(rA.esforcos[bid]), np.array(rB.esforcos[bid])
            eB[:, 0] += x0
            np.testing.assert_allclose(eA, eB, atol=3e-5 * 50)
            fA, fB = np.array(rA.flechas[bid]), np.array(rB.flechas[bid])
            fB[:, 0] += x0
            np.testing.assert_allclose(fA, fB, atol=3e-5 * escu)

    def test_pdelta_trecho_rigido_no_topo_compressao_e_tracao(self):
        """Pilar em balanço (base engastada), Lf = 3,4 + tj = 0,6 no topo, 4 barras, Hx = 10 e Hy = -6 kN,
        P = ±1000 kN (P > 0 compressão; Pcr do trecho flexível ≈ 4270). EDO exata (Timoshenko & Gere,
        Theory of Elastic Stability §1.11): EI·w'' = H(Lf + t - x) + P(Δ - w), Δ = w(Lf) + t·w'(Lf) ⇒
        Δ = -H(Lf + t)/P + H/(P·k)·(s + t·k·c)/(c - t·k·s), k = √(P/EI), s, c = sen, cos(k·Lf) (k imaginário na
        tração); M_base = H(Lf + t) + P·Δ. Exige a rigidez de corda N·t do trecho rígido girado (TC-01).
        Erro medido 3e-6 (Kg cúbica, 4 barras); tol. 1e-4."""
        Lf, t, H = 3.4, 0.6, np.array([10.0, -6.0])
        for P in (1000.0, -1000.0):
            m = base()
            zs = np.linspace(0, Lf, 5)
            zs[-1] = Lf + t
            for k, z in enumerate(zs):
                m.add(No(f"n{k}", 0, 0, z, apoio=ENG if k == 0 else (False,) * 6))
            for k in range(4):
                m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "s", "aco",
                            params={"trecho_rigido_j": t} if k == 3 else {}))
            m.add(CargaNodal("G", "n4", (*H, -P, 0, 0, 0)))
            r = analisar(m, "G", pdelta=True)
            k_ = np.sqrt(complex(P / (E * I)))
            s, c = np.sin(k_ * Lf), np.cos(k_ * Lf)
            fator = (-(Lf + t) / P + (s + t * k_ * c) / (P * k_ * (c - t * k_ * s))).real
            D = H * fator
            Mb = H * (Lf + t) + P * D
            with self.subTest(P=P):
                np.testing.assert_allclose(r.deslocamentos["n4"][:2], D, rtol=1e-4)
                np.testing.assert_allclose(np.abs(r.reacoes["n0"][[4, 3]]), np.abs(Mb), rtol=1e-4)

    def test_validacao(self):
        for ti, tj in ((-0.1, 0.0), (3.0, 3.0), (4.0, 2.5), (float("nan"), 0.0)):
            with self.assertRaisesRegex(ValueError, "trecho"):
                analisar(self.biapoiada(ti, tj), "G")


class TestCortante(unittest.TestCase):
    """Deformação por cortante (Timoshenko), params["cortante"] = True e props Ay, Az (m²) da seção:
    Ay = área de cisalhamento para Vy (plano xy, Iz), Az para Vz (plano xz, Iy). Iy ≠ Iz e Ay ≠ Az
    para pegar troca de plano."""
    Iy, Iz, Ay, Az = 3e-5, 1e-5, 2e-4, 6e-4

    def modelo(self, apoios, L=2.0, pts=None, cortante=True):
        m = Modelo()
        m.add(Material("aco", E, G, 0.0))
        m.add(Secao("s", A, self.Iy, self.Iz, J, props={"Ay": self.Ay, "Az": self.Az}))
        m.add(CasoCarga("G", "G"))
        xs = pts or [0.0, L]
        for k, x in enumerate(xs):
            m.add(No(f"n{k}", x, 0, 0, apoio=apoios.get(k, (False,) * 6)))
        for k in range(len(xs) - 1):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "s", "aco", params={"cortante": cortante}))
        return m

    def test_balanco_carga_na_ponta(self):
        """Balanço de Timoshenko, P na ponta (Timoshenko & Gere, Mechanics of Materials, def. por
        cortante): w(x) = P·x²(3L - x)/(6EI) + P·x/(G·As), nos dois planos. Tol. 1e-9 (elemento exato)."""
        L, Py, Pz = 2.0, 3.0, -5.0
        m = self.modelo({0: ENG}, L)
        m.add(CargaNodal("G", "n1", (0, Py, Pz, 0, 0, 0)))
        r = analisar(m, "G")
        for x, _, uy, uz in r.flechas["b0"]:
            wy = Py * x * x * (3 * L - x) / (6 * E * self.Iz) + Py * x / (G * self.Ay)
            wz = Pz * x * x * (3 * L - x) / (6 * E * self.Iy) + Pz * x / (G * self.Az)
            self.assertAlmostEqual(uy, wy, delta=1e-9 * abs(wy) + 1e-15)
            self.assertAlmostEqual(uz, wz, delta=1e-9 * abs(wz) + 1e-15)
        checar_equilibrio(self, m, r, {"G": 1.0})

    def test_biapoiada_q(self):
        """Biapoiada, q uniforme: flecha no meio = 5qL⁴/(384EI) + qL²/(8G·As); M = qL²/8. Tol. 1e-9."""
        L, q = 3.0, 10.0
        m = self.modelo({0: (True, True, True, True, False, False), 1: (False, True, True, False, False, False)}, L)
        m.add(CargaDistribuida("G", "b0", (0, 0, -q)))
        r = analisar(m, "G")
        d = 5 * q * L**4 / (384 * E * self.Iy) + q * L * L / (8 * G * self.Az)
        self.assertAlmostEqual(r.flechas["b0"][5][3] / -d, 1.0, delta=1e-9)
        self.assertAlmostEqual(r.esforcos["b0"][5][5] / (-q * L * L / 8), 1.0, delta=1e-9)

    def test_biengastada_pontual_igual_a_duas_barras(self):
        """Biengastada com P em a (hiperestática; engastamento de Timoshenko: M_i = P·a·b(b + ΦL/2)/(L²(1+Φ)),
        Przemieniecki §5.6) = duas barras com carga nodal em a (elemento exato nos nós). Flechas e esforços
        iguais em todos os pontos comuns; Φ = 0 recupera Euler. Tol. 1e-9."""
        L, a, F = 3.0, 1.1, (0.0, 4.0, -9.0)
        m1 = self.modelo({0: ENG, 1: ENG}, L)
        m1.add(CargaPontual("G", "b0", a, F))
        m2 = self.modelo({0: ENG, 2: ENG}, pts=[0.0, a, L])
        m2.add(CargaNodal("G", "n1", (*F, 0, 0, 0)))
        r1, r2 = analisar(m1, "G"), analisar(m2, "G")
        np.testing.assert_allclose(r1.reacoes["n0"], r2.reacoes["n0"], rtol=1e-9, atol=1e-9)
        np.testing.assert_allclose(r1.reacoes["n1"], r2.reacoes["n2"], rtol=1e-9, atol=1e-9)
        _, fa = ponto(r1, "b0", a)
        np.testing.assert_allclose(fa[0][1:], r2.deslocamentos["n1"][:3], rtol=1e-9, atol=1e-15)
        # ponto interno a/2 (carga nula força a saída lá): flecha da barra única = da 1ª das duas
        m1.add(CargaPontual("G", "b0", a / 2, (0, 0, 0)))
        _, f1 = ponto(analisar(m1, "G"), "b0", a / 2)
        m2.add(CargaPontual("G", "b0", a / 2, (0, 0, 0)))
        _, f2 = ponto(analisar(m2, "G"), "b0", a / 2)
        np.testing.assert_allclose(f1[0][1:], f2[0][1:], rtol=1e-9, atol=1e-15)
        checar_equilibrio(self, m1, r1, {"G": 1.0})

    def test_sem_areas_erro(self):
        m = self.modelo({0: ENG})
        m.secoes["s"].props = {}
        with self.assertRaisesRegex(ValueError, "Ay"):
            analisar(m, "G")


class TestCargaLinear(unittest.TestCase):
    """Carga distribuída linear (CargaDistribuida.q2 = valor em nj). Valores à mão: Gere & Timoshenko,
    apêndice G (viga simples, carga triangular e trapezoidal) e tabelas de engastamento perfeito (AISC
    tab. 3-23)."""
    L, w = 6.0, 12.0

    def viga(self, apoio_i, apoio_j, q, q2, nos=(0.0, 6.0), params=None):
        m = base()
        for k, x in enumerate(nos):
            m.add(No(f"n{k}", x, 0, 0, apoio=apoio_i if k == 0 else apoio_j if k == len(nos) - 1 else (False,) * 6))
        L = nos[-1]
        for k in range(len(nos) - 1):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "s", "aco", params=dict(params or {})))
            ta, tb = nos[k] / L, nos[k + 1] / L  # q linear ao longo da viga inteira
            m.add(CargaDistribuida("G", f"b{k}", tuple(q[i] + (q2[i] - q[i]) * ta for i in range(3)),
                                   q2=tuple(q[i] + (q2[i] - q[i]) * tb for i in range(3))))
        return m

    def test_biengastada_triangular(self):
        """0 em ni, w em nj (para baixo): R_i = 3wL/20 = 10,8; R_j = 7wL/20 = 25,2 kN; M_i = wL²/30 = 14,4;
        M_j = wL²/20 = 21,6 kN·m; flecha no meio wL⁴/(768EI)."""
        L, w = self.L, self.w
        m = self.viga(ENG, ENG, (0, 0, 0), (0, 0, -w))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.reacoes["n0"][2], 10.8, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][2], 25.2, delta=1e-9)
        self.assertAlmostEqual(abs(r.reacoes["n0"][4]), 14.4, delta=1e-9)
        self.assertAlmostEqual(abs(r.reacoes["n1"][4]), 21.6, delta=1e-9)
        self.assertAlmostEqual(r.esforcos["b0"][0][5], 14.4, delta=1e-9)   # My > 0 nos engastes
        self.assertAlmostEqual(r.esforcos["b0"][-1][5], 21.6, delta=1e-9)
        _, fle = ponto(r, "b0", L / 2)
        self.assertAlmostEqual(fle[0][3] / (-w * L**4 / (768 * E * I)), 1.0, delta=1e-9)

    def test_biapoiada_trapezio(self):
        """q de 4 (ni) a 10 kN/m (nj), L = 6: R_i = L(2q1+q2)/6 = 18, R_j = L(q1+2q2)/6 = 24 kN;
        M(x) = R_i·x - q1·x²/2 - (q2-q1)·x³/(6L): M(3) = 54 - 18 - 4,5 = 31,5 kN·m; V(3) = 18 - 12 - 4,5 = 1,5;
        flecha no meio 5q1L⁴/(384EI) + 5(q2-q1)L⁴/(768EI)."""
        L = self.L
        apoio_i, apoio_j = (True, True, True, True, False, False), (False, True, True, False, False, False)
        m = self.viga(apoio_i, apoio_j, (0, 0, -4.0), (0, 0, -10.0))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.reacoes["n0"][2], 18.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][2], 24.0, delta=1e-9)
        esf, fle = ponto(r, "b0", 3.0)
        self.assertAlmostEqual(esf[0][5], -31.5, delta=1e-9)   # My < 0 no vão (convenção do solver)
        self.assertAlmostEqual(abs(esf[0][3]), 1.5, delta=1e-9)
        f = 5 * 4 * L**4 / 384 + 5 * 6 * L**4 / 768
        self.assertAlmostEqual(fle[0][3] / (-f / (E * I)), 1.0, delta=1e-9)
        self.assertAlmostEqual(r.esforcos["b0"][-1][5], 0.0, delta=1e-9)

    def test_axial_e_inclinada(self):
        """Carga linear axial (0 → 6 kN/m ao longo de x) em barra engastada em ni e apoiada em x em nj:
        biengastada axial, R_i = wL/6 = 6, R_j = wL/3 = 12 kN; N(x) = R_i - w·x²/(2L)."""
        L = self.L
        apoio_j = (True, True, True, True, False, False)
        m = self.viga(ENG, apoio_j, (0, 0, 0), (6.0, 0, 0))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.reacoes["n0"][0], -6.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][0], -12.0, delta=1e-9)
        for x, N, *_ in r.esforcos["b0"]:
            self.assertAlmostEqual(N, 6.0 - 6.0 * x * x / (2 * L), delta=1e-9)

    def test_elemento_exato(self):
        """Uma barra x duas barras (nó no meio) com a mesma carga linear: a flecha interna da barra única no
        meio = deslocamento do nó do meio, e os esforços coincidem. Euler e Timoshenko, dois planos."""
        for params, props in (({}, {}), ({"cortante": True}, {"Ay": 2e-4, "Az": 6e-4})):
            ms = []
            for nos in ((0.0, 6.0), (0.0, 3.0, 6.0)):
                m = self.viga(ENG, (True, True, True, True, False, False), (0, 2.0, -3.0), (0, -5.0, -15.0),
                              nos, params)
                m.secoes["s"].props = props
                ms.append(analisar(m, "G"))
            r1, r2 = ms
            _, fle = ponto(r1, "b0", 3.0)
            np.testing.assert_allclose(fle[0][1:], r2.deslocamentos["n1"][:3], rtol=1e-9, atol=1e-15)
            for n in ("n0",):
                np.testing.assert_allclose(r1.reacoes[n], r2.reacoes[n], rtol=1e-9, atol=1e-9)
            esf, _ = ponto(r1, "b0", 3.0)
            np.testing.assert_allclose(esf[0][1:], r2.esforcos["b1"][0][1:], rtol=1e-9, atol=1e-9)

    def test_trelica_e_trecho_rigido(self):
        """Treliça: reações da biapoiada (wL/6 e wL/3). Trecho rígido com q2: ValueError claro."""
        m = self.viga((True,) * 3 + (True, True, True), (True,) * 3 + (False,) * 3, (0, 0, 0), (0, 0, -self.w),
                      params={"trelica": True})
        r = analisar(m, "G")
        self.assertAlmostEqual(r.reacoes["n0"][2], self.w * self.L / 6, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][2], self.w * self.L / 3, delta=1e-9)
        m = self.viga(ENG, ENG, (0, 0, 0), (0, 0, -self.w), params={"trecho_rigido_i": 0.2})
        with self.assertRaisesRegex(ValueError, "trecho rígido"):
            analisar(m, "G")

    def test_json_ida_e_volta(self):
        m = self.viga(ENG, ENG, (0, 0, -1.0), (0, 0, -2.0))
        m.add(CargaDistribuida("G", "b0", (0, 0, -3.0)))
        d = m.to_dict()
        self.assertEqual(d["cargas_distribuidas"][0]["q2"], [0, 0, -2.0])
        self.assertNotIn("q2", d["cargas_distribuidas"][1])   # uniforme: formato de arquivo inalterado
        self.assertEqual(Modelo.from_dict(d).cargas_distribuidas[0].q2, (0, 0, -2.0))


if __name__ == "__main__":
    unittest.main()


class TestMolasRecalques(unittest.TestCase):
    """Molas nodais (globais) e recalques prescritos; valores à mão (resistência dos materiais)."""
    EI = E * I

    def test_balanco_mola_de_giro(self):
        # balanço com giro da base elástico: θ = PL/kθ, w = PL³/3EI + PL²/kθ
        L, P, kt = 3.0, 5.0, 1e4
        m = base()
        m.add(No("n0", 0, 0, 0, apoio=(True, True, True, True, False, True), molas=(0, 0, 0, 0, kt, 0)))
        m.add(No("n1", L, 0, 0))
        m.add(Barra("b", "n0", "n1", "s", "aco"))
        m.add(CargaNodal("G", "n1", (0, 0, -P, 0, 0, 0)))
        for pd in (False, True):  # sem axial, P-Δ = linear
            r = analisar(m, "G", pdelta=pd)
            w = P * L**3 / (3 * self.EI) + P * L * L / kt
            self.assertAlmostEqual(r.deslocamentos["n1"][2], -w, delta=1e-12)
            self.assertAlmostEqual(abs(r.deslocamentos["n0"][4]), P * L / kt, delta=1e-12)
            self.assertAlmostEqual(abs(r.reacoes["n0"][4]), P * L, places=8)
            self.assertAlmostEqual(r.reacoes["n0"][2], P, places=8)

    def test_viga_sobre_duas_molas(self):
        # biapoiada em molas k: w(L/2) = P/2k + PL³/48EI; cada mola reage P/2
        L, P, k = 4.0, 10.0, 2e3
        m = base()
        ap = (True, True, False, True, False, True)
        mola = (0, 0, k, 0, 0, 0)
        m.add(No("a", 0, 0, 0, apoio=ap, molas=mola))
        m.add(No("c", L / 2, 0, 0))
        m.add(No("b", L, 0, 0, apoio=(False, True, False, True, False, True), molas=mola))
        m.add(Barra("b1", "a", "c", "s", "aco")); m.add(Barra("b2", "c", "b", "s", "aco"))
        m.add(CargaNodal("G", "c", (0, 0, -P, 0, 0, 0)))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["c"][2], -(P / (2 * k) + P * L**3 / (48 * self.EI)), delta=1e-12)
        self.assertAlmostEqual(r.reacoes["a"][2], P / 2, places=8)
        self.assertAlmostEqual(r.reacoes["b"][2], P / 2, places=8)

    def test_biengastada_recalque(self):
        # recalque Δ em nj: M = 6EIΔ/L², V = 12EIΔ/L³
        L, D = 4.0, -0.01
        m = base()
        m.add(No("n0", 0, 0, 0, apoio=ENG))
        m.add(No("n1", L, 0, 0, apoio=ENG, recalques=(0, 0, D, 0, 0, 0)))
        m.add(Barra("b", "n0", "n1", "s", "aco"))
        r = analisar(m, "G")
        self.assertAlmostEqual(r.deslocamentos["n1"][2], D)
        self.assertAlmostEqual(abs(r.reacoes["n0"][4]), 6 * self.EI * abs(D) / L**2, places=8)
        self.assertAlmostEqual(r.reacoes["n1"][2], 12 * self.EI * D / L**3, places=8)
        self.assertAlmostEqual(r.reacoes["n0"][2], -12 * self.EI * D / L**3, places=8)
        self.assertAlmostEqual(max(abs(p[5]) for p in r.esforcos["b"]), 6 * self.EI * abs(D) / L**2, places=8)

    def test_continua_apoio_central_recalca(self):
        # 2 vãos L, apoio central desce Δ: R = 6EIΔ/L³ (biapoiada 2L), M central = 3EIΔ/L²
        L, D = 5.0, -0.02
        m = base()
        viga(m, [(0, 0, 0), (L, 0, 0), (2 * L, 0, 0)],
             [(True, True, True, True, False, True), (False, True, True, True, False, True),
              (False, True, True, True, False, True)])
        m.nos["n1"] = No("n1", L, 0, 0, apoio=(False, True, True, True, False, True), recalques=(0, 0, D, 0, 0, 0))
        r = analisar(m, "G")
        R = 6 * self.EI * D / L**3
        self.assertAlmostEqual(r.reacoes["n1"][2], R, places=8)
        self.assertAlmostEqual(r.reacoes["n0"][2], -R / 2, places=8)
        self.assertAlmostEqual(r.reacoes["n2"][2], -R / 2, places=8)
        self.assertAlmostEqual(abs(r.esforcos["bn0"][-1][5]), 3 * self.EI * abs(D) / L**2, places=8)
        # recalque é ação permanente do 1º caso G: Q não o vê; combinar pondera por γ
        self.assertAlmostEqual(analisar(m, "Q").reacoes["n1"][2], 0.0)
        c = combinar({"G": r, "Q": analisar(m, "Q")}, {"G": 1.4, "Q": 1.5})
        self.assertAlmostEqual(c.reacoes["n1"][2], 1.4 * R, places=8)
        self.assertAlmostEqual(analisar(m, {"G": 1.4, "Q": 1.5}).reacoes["n1"][2], 1.4 * R, places=8)

    def test_recalque_em_gl_livre_erro(self):
        with self.assertRaisesRegex(ValueError, r"nó n9.*recalque.*uz.*não está travado"):
            No("n9", 0, 0, 0, apoio=(True, True, False, True, True, True), recalques=(0, 0, -0.01, 0, 0, 0))
        with self.assertRaisesRegex(ValueError, "mola"):
            No("n9", 0, 0, 0, molas=(0, 0, -1, 0, 0, 0))

    def test_modal_com_mola(self):
        # massa na ponta de barra axial + mola: ω² = (EA/L + k)/m
        L, P, k = 2.0, 50.0, 3e5
        m = base()
        m.add(No("n0", 0, 0, 0, apoio=ENG))
        m.add(No("n1", 0, 0, L, apoio=(True, True, False, True, True, True), molas=(0, 0, k, 0, 0, 0)))
        m.add(Barra("b", "n0", "n1", "s", "aco"))
        m.add(CargaNodal("G", "n1", (0, 0, -P, 0, 0, 0)))
        f, _, _ = modal(m, 1)
        w2 = (E * A / L + k) / (P / 9.80665)
        self.assertAlmostEqual(f[0], math.sqrt(w2) / (2 * math.pi), places=6)
