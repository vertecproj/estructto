"""Paridade entre canais (ficha 023, caso 4): o MESMO modelo JSON calculado pela CLI (`--formato json`), pelo MCP
(em processo e via stdio), pela API web e direto pela biblioteca `projeto` dá os mesmos números (tolerância relativa
1e-9): taxa e combinação de cada barra, γz/B2, reações da envoltória, flechas e resumo.

A referência é o próprio `projeto` (os adaptadores têm de ser finos); a engenharia dos números é conferida pelos
testes dos motores e da regressão — aqui só se prova que nenhum canal altera, arredonda ou perde resultado.
"""
import asyncio
import json
import tempfile
import unittest
from pathlib import Path

from mcp import Client

from prossen_estrutural import projeto, servidor_mcp

from .utilitarios import (EXEMPLOS, NOMES_EXEMPLOS, TOL, diferencas, json_estrito, lento, parametros_stdio,
                          referencia, rodar_cli, saida_cli, servidor_web)

IGNORAR = ("data", "tempo_s", "limitacao")  # prosa das limitações não é número


def modelo_json(nome):
    return json.loads((EXEMPLOS / f"{nome}.json").read_text(encoding="utf-8"))


async def pelo_mcp(c, nome, pdelta=None):
    """Importa o JSON do exemplo e devolve o que o MCP entrega para comparação."""
    async def ok(ferr, args=None):
        r = await c.call_tool(ferr, args or {})
        assert not r.is_error, f"{ferr}: {r.content[0].text if r.content else ''}"
        return r.structured_content
    await ok("modelo_importar_json", {"modelo": modelo_json(nome)})
    calc = await ok("projeto_calcular", {} if pdelta is None else {"pdelta": pdelta})
    ver = await ok("verificacao_listar", {"limite": 1000})
    est = await ok("estabilidade_consultar")
    reac = await ok("resultados_consultar", {"alvo": "reacoes", "limite": 1000})
    flechas = await ok("resultados_consultar", {"alvo": "flechas", "limite": 1000})
    desl = await ok("resultados_consultar", {"alvo": "deslocamentos", "limite": 1000})
    return {"calc": calc, "ver": ver, "est": est, "reac": reac, "flechas": flechas, "desl": desl}


def rodar_mcp(nome, pdelta=None, stdio=False, pasta=None):
    async def _():
        alvo = parametros_stdio(pasta) if stdio else servidor_mcp.mcp
        async with Client(alvo) as c:
            return await pelo_mcp(c, nome, pdelta)
    if not stdio:
        servidor_mcp.definir_pasta(pasta)
    try:
        return asyncio.run(_())
    finally:
        if not stdio:
            servidor_mcp.definir_pasta(servidor_mcp.PASTA_PADRAO)


