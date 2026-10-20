"""Solver de pórtico espacial pelo método da rigidez direta.

Base teórica: McGuire, Gallagher & Ziemian, "Matrix Structural Analysis" (2ª ed.),
cap. 4-5 (elemento de pórtico 3D, 12 GL, Euler-Bernoulli, forças de engastamento perfeito),
cap. 7 (liberações por condensação estática), cap. 9 (rigidez geométrica);
Przemieniecki, "Theory of Matrix Structural Analysis" (condensação, vínculos por transformação);
Clough & Penzien (dinâmica, massa concentrada, participação modal).
Unidades: kN, m (ver ESPEC.md).

Convenções:
- GL por nó: ux, uy, uz, rx, ry, rz (globais). Z global é vertical (para cima).
- Eixo local x: de ni para nj. Barra não vertical: y local = Z × x (horizontal),
  z local = x × y ("para cima"). Barra vertical: y local = Y global. Depois gira-se
  (y, z) de `rot` em torno de x. `eixos_locais` devolve essas linhas (mesma matriz da rigidez).
- Flexão em torno de y local usa Iy (deslocamento w, plano xz); em torno de z usa Iz.
- Esforços na seção (face positiva): N > 0 tração; Vy, Vz, T, My, Mz pelo sentido dos
  eixos locais. Viga horizontal com carga de gravidade dá My < 0 no vão.
- `analisar(modelo, caso)`: caso = nome (str) ou dict {caso: fator}; com dict, cargas e peso
  próprio (se `modelo.peso_proprio` estiver no dict) são somados ponderados e a combinação é
  analisada como um caso (P-Δ de combinação: sempre assim, nunca por `combinar`).
- Cargas: nodais (globais), distribuídas q (globais, kN/m; uniformes, ou lineares de q em ni a q2 em
  nj = uniforme + triangular, engastamento em _engaste_linear; com trecho rígido -> ValueError) e pontuais em barra
  F (globais, kN) a `a` m de ni. Ambas são projetadas nos eixos locais (componente axial incluída)
  e viram forças de engastamento perfeito. Com carga pontual, a saída da barra ganha dois pontos
  em x = a (a⁻ e a⁺, mostrando o salto de V); a grade depende de todas as cargas pontuais do
  modelo, não do caso, para que `combinar` some pontos iguais.
- P-Δ (pdelta=True): rigidez geométrica iterativa; os esforços ao longo da barra incluem o
  momento da força axial na posição deformada (N·Δv, N·Δw), contínuo nos nós. K + Kg não
  positiva definida (carga acima da crítica) -> ErroMecanismo citando a combinação.
  Caso inexistente em `caso` -> KeyError.
- params da barra: "trelica": True -> só rigidez axial; carga de vão em treliça é transmitida
  aos nós como em viga biapoiada (sem momento nodal) e a flecha é a reta entre os nós mais a
  da biapoiada (giros nodais não entram). "rotula_i"/"rotula_j": True -> libera
  My e Mz locais na extremidade por condensação estática (rigidez e engastamento); axial e torção
  continuam. GL nodal sem rigidez e sem carga (ex.: giro de nó só com barras rotuladas) é fixado;
  com carga -> ErroMecanismo citando nó e GL. "fator_EI": float > 0 (padrão 1,0) multiplica
  E·Iy e E·Iz (rigidez e flechas); EA, GJ e massa inalterados (NBR 6118 15.7.3, γz).
  "trecho_rigido_i"/"trecho_rigido_j": float ≥ 0 (m, padrão 0) = trecho infinitamente rígido ao longo
  do eixo a partir de ni / nj (dimensão do nó); trecho flexível Lf = L - ti - tj > 0. Ligação por corpo
  rígido (McGuire 2ª ed. §4.6 / Przemieniecki, vínculos por transformação): u_face = u_nó + θ × r,
  k_nó = Aᵀ·k_flex·A, f_nó = Aᵀ·f_flex. Rótulas ficam nas faces. Carga (distribuída, peso próprio,
  pontual) sobre o trecho rígido vai direto ao nó como força + momento (estática); a do trecho flexível
  vira engastamento de vão Lf. Esforços e flechas só no trecho flexível: x de ti a L - tj (medido de ni;
  1º e último pontos = faces), deslocamentos locais do eixo flexível. P-Δ: Kg = Aᵀ·Kg(Lf)·A (trecho
  flexível, mesma transformação de corpo rígido) + corda de cada trecho rígido, N/t entre nó e face
  (= N·t nos giros nodais de flexão; o trecho girado de θ sob N gera N·θ·t). Sem a corda o erro era da
  ordem de t/Lf com o trecho na ponta carregada (balanço, t/Lf = 0,11: -9,5 % em Δ). Hipóteses: N
  constante na barra (o da extremidade i), sem termos de torção/momento em Kg, trecho sem flexibilidade.
  Treliça ignora os trechos (só axial).
  "cortante": True -> deformação por cortante (Timoshenko; Przemieniecki §5.6): Φ = 12·E·I/(G·As·Lf²),
  com Secao.props "Ay" (m², cortante Vy, plano xy com Iz) e "Az" (m², Vz, plano xz com Iy); rigidez,
  engastamento de carga pontual (o da uniforme não muda) e flechas exatos do elemento de Timoshenko.
  P-Δ: Kg de Euler (aproximação, erro da ordem de Φ·N/Ncr).
- Pavimento com diafragma=True: nós com |z - pav.z| <= 0,001 m ficam vinculados no plano
  (ux_i = ux_c - (y_i - y_c)·rz_c; uy_i = uy_c + (x_i - x_c)·rz_c; rz_i = rz_c), c = centro
  geométrico dos nós vinculados, 3 GL mestres virtuais, por transformação exata u = T·u_red.
  AVISO: nós com apoio em ux, uy ou rz não entram no diafragma nem no cálculo do centro.
  Resultado.pavimentos[nome] = (ux, uy, rz) do centro (sem diafragma: média de todos os nós do
  pavimento). `modal` usa a mesma transformação (Tᵀ·K·T, Tᵀ·M·T).
"""
from __future__ import annotations

