"""Testes do servidor MCP (ficha 016): cliente MCP real, em processo e via stdio.

Valores de referência independentes do código: ΣRz = carga total (equilíbrio) e os valores de conferência de
`exemplos/LEIAME.md` (cálculo manual: viga biapoiada qL/2 = 30 kN e qL²/8 = 45 kN·m; pórtico V1 ΣRx = −10 kN).
Os números do fluxo F5 são comparados com a chamada direta de `projeto` (1e-9).
"""
import asyncio
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

SOFTWARE = Path(__file__).resolve().parent.parent
RAIZ = SOFTWARE.parent
sys.path.insert(0, str(SOFTWARE))

from mcp import Client, StdioServerParameters  # noqa: E402

from prossen_estrutural import projeto, servidor_mcp  # noqa: E402

TOL = 1e-9
QUARENTA = {
    "modelo_novo", "modelo_abrir", "modelo_salvar", "modelo_exemplos", "modelo_abrir_exemplo",
    "modelo_exportar_json", "modelo_importar_json", "modelo_resumo", "modelo_validar", "modelo_geometria",
    "elementos_listar", "no_adicionar", "material_adicionar", "secao_adicionar", "barra_adicionar",
    "caso_adicionar", "carga_adicionar", "pavimento_adicionar", "lote_adicionar", "elemento_editar",
    "elemento_remover", "estrutura_gerar", "config_definir", "acoes_gerar", "combinacoes_listar",
    "analise_caso", "analise_combinacao", "analise_modal", "projeto_calcular", "resultados_consultar",
    "verificacao_listar", "verificacao_barra", "estabilidade_consultar", "ligacao_tipos", "ligacao_verificar",
    "perfis_listar", "perfil_consultar", "materiais_listar", "normas_implementadas", "relatorio_gerar",
    "modelo_importar_cype", "cype_comparar"}
SOMENTE_LEITURA = {
    "modelo_exemplos", "modelo_exportar_json", "modelo_resumo", "modelo_validar", "modelo_geometria",
    "elementos_listar", "combinacoes_listar", "analise_caso", "analise_combinacao", "analise_modal",
    "projeto_calcular", "resultados_consultar", "verificacao_listar", "verificacao_barra",
    "estabilidade_consultar", "ligacao_tipos", "ligacao_verificar", "perfis_listar", "perfil_consultar",
    "materiais_listar", "normas_implementadas", "cype_comparar"}
# ESPEC §8 + modelo_abrir e modelo_importar_json (substituem o modelo atual: decisão conservadora, ver relatório)
DESTRUTIVAS = {"modelo_novo", "modelo_abrir", "modelo_salvar", "modelo_abrir_exemplo", "modelo_importar_json",
               "elemento_remover", "relatorio_gerar", "modelo_importar_cype"}

