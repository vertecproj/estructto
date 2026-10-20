"""Memória de cálculo independente — trechos rígidos e deformação por cortante (2026-10-20).

Só numpy; NÃO importa prossen_estrutural. Rodar: python trechos-cortante-casos.py (imprime os esperados).
Unidades: kN, m, rad. E = 200 GPa, G = 80 GPa.

Método (independente do método da rigidez): estática + Princípio dos Trabalhos Virtuais (PTV, carga
unitária; Timoshenko & Gere, Mecânica dos Sólidos, cap. de energia; Przemieniecki cap. 5 para o termo de
cortante V·v/(G·As)) integrado SÓ no trecho flexível [ti, L - tj] (trecho rígido: energia de deformação nula).
Hiperestáticas: método das forças (incógnitas = reações redundantes, compatibilidade = deslocamento nulo).
Integração por Gauss-Legendre de 6 pontos entre descontinuidades (integrandos polinomiais de grau ≤ 4: exato).

Convenção dos eixos locais = contrato do docstring do solver (interface, não fórmula): x de ni para nj;
barra não vertical y = Z × x, z = x × y; vertical y = Y; depois (y, z) girados de `rot` em torno de x.
Esforços na seção (face positiva) = resultante das forças do lado de nj (ação da parte j sobre a parte i).
Flexão em torno de y usa Iy e cortante Vz usa Az; em torno de z usa Iz e Vy usa Ay.
"""
import json
import math

import numpy as np

E, G = 2.0e8, 8.0e7
SEC = dict(A=0.01, Iy=8e-5, Iz=2e-5, J=1e-6, Ay=2.5e-3, Az=4e-3)
GX, GW = np.polynomial.legendre.leggauss(6)


def eixos(pi, pj, rot):
    d = np.asarray(pj, float) - np.asarray(pi, float)
    L = np.linalg.norm(d)
    ex = d / L
    if math.hypot(ex[0], ex[1]) < 1e-9:
        ey = np.array([0.0, 1.0, 0.0])
    else:
        ey = np.cross([0, 0, 1.0], ex)
        ey /= np.linalg.norm(ey)
    ez = np.cross(ex, ey)
    c, s = math.cos(rot), math.sin(rot)
    return np.vstack([ex, c * ey + s * ez, -s * ey + c * ez]), L


class Cadeia:
    """Barras em cadeia (barra k+1 começa no nj da barra k). Sistema de forças = lista de
    ('p', barra, s, F, C) pontuais (C = binário) e ('q', barra, s0, s1, q) uniformes (globais).
    Resultante na seção (barra b, s) = forças com (barra > b) ou (barra == b e s_k > s)."""

    def __init__(self, barras):
        self.b = []
        for d in barras:
            R, L = eixos(d["pi"], d["pj"], d.get("rot", 0.0))
            self.b.append(dict(d, R=R, L=L, pi=np.asarray(d["pi"], float)))

    def pos(self, k, s):
        return self.b[k]["pi"] + s * self.b[k]["R"][0]

    def resultante(self, sist, k, s):
        p = self.pos(k, s)
        F, M = np.zeros(3), np.zeros(3)
        for f in sist:
            if f[0] == "p":
                _, kb, sk, Fk, Ck = f
                if kb > k or (kb == k and sk > s + 1e-12):
                    F += Fk
                    M += np.cross(self.pos(kb, sk) - p, Fk) + Ck
            else:
                _, kb, s0, s1, q = f
                if kb < k or (kb == k and s1 <= s):
                    continue
                a = max(s0, s) if kb == k else s0
                Fr = np.asarray(q, float) * (s1 - a)
                F += Fr
                M += np.cross(self.pos(kb, (a + s1) / 2) - p, Fr)
        return F, M

    def locais(self, sist, k, s):
        R = self.b[k]["R"]
        F, M = self.resultante(sist, k, s)
        return np.concatenate([R @ F, R @ M])  # N, Vy, Vz, T, My, Mz

    def ptv(self, s1, s2):
        """∫ (N n/EA + Vy vy/GAy + Vz vz/GAz + T t/GJ + My my/EIy + Mz mz/EIz) no trecho flexível."""
        tot = 0.0
        for k, b in enumerate(self.b):
            c = [b["A"] * E, G * b["Ay"] if b["cortante"] else math.inf,
                 G * b["Az"] if b["cortante"] else math.inf, G * b["J"], E * b["Iy"], E * b["Iz"]]
            x0, x1 = b["ti"], b["L"] - b["tj"]
            cortes = {x0, x1}
            for f in list(s1) + list(s2):
                if f[1] == k:
                    cortes.update(v for v in f[2:4] if isinstance(v, float) and x0 < v < x1)
            cortes = sorted(cortes)
            for a, z in zip(cortes, cortes[1:]):
                for g, w in zip(GX, GW):
                    s = (a + z) / 2 + (z - a) / 2 * g
                    f1, f2 = self.locais(s1, k, s), self.locais(s2, k, s)
                    tot += w * (z - a) / 2 * sum(u * v / ci for u, v, ci in zip(f1, f2, c))
        return tot


