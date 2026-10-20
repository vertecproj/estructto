"""Memória independente — lógica do CYPE 2016 (nbrcomphorm/comphorm) descrita em linguagem de engenharia
e recalculada à mão. Só stdlib; NÃO importa prossen_estrutural. Unidades: kN, m, kPa (kN/m²); fck em MPa.

Origem de cada regra (relatório software/verificacao/logica-cype-concreto.md, seção 1):
- constantes lidas da .rdata de nbrcomphorm.dll (0,27; 0,6; 0,7; 0,67; 0,2; 0,3; 300/200/800/350 mm;
  0,8 e 0,39 do Md,mín; 0,035; 0,15; 0,004; 0,04; 25/12,5/35; 0,005; 10; 140/200; η1 1,0/2,25/1,4);
- as fórmulas são as da NBR 6118 (itens citados); nenhum trecho do pseudocódigo foi transcrito.
"""
import math

ES = 210e6        # kPa (8.3.5)
GC, GS = 1.4, 1.15


def fcd(fck):
    return fck * 1e3 / GC                                   # kPa


def fctm(fck):                                              # 8.2.5 (2023)
    return (0.3 * fck ** (2 / 3) if fck <= 50 else 2.12 * math.log(1 + 0.1 * (fck + 8))) * 1e3


def fctm_cype(fck):                                         # CYPE: 0,3·fck^(2/3) em todas as classes
    return 0.3 * fck ** (2 / 3) * 1e3


def ecs(fck):                                               # 8.2.8, granito (αE = 1)
    eci = 5600 * math.sqrt(fck) if fck <= 50 else 21.5e3 * (fck / 10 + 1.25) ** (1 / 3)
    ai = min(1.0, 0.8 + 0.2 * fck / 80)
    return ai * eci * 1e3                                   # kPa


# ------------------------------------------------------------------ diagrama e seção (comphorm, 17.2.2)
def sig_c(e, fck):
    """Parábola-retângulo, pico 0,85·fcd (fck ≤ 50: εc2 = 2‰, εcu = 3,5‰, n = 2). e > 0 compressão."""
    if e <= 0:
        return 0.0
    f = 0.85 * fcd(fck)
    return f * (1 - (1 - e / 2e-3) ** 2) if e < 2e-3 else f


def sig_s(e, fyk):
    fyd = fyk * 1e3 / GS
    return max(-fyd, min(fyd, ES * e))


def perfil(x, H, dmax):
    """Deformação na fibra a distância u da borda comprimida, LN a x (pivôs A/B/C, 17.2.2)."""
    if x <= 3.5e-3 / (3.5e-3 + 10e-3) * dmax:              # domínio 2 (pivô A)
        k = 10e-3 / (dmax - x)
    elif x <= H:                                            # 3–4a (pivô B)
        k = 3.5e-3 / x
    else:                                                   # 5 (pivô C a 3/7 H)
        k = 2e-3 / (x - 3 / 7 * H)
    return lambda u: k * (x - u)


def esforcos_1d(x, b, H, barras, fck, fyk, nf=400):
    """(N, M) com N > 0 compressão, M em torno do meio de H; barras = [(u, A)] (u da borda comprimida)."""
    eps = perfil(x, H, max(u for u, _ in barras))
    N = M = 0.0
    du = H / nf
    for i in range(nf):
        u = (i + 0.5) * du
        s = sig_c(eps(u), fck) * b * du
        N += s
        M += s * (H / 2 - u)
    for u, A in barras:
        s = sig_s(eps(u), fyk) * A - (sig_c(eps(u), fck) * A if eps(u) > 0 else 0)  # desconta o furo
        N += s
        M += s * (H / 2 - u)
    return N, M


