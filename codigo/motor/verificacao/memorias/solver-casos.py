"""Memória de cálculo independente — solver (ficha 012). Só stdlib + numpy; NÃO importa prossen_estrutural.

Cada função devolve os valores esperados de um caso, com a fonte. Rodar: python solver-casos.py
Unidades: kN, m, rad.
"""
import math

import numpy as np

E = 2.0e8      # kN/m² (aço, 200 GPa)
G = 8.0e7      # kN/m²


def caso_pontual_biapoiada(P=20.0, a=2.0, L=6.0, I=8.581e-5):
    """Viga biapoiada, carga concentrada P (para baixo) em x = a.
    Fonte: Timoshenko & Gere, Mecânica dos Sólidos, tab. de vigas (caso P em a):
    R_i = P·b/L, R_j = P·a/L, M(a) = P·a·b/L, flecha em a = P·a²·b²/(3·E·I·L)."""
    b = L - a
    return dict(Ri=P * b / L, Rj=P * a / L, Ma=P * a * b / L,
                fa=P * a**2 * b**2 / (3 * E * I * L))


def caso_pontual_inclinada(F=10.0, a=2.0):
    """Barra biengastada de (0,0,0) a (4,0,3) (L = 5, ex = (0,8; 0; 0,6)), força global (0,0,−F) em a.
    Projeção: axial Fx = F·ez·ex = −0,6F; transversal Ft = 0,8F no sentido (0,6; 0; −0,8).
    Engastamento (McGuire, Gallagher & Ziemian, Matrix Structural Analysis, 2ª ed., apêndice de
    forças de engastamento): axial R_i = Fax·b/L, R_j = Fax·a/L; transversal R_i = Ft·b²(L+2a)/L³,
    R_j = Ft·a²(L+2b)/L³; M_i = Ft·a·b²/L², M_j = Ft·a²·b/L².
    Reações globais = reações de engastamento (nós totalmente fixos). N interno: −3,6 (x < a), +2,4 (x > a)."""
    L = 5.0
    b = L - a
    ex = np.array([0.8, 0.0, 0.6])
    Fv = np.array([0.0, 0.0, -F])
    fax = Fv @ ex                         # −6
    ft_vec = Fv - fax * ex                # (4,8; 0; −6,4)
    Ft = np.linalg.norm(ft_vec)           # 8
    et = ft_vec / Ft
    Rax_i, Rax_j = -fax * b / L, -fax * a / L          # 3,6 e 2,4 (sentido +ex)
    Rt_i = Ft * b**2 * (L + 2 * a) / L**3              # 5,184
    Rt_j = Ft * a**2 * (L + 2 * b) / L**3              # 2,816
    Mi, Mj = Ft * a * b**2 / L**2, Ft * a**2 * b / L**2  # 5,76 e 3,84
    Ri = Rax_i * ex - Rt_i * et
    Rj = Rax_j * ex - Rt_j * et
    # momento de reação em torno de Y global: extremidade i anti-horária no plano XZ (vista de −Y)
    # => vetor −Y; j horária => +Y.  Conferência: Σ M em i = 0 (ver __main__).
    return dict(Ri=Ri, Rj=Rj, MYi=-Mi, MYj=+Mj, N_antes=-Rax_i, N_depois=Rax_j)


def caso_rotula_j(q=10.0, L=6.0):
    """Viga engastada em i, rótula em j (apoio fixo em j): M_i = qL²/8, M_j = 0, R_i = 5qL/8,
    R_j = 3qL/8. Fonte: Timoshenko & Gere, viga engastada-apoiada com carga uniforme."""
    return dict(Mi=q * L**2 / 8, Mj=0.0, Ri=5 * q * L / 8, Rj=3 * q * L / 8)


