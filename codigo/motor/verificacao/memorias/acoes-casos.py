"""Memória de cálculo independente — ações (ficha 012). Só stdlib; NÃO importa prossen_estrutural.

Fontes conferidas pelo verificador em 2026-10-20 (ver 2026-10-20-acoes.md):
- NBR 6123:1988 (cópia pública em professormendoncauenf.com.br, crr_nbr_6123_forcasvento.pdf):
  tabela 1 (b, Fr, p, zg), tabela 2 (S2) e 5.3.3 ("S2 constante até 10 m na categoria V"), fig. 4 (Ca).
- NBR 6123:2023: comparativo "ABNT NBR6123:2023 principais mudanças" (calculistadeaco.com.br, slides 3 e 4):
  S2 com o mesmo procedimento (tabela 1 bm/p, tabela 2 = Fr); S3 tabela 4 = 1,11/1,06/1,00/0,95/0,83, 0,92·S3 em vedações.
- NBR 15421:2006: França (TCC UFCG 2021) e Miranda (dissertação UFC 2011): tabela 3 (Ca, Cv), Cs (9.1),
  Ta (9.2), Cup por ZONA (2: 1,7; 3: 1,6; 4: 1,5), k (9.3), zona 1: Fx = 0,01·wx; w com 25% de Q em armazenagem.
- NBR 8681:2003 tabelas 1, 2, 4, 5, 6 (reprodução em ecivilufes.wordpress.com, "4-6 ações e segurança").
- NBR 6120:2019 tabela 1 (calculistadeaco.com.br, tabela de peso específico) e tabela 10 (UFAL, PFF — Ações e
  Segurança, notas de aula, slide 6).
Rodar: python acoes-casos.py
"""
import math

# ---------------------------------------------------------------- NBR 6123 tabela 2 (S2), linhas usadas
# colunas: I(A,B,C) II(A,B,C) III(A,B,C) IV(A,B,C) V(A,B,C)
TAB2 = {
    5:   [1.06, 1.04, 1.01, 0.94, 0.92, 0.89, 0.88, 0.86, 0.82, 0.79, 0.76, 0.73, 0.74, 0.72, 0.67],
    10:  [1.10, 1.09, 1.06, 1.00, 0.98, 0.95, 0.94, 0.92, 0.88, 0.86, 0.83, 0.80, 0.74, 0.72, 0.67],
    15:  [1.13, 1.12, 1.09, 1.04, 1.02, 0.99, 0.98, 0.96, 0.93, 0.90, 0.88, 0.84, 0.79, 0.76, 0.72],
    30:  [1.17, 1.17, 1.15, 1.10, 1.08, 1.06, 1.05, 1.03, 1.00, 0.98, 0.96, 0.93, 0.87, 0.85, 0.82],
    50:  [1.21, 1.21, 1.19, 1.15, 1.13, 1.12, 1.10, 1.09, 1.06, 1.04, 1.02, 0.99, 0.94, 0.93, 0.89],
    100: [1.26, 1.26, 1.25, 1.22, 1.21, 1.20, 1.18, 1.17, 1.15, 1.13, 1.11, 1.09, 1.05, 1.03, 1.01],
    200: [1.32, 1.32, 1.32, 1.29, 1.28, 1.28, 1.27, 1.26, 1.25, 1.23, 1.21, 1.20, 1.16, 1.16, 1.14],
}
CATS = ["I", "II", "III", "IV", "V"]
CLS = ["A", "B", "C"]
# tabela 1 (b, p) e Fr (sempre da categoria II)
B = {"I": (1.10, 1.11, 1.12), "II": (1.00, 1.00, 1.00), "III": (0.94, 0.94, 0.93),
     "IV": (0.86, 0.85, 0.84), "V": (0.74, 0.73, 0.71)}
P = {"I": (0.06, 0.065, 0.07), "II": (0.085, 0.09, 0.10), "III": (0.10, 0.105, 0.115),
     "IV": (0.12, 0.125, 0.135), "V": (0.15, 0.16, 0.175)}
