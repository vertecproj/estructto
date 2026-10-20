"""Propriedades geométricas de seções. Unidades: m (ver ESPEC.md).

Convenção (notação da NBR 8800): eixo x = eixo de maior inércia, y = menor.
Secao.Iy (eixo local y da barra) recebe Ix (forte); Secao.Iz recebe Iy (fraco).
props: tipo, h, b, tw, tf, r, Ix, Iy, Wx, Wy, Zx, Zy, rx, ry, J, Cw, x0, y0
(centro de cisalhamento em relação ao centroide), relações b/t.
Ay, Az (m²): áreas de cisalhamento p/ Timoshenko no solver. Ay ↔ Vy (flexão em Iz = eixo fraco da
norma); Az ↔ Vz (flexão em Iy do solver = eixo forte). Perfil I: Az = alma, Ay = mesas.
"""
from __future__ import annotations

import csv
import math
import os
import re

from .modelo import Secao

_CSV = os.path.join(os.path.dirname(__file__), "dados", "perfis_gerdau.csv")
_CSV_PFF = os.path.join(os.path.dirname(__file__), "dados", "perfis_pff.csv")


def wn_max(p):
    """Máximo |ωn| (coordenada setorial normalizada, polo no CC) para σw = B·ωn/Cw. I duplamente simétrico:
    h0·bf/4 na ponta da mesa (Seaburg & Carter, AISC Design Guide 9, 1997); U laminado, U e Ue formados a
    frio: polilinha da linha média refeita das dimensões (_parede_fina); demais (L, maciças, tubos): 0."""
    tipo = p.get("tipo")
    if tipo == "I":
        return (p["h"] - p["tf"]) * p["b"] / 4
    if tipo == "U" and "tw" in p:
        return _parede_fina(_pts_U(p["h"], p["b"], p["tw"], p["tf"]), [p["tf"], p["tw"], p["tf"]])["wn_max"]
    if tipo == "U":
        return _parede_fina(_pts_U_pf(p["h"], p["b"], p["t"], p.get("ri", 0.0)), p["t"])["wn_max"]
    if tipo == "Ue":
        return _parede_fina(_pts_Ue(p["h"], p["b"], p["D"], p["t"], p.get("ri", 0.0)), p["t"])["wn_max"]
    return 0.0


def _monta(nome, tipo, A, Ix, Iy, J, Wx, Wy, Zx, Zy, Cw=0.0, x0=0.0, y0=0.0, **extra):
    p = dict(tipo=tipo, A=A, Ix=Ix, Iy=Iy, J=J, Wx=Wx, Wy=Wy, Zx=Zx, Zy=Zy, Cw=Cw,
             rx=math.sqrt(Ix / A), ry=math.sqrt(Iy / A), x0=x0, y0=y0)
    p.update(extra)
    p["r0"] = math.sqrt(p["rx"] ** 2 + p["ry"] ** 2 + x0 ** 2 + y0 ** 2)
    return Secao(nome, A, Ix, Iy, J, p)


def secao_retangular(b, h, nome=None):
    A = b * h
    a, c = max(b, h), min(b, h)
    J = a * c ** 3 * (1 / 3 - 0.21 * c / a * (1 - c ** 4 / (12 * a ** 4)))  # Roark
    return _monta(nome or f"R{b*1e3:g}x{h*1e3:g}", "retangular", A, b * h ** 3 / 12, h * b ** 3 / 12, J,
                  b * h ** 2 / 6, h * b ** 2 / 6, b * h ** 2 / 4, h * b ** 2 / 4, h=h, b=b, Ay=5 / 6 * A, Az=5 / 6 * A)


def secao_circular(d, nome=None):
    A = math.pi * d ** 2 / 4
    I = math.pi * d ** 4 / 64
    return _monta(nome or f"C{d*1e3:g}", "circular", A, I, I, 2 * I, I / (d / 2), I / (d / 2),
                  d ** 3 / 6, d ** 3 / 6, h=d, b=d, Ay=0.9 * A, Az=0.9 * A)


def secao_tubo_circular(d, t, nome=None):
    di = d - 2 * t
    A = math.pi * (d ** 2 - di ** 2) / 4
    I = math.pi * (d ** 4 - di ** 4) / 64
    Z = (d ** 3 - di ** 3) / 6
    return _monta(nome or f"TC{d*1e3:g}x{t*1e3:g}", "tubo_circular", A, I, I, 2 * I, 2 * I / d, 2 * I / d,
                  Z, Z, h=d, b=d, t=t, D_t=d / t,
                  Ay=A / 2, Az=A / 2)  # A/2: valor usual de tubo circular de parede fina


