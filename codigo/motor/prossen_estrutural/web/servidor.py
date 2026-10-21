"""Servidor HTTP local da interface (ESPEC §9): estáticos de `estatico/` e API JSON sobre `projeto.py`.

Só stdlib. Ligado apenas em 127.0.0.1; recusa Host diferente de 127.0.0.1:<porta> / localhost:<porta>
(proteção contra DNS rebinding). Endpoints são adaptadores finos: nenhuma fórmula de engenharia aqui.
Erros no formato da ESPEC §6.1, sem stack trace para o cliente (o log vai para stderr via `logging`).
"""
from __future__ import annotations

import base64
import binascii
import json
import logging
import re
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlsplit

from .. import __version__, projeto

log = logging.getLogger(__name__)

ESTATICO = (Path(__file__).parent / "estatico").resolve()
LIMITE_CORPO = 10 * 1024 * 1024
LIMITE_CORPO_ED3 = 20 * 1024 * 1024  # upload de obra do CYPE (base64 no JSON) em /api/importar_cype e /api/comparar_cype
LIMITE_DESFAZER = 50
MIME = {  # forçado: o registro do Windows erra .js com frequência
    ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8", ".json": "application/json; charset=utf-8", ".svg": "image/svg+xml",
    ".woff2": "font/woff2", ".ttf": "font/ttf", ".png": "image/png", ".txt": "text/plain; charset=utf-8",
    ".md": "text/plain; charset=utf-8",
}
# Nada vindo da internet: tudo do próprio servidor (inline só para o import map e estilos de componentes).
CSP = ("default-src 'self'; script-src 'self'{hashes}; style-src 'self' 'unsafe-inline'; "
       "img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-src 'self'; object-src 'none'; "
       "base-uri 'none'; form-action 'self'")
_SCRIPT_EMBUTIDO = re.compile(rb"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", re.S)


def _csp(html: bytes) -> str:
    """CSP do HTML servido: scripts só do próprio servidor + o hash de cada script embutido (o import map).
    Sem 'unsafe-inline': um atributo onerror=... vindo de dado do modelo não executa (defesa contra XSS)."""
    import hashlib
    hashes = "".join(f" 'sha256-{base64.b64encode(hashlib.sha256(m.group(1)).digest()).decode()}'" for m in _SCRIPT_EMBUTIDO.finditer(html))
    return CSP.format(hashes=hashes)


class ErroHTTP(Exception):
    def __init__(self, status: int, erro: str, detalhe: str, **extra):
        super().__init__(detalhe)
        self.status, self.corpo = status, {"erro": erro, "detalhe": detalhe, **extra}


def _erro_de_excecao(e: Exception) -> ErroHTTP:
    """Mapeia as exceções de `projeto` (ESPEC §6.1)."""
    msg = e.args[0] if e.args and isinstance(e.args[0], str) else str(e)
    if isinstance(e, projeto.ErroEntrada):
        return ErroHTTP(422, "entrada_invalida", msg, erros=getattr(e, "erros", []))
    if isinstance(e, projeto.ErroNaoEncontrado):
        return ErroHTTP(404, "nao_encontrado", msg)
    if isinstance(e, projeto.ErroMecanismo):
        return ErroHTTP(422, "mecanismo", msg)
    if isinstance(e, NotImplementedError):  # módulo opcional ainda não entregue (ex.: importador .ed3)
        return ErroHTTP(501, "nao_implementado", msg)
    log.exception("erro interno na API")
    return ErroHTTP(500, "interno", "ver log")


# ---------------------------------------------------------------- estado e rotas
def _modelo(srv):
    if srv.estado["modelo"] is None:
        srv.estado["modelo"] = projeto.novo_modelo()
    return srv.estado["modelo"]


def _mutar(srv, rota, *args):
    """Rota que muta o modelo: guarda o estado antes; se der certo, empilha (desfazer ≤ 50), limpa o refazer e
    descarta o cálculo; se falhar, restaura o modelo (nenhuma edição pela metade)."""
    antes = projeto.para_dict(_modelo(srv))
    try:
        saida = rota(srv, *args)
    except Exception:
        srv.estado["modelo"] = projeto.de_dict(antes)
        raise
    pilha = srv.estado["desfazer"]
    pilha.append(antes)
    del pilha[:-LIMITE_DESFAZER]
    srv.estado["refazer"].clear()
    srv.estado["calculo"] = None
    return saida


