"""Camada única de operações do Estructto (ESPEC §6): CLI, servidor MCP e interface web
são adaptadores finos sobre este módulo.

Aqui só há montagem de dados e chamadas aos motores (solver, ações, aço, concreto): nenhuma fórmula
de norma. Entradas e saídas são JSON-serializáveis (dict, list, str, float, int, bool, None), exceto
`Modelo` e `Calculo`, que os adaptadores guardam em memória. Operações de edição mutam o `Modelo`.
Unidades: kN, m, kN/m² (ESPEC §3); deslocamentos em m.
"""
from __future__ import annotations

import contextlib
import copy
import datetime
import json
import logging
import math
import os
import re
import struct
import tempfile
import time
from dataclasses import asdict, dataclass, field, fields, MISSING
from pathlib import Path

import numpy as np

from . import __version__
from . import importar_cype as _ed3
from . import materiais as _mat
from . import secoes as _sec
from . import solver as _sol
from .acoes import cargas as _cargas
from .acoes import combinacoes as _comb
from .acoes import sismo as _sismo
from .acoes import nbr8400 as _nbr8400
from .acoes import vento as _vento
from .acoes import vento_barras as _vb
from .aco import nbr8800 as _nbr8800
from .concreto import nbr6118 as _nbr6118
from .modelo import (Barra, CargaDistribuida, CargaNodal, CargaPontual, CasoCarga, Material, Modelo, MPa,
                     No, Pavimento, Secao, VERSAO_FORMATO, VERSOES_LIDAS)
from .solver import ErroMecanismo  # noqa: F401  (re-export, ESPEC §6.1)

try:  # a NBR 14762 chegou na ficha 003; sem ela as barras "pff" saem como não verificadas
    from .aco import nbr14762 as _nbr14762
except ImportError:  # pragma: no cover
    _nbr14762 = None

log = logging.getLogger(__name__)

N_PONTOS = 13  # quartos de vão exatos para o Cb (ESPEC §6.5, regra 3)
EXEMPLOS = Path(__file__).resolve().parent.parent / "exemplos"
CONFIG_PADRAO = {"tipo_uso": "comercial", "variabilidade": "grandes", "pdelta": False,
                 "armadura": "CA-50", "cobrimento_m": None, "caa": "II",  # cobrimento None = pela CAA
                 "tipo_edificacao": None,  # None | "tipo1" | "tipo2" | "separadas": γ da NBR 8681 tab. 2/5 ou NBR 8800 tab. 1 (None = por `variabilidade`)
                 # Conjunto de combinações: "nbr8681" (padrão) ou "nbr8400" (equipamentos de levantamento, casos I/II/III,
                 # acoes/nbr8400.py); `nbr8400` = {grupo 1-6 (Mx, tab. 10), psi (tab. 5)}.
                 "combinacoes": "nbr8681", "nbr8400": {"grupo": 3, "psi": _nbr8400.PSI_PADRAO},
                 # Dados do projeto (tela de novo projeto do Estructto, docs/v3/INICIO.md): só informativos, não entram no cálculo.
                 "projeto": {},  # {cliente, engenheiro, empresa, normas: ["NBR 8800", ...], pasta}
                 "grade": None}  # {"x": [espaçamentos m], "y": [...], "nomes_x": ["1", ...], "nomes_y": ["A", ...]} | None


# ================================================================ erros (ESPEC §6.1)

class ErroEntrada(ValueError):
    """Dado inválido, referência ausente ou validação com erro. `erros` = [{"campo", "mensagem"}]
    (todos juntos, não um por vez). Aceita também (mensagem: str, campo: str)."""

    def __init__(self, erros, campo=""):
        if isinstance(erros, str):
            erros = [{"campo": campo, "mensagem": erros}]
        self.erros = [dict(e) for e in erros]
        super().__init__("; ".join(f"{e['campo']}: {e['mensagem']}" if e["campo"] else e["mensagem"]
                                   for e in self.erros))


class ErroNaoEncontrado(KeyError):
    """Id inexistente; a mensagem lista até 20 ids válidos."""

    def __init__(self, tipo, id_, validos=()):
        validos = sorted(str(v) for v in validos)
        lista = ", ".join(validos[:20]) + (" …" if len(validos) > 20 else "")
        self.mensagem = f"{tipo} '{id_}' não encontrado" + (f"; válidos: {lista}" if validos else "")
        super().__init__(self.mensagem)

    def __str__(self):  # KeyError.__str__ devolveria a mensagem entre aspas
        return self.mensagem


# ================================================================ utilidades

