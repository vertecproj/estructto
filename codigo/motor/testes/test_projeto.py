"""Testes da camada de operações (ficha 008, ESPEC §6): as 31 operações, atomicidade, números iguais aos
motores e um pórtico conferido à mão.

Pórtico de referência (solução fechada): pórtico biarticulado na base, pilares h e viga L com a mesma
rigidez EI, carga w uniforme na viga. Pela equação de rotação (McGuire, Gallagher & Ziemian, Matrix
Structural Analysis, cap. 1-2; equivale à fórmula de qualquer livro de Análise Estrutural):
    k = (Ib/L)/(Ic/h),  M_nó = (w·L²/12)·3/(3 + 2k),  H = M_nó/h,  V = w·L/2,
    M_vão = w·L²/8 − M_nó.
Com w = 1,4·10 kN/m (combinação ELU G, γg = 1,4 da NBR 8681), L = 6 m, h = 4 m: V = 42 kN,
M_nó = 29,0769 kN·m, H = 7,2692 kN, M_vão = 33,9231 kN·m.
"""
import copy
import json
import math
import os
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

import numpy as np

from prossen_estrutural import projeto as P
from prossen_estrutural import solver
from prossen_estrutural.aco import nbr8800
from prossen_estrutural.acoes import vento as vento_mod
from prossen_estrutural.concreto import nbr6118
from prossen_estrutural.modelo import CargaNodal

ENG_PLANO = [True, True, True, True, False, False]  # pé de pilar articulado em ry (no plano XZ)


def portico(q=10.0, L=6.0, h=4.0, secao="W310x38.7"):
    m = P.novo_modelo("Pórtico de teste")
    P.adicionar_lote(m, [
        {"tipo": "material", "dados": {"aco": "A-572 GR.50"}},
        {"tipo": "secao", "dados": {"perfil": secao}},
        {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": ENG_PLANO}},
        {"tipo": "no", "dados": {"id": "n2", "x": 0, "y": 0, "z": h}},
        {"tipo": "no", "dados": {"id": "n3", "x": L, "y": 0, "z": h}},
        {"tipo": "no", "dados": {"id": "n4", "x": L, "y": 0, "z": 0, "apoio": ENG_PLANO}},
        {"tipo": "barra", "dados": {"id": "b1", "ni": "n1", "nj": "n2", "secao": secao, "material": "A-572 GR.50"}},
        {"tipo": "barra", "dados": {"id": "b2", "ni": "n2", "nj": "n3", "secao": secao, "material": "A-572 GR.50"}},
        {"tipo": "barra", "dados": {"id": "b3", "ni": "n4", "nj": "n3", "secao": secao, "material": "A-572 GR.50"}},
        {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}},
        {"tipo": "carga_distribuida", "dados": {"caso": "G1", "barra": "b2", "q": [0, 0, -q]}},
    ])
    return m


def edificio_aco(vento=True, nx=3, ny=2, nz=4):
    m = P.novo_modelo("Edifício de aço")
    r = P.gerar_estrutura(m, {"tipo": "edificio", "material": {"aco": "A-572 GR.50"},
                              "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                              "vaos_x": [5.0] * nx, "vaos_y": [5.0] * ny, "alturas": [3.0] * nz})
    P.adicionar_lote(m, [{"tipo": "carga_distribuida", "dados": {"caso": "Q1", "barra": b, "q": [0, 0, -8.0]}}
                         for b in r["ids"]["barras"] if b.startswith("V")])
    if vento:
        P.configurar(m, {"vento": {"V0": 35, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 15, "ly": 10}}})
    return m


class TestErros(unittest.TestCase):
    def test_nao_encontrado_sem_aspas_e_limita_a_20(self):
        e = P.ErroNaoEncontrado("barra", "bx", [f"b{i}" for i in range(30)])
        self.assertFalse(str(e).startswith("'"))
        self.assertIn("barra 'bx' não encontrado", str(e))
        self.assertEqual(len(str(e).split("válidos: ")[1].rstrip(" …").split(", ")), 20)

    def test_entrada_junta_erros(self):
        e = P.ErroEntrada([{"campo": "a", "mensagem": "x"}, {"campo": "b", "mensagem": "y"}])
        self.assertEqual(len(e.erros), 2)
        self.assertIn("a: x", str(e))
        self.assertIsInstance(e, ValueError)
        self.assertIs(P.ErroMecanismo, solver.ErroMecanismo)


class TestModelo(unittest.TestCase):
    def test_novo_modelo_e_dict(self):
        m = P.novo_modelo("X")
        d = P.para_dict(m)
        self.assertEqual((d["nome"], d["versao_formato"]), ("X", 1))
        self.assertEqual(P.para_dict(P.de_dict(d))["nome"], "X")
        with self.assertRaises(P.ErroEntrada):
            P.de_dict({"versao_formato": 99})

    def test_de_dict_lista_todos_os_erros(self):
        with self.assertRaises(P.ErroEntrada) as c:
            P.de_dict({"nos": {"n1": {"x": "a", "y": 0, "z": 0}}, "barras": {"b1": {"ni": "n1"}},
                       "casos": {"G": {"tipo": "Z"}}})
        campos = [e["campo"] for e in c.exception.erros]
        self.assertIn("nos.n1.x", campos)
        self.assertIn("barras.b1.nj", campos)
        self.assertIn("casos.G.tipo", campos)

    def test_v0_carrega(self):
        v0 = {"nome": "antigo",
              "nos": {"n1": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True] * 6},
                      "n2": {"id": "n2", "x": 3, "y": 0, "z": 0, "apoio": [False] * 6}},
              "materiais": {"a": {"nome": "a", "E": 2e8, "G": 7.7e7, "peso": 77.0, "fy": 3.45e5, "fu": 4.5e5, "fck": None}},
              "secoes": {"s": {"nome": "s", "A": 0.005, "Iy": 8e-5, "Iz": 7e-6, "J": 1e-7, "props": {}}},
              "barras": {"b1": {"id": "b1", "ni": "n1", "nj": "n2", "secao": "s", "material": "a", "rot": 0.0,
                                "tipo": "aco", "params": {}}},
              "casos": {"G1": {"nome": "G1", "tipo": "G", "subtipo": ""}},
              "cargas_nodais": [{"caso": "G1", "no": "n2", "F": [0, 0, -10, 0, 0, 0]}],
              "cargas_distribuidas": [], "peso_proprio": None, "config": {}}
        m = P.de_dict(v0)
        self.assertEqual(m.cargas_nodais[0].id, "cn1")
        self.assertEqual(P.validar(m), [])
        self.assertEqual(P.resumo(m)["n_barras"], 1)

    def test_salvar_e_carregar(self):
        m = portico()
        with tempfile.TemporaryDirectory() as t:
            r = P.salvar(m, Path(t) / "sub" / "p.json")
            self.assertGreater(r["bytes"], 100)
            m2 = P.carregar(r["caminho"])
            self.assertEqual(P.para_dict(m2), P.para_dict(m))
            with self.assertRaises(P.ErroEntrada):
                P.carregar(Path(t) / "nao_existe.json")
            (Path(t) / "ruim.json").write_text("{nao é json", encoding="utf-8")
            with self.assertRaises(P.ErroEntrada) as c:
                P.carregar(Path(t) / "ruim.json")
            self.assertIn("JSON inválido", str(c.exception))

    def test_adicionar_todos_os_tipos(self):
        m = P.novo_modelo()
        self.assertEqual(P.adicionar(m, "no", {"x": 0, "y": 0, "z": 0, "apoio": "engaste"})["id"], "n1")
        self.assertEqual(P.adicionar(m, "no", {"x": 3, "y": 0, "z": 0, "apoio": "rolete_z"})["apoio"],
                         [False, False, True, False, False, False])
        self.assertEqual(P.adicionar(m, "material", {"concreto": 30})["nome"], "C30")
        self.assertEqual(P.adicionar(m, "material", {"nome": "X", "E_MPa": 1000, "G_MPa": 400, "peso_kN_m3": 10})["E"], 1e6)
        s = P.adicionar(m, "secao", {"forma": "retangular", "b": 0.2, "h": 0.5})
        self.assertAlmostEqual(s["A"], 0.1)
        b = P.adicionar(m, "barra", {"ni": "n1", "nj": "n2", "secao": s["nome"], "material": "C30"})
        self.assertEqual((b["id"], b["tipo"]), ("b1", "concreto"))
        P.adicionar(m, "caso", {"nome": "G1", "tipo": "G"})
        self.assertEqual(P.adicionar(m, "carga_nodal", {"caso": "G1", "no": "n2", "F": [0, 0, -5, 0, 0, 0]})["id"], "cn1")
        self.assertEqual(P.adicionar(m, "carga_distribuida", {"caso": "G1", "barra": "b1", "q": [0, 0, -2]})["id"], "cd1")
        self.assertEqual(P.adicionar(m, "carga_pontual", {"caso": "G1", "barra": "b1", "a": 1.0, "F": [0, 0, -3]})["id"], "cp1")
        self.assertEqual(P.adicionar(m, "pavimento", {"nome": "P1", "z": 3.0})["diafragma"], True)
        self.assertEqual(P.resumo(m)["n_cargas"], 3)

    def test_adicionar_erros(self):
        m = portico()
        casos = [("no", {"id": "n1", "x": 0, "y": 0, "z": 0}), ("no", {"x": float("nan"), "y": 0, "z": 0}),
                 ("no", {"x": 0, "y": 0, "z": 0, "foo": 1}), ("barra", {"ni": "n1", "nj": "n1", "secao": "W310x38.7", "material": "A-572 GR.50"}),
                 ("barra", {"ni": "n1", "nj": "zz", "secao": "x", "material": "y"}),
                 ("carga_pontual", {"caso": "G1", "barra": "b2", "a": 99, "F": [0, 0, 1]}),
                 ("carga_nodal", {"caso": "nao", "no": "n1", "F": [1, 2]}), ("caso", {"nome": "G1", "tipo": "G"}),
                 ("caso", {"nome": "Z", "tipo": "W"}), ("material", {"aco": "inexistente"}),
                 ("secao", {"forma": "I", "h": 0.3}), ("tipo_ruim", {})]
        for tipo, dados in casos:
            with self.subTest(tipo=tipo, dados=dados), self.assertRaises(P.ErroEntrada):
                P.adicionar(m, tipo, dados)
        with self.assertRaises(P.ErroEntrada) as c:  # erros do mesmo item vêm juntos
            P.adicionar(m, "barra", {"ni": "zz", "nj": "yy", "secao": "x", "material": "y"})
        self.assertGreaterEqual(len(c.exception.erros), 4)

    def test_lote_atomico(self):
        m = portico()
        antes = P.para_dict(m)
        ultimo = dict(m._ultimo_id)
        itens = [{"tipo": "no", "dados": {"id": "n9", "x": 1, "y": 1, "z": 1}},
                 {"tipo": "caso", "dados": {"nome": "Q1", "tipo": "Q"}},
                 {"tipo": "barra", "dados": {"ni": "n9", "nj": "ninexistente", "secao": "W310x38.7", "material": "A-572 GR.50"}},
                 {"tipo": "carga_nodal", "dados": {"caso": "Q1", "no": "n9", "F": [1, 2, 3, 4, 5, 6]}}]
        with self.assertRaises(P.ErroEntrada) as c:
            P.adicionar_lote(m, itens)
        self.assertTrue(any(e["campo"] == "itens[2].nj" for e in c.exception.erros))
        self.assertEqual(P.para_dict(m), antes)
        self.assertEqual(m._ultimo_id, ultimo)
        r = P.adicionar_lote(m, [itens[0], itens[1], itens[3]])  # sem o item ruim: passa e mantém o mesmo objeto
        self.assertEqual([x["tipo"] for x in r["criados"]], ["no", "caso", "carga_nodal"])
        self.assertIn("n9", m.nos)
        self.assertEqual(P.adicionar_lote(m, [])["criados"], [])

    def test_editar(self):
        m = portico()
        e = P.editar(m, "no", "n2", {"z": 5.0})
        self.assertEqual(e["z"], 5.0)
        self.assertEqual(m.nos["n2"].z, 5.0)
        self.assertEqual(P.editar(m, "barra", "b1", {"params": {"kx": 2.0, "Lb": 1.0}})["params"], {"kx": 2.0, "Lb": 1.0})
        self.assertEqual(P.editar(m, "barra", "b1", {"params": {"kx": None}})["params"], {"Lb": 1.0})
        self.assertEqual(P.editar(m, "carga_distribuida", "cd1", {"q": [0, 0, -7]})["q"], [0, 0, -7])
        self.assertEqual(P.editar(m, "caso", "G1", {"subtipo": "x"})["subtipo"], "x")
        self.assertEqual(P.editar(m, "material", "A-572 GR.50", {"fy_MPa": 300})["fy"], 300e3)
        self.assertEqual(P.editar(m, "secao", "W310x38.7", {"props": {"foo": 1}})["props"]["foo"], 1)
        self.assertEqual(P.editar(m, "pavimento", *[P.adicionar(m, "pavimento", {"nome": "P1", "z": 4})["nome"]],
                                  {"z": 4.5})["z"], 4.5)
        for args in (("no", "n2", {"id": "outro"}), ("no", "n2", {"x": float("inf")}), ("no", "zz", {"x": 1}),
                     ("barra", "b1", {"nj": "n1"}), ("barra", "b1", {}), ("caso", "G1", {"nome": "H"})):
            with self.subTest(args=args), self.assertRaises((P.ErroEntrada, P.ErroNaoEncontrado)):
                P.editar(m, *args)
        self.assertEqual(m.nos["n2"].z, 5.0)  # erro não deixa meio-edição
        with self.assertRaises(P.ErroEntrada):  # mover nó não pode zerar barra
            P.editar(m, "no", "n3", {"x": 0, "z": 5.0})

    def test_remover_com_dependentes(self):
        m = portico()
        with self.assertRaises(P.ErroEntrada) as c:
            P.remover(m, "barra", "b2")
        self.assertIn("carga_distribuida cd1", str(c.exception))
        self.assertIn("b2", m.barras)
        r = P.remover(m, "barra", "b2", cascata=True)
        self.assertEqual(r["removidos"], [{"tipo": "barra", "id": "b2"}, {"tipo": "carga_distribuida", "id": "cd1"}])
        m = portico()
        with self.assertRaises(P.ErroEntrada):
            P.remover(m, "no", "n2")
        r = P.remover(m, "no", "n2", cascata=True)
        self.assertEqual({(x["tipo"], x["id"]) for x in r["removidos"]},
                         {("no", "n2"), ("barra", "b1"), ("barra", "b2"), ("carga_distribuida", "cd1")})
        m = portico()
        with self.assertRaises(P.ErroEntrada):
            P.remover(m, "secao", "W310x38.7")
        with self.assertRaises(P.ErroEntrada):
            P.remover(m, "caso", "G1")
        P.configurar(m, {"peso_proprio": "G1"})
        P.remover(m, "caso", "G1", cascata=True)
        self.assertIsNone(m.peso_proprio)
        self.assertEqual(m.cargas_distribuidas, [])
        with self.assertRaises(P.ErroNaoEncontrado) as c:
            P.remover(m, "barra", "nao")
        self.assertIn("b1", str(c.exception))
        self.assertEqual(P.remover(P.adicionar(m, "pavimento", {"nome": "X", "z": 1}) and m, "pavimento", "X")["removidos"],
                         [{"tipo": "pavimento", "id": "X"}])

    def test_listar(self):
        m = portico()
        self.assertEqual([b["id"] for b in P.listar(m, "barra")], ["b1", "b2", "b3"])
        self.assertEqual(len(P.listar(m, "nos")), 4)  # plural aceito
        self.assertEqual([c["id"] for c in P.listar(m, "carga_distribuida", {"caso": "G1"})], ["cd1"])
        self.assertEqual(P.listar(m, "carga_distribuida", {"caso": "outro"}), [])
        self.assertEqual(P.listar(m, "caso")[0]["id"], "G1")
        with self.assertRaises(P.ErroEntrada):
            P.listar(m, "coisa")

    def test_configurar(self):
        m = P.novo_modelo()
        cfg = P.configurar(m, {"tipo_uso": "garagem", "pdelta": True,
                               "vento": {"V0": 35, "categoria": "IV", "classe": "B"},
                               "sismo": {"zona": 0}})
        self.assertEqual((cfg["tipo_uso"], cfg["pdelta"], cfg["caa"]), ("garagem", True, "II"))
        self.assertEqual(cfg["vento"]["V0"], 35)
        cfg = P.configurar(m, {"vento": {"V0": 40, "categoria": "II", "classe": "A"}})  # substitui inteiro
        self.assertNotIn("S1", cfg["vento"])
        self.assertNotIn("vento", P.configurar(m, {"vento": None}))
        with self.assertRaises(P.ErroEntrada) as c:
            P.configurar(m, {"vento": {"V0": -1, "categoria": "VI", "classe": "Z"}, "sismo": {"zona": 7},
                             "caa": "V", "foo": 1, "peso_proprio": "nao"})
        campos = {e["campo"] for e in c.exception.erros}
        self.assertTrue({"vento.V0", "vento.categoria", "vento.classe", "sismo.zona", "caa", "foo",
                         "peso_proprio"} <= campos)
        self.assertNotIn("vento", m.config)  # erro não altera a config

    def test_resumo_e_geometria(self):
        m = portico()
        r = P.resumo(m)
        self.assertEqual((r["n_nos"], r["n_barras"], r["n_cargas"]), (4, 3, 1))
        self.assertEqual(r["caixa"], {"min": [0, 0, 0], "max": [6, 0, 4]})
        g = P.geometria(m)
        self.assertEqual(g["nome"], "Pórtico de teste")
        b1 = next(b for b in g["barras"] if b["id"] == "b1")
        self.assertAlmostEqual(b1["L"], 4.0)
        self.assertEqual(b1["eixos"]["x"], [0.0, 0.0, 1.0])
        self.assertEqual(g["cargas"]["G1"][0], {"tipo": "carga_distribuida", "id": "cd1", "alvo": "b2",
                                                "valores": [0, 0, -10.0]})
        P.adicionar(m, "carga_pontual", {"caso": "G1", "barra": "b2", "a": 2.0, "F": [0, 0, -4]})
        P.adicionar(m, "carga_nodal", {"caso": "G1", "no": "n2", "F": [1, 0, 0, 0, 0, 0]})
        g = P.geometria(m)
        self.assertEqual({c["tipo"] for c in g["cargas"]["G1"]}, {"carga_nodal", "carga_distribuida", "carga_pontual"})
        self.assertEqual(next(c for c in g["cargas"]["G1"] if c["tipo"] == "carga_pontual")["valores"], [2.0, 0, 0, -4])
        json.dumps(g)

    def test_validar(self):
        m = P.novo_modelo()
        cod = {v["codigo"] for v in P.validar(m)}
        self.assertTrue({"sem_barras", "sem_casos"} <= cod)
        m = portico()
        self.assertEqual([v for v in P.validar(m) if v["nivel"] == "erro"], [])
        m.nos["n1"].apoio = m.nos["n4"].apoio = (False,) * 6
        self.assertIn("sem_apoio", {v["codigo"] for v in P.validar(m)})
        m = portico()
        P.adicionar(m, "no", {"x": 9, "y": 9, "z": 9})
        P.adicionar(m, "caso", {"nome": "Q1", "tipo": "Q"})
        P.configurar(m, {"vento": {"V0": 35, "categoria": "IV", "classe": "B"}})
        P.adicionar(m, "secao", {"forma": "retangular", "b": 0.2, "h": 0.4})
        P.adicionar(m, "material", {"concreto": 25})
        P.adicionar(m, "barra", {"ni": "n1", "nj": "n3", "secao": "R 200x400", "material": "A-572 GR.50", "tipo": "pff",
                                 "params": {"typo": 1}})
        P.adicionar(m, "barra", {"ni": "n1", "nj": "n3", "secao": "W310x38.7", "material": "A-572 GR.50", "tipo": "concreto"})
        m.barras["b2"].nj = "fantasma"
        m.cargas_nodais.append(CargaNodal("QX", "n1", (0,) * 6, "cnx"))
        v = {(x["codigo"], x["nivel"]) for x in P.validar(m)}
        for esperado in (("no_isolado", "aviso"), ("caso_sem_carga", "aviso"), ("vento_sem_pavimentos", "erro"),
                         ("referencia_ausente", "erro"), ("secao_incompativel", "aviso"),
                         ("material_incompativel", "aviso"), ("param_desconhecido", "aviso")):
            self.assertIn(esperado, v)
        m = portico()
        m.barras["b3"].nj = "n4"
        self.assertIn("barra_comprimento_nulo", {x["codigo"] for x in P.validar(m)})
        m = portico()
        P.adicionar(m, "carga_pontual", {"caso": "G1", "barra": "b1", "a": 1, "F": [0, 0, -1]})
        m.cargas_pontuais[0].a = 99.0  # fora da barra (L = 4 m)
        self.assertIn("carga_fora_da_barra", {x["codigo"] for x in P.validar(m)})

    def test_gerar_estrutura_delega_e_converte_erro(self):
        m = P.novo_modelo()
        r = P.gerar_estrutura(m, {"tipo": "portico_plano", "material": {"aco": "A-36"},
                                  "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W310x38.7"}},
                                  "vaos_x": [6.0], "alturas": [3.0]})
        self.assertEqual((r["nos_criados"], r["barras_criadas"]), (4, 3))
        self.assertEqual([v for v in P.validar(m) if v["nivel"] == "erro"], [])
        with self.assertRaises(P.ErroEntrada) as c:
            P.gerar_estrutura(m, {"tipo": "edificio"})
        self.assertGreater(len(c.exception.erros), 1)