from dataclasses import dataclass, field

import math

import numpy as np
import scipy.linalg as sla
import scipy.sparse as sp
import scipy.sparse.linalg as spla

G_ACEL = 9.80665  # m/s²
N_PONTOS = 11     # pontos de saída ao longo da barra
TOL_PAV = 0.001   # m, tolerância de cota dos nós de um pavimento (ESPEC §5)
TOL_X = 1e-9      # m, coincidência de abscissas ao longo da barra
GLS = ("ux", "uy", "uz", "rx", "ry", "rz")


class ErroMecanismo(Exception):
    """Estrutura hipostática (mecanismo) ou matriz de rigidez singular."""


@dataclass
class Resultado:
    deslocamentos: dict = field(default_factory=dict)  # no -> array(6) global
    reacoes: dict = field(default_factory=dict)        # no -> array(6) global (só nós apoiados)
    esforcos: dict = field(default_factory=dict)       # barra -> [(x, N, Vy, Vz, T, My, Mz)]
    flechas: dict = field(default_factory=dict)        # barra -> [(x, ux, uy, uz)] locais
    pavimentos: dict = field(default_factory=dict)     # pavimento -> array(3) (ux, uy, rz) do centro


# ---------------------------------------------------------------- elemento

def _rotacao(xi, xj, rot):
    """Matriz 3x3 cujas linhas são os eixos locais x, y, z em coordenadas globais."""
    d = np.asarray(xj, float) - np.asarray(xi, float)
    L = np.linalg.norm(d)
    if L < 1e-9:
        raise ValueError("barra de comprimento nulo")
    ex = d / L
    if np.hypot(ex[0], ex[1]) < 1e-9:  # barra vertical
        ey = np.array([0.0, 1.0, 0.0])
    else:
        ey = np.cross([0.0, 0.0, 1.0], ex)
        ey /= np.linalg.norm(ey)
    ez = np.cross(ex, ey)
    c, s = np.cos(rot), np.sin(rot)
    ey, ez = c * ey + s * ez, -s * ey + c * ez
    return np.vstack([ex, ey, ez]), L


def _T12(R):
    return np.kron(np.eye(4), R)


def _flexao(L, ph):
    """Bloco de flexão [v, θ, v, θ] / (EI/L³) de Timoshenko (Przemieniecki §5.6; Φ = 0: Euler)."""
    return np.array([[12, 6 * L, -12, 6 * L], [6 * L, (4 + ph) * L * L, -6 * L, (2 - ph) * L * L],
                     [-12, -6 * L, 12, -6 * L], [6 * L, (2 - ph) * L * L, -6 * L, (4 + ph) * L * L]]) / (1 + ph)


def _forma(s, L, ph):
    """Funções de forma exatas do elemento de Timoshenko sem carga (Φ = 0: Hermite)."""
    c = ph * (s - s * s) / 2
    return ((1 - 3 * s * s + 2 * s**3 + ph * (1 - s)) / (1 + ph), L * (s - 2 * s * s + s**3 + c) / (1 + ph),
            (3 * s * s - 2 * s**3 + ph * s) / (1 + ph), L * (-s * s + s**3 - c) / (1 + ph))


def _k_local(E, G, A, Iy, Iz, J, L, trelica=False, phy=0.0, phz=0.0):
    k = np.zeros((12, 12))
    a = E * A / L
    k[np.ix_([0, 6], [0, 6])] = a * np.array([[1, -1], [-1, 1]])
    if trelica:
        return k
    t = G * J / L
    k[np.ix_([3, 9], [3, 9])] = t * np.array([[1, -1], [-1, 1]])
    # plano xy: v (1,7), θz (5,11), Iz, Φy
    k[np.ix_([1, 5, 7, 11], [1, 5, 7, 11])] = E * Iz / L**3 * _flexao(L, phy)
    # plano xz: w (2,8), θy (4,10), Iy, Φz; dw/dx = -θy inverte os termos cruzados
    s = np.diag([1, -1, 1, -1])
    k[np.ix_([2, 4, 8, 10], [2, 4, 8, 10])] = E * Iy / L**3 * (s @ _flexao(L, phz) @ s)
    return k


def _kg_local(N, L, trelica=False):
    """Rigidez geométrica (N > 0 tração), McGuire cap. 9, sem termos de torção."""
    kg = np.zeros((12, 12))
    c = N / L
    kg[np.ix_([1, 7], [1, 7])] = c * np.array([[1, -1], [-1, 1]])
    kg[np.ix_([2, 8], [2, 8])] = c * np.array([[1, -1], [-1, 1]])
    if trelica:
        return kg
    m = c * np.array([[6 / 5, L / 10, -6 / 5, L / 10],
                      [L / 10, 2 * L * L / 15, -L / 10, -L * L / 30],
                      [-6 / 5, -L / 10, 6 / 5, -L / 10],
                      [L / 10, -L * L / 30, -L / 10, 2 * L * L / 15]])
    kg[np.ix_([1, 5, 7, 11], [1, 5, 7, 11])] = m
    s = np.diag([1, -1, 1, -1])
    kg[np.ix_([2, 4, 8, 10], [2, 4, 8, 10])] = s @ m @ s
    return kg


def _engaste(q, L):
    """Reações de engastamento perfeito (sobre a barra) p/ carga uniforme local q."""
    qx, qy, qz = q
    return np.array([-qx * L / 2, -qy * L / 2, -qz * L / 2, 0, qz * L * L / 12, -qy * L * L / 12,
                     -qx * L / 2, -qy * L / 2, -qz * L / 2, 0, -qz * L * L / 12, qy * L * L / 12])


