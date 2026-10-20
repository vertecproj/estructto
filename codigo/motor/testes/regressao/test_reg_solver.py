"""Regressão do solver/modelo — verificador-calculos, ficha 012 (2026-10-20).

Valores esperados de soluções fechadas e benchmarks publicados (fonte em cada docstring), calculados
em `verificacao/memorias/solver-casos.py` sem usar o código. Achados: `verificacao/2026-10-20-solver.md`.
Esforços: tuplas (x, N, Vy, Vz, T, My, Mz) locais; reações globais (Fx, Fy, Fz, Mx, My, Mz).
"""
import json
import math
import unittest

import numpy as np

from prossen_estrutural import solver as S
from prossen_estrutural.modelo import (CargaDistribuida, CargaNodal, CargaPontual, CasoCarga, Barra,
                                       Material, Modelo, No, Pavimento, Secao)

E, G = 2.0e8, 8.0e7
FIX = (True,) * 6
LIVRE = (False,) * 6


def _modelo(secoes=(("s", 0.01, 8.581e-5, 4.29e-5, 1e-6),)):
    m = Modelo(nome="reg")
    m.add(Material("aco", E, G, 78.5))
    for nome, A, Iy, Iz, J in secoes:
        m.add(Secao(nome, A, Iy, Iz, J))
    m.add(CasoCarga("G", "G"))
    m.add(CasoCarga("Q", "Q"))
    return m


def _equilibrio(tc, m, r, fatores):
    """Σ reações + Σ cargas = 0 (forças), para cargas nodais, distribuídas (q·L) e pontuais."""
    tot = np.zeros(3)
    for c in m.cargas_nodais:
        tot += fatores.get(c.caso, 0.0) * np.array(c.F[:3], float)
    for c in m.cargas_distribuidas:
        b = m.barras[c.barra]
        ni, nj = m.nos[b.ni], m.nos[b.nj]
        L = math.dist((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z))
        tot += fatores.get(c.caso, 0.0) * np.array(c.q, float) * L
    for c in m.cargas_pontuais:
        tot += fatores.get(c.caso, 0.0) * np.array(c.F, float)
    reac = sum((np.asarray(v[:3]) for v in r.reacoes.values()), np.zeros(3))
    np.testing.assert_allclose(reac + tot, 0.0, atol=1e-7 * max(1.0, np.abs(tot).max()))


def _ponto(esf, x, ordem=0):
    """Linha de esforços em x (ordem 0 = primeira ocorrência, 1 = segunda, para a⁻/a⁺)."""
    linhas = [e for e in esf if abs(e[0] - x) < 1e-9]
    return linhas[ordem]


