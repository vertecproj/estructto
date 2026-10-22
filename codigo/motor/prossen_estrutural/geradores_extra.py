"""Geradores de treliças, contraventamentos e terças (docs/v3/TRELICAS.md).

Entram pelo mesmo `gerador.gerar_estrutura(modelo, dados)` (e daí `projeto.gerar_estrutura`, rota
`POST /api/gerar-estrutura` e ferramenta MCP `estrutura_gerar`). Tudo ou nada: valida, monta numa
cópia de trabalho e só então muta o modelo (uma operação = um desfazer no servidor).

Tipos (`dados.tipo`):
- `trelica_plana`: modelo pratt | howe | warren | warren_v | k | fink; banzo paralelo | uma_agua |
  duas_aguas | arco; vao, altura (extremos / esquerda), altura2 (cumeeira / direita / flecha do arco),
  paineis; secoes {banzo | banzo_superior + banzo_inferior, diagonal, montante?}; rotulas diagonais
  (padrão: diagonais e montantes só axiais) | todas (treliça ideal) | nenhuma; apoios [esq, dir] cada um
  rotula | movel | engaste | livre (padrão [rotula, movel]); origem [x, y, z], eixo x | y; ou
  entre [nóA, nóB] (banzo inferior liga dois nós existentes, vão = distância em planta); travar_plano.
- `trelicas_serie`: o mesmo + n (quantidade), espacamento (m, na horizontal perpendicular ao vão) e
  secoes.terca opcional (terças rotuladas ligando os nós do banzo superior de treliças vizinhas).
- `contraventamento`: modelo x | diagonal | k | v | lambda | mao_francesa entre dois pórticos/num vão:
  painel [n1, n2, n3, n4] (n1-n2 de baixo, n4 sobre n1) ou barras [bA, bB] (dois pilares, duas vigas
  ou duas tesouras: o painel é formado pelas pontas); divisoes (x e diagonal), distancia (m, mão-francesa),
  secoes {contraventamento, escora?}. Todas as diagonais são `trelica` (o motor não tem barra só de
  tração: com X, as duas diagonais trabalham também à compressão).
- `tercas`: barras [tesouras/vigas de cobertura; ordenadas ao longo do prédio], espacamento (máx., m, ao longo da água),
  secoes {terca, tirante?}, tirantes (linhas por vão, padrão 0).

Pontos novos sobre barras existentes (meio de viga no V/Λ/K, mão-francesa, terça sobre a tesoura) dividem
a barra (mesma seção, material, giro e params; rótulas de ponta só no trecho da ponta; cargas
distribuídas reinterpoladas, pontuais no trecho onde caem). Ponto a 1 mm ou menos de um nó existente
usa o nó. Barra que repetiria outra (mesmos nós) não é criada.
"""
from __future__ import annotations

import math

from . import gerador as _g
from .modelo import Barra, CargaDistribuida, CargaPontual, CasoCarga, No

TIPOS = ("trelica_plana", "trelicas_serie", "contraventamento", "tercas")
MODELOS_TRELICA = ("pratt", "howe", "warren", "warren_v", "k", "fink")
BANZOS = ("paralelo", "uma_agua", "duas_aguas", "arco")
MODELOS_CONTRA = ("x", "diagonal", "k", "v", "lambda", "mao_francesa")
_TREL = {"tipo", "material", "secoes", "prefixo", "modelo", "banzo", "vao", "altura", "altura2", "paineis",
         "rotulas", "apoios", "origem", "eixo", "entre", "travar_plano"}
_CHAVES = {"trelica_plana": _TREL, "trelicas_serie": (_TREL - {"entre"}) | {"n", "espacamento"},
           "contraventamento": {"tipo", "material", "secoes", "prefixo", "modelo", "painel", "barras", "divisoes",
                                "distancia"},
           "tercas": {"tipo", "material", "secoes", "prefixo", "barras", "espacamento", "tirantes"}}
_SECOES = {"trelica_plana": ("banzo", "banzo_superior", "banzo_inferior", "diagonal", "montante"),
           "trelicas_serie": ("banzo", "banzo_superior", "banzo_inferior", "diagonal", "montante", "terca"),
           "contraventamento": ("contraventamento", "escora"), "tercas": ("terca", "tirante")}
_PREF = {"trelica_plana": "TR", "trelicas_serie": "TS", "contraventamento": "CV", "tercas": "TC"}
TOL = 1e-3  # m: ponto a 1 mm de um nó usa o nó
_LIVRE = (False,) * 6


# ---------------------------------------------------------------- vetores

def _sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def _mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def _dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def _norma(a): return math.sqrt(_dot(a, a))
def _unit(a): n = _norma(a); return _mul(a, 1 / n)


def rot_normal(ex, normal):
    """Giro `rot` (rad) da barra de direção `ex` para o eixo local z ficar no plano de `normal`
    (mesma regra de solver._rotacao: rot 0 → y = Z × x, z = x × y)."""
    ex = _unit(ex)
    if math.hypot(ex[0], ex[1]) < 1e-9:
        ey = (0.0, 1.0, 0.0)
    else:
        ey = _unit(_cross((0.0, 0.0, 1.0), ex))
    ez = _cross(ex, ey)
    return math.atan2(-_dot(normal, ey), _dot(normal, ez))


