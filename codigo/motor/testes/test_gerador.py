"""Testes do gerador de estruturas e dos modelos de exemplo (ficha 009).

Valores de referência: contagens por fórmula (docstrings), solução fechada (M = qL²/8, reações
por simetria, equilíbrio global) e NBR 6120:2019 (6.4: cobertura leve, i ≥ 3% → q = 0,25 kN/m²;
6.2 tabela 10: escritório 2,5 kN/m²; 5.3 tabela 1: concreto armado 25 kN/m³).
"""
import filecmp
import importlib.util
import json
import math
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import secoes
from prossen_estrutural.gerador import ErroGerador, gerar_estrutura
from prossen_estrutural.materiais import aco
from prossen_estrutural.modelo import (Barra, CargaDistribuida, CargaNodal, CasoCarga, Modelo, No)
from prossen_estrutural.solver import analisar, eixos_locais

EXEMPLOS = os.path.join(os.path.dirname(__file__), "..", "exemplos")
ACO = "A-572 GR.50"
W = {"pilar": "W310x38.7", "viga": "W360x51"}


def _gerar_exemplos():
    spec = importlib.util.spec_from_file_location("gerar_exemplos", os.path.join(EXEMPLOS, "gerar_exemplos.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def soma_reacoes(r, k):
    return sum(float(v[k]) for v in r.reacoes.values())


class TestContagens(unittest.TestCase):
    def test_portico_plano(self):
        """nx vãos, np níveis: nós (nx+1)(np+1); pilares (nx+1)np; vigas nx·np; pavimentos np.
        2 vãos, 2 níveis → 9 nós, 6 + 4 = 10 barras, 2 pavimentos."""
        m = Modelo()
        r = gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": W,
                                "vaos_x": [6, 6], "alturas": [4, 3.5]})
        self.assertEqual((r["nos_criados"], r["barras_criadas"], r["pavimentos_criados"]), (9, 10, 2))
        self.assertEqual((len(m.nos), len(m.barras), len(m.pavimentos)), (9, 10, 2))
        self.assertEqual(r["ids"]["nos"], list(m.nos))
        self.assertIn("VX2-1-0", m.barras)
        self.assertFalse(any(p.diafragma for p in m.pavimentos.values()))
        self.assertAlmostEqual(m.pavimentos["P2"].z, 7.5)
        self.assertEqual(m.peso_proprio, "G1")
        self.assertEqual({"G1", "Q1"} - set(m.casos), set())

    def test_edificio(self):
        """nx × ny vãos, np níveis: nós (nx+1)(ny+1)(np+1); pilares (nx+1)(ny+1)np;
        VX nx(ny+1)np; VY ny(nx+1)np. 3×2×4: 60 nós; 48 + 36 + 32 = 116 barras; 4 pavimentos."""
        nx, ny, npv = 3, 2, 4
        m = Modelo()
        r = gerar_estrutura(m, {"tipo": "edificio", "material": {"concreto": 30},
                                "secoes": {"pilar": {"forma": "retangular", "b": 0.25, "h": 0.5},
                                           "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
                                "vaos_x": [5] * nx, "vaos_y": [5] * ny, "alturas": [3] * npv})
        self.assertEqual(r["nos_criados"], (nx + 1) * (ny + 1) * (npv + 1))
        self.assertEqual(r["barras_criadas"], (nx + 1) * (ny + 1) * npv + nx * (ny + 1) * npv + ny * (nx + 1) * npv)
        self.assertEqual((r["nos_criados"], r["barras_criadas"], r["pavimentos_criados"]), (60, 116, 4))
        self.assertTrue(all(p.diafragma for p in m.pavimentos.values()))
        self.assertTrue(all(b.tipo == "concreto" for b in m.barras.values()))
        self.assertEqual(m.barras["P2-1-1"].params["papel"], "pilar")
        self.assertEqual(m.barras["VY3-2-0"].params["papel"], "viga")

    def test_galpao(self):
        """n módulos → n+1 pórticos de 5 nós e 4 barras; 3 linhas de terças × n.
        n = 5: nós 5·6 = 30; barras 4·6 + 3·5 = 39 (24 sem terças); sem pavimentos."""
        d = {"tipo": "galpao", "material": ACO, "vao": 20, "altura_pilar": 6, "altura_cumeeira": 8,
             "n_modulos": 5, "espacamento": 6,
             "secoes": {"pilar": "W360x51", "viga": "W360x44.6", "terca": {"perfil": "Ue 200x75x25x3,00"}}}
        m = Modelo()
        r = gerar_estrutura(m, d)
        self.assertEqual((r["nos_criados"], r["barras_criadas"], r["pavimentos_criados"]), (30, 39, 0))
        self.assertEqual(m.barras["T1"].tipo, "pff")
        self.assertTrue(m.barras["T15"].params["rotula_i"] and m.barras["T15"].params["rotula_j"])
        self.assertEqual(m.barras["VX2-0-0"].tipo, "aco")
        self.assertTrue(any("SEM CONTRAVENTAMENTO" in a["mensagem"] for a in r["avisos"]))
        d["secoes"].pop("terca")
        m2 = Modelo()
        self.assertEqual(gerar_estrutura(m2, d)["barras_criadas"], 24)

    def test_geometria_galpao(self):
        m = Modelo()
        gerar_estrutura(m, {"tipo": "galpao", "material": ACO, "vao": 20, "altura_pilar": 6,
                            "altura_cumeeira": 8, "n_modulos": 2, "espacamento": 6,
                            "secoes": W, "origem": [1, 2, 3]})
        n = m.nos
        self.assertEqual((n["N2-1-0"].x, n["N2-1-0"].z), (11.0, 11.0))
        self.assertEqual((n["N1-2-2"].x, n["N1-2-2"].y, n["N1-2-2"].z), (21.0, 14.0, 9.0))


class TestPorticoPlano(unittest.TestCase):
    def test_igual_modelo_a_mao(self):
        """Pórtico 1 vão × 1 nível com q na viga: esforços e reações iguais aos de um modelo
        montado à mão (1e-9); reação vertical = qL/2 por simetria."""
        L, H, q = 6.0, 4.0, 12.0
        g = Modelo()
        gerar_estrutura(g, {"tipo": "portico_plano", "material": ACO, "secoes": W,
                            "vaos_x": [L], "alturas": [H]})
        g.add(CargaDistribuida("Q1", "VX1-0-0", (0, 0, -q)))

        h = Modelo()
        h.add(aco(ACO))
        for n in ("W310x38.7", "W360x51"):
            h.add(secoes.catalogo(n))
        h.add(CasoCarga("Q1", "Q"))
        fora = (False, True, False, True, False, True)  # uy, rx, rz presos (plano XZ)
        h.add(No("a", 0, 0, 0, (True,) * 6))
        h.add(No("b", L, 0, 0, (True,) * 6))
        h.add(No("c", 0, 0, H, fora))
        h.add(No("d", L, 0, H, fora))
        h.add(Barra("pe", "a", "c", "W310x38.7", ACO))
        h.add(Barra("pd", "b", "d", "W310x38.7", ACO))
        h.add(Barra("v", "c", "d", "W360x51", ACO))
        h.add(CargaDistribuida("Q1", "v", (0, 0, -q)))

        rg, rh = analisar(g, "Q1"), analisar(h, "Q1")
        for bg, bh in (("P1-0-0", "pe"), ("P1-1-0", "pd"), ("VX1-0-0", "v")):
            for lg, lh in zip(rg.esforcos[bg], rh.esforcos[bh]):
                for a, b in zip(lg, lh):
                    self.assertAlmostEqual(a, b, delta=1e-9)
        self.assertAlmostEqual(rg.reacoes["N0-0-0"][2], q * L / 2, delta=1e-9)
        self.assertAlmostEqual(rg.reacoes["N0-1-0"][2], q * L / 2, delta=1e-9)
        self.assertAlmostEqual(soma_reacoes(rg, 1), 0.0, delta=1e-9)  # nada fora do plano

    def test_apoio_rotula_sem_mecanismo(self):
        m = Modelo()
        gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": W,
                            "vaos_x": [6], "alturas": [4], "apoio": "rotula"})
        m.add(CargaDistribuida("Q1", "VX1-0-0", (0, 0, -10)))
        r = analisar(m, "Q1")
        self.assertAlmostEqual(abs(r.esforcos["P1-0-0"][0][5]), 0.0, delta=1e-9)  # base rotulada: M = 0


