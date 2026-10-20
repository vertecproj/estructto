"""E2E do servidor MCP com cliente real (`mcp.Client`), em processo e via stdio (como o `.mcp.json` da raiz).
Ficha 023, casos 2 e 5. Fluxo F5 do PLANO §6.3 (o F2 feito só por ferramentas) e fluxo manual do F1.

Valores de referência independentes do código:
- peso próprio do edifício gerado (sem cargas de laje): pilares 48·3 m·(0,25·0,50 m²)·25 kN/m³ = 450 kN e vigas
  340 m·(0,20·0,50 m²)·25 kN/m³ = 850 kN → ΣRz(G1) = 1 300 kN (conferência de `exemplos/LEIAME.md`).
- viga biapoiada L = 6 m, q = 10 kN/m: reações qL/2 = 30 kN, M_máx = qL²/8 = 45 kN·m.
- balanço de 3 m com P = (5, 0, −50) kN no topo: Rx = −5, Rz = 50, My = −15 kN·m (equilíbrio).
- treliça triangular (6 m × 4 m, P = 100 kN no vértice): barras inclinadas −62,5 kN (compressão), banzo
  inferior +37,5 kN (equilíbrio dos nós; barras do tipo `trelica` só têm axial).
- balanços de 2 m com P = 10 kN: δ = PL³/(3EI) com I forte (rot = 0) e I fraco (rot = π/2).
- parafuso A325 M20 (NBR 8800 6.3.3.2, rosca no plano de corte): F_v,Rd = 0,4·A_b·f_ub/γa2 = 0,4·314,16 mm²·
  825 MPa/1,35 = 76,79 kN.
"""
import asyncio
import json
import math
import shutil
import tempfile
import unittest
from pathlib import Path

from mcp import Client

from .utilitarios import (EDIFICIO_F2, EXEMPLOS, MODELOS, NOMES_EXEMPLOS, QUARENTA, VENTO_F2, TOL, diferencas,
                          json_estrito, lento, nao_finitos, parametros_stdio, referencia, referencia_f2,
                          rodar_cli)
from prossen_estrutural import projeto, servidor_mcp  # noqa: E402  (software/ já está no sys.path)


def rodar(cena, pasta, stdio=False, mcp_json=False):
    """Executa `cena(cliente)` num cliente MCP: em processo ou como subprocesso stdio."""
    async def _():
        if stdio:
            alvo = parametros_stdio(pasta, usar_mcp_json=mcp_json)
        else:
            servidor_mcp.definir_pasta(pasta)
            alvo = servidor_mcp.mcp
        async with Client(alvo) as c:
            return await cena(c)
    try:
        return asyncio.run(_())
    finally:
        if not stdio:
            servidor_mcp.definir_pasta(servidor_mcp.PASTA_PADRAO)


class BaseMCP(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory(prefix="prossen_mcp_")
        self.pasta = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)

    async def ok(self, c, nome, args=None):
        r = await c.call_tool(nome, args or {})
        self.assertFalse(r.is_error, f"{nome} {args}: {r.content[0].text if r.content else ''}")
        self.assertIsNotNone(r.structured_content, nome)
        self.assertEqual(nao_finitos(r.structured_content), [], nome)
        return r.structured_content

    async def erro(self, c, nome, args=None):
        r = await c.call_tool(nome, args or {})
        self.assertTrue(r.is_error, f"{nome} {args} deveria falhar")
        texto = r.content[0].text
        self.assertNotIn("Traceback", texto)
        return texto


# ================================================================= F5 (o F2 só por ferramentas)
async def fluxo_f5(self, c):
    """Percorre o fluxo F5 completo e devolve tudo o que as asserções precisam."""
    ok = lambda n, a=None: self.ok(c, n, a)  # noqa: E731
    r = {}
    r["tools"] = (await c.list_tools()).tools
    r["exemplos"] = await ok("modelo_exemplos")
    await ok("modelo_novo", {"nome": "F5"})
    r["gerar"] = await ok("estrutura_gerar", {"dados": EDIFICIO_F2})
    r["cfg"] = await ok("config_definir", {"dados": {"peso_proprio": "G1", "vento": VENTO_F2}})
    r["validar"] = await ok("modelo_validar")
    r["acoes"] = await ok("acoes_gerar")
    r["comb"] = await ok("combinacoes_listar")
    r["resumo_modelo"] = await ok("modelo_resumo")
    r["g1"] = await ok("analise_caso", {"caso": "G1", "incluir": ["reacoes"]})
    r["vx"] = await ok("analise_caso", {"caso": "V+X", "incluir": ["reacoes"]})
    r["calc"] = await ok("projeto_calcular")
    r["todas"] = await ok("verificacao_listar", {"limite": 500})
    r["reprov"] = await ok("verificacao_listar", {"reprovadas": True})
    r["concreto"] = await ok("verificacao_listar", {"tipo": "concreto", "limite": 5})
    pilar = r["calc"]["piores_barras"][0]["barra"]
    r["det"] = await ok("verificacao_barra", {"barra": pilar})
    r["est"] = await ok("estabilidade_consultar")
    r["reacoes"] = await ok("resultados_consultar", {"alvo": "reacoes", "limite": 100})
    r["pav"] = await ok("resultados_consultar", {"alvo": "pavimentos"})
    r["html"] = await ok("relatorio_gerar", {"caminho": "saida/f5.html"})
    r["md"] = await ok("relatorio_gerar", {"caminho": "saida/f5.md", "formato": "md"})
    r["json"] = await ok("relatorio_gerar", {"caminho": "saida/f5.json", "formato": "json"})
    r["salvo"] = await ok("modelo_salvar", {"caminho": "f5_modelo.json"})
    r["recurso_modelo"] = json.loads((await c.read_resource("modelo://atual")).contents[0].text)
    r["recurso_calculo"] = json.loads((await c.read_resource("calculo://ultimo")).contents[0].text)
    return r


