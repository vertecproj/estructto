"""Ligações metálicas — NBR 8800:2008, cap. 6 (componentes + duas tipologias flexíveis).
Unidades: kN, m, kN/m² (dados em m; diâmetro de parafuso em mm na entrada, `d_mm`).

Edição: a NBR 8800:2024 revisou o cap. 6 (efeito de alavanca em 6.3.5, retirada do fator
0,67 da resistência à tração) segundo B. I. R. de Oliveira, "Análise do projeto de revisão
da ABNT NBR 8800: efeito de alavanca...", monografia UFMG, 2024 — mas a fonte descreve o
projeto em consulta nacional, não o texto publicado. Nada da 2024 foi implementado: o
módulo é da 2008 e o efeito de alavanca fica como limitação (o usuário inclui no Ft).
Fontes de conferência dos valores (acesso 2026-10-20):
  [CBCA] Instituto Aço Brasil/CBCA, "Ligações em estruturas metálicas", vol. 2, 4ª ed., 2011.
  [NBR]  ABNT NBR 8800:2008, tab. A.3 (fub), 6.2.6 tab. 10, 6.3.8, 6.3.10, 6.3.11 tab. 14, 6.5.3 — conferidos
         pelo verificador (verificacao/2026-10-20-ligacoes.md).
"""
from __future__ import annotations

import logging
import math
import re

from .. import materiais, secoes
from ..modelo import MPa
from .nbr8800 import GA1, GA2

log = logging.getLogger(__name__)

EDICAO = "NBR 8800:2008"
GW2 = 1.35  # NBR 8800 tabela 8 (metal da solda, filete)
MM = 1e-3

# fub, fyb (MPa) — NBR 8800 tabela A.3; alta = parafuso de alta resistência (6.3.3.2)
#   A307: fub 415 (curso calculistadeaco, 41,5 kN/cm²); fyb não usado (sem barra rosqueada) → None.
#   A325: fub 825 para db ≤ 24 mm ou 1" (tab. A.3); 725 para 24 < db ≤ 36 mm (ver _parafuso).
#   A490: fub 1035, fyb 895 (tab. A.3).
#   ISO 898-1 classes: fub pela designação; ISO 4.6: fyb 235 (tab. A.3). fyb só é usado no pino (6.4,
#   ligacoes_contraventamento); A325 com db > 24 mm: fyb 560 (tab. A.3).
PARAFUSOS = {
    "A307": dict(fub=415, fyb=None, alta=False),
    "A325": dict(fub=825, fyb=635, alta=True),
    "A490": dict(fub=1035, fyb=895, alta=True),   # fyb: NBR 8800 tabela A.3
    "ISO 4.6": dict(fub=400, fyb=235, alta=False),
    "ISO 8.8": dict(fub=800, fyb=640, alta=True),
    "ISO 10.9": dict(fub=1000, fyb=900, alta=True),
}
DIAMETROS_USUAIS_MM = (12.5, 16, 19, 20, 22, 24, 25, 27, 30, 12.7, 15.875, 19.05, 22.225, 25.4, 28.575, 31.75)
ELETRODOS = {"E60": 415, "E70": 485}  # fw (MPa) — CBCA vol. 2 p. 16 e p. 57 (NBR 8800 tabela A.4)
FURO_FOLGA = 1.5 * MM    # NBR 8800 6.3.6 tabela 12: furo-padrão = db + 1,5 mm (CBCA p. 11)
FURO_DANO = 2.0 * MM     # NBR 8800 5.2.4.1: +2 mm no cálculo de áreas líquidas (CBCA p. 11)

# Distância mínima furo-padrão → borda: NBR 8800:2008 6.3.11.1, tabela 14 (mm, linhas em polegadas e em
# mm). Acima de 36 mm (1 1/4"): 1,75db (serra/tesoura) e 1,25db (laminada/maçarico).
_BORDA = [  # (db mm, cortada com serra/tesoura, laminada/maçarico)
    (12.7, 22, 19), (15.875, 29, 22), (16, 29, 22), (19.05, 32, 26), (20, 35, 27), (22, 38, 29),
    (22.225, 38, 29), (24, 42, 31), (25.4, 44, 32), (27, 50, 38), (28.575, 50, 38), (30, 53, 39),
    (31.75, 57, 42), (36, 64, 46)]
COMPRIMENTO_LONGA = 1.270  # NBR 8800 6.3.8: ligação por contato mais longa → Fv,Sd e Fc,Sd × 1,25
F_MINIMA = 45.0  # kN — NBR 8800:2008 6.1.5.2 (p. 63): resistência mínima de ligações
ISENCOES_6152 = ("diagonais e montantes de travejamento de barras compostas, tirantes de barras redondas, "
                 "travessas de fechamento e terças")


def fator_minimo_6152(F, isenta=False):
    """NBR 8800 6.1.5.2: ligação com força de cálculo F < 45 kN é dimensionada para 45 kN, exceto as de
    ISENCOES_6152 (isenta=True). Retorna (fator ≥ 1 para os esforços, aviso ou ""). F = 0 → fator inf:
    o chamador aplica 45 kN na direção principal da ligação."""
    F = abs(F)
    if isenta or F >= F_MINIMA:
        return 1.0, ""
    return (F_MINIMA / F if F > 0 else math.inf), \
        f"força de cálculo {F:.1f} kN < 45 kN: ligação dimensionada para o mínimo de 45 kN (NBR 8800 6.1.5.2)"

_LIM = "NBR 8800:2024 não confirmada em fonte pública (texto final): mantida a 2008"
COBERTURA = [
    dict(norma="NBR 8800", edicao="2008", item="6.3.3.1", verificacao="parafuso: tração Ft,Rd = 0,75·Ab·fub/γa2",
         limitacao="efeito de alavanca (6.3.5) não implementado — incluir no Ft,Sd; a 2024 revisou 6.3.5: " + _LIM),
    dict(norma="NBR 8800", edicao="2008", item="6.3.3.2", verificacao="parafuso: cisalhamento 0,4 / 0,5·Ab·fub/γa2 por plano",
         limitacao="ligação por contato; deslizamento (atrito, 6.3.4.3) não implementado"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.3.3", verificacao="pressão de contato e rasgamento (furo-padrão)",
         limitacao="padrão: deformação limitada (1,2/2,4); ligação de tração: soma por furo (extremidade e "
                   "internos) e grupo Σ min(Fv,Rd; Fc,Rd); chapa simples: lf = menor entre furo-furo e furo-borda; "
                   "furos alargados/alongados não tratados"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.3.4", verificacao="parafuso: tração + cisalhamento (interação quadrática)",
         limitacao=""),
    dict(norma="NBR 8800", edicao="2008", item="6.3.8", verificacao="ligação por contato com comprimento > 1270 mm",
         limitacao="Fv,Sd e Fc,Sd × 1,25 (ligação de tração e chapa simples)"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.9 / 6.3.10 / 6.3.11.1 (tabela 14) / 6.3.12",
         verificacao="espaçamento mínimo e máximo, borda mínima e máxima (disposições construtivas: só reprovam)",
         limitacao="espaçamento mínimo 2,7db (6.3.9; aviso abaixo do preferível 3db); espaçamento máximo 6.3.10: "
                   "24t ≤ 300 mm ou, com sujeito_corrosao (aço resistente à corrosão sem pintura), 14t ≤ 180 mm; "
                   "notas b) e c) da tabela 14 não usadas (a favor)"),
    dict(norma="NBR 8800", edicao="2008", item="6.3.7 / 6.5.7.2",
         verificacao="pega longa (Fv,Rd − 1 % a cada 1,5 mm além de 5db) e chapas de enchimento (Fv,Rd e Fc,Rd × "
                     "[1 − 0,0154(ts − 6)], ts em mm)",
         limitacao="campos opcionais pega e t_enchimento (0 = não considerados); pega aplicada também a parafusos de "
                   "alta resistência (protensão inicial não informada, a favor) e cada fração de 1,5 mm conta; "
                   "ts > 19 mm exige 6.5.7.2 b)/c) (não verificadas → reprova)"),
    dict(norma="NBR 8800", edicao="2008", item="6.2.5 tabela 8 / 6.2.6 tabela 10",
         verificacao="solda de filete: metal da solda, metal base, garganta 0,707b, dimensão mínima e máxima",
         limitacao="aumento de resistência com o ângulo θ não usado (a favor da segurança); metal base só da chapa "
                   "informada; comprimento mínimo do filete (6.2.6.2.3: ≥ 4b e ≥ 40 mm) verificado; comprimento efetivo "
                   "= comprimento total (6.2.2.2 c), com β da 6.2.2.2 d) se L_longitudinal > 0 (cordão longitudinal "
                   "em ligação extrema de barra axial)"),
    dict(norma="NBR 8800", edicao="2008", item="6.5.3 → 5.2", verificacao="elemento de ligação tracionado: escoamento e ruptura",
         limitacao="An ≤ 0,85·Ag (6.5.3, chapas de emenda parafusadas); furação alternada não tratada"),
    dict(norma="NBR 8800", edicao="2008", item="6.5.5", verificacao="elemento de ligação: escoamento e ruptura por cisalhamento",
         limitacao=""),
    dict(norma="NBR 8800", edicao="2008", item="6.5.6", verificacao="colapso por rasgamento (block shear)",
         limitacao="Cts = 1; uma linha de parafusos: um plano de cisalhamento + tração até a borda (CBCA 5.1.7)"),
    dict(norma="NBR 8800", edicao="2008", item="6.1 / 6.3 / 6.5", verificacao="chapa simples (viga–apoio flexível)",
         limitacao="parafusos com V e V·e (método elástico); flexão da chapa elástica sem FLT, sem interação V+M e "
                   "sem ruptura por flexão na seção líquida; limites de ductilidade da chapa (t ≤ db/2 + 2 mm) não "
                   "verificados; alma sem recorte (sem rasgamento na viga); apoio (mesa/alma do pilar) não verificado"),
    dict(norma="NBR 8800", edicao="2008", item="6", verificacao="ligações rígidas, chapa de topo, placas de base",
         limitacao="não implementadas"),
]