def secao_tubo_retangular(h, b, t, nome=None):
    # ponytail: cantos vivos; tubos com raio externo dão A ~2-4% menor
    hi, bi = h - 2 * t, b - 2 * t
    A = b * h - bi * hi
    Ix = (b * h ** 3 - bi * hi ** 3) / 12
    Iy = (h * b ** 3 - hi * bi ** 3) / 12
    hm, bm = h - t, b - t
    J = 2 * t * (hm * bm) ** 2 / (hm + bm)  # Bredt
    return _monta(nome or f"TR{h*1e3:g}x{b*1e3:g}x{t*1e3:g}", "tubo_retangular", A, Ix, Iy, J,
                  2 * Ix / h, 2 * Iy / b, (b * h ** 2 - bi * hi ** 2) / 4, (h * b ** 2 - hi * bi ** 2) / 4,
                  h=h, b=b, t=t, bt_mesa=(b - 3 * t) / t, ht_alma=(h - 3 * t) / t,
                  Ay=2 * b * t, Az=2 * h * t)  # Vz resistido pelas paredes verticais (h)


def secao_I(h, b, tw, tf, r=0.0, nome=None, soldado=None):
    """I/H duplamente simétrico; r = raio de concordância (laminado)."""
    hw = h - 2 * tf
    af = (1 - math.pi / 4) * r ** 2          # área de cada concordância
    e = (10 - 3 * math.pi) / (12 - 3 * math.pi) * r  # centroide da concordância a partir do canto
    A = 2 * tf * b + hw * tw + 4 * af       # = 2tf·b + (h-2tf)tw + (4-π)r²
    yf = h / 2 - tf - e
    xf = tw / 2 + e
    Ix = b * h ** 3 / 12 - (b - tw) * hw ** 3 / 12 + 4 * af * yf ** 2
    Iy = 2 * tf * b ** 3 / 12 + hw * tw ** 3 / 12 + 4 * af * xf ** 2
    Zx = b * tf * (h - tf) + tw * hw ** 2 / 4 + 4 * af * yf
    Zy = tf * b ** 2 / 2 + hw * tw ** 2 / 4 + 4 * af * xf
    J = (2 * b * tf ** 3 + (h - tf) * tw ** 3) / 3
    Cw = tf * b ** 3 * (h - tf) ** 2 / 24
    hw_liv = hw - 2 * r
    return _monta(nome or f"I{h*1e3:g}x{b*1e3:g}", "I", A, Ix, Iy, J, 2 * Ix / h, 2 * Iy / b, Zx, Zy, Cw,
                  h=h, b=b, tw=tw, tf=tf, r=r, hw=hw_liv, soldado=(r == 0) if soldado is None else soldado,
                  bt_mesa=b / (2 * tf), ht_alma=hw_liv / tw,
                  Ay=5 / 6 * 2 * b * tf, Az=h * tw)  # Vz: alma; Vy: mesas


