"""Memória independente — propriedades de seção (verificador-calculos, ficha 013, 2026-10-20).

Só stdlib. NÃO importa prossen_estrutural. Unidades: entrada em mm, saída em mm/mm²/mm⁴/mm⁶.

Fontes das fórmulas:
- Parede fina aberta (linha média, espessura constante), coordenada setorial e centro de
  cisalhamento: V. Z. Vlasov, "Thin-walled elastic beams" (1961); T. Galambos, "Flexural-torsional
  buckling of thin-walled members" — integrais por segmento com ω linear:
  ∫f·g dA = tL(fi·gi + fj·gj)/3 + tL(fi·gj + fj·gi)/6.
- U enrijecido / U simples, cantos vivos: W.-W. Yu & R. LaBoube, "Cold-formed steel design", 4ª ed.,
  Wiley 2010, apêndice B (fórmulas de x̄, m, J, Cw com a = h − t, b = bf − t, c = D − t/2).
- I com concordâncias: área A = 2·bf·tf + (d − 2tf)·tw + (4 − π)·r² (geometria); momentos do
  "spandrel" (quadrado r×r menos quarto de círculo) deduzidos aqui:
  área (1 − π/4)r², momento estático em relação à face (5/6 − π/4)r³, inércia (1 − 5π/16)r⁴.
- I duplamente simétrico: Cw = Iy_parede·h0²/4 = tf·bf³·h0²/24 (h0 = d − tf); J = Σ b·t³/3.
- Tubo retangular: Bredt, J = 4·Am²·t/p (linha média).
"""
import math


# ---------------------------------------------------------------- parede fina genérica (árvore)
def parede_fina(nos, segs, t):
    """nos: [(x, y)] linha média (mm); segs: [(i, j)] formando uma árvore; t: espessura.
    Retorna dict A, xc, yc, Ix, Iy, Ixy (centroidais), xs, ys (CC relativo ao centroide), J, Cw."""
    L = [math.dist(nos[i], nos[j]) for i, j in segs]
    A = t * sum(L)
    xc = sum(t * l * (nos[i][0] + nos[j][0]) / 2 for (i, j), l in zip(segs, L)) / A
    yc = sum(t * l * (nos[i][1] + nos[j][1]) / 2 for (i, j), l in zip(segs, L)) / A
    P = [(x - xc, y - yc) for x, y in nos]

    def integ(f, g):  # ∫ f g dA, f e g lineares por segmento (valores nodais por segmento)
        s = 0.0
        for k, ((i, j), l) in enumerate(zip(segs, L)):
            fi, fj = f(k, i), f(k, j)
            gi, gj = g(k, i), g(k, j)
            s += t * l * ((fi * gi + fj * gj) / 3 + (fi * gj + fj * gi) / 6)
        return s

    X = lambda k, n: P[n][0]
    Y = lambda k, n: P[n][1]
    Ix, Iy, Ixy = integ(Y, Y), integ(X, X), integ(X, Y)

    def setorial(polo):
        # ω por percurso na árvore a partir do nó 0
        adj = {}
        for k, (i, j) in enumerate(segs):
            adj.setdefault(i, []).append((j, k))
            adj.setdefault(j, []).append((i, k))
        w = {0: 0.0}
        pilha = [0]
        while pilha:
            a = pilha.pop()
            for b, k in adj[a]:
                if b in w:
                    continue
                ra = (P[a][0] - polo[0], P[a][1] - polo[1])
                rb = (P[b][0] - polo[0], P[b][1] - polo[1])
                w[b] = w[a] + (ra[0] * rb[1] - ra[1] * rb[0])
                pilha.append(b)
        return w

    w0 = setorial((0.0, 0.0))
    W0 = lambda k, n: w0[n]
    Iwx, Iwy = integ(W0, X), integ(W0, Y)
    D = Ix * Iy - Ixy ** 2
    xs = (Iy * Iwy - Ixy * Iwx) / D
    ys = (Ixy * Iwy - Ix * Iwx) / D
    # conferência: com a convenção ω = ∫(x dy − y dx), ∫ω_S x = Iwx − xs·Ixy + ys·Iy = 0
    ws = setorial((xs, ys))
    um = lambda k, n: 1.0
    WS = lambda k, n: ws[n]
    wm = integ(WS, um) / A
    WN = lambda k, n: ws[n] - wm
    Cw = integ(WN, WN)
    J = sum(l * t ** 3 / 3 for l in L)
    return dict(A=A, xc=xc, yc=yc, Ix=Ix, Iy=Iy, Ixy=Ixy, xs=xs, ys=ys, J=J, Cw=Cw)