def unit(k, s, i):
    """Força (i < 3) ou binário (i ≥ 3) unitário global na barra k, posição s."""
    F, C = np.zeros(3), np.zeros(3)
    (F if i < 3 else C)[i % 3] = 1.0
    return [("p", k, s, F, C)]


def P(k, s, F=(0, 0, 0), C=(0, 0, 0)):
    return ("p", k, float(s), np.asarray(F, float), np.asarray(C, float))


def Q(k, q, s0, s1):
    return ("q", k, float(s0), float(s1), np.asarray(q, float))


def barra(pi, pj, ti=0.0, tj=0.0, rot=0.0, cortante=False):
    return dict(SEC, pi=pi, pj=pj, ti=ti, tj=tj, rot=rot, cortante=cortante)


# ------------------------------------------------------------ casos

def caso1_biapoiada_assimetrica():
    """Biapoiada (rótula em i, apoio móvel em j, nós a 6 m), ti = 0,4, tj = 0,8, Euler. q = 10 kN/m em
    todo o vão (inclusive trechos rígidos), P1 = 7 em 0,2 (rígido i), P2 = 12 em 2,5 (flexível), P3 = 5
    em 5,6 (rígido j). Isostática: reações pela estática do vão L; flecha e giros por PTV."""
    L, ti, tj, q = 6.0, 0.4, 0.8, 10.0
    cargas = [(0.2, 7.0), (2.5, 12.0), (5.6, 5.0)]
    Rj = (q * L * L / 2 + sum(Pk * a for a, Pk in cargas)) / L
    Ri = q * L + sum(Pk for _, Pk in cargas) - Rj
    c = Cadeia([barra((0, 0, 0), (L, 0, 0), ti, tj)])
    real = [Q(0, (0, 0, -q), 0, L)] + [P(0, a, (0, 0, -Pk)) for a, Pk in cargas] + [P(0, L, (0, 0, Rj))]

    def unit_z(s):  # força unitária +Z em s na biapoiada, com a reação de j
        return [P(0, s, (0, 0, 1)), P(0, L, (0, 0, -s / L))]
    uz25 = c.ptv(real, unit_z(2.5))
    wi, wj = c.ptv(real, unit_z(ti)), c.ptv(real, unit_z(L - tj))
    e_fi, e_25, e_fj = c.locais(real, 0, ti), c.locais(real, 0, 2.5 - 1e-9), c.locais(real, 0, L - tj)
    # giro ry global = -dw/dx; trecho rígido i: dw/dx = w(face)/ti; j: dw/dx = -w(face)/tj
    return dict(Ri=Ri, Rj=Rj, Vz_face_i=e_fi[2], My_face_i=e_fi[4], My_25=e_25[4],
                Vz_face_j=e_fj[2], My_face_j=e_fj[4], uz_25=uz25, ry_i=-wi / ti, ry_j=wj / tj)


