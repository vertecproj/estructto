"""NBR 14762:2010 — barras de perfis formados a frio U simples e U enrijecido (Ue).

Flambagem local pelo método da largura efetiva (MLE, 9.2), elemento a elemento, pelo "método linear"
(linha média, cantos em arco de raio ri + t/2 sempre efetivos). O método da seção efetiva (MSE) foi
removido: os kl antigos estavam digitados de memória (o de flexão estava errado) e a tabela de flexão
não foi lida em fonte pública (ESPEC, decisão de 2026-10-20).

Unidades kN, m, kN/m². Seção de secoes.secao_Ue / secao_U_pf: x = eixo de simetria (maior inércia),
y = menor. Raio interno de dobra: props["ri"] (ausente → 0, a favor da segurança: partes planas maiores).

Fontes das fórmulas (conferidas com exemplos resolvidos, ver testes/test_pff.py):
- 9.2.2 e 9.2.3 (largura efetiva, enrijecedor de borda): V. P. Silva, "Perfis formados a frio conforme
  revisão da ABNT NBR 14762", Instituto de Engenharia; T. B. Soares, monografia UFMG 2018 (tabela 5).
- tabelas 11 e 14 (dispensa da distorcional): V. P. Silva e L. B. Santos (UFAL, eduCAPES 572684/572685).
- 9.6.2 (tração, γ 1,10 / 1,35 / 1,65): L. B. Santos, notas de aula UFAL (eduCAPES 572683).
- 9.7 / 9.8 (γ = 1,20 compressão; 1,10 flexão e cortante; χ, χFLT, Me): Silva, Pierin & Silva,
  manual CBCA 2014; 9.8.3 (cortante) e 9.8.4 (M + V): J. M. Maia e monografia UFRGS 10183/159605;
  9.9 (interação linear): L. B. Santos, UFAL (eduCAPES 572686).
"""
from __future__ import annotations

import math

from .nbr8800 import _json, _validar_entrada, torcao, von_mises_aberta
from .nbr8800 import forca_flambagem  # NBR 14762 9.7.2.1 = mesmas expressões de Nex, Ney, Nez, Nexz
from .nbr8800 import _componente  # perfil isolado de uma barra composta (secoes.secao_dupla)

EDICAO = "NBR 14762:2010"
G_ESC, G_RUP0, G_RUP = 1.10, 1.35, 1.65  # 9.6.2 a), b), c)
G_COMP = 1.20                            # 9.7.2
G_FLEX = G_CORT = 1.10                   # 9.8.2, 9.8.3
NU = 0.3                                 # coeficiente de Poisson do aço (NBR 14762)

# Expressão simplificada de σdist (Fcrd) do AISI para C sem restrição rotacional da mesa: AISI S100-2007
# C4.2(a) (compressão) e C3.1.4(a) (flexão); em 2010 passou ao Commentary como limite inferior para
# pré-dimensionamento — AISI S100-16 (reaf. 2020) Commentary, eq. C-2.3.1.3-1/-4 e C-2.3.3.3-1/-4 (lido em
# 2026-10-20). É a usada pelo CYPE. Aceita porque a NBR 14762 9.7.3/9.8.2.3 deixa Ndist/Mdist à análise de
# estabilidade elástica e esta expressão é um limite inferior dela (conservadora). α = β = 1 (a favor).
_AISI_DIST = "AISI S100-16 Commentary C-2.3.1.3 / C-2.3.3.3 (S100-2007 C4.2(a) / C3.1.4(a)), α = β = 1"
_LIM_AISI = (("ho/t", 50, 200), ("bo/t", 25, 100), ("ho/bo", 2, 8), ("D/bo", 0.04, 0.5))