class TestCargaPontual(unittest.TestCase):
    def test_biapoiada(self):
        """P = 20 kN em a = 2 m, L = 6 m: R = 13,333/6,667; M(a) = Pab/L = 26,667 kN·m (My < 0,
        ESPEC §3); flecha em a = Pa²b²/(3EIL) = 4,1435e-3 m. Timoshenko & Gere, tabela de vigas."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, (True, True, True, True, False, False)))
        m.add(No("n2", 6, 0, 0, (False, True, True, False, False, False)))
        m.add(Barra("b1", "n1", "n2", "s", "aco"))
        m.add(CargaPontual("Q", "b1", 2.0, (0, 0, -20.0)))
        r = S.analisar(m, "Q", n_pontos=13)
        self.assertAlmostEqual(r.reacoes["n1"][2], 13.333333333333334, places=9)
        self.assertAlmostEqual(r.reacoes["n2"][2], 6.666666666666667, places=9)
        self.assertAlmostEqual(_ponto(r.esforcos["b1"], 2.0)[5], -26.666666666666668, places=9)
        uz = [f[3] for f in r.flechas["b1"] if abs(f[0] - 2.0) < 1e-9][0]
        self.assertAlmostEqual(uz / -0.0041435212161234775, 1.0, places=9)
        _equilibrio(self, m, r, {"Q": 1.0})

    def test_inclinada_componente_axial(self):
        """Barra biengastada (0,0,0)→(4,0,3), F = (0,0,−10) em a = 2: reações globais
        Ri = (−0,2304; 0; 6,3072), Rj = (0,2304; 0; 3,6928); My reação −5,76 (i) e +3,84 (j);
        N = −3,6 antes de a e +2,4 depois. McGuire et al., forças de engastamento; memória solver-casos.py."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX))
        m.add(No("n2", 4, 0, 3, FIX))
        m.add(Barra("b1", "n1", "n2", "s", "aco"))
        m.add(CargaPontual("Q", "b1", 2.0, (0, 0, -10.0)))
        r = S.analisar(m, "Q")
        np.testing.assert_allclose(r.reacoes["n1"][:3], [-0.2304, 0, 6.3072], atol=1e-9)
        np.testing.assert_allclose(r.reacoes["n2"][:3], [0.2304, 0, 3.6928], atol=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][4], -5.76, places=9)
        self.assertAlmostEqual(r.reacoes["n2"][4], 3.84, places=9)
        self.assertAlmostEqual(_ponto(r.esforcos["b1"], 2.0, 0)[1], -3.6, places=9)
        self.assertAlmostEqual(_ponto(r.esforcos["b1"], 2.0, 1)[1], 2.4, places=9)
        _equilibrio(self, m, r, {"Q": 1.0})

    def test_distribuida_global_inclinada(self):
        """q = (0,0,−4) kN/m (global, por metro de barra) em barra biengastada (0,0,0)→(4,0,3), L = 5:
        por simetria cada nó recebe (0, 0, qL/2 = 10); |M| = q_t·L²/12 = 3,2·25/12 = 6,6667 kN·m."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX))
        m.add(No("n2", 4, 0, 3, FIX))
        m.add(Barra("b1", "n1", "n2", "s", "aco"))
        m.add(CargaDistribuida("Q", "b1", (0, 0, -4.0)))
        r = S.analisar(m, "Q")
        np.testing.assert_allclose(r.reacoes["n1"][:3], [0, 0, 10.0], atol=1e-9)
        np.testing.assert_allclose(r.reacoes["n2"][:3], [0, 0, 10.0], atol=1e-9)
        self.assertAlmostEqual(abs(r.reacoes["n1"][4]), 3.2 * 25 / 12, places=9)


class TestRotulas(unittest.TestCase):
    def test_engastada_rotula_j(self):
        """Barra entre dois engastes com rótula em j, q = 10 kN/m: M_i = qL²/8 = 45, M_j = 0,
        R_i = 37,5, R_j = 22,5 (Timoshenko & Gere, viga engastada-apoiada)."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX))
        m.add(No("n2", 6, 0, 0, FIX))
        m.add(Barra("b1", "n1", "n2", "s", "aco", params={"rotula_j": True}))
        m.add(CargaDistribuida("Q", "b1", (0, 0, -10.0)))
        r = S.analisar(m, "Q")
        self.assertAlmostEqual(r.reacoes["n1"][2], 37.5, places=9)
        self.assertAlmostEqual(r.reacoes["n2"][2], 22.5, places=9)
        self.assertAlmostEqual(abs(r.reacoes["n1"][4]), 45.0, places=9)
        self.assertAlmostEqual(r.reacoes["n2"][4], 0.0, places=9)
        self.assertAlmostEqual(r.esforcos["b1"][-1][5], 0.0, places=9)
        self.assertAlmostEqual(r.esforcos["b1"][0][5], 45.0, places=9)   # momento negativo (tração em cima): My > 0

    def test_gerber_e_mecanismo(self):
        """Engaste n1 — rótula n2 — apoio n3. P em n2 só pode ir para o balanço n1–n2:
        δ = PL³/3EI (Timoshenko). Momento aplicado em n2 (giro sem rigidez) → ErroMecanismo citando
        'n2' e o GL de giro (ficha 001)."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX))
        m.add(No("n2", 3, 0, 0))
        m.add(No("n3", 7, 0, 0, (False, True, True, True, False, False)))
        m.add(Barra("b1", "n1", "n2", "s", "aco", params={"rotula_j": True}))
        m.add(Barra("b2", "n2", "n3", "s", "aco", params={"rotula_i": True}))
        m.add(CargaNodal("Q", "n2", (0, 0, -10.0, 0, 0, 0)))
        r = S.analisar(m, "Q")
        self.assertAlmostEqual(r.deslocamentos["n2"][2] / (-10.0 * 27 / (3 * E * 8.581e-5)), 1.0, places=9)
        self.assertAlmostEqual(r.reacoes["n3"][2], 0.0, places=9)
        m.add(CargaNodal("G", "n2", (0, 0, 0, 0, 5.0, 0)))
        with self.assertRaises(S.ErroMecanismo) as cm:
            S.analisar(m, "G")
        msg = str(cm.exception)
        self.assertIn("n2", msg)
        self.assertIn("ry", msg)

    def test_reciprocidade_com_rotulas(self):
        """Maxwell-Betti: u(n4, y | Fx unitário em n2) = u(n2, x | Fy unitário em n4) em pórtico 3D
        com barras rotuladas (matriz condensada tem de continuar simétrica)."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX)); m.add(No("n2", 0, 0, 3))
        m.add(No("n3", 5, 0, 0, FIX)); m.add(No("n4", 5, 0, 3))
        m.add(No("n5", 5, 4, 3, (True, True, True, False, False, False)))
        m.add(Barra("b1", "n1", "n2", "s", "aco"))
        m.add(Barra("b2", "n3", "n4", "s", "aco"))
        m.add(Barra("b3", "n2", "n4", "s", "aco", params={"rotula_i": True}))
        m.add(Barra("b4", "n4", "n5", "s", "aco", params={"rotula_j": True}))
        m.add(CargaNodal("G", "n2", (1.0, 0, 0, 0, 0, 0)))
        m.add(CargaNodal("Q", "n4", (0, 1.0, 0, 0, 0, 0)))
        rg, rq = S.analisar(m, "G"), S.analisar(m, "Q")
        self.assertAlmostEqual(rg.deslocamentos["n4"][1] / rq.deslocamentos["n2"][0], 1.0, places=9)