EDIFICIO = {"tipo": "edificio", "material": {"concreto": 30},
            "secoes": {"pilar": {"forma": "retangular", "b": 0.25, "h": 0.5},
                       "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
            "vaos_x": [5, 5, 5], "vaos_y": [5, 5], "alturas": [3, 3, 3, 3], "apoio": "engaste"}
VENTO = {"V0": 35, "S1": 1.0, "categoria": "IV", "classe": "B", "S3": 1.0, "dimensoes": {"lx": 15, "ly": 10}}


def rodar(cena):
    """Executa `cena(cliente)` num cliente em processo e devolve o resultado."""
    async def _():
        async with Client(servidor_mcp.mcp) as c:
            return await cena(c)
    return asyncio.run(_())


class Base(unittest.TestCase):
    usadas: set

    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory()
        cls.pasta = Path(cls._tmp.name)
        servidor_mcp.definir_pasta(cls.pasta)

    @classmethod
    def tearDownClass(cls):
        servidor_mcp.definir_pasta(servidor_mcp.PASTA_PADRAO)
        cls._tmp.cleanup()

    def setUp(self):
        self.usadas = set()

    async def ok(self, c, nome, args=None):
        """Chama a ferramenta, exige sucesso e `structured_content`."""
        r = await c.call_tool(nome, args or {})
        self.usadas.add(nome)
        self.assertFalse(r.is_error, f"{nome}: {r.content[0].text if r.content else ''}")
        self.assertIsNotNone(r.structured_content, nome)
        return r.structured_content

    async def erro(self, c, nome, args=None):
        """Chama a ferramenta, exige is_error e devolve o texto da mensagem."""
        r = await c.call_tool(nome, args or {})
        self.assertTrue(r.is_error, f"{nome} deveria falhar")
        return r.content[0].text


class TestCatalogo(Base):
    def test_quarenta_ferramentas_exatas(self):
        async def cena(c):
            return (await c.list_tools()).tools
        tools = rodar(cena)
        self.assertEqual(len(tools), 42)
        self.assertEqual({t.name for t in tools}, QUARENTA)

    def test_descricao_anotacoes_e_parametros_documentados(self):
        async def cena(c):
            return (await c.list_tools()).tools
        for t in rodar(cena):
            self.assertGreater(len(t.description or ""), 40, t.name)
            a = t.annotations
            self.assertIsNotNone(a, t.name)
            self.assertEqual(a.read_only_hint, t.name in SOMENTE_LEITURA, t.name)
            if t.name not in SOMENTE_LEITURA:
                self.assertEqual(a.destructive_hint, t.name in DESTRUTIVAS, t.name)
            for p, esq in (t.input_schema.get("properties") or {}).items():
                self.assertTrue(esq.get("description"), f"{t.name}.{p} sem descrição")

    def test_recursos(self):
        async def cena(c):
            uris = {str(r.uri) for r in (await c.list_resources()).resources}
            await self.ok(c, "modelo_novo", {"nome": "Recurso"})
            doc = await c.read_resource("modelo://atual")
            return uris, json.loads(doc.contents[0].text)
        uris, modelo = rodar(cena)
        self.assertEqual(uris, {"modelo://atual", "calculo://ultimo"})
        self.assertEqual(modelo["nome"], "Recurso")
        self.assertEqual(modelo["versao_formato"], 1)


class TestErros(Base):
    def test_erros_orientam_a_correcao(self):
        async def cena(c):
            msgs = {}
            await self.ok(c, "modelo_novo")
            await self.ok(c, "no_adicionar", {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "rotula"})
            await self.ok(c, "no_adicionar", {"id": "n2", "x": 6, "y": 0, "z": 0})
            await self.ok(c, "secao_adicionar", {"perfil": "W310x38.7"})
            await self.ok(c, "material_adicionar", {"aco": "A-572 GR.50"})
            sb = {"secao": "W310x38.7", "material": "A-572 GR.50"}
            msgs["no_inexistente"] = await self.erro(c, "barra_adicionar", {"ni": "n1", "nj": "n7", **sb})
            await self.ok(c, "barra_adicionar", {"id": "b1", "ni": "n1", "nj": "n2", **sb})
            await self.ok(c, "caso_adicionar", {"nome": "G1", "tipo": "G"})
            await self.ok(c, "carga_adicionar", {"tipo": "distribuida", "caso": "G1", "barra": "b1", "q": [0, 0, -5]})
            msgs["carga_params"] = await self.erro(c, "carga_adicionar", {"tipo": "distribuida", "caso": "G1", "no": "n1", "q": [0, 0, -5]})
            msgs["hipostatico"] = await self.erro(c, "projeto_calcular")  # n2 livre em uy: mecanismo
            msgs["hipostatico_caso"] = await self.erro(c, "analise_caso", {"caso": "G1"})
            msgs["vento_VI"] = await self.erro(c, "config_definir", {"dados": {"vento": {**VENTO, "categoria": "VI"}}})
            msgs["dois_pontos"] = await self.erro(c, "modelo_salvar", {"caminho": "../fora.json"})
            msgs["absoluto"] = await self.erro(c, "modelo_salvar", {"caminho": "C:/Windows/fora.json"})
            msgs["arquivo"] = await self.erro(c, "modelo_abrir", {"caminho": "nao_existe.json"})
            msgs["cascata"] = await self.erro(c, "elemento_remover", {"tipo": "no", "id": "n2"})
            msgs["caso"] = await self.erro(c, "analise_caso", {"caso": "XX"})
            msgs["tipo_arg"] = await self.erro(c, "no_adicionar", {"x": "abc", "y": 0, "z": 0})
            msgs["exemplo"] = await self.erro(c, "modelo_abrir_exemplo", {"nome": "nao_existe"})
            msgs["perfil"] = await self.erro(c, "perfil_consultar", {"nome": "W999x1"})
            return msgs
        m = rodar(cena)
        self.assertIn("n7", m["no_inexistente"])
        self.assertIn("n1, n2", m["no_inexistente"])           # lista os ids válidos
        self.assertIn("barra", m["carga_params"])
        self.assertIn("mecanismo", m["hipostatico"])
        self.assertIn("modelo_validar", m["hipostatico"])
        self.assertIn("mecanismo", m["hipostatico_caso"])
        self.assertIn("vento.categoria", m["vento_VI"])
        self.assertIn("I, II, III, IV, V", m["vento_VI"])
        self.assertIn("..", m["dois_pontos"])
        self.assertIn("fora da pasta", m["absoluto"])
        self.assertIn("não encontrado", m["arquivo"])
        self.assertIn("cascata", m["cascata"])
        self.assertIn("XX", m["caso"])
        self.assertIn("G1", m["caso"])
        self.assertIn("number", m["tipo_arg"])
        self.assertIn("nao_existe", m["exemplo"])
        self.assertIn("W999x1", m["perfil"])
        self.assertFalse(list(self.pasta.parent.glob("fora.json")))

    def test_consulta_sem_calculo_orienta(self):
        async def cena(c):
            await self.ok(c, "modelo_novo")
            out = [await self.erro(c, "verificacao_listar"), await self.erro(c, "verificacao_barra", {"barra": "b1"}),
                   await self.erro(c, "estabilidade_consultar"),
                   await self.erro(c, "resultados_consultar", {"alvo": "reacoes"}),
                   await self.erro(c, "relatorio_gerar", {"caminho": "r.html"})]
            with self.assertRaises(Exception) as ctx:
                await c.read_resource("calculo://ultimo")
            return out, str(ctx.exception)
        msgs, recurso = rodar(cena)
        for t in msgs:
            self.assertIn("projeto_calcular", t)
        self.assertIn("projeto_calcular", recurso)

    def test_editar_o_modelo_descarta_o_calculo(self):
        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            await self.ok(c, "projeto_calcular")
            await self.ok(c, "verificacao_listar")
            await self.ok(c, "pavimento_adicionar", {"nome": "P9", "z": 9.0})
            return await self.erro(c, "verificacao_listar")
        self.assertIn("projeto_calcular", rodar(cena))


class TestExemplosPontaAPonta(Base):
    def test_valores_de_conferencia_do_leiame(self):
        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            q = await self.ok(c, "analise_caso", {"caso": "Q1"})
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "portico_aco"})
            v = await self.ok(c, "analise_caso", {"caso": "V1", "incluir": ["reacoes"]})
            q2 = await self.ok(c, "analise_caso", {"caso": "Q1", "incluir": ["reacoes"]})
            return q, v, q2
        q, v, q2 = rodar(cena)
        for no, r in q["reacoes"].items():   # qL/2 = 10·6/2
            self.assertAlmostEqual(r[2], 30.0, places=6, msg=no)
        self.assertAlmostEqual(max(abs(x) for x in q["esforcos"]["b1"]["My"]), 45.0, places=6)  # qL²/8
        self.assertEqual(set(v), {"nome", "pdelta", "reacoes"})
        self.assertAlmostEqual(sum(r[0] for r in v["reacoes"].values()), -10.0, places=6)
        self.assertAlmostEqual(sum(r[2] for r in q2["reacoes"].values()), 60.0, places=6)

    def test_quatro_exemplos_calculam_e_batem_com_projeto(self):
        async def cena(c):
            nomes = [e["nome"] for e in (await self.ok(c, "modelo_exemplos"))["itens"]]
            saida = {}
            for n in nomes:
                await self.ok(c, "modelo_abrir_exemplo", {"nome": n})
                r = await self.ok(c, "projeto_calcular")
                rel = await self.ok(c, "relatorio_gerar", {"caminho": f"ex/{n}.md", "formato": "md"})
                saida[n] = (r, rel)
            return saida
        saida = rodar(cena)
        self.assertEqual(set(saida), {"viga_biapoiada", "portico_aco", "galpao_aco", "edificio_concreto"})
        for n, (r, rel) in saida.items():
            direto = projeto.calcular(projeto.abrir_exemplo(n)).resumo()
            for k in ("n_barras", "n_reprovadas", "barra_critica"):
                self.assertEqual(r["resumo"][k], direto[k], f"{n}.{k}")
            self.assertAlmostEqual(r["resumo"]["taxa_max"], direto["taxa_max"], delta=TOL, msg=n)
            self.assertTrue(Path(rel["caminho"]).is_file(), n)
            self.assertGreater(rel["bytes"], 500)
            self.assertLessEqual(len(r["piores_barras"]), 10)