# ---------------------------------------------------------------- trabalho (cópia do que muda)

class _Obra:
    """Nós/barras novos, barras divididas e cargas trocadas; nada toca no modelo até `aplicar`."""

    def __init__(self, modelo, prefixo):
        self.m, self.p = modelo, prefixo
        self.nos, self.barras, self.removidas, self.avisos = {}, {}, [], []
        self.cargas_novas, self.cargas_removidas = [], set()
        self.pares = {frozenset((b.ni, b.nj)) for b in modelo.barras.values()}

    def xyz(self, nid):
        n = self.nos.get(nid) or self.m.nos[nid]
        return (n.x, n.y, n.z)

    def achar_no(self, p):
        for d in (self.nos, self.m.nos):
            for n in d.values():
                if abs(n.x - p[0]) <= TOL and abs(n.y - p[1]) <= TOL and abs(n.z - p[2]) <= TOL:
                    return n.id
        return None

    def no(self, nome, p, apoio=_LIVRE):
        """Nó em p (reusa o existente a 1 mm); devolve o id."""
        ex = self.achar_no(p)
        if ex is not None:
            return ex
        nid = self.p + nome
        self.nos[nid] = No(nid, round(p[0], 9), round(p[1], 9), round(p[2], 9), tuple(apoio))
        return nid

    def barra(self, nome, ni, nj, sec, mat, rot=0.0, **params):
        """Barra nova; None se ni == nj ou se já existe barra entre os dois nós."""
        par = frozenset((ni, nj))
        if ni == nj or par in self.pares:
            return None
        self.pares.add(par)
        bid = self.p + nome
        self.barras[bid] = Barra(bid, ni, nj, sec.nome, mat.nome, rot, _g._tipo_barra(sec, mat),
                                 {"papel": "viga", **params})
        return bid

    # --- divisão de barra existente
    def barra_contendo(self, p):
        """(barra, t) de uma barra (existente ou nova) que passa por p no interior, ou None."""
        for d in (self.barras, self.m.barras):
            for b in d.values():
                if b.id in self.removidas:
                    continue
                a, c = self.xyz(b.ni), self.xyz(b.nj)
                v = _sub(c, a)
                L2 = _dot(v, v)
                t = _dot(_sub(p, a), v) / L2
                if 0 < t < 1 and _norma(_sub(_add(a, _mul(v, t)), p)) <= TOL:
                    return b, t
        return None

    def dividir(self, b, pontos):
        """Divide a barra b nos nós `pontos` (ids, já criados). Cargas da barra vão para os trechos."""
        a, c = self.xyz(b.ni), self.xyz(b.nj)
        v = _sub(c, a)
        L = _norma(v)
        ts = sorted({_dot(_sub(self.xyz(n), a), v) / (L * L): n for n in pontos}.items())
        cadeia = [(0.0, b.ni)] + ts + [(1.0, b.nj)]
        if b.id in self.m.barras:
            self.removidas.append(b.id)
        self.pares.discard(frozenset((b.ni, b.nj)))
        novos = []
        base = {k: x for k, x in b.params.items() if k not in ("rotula_i", "rotula_j", "Lb")}
        for k in range(len(cadeia) - 1):
            (t0, n0), (t1, n1) = cadeia[k], cadeia[k + 1]
            params = dict(base)
            if k == 0 and b.params.get("rotula_i"):
                params["rotula_i"] = True
            if k == len(cadeia) - 2 and b.params.get("rotula_j"):
                params["rotula_j"] = True
            bid = f"{b.id}.{k + 1}"
            while bid in self.m.barras or bid in self.barras:
                bid += "'"
            self.barras[bid] = Barra(bid, n0, n1, b.secao, b.material, b.rot, b.tipo, params)
            self.pares.add(frozenset((n0, n1)))
            novos.append((bid, t0, t1))
        self.barras.pop(b.id, None)
        if b.id in self.m.barras:
            self._levar_cargas(b, novos, L)
        return novos

    def _levar_cargas(self, b, trechos, L):
        for c in self.m.cargas_distribuidas:
            if c.barra != b.id:
                continue
            self.cargas_removidas.add(id(c))
            q1, q2 = c.q, c.q2 or c.q
            for bid, t0, t1 in trechos:
                qa = tuple(x + (y - x) * t0 for x, y in zip(q1, q2))
                qb = tuple(x + (y - x) * t1 for x, y in zip(q1, q2))
                self.cargas_novas.append(CargaDistribuida(c.caso, bid, qa, "", None if c.q2 is None else qb))
        for c in self.m.cargas_pontuais:
            if c.barra != b.id:
                continue
            self.cargas_removidas.add(id(c))
            for bid, t0, t1 in trechos:
                if t0 * L - 1e-9 <= c.a <= t1 * L + 1e-9:
                    self.cargas_novas.append(CargaPontual(c.caso, bid, c.a - t0 * L, c.F))
                    break

    def ponto(self, nome, p, escora=None):
        """Nó em p; se cair no meio de uma barra, guarda para dividir (ver `fechar`)."""
        nid = self.achar_no(p)
        if nid is not None:
            return nid
        nid = self.no(nome, p)
        hb = self.barra_contendo(p)
        if hb is not None:
            self._pendentes.setdefault(hb[0].id, (hb[0], []))[1].append(nid)
        elif escora is not None:
            self._soltos.append((nid, escora))
        return nid

    _pendentes: dict
    _soltos: list

    def abrir_pontos(self):
        self._pendentes, self._soltos = {}, []

    def fechar_pontos(self):
        for b, pts in self._pendentes.values():
            self.dividir(b, pts)
        self._pendentes = {}