# ---------------------------------------------------------------- componentes (funções puras)

def area_parafuso(d):
    return math.pi * d ** 2 / 4  # NBR 8800 6.3.2.2 (área bruta Ab)


def tracao_parafuso(d, fub):
    return 0.75 * area_parafuso(d) * fub / GA2  # NBR 8800 6.3.3.1 (Abe = 0,75·Ab)


def cisalhamento_parafuso(d, fub, rosca_no_plano=True, alta_resistencia=True):
    """Por plano de corte. NBR 8800 6.3.3.2: 0,5 só para alta resistência com rosca fora do plano."""
    c = 0.5 if (alta_resistencia and not rosca_no_plano) else 0.4
    return c * area_parafuso(d) * fub / GA2


def pressao_contato(lf, t, d, fu, deformacao_limitada=True):
    """NBR 8800 6.3.3.3 (furo-padrão): 1,2·lf·t·fu ≤ 2,4·db·t·fu (deformação limitada) ou 1,5 / 3,0."""
    a, b = (1.2, 2.4) if deformacao_limitada else (1.5, 3.0)
    return min(a * lf * t * fu, b * d * t * fu) / GA2


def interacao_tracao_cisalhamento(Ft, FtRd, Fv, FvRd):
    return (Ft / FtRd) ** 2 + (Fv / FvRd) ** 2  # NBR 8800 6.3.3.4 (≤ 1,0)


def resistencia_filete(b, fw, fy, fu):
    """kN por metro de filete de perna b. NBR 8800 6.2.5 tabela 8: metal da solda 0,6·Aw·fw/γw2
    (Aw = 0,707·b, 6.2.2.1); metal base pela 6.5.5 na face de fusão (AMB = b): escoamento e ruptura."""
    solda = 0.6 * 0.707 * b * fw / GW2
    base = min(0.6 * b * fy / GA1, 0.6 * b * fu / GA2)
    return dict(metal_solda=solda, metal_base=base, FwRd=min(solda, base))


def beta_filete_longo(lw, dw):
    """NBR 8800 6.2.2.2 d): filete longitudinal em ligação extrema de elemento axialmente solicitado,
    comprimento efetivo = β·lw, β = 1,2 − 0,002·lw/dw, 0,6 ≤ β ≤ 1,0 (Pfeil eq. 4.3, p. 100)."""
    return min(max(1.2 - 0.002 * lw / dw, 0.6), 1.0)


def filete_minimo(t):
    """NBR 8800 6.2.6.2.1 tabela 10 pela chapa mais fina: até 6,35 → 3; até 12,5 → 5; até 19 → 6; acima → 8 mm."""
    tm = t / MM
    return MM * (3 if tm <= 6.35 else 5 if tm <= 12.5 else 6 if tm <= 19 else 8)


def filete_maximo(t):
    """NBR 8800 6.2.6.2.2, ao longo de bordas: t se t < 6,35 mm; senão t − 1,5 mm (CBCA p. 39, 60)."""
    return t if t < 6.35 * MM else t - 1.5 * MM


def distancia_min_borda(d, laminada=False):
    """NBR 8800 6.3.11.1 tabela 14 (ver _BORDA): linha do diâmetro (± 0,05 mm); diâmetro fora da tabela usa
    a linha seguinte (a favor da segurança); acima de 36 mm, 1,75db / 1,25db."""
    dm = d / MM
    for dt, serra, lam in _BORDA:
        if dm <= dt + 0.05:
            return MM * (lam if laminada else serra)
    return (1.25 if laminada else 1.75) * d


def tracao_chapa(Ag, An, fy, fu, Ct=1.0):
    """NBR 8800 6.5.3 → 5.2.2: escoamento Ag·fy/γa1; ruptura Ae·fu/γa2, Ae = Ct·An ≤ 0,85·Ag (6.5.3)."""
    Ae = min(Ct * An, 0.85 * Ag)
    return dict(escoamento=Ag * fy / GA1, ruptura=Ae * fu / GA2, Ae=Ae)


def cisalhamento_chapa(Ag, Anv, fy, fu):
    return dict(escoamento=0.6 * fy * Ag / GA1, ruptura=0.6 * fu * Anv / GA2)  # NBR 8800 6.5.5


def colapso_rasgamento(Agv, Anv, Ant, fy, fu, Cts=1.0):
    # NBR 8800 6.5.6: (0,6·fu·Anv + Cts·fu·Ant)/γa2 ≤ (0,6·fy·Agv + Cts·fu·Ant)/γa2
    return min(0.6 * fu * Anv + Cts * fu * Ant, 0.6 * fy * Agv + Cts * fu * Ant) / GA2


# ---------------------------------------------------------------- esquema

_PAR = [("d_mm", "mm", "diâmetro nominal do parafuso (12,5–30 e M16–M30 usuais)", None, (10.0, 40.0)),
        ("tipo_parafuso", "", "especificação do parafuso", "A325", list(PARAFUSOS)),
        ("rosca_no_plano", "", "rosca no plano de corte (a favor da segurança: sim)", True, bool),
        ("pega", "m", "comprimento de pega (soma das espessuras ligadas; 6.3.7); 0 = não considerar", 0.0, (0.0, 0.3)),
        ("t_enchimento", "m", "soma das espessuras das chapas de enchimento (6.5.7.2); 0 = sem enchimento", 0.0,
         (0.0, 0.05))]
_CHAPA = [("t", "m", "espessura da chapa", None, (0.002, 0.1)),
          ("aco_chapa", "", "aço da chapa", "A-36", list(materiais.ACOS))]
_DEF = [("deformacao_limitada", "", "deformação no furo é limitação de projeto (1,2/2,4)", True, bool),
        ("borda_laminada", "", "bordas laminadas ou a maçarico (senão: serra/tesoura)", False, bool),
        ("sujeito_corrosao", "", "peça sujeita à corrosão atmosférica, aço resistente à corrosão sem pintura "
         "(espaçamento máximo 14t ≤ 180 mm, 6.3.10)", False, bool)]

_TIPOS = {
    "parafuso": dict(
        descricao="Um parafuso: tração, cisalhamento, tração + cisalhamento e pressão de contato na chapa (NBR 8800 6.3.3)",
        campos=_PAR + [("planos_corte", "", "planos de corte", 1, (1, 4))] + _CHAPA + [
            ("lf", "m", "distância livre, na direção da força, até a borda do furo adjacente ou da chapa", None,
             (0.001, 1.0)), _DEF[0]],
        esforcos=[("Ft", "kN", 0.0), ("Fv", "kN", 0.0)]),
    "solda_filete": dict(
        descricao="Filete de solda: metal da solda, metal base e dimensões mínima/máxima (NBR 8800 6.2)",
        campos=[("b", "m", "perna do filete", None, (0.002, 0.03)),
                ("L", "m", "comprimento total efetivo dos filetes", None, (0.01, 10.0)),
                ("eletrodo", "", "eletrodo", "E70", list(ELETRODOS)),
                ("aco_base", "", "aço do metal base", "A-36", list(materiais.ACOS)),
                ("t_base", "m", "espessura da chapa mais fina da junta", None, (0.002, 0.1)),
                ("L_longitudinal", "m", "comprimento de cada cordão longitudinal em ligação extrema de barra axial "
                 "(6.2.2.2 d: β se > 100·b); 0 = não se aplica", 0.0, (0.0, 10.0))],
        esforcos=[("F", "kN", None)]),
    "chapa_simples": dict(
        descricao="Viga–apoio flexível: chapa soldada ao apoio (filete duplo) e parafusada à alma da viga, "
                  "n parafusos numa linha vertical centrada na chapa",
        campos=[("h", "m", "altura da chapa", None, (0.05, 2.0))] + _CHAPA + _PAR + [
            ("n", "", "número de parafusos na linha vertical", None, (2, 20)),
            ("s", "m", "espaçamento vertical entre parafusos", None, (0.02, 0.5)),
            ("eh", "m", "distância horizontal do eixo dos parafusos à borda livre da chapa", 0.04, (0.01, 0.5)),
            ("b_solda", "m", "perna do filete chapa–apoio (dois lados)", 0.006, (0.002, 0.03)),
            ("eletrodo", "", "eletrodo", "E70", list(ELETRODOS)),
            ("tw", "m", "espessura da alma da viga", None, (0.002, 0.1)),
            ("aco_alma", "", "aço da viga", "A-36", list(materiais.ACOS)),
            ("e_alma", "m", "distância do eixo dos parafusos à extremidade da viga", 0.04, (0.01, 0.5)),
            ("e", "m", "excentricidade: eixo dos parafusos à face do apoio", None, (0.0, 0.5))] + _DEF,
        esforcos=[("V", "kN", None)]),
    "ligacao_tracao": dict(
        descricao="Chapa de ligação de tirante/diagonal parafusada: n_linhas paralelas à força com "
                  "n_por_linha parafusos, grupo centrado na largura",
        campos=[("b", "m", "largura da chapa", None, (0.02, 2.0))] + _CHAPA + _PAR + [
            ("n_linhas", "", "linhas de furação paralelas à força", None, (1, 10)),
            ("n_por_linha", "", "parafusos em cada linha", None, (1, 20)),
            ("s", "m", "espaçamento na direção da força (se n_por_linha > 1)", 0.07, (0.02, 0.5)),
            ("g", "m", "gabarito entre linhas (se n_linhas > 1)", 0.07, (0.02, 0.5)),
            ("e1", "m", "distância do último furo à extremidade da chapa (direção da força)", None, (0.01, 0.5)),
            ("planos_corte", "", "planos de corte por parafuso", 1, (1, 4)),
            ("Ct", "", "coeficiente de redução da área líquida (5.2.5)", 1.0, (0.1, 1.0))] + _DEF,
        esforcos=[("N", "kN", None)]),
}
_INTEIROS = {"n", "n_linhas", "n_por_linha", "planos_corte"}