def conferir_f5(self, r):
    ref_m, ref_c, ref = referencia_f2()
    # catálogo
    self.assertEqual(len(r["tools"]), 42)
    self.assertEqual({t.name for t in r["tools"]}, QUARENTA)
    self.assertEqual({e["nome"] for e in r["exemplos"]["itens"]}, set(NOMES_EXEMPLOS))
    # geração e ações
    self.assertEqual((r["gerar"]["nos_criados"], r["gerar"]["barras_criadas"], r["gerar"]["pavimentos_criados"]),
                     (60, 116, 4))
    self.assertTrue(r["validar"]["ok"])
    self.assertEqual({"V+X", "V-X", "V+Y", "V-Y"} & set(r["acoes"]["casos"]), {"V+X", "V-X", "V+Y", "V-Y"})
    self.assertEqual(r["comb"]["total"], len(ref["combinacoes"]))
    self.assertEqual([x["nome"] for x in r["comb"]["itens"]], [x["nome"] for x in ref["combinacoes"]])
    # equilíbrio independente: ΣRz(G1) = peso próprio = 450 + 850 kN; ΣRx(V+X) = −ΣF(V+X)
    self.assertAlmostEqual(sum(v[2] for v in r["g1"]["reacoes"].values()), 1300.0, delta=1e-6)
    forca_x = sum(f["F_kN"] for f in r["acoes"]["forcas"]["V+X"])
    self.assertGreater(forca_x, 0)
    self.assertAlmostEqual(sum(v[0] for v in r["vx"]["reacoes"].values()), -forca_x, delta=1e-6 * forca_x)
    # cálculo = biblioteca (1e-9)
    self.assertEqual(r["calc"]["resumo"]["barra_critica"], ref["resumo"]["barra_critica"])
    self.assertEqual(r["calc"]["resumo"]["n_barras"], 116)
    self.assertEqual(diferencas(r["calc"]["resumo"], ref["resumo"], ignorar=("tempo_s",)), [])
    chave = lambda ns: [(n["norma"], n["edicao"], n["modulo"]) for n in ns]  # noqa: E731  (MCP omite as limitações)
    self.assertEqual(chave(r["calc"]["normas"]), chave(ref["normas"]))
    self.assertEqual(r["todas"]["total"], 116)
    itens = r["todas"]["itens"]
    self.assertEqual([x["barra"] for x in itens], [x["barra"] for x in ref["verificacao"]])
    for lm, lr in zip(itens, ref["verificacao"]):
        self.assertEqual((lm["criterio"], lm["item"], lm["combinacao"], lm["ok"]),
                         (lr["criterio"], lr["item"], lr["combinacao"], lr["ok"]), lm["barra"])
        self.assertAlmostEqual(lm["taxa"], lr["taxa"], delta=TOL * max(1.0, lr["taxa"]), msg=lm["barra"])
    self.assertEqual(r["reprov"]["total"], ref["resumo"]["n_reprovadas"])
    self.assertTrue(all(x["tipo"] == "concreto" for x in r["concreto"]["itens"]))
    self.assertTrue(r["concreto"]["itens"][0]["armadura"], "concreto devolve armadura")
    # detalhe da barra crítica
    self.assertEqual(r["det"]["barra"], ref["resumo"]["barra_critica"])
    self.assertTrue(r["det"]["por_combinacao"] and r["det"]["detalhes"])
    # estabilidade γz por direção
    for d in ("X", "Y"):
        self.assertEqual(diferencas(r["est"]["gama_z"][d], ref["estabilidade"]["gama_z"][d]), [], d)
        self.assertGreaterEqual(r["est"]["gama_z"][d]["valor"], 1.0)
    # reações da envoltória
    self.assertEqual(set(r["reacoes"]["dados"]), set(ref["reacoes_envoltoria"]))
    for no, v in r["reacoes"]["dados"].items():
        for k in ("max", "min", "comb_max", "comb_min"):
            self.assertEqual(diferencas(v[k], ref["reacoes_envoltoria"][no][k]), [], f"{no}.{k}")
    self.assertEqual(set(r["pav"]["dados"]), {"P1", "P2", "P3", "P4"})
    # relatórios
    for fmt in ("html", "md", "json"):
        arq = Path(r[fmt]["caminho"])
        self.assertTrue(arq.is_file(), fmt)
        self.assertEqual(arq.parent.name, "saida")
        self.assertGreater(r[fmt]["bytes"], 1000)
    html = Path(r["html"]["caminho"]).read_text(encoding="utf-8")
    self.assertTrue(html.lstrip().lower().startswith("<!doctype html"))
    self.assertIn("Ferramenta de apoio", html)
    self.assertIn("Ferramenta de apoio", Path(r["md"]["caminho"]).read_text(encoding="utf-8"))
    rel = json_estrito(Path(r["json"]["caminho"]).read_text(encoding="utf-8"))
    self.assertEqual(diferencas(rel, ref, ignorar=("data", "tempo_s")), [], "relatório JSON do MCP = biblioteca")
    # recursos MCP
    self.assertEqual(r["recurso_modelo"]["nome"], "F5")
    self.assertEqual(diferencas(r["recurso_calculo"], ref, ignorar=("data", "tempo_s", "piores_barras", "verificacao")), [],
                     "recurso calculo://ultimo")
    # o modelo salvo, calculado pela CLI, dá os mesmos números (canal CLI ↔ MCP)
    with tempfile.TemporaryDirectory() as t:
        p = rodar_cli(Path(r["salvo"]["caminho"]), "-o", t, "--formato", "json")
        self.assertEqual(p.returncode, 0, p.stderr)
        cli = json_estrito((Path(t) / "f5_modelo.json").read_text(encoding="utf-8"))
    self.assertEqual(diferencas(cli, rel, ignorar=("data", "tempo_s")), [], "CLI sobre o modelo salvo pelo MCP")