_L = "método da largura efetiva (9.2), método linear com cantos efetivos"
COBERTURA = [
    dict(norma="NBR 14762", edicao="2010", item="9.2", verificacao="largura efetiva de elementos AA e AL",
         limitacao="AL sob gradiente usa k = 0,43 com a tensão máxima (a favor da segurança); MSE não "
                   "implementado (tabelas 9/10 de compressão achadas em notas de aula; a de flexão não legível) "
                   "— um só método para compressão, flexão em x e em y"),
    dict(norma="NBR 14762", edicao="2010", item="9.2.3", verificacao="mesa com enrijecedor de borda simples (90°)",
         limitacao="enrijecedor sempre com k = 0,43 e tensão máxima; D/b > 0,8 → mesa tratada como AL (k = 0,43)"),
    dict(norma="NBR 14762", edicao="2010", item="9.6.2", verificacao="tração: escoamento, ruptura fora e na ligação (Ct)",
         limitacao="An e Ct informados pelo usuário (padrão An = A, Ct = 1); limite de esbeltez 300 não verificado"),
    dict(norma="NBR 14762", edicao="2010", item="9.7.2", verificacao="compressão: flexão, torção e flexo-torção (χ), Aef",
         limitacao=_L),
    dict(norma="NBR 14762", edicao="2010", item="9.7.3", verificacao="flambagem distorcional na compressão",
         limitacao="Ue: dispensa pela tabela 11; senão χdist com Ndist informado (análise de estabilidade elástica) "
                   "ou pela expressão simplificada do AISI (" + _AISI_DIST + "; limites 50 ≤ ho/t ≤ 200, "
                   "25 ≤ bo/t ≤ 100, 6,25 < D/t ≤ 50, 2 ≤ ho/bo ≤ 8, 0,04 ≤ D/bo ≤ 0,5; enrijecedor a 90°); fora "
                   "deles sem Ndist → reprova (nao_verificado)"),
    dict(norma="NBR 14762", edicao="2010", item="9.7.4", verificacao="esbeltez KL/r ≤ 200", limitacao=""),
    dict(norma="NBR 14762", edicao="2010", item="9.8.2.1", verificacao="flexão: início de escoamento da seção efetiva (x e y)",
         limitacao=_L + "; My: menor dos dois sentidos"),
    dict(norma="NBR 14762", edicao="2010", item="9.8.2.2", verificacao="flexão em x: FLT com Cb (Me = Cb·r0·√(Ney·Nez))",
         limitacao="FLT em y (eixo de menor inércia) não se aplica e não é calculada"),
    dict(norma="NBR 14762", edicao="2010", item="9.8.2.3", verificacao="flambagem distorcional na flexão",
         limitacao="Ue, flexão em x: dispensa pela tabela 14; senão χdist com Mdist informado ou pela expressão "
                   "simplificada do AISI (mesmos limites da compressão; Mdist = Wx·σdist); fora deles sem Mdist → "
                   "reprova; flexão em y (Ue, My ≠ 0, qualquer sentido): Mdist,y informado ou dispensa pela tabela "
                   "11 e σdist da compressão (AISI, mesmos limites) com Mdist,y = Wy,c·σdist (Wy,c do lado dos "
                   "enrijecedores) — extensão a favor (a norma não tem tabela/expressão da distorcional em y); fora → reprova"),
    dict(norma="NBR 14762", edicao="2010", item="9.8.3", verificacao="força cortante paralela à alma (kv = 5)",
         limitacao="alma sem enrijecedores transversais; Vx (paralela às mesas) não verificado (aviso)"),
    dict(norma="NBR 14762", edicao="2010", item="— (critério do CYPE)", verificacao="torção: τ = T·t/J ≤ 0,6fy/γ e Von Mises",
         limitacao="a NBR 14762 não traz verificação de torção; adotados o critério do CYPE (WT = J/t, γ = 1,10) e, "
                   "com T > 0,2·TRd e outros esforços, Von Mises por envelope com empenamento majorado (ver NBR 8800)"),
    dict(norma="NBR 14762", edicao="2010", item="9.8.4", verificacao="momento fletor e cortante combinados",
         limitacao="usa MxRd com FLT (mais conservador que o momento de início de escoamento)"),
    dict(norma="NBR 14762", edicao="2010", item="9.9", verificacao="flexo-compressão e flexo-tração (interação linear)",
         limitacao="esforços de cálculo já de 2ª ordem (análise global)"),
]

# NBR 14762:2010 tabelas 11 (compressão) e 14 (flexão em x): D/bw mínimo para dispensar a distorcional,
# seções Ue; linhas bf/bw, colunas bw/t; interpolação linear. Fontes: V. P. Silva (Instituto de Engenharia)
# e L. B. Santos (UFAL, eduCAPES 572684/572685); única divergência (tabela 11, bf/bw = 1,4, bw/t = 250:
# 0,06 × 0,07) → adotado o maior, a favor da segurança.
_BWT = (50, 100, 125, 200, 250)
_BFBW = (0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0)
_DIST = {
    "compressao": ((0.08, 0.04, 0.04, 0.03, 0.02), (0.15, 0.06, 0.06, 0.04, 0.03), (0.22, 0.10, 0.08, 0.06, 0.05),
                   (0.27, 0.12, 0.10, 0.07, 0.06), (0.27, 0.15, 0.12, 0.07, 0.06), (0.27, 0.15, 0.12, 0.08, 0.07),
                   (0.27, 0.15, 0.12, 0.08, 0.07), (0.27, 0.15, 0.12, 0.08, 0.07), (0.27, 0.15, 0.12, 0.08, 0.07)),
    "flexao": ((0.25, 0.12, 0.10, 0.06, 0.05), (0.25, 0.12, 0.10, 0.06, 0.05), (0.22, 0.12, 0.09, 0.06, 0.05),
               (0.22, 0.11, 0.09, 0.06, 0.05), (0.20, 0.11, 0.09, 0.06, 0.05), (0.20, 0.10, 0.09, 0.06, 0.05),
               (0.20, 0.10, 0.09, 0.06, 0.05), (0.19, 0.10, 0.09, 0.06, 0.05), (0.19, 0.10, 0.09, 0.06, 0.05)),
}


def _interp(xs, ys, x):
    i = max(1, min(len(xs) - 1, next((k for k, v in enumerate(xs) if v >= x), len(xs) - 1)))
    return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1])


def dispensa_distorcional(sec, caso):
    """Tabela 11 (caso="compressao") ou 14 ("flexao"), dimensões nominais. dispensada: True/False;
    None para U simples (sem enrijecedor). Fora da faixa da tabela → não dispensa (a favor da segurança)."""
    p = sec.props
    if p["tipo"] != "Ue":
        return dict(dispensada=None, item="9.7.3" if caso == "compressao" else "9.8.2.3")
    bw, bf, D, t = p["h"], p["b"], p["D"], p["t"]
    eta, bwt, mu = bf / bw, bw / t, D / bw
    item = "9.7.3 (tabela 11)" if caso == "compressao" else "9.8.2.3 (tabela 14)"
    tol = 1e-9  # bordas da tabela em ponto flutuante (0,04/0,1 = 0,39999999999999997)
    if not (_BFBW[0] - tol <= eta <= _BFBW[-1] + tol and _BWT[0] - tol <= bwt <= _BWT[-1] + tol):
        return dict(dispensada=False, bf_bw=eta, bw_t=bwt, D_bw=mu, D_bw_min=None, item=item,
                    motivo="fora da faixa da tabela")
    eta, bwt = min(max(eta, _BFBW[0]), _BFBW[-1]), min(max(bwt, _BWT[0]), _BWT[-1])
    linhas = [_interp(_BWT, lin, bwt) for lin in _DIST[caso]]
    dmin = _interp(_BFBW, linhas, eta)
    return dict(dispensada=mu >= dmin, bf_bw=eta, bw_t=bwt, D_bw=mu, D_bw_min=dmin, item=item)


