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

import copy
import math

from . import ligacoes as _lig
from . import placa_base as _placa
from . import pontas as _P
from .ligacoes_rigidas import U_RIGIDA
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
    "tubo_tubo_soldado": ("Tubo–tubo soldado", "tubo_tubo_soldado"),
    "perfil_tubo_soldado": ("Perfil–tubo soldado", "perfil_tubo_soldado"),
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
    mb = dict(no=no, barra=b.id, ponta=e["ponta"], tipo=tipo, assinatura=assin, nome=nome,
              apoio=apoio["b"].id if apoio else None)
    # G5: sem tipo rígido para U. Só a viga–pilar na mesa pede tipo rígido; alma do pilar e viga–viga já são
    # propostas flexíveis (chapa simples) com qualquer condição
    if cond == "rigida" and tipo == "viga_pilar_mesa" and m.secoes[b.secao].props.get("tipo") in ("U", "Ue"):
        mb.update(sem_tipo=True, motivo=U_RIGIDA)
    return mb


def _emenda(m, nid, a, c):
    """Uma ligação (membro = a de menor id) entre duas barras; ângulo = desvio da reta (0 = alinhadas)."""
    a, c = sorted((a, c), key=lambda e: e["b"].id)
    cond = "rotulada" if _rotulada(a["b"], a["ponta"]) or _rotulada(c["b"], c["ponta"]) else "rigida"
    ang = int(round(math.degrees(math.acos(max(-1.0, min(1.0, -_dot(a["d"], c["d"])))))))
    mb = _membro(m, nid, a, "emenda", c, ang, cond)
    secs = sorted((a["b"].secao, c["b"].secao))   # assinatura independe da ordem
    mb["assinatura"] = "|".join(str(x) for x in ("emenda", *secs, ang, cond, a["b"].material))
    return mb


def _classificar_no(m, nid, ends, motivos=None):
    """Pontas de barra num nó → membros de ligação (lista). `motivos` (dict): recebe {nid: texto} quando o nó fica
    sem ligação ou alguma ponta é recusada (ex.: ângulo pequeno demais)."""
    recusas = []
    out = _classificar(m, nid, ends, recusas)
    if motivos is not None and (recusas or not out):
        motivos[nid] = "; ".join(recusas) or _motivo_vazio(m, nid, ends)
    return out


def _motivo_vazio(m, nid, ends):
    if len(ends) < 2:
        enc = encosto(m, nid)
        if enc:
            b = enc["barra"]
            return (f"{ends[0]['b'].id} chega em {b} a {enc['distancia'] * 1000:.0f} mm do eixo, mas {b} não tem nó "
                    f"aqui: as barras não estão ligadas na análise — ajuste: ligar {nid} em {b} (divide {b} no ponto)")
        return "ponta livre: 1 barra, sem apoio"
    papeis = sorted(e.get("papel", "?") for e in ends)
    return f"só barras contínuas ou pilar que termina ({', '.join(papeis)}): sem ligação a dimensionar"


_ANG_TELHADO = 60   # ° com o pilar acima do qual a diagonal é do telhado (gusset na viga, G6)
_ANG_MIN = 20   # ° entre a barra e o apoio abaixo do qual a chapa/gusset não cabe (Review Focus 2)


_L_CURTA = 1.5   # m: barra inclinada sobre apoio mais curta que isso é escora (mão-francesa), não pilar


def _comprimento(m, b):
    a, c = m.nos[b.ni], m.nos[b.nj]
    return math.dist((a.x, a.y, a.z), (c.x, c.y, c.z))


def _plano_vertical(e, continuo):
    """Barra de treliça `e` e banzo contínuo num plano vertical (normal quase horizontal)."""
    if not (continuo and e["b"].params.get("trelica")):
        return False
    a, c = e["d"], continuo[0]["d"]
    n = (a[1] * c[2] - a[2] * c[1], a[2] * c[0] - a[0] * c[2], a[0] * c[1] - a[1] * c[0])
    L = math.sqrt(_dot(n, n))
    return L > 1e-9 and abs(n[2]) / L < 0.5


