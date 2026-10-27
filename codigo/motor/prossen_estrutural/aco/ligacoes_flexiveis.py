"""Ligações flexíveis (rotuladas) viga–pilar e viga–viga — NBR 8800:2008, cap. 6 (+ 5.7.3/5.7.4).

Contrato: docs/v3/LIGACOES-CONTRATO.md. Implementação, hipóteses e fontes: docs/v3/ligacoes/IMPL-FLEXIVEIS.md.
Tipos (TIPOS): cantoneira_dupla_alma, chapa_simples_alma, chapa_extremidade_flexivel, apoio_cantoneira,
viga_viga_recorte. Entradas em mm; perfis por nome do catálogo ("W360x51") ou dict {d, bf, tw, tf, r} em mm
(perfis soldados fora do catálogo). Cálculo interno em m, kN e kN/m² com as primitivas de aco/ligacoes.py.

Fontes (acesso 2026-10-20):
  [PF]  W. Pfeil, M. Pfeil, "Estruturas de aço — dimensionamento prático", 8ª ed., LTC: 9.5 e problemas
        9.10.2 (dupla cantoneira) e 9.10.6 (consolo enrijecido).
  [GD]  O. T. Baião Fº, A. C. V. Silva, "Ligações para Estruturas de Aço", 7ª ed., Gerdau, 2018: cap. 3 (LCPP/
        LCSP), 4 (LCHE), 5 (LCHS); resumo em docs/v3/ligacoes/CBCA.md.
  [KF]  G. Kulak, J. Fisher, J. Struik, "Guide to Design Criteria for Bolted and Riveted Joints", 2ª ed., cap. 17
        (efeito de alavanca); resumo em docs/v3/ligacoes/AISC-DG1.md §2.
  [AISC] AISC Steel Construction Manual, 13ª ed.: p. 9-7 (flambagem local da alma recortada) e cap. 7 (centro
        instantâneo de rotação, Crawford–Kulak). Não há item equivalente na NBR 8800:2008.
"""
from __future__ import annotations

import math
import re

from .. import materiais, secoes
from ..modelo import MPa
from . import ligacoes as L
from .nbr8800 import GA1, GA2

MM = 1e-3
E_ACO = 200e6          # kN/m² — NBR 8800 4.5.2.9
RAIO_RECORTE = 12.0    # mm — [GD] 3.1.1 (canto reentrante do recorte)
FOLGA_RECORTE = 10 * MM  # [GD] 2.4.6: recorte = interferência + 10 mm
_INF = float("inf")

COBERTURA = [
    dict(norma="NBR 8800", edicao="2008", item="6.3 / 6.5 / 6.2",
         verificacao="ligações flexíveis: dupla cantoneira, chapa simples, chapa de extremidade, apoio em cantoneira "
                     "(com e sem enrijecedor), viga–viga com recorte",
         limitacao="contato por furo pela menor distância livre (como [GD]); grupo excêntrico da chapa simples pelo "
                   "centro instantâneo (AISC, só V); solda pelo método elástico; efeito de alavanca por Kulak & Fisher "
                   "(cap. 17) com fy/γa1; flambagem local da alma recortada pelo AISC 13ª ed. p. 9-7 (sem item na NBR); "
                   "apoio (alma/mesa do pilar ou viga suporte) só ao contato dos parafusos e ao metal base da solda"),
    dict(norma="NBR 8800", edicao="2008", item="5.7.3 / 5.7.4",
         verificacao="escoamento local e enrugamento da alma da viga no apoio em cantoneira/consolo",
         limitacao="força na extremidade (distância < d/2 e < d)"),
]


# ---------------------------------------------------------------- componentes (funções puras, SI)

def coeficiente_ci(ys, e):
    """Coeficiente C (força / resistência de 1 parafuso) de uma linha vertical de parafusos (ordenadas ys) com
    carga vertical a uma distância horizontal e: centro instantâneo de rotação, R = Rult(1 − e^(−10Δ))^0,55,
    Δmáx = 0,34 pol (AISC 13ª ed., cap. 7, Crawford & Kulak). Reproduz os C do [GD] 5.4 (0,88; 1,75; 2,81;
    3,90; 4,98 para 2–6 parafusos com s = ec = 75 mm)."""
    n = len(ys)
    if e <= 0 or n == 1:
        return float(n)

    def f(r0):
        rs = [math.hypot(r0, y) for y in ys]
        rm = max(rs)
        R = [(1 - math.exp(-10 * 0.34 * ri / rm)) ** 0.55 for ri in rs]
        P = sum(Ri * ri for Ri, ri in zip(R, rs)) / (e + r0)
        return P - sum(Ri * r0 / ri for Ri, ri in zip(R, rs)), P

    a, b = 1e-9 * (1 + e), 1e4 * (1 + e + max(map(abs, ys)))
    fa = f(a)[0]
    for _ in range(200):
        m = (a + b) / 2
        fm = f(m)[0]
        if fa * fm <= 0:
            b = m
        else:
            a, fa = m, fm
    return f((a + b) / 2)[1]   # G5: tabela/centro instantâneo como está (Gerdau n = 2: 0,88, conservador)


def alavanca(T, t, p, b, a, db, dh, fy):
    """Efeito de alavanca por parafuso — [KF] cap. 17 (eqs. 17.10–17.12; AISC-DG1.md §2), com M = p·t²·fy/(4γa1).
    b: eixo do parafuso à face da alma/aba; a: eixo do parafuso à borda livre (a ≤ 1,25b);
    b' = b − db/2, a' = a + db/2, δ = 1 − dh/p; α = (T·b'/M − 1)/δ.
    α < 0: sem alavanca (Q = 0); 0 ≤ α ≤ 1: Q = α·δ·M/a'; α > 1: chapa insuficiente (T > Tmáx = M(1 + δ)/b').
    Retorna dict(B = T + Q, Q, alfa, Tmax, b1, a1, delta)."""
    b1 = b - db / 2
    a1 = min(a, 1.25 * b) + db / 2
    delta = 1 - dh / p
    M = p * t * t * fy / (4 * GA1)
    alfa = (T * b1 / M - 1) / delta if T > 0 else -1.0
    Q = min(alfa, 1.0) * delta * M / a1 if alfa > 0 else 0.0
    return dict(B=T + Q, Q=Q, alfa=alfa, Tmax=M * (1 + delta) / b1, b1=b1, a1=a1, delta=delta)


def escoamento_local_alma(ln, k, tw, fy, extremidade=True):
    """NBR 8800 5.7.3.2: FRd = 1,10(5k + ln)·fy·tw/γa1; força a menos de d da extremidade: (2,5k + ln)."""
    return 1.10 * ((2.5 if extremidade else 5.0) * k + ln) * fy * tw / GA1


def enrugamento_alma(ln, d, tw, tf, fy, extremidade=True):
    """NBR 8800 5.7.4.2: 0,66·tw²[1 + 3(ln/d)(tw/tf)^1,5]√(E·fy·tf/tw)/γa1; a menos de d/2 da extremidade:
    0,33·tw²[1 + 3(ln/d)(tw/tf)^1,5]… (ln/d ≤ 0,2) ou 0,33·tw²[1 + (4ln/d − 0,2)(tw/tf)^1,5]… (ln/d > 0,2)."""
    raiz = math.sqrt(E_ACO * fy * tf / tw)
    r = (tw / tf) ** 1.5
    if not extremidade:
        return 0.66 * tw * tw * (1 + 3 * ln / d * r) * raiz / GA1
    termo = 3 * ln / d if ln / d <= 0.2 else 4 * ln / d - 0.2
    return 0.33 * tw * tw * (1 + termo * r) * raiz / GA1


def comprimento_apoio_necessario(V, d, tw, tf, k, fy):
    """Menor ln com V ≤ min(5.7.3, 5.7.4) na extremidade (bisseção; ∞ se nem ln = 3d basta)."""
    def ok(ln):
        return V <= min(escoamento_local_alma(ln, k, tw, fy), enrugamento_alma(ln, d, tw, tf, fy))
    if ok(0.0):
        return 0.0
    if not ok(3 * d):
        return _INF
    a, b = 0.0, 3 * d
    for _ in range(60):
        m = (a + b) / 2
        a, b = (a, m) if ok(m) else (m, b)
    return b


def solda_c(Lh, Lv):
    """Linha de solda em C (2 horizontais Lh + 1 vertical Lv, [PF] tabela A.11): x̄ a partir da vertical e Ip."""
    Lt = 2 * Lh + Lv
    xb = Lh * Lh / Lt
    Ip = Lv ** 3 / 12 + 2 * Lh * (Lv / 2) ** 2 + 2 * (Lh ** 3 / 12 + Lh * (Lh / 2 - xb) ** 2) + Lv * xb * xb
    return dict(Lt=Lt, xb=xb, Ip=Ip)


def solda_consolo(w, h0):
    """Consolo enrijecido [PF] 9.10.6: cordão horizontal w no topo + 2 verticais h0; ȳ (do topo) e I por garganta
    unitária. Obs.: o livro usa 2·20·1,2² no lugar de 2·20·(10 − 7,59)² e obtém I = 2122t (correto: 2297t)."""
    yb = h0 * h0 / (2 * h0 + w)
    I = w * yb * yb + 2 * (h0 ** 3 / 12 + h0 * (h0 / 2 - yb) ** 2)
    return dict(Lt=w + 2 * h0, yb=yb, I=I)


def recortes_viga_viga(viga, suporte, folga, desnivel=0.0):
    """Recortes da viga apoiada na alma da viga suporte, topos alinhados (desnivel = topo da viga abaixo do topo
    do suporte). [GD] 3.3: altura = tf + 10 mm; comprimento = bf/2 − tw/2 + 10 mm − folga (exemplos E1/E2)."""
    c = suporte["bf"] / 2 - suporte["tw"] / 2 + FOLGA_RECORTE - folga
    dct = max(0.0, suporte["tf"] + FOLGA_RECORTE - desnivel)
    dcb = max(0.0, desnivel + viga["d"] - (suporte["d"] - suporte["tf"] - FOLGA_RECORTE))
    return dict(recorte_sup_h=dct, recorte_sup_c=c if dct > 0 else 0.0,
                recorte_inf_h=dcb, recorte_inf_c=c if dcb > 0 else 0.0)


# ---------------------------------------------------------------- esquema (contrato §Registro)

def _e(rotulo, unidade, padrao, opcoes=None, mn=None, mx=None):
    c = dict(rotulo=rotulo, unidade=unidade, padrao=padrao)
    if opcoes is not None:
        c["opcoes"] = list(opcoes)
    if mn is not None:
        c["min"] = mn
    if mx is not None:
        c["max"] = mx
    return c


_ACOS = list(materiais.ACOS)
_PERFIS = {"perfil_viga", "perfil_suporte", "cantoneira", "cantoneira_superior"}
_INTEIROS = {"n", "n_colunas"}
_APOIOS = ["mesa_pilar", "alma_pilar", "alma_viga"]

_BASE = {
    "perfil_viga": _e("Perfil da viga", "-", None),
    "perfil_suporte": _e("Perfil do apoio", "-", None),
    "apoio": _e("Apoio", "-", "mesa_pilar", _APOIOS),
    "aco_viga": _e("Aço da viga", "-", "A-572 GR.50", _ACOS),
    "aco_suporte": _e("Aço do apoio", "-", "A-572 GR.50", _ACOS),
    "terca_ou_travessa": _e("Terça/travessa de fechamento", "-", False),   # isenta do mínimo de 6.1.5.2
    "abertura_y": _e("Abertura do U (lado y)", "-", 1, [1, -1]),   # G5; ignorado em perfil I
    "x_face": _e("Face do apoio (do nó)", "mm", 0, mn=0, mx=2000),   # G6; 0 = pelo perfil (apoio U: alma fora do eixo)
    "x_max": _e("Comprimento livre na barra", "mm", 0, mn=0, mx=100000),   # G6 r3; 0 = sem limite
}
_PARAF = {
    "d": _e("Diâmetro do parafuso", "mm", 19.05, mn=10, mx=40),
    "tipo_parafuso": _e("Tipo de parafuso", "-", "A325", list(L.PARAFUSOS)),
    "rosca_no_plano": _e("Rosca no plano de corte", "-", True),
    "deformacao_limitada": _e("Deformação limitada", "-", True),
    "borda_laminada": _e("Bordas laminadas", "-", False),
}
_FURACAO = {
    "n": _e("Parafusos por linha", "-", 3, mn=2, mx=20),
    "s": _e("Espaçamento vertical s", "mm", 75, mn=20, mx=500),
    "e_v": _e("Borda vertical", "mm", 40, mn=10, mx=200),
    "dist_topo_furo": _e("1º furo ao topo da viga", "mm", 75, mn=10, mx=2000),
}
_RECORTE = {
    "recorte_sup_h": _e("Recorte sup.: altura", "mm", 0, mn=0, mx=1000),
    "recorte_sup_c": _e("Recorte sup.: comprimento", "mm", 0, mn=0, mx=2000),
    "recorte_inf_h": _e("Recorte inf.: altura", "mm", 0, mn=0, mx=1000),
    "recorte_inf_c": _e("Recorte inf.: comprimento", "mm", 0, mn=0, mx=2000),
}
_SOLDA = {
    "b_solda": _e("Perna do filete", "mm", 5, mn=2, mx=30),
    "eletrodo": _e("Eletrodo", "-", "E70", list(L.ELETRODOS)),
}

