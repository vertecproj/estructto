"""Regressão — trechos rígidos de extremidade e deformação por cortante (verificador-calculos, 2026-10-20).

Esperados de `verificacao/memorias/trechos-cortante-casos.py` (estática + PTV com termo V·v/(G·As) integrado só
no trecho flexível; método das forças nas hiperestáticas; EDO de 2ª ordem fechada no P-Δ). Não usa o código.
Relatório: `verificacao/2026-10-20-trechos-cortante.md`. Achado TC-01 = expectedFailure (o dono tira o decorador).
Esforços: (x, N, Vy, Vz, T, My, Mz) locais; deslocamentos e reações globais (ux, uy, uz, rx, ry, rz).
"""
import unittest

import numpy as np

from prossen_estrutural import secoes
from prossen_estrutural.modelo import (Barra, CargaDistribuida, CargaNodal, CargaPontual, CasoCarga, Material,
                                       Modelo, No, Secao)
from prossen_estrutural.solver import analisar

E, G = 2.0e8, 8.0e7
ENG = (True,) * 6
PINO = (True, True, True, True, False, False)
MOVEL = (False, True, True, False, False, False)
LIVRE = (False,) * 6
TOL = 1e-8  # elemento exato: só arredondamento


def _modelo(Iz=2e-5, peso=0.0):
    m = Modelo()
    m.add(Material("aco", E, G, peso))
    m.add(Secao("s", 0.01, 8e-5, Iz, 1e-6, props={"Ay": 2.5e-3, "Az": 4e-3}))
    m.add(CasoCarga("G", "G"))
    return m


def _pt(r, b, x):
    """(esforços, flechas) da 1ª saída da barra em x."""
    e = next(v for v in r.esforcos[b] if abs(v[0] - x) < 1e-9)
    f = next(v for v in r.flechas[b] if abs(v[0] - x) < 1e-9)
    return e, f