def _aplicar(modelo, obra, novas, novos):
    """Muta o modelo (tudo de uma vez) e devolve o resumo no formato do gerador."""
    for s in novas.values():
        modelo.add(s)
    for m in novos.values():
        modelo.add(m)
    for bid in obra.removidas:
        modelo.barras.pop(bid, None)
    if obra.cargas_removidas:
        modelo.cargas_distribuidas[:] = [c for c in modelo.cargas_distribuidas if id(c) not in obra.cargas_removidas]
        modelo.cargas_pontuais[:] = [c for c in modelo.cargas_pontuais if id(c) not in obra.cargas_removidas]
    for o in (*obra.nos.values(), *obra.barras.values()):
        modelo.add(o)
    for c in obra.cargas_novas:
        modelo.add(c)
    if "G1" not in modelo.casos:
        modelo.add(CasoCarga("G1", "G"))
    if "Q1" not in modelo.casos:
        modelo.add(CasoCarga("Q1", "Q"))
    modelo.liberar_pp_vazio()
    if modelo.peso_proprio is None:
        modelo.peso_proprio = "G1"
    if not obra.barras:
        obra.avisos.append("nenhuma barra criada: todas já existiam nesses nós")
    return {"nos_criados": len(obra.nos), "barras_criadas": len(obra.barras), "pavimentos_criados": 0,
            "barras_removidas": len(obra.removidas),
            "ids": {"nos": list(obra.nos), "barras": list(obra.barras), "pavimentos": [],
                    "barras_removidas": list(obra.removidas)},
            "avisos": [{"nivel": "aviso", "codigo": "gerador", "mensagem": a, "ref": None} for a in obra.avisos]}


# ---------------------------------------------------------------- geometria da treliça plana (local)

def altura_em(banzo, L, h1, h2, x):
    if banzo == "paralelo":
        return h1
    if banzo == "uma_agua":
        return h1 + (h2 - h1) * x / L
    if banzo == "duas_aguas":
        return h1 + (h2 - h1) * (1 - abs(2 * x / L - 1))
    return h1 + (h2 - h1) * 4 * x * (L - x) / (L * L)  # arco parabólico


def geometria_trelica(modelo, banzo, L, h1, h2, n):
    """Treliça no plano (s ao longo do vão, h para cima). → (nos {nome: (s, h)}, barras [(nome, a, b, grupo)],
    alias {nó do banzo superior: nó do inferior}).
    grupo ∈ banzo_superior, banzo_inferior, diagonal, montante. Nó do banzo superior com h ≈ 0 é o do inferior."""
    nos, barras, pares = {}, [], set()
    alias = {}

    def no(nome, s, h):
        nos[nome] = (s, h)
        return nome

    def ligar(pref, a, b, grupo):
        a, b = alias.get(a, a), alias.get(b, b)
        if a == b or frozenset((a, b)) in pares:
            return
        pares.add(frozenset((a, b)))
        k = sum(1 for x in barras if x[3] == grupo) + 1
        barras.append((f"{pref}{k}", a, b, grupo))

    if modelo == "fink":
        n = 4
        xi = [0, L / 3, 2 * L / 3, L]
        for i, x in enumerate(xi):
            no(f"I{i}", x, 0.0)
        for i in range(5):
            s = i * L / 4
            h = altura_em(banzo, L, h1, h2, s)
            if h <= TOL and i in (0, 4):
                alias[f"S{i}"] = "I0" if i == 0 else "I3"
            no(f"S{i}", s, h)
        for i in range(3):
            ligar("BI", f"I{i}", f"I{i+1}", "banzo_inferior")
        for i in range(4):
            ligar("BS", f"S{i}", f"S{i+1}", "banzo_superior")
        ligar("M", "I0", "S0", "montante")
        ligar("M", "I3", "S4", "montante")
        for a, b in (("S1", "I1"), ("I1", "S2"), ("S2", "I2"), ("I2", "S3")):
            ligar("D", a, b, "diagonal")
    elif modelo == "warren":
        for i in range(n + 1):
            no(f"I{i}", i * L / n, 0.0)
        for i in range(n):
            s = (i + 0.5) * L / n
            no(f"S{i}", s, altura_em(banzo, L, h1, h2, s))
        for i in range(n):
            ligar("BI", f"I{i}", f"I{i+1}", "banzo_inferior")
            if i < n - 1:
                ligar("BS", f"S{i}", f"S{i+1}", "banzo_superior")
            ligar("D", f"I{i}", f"S{i}", "diagonal")
            ligar("D", f"S{i}", f"I{i+1}", "diagonal")
    else:
        for i in range(n + 1):
            no(f"I{i}", i * L / n, 0.0)
        for i in range(n + 1):
            s = i * L / n
            h = altura_em(banzo, L, h1, h2, s)
            if h <= TOL:
                alias[f"S{i}"] = f"I{i}"
            no(f"S{i}", s, h)
        meio = {}
        for i in range(n):
            ligar("BI", f"I{i}", f"I{i+1}", "banzo_inferior")
            ligar("BS", f"S{i}", f"S{i+1}", "banzo_superior")
        for i in range(n):
            esq = (i + 0.5) <= n / 2
            if modelo == "pratt":
                a, b = (f"S{i}", f"I{i+1}") if esq else (f"I{i}", f"S{i+1}")
            elif modelo == "howe":
                a, b = (f"I{i}", f"S{i+1}") if esq else (f"S{i}", f"I{i+1}")
            elif modelo == "warren_v":
                a, b = (f"I{i}", f"S{i+1}") if i % 2 == 0 else (f"S{i}", f"I{i+1}")
            else:  # k: as duas diagonais saem do meio do montante interno para o nó de fora
                j, o = (i + 1, i) if esq else (i, i + 1)
                if f"S{j}" in alias:
                    ligar("D", f"S{o}", f"I{j}", "diagonal")
                    continue
                if j not in meio:
                    s = j * L / n
                    meio[j] = no(f"K{j}", s, altura_em(banzo, L, h1, h2, s) / 2)
                ligar("D", meio[j], f"S{o}", "diagonal")
                ligar("D", meio[j], f"I{o}", "diagonal")
                continue
            ligar("D", a, b, "diagonal")
        for i in range(n + 1):
            if i in meio:
                ligar("M", f"I{i}", meio[i], "montante")
                ligar("M", meio[i], f"S{i}", "montante")
            else:
                ligar("M", f"I{i}", f"S{i}", "montante")
    for k in alias:
        nos.pop(k, None)
    return nos, barras, alias