def _engaste_linear(dq, L, phy=0.0, phz=0.0):
    """Reações de engastamento perfeito (sobre a barra) p/ carga triangular local, 0 em ni e dq em nj.
    Giros da biapoiada (EI·θi = 7wL³/360, EI·θj = 8wL³/360) travados pela rigidez de _flexao:
    M_i = wL²(12 + 15Φ)/(360(1+Φ)), M_j = wL²(18 + 15Φ)/(360(1+Φ)) (Φ = 0: wL²/30 e wL²/20);
    R_i = wL/6 + (M_i - M_j)/L (Φ = 0: 3wL/20), R_j = wL/2 - R_i. Axial N_i = wL/6, N_j = wL/3."""
    dx, dy, dz = dq

    def plano(ph):
        mi, mj = L * L * (12 + 15 * ph) / (360 * (1 + ph)), L * L * (18 + 15 * ph) / (360 * (1 + ph))
        ri = L / 6 + (mi - mj) / L
        return ri, L / 2 - ri, mi, mj
    ryi, ryj, myi, myj = plano(phy)
    rzi, rzj, mzi, mzj = plano(phz)
    return np.array([-dx * L / 6, -dy * ryi, -dz * rzi, 0, dz * mzi, -dy * myi,
                     -dx * L / 3, -dy * ryj, -dz * rzj, 0, -dz * mzj, dy * myj])


def _flecha_linear(x, L, ph=0.0):
    """EI·v(x) da viga biengastada com carga triangular 0 → 1 (em nj), como _flecha_pontual: biapoiada
    x(7L⁴ - 10L²x² + 3x⁴)/(360L) + M_bi·ΦL²/12 menos os giros da biapoiada travados (Φ = 0:
    x²(L-x)²(x + 2L)/(120L))."""
    _, n2, _, n4 = _forma(x / L, L, ph)
    return (x * (7 * L**4 - 10 * L * L * x * x + 3 * x**4) / (360 * L) + x * (L * L - x * x) / (6 * L) * ph * L * L / 12
            - 7 * L**3 / 360 * n2 + 8 * L**3 / 360 * n4)


def _engaste_pontual(a, P, L, phy=0.0, phz=0.0):
    """Reações de engastamento perfeito (sobre a barra) p/ carga concentrada local P em x = a.
    Timoshenko (giros da biapoiada travados pela rigidez de _flexao): M_i = P·a·b(b + ΦL/2)/(L²(1+Φ)),
    M_j = P·a·b(a + ΦL/2)/(L²(1+Φ)), R_i = (P·b + M_i - M_j)/L; Φ = 0 dá McGuire (M_i = P·a·b²/L²).
    Axial N_i = P·b/L, N_j = P·a/L."""
    Px, Py, Pz = P
    b = L - a

    def plano(ph):
        mi, mj = a * b * (b + ph * L / 2) / (L * L * (1 + ph)), a * b * (a + ph * L / 2) / (L * L * (1 + ph))
        ri = (b + mi - mj) / L
        return ri, 1 - ri, mi, mj
    ryi, ryj, myi, myj = plano(phy)
    rzi, rzj, mzi, mzj = plano(phz)
    return np.array([-Px * b / L, -Py * ryi, -Pz * rzi, 0, Pz * mzi, -Py * myi,
                     -Px * a / L, -Py * ryj, -Pz * rzj, 0, -Pz * mzj, Py * myj])


def _flecha_pontual(x, a, L, ph=0.0):
    """EI·v(x) da viga biengastada com carga unitária em a (solução particular): biapoiada
    (AISC tab. 3-23 caso 8, + cortante M_bi·ΦL²/12) menos os giros de extremidade da biapoiada
    travados pelas funções de forma de Timoshenko (superposição; Φ = 0 dá AISC tab. 3-23)."""
    b = L - a
    m = b * x / L if x <= a else a * (L - x) / L
    _, n2, _, n4 = _forma(x / L, L, ph)
    return (_flecha_pontual_bi(x, a, L) + m * ph * L * L / 12
            - a * b * (L + b) / (6 * L) * n2 + a * b * (L + a) / (6 * L) * n4)


def _flecha_pontual_bi(x, a, L):
    """EI·v(x) da viga biapoiada com carga unitária em a (AISC Manual tab. 3-23, caso 8)."""
    if x > a:
        x, a = L - x, L - a
    b = L - a
    return b * x * (L * L - b * b - x * x) / (6 * L)


def _condensar(k, fe, rel):
    """Condensação estática dos GL locais liberados (força nula): Przemieniecki cap. 11,
    k* = k_aa - k_ar·k_rr⁻¹·k_ra, f* = f_a - k_ar·k_rr⁻¹·f_r (linhas/colunas rel zeradas)."""
    if not rel:
        return k, fe
    a = [i for i in range(12) if i not in rel]
    X = np.linalg.solve(k[np.ix_(rel, rel)], np.c_[k[np.ix_(rel, a)], fe[rel]])
    kc, fc = np.zeros((12, 12)), np.zeros(12)
    kc[np.ix_(a, a)] = k[np.ix_(a, a)] - k[np.ix_(a, rel)] @ X[:, :-1]
    fc[a] = fe[a] - k[np.ix_(a, rel)] @ X[:, -1]
    return kc, fc


def _liberar(k, fe, ul, rel):
    """Deslocamentos locais com os giros liberados recuperados (f_rel = 0)."""
    if not rel:
        return ul
    a = [i for i in range(12) if i not in rel]
    ul = ul.copy()
    ul[rel] = -np.linalg.solve(k[np.ix_(rel, rel)], k[np.ix_(rel, a)] @ ul[a] + fe[rel])
    return ul