class TestDiafragma(unittest.TestCase):
    H = 100.0
    # memória solver-casos.py, caso_diafragma(): u, θ e força em cada pilar (y = 0: 42,62; y = 4: 7,38)
    U, TH = 0.00811562397641664, 0.0007369800196528006
    FX = (42.62037340320996, 42.62037340320996, 7.379626596790042, 7.379626596790042)

    def _portico(self, extra=False):
        m = _modelo((("A", 0.01, 2e-4, 1e-4, 1e-6), ("B", 0.01, 5e-5, 1e-4, 1e-6)))
        xy = [(0, 0), (6, 0), (0, 4), (6, 4)]
        for i, (x, y) in enumerate(xy, 1):
            m.add(No(f"b{i}", x, y, 0, FIX))
            m.add(No(f"t{i}", x, y, 3.0))
            m.add(Barra(f"p{i}", f"b{i}", f"t{i}", "A" if y == 0 else "B", "aco"))
        if extra:   # nó apoiado no nível do pavimento: não pode entrar no diafragma
            m.add(No("ap", 3, 6, 3.0, FIX))
        m.add(Pavimento("P1", 3.0))
        m.add(CargaNodal("Q", "t1", (self.H, 0, 0, 0, 0, 0)))
        return m

    def test_excentrico(self):
        """4 pilares em balanço (y = 0: Iy = 2e-4; y = 4: Iy = 5e-5), H = 100 kN em +X no canto (0,0):
        kx = 3EIy/h³, kt = GJ/h; sistema 3×3 no centro (3, 2) resolvido à mão (solver-casos.py)."""
        m = self._portico()
        r = S.analisar(m, "Q")
        u, v, th = r.pavimentos["P1"]
        self.assertAlmostEqual(u / self.U, 1.0, places=6)
        self.assertAlmostEqual(v, 0.0, places=12)
        self.assertAlmostEqual(th / self.TH, 1.0, places=6)
        for i, fx in enumerate(self.FX, 1):
            self.assertAlmostEqual(-r.reacoes[f"b{i}"][0] / fx, 1.0, places=6)
        _equilibrio(self, m, r, {"Q": 1.0})

    def test_no_apoiado_excluido(self):
        """Nó com apoio no nível do pavimento não entra no diafragma (ficha 001): forças nos pilares iguais."""
        r = S.analisar(self._portico(extra=True), "Q")
        for i, fx in enumerate(self.FX, 1):
            self.assertAlmostEqual(-r.reacoes[f"b{i}"][0] / fx, 1.0, places=6)
        np.testing.assert_allclose(r.reacoes["ap"], 0.0, atol=1e-9)


