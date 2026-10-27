"""Pontas de barra e recortes numa ligação (contrato em docs/v3/PONTAS-CONTRATO.md).

Geometria pura: o motor é a fonte; o render 3D (Render3DGeometria.Juntas) só desenha.
Unidades: metros e radianos. Vetores como listas [x, y, z].

Membro (entrada de pontas_do_no):
  {barra, extremo "i"|"j", dir: unitário saindo do nó ao longo da barra, comprimento,
   perfil: Secao.props (h, b, tw, tf / t; tubo circular: h = b = diâmetro), eixo_d: unitário da altura h,
   continuo: True se a barra é o apoio (passa pelo nó, não recebe Ponta), desloc: [x,y,z] opcional}
"""
import math

FOLGA = 0.012
RAIO_COPE = 0.010
_N_SELA = 72          # amostras do contorno do ramal na boca de peixe


class BarraCurta(ValueError):
    """Recuo + recorte alcançam o comprimento livre da barra."""

    def __init__(self, msg="barra curta demais para a ligação"):
        super().__init__(msg)


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _add(a, b, k=1.0):
    return [x + k * y for x, y in zip(a, b)]


def _cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _unit(a, msg="vetor nulo"):
    n = math.sqrt(_dot(a, a))
    if n < 1e-9:
        raise ValueError(msg)
    return [x / n for x in a]


def _perfil(m, *chaves):
    p = m["perfil"]
    if p.get("tipo") in ("U", "Ue") and "t" in p and not ("tw" in p and "tf" in p):   # U/Ue formado a frio: só t (tw = tf = t)
        p = dict(p, tw=p.get("tw", p["t"]), tf=p.get("tf", p["t"]))
    falta = [k for k in chaves if k not in p]
    if falta:
        raise ValueError(f"perfil da barra {m['barra']} sem {', '.join(falta)}")
    return p


def _meia_largura(m, w):
    """Meia extensão da seção de m na direção w (caixa h × b)."""
    p, d = _perfil(m, "h", "b"), m["eixo_d"]
    e_b = _cross(m["dir"], d)
    return p["h"] / 2 * abs(_dot(d, w)) + p["b"] / 2 * abs(_dot(e_b, w))


def _copes(m, ap, params):
    """Cope em cada mesa da viga que invade a mesa do apoio (viga na alma).

    face: sup/inf quando a mesa invadida está acima/abaixo da viga; esq/dir quando a invasão é
    pelas bordas das mesas da viga (viga mais larga que o vão livre entre as mesas do apoio)."""
    w, pa = ap["eixo_d"], _perfil(ap, "h", "b", "tf", "tw")
    e = _dot(_add(m.get("desloc", [0, 0, 0]), ap.get("desloc", [0, 0, 0]), -1), w)
    ext, livre = _meia_largura(m, w), pa["h"] / 2 - pa["tf"]
    vert = _dot(w, m["eixo_d"])
    lat = _dot(w, _cross(m["dir"], m["eixo_d"]))
    out = []
    for lado in (1, -1):
        if lado * e + ext > livre:
            if abs(vert) >= abs(lat):
                face = "sup" if lado * vert > 0 else "inf"
            else:
                face = "esq" if lado * lat > 0 else "dir"
            out.append(dict(tipo="cope", face=face, altura=pa["tf"] + params["cope_folga_alt"],
                            comprimento=pa["b"] / 2 - pa["tw"] / 2 - params["folga"] + params["cope_folga_comp"],
                            raio=params["raio_cope"]))
    return out


def _gusset(m, ap, folga):
    """Diagonal: recuo no eixo até a face do apoio (borda do gusset) + folga, e recorte `angulo`
    paralelo a essa face, afastado dela pela folga."""
    u, a = m["dir"], ap["dir"]
    c = _dot(u, a)
    s = math.sqrt(max(0.0, 1 - c * c))
    if s < 1e-6:
        raise ValueError("diagonal paralela ao apoio: gusset impossível")
    perp = _unit(_add(u, a, -c))
    ext = _meia_largura(ap, perp)
    recuo = ext / s + _perfil(m, "b")["b"] / 2 * abs(c) / s + folga
    corte = dict(tipo="angulo", normal=[-x for x in perp], folga=folga, distancia=ext + folga)
    return recuo, corte


