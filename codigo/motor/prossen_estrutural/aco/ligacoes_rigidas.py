"""Ligações rígidas e emendas — NBR 8800:2008, cap. 5.7 e 6 (contrato: docs/v3/LIGACOES-CONTRATO.md).

Tipos: chapa de topo ajustada (flush) e estendida (com/sem enrijecedor), flange plate soldada ou
parafusada, painel de alma do pilar (com enrijecedores e chapa de reforço), emenda de viga e emenda
de pilar com talas parafusadas (com ou sem contato), emenda flangeada de viga e de pilar (I/H/U, W9-FLANGE-PERFIL.md).
Documentação: docs/v3/ligacoes/IMPL-RIGIDAS.md.

Entradas em mm, kN, kN·m (contrato); cálculo interno em m, kN e kN/m² como `aco.ligacoes`, cujas
primitivas são reutilizadas (parafuso, solda, chapa, bordas).
Convenção de sinais: N > 0 tração; M > 0 traciona a mesa superior (z+) da viga/pilar que chega.

Fontes (acesso 2026-10-20):
  [NBR]   ABNT NBR 8800:2008, 5.7 (forças localizadas), 6.2 (soldas), 6.3 (parafusos), 6.5 (elementos).
  [CBCA]  Baião Filho & Viana Silva, "Ligações para Estruturas de Aço", Gerdau, 7ª ed., 2018,
          cap. 6 (LMPA, p. 113–140) — ver docs/v3/ligacoes/CBCA.md §3.
  [Pfeil] W. Pfeil & M. Pfeil, "Estruturas de Aço — dimensionamento prático", 8ª ed., LTC, 9.10.1
          (emenda de viga, p. 253–255), 6.3.6 (forças localizadas, p. 180–183), 9.1 (emenda de pilar).
  [Kulak] Kulak, Fisher & Struik, "Guide to Design Criteria for Bolted and Riveted Joints", 2ª ed.,
          cap. 17 (efeito alavanca, Struik–de Back, eqs. 17.10–17.12, p. 279/294) — ver AISC-DG1.md §2.
"""
from __future__ import annotations

import math

from .. import materiais, secoes
from ..materiais import E_ACO
from ..modelo import MPa
from . import ligacoes as lg
from . import pontas as _pontas
from .ligacoes import GA1, GA2, MM
from .nbr8800 import _cortante

EDICAO = "NBR 8800:2008"
COBERTURA = [
    dict(norma="NBR 8800", edicao="2008", item="6.3.3 / 6.3.5", verificacao="chapa de topo ajustada e estendida: "
         "parafusos tracionados e chapa como T-stub (Struik–de Back), distribuição plástica Σn·T·h",
         limitacao="linhas internas junto à mesa comprimida só ao cortante; mesa do pilar como T-stub com largura "
                   "tributária (sem charneiras); enrijecedor da extensão só reduz b"),
    dict(norma="NBR 8800", edicao="2008", item="5.7.2 a 5.7.7 / 5.7.9", verificacao="pilar: flexão local da mesa, "
         "escoamento e enrugamento da alma, flambagem da alma, painel ao cisalhamento (5.7.7.1 com VRd da 5.4.3), "
         "chapas de reforço dos dois lados da alma (5.7.7.2) e enrijecedores (5.7.9)",
         limitacao="painel sem a parcela de deformação; na condição de 0,4·Npℓ usa a compressão do pilar (intenção da "
                   "5.7.7.1; ver ITENS-NORMA IN07); soldas de enrijecedor e reforço não dimensionadas"),
    dict(norma="NBR 8800", edicao="2008", item="6.1.5.2", verificacao="força de cálculo mínima de 45 kN "
         "(chapa de topo, flange plate, emendas): esforços escalados até max(√(V² + N²), M/braço) = 45 kN",
         limitacao="sem isenção (terças e travessas de fechamento não usam ligação rígida neste módulo)"),
    dict(norma="NBR 8800", edicao="2008", item="6.5.3 / 6.5.4 / 6.5.6", verificacao="flange plate soldada ou "
         "parafusada; emendas de viga e pilar com talas parafusadas (divisão do momento de Pfeil 9.1–9.3)",
         limitacao="emenda só de perfis iguais; talas internas da mesa não previstas; ligação por atrito não tratada"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.3 / 6.3.4.3 / 6.3.5 / 6.2.5 / 6.6.1", verificacao="emenda flangeada "
         "de viga e de pilar (I/H/U): chapas de topo dos dois lados (T-stub), cortante por grupo elástico, atrito "
         "opcional, soldas perfil–chapa e contato usinado (docs/v3/W9-FLANGE-PERFIL.md)",
         limitacao="compressão não alivia a tração dos parafusos; U dobrado: pernas na parede fina não verificadas; "
                   "torção do U (centro de cisalhamento) só em aviso"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.3 / 6.3.5 / 6.2.5 / 5.7", verificacao="chapa de topo ajustada com "
         "viga U (UPN, U, Ue): chapa = U + 5 mm, 2 parafusos dentro do U com T = M/h até a mesa comprimida, chapa como "
         "T-stub da EN 1993-1-8 no canto alma–mesa, contorno soldado (grupo elástico), apoio na mesa do pilar I/H ou nas "
         "costas da alma do pilar U (docs/v3/W11-CHAPA-TOPO-U.md)",
         limitacao="torção da barra (centro de cisalhamento) só em aviso; alma do pilar U à compressão pela face de tubo "
                   "retangular (EN tab. 7.11, kn = 1); sem enrijecedores do pilar; U dobrado: pernas na parede fina não "
                   "verificadas"),
]
_PERFIS_PADRAO = "A-572 GR.50"


# ---------------------------------------------------------------- componentes (funções puras)

def tstub(T, p, t, b, a, d, dh, fy, FtRd):
    """T-stub com efeito alavanca, modelo de Struik & de Back (Kulak cap. 17, eqs. 17.10–17.12),
    com resistências da NBR 8800 (Mp = p·t²/4·fy/γa1; Ft,Rd de 6.3.3.1). Unidades m, kN.
    T: tração de cálculo por parafuso; p: largura tributária por parafuso; b: furo → face da "alma";
    a: furo → borda (limitado a 1,25b); d: diâmetro; dh: furo.
    Plástico: T·b' = Mp(1 + δα), Q·a' = αδMp, B = T + Q (0 ≤ α ≤ 1)."""
    b1 = b - d / 2
    if b1 <= 0 or p <= 0 or t <= 0:
        raise ValueError(f"T-stub: b = {b / MM:.1f} mm ≤ d/2 ou p, t ≤ 0 — geometria impossível")
    a1 = min(a, 1.25 * b) + d / 2
    delta = max(1 - dh / p, 0.0)
    Mp = p * t ** 2 / 4 * fy / GA1
    alfa = (T * b1 / Mp - 1) / delta if delta > 0 else (0.0 if T * b1 <= Mp else math.inf)
    Q = 0.0 if alfa <= 0 else min(alfa, 1.0) * delta * Mp / a1
    # capacidade por parafuso: menor entre chapa (α = 1) e parafuso com Q (B = Ft,Rd)
    T_chapa = Mp * (1 + delta) / b1
    T_paraf = FtRd if FtRd * b1 <= Mp else (FtRd * a1 + Mp) / (a1 + b1)
    return dict(b1=b1, a1=a1, delta=delta, Mp=Mp, alfa=alfa, Q=Q, B=T + Q,
                T_chapa=T_chapa, T_parafuso=T_paraf, Tcap=min(T_chapa, T_paraf))


def alfa_en(l1, l2):
    """α da EN 1993-1-8 fig. 6.11 (linha junto a enrijecedor), pela aproximação analítica de Jaspart (1991) das curvas:
    λ1,lim = 1,25/(α − 2,75); λ2,lim = α·λ1,lim/2; λ2 = λ2,lim + (1 − λ2,lim)·((λ1,lim − λ1)/λ1,lim)^(α/√2) para
    λ1 < λ1,lim. Devolve o maior α em [4,45; 8] cuja curva contém o ponto (λ1, λ2); None se nem a de 4,45 contém
    (enrijecedor longe demais: não conta)."""
    def dentro(a):
        l1l = 1.25 / (a - 2.75)
        if l1 >= l1l:
            return False
        l2l = a * l1l / 2
        return l2 <= l2l + (1 - l2l) * ((l1l - l1) / l1l) ** (a / math.sqrt(2)) + 1e-12
    if dentro(8.0):
        return 8.0
    if not dentro(4.45):
        return None
    lo, hi = 4.45, 8.0
    for _ in range(50):
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if dentro(mid) else (lo, mid)
    return lo


def leff_junto_enrijecedor(m, m2, e):
    """Comprimento efetivo da mesa do pilar com a linha junto a um enrijecedor (EN 1993-1-8 tab. 6.4, "linha adjacente
    a enrijecedor", linha isolada): leff = min(2π·m, α·m). m e m2 como na EN fig. 6.8: m = furo → face da alma − 0,8·r;
    m2 = furo → face do enrijecedor − 0,8·a·√2 (a = garganta da solda); e = furo → borda livre (m). None sem α."""
    a = alfa_en(m / (m + e), m2 / (m + e))
    return None if a is None else min(2 * math.pi * m, a * m)


def tstub_en(T, leff, t, m, e, fy, FtRd):
    """T-stub da EN 1993-1-8 6.2.4 (tab. 6.2), POR PARAFUSO (a linha tem 2): Mpl = 0,25·leff·t²·fy/γa1;
    modo 1 = 2·Mpl/m; modo 2 = (Mpl + n·Ft,Rd)/(m + n), n = min(e; 1,25m); modo 3 = Ft,Rd.
    B (força no parafuso, para a verificação da alavanca) = T·Ft,Rd/min(modo 2; modo 3): a EN cobre a alavanca pelos
    modos 2 e 3 — B ≤ Ft,Rd equivale a T ≤ min(modo 2, modo 3). Mesmas chaves de tstub."""
    n = min(e, 1.25 * m)
    Mpl = 0.25 * leff * t ** 2 * fy / GA1
    m1, m2_, m3 = 2 * Mpl / m, (Mpl + n * FtRd) / (m + n), FtRd
    lim_b = min(m2_, m3)
    return dict(b1=m, a1=n, delta=1.0, Mp=Mpl, alfa=0.0, Q=T * FtRd / lim_b - T, B=T * FtRd / lim_b,
                T_chapa=m1, T_parafuso=lim_b, Tcap=min(m1, lim_b), en=True)


A_SOLDA_ENR = 0.005   # m, garganta da solda do enrijecedor do pilar (ponytail: solda não dimensionada; a ≈ 5 mm)


def momento_efetivo(M, N, braco):
    """Momento nos parafusos tracionados com tração axial: M + N⁺·braço/2 (N dividida entre as mesas,
    CBCA 6.4 caso 2: Nd = 2·(0,2·Mmax)/(H − tf) equivale a 0,2·Mmax)."""
    return abs(M) + max(N, 0.0) * braco / 2


def momento_resistente_linhas(linhas):
    """Distribuição plástica (DG4 2ª ed.: Mnp = Σ n·Pt·hi): Σ n_i·Tcap_i·h_i."""
    return sum(L["n"] * L["Tcap"] * L["h"] for L in linhas)


def divisao_momento(M, Af, Aw):
    """Pfeil eqs. 9.1–9.3: a alma absorve M·(Aw/6)/(Af + Aw/6); as mesas o restante."""
    Mw = M * (Aw / 6) / (Af + Aw / 6)
    return M - Mw, Mw


def grupo_excentrico(xs, ys, Vy, Vx, M):
    """Método elástico do disco rígido (Pfeil eqs. 3.9–3.12): força máxima num parafuso.
    Vy paralela a y (cortante), Vx paralela a x (axial), M no plano. Retorna (F, Σr²)."""
    n = len(xs)
    cx, cy = sum(xs) / n, sum(ys) / n
    r2 = sum((x - cx) ** 2 + (y - cy) ** 2 for x, y in zip(xs, ys))
    F = 0.0
    for x, y in zip(xs, ys):
        fx = Vx / n + (M * (y - cy) / r2 if r2 else 0.0)
        fy_ = Vy / n + (M * (x - cx) / r2 if r2 else 0.0)
        F = max(F, math.hypot(fx, fy_), math.hypot(Vx / n - (M * (y - cy) / r2 if r2 else 0.0),
                                                    Vy / n - (M * (x - cx) / r2 if r2 else 0.0)))
    return F, r2


def chi_compressao(KL, r, fy):
    """NBR 8800 5.3.3 (Q = 1): χ = 0,658^λ0² (λ0 ≤ 1,5) ou 0,877/λ0²."""
    l0 = KL / r * math.sqrt(fy / E_ACO) / math.pi
    return 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2


def compressao_chapa(Ag, KL, r, fy):
    """NBR 8800 6.5.4: KL/r ≤ 25 → Ag·fy/γa1; senão pela 5.3 (χ, Q = 1)."""
    return Ag * fy / GA1 if KL / r <= 25 else chi_compressao(KL, r, fy) * Ag * fy / GA1


def ct_soldas_longitudinais(Lw, largura):
    """NBR 8800 5.2.5 c) (Pfeil eq. 2.8): chapa só com filetes longitudinais."""
    return 1.0 if Lw >= 2 * largura else 0.87 if Lw >= 1.5 * largura else 0.75


# NBR 8800:2008 5.7 — forças localizadas no pilar (numeração conferida no texto: docs/v3/ITENS-NORMA.md §2)
def flexao_local_mesa(tf, fy, extremidade=False):
    return (0.5 if extremidade else 1.0) * 6.25 * tf ** 2 * fy / GA1                       # 5.7.2


def escoamento_local_alma(tw, k, ln, fy, extremidade=False):
    return 1.10 * ((2.5 if extremidade else 5.0) * k + ln) * fy * tw / GA1                # 5.7.3


def enrugamento_alma(tw, tf, d, ln, fy, extremidade=False):
    r = math.sqrt(E_ACO * fy * tf / tw)                                                    # 5.7.4
    if not extremidade:
        return 0.66 * tw ** 2 * (1 + 3 * (ln / d) * (tw / tf) ** 1.5) * r / GA1
    if ln / d <= 0.2:
        return 0.33 * tw ** 2 * (1 + 3 * (ln / d) * (tw / tf) ** 1.5) * r / GA1
    return 0.33 * tw ** 2 * (1 + (4 * ln / d - 0.2) * (tw / tf) ** 1.5) * r / GA1


def flambagem_alma_compressao(tw, h, fy, extremidade=False):
    return (0.5 if extremidade else 1.0) * 24 * tw ** 3 * math.sqrt(E_ACO * fy) / (h * GA1)  # 5.7.6


def cisalhamento_painel(dc, tw, fy, Nc=0.0, Ny=math.inf, h=None, a=None):
    """5.7.7.1 (sem considerar a deformação do painel): VRd da 5.4.3.1.1 com Aw = dc·tw, λ = h/tw e kv com
    a = distância entre as mesas da viga (sem h: alma compacta, VRd = 0,6·fy·dc·tw/γa1); × (1,4 − Nc/Ny) se
    Nc > 0,4·Ny. Nc é a compressão do pilar (o texto de 2008 escreve FSd; AISC J10.6 e PR 2022: ITENS-NORMA IN07)."""
    f = 1.0 if Nc <= 0.4 * Ny else 1.4 - Nc / Ny
    if h is None:
        return 0.6 * fy * dc * tw * f / GA1
    lam = h / tw
    kv = 5.0 if a is None or a / h > 3 or a / h > (260 / lam) ** 2 else 5 + 5 / (a / h) ** 2
    return _cortante(dc * tw, lam, kv, E_ACO, fy, "5.4.3.1.1")["VRd"] * f


# ---------------------------------------------------------------- entradas

def _ent(rotulo, unidade, padrao, minimo=None, maximo=None, opcoes=None, descricao=""):
    e = {"rotulo": rotulo, "unidade": unidade, "padrao": padrao}
    if descricao:
        e["ajuda"] = descricao   # texto longo; "descricao" do campo é o rótulo (ligacoes.tipos_ligacao)
    if minimo is not None:
        e["min"] = minimo
    if maximo is not None:
        e["max"] = maximo
    if opcoes is not None:
        e["opcoes"] = list(opcoes)
    return e


_ACOS = list(materiais.ACOS)
_PARAFUSOS = list(lg.PARAFUSOS)


_E_PAR = {"d_mm": _ent("Diâmetro do parafuso", "mm", 19.05, 12.0, 40.0),
          "tipo_parafuso": _ent("Tipo de parafuso", "-", "A325", opcoes=_PARAFUSOS),
          "rosca_no_plano": _ent("Rosca no plano de corte", "-", True)}
_E_PILAR = {
    "perfil_pilar": _ent("Perfil do pilar", "-", "", descricao="nome do catálogo ou {d, bf, tw, tf, r} em mm; "
                         "vazio = pilar não verificado"),
    "aco_pilar": _ent("Aço do pilar", "-", _PERFIS_PADRAO, opcoes=_ACOS),
    "t_enr_pilar": _ent("Enrijecedor do pilar (t)", "mm", 0.0, 0.0, 50.0,
                        descricao="espessura dos enrijecedores de continuidade do pilar; 0 = sem"),
    "t_reforco": _ent("Chapa de reforço da alma", "mm", 0.0, 0.0, 50.0,
                      descricao="espessura de cada chapa de reforço (doubler), uma de cada lado da alma (5.7.7.2); 0 = sem"),
    "M_outra": _ent("Momento da viga oposta", "kN·m", 0.0, 0.0, 1e5,
                    descricao="momento da viga do outro lado no mesmo sentido (soma no painel); 0 = unilateral"),
    "V_pilar": _ent("Cortante do pilar", "kN", 0.0, 0.0, 1e5, descricao="cortante do pilar junto ao nó (alivia o painel)"),
    "N_pilar": _ent("Compressão no pilar", "kN", 0.0, 0.0, 1e6, descricao="força de compressão do pilar (5.7.7)"),
    "topo_pilar": _ent("Nó no topo do pilar", "-", False,
                       descricao="força a menos de d do topo (reduções de 5.7.2 a 5.7.6)"),
}
_E_VIGA = {"perfil_viga": _ent("Perfil da viga", "-", None, descricao="nome do catálogo ou {d, bf, tw, tf, r} em mm"),
           "aco_viga": _ent("Aço da viga", "-", _PERFIS_PADRAO, opcoes=_ACOS)}
_E_CHAPA = {"aco_chapa": _ent("Aço das chapas", "-", "A-36", opcoes=_ACOS),
            "eletrodo": _ent("Eletrodo", "-", "E70", opcoes=list(lg.ELETRODOS)),
            "borda_laminada": _ent("Bordas laminadas/maçarico", "-", False,
                                   descricao="bordas laminadas ou cortadas a maçarico (tabela 14); senão serra/tesoura")}


def _ler(tipo, dados):
    ents = TIPOS[tipo]["entradas"]
    dados = dict(dados or {})
    d, erros = {}, []
    for nome, ent in ents.items():
        v = dados.get(nome, ent["padrao"])
        if nome.startswith("perfil_"):
            if v is None:
                erros.append(f"{nome}: obrigatório")
            d[nome] = v
            continue
        if v is None:
            erros.append(f"{nome}: obrigatório")
            continue
        if isinstance(ent["padrao"], bool):   # W8-RIG: sim/não sem "opcoes" (como os outros módulos); "false" ≠ True
            d[nome] = v if isinstance(v, bool) else str(v).strip().lower() in ("1", "true", "sim", "s")
            continue
        if "opcoes" in ent:
            ops = ent["opcoes"]
            if str(v).upper() not in [str(o).upper() for o in ops]:
                erros.append(f"{nome}: '{v}' inválido ({', '.join(map(str, ops))})")
            else:
                d[nome] = next(o for o in ops if str(o).upper() == str(v).upper())
            continue
        try:
            x = float(v)
        except (TypeError, ValueError):
            erros.append(f"{nome}: número esperado, recebido {v!r}")
            continue
        if not math.isfinite(x) or not ent.get("min", -math.inf) <= x <= ent.get("max", math.inf):
            erros.append(f"{nome}: {x:g} fora da faixa [{ent.get('min', '-')}, {ent.get('max', '-')}]")
            continue
        inteiro = isinstance(ent["padrao"], int) and not isinstance(ent["padrao"], bool)
        if inteiro and x != int(x):
            erros.append(f"{nome}: inteiro esperado")
            continue
        d[nome] = int(x) if inteiro else x
    if erros:
        raise ValueError(f"ligação '{tipo}': " + "; ".join(erros))
    return d


def _esforcos(esforcos, avisos, usados=("N", "V", "M")):
    e = {}
    for k in ("N", "V", "Vz", "M", "Mz", "T"):
        v = (esforcos or {}).get(k, 0.0) or 0.0
        try:
            x = float(v)
        except (TypeError, ValueError):
            raise ValueError(f"esforço {k}: número esperado, recebido {v!r}") from None
        if not math.isfinite(x):
            raise ValueError(f"esforço {k}: número finito esperado")
        e[k] = x
    ign = [k for k in ("Vz", "Mz", "T") if k not in usados and abs(e[k]) > 1e-9]
    if ign:
        avisos.append(f"esforços {', '.join(ign)} não considerados por esta ligação (plano forte apenas)")
    return e


def _minimo_6152(e, braco, avisos, isenta=False):
    """NBR 8800 6.1.5.2: força de cálculo da ligação = max(√(V² + N²), |M|/braço); abaixo de 45 kN os
    esforços são escalados até 45 kN (sem esforço: V = 45 kN). Altera e no lugar. isenta (W11, campo
    terca_ou_travessa da chapa de topo ajustada): terças e travessas de fechamento, exceção da 6.1.5.2."""
    if isenta:
        avisos.append("terça/travessa de fechamento: isenta do mínimo de 45 kN (NBR 8800 6.1.5.2)")
        return
    k, aviso = lg.fator_minimo_6152(max(math.hypot(e["V"], e["N"]), abs(e["M"]) / braco))
    if not aviso:
        return
    if math.isfinite(k):
        for c in ("N", "V", "M"):
            e[c] *= k
    else:
        e["V"] = lg.F_MINIMA
    avisos.append(aviso)