class TestFluxoF5(BaseMCP):
    @lento
    def test_f5_em_processo(self):
        conferir_f5(self, rodar(lambda c: fluxo_f5(self, c), self.pasta))

    @lento
    def test_f5_via_stdio_com_o_mcp_json(self):
        """Mesmo fluxo com o servidor como subprocesso, configurado como o `.mcp.json` da raiz (cwd = raiz)."""
        conferir_f5(self, rodar(lambda c: fluxo_f5(self, c), self.pasta, stdio=True, mcp_json=True))


# ================================================================= F1 manual: nó a nó
class TestFluxoManual(BaseMCP):
    def test_viga_do_zero_confere_com_a_solucao_fechada_e_com_o_exemplo(self):
        async def cena(c):
            ok = lambda n, a=None: self.ok(c, n, a)  # noqa: E731
            await ok("modelo_novo", {"nome": "Viga F1"})
            await ok("secao_adicionar", {"perfil": "W360x51"})
            await ok("material_adicionar", {"aco": "A-572 GR.50"})
            await ok("no_adicionar", {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True, True, True, True, False, False]})
            await ok("no_adicionar", {"id": "n2", "x": 6, "y": 0, "z": 0, "apoio": [False, True, True, False, False, False]})
            await ok("barra_adicionar", {"id": "b1", "ni": "n1", "nj": "n2", "secao": "W360x51", "material": "A-572 GR.50",
                                         "params": {"papel": "viga", "elemento_desloc": "viga_piso"}})
            await ok("caso_adicionar", {"nome": "G1", "tipo": "G"})
            await ok("caso_adicionar", {"nome": "Q1", "tipo": "Q"})
            await ok("carga_adicionar", {"tipo": "distribuida", "caso": "G1", "barra": "b1", "q": [0, 0, -5.0]})
            await ok("carga_adicionar", {"tipo": "distribuida", "caso": "Q1", "barra": "b1", "q": [0, 0, -10.0]})
            await ok("config_definir", {"dados": {"peso_proprio": "G1"}})
            val = await ok("modelo_validar")
            comb = await ok("combinacoes_listar")
            q1 = await ok("analise_caso", {"caso": "Q1"})
            calc = await ok("projeto_calcular")
            lista = await ok("verificacao_listar")
            rel = await ok("relatorio_gerar", {"caminho": "viga.md", "formato": "md"})
            return val, comb, q1, calc, lista, rel
        val, comb, q1, calc, lista, rel = rodar(cena, self.pasta)
        self.assertTrue(val["ok"])
        _, ref = referencia("viga_biapoiada")                           # o mesmo modelo montado à mão = exemplo
        self.assertEqual([c["nome"] for c in comb["itens"]], [c["nome"] for c in ref.combinacoes])
        for r in q1["reacoes"].values():
            self.assertAlmostEqual(r[2], 30.0, places=6)               # qL/2
        self.assertAlmostEqual(max(abs(x) for x in q1["esforcos"]["b1"]["My"]), 45.0, places=6)   # qL²/8
        self.assertAlmostEqual(calc["resumo"]["taxa_max"], ref.resumo()["taxa_max"], delta=TOL)
        self.assertEqual(lista["itens"][0]["combinacao"], ref.verificacao[0]["combinacao"])
        self.assertIn("Ferramenta de apoio", Path(rel["caminho"]).read_text(encoding="utf-8"))

    def test_edicao_de_carga_remocao_e_descarte_do_calculo(self):
        async def cena(c):
            ok = lambda n, a=None: self.ok(c, n, a)  # noqa: E731
            await ok("modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            a = (await ok("projeto_calcular"))["resumo"]["taxa_max"]
            cd = (await ok("elementos_listar", {"tipo": "carga_distribuida", "filtro": {"caso": "Q1"}}))["itens"][0]
            await ok("elemento_editar", {"tipo": "carga_distribuida", "id": cd["id"], "dados": {"q": [0, 0, -20.0]}})
            sem_calculo = await self.erro(c, "verificacao_listar")      # editar descarta o cálculo
            b = (await ok("projeto_calcular"))["resumo"]["taxa_max"]
            rem = await ok("elemento_remover", {"tipo": "carga_distribuida", "id": cd["id"]})
            return a, b, sem_calculo, rem
        a, b, sem_calculo, rem = rodar(cena, self.pasta)
        self.assertIn("projeto_calcular", sem_calculo)
        self.assertGreater(b, a)                                         # mais carga, mais taxa
        self.assertEqual([x["tipo"] for x in rem["removidos"]], ["carga_distribuida"])

    def test_importar_e_exportar_json_idem(self):
        d = json.loads((EXEMPLOS / "portico_aco.json").read_text(encoding="utf-8"))

        async def cena(c):
            await self.ok(c, "modelo_importar_json", {"modelo": d})
            exp = await self.ok(c, "modelo_exportar_json")
            calc = await self.ok(c, "projeto_calcular")
            return exp, calc
        exp, calc = rodar(cena, self.pasta)
        self.assertEqual(diferencas(exp["modelo"], projeto.para_dict(projeto.de_dict(d))), [])
        _, ref = referencia("portico_aco")
        self.assertAlmostEqual(calc["resumo"]["taxa_max"], ref.resumo()["taxa_max"], delta=TOL)

    def test_ligacao_f4_chapa_simples(self):
        """F4: chapa simples V = 120 kN, 3 parafusos M20 A325; F_v,Rd conferido pela fórmula da norma."""
        async def cena(c):
            tipos = await self.ok(c, "ligacao_tipos")
            r = await self.ok(c, "ligacao_verificar", {
                "tipo": "chapa_simples", "esforcos": {"V": 120},
                "dados": {"h": 0.24, "t": 0.0095, "d_mm": 20, "tipo_parafuso": "A325", "n": 3, "s": 0.075,
                          "tw": 0.0072, "e": 0.06}})
            return tipos, r
        tipos, r = rodar(cena, self.pasta)
        self.assertLessEqual({"parafuso", "solda_filete", "chapa_simples", "ligacao_tracao"}, {t["tipo"] for t in tipos["itens"]})
        fv_rd = 0.4 * (math.pi * 20 ** 2 / 4) * 825 / 1.35 / 1000.0     # kN (NBR 8800 6.3.3.2)
        cis = next(v for v in r["verificacoes"] if v["nome"].startswith("parafusos: cisalhamento"))
        self.assertAlmostEqual(cis["resistente"], fv_rd, delta=1e-3)
        self.assertTrue(r["ok"] and 0 < r["taxa_max"] <= 1.0)
        for v in r["verificacoes"]:
            self.assertTrue(v["item"] and v["unidade"], v)
        self.assertTrue(r["avisos"], "a ligação diz o que não cobre")


# ================================================================= erros que orientam a correção
class TestErros(BaseMCP):
    def _com_modelos(self, *arqs):
        for a in arqs:
            shutil.copy(MODELOS / a, self.pasta / a)

    def test_no_e_barra_com_no_inexistente(self):
        async def cena(c):
            await self.ok(c, "modelo_novo")
            await self.ok(c, "secao_adicionar", {"perfil": "W310x38.7"})
            await self.ok(c, "material_adicionar", {"aco": "A-572 GR.50"})
            await self.ok(c, "no_adicionar", {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": "engaste"})
            await self.ok(c, "no_adicionar", {"id": "n2", "x": 3, "y": 0, "z": 0})
            await self.ok(c, "caso_adicionar", {"nome": "Q1", "tipo": "Q"})
            sb = {"secao": "W310x38.7", "material": "A-572 GR.50"}
            return {
                "barra_no": await self.erro(c, "barra_adicionar", {"ni": "n1", "nj": "n7", **sb}),
                "barra_secao": await self.erro(c, "barra_adicionar", {"ni": "n1", "nj": "n2", "secao": "XX", "material": "A-572 GR.50"}),
                "carga_no": await self.erro(c, "carga_adicionar", {"tipo": "nodal", "caso": "Q1", "no": "n9", "F": [0, 0, -1, 0, 0, 0]}),
                "carga_caso": await self.erro(c, "carga_adicionar", {"tipo": "nodal", "caso": "Z9", "no": "n2", "F": [0, 0, -1, 0, 0, 0]}),
                "lote": await self.erro(c, "lote_adicionar", {"itens": [
                    {"tipo": "no", "dados": {"id": "n3", "x": 1, "y": 0, "z": 0}},
                    {"tipo": "barra", "dados": {"ni": "n3", "nj": "n8", **sb}}]}),
                "depois_do_lote": await self.ok(c, "elementos_listar", {"tipo": "no"}),
            }
        m = rodar(cena, self.pasta)
        for chave in ("barra_no", "carga_no", "lote"):
            self.assertRegex(m[chave], r"n[789]", chave)
        for chave in ("barra_no", "carga_no"):
            self.assertIn("n1, n2", m[chave], f"{chave}: deve listar os ids válidos")
        self.assertIn("XX", m["barra_secao"])
        self.assertIn("W310x38.7", m["barra_secao"])
        self.assertIn("Z9", m["carga_caso"])
        self.assertIn("itens[1]", m["lote"])                                # índice do item que falhou
        self.assertEqual(m["depois_do_lote"]["total"], 2, "lote é atômico: n3 não pode ter ficado")

    def test_mecanismos_dizem_onde_olhar(self):
        self._com_modelos("viga_hipostatica.json", "portico_rotula_dupla.json")

        async def cena(c):
            out = {}
            for arq in ("viga_hipostatica.json", "portico_rotula_dupla.json"):
                await self.ok(c, "modelo_abrir", {"caminho": arq})
                out[arq] = (await self.erro(c, "projeto_calcular"), await self.erro(c, "analise_caso", {"caso": "Q1"}))
            return out
        for arq, (calc, caso) in rodar(cena, self.pasta).items():
            for texto in (calc, caso):
                self.assertIn("mecanismo", texto, arq)
                self.assertRegex(texto, r"suspeito: nó \w+, GL \w+", arq)
            self.assertIn("modelo_validar", calc, "orienta a ferramenta de diagnóstico")

    def test_modelo_so_com_nos_lista_todos_os_erros(self):
        self._com_modelos("somente_nos.json")

        async def cena(c):
            await self.ok(c, "modelo_abrir", {"caminho": "somente_nos.json"})
            return await self.erro(c, "projeto_calcular"), await self.ok(c, "modelo_validar")
        msg, val = rodar(cena, self.pasta)
        self.assertIn("sem_barras", msg)
        self.assertIn("sem_casos", msg)
        self.assertFalse(val["ok"])
        self.assertGreaterEqual(val["n_erros"], 2)

    def test_pdelta_acima_da_critica_erro_claro(self):
        self._com_modelos("pdelta_acima_da_critica.json")

        async def cena(c):
            await self.ok(c, "modelo_abrir", {"caminho": "pdelta_acima_da_critica.json"})
            primeira = await self.ok(c, "projeto_calcular")                 # 1ª ordem: calcula, barra reprovada
            return (primeira, await self.erro(c, "projeto_calcular", {"pdelta": True}),
                    await self.erro(c, "analise_combinacao", {"nome": "ELU G", "pdelta": True}),
                    await self.erro(c, "analise_caso", {"caso": "G1", "pdelta": True}))
        primeira, calc, comb, caso = rodar(cena, self.pasta)
        self.assertGreater(primeira["resumo"]["taxa_max"], 1.0)
        for texto in (calc, comb, caso):
            self.assertIn("carga acima da crítica", texto)
            self.assertIn("P-Δ", texto)

    def test_caminhos_fora_da_pasta(self):
        fora = self.pasta.parent / "prossen_fora.json"
        fora.unlink(missing_ok=True)

        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            await self.ok(c, "projeto_calcular")
            out = {}
            for rotulo, ferr, args in (
                    ("salvar_dois_pontos", "modelo_salvar", {"caminho": "../prossen_fora.json"}),
                    ("salvar_aninhado", "modelo_salvar", {"caminho": "a/../../prossen_fora.json"}),
                    ("salvar_absoluto", "modelo_salvar", {"caminho": str(fora)}),
                    ("salvar_unc", "modelo_salvar", {"caminho": "\\\\servidor\\compartilhado\\x.json"}),
                    ("abrir_absoluto", "modelo_abrir", {"caminho": str(MODELOS / "trelica_pura.json")}),
                    ("relatorio_fora", "relatorio_gerar", {"caminho": "../prossen_fora.html"}),
                    ("relatorio_absoluto", "relatorio_gerar", {"caminho": str(self.pasta.parent / "prossen_fora.html")})):
                out[rotulo] = await self.erro(c, ferr, args)
            return out
        msgs = rodar(cena, self.pasta)
        for rotulo, t in msgs.items():
            self.assertRegex(t, r"pasta de trabalho|\.\.", rotulo)
        self.assertFalse(fora.exists())
        self.assertFalse((self.pasta.parent / "prossen_fora.html").exists())

    def test_unidades_absurdas_que_a_validacao_pega(self):
        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "edificio_concreto"})
            out = {
                "V0_negativa": await self.erro(c, "config_definir", {"dados": {"vento": {**VENTO_F2, "V0": -35}}}),
                "V0_zero": await self.erro(c, "config_definir", {"dados": {"vento": {**VENTO_F2, "V0": 0}}}),
                "categoria": await self.erro(c, "config_definir", {"dados": {"vento": {**VENTO_F2, "categoria": "VI"}}}),
                "fck_5": await self.erro(c, "material_adicionar", {"concreto": 5}),
                "fck_500": await self.erro(c, "material_adicionar", {"concreto": 500}),
                "secao_negativa": await self.erro(c, "secao_adicionar", {"forma": "retangular", "b": -0.2, "h": 0.5}),
                "tipo_texto": await self.erro(c, "no_adicionar", {"x": "abc", "y": 0, "z": 0}),
                "pontual_alem": await self.erro(c, "carga_adicionar", {"tipo": "pontual", "caso": "G1", "barra": "VX1-0-0", "a": 99, "F": [0, 0, -1]}),
                "zona_sismica": await self.erro(c, "config_definir", {"dados": {"sismo": {"zona": 9, "classe_terreno": "C", "categoria_utilizacao": "I", "R": 3.0, "sistema": "concreto_portico"}}}),
            }
            # a configuração recusada não pode ter sido gravada
            out["cfg_depois"] = (await self.ok(c, "modelo_resumo"))["config"]["vento"]["V0"]
            return out
        m = rodar(cena, self.pasta)
        self.assertIn("V0", m["V0_negativa"])
        self.assertIn("> 0", m["V0_negativa"])
        self.assertIn("V0", m["V0_zero"])
        self.assertIn("I, II, III, IV, V", m["categoria"])
        self.assertIn("C20 a C90", m["fck_5"])
        self.assertIn("C20 a C90", m["fck_500"])
        self.assertIn("> 0", m["secao_negativa"])
        self.assertIn("number", m["tipo_texto"])
        self.assertIn("excede", m["pontual_alem"])
        self.assertIn("zona", m["zona_sismica"])
        self.assertEqual(m["cfg_depois"], 35.0)

    def test_V0_de_3500_ms_deveria_ser_recusada_ou_avisada(self):
        """ACHADO E2E-001 (dono: engenheiro-integracao, `projeto.configurar`/`gerar_acoes`): V0 = 3500 m/s (unidade
        absurda; a isopleta máxima do Brasil é ~50 m/s) é aceita sem erro nem aviso e reprova as 116 barras. Esperado:
        erro de faixa em `config_definir` ou aviso mencionando V0 em `acoes_gerar`."""
        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "edificio_concreto"})
            r = await c.call_tool("config_definir", {"dados": {"vento": {**VENTO_F2, "V0": 3500}}})
            if r.is_error:
                return r.content[0].text
            ac = await self.ok(c, "acoes_gerar")
            return " ".join(a["mensagem"] for a in ac.get("avisos", []))
        texto = rodar(cena, self.pasta)
        self.assertIn("V0", texto)

    def test_secao_de_25_m_e_barra_de_3_km_deveriam_gerar_aviso(self):
        """ACHADO E2E-002 (dono: engenheiro-integracao, `projeto.validar`): seção 25 m × 50 m (cm digitados como m) e
        pilar de 3 000 m (mm como m) passam em `modelo_validar` sem nenhum aviso de dimensão."""
        async def cena(c):
            await self.ok(c, "modelo_novo")
            s = await self.ok(c, "secao_adicionar", {"forma": "retangular", "b": 25, "h": 50})
            await self.ok(c, "material_adicionar", {"concreto": 30})
            await self.ok(c, "no_adicionar", {"id": "a", "x": 0, "y": 0, "z": 0, "apoio": "engaste"})
            await self.ok(c, "no_adicionar", {"id": "b", "x": 0, "y": 0, "z": 3000})
            await self.ok(c, "barra_adicionar", {"ni": "a", "nj": "b", "secao": s["nome"], "material": "C30"})
            await self.ok(c, "caso_adicionar", {"nome": "G1", "tipo": "G"})
            return await self.ok(c, "modelo_validar")
        val = rodar(cena, self.pasta)
        self.assertTrue([i for i in val["itens"] if i["codigo"] != "caso_sem_carga"], val["itens"])

    def test_geometria_de_1e308_m_deveria_dar_erro_de_entrada_nao_erro_interno(self):
        """ACHADO E2E-003 (dono: engenheiro-solver/integração): nó a 1e308 m vaza `numpy.linalg.LinAlgError`
        ("Eigenvalues did not converge") → o MCP responde "erro interno" (a web, 500). Esperado: ErroEntrada/
        ErroMecanismo com mensagem sobre a geometria."""
        async def cena(c):
            await self.ok(c, "modelo_importar_json", {"modelo": json.loads((MODELOS / "barra_vertical_unica.json").read_text(encoding="utf-8"))})
            await self.ok(c, "elemento_editar", {"tipo": "no", "id": "n2", "dados": {"z": 1e308}})
            return await self.erro(c, "projeto_calcular")
        self.assertNotIn("erro interno", rodar(cena, self.pasta))

    def test_consulta_sem_calculo_orienta_o_proximo_passo(self):
        async def cena(c):
            await self.ok(c, "modelo_novo")
            out = [await self.erro(c, "verificacao_listar"), await self.erro(c, "verificacao_barra", {"barra": "b1"}),
                   await self.erro(c, "estabilidade_consultar"), await self.erro(c, "resultados_consultar", {"alvo": "reacoes"}),
                   await self.erro(c, "relatorio_gerar", {"caminho": "r.html"})]
            with self.assertRaises(Exception) as ctx:
                await c.read_resource("calculo://ultimo")
            return out, str(ctx.exception)
        msgs, recurso = rodar(cena, self.pasta)
        for t in msgs + [recurso]:
            self.assertIn("projeto_calcular", t)

    def test_ids_e_nomes_inexistentes_listam_validos(self):
        async def cena(c):
            await self.ok(c, "modelo_abrir_exemplo", {"nome": "portico_aco"})
            await self.ok(c, "projeto_calcular")
            return {"comb": await self.erro(c, "resultados_consultar", {"alvo": "reacoes", "combinacao": "ELU XYZ"}),
                    "barra": await self.erro(c, "verificacao_barra", {"barra": "B99"}),
                    "exemplo": await self.erro(c, "modelo_abrir_exemplo", {"nome": "torre"}),
                    "perfil": await self.erro(c, "perfil_consultar", {"nome": "W999x1"}),
                    "caso": await self.erro(c, "analise_caso", {"caso": "ZZ"}),
                    "alvo": await self.erro(c, "resultados_consultar", {"alvo": "tudo"})}
        m = rodar(cena, self.pasta)
        self.assertIn("ELU XYZ", m["comb"])
        self.assertIn("ELU G", m["comb"])           # lista as combinações válidas
        self.assertIn("B99", m["barra"])
        self.assertIn("VX1-0-0", m["barra"])
        self.assertIn("viga_biapoiada", m["exemplo"])
        self.assertIn("W999x1", m["perfil"])
        self.assertIn("G1", m["caso"])
        self.assertIn("reacoes", m["alvo"])         # alvos válidos