def distorcional(sec, mat, caso, valor=None):
    """Seção Ue. caso="compressao" (9.7.3): Nc,Rd,dist = χdist·A·fy/1,20, λdist = √(A·fy/Ndist), χdist = 1
    (λ ≤ 0,561) ou (1 − 0,25/λ^1,2)/λ^1,2. caso="flexao" (9.8.2.3, em x): MRd,dist = χdist·W·fy/1,10,
    λdist = √(W·fy/Mdist), χdist = 1 (λ ≤ 0,673) ou (1 − 0,22/λ)/λ. caso="flexao_y": idem com W = Wy,c (fibra
    extrema do lado dos enrijecedores) e σdist da compressão (limite inferior: mesa com gradiente e alma
    tracionada). valor: Ndist (kN) ou Mdist (kN·m) de
    análise de estabilidade elástica; None → σdist pela expressão simplificada do AISI (ver _AISI_DIST) com
    dimensões externas: σ = kd·π²E/(12(1 − ν²))·(t/bo)², kd = 0,1x^1,4 ∈ [0,05; 8] (compressão) ou 0,6x^0,7
    ∈ [0,5; 8] (flexão), x = bo·D/(ho·t). Fora dos limites → dict com "nao_calculado" (motivo)."""
    p, E, fy = sec.props, mat.E, mat.fy
    comp, em_y = caso == "compressao", caso == "flexao_y"
    # Ndist = A·σdist; Mdist = Wc·σdist (seção bruta, fibra comprimida)
    ref = sec.A if comp else (p["Iy"] / (p["b"] - p["t"] / 2 - p["xg"]) if em_y else p["Wx"])
    r = dict(item="9.7.3" if comp else ("9.8.2.3 (flexão em y)" if em_y else "9.8.2.3"))
    if valor is None:
        ho, bo, D, t = p["h"], p["b"], p["D"], p["t"]
        rel = {"ho/t": ho / t, "bo/t": bo / t, "ho/bo": ho / bo, "D/bo": D / bo}
        fora = [f"{k} = {rel[k]:.3g} fora de [{a:g}; {b:g}]" for k, a, b in _LIM_AISI
                if not a - 1e-9 <= rel[k] <= b + 1e-9]
        if not 6.25 < D / t <= 50 + 1e-9:
            fora.append(f"D/t = {D / t:.3g} fora de (6,25; 50]")
        if fora:
            r["nao_calculado"] = ("expressão simplificada do AISI inaplicável (" + "; ".join(fora) + "): informe "
                                  + ("Ndist" if comp else "Mdist") + " de análise de estabilidade elástica")
            return r
        x = bo * D / (ho * t)
        kd = min(max(0.1 * x ** 1.4, 0.05), 8.0) if comp or em_y else min(max(0.6 * x ** 0.7, 0.5), 8.0)
        sd = kd * math.pi ** 2 * E / (12 * (1 - NU ** 2)) * (t / bo) ** 2
        valor = ref * sd
        r.update(kd=kd, sigma_dist=sd, fonte=_AISI_DIST)
    else:
        r["fonte"] = "informado (análise de estabilidade elástica)"
    lam = math.sqrt(ref * fy / valor)
    if comp:
        chi = 1.0 if lam <= 0.561 else (1 - 0.25 / lam ** 1.2) / lam ** 1.2
        r.update(Ndist=valor, NRd_dist=chi * sec.A * fy / G_COMP)
    else:
        chi = 1.0 if lam <= 0.673 else (1 - 0.22 / lam) / lam
        r.update(Mdist=valor, MRd_dist=chi * ref * fy / G_FLEX, W=ref)
    r.update(lambda_dist=lam, chi_dist=chi)
    return r


# ---------------------------------------------------------------- 9.2 largura efetiva

def largura_efetiva(b, t, k, sigma, E):
    """9.2.2: (bef, λp). bef = b para λp ≤ 0,673."""
    lp = (b / t) / (0.95 * math.sqrt(k * E / sigma))
    return (b if lp <= 0.673 else min(b, b * (1 - 0.22 / lp) / lp)), lp


def _aa(b, t, E, sa, sb):
    """Elemento AA (tabela 5), tensões nas bordas a e b (compressão > 0). Retorna (trechos efetivos em
    coordenada s ∈ [0, b] a partir da borda a, detalhe)."""
    smax = max(sa, sb)
    if smax <= 0:
        return [(0.0, b)], dict(b=b, bef=b)
    psi = min(sa, sb) / smax
    k = 4.0 if psi >= 1 else 4 + 2 * (1 - psi) ** 3 + 2 * (1 - psi)
    bef, lp = largura_efetiva(b, t, k, smax, E)
    bc = b if psi >= 0 else b / (1 - psi)            # largura comprimida
    bef1 = bef / (3 - psi)                            # junto à borda mais comprimida
    bef2 = bef - bef1 if psi > -0.236 else bef / 2   # junto à outra borda / à linha neutra
    det = dict(b=b, k=k, psi=psi, lambda_p=lp, bef=b if bef1 + bef2 >= bc else b - (bc - bef1 - bef2))
    if bef1 + bef2 >= bc:
        return [(0.0, b)], det
    trechos = [(0.0, bef1), (bc - bef2, b)]
    return (trechos if sa >= sb else [(b - s1, b - s0) for s0, s1 in reversed(trechos)]), det