U_RIGIDA = "ligação rígida com perfil U não suportada"


def _perfil(v, campo):
    """Catálogo (W/HP Gerdau) ou dict {d, bf, tw, tf, r} em mm → props em m."""
    if isinstance(v, dict):
        try:
            s = secoes.secao_I(*(float(v[k]) * MM for k in ("d", "bf", "tw", "tf")), float(v.get("r", 0)) * MM,
                               nome=v.get("nome"))
        except (KeyError, TypeError, ValueError) as ex:
            raise ValueError(f"{campo}: dict precisa de d, bf, tw, tf (mm) — {ex}") from None
    else:
        try:
            s = secoes.catalogo(str(v))
        except KeyError as ex:
            raise ValueError(f"{campo}: {ex.args[0]}") from None
    p = s.props
    if p.get("tipo") in ("U", "Ue"):   # G5: sem tipo rígido para U (docs/v3/LIGACOES-U.md)
        raise ValueError(f"{campo}: {U_RIGIDA} ({s.nome}) — para U use a solda direta (perfil soldado no apoio) ou uma "
                         "ligação flexível (chapa simples, cantoneiras)")
    if p.get("tipo") != "I":
        raise ValueError(f"{campo}: só perfis I/H (W, HP, soldados) — recebido {s.nome}")
    return dict(p, nome=s.nome)


def _aco(nome):
    m = materiais.aco(nome)
    return m.fy, m.fu


def _par(d):
    """Parafuso via primitiva de ligacoes: (db, fub, alta, dh, FtRd, FvRd por plano, avisos)."""
    db, fub, alta, dh, av = lg._parafuso(dict(d_mm=d["d_mm"], tipo_parafuso=d["tipo_parafuso"]))
    return db, fub, alta, dh, lg.tracao_parafuso(db, fub), \
        lg.cisalhamento_parafuso(db, fub, d["rosca_no_plano"], alta), av


# ---------------------------------------------------------------- resultado

class _R:
    """Acumula verificações (contrato) e avisos."""

    def __init__(self):
        self.v, self.avisos = [], []

    def res(self, nome, item, sd, rd, unidade="kN"):
        if not (math.isfinite(rd) and rd > 0):
            raise ValueError(f"{nome}: resistência {rd:g} ≤ 0 — geometria ou dados inválidos")
        t = float(abs(sd) / rd)
        self.v.append(dict(nome=nome, item=item if item.startswith(("CBCA", "AISC", "Pfeil")) else f"NBR 8800 {item}", solicitante=round(float(abs(sd)), 4),
                           resistente=round(float(rd), 4), unidade=unidade, taxa=round(t, 4), ok=t <= 1 + 1e-9))

    def minimo(self, nome, item, minimo, real, parametro=None):   # detalhamento, em mm
        self.res(nome, item, minimo / MM, max(real, 1e-9) / MM, "mm")
        lg._dica(self.v[-1], "sobe", parametro)

    def maximo(self, nome, item, real, maximo, parametro=None):
        self.res(nome, item, real / MM, maximo / MM, "mm")
        lg._dica(self.v[-1], "desce", parametro)

    def resultado(self, geometria):
        pior = max(self.v, key=lambda x: x["taxa"]) if self.v else None
        taxa = pior["taxa"] if pior else 0.0
        return dict(ok=all(x["ok"] for x in self.v), taxa=taxa, criterio=pior["nome"] if pior else "",
                    verificacoes=self.v, avisos=self.avisos, geometria=geometria)


def _bordas(R, db, t, borda, nome, laminada=False):
    R.minimo(f"borda mínima ({nome})", "6.3.11.1 / tabela 14", lg.distancia_min_borda(db, laminada), borda)
    R.maximo(f"borda máxima ({nome})", "6.3.12", borda, min(12 * t, 0.150))


def _espacamento(R, s, db, t, nome):
    R.minimo(f"espaçamento mínimo 2,7db ({nome})", "6.3.9", 2.7 * db, s)
    R.maximo(f"espaçamento máximo 24t ≤ 300 mm ({nome})", "6.3.10", s, min(24 * t, 0.300))


def _filete(R, b, tmin, nome, parametro):
    R.minimo(f"solda {nome}: perna mínima", "6.2.6.2.1 / tabela 10", lg.filete_minimo(tmin), b, parametro)
    R.maximo(f"solda {nome}: perna máxima", "6.2.6.2.2", b, lg.filete_maximo(tmin), parametro)


def _mm(v):
    return round(v / MM, 3)


def _chapa(nome, contorno, origem, normal, u, t):
    return dict(nome=nome, contorno=[[_mm(a), _mm(b)] for a, b in contorno], origem=[_mm(c) for c in origem],
                normal=list(normal), u=list(u), espessura=_mm(t))


def _paraf(d, L, origem, eixo):
    return dict(d=_mm(d), comprimento=_mm(L), origem=[_mm(c) for c in origem], eixo=list(eixo))


def _solda(tipo, perna, pts):
    return dict(tipo=tipo, perna=_mm(perna), pontos=[[_mm(c) for c in p] for p in pts])


def _comprimento_parafuso(pega, db):
    # ponytail: pega + porca (≈ 0,9d) + arruela (4 mm) + 3 fios; arredondado a 5 mm — catálogo de comprimentos depois
    return math.ceil((pega + 0.9 * db + 0.004 + 0.006) / 0.005) * 0.005


# ---------------------------------------------------------------- pilar: forças localizadas e painel

def _pilar(R, d, pil, fyc, Ft, Fc, ln, bfb, tfb, H, M, soldada, geo, x_face):
    """NBR 8800 5.7: forças das mesas da viga no pilar (Ft tração, Fc compressão), painel de alma,
    enrijecedores de continuidade (5.7.9) e chapas de reforço (5.7.7.2). Acrescenta a geometria."""
    dc, bfc, twc, tfc, rc = pil["h"], pil["b"], pil["tw"], pil["tf"], pil.get("r", 0.0)
    k = tfc + rc
    hc = dc - 2 * k
    ext = d["topo_pilar"]
    ts, td = d["t_enr_pilar"] * MM, d["t_reforco"] * MM
    tr2 = 2 * td                                       # 5.7.7.2: chapas dos dois lados da alma
    bilateral = d["M_outra"] > 0
    loc = []
    if soldada and Ft > 0:
        loc.append(("flexão local da mesa do pilar (tração)", "5.7.2", Ft, flexao_local_mesa(tfc, fyc, ext)))
    for F, lado in ((Ft, "tração"), (Fc, "compressão")):
        if F > 0:
            loc.append((f"escoamento local da alma do pilar ({lado})", "5.7.3", F,
                        escoamento_local_alma(twc + tr2, k, ln, fyc, ext)))
    if Fc > 0:
        loc.append(("enrugamento da alma do pilar", "5.7.4", Fc, enrugamento_alma(twc + tr2, tfc, dc, ln, fyc, ext)))
        if bilateral:
            loc.append(("flambagem da alma do pilar por compressão", "5.7.6", Fc,
                        flambagem_alma_compressao(twc + tr2, hc, fyc, ext)))
    if ts <= 0:
        for nome, item, F, Rd in loc:
            R.res(nome, item, F, Rd)
    else:
        bs = (bfc - twc) / 2 - 0.010                  # ponytail: recorte de canto 10 mm fixo
        Lc = 2 * (bs - 0.010)                          # área líquida no contato (chanfro do canto)
        for F, lado in ((Ft, "tração"), (Fc, "compressão")):
            Rmin = min([Rd for _n, _i, FF, Rd in loc if FF == F] or [math.inf])
            Fs = max(F - Rmin, 0.0) if math.isfinite(Rmin) else F
            if lado == "tração":
                Rs = Lc * ts * fyc / GA1
            else:  # 5.7.9.2 a 5.7.9.4: coluna com faixa de alma 25tw (12tw no topo), KL = 0,75h
                faixa = (12 if ext else 25) * twc
                A = 2 * bs * ts + faixa * twc
                Ist = ts * (2 * bs + twc) ** 3 / 12
                Rs = min(Lc * ts * fyc / GA1, compressao_chapa(A, 0.75 * hc, math.sqrt(Ist / A), fyc))
            R.res(f"enrijecedores do pilar ({lado}): F − Rmín(5.7.2 a 5.7.6)",
                  "5.7.9.1" if lado == "tração" else "5.7.9.2 a 5.7.9.4", Fs, Rs)
        # Q = 1 na coluna do enrijecedor (compressao_chapa) exige b/t ≤ 0,56√(E/fy); 5.7.9.5 exige t ≥ b/15
        R.maximo("enrijecedor do pilar: b/t ≤ 0,56√(E/fy)", "5.7.9 / tabela F.1 (grupo 3)", bs / ts * MM,
                 0.56 * math.sqrt(E_ACO / fyc) * MM)
        R.minimo("enrijecedor do pilar: t ≥ b/15", "5.7.9.5", bs / 15, ts)
        R.minimo("enrijecedor do pilar: t ≥ tf da viga / 2", "5.7.9.5", tfb / 2, ts)
        R.minimo("enrijecedor do pilar: b + tw/2 ≥ bf da viga / 3", "5.7.9.5", bfb / 3, bs + twc / 2)
        for zf in (H / 2 - tfb / 2, -(H / 2 - tfb / 2)):
            for s in (1, -1):
                xc = x_face - dc / 2
                geo["chapas"].append(_chapa(
                    "enrijecedor do pilar", [[-(dc / 2 - tfc), s * twc / 2], [dc / 2 - tfc, s * twc / 2],
                                             [dc / 2 - tfc, s * bfc / 2], [-(dc / 2 - tfc), s * bfc / 2]],
                    [xc, 0, zf - ts / 2], [0, 0, 1], [1, 0, 0], ts))
                bw_s = max(lg.filete_minimo(min(ts, twc)), 0.005)   # ponytail: filete mínimo, solda não dimensionada
                geo["soldas"] += [_solda("filete", bw_s, [[xc - (dc / 2 - tfc), s * twc / 2, zf],
                                                           [xc + dc / 2 - tfc, s * twc / 2, zf]]),
                                  _solda("filete", bw_s, [[xc + dc / 2 - tfc, s * twc / 2, zf],
                                                           [xc + dc / 2 - tfc, s * bfc / 2, zf]]),
                                  _solda("filete", bw_s, [[xc - (dc / 2 - tfc), s * twc / 2, zf],
                                                           [xc - (dc / 2 - tfc), s * bfc / 2, zf]])]
    # painel de alma (5.7.7.1); demanda = Σ forças das mesas − cortante do pilar
    Vp = abs(M) / (H - tfb) + d["M_outra"] / (H - tfb) - d["V_pilar"]
    Ny = pil["A"] * fyc
    a = H - tfb                                        # 5.7.7.1: a = distância entre as mesas da viga
    VRd = cisalhamento_painel(dc, twc, fyc, d["N_pilar"], Ny, hc, a)
    if td > 0:   # 5.7.7.2: duas chapas (dos dois lados da alma) dimensionadas pela 5.4 (λ = h/t de cada chapa)
        hp = dc - 2 * tfc
        VRd += 2 * cisalhamento_painel(hp, td, fyc, d["N_pilar"], Ny, hp, a)
    R.res("cisalhamento do painel de alma do pilar" + (" (com chapas de reforço)" if td else ""),
          "5.7.7.1 / 5.4.3" + (" / 5.7.7.2" if td else ""), abs(Vp), VRd)   # V_pilar > ΣF mesas inverte o painel
    if td > 0:
        # 5.7.7.2: estendidas 150 mm além das forças das mesas. A flambagem da chapa entra pela 5.4.3; o limite
        # (dz + wz)/t ≤ 90 do AISC 341 não é da NBR e saiu
        zr = H / 2 - tfb / 2 + 0.150
        for s in (1, -1):
            geo["chapas"].append(_chapa("chapa de reforço da alma",
                                        [[-(dc / 2 - tfc), -zr], [dc / 2 - tfc, -zr], [dc / 2 - tfc, zr],
                                         [-(dc / 2 - tfc), zr]], [x_face - dc / 2, s * twc / 2, 0], [0, s, 0],
                                        [1, 0, 0], td))
        R.avisos.append("chapas de reforço (5.7.7.2): uma de cada lado da alma, estendidas 150 mm além das mesas da "
                        "viga; soldar às mesas do pilar para desenvolver a parcela da força cortante de cada chapa "
                        "(solda não dimensionada)")
        if ext:
            R.avisos.append("nó no topo do pilar: confira se cabe a extensão de 150 mm das chapas de reforço (5.7.7.2)")


def _pilar_props(d, R):
    if not d.get("perfil_pilar"):
        R.avisos.append("pilar não informado: mesa, alma e painel do pilar NÃO verificados (5.7)")
        return None, None, None
    pil = _perfil(d["perfil_pilar"], "perfil_pilar")
    fyc, fuc = _aco(d["aco_pilar"])
    return pil, fyc, fuc


# ---------------------------------------------------------------- chapa de topo (ajustada e estendida)

_PF_MIN = ((19.05, 0.030), (22.225, 0.035), (25.4, 0.040))   # CBCA 6.1.1: furo → face da mesa


def _linhas_chapa(H, tf, tw, pf, de, g, bp, estendida, sobra, db, enr, ts, nx=0, sl=0.075):
    """Linhas de parafusos (2 por linha, gabarito g): z a partir do eixo da viga; tipo 'ext' (fora da mesa)
    ou 'int' (entre as mesas). Retorna (linhas, z_base, z_topo da chapa)."""
    linhas = []
    for s in (1, -1):
        lado_ext = estendida == "ambas" or (estendida == "superior" and s == 1) or (estendida == "inferior" and s == -1)
        if lado_ext:
            linhas.append(dict(z=s * (H / 2 + pf), tipo="ext", lado=s))
        linhas.append(dict(z=s * (H / 2 - tf - pf), tipo="int", lado=s))
        linhas += [dict(z=s * (H / 2 - tf - pf - k * sl), tipo="alma", lado=s) for k in range(1, nx + 1)]
    zt = H / 2 + (pf + de if estendida in ("superior", "ambas") else sobra)
    zb = -(H / 2 + (pf + de if estendida in ("ambas", "inferior") else sobra))
    for L in linhas:
        L["n"] = 2
        if L["tipo"] == "ext":
            L["b"] = min(pf, (g - ts) / 2) if enr else pf
            L["a"] = (zt if L["lado"] == 1 else -zb) - abs(L["z"])
        elif L["tipo"] == "int":
            L["b"] = min(pf, (g - tw) / 2)
            L["a"] = (bp - g) / 2 if L["b"] < pf else 1.25 * L["b"]
        else:   # linha só junto à alma: T-stub com "alma" = alma da viga
            L["b"], L["a"] = (g - tw) / 2, (bp - g) / 2
        L["p"] = bp / 2 if L["tipo"] != "alma" else min(sl, bp / 2)
    return linhas, zb, zt


