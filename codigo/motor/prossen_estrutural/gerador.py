"""Gerador de estruturas paramétricas (ESPEC §6.2 #14 e §6.3): pórtico plano, edifício e galpão.
Treliças, contraventamentos e terças: `geradores_extra` (mesma entrada `gerar_estrutura`).

`gerar_estrutura(modelo, dados)` valida tudo, confere conflitos de ids e só então muta o modelo
(tudo ou nada). Não importa `projeto`: `projeto.gerar_estrutura` delega a esta função e converte
`ErroGerador` (ValueError com `.erros = [{"campo", "mensagem"}]`) em `ErroEntrada`.

Ids (previsíveis): nós `N<pav>-<i>-<j>`, pilares `P<pav>-<i>-<j>` (do nível pav-1 ao pav),
vigas `VX<pav>-<i>-<j>` (do nó i ao i+1 no nível pav, direção X) e `VY<pav>-<i>-<j>` (direção Y),
terças `T<k>`. `pav` = 0 na base; `i` = índice da linha de pilares em X; `j` = em Y.
Pavimentos: nomes `P1`…`Pn` (cota = origem z + alturas acumuladas).

Convenções de cada tipo:
- `portico_plano`: plano XZ em y = origem y (j = 0). Para o pórtico não ser mecanismo fora do
  plano, TODOS os nós têm uy, rx e rz restringidos (além do apoio da base). Pavimentos sem diafragma.
- `edificio`: grade vaos_x × vaos_y × alturas; pilares, vigas X e vigas Y em cada pavimento;
  pavimentos com diafragma rígido.
- `galpao`: n_modulos + 1 pórticos transversais (plano XZ) de alma cheia espaçados de `espacamento`
  em Y. Níveis: 0 = base, 1 = beiral (altura_pilar), 2 = cumeeira (altura_cumeeira, cota a partir
  da base). Nós: base/beiral em i = 0 (esq.) e i = 2 (dir.); cumeeira em i = 1. Vigas inclinadas
  `VX2-0-j` (beiral esq. → cumeeira) e `VX2-1-j` (cumeeira → beiral dir.). Terças (se há
  `secoes.terca`), rotuladas nas extremidades, ligam pórticos consecutivos nas linhas beiral esq.,
  cumeeira, beiral dir.: `T<k>`, k = linha·n_modulos + módulo + 1 (linha 0, 1, 2 nessa ordem).
  A alma da terça é girada para ficar normal ao plano da água (cumeeira: sem giro).
  O galpão é 3D de verdade: só a base tem apoio (nenhum nó recebe uy/rx/rz fictício).
  Apoio "engaste": 6 graus presos. Apoio "rotula": ux, uy, uz presos e ry livre (articulado no plano).
  - `contraventamento: true` (exige `secoes.terca`; `secoes.contraventamento` opcional, padrão = terça):
    uma diagonal por painel nos vãos extremos (módulos 0 e n−1), todas `trelica` (só axial), ids
    `DC<k>` (cobertura: beiral→cumeeira de cada água, ligando pórticos consecutivos) e `DP<k>` (parede:
    base→beiral em x = 0 e x = vao). Com as terças (escoras de beiral e cumeeira), os vãos extremos
    formam painéis triangulados e travam os demais pórticos; a base "rotula" fica com rx livre.
  - `contraventamento` ausente/false: NADA é gerado (aviso claro). Para a estrutura não ser mecanismo
    sem travamento longitudinal, apoio "rotula" prende também rx nas bases (articulação só no plano;
    fora dele a base é engastada) e não se prende mais nada; cada pórtico resiste a carga em Y
    pela flexão dos pilares fora do plano (muito flexível: o aviso diz isso).
- Tipo da barra: material com fck → concreto; seção Ue/U formado a frio → pff; senão aço.
  Aço recebe `elemento_desloc` ("viga_piso", último nível/água: "viga_cobertura").
"""
from __future__ import annotations

import logging
import math

from . import materiais as _materiais
from . import secoes as _secoes
from .modelo import Barra, CasoCarga, No, Pavimento

log = logging.getLogger(__name__)

TIPOS = ("portico_plano", "edificio", "galpao")
_COMUNS = {"tipo", "material", "secoes", "apoio", "origem"}
_CHAVES = {"portico_plano": _COMUNS | {"vaos_x", "alturas"},
           "edificio": _COMUNS | {"vaos_x", "vaos_y", "alturas"},
           "galpao": _COMUNS | {"vao", "altura_pilar", "altura_cumeeira", "n_modulos", "espacamento",
                                "contraventamento"}}