def _ligacoes(srv, f):
    """Grupos de ligações: empilham desfazer (se mudou) mas NÃO descartam o cálculo (não mexem na estrutura)."""
    antes = projeto.para_dict(_modelo(srv))
    try:
        saida = f(_modelo(srv))
    except Exception:
        srv.estado["modelo"] = projeto.de_dict(antes)
        raise
    if projeto.para_dict(srv.estado["modelo"]) != antes:
        srv.estado["desfazer"].append(antes)
        del srv.estado["desfazer"][:-LIMITE_DESFAZER]
        srv.estado["refazer"].clear()
    return saida


def _trocar(srv, de: str, para: str):
    origem = srv.estado[de]
    if not origem:
        raise ErroHTTP(409, "pilha_vazia", f"nada para {'desfazer' if de == 'desfazer' else 'refazer'}")
    srv.estado[para].append(projeto.para_dict(_modelo(srv)))
    srv.estado.update(modelo=projeto.de_dict(origem.pop()), calculo=None)
    return projeto.para_dict(srv.estado["modelo"])


def _calculo(srv):
    if srv.estado["calculo"] is None:
        raise ErroHTTP(409, "sem_calculo", "calcule o modelo antes (POST /api/calcular)")
    return srv.estado["calculo"]


def _calc_membros(srv, corpo):
    """Placa de base: com `membros` os esforços vêm do cálculo (409 sem cálculo); com `esforcos` não precisa."""
    return _calculo(srv) if (corpo or {}).get("membros") else srv.estado["calculo"]


def _caminho(srv, corpo):
    return projeto.caminho_seguro(srv.pasta, str((corpo or {}).get("caminho", "")))


def _bool(q, chave):
    v = q.get(chave)
    return None if v is None else v.lower() in ("1", "true", "sim")


def _int(q, chave, padrao):
    try:
        return int(q.get(chave, padrao))
    except ValueError:
        raise projeto.ErroEntrada(f"{chave}: número inteiro esperado", chave) from None


def _limpar_historico(srv):
    """N1: o WPF acabou de criar o documento e já tem o modelo do motor; começa sem desfazer/refazer/cálculo sem reabrir o arquivo."""
    srv.estado["desfazer"].clear()
    srv.estado["refazer"].clear()
    srv.estado["calculo"] = None
    return {"ok": True}


def _novo(srv, m):
    srv.estado.update(modelo=m, calculo=None)
    srv.estado["desfazer"].clear()
    srv.estado["refazer"].clear()
    return projeto.para_dict(m)


def _calcular(srv, a, q, c):
    srv.estado["calculo"] = projeto.calcular(_modelo(srv), c or None)
    return srv.estado["calculo"].to_dict()


def _origem_cype(srv, c):
    """Corpo de importar/comparar: {"caminho"} (pasta de trabalho ou pasta de projetos do CYPE, só leitura)
    ou {"nome", "conteudo_base64"} (upload). → (origem, kwargs) de `projeto.importar_cype` / `comparar_cype`."""
    c = c or {}
    if "conteudo_base64" in c:
        try:
            dados = base64.b64decode(str(c["conteudo_base64"]), validate=True)
        except (binascii.Error, ValueError):
            raise projeto.ErroEntrada("conteudo_base64: base64 inválido", "conteudo_base64") from None
        return dados, {"nome": c.get("nome")}
    if not isinstance(c.get("caminho"), str):
        raise projeto.ErroEntrada("informe 'caminho' (.ed3) ou 'nome' e 'conteudo_base64'", "caminho")
    return c["caminho"], {"pasta": srv.pasta}


def _importar_cype(srv, a, q, c):
    """Obra do CYPE 3D 2016 (.ed3) → modelo atual. Rota em MUTAM: empilha o modelo anterior (desfazer)."""
    origem, kw = _origem_cype(srv, c)
    m = projeto.importar_cype(origem, **kw)
    srv.estado["modelo"] = m
    return {"modelo": projeto.para_dict(m), "avisos": list(m.avisos)}


def _comparar_cype(srv, a, q, c):
    origem, kw = _origem_cype(srv, c)
    return projeto.comparar_cype(origem, **kw)