def _sela(m, ap):
    """Boca de peixe: distâncias (mín, máx) no eixo do ramal até a superfície do receptor, e o ângulo."""
    R, r = _perfil(ap, "h")["h"] / 2, _perfil(m, "h")["h"] / 2
    if r > R:
        raise ValueError("ramal maior que o tubo receptor")
    u, a = m["dir"], ap["dir"]
    c = _dot(u, a)
    s = math.sqrt(max(0.0, 1 - c * c))
    if s < 1e-6:
        raise ValueError("ramal paralelo ao receptor")
    ts = []
    for k in range(_N_SELA):
        cf = math.cos(2 * math.pi * k / _N_SELA)
        # |t·u + r(cosφ e1 + sinφ e2)|² − (p·a)² = R², e1 = (a − c·u)/s
        b = c * r * s * cf
        disc = b * b - s * s * (r * r - (r * s * cf) ** 2 - R * R)
        ts.append((b + math.sqrt(max(0.0, disc))) / (s * s))
    return min(ts), max(ts), math.acos(min(1.0, abs(c)))


FORMAS_RAMAL = ("I", "H", "U", "tubo_retangular", "retangular", "tubo_circular")


def contorno(p):
    """Contorno externo da seção (m) como polígono [(yb, zh)]: yb na largura b, zh na altura h, centro na caixa h × b.
    I/H: 12 pontos (mesas e alma); U: 8 pontos, alma em yb = −b/2, mesas abrindo para +yb; tubo retangular e
    retangular: 4 cantos (raios ignorados); tubo circular: 72 pontos. Outras formas: ValueError."""
    forma = p.get("tipo", "I")
    h, b = p["h"], p.get("b", p["h"])
    if forma == "tubo_circular":
        r = h / 2
        return [(r * math.cos(2 * math.pi * k / _N_SELA), r * math.sin(2 * math.pi * k / _N_SELA)) for k in range(_N_SELA)]
    if forma in ("tubo_retangular", "retangular"):
        return [(-b / 2, -h / 2), (b / 2, -h / 2), (b / 2, h / 2), (-b / 2, h / 2)]
    if forma in ("I", "H", "U"):
        tw, tf = p["tw"], p["tf"]
        if forma == "U":
            return [(-b / 2, -h / 2), (b / 2, -h / 2), (b / 2, -h / 2 + tf), (-b / 2 + tw, -h / 2 + tf),
                    (-b / 2 + tw, h / 2 - tf), (b / 2, h / 2 - tf), (b / 2, h / 2), (-b / 2, h / 2)]
        return [(-b / 2, -h / 2), (b / 2, -h / 2), (b / 2, -h / 2 + tf), (tw / 2, -h / 2 + tf), (tw / 2, h / 2 - tf),
                (b / 2, h / 2 - tf), (b / 2, h / 2), (-b / 2, h / 2), (-b / 2, h / 2 - tf), (-tw / 2, h / 2 - tf),
                (-tw / 2, -h / 2 + tf), (-b / 2, -h / 2 + tf)]
    raise ValueError(f"seção '{forma}' sem contorno para solda em tubo (use I, H, U, tubo ou retangular)")


