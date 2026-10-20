"""Memória independente — lógica do CYPE 2016 para barras de aço (nbr8800.dll + seccion.dll), PFF
(nbr14762.dll) e ligações (unmetcomp.dll), descrita em linguagem de engenharia e recalculada à mão.
Só stdlib; NÃO importa prossen_estrutural. Unidades: kN, m, kN/m² (kPa).

Origem de cada regra: relatório software/verificacao/logica-cype-aco.md, seção 1 (funções e constantes
lidas do pseudocódigo do Ghidra em %USERPROFILE%/cype-ghidra/<dll>/out/decomp). As fórmulas são as da
NBR 8800:2008 / NBR 14762:2010 (itens citados) ou, quando o CYPE usa critério próprio, a expressão de
engenharia equivalente; nenhum trecho de pseudocódigo foi transcrito.
"""
import math

E, G, NU = 200e6, 77e6, 0.3
GA1 = 1.10


# ------------------------------------------------------------------ NBR 8800 — compressão (5.3, anexos E/F)
def chi(l0):
    """5.3.3: 0,658^λ0² (λ0 ≤ 1,5) | 0,877/λ0² (CYPE: nblcalc, constantes 1,5 e 0,877)."""
    return 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2


def ne_dupla_simetria(Ix, Iy, Cw, J, r0, KLx, KLy, KLz):
    """E.1.1: CYPE escolhe pelo tipo de simetria; dupla simetria → menor entre Nex, Ney e Nez."""
    nex = math.pi ** 2 * E * Ix / KLx ** 2
    ney = math.pi ** 2 * E * Iy / KLy ** 2
    nez = (math.pi ** 2 * E * Cw / KLz ** 2 + G * J) / r0 ** 2
    return min(nex, ney, nez)


def nc_rd(A, fy, Ne, Q=1.0):
    l0 = math.sqrt(Q * A * fy / Ne)
    return chi(l0) * Q * A * fy / GA1


# ------------------------------------------------------------------ NBR 8800 — flexão (5.4.2, anexo G)
def mcr_geral(Iy, Cw, J, Lb, C1=1.0, k=1.0, kw=1.0, zg=0.0):
    """seccion.dll (secpandeo): momento crítico elástico geral
    Mcr = C1·π²EIy/(kLb)²·[√((k/kw)²·Cw/Iy + (kLb)²GJ/(π²EIy) + zg²) − zg].
    Com k = kw = 1 e zg = 0 é a expressão da tabela G.1 (0,039 = G/(π²E))."""
    a = C1 * math.pi ** 2 * E * Iy / (k * Lb) ** 2
    return a * (math.sqrt((k / kw) ** 2 * Cw / Iy + (k * Lb) ** 2 * G * J / (math.pi ** 2 * E * Iy) + zg ** 2) - zg)


def mrd_I_laminado(fy, Zx, Wx, Iy, ry, J, Cw, bt_mesa, h_tw, Lb, Cb=1.0):
    """Tabela G.1, I laminado de alma não esbelta (h/tw ≤ 5,70√(E/fy)); MRd ≤ 1,5·W·fy/γa1."""
    Mpl, sr = Zx * fy, 0.3 * fy
    Mr = (fy - sr) * Wx

    def trecho(lam, lp, lr, mcr, cb=1.0, mr=Mr):
        if lam <= lp:
            return Mpl
        if lam <= lr:
            return min(cb * (Mpl - (Mpl - mr) * (lam - lp) / (lr - lp)), Mpl)
        return min(mcr, Mpl)
    # FLT (CYPE: só se houver comprimento destravado; Cb = C1 informado, faixa 1,0–3,0)
    b1 = (fy - sr) * Wx / (E * J)
    lr = 1.38 * math.sqrt(Iy * J) / (ry * J * b1) * math.sqrt(1 + math.sqrt(1 + 27 * Cw * b1 ** 2 / Iy))
    flt = trecho(Lb / ry, 1.76 * math.sqrt(E / fy), lr, mcr_geral(Iy, Cw, J, Lb, Cb), Cb) if Lb > 0 else Mpl
    flm = trecho(bt_mesa, 0.38 * math.sqrt(E / fy), 0.83 * math.sqrt(E / (fy - sr)), 0.69 * E * Wx / bt_mesa ** 2)
    fla = trecho(h_tw, 3.76 * math.sqrt(E / fy), 5.70 * math.sqrt(E / fy), None, mr=fy * Wx)
    return min(flt, flm, fla, 1.5 * Wx * fy) / GA1