class _Elem:
    def __init__(self, modelo, b):
        ni, nj = modelo.nos[b.ni], modelo.nos[b.nj]
        self.R, self.L = _rotacao((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z), b.rot)
        self.T = _T12(self.R)
        self.Lt = self.L  # comprimento entre nós; self.L = trecho flexível
        sec, mat = modelo.secoes[b.secao], modelo.materiais[b.material]
        self.sec, self.mat = sec, mat
        self.trelica = bool(b.params.get("trelica", False))
        f = b.params.get("fator_EI", 1.0)  # NBR 6118 15.7.3: só E·Iy e E·Iz (EA, GJ, massa não)
        if not (isinstance(f, (int, float)) and not isinstance(f, bool) and math.isfinite(f) and f > 0):
            raise ValueError(f"barra {b.id} ({b.ni}-{b.nj}): fator_EI = {f!r} deve ser > 0")
        self.Iy, self.Iz = f * sec.Iy, f * sec.Iz
        ti, tj = (0.0, 0.0) if self.trelica else (
            b.params.get("trecho_rigido_i", 0.0), b.params.get("trecho_rigido_j", 0.0))
        for t in (ti, tj):
            if not (isinstance(t, (int, float)) and not isinstance(t, bool) and math.isfinite(t) and t >= 0):
                raise ValueError(f"barra {b.id} ({b.ni}-{b.nj}): trecho rígido = {t!r} deve ser ≥ 0")
        if self.Lt - ti - tj <= 1e-6 * self.Lt:
            raise ValueError(f"barra {b.id} ({b.ni}-{b.nj}): trechos rígidos {ti} + {tj} m não deixam "
                             f"trecho flexível (L = {self.Lt:.4f} m)")
        self.ti, self.tj, self.L = float(ti), float(tj), self.Lt - ti - tj
        # u_flex = A·u_nó (locais): v_face = v + θz·r, w_face = w - θy·r, r = +ti em i e -tj em j
        self.A = np.eye(12)
        self.A[1, 5], self.A[2, 4], self.A[7, 11], self.A[8, 10] = ti, -ti, -tj, tj
        self.fr = np.zeros(12)  # reações (sobre a barra, no nó) das cargas nos trechos rígidos
        self.phy = self.phz = 0.0  # Φ de Timoshenko nos planos xy e xz
        if b.params.get("cortante") and not self.trelica:
            Ay, Az = sec.props.get("Ay"), sec.props.get("Az")
            if not all(isinstance(v, (int, float)) and math.isfinite(v) and v > 0 for v in (Ay, Az)):
                raise ValueError(f"barra {b.id}: cortante=True exige props Ay e Az (m², > 0) na seção "
                                 f"{sec.nome} (lidas: Ay={Ay!r}, Az={Az!r})")
            c = 12 * mat.E / (mat.G * self.L**2)
            self.phy, self.phz = c * self.Iz / Ay, c * self.Iy / Az
        self.kl = _k_local(mat.E, mat.G, sec.A, self.Iy, self.Iz, sec.J, self.L, self.trelica,
                           self.phy, self.phz)
        self.rel = [] if self.trelica else (
            [4, 5] * bool(b.params.get("rotula_i")) + [10, 11] * bool(b.params.get("rotula_j")))
        self.cortes = []        # abscissas internas de cargas pontuais (todos os casos)
        self.q = np.zeros(3)    # carga uniforme local do caso
        self.dq = np.zeros(3)   # parcela triangular local do caso (0 em ni, dq em nj): q(x) = q + dq·x/L
        self.p = []             # [(a, P local)] cargas pontuais do caso
        self.fe = np.zeros(12)  # engastamento perfeito completo (antes da condensação)

    def k(self, N=None):
        return self.kl if N is None else self.kl + _kg_local(N, self.L, self.trelica)


def _elementos(modelo):
    elems = {bid: _Elem(modelo, b) for bid, b in modelo.barras.items()}
    for c in modelo.cargas_pontuais:
        e = elems[c.barra]
        if not -TOL_X <= c.a <= e.Lt + TOL_X:
            raise ValueError(f"carga pontual {c.id}: a = {c.a} m fora da barra {c.barra} "
                             f"(L = {e.Lt:.4f} m)")
        a = c.a - e.ti  # abscissa no trecho flexível
        if TOL_X < a < e.L - TOL_X and all(abs(a - x) > TOL_X for x in e.cortes):
            e.cortes.append(float(a))
    for e in elems.values():
        e.cortes.sort()
    return elems


# ---------------------------------------------------------------- montagem

def _indices(modelo):
    ids = list(modelo.nos)
    return ids, {n: i for i, n in enumerate(ids)}


def _gl(idx, b):
    i, j = idx[b.ni], idx[b.nj]
    return np.r_[6 * i:6 * i + 6, 6 * j:6 * j + 6]


def _montar(modelo, elems, idx, ndof, N=None):
    """Rigidez global K e vetor nodal equivalente das cargas de barra (Fb = -Σ Tᵀ·f_eng*)."""
    lin, col, val = [], [], []
    Fb = np.zeros(ndof)
    for bid, b in modelo.barras.items():
        e = elems[bid]
        kc, fc = _condensar(e.k(None if N is None else N[bid]), e.fe, e.rel)
        g = _gl(idx, b)
        lin.append(np.repeat(g, 12)); col.append(np.tile(g, 12))
        TA = e.A @ e.T
        kn = TA.T @ kc @ TA
        if N is not None and (e.ti or e.tj):
            # corda dos trechos rígidos (TC-01): Kg de barra de comprimento t, N/t·[[1,-1],[-1,1]] entre
            # v_nó e v_face = v_nó + θ·t (McGuire 2ª ed. cap. 9, termo de corda) ⇒ N·t nos giros nodais
            # de flexão. Após a condensação: a rótula fica na face, o giro do nó segue o trecho rígido.
            d = np.zeros(12)
            d[[4, 5]], d[[10, 11]] = N[bid] * e.ti, N[bid] * e.tj
            kn += e.T.T @ (d[:, None] * e.T)
        val.append(kn.ravel())
        Fb[g] -= e.T.T @ (e.A.T @ fc + e.fr)
    if not lin:
        return sp.csr_matrix((ndof, ndof)), Fb
    K = sp.coo_matrix((np.concatenate(val), (np.concatenate(lin), np.concatenate(col))),
                      shape=(ndof, ndof)).tocsr()
    return K, Fb