def MRd_1d(Nd, b, H, barras, fck, fyk):
    """Momento resistente para Nd (compressão > 0): bisseção na profundidade da LN."""
    lo, hi = 1e-5 * H, 50 * H
    if esforcos_1d(hi, b, H, barras, fck, fyk)[0] < Nd:
        return None
    for _ in range(80):
        mid = math.sqrt(lo * hi)
        if esforcos_1d(mid, b, H, barras, fck, fyk)[0] < Nd:
            lo = mid
        else:
            hi = mid
    return esforcos_1d(hi, b, H, barras, fck, fyk)[1]


def As_flexao_simples(b, d, Md, fck, fyk):
    """As de tração (m²) por parábola-retângulo (o CYPE integra a seção; sem λ = 0,8)."""
    def M(x):  # momento do concreto em torno da armadura com εs ≥ εyd (domínios 2–3)
        eps = perfil(x, 10 * d, d)
        nf, C, Mc = 400, 0.0, 0.0
        for i in range(nf):
            u = (i + 0.5) * x / nf
            s = sig_c(eps(u), fck) * b * x / nf
            C += s
            Mc += s * (d - u)
        return C, Mc
    lo, hi = 1e-6, 0.45 * d
    for _ in range(80):
        mid = (lo + hi) / 2
        if M(mid)[1] < Md:
            lo = mid
        else:
            hi = mid
    C, _ = M(hi)
    return C / (fyk * 1e3 / GS), hi


def As_simetrica(b, H, Nd, Md, fck, fyk, dl, n_por_face=2):
    """As total (m²) simétrica em 2 faces opostas (paralelas a b) que dá MRd(Nd) = Md."""
    def mr(As):
        a = As / (2 * n_por_face)
        return MRd_1d(Nd, b, H, [(dl, a * n_por_face), (H - dl, a * n_por_face)], fck, fyk)
    lo, hi = 0.0, 0.08 * b * H
    for _ in range(60):
        mid = (lo + hi) / 2
        m = mr(mid)
        if m is None or m < Md:
            lo = mid
        else:
            hi = mid
    return hi


# ------------------------------------------------------------------ flexão oblíqua exata (CYPE integra 2D)
def capacidade_obliqua(b, h, Nd, Mx, My, As_total, fck, fyk, dl, nf=40, nth=360):
    """Razão |M|/|M_res| na direção (Mx, My) para Nd, 4 barras de canto, integração 2D por fibras.
    Mx no plano de h (coordenada y ao longo de h), My no plano de b (x ao longo de b)."""
    a = As_total / 4
    bars = [(sx * (b / 2 - dl), sy * (h / 2 - dl)) for sx in (-1, 1) for sy in (-1, 1)]
    fib = [((i + 0.5) * b / nf - b / 2, (j + 0.5) * h / nf - h / 2) for i in range(nf) for j in range(nf)]
    dA = b * h / nf ** 2
    cant = [(sx * b / 2, sy * h / 2) for sx in (-1, 1) for sy in (-1, 1)]
    alvo = math.atan2(My, Mx)
    pts = []
    for k in range(nth):
        t = 2 * math.pi * k / nth
        c, s = math.cos(t), math.sin(t)  # direção da fibra mais comprimida (y·c + x·s)
        w = lambda p: p[1] * c + p[0] * s
        umax = max(w(p) for p in cant)
        H = umax - min(w(p) for p in cant)
        dmax = umax - min(w(p) for p in bars)

        def NM(x):
            eps = perfil(x, H, dmax)
            N = mx = my = 0.0
            for p in fib:
                sg = sig_c(eps(umax - w(p)), fck) * dA
                N += sg; mx += sg * p[1]; my += sg * p[0]
            for p in bars:
                e = eps(umax - w(p))
                sg = (sig_s(e, fyk) - (sig_c(e, fck) if e > 0 else 0)) * a
                N += sg; mx += sg * p[1]; my += sg * p[0]
            return N, mx, my
        lo, hi = 1e-5 * H, 50 * H
        if NM(hi)[0] < Nd:
            return None
        for _ in range(50):
            mid = math.sqrt(lo * hi)
            if NM(mid)[0] < Nd:
                lo = mid
            else:
                hi = mid
        _, mx, my = NM(hi)
        pts.append((math.atan2(my, mx), math.hypot(mx, my)))
    pts.sort()
    # interpolação no ângulo do alvo
    for (a1, r1), (a2, r2) in zip(pts, pts[1:] + [(pts[0][0] + 2 * math.pi, pts[0][1])]):
        aa = alvo if alvo >= a1 else alvo + 2 * math.pi
        if a1 <= aa <= a2:
            r = r1 + (r2 - r1) * (aa - a1) / (a2 - a1)
            return math.hypot(Mx, My) / r
    return None