def caso2_biengastada_assimetrica():
    """Biengastada (nós a 6 m), ti = 0,5, tj = 0,25, Euler. q = 10 em todo o vão, P1 = 8 em 0,3 (rígido i),
    P2 = 15 em 3,4 (flexível). Fechada: o trecho flexível Lf é viga biengastada nas faces (os trechos rígidos
    estão presos a nós fixos): M_face = qLf²/12 + P·a'b'²/Lf² (i), qLf²/12 + P·a'²b'/Lf² (j); flecha em a' =
    q·a'²b'²/(24EI) + P·a'³b'³/(3EI·Lf³) (Timoshenko & Gere, tab. de vigas biengastadas).
    Conferido pelo método das forças (PTV) na cadeia."""
    L, ti, tj, q = 6.0, 0.5, 0.25, 10.0
    Lf = L - ti - tj
    ap, Pf = 3.4 - ti, 15.0
    bp = Lf - ap
    EI = E * SEC["Iy"]
    Mi = q * Lf**2 / 12 + Pf * ap * bp**2 / Lf**2
    Mj = q * Lf**2 / 12 + Pf * ap**2 * bp / Lf**2
    Vi = q * Lf / 2 + Pf * bp / Lf + (Mi - Mj) / Lf  # reação da viga flexível na face i (para cima)
    Vj = q * Lf + Pf - Vi
    Rzi = Vi + q * ti + 8.0
    Rzj = Vj + q * tj
    # momento de reação global (em torno de +Y): M_flexão(sagging) logo à direita do nó i = binário de
    # reação em i, e logo à esquerda do nó j = -binário em j. Hogging M_nó = Mi + Vi·ti + q·ti²/2 + 8·0,3
    # -> ry_i = -M_nó,i ; ry_j = +M_nó,j
    Mni = Mi + Vi * ti + q * ti**2 / 2 + 8.0 * 0.3
    Mnj = Mj + Vj * tj + q * tj**2 / 2
    w = q * ap**2 * bp**2 / (24 * EI) + Pf * ap**3 * bp**3 / (3 * EI * Lf**3)
    # método das forças (independente da fechada): primária = balanço engastado em i
    c = Cadeia([barra((0, 0, 0), (L, 0, 0), ti, tj)])
    carg = [Q(0, (0, 0, -q), 0, L), P(0, 0.3, (0, 0, -8.0)), P(0, 3.4, (0, 0, -Pf))]
    red = [unit(0, L, 2), unit(0, L, 4)]
    Fm = np.array([[c.ptv(a, b) for b in red] for a in red])
    X = np.linalg.solve(Fm, [-c.ptv(a, carg) for a in red])
    assert abs(X[0] - Rzj) < 1e-8 * Rzj and abs(X[1] - Mnj) < 1e-8 * Mnj, (X, Rzj, Mnj)
    real = carg + [P(0, L, (0, 0, X[0]), (0, X[1], 0))]
    w2 = c.ptv(real, unit(0, 3.4, 2))
    assert abs(w2 + w) < 1e-8 * w
    return dict(Rzi=Rzi, Myi=-Mni, Rzj=Rzj, Myj=Mnj, My_face_i=Mi, My_face_j=Mj, uz_34=-w)