class TestCombinacaoPorDict(unittest.TestCase):
    def _m(self):
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX)); m.add(No("n2", 0, 0, 3)); m.add(No("n3", 5, 0, 3))
        m.add(No("n4", 5, 0, 0, FIX))
        m.add(Barra("c1", "n1", "n2", "s", "aco")); m.add(Barra("v1", "n2", "n3", "s", "aco"))
        m.add(Barra("c2", "n4", "n3", "s", "aco"))
        m.peso_proprio = "G"
        m.add(CargaDistribuida("G", "v1", (0, 0, -8.0)))
        m.add(CargaPontual("Q", "v1", 1.5, (0, 0, -12.0)))
        m.add(CargaNodal("Q", "n2", (6.0, 0, 0, 0, 0, 0)))
        return m

    def test_superposicao(self):
        """Linearidade: analisar(m, {G: 1,4, Q: 1,5}) = 1,4·analisar(G) + 1,5·analisar(Q) (1e-9)."""
        m = self._m()
        rc = S.analisar(m, {"G": 1.4, "Q": 1.5})
        rg, rq = S.analisar(m, "G"), S.analisar(m, "Q")
        for n in m.nos:
            np.testing.assert_allclose(rc.deslocamentos[n], 1.4 * rg.deslocamentos[n] + 1.5 * rq.deslocamentos[n],
                                       rtol=1e-9, atol=1e-12)
        for n in rc.reacoes:
            np.testing.assert_allclose(rc.reacoes[n], 1.4 * rg.reacoes[n] + 1.5 * rq.reacoes[n], rtol=1e-9, atol=1e-9)
        for b in m.barras:
            ec, eg, eq = (np.array(r.esforcos[b], float) for r in (rc, rg, rq))
            np.testing.assert_allclose(ec[:, 0], eg[:, 0])
            np.testing.assert_allclose(ec[:, 1:], 1.4 * eg[:, 1:] + 1.5 * eq[:, 1:], rtol=1e-9, atol=1e-9)

    def test_pesos_nodais(self):
        """Viga L = 4 biapoiada, P = 10 em a = 1, q = 2, nodal 5 kN em n2: repartição pelas reações
        verticais de engastamento P·b²(L+2a)/L³ = 8,4375 e P·a²(L+2b)/L³ = 1,5625 (McGuire),
        qL/2 = 4: n1 = 12,4375; n2 = 10,5625; Σ = 23 (equilíbrio)."""
        m = _modelo()
        m.add(No("n1", 0, 0, 0, (True, True, True, True, False, False)))
        m.add(No("n2", 4, 0, 0, (False, True, True, False, False, False)))
        m.add(Barra("b1", "n1", "n2", "s", "aco"))
        m.add(CargaPontual("Q", "b1", 1.0, (0, 0, -10.0)))
        m.add(CargaDistribuida("Q", "b1", (0, 0, -2.0)))
        m.add(CargaNodal("Q", "n2", (0, 0, -5.0, 0, 0, 0)))
        p = S.pesos_nodais(m, {"Q": 1.0})
        self.assertAlmostEqual(p["n1"], 12.4375, places=9)
        self.assertAlmostEqual(p["n2"], 10.5625, places=9)
        self.assertAlmostEqual(sum(p.values()), 23.0, places=9)


class TestCombinarPavimentos(unittest.TestCase):
    def test_combinar_soma_pavimentos(self):
        """ESPEC §7.1: combinar também combina `pavimentos` (linearidade: 2·r = combinar({Q: r}, {Q: 2}))."""
        m = TestDiafragma()._portico()
        r = S.analisar(m, "Q")
        rc = S.combinar({"Q": r}, {"Q": 2.0})
        np.testing.assert_allclose(rc.pavimentos["P1"], 2 * np.asarray(r.pavimentos["P1"]), rtol=1e-12)

    def test_pdelta_dict_igual_a_str(self):
        """P-Δ com dict {caso: 1,0} = P-Δ do caso (combinação analisada como um caso, ESPEC Decisões)."""
        t = TestPDeltaAISC()
        m = t._coluna(FIX, LIVRE)
        m.add(CargaNodal("C", "n8", (4.448222, 0, -444.8222, 0, 0, 0)))
        a, b = S.analisar(m, "C", pdelta=True), S.analisar(m, {"C": 1.0}, pdelta=True)
        np.testing.assert_allclose(a.deslocamentos["n8"], b.deslocamentos["n8"], rtol=1e-9)