def _parede_fina(pts, t):
    """Seção aberta de parede fina pela linha média (polilinha). Integra coordenada setorial.
    t: espessura única ou lista com uma espessura por segmento.
    Retorna A, Ix, Iy, Ixy (centroidais), xg, yg, x0, y0 (CC rel. centroide), J, Cw e wn_max = máx |ωn|
    (coordenada setorial normalizada com polo no CC, tensão de empenamento σw = B·ωn/Cw)."""
    segs = list(zip(pts[:-1], pts[1:]))
    ts = list(t) if isinstance(t, (list, tuple)) else [t] * len(segs)
    L = [math.dist(p, q) for p, q in segs]
    A = sum(l * ti for l, ti in zip(L, ts))
    xg = sum(l * ti * (p[0] + q[0]) / 2 for l, ti, (p, q) in zip(L, ts, segs)) / A
    yg = sum(l * ti * (p[1] + q[1]) / 2 for l, ti, (p, q) in zip(L, ts, segs)) / A
    P = [(x - xg, y - yg) for x, y in pts]
    w = [0.0]
    for (x1, y1), (x2, y2) in zip(P[:-1], P[1:]):
        w.append(w[-1] + x1 * y2 - x2 * y1)  # coordenada setorial com polo no centroide

    def integ(f, g):  # ∫ f g dA, f e g lineares em cada segmento
        s = 0.0
        for i, (l, ti) in enumerate(zip(L, ts)):
            f1, f2, g1, g2 = f[i], f[i + 1], g[i], g[i + 1]
            s += l * ti * (2 * f1 * g1 + f1 * g2 + f2 * g1 + 2 * f2 * g2) / 6
        return s
    X = [p[0] for p in P]
    Y = [p[1] for p in P]
    Ix, Iy, Ixy = integ(Y, Y), integ(X, X), integ(X, Y)
    Iwx, Iwy = integ(w, Y), integ(w, X)
    # ω_S = ω_C - x0·y + y0·x ; ∫ω_S x = ∫ω_S y = 0
    det = Ix * Iy - Ixy ** 2
    x0 = (Iwx * Iy - Iwy * Ixy) / det
    y0 = (Iwx * Ixy - Iwy * Ix) / det
    ws = [wi - x0 * yi + y0 * xi for wi, xi, yi in zip(w, X, Y)]
    um = [1.0] * len(ws)
    Cw = integ(ws, ws) - integ(ws, um) ** 2 / A
    wm = integ(ws, um) / A
    J = sum(l * ti ** 3 for l, ti in zip(L, ts)) / 3
    return dict(A=A, Ix=Ix, Iy=Iy, Ixy=Ixy, xg=xg, yg=yg, x0=x0, y0=y0, J=J, Cw=Cw, pts=P,
                wn_max=max(abs(wi - wm) for wi in ws))


def _arredonda(pts, rm, n=16):
    """Troca cada vértice interno (dobra de 90°) da polilinha por um arco de raio rm (linha média)."""
    if rm <= 0:
        return list(pts)
    out = [pts[0]]
    for a, P, b in zip(pts[:-2], pts[1:-1], pts[2:]):
        u = ((P[0] - a[0]) / math.dist(a, P), (P[1] - a[1]) / math.dist(a, P))  # direção de chegada
        v = ((b[0] - P[0]) / math.dist(P, b), (b[1] - P[1]) / math.dist(P, b))  # direção de saída
        c = (P[0] + rm * (v[0] - u[0]), P[1] + rm * (v[1] - u[1]))
        for k in range(n + 1):
            f = math.pi / 2 * k / n
            out.append((c[0] + rm * (-v[0] * math.cos(f) + u[0] * math.sin(f)),
                        c[1] + rm * (-v[1] * math.cos(f) + u[1] * math.sin(f))))
    out.append(pts[-1])
    return out


def ri_nbr6355(t):
    """Raio interno de dobra da NBR 6355:2012 (anexo A): ri = t até 6,3 mm; 1,5t acima."""
    return t if t <= 0.0063 + 1e-12 else 1.5 * t


def _plastico_x(pts, t):
    # ponytail: Z por discretização da polilinha (eixo de flexão x simétrico: LN no centroide)
    Z = 0.0
    for (x1, y1), (x2, y2), n in zip(pts[:-1], pts[1:], _amostras(pts)):
        for k in range(n):
            yy = y1 + (y2 - y1) * (k + 0.5) / n
            Z += abs(yy) * math.dist((x1, y1), (x2, y2)) / n * t
    return Z


def _amostras(pts):
    """Pontos de integração por segmento, proporcionais ao comprimento (200 no maior; arcos: poucos)."""
    L = [math.dist(p, q) for p, q in zip(pts[:-1], pts[1:])]
    return [max(4, round(200 * l / max(L))) for l in L]


def _plastico_y(pts, t, A):
    # LN plástica: abscissa que divide a área ao meio (bisseção)
    amostras = []
    for (x1, y1), (x2, y2), n in zip(pts[:-1], pts[1:], _amostras(pts)):
        l = math.dist((x1, y1), (x2, y2))
        for k in range(n):
            amostras.append((x1 + (x2 - x1) * (k + 0.5) / n, l / n * t))
    lo, hi = min(a for a, _ in amostras), max(a for a, _ in amostras)
    for _ in range(60):
        m = (lo + hi) / 2
        if sum(da for a, da in amostras if a < m) < A / 2:
            lo = m
        else:
            hi = m
    return sum(abs(a - m) * da for a, da in amostras)