def _al(b, t, E, sa, sb, fator=1.0):
    """Elemento AL apoiado em a, livre em b. ponytail: k = 0,43 com a tensão máxima e largura total
    (k mínimo da tabela 6 → a favor da segurança). Parte inefetiva (1 − ρ)·bc na ponta livre, ou no fim
    da zona comprimida se a ponta estiver tracionada. fator = Is/Ia do enrijecedor (9.2.3)."""
    smax = max(sa, sb)
    if smax <= 0:
        return [(0.0, b)], dict(b=b, bef=b)
    bef, lp = largura_efetiva(b, t, 0.43, smax, E)
    rho = bef / b * fator
    if sb >= 0:  # ponta livre comprimida
        bc = b if sa >= 0 else b * sb / (sb - sa)
        ef = b - (1 - rho) * bc
        return [(0.0, ef)], dict(b=b, k=0.43, lambda_p=lp, bef=ef)
    bc = b * sa / (sa - sb)
    return [(0.0, rho * bc), (bc, b)], dict(b=b, k=0.43, lambda_p=lp, bef=b - (1 - rho) * bc)


def mesa_enrijecida(b, d, D, t, sigma, sigma_enr, E):
    """9.2.3: mesa uniformemente comprimida com enrijecedor de borda simples a 90°.
    b, d: larguras planas da mesa e do enrijecedor; D: dimensão nominal do enrijecedor; σ da mesa."""
    lp0 = (b / t) / (0.623 * math.sqrt(E / sigma))
    def_ = largura_efetiva(d, t, 0.43, sigma_enr, E)[0]
    if lp0 <= 0.673:
        return dict(lambda_p0=lp0, k=4.0, bef=b, bef_web=b, bef_enr=0.0, ds=def_, Is_Ia=1.0, aviso="")
    Ia = min(399 * t ** 4 * (0.487 * lp0 - 0.328) ** 3, t ** 4 * (56 * lp0 + 5))
    Is = t * d ** 3 / 12
    r = min(Is / Ia, 1.0)
    n = max(0.582 - 0.122 * lp0, 1 / 3)
    aviso = ""
    if D / b <= 0.25:
        k = min(3.57 * r ** n + 0.43, 4.0)
    elif D / b <= 0.8:
        k = min((4.82 - 5 * D / b) * r ** n + 0.43, 4.0)
    else:
        k, aviso = 0.43, "9.2.3: D/b > 0,8 fora do escopo; mesa tratada como AL (k = 0,43)"
    bef, lp = largura_efetiva(b, t, k, sigma, E)
    bef2 = r * bef / 2  # junto ao enrijecedor
    return dict(lambda_p0=lp0, Ia=Ia, Is=Is, Is_Ia=r, n=n, k=k, lambda_p=lp, bef=bef,
                bef_web=bef - bef2, bef_enr=bef2, ds=r * def_, aviso=aviso)


# ---------------------------------------------------------------- modelo linear da seção

def _modelo(p):
    """Elementos planos e cantos pela linha média, coordenadas com a alma em x = 0.
    Retorna (planos, cantos): planos = [(nome, P, Q)], P = borda apoiada (alma: inferior)."""
    t, ri = p["t"], p.get("ri", 0.0)
    rm, ue = ri + t / 2, p["tipo"] == "Ue"
    ym = (p["h"] - t) / 2
    xb = p["b"] - t if ue else p["b"] - t / 2
    planos = [("alma", (0.0, -ym + rm), (0.0, ym - rm))]
    cantos = []

    def arco(cx, cy, a0, a1, n=6):
        q = [(cx + rm * math.cos(a0 + (a1 - a0) * i / n), cy + rm * math.sin(a0 + (a1 - a0) * i / n))
             for i in range(n + 1)]
        cantos.extend(zip(q[:-1], q[1:]))
    for s in (1, -1):
        planos.append(("mesa", (rm, s * ym), (xb - rm if ue else xb, s * ym)))
        arco(rm, s * (ym - rm), math.pi, math.pi / 2 if s > 0 else 1.5 * math.pi)
        if ue:
            planos.append(("enrijecedor", (xb, s * (ym - rm)), (xb, s * (ym - p["D"] + t / 2))))
            arco(xb - rm, s * (ym - rm), math.pi / 2 if s > 0 else 1.5 * math.pi, 0 if s > 0 else 2 * math.pi)
    return planos, cantos


def _props(segs, t, eixo, c0=None):
    """Área, centroide e inércia (em torno de eixo 'x' → coordenada y; 'y' → x) de segmentos retos."""
    j = 1 if eixo == "x" else 0
    L = [math.dist(a, b) for a, b in segs]
    A = t * sum(L)
    c = c0 if c0 is not None else t * sum(l * (a[j] + b[j]) / 2 for l, (a, b) in zip(L, segs)) / A
    I = t * sum(l * (((a[j] + b[j]) / 2 - c) ** 2 + (b[j] - a[j]) ** 2 / 12) for l, (a, b) in zip(L, segs))
    return A, c, I


