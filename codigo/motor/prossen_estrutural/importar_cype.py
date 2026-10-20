"""Importação de obras do CYPE 3D 2016 (arquivo .ed3) para o `Modelo` (ver software/docs/formato-ed3.md).

    ler_ed3(caminho) -> Modelo          (Modelo.avisos: list[str] com o que não foi importado)
    ler_resultados_ed3(caminho) -> dict (resultados gravados pelo CYPE: deslocamentos, reações, esforços)

O .ed3 é a gravação sequencial (sem rótulos) das estruturas do CYPE. A sequência de campos de cada
estrutura está em dados/cype_ed3_gramatica.json (levantada das funções de gravação, ver o documento).
Unidades internas do CYPE: t (tonelada-força) e m; aqui tudo sai em kN e m (1 t = 9,80665 kN).
Nada é inventado: o que não se sabe ler vira aviso.
"""
from __future__ import annotations

import json
import math
import os
import re
import struct
import threading
import time

from . import materiais as _mat
from . import secoes as _sec
from .modelo import (Barra, CargaDistribuida, CargaNodal, CargaPontual, CasoCarga, Material, Modelo, No,
                     MPa)

T_KN = 9.80665  # 1 tf em kN (CYPE grava forças em t e momentos em t·m)
_GRAM = None

# Guardas contra arquivo corrompido ou malicioso (a leitura roda sob a trava global da web): tamanho, contagens
# (nunca maiores que os bytes que restam), janela do resync e tempo total. Obras reais: salto do resync <= 2 KB,
# leitura de 0,03 a 4 s (PIPERACK, 177 barras).
LIMITE_BYTES = 32 * 1024 * 1024
JANELA_RESYNC = 16 * 1024
PRAZO_S = 20.0
_prazo = threading.local()


def _iniciar_prazo():
    _prazo.fim = time.monotonic() + PRAZO_S


def _checar_prazo():
    fim = getattr(_prazo, "fim", None)
    if fim is not None and time.monotonic() > fim:
        raise ErroFormato(f"leitura do .ed3 interrompida: mais de {PRAZO_S:g} s (arquivo corrompido ou fora do formato)")


def _ler_arquivo(caminho):
    """Bytes do .ed3 (limite de tamanho) e arma o prazo da leitura."""
    with open(caminho, "rb") as f:
        d = f.read(LIMITE_BYTES + 1)
    if len(d) > LIMITE_BYTES:
        raise ErroFormato(f"arquivo acima de {LIMITE_BYTES // 2**20} MB")
    _iniciar_prazo()
    return d


class ErroFormato(ValueError):
    """Arquivo que não é .ed3 do CYPE 3D 2016 ou que não segue o formato conhecido."""


def _gramatica():
    global _GRAM
    if _GRAM is None:
        with open(os.path.join(os.path.dirname(__file__), "dados", "cype_ed3_gramatica.json"), encoding="utf-8") as f:
            _GRAM = json.load(f)["G"]
    return _GRAM


# ---------------------------------------------------------------- leitor genérico
class _Leitor:
    """Interpreta a gramática: tipos primitivos little-endian, listas = u32 + itens, opcional = bool + item."""

    def __init__(self, d, pos, especiais=None):
        self.d, self.p, self.G, self.esp = d, pos, _gramatica(), especiais or {}

    def _un(self, fmt, n):
        if self.p + n > len(self.d):
            raise ErroFormato(f"fim de arquivo em {self.p:#x}")
        v = struct.unpack_from(fmt, self.d, self.p)[0]
        self.p += n
        return v

    def cnt(self, lim=5_000_000):
        n = self._un("<I", 4)
        # cada item ocupa pelo menos 1 byte: contagem maior que o resto do arquivo é lixo
        if n > lim or n > len(self.d) - self.p:
            raise ErroFormato(f"contagem {n} inválida em {self.p - 4:#x}")
        if n > 1000:
            _checar_prazo()
        return n

    def prim(self, t):
        if t == "u8":
            return self._un("<B", 1)
        if t == "bool":
            v = self._un("<B", 1)
            if v > 1:
                raise ErroFormato(f"bool {v} inválido em {self.p - 1:#x}")
            return v
        fmt = {"i8": ("<b", 1), "u16": ("<H", 2), "i16": ("<h", 2), "u32": ("<I", 4), "i32": ("<i", 4),
               "f64": ("<d", 8)}.get(t)
        if fmt:
            return self._un(*fmt)
        if t == "str":
            n = self.cnt(100_000)
            if self.p + 2 * n > len(self.d):
                raise ErroFormato(f"fim de arquivo em {self.p:#x}")
            v = self.d[self.p:self.p + 2 * n].decode("utf-16-le", "replace")
            self.p += 2 * n
            return v
        if t == "astr":
            n = self._un("<H", 2)
            if self.p + n > len(self.d):
                raise ErroFormato(f"fim de arquivo em {self.p:#x}")
            v = self.d[self.p:self.p + n].decode("latin-1")
            self.p += n
            return v
        if t.startswith("arr"):
            n = self.cnt()
            return [self.prim(t[3:]) for _ in range(n)]
        if t in ("pts2d", "pts3d"):
            k = 2 if t == "pts2d" else 3
            return [tuple(self.prim("f64") for _ in range(k)) for _ in range(self.cnt())]
        raise ErroFormato(f"tipo {t} desconhecido")

    def op(self, o):
        if isinstance(o, list):
            k, sub = o
            if k == "arr":
                return [self.run(sub) for _ in range(self.cnt())]
            if k in ("opt", "optarr"):
                return self.run(sub) if self.prim("bool") else None
            if k == "idt01":  # lista com id (idarrays): u8, u32, n, n×(u8, u32, str, item)
                self.prim("u8"), self.prim("u32")
                return [[self.prim("u8"), self.prim("u32"), self.prim("str"), self.run(sub)] for _ in range(self.cnt())]
            raise ErroFormato(f"operação {k}")
        if o.startswith("call:"):
            return self.run(o[5:])
        if o.startswith(("EXT:", "?")):
            raise ErroFormato(f"estrutura sem leitor ({o}) em {self.p:#x}")
        return self.prim(o)

    def run(self, nome):
        if nome in self.esp:
            return self.esp[nome](self)
        g = self.G.get(nome)
        if g is None:
            if ":" in nome or "!" in nome:
                raise ErroFormato(f"estrutura sem leitor ({nome}) em {self.p:#x}")
            return self.prim(nome)
        if g and g[0] == "optself":
            return [self.op(o) for o in g[1]] if self.prim("bool") else None
        return [self.op(o) for o in g]


# ---------------------------------------------------------------- localização dos blocos
_OBRA, _MODELO = "ed3cype:obra", "ed3dlib.dll:100cc3a0"
_UNIAO = "ed3dlib.dll:102f76b0"