# ---------------------------------------------------------------- formas
def I_laminado(d, bf, tw, tf, r=0.0):
    """I duplamente simétrico com 4 concordâncias de raio r (mm). x = forte."""
    hw = d - 2 * tf
    A = 2 * bf * tf + hw * tw + (4 - math.pi) * r ** 2
    Af, Sf, If = (1 - math.pi / 4) * r ** 2, (5 / 6 - math.pi / 4) * r ** 3, (1 - 5 * math.pi / 16) * r ** 4
    Y0 = d / 2 - tf
    Ix = bf * d ** 3 / 12 - (bf - tw) * hw ** 3 / 12 + 4 * (Y0 ** 2 * Af - 2 * Y0 * Sf + If)
    X0 = tw / 2
    Iy = 2 * tf * bf ** 3 / 12 + hw * tw ** 3 / 12 + 4 * (X0 ** 2 * Af + 2 * X0 * Sf + If)
    # plástico: metade da seção × 2 (fillets: momento estático em relação ao eixo)
    Zx = 2 * (bf * tf * (d - tf) / 2 + tw * (hw / 2) ** 2 / 2 + 2 * (Y0 * Af - Sf))
    Zy = 2 * (2 * tf * (bf / 2) ** 2 / 2 + hw * (tw / 2) ** 2 / 2 + 2 * (X0 * Af + Sf))
    h0 = d - tf
    Cw = tf * bf ** 3 * h0 ** 2 / 24
    J_h0 = (2 * bf * tf ** 3 + h0 * tw ** 3) / 3        # alma até a linha média das mesas
    J_hw = (2 * bf * tf ** 3 + hw * tw ** 3) / 3        # alma só entre as faces das mesas
    return dict(A=A, Ix=Ix, Iy=Iy, Wx=Ix / (d / 2), Wy=Iy / (bf / 2), Zx=Zx, Zy=Zy, Cw=Cw,
                J_h0=J_h0, J_hw=J_hw, rx=math.sqrt(Ix / A), ry=math.sqrt(Iy / A))


def Ue_yu(h, b, D, t):
    """U enrijecido, cantos vivos, Yu & LaBoube ap. B. Eixo x = simetria. Retorna m, x̄ (da alma),
    x0 = m + x̄ (CC ao centroide), J, Cw, A, Ix, Iy."""
    a, bb, c = h - t, b - t, D - t / 2
    A = t * (a + 2 * bb + 2 * c)
    xb = bb * (bb + 2 * c) / (a + 2 * bb + 2 * c)
    m = bb * (3 * a ** 2 * bb + c * (6 * a ** 2 - 8 * c ** 2)) / (
        a ** 3 + 6 * a ** 2 * bb + c * (8 * c ** 2 - 12 * a * c + 6 * a ** 2))
    J = t ** 3 * (a + 2 * bb + 2 * c) / 3
    Cw = t * a ** 2 * bb ** 2 / 12 * (2 * a ** 3 * bb + 3 * a ** 2 * bb ** 2 + 48 * c ** 4 + 112 * bb * c ** 3
                                      + 8 * a * c ** 3 + 48 * a * bb * c ** 2 + 12 * a ** 2 * c ** 2
                                      + 12 * a ** 2 * bb * c + 6 * a ** 3 * c) / (
        6 * a ** 2 * bb + (a + 2 * c) ** 3 - 24 * a * c ** 2)
    Ix = t * a ** 3 / 12 + 2 * bb * t * (a / 2) ** 2 + 2 * (t * c ** 3 / 12 + c * t * (a / 2 - c / 2) ** 2)
    Iy = (t * a * xb ** 2 + 2 * (t * bb ** 3 / 12 + bb * t * (bb / 2 - xb) ** 2)
          + 2 * c * t * (bb - xb) ** 2)
    return dict(A=A, Ix=Ix, Iy=Iy, xb=xb, m=m, x0=m + xb, J=J, Cw=Cw)


def U_yu(h, b, t):
    """U simples (c = 0): m = 3b²/(a + 6b); Cw = t a² b³ (2a + 3b) / (12 (a + 6b)).
    Sem enrijecedor a mesa vai da linha média da alma à ponta: b = bf − t/2 (no Ue, b = bf − t)."""
    a, bb = h - t, b - t / 2
    A = t * (a + 2 * bb)
    xb = bb ** 2 / (a + 2 * bb)
    m = 3 * bb ** 2 / (a + 6 * bb)
    Cw = t * a ** 2 * bb ** 3 * (2 * a + 3 * bb) / (12 * (a + 6 * bb))
    Ix = t * a ** 3 / 12 + 2 * bb * t * (a / 2) ** 2
    Iy = t * a * xb ** 2 + 2 * (t * bb ** 3 / 12 + bb * t * (bb / 2 - xb) ** 2)
    return dict(A=A, Ix=Ix, Iy=Iy, xb=xb, m=m, x0=m + xb, J=t ** 3 * (a + 2 * bb) / 3, Cw=Cw)