def tipos_ligacao():
    """Esquema dos formulários (ESPEC §7.4). Campos de escolha trazem também `opcoes`."""
    out = []
    for tipo, t in _TIPOS.items():
        campos = []
        for nome, un, desc, pad, regra in t["campos"]:
            c = dict(nome=nome, unidade=un, descricao=desc, padrao=pad)
            if isinstance(regra, list):
                c["opcoes"] = list(regra)
            campos.append(c)
        out.append(dict(tipo=tipo, descricao=t["descricao"], campos=campos,
                        esforcos=[dict(nome=n, unidade=u) for n, u, _ in t["esforcos"]]))
    for tipo, t in tipos_contrato().items():   # docs/v3/LIGACOES-CONTRATO.md (entradas em mm)
        out.append(dict(tipo=tipo, nome=t["nome"], familia=t["familia"], liga=t["liga"], descricao=t["descricao"],
                        campos=[dict(nome=k, descricao=c["rotulo"], **c) for k, c in t["entradas"].items()],
                        esforcos=[dict(nome=n, unidade="kN") for n in t.get("esforcos", ("N", "V"))]))
    return out


# Módulos de família do contrato (docs/v3/LIGACOES-CONTRATO.md §Registro): cada um expõe TIPOS.
_MODULOS_CONTRATO = ("ligacoes_flexiveis", "ligacoes_rigidas", "ligacoes_contraventamento")


def tipos_contrato():
    import importlib
    out = {}
    for m in _MODULOS_CONTRATO:
        out.update(importlib.import_module(f"{__package__}.{m}").TIPOS)
    return out


def _validar(tipo, dados, esforcos):
    if tipo not in _TIPOS:
        raise ValueError(f"tipo de ligação '{tipo}' desconhecido; válidos: {', '.join(_TIPOS)}")
    dados, esforcos = dict(dados or {}), dict(esforcos or {})
    erros, d, e = [], {}, {}
    for nome, _un, _desc, pad, regra in _TIPOS[tipo]["campos"]:
        v = dados.get(nome, pad)
        if v is None:
            erros.append(f"{nome}: obrigatório")
        elif regra is bool:
            d[nome] = bool(v)
        elif isinstance(regra, list):
            if str(v).upper() not in [o.upper() for o in regra]:
                erros.append(f"{nome}: '{v}' inválido ({', '.join(regra)})")
            else:
                d[nome] = next(o for o in regra if o.upper() == str(v).upper())
        else:
            try:
                x = float(v)
            except (TypeError, ValueError):
                erros.append(f"{nome}: número esperado, recebido {v!r}")
                continue
            if not math.isfinite(x):
                erros.append(f"{nome}: número finito esperado, recebido {v!r}")
            elif nome in _INTEIROS and x != int(x):
                erros.append(f"{nome}: inteiro esperado")
            elif not regra[0] <= x <= regra[1]:
                erros.append(f"{nome}: {x:g} fora da faixa [{regra[0]:g}, {regra[1]:g}]")
            else:
                d[nome] = int(x) if nome in _INTEIROS else x
    for nome, _un, pad in _TIPOS[tipo]["esforcos"]:
        v = esforcos.get(nome, pad)
        try:
            x = float(v) if v is not None else None
        except (TypeError, ValueError):
            x = -1.0
        if x is None:
            erros.append(f"{nome}: esforço obrigatório")
        elif x < 0 or not math.isfinite(x):
            erros.append(f"{nome}: esforço deve ser ≥ 0 (valor de cálculo, em módulo)")
        else:
            e[nome] = x
    if erros:
        raise ValueError(f"ligação '{tipo}': " + "; ".join(erros))
    return d, e


# ---------------------------------------------------------------- tipologias

def _taxa(nome, num, den):
    # denominador ≤ 0 não ocorre após _geometria; se ocorrer, é entrada inválida (nunca taxa infinita)
    if not (math.isfinite(den) and den > 0):
        raise ValueError(f"{nome}: denominador {den:g} ≤ 0 — geometria ou dados inválidos")
    return float(num / den)


def _v(nome, sd, rd, unidade, item):
    return dict(nome=nome, solicitante=float(sd), resistente=float(rd), unidade=unidade,
                taxa=_taxa(nome, sd, rd), item=item)


# Disposições construtivas (dimensões, bordas, espaçamentos): unidade "m" — só reprovam (ESPEC §7.2).
def _minimo(nome, minimo, real, item, parametro=None):  # taxa = mínimo / real
    return _dica(dict(nome=nome, solicitante=float(minimo), resistente=float(real), unidade="m",
                      taxa=_taxa(nome, minimo, real), item=item), "sobe", parametro)


def _dica(x, sentido, parametro=None):
    """Dica do otimizador (docs/v3/LIGACOES-CONTRATO.md §dicas): `sentido` = para onde o valor verificado precisa ir
    ("sobe" num mínimo, "desce" num máximo); `parametro` = campo de entrada que o controla (opcional)."""
    x["sentido"] = sentido
    if parametro:
        x["parametro"] = parametro
    return x


def _geometria(tipo, dn, regras):
    """Geometria impossível → ValueError com os campos. dn = furo-padrão + 2 mm (6.3.6 tab. 12 + 5.2.4.1):
    espaçamento ≤ dn sobrepõe os furos na seção líquida; borda ≤ dn/2 rompe a borda (áreas líquidas ≤ 0).
    regras: (campo, valor, limite, descrição)."""
    erros = [f"{campo}: {desc} = {v * 1e3:g} mm, deve ser > {'dn' if lim == dn else 'dn/2'} = {lim * 1e3:g} mm"
             for campo, v, lim, desc in regras if v <= lim + 1e-12]
    if erros:
        raise ValueError(f"ligação '{tipo}': geometria impossível (dn = {dn * 1e3:g} mm, furo + 2 mm) — "
                         + "; ".join(erros))


def _maximo(nome, real, maximo, item, parametro=None):  # taxa = real / máximo
    return _dica(_v(nome, real, maximo, "m", item), "desce", parametro)


def _parafuso(d):
    p = PARAFUSOS[d["tipo_parafuso"]]
    db = d["d_mm"] * MM
    fub = p["fub"]
    avisos = []
    if d["tipo_parafuso"] == "A325" and d["d_mm"] > 24 + 1e-9 and abs(d["d_mm"] - 25.4) > 0.05:
        fub = 725  # tab. A.3: 24 < db ≤ 36 mm (e > 1"); d = 25,4 mm é 1" → 825
        if d["d_mm"] < 25.4:
            avisos.append(f"A325 com d = {d['d_mm']:g} mm (métrico > 24 mm): fub = 725 MPa (tab. A.3); "
                          "para 1\" informe d = 25,4 mm")
    if not any(abs(d["d_mm"] - x) < 0.05 for x in DIAMETROS_USUAIS_MM):
        avisos.append(f"d = {d['d_mm']:g} mm não é diâmetro usual")
    return db, fub * MPa, p["alta"], db + FURO_FOLGA, avisos


def _aco(nome):
    m = materiais.aco(nome)
    return m.fy, m.fu


def _bordas(v, db, t, borda, item_t="6.3.11.1 / tabela 14", laminada=False, nome=""):
    emin = distancia_min_borda(db, laminada)
    v.append(_minimo(f"borda mínima{nome}", emin, borda, item_t))
    v.append(_maximo(f"borda máxima{nome}", borda, min(12 * t, 0.150), "6.3.12"))


def fator_pega_longa(pega, db):
    """NBR 8800 6.3.7: pega > 5db → Fv,Rd reduzida em 1 % para cada 1,5 mm adicionais (cada fração conta, a favor)."""
    excesso = pega - 5 * db
    return 1.0 if excesso <= 1e-9 else 1 - 0.01 * math.ceil(excesso / (1.5 * MM) - 1e-9)


def fator_enchimento(ts):
    """NBR 8800 6.5.7.2 a): ts ≤ 6 mm → 1; senão 1 − 0,0154(ts − 6), ts em mm (válido até 19 mm)."""
    return 1.0 if ts <= 6 * MM + 1e-12 else 1 - 0.0154 * (ts / MM - 6)


def _fatores(d, db, v, avisos):
    """(fator de Fv,Rd, fator de Fc,Rd) por 6.3.7 e 6.5.7.2; acrescenta o limite ts ≤ 19 mm (só reprova)."""
    kp, ke = fator_pega_longa(d["pega"], db), fator_enchimento(d["t_enchimento"])
    if kp < 1:
        avisos.append(f"pega longa ({d['pega'] * 1e3:.0f} mm > 5db = {5 * db * 1e3:.0f} mm): Fv,Rd × {kp:.2f} (6.3.7)")
    if d["t_enchimento"] > 0:
        v.append(_maximo("chapas de enchimento: ts ≤ 19 mm (6.5.7.2 a; b e c não verificadas)",
                         d["t_enchimento"], 0.019, "6.5.7.2"))
        if ke < 1:
            avisos.append(f"chapas de enchimento ts = {d['t_enchimento'] * 1e3:.1f} mm > 6 mm: Fv,Rd e Fc,Rd × "
                          f"{ke:.4f} (6.5.7.2 a)")
    return kp * ke, ke


def _ligacao_parafuso(d, e):
    db, fub, alta, dh, avisos = _parafuso(d)
    fy, fu = _aco(d["aco_chapa"])
    v = []
    kv, kc = _fatores(d, db, v, avisos)
    FtRd = tracao_parafuso(db, fub)
    FvRd = kv * cisalhamento_parafuso(db, fub, d["rosca_no_plano"], alta) * d["planos_corte"]
    if e["Ft"] > 0:
        v.append(_v("tração no parafuso", e["Ft"], FtRd, "kN", "6.3.3.1"))
        avisos.append("efeito de alavanca não incluído: some-o a Ft (NBR 8800 6.3.5)")
    if e["Fv"] > 0:
        v.append(_v("cisalhamento no parafuso", e["Fv"], FvRd, "kN", "6.3.3.2"))
        v.append(_v("pressão de contato / rasgamento", e["Fv"],
                    kc * pressao_contato(d["lf"], d["t"], db, fu, d["deformacao_limitada"]), "kN", "6.3.3.3"))
    if e["Ft"] > 0 and e["Fv"] > 0:
        v.append(_v("tração + cisalhamento", interacao_tracao_cisalhamento(e["Ft"], FtRd, e["Fv"], FvRd), 1.0, "",
                    "6.3.3.4"))
    if not any(x["unidade"] != "m" for x in v):
        avisos.append("sem esforços: nada a verificar")
    return v, avisos


