"""Membro (peça física): barras colineares com o mesmo `params["membro"]` formam uma peça contínua.

O solver só liga barras nas pontas, então uma viga que recebe outra no meio vira dois elementos. Sem o membro, cada
elemento era verificado com o próprio comprimento (flambagem, FLT e flecha pela metade: contra a segurança) e o nó
interno virava "emenda". Com o membro (docs/MEMBROS.md):
- flambagem e FLT: comprimento entre os nós em que outra barra (ou apoio) trava o deslocamento naquele eixo local;
- flecha: vão entre os nós que travam o eixo z local (apoios reais), medida em relação à corda desse vão;
- ligações: nó interno do membro é passante (sem emenda; a barra que chega apoia nele).

Travamento: no nó interno, outra barra trava o eixo e_k (ey ou ez local) se |cos(barra, e_k)| >= _TRAVA; apoio de
translação trava e_k se |e_k[g]| >= _TRAVA. ponytail: a barra que chega trava sempre, mesmo se a outra ponta dela
estiver solta (balanço ou contraventamento sem continuidade); o usuário reduz com params Lb/kx/ky se precisar.
"""
from __future__ import annotations

import math

from .solver import eixos_locais

_COLINEAR = math.cos(math.radians(1.0))
_TRAVA = 0.25


def _xyz(m, nid):
    n = m.nos[nid]
    return (n.x, n.y, n.z)


def _unit(a, b):
    d = tuple(y - x for x, y in zip(a, b))
    L = math.sqrt(sum(x * x for x in d))
    return tuple(x / L for x in d) if L > 0 else (0.0, 0.0, 0.0), L


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _ordenar(m, bs):
    """Barras do membro em ordem ao longo da peça → [(barra, sentido +1/-1, s0, s1)] ou None se não forem uma
    cadeia simples e colinear."""
    adj = {}
    for b in bs:
        adj.setdefault(b.ni, []).append(b)
        adj.setdefault(b.nj, []).append(b)
    pontas = [n for n, l in adj.items() if len(l) == 1]
    if len(pontas) != 2 or any(len(l) > 2 for l in adj.values()):
        return None
    no, usadas, cad, s = min(pontas), set(), [], 0.0
    d0 = None
    while True:
        prox = [b for b in adj[no] if b.id not in usadas]
        if not prox:
            break
        b = prox[0]
        usadas.add(b.id)
        outro = b.nj if b.ni == no else b.ni
        d, L = _unit(_xyz(m, no), _xyz(m, outro))
        if d0 is None:
            d0 = d
        elif _dot(d, d0) < _COLINEAR:
            return None
        cad.append((b, 1 if b.ni == no else -1, s, s + L))
        s += L
        no = outro
    return cad if len(usadas) == len(bs) else None


def cadeias(m, invalidos=None):
    """{membro: [(barra, sentido, s0, s1)]} dos membros com 2+ barras em cadeia colinear. `invalidos` (lista)
    recebe os ids de membro que não formam uma cadeia (esses ficam barra a barra)."""
    grupos = {}
    for b in m.barras.values():
        mid = b.params.get("membro")
        if mid not in (None, ""):
            grupos.setdefault(str(mid), []).append(b)
    out = {}
    for mid, bs in grupos.items():
        if len(bs) < 2:
            continue
        cad = _ordenar(m, bs)
        if cad is None:
            if invalidos is not None:
                invalidos.append(mid)
            continue
        out[mid] = cad
    return out


def _travas(m, no, cadeia_ids, ey, ez, por_no):
    ty = tz = False
    p = _xyz(m, no)
    for b in por_no.get(no, ()):
        if b.id in cadeia_ids:
            continue
        d, L = _unit(p, _xyz(m, b.nj if b.ni == no else b.ni))
        if L <= 0:
            continue
        ty |= abs(_dot(d, ey)) >= _TRAVA
        tz |= abs(_dot(d, ez)) >= _TRAVA
    ap = m.nos[no].apoio
    for g in range(3):
        if ap[g]:
            ty |= abs(ey[g]) >= _TRAVA
            tz |= abs(ez[g]) >= _TRAVA
    return ty, tz


def _segmentos(cad, travado):
    """Cortes nos nós internos travados → [(s0, s1, [barras])]."""
    segs, atual, s0 = [], [], cad[0][2]
    for k, (b, _sent, _a, s1) in enumerate(cad):
        atual.append(b.id)
        if k == len(cad) - 1 or travado[k]:
            segs.append((s0, s1, atual))
            atual, s0 = [], s1
    return segs


def trechos(m):
    """{barra: {membro, L, s0, s1, sentido, cadeia, y: (s0, s1, [barras]), z: (...)}} para as barras de membros.
    `y`/`z`: trecho do membro, entre nós que travam o deslocamento no eixo local y/z, que contém a barra.
    Flambagem: KLx (eixo forte da seção = y local, desloca em z) usa `z`; KLy, Lb e Lz usam `y`."""
    cads = cadeias(m)
    if not cads:
        return {}
    por_no = {}
    for b in m.barras.values():
        por_no.setdefault(b.ni, []).append(b)
        por_no.setdefault(b.nj, []).append(b)
    out = {}
    for mid, cad in cads.items():
        _ex, ey, ez = eixos_locais(m, cad[0][0].id)
        ids = {b.id for b, *_ in cad}
        ty, tz = [], []
        for b, sent, _s0, _s1 in cad[:-1]:
            fim = b.nj if sent > 0 else b.ni
            a, c = _travas(m, fim, ids, ey, ez, por_no)
            ty.append(a)
            tz.append(c)
        ty.append(True)
        tz.append(True)
        sy, sz = _segmentos(cad, ty), _segmentos(cad, tz)
        Lm = cad[-1][3]
        ordem = [b.id for b, *_ in cad]
        for b, sent, s0, s1 in cad:
            out[b.id] = dict(membro=mid, L=Lm, s0=s0, s1=s1, sentido=sent, cadeia=ordem,
                             y=next(s for s in sy if b.id in s[2]), z=next(s for s in sz if b.id in s[2]))
    return out


def concatenar(tr, seg, linhas_de, valor):
    """(xs, vs) do trecho `seg` = (s0, s1, [barras]) com x medido do início do trecho, crescente."""
    s_ini = seg[0]
    pts = []
    for bid in seg[2]:
        t = tr[bid]
        Lb = t["s1"] - t["s0"]
        for l in linhas_de(bid):
            x = float(l[0])
            s = t["s0"] + (x if t["sentido"] > 0 else Lb - x) - s_ini
            pts.append((s, valor(l)))
    pts.sort(key=lambda p: p[0])
    return [p[0] for p in pts], [p[1] for p in pts]