def _efetivos(p, E, sig):
    """Segmentos efetivos para o campo de tensões sig(x, y) (compressão > 0). Retorna (segs, detalhes, avisos)."""
    t = p["t"]
    planos, cantos = _modelo(p)
    segs, det, avisos = list(cantos), {}, []

    def corta(P, Q, trechos):
        b = math.dist(P, Q)
        for s0, s1 in trechos:
            if s1 - s0 > 1e-12:
                segs.append(tuple((P[0] + (Q[0] - P[0]) * s / b, P[1] + (Q[1] - P[1]) * s / b) for s in (s0, s1)))
    i = 0
    while i < len(planos):
        nome, P, Q = planos[i]
        b = math.dist(P, Q)
        sP, sQ = sig(*P), sig(*Q)
        if nome == "mesa" and p["tipo"] == "Ue":
            _, E1, E2 = planos[i + 1]
            d = math.dist(E1, E2)
            s1, s2 = sig(*E1), sig(*E2)
            if sQ > 0:  # enrijecedor comprimido → 9.2.3 com a tensão máxima da mesa
                m = mesa_enrijecida(b, d, p["D"], t, max(sP, sQ), max(s1, s2), E)
                if m["aviso"]:
                    avisos.append(m["aviso"])
                corta(P, Q, [(0.0, b)] if m["bef"] >= b else [(0.0, m["bef_web"]), (b - m["bef_enr"], b)])
                tr, de = _al(d, t, E, s1, s2, fator=m["Is_Ia"] if m["lambda_p0"] > 0.673 else 1.0)
                corta(E1, E2, tr)
                det[f"mesa{len(det)}"] = m
                det[f"enrijecedor{len(det)}"] = de
            else:       # enrijecedor tracionado: mesa AA sob gradiente
                tr, de = _aa(b, t, E, sP, sQ)
                corta(P, Q, tr)
                det[f"mesa{len(det)}"] = de
                tr, de = _al(d, t, E, s1, s2)
                corta(E1, E2, tr)
                det[f"enrijecedor{len(det)}"] = de
            i += 2
            continue
        tr, de = (_aa if nome == "alma" else _al)(b, t, E, sP, sQ)
        corta(P, Q, tr)
        det[f"{nome}{len(det)}"] = de
        i += 1
    return segs, det, avisos


def _bruto(p):
    planos, cantos = _modelo(p)
    return [(P, Q) for _, P, Q in planos] + cantos


def _area_efetiva(p, E, sigma):
    """9.7.2: área efetiva sob compressão uniforme σ (modelo linear)."""
    segs, det, av = _efetivos(p, E, lambda x, y: sigma)
    return _props(segs, p["t"], "x")[0], _props(_bruto(p), p["t"], "x")[0], det, av


def _modulo_efetivo(p, E, sigma, eixo, sentido=1, fibra="max"):
    """9.8.2: módulo elástico da seção efetiva com tensão σ na fibra de referência ('max' = a mais
    afastada da LN efetiva; 'comprimida' = fibra extrema comprimida). Iterativo na posição da LN.
    Retorna (Wef_lin, W_bruto_lin, detalhes, avisos)."""
    t, j = p["t"], 1 if eixo == "x" else 0
    bruto = _bruto(p)
    cmax = max(max(a[j], b[j]) for a, b in bruto) + t / 2
    cmin = min(min(a[j], b[j]) for a, b in bruto) - t / 2

    def dists(cn):
        dc, dt = (cmax - cn, cn - cmin) if sentido > 0 else (cn - cmin, cmax - cn)
        return dc, (dc if fibra == "comprimida" else max(dc, dt))
    _, cn, Ig = _props(bruto, t, eixo)
    W_bruto = Ig / dists(cn)[1]
    for _ in range(100):
        dc, dref = dists(cn)
        sig = (lambda x, y, cn=cn, dref=dref: sigma * sentido * ((y, x)[1 - j] - cn) / dref)
        segs, det, av = _efetivos(p, E, sig)
        _, novo, _ = _props(segs, t, eixo)
        if abs(novo - cn) < 1e-9:
            break
        cn = novo
    _, _, Ief = _props(segs, t, eixo, cn)
    return Ief / dists(cn)[1], W_bruto, det, av


# ---------------------------------------------------------------- verificações

def tracao(sec, mat, An=None, Ct=1.0, An0=None):
    """9.6.2: menor entre escoamento da bruta, ruptura fora da ligação (An0) e na ligação (Ct·An)."""
    if not 0 < Ct <= 1.0:
        raise ValueError(f"Ct = {Ct!r}: coeficiente de redução da área líquida deve estar em (0; 1,0] (NBR 14762 9.6.2)")
    A = sec.A
    Nesc = A * mat.fy / G_ESC
    Nr0 = (An0 if An0 is not None else A) * mat.fu / G_RUP0
    Nr = Ct * (An if An is not None else A) * mat.fu / G_RUP
    return dict(NtRd=min(Nesc, Nr0, Nr), Nt_escoamento=Nesc, Nt_ruptura_fora=Nr0, Nt_ruptura_ligacao=Nr,
                Ct=Ct, item="9.6.2")


def compressao(sec, mat, KLx, KLy, KLz, Ndist=None):
    """9.7.2: Nc,Rd = χ·Aef·fy/γ (γ = 1,20), Aef por 9.2 com σ = χ·fy. Distorcional (9.7.3) em
    distorcional.NRd_dist quando a tabela 11 não dispensa (Ndist: ver distorcional)."""
    p, E, fy = sec.props, mat.E, mat.fy
    dupla = p["tipo"] == "dupla"
    composta = _dupla_9_7_5(sec, KLx, KLy) if dupla else None   # NotImplementedError → verificar_pff reprova
    ne = {k: float(v) for k, v in forca_flambagem(sec, mat, KLx, KLy, KLz).items()}
    l0 = math.sqrt(sec.A * fy / ne["Ne"])
    chi = 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2
    # barra composta (W9): χ da flambagem global do conjunto; Aef e distorcional de cada perfil isolado × 2
    um, n = (_componente(sec), 2) if dupla else (sec, 1)
    Aef_lin, A_lin, det, avisos = _area_efetiva(um.props, E, chi * fy)
    Aef = n * um.A * Aef_lin / A_lin  # ponytail: razão do modelo linear aplicada à área bruta da Secao
    dist = dispensa_distorcional(um, "compressao")
    if dist["dispensada"] is False:
        dist.update(distorcional(um, mat, "compressao", Ndist / n if Ndist else None))
        if "NRd_dist" in dist:
            dist["NRd_dist"] *= n
        if "nao_calculado" in dist:
            avisos = avisos + [f"distorcional (compressão, 9.7.3): {dist['nao_calculado']}"]
    esb = max(KLx / p["rx"], KLy / p["ry"])
    r = dict(NcRd=chi * Aef * fy / G_COMP, chi=chi, lambda0=l0, Aef=Aef, A=sec.A, sigma=chi * fy,
             elementos=det, distorcional=dist, esbeltez=esb, esbeltez_ok=esb <= 200,
             avisos=avisos, item="9.7.2", **ne)
    if dupla:
        r.update(composta=composta, item="9.7.2 / 9.7.5")
    return r


