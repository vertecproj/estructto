"""Placa de base: pilar na base (NBR 8800 5.5.1.2), solda automática ≤ parede / penetração total e o dimensionamento que
cresce a placa e troca o tipo de fixação (docs/PLACA-BASE-PILAR-SOLDA.md). Caso real do usuário: pilar Tubo 175×175×9,50,
N = −63,22 kN, V = 0, M = 89,51 kN·m, placa 300 × 230 × 19 com 4 × WB 5/8" hef 59, fck 20, chapa fy 250, E70."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import placa_base as pb

TUBO = "Tubo 175x175x9,50"
D = dict(H=.3, B=.23, esp=.019, borda=.05, pelo_perfil=False, fileiras=2, por_fileira=2, chumbador="WB 58312",
         fck=20000, fy_chapa=250000, eletrodo="E70", solda=0)


def corpo(fy_mpa):
    return dict(dados=dict(D, fy_pilar=fy_mpa * 1000), esforcos=dict(N=-63.22, V=0, M=89.51), secao=TUBO)


class TestPilarNaBase(unittest.TestCase):
    def test_resistencias_do_tubo(self):
        # Z = 390,7 cm³, tubo compacto (b/t = 15,4 < 1,12·√(E/fy)): MRd = Z·fy/γa1; NRd = A·fy/γa1 (Q = 1, χ = 1)
        sec = secoes.catalogo(TUBO).props
        for fy, mrd in ((250000, 88.8), (300000, 106.6)):
            r = pb.resistencia_pilar(sec, fy)
            self.assertAlmostEqual(r["MxRd"], sec["Zx"] * fy / 1.10, places=6)
            self.assertAlmostEqual(r["MxRd"], mrd, delta=.05)
            self.assertAlmostEqual(r["NRd"], sec["A"] * fy / 1.10, places=6)
        # NSd/NRd < 0,2: N/(2·NRd) + M/MRd
        v = pb.verificar_pilar_base(sec, 250000, -63.22, 89.51)
        self.assertAlmostEqual(v["taxa"], 63.22 / (2 * sec["A"] * 250000 / 1.1) + 89.51 / (sec["Zx"] * 250000 / 1.1), places=9)

    def test_a_fy250_pilar_reprova_e_nenhuma_placa_resolve(self):
        v = P.placa_base_verificar(None, None, corpo(250))
        self.assertFalse(v["pilar"]["ok"])
        self.assertAlmostEqual(v["pilar"]["taxa"], 1.031, delta=.001)
        self.assertEqual(v["pilar"]["mensagem"], "O pilar não resiste aos esforços na base (taxa 1,03): troque o perfil ou o "
                                                 "aço — nenhuma placa resolve.")
        self.assertEqual({"fixacao", "chapa", "solda"}, set(v["passos"]))   # o pilar fica fora dos 3 passos da placa
        self.assertEqual(v["pilar"]["verificacoes"][0]["nome"], "pilar: flexo-compressão na base")
        self.assertIn(v["pilar"]["mensagem"], v["resultado"]["avisos"])
        d = P.placa_base_dimensionar(None, None, corpo(250))
        self.assertFalse(d["achou"])
        self.assertIsNone(d["dados"])
        self.assertIn("nenhuma placa resolve", d["descricao"])
        s = P.placa_base_dimensionar(None, None, dict(corpo(250), opcoes=3))
        self.assertEqual(s["opcoes"], [])

    def test_b_fy300_dimensionar_acha_placa_maior_que_passa_em_tudo(self):
        v0 = P.placa_base_verificar(None, None, corpo(300))
        self.assertTrue(v0["pilar"]["ok"])
        self.assertFalse(v0["ok"])   # a placa do usuário não passa (fixação 50×, chapa 2,8×)
        d = P.placa_base_dimensionar(None, None, corpo(300))
        self.assertTrue(d["achou"], d["descricao"])
        v = d["verificacao"]
        self.assertTrue(v["ok"])
        self.assertTrue(all(p["ok"] and p["taxa"] <= 1 for p in v["passos"].values()), v["passos"])
        g = d["dados"]
        self.assertGreater(g["H"] * g["B"], .3 * .23)
        self.assertGreaterEqual(g["H"], .3 - 1e-9)
        self.assertGreaterEqual(g["B"], .23 - 1e-9)
        self.assertLessEqual(max(g["H"], g["B"]), .70 + 1e-9)
        # tipo atual (mecânico) não resolve: a busca troca de família e chega ao pré-concretado
        self.assertEqual(g["chumbador"], "")
        self.assertEqual(g["solda_tipo"], "penetracao")
        self.assertIn("penetração total", d["descricao"])
        # Sugerir: até 3 opções diferentes, todas passando
        s = P.placa_base_dimensionar(None, None, dict(corpo(300), opcoes=3))
        self.assertGreaterEqual(len(s["opcoes"]), 1)
        self.assertEqual(s["opcoes"][0]["dados"], g)
        for o in s["opcoes"]:
            self.assertTrue(P.placa_base_verificar(None, None, dict(corpo(300), dados=o["dados"]))["ok"])

    def test_c_solda_auto_nunca_acima_da_parede(self):
        sec = secoes.catalogo(TUBO).props
        esf = dict(N=-63.22, V=0, M=89.51)
        self.assertEqual(pb.pernas_filete(.0095)[-1], .0095)
        self.assertTrue(all(h <= .0095 + 1e-12 for h in pb.pernas_filete(.0095)))
        self.assertEqual(pb.perna_minima_contorno(sec, D, esf, ate_parede=True), 0.)
        r = pb.verificar_placa_base(dict(D, fy_pilar=300000), dict(esf, N_barra=-63.22, V_barra=0, M_barra=89.51), sec)
        self.assertLessEqual(r["solda"]["h"], .0095 + 1e-12)   # antes: "auto (25 mm)" numa parede de 9,5
        self.assertTrue(r["solda"].get("sem_filete"))
        self.assertFalse(r["solda"]["ok"])
        self.assertTrue(any("penetração total" in a for a in r["avisos"]))
        dg = pb.diagnosticar_solda(sec, dict(D, solda=.0095), esf)
        self.assertTrue(dg["sem_solucao"] and dg["penetracao"])
        self.assertIn("9,5", dg["porque"])
        # esforço pequeno: a automática acha um filete e ele cabe na parede
        h = pb.perna_minima_contorno(sec, D, dict(N=-10, V=2, M=5), ate_parede=True)
        self.assertTrue(0 < h <= .0095)

    def test_penetracao_total_metal_base_da_parede(self):
        sec = secoes.catalogo(TUBO).props
        esf = dict(N=-63.22, V=10, M=89.51)
        for fy, ok in ((250000, False), (300000, True)):
            r = pb.verificar_solda_penetracao(sec, dict(D, fy_pilar=fy), esf)
            rp = pb.resistencia_pilar(sec, fy)
            self.assertAlmostEqual(r["verificacoes"][0]["solicitante"], 63.22 / rp["NRd"] + 89.51 / rp["MxRd"], places=9)
            self.assertEqual(r["ok"], ok)
        # sem fy do pilar, o metal-base é o da chapa
        self.assertEqual(pb.verificar_solda_penetracao(sec, D, esf)["fy_metal_base"], 250000)


if __name__ == "__main__":
    unittest.main()