def cb_norma(Mmax, MA, MB, MC):
    """5.4.2.3 (Rm = 1), limitado a 3,0. O CYPE NÃO calcula: usa o C1 informado na barra (padrão 1,0)."""
    return min(12.5 * Mmax / (2.5 * Mmax + 3 * MA + 4 * MB + 3 * MC), 3.0)


# ------------------------------------------------------------------ tração, interação, esbeltez
def nt_rd_cype(A, fy):
    """CYPE (nblcomp01, 5.2): só escoamento da seção bruta A·fy/γa1 — sem ruptura da seção líquida."""
    return A * fy / GA1


def interacao(N, NRd, Mx, MxRd, My, MyRd):
    """5.5.1.2 (CYPE nblcomp06: 0,2; 8/9; n/2)."""
    n = abs(N) / NRd
    m = abs(Mx) / MxRd + abs(My) / MyRd
    return n + 8 / 9 * m if n >= 0.2 else n / 2 + m


# ------------------------------------------------------------------ cortante de tubo circular (5.4.3.6)
def vrd_tubo_circular_cype(A, D, t, Lv, fy):
    """CYPE (nblcomp05): espessura de cálculo td = 0,93·t no D/t; τcr = MAIOR de
    1,60E/(√(Lv/D)(D/td)^1,25) e 0,78E/(D/td)^1,5, cada um ≤ 0,60fy; VRd = 0,5·τcr·A/γa1."""
    td = 0.93 * t
    t1 = min(1.60 * E / (math.sqrt(Lv / D) * (D / td) ** 1.25), 0.6 * fy)
    t2 = min(0.78 * E / (D / td) ** 1.5, 0.6 * fy)
    return 0.5 * max(t1, t2) * A / GA1


# ------------------------------------------------------------------ torção (5.5.2) e Von Mises (CYPE)
def trd_tubo_circular(D, t, L, fy):
    """5.5.2.2: WT = π(D − t)²t/2; τcr = maior de 1,23E/(√(L/D)(D/t)^1,25) e 0,60E/(D/t)^1,5, ≤ 0,6fy."""
    WT = math.pi * (D - t) ** 2 * t / 2
    t1 = min(1.23 * E / (math.sqrt(L / D) * (D / t) ** 1.25), 0.6 * fy)
    t2 = min(0.60 * E / (D / t) ** 1.5, 0.6 * fy)
    return max(t1, t2) * WT / GA1, WT


def trd_tubo_retangular(B, H, t, fy, h_plano=None):
    """5.5.2.3: WT = 2(B − t)(H − t)t − 4,5(4 − π)t³; h/t ≤ 2,45√(E/fy): 0,6fy;
    ≤ 3,07√(E/fy): 0,6fy·2,45√(E/fy)/(h/t); ≤ 260: 0,458π²E/(h/t)² (CYPE grava 0,46·π²)."""
    WT = 2 * (B - t) * (H - t) * t - 4.5 * (4 - math.pi) * t ** 3
    h = h_plano if h_plano is not None else max(B, H) - 3 * t
    ht, r = h / t, math.sqrt(E / fy)
    if ht <= 2.45 * r:
        f = 0.6 * fy
    elif ht <= 3.07 * r:
        f = 0.6 * fy * 2.45 * r / ht
    elif ht <= 260:
        f = 0.46 * math.pi ** 2 * E / ht ** 2
    else:
        return None, WT
    return f * WT / GA1, WT


def trd_aberta(J, tmax, fy):
    """CYPE (nblcomp07, critério próprio para seções abertas e maciças): TRd = 0,6·fy·WT/γa1, WT = J/tmáx."""
    WT = J / tmax
    return 0.6 * fy * WT / GA1, WT