def _achar_obra(d):
    """Início do objeto obra: u8 9 (versão), u32 id, título (str), u8 27 (versão do modelo)."""
    achados = []
    for m in re.finditer(rb"\x09(?=.{4}(.{4}))", d, re.S):
        i = m.start()
        L = struct.unpack_from("<I", d, i + 5)[0]
        j = i + 9 + 2 * L
        if L > 300 or j >= len(d) or d[j] != 0x1B:
            continue
        try:
            t = d[i + 9:j].decode("utf-16-le")
        except UnicodeDecodeError:
            continue
        if all(31 < ord(c) < 0x3000 for c in t):
            achados.append(i)
    if len(achados) != 1:
        raise ErroFormato(f"início da obra não identificado ({len(achados)} candidatos); só CYPE 3D 2016.n")
    return achados[0]


def _resultados_pos(d):
    """(início, fim) do bloco EBARESUL_01 e posição onde termina a obra (antes do bool + u32 de versão)."""
    i = d.find(b"EBARESUL_01\x00")
    if i < 5:
        raise ErroFormato("bloco de resultados EBARESUL_01 não encontrado")
    fim = struct.unpack_from("<I", d, i + 12)[0]
    if d[fim:fim + 12] != b"EBARESUL_01\x00":
        raise ErroFormato("fim do bloco de resultados inconsistente")
    return i, fim, i - 5


def _ler_uniao_resync(L, fim_obra, resto):
    """Ligações editáveis (ununioned): o conteúdo depende do tipo de ligação e não é lido.
    Pula até a posição em que o restante do arquivo volta a fechar exatamente (validação forte)."""
    ver, ident = L.prim("u8"), L.prim("u32")
    uver, nome = L.prim("u8"), L.prim("astr")
    inicio = L.p

    def cauda(p0):
        L2 = _Leitor(L.d, p0, L.esp)
        L2.op(["arr", "ed3dlib.dll:10184240"]), L2.prim("bool"), L2.prim("str"), L2.prim("bool")
        return L2

    for c in range(inicio, min(len(L.d), fim_obra, inicio + JANELA_RESYNC)):
        if not c & 0xFF:
            _checar_prazo()
        try:
            L2 = cauda(c)
        except (ErroFormato, struct.error, UnicodeDecodeError):
            continue
        p = L2.p
        prox = L.d[p:p + 12]
        if len(prox) == 12 and prox[0] == ver and prox[5] == uver and prox[8:10] == b"UN":
            L.p = p
            return ("proxima", [ver, ident, nome])
        if resto is not None:
            try:
                L3 = _Leitor(L.d, p, L.esp)
                r = resto(L3)
                if L3.p == fim_obra:
                    L.p = p
                    return ("fim", [ver, ident, nome], r)
            except (ErroFormato, struct.error, UnicodeDecodeError, IndexError):
                continue
    raise ErroFormato(f"ligação editável {nome} em {inicio:#x}: não foi possível pular o conteúdo")


def _ler_obra(d, avisos):
    ini = _achar_obra(d)
    _, _, fim_obra = _resultados_pos(d)
    G = _gramatica()
    ops_obra, ops_mod = G[_OBRA], G[_MODELO]
    k_mod = ops_obra.index("call:" + _MODELO)
    k_uni = ops_mod.index(["arr", _UNIAO])
    L = _Leitor(d, ini)
    obra = [L.op(o) for o in ops_obra[:k_mod]]
    mod = [L.op(o) for o in ops_mod[:k_uni]]

    def resto(Lx):
        r1 = [Lx.op(o) for o in ops_mod[k_uni + 1:]]
        r2 = [Lx.op(o) for o in ops_obra[k_mod + 1:]]
        return r1, r2

    n = L.cnt()
    unioes, fechou = [], None
    for k in range(n):
        p0 = L.p
        try:
            unioes.append(L.run(_UNIAO))
            continue
        except (ErroFormato, struct.error, UnicodeDecodeError):
            L.p = p0
        r = _ler_uniao_resync(L, fim_obra, resto if k == n - 1 else None)
        unioes.append(r[1])
        if r[0] == "fim":
            fechou = r[2]
    puladas = sum(1 for u in unioes if len(u) == 3)
    if n:
        avisos.append(f"{n} ligação(ões) editável(is) do CYPE não importada(s) ({puladas} com conteúdo pulado sem "
                      "leitura); o dimensionamento de ligações fica fora.")
    if fechou is None:
        r1, r2 = resto(L)
    else:
        r1, r2 = fechou
        L.p = fim_obra
    if L.p != fim_obra:
        raise ErroFormato(f"leitura da obra terminou em {L.p:#x}, esperado {fim_obra:#x}")
    obra += [mod + [unioes] + r1] + r2
    return obra


def _ler_cabecalho(d, avisos):
    """Bibliotecas do arquivo: normas, séries de perfis (com propriedades) e hipóteses do usuário."""
    G = _gramatica()
    for st in range(0x10, min(0x400, len(d))):
        if d[st] != 0x27:
            continue
        L = _Leitor(d, st)
        out = []
        try:
            for o in G["ed3cype:cabecalho"][:12]:  # até a biblioteca de séries de perfis
                out.append(L.op(o))
        except (ErroFormato, struct.error, UnicodeDecodeError):
            continue
        try:
            for o in G["ed3cype:cabecalho"][12:]:  # perfis genéricos de alumínio, estados-limite e hipóteses
                out.append(L.op(o))
        except (ErroFormato, struct.error, UnicodeDecodeError) as e:
            avisos.append(f"hipóteses do usuário não lidas ({e}); casos ficam com o nome 'H<id>'.")
        return out
    raise ErroFormato("cabeçalho de bibliotecas não encontrado")


def _ler_materiais_obra(d):
    """ed3matob (aços da obra): achado pela lista de madeiras que vem dentro dele ('MATERIALES_...')."""
    i = d.find(b"MATERIALES_")
    while i != -1:
        p = i - 2 - 4 - 1 - 4 - 1
        try:
            L = _Leitor(d, p)
            r = L.run("ed3dlib.dll:103b1bc0")
            if r[0] == 3:
                return r
        except (ErroFormato, struct.error, UnicodeDecodeError):
            pass
        i = d.find(b"MATERIALES_", i + 1)
    return None


# ---------------------------------------------------------------- tabelas do CYPE (dmatndll: acelam11 / acecnf18)
# Ordem e fy (MPa) dos aços dos arquivos de materiais do CYPE 2016 (dmatndll.dll), usados pelo índice gravado.
_LAMINADO_NBR8800 = [
    ("A-36", 250), ("A-570 Grau 40", 280), ("A-570 Grau 45", 310), ("A-441 Grupos 1 e 2, t<=19", 345),
    ("A-441 Grupo 3, 19<t<=38", 315), ("A-441 38<t<=100", 290), ("A-441 100<t<=200", 275),
    ("A-572 Grau 42, t<=150", 290), ("A-572 Grau 50, t<=50", 345), ("A-242 Grupos 1 e 2, t<=19", 345),
    ("A-242 Grupo 3, 19<t<=38", 315), ("A-242 38<t<=100", 290), ("A-588 t<=100", 345), ("A-588 100<t<=127", 315),
    ("A-588 127<t<=200", 290), ("USI SAC-41", 245), ("COS-AR-COR-400", 250), ("COS-AR-COR-500", 375),
    ("COS-AR-COR-290", 290), ("COS-AR-COR-345", 345), ("MR-250", 250), ("CG-24", 235), ("CG-26", 255),
    ("CF-24", 240), ("CF-26", 260), ("G-30", 300), ("G-35", 345), ("F-32/Q-32", 310), ("F-35/Q-35", 340)]