# ------------------------------------------------------------------ cisalhamento (nbhcomp03, 17.4.2.2)
def vc0(bw, d, fck):
    return 0.6 * (0.7 * fctm(fck) / GC) * bw * d          # kN


def vc_cype(bw, d, h, fck, N, M):
    """CYPE: tensões da seção BRUTA (estádio I) nas fibras extremas, N > 0 compressão.
    - as duas fibras tracionadas → Vc = 0;
    - uma comprimida e outra tracionada → Vc = Vc0 (sem acréscimo);
    - as duas comprimidas → Vc = Vc0·(1 + M0'/|M|) ≤ 2Vc0, com M0' = σ(fibra menos comprimida)·W."""
    A, W = bw * h, bw * h ** 2 / 6
    s_sup, s_inf = N / A + abs(M) / W, N / A - abs(M) / W
    v0 = vc0(bw, d, fck)
    if s_sup <= 0 and s_inf <= 0:
        return 0.0
    if s_inf < 0:
        return v0
    if abs(M) < 1e-9:
        return 2 * v0
    return min(2 * v0, v0 * (1 + s_inf * W / abs(M)))


def vc_nbr(bw, d, h, fck, N, M):
    """NBR 17.4.2.2 b: flexo-compressão Vc = Vc0(1 + M0/Msd,máx) ≤ 2Vc0, M0 = N·W/A (anula a tensão
    da borda tracionada). Tração com LN fora (domínio 1: |M|/T ≤ d − h/2) → 0; senão Vc0."""
    A, W = bw * h, bw * h ** 2 / 6
    v0 = vc0(bw, d, fck)
    if N > 0:
        return 2 * v0 if abs(M) < 1e-9 else min(2 * v0, v0 * (1 + (N * W / A) / abs(M)))
    if N < 0 and abs(M) / (-N) <= d - h / 2:
        return 0.0
    return v0


def asw_s(Vd, Vc, d, fywk=500):                              # cm²/m, α = 90°
    return max(0.0, (Vd - Vc) / (0.9 * d * min(fywk / GS, 435) * 1e3)) * 1e4


def vrd2(bw, d, fck):
    return 0.27 * (1 - fck / 250) * fcd(fck) * bw * d


def smax_estribo(Vd, VRd2, d):                               # 18.3.3.2 (CYPE: 0,67 / 0,6d ≤ 300 / 0,3d ≤ 200)
    return min(0.6 * d, 0.30) if Vd <= 0.67 * VRd2 else min(0.3 * d, 0.20)


# ------------------------------------------------------------------ armadura mínima (nbhcomp02, 17.3.5.2.1)
def as_min_cype(b, h, d, fck, fyk):
    """CYPE (flexão): MRd ≥ Md,mín = 0,8·W0·fctk,sup, fctk,sup = 0,39·fck^(2/3); e ρ ≥ 0,15% (texto)."""
    W0 = b * h ** 2 / 6
    Mmin = 0.8 * W0 * 0.39 * fck ** (2 / 3) * 1e3
    As, _ = As_flexao_simples(b, d, Mmin, fck, fyk)
    return max(As, 0.0015 * b * h), Mmin