def _secao_pf(nome, tipo, pts, t, **extra):
    g = _parede_fina(pts, t)
    P = g["pts"]
    ymax = max(abs(p[1]) for p in P) + t / 2
    xs = [p[0] for p in P]
    Wy = g["Iy"] / (max(max(xs), -min(xs)) + t / 2)
    return _monta(nome, tipo, g["A"], g["Ix"], g["Iy"], g["J"], g["Ix"] / ymax, Wy,
                  _plastico_x(P, t), _plastico_y(P, t, g["A"]), g["Cw"], g["x0"], g["y0"],
                  xg=g["xg"], t=t, **extra)


def _pts_Ue(h, b, D, t, ri):
    hm, bm, Dm = h - t, b - t, D - t / 2
    pts = [(bm, hm / 2 - Dm), (bm, hm / 2), (0, hm / 2), (0, -hm / 2), (bm, -hm / 2), (bm, -hm / 2 + Dm)]
    return _arredonda(pts, ri + t / 2 if ri > 0 else 0.0)


def _pts_U_pf(h, b, t, ri):
    hm, bm = h - t, b - t / 2
    return _arredonda([(bm, hm / 2), (0, hm / 2), (0, -hm / 2), (bm, -hm / 2)], ri + t / 2 if ri > 0 else 0.0)


def _pts_U(h, b, tw, tf):
    return [(b - tw / 2, (h - tf) / 2), (0, (h - tf) / 2), (0, -(h - tf) / 2), (b - tw / 2, -(h - tf) / 2)]


def secao_Ue(h, b, D, t, nome=None, ri=0.0):
    """U enrijecido formado a frio (dimensões externas), linha média. ri = raio interno de dobra:
    0 → cantos vivos (linha média em ângulo reto); ri > 0 → arcos de raio ri + t/2 pela linha média.
    O catálogo e secao_de_spec usam ri da NBR 6355 (ri_nbr6355)."""
    return _secao_pf(nome or f"Ue{h*1e3:g}x{b*1e3:g}x{D*1e3:g}x{t*1e3:g}", "Ue",
                     _pts_Ue(h, b, D, t, ri), t, h=h, b=b, D=D, ri=ri,
                     bt_mesa=(b - 2 * (t + ri)) / t, ht_alma=(h - 2 * (t + ri)) / t, bt_enr=(D - t - ri) / t,
                     Ay=5 / 6 * 2 * b * t, Az=h * t)


def secao_U_pf(h, b, t, nome=None, ri=0.0):
    """U simples formado a frio (dimensões externas), linha média; ri como em secao_Ue."""
    return _secao_pf(nome or f"U{h*1e3:g}x{b*1e3:g}x{t*1e3:g}", "U",
                     _pts_U_pf(h, b, t, ri), t, h=h, b=b, D=0.0, ri=ri,
                     bt_mesa=(b - t - ri - t) / t, ht_alma=(h - 2 * (t + ri)) / t,
                     Ay=5 / 6 * 2 * b * t, Az=h * t)


def secao_U(h, b, tw, tf, nome=None):
    """U laminado/soldado de mesas paralelas, cantos vivos (exato por retângulos)."""
    hw = h - 2 * tf
    A = 2 * b * tf + hw * tw
    xg = (2 * b * tf * b / 2 + hw * tw * tw / 2) / A
    Ix = b * h ** 3 / 12 - (b - tw) * hw ** 3 / 12
    Iy = 2 * (tf * b ** 3 / 12 + b * tf * (b / 2 - xg) ** 2) + hw * tw ** 3 / 12 + hw * tw * (tw / 2 - xg) ** 2
    Zx = b * tf * (h - tf) + tw * hw ** 2 / 4
    # LN plástica vertical (A/2 de cada lado), supondo dentro das mesas
    xp = A / (2 * h) if h * tw >= A / 2 else (A / 2 - hw * tw) / (2 * tf)
    if xp >= tw:
        Zy = hw * tw * (xp - tw / 2) + 2 * tf * (xp ** 2 + (b - xp) ** 2) / 2
    else:
        Zy = (h * xp ** 2 + h * (tw - xp) ** 2) / 2 + 2 * tf * (b - tw) * ((b + tw) / 2 - xp)
    # linha média com tf nas mesas e tw na alma: Cw = G.1 nota 1 (tf·b'³·h'²/12·(3b'tf + 2h'tw)/(6b'tf + h'tw))
    # e x0 = e + x̄ (e = 3b'²tf/(6b'tf + h'tw) da linha média da alma)
    g = _parede_fina(_pts_U(h, b, tw, tf), [tf, tw, tf])
    J = (2 * b * tf ** 3 + hw * tw ** 3) / 3
    return _monta(nome or f"U{h*1e3:g}x{b*1e3:g}", "U", A, Ix, Iy, J, 2 * Ix / h, Iy / (b - xg), Zx, Zy,
                  g["Cw"], g["x0"], 0.0, h=h, b=b, tw=tw, tf=tf, xg=xg,
                  bt_mesa=b / tf, ht_alma=hw / tw, Ay=5 / 6 * 2 * b * tf, Az=h * tw)