_ENT_CD = {**_BASE,
           "cantoneira": _e("Cantoneiras (L b1xb2xt)", "-", "L76x76x6.35"),
           "aco_cantoneira": _e("Aço das cantoneiras", "-", "A-36", _ACOS),
           "ligacao_viga": _e("Ligação à alma da viga", "-", "parafusada", ["parafusada", "soldada"]),
           **_PARAF, **_FURACAO,
           "g_viga": _e("Gabarito na aba da viga", "mm", 45, mn=10, mx=300),
           "g_apoio": _e("Gabarito na aba do apoio", "mm", 45, mn=10, mx=300),
           "folga": _e("Folga viga–apoio", "mm", 10, mn=0, mx=50),
           "excentricidade_apoio": _e("Excentric. nos paraf. apoio", "-", True),
           **_SOLDA, **_RECORTE}

_ENT_CS = {**_BASE,
           "t_chapa": _e("Espessura da chapa", "mm", 6.3, mn=3, mx=50),
           "aco_chapa": _e("Aço da chapa", "-", "A-36", _ACOS),
           **_PARAF, **_FURACAO,
           "n": _e("Parafusos por linha", "-", 3, mn=1, mx=20),   # G5: 1 parafuso (rótula) em U baixo
           "a": _e("Solda ao eixo dos furos", "mm", 75, mn=20, mx=300),
           "e_h": _e("Borda horizontal da chapa", "mm", 40, mn=10, mx=200),
           "folga": _e("Folga viga–apoio", "mm", 10, mn=0, mx=50),
           **_SOLDA, **_RECORTE}

_ENT_CE = {**_BASE,
           "t_chapa": _e("Espessura da chapa", "mm", 6.3, mn=3, mx=50),
           "aco_chapa": _e("Aço da chapa", "-", "A-36", _ACOS),
           "largura_chapa": _e("Largura da chapa", "mm", 200, mn=50, mx=1000),
           "gabarito": _e("Gabarito entre colunas", "mm", 120, mn=30, mx=900),
           **_PARAF,
           "n": _e("Fileiras de parafusos", "-", 2, mn=1, mx=20),
           "s": _e("Espaçamento vertical s", "mm", 75, mn=20, mx=500),
           "e_v": _e("Borda vertical", "mm", 40, mn=10, mx=200),
           "dist_topo_chapa": _e("Topo da chapa ao topo viga", "mm", 30, mn=0, mx=2000),
           **_SOLDA}

_ENT_AP = {**_BASE,
           "assento": _e("Assento", "-", "cantoneira", ["cantoneira", "enrijecido"]),
           "cantoneira": _e("Cantoneira (assento×vert.)", "-", "L102x152x12.7"),
           "aco_cantoneira": _e("Aço da cantoneira/chapas", "-", "A-36", _ACOS),
           "comprimento": _e("Comprimento do assento", "mm", 150, mn=50, mx=1000),
           "folga": _e("Folga viga–apoio", "mm", 10, mn=0, mx=50),
           "ligacao_apoio": _e("Ligação ao apoio", "-", "parafusada", ["parafusada", "soldada"]),
           **_PARAF,
           "n": _e("Parafusos por coluna", "-", 2, mn=1, mx=10),
           "n_colunas": _e("Colunas de parafusos", "-", 2, mn=1, mx=2),
           "g_colunas": _e("Gabarito entre colunas", "mm", 80, mn=30, mx=500),
           "s": _e("Espaçamento vertical s", "mm", 60, mn=20, mx=500),
           "e_v": _e("Borda inferior da aba", "mm", 30, mn=10, mx=200),
           "t_enrijecedor": _e("Espessura do enrijecedor", "mm", 12.5, mn=3, mx=50),
           "b_enrijecedor": _e("Largura do enrijecedor", "mm", 100, mn=30, mx=500),
           "h_enrijecedor": _e("Altura do enrijecedor", "mm", 200, mn=30, mx=1000),
           "t_assento": _e("Espessura chapa do assento", "mm", 12.5, mn=3, mx=50),
           "largura_assento": _e("Largura chapa do assento", "mm", 127, mn=30, mx=600),
           "cantoneira_superior": _e("Cantoneira superior", "-", "L76x76x6.35"),
           **_SOLDA}

_ENT_SD = {**_BASE, **_SOLDA,
           "comprimento_solda": _e("Comprimento do filete", "mm", 0, mn=0, mx=2000),
           "soldar_mesas": _e("Soldar as mesas", "-", "auto", ["auto", "sim", "não"]),
           "folga": _e("Folga viga–apoio", "mm", 2, mn=0, mx=10)}

_ENT_VV = {**{k: v for k, v in _ENT_CD.items() if k not in _RECORTE and k != "apoio"},
           "conector": _e("Conector", "-", "cantoneira_dupla", ["cantoneira_dupla", "chapa_simples"]),
           "desnivel": _e("Topo viga abaixo do suporte", "mm", 0, mn=0, mx=2000),
           "t_chapa": _ENT_CS["t_chapa"], "aco_chapa": _ENT_CS["aco_chapa"],
           "a": _ENT_CS["a"], "e_h": _ENT_CS["e_h"]}


def _ler(tipo, dados):
    ent = TIPOS[tipo]["entradas"]
    dados = dict(dados or {})
    out, erros = {}, []
    for k, c in ent.items():
        v = dados.get(k, c["padrao"])
        if v is None:
            erros.append(f"{k}: obrigatório")
        elif k in _PERFIS:
            out[k] = v
        elif "opcoes" in c:
            m = next((o for o in c["opcoes"] if str(o).upper() == str(v).upper()), None)
            if m is None:
                erros.append(f"{k}: '{v}' inválido ({', '.join(map(str, c['opcoes']))})")
            out[k] = m
        elif isinstance(c["padrao"], bool):
            out[k] = bool(v)
        else:
            try:
                x = float(v)
            except (TypeError, ValueError):
                erros.append(f"{k}: número esperado, recebido {v!r}")
                continue
            if not math.isfinite(x):
                erros.append(f"{k}: número finito esperado")
            elif ("min" in c and x < c["min"]) or ("max" in c and x > c["max"]):
                erros.append(f"{k}: {x:g} fora da faixa [{c.get('min', '-∞')}, {c.get('max', '∞')}] {c['unidade']}")
            elif k in _INTEIROS and x != int(x):
                erros.append(f"{k}: inteiro esperado")
            out[k] = int(x) if k in _INTEIROS else x * MM if c["unidade"] == "mm" else x
    if erros:
        raise ValueError(f"ligação '{tipo}': " + "; ".join(erros))
    return out


def _esforcos(esf, r, d):
    esf = dict(esf or {})
    try:
        V, N = abs(float(esf.get("V", 0.0))), float(esf.get("N", 0.0))
    except (TypeError, ValueError):
        raise ValueError("esforços V e N: números (kN) esperados") from None
    if not (math.isfinite(V) and math.isfinite(N)):
        raise ValueError("esforços V e N: números finitos esperados")
    outros = [k for k in ("Vz", "M", "Mz", "T") if abs(float(esf.get(k, 0.0) or 0.0)) > 1e-9]
    if outros:
        r.avisos.append(f"ligação flexível: {', '.join(outros)} ignorados (rótula; só V e N são transmitidos)")
    # NBR 8800 6.1.5.2: força de cálculo (resultante de V e N) mínima de 45 kN; terças e travessas isentas
    k, aviso = L.fator_minimo_6152(math.hypot(V, N), isenta=d["terca_ou_travessa"])
    if aviso:
        V, N = (V * k, N * k) if math.isfinite(k) else (L.F_MINIMA, 0.0)
        r.avisos.append(aviso)
    if V == 0 and N == 0:
        r.avisos.append("sem esforços: só as disposições construtivas foram verificadas")
    return V, N


def _perfil(v, campo):
    if isinstance(v, dict):
        try:
            p = {k: float(v[k]) * MM for k in ("d", "bf", "tw", "tf")}
            p["r"] = float(v.get("r", 0.0)) * MM
        except (KeyError, TypeError, ValueError):
            raise ValueError(f"{campo}: dict com d, bf, tw, tf (e r opcional), em mm") from None
        p["nome"] = str(v.get("nome", "perfil informado"))
        return p
    try:
        s = secoes.catalogo(str(v))
    except KeyError:
        raise ValueError(f"{campo}: perfil '{v}' não está no catálogo (use o nome ou um dict com d, bf, tw, tf)") \
            from None
    pr = s.props
    if pr.get("tipo") in ("U", "Ue"):   # G5: UPN, U laminado, U e Ue formados a frio (docs/v3/LIGACOES-U.md)
        tw = pr.get("tw") or pr["t"]
        tf = pr.get("tf") or pr["t"]
        # raio da mesa: UPN/U laminado sem r no catálogo → r ≈ tf (DIN 1026: r1 = tf); dobrado: ri + t
        r = pr.get("r") or (pr["ri"] + pr["t"] if pr.get("ri") is not None and "t" in pr else tf)
        if pr.get("d_alma"):   # altura reta tabelada (UPN, mesa inclinada): tf + r = (h − d)/2
            r = (pr["h"] - pr["d_alma"]) / 2 - tf
        xg = pr.get("xg", 0.0)
        return dict(nome=str(v), d=pr["h"], bf=pr["b"], tw=tw, tf=tf, r=r, forma="U", xg=xg,
                    x_cc=abs(pr.get("x0", 0.0)) - xg)   # centro de cisalhamento: x_cc além da face externa da alma
    if pr.get("tipo") != "I":
        raise ValueError(f"{campo}: '{v}' ({pr.get('tipo')}): ligação flexível só com perfil I/H ou U")
    return dict(nome=str(v), d=pr["h"], bf=pr["b"], tw=pr["tw"], tf=pr["tf"], r=pr.get("r", 0.0))


def _cantoneira(nome):
    m = re.fullmatch(r"\s*L\s*(\d+(?:[.,]\d+)?)\s*[xX]\s*(\d+(?:[.,]\d+)?)(?:\s*[xX]\s*(\d+(?:[.,]\d+)?))?\s*",
                     str(nome))
    if not m:
        raise ValueError(f"cantoneira '{nome}': use 'L b1 x b2 x t' ou 'L b x t' (mm)")
    v = [float(x.replace(",", ".")) * MM for x in m.groups() if x]
    b1, b2, t = (v[0], v[0], v[1]) if len(v) == 2 else v
    if not t < min(b1, b2):
        raise ValueError(f"cantoneira '{nome}': espessura ≥ aba")
    return b1, b2, t


def _aco(nome):
    m = materiais.aco(nome)
    return m.fy, m.fu


# ---------------------------------------------------------------- coletor de verificações