# ------------------------------------------------------------------ pilar (nbhcomp04, 11.3.3.4.3, 15.8)
def lambda1_cype(e1, h):
    return max(25 + 12.5 * e1 / h, 35.0)                    # sem αb e sem teto 90


def md_tot_cype(Nd, M1, h, b, le, fck):
    """e1 + e2 (curvatura aproximada) para λ ≤ 90; M1 ≥ Nd(0,015 + 0,03h)."""
    M1 = max(abs(M1), Nd * (0.015 + 0.03 * h))
    lam = le * math.sqrt(12) / h
    if lam <= lambda1_cype(abs(M1) / Nd, h):
        return M1, lam
    nu = Nd / (b * h * fcd(fck))
    r = min(0.005 / (h * (nu + 0.5)), 0.005 / h)
    return M1 + Nd * le ** 2 / 10 * r, lam


def gama_n(bmin):                                          # 13.2.3, Tabela 13.1 (2014)
    return max(1.0, 1.95 - 0.05 * bmin * 100)


# ------------------------------------------------------------------ fissuração (nbhcompfistrac, 17.3.3.2)
def eta1_cype(fyk):
    return {250: 1.0, 500: 2.25, 600: 1.4}[fyk]


def estadio2(b, d, As, ae):
    a, bb, c = b / 2, ae * As, -ae * As * d
    x = (-bb + math.sqrt(bb * bb - 4 * a * c)) / (2 * a)
    return x, b * x ** 3 / 3 + ae * As * (d - x) ** 2


def wk(phi_mm, sig_kPa, rho, fck, eta1):
    s = sig_kPa
    w1 = phi_mm / (12.5 * eta1) * s / ES * 3 * s / fctm(fck)
    w2 = phi_mm / (12.5 * eta1) * s / ES * (4 / rho + 45)
    return min(w1, w2)


def acri_por_barra(b, h, d, n, phi, cob_lat):
    """Acri de cada barra (17.3.3.2): retângulo até 7,5φ do eixo, limitado pelas faces e pela metade
    da distância às barras vizinhas. Barras em uma camada, igualmente espaçadas."""
    xs = [cob_lat + (b - 2 * cob_lat) * i / (n - 1) for i in range(n)]
    yb = h - d                                               # do eixo da barra à face inferior
    out = []
    for i, xi in enumerate(xs):
        esq = min(7.5 * phi, xi if i == 0 else (xi - xs[i - 1]) / 2)
        dir_ = min(7.5 * phi, b - xi if i == n - 1 else (xs[i + 1] - xi) / 2)
        out.append((esq + dir_) * (min(7.5 * phi, yb) + 7.5 * phi))
    return out


# ------------------------------------------------------------------ flecha (compflecha, 17.3.2.1)
def flecha_branson(b, h, d, As, Ma, L, q, fck, t0=1.0):
    Ic = b * h ** 3 / 12
    Mr = 1.5 * fctm(fck) * Ic / (h / 2)
    _, I2 = estadio2(b, d, As, ES / ecs(fck))
    r3 = min(1.0, (Mr / Ma) ** 3)
    Ie = min(Ic, r3 * Ic + (1 - r3) * I2)
    fi = 5 / 384 * q * L ** 4 / (ecs(fck) * Ie)
    xi0 = 0.68 * 0.996 ** t0 * t0 ** 0.32
    return fi, fi * (1 + (2 - xi0)), Ie, Mr


if __name__ == "__main__":
    # verificações internas mínimas (soluções fechadas)
    assert abs(vrd2(0.2, 0.455, 25) - 0.27 * 0.9 * 25e3 / 1.4 * 0.2 * 0.455) < 1e-6
    assert abs(lambda1_cype(0.0, 0.2) - 35) < 1e-12 and abs(lambda1_cype(1.2, 0.2) - 100) < 1e-12
    assert abs(gama_n(0.14) - 1.25) < 1e-12
    print("ok")