_SECOES = {"portico_plano": ("pilar", "viga"), "edificio": ("pilar", "viga"),
           "galpao": ("pilar", "viga", "terca", "contraventamento")}
_OPCIONAIS = ("terca", "contraventamento")
_LIVRE = (False,) * 6
_ENGASTE = (True,) * 6
_ROTULA = (True, True, True, False, False, False)


class ErroGerador(ValueError):
    """Entrada inválida ou conflito de ids; `erros` = [{"campo", "mensagem"}] (todos juntos)."""

    def __init__(self, erros):
        self.erros = [{"campo": c, "mensagem": m} for c, m in erros]
        super().__init__("; ".join(f"{c}: {m}" for c, m in erros))


# ---------------------------------------------------------------- validação

def _num_ok(v, minimo=0.0):
    return (not isinstance(v, bool) and isinstance(v, (int, float)) and math.isfinite(v) and v > minimo)


def _lista_pos(dados, campo, erros):
    v = dados.get(campo)
    if not isinstance(v, (list, tuple)) or not v or not all(_num_ok(x) for x in v):
        erros.append((campo, f"lista não vazia de números finitos > 0 (m) obrigatória (recebido {v!r})"))
        return None
    return [float(x) for x in v]


def _escalar(dados, campo, erros, minimo=0.0):
    v = dados.get(campo)
    if not _num_ok(v, minimo):
        erros.append((campo, f"número finito > {minimo:g} (m) obrigatório (recebido {v!r})"))
        return None
    return float(v)


def _resolve_secao(modelo, spec, campo, erros, novas):
    if isinstance(spec, str):
        if spec in modelo.secoes:
            return modelo.secoes[spec]
        try:
            s = _secoes.catalogo(spec)
        except KeyError:
            erros.append((campo, f"seção '{spec}' não existe no modelo nem no catálogo"))
            return None
    else:
        try:
            s = _secoes.secao_de_spec(spec)
        except ValueError as e:
            erros.append((campo, str(e)))
            return None
    ex = modelo.secoes.get(s.nome) or novas.get(s.nome)
    if ex is None:
        novas[s.nome] = s
        return s
    if not all(math.isclose(getattr(ex, p), getattr(s, p), rel_tol=1e-6) for p in ("A", "Iy", "Iz")):
        erros.append((campo, f"já existe seção '{s.nome}' com propriedades diferentes; renomeie"))
        return None
    return ex


def _resolve_material(modelo, spec, erros, novos):
    try:
        if isinstance(spec, str):
            if spec in modelo.materiais:
                return modelo.materiais[spec]
            m = _materiais.material_de_spec({"aco": spec})
        else:
            m = _materiais.material_de_spec(spec)
    except ValueError as e:
        erros.append(("material", str(e)))
        return None
    ex = modelo.materiais.get(m.nome) or novos.get(m.nome)
    if ex is None:
        novos[m.nome] = m
        return m
    if not (math.isclose(ex.E, m.E, rel_tol=1e-6) and math.isclose(ex.peso, m.peso, rel_tol=1e-6)):
        erros.append(("material", f"já existe material '{m.nome}' com propriedades diferentes; renomeie"))
        return None
    return ex


def _tipo_barra(sec, mat):
    p = sec.props
    if mat.fck is not None:
        return "concreto"
    if p.get("tipo") == "Ue" or (p.get("tipo") == "U" and "tw" not in p):
        return "pff"
    return "aco"


def _fmt_lista(ids, n=20):
    return ", ".join(ids[:n]) + (f" … (+{len(ids) - n})" if len(ids) > n else "")


# ---------------------------------------------------------------- construção

class _Plano:
    """Acumula nós/barras/pavimentos sem tocar no modelo."""

    def __init__(self, ox, oy, oz, apoio_base, plano, apoio_rotula=False):
        self.o = (ox, oy, oz)
        self.apoio_base = apoio_base
        self.plano = plano  # True → uy, rx, rz restringidos em todos os nós (só pórtico plano)
        self.apoio_rotula = apoio_rotula
        self.nos, self.barras, self.pavimentos, self.avisos = {}, {}, [], []

    def no(self, nid, x, y, z, base=False):
        ap = self.apoio_base if base else _LIVRE
        if self.plano:
            ap = tuple(a or g in (1, 3, 5) for g, a in enumerate(ap))
        self.nos[nid] = No(nid, round(self.o[0] + x, 9), round(self.o[1] + y, 9), round(self.o[2] + z, 9), ap)
        return nid

    def barra(self, bid, ni, nj, secao, mat, papel, rot=0.0, **extra):
        params = {"papel": papel, **extra}
        if mat.fck is None and "elemento_desloc" not in params and papel == "viga":
            params["elemento_desloc"] = "viga_piso"
        self.barras[bid] = Barra(bid, ni, nj, secao.nome, mat.nome, rot, _tipo_barra(secao, mat), params)