class _Col:
    def __init__(self):
        self.v, self.avisos, self.val = [], [], {}

    def res(self, nome, sd, rd, un, item):
        if not (math.isfinite(rd) and rd > 0):
            raise ValueError(f"{nome}: resistência {rd:g} ≤ 0 — geometria ou dados inválidos")
        conv = 1 / MM if un == "mm" else 1.0
        item = item if item.startswith(("AISC", "Pfeil", "Gerdau", "Kulak", "geometria")) else "NBR 8800 " + item
        self.v.append(dict(nome=nome, item=item, solicitante=float(sd) * conv, resistente=float(rd) * conv,
                           unidade=un, taxa=float(sd / rd), ok=bool(sd <= rd * (1 + 1e-9))))

    def minimo(self, nome, minimo, real, item, parametro=None):   # disposição construtiva: taxa = mínimo/real
        if real <= 0:
            self.v.append(dict(nome=nome, item=item if item.startswith(("AISC", "Pfeil", "Gerdau", "geometria"))
                               else "NBR 8800 " + item, solicitante=minimo / MM, resistente=real / MM, unidade="mm",
                               taxa=_INF if minimo > 0 else 0.0, ok=minimo <= 0))
        else:
            self.res(nome, minimo, real, "mm", item)
        L._dica(self.v[-1], "sobe", parametro)

    def maximo(self, nome, real, maximo, item, parametro=None):
        self.res(nome, real, maximo, "mm", item)
        L._dica(self.v[-1], "desce", parametro)

    def bordas(self, nome, e, t, db, laminada):
        self.minimo(f"borda mínima ({nome})", L.distancia_min_borda(db, laminada), e, "6.3.11.1 / tabela 14")
        self.maximo(f"borda máxima ({nome})", e, min(12 * t, 0.150), "6.3.12")

    def espacamento(self, s, db, t):
        if 2.7 * db <= s < 3 * db:
            self.avisos.append(f"espaçamento {s / MM:.1f} mm < 3db = {3 * db / MM:.1f} mm (preferível, 6.3.9)")
        self.minimo("espaçamento mínimo entre furos (2,7db)", 2.7 * db, s, "6.3.9")
        self.maximo("espaçamento máximo entre furos (24t ≤ 300 mm)", s, min(24 * t, 0.300), "6.3.10")

    def resultado(self, geo):
        taxa = max((x["taxa"] for x in self.v), default=0.0)
        return dict(ok=all(x["ok"] for x in self.v), taxa=taxa, verificacoes=self.v, avisos=self.avisos,
                    valores=self.val, geometria=geo)


def _parafuso(d, r):
    db, fub, alta, dh, av = L._parafuso({"d_mm": d["d"] / MM, "tipo_parafuso": d["tipo_parafuso"]})
    r.avisos += av
    r.val.update(db_mm=db / MM, dh_mm=dh / MM)
    return dict(db=db, dh=dh, dn=dh + L.FURO_DANO,
                Fv=L.cisalhamento_parafuso(db, fub, d["rosca_no_plano"], alta), Ft=L.tracao_parafuso(db, fub))


def _impossivel(tipo, regras):
    erros = [f"{campo}: {desc} = {v / MM:g} mm (mínimo físico {lim / MM:g} mm)" for campo, v, lim, desc in regras
             if v <= lim + 1e-12]
    if erros:
        raise ValueError(f"ligação '{tipo}': geometria impossível — " + "; ".join(erros))


def _t_apoio(sp, apoio):
    return sp["tf"] if apoio == "mesa_pilar" else sp["tw"]


def _x_face(sp, apoio, d=None):
    """Distância do nó (eixo do apoio) à face de contato; `x_face` (dados, > 0) do chamador manda (G6)."""
    if d and d.get("x_face", 0) > 0:
        return d["x_face"]
    return sp["d"] / 2 if apoio == "mesa_pilar" else sp["tw"] / 2


def _fc(lf, t, p, fu, lim):
    return L.pressao_contato(lf, t, p["db"], fu, lim)


# ---------------------------------------------------------------- verificações compartilhadas

def _alma_viga(r, d, vg, fy, fu, V, furos, n, lf_holes):
    """Cisalhamento da alma (bruto e líquido) com recortes — 6.5.5 ([GD] 3.4.2, item 5)."""
    h0 = vg["d"] - d["recorte_sup_h"] - d["recorte_inf_h"]
    tw = vg["tw"]
    r.res("alma da viga: escoamento por cisalhamento", V, 0.6 * fy * h0 * tw / GA1, "kN", "6.5.5 a)")
    if furos:
        r.res("alma da viga: ruptura por cisalhamento", V, 0.6 * fu * (h0 - n * lf_holes) * tw / GA2, "kN",
              "6.5.5 b)")
    return h0


def _secao_recortada(vg, dct, dcb):
    """Módulo elástico (mínimo) da seção no fim do recorte: T (um recorte) ou retângulo (dois)."""
    tw, tf, bf, d = vg["tw"], vg["tf"], vg["bf"], vg["d"]
    h0 = d - dct - dcb
    if dct > 0 and dcb > 0:
        return tw * h0 * h0 / 6, h0
    hw = h0 - tf
    A1, A2 = bf * tf, tw * hw
    yc = (A1 * tf / 2 + A2 * (tf + hw / 2)) / (A1 + A2)          # a partir da mesa remanescente
    I = bf * tf ** 3 / 12 + A1 * (yc - tf / 2) ** 2 + tw * hw ** 3 / 12 + A2 * (tf + hw / 2 - yc) ** 2
    return I / max(yc, h0 - yc), h0


def _recortes(r, d, vg, fy, V, folga):
    """Viga recortada: início de escoamento ([GD] 3.4.2, item 8) e flambagem local ([AISC] p. 9-7) na seção do
    fim do recorte, com braço = folga + comprimento do recorte (reação na face do apoio)."""
    dct, ct, dcb, cb = d["recorte_sup_h"], d["recorte_sup_c"], d["recorte_inf_h"], d["recorte_inf_c"]
    if dct <= 0 and dcb <= 0:
        return
    dv, tw = vg["d"], vg["tw"]
    if (dct > 0 and ct <= 0) or (dcb > 0 and cb <= 0):
        raise ValueError("recorte com altura > 0 exige comprimento > 0")
    if dct + dcb >= dv - vg["tf"]:
        raise ValueError("recortes superior + inferior deixam alma sem altura")
    for pos, h, c in (("superior", dct, ct), ("inferior", dcb, cb)):
        if h <= 0:
            continue
        # seção no fim deste recorte: o outro recorte só conta se for pelo menos tão longo
        outro_h, outro_c = (dcb, cb) if pos == "superior" else (dct, ct)
        outro_h = outro_h if outro_c >= c else 0.0
        dois = outro_h > 0
        W, h0 = _secao_recortada(vg, *((h, outro_h) if pos == "superior" else (outro_h, h)))
        ex = folga + c
        r.val[f"W_recorte_{pos}_cm3"] = W * 1e6
        r.res(f"viga recortada ({pos}): início de escoamento por flexão", V * ex, fy * W / GA1, "kN·m",
              "5.4 / 6.5 (Gerdau 3.4.2)")
        if dois:
            dc = max(dct, dcb)
            if dc <= 0.2 * dv and c <= 2 * dv:
                fd = 3.5 - 7.5 * dc / dv
                Fcr = min(fy, 0.62 * math.pi * E_ACO * tw * tw / (c * h0) * fd)
                r.res("viga recortada (dois recortes): flambagem local da alma", V * ex, Fcr * W / GA1, "kN·m",
                      "AISC Manual 13ª ed. p. 9-7 (sem item na NBR)")
            else:
                r.avisos.append("dois recortes fora do campo do AISC p. 9-7 (dc ≤ 0,2d e c ≤ 2d): flambagem local "
                                "da alma recortada NÃO verificada")
        elif pos == "superior":
            if c <= 2 * dv and h <= dv / 2:
                f = 2 * c / dv if c / dv <= 1 else 1 + c / dv
                k = 2.2 * (h0 / c) ** 1.65 if c / h0 <= 1 else 2.2 * h0 / c
                Fcr = min(fy, 26210 * 6.894757 * MPa * (tw / h0) ** 2 * f * k)
                r.val["recorte_f_k"] = (f, k)
                r.res("viga recortada (superior): flambagem local da alma", V * ex, Fcr * W / GA1, "kN·m",
                      "AISC Manual 13ª ed. p. 9-7 (sem item na NBR)")
            else:
                r.avisos.append("recorte superior fora do campo do AISC p. 9-7 (c ≤ 2d e dc ≤ d/2): flambagem "
                                "local da alma recortada NÃO verificada")
    r.avisos.append("viga recortada: verifique a FLT da viga considerando o recorte (nota 4 das tabelas Gerdau)")


def _bloco_alma(r, d, vg, fy, fu, V, p, z_ultimo, e_h_alma):
    """Colapso por rasgamento da alma recortada no topo (6.5.6): cisalhamento do recorte ao último furo e tração até
    a ponta da viga ([GD] 3.4.2, item 7). Sem recorte superior a mesa impede o bloco (não verificado)."""
    if d["recorte_sup_h"] <= 0:
        return
    tw, dn = vg["tw"], p["dn"]
    Agv = (z_ultimo - d["recorte_sup_h"]) * tw
    Anv = Agv - (d["n"] - 0.5) * dn * tw
    Ant = (e_h_alma - 0.5 * dn) * tw
    r.res("alma da viga recortada: colapso por rasgamento", V, L.colapso_rasgamento(Agv, Anv, Ant, fy, fu), "kN",
          "6.5.6")


def _cabe_na_alma(r, d, vg, topo, base, nome):
    lim_s = d.get("recorte_sup_h", 0) or vg["tf"] + vg["r"]
    lim_i = d.get("recorte_inf_h", 0) or vg["tf"] + vg["r"]
    r.minimo(f"{nome} abaixo da mesa/recorte superior", lim_s, topo, "geometria (altura livre da alma)")
    r.minimo(f"{nome} acima da mesa/recorte inferior", lim_i, vg["d"] - base, "geometria (altura livre da alma)")


def _cabe_no_apoio(r, sp, apoio, largura, nome, folga_viga_sup=None, topo=None):
    """Largura ocupada no apoio: na mesa do pilar ≤ bf; na alma do pilar ≤ altura livre entre mesas."""
    if apoio == "mesa_pilar":
        r.maximo(f"{nome}: largura na mesa do pilar", largura, sp["bf"], "geometria")
    elif apoio == "alma_pilar":
        r.maximo(f"{nome}: largura entre as mesas do pilar", largura, sp["d"] - 2 * (sp["tf"] + sp["r"]),
                 "geometria")
    elif topo is not None:   # viga–viga, topos alinhados: abaixo da mesa da viga suporte
        r.minimo(f"{nome}: abaixo da mesa da viga suporte", sp["tf"] + sp["r"], topo, "geometria (viga–viga)")


def _face_apoio(sp, apoio):
    """Largura disponível na face do apoio (m): mesa = bf; alma do pilar = altura livre entre mesas; None = viga."""
    if apoio == "mesa_pilar":
        return sp["bf"]
    if apoio == "alma_pilar":
        return sp["d"] - 2 * (sp["tf"] + sp["r"])
    return None


def _u_viga(r, vg, p, V, pega):
    """Viga U ligada pelas costas da alma (G5): a ponta do parafuso fica entre as mesas e a reação passa fora do
    centro de cisalhamento (torção V·e na viga, não verificada aqui)."""
    ponta = _comp_parafuso(pega, p["db"]) * MM - pega
    r.maximo("parafusos: ponta dentro da mesa do U", ponta, vg["bf"] - vg["tw"], "geometria (gabarito na mesa do U)")
    e = vg["x_cc"]   # do plano de corte dos parafusos (costas da alma) ao centro de cisalhamento
    r.val["e_centro_cisalhamento_mm"] = e / MM
    r.avisos.append(f"viga U: reação a {e / MM:.0f} mm do centro de cisalhamento — torção V·e = "
                    f"{V * e:.2f} kN·m na viga não verificada (trave a viga ou confira a torção)")


def _comp_parafuso(pega, db):
    return math.ceil((pega + 1.2 * db + 6 * MM) / (5 * MM)) * 5   # mm: pega + porca/arruela + 3 fios (aprox.)


def _p(*v):
    return [round(x / MM, 3) for x in v]


def _cruz(u, v):
    return [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]


def _chapa(nome, origem, u, v, larg, alt, t):
    """Chapa retangular larg × alt; `origem` (argumento) = canto no plano médio; ponto = origem + a·u + b·v. No contrato
    a origem vai para a face: o sólido é origem + [0, espessura]·normal (G6, como gusset e rígidas)."""
    n = _cruz(u, v)
    return dict(nome=nome, contorno=[[0, 0], [larg / MM, 0], [larg / MM, alt / MM], [0, alt / MM]],
                origem=_p(*(origem[i] - n[i] * t / 2 for i in range(3))), normal=n, u=u, v=v, espessura=t / MM)


def _paraf(p, origem, eixo, pega):
    return dict(d=p["db"] / MM, comprimento=_comp_parafuso(pega, p["db"]), pega=pega / MM, origem=_p(*origem),
                eixo=eixo)