def von_mises(sigma, tau, fy, TSd, TRd):
    """CYPE (nblcomp09 + seccion): só se TSd > 20 % de TRd; σVM = √(σ² + 3τ²) ≤ fy/γa1 (taxa)."""
    if abs(TSd) <= 0.20 * TRd:
        return None
    return math.sqrt(sigma ** 2 + 3 * tau ** 2) / (fy / GA1)


def von_mises_I(N, Mx, Vy, T, p, fy, TRd):
    """Von Mises do CYPE num I duplamente simétrico, avaliado em pontos da seção (seccion.dll:
    σ = N/A + M·y/Ix; τ de cortante por Jourawski V·S/(Ix·t); τ de torção T·t/J no elemento).
    Pontos: ponta da mesa (σ máx., τ de torção na mesa), junção alma–mesa e linha neutra da alma."""
    d, bf, tw, tf, Ix, A, J = p["d"], p["bf"], p["tw"], p["tf"], p["Ix"], p["A"], p["J"]
    Sf = bf * tf * (d - tf) / 2
    Sna = Sf + tw * (d / 2 - tf) ** 2 / 2
    pts = [(abs(N) / A + abs(Mx) * d / 2 / Ix, abs(T) * tf / J),
           (abs(N) / A + abs(Mx) * (d / 2 - tf) / Ix, abs(Vy) * Sf / (Ix * tw) + abs(T) * tw / J),
           (abs(N) / A, abs(Vy) * Sna / (Ix * tw) + abs(T) * tw / J)]
    return max(von_mises(s, t, fy, T, TRd) or 0.0 for s, t in pts) if abs(T) > 0.20 * TRd else None


# ------------------------------------------------------------------ NBR 14762 — distorcional (CYPE)
def ncrd_dist_cype(A, fy, bw, bf, D, t, gama=1.20):
    """CYPE (nbccomp02), seção Ue não dispensada pela tabela 11: Ndist por expressão simplificada
    (forma do AISI S100-2007 C4.2, provável): kd = 0,1(bf·D/(bw·t))^1,4 em [0,05; 8];
    σdist = kd·π²E/(12(1 − ν²))·(t/bf)²; λdist = √(fy/σdist); χdist (9.7.3): 1 se λ ≤ 0,561,
    senão (1 − 0,25/λ^1,2)/λ^1,2; Nc,Rd,dist = χdist·A·fy/γ."""
    kd = min(max(0.1 * (bf * D / (bw * t)) ** 1.4, 0.05), 8.0)
    sd = kd * math.pi ** 2 * E / (12 * (1 - NU ** 2)) * (t / bf) ** 2
    lam = math.sqrt(fy / sd)
    x = 1.0 if lam <= 0.561 else (1 - 0.25 / lam ** 1.2) / lam ** 1.2
    return x * A * fy / gama, dict(kd=kd, sigma_dist=sd, lambda_dist=lam, chi_dist=x)


# ------------------------------------------------------------------ ligações (unmetcomp / tabela 14)
def limites_parafusos_cype(db, t):
    """CYPE (umnbrcomp): s,mín = 2,7db; s,máx = 14t ≤ 180 mm (caso 'sujeito à corrosão' de 6.3.10);
    borda máx = 12t ≤ 150 mm (6.3.12)."""
    return dict(s_min=2.7 * db, s_max=min(14 * t, 0.180), e_max=min(12 * t, 0.150))