def _arquivos(srv, a, q, c):
    ext = q.get("ext", "json")
    if ext not in ("json", "ed3"):  # só modelos Estructto e obras do CYPE 3D
        raise projeto.ErroEntrada(f"ext: use json ou ed3 (recebido {ext!r})", "ext")
    return [str(p.relative_to(srv.pasta)).replace("\\", "/") for p in sorted(srv.pasta.rglob(f"*.{ext}"))]


# (método, padrão) -> função(servidor, args_da_rota, consulta, corpo). Rotas marcadas em MUTAM empilham desfazer.
ROTAS = {
    ("GET", r"/api/modelo"): lambda s, a, q, c: projeto.para_dict(_modelo(s)),
    ("PUT", r"/api/modelo"): lambda s, a, q, c: (s.estado.__setitem__("modelo", projeto.de_dict(c)), projeto.para_dict(s.estado["modelo"]))[1],
    ("POST", r"/api/modelo/novo"): lambda s, a, q, c: _novo(s, projeto.novo_modelo((c or {}).get("nome", ""))),
    ("POST", r"/api/modelo/abrir"): lambda s, a, q, c: _novo(s, projeto.carregar(_caminho(s, c))),
    ("POST", r"/api/importar_cype"): _importar_cype,
    ("POST", r"/api/comparar_cype"): _comparar_cype,
    ("POST", r"/api/modelo/salvar"): lambda s, a, q, c: projeto.salvar(_modelo(s), _caminho(s, c)),
    ("GET", r"/api/arquivos"): _arquivos,
    ("GET", r"/api/versao"): lambda s, a, q, c: {"versao": __version__},
    ("GET", r"/api/exemplos"): lambda s, a, q, c: projeto.exemplos(),
    ("POST", r"/api/exemplos/(?P<nome>[^/]+)"): lambda s, a, q, c: _novo(s, projeto.abrir_exemplo(a["nome"])),
    ("POST", r"/api/modelo/lote"): lambda s, a, q, c: projeto.adicionar_lote(_modelo(s), (c or {}).get("itens", [])),
    ("POST", r"/api/modelo/(?P<tipo>[a-z_]+)"): lambda s, a, q, c: projeto.adicionar(_modelo(s), a["tipo"], c or {}),
    ("GET", r"/api/modelo/(?P<tipo>[a-z_]+)"): lambda s, a, q, c: projeto.listar(_modelo(s), a["tipo"], dict(q) or None),
    ("PATCH", r"/api/modelo/(?P<tipo>[a-z_]+)/(?P<id>[^/]+)"): lambda s, a, q, c: projeto.editar(_modelo(s), a["tipo"], a["id"], c or {}),
    ("DELETE", r"/api/modelo/(?P<tipo>[a-z_]+)/(?P<id>[^/]+)"): lambda s, a, q, c: projeto.remover(_modelo(s), a["tipo"], a["id"], cascata=bool(_bool(q, "cascata"))),
    ("POST", r"/api/historico/limpar"): lambda s, a, q, c: _limpar_historico(s),
    ("POST", r"/api/desfazer"): lambda s, a, q, c: _trocar(s, "desfazer", "refazer"),
    ("POST", r"/api/refazer"): lambda s, a, q, c: _trocar(s, "refazer", "desfazer"),
    ("PATCH", r"/api/config"): lambda s, a, q, c: projeto.configurar(_modelo(s), c or {}),
    ("POST", r"/api/gerar-estrutura"): lambda s, a, q, c: projeto.gerar_estrutura(_modelo(s), c or {}),
    ("POST", r"/api/gerar-estrutura/previa"): lambda s, a, q, c: projeto.previa_estrutura(projeto.novo_modelo() if _bool(q, "vazio") else _modelo(s), c or {}),
    ("POST", r"/api/acoes/gerar"): lambda s, a, q, c: projeto.gerar_acoes(_modelo(s)),
    ("GET", r"/api/resumo"): lambda s, a, q, c: projeto.resumo(_modelo(s)),
    ("GET", r"/api/validar"): lambda s, a, q, c: projeto.validar(_modelo(s)),
    ("GET", r"/api/geometria"): lambda s, a, q, c: projeto.geometria(_modelo(s)),
    ("GET", r"/api/combinacoes"): lambda s, a, q, c: projeto.combinacoes(_modelo(s)),
    ("POST", r"/api/calcular"): _calcular,
    ("GET", r"/api/calculo"): lambda s, a, q, c: None if s.estado["calculo"] is None else s.estado["calculo"].to_dict(),  # sem cálculo: 200 null
    ("GET", r"/api/resultados"): lambda s, a, q, c: projeto.resultados(_calculo(s), q.get("alvo", ""), q["ids"].split(",") if q.get("ids") else None, q.get("combinacao") or None),
    ("GET", r"/api/analise/caso/(?P<nome>[^/]+)"): lambda s, a, q, c: projeto.analisar_caso(_modelo(s), a["nome"], bool(_bool(q, "pdelta"))),
    ("GET", r"/api/modal"): lambda s, a, q, c: projeto.modal(_modelo(s), _int(q, "n", 6)),
    ("GET", r"/api/verificacao"): lambda s, a, q, c: projeto.verificacao(_calculo(s), {k: v for k, v in (("reprovadas", _bool(q, "reprovadas")), ("tipo", q.get("tipo"))) if v is not None} or None),
    ("GET", r"/api/verificacao/(?P<barra>[^/]+)"): lambda s, a, q, c: projeto.verificacao_barra(_calculo(s), a["barra"]),
    ("GET", r"/api/estabilidade"): lambda s, a, q, c: projeto.estabilidade(_calculo(s)),
    ("GET", r"/api/ligacoes/tipos"): lambda s, a, q, c: projeto.tipos_ligacao(),
    ("POST", r"/api/ligacoes/verificar"): lambda s, a, q, c: projeto.verificar_ligacao((c or {}).get("tipo"), (c or {}).get("dados", {}), (c or {}).get("esforcos", {})),
    ("POST", r"/api/ligacoes/sugerir"): lambda s, a, q, c: projeto.sugerir_ligacao((c or {}).get("tipo"), (c or {}).get("perfis", {}), (c or {}).get("esforcos", {})),
    ("POST", r"/api/ligacoes/otimizar"): lambda s, a, q, c: projeto.otimizar_ligacao((c or {}).get("tipo"), (c or {}).get("dados", {}), (c or {}).get("esforcos", {})),
    ("POST", r"/api/ligacoes/memorial"): lambda s, a, q, c: projeto.memorial_ligacao((c or {}).get("tipo"), (c or {}).get("dados", {}), (c or {}).get("esforcos", {})),
    ("GET", r"/api/ligacoes/grupos"): lambda s, a, q, c: projeto.ligacoes_grupos(_modelo(s), s.estado["calculo"]),
    ("POST", r"/api/ligacoes/identificar"): lambda s, a, q, c: _ligacoes(s, projeto.ligacoes_identificar),
    ("POST", r"/api/ligacoes/grupos/editar"): lambda s, a, q, c: _ligacoes(s, lambda m: projeto.ligacoes_editar(m, c or {})),
    ("POST", r"/api/ligacoes/grupos/(?P<id>[^/]+)/dimensionar"): lambda s, a, q, c: projeto.ligacoes_dimensionar(_modelo(s), _calculo(s), a["id"]),
    ("GET", r"/api/placa_base/esquema"): lambda s, a, q, c: projeto.placa_base_esquema(),
    ("POST", r"/api/placa_base/verificar"): lambda s, a, q, c: projeto.placa_base_verificar(_modelo(s), _calc_membros(s, c), c or {}),
    ("POST", r"/api/placa_base/dimensionar"): lambda s, a, q, c: projeto.placa_base_dimensionar(_modelo(s), _calc_membros(s, c), c or {}),
    ("POST", r"/api/placa_base/gravar"): lambda s, a, q, c: _ligacoes(s, lambda m: projeto.placa_base_gravar(m, c or {})),
    ("GET", r"/api/catalogo"): lambda s, a, q, c: projeto.catalogo_arvore() if q.get("formato") == "arvore" else projeto.catalogo_perfis(q.get("filtro", ""), q.get("familia")),
    ("GET", r"/api/catalogo/(?P<nome>[^/]+)"): lambda s, a, q, c: projeto.perfil(a["nome"]),
    ("GET", r"/api/materiais"): lambda s, a, q, c: projeto.materiais_disponiveis(),
    ("GET", r"/api/normas"): lambda s, a, q, c: projeto.normas_implementadas(),
    ("GET", r"/api/relatorio.html"): lambda s, a, q, c: projeto.relatorio_html(_modelo(s), _calculo(s), q.get("empresa", "")),
    ("POST", r"/api/relatorio"): lambda s, a, q, c: projeto.gerar_relatorio(_modelo(s), _calculo(s), (c or {}).get("formato", "html"), _caminho(s, c), str((c or {}).get("empresa") or "")),
}
ROTAS_ED3 = {("POST", r"/api/importar_cype"), ("POST", r"/api/comparar_cype")}  # corpo até LIMITE_CORPO_ED3
MUTAM = {("PUT", r"/api/modelo"), ("POST", r"/api/importar_cype"), ("POST", r"/api/modelo/lote"), ("POST", r"/api/modelo/(?P<tipo>[a-z_]+)"),
         ("PATCH", r"/api/modelo/(?P<tipo>[a-z_]+)/(?P<id>[^/]+)"), ("DELETE", r"/api/modelo/(?P<tipo>[a-z_]+)/(?P<id>[^/]+)"),
         ("PATCH", r"/api/config"), ("POST", r"/api/gerar-estrutura"), ("POST", r"/api/acoes/gerar")}
