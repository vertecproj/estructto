"""Geometria das ligações x cálculo (docs/v3/GEOMETRIA-LIGACOES.md).

1. Placa de base menor que o perfil: a solda do cálculo é só a que assenta na chapa.
2. Enrijecedor da chapa de topo: a geometria traz as soldas dele (antes: chapa sem solda no desenho).
3. Placa no eixo fraco (deitada): solda e geometria trocam H↔B de forma coerente.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural.aco import ligacoes_rigidas as lr  # noqa: E402
from prossen_estrutural.aco import placa_base as pb  # noqa: E402

W = dict(tipo='I', h=.30, b=.15, tw=.0066, tf=.0097)   # ~W 310 x 32,7 (m)
E = dict(N=-50., V=10., M=5.)


class SoldaSobreAChapa(unittest.TestCase):
    def test_chapa_menor_que_o_perfil_limita_o_contorno(self):
        r = pb.verificar_solda_contorno(W, dict(pelo_perfil=False, H=.20, B=.12, solda=.006), E)
        # contorno retangular b × d limitado à chapa: 2·(120 + 200) = 640 mm (o perfil todo seria 900 mm)
        self.assertAlmostEqual(r['L'], .640, places=6)
        self.assertTrue(r['recortada'])
        self.assertTrue(any('Chapa menor que o perfil' in a for a in r.get('avisos', [])))
        cheio = pb.verificar_solda_contorno(W, dict(pelo_perfil=False, H=.50, B=.40, solda=.006), E)
        self.assertAlmostEqual(cheio['L'], .900, places=6)
        self.assertFalse(cheio['recortada'])
        self.assertGreater(r['tau'], cheio['tau'])   # menos solda = mais tensão

    def test_pelo_perfil_nao_recorta(self):
        r = pb.verificar_solda_contorno(W, dict(pelo_perfil=True, H=.10, B=.10, solda=.006), E)
        self.assertAlmostEqual(r['L'], .900, places=6)
        self.assertFalse(r['recortada'])

    def test_eixo_fraco_troca_as_direcoes(self):
        # deitada: d (ao longo de H) = b do perfil = 150; chapa H = 0,12 < 150 → 2·(120 + 300) = 840 mm
        r = pb.verificar_solda_contorno(W, dict(pelo_perfil=False, eixo_fraco=True, H=.12, B=.50, solda=.006), E)
        self.assertAlmostEqual(r['L'], .840, places=6)
        g = pb.geometria_placa(dict(pelo_perfil=True, sobra=.05, eixo_fraco=True), W)
        self.assertAlmostEqual(g['H'], .25)   # b + 2·sobra
        self.assertAlmostEqual(g['B'], .40)   # h + 2·sobra

    def test_tubo_circular_maior_que_a_chapa(self):
        tc = dict(tipo='TC', h=.20, b=.20, tw=.006, tf=.006)
        r = pb.verificar_solda_contorno(tc, dict(pelo_perfil=False, H=.16, B=.30, solda=.006), E)
        self.assertTrue(r['recortada'])
        self.assertAlmostEqual(r['intermediarios']['c'], .08, places=9)


class SoldaDoEnrijecedor(unittest.TestCase):
    def test_enrijecedor_da_chapa_de_topo_tem_solda(self):
        r = lr.verificar("chapa_topo_estendida", dict(perfil_viga="W460x89", d_mm=22.225, tp=22.4, enrijecedor_chapa=True,
                                                      t_enrijecedor=12.5, estendida="ambas"), {"M": -150, "V": 80})
        g = r["geometria"]
        enr = [c for c in g["chapas"] if c["nome"] == "enrijecedor da chapa de topo"]
        self.assertEqual(len(enr), 2)
        # cada enrijecedor: uma solda na chapa de topo e uma na mesa, no plano médio (y = 0)
        no_meio = [s for s in g["soldas"] if all(abs(p[1]) < 1e-9 for p in s["pontos"])]
        self.assertEqual(len(no_meio), 4)
        self.assertTrue(all(s["perna"] >= 5 for s in no_meio))


if __name__ == "__main__":
    unittest.main()