def _cargas(modelo, fatores, elems, idx, ndof):
    """Vetor das cargas nodais ponderadas; grava em cada elemento q, p e fe locais ponderados."""
    F = np.zeros(ndof)
    for e in elems.values():
        e.q, e.dq, e.p, e.fr = np.zeros(3), np.zeros(3), [], np.zeros(12)
    for c in modelo.cargas_nodais:
        g = fatores.get(c.caso, 0.0)
        if g:
            i = idx[c.no]
            F[6 * i:6 * i + 6] += g * np.asarray(c.F, float)
    for c in modelo.cargas_distribuidas:
        g = fatores.get(c.caso, 0.0)
        if g:
            e = elems[c.barra]
            e.q = e.q + e.R @ (g * np.asarray(c.q, float))
            if c.q2 is not None:
                if e.ti or e.tj:
                    raise ValueError(f"carga distribuída {c.id}: carga variável (q2) em barra com trecho rígido "
                                     f"({c.barra}) não é suportada")
                e.dq = e.dq + e.R @ (g * (np.asarray(c.q2, float) - np.asarray(c.q, float)))
    g =fatores.get(modelo.peso_proprio, 0.0) if modelo.peso_proprio else 0.0
    if g:
        for e in elems.values():
            e.q = e.q + e.R @ np.array([0.0, 0.0, -g * e.sec.A * e.mat.peso])
    for c in modelo.cargas_pontuais:
        g = fatores.get(c.caso, 0.0)
        if g:
            e = elems[c.barra]
            _pontual(e, float(c.a), e.R @ (g * np.asarray(c.F, float)))
    for e in elems.values():
        if e.ti:
            _rigido(e, e.ti / 2, e.q * e.ti)
        if e.tj:
            _rigido(e, e.Lt - e.tj / 2, e.q * e.tj)
        if e.trelica:  # biapoiada: só reações de força (estática), sem momento nos nós
            L = e.L
            e.fe = np.r_[-e.q * L / 2 - e.dq * L / 6, 0, 0, 0, -e.q * L / 2 - e.dq * L / 3, 0, 0, 0]
            for a, P in e.p:
                e.fe[:3] -= P * (L - a) / L
                e.fe[6:9] -= P * a / L
        else:
            e.fe = _engaste(e.q, e.L) + _engaste_linear(e.dq, e.L, e.phy, e.phz) + sum((_engaste_pontual(a, P, e.L, e.phy, e.phz) for a, P in e.p),
                                            np.zeros(12))
    return F


def _pontual(e, a, P):
    """Carga pontual local P a `a` m de ni: no trecho flexível (abscissa a - ti) ou no rígido."""
    af = a - e.ti
    if af < -TOL_X or af > e.L + TOL_X:
        _rigido(e, a, P)
    else:
        e.p.append((min(max(af, 0.0), e.L), P))


def _rigido(e, a, P):
    """Carga P local a `a` m de ni sobre trecho rígido: reação no nó = -P e -(r × P) (estática)."""
    k, r = (0, a) if a <= e.ti + TOL_X else (6, a - e.Lt)
    e.fr[k:k + 3] -= P
    e.fr[k + 3:k + 6] -= np.cross([r, 0.0, 0.0], P)


def _diafragmas(modelo, ids, idx, restr):
    """Transformação u = T·u_red dos diafragmas rígidos (Przemieniecki, vínculos lineares por
    eliminação). Devolve T, restrições e rótulos dos GL reduzidos, {pav: nós} e
    {pav com diafragma: índice do 1º GL mestre}."""
    ndof = 6 * len(ids)
    pavs, mestres, escravo, usados = {}, {}, {}, set()
    for nome, p in modelo.pavimentos.items():
        nos = [n for n in ids if abs(modelo.nos[n].z - p.z) <= TOL_PAV]
        if not nos:
            continue
        pavs[nome] = nos
        membros = [n for n in nos if n not in usados
                   and not any(modelo.nos[n].apoio[k] for k in (0, 1, 5))]
        if not p.diafragma or not membros:
            continue
        usados.update(membros)
        # centro só dos nós vinculados (VC-solver-001): nó apoiado fora do vínculo não o desloca
        xc = np.mean([modelo.nos[n].x for n in membros])
        yc = np.mean([modelo.nos[n].y for n in membros])
        mestres[nome] = len(mestres)
        for n in membros:
            i, no = idx[n], modelo.nos[n]
            dx, dy = no.x - xc, no.y - yc
            escravo[6 * i] = (nome, ((0, 1.0), (2, -dy)))
            escravo[6 * i + 1] = (nome, ((1, 1.0), (2, dx)))
            escravo[6 * i + 5] = (nome, ((2, 1.0),))
    livres = [d for d in range(ndof) if d not in escravo]
    base = {nome: len(livres) + 3 * k for nome, k in mestres.items()}
    lin, col, val = list(livres), list(range(len(livres))), [1.0] * len(livres)
    for d, (nome, termos) in escravo.items():
        for comp, c in termos:
            if c:
                lin.append(d); col.append(base[nome] + comp); val.append(c)
    nred = len(livres) + 3 * len(mestres)
    T = sp.coo_matrix((val, (lin, col)), shape=(ndof, nred)).tocsr()
    restr_r = np.r_[restr[livres], np.zeros(3 * len(mestres), bool)]
    rotulos = [f"nó {ids[d // 6]}, GL {GLS[d % 6]}" for d in livres]
    rotulos += [f"pavimento {nome}, GL {g}" for nome in base for g in ("ux", "uy", "rz")]
    return T, restr_r, rotulos, pavs, base


def _suspeito(Kff, livre, rotulos):
    """GL com maior componente no modo de rigidez (quase) nula."""
    # ponytail: autovetor denso até 3000 GL; acima disso a mensagem não localiza o GL
    if Kff.shape[0] > 3000:
        return "GL não localizado (modelo grande)"
    w, v = np.linalg.eigh(Kff.toarray())
    return rotulos[livre[int(np.argmax(np.abs(v[:, 0])))]]