class TestAcoes(unittest.TestCase):
    def test_gerar_acoes_vento_igual_ao_motor(self):
        m = edificio_aco()
        r = P.gerar_acoes(m)
        self.assertEqual(r["casos"], ["V+X", "V-X", "V+Y", "V-Y"])
        pavs = [(p.nome, p.z) for p in m.pavimentos.values()]
        ref = {c["caso"]: c for c in vento_mod.casos_vento(m.config["vento"], pavs, m.config["vento"]["dimensoes"])}
        for nome, c in ref.items():
            self.assertEqual(m.casos[nome].tipo, "V")
            self.assertEqual(m.casos[nome].subtipo, "vento")
            carga = [k for k in m.cargas_nodais if k.caso == nome]
            self.assertTrue(all(k.id.startswith("auto_") for k in carga))
            self.assertAlmostEqual(sum(k.F[0] for k in carga), sum(f["F_vet_kN"][0] for f in c["forcas"]), places=9)
            self.assertAlmostEqual(sum(k.F[1] for k in carga), sum(f["F_vet_kN"][1] for f in c["forcas"]), places=9)
            self.assertAlmostEqual(sum(f["F_kN"] for f in r["forcas"][nome]), sum(f["F_kN"] for f in c["forcas"]), places=9)
        # idempotente: nada duplica
        n = len(m.cargas_nodais)
        P.gerar_acoes(m)
        self.assertEqual(len(m.cargas_nodais), n)
        # remover o vento da config limpa os casos automáticos
        P.configurar(m, {"vento": None})
        P.gerar_acoes(m)
        self.assertNotIn("V+X", m.casos)
        self.assertFalse([k for k in m.cargas_nodais if k.id.startswith("auto_")])

    def test_gerar_acoes_sismo_e_zona_zero(self):
        m = edificio_aco(vento=False)
        P.configurar(m, {"sismo": {"zona": 0}})
        r = P.gerar_acoes(m)
        self.assertEqual(r["casos"], [])
        self.assertIn("sismo_dispensado", [a["codigo"] for a in r["avisos"]])
        P.configurar(m, {"sismo": {"zona": 2, "classe_terreno": "C", "R": 3.0, "sistema": "aco_portico"}})
        r = P.gerar_acoes(m)
        self.assertEqual(r["casos"], ["SX", "SY", "SX_TOR", "SY_TOR"])  # + torção acidental (planta 15 × 10 m)
        self.assertEqual(m.casos["SX"].tipo, "S")
        self.assertIn("sismo_edicao_2006", [a["codigo"] for a in r["avisos"]])
        H = r["parametros"]["SX"]["H_kN"]
        self.assertAlmostEqual(sum(f["F_kN"] for f in r["forcas"]["SX"]), H, places=9)
        # peso sísmico = ΣG + fração de Q (ψ2 da NBR 8681) dos nós dos pavimentos
        pesos = solver.pesos_nodais(m, {"G1": 1.0, "Q1": 0.4})
        W = sum(w for n, w in pesos.items() if m.nos[n].z > 1e-6)
        self.assertAlmostEqual(r["parametros"]["SX"]["W_kN"], W, places=6)

    def test_gerar_acoes_erros(self):
        m = portico()
        P.configurar(m, {"vento": {"V0": 35, "categoria": "IV", "classe": "B"}})
        with self.assertRaises(P.ErroEntrada) as c:
            P.gerar_acoes(m)
        self.assertIn("pavimentos", str(c.exception))
        self.assertNotIn("V+X", m.casos)

    def test_combinacoes(self):
        m = edificio_aco()
        P.gerar_acoes(m)
        cs = P.combinacoes(m)
        self.assertTrue(all(set(c) == {"nome", "estado", "fatores"} for c in cs))
        self.assertEqual(len({c["nome"] for c in cs}), len(cs))
        self.assertTrue({"ELU", "ELS_QP", "ELS_FREQ", "ELS_RARA"} <= {c["estado"] for c in cs})
        P.configurar(m, {"variabilidade": "pequenas"})
        g1 = next(c for c in P.combinacoes(m) if c["nome"] == "ELU G")["fatores"]["G1"]
        self.assertAlmostEqual(g1, 1.3)
        self.assertEqual(P.combinacoes(P.novo_modelo()), [])


class TestAnalises(unittest.TestCase):
    def setUp(self):
        self.m = portico()
        self.c = P.calcular(self.m)

    def test_portico_conferido_a_mao(self):
        w, L, h = 14.0, 6.0, 4.0
        k = h / L
        M_no = w * L**2 / 12 * 3 / (3 + 2 * k)
        H, V, M_vao = M_no / h, w * L / 2, w * L**2 / 8 - M_no
        m = portico()
        P.editar(m, "secao", "W310x38.7", {"A": 20.0})  # axialmente quase rígida: a fórmula fechada ignora a deformação axial
        c = P.calcular(m)
        reac = P.resultados(c, "reacoes", combinacao="ELU G")
        self.assertAlmostEqual(reac["n1"][2], V, delta=1e-6)
        self.assertAlmostEqual(reac["n4"][2], V, delta=1e-6)
        self.assertAlmostEqual(reac["n1"][0], H, delta=1e-4)  # empuxo para dentro no pé esquerdo
        self.assertAlmostEqual(reac["n4"][0], -H, delta=1e-4)
        my = P.resultados(c, "esforcos", ids=["b2"], combinacao="ELU G")["b2"]["My"]
        self.assertAlmostEqual(max(abs(v) for v in my), M_vao, delta=1e-4)
        self.assertAlmostEqual(abs(my[0]), M_no, delta=1e-4)
        env = c.reacoes_envoltoria["n1"]
        self.assertAlmostEqual(env["max"][2], V, delta=1e-6)
        self.assertEqual(env["comb_max"][2], "ELU G")

    def test_calcular_igual_aos_motores(self):
        linha = next(l for l in self.c.verificacao if l["barra"] == "b2")
        fat = next(c["fatores"] for c in self.c.combinacoes if c["nome"] == linha["combinacao"])
        res = solver.analisar(self.m, fat, n_pontos=13)
        lin = res.esforcos["b2"]
        esf = [nbr8800.esforcos_nbr(x) for x in lin]
        L = 6.0
        Cb = nbr8800.cb_de_diagrama([x[0] for x in lin], [e["Mx"] for e in esf])
        r = nbr8800.verificar_barra_aco(self.m.secoes["W310x38.7"], self.m.materiais["A-572 GR.50"], esf, L, L, L, Cb, L)
        self.assertAlmostEqual(linha["taxa"], r["taxa_max"], delta=1e-9)
        self.assertEqual((linha["criterio"], linha["item"], linha["ok"]), (r["criterio"], r["item"], r["ok"]))
        self.assertAlmostEqual(linha["x_m"], lin[r["indice"]][0], delta=1e-12)
        self.assertEqual(linha["norma"], "NBR 8800:2008")

    def test_json_puro(self):
        d = self.c.to_dict(completo=True)
        json.dumps(d, allow_nan=False)
        self.assertEqual(set(d["resultados"]), {c["nome"] for c in d["combinacoes"]})
        self.assertNotIn("resultados", self.c.to_dict())
        self.assertEqual(d["resumo"]["n_barras"], 3)
        self.assertEqual(d["opcoes"], {"pdelta": False, "n_pontos": 13})
        self.assertIn("NBR 8800", [n["norma"] for n in d["normas"]])
        self.assertTrue(all("limitacao" in n for n in d["normas"]))
        self.assertIn("motivo", d["estabilidade"])
        self.assertFalse(d["estabilidade"]["aplicavel"])

    def test_analisar_caso_e_combinacao(self):
        r = P.analisar_caso(self.m, "G1")
        json.dumps(r)
        self.assertEqual(set(r), {"nome", "pdelta", "deslocamentos", "reacoes", "esforcos", "flechas", "pavimentos"})
        self.assertAlmostEqual(sum(v[2] for v in r["reacoes"].values()), 60.0, delta=1e-6)
        f = r["flechas"]["b2"]
        self.assertEqual(len(f["rel_uz"]), len(f["x"]))
        self.assertAlmostEqual(f["rel_uz"][0], 0.0, delta=1e-15)
        self.assertAlmostEqual(f["rel_uz"][-1], 0.0, delta=1e-15)
        rc = P.analisar_combinacao(self.m, "ELU G")
        self.assertAlmostEqual(sum(v[2] for v in rc["reacoes"].values()), 84.0, delta=1e-6)
        with self.assertRaises(P.ErroNaoEncontrado):
            P.analisar_caso(self.m, "X")
        with self.assertRaises(P.ErroNaoEncontrado):
            P.analisar_combinacao(self.m, "X")
        mec = portico()
        mec.nos["n1"].apoio = mec.nos["n4"].apoio = (False,) * 6
        with self.assertRaises(P.ErroEntrada):
            P.analisar_caso(mec, "G1")

    def test_mecanismo(self):
        m = portico()
        m.nos["n1"].apoio = (True, True, True, False, False, False)
        m.nos["n4"].apoio = (False, True, True, False, False, False)  # nada impede ux em n4 e rx: mecanismo
        with self.assertRaises(P.ErroMecanismo):
            P.calcular(m)

    def test_pdelta_muda_elu_e_nao_els(self):
        # q=40: com q=60 N > Pcr fora do plano dos pilares (instabilidade real, detectada desde a ficha 026)
        m = portico(q=40.0)
        P.adicionar(m, "caso", {"nome": "V1", "tipo": "V"})
        P.adicionar(m, "carga_nodal", {"caso": "V1", "no": "n2", "F": [20, 0, 0, 0, 0, 0]})
        a, b = P.calcular(m, {"pdelta": False}), P.calcular(m, {"pdelta": True})
        self.assertTrue(b.opcoes["pdelta"])
        elu = [c["nome"] for c in a.combinacoes if c["estado"] == "ELU" and "V1" in c["fatores"]][0]
        ux_a, ux_b = a.resultados[elu].deslocamentos["n2"][0], b.resultados[elu].deslocamentos["n2"][0]
        self.assertGreater(abs(ux_b), abs(ux_a) * 1.005)
        for c in a.combinacoes:
            if c["estado"].startswith("ELS"):
                np.testing.assert_array_equal(a.resultados[c["nome"]].deslocamentos["n2"],
                                              b.resultados[c["nome"]].deslocamentos["n2"])
        self.assertTrue(P.analisar_combinacao(m, elu, pdelta=True)["pdelta"])
        self.assertFalse(P.analisar_combinacao(m, elu, pdelta=False)["pdelta"])

    def test_modal(self):
        m = edificio_aco(vento=False, nx=1, ny=1, nz=2)
        r = P.modal(m, 3)
        json.dumps(r)
        self.assertEqual(len(r["frequencias_hz"]), 3)
        self.assertTrue(all(f > 0 for f in r["frequencias_hz"]))
        self.assertAlmostEqual(r["periodos_s"][0], 1 / r["frequencias_hz"][0])
        self.assertIn("N0-0-0" if "N0-0-0" in r["modos"][0] else next(iter(r["modos"][0])), r["modos"][0])
        with self.assertRaises(P.ErroEntrada):
            P.modal(P.novo_modelo())
        sem_massa = portico()
        sem_massa.cargas_distribuidas.clear()
        with self.assertRaises(P.ErroEntrada):
            P.modal(sem_massa)