class Paridade(unittest.TestCase):
    def conferir_mcp(self, m, ref, canal):
        """O MCP devolve respostas enxutas: confere cada fatia contra o `to_dict()` da biblioteca."""
        self.assertEqual(diferencas(m["calc"]["resumo"], ref["resumo"], ignorar=IGNORAR), [], f"{canal}: resumo")
        self.assertEqual(m["calc"]["modelo"], ref["modelo"])
        self.assertEqual(diferencas(m["est"], ref["estabilidade"]), [], f"{canal}: estabilidade")
        itens = m["ver"]["itens"]
        self.assertEqual(m["ver"]["total"], len(ref["verificacao"]))
        for lm, lr in zip(itens, ref["verificacao"]):
            self.assertEqual((lm["barra"], lm["combinacao"], lm["criterio"], lm["item"], lm["ok"]),
                             (lr["barra"], lr["combinacao"], lr["criterio"], lr["item"], lr["ok"]), f"{canal}: {lr['barra']}")
            self.assertAlmostEqual(lm["taxa"], lr["taxa"], delta=TOL * max(1.0, lr["taxa"]), msg=f"{canal}: {lr['barra']}")
            self.assertAlmostEqual(lm["x_m"], lr["x_m"], delta=1e-9)
        self.assertEqual(len(itens), len(ref["verificacao"]))
        self.assertEqual(set(m["reac"]["dados"]), set(ref["reacoes_envoltoria"]), canal)
        for no, v in m["reac"]["dados"].items():
            for k in ("max", "min", "comb_max", "comb_min"):
                self.assertEqual(diferencas(v[k], ref["reacoes_envoltoria"][no][k]), [], f"{canal}: reação {no}.{k}")

    def conferir_deslocamentos_mcp(self, m, calc):
        """Flechas/deslocamentos da envoltória do MCP = `projeto.resultados` direto."""
        for alvo, chave in (("flechas", "flechas"), ("deslocamentos", "desl")):
            direto = projeto.resultados(calc, alvo)
            self.assertEqual(diferencas(m[chave]["dados"], direto), [], alvo)

    # ---------------------------------------------------------------- 4 exemplos x 5 canais
    def test_exemplos_cli_web_projeto_idem(self):
        """CLI (json) = web (POST /api/calcular) = projeto, em tudo o que o `to_dict` tem."""
        with servidor_web() as (porta, api, pasta):
            for nome in NOMES_EXEMPLOS:
                with self.subTest(exemplo=nome):
                    _, calc = referencia(nome)
                    ref = calc.to_dict()
                    cli = saida_cli(nome)
                    self.assertEqual(cli["rc"], 0, cli["stderr"][-300:])
                    st, corpo = api("PUT", "/api/modelo", modelo_json(nome))
                    self.assertEqual(st, 200, corpo)
                    st, web = api("POST", "/api/calcular")
                    self.assertEqual(st, 200, web)
                    self.assertEqual(diferencas(cli["json"], ref, ignorar=IGNORAR), [], "CLI × projeto")
                    self.assertEqual(diferencas(web, ref, ignorar=IGNORAR), [], "web × projeto")
                    self.assertEqual(diferencas(web, cli["json"], ignorar=IGNORAR), [], "web × CLI")
                    # os endpoints de consulta devolvem as mesmas fatias
                    self.assertEqual(diferencas(api("GET", "/api/verificacao")[1], ref["verificacao"]), [], "GET /api/verificacao")
                    self.assertEqual(diferencas(api("GET", "/api/estabilidade")[1], ref["estabilidade"]), [], "GET /api/estabilidade")
                    reac = api("GET", "/api/resultados?alvo=reacoes")[1]
                    self.assertEqual(diferencas(reac, projeto.resultados(calc, "reacoes")), [], "GET /api/resultados reações")
                    self.assertEqual(diferencas(api("GET", "/api/resultados?alvo=flechas")[1], projeto.resultados(calc, "flechas")), [])

    def test_exemplos_mcp_em_processo_igual_a_biblioteca(self):
        with tempfile.TemporaryDirectory() as t:
            for nome in NOMES_EXEMPLOS:
                with self.subTest(exemplo=nome):
                    _, calc = referencia(nome)
                    m = rodar_mcp(nome, pasta=t)
                    self.conferir_mcp(m, calc.to_dict(), "MCP em processo")
                    self.conferir_deslocamentos_mcp(m, calc)

    @lento
    def test_exemplos_mcp_via_stdio_igual_a_cli(self):
        """O que o Claude Code vê (stdio) = o que a CLI grava: portico_aco e edificio_concreto."""
        with tempfile.TemporaryDirectory() as t:
            for nome in ("portico_aco", "edificio_concreto"):
                with self.subTest(exemplo=nome):
                    cli = saida_cli(nome)["json"]
                    m = rodar_mcp(nome, stdio=True, pasta=t)
                    self.conferir_mcp(m, cli, "MCP stdio × CLI")

    def test_exemplos_cli_relatorios_trazem_as_mesmas_taxas(self):
        """O MD e o HTML da CLI mostram a taxa máxima e a barra crítica do JSON (mesma execução)."""
        for nome in NOMES_EXEMPLOS:
            with self.subTest(exemplo=nome):
                s = saida_cli(nome)
                r = s["json"]["resumo"]
                taxa = f"{r['taxa_max']:.2f}".replace(".", ",")
                self.assertIn(f"**Taxa máxima:** {taxa} em {r['barra_critica']}", s["md"])
                self.assertIn(f"Taxa máxima: {taxa} (barra {r['barra_critica']})", s["stdout"])
                self.assertIn(r["barra_critica"], s["html"])

    # ---------------------------------------------------------------- P-Δ
    def test_pdelta_cli_mcp_web_projeto(self):
        nome = "portico_aco"
        m0 = projeto.carregar(EXEMPLOS / f"{nome}.json")
        ref = projeto.calcular(m0, {"pdelta": True}).to_dict()
        cli = saida_cli(nome, True)
        self.assertEqual(cli["rc"], 0, cli["stderr"][-300:])
        self.assertEqual(diferencas(cli["json"], ref, ignorar=IGNORAR), [], "CLI --pdelta × projeto")
        with tempfile.TemporaryDirectory() as t:
            self.conferir_mcp(rodar_mcp(nome, pdelta=True, pasta=t), ref, "MCP pdelta")
        with servidor_web() as (porta, api, pasta):
            api("PUT", "/api/modelo", modelo_json(nome))
            st, web = api("POST", "/api/calcular", {"pdelta": True})
            self.assertEqual(st, 200, web)
            self.assertEqual(diferencas(web, ref, ignorar=IGNORAR), [], "web pdelta × projeto")
        self.assertTrue(ref["opcoes"]["pdelta"])

    # ---------------------------------------------------------------- relatório do MCP e da web = o da CLI
    def test_relatorio_json_do_mcp_e_da_web_igual_ao_da_cli(self):
        nome = "portico_aco"
        cli = saida_cli(nome)["json"]
        with tempfile.TemporaryDirectory() as t:
            async def cena(c):
                await c.call_tool("modelo_importar_json", {"modelo": modelo_json(nome)})
                await c.call_tool("projeto_calcular", {})
                return (await c.call_tool("relatorio_gerar", {"caminho": "r.json", "formato": "json"})).structured_content
            servidor_mcp.definir_pasta(t)
            try:
                async def _():
                    async with Client(servidor_mcp.mcp) as c:
                        return await cena(c)
                rel = asyncio.run(_())
            finally:
                servidor_mcp.definir_pasta(servidor_mcp.PASTA_PADRAO)
            mcp_json = json_estrito(Path(rel["caminho"]).read_text(encoding="utf-8"))
        with servidor_web() as (porta, api, pasta):
            api("PUT", "/api/modelo", modelo_json(nome))
            api("POST", "/api/calcular")
            self.assertEqual(api("POST", "/api/relatorio", {"formato": "json", "caminho": "r.json"})[0], 200)
            web_json = json_estrito((pasta / "r.json").read_text(encoding="utf-8"))
        self.assertEqual(diferencas(mcp_json, cli, ignorar=IGNORAR), [], "relatório JSON MCP × CLI")
        self.assertEqual(diferencas(web_json, cli, ignorar=IGNORAR), [], "relatório JSON web × CLI")

    # ---------------------------------------------------------------- bordas pelos três canais
    def test_bordas_cli_mcp_web_dao_o_mesmo(self):
        from .utilitarios import MODELOS
        for arq in ("barra_vertical_unica", "trelica_pura", "barra_com_rot"):
            with self.subTest(modelo=arq):
                d = json.loads((MODELOS / f"{arq}.json").read_text(encoding="utf-8"))
                ref = projeto.calcular(projeto.de_dict(d)).to_dict()
                with tempfile.TemporaryDirectory() as t:
                    p = rodar_cli(MODELOS / f"{arq}.json", "-o", t, "--formato", "json")
                    self.assertEqual(p.returncode, 0, p.stderr)
                    cli = json_estrito((Path(t) / f"{arq}.json").read_text(encoding="utf-8"))

                    async def _():
                        servidor_mcp.definir_pasta(t)
                        async with Client(servidor_mcp.mcp) as c:
                            await c.call_tool("modelo_importar_json", {"modelo": d})
                            return (await c.call_tool("projeto_calcular", {})).structured_content
                    try:
                        mcp = asyncio.run(_())
                    finally:
                        servidor_mcp.definir_pasta(servidor_mcp.PASTA_PADRAO)
                with servidor_web() as (porta, api, pasta):
                    api("PUT", "/api/modelo", d)
                    web = api("POST", "/api/calcular")[1]
                self.assertEqual(diferencas(cli, ref, ignorar=IGNORAR), [], "CLI")
                self.assertEqual(diferencas(web, ref, ignorar=IGNORAR), [], "web")
                self.assertEqual(diferencas(mcp["resumo"], ref["resumo"], ignorar=IGNORAR), [], "MCP")


if __name__ == "__main__":
    unittest.main()
