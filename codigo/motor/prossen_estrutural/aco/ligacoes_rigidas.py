"""Ligações rígidas e emendas — NBR 8800:2008, cap. 5.7 e 6 (contrato: docs/v3/LIGACOES-CONTRATO.md).

Tipos: chapa de topo ajustada (flush) e estendida (com/sem enrijecedor), flange plate soldada ou
parafusada, painel de alma do pilar (com enrijecedores e chapa de reforço), emenda de viga e emenda
de pilar com talas parafusadas (com ou sem contato). Documentação: docs/v3/ligacoes/IMPL-RIGIDAS.md.

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
          "rosca_no_plano": _ent("Rosca no plano de corte", "-", True, opcoes=[True, False])}
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
    "topo_pilar": _ent("Nó no topo do pilar", "-", False, opcoes=[True, False],
                       descricao="força a menos de d do topo (reduções de 5.7.2 a 5.7.6)"),
}
_E_VIGA = {"perfil_viga": _ent("Perfil da viga", "-", None, descricao="nome do catálogo ou {d, bf, tw, tf, r} em mm"),
           "aco_viga": _ent("Aço da viga", "-", _PERFIS_PADRAO, opcoes=_ACOS)}
_E_CHAPA = {"aco_chapa": _ent("Aço das chapas", "-", "A-36", opcoes=_ACOS),
            "eletrodo": _ent("Eletrodo", "-", "E70", opcoes=list(lg.ELETRODOS)),
            "borda_laminada": _ent("Bordas laminadas/maçarico", "-", False, opcoes=[True, False],
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
        if "opcoes" in ent:
            ops = ent["opcoes"]
            if isinstance(ops[0], bool):
                d[nome] = bool(v)
            elif str(v).upper() not in [str(o).upper() for o in ops]:
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


def _minimo_6152(e, braco, avisos):
    """NBR 8800 6.1.5.2: força de cálculo da ligação = max(√(V² + N²), |M|/braço); abaixo de 45 kN os
    esforços são escalados até 45 kN (sem esforço: V = 45 kN). Altera e no lugar."""
    k, aviso = lg.fator_minimo_6152(max(math.hypot(e["V"], e["N"]), abs(e["M"]) / braco))
    if not aviso:
        return
    if math.isfinite(k):
        for c in ("N", "V", "M"):
            e[c] *= k
    else:
        e["V"] = lg.F_MINIMA
    avisos.append(aviso)


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
        lado_ext = estendida == "ambas" or (estendida == "superior" and s == 1)
        if lado_ext:
            linhas.append(dict(z=s * (H / 2 + pf), tipo="ext", lado=s))
        linhas.append(dict(z=s * (H / 2 - tf - pf), tipo="int", lado=s))
        linhas += [dict(z=s * (H / 2 - tf - pf - k * sl), tipo="alma", lado=s) for k in range(1, nx + 1)]
    zt = H / 2 + (pf + de if estendida in ("superior", "ambas") else sobra)
    zb = -(H / 2 + (pf + de if estendida == "ambas" else sobra))
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
    e = _esforcos(e, av)
    _minimo_6152(e, H - tf, av)
    M, N, V = e["M"], e["N"], e["V"]
    if bp > bf + 0.025 + 1e-9:
        av.append(f"bp = {bp / MM:.0f} mm > bf + 25 mm: largura efetiva do T-stub limitada a {(bf + 0.025) / MM:.0f} mm "
                  "(CBCA 6.1.1)")
    bp_ef = min(bp, bf + 0.025)
    nx, sl = d["n_linhas_extras"], d["s_linhas"] * MM
    if nx and H / 2 - tf - pf - nx * sl < 0:
        raise ValueError(f"n_linhas_extras = {nx}: as linhas passam do meio da alma (H/2 − tf − pf − n·s < 0)")
    linhas, zb, zt = _linhas_chapa(H, tf, tw, pf, de, g, bp_ef, estendida, d["sobra"] * MM, db, enr, ts, nx, sl)
    if estendida == "superior" and M < 0:
        av.append("M < 0 traciona a mesa inferior, lado não estendido da chapa assimétrica (CBCA 6.1: com inversão "
                  "de momento usar chapa estendida nas duas mesas)")
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
    Vb = abs(V) / n_tot
    for i, L in enumerate(tr + co, 1):
        T = L["Tcap"] * (fator if i <= len(tr) else fator_op)
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
    # cortante
    R.res("parafusos: cisalhamento (V/n, todos os parafusos)", "6.3.3.2", Vb, FvRd)
    zs = sorted(L["z"] for L in linhas)
    lf = min([zs[i + 1] - zs[i] - dh for i in range(len(zs) - 1)] + [zs[0] - zb - dh / 2])
    R.res("pressão de contato na chapa de topo", "6.3.3.3", Vb, lg.pressao_contato(lf, tp, db, fup))
    if pil is not None:
        R.res("pressão de contato na mesa do pilar", "6.3.3.3", Vb,
              lg.pressao_contato(min(zs[i + 1] - zs[i] - dh for i in range(len(zs) - 1)), pil["tf"], db, fuc))
    c = lg.cisalhamento_chapa(bp * tp, (bp - 2 * dn) * tp, fyp, fup)
    R.res("chapa de topo: escoamento por cisalhamento", "6.5.5", abs(V), c["escoamento"])
    R.res("chapa de topo: ruptura por cisalhamento", "6.5.5", abs(V), c["ruptura"])
    # viga e soldas
    R.res("viga: momento resistente (Zx·fy/γa1 ≤ 1,5·Wx·fy/γa1)", "5.4.2 (CBCA 6.4)", abs(M),
          min(vg["Zx"], 1.5 * vg["Wx"]) * fyb / GA1, "kN·m")
    Ff = Mef / braco
    fw = lg.ELETRODOS[d["eletrodo"]] * MPa
    # ponta da viga (pontas.py): chapa encostada no apoio, viga cortada a tp dele; copes laterais encurtam as mesas
    ctx = d.get("contexto_pontas") or {}
    ponta = _pontas.pontas_locais(ctx.get("tipo_no", "viga_pilar_mesa"), vg, pil,
                                  comprimento=ctx.get("comprimento", math.inf), desloc=ctx.get("desloc"),
                                  params=dict(ctx.get("params") or {}, folga=tp))[0]
    bf_s = _pontas.largura_util(ponta, bf)
    if bf_s < bf - 1e-9:
        av.append(f"mesas da viga recortadas na ponta: solda das mesas em {bf_s / MM:.1f} mm de {bf / MM:.0f} mm")
    if d["solda_mesa"] == "penetracao":
        R.res("solda das mesas (penetração total): metal base", "6.2.5 / tabela 8", Ff, bf_s * tf * fyb / GA1)
    else:
        bm = d["b_mesa"] * MM
        r = lg.resistencia_filete(bm, fw, min(fyb, fyp), min(fub_v, fup))
        R.res("solda das mesas (filetes dos dois lados)", "6.2.5 / tabela 8", Ff, 2 * bf_s * r["FwRd"])
        _filete(R, bm, min(tf, tp), "das mesas", "b_mesa")
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
    if estendida in ("superior", "ambas"):
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
        for s in ((1,) if estendida == "superior" else (1, -1)):
            geo["chapas"].append(_chapa("enrijecedor da chapa de topo",
                                        [[0, s * H / 2], [Ls, s * H / 2], [0, s * (H / 2 + hs)]],
                                        [xs, ts / 2, 0], [0, -1, 0], [1, 0, 0], ts))
    R.avisos.append("modelo: T-stub de Struik–de Back (Kulak cap. 17) por linha, distribuição plástica "
                    "(Σ n·T·h); linhas internas junto à mesa comprimida só ao cortante")
    geo["pontas"] = [ponta]
    out = R.resultado(geo)
    out["intermediarios"] = dict(
        MRd=MRd, Mef=Mef, FtRd=FtRd, FvRd=FvRd, bp=_mm(bp), g=_mm(g), pf=_mm(pf), altura_chapa=_mm(zt - zb),
        linhas=[dict(z=_mm(L["z"]), h=_mm(L["h"]), b=_mm(L["b"]), a=_mm(L["a"]), p=_mm(L["p"]), Tcap=L["Tcap"],
                     T=L["T"], Q=L["Q"]) for L in tr])
    return out


def verificar_chapa_topo_ajustada(dados, esforcos):
    d = _ler("chapa_topo_ajustada", dados)
    d["contexto_pontas"] = (dados or {}).get("contexto_pontas")
    d["estendida"] = "nao"
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
    _pilar(R, d, pil, fyc, Ft, Fc, d["ln"] * MM or tf, bf, tf, H, e["M"], d["mesa_soldada"], geo, pil["h"] / 2)
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


# ---------------------------------------------------------------- sugerir (pré-dimensionamento por busca)

def _buscar(tipo, base, grade, esforcos):
    """Primeira combinação da grade que passa; senão a de menor taxa (o verificar mostra a falha).
    Com pilar, cada combinação tenta também enrijecedores e chapa de reforço (do mais leve ao mais pesado)."""
    pilar = [{}] + ([dict(t_enr_pilar=12.5), dict(t_enr_pilar=16.0, t_reforco=9.5),
                     dict(t_enr_pilar=19.0, t_reforco=16.0)] if base.get("perfil_pilar") else [])
    melhor = None
    for extra in grade:
        for pp in pilar:
            dados = dict(base, **extra, **pp)
            try:
                r = TIPOS[tipo]["verificar"](dados, esforcos)
            except ValueError:
                continue
            if r["ok"]:
                return dados
            if melhor is None or r["taxa"] < melhor[0]:
                melhor = (r["taxa"], dados)
    return melhor[1] if melhor else dict(base, **grade[0])


_DS = (15.875, 19.05, 22.225, 25.4)
_TS = (12.5, 16.0, 19.0, 22.4, 25.0, 31.5, 37.5, 44.5, 50.0)


def _borda(dmm):
    """Borda mínima (tabela 14, serra/tesoura) arredondada a 5 mm, em mm."""
    return math.ceil(lg.distancia_min_borda(dmm * MM) / MM / 5) * 5


def _base(perfis):
    return {k: perfis[k] for k in ("perfil_viga", "perfil_pilar") if perfis.get(k)}


def _sugerir_chapa(tipo):
    def f(perfis, esforcos):
        base = _base(perfis)
        if tipo == "chapa_topo_estendida":
            base["estendida"] = "ambas" if (esforcos or {}).get("M", 0) < 0 else "superior"
        bf = _perfil(base["perfil_viga"], "perfil_viga")["b"] / MM
        grade = [dict(d_mm=dd, tp=t, de=max(40, _borda(dd)), bp=bf + 25, n_linhas_extras=nx,
                      s_linhas=max(75, math.ceil(3 * dd / 5) * 5),
                      g=max(round(bf / 2 / 5) * 5, math.ceil((2.7 * dd + 10) / 5) * 5))
                 for nx in (0, 1, 2) for dd in _DS for t in _TS]
        grade = [x for x in grade if (x["bp"] - x["g"]) / 2 >= _borda(x["d_mm"])]
        return _buscar(tipo, base, grade, esforcos)
    return f


def _sugerir_flange(perfis, esforcos):
    base = _base(perfis)
    grade = [dict(modo="parafusada", d_mm=dd, t_mesa=t, n_por_linha=n, e1=max(40, _borda(dd)),
                  e_viga=max(40, _borda(dd)), s=max(75, math.ceil(3 * dd / 5) * 5))
             for t in _TS for dd in _DS for n in (2, 3, 4, 5, 6)]
    return _buscar("flange_plate", base, grade, esforcos)


def _sugerir_emenda(tipo, chave):
    def f(perfis, esforcos):
        base = {chave: perfis.get(chave) or perfis.get("perfil_viga") or perfis.get("perfil_pilar")}
        p = _perfil(base[chave], chave)
        tf, tw = p["tf"] / MM, p["tw"] / MM
        livre = (p["h"] - 2 * p["tf"] - 2 * p.get("r", 0)) / MM - 10
        grade = []
        for dd in _DS:
            ev, sv = _borda(dd), math.ceil(3 * dd / 5) * 5
            nr = max(2, int((livre - 2 * ev) // sv) + 1)
            for n in (2, 3, 4, 5, 6):
                grade.append(dict(d_mesa_mm=dd, d_alma_mm=dd, n_por_linha_mesa=n, s_mesa=max(70, sv),
                                  t_tala_mesa=max(tf, 6.3), t_tala_alma=max(0.6 * tw, 6.3), n_linhas_alma=nr,
                                  s_alma=sv, sh_alma=max(70, sv), h_tala_alma=(nr - 1) * sv + 2 * ev,
                                  e1_mesa=max(40, ev), e_mesa_perfil=max(40, ev), e_tala_alma=ev,
                                  e_emenda_alma=ev + 5))
        return _buscar(tipo, base, grade, esforcos)
    return f


def _sugerir_painel(perfis, esforcos):
    return _buscar("painel_alma_pilar", _base(perfis), [{}], esforcos)


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
_E_CHAPA_TOPO_EST = dict(
    _E_CHAPA_TOPO,
    estendida=_ent("Lado(s) estendido(s)", "-", "superior", opcoes=["superior", "ambas"],
                   descricao="superior = assimétrica (CBCA LMPA); ambas = simétrica (inversão de momento)"),
    de=_ent("Furo → extremidade da chapa", "mm", 40.0, 15.0, 200.0),
    enrijecedor_chapa=_ent("Enrijecedor na extensão", "-", False, opcoes=[True, False]),
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
    mesa_soldada=_ent("Mesa da viga soldada", "-", True, opcoes=[True, False],
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
                      minimo_50=_ent("Mínimo 50% da capacidade", "-", False, opcoes=[True, False],
                                     descricao="M e V ≥ 50 % de MRd e VRd (Pfeil p. 244)"))
_E_EMENDA_PILAR = dict(_e_emenda("perfil_pilar", "aco_pilar", "do pilar"),
                       contato=_ent("Contato usinado", "-", True, opcoes=[True, False],
                                    descricao="superfícies usinadas: compressão por contato (Pfeil 9.1)"))
_ESF = ["N", "V", "M"]

TIPOS = {
    "chapa_topo_ajustada": dict(
        nome="Chapa de topo ajustada", familia="rigida", liga=["viga-pilar-mesa"],
        descricao="Chapa de topo (flush) soldada na viga e parafusada na mesa do pilar; 2 parafusos por linha "
                  "junto às mesas; T-stub com alavanca (Struik–de Back)",
        entradas=_E_CHAPA_TOPO, verificar=verificar_chapa_topo_ajustada,
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
}


def verificar(tipo, dados, esforcos):
    if tipo not in TIPOS:
        raise ValueError(f"tipo '{tipo}' desconhecido; válidos: {', '.join(TIPOS)}")
    return TIPOS[tipo]["verificar"](dados, esforcos)


lg.registrar_pontas(TIPOS)   # G2: geometria["pontas"] em todo resultado