# ================================================================= bordas
class TestModelosDeBorda(BaseMCP):
    def _abrir(self, c, arq):
        shutil.copy(MODELOS / arq, self.pasta / arq)
        return self.ok(c, "modelo_abrir", {"caminho": arq})

    def test_barra_vertical_unica_equilibrio(self):
        async def cena(c):
            await self._abrir(c, "barra_vertical_unica.json")
            q = await self.ok(c, "analise_caso", {"caso": "Q1"})
            g = await self.ok(c, "analise_caso", {"caso": "G1", "incluir": ["reacoes", "esforcos"]})
            calc = await self.ok(c, "projeto_calcular")
            return q, g, calc
        q, g, calc = rodar(cena, self.pasta)
        r = q["reacoes"]["n1"]
        self.assertAlmostEqual(r[0], -5.0, places=9)
        self.assertAlmostEqual(r[2], 50.0, places=9)
        self.assertAlmostEqual(r[4], -15.0, places=9)               # My = −F·H
        peso = 77.0 * 0.0045715607346410204 * 3.0                    # γ·A·L
        self.assertAlmostEqual(g["reacoes"]["n1"][2], peso, places=9)
        self.assertAlmostEqual(g["esforcos"]["b1"]["N"][0], -peso, places=9)   # compressão na base
        self.assertEqual(calc["resumo"]["n_verificadas"], 1)

    def test_trelica_pura_esforcos_axiais_e_sem_momento(self):
        async def cena(c):
            await self._abrir(c, "trelica_pura.json")
            return await self.ok(c, "analise_caso", {"caso": "Q1", "incluir": ["esforcos", "reacoes"]})
        r = rodar(cena, self.pasta)
        e = r["esforcos"]
        self.assertAlmostEqual(e["b1"]["N"][0], 37.5, places=9)      # banzo inferior tracionado
        self.assertAlmostEqual(e["b2"]["N"][0], -62.5, places=9)     # inclinadas comprimidas: (P/2)/(4/5)
        self.assertAlmostEqual(e["b3"]["N"][0], -62.5, places=9)
        for b in e.values():
            for comp in ("My", "Mz", "Vy", "Vz"):
                self.assertTrue(all(abs(x) < 1e-9 for x in b[comp]), (comp, b[comp]))
        self.assertAlmostEqual(sum(v[2] for v in r["reacoes"].values()), 100.0, places=9)

    def test_barra_com_rot_usa_a_inercia_fraca(self):
        """Dois balanços de 2 m (W360x51), P = 10 kN: rot = 0 → I forte; rot = π/2 → I fraca (solução fechada)."""
        d = json.loads((MODELOS / "barra_com_rot.json").read_text(encoding="utf-8"))
        sec = d["secoes"]["W360x51"]
        E, L, P = 2.0e8, 2.0, 10.0

        async def cena(c):
            await self._abrir(c, "barra_com_rot.json")
            return await self.ok(c, "analise_caso", {"caso": "Q1", "incluir": ["deslocamentos"]})
        u = rodar(cena, self.pasta)["deslocamentos"]
        self.assertAlmostEqual(u["p1"][2], -P * L ** 3 / (3 * E * sec["Iy"]), delta=1e-9 * 1e-3)
        self.assertAlmostEqual(u["p2"][2], -P * L ** 3 / (3 * E * sec["Iz"]), delta=1e-9 * 1e-3)
        self.assertGreater(abs(u["p2"][2]), 10 * abs(u["p1"][2]))     # 14,7× mais flexível no eixo fraco


