"""Testes dos geradores de treliças, contraventamentos e terças (geradores_extra, docs/v3/TRELICAS.md).

Referências: contagens por fórmula (docstrings) e método dos nós (Hibbeler, Análise das Estruturas,
cap. 3) para a Pratt de 4 painéis com cargas nodais no banzo inferior.
"""
import copy
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto
from prossen_estrutural.gerador import ErroGerador, gerar_estrutura
from prossen_estrutural.modelo import CargaDistribuida, CargaNodal, Modelo
from prossen_estrutural.solver import analisar, eixos_locais

ACO = "A-572 GR.50"
S = {"banzo": "W150x13", "diagonal": "W150x13"}


def trelica(m=None, **kw):
    m = m or Modelo()
    d = {"tipo": "trelica_plana", "material": ACO, "secoes": S, "vao": 12, "altura": 1.5, "paineis": 6}
    d.update(kw)
    return m, gerar_estrutura(m, d)


def galpao():
    m = Modelo()
    gerar_estrutura(m, {"tipo": "galpao", "material": ACO, "secoes": {"pilar": "W310x38.7", "viga": "W360x51"},
                        "vao": 20, "altura_pilar": 6, "altura_cumeeira": 8, "n_modulos": 3, "espacamento": 6})
    return m


def N(res, barra):
    return res.esforcos[barra][0][1]