def _chapa_topo(d, e, estendida_padrao):
    R = _R()
    av = R.avisos
    vg = _perfil(d["perfil_viga"], "perfil_viga")
    H, bf, tw, tf = vg["h"], vg["b"], vg["tw"], vg["tf"]
    fyb, fub_v = _aco(d["aco_viga"])
    fyp, fup = _aco(d["aco_chapa"])
    db, fub, alta, dh, FtRd, FvRd, a0 = _par(d)
    av += a0
    dn = dh + lg.FURO_DANO
    tp = d["tp"] * MM
    bp = (d["bp"] or (bf / MM + 25)) * MM
    g = (d["g"] or max(round(bf / MM / 2 / 5) * 5, 2.7 * db / MM + tw / MM)) * MM
    pf_min = next((p for dd, p in _PF_MIN if d["d_mm"] <= dd + 0.05), 1.6 * db)
    pf = d["pf"] * MM if d["pf"] else pf_min
    de = d.get("de", 0.0) * MM
    estendida = d.get("estendida", estendida_padrao)
    enr = bool(d.get("enrijecedor_chapa")) and estendida != "nao"
    ts = (d.get("t_enrijecedor") or 0) * MM
    if enr and ts <= 0:
        raise ValueError("t_enrijecedor: obrigatório (> 0) com enrijecedor_chapa")
    e = _esforcos(e, av, usados=("N", "V", "Vz", "M", "Mz", "T"))
    _minimo_6152(e, H - tf, av, d.get("terca_ou_travessa", False))
    M, N, V = e["M"], e["N"], e["V"]
    # fora do plano forte (docs/v3/LIGACOES-ESFORCOS-3D.md): Vh = cortante horizontal (eixo y), Mz = momento no eixo
    # fraco (vertical), Tx = torção (eixo da viga)
    Vh, Mz, Tx = e["Vz"], e["Mz"], e["T"]
    tem3d = any(abs(x) > 1e-9 for x in (Vh, Mz, Tx))
    if bp > bf + 0.025 + 1e-9:
        av.append(f"bp = {bp / MM:.0f} mm > bf + 25 mm: largura efetiva do T-stub limitada a {(bf + 0.025) / MM:.0f} mm "
                  "(CBCA 6.1.1)")
    bp_ef = min(bp, bf + 0.025)
    nx, sl = d["n_linhas_extras"], d["s_linhas"] * MM
    if nx and H / 2 - tf - pf - nx * sl < 0:
        raise ValueError(f"n_linhas_extras = {nx}: as linhas passam do meio da alma (H/2 − tf − pf − n·s < 0)")
    linhas, zb, zt = _linhas_chapa(H, tf, tw, pf, de, g, bp_ef, estendida, d["sobra"] * MM, db, enr, ts, nx, sl)
    if (estendida == "superior" and M < 0) or (estendida == "inferior" and M > 0):
        av.append(f"M {'<' if M < 0 else '>'} 0 traciona a mesa {'inferior' if M < 0 else 'superior'}, lado não estendido da "
                  "chapa assimétrica (CBCA 6.1: com inversão de momento usar chapa estendida nas duas mesas)")
    s_t = 1 if M >= 0 else -1
    zc = -s_t * (H / 2 - tf / 2)                       # centro da mesa comprimida
    tr = [L for L in linhas if L["z"] * s_t > 0]
    braco = H - tf
    # tração axial com N/2 > |M|/braço: a mesa "comprimida" também traciona (sem contato). Cada mesa recebe
    # N/2 ± |M|/braço (ΣF = N, ΣM = M) e as suas linhas precisam resistir a essa força (6.3.3.1 / 6.3.5)
    F_op = N / 2 - abs(M) / braco
    co = [L for L in linhas if L["z"] * s_t < 0] if N > 0 and F_op > 1e-9 else []
    pil, fyc, fuc = _pilar_props(d, R)
    for L in tr + co:
        L["h"] = abs(L["z"] - zc)
        L["chapa"] = tstub(0.0, L["p"], tp, L["b"], L["a"], db, dh, fyp, FtRd)
        L["Tcap"] = L["chapa"]["Tcap"]
        if pil is not None:   # mesa do pilar como T-stub: b até a alma, a até a borda, p tributário
            bc = (g - pil["tw"]) / 2
            zs = sorted(x["z"] for x in linhas)
            i = zs.index(L["z"])
            viz = [abs(zs[j] - L["z"]) for j in (i - 1, i + 1) if 0 <= j < len(zs)]
            # ponytail: largura tributária AISC (≤ 3,5b por lado), sem padrões de charneira da mesa
            pc = sum(min(x, 3.5 * bc) / 2 for x in viz[:2]) + (3.5 * bc / 2 if len(viz) < 2 else 0.0)
            ops = [(pc, bc, (pil["b"] - g) / 2)]
            tsc = d["t_enr_pilar"] * MM
            if tsc > 0:   # enrijecedor de continuidade como "alma" do T-stub (linha junto à mesa da viga)
                bs_ = abs(abs(L["z"]) - (H / 2 - tf / 2)) - tsc / 2
                if db / 2 < bs_ < bc:
                    ops.append(((pil["b"] - pil["tw"]) / 2, bs_, 1.25 * bs_))
                m_en = bc - 0.8 * pil.get("r", 0.0)                      # EN fig. 6.8
                m2_en = bs_ - 0.8 * A_SOLDA_ENR * math.sqrt(2)
                e_en = (pil["b"] - g) / 2
                if m_en > 0 and m2_en > 0:   # canto alma + enrijecedor (EN 1993-1-8 tab. 6.4, fig. 6.11)
                    leff = leff_junto_enrijecedor(m_en, m2_en, e_en)
                    if leff:
                        ops.append(("en", leff, m_en, e_en))
                        aviso = (f"solda do enrijecedor do pilar: garganta {A_SOLDA_ENR / MM:.0f} mm assumida no T-stub "
                                 "(EN fig. 6.11), não dimensionada — confira a solda do enrijecedor")
                        if aviso not in av:
                            av.append(aviso)
            cands = [((tstub_en(0.0, o[1], pil["tf"], o[2], o[3], fyc, FtRd) if o[0] == "en"
                       else tstub(0.0, o[0], pil["tf"], o[1], o[2], db, dh, fyc, FtRd)), o) for o in ops]
            L["pilar"], L["pc"] = max(cands, key=lambda c: c[0]["Tcap"])
            L["Tcap"] = min(L["Tcap"], L["pilar"]["Tcap"])
    Mef = momento_efetivo(M, N, braco)
    MRd = momento_resistente_linhas(tr)
    R.res("momento: parafusos tracionados + chapa (T-stub, alavanca)", "6.3.3.1 / 6.3.5", Mef, MRd, "kN·m")
    fator = Mef / MRd if MRd > 0 else 0.0
    fator_op = 0.0
    if co:   # ponytail: força da mesa oposta concentrada nela (linhas a ±pf da mesa), sem charneiras combinadas
        Rop = sum(L["n"] * L["Tcap"] for L in co)
        R.res("tração axial: linhas da mesa oposta (N/2 − |M|/braço, sem contato)", "6.3.3.1 / 6.3.5", F_op, Rop)
        fator_op = F_op / Rop
    n_tot = sum(L["n"] for L in linhas)
    # cortante por parafuso: grupo elástico no plano da chapa (y, z) com Vh, V e a torção Tx (Ip = Σ(y² + z²));
    # sem Vh e Tx dá V/n como antes
    furos = [(y, L["z"]) for L in linhas for y in (-g / 2, g / 2)]
    zg = sum(z for _, z in furos) / len(furos)          # centroide (chapa assimétrica: fora do eixo da viga)
    Ip = sum(y * y + (z - zg) ** 2 for y, z in furos)
    Vb = max(math.hypot(Vh / n_tot - Tx * (z - zg) / Ip, V / n_tot + Tx * y / Ip) for y, z in furos)
    # Mz: binário nas duas colunas de parafusos (braço g, sem contar o contato na borda da chapa: conservador);
    # cada parafuso da coluna tracionada recebe dT a mais
    dT = 2 * abs(Mz) / (g * n_tot)
    for i, L in enumerate(tr + co, 1):
        T = L["Tcap"] * (fator if i <= len(tr) else fator_op) + dT   # T e Tcap são por parafuso
        c = tstub(T, L["p"], tp, L["b"], L["a"], db, dh, fyp, FtRd)
        L["T"], L["Q"] = T, c["Q"]
        nome = f"linha {i} ({dict(ext='externa', int='interna', alma='junto à alma')[L['tipo']]}, h = {L['h'] / MM:.0f} mm)"
        R.res(f"{nome}: flexão da chapa de topo (T-stub, α = {min(max(c['alfa'], 0), 1):.2f})", "6.3.5",
              T, c["T_chapa"])
        R.res(f"{nome}: tração no parafuso com alavanca B = T + Q (Q = {c['Q']:.1f} kN)", "6.3.3.1 / 6.3.5",
              c["B"], FtRd)
        B = c["B"]
        if pil is not None:
            o = L["pc"]
            cp = (tstub_en(T, o[1], pil["tf"], o[2], o[3], fyc, FtRd) if o[0] == "en"
                  else tstub(T, o[0], pil["tf"], o[1], o[2], db, dh, fyc, FtRd))
            R.res(f"{nome}: flexão da mesa do pilar (T-stub)", "6.3.5", T, cp["T_chapa"])
            R.res(f"{nome}: parafuso com alavanca na mesa do pilar", "6.3.3.1 / 6.3.5", cp["B"], FtRd)
            B = max(B, cp["B"])
        if Vb > 0:
            R.res(f"{nome}: tração + cisalhamento no parafuso", "6.3.3.4",
                  lg.interacao_tracao_cisalhamento(B, FtRd, Vb, FvRd), 1.0, "-")
    if dT > 0 and len(tr + co) < len(linhas):   # linhas comprimidas pelo M também tracionam pelo Mz
        R.res("parafusos fora da zona tracionada: tração por Mz", "6.3.3.1", dT, FtRd)
        R.res("parafusos fora da zona tracionada: tração (Mz) + cisalhamento", "6.3.3.4",
              lg.interacao_tracao_cisalhamento(dT, FtRd, Vb, FvRd), 1.0, "-")
    # cortante
    R.res("parafusos: cisalhamento (grupo elástico: Fz, Fy e Mx)" if tem3d else "parafusos: cisalhamento (V/n, todos os parafusos)",
          "6.3.3.2", Vb, FvRd)
    zs = sorted(L["z"] for L in linhas)
    lf = min([zs[i + 1] - zs[i] - dh for i in range(len(zs) - 1)] + [zs[0] - zb - dh / 2, zt - zs[-1] - dh / 2]
             + ([(bp - g) / 2 - dh / 2] if abs(Vh) > 1e-9 else []))
    R.res("pressão de contato na chapa de topo", "6.3.3.3", Vb, lg.pressao_contato(lf, tp, db, fup))
    if pil is not None:
        R.res("pressão de contato na mesa do pilar", "6.3.3.3", Vb,
              lg.pressao_contato(min(zs[i + 1] - zs[i] - dh for i in range(len(zs) - 1)), pil["tf"], db, fuc))
    c = lg.cisalhamento_chapa(bp * tp, (bp - 2 * dn) * tp, fyp, fup)
    R.res("chapa de topo: escoamento por cisalhamento", "6.5.5", abs(V), c["escoamento"])
    R.res("chapa de topo: ruptura por cisalhamento", "6.5.5", abs(V), c["ruptura"])
    if abs(Vh) > 1e-9:   # cortante horizontal: seção vertical da chapa (altura × tp), descontadas as linhas de furos
        hc = zt - zb
        c = lg.cisalhamento_chapa(hc * tp, (hc - len(linhas) * dn) * tp, fyp, fup)
        R.res("chapa de topo: escoamento por cisalhamento horizontal (Fy)", "6.5.5", abs(Vh), c["escoamento"])
        R.res("chapa de topo: ruptura por cisalhamento horizontal (Fy)", "6.5.5", abs(Vh), c["ruptura"])
    # viga e soldas
    MRdx = min(vg["Zx"], 1.5 * vg["Wx"]) * fyb / GA1
    R.res("viga: momento resistente (Zx·fy/γa1 ≤ 1,5·Wx·fy/γa1)", "5.4.2 (CBCA 6.4)", abs(M), MRdx, "kN·m")
    if abs(Mz) > 1e-9:
        MRdy = min(vg["Zy"], 1.5 * vg["Wy"]) * fyb / GA1
        # 5.5.1.2 com N (NRd = A·fy/γa1: seção da ponta, sem flambagem)
        nr = abs(N) / (vg["A"] * fyb / GA1)
        fm = abs(M) / MRdx + abs(Mz) / MRdy
        R.res("viga: N + flexão oblíqua (My e Mz)", "5.5.1.2", nr + 8 / 9 * fm if nr >= 0.2 else nr / 2 + fm, 1.0, "-")
    Ff = Mef / braco
    # solda das mesas: além do binário do My, cada mesa leva Mz/2 no seu plano (σ = 6·(Mz/2)/bf² por comprimento →
    # 3·Mz/bf na força equivalente), Fy/2 e o binário da torção Tx/braço (conservador: somados em módulo)
    fw = lg.ELETRODOS[d["eletrodo"]] * MPa
    # ponta da viga (pontas.py): chapa encostada no apoio, viga cortada a tp dele; copes laterais encurtam as mesas
    ctx = d.get("contexto_pontas") or {}
    ponta = _pontas.pontas_locais(ctx.get("tipo_no", "viga_pilar_mesa"), vg, pil,
                                  comprimento=ctx.get("comprimento", math.inf), desloc=ctx.get("desloc"),
                                  params=dict(ctx.get("params") or {}, folga=tp))[0]
    bf_s = _pontas.largura_util(ponta, bf)
    if bf_s < bf - 1e-9:
        av.append(f"mesas da viga recortadas na ponta: solda das mesas em {bf_s / MM:.1f} mm de {bf / MM:.0f} mm")
    Fn, Fc_m = Ff + 3 * abs(Mz) / bf_s, abs(Vh) / 2 + abs(Tx) / braco   # normal e cisalhamento por mesa
    Ffm = Fn + Fc_m
    if d["solda_mesa"] == "penetracao":   # tabela 8: normal com fy, cisalhamento com 0,6·fy (metal base)
        R.res("solda das mesas (penetração total): metal base", "6.2.5 / tabela 8", Fn, bf_s * tf * fyb / GA1)
        if Fc_m > 1e-9:
            R.res("solda das mesas (penetração total): cisalhamento (Fy, Mx)", "6.2.5 / tabela 8", Fc_m,
                  0.6 * bf_s * tf * fyb / GA1)
    else:
        bm = d["b_mesa"] * MM
        r = lg.resistencia_filete(bm, fw, min(fyb, fyp), min(fub_v, fup))
        R.res("solda das mesas (filetes dos dois lados)", "6.2.5 / tabela 8", Ffm, 2 * bf_s * r["FwRd"])
        _filete(R, bm, min(tf, tp), "das mesas", "b_mesa")
    if tem3d:
        R.avisos.append("esforços fora do plano forte: parafusos ao cortante por grupo elástico (Fy, Fz, Mx); Mz como binário "
                        "nas duas colunas de parafusos (braço g); solda das mesas com Mz, Fy e Mx somados em módulo. "
                        "Pilar verificado só com My e N (docs/v3/LIGACOES-ESFORCOS-3D.md)")
    bw = d["b_alma"] * MM
    lw = (H - 2 * tf) / 2                                  # CBCA 6.4: só a metade comprimida da alma
    r = lg.resistencia_filete(bw, fw, min(fyb, fyp), min(fub_v, fup))
    R.res("solda da alma ao cortante (2 filetes, meia altura)", "6.2.5 / tabela 8", abs(V) / (2 * lw), r["FwRd"], "kN/m")
    _filete(R, bw, min(tw, tp), "da alma", "b_alma")
    # enrijecedor da chapa estendida (DG4: ts ≥ tw·fyb/fys; b/t ≤ 0,56√(E/fy))
    hs = pf + de
    Ls = hs / math.tan(math.radians(30))
    if enr:
        R.minimo("enrijecedor da chapa: t ≥ tw·fy,viga/fy,enr", "AISC DG4 3.1 (detalhe)", tw * fyb / fyp, ts)
        R.maximo("enrijecedor da chapa: hs/t ≤ 0,56√(E/fy)", "tabela F.1 (grupo 3)", hs / ts * MM,
                 0.56 * math.sqrt(E_ACO / fyp) * MM)
    # detalhamento
    _bordas(R, db, tp, (bp - g) / 2, "lateral da chapa", d["borda_laminada"])
    if estendida in ("superior", "ambas", "inferior"):
        _bordas(R, db, tp, de, "extremidade da chapa estendida", d["borda_laminada"])
    R.minimo("gabarito ≥ 2,7db", "6.3.9", 2.7 * db, g)
    R.minimo("gabarito: folga para a alma (g − tw)/2 ≥ db", "CBCA 6.1.1", db, (g - tw) / 2)
    R.minimo("furo → face da mesa (pf) ≥ mínimo de aperto", "CBCA 6.1.1", pf_min, pf)
    # pilar
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[])
    x0 = ponta["plano"]["ponto"][0] - tp if pil is not None else 0.0
    if pil is not None:
        Fc = abs(M) / braco + max(-N, 0) / 2
        ln = tf + 2 * tp + (2 * d["b_mesa"] * MM if d["solda_mesa"] == "filete" else 0)
        _pilar(R, d, pil, fyc, Ff, Fc, ln, bf, tf, H, Mef, False, geo, x0)
    # geometria
    geo["chapas"].insert(0, _chapa("chapa de topo", [[-bp / 2, zb], [bp / 2, zb], [bp / 2, zt], [-bp / 2, zt]],
                                   [x0, 0, 0], [1, 0, 0], [0, 1, 0], tp))
    pega = tp + (pil["tf"] if pil is not None else tp)
    for L in linhas:
        for y in (-g / 2, g / 2):
            geo["parafusos"].append(_paraf(db, _comprimento_parafuso(pega, db), [x0 + tp, y, L["z"]], [-1, 0, 0]))
    xs = x0 + tp
    tipo_m = "penetracao" if d["solda_mesa"] == "penetracao" else "filete"
    for zf in (H / 2, H / 2 - tf, -H / 2, -(H / 2 - tf)):
        geo["soldas"].append(_solda(tipo_m, tf if tipo_m == "penetracao" else d["b_mesa"] * MM,
                                    [[xs, -bf_s / 2, zf], [xs, bf_s / 2, zf]]))
    for y in (-tw / 2, tw / 2):
        geo["soldas"].append(_solda("filete", bw, [[xs, y, -(H / 2 - tf)], [xs, y, H / 2 - tf]]))
    if enr:
        for s in {"superior": (1,), "inferior": (-1,)}.get(estendida, (1, -1)):
            geo["chapas"].append(_chapa("enrijecedor da chapa de topo",
                                        [[0, s * H / 2], [Ls, s * H / 2], [0, s * (H / 2 + hs)]],
                                        [xs, ts / 2, 0], [0, -1, 0], [1, 0, 0], ts))
            # soldas do enrijecedor (só geometria/desenho, não dimensionadas: filete mínimo como no enrijecedor do pilar):
            # na chapa de topo (x = xs) e na mesa da viga (z = ±H/2), no plano médio do enrijecedor (y = 0)
            bw_e = max(lg.filete_minimo(min(ts, tp)), 0.005)
            geo["soldas"] += [_solda("filete", bw_e, [[xs, 0.0, s * H / 2], [xs, 0.0, s * (H / 2 + hs)]]),
                              _solda("filete", bw_e, [[xs, 0.0, s * H / 2], [xs + Ls, 0.0, s * H / 2]])]
    R.avisos.append("modelo: T-stub de Struik–de Back (Kulak cap. 17) por linha, distribuição plástica "
                    "(Σ n·T·h); linhas internas junto à mesa comprimida só ao cortante")
    geo["pontas"] = [ponta]
    out = R.resultado(geo)
    out["intermediarios"] = dict(
        MRd=MRd, Mef=Mef, FtRd=FtRd, FvRd=FvRd, bp=_mm(bp), g=_mm(g), pf=_mm(pf), altura_chapa=_mm(zt - zb),
        linhas=[dict(z=_mm(L["z"]), h=_mm(L["h"]), b=_mm(L["b"]), a=_mm(L["a"]), p=_mm(L["p"]), Tcap=L["Tcap"],
                     T=L["T"], Q=L["Q"]) for L in tr])
    return out


# ---------------------------------------------------------------- chapa de topo ajustada com viga U (W11-CHAPA-TOPO-U.md)

SOBRA_U = 0.005   # m: chapa = seção do U + 5 mm de cada lado (pedido do usuário); viga e apoio do mesmo tamanho: sem sobra


def _forma_cat(v):
    """Tipo do catálogo ("I", "U", "Ue"...) ou None (dict, vazio ou fora do catálogo: o _perfil explica depois)."""
    if not v or isinstance(v, dict):
        return None
    try:
        return secoes.catalogo(str(v)).props.get("tipo")
    except KeyError:
        return None


def _cabeca(db):
    """Largura nos cantos da cabeça sextavada pesada (A325, ASME B18.2.6: F = 1,5d + ⅛"; e = F/cos 30°), m."""
    return (1.5 * db + 0.003175) / math.cos(math.radians(30))


def _pf_cbca(db):
    """Furo → face da mesa para o aperto da porca (CBCA 6.1.1): 30 mm (⅝", ¾"), 35 (⅞"), 40 (1"); abaixo de ⅝" (fora
    da tabela do CBCA) db + 12,7 mm (AISC DG4: pf ≥ db + ½", a regra que a tabela do CBCA arredonda)."""
    if db < 15.875 * MM - 1e-9:
        return db + 0.0127
    return next((p for dd, p in _PF_MIN if db / MM <= dd + 0.05), 1.6 * db)


def _folgas_u(db, bm, bw):
    """W11: distância mínima do furo às faces internas do U. A cabeça fica dentro do U (só segurada); o aperto é na porca,
    do lado do apoio. Mínimo = cabeça/2 + perna do filete interno (o canto da cabeça livre do pé do filete) e ≥ db (folga
    de chave da CBCA 6.1.1, a mesma da chapa de topo junto à alma). → (até a mesa, até a alma), m."""
    e2 = _cabeca(db) / 2
    return max(db, e2 + bm), max(db, e2 + bw)


def _d_max_u(vg, bm, bw, sobra):
    """Maior diâmetro (mm) com 2 parafusos dentro do U, pelos mínimos (sem folga): na altura livre entre as mesas
    2·pf + 2,7d (6.3.9) e, na largura, furo → alma + borda (tabela 14) até a borda da chapa."""
    hi, larg = vg["h"] - 2 * vg["tf"], vg["b"] - vg["tw"] + sobra

    def cabe(dm):
        pf, c = _folgas_u(dm * MM, bm, bw)
        return hi - 2 * pf >= 2.7 * dm * MM and larg - c >= lg.distancia_min_borda(dm * MM)
    lo, hi_ = 0.0, 40.0
    for _ in range(40):
        m = (lo + hi_) / 2
        lo, hi_ = (m, hi_) if cabe(m) else (lo, m)
    return lo


def _contorno_u(H, bf, tw, tf, D, uk):
    """Contorno (u, z) da seção do U na face da chapa, u = ab·y (u > 0 para o lado aberto), costas da alma em u = uk.
    Ue: com os enrijecedores de borda (D) na ponta das mesas."""
    ui, ut, h2 = uk + tw, uk + bf, H / 2
    if D > 0:
        baixo = [(uk, -h2), (ut, -h2), (ut, -h2 + D), (ut - tw, -h2 + D), (ut - tw, -h2 + tf), (ui, -h2 + tf)]
    else:
        baixo = [(uk, -h2), (ut, -h2), (ut, -h2 + tf), (ui, -h2 + tf)]
    return baixo + [(u, -z) for u, z in reversed(baixo[1:])] + [(uk, h2)]