def contato_tubo(m, ap):
    """Perfil `m` encostado no tubo `ap` (circular: superfície do cilindro; retangular: a face voltada para o perfil).
    Cada ponto do contorno anda ao longo de m.dir até tocar o tubo. → (pontos [[x,y,z]] relativos ao nó,
    t [distância no eixo de cada ponto], beta = largura do perfil no tubo / largura do tubo). ValueError se o perfil não
    cabe no tubo (beta > 1) ou é paralelo a ele."""
    u, a = m["dir"], ap["dir"]
    c = _dot(u, a)
    if 1 - abs(c) < 1e-6:
        raise ValueError("perfil paralelo ao tubo: solda em tubo impossível")
    up = _add(u, a, -c)                     # u ⊥ ao eixo do tubo
    n = _unit(up)                           # do eixo do tubo para o perfil
    lat = _cross(a, n)                      # largura da face (tubo retangular) / corda (circular)
    d, eb = m["eixo_d"], _cross(m["dir"], m["eixo_d"])
    p0s = [_add(_mul(eb, yb), d, zh) for yb, zh in contorno(m["perfil"])]
    pa = ap["perfil"]
    circ = pa.get("tipo") == "tubo_circular"
    larg = pa["h"] if circ else 2 * _meia_largura(ap, lat)
    beta = 2 * max(abs(_dot(p, lat)) for p in p0s) / larg
    if beta > 1 + 1e-9:
        raise ValueError(f"perfil mais largo que o tubo (β = {beta:.2f} > 1): solda em tubo impossível")
    ts = []
    if circ:
        R, s2 = pa["h"] / 2, _dot(up, up)
        for p in p0s:
            pp = _add(p, a, -_dot(p, a))
            B, C = _dot(up, pp), _dot(pp, pp) - R * R
            ts.append((-B + math.sqrt(max(0.0, B * B - s2 * C))) / s2)
    else:
        f = _meia_largura(ap, n)
        un = _dot(u, n)
        ts = [(f - _dot(p, n)) / un for p in p0s]
    return [_add(p, u, t) for p, t in zip(p0s, ts)], ts, beta


def _mul(a, k):
    return [x * k for x in a]


def _curta(dist, m):
    if dist >= m["comprimento"]:
        raise BarraCurta()