def _geo_recortes(d, x_ponta):
    out = [dict(barra="viga", tipo="recuo", parametros=dict(x_ponta=x_ponta / MM))]
    for pos, h, c in (("topo", d.get("recorte_sup_h", 0), d.get("recorte_sup_c", 0)),
                      ("base", d.get("recorte_inf_h", 0), d.get("recorte_inf_c", 0))):
        if h > 0:
            out.append(dict(barra="viga", tipo="cope", parametros=dict(posicao=pos, altura=h / MM, comprimento=c / MM,
                                                                        raio=RAIO_RECORTE, x_inicio=x_ponta / MM)))
    return out


# ---------------------------------------------------------------- cantoneira dupla na alma

def _cantoneira_dupla(d, e, tipo="cantoneira_dupla_alma"):
    r = _Col()
    V, N = _esforcos(e, r, d)
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    b1, b2, ta = _cantoneira(d["cantoneira"])
    p = _parafuso(d, r)
    db, dh, dn = p["db"], p["dh"], p["dn"]
    fyv, fuv = _aco(d["aco_viga"])
    fya, fua = _aco(d["aco_cantoneira"])
    fys, fus = _aco(d["aco_suporte"])
    apoio, n, s, ev, folga = d["apoio"], d["n"], d["s"], d["e_v"], d["folga"]
    gv, gs, tw = d["g_viga"], d["g_apoio"], vg["tw"]
    soldada = d["ligacao_viga"] == "soldada"
    exc, lim, lam = d["excentricidade_apoio"], d["deformacao_limitada"], d["borda_laminada"]
    t_ap = _t_apoio(sp, apoio)
    Lc = (n - 1) * s + 2 * ev
    zs = [d["dist_topo_furo"] + i * s for i in range(n)]       # profundidade dos furos a partir do topo da viga
    ys = [(i - (n - 1) / 2) * s for i in range(n)]
    _impossivel(tipo, [("s", s, dn, "espaçamento s"), ("e_v", ev, dn / 2, "borda vertical da cantoneira"),
                       ("g_apoio", b2 - gs, dn / 2, "borda da aba no apoio (b2 − g_apoio)"),
                       ("g_apoio", gs - ta, db / 2, "gabarito menos espessura (g_apoio − t)")]
                + ([] if soldada else [("g_viga", b1 - gv, dn / 2, "borda da aba na viga (b1 − g_viga)"),
                                       ("g_viga", gv - folga, dn / 2, "borda da alma na ponta (g_viga − folga)")])
                + ([("folga", b1 - folga, 2 * d["b_solda"], "filete horizontal (b1 − folga)")] if soldada else []))
    r.val.update(Lc_mm=Lc / MM)

    # parafusos na alma da viga (corte duplo; excentricidade desprezada, prática [GD]/AISC)
    if not soldada:
        Rb = math.hypot(V / n, N / n)
        r.res("parafusos na alma da viga: cisalhamento (2 planos)", Rb, 2 * p["Fv"], "kN", "6.3.3.2")
        lf_w = min(s - dh, zs[0] - d["recorte_sup_h"] - dh / 2)
        lf_a = min(s - dh, ev - dh / 2)
        if N > 0:
            lf_w, lf_a = min(lf_w, gv - folga - dh / 2), min(lf_a, b1 - gv - dh / 2)
        r.res("pressão de contato na alma da viga", Rb, _fc(lf_w, tw, p, fuv, lim), "kN", "6.3.3.3")
        r.res("pressão de contato nas cantoneiras (aba na viga)", Rb, 2 * _fc(lf_a, ta, p, fua, lim), "kN", "6.3.3.3")

    # parafusos no apoio (corte simples, n por cantoneira); excentricidade g_apoio por [PF] 9.10.2
    Fy = V / (2 * n)
    Fx = (V / 2) * gs * max(map(abs, ys)) / sum(y * y for y in ys) if exc else 0.0
    Rs = math.hypot(Fy, Fx)
    r.val.update(Fd_apoio=Rs, Fdy_apoio=Fy, Fdx_apoio=Fx)
    r.res("parafusos no apoio: cisalhamento", Rs, p["Fv"], "kN", "6.3.3.2")
    lf_s = min(s - dh, ev - dh / 2)
    if exc:
        lf_s = min(lf_s, b2 - gs - dh / 2)
    r.val["Fc_esmagamento_cantoneira"] = 2.4 * db * ta * fua / GA2 if lim else 3.0 * db * ta * fua / GA2
    r.res("pressão de contato nas cantoneiras (aba no apoio)", Rs, _fc(lf_s, ta, p, fua, lim), "kN", "6.3.3.3")
    r.res("pressão de contato no apoio (sem rasgamento)", Rs, _fc(_INF, t_ap, p, fus, lim), "kN", "6.3.3.3")
    if N > 0:
        T = N / (2 * n)
        al = alavanca(T, ta, min(s, Lc / n), gs - ta, b2 - gs, db, dh, fya)
        r.val["alavanca"] = al
        r.res("abas no apoio: flexão com efeito de alavanca", T, al["Tmax"], "kN", "Kulak & Fisher cap. 17 (6.3.5)")
        r.res("parafusos no apoio: tração com alavanca", al["B"], p["Ft"], "kN", "6.3.3.1 / 6.3.5")
        r.res("parafusos no apoio: tração + cisalhamento", L.interacao_tracao_cisalhamento(al["B"], p["Ft"], Rs, p["Fv"]),
              1.0, "-", "6.3.3.4")
    elif N < 0:
        r.avisos.append("compressão N transmitida por contato da viga/cantoneiras com o apoio (não verificada)")

    # cantoneiras
    r.res("cantoneiras: escoamento por cisalhamento", V, 2 * 0.6 * fya * Lc * ta / GA1, "kN", "6.5.5 a)")
    r.res("cantoneiras: ruptura por cisalhamento", V, 2 * 0.6 * fua * (Lc - n * dn) * ta / GA2, "kN", "6.5.5 b)")
    Agv = ((n - 1) * s + ev) * ta
    Anv = Agv - (n - 0.5) * dn * ta
    for aba, gg, bb in [("apoio", gs, b2)] + ([] if soldada else [("viga", gv, b1)]):
        Fr = 2 * L.colapso_rasgamento(Agv, Anv, (bb - gg - 0.5 * dn) * ta, fya, fua)
        r.val[f"Fr_cantoneiras_{aba}"] = Fr
        r.res(f"cantoneiras: colapso por rasgamento (aba no {aba})", V, Fr, "kN", "6.5.6")
    if exc:
        Inet = ta * (Lc ** 3 / 12 - sum(dn ** 3 / 12 + dn * y * y for y in ys))
        r.val["W_liq_aba_cm3"] = Inet / (Lc / 2) * 1e6
        r.res("cantoneiras: flexão da aba no apoio (V/2·g, seção líquida)", V / 2 * gs, Inet / (Lc / 2) * fya / GA1,
              "kN·m", "5.4 / 6.5 (Pfeil 9.10.2)")

    # alma da viga
    _alma_viga(r, d, vg, fyv, fuv, V, not soldada, n, dn)
    if not soldada:
        _bloco_alma(r, d, vg, fyv, fuv, V, p, zs[-1], gv - folga)
    _recortes(r, d, vg, fyv, V, folga)

    # solda cantoneira–alma ([PF] 9.10.2: V e momento V·e1, e1 da face do apoio ao centroide da solda)
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * MPa
    if soldada:
        Lh = b1 - folga
        g = solda_c(Lh, Lc)
        e1 = b1 - g["xb"]
        M = V / 2 * e1
        qy = V / 2 / g["Lt"] + M * (Lh - g["xb"]) / g["Ip"]
        qx = M * (Lc / 2) / g["Ip"] + abs(N) / 2 / g["Lt"]
        q = math.hypot(qy, qx)
        r.val.update(solda_xb_mm=g["xb"] / MM, solda_e1_mm=e1 / MM, solda_Ip_cm3=g["Ip"] * 1e6, solda_q=q)
        rw = L.resistencia_filete(bs, fw, fya, fua)
        r.res("solda cantoneira–alma (V, V·e1, N)", q, rw["FwRd"], "kN/m", "6.2.5 / tabela 8")
        r.res("alma da viga: metal base junto às soldas (2 cantoneiras)", 2 * q,
              min(0.6 * fyv * tw / GA1, 0.6 * fuv * tw / GA2), "kN/m", "6.5.5 / tabela 8")
        r.minimo("solda: dimensão mínima", L.filete_minimo(min(ta, tw)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
        r.maximo("solda: dimensão máxima (borda da cantoneira)", bs, L.filete_maximo(ta), "6.2.6.2.2", "b_solda")
        r.minimo("solda: comprimento mínimo do filete horizontal (4b e 40 mm)", max(4 * bs, 0.040), Lh, "6.2.6.2.3")

    # disposições construtivas
    r.bordas("cantoneira, vertical", ev, ta, db, lam)
    r.bordas("aba no apoio, horizontal", b2 - gs, ta, db, True)    # ponta da aba: borda laminada
    if not soldada:
        r.bordas("aba na viga, horizontal", b1 - gv, ta, db, True)
        r.bordas("alma da viga, ponta", gv - folga, tw, db, lam)
    r.espacamento(s, db, min(ta, tw))
    topo, base = zs[0] - ev, zs[-1] + ev
    _cabe_na_alma(r, d, vg, topo, base, "cantoneiras")
    _cabe_no_apoio(r, sp, apoio, tw + 2 * b2, "cantoneiras", topo=topo)
    if vg.get("forma") == "U":   # G5: a cantoneira de dentro fica entre as mesas do U
        r.maximo("cantoneira interna dentro da mesa do U", b1, vg["bf"] - tw, "geometria (gabarito na mesa do U)")
        e = vg["x_cc"] + tw / 2
        r.val["e_centro_cisalhamento_mm"] = e / MM
        r.avisos.append(f"viga U: reação a {e / MM:.0f} mm do centro de cisalhamento — torção V·e na viga não "
                        "verificada")
    if apoio == "mesa_pilar":
        r.minimo("borda dos furos na mesa do pilar", L.distancia_min_borda(db, lam), (sp["bf"] - tw) / 2 - gs,
                 "6.3.11.1 / tabela 14")
    if Lc < vg["d"] / 2:
        r.avisos.append(f"cantoneira com {Lc / MM:.0f} mm < d/2 = {vg['d'] / 2 / MM:.0f} mm: estabilidade da viga no "
                        "apoio comprometida (Gerdau 3.1.2)")
    if apoio == "alma_viga" and d["recorte_sup_h"] <= 0:
        r.avisos.append("viga–viga sem recorte: confira a interferência com a mesa da viga suporte (viga_viga_recorte)")
    r.avisos.append("apoio verificado só à pressão de contato dos parafusos; cortante/flexão local do apoio à parte")

    # geometria
    xf = _x_face(sp, apoio, d)
    ab = _abertura(d, vg)
    yw = ab * (tw / 2 - vg["xg"]) if vg.get("forma") == "U" else 0.0   # plano médio da alma (U abre para ab·y)
    zt = vg["d"] / 2
    zb = zt - base
    can = [dict(perfil=d["cantoneira"], origem=_p(xf, yw + sg * tw / 2, zb), eixo=[0, 0, 1], u=[1, 0, 0], w=[0, sg, 0],
                comprimento=Lc / MM, abas=[b1 / MM, b2 / MM], espessura=ta / MM) for sg in (1, -1)]
    par = [_paraf(p, (xf + ta, yw + sg * (tw / 2 + gs), zt - z), [-1, 0, 0], ta + t_ap) for sg in (1, -1) for z in zs]
    sol = []
    if soldada:
        sol = [dict(tipo="filete", perna=bs / MM,
                    pontos=[_p(xf + folga, yw + sg * tw / 2, zt - topo), _p(xf + b1, yw + sg * tw / 2, zt - topo),
                            _p(xf + b1, yw + sg * tw / 2, zb), _p(xf + folga, yw + sg * tw / 2, zb)]) for sg in (1, -1)]
    else:
        par += [_paraf(p, (xf + gv, yw + tw / 2 + ta, zt - z), [0, -1, 0], tw + 2 * ta) for z in zs]
    geo = dict(chapas=[], cantoneiras=can, parafusos=par, soldas=sol, recortes=_geo_recortes(d, xf + folga))
    if vg.get("forma") == "U":
        geo["abertura_y"] = ab
    return r.resultado(geo)


# ---------------------------------------------------------------- chapa simples (shear tab)

def _chapa_simples(d, e, tipo="chapa_simples_alma"):
    r = _Col()
    V, N = _esforcos(e, r, d)
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    p = _parafuso(d, r)
    db, dh, dn = p["db"], p["dh"], p["dn"]
    fyv, fuv = _aco(d["aco_viga"])
    fyc, fuc = _aco(d["aco_chapa"])
    fys, fus = _aco(d["aco_suporte"])
    apoio, n, s, ev, folga, a, eh, t = d["apoio"], d["n"], d["s"], d["e_v"], d["folga"], d["a"], d["e_h"], d["t_chapa"]
    tw, lim, lam = vg["tw"], d["deformacao_limitada"], d["borda_laminada"]
    Lc = (n - 1) * s + 2 * ev
    zs = [d["dist_topo_furo"] + i * s for i in range(n)]
    ys = [(i - (n - 1) / 2) * s for i in range(n)]
    _impossivel(tipo, [("s", s, dn, "espaçamento s"), ("e_v", ev, dn / 2, "borda vertical"),
                       ("e_h", eh, dn / 2, "borda horizontal da chapa"),
                       ("a", a - folga, dn / 2, "borda da alma na ponta (a − folga)")])
    r.val.update(Lc_mm=Lc / MM)

    lf_c = min(s - dh, ev - dh / 2, eh - dh / 2)
    lf_w = min(s - dh, zs[0] - d["recorte_sup_h"] - dh / 2, a - folga - dh / 2)
    Fc_c, Fc_w = _fc(lf_c, t, p, fuc, lim), _fc(lf_w, tw, p, fuv, lim)
    if N == 0:   # centro instantâneo (AISC cap. 7), como [GD] 5.4
        C = coeficiente_ci(ys, a)
        r.val["C_centro_instantaneo"] = C
        r.res("parafusos: cisalhamento (grupo excêntrico, centro instantâneo)", V, C * p["Fv"], "kN",
              "6.3.3.2 (AISC cap. 7)")
        r.res("pressão de contato na chapa (grupo excêntrico)", V, C * Fc_c, "kN", "6.3.3.3")
        r.res("pressão de contato na alma da viga (grupo excêntrico)", V, C * Fc_w, "kN", "6.3.3.3")
    else:        # método elástico com N
        M = V * a
        # parafuso extremo do lado em que N e V·a somam (linha simétrica): compressão não cancela a parcela de M
        Iy = sum(y * y for y in ys)   # 1 parafuso: rótula, V·a vai para a chapa (verificada abaixo)
        R = math.hypot(V / n, abs(N) / n + (abs(M) * max(map(abs, ys)) / Iy if Iy else 0.0))
        r.val["Fd_parafuso"] = R
        r.avisos.append("com N ≠ 0 o grupo de parafusos é verificado pelo método elástico (V, N e V·a)")
        r.res("parafusos: cisalhamento (V, N e V·a, elástico)", R, p["Fv"], "kN", "6.3.3.2")
        r.res("pressão de contato na chapa", R, Fc_c, "kN", "6.3.3.3")
        r.res("pressão de contato na alma da viga", R, Fc_w, "kN", "6.3.3.3")

    # chapa
    r.res("chapa: escoamento por cisalhamento", V, 0.6 * fyc * Lc * t / GA1, "kN", "6.5.5 a)")
    r.res("chapa: ruptura por cisalhamento", V, 0.6 * fuc * (Lc - n * dn) * t / GA2, "kN", "6.5.5 b)")
    Agv = ((n - 1) * s + ev) * t
    r.res("chapa: colapso por rasgamento", V,
          L.colapso_rasgamento(Agv, Agv - (n - 0.5) * dn * t, (eh - 0.5 * dn) * t, fyc, fuc), "kN", "6.5.6")
    Wg = t * Lc * Lc / 6
    Wn = t * (Lc ** 3 / 12 - sum(dn ** 3 / 12 + dn * y * y for y in ys)) / (Lc / 2)
    r.res("chapa: flexão V·a (início de escoamento, seção bruta)", V * a, fyc * Wg / GA1, "kN·m", "5.4 / 6.5 (Gerdau 5.4)")
    r.res("chapa: flexão V·a (ruptura, seção líquida)", V * a, fuc * Wn / GA2, "kN·m", "5.4 / 6.5 (Gerdau 5.4)")
    if N > 0:
        tr = L.tracao_chapa(Lc * t, (Lc - n * dn) * t, fyc, fuc)
        r.res("chapa: tração (escoamento)", N, tr["escoamento"], "kN", "6.5.3 → 5.2.2 a)")
        r.res("chapa: tração (ruptura)", N, tr["ruptura"], "kN", "6.5.3 → 5.2.2 b)")

    # alma da viga
    _alma_viga(r, d, vg, fyv, fuv, V, True, n, dn)
    _bloco_alma(r, d, vg, fyv, fuv, V, p, zs[-1], a - folga)
    _recortes(r, d, vg, fyv, V, folga)

    # solda chapa–apoio (2 filetes Lc; rótula na linha de parafusos — V e N, sem excentricidade)
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * MPa
    q = math.hypot(V, N) / (2 * Lc)
    t_ap = _t_apoio(sp, apoio)
    rw = L.resistencia_filete(bs, fw, fyc, fuc)
    r.res("solda chapa–apoio (V, N)", q, rw["FwRd"], "kN/m", "6.2.5 / tabela 8")
    r.res("apoio: metal base junto à solda", 2 * q, min(0.6 * fys * t_ap / GA1, 0.6 * fus * t_ap / GA2), "kN/m",
          "6.5.5 / tabela 8")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(t, t_ap)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
    r.minimo("solda: perna ≥ 5t/8 (chapa escoa antes da solda)", 5 * t / 8, bs, "AISC Manual 13ª ed. p. 10-102")

    # disposições construtivas
    r.maximo("ductilidade: menor entre t chapa e tw ≤ db/2 + 2 mm", min(t, tw), db / 2 + 2 * MM,
             "Gerdau 5.1.2 / AISC Manual 13ª ed. p. 10-102")
    r.bordas("chapa, vertical", ev, t, db, lam)
    r.bordas("chapa, horizontal", eh, t, db, lam)
    r.bordas("alma da viga, ponta", a - folga, tw, db, lam)
    r.espacamento(s, db, min(t, tw))
    topo, base = zs[0] - ev, zs[-1] + ev
    _cabe_na_alma(r, d, vg, topo, base, "chapa")
    _cabe_no_apoio(r, sp, apoio, t + 2 * bs, "chapa", topo=topo)
    if a > 89 * MM:
        r.avisos.append(f"a = {a / MM:.0f} mm > 89 mm: chapa simples estendida (AISC) — interação V+M e flambagem "
                        "da chapa não verificadas")
    if t < 6.3 * MM - 1e-9:
        r.avisos.append("chapa com t < 6,3 mm (mínimo usual, Gerdau 5.1.2)")
    if apoio == "alma_pilar":
        r.avisos.append("chapa soldada na alma do pilar: punção/flexão local da alma não verificada")

    xf = _x_face(sp, apoio, d)
    zt = vg["d"] / 2
    zb = zt - base
    # I: chapa do lado +y da alma (plano médio da alma em y = 0). U: chapa nas costas da alma (face externa em y =
    # −xg, o U abre para +y local) e parafusos de fora para dentro; a porca fica entre as mesas.
    ab = _abertura(d, vg)
    y0, sg = (-ab * vg["xg"], -ab) if vg.get("forma") == "U" else (tw / 2, 1)
    yc = y0 + sg * t / 2
    if vg.get("forma") == "U":
        _u_viga(r, vg, p, V, t + tw)
    ch = [_chapa("chapa simples", (xf, yc, zb), [1, 0, 0], [0, 0, 1], a + eh, Lc, t)]
    par = [_paraf(p, (xf + a, y0 + sg * t, zt - z), [0, -sg, 0], t + tw) for z in zs]
    sol = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, y, zb), _p(xf, y, zb + Lc)]) for y in (y0, y0 + sg * t)]
    geo = dict(chapas=ch, cantoneiras=[], parafusos=par, soldas=sol, recortes=_geo_recortes(d, xf + folga))
    if vg.get("forma") == "U":
        geo["abertura_y"] = ab
    return r.resultado(geo)