def _dupla_9_7_5(sec, KLx, KLy):
    """NBR 14762:2010 9.7.5, barras compostas comprimidas: com presilhas (chapas separadoras), l/r de cada perfil
    entre ligações < 1/2 do índice de esbeltez máximo do conjunto (o mesmo critério da NBR 8800 5.3.4); solda
    contínua sem afastamento dispensa (perfis em contato trabalham juntos). Ligação
    genérica, afastamento sem chapas ou sem a distância entre elas → NotImplementedError (a barra reprova)."""
    c, p = sec.props["composicao"], sec.props
    if c["ligacao"] == "soldada" and c["s"] == 0:
        return dict(item="9.7.5", dispensa="solda contínua: perfis em contato trabalham juntos")
    if c["ligacao"] == "generica":
        raise NotImplementedError("barra composta com ligação genérica entre perfis: sem item na NBR 14762:2010")
    if c["s"] > 0 and not c.get("chapas_espacadoras"):
        raise NotImplementedError(f"barra composta com afastamento {c['s'] * 1e3:g} mm entre perfis sem chapas "
                                  "espaçadoras: fora da NBR 14762:2010 9.7.5")
    l = c.get("l_ligacoes")
    if not l:
        raise NotImplementedError("barra composta sem a distância entre presilhas/chapas (composicao.l_ligacoes): "
                                  "9.7.5 não verificável")
    cp = _componente(sec).props
    ri = min(cp["rx"], cp["ry"])
    conj = max(KLx / p["rx"], KLy / p["ry"])
    li = l / ri
    if li > conj / 2:
        raise NotImplementedError(f"9.7.5: l/r de cada perfil entre ligações = {li:.0f} > 1/2 do KL/r do conjunto "
                                  f"({conj:.0f}/2): aproxime as presilhas")
    return dict(l_ri=li, KL_r_conjunto=conj, item="9.7.5")


def _chi_flt(l0):
    # 9.8.2.2
    return 1.0 if l0 <= 0.6 else (1.11 * (1 - 0.278 * l0 ** 2) if l0 < 1.336 else 1 / l0 ** 2)


def flexao_x(sec, mat, Lb, Cb=1.0, Mdist=None):
    """9.8.2: menor entre início de escoamento da seção efetiva (9.8.2.1) e FLT (9.8.2.2). Distorcional
    (9.8.2.3) em distorcional.MRd_dist quando a tabela 14 não dispensa (Mdist: ver distorcional).
    Barra composta (W9): cada perfil fletido no seu eixo x (comum aos dois) = 2 × perfil isolado, como na NBR 8800."""
    if sec.props["tipo"] == "dupla":
        r = flexao_x(_componente(sec), mat, Lb, Cb, Mdist / 2 if Mdist else None)
        for k in ("MRd", "MRd_esc", "MRd_FLT", "Wef", "Wc_ef"):
            if r.get(k) is not None:
                r[k] *= 2
        if "MRd_dist" in r["distorcional"]:
            r["distorcional"]["MRd_dist"] *= 2
        r["item"] += " (barra composta: 2 × perfil isolado)"
        return r
    p, E, G, fy = sec.props, mat.E, mat.G, mat.fy
    Wl, Wb, det, avisos = _modulo_efetivo(p, E, fy, "x")
    Wef = p["Wx"] * Wl / Wb  # ponytail: razão do modelo linear aplicada ao W bruto da Secao
    r = dict(Wef=Wef, MRd_esc=Wef * fy / G_FLEX, elementos=det, Cb=Cb)
    if Lb > 0:
        Ney = math.pi ** 2 * E * p["Iy"] / Lb ** 2
        Nez = (math.pi ** 2 * E * p["Cw"] / Lb ** 2 + G * p["J"]) / p["r0"] ** 2
        Me = Cb * p["r0"] * math.sqrt(Ney * Nez)  # seção monossimétrica, flexão em torno de x
        l0 = math.sqrt(p["Wx"] * fy / Me)
        chi = _chi_flt(l0)
        Wl, Wb, _, av = _modulo_efetivo(p, E, chi * fy, "x", fibra="comprimida")
        Wcef = p["Wx"] * Wl / Wb
        avisos = avisos + av
        r.update(Me=Me, lambda0=l0, chi_FLT=chi, Wc_ef=Wcef, MRd_FLT=chi * Wcef * fy / G_FLEX)
    else:
        r.update(Me=None, lambda0=0.0, chi_FLT=1.0, MRd_FLT=None)
    dist = dispensa_distorcional(sec, "flexao")
    if dist["dispensada"] is False:
        dist.update(distorcional(sec, mat, "flexao", Mdist))
        if "nao_calculado" in dist:
            avisos = avisos + [f"distorcional (flexão, 9.8.2.3): {dist['nao_calculado']}"]
    flt = r["MRd_FLT"] is not None and r["MRd_FLT"] < r["MRd_esc"]
    r.update(MRd=r["MRd_FLT"] if flt else r["MRd_esc"], criterio="FLT" if flt else "escoamento",
             distorcional=dist, avisos=sorted(set(avisos)), item="9.8.2.2" if flt else "9.8.2.1")
    return r