def _anexar(m, nid, e, tipo, ap, out, recusas):
    """Ligação da ponta `e` no apoio `ap`; ângulo < _ANG_MIN → recusada com motivo (não vira ligação)."""
    ang = _ang(e["d"], ap["d"]) if ap else 0
    if ap is not None and ang < _ANG_MIN:
        recusas.append(f"{e['b'].id}: ângulo {ang}° com {ap['b'].id} < {_ANG_MIN}° — {TIPOS[tipo][0].lower()} "
                       "impossível")
        return
    out.append(_membro(m, nid, e, tipo, ap, ang))


def _tubo_em_tubo(m, b, ap):
    """Ramal e receptor tubos circulares, diâmetro do ramal ≤ o do receptor (β ≤ 1)."""
    sb, sa = m.secoes.get(b.secao), m.secoes.get(ap.secao)
    if sb is None or sa is None:
        return False
    pb, pa = sb.props, sa.props
    return (pb.get("tipo") == "tubo_circular" and pa.get("tipo") == "tubo_circular"
            and pb.get("h", 0) <= pa.get("h", 0) + 1e-9)


def _em_tubo(m, b, ap):
    """Perfil (I, U, tubo, retangular) encostado num tubo (circular ou retangular) que não é o caso tubo–tubo redondo:
    solda direta no contorno com recorte (perfil_tubo_soldado)."""
    sb, sa = m.secoes.get(b.secao), m.secoes.get(ap.secao)
    return (sb is not None and sa is not None and sa.props.get("tipo") in ("tubo_circular", "tubo_retangular")
            and sb.props.get("tipo", "I") in _P.FORMAS_RAMAL and not _tubo_em_tubo(m, b, ap))


def _classificar(m, nid, ends, recusas):
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
    tubo_ap = continuo[0] if continuo else apoio_pilar
    for e in ends:
        if id(e) in passa:
            continue
        b, papel = e["b"], e["papel"]
        if not apoiado and tubo_ap is not None and tubo_ap is not e and _tubo_em_tubo(m, b, tubo_ap["b"]):
            # v0.6 A3: tubo redondo na superfície de tubo redondo ≥ = solda direta com boca de peixe (não gusset)
            _anexar(m, nid, e, "tubo_tubo_soldado", tubo_ap, out, recusas)
            continue
        if not apoiado and tubo_ap is not None and tubo_ap is not e and _em_tubo(m, b, tubo_ap["b"]):
            # viga ou diagonal encostada num pilar/banzo tubular: filete no contorno com recorte (sem chapa)
            _anexar(m, nid, e, "perfil_tubo_soldado", tubo_ap, out, recusas)
            continue
        if apoiado and (papel != "diagonal" or so_trelica and not b.params.get("trelica")):
            # barra inclinada única sobre apoio (classe "diagonal" pela geometria) também é base
            cond = "rotulada" if _rotulada(b, e["ponta"]) or not (no.apoio[3] and no.apoio[4]) else "rigida"
            mb = _membro(m, nid, e, "base", None, _ang(e["d"], (0, 0, 1)), cond)
            if papel == "diagonal" and _comprimento(m, b) < _L_CURTA:
                # ponytail: só comprimento; checar perfil de pilar se aparecer escora longa
                mb["aviso"] = "barra inclinada sobre apoio: chapa de apoio, não placa de base de pilar — conferir"
            out.append(mb)
        elif papel == "diagonal":
            ap = (continuo[0] if continuo else apoio_pilar or next((x for x in ends if x["papel"] == "viga"), None))
            # G6: diagonal quase horizontal no topo do pilar (contraventamento do telhado no beiral): o gusset fica no
            # plano do telhado, na viga mais pesada do nó (tesoura), não num canto viga–pilar vertical
            if ap is not None and not apoiado and ap["papel"].startswith("pilar") and _ang(e["d"], ap["d"]) > _ANG_TELHADO:
                vs = [x for x in ends if x["papel"] == "viga" and _ang(e["d"], x["d"]) >= _ANG_MIN]
                if vs:
                    ap = dict(max(vs, key=lambda x: (m.secoes[x["b"].secao].A, not _rotulada(x["b"], x["ponta"]))),
                              papel="viga")
            # treliça com banzo contínuo comum (barra "trelica" sobre banzo, sem pilar, plano vertical); o
            # contraventamento em X da cobertura também é "trelica", mas fica no plano do telhado → gusset
            tipo = ("no_trelica" if so_trelica or _plano_vertical(e, continuo) and not pilares
                    else "gusset_contraventamento")
            if ap is not None and apoiado:
                ap = dict(ap, papel="base")   # gusset no pé do pilar ≠ gusset no topo
            _anexar(m, nid, e, tipo, ap, out, recusas)
        elif papel == "viga":
            if apoio_pilar is not None:
                _ex, ey, ez = eixos_locais(m, apoio_pilar["b"].id)
                tipo = "viga_pilar_mesa" if abs(_dot(e["d"], ez)) >= abs(_dot(e["d"], ey)) else "viga_pilar_alma"
                out.append(_membro(m, nid, e, tipo, apoio_pilar, _ang(e["d"], apoio_pilar["d"])))
            else:
                ap = continuo[0] if continuo else max((x for x in ends if x is not e and x["papel"] == "viga"),
                                                      key=lambda x: m.secoes[x["b"].secao].A, default=None)
                _anexar(m, nid, e, "viga_viga", ap, out, recusas)
        # pilar que termina sem apoio (topo, pilar sobre viga): o pilar é o apoio das vigas; sem ligação própria
    return out