def pontas_do_no(no, membros, tipo_ligacao, params=None):
    """Planos de corte e recortes das barras que terminam no nó.

    tipo_ligacao: viga_pilar_mesa | viga_pilar_alma | viga_viga | gusset_contraventamento | esquadria | boca_peixe.
    params (m): x_face (face do apoio informada, G6), folga (0,012), raio_cope (0,010, mínimo 0,010), cope_folga_alt (0,012), cope_folga_comp (0,012).
    ValueError("barra curta demais para a ligação") se o corte (recuo + recorte) alcança o comprimento da barra.
    """
    p = dict(folga=FOLGA, raio_cope=RAIO_COPE, cope_folga_alt=FOLGA, cope_folga_comp=FOLGA)
    p.update(params or {})
    p["raio_cope"] = max(p["raio_cope"], RAIO_COPE)
    folga = p["folga"]
    ap = next((m for m in membros if m.get("continuo")), None)
    if ap is None and tipo_ligacao != "esquadria":
        raise ValueError(f"{tipo_ligacao}: nó sem barra contínua de apoio")
    livres = [m for m in membros if not m.get("continuo")]
    if tipo_ligacao == "esquadria" and len(livres) < 2:
        raise ValueError("esquadria precisa de ao menos duas barras")
    out = []
    for m in livres:
        u = m["dir"]
        recortes, fol = [], folga
        if tipo_ligacao in ("viga_pilar_mesa", "viga_pilar_alma", "viga_viga") and p.get("x_face"):
            dist = p["x_face"] + folga   # G6: face informada (apoio U/composto: o perfil inteiro fica antes dela, sem cope)
        elif tipo_ligacao == "viga_pilar_mesa":
            dist = _perfil(ap, "h")["h"] / 2 + folga
        elif tipo_ligacao in ("viga_pilar_alma", "viga_viga"):
            dist = _perfil(ap, "tw")["tw"] / 2 + folga
            recortes = _copes(m, ap, p)
        elif tipo_ligacao == "gusset_contraventamento":
            dist, corte = _gusset(m, ap, folga)
            recortes = [corte]
        elif tipo_ligacao == "esquadria":
            # bissetriz com o vizinho mais próximo (maior cosseno)
            viz = max((x for x in livres if x is not m), key=lambda x: _dot(x["dir"], u))
            cs = _dot(viz["dir"], u)
            n = _unit(_add(viz["dir"], u, -1), "tubos colineares: esquadria impossível")
            # alcance do corte no eixo: canto externo a (h/2)·cot(α/2)
            meio = math.acos(max(-1.0, min(1.0, cs))) / 2
            _curta(_perfil(m, "h")["h"] / 2 / math.tan(meio), m)
            out.append(dict(barra=m["barra"], extremo=m["extremo"], plano=dict(ponto=list(no), normal=n),
                            folga=0.0, recortes=[dict(tipo="esquadria", normal=n, vizinho=viz["barra"])]))
            continue
        elif tipo_ligacao == "boca_peixe":
            dist, tmax, ang = _sela(m, ap)
            _curta(tmax, m)
            recortes, fol = [dict(tipo="boca_peixe", raio=ap["perfil"]["h"] / 2, eixo=list(ap["dir"]), angulo=ang)], 0.0
        elif tipo_ligacao == "solda_em_tubo":
            # perfil qualquer (I, U, tubo, retangular) encostado no tubo: circular → recorte boca_peixe (o sólido do perfil
            # é cortado pelo cilindro); retangular → plano da face do tubo (corte oblíquo, sem recorte)
            _pts, ts, _beta = contato_tubo(m, ap)   # o cordão (contorno de contato) vai em geometria.soldas
            _curta(max(ts), m)
            ang = math.acos(min(1.0, abs(_dot(u, ap["dir"]))))
            if ap["perfil"].get("tipo") == "tubo_circular":
                out.append(dict(barra=m["barra"], extremo=m["extremo"], plano=dict(ponto=_add(no, u, min(ts)),
                                normal=[-x for x in u]), folga=0.0,
                                recortes=[dict(tipo="boca_peixe", raio=ap["perfil"]["h"] / 2, eixo=list(ap["dir"]),
                                               angulo=ang)]))
            else:
                nf = _unit(_add(u, ap["dir"], -_dot(u, ap["dir"])))
                f = _meia_largura(ap, nf)
                out.append(dict(barra=m["barra"], extremo=m["extremo"], plano=dict(ponto=_add(no, u, f / _dot(u, nf)),
                                normal=[-x for x in nf]), folga=0.0, recortes=[]))
            continue
        else:
            raise ValueError(f"tipo de ligação sem pontas: {tipo_ligacao}")
        _curta(dist + max((c.get("comprimento", 0.0) for c in recortes), default=0.0), m)
        n = u
        if tipo_ligacao in ("viga_pilar_mesa", "viga_pilar_alma"):
            # v0.6 A1: viga inclinada (joelho) cortada no plano da face do pilar (vertical), não perpendicular a ela
            h = _add(u, ap["dir"], -_dot(u, ap["dir"]))
            if 1e-6 < _dot(u, ap["dir"]) ** 2 < 0.75:
                n = _unit(h)
        out.append(dict(barra=m["barra"], extremo=m["extremo"],
                        plano=dict(ponto=_add(no, n, dist) if n is u else _add(no, u, dist / _dot(u, n)), normal=[-x for x in n]),
                        folga=fol, recortes=recortes))
    return out


# ---------------------------------------------------------------- referencial local da ligação (G2)
# Origem no nó; x ao longo da barra que chega (saindo do nó); z = altura h dela (docs/v3/LIGACOES-CONTRATO.md).
_APOIO_LOCAL = {   # tipo → (dir, eixo_d) do apoio contínuo
    "viga_pilar_mesa": ([0.0, 0.0, 1.0], [1.0, 0.0, 0.0]),
    "viga_pilar_alma": ([0.0, 0.0, 1.0], [0.0, 1.0, 0.0]),
    "viga_viga": ([0.0, 1.0, 0.0], [0.0, 0.0, 1.0]),
}


