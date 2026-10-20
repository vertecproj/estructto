"""Memória independente — NBR 8800:2008 (verificador-calculos, ficha 013, 2026-10-20).

Só stdlib. NÃO importa prossen_estrutural. Unidades: kN, cm (tensões em kN/cm²), resultados em kN e kN·m.
Fórmulas escritas a partir dos itens da norma (citados em cada função) e conferidas com os exemplos
publicados citados em ../2026-10-20-aco.md. E = 20000 kN/cm², G = 7700 kN/cm², γa1 = 1,10, γa2 = 1,35.
"""
import math

E, G, GA1, GA2 = 20000.0, 7700.0, 1.10, 1.35


# 5.3.3 / anexo E ------------------------------------------------------------------------------
def chi(l0):
    """5.3.3.1: χ = 0,658^(λ0²) (λ0 ≤ 1,5); χ = 0,877/λ0² (λ0 > 1,5)."""
    return 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2


def Ne_flexao(I, KL):
    """E.1.1: Ne = π²EI/(KL)²."""
    return math.pi ** 2 * E * I / KL ** 2


def Nez(Cw, J, r0, KzLz):
    """E.1.1 c): Nez = [π²ECw/(KzLz)² + GJ]/r0²."""
    return (math.pi ** 2 * E * Cw / KzLz ** 2 + G * J) / r0 ** 2


def Ne_mono(Nflex, Nz, e0, r0):
    """E.1.3 (seção monossimétrica): força de flexo-torção com o eixo de simetria (excentricidade e0
    do CC na direção do eixo de simetria):
    N = (N1 + Nz)/(2[1 − (e0/r0)²])·[1 − √(1 − 4·N1·Nz·[1 − (e0/r0)²]/(N1 + Nz)²)]."""
    a = 1 - (e0 / r0) ** 2
    return (Nflex + Nz) / (2 * a) * (1 - math.sqrt(1 - 4 * Nflex * Nz * a / (Nflex + Nz) ** 2))


def Ne_cubica(Nex, Ney, Nez_, x0, y0, r0):
    """E.1.4 (seção assimétrica): menor raiz de
    (N − Nex)(N − Ney)(N − Nez) − N²(N − Ney)(x0/r0)² − N²(N − Nex)(y0/r0)² = 0 (bisseção em (0, min])."""
    f = lambda N: ((N - Nex) * (N - Ney) * (N - Nez_) - N ** 2 * (N - Ney) * (x0 / r0) ** 2
                   - N ** 2 * (N - Nex) * (y0 / r0) ** 2)
    lo, hi = 0.0, min(Nex, Ney, Nez_)
    flo = f(lo)
    for _ in range(200):
        mid = (lo + hi) / 2
        if (f(mid) > 0) == (flo > 0):
            lo, flo = mid, f(mid)
        else:
            hi = mid
    return (lo + hi) / 2


# anexo F -----------------------------------------------------------------------------------------
def Qs_grupo4(bt, fy):
    """F.2 c) grupo 4 (mesas de I/U laminados, AL): limites 0,56√(E/fy) e 1,03√(E/fy)."""
    r = math.sqrt(E / fy)
    if bt <= 0.56 * r:
        return 1.0
    if bt <= 1.03 * r:
        return 1.415 - 0.74 * bt * math.sqrt(fy / E)
    return 0.69 * E / (fy * bt ** 2)


def Qs_grupo5(bt, fy, h_tw):
    """F.2 d) grupo 5 (mesas de I soldado): kc = 4/√(h/tw) ∈ [0,35; 0,76];
    limites 0,64√(E/(fy/kc)) e 1,17√(E/(fy/kc))."""
    kc = min(max(4 / math.sqrt(h_tw), 0.35), 0.76)
    r = math.sqrt(E * kc / fy)
    if bt <= 0.64 * r:
        return 1.0, kc
    if bt <= 1.17 * r:
        return 1.415 - 0.65 * bt * math.sqrt(fy / (kc * E)), kc
    return 0.90 * E * kc / (fy * bt ** 2), kc