class TestPDeltaAISC(unittest.TestCase):
    """AISC 360-16 Comentário C2 (figs. C-C2.2/C-C2.3), W14x48, L = 28 ft; 8 elementos; M 3%, desloc. 5%
    (tolerâncias do Pynite). Conversões em solver-casos.py."""
    kip, inch, ft = 4.448222, 0.0254, 0.3048

    def _coluna(self, base, topo):
        k, i, f = self.kip, self.inch, self.ft
        m = Modelo(nome="aisc")
        m.add(Material("a", 29000 * 6894.757, 11200 * 6894.757, 0.0))
        m.add(Secao("W14x48", 14.1 * i**2, 484 * i**4, 51.4 * i**4, 1.45 * i**4))
        m.add(CasoCarga("C", "Q"))
        L = 28 * f
        for n in range(9):
            ap = base if n == 0 else (topo if n == 8 else LIVRE)
            # pórtico plano XZ: uy, rx, rz contidos (sem isso P > Pcr do eixo fraco = instabilidade real, ficha 026)
            ap = tuple(a or c for a, c in zip(ap, (False, True, False, True, False, True)))
            m.add(No(f"n{n}", 0, 0, L * n / 8, ap))
        for n in range(8):
            m.add(Barra(f"b{n}", f"n{n}", f"n{n + 1}", "W14x48", "a"))
        return m

    def test_caso1(self):
        k, i, f = self.kip, self.inch, self.ft
        for P, M, d in [(0, 235, 0.197), (150, 269, 0.224), (300, 313, 0.261), (450, 375, 0.311)]:
            m = self._coluna((True, True, True, False, False, True), (True, True, False, False, False, False))
            for n in range(8):
                m.add(CargaDistribuida("C", f"b{n}", (0.2 * k / f, 0, 0)))
            m.add(CargaNodal("C", "n8", (0, 0, -P * k, 0, 0, 0)))
            r = S.analisar(m, "C", pdelta=True)
            self.assertAlmostEqual(abs(r.esforcos["b3"][-1][5]) / (M * k * i), 1.0, delta=0.03, msg=f"P={P}")
            self.assertAlmostEqual(abs(r.deslocamentos["n4"][0]) / (d * i), 1.0, delta=0.05, msg=f"P={P}")

    def test_caso2(self):
        k, i, f = self.kip, self.inch, self.ft
        for P, M, d in [(0, 336, 0.901), (100, 469, 1.33), (150, 598, 1.75), (200, 848, 2.56)]:
            m = self._coluna(FIX, LIVRE)
            m.add(CargaNodal("C", "n8", (1.0 * k, 0, -P * k, 0, 0, 0)))
            r = S.analisar(m, "C", pdelta=True)
            self.assertAlmostEqual(abs(r.reacoes["n0"][4]) / (M * k * i), 1.0, delta=0.03, msg=f"P={P}")
            self.assertAlmostEqual(abs(r.deslocamentos["n8"][0]) / (d * i), 1.0, delta=0.05, msg=f"P={P}")


class TestEixosLocais(unittest.TestCase):
    def test_orientacoes(self):
        """ESPEC §3: y = Z × x (não vertical), vertical y = Y; z = x × y; rot gira (y, z) em torno de x."""
        m = _modelo()
        for n, xyz in {"o": (0, 0, 0), "x": (2, 0, 0), "y": (0, 2, 0), "z": (0, 0, 2)}.items():
            m.add(No(n, *xyz))
        m.add(Barra("bx", "o", "x", "s", "aco")); m.add(Barra("by", "o", "y", "s", "aco"))
        m.add(Barra("bz", "o", "z", "s", "aco")); m.add(Barra("bd", "z", "o", "s", "aco"))
        m.add(Barra("br", "o", "x", "s", "aco", rot=math.pi / 2))
        esp = {"bx": ([1, 0, 0], [0, 1, 0], [0, 0, 1]), "by": ([0, 1, 0], [-1, 0, 0], [0, 0, 1]),
               "bz": ([0, 0, 1], [0, 1, 0], [-1, 0, 0]), "bd": ([0, 0, -1], [0, 1, 0], [1, 0, 0]),
               "br": ([1, 0, 0], [0, 0, 1], [0, -1, 0])}
        for b, e in esp.items():
            np.testing.assert_allclose(np.array(S.eixos_locais(m, b), float), np.array(e, float), atol=1e-12, err_msg=b)

    def test_mesma_matriz_da_rigidez(self):
        """Balanço L = 3, P = 1: flecha = PL³/(3EI) com o I do plano indicado por eixos_locais:
        horizontal rot = 0 e vertical → Iy; rot = π/2 → Iz; pilar com força em Y → Iz."""
        Iy, Iz, L = 8.581e-5, 4.29e-5, 3.0
        casos = [((L, 0, 0), 0.0, (0, 0, -1.0), 2, Iy), ((L, 0, 0), math.pi / 2, (0, 0, -1.0), 2, Iz),
                 ((0, 0, L), 0.0, (1.0, 0, 0), 0, Iy), ((0, 0, L), 0.0, (0, 1.0, 0), 1, Iz)]
        for fim, rot, F, gl, I in casos:
            m = _modelo()
            m.add(No("a", 0, 0, 0, FIX)); m.add(No("b", *fim))
            m.add(Barra("b1", "a", "b", "s", "aco", rot=rot))
            m.add(CargaNodal("Q", "b", tuple(F) + (0, 0, 0)))
            r = S.analisar(m, "Q")
            self.assertAlmostEqual(abs(r.deslocamentos["b"][gl]) / (L**3 / (3 * E * I)), 1.0, places=9,
                                   msg=f"{fim} rot={rot}")


