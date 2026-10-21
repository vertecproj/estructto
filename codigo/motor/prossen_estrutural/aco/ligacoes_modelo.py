"""Ligações do modelo: identificar as ligações nos nós, agrupar as iguais, achar a pior do grupo e dimensionar.

Prática de escritório: calcula-se a pior ligação de cada grupo de ligações iguais e o mesmo detalhe vale para todas.
Documentação: docs/v3/LIGACOES-GRUPOS.md.

- Membro de ligação = ponta de barra num nó: {no, barra, ponta ("i"|"j")}.
- Tipo de nó (TIPOS) proposto pela geometria; assinatura = tipo + seções + ângulo (1°) + condição + material.
- Grupos ficam no JSON do modelo: modelo.ligacoes = {"grupos": {id: {nome, tipo, assinatura, membros, projeto,
  travado, manual}}}. `manual` = o usuário mexeu nos membros (a re-identificação não tira ninguém dele).
- Dimensionar: o projeto do grupo (`tipo_ligacao` + `dados`) é verificado com os esforços de cada membro em todas as
  combinações ELU; o membro de maior taxa governa e o detalhe vale para todos (aplicar = mesmo projeto para o grupo).

Estender: novo tipo de nó → TIPOS (+ regra em `_classificar_no`); novo tipo de ligação → VERIFICADORES
(função (modelo, membro, esforcos_da_ponta, dados) → resultado com `taxa_max`).
Barras de concreto não entram (ligação monolítica).
"""
from __future__ import annotations

import math

from . import ligacoes as _lig
from . import placa_base as _placa
from ..solver import eixos_locais

# tipo de nó → (rótulo, tipo de ligação sugerido)
TIPOS = {
    "base": ("Base de pilar", "placa_base"),
    "viga_pilar_mesa": ("Viga–pilar (mesa)", "chapa_simples"),
    "viga_pilar_alma": ("Viga–pilar (alma)", "chapa_simples"),
    "viga_viga": ("Viga–viga", "chapa_simples"),
    "emenda": ("Emenda", "parafuso"),
    "gusset_contraventamento": ("Gusset de contraventamento", "ligacao_tracao"),
    "no_trelica": ("Nó de treliça", "ligacao_tracao"),
}
_COS_COLINEAR = math.cos(math.radians(1.0))
_PRIORIDADE = {"pilar": 0, "viga": 1, "diagonal": 2}


# ---------------------------------------------------------------- identificação

def _chave(mb):
    return (mb["no"], mb["barra"], mb["ponta"])


def _dir(m, b, ponta):
    """Vetor unitário saindo do nó da ponta ao longo da barra."""
    a, c = (m.nos[b.ni], m.nos[b.nj]) if ponta == "i" else (m.nos[b.nj], m.nos[b.ni])
    d = (c.x - a.x, c.y - a.y, c.z - a.z)
    L = math.sqrt(sum(x * x for x in d))
    return tuple(x / L for x in d) if L > 0 else (0.0, 0.0, 0.0)


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _classe(m, b):
    if b.params.get("trelica"):
        return "diagonal"
    papel = b.params.get("papel")
    if papel in ("pilar", "viga", "diagonal"):
        return papel
    dz = abs(_dir(m, b, "i")[2])
    return "pilar" if dz > 0.99 else "viga" if dz < math.sin(math.radians(30)) else "diagonal"


def _rotulada(b, ponta):
    return bool(b.params.get(f"rotula_{ponta}") or b.params.get("trelica"))


def _apoiado(no):
    a = no.apoio
    return bool(a[2] or sum(a[:3]) >= 2)


def _ang(a, b, linha=True):
    """Ângulo (°, inteiro) entre direções; linha=True: entre retas (0–90)."""
    c = _dot(a, b)
    return int(round(math.degrees(math.acos(min(1.0, abs(c) if linha else max(-1.0, c))))))


def _membro(m, no, e, tipo, apoio=None, ang=0, cond=None):
    b = e["b"]
    sec_ap = apoio["b"].secao if apoio else ""
    papel_ap = (apoio["papel"] if apoio else "")
    cond = cond or ("rotulada" if _rotulada(b, e["ponta"]) else "rigida")
    assin = "|".join(str(x) for x in (tipo, b.secao, sec_ap, papel_ap, ang, cond, b.material))
    rot = TIPOS[tipo][0]
    nome = f"{rot} {b.secao}" + (f" → {sec_ap}" if sec_ap else "") + (f" {ang}°" if tipo in ("emenda",) and ang else "")
    return dict(no=no, barra=b.id, ponta=e["ponta"], tipo=tipo, assinatura=assin, nome=nome)