def membros(modelo, motivos=None):
    """Todas as ligações do modelo, sem agrupar: [{no, barra, ponta, tipo, assinatura, nome}].
    `motivos` (dict, opcional) recebe {nó: motivo} dos nós sem ligação ou com ponta recusada."""
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
            out += _classificar_no(m, nid, pontas[nid], motivos)
    return _nos_proximos(m, out, motivos)


_ENCOSTO_FOLGA = 0.050   # m além da meia seção: ponta desenhada até a face (ou um pouco dentro/fora) da barra vizinha
_TOL_NO = 1e-3


def encosto(m, nid):
    """Nó com UMA ponta de barra e sem apoio que cai sobre o vão de outra barra de aço (a ≤ meia seção + 50 mm do eixo)
    sem nó ali: a barra foi desenhada até a coluna/viga, mas não está ligada na análise.
    → {no, barra, ponto [x,y,z] no eixo da barra, t (0–1 ao longo dela), distancia (m)} ou None."""
    n = m.nos[nid]
    if _apoiado(n):
        return None
    donas = [b for b in m.barras.values() if nid in (b.ni, b.nj) and b.tipo != "concreto"]
    if len(donas) != 1:
        return None
    p = (n.x, n.y, n.z)
    melhor = None
    for b in m.barras.values():
        if b.tipo == "concreto" or b is donas[0] or nid in (b.ni, b.nj):
            continue
        a, c = m.nos[b.ni], m.nos[b.nj]
        v = (c.x - a.x, c.y - a.y, c.z - a.z)
        L = math.sqrt(_dot(v, v))
        if L <= 0:
            continue
        t = _dot((p[0] - a.x, p[1] - a.y, p[2] - a.z), v) / (L * L)
        if not (_TOL_NO < t * L < L - _TOL_NO):   # ponytail: perto de um nó da barra = outro caso (unir nós), não tratado
            continue
        q = (a.x + t * v[0], a.y + t * v[1], a.z + t * v[2])
        d = math.dist(p, q)
        pr = m.secoes[b.secao].props if b.secao in m.secoes else {}
        if d <= max(pr.get("h", 0.0), pr.get("b", 0.0)) / 2 + _ENCOSTO_FOLGA and (melhor is None or d < melhor["distancia"]):
            melhor = dict(no=nid, barra=b.id, ponto=list(q), t=t, distancia=d)
    return melhor