def _ligacao_solda(d, e):
    fy, fu = _aco(d["aco_base"])
    r = resistencia_filete(d["b"], ELETRODOS[d["eletrodo"]] * MPa, fy, fu)
    beta = beta_filete_longo(d["L_longitudinal"], d["b"]) if d["L_longitudinal"] > 0 else 1.0
    v = [_v("solda: resistência (metal da solda e metal base)" + (f", β = {beta:.3f}" if beta < 1 else ""),
            e["F"] / (beta * d["L"]), r["FwRd"], "kN/m",
            "6.2.5 / tabela 8" + (" (metal base)" if r["metal_base"] < r["metal_solda"] else "")
            + (" / 6.2.2.2 d)" if beta < 1 else "")),
         _minimo("solda: dimensão mínima", filete_minimo(d["t_base"]), d["b"], "6.2.6.2.1 / tabela 10", "b"),
         _maximo("solda: dimensão máxima (ao longo de borda)", d["b"], filete_maximo(d["t_base"]), "6.2.6.2.2", "b"),
         _minimo("solda: comprimento mínimo do filete (4b e 40 mm)", max(4 * d["b"], 0.040), d["L"], "6.2.6.2.3")]
    return v, ["resistência do filete sem o acréscimo pelo ângulo θ da força (a favor da segurança)"]


def _ligacao_chapa_simples(d, e):
    db, fub, alta, dh, avisos = _parafuso(d)
    dn = dh + FURO_DANO
    fy, fu = _aco(d["aco_chapa"])
    fyw, fuw = _aco(d["aco_alma"])
    n, s, h, t, V = d["n"], d["s"], d["h"], d["t"], e["V"]
    ev = (h - (n - 1) * s) / 2
    _geometria("chapa_simples", dn, [
        ("s", s, dn, "espaçamento s"),
        ("h", ev, dn / 2, f"borda vertical (h − (n − 1)·s)/2 com n = {n}"),
        ("eh", d["eh"], dn / 2, "borda horizontal eh"),
        ("e_alma", d["e_alma"], dn / 2, "borda da alma e_alma")])
    lim = d["deformacao_limitada"]
    M = V * d["e"]
    # parafusos: V centrado + V·e pelo método elástico (CBCA vol. 2, 5.2.1); linha única: Σr² = Σy²
    ys = [(i - (n - 1) / 2) * s for i in range(n)]
    R = math.hypot(V / n, M * max(map(abs, ys)) / sum(y * y for y in ys))
    R *= _fator_longa((n - 1) * s, avisos)
    fat = []
    kv, kc = _fatores(d, db, fat, avisos)
    FvRd = kv * cisalhamento_parafuso(db, fub, d["rosca_no_plano"], alta)
    lf_ch = min(s - dh, ev - dh / 2, d["eh"] - dh / 2)   # direção da resultante varia: menor lf
    lf_al = min(s - dh, d["e_alma"] - dh / 2)
    v = [_v("parafusos: cisalhamento (V e V·e)", R, FvRd, "kN", "6.3.3.2"),
         _v("pressão de contato na chapa", R, kc * pressao_contato(lf_ch, t, db, fu, lim), "kN", "6.3.3.3"),
         _v("pressão de contato na alma", R, kc * pressao_contato(lf_al, d["tw"], db, fuw, lim), "kN", "6.3.3.3")] + fat
    c = cisalhamento_chapa(h * t, (h - n * dn) * t, fy, fu)
    v += [_v("chapa: escoamento por cisalhamento", V, c["escoamento"], "kN", "6.5.5"),
          _v("chapa: ruptura por cisalhamento", V, c["ruptura"], "kN", "6.5.5")]
    Lv = ev + (n - 1) * s
    v.append(_v("chapa: colapso por rasgamento", V,
                colapso_rasgamento(Lv * t, (Lv - (n - 0.5) * dn) * t, (d["eh"] - 0.5 * dn) * t, fy, fu), "kN", "6.5.6"))
    v.append(_v("chapa: flexão (V·e, elástica)", M, t * h ** 2 / 6 * fy / GA1, "kN·m", "5.4 / 6.5"))
    # solda: dois filetes de comprimento h, linha elástica: v = V/2L; f = 6M/(2L²)
    b = d["b_solda"]
    Lw = h   # 6.2.2.2 c): comprimento efetivo = comprimento total do filete
    r = resistencia_filete(b, ELETRODOS[d["eletrodo"]] * MPa, fy, fu)
    v.append(_v("solda: resistência (V e V·e)", math.hypot(V / (2 * Lw), 6 * M / (2 * Lw ** 2)), r["FwRd"], "kN/m",
                "6.2.5 / tabela 8"))
    v += [_minimo("solda: dimensão mínima", filete_minimo(t), b, "6.2.6.2.1 / tabela 10", "b_solda"),
          _maximo("solda: dimensão máxima", b, filete_maximo(t), "6.2.6.2.2", "b_solda"),
          _espacamento_minimo(s, db, avisos),
          _espacamento_maximo(s, min(t, d["tw"]), d["sujeito_corrosao"])]
    lam = d["borda_laminada"]
    _bordas(v, db, t, ev, laminada=lam, nome=" (chapa, vertical)")
    _bordas(v, db, t, d["eh"], laminada=lam, nome=" (chapa, horizontal)")
    _bordas(v, db, d["tw"], d["e_alma"], laminada=lam, nome=" (alma)")
    avisos += ["apoio (alma/mesa do pilar ou viga suporte) não verificado; metal base da solda só pela chapa",
               "alma da viga sem recorte: rasgamento da alma não verificado"]
    return v, avisos


def _ligacao_tracao(d, e):
    db, fub, alta, dh, avisos = _parafuso(d)
    dn = dh + FURO_DANO
    fy, fu = _aco(d["aco_chapa"])
    nl, np_, s, g, t, b, e1, N = d["n_linhas"], d["n_por_linha"], d["s"], d["g"], d["t"], d["b"], d["e1"], e["N"]
    e2 = (b - (nl - 1) * g) / 2
    _geometria("ligacao_tracao", dn, [("e1", e1, dn / 2, "borda de extremidade e1")]
               + ([("s", s, dn, "espaçamento s")] if np_ > 1 else [])
               + ([("g", g, dn, "gabarito g")] if nl > 1 else [])
               + [("b", e2, dn / 2, f"borda lateral (b − (n_linhas − 1)·g)/2 com n_linhas = {nl}")])
    n = nl * np_
    lim = d["deformacao_limitada"]
    k = _fator_longa((np_ - 1) * s, avisos)   # 6.3.8
    fat = []
    kv, kc = _fatores(d, db, fat, avisos)
    FvRd = kv * cisalhamento_parafuso(db, fub, d["rosca_no_plano"], alta) * d["planos_corte"]
    # 6.3.3.3: resistência total ao contato = soma por furo: extremidade (lf = e1 − dh/2) e internos (s − dh)
    Fc = [kc * pressao_contato(e1 - dh / 2, t, db, fu, lim)] + [kc * pressao_contato(s - dh, t, db, fu, lim)] * (np_ - 1)
    v = [_v("parafusos: cisalhamento (por parafuso)", k * N / n, FvRd, "kN", "6.3.3.2"),
         _v("pressão de contato / rasgamento na chapa (soma por furo)", k * N, nl * sum(Fc), "kN", "6.3.3.3"),
         _v("grupo de parafusos: Σ min(Fv,Rd; Fc,Rd) por parafuso", k * N, nl * sum(min(FvRd, f) for f in Fc),
            "kN", "6.3.3.2 / 6.3.3.3")] + fat
    tr = tracao_chapa(b * t, (b - nl * dn) * t, fy, fu, d["Ct"])
    v += [_v("chapa: escoamento da seção bruta", N, tr["escoamento"], "kN", "6.5.3 → 5.2.2 a)"),
          _v("chapa: ruptura da seção líquida", N, tr["ruptura"], "kN", "6.5.3 → 5.2.2 b) / 5.2.3")]
    Lv = e1 + (np_ - 1) * s
    Agv1, Anv1 = Lv * t, (Lv - (np_ - 0.5) * dn) * t
    if nl == 1:   # um plano de cisalhamento + tração até a borda (CBCA vol. 2, 5.1.7)
        Fr = colapso_rasgamento(Agv1, Anv1, (e2 - 0.5 * dn) * t, fy, fu)
    else:         # bloco central (entre linhas externas) e blocos laterais (até as bordas)
        Fr = min(colapso_rasgamento(2 * Agv1, 2 * Anv1, (nl - 1) * (g - dn) * t, fy, fu),
                 colapso_rasgamento(2 * Agv1, 2 * Anv1, 2 * (e2 - 0.5 * dn) * t, fy, fu))
    v.append(_v("chapa: colapso por rasgamento", N, Fr, "kN", "6.5.6"))
    if np_ > 1:
        v += [_espacamento_minimo(s, db, avisos, " (direção da força)"),
              _espacamento_maximo(s, t, d["sujeito_corrosao"])]
    if nl > 1:
        v += [_espacamento_minimo(g, db, avisos, " (entre linhas)"),
              _espacamento_maximo(g, t, d["sujeito_corrosao"])]
    lam = d["borda_laminada"]
    _bordas(v, db, t, e1, laminada=lam, nome=" (extremidade)")
    _bordas(v, db, t, e2, laminada=lam, nome=" (lateral)")
    return v, avisos


def _fator_longa(comprimento, avisos):
    """6.3.8: ligação por contato com comprimento (na direção da força) > 1270 mm → esforços × 1,25."""
    if comprimento > COMPRIMENTO_LONGA + 1e-9:
        avisos.append(f"ligação longa ({comprimento * 1e3:.0f} mm > 1270 mm): Fv,Sd e Fc,Sd × 1,25 (6.3.8)")
        return 1.25
    return 1.0


