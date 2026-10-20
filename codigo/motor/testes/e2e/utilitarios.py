"""Utilitários dos testes ponta a ponta (ficha 023): caminhos, CLI em subprocesso, cliente HTTP, servidor web em
thread, parâmetros do MCP stdio, comparação numérica recursiva e checagem de JSON sem NaN/inf.

Regras do manual do engenheiro-testes: sem rede externa, sem `time.sleep` fixo (espera por condição com prazo),
portas livres (porta 0), arquivos em pasta temporária, nada depende de ordem entre testes.
"""
from __future__ import annotations

import atexit
import contextlib
import functools
import http.client
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path

SOFTWARE = Path(__file__).resolve().parents[2]
RAIZ = SOFTWARE.parent
EXEMPLOS = SOFTWARE / "exemplos"
MODELOS = Path(__file__).resolve().parent / "modelos"
DOCS_IMAGENS = SOFTWARE / "docs" / "imagens"
NAVEGADOR = Path(__file__).resolve().parent / "navegador"
if str(SOFTWARE) not in sys.path:
    sys.path.insert(0, str(SOFTWARE))

TOL = 1e-9  # tolerância relativa entre canais (ficha 023, caso 4)
NOMES_EXEMPLOS = ("viga_biapoiada", "portico_aco", "galpao_aco", "edificio_concreto")
AVISO_ART = "Ferramenta de apoio"

# ESPEC §8: as 40 ferramentas do servidor MCP
QUARENTA = frozenset((
    "modelo_novo modelo_abrir modelo_salvar modelo_exemplos modelo_abrir_exemplo modelo_exportar_json "
    "modelo_importar_json modelo_resumo modelo_validar modelo_geometria elementos_listar no_adicionar "
    "material_adicionar secao_adicionar barra_adicionar caso_adicionar carga_adicionar pavimento_adicionar "
    "lote_adicionar elemento_editar elemento_remover estrutura_gerar config_definir acoes_gerar "
    "combinacoes_listar analise_caso analise_combinacao analise_modal projeto_calcular resultados_consultar "
    "verificacao_listar verificacao_barra estabilidade_consultar ligacao_tipos ligacao_verificar perfis_listar "
    "perfil_consultar materiais_listar normas_implementadas relatorio_gerar modelo_importar_cype cype_comparar").split())