class TestConsultas(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.m = portico(q=40.0)
        cls.c = P.calcular(cls.m)

    def test_resultados_envoltoria(self):
        env = P.resultados(self.c, "esforcos", ids=["b2"])["b2"]
        self.assertEqual(set(env["My"]), {"max", "min", "comb_max", "comb_min"})
        self.assertEqual(len(env["My"]["max"]), len(env["x"]))
        self.assertTrue(all(a >= b for a, b in zip(env["My"]["max"], env["My"]["min"])))
        d = P.resultados(self.c, "deslocamentos", ids=["n2"])["n2"]  # ELS
        self.assertEqual(len(d["max"]), 6)
        self.assertIn(d["comb_max"][2], [c["nome"] for c in self.c.combinacoes if c["estado"].startswith("ELS")])
        fl = P.resultados(self.c, "flechas", ids=["b2"], combinacao="ELS QP")["b2"]
        self.assertIn("rel_uz", fl)
        json.dumps([env, d, fl])
        for args, exc in ((("esforcos", ["zz"]), P.ErroNaoEncontrado), (("coisa",), P.ErroEntrada)):
            with self.assertRaises(exc):
                P.resultados(self.c, *args)
        with self.assertRaises(P.ErroNaoEncontrado):
            P.resultados(self.c, "reacoes", combinacao="nao")

    def test_verificacao_filtros_e_ordem(self):
        v = P.verificacao(self.c)
        self.assertEqual([l["taxa"] for l in v], sorted([l["taxa"] for l in v], reverse=True))
        self.assertEqual(len(P.verificacao(self.c, {"tipo": "concreto"})), 0)
        self.assertEqual(len(P.verificacao(self.c, {"tipo": "aco"})), 3)
        self.assertTrue(all(not l["ok"] for l in P.verificacao(self.c, {"reprovadas": True})))
        self.assertEqual(P.verificacao(self.c, {"taxa_min": 1e9}), [])
        with self.assertRaises(P.ErroEntrada):
            P.verificacao(self.c, {"x": 1})

    def test_verificacao_barra(self):
        r = P.verificacao_barra(self.c, "b2")
        linha = next(l for l in P.verificacao(self.c) if l["barra"] == "b2")
        self.assertEqual({k: r[k] for k in linha}, linha)
        self.assertIn("taxas", r["detalhes"])
        self.assertAlmostEqual(r["detalhes"]["taxas"][r["criterio"]], r["taxa"], delta=1e-9)
        self.assertEqual({p["combinacao"] for p in r["por_combinacao"]},
                         {c["nome"] for c in self.c.combinacoes if c["estado"] in ("ELU", "ELU_EXC")})
        self.assertAlmostEqual(max(p["taxa"] for p in r["por_combinacao"]), r["taxa"], delta=1e-12)
        json.dumps(r)
        with self.assertRaises(P.ErroNaoEncontrado):
            P.verificacao_barra(self.c, "zz")

    def test_barra_incompativel_nao_aborta(self):
        m = portico()
        P.adicionar(m, "secao", {"forma": "retangular", "b": 0.2, "h": 0.4})
        P.adicionar(m, "material", {"concreto": 25})
        P.adicionar(m, "barra", {"id": "bc", "ni": "n1", "nj": "n3", "secao": "R 200x400", "material": "A-572 GR.50",
                                 "tipo": "concreto"})
        c = P.calcular(m)
        l = next(x for x in c.verificacao if x["barra"] == "bc")
        self.assertEqual((l["criterio"], l["ok"], l["taxa"]), ("nao_verificada", False, None))
        self.assertTrue(any(a["codigo"] == "barra_nao_verificada" for a in c.avisos))
        self.assertEqual(c.verificacao[0]["barra"], "bc")  # não verificadas primeiro
        self.assertEqual(c.resumo()["n_nao_verificadas"], 1)
        self.assertEqual(P.verificacao_barra(c, "bc")["por_combinacao"], [])
        json.dumps(c.to_dict())

    def test_barras_opcao_e_erros_de_opcoes(self):
        c = P.calcular(self.m, {"barras": ["b2"]})
        self.assertEqual([l["barra"] for l in c.verificacao], ["b2"])
        for op, exc in (({"barras": ["zz"]}, P.ErroNaoEncontrado), ({"foo": 1}, P.ErroEntrada),
                        ({"n_pontos": 1}, P.ErroEntrada)):
            with self.assertRaises(exc):
                P.calcular(self.m, op)
        with self.assertRaises(P.ErroEntrada):
            P.calcular(P.novo_modelo())

    def test_flecha_de_viga_de_aco(self):
        f = self.c.deslocamentos["verticais"]
        self.assertEqual([x["barra"] for x in f], ["b2"])
        self.assertEqual(f[0]["elemento"], "viga_piso")
        self.assertEqual(f[0]["combinacao"], "ELS QP")  # só permanente: não há combinação frequente
        self.assertAlmostEqual(f[0]["limite_m"], 6.0 / 350, delta=1e-12)
        self.assertEqual(f[0]["razao"], "L/350")
        self.assertAlmostEqual(f[0]["taxa"], f[0]["flecha_m"] / f[0]["limite_m"], delta=1e-12)

    def test_estabilidade_sem_pavimentos(self):
        e = P.estabilidade(self.c)
        self.assertFalse(e["aplicavel"])
        self.assertIn("motivo", e)

    def test_catalogo_materiais_normas(self):
        todos = P.catalogo_perfis()
        self.assertGreater(len(todos), 50)
        w = P.catalogo_perfis("w310", "W")
        self.assertTrue(w and all(p["nome"].startswith("W310") for p in w))
        self.assertTrue(any(p["familia"] == "Ue" for p in P.catalogo_perfis(familia="Ue")))
        p = P.perfil("W310x38.7")
        self.assertAlmostEqual(p["A_cm2"], 49.72, delta=0.3)
        self.assertAlmostEqual(p["Ix_cm4"], 8581, delta=100)
        self.assertEqual(p["secao"]["nome"], "W310x38.7")
        self.assertAlmostEqual(p["secao"]["A"], p["A_cm2"] / 1e4)
        json.dumps(p)
        with self.assertRaises(P.ErroNaoEncontrado):
            P.perfil("W999x1")
        with self.assertRaises(P.ErroEntrada):
            P.catalogo_perfis(familia="Z")
        mat = P.materiais_disponiveis()
        self.assertEqual(mat["concreto"], {"fck_MPa_min": 20, "fck_MPa_max": 90})
        self.assertIn({"nome": "CA-50", "fyk_MPa": 500}, mat["armadura"])
        self.assertIn("A-572 GR.50", [a["nome"] for a in mat["aco"]])
        n = P.normas_implementadas()
        self.assertTrue({"NBR 8800", "NBR 6118", "NBR 8681", "NBR 6123"} <= {x["norma"] for x in n})
        self.assertTrue(all({"norma", "edicao", "item", "verificacao", "limitacao", "modulo"} <= set(x) for x in n))
        json.dumps(n)

    def test_ligacoes(self):
        try:
            from prossen_estrutural.aco import ligacoes  # noqa: F401
        except ImportError:
            with self.assertRaises(NotImplementedError):
                P.tipos_ligacao()
            self.skipTest("aco.ligacoes ainda não existe (ficha 011)")
        tipos = P.tipos_ligacao()
        self.assertTrue(tipos)
        campos = [(t["tipo"], c) for t in tipos for c in t["campos"]]
        self.assertTrue(all(c["rotulo"] and len(c["rotulo"]) <= 28 for _, c in campos))  # campo novo cai no próprio nome
        self.assertEqual({c["nome"]: c["rotulo"] for _, c in campos}["eh"], "Borda livre eh")
        self.assertTrue(all("opcoes" in c for _, c in campos if c["nome"] in ("tipo_parafuso", "eletrodo")))
        rot_b = {t: c["rotulo"] for t, c in campos if c["nome"] == "b"}
        self.assertEqual(rot_b, {"solda_filete": "Perna do filete", "ligacao_tracao": "Largura da chapa"})
        json.dumps(tipos)
        with self.assertRaises(P.ErroEntrada):
            P.verificar_ligacao("tipo_inexistente", {}, {})

    def test_relatorios(self):
        modelo = self.m
        try:
            from prossen_estrutural import relatorio  # noqa: F401
        except ImportError:
            with self.assertRaises(NotImplementedError):
                P.relatorio_html(modelo, self.c)
            self.skipTest("relatorio.py ainda não existe (ficha 010)")
        h = P.relatorio_html(modelo, self.c)
        self.assertIn("<html", h.lower())
        with tempfile.TemporaryDirectory() as t:
            for fmt in ("html", "md", "json"):
                r = P.gerar_relatorio(modelo, self.c, fmt, Path(t) / "saida" / f"r.{fmt}")
                self.assertGreater(r["bytes"], 50)
                self.assertTrue(Path(r["caminho"]).exists())
            d = json.loads((Path(t) / "saida" / "r.json").read_text(encoding="utf-8"))
            self.assertEqual(d["resumo"]["n_barras"], 3)
            self.assertNotIn("resultados", d)
            for args in (("pdf", Path(t) / "x"), ("html", None)):
                with self.assertRaises(P.ErroEntrada):
                    P.gerar_relatorio(modelo, self.c, *args)

    def test_relatorio_json_sem_relatorio_py(self):
        with tempfile.TemporaryDirectory() as t:
            r = P.gerar_relatorio(self.m, self.c, "json", Path(t) / "a.json")
            self.assertEqual(json.loads(Path(r["caminho"]).read_text(encoding="utf-8"))["modelo"], "Pórtico de teste")


class TestCaminhoEExemplos(unittest.TestCase):
    def test_caminho_seguro(self):
        with tempfile.TemporaryDirectory() as t:
            base = Path(t).resolve()
            self.assertEqual(P.caminho_seguro(base, "a/b.json"), base / "a" / "b.json")
            self.assertEqual(P.caminho_seguro(str(base), "b.json"), base / "b.json")
            self.assertEqual(P.caminho_seguro(base, str(base / "x.json")), base / "x.json")  # absoluto dentro
            for ruim in ("../fora.json", "a/../../fora", "..", "a/..", "C:\\Windows\\win.ini", "C:\\", "C:fora",
                         "\\\\servidor\\share\\x", "", "x\x00y", str(base.parent / "fora.json"), "/etc/passwd"):
                with self.subTest(ruim=ruim), self.assertRaises(P.ErroEntrada):
                    P.caminho_seguro(base, ruim)

    def test_exemplos(self):
        ex = P.exemplos()
        nomes = {e["nome"] for e in ex}
        self.assertTrue({"viga_biapoiada", "portico_aco", "galpao_aco", "edificio_concreto"} <= nomes)
        self.assertTrue(all(e["descricao"] for e in ex))
        m = P.abrir_exemplo("viga_biapoiada")
        self.assertEqual(len(m.barras), 1)
        self.assertEqual(ex[0]["descricao"], next(e for e in ex if e["nome"] == ex[0]["nome"])["descricao"])
        with self.assertRaises(P.ErroNaoEncontrado) as c:
            P.abrir_exemplo("../ESPEC")
        self.assertIn("viga_biapoiada", str(c.exception))

    def test_viga_biapoiada_conferencia_manual(self):
        """Valores do exemplos/LEIAME.md: Q1 q = 10 kN/m, L = 6 m → M = 45,00 kN·m; reações 30,00 kN."""
        m = P.abrir_exemplo("viga_biapoiada")
        r = P.analisar_caso(m, "Q1")
        self.assertAlmostEqual(max(abs(v) for v in r["esforcos"]["b1"]["My"]), 45.0, delta=1e-6)
        self.assertAlmostEqual(r["reacoes"]["n1"][2], 30.0, delta=1e-6)


class TestIntegracaoEdificio(unittest.TestCase):
    def test_desempenho_edificio_3x2x4(self):
        m = edificio_aco()
        self.assertGreaterEqual(len(m.barras), 100)
        t = time.perf_counter()
        c = P.calcular(m)
        dt = time.perf_counter() - t
        self.assertLess(dt, 20.0, f"calcular levou {dt:.1f} s")
        self.assertGreaterEqual(len(c.combinacoes), 30)
        self.assertEqual(len(c.verificacao), len(m.barras))
        json.dumps(c.to_dict(completo=True), allow_nan=False)

    def test_b2_do_edificio_de_aco(self):
        m = edificio_aco()
        c = P.calcular(m)
        est = c.estabilidade
        self.assertTrue(est["aplicavel"])
        self.assertEqual(est["gama_z"], {})  # só aço: só B2
        b2 = est["B2"]["X"]
        self.assertIn(b2["classificacao"], ("pequena", "media", "grande"))
        # recalcula o pavimento reportado direto com solver + coef_B2
        comb = next(x for x in c.combinacoes if x["nome"] == b2["combinacao"])
        res = solver.combinar({k: solver.analisar(m, k, n_pontos=13) for k in comb["fatores"]}, comb["fatores"])
        pavs = sorted(m.pavimentos.values(), key=lambda p: p.z)
        i = [p.nome for p in pavs].index(b2["pavimento"])
        pes = solver.pesos_nodais(m, comb["fatores"])
        P_ = [sum(w for n, w in pes.items() if abs(m.nos[n].z - p.z) < 1e-3) for p in pavs]
        H = [sum(cn.F[0] * comb["fatores"][cn.caso] for cn in m.cargas_nodais
                 if cn.caso in comb["fatores"] and abs(m.nos[cn.no].z - p.z) < 1e-3) for p in pavs]
        dx = [res.pavimentos[p.nome][0] for p in pavs]
        dh = abs(dx[i] - (dx[i - 1] if i else 0.0))
        h = pavs[i].z - (pavs[i - 1].z if i else 0.0)
        esperado = nbr8800.coef_B2(sum(P_[i:]), dh, abs(sum(H[i:])), h, 0.85)
        self.assertAlmostEqual(b2["max"], esperado, delta=1e-9)
        # deslocamento horizontal: topo e entre pisos, ELS frequente com vento
        hz = c.deslocamentos["horizontais"]
        self.assertTrue({"topo", "entre_pisos"} <= {x["tipo"] for x in hz})
        vert = c.deslocamentos["verticais"]
        self.assertTrue(vert and all(x["combinacao"].startswith("ELS FREQ") for x in vert))
        topo = next(x for x in hz if x["tipo"] == "topo" and x["direcao"] == "X")
        self.assertAlmostEqual(topo["limite_m"], 12.0 / 400, delta=1e-12)
        self.assertTrue(topo["combinacao"].startswith("ELS FREQ"))

    def test_gama_z_igual_ao_motor(self):
        m = P.novo_modelo("Edifício de concreto")
        r = P.gerar_estrutura(m, {"tipo": "edificio", "material": {"concreto": 30},
                                  "secoes": {"pilar": {"forma": "retangular", "b": 0.4, "h": 0.4},
                                             "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
                                  "vaos_x": [5.0, 5.0], "vaos_y": [5.0], "alturas": [3.0] * 4})
        viga = next(b for b in r["ids"]["barras"] if b.startswith("VX"))
        P.adicionar_lote(m, [{"tipo": "carga_distribuida", "dados": {"caso": "Q1", "barra": b, "q": [0, 0, -5.0]}}
                             for b in r["ids"]["barras"] if b.startswith("V")])
        P.configurar(m, {"vento": {"V0": 40, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 10, "ly": 5}}})
        c = P.calcular(m, {"barras": [viga]})
        self.assertEqual(c.estabilidade["B2"], {})  # só concreto: só γz
        for d, k in (("X", 0), ("Y", 1)):
            reg = c.estabilidade["gama_z"][d]
            comb = next(x for x in c.combinacoes if x["nome"] == reg["combinacao"])
            fat = comb["fatores"]
            # δ de 1ª ordem com (EI)sec da NBR 6118 15.7.3: 0,4 (vigas) / 0,8 (pilares) · Eci; o material guarda
            # Ecs = αi·Eci, αi = 0,8 + 0,2·30/80 = 0,875 (8.2.8) → fator_EI = 0,4/0,875 e 0,8/0,875
            mr = copy.deepcopy(m)
            for bb in mr.barras.values():
                bb.params["fator_EI"] = (0.8 if bb.id.startswith("P") else 0.4) / 0.875
            res = solver.combinar({q: solver.analisar(mr, q, n_pontos=13) for q in fat}, fat)
            pavs = sorted(m.pavimentos.values(), key=lambda p: p.z)
            pes = solver.pesos_nodais(m, fat)
            dados = []
            for p in pavs:
                nos = {n for n in m.nos if abs(m.nos[n].z - p.z) < 1e-3}
                dados.append({"z_m": p.z, "P_kN": sum(pes[n] for n in nos),
                              "H_kN": sum(cn.F[k] * fat[cn.caso] for cn in m.cargas_nodais
                                          if cn.caso in fat and cn.no in nos),
                              "delta_m": float(res.pavimentos[p.nome][k])})
            self.assertAlmostEqual(reg["valor"], nbr6118.gama_z(dados)["gama_z"], delta=1e-9)
        self.assertIn("15.5.3", c.estabilidade["gama_z"]["X"]["item"])
        self.assertIn("15.7.3", c.estabilidade["gama_z"]["X"]["item"])
        self.assertEqual(len(c.verificacao), 1)
        l = c.verificacao[0]
        self.assertEqual(l["tipo"], "concreto")
        self.assertIn("Asl_torcao_cm2", l["armadura"])
        self.assertEqual(l["norma"], "NBR 6118:2023")
        json.dumps(c.to_dict(), allow_nan=False)


class TestConcretoFlecha(unittest.TestCase):
    def test_viga_de_concreto_tem_armadura_e_flecha(self):
        m = P.novo_modelo("Viga de concreto")
        P.adicionar_lote(m, [
            {"tipo": "material", "dados": {"concreto": 30}},
            {"tipo": "secao", "dados": {"forma": "retangular", "b": 0.2, "h": 0.5, "nome": "V20x50"}},
            {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True, True, True, True, False, False]}},
            {"tipo": "no", "dados": {"id": "n2", "x": 5, "y": 0, "z": 0, "apoio": [False, True, True, False, False, False]}},
            {"tipo": "barra", "dados": {"id": "v1", "ni": "n1", "nj": "n2", "secao": "V20x50", "material": "C30"}},
            {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}},
            {"tipo": "caso", "dados": {"nome": "Q1", "tipo": "Q"}},
            {"tipo": "carga_distribuida", "dados": {"caso": "G1", "barra": "v1", "q": [0, 0, -12]}},
            {"tipo": "carga_distribuida", "dados": {"caso": "Q1", "barra": "v1", "q": [0, 0, -6]}}])
        c = P.calcular(m)
        l = c.verificacao[0]
        self.assertEqual(l["tipo"], "concreto")
        self.assertGreater(l["armadura"]["As_cm2"], 0)
        # Md = 1,4·(12+6)·5²/8 = 78,75 kN·m na combinação ELU: o motor vê esse momento
        r = P.verificacao_barra(c, "v1")
        self.assertTrue(r["por_combinacao"])
        self.assertLessEqual(max(p["taxa"] for p in r["por_combinacao"]), r["taxa"] + 1e-9)
        fl = c.deslocamentos["verticais"]
        self.assertEqual(len(fl), 1)
        self.assertEqual(fl[0]["combinacao"], "ELS QP")
        self.assertTrue(fl[0]["item"].startswith("17.3.2.1"))


