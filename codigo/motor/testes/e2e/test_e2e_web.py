"""E2E da interface web (ficha 023, casos 3 e 6): API HTTP com servidor em thread (porta 0);
só a API JSON (a interface web saiu; o produto usa WPF).

Valores de referência independentes do código:
- viga biapoiada L = 6 m, q = 10 kN/m: reações qL/2 = 30 kN, M_máx = qL²/8 = 45 kN·m (resistência dos materiais).
- pórtico do F1 (3 pilares de 4 m W310x38.7 + 2 vigas de 6 m W360x51, q = 10 kN/m): ΣRz(Q1) = 10·12 = 120 kN e
  ΣRz(G1, peso próprio) = 77·(12·A_pilar + 12·A_viga) = 77·(12·49,72 + 12·64,80) cm² = 10,58 kN (`exemplos/LEIAME.md`).
- peso próprio do edifício gerado: 450 kN (pilares) + 850 kN (vigas) = 1 300 kN.
"""
import json
import os
import re
import tempfile
import unittest
from pathlib import Path

from .utilitarios import (EDIFICIO_F2, EXEMPLOS, MODELOS, NOMES_EXEMPLOS, TOL, VENTO_F2,
                          diferencas, json_estrito, nao_finitos, referencia, referencia_f2,
                          rodar_cli, servidor_web)

VIGA = {"secao": {"perfil": "W360x51"}, "material": {"aco": "A-572 GR.50"}}


def montar_viga(api):
    """F1 pela API: viga biapoiada L = 6 m com G1 (5 kN/m + peso próprio) e Q1 (10 kN/m)."""
    assert api("POST", "/api/modelo/novo", {"nome": "Viga F1"})[0] == 200
    assert api("POST", "/api/modelo/secao", VIGA["secao"])[0] == 200
    assert api("POST", "/api/modelo/material", VIGA["material"])[0] == 200
    assert api("POST", "/api/modelo/no", {"id": "n1", "x": 0, "y": 0, "z": 0, "apoio": [True, True, True, True, False, False]})[0] == 200
    assert api("POST", "/api/modelo/no", {"id": "n2", "x": 6, "y": 0, "z": 0, "apoio": [False, True, True, False, False, False]})[0] == 200
    assert api("POST", "/api/modelo/barra", {"id": "b1", "ni": "n1", "nj": "n2", "secao": "W360x51", "material": "A-572 GR.50",
                                             "params": {"papel": "viga", "elemento_desloc": "viga_piso"}})[0] == 200
    assert api("POST", "/api/modelo/caso", {"nome": "G1", "tipo": "G"})[0] == 200
    assert api("POST", "/api/modelo/caso", {"nome": "Q1", "tipo": "Q"})[0] == 200
    assert api("POST", "/api/modelo/carga_distribuida", {"caso": "G1", "barra": "b1", "q": [0, 0, -5.0]})[0] == 200
    assert api("POST", "/api/modelo/carga_distribuida", {"caso": "Q1", "barra": "b1", "q": [0, 0, -10.0]})[0] == 200
    assert api("PATCH", "/api/config", {"peso_proprio": "G1"})[0] == 200