# Edifício de concreto do F2/F5 (PLANO §6.3): 3x2 vãos de 5 m, 4 pavimentos de 3 m, C30, pilar 25x50, viga 20x50
EDIFICIO_F2 = {"tipo": "edificio", "material": {"concreto": 30},
               "secoes": {"pilar": {"forma": "retangular", "b": 0.25, "h": 0.5},
                          "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
               "vaos_x": [5, 5, 5], "vaos_y": [5, 5], "alturas": [3, 3, 3, 3], "apoio": "engaste"}
VENTO_F2 = {"V0": 35, "S1": 1.0, "categoria": "IV", "classe": "B", "S3": 1.0, "dimensoes": {"lx": 15, "ly": 10}}

LENTO = os.environ.get("PROSSEN_PULAR_LENTOS") == "1"
lento = unittest.skipIf(LENTO, "teste lento (PROSSEN_PULAR_LENTOS=1)")


# ---------------------------------------------------------------- pasta temporária da sessão
@functools.lru_cache(maxsize=None)
def pasta_sessao() -> Path:
    """Pasta temporária única do processo de teste (apagada ao sair); guarda saídas reaproveitadas entre testes."""
    p = Path(tempfile.mkdtemp(prefix="prossen_e2e_"))
    atexit.register(shutil.rmtree, str(p), True)
    return p


# ---------------------------------------------------------------- JSON estrito
def _recusar(constante):
    raise ValueError(f"JSON com constante não padrão: {constante}")


def json_estrito(texto: str):
    """`json.loads` que recusa NaN, Infinity e -Infinity (JSON válido não os tem)."""
    return json.loads(texto, parse_constant=_recusar)


def nao_finitos(obj, onde="$") -> list[str]:
    """Caminhos de float NaN/inf dentro de uma estrutura JSON já carregada."""
    achados = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            achados += nao_finitos(v, f"{onde}.{k}")
    elif isinstance(obj, (list, tuple)):
        for i, v in enumerate(obj):
            achados += nao_finitos(v, f"{onde}[{i}]")
    elif isinstance(obj, float) and not math.isfinite(obj):
        achados.append(onde)
    return achados


def diferencas(a, b, tol=TOL, onde="$", ignorar=(), limite=12) -> list[str]:
    """Diferenças entre duas estruturas JSON, com tolerância relativa `tol` nos números. Chaves em `ignorar`
    (nome simples, em qualquer nível) são puladas. Lista vazia = iguais."""
    saida: list[str] = []

    def rec(x, y, caminho):
        if len(saida) >= limite:
            return
        if isinstance(x, dict) and isinstance(y, dict):
            ks = (set(x) | set(y)) - set(ignorar)
            for k in sorted(ks, key=str):
                if k not in x or k not in y:
                    saida.append(f"{caminho}.{k}: ausente em {'primeiro' if k not in x else 'segundo'}")
                else:
                    rec(x[k], y[k], f"{caminho}.{k}")
        elif isinstance(x, (list, tuple)) and isinstance(y, (list, tuple)):
            if len(x) != len(y):
                saida.append(f"{caminho}: tamanhos {len(x)} != {len(y)}")
                return
            for i, (u, v) in enumerate(zip(x, y)):
                rec(u, v, f"{caminho}[{i}]")
        elif isinstance(x, bool) or isinstance(y, bool) or x is None or y is None:
            if x is not y and x != y:
                saida.append(f"{caminho}: {x!r} != {y!r}")
        elif isinstance(x, (int, float)) and isinstance(y, (int, float)):
            if not (x == y or abs(x - y) <= tol * max(1.0, abs(x), abs(y))):
                saida.append(f"{caminho}: {x!r} != {y!r}")
        elif x != y:
            saida.append(f"{caminho}: {x!r} != {y!r}")
    rec(a, b, onde)
    return saida


# ---------------------------------------------------------------- CLI
def ambiente() -> dict:
    """Ambiente de subprocesso: PYTHONPATH absoluto para `software/` e saída em UTF-8."""
    return {**os.environ, "PYTHONPATH": str(SOFTWARE), "PYTHONIOENCODING": "utf-8", "PYTHONUTF8": "1"}


def rodar_cli(*args: str, timeout=300) -> subprocess.CompletedProcess:
    """`python -m prossen_estrutural <args>` em subprocesso (cwd = raiz do projeto, que tem espaços duplos)."""
    return subprocess.run([sys.executable, "-m", "prossen_estrutural", *map(str, args)], capture_output=True,
                          text=True, encoding="utf-8", errors="replace", env=ambiente(), cwd=str(RAIZ), timeout=timeout)


def cli_em_processo(*args: str):
    """Mesma CLI chamada em processo (`__main__.main`), para os casos de erro: evita ~1,5 s de partida por caso.
    Devolve objeto com `returncode`, `stdout`, `stderr`; `SystemExit` do argparse vira `returncode`."""
    import io
    import types
    from prossen_estrutural.__main__ import main

    def fluxo():
        return io.TextIOWrapper(io.BytesIO(), encoding="utf-8", write_through=True)
    out, err = fluxo(), fluxo()
    antigo = sys.stdout, sys.stderr
    sys.stdout, sys.stderr = out, err
    try:
        try:
            rc = main([str(a) for a in args])
        except SystemExit as e:
            rc = e.code if isinstance(e.code, int) else 1
    finally:
        sys.stdout, sys.stderr = antigo

    def ler(f):
        f.flush()
        return f.buffer.getvalue().decode("utf-8", errors="replace")
    return types.SimpleNamespace(returncode=rc, stdout=ler(out), stderr=ler(err))


@functools.lru_cache(maxsize=None)
def saida_cli(nome: str, pdelta: bool = False) -> dict:
    """Roda a CLI uma vez por exemplo (html, md e json) e reaproveita a saída entre os testes do processo."""
    pasta = pasta_sessao() / ("cli_pdelta" if pdelta else "cli") / nome
    args = [str(EXEMPLOS / f"{nome}.json"), "-o", str(pasta)] + (["--pdelta"] if pdelta else [])
    t0 = time.monotonic()
    r = rodar_cli(*args)
    saida = {"rc": r.returncode, "stdout": r.stdout, "stderr": r.stderr, "pasta": pasta,
             "tempo_s": time.monotonic() - t0}
    if r.returncode == 0:
        saida["html"] = (pasta / f"{nome}.html").read_text(encoding="utf-8")
        saida["md"] = (pasta / f"{nome}.md").read_text(encoding="utf-8")
        saida["json_texto"] = (pasta / f"{nome}.json").read_text(encoding="utf-8")
        saida["json"] = json_estrito(saida["json_texto"])
    return saida


# ---------------------------------------------------------------- referência direta (biblioteca)
@functools.lru_cache(maxsize=None)
def referencia(nome: str):
    """(modelo, calculo) pela biblioteca `projeto`, uma vez por exemplo."""
    from prossen_estrutural import projeto
    m = projeto.carregar(EXEMPLOS / f"{nome}.json")
    return m, projeto.calcular(m)


# ---------------------------------------------------------------- web
class ClienteWeb:
    """Cliente HTTP mínimo (http.client) para a API local: devolve (status, JSON | texto)."""

    def __init__(self, porta: int):
        self.porta = porta

    def pedir(self, metodo, caminho, dados=None, cabecalhos=None, corpo_bruto=None):
        corpo = corpo_bruto if corpo_bruto is not None else (None if dados is None else json.dumps(dados).encode("utf-8"))
        cab = {"Origin": f"http://127.0.0.1:{self.porta}", **(cabecalhos or {})}   # mesma origem (CSRF, servidor.py)
        if metodo not in ("GET", "HEAD"):
            cab.setdefault("Content-Type", "application/json")
        c = http.client.HTTPConnection("127.0.0.1", self.porta, timeout=120)
        try:
            c.request(metodo, caminho, body=corpo, headers=cab)
            r = c.getresponse()
            bruto = r.read()
            tipo = r.getheader("Content-Type", "")
            texto = bruto.decode("utf-8", errors="replace")
            return r.status, (json_estrito(texto) if tipo.startswith("application/json") else texto), dict(r.getheaders())
        finally:
            c.close()

    def __call__(self, metodo, caminho, dados=None, **kw):
        st, corpo, _ = self.pedir(metodo, caminho, dados, **kw)
        return st, corpo


@contextlib.contextmanager
def servidor_web(pasta: Path | None = None):
    """Sobe `prossen_estrutural.web` em thread numa porta livre (porta 0) e entrega (porta, ClienteWeb, pasta).
    Espera o servidor responder (prazo de 5 s) e encerra com `shutdown()` + `server_close()`."""
    from prossen_estrutural.web import servidor
    with contextlib.ExitStack() as pilha:
        if pasta is None:
            tmp = tempfile.TemporaryDirectory(prefix="prossen_web_")
            pilha.callback(_limpar, tmp)
            pasta = Path(tmp.name)
        srv = servidor.criar_servidor(porta=0, pasta=pasta)
        porta = srv.server_address[1]
        th = threading.Thread(target=srv.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        th.start()

        def parar():
            srv.shutdown()
            srv.server_close()
            th.join(5)
        pilha.callback(parar)
        cli = ClienteWeb(porta)
        prazo = time.monotonic() + 5
        while True:
            try:
                if cli("GET", "/api/resumo")[0] == 200:
                    break
            except OSError:
                if time.monotonic() > prazo:
                    raise
                time.sleep(0.02)  # espera curta por condição (não é pausa fixa)
        yield porta, cli, Path(pasta).resolve()


def _limpar(tmp):
    with contextlib.suppress(Exception):
        tmp.cleanup()


# ---------------------------------------------------------------- MCP
def parametros_stdio(pasta, usar_mcp_json=False):
    """`StdioServerParameters` do servidor MCP como subprocesso (o que o Claude Code faz). Com `usar_mcp_json`,
    lê `.mcp.json` da raiz (PYTHONPATH relativo, cwd = raiz) trocando só o executável."""
    from mcp import StdioServerParameters
    if usar_mcp_json:
        cfg = json.loads((RAIZ / ".mcp.json").read_text(encoding="utf-8"))["mcpServers"]["prossen-estrutural"]
        return StdioServerParameters(command=sys.executable, args=[*cfg["args"], "--pasta", str(pasta)],
                                     env={**os.environ, **cfg.get("env", {}), "PYTHONIOENCODING": "utf-8"},
                                     cwd=str(RAIZ))
    return StdioServerParameters(command=sys.executable, args=["-m", "prossen_estrutural.servidor_mcp", "--pasta", str(pasta)],
                                 env=ambiente(), cwd=str(RAIZ))


# ---------------------------------------------------------------- navegador (playwright-cli)
def playwright_cli() -> str | None:
    """Caminho do `playwright-cli` (ficha 023: ferramenta instalada globalmente via npm), ou None."""
    return shutil.which("playwright-cli")


# ---------------------------------------------------------------- F2/F5: edifício gerado + vento (referência)
@functools.lru_cache(maxsize=None)
def referencia_f2():
    """(modelo, calculo, dict) do F2/F5 feito direto pela biblioteca: gerar edifício C30 + peso próprio + vento IV."""
    from prossen_estrutural import projeto
    m = projeto.novo_modelo("F5")
    projeto.gerar_estrutura(m, EDIFICIO_F2)
    projeto.configurar(m, {"peso_proprio": "G1", "vento": VENTO_F2})
    c = projeto.calcular(m)
    return m, c, c.to_dict()


class Navegador:
    """Sessão do `playwright-cli` (Chromium sem janela) que roda os roteiros de `navegador/*.js` com `run-code`.
    Cada roteiro é o corpo de `async page => {...}`; `_prelude.js` entra antes e traz os ajudantes (coleta de erros
    de console, requisições fora de 127.0.0.1, `api()`, `etapa()`, `formato()`...). Tokens `__BASE__` e
    `__IMAGENS__` são trocados antes de rodar. A saída é o JSON devolvido pelo roteiro."""

    def __init__(self, base: str, sessao: str):
        self.base, self.sessao = base, sessao
        self.cwd = Path(tempfile.mkdtemp(prefix="prossen_pw_"))
        atexit.register(shutil.rmtree, str(self.cwd), True)

    def _cli(self, *args, timeout=240):
        return subprocess.run([playwright_cli(), f"-s={self.sessao}", *args], capture_output=True, text=True,
                              encoding="utf-8", errors="replace", cwd=str(self.cwd), timeout=timeout)

    def abrir(self):
        r = self._cli("open", "about:blank")
        if r.returncode != 0:
            raise RuntimeError(f"playwright-cli open falhou: {r.stdout[-400:]} {r.stderr[-400:]}")
        self._cli("resize", "1440", "900")

    def fechar(self):
        with contextlib.suppress(Exception):
            self._cli("close", timeout=60)

    def console(self) -> tuple[int, int]:
        """(erros, avisos) acumulados no console da sessão, segundo `playwright-cli console`."""
        import re
        saida = self._cli("console").stdout
        m = re.search(r"Total messages: \d+ \(Errors: (\d+), Warnings: (\d+)\)", saida)
        return (int(m.group(1)), int(m.group(2))) if m else (-1, -1)

    def rodar(self, roteiro: str, imagens: Path | None = None, tema: str | None = None, timeout=420) -> dict:
        corpo = (NAVEGADOR / roteiro).read_text(encoding="utf-8")
        prelude = (NAVEGADOR / "_prelude.js").read_text(encoding="utf-8")
        codigo = "async page => {\n" + prelude + "\n" + corpo + "\n}"
        codigo = (codigo.replace("__BASE__", self.base).replace("__IMAGENS__", (imagens or self.cwd).as_posix())
                  .replace("__TEMA__", tema or ""))
        arq = self.cwd / f"{Path(roteiro).stem}.run.js"
        arq.write_text(codigo, encoding="utf-8")
        r = self._cli("run-code", f"--filename={arq}", timeout=timeout)
        saida = r.stdout
        if "### Error" in saida or "### Result" not in saida:
            raise AssertionError(f"roteiro {roteiro} falhou:\n{saida[-1800:]}\n{r.stderr[-400:]}")
        trecho = saida.split("### Result", 1)[1].split("### Ran Playwright code", 1)[0].strip()
        return json_estrito(trecho)