def balanco(L=4.0, q=10.0, secao="W310x38.7", params=None):
    """Balanço engastado em n1 com carga uniforme (n2 livre)."""
    m = P.novo_modelo("Balanço")
    P.adicionar_lote(m, [
        {"tipo": "material", "dados": {"aco": "A-572 GR.50"}},
        {"tipo": "secao", "dados": {"perfil": secao}},
        {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True] * 6}},
        {"tipo": "no", "dados": {"id": "n2", "x": L, "y": 0, "z": 0}},
        {"tipo": "barra", "dados": {"id": "b1", "ni": "n1", "nj": "n2", "secao": secao,
                                    "material": "A-572 GR.50", **({"params": params} if params else {})}},
        {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}},
        {"tipo": "carga_distribuida", "dados": {"caso": "G1", "barra": "b1", "q": [0, 0, -q]}}])
    return m


class TestIntegracao022(unittest.TestCase):
    def test_tipo_edificacao_na_config_e_nas_combinacoes(self):
        m = portico()
        self.assertIsNone(P.configurar(m, {})["tipo_edificacao"])
        elu = lambda: next(c for c in P.combinacoes(m) if c["estado"] == "ELU")["fatores"]["G1"]  # noqa: E731
        self.assertAlmostEqual(elu(), 1.4, delta=1e-12)  # padrão: γg = 1,4 (grandes variabilidades)
        # NBR 8681:2003 tabela 2: ação permanente de grande variabilidade, normal: 1,35 (tipo 1) / 1,40 (tipo 2)
        self.assertEqual(P.configurar(m, {"tipo_edificacao": "tipo1"})["tipo_edificacao"], "tipo1")
        self.assertAlmostEqual(elu(), 1.35, delta=1e-12)
        P.configurar(m, {"tipo_edificacao": "tipo2"})
        self.assertAlmostEqual(elu(), 1.40, delta=1e-12)
        P.configurar(m, {"tipo_edificacao": None})
        self.assertAlmostEqual(elu(), 1.4, delta=1e-12)

    def test_tipo_edificacao_invalido(self):
        m = portico()
        with self.assertRaises(P.ErroEntrada) as ctx:
            P.configurar(m, {"tipo_edificacao": "tipo3"})
        self.assertEqual(ctx.exception.erros[0]["campo"], "tipo_edificacao")
        self.assertIsNone(P.resumo(m)["config"]["tipo_edificacao"])  # atômico: nada mudou
        m.config["tipo_edificacao"] = "x"  # arquivo editado à mão
        self.assertIn("config_invalida", [v["codigo"] for v in P.validar(m) if v["nivel"] == "erro"])
        with self.assertRaises(P.ErroEntrada):
            P.calcular(m)

    def test_balanco_usa_cb_1(self):
        """NBR 8800 5.4.2.3: Cb = 1,0 em balanço. O diagrama dá Cb = 12,5/(2,5+3·0,5625+4·0,25+3·0,0625)
        = 2,3256 (momento parabólico, M(x) = w(L−x)²/2), que NÃO pode ser usado."""
        L = 4.0
        for params in (None, {"Cb": 2.0}):  # params.Cb também é ignorado em balanço
            m = balanco(L, params=params)
            self.assertTrue(P._balanco(m, m.barras["b1"]))
            c = P.calcular(m)
            l = c.verificacao[0]
            esf = [nbr8800.esforcos_nbr(x) for x in c.resultados[l["combinacao"]].esforcos["b1"]]
            sec, mat = m.secoes["W310x38.7"], m.materiais["A-572 GR.50"]
            cb1 = nbr8800.verificar_barra_aco(sec, mat, esf, L, L, L, 1.0, L, None, 1.0)
            cb233 = nbr8800.verificar_barra_aco(sec, mat, esf, L, L, L, 12.5 / 5.375, L, None, 1.0)
            self.assertAlmostEqual(l["taxa"], cb1["taxa_max"], delta=1e-9)
            self.assertGreater(l["taxa"], cb233["taxa_max"] + 1e-6)

    def test_nao_e_balanco(self):
        m = portico()
        self.assertFalse(any(P._balanco(m, b) for b in m.barras.values()))
        m2 = balanco()
        P.adicionar(m2, "no", {"id": "n3", "x": 4, "y": 0, "z": 3, "apoio": [True] * 6})
        P.adicionar(m2, "barra", {"id": "b2", "ni": "n2", "nj": "n3", "secao": "W310x38.7", "material": "A-572 GR.50"})
        self.assertFalse(P._balanco(m2, m2.barras["b1"]))  # n2 agora tem outra barra

    def test_cantoneira_eixos_geometricos(self):
        m = balanco(2.0)
        self.assertNotIn("cantoneira_eixos_geometricos", [a["codigo"] for a in P.calcular(m).avisos])
        m.secoes["W310x38.7"].props["alfa_principal"] = 0.3  # o engenheiro-aco grava alfa_principal em cantoneiras
        av = [a for a in P.calcular(m).avisos if a["codigo"] == "cantoneira_eixos_geometricos"]
        self.assertEqual(len(av), 1)
        self.assertEqual(av[0]["barras"], ["b1"])

    def test_resumo_taxa_max_ignora_conformidade_sem_esforco(self):
        """ESPEC §7.2: v2 reprovada só por cobrimento (conformidade, sem esforço) não vira a taxa máxima nem
        a barra crítica do resumo/relatório; a crítica é v1, a mais solicitada (maior razão de esforço)."""
        from prossen_estrutural import relatorio
        m = P.novo_modelo("Vigas")
        apoios = ([True, True, True, True, False, False], [False, True, True, False, False, False])
        P.adicionar_lote(m, [
            {"tipo": "material", "dados": {"concreto": 30}},
            {"tipo": "secao", "dados": {"forma": "retangular", "b": 0.2, "h": 0.5, "nome": "V20x50"}},
            {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}}] + [
            e for k, (q, par) in enumerate(((20, {}), (12, {"cobrimento_m": 0.01})), 1) for e in (
                {"tipo": "no", "dados": {"id": f"a{k}", "x": 0, "y": k, "z": 0, "apoio": apoios[0]}},
                {"tipo": "no", "dados": {"id": f"c{k}", "x": 5, "y": k, "z": 0, "apoio": apoios[1]}},
                {"tipo": "barra", "dados": {"id": f"v{k}", "ni": f"a{k}", "nj": f"c{k}", "secao": "V20x50",
                                            "material": "C30", "params": par}},
                {"tipo": "carga_distribuida", "dados": {"caso": "G1", "barra": f"v{k}", "q": [0, 0, -q]}})])
        c = P.calcular(m)
        l = {x["barra"]: x for x in c.verificacao}
        self.assertTrue(l["v1"]["ok"])
        self.assertFalse(l["v2"]["ok"])  # cobrimento 10 mm < mínimo da CAA II (NBR 6118 Tabela 7.2)
        self.assertNotEqual(l["v2"]["criterio"], "cobrimento")
        self.assertGreater(l["v1"]["taxa"], l["v2"]["taxa"])
        r = c.resumo()
        self.assertEqual((r["barra_critica"], r["n_reprovadas"]), ("v1", 1))
        self.assertEqual(r["taxa_max"], l["v1"]["taxa"])
        md = relatorio.markdown(m.to_dict(), c.to_dict())
        linha_taxa = next(x for x in md.splitlines() if "Taxa máxima" in x)
        self.assertIn(f"{r['taxa_max']:.2f}".replace(".", ","), linha_taxa)
        self.assertIn("v1", linha_taxa)