class TestApiWeb(unittest.TestCase):
    def setUp(self):
        import contextlib
        pilha = contextlib.ExitStack()
        self.addCleanup(pilha.close)
        self.porta, self.api, self.pasta = pilha.enter_context(servidor_web())

    def ok(self, metodo, caminho, dados=None, status=200, **kw):
        st, corpo = self.api(metodo, caminho, dados, **kw)
        self.assertEqual(st, status, (metodo, caminho, str(corpo)[:400]))
        self.assertEqual(nao_finitos(corpo), [], (metodo, caminho))
        return corpo

    def erro(self, metodo, caminho, dados=None, status=422, codigo="entrada_invalida", **kw):
        st, corpo = self.api(metodo, caminho, dados, **kw)
        self.assertEqual(st, status, (metodo, caminho, str(corpo)[:400]))
        self.assertIsInstance(corpo, dict, "erros são JSON")
        self.assertEqual(corpo["erro"], codigo, corpo)
        self.assertTrue(corpo["detalhe"])
        self.assertNotIn("Traceback", json.dumps(corpo))
        return corpo

    # ------------------------------------------------------------ fluxo F1 pela API
    def test_f1_viga_crud_calculo_verificacao_relatorio(self):
        montar_viga(self.api)
        r = self.ok("GET", "/api/resumo")
        self.assertEqual((r["n_nos"], r["n_barras"], r["n_casos"], r["n_cargas"]), (2, 1, 2, 2))
        self.assertEqual(self.ok("GET", "/api/validar"), [])
        self.assertEqual([c["nome"] for c in self.ok("GET", "/api/combinacoes")],
                         [c["nome"] for c in referencia("viga_biapoiada")[1].combinacoes])   # mesmo modelo do exemplo
        geo = self.ok("GET", "/api/geometria")
        self.assertAlmostEqual(geo["barras"][0]["L"], 6.0)
        self.assertEqual({c["id"] for c in self.ok("GET", "/api/modelo/carga_distribuida")}, {"cd1", "cd2"})
        self.assertEqual([c["id"] for c in self.ok("GET", "/api/modelo/carga_distribuida?caso=Q1")], ["cd2"])
        # solução fechada
        q1 = self.ok("GET", "/api/analise/caso/Q1")
        for no in ("n1", "n2"):
            self.assertAlmostEqual(q1["reacoes"][no][2], 30.0, places=6)      # qL/2
        self.assertAlmostEqual(max(abs(x) for x in q1["esforcos"]["b1"]["My"]), 45.0, places=6)   # qL²/8
        # cálculo = exemplo viga_biapoiada (mesmo modelo montado à mão), 1e-9
        calc = self.ok("POST", "/api/calcular")
        _, ref = referencia("viga_biapoiada")
        self.assertEqual(diferencas(calc, ref.to_dict(), ignorar=("data", "tempo_s", "modelo")), [])
        self.assertEqual(self.ok("GET", "/api/calculo")["resumo"]["taxa_max"], calc["resumo"]["taxa_max"])
        self.assertEqual(len(self.ok("GET", "/api/verificacao")), 1)
        self.assertEqual(self.ok("GET", "/api/verificacao?reprovadas=true"), [])
        self.assertEqual(len(self.ok("GET", "/api/verificacao?tipo=aco")), 1)
        det = self.ok("GET", "/api/verificacao/b1")
        self.assertTrue(det["detalhes"] and det["por_combinacao"])
        self.assertFalse(self.ok("GET", "/api/estabilidade")["aplicavel"])      # sem pavimentos
        reac = self.ok("GET", "/api/resultados?alvo=reacoes&ids=n1,n2")
        self.assertEqual(set(reac), {"n1", "n2"})
        comb = self.ok("GET", "/api/resultados?alvo=reacoes&combinacao=ELU%20G")
        self.assertAlmostEqual(comb["n1"][2], 1.4 * (5.0 + 77 * 0.006479770657883069) * 3, delta=1e-6)   # 1,4·gL/2
        # relatório
        html = self.ok("GET", "/api/relatorio.html")
        self.assertIn("Ferramenta de apoio", html)
        self.assertTrue(html.lstrip().lower().startswith("<!doctype html"))
        for fmt in ("html", "md", "json"):
            g = self.ok("POST", "/api/relatorio", {"formato": fmt, "caminho": f"rel/viga.{fmt}"})
            self.assertTrue((self.pasta / "rel" / f"viga.{fmt}").is_file())
            self.assertGreater(g["bytes"], 1000)
        self.assertEqual(diferencas(json_estrito((self.pasta / "rel/viga.json").read_text(encoding="utf-8")), calc,
                                    ignorar=("data", "tempo_s")), [])

    def test_salvar_abrir_arquivos_e_modelo_inteiro(self):
        montar_viga(self.api)
        antes = self.ok("GET", "/api/modelo")
        self.ok("POST", "/api/modelo/salvar", {"caminho": "m/viga.json"})
        self.assertIn("m/viga.json", self.ok("GET", "/api/arquivos"))
        self.ok("POST", "/api/modelo/novo", {"nome": "outro"})
        self.assertEqual(self.ok("GET", "/api/resumo")["n_barras"], 0)
        self.ok("POST", "/api/modelo/abrir", {"caminho": "m/viga.json"})
        self.assertEqual(diferencas(self.ok("GET", "/api/modelo"), antes), [])
        # PUT com o JSON de um exemplo
        d = json.loads((EXEMPLOS / "portico_aco.json").read_text(encoding="utf-8"))
        self.ok("PUT", "/api/modelo", d)
        self.assertEqual(self.ok("GET", "/api/resumo")["n_barras"], 5)

    def test_lote_atomico_desfazer_refazer_e_remocao(self):
        montar_viga(self.api)
        n0 = len(self.ok("GET", "/api/modelo/no"))
        e = self.erro("POST", "/api/modelo/lote", {"itens": [
            {"tipo": "no", "dados": {"id": "n3", "x": 1, "y": 0, "z": 0}},
            {"tipo": "no", "dados": {"id": "n4", "x": "abc", "y": 0, "z": 0}}]})
        self.assertTrue(any("itens[1]" in x["campo"] for x in e["erros"]), e["erros"])
        self.assertEqual(len(self.ok("GET", "/api/modelo/no")), n0, "lote é atômico")
        ok = self.ok("POST", "/api/modelo/lote", {"itens": [
            {"tipo": "no", "dados": {"id": "n3", "x": 1, "y": 0, "z": 0}},
            {"tipo": "no", "dados": {"id": "n4", "x": 2, "y": 0, "z": 0}}]})
        self.assertEqual(len(ok["criados"]), 2)
        # desfazer / refazer
        self.ok("POST", "/api/calcular")
        self.ok("PATCH", "/api/modelo/no/n3", {"x": 1.5})
        self.assertIsNone(self.ok("GET", "/api/calculo"), "editar descarta o cálculo")
        self.assertEqual(self.ok("GET", "/api/modelo/no?id=n3")[0]["x"], 1.5)
        self.ok("POST", "/api/desfazer")
        self.assertEqual(self.ok("GET", "/api/modelo/no?id=n3")[0]["x"], 1.0)
        self.ok("POST", "/api/refazer")
        self.assertEqual(self.ok("GET", "/api/modelo/no?id=n3")[0]["x"], 1.5)
        # remoção referenciada: lista os dependentes; cascata resolve
        e = self.erro("DELETE", "/api/modelo/no/n2")
        self.assertIn("b1", e["detalhe"])
        self.assertIn("cascata", e["detalhe"])
        rem = self.ok("DELETE", "/api/modelo/no/n2?cascata=1")
        self.assertIn("b1", {x["id"] for x in rem["removidos"]})
        self.assertEqual(self.ok("GET", "/api/resumo")["n_barras"], 0)
        # pilha vazia depois de esgotar
        while self.api("POST", "/api/desfazer")[0] == 200:
            pass
        self.erro("POST", "/api/desfazer", status=409, codigo="pilha_vazia")

    def test_edificio_f2_pela_api_igual_a_biblioteca(self):
        _, _, ref = referencia_f2()
        self.ok("POST", "/api/modelo/novo", {"nome": "F5"})
        g = self.ok("POST", "/api/gerar-estrutura", EDIFICIO_F2)
        self.assertEqual((g["nos_criados"], g["barras_criadas"], g["pavimentos_criados"]), (60, 116, 4))
        self.ok("PATCH", "/api/config", {"peso_proprio": "G1", "vento": VENTO_F2})
        ac = self.ok("POST", "/api/acoes/gerar")
        self.assertEqual(set(ac["casos"]), {"V+X", "V-X", "V+Y", "V-Y"})
        g1 = self.ok("GET", "/api/analise/caso/G1")
        self.assertAlmostEqual(sum(v[2] for v in g1["reacoes"].values()), 1300.0, delta=1e-6)   # 450 + 850 kN
        calc = self.ok("POST", "/api/calcular")
        self.assertEqual(diferencas(calc, ref, ignorar=("data", "tempo_s")), [])
        est = self.ok("GET", "/api/estabilidade")
        for d in ("X", "Y"):
            self.assertEqual(est["gama_z"][d]["classificacao"], "nos_fixos")
            self.assertGreaterEqual(est["gama_z"][d]["valor"], 1.0)
        self.assertEqual(len(self.ok("GET", "/api/verificacao")), 116)
        self.assertEqual(self.ok("GET", "/api/verificacao?reprovadas=true"), [])
        modal = self.ok("GET", "/api/modal?n=3")
        self.assertEqual(len(modal["frequencias_hz"]), 3)
        self.assertTrue(all(f > 0 for f in modal["frequencias_hz"]))
        self.assertEqual(modal["frequencias_hz"], sorted(modal["frequencias_hz"]))

    def test_exemplos_e_catalogos(self):
        ex = self.ok("GET", "/api/exemplos")
        self.assertEqual({e["nome"] for e in ex}, set(NOMES_EXEMPLOS))
        for nome in ("viga_biapoiada", "portico_aco"):
            self.ok("POST", f"/api/exemplos/{nome}")
            calc = self.ok("POST", "/api/calcular")
            _, ref = referencia(nome)
            self.assertAlmostEqual(calc["resumo"]["taxa_max"], ref.resumo()["taxa_max"], delta=TOL)
        self.assertGreater(len(self.ok("GET", "/api/catalogo?familia=W")), 20)
        self.assertEqual(self.ok("GET", "/api/catalogo/W310x38.7")["nome"], "W310x38.7")
        self.assertIn("aco", self.ok("GET", "/api/materiais"))
        normas = self.ok("GET", "/api/normas")
        self.assertTrue(any(n["norma"] == "NBR 8800" and n["edicao"] for n in normas))
        tipos = self.ok("GET", "/api/ligacoes/tipos")
        self.assertLessEqual({"parafuso", "solda_filete", "chapa_simples", "ligacao_tracao"}, {t["tipo"] for t in tipos})

    def test_ligacao_f4_chapa_simples(self):
        corpo = {"tipo": "chapa_simples", "esforcos": {"V": 120},
                 "dados": {"h": 0.24, "t": 0.0095, "d_mm": 20, "tipo_parafuso": "A325", "n": 3, "s": 0.075, "tw": 0.0072, "e": 0.06}}
        r = self.ok("POST", "/api/ligacoes/verificar", corpo)
        import math
        fv_rd = 0.4 * (math.pi * 20 ** 2 / 4) * 825 / 1.35 / 1000.0          # NBR 8800 6.3.3.2 (kN)
        cis = next(v for v in r["verificacoes"] if v["nome"].startswith("parafusos: cisalhamento"))
        self.assertAlmostEqual(cis["resistente"], fv_rd, delta=1e-3)
        self.assertTrue(r["ok"])
        e = self.erro("POST", "/api/ligacoes/verificar", {"tipo": "inventada", "dados": {}, "esforcos": {}})
        self.assertIn("inventada", e["detalhe"])
        self.erro("POST", "/api/ligacoes/verificar", {"tipo": "chapa_simples", "dados": {"h": -1}, "esforcos": {"V": 1}})

    # ------------------------------------------------------------ erros em JSON: 400/403/404/405/409/413/422
    def test_erros_400_404_405_409_413(self):
        # 400: JSON ilegível; corpo que não é objeto; Content-Length inválido
        self.erro("POST", "/api/modelo/no", corpo_bruto=b'{"x": ', status=400, codigo="json_invalido")
        self.erro("POST", "/api/modelo/no", corpo_bruto=b"[1, 2]", status=400, codigo="json_invalido")
        self.erro("POST", "/api/modelo/no", corpo_bruto=b"{}", cabecalhos={"Content-Length": "abc"}, status=400, codigo="json_invalido")
        # 404: rota, id de modelo, id de verificação, exemplo, perfil
        self.erro("GET", "/api/naoexiste", status=404, codigo="nao_encontrado")
        self.erro("PATCH", "/api/modelo/no/zzz", {"x": 1}, status=404, codigo="nao_encontrado")
        self.erro("POST", "/api/exemplos/torre", status=404, codigo="nao_encontrado")
        self.erro("GET", "/api/catalogo/W999x1", status=404, codigo="nao_encontrado")
        self.erro("GET", "/arquivo_que_nao_existe.js", status=404, codigo="nao_encontrado")
        # 409: consulta sem cálculo
        for rota in ("/api/verificacao", "/api/verificacao/b1", "/api/estabilidade", "/api/resultados?alvo=reacoes", "/api/relatorio.html"):
            e = self.erro("GET", rota, status=409, codigo="sem_calculo")
            self.assertIn("calcul", e["detalhe"])
        self.assertIsNone(self.ok("GET", "/api/calculo"))                      # sem cálculo: 200 null
        self.erro("POST", "/api/relatorio", {"formato": "html", "caminho": "r.html"}, status=409, codigo="sem_calculo")
        # 405 em estático; 403 em Host de outro nome e em caminhos fora da pasta
        self.erro("POST", "/index.html", {}, status=405, codigo="metodo_nao_permitido")
        self.erro("GET", "/api/resumo", cabecalhos={"Host": "evil.example:80"}, status=403, codigo="host_recusado")
        for rota in ("/..%2f..%2fservidor.py", "/%2e%2e/%2e%2e/servidor.py", "/..\\servidor.py"):
            self.erro("GET", rota, status=403, codigo="caminho_recusado")
        # 413: corpo declarado acima de 10 MB (recusado antes de ler)
        e = self.erro("POST", "/api/modelo/no", corpo_bruto=b"{}", cabecalhos={"Content-Length": str(11 * 1024 * 1024)},
                      status=413, codigo="corpo_grande")
        self.assertIn("10 MB", e["detalhe"])
        # e o servidor continua de pé depois dos erros
        self.assertEqual(self.ok("GET", "/api/resumo")["n_nos"], 0)

    def test_erros_422_orientam_a_correcao(self):
        montar_viga(self.api)
        e = self.erro("POST", "/api/modelo/barra", {"ni": "n1", "nj": "n9", "secao": "W360x51", "material": "A-572 GR.50"})
        self.assertIn("n9", e["detalhe"])
        self.assertIn("n1, n2", e["detalhe"])                                  # ids válidos
        self.assertEqual(e["erros"][0]["campo"], "nj")
        e = self.erro("POST", "/api/modelo/no", {"x": "abc", "y": 0, "z": 0})
        self.assertEqual(e["erros"][0]["campo"], "x")
        self.erro("POST", "/api/modelo/secao", {"perfil": "W999x1"})
        e = self.erro("POST", "/api/modelo/material", {"concreto": 5})
        self.assertIn("C20 a C90", e["detalhe"])
        e = self.erro("PATCH", "/api/config", {"vento": {**VENTO_F2, "V0": -35}})
        self.assertIn("V0", e["detalhe"])
        e = self.erro("PATCH", "/api/config", {"vento": {**VENTO_F2, "categoria": "VI"}})
        self.assertIn("I, II, III, IV, V", e["detalhe"])
        self.erro("PATCH", "/api/config", {"chave_inexistente": 1})
        self.erro("POST", "/api/modelo/xyz", {})
        e = self.erro("POST", "/api/modelo/carga_pontual", {"caso": "Q1", "barra": "b1", "a": 99, "F": [0, 0, -1]})
        self.assertIn("excede", e["detalhe"])
        # arquivos fora da pasta de trabalho (barra do modelo salvo, abrir, relatório)
        for rota, corpo in (("/api/modelo/salvar", {"caminho": "../fora.json"}), ("/api/modelo/salvar", {"caminho": str(self.pasta.parent / "fora.json")}),
                            ("/api/modelo/abrir", {"caminho": str(MODELOS / "trelica_pura.json")}),
                            ("/api/modelo/abrir", {"caminho": "inexistente.json"})):
            e = self.erro("POST", rota, corpo)
            self.assertRegex(e["detalhe"], r"pasta de trabalho|\.\.|não encontrado")
        self.assertFalse((self.pasta.parent / "fora.json").exists())
        self.ok("POST", "/api/calcular")
        for caminho in ("../r.html", str(self.pasta.parent / "r.html"), "\\\\servidor\\x\\r.html"):
            self.erro("POST", "/api/relatorio", {"formato": "html", "caminho": caminho})
        self.erro("POST", "/api/relatorio", {"formato": "docx", "caminho": "r.docx"})
        self.erro("GET", "/api/resultados?alvo=tudo")
        self.erro("GET", "/api/modal?n=abc")

    def test_modelos_com_erro_dao_422_em_json(self):
        # só nós: erros de validação juntos
        self.ok("PUT", "/api/modelo", json.loads((MODELOS / "somente_nos.json").read_text(encoding="utf-8")))
        e = self.erro("POST", "/api/calcular")
        self.assertEqual({x["campo"] for x in e["erros"]} & {"sem_barras", "sem_casos"}, {"sem_barras", "sem_casos"}, e)
        # mecanismos
        for arq in ("viga_hipostatica.json", "portico_rotula_dupla.json"):
            self.ok("PUT", "/api/modelo", json.loads((MODELOS / arq).read_text(encoding="utf-8")))
            e = self.erro("POST", "/api/calcular", codigo="mecanismo")
            self.assertRegex(e["detalhe"], r"suspeito: nó \w+, GL \w+", arq)
            e = self.erro("GET", "/api/analise/caso/Q1", codigo="mecanismo")
            self.assertIn("mecanismo", e["detalhe"])
        # P-Δ acima da crítica
        self.ok("PUT", "/api/modelo", json.loads((MODELOS / "pdelta_acima_da_critica.json").read_text(encoding="utf-8")))
        e = self.erro("POST", "/api/calcular", {"pdelta": True}, codigo="mecanismo")
        self.assertIn("carga acima da crítica", e["detalhe"])
        self.assertIn("P-Δ", e["detalhe"])
        self.assertEqual(self.ok("POST", "/api/calcular")["resumo"]["n_barras"], 1)   # 1ª ordem: calcula

    def test_bordas_equilibrio_pela_api(self):
        # barra vertical única
        self.ok("PUT", "/api/modelo", json.loads((MODELOS / "barra_vertical_unica.json").read_text(encoding="utf-8")))
        r = self.ok("GET", "/api/analise/caso/Q1")["reacoes"]["n1"]
        self.assertAlmostEqual(r[0], -5.0, places=9)
        self.assertAlmostEqual(r[2], 50.0, places=9)
        self.assertAlmostEqual(r[4], -15.0, places=9)
        # treliça pura
        self.ok("PUT", "/api/modelo", json.loads((MODELOS / "trelica_pura.json").read_text(encoding="utf-8")))
        e = self.ok("GET", "/api/analise/caso/Q1")["esforcos"]
        self.assertAlmostEqual(e["b1"]["N"][0], 37.5, places=9)
        self.assertAlmostEqual(e["b2"]["N"][0], -62.5, places=9)

    def test_geometria_de_1e308_m_nao_pode_dar_500(self):
        """ACHADO E2E-003 (ver test_e2e_mcp): nó a 1e308 m → `LinAlgError` vaza e a API responde 500 `interno`
        em vez de 422 com mensagem sobre a geometria."""
        self.ok("PUT", "/api/modelo", json.loads((MODELOS / "barra_vertical_unica.json").read_text(encoding="utf-8")))
        self.ok("PATCH", "/api/modelo/no/n2", {"z": 1e308})
        st, corpo = self.api("POST", "/api/calcular")
        self.assertNotEqual(st, 500, corpo)

    def test_nenhuma_resposta_com_nan_ou_inf_nos_exemplos(self):
        for nome in ("viga_biapoiada", "galpao_aco"):
            self.ok("POST", f"/api/exemplos/{nome}")
            self.ok("POST", "/api/calcular")
            for rota in ("/api/calculo", "/api/verificacao", "/api/estabilidade", "/api/resultados?alvo=deslocamentos",
                         "/api/resultados?alvo=esforcos", "/api/resultados?alvo=flechas", "/api/geometria",
                         "/api/analise/caso/Q1"):
                self.ok("GET", rota)   # `ok` roda json_estrito (recusa NaN/Infinity) e nao_finitos