class TestAvaliacao(BaseMCP):
    """`mcp_avaliacao.xml` (10 perguntas, padrão mcp-builder): cada resposta é refeita com as ferramentas do MCP."""

    def _pares(self):
        import xml.etree.ElementTree as ET
        raiz = ET.parse(Path(__file__).parent / "mcp_avaliacao.xml").getroot()
        pares = {p.get("id"): (p.findtext("question", "").strip(), float(p.findtext("answer", "").strip()))
                 for p in raiz.findall("qa_pair")}
        self.assertEqual(sorted(pares, key=int), [str(i) for i in range(1, 11)])
        for q, _ in pares.values():
            self.assertTrue(q)
        return {k: v[1] for k, v in pares.items()}

    def test_respostas_conferem_com_as_ferramentas(self):
        esperado = self._pares()
        for nome in ("trelica_pura", "barra_vertical_unica"):
            shutil.copy(MODELOS / f"{nome}.json", self.pasta / f"{nome}.json")

        async def cena(c):
            ok = lambda n, a=None: self.ok(c, n, a)  # noqa: E731
            r = {"1": float(len((await c.list_tools()).tools))}
            await ok("modelo_abrir_exemplo", {"nome": "viga_biapoiada"})
            q = await ok("analise_caso", {"caso": "Q1"})
            r["2"] = q["reacoes"]["n1"][2]
            r["3"] = round(max(abs(x) for x in q["esforcos"]["b1"]["My"]))
            await ok("modelo_novo")
            g = await ok("estrutura_gerar", {"dados": EDIFICIO_F2})
            r["4"] = g["barras_criadas"]
            await ok("config_definir", {"dados": {"peso_proprio": "G1"}})
            r["5"] = sum(v[2] for v in (await ok("analise_caso", {"caso": "G1", "incluir": ["reacoes"]}))["reacoes"].values())
            await ok("modelo_abrir_exemplo", {"nome": "galpao_aco"})
            r["6"] = sum(v[2] for v in (await ok("analise_caso", {"caso": "Q1", "incluir": ["reacoes"]}))["reacoes"].values())
            await ok("modelo_abrir_exemplo", {"nome": "portico_aco"})
            r["7"] = sum(v[0] for v in (await ok("analise_caso", {"caso": "V1", "incluir": ["reacoes"]}))["reacoes"].values())
            lig = await ok("ligacao_verificar", {"tipo": "chapa_simples", "esforcos": {"V": 120}, "dados": {
                "h": 0.24, "t": 0.0095, "d_mm": 20, "tipo_parafuso": "A325", "n": 3, "s": 0.075, "tw": 0.0072, "e": 0.06}})
            r["8"] = round(next(v for v in lig["verificacoes"] if v["nome"].startswith("parafusos: cisalhamento"))["resistente"], 1)
            await ok("modelo_abrir", {"caminho": "barra_vertical_unica.json"})
            r["9"] = abs((await ok("analise_caso", {"caso": "Q1", "incluir": ["reacoes"]}))["reacoes"]["n1"][4])
            await ok("modelo_abrir", {"caminho": "trelica_pura.json"})
            r["10"] = abs((await ok("analise_caso", {"caso": "Q1", "incluir": ["esforcos"]}))["esforcos"]["b2"]["N"][0])
            return r
        obtido = rodar(cena, self.pasta)
        for k, v in esperado.items():
            self.assertAlmostEqual(obtido[k], v, delta=1e-3 + 1e-6 * abs(v), msg=f"pergunta {k}")


if __name__ == "__main__":
    unittest.main()