# ------------------------------------------------------------------ perfis (geometria fechada)
def perfil_I(d, bf, tw, tf, r):
    """I laminado com concordâncias (área com k = 4 − π, como em seccion.dll); J e Cw sem concordância."""
    hw = d - 2 * tf
    af = (1 - math.pi / 4) * r ** 2
    e = (10 - 3 * math.pi) / (12 - 3 * math.pi) * r
    A = 2 * tf * bf + hw * tw + 4 * af
    yf, xf = d / 2 - tf - e, tw / 2 + e
    Ix = bf * d ** 3 / 12 - (bf - tw) * hw ** 3 / 12 + 4 * af * yf ** 2
    Iy = 2 * tf * bf ** 3 / 12 + hw * tw ** 3 / 12 + 4 * af * xf ** 2
    Zx = bf * tf * (d - tf) + tw * hw ** 2 / 4 + 4 * af * yf
    J = (2 * bf * tf ** 3 + (d - tf) * tw ** 3) / 3
    Cw = tf * bf ** 3 * (d - tf) ** 2 / 24
    rx, ry = math.sqrt(Ix / A), math.sqrt(Iy / A)
    return dict(A=A, Ix=Ix, Iy=Iy, Zx=Zx, Wx=2 * Ix / d, J=J, Cw=Cw, rx=rx, ry=ry, r0=math.hypot(rx, ry),
                bt_mesa=bf / (2 * tf), h_tw=(hw - 2 * r) / tw, Aw=d * tw, tmax=max(tw, tf),
                d=d, bf=bf, tw=tw, tf=tf)


def casos():
    fy = 250e3                                            # A-36 (kPa)
    w200 = perfil_I(0.207, 0.133, 0.0058, 0.0084, 0.010)  # W 200 x 26,6 (Gerdau)
    w310 = perfil_I(0.310, 0.165, 0.0058, 0.0097, 0.010)  # W 310 x 38,7
    out = {}
    p = w200
    out["C1 NcRd W200x26,6 KL=3 m"] = nc_rd(p["A"], fy, ne_dupla_simetria(p["Ix"], p["Iy"], p["Cw"], p["J"], p["r0"], 3, 3, 3))
    q = w310
    out["C2 MRd W310x38,7 Lb=4 m Cb=1"] = mrd_I_laminado(fy, q["Zx"], q["Wx"], q["Iy"], q["ry"], q["J"], q["Cw"], q["bt_mesa"], q["h_tw"], 4.0)
    cb = cb_norma(1.0, 0.75, 1.0, 0.75)                   # biapoiada, carga uniforme
    out["C3 Cb norma (q uniforme)"] = cb
    out["C3 MRd W310 Lb=4 m, Cb(norma)"] = mrd_I_laminado(fy, q["Zx"], q["Wx"], q["Iy"], q["ry"], q["J"], q["Cw"], q["bt_mesa"], q["h_tw"], 4.0, cb)
    out["C4 NtRd CYPE W200 (An = 0,75Ag, Ct = 0,85: ignorados)"] = nt_rd_cype(p["A"], fy)
    D, t = 0.3238, 0.0027                                 # tubo D/t = 120
    A = math.pi * (D ** 2 - (D - 2 * t) ** 2) / 4
    out["C5 VRd tubo 323,8x2,7 Lv=3 m"] = vrd_tubo_circular_cype(A, D, t, 3.0, fy)
    out["C6 TRd tubo 168,3x6,4 L=3 m"] = trd_tubo_circular(0.1683, 0.0064, 3.0, fy)[0]
    out["C7 TRd TR 200x100x6"] = trd_tubo_retangular(0.100, 0.200, 0.006, fy)[0]
    trd, wt = trd_aberta(p["J"], p["tmax"], fy)
    out["C8 TRd W200 (aberta, critério CYPE)"] = trd
    out["C9 Von Mises W200 N100 M30 V60 T0,9"] = von_mises_I(100, 30, 60, 0.9, p, fy, trd)
    bw, bf, Dl, tl = 0.150, 0.100, 0.010, 0.0015          # Ue 150x100x10x1,5 (linha média aproximada)
    Al = tl * ((bw - tl) + 2 * (bf - tl) + 2 * (Dl - tl / 2))
    out["C10 NcRd,dist Ue150x100x10x1,5"] = ncrd_dist_cype(Al, fy, bw, bf, Dl, tl)[0]
    out["C11 esbeltez de tração L/r = 320"] = "CYPE não verifica (só 200 na compressão)"
    out["C12 limites parafuso 3/4\" t=8 mm"] = limites_parafusos_cype(0.01905, 0.008)
    return out


if __name__ == "__main__":
    for k, v in casos().items():
        print(f"{k}: {v}")