_CONFORMADO_NBR14762 = [
    ("CF-26", 260), ("CF-28", 280), ("CF-30 (a quente)", 300), ("F-32/Q-32", 310), ("F-35/Q-35", 340), ("Q-40", 380),
    ("Q-42", 410), ("Q-45", 450), ("CFR 400 (a quente)", 250), ("CFR 500 (a frio)", 310),
    ("CFR 500 (a quente)", 370), ("ZAR-250", 250), ("ZAR-280", 280), ("ZAR-320", 320), ("ZAR-345", 345)]
# nome do CYPE -> nome em materiais.ACOS (mesmo aço)
_EQUIV = {"A-36": "A-36", "MR-250": "MR-250", "A-572 Grau 50, t<=50": "A-572 GR.50",
          "A-572 Grau 42, t<=150": "A-572 GR.42", "A-588 t<=100": "A-588", "CG-26": "CG-26", "CF-24": "CF-24",
          "CF-26": "CF-26", "COS-AR-COR-500": "COS-AR-COR-500", "CF-28": "CF-28", "CF-30 (a quente)": "CF-30",
          "ZAR-250": "ZAR-250", "ZAR-280": "ZAR-280", "ZAR-345": "ZAR-345"}


def _material(tabela, idx, rotulo, avisos):
    if idx is None or idx >= len(tabela):
        avisos.append(f"aço {rotulo}: índice {idx} fora da tabela conhecida; barras desse tipo não importadas.")
        return None
    nome, fy = tabela[idx]
    if nome in _EQUIV:
        return _mat.aco(_EQUIV[nome])
    avisos.append(f"aço '{nome}' ({rotulo}) não está em materiais.ACOS: criado com fy = {fy} MPa do CYPE, "
                  "E/G/peso da NBR 8800 e fu desconhecido.")
    return Material(nome, _mat.E_ACO, _mat.G_ACO, _mat.PESO_ACO, fy=fy * MPa)


# ---------------------------------------------------------------- perfis
def _indice_perfis(bp3):
    """(série, nome) -> (função da família, registro) a partir da biblioteca de séries do arquivo."""
    G, nomes = _gramatica(), _nomes()
    fams = G["bibperf3.dll:10025820"][1:]
    idx = {}
    for arr, f in zip(bp3[1:], fams):
        fam = nomes.get(f[1], "")
        for serie in arr:
            for reg in serie[3]:
                idx.setdefault((serie[1], reg[1]), (fam, reg))
    return idx


_NOMES = None


def _nomes():
    global _NOMES
    if _NOMES is None:
        with open(os.path.join(os.path.dirname(__file__), "dados", "cype_ed3_gramatica.json"), encoding="utf-8") as f:
            _NOMES = json.load(f)["nomes"]
    return _NOMES


def _secao(fam, reg, nome):
    """Secao a partir do registro da biblioteca do CYPE (m). None se a família não é suportada."""
    v = reg[2:]
    if fam == "pf3silam_graba":  # I laminado: h, b, tw, tf, (inclinação), (r1), (r2), A, Ix, Iy, J, Cw, Zx, Zy
        h, b, tw, tf = v[:4]
        par = list(zip(v[4::2], v[5::2]))
        if par[0][0]:
            return None, "perfil I com mesas inclinadas (não suportado)"
        s = _sec.secao_I(h, b, tw, tf, par[1][1] if par[1][0] else 0.0, nome=nome, soldado=False)
        if par[3][0] and abs(s.A - par[3][1]) > 0.03 * par[3][1]:
            return s, f"A calculada {s.A * 1e4:.2f} cm² difere da tabela do CYPE {par[3][1] * 1e4:.2f} cm²"
        return s, None
    if fam == "pf3siarm_graba":
        return _sec.secao_I(*v[:4], nome=nome, soldado=True), None
    if fam == "pf3sclam_graba":
        return _sec.secao_U(*v[:4], nome=nome), None
    if fam == "pf3suconf_graba":  # U dobrado: h, b, t, r (raio interno)
        return _sec.secao_U_pf(v[0], v[1], v[2], nome=nome, ri=v[3]), None
    if fam == "pf3surigconf_graba":  # U enrijecido: h, b, D, t, r
        return _sec.secao_Ue(v[0], v[1], v[2], v[3], nome=nome, ri=v[4]), None
    if fam == "pf3stuboconf_graba":
        return _sec.secao_tubo_circular(v[0], v[1], nome=nome), None
    if fam in ("pf3scajrconf_graba", "pf3stubrarm_graba"):
        return _sec.secao_tubo_retangular(v[0], v[1], v[2], nome=nome), None
    if fam == "pf3scajcconf_graba":
        return _sec.secao_tubo_retangular(v[0], v[0], v[1], nome=nome), None
    if fam in ("pf3sasimlam_graba", "pf3sangsconf_graba"):  # cantoneira de abas iguais: b, t
        return _sec.secao_cantoneira(v[0], v[0], v[1], nome=nome), None
    if fam in ("pf3sanglam_graba", "pf3sangconf_graba"):  # abas desiguais: h, b, t
        return _sec.secao_cantoneira(v[0], v[1], v[2], nome=nome), None
    if fam == "pf3sredlam_graba":
        return _sec.secao_circular(v[0], nome=nome), None
    if fam == "pf3scuadlam_graba":
        return _sec.secao_retangular(v[0], v[0], nome=nome), None
    return None, f"família {fam or '?'} não suportada"


# Tipo de peça (pzmetstr, u16) de U laminado -> (arranjo, ligação). Lido de piezamet: pzmetstr_crea_doble_en_cajon_
# soldado cria o tipo 3 com soldlong; a tabela de opções do U (pzmetdef) dá 3/7 soldadura longitudinal,
# 4 soldadura + platabanda, 5/8 separação + presilhas, 6/9 separação + união genérica; 7 tem "forma de doblete" (I).
_COMPOSTAS = {3: ("caixao", "soldada"), 4: ("caixao", "chapas"), 5: ("caixao", "presilhas"),
              6: ("caixao", "generica"), 7: ("I", "soldada"), 8: ("I", "presilhas"), 9: ("I", "generica")}
# índices opcionais do pzmetstr: 5 separvar [ver, s_ini, s_fim], 7 soldlong [ver, bool], 10 presilla
# [ver, aço, u16, perfil, separação do usuário?, separação (m)]
_PZ_SEPARVAR, _PZ_PRESILLA = 5, 10