def caso3_balanco_timoshenko():
    """Balanço ao longo de X (L = 3, rot = 0,3 rad, Iy ≠ Iz, Ay ≠ Az), ti = 0,3 no engaste, tj = 0,4 na
    ponta, cortante ligado. Cargas: q = (0; 1,5; -2) em todo o vão, P = (1; -2; -3) em 2,8 (rígido j), nodal
    na ponta (0; 4; -6) e binário (0,5; 0,2; -0,3). Isostática: 6 deslocamentos da ponta por PTV."""
    L = 3.0
    c = Cadeia([barra((0, 0, 0), (L, 0, 0), 0.3, 0.4, 0.3, True)])
    real = [Q(0, (0, 1.5, -2), 0, L), P(0, 2.8, (1, -2, -3)), P(0, L, (0, 4, -6), (0.5, 0.2, -0.3))]
    u = [c.ptv(real, unit(0, L, i)) for i in range(6)]
    # sem cortante, para medir a parcela de cortante
    c0 = Cadeia([barra((0, 0, 0), (L, 0, 0), 0.3, 0.4, 0.3, False)])
    u0 = [c0.ptv(real, unit(0, L, i)) for i in range(6)]
    e0 = c.locais(real, 0, 0.3)
    return dict(u_ponta=u, u_ponta_euler=u0, esf_face_i=list(e0))


def caso4_portico_3d():
    """Pilar engastado a(0,0,0)-t(0,0,3) (rot 0,5, ti = 0,1, tj = 0,25) + viga em balanço t-c(3,2,3,6)
    (rot -0,4, ti = 0,2, tj = 0,15), nó t rígido, cortante nas duas. Cargas: q pilar (1; 0,5; 0), q viga
    (0,5; -1; -4), P viga 0,1 (rígido) (2; 1; -5), P viga 1,7 (0; 3; -6), nodal t (4; 0; -10), nodal c
    (1; -2; -3 | 0,5; 0,2; -0,3). Isostática: deslocamentos de t e c por PTV, reações e esforços por estática."""
    c = Cadeia([barra((0, 0, 0), (0, 0, 3), 0.1, 0.25, 0.5, True),
                barra((0, 0, 3), (3, 2, 3.6), 0.2, 0.15, -0.4, True)])
    Lv = c.b[1]["L"]
    real = [Q(0, (1, 0.5, 0), 0, 3.0), Q(1, (0.5, -1, -4), 0, Lv), P(1, 0.1, (2, 1, -5)), P(1, 1.7, (0, 3, -6)),
            P(0, 3.0, (4, 0, -10)), P(1, Lv, (1, -2, -3), (0.5, 0.2, -0.3))]
    uc = [c.ptv(real, unit(1, Lv, i)) for i in range(6)]
    ut = [c.ptv(real, unit(0, 3.0, i)) for i in range(6)]
    F, M = c.resultante(real, 0, -1.0)  # tudo acima da base (momento em s = -1)
    M = M + np.cross(c.pos(0, -1.0) - c.pos(0, 0.0), F)  # transporta o momento para o nó a
    # flecha local no meio do trecho flexível da viga: PTV com unidade nos eixos locais
    sm = (0.2 + Lv - 0.15) / 2
    R1 = c.b[1]["R"]
    fl = [c.ptv(real, [P(1, sm, R1[i])]) for i in range(3)]
    return dict(u_c=uc, u_t=ut, reacao_a=list(-F) + list(-M), esf_viga_face_i=list(c.locais(real, 1, 0.2)),
                esf_pilar_face_j=list(c.locais(real, 0, 3.0 - 0.25)), x_meio_viga=sm, flecha_meio_viga=fl,
                Lv=Lv)