class TestFluxoF5(Base):
    """F2/F5 do PLANO §6.3 só por ferramentas: edifício 3×2 vãos × 4 pavimentos em C30, vento IV, calcular."""

    def test_edificio_com_vento(self):
        async def cena(c):
            await self.ok(c, "modelo_novo", {"nome": "F5"})
            g = await self.ok(c, "estrutura_gerar", {"dados": EDIFICIO})
            cfg = await self.ok(c, "config_definir", {"dados": {"peso_proprio": "G1", "vento": VENTO}})
            ac = await self.ok(c, "acoes_gerar")
            calc = await self.ok(c, "projeto_calcular")
            todas = await self.ok(c, "verificacao_listar", {"limite": 500})
            rep = await self.ok(c, "verificacao_listar", {"reprovadas": True})
            pag = await self.ok(c, "verificacao_listar", {"limite": 10, "deslocamento": 5})
            pilar = calc["piores_barras"][0]["barra"]
            det = await self.ok(c, "verificacao_barra", {"barra": pilar})
            est = await self.ok(c, "estabilidade_consultar")
            reac = await self.ok(c, "resultados_consultar", {"alvo": "reacoes", "ids": ["N0-0-0"]})
            pav = await self.ok(c, "resultados_consultar", {"alvo": "pavimentos"})
            html = await self.ok(c, "relatorio_gerar", {"caminho": "relatorios/f5.html"})
            js = await self.ok(c, "relatorio_gerar", {"caminho": "relatorios/f5.json", "formato": "json"})
            return g, cfg, ac, calc, todas, rep, pag, det, est, reac, pav, html, js

        g, cfg, ac, calc, todas, rep, pag, det, est, reac, pav, html, js = rodar(cena)
        self.assertEqual((g["nos_criados"], g["barras_criadas"], g["pavimentos_criados"]), (60, 116, 4))
        self.assertIn("V+X", ac["casos"])

        # os mesmos passos chamando projeto diretamente
        m = projeto.novo_modelo("F5")
        projeto.gerar_estrutura(m, EDIFICIO)
        projeto.configurar(m, {"peso_proprio": "G1", "vento": VENTO})
        d = projeto.calcular(m)
        dd = d.to_dict()

        self.assertEqual(calc["resumo"]["barra_critica"], dd["resumo"]["barra_critica"])
        self.assertAlmostEqual(calc["resumo"]["taxa_max"], dd["resumo"]["taxa_max"], delta=TOL)
        self.assertEqual(todas["total"], 116)
        for lm, ld in zip(todas["itens"], dd["verificacao"]):
            self.assertEqual((lm["barra"], lm["criterio"], lm["combinacao"]), (ld["barra"], ld["criterio"], ld["combinacao"]))
            self.assertAlmostEqual(lm["taxa"], ld["taxa"], delta=TOL, msg=lm["barra"])
        self.assertEqual(rep["total"], dd["resumo"]["n_reprovadas"])
        self.assertEqual((pag["retornados"], pag["deslocamento"], pag["proximo_deslocamento"]), (10, 5, 15))
        self.assertEqual(pag["itens"][0]["barra"], dd["verificacao"][5]["barra"])
        for dr in ("X", "Y"):  # γz por direção
            self.assertAlmostEqual(est["gama_z"][dr]["valor"], dd["estabilidade"]["gama_z"][dr]["valor"], delta=TOL)
            self.assertEqual(calc["estabilidade"]["gama_z"][dr]["classificacao"], est["gama_z"][dr]["classificacao"])
        self.assertEqual(det["barra"], calc["piores_barras"][0]["barra"])
        self.assertTrue(det["por_combinacao"])
        direto = projeto.resultados(d, "reacoes", ["N0-0-0"])["N0-0-0"]
        for k in ("max", "min"):
            for a, b in zip(reac["dados"]["N0-0-0"][k], direto[k]):
                self.assertAlmostEqual(a, b, delta=TOL)
        self.assertEqual(set(pav["dados"]), {"P1", "P2", "P3", "P4"})
        self.assertEqual(Path(html["caminho"]).parent.name, "relatorios")
        self.assertTrue(Path(html["caminho"]).read_text(encoding="utf-8").lstrip().lower().startswith("<!doctype html"))
        self.assertAlmostEqual(json.loads(Path(js["caminho"]).read_text(encoding="utf-8"))["resumo"]["taxa_max"],
                               dd["resumo"]["taxa_max"], delta=TOL)
        # resposta enxuta
        self.assertLess(len(json.dumps(calc)), 30000)