def _secao_composta(comp, composta, pz, nome):
    """Seção de 2 perfis (secoes.secao_dupla) a partir da peça composta do CYPE. -> (Secao | None, nota)."""
    arranjo, lig = composta
    if comp.props.get("tipo") != "U" or "tw" not in comp.props:
        return None, "só 2 U laminados são suportados"
    if lig == "chapas":
        return None, "duplo em tubo com chapas (platabandas): dimensões das chapas não suportadas"
    notas = []
    sv = pz[_PZ_SEPARVAR] if len(pz) > _PZ_SEPARVAR else None
    s = 0.0
    if sv:
        s = min(sv[1], sv[2])
        if abs(sv[1] - sv[2]) > 1e-9:
            notas.append(f"separação variável {sv[1] * 1e3:g}–{sv[2] * 1e3:g} mm: usada a menor")
    l_lig = None
    if lig == "presilhas":
        pr = pz[_PZ_PRESILLA] if len(pz) > _PZ_PRESILLA else None
        if pr and pr[4]:
            l_lig = pr[5]
        else:
            notas.append("distância entre presilhas automática do CYPE (não gravada): compressão não verificável "
                         "(informe composicao.l_ligacoes)")
        if s > 0:
            notas.append("perfis afastados com presilhas: NBR 8800 5.3.4 só cobre perfis em contato ou com chapas "
                         "espaçadoras (compressão reprova)")
    if lig == "generica":
        notas.append("união genérica: perfis tratados como independentes (soma); verificação de compressão e "
                     "flexão em y reprova")
    rotulo = {("caixao", "soldada"): "duplo em tubo soldado", ("I", "soldada"): "duplo em I soldado",
              ("caixao", "presilhas"): "duplo em caixão com presilhas", ("I", "presilhas"): "duplo em I com presilhas",
              ("caixao", "generica"): "duplo em tubo união genérica", ("I", "generica"): "duplo em I união genérica"}
    sec = _sec.secao_dupla(comp, arranjo, lig, s=s, l_ligacoes=l_lig,
                           nome=f"2{nome} {rotulo[composta]}" + (f" s={s * 1e3:g}" if s else ""))
    return sec, "; ".join(notas) or None


# ---------------------------------------------------------------- hipóteses e cargas
# grupos de hipóteses do usuário (índice na lista lida do cb3hipusr) -> tipo do CasoCarga, na ordem em que
# o CYPE monta as hipóteses de cálculo (ghipocal): PP, permanentes, sobrecargas, fluidos, temperatura, vento,
# sismo, neve, retração, geada, acidentais, empuxos
_GRUPOS = [(6, "G", "cargas permanentes"), (5, "Q", "sobrecargas"), (17, "G", "fluidos"), (15, "Q", "temperatura"),
           (7, "V", "vento"), (10, "S", "sismo"), (13, "Q", "neve"), (18, "G", "retração"), (16, "Q", "geada"),
           (19, "E", "acidental"), (20, "G", "empuxo de terra")]


def _hipoteses(hip, avisos):
    """[(id, nome, tipo, grupo)] na ordem das hipóteses de cálculo do CYPE (sem o PP)."""
    out = []
    if hip is None:
        return out
    for k, tipo, grupo in _GRUPOS:
        g = hip[k]
        if k == 10:  # sismo do usuário: [ver, nome, descrição, ...] sem id de hipótese conhecido
            for h in g:
                out.append((None, h[1], tipo, grupo))
                avisos.append(f"hipótese de sismo '{h[1]}' do CYPE não importada (use config.sismo).")
            continue
        listas = [c[3][1] for c in g] if k == 5 else [g[1]]
        for lst in listas:
            for h in lst:
                out.append((h[1], h[2] or f"H{h[1]}", tipo, grupo))
                if grupo not in ("cargas permanentes", "sobrecargas", "vento", "acidental"):
                    avisos.append(f"hipótese '{h[2]}' ({grupo}) importada como tipo {tipo}: confira o tipo/ψ.")
    return out


_DIRS = {0: (1, 0, 0), 1: (0, 1, 0), 2: (0, 0, 1), 3: (-1, 0, 0), 4: (0, -1, 0), 5: (0, 0, -1)}
_TIPOS_CARGA = {0: "uniforme", 1: "pontual", 2: "faixa", 3: "triangular à esquerda", 4: "triangular à direita",
                5: "trapezoidal", 6: "momento pontual", 7: "temperatura variável", 8: "temperatura uniforme"}


def _direcao(dr):
    """f1014ec90: [ver, tipo ed3tipo_direccion_t, vetor] -> vetor unitário global."""
    if dr[1] in _DIRS:
        return _DIRS[dr[1]]
    if dr[1] == 6:
        x, y, z = dr[2][1:4]
        n = math.sqrt(x * x + y * y + z * z)
        return (x / n, y / n, z / n) if n > 0 else None
    return None


# ---------------------------------------------------------------- vínculos
def _apoio(v, avisos, nid):
    """ed3vincext -> 6 bools. tipo: 0 livre, 1 articulado, 2 genérico, 3 engastado,
    4..11 engastado com deslizamento (reta x/y/z/usuário, plano xy/xz/yz/usuário), 12..19 idem articulado."""
    t = v[1]
    if t == 0:
        return (False,) * 6
    if t == 1:
        return (True, True, True, False, False, False)
    if t == 3:
        return (True,) * 6
    if 4 <= t <= 19:
        k = (t - 4) % 8
        giro = t < 12
        livre = {0: "x", 1: "y", 2: "z", 4: "xy", 5: "xz", 6: "yz"}.get(k)
        if livre is None:
            avisos.append(f"nó {nid}: apoio com deslizamento em direção do usuário não importado (ficou engastado/articulado sem deslizar).")
            livre = ""
        return tuple(c not in livre for c in "xyz") + ((True,) * 3 if giro else (False,) * 3)
    if t == 2:  # genérico: estado por GL (0 livre, 1 restrito, outro = elástico)
        g = v[5]
        est = [g[2][1], g[2][3], g[2][5], g[3][1], g[3][3], g[3][5]]
        if any(e not in (0, 1) for e in est):
            avisos.append(f"nó {nid}: apoio elástico do CYPE não importado (GL elástico ficou livre).")
        if (g[4][1:], g[6][1:]) != ([1.0, 0.0, 0.0], [0.0, 0.0, 1.0]):
            avisos.append(f"nó {nid}: apoio genérico com eixos girados; restrições lidas nos eixos globais.")
        return tuple(e == 1 for e in est)
    avisos.append(f"nó {nid}: tipo de apoio {t} desconhecido; nó ficou livre.")
    return (False,) * 6


# ---------------------------------------------------------------- montagem do Modelo
# listas do modelo do CYPE que o importador não traduz (índice na lista lida -> rótulo)
_NAO_IMPORTADOS = {10: "grupo(s) 1018de70", 11: "grupo(s) 1018de70", 12: "grupo(s) 1018de70",
                   17: "lista(s) 10187240", 18: "lista(s) 10187240", 19: "lista(s) 10187240",
                   21: "superfície(s) de carga",
                   22: "carga(s) 101619e0", 24: "lâmina(s)/elemento(s) de casca", 25: "viga(s) de concreto",
                   26: "grupo(s) 103be590"}