def _puro(o):
    """Converte para JSON puro: numpy → float/list, tupla → lista, não finito → None."""
    if isinstance(o, dict):
        return {str(k): _puro(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [_puro(v) for v in o]
    if isinstance(o, np.ndarray):
        return _puro(o.tolist())
    if isinstance(o, (bool, np.bool_)):
        return bool(o)
    if isinstance(o, (int, np.integer)):
        return int(o)
    if isinstance(o, (float, np.floating)):
        v = float(o)
        return v if math.isfinite(v) else None
    if o is None or isinstance(o, str):
        return o
    return str(o)


def _aviso(codigo, mensagem, ref=None, nivel="aviso"):
    return {"nivel": nivel, "codigo": codigo, "mensagem": mensagem, "ref": ref}


def _num(v, campo, err, pos=False, nao_neg=False, obrig=True, padrao=None):
    """Número finito; `pos` exige > 0, `nao_neg` exige ≥ 0. Devolve float ou `padrao` (com erro registrado)."""
    if v is None and not obrig:
        return padrao
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
        err(campo, f"número finito obrigatório (recebido {v!r})")
        return padrao
    if pos and v <= 0:
        err(campo, f"deve ser > 0 (recebido {v!r})")
        return padrao
    if nao_neg and v < 0:
        err(campo, f"deve ser ≥ 0 (recebido {v!r})")
        return padrao
    return float(v)


def _ok_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _vetor(v, n, campo, err):
    if not isinstance(v, (list, tuple)) or len(v) != n:
        err(campo, f"lista de {n} números obrigatória (recebido {v!r})")
        return None
    out = [_num(x, f"{campo}[{i}]", err) for i, x in enumerate(v)]
    return None if any(x is None for x in out) else tuple(out)


def _chaves(d, permitidas, err, prefixo=""):
    for k in d:
        if k not in permitidas:
            err(f"{prefixo}{k}", f"campo desconhecido; válidos: {', '.join(sorted(permitidas))}")


def _cfg(m):
    """Config efetiva: padrões + o que o modelo traz (arquivo v0 tem config vazia)."""
    return {**CONFIG_PADRAO, **(m.config or {})}


def _comprimento(m, b):
    ni, nj = m.nos[b.ni], m.nos[b.nj]
    return math.dist((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z))


def _vertical(m, b):
    L = _comprimento(m, b)
    return L > 0 and abs(m.nos[b.nj].z - m.nos[b.ni].z) / L > 0.99


def _papel(m, b):
    return b.params.get("papel") or ("pilar" if _vertical(m, b) else "viga")


def _norma_curta(mod):
    """'NBR 6118:2023 (itens …)' → 'NBR 6118:2023'."""
    return mod.EDICAO.split(" (")[0]


# ================================================================ tipos de elemento

TIPOS = ("no", "material", "secao", "barra", "caso", "carga_nodal", "carga_distribuida",
         "carga_pontual", "pavimento")
_COLECAO = {"no": "nos", "material": "materiais", "secao": "secoes", "barra": "barras", "caso": "casos",
            "carga_nodal": "cargas_nodais", "carga_distribuida": "cargas_distribuidas",
            "carga_pontual": "cargas_pontuais", "pavimento": "pavimentos"}
_PLURAL = {v: k for k, v in _COLECAO.items()}
_POR_NOME = ("material", "secao", "caso", "pavimento")  # chave = nome (o resto = id)
_LISTAS = ("carga_nodal", "carga_distribuida", "carga_pontual")
_TIPOS_CASO = ("G", "Q", "V", "S", "E")
_TIPOS_BARRA = ("aco", "pff", "concreto")
_APOIOS = {"engaste": (True,) * 6, "rotula": (True, True, True, False, False, False),
           "rolete_z": (False, False, True, False, False, False), "livre": (False,) * 6}
_PARAMS_BARRA = {"kx", "ky", "kz", "Lb", "Cb", "rotula_i", "rotula_j", "trelica", "papel", "elemento_desloc",
                 "An", "Ct", "fyk_MPa", "fywk_MPa", "le_x", "le_y", "alfa_b", "cobrimento_m", "caa", "d_m",
                 "t0_meses", "ligacao_uma_aba", "fator_EI", "emenda", "Lv", "tubo_com_costura", "Ndist", "Mdist",
                 "trecho_rigido_i", "trecho_rigido_j", "cortante", "l0_x", "l0_y", "t_alvenaria_meses",
                 "fracao_qp_pos_alvenaria", "ponto_aplicacao", "C2",
                 "cardinal", "espelho"}  # espelho: seção refletida no desenho (secoes.espelhar: resistências invariantes); cardinal: justificação do desenho (1..9 da caixa, 10 = centroide); a análise usa o eixo
_AUTO = ("V+X", "V-X", "V+Y", "V-Y", "SX", "SY")
_RE_AUTO_V = re.compile(r"V[SM]?[+-][XY]( Cpi=.*)?")  # vento de edifício/barras ("V+X"), galpão ("V+X Cpi=-0.3"), NBR 8400 ("VS+X", "VM+X")
_RE_AUTO_S = re.compile(r"S[XY](" + re.escape(_comb.TORCAO) + ")?")  # sismo e torção acidental


def _eh_auto(nome):
    return bool(_RE_AUTO_V.fullmatch(nome) or _RE_AUTO_S.fullmatch(nome))


def _tipo(tipo):
    t = str(tipo).strip().lower()
    t = _PLURAL.get(t, t)
    if t not in TIPOS:
        raise ErroEntrada(f"tipo '{tipo}' desconhecido; use um de {', '.join(TIPOS)}", "tipo")
    return t


def _colecao(m, tipo):
    return getattr(m, _COLECAO[tipo])


def _chave_de(tipo, obj):
    return obj.nome if tipo in _POR_NOME else obj.id


def _el_dict(obj):
    d = _puro(asdict(obj))
    d.setdefault("id", d.get("nome"))
    return d


def _elementos(m, tipo):
    col = _colecao(m, tipo)
    return list(col.values()) if isinstance(col, dict) else list(col)


def _obter(m, tipo, id_):
    """(objeto, posição na lista | None). ErroNaoEncontrado se não existir."""
    col = _colecao(m, tipo)
    if isinstance(col, dict):
        if not isinstance(id_, str) or id_ not in col:
            raise ErroNaoEncontrado(tipo, id_, col)
        return col[id_], None
    for i, c in enumerate(col):
        if c.id == id_:
            return c, i
    raise ErroNaoEncontrado(tipo, id_, [c.id for c in col])


def _id_livre(colecao, prefixo):
    usados = [int(g.group(1)) for k in colecao if (g := re.fullmatch(prefixo + r"(\d+)", str(k)))]
    n = max(usados + [0]) + 1
    while f"{prefixo}{n}" in colecao:
        n += 1
    return f"{prefixo}{n}"


# ================================================================ construção/validação de elementos

def _apoio(v, err):
    if isinstance(v, str):
        if v not in _APOIOS:
            err("apoio", f"'{v}' inválido; use {', '.join(_APOIOS)} ou lista de 6 booleanos")
            return None
        return _APOIOS[v]
    if (isinstance(v, (list, tuple)) and len(v) == 6
            and all(isinstance(a, bool) or a in (0, 1) for a in v)):
        return tuple(bool(a) for a in v)
    err("apoio", f"lista de 6 booleanos (ux, uy, uz, rx, ry, rz) ou atalho {'/'.join(_APOIOS)}; recebido {v!r}")
    return None


def _c_no(m, d, err, ignorar):
    _chaves(d, {"id", "x", "y", "z", "apoio", "molas", "recalques"}, err)
    id_ = str(d["id"]) if d.get("id") not in (None, "") else _id_livre(m.nos, "n")
    if id_ in m.nos and id_ != ignorar:
        err("id", f"id '{id_}' repetido")
    x, y, z = (_num(d.get(c), c, err) for c in ("x", "y", "z"))
    ap = _apoio(d.get("apoio", "livre"), err)
    extra = {c: _vetor(d[c], 6, c, err) for c in ("molas", "recalques") if d.get(c) is not None}
    if None in (x, y, z, ap) or None in extra.values():
        return None
    try:
        return No(id_, x, y, z, ap, **extra)
    except ValueError as e:  # mola negativa, recalque em GL livre
        err("recalques" if "recalque" in str(e) else "molas", str(e))
        return None


def _c_material(m, d, err, ignorar):
    try:
        mat = _mat.material_de_spec(d)
    except (ValueError, TypeError, KeyError) as e:
        err("material", str(e))
        return None
    if mat.nome in m.materiais and mat.nome != ignorar:
        err("nome", f"material '{mat.nome}' já existe")
    for c in ("E", "G"):
        if not (math.isfinite(getattr(mat, c)) and getattr(mat, c) > 0):
            err(c, "deve ser finito e > 0")
    return mat


def _c_secao(m, d, err, ignorar):
    try:
        sec = _sec.secao_de_spec(d)
    except (ValueError, TypeError, KeyError) as e:
        err("secao", str(e))
        return None
    if sec.nome in m.secoes and sec.nome != ignorar:
        err("nome", f"seção '{sec.nome}' já existe")
    for c in ("A", "Iy", "Iz", "J"):
        if not (math.isfinite(getattr(sec, c)) and getattr(sec, c) > 0):
            err(c, "deve ser finito e > 0")
    return sec


def _tipo_padrao(sec, mat):
    """Tipo da barra (ESPEC §6.3): material com fck → concreto; Ue/U formado a frio → pff; senão aço."""
    if mat.fck is not None:
        return "concreto"
    p = sec.props or {}
    if p.get("tipo") == "Ue" or (p.get("tipo") == "U" and "t" in p and "tw" not in p):
        return "pff"
    return "aco"


def _val_trechos_cortante(p, err, comprimento=None, secao=None):
    if not isinstance(p, dict):
        return  # o formato de params é conferido por _val_params
    trechos = []
    for k in ("trecho_rigido_i", "trecho_rigido_j"):
        trechos.append(_num(p[k], f"params.{k}", err, nao_neg=True) if k in p else 0.0)
    if comprimento is not None and all(v is not None for v in trechos) and sum(trechos) >= comprimento:
        for k in ("trecho_rigido_i", "trecho_rigido_j"):
            if k in p:
                err(f"params.{k}", "a soma dos trechos rígidos deve ser menor que o comprimento da barra")
    if "cortante" in p and not isinstance(p["cortante"], bool):
        err("params.cortante", "true/false")
    if p.get("cortante") is True and secao is not None:
        props = secao.props or {}
        if not all(_ok_num(props.get(k)) and props[k] > 0 for k in ("Ay", "Az")):
            err("params.cortante", f"seção {secao.nome} sem área de cisalhamento Ay/Az finita e > 0")


def _val_params(p, err):
    if not isinstance(p, dict):
        err("params", "esperado objeto")
        return
    for k in ("kx", "ky", "kz", "Lb", "Cb", "An", "Ct", "fator_EI", "Lv", "Ndist", "Mdist", "fyk_MPa", "fywk_MPa", "le_x", "le_y", "alfa_b",
              "cobrimento_m", "d_m", "l0_x", "l0_y", "C2"):
        if p.get(k) is not None:
            _num(p[k], f"params.{k}", err, pos=True)
    for k in ("t0_meses", "t_alvenaria_meses"):
        if p.get(k) is not None:
            _num(p[k], f"params.{k}", err, nao_neg=True)
    fr = p.get("fracao_qp_pos_alvenaria")
    if fr is not None and not (_ok_num(fr) and 0.0 <= fr <= 1.0):
        err("params.fracao_qp_pos_alvenaria", "número entre 0 e 1 (NBR 6118 13.3)")
    if p.get("ponto_aplicacao") not in (None, *_nbr8800._PONTOS):
        err("params.ponto_aplicacao", f"use {', '.join(_nbr8800._PONTOS)} (altura da carga na mesa, NBR 8800 G.1/SN003b)")
    # faixas da norma (VC-guia-002): o motor também limita, mas o erro aparece já na edição
    cb, ct = p.get("Cb"), p.get("Ct")
    if _ok_num(cb) and not 1.0 <= cb <= 3.0:
        err("params.Cb", f"deve estar entre 1,0 e 3,0 (NBR 8800:2008 5.4.2.3); recebido {cb:g}")
    if _ok_num(ct) and ct > 1.0:
        err("params.Ct", f"deve ser ≤ 1,0 (NBR 8800:2008 5.2.5); recebido {ct:g}")
    if p.get("emenda") not in (None, "traspasse", "luva"):
        err("params.emenda", "use 'traspasse' ou 'luva' (NBR 6118 17.3.5.3.2)")
    for k in ("rotula_i", "rotula_j", "trelica", "tubo_com_costura"):
        if k in p and not isinstance(p[k], bool):
            err(f"params.{k}", "true/false")
    if p.get("papel") not in (None, "viga", "pilar"):
        err("params.papel", "use 'viga' ou 'pilar'")
    card = p.get("cardinal")
    if card is not None and (isinstance(card, bool) or not isinstance(card, int) or not 1 <= card <= 10):
        err("params.cardinal", "inteiro de 1 a 10 (ponto cardinal da seção; 5 = centro, 10 = centroide)")
    if p.get("caa") is not None and str(p["caa"]).upper() not in _nbr6118.CNOM:
        err("params.caa", "use I, II, III ou IV")


def _c_barra(m, d, err, ignorar):
    _chaves(d, {"id", "ni", "nj", "secao", "material", "tipo", "rot", "params"}, err)
    id_ = str(d["id"]) if d.get("id") not in (None, "") else _id_livre(m.barras, "b")
    if id_ in m.barras and id_ != ignorar:
        err("id", f"id '{id_}' repetido")
    ref = {}
    for campo, col in (("ni", m.nos), ("nj", m.nos), ("secao", m.secoes), ("material", m.materiais)):
        v = d.get(campo)
        if not isinstance(v, str) or v not in col:
            err(campo, f"{'nó' if campo in ('ni', 'nj') else campo} '{v}' não existe"
                + (f"; válidos: {', '.join(sorted(map(str, col))[:20])}" if col else ""))
        else:
            ref[campo] = v
    if "ni" in ref and "nj" in ref:
        if ref["ni"] == ref["nj"] or _comprimento(m, Barra("", ref["ni"], ref["nj"], "", "")) < 1e-9:
            err("nj", "barra de comprimento nulo")
    rot = _num(d.get("rot", 0.0), "rot", err)
    tipo = d.get("tipo")
    if tipo is not None and tipo not in _TIPOS_BARRA:
        err("tipo", f"'{tipo}' inválido; use {', '.join(_TIPOS_BARRA)}")
    elif tipo is None and "secao" in ref and "material" in ref:
        tipo = _tipo_padrao(m.secoes[ref["secao"]], m.materiais[ref["material"]])
    params = d.get("params") or {}
    _val_params(params, err)
    comprimento = (_comprimento(m, Barra("", ref["ni"], ref["nj"], "", ""))
                   if "ni" in ref and "nj" in ref else None)
    _val_trechos_cortante(params, err, comprimento, m.secoes.get(ref.get("secao")))
    if len(ref) < 4 or rot is None or tipo is None or not isinstance(params, dict):
        return None
    return Barra(id_, ref["ni"], ref["nj"], ref["secao"], ref["material"], rot, tipo, dict(params))


def _c_caso(m, d, err, ignorar):
    _chaves(d, {"id", "nome", "tipo", "subtipo"}, err)
    nome = d.get("nome", d.get("id"))
    if not isinstance(nome, str) or not nome.strip():
        err("nome", "texto não vazio obrigatório")
        return None
    if nome in m.casos and nome != ignorar:
        err("nome", f"caso '{nome}' já existe")
    if d.get("tipo") not in _TIPOS_CASO:
        err("tipo", f"'{d.get('tipo')}' inválido; use {', '.join(_TIPOS_CASO)}")
        return None
    sub = str(d.get("subtipo") or "")
    if d["tipo"] == "Q" and sub and sub not in _comb.USOS_Q:
        err("subtipo", f"'{sub}' inválido para caso Q; use um de {', '.join(_comb.USOS_Q)} "
            "(vazio = config.tipo_uso)")
        return None
    return CasoCarga(nome, d["tipo"], sub)


def _c_pavimento(m, d, err, ignorar):
    _chaves(d, {"id", "nome", "z", "diafragma"}, err)
    nome = d.get("nome", d.get("id"))
    if not isinstance(nome, str) or not nome.strip():
        err("nome", "texto não vazio obrigatório")
        return None
    if nome in m.pavimentos and nome != ignorar:
        err("nome", f"pavimento '{nome}' já existe")
    z = _num(d.get("z"), "z", err)
    dia = d.get("diafragma", True)
    if not isinstance(dia, bool):
        err("diafragma", "true/false")
    return None if z is None or not isinstance(dia, bool) else Pavimento(nome, z, dia)


def _c_carga(tipo):
    classe, alvo, campo, n = {"carga_nodal": (CargaNodal, "no", "F", 6),
                              "carga_distribuida": (CargaDistribuida, "barra", "q", 3),
                              "carga_pontual": (CargaPontual, "barra", "F", 3)}[tipo]

    def construir(m, d, err, ignorar):
        _chaves(d, {"id", "caso", alvo, campo} | ({"a"} if tipo == "carga_pontual" else set())
                | ({"q2"} if tipo == "carga_distribuida" else set()), err)
        id_ = str(d.get("id") or "")
        if id_ and id_ != ignorar and any(c.id == id_ for c in _colecao(m, tipo)):
            err("id", f"id '{id_}' repetido")
        ok_caso = isinstance(d.get("caso"), str) and d.get("caso") in m.casos
        if not ok_caso:
            err("caso", f"caso '{d.get('caso')}' não existe" +
                (f"; válidos: {', '.join(sorted(m.casos)[:20])}" if m.casos else ""))
        col = m.nos if alvo == "no" else m.barras
        ok_alvo = isinstance(d.get(alvo), str) and d.get(alvo) in col
        if not ok_alvo:
            err(alvo, f"{'nó' if alvo == 'no' else 'barra'} '{d.get(alvo)}' não existe" +
                (f"; válidos: {', '.join(sorted(col)[:20])}" if col else ""))
        vet = _vetor(d.get(campo), n, campo, err)
        extra = {}
        if tipo == "carga_pontual":
            a = _num(d.get("a"), "a", err, nao_neg=True)
            if a is not None and ok_alvo and a > _comprimento(m, m.barras[d[alvo]]) + 1e-9:
                err("a", f"a = {a:g} m excede o comprimento da barra")
            extra["a"] = a
            if a is None:
                return None
        if tipo == "carga_distribuida" and d.get("q2") is not None:
            extra["q2"] = _vetor(d["q2"], 3, "q2", err)
            if extra["q2"] is None:
                return None
        if vet is None or not ok_caso or not ok_alvo:
            return None
        return classe(caso=d["caso"], **{alvo: d[alvo]}, **{campo: vet}, **extra, id=id_)
    return construir


_CONSTRUTORES = {"no": _c_no, "material": _c_material, "secao": _c_secao, "barra": _c_barra,
                 "caso": _c_caso, "pavimento": _c_pavimento, **{t: _c_carga(t) for t in _LISTAS}}


def _construir(m, tipo, dados, ignorar=None):
    if not isinstance(dados, dict):
        raise ErroEntrada(f"dados: esperado objeto, recebido {type(dados).__name__}", tipo)
    erros = []
    obj = _CONSTRUTORES[tipo](m, dict(dados), lambda c, msg: erros.append({"campo": c, "mensagem": msg}),
                              ignorar)
    if erros or obj is None:
        raise ErroEntrada(erros or [{"campo": tipo, "mensagem": "dados inválidos"}])
    return obj


# ================================================================ 1-4: modelo e arquivo

def novo_modelo(nome=""):
    """#1 Modelo vazio (`versao_formato` 1, config com os padrões)."""
    return Modelo(nome=str(nome), config=copy.deepcopy(CONFIG_PADRAO))


def para_dict(modelo):
    """#4 Modelo → dict JSON (formato da ESPEC §5)."""
    return modelo.to_dict()


_NUM_CAMPOS = {"x", "y", "z", "E", "G", "A", "Iy", "Iz", "J", "rot", "a", "peso"}
_VET_CAMPOS = {"apoio": 6, "molas": 6, "recalques": 6, "F": None, "q": 3, "q2": 3}


def _esquema(d):
    """Valida a estrutura do JSON do modelo e devolve TODOS os erros (campo, mensagem)."""
    erros = []

    def err(c, msg):
        erros.append({"campo": c, "mensagem": msg})
    if not isinstance(d, dict):
        return [{"campo": "", "mensagem": "esperado um objeto JSON"}]
    if d.get("versao_formato", 0) not in VERSOES_LIDAS:
        err("versao_formato", f"{d.get('versao_formato')!r} não suportada (máximo {VERSAO_FORMATO})")
    lig = d.get("ligacoes", {})
    if not isinstance(lig, dict) or not isinstance(lig.get("grupos", {}), dict):
        err("ligacoes", "esperado objeto {grupos: {id: {...}}}")
    else:
        for gid, g in lig.get("grupos", {}).items():
            if not isinstance(g, dict) or not isinstance(g.get("membros"), list) or not all(
                    isinstance(x, dict) and {"no", "barra", "ponta"} <= set(x) for x in g["membros"]):
                err(f"ligacoes.grupos.{gid}", "esperado {nome, tipo, assinatura, membros: [{no, barra, ponta}], ...}")
    secoes = (("nos", No), ("materiais", Material), ("secoes", Secao), ("barras", Barra),
              ("casos", CasoCarga), ("pavimentos", Pavimento), ("cargas_nodais", CargaNodal),
              ("cargas_distribuidas", CargaDistribuida), ("cargas_pontuais", CargaPontual))
    for chave, cls in secoes:
        sec = d.get(chave, [] if chave.startswith("cargas") else {})
        eh_lista = chave.startswith("cargas")
        if not isinstance(sec, list if eh_lista else dict):
            err(chave, f"esperado {'lista' if eh_lista else 'objeto'}")
            continue
        for k, v in (enumerate(sec) if eh_lista else sec.items()):
            loc = f"{chave}[{k}]" if eh_lista else f"{chave}.{k}"
            if not isinstance(v, dict):
                err(loc, "esperado objeto")
                continue
            nomes = {f.name for f in fields(cls)}
            for c in v:
                if c not in nomes:
                    err(f"{loc}.{c}", "campo desconhecido")
            for f in fields(cls):
                if f.default is MISSING and f.default_factory is MISSING and f.name not in v:
                    if not (f.name in ("id", "nome") and not eh_lista):  # vem da chave do objeto
                        err(f"{loc}.{f.name}", "campo obrigatório ausente")
            for c, val in v.items():
                if c in _NUM_CAMPOS and not (isinstance(val, (int, float)) and not isinstance(val, bool)
                                             and math.isfinite(val)):
                    if not (c in ("peso",) and val is None):
                        err(f"{loc}.{c}", f"número finito esperado (recebido {val!r})")
                if c in _VET_CAMPOS and not (c == "q2" and val is None):
                    n =_VET_CAMPOS[c] or (6 if cls is CargaNodal else 3)
                    if not (isinstance(val, (list, tuple)) and len(val) == n):
                        err(f"{loc}.{c}", f"lista de {n} valores esperada")
                    elif c != "apoio" and not all(isinstance(x, (int, float)) and not isinstance(x, bool)
                                                  and math.isfinite(x) for x in val):
                        err(f"{loc}.{c}", "valores devem ser números finitos")
            if cls is CasoCarga and v.get("tipo") not in _TIPOS_CASO:
                err(f"{loc}.tipo", f"use {', '.join(_TIPOS_CASO)}")
            if cls is Barra and v.get("tipo", "aco") not in _TIPOS_BARRA:
                err(f"{loc}.tipo", f"use {', '.join(_TIPOS_BARRA)}")
    for c in ("config",):
        if c in d and not isinstance(d[c], dict):
            err(c, "esperado objeto")
    return erros


def de_dict(d):
    """#4 dict (formato da ESPEC §5, v0 ou v1) → Modelo. ErroEntrada com a lista completa de problemas."""
    erros = _esquema(d)
    if erros:
        raise ErroEntrada(erros)
    d = copy.deepcopy(d)
    for chave in ("nos", "barras"):
        for k, v in d.get(chave, {}).items():
            v.setdefault("id", k)
    for chave in ("materiais", "secoes", "casos", "pavimentos"):
        for k, v in d.get(chave, {}).items():
            v.setdefault("nome", k)
    try:
        m = Modelo.from_dict(d)
    except (ValueError, TypeError, KeyError) as e:
        raise ErroEntrada(f"modelo inválido: {e}") from e
    m.config = {**CONFIG_PADRAO, **(m.config or {})} if m.config else copy.deepcopy(CONFIG_PADRAO)
    return m


def carregar(caminho):
    """#2 Lê um modelo `.json` (v0 ou v1)."""
    p = Path(caminho)
    try:
        texto = p.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise ErroEntrada(f"arquivo não encontrado: {p}", "caminho") from None
    except (OSError, UnicodeDecodeError) as e:
        raise ErroEntrada(f"não foi possível ler {p}: {e}", "caminho") from None
    try:
        d = json.loads(texto)
    except json.JSONDecodeError as e:
        raise ErroEntrada(f"JSON inválido em {p.name}: {e}", "caminho") from None
    return de_dict(d)


def _gravar(caminho, texto):
    p = Path(caminho)
    try:
        p.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(dir=p.parent, prefix=p.name + ".", suffix=".tmp")
        try:
            with os.fdopen(fd, "wb") as f:
                dados = texto.encode("utf-8") if isinstance(texto, str) else texto
                f.write(dados)
            os.replace(tmp, p)
        except BaseException:
            if os.path.exists(tmp):
                os.unlink(tmp)
            raise
    except OSError as e:
        raise ErroEntrada(f"não foi possível gravar {p}: {e}", "caminho") from None
    return {"caminho": str(p), "bytes": len(dados)}


def salvar(modelo, caminho):
    """#3 Grava o modelo (cria a pasta). → {"caminho", "bytes"}."""
    return _gravar(caminho, json.dumps(modelo.to_dict(), ensure_ascii=False, indent=1))


# ================================================================ 5-9: edição

def adicionar(modelo, tipo, dados):
    """#5 Adiciona um elemento (formatos de `dados` na ESPEC §6.3) e devolve o armazenado, com id."""
    tipo = _tipo(tipo)
    obj = _construir(modelo, tipo, dados)
    modelo.add(obj)
    return _el_dict(obj)


def _copiar_para(destino, origem):
    for c in ("nos", "materiais", "secoes", "barras", "casos", "cargas_nodais", "cargas_distribuidas",
              "cargas_pontuais", "pavimentos", "peso_proprio", "config", "nome"):
        setattr(destino, c, getattr(origem, c))
    destino._ultimo_id = origem._ultimo_id


def adicionar_lote(modelo, itens):
    """#6 Atômico: trabalha numa cópia e só troca no fim. Erros com o índice do item no campo
    ("itens[3].ni"). → {"criados": [{"tipo", "id"}]}."""
    if not isinstance(itens, list):
        raise ErroEntrada("itens: esperada lista de {tipo, dados}", "itens")
    copia = Modelo.from_dict(modelo.to_dict())
    copia._ultimo_id = dict(modelo._ultimo_id)
    criados, erros = [], []
    for i, it in enumerate(itens):
        if not isinstance(it, dict) or "tipo" not in it or "dados" not in it:
            erros.append({"campo": f"itens[{i}]", "mensagem": "esperado {tipo, dados}"})
            continue
        try:
            t = _tipo(it["tipo"])
            obj = _construir(copia, t, it["dados"])
        except ErroEntrada as e:
            erros += [{"campo": f"itens[{i}]" + (f".{x['campo']}" if x["campo"] else ""),
                       "mensagem": x["mensagem"]} for x in e.erros]
            continue
        copia.add(obj)
        criados.append({"tipo": t, "id": _chave_de(t, obj)})
    if erros:
        raise ErroEntrada(erros)
    _copiar_para(modelo, copia)
    return {"criados": criados}


def _editar_material(m, atual, dados):
    if any(k in dados for k in ("aco", "concreto", "armadura")):
        spec = dict(dados)
    else:
        spec = {"nome": atual.nome, "E_MPa": atual.E / MPa, "G_MPa": atual.G / MPa, "peso_kN_m3": atual.peso,
                "fy_MPa": None if atual.fy is None else atual.fy / MPa,
                "fu_MPa": None if atual.fu is None else atual.fu / MPa,
                "fck_MPa": None if atual.fck is None else atual.fck / MPa, **dados}
    novo = _construir(m, "material", spec, ignorar=atual.nome)
    novo.nome = atual.nome  # o id não muda
    return novo


def _editar_secao(m, atual, dados):
    if "perfil" in dados or "forma" in dados:
        spec = dict(dados)
    else:
        spec = {"nome": atual.nome, "A": atual.A, "Iy": atual.Iy, "Iz": atual.Iz, "J": atual.J,
                **{k: v for k, v in dados.items() if k != "props"},
                "props": {**atual.props, **(dados.get("props") or {})}}
    novo = _construir(m, "secao", spec, ignorar=atual.nome)
    novo.nome = atual.nome
    return novo


def editar(modelo, tipo, id, dados):  # noqa: A002
    """#7 Atualização parcial (o id não muda; `params` de barra e `props` de seção são mesclados;
    valor None em `params` apaga a chave). Nada muda se houver erro."""
    tipo = _tipo(tipo)
    atual, pos = _obter(modelo, tipo, id)
    if not isinstance(dados, dict) or not dados:
        raise ErroEntrada("dados: objeto não vazio obrigatório", tipo)
    for k in ("id", "nome"):
        if k in dados and dados[k] != id and (k == "id" or tipo in _POR_NOME):
            raise ErroEntrada(f"{k} não pode mudar ('{id}' → '{dados[k]}'); remova e adicione de novo", k)
    if tipo == "material":
        novo = _editar_material(modelo, atual, dados)
    elif tipo == "secao":
        novo = _editar_secao(modelo, atual, dados)
    else:
        mesclado = {**_el_dict(atual), **dados}
        if tipo == "barra" and "tipo" not in dados and ("material" in dados or "secao" in dados):
            # o tipo acompanha o material: trocar aço ↔ concreto recalcula; senão mantém (ex.: pff escolhido à mão)
            sec, mat = modelo.secoes.get(mesclado.get("secao")), modelo.materiais.get(mesclado.get("material"))
            if sec is not None and mat is not None:
                padrao = _tipo_padrao(sec, mat)
                if "concreto" in (padrao, atual.tipo):
                    mesclado["tipo"] = padrao
        if tipo == "barra" and "params" in dados:
            p = {**atual.params, **(dados["params"] or {})}
            mesclado["params"] = {k: v for k, v in p.items() if v is not None}
        novo = _construir(modelo, tipo, mesclado, ignorar=id)
        if tipo == "no":  # mover um nó não pode zerar o comprimento de uma barra
            for b in modelo.barras.values():
                if id in (b.ni, b.nj):
                    a, c = (novo if n == id else modelo.nos[n] for n in (b.ni, b.nj))
                    if math.dist((a.x, a.y, a.z), (c.x, c.y, c.z)) < 1e-9:
                        raise ErroEntrada(f"barra {b.id} ficaria com comprimento nulo", "x")
    col = _colecao(modelo, tipo)
    if pos is None:
        col[id] = novo
    else:
        col[pos] = novo
    modelo._max_lista.clear()  # troca no mesmo lugar não muda o tamanho da lista: cache de ids vale menos que a verdade
    return _el_dict(novo)


def _dependentes(m, tipo, id_):
    """Elementos que referenciam (direta ou indiretamente) o alvo, como [(tipo, id)]."""
    dep, barras = [], set()
    if tipo == "no":
        barras = {b.id for b in m.barras.values() if id_ in (b.ni, b.nj)}
        dep += [("carga_nodal", c.id) for c in m.cargas_nodais if c.no == id_]
    elif tipo in ("secao", "material"):
        campo = "secao" if tipo == "secao" else "material"
        barras = {b.id for b in m.barras.values() if getattr(b, campo) == id_}
    elif tipo == "barra":
        barras = set()
        dep += [("carga_distribuida", c.id) for c in m.cargas_distribuidas if c.barra == id_]
        dep += [("carga_pontual", c.id) for c in m.cargas_pontuais if c.barra == id_]
    elif tipo == "caso":
        for t in _LISTAS:
            dep += [(t, c.id) for c in _colecao(m, t) if c.caso == id_]
        if m.peso_proprio == id_:
            dep.append(("config", "peso_proprio"))
    for b in sorted(barras):
        dep.append(("barra", b))
        dep += [("carga_distribuida", c.id) for c in m.cargas_distribuidas if c.barra == b]
        dep += [("carga_pontual", c.id) for c in m.cargas_pontuais if c.barra == b]
    return list(dict.fromkeys(dep))


def remover(modelo, tipo, id, cascata=False):  # noqa: A002
    """#8 Remove um elemento. Com dependentes (barra → cargas; nó → barras e cargas; seção/material →
    barras; caso → cargas) exige `cascata=True`, senão ErroEntrada lista os dependentes."""
    tipo = _tipo(tipo)
    _obter(modelo, tipo, id)
    dep = _dependentes(modelo, tipo, id)
    if dep and not cascata:
        lista = ", ".join(f"{t} {i}" for t, i in dep[:20]) + (" …" if len(dep) > 20 else "")
        raise ErroEntrada([{"campo": f"{tipo}.{id}", "mensagem": f"referenciado por: {lista} "
                            "(use cascata=true para remover tudo)"}])
    alvo = [(tipo, id)] + dep
    for t, i in alvo:
        if t == "config":
            modelo.peso_proprio = None
            continue
        col = _colecao(modelo, t)
        if isinstance(col, dict):
            del col[i]
        else:
            col[:] = [c for c in col if c.id != i]
    return {"removidos": [{"tipo": t, "id": i} for t, i in alvo if t != "config"]}


def listar(modelo, tipo, filtro=None):
    """#9 Elementos como dicts; `filtro` = igualdade de campos ({"caso": "G1"})."""
    tipo = _tipo(tipo)
    if filtro is not None and not isinstance(filtro, dict):
        raise ErroEntrada("filtro: esperado objeto", "filtro")
    itens = [_el_dict(o) for o in _elementos(modelo, tipo)]
    return [d for d in itens if all(d.get(k) == v for k, v in (filtro or {}).items())]


# ================================================================ 10: configuração

def _val_vento(v, err):
    if not isinstance(v, dict):
        err("vento", "esperado objeto (ou null para remover)")
        return
    _chaves(v, {"V0", "S1", "S3", "categoria", "classe", "direcoes", "dimensoes", "Ca", "T1_s", "tipo", "geometria",
                "permeabilidade", "cpi", "usar_K", "v_servico"}, err, "vento.")
    if v.get("tipo") not in _TIPOS_VENTO:
        err("vento.tipo", "use 'edificio', 'galpao', 'barras' (NBR 6123 8.1, por barra) ou 'nbr8400' (equipamentos de "
            "levantamento); vazio: galpão se o modelo veio do gerador e não tem pavimentos")
    if v.get("tipo") == "nbr8400":  # NBR 8400:1984 5.5.4.1: só a velocidade de serviço (o vento máximo é tabelado)
        if v.get("v_servico") is not None:
            _num(v["v_servico"], "vento.v_servico", err, pos=True)
        dirs = v.get("direcoes")
        if dirs is not None and (not isinstance(dirs, list) or not dirs or any(x not in _vento.DIRECOES for x in dirs)):
            err("vento.direcoes", f"lista de {', '.join(_vento.DIRECOES)}")
        return
    if v.get("usar_K") is not None and not isinstance(v["usar_K"], bool):
        err("vento.usar_K", "true/false (fator K da NBR 6123 tabela 28)")
    if _num(v.get("V0"), "vento.V0", err, pos=True) is not None and v["V0"] > 100:
        err("vento.V0", f"{v['V0']:g} m/s é irreal (as isopletas do Brasil ficam em torno de 30 a 50 m/s): "
            "confira a unidade (m/s)")
    for k in ("S1", "S3", "T1_s"):
        if v.get(k) is not None:
            _num(v[k], f"vento.{k}", err, pos=True)
    if v.get("Ca") is not None:
        _num(v["Ca"], "vento.Ca", err, pos=True)
    if str(v.get("categoria", "")).upper() not in _vento.TAB21:
        err("vento.categoria", f"use {', '.join(_vento.TAB21)}")
    if str(v.get("classe", "")).upper() not in _vento.FR and not (v.get("tipo") == "barras" and not v.get("classe")):
        err("vento.classe", f"use {', '.join(_vento.FR)}" + (" (vazio = automática, 5.3.2)" if v.get("tipo") == "barras" else ""))
    dirs = v.get("direcoes")
    if dirs is not None and (not isinstance(dirs, list) or not dirs or any(x not in _vento.DIRECOES for x in dirs)):
        err("vento.direcoes", f"lista de {', '.join(_vento.DIRECOES)}")
    dim = v.get("dimensoes")
    if dim is not None:
        if not isinstance(dim, dict):
            err("vento.dimensoes", "esperado {lx, ly}")
        else:
            for k in ("lx", "ly"):
                _num(dim.get(k), f"vento.dimensoes.{k}", err, pos=True)
    geo = v.get("geometria")
    if geo is not None:
        if not isinstance(geo, dict):
            err("vento.geometria", "esperado {vao, altura_pilar, altura_cumeeira, n_modulos, espacamento}")
        else:
            _chaves(geo, set(_GEOM_GALPAO), err, "vento.geometria.")
            for k in _GEOM_GALPAO:
                if k == "n_modulos":
                    if isinstance(geo.get(k), bool) or not isinstance(geo.get(k), int) or geo[k] < 1:
                        err(f"vento.geometria.{k}", f"inteiro ≥ 1 obrigatório (recebido {geo.get(k)!r})")
                else:
                    _num(geo.get(k), f"vento.geometria.{k}", err, pos=True)
    if v.get("permeabilidade") is not None and v["permeabilidade"] not in _vento.CPI:
        err("vento.permeabilidade", f"use {', '.join(_vento.CPI)} (NBR 6123 6.2.5)")
    if v.get("cpi") is not None and (not isinstance(v["cpi"], list) or not v["cpi"] or not all(_ok_num(c) for c in v["cpi"])):
        err("vento.cpi", "lista de coeficientes de pressão interna (números)")


_GEOM_GALPAO = ("vao", "altura_pilar", "altura_cumeeira", "n_modulos", "espacamento")
_TIPOS_VENTO = (None, "edificio", "galpao", "barras", "nbr8400")
_VENTO_POR_BARRA = ("barras", "nbr8400")  # carga distribuída em cada barra: não usam pavimentos nem geometria de galpão


def _val_nbr8400(v, err):
    if not isinstance(v, dict):
        err("nbr8400", "esperado {grupo 1-6, psi}")
        return
    _chaves(v, {"grupo", "psi"}, err, "nbr8400.")
    g = v.get("grupo", 3)
    if isinstance(g, bool) or not isinstance(g, int) or not 1 <= g <= 6:
        err("nbr8400.grupo", "inteiro de 1 a 6 (NBR 8400 tabela 10, Mx)")
    _num(v.get("psi", _nbr8400.PSI_PADRAO), "nbr8400.psi", err, pos=True)


def _val_sismo(v, err):
    if not isinstance(v, dict):
        err("sismo", "esperado objeto (ou null para remover)")
        return
    _chaves(v, {"zona", "classe_terreno", "categoria_utilizacao", "R", "sistema", "ag_g", "sistema_resistente",
                "dimensoes"}, err, "sismo.")
    z = v.get("zona")
    if isinstance(z, bool) or not isinstance(z, int) or not 0 <= z <= 4:
        err("sismo.zona", "inteiro de 0 a 4")
        return
    if z == 0:
        return
    if str(v.get("classe_terreno", "")).upper() not in _sismo.TAB3:
        err("sismo.classe_terreno", f"use {', '.join(_sismo.TAB3)}")
    if str(v.get("categoria_utilizacao", "I")).upper() not in _sismo.FATOR_I:
        err("sismo.categoria_utilizacao", f"use {', '.join(_sismo.FATOR_I)}")
    sr = v.get("sistema_resistente")
    if sr is not None and sr not in _sismo.TAB6:
        err("sismo.sistema_resistente", f"use {', '.join(_sismo.TAB6)} (NBR 15421:2006 tabela 6)")
    _num(v.get("R"), "sismo.R", err, pos=True, obrig=sr is None)  # com sistema_resistente, R vem da tabela 6
    if v.get("sistema", "concreto_portico") not in _sismo.CT_X:
        err("sismo.sistema", f"use {', '.join(_sismo.CT_X)}")
    dim = v.get("dimensoes")
    if dim is not None:
        if not isinstance(dim, dict):
            err("sismo.dimensoes", "esperado {lx, ly}")
        else:
            for k in ("lx", "ly"):
                _num(dim.get(k), f"sismo.dimensoes.{k}", err, pos=True)
    if v.get("ag_g") is not None:
        _num(v["ag_g"], "sismo.ag_g", err, pos=True)


_TIPOS_EDIFICACAO = _comb.TIPOS_EDIFICACAO


def configurar(modelo, dados):
    """#10 Merge raso em `config` (e `peso_proprio`); `vento`/`sismo` substituídos inteiros (null remove).
    → config completa."""
    if not isinstance(dados, dict):
        raise ErroEntrada("dados: esperado objeto", "config")
    erros = []

    def err(c, msg):
        erros.append({"campo": c, "mensagem": msg})
    validos = set(CONFIG_PADRAO) | {"vento", "sismo", "peso_proprio"}
    _chaves(dados, validos, err)
    usos = sorted(k for k in _comb.PSI if k not in ("vento", "temperatura"))
    if "tipo_uso" in dados and dados["tipo_uso"] not in usos:
        err("tipo_uso", f"use {', '.join(usos)}")
    if "variabilidade" in dados and dados["variabilidade"] not in ("grandes", "pequenas"):
        err("variabilidade", "use 'grandes' ou 'pequenas'")
    if "pdelta" in dados and not isinstance(dados["pdelta"], bool):
        err("pdelta", "true/false")
    if dados.get("tipo_edificacao") not in _TIPOS_EDIFICACAO:
        err("tipo_edificacao", "use null, 'tipo1', 'tipo2' ou 'separadas'")
    if "armadura" in dados and dados["armadura"] not in _mat.ARMADURAS:
        err("armadura", f"use {', '.join(_mat.ARMADURAS)}")
    if dados.get("cobrimento_m") is not None:
        _num(dados["cobrimento_m"], "cobrimento_m", err, pos=True)
    if "caa" in dados and str(dados["caa"]).upper() not in _nbr6118.CNOM:
        err("caa", "use I, II, III ou IV")
    if "combinacoes" in dados and dados["combinacoes"] not in ("nbr8681", "nbr8400"):
        err("combinacoes", "use 'nbr8681' (padrão) ou 'nbr8400' (equipamentos de levantamento, casos I/II/III)")
    if "nbr8400" in dados:
        _val_nbr8400(dados["nbr8400"], err)
    if dados.get("vento") is not None:
        _val_vento(dados["vento"], err)
    if dados.get("sismo") is not None:
        _val_sismo(dados["sismo"], err)
    if dados.get("peso_proprio") is not None and dados["peso_proprio"] not in modelo.casos:
        err("peso_proprio", f"caso '{dados['peso_proprio']}' não existe")
    if erros:
        raise ErroEntrada(erros)
    cfg = dict(modelo.config or {})
    for k, v in dados.items():
        if k == "peso_proprio":
            modelo.peso_proprio = v
        elif k in ("vento", "sismo") and v is None:
            cfg.pop(k, None)
        else:
            cfg[k] = copy.deepcopy(v)
    modelo.config = cfg
    return copy.deepcopy(_cfg(modelo))


# ================================================================ 11-13: resumo, validação, geometria

def _caixa(m):
    if not m.nos:
        return {"min": [0.0, 0.0, 0.0], "max": [0.0, 0.0, 0.0]}
    xyz = np.array([[n.x, n.y, n.z] for n in m.nos.values()])
    return {"min": xyz.min(0).tolist(), "max": xyz.max(0).tolist()}


def resumo(modelo):
    """#11 Contagens e caixa envolvente."""
    m = modelo
    return _puro({
        "nome": m.nome, "n_nos": len(m.nos), "n_barras": len(m.barras), "n_casos": len(m.casos),
        "n_cargas": len(m.cargas_nodais) + len(m.cargas_distribuidas) + len(m.cargas_pontuais),
        "materiais": list(m.materiais), "secoes": list(m.secoes),
        "casos": [{"nome": c.nome, "tipo": c.tipo} for c in m.casos.values()],
        "pavimentos": [{"nome": p.nome, "z": p.z, "diafragma": p.diafragma} for p in m.pavimentos.values()],
        "caixa": _caixa(m), "peso_proprio": m.peso_proprio, "config": copy.deepcopy(_cfg(m))})


_ESTRUTURAIS = {"sem_barras", "sem_apoio", "referencia_ausente", "barra_comprimento_nulo", "carga_fora_da_barra",
                "config_invalida", "geometria_absurda"}
COORD_MAX_M = 1e5  # 100 km: acima disso a unidade está errada e o solver perde a precisão (E2E-003)
LADO_SECAO_SUSPEITO_M = 5.0  # seção com lado maior que isto: provável cm/mm digitado como m (E2E-002)
COMPRIMENTO_BARRA_SUSPEITO_M = 200.0


def _base(m):
    """Cota da base: a menor cota dos nós apoiados (0 se não há apoio; `validar` acusa). Alturas de
    vento, sismo, γz e B2 são medidas a partir dela (VC-integracao-004)."""
    return min((n.z for n in m.nos.values() if any(n.apoio)), default=0.0)


def _pavimentos_acima(m):
    """[(nome, z absoluto)] dos pavimentos acima da base, do mais baixo ao mais alto."""
    base = _base(m)
    return [(p.nome, p.z) for p in sorted(m.pavimentos.values(), key=lambda p: p.z) if p.z > base + _sol.TOL_PAV]


def validar(modelo):
    """#12 Lista de {nivel: erro|aviso, codigo, mensagem, ref}."""
    m, out = modelo, []

    def add(nivel, codigo, msg, ref=None):
        out.append({"nivel": nivel, "codigo": codigo, "mensagem": msg, "ref": ref})
    cfg = _cfg(m)
    if cfg["tipo_edificacao"] not in _TIPOS_EDIFICACAO:
        add("erro", "config_invalida", f"config.tipo_edificacao {cfg['tipo_edificacao']!r}: use null, 'tipo1', 'tipo2' ou 'separadas'")
    if not m.barras:
        add("erro", "sem_barras", "o modelo não tem barras")
    if m.nos and not any(any(n.apoio) for n in m.nos.values()):
        add("erro", "sem_apoio", "nenhum nó tem apoio: a estrutura é um mecanismo")
    for n in m.nos.values():
        if max(abs(n.x), abs(n.y), abs(n.z)) > COORD_MAX_M:
            add("erro", "geometria_absurda", f"nó {n.id}: coordenada além de {COORD_MAX_M:g} m (confira a unidade: m)",
                {"tipo": "no", "id": n.id})
    for sec in m.secoes.values():
        grande = {k: v for k, v in (sec.props or {}).items() if k in ("h", "b", "D") and _ok_num(v) and v > LADO_SECAO_SUSPEITO_M}
        if grande:
            add("aviso", "dimensao_suspeita", f"seção {sec.nome}: lado de {max(grande.values()):g} m; confira a unidade "
                "(as medidas são em m, não cm nem mm)", {"tipo": "secao", "id": sec.nome})
    usados = {n for b in m.barras.values() for n in (b.ni, b.nj)}
    for n in m.nos.values():
        if n.id not in usados:
            add("aviso", "no_isolado", f"nó {n.id} não pertence a nenhuma barra", {"tipo": "no", "id": n.id})
    for b in m.barras.values():
        ref = {"tipo": "barra", "id": b.id}
        faltam = [f"{c} '{getattr(b, c)}'" for c, col in (("ni", m.nos), ("nj", m.nos), ("secao", m.secoes),
                                                          ("material", m.materiais)) if getattr(b, c) not in col]
        if faltam:
            add("erro", "referencia_ausente", f"barra {b.id} referencia inexistente: {', '.join(faltam)}", ref)
            continue
        if _comprimento(m, b) < 1e-9:
            add("erro", "barra_comprimento_nulo", f"barra {b.id} tem comprimento nulo", ref)
        elif _comprimento(m, b) > COMPRIMENTO_BARRA_SUSPEITO_M:
            add("aviso", "dimensao_suspeita", f"barra {b.id}: comprimento de {_comprimento(m, b):g} m; confira a "
                "unidade (m, não mm)", ref)
        sec, mat, p = m.secoes[b.secao], m.materiais[b.material], (m.secoes[b.secao].props or {})
        if b.tipo == "concreto":
            if p.get("tipo") != "retangular":
                add("aviso", "secao_incompativel", f"barra {b.id}: concreto só verifica seção retangular "
                    f"(seção '{sec.nome}' é '{p.get('tipo', 'explícita')}')", ref)
            if mat.fck is None:
                add("aviso", "material_incompativel", f"barra {b.id}: barra de concreto com material '{mat.nome}' sem fck "
                    "(troque o material ou o tipo da barra)", ref)
        else:
            if mat.fy is None:
                add("aviso", "material_incompativel", f"barra {b.id}: barra de {b.tipo} com material '{mat.nome}' sem fy "
                    "(troque o material ou o tipo da barra)", ref)
            if p.get("tipo") == "I" and not ("tw" in p and "tf" in p):
                add("aviso", "secao_incompativel", f"barra {b.id}: seção I '{sec.nome}' sem tw/tf", ref)
            if b.tipo == "pff" and p.get("tipo") not in ("Ue", "U"):
                add("aviso", "secao_incompativel", f"barra {b.id}: perfil formado a frio exige seção Ue/U "
                    f"(seção '{sec.nome}')", ref)
        _val_trechos_cortante(
            b.params, lambda campo, msg: add("erro", "param_invalido", f"{campo}: {msg}", ref),
            _comprimento(m, b), sec)
        desconhecidos = sorted(set(b.params) - _PARAMS_BARRA)
        if desconhecidos:
            add("aviso", "param_desconhecido", f"barra {b.id}: params ignorados {desconhecidos}", ref)
    if not m.casos:
        add("erro", "sem_casos", "o modelo não tem casos de carga")
    com_carga = {c.caso for t in _LISTAS for c in _colecao(m, t)}
    sep = cfg["tipo_edificacao"] == "separadas"
    for c in m.casos.values():
        auto = (_RE_AUTO_V.fullmatch(c.nome) and cfg.get("vento")) or (_RE_AUTO_S.fullmatch(c.nome) and cfg.get("sismo"))
        rec = c.tipo == "G" and c.subtipo == _comb.SUBTIPO_RECALQUE
        if c.nome not in com_carga and c.nome != m.peso_proprio and not auto and not rec:
            add("aviso", "caso_sem_carga", f"caso {c.nome} não tem cargas", {"tipo": "caso", "id": c.nome})
        ref = {"tipo": "caso", "id": c.nome}
        if c.tipo == "Q" and c.subtipo and c.subtipo not in _comb.USOS_Q:
            add("erro", "caso_invalido", f"caso {c.nome}: subtipo '{c.subtipo}' inválido para Q; use um de "
                f"{', '.join(_comb.USOS_Q)} (vazio = config.tipo_uso)", ref)
        if sep and c.tipo == "G" and c.subtipo and c.subtipo not in _comb.GAMA_G_8800 and c.subtipo != _comb.SUBTIPO_RECALQUE:
            add("erro", "caso_invalido", f"caso {c.nome}: origem da permanente '{c.subtipo}' inválida para ações "
                f"separadas (NBR 8800:2008 tab. 1); use um de {', '.join(_comb.GAMA_G_8800)}", ref)
    if sep:
        gerais = [c.nome for c in m.casos.values() if c.tipo == "G" and not c.subtipo]  # recalque tem subtipo
        if gerais and any(b.tipo in ("aco", "pff") for b in m.barras.values()):
            add("aviso", "separadas_origem_g", f"ações separadas (NBR 8800:2008 tab. 1): {', '.join(gerais)} sem origem "
                "usa γg = 1,50 (permanentes em geral); defina o subtipo 'metalica' (1,25) no caso do peso próprio da "
                "estrutura de aço")
    if m.peso_proprio is not None and m.peso_proprio not in m.casos:
        add("erro", "referencia_ausente", f"peso_proprio referencia o caso inexistente '{m.peso_proprio}'")
    for t in _LISTAS:
        for c in _colecao(m, t):
            ref = {"tipo": t, "id": c.id}
            if c.caso not in m.casos:
                add("erro", "referencia_ausente", f"carga {c.id} referencia o caso inexistente '{c.caso}'", ref)
            alvo, col = (c.no, m.nos) if t == "carga_nodal" else (c.barra, m.barras)
            if alvo not in col:
                add("erro", "referencia_ausente", f"carga {c.id} referencia '{alvo}' inexistente", ref)
            elif t == "carga_pontual" and m.barras[alvo].ni in m.nos and m.barras[alvo].nj in m.nos \
                    and c.a > _comprimento(m, m.barras[alvo]) + 1e-9:
                add("erro", "carga_fora_da_barra", f"carga {c.id}: a = {c.a:g} m excede a barra {alvo}", ref)
    acima = _pavimentos_acima(m)
    if cfg.get("vento") and cfg["vento"].get("tipo") not in _VENTO_POR_BARRA:
        try:
            galpao = _geom_galpao(m, cfg["vento"]) is not None
        except ErroEntrada as e:
            galpao = True
            add("erro", "vento_galpao", str(e))
        if not acima and not galpao:
            add("erro", "vento_sem_pavimentos", "config.vento exige pavimentos com z > 0 (ou um galpão do gerador)")
    if (cfg.get("sismo") or {}).get("zona", 0) and not acima:
        add("erro", "sismo_sem_pavimentos", "config.sismo (zona > 0) exige pavimentos com z > 0")
    for p in m.pavimentos.values():
        if not any(abs(n.z - p.z) <= _sol.TOL_PAV for n in m.nos.values()):
            add("aviso", "pavimento_sem_nos", f"nenhum nó na cota do pavimento {p.nome} (z = {p.z:g} m)",
                {"tipo": "pavimento", "id": p.nome})
    return out


def _exigir(modelo, codigos=None):
    """Levanta ErroEntrada se `validar` tem erro (restrito a `codigos`, se dado)."""
    erros = [v for v in validar(modelo) if v["nivel"] == "erro" and (codigos is None or v["codigo"] in codigos)]
    if erros:
        raise ErroEntrada([{"campo": (f"{v['ref']['tipo']}.{v['ref']['id']}" if v["ref"] else v["codigo"]),
                            "mensagem": v["mensagem"]} for v in erros])


def geometria(modelo):
    """#13 Geometria para a vista 3D (eixos locais vêm de `solver.eixos_locais`)."""
    m = modelo
    barras = []
    for b in m.barras.values():
        if not all(n in m.nos for n in (b.ni, b.nj)):
            continue
        if _comprimento(m, b) > 1e-9:
            ex, ey, ez = _sol.eixos_locais(m, b.id)
        else:  # barra de comprimento nulo: a validação já avisa
            ex = ey = ez = [0.0, 0.0, 0.0]
        props = m.secoes[b.secao].props if b.secao in m.secoes else {}
        barras.append({"id": b.id, "ni": b.ni, "nj": b.nj, "tipo": b.tipo, "secao": b.secao,
                       "material": b.material, "L": _comprimento(m, b), "rot": b.rot,
                       "eixos": {"x": list(ex), "y": list(ey), "z": list(ez)},
                       "forma": props.get("tipo"),
                       "dim": {k: props[k] for k in ("h", "b", "tw", "tf", "t", "D") if k in props},
                       "rotula_i": bool(b.params.get("rotula_i")), "rotula_j": bool(b.params.get("rotula_j"))})
    cargas = {}
    for c in m.cargas_nodais:
        cargas.setdefault(c.caso, []).append({"tipo": "carga_nodal", "id": c.id, "alvo": c.no, "valores": list(c.F)})
    for c in m.cargas_distribuidas:
        cargas.setdefault(c.caso, []).append({"tipo": "carga_distribuida", "id": c.id, "alvo": c.barra,
                                              "valores": list(c.q),
                                              **({"valores2": list(c.q2)} if c.q2 is not None else {})})
    for c in m.cargas_pontuais:
        cargas.setdefault(c.caso, []).append({"tipo": "carga_pontual", "id": c.id, "alvo": c.barra,
                                              "valores": [c.a, *c.F]})
    return _puro({
        "nome": m.nome,
        "nos": [{"id": n.id, "x": n.x, "y": n.y, "z": n.z, "apoio": list(n.apoio)} for n in m.nos.values()],
        "barras": barras, "cargas": cargas,
        "pavimentos": [{"nome": p.nome, "z": p.z, "diafragma": p.diafragma} for p in m.pavimentos.values()],
        "caixa": _caixa(m)})


# ================================================================ 14: gerador

def gerar_estrutura(modelo, dados):
    """#14 Delega a `gerador.gerar_estrutura` (ErroGerador → ErroEntrada). → {nos_criados, barras_criadas,
    pavimentos_criados, ids, avisos}."""
    try:
        from . import gerador
    except ImportError as e:  # pragma: no cover
        raise NotImplementedError("gerador.py indisponível") from e
    try:
        return gerador.gerar_estrutura(modelo, dados)
    except gerador.ErroGerador as e:
        raise ErroEntrada(e.erros) from e


def previa_estrutura(modelo, dados):
    """Gera numa cópia do modelo (o modelo não muda) e devolve só o que o gerador cria, para desenhar a prévia:
    {nos: {id: [x, y, z]} (pontas das barras novas), barras: [{id, ni, nj, trelica}], removidas: [ids divididos],
    nos_criados, barras_criadas, avisos}."""
    m = copy.deepcopy(modelo)
    r = gerar_estrutura(m, dados)
    barras = [m.barras[b] for b in r["ids"]["barras"]]
    nos = {n for b in barras for n in (b.ni, b.nj)} | set(r["ids"]["nos"])
    return {"nos": {n: [m.nos[n].x, m.nos[n].y, m.nos[n].z] for n in nos},
            "barras": [{"id": b.id, "ni": b.ni, "nj": b.nj, "trelica": bool(b.params.get("trelica"))} for b in barras],
            "removidas": r["ids"].get("barras_removidas", []), "nos_criados": r["nos_criados"],
            "barras_criadas": r["barras_criadas"], "avisos": r["avisos"]}


# ================================================================ 15: ações automáticas

def _nos_no_nivel(m, z):
    return [n for n in m.nos.values() if abs(n.z - z) <= _sol.TOL_PAV]


def _mapa_pesos(m, pavs):
    """{nó: índice do pavimento} para somar pesos. Nó na cota do pavimento → ele; nó sem apoio entre
    pisos ou acima do topo → pavimento imediatamente acima (ou o topo); nó apoiado ou abaixo do primeiro
    pavimento → fora (o peso vai direto ao solo)."""
    mapa = {}
    for n in m.nos.values():
        idx = next((i for i, (_, z) in enumerate(pavs) if abs(n.z - z) <= _sol.TOL_PAV), None)
        if idx is None and pavs and not any(n.apoio) and n.z > pavs[0][1]:
            idx = next((i for i, (_, z) in enumerate(pavs) if z >= n.z), len(pavs) - 1)
        if idx is not None:
            mapa[n.id] = idx
    return mapa


def _pesos_por_pavimento(m, mapa, npav, fatores):
    """Peso vertical (kN, para baixo) de cada pavimento para os casos ponderados `fatores`."""
    p = np.zeros(npav)
    for no, w in _sol.pesos_nodais(m, fatores).items():
        if no in mapa:
            p[mapa[no]] += w
    return p


def _fracao_q(m, c, cfg):
    """Fração da sobrecarga no peso sísmico: uso do caso (subtipo) ou `config.tipo_uso`, validado por
    `combinacoes.psi` (implementação única; ValueError para subtipo desconhecido). Temperatura não tem massa."""
    if c.subtipo == "temperatura":
        return 0.0
    _comb.psi(c, cfg["tipo_uso"])
    return _sismo.fracao_Q_sismo(c.subtipo or cfg["tipo_uso"])


def _garantir_recalque(m):
    """Recalques de apoio (No.recalques) exigem um caso G de subtipo "recalque" (NBR 8681 tab. 3): cria "REC" se
    faltar; remove o "REC" automático (sem cargas) se não há mais recalques. Nunca descarta recalques em silêncio."""
    tem = any(any(n.recalques) for n in m.nos.values())
    cr = next((c for c in m.casos.values() if c.tipo == "G" and c.subtipo == _comb.SUBTIPO_RECALQUE), None)
    if tem and cr is None:
        if "REC" in m.casos:
            raise ErroEntrada('caso "REC" já existe e não é G de subtipo "recalque"; renomeie-o ou ajuste o subtipo', "casos")
        m.casos["REC"] = CasoCarga("REC", "G", _comb.SUBTIPO_RECALQUE)
    elif not tem and cr is not None and cr.nome == "REC" and not any(c.caso == "REC" for t in _LISTAS for c in _colecao(m, t)):
        del m.casos["REC"]


def _limpar_auto(m):
    m.cargas_nodais[:] = [c for c in m.cargas_nodais if not c.id.startswith("auto_")]
    m.cargas_distribuidas[:] = [c for c in m.cargas_distribuidas if not c.id.startswith("auto_")]
    com_carga = {c.caso for t in _LISTAS for c in _colecao(m, t)}
    for nome in [n for n in m.casos if _eh_auto(n)]:
        if nome not in com_carga and nome != m.peso_proprio:
            del m.casos[nome]


# barras do galpão do gerador que recebem o vento por pórtico j (vento.casos_vento_galpao)
_BARRAS_GALPAO = {"pilar_esq": "P1-0-{j}", "pilar_dir": "P1-2-{j}", "agua_esq": "VX2-0-{j}", "agua_dir": "VX2-1-{j}"}


def _geometria_do_gerador(m):
    """Geometria de um galpão criado por `gerador` (nós N0-0-j, N0-2-j, N1-0-j, N2-1-j) ou None."""
    if not all(i in m.nos for i in ("N0-0-0", "N0-2-0", "N1-0-0", "N2-1-0", "N0-0-1")):
        return None
    o, n = m.nos["N0-0-0"], 1
    while f"N0-0-{n + 1}" in m.nos:
        n += 1
    return {"vao": m.nos["N0-2-0"].x - o.x, "altura_pilar": m.nos["N1-0-0"].z - o.z,
            "altura_cumeeira": m.nos["N2-1-0"].z - o.z, "n_modulos": n, "espacamento": m.nos["N0-0-1"].y - o.y}


def _geom_galpao(m, vcfg):
    """Geometria do galpão se o vento é de galpão (Cpe/Cpi por pórtico), senão None. Galpão: `vento.tipo` =
    'galpao', ou `vento.geometria` informada, ou modelo do gerador `galpao` sem pavimentos."""
    tipo = vcfg.get("tipo")
    if tipo in ("edificio",) + _VENTO_POR_BARRA:
        return None
    explicita = vcfg.get("geometria")
    g = explicita or _geometria_do_gerador(m)
    if g is None:
        if tipo == "galpao":
            raise ErroEntrada("config.vento.tipo = 'galpao' exige config.vento.geometria {vao, altura_pilar, "
                              "altura_cumeeira, n_modulos, espacamento} ou um galpão criado por estrutura_gerar",
                              "vento.geometria")
        return None
    if tipo is None and not explicita and _pavimentos_acima(m):
        return None
    return dict(g)


def _cargas_no_pavimento(m, caso, f, z):
    """Força horizontal `f` (F_vet_kN) do pavimento dividida igualmente entre os nós livres da cota z."""
    nos = _nos_no_nivel(m, z)
    livres = [n for n in nos if not (n.apoio[0] and n.apoio[1])] or nos
    if not livres:
        raise ErroEntrada(f"pavimento {f['pavimento']} (z = {z:g} m) não tem nós: "
                          "não há onde aplicar a força", "pavimentos")
    fx, fy = f["F_vet_kN"][0] / len(livres), f["F_vet_kN"][1] / len(livres)
    return [CargaNodal(caso, n.id, (fx, fy, 0.0, 0.0, 0.0, 0.0), f"auto_{caso}_{n.id}") for n in livres]


def _binario_no_pavimento(m, caso, pav, z, Mz):
    """Momento torsional Mz (kN·m, anti-horário em torno de +Z) do pavimento como binário de forças horizontais nos
    nós livres da cota z: F = Mz·(−dy, dx)/Σr², com d medido do centroide dos nós (resultante nula, momento Mz).
    Vale com e sem diafragma. Um só nó (ou nós coincidentes): momento nodal."""
    nos = _nos_no_nivel(m, z)
    livres = [n for n in nos if not (n.apoio[0] and n.apoio[1])] or nos
    if not livres:
        raise ErroEntrada(f"pavimento {pav} (z = {z:g} m) não tem nós: não há onde aplicar a torção acidental",
                          "pavimentos")
    cx, cy = (sum(n.x for n in livres) / len(livres), sum(n.y for n in livres) / len(livres))
    r2 = sum((n.x - cx) ** 2 + (n.y - cy) ** 2 for n in livres)
    if r2 < 1e-9:
        return [CargaNodal(caso, livres[0].id, (0.0, 0.0, 0.0, 0.0, 0.0, Mz), f"auto_{caso}_{livres[0].id}")]
    return [CargaNodal(caso, n.id, (-Mz * (n.y - cy) / r2, Mz * (n.x - cx) / r2, 0.0, 0.0, 0.0, 0.0),
                       f"auto_{caso}_{n.id}") for n in livres]


def _vento_por_barra(m, vcfg):
    """Casos de vento com carga distribuída em cada barra: NBR 6123:2023 8.1 (`tipo` 'barras',
    acoes/vento_barras.py) ou NBR 8400:1984 5.5.4.1 (`tipo` 'nbr8400', acoes/nbr8400.py)."""
    barras = []
    for b in m.barras.values():
        ni, nj = m.nos[b.ni], m.nos[b.nj]
        barras.append(dict(id=b.id, xi=(ni.x, ni.y, ni.z), xj=(nj.x, nj.y, nj.z), eixos=_sol.eixos_locais(m, b.id),
                           props=m.secoes[b.secao].props, apoiada=any(ni.apoio) or any(nj.apoio)))
    if vcfg["tipo"] == "nbr8400":
        return _nbr8400.casos_vento_8400(vcfg, barras, _base(m))
    return _vb.casos_vento_barras(vcfg, barras, _base(m), _caixa(m))


def gerar_acoes(modelo):
    """#15 Recria os casos e cargas `auto_` de vento (NBR 6123) e sismo (NBR 15421) a partir de
    `config.vento`, `config.sismo` e dos pavimentos. Idempotente. → {casos, forcas, parametros, avisos}.

    Vento por barra (`config.vento.tipo` = 'barras', NBR 6123:2023 8.1): casos V±X/V±Y com carga distribuída
    w = Cf·q(z)·K·c·sen²α normal a cada barra (acoes/vento_barras.py; forcas[caso] = uma linha por barra).
    Vento NBR 8400:1984 (`tipo` = 'nbr8400', equipamentos de levantamento): VS±X/VS±Y (serviço, subtipo 'vento')
    e VM±X/VM±Y (máximo, subtipo 'vento_maximo'), w = C·Pa·c·sen²α (acoes/nbr8400.py).

    Vento de edifício: casos V±X/V±Y, força de cada pavimento dividida igualmente entre seus nós.
    Vento de galpão (`config.vento.tipo` = 'galpao', `.geometria` ou modelo do gerador sem pavimentos):
    um caso por direção e Cpi ("V+X Cpi=-0.3"), com carga distribuída (Cpe − Cpi)·q em pilares e águas de cada
    pórtico (`vento.casos_vento_galpao`); aceita `permeabilidade` e `cpi`.
    Sismo (zona ≥ 2): SX/SY e a torção acidental SX_TOR/SY_TOR (binário de forças no pavimento, 5% da dimensão da
    planta, `config.sismo.dimensoes` ou caixa do modelo); R/Cd/Ω0 por `config.sismo.sistema_resistente`."""
    m, cfg = modelo, _cfg(modelo)
    pavs = _pavimentos_acima(m)
    base = _base(m)
    prel = [(n, z - base) for n, z in pavs]  # os motores de vento e sismo recebem a altura sobre a base
    avisos, casos, cargas, forcas, params = [], [], [], {}, {}  # casos: (nome, tipo, subtipo)

    def caso_pavimentos(r, tipo, sub):
        casos.append((r["caso"], tipo, sub))
        avisos.extend(r["avisos"])
        lista = []
        for f in r["forcas"]:
            z = f["z"] + base  # volta à cota absoluta dos nós
            cargas.extend(_cargas_no_pavimento(m, r["caso"], f, z))
            lista.append({"pavimento": f["pavimento"], "z": z, "F_kN": f["F_kN"], "F_vet_kN": f["F_vet_kN"],
                          **{k: f[k] for k in ("S2", "Vk_m_s", "q_kN_m2") if k in f}})  # z do cálculo = relativo à base
        forcas[r["caso"]] = lista
        params[r["caso"]] = r["parametros"]

    try:
        vcfg = cfg.get("vento")
        if vcfg and vcfg.get("tipo") in _VENTO_POR_BARRA:
            if vcfg["tipo"] == "nbr8400" and cfg.get("combinacoes") != "nbr8400":
                avisos.append(_aviso("vento_8400_sem_combinacoes", "vento NBR 8400 com combinações NBR 8681: defina "
                                     "config.combinacoes = 'nbr8400' (casos I, II e III da NBR 8400 5.6)"))
            elif vcfg["tipo"] == "barras" and not 20 <= (vcfg.get("V0") or 0) <= 60:
                avisos.append(_aviso("vento_V0_fora_da_faixa", f"V0 = {vcfg.get('V0')} m/s fora da faixa usual das "
                                     "isopletas do Brasil (≈ 25 a 55 m/s, NBR 6123); confira o valor e a unidade (m/s)"))
            for r in _vento_por_barra(m, vcfg):
                casos.append((r["caso"], "V", r.get("subtipo", "vento")))
                avisos.extend(r["avisos"])
                for f in r["barras"]:
                    if max(abs(x) for x in f["w_vet_kN_m"]) > 1e-12:  # barra paralela ao vento: sem carga
                        cargas.append(CargaDistribuida(r["caso"], f["barra"], tuple(f["w_vet_kN_m"]),
                                                       f"auto_{r['caso']}_{f['barra']}"))
                forcas[r["caso"]] = r["barras"]
                params[r["caso"]] = r["parametros"]
        elif vcfg:
            if not 20 <= (vcfg.get("V0") or 0) <= 60:
                avisos.append(_aviso("vento_V0_fora_da_faixa", f"V0 = {vcfg.get('V0')} m/s fora da faixa usual das "
                                     "isopletas do Brasil (≈ 25 a 55 m/s, NBR 6123); confira o valor e a unidade (m/s)"))
            geo = _geom_galpao(m, vcfg)
            if geo is not None:
                for r in _vento.casos_vento_galpao(vcfg, geo):
                    casos.append((r["caso"], "V", "vento"))
                    avisos.extend(r["avisos"])
                    for pt in r["porticos"]:
                        for chave, d in pt["barras"].items():
                            bid = _BARRAS_GALPAO[chave].format(j=pt["portico"])
                            if bid not in m.barras:
                                raise ErroEntrada(f"vento de galpão: barra '{bid}' não existe (o vento por pórtico usa "
                                                  "as barras do gerador `galpao`: P1-0-j, P1-2-j, VX2-0-j, VX2-1-j)",
                                                  "vento.geometria")
                            cargas.append(CargaDistribuida(r["caso"], bid, tuple(d["w_vet_kN_m"]),
                                                           f"auto_{r['caso']}_{bid}"))
                    forcas[r["caso"]] = []
                    params[r["caso"]] = {**r["parametros"], "Cpi": r["Cpi"], "alfa_graus": r["alfa_graus"],
                                         "porticos": r["porticos"]}
            else:
                if not pavs:
                    raise ErroEntrada("config.vento exige pavimentos com z > 0", "vento_sem_pavimentos")
                vcfg = dict(vcfg)
                dim = vcfg.get("dimensoes") or {}
                if not (dim.get("lx") and dim.get("ly")):
                    cx = _caixa(m)
                    dim = {"lx": cx["max"][0] - cx["min"][0], "ly": cx["max"][1] - cx["min"][1]}
                    if dim["lx"] <= 0 or dim["ly"] <= 0:
                        raise ErroEntrada("config.vento.dimensoes: informe lx e ly (a caixa do modelo é plana)",
                                          "vento.dimensoes")
                    avisos.append(_aviso("vento_dimensoes_da_caixa", f"dimensões do vento tiradas da caixa do modelo "
                                         f"(lx = {dim['lx']:g} m, ly = {dim['ly']:g} m)"))
                for r in _vento.casos_vento(vcfg, prel, dim):
                    caso_pavimentos(r, "V", "vento")
        sc = cfg.get("sismo")
        if sc is not None:
            zona = int(sc.get("zona", 0))
            if zona == 0:
                avisos.append(_aviso("sismo_dispensado", "zona sísmica 0: NBR 15421 não exige verificação "
                                     "(nenhum caso de sismo gerado)"))
            else:
                if not pavs:
                    raise ErroEntrada("config.sismo exige pavimentos com z > 0", "sismo_sem_pavimentos")
                fat = {c.nome: 1.0 for c in m.casos.values() if c.tipo == "G"}
                fat.update({c.nome: _fracao_q(m, c, cfg) for c in m.casos.values() if c.tipo == "Q"})
                if m.peso_proprio in m.casos:
                    fat.setdefault(m.peso_proprio, 1.0)
                mapa = _mapa_pesos(m, pavs)
                pesos = _pesos_por_pavimento(m, mapa, len(pavs), fat)
                perdido = sum(w for no, w in _sol.pesos_nodais(m, fat).items()
                              if no not in mapa and m.nos[no].z > base + _sol.TOL_PAV and not any(m.nos[no].apoio))
                if perdido > 1e-6:
                    avisos.append(_aviso("peso_fora_dos_pavimentos", f"{perdido:.1f} kN de peso em nós abaixo do "
                                         "primeiro pavimento ficaram fora do peso sísmico"))
                sismos = _sismo.casos_sismo(sc, prel, pesos.tolist(), None)
                for r in sismos:
                    caso_pavimentos(r, "S", "")
                if zona >= 2:  # zona 1 usa o procedimento simplificado (1% do peso), sem torção acidental
                    dim = sc.get("dimensoes") or (cfg.get("vento") or {}).get("dimensoes")
                    if not (dim and dim.get("lx") and dim.get("ly")):
                        cx = _caixa(m)
                        dim = {"lx": cx["max"][0] - cx["min"][0], "ly": cx["max"][1] - cx["min"][1]}
                    if dim["lx"] <= 0 or dim["ly"] <= 0:
                        avisos.append(_aviso("sismo_torcao_dispensada", "modelo plano: torção acidental do sismo "
                                             "(NBR 15421:2006) não aplicável"))
                    else:
                        for t in _sismo.torcao_acidental(sismos, {"lx": dim["lx"], "ly": dim["ly"]}):
                            casos.append((t["caso"], "S", ""))
                            lista = []
                            for mo in t["momentos"]:
                                z = mo["z"] + base
                                cargas.extend(_binario_no_pavimento(m, t["caso"], mo["pavimento"], z, mo["Mz_kNm"]))
                                lista.append({"pavimento": mo["pavimento"], "z": z, "F_kN": 0.0,
                                              "F_vet_kN": [0.0, 0.0, 0.0], "Mz_kNm": mo["Mz_kNm"]})
                            forcas[t["caso"]] = lista
                            params[t["caso"]] = t["parametros"]
    except ValueError as e:
        if isinstance(e, ErroEntrada):
            raise
        raise ErroEntrada(str(e), "config") from e
    _limpar_auto(m)  # tudo ou nada: o modelo só muda depois de montadas todas as cargas
    for nome, tipo, sub in casos:
        if nome not in m.casos:
            m.add(CasoCarga(nome, tipo, sub))
    for c in cargas:
        m.add(c)
    unicos = list({a["mensagem"]: a for a in avisos}.values())
    return _puro({"casos": [c[0] for c in casos], "forcas": forcas, "parametros": params, "avisos": unicos})


# ================================================================ 16-19: combinações e análises

def _detalhe_fatores(casos, cfg, estado, fatores):
    """{caso: {gamma, psi, produto}}; fatores = γ·ψ do motor. Q/V: ELU principal ψ=1, secundária ψ0; ELU_EXC ψ2
    (γ=1); ELS γ=1 e ψ = fator. G, S, E, _TOR: ψ=1."""
    out = {}
    for k, f in fatores.items():
        c = casos.get(k)
        g, p = f, 1.0
        if c is not None and c.tipo in ("Q", "V") and estado != "ELU_EXC":
            if estado == "ELU":
                gq = _comb._gq(c, "normal", cfg["tipo_edificacao"])
                g, p = gq, f / gq
            else:
                g, p = 1.0, f
        elif c is not None and c.tipo in ("Q", "V"):
            g, p = 1.0, f
        out[k] = {"gamma": g, "psi": p, "produto": f}
    return out


def combinacoes(modelo):
    """#16 Combinações NBR 8681: [{nome, estado, fatores}] (estado ∈ ELU, ELU_EXC, ELS_QP, ELS_FREQ, ELS_RARA).
    `config.combinacoes` = 'nbr8400': casos I/II/III da NBR 8400:1984 5.6 (ELU já com ν/γa1) e ELS_RARA G + Q
    (+ vento de serviço); cada item traz `nbr8400` {caso_8400, Mx, psi, nu, conversao}."""
    cfg = _cfg(modelo)
    _garantir_recalque(modelo)
    if cfg.get("combinacoes") == "nbr8400":  # NBR 8400:1984 5.6, casos I/II/III (acoes/nbr8400.py)
        p = cfg.get("nbr8400") or {}
        try:
            lista = _nbr8400.gerar_combinacoes(modelo.casos, p.get("grupo", 3), p.get("psi", _nbr8400.PSI_PADRAO))
        except ValueError as e:
            raise ErroEntrada(str(e), "nbr8400") from e
        return [{"nome": n, "estado": e, "fatores": dict(f),
                 "detalhe": {k: {"gamma": v, "psi": 1.0, "produto": v} for k, v in f.items()}, "nbr8400": d}
                for n, f, e, d in lista]
    try:
        lista = _comb.gerar_combinacoes(modelo.casos, cfg["tipo_uso"], cfg["variabilidade"], cfg["tipo_edificacao"])
    except ValueError as e:  # subtipo de Q/origem de G inválidos, mais de 12 casos Q: erro de entrada, não bug
        raise ErroEntrada(str(e), "casos") from e
    return [{"nome": n, "estado": e, "fatores": dict(f), "detalhe": _detalhe_fatores(modelo.casos, cfg, e, f)}
            for n, f, e in lista if f]


def _rel(a):
    """Deslocamentos locais relativos à corda entre as extremidades: (rel_uy, rel_uz) de `a` (n × 4:
    x, ux, uy, uz)."""
    x = a[:, 0]
    t = (x - x[0]) / (x[-1] - x[0]) if x[-1] > x[0] else np.zeros_like(x)
    return tuple(a[:, k] - (a[0, k] + (a[-1, k] - a[0, k]) * t) for k in (2, 3))


_COMP = {"esforcos": ("N", "Vy", "Vz", "T", "My", "Mz"), "flechas": ("ux", "uy", "uz", "rel_uy", "rel_uz")}


def _arrays(r, alvo):
    """Resultado do solver → {id: array}; esforços/flechas: (x, matriz n × componentes)."""
    if alvo in ("deslocamentos", "reacoes", "pavimentos"):
        return {k: np.asarray(v, float) for k, v in getattr(r, alvo).items()}
    out = {}
    for b, lst in getattr(r, alvo).items():
        a = np.asarray(lst, float)
        if alvo == "esforcos":
            out[b] = (a[:, 0], a[:, 1:7])
        else:
            ry, rz = _rel(a)
            out[b] = (a[:, 0], np.column_stack([a[:, 1], a[:, 2], a[:, 3], ry, rz]))
    return out


def _resultado_dict(nome, r, pdelta):
    """Resultado do solver → ResultadoDict (ESPEC §6.4), JSON puro."""
    esf = {b: {"x": x, **{c: mat[:, k] for k, c in enumerate(_COMP["esforcos"])}}
           for b, (x, mat) in _arrays(r, "esforcos").items()}
    fle = {b: {"x": x, **{c: mat[:, k] for k, c in enumerate(_COMP["flechas"])}}
           for b, (x, mat) in _arrays(r, "flechas").items()}
    return _puro({"nome": nome, "pdelta": bool(pdelta), "deslocamentos": r.deslocamentos, "reacoes": r.reacoes,
                  "esforcos": esf, "flechas": fle, "pavimentos": r.pavimentos})


def analisar_caso(modelo, caso, pdelta=False, n_pontos=N_PONTOS):
    """#17 Análise de um caso → ResultadoDict."""
    _exigir(modelo, _ESTRUTURAIS)
    _garantir_recalque(modelo)
    if caso not in modelo.casos:
        raise ErroNaoEncontrado("caso", caso, modelo.casos)
    return _resultado_dict(caso, _sol.analisar(modelo, caso, bool(pdelta), n_pontos), pdelta)


def _resultado_comb(modelo, fatores, estado, pdelta, res_caso, n_pontos):
    """Combinação linear dos casos já analisados; com P-Δ, ELU/ELU_EXC são analisadas como caso único."""
    if pdelta and estado in ("ELU", "ELU_EXC"):
        return _sol.analisar(modelo, fatores, True, n_pontos)
    for c in fatores:
        if c not in res_caso:
            res_caso[c] = _sol.analisar(modelo, c, False, n_pontos)
    return _sol.combinar(res_caso, fatores)


def analisar_combinacao(modelo, nome, pdelta=None, n_pontos=N_PONTOS):
    """#18 Análise de uma combinação pelo nome → ResultadoDict. `pdelta=None` usa `config.pdelta`; com P-Δ
    a combinação é analisada como caso único (combinar P-Δ linearmente seria errado). Análise pura da combinação
    nominal: não aplica a majoração 0,95·γz / B2 das ações horizontais que `calcular` faz sem P-Δ."""
    _exigir(modelo, _ESTRUTURAIS)
    comb = {c["nome"]: c for c in combinacoes(modelo)}
    if nome not in comb:
        raise ErroNaoEncontrado("combinação", nome, comb)
    pd = _cfg(modelo)["pdelta"] if pdelta is None else bool(pdelta)
    c = comb[nome]
    if pd:
        r = _sol.analisar(modelo, c["fatores"], True, n_pontos)
    else:
        r = _resultado_comb(modelo, c["fatores"], c["estado"], False, {}, n_pontos)
    return _resultado_dict(nome, r, pd)


def modal(modelo, n_modos=6):
    """#19 Frequências e modos com massa G + ψ2·Q (ψ2 da NBR 8681). → {frequencias_hz, periodos_s,
    participacao, modos}."""
    _exigir(modelo, _ESTRUTURAIS)
    cfg = _cfg(modelo)
    if isinstance(n_modos, bool) or not isinstance(n_modos, int) or n_modos < 1:
        raise ErroEntrada("n_modos: inteiro ≥ 1", "n_modos")
    massa = {}
    for c in modelo.casos.values():
        if c.tipo == "G":
            massa[c.nome] = 1.0
        elif c.tipo == "Q":
            try:
                massa[c.nome] = _comb.psi(c, cfg["tipo_uso"])[2]
            except ValueError as e:
                raise ErroEntrada(str(e), "casos") from e
    if modelo.peso_proprio in modelo.casos:
        massa.setdefault(modelo.peso_proprio, 1.0)
    massa = {k: v for k, v in massa.items() if v}
    if not massa:
        raise ErroEntrada("sem massa: defina casos G (ou peso próprio) com cargas", "casos")
    try:
        f, modos, part = _sol.modal(modelo, n_modos, massa)
    except ValueError as e:
        raise ErroEntrada(str(e), "massa") from e
    return _puro({"frequencias_hz": f, "periodos_s": [1 / x if x > 1e-12 else None for x in f],
                  "participacao": part, "modos": [{n: v for n, v in md.items()} for md in modos]})


# ================================================================ 20: cálculo completo

_NORMA_DE = {"aco": _nbr8800, "pff": _nbr14762, "concreto": _nbr6118}
_PARAMS_CONCRETO = ("fyk_MPa", "fywk_MPa", "cobrimento_m", "caa", "d_m", "le_x", "le_y", "alfa_b",
                    "elemento_desloc", "t0_meses", "emenda", "l0_x", "l0_y", "t_alvenaria_meses",
                    "fracao_qp_pos_alvenaria")


@dataclass
class Calculo:
    """Resultado de `calcular` (ESPEC §6.5). `resultados` guarda o `solver.Resultado` de cada combinação;
    `to_dict` converte tudo em JSON puro."""
    modelo_nome: str = ""
    versao: str = __version__
    data: str = ""
    opcoes: dict = field(default_factory=dict)
    combinacoes: list = field(default_factory=list)
    resultados: dict = field(default_factory=dict)
    verificacao: list = field(default_factory=list)
    deslocamentos: dict = field(default_factory=lambda: {"verticais": [], "horizontais": [], "sismicos": []})
    estabilidade: dict = field(default_factory=dict)
    reacoes_envoltoria: dict = field(default_factory=dict)
    avisos: list = field(default_factory=list)
    tempo_s: float = 0.0
    normas: list = field(default_factory=list)
    por_combinacao: dict = field(default_factory=dict)  # barra -> [{combinacao, taxa, criterio}] (aço/PFF)
    modelo: Modelo | None = field(default=None, repr=False)  # cópia do modelo calculado

    def resumo(self):
        v = self.verificacao
        num = [x for x in v if x["taxa"] is not None]
        crit = max(num, key=lambda x: x["taxa"]) if num else None
        d = self.deslocamentos
        return {"n_barras": len(v), "n_verificadas": len(num), "n_reprovadas": sum(1 for x in v if not x["ok"]),
                "n_nao_verificadas": len(v) - len(num), "taxa_max": crit["taxa"] if crit else None,
                "barra_critica": crit["barra"] if crit else None,
                "n_desloc_reprovados": sum(1 for x in d["verticais"] + d["horizontais"] + d.get("sismicos", []) if not x["ok"]),
                "tempo_s": round(self.tempo_s, 3)}

    def to_dict(self, completo=False):
        d = {"versao": self.versao, "data": self.data, "modelo": self.modelo_nome, "opcoes": self.opcoes,
             "normas": self.normas, "combinacoes": self.combinacoes, "resumo": self.resumo(),
             "verificacao": self.verificacao, "deslocamentos": self.deslocamentos,
             "estabilidade": self.estabilidade, "reacoes_envoltoria": self.reacoes_envoltoria,
             "avisos": self.avisos}
        if completo:
            d["resultados"] = {n: _resultado_dict(n, r, self.opcoes.get("pdelta") and
                                                  next((c["estado"] in ("ELU", "ELU_EXC") for c in self.combinacoes
                                                        if c["nome"] == n), False))
                               for n, r in self.resultados.items()}
        return _puro(d)


def _envoltoria(nomes, stack):
    """stack: (C, ...) com um valor por combinação → {max, min, comb_max, comb_min} (arrays)."""
    nomes = np.array(nomes, dtype=object)
    imax, imin = stack.argmax(0), stack.argmin(0)
    return {"max": stack.max(0), "min": stack.min(0), "comb_max": nomes[imax], "comb_min": nomes[imin]}


def _env_alvo(resultados, nomes, alvo, ids=None):
    """Envoltória de `alvo` nas combinações `nomes` → {id: ...} (ver `resultados`)."""
    arr = {n: _arrays(resultados[n], alvo) for n in nomes}
    chaves = list(arr[nomes[0]]) if nomes else []
    saida = {}
    for k in chaves:
        if ids is not None and k not in ids:
            continue
        if alvo in _COMP:
            x = arr[nomes[0]][k][0]
            env = _envoltoria(nomes, np.stack([arr[n][k][1] for n in nomes]))
            saida[k] = {"x": x, **{c: {t: v[..., i] for t, v in env.items()} for i, c in enumerate(_COMP[alvo])}}
        else:
            saida[k] = _envoltoria(nomes, np.stack([arr[n][k] for n in nomes]))
    return _puro(saida)


# ---------------------------------------------------------------- verificação por barra

def _livre(m, b, nid):
    """Extremidade livre: sem outra barra e sem apoio próprio. Modelo plano XZ (uy, rx e rz presos em TODOS os
    nós, como no gerador) não conta: essas três restrições comuns não são apoio. Só vale a trinca completa;
    senão (ex.: viga com 2 nós) todo apoio conta."""
    plano = all(all(n.apoio[g] for n in m.nos.values()) for g in (1, 3, 5))
    ignora = (1, 3, 5) if plano else ()
    return (not any(a for g, a in enumerate(m.nos[nid].apoio) if g not in ignora)
            and not any(o is not b and nid in (o.ni, o.nj) for o in m.barras.values()))


def _balanco(m, b):
    """True se a barra é um balanço: exatamente uma extremidade livre (ver `_livre`)."""
    return sum(_livre(m, b, nid) for nid in (b.ni, b.nj)) == 1


def _aco_comb(m, b, L, res, tipo, balanco=False):
    """Verifica uma barra de aço/PFF numa combinação (uma chamada ao motor; todos os pontos)."""
    linhas = res.esforcos[b.id]
    esf = [_nbr8800.esforcos_nbr(l) for l in linhas]
    p = b.params
    Lx, Ly, Lz = (float(p.get(k, 1.0)) * L for k in ("kx", "ky", "kz"))
    # trecho destravado padrão = trecho flexível [trecho_rigido_i, L − trecho_rigido_j]: os esforços do solver
    # começam na face do trecho rígido (xs[0] e xs[-1]); sem trecho rígido é a barra toda
    xa, xb = float(linhas[0][0]), float(linhas[-1][0])
    Lb = float(p.get("Lb", xb - xa))
    if balanco:
        Cb = _nbr8800.cb_de_diagrama([l[0] for l in linhas], [e["Mx"] for e in esf], balanco=True)  # 5.4.2.3: 1,0; prevalece sobre params.Cb
    elif p.get("Cb"):
        Cb = float(p["Cb"])
    elif Lb < (xb - xa) * (1 - 1e-9):
        # ponytail: trecho destravado de posição desconhecida → Cb = 1 (a favor da segurança);
        # informe params.Cb para aproveitar o diagrama
        Cb = 1.0
    else:
        Cb = _nbr8800.cb_de_diagrama([l[0] for l in linhas], [e["Mx"] for e in esf], xa, xb)
    sec, mat = m.secoes[b.secao], m.materiais[b.material]
    An, Ct = p.get("An"), float(p.get("Ct", 1.0))
    if tipo == "pff":
        if _nbr14762 is None:
            raise ValueError("NBR 14762 indisponível")
        return _nbr14762.verificar_pff(sec, mat, esf, Lx, Ly, Lz, Lb, Cb, An, Ct, p.get("Ndist"), p.get("Mdist"))
    return _nbr8800.verificar_barra_aco(sec, mat, esf, Lx, Ly, Lb, Cb, Lz, An, Ct, p.get("ligacao_uma_aba"),
                                        L=L, Lv=p.get("Lv"), tubo_com_costura=bool(p.get("tubo_com_costura", False)),
                                        ponto_aplicacao=p.get("ponto_aplicacao") or "centro", C2=p.get("C2"), Lt=p.get("Lt"))


def _aco_barra(m, b, L, resultados, elu, so=None):
    """Pior combinação de uma barra de aço/PFF. → (resultado, combinação, [{combinacao, taxa, criterio}])."""
    pior, esb, por, balanco = None, None, [], _balanco(m, b)
    for nome in ([so] if so else elu):
        r = _aco_comb(m, b, L, resultados[nome], b.tipo, balanco)
        por.append({"combinacao": nome, "taxa": r["taxa_max"], "criterio": r["criterio"]})
        if pior is None or r["taxa_max"] > pior[0]["taxa_max"]:
            pior = (r, nome)
        t = ((r.get("detalhes") or {}).get("taxas") or {}).get("esbeltez", 0.0)
        if esb is None or t > esb[0]:
            esb = (t, r, nome)
    # a esbeltez independe do esforço: a combinação governante por esforço pode esconder outra que reprova
    # por KL/r > 200 (5.3.4) ou L/r > 300 (5.2.8). Se a pior esbeltez reprova e a linha não a mostraria, usa-a.
    if esb[0] > 1 and esb[2] != pior[1] and pior[0]["ok"]:
        return esb[1], esb[2], por
    return pior[0], pior[1], por


def _args_concreto(m, b, resultados, elu, qp, freq):
    """Argumentos `elu`, `els` e `params` de `verificar_barra_concreto` (ESPEC §7.2)."""
    cfg = _cfg(m)
    elu_l = [{"combinacao": n, "linhas": resultados[n].esforcos[b.id]} for n in elu]
    els = {"qp": None, "freq": None}
    if qp:
        ry, rz = _rel(np.asarray(resultados[qp].flechas[b.id], float))
        els["qp"] = {"combinacao": qp, "linhas": resultados[qp].esforcos[b.id],
                     "flecha_elastica_m": float(max(np.abs(ry).max(), np.abs(rz).max()))}
    if freq:
        mom = {n: max(abs(l[5]) for l in resultados[n].esforcos[b.id]) for n in freq}
        n = max(mom, key=mom.get)
        els["freq"] = {"combinacao": n, "linhas": resultados[n].esforcos[b.id]}
    params = {"fyk_MPa": float(_mat.ARMADURAS[cfg["armadura"]][0]), "caa": cfg["caa"]}
    if cfg.get("cobrimento_m"):
        params["cobrimento_m"] = float(cfg["cobrimento_m"])
    params.update({k: v for k, v in b.params.items() if k in _PARAMS_CONCRETO})
    return elu_l, els, params


def _concreto_barra(m, b, L, resultados, elu, qp, freq):
    elu_l, els, params = _args_concreto(m, b, resultados, elu, qp, freq)
    return _nbr6118.verificar_barra_concreto(m.secoes[b.secao], m.materiais[b.material], elu_l, els, L,
                                             _papel(m, b), params)


def _linha_base(m, b, L):
    return {"barra": b.id, "tipo": b.tipo, "secao": b.secao, "material": b.material, "L_m": L}


def _linha_nao_verificada(m, b, L, motivo):
    mod = _NORMA_DE.get(b.tipo)
    return {**_linha_base(m, b, L), "norma": _norma_curta(mod) if mod else "", "taxa": None,
            "criterio": "nao_verificada", "item": "", "ok": False, "combinacao": None, "x_m": None,
            "armadura": None, "motivo": motivo, "avisos": [motivo]}


def _linha_de(m, b, L, r, comb, resultados):
    x = 0.0
    if comb and comb in resultados and r["indice"] < len(resultados[comb].esforcos[b.id]):
        x = float(resultados[comb].esforcos[b.id][r["indice"]][0])
    linha = {**_linha_base(m, b, L), "norma": _norma_curta(_NORMA_DE[b.tipo]), "taxa": float(r["taxa_max"]),
             "criterio": r["criterio"], "item": r["item"], "ok": bool(r["ok"]), "combinacao": comb, "x_m": x,
             "armadura": r.get("armadura"), "avisos": list(r.get("avisos", []))}
    # taxa_max do motor é de esforço; a esbeltez (independe do esforço) só reprova. Barra reprovada por ela
    # mostra a taxa e o item da esbeltez (taxa > 1 = reprovada); combinação e x seguem os do esforço determinante.
    det = r.get("detalhes") or {}
    t_esb = (det.get("taxas") or {}).get("esbeltez")
    if t_esb and t_esb > 1 and t_esb > linha["taxa"]:
        esb = det.get("esbeltez")
        item = (esb.get("item") if isinstance(esb, dict) else None) or getattr(_NORMA_DE[b.tipo], "_ITENS", {}).get("esbeltez")
        linha.update(taxa_esforco=linha["taxa"], taxa=float(t_esb), criterio="esbeltez", item=item or linha["item"])
    return linha


def _verificar_uma(m, b, resultados, elu, qp, freq):
    """→ (linha, por_combinacao, ResultadoNorma | None). Erros de dados viram linha `nao_verificada`."""
    L = _comprimento(m, b)
    if b.tipo == "pff" and _nbr14762 is None:
        return _linha_nao_verificada(m, b, L, "NBR 14762 indisponível"), [], None
    try:
        if b.tipo == "concreto":
            r = _concreto_barra(m, b, L, resultados, elu, qp, freq)
            return _linha_de(m, b, L, r, r["combinacao"], resultados), [], r
        r, comb, por = _aco_barra(m, b, L, resultados, elu)
        return _linha_de(m, b, L, r, comb, resultados), por, r
    except (ValueError, KeyError, TypeError) as e:
        motivo = f"{type(e).__name__}: {e}" if isinstance(e, (KeyError, TypeError)) else str(e)
        (log.exception if isinstance(e, TypeError) else log.info)("barra %s não verificada: %s", b.id, motivo)
        return _linha_nao_verificada(m, b, L, motivo), [], None


# ---------------------------------------------------------------- deslocamentos

def _flecha_max(cache, resultados, comb, bid, ponta=None):
    """Flecha máxima da barra relativa à corda; em balanço (`ponta` = 0 ou -1, extremidade livre), deslocamento
    transversal da ponta em relação ao apoio (NBR 8800 C.1 nota a: L = 2 × balanço)."""
    k = (comb, bid)
    if k not in cache:
        a = np.asarray(resultados[comb].flechas[bid], float)
        if ponta is None:
            ry, rz = _rel(a)
            cache[k] = float(max(np.abs(ry).max(), np.abs(rz).max()))
        else:
            cache[k] = float(np.abs(a[ponta, 2:4] - a[-1 if ponta == 0 else 0, 2:4]).max())
    return cache[k]


def _ponta_livre(m, b):
    """Índice (0 ou -1) da extremidade livre de um balanço; None se a barra não é balanço."""
    if not _balanco(m, b):
        return None
    return 0 if _livre(m, b, b.ni) else -1


def _desloc_verticais(m, barras, resultados, por_estado, linhas, avisos):
    out, cache = [], {}
    por_barra = {l["barra"]: l for l in linhas}
    for b in barras:
        L = _comprimento(m, b)
        if b.tipo == "concreto":
            l = por_barra.get(b.id)
            if not l or not l.get("armadura") or _papel(m, b) != "viga":
                continue
            r = l["_motor"]
            fl = next((v for v in r["detalhes"]["verificacoes"] if v["criterio"] == "flecha"), None)
            if fl:
                out.append({"barra": b.id, "elemento": b.params.get("elemento_desloc") or "aceitabilidade",
                            "flecha_m": fl["solicitante"], "limite_m": fl["resistente"], "razao": fl["razao"],
                            "taxa": fl["taxa"], "combinacao": fl["combinacao"], "item": fl["item"],
                            "ok": fl["taxa"] <= 1})
            continue
        if _papel(m, b) != "viga" or b.params.get("trelica"):
            continue
        ponta = _ponta_livre(m, b)
        elem = b.params.get("elemento_desloc") or ("balanco" if ponta is not None else "viga_piso")
        try:
            lim = _nbr8800.limite_deslocamento(elem, L)
        except ValueError as e:
            avisos.append(_aviso("elemento_desloc", f"barra {b.id}: {e}; usado 'viga_piso'", {"tipo": "barra", "id": b.id}))
            elem, lim = "viga_piso", _nbr8800.limite_deslocamento("viga_piso", L)
        # sem ações variáveis não há combinação frequente/rara: coincidem com a quase permanente
        nomes = por_estado.get(lim["estado"]) or por_estado.get("ELS_QP") or por_estado.get("ELS_RARA", [])  # NBR 8400: só RARA
        if not nomes:
            continue
        vals = {n: _flecha_max(cache, resultados, n, b.id, ponta) for n in nomes}
        n = max(vals, key=vals.get)
        out.append({"barra": b.id, "elemento": elem, "flecha_m": vals[n], "limite_m": lim["limite_m"],
                    "razao": lim["razao"], "taxa": vals[n] / lim["limite_m"], "combinacao": n,
                    "item": lim["item"], "ok": vals[n] <= lim["limite_m"]})
    return out


def _forcas_horizontais(m, pavs):
    """Forças horizontais por caso e pavimento → ({caso: array (npav, 2)}, {caso: [nós ignorados]}).
    Vêm de cargas nodais, distribuídas e pontuais: cada carga de barra vira forças nas duas pontas
    (q·L/2 em cada nó; pontual F(1−a/L) em ni e F·a/L em nj), o que preserva a força e o momento em
    relação à base (a resultante de carga uniforme está a meia altura). Só nós na cota de um pavimento
    entram; nó na base vai ao solo; nó em outra cota fica fora (devolvido para o aviso). VC-integracao-007."""
    base = _base(m)
    por_no = {}  # (caso, nó) -> força xy

    def somar(caso, no, v):
        por_no[(caso, no)] = por_no.get((caso, no), 0.0) + np.asarray(v[:2], float)
    for c in m.cargas_nodais:
        somar(c.caso, c.no, c.F)
    for c in m.cargas_distribuidas:
        b = m.barras[c.barra]
        qi = np.asarray(c.q, float)
        qj = qi if c.q2 is None else np.asarray(c.q2, float)
        L = _comprimento(m, b)
        somar(c.caso, b.ni, (2 * qi + qj) * L / 6)  # trapézio: reações da biapoiada
        somar(c.caso, b.nj, (qi + 2 * qj) * L / 6)
    for c in m.cargas_pontuais:
        b = m.barras[c.barra]
        t = c.a / _comprimento(m, b)
        somar(c.caso, b.ni, np.asarray(c.F, float) * (1 - t))
        somar(c.caso, b.nj, np.asarray(c.F, float) * t)
    H = {c: np.zeros((len(pavs), 2)) for c in m.casos}
    fora = {}
    for (caso, no), v in por_no.items():
        z = m.nos[no].z
        i = next((i for i, (_, zp) in enumerate(pavs) if abs(z - zp) <= _sol.TOL_PAV), None)
        if i is not None:
            H[caso][i] += v
        elif abs(z - base) > _sol.TOL_PAV and np.abs(v).max() > 1e-9:
            fora.setdefault(caso, []).append(no)
    return H, fora


def _direcoes(Hc):
    """Direções ('X', 'Y') em que a combinação tem força horizontal; sem nenhuma, as duas."""
    d = tuple(k for k, c in enumerate("XY") if np.abs(Hc[:, k]).sum() > 1e-9)
    return d or (0, 1)


def _grupo_predominante(m):
    """'concreto' ou 'aco' conforme o material dos pilares (ou de todas as barras, sem pilares)."""
    barras = [b for b in m.barras.values() if _vertical(m, b)] or list(m.barras.values())
    tot = {"concreto": 0.0, "aco": 0.0}
    for b in barras:
        tot["concreto" if b.tipo == "concreto" else "aco"] += _comprimento(m, b)
    return "concreto" if tot["concreto"] > tot["aco"] else "aco"


def _desloc_horizontais(m, pavs, resultados, freq_vento, H):
    """Deslocamento horizontal do topo e entre pisos, ELS frequente com vento (NBR 8800 anexo C / NBR 6118 13.3).
    `freq_vento`: {combinação: fatores}; a direção de cada uma vem das suas forças horizontais `H`."""
    if not pavs or not freq_vento:
        return []
    grupo, npav, out = _grupo_predominante(m), len(pavs), []
    dirs = {n: _direcoes(sum(v * H[k] for k, v in f.items() if k in H)) for n, f in freq_vento.items()}
    for k, d in ((0, "X"), (1, "Y")):
        nomes = [n for n in freq_vento if k in dirs[n]]
        if not nomes:
            continue
        D = np.array([[resultados[n].pavimentos[p][k] if p in resultados[n].pavimentos else 0.0
                       for p, _ in pavs] for n in nomes])
        z0 = _base(m)  # alturas relativas à base (VC-integracao-004)
        for i, (nome, z) in enumerate(pavs):
            delta = np.abs(D[:, i] - (D[:, i - 1] if i else 0.0))
            casos = [("entre_pisos", delta, z - (pavs[i - 1][1] if i else z0))] if npav > 1 else []
            if i == npav - 1:
                casos.append(("topo", np.abs(D[:, i]), z - z0))
            for tipo, vals, h in casos:
                if grupo == "aco":
                    lim = _nbr8800.limite_deslocamento("entre_pisos" if tipo == "entre_pisos" else
                                                       ("topo_pilar" if npav > 1 else "galpao_topo"), h)
                else:
                    lim = _nbr6118.limite_deslocamento("entre_pisos" if tipo == "entre_pisos" else "horizontal_topo", h)
                j = int(vals.argmax())
                out.append({"pavimento": nome, "direcao": d, "tipo": tipo, "valor_m": float(vals[j]),
                            "limite_m": lim["limite_m"], "razao": lim["razao"], "taxa": float(vals[j] / lim["limite_m"]),
                            "combinacao": nomes[j], "item": lim["item"], "ok": bool(vals[j] <= lim["limite_m"])})
    return out


# ---------------------------------------------------------------- estabilidade global

def _rigidez_reduzida(m):
    """Cópia de `m` com a rigidez aproximada da NBR 6118 15.7.3 (efeitos globais de 2ª ordem) nas barras de
    concreto: (EI)sec = 0,4·Eci·Ic nas vigas e 0,8·Eci·Ic nos pilares. O material guarda Ecs em E, logo
    `fator_EI` = 0,4 (ou 0,8)·Eci/Ecs, com Eci/Ecs = 1/αi (NBR 6118 8.2.8, via `materiais.props_concreto`;
    não depende de αE). `fator_EI` já definido na barra é respeitado. → (cópia, [avisos])."""
    c, avisos = copy.deepcopy(m), []
    for b in c.barras.values():
        if b.tipo != "concreto" or "fator_EI" in b.params:
            continue
        fck = c.materiais[b.material].fck
        try:
            pc = _mat.props_concreto(fck / MPa)
            razao = pc["Eci"] / pc["Ecs"]
        except (TypeError, ValueError):
            razao = 1.0
            avisos.append(f"material {b.material}: sem fck de C20–C90; γz com 0,4/0,8·Ecs em vez de Eci (Eci/Ecs = 1)")
        b.params["fator_EI"] = (0.8 if _papel(c, b) == "pilar" else 0.4) * razao
    return c, list(dict.fromkeys(avisos))


def _estabilidade(m, pavs, combs, res_caso, resultados, pdelta, tipos, n_pontos, H, fora):
    """γz (NBR 6118 15.5.3) e B2 (NBR 8800 anexo D) com as combinações ELU/ELU_EXC que têm força horizontal.
    δ vem da combinação LINEAR (1ª ordem), mesmo com P-Δ ligado. Para o γz de concreto o δ vem de uma cópia
    do modelo com a rigidez reduzida da NBR 6118 15.7.3 (`_rigidez_reduzida`); o B2 de aço usa a rigidez
    nominal. `H`, `fora`: `_forcas_horizontais`."""
    est = {"aplicavel": False, "gama_z": {}, "B2": {}, "por_combinacao": {}, "avisos": []}
    for caso, nos in fora.items():
        est["avisos"].append(f"caso {caso}: carga horizontal em nós fora da cota dos pavimentos ({', '.join(nos[:5])}"
                             f"{'…' if len(nos) > 5 else ''}) não entra em H de γz/B2")
    if not pavs:
        est["motivo"] = "sem pavimentos acima da base: γz e B2 não aplicáveis"
        est["avisos"].append(est["motivo"])
        return est
    npav = len(pavs)
    P = {c: _pesos_por_pavimento(m, _mapa_pesos(m, pavs), npav, {c: 1.0}) for c in m.casos}
    base = _base(m)
    z = np.array([z - base for _, z in pavs])  # alturas sobre a base
    h = np.diff(np.concatenate([[0.0], z]))
    red, res_red = None, {}
    if "concreto" in tipos:
        red, av = _rigidez_reduzida(m)
        est["avisos"] += av
        if npav < 4:
            est["avisos"].append(f"γz com a rigidez reduzida da NBR 6118 15.7.3, prevista para estruturas reticuladas "
                                 f"com no mínimo 4 andares (aqui {npav}): mantida por segurança")
    melhor_gz, melhor_b2, por_pav = {}, {}, {}
    por_comb = est["por_combinacao"]  # {combinação: {direcao, gama_z, B2, instavel}} (1ª ordem, base de `_amplificar`)
    for c in combs:
        if c["estado"] not in ("ELU", "ELU_EXC"):
            continue
        f = c["fatores"]
        Hc = sum(v * H[k] for k, v in f.items())
        if np.abs(Hc).sum() < 1e-9:
            continue
        est["aplicavel"] = True
        d = "X" if np.abs(Hc[:, 0]).sum() >= np.abs(Hc[:, 1]).sum() else "Y"
        k = 0 if d == "X" else 1
        Pc = sum(v * P[kk] for kk, v in f.items())
        # 1ª ordem: com P-Δ, `resultados` são de 2ª ordem; `_resultado_comb(pdelta=False)` completa os casos que faltam
        lin = resultados[c["nome"]] if not pdelta else _resultado_comb(m, f, c["estado"], False, res_caso, n_pontos)
        delta = np.array([lin.pavimentos[p][k] if p in lin.pavimentos else 0.0 for p, _ in pavs])
        Hd = Hc[:, k]
        info = por_comb[c["nome"]] = {"direcao": d, "gama_z": None, "B2": None, "instavel": False}
        if "concreto" in tipos:
            try:
                lin_r = _resultado_comb(red, f, c["estado"], False, res_red, n_pontos)
                delta_r = np.array([lin_r.pavimentos[p][k] if p in lin_r.pavimentos else 0.0 for p, _ in pavs])
                r = _nbr6118.gama_z([{"z_m": float(z[i]), "H_kN": float(Hd[i]), "P_kN": float(Pc[i]),
                                      "delta_m": float(delta_r[i])} for i in range(npav)])
                reg = {"valor": r["gama_z"], "combinacao": c["nome"], "classificacao": r["classificacao"],
                       "item": f"NBR 6118 {r['item']}, rigidez de 15.7.3", "majoracao": r["majoracao"],
                       "rigidez": "0,4·Eci·Ic (vigas) e 0,8·Eci·Ic (pilares) — NBR 6118 15.7.3",
                       "M1_kNm": r["M1_kNm"], "dM_kNm": r["dM_kNm"]}
                # o aviso "γz > 1,3" é um só, em `_amplificar` (aqui sairia um por combinação)
                est["avisos"] += [a for a in r["avisos"] if a not in est["avisos"] and not a.startswith("γz = ")]
                chave = info["gama_z"] = r["gama_z"]
            except ValueError as e:
                reg = {"valor": None, "combinacao": c["nome"], "classificacao": "instavel", "item": "NBR 6118 15.5.3",
                       "majoracao": None}
                est["avisos"].append(f"{c['nome']}: {e}")
                chave = math.inf
                info["instavel"] = True
            if d not in melhor_gz or chave > melhor_gz[d][0]:
                melhor_gz[d] = (chave, reg)
        if tipos & {"aco", "pff"}:
            b2_comb = 0.0
            for i in range(npav):
                dh = abs(delta[i] - (delta[i - 1] if i else 0.0))
                sN, sH = float(Pc[i:].sum()), abs(float(Hd[i:].sum()))
                if sH < 1e-9:
                    continue
                try:
                    # ponytail: Rs = 0,85 sempre (pórtico rígido, anexo D) — o mais conservador
                    b2 = _nbr8800.coef_B2(sN, dh, sH, float(h[i]), 0.85)
                except ValueError as e:
                    b2 = math.inf
                    est["avisos"].append(f"{pavs[i][0]} {d} ({c['nome']}): {e}")
                b2_comb = max(b2_comb, b2)
                reg = {"B2": b2, "pavimento": pavs[i][0], "combinacao": c["nome"]}
                if b2 > por_pav.get((d, i), (0,))[0]:
                    por_pav[(d, i)] = (b2, reg)
                if d not in melhor_b2 or b2 > melhor_b2[d]["B2"]:
                    melhor_b2[d] = reg
            if b2_comb:  # maior B2 dos andares: um único fator por combinação
                info["B2"] = b2_comb
                info["instavel"] = info["instavel"] or math.isinf(b2_comb)
    for d, (_, reg) in melhor_gz.items():
        est["gama_z"][d] = reg
    for d, reg in melhor_b2.items():
        b2 = reg["B2"]
        est["B2"][d] = {"max": None if math.isinf(b2) else b2, "pavimento": reg["pavimento"],
                        "combinacao": reg["combinacao"],
                        "classificacao": "instavel" if math.isinf(b2) else _nbr8800.deslocabilidade(b2),
                        "item": "NBR 8800 anexo D (D.2.3) / 4.9.4",
                        "por_pavimento": [{"pavimento": pavs[i][0], "B2": None if math.isinf(v[0]) else v[0],
                                           "combinacao": v[1]["combinacao"]}
                                          for (dd, i), v in sorted(por_pav.items(), key=lambda t: t[0][1]) if dd == d]}
    if not est["aplicavel"]:
        est["motivo"] = "nenhuma combinação ELU com força horizontal nos pavimentos: γz e B2 não aplicáveis"
        if fora:
            est["motivo"] += " (há cargas horizontais fora das cotas dos pavimentos, ignoradas)"
        est["avisos"].append(est["motivo"])
    return est


# ---------------------------------------------------------------- 2ª ordem global: majoração das ações horizontais

def _pt(x, casas=3):
    """Número com vírgula decimal para mensagens."""
    return f"{x:.{casas}f}".replace(".", ",")


def _amplificar(m, combs, est, H, res_caso, resultados, pdelta, n_pontos):
    """Efeitos globais de 2ª ordem sem P-Δ (VC-guia-001, VC-logica-acoes-001). Para cada combinação ELU/ELU_EXC com
    força horizontal, pelo γz (concreto) ou B2 (aço) da própria combinação, calculado em 1ª ordem por `_estabilidade`:
      - concreto, 1,1 < γz ≤ 1,3: ações horizontais × 0,95·γz (NBR 6118:2023 15.7.2);
      - aço, 1,1 < B2 ≤ 1,4: ações horizontais × B2 (NBR 8800:2008 4.9.4, MAES simplificado: o maior B2 dos
        andares multiplica as ações horizontais; B1 e o deslocamento lateral das ações verticais não entram);
      - acima (γz > 1,3 ou B2 > 1,4) ou instável: sem majoração, com aviso de análise de 2ª ordem rigorosa (P-Δ).
    A estrutura predominante (`_grupo_predominante`) escolhe entre γz e B2. As ações horizontais são os casos com
    força horizontal (vento, sismo) e as torções acidentais _TOR dos sismos. Os resultados das combinações majoradas
    substituem os de `resultados` (verificação, reações); `combs[i]["majoracao_horizontal"]` guarda o fator.
    `est["por_combinacao"]` fica só com as combinações majoradas ou que excedem. → avisos (formato ESPEC §6.5)."""
    por = est["por_combinacao"]
    avisos = []
    if pdelta or not por:  # com P-Δ os efeitos de 2ª ordem já estão na análise
        est["por_combinacao"] = {}
        return avisos
    concreto = _grupo_predominante(m) == "concreto"
    chave, lim_max = ("gama_z", 1.3) if concreto else ("B2", 1.4)
    horiz = {c for c, h in H.items() if np.abs(h).sum() > 1e-9}
    horiz |= {c for c in m.casos if c.endswith(_comb.TORCAO) and c[: -len(_comb.TORCAO)] in horiz}
    majoradas, excedem = [], []
    for c in combs:
        info = por.get(c["nome"])
        if info is None:
            continue
        v = info[chave]
        if v is not None and not info["instavel"] and v <= 1.1:
            del por[c["nome"]]  # nós fixos / pequena deslocabilidade: nada a fazer
        elif v is None and not info["instavel"]:
            del por[c["nome"]]
        elif info["instavel"] or v > lim_max:
            info["situacao"] = "excede"
            excedem.append((c["nome"], math.inf if v is None else v))
        else:
            fator = 0.95 * v if concreto else v
            fat = {k: g * fator if k in horiz else g for k, g in c["fatores"].items()}
            resultados[c["nome"]] = _resultado_comb(m, fat, c["estado"], False, res_caso, n_pontos)
            c["majoracao_horizontal"] = info["fator"] = fator
            info["situacao"] = "majorada"
            majoradas.append((c["nome"], fator, v))
    est["majoracao"] = {"base": chave, "n_majoradas": len(majoradas), "n_excedem": len(excedem),
                        "fator_max": max((f for _, f, _ in majoradas), default=None)}
    if majoradas:
        n, f, v = max(majoradas, key=lambda t: t[1])
        if concreto:
            msg = (f"{len(majoradas)} combinações últimas com 1,1 < γz ≤ 1,3 (nós móveis): ações horizontais majoradas "
                   f"por 0,95·γz (NBR 6118:2023 15.7.2); máximo 0,95·γz = {_pt(f)} (γz = {_pt(v)}) em '{n}'. Aproximação "
                   "dos efeitos globais de 2ª ordem; para a análise rigorosa, rode com P-Δ.")
        else:
            msg = (f"{len(majoradas)} combinações últimas com 1,1 < B2 ≤ 1,4 (média deslocabilidade): ações horizontais "
                   f"majoradas por B2 (NBR 8800:2008 4.9.4 e anexo D, MAES simplificado: o maior B2 dos andares multiplica "
                   f"as ações horizontais; B1 e o deslocamento lateral das ações verticais não são tratados); máximo "
                   f"B2 = {_pt(f)} em '{n}'. Para a análise rigorosa, rode com P-Δ.")
        avisos.append(_aviso("gama_z_majorado" if concreto else "b2_majorado", msg))
    if excedem:
        n, v = max(excedem, key=lambda t: t[1])
        pior = "instável" if math.isinf(v) else _pt(v)
        if concreto:
            msg = (f"γz > 1,3 (ou instável) em {len(excedem)} combinações últimas (pior: {pior} em '{n}'): a majoração "
                   "0,95·γz não vale (NBR 6118:2023 15.7.2). É necessária análise de 2ª ordem rigorosa (rode com P-Δ) "
                   "e rigidez reduzida (15.7.3); os esforços verificados são de 1ª ordem, sem majoração, contra a segurança.")
        else:
            msg = (f"B2 > 1,4 (grande deslocabilidade) ou instável em {len(excedem)} combinações últimas (pior: {pior} "
                   f"em '{n}'): o MAES não se aplica (NBR 8800:2008 4.9.4). É necessária análise de 2ª ordem rigorosa "
                   "(rode com P-Δ, com imperfeições iniciais e rigidez reduzida, 4.9.7); os esforços verificados são "
                   "de 1ª ordem, sem majoração, contra a segurança.")
        avisos.append(_aviso("gama_z_2a_ordem" if concreto else "b2_2a_ordem", msg))
    est["avisos"] += [a["mensagem"] for a in avisos]
    return avisos


def _deslocamentos_sismicos(m, pavs, cfg, res_caso, n_pontos, avisos):
    """Deslocamento relativo entre pavimentos do sismo (NBR 15421:2006): δx = Cd·δxe/I, Δx = δx(i) − δx(i−1) ≤
    limite·hsx (`sismo.drift_sismico`). δxe = deslocamento elástico do pavimento (`Resultado.pavimentos`, centro
    geométrico) no caso SX (direção X) ou SY (direção Y). Cd vem de `config.sismo.sistema_resistente` (tabela 6);
    sem ele não há verificação e sai um aviso."""
    sc = cfg.get("sismo")
    casos = [(c, k, d) for c, k, d in (("SX", 0, "X"), ("SY", 1, "Y")) if c in m.casos]
    if not sc or int(sc.get("zona", 0)) < 2 or not pavs or not casos:
        return []
    sr = sc.get("sistema_resistente")
    if sr is None:
        avisos.append(_aviso("sismo_drift_nao_verificado", "deslocamento relativo entre pavimentos do sismo não "
                             "verificado: informe config.sismo.sistema_resistente (R, Cd e Ω0 da tabela 6 da "
                             "NBR 15421:2006)"))
        return []
    base, cat = _base(m), str(sc.get("categoria_utilizacao", "I")).upper()
    cotas = [z - base for _, z in pavs]
    out = []
    for caso, k, d in casos:
        if caso not in res_caso:
            res_caso[caso] = _sol.analisar(m, caso, False, n_pontos)
        pv = res_caso[caso].pavimentos
        dxe = [float(pv[nome][k]) if nome in pv else 0.0 for nome, _ in pavs]
        for (nome, _), l in zip(pavs, _sismo.drift_sismico(dxe, cotas, _sismo.TAB6[sr]["Cd"], cat)):
            frac = _sismo.LIMITE_DRIFT[cat]
            out.append({"caso": caso, "pavimento": nome, "direcao": d, "tipo": "entre_pisos", "valor_m": l["Delta_m"],
                        "limite_m": l["limite_m"], "razao": f"{frac:g}·h", "taxa": l["razao"], "delta_x_m": l["delta_x_m"],
                        "combinacao": caso, "item": "NBR 15421:2006 (deslocamento relativo, δx = Cd·δxe/I)",
                        "ok": bool(l["ok"])})
    return out


# ---------------------------------------------------------------- calcular

def _normas_usadas(m, cfg, tipos):
    mods = [(_comb, "acoes.combinacoes")]
    if cfg.get("combinacoes") == "nbr8400":
        mods[0] = (_nbr8400, "acoes.nbr8400")
    tv = (cfg.get("vento") or {}).get("tipo")
    if tv == "barras":
        mods.append((_vb, "acoes.vento_barras"))
    elif tv == "nbr8400":
        if cfg.get("combinacoes") != "nbr8400":
            mods.append((_nbr8400, "acoes.nbr8400"))
    elif cfg.get("vento"):
        mods.append((_vento, "acoes.vento"))
    if cfg.get("sismo") and int(cfg["sismo"].get("zona", 0)) > 0:
        mods.append((_sismo, "acoes.sismo"))
    for t, nome in (("aco", "aco.nbr8800"), ("pff", "aco.nbr14762"), ("concreto", "concreto.nbr6118")):
        if t in tipos and _NORMA_DE[t] is not None:
            mods.append((_NORMA_DE[t], nome))
    out = []
    for mod, nome in mods:
        g = re.match(r"(NBR \d+):(\d{4})", mod.EDICAO)
        lim = [c["limitacao"] for c in getattr(mod, "COBERTURA", []) if c.get("limitacao")]
        out.append({"norma": g.group(1), "edicao": g.group(2), "modulo": nome, "limitacao": " | ".join(lim),
                    "limitacoes": lim})
    return out


def calcular(modelo, opcoes=None):
    """#20 Fluxo completo (ESPEC §6.5): valida → gera ações → combinações → análises → verificação por norma
    → deslocamentos → γz/B2 → envoltória de reações. `opcoes`: pdelta, barras (None = todas), n_pontos."""
    t0 = time.perf_counter()
    m = modelo
    opcoes = dict(opcoes or {})
    erros = []
    _chaves(opcoes, {"pdelta", "barras", "n_pontos"}, lambda c, msg: erros.append({"campo": c, "mensagem": msg}),
            "opcoes.")
    if erros:
        raise ErroEntrada(erros)
    cfg = _cfg(m)
    pdelta = cfg["pdelta"] if opcoes.get("pdelta") is None else bool(opcoes["pdelta"])
    npt = opcoes.get("n_pontos") or N_PONTOS
    if isinstance(npt, bool) or not isinstance(npt, int) or npt < 3:
        raise ErroEntrada("n_pontos: inteiro ≥ 3", "opcoes.n_pontos")
    _exigir(m)
    sel = opcoes.get("barras")
    if sel is not None:
        falta = [i for i in sel if i not in m.barras]
        if falta:
            raise ErroNaoEncontrado("barra", falta[0], m.barras)
    avisos = [_aviso(v["codigo"], v["mensagem"], v["ref"]) for v in validar(m) if v["nivel"] == "aviso"]
    avisos += gerar_acoes(m)["avisos"]
    combs = combinacoes(m)
    if not combs:
        raise ErroEntrada("nenhuma combinação gerada: defina casos com cargas", "casos")
    snapshot = Modelo.from_dict(m.to_dict())
    snapshot._ultimo_id = dict(m._ultimo_id)

    res_caso = {}  # análises lineares por caso (base das combinações e do δ de 1ª ordem)
    resultados = {c["nome"]: _resultado_comb(m, c["fatores"], c["estado"], pdelta, res_caso, npt) for c in combs}
    por_estado = {}
    for c in combs:
        por_estado.setdefault(c["estado"], []).append(c["nome"])
    elu = por_estado.get("ELU", []) + por_estado.get("ELU_EXC", [])
    qp = (por_estado.get("ELS_QP") or [None])[0]
    freq = por_estado.get("ELS_FREQ", [])

    # 2ª ordem global ANTES da verificação (VC-guia-001): γz/B2 de 1ª ordem majoram as ações horizontais das ELU
    pavs = _pavimentos_acima(m)
    tipos = {b.tipo for b in m.barras.values()}
    H, fora = _forcas_horizontais(m, pavs)
    est = _estabilidade(m, pavs, combs, res_caso, resultados, pdelta, tipos, npt, H, fora)
    avisos += _amplificar(m, combs, est, H, res_caso, resultados, pdelta, npt)

    barras = [m.barras[i] for i in (sel if sel is not None else m.barras)]
    linhas, por_comb, motores = [], {}, {}
    for b in barras:
        linha, por, r = _verificar_uma(m, b, resultados, elu, qp, freq)
        linhas.append(linha)
        if por:
            por_comb[b.id] = por
        if r is not None:
            linha["_motor"] = r
            motores[b.id] = r
    vistos = {}
    for l in linhas:
        for a in l["avisos"]:
            if l["criterio"] == "nao_verificada":
                avisos.append(_aviso("barra_nao_verificada", f"barra {l['barra']}: {a}", {"tipo": "barra", "id": l["barra"]}))
            else:
                vistos.setdefault(a, []).append(l["barra"])
    for msg, bs in vistos.items():
        avisos.append({**_aviso("aviso_norma", msg, {"tipo": "barra", "id": bs[0]}), "barras": bs})

    cant = [b.id for b in barras if b.tipo in ("aco", "pff") and "alfa_principal" in (m.secoes[b.secao].props or {})]
    if cant:  # a verificação usa os eixos geométricos da cantoneira (rot da barra não alinha aos principais)
        avisos.append({**_aviso("cantoneira_eixos_geometricos",
                                "cantoneira verificada nos eixos geométricos, não nos principais (alfa_principal); "
                                "alinhe a barra (rot) aos eixos principais ou confira à parte",
                                {"tipo": "barra", "id": cant[0]}), "barras": cant})
    fat = {c["nome"]: c["fatores"] for c in combs}
    freq_vento = {n: fat[n] for n in freq if any(k in m.casos and m.casos[k].tipo == "V" for k in fat[n])}
    desl = {"verticais": _desloc_verticais(m, barras, resultados, por_estado, linhas, avisos),
            "horizontais": _desloc_horizontais(m, pavs, resultados, freq_vento, H),
            "sismicos": _deslocamentos_sismicos(m, pavs, cfg, res_caso, npt, avisos)}
    for l in linhas:
        l.pop("_motor", None)
    reac = _env_alvo(resultados, elu, "reacoes") if elu else {}
    linhas.sort(key=lambda l: -math.inf if l["taxa"] is None else -l["taxa"])
    return Calculo(
        modelo_nome=m.nome, data=datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        opcoes={"pdelta": pdelta, "n_pontos": npt}, combinacoes=combs, resultados=resultados,
        verificacao=linhas, deslocamentos=_puro(desl), estabilidade=_puro(est), reacoes_envoltoria=reac, avisos=_puro(avisos),
        tempo_s=time.perf_counter() - t0, normas=_normas_usadas(m, cfg, tipos), por_combinacao=por_comb,
        modelo=snapshot)


# ================================================================ 21-24: consultas ao cálculo

_ESTADOS_ALVO = {"esforcos": ("ELU", "ELU_EXC"), "reacoes": ("ELU", "ELU_EXC"),
                 "deslocamentos": ("ELS_QP", "ELS_FREQ", "ELS_RARA"), "flechas": ("ELS_QP", "ELS_FREQ", "ELS_RARA"),
                 "pavimentos": ("ELS_QP", "ELS_FREQ", "ELS_RARA")}


def resultados(calculo, alvo, ids=None, combinacao=None, estados=None):
    """#21 Resultados de `alvo` ∈ {deslocamentos, reacoes, esforcos, flechas, pavimentos}.
    Com `combinacao`: valores dela (mesma forma do ResultadoDict). Sem: envoltória {max, min, comb_max, comb_min}
    (por componente e, em esforços/flechas, por ponto) nos `estados` (padrão: esforços e reações → ELU e
    ELU_EXC; deslocamentos, flechas e pavimentos → ELS). `ids` restringe nós/barras/pavimentos."""
    if alvo not in _ESTADOS_ALVO:
        raise ErroEntrada(f"alvo '{alvo}' inválido; use {', '.join(_ESTADOS_ALVO)}", "alvo")
    if combinacao is not None:
        if combinacao not in calculo.resultados:
            raise ErroNaoEncontrado("combinação", combinacao, calculo.resultados)
        dados = _resultado_dict(combinacao, calculo.resultados[combinacao], False)[alvo]
    else:
        est = tuple(estados or _ESTADOS_ALVO[alvo])
        nomes = [c["nome"] for c in calculo.combinacoes if c["estado"] in est]
        if not nomes:
            raise ErroEntrada(f"nenhuma combinação nos estados {', '.join(est)}", "estados")
        dados = _env_alvo(calculo.resultados, nomes, alvo)
    if ids is not None:
        falta = [i for i in ids if i not in dados]
        if falta:
            raise ErroNaoEncontrado(alvo, falta[0], dados)
        dados = {i: dados[i] for i in ids}
    return dados


def verificacao(calculo, filtro=None):
    """#22 Linhas da verificação por barra, da maior taxa para a menor (não verificadas primeiro).
    `filtro`: reprovadas (bool), tipo (aco|pff|concreto), taxa_min (float)."""
    f = filtro or {}
    if not isinstance(f, dict) or set(f) - {"reprovadas", "tipo", "taxa_min"}:
        raise ErroEntrada("filtro: use reprovadas, tipo e/ou taxa_min", "filtro")
    out = []
    for l in calculo.verificacao:
        if f.get("reprovadas") and l["ok"]:
            continue
        if f.get("tipo") and l["tipo"] != f["tipo"]:
            continue
        if f.get("taxa_min") is not None and (l["taxa"] is None or l["taxa"] < f["taxa_min"]):
            continue
        out.append(copy.deepcopy(l))
    return _puro(out)


def verificacao_barra(calculo, barra):
    """#23 Linha da barra + `detalhes` do motor (re-executado só para a combinação determinante) +
    `por_combinacao` [{combinacao, taxa, criterio}]. No concreto, uma chamada por combinação ELU, só ELU
    (sem ELS): max(por_combinacao.taxa) ≤ taxa da linha, que inclui fissuração/flecha da envoltória."""
    linha = next((l for l in calculo.verificacao if l["barra"] == barra), None)
    if linha is None:
        raise ErroNaoEncontrado("barra", barra, [l["barra"] for l in calculo.verificacao])
    if linha["criterio"] == "nao_verificada":
        return _puro({**linha, "detalhes": {}, "por_combinacao": []})
    m, res = calculo.modelo, calculo.resultados
    b = m.barras[barra]
    por_estado = {}
    for c in calculo.combinacoes:
        por_estado.setdefault(c["estado"], []).append(c["nome"])
    elu = por_estado.get("ELU", []) + por_estado.get("ELU_EXC", [])
    qp = (por_estado.get("ELS_QP") or [None])[0]
    freq = por_estado.get("ELS_FREQ", [])
    if b.tipo == "concreto":
        r = _concreto_barra(m, b, _comprimento(m, b), res, elu, qp, freq)
        por = []
        for n in elu:
            # sem ELS: fissuração/flecha dependem da armadura da envoltória, não de uma combinação isolada
            ri = _concreto_barra(m, b, _comprimento(m, b), res, [n], None, [])
            por.append({"combinacao": n, "taxa": ri["taxa_max"], "criterio": ri["criterio"]})
    else:
        r, _, _ = _aco_barra(m, b, _comprimento(m, b), res, elu, so=linha["combinacao"])
        por = calculo.por_combinacao.get(barra, [])
    return _puro({**linha, "detalhes": r["detalhes"], "por_combinacao": por})


def estabilidade(calculo):
    """#24 Bloco `estabilidade` do Calculo (γz, B2, avisos)."""
    return _puro(calculo.estabilidade)


# ================================================================ 25-28: ligações, catálogo, materiais, normas

def _ligacoes():
    try:
        from .aco import ligacoes
    except ImportError as e:
        raise NotImplementedError("aco.ligacoes ainda não disponível (ficha 011)") from e
    return ligacoes


# rótulo curto de cada campo de `aco.ligacoes` para formulários (a descrição longa fica em `descricao`);
# chave (tipo, campo) vence a chave só do campo
_ROTULOS_LIG = {
    "d_mm": "Diâmetro do parafuso", "tipo_parafuso": "Tipo de parafuso", "rosca_no_plano": "Rosca no plano de corte",
    "planos_corte": "Planos de corte", "t": "Espessura da chapa", "aco_chapa": "Aço da chapa",
    "lf": "Distância livre lf", "deformacao_limitada": "Deformação limitada", "borda_laminada": "Bordas laminadas",
    "L": "Comprimento dos filetes", "eletrodo": "Eletrodo", "aco_base": "Aço do metal base",
    "t_base": "Espessura da junta", "h": "Altura da chapa", "n": "Parafusos na linha", "s": "Espaçamento s",
    "eh": "Borda livre eh", "b_solda": "Filete chapa–apoio", "tw": "Espessura da alma", "aco_alma": "Aço da viga",
    "e_alma": "Distância à extremidade", "e": "Excentricidade", "n_linhas": "Linhas de furação",
    "n_por_linha": "Parafusos por linha", "g": "Gabarito g", "e1": "Distância e1 à extremidade", "Ct": "Coeficiente Ct",
    "sujeito_corrosao": "Sujeita à corrosão",
    ("solda_filete", "b"): "Perna do filete", ("ligacao_tracao", "b"): "Largura da chapa",
}


def tipos_ligacao():
    """#25 Tipos de ligação e seus campos (delegado a `aco.ligacoes`); cada campo ganha `rotulo` curto
    (campo sem rótulo conhecido usa o próprio nome)."""
    lig = _ligacoes()
    tipos = lig.tipos_ligacao()
    for t in tipos:
        for c in t["campos"]:
            c["rotulo"] = c.get("rotulo") or _ROTULOS_LIG.get((t["tipo"], c["nome"])) or _ROTULOS_LIG.get(c["nome"]) or c["nome"]
        # projetista (docs/v3/LIGACOES-UI.md): nome/família/liga dos tipos antigos, faixa e inteiros do esquema, sugerir
        for k, v in getattr(lig, "META", {}).get(t["tipo"], {}).items():
            t.setdefault(k, v)
        esq = {c["nome"]: c for c in lig.esquema(t["tipo"])}
        for c in t["campos"]:
            e = esq.get(c["nome"], {})
            for k in ("min", "max", "inteiro"):
                if c.get(k) is None and e.get(k) is not None:
                    c[k] = e[k]
        reg = lig.registro(t["tipo"])
        t["sugerir"] = bool(reg and reg.get("sugerir"))
        t["exemplo"] = _exemplo_lig(lig, t)
    return _puro(tipos)


# Exemplo de cada tipo (miniatura da biblioteca e ponto de partida do projetista): padrões do esquema + estes valores
# nos campos obrigatórios sem padrão + `sugerir` com estes perfis. Antigos em m, contrato em mm.
_AMOSTRA_LIG = {
    "perfil_viga": "W310x38.7", "perfil_suporte": "W250x73", "perfil_pilar": "W250x73",
    "dim_a": 76.2, "t_perfil": 6.35, "dim_a_1": 63.5, "t_perfil_1": 6.35,
    ("chapa_simples", "h"): 0.23, ("chapa_simples", "t"): 0.0095, ("chapa_simples", "d_mm"): 19.05,
    ("chapa_simples", "n"): 3, ("chapa_simples", "s"): 0.075, ("chapa_simples", "e"): 0.06, ("chapa_simples", "tw"): 0.0058,
    ("ligacao_tracao", "b"): 0.2, ("ligacao_tracao", "t"): 0.0095, ("ligacao_tracao", "d_mm"): 19.05,
    ("ligacao_tracao", "n_linhas"): 2, ("ligacao_tracao", "n_por_linha"): 3, ("ligacao_tracao", "e1"): 0.04,
    ("parafuso", "d_mm"): 16, ("parafuso", "t"): 0.0095, ("parafuso", "lf"): 0.03,
    ("solda_filete", "b"): 0.006, ("solda_filete", "L"): 0.2, ("solda_filete", "t_base"): 0.0095,
}
_EXEMPLOS_LIG = {}


def _exemplo_lig(lig, t):
    tipo = t["tipo"]
    if tipo in _EXEMPLOS_LIG:
        return _EXEMPLOS_LIG[tipo]
    nomes = [e["nome"] for e in t["esforcos"]]
    esf = {n: (100.0 if i == 0 else 0.0) for i, n in enumerate(nomes)}
    if "M" in esf:   # rígidas e emendas: momento e cortante típicos de viga W 310
        esf.update(M=60.0, N=0.0, **({"V": 100.0} if "V" in esf else {}))
    if tipo == "parafuso":
        esf = dict(Ft=20.0, Fv=35.0)
    dados = {c["nome"]: c["padrao"] for c in t["campos"] if c.get("padrao") is not None}
    for c in t["campos"]:
        v = _AMOSTRA_LIG.get((tipo, c["nome"]), _AMOSTRA_LIG.get(c["nome"]))
        if v is not None:
            dados[c["nome"]] = v
    reg = lig.registro(tipo)
    if reg and reg.get("sugerir"):
        try:
            dados.update({k: v for k, v in reg["sugerir"](dict(dados), dict(esf)).items() if v is not None})
        except (ValueError, KeyError, TypeError):
            pass
    _EXEMPLOS_LIG[tipo] = dict(dados=dados, esforcos=esf)
    return _EXEMPLOS_LIG[tipo]


def _membros_geometria(dados, abertura=1):
    """Perfis ligados (dados perfil_*) com d, bf, tw, tf em mm, para o desenho da ligação (geometria.membros).
    U/Ue (G5, docs/v3/LIGACOES-U.md): forma "U", xg (mm, centroide → costas da alma) e abertura_y (±1, lado y para
    onde as mesas abrem). Viga U: costas da alma em y = −abertura·xg, como as chapas do contrato."""
    out = []
    for k, v in (dados or {}).items():
        if not (k.startswith("perfil_") and isinstance(v, str) and v):
            continue
        try:
            p = _sec.catalogo(v).props
        except KeyError:
            continue
        if p.get("tipo") not in ("I", "U", "Ue"):
            continue
        tw, tf = p.get("tw") or p["t"], p.get("tf") or p["t"]   # formado a frio: tw = tf = t
        m = dict(barra=k[7:], perfil=v, campo=k, d=p["h"] * 1e3, bf=p["b"] * 1e3, tw=tw * 1e3, tf=tf * 1e3,
                 apoio=str((dados or {}).get("apoio") or ""))
        if p["tipo"] != "I":
            m.update(forma="U", xg=p.get("xg", 0.0) * 1e3, abertura_y=-1 if abertura < 0 else 1)
        out.append(m)
    return out


def verificar_ligacao(tipo, dados, esforcos):
    """#25 Verifica uma ligação (delegado a `aco.ligacoes`; medidas em m nos tipos antigos, mm nos do contrato, esforços
    em kN). Para o projetista, o resultado sempre traz `taxa`, `taxa_max` e `geometria` (com `membros` = perfis ligados)."""
    lig = _ligacoes()
    try:
        r = lig.verificar_ligacao(tipo, dados, esforcos)
    except (ValueError, KeyError, TypeError) as e:
        raise ErroEntrada(str(e.args[0] if isinstance(e, KeyError) and e.args else e), "ligacao") from e
    r.setdefault("taxa", r.get("taxa_max", 0.0))
    r.setdefault("taxa_max", r["taxa"])
    if r.get("geometria") is None and hasattr(lig, "geometria_basica"):
        r["geometria"] = lig.geometria_basica(tipo, dados)
    if isinstance(r.get("geometria"), dict):
        # viga_viga_recorte não leva `apoio` nos dados (o tipo já é alma de viga): sem ele o desenho põe o suporte em pé, como pilar
        ab = r["geometria"].get("abertura_y") or (dados or {}).get("abertura_y") or 1
        r["geometria"].setdefault("membros", _membros_geometria(dict(dados or {}, apoio="alma_viga") if tipo == "viga_viga_recorte" else dados,
                                                                 -1 if float(ab) < 0 else 1))
        from .aco import ligacoes_campos as _lc   # `campo` de cada peça: o clique na peça leva ao campo (docs/v3/LIGACOES-EDITOR.md)
        _lc.anotar(tipo, r["geometria"], dados, {c["nome"] for c in lig.esquema(tipo)} if hasattr(lig, "esquema") else set())
    return _puro(r)


def _lig_ui(fn, *a):
    try:
        return _puro(fn(*a))
    except (ValueError, KeyError, TypeError) as e:
        raise ErroEntrada(str(e.args[0] if isinstance(e, KeyError) and e.args else e), "ligacao") from e


def sugerir_ligacao(tipo, perfis, esforcos):
    """Pré-dimensionamento do tipo (contrato: `sugerir(perfis, esforcos) → dados`)."""
    reg = _ligacoes().registro(tipo)
    if not (reg and reg.get("sugerir")):
        raise ErroEntrada(f"tipo '{tipo}' não tem pré-dimensionamento (use otimizar)", "tipo")
    return _lig_ui(reg["sugerir"], dict(perfis or {}), dict(esforcos or {}))


MAX_LOTE_LIGACOES = 500   # itens por POST /api/ligacoes/geometrias (o WPF divide em lotes)


def geometrias_ligacoes(itens):
    """Lote para o Realista do WPF: cada item {tipo, dados, perfis?} -> sugerir (se há perfis, esforços típicos N 50, V 50,
    M 30) + verificar com esforços nulos. Devolve, na mesma ordem, {geometria, motivo, avisos} ou {erro}; um item ruim
    não derruba o lote."""
    if len(itens or []) > MAX_LOTE_LIGACOES:
        raise ErroEntrada(f"lote com {len(itens)} itens: máximo {MAX_LOTE_LIGACOES}", "itens")
    nulos = dict(N=0, V=0, Vz=0, M=0, Mz=0, T=0)
    saida = []
    for it in itens or []:
        try:
            tipo, dados, perfis = it.get("tipo"), dict(it.get("dados") or {}), it.get("perfis")
            if perfis:
                dados.update(perfis)
                sug = dict(sugerir_ligacao(tipo, perfis, dict(N=50, V=50, M=30)) or {})
                pedido = tipo
                tipo, mot_sug = sug.pop("tipo_ligacao", None) or tipo, sug.pop("motivo", None)   # G6 r4: troca (solda direta)
                dados.update({k: v for k, v in sug.items() if v is not None})
                troca = None if tipo == pedido else dict(   # G6 r5: o WPF grava no projeto do grupo
                    tipo=tipo, motivo=mot_sug, dados={k: v for k, v in sug.items() if v is not None and k not in
                                                      ("x_max", "x_face", "abertura_y")})
            else:
                mot_sug = troca = None
            r = verificar_ligacao(tipo, dados, nulos)
            mot = r.get("motivo") or mot_sug
            cab = next((v for v in r.get("verificacoes") or [] if v.get("nome", "").startswith("geometria: comprimento disponível")), None)
            curta = bool(r.get("barra_curta")) or bool(cab and not cab["ok"])
            if cab and not cab["ok"]:   # G6 r3: não cabe no x_max → sem ligação válida nesta ponta, com o motivo
                mot = (f"barra curta demais para a ligação: precisa {cab['solicitante']:.0f} mm, "
                       f"disponível {cab['resistente']:.0f} mm")
            saida.append(dict(geometria=r.get("geometria"), motivo=mot, avisos=r.get("avisos") or [], ok=bool(r.get("ok")),
                              **({"barra_curta": True, "excluir_interferencia": True} if curta else {}),
                              **({"troca": troca} if troca else {})))
        except Exception as e:   # noqa: BLE001 - o lote continua; o WPF cai na proposta local
            saida.append(dict(erro=str(e)))
    return saida


def otimizar_ligacao(tipo, dados, esforcos):
    """Busca simples do menor detalhe que passa (`aco.ligacoes.otimizar`); o resultado vem como em verificar_ligacao."""
    r = _lig_ui(_ligacoes().otimizar, tipo, dados, esforcos)
    if r.get("resultado") is not None:   # sem solução por dados inválidos: resultado None, `motivo` explica
        r["resultado"] = verificar_ligacao(tipo, r["dados"], esforcos)
    return r


def memorial_ligacao(tipo, dados, esforcos):
    """Seção HTML do memorial da ligação (`aco.ligacoes.memorial_html`), com os rótulos dos campos."""
    rot = {c["nome"]: c["rotulo"] for t in tipos_ligacao() if t["tipo"] == tipo for c in t["campos"]}
    return dict(html=_lig_ui(_ligacoes().memorial_html, tipo, dados, esforcos, None, rot))


# ---- grupos de ligações do modelo (aco/ligacoes_modelo.py, docs/v3/LIGACOES-GRUPOS.md)

def _lm(fn, modelo, *a):
    from .aco import ligacoes_modelo as lm
    try:
        return _puro(getattr(lm, fn)(modelo, *a))
    except KeyError as e:
        raise ErroNaoEncontrado("grupo de ligações", e.args[0] if e.args else "", lm._grupos(modelo)) from None
    except (ValueError, TypeError) as e:
        raise ErroEntrada(str(e), "ligacoes") from e


def ligacoes_identificar(modelo):
    """Identifica as ligações nos nós e (re)agrupa as iguais em `modelo.ligacoes`, guardando as edições do
    usuário (grupos travados/manuais). → {grupos, membros, novos}"""
    return _lm("identificar", modelo)


def ligacoes_ligar_no(modelo, dados):
    """Liga o nó {no} que encosta no vão de outra barra sem nó compartilhado (ajuste de `identificar.ajustes`): o nó
    vai ao eixo da barra e ela é dividida ali. Muda a estrutura (rota em MUTAM: descarta o cálculo)."""
    return _lm("ligar_no", modelo, dados)


def ligacoes_grupos(modelo, calculo=None):
    """Grupos de ligações; com cálculo, cada grupo vem com taxa por membro, governante e taxa máxima."""
    return _lm("resumo_grupos", modelo, calculo)


def ligacoes_editar(modelo, dados):
    """Edição dos grupos: {acao: renomear|travar|projeto|mover|dividir|unir, ...} (ver ligacoes_modelo.editar)."""
    return _lm("editar", modelo, dados)


def ligacoes_aplicar_troca(modelo, calculo, dados):
    """Aplica a troca proposta pelo Realista a um membro {grupo, barra, no, tipo_ligacao, dados}: divide o grupo se
    preciso, grava o projeto e re-verifica com os esforços reais; não passa → nada muda e o erro cita a verificação."""
    return _lm("aplicar_troca", modelo, calculo, dados)


def ligacoes_dimensionar(modelo, calculo, grupo):
    """Dimensiona o grupo pela pior ligação (todas as combinações ELU de todos os membros); o projeto vale
    para o grupo inteiro."""
    return _lm("dimensionar_grupo", modelo, calculo, grupo)


# ---- placa de base pelo método NBR (aco/placa_base.py, docs/v3/PLACA-BASE-V2.md §10): a janela da placa usa estas

_NOTA_BIAXIAL = ("Flexão biaxial: o método da placa é uniaxial. Cada combinação é verificada com My no eixo forte e com "
                 "Mz no eixo fraco (a mesma placa girada), separadamente; vale o pior. Os dois momentos não são somados.")


def _pb():
    from .aco import placa_base
    return placa_base


def placa_base_esquema():
    """Campos de entrada da placa de base (dimensões em m, resistências em kN/m²) e as chapas/diâmetros/classes."""
    pb = _pb()
    return _puro(dict(campos=pb.esquema_placa_base(), chapas=pb.CHAPAS, diametros_mm=pb.DIAMETROS_MM, classes=pb.CLASSES))


def _passo_placa(nome):
    """Passo da janela: fixação (chumbadores, concreto ACI, corte), chapa (espessura, furo, pressão no concreto), solda."""
    if nome.startswith("solda"):
        return "solda"
    return "chapa" if nome.startswith("chapa") or nome.startswith("concreto: pressão de contato") else "fixacao"


def _casos_placa(modelo, calculo, c):
    """Casos de esforços da placa: [{barra, ponta, no, combinacao, esf {N, V, My, Mz, sismo}, secao}].
    `membros` [{barra, ponta | no}] → todas as combinações ELU do cálculo na ponta (aco.ligacoes_modelo.esforcos_ponta);
    senão `esforcos` (um dict ou lista: N, V | Vy/Vz, M | My, Mz, nome) com `secao` (nome do modelo/catálogo ou props)."""
    from .aco import ligacoes_modelo as lm
    casos = []
    if c.get("membros"):
        if calculo is None:
            raise ErroEntrada("calcule o modelo antes (membros usam as combinações ELU do cálculo)", "membros")
        for mb in c["membros"]:
            b = modelo.barras.get(str(mb.get("barra")))
            if b is None:
                raise ErroNaoEncontrado("barra", mb.get("barra"), modelo.barras)
            ponta = mb.get("ponta") or ("i" if mb.get("no") == b.ni else "j" if mb.get("no") == b.nj else None)
            if ponta not in ("i", "j"):
                raise ErroEntrada("membros: informe ponta i/j ou o nó de uma das pontas", "ponta")
            esf = lm.esforcos_ponta(calculo, b.id, ponta)
            if not esf:
                raise ErroEntrada(f"barra {b.id}: sem esforços ELU no cálculo (calcule de novo)", "membros")
            sec = modelo.secoes[b.secao].props
            for nome, e in esf.items():
                casos.append(dict(barra=b.id, ponta=ponta, no=b.ni if ponta == "i" else b.nj, combinacao=nome,
                                  esf=lm.esforcos_placa(modelo, e), secao=sec))
        return casos
    esf = c.get("esforcos")
    if not esf:
        raise ErroEntrada("informe membros (com cálculo) ou esforcos + secao", "esforcos")
    s = c.get("secao")
    if isinstance(s, str):
        sec = modelo.secoes[s].props if modelo is not None and s in modelo.secoes else None
        if sec is None:
            try:
                sec = _sec.catalogo(s).props
            except KeyError:
                raise ErroNaoEncontrado("perfil", s, list(modelo.secoes) if modelo is not None else []) from None
    elif isinstance(s, dict):
        sec = s
    else:
        raise ErroEntrada("secao: nome do perfil ou propriedades {tipo, h, b, tw, tf}", "secao")
    for i, e in enumerate(esf if isinstance(esf, list) else [esf]):
        try:
            V = float(e["V"]) if "V" in e else math.hypot(float(e.get("Vy", 0)), float(e.get("Vz", 0)))
            ee = dict(N=float(e.get("N", 0)), V=V, My=float(e.get("My", e.get("M", 0))), Mz=float(e.get("Mz", 0)),
                      sismo=bool(e.get("sismo", False)))
        except (TypeError, ValueError):
            raise ErroEntrada(f"esforcos[{i}]: números N, V, M esperados", "esforcos") from None
        casos.append(dict(barra=None, ponta=None, no=None, combinacao=str(e.get("nome") or f"caso {i + 1}"), esf=ee, secao=sec))
    return casos


def _placa_pior(dados, casos, primeiro=None, passo=None):
    """Verifica a placa em todos os casos, nos dois eixos (placa_base.casos_biaxiais). → resumo com o governante (maior
    taxa), o pior de cada passo, uma linha por caso (eixo pior) e o pior de cada membro. Triagem: com `primeiro`, esse
    caso vai antes e, se reprovar (o `passo`, ou a placa inteira), para ali com parcial=True."""
    pb = _pb()
    ordem = ([primeiro] if primeiro is not None else []) + [i for i in range(len(casos)) if i != primeiro]
    gov, passos, linhas, membros, eixos = None, {}, [], {}, {}
    ok_total = True
    for k, i in enumerate(ordem):
        cs = casos[i]
        pior_caso, ok_c = None, True
        for eixo, d, e in pb.casos_biaxiais(dados, cs["esf"]):
            try:
                r = pb.verificar_placa_base(d, e, cs["secao"])
            except (ValueError, KeyError, TypeError) as ex:
                raise ErroEntrada(str(ex.args[0] if ex.args else ex), "dados") from ex
            r["eixo"] = eixo
            t = r["taxa_max"] if math.isfinite(r["taxa_max"]) else math.inf
            ok = bool(r["ok"])
            ok_c = ok_c and ok
            eixos[eixo] = max(eixos.get(eixo, 0.0), t)
            onde = dict(barra=cs["barra"], ponta=cs["ponta"], no=cs["no"], combinacao=cs["combinacao"], eixo=eixo, caso=i)
            grupos = {}
            for v in r["verificacoes"]:
                g = grupos.setdefault(_passo_placa(v["nome"]), [0.0, True, ""])
                tv = v["taxa"] if math.isfinite(v["taxa"]) else math.inf
                if (v["unidade"] != "m" or v.get("criterio_resistente")) and tv > g[0]:
                    g[0], g[2] = tv, v["nome"]
                if tv > 1 and g[1]:
                    g[1], g[2] = False, g[2] if g[0] > 1 else v["nome"]
            grupos.setdefault("fixacao", [0.0, True, ""])[1] &= bool(r["fixacao"].get("ChOk", True))
            grupos.setdefault("chapa", [0.0, True, ""])[1] &= bool(r["chapa"]["ok"])
            sd = r.get("solda")
            g = grupos.setdefault("solda", [0.0, True, ""])
            if sd is None or not sd["ok"]:
                g[1], g[2] = False, g[2] or ("solda não verificada" if sd is None else "solda")
            for p, (tp, okp, nome) in grupos.items():   # local do passo: o pior caso (reprovado antes, depois a taxa)
                x = passos.setdefault(p, dict(taxa=-1.0, ok=True, _k=(False, -1.0)))
                if (not okp, tp) > x["_k"]:
                    # cada passo com os números da SUA pior combinação (Bruno, origin/main be0a9e2): trocar a fixação
                    # pode mudar qual combinação manda na chapa ou na solda, e a janela mostra cada uma
                    x.update(onde, criterio=nome, esforcos=dict(N=e["N"], V=e["V"], M=e["M"]), _k=(not okp, tp),
                             verificacoes=[v for v in r["verificacoes"] if _passo_placa(v["nome"]) == p])
                    if p == "solda":
                        x["solda"] = sd
                x["taxa"], x["ok"] = max(x["taxa"], tp), x["ok"] and okp
            passos["chapa"]["t_nec"] = max(passos["chapa"].get("t_nec", 0.0), r["chapa"]["t_cbca"])
            if sd is not None:
                passos["solda"]["h_max"] = max(passos["solda"].get("h_max", 0.0), sd["h"])
            if pior_caso is None or t > pior_caso[0]:
                pior_caso = (t, ok, r, e, onde)
            if gov is None or t > gov[0]:
                gov = (t, ok, r, e, onde)
        t, _, r, e, onde = pior_caso
        ok = ok_c
        ok_total = ok_total and ok
        linhas.append(dict(onde, taxa=t, ok=ok, N=cs["esf"]["N"], V=cs["esf"]["V"], My=cs["esf"]["My"], Mz=cs["esf"]["Mz"]))
        x = membros.setdefault((cs["barra"], cs["ponta"]), dict(barra=cs["barra"], ponta=cs["ponta"], no=cs["no"], taxa=-1.0, ok=True))
        x["ok"] = x["ok"] and ok
        if t > x["taxa"]:
            x.update(combinacao=cs["combinacao"], eixo=onde["eixo"], taxa=t)
        if k == 0 and primeiro is not None and not (passos.get(passo, {}).get("ok", True) if passo else ok):
            break
    else:
        k = None
    t, ok, r, e, onde = gov
    for x in passos.values():
        x.pop("_k", None)
    return dict(ok=ok_total and k is None, parcial=k is not None, taxa_max=t,
                governante=dict(onde, N=e["N"], V=e["V"], M=e["M"]), resultado=r, passos=passos,
                combinacoes=sorted(linhas, key=lambda x: -x["taxa"]), membros=list(membros.values()),
                biaxial=dict(eixos=eixos, nota=_NOTA_BIAXIAL))


def placa_base_verificar(modelo, calculo, corpo):
    """Placa de base pelo método NBR (aco/placa_base.py) na PIOR combinação ELU: corpo {dados, membros [{barra, ponta}]}
    (esforços do cálculo, todas as combinações ELU de todos os membros) ou {dados, esforcos, secao}. Biaxial: My no eixo
    forte e Mz no eixo fraco, separadamente (nota em `biaxial`). `variantes` [dados...]: resumo de cada uma (triagem
    pelo caso governante da placa de `dados`; `triagem` = passo que manda parar: fixacao, chapa ou solda). → {ok, taxa_max, governante, resultado, passos, combinacoes, membros,
    biaxial[, variantes]}. Cada passo (fixacao, chapa, solda) traz a sua pior combinação (`combinacao`, `eixo`,
    `esforcos`) e as `verificacoes` dele nessa combinação (solda: também o resultado `solda`, com `seguranca`
    n = 0,577·FEXX/τ informativo); `resultado` é o da combinação governante da placa inteira."""
    c = corpo or {}
    casos = _casos_placa(modelo, calculo, c)
    out = _placa_pior(c.get("dados") or {}, casos)
    if c.get("variantes") is not None:
        gi = out["governante"]["caso"]
        out["variantes"] = []
        for v in c["variantes"]:
            s = _placa_pior(v or {}, casos, gi, c.get("triagem"))
            out["variantes"].append({k: s[k] for k in ("ok", "parcial", "taxa_max", "passos", "governante")})
    return _puro(out)


def placa_base_gravar(modelo, corpo):
    """Grava a placa como projeto `placa_base` do grupo de ligações das bases (fonte única: modelo.ligacoes). corpo
    {membros [{no, barra, ponta}], dados, grupo_inteiro}. grupo_inteiro: o projeto vale para o grupo do 1º membro e os
    outros membros entram nele; senão, se os membros não são exatamente um grupo, vão para um grupo novo (dividir).
    Identifica os grupos antes se algum membro ainda não tem grupo. → {grupo}"""
    from .aco import ligacoes_modelo as lm
    c = corpo or {}
    sel = [dict(no=str(x["no"]), barra=str(x["barra"]), ponta=str(x["ponta"])) for x in c.get("membros") or []]
    if not sel:
        raise ErroEntrada("membros: lista não vazia de {no, barra, ponta}", "membros")

    def dono():
        return {lm._chave(mb): gid for gid, g in lm._grupos(modelo).items() for mb in g["membros"]}
    if any(lm._chave(x) not in dono() for x in sel):
        _lm("identificar", modelo)
    d = dono()
    falta = [x for x in sel if lm._chave(x) not in d]
    if falta:
        raise ErroEntrada(f"membros sem ligação de base no modelo: {falta}", "membros")
    gid = d[lm._chave(sel[0])]
    grupo = lm._grupos(modelo)[gid]
    chaves = {lm._chave(x) for x in sel}
    if c.get("grupo_inteiro"):
        resto = [x for x in sel if d[lm._chave(x)] != gid]
        if resto:
            _lm("editar", modelo, dict(acao="mover", membros=resto, destino=gid))
    elif chaves != {lm._chave(x) for x in grupo["membros"]}:
        gid = _lm("editar", modelo, dict(acao="mover", membros=sel, destino=None))["grupo"]
    _lm("editar", modelo, dict(acao="projeto", grupo=gid, tipo_ligacao="placa_base", dados=dict(c.get("dados") or {})))
    return dict(grupo=gid)


def placa_base_dimensionar(modelo, calculo, corpo):
    """Menor fixação que passa em todos os casos (preferencia 'leve': menor diâmetro/classe; 'furo': menor furo), depois
    a chapa comercial ≥ t_CBCA e a perna de solda ≥ a maior mínima. Chumbador de catálogo (dados.chumbador preenchido):
    mesmo tipo (mecânico/químico), arranjos 2×2, 2×3, 3×3 (≥ o atual). Pré-concretado (chumbador vazio): diâmetro e
    classe da tabela métrica. → {achou, descricao, dados, verificacao (como placa_base_verificar)}"""
    pb = _pb()
    c = corpo or {}
    casos = _casos_placa(modelo, calculo, c)
    d0 = dict(c.get("dados") or {})
    pad = {x["nome"]: x["padrao"] for x in pb.esquema_placa_base()}
    gi = _placa_pior(d0, casos)["governante"]["caso"]
    furo = c.get("preferencia") == "furo"
    f0, q0 = int(d0.get("fileiras", pad["fileiras"])), int(d0.get("por_fileira", pad["por_fileira"]))
    cands = []
    if d0.get("chumbador"):
        atual = pb.achar_chumbador(d0["chumbador"])
        itens = [x for x in pb.carregar_chumbadores() if atual is None or x["quimico"] == atual["quimico"]]
        itens.sort(key=lambda x: ((x["furo_mm"] or 0) if furo else 0, x["rosca_mm"], x["hef_mm"]))
        arrs = [(nf, nq) for nf, nq in ((2, 2), (2, 3), (3, 3)) if nf * nq >= f0 * q0] or [(f0, q0)]
        pares = [(x, a) for x in itens for a in arrs] if furo else [(x, a) for a in arrs for x in itens]
        for x, (nf, nq) in pares:
            cands.append(dict(d0, chumbador=x["codigo"] or x["nome"], fileiras=max(nf, f0), por_fileira=max(nq, q0)))
    else:
        cl = str(d0.get("classe", pad["classe"]))
        i0 = pb.CLASSES.index(cl) if cl in pb.CLASSES else 0
        pares = ([(dm, k) for dm in pb.DIAMETROS_MM for k in pb.CLASSES[i0:]] if furo
                 else [(dm, k) for k in pb.CLASSES[i0:] for dm in pb.DIAMETROS_MM])
        cands = [dict(d0, diametro_mm=dm, classe=k) for dm, k in pares]
    escolhido = None
    for cand in cands:
        try:
            s = _placa_pior(cand, casos, gi, "fixacao")
        except ErroEntrada:
            continue
        if not s["parcial"] and s["passos"]["fixacao"]["ok"]:
            escolhido = (cand, s)
            break
    if escolhido is None:
        return dict(achou=False, dados=None, descricao="Nenhuma fixação da tabela passa em todas as combinações nesta placa: "
                    "aumente a placa ou o arranjo.", verificacao=placa_base_verificar(modelo, calculo, dict(c, dados=d0)))
    cand, s = escolhido
    t = s["passos"]["chapa"]["t_nec"]
    cand["esp"] = next((x for x in pb.CHAPAS if x >= t - 1e-9), pb.CHAPAS[-1])
    cand["solda"] = 0
    ps = _placa_pior(cand, casos)["passos"].get("solda", {})
    h = ps.get("h_max", 0.0)
    if h > 0:
        cand["solda"] = h
        sd = _placa_pior(cand, casos)["passos"]["solda"]
        if not sd["ok"] and sd.get("caso") is not None:   # perna > parede etc.: eletrodo mais forte com perna que cabe
            cs = casos[sd["caso"]]
            eixo, de, e = next(x for x in pb.casos_biaxiais(cand, cs["esf"]) if x[0] == sd["eixo"])
            dg = pb.diagnosticar_solda(cs["secao"], de, dict(N=e["N_barra"], V=e["V_barra"], M=e["M_barra"]))
            if dg["perna"] > 0:
                cand.update(eletrodo=dg["eletrodo"], solda=dg["perna"])
                h = dg["perna"]
    v = placa_base_verificar(modelo, calculo, dict(c, dados=cand))
    n = v["resultado"]["geometria"]["coordenadas"]
    fix = (f"{len(n)} × {cand['chumbador']}" if cand.get("chumbador")
           else f"{len(n)} × Ø{cand['diametro_mm']:g} mm classe {cand['classe']} (pré-concretado)")
    mm = lambda x: f"{x * 1000:g}".replace(".", ",")
    desc = f"{fix} · chapa {mm(cand['esp'])} mm" + (f" · solda {mm(h)} mm {cand.get('eletrodo', 'E70')}" if h > 0 else "")
    return dict(achou=True, dados=_puro(cand), verificacao=v, descricao=desc)


def _norm(s):
    return str(s).upper().replace(" ", "").replace(",", ".")


def catalogo_arvore():
    """Catálogo em árvore: [{nivel1, familias:[{nome, series:[{nome, perfis:[nome]}]}]}]."""
    return _sec.arvore_catalogo()


def catalogo_perfis(filtro="", familia=None):
    """#26 Resumo dos perfis do catálogo (W/HP Gerdau e PFF NBR 6355). `filtro`: trecho do nome."""
    if familia is not None and familia not in {p["familia"] for p in _sec.lista_catalogo()}:
        raise ErroEntrada("familia: use uma das famílias de GET /api/catalogo?formato=arvore (W, HP, Ue, U, IPE...)", "familia")
    f = _norm(filtro or "")
    return [p for p in _sec.lista_catalogo(familia) if f in _norm(p["nome"])]


def perfil(nome):
    """#26 Propriedades de um perfil: unidades usuais com sufixo (notação da NBR: x = eixo forte) e a
    seção explícita em m (`secao`, pronta para `adicionar(modelo, "secao", ...)`)."""
    try:
        s = _sec.catalogo(nome)
    except KeyError:
        raise ErroNaoEncontrado("perfil", nome, [p["nome"] for p in _sec.lista_catalogo()]) from None
    p = s.props
    cm = lambda k, e: p[k] * 100 ** e if k in p else None  # noqa: E731
    fam = next((x["familia"] for x in _sec.lista_catalogo() if _norm(x["nome"]) == _norm(s.nome)), None)
    return _puro({
        "nome": s.nome, "familia": fam, "massa_kg_m": p.get("massa", s.A * 7850), "A_cm2": s.A * 1e4,
        "d_mm": p["h"] * 1e3 if "h" in p else None, "bf_mm": p["b"] * 1e3 if "b" in p else None,
        "tw_mm": p["tw"] * 1e3 if "tw" in p else None, "tf_mm": p["tf"] * 1e3 if "tf" in p else None,
        "t_mm": p["t"] * 1e3 if "t" in p else None,
        "Ix_cm4": cm("Ix", 4), "Iy_cm4": cm("Iy", 4), "Wx_cm3": cm("Wx", 3), "Wy_cm3": cm("Wy", 3),
        "Zx_cm3": cm("Zx", 3), "Zy_cm3": cm("Zy", 3), "J_cm4": cm("J", 4), "Cw_cm6": cm("Cw", 6),
        "rx_cm": cm("rx", 1), "ry_cm": cm("ry", 1),
        "secao": {"nome": s.nome, "A": s.A, "Iy": s.Iy, "Iz": s.Iz, "J": s.J, "props": s.props}})


def materiais_disponiveis():
    """#27 Aços, faixa de fck do concreto e armaduras."""
    return {"aco": [{"nome": n, "fy_MPa": fy, "fu_MPa": fu} for n, (fy, fu) in _mat.ACOS.items()],
            "concreto": {"fck_MPa_min": 20, "fck_MPa_max": 90},
            "armadura": [{"nome": n, "fyk_MPa": fyk} for n, (fyk, _) in _mat.ARMADURAS.items()]}


def normas_implementadas():
    """#28 `COBERTURA` de todos os módulos de norma/ações (item, verificação, limitação)."""
    import importlib
    out = []
    for nome in ("acoes.combinacoes", "acoes.vento", "acoes.vento_barras", "acoes.nbr8400", "acoes.sismo", "acoes.cargas", "aco.nbr8800",
                 "aco.nbr14762", "aco.ligacoes", "concreto.nbr6118"):
        try:
            mod = importlib.import_module(f"{__package__}.{nome}")
        except ImportError:
            continue
        cob = getattr(mod, "COBERTURA", None)
        if cob:
            out += [{**c, "modulo": nome} for c in cob]
        else:
            ed = getattr(mod, "EDICAO", "")
            g = re.match(r"(NBR \d+):(\d{4})", ed)
            out.append({"norma": g.group(1) if g else nome, "edicao": g.group(2) if g else "", "item": "",
                        "verificacao": "", "limitacao": "módulo sem cobertura declarada", "modulo": nome})
    return _puro(out)


# ================================================================ 29-31: relatório, caminhos, exemplos

def _relatorio_mod():
    try:
        from . import relatorio
    except ImportError as e:
        raise NotImplementedError("relatorio.py ainda não disponível (ficha 010)") from e
    return relatorio


def relatorio_html(modelo, calculo, empresa=""):
    """#29 Relatório HTML imprimível (string). `empresa` vai no carimbo (vazio = em branco)."""
    return _relatorio_mod().html(modelo.to_dict(), calculo.to_dict(completo=False), empresa)


def gerar_relatorio(modelo, calculo, formato="html", caminho=None, empresa=""):
    """#29 Grava o relatório (`formato` ∈ html, md, json) em `caminho` (cria a pasta). → {caminho, bytes}."""
    if formato not in ("html", "md", "json"):
        raise ErroEntrada(f"formato '{formato}' inválido; use html, md ou json", "formato")
    if not caminho:
        raise ErroEntrada("caminho obrigatório", "caminho")
    if formato == "json":
        texto = json.dumps(calculo.to_dict(completo=False), ensure_ascii=False, indent=1)
    elif formato == "html":
        texto = relatorio_html(modelo, calculo, empresa)
    else:
        texto = _relatorio_mod().markdown(modelo.to_dict(), calculo.to_dict(completo=False))
    return _gravar(caminho, texto)


def caminho_seguro(base, relativo):
    """#30 Caminho resolvido dentro de `base`. Recusa `..`, unidade (C:\\), UNC, absoluto fora da base e
    links que saem dela."""
    rel = str(relativo)
    if not rel.strip() or "\x00" in rel:
        raise ErroEntrada("caminho vazio ou inválido", "caminho")
    # UNC/dispositivo (duas barras iniciais: \\host\x, //host/x, \\?\, \\.\): recusa ANTES de qualquer resolve,
    # que no Windows abre o caminho e autentica (NTLM) num servidor SMB remoto
    if re.match(r"[\\/]{2}", rel.lstrip()):
        raise ErroEntrada("caminho de rede (UNC) ou de dispositivo fora da pasta de trabalho não é permitido", "caminho")
    if re.match(r"^[A-Za-z]:", rel) and not Path(rel).is_absolute():  # "C:x": relativo à unidade, não à base
        raise ErroEntrada(f"caminho '{rel}' fora da pasta de trabalho", "caminho")
    if ".." in re.split(r"[\\/]", rel):
        raise ErroEntrada(f"caminho '{rel}' contém '..'", "caminho")
    raiz = Path(base).resolve()
    alvo = (raiz / rel).resolve()
    if alvo != raiz and raiz not in alvo.parents:
        raise ErroEntrada(f"caminho '{rel}' fora da pasta de trabalho", "caminho")
    return alvo


def exemplos():
    """#31 Modelos de `software/exemplos/*.json`: [{nome, descricao}] (descrição = `nome` do modelo)."""
    out = []
    for p in sorted(EXEMPLOS.glob("*.json")):
        try:
            d = json.loads(p.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        out.append({"nome": p.stem, "descricao": str(d.get("descricao") or d.get("nome") or p.stem)})
    return out


def abrir_exemplo(nome):
    """#31 Carrega um exemplo pelo nome (nome do arquivo sem `.json`)."""
    validos = [e["nome"] for e in exemplos()]
    if nome not in validos:
        raise ErroNaoEncontrado("exemplo", nome, validos)
    return carregar(EXEMPLOS / f"{nome}.json")


# ================================================================ 32-33: obras do CYPE 3D (.ed3)

PASTA_CYPE = Path(r"C:\CYPE Ingenieros\Projetos")  # leitura permitida (nunca se grava lá)
LIMITE_ED3 = 20 * 1024 * 1024


def _caminho_ed3(origem, pasta):
    """.ed3 dentro da pasta de trabalho (relativo ou absoluto) ou, só absoluto, dentro de `PASTA_CYPE`."""
    rel = str(origem)
    if not rel.lower().endswith(".ed3"):
        raise ErroEntrada(f"'{rel}' não é um arquivo .ed3", "caminho")
    try:
        return caminho_seguro(pasta or Path.cwd(), rel)
    except ErroEntrada as e:
        if Path(rel).is_absolute():
            try:
                return caminho_seguro(PASTA_CYPE, rel)
            except ErroEntrada:
                pass
        raise ErroEntrada(f"{e.erros[0]['mensagem']} (permitidos: a pasta de trabalho e {PASTA_CYPE})",
                          "caminho") from None


@contextlib.contextmanager
def _arquivo_ed3(origem, pasta):
    """Entrega (caminho legível, campo do erro). Bytes de upload vão para uma pasta temporária própria."""
    if isinstance(origem, (bytes, bytearray, memoryview)):
        dados = bytes(origem)
        if not dados or len(dados) > LIMITE_ED3:
            raise ErroEntrada("arquivo vazio" if not dados else f"arquivo acima de {LIMITE_ED3 // 2**20} MB",
                              "conteudo")
        with tempfile.TemporaryDirectory(prefix="prossen_ed3_") as t:
            p = Path(t) / "obra.ed3"
            p.write_bytes(dados)
            yield p, "conteudo"
        return
    p = _caminho_ed3(origem, pasta)
    if not p.is_file():
        raise ErroEntrada(f"arquivo não encontrado: {origem}", "caminho")
    if p.stat().st_size > LIMITE_ED3:
        raise ErroEntrada(f"arquivo acima de {LIMITE_ED3 // 2**20} MB", "caminho")
    yield p, "caminho"


def _ler_ed3(leitor, p, campo):
    try:
        return leitor(p)
    except _ed3.ErroFormato as e:
        raise ErroEntrada(str(e), campo) from None
    except (LookupError, ArithmeticError, struct.error, UnicodeError, TypeError, ValueError) as e:
        log.exception("falha ao ler .ed3 (arquivo corrompido ou defeito do importador)")
        raise ErroEntrada(f"arquivo .ed3 corrompido ou truncado ({type(e).__name__})", campo) from None


def importar_cype(origem, pasta=None, nome=None):
    """#32 Importa uma obra do CYPE 3D 2016 (.ed3) → `Modelo` (kN, m) com `modelo.avisos` (lista de textos do que
    ficou de fora). `origem`: caminho `.ed3` na pasta de trabalho `pasta` (relativo ou absoluto) ou absoluto em
    `PASTA_CYPE` (só leitura), OU bytes do arquivo (upload; `nome` = nome original). O adaptador o torna o modelo
    atual (como `carregar`)."""
    with _arquivo_ed3(origem, pasta) as (p, campo):
        m = _ler_ed3(_ed3.ler_ed3, p, campo)
    if nome and m.nome == "obra":
        m.nome = Path(str(nome).replace("\\", "/")).stem or m.nome
    return m


def _dif(cype, nosso):
    return None if cype is None or nosso is None or abs(cype) < 1e-12 else (nosso - cype) / abs(cype) * 100


def _linhas_hipotese(modelo, ref, k, caso):
    """Linhas {grandeza, unidade, cype, nosso} de uma hipótese: só grandezas globais, que não dependem da
    convenção de eixos locais de cada programa (mesmo critério de testes/regressao/test_reg_benchmark_cype.py).
    Reação: o CYPE grava a ação no apoio, oposta à do solver."""
    res = _sol.analisar(modelo, caso)
    nos = [n for n in ref["nos"] if n in res.deslocamentos]
    ap = [n for n in nos if n in res.reacoes]
    nrm = lambda v: float(np.linalg.norm(np.asarray(v[:3], float)))  # noqa: E731
    Rc = np.sum([ref["nos"][n]["reacao"][k][:3] for n in ap], axis=0) if ap else np.zeros(3)
    Rn = -np.sum([res.reacoes[n][:3] for n in ap], axis=0) if ap else np.zeros(3)
    linhas = [("desloc_max", "mm", 1000 * max((nrm(ref["nos"][n]["desloc"][k]) for n in nos), default=0.0),
               1000 * max((nrm(res.deslocamentos[n]) for n in nos), default=0.0))]
    linhas += [("reacao_total_" + e, "kN", float(Rc[i]), float(Rn[i])) for i, e in enumerate(("Fx", "Fy", "Fz"))]
    linhas.append(("reacao_max_apoio", "kN", max((nrm(ref["nos"][n]["reacao"][k]) for n in ap), default=0.0),
                   max((nrm(res.reacoes[n]) for n in ap), default=0.0)))
    bs = [b for b in ref["barras"] if b in res.esforcos]
    pc = [p for b in bs for p in ref["barras"][b]["esforcos"][k]]  # N, Vy, Vz, T, My, Mz
    pn = [p[1:7] for b in bs for p in res.esforcos[b]]  # (x, N, Vy, Vz, T, My, Mz)
    for g, u, f in (("N_max", "kN", lambda p: abs(p[0])), ("V_max", "kN", lambda p: math.hypot(p[1], p[2])),
                    ("T_max", "kN·m", lambda p: abs(p[3])), ("M_max", "kN·m", lambda p: math.hypot(p[4], p[5]))):
        linhas.append((g, u, max(map(f, pc), default=0.0), max(map(f, pn), default=0.0)))
    return [{"hipotese": caso, "grandeza": g, "unidade": u, "cype": c, "nosso": n, "dif_pct": _dif(c, n)}
            for g, u, c, n in linhas]


def comparar_cype(origem, pasta=None, nome=None, taxas=True):
    """#33 Conferência com o CYPE: calcula a obra com o nosso software e compara com o que o CYPE gravou no
    `.ed3` (`origem` como em `importar_cype`). → {arquivo, valido, hipoteses, tabela: [{hipotese, grandeza,
    unidade, cype, nosso, dif_pct}], taxas: [{barra, cype, nosso, dif_pct, criterio, ok}], nota_taxas, avisos}.
    `valido` = o CYPE não alterou a obra depois de calcular (senão a referência não vale). Taxas (`taxas=True`):
    maior η da barra no CYPE x maior taxa de `calcular` (NBR 8800/8681 aqui; normas e combinações da obra lá)."""
    with _arquivo_ed3(origem, pasta) as (p, campo):
        modelo = _ler_ed3(_ed3.ler_ed3, p, campo)
        ref = _ler_ed3(_ed3.ler_resultados_ed3, p, campo)
        arquivo = str(nome) if nome else p.name
    if not ref["calculado"]:
        raise ErroEntrada("a obra não tem resultados gravados pelo CYPE (calcule-a no CYPE 3D e salve)", campo)
    avisos = list(dict.fromkeys(modelo.avisos + ref["avisos"]))
    tabela = []
    for k, caso in enumerate(ref["hipoteses"]):
        if caso in modelo.casos:
            tabela += _linhas_hipotese(modelo, ref, k, caso)
        else:
            avisos.append(f"hipótese '{caso}' do CYPE sem caso correspondente no modelo importado: não comparada.")
    linhas = []
    if taxas:
        try:
            calc = calcular(de_dict(para_dict(modelo)))  # cópia: o cálculo gera ações e não pode tocar no modelo
        except Exception as e:  # noqa: BLE001 — a comparação de esforços vale mesmo sem as taxas
            log.exception("comparar_cype: calcular falhou")
            avisos.append(f"taxas de aproveitamento não calculadas: {e}")
        else:
            por = {l["barra"]: l for l in calc.verificacao}
            for b in modelo.barras:
                el = ref["aproveitamento"].get(b)
                c = max((x[1] for x in el), default=None) if el else None
                n = por.get(b, {}).get("taxa")
                if c is not None or n is not None:
                    linhas.append({"barra": b, "cype": c, "nosso": n, "dif_pct": _dif(c, n),
                                   "criterio": por.get(b, {}).get("criterio"), "ok": por.get(b, {}).get("ok")})
    return _puro({"arquivo": arquivo, "valido": ref["valido"], "hipoteses": ref["hipoteses"], "tabela": tabela,
                  "taxas": linhas,
                  "nota_taxas": "taxa do CYPE = maior η da barra nas combinações e normas da obra; a nossa vem de "
                                "calcular (NBR 8800/8681, perfis e esbeltez do modelo importado): compare a ordem "
                                "de grandeza, não o valor.",
                  "avisos": avisos})