class TestTrelicaPlana(unittest.TestCase):
    def test_contagens(self):
        """n painéis, banzos paralelos: Pratt/Howe/Warren-V 2(n+1) nós, 2n banzos + n diagonais + (n+1) montantes;
        Warren (sem montantes) n+1 + n nós, n + (n-1) + 2n barras; K (n par) 2(n+1) + (n-1) nós,
        2n + 2n + 2 + 2(n-1) barras; Fink 7 nós e 11 barras. n = 6."""
        casos = {"pratt": (14, 25), "howe": (14, 25), "warren_v": (14, 25), "warren": (13, 23), "k": (19, 36)}
        for mod, (nn, nb) in casos.items():
            with self.subTest(mod):
                m, r = trelica(modelo=mod)
                self.assertEqual((r["nos_criados"], r["barras_criadas"]), (nn, nb))
                self.assertEqual((len(m.nos), len(m.barras)), (nn, nb))
        m, r = trelica(modelo="fink", banzo="duas_aguas", altura=0, altura2=3, paineis=4)
        self.assertEqual((r["nos_criados"], r["barras_criadas"]), (7, 11))

    def test_triangular_funde_nos_dos_apoios(self):
        """Tesoura (duas águas, altura 0 nos apoios): S0 e Sn são I0 e In → 2(n+1) − 2 nós; somem os 2 montantes
        de altura nula e a diagonal da ponta que repetiria o banzo → 25 − 4 = 21 barras."""
        m, r = trelica(banzo="duas_aguas", altura=0, altura2=2.5)
        self.assertEqual((r["nos_criados"], r["barras_criadas"]), (12, 21))
        self.assertAlmostEqual(max(n.z for n in m.nos.values()), 2.5)
        pares = [frozenset((b.ni, b.nj)) for b in m.barras.values()]
        self.assertEqual(len(pares), len(set(pares)))
        self.assertTrue(all(b.ni != b.nj for b in m.barras.values()))

    def test_geometria_banzos(self):
        _, _ = trelica()
        m, _ = trelica(banzo="arco", altura=1, altura2=3, prefixo="A-")
        self.assertAlmostEqual(m.nos["A-S3"].z, 3.0)          # flecha no meio
        self.assertAlmostEqual(m.nos["A-S0"].z, 1.0)
        self.assertAlmostEqual(m.nos["A-S1"].z, 1 + 2 * 4 * 2 * 10 / 144)  # parábola em x = 2
        m, _ = trelica(banzo="uma_agua", altura=1, altura2=3, prefixo="U-")
        self.assertAlmostEqual(m.nos["U-S6"].z, 3.0)
        self.assertAlmostEqual(m.nos["U-S3"].z, 2.0)
        m, _ = trelica(eixo="y", origem=[5, 2, 7], prefixo="Y-")
        self.assertEqual((m.nos["Y-I6"].x, m.nos["Y-I6"].y, m.nos["Y-I6"].z), (5, 14, 7))

    def test_diagonais_pratt_e_howe(self):
        """Pratt: diagonais descem para o meio (S0–I1 no painel da esquerda); Howe: sobem (I0–S1)."""
        m, _ = trelica(modelo="pratt", prefixo="P-")
        self.assertEqual((m.barras["P-D1"].ni, m.barras["P-D1"].nj), ("P-S0", "P-I1"))
        self.assertEqual((m.barras["P-D6"].ni, m.barras["P-D6"].nj), ("P-I5", "P-S6"))
        m, _ = trelica(modelo="howe", prefixo="H-")
        self.assertEqual((m.barras["H-D1"].ni, m.barras["H-D1"].nj), ("H-I0", "H-S1"))

    def test_rotulas_e_apoios(self):
        m, _ = trelica(prefixo="T-")
        self.assertTrue(m.barras["T-D1"].params.get("trelica"))
        self.assertFalse(m.barras["T-BS1"].params.get("trelica"))
        self.assertEqual(m.nos["T-I0"].apoio, (True, True, True, True, False, True))
        self.assertEqual(m.nos["T-I6"].apoio, (False, True, True, True, False, True))   # móvel em X
        self.assertEqual(m.nos["T-S3"].apoio, (False, True, False, True, False, True))  # travado fora do plano
        m, _ = trelica(prefixo="L-", rotulas="todas", travar_plano=False)
        self.assertTrue(all(b.params.get("trelica") for b in m.barras.values()))
        self.assertEqual(m.nos["L-S3"].apoio, (False,) * 6)

    def test_entre_dois_nos_existentes(self):
        m = Modelo()
        gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": {"pilar": "W310x38.7", "viga": "W360x51"},
                            "vaos_x": [10], "alturas": [5]})
        apoio_antes = m.nos["N1-0-0"].apoio
        r = gerar_estrutura(m, {"tipo": "trelica_plana", "material": ACO, "secoes": S, "altura": 1.2, "paineis": 5,
                                "entre": ["N1-0-0", "N1-1-0"]})
        self.assertEqual(r["nos_criados"], 2 * 6 - 2)
        self.assertNotIn("TR1-I0", m.nos)
        self.assertEqual(m.nos["N1-0-0"].apoio, apoio_antes)
        self.assertAlmostEqual(m.nos["TR1-S5"].x, 10)
        self.assertAlmostEqual(m.nos["TR1-S5"].z, 6.2)

    def test_pratt_metodo_dos_nos(self):
        """Pratt 4 painéis de 3 m, h = 3 m, P = 10 kN em I1, I2, I3 (rótulas em todas as barras).
        R = 15 kN. Nó S0: diagonal D1 = 15/sen45° = 21,21 kN (tração); corte no painel 2, momento em I2:
        banzo superior BS2 = −(15·6 − 10·3)/3 = −20 kN; corte no painel 2, momento em S1: BI2 = 15·3/3 = 15 kN."""
        m, _ = trelica(vao=12, altura=3, paineis=4, rotulas="todas", prefixo="P-")
        for i in (1, 2, 3):
            m.add(CargaNodal("Q1", f"P-I{i}", (0, 0, -10, 0, 0, 0)))
        res = analisar(m, "Q1")
        self.assertAlmostEqual(N(res, "P-D1"), 15 * math.sqrt(2), places=3)
        self.assertAlmostEqual(N(res, "P-BS2"), -20, places=3)
        self.assertAlmostEqual(N(res, "P-BI2"), 15, places=3)
        self.assertAlmostEqual(N(res, "P-BI1"), 0, places=3)
        self.assertAlmostEqual(res.reacoes["P-I0"][2], 15, places=6)
        # banzos contínuos (padrão): mesmas forças a menos de 2 %
        m, _ = trelica(vao=12, altura=3, paineis=4, prefixo="C-")
        for i in (1, 2, 3):
            m.add(CargaNodal("Q1", f"C-I{i}", (0, 0, -10, 0, 0, 0)))
        res = analisar(m, "Q1")
        self.assertAlmostEqual(N(res, "C-D1") / (15 * math.sqrt(2)), 1, delta=0.02)

    def test_todos_os_tipos_resolvem(self):
        for mod in ("pratt", "howe", "warren", "warren_v", "k"):
            for banzo in ("paralelo", "uma_agua", "duas_aguas", "arco"):
                with self.subTest(mod=mod, banzo=banzo):
                    kw = {} if banzo == "paralelo" else {"altura2": 3}
                    m, _ = trelica(modelo=mod, banzo=banzo, altura=0 if banzo == "duas_aguas" else 1, **kw)
                    for n in m.nos.values():
                        if n.z > 0.5:
                            m.add(CargaNodal("Q1", n.id, (0, 0, -10, 0, 0, 0)))
                    res = analisar(m, "Q1")
                    self.assertLess(max(abs(v[2]) for v in res.deslocamentos.values()), 0.05)

    def test_erros(self):
        with self.assertRaises(ErroGerador) as e:
            trelica(modelo="fink", paineis=6)
        self.assertIn("paineis", {x["campo"] for x in e.exception.erros})
        with self.assertRaises(ErroGerador) as e:
            trelica(chave_errada=1, modelo="xyz")
        self.assertEqual({x["campo"] for x in e.exception.erros}, {"chave_errada", "modelo"})
        m, _ = trelica(prefixo="A-")
        with self.assertRaises(ErroGerador):
            trelica(m, prefixo="A-", origem=[0, 5, 0])
        _, r = trelica(m, origem=[0, 5, 0])  # sem prefixo: o próximo livre
        self.assertTrue(all(i.startswith("TR1-") for i in r["ids"]["nos"]))