# ---------------------------------------------------------------- validação comum

def _prefixo(modelo, dados, tipo, erros):
    p = dados.get("prefixo")
    if p is not None:
        if not isinstance(p, str) or not p:
            erros.append(("prefixo", "texto não vazio"))
            return "X"
        return p
    k = 1
    while any(i.startswith(f"{_PREF[tipo]}{k}-") for i in (*modelo.nos, *modelo.barras)):
        k += 1
    return f"{_PREF[tipo]}{k}-"


def _inteiro(dados, campo, erros, padrao=None, minimo=1):
    v = dados.get(campo, padrao)
    if isinstance(v, bool) or not isinstance(v, int) or v < minimo:
        erros.append((campo, f"inteiro ≥ {minimo} obrigatório (recebido {v!r})"))
        return None
    return v


def _material(modelo, dados, erros, novos):
    if "material" in dados:
        return _g._resolve_material(modelo, dados["material"], erros, novos)
    if modelo.materiais:
        return next(iter(modelo.materiais.values()))
    erros.append(("material", "obrigatório (o modelo não tem material)"))
    return None


def _nos_existentes(modelo, ids, campo, n, erros):
    if not isinstance(ids, (list, tuple)) or len(ids) != n or not all(isinstance(i, str) for i in ids):
        erros.append((campo, f"lista de {n} ids de nós obrigatória (recebido {ids!r})"))
        return None
    falta = [i for i in ids if i not in modelo.nos]
    if falta:
        erros.append((campo, f"nós inexistentes: {', '.join(falta)}"))
        return None
    if len(set(ids)) != n:
        erros.append((campo, "nós repetidos"))
        return None
    return list(ids)


# ---------------------------------------------------------------- treliça plana / série

def _apoio(tipo, eixo, alinhado):
    if tipo == "livre":
        return _LIVRE
    if tipo == "engaste":
        return (True,) * 6
    if not alinhado:
        return (True, True, True, False, False, False) if tipo == "rotula" else (False, False, True, False, False, False)
    ap = [True, True, True, eixo == "x", eixo == "y", True]  # rótula: só o giro no plano livre
    if tipo == "movel":
        ap[0 if eixo == "x" else 1] = False
    return tuple(ap)