def ligar_no(modelo, dados):
    """Ajuste oferecido pelo gerenciador para o nó com `encosto`: leva o nó ao eixo da barra vizinha e divide essa
    barra nele (cargas reinterpoladas, rótulas de ponta só nos trechos das pontas — geradores_extra._Obra.dividir).
    Muda a estrutura: o cálculo anterior deixa de valer. → {no, barra, barras: [ids novos]}"""
    from ..geradores_extra import _Obra
    nid = (dados or {}).get("no")
    if nid not in modelo.nos:
        raise ValueError(f"nó '{nid}' não existe")
    enc = encosto(modelo, nid)
    if enc is None or (dados.get("barra") not in (None, enc["barra"])):
        raise ValueError(f"nó {nid}: nenhuma barra passa junto dele sem nó (nada a ligar)")
    n = modelo.nos[nid]
    n.x, n.y, n.z = enc["ponto"]
    b = modelo.barras[enc["barra"]]
    obra = _Obra(modelo, "")
    novos = obra.dividir(b, [nid])
    modelo.barras.pop(b.id)
    if obra.cargas_removidas:
        modelo.cargas_distribuidas[:] = [c for c in modelo.cargas_distribuidas if id(c) not in obra.cargas_removidas]
        modelo.cargas_pontuais[:] = [c for c in modelo.cargas_pontuais if id(c) not in obra.cargas_removidas]
    for o in (*obra.barras.values(), *obra.cargas_novas):
        modelo.add(o)
    return dict(no=nid, barra=b.id, barras=[x[0] for x in novos])


_D_PROXIMOS = 0.200   # m: nós mais perto que isso com chapa nos dois → uma ligação só (G5)
_CHAPA_NO = ("gusset_contraventamento", "emenda", "no_trelica")


def _celula(n):
    return tuple(int(math.floor(c / _D_PROXIMOS)) for c in (n.x, n.y, n.z))


def _nos_proximos(m, out, motivos):
    """Dois nós a < 200 mm com chapa (gusset/emenda/nó de treliça) nos dois: as chapas se sobrepõem. Fica a ligação
    do nó com mais pontas (empate: o primeiro do modelo); o outro perde as chapas e recebe o motivo."""
    por_no = {}
    for mb in out:
        if mb["tipo"] in _CHAPA_NO:
            por_no.setdefault(mb["no"], []).append(mb)
    grade = {}   # ponytail: grade de células de 200 mm, vizinhança 3×3×3
    for nid in por_no:
        grade.setdefault(_celula(m.nos[nid]), []).append(nid)
    ordem = {k: i for i, k in enumerate(m.nos)}
    fora = set()
    for nid in sorted(por_no, key=lambda k: (-len(por_no[k]), ordem[k])):
        if nid in fora:
            continue
        n = m.nos[nid]
        c = _celula(n)
        for d in ((i, j, k) for i in (-1, 0, 1) for j in (-1, 0, 1) for k in (-1, 0, 1)):
            for outro in grade.get((c[0] + d[0], c[1] + d[1], c[2] + d[2]), ()):
                o = m.nos[outro]
                if outro != nid and outro not in fora and math.dist((n.x, n.y, n.z), (o.x, o.y, o.z)) < _D_PROXIMOS:
                    fora.add(outro)
                    if motivos is not None:
                        motivos[outro] = f"nós a <200 mm: ligação única (no nó {nid})"
    return [mb for mb in out if not (mb["no"] in fora and mb["tipo"] in _CHAPA_NO)]


# ---------------------------------------------------------------- grupos

def _grupos(modelo):
    lig = modelo.ligacoes if isinstance(modelo.ligacoes, dict) else {}
    modelo.ligacoes = lig
    return lig.setdefault("grupos", {})


def _novo_id(grupos):
    n = max([int(k[1:]) for k in grupos if k[:1] == "G" and k[1:].isdigit()] + [0]) + 1
    return f"G{n}"


_APOIO_FLEX = {"viga_pilar_mesa": "mesa_pilar", "viga_pilar_alma": "alma_pilar", "viga_viga": "alma_viga"}