class TestSerie(unittest.TestCase):
    def test_tesouras_com_tercas(self):
        """4 tesouras triangulares Howe de 6 painéis (12 nós, 21 barras cada) + terças em 7 linhas × 3 vãos."""
        m = Modelo()
        r = gerar_estrutura(m, {"tipo": "trelicas_serie", "material": ACO, "secoes": {**S, "terca": "W150x13"},
                                "modelo": "howe", "banzo": "duas_aguas", "vao": 15, "altura": 0, "altura2": 2.5,
                                "paineis": 6, "n": 4, "espacamento": 6})
        self.assertEqual(r["nos_criados"], 4 * 12)
        self.assertEqual(r["barras_criadas"], 4 * 21 + 7 * 3)
        self.assertAlmostEqual(m.nos["TS1-4-I0"].y, 18)
        t = m.barras["TS1-T1"]
        self.assertTrue(t.params["rotula_i"] and t.params["rotula_j"])
        # alma normal ao plano da água: eixo local z perpendicular ao banzo superior
        ez = eixos_locais(m, "TS1-T1")[2]
        self.assertAlmostEqual(ez[0], -math.sin(math.atan(2.5 / 7.5)), places=6)


class TestContraventamento(unittest.TestCase):
    def test_x_entre_pilares(self):
        m = galpao()
        r = gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "x", "barras": ["P1-0-0", "P1-0-1"],
                                "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual((r["nos_criados"], r["barras_criadas"]), (0, 2))
        d1, d2 = m.barras["CV1-D1"], m.barras["CV1-D2"]
        self.assertEqual({d1.ni, d1.nj}, {"N0-0-0", "N1-0-1"})
        self.assertEqual({d2.ni, d2.nj}, {"N0-0-1", "N1-0-0"})
        self.assertTrue(d1.params["trelica"])

    def test_k_divide_o_pilar(self):
        m = galpao()
        r = gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "k", "barras": ["P1-0-0", "P1-0-1"],
                                "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual(r["ids"]["barras_removidas"], ["P1-0-1"])
        self.assertNotIn("P1-0-1", m.barras)
        self.assertAlmostEqual(m.nos["CV1-N1"].z, 3.0)
        self.assertEqual(m.barras["P1-0-1.1"].secao, "W310x38.7")

    def test_v_lambda_sem_barra_cria_escora(self):
        m = galpao()
        r = gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "lambda", "barras": ["P1-0-0", "P1-0-1"],
                                "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual((r["nos_criados"], r["barras_criadas"]), (1, 4))  # 2 diagonais + 2 escoras no beiral

    def test_mao_francesa(self):
        m = Modelo()
        gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": {"pilar": "W310x38.7", "viga": "W360x51"},
                            "vaos_x": [6], "alturas": [4]})
        r = gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "mao_francesa", "distancia": 0.8,
                                "painel": ["N0-0-0", "N0-1-0", "N1-1-0", "N1-0-0"], "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual(r["nos_criados"], 4)
        self.assertEqual(sorted(r["ids"]["barras_removidas"]), ["P1-0-0", "P1-1-0", "VX1-0-0"])
        self.assertEqual(len([b for b in m.barras if b.startswith("VX1-0-0.")]), 3)
        self.assertAlmostEqual(m.nos["CV1-N1"].z, 3.2)

    def test_cobertura_com_divisoes(self):
        """X no plano da água entre as tesouras dos pórticos 0 e 1, 3 painéis: 4 nós (2 por tesoura), 6 diagonais,
        2 escoras e 2 tesouras divididas em 3 trechos."""
        m = galpao()
        r = gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "x", "barras": ["VX2-0-0", "VX2-0-1"],
                                "divisoes": 3, "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual((r["nos_criados"], r["barras_criadas"], r["barras_removidas"]), (4, 6 + 2 + 6, 2))
        m.add(CargaNodal("Q1", "N2-1-0", (0, 5, 0, 0, 0, 0)))
        analisar(m, "Q1")  # não é mecanismo


