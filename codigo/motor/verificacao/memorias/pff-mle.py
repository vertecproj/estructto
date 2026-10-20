"""Memória independente — NBR 14762:2010, método da largura efetiva (verificador-calculos, ficha 013).

Só stdlib; reaproveita o integrador de parede fina de aco-secoes.py (escrito por este verificador,
não importa prossen_estrutural). Unidades: mm, N, MPa; resultados convertidos na saída.

Fórmulas (NBR 14762:2010, conferidas com [UFAL-C] e [TBS] citados em ../2026-10-20-pff.md):
- 9.2.2: λp = (b/t)/(0,95√(kE/σ)); bef = b(1 − 0,22/λp)/λp ≤ b (λp > 0,673).
  Tabela 5 (AA): ψ = σ2/σ1; k = 4 (ψ = 1); k = 4 + 2(1 − ψ)³ + 2(1 − ψ) (ψ < 1);
  bef,1 = bef/(3 − ψ); bef,2 = bef/2 (ψ ≤ −0,236) ou bef − bef,1 (ψ > −0,236); bef,1 + bef,2 ≤ bc.
  Tabela 6 (AL): k = 0,43 com a maior tensão (a favor da segurança).
- 9.2.3 (enrijecedor de borda simples, θ = 90°): λp0 = (b/t)/(0,623√(E/σ));
  λp0 ≤ 0,673: mesa e enrijecedor efetivos; senão Ia = 399t⁴(0,487λp0 − 0,328)³ ≤ t⁴(56λp0 + 5);
  Is = t·d³/12; n = 0,582 − 0,122λp0 ≥ 1/3; k = 3,57(Is/Ia)ⁿ + 0,43 ≤ 4 (D/b ≤ 0,25) ou
  (4,82 − 5D/b)(Is/Ia)ⁿ + 0,43 ≤ 4 (0,25 < D/b ≤ 0,8); def do enrijecedor como AL; ds = def·Is/Ia ≤ def.
- 9.7.2: λ0 = √(A·fy/Ne); χ = 0,658^(λ0²) (λ0 ≤ 1,5) ou 0,877/λ0²; Aef com σ = χ·fy; Nc,Rd = χAef·fy/1,20.
- 9.8.2.1: Wef com σ = fy na fibra extrema comprimida (LN da seção efetiva, iterativa); MRd = Wef·fy/1,10.
- 9.8.2.2: Me = Cb·r0·√(Ney·Nez) (monossimétrica fletida no eixo de simetria); λ0 = √(Wc·fy/Me);
  χFLT = 1 (λ0 ≤ 0,6); 1,11(1 − 0,278λ0²) (0,6 < λ0 < 1,336); 1/λ0² (≥ 1,336); MRd = χFLT·Wc,ef·fy/1,10.
Geometria NBR 6355 com raio interno ri (cantos em arco de raio ri + t/2 pela linha média, sempre efetivos).
"""
import importlib.util
import math
import os

_sp = importlib.util.spec_from_file_location("sec", os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                                 "aco-secoes.py"))
sec = importlib.util.module_from_spec(_sp)
_sp.loader.exec_module(sec)


def bef_simples(b, t, k, sig, E):
    lp = (b / t) / (0.95 * math.sqrt(k * E / sig))
    return (b if lp <= 0.673 else min(b, b * (1 - 0.22 / lp) / lp)), lp


def chi(l0):
    return 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2


def chi_flt(l0):
    if l0 <= 0.6:
        return 1.0
    return 1.11 * (1 - 0.278 * l0 ** 2) if l0 < 1.336 else 1 / l0 ** 2


def mesa_enrijecida(b, d, D, t, sig, E):
    """9.2.3 → (bef da mesa, ds do enrijecedor, detalhes)."""
    lp0 = (b / t) / (0.623 * math.sqrt(E / sig))
    if lp0 <= 0.673:
        return b, d, dict(lp0=lp0, def_=d, ds=d)
    Ia = min(399 * t ** 4 * (0.487 * lp0 - 0.328) ** 3, t ** 4 * (56 * lp0 + 5))
    Is = t * d ** 3 / 12
    n = max(0.582 - 0.122 * lp0, 1 / 3)
    r = min(Is / Ia, 1.0)
    k = (3.57 if D / b <= 0.25 else 4.82 - 5 * D / b) * r ** n + 0.43
    k = min(k, 4.0)
    bef, lp = bef_simples(b, t, k, sig, E)
    def_ = bef_simples(d, t, 0.43, sig, E)[0]
    ds = min(def_ * r, def_)
    return bef, ds, dict(lp0=lp0, Ia=Ia, Is=Is, n=n, k=k, lp=lp, def_=def_, ds=ds)