def _hipcal(hc):
    """hipocal: [ver, tipo, ?, id] -> ('PP' | id do usuário | None)."""
    if hc[1] == 0:
        return "PP"
    if hc[1] == 11:
        return hc[3]
    return None


def ler_ed3(caminho):
    """Lê a obra do CYPE 3D 2016 (.ed3) e devolve um Modelo (kN, m). `modelo.avisos` lista o que ficou de fora."""
    d = _ler_arquivo(caminho)
    if not d.startswith(b"\x02\x01\x0b\x00CYPE-2016"):
        raise ErroFormato("não é um arquivo do CYPE 2016 (cabeçalho 'CYPE-2016' ausente)")
    avisos = []
    cab = _ler_cabecalho(d, avisos)
    obra = _ler_obra(d, avisos)
    m_ = obra[3]
    modelo = Modelo(nome=obra[2] or os.path.splitext(os.path.basename(caminho))[0])
    modelo.config = {}

    # coordenadas (listas X, Y, Z com valor absoluto) e nós
    coord = {}
    for eixo in (3, 4, 5):
        for c in m_[eixo]:
            coord[c[1]] = c[2]
    nos = {}
    for n in m_[9]:
        nid = f"N{n[1]}"
        try:
            x, y, z = (coord[i] for i in n[4:7])
        except KeyError:
            avisos.append(f"nó {n[1]}: coordenada referenciada não encontrada; nó não importado.")
            continue
        nos[n[1]] = modelo.add(No(nid, round(x, 9), round(y, 9), round(z, 9), _apoio(n[19], avisos, nid)))

    # casos
    hips = _hipoteses(cab[15] if len(cab) > 15 else None, avisos)
    casos = {"PP": modelo.add(CasoCarga("PP", "G"))}
    modelo.peso_proprio = "PP"
    usados = {"PP"}
    for hid, nome, tipo, _ in hips:
        if hid is None:
            continue
        nm = nome
        while nm in usados:
            nm += "'"
        usados.add(nm)
        casos[hid] = modelo.add(CasoCarga(nm, tipo))

    def caso_de(hc, onde):
        k = _hipcal(hc)
        if k not in casos:
            if k is not None and not hips:
                casos[k] = modelo.add(CasoCarga(f"H{k}", "G"))
                avisos.append(f"hipótese {k} sem nome/tipo lido: criada como 'H{k}' tipo G.")
                return casos[k].nome
            avisos.append(f"{onde}: carga em hipótese {hc[1:]} desconhecida; não importada.")
            return None
        return casos[k].nome

    # materiais e perfis
    matob = _ler_materiais_obra(d)
    if matob is None:
        avisos.append("aços da obra (ed3matob) não encontrados: barras de aço não importadas.")
    norma_lam = cab[2] if len(cab) > 2 else None
    lam = conf = None
    if matob is not None:
        if norma_lam in (4, 11):
            lam = _material(_LAMINADO_NBR8800, matob[1], "laminado/soldado", avisos)
        else:
            avisos.append(f"norma de aço laminado {norma_lam} (não NBR 8800): aço laminado não importado.")
    perfis = _indice_perfis(cab[11])

    descr = {}
    for dd in m_[15]:
        f = dd[2]
        if f is None:
            descr[dd[1]] = (None, None, "descrição vazia")
            continue
        sel = f[2][1]
        if f[1] != 0:
            descr[dd[1]] = (None, None, f"descrição de material {f[1]} (não aço) não suportada")
            continue
        if sel > 4:
            descr[dd[1]] = (None, None, "seção genérica (por dimensões/propriedades) não suportada")
            continue
        pz = f[2][2 + sel]
        bp = pz[2]
        nome, serie, tmat = bp[4], bp[3], bp[1]
        composta = None
        if pz[1] != 0:
            composta = _COMPOSTAS.get(pz[1])
            if composta is None:
                descr[dd[1]] = (None, None, f"perfil composto (tipo de peça {pz[1]}) não suportado: {nome}")
                continue
        if tmat not in (0, 1, 2):
            descr[dd[1]] = (None, None, f"material tipo {tmat} (não aço) não suportado: {nome}")
            continue
        sec = None
        try:
            sec = _sec.catalogo(nome)
            sec.nome = nome
        except KeyError:
            reg = perfis.get((serie, nome))
            if reg is None:
                descr[dd[1]] = (None, None, f"perfil '{nome}' (série '{serie}') não está na biblioteca do arquivo")
                continue
            sec, nota = _secao(reg[0], reg[1], nome)
            if nota:
                avisos.append(f"perfil {nome}: {nota}.")
            if sec is None:
                descr[dd[1]] = (None, None, f"perfil '{nome}': {nota}")
                continue
        if composta is not None:
            sec, nota = _secao_composta(sec, composta, pz, nome)
            if sec is None:
                descr[dd[1]] = (None, None, f"perfil composto (tipo de peça {pz[1]}) {nome}: {nota}")
                continue
            if nota:
                avisos.append(f"perfil composto {sec.nome}: {nota}.")
        if tmat == 2 and conf is None and matob is not None:
            conf = _material(_CONFORMADO_NBR14762, matob[2], "formado a frio", avisos)
            avisos.append(f"aço formado a frio lido na tabela da NBR 14762:2010 (código de norma do arquivo: {cab[3]}).")
        mat = lam if tmat in (0, 1) else conf
        if mat is None:
            descr[dd[1]] = (None, None, f"sem aço para o perfil '{nome}'")
            continue
        modelo.secoes.setdefault(sec.nome, sec)
        modelo.materiais.setdefault(mat.nome, mat)
        tipo = "pff" if tmat == 2 and sec.props.get("tipo") in ("U", "Ue") else "aco"
        if tmat == 2 and tipo == "aco":
            avisos.append(f"perfil formado a frio {nome}: forma sem verificação NBR 14762 no software; barra tipo 'aco'.")
        descr[dd[1]] = (sec.nome, mat.nome, tipo)

    # barras (a peça dá perfil, giro e articulações por coeficiente)
    pecas = {}
    for pc in m_[14]:
        for b in pc[2]:
            pecas[b] = pc
    barras_cy = {b[1]: b for b in m_[13]}
    sem_desc, giros = set(), set()
    for b in m_[13]:
        pc = pecas.get(b[1])
        if pc is None or b[2] not in nos or b[3] not in nos:
            avisos.append(f"barra {b[1]}: sem peça ou com nó ausente; não importada.")
            continue
        sec, mat, tipo = descr.get(pc[3], (None, None, "descrição ausente"))
        if sec is None:
            if pc[3] not in sem_desc:
                avisos.append(f"descrição {pc[3]}: {tipo}; barras dela não importadas.")
                sem_desc.add(pc[3])
            continue
        g = pc[4]
        giro = {0: 0.0, 1: math.pi / 2, 2: math.pi, 3: -math.pi / 2}.get(g[1], g[2])
        ni, nj = nos[b[2]], nos[b[3]]
        # barra vertical: o eixo de referência do CYPE fica a 90° do nosso (y local = Y global); conferido
        # com os deslocamentos gravados pelo CYPE (testes/regressao/test_reg_benchmark_cype.py)
        vertical = math.hypot(nj.x - ni.x, nj.y - ni.y) < 1e-9
        rot = giro + (math.pi / 2 if vertical else 0.0)
        giros |= {giro} - {0.0}
        barra = modelo.add(Barra(f"B{b[1]}", ni.id, nj.id, sec, mat, rot=rot, tipo=tipo))
        barra.params["cortante"] = True  # o CYPE considera a deformação por cortante (Timoshenko)
    _trechos_rigidos(d, cab, obra, modelo, avisos)
    if giros:
        avisos.append("peças giradas no CYPE: o sentido do giro (+/-) não muda a análise, mas não foi confirmado "
                      "para perfis assimétricos (U, L): confira a posição das abas.")

    for k, rotulo in _NAO_IMPORTADOS.items():
        if m_[k]:
            avisos.append(f"{len(m_[k])} {rotulo} do CYPE não importado(s).")
    _articulacoes(m_, modelo, pecas, barras_cy, avisos)
    _cargas(m_, modelo, nos, barras_cy, caso_de, avisos)
    _paineis(m_[20], modelo, caso_de, avisos)
    modelo.avisos = avisos
    return modelo