def _cotas(alturas):
    zs = [0.0]
    for h in alturas:
        zs.append(round(zs[-1] + h, 9))
    return zs


def _grade(p, vx, vy, alt, sp, sv, mat):
    """Pórtico plano (vy = [] → j = 0 só) ou edifício: grade de nós, pilares e vigas."""
    xs, ys, zs = _cotas(vx), _cotas(vy) if vy else [0.0], _cotas(alt)
    np_ = len(alt)
    for k, z in enumerate(zs):
        for i, x in enumerate(xs):
            for j, y in enumerate(ys):
                p.no(f"N{k}-{i}-{j}", x, y, z, base=k == 0)
    for k in range(1, np_ + 1):
        topo = k == np_
        ed = {"elemento_desloc": "viga_cobertura" if topo else "viga_piso"} if mat.fck is None else {}
        for i in range(len(xs)):
            for j in range(len(ys)):
                p.barra(f"P{k}-{i}-{j}", f"N{k-1}-{i}-{j}", f"N{k}-{i}-{j}", sp, mat, "pilar")
                if i < len(xs) - 1:
                    p.barra(f"VX{k}-{i}-{j}", f"N{k}-{i}-{j}", f"N{k}-{i+1}-{j}", sv, mat, "viga", **ed)
                if j < len(ys) - 1:
                    p.barra(f"VY{k}-{i}-{j}", f"N{k}-{i}-{j}", f"N{k}-{i}-{j+1}", sv, mat, "viga", **ed)
        p.pavimentos.append(Pavimento(f"P{k}", round(p.o[2] + zs[k], 9), diafragma=bool(vy)))


def _contraventamento(p, d, sc, mat):
    """Uma diagonal (treliça) por painel nos vãos extremos: cobertura (DC) e paredes laterais (DP)."""
    n = d["n_modulos"]
    extremos = [(0, 1)] + ([(n, n - 1)] if n > 1 else [])  # (pórtico da base/beiral, pórtico oposto)
    dc = dp = 0
    for a, b in extremos:
        # cobertura: beiral do pórtico a → cumeeira do pórtico b (espelhado no outro extremo)
        for beiral in ("N1-0", "N1-2"):
            dc += 1
            p.barra(f"DC{dc}", f"{beiral}-{a}", f"N2-1-{b}", sc, mat, "viga", trelica=True)
        # parede: base do pórtico a → beiral do pórtico b, em x = 0 e x = vao
        for i in (0, 2):
            dp += 1
            p.barra(f"DP{dp}", f"N0-{i}-{a}", f"N1-{i}-{b}", sc, mat, "viga", trelica=True)
    p.avisos.append(f"contraventamento gerado nos vãos extremos: {dc} diagonais de cobertura (DC) e {dp} de parede "
                    "(DP), como treliça (só esforço axial); confira seção, posição e esbeltez")