class Ue:
    """Ue h×b×D×t (externas, mm) com raio interno ri; geometria pela linha média com arcos."""

    def __init__(self, h, b, D, t, ri, narc=24):
        self.h, self.b, self.D, self.t, self.ri = h, b, D, t, ri
        rm = ri + t / 2
        self.bw = h - 2 * (ri + t)       # partes planas
        self.bf = b - 2 * (ri + t)
        self.d = D - (ri + t)
        hm, bm = h - t, b - t            # linha média: alma em x = 0, mesas em y = ±hm/2
        pts = [(bm, hm / 2 - (D - t / 2))]

        def arco(cx, cy, a0, a1):
            pts.extend((cx + rm * math.cos(a0 + (a1 - a0) * k / narc), cy + rm * math.sin(a0 + (a1 - a0) * k / narc))
                       for k in range(narc + 1))
        arco(bm - rm, hm / 2 - rm, 0, math.pi / 2)
        arco(rm, hm / 2 - rm, math.pi / 2, math.pi)
        arco(rm, -hm / 2 + rm, math.pi, 1.5 * math.pi)
        arco(bm - rm, -hm / 2 + rm, 1.5 * math.pi, 2 * math.pi)
        pts.append((bm, -hm / 2 + (D - t / 2)))
        self.pts = pts
        g = sec.parede_fina(pts, [(i, i + 1) for i in range(len(pts) - 1)], t)
        self.g = g
        self.A, self.Ix, self.Iy = g["A"], g["Ix"], g["Iy"]
        self.x0, self.J, self.Cw = abs(g["xs"]), g["J"], g["Cw"]
        self.rx, self.ry = math.sqrt(self.Ix / self.A), math.sqrt(self.Iy / self.A)
        self.r0 = math.sqrt(self.rx ** 2 + self.ry ** 2 + self.x0 ** 2)
        self.ymax = h / 2
        self.Wx = self.Ix / self.ymax

    def Ne(self, KLx, KLy, KLz, E, G):
        Nex = math.pi ** 2 * E * self.Ix / KLx ** 2
        Ney = math.pi ** 2 * E * self.Iy / KLy ** 2
        Nez = (math.pi ** 2 * E * self.Cw / KLz ** 2 + G * self.J) / self.r0 ** 2
        a = 1 - (self.x0 / self.r0) ** 2
        Nexz = (Nex + Nez) / (2 * a) * (1 - math.sqrt(1 - 4 * Nex * Nez * a / (Nex + Nez) ** 2))
        return dict(Nex=Nex, Ney=Ney, Nez=Nez, Nexz=Nexz, Ne=min(Ney, Nexz))

    def Aef(self, sig, E):
        t = self.t
        bw = bef_simples(self.bw, t, 4.0, sig, E)
        bf, ds, det = mesa_enrijecida(self.bf, self.d, self.D, t, sig, E)
        A = self.A - (self.bw - bw[0]) * t - 2 * (self.bf - bf) * t - 2 * (self.d - ds) * t
        det.update(bef_alma=bw[0], bef_mesa=bf, ds=ds)
        return A, det

    def Wef(self, sig_max, E, it=60):
        """9.8.2.1, flexão em x com a mesa superior comprimida; σ = sig_max na fibra extrema comprimida.
        Partes: retira-se a parte inefetiva de cada elemento (alma do topo para baixo, a partir de bef,1)."""
        t, h = self.t, self.h
        yt = h / 2 - t / 2           # linha média da mesa
        y_alma_top = h / 2 - (self.ri + t)   # topo da parte plana da alma
        Ix, A, yc = self.Ix, self.A, 0.0    # brutos (centroide em y = 0)
        lost = []
        for _ in range(it):
            # tensões com a LN em yc (compressão > 0 acima)
            c = h / 2 - yc
            s = lambda y: sig_max * (y - yc) / c
            lost = []
            # mesa superior + enrijecedor (tensão da linha média da mesa)
            bf, ds, _d = mesa_enrijecida(self.bf, self.d, self.D, t, s(yt), E)
            xm = self.ri + t + self.bf / 2
            if bf < self.bf:
                lost.append(((self.bf - bf) * t, yt, 0.0))
            if ds < self.d:
                # enrijecedor desce da mesa: parte inefetiva na ponta livre
                y_tip = h / 2 - self.D
                L_ = self.d - ds
                lost.append((L_ * t, y_tip + L_ / 2, t * L_ ** 3 / 12))
            # alma: AA sob gradiente
            s1, s2 = s(y_alma_top), s(-y_alma_top)
            psi = s2 / s1
            k = 4.0 if psi >= 1 else 4 + 2 * (1 - psi) ** 3 + 2 * (1 - psi)
            bef = bef_simples(self.bw, t, k, s1, E)[0]
            if psi < 0:
                bc = self.bw * s1 / (s1 - s2)
                b1 = bef / (3 - psi)
                b2 = bef / 2 if psi <= -0.236 else bef - b1
                if b1 + b2 < bc:
                    L_ = bc - b1 - b2
                    lost.append((L_ * t, y_alma_top - b1 - L_ / 2, t * L_ ** 3 / 12))
            Al = sum(a for a, *_ in lost)
            Aef = A - Al
            yn = (A * 0.0 - sum(a * y for a, y, _ in lost)) / Aef
            if abs(yn - yc) < 1e-9:
                break
            yc = yn
        Ief = Ix + A * yc ** 2 - sum(i0 + a * (y - yc) ** 2 for a, y, i0 in lost) - A * 0 - Aef * 0
        # Ix bruto em relação a yc: Ix + A·yc²; retirar as partes em relação a yc
        Ief = Ix + A * yc ** 2 - sum(i0 + a * (y - yc) ** 2 for a, y, i0 in lost)
        return Ief / (h / 2 - yc), dict(yc=yc, Ief=Ief, perdas=len(lost))