def _ler_trelica(modelo, dados, tipo, erros, novas):
    d = {}
    d["modelo"] = dados.get("modelo", "pratt")
    if d["modelo"] not in MODELOS_TRELICA:
        erros.append(("modelo", f"'{d['modelo']}' inválido; use {list(MODELOS_TRELICA)}"))
    d["banzo"] = dados.get("banzo", "paralelo")
    if d["banzo"] not in BANZOS:
        erros.append(("banzo", f"'{d['banzo']}' inválido; use {list(BANZOS)}"))
    d["entre"] = None
    if "entre" in dados:
        d["entre"] = _nos_existentes(modelo, dados["entre"], "entre", 2, erros)
        if "vao" in dados:
            erros.append(("vao", "com 'entre' o vão é a distância entre os nós"))
    else:
        d["vao"] = _g._escalar(dados, "vao", erros)
    d["altura"] = (float(dados["altura"]) if "altura" in dados and _g._num_ok(dados["altura"], -1e-12)
                   else _g._escalar(dados, "altura", erros))
    if d["banzo"] == "paralelo":
        if "altura2" in dados:
            erros.append(("altura2", "não se aplica a banzos paralelos"))
        d["altura2"] = d["altura"]
    else:
        d["altura2"] = _g._escalar(dados, "altura2", erros)
    if d["altura"] is not None and d["altura2"] is not None and max(d["altura"], d["altura2"]) <= TOL:
        erros.append(("altura", "a treliça precisa de altura > 0 em algum ponto"))
    if d["modelo"] == "fink":
        if dados.get("paineis", 4) != 4:
            erros.append(("paineis", "Fink: 4 painéis no banzo superior (W clássico)"))
        if d["banzo"] != "duas_aguas" or d["altura"] != 0:
            erros.append(("modelo", "Fink: tesoura triangular (banzo duas_aguas, altura 0 nos apoios)"))
        d["paineis"] = 4
    else:
        d["paineis"] = _inteiro(dados, "paineis", erros, 6, 2 if d["modelo"] != "warren" else 1)
    if d["modelo"] in ("pratt", "howe", "warren_v", "k") and d["banzo"] == "paralelo" and d["altura"] is not None \
            and d["altura"] <= TOL:
        erros.append(("altura", "banzos paralelos com altura 0"))
    d["rotulas"] = dados.get("rotulas", "diagonais")
    if d["rotulas"] not in ("diagonais", "todas", "nenhuma"):
        erros.append(("rotulas", "use 'diagonais', 'todas' ou 'nenhuma'"))
    d["eixo"] = dados.get("eixo", "x")
    if d["eixo"] not in ("x", "y"):
        erros.append(("eixo", "use 'x' ou 'y'"))
    org = dados.get("origem", [0.0, 0.0, 0.0])
    if (not isinstance(org, (list, tuple)) or len(org) != 3
            or not all(not isinstance(v, bool) and isinstance(v, (int, float)) and math.isfinite(v) for v in org)):
        erros.append(("origem", f"[x, y, z] finitos em m obrigatório (recebido {org!r})"))
        org = [0.0, 0.0, 0.0]
    d["origem"] = tuple(float(v) for v in org)
    pad = ["livre", "livre"] if d["entre"] else ["rotula", "movel"]
    ap = dados.get("apoios", pad)
    if not isinstance(ap, (list, tuple)) or len(ap) != 2 or any(a not in ("rotula", "movel", "engaste", "livre") for a in ap):
        erros.append(("apoios", "[esq, dir] com 'rotula', 'movel', 'engaste' ou 'livre'"))
        ap = pad
    d["apoios"] = list(ap)
    tp = dados.get("travar_plano")
    if tp is not None and not isinstance(tp, bool):
        erros.append(("travar_plano", "true/false"))
    d["travar_plano"] = tp
    spec = dados.get("secoes")
    if not isinstance(spec, dict):
        erros.append(("secoes", "objeto {banzo, diagonal, montante?} obrigatório"))
        spec = {}
    erros += [(f"secoes.{c}", "chave desconhecida") for c in spec if c not in _SECOES[tipo]]
    sec = {k: _g._resolve_secao(modelo, v, f"secoes.{k}", erros, novas) for k, v in spec.items() if k in _SECOES[tipo]}
    for g_ in ("banzo_superior", "banzo_inferior"):
        if g_ not in sec:
            if "banzo" in sec:
                sec[g_] = sec["banzo"]
            else:
                erros.append((f"secoes.{g_}", "obrigatória (ou secoes.banzo para os dois)"))
    if "diagonal" not in sec:
        erros.append(("secoes.diagonal", "obrigatória"))
    sec.setdefault("montante", sec.get("diagonal"))
    d["secoes"] = sec
    return d


def _montar_trelica(obra, d, mat, origem, u, prefixo_local=""):
    """Põe uma treliça (geometria local) no espaço: ponto = origem + s·u + h·Z. → {nome local: id do nó}."""
    L = d["vao"]
    nos, barras, alias = geometria_trelica(d["modelo"], d["banzo"], L, d["altura"], d["altura2"], d["paineis"])
    alinhado = abs(abs(u[0]) - 1) < 1e-9 or abs(abs(u[1]) - 1) < 1e-9
    eixo = "x" if abs(u[0]) > 0.5 else "y"
    travar = d["travar_plano"] if d["travar_plano"] is not None else (alinhado and d["entre"] is None)
    trava = (False, eixo == "x", False, eixo == "x", eixo == "y", True) if eixo == "x" else \
            (True, False, False, False, True, True)
    if not travar:
        trava = _LIVRE
    ids = {}
    ultimo = max(int(k[1:]) for k in nos if k.startswith("I"))
    for nome, (s, h) in nos.items():
        if d["entre"] and nome == "I0":
            ids[nome] = d["entre"][0]
            continue
        if d["entre"] and nome == f"I{ultimo}":
            ids[nome] = d["entre"][1]
            continue
        ap = _LIVRE
        if nome == "I0":
            ap = _apoio(d["apoios"][0], eixo, alinhado)
        elif nome == f"I{ultimo}":
            ap = _apoio(d["apoios"][1], eixo, alinhado)
        ap = tuple(a or t for a, t in zip(ap, trava))
        p = _add(_add(origem, _mul(u, s)), (0.0, 0.0, h))
        ids[nome] = obra.no(prefixo_local + nome, p, ap)
    for k, v in alias.items():
        ids[k] = ids[v]
    rot_web = d["rotulas"] in ("diagonais", "todas")
    for nome, a, b, grupo in barras:
        sec = d["secoes"][grupo]
        extra = {}
        if grupo in ("diagonal", "montante"):
            if rot_web:
                extra["trelica"] = True
        else:
            extra["elemento_desloc"] = "viga_cobertura"
            if d["rotulas"] == "todas":
                extra["trelica"] = True
        obra.barra(prefixo_local + nome, ids[a], ids[b], sec, mat, **extra)
    return ids, {**nos, **{k: nos[v] for k, v in alias.items()}}