def _chapa_topo_u(d, esforcos, dados):
    """W11 (docs/v3/W11-CHAPA-TOPO-U.md): chapa de topo ajustada com viga U (UPN, U laminado, U e Ue dobrados).
    Chapa = seção do U + 5 mm de cada lado (d + 10 × bf + 10); viga e apoio do mesmo tamanho (mesmos d e bf): d × bf.
    2 parafusos numa linha vertical dentro do U (entre as mesas, do lado aberto da alma), cabeça dentro do U e porca do
    lado do apoio. Apoio: mesa do pilar I/H ou costas da alma do pilar U (a alma do pilar U encosta na chapa).

    Modelo: M_ef = |M| + N⁺·(H − tf)/2 (momento_efetivo) no parafuso tracionado com braço h até o centro da mesa
    comprimida, T = M_ef/h (N maior que o binário: os dois tracionados, N/2 ± M/s); Mz por binário da linha dos parafusos
    até a borda mais próxima do contorno do U; cortante V/2 por parafuso mais a torção T + V·y (V no eixo da barra,
    parafusos em y). Chapa: T-stub da EN 1993-1-8 (6.2.4) com o parafuso no canto alma–mesa (leff = α·m da fig. 6.11, a
    mesa no lugar do enrijecedor). Apoio I: mesa como T-stub (Struik–de Back, b até a alma), 5.7.3/5.7.4 na compressão e
    painel 5.7.7.1. Apoio U: alma como T-stub da EN entre as mesas do pilar (m até a mais próxima) e plastificação sob a
    mesa comprimida pela face do tubo retangular (EN 1993-1-8 tab. 7.11, NBR 16239), β ≤ 0,85. Solda: contorno do U pelo
    grupo elástico do W10 (lado externo só com a perna apoiada na sobra da chapa). Torção da barra: só aviso."""
    from . import ligacoes_flexiveis as _lf   # grupo de solda do W10 (import local: os módulos de família se carregam juntos)
    R = _R()
    av = R.avisos
    vg = _perfil_iu(d["perfil_viga"], "perfil_viga", ue=True)
    H, bf, tw, tf, xg, Dl = vg["h"], vg["b"], vg["tw"], vg["tf"], vg["xg"], vg.get("D", 0.0)
    ab = -1.0 if float((dados or {}).get("abertura_y") or 1) < 0 else 1.0
    fyb, fub_v = _aco(d["aco_viga"])
    fyp, fup = _aco(d["aco_chapa"])
    db, fub, alta, dh, FtRd, FvRd, a0 = _par(d)
    av += a0
    dn = dh + lg.FURO_DANO
    tp, bm, bw = d["tp"] * MM, d["b_mesa"] * MM, d["b_alma"] * MM
    pil = _perfil_iu(d["perfil_pilar"], "perfil_pilar", ue=True) if d.get("perfil_pilar") else None
    if pil is None:
        av.append("pilar não informado: apoio NÃO verificado")
    else:
        fyc, fuc = _aco(d["aco_pilar"])
    pU = pil is not None and pil["forma"] == "U"
    mesmo = pil is not None and abs(pil["h"] - H) < 0.5 * MM and abs(pil["b"] - bf) < 0.5 * MM
    sobra = 0.0 if mesmo else SOBRA_U
    # u = ab·y (u > 0 para o lado aberto): costas da alma em −xg, face interna em tw − xg, ponta das mesas em bf − xg
    uk, ui, ut = -xg, tw - xg, bf - xg
    y0, y1 = sorted((ab * (uk - sobra), ab * (ut + sobra)))
    zt_p, zb_p = H / 2 + sobra, -(H / 2 + sobra)
    bp, hp = y1 - y0, zt_p - zb_p
    borda = lg.distancia_min_borda(db, d["borda_laminada"])
    pf_min, c_min = _folgas_u(db, bm, bw)
    hi = H - 2 * tf
    if hi - 2 * pf_min < 2.7 * db or (ut + sobra) - (ui + c_min) < borda:
        dm = _d_max_u(vg, bm, bw, sobra)
        raise ValueError(
            f"viga {vg['nome']}: 2 parafusos de {db / MM:g} mm não cabem dentro do U (altura livre entre as mesas "
            f"{hi / MM:.0f} mm; precisa 2·pf + 2,7d = {(2 * pf_min + 2.7 * db) / MM:.0f} mm, pf ≥ {pf_min / MM:.1f} mm = "
            f"cabeça/2 + filete; largura {(bf - tw) / MM:.0f} mm da alma à ponta da mesa). Diâmetro máximo que cabe: "
            f"{dm:.1f} mm" + (" — menor que M12, o menor usual: use a solda direta ou outra ligação" if dm < 12 else ""))
    # posição vertical: pf (furo → face interna da mesa); auto = mínimo com a folga de 10 % (W8-RIG) sem levar o
    # espaçamento abaixo de 3d (= 2,7d com folga)
    if d["pf"]:
        pf = d["pf"] * MM
    else:
        pf = math.ceil(pf_min / ALVO / MM - 1e-9) * MM
        if hi - 2 * pf < 2.7 * db / ALVO:
            pf = max(pf_min, (hi - 2.7 * db / ALVO) / 2)
    zb = H / 2 - tf - pf
    if zb <= dh / 2:
        raise ValueError(f"pf = {pf / MM:g} mm: os 2 parafusos se sobrepõem (altura livre entre as mesas {hi / MM:.0f} mm)")
    s = 2 * zb
    # posição horizontal: c (furo → face interna da alma). Mínimos: cabeça dentro do U; porca do lado do apoio com a
    # folga de chave (CBCA 6.1.1): pilar I → furo a ≥ db da face da alma do pilar; pilar U → a ≥ pf (CBCA) das mesas do
    # pilar. Máximo: a borda lateral da chapa (tabela 14). Auto: o menor com a folga de 10 %; se não cabe, sem a folga
    lo, lo0 = [c_min / ALVO], [c_min]
    hic, hic0 = [ut + sobra - borda / ALVO - ui], [ut + sobra - borda - ui]
    if pil is not None and not pU:   # ponytail: só o lado aberto (u > 0); atrás da alma do pilar a cabeça não cabe
        lo.append(pil["tw"] / 2 + db / ALVO - ui)
        lo0.append(pil["tw"] / 2 + db - ui)
    if pU:
        hwp, pfc = pil["h"] / 2 - pil["tf"], _pf_cbca(db)
        hic.append(hwp - pfc / ALVO - ui)
        hic0.append(hwp - pfc - ui)
    if d["c_alma"]:
        c = d["c_alma"] * MM
    else:
        c = math.ceil(max(lo) / MM - 1e-9) * MM
        if c > min(hic) + 1e-9:
            c = math.ceil(max(lo0) / MM - 1e-9) * MM
    ub = ui + c
    if not ui + dh / 2 < ub < ut - dh / 2:
        raise ValueError(f"c_alma = {c / MM:g} mm: a linha dos parafusos sai de dentro do U "
                         f"({(bf - tw) / MM:.0f} mm da alma à ponta da mesa)")
    yb = ab * ub
    e_l = ut + sobra - ub                                       # furo → borda lateral da chapa (lado aberto)
    # esforços; braço até o centro da mesa comprimida
    braco = H - tf
    h = zb + braco / 2
    e = _esforcos(esforcos, av, usados=("N", "V", "Vz", "M", "Mz", "T"))
    _minimo_6152(e, h, av, d.get("terca_ou_travessa", False))   # força da ligação = tração no parafuso (M/h)
    M, N = e["M"], e["N"]
    V, Vh, Mz, Tx = abs(e["V"]), abs(e["Vz"]), abs(e["Mz"]), abs(e["T"])
    Mef = momento_efetivo(M, N, braco)
    Tt, Tc = Mef / h, 0.0
    if N > 0 and Tt < N:   # tração maior que o binário: os dois parafusos tracionados, sem contato
        Tt, Tc = N / 2 + abs(M) / s, max(N / 2 - abs(M) / s, 0.0)
    C = max(Tt + Tc - N, 0.0)                                   # compressão da mesa comprimida na face do apoio
    dT = Mz / (2 * min(ub - uk, ut - ub)) if Mz > 1e-9 else 0.0   # Mz: braço até a borda mais próxima do contorno
    Ts = [Tt + dT] + ([Tc + dT] if Tc + dT > 1e-9 else [])
    Vb = math.hypot(V / 2, Vh / 2 + (Tx + V * abs(yb)) / s)    # torção nos 2 parafusos: (T + V·y)/s cada
    # parafusos e chapa (T-stub da EN no canto alma–mesa; m até o pé do filete: 0,8·a·√2 = 0,8·b, EN fig. 6.2)
    m, m2 = c - 0.8 * bw, pf - 0.8 * bm
    if m <= 0:
        raise ValueError(f"c_alma = {c / MM:g} mm: furo sobre o filete da alma (m = {m / MM:.1f} mm)")
    leff = (leff_junto_enrijecedor(m, m2, e_l) if m2 > 0 else None) or min(2 * math.pi * m, 4 * m + 1.25 * e_l)
    i0 = len(R.v)
    R.res(f"momento: 2 parafusos dentro do U, tração T = M_ef/h (h = {h / MM:.0f} mm até a mesa comprimida)",
          "6.3.3.1", Ts[0], FtRd)
    apoio_ok = []
    for i, T in enumerate(Ts):
        nome = "parafuso tracionado" if i == 0 else "parafuso junto à outra mesa (tração)"
        cp = tstub_en(T, leff, tp, m, e_l, fyp, FtRd)
        R.res(f"chapa de topo: flexão no canto alma–mesa do U ({nome}; T-stub, leff = {leff / MM:.0f} mm)",
              "6.3.5 (EN 1993-1-8 6.2.4)", T, cp["T_chapa"])
        R.res(f"{nome}: tração com alavanca na chapa B = T + Q (Q = {cp['B'] - T:.1f} kN)", "6.3.3.1 / 6.3.5",
              cp["B"], FtRd)
        B = cp["B"]
        j0 = len(R.v)
        if pil is not None and not pU:   # mesa do pilar: T-stub com b até a face da alma do pilar, a até a borda
            bc, ac = abs(ub) - pil["tw"] / 2, pil["b"] / 2 - abs(ub)
            if bc <= db / 2 or ac <= dh / 2:
                raise ValueError(f"linha dos parafusos (y = {yb / MM:.0f} mm) fora da mesa do pilar ou sobre a alma dele")
            pc = min(s, 3.5 * bc) / 2 + 3.5 * bc / 2   # largura tributária como na chapa de topo (≤ 3,5b por lado)
            ap = tstub(T, pc, pil["tf"], bc, ac, db, dh, fyc, FtRd)
            R.res(f"{nome}: flexão da mesa do pilar (T-stub)", "6.3.5", T, ap["T_chapa"])
            R.res(f"{nome}: tração com alavanca na mesa do pilar", "6.3.3.1 / 6.3.5", ap["B"], FtRd)
            B = max(B, ap["B"])
        elif pU:   # alma do pilar U entre as mesas: T-stub da EN, m até a mesa mais próxima, e até a outra
            mu, eu = hwp - abs(ub) - 0.8 * pil["r"], hwp + abs(ub)
            if mu <= 0:
                raise ValueError(f"linha dos parafusos (y = {yb / MM:.0f} mm) sobre a mesa do pilar U")
            leu = min(2 * math.pi * mu, 4 * mu + 1.25 * eu)
            ap = tstub_en(T, leu, pil["tw"], mu, eu, fyc, FtRd)
            R.res(f"{nome}: flexão da alma do pilar U (T-stub entre as mesas, leff = {leu / MM:.0f} mm)",
                  "6.3.5 (EN 1993-1-8 6.2.4)", T, ap["T_chapa"])
            R.res(f"{nome}: tração com alavanca na alma do pilar U", "6.3.3.1 / 6.3.5", ap["B"], FtRd)
            B = max(B, ap["B"])
        apoio_ok += R.v[j0:]
        R.res(f"{nome}: tração + cisalhamento", "6.3.3.4", lg.interacao_tracao_cisalhamento(B, FtRd, Vb, FvRd), 1.0, "-")
    if any(not v["ok"] for v in R.v[i0:] if not any(v is x for x in apoio_ok)):
        av.append(f"momento além da capacidade de 2 parafusos dentro do U (M = {abs(M):.2f} kN·m, T = {Ts[0]:.1f} kN no "
                  f"parafuso com h = {h / MM:.0f} mm): aumente o diâmetro ou a chapa, ou use outra ligação")
    R.res("parafusos: cisalhamento (V/2 + torção T + V·y, 2 parafusos)", "6.3.3.2", Vb, FvRd)
    lf = min(s - dh, zt_p - zb - dh / 2)
    if Vh > 1e-9:
        lf = min(lf, e_l - dh / 2, ub - (uk - sobra) - dh / 2)
    R.res("pressão de contato na chapa de topo", "6.3.3.3", Vb, lg.pressao_contato(lf, tp, db, fup))
    if pil is not None:
        t_ap = pil["tw"] if pU else pil["tf"]
        R.res(f"pressão de contato no apoio ({'alma do pilar U' if pU else 'mesa do pilar'})", "6.3.3.3", Vb,
              lg.pressao_contato(s - dh, t_ap, db, fuc))
    cc = lg.cisalhamento_chapa(bp * tp, (bp - dn) * tp, fyp, fup)
    R.res("chapa de topo: escoamento por cisalhamento", "6.5.5", V, cc["escoamento"])
    R.res("chapa de topo: ruptura por cisalhamento", "6.5.5", V, cc["ruptura"])
    if Vh > 1e-9:
        cc = lg.cisalhamento_chapa(hp * tp, (hp - 2 * dn) * tp, fyp, fup)
        R.res("chapa de topo: escoamento por cisalhamento horizontal (Fy)", "6.5.5", Vh, cc["escoamento"])
        R.res("chapa de topo: ruptura por cisalhamento horizontal (Fy)", "6.5.5", Vh, cc["ruptura"])
    R.res("viga U: momento resistente (Zx·fy/γa1 ≤ 1,5·Wx·fy/γa1)", "5.4.2 (CBCA 6.4)", abs(M),
          min(vg["Zx"], 1.5 * vg["Wx"]) * fyb / GA1, "kN·m")
    R.res("viga U: cisalhamento da alma (Aw = d·tw)", "5.4.3.1", V, 0.6 * fyb * H * tw / GA1)
    # solda: contorno do U (grupo elástico do W10). Lado externo (costas da alma, faces externas e pontas das mesas) só
    # com a perna apoiada na sobra da chapa; sem sobra (viga e apoio do mesmo tamanho) só o contorno interno
    eps = 1e-9
    lados, sem = [], 0.0
    pts = _contorno_u(H, bf, tw, tf, Dl, uk)
    for (ua, za), (uc, zc) in zip(pts, pts[1:] + pts[:1]):
        vert = abs(ua - uc) < eps
        fora = (vert and (abs(ua - uk) < eps or abs(ua - ut) < eps)) or (not vert and abs(abs(za) - H / 2) < eps)
        alma = vert and (abs(ua - uk) < eps or abs(ua - ui) < eps)
        b = bw if alma else bm
        if fora and b > sobra + eps:
            sem += math.hypot(uc - ua, zc - za)
            continue
        lados.append((((ab * ua, za), (ab * uc, zc)), b, alma))
    segs = [x[0] for x in lados]
    tf_g = max(tf, Dl / 1.5)   # ponytail: o _alma_lado do W10 (vertical > 1,5·tf) não pode contar o enrijecedor do Ue
    g = _lf._grupo(segs, tf_g)
    q = max(_lf._q_lado(g, sg, tf_g, V, abs(N), Vh, abs(M), Mz, Tx + V * abs(g["yc"])) for sg in segs)
    b_min = min(x[1] for x in lados)
    fw = lg.ELETRODOS[d["eletrodo"]] * MPa
    R.res(f"solda: contorno do U ({'inteiro' if sem == 0 else 'só o lado interno'}; grupo elástico, perna "
          f"{b_min / MM:g} mm)", "6.2.5 / tabela 8", q,
          lg.resistencia_filete(b_min, fw, min(fyb, fyp), min(fub_v, fup))["FwRd"], "kN/m")
    if sem > 0:
        av.append(f"{sem / MM:.0f} mm do contorno externo do U sem solda: " + (
            "chapa do tamanho do perfil (viga e apoio do mesmo tamanho), sem sobra para a perna" if sobra == 0 else
            f"perna maior que a sobra de {sobra / MM:.0f} mm da chapa"))
    if vg["formado"]:
        av.append(f"U dobrado (t = {tw / MM:.2f} mm): barra pela NBR 14762; pernas na parede fina não verificadas aqui "
                  "(use perna = t); a resistência do filete está verificada pelo metal base")
    else:
        for nome, b, t_, campo in (("da alma", bw, tw, "b_alma"), ("das mesas", bm, tf, "b_mesa")):
            bmin = lg.filete_minimo(min(t_, tp))
            R.minimo(f"solda {nome}: perna mínima", "6.2.6.2.1 / tabela 10", bmin, b, campo)
            if sobra > 0 and abs(b - sobra) < eps and bmin / ALVO > sobra + eps:
                R.v[-1]["limite_sobra"] = True   # perna = sobra da chapa: o sugerir não consegue a folga de 10 % aqui
    # disposições
    _espacamento(R, s, db, tp, "entre os 2 parafusos")
    R.minimo("cabeça dentro do U: furo → face interna da mesa (cabeça/2 + filete, ≥ db)", "CBCA 6.1.1 (folga de chave)",
             pf_min, pf, "pf")
    R.minimo("cabeça dentro do U: furo → face interna da alma (cabeça/2 + filete, ≥ db)", "CBCA 6.1.1 (folga de chave)",
             c_min, c, "c_alma")
    if Dl > 0:
        R.minimo("cabeça dentro do U: furo → enrijecedor de borda (Ue)", "CBCA 6.1.1 (folga de chave)", pf_min,
                 math.hypot(max(0.0, ut - tw - ub), max(0.0, H / 2 - Dl - zb)))
    _bordas(R, db, tp, e_l, "lateral da chapa, lado aberto do U", d["borda_laminada"])
    if pil is not None and not pU:
        R.minimo("apoio: furo → face da alma do pilar ≥ db (aperto da porca)", "CBCA 6.1.1", db, abs(ub) - pil["tw"] / 2,
                 "c_alma")
        R.minimo("apoio: borda mínima na mesa do pilar", "6.3.11.1 / tabela 14", lg.distancia_min_borda(db, True),
                 pil["b"] / 2 - abs(ub))
        R.minimo("apoio: mesa do pilar cobre a chapa", "geometria", 2 * max(abs(y0), abs(y1)), pil["b"])
    elif pU:
        R.minimo("apoio: furo → face interna da mesa do pilar U ≥ pf (aperto da porca)", "CBCA 6.1.1", pfc,
                 hwp - abs(ub), "c_alma")
        R.minimo("apoio: alma do pilar U cobre a chapa", "geometria", 2 * max(abs(y0), abs(y1)), pil["h"])
    # apoio: compressão da mesa comprimida
    if pil is not None and not pU:
        k, ext = pil["tf"] + pil.get("r", 0.0), d["topo_pilar"]
        ln = tf + 2 * tp + 2 * bm
        if C > 0:
            R.res("escoamento local da alma do pilar (compressão)", "5.7.3", C,
                  escoamento_local_alma(pil["tw"], k, ln, fyc, ext))
            R.res("enrugamento da alma do pilar", "5.7.4", C, enrugamento_alma(pil["tw"], pil["tf"], pil["h"], ln, fyc, ext))
        Vp = abs(M) / h + d["M_outra"] / h - d["V_pilar"]
        R.res("cisalhamento do painel de alma do pilar", "5.7.7.1 / 5.4.3", abs(Vp),
              cisalhamento_painel(pil["h"], pil["tw"], fyc, d["N_pilar"], pil["A"] * fyc, pil["h"] - 2 * k, h))
    elif pU and C > 0:
        b0, h1 = pil["h"], tf + 2 * tp
        beta, eta = min(bf / b0, 0.85), h1 / b0
        NRd = fyc * pil["tw"] ** 2 / (1 - beta) * (2 * eta + 4 * math.sqrt(1 - beta)) / GA1
        R.res(f"alma do pilar U: plastificação sob a mesa comprimida (face de tubo retangular, β = {beta:.2f}, "
              f"η = {eta:.2f})", "EN 1993-1-8 tab. 7.11 (NBR 16239, analogia)", C, NRd)
        av.append("alma do pilar U sob a compressão: plastificação como a face de um tubo retangular apoiada nas mesas "
                  "(EN 1993-1-8 tab. 7.11), sem a redução kn pela compressão do pilar" +
                  ("; β > 0,85 usado como 0,85 (a favor), compressão das mesas do pilar não verificada"
                   if bf / b0 > 0.85 else ""))
    ign = [k for k in ("bp", "g", "n_linhas_extras", "t_enr_pilar", "t_reforco") if d.get(k)]
    if ign:
        av.append(f"viga U: {', '.join(ign)} não usados (chapa = U + sobra, 2 parafusos, sem enrijecedores do pilar)")
    if d["solda_mesa"] == "penetracao":
        av.append("viga U: filete em volta do U (a penetração das mesas não é usada)")
    x0s = abs(vg.get("x0") or 0.0)
    av.append(f"viga U: centro de cisalhamento a {(x0s - xg) / MM:.0f} mm atrás das costas da alma, a "
              f"{(ub + x0s) / MM:.0f} mm da linha dos parafusos: a torção da barra (V·e) não é verificada aqui; nos "
              f"parafusos entra a torção V·y = {V * abs(yb):.2f} kN·m (V no eixo da barra)")
    av.append(f"modelo: 2 parafusos numa linha vertical dentro do U (cabeça dentro, porca e aperto do lado do apoio); "
              f"T = M_ef/h com h = {h / MM:.0f} mm do parafuso tracionado ao centro da mesa comprimida; chapa como T-stub "
              "da EN 1993-1-8 no canto alma–mesa" + ("; apoio = costas da alma do pilar U" if pU else ""))
    # geometria (mm; x ao longo da viga, origem no nó): chapa encostada na face do apoio
    xf = 0.0 if pil is None else (pil["xg"] if pU else pil["h"] / 2)
    xs = xf + tp
    geo = dict(chapas=[dict(_chapa("chapa de topo", [[y0, zb_p], [y1, zb_p], [y1, zt_p], [y0, zt_p]], [xf, 0, 0],
                                   [1, 0, 0], [0, 1, 0], tp), campo="tp")],
               cantoneiras=[], recortes=[], abertura_y=int(ab),
               parafusos=[dict(_paraf(db, _comprimento_parafuso(tp + (tp if pil is None else t_ap), db), [xs, yb, z],
                                      [-1, 0, 0]), campo="d_mm") for z in (zb, -zb)],
               soldas=[dict(_solda("filete", b, [[xs, *pa], [xs, *pc_]]), campo="b_alma" if alma else "b_mesa")
                       for (pa, pc_), b, alma in lados],
               pontas=[dict(barra=0, extremo="i", plano=dict(ponto=[xs, 0.0, 0.0], normal=[-1.0, 0.0, 0.0]), folga=0.0,
                            recortes=[])])

    def placa(pts3, normal, t):
        return dict(base=[[_mm(x) for x in p] for p in pts3], normal=normal, espessura=_mm(t))

    def ret_x(x, ya, yc, za, zc, t):   # placa no plano x (normal x)
        return placa([[x, ya, za], [x, yc, za], [x, yc, zc], [x, ya, zc]], [1, 0, 0], t)

    def ret_y(y, xa, xc, za, zc, t):
        return placa([[xa, y, za], [xc, y, za], [xc, y, zc], [xa, y, zc]], [0, 1, 0], t)

    def ret_z(z, xa, xc, ya, yc, t):
        return placa([[xa, ya, z], [xc, ya, z], [xc, yc, z], [xa, yc, z]], [0, 0, 1], t)
    xe = xs + max(1.5 * H, 0.250)
    pv = [ret_y(ab * (uk + tw / 2), xs, xe, -(H / 2 - tf), H / 2 - tf, tw)]
    pv += [ret_z(zm, xs, xe, ab * uk, ab * ut, tf) for zm in (H / 2 - tf / 2, -(H / 2 - tf / 2))]
    if Dl > 0:
        pv += [ret_y(ab * (ut - tw / 2), xs, xe, sg * (H / 2 - tf), sg * (H / 2 - Dl), tw) for sg in (1, -1)]
    membros = [dict(barra="viga", perfil=vg["nome"], campo="perfil_viga", d=_mm(H), bf=_mm(bf), tw=_mm(tw), tf=_mm(tf),
                    forma="U", xg=_mm(xg), abertura_y=int(ab), pecas=pv)]
    if pil is not None:
        ze = H / 2 + 0.6 * max(H, 0.250)
        hc, bc_, twc, tfc = pil["h"], pil["b"], pil["tw"], pil["tf"]
        if pU:   # costas da alma para a viga (+x), mesas para −x
            xgp = pil["xg"]
            pp = [ret_x(xgp - twc / 2, -(hc / 2 - tfc), hc / 2 - tfc, -ze, ze, twc)]
            pp += [ret_y(sg * (hc / 2 - tfc / 2), xgp - bc_, xgp, -ze, ze, tfc) for sg in (1, -1)]
            if pil.get("D"):
                pp += [ret_x(xgp - bc_ + twc / 2, sg * (hc / 2 - tfc), sg * (hc / 2 - pil["D"]), -ze, ze, twc)
                       for sg in (1, -1)]
        else:
            pp = [ret_x(sg * (hc / 2 - tfc / 2), -bc_ / 2, bc_ / 2, -ze, ze, tfc) for sg in (1, -1)]
            pp.append(ret_y(0.0, -(hc / 2 - tfc), hc / 2 - tfc, -ze, ze, twc))
        membros.append(dict(barra="pilar", perfil=pil["nome"], campo="perfil_pilar", d=_mm(hc), bf=_mm(bc_),
                            tw=_mm(twc), tf=_mm(tfc), pecas=pp, **({"forma": "U"} if pU else {})))
    geo["membros"] = membros
    out = R.resultado(geo)
    out["intermediarios"] = dict(FtRd=FtRd, FvRd=FvRd, Mef=Mef, M=M, V=V, N=N, h=_mm(h), zb=_mm(zb), s=_mm(s),
                                 pf=_mm(pf), c_alma=_mm(c), y_parafusos=_mm(yb), T=Ts[0], Tc=Tc, C=C, Vb=Vb,
                                 leff=_mm(leff), m=_mm(m), m2=_mm(m2), e_borda=_mm(e_l), b_chapa=_mm(bp),
                                 h_chapa=_mm(hp), sobra=_mm(sobra), q_solda=q, L_solda=_mm(g["L"]),
                                 pf_min=_mm(pf_min), c_min=_mm(c_min))
    return out


