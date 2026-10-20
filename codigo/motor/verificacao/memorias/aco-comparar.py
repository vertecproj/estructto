"""Comparação memória independente × código (NBR 8800 e seções). Roda depois das memórias.

python -X utf8 verificacao/memorias/aco-comparar.py   (a partir de software/)
Imprime uma linha por grandeza: caso | esperado (memória) | obtido (código) | dif.
"""
import importlib.util
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(AQUI)))


def carrega(nome):
    sp = importlib.util.spec_from_file_location(nome.replace("-", "_"), os.path.join(AQUI, nome + ".py"))
    mod = importlib.util.module_from_spec(sp)
    sp.loader.exec_module(mod)
    return mod


sec = carrega("aco-secoes")
mem = carrega("aco-nbr8800")
from prossen_estrutural import secoes as s, materiais as m  # noqa: E402
from prossen_estrutural.aco import nbr8800 as n  # noqa: E402

LIN = []


def cmp(caso, esp, obt, tol=0.01):
    d = abs(obt - esp) / abs(esp) if esp else abs(obt)
    LIN.append((caso, esp, obt, d, "OK" if d <= tol else "DIF"))


a36, a572 = m.aco("A-36"), m.aco("A-572 GR.50")
fy = 25.0

# ---------------------------------------------------------------- seções
w = s.catalogo("W200x26.6")
wm = sec.I_laminado(207, 133, 5.8, 8.4, 10)
for k, p in (("A", 2), ("Ix", 4), ("Iy", 4), ("Zx", 3), ("Zy", 3), ("Cw", 6)):
    cmp(f"W200x26.6 {k}", wm[k] * 1e-3 ** p if p != 2 else wm[k] * 1e-6,
        w.props[k] if k != "A" else w.A, 0.005)
cmp("W200x26.6 J (alma até linha média das mesas)", wm["J_h0"] * 1e-12, w.props["J"], 0.005)

U = s.secao_U(0.200, 0.075, 0.008, 0.012)  # U laminado/soldado de mesas paralelas
um = sec.parede_fina([(75 - 4, 94), (0, 94), (0, -94), (75 - 4, -94)], [(0, 1), (1, 2), (2, 3)], 1.0)
# parede fina com espessuras diferentes: integrador só aceita t único → fórmulas fechadas (Galambos):
hp, bp, tw, tf = 200 - 12, 75 - 4, 8.0, 12.0
xb = bp ** 2 * tf / (2 * bp * tf + hp * tw)
e = 3 * bp ** 2 * tf / (6 * bp * tf + hp * tw)
Cw_U = tf * bp ** 3 * hp ** 2 / 12 * (3 * bp * tf + 2 * hp * tw) / (6 * bp * tf + hp * tw)
cmp("U 200x75x8x12 x0 (CC-centroide, parede fina)", (e + xb) * 1e-3, abs(U.props["x0"]), 0.03)
cmp("U 200x75x8x12 Cw (parede fina)", Cw_U * 1e-18, U.props["Cw"], 0.03)
cmp("U 200x75x8x12 A", (2 * 75 * 12 + 176 * 8) * 1e-6, U.A, 1e-6)

L = s.secao_cantoneira(0.100, 0.100, 0.010)
Lm = sec.cantoneira(100, 100, 10)
cmp("L100x10 A", Lm["A"] * 1e-6, L.A, 1e-6)
cmp("L100x10 Ix geom.", Lm["Ix"] * 1e-12, L.props["Ix"], 1e-6)
cmp("L100x10 Imin", Lm["Imin"] * 1e-12, L.props.get("Imin", float("nan")), 1e-4)
cmp("L100x10 J", Lm["J"] * 1e-12, L.props["J"], 0.02)
cmp("L100x10 |x0|", abs(Lm["x0"]) * 1e-3, abs(L.props["x0"]), 0.01)

L2 = s.secao_cantoneira(0.150, 0.100, 0.010)
L2m = sec.cantoneira(150, 100, 10)
cmp("L150x100x10 Ixy→Imin", L2m["Imin"] * 1e-12, L2.props.get("Imin", float("nan")), 1e-4)

T = s.secao_tubo_retangular(0.200, 0.100, 0.006)
Tm = sec.tubo_retangular(200, 100, 6)
for k in ("A", "Ix", "Iy", "J"):
    cmp(f"TR 200x100x6 {k}", Tm[k] * (1e-6 if k == "A" else 1e-12), T.A if k == "A" else T.props[k], 0.01)
C = s.secao_tubo_circular(0.1683, 0.007)
Cm = sec.tubo_circular(168.3, 7)
for k in ("A", "Ix", "J"):
    cmp(f"TC 168,3x7 {k}", Cm[k] * (1e-6 if k == "A" else 1e-12), C.A if k == "A" else C.props[k], 1e-6)