def _emenda(m, nid, a, c):
    """Uma ligação (membro = a de menor id) entre duas barras; ângulo = desvio da reta (0 = alinhadas)."""
    a, c = sorted((a, c), key=lambda e: e["b"].id)
    cond = "rotulada" if _rotulada(a["b"], a["ponta"]) or _rotulada(c["b"], c["ponta"]) else "rigida"
    ang = int(round(math.degrees(math.acos(max(-1.0, min(1.0, -_dot(a["d"], c["d"])))))))
    mb = _membro(m, nid, a, "emenda", c, ang, cond)
    secs = sorted((a["b"].secao, c["b"].secao))   # assinatura independe da ordem
    mb["assinatura"] = "|".join(str(x) for x in ("emenda", *secs, ang, cond, a["b"].material))
    return mb


def _classificar_no(m, nid, ends):
    """Pontas de barra num nó → membros de ligação (lista)."""
    no = m.nos[nid]
    apoiado = _apoiado(no)
    if not apoiado and len(ends) < 2:
        return []
    for e in ends:
        e["papel"] = _classe(m, e["b"])
        e["d"] = _dir(m, e["b"], e["ponta"])
    # emenda: só duas barras da mesma classe, sem apoio (reta, cumeeira, joelho de mesma classe)
    if not apoiado and len(ends) == 2 and ends[0]["papel"] == ends[1]["papel"]:
        return [_emenda(m, nid, *ends)]
    # par contínuo (passa pelo nó): pilar > viga (só sem pilar no nó) > diagonal (só sem pilar e viga)
    papeis = {e["papel"] for e in ends}
    continuo = None
    for i, a in enumerate(ends):
        for c in ends[i + 1:]:
            if a["papel"] != c["papel"] or _dot(a["d"], c["d"]) > -_COS_COLINEAR:
                continue
            if a["papel"] != "diagonal" and (_rotulada(a["b"], a["ponta"]) or _rotulada(c["b"], c["ponta"])):
                continue
            if a["papel"] == "viga" and "pilar" in papeis or a["papel"] == "diagonal" and papeis & {"pilar", "viga"}:
                continue
            if continuo is None or _PRIORIDADE[a["papel"]] < _PRIORIDADE[continuo[0]["papel"]]:
                continuo = (a, c)
    pilares = [e for e in ends if e["papel"] == "pilar"]
    out = []
    # cumeeira/joelho com terças ou diagonais chegando: as duas vigas rígidas formam uma emenda e apoiam as outras
    vigas = [e for e in ends if e["papel"] == "viga" and not _rotulada(e["b"], e["ponta"])]
    if not apoiado and not pilares and continuo is None and len(vigas) == 2:
        out.append(_emenda(m, nid, *vigas))
        continuo = tuple(vigas)
    passa = set(id(e) for e in continuo) if continuo else set()
    apoio_pilar = continuo[0] if continuo and continuo[0]["papel"] == "pilar" else (pilares[0] if pilares else None)
    if apoio_pilar is not None:
        apoio_pilar = dict(apoio_pilar, papel="pilar continuo" if id(apoio_pilar) in passa else "pilar termina")
    so_trelica = all(e["papel"] == "diagonal" for e in ends)
    for e in ends:
        if id(e) in passa:
            continue
        b, papel = e["b"], e["papel"]
        if apoiado and papel != "diagonal":
            cond = "rotulada" if _rotulada(b, e["ponta"]) or not (no.apoio[3] and no.apoio[4]) else "rigida"
            out.append(_membro(m, nid, e, "base", None, _ang(e["d"], (0, 0, 1)), cond))
        elif papel == "diagonal":
            ap = (continuo[0] if continuo else apoio_pilar or next((x for x in ends if x["papel"] == "viga"), None))
            tipo = "no_trelica" if so_trelica else "gusset_contraventamento"
            if ap is not None and apoiado:
                ap = dict(ap, papel="base")   # gusset no pé do pilar ≠ gusset no topo
            out.append(_membro(m, nid, e, tipo, ap, _ang(e["d"], ap["d"]) if ap else 0))
        elif papel == "viga":
            if apoio_pilar is not None:
                _ex, ey, ez = eixos_locais(m, apoio_pilar["b"].id)
                tipo = "viga_pilar_mesa" if abs(_dot(e["d"], ez)) >= abs(_dot(e["d"], ey)) else "viga_pilar_alma"
                out.append(_membro(m, nid, e, tipo, apoio_pilar, _ang(e["d"], apoio_pilar["d"])))
            else:
                ap = continuo[0] if continuo else max((x for x in ends if x is not e and x["papel"] == "viga"),
                                                      key=lambda x: m.secoes[x["b"].secao].A, default=None)
                out.append(_membro(m, nid, e, "viga_viga", ap, _ang(e["d"], ap["d"]) if ap else 0))
        # pilar que termina sem apoio (topo, pilar sobre viga): o pilar é o apoio das vigas; sem ligação própria
    return out