def verificar_chapa_topo_ajustada(dados, esforcos):
    d = _ler("chapa_topo_ajustada", dados)
    d["contexto_pontas"] = (dados or {}).get("contexto_pontas")
    d["estendida"] = "nao"
    if _forma_cat(d["perfil_viga"]) in ("U", "Ue"):   # W11: viga U (2 parafusos dentro do U)
        return _chapa_topo_u(d, esforcos, dados)
    if _forma_cat(d.get("perfil_pilar")) in ("U", "Ue"):
        raise ValueError(f"perfil_pilar: pilar U ({d['perfil_pilar']}) nesta ligação só com viga U (2 parafusos dentro "
                         "do U, nas costas da alma do pilar); com viga I/H use pilar I/H")
    return _chapa_topo(d, esforcos, "nao")


def verificar_chapa_topo_estendida(dados, esforcos):
    d = _ler("chapa_topo_estendida", dados)
    d["contexto_pontas"] = (dados or {}).get("contexto_pontas")
    return _chapa_topo(d, esforcos, d["estendida"])


# ---------------------------------------------------------------- flange plate

def verificar_flange_plate(dados, esforcos):
    d = _ler("flange_plate", dados)
    R = _R()
    av = R.avisos
    e = _esforcos(esforcos, av)
    vg = _perfil(d["perfil_viga"], "perfil_viga")
    H, bf, tw, tf = vg["h"], vg["b"], vg["tw"], vg["tf"]
    fyb, fub_v = _aco(d["aco_viga"])
    fyp, fup = _aco(d["aco_chapa"])
    fw = lg.ELETRODOS[d["eletrodo"]] * MPa
    t, bpl, folga = d["t_mesa"] * MM, (d["b_chapa"] or bf / MM) * MM, d["folga"] * MM
    braco = H + t
    _minimo_6152(e, braco, av)
    M, N, V = e["M"], e["N"], e["V"]
    Fc = abs(M) / braco + max(-N, 0) / 2
    Ft = abs(M) / braco + max(N, 0) / 2
    pil, fyc, fuc = _pilar_props(d, R)
    x0 = pil["h"] / 2 if pil is not None else 0.0
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[])
    Ag = bpl * t
    R.res("chapa de mesa: escoamento da seção bruta (tração)", "6.5.3 → 5.2.2 a)", Ft, Ag * fyp / GA1)
    if d["modo"] == "parafusada":
        db, fub, alta, dh, FtRd, FvRd, a0 = _par(d)
        av += a0
        dn = dh + lg.FURO_DANO
        nl, npl, s, e1, g = 2, d["n_por_linha"], d["s"] * MM, d["e1"] * MM, (d["g"] or bf / MM / 2 + 10) * MM
        n = nl * npl
        L1 = folga + d["e_viga"] * MM                   # face do pilar → 1ª linha
        Lp = L1 + (npl - 1) * s + e1
        tr = lg.tracao_chapa(Ag, (bpl - nl * dn) * t, fyp, fup)
        R.res("chapa de mesa: ruptura da seção líquida", "6.5.3 → 5.2.2 b)", Ft, tr["ruptura"])
        R.res("chapa de mesa: compressão (K = 0,65, L até a 1ª linha)", "6.5.4", Fc,
              compressao_chapa(Ag, 0.65 * L1, t / math.sqrt(12), fyp))
        R.res("parafusos da mesa: cisalhamento (corte simples)", "6.3.3.2", max(Ft, Fc) / n, FvRd)
        for nome, tt, fu_, e_ in (("chapa", t, fup, e1), ("mesa da viga", tf, fub_v, d["e_viga"] * MM)):
            Fc_ = sum([lg.pressao_contato(e_ - dh / 2, tt, db, fu_)] + [lg.pressao_contato(s - dh, tt, db, fu_)] * (npl - 1))
            R.res(f"pressão de contato / rasgamento ({nome}, soma por furo)", "6.3.3.3", max(Ft, Fc), nl * Fc_)
            Lv = e_ + (npl - 1) * s
            R.res(f"colapso por rasgamento ({nome})", "6.5.6", Ft,
                  lg.colapso_rasgamento(2 * Lv * tt, 2 * (Lv - (npl - 0.5) * dn) * tt,
                                        ((bpl if nome == "chapa" else bf) - g - dn) * tt, fyp if nome == "chapa" else fyb, fu_))
        Afn, Afg = (bf - 2 * dn) * tf, bf * tf
        Yt = 1.0 if fyb / fub_v <= 0.8 else 1.1
        if fub_v * Afn < Yt * fyb * Afg:   # Pfeil eqs. 6.8b / 6.9
            R.res("viga: furos na mesa tracionada (fu·Afn·W/Afg)", "5.4.2.5", abs(M),
                  fub_v * Afn / Afg * vg["Wx"] / GA1, "kN·m")
        _espacamento(R, s, db, min(t, tf), "mesa, longitudinal")
        _espacamento(R, g, db, min(t, tf), "mesa, gabarito")
        _bordas(R, db, t, e1, "extremidade da chapa", d["borda_laminada"])
        _bordas(R, db, t, (bpl - g) / 2, "lateral da chapa", d["borda_laminada"])
        _bordas(R, db, tf, d["e_viga"] * MM, "extremidade da viga", d["borda_laminada"])
        for zs, sg in ((H / 2, 1), (-H / 2, -1)):
            for i in range(npl):
                for y in (-g / 2, g / 2):
                    geo["parafusos"].append(_paraf(db, _comprimento_parafuso(t + tf, db),
                                                   [x0 + L1 + i * s, y, zs + sg * t], [0, 0, -sg]))
    else:
        bw_ = d["b_solda_mesa"] * MM
        Lw = d["L_solda"] * MM
        Lp = folga + Lw
        Ct = ct_soldas_longitudinais(Lw, bpl)
        R.res(f"chapa de mesa: ruptura (Ct = {Ct:.2f}, só filetes longitudinais)", "6.5.3 → 5.2.2 b) / 5.2.5",
              Ft, Ct * Ag * fup / GA2)
        R.res("chapa de mesa: compressão (K = 0,65, L = folga)", "6.5.4", Fc,
              compressao_chapa(Ag, 0.65 * max(folga, 1e-3), t / math.sqrt(12), fyp))
        r = lg.resistencia_filete(bw_, fw, min(fyp, fyb), min(fup, fub_v))
        bt = lg.beta_filete_longo(Lw, bw_)   # 6.2.2.2 d): filetes longitudinais da chapa de mesa (força axial)
        R.res("solda chapa–mesa da viga (2 filetes longitudinais" + (f", β = {bt:.3f}" if bt < 1 else "") + ")",
              "6.2.5 / tabela 8" + (" / 6.2.2.2 d)" if bt < 1 else ""), max(Ft, Fc) / (2 * bt * Lw), r["FwRd"], "kN/m")
        _filete(R, bw_, min(t, tf), "chapa–mesa", "b_solda_mesa")
        R.minimo("filetes longitudinais: L ≥ distância entre eles", "6.2.6.2.3 (Pfeil 4.4)", bpl, Lw)
        R.minimo("filete: L ≥ 4b e ≥ 40 mm", "6.2.6.2.3", max(4 * bw_, 0.040), Lw)
        for zs in (H / 2 + t, -(H / 2 + t)):
            for y in (-bpl / 2, bpl / 2):
                geo["soldas"].append(_solda("filete", bw_, [[x0 + folga, y, zs], [x0 + Lp, y, zs]]))
    # solda chapa–pilar
    if d["solda_pilar"] == "penetracao":
        R.res("solda chapa–pilar (penetração total): metal base", "6.2.5 / tabela 8", max(Ft, Fc), Ag * fyp / GA1)
    else:
        bp_ = d["b_solda_pilar"] * MM
        r = lg.resistencia_filete(bp_, fw, fyp, fup)
        R.res("solda chapa–pilar (2 filetes)", "6.2.5 / tabela 8", max(Ft, Fc) / (2 * bpl), r["FwRd"], "kN/m")
        _filete(R, bp_, t, "chapa–pilar", "b_solda_pilar")
    # alma: chapa simples (primitiva de aco.ligacoes)
    cs = dict(h=d["h_alma"] * MM, t=d["t_alma"] * MM, aco_chapa=d["aco_chapa"], d_mm=d["d_alma_mm"],
              tipo_parafuso=d["tipo_parafuso"], rosca_no_plano=d["rosca_no_plano"], n=int(d["n_alma"]),
              s=d["s_alma"] * MM, eh=0.035, b_solda=d["b_solda_alma"] * MM, eletrodo=d["eletrodo"], tw=tw,
              aco_alma=d["aco_viga"], e_alma=0.035, e=folga + 0.035)
    rs = lg.verificar_ligacao("chapa_simples", cs, {"V": abs(V)})
    for x in rs["verificacoes"]:
        un = "mm" if x["unidade"] == "m" else x["unidade"]
        f = 1 / MM if x["unidade"] == "m" else 1.0
        R.v.append(dict(nome="alma (chapa simples): " + x["nome"], item="NBR 8800 " + x["item"],
                        solicitante=round(x["solicitante"] * f, 4), resistente=round(x["resistente"] * f, 4),
                        unidade=un, taxa=round(x["taxa"], 4), ok=x["taxa"] <= 1 + 1e-9))
    av += ["alma: " + a for a in rs["avisos"] if "apoio" not in a]
    R.res("viga: momento resistente (Zx·fy/γa1 ≤ 1,5·Wx·fy/γa1)", "5.4.2", abs(M),
          min(vg["Zx"], 1.5 * vg["Wx"]) * fyb / GA1, "kN·m")
    if pil is not None:
        ln = t + (2 * d["b_solda_pilar"] * MM if d["solda_pilar"] == "filete" else 0.0)
        # H fictício = braço + t: H − tf = braço entre os centros das chapas (painel e enrijecedores)
        _pilar(R, d, pil, fyc, Ft, Fc, ln, bpl, t, braco + t, M, True, geo, x0)
    for zs, nm in ((H / 2, "chapa de mesa superior"), (-H / 2, "chapa de mesa inferior")):
        geo["chapas"].append(_chapa(nm, [[0, -bpl / 2], [Lp, -bpl / 2], [Lp, bpl / 2], [0, bpl / 2]],
                                    [x0, 0, zs], [0, 0, 1 if zs > 0 else -1], [1, 0, 0], t))
        geo["soldas"].append(_solda(d["solda_pilar"], t, [[x0, -bpl / 2, zs], [x0, bpl / 2, zs]]))
    ha = d["h_alma"] * MM
    geo["chapas"].append(_chapa("chapa de alma", [[0, -ha / 2], [folga + 0.070, -ha / 2], [folga + 0.070, ha / 2],
                                                   [0, ha / 2]], [x0, tw / 2, 0], [0, 1, 0], [1, 0, 0], d["t_alma"] * MM))
    da = d["d_alma_mm"] * MM
    for i in range(int(d["n_alma"])):
        z = (i - (d["n_alma"] - 1) / 2) * d["s_alma"] * MM
        geo["parafusos"].append(_paraf(da, _comprimento_parafuso(tw + d["t_alma"] * MM, da),
                                       [x0 + folga + 0.035, tw / 2 + d["t_alma"] * MM, z], [0, -1, 0]))
    geo["recortes"].append(dict(barra="viga", tipo="recuo", parametros=dict(folga=_mm(folga))))
    out = R.resultado(geo)
    out["intermediarios"] = dict(Ft=Ft, Fc=Fc, braco=_mm(braco), comprimento_chapa=_mm(Lp))
    return out


# ---------------------------------------------------------------- painel de alma do pilar (isolado)

def verificar_painel_alma(dados, esforcos):
    d = _ler("painel_alma_pilar", dados)
    R = _R()
    e = _esforcos(esforcos, R.avisos)
    vg = _perfil(d["perfil_viga"], "perfil_viga")
    pil = _perfil(d["perfil_pilar"], "perfil_pilar")
    fyc, _ = _aco(d["aco_pilar"])
    H, tf, bf = vg["h"], vg["tf"], vg["b"]
    Ft = abs(e["M"]) / (H - tf) + max(e["N"], 0) / 2
    Fc = abs(e["M"]) / (H - tf) + max(-e["N"], 0) / 2
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[])
    xf = pil["h"] / 2                                  # face da mesa do pilar (origem no eixo do pilar)
    _pilar(R, d, pil, fyc, Ft, Fc, d["ln"] * MM or tf, bf, tf, H, e["M"], d["mesa_soldada"], geo, xf)
    if d["mesa_soldada"]:
        # W8-RIG: o padrão é a viga soldada direto na mesa do pilar — encostada (folga 0), mesas com penetração total e
        # alma com filete dos dois lados. Só desenho: este tipo verifica o pilar; a solda da viga não é dimensionada
        ctx = (dados or {}).get("contexto_pontas") or {}
        geo["pontas"] = [_pontas.pontas_locais(ctx.get("tipo_no", "viga_pilar_mesa"), vg, pil,
                                               comprimento=ctx.get("comprimento", math.inf), desloc=ctx.get("desloc"),
                                               params=dict(ctx.get("params") or {}, folga=0.0))[0]]
        for zf in (H / 2, H / 2 - tf, -H / 2, -(H / 2 - tf)):
            geo["soldas"].append(dict(_solda("penetracao", tf, [[xf, -bf / 2, zf], [xf, bf / 2, zf]]), campo="mesa_soldada"))
        bw = max(lg.filete_minimo(min(vg["tw"], pil["tf"])), min(0.005, lg.filete_maximo(vg["tw"])))
        for y in (-vg["tw"] / 2, vg["tw"] / 2):
            geo["soldas"].append(dict(_solda("filete", bw, [[xf, y, -(H / 2 - tf)], [xf, y, H / 2 - tf]]),
                                      campo="mesa_soldada"))
        R.avisos.append("viga soldada na mesa do pilar (mesas com penetração total, alma com filete dos dois lados): "
                        "a solda da viga não é dimensionada neste tipo (só desenhada; confira pela 6.2)")
    out = R.resultado(geo)
    out["intermediarios"] = dict(Ft=Ft, Fc=Fc)
    return out


# ---------------------------------------------------------------- emendas (viga e pilar)

def _emenda(d, esforcos, pilar):
    R = _R()
    av = R.avisos
    e = _esforcos(esforcos, av)
    pf_ = _perfil(d["perfil"], "perfil")
    H, bf, tw, tf = pf_["h"], pf_["b"], pf_["tw"], pf_["tf"]
    _minimo_6152(e, H, av)
    M, N, V = e["M"], e["N"], e["V"]
    fyb, fub_v = _aco(d["aco_perfil"])
    fyp, fup = _aco(d["aco_chapa"])
    Af, Aw = bf * tf, (H - 2 * tf) * tw
    A = 2 * Af + Aw
    if d["minimo_50"]:   # Pfeil p. 244: emenda fora do ponto crítico → 50 % da capacidade
        M = math.copysign(max(abs(M), 0.5 * vg_mrd(pf_, fyb)), M or 1)
        V = max(abs(V), 0.5 * 0.6 * fyb * H * tw / GA1)
    Mf, Mw = divisao_momento(abs(M), Af, Aw)
    contato = pilar and d["contato"]
    # força nas talas de mesa (plano de corte na face externa da mesa: braço = H)
    Nf, Nw = N * Af / A, N * Aw / A
    if contato:
        Ft = max(Mf / H + Nf, 0.0)          # compressão passa por contato (Pfeil 9.1, p. 242)
        Fc = 0.0
        Nw_t = max(Nw, 0.0)
        Apb = A
        R.res("contato usinado: pressão de contato (1,8·fy·A/γa2)", "6.6.1", max(-N, 0) + Mf / H, 1.8 * fyb * Apb / GA2)
        av.append("emenda por contato: as superfícies devem ser usinadas; talas e parafusos levam tração, cortante e "
                  "posicionamento (Pfeil 9.1, p. 242). Emendar perto de ponto de contenção lateral")
    else:   # tala mais tracionada e mais comprimida (N dividida pelas áreas)
        Ft = Mf / H + max(Nf, 0.0)
        Fc = Mf / H + max(-Nf, 0.0)
        Nw_t = Nw
    Fmax = max(Ft, Fc)
    # --- mesa
    dbm, fubm, altam, dhm, _FtRd, FvRdm, a0 = _par(dict(d, d_mm=d["d_mesa_mm"]))
    av += a0
    dnm = dhm + lg.FURO_DANO
    tm, bm = d["t_tala_mesa"] * MM, (d["b_tala_mesa"] or bf / MM) * MM
    npl, s, e1, g = d["n_por_linha_mesa"], d["s_mesa"] * MM, d["e1_mesa"] * MM, (d["g_mesa"] or bf / MM / 2 + 10) * MM
    nl = 2
    nm = nl * npl
    folga = d["folga"] * MM
    ev = d["e_mesa_perfil"] * MM
    Ltm = 2 * (folga / 2 + ev + (npl - 1) * s + e1)
    R.res("parafusos da mesa: cisalhamento (corte simples)", "6.3.3.2", Fmax / nm, FvRdm)
    for nome, tt, fu_, ee in (("tala da mesa", tm, fup, e1), ("mesa do perfil", tf, fub_v, ev)):
        Fcr = nl * sum([lg.pressao_contato(ee - dhm / 2, tt, dbm, fu_)] + [lg.pressao_contato(s - dhm, tt, dbm, fu_)] * (npl - 1))
        R.res(f"pressão de contato / rasgamento ({nome}, soma por furo)", "6.3.3.3", Fmax, Fcr)
        Lv = ee + (npl - 1) * s
        larg = bm if nome.startswith("tala") else bf
        R.res(f"colapso por rasgamento ({nome})", "6.5.6", Ft,
              lg.colapso_rasgamento(2 * Lv * tt, 2 * (Lv - (npl - 0.5) * dnm) * tt, (larg - g - dnm) * tt,
                                    fyp if nome.startswith("tala") else fyb, fu_))
    Ag = bm * tm
    tr = lg.tracao_chapa(Ag, (bm - nl * dnm) * tm, fyp, fup)
    R.res("tala da mesa: escoamento da seção bruta", "6.5.3 → 5.2.2 a)", Ft, tr["escoamento"])
    R.res("tala da mesa: ruptura da seção líquida (Ae ≤ 0,85Ag)", "6.5.3 → 5.2.2 b)", Ft, tr["ruptura"])
    if Fc > 0:
        R.res("tala da mesa: compressão (K = 0,65, L = folga + 2·borda)", "6.5.4", Fc,
              compressao_chapa(Ag, 0.65 * (folga + 2 * ev), tm / math.sqrt(12), fyp))
    if not pilar:
        Afn = (bf - nl * dnm) * tf
        Yt = 1.0 if fyb / fub_v <= 0.8 else 1.1
        if fub_v * Afn < Yt * fyb * Af:
            R.res("viga: furos na mesa tracionada (fu·Afn·W/Afg)", "5.4.2.5", abs(M),
                  fub_v * Afn / Af * pf_["Wx"] / GA1, "kN·m")
    # --- alma
    dba, fuba, altaa, dha, _f, FvRda, a1 = _par(dict(d, d_mm=d["d_alma_mm"]))
    av += [a for a in a1 if a not in av]
    dna = dha + lg.FURO_DANO
    ta, ha = d["t_tala_alma"] * MM, d["h_tala_alma"] * MM
    nr, nc, sv, sh = d["n_linhas_alma"], d["n_colunas_alma"], d["s_alma"] * MM, d["sh_alma"] * MM
    ee = d["e_emenda_alma"] * MM                     # eixo da emenda → 1ª coluna
    xs = [ee + j * sh for j in range(nc) for _i in range(nr)]
    ys = [(i - (nr - 1) / 2) * sv for _j in range(nc) for i in range(nr)]
    a_exc = ee + (nc - 1) * sh / 2
    M0 = Mw + abs(V) * a_exc
    Fw, r2 = grupo_excentrico(xs, ys, abs(V), Nw_t, M0)
    R.res(f"parafusos da alma: cisalhamento duplo (M0 = Mw + V·a = {M0:.1f} kN·m)", "6.3.3.2", Fw, 2 * FvRda)
    lf_t = min(sv - dha, (ha - (nr - 1) * sv) / 2 - dha / 2, d["e_tala_alma"] * MM - dha / 2)
    lf_w = min(sv - dha, ee - folga / 2 - dha / 2)
    R.res("pressão de contato nas talas da alma (2 talas)", "6.3.3.3", Fw, 2 * lg.pressao_contato(lf_t, ta, dba, fup))
    R.res("pressão de contato na alma do perfil", "6.3.3.3", Fw, lg.pressao_contato(lf_w, tw, dba, fub_v))
    c = lg.cisalhamento_chapa(2 * ha * ta, 2 * (ha - nr * dna) * ta, fyp, fup)
    R.res("talas da alma: escoamento por cisalhamento", "6.5.5", abs(V), c["escoamento"])
    R.res("talas da alma: ruptura por cisalhamento", "6.5.5", abs(V), c["ruptura"])
    MRd_ta = 2 * ta * ha ** 2 / 4 * fyp / GA1
    R.res("talas da alma: flexão (Z·fy/γa1, M0)", "6.5 (Pfeil 9.10.1 d)", M0, MRd_ta, "kN·m")
    # parcela axial da alma Nw = N·Aw/A nas duas talas: 6.5.3 (tração) / 6.5.4 (compressão) e interação com M0
    NRd_ta = None
    if Nw_t > 0:
        tra = lg.tracao_chapa(2 * ha * ta, 2 * (ha - nr * dna) * ta, fyp, fup)
        R.res("talas da alma: tração Nw (escoamento da seção bruta)", "6.5.3 → 5.2.2 a)", Nw_t, tra["escoamento"])
        R.res("talas da alma: tração Nw (ruptura da seção líquida, An ≤ 0,85Ag)", "6.5.3 → 5.2.2 b)", Nw_t,
              tra["ruptura"])
        NRd_ta = min(tra["escoamento"], tra["ruptura"])
    elif Nw_t < 0:   # sem contato: KL = 0,65 · (distância livre entre as primeiras colunas, 2·ee), como na mesa
        NRd_ta = compressao_chapa(2 * ha * ta, 0.65 * 2 * ee, ta / math.sqrt(12), fyp)
        R.res("talas da alma: compressão Nw (K = 0,65, L = 2·e_emenda)", "6.5.4", Nw_t, NRd_ta)
    if NRd_ta and M0 > 0:
        nr_ = abs(Nw_t) / NRd_ta
        R.res("talas da alma: Nw + M0 (interação)", "6.5 → 5.5.1.2",
              nr_ + 8 / 9 * M0 / MRd_ta if nr_ >= 0.2 else nr_ / 2 + M0 / MRd_ta, 1.0, "-")
    hw = H - 2 * tf
    R.res("alma do perfil: ruptura por cisalhamento na seção líquida", "6.5.5", abs(V),
          0.6 * fub_v * (hw - nr * dna) * tw / GA2)
    # detalhamento
    _espacamento(R, s, dbm, min(tm, tf), "mesa")
    _espacamento(R, g, dbm, min(tm, tf), "gabarito da mesa")
    _espacamento(R, sv, dba, min(ta, tw), "alma, vertical")
    if nc > 1:
        _espacamento(R, sh, dba, min(ta, tw), "alma, horizontal")
    _bordas(R, dbm, tm, e1, "extremidade da tala da mesa", d["borda_laminada"])
    _bordas(R, dbm, tm, (bm - g) / 2, "lateral da tala da mesa", d["borda_laminada"])
    _bordas(R, dbm, tf, ev, "extremidade da mesa do perfil", d["borda_laminada"])
    _bordas(R, dba, ta, (ha - (nr - 1) * sv) / 2, "tala da alma, vertical", d["borda_laminada"])
    _bordas(R, dba, ta, d["e_tala_alma"] * MM, "tala da alma, horizontal", d["borda_laminada"])
    R.maximo("tala da alma cabe entre as mesas (h ≤ hw − 2r)", "Pfeil 9.10.1 (geometria)", ha,
             hw - 2 * pf_.get("r", 0.0))
    if pilar and N < 0 and not contato:
        av.append("emenda sem contato: talas e parafusos transmitem toda a compressão")
    # geometria (x ao longo da barra, origem no eixo da emenda)
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[])
    for zs, sg in ((H / 2, 1), (-H / 2, -1)):
        geo["chapas"].append(_chapa("tala da mesa", [[-Ltm / 2, -bm / 2], [Ltm / 2, -bm / 2], [Ltm / 2, bm / 2],
                                                      [-Ltm / 2, bm / 2]], [0, 0, zs], [0, 0, sg], [1, 0, 0], tm))
        for lado in (-1, 1):
            for i in range(npl):
                for y in (-g / 2, g / 2):
                    geo["parafusos"].append(_paraf(dbm, _comprimento_parafuso(tm + tf, dbm),
                                                   [lado * (folga / 2 + ev + i * s), y, zs + sg * tm], [0, 0, -sg]))
    Lta = 2 * (ee + (nc - 1) * sh + d["e_tala_alma"] * MM)
    for sg in (1, -1):
        geo["chapas"].append(_chapa("tala da alma", [[-Lta / 2, -ha / 2], [Lta / 2, -ha / 2], [Lta / 2, ha / 2],
                                                      [-Lta / 2, ha / 2]], [0, sg * tw / 2, 0], [0, sg, 0], [1, 0, 0], ta))
    for lado in (-1, 1):
        for x, y in zip(xs, ys):
            geo["parafusos"].append(_paraf(dba, _comprimento_parafuso(2 * ta + tw, dba),
                                           [lado * x, tw / 2 + ta, y], [0, -1, 0]))
    out = R.resultado(geo)
    out["intermediarios"] = dict(Mf=Mf, Mw=Mw, Ft=Ft, Fc=Fc, M0=M0, sum_r2=r2, Fw=Fw, FvRd_mesa=FvRdm,
                                 FvRd_alma=FvRda, comprimento_tala_mesa=_mm(Ltm), comprimento_tala_alma=_mm(Lta))
    return out


