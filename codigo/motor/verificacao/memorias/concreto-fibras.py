"""Integração independente de seção retangular de concreto armado (ELU), por fatias.
NBR 6118 8.2.10.1 (parábola-retângulo, σ = 0,85·ηc·fcd·[1 − (1 − ε/εc2)^n]), 8.3.6 (aço
elastoplástico, Es = 210 GPa), 17.2.2 (domínios por pivô: A εs = 10‰ na barra mais
tracionada; B εcu na borda; C εc2 a (1 − εc2/εcu)·h). Só C ≤ 50 (εc2 = 2‰, εcu = 3,5‰, n = 2).
Unidades kN, cm. Compressão positiva. Não importa prossen_estrutural. Uso:
    from concreto_fibras import As_simetrica  (ou rodar como script)."""
import math

ES = 21000.0


def esforcos(x, b, h, bars, fcd, fyd, ec2=2e-3, ecu=3.5e-3, n=2, nf=4000):
    """bars: [(y_do_topo, area)]. Retorna (N, M em relação a h/2) para LN a x do topo."""
    dmax = max(y for y, _ in bars)
    xab = ecu / (ecu + 10e-3) * dmax
    if x <= xab:
        k = 10e-3 / (dmax - x)          # pivô A
    elif x <= h:
        k = ecu / x                      # pivô B
    else:
        k = ec2 / (x - (1 - ec2 / ecu) * h)  # pivô C
    fc = 0.85 * fcd
    N = M = 0.0
    dy = h / nf
    for i in range(nf):
        y = (i + 0.5) * dy
        e = k * (x - y)
        if e > 0:
            s = fc if e >= ec2 else fc * (1 - (1 - e / ec2) ** n)
            N += s * b * dy
            M += s * b * dy * (h / 2 - y)
    for y, A in bars:
        e = k * (x - y)
        s = max(-fyd, min(fyd, ES * e))
        N += s * A
        M += s * A * (h / 2 - y)
    return N, M


def MRd(Nd, b, h, bars, fcd, fyd):
    lo, hi = 1e-6, 50 * h
    if esforcos(hi, b, h, bars, fcd, fyd)[0] < Nd:
        return None
    for _ in range(100):
        mid = (lo + hi) / 2
        if esforcos(mid, b, h, bars, fcd, fyd)[0] < Nd:
            lo = mid
        else:
            hi = mid
    return esforcos(hi, b, h, bars, fcd, fyd)[1]


def As_simetrica(Nd, Md, b, h, dl, fck, fyk=500):
    """As total (2 faces, metade em cada) para (Nd kN compressão +, Md kN·cm)."""
    fcd, fyd = fck / 14, fyk / 11.5
    lo, hi = 0.0, 0.08 * b * h * 3
    for _ in range(80):
        mid = (lo + hi) / 2
        bars = [(dl, mid / 2), (h - dl, mid / 2)]
        M = MRd(Nd, b, h, bars, fcd, fyd)
        if M is None or M < Md:
            lo = mid
        else:
            hi = mid
    return hi


if __name__ == "__main__":
    print("20x50, d'=4,5, C25, Nd=600, Md=12500:", As_simetrica(600, 12500, 20, 50, 4.5, 25))
    print("20x50, d'=4,5, C25, Nd=0, Md=12500 (só p/ comparar com flexão simples):", As_simetrica(0, 12500, 20, 50, 4.5, 25))
