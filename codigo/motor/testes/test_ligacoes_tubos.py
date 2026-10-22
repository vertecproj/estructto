"""v0.6 A3: tubo redondo soldado em tubo redondo (aco/ligacoes_tubos.py) e a classificação tubo–tubo no agrupamento."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco.ligacoes_tubos import sela
from prossen_estrutural.modelo import Barra, No, Secao

DADOS = dict(d_ramal=114.3, t_ramal=6.0, D_receptor=168.3, t_receptor=6.4, theta=45, b_solda=5)


class TestTuboTubo(unittest.TestCase):
    def test_registro_e_geometria(self):
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=100, V=0))
        g = r["geometria"]
        self.assertEqual((g["chapas"], g["parafusos"]), ([], []))
        p = g["pontas"][0]
        self.assertEqual([c["tipo"] for c in p["recortes"]], ["boca_peixe"])
        self.assertAlmostEqual(p["recortes"][0]["raio"], 0.16830 / 2)
        # solda = curva da sela: todo ponto na superfície do receptor (eixo (cos45, 0, sen45) pelo nó)
        a = [math.cos(math.pi / 4), 0, math.sin(math.pi / 4)]
        for q in g["soldas"][0]["pontos"]:
            q = [x / 1000 for x in q]
            t = sum(x * y for x, y in zip(q, a))
            self.assertAlmostEqual(math.dist(q, [t * x for x in a]), 0.16830 / 2, places=6)

    def test_solda_a_mao(self):
        # perímetro da sela > π·d (sela alongada a 45°); q = N/Lw; FwRd E70 perna 5: 0,6·0,707·5·485/1,35 = 762 N/mm
        Lw = sum(math.dist(a, b) for a, b in zip(sela(0.05715, 0.08415, math.pi / 4),
                                                  sela(0.05715, 0.08415, math.pi / 4)[1:] + sela(0.05715, 0.08415, math.pi / 4)[:1]))
        self.assertGreater(Lw, math.pi * 0.1143)
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=0))
        v = next(x for x in r["verificacoes"] if x["nome"].startswith("solda ramal"))
        self.assertAlmostEqual(v["solicitante"], 200 / Lw, places=3)
        self.assertAlmostEqual(v["resistente"], 0.6 * 0.707 * 0.005 * 485e3 / 1.35, places=1)
        self.assertTrue(any("NBR 16239" in a for a in r["avisos"]))

    def test_momento_entra_na_solda(self):
        # à mão: projeção da sela = círculo r = 57,15 mm ⇒ W = π r² = 1,0261e-2 m²; M = 2 kN·m ⇒ q = 194,9 kN/m
        from prossen_estrutural.aco.ligacoes_tubos import modulo_sela
        W = modulo_sela(sela(0.05715, 0.08415, math.pi / 4))
        self.assertAlmostEqual(W, math.pi * 0.05715 ** 2, delta=0.005 * W)   # polígono de 72 lados
        r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=0, V=0, M=2))
        v = next(x for x in r["verificacoes"] if x["nome"].startswith("solda ramal"))
        self.assertAlmostEqual(v["solicitante"], 2 / (math.pi * 0.05715 ** 2), delta=0.5)
        # com N e V: q = hypot(N/Lw + M/W, V/Lw) > só N
        r2 = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=50, M=2))
        r0 = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=200, V=50))
        q = lambda rr: next(x for x in rr["verificacoes"] if x["nome"].startswith("solda ramal"))["solicitante"]
        self.assertGreater(q(r2), q(r0) + 190)

    def test_momento_sem_W_reprova(self):
        from unittest import mock
        with mock.patch("prossen_estrutural.aco.ligacoes_tubos.modulo_sela", return_value=0.0):
            r = L.registro("tubo_tubo_soldado")["verificar"](DADOS, dict(N=10, M=1))
        self.assertFalse(r["ok"])
        self.assertTrue(any("W_sela" in x["nome"] or "módulo resistente" in a for x in r["verificacoes"] for a in r["avisos"]))

    def test_beta_maior_que_1_reprova(self):
        r = L.registro("tubo_tubo_soldado")["verificar"](dict(DADOS, d_ramal=200), dict(N=10))
        self.assertFalse(r["ok"])
        self.assertFalse(next(x for x in r["verificacoes"] if x["nome"].startswith("geometria: β"))["ok"])

    def test_classificacao_tubo_tubo(self):
        m = P.novo_modelo()
        for s in (secoes.secao_tubo_circular(0.1683, 0.0064, "TC168"), secoes.secao_tubo_circular(0.1143, 0.006, "TC114")):
            m.add(Secao(s.nome, s.A, s.Iy, s.Iz, s.J, s.props))
        m.add(No("N1", 0, 0, 0, apoio=(True,) * 6)); m.add(No("N2", 0, 0, 3)); m.add(No("N3", 0, 0, 6))
        m.add(No("N4", 2, 0, 1, apoio=(True,) * 6))
        m.add(Barra("P1", "N1", "N2", "TC168", "A-572 GR.50")); m.add(Barra("P2", "N2", "N3", "TC168", "A-572 GR.50"))
        m.add(Barra("D1", "N2", "N4", "TC114", "A-572 GR.50"))
        tipos = {(x["no"], x["barra"]): x["tipo"] for x in lm.membros(m)}
        self.assertEqual(tipos[("N2", "D1")], "tubo_tubo_soldado")


if __name__ == "__main__":
    unittest.main()