def secao_cantoneira(b, h, t, nome=None):
    """Cantoneira (abas b horizontal, h vertical), cantos vivos.
    Ix/Iy (e Secao.Iy/Iz do solver) em relação aos eixos geométricos paralelos às abas.
    Eixos centrais principais em props (NBR 8800 E.1.2/E.1.3): Iu = Imax, Iv = Imin, alfa_principal
    (rad, de x até u, anti-horário) e o centro de cisalhamento (u0, v0) nesses eixos."""
    A1, A2 = b * t, (h - t) * t
    A = A1 + A2
    xg = (A1 * b / 2 + A2 * t / 2) / A
    yg = (A1 * t / 2 + A2 * (t + (h - t) / 2)) / A
    Ix = b * t ** 3 / 12 + A1 * (t / 2 - yg) ** 2 + t * (h - t) ** 3 / 12 + A2 * (t + (h - t) / 2 - yg) ** 2
    Iy = t * b ** 3 / 12 + A1 * (b / 2 - xg) ** 2 + (h - t) * t ** 3 / 12 + A2 * (t / 2 - xg) ** 2
    Ixy = A1 * (b / 2 - xg) * (t / 2 - yg) + A2 * (t / 2 - xg) * (t + (h - t) / 2 - yg)
    m, rr = (Ix + Iy) / 2, math.hypot((Ix - Iy) / 2, Ixy)
    J = (b + h - t) * t ** 3 / 3
    # centro de cisalhamento no encontro das linhas médias
    x0, y0 = t / 2 - xg, t / 2 - yg
    Wx, Wy = Ix / (h - yg), Iy / (b - xg)
    alfa = 0.5 * math.atan2(-2 * Ixy, Ix - Iy)  # eixo u de inércia máxima
    c, s_ = math.cos(alfa), math.sin(alfa)
    sec = _monta(nome or f"L{b*1e3:g}x{h*1e3:g}x{t*1e3:g}", "L", A, Ix, Iy, J, Wx, Wy,
                 None, None, 0.0, x0, y0,
                 b=b, h=h, t=t, Ixy=Ixy, Imax=m + rr, Imin=m - rr, rmin=math.sqrt((m - rr) / A),
                 Iu=m + rr, Iv=m - rr, alfa_principal=alfa, u0=x0 * c + y0 * s_, v0=-x0 * s_ + y0 * c,
                 bt_mesa=max(b, h) / t,
                 Ay=5 / 6 * b * t, Az=5 / 6 * h * t)  # aproximação: cantoneira não tem cisalhamento uniforme
    # Zx/Zy não calculados: a flexão de cantoneira simples não está implementada (fora da tabela G.1)
    del sec.props["Zx"], sec.props["Zy"]
    return sec


ARRANJOS = ("caixao", "I")       # caixão/tubo: abas para dentro; I: costas com costas (2L: forma um T)
LIGACOES = ("soldada", "presilhas", "generica")