def _trechos_rigidos(d, cab, obra, modelo, avisos):
    """Trechos rígidos de extremidade gravados nos resultados do CYPE -> params trecho_rigido_i/_j (m)."""
    try:
        res = _resultados(d, cab, obra, [])
        if not res["calculado"]:
            avisos.append("obra sem resultados calculados no CYPE: trechos rígidos de extremidade não importados.")
            return
        for bid, r in res["barras"].items():
            b = modelo.barras.get(bid)
            if b is None:
                continue
            ti, tj = r["trecho_rigido"]
            # trocar quando invertida: testado contra o CYPE (2026-10-20). TESTE 4 (3 de 4 barras invertidas):
            # u 1,0 % / M 0,01 % com troca contra u 8,9 % / M 2,0 % sem troca; PIPERACK: equivalentes (u <= 2,3 %).
            if r["invertida"]:
                ti, tj = tj, ti
            if ti > 0:
                b.params["trecho_rigido_i"] = float(ti)
            if tj > 0:
                b.params["trecho_rigido_j"] = float(tj)
    except Exception as e:  # ponytail: resultados são opcionais; qualquer falha de leitura só vira aviso
        avisos.append(f"trechos rígidos de extremidade não importados ({type(e).__name__}: {e}).")


def _articulacoes(m_, modelo, pecas, barras_cy, avisos):
    """Vinculação interior dos nós (0 rígido, 1 articulado, 2 engaste parcial, 3 genérica por grupos)
    e coeficientes de engastamento das peças (1 = engastado, 0 = articulado)."""
    def soltar(bid, nid):
        b = modelo.barras.get(f"B{bid}")
        if b is None:
            return
        if b.ni == nid:
            b.params["rotula_i"] = True
        if b.nj == nid:
            b.params["rotula_j"] = True

    barras_no = {}
    for b in m_[13]:
        for n in (b[2], b[3]):
            barras_no.setdefault(n, []).append(b[1])
    for n in m_[9]:
        nid, vi = f"N{n[1]}", n[18]
        if vi[1] == 0:
            continue
        if vi[1] == 1:
            for bid in barras_no.get(n[1], []):
                soltar(bid, nid)
        elif vi[1] == 2:
            avisos.append(f"nó {nid}: engastamento parcial (coef. {vi[2]:g}) não importado; ficou rígido.")
        elif vi[1] == 3:
            grupos = [set(g[1]) for g in vi[3][1]]
            if len(grupos) > 2 or any(len(gr) > 1 for gr in grupos[1:]):
                avisos.append(f"nó {nid}: vinculação genérica com vários grupos aproximada (barras fora do 1º grupo "
                              "articuladas uma a uma).")
            for gr in grupos[1:]:
                for bid in barras_no.get(n[1], []):
                    if pecas.get(bid) and pecas[bid][1] in gr:
                        soltar(bid, nid)
        else:
            avisos.append(f"nó {nid}: vinculação interior tipo {vi[1]} desconhecida; ficou rígido.")
    for pc in m_[14]:
        coef = [e[2] for e in pc[16][1:5]]
        if all(abs(c - 1) < 1e-9 for c in coef):
            continue
        if all(abs(c) < 1e-9 for c in coef):
            ids = pc[2]
            ini, fim = barras_cy[ids[0]], barras_cy[ids[-1]]
            n_ini = ini[2] if len(ids) == 1 or ini[2] not in (barras_cy[ids[1]][2], barras_cy[ids[1]][3]) else ini[3]
            n_fim = fim[3] if len(ids) == 1 or fim[3] not in (barras_cy[ids[-2]][2], barras_cy[ids[-2]][3]) else fim[2]
            soltar(ids[0], f"N{n_ini}")
            soltar(ids[-1], f"N{n_fim}")
        else:
            avisos.append(f"peça {pc[1]}: coeficientes de engastamento {coef} (parciais ou por plano) não importados.")


def _cargas(m_, modelo, nos, barras_cy, caso_de, avisos):
    ign = {}

    def ignorar(motivo):
        ign[motivo] = ign.get(motivo, 0) + 1

    for n in m_[9]:
        for c in n[20]:
            caso = caso_de(c[1], f"nó N{n[1]}")
            dr = _direcao(c[2])
            if caso is None or dr is None or n[1] not in nos:
                ignorar("carga nodal com hipótese/direção não reconhecida")
                continue
            F = tuple(c[3] * T_KN * k for k in dr) + (0.0, 0.0, 0.0)
            modelo.add(CargaNodal(caso, nos[n[1]].id, F))
    for b in m_[13]:
        bid = f"B{b[1]}"
        if bid not in modelo.barras:
            if b[4]:
                ignorar("carga em barra não importada")
            continue
        br = modelo.barras[bid]
        ni, nj = modelo.nos[br.ni], modelo.nos[br.nj]
        L = math.dist((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z))
        faixas = {}
        for c in b[4]:
            tipo = c[2]
            if c[13] != 0:
                ignorar("carga em eixos locais")
                continue
            dr = _direcao(c[14])
            caso = caso_de(c[1], f"barra {bid}")
            if caso is None or dr is None:
                ignorar("carga com hipótese/direção não reconhecida")
                continue
            if tipo == 0:
                modelo.add(CargaDistribuida(caso, bid, tuple(c[3] * T_KN * k for k in dr)))
            elif tipo == 1:
                pos, P = c[4][1], c[4][2]
                a = pos[2][1]
                if not -1e-9 <= a <= L + 1e-9:
                    ignorar("carga pontual fora da barra")
                    continue
                modelo.add(CargaPontual(caso, bid, min(max(a, 0.0), L), tuple(P * T_KN * k for k in dr)))
            elif tipo == 2:
                fx = c[5]
                faixas.setdefault((caso, dr, round(fx[1], 12)), []).append((fx[4][2][1], fx[4][3][1]))
            else:
                ignorar(f"carga {_TIPOS_CARGA.get(tipo, tipo)}")
        # faixas: só entram se cobrem a barra inteira com multiplicidade constante (= uniforme × k)
        for (caso, dr, q), ints in faixas.items():
            pts = sorted({0.0, L, *[x for i in ints for x in i]})
            mult = set()
            for a, bb in zip(pts, pts[1:]):
                if bb - a < 1e-6:
                    continue
                xm = (a + bb) / 2
                mult.add(sum(1 for i0, i1 in ints if i0 - 1e-6 <= xm <= i1 + 1e-6))
            if len(mult) == 1 and min(mult) > 0:
                k = mult.pop()
                modelo.add(CargaDistribuida(caso, bid, tuple(k * q * T_KN * c for c in dr)))
            else:
                ignorar("carga em faixa parcial")
    for motivo, n in sorted(ign.items()):
        avisos.append(f"{n} carga(s) não importada(s): {motivo}.")