def _espacamento_minimo(s, db, avisos, nome=""):
    # 6.3.9: distância entre centros de furos ≥ 2,7db, de preferência 3db
    if 2.7 * db <= s < 3 * db:
        avisos.append(f"espaçamento {s * 1e3:.1f} mm{nome} < 3db = {3 * db * 1e3:.1f} mm (preferível pela 6.3.9)")
    return _minimo(f"espaçamento mínimo entre furos (2,7db){nome}", 2.7 * db, s, "6.3.9")


def _espacamento_maximo(s, t, corrosao=False):
    # 6.3.10: 24t da parte ligada mais fina ≤ 300 mm; sujeita à corrosão atmosférica (aço resistente à
    # corrosão sem pintura): 14t ≤ 180 mm
    if corrosao:
        return _maximo("espaçamento máximo entre parafusos (14t ≤ 180 mm, sujeito à corrosão)", s,
                       min(14 * t, 0.180), "6.3.10")
    return _maximo("espaçamento máximo entre parafusos (24t ≤ 300 mm)", s, min(24 * t, 0.300), "6.3.10")


_FUNCOES = {"parafuso": _ligacao_parafuso, "solda_filete": _ligacao_solda,
            "chapa_simples": _ligacao_chapa_simples, "ligacao_tracao": _ligacao_tracao}


def verificar_ligacao(tipo, dados, esforcos):
    """ESPEC §7.4 → {taxa_max, criterio, item, ok, verificacoes, avisos}. Campos inválidos → ValueError com todos.
    ESPEC §7.2: taxa_max/criterio = pior verificação de resistência; disposições construtivas (unidade "m":
    dimensões mínimas/máximas, bordas, espaçamentos) só reprovam (ok = False) e ficam em verificacoes.
    Tipos do contrato (tipos_contrato) devolvem o resultado do contrato (ok, taxa, verificacoes, geometria)."""
    if tipo not in _TIPOS and tipo in (tc := tipos_contrato()):
        return tc[tipo]["verificar"](dados, esforcos)
    return com_pontas(tipo, lambda dd: _verificar_antigo(tipo, dd, esforcos))(dados)


def _verificar_antigo(tipo, dados, esforcos):
    d, e = _validar(tipo, dados, esforcos)
    v, avisos = _FUNCOES[tipo](d, e)
    if not v:
        return dict(taxa_max=0.0, criterio="", item="", ok=True, verificacoes=[], avisos=avisos)
    res = [x for x in v if x["unidade"] != "m"]
    pior = max(res, key=lambda x: x["taxa"]) if res else dict(v[0], taxa=0.0)
    falhas = [x["nome"] for x in v if x["unidade"] == "m" and x["taxa"] > 1.0 + 1e-9]
    if falhas:
        avisos.append("disposições construtivas não atendidas: " + "; ".join(falhas))
    if any("tabela 14" in x["item"] for x in v):
        avisos.append("borda mínima (tabela 14): bordas cortadas com serra/tesoura salvo indicação")
    return dict(taxa_max=pior["taxa"], criterio=pior["nome"], item=f"NBR 8800:2008 {pior['item']}",
                ok=pior["taxa"] <= 1.0 + 1e-9 and not falhas, verificacoes=v, avisos=avisos)


# ---------------------------------------------------------------- pontas (docs/v3/PONTAS-CONTRATO.md, G2)
# Todo resultado de verificar traz geometria["pontas"] (m, referencial local da ligação). Contexto opcional em
# dados["contexto_pontas"] = {tipo_no, comprimento (m), desloc, params}: o agrupamento passa o da barra real.
BARRA_CURTA = "barra curta demais para a ligação"
APOIO_SEM_PERFIL = "apoio sem perfil: ponta aproximada"
_NO_APOIO = {"mesa_pilar": "viga_pilar_mesa", "alma_pilar": "viga_pilar_alma", "alma_viga": "viga_viga"}
_NO_FAMILIA = {"contraventamento": "gusset_contraventamento", "emenda": "emenda"}
_COM_APOIO = ("viga_pilar_mesa", "viga_pilar_alma", "viga_viga", "gusset_contraventamento")


def perfil_pontas(v):
    """Perfil do catálogo, dict {d, bf, tw, tf} em mm ou props já em m ({h, b, ...}) → props; None se não há perfil."""
    if isinstance(v, dict):
        if "h" in v:
            return v
        try:
            return dict(h=float(v["d"]) * MM, b=float(v["bf"]) * MM, tw=float(v["tw"]) * MM, tf=float(v["tf"]) * MM)
        except (KeyError, TypeError, ValueError):
            return None
    try:
        return secoes.catalogo(str(v)).props if v else None
    except KeyError:
        return None


def pontas_de(tipo, dados, ctx=None, aproximacao=None):
    """Pontas do tipo de ligação a partir dos perfis em `dados` e do contexto (ver pontas.pontas_locais).
    ctx: tipo_no, comprimento (livre, m), desloc, params, perfil_apoio (o agrupamento passa o do modelo).
    aproximacao: lista que recebe APOIO_SEM_PERFIL quando o tipo pede apoio e não há perfil dele."""
    from . import pontas as _p
    d, ctx = dados or {}, ctx or {}
    reg = registro(tipo) or META.get(tipo, {})
    tipo_no = ctx.get("tipo_no") or _NO_APOIO.get(d.get("apoio")) or _NO_FAMILIA.get(reg.get("familia")) or         (reg.get("liga") or ["viga-pilar-mesa"])[0].replace("-", "_")
    barra = perfil_pontas(d.get("perfil_viga") or d.get("perfil_pilar"))
    apoio = perfil_pontas(ctx.get("perfil_apoio") or d.get("perfil_suporte")
                          or (d.get("perfil_pilar") if d.get("perfil_viga") else None))
    theta = None
    for s in ("", "_1"):   # diagonal: aba ligada/altura (dim_a) × dim_b, em mm; tirante: D
        a = d.get(f"dim_a{s}") or d.get("D_barra")
        if barra is None and a:
            b = d.get(f"dim_b{s}") or a
            barra = dict(h=float(a) * MM, b=float(b) * MM)
            theta = math.radians(float(d.get(f"theta{s}", 45.0)))
    params = dict(ctx.get("params") or {})
    if tipo.startswith("chapa_topo") and "folga" not in params:   # chapa encostada: viga cortada a tp do apoio
        params["folga"] = float(d.get("tp") or reg["entradas"]["tp"]["padrao"]) * MM
    if d.get("folga") and "folga" not in params:   # folga viga–apoio do detalhe (mm) = folga da ponta
        params["folga"] = float(d["folga"]) * MM
    if aproximacao is not None and tipo_no in _COM_APOIO and (barra is None or apoio is None):
        aproximacao.append(APOIO_SEM_PERFIL)
    return _p.pontas_locais(tipo_no, barra, apoio, comprimento=ctx.get("comprimento", math.inf), theta=theta,
                            recuo=0.0 if tipo_no == "emenda" else None, desloc=ctx.get("desloc"), params=params)


def _recortes_ponta(pts):
    """Copes sup/inf da ponta → campos recorte_*_h/_c (m) que os verificadores flexíveis já usam."""
    out = {}
    for c in (pts[0]["recortes"] if pts else []):
        if c["tipo"] == "cope" and c["face"] in ("sup", "inf"):
            out[f"recorte_{c['face']}_h"] = max(out.get(f"recorte_{c['face']}_h", 0.0), c["altura"])
            out[f"recorte_{c['face']}_c"] = max(out.get(f"recorte_{c['face']}_c", 0.0), c["comprimento"])
    return out


def com_pontas(tipo, verificar):
    """verificar(dados) → resultado com geometria["pontas"]. Barra curta (pontas.BarraCurta) não derruba: o cálculo
    segue sem o limite de comprimento e o resultado sai com ok = False, `motivo` e aviso (a barra fica no modelo).
    Os copes da ponta vão para o cálculo em dados["_recortes_ponta"] (m)."""
    from .pontas import BarraCurta

    def f(dados):
        dados = dict(dados or {})
        ctx = dict(dados.get("contexto_pontas") or {})
        motivo, aprox = None, []
        while True:
            try:
                pts = pontas_de(tipo, dados, ctx, aprox)
                dados["_recortes_ponta"] = _recortes_ponta(pts)
                r = verificar(dados)
                break
            except BarraCurta:
                if motivo:
                    raise
                motivo, ctx["comprimento"], aprox = BARRA_CURTA, math.inf, []
                dados["contexto_pontas"] = ctx
        usados = r.pop("recortes_usados", None)
        if usados and pts:   # o módulo ajustou o recorte: a ponta desenha o que foi calculado
            _p0 = pts[0]
            raio = next((c["raio"] for c in _p0["recortes"] if c["tipo"] == "cope"), 0.010)
            _p0["recortes"] = [c for c in _p0["recortes"] if not (c["tipo"] == "cope" and c["face"] in ("sup", "inf"))]
            for face in ("sup", "inf"):
                if usados.get(f"recorte_{face}_h", 0.0) > 0:
                    _p0["recortes"].append(dict(tipo="cope", face=face, altura=usados[f"recorte_{face}_h"],
                                                comprimento=usados[f"recorte_{face}_c"], raio=raio))
        geo = r.get("geometria") or (geometria_basica(tipo, dados) if tipo in _TIPOS else None) or {}
        geo.setdefault("pontas", pts)
        r["geometria"] = geo
        if aprox:
            r.setdefault("avisos", []).append(APOIO_SEM_PERFIL)
        if motivo:
            r["ok"], r["motivo"], r["excluir_interferencia"] = False, motivo, True   # G5: WPF não conta
            r.setdefault("avisos", []).append(f"{motivo}: recuo + recorte alcançam o comprimento da barra "
                                              "(barra mantida; revise o detalhe ou a modelagem)")
        return r
    f.__doc__ = getattr(verificar, "__doc__", None)
    return f