def _resolver(K, F, restr, rotulos, combinacao=None):
    """Resolve K u = F com GL restritos nulos. GL livres sem rigidez (ex.: giros de nós
    só de treliça ou só com barras rotuladas) são fixados se não tiverem carga; com carga ->
    mecanismo. Com `combinacao` (P-Δ, K + Kg), exige K positiva definida: fatoração LDLᵀ com
    permutação simétrica e sem pivotamento; pivô ≤ 0 = inércia negativa (lei da inércia de
    Sylvester; McGuire cap. 9: det(K + Kg) = 0 na carga crítica) -> ErroMecanismo."""
    diag = np.abs(K.diagonal())
    escala = diag.max() if diag.size and diag.max() > 0 else 1.0
    nulo = (diag <= 1e-12 * escala) & ~restr
    carregado = np.flatnonzero(nulo & (np.abs(F) > 1e-9))
    if carregado.size:
        raise ErroMecanismo("GL sem rigidez com carga aplicada: "
                            + "; ".join(rotulos[k] for k in carregado[:10]))
    livre = np.flatnonzero(~restr & ~nulo)
    u = np.zeros(len(F))
    if livre.size == 0:
        return u
    Kff = K[livre][:, livre].tocsc()
    try:
        if combinacao is None:
            lu = spla.splu(Kff)
        else:
            lu = spla.splu(Kff, permc_spec="MMD_AT_PLUS_A", diag_pivot_thresh=0,
                           options={"SymmetricMode": True})
            neg = int(np.sum(lu.U.diagonal() <= 0))
            if neg or not np.array_equal(lu.perm_r, lu.perm_c):  # pivô fora da diagonal: indefinida
                raise ErroMecanismo(
                    f"carga acima da crítica / instabilidade (P-Δ) na combinação {combinacao}: "
                    f"K + Kg não é positiva definida ({neg} pivô(s) ≤ 0); modo instável "
                    f"predominante: {_suspeito(Kff, livre, rotulos)}")
        uf = lu.solve(F[livre])
    except RuntimeError:
        if combinacao is not None:
            raise ErroMecanismo(f"carga acima da crítica / instabilidade (P-Δ) na combinação "
                                f"{combinacao}: K + Kg singular") from None
        raise ErroMecanismo("matriz de rigidez singular (mecanismo); suspeito: "
                            + _suspeito(Kff, livre, rotulos)) from None
    # pivô quase nulo não é detectado pelo splu: confere resíduo e finitude
    res = np.linalg.norm(Kff @ uf - F[livre])
    if not np.all(np.isfinite(uf)) or res > 1e-6 * max(np.linalg.norm(F[livre]), 1.0) \
            or np.min(np.abs(lu.U.diagonal())) < 1e-10 * escala:
        raise ErroMecanismo("matriz de rigidez singular ou mal condicionada (mecanismo); "
                            "suspeito: " + _suspeito(Kff, livre, rotulos))
    u[livre] = uf
    return u


# ---------------------------------------------------------------- pós-processamento

def _forcas(modelo, elems, idx, u, N=None):
    """Forças de extremidade locais (sobre a barra) e deslocamentos locais completos, nas faces
    do trecho flexível."""
    out = {}
    for bid, b in modelo.barras.items():
        e = elems[bid]
        k = e.k(None if N is None else N[bid])
        ul = _liberar(k, e.fe, e.A @ (e.T @ u[_gl(idx, b)]), e.rel)
        out[bid] = (k @ ul + e.fe, ul)
    return out


def _secoes(e, f, ul, n, pdelta=False):
    """Esforços e deslocamentos locais em n pontos (+ a⁻/a⁺ das cargas pontuais) a partir das
    forças de extremidade f (sobre a barra, locais) e deslocamentos locais ul, por equilíbrio
    do trecho [0, x]; com pdelta, na posição deformada (braço v, w da força axial de ni)."""
    L, (qx, qy, qz), (dx, dy, dz) = e.L, e.q, e.dq
    E = e.mat.E
    pts = [(x, True) for x in np.linspace(0.0, L, n) if all(abs(x - c) > TOL_X for c in e.cortes)]
    pts = sorted(pts + [(c, lado) for c in e.cortes for lado in (False, True)])
    # carga na extremidade i age em todo o vão; na extremidade j, em nenhum ponto
    cargas = [(-1.0 if a <= TOL_X else (2 * L if a >= L - TOL_X else a), a, P) for a, P in e.p]
    esf, fle = [], []
    for x, direita in pts:
        Pe, my, mz = np.zeros(3), 0.0, 0.0
        for ae, a, P in cargas:
            if ae < x - TOL_X or (abs(ae - x) <= TOL_X and direita):
                Pe += P
                my += P[2] * (x - a)
                mz += P[1] * (x - a)
        s = x / L
        ux = (ul[0] * (1 - s) + ul[6] * s + qx * x * (L - x) / (2 * E * e.sec.A)
              + dx * x * (L * L - x * x) / (6 * L * E * e.sec.A))
        if e.trelica:  # biapoiada: reta entre os nós (giro nodal não entra) + solução particular
            tri = x * (7 * L**4 - 10 * L * L * x * x + 3 * x**4) / (360 * L)  # EI·v biapoiada, triangular 0 → 1
            uy = ul[1] * (1 - s) + ul[7] * s + (qy * x * (L**3 - 2 * L * x * x + x**3) / 24 + dy * tri) / (E * e.Iz)
            uz = ul[2] * (1 - s) + ul[8] * s + (qz * x * (L**3 - 2 * L * x * x + x**3) / 24 + dz * tri) / (E * e.Iy)
            for _, a, P in cargas:
                g = _flecha_pontual_bi(x, min(max(a, 0.0), L), L)
                uy += P[1] * g / (E * e.Iz)
                uz += P[2] * g / (E * e.Iy)
        else:
            h1, h2, h3, h4 = _forma(s, L, e.phy)
            uy = h1 * ul[1] + h2 * ul[5] + h3 * ul[7] + h4 * ul[11]
            h1, h2, h3, h4 = _forma(s, L, e.phz)
            uz = h1 * ul[2] - h2 * ul[4] + h3 * ul[8] - h4 * ul[10]
        for _, a, P in cargas:  # solução particular axial da carga pontual (biengastada)
            ux += P[0] * ((L - a) * x / L if x <= a else a * (L - x) / L) / (E * e.sec.A)
        if not e.trelica:  # solução particular das cargas de vão (biengastada; cortante: + q·x(L-x)/(2G·As))
            uy += qy * x * (L - x) * (x * (L - x) + e.phy * L * L) / (24 * E * e.Iz)
            uz += qz * x * (L - x) * (x * (L - x) + e.phz * L * L) / (24 * E * e.Iy)
            if dy or dz:
                uy += dy * _flecha_linear(x, L, e.phy) / (E * e.Iz)
                uz += dz * _flecha_linear(x, L, e.phz) / (E * e.Iy)
            for _, a, P in cargas:
                a = min(max(a, 0.0), L)
                uy += P[1] * _flecha_pontual(x, a, L, e.phy) / (E * e.Iz)
                uz += P[2] * _flecha_pontual(x, a, L, e.phz) / (E * e.Iy)
        fle.append((x + e.ti, ux, uy, uz))
        if pdelta:  # momento de f[0] (axial em ni) com braço w(0) - w(x), v(0) - v(x)
            # ponytail: braço só da axial de ni; qx e cargas pontuais na deformada desprezadas
            my += f[0] * (ul[2] - uz)
            mz += f[0] * (ul[1] - uy)
        r2, r3 = x * x / (2 * L), x**3 / (6 * L)  # resultante e momento em x da parcela triangular em [0, x]
        N = -(f[0] + qx * x + dx * r2 + Pe[0])
        Vy = -(f[1] + qy * x + dy * r2 + Pe[1])
        Vz = -(f[2] + qz * x + dz * r2 + Pe[2])
        T = -f[3]
        My = -(f[4] + x * f[2] + qz * x * x / 2 + dz * r3 + my)
        Mz = -(f[5] - x * f[1] - qy * x * x / 2 - dy * r3 - mz)
        esf.append((x + e.ti, N, Vy, Vz, T, My, Mz))
    return esf, fle