class TestCorrecoes026(unittest.TestCase):
    """Correções da ficha 026 (VC-integracao-004, -006, -007)."""

    def test_base_abaixo_de_zero_nao_descarta_pavimentos(self):
        """Base em z = −3: pavimentos em z = 0, 3, 6, 9 ficam 3, 6, 9, 12 m acima da base; vento e γz iguais
        aos do mesmo prédio com a base em z = 0."""
        def modelo(z0):
            m = P.novo_modelo("base")
            r = P.gerar_estrutura(m, {"tipo": "edificio", "material": {"aco": "A-572 GR.50"},
                                      "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                                      "vaos_x": [5.0] * 2, "vaos_y": [5.0], "alturas": [3.0] * 4, "origem": [0, 0, z0]})
            P.adicionar_lote(m, [{"tipo": "carga_distribuida", "dados": {"caso": "Q1", "barra": b, "q": [0, 0, -8.0]}}
                                 for b in r["ids"]["barras"] if b.startswith("V")])
            P.configurar(m, {"vento": {"V0": 35, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 10, "ly": 5}}})
            return m
        m0, m1 = modelo(0.0), modelo(-3.0)
        a0, a1 = P.gerar_acoes(m0), P.gerar_acoes(m1)
        self.assertEqual(len(a1["forcas"]["V+X"]), 4)
        self.assertEqual([f["F_vet_kN"] for f in a0["forcas"]["V+X"]], [f["F_vet_kN"] for f in a1["forcas"]["V+X"]])
        self.assertEqual([f["z"] - (-3.0) for f in a1["forcas"]["V+X"]], [f["z"] for f in a0["forcas"]["V+X"]])
        g0, g1 = P.calcular(m0).estabilidade["B2"]["X"], P.calcular(m1).estabilidade["B2"]["X"]
        self.assertAlmostEqual(g0["max"], g1["max"], delta=1e-9)

    def test_vento_distribuido_em_pilar_entra_em_H(self):
        """q = 2 kN/m em X em todos os pilares: cada pilar de 3 m dá q·L/2 = 3 kN a cada ponta; 6 pilares
        → 36 kN nos pavimentos intermediários (pilar abaixo + pilar acima) e 18 kN no topo; a base vai ao solo."""
        m = P.novo_modelo("vento distribuído")
        r = P.gerar_estrutura(m, {"tipo": "edificio", "material": {"aco": "A-572 GR.50"},
                                  "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                                  "vaos_x": [5.0] * 2, "vaos_y": [5.0], "alturas": [3.0] * 4})
        P.adicionar(m, "caso", {"nome": "V1", "tipo": "V"})
        pilares = [b for b in r["ids"]["barras"] if b.startswith("P")]
        for b in pilares:
            P.adicionar(m, "carga_distribuida", {"caso": "V1", "barra": b, "q": [2.0, 0, 0]})
        H, fora = P._forcas_horizontais(m, P._pavimentos_acima(m))
        np.testing.assert_allclose(H["V1"][:, 0], [36.0, 36.0, 36.0, 18.0], atol=1e-9)
        np.testing.assert_allclose(H["V1"][:, 1], 0.0, atol=1e-12)
        self.assertEqual(fora, {})
        c = P.calcular(m)
        self.assertTrue(c.estabilidade["aplicavel"])
        self.assertNotIn("motivo", c.estabilidade)
        self.assertEqual({x["direcao"] for x in c.deslocamentos["horizontais"]}, {"X"})

    def test_carga_horizontal_fora_da_cota_avisa(self):
        m = edificio_aco(vento=False, nx=1, ny=1, nz=2)
        P.adicionar(m, "caso", {"nome": "V1", "tipo": "V"})
        P.adicionar(m, "no", {"id": "meio", "x": 0, "y": 0, "z": 1.5})
        P.adicionar(m, "barra", {"id": "tirante", "ni": "N0-0-0", "nj": "meio", "secao": "W310x38.7",
                                 "material": "A-572 GR.50", "params": {"papel": "pilar"}})
        P.adicionar(m, "carga_nodal", {"caso": "V1", "no": "meio", "F": [5.0, 0, 0, 0, 0, 0]})
        P.adicionar(m, "carga_nodal", {"caso": "V1", "no": "N1-0-0", "F": [5.0, 0, 0, 0, 0, 0]})
        H, fora = P._forcas_horizontais(m, P._pavimentos_acima(m))
        self.assertEqual(fora, {"V1": ["meio"]})
        self.assertAlmostEqual(H["V1"][:, 0].sum(), 5.0, delta=1e-12)
        est = P._estabilidade(m, P._pavimentos_acima(m), [], {}, {}, False, {"aco"}, 13, H, fora)
        self.assertTrue(any("fora da cota" in a and "meio" in a for a in est["avisos"]))

    def test_portico_plano_so_confere_direcao_x(self):
        c = P.calcular(P.abrir_exemplo("portico_aco"))
        self.assertEqual({x["direcao"] for x in c.deslocamentos["horizontais"]}, {"X"})

    def test_reprovada_por_esbeltez_mostra_o_criterio(self):
        """Água do galpão de 10,2 m em W360x44.6 (sem travamento): KL/r = 271 > 200 (NBR 8800 5.3.4). O motor
        dá taxa de esforço 0,81; a linha mostra a esbeltez (taxa 1,35) e guarda a taxa de esforço."""
        m = P.abrir_exemplo("galpao_aco")
        P.adicionar(m, "secao", {"perfil": "W360x44.6"})
        P.editar(m, "barra", "VX2-1-2", {"secao": "W360x44.6"})
        c = P.calcular(m, {"barras": ["VX2-1-2"]})
        l = c.verificacao[0]
        self.assertFalse(l["ok"])
        self.assertEqual((l["criterio"], l["item"]), ("esbeltez", "5.3.4"))
        self.assertAlmostEqual(l["taxa"], 270.8 / 200, delta=0.01)
        self.assertLess(l["taxa_esforco"], 1.0)
        self.assertTrue(l["combinacao"].startswith("ELU"))

    def test_balanco_flecha_na_ponta_e_limite_2L(self):
        """Balanço de 4 m, q = 10 kN/m (G1 + peso próprio não ligado): flecha da ponta = qL⁴/(8EI) (resistência
        dos materiais); limite NBR 8800 C.1 nota a: 2L/350 (balanço de piso)."""
        L, q = 4.0, 10.0
        m = balanco(L, q)
        P.adicionar(m, "caso", {"nome": "Q1", "tipo": "Q"})
        P.adicionar(m, "carga_distribuida", {"caso": "Q1", "barra": "b1", "q": [0, 0, -q]})
        c = P.calcular(m)
        v = next(x for x in c.deslocamentos["verticais"] if x["barra"] == "b1")
        EI = m.materiais["A-572 GR.50"].E * m.secoes["W310x38.7"].Iy
        comb = next(x for x in c.combinacoes if x["nome"] == v["combinacao"])["fatores"]
        w = q * sum(comb.values())
        self.assertAlmostEqual(v["flecha_m"], w * L ** 4 / (8 * EI), delta=1e-6)
        self.assertAlmostEqual(v["limite_m"], 2 * L / 350, delta=1e-12)

    def test_params_ligacao_uma_aba_e_fator_EI_sao_conhecidos(self):
        m = balanco(2.0, params={"ligacao_uma_aba": True, "fator_EI": 0.8})
        self.assertNotIn("param_desconhecido", [v["codigo"] for v in P.validar(m)])


def coluna(material, b, h=4.0, G=1000.0, H=50.0, caso_h="V1"):
    """Coluna engastada na base com carga vertical G e horizontal H no topo (pavimento P1 em z = h). Uma só
    barra, 1 pavimento: δ = H·h³/(3·EI) e γz/B2 têm fórmula fechada."""
    m = P.novo_modelo("Coluna")
    P.adicionar_lote(m, [
        {"tipo": "material", "dados": material},
        {"tipo": "secao", "dados": {"forma": "retangular", "b": b, "h": b}},
        {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "engaste"}},
        {"tipo": "no", "dados": {"id": "n2", "x": 0, "y": 0, "z": h}},
        {"tipo": "pavimento", "dados": {"nome": "P1", "z": h}},
        {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}},
        {"tipo": "caso", "dados": {"nome": caso_h, "tipo": "V", "subtipo": "vento"}},
        {"tipo": "carga_nodal", "dados": {"caso": "G1", "no": "n2", "F": [0, 0, -G, 0, 0, 0]}},
        {"tipo": "carga_nodal", "dados": {"caso": caso_h, "no": "n2", "F": [H, 0, 0, 0, 0, 0]}}])
    nome_mat = next(iter(m.materiais))
    nome_sec = next(iter(m.secoes))
    P.adicionar(m, "barra", {"id": "b1", "ni": "n1", "nj": "n2", "secao": nome_sec, "material": nome_mat})
    return m


def momento_base(calculo, comb):
    """|M| e |V| na base da coluna (x = 0) na combinação `comb` (resultado local, kN·m e kN)."""
    e = calculo.resultados[comb].esforcos["b1"][0]  # (x, N, Vy, Vz, T, My, Mz)
    return max(abs(e[5]), abs(e[6])), max(abs(e[2]), abs(e[3]))


class TestMajoracaoHorizontal(unittest.TestCase):
    """VC-guia-001 / VC-logica-acoes-001: sem P-Δ, γz entre 1,1 e 1,3 majora as ações horizontais por 0,95·γz
    (NBR 6118:2023 15.7.2); B2 entre 1,1 e 1,4 majora por B2 (NBR 8800:2008 4.9.4, MAES); acima disso, aviso.

    Coluna em balanço, um pavimento: δ = H·h³/(3·EI), γz = 1/(1 − P·δ/(H·h)) = 1/(1 − P·h²/(3·EI)).
    Concreto C30: Eci = 5600·√30 = 30 672,46 MPa, αi = 0,8 + 0,2·30/80 = 0,875 (Ecs = 26 838,4 MPa),
    (EI)sec = 0,8·Eci·Ic (15.7.3, pilar), Ic = 0,4⁴/12 = 2,1333e-3 m⁴ → EI = 52 347,1 kN·m²; h = 4 m.
    """
    EI = 0.8 * 5600 * math.sqrt(30) * 1e3 * 0.4 ** 4 / 12

    def _nome(self, c, G, V):
        """Combinação com G e V1 (fatores G, V)."""
        return next(x["nome"] for x in c.combinacoes if x["estado"] == "ELU" and set(x["fatores"]) == {"G1", "V1"}
                    and abs(x["fatores"]["G1"] - G) < 1e-9 and abs(x["fatores"]["V1"] - V) < 1e-9)

    def test_concreto_majora_por_095_gama_z(self):
        m = coluna({"concreto": 30}, 0.4)
        self.assertAlmostEqual(m.materiais["C30"].E / 1e3, 0.875 * 5600 * math.sqrt(30), delta=1e-6 * 26838)
        c = P.calcular(m)
        # P = 1,4·1000 = 1400 kN: γz = 1/(1 − 1400·16/(3·EI)) = 1,16636; G favorável (P = 1000): 1,11344
        gz_d = 1 / (1 - 1400 * 16 / (3 * self.EI))
        gz_f = 1 / (1 - 1000 * 16 / (3 * self.EI))
        self.assertAlmostEqual(gz_d, 1.16636, delta=1e-5)
        por = c.estabilidade["por_combinacao"]
        for G, gz in ((1.4, gz_d), (1.0, gz_f)):
            n = self._nome(c, G, 1.4)
            self.assertAlmostEqual(por[n]["gama_z"], gz, delta=1e-9)
            fator = 0.95 * gz
            self.assertAlmostEqual(por[n]["fator"], fator, delta=1e-9)
            self.assertAlmostEqual(next(x for x in c.combinacoes if x["nome"] == n)["majoracao_horizontal"], fator, delta=1e-9)
            M, V = momento_base(c, n)
            self.assertAlmostEqual(V, fator * 1.4 * 50, delta=1e-6)       # cortante = fator · γ·H
            self.assertAlmostEqual(M, fator * 1.4 * 50 * 4, delta=1e-5)  # momento = fator · γ·H·h (G centrado: sem momento)
        # a combinação só de G não tem ação horizontal: nada muda
        self.assertNotIn("majoracao_horizontal", next(x for x in c.combinacoes if x["nome"] == "ELU G"))
        self.assertTrue(any(a["codigo"] == "gama_z_majorado" and "0,95·γz" in a["mensagem"] for a in c.avisos))
        self.assertAlmostEqual(c.estabilidade["gama_z"]["X"]["valor"], gz_d, delta=1e-9)  # γz reportado = 1ª ordem

    def test_concreto_gama_z_acima_de_1_3_avisa_e_nao_majora(self):
        c = P.calcular(coluna({"concreto": 30}, 0.4, G=2000.0))
        gz = 1 / (1 - 2800 * 16 / (3 * self.EI))  # = 1,3995 > 1,3
        self.assertGreater(gz, 1.3)
        n = self._nome(c, 1.4, 1.4)
        self.assertAlmostEqual(c.estabilidade["por_combinacao"][n]["gama_z"], gz, delta=1e-9)
        self.assertNotIn("majoracao_horizontal", next(x for x in c.combinacoes if x["nome"] == n))
        M, _ = momento_base(c, n)
        self.assertAlmostEqual(M, 1.4 * 50 * 4, delta=1e-5)  # esforço de 1ª ordem, como avisado
        av = [a for a in c.avisos if a["codigo"] == "gama_z_2a_ordem"]
        self.assertEqual(len(av), 1)
        self.assertIn("2ª ordem rigorosa", av[0]["mensagem"])
        self.assertIn("P-Δ", av[0]["mensagem"])

    def test_com_pdelta_nao_majora(self):
        c = P.calcular(coluna({"concreto": 30}, 0.4), {"pdelta": True})
        self.assertFalse([x for x in c.combinacoes if "majoracao_horizontal" in x])
        self.assertFalse([a for a in c.avisos if a["codigo"].startswith("gama_z")])

    def test_aco_majora_por_B2(self):
        """Aço A-572, seção maciça 0,3 × 0,3 (I = 6,75e-4 m⁴, E = 200 GPa → EI = 135 000 kN·m²):
        B2 = 1/(1 − N·h²/(3·Rs·EI)), Rs = 0,85 (anexo D). N = 1,4·2500 = 3500 kN → 1,19428; G favorável
        (N = 2500) → 1,13146; ambos entre 1,1 e 1,4 (média deslocabilidade): ações horizontais × B2."""
        m = coluna({"aco": "A-572 GR.50"}, 0.3, G=2500.0)
        EI = 2.0e8 * 0.3 ** 4 / 12
        self.assertAlmostEqual(m.materiais["A-572 GR.50"].E, 2.0e8, delta=1.0)
        c = P.calcular(m)
        for G, N in ((1.4, 3500.0), (1.0, 2500.0)):
            b2 = 1 / (1 - N * 16 / (3 * 0.85 * EI))
            n = self._nome(c, G, 1.4)
            self.assertAlmostEqual(c.estabilidade["por_combinacao"][n]["B2"], b2, delta=1e-9)
            M, V = momento_base(c, n)
            self.assertAlmostEqual(M, b2 * 1.4 * 50 * 4, delta=1e-5)
            self.assertAlmostEqual(V, b2 * 1.4 * 50, delta=1e-6)
        self.assertAlmostEqual(1 / (1 - 3500 * 16 / (3 * 0.85 * EI)), 1.19428, delta=1e-5)
        av = [a for a in c.avisos if a["codigo"] == "b2_majorado"]
        self.assertEqual(len(av), 1)
        self.assertIn("B2", av[0]["mensagem"])
        self.assertIn("NBR 8800", av[0]["mensagem"])

    def test_aco_B2_acima_de_1_4_avisa(self):
        """N = 1,4·6000 = 8400 kN: B2 = 1/(1 − 8400·16/(3·0,85·135 000)) = 1,6404 > 1,4 (grande deslocabilidade)."""
        c = P.calcular(coluna({"aco": "A-572 GR.50"}, 0.3, G=6000.0))
        n = self._nome(c, 1.4, 1.4)
        self.assertAlmostEqual(c.estabilidade["por_combinacao"][n]["B2"], 1 / (1 - 8400 * 16 / (3 * 0.85 * 135000.0)), delta=1e-9)
        M, _ = momento_base(c, n)
        self.assertAlmostEqual(M, 1.4 * 50 * 4, delta=1e-5)
        av = [a for a in c.avisos if a["codigo"] == "b2_2a_ordem"]
        self.assertEqual(len(av), 1)
        self.assertIn("análise de 2ª ordem rigorosa", av[0]["mensagem"])



def _copia(m):
    return P.de_dict(P.para_dict(m))


class TestEntradasDaRodadaFinal(unittest.TestCase):
    """Subtipo de Q, tipo_edificacao 'separadas', faixas de Cb/Ct e emenda (itens 2 e 4 da rodada final)."""

    def test_subtipo_de_Q_invalido_e_erro_de_entrada_com_a_lista(self):
        m = portico()
        for op in (lambda: P.adicionar(m, "caso", {"nome": "Q9", "tipo": "Q", "subtipo": "balada"}),
                   lambda: P.adicionar_lote(m, [{"tipo": "caso", "dados": {"nome": "Q9", "tipo": "Q", "subtipo": "balada"}}])):
            with self.assertRaises(P.ErroEntrada) as c:
                op()
            self.assertIn("balada", str(c.exception))
            self.assertIn("cobertura", str(c.exception))  # lista os usos válidos (combinacoes.USOS_Q)
        self.assertNotIn("Q9", m.casos)
        P.adicionar(m, "caso", {"nome": "Q1", "tipo": "Q", "subtipo": "cobertura"})
        with self.assertRaises(P.ErroEntrada):
            P.editar(m, "caso", "Q1", {"subtipo": "balada"})

    def test_subtipo_invalido_vindo_do_arquivo_chega_como_erro_de_entrada(self):
        """Modelo carregado de JSON (sem passar por `adicionar`): validar acusa; combinações, modal e calcular
        levantam ErroEntrada (nunca ValueError cru / 'erro interno')."""
        m = portico()
        P.adicionar(m, "caso", {"nome": "Q1", "tipo": "Q"})
        P.adicionar(m, "carga_nodal", {"caso": "Q1", "no": "n2", "F": [0, 0, -10, 0, 0, 0]})
        m.casos["Q1"].subtipo = "balada"
        self.assertIn(("erro", "caso_invalido"), [(v["nivel"], v["codigo"]) for v in P.validar(m)])
        for op in (lambda: P.combinacoes(m), lambda: P.modal(m), lambda: P.calcular(m)):
            with self.assertRaises(P.ErroEntrada):
                op()

    def test_tipo_edificacao_separadas_gama_da_nbr8800(self):
        """NBR 8800:2008 tab. 1 (ações separadas): permanente de estrutura metálica 1,25, de origem geral 1,50;
        variável de uso 1,5. Sem origem, o caso G usa 1,50 e o validar avisa."""
        m = portico()
        P.adicionar(m, "caso", {"nome": "Q1", "tipo": "Q"})
        P.configurar(m, {"tipo_edificacao": "separadas"})

        def elu():
            return {c["nome"]: c["fatores"] for c in P.combinacoes(m)}
        self.assertEqual(elu()["ELU G"], {"G1": 1.5})
        self.assertEqual(elu()["ELU Q1 princ."], {"G1": 1.5, "Q1": 1.5})
        self.assertIn("separadas_origem_g", [v["codigo"] for v in P.validar(m)])
        P.editar(m, "caso", "G1", {"subtipo": "metalica"})
        self.assertEqual(elu()["ELU Q1 princ."], {"G1": 1.25, "Q1": 1.5})
        self.assertNotIn("separadas_origem_g", [v["codigo"] for v in P.validar(m)])
        m.casos["G1"].subtipo = "madeira"
        self.assertIn(("erro", "caso_invalido"), [(v["nivel"], v["codigo"]) for v in P.validar(m)])
        with self.assertRaises(P.ErroEntrada):
            P.configurar(m, {"tipo_edificacao": "tipo3"})
        P.configurar(m, {"tipo_edificacao": "tipo2"})  # fora de 'separadas', o subtipo de G não é validado
        self.assertNotIn("caso_invalido", [v["codigo"] for v in P.validar(m)])

    def test_faixas_de_Cb_Ct_e_emenda(self):
        m = portico()

        def barra(**params):
            return P.adicionar(m, "barra", {"ni": "n1", "nj": "n2", "secao": "W310x38.7", "material": "A-572 GR.50",
                                            "params": params})
        for ok in ({"Cb": 1.0}, {"Cb": 3.0}, {"Ct": 1.0}, {"Ct": 0.9}, {"emenda": "luva"}, {"emenda": "traspasse"}):
            barra(**ok)
        for ruim, campo in (({"Cb": 5.0}, "params.Cb"), ({"Cb": 0.9}, "params.Cb"), ({"Ct": 1.5}, "params.Ct"),
                            ({"emenda": "solda"}, "params.emenda")):
            with self.assertRaises(P.ErroEntrada) as c:
                barra(**ruim)
            self.assertEqual([e["campo"] for e in c.exception.erros], [campo])
        with self.assertRaises(P.ErroEntrada) as c:
            barra(Cb=5.0)
        self.assertIn("5.4.2.3", str(c.exception))  # a mensagem cita o item da norma
        self.assertNotIn("param_desconhecido", [v["codigo"] for v in P.validar(m)])  # emenda é parâmetro conhecido


def edificio_1_andar_aco(h=3.0, lado=5.0, G_no=250.0):
    """Edifício de aço de 1 andar (lado × lado), G1 = 4 · G_no no topo, sem peso próprio automático."""
    m = P.novo_modelo("1 andar")
    P.gerar_estrutura(m, {"tipo": "edificio", "material": {"aco": "A-572 GR.50"},
                          "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                          "vaos_x": [lado], "vaos_y": [lado], "alturas": [h]})
    m.peso_proprio = None
    for n in [n for n in m.nos.values() if abs(n.z - h) < 1e-6]:
        P.adicionar(m, "carga_nodal", {"caso": "G1", "no": n.id, "F": [0, 0, -G_no, 0, 0, 0]})
    return m


SISMO_C = {"zona": 2, "classe_terreno": "C", "sistema": "aco_portico"}


class TestSismoNovidades(unittest.TestCase):
    def test_torcao_acidental_binario_de_25_kNm(self):
        """Sismo zona 2 (ag = 0,10g), terreno C (Ca = 1,2), R = 3, I = 1, pórtico de aço, hn = 3 m, W = 1000 kN:
        Ta = 0,0724·3^0,8 = 0,1744 s; Cs = min(2,5·1,2·0,10/3 = 0,100; 1,7·0,10/(0,1744·3) = 0,325) = 0,100 →
        SX = 100 kN. Torção acidental e = 5% · 5 m = 0,25 m → Mz = 25 kN·m (binário sem resultante)."""
        m = edificio_1_andar_aco()
        P.configurar(m, {"sismo": {**SISMO_C, "R": 3.0}})
        r = P.gerar_acoes(m)
        self.assertEqual(r["casos"], ["SX", "SY", "SX_TOR", "SY_TOR"])
        self.assertAlmostEqual(r["forcas"]["SX"][0]["F_kN"], 100.0, delta=1e-6)
        self.assertAlmostEqual(r["forcas"]["SX_TOR"][0]["Mz_kNm"], 25.0, delta=1e-6)
        for caso in ("SX_TOR", "SY_TOR"):
            self.assertEqual(m.casos[caso].tipo, "S")
            cs = [c for c in m.cargas_nodais if c.caso == caso]
            self.assertEqual(len(cs), 4)
            self.assertAlmostEqual(sum(c.F[0] for c in cs), 0.0, delta=1e-9)
            self.assertAlmostEqual(sum(c.F[1] for c in cs), 0.0, delta=1e-9)
            torque = sum(m.nos[c.no].x * c.F[1] - m.nos[c.no].y * c.F[0] for c in cs)
            self.assertAlmostEqual(torque, 25.0, delta=1e-9)
            # quatro cantos de 5 × 5 m (r = 5/√2): |F| = Mz/(4·r) = 25/(4 · 3,5355) = 1,7678 kN em cada nó
            self.assertAlmostEqual(math.hypot(cs[0].F[0], cs[0].F[1]), 25.0 / (4 * 5 / math.sqrt(2)), delta=1e-9)
        c = P.calcular(m)
        com_tor = [x for x in c.combinacoes if "SX_TOR" in x["fatores"]]
        self.assertTrue(com_tor and all(x["estado"] == "ELU_EXC" for x in com_tor))
        self.assertEqual({x["fatores"]["SX_TOR"] for x in com_tor}, {1.0, -1.0})
        # idempotente e removível: a config sem sismo apaga os casos _TOR
        n = len(m.cargas_nodais)
        P.gerar_acoes(m)
        self.assertEqual(len(m.cargas_nodais), n)
        P.configurar(m, {"sismo": None})
        P.gerar_acoes(m)
        self.assertFalse([k for k in m.casos if "SX" in k or "SY" in k])
        self.assertFalse([k for k in m.cargas_nodais if k.id.startswith("auto_")])

    def test_modelo_plano_dispensa_a_torcao_com_aviso(self):
        m = coluna({"aco": "A-572 GR.50"}, 0.3)
        P.configurar(m, {"sismo": {**SISMO_C, "R": 3.0}})
        r = P.gerar_acoes(m)
        self.assertEqual(r["casos"], ["SX", "SY"])
        self.assertIn("sismo_torcao_dispensada", [a["codigo"] for a in r["avisos"]])

    def test_sistema_resistente_R_Cd_e_drift(self):
        """portico_aco_especial (NBR 15421:2006 tab. 6): R = 8, Cd = 5,5. Coluna de 4 m (EI = 135 000 kN·m²),
        W = 1000 kN, zona 2, terreno C, Ta = 0,0724·4^0,8 = 0,2195 s: Cs = 2,5·0,12/8 = 0,0375 (o ramo 1/T daria
        0,0968) → F = 37,5 kN; δxe = F·h³/(3EI) = 5,926 mm; δx = Cd·δxe/I = 32,59 mm; limite 0,020·4 m = 80 mm
        (categoria I) → razão 0,4074."""
        m = coluna({"aco": "A-572 GR.50"}, 0.3)
        P.configurar(m, {"sismo": {**SISMO_C, "sistema_resistente": "portico_aco_especial"}})
        r = P.gerar_acoes(m)
        self.assertAlmostEqual(r["forcas"]["SX"][0]["F_kN"], 37.5, delta=1e-6)
        self.assertEqual((r["parametros"]["SX"]["R"], r["parametros"]["SX"]["Cd"]), (8.0, 5.5))
        c = P.calcular(m)
        sis = [x for x in c.deslocamentos["sismicos"] if x["direcao"] == "X"]
        self.assertEqual(len(sis), 1)
        dxe = 37.5 * 4 ** 3 / (3 * 135000.0)
        self.assertAlmostEqual(sis[0]["delta_x_m"], 5.5 * dxe, delta=1e-9)
        self.assertAlmostEqual(sis[0]["valor_m"], 5.5 * dxe, delta=1e-9)
        self.assertAlmostEqual(sis[0]["limite_m"], 0.08, delta=1e-12)
        self.assertAlmostEqual(sis[0]["taxa"], 5.5 * dxe / 0.08, delta=1e-9)
        self.assertAlmostEqual(sis[0]["taxa"], 0.4074, delta=1e-4)
        self.assertTrue(sis[0]["ok"])
        # coluna quadrada: SY dá o mesmo deslocamento em Y
        self.assertAlmostEqual(next(x for x in c.deslocamentos["sismicos"] if x["direcao"] == "Y")["valor_m"], 5.5 * dxe, delta=1e-9)
        json.dumps(c.to_dict(), allow_nan=False)

    def test_drift_reprova_quando_passa_do_limite(self):
        """Categoria III (I = 1,5; limite 0,010·4 = 40 mm), Cd = 5,5, coluna 0,2 × 0,2 (EI = 2,0e8·0,2⁴/12 =
        26 667 kN·m²): a força cresce com I (Cs = 2,5·ags0·I/R): F = 1,5·37,5 = 56,25 kN; δxe = 56,25·64/(3·EI) =
        45,0 mm → δx = 5,5·45/1,5 = 165 mm > 40 mm."""
        m = coluna({"aco": "A-572 GR.50"}, 0.2)
        P.configurar(m, {"sismo": {**SISMO_C, "categoria_utilizacao": "III",
                                   "sistema_resistente": "portico_aco_especial"}})
        c = P.calcular(m)
        s = next(x for x in c.deslocamentos["sismicos"] if x["direcao"] == "X")
        EI = 2.0e8 * 0.2 ** 4 / 12
        self.assertAlmostEqual(s["valor_m"], 5.5 * (56.25 * 64 / (3 * EI)) / 1.5, delta=1e-9)
        self.assertAlmostEqual(s["valor_m"], 0.165, delta=1e-3)
        self.assertAlmostEqual(s["limite_m"], 0.04, delta=1e-12)
        self.assertFalse(s["ok"])
        self.assertEqual(c.resumo()["n_desloc_reprovados"], 2)  # SX e SY

    def test_sem_sistema_resistente_avisa_que_nao_verificou_o_drift(self):
        m = coluna({"aco": "A-572 GR.50"}, 0.3)
        P.configurar(m, {"sismo": {**SISMO_C, "R": 3.0}})
        c = P.calcular(m)
        self.assertEqual(c.deslocamentos["sismicos"], [])
        self.assertIn("sismo_drift_nao_verificado", [a["codigo"] for a in c.avisos])

    def test_config_sismo_valida_sistema_resistente(self):
        m = coluna({"aco": "A-572 GR.50"}, 0.3)
        base = {"zona": 2, "classe_terreno": "C"}
        with self.assertRaises(P.ErroEntrada):
            P.configurar(m, {"sismo": {**base, "sistema_resistente": "inexistente"}})
        with self.assertRaises(P.ErroEntrada):  # sem R e sem sistema_resistente
            P.configurar(m, {"sismo": base})
        P.configurar(m, {"sismo": {**base, "sistema_resistente": "portico_concreto_usual"}})  # R vem da tabela 6

    def test_subtipo_de_Q_desconhecido_no_peso_sismico(self):
        m = edificio_1_andar_aco()
        m.casos["Q1"].subtipo = "balada"
        P.configurar(m, {"sismo": {**SISMO_C, "R": 3.0}})
        with self.assertRaises(P.ErroEntrada) as c:
            P.gerar_acoes(m)
        self.assertIn("balada", str(c.exception))


class TestVentoGalpao(unittest.TestCase):
    """vento.casos_vento_galpao ligado a gerar_acoes (VC-logica-acoes-004). Galpão de 20 m de vão, pilar 6 m,
    cumeeira 8 m (θ = atan(2/10) = 11,31°), 6 módulos de 5 m; V0 = 40 m/s, categoria III, classe B, S1 = S3 = 1."""

    @classmethod
    def setUpClass(cls):
        cls.m = P.novo_modelo("Galpão")
        P.gerar_estrutura(cls.m, {"tipo": "galpao", "material": {"aco": "A-572 GR.50"},
                                  "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                                  "vao": 20.0, "altura_pilar": 6.0, "altura_cumeeira": 8.0, "n_modulos": 6,
                                  "espacamento": 5.0})
        P.configurar(cls.m, {"vento": {"V0": 40, "categoria": "III", "classe": "B"}})
        cls.r = P.gerar_acoes(cls.m)

    def _soma_z(self, caso, portico):
        """Força vertical total (kN, + para cima) do vento `caso` nas duas águas do pórtico."""
        tot = 0.0
        for b in (f"VX2-0-{portico}", f"VX2-1-{portico}"):
            L = P._comprimento(self.m, self.m.barras[b])
            tot += sum(c.q[2] * L for c in self.m.cargas_distribuidas if c.caso == caso and c.barra == b)
        return tot

    def test_casos_por_direcao_e_cpi(self):
        self.assertEqual(self.r["casos"], [f"V{d} Cpi={c}" for d in ("+X", "-X", "+Y", "-Y") for c in ("0", "-0.3")])
        for nome in self.r["casos"]:
            self.assertEqual((self.m.casos[nome].tipo, self.m.casos[nome].subtipo), ("V", "vento"))
        # 4 barras carregadas por pórtico × 7 pórticos × 8 casos
        self.assertEqual(len([c for c in self.m.cargas_distribuidas if c.id.startswith("auto_")]), 4 * 7 * 8)
        self.assertEqual([c for c in self.m.cargas_nodais if c.id.startswith("auto_")], [])
        self.assertEqual([v for v in P.validar(self.m) if v["nivel"] == "erro"], [])

    def test_succao_vertical_do_portico_interno_a_mao(self):
        """V+X, Cpi = 0, pórtico interno (largura de influência 5 m): S2(8 m) = 0,94·0,98·0,8^0,105 = 0,8999;
        q = 0,613·(40·0,8999)² = 794,2 N/m²; h/b = 0,3 ≤ 1/2; Ce do telhado (tab. 5, α = 90°): EF = −1,2 +
        (11,31 − 10)/5·0,2 = −1,1476 (barlavento), GH = −0,4. Sucção vertical = (1,1476 + 0,4)·q·5·(b/2 = 10 m)
        = 61,4 kN por pórtico para cima (a componente é w·cos θ · L = w · 10 m)."""
        s2 = 0.94 * 0.98 * 0.8 ** 0.105
        q = 0.613 * (40 * s2) ** 2 / 1000
        theta = math.degrees(math.atan2(2.0, 10.0))
        ef = -1.2 + (theta - 10.0) / 5.0 * 0.2
        esperado = (-ef + 0.4) * q * 5.0 * 10.0
        self.assertAlmostEqual(esperado, 61.4, delta=0.1)
        self.assertAlmostEqual(self._soma_z("V+X Cpi=0", 2), esperado, delta=0.003 * esperado)
        # Cpi = −0,3 (sobrepressão interna, 'quatro faces'): C = Ce + 0,3 → menos sucção
        self.assertAlmostEqual(self._soma_z("V+X Cpi=-0.3", 2), (-ef + 0.4 - 0.6) * q * 5.0 * 10.0, delta=0.003 * esperado)
        # vento −X inverte o lado de barlavento, mesma soma
        self.assertAlmostEqual(self._soma_z("V-X Cpi=0", 2), esperado, delta=0.003 * esperado)
        # pórtico de extremidade: metade da largura de influência (2,5 m) no vento ±X
        self.assertAlmostEqual(self._soma_z("V+X Cpi=0", 0), esperado / 2, delta=0.003 * esperado)

    def test_idempotente_e_limpa_ao_remover_o_vento(self):
        m = _copia(self.m)
        n = len(m.cargas_distribuidas)
        P.gerar_acoes(m)
        self.assertEqual(len(m.cargas_distribuidas), n)
        P.configurar(m, {"vento": None})
        P.gerar_acoes(m)
        self.assertFalse([c for c in m.casos if "Cpi" in c])
        self.assertFalse([c for c in m.cargas_distribuidas if c.id.startswith("auto_")])

    def test_permeabilidade_cpi_e_erros_de_entrada(self):
        m = _copia(self.m)
        P.configurar(m, {"vento": {"V0": 40, "categoria": "III", "classe": "B", "permeabilidade": "duas_faces_opostas"}})
        self.assertEqual([c for c in P.gerar_acoes(m)["casos"] if c.startswith("V+X")], ["V+X Cpi=0.2", "V+X Cpi=-0.3"])
        P.configurar(m, {"vento": {"V0": 40, "categoria": "III", "classe": "B", "cpi": [0.1]}})
        self.assertEqual([c for c in P.gerar_acoes(m)["casos"] if c.startswith("V+X")], ["V+X Cpi=0.1"])
        with self.assertRaises(P.ErroEntrada):
            P.configurar(m, {"vento": {"V0": 40, "categoria": "III", "classe": "B", "permeabilidade": "meia"}})
        # h/b > 3/2: o motor recusa (fora das tabelas) e vira erro de entrada
        alto = _copia(self.m)
        P.configurar(alto, {"vento": {"V0": 40, "categoria": "III", "classe": "B",
                                      "geometria": {"vao": 5.0, "altura_pilar": 8.0, "altura_cumeeira": 9.0,
                                                    "n_modulos": 6, "espacamento": 5.0}}})
        with self.assertRaises(P.ErroEntrada) as c:
            P.gerar_acoes(alto)
        self.assertIn("3/2", str(c.exception))
        # galpão explícito sem gerador nem geometria
        p = portico()
        P.configurar(p, {"vento": {"V0": 40, "categoria": "III", "classe": "B", "tipo": "galpao"}})
        self.assertIn("vento_galpao", [v["codigo"] for v in P.validar(p)])

    def test_calcula_o_galpao_com_vento_de_cobertura(self):
        m = P.abrir_exemplo("galpao_aco")
        P.configurar(m, {"vento": {"V0": 40, "categoria": "III", "classe": "B"}})
        c = P.calcular(m)
        self.assertTrue(any("Cpi=" in k for x in c.combinacoes for k in x["fatores"]))
        self.assertFalse(c.estabilidade["aplicavel"])  # sem pavimentos: γz e B2 não se aplicam
        # a sucção da cobertura inverte o sinal da reação vertical em algum apoio (G leve + vento): tração
        self.assertTrue(any(r["min"][2] < 0 for r in c.reacoes_envoltoria.values()))


class TestEntradasAbsurdas(unittest.TestCase):
    """Achados E2E-001/002/003: unidade errada não passa em silêncio."""

    def test_V0_absurdo_e_erro_e_fora_da_faixa_avisa(self):
        m = edificio_aco(vento=False)
        with self.assertRaises(P.ErroEntrada) as c:
            P.configurar(m, {"vento": {"V0": 3500, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 15, "ly": 10}}})
        self.assertIn("V0", str(c.exception))
        P.configurar(m, {"vento": {"V0": 12, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 15, "ly": 10}}})
        self.assertIn("vento_V0_fora_da_faixa", [a["codigo"] for a in P.gerar_acoes(m)["avisos"]])
        P.configurar(m, {"vento": {"V0": 35, "categoria": "IV", "classe": "B", "dimensoes": {"lx": 15, "ly": 10}}})
        self.assertNotIn("vento_V0_fora_da_faixa", [a["codigo"] for a in P.gerar_acoes(m)["avisos"]])

    def test_dimensoes_suspeitas_avisam_e_coordenada_absurda_e_erro(self):
        m = portico()
        sec = P.adicionar(m, "secao", {"forma": "retangular", "b": 25, "h": 50})["nome"]
        P.adicionar(m, "no", {"id": "alto", "x": 0, "y": 0, "z": 3000})
        P.adicionar(m, "barra", {"id": "b9", "ni": "n1", "nj": "alto", "secao": sec, "material": "A-572 GR.50"})
        v = P.validar(m)
        sus = [x for x in v if x["codigo"] == "dimensao_suspeita"]
        self.assertEqual({x["ref"]["id"] for x in sus}, {sec, "b9"})
        self.assertFalse([x for x in v if x["nivel"] == "erro"])
        P.editar(m, "no", "alto", {"z": 1e308})
        self.assertIn("geometria_absurda", [x["codigo"] for x in P.validar(m) if x["nivel"] == "erro"])
        with self.assertRaises(P.ErroEntrada):
            P.analisar_caso(m, "G1")


class TestParamsTrechoCortante(unittest.TestCase):
    def setUp(self):
        from prossen_estrutural import secoes
        self.m = portico()
        sec = secoes.secao_retangular(0.3, 0.5, nome="Retangular cortante")
        self.m.add(sec)
        P.editar(self.m, "barra", "b2", {"secao": sec.nome})
        self.secao = sec

    def test_aceita_sem_aviso_e_calcula(self):
        params = {"trecho_rigido_i": 0.2, "trecho_rigido_j": 0.3, "cortante": True}
        P.editar(self.m, "barra", "b2", {"params": params})
        P.adicionar(self.m, "barra", {"id": "extra", "ni": "n1", "nj": "n4",
                    "secao": self.secao.nome, "material": "A-572 GR.50", "params": params})
        avisos = P.validar(self.m)
        self.assertFalse([a for a in avisos if a["nivel"] == "erro" or a["codigo"] == "param_desconhecido"])
        calculo = P.calcular(self.m)
        self.assertTrue(calculo.combinacoes)
        self.assertFalse([a for a in calculo.avisos if a["codigo"] == "param_desconhecido"])

    def test_recusa_trechos_e_booleano_invalidos_sem_mutar(self):
        for chave in ("trecho_rigido_i", "trecho_rigido_j"):
            for valor in (-0.1, float("nan"), float("inf"), "0.2", True, None):
                with self.subTest(chave=chave, valor=valor):
                    # None na edição remove a chave; na adição é entrada inválida.
                    with self.assertRaises(P.ErroEntrada) as ctx:
                        P.adicionar(self.m, "barra", {"id": "ruim", "ni": "n2", "nj": "n3",
                                    "secao": self.secao.nome, "material": "A-572 GR.50",
                                    "params": {chave: valor}})
                    self.assertIn("params." + chave, [e["campo"] for e in ctx.exception.erros])
        for params in ({"trecho_rigido_i": -1}, {"trecho_rigido_i": 3, "trecho_rigido_j": 3},
                       {"trecho_rigido_j": 7}, {"cortante": "sim"}, {"cortante": 1}):
            with self.subTest(params=params), self.assertRaises(P.ErroEntrada) as ctx:
                P.editar(self.m, "barra", "b2", {"params": params})
            self.assertTrue(all(e["campo"].startswith("params.") for e in ctx.exception.erros))
            self.assertEqual(self.m.barras["b2"].params, {})

    def test_cortante_exige_areas_validas(self):
        for chave in ("Ay", "Az"):
            for valor in (None, 0, -1, float("nan"), float("inf"), True):
                with self.subTest(chave=chave, valor=valor):
                    props = dict(self.secao.props)
                    self.secao.props[chave] = valor
                    with self.assertRaises(P.ErroEntrada) as ctx:
                        P.editar(self.m, "barra", "b2", {"params": {"cortante": True}})
                    self.assertEqual(ctx.exception.erros[0]["campo"], "params.cortante")
                    self.assertIn("sem área de cisalhamento Ay/Az", str(ctx.exception))
                    self.secao.props = props
        self.secao.props.pop("Ay")
        P.editar(self.m, "barra", "b2", {"params": {"cortante": False}})
        self.m.barras["b2"].params = {"cortante": True}
        self.assertTrue([a for a in P.validar(self.m) if a["nivel"] == "erro" and "Ay/Az" in a["mensagem"]])

    def test_validar_detecta_params_de_arquivo_e_mudanca_de_comprimento(self):
        for params in ({"trecho_rigido_i": -1}, {"trecho_rigido_j": float("inf")},
                       {"trecho_rigido_i": 3, "trecho_rigido_j": 3}, {"cortante": 1}):
            with self.subTest(params=params):
                self.m.barras["b2"].params = params
                self.assertTrue([a for a in P.validar(self.m) if a["nivel"] == "erro" and "params." in a["mensagem"]])
        self.m.barras["b2"].params = {"trecho_rigido_i": 2}
        P.editar(self.m, "no", "n3", {"x": 1})
        self.assertTrue([a for a in P.validar(self.m) if a["nivel"] == "erro" and "params.trecho_rigido_i" in a["mensagem"]])


class TestParamsDoMotorDeAco(unittest.TestCase):
    """L, Lv, tubo_com_costura (NBR 8800) e Ndist/Mdist (NBR 14762) chegam aos motores; Cb/Ct fora da faixa
    viram erro de entrada (na edição) ou barra não verificada com o motivo (modelo vindo de arquivo)."""

    def _tubo(self, **params):
        m = P.novo_modelo("tubo")
        P.adicionar_lote(m, [
            {"tipo": "material", "dados": {"aco": "A-572 GR.50"}},
            {"tipo": "secao", "dados": {"forma": "tubo_circular", "d": 0.2191, "t": 0.0064}},
            {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "engaste"}},
            {"tipo": "no", "dados": {"id": "n2", "x": 0, "y": 0, "z": 3}},
            {"tipo": "caso", "dados": {"nome": "Q1", "tipo": "Q"}},
            {"tipo": "carga_nodal", "dados": {"caso": "Q1", "no": "n2", "F": [20, 0, 0, 0, 0, 0]}}])
        sec = next(iter(m.secoes))
        P.adicionar(m, "barra", {"id": "b1", "ni": "n1", "nj": "n2", "secao": sec, "material": "A-572 GR.50", "params": params})
        return m

    def test_costura_e_Lv_chegam_ao_motor(self):
        sem = P.verificacao_barra(P.calcular(self._tubo()), "b1")
        com = P.verificacao_barra(P.calcular(self._tubo(tubo_com_costura=True, Lv=1.5)), "b1")
        self.assertFalse([a for a in sem["avisos"] if "costura" in a])
        self.assertTrue([a for a in com["avisos"] if "costura" in a])  # espessura de cálculo 0,93t
        self.assertGreater(com["taxa"], sem["taxa"])  # parede mais fina: menos resistência
        self.assertNotIn("param_desconhecido", [v["codigo"] for v in P.validar(self._tubo(Ndist=100.0, Mdist=50.0, Lv=2.0))])

    def test_params_novos_validados(self):
        m = self._tubo()
        for ruim in ({"Lv": -1.0}, {"Ndist": 0}, {"Mdist": "x"}, {"tubo_com_costura": "sim"}, {"Ct": 0.0}):
            with self.assertRaises(P.ErroEntrada):
                P.editar(m, "barra", "b1", {"params": ruim})

    def test_Ct_fora_da_faixa_em_arquivo_vira_barra_nao_verificada_com_motivo(self):
        m = self._tubo()
        m.barras["b1"].params["Ct"] = 1.5  # contorna a validação de edição, como um arquivo antigo
        l = P.calcular(m).verificacao[0]
        self.assertEqual(l["criterio"], "nao_verificada")
        self.assertIn("Ct", l["motivo"])