def vg_mrd(p, fy):
    return min(p["Zx"], 1.5 * p["Wx"]) * fy / GA1


def verificar_emenda_viga(dados, esforcos):
    d = _ler("emenda_viga", dados)
    d["perfil"], d["aco_perfil"] = d.pop("perfil_viga"), d.pop("aco_viga")
    return _emenda(d, esforcos, False)


def verificar_emenda_pilar(dados, esforcos):
    d = _ler("emenda_pilar", dados)
    d["perfil"], d["aco_perfil"] = d.pop("perfil_pilar"), d.pop("aco_pilar")
    d["minimo_50"] = False
    return _emenda(d, esforcos, True)


# ---------------------------------------------------------------- emenda flangeada (W9, docs/v3/W9-FLANGE-PERFIL.md)
# Em cada ponta uma chapa de topo ("chapa de flange") soldada ao perfil; as duas chapas aparafusadas entre si. É a chapa
# de topo dos dois lados: os componentes (tstub, momento_efetivo, grupo elástico) são os da chapa de topo, com as duas
# chapas iguais (a verificação de uma vale para a outra). Perfis I/H e U (UPN, U laminado, U dobrado); metalon e tubo
# redondo ficam em ligacoes_tubos.

_U_FLANGE = ("U laminado", "UPN", "U formado a frio")   # U dobrado simples; Ue (enrijecido) fora: os enrijecedores
PERFIS_FLANGE = list(dict.fromkeys(lin["nome"] for lin in secoes._carrega_catalogo().values()
                                   if lin["forma"] == "I" or lin["forma"] == "U" and lin["familia"] in _U_FLANGE))


def _perfil_iu(v, campo, ue=False):
    """I/H (como _perfil, inclusive dict {d, bf, tw, tf}) ou U do catálogo → props em m com `forma` ("I"/"U"), `xg`
    (centroide → costas da alma), `r` e `formado` (U dobrado: tw = tf = t, r = ri + t; laminado: r = tf, LIGACOES-U).
    ue=True (W11, chapa de topo ajustada): aceita também o Ue dobrado, com o enrijecedor de borda D (U: D = 0)."""
    if isinstance(v, dict) or not v:
        return dict(_perfil(v, campo), forma="I", xg=0.0, formado=False)
    try:
        s = secoes.catalogo(str(v))
    except KeyError as ex:
        raise ValueError(f"{campo}: {ex.args[0]}") from None
    p = s.props
    if p.get("tipo") == "I":
        return dict(_perfil(v, campo), forma="I", xg=0.0, formado=False)
    if ue and p.get("tipo") == "Ue":
        p = dict(p, tipo="U")
    if p.get("tipo") != "U":
        raise ValueError(f"{campo}: emenda flangeada só com perfil I/H ou U (UPN, U laminado, U dobrado) — recebido "
                         f"{s.nome}" + (" (U enrijecido: os enrijecedores de borda impedem a chapa)" if p.get("tipo") == "Ue"
                                        else "; metalon e tubo: emenda flangeada de tubos"))
    t = p.get("t")
    tw, tf = p.get("tw") or t, p.get("tf") or t
    return dict(p, nome=s.nome, forma="U", tw=tw, tf=tf, r=p.get("r") or ((p.get("ri") or 0.0) + t if t else tf),
                formado=bool(t), D=p.get("D") or 0.0)


def _esf_flange(esforcos, braco, av):
    e = _esforcos(esforcos, av, usados=("N", "V", "Vz", "M", "Mz", "T"))
    _minimo_6152(e, braco, av)
    return e


def _forcas_mesa(e, braco, bf, contato):
    """Força normal na solda da mesa mais solicitada (tração: M/braço + N⁺/2; compressão: M/braço + N⁻/2, que com faces
    usinadas passa por contato e sai da solda) + Mz no plano da mesa (3·Mz/bf, como na chapa de topo) e a parcela de
    cisalhamento Fy/2 + T/braço. → (normal, cisalhamento), kN."""
    extra = 3 * abs(e["Mz"]) / bf
    Ft = momento_efetivo(e["M"], e["N"], braco) / braco + extra
    Fc = 0.0 if contato else abs(e["M"]) / braco + max(-e["N"], 0.0) / 2 + extra
    return max(Ft, Fc), abs(e["Vz"]) / 2 + abs(e["T"]) / braco


def _g_flange(pf_, dmm):
    """Gabarito automático (mm), com a folga de 10 % do W8-RIG nas duas disposições que ele controla: folga de chave
    para a alma (g − tw)/2 ≥ d e espaçamento 2,7d → g ≥ max(2d/0,9 + tw; 2,7d/0,9), para cima a 5. I: no mínimo bf/2
    arredondado a 5 (como a chapa de topo). U: só o mínimo (as duas colunas simétricas ao plano médio da alma)."""
    tw = pf_["tw"] / MM
    gmin = math.ceil(max(2 * dmm / ALVO + tw, 2.7 * dmm / ALVO) / 5 - 1e-9) * 5
    return gmin if pf_["forma"] == "U" else max(round(pf_["b"] / MM / 2 / 5) * 5, gmin)


def _flange(d, esforcos, pilar, dados):
    R = _R()
    av = R.avisos
    chave = "perfil_pilar" if pilar else "perfil_viga"
    pf_ = _perfil_iu(d[chave], chave)
    H, bf, tw, tf, U = pf_["h"], pf_["b"], pf_["tw"], pf_["tf"], pf_["forma"] == "U"
    ab = -1.0 if U and float((dados or {}).get("abertura_y") or 1) < 0 else 1.0
    # referencial do contrato (x ao longo da barra, z = altura): U abre para +y·ab, costas da alma em y = −ab·xg
    yw = ab * (tw / 2 - pf_["xg"]) if U else 0.0                          # plano médio da alma = centro da chapa
    yf = tuple(sorted((-ab * pf_["xg"], ab * (bf - pf_["xg"])))) if U else (-bf / 2, bf / 2)   # mesas em y
    fyb, fub_v = _aco(d["aco_pilar" if pilar else "aco_viga"])
    fyp, fup = _aco(d["aco_chapa"])
    db, fub, alta, dh, FtRd, FvRd, a0 = _par(d)
    av += a0
    dn = dh + lg.FURO_DANO
    tp = d["tp"] * MM
    braco = H - tf
    e = _esf_flange(esforcos, braco, av)
    M, N, V, Vh, Mz, Tx = e["M"], e["N"], e["V"], e["Vz"], e["Mz"], e["T"]
    # colunas (y) simétricas ao plano médio da alma
    nc = int(d["n_colunas"])
    g = (d["g"] or _g_flange(pf_, d["d_mm"])) * MM
    g2 = (d["g2"] or g / MM + 2 * _passo(d["d_mm"])) * MM
    ys = sorted([yw - g / 2, yw + g / 2] + ([yw - g2 / 2, yw + g2 / 2] if nc == 4 else []))
    if nc == 4 and g2 <= g:
        raise ValueError(f"g2 = {g2 / MM:g} mm ≤ g = {g / MM:g} mm: as colunas externas ficam por fora das internas")
    # linhas (z): fora das mesas (estendida) a pf da face externa; dentro, a pf da face interna e depois a cada s_linhas
    pf_min = next((p for dd, p in _PF_MIN if d["d_mm"] <= dd + 0.05), 1.6 * db)
    pf = d["pf"] * MM if d["pf"] else pf_min
    de, sobra, sl, nd = d["de"] * MM, d["sobra"] * MM, d["s_linhas"] * MM, int(d["linhas_dentro"])
    est = d["estendida"]
    fora = {s: est == "ambas" or (est, s) in (("superior", 1), ("inferior", -1)) for s in (1, -1)}
    if nd == 0 and not any(fora.values()):
        raise ValueError("sem parafusos: linhas_dentro = 0 e nenhuma linha fora das mesas")
    if nd and H / 2 - tf - pf - (nd - 1) * sl <= 0:
        raise ValueError(f"linhas_dentro = {nd}: as linhas passam do meio da alma (H/2 − tf − pf − (n − 1)·s ≤ 0)")
    linhas = []
    for s in (1, -1):
        if fora[s]:
            linhas.append(dict(z=s * (H / 2 + pf), tipo="ext", lado=s))
        linhas += [dict(z=s * (H / 2 - tf - pf - k * sl), tipo="int" if k == 0 else "alma", lado=s) for k in range(nd)]
    # chapa: envolve o perfil com a sobra e os furos com a borda (folga de 10 % do pré-dimensionamento); medidas
    # informadas maiores crescem em volta do mesmo centro; quadrada = lado maior nas duas direções
    e_lat = _borda(d["d_mm"]) * MM
    meia = max(max(abs(y - yw) for y in ys) + e_lat, max(yf[1] - yw, yw - yf[0]) + sobra)
    y0, y1 = yw - meia, yw + meia
    zt = H / 2 + (pf + de if fora[1] else sobra)
    zb = -(H / 2 + (pf + de if fora[-1] else sobra))
    if d["b_chapa"]:
        y0, y1 = yw - d["b_chapa"] * MM / 2, yw + d["b_chapa"] * MM / 2
    if d["h_chapa"]:
        zm = (zt + zb) / 2
        zb, zt = zm - d["h_chapa"] * MM / 2, zm + d["h_chapa"] * MM / 2
    if d["forma"] == "quadrada":
        lado = max(y1 - y0, zt - zb)
        ym, zm = (y0 + y1) / 2, (zb + zt) / 2
        y0, y1, zb, zt = ym - lado / 2, ym + lado / 2, zm - lado / 2, zm + lado / 2
    bp, hp = y1 - y0, zt - zb
    if d["b_chapa"]:
        R.minimo("chapa cobre o perfil (largura)", "geometria", 2 * max(yf[1] - yw, yw - yf[0]), bp)
    if d["h_chapa"]:
        R.minimo("chapa cobre o perfil (altura)", "geometria", H, hp)

    def tstub_furo(L, y, T=0.0):
        """T-stub de um parafuso (Struik–de Back, `tstub`): b até a mesa (pf) ou até a alma ((|y − yw| − tw/2)), o que
        estiver mais perto; fora da largura da mesa (U: coluna atrás da alma; 4 colunas além da mesa), b soma a
        distância até a mesa (a favor). a = borda livre do lado oposto; p = largura da chapa / colunas."""
        sob = yf[0] - 1e-9 <= y <= yf[1] + 1e-9
        bw = abs(y - yw) - tw / 2
        if L["tipo"] == "ext":
            b = pf + (0.0 if sob else min(abs(y - yf[0]), abs(y - yf[1])))
            a = (zt if L["lado"] == 1 else -zb) - abs(L["z"])
        elif L["tipo"] == "int" and sob and pf <= bw:
            b, a = pf, 1.25 * pf
        else:
            b, a = bw, min(y1 - y, y - y0)
        p = bp / nc if L["tipo"] != "alma" else min(sl, bp / nc)
        return tstub(T, p, tp, b, a, db, dh, fyp, FtRd)

    s_t = 1 if M >= 0 else -1
    zc = -s_t * (H / 2 - tf / 2)                       # centro da mesa comprimida
    tr = [L for L in linhas if L["z"] * s_t > 0]
    F_op = N / 2 - abs(M) / braco                      # tração axial maior que o binário: a outra mesa também traciona
    co = [L for L in linhas if L["z"] * s_t < 0] if N > 0 and F_op > 1e-9 else []
    for L in tr + co:
        L["h"] = abs(L["z"] - zc)
        L["Tcap"] = [tstub_furo(L, y)["Tcap"] for y in ys]
    Mef = momento_efetivo(M, N, braco)
    MRd = sum(sum(L["Tcap"]) * L["h"] for L in tr)
    R.res("momento: parafusos tracionados + chapas de flange (T-stub, alavanca)", "6.3.3.1 / 6.3.5", Mef, MRd, "kN·m")
    fator = Mef / MRd if MRd > 0 else 0.0
    fator_op = 0.0
    if co:
        Rop = sum(sum(L["Tcap"]) for L in co)
        R.res("tração axial: linhas da mesa oposta (N/2 − |M|/braço)", "6.3.3.1 / 6.3.5", F_op, Rop)
        fator_op = F_op / Rop
    furos = [(y, L["z"]) for L in linhas for y in ys]
    n_tot = len(furos)
    # cortante por parafuso: grupo elástico no plano da chapa com Fy (Vz), Fz (V) e a torção (Ip = Σ r²); sem Fy e T = V/n
    zg = sum(z for _, z in furos) / n_tot
    Ip = sum((y - yw) ** 2 + (z - zg) ** 2 for y, z in furos)
    Vb = max(math.hypot(Vh / n_tot - Tx * (z - zg) / Ip, V / n_tot + Tx * (y - yw) / Ip) for y, z in furos)
    # Mz: distribuição linear em torno do plano médio da alma (sem contar o contato na borda: a favor)
    Iy = sum((y - yw) ** 2 for y, _ in furos)
    dT = [abs(Mz) * abs(y - yw) / Iy for y in ys]
    Tmax = 0.0
    nomes = dict(ext="fora da mesa", int="junto à mesa", alma="junto à alma")
    for i, L in enumerate(tr + co, 1):
        f = fator if i <= len(tr) else fator_op
        cs = []
        for k, y in enumerate(ys):
            T = L["Tcap"][k] * f + dT[k]
            cs.append((T, tstub_furo(L, y, T)))
            Tmax = max(Tmax, T)
        nome = f"linha {i} ({nomes[L['tipo']]}, h = {L['h'] / MM:.0f} mm)"
        T, c = max(cs, key=lambda x: x[0] / x[1]["T_chapa"])
        R.res(f"{nome}: flexão das chapas de flange (T-stub, α = {min(max(c['alfa'], 0), 1):.2f})", "6.3.5",
              T, c["T_chapa"])
        T, c = max(cs, key=lambda x: x[1]["B"])
        R.res(f"{nome}: tração no parafuso com alavanca B = T + Q (Q = {c['Q']:.1f} kN)", "6.3.3.1 / 6.3.5",
              c["B"], FtRd)
        if Vb > 0:
            R.res(f"{nome}: tração + cisalhamento no parafuso", "6.3.3.4",
                  lg.interacao_tracao_cisalhamento(c["B"], FtRd, Vb, FvRd), 1.0, "-")
    if max(dT) > 0 and len(tr + co) < len(linhas):   # linhas comprimidas pelo My também tracionam pelo Mz
        R.res("parafusos fora da zona tracionada: tração por Mz", "6.3.3.1", max(dT), FtRd)
        R.res("parafusos fora da zona tracionada: tração (Mz) + cisalhamento", "6.3.3.4",
              lg.interacao_tracao_cisalhamento(max(dT), FtRd, Vb, FvRd), 1.0, "-")
        Tmax = max(Tmax, max(dT))
    tem3d = any(abs(x) > 1e-9 for x in (Vh, Mz, Tx))
    R.res("parafusos: cisalhamento (grupo elástico: Fz, Fy e Mx)" if tem3d else
          "parafusos: cisalhamento (V/n, todos os parafusos)", "6.3.3.2", Vb, FvRd)
    if d["atrito"]:
        if not alta:
            raise ValueError("ligação por atrito: só com parafuso de alta resistência (A325, A490, ISO 8.8/10.9)")
        # 6.3.4.3: Ff,Rd = 1,13·μ·Ch·FTb·ns·(1 − Ft,Sd/(1,13·FTb))/γe, Ch = 1 (furo-padrão), ns = 1 (um plano entre as
        # chapas), γe = 1,20. FTb ≈ 70 % de fub na área efetiva 0,75·Ab (a tabela 15 dá até 4 % mais: a favor)
        FTb = 0.70 * fub * 0.75 * lg.area_parafuso(db)
        FfRd = 1.13 * d["mu"] * FTb * max(1 - Tmax / (1.13 * FTb), 0.0) / 1.20
        R.res(f"parafusos: deslizamento (atrito, μ = {d['mu']:.2f}, FTb = {FTb:.0f} kN)", "6.3.4.3", Vb, max(FfRd, 1e-9))
    zs = sorted({L["z"] for L in linhas})
    gaps = [zs[i + 1] - zs[i] for i in range(len(zs) - 1)]
    lf = min([x - dh for x in gaps] + [zs[0] - zb - dh / 2, zt - zs[-1] - dh / 2]
             + ([ys[0] - y0 - dh / 2, y1 - ys[-1] - dh / 2] if abs(Vh) > 1e-9 else []))
    R.res("pressão de contato nas chapas de flange", "6.3.3.3", Vb, lg.pressao_contato(lf, tp, db, fup))
    c = lg.cisalhamento_chapa(bp * tp, (bp - nc * dn) * tp, fyp, fup)
    R.res("chapa de flange: escoamento por cisalhamento", "6.5.5", abs(V), c["escoamento"])
    R.res("chapa de flange: ruptura por cisalhamento", "6.5.5", abs(V), c["ruptura"])
    if abs(Vh) > 1e-9:
        c = lg.cisalhamento_chapa(hp * tp, (hp - len(zs) * dn) * tp, fyp, fup)
        R.res("chapa de flange: escoamento por cisalhamento horizontal (Fy)", "6.5.5", abs(Vh), c["escoamento"])
        R.res("chapa de flange: ruptura por cisalhamento horizontal (Fy)", "6.5.5", abs(Vh), c["ruptura"])
    R.res("perfil: momento resistente (Zx·fy/γa1 ≤ 1,5·Wx·fy/γa1)", "5.4.2.2", abs(M),
          min(pf_["Zx"], 1.5 * pf_["Wx"]) * fyb / GA1, "kN·m")
    # contato: com faces usinadas a compressão da mesa passa da ponta do perfil para a chapa por contato (6.6.1); a
    # compressão de uma chapa para a outra é sempre por contato (chapas encostadas)
    if d["contato"]:
        Fcc = abs(M) / braco + max(-N, 0.0) / 2
        if Fcc > 0:
            R.res("contato usinado: compressão da mesa no topo do perfil (1,8·fy·Apb/γa2)", "6.6.1", Fcc,
                  1.8 * min(fyb, fyp) * bf * tf / GA2)
        av.append("faces usinadas: a compressão das mesas passa por contato (6.6.1) — usinar a ponta do perfil antes de "
                  "soldar; soldas e parafusos levam tração, cortante e posicionamento")
    elif pilar and N < 0:
        av.append("sem contato usinado: a compressão passa pelas soldas das mesas")
    # soldas perfil–chapa (iguais nas duas chapas)
    fw = lg.ELETRODOS[d["eletrodo"]] * MPa
    Fn, Fcis = _forcas_mesa(e, braco, bf, d["contato"])
    Lf = bf + (bf - tw)                                   # filete das duas faces da mesa (dentro, menos a alma)
    if d["solda_mesa"] == "penetracao":
        R.res("solda das mesas (penetração total): metal base", "6.2.5 / tabela 8", Fn, bf * tf * min(fyb, fyp) / GA1)
        if Fcis > 1e-9:
            R.res("solda das mesas (penetração total): cisalhamento (Fy, Mx)", "6.2.5 / tabela 8", Fcis,
                  0.6 * bf * tf * min(fyb, fyp) / GA1)
    else:
        bm = d["b_mesa"] * MM
        r = lg.resistencia_filete(bm, fw, min(fyb, fyp), min(fub_v, fup))
        R.res("solda das mesas (filetes nas duas faces)", "6.2.5 / tabela 8", Fn + Fcis, Lf * r["FwRd"])
    bw = d["b_alma"] * MM
    lw = (H - 2 * tf) / 2                                 # CBCA 6.4: só a metade comprimida da alma (como a chapa de topo)
    r = lg.resistencia_filete(bw, fw, min(fyb, fyp), min(fub_v, fup))
    R.res("solda da alma ao cortante (2 filetes, meia altura)", "6.2.5 / tabela 8", abs(V) / (2 * lw), r["FwRd"], "kN/m")
    if pf_["formado"]:   # parede fina: tabela 10 pede 3 mm e 6.2.6.2.2 limita a t; vale a NBR 14762
        av.append(f"U dobrado (t = {tw / MM:.2f} mm): barra pela NBR 14762 e pernas das soldas na parede fina não "
                  "verificadas aqui (use perna = t); a resistência dos filetes está verificada pelo metal base")
    else:
        if d["solda_mesa"] != "penetracao":
            _filete(R, d["b_mesa"] * MM, min(tf, tp), "das mesas", "b_mesa")
        _filete(R, bw, min(tw, tp), "da alma", "b_alma")
    # disposições
    _bordas(R, db, tp, min(ys[0] - y0, y1 - ys[-1]), "lateral da chapa", d["borda_laminada"])
    if any(fora.values()):
        _bordas(R, db, tp, min(zt - zs[-1], zs[0] - zb), "extremidade da chapa", d["borda_laminada"])
    gc = [ys[i + 1] - ys[i] for i in range(len(ys) - 1)]
    for nome, s in (("colunas", gc), ("linhas", gaps)):
        if s:
            R.minimo(f"espaçamento mínimo 2,7db ({nome})", "6.3.9", 2.7 * db, min(s))
            R.maximo(f"espaçamento máximo 24t ≤ 300 mm ({nome})", "6.3.10", max(s), min(24 * tp, 0.300))
    R.minimo("gabarito: folga para a alma (g − tw)/2 ≥ db", "CBCA 6.1.1", db, (g - tw) / 2)
    if any(L["tipo"] in ("ext", "int") for L in linhas):
        R.minimo("furo → face da mesa (pf) ≥ mínimo de aperto", "CBCA 6.1.1", pf_min, pf)
    if U:
        x0 = abs(pf_.get("x0") or 0.0)
        av.append(f"perfil U: centro de cisalhamento a {(x0 - pf_['xg'] + tw / 2) / MM:.0f} mm do plano médio da alma, "
                  "onde ficam a chapa e o grupo de parafusos: a torção V·e na barra não é verificada aqui (LIGACOES-U)")
    av.append("modelo: chapa de topo dos dois lados (T-stub de Struik–de Back por parafuso, distribuição plástica Σ T·h); "
              "as duas chapas são iguais, a verificação de uma vale para a outra; a compressão N não alivia a tração "
              "dos parafusos (a favor)")
    # geometria (mm no contrato; origem no plano da junta, x ao longo da barra)
    cont = [[y0, zb], [y1, zb], [y1, zt], [y0, zt]]
    geo = dict(chapas=[dict(_chapa("chapa de flange", cont, [x, 0, 0], [1, 0, 0], [0, 1, 0], tp), campo="tp")
                       for x in (-tp, 0.0)],
               cantoneiras=[], recortes=[], soldas=[],
               parafusos=[dict(_paraf(db, _comprimento_parafuso(2 * tp, db), [-tp, y, L["z"]], [1, 0, 0]), campo="d_mm")
                          for L in linhas for y in ys])
    tipo_m = "penetracao" if d["solda_mesa"] == "penetracao" else "filete"
    perna_m = tf if tipo_m == "penetracao" else d["b_mesa"] * MM
    yi = yw + ab * tw / 2 if U else None                 # U: face interna da alma
    for xs in (-tp, tp):
        for s in (1, -1):
            zo, zi = s * H / 2, s * (H / 2 - tf)
            trechos = [((yf[0], zo), (yf[1], zo))]
            if U:
                ponta = yf[1] if ab > 0 else yf[0]
                trechos.append(((yi, zi), (ponta, zi)))
            else:
                trechos += [((yf[0], zi), (-tw / 2, zi)), ((tw / 2, zi), (yf[1], zi))]
            for (ya, za), (yb, zb_) in trechos:
                geo["soldas"].append(dict(_solda(tipo_m, perna_m, [[xs, ya, za], [xs, yb, zb_]]),
                                          campo="b_mesa" if tipo_m == "filete" else "solda_mesa"))
        for y in (yw - tw / 2, yw + tw / 2):
            costas = U and abs(y - yi) > 1e-12             # U: costas da alma na altura toda
            hz = H / 2 if costas else H / 2 - tf
            geo["soldas"].append(dict(_solda("filete", bw, [[xs, y, -hz], [xs, y, hz]]), campo="b_alma"))
    Lm = max(H, 0.250)

    def pecas(xa, xb):
        hw = H / 2 - tf
        out = [dict(base=[[_mm(xa), _mm(yw), _mm(-hw)], [_mm(xb), _mm(yw), _mm(-hw)], [_mm(xb), _mm(yw), _mm(hw)],
                          [_mm(xa), _mm(yw), _mm(hw)]], normal=[0, 1, 0], espessura=_mm(tw))]
        for zm in (H / 2 - tf / 2, -(H / 2 - tf / 2)):
            out.append(dict(base=[[_mm(xa), _mm(yf[0]), _mm(zm)], [_mm(xb), _mm(yf[0]), _mm(zm)],
                                  [_mm(xb), _mm(yf[1]), _mm(zm)], [_mm(xa), _mm(yf[1]), _mm(zm)]],
                            normal=[0, 0, 1], espessura=_mm(tf)))
        return out
    geo["membros"] = [dict(barra="pilar" if pilar else "viga", perfil=pf_["nome"], campo=chave, d=_mm(H), bf=_mm(bf),
                           tw=_mm(tw), tf=_mm(tf), pecas=pecas(*xx)) for xx in ((-tp - Lm, -tp), (tp, tp + Lm))]
    if U:
        geo["abertura_y"] = int(ab)
    if pilar:   # W8-CONTRAV: eixos da elevação no referencial local — pilar em pé (x da barra na vertical)
        geo["vista"] = dict(x=[0, 0, -1], z=[1, 0, 0])
    out = R.resultado(geo)
    out["intermediarios"] = dict(MRd=MRd, Mef=Mef, FtRd=FtRd, FvRd=FvRd, Vb=Vb, g=_mm(g), pf=_mm(pf), b_chapa=_mm(bp),
                                 h_chapa=_mm(hp), linhas=[dict(z=_mm(L["z"]), tipo=L["tipo"], h=_mm(L.get("h", 0.0)),
                                                               Tcap=L.get("Tcap")) for L in linhas])
    return out


