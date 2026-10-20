"""Memória de cálculo independente — ligações (NBR 8800:2008, cap. 6). Só stdlib.

Não importa prossen_estrutural. Unidades: N, mm, MPa (N/mm²); resultados em kN.
Fontes:
  [NBR]    ABNT NBR 8800:2008 — 6.2.5 tab. 8, 6.2.6 tab. 10, 6.3.3.1–6.3.3.4, 6.3.9, 6.3.11 tab. 14,
           6.3.12, 6.5.3, 6.5.5, 6.5.6, anexo A tab. A.3 (parafusos) e A.4 (fw).
  [GERDAU] O. T. Baião Filho e A. C. V. Silva, "Ligações para estruturas de aço — guia prático
           para estruturas com perfis laminados", Gerdau, 6ª ed., 2014: tab. 1–4 p. 15 (A325),
           LCHS 30-B p. 94 (chapa simples 6,3 mm, 3 φ 3/4", Vd = 120 kN pelo método do CIR/AISC).
  [CBCA]   CBCA, "Ligações em estruturas metálicas", vol. 2, 2011, ex. 5.1 p. 10–17.
Rodar: python verificacao/memorias/ligacoes-casos.py  (imprime a tabela de valores esperados)
"""
import math

GA1, GA2, GW2 = 1.10, 1.35, 1.35  # NBR 8800 tabela 3 (normais) e tabela 8
POL = 25.4


def Ab(d):
    return math.pi * d * d / 4


# ---------------------------------------------------------------- parafusos
def Ft(d, fub):          # 6.3.3.1: Abe = 0,75 Ab
    return 0.75 * Ab(d) * fub / GA2 / 1e3


def Fv(d, fub, k=0.4):   # 6.3.3.2: 0,4 (rosca no plano / comuns) ou 0,5
    return k * Ab(d) * fub / GA2 / 1e3


def Fc(lf, t, d, fu, lim=True):  # 6.3.3.3 a)
    a, b = (1.2, 2.4) if lim else (1.5, 3.0)
    return min(a * lf * t * fu, b * d * t * fu) / GA2 / 1e3


# NBR tab. A.3 (fub, MPa): A325 825 (16 ≤ db ≤ 24 mm; 1/2"–1"), 725 (24 < db ≤ 36; 1"–1 1/2");
# A490 1035; A307 415; ISO 4.6 400; ISO 8.8 800; ISO 10.9 1000.
CASOS_PARAFUSO = {
    # [GERDAU] tab. 1 e 2 (kN): A325, rosca no plano
    "A325 5/8 Ft": (Ft(5 / 8 * POL, 825), 90.8), "A325 3/4 Ft": (Ft(0.75 * POL, 825), 130.6),
    "A325 7/8 Ft": (Ft(7 / 8 * POL, 825), 177.8), "A325 1 Ft": (Ft(POL, 825), 232.4),
    "A325 5/8 Fv": (Fv(5 / 8 * POL, 825), 48.4), "A325 3/4 Fv": (Fv(0.75 * POL, 825), 69.7),
    "A325 7/8 Fv": (Fv(7 / 8 * POL, 825), 94.8), "A325 1 Fv": (Fv(POL, 825), 123.9),
    # [GERDAU] tab. 4 (borda, furo do guia 18 mm → lf = 1,5·15,875 − 9 = 14,81 mm), A36, t = 10 mm
    "Fc 5/8 A36 borda": (Fc(1.5 * 5 / 8 * POL - 9.0, 10, 5 / 8 * POL, 400), 52.6),
    # [GERDAU] tab. 3 (entre furos s = 3d, furo 21 mm → lf = 57,15 − 21 = 36,15 mm), A572-50, t = 10 mm
    "Fc 3/4 A572 furos": (Fc(3 * 0.75 * POL - 21.0, 10, 0.75 * POL, 450), 144.6),
}


# ---------------------------------------------------------------- solda de filete
def filete_solda(b, L, fw):            # tab. 8: 0,6·Aw·fw/γw2, Aw = 0,707·b·L (6.2.2.2)
    return 0.6 * 0.707 * b * L * fw / GW2 / 1e3


def filete_base_fusao(b, L, fy):       # CBCA 5.1.6 p. 16: AMB = face de fusão b·L, 0,6·fy/γa1
    return 0.6 * b * L * fy / GA1 / 1e3


def filete_min(t):                     # tab. 10
    return 3 if t <= 6.35 else 5 if t <= 12.5 else 6 if t <= 19 else 8


def filete_max(t):                     # 6.2.6.2.2
    return t if t < 6.35 else t - 1.5


# ---------------------------------------------------------------- tabela 14 (mm): (serra/tesoura, laminada/maçarico)
TAB14 = {12.7: (22, 19), 15.875: (29, 22), 16: (29, 22), 19.05: (32, 26), 20: (35, 27),
         22.225: (38, 29), 22: (38, 29), 24: (42, 31), 25.4: (44, 32), 28.575: (50, 38),
         27: (50, 38), 30: (53, 39), 31.75: (57, 42), 36: (64, 46)}


def borda_min(d, laminada=False):
    if d > 36 or (d > 31.75 and d not in TAB14):
        return (1.25 if laminada else 1.75) * d
    return TAB14[d][1 if laminada else 0]