def _trelica(modelo, dados, tipo, erros, novas, novos):
    d = _ler_trelica(modelo, dados, tipo, erros, novas)
    mat = _material(modelo, dados, erros, novos)
    if tipo == "trelicas_serie":
        d["n"] = _inteiro(dados, "n", erros)
        d["espacamento"] = _g._escalar(dados, "espacamento", erros)
    p = _prefixo(modelo, dados, tipo, erros)
    u = (1.0, 0.0, 0.0) if d["eixo"] == "x" else (0.0, 1.0, 0.0)
    origem = d["origem"]
    if d.get("entre") and not erros:
        a, b = (modelo.nos[i] for i in d["entre"])
        if abs(a.z - b.z) > TOL:
            erros.append(("entre", f"os nós precisam estar na mesma cota (z {a.z:g} × {b.z:g})"))
        else:
            v = (b.x - a.x, b.y - a.y, 0.0)
            d["vao"] = _norma(v)
            if d["vao"] <= TOL:
                erros.append(("entre", "nós na mesma vertical"))
            else:
                u, origem = _unit(v), (a.x, a.y, a.z)
                if d["travar_plano"] and not (abs(abs(u[0]) - 1) < 1e-9 or abs(abs(u[1]) - 1) < 1e-9):
                    erros.append(("travar_plano", "só para treliça em X ou em Y"))
    if erros:
        raise _g.ErroGerador(erros)
    obra = _Obra(modelo, p)
    if tipo == "trelica_plana":
        _montar_trelica(obra, d, mat, origem, u)
        if d["rotulas"] == "diagonais":
            obra.avisos.append("banzos contínuos; diagonais e montantes só com esforço axial (trelica)")
        if d["travar_plano"] is None and not d["entre"]:
            obra.avisos.append("treliça isolada: nós travados fora do plano (sem terças/contraventamento seria "
                               "mecanismo); use travar_plano=false quando a estrutura tiver travamento")
        return obra
    perp = (-u[1], u[0], 0.0)
    linhas = []
    for k in range(d["n"]):
        o = _add(origem, _mul(perp, k * d["espacamento"]))
        ids, nos = _montar_trelica(obra, d, mat, o, u, f"{k + 1}-")
        linhas.append((ids, nos))
    st = d["secoes"].get("terca")
    if st is not None and d["n"] > 1:
        sup = sorted((n for n in linhas[0][1] if n.startswith("S")), key=lambda n: int(n[1:]))
        nt = 0
        for nome in sup:
            s, h = linhas[0][1][nome]
            dh = 1e-3 * d["vao"]
            h_a = altura_em(d["banzo"], d["vao"], d["altura"], d["altura2"], max(0.0, s - dh))
            h_b = altura_em(d["banzo"], d["vao"], d["altura"], d["altura2"], min(d["vao"], s + dh))
            r = _unit(_add(_mul(u, min(d["vao"], s + dh) - max(0.0, s - dh)), (0.0, 0.0, h_b - h_a)))
            normal = _cross(perp, r)
            if normal[2] < 0:
                normal = _mul(normal, -1)
            for k in range(d["n"] - 1):
                a, b = linhas[k][0][nome], linhas[k + 1][0][nome]
                nt += 1
                obra.barra(f"T{nt}", a, b, st, mat, rot_normal(perp, normal), rotula_i=True, rotula_j=True,
                           elemento_desloc="viga_cobertura")
        obra.avisos.append(f"{nt} terças rotuladas ligando os nós do banzo superior")
    obra.avisos.append("treliças em série sem contraventamento: acrescente contraventamento no plano da "
                       "cobertura (tipo contraventamento) antes de dimensionar")
    return obra


# ---------------------------------------------------------------- contraventamento

def _painel_de_barras(modelo, ids, erros):
    if not isinstance(ids, (list, tuple)) or len(ids) != 2 or any(i not in modelo.barras for i in ids):
        erros.append(("barras", f"2 ids de barras existentes (recebido {ids!r})"))
        return None
    A, B = (modelo.barras[i] for i in ids)
    pos = lambda n: (modelo.nos[n].x, modelo.nos[n].y, modelo.nos[n].z)
    a0, a1, b0, b1 = A.ni, A.nj, B.ni, B.nj
    if _norma(_sub(pos(a0), pos(b0))) + _norma(_sub(pos(a1), pos(b1))) > \
            _norma(_sub(pos(a0), pos(b1))) + _norma(_sub(pos(a1), pos(b0))):
        b0, b1 = b1, b0
    if pos(a1)[2] < pos(a0)[2] - TOL:
        a0, a1, b0, b1 = a1, a0, b1, b0
    if len({a0, a1, b0, b1}) != 4:
        erros.append(("barras", "as duas barras não podem ter nó comum"))
        return None
    return [a0, b0, b1, a1]