class TestTrechosCortante(unittest.TestCase):
    def proximo(self, obt, esp, tol=TOL):
        obt, esp = np.atleast_1d(np.asarray(obt, float)), np.atleast_1d(np.asarray(esp, float))
        np.testing.assert_allclose(obt, esp, rtol=tol, atol=tol * np.max(np.abs(esp)))

    def test_c1_biapoiada_trechos_assimetricos_cargas_dentro_e_fora(self):
        """L = 6, ti = 0,4, tj = 0,8, q = 10 em todo o vão, P = 7 / 12 / 5 em 0,2 (rígido i) / 2,5 / 5,6
        (rígido j). Estática do vão L + PTV só em [ti, L - tj] (Timoshenko & Gere, carga unitária)."""
        m = _modelo()
        m.add(No("i", 0, 0, 0, apoio=PINO)); m.add(No("j", 6, 0, 0, apoio=MOVEL))
        m.add(Barra("b", "i", "j", "s", "aco", params={"trecho_rigido_i": 0.4, "trecho_rigido_j": 0.8}))
        m.add(CargaDistribuida("G", "b", (0, 0, -10)))
        for a, P in ((0.2, 7), (2.5, 12), (5.6, 5)):
            m.add(CargaPontual("G", "b", a, (0, 0, -P)))
        r = analisar(m, "G")
        es = r.esforcos["b"]
        self.proximo([r.reacoes["i"][2], r.reacoes["j"][2]], [44.1, 39.9])
        self.proximo([es[0][0], es[0][3], es[0][5]], [0.4, -33.1, -15.44])
        self.proximo([es[-1][0], es[-1][3], es[-1][5]], [5.2, 26.9, -26.72])
        e, f = _pt(r, "b", 2.5)
        self.proximo([e[5], f[3]], [-62.9, -0.0136571140625])
        self.proximo([r.deslocamentos["i"][4], r.deslocamentos["j"][4]], [0.0073886375, -0.0068216125])

    def test_c2_biengastada_trechos_assimetricos(self):
        """L = 6, ti = 0,5, tj = 0,25, q = 10, P = 8 em 0,3 (rígido i), P = 15 em 3,4. Trecho flexível = viga
        biengastada nas faces (M = qLf²/12 + Pa'b'²/Lf²); conferido pelo método das forças. Também com o q vindo
        do peso próprio (γ·A = 10 kN/m), que deve cair inteiro, inclusive sobre os trechos rígidos."""
        for pp in (False, True):
            m = _modelo(peso=1000.0 if pp else 0.0)
            if pp:
                m.peso_proprio = "G"
            m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", 6, 0, 0, apoio=ENG))
            m.add(Barra("b", "i", "j", "s", "aco", params={"trecho_rigido_i": 0.5, "trecho_rigido_j": 0.25}))
            if not pp:
                m.add(CargaDistribuida("G", "b", (0, 0, -10)))
            m.add(CargaPontual("G", "b", 0.3, (0, 0, -8))); m.add(CargaPontual("G", "b", 3.4, (0, 0, -15)))
            r = analisar(m, "G")
            with self.subTest(peso_proprio=pp):
                self.proximo([r.reacoes["i"][2], r.reacoes["i"][4], r.reacoes["j"][2], r.reacoes["j"][4]],
                             [45.57574020084224, -51.622402413346286, 37.42425979915776, 42.767961208292846])
                self.proximo([r.esforcos["b"][0][5], r.esforcos["b"][-1][5]], [31.68453231292517, 33.724396258503404])
                self.proximo(_pt(r, "b", 3.4)[1][3], -0.0018930340885079228)

    def test_c3_balanco_timoshenko_com_trechos_e_rot(self):
        """Balanço L = 3 em X, rot = 0,3, ti = 0,3, tj = 0,4, Iy ≠ Iz, Ay ≠ Az. q = (0; 1,5; -2), P = (1; -2; -3) em
        2,8 (rígido j), nodal (0; 4; -6 | 0,5; 0,2; -0,3) na ponta. 6 deslocamentos da ponta por PTV (com e sem
        cortante) e esforços na face do engaste."""
        esp = {True: [1.1500000000000002e-06, 0.0017560842720353153, -0.004425096568299808, 0.014375000000000004,
                      0.0023247101331788965, 0.0008694495691651379],
               False: [1.1500000000000002e-06, 0.0017227075289152555, -0.004338836447518902, 0.014375000000000004,
                       0.0023247101331788965, 0.0008694495691651379]}
        for cort in (True, False):
            m = _modelo()
            m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", 3, 0, 0))
            m.add(Barra("b", "i", "j", "s", "aco", rot=0.3,
                        params={"trecho_rigido_i": 0.3, "trecho_rigido_j": 0.4, "cortante": cort}))
            m.add(CargaDistribuida("G", "b", (0, 1.5, -2))); m.add(CargaPontual("G", "b", 2.8, (1, -2, -3)))
            m.add(CargaNodal("G", "j", (0, 4, -6, 0.5, 0.2, -0.3)))
            r = analisar(m, "G")
            with self.subTest(cortante=cort):
                self.proximo(r.deslocamentos["j"], esp[cort])
                self.proximo(r.esforcos["b"][0][1:], [1.0, 1.5242947832866272, -15.54474269370983, 0.5,
                                                      33.038062962385894, 1.2603776987179032])

    def test_c4_portico_3d_no_rigido_girado_com_cortante(self):
        """Pilar vertical a-t (rot 0,5; ti 0,1; tj 0,25) + viga inclinada t-c (3; 2; 3,6) (rot -0,4; ti 0,2;
        tj 0,15), nó t rígido, cortante nas duas, cargas em todas as direções (q com componente axial na viga
        inclinada, P no trecho rígido e no flexível, nodais). Isostática: PTV 3D com 6 esforços."""
        m = _modelo()
        m.add(No("a", 0, 0, 0, apoio=ENG)); m.add(No("t", 0, 0, 3)); m.add(No("c", 3, 2, 3.6))
        m.add(Barra("P", "a", "t", "s", "aco", rot=0.5,
                    params={"trecho_rigido_i": 0.1, "trecho_rigido_j": 0.25, "cortante": True}))
        m.add(Barra("V", "t", "c", "s", "aco", rot=-0.4,
                    params={"trecho_rigido_i": 0.2, "trecho_rigido_j": 0.15, "cortante": True}))
        m.add(CargaDistribuida("G", "P", (1, 0.5, 0))); m.add(CargaDistribuida("G", "V", (0.5, -1, -4)))
        m.add(CargaPontual("G", "V", 0.1, (2, 1, -5))); m.add(CargaPontual("G", "V", 1.7, (0, 3, -6)))
        xm = 1.8525666882497067
        m.add(CargaPontual("G", "V", xm, (0, 0, 0)))  # força ponto de saída no meio do trecho flexível
        m.add(CargaNodal("G", "t", (4, 0, -10, 0, 0, 0))); m.add(CargaNodal("G", "c", (1, -2, -3, 0.5, 0.2, -0.3)))
        r = analisar(m, "G")
        tol = 1e-9 * 1e3  # PTV com Gauss: 1e-12 relativo; folga para o mal-condicionamento (GJ pequeno)
        self.proximo(r.deslocamentos["c"], [0.7865021900600615, -1.1383332148901042, -0.04337439877526745,
                                            0.013600259211527688, 0.024563501322716706, -0.3769873241450754], tol)
        self.proximo(r.deslocamentos["t"], [0.019352851648360883, -0.0006004423002906224, -5.117220689544689e-05,
                                            -0.0006240311615378768, 0.011075140343044505, -0.3793386150054024], tol)
        self.proximo(r.reacoes["a"], [-11.827566688249707, 0.15513337649941317, 38.620533505997656,
                                      21.816966895867377, -72.07677013492194, 11.451731773747998], tol)
        self.proximo(r.esforcos["V"][0], [0.2, -2.850754191035188, 5.54911657222312, -22.255888102787235,
                                          0.47057106344153776, 43.54774185869724, 6.5488046660162], tol)
        self.proximo(r.esforcos["P"][-1], [2.75, -38.620533505997656, -5.694835667312009, -7.232729211840386,
                                           -11.451731773747998, 49.59814938891698, 0.4051917302476384], tol)
        self.proximo(_pt(r, "V", xm)[1][1:], [0.015545302528448367, -0.6412221653074885, -0.29658221052974143], tol)

    def test_c5_biengastada_timoshenko_plano_xy(self):
        """Biengastada L = 2,5, plano xy (Vy, Iz, Ay; o teste do dono só cobre o plano xz). q = (0; -8; 0),
        P = (0; -10; 0) em 0,9. Método das forças com PTV e V·v/(G·Ay); parcela de q conferida por
        qL⁴/(384EIz) + qL²/(8G·Ay)."""
        m = _modelo()
        m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", 2.5, 0, 0, apoio=ENG))
        m.add(Barra("b", "i", "j", "s", "aco", params={"cortante": True}))
        m.add(CargaDistribuida("G", "b", (0, -8, 0))); m.add(CargaPontual("G", "b", 0.9, (0, -10, 0)))
        m.add(CargaPontual("G", "b", 1.25, (0, 0, 0)))
        r = analisar(m, "G")
        self.proximo([r.reacoes["i"][1], r.reacoes["i"][5], r.reacoes["j"][1], r.reacoes["j"][5]],
                     [17.021263482280435, 7.82324601951721, 12.978736517719565, -6.27008731381612])
        e, f = _pt(r, "b", 0.9)
        self.proximo([f[2], e[6]], [-0.00039015346687211103, 4.255891104713918], 1e-7)
        self.proximo(_pt(r, "b", 1.25)[1][2], -0.0004217317708333333)

    def test_c6_rotula_na_face_trechos_e_cortante(self):
        """Nó i engastado, ti = 0,3 com rotula_i (rótula na face), nó j apoio (uy, uz), tj = 0,35, L = 5,
        cortante. q = 6, P = 4 em 0,15 (rígido i), P = 9 em 2,0. Isostática: vão simples rótula-nó j."""
        for cort, uz in ((True, -0.0032708638667896237), (False, -0.003198983324830593)):
            m = _modelo()
            m.add(No("i", 0, 0, 0, apoio=ENG)); m.add(No("j", 5, 0, 0, apoio=MOVEL))
            m.add(Barra("b", "i", "j", "s", "aco", params={"trecho_rigido_i": 0.3, "trecho_rigido_j": 0.35,
                                                            "rotula_i": True, "cortante": cort}))
            m.add(CargaDistribuida("G", "b", (0, 0, -6)))
            m.add(CargaPontual("G", "b", 0.15, (0, 0, -4))); m.add(CargaPontual("G", "b", 2.0, (0, 0, -9)))
            r = analisar(m, "G")
            e, f = _pt(r, "b", 2.0)
            with self.subTest(cortante=cort):
                self.proximo([r.reacoes["i"][2], r.reacoes["i"][4], r.reacoes["j"][2]],
                             [25.644680851063832, -6.823404255319149, 17.35531914893617])
                self.assertAlmostEqual(r.esforcos["b"][0][5], 0.0, delta=1e-9)
                self.proximo([e[5], f[3]], [-25.065957437163835, uz])
                if cort:
                    self.proximo(r.deslocamentos["j"][4], -0.0022164827105555372)

    # ------------------------------------------------------------- P-Δ

    def _pilar(self, n, ti=0.0, tj=0.0, L=4.0, F=(10, 0, -900), cortante=False):
        m = _modelo(Iz=8e-5)
        for k, z in enumerate(np.linspace(0, L, n + 1)):
            m.add(No(f"n{k}", 0, 0, z, apoio=ENG if k == 0 else LIVRE))
        for k in range(n):
            p = {"cortante": cortante}
            if k == 0 and ti:
                p["trecho_rigido_i"] = ti
            if k == n - 1 and tj:
                p["trecho_rigido_j"] = tj
            m.add(Barra(f"b{k}", f"n{k}", f"n{k+1}", "s", "aco", params=p))
        m.add(CargaNodal("G", f"n{n}", (*F, 0, 0, 0)))
        r = analisar(m, "G", pdelta=True)
        return r.deslocamentos[f"n{n}"][0], abs(r.reacoes["n0"][4])

    def test_c7a_pdelta_trecho_rigido_no_topo_TC01(self):
        """TC-01. Pilar em balanço 4 m, tj = 0,4 no topo, P = 900 kN (0,30·Pcr do trecho flexível), H = 10 kN.
        Exato (EI·w'' = H(Lf + t - x) + P(Δ - w), Δ = w(Lf) + w'(Lf)·t; Timoshenko & Gere, Theory of Elastic
        Stability §1.11): Δ = 0,020847 m, M_base = 58,762 kN·m. Hoje: Δ = 0,018863 (-9,5 %), M = 54,45 (-7,3 %),
        a desfavor da segurança, e não converge com subdivisão: falta a rigidez geométrica dos trechos rígidos
        (N/t entre nó e face — o trecho rígido girado com N gera N·θ·t)."""
        D, M = self._pilar(4, tj=0.4)
        self.assertAlmostEqual(D / 0.020846752507064022, 1.0, delta=0.01)
        self.assertAlmostEqual(M / 58.76207725635762, 1.0, delta=0.01)

    def test_c7b_pdelta_trecho_rigido_na_base(self):
        """Mesmo pilar com ti = 0,4 na base (trecho rígido parado: exato = balanço de Lf = 3,6 m,
        Δ = H/(P·k)·(tan kLf - kLf)): Δ = 0,0137414 m, M_base = 52,3672 kN·m. Tol. 0,1 % (4 barras)."""
        D, M = self._pilar(4, ti=0.4)
        self.assertAlmostEqual(D / 0.01374135657631635, 1.0, delta=1e-3)
        self.assertAlmostEqual(M / 52.367220918684716, 1.0, delta=1e-3)

    def test_c8_pdelta_com_cortante_engesser(self):
        """Balanço L = 2 com cortante (Φ = 0,15), P = 0,3·Pcr de Engesser (Pcr = Pe/(1 + Pe/(G·Az))), H = 10 kN.
        Engesser (Timoshenko & Gere, Theory of Elastic Stability §2.8): Δ = 0,00245611 m, M_base = 27,0547.
        Aproximação declarada (Kg de Euler): erro medido 0,02 %; tol. 0,5 %."""
        D, M = self._pilar(4, L=2.0, F=(10, 0, -2872.2925964179844), cortante=True)
        self.assertAlmostEqual(D / 0.002456111187376957, 1.0, delta=5e-3)
        self.assertAlmostEqual(M / 27.054669979482217, 1.0, delta=5e-3)