class TestTercas(unittest.TestCase):
    def test_tercas_tirantes_e_cargas(self):
        m = galpao()
        m.add(CargaDistribuida("Q1", "VX2-0-0", (0, 0, -2)))
        L = math.hypot(10, 2)
        r = gerar_estrutura(m, {"tipo": "tercas", "barras": ["VX2-0-2", "VX2-0-0", "VX2-0-3", "VX2-0-1"],  # qualquer ordem
                                "espacamento": 1.5, "tirantes": 1,
                                "secoes": {"terca": "W150x13", "tirante": {"forma": "circular", "d": 0.0125}}})
        ndiv = math.ceil(L / 1.5)  # 7 trechos, 8 linhas
        self.assertEqual(r["barras_removidas"], 4)
        # nós: 6 internos por tesoura × 4 + 1 nó de tirante por terça (8 × 3)
        self.assertEqual(r["nos_criados"], (ndiv - 1) * 4 + (ndiv + 1) * 3)
        tc = [b for b in m.barras.values() if b.id.startswith("TC1-T")]
        self.assertEqual(len(tc), (ndiv + 1) * 3 * 2)
        self.assertAlmostEqual(tc[0].rot, -math.atan(2 / 10), places=9)
        self.assertEqual(len([b for b in m.barras if b.startswith("TC1-R")]), ndiv * 3)
        tot = sum(c.q[2] * (math.dist(*[(m.nos[n].x, m.nos[n].y, m.nos[n].z) for n in (m.barras[c.barra].ni, m.barras[c.barra].nj)]))
                  for c in m.cargas_distribuidas)
        self.assertAlmostEqual(tot, -2 * L, places=6)


class TestProjeto(unittest.TestCase):
    def test_erro_vira_erro_entrada(self):
        m = Modelo()
        with self.assertRaises(projeto.ErroEntrada):
            projeto.gerar_estrutura(m, {"tipo": "contraventamento", "modelo": "x"})
        r = projeto.gerar_estrutura(m, {"tipo": "trelica_plana", "material": ACO, "secoes": S, "vao": 10,
                                        "altura": 1, "paineis": 4})
        self.assertEqual(r["barras_criadas"], 17)

    def test_previa_nao_muda_o_modelo(self):
        m = galpao()
        nb = len(m.barras)
        r = projeto.previa_estrutura(m, {"tipo": "contraventamento", "modelo": "k", "barras": ["P1-0-0", "P1-0-1"],
                                         "secoes": {"contraventamento": "W150x13"}})
        self.assertEqual(len(m.barras), nb)
        self.assertEqual(r["removidas"], ["P1-0-1"])
        self.assertEqual(len(r["barras"]), 4)
        self.assertTrue(all(b["ni"] in r["nos"] and b["nj"] in r["nos"] for b in r["barras"]))


if __name__ == "__main__":
    unittest.main()