def caso5_biengastada_timoshenko_xy():
    """Biengastada ao longo de X, plano xy (Vy, Iz, Ay), L = 2,5, sem trechos, cortante. q = (0; -8; 0),
    P = (0; -10; 0) em a = 0,9. Método das forças com PTV incluindo V·v/(G·Ay). Conferência fechada da
    parcela uniforme: M_ext = qL²/12 e flecha no meio = qL⁴/(384EIz) + qL²/(8G·Ay)."""
    L, q, Pv, a = 2.5, 8.0, 10.0, 0.9
    c = Cadeia([barra((0, 0, 0), (L, 0, 0), 0, 0, 0, True)])
    red = [unit(0, L, 1), unit(0, L, 5)]
    Fm = np.array([[c.ptv(x, y) for y in red] for x in red])

    def resolve(carg):
        X = np.linalg.solve(Fm, [-c.ptv(r, carg) for r in red])
        return carg + [P(0, L, (0, X[0], 0), (0, 0, X[1]))], X
    so_q, Xq = resolve([Q(0, (0, -q, 0), 0, L)])
    wq = c.ptv(so_q, unit(0, L / 2, 1))
    wq_fech = -(q * L**4 / (384 * E * SEC["Iz"]) + q * L * L / (8 * G * SEC["Ay"]))
    assert abs(wq / wq_fech - 1) < 1e-10 and abs(Xq[1] - (-q * L * L / 12)) < 1e-9, (wq, wq_fech, Xq)
    real, X = resolve([Q(0, (0, -q, 0), 0, L), P(0, a, (0, -Pv, 0))])
    F0, M0 = c.resultante(real, 0, -1.0)
    M0 = M0 + np.cross(c.pos(0, -1.0) - c.pos(0, 0.0), F0)  # momento no nó i
    return dict(Ry_j=X[0], Mz_j=X[1], Ry_i=-F0[1], Mz_i=-M0[2], uy_a=c.ptv(real, unit(0, a, 1)),
                uy_meio=c.ptv(real, unit(0, L / 2, 1)), Mz_a=c.locais(real, 0, a - 1e-9)[5])


def caso6_rotula_face_trechos_cortante():
    """Nó i engastado, ti = 0,3 com rótula na face i (rotula_i), nó j apoio fixo (uy, uz), tj = 0,35,
    L = 5, cortante. q = (0; 0; -6) em todo o vão, P1 = 4 em 0,15 (rígido i), P2 = 9 em 2,0.
    Isostática: vão simples entre a rótula (x = ti) e o nó j; trecho [0, ti] leva sua carga ao nó i."""
    L, ti, tj, q = 5.0, 0.3, 0.35, 6.0
    span = L - ti
    # reação do nó j (momentos em torno da rótula, cargas em [ti, L])
    Rj = (q * span * span / 2 + 9.0 * (2.0 - ti)) / span
    Rh = q * span + 9.0 - Rj           # força na rótula
    Rzi = Rh + q * ti + 4.0
    Mni = Rh * ti + q * ti**2 / 2 + 4.0 * 0.15  # momento (hogging) no engaste do nó i
    c = Cadeia([barra((0, 0, 0), (L, 0, 0), ti, tj, 0, True)])
    real = [Q(0, (0, 0, -q), 0, L), P(0, 0.15, (0, 0, -4.0)), P(0, 2.0, (0, 0, -9.0)), P(0, L, (0, 0, Rj))]

    def uz(s):  # unidade +Z em s no vão simples [ti, L]
        return [P(0, s, (0, 0, 1)), P(0, L, (0, 0, -(s - ti) / span))]
    w2 = c.ptv(real, uz(2.0))
    wfj = c.ptv(real, uz(L - tj))
    c0 = Cadeia([barra((0, 0, 0), (L, 0, 0), ti, tj, 0, False)])
    return dict(Rzi=Rzi, Myi=-Mni, Rzj=Rj, My_face_i=c.locais(real, 0, ti)[4], My_2=c.locais(real, 0, 2.0 - 1e-9)[4],
                uz_2=w2, uz_2_euler=c0.ptv(real, uz(2.0)), ry_j=wfj / tj)