def membros(modelo):
    """Todas as ligações do modelo, sem agrupar: [{no, barra, ponta, tipo, assinatura, nome}]."""
    m = modelo
    pontas = {}
    for b in m.barras.values():
        if b.tipo == "concreto":
            continue
        pontas.setdefault(b.ni, []).append(dict(b=b, ponta="i"))
        pontas.setdefault(b.nj, []).append(dict(b=b, ponta="j"))
    out = []
    for nid in m.nos:
        if nid in pontas:
            out += _classificar_no(m, nid, pontas[nid])
    return out


# ---------------------------------------------------------------- grupos

def _grupos(modelo):
    lig = modelo.ligacoes if isinstance(modelo.ligacoes, dict) else {}
    modelo.ligacoes = lig
    return lig.setdefault("grupos", {})


def _novo_id(grupos):
    n = max([int(k[1:]) for k in grupos if k[:1] == "G" and k[1:].isdigit()] + [0]) + 1
    return f"G{n}"


def _projeto_padrao(tipo):
    sug = TIPOS[tipo][1]
    return dict(tipo_ligacao=sug, dados=dict(pelo_perfil=True)) if sug == "placa_base" else None


def _mb(n):
    return dict(no=n["no"], barra=n["barra"], ponta=n["ponta"])


def identificar(modelo):
    """(Re)identifica as ligações e atualiza `modelo.ligacoes` guardando as edições do usuário:
    - membro que sumiu do modelo sai do grupo;
    - grupo travado ou manual mantém os membros que ainda existem; nos outros, membro cuja assinatura mudou sai;
    - membro sem grupo vai para o primeiro grupo não travado com a mesma assinatura, ou para um grupo novo;
    - grupo vazio é apagado. → {"grupos": n, "membros": n, "novos": [ids]}"""
    novos = membros(modelo)
    por_chave = {_chave(n): n for n in novos}
    grupos = _grupos(modelo)
    colocados = set()
    for g in grupos.values():
        manter = []
        for mb in g.get("membros", []):
            k = _chave(mb)
            n = por_chave.get(k)
            if n is None or k in colocados:
                continue
            if g.get("travado") or g.get("manual") or n["assinatura"] == g.get("assinatura"):
                manter.append(_mb(n))
                colocados.add(k)
        g["membros"] = manter
    criados = []
    for n in novos:
        k = _chave(n)
        if k in colocados:
            continue
        alvo = next((g for g in grupos.values() if not g.get("travado") and g.get("assinatura") == n["assinatura"]),
                    None)
        if alvo is None:
            gid = _novo_id(grupos)
            alvo = grupos[gid] = dict(nome=n["nome"], tipo=n["tipo"], assinatura=n["assinatura"], membros=[],
                                      projeto=_projeto_padrao(n["tipo"]), travado=False, manual=False)
            criados.append(gid)
        alvo["membros"].append(_mb(n))
        colocados.add(k)
    for gid in [k for k, g in grupos.items() if not g["membros"]]:
        del grupos[gid]
    return dict(grupos=len(grupos), membros=len(colocados), novos=[g for g in criados if g in grupos])


def _grupo(modelo, gid):
    grupos = _grupos(modelo)
    if gid not in grupos:
        raise KeyError(gid)
    return grupos[gid]


def _livre(g, gid):
    if g.get("travado"):
        raise ValueError(f"grupo {gid} travado: destrave antes de mudar os membros")