def caso_diafragma(H=100.0, no_carga=(0.0, 0.0), Iy=(2e-4, 2e-4, 5e-5, 5e-5), Iz=(1e-4,) * 4,
                   J=1e-6, h=3.0):
    """4 pilares em balanço (engastados na base, topo livre em giro) em (0,0), (6,0), (0,4), (6,4),
    topo em z = h vinculado por diafragma rígido. Pilar vertical: y local = Y, z local = −X ⇒
    deslocamento em X usa Iy, em Y usa Iz (ESPEC §3). Rigidezes: kx = 3EIy/h³, ky = 3EIz/h³,
    kt = GJ/h (Timoshenko, balanço). Centro c = centro geométrico (3, 2).
    Cinemática (ficha 001): ux_i = u − (y_i − yc)θ ; uy_i = v + (x_i − xc)θ ; rz_i = θ.
    Equilíbrio (princípio dos trabalhos virtuais): K·[u v θ] = [Hx, Hy, Mz_c]."""
    xy = [(0.0, 0.0), (6.0, 0.0), (0.0, 4.0), (6.0, 4.0)]
    xc = sum(p[0] for p in xy) / 4
    yc = sum(p[1] for p in xy) / 4
    K = np.zeros((3, 3))
    for (x, y), iy, iz in zip(xy, Iy, Iz):
        kx, ky, kt = 3 * E * iy / h**3, 3 * E * iz / h**3, G * J / h
        ax = np.array([1.0, 0.0, -(y - yc)])
        ay = np.array([0.0, 1.0, (x - xc)])
        K += kx * np.outer(ax, ax) + ky * np.outer(ay, ay)
        K[2, 2] += kt
    Mz = -(no_carga[1] - yc) * H          # força em +X no ponto (x0, y0): Mz = −(y0 − yc)·Fx
    u, v, th = np.linalg.solve(K, [H, 0.0, Mz])
    Fx = []
    for (x, y), iy in zip(xy, Iy):
        Fx.append(3 * E * iy / h**3 * (u - (y - yc) * th))
    return dict(u=u, v=v, theta=th, Fx_pilares=Fx, centro=(xc, yc))


def caso_pdelta_aisc():
    """AISC 360-16 Comentário C2, fig. C-C2.2 e C-C2.3 (W14x48, L = 28 ft, E = 29 000 ksi).
    Caso 1 (biarticulado, w = 0,200 kip/ft): P = 0/150/300/450 kip → M meio 235/269/313/375 kip·in,
    flecha 0,197/0,224/0,261/0,311 in. Caso 2 (balanço, H = 1 kip): P = 0/100/150/200 kip →
    M base 336/469/598/848 kip·in, topo 0,901/1,33/1,75/2,56 in. Conversão para SI."""
    kip, inch, ft = 4.448222, 0.0254, 0.3048
    kipin = kip * inch
    return dict(
        A=14.1 * inch**2, I=484 * inch**4, Iy_fraco=51.4 * inch**4, J=1.45 * inch**4,
        E=29000 * 6894.757, L=28 * ft, w=0.2 * kip / ft, H=1.0 * kip,
        caso1=[(P * kip, M * kipin, d * inch) for P, M, d in
               [(0, 235, 0.197), (150, 269, 0.224), (300, 313, 0.261), (450, 375, 0.311)]],
        caso2=[(P * kip, M * kipin, d * inch) for P, M, d in
               [(0, 336, 0.901), (100, 469, 1.33), (150, 598, 1.75), (200, 848, 2.56)]])


def caso_frequencia(L=6.0, I=8.581e-5, q=10.0):
    """Biapoiada: f1 = (π/2L²)·√(EI/m̄), m̄ = q/g (t/m). Fonte: Clough & Penzien, Dynamics of
    Structures, viga simplesmente apoiada (modo 1)."""
    m = q / 9.80665
    return math.pi / (2 * L**2) * math.sqrt(E * I / m)


def eixos_esperados():
    """ESPEC §3: x de ni para nj; barra não vertical y = Z × x (normalizado), z = x × y; vertical
    y = Y; giro rot em torno de x: y' = cos·y + sen·z, z' = −sen·y + cos·z (regra da mão direita)."""
    return {
        "X": ([1, 0, 0], [0, 1, 0], [0, 0, 1]),
        "Y": ([0, 1, 0], [-1, 0, 0], [0, 0, 1]),
        "Z_sobe": ([0, 0, 1], [0, 1, 0], [-1, 0, 0]),
        "Z_desce": ([0, 0, -1], [0, 1, 0], [1, 0, 0]),
        "X_rot90": ([1, 0, 0], [0, 0, 1], [0, -1, 0]),
    }


if __name__ == "__main__":
    print("pontual biapoiada:", caso_pontual_biapoiada())
    r = caso_pontual_inclinada()
    print("pontual inclinada:", r)
    # Σ M_Y em i: carga em (1,6;0;1,2) com (0,0,−10) → +16; Rj em (4,0,3) → 3·Rjx − 4·Rjz
    m = 16.0 + (3 * r["Rj"][0] - 4 * r["Rj"][2]) + r["MYi"] + r["MYj"]
    print("  ΣMy(i) =", m, " ΣF =", r["Ri"] + r["Rj"])
    print("rótula em j:", caso_rotula_j())
    print("diafragma (H em (0,0)):", caso_diafragma())
    print("diafragma (H/4 nos 4 nós) — superpor 4 casos de 25:",
          [caso_diafragma(25.0, p)["theta"] for p in [(0, 0), (6, 0), (0, 4), (6, 4)]])
    print("f1 biapoiada:", caso_frequencia())
    print("AISC:", caso_pdelta_aisc())