def _galpao(p, d, sp, sv, st, mat, sc=None):
    vao, hp, hc = d["vao"], d["altura_pilar"], d["altura_cumeeira"]
    n, esp = d["n_modulos"], d["espacamento"]
    cos_t = (vao / 2) / math.hypot(vao / 2, hc - hp)
    theta = math.acos(cos_t)
    for j in range(n + 1):
        y = j * esp
        p.no(f"N0-0-{j}", 0.0, y, 0.0, base=True)
        p.no(f"N0-2-{j}", vao, y, 0.0, base=True)
        p.no(f"N1-0-{j}", 0.0, y, hp)
        p.no(f"N1-2-{j}", vao, y, hp)
        p.no(f"N2-1-{j}", vao / 2, y, hc)
        p.barra(f"P1-0-{j}", f"N0-0-{j}", f"N1-0-{j}", sp, mat, "pilar")
        p.barra(f"P1-2-{j}", f"N0-2-{j}", f"N1-2-{j}", sp, mat, "pilar")
        ed = {"elemento_desloc": "viga_cobertura"} if mat.fck is None else {}
        p.barra(f"VX2-0-{j}", f"N1-0-{j}", f"N2-1-{j}", sv, mat, "viga", **ed)
        p.barra(f"VX2-1-{j}", f"N2-1-{j}", f"N1-2-{j}", sv, mat, "viga", **ed)
    if st is not None:
        # linha 0 = beiral esq. (água sobe para +X: normal (-sen, cos) → rot = -θ);
        # linha 1 = cumeeira (rot 0); linha 2 = beiral dir. (rot = +θ). Ver solver._rotacao.
        for linha, (no_, rot) in enumerate((("N1-0", -theta), ("N2-1", 0.0), ("N1-2", theta))):
            for m in range(n):
                k = linha * n + m + 1
                p.barra(f"T{k}", f"{no_}-{m}", f"{no_}-{m+1}", st, mat, "viga", rot=rot,
                        rotula_i=True, rotula_j=True,
                        **({"elemento_desloc": "viga_cobertura"} if mat.fck is None else {}))
    else:
        p.avisos.append("sem seção de terça: pórticos sem ligação longitudinal (sem terças nem contraventamento); "
                        "cada pórtico é analisado isolado")
    if sc is not None:
        _contraventamento(p, d, sc, mat)
    else:
        p.avisos.append("SEM CONTRAVENTAMENTO (contraventamento=false): cada pórtico só resiste a carga em Y "
                        "(oitão, frenagem de ponte rolante) pela flexão dos pilares fora do plano, e as terças "
                        "rotuladas só transmitem esforço axial entre pórticos; use contraventamento=true ou "
                        "adicione diagonais antes de dimensionar")
        if p.apoio_rotula:
            p.avisos.append("apoio 'rotula' sem contraventamento: bases articuladas só no plano (ry livre); "
                            "rx preso para a estrutura não virar mecanismo")