def compressao(u, fy, E, G, KLx, KLy, KLz):
    ne = u.Ne(KLx, KLy, KLz, E, G)
    l0 = math.sqrt(u.A * fy / ne["Ne"])
    x = chi(l0)
    Aef, det = u.Aef(x * fy, E)
    return dict(chi=x, l0=l0, Aef=Aef, NcRd=x * Aef * fy / 1.20, **ne, **det)


def flexao(u, fy, E, G, Lb, Cb=1.0):
    W, det = u.Wef(fy, E)
    out = dict(Wef=W, MRd_esc=W * fy / 1.10, **det)
    if Lb > 0:
        ne = u.Ne(Lb, Lb, Lb, E, G)
        Me = Cb * u.r0 * math.sqrt(ne["Ney"] * ne["Nez"])
        l0 = math.sqrt(u.Wx * fy / Me)
        xf = chi_flt(l0)
        Wc = u.Wef(xf * fy, E)[0]
        out.update(Me=Me, l0_FLT=l0, chi_FLT=xf, MRd_FLT=xf * Wc * fy / 1.10)
    return out


if __name__ == "__main__":
    # [UFAL-C] Ue 250x85x25x2,00, ri = t, fy = 300 MPa, E = 200 GPa, G = 77 GPa, KxLx = 2400, KyLy = KzLz = 1200 mm
    u = Ue(250, 85, 25, 2.0, 2.0)
    r = compressao(u, 300, 200e3, 77e3, 2400, 1200, 1200)
    print(f"UFAL-C: A={u.A / 100:.3f} cm² χ={r['chi']:.4f} bef,mesa={r['bef_mesa'] / 10:.3f} ds={r['ds'] / 10:.3f} "
          f"def={r['def_'] / 10:.3f} bef,alma={r['bef_alma'] / 10:.3f} cm Aef={r['Aef'] / 100:.3f} cm² "
          f"NcRd={r['NcRd'] / 1e3:.2f} kN  (fonte: 0,890; 6,91; 1,54; 2,10; 9,42; 5,64; 125,5)")
    # [CBCA 4.12] Ue 300x85x25x3,00, ri = t, fy = 240, G = 77 GPa, Lx = 10290, Ly = Lz = 2572,5 mm, Cb = 1,45
    u = Ue(300, 85, 25, 3.0, 3.0)
    r = compressao(u, 240, 200e3, 77e3, 10290, 2572.5, 2572.5)
    print(f"CBCA 4.12 compressão: Nex={r['Nex'] / 1e3:.2f} Ney={r['Ney'] / 1e3:.2f} Nexz={r['Nexz'] / 1e3:.2f} "
          f"Aef={r['Aef'] / 100:.3f} NcRd={r['NcRd'] / 1e3:.2f}  (fonte: 364,02; 399,95; 276,48; 11,78; 136,59)")
    f = flexao(u, 240, 200e3, 77e3, 2572.5, 1.45)
    print(f"CBCA 4.12 flexão: Wbruto={u.Wx / 1e3:.2f} Wef={f['Wef'] / 1e3:.2f} cm³ Ief={f['Ief'] / 1e4:.2f} cm⁴ "
          f"MRd,esc={f['MRd_esc'] / 1e4:.1f} MRd,FLT={f['MRd_FLT'] / 1e4:.1f} kN·cm  (fonte: Ief 1845,89; 2551,45; 2523,42)")
    # [TBS] Ue 300x85x25x2,25, fy = 340, ri = t
    u = Ue(300, 85, 25, 2.25, 2.25)
    f = flexao(u, 340, 200e3, 77e3, 0)
    print(f"TBS: Wef={f['Wef'] / 1e3:.2f} cm³ (fonte 89,78); yc={f['yc']:.2f} mm")