def _contraventamento(modelo, dados, erros, novas, novos):
    mod = dados.get("modelo", "x")
    if mod not in MODELOS_CONTRA:
        erros.append(("modelo", f"'{mod}' inválido; use {list(MODELOS_CONTRA)}"))
    if ("painel" in dados) == ("barras" in dados):
        erros.append(("painel", "informe painel [n1, n2, n3, n4] ou barras [bA, bB] (um dos dois)"))
        pn = None
    elif "painel" in dados:
        pn = _nos_existentes(modelo, dados["painel"], "painel", 4, erros)
    else:
        pn = _painel_de_barras(modelo, dados["barras"], erros)
    div = _inteiro(dados, "divisoes", erros, 1)
    if div and div > 1 and mod not in ("x", "diagonal"):
        erros.append(("divisoes", "só para X e diagonal"))
    dist = dados.get("distancia", 1.0)
    if not _g._num_ok(dist):
        erros.append(("distancia", "número > 0 (m)"))
    spec = dados.get("secoes")
    if not isinstance(spec, dict) or "contraventamento" not in spec:
        erros.append(("secoes.contraventamento", "obrigatória"))
        spec = spec if isinstance(spec, dict) else {}
    erros += [(f"secoes.{c}", "chave desconhecida") for c in spec if c not in _SECOES["contraventamento"]]
    sec = {k: _g._resolve_secao(modelo, v, f"secoes.{k}", erros, novas) for k, v in spec.items() if k in _SECOES["contraventamento"]}
    mat = _material(modelo, dados, erros, novos)
    p = _prefixo(modelo, dados, "contraventamento", erros)
    if erros:
        raise _g.ErroGerador(erros)
    sc = sec["contraventamento"]
    se = sec.get("escora", sc)
    obra = _Obra(modelo, p)
    obra.abrir_pontos()
    P = [obra.xyz(n) for n in pn]
    nd = [0]

    def em(i, j, t):  # ponto na aresta Pi → Pj a fração t
        if t <= 0:
            return pn[i]
        if t >= 1:
            return pn[j]
        nd[0] += 1
        return obra.ponto(f"N{nd[0]}", _add(P[i], _mul(_sub(P[j], P[i]), t)), escora=(i, j))

    nb = [0]

    def diag(a, b):
        nb[0] += 1
        obra.barra(f"D{nb[0]}", a, b, sc, mat, trelica=True)

    if mod in ("x", "diagonal"):
        A = [em(0, 3, k / div) for k in range(div + 1)]
        B = [em(1, 2, k / div) for k in range(div + 1)]
        for k in range(div):
            diag(A[k], B[k + 1])
            if mod == "x":
                diag(B[k], A[k + 1])
            if k:
                obra.barra(f"EI{k}", A[k], B[k], se, mat, trelica=True)
    elif mod == "k":
        m = em(1, 2, 0.5)
        diag(pn[0], m); diag(pn[3], m)
    elif mod == "v":
        m = em(0, 1, 0.5)
        diag(pn[3], m); diag(pn[2], m)
    elif mod == "lambda":
        m = em(3, 2, 0.5)
        diag(pn[0], m); diag(pn[1], m)
    else:  # mão-francesa nos cantos de cima (n4 e n3)
        for c, lado, topo in ((3, 0, 2), (2, 1, 3)):
            Lv, Lh = _norma(_sub(P[lado], P[c])), _norma(_sub(P[topo], P[c]))
            if dist >= min(Lv, Lh):
                raise _g.ErroGerador([("distancia", f"{dist:g} m não cabe nas arestas ({min(Lv, Lh):.3g} m)")])
            a = em(c, lado, dist / Lv)
            b = em(c, topo, dist / Lh)
            diag(a, b)
        if obra._soltos:
            raise _g.ErroGerador([("painel", "mão-francesa exige barras nas arestas do canto (pilar e viga)")])
    # pontos que não caem em barra: escoras entre os nós da aresta
    for nid, (i, j) in obra._soltos:
        seq = sorted([pn[i], pn[j]] + [n for n, e in obra._soltos if e in ((i, j), (j, i))],
                     key=lambda n: _dot(_sub(obra.xyz(n), P[i]), _sub(P[j], P[i])))
        for a, b in zip(seq, seq[1:]):
            nb[0] += 1
            obra.barra(f"E{nb[0]}", a, b, se, mat)
    obra.fechar_pontos()
    obra.avisos.append(f"contraventamento {mod}: diagonais como treliça (só axial; o motor não tem barra só de "
                       "tração — no X as duas diagonais trabalham também à compressão)")
    if obra.removidas:
        obra.avisos.append(f"barras divididas para receber o contraventamento: {', '.join(obra.removidas)}")
    return obra


# ---------------------------------------------------------------- terças e tirantes