def _projeto_padrao(tipo, modelo=None, n=None):
    sug = TIPOS[tipo][1]
    if sug == "placa_base":
        return dict(tipo_ligacao=sug, dados=dict(pelo_perfil=True))
    if sug == "perfil_tubo_soldado" and modelo is not None and n and n.get("apoio"):
        return dict(tipo_ligacao=sug, dados=dados_perfil_tubo(modelo, n))
    if modelo is not None and n and tipo in _APOIO_FLEX and not n.get("sem_tipo") and n.get("apoio"):
        return _solda_direta_padrao(modelo, n, tipo)
    return None


def dados_perfil_tubo(modelo, n):
    """Entradas de perfil_tubo_soldado (mm, °) tiradas das seções do modelo: perfil da ponta, tubo de apoio, ângulo entre
    os eixos e o lado do tubo retangular voltado para a barra (D) e a largura dessa face (B)."""
    b, ap = modelo.barras[n["barra"]], modelo.barras[n["apoio"]]
    pb, pa = modelo.secoes[b.secao].props, modelo.secoes[ap.secao].props
    u, a = _dir(modelo, b, n["ponta"]), _dir(modelo, ap, "i")
    if _dot(u, a) < 0:
        a = tuple(-x for x in a)
    _ex, _ey, zb = eixos_locais(modelo, b.id)   # z local da barra = altura h (igual nas duas pontas)
    yb = (zb[1] * u[2] - zb[2] * u[1], zb[2] * u[0] - zb[0] * u[2], zb[0] * u[1] - zb[1] * u[0])   # y = z × x
    tw = pb.get("tw", pb.get("t", 0.0))
    d = dict(forma_ramal=pb.get("tipo", "I"), h_ramal=pb["h"] * 1e3, b_ramal=pb.get("b", pb["h"]) * 1e3,
             tw_ramal=tw * 1e3, tf_ramal=pb.get("tf", tw) * 1e3, forma_receptor=pa["tipo"],
             t_receptor=pa.get("t", pa.get("tw", 0.0)) * 1e3, theta=max(1, _ang(u, a)),
             giro=round(math.degrees(math.atan2(_dot(a, yb), _dot(a, zb))), 3))
    if pa["tipo"] == "tubo_circular":
        d.update(D_receptor=pa["h"] * 1e3)
    else:   # lado do tubo na direção da barra (⊥ ao eixo): eixo local z do tubo = altura h
        _ex, ey, ez = eixos_locais(modelo, ap.id)
        up = tuple(x - _dot(u, a) * y for x, y in zip(u, a))
        alto = abs(_dot(up, ez)) >= abs(_dot(up, ey))
        d.update(D_receptor=(pa["h"] if alto else pa["b"]) * 1e3, B_receptor=(pa["b"] if alto else pa["h"]) * 1e3)
    return d


def _solda_direta_padrao(modelo, n, tipo):
    """G5: viga U baixa demais até para 1 parafuso (UPN 50/65) → projeto padrão solda_direta_alma."""
    from . import ligacoes_flexiveis as F
    sec = modelo.barras[n["barra"]].secao
    try:
        vg = F._perfil(sec, "perfil_viga")
    except ValueError:
        return None
    if vg.get("forma") != "U" or F._furos_viga(vg, F._padrao_gerdau(vg["d"]), 1) is not None:
        return None
    return dict(tipo_ligacao="solda_direta_alma", dados=dict(perfil_viga=sec, apoio=_APOIO_FLEX[tipo],
                                                             perfil_suporte=modelo.barras[n["apoio"]].secao))


def _mb(n):
    return dict(no=n["no"], barra=n["barra"], ponta=n["ponta"])