def editar(modelo, dados):
    """Edição do usuário. dados.acao:
    renomear {grupo, nome} | travar {grupo, travado} | projeto {grupo, tipo_ligacao, dados} (tipo_ligacao None = sem) |
    mover {membros: [{no, barra, ponta}], destino: id ou None (grupo novo)} | dividir {grupo, membros} (= mover p/ novo) |
    unir {grupos: [id, ...]} (no primeiro). → {"grupo": id afetado}"""
    d = dict(dados or {})
    acao = d.get("acao")
    grupos = _grupos(modelo)
    if acao == "renomear":
        nome = str(d.get("nome") or "").strip()
        if not nome:
            raise ValueError("nome: obrigatório")
        _grupo(modelo, d.get("grupo"))["nome"] = nome
        return dict(grupo=d["grupo"])
    if acao == "travar":
        _grupo(modelo, d.get("grupo"))["travado"] = bool(d.get("travado", True))
        return dict(grupo=d["grupo"])
    if acao == "projeto":
        g = _grupo(modelo, d.get("grupo"))
        tl = d.get("tipo_ligacao")
        if tl is not None and verificador(tl) is None:
            raise ValueError(f"tipo_ligacao '{tl}' desconhecido; válidos: {', '.join(VERIFICADORES)}")
        g["projeto"] = None if tl is None else dict(tipo_ligacao=tl, dados=dict(d.get("dados") or {}))
        if tl == "chapa_simples" and g["membros"]:  # sugestão: alma da viga do grupo
            b = modelo.barras.get(g["membros"][0]["barra"])
            tw = b and modelo.secoes[b.secao].props.get("tw")
            if tw:
                g["projeto"]["dados"].setdefault("tw", tw)
        return dict(grupo=d["grupo"])
    if acao in ("mover", "dividir"):
        sel = [dict(no=x["no"], barra=x["barra"], ponta=x["ponta"]) for x in d.get("membros") or []]
        if not sel:
            raise ValueError("membros: lista não vazia de {no, barra, ponta}")
        dono = {}
        for gid, g in grupos.items():
            for mb in g["membros"]:
                dono[_chave(mb)] = gid
        faltam = [k for k in map(_chave, sel) if k not in dono]
        if faltam:
            raise ValueError(f"membros sem grupo (identifique antes): {faltam}")
        destino = d.get("destino") if acao == "mover" else None
        origens = {dono[_chave(x)] for x in sel}
        if acao == "dividir" and origens != {d.get("grupo")}:
            raise ValueError("dividir: todos os membros devem ser do grupo informado")
        for gid in origens:
            _livre(grupos[gid], gid)
        if destino is None:
            g0 = grupos[next(iter(origens))]
            destino = _novo_id(grupos)
            grupos[destino] = dict(nome=g0["nome"] + " (b)", tipo=g0["tipo"], assinatura=g0["assinatura"], membros=[],
                                   projeto=_copia(g0.get("projeto")), travado=False, manual=True)
        g = _grupo(modelo, destino)
        _livre(g, destino)
        chaves = set(map(_chave, sel))
        for gid in origens:
            grupos[gid]["membros"] = [x for x in grupos[gid]["membros"] if _chave(x) not in chaves]
            grupos[gid]["manual"] = True
        g["membros"] += [x for x in sel if _chave(x) not in set(map(_chave, g["membros"]))]
        g["manual"] = True
        for gid in [k for k, x in grupos.items() if not x["membros"]]:
            del grupos[gid]
        return dict(grupo=destino)
    if acao == "unir":
        ids = list(dict.fromkeys(d.get("grupos") or []))  # repetido sairia duas vezes do pop (KeyError no meio da mudança)
        if len(ids) < 2:
            raise ValueError("grupos: informe 2 ou mais (distintos)")
        alvo = _grupo(modelo, ids[0])
        for gid in ids:  # tudo conferido antes de mudar qualquer grupo
            _livre(_grupo(modelo, gid), gid)
        for gid in ids[1:]:
            alvo["membros"] += grupos.pop(gid)["membros"]
        alvo["manual"] = True
        return dict(grupo=ids[0])
    raise ValueError(f"acao '{acao}' desconhecida; use renomear, travar, projeto, mover, dividir ou unir")


def _copia(p):
    return None if p is None else dict(tipo_ligacao=p["tipo_ligacao"], dados=dict(p.get("dados") or {}))


# ---------------------------------------------------------------- esforços e verificação

_COMP = ("N", "Vy", "Vz", "T", "My", "Mz")