def analisar(modelo, caso, pdelta=False, n_pontos=N_PONTOS, tol=1e-6, max_iter=30):
    """Análise linear (ou P-Δ iterativa) de um caso (str) ou combinação ({caso: fator})."""
    fatores = {caso: 1.0} if isinstance(caso, str) else dict(caso)
    for c in fatores:
        if c not in modelo.casos and c != modelo.peso_proprio:
            raise KeyError(f"caso de carga {c!r} não existe no modelo")
    nome = caso if isinstance(caso, str) else " + ".join(f"{g:g}·{c}" for c, g in fatores.items())
    ids, idx = _indices(modelo)
    ndof = 6 * len(ids)
    elems = _elementos(modelo)
    restr = np.array([r for n in ids for r in modelo.nos[n].apoio], bool)
    Tr, restr_r, rotulos, pavs, mestres = _diafragmas(modelo, ids, idx, restr)
    Fn = _cargas(modelo, fatores, elems, idx, ndof)

    def resolver(N=None):
        K, Fb = _montar(modelo, elems, idx, ndof, N)
        F = Fn + Fb
        ur = _resolver((Tr.T @ K @ Tr).tocsr(), Tr.T @ F, restr_r, rotulos,
                       None if N is None else nome)
        return Tr @ ur, ur, K, F

    u, ur, K, F = resolver()
    fe = _forcas(modelo, elems, idx, u)
    if pdelta:
        # Kg com N do passo anterior; acima da crítica K + Kg indefinida -> ErroMecanismo
        for _ in range(max_iter):
            N = {bid: -v[0][0] for bid, v in fe.items()}  # tração na extremidade i
            u_novo, ur, K, F = resolver(N)
            dif = np.linalg.norm(u_novo - u) / max(np.linalg.norm(u_novo), 1e-30)
            u = u_novo
            fe = _forcas(modelo, elems, idx, u, N)
            if dif < tol:
                break
        else:
            raise ErroMecanismo("P-Δ não convergiu (carga próxima/acima da crítica)")

    Rg = K @ u - F
    r = Resultado()
    for i, n in enumerate(ids):
        r.deslocamentos[n] = u[6 * i:6 * i + 6].copy()
        if any(modelo.nos[n].apoio):
            r.reacoes[n] = np.where(modelo.nos[n].apoio, Rg[6 * i:6 * i + 6], 0.0)
    for bid, (f, ul) in fe.items():
        r.esforcos[bid], r.flechas[bid] = _secoes(elems[bid], f, ul, n_pontos, pdelta)
    for nome, nos in pavs.items():
        if nome in mestres:
            r.pavimentos[nome] = ur[mestres[nome]:mestres[nome] + 3].copy()
        else:
            r.pavimentos[nome] = np.mean([u[6 * idx[n] + np.array([0, 1, 5])] for n in nos], axis=0)
    return r


def combinar(resultados, fatores):
    """Combinação linear: resultados {caso: Resultado}, fatores {caso: γ}."""
    r = Resultado()
    for caso, g in fatores.items():
        a = resultados[caso]
        for nome in ("deslocamentos", "reacoes", "pavimentos"):
            dst = getattr(r, nome)
            for k, v in getattr(a, nome).items():
                dst[k] = dst.get(k, 0.0) + g * np.asarray(v)
        for nome, dst in (("esforcos", r.esforcos), ("flechas", r.flechas)):
            for k, lst in getattr(a, nome).items():
                arr = np.asarray(lst, float)
                if k in dst:
                    dst[k][:, 1:] += g * arr[:, 1:]
                else:
                    dst[k] = arr.copy()
                    dst[k][:, 1:] *= g
    for nome in ("esforcos", "flechas"):
        d = getattr(r, nome)
        for k in d:
            d[k] = [tuple(l) for l in d[k]]
    return r