class TestTodasAsFerramentas(Base):
    """Pórtico de 2 vãos montado só por ferramentas (F1), exercitando as 40."""

    def test_portico_do_zero(self):
        async def cena(c):
            ok = lambda n, a=None: self.ok(c, n, a)  # noqa: E731
            await ok("modelo_novo", {"nome": "Pórtico"})
            await ok("secao_adicionar", {"perfil": "W310x38.7"})
            await ok("secao_adicionar", {"perfil": "W360x51"})
            await ok("material_adicionar", {"aco": "A-572 GR.50"})
            await ok("no_adicionar", {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "engaste"})
            await ok("lote_adicionar", {"itens": [
                {"tipo": "no", "dados": {"id": "n2", "x": 6, "y": 0, "z": 0, "apoio": "engaste"}},
                {"tipo": "no", "dados": {"id": "n3", "x": 12, "y": 0, "z": 0, "apoio": "engaste"}},
                {"tipo": "no", "dados": {"id": "n4", "x": 0, "y": 0, "z": 4}},
                {"tipo": "no", "dados": {"id": "n5", "x": 6, "y": 0, "z": 4}},
                {"tipo": "no", "dados": {"id": "n6", "x": 12, "y": 0, "z": 4}}]})
            col = {"secao": "W310x38.7", "material": "A-572 GR.50"}
            vig = {"secao": "W360x51", "material": "A-572 GR.50"}
            for i, (a, b) in enumerate([("n1", "n4"), ("n2", "n5"), ("n3", "n6")], 1):
                await ok("barra_adicionar", {"id": f"b{i}", "ni": a, "nj": b, **col})
            await ok("barra_adicionar", {"id": "b4", "ni": "n4", "nj": "n5", **vig})
            await ok("barra_adicionar", {"id": "b5", "ni": "n5", "nj": "n6", "params": {"Lb": 3.0}, **vig})
            await ok("caso_adicionar", {"nome": "G1", "tipo": "G"})
            await ok("caso_adicionar", {"nome": "Q1", "tipo": "Q", "subtipo": "escritorio"})
            await ok("caso_adicionar", {"nome": "V1", "tipo": "V"})
            for b in ("b4", "b5"):
                await ok("carga_adicionar", {"tipo": "distribuida", "caso": "Q1", "barra": b, "q": [0, 0, -10]})
            await ok("carga_adicionar", {"tipo": "nodal", "caso": "V1", "no": "n4", "F": [10, 0, 0, 0, 0, 0]})
            await ok("carga_adicionar", {"tipo": "pontual", "caso": "G1", "barra": "b4", "a": 2.0, "F": [0, 0, -20]})
            await ok("pavimento_adicionar", {"nome": "P1", "z": 4.0, "diafragma": False})
            await ok("config_definir", {"dados": {"peso_proprio": "G1", "tipo_uso": "comercial"}})

            r = {}
            r["valid"] = await ok("modelo_validar")
            r["resumo"] = await ok("modelo_resumo")
            r["geo"] = await ok("modelo_geometria")
            r["barras"] = await ok("elementos_listar", {"tipo": "barra", "limite": 2})
            r["cargas_q"] = await ok("elementos_listar", {"tipo": "carga_distribuida", "filtro": {"caso": "Q1"}})
            r["comb"] = await ok("combinacoes_listar")
            r["q1"] = await ok("analise_caso", {"caso": "Q1", "incluir": ["reacoes"]})
            r["v1"] = await ok("analise_caso", {"caso": "V1", "incluir": ["reacoes"]})
            nome_comb = r["comb"]["itens"][0]["nome"]
            r["ac"] = await ok("analise_combinacao", {"nome": nome_comb, "incluir": ["reacoes", "pavimentos"]})
            r["modal"] = await ok("analise_modal", {"n_modos": 2})
            r["ed"] = await ok("elemento_editar", {"tipo": "barra", "id": "b4", "dados": {"params": {"Lb": 3.0}}})
            r["no_ed"] = await ok("elemento_editar", {"tipo": "no", "id": "n6", "dados": {"z": 4.5}})
            await ok("elemento_editar", {"tipo": "no", "id": "n6", "dados": {"z": 4.0}})
            r["rem"] = await ok("elemento_remover", {"tipo": "carga_pontual", "id": "cp1"})
            await ok("acoes_gerar")
            r["calc"] = await ok("projeto_calcular", {"n_pontos": 13})
            r["calc_barras"] = await ok("projeto_calcular", {"barras": ["b4"], "pdelta": True})
            r["calc"] = await ok("projeto_calcular")
            r["res_env"] = await ok("resultados_consultar", {"alvo": "esforcos", "ids": ["b4"]})
            r["res_comb"] = await ok("resultados_consultar", {"alvo": "deslocamentos", "ids": ["n5"], "combinacao": nome_comb})
            r["ver"] = await ok("verificacao_listar", {"tipo": "aco", "taxa_min": 0.0})
            r["ver_b"] = await ok("verificacao_barra", {"barra": "b4"})
            r["est"] = await ok("estabilidade_consultar")
            r["rel"] = await ok("relatorio_gerar", {"caminho": "portico.html"})
            await ok("modelo_salvar", {"caminho": "obra/portico.json"})
            exp = await ok("modelo_exportar_json")
            await ok("modelo_novo")
            r["aberto"] = await ok("modelo_abrir", {"caminho": "obra/portico.json"})
            await ok("modelo_novo")
            r["importado"] = await ok("modelo_importar_json", {"modelo": exp["modelo"]})
            r["exemplos"] = await ok("modelo_exemplos")
            await ok("modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            tipos = await ok("ligacao_tipos")
            r["lig"] = await ok("ligacao_verificar", {"tipo": "solda_filete", "dados": {"b": 0.006, "L": 0.2, "t_base": 0.0125},
                                                      "esforcos": {"F": 100.0}})
            r["lig_tipos"] = [t["tipo"] for t in tipos["itens"]]
            r["perfis"] = await ok("perfis_listar", {"filtro": "W310", "familia": "W", "limite": 3})
            r["perfil"] = await ok("perfil_consultar", {"nome": "W310x38.7"})
            r["mat"] = await ok("materiais_listar")
            r["normas"] = await ok("normas_implementadas")
            ed3_bytes = (SOFTWARE / "testes" / "dados_cype" / "PIPERACK TESTE 4.ed3").read_bytes()
            (self.pasta / "obra_cype.ed3").write_bytes(ed3_bytes)
            r["cype_comp"] = await ok("cype_comparar", {"caminho": "obra_cype.ed3"})
            r["cype_imp"] = await ok("modelo_importar_cype", {"caminho": "obra_cype.ed3"})
            await ok("modelo_novo")
            r["gerar"] = await ok("estrutura_gerar", {"dados": {
                "tipo": "portico_plano", "material": {"aco": "A-572 GR.50"},
                "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                "vaos_x": [6, 6], "alturas": [4], "apoio": "engaste"}})
            r["exp"] = exp
            return r

        r = rodar(cena)
        self.assertEqual(self.usadas, QUARENTA)
        self.assertTrue(r["valid"]["ok"], r["valid"])
        self.assertEqual((r["resumo"]["n_nos"], r["resumo"]["n_barras"]), (6, 5))
        self.assertEqual(r["barras"]["total"], 5)
        self.assertEqual(r["barras"]["retornados"], 2)
        self.assertEqual(r["cargas_q"]["total"], 2)
        self.assertEqual(len(r["geo"]["barras"]), 5)
        self.assertAlmostEqual(r["geo"]["barras"][0]["L"], 4.0, places=9)
        # equilíbrio (independente do código): ΣRz = 2 vãos · 6 m · 10 kN/m; ΣRx = −10 kN
        self.assertAlmostEqual(sum(v[2] for v in r["q1"]["reacoes"].values()), 120.0, places=6)
        self.assertAlmostEqual(sum(v[0] for v in r["v1"]["reacoes"].values()), -10.0, places=6)
        self.assertEqual(len(r["modal"]["frequencias_hz"]), 2)
        self.assertGreater(r["modal"]["frequencias_hz"][0], 0.0)
        self.assertEqual(r["ed"]["params"]["Lb"], 3.0)
        self.assertEqual(r["no_ed"]["z"], 4.5)
        self.assertEqual(r["rem"]["removidos"], [{"tipo": "carga_pontual", "id": "cp1"}])
        self.assertEqual(r["calc"]["resumo"]["n_barras"], 5)
        self.assertEqual(r["calc_barras"]["resumo"]["n_barras"], 1)
        self.assertEqual(r["ver"]["total"], 5)
        self.assertEqual(r["ver_b"]["barra"], "b4")
        self.assertIn("b4", r["res_env"]["dados"])
        self.assertEqual(len(r["res_comb"]["dados"]["n5"]), 6)
        self.assertTrue(Path(r["rel"]["caminho"]).is_file())
        self.assertEqual(r["aberto"]["n_barras"], 5)
        self.assertEqual(r["importado"]["n_barras"], 5)
        self.assertEqual(r["exemplos"]["total"], 4)
        self.assertLessEqual({"chapa_simples", "ligacao_tracao", "parafuso", "solda_filete"}, set(r["lig_tipos"]))
        direto = projeto.verificar_ligacao("solda_filete", {"b": 0.006, "L": 0.2, "t_base": 0.0125}, {"F": 100.0})
        self.assertAlmostEqual(r["lig"]["taxa_max"], direto["taxa_max"], delta=TOL)
        self.assertGreater(r["perfis"]["total"], 3)
        self.assertEqual(r["perfis"]["retornados"], 3)
        self.assertEqual(r["perfil"]["nome"], "W310x38.7")
        self.assertAlmostEqual(r["perfil"]["A_cm2"], 49.72, places=1)
        self.assertGreater(len(r["mat"]["aco"]), 3)
        self.assertGreater(r["normas"]["total"], 5)
        self.assertEqual((r["gerar"]["nos_criados"], r["gerar"]["barras_criadas"]), (6, 5))


class TestCype(Base):
    """Testes das ferramentas de obras CYPE 3D (.ed3): importar e comparar."""

    def test_cype_importar_comparar_e_erros(self):
        ed3_pequeno = SOFTWARE / "testes" / "dados_cype" / "PIPERACK TESTE 4.ed3"
        (self.pasta / "t4.ed3").write_bytes(ed3_pequeno.read_bytes())

        async def cena(c):
            # importar pequeno .ed3: confere nós e barras > 0 e avisos lista
            imp = await self.ok(c, "modelo_importar_cype", {"caminho": "t4.ed3"})
            # cype_comparar no mesmo arquivo devolve a estrutura esperada
            comp = await self.ok(c, "cype_comparar", {"caminho": "t4.ed3"})
            # caminho fora das pastas permitidas e extensão errada -> erro (is_error)
            e_ext = await self.erro(c, "modelo_importar_cype", {"caminho": "t4.txt"})
            e_fora = await self.erro(c, "modelo_importar_cype", {"caminho": "../fora.ed3"})
            e_abs = await self.erro(c, "modelo_importar_cype", {"caminho": "C:/Windows/fora.ed3"})
            e_comp_ext = await self.erro(c, "cype_comparar", {"caminho": "t4.txt"})
            e_comp_fora = await self.erro(c, "cype_comparar", {"caminho": "../fora.ed3"})
            return imp, comp, e_ext, e_fora, e_abs, e_comp_ext, e_comp_fora

        imp, comp, e_ext, e_fora, e_abs, e_comp_ext, e_comp_fora = rodar(cena)
        self.assertGreater(imp["n_nos"], 0)
        self.assertGreater(imp["n_barras"], 0)
        self.assertIsInstance(imp["avisos"], list)

        for k in ("arquivo", "valido", "hipoteses", "tabela", "taxas", "total", "avisos"):
            self.assertIn(k, comp)
        self.assertIsInstance(comp["tabela"], list)
        self.assertIsInstance(comp["taxas"], list)
        self.assertGreater(comp["total"], 0)
        self.assertGreater(len(comp["tabela"]), 0)
        for campo in ("hipotese", "grandeza", "unidade", "cype", "nosso", "dif_pct"):
            self.assertIn(campo, comp["tabela"][0])
        for campo in ("barra", "cype", "nosso", "dif_pct", "criterio", "ok"):
            self.assertIn(campo, comp["taxas"][0])

        self.assertIn(".ed3", e_ext)
        self.assertIn("..", e_fora)
        self.assertIn("fora da pasta", e_abs)
        self.assertIn(".ed3", e_comp_ext)
        self.assertIn("..", e_comp_fora)


class TestStdio(unittest.TestCase):
    """Servidor como subprocesso (o que o Claude Code faz), com a raiz do projeto (caminho com espaços) como cwd."""

    def _rodar(self, params):
        async def _():
            async with Client(params) as c:
                tools = (await c.list_tools()).tools
                r = await c.call_tool("modelo_novo", {"nome": "stdio"})
                lib = await c.call_tool("perfil_consultar", {"nome": "W310x38.7"})
                return tools, r, lib
        return asyncio.run(_())

    def _conferir(self, tools, r, lib):
        self.assertEqual({t.name for t in tools}, QUARENTA)
        self.assertFalse(r.is_error)
        self.assertEqual(r.structured_content["nome"], "stdio")
        self.assertFalse(lib.is_error)
        self.assertAlmostEqual(lib.structured_content["A_cm2"], 49.72, places=1)

    def test_stdio_pythonpath_absoluto(self):
        with tempfile.TemporaryDirectory() as pasta:
            p = StdioServerParameters(
                command=sys.executable, args=["-m", "prossen_estrutural.servidor_mcp", "--pasta", pasta],
                env={**os.environ, "PYTHONPATH": str(SOFTWARE)}, cwd=str(RAIZ))
            self._conferir(*self._rodar(p))

    def test_stdio_com_o_mcp_json_da_raiz(self):
        """Lê `codigo/.mcp.json` (servidor "estructto", PYTHONPATH relativo, cwd = codigo); troca só o executável."""
        cfg = json.loads((RAIZ / ".mcp.json").read_text(encoding="utf-8"))["mcpServers"]["estructto"]
        self.assertEqual(cfg["args"], ["-m", "prossen_estrutural.servidor_mcp"])
        with tempfile.TemporaryDirectory() as pasta:
            p = StdioServerParameters(command=sys.executable, args=[*cfg["args"], "--pasta", pasta],
                                      env={**os.environ, **cfg.get("env", {})}, cwd=str(RAIZ))
            self._conferir(*self._rodar(p))


if __name__ == "__main__":
    unittest.main()
