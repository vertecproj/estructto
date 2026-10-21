"""Pontas de barra e recortes numa ligação (contrato em docs/v3/PONTAS-CONTRATO.md).

Geometria pura: o motor é a fonte; o render 3D (Render3DGeometria.Juntas) só desenha.
Unidades: metros. Vetores como listas [x, y, z].

Membro (entrada de pontas_do_no):
  {barra, extremo "i"|"j", dir: unitário saindo do nó ao longo da barra, comprimento,
   perfil: Secao.props (tipo, h, b, tw, tf / t), eixo_d: unitário da altura h do perfil,
   continuo: True se a barra é o apoio (passa pelo nó, não recebe Ponta), desloc: [x,y,z] opcional}
"""
import math

FOLGA = 0.012
RAIO_COPE = 0.010


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _add(a, b, k=1.0):
    return [x + k * y for x, y in zip(a, b)]


def _cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _unit(a):
    n = math.sqrt(_dot(a, a))
    return [x / n for x in a]


def _meia_largura(m, w):
    """Meia extensão da seção de m na direção w (caixa h × b)."""
    p, d = m["perfil"], m["eixo_d"]
    e_b = _cross(m["dir"], d)
    return p["h"] / 2 * abs(_dot(d, w)) + p["b"] / 2 * abs(_dot(e_b, w))


def _copes(m, ap, folga, raio):
    """Cope quando as mesas da viga atravessam a mesa do apoio (viga na alma)."""
    w, pa = ap["eixo_d"], ap["perfil"]
    e = _dot(_add(m.get("desloc", [0, 0, 0]), ap.get("desloc", [0, 0, 0]), -1), w)
    ext, livre = _meia_largura(m, w), pa["h"] / 2 - pa["tf"]
    out = []
    for lado in (1, -1):
        if lado * e + ext > livre:
            sup = lado * _dot(w, m["eixo_d"]) > 0
            out.append(dict(tipo="cope", face="sup" if sup else "inf", altura=pa["tf"] + folga,
                            comprimento=pa["b"] / 2 + folga, raio=raio))
    return out


def _recuo_gusset(m, ap, folga):
    """Diagonal: até a face do apoio (borda do gusset soldado nele) + folga, medido no eixo da diagonal."""
    u, a = m["dir"], ap["dir"]
    c = _dot(u, a)
    s = math.sqrt(max(0.0, 1 - c * c))
    if s < 1e-9:
        raise ValueError("diagonal paralela ao apoio")
    perp = _unit(_add(u, a, -c))
    return _meia_largura(ap, perp) / s + m["perfil"]["b"] / 2 * abs(c) / s + folga


def pontas_do_no(no, membros, tipo_ligacao, params=None):
    """Planos de corte e recortes das barras que terminam no nó.

    tipo_ligacao: viga_pilar_mesa | viga_pilar_alma | viga_viga | gusset_contraventamento | esquadria | boca_peixe.
    params: folga (m, padrão 0,012), raio_cope (m, padrão 0,010).
    Levanta ValueError("barra curta demais para a ligação") se recuo + recorte ≥ comprimento.
    """
    params = params or {}
    folga = params.get("folga", FOLGA)
    raio = max(params.get("raio_cope", RAIO_COPE), RAIO_COPE)
    ap = next((m for m in membros if m.get("continuo")), None)
    out = []
    for m in membros:
        if m.get("continuo"):
            continue
        u = m["dir"]
        recortes = []
        if tipo_ligacao == "viga_pilar_mesa":
            dist = ap["perfil"]["h"] / 2 + folga
        elif tipo_ligacao in ("viga_pilar_alma", "viga_viga"):
            dist = ap["perfil"]["tw"] / 2 + folga
            recortes = _copes(m, ap, folga, raio)
        elif tipo_ligacao == "gusset_contraventamento":
            dist = _recuo_gusset(m, ap, folga)
        elif tipo_ligacao == "esquadria":
            # ponytail: só o par de tubos; nó com 3+ tubos sem contínuo fica para o G3
            outro = next(x for x in membros if x is not m)
            n = _unit(_add(outro["dir"], u, -1))   # bissetriz, aponta para fora da barra
            out.append(dict(barra=m["barra"], extremo=m["extremo"], plano=dict(ponto=list(no), normal=n),
                            folga=0.0, recortes=[dict(tipo="esquadria", normal=n)]))
            continue
        elif tipo_ligacao == "boca_peixe":
            # ponytail: ramal perpendicular ao receptor; ramal inclinado precisa da sela geral
            R, r = ap["perfil"]["h"] / 2, m["perfil"]["h"] / 2
            dist = math.sqrt(max(0.0, R * R - r * r))
            recortes = [dict(tipo="boca_peixe", raio=R, eixo=list(ap["dir"]))]
        else:
            raise ValueError(f"tipo de ligação sem pontas: {tipo_ligacao}")
        if dist + max((c.get("comprimento", 0.0) for c in recortes), default=0.0) >= m["comprimento"]:
            raise ValueError("barra curta demais para a ligação")
        out.append(dict(barra=m["barra"], extremo=m["extremo"],
                        plano=dict(ponto=_add(no, u, dist), normal=[-x for x in u]),
                        folga=folga if tipo_ligacao != "boca_peixe" else 0.0, recortes=recortes))
    return out