def Ue_nos(h, b, D, t):
    a, bb, c = h - t, b - t, D - t / 2
    return [(bb, a / 2 - c), (bb, a / 2), (0, a / 2), (0, -a / 2), (bb, -a / 2), (bb, -a / 2 + c)]


def cantoneira(b, h, t):
    """L abas b (horizontal) e h (vertical), cantos vivos, retângulos exatos e eixos principais."""
    A = t * (b + h - t)
    # retângulos: aba horizontal b×t; aba vertical t×(h−t) acima
    r1 = (b, t, b / 2, t / 2)
    r2 = (t, h - t, t / 2, t + (h - t) / 2)
    xc = (r1[0] * r1[1] * r1[2] + r2[0] * r2[1] * r2[2]) / A
    yc = (r1[0] * r1[1] * r1[3] + r2[0] * r2[1] * r2[3]) / A
    Ix = sum(w * hh ** 3 / 12 + w * hh * (cy - yc) ** 2 for w, hh, cx, cy in (r1, r2))
    Iy = sum(hh * w ** 3 / 12 + w * hh * (cx - xc) ** 2 for w, hh, cx, cy in (r1, r2))
    Ixy = sum(w * hh * (cx - xc) * (cy - yc) for w, hh, cx, cy in (r1, r2))
    med, rad = (Ix + Iy) / 2, math.hypot((Ix - Iy) / 2, Ixy)
    J = (b + h - t) * t ** 3 / 3  # linha média: (b − t/2 + h − t/2)·t³/3
    # CC no encontro das linhas médias (t/2, t/2)
    return dict(A=A, xc=xc, yc=yc, Ix=Ix, Iy=Iy, Ixy=Ixy, Imax=med + rad, Imin=med - rad,
                x0=t / 2 - xc, y0=t / 2 - yc, J=J)


def tubo_retangular(h, b, t):
    A = b * h - (b - 2 * t) * (h - 2 * t)
    Ix = (b * h ** 3 - (b - 2 * t) * (h - 2 * t) ** 3) / 12
    Iy = (h * b ** 3 - (h - 2 * t) * (b - 2 * t) ** 3) / 12
    Am, p = (b - t) * (h - t), 2 * ((b - t) + (h - t))
    return dict(A=A, Ix=Ix, Iy=Iy, J=4 * Am ** 2 * t / p)


def tubo_circular(D, t):
    d = D - 2 * t
    I = math.pi * (D ** 4 - d ** 4) / 64
    return dict(A=math.pi * (D ** 2 - d ** 2) / 4, Ix=I, Iy=I, J=2 * I)


if __name__ == "__main__":
    # autoconferência: integrador genérico × fórmulas fechadas de Yu (Ue 150x60x20x2 e U 150x50x2)
    ue = Ue_yu(150, 60, 20, 2)
    pf = parede_fina(Ue_nos(150, 60, 20, 2), [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5)], 2)
    for k, v in (("A", pf["A"]), ("Ix", pf["Ix"]), ("Iy", pf["Iy"]), ("J", pf["J"]), ("Cw", pf["Cw"])):
        assert abs(v - ue[k]) / ue[k] < 1e-6, (k, v, ue[k])
    assert abs(abs(pf["xs"]) - ue["x0"]) / ue["x0"] < 1e-6, (pf["xs"], ue["x0"])
    print("Ue 150x60x20x2 (Yu = integrador):", {k: round(v, 1) for k, v in ue.items()})
    # I por parede fina × fórmula fechada de Cw
    d, bf, tw, tf = 207, 133, 5.8, 8.4
    h0 = d - tf
    # malha só com t = tf (parede fina) para conferir Cw = tf bf³ h0²/24
    ii = parede_fina([(-bf / 2, h0 / 2), (0, h0 / 2), (bf / 2, h0 / 2), (0, -h0 / 2), (-bf / 2, -h0 / 2),
                      (bf / 2, -h0 / 2)], [(0, 1), (1, 2), (1, 3), (3, 4), (3, 5)], tf)
    assert abs(ii["Cw"] - tf * bf ** 3 * h0 ** 2 / 24) / ii["Cw"] < 1e-9
    print("W200x26.6 (r=10):", {k: round(v, 1) for k, v in I_laminado(207, 133, 5.8, 8.4, 10).items()})
    print("ok")