def verificar_emenda_flangeada_viga(dados, esforcos):
    return _flange(_ler("emenda_flangeada_viga", dados), esforcos, False, dados)


def verificar_emenda_flangeada_pilar(dados, esforcos):
    return _flange(_ler("emenda_flangeada_pilar", dados), esforcos, True, dados)


# ---------------------------------------------------------------- sugerir (pré-dimensionamento por busca)

ALVO = 0.90   # W8-RIG: taxa governante do pré-dimensionamento (a interface mostra "no limite" acima de 0,90)


def _buscar(tipo, base, grade, esforcos, alvo=ALVO):
    """Primeira combinação da grade com taxa ≤ alvo (folga sobre o limite, inclusive nas disposições construtivas);
    senão a primeira que passa; senão a de menor taxa (o verificar mostra a falha).
    Com pilar, cada combinação tenta também enrijecedores e chapa de reforço (do mais leve ao mais pesado)."""
    pilar = [{}] + ([dict(t_enr_pilar=12.5), dict(t_enr_pilar=16.0, t_reforco=9.5),
                     dict(t_enr_pilar=19.0, t_reforco=16.0)]
                    if base.get("perfil_pilar") and "t_enr_pilar" in TIPOS[tipo]["entradas"] else [])
    melhor = passa = None
    for extra in grade:
        for pp in pilar:
            dados = dict(base, **extra, **pp)
            try:
                r = TIPOS[tipo]["verificar"](dados, esforcos)
            except ValueError:
                continue
            if r["ok"] and r["taxa"] <= alvo + 1e-9:
                return dados
            if r["ok"] and passa is None:
                passa = dados
            if melhor is None or r["taxa"] < melhor[0]:
                melhor = (r["taxa"], dados)
    return passa or (melhor[1] if melhor else dict(base, **grade[0]))


_DS = (15.875, 19.05, 22.225, 25.4)
_TS = (12.5, 16.0, 19.0, 22.4, 25.0, 31.5, 37.5, 44.5, 50.0)


def _com_folga(minimo_mm):
    """W8-RIG: mínimo de detalhamento / ALVO, para cima a 5 mm (taxa ≤ 0,90 na disposição construtiva)."""
    return math.ceil(minimo_mm / ALVO / 5 - 1e-9) * 5


def _borda(dmm):
    """Borda do pré-dimensionamento (mm): tabela 14 (serra/tesoura) com folga de 10 %, para cima a 5 mm —
    ⅝" 35, ¾" 40, ⅞" 45, 1" 50. Antes era a tabela 14 só arredondada (⅝" 30 contra 29: 97 %, W8-RIG). Bate com o
    padrão CBCA 2.4.4 (40 mm para ¾") e fica acima dele onde o CBCA cai no limite da tabela 14 (⅝" 30, ⅞"/1" 40)."""
    return _com_folga(lg.distancia_min_borda(dmm * MM) / MM)


def _passo(dmm):
    """Espaçamento usual (mm): CBCA 2.4.3 (60 mm para ⅝", 75 mm para os demais), ≥ 3d (NBR 6.3.9: 2,7d mínimo,
    3d preferível = 2,7d com a folga de 10 %), para cima a 5 mm — ⅝" 60, ¾" 75, ⅞" 75, 1" 80."""
    return max(60 if dmm <= 16 else 75, math.ceil(3 * dmm / 5 - 1e-9) * 5)


def _comercial(t_mm):
    """Menor espessura comercial (lista do otimizador) ≥ t_mm."""
    return next((t for t in lg._T_MM if t >= t_mm - 1e-9), t_mm)


def _base(perfis):
    return {k: perfis[k] for k in ("perfil_viga", "perfil_pilar") if perfis.get(k)}


_DS_U = (15.875, 19.05, 22.225, 25.4, 12.7, 12.0)   # W11: ⅝"…1"; nos U baixos ½" e M12 (ISO 8.8)
_TS_U = (8.0, 9.5, 12.5, 16.0, 19.0, 22.4, 25.0, 31.5)


def _sugerir_chapa_u(base, esforcos):
    """W11: pré-dimensionamento da chapa de topo ajustada com viga U. Diâmetro (⅝" a 1", depois ½" e M12) e espessura
    da chapa: a 1ª combinação com taxa ≤ 0,90 (W8-RIG). pf e c_alma no automático (mínimo com 10 % de folga). Pernas: a
    menor comercial ≥ mínima/0,90; com sobra de 5 mm, a perna do lado externo fica limitada à sobra (o contorno inteiro
    soldado): se a mínima da tabela 10 já é a sobra, essa verificação fica no limite e não conta para o alvo. Nada passa:
    a de menor taxa (o verificar diz "momento além da capacidade de 2 parafusos dentro do U"). Nada cabe: ValueError com
    o diâmetro máximo."""
    tipo = "chapa_topo_ajustada"
    vg = _perfil_iu(base["perfil_viga"], "perfil_viga", ue=True)
    pv = base.get("perfil_pilar")
    pp = _perfil_iu(pv, "perfil_pilar", ue=True) if pv else None
    mesmo = pp is not None and abs(pp["h"] - vg["h"]) < 0.5 * MM and abs(pp["b"] - vg["b"]) < 0.5 * MM
    sobra = 0.0 if mesmo else SOBRA_U / MM
    grade = []
    for dd in _DS_U:
        for t in _TS_U:
            if vg["formado"]:
                pernas = [(round(vg["tw"] / MM, 2),) * 2]
            else:
                b0 = [lg.filete_minimo(min(x, t * MM)) / MM for x in (vg["tw"], vg["tf"])]
                folga = tuple(next(x for x in lg._B_MM if x >= b / ALVO - 1e-9) for b in b0)
                pernas = [tuple(min(f, sobra) if b <= sobra else f for f, b in zip(folga, b0))] if sobra else []
                pernas += [folga] if folga not in pernas else []
            grade += [dict(d_mm=dd, tipo_parafuso="ISO 8.8" if dd < 12.5 else "A325", tp=t, b_alma=bw, b_mesa=bm,
                           solda_mesa="filete", pf=0.0, c_alma=0.0) for bw, bm in pernas]
    melhor = passa = erro = None
    for x in grade:
        dados = dict(base, **x)
        try:
            r = TIPOS[tipo]["verificar"](dados, esforcos)
        except ValueError as ex:
            erro = ex
            continue
        alvo = max([v["taxa"] for v in r["verificacoes"] if not v.get("limite_sobra")] or [0.0])
        if r["ok"] and alvo <= ALVO + 1e-9:
            passa = (dados, r)
            break
        if r["ok"] and passa is None:
            passa = (dados, r)
        if melhor is None or r["taxa"] < melhor[1]["taxa"]:
            melhor = (dados, r)
    if not (passa or melhor):
        raise erro or ValueError(f"{base['perfil_viga']}: nenhuma chapa de topo com 2 parafusos dentro do U")
    dados, r = passa or melhor
    # campos só do I zerados: a tela que vinha do W310 não guarda bp/g/linhas extras que a viga U não usa
    return dict(dados, pf=r["intermediarios"]["pf"], c_alma=r["intermediarios"]["c_alma"], bp=0.0, g=0.0,
                n_linhas_extras=0)


def _sugerir_chapa(tipo):
    def f(perfis, esforcos):
        base = _base(perfis)
        if tipo == "chapa_topo_ajustada":
            if (perfis or {}).get("terca_ou_travessa") is not None:   # W11: isenção da 6.1.5.2 vem da tela
                base["terca_ou_travessa"] = perfis["terca_ou_travessa"]
            if _forma_cat(base.get("perfil_viga")) in ("U", "Ue"):
                return _sugerir_chapa_u(base, esforcos)
        if tipo == "chapa_topo_estendida":
            base["estendida"] = "ambas" if (esforcos or {}).get("M", 0) < 0 else "superior"
        pv = _perfil(base["perfil_viga"], "perfil_viga")
        bf = pv["b"] / MM
        # W7-LIG: filete da alma dentro da faixa da tabela 10 / 6.2.6.2.2 da alma (W310x38,7: tw 5,8 → 5 mm; o padrão 6 reprovava)
        bmax = math.floor(lg.filete_maximo(pv["tw"]) / MM)
        base["b_alma"] = max(math.ceil(lg.filete_minimo(pv["tw"]) / MM), min(6, bmax))
        # W8-RIG: pf = mínimo de aperto (CBCA 6.1.1) com folga de 10 % (30 → 35, 35 → 40, 40 → 45); com pf = 0 (auto =
        # o mínimo) o padrão nascia a 1,00 nessa disposição
        grade = [dict(d_mm=dd, tp=t, de=max(40, _borda(dd)), bp=bf + 25, n_linhas_extras=nx,
                      pf=_com_folga(next((p for d0, p in _PF_MIN if dd <= d0 + 0.05), 1.6 * dd * MM) / MM),
                      s_linhas=max(75, math.ceil(3 * dd / 5) * 5),
                      g=max(round(bf / 2 / 5) * 5, math.ceil((2.7 * dd + 10) / 5) * 5))
                 for nx in (0, 1, 2) for dd in _DS for t in _TS]
        grade = [x for x in grade if (x["bp"] - x["g"]) / 2 >= _borda(x["d_mm"])]
        return _buscar(tipo, base, grade, esforcos)
    return f


def _sugerir_flange(perfis, esforcos):
    base = _base(perfis)
    # W8-RIG: o mesmo diâmetro na chapa de alma (antes ficava o padrão ¾", e as bordas fixas de 35 mm da alma davam
    # 32/35 = 0,91: nenhuma combinação chegava a ≤ 0,90 e o padrão saía a 0,96)
    grade = [dict(modo="parafusada", d_mm=dd, d_alma_mm=dd, t_mesa=t, n_por_linha=n, e1=max(40, _borda(dd)),
                  e_viga=max(40, _borda(dd)), s=max(75, math.ceil(3 * dd / 5) * 5),
                  n_alma=na, s_alma=_passo(dd), h_alma=(na - 1) * _passo(dd) + 2 * _borda(dd))
             for t in _TS for dd in _DS for n in (2, 3, 4, 5, 6) for na in (3, 4)]
    return _buscar("flange_plate", base, grade, esforcos)


def _sugerir_emenda(tipo, chave):
    def f(perfis, esforcos):
        base = {chave: perfis.get(chave) or perfis.get("perfil_viga") or perfis.get("perfil_pilar")}
        p = _perfil(base[chave], chave)
        bf, tf, tw = p["b"] / MM, p["tf"] / MM, p["tw"] / MM
        # W8-RIG: tala da alma até 90 % da altura livre hw − 2r (folga com os raios de concordância e a tolerância de
        # laminação; a verificação "cabe entre as mesas" fica ≤ 0,90)
        livre = ALVO * (p["h"] - 2 * p["tf"] - 2 * p.get("r", 0)) / MM
        tm0 = _comercial(max(tf, 6.3))                     # Pfeil 9.10.1: tala da mesa com a espessura da mesa
        ta0 = _comercial(max(0.6 * tw, 6.3))
        grade = []
        for dd in _DS:   # o mesmo diâmetro nas mesas e na alma (CBCA 2.4: evita troca na montagem)
            ev, s = _borda(dd), _passo(dd)
            # gabarito da mesa: bf/2 + 10 para baixo a 5 mm, sem passar da borda lateral com folga
            g = math.floor(min(bf / 2 + 10, bf - 2 * lg.distancia_min_borda(dd * MM) / MM / ALVO) / 5) * 5
            mesas = [dict(d_mesa_mm=dd, n_por_linha_mesa=n, s_mesa=s, g_mesa=g, t_tala_mesa=t,
                          e1_mesa=max(40, ev), e_mesa_perfil=max(40, ev))
                     for n in (2, 3, 4, 5, 6) for t in (tm0, _comercial(tm0 + 0.1))]
            almas = []
            for nc in (1, 2):
                for nr in range(2, 13):
                    if (livre - 2 * ev) < (nr - 1) * s:
                        break
                    for ta in (ta0, _comercial(ta0 + 0.1)):
                        # linhas espalhadas na altura disponível (mais braço para M0), até 24t ≤ 300 mm (6.3.10)
                        smax = min(24 * min(ta, tw), 300.0) * ALVO
                        sv = max(s, min(math.floor((livre - 2 * ev) / (nr - 1) / 5) * 5, math.floor(smax / 5) * 5))
                        almas.append(dict(d_alma_mm=dd, n_linhas_alma=nr, n_colunas_alma=nc, s_alma=sv, sh_alma=s,
                                          t_tala_alma=ta, h_tala_alma=(nr - 1) * sv + 2 * ev, e_tala_alma=ev,
                                          e_emenda_alma=ev + 10.0 / 2))   # borda ev na ponta da alma (folga 10)
            grade += [dict(folga=10.0, **m, **a) for m in mesas for a in almas]
        return _buscar(tipo, base, grade, esforcos)
    return f


def _sugerir_painel(perfis, esforcos):
    return _buscar("painel_alma_pilar", _base(perfis), [{}], esforcos)