# ---------------------------------------------------------------- chapa de extremidade flexível

def _chapa_extremidade(d, e, tipo="chapa_extremidade_flexivel"):
    r = _Col()
    V, N = _esforcos(e, r, d)
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    p = _parafuso(d, r)
    db, dh, dn = p["db"], p["dh"], p["dn"]
    fyv, fuv = _aco(d["aco_viga"])
    fyc, fuc = _aco(d["aco_chapa"])
    fys, fus = _aco(d["aco_suporte"])
    apoio, n, s, ev, t = d["apoio"], d["n"], d["s"], d["e_v"], d["t_chapa"]
    bp, gch, tw, lim, lam = d["largura_chapa"], d["gabarito"], vg["tw"], d["deformacao_limitada"], d["borda_laminada"]
    Lp = (n - 1) * s + 2 * ev
    el = (bp - gch) / 2
    _impossivel(tipo, ([("s", s, dn, "espaçamento s")] if n > 1 else [])
                + [("e_v", ev, dn / 2, "borda vertical"), ("gabarito", el, dn / 2, "borda lateral (largura − g)/2"),
                   ("gabarito", (gch - tw) / 2 - d["b_solda"], db / 2, "parafuso à solda ((g − tw)/2 − b)")])
    r.val.update(Lp_mm=Lp / MM, Nps=2 * n)
    t_ap = _t_apoio(sp, apoio)

    Fv = V / (2 * n)
    r.res("parafusos: cisalhamento", Fv, p["Fv"], "kN", "6.3.3.2")
    lf = min(s - dh if n > 1 else _INF, ev - dh / 2)
    r.res("pressão de contato na chapa", Fv, _fc(lf, t, p, fuc, lim), "kN", "6.3.3.3")
    r.res("pressão de contato no apoio (sem rasgamento)", Fv, _fc(_INF, t_ap, p, fus, lim), "kN", "6.3.3.3")
    if N > 0:
        T = N / (2 * n)
        al = alavanca(T, t, min(s, Lp / n) if n > 1 else Lp, (gch - tw) / 2, el, db, dh, fyc)
        r.val["alavanca"] = al
        r.res("chapa: flexão com efeito de alavanca", T, al["Tmax"], "kN", "Kulak & Fisher cap. 17 (6.3.5)")
        r.res("parafusos: tração com alavanca", al["B"], p["Ft"], "kN", "6.3.3.1 / 6.3.5")
        r.res("parafusos: tração + cisalhamento", L.interacao_tracao_cisalhamento(al["B"], p["Ft"], Fv, p["Fv"]),
              1.0, "-", "6.3.3.4")

    r.res("chapa: escoamento por cisalhamento (2 seções)", V, 2 * 0.6 * fyc * Lp * t / GA1, "kN", "6.5.5 a)")
    r.res("chapa: ruptura por cisalhamento (2 seções)", V, 2 * 0.6 * fuc * (Lp - n * dn) * t / GA2, "kN", "6.5.5 b)")
    Agv = ((n - 1) * s + ev) * t
    r.res("chapa: colapso por rasgamento (2 blocos)", V,
          2 * L.colapso_rasgamento(Agv, Agv - (n - 0.5) * dn * t, (el - 0.5 * dn) * t, fyc, fuc), "kN", "6.5.6")
    r.res("alma da viga: escoamento por cisalhamento junto à solda", V, 0.6 * fyv * Lp * tw / GA1, "kN",
          "6.5.5 a) (Gerdau 4.4)")

    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * MPa
    q = math.hypot(V, N) / (2 * Lp)
    rw = L.resistencia_filete(bs, fw, fyc, fuc)
    r.res("solda chapa–alma (V, N)", q, rw["FwRd"], "kN/m", "6.2.5 / tabela 8")
    r.res("alma da viga: metal base junto às soldas", 2 * q, min(0.6 * fyv * tw / GA1, 0.6 * fuv * tw / GA2), "kN/m",
          "6.5.5 / tabela 8")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(t, tw)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
    r.minimo("solda: comprimento mínimo do filete (4b e 40 mm)", max(4 * bs, 0.040), Lp, "6.2.6.2.3")

    r.bordas("chapa, vertical", ev, t, db, lam)
    r.bordas("chapa, lateral", el, t, db, lam)
    if n > 1:
        r.espacamento(s, db, t)
    topo = d["dist_topo_chapa"]
    _cabe_na_alma(r, {}, vg, topo, topo + Lp, "chapa")
    _cabe_no_apoio(r, sp, apoio, bp, "chapa", topo=topo)
    if t > 10 * MM:
        r.avisos.append(f"chapa de extremidade com t = {t / MM:g} mm > 10 mm: rótula duvidosa (Gerdau 4.1 usa 6,3 mm)")
    r.avisos.append("apoio verificado só à pressão de contato dos parafusos; flexão local da mesa/alma do apoio à parte")

    xf = _x_face(sp, apoio, d)
    zt = vg["d"] / 2
    zb = zt - topo - Lp
    zs = [zt - topo - ev - i * s for i in range(n)]
    # G5: viga U → chapa, parafusos e soldas centrados no plano médio da alma (centroide no eixo, costas em y = −ab·xg)
    ab = _abertura(d, vg)
    yw = ab * (tw / 2 - vg["xg"]) if vg.get("forma") == "U" else 0.0
    ch = [_chapa("chapa de extremidade", (xf + t / 2, yw - bp / 2, zb), [0, 1, 0], [0, 0, 1], bp, Lp, t)]
    par = [_paraf(p, (xf + t, yw + sg * gch / 2, z), [-1, 0, 0], t + t_ap) for sg in (1, -1) for z in zs]
    sol = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf + t, yw + sg * tw / 2, zb), _p(xf + t, yw + sg * tw / 2, zb + Lp)])
           for sg in (1, -1)]
    geo = dict(chapas=ch, cantoneiras=[], parafusos=par, soldas=sol,
               recortes=[dict(barra="viga", tipo="recuo", parametros=dict(x_ponta=(xf + t) / MM))])
    if vg.get("forma") == "U":
        geo["abertura_y"] = ab
    return r.resultado(geo)


