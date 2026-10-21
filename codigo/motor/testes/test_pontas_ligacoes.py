"""G2: toda ligação do motor preenche geometria["pontas"] (docs/v3/PONTAS-CONTRATO.md) e o cálculo usa essa geometria.

Conferência à mão (test_calculo_usa_geometria): chapa de topo de W310x38.7 (bf = 165 mm) na alma de um W150x13
(h = 148, tf = 4,9 mm). Vão livre entre mesas do pilar: 148/2 − 4,9 = 69,1 mm < bf/2 = 82,5 mm → cope esq e dir,
altura = tf + 12 = 16,9 mm cada. Largura real da ponta = 165 − 2·16,9 = 131,2 mm; solda das mesas (filetes dos dois
lados) resiste 2·131,2 mm·FwRd, não 2·165 mm·FwRd."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco import placa_base


class TestTodosOsTipos(unittest.TestCase):
    def test_19_tipos_tem_pontas(self):
        tipos = P.tipos_ligacao()
        self.assertEqual(len(tipos), 19)
        for t in tipos:
            with self.subTest(t["tipo"]):
                r = L.verificar_ligacao(t["tipo"], t["exemplo"]["dados"], t["exemplo"]["esforcos"])
                pts = r["geometria"]["pontas"]
                self.assertTrue(pts)
                for p in pts:
                    self.assertEqual(set(p), {"barra", "extremo", "plano", "folga", "recortes"})
                    self.assertAlmostEqual(math.hypot(*p["plano"]["normal"]), 1.0)

    def test_placa_base_ponta_na_face_superior(self):
        sec = secoes.catalogo("W310x38.7")
        r = placa_base.verificar_placa_base(dict(H=0.45, B=0.3, esp=0.025), dict(N=-100, V=10, M=20), sec.props)
        (p,) = r["geometria"]["pontas"]
        self.assertEqual(p["recortes"], [])
        self.assertAlmostEqual(p["plano"]["ponto"][0], 0.025)        # x local = ao longo do pilar, subindo
        self.assertEqual(p["plano"]["normal"], [-1.0, 0.0, 0.0])


class TestCalculoUsaGeometria(unittest.TestCase):
    D = dict(perfil_viga="W310x38.7", perfil_pilar="W150x13", tp=19, d_mm=19.05, solda_mesa="filete", b_mesa=8)
    E = {"M": 40, "V": 50}

    def _solda_mesas(self, r):
        return next(v for v in r["verificacoes"] if v["nome"].startswith("solda das mesas (filetes"))

    def test_calculo_usa_geometria(self):
        reto = L.verificar_ligacao("chapa_topo_ajustada", self.D, self.E)
        rec = L.verificar_ligacao("chapa_topo_ajustada", dict(self.D, contexto_pontas={"tipo_no": "viga_pilar_alma"}),
                                  self.E)
        (p,) = rec["geometria"]["pontas"]
        self.assertEqual(sorted(c["face"] for c in p["recortes"]), ["dir", "esq"])
        largura = 0.165 - 2 * (0.0049 + 0.012)
        fw_reto, fw_rec = self._solda_mesas(reto)["resistente"], self._solda_mesas(rec)["resistente"]
        self.assertAlmostEqual(fw_rec / fw_reto, largura / 0.165, places=3)
        # chapa encostada na alma do pilar: face da chapa a tw/2, ponta da viga a tw/2 + tp
        self.assertAlmostEqual(p["plano"]["ponto"][0], 0.0043 / 2 + 0.019, places=4)


class TestBarraCurta(unittest.TestCase):
    """Review Focus 1: recuo + recorte ≥ comprimento → aviso, ok = False, motivo; a barra fica."""

    def test_barra_curta_vira_aviso(self):
        d = dict(TestCalculoUsaGeometria.D, contexto_pontas={"comprimento": 0.05})
        r = L.verificar_ligacao("chapa_topo_ajustada", d, TestCalculoUsaGeometria.E)
        self.assertFalse(r["ok"])
        self.assertEqual(r["motivo"], "barra curta demais para a ligação")
        self.assertTrue(any("barra curta demais" in a for a in r["avisos"]))
        self.assertTrue(r["geometria"]["pontas"])

    def test_barra_curta_no_grupo(self):
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                              "secoes": {"pilar": "W310x38.7", "viga": "W360x51"}, "vaos_x": [0.15], "alturas": [3]})
        P.ligacoes_identificar(m)
        gid = next(k for k, g in m.ligacoes["grupos"].items() if g["tipo"] == "viga_pilar_mesa")
        m.ligacoes["grupos"][gid]["projeto"] = dict(tipo_ligacao="chapa_topo_ajustada",
                                                    dados=dict(perfil_viga="W360x51", perfil_pilar="W310x38.7"))
        c = P.calcular(m)
        r = lm.dimensionar_grupo(m, c, gid)
        self.assertFalse(r["ok"])
        self.assertTrue(all(x["motivo"] == "barra curta demais para a ligação" for x in r["membros"]))


if __name__ == "__main__":
    unittest.main()