def secao_dupla(comp, arranjo, ligacao, s=0.0, nome=None, l_ligacoes=None, chapas_espacadoras=False):
    """Barra composta de dois perfis iguais `comp` (Secao de secao_U ou secao_cantoneira).
    arranjo: 'caixao' (abas para dentro, unidas pelas pontas; afastamento s entre as pontas) ou 'I' (costas
    com costas, afastamento s entre as almas/abas). ligacao: 'soldada' (cordão contínuo), 'presilhas' (ou
    chapas espaçadoras: os perfis trabalham isolados entre ligações) ou 'generica' (desconhecida).
    Soldada com s = 0 é seção única: caixão → tubo retangular de paredes tw (almas) e tf (mesas), J pela fórmula
    de Bredt J = 4Am²/∮ds/t (Timoshenko & Goodier, Theory of Elasticity, §115); 2U em I → I de alma 2tw
    (mesma geometria). Demais: Steiner (A, I, W, Z somados com a translação), J = 2J₁ e Cw = 2Cw₁ (perfis
    abertos independentes: menor que o real, a favor da segurança). props['composicao'] guarda o arranjo,
    a ligação, s, l_ligacoes (m, distância entre presilhas) e chapas_espacadoras (NBR 8800 5.3.4)."""
    if arranjo not in ARRANJOS or ligacao not in LIGACOES:
        raise ValueError(f"secao_dupla: arranjo em {ARRANJOS}, ligacao em {LIGACOES}")
    if not s >= 0:
        raise ValueError("secao_dupla: afastamento s deve ser >= 0")
    p = comp.props
    forma = p["tipo"]
    if forma not in ("U", "L") or (forma == "U" and "tw" not in p):
        raise ValueError(f"secao_dupla: componente '{forma}' não suportado (use U laminado ou cantoneira)")
    comp_spec = ({"forma": "U", **{k: p[k] for k in ("h", "b", "tw", "tf")}} if forma == "U" else
                 {"forma": "cantoneira", **{k: p[k] for k in ("b", "h", "t")}})
    nome = nome or f"2{comp.nome} {arranjo} {ligacao}" + (f" s={s * 1e3:g}" if s else "")
    compos = dict(arranjo=arranjo, ligacao=ligacao, s=s, l_ligacoes=l_ligacoes, componente=comp_spec,
                  nome_componente=comp.nome, chapas_espacadoras=bool(chapas_espacadoras))
    unica = ligacao == "soldada" and s == 0
    if forma == "U" and unica:
        h, b, tw, tf = p["h"], p["b"], p["tw"], p["tf"]
        if arranjo == "I":
            sec = secao_I(h, 2 * b, 2 * tw, tf, nome=nome, soldado=False)
        else:
            sec = _caixao_soldado(h, 2 * b, tw, tf, nome)
        sec.props["composicao"] = compos
        return sec
    if forma == "L" and unica and arranjo == "caixao":
        sec = secao_tubo_retangular(p["h"], p["b"], p["t"], nome=nome)  # 2L pelas pontas = tubo b × h, parede t
        sec.props["composicao"] = compos
        return sec
    A1 = comp.A
    if forma == "U":
        B = 2 * p["b"] + s
        d = B / 2 - p["xg"] if arranjo == "caixao" else s / 2 + p["xg"]   # centroide de cada U ao eixo y
        ymax, x0, y0 = p["h"] / 2, 0.0, 0.0
        Zx, Zy = 2 * p["Zx"], 2 * A1 * d    # LN plástica no eixo de simetria: Z = 2·A₁·d
        extra = dict(h=p["h"], b=B, tw=p["tw"], tf=p["tf"])
    else:  # cantoneiras: aba b horizontal, h vertical; em I = T (costas com costas pelas abas h)
        if arranjo == "caixao":
            raise ValueError("2L em caixão só soldado com s = 0 (tubo); com afastamento não suportado")
        B = 2 * p["b"] + s
        xg = p["b"] - p["Iy"] / p["Wy"]   # secao_cantoneira: Wy = Iy/(b − xg), Wx = Ix/(h − yg)
        yg = p["h"] - p["Ix"] / p["Wx"]
        d = s / 2 + xg
        ymax, x0, y0 = max(yg, p["h"] - yg), 0.0, p["t"] / 2 - yg   # CC no encontro das linhas médias (T)
        Zx = Zy = None
        extra = dict(h=p["h"], b=B, t=p["t"])
    Ix = 2 * p["Ix"]
    Iy = 2 * (p["Iy"] + A1 * d * d)
    J = 2 * p["J"]
    Cw = 2 * p.get("Cw", 0.0)
    sec = _monta(nome, "dupla", 2 * A1, Ix, Iy, J, Ix / ymax, Iy / (B / 2), Zx, Zy, Cw, x0, y0,
                 composicao=compos, Ay=2 * p["Ay"], Az=2 * p["Az"], **extra)
    if Zx is None:
        del sec.props["Zx"], sec.props["Zy"]
    return sec