def esforcos_ponta(calculo, barra, ponta, combinacoes=None):
    """{combinação: {N, Vy, Vz, T, My, Mz}} na ponta (esforço na seção, N > 0 tração) nas combinações ELU."""
    nomes = combinacoes or [c["nome"] for c in calculo.combinacoes if c.get("estado") in ("ELU", "ELU_EXC")]
    out = {}
    for n in nomes:
        r = calculo.resultados.get(n)
        if r is None or barra not in r.esforcos:
            continue
        lin = r.esforcos[barra][0 if ponta == "i" else -1]
        out[n] = {c: float(lin[k + 1]) for k, c in enumerate(_COMP)}
    return out


def demanda(e):
    """Proxy de demanda (sem projeto escolhido): |V|, |N|, |M| da ponta."""
    return dict(V=math.hypot(e["Vy"], e["Vz"]), N=abs(e["N"]), M=math.hypot(e["My"], e["Mz"]))


_ORDEM_DEMANDA = {"base": ("M", "N", "V"), "gusset_contraventamento": ("N", "V", "M"), "no_trelica": ("N", "V", "M"),
                  "emenda": ("M", "V", "N")}  # vigas: V, N, M


def _nota_demanda(tipo, dm):
    return tuple(dm[k] for k in _ORDEM_DEMANDA.get(tipo, ("V", "N", "M")))


# esforços do modelo → esforços pedidos por cada tipo de `aco.ligacoes` (módulos de cálculo, kN)
_ESFORCOS_LIG = {
    "parafuso": lambda e: dict(Ft=max(e["N"], 0.0), Fv=math.hypot(e["Vy"], e["Vz"])),
    "solda_filete": lambda e: dict(F=math.sqrt(e["N"] ** 2 + e["Vy"] ** 2 + e["Vz"] ** 2)),
    "chapa_simples": lambda e: dict(V=math.hypot(e["Vy"], e["Vz"])),
    "ligacao_tracao": lambda e: dict(N=abs(e["N"])),  # ponytail: compressão tratada como tração (chapa sem flambagem)
}


# Tipos do contrato (flexíveis, rígidas, contraventamento): verificador() monta um genérico com esforcos_contrato();
# não entram em VERIFICADORES (um caminho só).


def _verif_lig(tipo):
    def f(modelo, membro, e, dados):
        return _lig.verificar_ligacao(tipo, dados, _ESFORCOS_LIG[tipo](e))
    return f


def _verif_placa(modelo, membro, e, dados):
    """Placa de base (modelo plano): verifica com My no eixo forte e com Mz no eixo fraco da mesma placa; vale a pior."""
    sec = modelo.secoes[modelo.barras[membro["barra"]].secao]
    return _placa.verificar_placa_base_biaxial(dados, esforcos_placa(modelo, e), sec.props)


def esforcos_placa(modelo, e):
    """Esforços da ponta (esforcos_ponta) → entrada de placa_base.casos_biaxiais: N, V = |Vy, Vz|, My, Mz, sismo."""
    sismo = bool(modelo.config.get("sismo")) or any(getattr(c, "tipo", "") == "S" for c in modelo.casos.values())
    return dict(N=e["N"], V=math.hypot(e["Vy"], e["Vz"]), My=e["My"], Mz=e["Mz"], sismo=sismo)


VERIFICADORES = {**{t: _verif_lig(t) for t in _ESFORCOS_LIG}, "placa_base": _verif_placa}


def esforcos_contrato(e):
    """Esforços do modelo na ponta → convenção do contrato (docs/v3/LIGACOES-CONTRATO.md): V no plano da alma = Vz
    (local z = altura do perfil), Vz = Vy, M = My (eixo forte), Mz, T; N > 0 tração. Igual em JanelaLigacaoProjeto.cs."""
    return dict(N=e["N"], V=abs(e["Vz"]), Vz=abs(e["Vy"]), M=e["My"], Mz=e["Mz"], T=e["T"])


def verificador(tl):
    """Verificador do tipo de ligação: os de VERIFICADORES ou, para os tipos do contrato (módulos de família), um genérico
    com os esforços do contrato; None se o tipo não existe."""
    if tl in VERIFICADORES:
        return VERIFICADORES[tl]
    if _lig.registro(tl) is None:
        return None

    def f(modelo, membro, e, dados):
        r = _lig.verificar_ligacao(tl, dados, esforcos_contrato(e))
        r.setdefault("taxa_max", r.get("taxa"))
        return r
    return f