def Qs_grupo3(bt, fy):
    """F.2 a) grupo 3 (cantoneiras simples): 0,45 e 0,91√(E/fy); Qs = 1,340 − 0,76(b/t)√(fy/E);
    0,53E/(fy(b/t)²)."""
    r = math.sqrt(E / fy)
    if bt <= 0.45 * r:
        return 1.0
    if bt <= 0.91 * r:
        return 1.340 - 0.76 * bt * math.sqrt(fy / E)
    return 0.53 * E / (fy * bt ** 2)


def bef_AA(b, t, sigma, ca=0.34):
    """F.3.2: bef = 1,92t√(E/σ)[1 − (ca/(b/t))√(E/σ)] ≤ b (ca = 0,38 tubos; 0,34 demais)."""
    r = math.sqrt(E / sigma)
    return min(b, 1.92 * t * r * (1 - ca / (b / t) * r))


# anexo G (tabela G.1) — I duplamente simétrico, flexão em x ---------------------------------------
def flt(Lb, ry, Iy, J, Cw, Wx, Zx, fy, Cb=1.0):
    """Tabela G.1 FLT: λ = Lb/ry; λp = 1,76√(E/fy); β1 = (fy − σr)W/(EJ), σr = 0,3fy;
    λr = 1,38√(IyJ)/(ryJβ1)·√(1 + √(1 + 27Cwβ1²/Iy)); Mr = (fy − σr)W;
    Mcr = (CbπEIy/Lb²)... = Cb·π²EIy/Lb²·√(Cw/Iy·(1 + 0,039JLb²/Cw)). Retorna (Mn kN·cm, λ, λp, λr, Mcr)."""
    lam = Lb / ry
    lp = 1.76 * math.sqrt(E / fy)
    b1 = 0.7 * fy * Wx / (E * J)
    lr = 1.38 * math.sqrt(Iy * J) / (ry * J * b1) * math.sqrt(1 + math.sqrt(1 + 27 * Cw * b1 ** 2 / Iy))
    Mpl, Mr = Zx * fy, 0.7 * fy * Wx
    Mcr = Cb * math.pi ** 2 * E * Iy / Lb ** 2 * math.sqrt(Cw / Iy * (1 + 0.039 * J * Lb ** 2 / Cw))
    if lam <= lp:
        Mn = Mpl
    elif lam <= lr:
        Mn = min(Mpl, Cb * (Mpl - (Mpl - Mr) * (lam - lp) / (lr - lp)))
    else:
        Mn = min(Mpl, Mcr)
    return Mn, lam, lp, lr, Mcr


def flm(bt, Wc, Zx, fy, soldado=False, kc=None):
    """Tabela G.1 FLM: λ = b/t; λp = 0,38√(E/fy); λr = 0,83√(E/(fy − σr)) (laminado) ou
    0,95√(E/((fy − σr)/kc)) (soldado); Mr = (fy − σr)Wc; Mcr = 0,69EWc/λ² (lam.) ou 0,90EkcWc/λ² (sold.)."""
    lp = 0.38 * math.sqrt(E / fy)
    lr = 0.95 * math.sqrt(E * kc / (0.7 * fy)) if soldado else 0.83 * math.sqrt(E / (0.7 * fy))
    Mpl, Mr = Zx * fy, 0.7 * fy * Wc
    if bt <= lp:
        Mn = Mpl
    elif bt <= lr:
        Mn = Mpl - (Mpl - Mr) * (bt - lp) / (lr - lp)
    else:
        Mn = (0.90 * E * kc if soldado else 0.69 * E) * Wc / bt ** 2
    return Mn, lp, lr


def fla(h_tw, Wx, Zx, fy):
    """Tabela G.1 FLA: λ = h/tw; λp = 3,76√(E/fy); λr = 5,70√(E/fy); Mr = fy·W. λ > λr → anexo H."""
    lp, lr = 3.76 * math.sqrt(E / fy), 5.70 * math.sqrt(E / fy)
    Mpl, Mr = Zx * fy, fy * Wx
    if h_tw <= lp:
        return Mpl, lp, lr
    if h_tw <= lr:
        return Mpl - (Mpl - Mr) * (h_tw - lp) / (lr - lp), lp, lr
    return None, lp, lr


