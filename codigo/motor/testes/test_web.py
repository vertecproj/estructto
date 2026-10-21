"""Testes do servidor web local (fichas 007 e 017): segurança de caminho/Host, MIME, erros JSON (ESPEC §6.1)
e rotas da ESPEC §9 ligadas ao `projeto.py` real (CRUD, lote, desfazer/refazer, cálculo, relatório...).

Referência dos números: a própria API tem de reproduzir `projeto` chamado direto (tolerância 1e-9) — o servidor é
um adaptador fino. Valor de engenharia conferido à parte: viga biapoiada L = 6 m, q = 10 kN/m (caso Q1 do exemplo
`viga_biapoiada`) → M_max = qL²/8 = 10·6²/8 = 45 kN·m (solução fechada, resistência dos materiais).
"""
import base64
import http.client
import json
import tempfile
import threading
import unittest
from pathlib import Path
from urllib.parse import quote

from prossen_estrutural import projeto
from prossen_estrutural.web import servidor


class BaseWeb(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.pasta = Path(self.tmp.name)
        self.srv = servidor.criar_servidor(porta=0, pasta=self.pasta)
        self.porta = self.srv.server_address[1]
        self.thread = threading.Thread(target=self.srv.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.srv.shutdown()
        self.srv.server_close()
        self.thread.join(5)
        self.tmp.cleanup()

    def pedir(self, metodo, caminho, corpo=None, cabecalhos=None):
        """Como o navegador na mesma origem: Origin da própria porta e, fora de GET/HEAD, Content-Type JSON.
        Um cabeçalho com valor None é omitido (para testar a ausência)."""
        cab = {"Origin": f"http://127.0.0.1:{self.porta}"}
        if metodo not in ("GET", "HEAD"):
            cab["Content-Type"] = "application/json"
        cab.update(cabecalhos or {})
        cab = {k: v for k, v in cab.items() if v is not None}
        c = http.client.HTTPConnection("127.0.0.1", self.porta, timeout=60)
        try:
            c.request(metodo, caminho, body=corpo, headers=cab)
            r = c.getresponse()
            return r.status, dict(r.getheaders()), r.read()
        finally:
            c.close()

    def json_de(self, corpo):
        return json.loads(corpo.decode("utf-8"))

    def api(self, metodo, caminho, dados=None, status=200):
        """Chama a API com JSON e confere o status; devolve o JSON (ou o texto, se não for JSON)."""
        corpo = None if dados is None else json.dumps(dados).encode("utf-8")
        st, cab, saida = self.pedir(metodo, caminho, corpo, {"Content-Type": "application/json"} if corpo else None)
        self.assertEqual(st, status, (metodo, caminho, saida[:400]))
        return self.json_de(saida) if cab["Content-Type"].startswith("application/json") else saida.decode("utf-8")

    def assertQuase(self, a, b, tol=1e-9, onde=""):
        """Igualdade recursiva com tolerância nos números (dict, list, str, bool, None)."""
        if isinstance(a, dict):
            self.assertEqual(set(a), set(b), onde)
            for k in a:
                self.assertQuase(a[k], b[k], tol, f"{onde}.{k}")
        elif isinstance(a, list):
            self.assertEqual(len(a), len(b), onde)
            for i, (x, y) in enumerate(zip(a, b)):
                self.assertQuase(x, y, tol, f"{onde}[{i}]")
        elif isinstance(a, float) or isinstance(b, float):
            self.assertAlmostEqual(a, b, delta=tol * max(1.0, abs(b)), msg=onde)
        else:
            self.assertEqual(a, b, onde)


class TestServidorWeb(BaseWeb):
    """Base da ficha 007: segurança e formato dos erros."""

    def test_so_loopback(self):
        self.assertEqual(self.srv.server_address[0], "127.0.0.1")

    def test_caminho_fora_da_pasta_recusado(self):
        for caminho in ("/../ESPEC.md", "/%2e%2e/ESPEC.md", "/%2E%2E%2FESPEC.md", "/..%5cESPEC.md",
                        "/dev/../../servidor.py", "/%2e%2e%2f%2e%2e%2fservidor.py", "/vendor/../../servidor.py",
                        "/C:/Windows/win.ini", "//etc/passwd"):
            st, _, corpo = self.pedir("GET", caminho)
            self.assertIn(st, (403, 404), caminho)
            self.assertNotIn(b"ThreadingHTTPServer", corpo, caminho)
            self.assertIn("erro", self.json_de(corpo), caminho)  # corpo é JSON de erro, nunca o arquivo

    def test_host_estranho_recusado(self):
        for host in ("exemplo.com", f"exemplo.com:{self.porta}", "127.0.0.1:1", "192.168.0.10:%d" % self.porta):
            st, _, corpo = self.pedir("GET", "/api/exemplos", cabecalhos={"Host": host})
            self.assertEqual(st, 403, host)
            self.assertEqual(self.json_de(corpo)["erro"], "host_recusado")
        st, _, _ = self.pedir("GET", "/api/exemplos", cabecalhos={"Host": f"localhost:{self.porta}"})
        self.assertEqual(st, 200)

    # ------------------------------------------------------------ CSRF (revisão de camadas 2026-10-20)
    def test_csrf_origem_de_outro_site_recusada(self):
        self.api("POST", "/api/modelo/novo", {"nome": "Original"})
        for origem in ("http://exemplo.com", f"http://127.0.0.1:{self.porta + 1}", "null", f"https://127.0.0.1:{self.porta}"):
            st, _, corpo = self.pedir("POST", "/api/modelo/novo", b'{"nome": "Atacado"}', {"Origin": origem})
            self.assertEqual(st, 403, origem)
            self.assertEqual(self.json_de(corpo)["erro"], "origem_recusada")
        self.assertEqual(self.api("GET", "/api/modelo")["nome"], "Original")   # nada mudou

    def test_csrf_post_sem_origin_recusado_get_sem_origin_aceito(self):
        st, _, corpo = self.pedir("POST", "/api/desfazer", None, {"Origin": None})
        self.assertEqual(st, 403)
        self.assertEqual(self.json_de(corpo)["erro"], "origem_recusada")
        self.assertEqual(self.pedir("GET", "/api/versao", None, {"Origin": None})[0], 200)

    def test_csrf_origens_proprias_aceitas(self):
        for origem in (f"http://127.0.0.1:{self.porta}", f"http://localhost:{self.porta}"):
            st, _, _ = self.pedir("POST", "/api/modelo/novo", b'{"nome": "ok"}', {"Origin": origem})
            self.assertEqual(st, 200, origem)

    def test_csrf_sec_fetch_site_cross_site_recusado(self):
        for metodo, corpo in (("GET", None), ("POST", b"{}")):
            st, _, c = self.pedir(metodo, "/api/modelo" if metodo == "GET" else "/api/modelo/novo", corpo, {"Sec-Fetch-Site": "cross-site"})
            self.assertEqual(st, 403, metodo)
            self.assertEqual(self.json_de(c)["erro"], "origem_recusada")
        self.assertEqual(self.pedir("GET", "/api/versao", None, {"Sec-Fetch-Site": "same-origin"})[0], 200)

    def test_post_sem_content_type_json_415(self):
        for tipo in (None, "text/plain", "application/x-www-form-urlencoded", "multipart/form-data; boundary=x"):
            st, _, corpo = self.pedir("POST", "/api/modelo/novo", b'{"nome": "x"}', {"Content-Type": tipo})
            self.assertEqual(st, 415, tipo)
            self.assertEqual(self.json_de(corpo)["erro"], "tipo_nao_suportado")
        self.assertEqual(self.pedir("POST", "/api/modelo/novo", b'{"nome": "x"}', {"Content-Type": "application/json; charset=utf-8"})[0], 200)

    def test_json_malformado_400(self):
        st, cab, corpo = self.pedir("POST", "/api/calcular", corpo=b"{nao e json",
                                    cabecalhos={"Content-Type": "application/json"})
        self.assertEqual(st, 400)
        self.assertEqual(self.json_de(corpo)["erro"], "json_invalido")
        self.assertIn("no-store", cab["Cache-Control"])

    def test_corpo_que_nao_e_objeto_400(self):
        d = self.api("POST", "/api/modelo/no", [1, 2, 3], status=400)
        self.assertEqual(d["erro"], "json_invalido")

    def test_rota_desconhecida_404_json(self):
        st, cab, corpo = self.pedir("GET", "/api/nao-existe")
        self.assertEqual(st, 404)
        self.assertTrue(cab["Content-Type"].startswith("application/json"))
        d = self.json_de(corpo)
        self.assertEqual(d["erro"], "nao_encontrado")
        self.assertNotIn("Traceback", d["detalhe"])

    def test_corpo_maior_que_10MB_413(self):
        c = http.client.HTTPConnection("127.0.0.1", self.porta, timeout=10)
        try:  # só cabeçalhos: o servidor deve recusar sem ler o corpo
            c.putrequest("PUT", "/api/modelo")
            c.putheader("Content-Type", "application/json")
            c.putheader("Origin", f"http://127.0.0.1:{self.porta}")
            c.putheader("Content-Length", str(10 * 1024 * 1024 + 1))
            c.endheaders()
            r = c.getresponse()
            self.assertEqual(r.status, 413)
            self.assertEqual(json.loads(r.read())["erro"], "corpo_grande")
        finally:
            c.close()

    def test_geometria_json(self):
        st, cab, corpo = self.pedir("GET", "/api/geometria")
        self.assertEqual(st, 200)
        self.assertIn("no-store", cab["Cache-Control"])
        d = self.json_de(corpo)
        for chave in ("nos", "barras", "cargas", "pavimentos", "caixa"):
            self.assertIn(chave, d)

    def test_parametro_numerico_invalido_422(self):
        d = self.api("GET", "/api/modal?n=abc", status=422)
        self.assertEqual(d["erro"], "entrada_invalida")


class TestModeloAPI(BaseWeb):
    """Edição do modelo pela API (ESPEC §6.2 ops 5–9, §9)."""

    def portico_minimo(self):
        self.api("POST", "/api/modelo/novo", {"nome": "teste"})
        n1 = self.api("POST", "/api/modelo/no", {"x": 0, "y": 0, "z": 0, "apoio": "engaste"})
        n2 = self.api("POST", "/api/modelo/no", {"x": 0, "y": 0, "z": 3})
        self.api("POST", "/api/modelo/material", {"aco": "A-572 GR.50"})
        sec = self.api("POST", "/api/modelo/secao", {"perfil": "W310x38.7"})
        b = self.api("POST", "/api/modelo/barra", {"ni": n1["id"], "nj": n2["id"], "secao": sec["nome"], "material": "A-572 GR.50"})
        return n1, n2, sec, b

    def test_crud_no_barra_secao_material_caso_carga(self):
        n1, n2, sec, b = self.portico_minimo()
        self.assertTrue(n1["id"] and n2["id"] and n1["id"] != n2["id"])  # ids gerados pelo projeto
        self.assertEqual(b["ni"], n1["id"])
        self.api("POST", "/api/modelo/caso", {"nome": "Q1", "tipo": "Q"})
        c = self.api("POST", "/api/modelo/carga_nodal", {"caso": "Q1", "no": n2["id"], "F": [5, 0, -10, 0, 0, 0]})
        q = self.api("POST", "/api/modelo/carga_distribuida", {"caso": "Q1", "barra": b["id"], "q": [0, 0, -2]})
        p = self.api("POST", "/api/modelo/carga_pontual", {"caso": "Q1", "barra": b["id"], "a": 1.5, "F": [0, 1, 0]})
        self.assertTrue(c["id"] and q["id"] and p["id"])
        ed = self.api("PATCH", f"/api/modelo/no/{n2['id']}", {"z": 3.5})
        self.assertEqual(ed["z"], 3.5)
        self.assertEqual(ed["id"], n2["id"])
        self.assertEqual(self.api("PATCH", f"/api/modelo/barra/{b['id']}", {"rot": 90})["rot"], 90)
        self.assertEqual(self.api("PATCH", f"/api/modelo/secao/{quote(sec['nome'])}", {}, status=422)["erro"], "entrada_invalida")
        nos = self.api("GET", "/api/modelo/no")
        self.assertEqual({n["id"] for n in nos}, {n1["id"], n2["id"]})
        cargas = self.api("GET", "/api/modelo/carga_nodal?caso=Q1")
        self.assertEqual(len(cargas), 1)
        # mesmo resultado que o projeto direto
        m = projeto.de_dict(self.api("GET", "/api/modelo"))
        self.assertQuase(self.api("GET", "/api/geometria"), projeto.geometria(m))
        self.assertQuase(self.api("GET", "/api/resumo"), projeto.resumo(m))

    def test_erros_de_entrada_e_nao_encontrado(self):
        self.portico_minimo()
        d = self.api("POST", "/api/modelo/barra", {"ni": "n1", "nj": "nX", "secao": "?", "material": "?"}, status=422)
        self.assertEqual(d["erro"], "entrada_invalida")
        self.assertTrue(d["erros"] and all("campo" in e and "mensagem" in e for e in d["erros"]))
        self.assertEqual(self.api("PATCH", "/api/modelo/no/nao_existe", {"x": 1}, status=404)["erro"], "nao_encontrado")
        self.assertEqual(self.api("POST", "/api/modelo/tipo_maluco", {}, status=422)["erro"], "entrada_invalida")

    def test_remover_com_dependentes_e_cascata(self):
        n1, _, _, b = self.portico_minimo()
        d = self.api("DELETE", f"/api/modelo/no/{n1['id']}", status=422)
        self.assertEqual(d["erro"], "entrada_invalida")
        self.assertIn(b["id"], json.dumps(d, ensure_ascii=False))  # lista os dependentes
        r = self.api("DELETE", f"/api/modelo/no/{n1['id']}?cascata=1")
        self.assertIn({"tipo": "barra", "id": b["id"]}, r["removidos"])
        self.assertEqual(self.api("GET", "/api/modelo/barra"), [])

    def test_lote_atomico(self):
        self.api("POST", "/api/modelo/novo", {})
        r = self.api("POST", "/api/modelo/lote", {"itens": [{"tipo": "no", "dados": {"id": "a", "x": 0, "y": 0, "z": 0}},
                                                            {"tipo": "no", "dados": {"id": "b", "x": 1, "y": 0, "z": 0}}]})
        self.assertEqual([c["id"] for c in r["criados"]], ["a", "b"])
        d = self.api("POST", "/api/modelo/lote", {"itens": [{"tipo": "no", "dados": {"id": "c", "x": 0, "y": 0, "z": 0}},
                                                            {"tipo": "no", "dados": {"id": "d", "x": "zz", "y": 0, "z": 0}}]}, status=422)
        self.assertTrue(any(e["campo"].startswith("itens[1]") for e in d["erros"]), d)
        self.assertEqual(sorted(n["id"] for n in self.api("GET", "/api/modelo/no")), ["a", "b"])  # nada criado

    def test_historico_limpar(self):
        self.api("POST", "/api/modelo/novo", {})
        self.api("POST", "/api/modelo/no", {"id": "a", "x": 0, "y": 0, "z": 0})
        self.api("POST", "/api/modelo/no", {"id": "b", "x": 1, "y": 0, "z": 0})
        self.api("POST", "/api/desfazer", {})                                  # enche o refazer
        self.api("POST", "/api/historico/limpar", {})
        self.assertEqual(self.api("POST", "/api/desfazer", status=409)["erro"], "pilha_vazia")
        self.assertEqual(self.api("POST", "/api/refazer", status=409)["erro"], "pilha_vazia")
        self.assertEqual([n["id"] for n in self.api("GET", "/api/modelo/no")], ["a"])

    def test_historico_limpar_descarta_calculo(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        self.api("POST", "/api/calcular", {})
        self.api("POST", "/api/historico/limpar", {})
        self.assertEqual(self.api("GET", "/api/verificacao", status=409)["erro"], "sem_calculo")

    def test_desfazer_refazer(self):
        self.api("POST", "/api/modelo/novo", {})
        self.assertEqual(self.api("POST", "/api/desfazer", status=409)["erro"], "pilha_vazia")
        self.api("POST", "/api/modelo/no", {"id": "a", "x": 0, "y": 0, "z": 0})
        self.api("POST", "/api/modelo/no", {"id": "b", "x": 1, "y": 0, "z": 0})
        self.api("POST", "/api/desfazer")
        self.assertEqual(list(self.api("GET", "/api/modelo")["nos"]), ["a"])
        self.api("POST", "/api/refazer")
        self.assertEqual(sorted(self.api("GET", "/api/modelo")["nos"]), ["a", "b"])
        self.api("POST", "/api/desfazer")
        self.api("POST", "/api/modelo/no", {"id": "c", "x": 2, "y": 0, "z": 0})  # nova edição limpa o refazer
        self.assertEqual(self.api("POST", "/api/refazer", status=409)["erro"], "pilha_vazia")
        # edição que falha não entra na pilha
        self.api("POST", "/api/modelo/no", {"id": "a", "x": 0, "y": 0, "z": 0}, status=422)
        self.api("POST", "/api/desfazer")
        self.assertEqual(list(self.api("GET", "/api/modelo")["nos"]), ["a"])

    def test_pilha_limitada_a_50(self):
        self.api("POST", "/api/modelo/novo", {})
        for i in range(55):
            self.api("POST", "/api/modelo/no", {"id": f"n{i}", "x": i, "y": 0, "z": 0})
        for _ in range(50):
            self.api("POST", "/api/desfazer")
        self.api("POST", "/api/desfazer", status=409)
        self.assertEqual(len(self.api("GET", "/api/modelo")["nos"]), 5)

    def test_gerar_estrutura_e_config(self):
        self.api("POST", "/api/modelo/novo", {})
        r = self.api("POST", "/api/gerar-estrutura", {"tipo": "portico_plano", "material": {"aco": "A-572 GR.50"},
                                                     "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W310x38.7"}},
                                                     "vaos_x": [5, 5], "alturas": [3], "apoio": "engaste"})
        self.assertEqual(r["nos_criados"], 6)
        self.assertEqual(r["barras_criadas"], 5)
        cfg = self.api("PATCH", "/api/config", {"pdelta": True})
        self.assertTrue(cfg["pdelta"])
        self.assertEqual(self.api("PATCH", "/api/config", {"chave_maluca": 1}, status=422)["erro"], "entrada_invalida")

    def test_trelica_previa_e_um_desfazer(self):
        self.api("POST", "/api/modelo/novo", {})
        t = {"tipo": "trelica_plana", "material": {"aco": "A-572 GR.50"}, "secoes": {"banzo": "W150x13", "diagonal": "W150x13"},
             "vao": 12, "altura": 1.5, "paineis": 6}
        p = self.api("POST", "/api/gerar-estrutura/previa?vazio=1", t)
        self.assertEqual(len(p["barras"]), 25)
        self.assertEqual(self.api("GET", "/api/modelo")["nos"], {})  # prévia não muda o modelo
        self.api("POST", "/api/gerar-estrutura", t)
        self.assertEqual(len(self.api("GET", "/api/modelo")["barras"]), 25)
        self.api("POST", "/api/desfazer")
        self.assertEqual(self.api("GET", "/api/modelo")["barras"], {})

    def test_gerar_acoes_igual_ao_projeto(self):
        self.api("POST", "/api/exemplos/edificio_concreto")
        r = self.api("POST", "/api/acoes/gerar")
        m = projeto.abrir_exemplo("edificio_concreto")
        self.assertQuase(r, projeto.gerar_acoes(m))
        self.assertQuase(self.api("GET", "/api/combinacoes"), projeto.combinacoes(m))

    def test_salvar_abrir_arquivos_e_caminho_seguro(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        self.api("POST", "/api/calcular", {})  # relatório exige cálculo; aqui só o caminho é testado
        s = self.api("POST", "/api/modelo/salvar", {"caminho": "pasta/viga.json"})
        self.assertTrue((self.pasta / "pasta" / "viga.json").is_file())
        self.assertGreater(s["bytes"], 100)
        self.assertIn("pasta/viga.json", self.api("GET", "/api/arquivos"))
        self.api("POST", "/api/modelo/novo", {})
        m = self.api("POST", "/api/modelo/abrir", {"caminho": "pasta/viga.json"})
        self.assertIn("b1", m["barras"])
        self.api("POST", "/api/calcular", {})
        for ruim in ("../fora.json", "pasta/../../fora.json", str(Path(self.tmp.name).parent / "fora.json")):
            d = self.api("POST", "/api/modelo/salvar", {"caminho": ruim}, status=422)
            self.assertEqual(d["erro"], "entrada_invalida", ruim)
            self.api("POST", "/api/relatorio", {"formato": "html", "caminho": ruim}, status=422)
        self.assertFalse((self.pasta.parent / "fora.json").exists())

    def test_arquivos_por_extensao_e_versao(self):
        # ficha 024: o diálogo Abrir lista as obras .ed3 da pasta de trabalho; o rodapé mostra a versão do pacote
        (self.pasta / "obras").mkdir()
        (self.pasta / "obras" / "nave.ed3").write_bytes(b"\x00")
        (self.pasta / "m.json").write_text("{}", encoding="utf-8")
        self.assertEqual(self.api("GET", "/api/arquivos?ext=ed3"), ["obras/nave.ed3"])
        self.assertEqual(self.api("GET", "/api/arquivos"), ["m.json"])
        self.assertEqual(self.api("GET", "/api/arquivos?ext=py", status=422)["erro"], "entrada_invalida")
        import prossen_estrutural
        self.assertEqual(self.api("GET", "/api/versao"), {"versao": prossen_estrutural.__version__})

    def test_modulo_ausente_vira_501(self):
        from unittest import mock
        with mock.patch.object(projeto, "exemplos", side_effect=NotImplementedError("módulo ainda não disponível")):
            self.assertEqual(self.api("GET", "/api/exemplos", status=501)["erro"], "nao_implementado")

    def test_exemplos_catalogo_materiais_normas(self):
        nomes = {e["nome"] for e in self.api("GET", "/api/exemplos")}
        self.assertTrue({"viga_biapoiada", "portico_aco", "edificio_concreto", "galpao_aco"} <= nomes)
        self.assertEqual(self.api("POST", "/api/exemplos/nao_existe", status=404)["erro"], "nao_encontrado")
        cat = self.api("GET", "/api/catalogo?filtro=W310&familia=W")
        self.assertTrue(cat and all("W310" in c["nome"] for c in cat))
        self.assertQuase(self.api("GET", "/api/catalogo/W310x38.7"), projeto.perfil("W310x38.7"))
        self.assertQuase(self.api("GET", "/api/materiais"), projeto.materiais_disponiveis())
        self.assertTrue(self.api("GET", "/api/normas"))

    def test_ligacoes(self):
        tipos = self.api("GET", "/api/ligacoes/tipos")
        self.assertEqual({t["tipo"] for t in tipos} >= {"parafuso", "solda_filete", "chapa_simples", "ligacao_tracao"}, True)
        dados = {"h": 0.23, "t": 0.0095, "d_mm": 20, "tipo_parafuso": "A325", "n": 3, "s": 0.075, "tw": 0.0058, "e": 0.06,
                 "aco_chapa": "A-36", "aco_alma": "A-572 GR.50"}
        r = self.api("POST", "/api/ligacoes/verificar", {"tipo": "chapa_simples", "dados": dados, "esforcos": {"V": 120}})
        self.assertQuase(r, projeto.verificar_ligacao("chapa_simples", dados, {"V": 120}))
        self.assertTrue(r["verificacoes"] and all("item" in v for v in r["verificacoes"]))
        self.assertEqual(self.api("POST", "/api/ligacoes/verificar", {"tipo": "chapa_simples", "dados": {}, "esforcos": {}},
                                  status=422)["erro"], "entrada_invalida")


class TestCalculoAPI(BaseWeb):
    """Cálculo, resultados, verificação, estabilidade e relatório (ops 17–24, 29)."""

    def test_grupos_de_ligacoes(self):
        """Identificar/editar grupos empilham desfazer mas não descartam o cálculo; dimensionar exige cálculo."""
        self.api("POST", "/api/exemplos/portico_aco")
        self.api("POST", "/api/calcular", {})
        r = self.api("POST", "/api/ligacoes/identificar")
        self.assertGreater(r["grupos"], 0)
        self.assertIsNotNone(self.api("GET", "/api/calculo"))
        gr = self.api("GET", "/api/ligacoes/grupos")
        base = next(g for g in gr if g["tipo"] == "base")
        self.assertIsNotNone(base["taxa_max"])
        self.api("POST", "/api/ligacoes/grupos/editar", {"acao": "renomear", "grupo": base["id"], "nome": "Bases"})
        self.assertEqual(self.api("GET", "/api/modelo")["ligacoes"]["grupos"][base["id"]]["nome"], "Bases")
        d = self.api("POST", f"/api/ligacoes/grupos/{base['id']}/dimensionar")
        self.assertEqual(d["taxa_max"], max(x["taxa"] for x in d["membros"]))
        self.assertEqual(self.api("POST", "/api/ligacoes/grupos/editar", {"acao": "renomear", "grupo": "X9", "nome": "a"},
                                  status=404)["erro"], "nao_encontrado")
        self.api("POST", "/api/desfazer")  # desfaz o renomear
        self.assertNotEqual(self.api("GET", "/api/modelo")["ligacoes"]["grupos"][base["id"]]["nome"], "Bases")
        self.assertEqual(self.api("POST", f"/api/ligacoes/grupos/{base['id']}/dimensionar", status=409)["erro"],
                         "sem_calculo")

    def test_placa_base(self):
        """/api/placa_base: método NBR na pior combinação ELU de todos os membros, nos dois eixos; igual ao projeto
        chamado direto; sem cálculo → 409; com esforços explícitos não precisa de cálculo; dimensionar acha fixação."""
        self.api("POST", "/api/exemplos/galpao_aco")
        bases = [{"barra": "P1-0-0", "ponta": "i"}, {"barra": "P1-0-1", "ponta": "i"}]
        corpo = {"dados": {"pelo_perfil": True}, "membros": bases}
        self.assertEqual(self.api("POST", "/api/placa_base/verificar", corpo, status=409)["erro"], "sem_calculo")
        self.api("POST", "/api/calcular", {})
        r = self.api("POST", "/api/placa_base/verificar", dict(corpo, variantes=[{"pelo_perfil": True, "diametro_mm": 36}]))
        m = projeto.abrir_exemplo("galpao_aco")
        self.assertQuase(r, projeto.placa_base_verificar(m, projeto.calcular(m), dict(corpo, variantes=[{"pelo_perfil": True, "diametro_mm": 36}])))
        # governante = maior taxa entre todas as combinações ELU × membros × eixos
        self.assertEqual(r["taxa_max"], max(x["taxa"] for x in r["combinacoes"]))
        self.assertEqual(len(r["combinacoes"]), 2 * len([c for c in self.api("GET", "/api/calculo")["combinacoes"] if c["estado"].startswith("ELU")]))
        self.assertEqual(set(r["biaxial"]["eixos"]), {"forte (My)", "fraco (Mz)"})
        self.assertEqual(r["resultado"]["eixo"], r["governante"]["eixo"])
        self.assertIn("bloco retangular", r["resultado"]["metodo"])
        self.assertEqual({"fixacao", "chapa", "solda"}, set(r["passos"]))
        self.assertGreater(r["passos"]["chapa"]["t_nec"], 0)
        self.assertEqual(len(r["variantes"]), 1)
        # esforços explícitos (sem membros): caso do guincho da docs/v3/PLACA-BASE-V2.md §4 (WB 5/8", N = −2,99, V = 1,81, M = −2,37)
        e = self.api("POST", "/api/placa_base/verificar", {"dados": {"H": .25, "B": .25, "esp": .0127, "borda": .04, "chumbador": "WB 58500"},
                                                         "esforcos": {"N": -2.99, "V": 1.81, "M": -2.37}, "secao": "W360x51"})
        self.assertEqual(e["governante"]["combinacao"], "caso 1")
        self.assertGreater(e["resultado"]["forcas"]["Tmax"], 0)
        d = self.api("POST", "/api/placa_base/dimensionar", corpo)
        self.assertTrue(d["achou"], d["descricao"])
        self.assertTrue(d["verificacao"]["passos"]["fixacao"]["ok"])
        self.assertGreaterEqual(d["dados"]["esp"] + 1e-12, d["verificacao"]["passos"]["chapa"]["t_nec"])
        # gravar: fonte única = projeto do grupo de ligações; um membro só sai do grupo; grupo_inteiro vale para todos
        mb = {"no": "N0-0-0", "barra": "P1-0-0", "ponta": "i"}
        g1 = self.api("POST", "/api/placa_base/gravar", {"membros": [mb], "dados": d["dados"]})["grupo"]
        gr = self.api("GET", "/api/modelo")["ligacoes"]["grupos"]
        self.assertEqual(gr[g1]["membros"], [mb])
        self.assertEqual(gr[g1]["projeto"], {"tipo_ligacao": "placa_base", "dados": d["dados"]})
        self.assertIsNotNone(self.api("GET", "/api/calculo"))  # gravar não descarta o cálculo
        outro = {"no": "N0-0-1", "barra": "P1-0-1", "ponta": "i"}
        g2 = self.api("POST", "/api/placa_base/gravar", {"membros": [mb, outro], "dados": {"pelo_perfil": True}, "grupo_inteiro": True})["grupo"]
        self.assertEqual(g2, g1)
        self.assertIn(outro, self.api("GET", "/api/modelo")["ligacoes"]["grupos"][g1]["membros"])

    def test_sem_calculo(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        self.assertIsNone(self.api("GET", "/api/calculo"))  # 200 + null: a interface não loga erro
        self.assertEqual(self.api("GET", "/api/verificacao", status=409)["erro"], "sem_calculo")
        self.assertEqual(self.api("GET", "/api/resultados?alvo=esforcos", status=409)["erro"], "sem_calculo")

    def test_fluxo_viga_igual_ao_projeto(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        calc = self.api("POST", "/api/calcular", {})
        m = projeto.abrir_exemplo("viga_biapoiada")
        c = projeto.calcular(m)
        esperado = c.to_dict()
        for chave in ("verificacao", "deslocamentos", "estabilidade", "reacoes_envoltoria", "combinacoes", "avisos"):
            self.assertQuase(calc[chave], esperado[chave], onde=chave)
        self.assertAlmostEqual(calc["resumo"]["taxa_max"], esperado["resumo"]["taxa_max"], delta=1e-9)
        self.assertQuase(self.api("GET", "/api/calculo")["verificacao"], esperado["verificacao"])
        # resultados: envoltória e uma combinação
        self.assertQuase(self.api("GET", "/api/resultados?alvo=esforcos"), projeto.resultados(c, "esforcos"))
        self.assertQuase(self.api("GET", "/api/resultados?alvo=reacoes&ids=n1"), projeto.resultados(c, "reacoes", ["n1"]))
        nome = "ELU Q1 princ."
        self.assertQuase(self.api("GET", f"/api/resultados?alvo=esforcos&combinacao={quote(nome)}"),
                         projeto.resultados(c, "esforcos", combinacao=nome))
        self.assertEqual(self.api("GET", "/api/resultados?alvo=esforcos&combinacao=nada", status=404)["erro"], "nao_encontrado")
        # verificação, detalhe da barra e estabilidade
        self.assertQuase(self.api("GET", "/api/verificacao?reprovadas=1"), projeto.verificacao(c, {"reprovadas": True}))
        self.assertQuase(self.api("GET", "/api/verificacao/b1"), projeto.verificacao_barra(c, "b1"))
        self.assertEqual(self.api("GET", "/api/verificacao/bX", status=404)["erro"], "nao_encontrado")
        self.assertQuase(self.api("GET", "/api/estabilidade"), projeto.estabilidade(c))
        # caso simples Q1: M_max = qL²/8 = 45 kN·m
        q1 = self.api("GET", "/api/analise/caso/Q1")
        self.assertAlmostEqual(max(abs(v) for v in q1["esforcos"]["b1"]["My"]), 45.0, delta=1e-6)
        self.assertQuase(q1, projeto.analisar_caso(m, "Q1"))

    def test_estabilidade_portico(self):
        self.api("POST", "/api/exemplos/portico_aco")
        self.api("POST", "/api/calcular", {})
        c = projeto.calcular(projeto.abrir_exemplo("portico_aco"))
        self.assertQuase(self.api("GET", "/api/estabilidade"), projeto.estabilidade(c))

    def test_mutacao_e_desfazer_zeram_o_calculo(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        self.api("POST", "/api/calcular", {})
        self.api("PATCH", "/api/modelo/no/n2", {"x": 5})
        self.assertIsNone(self.api("GET", "/api/calculo"))
        self.api("POST", "/api/calcular", {})
        self.api("POST", "/api/desfazer")
        self.assertIsNone(self.api("GET", "/api/calculo"))
        self.api("POST", "/api/calcular", {})
        self.api("PATCH", "/api/modelo/no/nX", {"x": 5}, status=404)  # edição que falha mantém o cálculo
        self.assertIsNotNone(self.api("GET", "/api/calculo"))

    def test_calcular_com_erro_de_validacao_422(self):
        self.api("POST", "/api/modelo/novo", {})
        self.api("POST", "/api/modelo/no", {"x": 0, "y": 0, "z": 0})
        d = self.api("POST", "/api/calcular", {}, status=422)
        self.assertIn(d["erro"], ("entrada_invalida", "mecanismo"))

    def test_relatorio(self):
        self.api("POST", "/api/exemplos/viga_biapoiada")
        self.api("GET", "/api/relatorio.html", status=409)
        self.api("POST", "/api/calcular", {})
        st, cab, corpo = self.pedir("GET", "/api/relatorio.html")
        self.assertEqual(st, 200)
        self.assertTrue(cab["Content-Type"].startswith("text/html"))
        self.assertIn(b"<html", corpo.lower())
        r = self.api("POST", "/api/relatorio", {"formato": "md", "caminho": "rel/viga.md"})
        self.assertTrue((self.pasta / "rel" / "viga.md").is_file())
        self.assertGreater(r["bytes"], 100)

    def test_modal(self):
        self.api("POST", "/api/exemplos/portico_aco")
        r = self.api("GET", "/api/modal?n=2")
        self.assertQuase(r, projeto.modal(projeto.abrir_exemplo("portico_aco"), 2), tol=1e-7)


DADOS_CYPE = Path(__file__).resolve().parent / "dados_cype"
T4 = "PIPERACK TESTE 4.ed3"  # pórtico plano de 4 barras; resultados do CYPE válidos


class TestCypeAPI(BaseWeb):
    """Importação e conferência de obras do CYPE 3D (.ed3) pela API."""

    def b64(self, nome=T4):
        return base64.b64encode((DADOS_CYPE / nome).read_bytes()).decode("ascii")

    def test_importar_upload_vira_modelo_atual_com_avisos_e_desfaz(self):
        self.api("POST", "/api/modelo/no", {"id": "n1", "x": 1, "y": 2, "z": 3})
        r = self.api("POST", "/api/importar_cype", {"nome": "t4.ed3", "conteudo_base64": self.b64()})
        self.assertEqual(len(r["modelo"]["barras"]), 4)
        self.assertIsInstance(r["avisos"], list)
        self.assertEqual(self.api("GET", "/api/modelo"), r["modelo"])
        self.api("POST", "/api/desfazer")  # a importação entra na pilha: volta o modelo de antes
        self.assertEqual(list(self.api("GET", "/api/modelo")["nos"]), ["n1"])
        self.api("POST", "/api/refazer")
        self.assertEqual(len(self.api("GET", "/api/modelo")["barras"]), 4)

    def test_importar_por_caminho_na_pasta_de_trabalho(self):
        (self.pasta / "obras").mkdir()
        (self.pasta / "obras" / "t4.ed3").write_bytes((DADOS_CYPE / T4).read_bytes())
        r = self.api("POST", "/api/importar_cype", {"caminho": "obras/t4.ed3"})
        self.assertEqual(len(r["modelo"]["barras"]), 4)

    def test_importar_erros_de_entrada_com_campo_e_modelo_intacto(self):
        self.api("POST", "/api/modelo/no", {"id": "n1", "x": 0, "y": 0, "z": 0})
        casos = [({"caminho": "../fora.ed3"}, "caminho"), ({"caminho": "nao_existe.ed3"}, "caminho"),
                 ({"caminho": "C:/Windows/x.ed3"}, "caminho"), ({}, "caminho"), ({"caminho": 3}, "caminho"),
                 ({"nome": "x.ed3", "conteudo_base64": "@@@"}, "conteudo_base64"),
                 ({"nome": "x.ed3", "conteudo_base64": base64.b64encode(b"nao e CYPE").decode()}, "conteudo")]
        for corpo, campo in casos:
            with self.subTest(corpo=str(corpo)[:60]):
                d = self.api("POST", "/api/importar_cype", corpo, status=422)
                self.assertEqual(d["erro"], "entrada_invalida")
                self.assertEqual(d["erros"][0]["campo"], campo)
        self.assertEqual(list(self.api("GET", "/api/modelo")["nos"]), ["n1"])

    def test_comparar_cype_nao_troca_o_modelo_atual(self):
        self.api("POST", "/api/modelo/no", {"id": "n1", "x": 1, "y": 2, "z": 3})
        r = self.api("POST", "/api/comparar_cype", {"nome": "t4.ed3", "conteudo_base64": self.b64()})
        self.assertTrue(r["valido"])
        lin = {(l["hipotese"], l["grandeza"]): l for l in r["tabela"]}
        self.assertLess(abs(lin[("PP", "reacao_total_Fz")]["dif_pct"]), 0.5)
        self.assertEqual(len(r["taxas"]), 4)
        self.assertEqual(list(self.api("GET", "/api/modelo")["nos"]), ["n1"])
        (self.pasta / "t4.ed3").write_bytes((DADOS_CYPE / T4).read_bytes())
        self.assertQuase(self.api("POST", "/api/comparar_cype", {"caminho": "t4.ed3"}), {**r, "arquivo": "t4.ed3"})
        self.api("POST", "/api/comparar_cype", {"caminho": "../t4.ed3"}, status=422)

    def test_limite_de_corpo_20MB_so_nas_rotas_do_cype(self):
        lixo = json.dumps({"nome": "x.ed3", "conteudo_base64": base64.b64encode(b"A" * 8_500_000).decode()})
        self.assertGreater(len(lixo), 10 * 1024 * 1024)
        d = self.api("POST", "/api/importar_cype", json.loads(lixo), status=422)  # passou do limite geral: recusa por formato
        self.assertEqual(d["erros"][0]["campo"], "conteudo")
        self.api("POST", "/api/comparar_cype", json.loads(lixo), status=422)
        for rota, esperado in (("/api/importar_cype", 413), ("/api/modelo/lote", 413)):
            c = http.client.HTTPConnection("127.0.0.1", self.porta, timeout=10)
            try:
                c.putrequest("POST", rota)
                c.putheader("Content-Type", "application/json")
                c.putheader("Origin", f"http://127.0.0.1:{self.porta}")
                c.putheader("Content-Length", str((20 if "cype" in rota else 10) * 1024 * 1024 + 1))
                c.endheaders()
                r = c.getresponse()
                self.assertEqual(r.status, esperado, rota)
                self.assertEqual(json.loads(r.read())["erro"], "corpo_grande")
            finally:
                c.close()


if __name__ == "__main__":
    unittest.main()