_COMPILADAS = [(m, re.compile(p), (m, p)) for (m, p) in ROTAS]


def _achar_rota(metodo: str, caminho: str):
    for m, rx, chave in _COMPILADAS:  # ordem do dicionário: rotas fixas antes das com parâmetro
        if m == metodo and (r := rx.fullmatch(caminho)):
            return chave, {k: unquote(v) for k, v in r.groupdict().items()}
    raise ErroHTTP(404, "nao_encontrado", f"rota {metodo} {caminho} não existe")


# ---------------------------------------------------------------- handler
class Manipulador(BaseHTTPRequestHandler):
    # HTTP/1.1: conexão persistente (a interface pede ~70 arquivos; com 1.0 cada um abriria um socket e o Windows
    # esgota portas em TIME_WAIT: net::ERR_NO_BUFFER_SPACE). Toda resposta leva Content-Length.
    protocol_version = "HTTP/1.1"
    server_version = "Estructto"
    sys_version = ""

    def log_message(self, fmt, *args):
        log.debug("%s %s", self.address_string(), fmt % args)

    def _enviar(self, status: int, corpo: bytes, tipo: str, extra: dict | None = None):
        self.send_response(status)
        self.send_header("Content-Type", tipo)
        self.send_header("Content-Length", str(len(corpo)))
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(corpo)

    def _json(self, status: int, obj):
        corpo = json.dumps(obj, ensure_ascii=False, allow_nan=False).encode("utf-8")
        self._enviar(status, corpo, "application/json; charset=utf-8", {"Cache-Control": "no-store"})

    def _host_ok(self) -> bool:
        porta = self.server.server_address[1]
        return self.headers.get("Host", "").lower() in (f"127.0.0.1:{porta}", f"localhost:{porta}")

    def _origem_ok(self) -> bool:
        """CSRF: a API só atende a própria origem. Recusa `Sec-Fetch-Site: cross-site`; fora de GET/HEAD exige
        `Origin` igual a http://127.0.0.1:<porta> ou http://localhost:<porta> (a janela do app carrega dessa origem)."""
        if self.headers.get("Sec-Fetch-Site", "").lower() == "cross-site":
            return False
        origem = self.headers.get("Origin")
        if origem is None:
            return self.command in ("GET", "HEAD")
        porta = self.server.server_address[1]
        return origem in (f"http://127.0.0.1:{porta}", f"http://localhost:{porta}")

    def _tratar(self):
        try:
            if not self._host_ok():
                raise ErroHTTP(403, "host_recusado", "acesso só por 127.0.0.1 ou localhost nesta porta")
            caminho = urlsplit(self.path).path
            if caminho.startswith("/api/"):
                if not self._origem_ok():
                    raise ErroHTTP(403, "origem_recusada", "a API só aceita pedidos da própria interface (mesma origem)")
                tipo = self.headers.get("Content-Type", "").split(";")[0].strip().lower()
                if self.command in ("POST", "PUT", "PATCH") and tipo != "application/json":
                    raise ErroHTTP(415, "tipo_nao_suportado", "envie o corpo como application/json")
                self._api(caminho)
            elif self.command in ("GET", "HEAD"):
                self._estatico(caminho)
            else:
                raise ErroHTTP(405, "metodo_nao_permitido", f"{self.command} não se aplica a arquivos estáticos")
        except ErroHTTP as e:
            self.close_connection = True  # o corpo pode não ter sido lido: não reaproveitar a conexão
            self._json(e.status, e.corpo)
        except Exception:  # noqa: BLE001 — nunca devolver stack trace
            log.exception("erro interno no servidor")
            self._json(500, {"erro": "interno", "detalhe": "ver log"})

    do_GET = do_HEAD = do_POST = do_PUT = do_PATCH = do_DELETE = _tratar

    def _corpo(self, limite=LIMITE_CORPO):
        try:
            n = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            raise ErroHTTP(400, "json_invalido", "Content-Length inválido") from None
        if n < 0:
            raise ErroHTTP(400, "json_invalido", "Content-Length inválido")
        if n > limite:
            raise ErroHTTP(413, "corpo_grande", f"corpo acima de {limite // (1024 * 1024)} MB")
        if n == 0:
            return None
        try:
            corpo = json.loads(self.rfile.read(n).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as e:
            raise ErroHTTP(400, "json_invalido", str(e)) from None
        if not isinstance(corpo, dict):
            raise ErroHTTP(400, "json_invalido", "o corpo deve ser um objeto JSON")
        return corpo

    def _api(self, caminho: str):
        chave, args = _achar_rota(self.command, caminho)
        corpo = self._corpo(LIMITE_CORPO_ED3 if chave in ROTAS_ED3 else LIMITE_CORPO)
        consulta = {k: v[-1] for k, v in parse_qs(urlsplit(self.path).query).items()}
        srv = self.server
        try:
            with srv.trava:  # ponytail: uma trava global; o modelo é um só por servidor
                if chave in MUTAM:
                    saida = _mutar(srv, ROTAS[chave], args, consulta, corpo)
                else:
                    saida = ROTAS[chave](srv, args, consulta, corpo)
        except ErroHTTP:
            raise
        except Exception as e:  # noqa: BLE001
            raise _erro_de_excecao(e) from None
        if isinstance(saida, str):
            self._enviar(200, saida.encode("utf-8"), "text/html; charset=utf-8", {"Cache-Control": "no-store"})
        else:
            self._json(200, saida)

    def _estatico(self, caminho: str):
        if self.headers.get("Content-Length", "0") not in ("", "0"):
            self.close_connection = True  # corpo não lido: não reaproveitar a conexão (HTTP/1.1)
        rel = unquote(caminho).lstrip("/") or "index.html"
        partes = rel.split("/")
        if "\\" in rel or ":" in rel or "\x00" in rel or any(p in ("..", ".") for p in partes) or caminho.startswith("//"):
            raise ErroHTTP(403, "caminho_recusado", "caminho fora da pasta de arquivos estáticos")
        alvo = (ESTATICO / rel).resolve()
        if not alvo.is_relative_to(ESTATICO):
            raise ErroHTTP(403, "caminho_recusado", "caminho fora da pasta de arquivos estáticos")
        if not alvo.is_file():
            raise ErroHTTP(404, "nao_encontrado", f"arquivo {rel} não existe")
        tipo = MIME.get(alvo.suffix.lower(), "application/octet-stream")
        extra = {"Cache-Control": "no-cache"}
        dados = alvo.read_bytes()
        if tipo.startswith("text/html"):
            extra["Content-Security-Policy"] = _csp(dados)
        self._enviar(200, dados, tipo, extra)


class Servidor(ThreadingHTTPServer):
    daemon_threads = True


def criar_servidor(porta: int = 8765, pasta: str | Path | None = None) -> Servidor:
    """Cria (sem iniciar) o servidor em 127.0.0.1:<porta> (0 = porta livre). `pasta` = pasta de trabalho."""
    import threading
    srv = Servidor(("127.0.0.1", porta), Manipulador)
    srv.pasta = Path(pasta or Path(__file__).resolve().parents[2] / "trabalho").resolve()
    srv.estado = {"modelo": None, "calculo": None, "desfazer": [], "refazer": []}
    srv.trava = threading.Lock()
    return srv