# 5.4.3 ---------------------------------------------------------------------------------------------
def cortante(lam, Aw, fy, kv=5.0):
    """5.4.3.1.1: λp = 1,10√(kvE/fy); λr = 1,37√(kvE/fy); Vpl = 0,60Awfy;
    VRd = Vpl/γa1 | (λp/λ)Vpl/γa1 | 1,24(λp/λ)²Vpl/γa1."""
    lp, lr = 1.10 * math.sqrt(kv * E / fy), 1.37 * math.sqrt(kv * E / fy)
    Vpl = 0.6 * Aw * fy
    if lam <= lp:
        return Vpl / GA1, lp, lr, Vpl
    if lam <= lr:
        return lp / lam * Vpl / GA1, lp, lr, Vpl
    return 1.24 * (lp / lam) ** 2 * Vpl / GA1, lp, lr, Vpl


def interacao(n, mx, my):
    """5.5.1.2: n = NSd/NRd ≥ 0,2 → n + 8/9(mx + my); n < 0,2 → n/2 + (mx + my)."""
    return n + 8 / 9 * (mx + my) if n >= 0.2 else n / 2 + (mx + my)


def Cb(Mmax, MA, MB, MC, Rm=1.0):
    """5.4.2.3: Cb = 12,5Mmáx/(2,5Mmáx + 3MA + 4MB + 3MC)·Rm ≤ 3,0."""
    return min(3.0, 12.5 * Mmax / (2.5 * Mmax + 3 * MA + 4 * MB + 3 * MC) * Rm)


def B2(sN, dh, sH, h, Rs=0.85):
    """Anexo D: B2 = 1/(1 − Δh·ΣN/(Rs·h·ΣH))."""
    return 1 / (1 - dh * sN / (Rs * h * sH))


if __name__ == "__main__":
    import importlib.util
    import os
    sp = importlib.util.spec_from_file_location("sec", os.path.join(os.path.dirname(__file__), "aco-secoes.py"))
    sec = importlib.util.module_from_spec(sp)
    sp.loader.exec_module(sec)
    fy = 25.0  # A36
    # W 200 x 26,6 (Gerdau: d 207, bf 133, tw 5,8, tf 8,4, h 190, d' 170 → R = 10 mm)
    w = sec.I_laminado(207, 133, 5.8, 8.4, 10)
    cm = {k: w[k] / 10 ** p for k, p in (("A", 2), ("Ix", 4), ("Iy", 4), ("Wx", 3), ("Zx", 3), ("Wy", 3),
                                         ("Zy", 3), ("Cw", 6), ("J_h0", 4), ("rx", 1), ("ry", 1))}
    print("W200x26.6 [cm]:", {k: round(v, 3) for k, v in cm.items()})
    r0 = math.hypot(cm["rx"], cm["ry"])
    Nex, Ney = Ne_flexao(cm["Ix"], 300), Ne_flexao(cm["Iy"], 300)
    Nz = Nez(cm["Cw"], cm["J_h0"], r0, 300)
    l0 = math.sqrt(cm["A"] * fy / min(Nex, Ney, Nz))
    print(f"compressão KL=3 m: Nex={Nex:.1f} Ney={Ney:.1f} Nez={Nz:.1f} λ0={l0:.4f} χ={chi(l0):.4f} "
          f"NcRd={chi(l0) * cm['A'] * fy / GA1:.1f} kN")
    for Lb, cb in ((100, 1), (300, 1), (300, 1.3), (600, 1), (600, 2.0)):
        Mn, lam, lp, lr, Mcr = flt(Lb, cm["ry"], cm["Iy"], cm["J_h0"], cm["Cw"], cm["Wx"], cm["Zx"], fy, cb)
        print(f"FLT Lb={Lb} Cb={cb}: λ={lam:.2f} λp={lp:.2f} λr={lr:.2f} Mcr={Mcr / 100:.2f} "
              f"MRd={Mn / GA1 / 100:.3f} kN·m")
    print("χ(0,5)=%.5f χ(1,5)=%.5f/%.5f χ(2)=%.5f" % (chi(0.5), 0.658 ** 2.25, 0.877 / 2.25, chi(2.0)))
    print("Qs grupo 4 b/t=18,75 A36: %.4f; b/t=30: %.4f" % (Qs_grupo4(18.75, fy), Qs_grupo4(30, fy)))
    print("Cb engastada-engastada uniforme: %.4f; diagrama com quartos nulos: %.2f"
          % (Cb(1 / 12, 1 / 96, 1 / 24, 1 / 96), Cb(1, 0, 0, 0)))