def registrar_pontas(tipos):
    """Envolve o `verificar` de cada tipo do registro (TIPOS de um módulo de família) com com_pontas."""
    for t, reg in tipos.items():
        reg["verificar"] = _envolver(t, reg["verificar"])


def _envolver(tipo, v):
    def verificar(dados, esforcos):
        return com_pontas(tipo, lambda dd: v(dd, esforcos))(dados)
    verificar.__doc__ = v.__doc__
    return verificar


# ---------------------------------------------------------------- interface do projetista (docs/v3/LIGACOES-UI.md)
# Metadados do contrato (nome, família, liga) dos tipos antigos deste módulo, geometria esquemática deles, registro dos
# tipos do contrato em qualquer módulo de família, otimizador simples e memorial em HTML.

META = {
    "parafuso": dict(nome="Parafuso isolado", familia="componente", liga=["emenda"]),
    "solda_filete": dict(nome="Filete de solda", familia="componente", liga=[]),
    "chapa_simples": dict(nome="Chapa simples soldada (genérica)", familia="flexivel",
                          liga=["viga-pilar-mesa", "viga-pilar-alma", "viga-viga"]),
    "ligacao_tracao": dict(nome="Chapa de ligação tracionada", familia="contraventamento",
                           liga=["gusset-contraventamento", "no-trelica"]),
}


def registro(tipo):
    """Entrada do contrato (TIPOS[tipo] de um módulo de família) ou None (tipo antigo deste módulo)."""
    return tipos_contrato().get(tipo)


def esquema(tipo):
    """Campos do tipo: [{nome, unidade, padrao, min, max, opcoes, inteiro}] (antigos em m, contrato em mm)."""
    if tipo in _TIPOS:
        out = []
        for nome, un, _desc, pad, regra in _TIPOS[tipo]["campos"]:
            c = dict(nome=nome, unidade=un, padrao=pad, inteiro=nome in _INTEIROS, min=None, max=None, opcoes=None)
            if isinstance(regra, list):
                c["opcoes"] = list(regra)
            elif isinstance(regra, tuple):
                c["min"], c["max"] = regra
            out.append(c)
        return out
    reg = registro(tipo)
    if reg is None:
        raise ValueError(f"tipo de ligação '{tipo}' desconhecido")
    out = []
    for k, c in reg["entradas"].items():
        p = c.get("padrao")
        out.append(dict(nome=k, unidade=c.get("unidade", ""), padrao=p, min=c.get("min"), max=c.get("max"),
                        opcoes=c.get("opcoes"), inteiro=isinstance(p, int) and not isinstance(p, bool) and not c.get("opcoes")
                        and c.get("unidade", "") in ("", "-")))
    return out


def _quad(nome, origem, u, v, larg, alt, t):
    """Chapa retangular larg × alt (mm): ponto = origem + a·u + b·v, normal = u × v (como em ligacoes_flexiveis)."""
    n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
    return dict(nome=nome, contorno=[[0, 0], [larg, 0], [larg, alt], [0, alt]], origem=list(origem), normal=n,
                u=list(u), v=list(v), espessura=t)


def geometria_basica(tipo, dados):
    """Geometria esquemática (mm, referencial do contrato) dos tipos antigos; None se faltam dados.
    ponytail: só para o desenho — os tipos antigos não recebem perfil; o apoio é a face x = 0."""
    if tipo not in _TIPOS:
        return None
    k = {}
    for c in esquema(tipo):
        v = (dados or {}).get(c["nome"], c["padrao"])
        if isinstance(v, (int, float)) and not isinstance(v, bool):
            k[c["nome"]] = float(v) / MM if c["unidade"] == "m" else float(v)
    U, V, Z = [1, 0, 0], [0, 0, 1], [0, -1, 0]
    try:
        if tipo == "chapa_simples":
            h, t, n, s, eh, e, tw, b = k["h"], k["t"], int(k["n"]), k["s"], k["eh"], k["e"], k["tw"], k["b_solda"]
            db = k["d_mm"]
            par = [dict(d=db, comprimento=round(t + tw + 1.2 * db + 6), origem=[e, tw / 2 + t, (i - (n - 1) / 2) * s], eixo=Z)
                   for i in range(n)]
            sol = [dict(tipo="filete", perna=b, pontos=[[0, y, -h / 2], [0, y, h / 2]]) for y in (tw / 2, tw / 2 + t)]
            dv = round(h / 0.75)
            return dict(chapas=[_quad("chapa simples", [0, tw / 2 + t / 2, -h / 2], U, V, e + eh, h, t)], cantoneiras=[],
                        parafusos=par, soldas=sol,
                        recortes=[dict(barra="viga", tipo="recuo", parametros=dict(x_ponta=max(0.0, e - k["e_alma"])))],
                        membros=[dict(barra="viga", perfil="viga (esquemática)", d=dv, bf=round(dv * 0.5), tw=tw,
                                      tf=max(1.6 * tw, 8.0))])
        if tipo == "ligacao_tracao":
            b, t, nl, np_, s, g, e1 = k["b"], k["t"], int(k["n_linhas"]), int(k["n_por_linha"]), k["s"], k["g"], k["e1"]
            db, x0 = k["d_mm"], 80.0
            par = [dict(d=db, comprimento=round(2 * t + 1.2 * db + 6), origem=[x0 + i * s, t / 2, (j - (nl - 1) / 2) * g], eixo=Z)
                   for i in range(np_) for j in range(nl)]
            return dict(chapas=[_quad("chapa de ligação", [0, 0, -b / 2], U, V, x0 + (np_ - 1) * s + e1, b, t)], cantoneiras=[],
                        parafusos=par, soldas=[dict(tipo="filete", perna=max(4.0, t * 0.6), pontos=[[0, y, -b / 2], [0, y, b / 2]])
                                               for y in (-t / 2, t / 2)], recortes=[])
        if tipo == "parafuso":
            t, db, lf = k["t"], k["d_mm"], k["lf"]
            borda = lf + (db + 1.5) / 2
            return dict(chapas=[_quad("chapa", [0, 0, -50], U, V, borda + 70, 100, t)], cantoneiras=[], soldas=[], recortes=[],
                        parafusos=[dict(d=db, comprimento=round(2 * t + 1.2 * db + 6), origem=[70, t / 2, 0], eixo=Z)])
        if tipo == "solda_filete":
            t, L_, b = k["t_base"], k["L"], k["b"]
            return dict(chapas=[_quad("chapa soldada", [0, 0, -L_ / 2], U, V, 120, L_, t)], cantoneiras=[], parafusos=[],
                        recortes=[], soldas=[dict(tipo="filete", perna=b, pontos=[[0, y, -L_ / 2], [0, y, L_ / 2]])
                                             for y in (-t / 2, t / 2)])
    except (KeyError, TypeError, ValueError, ZeroDivisionError):
        return None
    return None


# --- otimizador: sobe parafusos (quantidade, diâmetro), espessura da chapa e perna da solda até passar e depois
#     desce cada um enquanto passar (mínimo de material local). Disposição máxima reprovada (perna máxima, espaçamento
#     e borda máximos, b/t) também abre a descida do campo da dica `parametro` (sem dica: de qualquer campo).
#     ponytail: busca gulosa (um degrau por vez), não global.
_D_MM = (12.7, 16, 19.05, 20, 22.225, 24, 25.4, 27, 28.575, 30, 31.75)
_T_MM = (4.75, 6.3, 8, 9.5, 12.5, 16, 19, 22.4, 25, 31.5, 37.5, 44.5, 50)
_B_MM = (3, 4, 5, 6, 8, 10, 12, 14, 16)
_CONTAGEM = {"n", "n_linhas", "n_linhas_extras", "n_por_linha", "n_colunas", "fileiras", "por_fileira", "n_parafusos",
             "n_alma", "n_linhas_alma", "n_colunas_alma", "n_por_linha_mesa"}
_ESPESSURA = {"t", "t_chapa", "t_gusset", "t_placa", "esp", "espessura", "t_assento", "t_enrijecedor", "tp", "t_mesa",
              "t_alma", "t_tala_mesa", "t_tala_alma", "t_orelha"}
_PERNA = {"b_solda", "perna", "perna_solda", "b_mesa", "b_alma", "b_solda_mesa", "b_solda_pilar", "b_solda_alma"}


def botoes_otimizar(tipo, campos=None):
    """[(campo, papel, valores na unidade do campo)]; papel = ordem de material: 0 solda, 1 chapa, 2 diâmetro, 3 quantidade."""
    out = []
    for c in campos or esquema(tipo):
        nome, un = c["nome"], c.get("unidade") or ""
        base = re.sub(r"_\d$", "", nome)   # nó de treliça: n_linhas_2 → n_linhas
        if c.get("opcoes") or isinstance(c.get("padrao"), bool):
            continue
        k = MM if un == "m" else 1.0
        lo = -math.inf if c.get("min") is None else float(c["min"])
        hi = math.inf if c.get("max") is None else float(c["max"])
        if base in _CONTAGEM:
            papel, lista = 3, [float(i) for i in range(max(1, int(lo) if math.isfinite(lo) else 1), int(min(hi, 30)) + 1)]
        elif base in ("d", "d_mm", "db", "diametro", "diametro_mm", "d_alma_mm", "d_mesa_mm"):
            papel, lista = 2, list(_D_MM)
        elif base in _ESPESSURA:
            papel, lista = 1, [x * k for x in _T_MM]
        elif base in _PERNA or (tipo == "solda_filete" and nome == "b"):
            papel, lista = 0, [x * k for x in _B_MM]
        else:
            continue
        lista = [x for x in lista if lo - 1e-12 <= x <= hi + 1e-12]
        if lista:
            out.append((nome, papel, lista))
    return out


def _nota(r):
    t = [x.get("taxa", 0.0) for x in r.get("verificacoes", [])]
    return max((x if math.isfinite(x) else 1e9 for x in t), default=0.0)


def _descer(r, nomes):
    """Campos que podem descer um degrau: os `parametro` das disposições máximas (`sentido` "desce") reprovadas;
    máximo reprovado sem dica (ou com campo que o otimizador não mexe) libera todos."""
    out = set()
    for x in r.get("verificacoes", []):
        if x.get("sentido") == "desce" and x.get("taxa", 0.0) > 1 + 1e-9:
            if x.get("parametro") not in nomes:
                return set(nomes)
            out.add(x["parametro"])
    return out