def _caixao_soldado(h, B, tw, tf, nome):
    """Caixão fechado h × B com almas tw e mesas tf (2U soldados pelas pontas das mesas). Props no formato de
    tubo retangular para a NBR 8800 (t = min(tw, tf): espessura a favor da segurança em cortante e torção;
    b/t das paredes com a largura livre)."""
    hi, bi = h - 2 * tf, B - 2 * tw
    A = B * h - bi * hi
    Ix = (B * h ** 3 - bi * hi ** 3) / 12
    Iy = (h * B ** 3 - hi * bi ** 3) / 12
    hm, bm = h - tf, B - tw
    J = 4 * (hm * bm) ** 2 / (2 * bm / tf + 2 * hm / tw)  # Bredt
    t = min(tw, tf)
    return _monta(nome, "tubo_retangular", A, Ix, Iy, J, 2 * Ix / h, 2 * Iy / B,
                  (B * h ** 2 - bi * hi ** 2) / 4, (h * B ** 2 - hi * bi ** 2) / 4,
                  h=h, b=B, t=t, bt_mesa=bi / tf, ht_alma=hi / tw, Ay=2 * B * tf, Az=2 * h * tw)


def _ler_csv(caminho):
    """Linhas do CSV (linhas iniciadas por '#' são comentário: fonte, data). [] se não existir."""
    if not os.path.exists(caminho):
        return []
    with open(caminho, encoding="utf-8") as f:
        return list(csv.DictReader(l for l in f if not l.lstrip().startswith("#")))


def _chave(nome):
    """'W 200 x 26,6' == 'W200x26.6'; 'Ue 150x60x20x2,00' == 'UE150X60X20X2'."""
    s = nome.upper().replace(" ", "").replace(",", ".")
    return re.sub(r"\d+(?:\.\d+)?", lambda m: f"{float(m.group()):g}", s)


def _secao_gerdau(lin):
    v = {k: float(lin[k]) / 1000 for k in ("d", "bf", "tw", "tf", "R")}
    s = secao_I(v["d"], v["bf"], v["tw"], v["tf"], v["R"], nome=lin["nome"], soldado=False)
    s.props["massa"] = float(lin["massa"])
    s.props["A_catalogo"] = float(lin["A"]) * 1e-4
    return s


def _secao_pff(lin):
    h, b, D, t = (float(lin[k]) / 1000 for k in ("h", "b", "D", "t"))
    if lin["tipo"] == "Ue":  # NBR 6355: dobras com ri = t (cantos arredondados nas propriedades brutas)
        return secao_Ue(h, b, D, t, nome=lin["nome"], ri=ri_nbr6355(t))
    if lin["tipo"] == "U":
        return secao_U_pf(h, b, t, nome=lin["nome"], ri=ri_nbr6355(t))
    raise ValueError(f"{_CSV_PFF}: tipo '{lin['tipo']}' inválido em {lin['nome']} (use Ue ou U)")


def catalogo(nome):
    """Perfil por nome: W/HP Gerdau ('W200x26.6') ou PFF NBR 6355 ('Ue 150x60x20x2,00', se
    perfis_pff.csv existir). CSV em mm; retorna Secao em m."""
    chave = _chave(nome)
    for caminho, monta in ((_CSV, _secao_gerdau), (_CSV_PFF, _secao_pff)):
        for lin in _ler_csv(caminho):
            if _chave(lin["nome"]) == chave:
                return monta(lin)
    raise KeyError(f"perfil {nome} não está no catálogo")


def lista_catalogo(familia=None):
    """[dict(nome, familia, massa_kg_m, d_mm, bf_mm, A_cm2)]; familia ∈ {W, HP, Ue, U} ou None.
    Massa de PFF = A·7850 kg/m³ (A com as dobras da NBR 6355)."""
    itens = []
    for lin in _ler_csv(_CSV):
        itens.append(dict(nome=lin["nome"], familia=re.match(r"[A-Z]+", lin["nome"]).group(),
                          massa_kg_m=float(lin["massa"]), d_mm=float(lin["d"]), bf_mm=float(lin["bf"]),
                          A_cm2=float(lin["A"])))
    for lin in _ler_csv(_CSV_PFF):
        s = _secao_pff(lin)
        itens.append(dict(nome=lin["nome"], familia=lin["tipo"], massa_kg_m=round(s.A * 7850, 2),
                          d_mm=float(lin["h"]), bf_mm=float(lin["b"]), A_cm2=round(s.A * 1e4, 3)))
    return [i for i in itens if familia is None or i["familia"] == familia]