FR = (1.00, 0.98, 0.95)


def s2_tabela(cat, cls, z):
    return TAB2[z][CATS.index(cat) * 3 + CLS.index(cls)]


def s2_formula(cat, cls, z):
    """5.3.3: S2 = b·Fr·(z/10)^p; z ≤ 5 → 5 (início da tabela 2); categoria V: constante até 10 m."""
    j = CLS.index(cls)
    zmin = 10.0 if cat == "V" else 5.0
    return B[cat][j] * FR[j] * (max(z, zmin) / 10.0) ** P[cat][j]


def q_kN(V0, S1, S2, S3):
    """NBR 6123 4.2: q = 0,613·Vk² (N/m²), Vk = V0·S1·S2·S3."""
    return 0.613 * (V0 * S1 * S2 * S3) ** 2 / 1000.0


# ---------------------------------------------------------------- exemplo da ficha 005 (vento)
CA_LIDO = {  # leitura do verificador na fig. 4 (pixels da reprodução pública, escala log; ±0,02)
    (0.6, 1.0): 0.927,    # curva 0,9 em h/l1 = 0,80 e 1,0 em 1,83
    (1.667, 0.6): 1.166,  # curva 1,2 em h/l1 = 0,742 e 1,3 em 1,373
    (1.0, 0.5): 1.00,     # curva 1,0 corta a base em l1/l2 = 1,0
    (1.0, 1.0): 1.138,
    (1.0, 5.0): 1.398,
    (1.0, 10.0): 1.554,
    (2.0, 1.0): 1.276,
    (2.0, 0.5): 1.162,
    (0.6, 0.5): 0.843,
    (0.4, 0.5): 0.71,
}
CA_LITERATURA = {  # leituras publicadas (Manual CBCA/Instituto Aço Brasil 2011, via TCC Thais, UFSJ)
    (0.6, 1.333): 0.95,
    (1.667, 0.8): 1.2,
}


def forcas_ficha005(direcao="+X"):
    """20 × 12 m, 4 pavimentos de 3 m, V0 = 35, S1 = 1, cat. IV, classe B, S3 = 1 (grupo 3).
    Fa = Ca·q(z)·l1·h_infl (NBR 6123 4.2.3, força de arrasto; faixa de influência = meia altura dos
    andares adjacentes; topo = 1,5 m). +X: l1 = 12 (perpendicular), l2 = 20."""
    l1, l2 = (12.0, 20.0) if direcao in ("+X", "-X") else (20.0, 12.0)
    h = 12.0
    Ca = CA_LIDO[(round(l1 / l2, 3), round(h / l1, 3))]
    out = []
    for z, hinf in [(3, 3.0), (6, 3.0), (9, 3.0), (12, 1.5)]:
        q = q_kN(35.0, 1.0, s2_formula("IV", "B", z), 1.0)
        out.append((z, Ca * q * l1 * hinf))
    return Ca, out


# ---------------------------------------------------------------- NBR 15421:2006
TAB3 = {"A": (0.8, 0.8, 0.8, 0.8), "B": (1.0, 1.0, 1.0, 1.0), "C": (1.2, 1.2, 1.7, 1.7),
        "D": (1.6, 1.5, 2.4, 2.2), "E": (2.5, 2.1, 3.5, 3.4)}  # Ca(≤0,10), Ca(0,15), Cv(≤0,10), Cv(0,15)
CUP_ZONA = {2: 1.7, 3: 1.6, 4: 1.5}


def ca_cv(ag, cls):
    t = TAB3[cls]
    f = min(max((ag - 0.10) / 0.05, 0.0), 1.0)
    return t[0] + f * (t[1] - t[0]), t[2] + f * (t[3] - t[2])