_CHAPA_MAIOR = 10.0   # mm, degrau das dimensões de chapa (papel 4, último recurso)
_ESPAC = ("s", "s_mesa", "s_alma", "sh_alma")


def _membro(dados):
    """membro de ligacoes_limites a partir dos perfis em `dados` (mesma leitura de pontas_de)."""
    d = dados or {}
    return dict(perfil=d.get("perfil_viga") or d.get("perfil_pilar"),
                perfil_apoio=d.get("perfil_suporte") or (d.get("perfil_pilar") if d.get("perfil_viga") else None))


def _construtivas(r):
    """Nomes das disposições construtivas (com `sentido`) reprovadas."""
    return {x["nome"] for x in r.get("verificacoes", []) if x.get("sentido") and x.get("taxa", 0.0) > 1 + 1e-9}


def _fmt(v):
    return f"{v:g}" if isinstance(v, (int, float)) and not isinstance(v, bool) else str(v)


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _t_min(d):
    """Espessura (mm) da chapa mais fina entre os campos de espessura preenchidos (limites de 6.3.12); 0 = não sei."""
    ts = [float(v) for k, v in d.items() if k in _ESPESSURA and _num(v) and v > 0]
    return min(ts) if ts else 0.0


def _botoes_limitados(tipo, campos, lim, cur):
    """botoes_otimizar cortados pela faixa de `limites` (passos comerciais quando houver) + chapa maior (papel 4):
    dimensões de chapa com máximo nos limites, em degraus de 10 mm (só se o valor atual não é o automático 0)."""
    out = []
    for nome, papel, lista in botoes_otimizar(tipo, campos):
        f = lim.get(nome)
        if f:
            if f["passos"]:
                lista = [x for x in f["passos"] if lista[0] - 1e-9 <= x <= lista[-1] + 1e-9]
            lista = [x for x in lista if x >= f["min"] - 1e-9 and (f["max"] is None or x <= f["max"] + 1e-9)]
        if lista:
            out.append((nome, papel, lista))
    feitos = {b[0] for b in out}
    for nome, f in lim.items():
        v = cur.get(nome)
        base = re.sub(r"_\d$", "", nome)
        if (nome not in feitos and f["max"] is not None and not f["passos"] and f["unidade"] == "mm" and _num(v)
                and v > 0 and base not in _ESPAC and not base.startswith("e")):
            n = int((f["max"] - f["min"]) // _CHAPA_MAIOR)
            out.append((nome, 4, sorted({f["min"] + i * _CHAPA_MAIOR for i in range(n + 1)} | {f["max"]})))
    return out


_ENR_PILAR = (dict(t_enr_pilar=12.5), dict(t_enr_pilar=16.0, t_reforco=9.5), dict(t_enr_pilar=19.0, t_reforco=16.0))


def otimizar(tipo, dados, esforcos, max_passos=60, membro=None, _sem_pilar=False):
    """Busca LIMITADA (docs/v3/LIGACOES-OTIMIZADOR.md): só valores dentro de ligacoes_limites.limites; degraus na
    ordem de material espessura → diâmetro → nº de parafusos → chapa maior (empate da pior taxa dentro de 2 %).
    Disposição construtiva é restrição: candidato que reprova uma que o atual não reprova tenta os espaçamentos no
    mínimo (3d) e, se continuar reprovando, é descartado; "geometria impossível" (ValueError) também descarta.
    Depois de passar, desce cada item enquanto continuar passando. Ainda sem solução e com pilar: tenta os
    enrijecedores do pilar (_ENR_PILAR, t ≥ tf da viga). Sem solução: dados como vieram, ok = False e
    `motivo` com a verificação limitante e o limite atingido.
    `membro` (opcional): perfis para os limites; padrão = perfis de `dados`.
    → {ok, params, motivo, verificacao_limitante, limite_atingido, dados, resultado, passos, mudancas}"""
    from .ligacoes_limites import limites
    campos = esquema(tipo)
    cur = {c["nome"]: c["padrao"] for c in campos if c["padrao"] is not None}
    cur.update(dict(dados or {}))
    original = dict(cur)
    membro = membro or _membro(cur)

    def lim(d):
        return limites(tipo, membro, dict(d, t=_t_min(d)))

    def rodar(d):
        try:
            return verificar_ligacao(tipo, d, esforcos)
        except (ValueError, KeyError, TypeError, ZeroDivisionError):
            return None

    def falha(motivo, limitante=None, atingido=None):
        return dict(ok=False, params={}, motivo=motivo, verificacao_limitante=limitante, limite_atingido=atingido or [],
                    dados=original, resultado=rodar(original), passos=[], mudancas={})

    L0 = lim(cur)
    for k, f in L0.items():   # faixa vazia (ex.: apoio mais estreito que a chapa mínima): para antes de buscar
        if f["max"] is not None and f["min"] > f["max"] + 1e-9:
            return falha(f"sem solução dentro dos limites: {k} mínimo {round(f['min'], 4):g} {f['unidade']} > máximo "
                         f"{round(f['max'], 4):g} {f['unidade']} (limite do apoio/barra) — nada alterado", None, [k])
    botoes = _botoes_limitados(tipo, campos, L0, cur)
    nomes_b = {b[0] for b in botoes}
    espac = [k for k in L0 if re.sub(r"_\d$", "", k) in _ESPAC and k not in nomes_b]

    def vizinho(nome, lista, sobe):
        v = cur.get(nome)
        if v is None:
            return lista[0]
        cand = [x for x in lista if (x > float(v) + 1e-9 if sobe else x < float(v) - 1e-9)]
        return (min(cand) if sobe else max(cand)) if cand else None

    def valor(nome, x):
        return int(round(x)) if re.sub(r"_\d$", "", nome) in _CONTAGEM else x

    def ajustar(cand):
        """Espaçamentos/bordas que o otimizador não move presos à faixa do candidato (3d sobe com o diâmetro)."""
        for k, f in lim(cand).items():
            v = cand.get(k)
            if k in nomes_b or not _num(v) or not v:
                continue
            x = max(v, f["min"]) if f["max"] is None else min(max(v, f["min"]), f["max"])
            if abs(x - v) > 1e-9:
                cand[k] = x
        return cand

    for nome, _p, lista in botoes:   # valor fora da lista (ex.: t = 20 mm não comercial) → degrau acima, dentro da faixa
        v = cur.get(nome)
        if _num(v) and not any(abs(float(v) - x) < 1e-9 for x in lista):
            cur[nome] = valor(nome, next((x for x in lista if x > v), lista[-1]))
    cur = ajustar(cur)
    r = rodar(cur)
    if r is None:
        try:
            verificar_ligacao(tipo, cur, esforcos)
        except (ValueError, KeyError, TypeError, ZeroDivisionError) as e:
            return falha(f"dados iniciais inválidos — {e.args[0] if e.args else e} — nada alterado")
    proibidas = _construtivas(r)

    def avaliar(cand):
        rc = rodar(ajustar(cand))
        if rc is not None and not (_construtivas(rc) <= proibidas) and espac:   # restrição: compacta o espaçamento
            for k in espac:
                cand[k] = lim(cand)[k]["min"]
            rc = rodar(ajustar(cand))
        return rc if rc is not None and _construtivas(rc) <= proibidas else None

    passos, barrados = [], []
    for _ in range(max_passos):
        if r["ok"]:
            break
        melhor, barrados = None, []
        # um degrau num item ou em dois juntos (ex.: chapa mais grossa só passa com solda maior, 5t/8)
        desce = _descer(r, nomes_b)
        um = [(i, True) for i in range(len(botoes))] + [(i, False) for i, b in enumerate(botoes) if b[0] in desce]
        movs = [(a,) for a in um] + [(a, b) for k, a in enumerate(um) for b in um[k + 1:] if a[0] != b[0]]
        for mov in movs:
            cand, ok_mov = dict(cur), True
            for i, sobe in mov:
                nome, papel, lista = botoes[i]
                x = vizinho(nome, lista, sobe)
                if x is None:
                    ok_mov = False
                    break
                cand[nome] = valor(nome, x)
            if not ok_mov:
                continue
            rc = avaliar(cand)
            n3 = [botoes[i][0] for i, sobe in mov if botoes[i][1] == 3 and sobe]
            if rc is None and n3 and _num(cur.get("h")) and _num(cur.get("s")):
                # ponytail: mais um parafuso numa chapa de altura h (tipos antigos) só cabe se a chapa crescer s
                cand["h"] = cur["h"] + cur["s"] * (cand[n3[0]] - (cur.get(n3[0]) or 0))
                rc = avaliar(cand)
            if rc is None:
                if len(mov) == 1:
                    barrados.append(botoes[mov[0][0]][0])
                continue
            chave = (not rc["ok"], round(_nota(rc) / 0.02), len(mov), sum(botoes[i][1] * (1 if s_ else -1) for i, s_ in mov),
                     _nota(rc))
            if melhor is None or chave < melhor[0]:
                melhor = (chave, [botoes[i][0] for i, _ in mov], rc, cand)
        if melhor is None or (not melhor[2]["ok"] and _nota(melhor[2]) >= _nota(r) - 1e-9):
            break   # nenhum degrau dentro dos limites ajuda
        _, nomes, r, cand = melhor
        for nome in nomes:
            passos.append(dict(campo=nome, de=cur.get(nome), para=cand[nome], taxa=_nota(r)))
        cur = cand
        proibidas &= _construtivas(r)   # restrição atendida não volta a ser violada
    if not r["ok"]:
        if not _sem_pilar and any(c["nome"] == "t_enr_pilar" for c in campos) and cur.get("perfil_pilar") \
                and not original.get("t_enr_pilar"):
            # último degrau: enrijecedores de continuidade (+ reforço da alma), como em ligacoes_rigidas._buscar
            from .ligacoes_limites import T_COMERCIAL, _dim
            vg = _dim(cur.get("perfil_viga"))
            tfb = vg["tf"] / MM if vg else 0.0   # t do enrijecedor ≥ tf da viga (comercial)
            for pp in _ENR_PILAR:
                pp = dict(pp, t_enr_pilar=next((t for t in T_COMERCIAL if t >= max(pp["t_enr_pilar"], tfb) - 1e-9), 50.0))
                r2 = otimizar(tipo, {**original, **pp}, esforcos, max_passos, membro, _sem_pilar=True)
                if r2["ok"]:
                    r2["mudancas"] = {k: [original.get(k), v] for k, v in r2["dados"].items() if original.get(k) != v}
                    r2["params"] = {k: v[1] for k, v in r2["mudancas"].items()}
                    return r2
        r_or = rodar(original) or r   # motivo pela verificação que governa os dados ORIGINAIS (= resultado devolvido)
        ruins = [x for x in r_or.get("verificacoes", []) if x.get("taxa", 0.0) > 1 + 1e-9] or r_or.get("verificacoes", [])
        pior = max(ruins, key=lambda x: x["taxa"] if math.isfinite(x["taxa"]) else 1e9, default=None)
        topo = [f"{n} = {_fmt(cur.get(n))} {(lim(cur).get(n) or {}).get('unidade', '')}".rstrip() for n, _p, lista in botoes
                if _num(cur.get(n)) and float(cur[n]) >= lista[-1] - 1e-9]
        topo += [f"{n}: próximo degrau viola disposição construtiva ou geometria" for n in dict.fromkeys(barrados)
                 if not any(t.startswith(n + " =") for t in topo)]
        nome = pior["nome"] if pior else "?"
        return falha(f"sem solução dentro dos limites: '{nome}' governa com taxa {_nota(r_or):.2f} nos dados atuais; "
                     f"a busca chegou a {_nota(r):.2f}"
                     + (f"; no limite: {', '.join(topo)}" if topo else "; nenhum degrau dentro dos limites reduz a taxa")
                     + " — nada alterado", nome, topo)
    mudou = True   # descida: menos material enquanto passa (do item mais caro para o mais barato)
    while mudou:
        mudou = False
        for nome, papel, lista in sorted(botoes, key=lambda b: -b[1]):
            x = vizinho(nome, lista, False)
            if x is None:
                continue
            cand = ajustar({**cur, nome: valor(nome, x)})
            rc = rodar(cand)
            if rc is not None and rc["ok"] and rc["verificacoes"] != r["verificacoes"]:
                passos.append(dict(campo=nome, de=cur.get(nome), para=valor(nome, x), taxa=_nota(rc)))
                cur, r, mudou = cand, rc, True
    mud = {k: [original.get(k), v] for k, v in cur.items() if original.get(k) != v}
    return dict(ok=True, params={k: v[1] for k, v in mud.items()}, motivo=None, verificacao_limitante=None,
                limite_atingido=[], dados=cur, resultado=r, passos=passos, mudancas=mud)


# --- memorial: seção HTML com as verificações (expressão pelo item da norma, valores e taxa)
_FORMULAS = [
    ("6.3.3.4", "(F<sub>t,Sd</sub>/F<sub>t,Rd</sub>)² + (F<sub>v,Sd</sub>/F<sub>v,Rd</sub>)² ≤ 1,0"),
    ("6.3.3.1", "F<sub>t,Rd</sub> = 0,75·A<sub>b</sub>·f<sub>ub</sub> / γ<sub>a2</sub>"),
    ("6.3.3.2", "F<sub>v,Rd</sub> = 0,4·A<sub>b</sub>·f<sub>ub</sub> / γ<sub>a2</sub> por plano (0,5 com rosca fora do plano)"),
    ("6.3.3.3", "F<sub>c,Rd</sub> = 1,2·l<sub>f</sub>·t·f<sub>u</sub> / γ<sub>a2</sub> ≤ 2,4·d<sub>b</sub>·t·f<sub>u</sub> / γ<sub>a2</sub>"),
    ("6.3.9", "s ≥ 2,7·d<sub>b</sub> (preferível 3·d<sub>b</sub>)"),
    ("6.3.10", "s ≤ 24·t ≤ 300 mm"),
    ("6.3.12", "e ≤ 12·t ≤ 150 mm"),
    ("tabela 14", "e ≥ e<sub>mín</sub> (tabela 14)"),
    ("6.2.6.2.1", "b ≥ b<sub>mín</sub> (tabela 10, chapa mais fina)"),
    ("6.2.6.2.2", "b ≤ t − 1,5 mm (t ≥ 6,35 mm)"),
    ("6.2.6.2.3", "L ≥ 4·b e L ≥ 40 mm"),
    ("6.2.5", "F<sub>w,Rd</sub> = 0,6·A<sub>w</sub>·f<sub>w</sub> / γ<sub>w2</sub> (A<sub>w</sub> = 0,707·b); metal base 0,6·b·f<sub>y</sub> / γ<sub>a1</sub>"),
    ("6.5.6", "F<sub>r,Rd</sub> = (0,6·f<sub>u</sub>·A<sub>nv</sub> + C<sub>ts</sub>·f<sub>u</sub>·A<sub>nt</sub>) / γ<sub>a2</sub> ≤ "
              "(0,6·f<sub>y</sub>·A<sub>gv</sub> + C<sub>ts</sub>·f<sub>u</sub>·A<sub>nt</sub>) / γ<sub>a2</sub>"),
    ("6.5.5", "escoamento 0,6·f<sub>y</sub>·A<sub>g</sub> / γ<sub>a1</sub>; ruptura 0,6·f<sub>u</sub>·A<sub>nv</sub> / γ<sub>a2</sub>"),
    ("5.2", "N<sub>t,Rd</sub> = A<sub>g</sub>·f<sub>y</sub> / γ<sub>a1</sub>; A<sub>e</sub>·f<sub>u</sub> / γ<sub>a2</sub> (A<sub>e</sub> ≤ 0,85·A<sub>g</sub>)"),
    ("5.4", "M<sub>Rd</sub> = W·f<sub>y</sub> / γ<sub>a1</sub> (elástico)"),
]


def formula(item):
    for chave, f in _FORMULAS:
        if chave in (item or ""):
            return f
    return "S<sub>d</sub> ≤ R<sub>d</sub>"


def memorial_html(tipo, dados, esforcos, resultado=None, rotulos=None):
    """Seção <section class="memorial-ligacao"> (sem <html>): veredito, esforços, dados, verificações com expressão,
    solicitante, resistente e taxa, avisos."""
    from html import escape as h
    r = resultado or verificar_ligacao(tipo, dados, esforcos)
    nome = (META.get(tipo) or registro(tipo) or {}).get("nome", tipo)
    rot, un = rotulos or {}, {c["nome"]: c["unidade"] for c in esquema(tipo)}

    def num(x, c=2):
        if isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x):
            return "—"
        return f"{x:,.{c}f}".replace(",", " ").replace(".", ",")

    def val(k, v):
        if isinstance(v, bool):
            return "sim" if v else "não"
        if isinstance(v, (int, float)):
            u = un.get(k, "")
            return (num(v / MM, 1) + " mm") if u == "m" else (num(v, 1 if u == "mm" else 2) + ("" if u in ("", "-") else " " + u))
        return h(str(v))

    def mm(x, u):
        return x / MM if u == "m" else x

    vs = sorted(r.get("verificacoes", []), key=lambda x: -x.get("taxa", 0.0))
    passa = (lambda x: x.get("taxa", 0.0) <= 1 + 1e-9 and x.get("ok") is not False)
    linhas = "".join(
        f"<tr class=\"{'' if passa(x) else 'falha'}\"><td>{h(x['nome'])}</td><td>{h(x.get('item', ''))}</td>"
        f"<td class=\"f\">{formula(x.get('item'))}</td><td class=\"n\">{num(mm(x['solicitante'], x.get('unidade')))}</td>"
        f"<td class=\"n\">{num(mm(x['resistente'], x.get('unidade')))}</td>"
        f"<td>{h('mm' if x.get('unidade') == 'm' else x.get('unidade', ''))}</td>"
        f"<td class=\"n\">{num(x.get('taxa', 0.0) * 100, 0)} %</td><td>{'✓' if passa(x) else '✕'}</td></tr>" for x in vs)
    entradas = "".join(f"<tr><td>{h(rot.get(k, k))}</td><td class=\"n\">{val(k, v)}</td></tr>" for k, v in (dados or {}).items())
    esf = ", ".join(f"{h(k)} = {num(float(v))} {'kN·m' if k.startswith('M') or k == 'T' else 'kN'}"
                    for k, v in (esforcos or {}).items() if isinstance(v, (int, float)) and not isinstance(v, bool))
    avisos = "".join(f"<li>{h(a)}</li>" for a in r.get("avisos", []))
    ok, taxa, pior = bool(r.get("ok")), r.get("taxa", r.get("taxa_max", 0.0)), (vs[0] if vs else None)
    return (f"<section class=\"memorial-ligacao\"><h2>Ligação — {h(nome)}</h2>"
            f"<p class=\"veredito {'ok' if ok else 'falha'}\">{'PASSA' if ok else 'NÃO PASSA'} · taxa máxima {num(taxa * 100, 0)} %"
            + (f" em {h(pior['nome'])} ({h(pior.get('item', ''))})" if pior else "") + "</p>"
            f"<p>Esforços de cálculo (ELU): {esf or '—'}. Norma: {EDICAO}.</p>"
            f"<h3>Dados</h3><table class=\"dados\">{entradas}</table>"
            "<h3>Verificações</h3><table class=\"verif\"><thead><tr><th>Verificação</th><th>Item</th><th>Expressão</th>"
            f"<th>Solicitante</th><th>Resistente</th><th>Un.</th><th>Taxa</th><th></th></tr></thead><tbody>{linhas}</tbody></table>"
            + (f"<h3>Avisos</h3><ul>{avisos}</ul>" if avisos else "")
            + "<p class=\"nota\">Resultado do motor; exige conferência do engenheiro responsável (ART).</p></section>")