def identificar(modelo):
    """(Re)identifica as ligações e atualiza `modelo.ligacoes` guardando as edições do usuário:
    - membro que sumiu do modelo sai do grupo;
    - grupo travado ou manual mantém os membros que ainda existem; nos outros, membro cuja assinatura mudou sai;
    - membro sem grupo vai para o primeiro grupo não travado com a mesma assinatura, ou para um grupo novo;
    - grupo vazio é apagado. → {"grupos": n, "membros": n, "novos": [ids]}"""
    sem_tipo = {}
    novos = membros(modelo, sem_tipo)
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
                                      projeto=_projeto_padrao(n["tipo"], modelo, n), travado=False, manual=False)
            criados.append(gid)
        alvo["membros"].append(_mb(n))
        colocados.add(k)
    for gid in [k for k, g in grupos.items() if not g["membros"]]:
        del grupos[gid]
    # ajuste oferecido (ligar_no) para cada nó sem ligação que encosta numa barra sem nó compartilhado
    ajustes = {nid: enc for nid in sem_tipo if (enc := encosto(modelo, nid))}
    return dict(grupos=len(grupos), membros=len(colocados), novos=[g for g in criados if g in grupos],
                sem_tipo=sem_tipo, ajustes=ajustes)


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
                  "tubo_tubo_soldado": ("N", "V", "M"),
                  "emenda": ("M", "V", "N")}  # vigas: V, N, M


def _nota_demanda(tipo, dm):
    return tuple(dm[k] for k in _ORDEM_DEMANDA.get(tipo, ("V", "N", "M")))


# esforços do modelo → esforços pedidos por cada tipo de `aco.ligacoes` (módulos de cálculo, kN)
_ESFORCOS_LIG = {
    "parafuso": lambda e: dict(Ft=max(e["N"], 0.0), Fv=math.hypot(e["Vy"], e["Vz"])),
    "solda_filete": lambda e: dict(F=math.sqrt(e["N"] ** 2 + e["Vy"] ** 2 + e["Vz"] ** 2)),
    "chapa_simples": lambda e: dict(V=math.hypot(e["Vy"], e["Vz"])),
    "ligacao_tracao": lambda e: dict(N=_so_tracao(e["N"])),
}


def _so_tracao(N):
    """ligacao_tracao só verifica tração (sem flambagem da chapa, 6.5.4): compressão não é aprovada por ela."""
    if N < 0:
        raise ValueError(f"ligacao_tracao: N = {N:.1f} kN é compressão — compressão não verificada por este tipo "
                         "(sem flambagem da chapa, NBR 8800 6.5.4); use gusset_contraventamento")
    return N


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
        # o nó de treliça exige N1–N4 simultâneos; a ponta só fornece o N da barra 1 (N2–N4 virariam 0/45 kN)
        if tl == "no_trelica_gusset" and any((dados or {}).get(f"secao_{i}", "nenhum") != "nenhum"
                                             or (dados or {}).get(f"arranjo_{i}", "nenhuma") != "nenhuma" for i in "234"):   # W8: esquema novo
            raise ValueError("no_trelica_gusset com várias barras: os esforços simultâneos N2–N4 não são obtidos do "
                             "modelo — verifique o nó pela janela da ligação com os esforços de todas as barras")
        r = _lig.verificar_ligacao(tl, dados, esforcos_contrato(e))
        r.setdefault("taxa_max", r.get("taxa"))
        return r
    return f


def _taxa(r):
    t = r.get("taxa_max")
    return math.inf if t is None or not math.isfinite(t) else float(t)


def _apoio_no(modelo, mb):
    """Perfil do apoio da ponta no nó: a barra de apoio escolhida por _classificar_no (contínua > pilar > viga
    suporte); se ela não indica nenhuma, a outra barra do nó de maior altura; None se não há."""
    ends = [dict(b=b, ponta=p) for b in modelo.barras.values() if b.tipo != "concreto"
            for p, n in (("i", b.ni), ("j", b.nj)) if n == mb["no"]]
    lig = next((x for x in _classificar_no(modelo, mb["no"], ends)
                if x["barra"] == mb["barra"] and x["ponta"] == mb["ponta"]), None)
    if lig and lig.get("apoio") is not None:
        return modelo.secoes[modelo.barras[lig["apoio"]].secao].props
    ps = [modelo.secoes[b.secao].props for b in modelo.barras.values()
          if b.id != mb["barra"] and mb["no"] in (b.ni, b.nj)]
    return max((p for p in ps if "h" in p and "b" in p), key=lambda p: p["h"], default=None)