# ---------------------------------------------------------------- apoio em cantoneira (com/sem enrijecedor)

def _apoio(d, e, tipo="apoio_cantoneira"):
    r = _Col()
    V, N = _esforcos(e, r, d)
    if N != 0:
        r.avisos.append("apoio em cantoneira/consolo não transmite N: força normal ignorada (prever outro caminho)")
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    fyv, fuv = _aco(d["aco_viga"])
    fya, fua = _aco(d["aco_cantoneira"])
    fys, fus = _aco(d["aco_suporte"])
    apoio, folga, lam = d["apoio"], d["folga"], d["borda_laminada"]
    tw, tf, dv = vg["tw"], vg["tf"], vg["d"]
    k = tf + vg["r"]
    t_ap = _t_apoio(sp, apoio)
    enr = d["assento"] == "enrijecido"
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * MPa
    xf = _x_face(sp, apoio, d)
    zt = dv / 2
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[],
               recortes=[dict(barra="viga", tipo="recuo", parametros=dict(x_ponta=(xf + folga) / MM))])

    proj = d["b_enrijecedor"] if enr else _cantoneira(d["cantoneira"])[0]
    ln = proj - folga
    if ln <= 0:
        raise ValueError(f"ligação '{tipo}': assento ({proj / MM:g} mm) não ultrapassa a folga")
    a_nec = comprimento_apoio_necessario(V, dv, tw, tf, k, fyv)
    r.val.update(a_necessario_mm=a_nec / MM, k_mm=k / MM, ln_disponivel_mm=ln / MM)
    r.res("alma da viga: escoamento local (extremidade)", V, escoamento_local_alma(ln, k, tw, fyv), "kN", "5.7.3")
    r.res("alma da viga: enrugamento (extremidade)", V, enrugamento_alma(ln, dv, tw, tf, fyv), "kN", "5.7.4")
    a1 = min(max(a_nec, k), ln)     # comprimento de apoio adotado (≥ k, AISC; ≤ disponível)

    if enr:
        t0, h0, w, tp = d["t_enrijecedor"], d["h_enrijecedor"], d["largura_assento"], d["t_assento"]
        ex = proj - a1 / 2                                     # [PF] eq. 9.5
        r.val["e_mm"] = ex / MM
        r.minimo("enrijecedor: t0 ≥ tw da viga", tw, t0, "Pfeil 9.5")
        r.res("enrijecedor: b/t0 ≤ 16 (flambagem local)", proj / t0, 16.0, "-", "Pfeil 9.5")
        r.res("enrijecedor: escoamento por cisalhamento na face soldada", V, 0.6 * fya * h0 * t0 / GA1, "kN", "6.5.5 a)")
        g = solda_consolo(w, h0)
        tv, th = V / g["Lt"], V * ex * g["yb"] / g["I"]
        q = math.hypot(tv, th)
        r.val.update(solda_yb_mm=g["yb"] / MM, solda_I_cm3=g["I"] * 1e6, solda_tv=tv, solda_th=th, solda_q=q)
        rw = L.resistencia_filete(bs, fw, fys, fus)
        r.res("solda consolo–apoio (V e V·e, topo)", q, rw["FwRd"], "kN/m", "6.2.5 / tabela 8")
        r.minimo("solda: dimensão mínima", L.filete_minimo(min(t0, t_ap)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
        _cabe_no_apoio(r, sp, apoio, w, "assento")
        if d["ligacao_apoio"] == "parafusada":
            r.avisos.append("consolo enrijecido parafusado não implementado: verificado como soldado ([PF] 9.10.6)")
        zs0 = -zt - tp
        geo["chapas"] = [_chapa("assento", (xf, -w / 2, -zt - tp / 2), [1, 0, 0], [0, 1, 0], proj, w, tp),
                         _chapa("enrijecedor", (xf, 0, zs0 - h0), [1, 0, 0], [0, 0, 1], proj, h0, t0)]
        geo["soldas"] = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, -w / 2, zs0), _p(xf, w / 2, zs0)])] + \
            [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, sg * t0 / 2, zs0), _p(xf, sg * t0 / 2, zs0 - h0)])
             for sg in (1, -1)]
        r.avisos.append("solda do consolo: tensão no topo (compressão da base por contato, como [PF] 9.10.6)")
    else:
        b1, b2, ta = _cantoneira(d["cantoneira"])
        lc = d["comprimento"]
        ex = folga + a1 / 2
        eA = max(ex - ta - 10 * MM, 0.0)                   # seção A-A a t + 10 mm do dorso (AISC: t + 3/8")
        r.val.update(e_mm=ex / MM, eA_mm=eA / MM)
        r.res("assento: flexão na seção A-A", V * eA, lc * ta * ta / 4 * fya / GA1, "kN·m", "5.4 (Pfeil eq. 9.4)")
        r.res("assento: escoamento por cisalhamento na seção A-A", V, 0.6 * fya * lc * ta / GA1, "kN", "6.5.5 a)")
        geo["cantoneiras"].append(dict(perfil=d["cantoneira"], origem=_p(xf, -lc / 2, -zt), eixo=[0, 1, 0],
                                       u=[1, 0, 0], w=[0, 0, -1], comprimento=lc / MM, abas=[b1 / MM, b2 / MM],
                                       espessura=ta / MM))
        if d["ligacao_apoio"] == "parafusada":
            p = _parafuso(d, r)
            db, dh, dn = p["db"], p["dh"], p["dn"]
            n, nc, s, ev, gc = d["n"], d["n_colunas"], d["s"], d["e_v"], d["g_colunas"]
            z1 = b2 - ev - (n - 1) * s                          # 1º furo, do topo do assento
            ycols = [0.0] if nc == 1 else [-gc / 2, gc / 2]
            _impossivel(tipo, ([("s", s, dn, "espaçamento s")] if n > 1 else [])
                        + [("e_v", ev, dn / 2, "borda inferior da aba vertical"),
                           ("comprimento", lc / 2 - max(map(abs, ycols)), dn / 2, "borda lateral da cantoneira")])
            Fv = V / (n * nc)
            r.avisos.append("parafusos do assento: excentricidade desprezada (prática AISC para apoio em cantoneira)")
            r.res("parafusos no apoio: cisalhamento", Fv, p["Fv"], "kN", "6.3.3.2")
            lf = min(s - dh if n > 1 else _INF, ev - dh / 2)
            r.res("pressão de contato na cantoneira", Fv, _fc(lf, ta, p, fua, d["deformacao_limitada"]), "kN",
                  "6.3.3.3")
            r.res("pressão de contato no apoio (sem rasgamento)", Fv, _fc(_INF, t_ap, p, fus, d["deformacao_limitada"]),
                  "kN", "6.3.3.3")
            r.bordas("aba vertical, inferior", ev, ta, db, True)    # ponta da aba: laminada
            r.bordas("cantoneira, lateral", lc / 2 - max(map(abs, ycols)), ta, db, lam)
            if n > 1:
                r.espacamento(s, db, ta)
            if nc == 2:
                r.minimo("gabarito entre colunas (2,7db)", 2.7 * db, gc, "6.3.9")
            r.minimo("1º furo abaixo do raio da cantoneira (t + 1,5db)", ta + 1.5 * db, z1, "geometria (aperto)")
            geo["parafusos"] = [_paraf(p, (xf + ta, y, -zt - z1 - i * s), [-1, 0, 0], ta + t_ap)
                                for y in ycols for i in range(n)]
        else:
            Lw = b2
            q = math.hypot(V / (2 * Lw), 6 * V * ex / (2 * Lw * Lw))
            r.val["solda_q"] = q
            rw = L.resistencia_filete(bs, fw, fya, fua)
            r.res("solda cantoneira–apoio (2 filetes verticais, V e V·e)", q, rw["FwRd"], "kN/m", "6.2.5 / tabela 8")
            r.minimo("solda: dimensão mínima", L.filete_minimo(min(ta, t_ap)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
            r.maximo("solda: dimensão máxima (borda da cantoneira)", bs, L.filete_maximo(ta), "6.2.6.2.2", "b_solda")
            geo["soldas"] = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, sg * lc / 2, -zt), _p(xf, sg * lc / 2,
                                                                                                        -zt - b2)])
                             for sg in (1, -1)]
        _cabe_no_apoio(r, sp, apoio, lc, "assento")
        bs1, _, ts = _cantoneira(d["cantoneira_superior"])
        geo["cantoneiras"].append(dict(perfil=d["cantoneira_superior"], origem=_p(xf, -lc / 2, zt), eixo=[0, 1, 0],
                                       u=[1, 0, 0], w=[0, 0, 1], comprimento=lc / MM, abas=[bs1 / MM, bs1 / MM],
                                       espessura=ts / MM))
        r.avisos.append("cantoneira superior: só estabilidade lateral (detalhe construtivo, não verificada)")
    if a_nec > ln:
        r.avisos.append(f"comprimento de apoio necessário {a_nec / MM:.0f} mm > disponível {ln / MM:.0f} mm")
    return r.resultado(geo)


# ---------------------------------------------------------------- viga–viga com recorte

def _viga_viga(d, e):
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    rec = recortes_viga_viga(vg, sp, d["folga"], d["desnivel"])
    rp = d.get("_recortes_ponta") or {}
    # G2: recorte desenhado = calculado, conservador: o maior entre Gerdau 3.3 e o cope da ponta (pontas.py)
    rec = {k: max(v, rp.get(k, 0.0)) for k, v in rec.items()}
    dd = dict(d, apoio="alma_viga", **rec)
    if d["conector"] == "chapa_simples":
        res = _chapa_simples(dd, e, "viga_viga_recorte")
    else:
        res = _cantoneira_dupla(dd, e, "viga_viga_recorte")
    res["valores"].update({k + "_mm": v / MM for k, v in rec.items()})
    res["recortes_usados"] = rec   # com_pontas devolve à ponta (render = cálculo)
    if d["desnivel"] > 0:
        z = d["desnivel"]
        for x in res["geometria"]["recortes"]:
            x["parametros"]["desnivel"] = z / MM
    return res


# ---------------------------------------------------------------- solda direta da alma (G5)

