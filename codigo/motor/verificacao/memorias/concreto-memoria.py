"""Memória de cálculo independente — concreto armado (NBR 6118:2023, itens confirmados em fonte pública).

Escrita ANTES de rodar o código (ficha 014). Só stdlib. NÃO importa prossen_estrutural.
Unidades: kN, cm, kN/cm² (1 MPa = 0,1 kN/cm²), kN·cm salvo indicação.

Fontes:
  [N]  NBR 6118:2014/2023 — itens citados em cada linha (fórmulas; texto não copiado).
  [BF] Bastos, Fundamentos do Concreto Armado, UNESP set/2023 (segue a NBR 6118:2023):
       Eq. 2.5–2.8 (fctm, inclusive fct,m = 2,12·ln[1 + 0,1(fck + 8)] para fck > 50),
       Eq. 2.11–2.13 (Eci, Ecs, αi), Eq. 2.21 (εc2), Tabela 2.3 (η1 = 1,00/2,25/1,00 para
       CA-25/CA-50/CA-60, Tabela 8.2 da NBR 6118:2023), Tabela 2.5 (NBR 7480).
  [UFU] TCC UFU "Atualizações ABNT NBR 6118" (repositorio.ufu.br/handle/123456789/38372),
       §2.6 (Tabela 8.1 removida em 2023), §2.7 (ηc = (40/fck)^(1/3)), §2.9 (η1 por categoria),
       §2.19 (αb = 1 em balanço).
Rodar:  python concreto-memoria.py   (imprime a memória).
"""
import math

GC, GS, ES = 1.4, 1.15, 21000.0  # γc, γs (12.4.1), Es = 210 GPa (8.3.5) em kN/cm²


# ------------------------------------------------------------------ 8.2 materiais (MPa)
def fctm(fck):                       # 8.2.5 [BF Eq. 2.5 e 2.8]
    return 0.3 * fck ** (2 / 3) if fck <= 50 else 2.12 * math.log(1 + 0.1 * (fck + 8))


def eci(fck, aE=1.0):                # 8.2.8 [BF Eq. 2.11/2.12]
    return aE * 5600 * math.sqrt(fck) if fck <= 50 else 21.5e3 * aE * (fck / 10 + 1.25) ** (1 / 3)


def ecs(fck, aE=1.0):                # 8.2.8 αi = 0,8 + 0,2 fck/80 ≤ 1 [BF Eq. 2.13]
    return min(1.0, 0.8 + 0.2 * fck / 80) * eci(fck, aE)


# Tabela 8.1 da NBR 6118:2014 (removida em 2023 [UFU §2.6]; conferência por arredondamento, GPa)
TAB_8_1 = {20: (25, 21), 25: (28, 24), 30: (31, 27), 35: (33, 29), 40: (35, 32), 45: (38, 34),
           50: (40, 37), 60: (42, 40), 70: (43, 42), 80: (45, 45), 90: (47, 47)}


def bloco(fck):
    """17.2.2 / 8.2.10.1: λ, αc, ηc (2023), εc2, εcu, n, x/d lim (14.6.4.3)."""
    lam = 0.8 if fck <= 50 else 0.8 - (fck - 50) / 400
    ac = 0.85 if fck <= 50 else 0.85 * (1 - (fck - 50) / 200)
    eta = 1.0 if fck <= 40 else (40 / fck) ** (1 / 3)          # [UFU §2.7]
    ec2 = 2.0e-3 if fck <= 50 else 2.0e-3 + 0.085e-3 * (fck - 50) ** 0.53
    ecu = 3.5e-3 if fck <= 50 else 2.6e-3 + 35e-3 * ((90 - fck) / 100) ** 4
    n = 2.0 if fck <= 50 else 1.4 + 23.4 * ((90 - fck) / 100) ** 4
    xd = 0.45 if fck <= 50 else 0.35
    return dict(lam=lam, ac=ac, eta=eta, ec2=ec2, ecu=ecu, n=n, xd=xd)