def _contexto_base(modelo, tipo_no, mb):
    b = modelo.barras[mb["barra"]]
    a, c = modelo.nos[b.ni], modelo.nos[b.nj]
    # comprimento livre: entre nós menos os trechos rígidos das pontas (params desloc_i/desloc_j, m)
    livre = math.dist((a.x, a.y, a.z), (c.x, c.y, c.z)) - float(b.params.get("desloc_i", 0.0)) -         float(b.params.get("desloc_j", 0.0))
    ctx = dict(tipo_no="gusset_contraventamento" if tipo_no == "no_trelica" else tipo_no, comprimento=livre)
    ap = _apoio_no(modelo, mb)
    if ap is not None:
        ctx["perfil_apoio"] = ap
    return ctx


def _recuo_ponta(modelo, barra, ponta):
    """Recuo (m) da ponta (barra, ponta) pelo projeto do grupo dela; 0 sem projeto."""
    for g in _grupos(modelo).values():
        mb = next((m for m in g["membros"] if m["barra"] == barra and m["ponta"] == ponta), None)
        proj = g.get("projeto") or {}
        if mb is None or not proj.get("tipo_ligacao"):
            continue
        if proj["tipo_ligacao"] == "placa_base":
            return float((proj.get("dados") or {}).get("esp") or 0.0)
        try:
            pts = _lig.pontas_de(proj["tipo_ligacao"], proj.get("dados"),
                                 dict(_contexto_base(modelo, g["tipo"], mb), comprimento=math.inf))
        except ValueError:
            return 0.0
        return pts[0]["plano"]["ponto"][0] if pts else 0.0
    return 0.0


def _contexto_pontas(modelo, tipo_no, mb):
    """Geometria real da ponta (aco/pontas.py): tipo do nó, perfil do apoio e comprimento livre da barra menos o recuo
    da outra ponta (barra curta quando recuo_i + recuo_j ≥ comprimento livre)."""
    ctx = _contexto_base(modelo, tipo_no, mb)
    ctx["comprimento"] -= _recuo_ponta(modelo, mb["barra"], "j" if mb["ponta"] == "i" else "i")
    return ctx


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
        linha = dict(mb, taxa=None, ok=None, combinacao=None, demanda=None, esforcos=None, erro=None, motivo=None)
        if not esf:
            linha["erro"] = "sem esforços no cálculo (barra nova ou modelo editado: calcule de novo)"
            saida.append(linha)
            continue
        cd = max(esf, key=lambda n: _nota_demanda(g["tipo"], demanda(esf[n])))
        linha.update(demanda=demanda(esf[cd]), combinacao=cd, esforcos=esf[cd])
        if tl:
            pior, pior_r, pior_c = -1.0, None, None
            dados = dict(proj.get("dados") or {}, contexto_pontas=_contexto_pontas(modelo, g["tipo"], mb))
            try:
                for n, e in esf.items():
                    r = verificador(tl)(modelo, mb, e, dados)
                    if _taxa(r) > pior:
                        pior, pior_r, pior_c = _taxa(r), r, n
            except (ValueError, KeyError, TypeError) as ex:
                linha["erro"] = str(ex.args[0] if ex.args else ex)
            else:
                linha.update(taxa=pior, combinacao=pior_c, esforcos=esf[pior_c], demanda=demanda(esf[pior_c]),
                             ok=bool(pior_r.get("ok")) and pior <= 1.0 + 1e-9, motivo=pior_r.get("motivo"))
                if pior_r.get("excluir_interferencia"):
                    linha["excluir_interferencia"] = True
                if pior_r.get("barra_curta"):   # onda final: não some — o nó fica "sem ligação válida" (gerenciador e memorial)
                    linha.update(barra_curta=True, sem_ligacao_valida=True, ok=False)
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
                erros=[f"{x['barra']}/{x['ponta']}: {x['erro']}" for x in saida if x["erro"]]
                + [f"{x['barra']}/{x['ponta']} (nó {x['no']}): sem ligação válida — {x.get('motivo') or 'barra curta'}"
                   for x in saida if x.get("sem_ligacao_valida")])