# ================================================================ importação e conferência com o CYPE (.ed3)

DADOS_CYPE = Path(__file__).resolve().parent / "dados_cype"
ORIGINAL_CYPE = Path(r"C:\CYPE Ingenieros\Projetos\CYPE 3D")
T4 = "PIPERACK TESTE 4.ed3"  # pórtico plano, só PP + carga nodal; resultados válidos (ver benchmark)


class TestImportarCype(unittest.TestCase):
    """Obras reais do CYPE 3D 2016 em testes/dados_cype/. O pórtico TESTE 4 tem 4 barras e 'PP' como caso."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.pasta = Path(self.tmp.name)
        self.addCleanup(self.tmp.cleanup)

    def copia(self, nome=T4, destino="obras/t4.ed3"):
        d = self.pasta / destino
        d.parent.mkdir(parents=True, exist_ok=True)
        d.write_bytes((DADOS_CYPE / nome).read_bytes())
        return d

    def test_caminho_relativo_na_pasta_de_trabalho(self):
        self.copia()
        m = P.importar_cype("obras/t4.ed3", pasta=self.pasta)
        self.assertEqual(len(m.barras), 4)
        self.assertIn("PP", m.casos)
        self.assertIsInstance(m.avisos, list)
        self.assertEqual(P.validar(m), [])  # modelo importado é utilizável como qualquer outro

    def test_caminho_absoluto_na_pasta_e_na_pasta_do_cype_somente_leitura(self):
        a = self.copia()
        self.assertEqual(len(P.importar_cype(str(a), pasta=self.pasta).barras), 4)
        outra = tempfile.TemporaryDirectory()
        self.addCleanup(outra.cleanup)
        (Path(outra.name) / "o.ed3").write_bytes((DADOS_CYPE / T4).read_bytes())
        antes = sorted(p.name for p in Path(outra.name).iterdir())
        with mock.patch.object(P, "PASTA_CYPE", Path(outra.name)):
            self.assertEqual(len(P.importar_cype(str(Path(outra.name) / "o.ed3"), pasta=self.pasta).barras), 4)
            with self.assertRaises(P.ErroEntrada):  # relativo nunca vale na pasta do CYPE
                P.importar_cype("o.ed3", pasta=self.pasta)
        self.assertEqual(sorted(p.name for p in Path(outra.name).iterdir()), antes)  # nada gravado lá

    def test_recusa_fora_das_pastas_permitidas(self):
        fora = tempfile.TemporaryDirectory()
        self.addCleanup(fora.cleanup)
        (Path(fora.name) / "f.ed3").write_bytes((DADOS_CYPE / T4).read_bytes())
        self.copia()
        for ruim in (str(Path(fora.name) / "f.ed3"), "../f.ed3", "obras/../../f.ed3", "C:x.ed3", "", "obras/t4.txt"):
            with self.subTest(ruim=ruim), self.assertRaises(P.ErroEntrada) as c:
                P.importar_cype(ruim, pasta=self.pasta)
            self.assertEqual(c.exception.erros[0]["campo"], "caminho")
        with mock.patch.object(P, "PASTA_CYPE", Path(fora.name)):
            with self.assertRaises(P.ErroEntrada):
                P.importar_cype(str(Path(fora.name) / ".." / "f.ed3"), pasta=self.pasta)

    def test_arquivo_ausente_e_formato_invalido_viram_erro_de_entrada_com_campo(self):
        with self.assertRaises(P.ErroEntrada) as c:
            P.importar_cype("nao_existe.ed3", pasta=self.pasta)
        self.assertEqual(c.exception.erros[0]["campo"], "caminho")
        (self.pasta / "lixo.ed3").write_bytes(b"isto nao e CYPE" * 10)
        with self.assertRaises(P.ErroEntrada) as c:
            P.importar_cype("lixo.ed3", pasta=self.pasta)
        self.assertEqual(c.exception.erros[0]["campo"], "caminho")
        self.assertIn("CYPE", c.exception.erros[0]["mensagem"])
        truncado = (DADOS_CYPE / T4).read_bytes()[:3000]  # cabeçalho válido, corpo cortado
        with self.assertRaises(P.ErroEntrada) as c:
            P.importar_cype(truncado, nome="t.ed3")
        self.assertEqual(c.exception.erros[0]["campo"], "conteudo")

    def test_bytes_do_upload_nao_deixa_rastro(self):
        usadas = []
        real = tempfile.TemporaryDirectory

        def registra(*a, **k):
            d = real(*a, **k)
            usadas.append(Path(d.name))
            return d

        with mock.patch.object(P.tempfile, "TemporaryDirectory", registra):
            m = P.importar_cype((DADOS_CYPE / T4).read_bytes(), nome="meu pórtico.ed3")
        self.assertEqual(len(m.barras), 4)
        self.assertEqual(len(usadas), 1)
        self.assertFalse(usadas[0].exists())  # pasta temporária própria, removida
        com_caminho = P.importar_cype(self.copia(), pasta=self.pasta)
        self.assertEqual(sorted(m.barras), sorted(com_caminho.barras))
        with self.assertRaises(P.ErroEntrada):
            P.importar_cype(b"")
        with self.assertRaises(P.ErroEntrada):
            P.importar_cype(b"x" * (P.LIMITE_ED3 + 1))

    def test_comparar_cype_equilibrio_bate_e_json_puro(self):
        """PP do TESTE 4: o benchmark (verificacao/2026-10-20-benchmark-cype.md) dá ΣR a 0,04 % do CYPE;
        aqui ΣRz fecha em 0,5 % e a tabela sai em JSON puro."""
        self.copia()
        r = P.comparar_cype("obras/t4.ed3", pasta=self.pasta)
        json.dumps(r, allow_nan=False)
        self.assertTrue(r["valido"])
        self.assertEqual(r["hipoteses"], ["PP"])
        lin = {(l["hipotese"], l["grandeza"]): l for l in r["tabela"]}
        soma = lin[("PP", "reacao_total_Fz")]
        self.assertLess(abs(soma["dif_pct"]), 0.5)
        self.assertEqual(soma["unidade"], "kN")
        for g in ("desloc_max", "reacao_total_Fx", "reacao_max_apoio", "N_max", "V_max", "M_max", "T_max"):
            self.assertIn(("PP", g), lin, g)
        for l in r["tabela"]:  # dif % = (nosso − CYPE)/|CYPE|·100
            if l["dif_pct"] is not None:
                self.assertAlmostEqual(l["dif_pct"], (l["nosso"] - l["cype"]) / abs(l["cype"]) * 100, places=9)
        self.assertEqual(len(r["taxas"]), 4)
        self.assertTrue(all(t["cype"] is not None and t["nosso"] is not None for t in r["taxas"]))

    def test_comparar_cype_marca_resultados_desatualizados(self):
        d = DADOS_CYPE / "DIMENSIONAMENTO DE JUNTA.ed3"
        r = P.comparar_cype(d.read_bytes(), nome=d.name)  # bytes: o fluxo de upload
        self.assertFalse(r["valido"])
        self.assertTrue([a for a in r["avisos"] if "desatualizad" in a])

    def test_comparar_cype_obra_sem_resultados_e_erro_de_entrada(self):
        with self.assertRaises(P.ErroEntrada):
            P.comparar_cype((DADOS_CYPE / "novo.ed3").read_bytes(), nome="novo.ed3")

    @unittest.skipUnless((ORIGINAL_CYPE / "PIPERACK.ed3").exists(), "PIPERACK.ed3 só no caminho original")
    @unittest.skipIf(os.environ.get("PROSSEN_PULAR_LENTOS") == "1", "lento")
    def test_piperack_original_somente_leitura(self):
        p = ORIGINAL_CYPE / "PIPERACK.ed3"
        antes = p.stat().st_mtime_ns
        m = P.importar_cype(str(p), pasta=self.pasta)
        self.assertEqual((len(m.nos), len(m.barras)), (102, 177))
        self.assertEqual(p.stat().st_mtime_ns, antes)


class TestCorrecoesRevisao(unittest.TestCase):
    """Achados da revisão 2026-10-20-camadas dos quais o engenheiro-integracao é dono."""

    UNC = (r"\\host\share\x.json", "//host/share/x.json", r"\\?\C:\x.json", r"\\.\pipe\x",
           r"\\?\UNC\host\share\x.json", r"\/host/share/x.json", r"  \\host\x")

    def test_unc_recusado_antes_de_qualquer_resolve(self):
        """No Windows o resolve abre o caminho e autentica no SMB remoto (NTLM): a recusa vem antes."""
        def nao_pode(self, *a, **k):
            raise AssertionError(f"resolve chamado em {self}")
        with tempfile.TemporaryDirectory() as t, mock.patch.object(Path, "resolve", nao_pode):
            for rel in self.UNC:
                with self.subTest(rel=rel):
                    with self.assertRaises(P.ErroEntrada) as c:
                        P.caminho_seguro(t, rel)
                    self.assertIn("UNC", str(c.exception))
                    with self.assertRaises(P.ErroEntrada):
                        P._caminho_ed3(rel[:-5] + ".ed3" if rel.endswith(".json") else rel + ".ed3", t)
                    with self.assertRaises(P.ErroEntrada):
                        P.importar_cype(rel + ".ed3", pasta=t)

    def test_editar_material_acompanha_tipo_da_barra(self):
        m = portico()
        P.adicionar(m, "secao", {"forma": "retangular", "b": 0.2, "h": 0.4})
        P.adicionar(m, "material", {"concreto": 25})
        conc = next(k for k, v in m.materiais.items() if v.fck is not None)
        P.editar(m, "barra", "b1", {"secao": "R 200x400", "material": conc})
        self.assertEqual(m.barras["b1"].tipo, "concreto")
        P.editar(m, "barra", "b1", {"material": "A-572 GR.50"})  # volta ao aço
        self.assertEqual(m.barras["b1"].tipo, "aco")
        P.editar(m, "barra", "b2", {"material": conc})  # só o material (seção I de aço): sem TypeError
        self.assertEqual(m.barras["b2"].tipo, "concreto")
        c = P.calcular(m)
        self.assertEqual(next(x for x in c.verificacao if x["barra"] == "b2")["criterio"], "nao_verificada")
        P.editar(m, "barra", "b3", {"tipo": "pff"})  # tipo explícito do usuário vale
        P.editar(m, "barra", "b3", {"material": "A-572 GR.50"})
        self.assertEqual(m.barras["b3"].tipo, "pff")

    def test_typeerror_do_motor_vira_nao_verificada(self):
        m = portico()
        m.barras["b1"].material = next(iter(m.materiais))
        c0 = P.calcular(m)
        with mock.patch.object(P, "_aco_barra", side_effect=TypeError("None / float")),                 self.assertLogs(P.log, "ERROR"):
            c = P.calcular(m)
        self.assertTrue(all(l["criterio"] == "nao_verificada" for l in c.verificacao))
        self.assertEqual(c.resumo()["n_nao_verificadas"], len(c0.verificacao))

    def test_id_nao_texto_e_erro_de_entrada(self):
        m = portico()
        for tipo, dados in (("barra", {"ni": ["n1"], "nj": "n2", "secao": "W310x38.7", "material": "A-572 GR.50"}),
                            ("barra", {"ni": "n1", "nj": {"a": 1}, "secao": "W310x38.7", "material": "A-572 GR.50"}),
                            ("carga_distribuida", {"caso": ["G1"], "barra": "b1", "q": [0, 0, -1]}),
                            ("carga_distribuida", {"caso": "G1", "barra": ["b1"], "q": [0, 0, -1]}),
                            ("carga_nodal", {"caso": "G1", "no": ["n1"], "F": [0, 0, -1, 0, 0, 0]})):
            with self.subTest(tipo=tipo, dados=dados), self.assertRaises(P.ErroEntrada):
                P.adicionar(m, tipo, dados)

    def test_lb_padrao_com_trecho_rigido_e_a_face_a_face(self):
        """Trecho destravado padrão = [trecho_rigido_i, L − trecho_rigido_j]: a barra com trechos rígidos
        é verificada (antes: 'trecho destravado [0, L] fora da barra') e o Lb padrão é L − ri − rj."""
        m = portico()
        P.editar(m, "barra", "b2", {"params": {"trecho_rigido_i": 0.2, "trecho_rigido_j": 0.3}})
        c = P.calcular(m)
        l = next(x for x in c.verificacao if x["barra"] == "b2")
        self.assertNotEqual(l["criterio"], "nao_verificada", l.get("motivo"))
        self.assertTrue(l["taxa"] > 0)
        visto = {}
        orig = nbr8800.verificar_barra_aco

        def espia(sec, mat, esf, Lx, Ly, Lb, *a, **k):
            visto["Lb"] = Lb
            return orig(sec, mat, esf, Lx, Ly, Lb, *a, **k)
        with mock.patch.object(nbr8800, "verificar_barra_aco", espia):
            P.calcular(m, {"barras": ["b2"]})
        self.assertAlmostEqual(visto["Lb"], 6.0 - 0.2 - 0.3, delta=1e-9)

    def test_balanco_em_modelo_plano(self):
        """Todos os nós com uy, rx, rz presos (modelo plano): a ponta sem outra restrição ainda é livre."""
        m = P.novo_modelo("balanço plano")
        plano = [False, True, False, True, False, True]
        P.adicionar_lote(m, [
            {"tipo": "material", "dados": {"aco": "A-572 GR.50"}},
            {"tipo": "secao", "dados": {"perfil": "W310x38.7"}},
            {"tipo": "no", "dados": {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True] * 6}},
            {"tipo": "no", "dados": {"id": "n2", "x": 3, "y": 0, "z": 0, "apoio": plano}},
            {"tipo": "barra", "dados": {"id": "b1", "ni": "n1", "nj": "n2", "secao": "W310x38.7",
                                        "material": "A-572 GR.50"}}])
        self.assertTrue(P._balanco(m, m.barras["b1"]))
        self.assertEqual(P._ponta_livre(m, m.barras["b1"]), -1)
        m.nos["n2"].apoio = (False, True, True, False, False, False)  # viga de 2 nós, apoio móvel: não é balanço
        m.nos["n1"].apoio = (True, True, True, True, False, False)
        self.assertFalse(P._balanco(m, m.barras["b1"]))


    def test_params_novos_validados_e_repassados(self):
        m = portico()
        for params in ({"ponto_aplicacao": "alto"}, {"C2": -1}, {"l0_x": 0}, {"t_alvenaria_meses": -1},
                       {"fracao_qp_pos_alvenaria": 1.5}):
            with self.subTest(params=params), self.assertRaises(P.ErroEntrada):
                P.editar(m, "barra", "b2", {"params": params})
        P.editar(m, "barra", "b2", {"params": {"ponto_aplicacao": "topo", "C2": 1.0}})
        visto = {}
        orig = nbr8800.verificar_barra_aco

        def espia(*a, **k):
            visto.update(k)
            return orig(*a, **k)
        with mock.patch.object(nbr8800, "verificar_barra_aco", espia):
            P.calcular(m, {"barras": ["b2"]})
        self.assertEqual((visto["ponto_aplicacao"], visto["C2"]), ("topo", 1.0))
        # concreto: os params de 15.6 e da flecha de alvenaria chegam ao motor
        P.adicionar(m, "secao", {"forma": "retangular", "b": 0.2, "h": 0.4})
        P.adicionar(m, "material", {"concreto": 25})
        conc = next(k for k, v in m.materiais.items() if v.fck is not None)
        P.editar(m, "barra", "b1", {"secao": "R 200x400", "material": conc,
                                    "params": {"l0_x": 3.0, "l0_y": 3.0, "t_alvenaria_meses": 2,
                                               "fracao_qp_pos_alvenaria": 0.5}})
        c = P.calcular(m)
        _, _, params = P._args_concreto(m, m.barras["b1"], c.resultados, ["ELU G"], None, None)
        self.assertEqual((params["l0_x"], params["t_alvenaria_meses"], params["fracao_qp_pos_alvenaria"]),
                         (3.0, 2, 0.5))


if __name__ == "__main__":
    unittest.main()