class TestEdificio(unittest.TestCase):
    def setUp(self):
        self.m = Modelo()
        self.r = gerar_estrutura(self.m, {
            "tipo": "edificio", "material": {"concreto": 30},
            "secoes": {"pilar": {"forma": "retangular", "b": 0.25, "h": 0.5},
                       "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
            "vaos_x": [5, 4], "vaos_y": [6], "alturas": [3, 3, 2.8], "origem": [0, 0, 0.5]})

    def test_nos_no_pavimento(self):
        m = self.m
        for p in m.pavimentos.values():
            nos = [n.id for n in m.nos.values() if abs(n.z - p.z) <= 0.001]
            self.assertEqual(len(nos), 3 * 2)
            self.assertTrue(all(i.startswith(f"N{p.nome[1:]}-") for i in nos))
        self.assertEqual([p.z for p in m.pavimentos.values()], [3.5, 6.5, 9.3])

    def test_equilibrio_global_g1(self):
        """ΣRz = peso próprio (Σ A·γ·L) + cargas aplicadas; ΣRx = ΣRy = 0."""
        m = self.m
        m.add(CargaDistribuida("G1", "VX1-0-0", (0, 0, -7.0)))
        m.add(CargaDistribuida("G1", "VY3-1-0", (0, 0, -3.0)))
        # vigas: X (5 e 4 m) em 2 linhas y × 3 níveis; Y (6 m) em 3 linhas x × 3 níveis
        pp = 25.0 * (0.25 * 0.5 * (3 + 3 + 2.8) * 6 + 0.2 * 0.5 * ((5 + 4) * 2 * 3 + 6 * 3 * 3))
        carga = 7.0 * 5 + 3.0 * 6
        r = analisar(m, {"G1": 1.0})
        self.assertAlmostEqual(soma_reacoes(r, 2), pp + carga, delta=1e-6)
        self.assertAlmostEqual(soma_reacoes(r, 0), 0.0, delta=1e-6)
        self.assertAlmostEqual(soma_reacoes(r, 1), 0.0, delta=1e-6)


class TestErros(unittest.TestCase):
    D = {"tipo": "portico_plano", "material": ACO, "secoes": W, "vaos_x": [6], "alturas": [4]}

    def test_conflito_de_ids(self):
        m = Modelo()
        gerar_estrutura(m, self.D)
        antes = json.dumps(m.to_dict(), sort_keys=True)
        with self.assertRaises(ValueError) as cm:
            gerar_estrutura(m, self.D)
        msg = str(cm.exception)
        for i in ("N0-0-0", "P1-0-0", "VX1-0-0", "P1"):
            self.assertIn(i, msg)
        self.assertEqual(json.dumps(m.to_dict(), sort_keys=True), antes)  # nada alterado

    def test_conflito_parcial_nao_deixa_lixo(self):
        m = Modelo()
        m.add(No("N1-1-0", 99, 99, 99))
        with self.assertRaises(ErroGerador) as cm:
            gerar_estrutura(m, self.D)
        self.assertIn("N1-1-0", cm.exception.erros[0]["mensagem"])
        self.assertEqual((len(m.nos), len(m.barras), len(m.secoes), len(m.casos)), (1, 0, 0, 0))

    def test_entradas_invalidas_reunidas(self):
        with self.assertRaises(ErroGerador) as cm:
            gerar_estrutura(Modelo(), {"tipo": "edificio", "material": "XYZ", "vaos_x": [5, -1],
                                       "alturas": [], "secoes": {"pilar": "W999x1"}, "apoio": "mola",
                                       "extra": 1})
        campos = {e["campo"] for e in cm.exception.erros}
        self.assertTrue({"vaos_x", "vaos_y", "alturas", "material", "secoes.pilar", "secoes.viga",
                         "apoio", "extra"} <= campos, campos)
        with self.assertRaises(ErroGerador):
            gerar_estrutura(Modelo(), {"tipo": "ponte"})
        with self.assertRaises(ErroGerador):
            gerar_estrutura(Modelo(), {"tipo": "galpao", "material": ACO, "secoes": W, "vao": 20,
                                       "altura_pilar": 6, "altura_cumeeira": 5, "n_modulos": 0,
                                       "espacamento": 6})

    def test_secao_existente_com_outras_propriedades(self):
        m = Modelo()
        m.add(secoes.secao_retangular(0.1, 0.1, nome="R 200x500"))
        with self.assertRaises(ErroGerador) as cm:
            gerar_estrutura(m, {"tipo": "portico_plano", "material": {"concreto": 30},
                                "secoes": {"pilar": {"forma": "retangular", "b": 0.2, "h": 0.5},
                                           "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
                                "vaos_x": [5], "alturas": [3]})
        self.assertIn("R 200x500", str(cm.exception))


class TestTercas(unittest.TestCase):
    def test_alma_normal_a_agua(self):
        """Eixo z local da terça = normal ao plano da água (±sen θ, 0, cos θ); cumeeira: vertical."""
        m = Modelo()
        gerar_estrutura(m, {"tipo": "galpao", "material": ACO, "vao": 20, "altura_pilar": 6,
                            "altura_cumeeira": 8, "n_modulos": 2, "espacamento": 6,
                            "secoes": {**W, "terca": {"perfil": "Ue 200x75x25x3,00"}}})
        th = math.atan2(2.0, 10.0)
        esq, cum, dir_ = (eixos_locais(m, b)[2] for b in ("T1", "T3", "T5"))
        for ez, esperado in ((esq, (-math.sin(th), 0, math.cos(th))), (cum, (0, 0, 1)),
                             (dir_, (math.sin(th), 0, math.cos(th)))):
            for a, b in zip(ez, esperado):
                self.assertAlmostEqual(a, b, delta=1e-12)

    def test_galpao_rotulado_analisa(self):
        m = Modelo()
        gerar_estrutura(m, {"tipo": "galpao", "material": ACO, "vao": 20, "altura_pilar": 6,
                            "altura_cumeeira": 8, "n_modulos": 2, "espacamento": 6, "apoio": "rotula",
                            "secoes": {**W, "terca": {"perfil": "Ue 200x75x25x3,00"}}})
        for j in range(3):
            for b in (f"VX2-0-{j}", f"VX2-1-{j}"):
                m.add(CargaDistribuida("Q1", b, (0, 0, -3.0)))
        r = analisar(m, "Q1")
        self.assertAlmostEqual(soma_reacoes(r, 2), 3 * 2 * 3.0 * math.hypot(10, 2), delta=1e-6)  # q por m de barra × L_barra


class TestGalpao3D(unittest.TestCase):
    """O galpão é 3D: só a base tem apoio (nada de uy/rx/rz fictício nos outros nós) e Fy não some."""

    def galpao(self, **extra):
        m = Modelo()
        d = {"tipo": "galpao", "material": ACO, "vao": 12, "altura_pilar": 6, "altura_cumeeira": 7,
             "n_modulos": 3, "espacamento": 5,
             "secoes": {**W, "terca": {"perfil": "Ue 200x75x25x3,00"}}, **extra}
        return m, gerar_estrutura(m, d)

    def test_so_a_base_tem_apoio(self):
        for ap in ("engaste", "rotula"):
            for contra in (False, True):
                m, _ = self.galpao(apoio=ap, contraventamento=contra)
                for n in m.nos.values():
                    if n.z > 0:
                        self.assertEqual(n.apoio, (False,) * 6, (ap, contra, n.id))

    def test_carga_em_y_nao_some(self):
        """ΣRy das bases = −Fy, com e sem contraventamento, engaste e rótula (sem mecanismo)."""
        for ap in ("engaste", "rotula"):
            for contra in (False, True):
                m, _ = self.galpao(apoio=ap, contraventamento=contra)
                m.add(CargaNodal("Q1", "N1-0-1", (0, 10.0, 0, 0, 0, 0)))
                self.assertAlmostEqual(soma_reacoes(analisar(m, "Q1"), 1), -10.0, delta=1e-8, msg=(ap, contra))

    def test_contraventamento_cria_diagonais_trelica_e_enrijece(self):
        m0, r0 = self.galpao()
        m1, r1 = self.galpao(contraventamento=True)
        diag = [b for b in m1.barras if b[:2] in ("DC", "DP")]
        self.assertEqual(sorted(diag), ["DC1", "DC2", "DC3", "DC4", "DP1", "DP2", "DP3", "DP4"])
        self.assertEqual(len(m1.barras) - len(m0.barras), 8)
        self.assertTrue(all(m1.barras[b].params["trelica"] for b in diag))
        self.assertTrue(any("SEM CONTRAVENTAMENTO" in a["mensagem"] for a in r0["avisos"]))
        self.assertFalse(any("SEM CONTRAVENTAMENTO" in a["mensagem"] for a in r1["avisos"]))
        for m in (m0, m1):
            m.add(CargaNodal("Q1", "N1-0-0", (0, 10.0, 0, 0, 0, 0)))
        uy = [max(abs(v[1]) for v in analisar(m, "Q1").deslocamentos.values()) for m in (m0, m1)]
        self.assertLess(uy[1], uy[0] / 20)

    def test_contraventamento_um_modulo_e_secao_propria(self):
        m, _ = self.galpao(n_modulos=1, contraventamento=True,
                           secoes={**W, "terca": {"perfil": "Ue 200x75x25x3,00"},
                                   "contraventamento": {"forma": "tubo_circular", "d": 0.1, "t": 0.005}})
        self.assertEqual(sorted(b for b in m.barras if b[0] == "D"), ["DC1", "DC2", "DP1", "DP2"])
        self.assertTrue(m.barras["DP1"].secao.startswith("TC"))

    def test_erros(self):
        with self.assertRaises(ErroGerador) as cm:  # sem terça não há escoras
            self.galpao(contraventamento=True, secoes=W)
        self.assertIn("contraventamento", str(cm.exception))
        with self.assertRaises(ErroGerador):
            self.galpao(contraventamento="sim")

    def test_rotula_sem_contraventamento_prende_so_rx(self):
        m, r = self.galpao(apoio="rotula")
        self.assertEqual(m.nos["N0-0-0"].apoio, (True, True, True, True, False, False))
        self.assertTrue(any("rotula" in a["mensagem"] for a in r["avisos"]))
        m, _ = self.galpao(apoio="rotula", contraventamento=True)
        self.assertEqual(m.nos["N0-0-0"].apoio, (True, True, True, False, False, False))


class TestExemplos(unittest.TestCase):
    def carrega(self, nome):
        return Modelo.from_json(os.path.join(EXEMPLOS, nome + ".json"))

    def test_todos_carregam_e_analisam_sem_mecanismo(self):
        arqs = sorted(f for f in os.listdir(EXEMPLOS) if f.endswith(".json"))
        self.assertEqual(arqs, ["edificio_concreto.json", "galpao_aco.json", "portico_aco.json",
                                "viga_biapoiada.json"])
        for a in arqs:
            m = self.carrega(a[:-5])
            self.assertTrue(m.nome, a)
            self.assertTrue(m.casos, a)
            for caso in m.casos:
                analisar(m, caso)  # ErroMecanismo falharia o teste

    def test_reproduziveis(self):
        with tempfile.TemporaryDirectory() as tmp:
            for caminho in _gerar_exemplos().gerar(tmp):
                self.assertTrue(filecmp.cmp(caminho, os.path.join(EXEMPLOS, os.path.basename(caminho)),
                                            shallow=False), os.path.basename(caminho))

    def test_viga_biapoiada(self):
        """Q1 = 10 kN/m, L = 6 m: Mmax = qL²/8 = 45 kN·m, reações qL/2 = 30 kN.
        G1 = 5 + γ·A (A da seção do catálogo, γ = 77 kN/m³ do aço)."""
        m = self.carrega("viga_biapoiada")
        L = 6.0
        r = analisar(m, "Q1")
        self.assertAlmostEqual(max(abs(p[5]) for p in r.esforcos["b1"]), 10 * L**2 / 8, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n1"][2], 30.0, delta=1e-9)
        self.assertAlmostEqual(r.reacoes["n2"][2], 30.0, delta=1e-9)
        g = 5.0 + 77.0 * m.secoes[m.barras["b1"].secao].A
        rg = analisar(m, "G1")
        self.assertAlmostEqual(max(abs(p[5]) for p in rg.esforcos["b1"]), g * L**2 / 8, delta=1e-9)
        self.assertAlmostEqual(rg.reacoes["n1"][2], g * L / 2, delta=1e-9)
        self.assertEqual(m.barras["b1"].params["elemento_desloc"], "viga_piso")

    def test_portico(self):
        """V1 = 10 kN em +X: ΣRx = −10; Q1 = 5 kN/m em 2 vãos de 6 m: ΣRz = 60."""
        m = self.carrega("portico_aco")
        self.assertEqual(len(m.barras), 3 + 2)
        self.assertAlmostEqual(soma_reacoes(analisar(m, "V1"), 0), -10.0, delta=1e-9)
        self.assertAlmostEqual(soma_reacoes(analisar(m, "Q1"), 2), 60.0, delta=1e-9)

    def test_galpao(self):
        """Q1 (NBR 6120 6.4, i = 20% ≥ 3% → 0,25 kN/m²) em 20 m × 30 m de projeção: ΣRz = 150 kN."""
        m = self.carrega("galpao_aco")
        self.assertEqual((len(m.nos), len(m.barras)), (30, 47))  # 39 + 4 diagonais de cobertura + 4 de parede
        self.assertAlmostEqual(soma_reacoes(analisar(m, "Q1"), 2), 0.25 * 20 * 30, delta=1e-8)
        pesos = {"pilar": 12 * 6.0, "viga": 12 * math.hypot(10, 2), "terca": 15 * 6.0,
                 "DC": 4 * math.hypot(10, 2, 6), "DP": 4 * math.hypot(6, 6)}
        sec = {"pilar": "W360x51", "viga": "HP250x62", "terca": "Ue 250x85x25x3,00", "DC": "TC 219,1x6,35",
               "DP": "TC 219,1x6,35"}
        g1 = 77.0 * sum(c * m.secoes[sec[k]].A for k, c in pesos.items())  # 146,85 kN (LEIAME)
        self.assertAlmostEqual(soma_reacoes(analisar(m, "G1"), 2), g1, delta=1e-8)
        self.assertAlmostEqual(g1, 146.85, delta=0.01)
        self.assertTrue(all(m.barras[b].params.get("trelica") for b in m.barras if b[:2] in ("DC", "DP")))

    def test_edificio(self):
        """Q1 = 2,5 kN/m² (escritório, tabela 10) × 15 × 10 m × 4 pav = 1500 kN.
        G1 = (0,12·25 + 1,0)·150·4 = 2400 (lajes+revestimento) + pilares 144 m·0,125 m²·25 = 450
        + vigas (45 + 40) m·4·0,1 m²·25 = 850 → 3700 kN."""
        m = self.carrega("edificio_concreto")
        self.assertEqual((len(m.nos), len(m.barras), len(m.pavimentos)), (60, 116, 4))
        self.assertAlmostEqual(soma_reacoes(analisar(m, "Q1"), 2), 1500.0, delta=1e-6)
        self.assertAlmostEqual(soma_reacoes(analisar(m, "G1"), 2), 3700.0, delta=1e-6)
        v = m.config["vento"]
        self.assertEqual((v["V0"], v["categoria"], v["classe"], v["dimensoes"]),
                         (35.0, "IV", "B", {"lx": 15.0, "ly": 10.0}))
        self.assertEqual(m.config["sismo"]["zona"], 0)


if __name__ == "__main__":
    unittest.main()