def sismo(ag, cls, R, I, hs, ws, sistema=(0.0466, 0.9), zona=2, T_modal=None):
    """9.1: Cs = 2,5·ags0/(R/I) ≤ ags1/(T·R/I), ≥ 0,01; 9.2: Ta = Ct·hn^x, T ≤ Cup(zona)·Ta;
    9.3: k = 1 (T ≤ 0,5), (T+1,5)/2, 2 (T ≥ 2,5); Fx = H·wx·hx^k/Σ wi·hi^k."""
    Ca, Cv = ca_cv(ag, cls)
    ags0, ags1 = Ca * ag, Cv * ag
    Ta = sistema[0] * max(hs) ** sistema[1]
    T = Ta if T_modal is None else min(T_modal, CUP_ZONA[zona] * Ta)
    Cs = max(min(2.5 * ags0 / (R / I), ags1 / (T * R / I)), 0.01)
    k = 1.0 if T <= 0.5 else (2.0 if T >= 2.5 else (T + 1.5) / 2)
    H = Cs * sum(ws)
    s = sum(w * h**k for w, h in zip(ws, hs))
    return dict(Ca=Ca, Cv=Cv, Ta=Ta, T=T, Cs=Cs, k=k, H=H, F=[H * w * h**k / s for w, h in zip(ws, hs)])


# ---------------------------------------------------------------- NBR 8681:2003
GAMA_G_T2 = {"normal": (1.40, 1.0), "especial": (1.30, 1.0), "excepcional": (1.20, 1.0)}   # edificação tipo 2
GAMA_G_T2_TIPO1 = {"normal": 1.35, "especial": 1.25, "excepcional": 1.15}                   # tipo 1 (q > 5 kN/m²)
GAMA_Q_T5 = {"normal": {"tipo1": 1.5, "tipo2": 1.4}, "especial": {"tipo1": 1.3, "tipo2": 1.2},
             "excepcional": {"tipo1": 1.0, "tipo2": 1.0}}
GAMA_Q_T4 = {"vento": 1.4, "temperatura": 1.2, "geral": 1.5}
PSI_T6 = {"residencial": (0.5, 0.4, 0.3), "comercial": (0.7, 0.6, 0.4), "biblioteca": (0.8, 0.7, 0.6),
          "vento": (0.6, 0.3, 0.0), "temperatura": (0.6, 0.5, 0.3)}


def contagem_elu_normal(grupos):
    """Convenção documentada em combinacoes.py: cada combinação = escolha de no máximo um caso por grupo
    (não vazia) e uma principal entre os escolhidos. Nº de pares (seleção, principal) =
    Σ_g n_g · Π_{h≠g} (1 + n_h)."""
    tot = 0
    for i, n in enumerate(grupos):
        p = n
        for j, m in enumerate(grupos):
            if j != i:
                p *= 1 + m
        tot += p
    return tot


if __name__ == "__main__":
    print("S2 fórmula × tabela 2 (|dif| > 0,005):")
    for z in TAB2:
        for c in CATS:
            for k in CLS:
                f, t = s2_formula(c, k, z), s2_tabela(c, k, z)
                if abs(f - t) > 0.0051:
                    print(f"  z={z} {c}{k}: fórmula {f:.4f} tabela {t}")
    print("q(12 m, IV B, V0 35) =", q_kN(35, 1, s2_formula("IV", "B", 12), 1))
    for d in ("+X", "+Y"):
        print("vento ficha 005", d, forcas_ficha005(d))
    print("sismo zona 2 D, R=3, 4×1000 kN:", sismo(0.10, "D", 3.0, 1.0, [3, 6, 9, 12], [1000] * 4))
    print("sismo zona 2 D, T modal 1,0:", sismo(0.10, "D", 3.0, 1.0, [3, 6, 9, 12], [1000] * 4, T_modal=1.0))
    print("sismo zona 4 A, T modal 2,0:", sismo(0.15, "A", 3.0, 1.0, [3, 6, 9, 12], [1000] * 4, zona=4, T_modal=2.0))
    print("sismo ag 0,125 E (interpolação):", ca_cv(0.125, "E"))
    print("ELU normal 2Q + 4V(1 grupo):", contagem_elu_normal([1, 1, 4]))