# ---------------------------------------------------------------- painéis de carga superficial
# Painel (ed3panyo, f10228560): [ver, origem (3), Ux (3), Uy (3), polígono 2D em (Ux, Uy), direção de transmissão
# (2, unitária no plano: = direção da aresta de índice u32 do polígono), cargas uniformes [ver, hipocal, tipo,
# valor t/m², f64, f64], cargas variáveis com a altura (só painel não horizontal), u32 aresta da transmissão].
# Transmissão em uma direção (panyos3d: o painel é cortado em faixas paralelas à direção de transmissão, cada
# faixa descarrega nas barras que cruza). Aqui cada faixa é uma viga biapoiada entre barras consecutivas (estática;
# trecho além da última barra vai inteiro para ela, em balanço) — equilíbrio exato.
N_FAIXAS = 400   # ponytail: faixas fixas; erro de área só em bordas oblíquas, some com mais faixas
N_TRECHOS = 10   # cargas pontuais por barra quando a carga não é uniforme na barra toda


def _sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _unit(v):
    n = math.sqrt(_dot(v, v))
    return tuple(x / n for x in v) if n > 0 else None


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _intervalos(poly, P, t):
    """Trechos [τ0, τ1] da reta P + τ·t dentro do polígono (par-ímpar)."""
    taus = []
    for A, B in zip(poly, poly[1:] + poly[:1]):
        e = _sub(B, A)
        den = t[0] * e[1] - t[1] * e[0]
        if abs(den) < 1e-14:
            continue
        w = _sub(A, P)
        tau = (w[0] * e[1] - w[1] * e[0]) / den
        u = (w[0] * t[1] - w[1] * t[0]) / den
        if 0 <= u < 1:
            taus.append(tau)
    taus.sort()
    return list(zip(taus[0::2], taus[1::2]))


def distribuir_painel(poly, t, q, barras2d, n=N_FAIXAS):
    """Carga uniforme q (força/área) do polígono `poly` (2D) às barras `barras2d` {id: (P0, P1)} (2D) em uma
    direção `t`. -> ({id: [(ξ m desde P0, força)]}, força não descarregada)."""
    t = _unit(t)
    s = (-t[1], t[0])
    ss = [_dot(p, s) for p in poly]
    s0, s1 = min(ss), max(ss)
    w = (s1 - s0) / n
    out, perdida = {}, 0.0
    for k in range(n):
        sc = s0 + (k + 0.5) * w
        P = (sc * s[0], sc * s[1])
        hits = []
        for bid, (A, B) in barras2d.items():
            e = _sub(B, A)
            den = t[0] * e[1] - t[1] * e[0]
            if abs(den) < 1e-12:
                continue  # barra paralela à transmissão: não recebe
            v = _sub(A, P)
            tau = (v[0] * e[1] - v[1] * e[0]) / den
            u = (v[0] * t[1] - v[1] * t[0]) / den
            if -1e-9 <= u <= 1 + 1e-9:
                hits.append((tau, bid, min(max(u, 0.0), 1.0) * math.hypot(*e)))
        hits.sort()
        uniq = []  # nó comum a duas barras: a faixa descarrega numa só
        for h in hits:
            if not uniq or h[0] - uniq[-1][0] > 1e-9:
                uniq.append(h)
        for a, b in _intervalos(poly, P, t):
            dentro = [h for h in uniq if a - 1e-9 <= h[0] <= b + 1e-9] or []
            if not dentro:
                perdida += q * w * (b - a)
                continue
            cortes = sorted({a, b, *[h[0] for h in dentro]})
            for x0, x1 in zip(cortes, cortes[1:]):
                F, c = q * w * (x1 - x0), (x0 + x1) / 2
                esq = [h for h in dentro if h[0] <= c]
                dir_ = [h for h in dentro if h[0] >= c]
                if esq and dir_ and dir_[0][0] > esq[-1][0]:
                    h0, h1 = esq[-1], dir_[0]
                    f1 = F * (c - h0[0]) / (h1[0] - h0[0])
                    partes = ((h0, F - f1), (h1, f1))
                else:
                    partes = (((esq or dir_)[-1 if esq else 0], F),)
                for h, f in partes:
                    out.setdefault(h[1], []).append((h[2], f))
    return out, perdida


def _cargas_na_barra(modelo, caso, bid, forcas, dr):
    """Forças (ξ, F) numa barra -> uniforme se cobrem a barra toda por igual; senão N_TRECHOS pontuais
    (resultante e centro de cada trecho preservados)."""
    b = modelo.barras[bid]
    ni, nj = modelo.nos[b.ni], modelo.nos[b.nj]
    L = math.dist((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z))
    tr = [[0.0, 0.0] for _ in range(N_TRECHOS)]
    for x, f in forcas:
        k = min(int(x / L * N_TRECHOS), N_TRECHOS - 1)
        tr[k][0] += f
        tr[k][1] += f * x
    tot = sum(f for f, _ in tr)
    if all(abs(f - tot / N_TRECHOS) <= 0.02 * abs(tot) / N_TRECHOS for f, _ in tr):
        modelo.add(CargaDistribuida(caso, bid, tuple(tot / L * c for c in dr)))
        return
    for f, m in tr:
        if f:
            modelo.add(CargaPontual(caso, bid, min(max(m / f, 0.0), L), tuple(f * c for c in dr)))