def eixos_locais(modelo, barra_id):
    """(ex, ey, ez) da barra em coordenadas globais (listas de 3 floats; mesma matriz da rigidez)."""
    b = modelo.barras[barra_id]
    ni, nj = modelo.nos[b.ni], modelo.nos[b.nj]
    R, _ = _rotacao((ni.x, ni.y, ni.z), (nj.x, nj.y, nj.z), b.rot)
    return tuple(R[k].tolist() for k in range(3))


def _peso_vertical(modelo, fatores, elems, idx, ndof):
    """Peso por nó (kN, para baixo positivo): -Fz das cargas nodais + reações de engastamento
    (força vertical) das cargas de barra. Momentos de engaste não entram."""
    Fn = _cargas(modelo, fatores, elems, idx, ndof)
    _, Fb = _montar(modelo, elems, idx, ndof)
    return -(Fn + Fb)[2::6]


def pesos_nodais(modelo, fatores):
    """{nó: peso vertical em kN (para baixo positivo)} das cargas dos casos ponderados."""
    ids, idx = _indices(modelo)
    p = _peso_vertical(modelo, dict(fatores), _elementos(modelo), idx, 6 * len(ids))
    return {n: float(p[i]) for i, n in enumerate(ids)}


# ---------------------------------------------------------------- modal

def modal(modelo, n_modos, casos_massa=None):
    """Análise modal com massa concentrada nos nós (translacional, igual em x, y, z),
    obtida do peso vertical (|Fz| para baixo) dos casos ponderados (ex.: G + ψ2·Q).
    Pavimento com diafragma: mesma transformação u = T·u_red da estática; K_red = Tᵀ·K·T e
    M_red = Tᵀ·M·T (Przemieniecki, vínculos por transformação), que dá ao rz do centro a inércia
    de massa Σ m·r² e o acoplamento m·dx, m·dy (Clough & Penzien, diafragma rígido).
    Retorna (freqs_hz, modos [{no: array(6)}], participacao [array(3) fração por x,y,z]);
    a participação é sobre a massa dos GL que se movem (livres ou presos ao diafragma)."""
    if casos_massa is None:
        casos_massa = {c: 1.0 for c, cc in modelo.casos.items() if cc.tipo == "G"}
        if modelo.peso_proprio:
            casos_massa.setdefault(modelo.peso_proprio, 1.0)
    ids, idx = _indices(modelo)
    ndof = 6 * len(ids)
    elems = _elementos(modelo)
    restr = np.array([r for n in ids for r in modelo.nos[n].apoio], bool)
    Tr, restr_r, _, _, _ = _diafragmas(modelo, ids, idx, restr)
    K = (Tr.T @ _montar(modelo, elems, idx, ndof)[0] @ Tr).toarray()

    peso = np.zeros(len(ids))
    for caso, fat in casos_massa.items():
        # ponytail: momentos de engaste ignorados; só a força vertical vira massa
        peso += fat * np.maximum(_peso_vertical(modelo, {caso: 1.0}, elems, idx, ndof), 0.0)
    m = np.zeros(ndof)
    for k in range(3):
        m[k::6] = peso / G_ACEL
    Mr = (Tr.T @ sp.diags(m) @ Tr).toarray()  # diagonal sem diafragma (T = I)
    mr = np.diag(Mr)

    livre = ~restr_r & (np.abs(np.diag(K)) > 1e-12 * np.abs(np.diag(K)).max())
    mest = np.flatnonzero(livre & (mr > 0))
    escr = np.flatnonzero(livre & (mr <= 0))
    if mest.size == 0:
        raise ValueError("sem massa nos GL livres")
    # condensação estática dos GL sem massa (Guyan, exata para massa nula)
    Kmm = K[np.ix_(mest, mest)]
    if escr.size:
        Kms, Kss = K[np.ix_(mest, escr)], K[np.ix_(escr, escr)]
        try:
            X = sla.solve(Kss, Kms.T, assume_a="sym")
        except sla.LinAlgError:
            raise ErroMecanismo("matriz de rigidez singular na análise modal") from None
        Kc = Kmm - Kms @ X
    else:
        X, Kc = None, Kmm
    n_modos = min(n_modos, mest.size)
    try:
        w2, phi = sla.eigh(Kc, Mr[np.ix_(mest, mest)], subset_by_index=[0, n_modos - 1])
    except sla.LinAlgError:
        raise ErroMecanismo("autoproblema mal posto (mecanismo)") from None
    if w2[0] <= 1e-9 * abs(w2[-1]) and w2[0] < 1e-6:
        raise ErroMecanismo("modo de corpo rígido (mecanismo)")
    freqs = np.sqrt(np.maximum(w2, 0.0)) / (2 * np.pi)

    # participação em coordenadas completas (Clough & Penzien: L_d = φᵀ·M·ι_d, M*_d = L_d²/(φᵀMφ))
    move = np.asarray(abs(Tr[:, mest]).sum(axis=1)).ravel() > 0  # GL completos com massa que se movem
    dirs = [np.arange(ndof) % 6 == d for d in range(3)]
    mt = [m[r & move].sum() for r in dirs]
    modos, part = [], []
    for k in range(n_modos):
        vr = np.zeros(K.shape[0])
        vr[mest] = phi[:, k]
        if escr.size:
            vr[escr] = -X @ phi[:, k]
        v = Tr @ vr
        modos.append({n: v[6 * i:6 * i + 6].copy() for i, n in enumerate(ids)})
        mg = phi[:, k] @ Mr[np.ix_(mest, mest)] @ phi[:, k]
        part.append(np.array([(v[r] @ m[r])**2 / mg / mt[d] if mt[d] > 0 else 0.0
                              for d, r in enumerate(dirs)]))
    return freqs, modos, part