# ---------------------------------------------------------------- chapa simples (LCHS 30-B), método elástico
def chapa_simples():
    d, dh, dn = 0.75 * POL, 0.75 * POL + 1.5, 0.75 * POL + 3.5  # furo-padrão; +2 mm nas áreas líquidas (5.2.4.1)
    h, t, n, s, eh, e, bw = 230.0, 6.3, 3, 75.0, 40.0, 75.0, 5.0
    tw, fu_w = 6.6, 450.0                         # alma W310x32.7, A572 GR.50
    fy, fu = 250.0, 400.0                         # chapa A36
    ev = (h - (n - 1) * s) / 2                    # 40 mm
    ys = [-(n - 1) * s / 2 + i * s for i in range(n)]
    J = sum(y * y for y in ys)
    k_paraf = math.hypot(1 / n, e * max(ys) / J)  # força no parafuso crítico / V
    lf_ch = min(ev - dh / 2, eh - dh / 2, s - dh)  # conservador (qualquer direção), como declarado no módulo
    lf_al = min(40.0 - dh / 2, s - dh)             # e_alma = 40 mm
    r = {}
    r["parafuso_cisalhamento"] = Fv(d, 825) / k_paraf
    r["contato_chapa"] = Fc(lf_ch, t, d, fu) / k_paraf
    r["contato_alma"] = Fc(lf_al, tw, d, fu_w) / k_paraf
    r["chapa_escoamento_cis"] = 0.6 * fy * h * t / GA1 / 1e3
    r["chapa_ruptura_cis"] = 0.6 * fu * (h - n * dn) * t / GA2 / 1e3
    Agv = ((n - 1) * s + ev) * t
    Anv = Agv - (n - 0.5) * dn * t
    Ant = (eh - 0.5 * dn) * t
    r["chapa_block_shear"] = min(0.6 * fu * Anv + fu * Ant, 0.6 * fy * Agv + fu * Ant) / GA2 / 1e3
    W = t * h * h / 6
    r["chapa_flexao_bruta"] = fy * W / GA1 / e / 1e3
    # solda: dois filetes de comprimento h, cisalhamento V/(2h) + flexão V·e/(2h²/6) por mm
    k_solda = math.hypot(1 / (2 * h), e / (2 * h * h / 6))
    r["solda_metal"] = 0.6 * 0.707 * bw * 485 / GW2 / 1e3 / k_solda
    r["solda_base_fusao"] = 0.6 * bw * fy / GA1 / 1e3 / k_solda
    return r, k_paraf


# ---------------------------------------------------------------- ligação de tração 2 × 3 parafusos 3/4"
def tracao():
    d, dh, dn = 0.75 * POL, 0.75 * POL + 1.5, 0.75 * POL + 3.5
    b, t, nl, npl, s, g, e1 = 200.0, 9.5, 2, 3, 70.0, 100.0, 40.0
    fy, fu = 250.0, 400.0
    r = {}
    r["parafusos_cisalhamento"] = nl * npl * Fv(d, 825)
    # 6.3.3.3: soma por furo, lf na direção da força
    r["contato_soma_por_furo"] = nl * (Fc(e1 - dh / 2, t, d, fu) + (npl - 1) * Fc(s - dh, t, d, fu))
    r["contato_lf_minimo_todos"] = nl * npl * Fc(min(e1 - dh / 2, s - dh), t, d, fu)
    Ag = b * t
    An = (b - nl * dn) * t
    r["escoamento_bruta"] = Ag * fy / GA1 / 1e3
    r["ruptura_liquida"] = min(An, 0.85 * Ag) * fu / GA2 / 1e3   # 6.5.3: Ae = An ≤ 0,85 Ag
    Agv = nl * (e1 + (npl - 1) * s) * t
    Anv = Agv - nl * (npl - 0.5) * dn * t
    Ant = (g - (nl - 1) * dn) * t
    r["block_shear"] = min(0.6 * fu * Anv + fu * Ant, 0.6 * fy * Agv + fu * Ant) / GA2 / 1e3
    return r


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")
    print("Parafusos (calculado × livro):")
    for k, (calc, livro) in CASOS_PARAFUSO.items():
        print(f"  {k:22s} {calc:8.2f}  {livro:7.1f}  dif {100 * (calc / livro - 1):+.2f} %")
    print(f"A490 3/4 Ft = {Ft(19.05, 1035):.2f} kN; Fv(0,5) = {Fv(19.05, 1035, 0.5):.2f} kN")
    print(f"A325 M27 Ft (fub 725) = {Ft(27, 725):.2f} kN; M24 (825) = {Ft(24, 825):.2f} kN")
    print(f"Interação 60/{Ft(19.05, 825):.1f} + 40/{Fv(19.05, 825):.1f}: "
          f"{(60 / Ft(19.05, 825)) ** 2 + (40 / Fv(19.05, 825)) ** 2:.4f}")
    print(f"Filete b=5 L=200 E70: metal {filete_solda(5, 200, 485):.2f} kN; base (fusão, A36) "
          f"{filete_base_fusao(5, 200, 250):.2f} kN")
    print("Tab. 10:", {t: filete_min(t) for t in (6.35, 6.36, 12.5, 12.6, 19.0, 19.1)})
    print("Tab. 14 serra/lam:", {d: (borda_min(d), borda_min(d, True)) for d in (12.7, 16, 19.05, 20, 22, 24, 25.4, 27, 30, 36, 40)})
    r, k = chapa_simples()
    print(f"Chapa simples LCHS 30-B (V resistente por critério, k_paraf = {k:.4f}):")
    for kk, v in r.items():
        print(f"  {kk:24s} {v:8.2f} kN")
    print(f"  → V_Rd = {min(r.values()):.2f} kN ({min(r, key=r.get)}); Gerdau (CIR) 120 kN")
    print("Ligação de tração 2×3 φ 3/4\", chapa 200×9,5 A36:")
    for kk, v in tracao().items():
        print(f"  {kk:24s} {v:8.2f} kN")