def _paineis(paineis, modelo, caso_de, avisos):
    for k, p in enumerate(paineis, 1):
        O, ux, uy = p[1:4], _unit(p[4:7]), _unit(p[7:10])
        poly, t = list(p[10]), (p[11], p[12])
        if ux is None or uy is None or len(poly) < 3:
            avisos.append(f"painel de carga {k}: geometria inválida; não importado.")
            continue
        if abs(t[0]) + abs(t[1]) < 1e-9:
            A, B = poly[p[15] % len(poly)], poly[(p[15] + 1) % len(poly)]
            t = _sub(B, A)
        n = _unit(_cross(ux, uy))
        horizontal = abs(abs(n[2]) - 1) < 1e-6
        if horizontal:
            dr = (0.0, 0.0, -1.0)  # painel horizontal: carga gravitacional (valor positivo para baixo)
        else:
            # sentido da normal: componente dominante positiva (bate com o equilíbrio do CYPE nos 2 painéis
            # verticais do PIPERACK, +X e +Y); não confirmado no código
            i = max(range(3), key=lambda j: abs(n[j]))
            dr = n if n[i] > 0 else tuple(-c for c in n)
        barras2d = {}
        for bid, b in modelo.barras.items():
            pts = []
            for nid in (b.ni, b.nj):
                no = modelo.nos[nid]
                v = _sub((no.x, no.y, no.z), O)
                if abs(_dot(v, n)) > 1e-3:
                    break
                pts.append((_dot(v, ux), _dot(v, uy)))
            else:
                barras2d[bid] = tuple(pts)
        if p[14]:
            avisos.append(f"painel de carga {k}: {len(p[14])} carga(s) variável(is) com a altura não importada(s).")
        for c in p[13]:
            caso = caso_de(c[1], f"painel de carga {k}")
            if caso is None:
                continue
            if c[2] != 0:
                avisos.append(f"painel de carga {k}: carga de tipo {c[2]} (não uniforme) não importada.")
                continue
            q = c[3] * T_KN
            dist, perdida = distribuir_painel(poly, t, q, barras2d)
            for bid, forcas in dist.items():
                _cargas_na_barra(modelo, caso, bid, forcas, dr)
            if abs(perdida) > 1e-6 * max(abs(q), 1e-12):
                avisos.append(f"painel de carga {k} ({caso}): {perdida:.3f} kN sem barra que receba na direção de "
                              "transmissão (perdidos).")
    if paineis:
        avisos.append(f"{len(paineis)} painel(éis) de carga superficial distribuído(s) às barras em uma direção "
                      "(faixas biapoiadas entre barras); sentido da carga em painel não horizontal pela normal com "
                      "componente dominante positiva — confira.")


# ---------------------------------------------------------------- resultados gravados pelo CYPE
def ler_resultados_ed3(caminho):
    """Resultados que o CYPE gravou na obra (kN, m, rad). Ordem das soluções = ordem das hipóteses
    ['PP', permanentes, sobrecargas, vento, ...] (conferida por equilíbrio nos testes de benchmark).

    {'calculado': bool, 'hipoteses': [nomes], 'nos': {Nid: {'xyz', 'desloc': [[6]/hip], 'reacao': [[6]/hip]}},
     'barras': {Bid: {'ni', 'nj' (sentido do CYPE), 'invertida', 'pontos': [x a partir de ni], 'trecho_rigido',
                      'esforcos': [[[N, Vy, Vz, T, My, Mz] por ponto] por hip]}},
     'aproveitamento': {Bid: [(x, η, código do critério)]}, 'valido': resultados atuais, 'avisos': [...]}.
    Reações e esforços no sinal gravado pelo CYPE (reação = ação no apoio, oposta à do solver)."""
    d = _ler_arquivo(caminho)
    if not d.startswith(b"\x02\x01\x0b\x00CYPE-2016"):
        raise ErroFormato("não é um arquivo do CYPE 2016 (cabeçalho 'CYPE-2016' ausente)")
    avisos = []
    cab = _ler_cabecalho(d, avisos)
    obra = _ler_obra(d, avisos)
    return _resultados(d, cab, obra, avisos)


def _resultados(d, cab, obra, avisos):
    i, fim, _ = _resultados_pos(d)
    out = {"calculado": False, "valido": False, "hipoteses": [], "nos": {}, "barras": {}, "aproveitamento": {},
           "avisos": avisos}
    L = _Leitor(d, i + 16)
    r = L.run("ed3dlib.dll:102a6120")
    if L.p != fim:
        raise ErroFormato(f"resultados terminaram em {L.p:#x}, esperado {fim:#x}")
    # estado (ed3rescal): 0 sem cálculo; 1 válido se a obra não foi alterada depois (2 bools da obra); 2 idem só o 2º
    alterada, alterada2 = obra[-2], obra[-1]
    out["valido"] = (r[0] == 1 and not alterada and not alterada2) or (r[0] == 2 and not alterada2)
    if r[0] and not out["valido"]:
        avisos.append("resultados do CYPE desatualizados (a obra foi alterada depois do último cálculo).")
    sol = r[2]
    m_ = obra[3]
    hips = ["PP"] + [h[1] for h in _hipoteses(cab[15] if len(cab) > 15 else None, [])]
    if not sol[0] or not sol[0][0][3][0]:
        return out
    out["calculado"] = True
    nsol = len(sol[0][0][3][0])
    if nsol != len(hips):
        avisos.append(f"{nsol} soluções gravadas para {len(hips)} hipóteses lidas: nomes são posicionais.")
        hips = [f"S{k}" for k in range(nsol)]
    out["hipoteses"] = hips
    # o cálculo renumera nós e barras (ordem por coordenadas): associação pela posição dos nós
    coord = {}
    for eixo in (3, 4, 5):
        for c in m_[eixo]:
            coord[c[1]] = c[2]
    pos = {}
    for n in m_[9]:
        try:
            pos[tuple(round(coord[i], 6) for i in n[4:7])] = f"N{n[1]}"
        except KeyError:
            pass
    def no_em(xyz):
        return pos.get(tuple(round(x, 6) for x in xyz))
    for s in sol[0]:
        nid = no_em(s[:3])
        if nid is None:
            avisos.append(f"resultado em {tuple(s[:3])} sem nó correspondente no modelo.")
            continue
        dsl, rea = [], []
        for h in s[3][0]:
            v = [a[0] if a else 0.0 for a in h]
            dsl.append(v[:6])
            rea.append([x * T_KN for x in v[6:12]])
        out["nos"][nid] = {"xyz": tuple(s[:3]), "desloc": dsl, "reacao": rea}
    barras = {frozenset((f"N{b[2]}", f"N{b[3]}")): b for b in m_[13]}
    for s in sol[1]:
        ni, nj = no_em(s[0][:3]), no_em(s[0][3:6])
        b = barras.get(frozenset((ni, nj)))
        if b is None:
            avisos.append(f"resultado de barra {tuple(s[0][:6])} sem barra correspondente no modelo.")
            continue
        esf = [[[x * T_KN for x in p[1][k][:6]] for p in s[3]] for k in range(nsol)]
        out["barras"][f"B{b[1]}"] = {"ni": ni, "nj": nj, "invertida": ni != f"N{b[2]}", "pontos": [p[0] for p in s[3]],
                                     "trecho_rigido": (s[1], s[2]), "esforcos": esf}
    # verificação de barras (aproveitamento por seção: x, η, código do critério que governa)
    for e in r[7]:
        out["aproveitamento"][f"B{e[0]}"] = [(p[0], p[4], p[6]) for p in e[2]]
    return out