# Tabela 17.3 (ρmín %, CA-50, γc = 1,4, γs = 1,15; inalterada em 2023 — Bastos, Flexão
# Normal Simples — Vigas, UNESP out/2023)
RHO_MIN = {20: .150, 25: .150, 30: .150, 35: .164, 40: .179, 45: .194, 50: .208, 55: .211,
           60: .219, 65: .226, 70: .233, 75: .239, 80: .245, 85: .251, 90: .256}


# ------------------------------------------------------------------ 17.2 flexão simples
def flexao(b, d, Md, fck, fyk=500):
    """Retangular simples, bloco retangular 0,8x (λx) com σcd = αc·ηc·fcd. Md kN·cm."""
    p = bloco(fck)
    scd = p["ac"] * p["eta"] * fck / 10 / GC
    mu = Md / (scd * b * d ** 2)
    # equilíbrio: Md = scd·b·λx·(d − λx/2)  →  λx = d(1 − √(1 − 2μ))
    lx = d * (1 - math.sqrt(1 - 2 * mu))
    x = lx / p["lam"]
    As = Md / (fyk / 10 / GS * (d - lx / 2))
    return x, As


# ------------------------------------------------------------------ 17.4 cisalhamento
def cis_I(bw, d, Vd, fck, fywk=500):
    av2 = 1 - fck / 250
    fcd = fck / 10 / GC
    fctd = 0.7 * fctm(fck) / 10 / GC
    VRd2 = 0.27 * av2 * fcd * bw * d                  # 17.4.2.2
    Vc = 0.6 * fctd * bw * d                          # 17.4.2.2 b (flexão simples)
    fywd = min(fywk / GS, 435) / 10                   # 17.4.2.2: fywd ≤ 435 MPa
    Asw_s = max(Vd - Vc, 0) / (0.9 * d * fywd) * 100  # cm²/m
    Asw_s_min = 0.2 * fctm(fck) / fywk * bw * 100     # 17.4.1.1.1 ρsw = 0,2 fctm/fywk (fywk real)
    return VRd2, Vc, Asw_s, Asw_s_min


def cis_II(bw, d, Vd, fck, theta, fywk=500):
    av2 = 1 - fck / 250
    fcd = fck / 10 / GC
    t = math.radians(theta)
    VRd2 = 0.54 * av2 * fcd * bw * d * math.sin(t) ** 2 * (1 / math.tan(t))   # 17.4.2.3, α = 90°
    Vc0 = 0.6 * 0.7 * fctm(fck) / 10 / GC * bw * d
    Vc1 = Vc0 if Vd <= Vc0 else max(0.0, Vc0 * (VRd2 - Vd) / (VRd2 - Vc0))  # interpolação linear
    fywd = min(fywk / GS, 435) / 10
    Asw_s = max(Vd - Vc1, 0) / (0.9 * d * fywd / math.tan(t)) * 100
    return VRd2, Vc1, Asw_s


# ------------------------------------------------------------------ 17.3.3.2 fissuração
def wk(phi, ss, rho_r, fck, eta1):
    """phi mm, σs MPa → mm."""
    a = phi / (12.5 * eta1) * ss / 210000
    return min(a * 3 * ss / fctm(fck), a * (4 / rho_r + 45))


def acri_barras(b, h, d, n, phi, c_lat):
    """Acri (cm²) de cada barra de uma camada inferior, 17.3.3.2: retângulo de lados ≤ 7,5φ em torno do
    eixo da barra, limitado pelas faces da seção e pela metade da distância às barras vizinhas.
    b, h, d, phi, c_lat (cobrimento lateral ao eixo da barra) em cm; n barras igualmente espaçadas.
    Altura: min(7,5φ, h − d) abaixo + 7,5φ acima (LN longe nos casos de teste)."""
    xs = [c_lat + (b - 2 * c_lat) * i / (n - 1) for i in range(n)]
    alt = min(7.5 * phi, h - d) + 7.5 * phi
    out = []
    for i, x in enumerate(xs):
        esq = min(7.5 * phi, x if i == 0 else (x - xs[i - 1]) / 2)
        dir_ = min(7.5 * phi, b - x if i == n - 1 else (xs[i + 1] - x) / 2)
        out.append((esq + dir_) * alt)
    return out