ue = s.secao_Ue(0.150, 0.060, 0.020, 0.002)
uem = sec.Ue_yu(150, 60, 20, 2)
for k, p in (("A", 2), ("Ix", 4), ("Iy", 4), ("J", 4), ("Cw", 6), ("x0", 1)):
    obt = ue.A if k == "A" else abs(ue.props[k])
    cmp(f"Ue 150x60x20x2 {k} (Yu)", uem[k] * 1e-3 ** p, obt, 0.005)
up = s.secao_U_pf(0.150, 0.050, 0.002)
upm = sec.U_yu(150, 50, 2)
for k, p in (("A", 2), ("Ix", 4), ("Iy", 4), ("Cw", 6), ("x0", 1)):
    obt = up.A if k == "A" else abs(up.props[k])
    cmp(f"U 150x50x2 {k} (Yu)", upm[k] * 1e-3 ** p, obt, 0.005)

# ---------------------------------------------------------------- compressão / Q
r = n.compressao(w, a36, 3, 3, 3)
cmp("W200 KL=3 NcRd", 474.3, r["NcRd"])
lam_I = s.secao_I(0.300, 0.300, 0.008, 0.008, soldado=False)       # bf/2tf = 18,75 (grupo 4)
cmp("Q grupo 4 inelástico b/t=18,75 (laminado)", mem.Qs_grupo4(18.75, fy), n.fator_Q(lam_I, a36)[1])
lam_E = s.secao_I(0.300, 0.300, 0.008, 0.005, soldado=False)       # bf/2tf = 30 (> 1,03√(E/fy) = 29,1)
cmp("Q grupo 4 elástico b/t=30 (laminado)", mem.Qs_grupo4(30.0, fy), n.fator_Q(lam_E, a36)[1])
sol_E = s.secao_I(0.500, 0.400, 0.0063, 0.008)                     # bf/2tf = 25; kc = 0,456
Qs5, kc = mem.Qs_grupo5(25.0, fy, 484 / 6.3)
cmp("Q grupo 5 elástico b/t=25 (soldado)", Qs5, n.fator_Q(sol_E, a36)[1])

# monossimétrica: U de mesas paralelas (eixo x de simetria) → min(Ney, Nexz) (E.1.3)
pu = U.props
A_, Ix_, Iy_ = U.A * 1e4, pu["Ix"] * 1e8, pu["Iy"] * 1e8
x0_ = pu["x0"] * 100
r0 = math.sqrt(Ix_ / A_ + Iy_ / A_ + x0_ ** 2)
for KL in (2.0, 4.0):
    Nex, Ney = mem.Ne_flexao(Ix_, KL * 100), mem.Ne_flexao(Iy_, KL * 100)
    Nz = mem.Nez(pu["Cw"] * 1e12, pu["J"] * 1e8, r0, KL * 100)
    Nexz = mem.Ne_mono(Nex, Nz, x0_, r0)
    cmp(f"U200x75 KL={KL} Ne = min(Ney, Nexz) (E.1.3)", min(Ney, Nexz), n.forca_flambagem(U, a36, KL, KL, KL)["Ne"])

# cantoneira simples: flambagem por flexão em torno do eixo principal de menor inércia (E.1.4 com eixos principais)
pl = L.props
Imin = Lm["Imin"] * 1e-4
for KL in (1.0, 2.0):
    cmp(f"L100x10 KL={KL} Ne ≤ π²E·Imin/KL² (eixo principal)",
        mem.Ne_flexao(Imin, KL * 100), n.forca_flambagem(L, a36, KL, KL, KL)["Ne"])

# ---------------------------------------------------------------- flexão
wc = {k: wm[k] / 10 ** p for k, p in (("Iy", 4), ("Wx", 3), ("Zx", 3), ("Cw", 6), ("J_h0", 4), ("ry", 1))}
for Lb, cb in ((1.0, 1), (3.0, 1), (3.0, 1.3), (6.0, 1), (6.0, 2.0)):
    Mn = mem.flt(Lb * 100, wc["ry"], wc["Iy"], wc["J_h0"], wc["Cw"], wc["Wx"], wc["Zx"], fy, cb)[0]
    cmp(f"W200 FLT Lb={Lb} Cb={cb} MRd", Mn / 1.1 / 100, n.flexao_x(w, a36, Lb, cb)["MRd"])