def flexao_y(sec, mat, Mdist=None):
    """9.8.2.1 em torno de y (menor inércia): menor dos dois sentidos. FLT não se aplica. Ue: distorcional
    (9.8.2.3) com os enrijecedores comprimidos — sentido desconhecido no modelo, aplicada a |My| (a favor);
    Mdist: Mdist,y de análise de estabilidade elástica (opcional; ver distorcional, caso "flexao_y")."""
    if sec.props["tipo"] == "dupla":  # mesma decisão da NBR 8800 (nbr8800.flexao_y): reprova com o motivo
        raise NotImplementedError("flexão de barra composta aberta em torno do eixo de simetria (ação conjunta "
                                  "pelas presilhas): sem item na NBR 14762:2010")
    p, E, fy = sec.props, mat.E, mat.fy
    res = []
    for sentido in (1, -1):
        Wl, Wb, det, av = _modulo_efetivo(p, E, fy, "y", sentido)
        res.append((p["Wy"] * Wl / Wb, sentido, det, av))
    Wef, sentido, det, av = min(res, key=lambda r: r[0])
    avisos = sorted(set(av))
    r = dict(MRd=Wef * fy / G_FLEX, Wef=Wef, sentido_critico=sentido, elementos=det, avisos=avisos,
             item="9.8.2.1")
    if p["tipo"] == "Ue":
        dist = dispensa_distorcional(sec, "compressao")   # tabela 11 (compressão uniforme: mais severa)
        dist["item"] = "9.8.2.3 (flexão em y; dispensa pela tabela 11)"
        if dist["dispensada"] is False:
            dist.update(distorcional(sec, mat, "flexao_y", Mdist))
            if "nao_calculado" in dist:
                avisos.append(f"distorcional (flexão em y, 9.8.2.3): {dist['nao_calculado']}")
        r["distorcional"] = dist
    return r


def cortante(sec, mat, kv=5.0):
    """9.8.3: força cortante paralela à alma, h = altura da parte plana da alma. Barra composta: 2 almas."""
    if sec.props["tipo"] == "dupla":
        r = cortante(_componente(sec), mat, kv)
        r["VRd"] *= 2
        return r
    p, E, fy = sec.props, mat.E, mat.fy
    t = p["t"]
    h = p["h"] - 2 * (t + p.get("ri", 0.0))
    ht, r = h / t, math.sqrt(E * kv / fy)
    if ht <= 1.08 * r:
        V, regime = 0.6 * fy * h * t, "escoamento"
    elif ht <= 1.40 * r:
        V, regime = 0.65 * t ** 2 * math.sqrt(kv * fy * E), "inelastico"
    else:
        V, regime = 0.905 * E * kv * t ** 3 / h, "elastico"
    return dict(VRd=V / G_CORT, h=h, h_t=ht, limites=[1.08 * r, 1.40 * r], kv=kv, regime=regime, item="9.8.3")


_ITENS = {"normal_tracao": "9.6.2", "normal_compressao": "9.7.2", "esbeltez": "9.7.4", "flexao_x": "9.8.2",
          "flexao_y": "9.8.2.1", "cortante": "9.8.3", "momento_cortante": "9.8.4", "interacao": "9.9",
          "distorcional": "9.7.3 / 9.8.2.3 / 9.9", "torcao": "critério do CYPE (τ = T·t/J ≤ 0,6fy/γ)",
          "von_mises": "critério adicional (Von Mises)", "nao_verificado": "ver avisos"}