def _mm(v):
    return f"{round(v * 1000, 3):g}".replace(".", ",")


# forma -> (construtor, campos obrigatórios, opcionais, prefixo do nome)
_FORMAS = {
    "retangular": (secao_retangular, ("b", "h"), (), "R"),
    "circular": (secao_circular, ("d",), (), "C"),
    "I": (secao_I, ("h", "b", "tw", "tf"), ("r", "soldado"), "I"),
    "tubo_circular": (secao_tubo_circular, ("d", "t"), (), "TC"),
    "tubo_retangular": (secao_tubo_retangular, ("h", "b", "t"), (), "TR"),
    "U": (secao_U, ("h", "b", "tw", "tf"), (), "U"),
    "Ue": (secao_Ue, ("h", "b", "D", "t"), ("ri",), "Ue"),
    "U_pf": (secao_U_pf, ("h", "b", "t"), ("ri",), "U"),
    "cantoneira": (secao_cantoneira, ("b", "h", "t"), (), "L"),
}


def _num(dados, campo, minimo=0.0):
    v = dados.get(campo)
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v <= minimo:
        raise ValueError(f"secao.{campo}: número > {minimo:g} obrigatório (recebido {v!r})")
    return float(v)


def secao_de_spec(dados):
    """Converte a spec de seção da ESPEC §6.3 em Secao (medidas em m).
    {"perfil": nome} | {"forma": ..., medidas, nome?} | explícita {nome, A, Iy, Iz, J, props}.
    Entrada inválida → ValueError com o campo na mensagem."""
    if not isinstance(dados, dict):
        raise ValueError(f"secao: esperado objeto, recebido {type(dados).__name__}")
    if "perfil" in dados:
        try:
            return catalogo(str(dados["perfil"]))
        except KeyError:
            raise ValueError(f"secao.perfil: '{dados['perfil']}' não está no catálogo") from None
    if "forma" in dados:
        forma = dados["forma"]
        if forma not in _FORMAS:
            raise ValueError(f"secao.forma: '{forma}' inválida; use uma de {sorted(_FORMAS)}")
        f, obrig, opc, pref = _FORMAS[forma]
        args = [_num(dados, c) for c in obrig]
        kw = {}
        if "r" in opc and dados.get("r"):
            kw["r"] = _num(dados, "r")
        if "ri" in opc:  # PFF: raio interno de dobra; ausente → NBR 6355 (ri = t); 0 → cantos vivos
            if dados.get("ri") is None:
                kw["ri"] = ri_nbr6355(_num(dados, "t"))
            elif dados["ri"] == 0:
                kw["ri"] = 0.0
            else:
                kw["ri"] = _num(dados, "ri")
        if "soldado" in opc and dados.get("soldado") is not None:
            if not isinstance(dados["soldado"], bool):
                raise ValueError("secao.soldado: true/false")
            kw["soldado"] = dados["soldado"]
        v = dict(zip(obrig, args))
        if "tf" in v and 2 * v["tf"] >= v["h"]:
            raise ValueError("secao.tf: 2·tf deve ser menor que h")
        if "tw" in v and v["tw"] >= v["b"]:  # I e U
            raise ValueError("secao.tw: deve ser menor que b")
        if "t" in v and 2 * v["t"] >= min(x for k, x in v.items() if k in ("d", "h", "b")):
            raise ValueError("secao.t: espessura incompatível com as dimensões")
        nome = dados.get("nome") or f"{pref} " + "x".join(_mm(a) for a in args)
        return f(*args, nome=nome, **kw)
    faltam = [c for c in ("A", "Iy", "Iz", "J") if c not in dados]
    if faltam:
        raise ValueError(f"secao: informe 'perfil', 'forma' ou a forma explícita (faltam {faltam})")
    A, Iy, Iz, J = (_num(dados, c) for c in ("A", "Iy", "Iz", "J"))
    props = dados.get("props") or {}
    if not isinstance(props, dict):
        raise ValueError("secao.props: esperado objeto")
    return Secao(str(dados.get("nome") or f"S A={A * 1e4:g}cm²"), A, Iy, Iz, J, dict(props))