def pontas_locais(tipo_no, barra=None, apoio=None, comprimento=math.inf, theta=None, recuo=None, desloc=None,
                  params=None):
    """Pontas da barra que chega, no referencial local da ligação (m).

    barra/apoio: props (h, b, tw, tf). theta (rad): ângulo diagonal–apoio no gusset (padrão 45°).
    Sem apoio conhecido (emenda, base, componentes): plano a `recuo` do nó (padrão: folga), sem recortes.
    ValueError("barra curta demais para a ligação") como em pontas_do_no."""
    m = dict(barra=0, extremo="i", dir=[1.0, 0.0, 0.0], eixo_d=[0.0, 0.0, 1.0], comprimento=comprimento,
             perfil=barra or {}, desloc=list(desloc or [0.0, 0.0, 0.0]))
    if barra and apoio and tipo_no in _APOIO_LOCAL:
        d, e = _APOIO_LOCAL[tipo_no]
    elif barra and tipo_no == "gusset_contraventamento":
        t = theta if theta is not None else math.pi / 4
        d, e = [math.cos(t), 0.0, math.sin(t)], [-math.sin(t), 0.0, math.cos(t)]
        apoio = apoio or dict(h=0.0, b=0.0)     # ponytail: apoio desconhecido no gusset = linha (só a folga)
    else:
        x = recuo if recuo is not None else (params or {}).get("folga", FOLGA)
        _curta(x, m)
        return [dict(barra=0, extremo="i", plano=dict(ponto=[x, 0.0, 0.0], normal=[-1.0, 0.0, 0.0]),
                     folga=0.0 if recuo is not None else x, recortes=[])]
    ap = dict(barra=1, extremo="i", dir=d, eixo_d=e, perfil=apoio, continuo=True, comprimento=math.inf)
    return pontas_do_no([0.0, 0.0, 0.0], [m, ap], tipo_no, params)


def topo_pilar(no, pilar, outros, chapa=0.0):
    """v0.6 A1: pilar que TERMINA no nó vai até o ponto mais alto das barras que chegam (+ chapa de cabeça).

    pilar: membro (dir saindo do nó para dentro do pilar). outros: membros das demais barras no nó.
    Cada barra é medida no plano da face do pilar (vertical) por onde ela sai: viga horizontal dá h/2; viga inclinada
    (joelho) dá o canto mais alto da seção nesse plano. Devolve a Ponta (plano além do nó, normal para fora do material)
    ou None sem barra para cobrir."""
    a = pilar["dir"]
    up = [-x for x in a]
    d0 = pilar.get("desloc", [0, 0, 0])
    ext = None
    for m in outros:
        u = m["dir"]
        h = _add(u, a, -_dot(u, a))
        if _dot(h, h) < 1e-12:
            continue
        h = _unit(h)
        f = _meia_largura(pilar, h)                          # face do pilar na direção horizontal da barra
        p, ed = _perfil(m, "h", "b"), m["eixo_d"]
        eb = _cross(u, ed)
        off = _add(m.get("desloc", [0, 0, 0]), d0, -1)
        for sh in (1, -1):
            for sb in (1, -1):
                k = _add(_add(off, ed, sh * p["h"] / 2), eb, sb * p["b"] / 2)
                t = (f - _dot(k, h)) / _dot(u, h)
                z = _dot(_add(k, u, t), up)
                ext = z if ext is None else max(ext, z)
    if ext is None or ext <= 0:
        return None
    ext += chapa
    return dict(barra=pilar["barra"], extremo=pilar["extremo"], plano=dict(ponto=_add(no, up, ext), normal=up),
                folga=0.0, recortes=[], prolongamento=ext)


def ponta_sobre_placa(no, barra, esp, vertical=(0.0, 0.0, 1.0)):
    """v0.6 A2: barra (vertical ou inclinada) sobre a placa de base horizontal de espessura esp.

    A placa fica com a face de baixo no nível do nó e centrada na projeção da barra; a barra é cortada no plano
    da face de cima (corte em ângulo assentando na placa). ValueError se a barra não sobe da placa."""
    up = _unit(list(vertical))
    u = barra["dir"]
    c = _dot(u, up)
    if c < 0.2:
        raise ValueError("barra deitada ou descendo: não assenta na placa de base")
    topo = _add(no, u, esp / c)
    return dict(barra=barra["barra"], extremo=barra["extremo"], plano=dict(ponto=topo, normal=[-x for x in up]),
                folga=0.0, recortes=[], placa=dict(centro=_add(topo, up, -esp), normal=up))


def largura_util(ponta, b):
    """Largura das mesas na ponta: b menos os copes laterais (esq/dir)."""
    return b - sum(c["altura"] for c in ponta["recortes"] if c["tipo"] == "cope" and c["face"] in ("esq", "dir"))