# ------------------------------------------------------------------ 17.3.2.1.2 flecha diferida
def xi(t):
    return 2.0 if t > 70 else 0.68 * 0.996 ** t * t ** 0.32


# ------------------------------------------------------------------ pilares
def pilar_dir(Nd, M1dA, h, le, b, fck, alfa_b=1.0):
    """11.3.3.4.3, 15.8.2, 15.8.3.3.2. Nd kN, M kN·m, m."""
    M1min = Nd * (0.015 + 0.03 * h)
    M1 = max(abs(M1dA), M1min)
    e1 = abs(M1dA) / Nd
    lam1 = min(90.0, max(35.0, (25 + 12.5 * e1 / h) / alfa_b))
    lam = le * math.sqrt(12) / h
    if lam <= lam1:
        return dict(M1min=M1min, lam=lam, lam1=lam1, Mtot=M1)
    nu = Nd / (b * h * 1e4 * fck / 10 / GC)
    r = min(0.005 / (h * (nu + 0.5)), 0.005 / h)
    M2 = Nd * le ** 2 / 10 * r
    Mtot = max(alfa_b * M1 + M2, M1)                 # 15.8.3.3.2: Md,tot = αb·M1d,A + Nd·le²/10·1/r ≥ M1d,A
    return dict(M1min=M1min, lam=lam, lam1=lam1, nu=nu, r=r, M2=M2, Mtot=Mtot)


# ------------------------------------------------------------------ 17.5 torção (Bastos ex. 1)
def torcao(b, h, Td, fck, he, theta, fywk=500):
    av2 = 1 - fck / 250
    fcd = fck / 10 / GC
    Ae = (b - he) * (h - he)
    ue = 2 * ((b - he) + (h - he))
    t = math.radians(theta)
    fywd = min(fywk / GS, 435) / 10
    TRd2 = 0.5 * av2 * fcd * Ae * he * math.sin(2 * t)        # 17.5.1.5
    A90 = Td / (2 * Ae * fywd / math.tan(t)) * 100              # 17.5.1.6 TRd3
    Asl = Td / (2 * Ae * fywd * math.tan(t)) * 100              # 17.5.1.6 TRd4
    return TRd2, A90, Asl, ue


# ------------------------------------------------------------------ 15.5.3 γz
def gama_z(pav):
    M1 = sum(H * z for z, H, P, d in pav)
    dM = sum(P * d for z, H, P, d in pav)
    return M1, dM, 1 / (1 - dM / M1)


if __name__ == "__main__":
    print("8.2 materiais (MPa):")
    for f in range(20, 95, 5):
        print(f"  C{f}: fctm={fctm(f):.4f}  Eci={eci(f):.1f}  Ecs={ecs(f):.1f}  bloco={bloco(f)}")
    print("Flexão Bastos ex.1 (20×47, C20, Md=14000):", flexao(20, 47, 14000, 20))
    print("Flexão C70 (20×47, Md=14000):", flexao(20, 47, 14000, 70))
    print("Cisalhamento Bastos (14×46, C25, 153 kN):", cis_I(14, 46, 153, 25))
    print("Cisalhamento CA-60 (14×46, C25, 153 kN):", cis_I(14, 46, 153, 25, fywk=600))
    print("Modelo II θ=30 (20×45, C20, 150 kN):", cis_II(20, 45, 150, 20, 30))
    print("ξ(1), ξ(0,5), ξ(70), ξ(71):", xi(1), xi(0.5), xi(70), xi(71))
    print("Pilar Bastos 13.1.1 dir 20 cm:", pilar_dir(1400, 0, 0.20, 2.8, 0.50, 30))
    print("Pilar Bastos 13.1.1 dir 50 cm:", pilar_dir(1400, 0, 0.50, 2.8, 0.20, 30))
    print("Pilar 20×50 Nd=1000 M1dA=40 le=3 C25:", pilar_dir(1000, 40, 0.20, 3.0, 0.50, 25))
    print("Torção Bastos ex.1:", torcao(35, 50, 6808, 25, 10, 38))
    print("γz 4 pav.:", gama_z([(3, 12, 900, .003), (6, 12, 900, .007), (9, 12, 900, .011), (12, 6, 600, .014)]))