def verificar_pff(secao, material, esforcos, Lx, Ly, Lz, Lb, Cb=1.0, An=None, Ct=1.0, Ndist=None, Mdist=None,
                  Mdist_y=None):
    """esforcos: dict ou lista de dicts em notação NBR — N (+ tração, − compressão), Mx, My, Vy (paralela
    à alma), Vx, T; valores de cálculo. Lx, Ly, Lz: comprimentos de flambagem; Lb: destravado (FLT).
    Ndist/Mdist/Mdist_y: cargas críticas distorcionais (compressão, flexão em x e em y) de análise de
    estabilidade elástica (opcional; ver distorcional).
    Resistências calculadas uma vez; retorna o ponto determinante (ResultadoNorma, ESPEC §4).
    Distorcional exigida e não calculável → reprova (nao_verificado).
    Comprimento ≤ 0 (Lb < 0), esforço não finito ou Ct fora de (0; 1] → ValueError."""
    pts = [esforcos] if isinstance(esforcos, dict) else list(esforcos)
    if not pts:
        raise ValueError("verificar_pff: lista de esforços vazia")
    _validar_entrada("verificar_pff", pts, dict(Lx=Lx, Ly=Ly, Lz=Lz, Cb=Cb),
                     dict(Lb=Lb, Ndist=Ndist or 0.0, Mdist=Mdist or 0.0, Mdist_y=Mdist_y or 0.0))
    d = dict(tracao=tracao(secao, material, An, Ct), flexao_x=flexao_x(secao, material, Lb, Cb, Mdist or None),
             cortante=cortante(secao, material))
    sem_item = []   # barra composta fora do escopo (9.7.5, flexão em y): NotImplementedError → reprova com o motivo
    if any(e.get("N", 0.0) < 0 for e in pts):
        try:
            d["compressao"] = compressao(secao, material, Lx, Ly, Lz, Ndist or None)
        except NotImplementedError as ex:
            sem_item.append(f"compressão: {ex}")
            d["compressao"] = dict(NcRd=math.inf, esbeltez=0.0, avisos=[], distorcional={})
    Tmax = max(abs(float(e.get("T", 0.0))) for e in pts)
    if Tmax > 0:
        d["torcao"] = torcao(secao, material, gama=G_CORT)
    # composta: My de ruído numérico (≤ 1e-6·Wy·fy) não exige a flexão em y
    tol_my = 1e-6 * secao.props["Wy"] * material.fy if secao.props["tipo"] == "dupla" else 0.0
    if any(abs(float(e.get("My", 0.0))) > tol_my for e in pts):
        try:
            d["flexao_y"] = flexao_y(secao, material, Mdist_y or None)
        except NotImplementedError as ex:
            sem_item.append(f"flexão em y: {ex}")
    MxRd, VRd = d["flexao_x"]["MRd"], d["cortante"]["VRd"]
    MyRd = d["flexao_y"]["MRd"] if "flexao_y" in d else 1.0
    avisos = set(d["flexao_x"]["avisos"]) | set(d.get("compressao", {}).get("avisos", [])) \
        | set(d.get("flexao_y", {}).get("avisos", []))
    dist_c = d.get("compressao", {}).get("distorcional", {})
    dist_f = d["flexao_x"]["distorcional"]
    dist_y = d.get("flexao_y", {}).get("distorcional", {})
    TRd = d["torcao"]["TRd"] if "torcao" in d else None
    pior, esb_ok, sem_dist, sem_vm, vm_pior = None, True, False, False, None
    for i, e in enumerate(pts):
        N, Mx, My, Vy, Vx, T = (float(e.get(k, 0.0)) for k in ("N", "Mx", "My", "Vy", "Vx", "T"))
        if Vx:
            avisos.add("cortante Vx (paralela às mesas) não verificada")
        if N < 0:
            NRd, chave = d["compressao"]["NcRd"], "normal_compressao"
        else:
            NRd, chave = d["tracao"]["NtRd"], "normal_tracao"
        taxas = {chave: abs(N) / NRd, "flexao_x": abs(Mx) / MxRd, "flexao_y": abs(My) / MyRd,
                 "cortante": abs(Vy) / VRd,
                 "momento_cortante": math.hypot(Mx / MxRd, Vy / VRd),            # 9.8.4
                 "interacao": abs(N) / NRd + abs(Mx) / MxRd + abs(My) / MyRd}   # 9.9
        # 9.9 com Nc,Rd e MRd que incluem a distorcional (9.7.1/9.8.2: menor valor), quando exigida
        dc, df, dy = (dist_c if N < 0 else {}), (dist_f if Mx else {}), (dist_y if My else {})
        sem_dist |= "nao_calculado" in dc or "nao_calculado" in df or "nao_calculado" in dy
        if "NRd_dist" in dc or "MRd_dist" in df or "MRd_dist" in dy:
            taxas["distorcional"] = abs(N) / min(NRd, dc.get("NRd_dist", NRd)) \
                + abs(Mx) / min(MxRd, df.get("MRd_dist", MxRd)) + abs(My) / min(MyRd, dy.get("MRd_dist", MyRd))
        if TRd is not None:
            taxas["torcao"] = abs(T) / TRd
            if abs(T) > 0.2 * TRd and any((N, Mx, My, Vy, Vx)):
                try:
                    vm = von_mises_aberta(secao, material, e, Tmax, G_FLEX)
                except KeyError:  # perfil sem dados para as tensões: torção não verificada → reprova
                    vm, sem_vm = None, True
                    avisos.add("Von Mises: perfil sem dados para as tensões — torção não verificada")
                if vm:
                    taxas["von_mises"] = vm["taxa"]
                    if vm_pior is None or vm["taxa"] > vm_pior["taxa"]:
                        vm_pior = dict(vm, indice=i)
        if N < 0:
            taxas["esbeltez"] = d["compressao"]["esbeltez"] / 200               # 9.7.4
        # ESPEC §7.2: taxa_max = pior critério de esforço; esbeltez (9.7.4) só reprova
        crit = max((k for k in taxas if k != "esbeltez"), key=taxas.get)
        if pior is None or taxas[crit] > pior[0]:
            pior = (taxas[crit], crit, i, taxas)
        esb_ok = esb_ok and taxas.get("esbeltez", 0.0) <= 1.0
    taxa, crit, i, taxas = pior
    d["taxas"] = taxas
    d["n_pontos"] = len(pts)
    ok = taxa <= 1.0 and esb_ok
    if vm_pior is not None:
        d["von_mises"] = vm_pior
        avisos.add("torção > 0,2·TRd com outros esforços: Von Mises por envelope (critério adicional, não é da "
                   "NBR 14762), com empenamento majorado (B ≤ T·a, AISC Design Guide 9)")
    if sem_dist or sem_vm or sem_item:  # verificação exigida e não feita: conservador, reprova
        ok, crit = False, "nao_verificado"
    avisos.update(f"{a} — verificação não feita: barra reprovada (nao_verificado)" for a in sem_item)
    if sem_dist:
        avisos.add("flambagem distorcional exigida (tabela 11/14 não dispensa) e não calculada: barra reprovada "
                   "(nao_verificado)")
    return dict(taxa_max=float(taxa), criterio=crit, item=_ITENS[crit], ok=bool(ok), indice=i,
                detalhes=_json(d), avisos=sorted(avisos))