def _taxa(r):
    t = r.get("taxa_max")
    return math.inf if t is None or not math.isfinite(t) else float(t)


def dimensionar_grupo(modelo, calculo, grupo):
    """Verifica o projeto do grupo com os esforços de cada membro em todas as combinações ELU.
    → {grupo, tipo_ligacao, membros: [{no, barra, ponta, taxa, combinacao, demanda, esforcos, erro}],
       governante: {no, barra, ponta, combinacao}, taxa_max, ok, resultado (do governante), por_demanda}
    Sem projeto: só a demanda (|V|, |N|, |M|) e o governante pela demanda (resultado None)."""
    g = _grupo(modelo, grupo)
    proj = g.get("projeto")
    tl = proj and proj.get("tipo_ligacao")
    if tl and verificador(tl) is None:
        raise ValueError(f"tipo_ligacao '{tl}' sem verificador; válidos: {', '.join(VERIFICADORES)}")
    nomes = [c["nome"] for c in calculo.combinacoes if c.get("estado") in ("ELU", "ELU_EXC")]
    saida, gov, gov_res = [], None, None
    for mb in g["membros"]:
        esf = esforcos_ponta(calculo, mb["barra"], mb["ponta"], nomes)
        linha = dict(mb, taxa=None, ok=None, combinacao=None, demanda=None, esforcos=None, erro=None)
        if not esf:
            linha["erro"] = "sem esforços no cálculo (barra nova ou modelo editado: calcule de novo)"
            saida.append(linha)
            continue
        cd = max(esf, key=lambda n: _nota_demanda(g["tipo"], demanda(esf[n])))
        linha.update(demanda=demanda(esf[cd]), combinacao=cd, esforcos=esf[cd])
        if tl:
            pior, pior_r, pior_c = -1.0, None, None
            try:
                for n, e in esf.items():
                    r = verificador(tl)(modelo, mb, e, proj.get("dados") or {})
                    if _taxa(r) > pior:
                        pior, pior_r, pior_c = _taxa(r), r, n
            except (ValueError, KeyError, TypeError) as ex:
                linha["erro"] = str(ex.args[0] if ex.args else ex)
            else:
                linha.update(taxa=pior, combinacao=pior_c, esforcos=esf[pior_c], demanda=demanda(esf[pior_c]),
                             ok=bool(pior_r.get("ok")) and pior <= 1.0 + 1e-9)
                if gov is None or pior > gov["taxa"]:
                    gov, gov_res = linha, pior_r
        saida.append(linha)
    por_dem = max((x for x in saida if x["demanda"]), key=lambda x: _nota_demanda(g["tipo"], x["demanda"]), default=None)
    if gov is None and not tl:
        gov = por_dem
    chave = (lambda x: dict(no=x["no"], barra=x["barra"], ponta=x["ponta"], combinacao=x["combinacao"]))
    return dict(grupo=grupo, nome=g["nome"], tipo=g["tipo"], tipo_ligacao=tl, membros=saida,
                governante=chave(gov) if gov else None, taxa_max=gov["taxa"] if gov and tl else None,
                ok=all(x.get("ok") for x in saida) if gov and tl else None,
                resultado=gov_res, por_demanda=chave(por_dem) if por_dem else None,
                erros=[f"{x['barra']}/{x['ponta']}: {x['erro']}" for x in saida if x["erro"]])


def resumo_grupos(modelo, calculo=None):
    """Lista dos grupos para a interface; com cálculo, cada grupo vem dimensionado (taxa por membro e governante)."""
    out = []
    for gid, g in _grupos(modelo).items():
        item = dict(id=gid, nome=g["nome"], tipo=g["tipo"], rotulo=TIPOS.get(g["tipo"], (g["tipo"],))[0],
                    assinatura=g.get("assinatura", ""), travado=bool(g.get("travado")), manual=bool(g.get("manual")),
                    projeto=g.get("projeto"), n_membros=len(g["membros"]), membros=[dict(x) for x in g["membros"]],
                    taxa_max=None, ok=None, governante=None, erros=[])
        if calculo is not None:
            r = dimensionar_grupo(modelo, calculo, gid)
            item.update(membros=r["membros"], taxa_max=r["taxa_max"], ok=r["ok"], governante=r["governante"],
                        erros=r["erros"])
        out.append(item)
    return out