def _solda_direta(d, e, tipo="solda_direta_alma"):
    """Alma da viga (U pequeno, UPN 50/65) soldada direto no apoio, sem chapa nem parafusos: dois filetes de
    comprimento Lw (um em cada face da alma; padrão Lw = h: a face interna continua pelas mesas).

    Conferência à mão (E70, fw = 485 MPa, perna 5 mm, 2 × 84 mm, aço A-572 GR.50):
      metal da solda 0,6·0,707·5·485/1,35 = 762 N/mm; metal base 0,6·5·345/1,10 = 941 N/mm → FwRd = 762 N/mm;
      2 × 84 × 0,762 = 128 kN (≈ 127 kN da revisão).
    UPN 50 em UPN 100, V = 45 kN (6.1.5.2): Lw = 50 mm → solda 2 × 50 × 0,762 = 76 kN; alma do UPN 50
      0,6·345·50·5/1,10 = 47,0 kN (taxa 0,96)."""
    r = _Col()
    V, N = _esforcos(e, r, d)
    vg, sp = _perfil(d["perfil_viga"], "perfil_viga"), _perfil(d["perfil_suporte"], "perfil_suporte")
    fyv, fuv = _aco(d["aco_viga"])
    fys, fus = _aco(d["aco_suporte"])
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * MPa
    tw, t_ap, apoio = vg["tw"], _t_apoio(sp, d["apoio"]), d["apoio"]
    # 26/10/2026: mesas soldadas (auto = sim no I, não no U pequeno, o caso de origem). O cordão contorna o perfil: a alma
    # entre as mesas (Lw = d − 2·tf) leva V; N se divide no contorno todo (alma 2·Lw + mesas: bf por fora e bf − tw por dentro).
    sm = d.get("soldar_mesas", "auto")
    mesas = (vg.get("forma") != "U") if sm == "auto" else sm == "sim"
    fwrd = L.resistencia_filete(bs, fw, fyv, fuv)["FwRd"]
    if mesas:
        Lw = d["comprimento_solda"] or vg["d"] - 2 * vg["tf"]
        Lt = 2 * Lw + 2 * (2 * vg["bf"] - tw)
        qn = abs(N) / Lt
        q = math.hypot(V / (2 * Lw), qn)
        r.val.update(Lw_mm=Lw / MM, L_contorno_mm=Lt / MM, q_kN_m=q)
    else:
        Lw = d["comprimento_solda"] or vg["d"]
        q = math.hypot(V, N) / (2 * Lw)
        r.val.update(Lw_mm=Lw / MM, q_kN_m=q)
    r.res("solda alma–apoio (V, N; 2 filetes)", q, fwrd, "kN/m", "6.2.5 / tabela 8")
    if mesas:
        r.res("solda das mesas (N no contorno)", qn, fwrd, "kN/m", "6.2.5 / tabela 8")
        r.avisos.append("mesas soldadas: a ligação passa a transmitir momento (deixa de ser flexível); o momento não é "
                        "verificado aqui — se o modelo a trata como rotulada, use 'soldar as mesas: não' ou uma ligação rígida")
    r.res("apoio: metal base junto à solda", q, L.resistencia_filete(bs, fw, fys, fus)["metal_base"], "kN/m",
          "6.2.5 / tabela 8")
    r.res("alma da viga: metal base entre os filetes", 2 * q, min(0.6 * fyv * tw / GA1, 0.6 * fuv * tw / GA2),
          "kN/m", "6.5.5 / tabela 8")
    r.res("alma da viga: escoamento por cisalhamento (Aw = h·tw)", V, 0.6 * fyv * vg["d"] * tw / GA1, "kN",
          "5.4.3.1 / 6.5.5 a)")
    if N > 0:
        r.res("alma da viga: tração na seção da solda", N, Lw * tw * fyv / GA1, "kN", "5.2.2 a)")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(tw, t_ap)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
    r.maximo("solda: dimensão máxima (borda da alma)", bs, L.filete_maximo(tw), "6.2.6.2.2", "b_solda")
    r.minimo("solda: comprimento mínimo do filete (4b e 40 mm)", max(4 * bs, 0.040), Lw, "6.2.6.2.3")
    _cabe_no_apoio(r, sp, apoio, (vg["bf"] if mesas else tw) + 2 * bs, "solda", topo=vg["tf"] + vg["r"])
    if vg.get("forma") == "U":
        e_ = vg["x_cc"]
        r.val["e_centro_cisalhamento_mm"] = e_ / MM
        r.avisos.append(f"viga U: reação a {e_ / MM:.0f} mm do centro de cisalhamento — torção na viga não verificada")
    r.avisos.append("solda direta: ligação de campo soldada (sem montagem parafusada); confira o acesso à face interna")
    xf = _x_face(sp, apoio, d)
    ab = _abertura(d, vg)
    yw = ab * (tw / 2 - vg.get("xg", tw / 2)) if vg.get("forma") == "U" else 0.0
    zt = vg["d"] / 2
    # G6: um filete fechado contornando a ponta da alma (as duas faces, Lw cada, e os retornos de tw no topo e na base)
    z1, z2, y1, y2 = zt - (vg["d"] - Lw) / 2, zt - (vg["d"] + Lw) / 2, yw + tw / 2, yw - tw / 2
    sol = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, y1, z1), _p(xf, y1, z2), _p(xf, y2, z2), _p(xf, y2, z1),
                                                      _p(xf, y1, z1)])]
    if mesas:   # contorno do perfil inteiro (sentido anti-horário em (y, z)), fechado
        h, b, tf_ = vg["d"] / 2, vg["bf"], vg["tf"]
        if vg.get("forma") == "U":   # costas da alma em yb, mesas para o lado da abertura (ab)
            yb, s_ = yw - ab * tw / 2, ab
            yz = [(yb, -h), (yb + s_ * b, -h), (yb + s_ * b, -h + tf_), (yb + s_ * tw, -h + tf_), (yb + s_ * tw, h - tf_),
                  (yb + s_ * b, h - tf_), (yb + s_ * b, h), (yb, h)]
        else:
            yz = [(-b / 2, -h), (b / 2, -h), (b / 2, -h + tf_), (tw / 2, -h + tf_), (tw / 2, h - tf_), (b / 2, h - tf_),
                  (b / 2, h), (-b / 2, h), (-b / 2, h - tf_), (-tw / 2, h - tf_), (-tw / 2, -h + tf_), (-b / 2, -h + tf_)]
            yz = [(y + yw, z) for y, z in yz]
        sol = [dict(tipo="filete", perna=bs / MM, pontos=[_p(xf, y, z) for y, z in yz + yz[:1]])]
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=sol,
               recortes=[dict(barra="viga", tipo="recuo", parametros=dict(x_ponta=(xf + d["folga"]) / MM))])
    if vg.get("forma") == "U":
        geo["abertura_y"] = ab
    return r.resultado(geo)


def _abertura(d, vg):
    """Lado local (±1) para onde o U abre (G5): `abertura_y` do chamador; padrão +1."""
    return -1 if vg.get("forma") == "U" and int(d.get("abertura_y") or 1) < 0 else 1


# ---------------------------------------------------------------- pré-dimensionamento (sugerir)

def _perfis_de(perfis):
    perfis = dict(perfis or {})
    v = next((perfis[k] for k in ("perfil_viga", "viga") if perfis.get(k) is not None), None)
    s = next((perfis[k] for k in ("perfil_suporte", "suporte", "perfil_pilar", "pilar", "viga_suporte")
              if perfis.get(k) is not None), None)
    if v is None or s is None:
        raise ValueError("sugerir: informe perfil_viga e perfil_suporte (ou viga e pilar/suporte)")
    return v, s, perfis.get("apoio", "mesa_pilar")


def _padrao_gerdau(dv):
    """[GD] 2.4 e 3.1.2: bitola, espaçamento, borda e cantoneira pela altura da viga."""
    if dv <= 0.150:   # G5: U/UPN baixos (altura útil ~84 mm no UPN 120); fora da tabela Gerdau
        return dict(d=12.7, s=38, e_v=22, cantoneira="L51x51x6.35", g=30)
    if dv <= 0.215:
        return dict(d=15.875, s=60, e_v=30, cantoneira="L76x76x6.35", g=45)
    if dv <= 0.560:
        return dict(d=19.05, s=75, e_v=40, cantoneira="L76x76x6.35", g=45)
    return dict(d=22.225, s=75, e_v=40, cantoneira="L102x102x7.94", g=65)


_CTX = ("x_face", "x_max", "abertura_y")   # G6 r4: geometria do chamador que muda a escolha (cabe na barra?)


def _tentar(tipo, candidatos, esforcos, perfis=None):
    ctx = {k: perfis[k] for k in _CTX if (perfis or {}).get(k) is not None}
    candidatos = [dict(c, **ctx) for c in candidatos]
    ult = None
    for c in candidatos:
        try:
            if TIPOS[tipo]["verificar"](c, esforcos)["ok"]:
                return c
        except ValueError:
            continue
        ult = c
    return ult or candidatos[-1]


def _furos_viga(vg, pd, n, rec_sup=0.0, rec_inf=0.0):
    """dist_topo_furo (mm): 75 mm [GD] 3.1.1, ou centrado em perfis ≤ 215 mm; empurrado para baixo do recorte /
    mesa; None se a cantoneira não cabe na altura livre."""
    s, ev = pd["s"] * MM, pd["e_v"] * MM
    lim_s, lim_i = rec_sup or vg["tf"] + vg["r"], rec_inf or vg["tf"] + vg["r"]
    if (n - 1) * s + 2 * ev > vg["d"] - lim_s - lim_i:
        return None
    topo = (vg["d"] - (n - 1) * s) / 2 if vg["d"] <= 0.215 else 0.075
    topo = min(max(topo, lim_s + ev), vg["d"] - lim_i - ev - (n - 1) * s)
    return round(topo / MM, 1)


def _sug_cd(perfis, esforcos, tipo="cantoneira_dupla_alma", extra=None):
    v, s_, apoio = _perfis_de(perfis)
    vg = _perfil(v, "perfil_viga")
    pd = _padrao_gerdau(vg["d"])
    extra = dict(extra or {})
    cands = []
    for n in range(2, 13):
        dt = _furos_viga(vg, pd, n, extra.get("_rs", 0.0), extra.get("_ri", 0.0))
        if dt is None:
            break
        if ((n - 1) * pd["s"] + 2 * pd["e_v"]) * MM < vg["d"] / 2 and n < 12:
            nxt = _furos_viga(vg, pd, n + 1, extra.get("_rs", 0.0), extra.get("_ri", 0.0))
            if nxt is not None:
                continue                                    # [GD] 3.1.2: Lc ≥ d/2 sempre que couber
        c = dict(perfil_viga=v, perfil_suporte=s_, d=pd["d"], s=pd["s"], e_v=pd["e_v"], n=n,
                 dist_topo_furo=dt, cantoneira=pd["cantoneira"], g_viga=pd["g"], g_apoio=pd["g"])
        if tipo != "viga_viga_recorte":
            c["apoio"] = apoio
        cands.append(c)
    if not cands:
        raise ValueError(f"sugerir: perfil {v} baixo demais para {tipo}")
    return _tentar(tipo, cands, esforcos, perfis)


def _sug_cs(perfis, esforcos):
    v, s_, apoio = _perfis_de(perfis)
    vg = _perfil(v, "perfil_viga")
    pd = _padrao_gerdau(vg["d"])
    cands, compactas = [], []
    emin = L.distancia_min_borda(pd["d"] * MM) / MM
    # G5: U baixo (UPN 100: altura útil 66 mm) não leva 2 parafusos → 1 parafuso
    for n in range(2, 13) if _furos_viga(vg, pd, 2) is not None else (1,):
        dt = _furos_viga(vg, pd, n)
        if dt is None:
            break
        for t in (6.3, 8.0, 9.5, 12.5):
            if t * MM > pd["d"] * MM / 2 + 2 * MM:
                break
            c = dict(perfil_viga=v, perfil_suporte=s_, apoio=apoio, d=pd["d"], s=pd["s"], e_v=pd["e_v"],
                     n=n, dist_topo_furo=dt, t_chapa=t, b_solda=max(5, math.ceil(5 * t / 8)))
            cands.append(c)
            # G6 r4: barra curta (x_max) → chapa compacta: furo e borda nas mínimas da Tab. 14 (a = folga 10 + emin)
            if (perfis or {}).get("x_max"):
                compactas.append(dict(c, a=10 + emin, e_h=emin))
    cands += compactas
    if not cands:
        raise ValueError(f"sugerir: perfil {v} baixo demais para chapa simples")
    return _tentar("chapa_simples_alma", cands, esforcos, perfis)