def _falha(r):
    """Texto da verificação que reprova o dimensionamento do grupo (para a mensagem de troca recusada)."""
    if r.get("erros"):
        return "; ".join(r["erros"])
    v = [x for x in (r.get("resultado") or {}).get("verificacoes") or [] if x.get("taxa") is not None]
    v = [x for x in v if x.get("ok") is False or x["taxa"] > 1.0 + 1e-9] or v   # a que reprovou, não a maior taxa
    pior = max(v, key=lambda x: x["taxa"], default=None)
    return f"{pior['nome']} (taxa {pior['taxa']:.3f})".replace(".", ",") if pior else (r.get("resultado") or {}).get(
        "motivo") or "não passa"


def aplicar_troca(modelo, calculo, dados):
    """Onda final (G6-a): aplica a troca proposta pelo Realista a UM membro {grupo, barra, no, tipo_ligacao, dados}.
    O membro sai do grupo (dividir) se o grupo tem outros, o projeto vai para o grupo dele e o grupo é verificado com
    os esforços reais (dimensionar_grupo: todas as combinações ELU). Não passa → nada muda e ValueError cita a
    verificação que reprovou. → {grupo, ok, taxa_max, combinacao}"""
    if calculo is None:
        raise ValueError("calcule o modelo antes: a troca é verificada com os esforços reais das combinações ELU")
    d = dict(dados or {})
    gid, tl = d.get("grupo"), d.get("tipo_ligacao")
    if not tl or verificador(tl) is None:
        raise ValueError(f"tipo_ligacao '{tl}' desconhecido")
    g = _grupo(modelo, gid)
    mb = next((x for x in g["membros"] if x["barra"] == d.get("barra") and x["no"] == d.get("no")), None)
    if mb is None:
        raise ValueError(f"barra {d.get('barra')} no nó {d.get('no')} não é membro do grupo {gid}")
    antes = copy.deepcopy(modelo.ligacoes)
    try:
        alvo = gid if len(g["membros"]) == 1 else editar(modelo, dict(acao="dividir", grupo=gid, membros=[mb]))["grupo"]
        editar(modelo, dict(acao="projeto", grupo=alvo, tipo_ligacao=tl, dados=d.get("dados") or {}))
        r = dimensionar_grupo(modelo, calculo, alvo)
        if not r["ok"]:
            raise ValueError(f"troca não aplicada ({tl} em {mb['barra']}, nó {mb['no']}): {_falha(r)}")
    except Exception:
        modelo.ligacoes = antes
        raise
    gov = next((x for x in r["membros"] if x["barra"] == mb["barra"] and x["no"] == mb["no"]), {})
    return dict(grupo=alvo, ok=True, taxa_max=r["taxa_max"], combinacao=gov.get("combinacao"))


def resumo_grupos(modelo, calculo=None):
    """Lista dos grupos para a interface; com cálculo, cada grupo vem dimensionado (taxa por membro e governante)."""
    out = []
    apoio = {_chave(n): n.get("apoio") for n in membros(modelo)}   # G6: barra de apoio de cada membro (não persiste)
    for gid, g in _grupos(modelo).items():
        mbs = [dict(x, **({"apoio": apoio[_chave(x)]} if apoio.get(_chave(x)) else {})) for x in g["membros"]]
        item = dict(id=gid, nome=g["nome"], tipo=g["tipo"], rotulo=TIPOS.get(g["tipo"], (g["tipo"],))[0],
                    assinatura=g.get("assinatura", ""), travado=bool(g.get("travado")), manual=bool(g.get("manual")),
                    projeto=g.get("projeto"), n_membros=len(g["membros"]), membros=mbs,
                    taxa_max=None, ok=None, governante=None, erros=[])
        if calculo is not None:
            r = dimensionar_grupo(modelo, calculo, gid)
            item.update(membros=r["membros"], taxa_max=r["taxa_max"], ok=r["ok"], governante=r["governante"],
                        erros=r["erros"])
            for x in item["membros"]:
                if apoio.get(_chave(x)):
                    x.setdefault("apoio", apoio[_chave(x)])
        out.append(item)
    return out