def gerar_estrutura(modelo, dados):
    """Gera nós, barras e pavimentos no `modelo` (muta) conforme `dados` (ESPEC §6.3).

    dados.tipo ∈ {portico_plano, edificio, galpao}; comuns: material, secoes {pilar, viga, terca?},
    apoio ("engaste"|"rotula"), origem [x, y, z]; portico_plano: vaos_x, alturas; edificio: vaos_x,
    vaos_y, alturas; galpao: vao, altura_pilar, altura_cumeeira, n_modulos, espacamento.
    Cria os casos G1 (peso próprio, se `modelo.peso_proprio` vazio) e Q1 se não existirem.
    Retorna {nos_criados, barras_criadas, pavimentos_criados, ids: {nos, barras, pavimentos}, avisos}.
    Erros (todos juntos): ErroGerador (ValueError) — nada é alterado no modelo."""
    if not isinstance(dados, dict):
        raise ErroGerador([("dados", f"esperado objeto, recebido {type(dados).__name__}")])
    tipo = dados.get("tipo")
    from . import geradores_extra
    if tipo in geradores_extra.TIPOS:  # treliças, contraventamentos, terças (docs/v3/TRELICAS.md)
        return geradores_extra.gerar(modelo, dados)
    if tipo not in TIPOS:
        raise ErroGerador([("tipo", f"'{tipo}' inválido; use um de {list(TIPOS + geradores_extra.TIPOS)}")])
    erros = [(c, "chave desconhecida") for c in dados if c not in _CHAVES[tipo]]

    d = {}
    if tipo in ("portico_plano", "edificio"):
        d["vaos_x"] = _lista_pos(dados, "vaos_x", erros)
        d["alturas"] = _lista_pos(dados, "alturas", erros)
        if tipo == "edificio":
            d["vaos_y"] = _lista_pos(dados, "vaos_y", erros)
    else:
        for c in ("vao", "altura_pilar", "altura_cumeeira", "espacamento"):
            d[c] = _escalar(dados, c, erros)
        if d["altura_pilar"] and d["altura_cumeeira"] and d["altura_cumeeira"] <= d["altura_pilar"]:
            erros.append(("altura_cumeeira", "deve ser maior que altura_pilar (cota a partir da base)"))
        nm = dados.get("n_modulos")
        if isinstance(nm, bool) or not isinstance(nm, int) or nm < 1:
            erros.append(("n_modulos", f"inteiro ≥ 1 obrigatório (recebido {nm!r})"))
        else:
            d["n_modulos"] = nm
    apoio = dados.get("apoio", "engaste")
    if apoio not in ("engaste", "rotula"):
        erros.append(("apoio", f"'{apoio}' inválido; use 'engaste' ou 'rotula'"))
    org = dados.get("origem", [0.0, 0.0, 0.0])
    if (not isinstance(org, (list, tuple)) or len(org) != 3
            or not all(not isinstance(v, bool) and isinstance(v, (int, float)) and math.isfinite(v) for v in org)):
        erros.append(("origem", f"[x, y, z] finitos em m obrigatório (recebido {org!r})"))
        org = [0.0, 0.0, 0.0]

    novas, novos = {}, {}
    mat = _resolve_material(modelo, dados["material"], erros, novos) if "material" in dados else None
    if "material" not in dados:
        erros.append(("material", "obrigatório (nome existente, {aco}, {concreto}, ...)"))
    spec = dados.get("secoes")
    if not isinstance(spec, dict):
        erros.append(("secoes", "objeto {pilar, viga, ...} obrigatório"))
        spec = {}
    sec = {}
    for nome in _SECOES[tipo]:
        if nome in spec:
            sec[nome] = _resolve_secao(modelo, spec[nome], f"secoes.{nome}", erros, novas)
        elif nome not in _OPCIONAIS:
            erros.append((f"secoes.{nome}", "obrigatória"))
    erros += [(f"secoes.{c}", "chave desconhecida") for c in spec if c not in _SECOES[tipo]]
    contra = dados.get("contraventamento", False)
    if tipo == "galpao":
        if not isinstance(contra, bool):
            erros.append(("contraventamento", f"true/false (recebido {contra!r})"))
        elif contra and "terca" not in spec:
            erros.append(("contraventamento", "exige secoes.terca (as terças fazem as escoras de beiral e cumeeira)"))
    if erros:
        raise ErroGerador(erros)

    base = _ROTULA if apoio == "rotula" else _ENGASTE
    plano = tipo == "portico_plano"
    if tipo == "galpao" and apoio == "rotula" and not contra:
        base = (True, True, True, True, False, False)  # sem travamento longitudinal: rx preso (ver docstring)
    p = _Plano(*org, base, plano, apoio == "rotula")
    if tipo == "portico_plano":
        _grade(p, d["vaos_x"], [], d["alturas"], sec["pilar"], sec["viga"], mat)
        p.avisos.append("pórtico plano: uy, rx e rz restringidos em todos os nós (evita mecanismo fora do plano)")
    elif tipo == "edificio":
        _grade(p, d["vaos_x"], d["vaos_y"], d["alturas"], sec["pilar"], sec["viga"], mat)
    else:
        _galpao(p, d, sec["pilar"], sec["viga"], sec.get("terca"), mat,
                (sec.get("contraventamento") or sec["terca"]) if contra else None)

    conf = [("nos", [i for i in p.nos if i in modelo.nos]),
            ("barras", [i for i in p.barras if i in modelo.barras]),
            ("pavimentos", [pv.nome for pv in p.pavimentos if pv.nome in modelo.pavimentos])]
    conf = [(k, v) for k, v in conf if v]
    if conf:
        raise ErroGerador([("ids", "ids já existentes no modelo — " +
                            "; ".join(f"{k}: {_fmt_lista(v)}" for k, v in conf))])

    for s in novas.values():
        modelo.add(s)
    for m in novos.values():
        modelo.add(m)
    for o in (*p.nos.values(), *p.barras.values(), *p.pavimentos):
        modelo.add(o)
    if "G1" not in modelo.casos:
        modelo.add(CasoCarga("G1", "G"))
    if "Q1" not in modelo.casos:
        modelo.add(CasoCarga("Q1", "Q"))
    if modelo.peso_proprio is None:
        modelo.peso_proprio = "G1"
    log.debug("gerar_estrutura %s: %d nós, %d barras", tipo, len(p.nos), len(p.barras))
    return {"nos_criados": len(p.nos), "barras_criadas": len(p.barras),
            "pavimentos_criados": len(p.pavimentos),
            "ids": {"nos": list(p.nos), "barras": list(p.barras), "pavimentos": [pv.nome for pv in p.pavimentos]},
            "avisos": [{"nivel": "aviso", "codigo": "gerador", "mensagem": a, "ref": None} for a in p.avisos]}