class TestModal(unittest.TestCase):
    def test_frequencia_biapoiada(self):
        """f1 = (π/2L²)·√(EI/m̄), m̄ = 10/9,80665 t/m, L = 6, I = 8,581e-5 → 5,6606 Hz
        (Clough & Penzien). Massa concentrada em 20 elementos: tolerância 1%."""
        m = _modelo((("s", 0.01, 8.581e-5, 8.581e-5, 1e-6),))
        n = 20
        for k in range(n + 1):
            ap = (True, True, True, True, False, False) if k == 0 else (
                (False, True, True, False, False, False) if k == n else LIVRE)
            m.add(No(f"n{k}", 6.0 * k / n, 0, 0, ap))
        for k in range(n):
            m.add(Barra(f"b{k}", f"n{k}", f"n{k + 1}", "s", "aco"))
            m.add(CargaDistribuida("G", f"b{k}", (0, 0, -10.0)))
        freqs = S.modal(m, 1, {"G": 1.0})[0]
        self.assertAlmostEqual(freqs[0] / 5.660589518535826, 1.0, delta=0.01)


class TestSerializacao(unittest.TestCase):
    def _m(self):
        m = _modelo()
        m.add(No("n1", 0, 0, 0, FIX)); m.add(No("n2", 4, 0, 0))
        m.add(Barra("b1", "n1", "n2", "s", "aco", params={"rotula_j": True}))
        m.add(CargaNodal("Q", "n2", (0, 0, -1.0, 0, 0, 0)))
        m.add(CargaDistribuida("G", "b1", (0, 0, -2.0)))
        m.add(CargaPontual("Q", "b1", 1.0, (0, 0, -3.0)))
        m.add(Pavimento("P1", 0.0))
        m.peso_proprio = "G"
        return m

    def test_v1_ida_e_volta(self):
        """ESPEC §5: to_dict → from_dict → to_dict idêntico e serializável em JSON."""
        d = self._m().to_dict()
        json.dumps(d)
        self.assertEqual(Modelo.from_dict(d).to_dict(), d)
        self.assertEqual(d["versao_formato"], 1)

    def test_v0_sem_ids(self):
        """ESPEC §5: arquivo v0 (sem versao_formato, ids, cargas_pontuais, pavimentos) carrega com ids
        cn1/cd1 e apoio como tupla de 6 bool."""
        d = json.loads(json.dumps(self._m().to_dict()))
        for k in ("versao_formato", "cargas_pontuais", "pavimentos", "nome"):
            d.pop(k, None)
        for c in d["cargas_nodais"] + d["cargas_distribuidas"]:
            c.pop("id", None)
        m = Modelo.from_dict(d)
        self.assertEqual([c.id for c in m.cargas_nodais], ["cn1"])
        self.assertEqual([c.id for c in m.cargas_distribuidas], ["cd1"])
        self.assertIsInstance(m.nos["n1"].apoio, tuple)
        self.assertEqual(len(m.nos["n1"].apoio), 6)


if __name__ == "__main__":
    unittest.main()