def _tercas(modelo, dados, erros, novas, novos):
    ids = dados.get("barras")
    if not isinstance(ids, (list, tuple)) or len(ids) < 2 or any(i not in modelo.barras for i in ids):
        erros.append(("barras", f"lista de 2 ou mais barras existentes (tesouras, em ordem) (recebido {ids!r})"))
        ids = None
    esp = _g._escalar(dados, "espacamento", erros)
    nt = _inteiro(dados, "tirantes", erros, 0, 0)
    spec = dados.get("secoes")
    if not isinstance(spec, dict) or "terca" not in spec:
        erros.append(("secoes.terca", "obrigatória"))
        spec = spec if isinstance(spec, dict) else {}
    erros += [(f"secoes.{c}", "chave desconhecida") for c in spec if c not in _SECOES["tercas"]]
    sec = {k: _g._resolve_secao(modelo, v, f"secoes.{k}", erros, novas) for k, v in spec.items() if k in _SECOES["tercas"]}
    if nt and "tirante" not in sec:
        erros.append(("secoes.tirante", "obrigatória com tirantes"))
    mat = _material(modelo, dados, erros, novos)
    p = _prefixo(modelo, dados, "tercas", erros)
    if erros:
        raise _g.ErroGerador(erros)
    obra = _Obra(modelo, p)
    pos = lambda n: obra.xyz(n)
    # ordem ao longo do prédio: projeção do meio de cada tesoura na horizontal perpendicular à primeira
    b0 = modelo.barras[ids[0]]
    r0 = _sub(pos(b0.nj), pos(b0.ni))
    perp = (-r0[1], r0[0], 0.0)
    ids = sorted(ids, key=lambda i: _dot(_mul(_add(pos(modelo.barras[i].ni), pos(modelo.barras[i].nj)), 0.5), perp))
    ends = []
    for k, bid in enumerate(ids):
        b = modelo.barras[bid]
        a, c = b.ni, b.nj
        if k and _norma(_sub(pos(a), pos(ends[0][0]))) > _norma(_sub(pos(c), pos(ends[0][0]))):
            a, c = c, a
        ends.append((a, c))
    L = max(_norma(_sub(pos(c), pos(a))) for a, c in ends)
    ndiv = max(1, math.ceil(L / esp - 1e-9))
    linhas = []  # linhas[k][j] = nó da terça j sobre a tesoura k
    obra.abrir_pontos()
    for k, (a, c) in enumerate(ends):
        pa, pc = pos(a), pos(c)
        linhas.append([obra.ponto(f"{k + 1}-{j}", _add(pa, _mul(_sub(pc, pa), j / ndiv))) for j in range(ndiv + 1)])
    obra.fechar_pontos()
    nb = 0
    for k in range(len(ends) - 1):
        r = _sub(pos(ends[k][1]), pos(ends[k][0]))
        for j in range(ndiv + 1):
            a, b = linhas[k][j], linhas[k + 1][j]
            e = _sub(pos(b), pos(a))
            normal = _cross(e, r)
            if normal[2] < 0:
                normal = _mul(normal, -1)
            rot = rot_normal(e, _unit(normal)) if _norma(normal) > 1e-9 else 0.0
            if frozenset((a, b)) in obra.pares:
                continue
            cadeia = [a] + [obra.no(f"{k + 1}-{j}-t{t}", _add(pos(a), _mul(e, t / (nt + 1))))
                            for t in range(1, nt + 1)] + [b]
            for s in range(len(cadeia) - 1):
                nb += 1
                extra = {"elemento_desloc": "viga_cobertura"}
                if s == 0:
                    extra["rotula_i"] = True
                if s == len(cadeia) - 2:
                    extra["rotula_j"] = True
                obra.barra(f"T{nb}", cadeia[s], cadeia[s + 1], sec["terca"], mat, rot, **extra)
        for j in range(ndiv):
            for t in range(1, nt + 1):
                a, b = obra.p + f"{k + 1}-{j}-t{t}", obra.p + f"{k + 1}-{j + 1}-t{t}"
                if a in obra.nos and b in obra.nos:
                    nb += 1
                    obra.barra(f"R{nb}", a, b, sec["tirante"], mat, trelica=True)
    obra.avisos.append(f"terças a cada {L / ndiv:.3g} m ({ndiv + 1} linhas), rotuladas nas tesouras"
                       + (f"; {nt} linha(s) de tirantes por vão (treliça)" if nt else ""))
    if obra.removidas:
        obra.avisos.append(f"barras divididas nos apoios das terças: {', '.join(obra.removidas)}")
    return obra


def gerar(modelo, dados):
    """Entrada dos tipos deste módulo (chamada por gerador.gerar_estrutura)."""
    tipo = dados["tipo"]
    erros = [(c, "chave desconhecida") for c in dados if c not in _CHAVES[tipo]]
    novas, novos = {}, {}
    if tipo in ("trelica_plana", "trelicas_serie"):
        obra = _trelica(modelo, dados, tipo, erros, novas, novos)
    elif tipo == "contraventamento":
        obra = _contraventamento(modelo, dados, erros, novas, novos)
    else:
        obra = _tercas(modelo, dados, erros, novas, novos)
    conf = [i for i in obra.nos if i in modelo.nos] + [i for i in obra.barras if i in modelo.barras]
    if conf:
        raise _g.ErroGerador([("ids", "ids já existentes no modelo (troque o prefixo): " + _g._fmt_lista(conf))])
    return _aplicar(modelo, obra, novas, novos)