class TestConvencaoAreasCisalhamento(unittest.TestCase):
    """Solver: Ay ↔ Vy (plano xy, Iz) e Az ↔ Vz (plano xz, Iy). secoes.py põe Secao.Iy = Ix (forte), logo o eixo
    local z é paralelo à alma: Az tem de ser a área da ALMA (≈ h·tw) e Ay a das MESAS (≈ 2·b·tf, com ou sem 5/6).
    Conferência cruzada: no benchmark PIPERACK (perfis W), alma = Az dá M máx. do PP a 1,7 % do CYPE; trocado, 5,3 %."""

    def _areas(self, s):
        if "Ay" not in s.props or "Az" not in s.props:
            self.skipTest("secoes.py ainda não gera Ay/Az (pendente, outro agente)")
        return s.props["Ay"], s.props["Az"]

    def test_I_alto_e_I_largo(self):
        for h, b, tw, tf in ((0.6, 0.15, 0.010, 0.012), (0.3, 0.3, 0.0075, 0.015)):
            Ay, Az = self._areas(secoes.secao_I(h, b, tw, tf))
            with self.subTest(h=h, b=b):
                self.assertTrue(0.85 <= Az / (h * tw) <= 1.05, f"Az = {Az} vs h·tw = {h * tw}")
                self.assertTrue(0.75 <= Ay / (2 * b * tf) <= 1.05, f"Ay = {Ay} vs 2·b·tf = {2 * b * tf}")

    def test_tubo_retangular_e_retangular(self):
        h, b, t = 0.2, 0.1, 0.005
        Ay, Az = self._areas(secoes.secao_tubo_retangular(h, b, t))
        self.assertGreater(Az, 1.5 * Ay)  # almas (lados h) levam Vz
        self.assertTrue(0.85 <= Az / (2 * h * t) <= 1.05)
        s = secoes.secao_retangular(0.2, 0.5)
        Ay, Az = self._areas(s)
        self.assertAlmostEqual(Ay / (5 / 6 * s.A), 1.0, delta=0.05)
        self.assertAlmostEqual(Az / (5 / 6 * s.A), 1.0, delta=0.05)


if __name__ == "__main__":
    unittest.main()