# FLM elástica laminado: bf/2tf = 30 > λr = 28,06
Ie = s.secao_I(0.300, 0.300, 0.010, 0.005, soldado=False)
pe = Ie.props
Mn = mem.flm(30.0, pe["Wx"] * 1e6, pe["Zx"] * 1e6, fy)[0]
cmp("I 300x300x10x5 lam. FLM elástica Mn", Mn / 100, n.flexao_x(Ie, a36, 0.01)["Mn_FLM"])
# FLM elástica soldado: I 500x400x6,3x8 (b/t = 25, kc = 0,456 → λr = 21,70)
ps = sol_E.props
Mn = mem.flm(25.0, ps["Wx"] * 1e6, ps["Zx"] * 1e6, fy, True, kc)[0]
cmp("I 500x400x6,3x8 sold. FLM elástica Mn", Mn / 100, n.flexao_x(sol_E, a36, 0.01)["Mn_FLM"])
# flexão em y: Mpl = Zy·fy limitado a 1,5·Wy·fy (5.4.2.2)
cmp("W200 flexão y MRd = min(Zy, 1,5Wy)·fy/γ", min(wm["Zy"], 1.5 * wm["Wy"]) * 1e-3 * fy / 1.1 / 100,
    n.flexao_y(w, a36)["MRd"])
# FLM em y com mesa esbelta (laminado b/t = 18,75: inelástica) — Mr = 0,7·fy·Wy (tabela G.1)
pi_ = lam_I.props
Mn = mem.flm(18.75, pi_["Wy"] * 1e6, pi_["Zy"] * 1e6, fy)[0]  # G.2: Mpl = Z·fy; o limite 1,5W·fy vale para MRd
cmp("I 300x300x8x8 lam. flexão y FLM inelástica Mn", Mn / 100, n.flexao_y(lam_I, a36)["Mn_FLM"])

# ---------------------------------------------------------------- cortante
Iv = s.secao_I(0.800, 0.300, 0.005, 0.016)
V = mem.cortante((800 - 32) / 5, 80 * 0.5, fy)[0]
cmp("I 800x300x5x16 cortante elástico VRd", V, n.cortante_y(Iv, a36)["VRd"])
cmp("W200 cortante // mesas (5.4.3.5)", mem.cortante(133 / 16.8, 2 * 13.3 * 0.84, fy, 1.2)[0],
    n.cortante_x(w, a36)["VRd"])

# ---------------------------------------------------------------- interação / tração / esbeltez
NcRd = n.compressao(w, a36, 3, 3, 3)["NcRd"]
MxRd = n.flexao_x(w, a36, 3)["MRd"]
N02 = 0.2 * NcRd
cmp("interação na fronteira N/NRd = 0,2 (ramo a)", mem.interacao(0.2, 20 / MxRd, 0),
    n.verificar_barra_aco(w, a36, dict(N=-N02, Mx=20), 3, 3, 3)["detalhes"]["taxas"]["interacao"])
NtRd = n.tracao(w, a36)["NtRd"]
cmp("interação com tração (NRd = Nt,Rd)", mem.interacao(300 / NtRd, 20 / MxRd, 0),
    n.verificar_barra_aco(w, a36, dict(N=300, Mx=20), 3, 3, 3)["detalhes"]["taxas"]["interacao"])
rt = n.verificar_barra_aco(w, a36, dict(N=10), 8, 8, 3)
cmp("esbeltez de barra tracionada L/r = 258 (limite 300, 5.2.8)", (8 / w.props["ry"]) / 300,
    rt["detalhes"]["taxas"].get("esbeltez", float("nan")))

# ---------------------------------------------------------------- Cb
xs = [6.0 * k / 12 for k in range(13)]
cmp("Cb biengastada uniforme (MA=qL²/96, MB=qL²/24)", mem.Cb(1 / 12, 1 / 96, 1 / 24, 1 / 96),
    n.cb_de_diagrama(xs, [10 * (6 * x / 2 - x * x / 2 - 36 / 12) for x in xs]))
pico = [1.0 if k in (0, 12) else 0.0 for k in range(13)]
cmp("Cb ≤ 3,0 (5.4.2.3)", 3.0, n.cb_de_diagrama(xs, pico))
cmp("Cb balanço uniforme: norma exige 1,0 (5.4.2.3)", 1.0, n.cb_de_diagrama(xs, [-5 * (6 - x) ** 2 for x in xs]))

# ---------------------------------------------------------------- saída
if __name__ == "__main__":
    w1 = max(len(c) for c, *_ in LIN)
    for c, e_, o, d, st in LIN:
        print(f"{st:3} {c:<{w1}}  esp={e_:.6g}  obt={o:.6g}  dif={d * 100:.3f}%")
    print(f"{sum(1 for *_, st in LIN if st == 'DIF')} diferenças de {len(LIN)} comparações")