def caso7_pdelta_trecho():
    """Pilar em balanço vertical de 4 m (seção com Iy = Iz = 8e-5 para não flambar no outro plano), P = 900 kN (compressão) e H = 10 kN em X no topo, flexão no plano
    xz local (Iy = 8e-5). (a) tj = 0,4 no topo; (b) ti = 0,4 na base. Solução exata de 2ª ordem (equação
    diferencial EI·w'' = H(Lf + t - x) + P(Δ - w), Timoshenko & Gere, Theory of Elastic Stability §1.11):
    (a) Δ = w(Lf) + w'(Lf)·tj (trecho rígido gira junto com a face); (b) = balanço de Lf (base rígida parada).
    Devolve Δ topo e M base (= H·L + P·Δ, equilíbrio na deformada)."""
    L, t, Pv, H = 4.0, 0.4, 900.0, 10.0
    EI = E * SEC["Iy"]
    k = math.sqrt(Pv / EI)
    Lf = L - t

    def w_dw(x, D, tt):  # w = A cos kx + B sin kx + (H(Lf+tt-x) + PΔ)/P, w(0) = w'(0) = 0
        B = H / (Pv * k)
        A = -(H * (Lf + tt) + Pv * D) / Pv
        w = A * math.cos(k * x) + B * math.sin(k * x) + (H * (Lf + tt - x) + Pv * D) / Pv
        dw = -A * k * math.sin(k * x) + B * k * math.cos(k * x) - H / Pv
        return w, dw
    # (a) Δ linear em Δ: Δ = w(Lf; Δ) + w'(Lf; Δ)·t
    f = lambda D: sum(np.array(w_dw(Lf, D, t)) * [1, t]) - D
    Da = -f(0.0) / (f(1.0) - f(0.0))
    # (b) balanço de Lf: Δ = H/(P·k)·(tan kLf - kLf)
    Db = H / (Pv * k) * (math.tan(k * Lf) - k * Lf)
    Da1 = H * Lf**3 / (3 * EI) + H * t * Lf**2 / (2 * EI) + t * (H * Lf**2 / (2 * EI) + H * t * Lf / EI)
    return dict(Delta_a=Da, M_base_a=H * L + Pv * Da, Delta_b=Db, M_base_b=H * L + Pv * Db,
                Delta_a_1ordem=Da1, amplif_a=Da / Da1, P_sobre_Pcr_Lf=Pv / (math.pi**2 * EI / (4 * Lf**2)))


def caso8_pdelta_cortante(Az=4e-3):
    """Pilar em balanço vertical L = 2 m, Iy = Iz = 8e-5, Az = 4e-3 (Φ = 12EI/(G·Az·L²) = 0,15), P = 0,3·Pcr
    (Engesser) e H = 10 kN no topo, cortante ligado. Referência: teoria de Engesser (Timoshenko & Gere,
    Theory of Elastic Stability §2.8): γ = Q/(G·As) com Q = -dM/dx ⇒ w''·(1 - P/(G·As)) = M/EI,
    M = H(L - x) + P(Δ - w), w(0) = 0, w'(0) = H/(G·As - P); Pcr = Pe/(1 + Pe/(G·As)), Pe = π²EI/(4L²)."""
    L, H = 2.0, 10.0
    EI, GA = E * SEC["Iy"], G * Az
    Pe = math.pi**2 * EI / (4 * L * L)
    Pcr = Pe / (1 + Pe / GA)
    Pv = 0.3 * Pcr
    k = math.sqrt(Pv / (EI * (1 - Pv / GA)))
    B = (H / (GA - Pv) + H / Pv) / k

    def wL(D):
        A = -(H * L + Pv * D) / Pv
        return A * math.cos(k * L) + B * math.sin(k * L) + Pv * D / Pv
    D = wL(0.0) / (1 - (wL(1.0) - wL(0.0)))
    D1 = H * L**3 / (3 * EI) + H * L / GA
    return dict(P=Pv, Pcr=Pcr, Delta=D, Delta_1ordem=D1, M_base=H * L + Pv * D)


if __name__ == "__main__":
    out = {}
    for nome, fn in list(globals().items()):
        if nome.startswith("caso"):
            out[nome] = fn()
    print(json.dumps(out, indent=1, default=float))