def _perna_folga(F, L, tmin, fy, fu, fw):
    """W9: menor perna da lista do otimizador com resistência e mínimo da tabela 10 a ≤ 90 % (F em kN, L em m);
    None se passa de 90 % do máximo de 6.2.6.2.2 (o chamador troca por penetração)."""
    k = lg.resistencia_filete(MM, fw, fy, fu)["FwRd"] / MM          # kN/m por m de perna (linear em b)
    need = max(F / (ALVO * L * k) if L > 0 else 0.0, lg.filete_minimo(tmin) / ALVO) / MM
    b = next((x for x in lg._B_MM if x >= need - 1e-9), None)
    return b if b is not None and b * MM <= ALVO * lg.filete_maximo(tmin) + 1e-12 else None


def _sugerir_flange_emenda(tipo, chave, pilar):
    """W9 (docs/v3/W9-FLANGE-PERFIL.md): busca do W8-RIG (_buscar, 1ª com taxa ≤ 0,90) — sem linhas fora das mesas
    primeiro, depois estendida (viga: do lado tracionado pelo M; pilar e inversão: as duas), 2 e 4 colunas, linhas dentro
    (1 a 3; 0 só com as duas estendidas), diâmetro e espessura. Bordas, passo e pf com a folga de 10 % (_borda, _passo,
    _com_folga); pernas das soldas pela força (_perna_folga), penetração nas mesas quando o filete não cabe."""
    def f(perfis, esforcos):
        base = {chave: perfis.get(chave) or perfis.get("perfil_viga") or perfis.get("perfil_pilar")}
        p = _perfil_iu(base[chave], chave)
        ents = TIPOS[tipo]["entradas"]
        contato = bool(ents["contato"]["padrao"])
        H, bf, tw, tf = p["h"], p["b"], p["tw"], p["tf"]
        fyb, fub_v = _aco(ents["aco_pilar" if pilar else "aco_viga"]["padrao"])
        fyp, fup = _aco(ents["aco_chapa"]["padrao"])
        fw = lg.ELETRODOS[ents["eletrodo"]["padrao"]] * MPa
        e = _esf_flange(esforcos, H - tf, [])
        Fn, Fcis = _forcas_mesa(e, H - tf, bf, contato)
        M = e["M"]
        ests = ("nao", "ambas") if pilar else ("nao", "superior" if M >= 0 else "inferior", "ambas")
        grade = []
        for est in ests:
            for nc in (2, 4):
                for nd in (0, 1, 2, 3):
                    if nd == 0 and est != "ambas":
                        continue
                    for dd in _DS:
                        s = _passo(dd)
                        g = _g_flange(p, dd)
                        for t in _TS:
                            x = dict(d_mm=dd, tp=t, estendida=est, n_colunas=nc, linhas_dentro=nd, s_linhas=s, g=g,
                                     g2=g + 2 * s if nc == 4 else 0.0, de=max(40, _borda(dd)),
                                     pf=_com_folga(next((q for d0, q in _PF_MIN if dd <= d0 + 0.05), 1.6 * dd * MM) / MM))
                            if p["formado"]:   # parede fina: perna = t (sem tabela 10 / 6.2.6.2.2, aviso no verificar)
                                x.update(solda_mesa="filete", b_mesa=round(tf / MM, 2), b_alma=round(tw / MM, 2))
                            else:
                                bm = _perna_folga(Fn + Fcis, bf + bf - tw, min(tf, t * MM), min(fyb, fyp),
                                                  min(fub_v, fup), fw)
                                x.update(solda_mesa="filete", b_mesa=bm) if bm else x.update(solda_mesa="penetracao")
                                x["b_alma"] = _perna_folga(abs(e["V"]) / 2, (H - 2 * tf) / 2, min(tw, t * MM),
                                                           min(fyb, fyp), min(fub_v, fup), fw) or \
                                    math.floor(lg.filete_maximo(min(tw, t * MM)) / MM)
                            grade.append(x)
        return _buscar(tipo, base, grade, esforcos)
    return f


# ---------------------------------------------------------------- registro (contrato)

_E_CHAPA_TOPO = dict(
    **_E_VIGA, **_E_PAR, **_E_CHAPA,
    tp=_ent("Espessura da chapa de topo", "mm", 22.4, 6.3, 100.0),
    bp=_ent("Largura da chapa (0 = bf+25)", "mm", 0.0, 0.0, 1000.0),
    g=_ent("Gabarito (0 = auto)", "mm", 0.0, 0.0, 600.0),
    pf=_ent("Furo → face mesa (0 = auto)", "mm", 0.0, 0.0, 200.0, descricao="0 = CBCA 6.1.1 (30/35/40 mm)"),
    n_linhas_extras=_ent("Linhas extras por mesa", "-", 0, 0, 6,
                         descricao="linhas de 2 parafusos além da 1ª linha interna, para dentro, em cada mesa"),
    s_linhas=_ent("Passo das linhas extras", "mm", 75.0, 30.0, 300.0),
    sobra=_ent("Sobra da chapa além da mesa", "mm", 20.0, 0.0, 100.0,
               descricao="lado não estendido: chapa além da face externa da mesa"),
    solda_mesa=_ent("Solda das mesas", "-", "penetracao", opcoes=["penetracao", "filete"]),
    b_mesa=_ent("Filete das mesas (perna)", "mm", 8.0, 3.0, 25.0),
    b_alma=_ent("Filete da alma (perna)", "mm", 6.0, 3.0, 25.0),
    **_E_PILAR)
# W11: só a ajustada aceita viga U (e pilar U com viga U); as outras rígidas continuam I/H (G5)
_U_CHAPA = ("U laminado", "UPN", "U formado a frio", "Ue formado a frio")
PERFIS_CHAPA_U = list(dict.fromkeys(lin["nome"] for lin in secoes._carrega_catalogo().values()
                                    if lin["forma"] == "I" or lin["forma"] in ("U", "Ue") and lin["familia"] in _U_CHAPA))
_E_CHAPA_TOPO_AJ = dict(
    _E_CHAPA_TOPO,
    perfil_viga=dict(_ent("Perfil da viga", "-", None, descricao="I/H do catálogo ou {d, bf, tw, tf, r} em mm; U (UPN, "
                          "U laminado, U e Ue dobrados): chapa do tamanho do U + 5 mm por lado e 2 parafusos dentro do U"),
                     perfis=PERFIS_CHAPA_U, resugerir=True),
    perfil_pilar=dict(_E_PILAR["perfil_pilar"], perfis=PERFIS_CHAPA_U, resugerir=True),
    c_alma=_ent("Furo → alma do U (0 = auto)", "mm", 0.0, 0.0, 200.0,
                descricao="só viga U: linha dos 2 parafusos dentro do U, a partir da face interna da alma; 0 = auto "
                          "(cabeça/2 + filete, aperto da porca "
                          "no apoio, folga de 10 %)"),
    terca_ou_travessa=_ent("Terça/travessa de fechamento", "-", False,
                           descricao="isenta do mínimo de 45 kN (NBR 8800 6.1.5.2)"))
_E_CHAPA_TOPO_EST = dict(
    _E_CHAPA_TOPO,
    estendida=_ent("Posição da chapa", "-", "superior", opcoes=["superior", "ambas", "inferior"],
                   descricao="superior = estende em cima, alinhada embaixo (CBCA LMPA); ambas = centralizada (inversão de momento); inferior = estende embaixo, alinhada em cima (M < 0)"),
    de=_ent("Furo → extremidade da chapa", "mm", 40.0, 15.0, 200.0),
    enrijecedor_chapa=_ent("Enrijecedor na extensão", "-", False),
    t_enrijecedor=_ent("Espessura do enrijecedor", "mm", 0.0, 0.0, 50.0))

_E_FLANGE = dict(
    **_E_VIGA, **_E_PAR, **_E_CHAPA,
    modo=_ent("Chapa–mesa da viga", "-", "parafusada", opcoes=["parafusada", "soldada"]),
    t_mesa=_ent("Espessura da chapa de mesa", "mm", 19.0, 6.3, 100.0),
    b_chapa=_ent("Largura da chapa (0 = bf)", "mm", 0.0, 0.0, 1000.0),
    folga=_ent("Folga viga–pilar", "mm", 12.0, 0.0, 50.0),
    n_por_linha=_ent("Parafusos por linha", "-", 3, 1, 12),
    s=_ent("Espaçamento longitudinal", "mm", 75.0, 30.0, 300.0),
    e1=_ent("Borda na ponta da chapa", "mm", 40.0, 15.0, 150.0),
    e_viga=_ent("Borda na ponta da viga", "mm", 40.0, 15.0, 150.0),
    g=_ent("Gabarito (0 = bf/2 + 10)", "mm", 0.0, 0.0, 600.0),
    b_solda_mesa=_ent("Filete chapa–mesa (perna)", "mm", 8.0, 3.0, 25.0),
    L_solda=_ent("Comprimento dos filetes", "mm", 250.0, 40.0, 2000.0),
    solda_pilar=_ent("Solda chapa–pilar", "-", "penetracao", opcoes=["penetracao", "filete"]),
    b_solda_pilar=_ent("Filete chapa–pilar (perna)", "mm", 12.0, 3.0, 25.0),
    h_alma=_ent("Altura da chapa de alma", "mm", 230.0, 60.0, 2000.0),
    t_alma=_ent("Espessura da chapa de alma", "mm", 9.5, 4.75, 50.0),
    n_alma=_ent("Parafusos na alma", "-", 3, 1, 20),
    s_alma=_ent("Espaçamento na alma", "mm", 75.0, 30.0, 300.0),
    d_alma_mm=_ent("Diâmetro parafuso alma", "mm", 19.05, 12.0, 40.0),
    b_solda_alma=_ent("Filete chapa de alma", "mm", 6.0, 3.0, 25.0),
    **_E_PILAR)

_E_PAINEL = dict(
    perfil_viga=_E_VIGA["perfil_viga"], perfil_pilar=_ent("Perfil do pilar", "-", None),
    **{k: v for k, v in _E_PILAR.items() if k != "perfil_pilar"},
    ln=_ent("ℓn de apoio (0 = tf)", "mm", 0.0, 0.0, 500.0,
            descricao="extensão da força na mesa do pilar (5.7.3/5.7.4); 0 = tf da viga"),
    mesa_soldada=_ent("Mesa da viga soldada", "-", True,
                      descricao="força aplicada por solda (verifica 5.7.2); parafusada = T-stub na ligação"))


def _e_emenda(chave, aco_chave, rot):
    return {chave: _ent(f"Perfil {rot}", "-", None), aco_chave: _ent(f"Aço {rot}", "-", _PERFIS_PADRAO, opcoes=_ACOS),
            "tipo_parafuso": _E_PAR["tipo_parafuso"], "rosca_no_plano": _E_PAR["rosca_no_plano"],
            "aco_chapa": _E_CHAPA["aco_chapa"], "borda_laminada": _E_CHAPA["borda_laminada"],
            "folga": _ent("Folga entre as peças", "mm", 10.0, 0.0, 50.0),
            "d_mesa_mm": _ent("Diâmetro parafuso mesa", "mm", 22.225, 12.0, 40.0),
            "t_tala_mesa": _ent("Espessura tala da mesa", "mm", 12.5, 4.75, 100.0),
            "b_tala_mesa": _ent("Largura tala mesa (0 = bf)", "mm", 0.0, 0.0, 1000.0),
            "n_por_linha_mesa": _ent("Parafusos/linha (mesa, lado)", "-", 3, 1, 12),
            "s_mesa": _ent("Espaçamento na mesa", "mm", 70.0, 30.0, 300.0),
            "g_mesa": _ent("Gabarito mesa (0 = bf/2+10)", "mm", 0.0, 0.0, 600.0),
            "e1_mesa": _ent("Borda tala mesa (ponta)", "mm", 40.0, 15.0, 150.0),
            "e_mesa_perfil": _ent("Borda mesa do perfil", "mm", 40.0, 15.0, 150.0),
            "d_alma_mm": _ent("Diâmetro parafuso alma", "mm", 15.875, 12.0, 40.0),
            "t_tala_alma": _ent("Espessura de cada tala alma", "mm", 6.3, 4.75, 50.0),
            "h_tala_alma": _ent("Altura das talas da alma", "mm", 510.0, 60.0, 3000.0),
            "n_linhas_alma": _ent("Linhas de parafusos (alma)", "-", 4, 1, 30),
            "n_colunas_alma": _ent("Colunas/lado (alma)", "-", 2, 1, 6),
            "s_alma": _ent("Espaçamento vertical alma", "mm", 150.0, 30.0, 300.0),
            "sh_alma": _ent("Espaçamento horiz. alma", "mm", 70.0, 30.0, 300.0),
            "e_emenda_alma": _ent("Emenda → 1ª coluna (alma)", "mm", 30.0, 10.0, 200.0),
            "e_tala_alma": _ent("Borda tala alma (horiz.)", "mm", 30.0, 15.0, 150.0)}


_E_EMENDA_VIGA = dict(_e_emenda("perfil_viga", "aco_viga", "da viga"),
                      minimo_50=_ent("Mínimo 50% da capacidade", "-", False,
                                     descricao="M e V ≥ 50 % de MRd e VRd (Pfeil p. 244)"))
_E_EMENDA_PILAR = dict(_e_emenda("perfil_pilar", "aco_pilar", "do pilar"),
                       contato=_ent("Contato usinado", "-", True,
                                    descricao="superfícies usinadas: compressão por contato (Pfeil 9.1)"))
_ESF = ["N", "V", "M"]


def _e_flange(chave, aco_chave, rot, pilar):
    return {
        chave: dict(_ent(f"Perfil {rot}", "-", None, descricao="I/H (W, HP, IPE, HEA, HEB) ou U (UPN, U laminado, "
                         "U dobrado) do catálogo; as duas pontas iguais"), perfis=PERFIS_FLANGE, resugerir=True),
        aco_chave: _ent(f"Aço {rot}", "-", _PERFIS_PADRAO, opcoes=_ACOS),
        **_E_PAR, **_E_CHAPA,
        "tp": _ent("Espessura das chapas", "mm", 19.0, 6.3, 100.0, descricao="as duas chapas iguais"),
        "forma": _ent("Forma da chapa", "-", "retangular", opcoes=["retangular", "quadrada"],
                      descricao="retangular = perfil + sobra e furos + borda; quadrada = o lado maior nas duas direções"),
        "b_chapa": _ent("Largura da chapa (0 = auto)", "mm", 0.0, 0.0, 2000.0,
                        descricao="centrada no plano médio da alma; 0 = envolve o perfil e os furos com as bordas"),
        "h_chapa": _ent("Altura da chapa (0 = auto)", "mm", 0.0, 0.0, 3000.0),
        "sobra": _ent("Sobra além do perfil", "mm", 15.0, 0.0, 100.0,
                      descricao="borda da chapa além da mesa (lados sem parafuso)"),
        "estendida": _ent("Parafusos fora das mesas", "-", "ambas" if pilar else "nao",
                          opcoes=["nao", "superior", "inferior", "ambas"],
                          descricao="linhas fora das mesas (chapa estendida): nao, só na mesa superior (z+) ou inferior, "
                                    "ou nas duas (momento que inverte)"),
        "linhas_dentro": _ent("Linhas dentro (por mesa)", "-", 1, 0, 6,
                              descricao="linhas entre as mesas, a pf da face interna de cada mesa e depois a cada passo"),
        "s_linhas": _ent("Passo das linhas dentro", "mm", 75.0, 30.0, 300.0),
        "n_colunas": _ent("Colunas de parafusos", "-", 2, opcoes=[2, 4],
                          descricao="simétricas ao plano médio da alma (U: uma coluna dentro do U e outra atrás da alma)"),
        "g": _ent("Gabarito (0 = auto)", "mm", 0.0, 0.0, 600.0, descricao="entre as colunas junto à alma"),
        "g2": _ent("Gabarito externo (0 = auto)", "mm", 0.0, 0.0, 1000.0, descricao="entre as colunas externas (4 colunas)"),
        "pf": _ent("Furo → face mesa (0 = auto)", "mm", 0.0, 0.0, 200.0, descricao="0 = CBCA 6.1.1 (30/35/40 mm)"),
        "de": _ent("Furo → extremidade da chapa", "mm", 40.0, 15.0, 200.0, descricao="linhas fora das mesas"),
        "solda_mesa": _ent("Solda das mesas", "-", "filete", opcoes=["filete", "penetracao"]),
        "b_mesa": _ent("Filete das mesas (perna)", "mm", 8.0, 2.0, 25.0),
        "b_alma": _ent("Filete da alma (perna)", "mm", 5.0, 2.0, 25.0),
        "contato": _ent("Faces usinadas (contato)", "-", pilar,
                        descricao="ponta do perfil usinada antes de soldar: a compressão das mesas passa por contato "
                                  "(NBR 8800 6.6.1) e sai da solda"),
        "atrito": _ent("Ligação por atrito", "-", False,
                       descricao="parafusos protendidos: verifica o deslizamento entre as chapas (6.3.4.3)"),
        "mu": _ent("Coeficiente de atrito μ", "-", 0.35, 0.2, 0.5,
                   descricao="0,35 superfície laminada limpa; 0,50 jateada; 0,20 galvanizada (6.3.4.3)")}


_E_FLANGE_VIGA = _e_flange("perfil_viga", "aco_viga", "da viga", False)
_E_FLANGE_PILAR = _e_flange("perfil_pilar", "aco_pilar", "do pilar", True)
_ESF6 = ["N", "V", "Vz", "M", "Mz", "T"]

TIPOS = {
    "chapa_topo_ajustada": dict(
        nome="Chapa de topo ajustada", familia="rigida", liga=["viga-pilar-mesa"],
        descricao="Chapa de topo (flush) soldada na viga e parafusada na mesa do pilar; 2 parafusos por linha "
                  "junto às mesas; T-stub com alavanca (Struik–de Back). Viga U (UPN, U, Ue): chapa do tamanho do U "
                  "+ 5 mm e 2 parafusos dentro do U, na mesa do pilar I/H ou nas costas da alma do pilar U",
        entradas=_E_CHAPA_TOPO_AJ, verificar=verificar_chapa_topo_ajustada,
        sugerir=_sugerir_chapa("chapa_topo_ajustada"), esforcos=_ESF),
    "chapa_topo_estendida": dict(
        nome="Chapa de topo estendida", familia="rigida", liga=["viga-pilar-mesa"],
        descricao="Chapa de topo estendida (CBCA LMPA) numa ou nas duas mesas, com ou sem enrijecedor; "
                  "T-stub com alavanca por linha",
        entradas=_E_CHAPA_TOPO_EST, verificar=verificar_chapa_topo_estendida,
        sugerir=_sugerir_chapa("chapa_topo_estendida"), esforcos=_ESF),
    "flange_plate": dict(
        nome="Chapas de mesa (flange plate)", familia="rigida", liga=["viga-pilar-mesa"],
        descricao="Chapas nas mesas soldadas ao pilar e parafusadas ou soldadas à viga; alma com chapa simples",
        entradas=_E_FLANGE, verificar=verificar_flange_plate, sugerir=_sugerir_flange, esforcos=_ESF),
    "painel_alma_pilar": dict(
        nome="Painel de alma do pilar", familia="rigida", liga=["viga-pilar-mesa"],
        descricao="Pilar sob as forças das mesas da viga: flexão local da mesa, escoamento, enrugamento, "
                  "flambagem da alma, painel ao cisalhamento, enrijecedores e chapa de reforço (5.7)",
        entradas=_E_PAINEL, verificar=verificar_painel_alma, sugerir=_sugerir_painel, esforcos=_ESF),
    "emenda_viga": dict(
        nome="Emenda de viga com talas", familia="emenda", liga=["emenda-viga"],
        descricao="Emenda parafusada com talas nas mesas (corte simples) e duas talas na alma (corte duplo); "
                  "divisão do momento de Pfeil (9.10.1)",
        entradas=_E_EMENDA_VIGA, verificar=verificar_emenda_viga,
        sugerir=_sugerir_emenda("emenda_viga", "perfil_viga"), esforcos=_ESF),
    "emenda_pilar": dict(
        nome="Emenda de pilar com talas", familia="emenda", liga=["emenda-pilar"],
        descricao="Emenda parafusada de pilar com talas nas mesas e na alma, com ou sem contato usinado",
        entradas=_E_EMENDA_PILAR, verificar=verificar_emenda_pilar,
        sugerir=_sugerir_emenda("emenda_pilar", "perfil_pilar"), esforcos=_ESF),
    "emenda_flangeada_viga": dict(
        nome="Emenda flangeada de viga (perfil I/H/U)", familia="emenda", liga=["emenda-viga"],
        descricao="Flange aparafusada: chapa de topo soldada em cada ponta da viga (I, H, U), quadrada ou retangular, "
                  "e as duas chapas aparafusadas; parafusos dentro e fora das mesas; T-stub com alavanca",
        entradas=_E_FLANGE_VIGA, verificar=verificar_emenda_flangeada_viga,
        sugerir=_sugerir_flange_emenda("emenda_flangeada_viga", "perfil_viga", False), esforcos=_ESF6),
    "emenda_flangeada_pilar": dict(
        nome="Emenda flangeada de pilar (perfil I/H/U)", familia="emenda", liga=["emenda-pilar"],
        descricao="Flange aparafusada de pilar: chapa de topo soldada em cada trecho (I, H, U) e as duas chapas "
                  "aparafusadas; compressão por contato com faces usinadas; T-stub com alavanca",
        entradas=_E_FLANGE_PILAR, verificar=verificar_emenda_flangeada_pilar,
        sugerir=_sugerir_flange_emenda("emenda_flangeada_pilar", "perfil_pilar", True), esforcos=_ESF6,
        exemplo=dict(esforcos=dict(N=-400.0, V=40.0, Vz=0.0, M=60.0, Mz=0.0, T=0.0))),
}


def verificar(tipo, dados, esforcos):
    if tipo not in TIPOS:
        raise ValueError(f"tipo '{tipo}' desconhecido; válidos: {', '.join(TIPOS)}")
    return TIPOS[tipo]["verificar"](dados, esforcos)


lg.registrar_pontas(TIPOS)   # G2: geometria["pontas"] em todo resultado