def _sug_ce(perfis, esforcos):
    v, s_, apoio = _perfis_de(perfis)
    vg, sp = _perfil(v, "perfil_viga"), _perfil(s_, "perfil_suporte")
    pd = _padrao_gerdau(vg["d"])
    larg = _face_apoio(sp, apoio) or 0.2
    bp = min(0.200, math.floor(larg / MM / 5) * 5 * MM)
    emin = L.distancia_min_borda(pd["d"] * MM)
    # G5: chapa mínima = duas colunas de parafusos que passem da solda na alma + bordas; não cabe → recusa (nunca
    # chapa cortada em silêncio)
    bmin = vg["tw"] + 2 * (5 * MM + pd["d"] * MM / 2 + emin)
    if bmin > larg + 1e-9:
        raise ValueError(f"chapa de topo {math.ceil(bmin / MM)} mm > face do apoio {larg / MM:.0f} mm "
                         f"({s_}): use cantoneira lateral ou outro apoio")
    gch = min(0.120, bp - 2 * emin)
    topo = vg["tf"] + vg["r"] + 5 * MM
    cands = []
    for n in range(1, 13):
        Lp = (n - 1) * pd["s"] * MM + 2 * pd["e_v"] * MM
        if topo + Lp > vg["d"] - vg["tf"] - vg["r"]:
            break
        for t in (6.3, 8.0, 9.5):
            cands.append(dict(perfil_viga=v, perfil_suporte=s_, apoio=apoio, d=pd["d"], s=pd["s"], e_v=pd["e_v"], n=n,
                              t_chapa=t, largura_chapa=round(bp / MM), gabarito=round(gch / MM),
                              dist_topo_chapa=round(topo / MM)))
    if not cands:
        raise ValueError(f"sugerir: perfil {v} baixo demais para chapa de extremidade")
    return _tentar("chapa_extremidade_flexivel", cands, esforcos, perfis)


def _sug_ap(perfis, esforcos):
    v, s_, apoio = _perfis_de(perfis)
    cands = [dict(perfil_viga=v, perfil_suporte=s_, apoio=apoio, assento="cantoneira", cantoneira=f"L102x152x{t}",
                  d=19.05, s=60, e_v=30) for t in (9.5, 12.7, 15.9, 19.0, 22.2, 25.4)]
    cands += [dict(perfil_viga=v, perfil_suporte=s_, apoio=apoio, assento="enrijecido", t_enrijecedor=t,
                   h_enrijecedor=h, b_enrijecedor=125, largura_assento=150, t_assento=t, b_solda=b)
              for t in (12.5, 16.0, 19.0) for h in (200, 250, 300, 400) for b in (6, 8, 10)]
    return _tentar("apoio_cantoneira", cands, esforcos, perfis)


def _sug_vv(perfis, esforcos):
    v, s_, _ = _perfis_de(perfis)
    vg, sp = _perfil(v, "perfil_viga"), _perfil(s_, "perfil_suporte")
    rec = recortes_viga_viga(vg, sp, 10 * MM)
    return _sug_cd(perfis, esforcos, "viga_viga_recorte",
                   dict(_rs=rec["recorte_sup_h"], _ri=rec["recorte_inf_h"]))


def _sug_sd(perfis, esforcos):
    v, s_, apoio = _perfis_de(perfis)
    cands = [dict(perfil_viga=v, perfil_suporte=s_, apoio=apoio, b_solda=b) for b in (3, 4, 5, 6, 8, 10)]
    return _tentar("solda_direta_alma", cands, esforcos, perfis)


def _governa(res):
    """Verificação de maior taxa de um resultado: "nome (taxa 0,96)" (G6: o motivo da troca cita o que governa).
    Onda final: resistências primeiro (kN, kN/m...); as disposições geométricas (mm: dimensão mínima da solda = 1,00 por
    definição) só se não houver resistência."""
    vs = [x for x in (res or {}).get("verificacoes") or [] if x.get("taxa") is not None]
    # W7-UPN: resultado que não passa cita o que reprova (ex.: "cantoneira interna dentro da mesa do U"), não a maior resistência
    v = max([x for x in vs if x.get("ok") is False] or [x for x in vs if x.get("unidade") not in ("mm", "m", "-")] or vs,
            key=lambda x: x["taxa"], default=None)
    return f"{v['nome']} (taxa {v['taxa']:.2f})".replace(".", ",") if v else "—"


def _cabe(res):
    """G6 r4: verificação "comprimento disponível na barra" (x_max) do resultado, ou None."""
    return next((v for v in res.get("verificacoes") or [] if v["nome"].startswith("geometria: comprimento disp")), None)


def _com_solda_direta(tipo, sug):
    """G5: viga U baixa demais para parafusos, ou parafusada que não passa → sugere solda_direta_alma se ela passa;
    a resposta traz `tipo_ligacao` e `motivo`, e o chamador passa a verificar esse tipo.
    G6 r4: o mesmo quando a parafusada (qualquer perfil) não cabe no `x_max` ou tem dados impossíveis (UPN 50)."""
    def solda(perfis, esforcos, motivo):
        c = _sug_sd(perfis, esforcos)
        if tipo == "viga_viga_recorte":
            c["apoio"] = "alma_viga"
        return dict(c, tipo_ligacao="solda_direta_alma", motivo=motivo)

    def f(perfis, esforcos):
        try:
            c = sug(perfis, esforcos)
        except ValueError as ex:
            if "baixo demais" not in str(ex):
                raise
            alt = solda(perfis, esforcos, "")
            dd = {k: v for k, v in alt.items() if k not in ("tipo_ligacao", "motivo")}
            try:   # o motivo cita a verificação que governa a solda direta (ex.: a solda, taxa 0,98), não só o perfil baixo
                gv = _governa(TIPOS["solda_direta_alma"]["verificar"](dd, esforcos))
            except ValueError:
                gv = "—"
            alt["motivo"] = f"{ex}: solda direta; governa: {gv}"
            return alt
        try:
            r1 = TIPOS[tipo]["verificar"](c, esforcos)
        except ValueError as ex:   # G6 r4: dados impossíveis (ex.: UPN 50: resistência ≤ 0) → solda direta
            alt = solda(perfis, esforcos, f"{tipo} impossível ({ex}): solda direta")
            dd = {k: v for k, v in alt.items() if k not in ("tipo_ligacao", "motivo")}
            try:
                return alt if TIPOS["solda_direta_alma"]["verificar"](dd, esforcos)["ok"] else c
            except ValueError:
                return c
        cb = _cabe(r1)
        if cb and not cb["ok"]:   # G6 r4: nem a compacta cabe na barra → solda direta (sem comprimento de chapa)
            alt = solda(perfis, esforcos, "")
            dd = {k: v for k, v in alt.items() if k not in ("tipo_ligacao", "motivo")}
            r2 = TIPOS["solda_direta_alma"]["verificar"](dd, esforcos)
            alt["motivo"] = (f"{tipo} não cabe na barra (precisa {cb['solicitante']:.0f} mm, disponível "
                             f"{cb['resistente']:.0f} mm): solda direta; governa: {_governa(r2)}")
            return alt if r2["ok"] else c
        try:
            if _perfil(_perfis_de(perfis)[0], "perfil_viga").get("forma") != "U" or r1["ok"]:
                return c
            alt = solda(perfis, esforcos, "")
            dd = {k: v for k, v in alt.items() if k not in ("tipo_ligacao", "motivo")}
            r2 = TIPOS["solda_direta_alma"]["verificar"](dd, esforcos)
            alt["motivo"] = (f"{tipo} não passa com viga U ({_governa(r1)}): solda direta; "
                             f"governa: {_governa(r2)}")
            return alt if r2["ok"] else c
        except ValueError:
            return c
    return f


# ---------------------------------------------------------------- registro (contrato §Registro)

def _alcance(g):
    """Maior x local (mm, ao longo da barra a partir do nó) de chapas, cantoneiras e parafusos da geometria."""
    xs = []
    for c in g.get("chapas") or []:
        o, u, n = c["origem"], c["u"], c["normal"]
        v = c.get("v") or _cruz(n, u)
        xs += [o[0] + u[0] * a + v[0] * b for a, b in c["contorno"]]
    for c in g.get("cantoneiras") or []:
        xs += [c["origem"][0], c["origem"][0] + c["eixo"][0] * c["comprimento"]]
    xs += [p["origem"][0] for p in g.get("parafusos") or []]
    return max(xs, default=0.0)


def _fn(tipo, f):
    def verificar(dados, esforcos):
        d = _ler(tipo, dados)
        rp = (dados or {}).get("_recortes_ponta") or {}
        if "recorte_sup_h" in d:   # G2: cope da ponta (pontas.py) entra nas verificações da viga recortada
            for k, v in rp.items():
                d[k] = max(d[k], v)
        else:
            d["_recortes_ponta"] = rp
        r = f(d, esforcos)
        if d.get("x_max", 0) > 0 and isinstance(r.get("geometria"), dict):   # G6 r3: a ligação cabe na barra?
            alc = _alcance(r["geometria"])
            ok = alc <= d["x_max"] / MM + 1e-6
            r["verificacoes"].append(dict(nome="geometria: comprimento disponível na barra", item="detalhamento",
                                          solicitante=round(alc, 1), resistente=round(d["x_max"] / MM, 1), unidade="mm",
                                          taxa=alc / (d["x_max"] / MM), ok=ok))
            r["ok"] = r["ok"] and ok
            r["taxa"] = max(r["taxa"], alc / (d["x_max"] / MM))
        return r
    verificar.__doc__ = f"{tipo}: verificar(dados, esforcos) -> resultado do contrato"
    return verificar


_LIGA_VP = ["viga-pilar-mesa", "viga-pilar-alma", "viga-viga"]
TIPOS = {
    "cantoneira_dupla_alma": dict(
        nome="Cantoneira dupla na alma", familia="flexivel", liga=_LIGA_VP, entradas=_ENT_CD, esforcos=["V", "N"],
        descricao="Duas cantoneiras na alma da viga (parafusadas ou soldadas) e parafusadas no apoio (mesa/alma do "
                  "pilar ou alma de viga), recortes opcionais. Pfeil 9.10.2; Gerdau LCPP/LCSP."),
    "chapa_simples_alma": dict(
        nome="Chapa simples (shear tab)", familia="flexivel", liga=_LIGA_VP, entradas=_ENT_CS, esforcos=["V", "N"],
        descricao="Chapa soldada no apoio e parafusada na alma da viga, uma linha vertical; grupo excêntrico pelo "
                  "centro instantâneo. Gerdau LCHS."),
    "chapa_extremidade_flexivel": dict(
        nome="Chapa de extremidade flexível", familia="flexivel", liga=["viga-pilar-mesa", "viga-pilar-alma",
                                                                        "viga-viga"],
        entradas=_ENT_CE, esforcos=["V", "N"],
        descricao="Chapa fina soldada na alma da viga (filete duplo) e parafusada no apoio em duas colunas. "
                  "Gerdau LCHE."),
    "apoio_cantoneira": dict(
        nome="Apoio em cantoneira / consolo", familia="flexivel", liga=["viga-pilar-mesa", "viga-pilar-alma"],
        entradas=_ENT_AP, esforcos=["V"],
        descricao="Viga apoiada sobre cantoneira (parafusada ou soldada) com cantoneira superior de estabilidade, ou "
                  "sobre consolo soldado enrijecido. Pfeil 9.5 e 9.10.6; NBR 5.7.3/5.7.4."),
    "viga_viga_recorte": dict(
        nome="Viga–viga com recorte", familia="flexivel", liga=["viga-viga"], entradas=_ENT_VV, esforcos=["V", "N"],
        descricao="Viga secundária na alma da principal, topos alinhados, recortes calculados (Gerdau 3.3), com "
                  "dupla cantoneira ou chapa simples; escoamento, flambagem local e rasgamento da viga recortada."),
    "solda_direta_alma": dict(
        nome="Solda direta", familia="flexivel", liga=_LIGA_VP, entradas=_ENT_SD, esforcos=["V", "N"],
        descricao="Viga soldada direto no apoio, sem chapa nem parafusos: dois filetes na alma e, com 'soldar as "
                  "mesas' (padrão no I), o contorno das mesas. NBR 8800 6.2.5 / tabela 10."),
}
for _t, _f, _s in (("cantoneira_dupla_alma", _cantoneira_dupla, _com_solda_direta("cantoneira_dupla_alma", _sug_cd)),
                   ("chapa_simples_alma", _chapa_simples, _com_solda_direta("chapa_simples_alma", _sug_cs)),
                   ("chapa_extremidade_flexivel", _chapa_extremidade, _sug_ce), ("apoio_cantoneira", _apoio, _sug_ap),
                   ("viga_viga_recorte", _viga_viga, _com_solda_direta("viga_viga_recorte", _sug_vv)),
                   ("solda_direta_alma", _solda_direta, _sug_sd)):
    TIPOS[_t]["verificar"] = _fn(_t, _f)
    TIPOS[_t]["sugerir"] = _s


L.registrar_pontas(TIPOS)   # G2: geometria["pontas"] em todo resultado
