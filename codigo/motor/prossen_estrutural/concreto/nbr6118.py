"""Concreto armado segundo a NBR 6118:2023 (vigas, pilares, lajes maciças).

Unidades de entrada: dimensões em m, forças em kN, momentos em kN·m, fck/fyk em MPa.
Internamente trabalha em kN e cm (tensões em kN/cm²). Saídas de armadura em cm²
(ou cm²/m). Fórmulas escritas a partir da norma e da literatura (Carvalho &
Figueiredo; Pinheiro/USP; Bastos/UNESP) — nada copiado do CYPE.

Edição (ESPEC §2): o texto da ABNT não é público; cada item foi confirmado na 2023 por fonte
pública citável (ver COBERTURA). Fontes:
  [B1] Bastos, Fundamentos do Concreto Armado, UNESP, set/2023 (segue a NBR 6118:2023):
       8.2.5 fctm (C55–C90 alterado), 8.2.8 Eci/Ecs, 8.2.10.1 ηc, 7.4 Tabelas 7.1 e 7.2.
  [B2] Bastos, Flexão Normal Simples — Vigas, UNESP, out/2023: 17.2.2 e) σcd = αc·ηc·fcd,
       λ e αc inalterados, 14.6.4.3 x/d ≤ 0,45/0,35, Tabela 17.3 (inalterada),
       p. 8–9: 17.3.5.2.1 (Md,mín) e 17.3.5.2.3 (pele).
  [B3] Bastos, Dimensionamento de vigas à força cortante, UNESP (NBR 6118:2023): 17.4 modelo I/II, p. 34–35: 18.3.3.2.
  [B4] Bastos, Torção em vigas de concreto armado, UNESP, mai/2024: 17.5.1, 17.7, p. 27–28: 18.3.4.
  [B5] Bastos, Pilares de concreto armado, UNESP, jun/2024: 11.3.3.4.3, 15.8, 17.3.5.3, p. 42: 15.6, p. 74: 13.2.3.
  [B6] Bastos, Vigas de concreto armado, UNESP, abr/2024: Tabelas 13.3 (com nota 5) e 13.4, 17.3.2, 17.3.3.
  [S1] Souza & Santos, Contribuciones a las Ciencias Sociales v.17 n.8 (2024), p. 8–9,
       doi 10.55905/revconv.17n.8-260: ηc = (40/fck)^(1/3) e deformações εc2/εcu inalteradas.
  [U1] TCC UFU (repositorio.ufu.br/handle/123456789/38372): síntese das alterações 2014→2023
       (η1 por categoria do aço, 15.8.2 αb = 1 em balanço, 17.4.2.2 c decalagem, Tabela 13.4 reestruturada).
  [P1] Cruz et al., Rev. Principia v.62 e7959 (2025), p. 7: 15.5.3 γz na NBR 6118:2023 (≥ 4 andares).
"""
from __future__ import annotations

import math

from scipy.optimize import brentq

from ..materiais import props_concreto
from ..modelo import MPa

EDICAO = "NBR 6118:2023 (itens confirmados; demais 2014 — ver COBERTURA)"

GAMA_C, GAMA_S = 1.4, 1.15
ES = 21000.0  # kN/cm² (210 GPa) — NBR 6118 8.3.5
BITOLAS = (5.0, 6.3, 8.0, 10.0, 12.5, 16.0, 20.0, 25.0, 32.0)  # mm


def _cob(item, verificacao, limitacao="", edicao="2023"):
    return dict(norma="NBR 6118", edicao=edicao, item=item, verificacao=verificacao, limitacao=limitacao)


COBERTURA = [
    _cob("8.2.5, 8.2.8, 8.2.10.1", "fctm, Eci, Ecs, diagrama parábola-retângulo com ηc",
         "agregado granito (αE = 1) no módulo concreto/; usar materiais.concreto(fck, alfa_E) para outros"),
    _cob("7.4.2 (Tabela 7.1)", "classe mínima do concreto armado pela CAA"),
    _cob("7.4.7.2 (Tabela 7.2)", "cobrimento nominal de viga/pilar pela CAA",
         "Δc = 10 mm; redução de 5 mm (7.4.7.6) não aplicada"),
    _cob("17.2.2, 14.6.4.3", "flexão simples (diagrama retangular, σcd = αc·ηc·fcd), x/d limite, armadura dupla",
         "seção T só com mesa comprimida"),
    _cob("17.2.2", "viga em flexo-tração para qualquer N de tração: pequena excentricidade (tração nas duas "
         "faces) e grande excentricidade (As = As(Ms) + N/fyd, Ms = M − N·(d − h/2))",
         "viga com compressão |N| ≤ 0,1·fcd·Ac: N desprezado na flexão (critério adotado, sem item na norma; "
         "a favor da segurança no As); acima disso, flexo-compressão simétrica"),
    _cob("17.2.2, 8.2.10.1, 17.2.5.2", "flexo-compressão de pilar (parábola-retângulo) e oblíqua aproximada",
         "armadura simétrica nas faces; oblíqua pela envoltória com α = 1,2"),
    _cob("17.3.5.2.1, 17.3.5.2.4", "As,mín pelo Md,mín = 0,8·W0·fctk,sup (parábola-retângulo) com ρ ≥ 0,15%, "
         "e As,máx = 4% Ac em vigas",
         "conferido na 2023 em [B2] p. 8 (Md,mín, fctk,sup = 1,3·fctm, ρ ≥ 0,15%); a Tabela 17.3 (alternativa) "
         "fica só na laje"),
    _cob("17.3.5.2.3", "armadura de pele com h > 60 cm: 0,10%·Ac,alma por face (≤ 5 cm²/m), s ≤ 20 cm, barras "
         "sugeridas", "conferido na 2023 em [B2] p. 9 (s ≤ 20 cm, ≤ 5 cm²/m); s ≤ d/3 também (texto de 2003, usado "
         "pelo CYPE; a favor)"),
    _cob("18.3.3.2", "estribos de viga: s,máx (0,6d ≤ 300 / 0,3d ≤ 200 mm por Vd/VRd2 ≥ 0,67), st,máx "
         "(d ≤ 800 / 0,6d ≤ 350 mm por Vd/VRd2 > 0,20), nº de ramos, 5 mm ≤ φt ≤ bw/10",
         "conferido na 2023 em [B3] p. 34–35; φt suposto 5 mm; bitola e espaçamento efetivos do estribo não escolhidos"),
    _cob("13.2.3 (Tabela 13.1), 14.4.2.4", "dimensões mínimas do pilar: b ≥ 14 cm, Ac ≥ 360 cm², γn = 1,95 − 0,05b "
         "sobre os esforços com b < 19 cm; h/b > 5 (pilar-parede) reprova",
         "13.2.3 conferido na 2023 em [B5] p. 74; pilar-parede (15.9, 18.5) fora do escopo"),
    _cob("13.2.2", "largura mínima de viga bw ≥ 12 cm (10 cm excepcional, aviso)",
         "texto da 2023 não localizado em fonte pública (o item não consta das alterações listadas no guia AltoQi "
         "NBR 6118:2023)", edicao="2014"),
    _cob("17.4.2.2, 17.4.2.3, 17.4.1.1.1", "cortante: VRd2, Vc, Asw/s (modelo I na verificação), ρsw,mín",
         "decalagem al (17.4.2.2 c, alterada na 2023) não calculada — é de detalhamento; Vc = 0 na tração "
         "com LN fora da seção (pequena excentricidade, qualquer tração); acréscimo de Vc na flexo-compressão não aplicado "
         "(a favor da segurança)"),
    _cob("17.5.1.4.1–17.5.1.6, 17.5.1.2, 17.7.2, 18.3.4", "torção: TRd2, A90/s, Asl/ue, mínimos, interação com "
         "cortante; nº mínimo de barras da Asl (uma por canto, ≤ 350 mm ao longo de ue — 18.3.4, [B4] p. 27–28)",
         "toda torção tratada como de equilíbrio; banzo comprimido (17.7.1.4) não verificado; "
         "Asl de torção informada à parte (somar na zona tracionada, 17.7.1.2); estribo de torção pelo critério de "
         "18.3.3.2 (φt suposto 5 mm)"),
    _cob("17.3.3.2, Tabela 13.4, Tabela 8.2", "abertura de fissuras wk (estádio II, αe = 15), combinação frequente, "
         "Acri por barra (pior entre canto e interna)",
         "2 camadas: só a inferior, sem limite pela camada de cima, e σs no centroide d (não por barra)"),
    _cob("17.3.2.1.1, 17.3.2.1.2, 17.3.1", "flecha: Branson + fluência αf (combinação quase permanente); flecha "
         "ativa (total a prazo infinito − ocorrida até a parede/forro) com params t_alvenaria_meses e "
         "fracao_qp_pos_alvenaria (padrão 1), mais o total contra L/250",
         "Ieq na seção de momento máximo (sem ponderação de vãos contínuos); ρ' = 0 e t0 = 1 mês (params "
         "t0_meses) a favor da segurança; sem t_alvenaria_meses o limite de alvenaria/forro usa a flecha total"),
    _cob("13.3 (Tabela 13.3)", "deslocamentos-limite",
         "subconjunto: aceitabilidade, apos_construcao (forros colados), alvenaria (L/500 e 10 mm; "
         "rotação 0,0017 rad não), balanco, horizontal_topo, entre_pisos"),
    _cob("15.6", "comprimento equivalente le = menor entre l0 + h e l (params l0_x/l0_y; le_x/le_y prevalecem)",
         "conferido na 2023 em [B5] p. 42; sem l0, le = L (limite superior de 15.6); o CYPE 2016 usa o maior "
         "dos dois (LC-018)"),
    _cob("11.3.3.4.3, 15.8.2, 15.8.3.3.2, 15.8.3.3.3", "pilar: M1d,mín, λ1, 2ª ordem local (curvatura; κ na função)",
         "αb por params, número ou par (αbx, αby), 0,4 ≤ αb ≤ 1,0, e αb = 1 quando M1d < M1d,mín "
         "(padrão 1,0, a favor da segurança); λ > 90 fora do escopo (aviso e taxa > 1); "
         "fluência 15.8.4 não considerada"),
    _cob("18.4.2.1, 18.4.2.2", "barras do pilar: n·π·φl²/4 ≥ As, 10 mm ≤ φl ≤ b/8, espaçamento livre mínimo; "
         "sem bitola que atenda acrescenta barras e refaz a flexão; sem disposição possível reprova",
         "não conferido na 2023; espaçamento máximo mín(2b, 400 mm) garantido pela disposição mínima; "
         "d' supõe φl = 20 mm",
         edicao="2014"),
    _cob("17.3.5.3.1, 17.3.5.3.2", "As,mín = 0,15·Nd/fyd ≥ 0,4% Ac; As,máx = 4% Ac fora da emenda por traspasse "
         "(padrão; 8% com a sobreposição) ou 8% com params emenda = 'luva'",
         "emendas defasadas não modeladas (tratadas como traspasse na mesma seção)"),
    _cob("15.5.3, 15.7.2", "γz, classificação e majoração 0,95·γz", "válido para ≥ 4 andares (aviso abaixo disso)"),
    _cob("19.3.3.2, Tabela 19.1", "laje maciça (Marcus) e armadura mínima de laje",
         "não conferida na 2023; só bordos apoiados", edicao="2014"),
]


# ---------------------------------------------------------------- parâmetros
def parametros(fck):
    """Parâmetros do concreto (fck em MPa). Tensões retornadas em kN/cm². fck fora de 20..90 → ValueError."""
    pc = props_concreto(fck)  # 8.2.5 e 8.2.8 (valida a faixa C20–C90)
    eta_c = 1.0 if fck <= 40 else (40 / fck) ** (1 / 3)  # 8.2.10.1 (2023): coeficiente de fragilidade
    if fck <= 50:  # NBR 6118 17.2.2 e 8.2.10.1
        lam, alfa_c, ecu, ec2, n = 0.8, 0.85, 3.5e-3, 2.0e-3, 2.0
        xd_lim = 0.45  # 14.6.4.3
    else:
        lam = 0.8 - (fck - 50) / 400
        alfa_c = 0.85 * (1 - (fck - 50) / 200)
        ecu = (2.6 + 35 * ((90 - fck) / 100) ** 4) * 1e-3
        ec2 = (2.0 + 0.085 * (fck - 50) ** 0.53) * 1e-3
        n = 1.4 + 23.4 * ((90 - fck) / 100) ** 4
        xd_lim = 0.35
    return dict(fcd=fck / GAMA_C / 10, lam=lam, alfa_c=alfa_c, eta_c=eta_c, ecu=ecu, ec2=ec2, n=n,
                fctm=pc["fctm"] / 10, fctd=0.7 * pc["fctm"] / GAMA_C / 10, xd_lim=xd_lim,
                Eci=pc["Eci"] / 10, Ecs=pc["Ecs"] / 10)


# NBR 6118 Tabela 17.3 — ρmin (%) seção retangular, C20..C90 de 5 em 5
_RHO_MIN = (0.150, 0.150, 0.150, 0.164, 0.179, 0.194, 0.208, 0.211,
            0.219, 0.226, 0.233, 0.239, 0.245, 0.251, 0.256)


def rho_min(fck):
    """Tabela 17.3 (alternativa de 17.3.5.2.1; ainda usada pela laje, Tabela 19.1)."""
    i = min(max(int(round((fck - 20) / 5)), 0), len(_RHO_MIN) - 1)
    return _RHO_MIN[i] / 100


def as_min_flexao(b, h, d, fck, fyk=500, W0=None, Ac=None):
    """As,mín de flexão (cm²) — NBR 6118 17.3.5.2.1: dimensionamento para Md,mín = 0,8·W0·fctk,sup
    (fctk,sup = 1,3·fctm, 8.2.5), respeitada a taxa absoluta de 0,15%. b = largura comprimida (m), W0 (m³)
    em relação à fibra tracionada (padrão b·h²/6), Ac (cm², padrão b·h). A Tabela 17.3 supõe d/h = 0,8 e
    CA-50: com d/h < 0,8 dá menos que isto (LC-001). As pela parábola-retângulo (8.2.10.1), como o CYPE."""
    p = parametros(fck)
    W0 = b * h ** 2 / 6 if W0 is None else W0
    Ac = b * h * 1e4 if Ac is None else Ac
    fctk_sup = 1.3 * p["fctm"] * 10  # MPa
    Md = 0.8 * W0 * fctk_sup * 1e3  # kN·m
    fyd = fyk / GAMA_S / 10
    bc, hc, ys = b * 100, h * 100, [d * 100]
    g = lambda As: _MRd(0.0, bc, hc, ys, As, p, fyd) - Md * 100
    As = brentq(g, 1e-9, 4 * Md * 100 / (fyd * d * 100), xtol=1e-12)  # z ≥ d/4 na raiz
    return dict(Md_min=Md, fctk_sup_MPa=fctk_sup, W0_m3=W0, As_calc=As, As_abs=0.0015 * Ac,
                As_min=max(As, 0.0015 * Ac), item="17.3.5.2.1 (Md,mín = 0,8·W0·fctk,sup; ρ ≥ 0,15%)")


def _sigma_s(eps, fyd):
    return max(-fyd, min(fyd, ES * eps))


# ---------------------------------------------------------------- barras
def escolher_barras(As, bw, cobrimento=0.03, phi_t=5.0, dmax=19.0, min_barras=2, fyk=500):
    """Escolhe bitola/quantidade de menor excesso de área que caiba em 1 camada
    (espaçamento livre ah >= max(20 mm, φ, 1,2·dmax) — NBR 6118 18.3.2.2).
    Se nada couber em 1 camada, aceita 2 camadas. bw, cobrimento em m. fyk = 600 (CA-60, fio da
    NBR 7480): só bitolas ≤ 10 mm. ponytail: série única BITOLAS filtrada, sem a série de fios."""
    bitolas = [f for f in BITOLAS if fyk < 600 or f <= 10.0]
    largura = bw * 1000 - 2 * (cobrimento * 1000 + phi_t)  # mm úteis
    melhor = None
    for camadas in (1, 2):
        for phi in bitolas:
            a = math.pi * phi ** 2 / 400  # cm²
            n = max(min_barras, math.ceil(As / a - 1e-9))
            ah = max(20.0, phi, 1.2 * dmax)
            por_camada = math.ceil(n / camadas)
            if por_camada * phi + (por_camada - 1) * ah > largura:
                continue
            cand = dict(n=n, phi=phi, As_ef=n * a, camadas=camadas, ah_min_mm=ah)
            if melhor is None or cand["As_ef"] < melhor["As_ef"] - 1e-9:
                melhor = cand
        if melhor:
            return melhor
    return None


# ---------------------------------------------------------------- flexão
def flexao_simples(b, d, Md, fck, fyk, h=None, bf=None, hf=0.0, d2=None):
    """Flexão simples retangular ou T (b=bw, bf=mesa, hf=espessura da mesa).
    Retorna As (tração), As2 (compressão), x, domínio. NBR 6118 17.2.2 / 14.6.4.3."""
    p = parametros(fck)
    fcd, lam, ac = p["fcd"], p["lam"], p["alfa_c"] * p["eta_c"]  # 17.2.2 e): σcd = αc·ηc·fcd
    fyd = fyk / GAMA_S / 10
    b, d, M = b * 100, d * 100, Md * 100  # cm, kN·cm
    d2 = (d2 * 100) if d2 else 0.1 * d
    bf = bf * 100 if bf else b
    hf = hf * 100
    sc = ac * fcd
    alertas, Mf = [], 0.0
    largura = bf
    # Mesa inteira comprimida basta? (T com λx <= hf comporta-se como retangular de largura bf)
    if bf > b and Md * 100 > sc * bf * hf * (d - hf / 2):
        Mf = sc * (bf - b) * hf * (d - hf / 2)  # parcela das abas
        largura = b
    Mr = M - Mf
    xlim = p["xd_lim"] * d
    Mlim = sc * largura * lam * xlim * (d - lam * xlim / 2)
    As2 = 0.0
    if Mr <= Mlim:
        mu = Mr / (sc * largura * d ** 2)
        x = d / lam * (1 - math.sqrt(1 - 2 * mu))
        As = Mr / (fyd * (d - lam * x / 2))
    else:  # armadura dupla — mantém x = xlim
        x = xlim
        es2 = p["ecu"] * (x - d2) / x
        As2 = (Mr - Mlim) / (_sigma_s(es2, fyd) * (d - d2))
        As = Mlim / (fyd * (d - lam * x / 2)) + (Mr - Mlim) / (fyd * (d - d2))
        alertas.append("Armadura dupla (x/d limite atingido — 14.6.4.3)")
    if Mf:
        As += Mf / (fyd * (d - hf / 2))
    x23 = p["ecu"] / (p["ecu"] + 0.010) * d
    dominio = 2 if x <= x23 else 3
    return dict(As=As, As2=As2, x=x / 100, x_d=x / d, dominio=dominio, Mlim=Mlim / 100, alertas=alertas)


def flexo_tracao(b, h, d, Nd, Md, fck, fyk=500, d2=None):
    """Flexo-tração normal em seção retangular — NBR 6118 17.2.2 (hipóteses do ELU; concreto tracionado
    desprezado). Nd > 0 tração (kN), Md (kN·m, usa |Md|), b, h, d, d2 em m (d2 padrão h − d). As em cm².
    a = d − h/2 (CG → armadura tracionada), a' = h/2 − d2 (CG → armadura oposta), e = |Md|/Nd.
    - Pequena excentricidade (e ≤ a: Nd entre as camadas, seção toda tracionada, domínio 1):
      As = Nd·(a' + e)/((a + a')·fyd) e As_oposta = Nd·(a − e)/((a + a')·fyd), as duas tracionadas.
    - Grande excentricidade (e > a: a LN corta a seção): Ms = |Md| − Nd·a (momento em relação a As) em
      flexão simples; As = As(Ms) + Nd/fyd; As2 = A's(Ms), comprimida.
    Em e = a os dois ramos dão As = Nd/fyd e As_oposta = As2 = 0: sem salto."""
    if Nd <= 0:
        raise ValueError("flexo_tracao: Nd deve ser > 0 (tração)")
    fyd = fyk / GAMA_S / 10
    d2 = h - d if d2 is None else d2
    a, a2, M = d - h / 2, h / 2 - d2, abs(Md)
    if a <= 0 or a2 <= 0:
        raise ValueError(f"flexo_tracao: armaduras fora das faces (d = {d}, d2 = {d2}, h = {h})")
    e = M / Nd
    base = dict(e_m=e, a_m=a, a2_m=a2, item="17.2.2 (flexo-tração)", alertas=[])
    if e <= a:  # 17.2.2: pequena excentricidade — tração total
        z = (a + a2) * 100
        return dict(base, excentricidade="pequena", Ms=0.0, dominio=1, x_d=None, As2=0.0,
                    As=Nd * (a2 + e) * 100 / (z * fyd), As_oposta=Nd * (a - e) * 100 / (z * fyd))
    Ms = M - Nd * a
    fl = flexao_simples(b, d, Ms, fck, fyk, d2=d2)
    return dict(base, excentricidade="grande", Ms=Ms, dominio=fl["dominio"], x_d=fl["x_d"],
                As=fl["As"] + Nd / fyd, As2=fl["As2"], As_oposta=0.0, alertas=fl["alertas"])


# ---------------------------------------------------------------- cisalhamento
def cisalhamento(bw, d, Vd, fck, fywk=500, modelo=1, theta=45.0, N=0.0, Md=0.0, h=None):
    """Estribos verticais (α=90°). Modelo I (17.4.2.2) ou II (17.4.2.3). Asw/s em cm²/m.
    N (kN, > 0 tração), Md (kN·m) e h (m, padrão d) só para 17.4.2.2 c: Vc = 0 em elemento tracionado
    com a LN fora da seção — tração de pequena excentricidade, |Md|/N ≤ d − h/2 (resultante entre as
    camadas de armadura). N = 0 (padrão) = flexão simples."""
    p = parametros(fck)
    tracao_total = N > 0 and abs(Md) / N <= d - (h if h is not None else d) / 2
    fcd, fctd = p["fcd"], p["fctd"]
    bw, d = bw * 100, d * 100
    fywd = min(fywk / GAMA_S, 435) / 10
    av2 = 1 - fck / 250
    Vc0 = 0.6 * fctd * bw * d
    if modelo == 1:
        VRd2 = 0.27 * av2 * fcd * bw * d
        Vc, cot = (0.0 if tracao_total else Vc0), 1.0
    else:
        t = math.radians(theta)
        VRd2 = 0.54 * av2 * fcd * bw * d * math.sin(t) ** 2 / math.tan(t)
        # Vc1: = Vc0 se Vsd <= Vc0; zero em Vsd = VRd2; interpolação linear
        Vc = Vc0 if Vd <= Vc0 else max(0.0, Vc0 * (VRd2 - Vd) / (VRd2 - Vc0))
        cot = 1 / math.tan(t)
        if tracao_total:
            Vc = 0.0
    asw = max(0.0, (Vd - Vc) / (0.9 * d * fywd * cot)) * 100  # cm²/m
    rho_sw = 0.2 * p["fctm"] / (fywk / 10)  # 17.4.1.1.1
    asw_min = rho_sw * bw * 100
    alertas = [] if Vd <= VRd2 else ["Vsd > VRd2: esmagamento da biela — aumentar seção"]
    # 18.3.3.2: espaçamentos máximos dos estribos (longitudinal s e entre ramos st) e 5 mm ≤ φt ≤ bw/10
    dm = d / 100
    s_max = min(0.6 * dm, 0.30) if Vd <= 0.67 * VRd2 else min(0.3 * dm, 0.20)
    st_max = min(dm, 0.80) if Vd <= 0.20 * VRd2 else min(0.6 * dm, 0.35)
    return dict(VRd2=VRd2, Vc=Vc, Asw_s=max(asw, asw_min), Asw_s_calc=asw,
                Asw_s_min=asw_min, ok=Vd <= VRd2, alertas=alertas,
                s_max_m=s_max, st_max_m=st_max, phi_t_max_mm=bw, item_estribo="18.3.3.2")  # bw cm → bw/10 em mm


def armadura_pele(bw, h, d):
    """Armadura de pele — NBR 6118 17.3.5.2.3: h > 60 cm → 0,10%·Ac,alma por face, ≤ 5 cm²/m por face,
    espaçamento ≤ 20 cm (e ≤ d/3, como o CYPE e a 2003; a favor). None se h ≤ 60 cm (dispensada).
    Barras: n por face no trecho h − 2·d' (d' = h − d) e a menor bitola com n·a ≥ As_face."""
    if h <= 0.60 + 1e-12:
        return None
    As = min(0.001 * bw * h * 1e4, 5.0 * h)  # cm² por face
    s = min(d / 3, 0.20)
    n = max(1, math.ceil((h - 2 * (h - d)) / s - 1e-9) - 1)
    phi = next((f for f in BITOLAS if n * math.pi * f ** 2 / 400 >= As - 1e-9), BITOLAS[-1])
    return dict(As_face_cm2=As, s_max_m=s, n_face=n, phi_mm=phi, As_ef_face_cm2=n * math.pi * phi ** 2 / 400,
                item="17.3.5.2.3")


def gama_n(bmin):
    """γn da Tabela 13.1 (NBR 6118 13.2.3) para pilar com menor dimensão bmin (m): 1,95 − 0,05·b(cm) se
    b < 19 cm, senão 1,0. b < 14 cm não é permitido (verificado à parte)."""
    return 1.0 if bmin >= 0.19 - 1e-12 else 1.95 - 0.05 * bmin * 100


# ---------------------------------------------------------------- estádio II / fissuração
def estadio_II(b, d, As, Ecs, As2=0.0, d2=0.0):
    """Linha neutra e inércia fissurada (seção retangular, cm). αe = Es/Ecs."""
    ae = ES / Ecs
    a, bb, c = b / 2, ae * As + (ae - 1) * As2, -(ae * As * d + (ae - 1) * As2 * d2)
    x = (-bb + math.sqrt(bb ** 2 - 4 * a * c)) / (2 * a)
    I = b * x ** 3 / 3 + ae * As * (d - x) ** 2 + (ae - 1) * As2 * (x - d2) ** 2
    return x, I


def abertura_fissura(phi, sigma_s, rho_r, fck, eta1=2.25):
    """wk (mm) — NBR 6118 17.3.3.2. phi em mm, σs em MPa, ρr = As/Acr."""
    fctm = parametros(fck)["fctm"] * 10
    es = ES * 10
    w1 = phi / (12.5 * eta1) * sigma_s / es * 3 * sigma_s / fctm
    w2 = phi / (12.5 * eta1) * sigma_s / es * (4 / rho_r + 45)
    return min(w1, w2)


# ---------------------------------------------------------------- flecha
def xi(t):
    """ξ(t), t em meses — NBR 6118 17.3.2.1.2."""
    return 2.0 if t > 70 else 0.68 * 0.996 ** t * t ** 0.32


def flecha_viga(b, h, d, As, Ma, L, q, fck, As2=0.0, d2=0.0, coef=5 / 384,
                t0=1.0, t=None, limite=250):
    """Flecha imediata (Branson, 17.3.2.1.1) + diferida (αf, 17.3.2.1.2).
    Ma = momento em serviço (quase-permanente) kN·m; q em kN/m; coef da elástica
    (5/384 biapoiada com carga uniforme). Retorna flechas em mm."""
    p = parametros(fck)
    bc, hc, dc, Lc = b * 100, h * 100, d * 100, L * 100
    Ic = bc * hc ** 3 / 12
    Mr = 1.5 * p["fctm"] * Ic / (hc / 2)  # 17.3.1, α=1,5 retangular
    _, I2 = estadio_II(bc, dc, As, p["Ecs"], As2, d2 * 100)
    Mac = abs(Ma) * 100  # momento negativo (balanço/apoio) também fissura
    r3 = min(1.0, (Mr / Mac) ** 3) if Mac > 0 else 1.0
    Ieq = min(Ic, r3 * Ic + (1 - r3) * I2)
    fi = coef * (q / 100) * Lc ** 4 / (p["Ecs"] * Ieq) * 10  # mm
    dxi = (2.0 if t is None else xi(t)) - xi(t0)
    rho2 = As2 / (bc * dc)
    af = dxi / (1 + 50 * rho2)
    ft = fi * (1 + af)
    flim = L * 1000 / limite
    alertas = [] if ft <= flim else [f"Flecha total {ft:.1f} mm > L/{limite} (Tabela 13.3)"]
    return dict(Ic=Ic, I2=I2, Ieq=Ieq, Mr=Mr / 100, Ecs=p["Ecs"] * 10, f_imediata=fi,
                alfa_f=af, f_total=ft, f_limite=flim, ok=ft <= flim,
                itens=["17.3.2.1.1", "17.3.2.1.2", "Tabela 13.3"], alertas=alertas)


def flecha_ativa(fi_antes, fi_pos, t0, t_alv, rho2=0.0):
    """Flecha ativa (Tabela 13.3, "após a construção da parede/forro"; LC-017): a que ocorre depois da
    construção do elemento danificável = flecha total a prazo infinito − flecha já ocorrida até t_alv.
    fi_antes: flecha imediata das cargas aplicadas em t0 (escoramento retirado); fi_pos: incremento imediato
    das aplicadas em t_alv (a própria parede, revestimentos, ψ2·q). Fluência de cada parcela pelo αf de
    17.3.2.1.2 com a sua idade de carregamento: αf = (ξ(t) − ξ(t0))/(1 + 50ρ'), ξ(∞) = 2. Meses; flechas
    em qualquer unidade (a da entrada)."""
    if t_alv < t0:
        raise ValueError(f"flecha_ativa: t_alv = {t_alv} < t0 = {t0} meses (parede antes da retirada do escoramento)")
    k = 1 + 50 * rho2
    f_inf = fi_antes * (1 + (2.0 - xi(t0)) / k) + fi_pos * (1 + (2.0 - xi(t_alv)) / k)  # 17.3.2.1.2
    f_ate = fi_antes * (1 + (xi(t_alv) - xi(t0)) / k)
    return dict(f_inf=f_inf, f_ate_t=f_ate, f_ativa=f_inf - f_ate, item="17.3.2.1.2, Tabela 13.3 (flecha ativa)")


def comprimento_equivalente(l, l0=None, h=None):
    """le do pilar vinculado nas duas extremidades — NBR 6118 15.6: o menor entre l0 + h e l. l = distância
    entre eixos dos elementos que vinculam o pilar (m); l0 = distância entre as faces internas; h = altura da
    seção no plano em estudo. Sem l0: le = l (o limite superior de 15.6, a favor da segurança). O CYPE 2016
    toma o maior dos dois (LC-018); seguimos a norma."""
    if l0 is None:
        return dict(le=l, item="15.6 (le = l)")
    if l0 < 0 or h is None or h <= 0:
        raise ValueError(f"comprimento_equivalente: l0 = {l0} e h = {h} inválidos (15.6)")
    return dict(le=min(l0 + h, l), item="15.6 (menor entre l0 + h e l)")


# ---------------------------------------------------------------- viga
def dimensionar_viga(b, h, d, Md, Vd, fck, fyk=500, bf=None, hf=0.0, fywk=500,
                     modelo_cis=1, theta=45.0, cobrimento=0.03):
    fl = flexao_simples(b, d, Md, fck, fyk, h, bf, hf)
    Ac = b * h * 1e4 if not bf else (b * h + (bf - b) * hf) * 1e4
    W0 = None
    if bf:  # T com mesa comprimida: W0 em relação à fibra inferior (tracionada)
        A1, A2 = bf * hf, b * (h - hf)
        yc = (A1 * hf / 2 + A2 * (h + hf) / 2) / (A1 + A2)
        I = bf * hf ** 3 / 12 + A1 * (yc - hf / 2) ** 2 + b * (h - hf) ** 3 / 12 + A2 * ((h + hf) / 2 - yc) ** 2
        W0 = I / (h - yc)
    amin = as_min_flexao(bf or b, h, d, fck, fyk, W0, Ac)  # 17.3.5.2.1
    As_min = amin["As_min"]
    As_max = 0.04 * Ac  # 17.3.5.2.4
    As = max(fl["As"], As_min)
    cis = cisalhamento(b, d, Vd, fck, fywk, modelo_cis, theta)
    alertas = fl["alertas"] + cis["alertas"]
    if As + fl["As2"] > As_max:
        alertas.append("As + A's > 4% Ac (17.3.5.2.4)")
    barras = escolher_barras(As, b, cobrimento)
    if barras is None:
        alertas.append("Barras não cabem em 2 camadas — aumentar bw")
    return dict(As=As, As_calc=fl["As"], As2=fl["As2"], As_min=As_min, Md_min=amin["Md_min"], As_max=As_max,
                x=fl["x"], x_d=fl["x_d"], dominio=fl["dominio"], taxa=As / Ac,
                cisalhamento=cis, barras=barras,
                itens=["17.2.2", "14.6.4.3", "17.3.5.2.1", "17.3.5.2.4",
                       "17.4.2.2" if modelo_cis == 1 else "17.4.2.3", "18.3.2.2"],
                alertas=alertas)


# ---------------------------------------------------------------- pilar
def _barras_perimetro(b, h, dl, nb, nh):
    """Posições (cm, origem no topo, eixo ao longo de h) de barras no perímetro:
    nb barras em cada face paralela a b, nh barras intermediárias por face lateral."""
    ys = [dl] * nb + [h - dl] * nb
    for i in range(1, nh + 1):
        y = dl + i * (h - 2 * dl) / (nh + 1)
        ys += [y, y]
    return ys


def _curvatura(x, h, ys, p):
    """Curvatura (1/cm) para LN a x do topo. Domínios por pivô (17.2.2): A (εs = 10‰ na barra
    mais tracionada), B (εcu na borda), C (εc2 a (1 − εc2/εcu)·h)."""
    d = max(ys)
    ecu, ec2 = p["ecu"], p["ec2"]
    if x <= ecu / (ecu + 0.010) * d:
        return 0.010 / (d - x)
    if x <= h:
        return ecu / x
    return ec2 / (x - (1 - ec2 / ecu) * h)


def _esforcos_secao(x, b, h, ys, As_barra, p, fyd):
    """(N, M) resistentes para LN a x do topo, com o diagrama parábola-retângulo (8.2.10.1,
    tensão de pico 0,85·ηc·fcd) integrado analiticamente. M em relação ao meio de h.
    ponytail: o retangular (λx) superestima o concreto em compressão dominante (−9% em As no
    ex. 13.1.1 de Bastos); a parábola-retângulo é o diagrama de referência da norma."""
    k = _curvatura(x, h, ys, p)
    fc, ec2, n = 0.85 * p["eta_c"] * p["fcd"], p["ec2"], p["n"]

    def F(e):  # ∫0^e σ/fc dε
        if e <= ec2:
            return e + ec2 / (n + 1) * ((1 - e / ec2) ** (n + 1) - 1)
        return ec2 * n / (n + 1) + (e - ec2)

    def G(e):  # ∫0^e (σ/fc)·ε dε
        if e <= ec2:
            u = 1 - e / ec2
            return e * e / 2 - ec2 ** 2 * ((1 - u ** (n + 1)) / (n + 1) - (1 - u ** (n + 2)) / (n + 2))
        return G(ec2) + (e * e - ec2 * ec2) / 2

    et, eb = k * x, k * max(x - h, 0.0)  # deformações na borda comprimida e no limite inferior do concreto
    Nc = fc * b * (F(et) - F(eb)) / k
    N = Nc
    M = (h / 2 - x) * Nc + fc * b * (G(et) - G(eb)) / k ** 2
    for yi in ys:
        s = _sigma_s(k * (x - yi), fyd)
        N += s * As_barra
        M += s * As_barra * (h / 2 - yi)
    return N, M


def _MRd(Nd, b, h, ys, As_barra, p, fyd):
    """Momento resistente para Nd dado (raiz de N(x) = Nd em x, Brent em ln x). None se Nd > NRd,max.
    N(x) é contínua e crescente em x (pivôs A/B/C contínuos, 17.2.2)."""
    f = lambda u: _esforcos_secao(math.exp(u), b, h, ys, As_barra, p, fyd)[0] - Nd
    lo, hi = math.log(1e-6 * h), math.log(1e4 * h)
    if f(hi) < 0:
        return None
    if f(lo) > 0:
        return 0.0
    u = brentq(f, lo, hi, xtol=1e-13, rtol=1e-14)
    return _esforcos_secao(math.exp(u), b, h, ys, As_barra, p, fyd)[1]


def flexo_compressao(b, h, Nd, Md, fck, fyk=500, dl=0.04, nb=2, nh=0):
    """As total (cm²) simétrico mínimo para (Nd kN, Md kN·m) — integração por bisseção em As."""
    p = parametros(fck)
    fyd = fyk / GAMA_S / 10
    bc, hc = b * 100, h * 100
    ys = _barras_perimetro(bc, hc, dl * 100, nb, nh)
    M = abs(Md) * 100

    def g(As):  # MRd(As) − Md, crescente em As; Nd > NRd,máx → negativo (salto no limiar, Brent acha)
        m = _MRd(Nd, bc, hc, ys, As / len(ys), p, fyd)
        return m - M if m is not None else -M - 1.0
    if g(0.0) >= 0:
        return 0.0
    hi = 0.08 * bc * hc  # até 8% (limite absoluto)
    if g(hi) < 0:
        return None
    return brentq(g, 0.0, hi, xtol=1e-10, rtol=1e-13)


def segunda_ordem(Nd, M1d, h, le, fck, b, alfa_b=1.0, metodo="curvatura"):
    """Momento total de 2ª ordem local (15.8.3.3.2 curvatura / 15.8.3.3.3 κ). kN, m."""
    p = parametros(fck)
    nu = Nd / (b * h * 1e4 * p["fcd"])
    M1 = abs(M1d)
    if metodo == "curvatura":
        r1 = min(0.005 / (h * (nu + 0.5)), 0.005 / h)
        return max(alfa_b * M1 + Nd * le ** 2 / 10 * r1, M1)
    # rigidez κ aproximada: solução da equação do 2º grau (15.8.3.3.3)
    A = 5 * h
    B = h ** 2 * Nd - Nd * le ** 2 / 320 - 5 * h * alfa_b * M1
    C = -Nd * h ** 2 * alfa_b * M1
    return max((-B + math.sqrt(B ** 2 - 4 * A * C)) / (2 * A), M1)


def _alfas_b(alfa_b):
    """αb por plano (x = plano de h, y = plano de b): número ou par (αbx, αby). 15.8.2: 0,4 ≤ αb ≤ 1,0."""
    abx, aby = (alfa_b, alfa_b) if isinstance(alfa_b, (int, float)) else tuple(alfa_b)
    for a in (abx, aby):
        if not 0.4 <= float(a) <= 1.0:
            raise ValueError(f"alfa_b = {a}: 15.8.2 exige 0,4 ≤ αb ≤ 1,0")
    return float(abx), float(aby)


def _as_max_pilar(emenda):
    """Fração de Ac — NBR 6118 17.3.5.3.2: 8% incluindo a sobreposição das emendas. Emenda por traspasse
    (todas as barras na mesma seção, padrão) dobra a área na emenda → 4% fora dela (LC-005); luva/solda → 8%."""
    if emenda not in ("traspasse", "luva"):
        raise ValueError(f"emenda '{emenda}' inválida (traspasse | luva) — 17.3.5.3.2")
    return 0.04 if emenda == "traspasse" else 0.08


def _cabe_face(n, vao_mm, phi):
    """n barras numa face com vão vao_mm entre os centros extremos: espaçamento livre ≥ máx(20 mm, φ,
    1,2·dmáx) — NBR 6118 18.4.2.2 (dmáx = 19 mm, brita 1, como em escolher_barras)."""
    return n < 2 or vao_mm / (n - 1) - phi >= max(20.0, phi, 1.2 * 19.0)


def _bitola_pilar(As, b, h, dl, nb, nh, phi=None):
    """Menor φl (mm) com n·π·φ²/4 ≥ As (cm²) na disposição nb (faces // b) / nh (intermediárias nas
    faces // h): 10 mm ≤ φl ≤ menor lado/8 (18.4.2.1) e espaçamento livre (18.4.2.2). None se nenhuma."""
    n = 2 * nb + 2 * nh
    for f in (phi,) if phi else BITOLAS:
        if (10.0 <= f <= min(b, h) * 1000 / 8 and n * math.pi * f ** 2 / 400 >= As - 1e-9
                and _cabe_face(nb, (b - 2 * dl) * 1000, f) and _cabe_face(nh + 2, (h - 2 * dl) * 1000, f)):
            return f
    return None


def _disposicao_minima(b, h, dl, nb=2, nh=0):
    """(nb, nh) ≥ os dados com eixos a no máximo mín(2·menor lado, 400 mm) — NBR 6118 18.4.2.2 (LC-006).
    nb: barras em cada face // b (vão b − 2dl); nh: intermediárias em cada face // h (vão h − 2dl)."""
    smax = min(2 * min(b, h), 0.40)
    return (max(nb, math.ceil((b - 2 * dl) / smax - 1e-9) + 1),
            max(nh, math.ceil((h - 2 * dl) / smax - 1e-9) - 1))


def _proxima_disposicao(b, h, dl, nb, nh, phi=None, mais_nb=None):
    """Mais uma barra por face: nas faces // b (nb, resistem a Mx) se mais_nb, nas faces // h (nh) se
    False, no lado de maior espaçamento se None; None se nada mais couber (18.4.2.2). Acrescentar barras
    só reduz o espaçamento: o máximo de 18.4.2.2 vem garantido de _disposicao_minima."""
    if mais_nb is None:
        mais_nb = (b - 2 * dl) / (nb - 1) > (h - 2 * dl) / (nh + 1)
    opcoes = [(nb + 1, nh), (nb, nh + 1)] if mais_nb else [(nb, nh + 1), (nb + 1, nh)]
    return next((o for o in opcoes if _bitola_pilar(0.0, b, h, dl, *o, phi=phi)), None)


def _momentos_pilar(b, h, Nd, Mdx, Mdy, lex, ley, fck, alfa_b=1.0, metodo="curvatura"):
    """Momentos de cálculo por direção (x = plano de h, y = plano de b): M1d,mín (11.3.3.4.3), λ e λ1 (15.8.2)
    e Md,tot da 2ª ordem local (15.8.3.3). → (dirs, alertas)."""
    alertas, dirs = [], {}
    for nome, hh, bb, M, l, ab in (("x", h, b, Mdx, lex, _alfas_b(alfa_b)[0]),
                                   ("y", b, h, Mdy, ley, _alfas_b(alfa_b)[1])):
        M1min = Nd * (0.015 + 0.03 * hh)  # 11.3.3.4.3
        M1 = max(abs(M), M1min)
        if abs(M) < M1min:
            ab = 1.0  # 15.8.2: αb = 1,0 se os momentos de 1ª ordem forem menores que M1d,mín
        lam = l * math.sqrt(12) / hh  # 15.8.2
        e1 = abs(M) / Nd if Nd else 0
        lam1 = min(max((25 + 12.5 * e1 / hh) / ab, 35), 90)
        if lam > 90:
            alertas.append(f"λ{nome}={lam:.1f} > 90: método do pilar-padrão não se aplica (15.8.3.3)")
        Mtot = segunda_ordem(Nd, M1, hh, l, fck, bb, ab, metodo) if lam > lam1 else M1
        dirs[nome] = dict(h=hh, b=bb, M1d_min=M1min, M1d=M1, lam=lam, lam1=lam1, alfa_b=ab, Md_tot=Mtot)
    return dirs, alertas


def _pilar_atende(b, h, Nd, Mdx, Mdy, dirs, As, fck, fyk, dl, nb, nh):
    """True se dimensionar_pilar daria As' ≤ As (cm²) na disposição (nb, nh): As ≥ As,mín (17.3.5.3.1) e, com
    As/1,02 (folga do passo de 2% da oblíqua), MRx ≥ Mx,tot, MRy ≥ My,tot e, com os dois momentos de cálculo,
    a oblíqua de 17.2.5.2 (α = 1,2) ≤ 1. 2 integrações, contra ~20 de dimensionar_pilar: _pilar usa para
    pular combinações já cobertas."""
    p, fyd, Ac = parametros(fck), fyk / GAMA_S / 10, b * h * 1e4
    if As < max(0.15 * Nd / fyd, 0.004 * Ac) or As > 0.08 * Ac:
        return False
    As /= 1.02
    ysx = _barras_perimetro(b * 100, h * 100, dl * 100, nb, nh)
    mx = _MRd(Nd, b * 100, h * 100, ysx, As / len(ysx), p, fyd)
    my = _MRd(Nd, h * 100, b * 100, _barras_perimetro(h * 100, b * 100, dl * 100, nh + 2, nb - 2),
              As / len(ysx), p, fyd)
    Mx, My = dirs["x"]["Md_tot"] * 100, dirs["y"]["Md_tot"] * 100
    if mx is None or my is None or mx < Mx or my < My:
        return False
    return not (abs(Mdx) > 0 and abs(Mdy) > 0) or (Mx / mx) ** 1.2 + (My / my) ** 1.2 <= 1.0


def dimensionar_pilar(b, h, Nd, Mdx, Mdy, le, fck, fyk=500, lex=None, ley=None, dl=0.04,
                      alfa_b=1.0, metodo="curvatura", nb=2, nh=0, phi_l=None, emenda="traspasse"):
    """Pilar retangular b×h. Mdx age no plano de h (flexão em torno do eixo paralelo a b);
    Mdy no plano de b. Oblíqua: fórmula aproximada 17.2.5.2 com α=1,2 (seção retangular):
    (Mx/MRx)^α + (My/MRy)^α <= 1, sendo MRx/MRy obtidos por integração da seção com a
    armadura adotada. ponytail: envoltória aproximada; integração biaxial se precisar de
    seções não retangulares. alfa_b: número ou par (αbx, αby), 0,4 ≤ αb ≤ 1,0 (15.8.2).
    Barras: n = 2·nb + 2·nh com n·π·φl²/4 ≥ As (_bitola_pilar); sem bitola que atenda, acrescenta barras
    (_proxima_disposicao) e refaz o cálculo com a nova disposição; sem disposição possível → ok = False,
    phi_l = None. phi_l informado fixa a bitola. nb/nh sobem ao mínimo do espaçamento máximo (18.4.2.2).
    emenda: "traspasse" (padrão) → As ≤ 4% Ac fora da emenda (8% com a sobreposição); "luva" → 8% (17.3.5.3.2)."""
    p = parametros(fck)
    As_lim = _as_max_pilar(emenda) * b * h * 1e4
    nb, nh = _disposicao_minima(b, h, dl, nb, nh)
    lex, ley = lex or le, ley or le
    dirs, alertas = _momentos_pilar(b, h, Nd, Mdx, Mdy, lex, ley, fck, alfa_b, metodo)
    Ac = b * h * 1e4
    # nb barras nas faces // b, nh intermediárias; para a direção y troca-se o papel das faces
    Asx = flexo_compressao(b, h, Nd, dirs["x"]["Md_tot"], fck, fyk, dl, nb, nh)
    Asy = flexo_compressao(h, b, Nd, dirs["y"]["Md_tot"], fck, fyk, dl, nh + 2, nb - 2)
    As_min = max(0.15 * Nd / (fyk / GAMA_S / 10), 0.004 * Ac)  # 17.3.5.3.1
    As_max = 0.08 * Ac  # 17.3.5.3.2 (inclui emendas)
    if Asx is None or Asy is None:
        return dict(ok=False, alertas=alertas + ["Seção insuficiente (As > 8%)"], direcoes=dirs)
    As = max(Asx, Asy, As_min)
    # verificação oblíqua com as duas envoltórias (bisseção aumenta As até atender)
    fyd = fyk / GAMA_S / 10
    ysx = _barras_perimetro(b * 100, h * 100, dl * 100, nb, nh)
    ysy = _barras_perimetro(h * 100, b * 100, dl * 100, nh + 2, nb - 2)
    nbar = len(ysx)

    def interacao(As_):
        mx = _MRd(Nd, b * 100, h * 100, ysx, As_ / nbar, p, fyd)
        my = _MRd(Nd, h * 100, b * 100, ysy, As_ / nbar, p, fyd)
        if not mx or not my:
            return 99.0
        return ((dirs["x"]["Md_tot"] * 100 / mx) ** 1.2 + (dirs["y"]["Md_tot"] * 100 / my) ** 1.2)

    # momentos mínimos não precisam ser somados na oblíqua (11.3.3.4.3): usa só os de cálculo
    if abs(Mdx) > 0 and abs(Mdy) > 0:
        while interacao(As) > 1.0 and As < As_max:
            As *= 1.02
    eta = interacao(As) if abs(Mdx) > 0 and abs(Mdy) > 0 else None
    if As > As_lim:
        alertas.append(f"As > {As_lim / Ac:.0%} Ac (17.3.5.3.2, emenda por {emenda})")
    phi = _bitola_pilar(As, b, h, dl, nb, nh, phi_l) if As <= As_max else None
    if phi is None and As <= As_max:
        # barras nas faces que resistem ao momento de maior excentricidade relativa (Md,tot/h)
        mais_nb = dirs["x"]["Md_tot"] / h >= dirs["y"]["Md_tot"] / b
        prox = _proxima_disposicao(b, h, dl, nb, nh, phi_l, mais_nb)
        if prox:  # 18.4.2: mais barras → refaz (a resistência depende da disposição)
            return dimensionar_pilar(b, h, Nd, Mdx, Mdy, le, fck, fyk, lex, ley, dl, alfa_b, metodo,
                                     *prox, phi_l=phi_l, emenda=emenda)
        alertas.append(f"nenhuma disposição de barras atende As = {As:.2f} cm² com φl de 10 mm a b/8 e o "
                       "espaçamento livre mínimo (18.4.2.1, 18.4.2.2): aumentar a seção")
    estribo = None
    if phi:
        estribo = dict(phi_t=max(5.0, phi / 4), s_max_mm=min(200.0, min(b, h) * 1000, 12 * phi))  # 18.4.3 (CA-50)
    return dict(ok=As <= As_lim and phi is not None, As=As, As_min=As_min, As_max=As_lim, taxa=As / Ac,
                nu=Nd / (Ac * p["fcd"]), n_barras=nbar, nb=nb, nh=nh, phi_l=phi,
                estribo=estribo, interacao_obliqua=eta,
                direcoes=dirs,
                itens=["11.3.3.4.3", "15.8.2", "15.8.3.3.2" if metodo == "curvatura" else "15.8.3.3.3",
                       "17.2.5.2", "17.3.5.3", "18.4.3"],
                alertas=alertas)


# ---------------------------------------------------------------- laje
def dimensionar_laje(lx, ly, h, p_kN_m2, fck, fyk=500, d=None):
    """Laje maciça simplesmente apoiada nos 4 bordos (Marcus, simplificado) ou
    unidirecional (ly/lx > 2: faixa biapoiada). lx = menor vão (m).
    Marcus: kx = λ⁴/(1+λ⁴), mx = kx·p·lx²/8·(1 − 5/6·kx·(lx/ly)²) — reproduz pl²/27,4 na laje
    quadrada. ponytail: só bordos apoiados; adicionar coeficientes de Bares p/ engastes."""
    lx, ly = min(lx, ly), max(lx, ly)
    d = d or h - 0.025
    lam = ly / lx
    alertas = []
    if lam > 2:
        Mx, My, tipo = p_kN_m2 * lx ** 2 / 8, 0.0, "unidirecional"
    else:
        kx = lam ** 4 / (1 + lam ** 4)
        ky = 1 - kx
        Mx = kx * p_kN_m2 * lx ** 2 / 8 * (1 - 5 / 6 * kx * (lx / ly) ** 2)
        My = ky * p_kN_m2 * ly ** 2 / 8 * (1 - 5 / 6 * ky * (ly / lx) ** 2)
        tipo = "bidirecional (Marcus)"
    Asmin_base = rho_min(fck) * h * 1e4  # cm²/m (bw = 1 m)
    res = {}
    for nome, M, fator in (("x", Mx, 0.67 if tipo != "unidirecional" else 1.0),
                           ("y", My, 0.67 if tipo != "unidirecional" else 0.5)):
        fl = flexao_simples(1.0, d, 1.4 * M, fck, fyk)
        As_min = fator * Asmin_base  # 19.3.3.2 / Tabela 19.1
        res[nome] = dict(Mk=M, Md=1.4 * M, As=max(fl["As"], As_min), As_calc=fl["As"],
                         As_min=As_min, x_d=fl["x_d"])
        alertas += fl["alertas"]
    return dict(tipo=tipo, lambda_=lam, direcoes=res, itens=["14.7.6", "19.3.3.2", "Tabela 19.1"],
                alertas=alertas + ["laje: NBR 6118:2014 (19.3.3.2 não conferido na 2023)"])


# ---------------------------------------------------------------- torção
def torcao(b, h, Td, fck, c1, Vd=0.0, VRd2=None, fywk=500, theta=45.0, he=None):
    """Torção de equilíbrio em seção retangular cheia (17.5.1) + interação com cortante (17.7.2).
    b, h, c1 (eixo da barra de canto à face), he em m; Td em kN·m; Vd e VRd2 (mesmo θ) em kN.
    Seção vazada equivalente (17.5.1.4.1): 2c1 ≤ he ≤ A/u. Sem he informado, a biela (TRd2) usa o
    menor he e as armaduras usam o maior (menor Ae): os dois lados a favor da segurança.
    Saídas: TRd2 em kN·m; A90/s por ramo em cm²/m (mínimo por ramo = ρsw·bw/2); Asl/ue em cm²/m; Asl total em cm²."""
    p = parametros(fck)
    bc, hc, c1c = b * 100, h * 100, c1 * 100
    A_u = bc * hc / (2 * (bc + hc))
    alertas = []
    if he is None:
        he_arm, he_bie = A_u, min(2 * c1c, A_u)
        if A_u < 2 * c1c:
            alertas.append("A/u < 2c1: he = A/u (17.5.1.4.1)")
    else:
        he_arm = he_bie = he * 100
    t = math.radians(theta)
    Ae_b = (bc - he_bie) * (hc - he_bie)
    TRd2 = 0.5 * (1 - fck / 250) * p["fcd"] * Ae_b * he_bie * math.sin(2 * t)  # 17.5.1.5, kN·cm
    Ae = (bc - he_arm) * (hc - he_arm)
    ue = 2 * ((bc - he_arm) + (hc - he_arm))
    fywd = min(fywk / GAMA_S, 435) / 10
    T = abs(Td) * 100
    A90 = T / (2 * Ae * fywd) * math.tan(t) * 100  # 17.5.1.6, cm²/m por ramo
    Asl_ue = T / (2 * Ae * fywd * math.tan(t)) * 100  # 17.5.1.6, cm²/m
    rho = 0.2 * p["fctm"] / (fywk / 10)  # 17.5.1.2: ρsl = ρsw ≥ 0,2·fctm/fywk
    # ρsw = Asw/(bw·s) com Asw = os 2 ramos do estribo fechado → mínimo por ramo = ρ·bw/2
    A90_min, Asl_min = rho * bc * 100 / 2, rho * he_arm * 100
    taxa_T = T / TRd2
    inter = taxa_T + (abs(Vd) / VRd2 if VRd2 else 0.0)  # 17.7.2.2
    if inter > 1:
        alertas.append(f"Vd/VRd2 + Td/TRd2 = {inter:.2f} > 1: esmagamento da biela (17.7.2.2)")
    Asl_ue_f = max(Asl_ue, Asl_min)
    # 18.3.4: barras longitudinais a ≤ 350 mm ao longo de ue; 17.5.1.6: uma em cada vértice (4 na retangular).
    # Estribo de torção: φt e s de 18.3.3.2 (critério "estribos" da viga)
    n_Asl = max(4, math.ceil(ue / 35.0 - 1e-9))
    return dict(TRd2=TRd2 / 100, he_m=he_arm / 100, he_biela_m=he_bie / 100, Ae_m2=Ae / 1e4, ue_m=ue / 100,
                A90_s=max(A90, A90_min), A90_s_calc=A90, A90_s_min=A90_min,
                Asl_ue=Asl_ue_f, Asl_ue_calc=Asl_ue, Asl_ue_min=Asl_min, Asl=Asl_ue_f * ue / 100,
                n_Asl_min=n_Asl, sl_max_m=0.35,
                taxa_T=taxa_T, interacao=inter, ok=inter <= 1,
                item="17.5.1.5, 17.5.1.6, 17.7.2.2, 18.3.4", alertas=alertas)


# ---------------------------------------------------------------- limites e estabilidade
# NBR 6118:2023 Tabela 13.3 (transcrição em [B6], p. 57–58): (denominador, fator do vão, estado, razão, efeito)
_LIMITES = {
    "aceitabilidade": (250, 1, "ELS_QP", "L/250", "aceitabilidade sensorial visual, deslocamento total"),
    "apos_construcao": (350, 1, "ELS_QP", "L/350", "forros com revestimentos colados, após a construção do forro"),
    "alvenaria": (500, 1, "ELS_QP", "L/500", "alvenaria/caixilhos, após a construção da parede (e ≤ 10 mm)"),
    "balanco": (250, 2, "ELS_QP", "2L/250", "aceitabilidade em balanço: vão equivalente = 2L (nota 1)"),
    "horizontal_topo": (1700, 1, "ELS_FREQ", "H/1700", "movimento lateral do edifício (vento, ψ1 = 0,30)"),
    "entre_pisos": (850, 1, "ELS_FREQ", "h/850", "movimento lateral entre pavimentos (vento, ψ1 = 0,30)"),
}


def limite_deslocamento(elemento, L_m):
    """Deslocamento-limite (m) da Tabela 13.3. L_m: vão (balanço: comprimento do balanço), H total ou
    h entre pisos. Flecha de longa duração com a combinação quase permanente (nota 5, 2023)."""
    if elemento not in _LIMITES:
        raise ValueError(f"elemento_desloc '{elemento}' desconhecido; válidos: {', '.join(_LIMITES)}")
    den, fat, estado, razao, efeito = _LIMITES[elemento]
    lim = fat * L_m / den
    if elemento == "alvenaria":
        lim = min(lim, 0.010)
    return dict(limite_m=lim, razao=razao, item="NBR 6118 13.3 (Tabela 13.3)", estado=estado, efeito=efeito)


def gama_z(pavimentos):
    """γz = 1/(1 − ΔMtot,d/M1,tot,d) (15.5.3). pavimentos: [{z_m, H_kN, P_kN, delta_m}] da mesma
    combinação ELU: M1 = Σ H·z (forças horizontais de cálculo); ΔM = Σ P·δ (forças verticais da
    combinação × deslocamentos horizontais de 1ª ordem). Classificação: ≤ 1,1 nós fixos;
    ≤ 1,3 nós móveis com majoração 0,95·γz dos esforços horizontais (15.7.2); > 1,3 fora do limite."""
    if not pavimentos:
        raise ValueError("gama_z: lista de pavimentos vazia")
    M1 = abs(sum(float(p["H_kN"]) * float(p["z_m"]) for p in pavimentos))
    dM = abs(sum(float(p["P_kN"]) * float(p["delta_m"]) for p in pavimentos))
    if M1 <= 0:
        raise ValueError("gama_z: sem forças horizontais (M1,tot,d = 0)")
    if dM >= M1:
        raise ValueError(f"gama_z: ΔMtot,d = {dM:.1f} ≥ M1,tot,d = {M1:.1f} kN·m — instabilidade global (15.5.3)")
    gz = 1 / (1 - dM / M1)
    avisos = []
    if len(pavimentos) < 4:
        avisos.append("γz é válido para estruturas reticuladas de no mínimo quatro andares (15.5.3)")
    if gz <= 1.1:
        cl, maj = "nos_fixos", 1.0
    elif gz <= 1.3:
        cl, maj = "nos_moveis", 0.95 * gz
    else:
        cl, maj = "fora_do_limite", None
        avisos.append(f"γz = {gz:.3f} > 1,3: majoração 0,95·γz não vale (15.7.2); "
                      "fazer análise de 2ª ordem rigorosa (P-Δ com não linearidade física, 15.7.3)")
    return dict(gama_z=gz, M1_kNm=M1, dM_kNm=dM, classificacao=cl, majoracao=maj, item="15.5.3",
                avisos=avisos)


# ---------------------------------------------------------------- verificação de barra (ESPEC §7.2)
PHI_T, PHI_L = 0.005, 0.020  # m: estribo e barra longitudinal supostos em d' = c + φt + φl/2
CNOM = {"I": 0.025, "II": 0.030, "III": 0.040, "IV": 0.050}  # Tabela 7.2, viga/pilar de CA (Δc = 10 mm)
FCK_MIN = {"I": 20, "II": 25, "III": 30, "IV": 40}  # Tabela 7.1, concreto armado
WK_LIM = {"I": 0.4, "II": 0.3, "III": 0.3, "IV": 0.2}  # Tabela 13.4, CA, ELS-W, combinação frequente (mm)
LAMBDA_MAX = 90.0  # 15.8.3.3: limite dos métodos aproximados do pilar-padrão
INSUFICIENTE = 9.99  # taxa sinalizadora quando a seção não comporta a armadura (As > As,máx)


def _wk(M, As, b, h, d, cob, fck, eta1, fyk=500):
    """wk (mm) para o momento de serviço M (kN·m) e As (cm²): σs no estádio II com αe = 15 em d
    (17.3.3.2), φ das barras escolhidas. Acri por barra (17.3.3.2, LC-002): retângulo até 7,5φ do eixo,
    limitado pelas faces, pela metade da distância às vizinhas da camada e pela LN (d − x); vale a pior
    barra. ponytail: 2 camadas → só a de baixo, sem limite pela camada de cima (Acri maior, a favor) e σs em d."""
    bc, hc, dc = b * 100, h * 100, d * 100
    x, I = estadio_II(bc, dc, As, ES / 15)
    sigma = 15 * M * 100 * (dc - x) / I * 10  # MPa
    br = escolher_barras(As, b, cob, fyk=fyk)
    phi = br["phi"] if br else (10.0 if fyk >= 600 else 32.0)
    n = math.ceil(br["n"] / br["camadas"]) if br else 2
    f, c_lat = phi / 10, (cob * 1000 + 5.0 + phi / 2) / 10  # cm; estribo de 5 mm como em escolher_barras
    s = (bc - 2 * c_lat) / (n - 1) if n > 1 else 2 * bc
    alt = min(7.5 * f, hc - dc) + min(7.5 * f, dc - x)
    larg = min(7.5 * f, c_lat) + min(7.5 * f, s / 2)  # barra de canto
    if n > 2:
        larg = max(larg, 2 * min(7.5 * f, s / 2))  # interna; maior Acri → menor ρri → maior wk
    rho = math.pi * f ** 2 / 4 / (larg * alt)
    return abertura_fissura(phi, sigma, rho, fck, eta1), sigma, phi


def verificar_barra_concreto(secao, material, elu, els, L, papel, params=None):
    """Verifica/dimensiona uma barra retangular de concreto armado (ESPEC §7.2).

    Adaptador de eixos (secoes.secao_retangular põe o eixo forte em Secao.Iy): b = largura,
    h = altura; My_local flete no plano de h (viga: My < 0 traciona a face inferior); Mz_local no
    plano de b; Vz_local é o cortante associado a My (bw = b, d = h − d'); Vy associado a Mz.
    elu: [{"combinacao", "linhas": [(x, N, Vy, Vz, T, My, Mz)]}] (kN, kN·m; N > 0 tração).
    els: {"qp": {"combinacao", "linhas", "flecha_elastica_m"} | None, "freq": {"combinacao", "linhas"} | None}.
    params: fyk_MPa (500), fywk_MPa (500), cobrimento_m (cnom da CAA), caa ("II"), d_m (h − c − φt − φl/2),
    le_x (flambagem no plano de h), le_y (plano de b) — sem eles, 15.6: menor entre l0_x + h (l0_y + b) e L,
    e L sem l0 —, alfa_b (1,0), elemento_desloc ("aceitabilidade"), t0_meses (1,0), emenda ("traspasse" |
    "luva", pilar: As,máx 4% | 8%), t_alvenaria_meses e fracao_qp_pos_alvenaria (1,0): flecha ativa para
    elemento_desloc "alvenaria"/"apos_construcao" (LC-017), com o total também contra L/250.
    Pilar com menor lado < 19 cm: esforços × γn (13.2.3); detalhes.gama_n.

    Flecha (viga, quase permanente): f = f_el · E·Ic/(Ecs·Ieq) · (1 + αf). O solver dá a flecha
    elástica f_el com a rigidez E·Ic da seção bruta (× params.fator_EI, se houver); a fissuração entra trocando Ic pela inércia
    equivalente de Branson Ieq = (Mr/Ma)³Ic + [1 − (Mr/Ma)³]·III ≤ Ic (17.3.2.1.1, com Ecs) e a
    fluência pelo fator (1 + αf), αf = Δξ/(1 + 50ρ') (17.3.2.1.2). Ma = máx |My| da quase permanente.

    Retorna ResultadoNorma + combinacao + armadura (ESPEC §4 e §7.2)."""
    params = params or {}
    pr = secao.props or {}
    if pr.get("tipo") != "retangular" or "b" not in pr or "h" not in pr:
        raise ValueError(f"concreto: só seção retangular (props tipo/b/h); seção '{secao.nome}' "
                         f"é '{pr.get('tipo')}'")
    if material.fck is None:
        raise ValueError(f"concreto: material '{material.nome}' sem fck")
    if papel not in ("viga", "pilar"):
        raise ValueError(f"concreto: papel '{papel}' inválido (viga | pilar)")
    if not elu:
        raise ValueError("concreto: nenhuma combinação ELU")
    b, h = float(pr["b"]), float(pr["h"])
    fck = material.fck / MPa
    p = parametros(fck)
    fyk = float(params.get("fyk_MPa", 500.0))
    fywk = float(params.get("fywk_MPa", 500.0))
    caa = str(params.get("caa", "II")).upper()
    if caa not in CNOM:
        raise ValueError(f"concreto: caa '{caa}' inválida (I, II, III, IV)")
    cob = float(params.get("cobrimento_m", CNOM[caa]))
    dl = cob + PHI_T + PHI_L / 2
    d = float(params.get("d_m", h - dl))
    if cob <= 0 or not 0 < d < h or 2 * dl >= min(b, h):
        raise ValueError(f"concreto: cobrimento {cob} m / d {d} m incompatíveis com a seção {b}×{h} m")
    fyd = fyk / GAMA_S / 10  # kN/cm²
    Ac = b * h * 1e4  # cm²
    ver, avisos, insuf = {}, [], [False]

    def guarda(criterio, taxa, item, comb, idx, **extra):
        """Razão de esforço; com extra ok=bool é verificação de conformidade (não define taxa_max)."""
        if criterio not in ver or taxa > ver[criterio]["taxa"]:
            ver[criterio] = dict(criterio=criterio, taxa=float(taxa), item=item, combinacao=comb,
                                 indice=idx, **extra)

    def aviso(txt):
        if txt not in avisos:
            avisos.append(txt)

    guarda("cobrimento", CNOM[caa] / cob, "7.4.7.2 (Tabela 7.2)", "", 0, exigido=CNOM[caa], adotado=cob,
           ok=cob >= CNOM[caa] - 1e-12)
    guarda("classe_concreto", FCK_MIN[caa] / fck, "7.4.2 (Tabela 7.1)", "", 0,
           exigido=float(FCK_MIN[caa]), adotado=fck, ok=fck >= FCK_MIN[caa] - 1e-12)
    bmin, gn = min(b, h), 1.0
    if papel == "pilar":  # 13.2.3 (LC-003/004) e 14.4.2.4 (LC-020)
        gn = gama_n(bmin)
        guarda("dimensao_minima_pilar", 0.14 / bmin, "13.2.3 (b ≥ 14 cm)", "", 0, exigido=0.14, adotado=bmin,
               ok=bmin >= 0.14 - 1e-12)
        guarda("area_minima_pilar", 360 / Ac, "13.2.3 (Ac ≥ 360 cm²)", "", 0, exigido=360.0, adotado=Ac,
               ok=Ac >= 360 - 1e-9)
        guarda("pilar_parede", max(b, h) / bmin / 5, "14.4.2.4 (h/b ≤ 5; pilar-parede fora do escopo)", "", 0,
               exigido=5.0, adotado=max(b, h) / bmin, ok=max(b, h) <= 5 * bmin + 1e-12)
        if gn > 1:
            aviso(f"pilar com menor dimensão {bmin * 100:.0f} cm < 19 cm: esforços de cálculo multiplicados por "
                  f"γn = {gn:.2f} (13.2.3, Tabela 13.1)")
            elu = [dict(cb, linhas=[(ln[0], *(gn * float(v) for v in ln[1:])) for ln in cb["linhas"]])
                   for cb in elu]
    else:  # 13.2.2 (VC-guia-006): bw ≥ 12 cm; 10 cm só em casos excepcionais
        guarda("largura_minima_viga", 0.10 / b, "13.2.2 (bw ≥ 12 cm; ≥ 10 cm excepcional)", "", 0,
               exigido=0.12, adotado=b, ok=b >= 0.10 - 1e-12)
        if 0.10 - 1e-12 <= b < 0.12 - 1e-12:
            aviso("viga com bw < 12 cm: só em casos excepcionais (13.2.2) — conferir alojamento das armaduras, "
                  "cobrimentos e lançamento/vibração do concreto")

    def cortante_torcao(nome, i, N, Vy, Vz, T, My, Mz):
        """Cortante nas duas direções e torção (17.4.2.2 modelo I, 17.5, 17.7.2). → (Asw/s total, Asl).
        N > 0 (tração relevante, decidida pelo chamador) zera Vc se a LN sai da seção (17.4.2.2 c)."""
        cz = cisalhamento(b, h - dl if papel == "pilar" else d, abs(Vz), fck, fywk, N=N, Md=My, h=h)
        guarda("cisalhamento_VRd2", abs(Vz) / cz["VRd2"], "17.4.2.2", nome, i,
               solicitante=abs(Vz), resistente=cz["VRd2"], Vc=cz["Vc"])
        cy = None
        if abs(Vy) > 1e-6:
            cy = cisalhamento(h, b - dl, abs(Vy), fck, fywk, N=N, Md=Mz, h=b)
            guarda("cisalhamento_VRd2_y", abs(Vy) / cy["VRd2"], "17.4.2.2", nome, i,
                   solicitante=abs(Vy), resistente=cy["VRd2"], Vc=cy["Vc"])
        asw_T, asl = 0.0, 0.0
        if abs(T) > 1e-6:
            t = torcao(b, h, T, fck, dl, Vd=abs(Vz), VRd2=cz["VRd2"], fywk=fywk)
            inter = t["interacao"]
            if cy:  # 17.7.2.2 também nas paredes do plano de b (Vy)
                inter = max(inter, t["taxa_T"] + abs(Vy) / cy["VRd2"])
            guarda("torcao_interacao", inter, t["item"], nome, i, solicitante=abs(T),
                   resistente=t["TRd2"], A90_s=t["A90_s"], Asl_ue=t["Asl_ue"], n_Asl_min=t["n_Asl_min"],
                   sl_max_m=t["sl_max_m"])
            for a in t["alertas"]:
                aviso(a)
            aviso("torção: Asl_torcao_cm2 deve ser somada à armadura de flexão na zona tracionada (17.7.1.2)")
            asw_T, asl = 2 * t["A90_s_calc"], t["Asl"]  # 2 ramos de A90/s
        # 17.7.2.3: soma das armaduras transversais calculadas; o mínimo (17.4.1.1.1 = 17.5.1.2,
        # ρsw·bw do estribo inteiro) vale para o total, sem somar mínimos
        asw = max(cz["Asw_s_calc"] + asw_T, cz["Asw_s_min"])
        if cy:
            asw = max(asw, cy["Asw_s_calc"] + asw_T, cy["Asw_s_min"])
        return asw, asl

    if papel == "viga":
        arm, extra = _viga(b, h, d, dl, cob, fck, fyk, fyd, Ac, p, elu, els or {}, L, params, material,
                           secao, guarda, aviso, insuf, cortante_torcao)
    else:
        arm, extra = _pilar(b, h, dl, fck, fyk, fyd, Ac, elu, L, params, guarda, aviso, insuf,
                            cortante_torcao)
    # 007 (ESPEC §7.2): taxa_max = pior razão de esforço; conformidade (cobrimento, classe, Mz fora do
    # escopo) só aprova/reprova. Sem razão de esforço: taxa_max = 0 e critério da 1ª conformidade.
    esf = sorted((v for v in ver.values() if "ok" not in v), key=lambda v: -v["taxa"])
    conf = sorted((v for v in ver.values() if "ok" in v), key=lambda v: (v["ok"], -v["taxa"]))
    lista = esf + conf
    det = esf[0] if esf else dict(conf[0], taxa=0.0)
    pto = next((v for v in lista if v["combinacao"]), det)
    ok = (all(v["taxa"] <= 1 + 1e-9 for v in esf) and all(v["ok"] for v in conf) and not insuf[0])
    detalhes = dict(edicao=EDICAO, papel=papel, b_m=b, h_m=h, d_m=d, d_linha_m=dl, fck_MPa=fck,
                    fyk_MPa=fyk, caa=caa, cobrimento_m=cob, gama_n=gn, verificacoes=lista, **extra)
    return dict(taxa_max=det["taxa"], criterio=det["criterio"], item=det["item"], ok=ok,
                indice=pto["indice"], combinacao=pto["combinacao"], detalhes=detalhes,
                avisos=avisos, armadura=arm)


def _viga(b, h, d, dl, cob, fck, fyk, fyd, Ac, p, elu, els, L, params, material, secao,
          guarda, aviso, insuf, cortante_torcao):
    amin = as_min_flexao(b, h, d, fck, fyk)  # 17.3.5.2.1 (Md,mín; LC-001)
    As_min = amin["As_min"]
    As_max = 0.04 * Ac  # 17.3.5.2.4
    # critério adotado (sem item na NBR 6118): compressão até 0,1·fcd·Ac é desprezada na flexão (aumenta o
    # momento resistente nos domínios 2/3, logo desprezá-la é a favor da segurança). Tração nunca é desprezada.
    lim_N = 0.1 * p["fcd"] * Ac  # kN
    As_pos = As_neg = As2 = Asw = Asl = 0.0
    tem_neg, Mmax = False, 0.0
    for comb in elu:
        Mmax = max([Mmax] + [abs(float(ln[5])) for ln in comb["linhas"]])
    for comb in elu:
        nome = comb["combinacao"]
        for i, ln in enumerate(comb["linhas"]):
            _, N, Vy, Vz, T, My, Mz = map(float, ln)
            Md, As2_pt, As_op = abs(My), 0.0, 0.0
            if N < -lim_N:  # compressão relevante: flexão composta, armadura simétrica
                aviso("viga com |N| > 0,1·fcd·Ac: flexo-compressão com armadura simétrica (17.2.2); "
                      "As_cm2/As_neg_cm2 e As2_cm2 recebem metade cada (faces tracionada e comprimida); "
                      "x/d de 14.6.4.3 não verificado nesse ramo")
                Ast = flexo_compressao(b, h, -N, Md, fck, fyk, dl)
                if Ast is None:
                    insuf[0] = True
                    aviso("viga: seção insuficiente à flexo-compressão (As > 8% Ac)")
                    guarda("As_max", INSUFICIENTE, "17.3.5.2.4", nome, i)
                    continue
                As_face = As2_pt = Ast / 2  # simétrica: a face comprimida também é armadura (17.2.2)
            else:
                if N > 1e-6:  # 17.2.2: flexo-tração para qualquer tração (1e-6 kN = só o zero numérico)
                    fl = flexo_tracao(b, h, d, N, Md, fck, fyk, d2=dl)
                    As_op = fl["As_oposta"]
                    aviso(f"viga com tração axial: flexo-tração de {fl['excentricidade']} excentricidade (17.2.2)"
                          + (", armadura tracionada nas duas faces" if As_op > 0 else ", As = As(Ms) + N/fyd"))
                else:
                    fl = flexao_simples(b, d, Md, fck, fyk, d2=dl)
                As_face, As2_pt = fl["As"], fl["As2"]
                if fl["x_d"] is not None:  # pequena excentricidade: domínio 1, sem concreto comprimido
                    guarda("x_d", fl["x_d"] / p["xd_lim"], "14.6.4.3", nome, i, x_d=fl["x_d"],
                           x_d_lim=p["xd_lim"], dominio=fl["dominio"], Md=Md)
            if My <= 0:
                As_pos, As_neg = max(As_pos, As_face), max(As_neg, As_op)
                tem_neg = tem_neg or As_op > 0
            else:
                As_neg, As_pos = max(As_neg, As_face), max(As_pos, As_op)
                tem_neg = tem_neg or Md > 1e-6
            As2 = max(As2, As2_pt)
            guarda("As_max", (max(As_face, As_min) + As2_pt + As_op) / As_max, "17.3.5.2.4", nome, i,
                   As=max(As_face, As_min), As2=As2_pt, As_oposta=As_op, As_max=As_max)
            asw, asl = cortante_torcao(nome, i, N, Vy, Vz, T, My, Mz)
            Asw, Asl = max(Asw, asw), max(Asl, asl)
            lim_Mz = max(0.05 * Mmax, 0.1)
            if abs(Mz) > lim_Mz:  # ESPEC §6.5: fora do escopo não sai ok silencioso
                aviso("viga com Mz (plano de b) não verificado à flexão: use papel 'pilar' (flexão oblíqua)")
                guarda("flexao_Mz_nao_verificada", abs(Mz) / lim_Mz, "17.2.5 (fora do escopo de 'viga')",
                       nome, i, Mz=abs(Mz), ok=False)
    As_pos = max(As_pos, As_min)
    if tem_neg:
        As_neg = max(As_neg, As_min)
    # 18.3.3.2 (LC-008): espaçamentos máximos dos estribos pelo maior Vd; ramos para st ≤ st,máx
    Vmax = max(abs(float(ln[3])) for cb in elu for ln in cb["linhas"])
    cz = cisalhamento(b, d, Vmax, fck)
    st2 = b - 2 * cob - PHI_T  # entre eixos dos 2 ramos
    guarda("estribos", PHI_T * 1000 / cz["phi_t_max_mm"], "18.3.3.2", "", 0, Vd_max=Vmax, VRd2=cz["VRd2"],
           s_max_m=cz["s_max_m"], st_max_m=cz["st_max_m"], n_ramos=max(2, math.ceil(st2 / cz["st_max_m"] - 1e-9) + 1),
           phi_t_mm=PHI_T * 1000, phi_t_max_mm=cz["phi_t_max_mm"], ok=PHI_T * 1000 <= cz["phi_t_max_mm"] + 1e-9)
    pele = armadura_pele(b, h, d)  # 17.3.5.2.3 (LC-007)
    if pele:
        guarda("armadura_pele", pele["As_face_cm2"] / pele["As_ef_face_cm2"], pele["item"], "", 0,
               ok=pele["As_ef_face_cm2"] >= pele["As_face_cm2"] - 1e-9,
               **{k: v for k, v in pele.items() if k != "item"})
    # ELS-W: fissuração com a combinação frequente (17.3.3.2, Tabela 13.4)
    caa = str(params.get("caa", "II")).upper()
    eta1 = 2.25 if fyk == 500 else 1.0  # Tabela 8.2 (2023): CA-50 2,25; CA-25 e CA-60 1,0
    fr = els.get("freq")
    if fr and fr.get("linhas"):
        for i, ln in enumerate(fr["linhas"]):
            My = float(ln[5])
            As_f = As_pos if My <= 0 else As_neg
            if abs(My) < 1e-9 or As_f <= 0:
                continue
            wk, sigma, phi = _wk(abs(My), As_f, b, h, d, cob, fck, eta1, fyk)
            guarda("fissuracao_wk", wk / WK_LIM[caa], "17.3.3.2 (Tabela 13.4)", fr["combinacao"], i,
                   solicitante=wk, resistente=WK_LIM[caa], sigma_s_MPa=sigma, phi_mm=phi)
    # ELS-DEF: flecha com a combinação quase permanente (17.3.2.1, Tabela 13.3)
    qp = els.get("qp")
    extra = {}
    if qp and qp.get("linhas") and qp.get("flecha_elastica_m") is not None:
        ms = [float(ln[5]) for ln in qp["linhas"]]
        i_ma = max(range(len(ms)), key=lambda k: abs(ms[k]))
        Ma = abs(ms[i_ma])
        As_f = As_pos if ms[i_ma] <= 0 else As_neg
        Ic = secao.Iy  # m⁴ (a mesma do solver)
        Ecs = p["Ecs"] * 1e4  # kN/m²
        Mr = 1.5 * p["fctm"] * 1e4 * Ic / (h / 2)  # 17.3.1 (α = 1,5, fct = fctm), kN·m
        _, I2 = estadio_II(b * 100, d * 100, As_f, p["Ecs"])  # αe = Es/Ecs (17.3.2.1.1)
        I2 *= 1e-8

        def ieq(M):  # Branson (17.3.2.1.1)
            r3 = min(1.0, (Mr / M) ** 3) if M > 0 else 1.0
            return min(Ic, r3 * Ic + (1 - r3) * I2)
        Ieq = ieq(Ma)
        t0 = float(params.get("t0_meses", 1.0))
        af = (2.0 - xi(t0)) / (1 + 50 * 0.0)  # ρ' = 0 (a favor da segurança)
        fator = float(params.get("fator_EI", 1.0))  # o solver usou E·(fator·Ic): desfaz antes de Branson
        fel = abs(float(qp["flecha_elastica_m"])) * material.E * fator / Ecs  # flecha elástica com Ecs·Ic
        f = fel * Ic / Ieq * (1 + af)
        elem = params.get("elemento_desloc") or "aceitabilidade"
        lim = limite_deslocamento(elem, L)
        dados = dict(Ma=Ma, Mr=Mr, Ieq_m4=Ieq, Ic_m4=Ic, alfa_f=af, fator_EI=fator)
        t_alv = params.get("t_alvenaria_meses")
        if t_alv is not None and elem in ("alvenaria", "apos_construcao"):
            # LC-017: limite "após a construção" da Tabela 13.3 vale para a flecha ativa; a parcela fr da carga
            # quase permanente aplicada em t_alv (padrão 1: toda, o maior valor ativo possível) entra pelo
            # incremento de Branson fi(Ma) − fi((1 − fr)·Ma)
            fr = float(params.get("fracao_qp_pos_alvenaria", 1.0))
            if not 0.0 <= fr <= 1.0:
                raise ValueError(f"concreto: fracao_qp_pos_alvenaria = {fr} fora de 0..1")
            fi_tot = fel * Ic / Ieq
            fi_a = fel * (1 - fr) * Ic / ieq((1 - fr) * Ma) if fr < 1 else 0.0
            fa = flecha_ativa(fi_a, fi_tot - fi_a, t0, float(t_alv))
            f = fa["f_ativa"]
            lt = limite_deslocamento("aceitabilidade", L)  # o total continua limitado a L/250
            guarda("flecha_total", fa["f_inf"] / lt["limite_m"], f"17.3.2.1 / {lt['item']}", qp["combinacao"], i_ma,
                   solicitante=fa["f_inf"], resistente=lt["limite_m"], razao=lt["razao"], **dados)
            dados.update(f_ativa_m=f, f_inf_m=fa["f_inf"], f_ate_alvenaria_m=fa["f_ate_t"],
                         t_alvenaria_meses=float(t_alv), fracao_qp_pos_alvenaria=fr)
        guarda("flecha", f / lim["limite_m"], f"17.3.2.1 / {lim['item']}", qp["combinacao"], i_ma,
               solicitante=f, resistente=lim["limite_m"], razao=lim["razao"], **dados)
        extra["flecha"] = dict(f_m=f, limite_m=lim["limite_m"], razao=lim["razao"], estado=lim["estado"])
    br = escolher_barras(As_pos, b, cob, fyk=fyk)
    if br is None:
        aviso("barras da armadura positiva não cabem em 2 camadas — aumentar bw (18.3.2.2)")
    arm = dict(As_cm2=As_pos, As2_cm2=As2, As_neg_cm2=As_neg, Asw_s_cm2_m=Asw, Asl_torcao_cm2=Asl,
               barras=f"{br['n']} ϕ {br['phi']:g}" if br else None, As_pilar_cm2=None)
    extra["As_min_cm2"], extra["As_max_cm2"], extra["Md_min_kNm"] = As_min, As_max, amin["Md_min"]
    return arm, extra


def _pilar(b, h, dl, fck, fyk, fyd, Ac, elu, L, params, guarda, aviso, insuf, cortante_torcao):
    # 15.6 (LC-018): le informado prevalece (balanço, nós móveis...); senão menor entre l0 + h e L; sem l0, L
    def _le(eixo, hh):
        if params.get(f"le_{eixo}") is not None:
            return float(params[f"le_{eixo}"])
        l0 = params.get(f"l0_{eixo}")
        return comprimento_equivalente(L, None if l0 is None else float(l0), hh)["le"]
    lex, ley = _le("x", h), _le("y", b)
    ab = _alfas_b(params.get("alfa_b", 1.0))  # 15.8.2 (valida também o pilar só tracionado)
    alfa_b = ab[0] if ab[0] == ab[1] else list(ab)
    emenda = str(params.get("emenda", "traspasse"))
    As_max = _as_max_pilar(emenda) * Ac  # 17.3.5.3.2: 4% fora da emenda por traspasse (LC-005), 8% com luva
    combs = [(c["combinacao"], [tuple(map(float, ln)) for ln in c["linhas"]]) for c in elu]

    def rodada(nb, nh):
        """Flexão de todas as combinações com a disposição (nb, nh). → (nova disposição, None) se alguma
        combinação pediu mais barras; senão (None, (registros, avisos, As_nec, det))."""
        regs, avs, As_nec, det = [], [], 0.0, None
        n = 2 * nb + 2 * nh
        # ponytail: combinações mais solicitadas primeiro (Nd + 2·(Mx/h + My/b)); as já cobertas pela maior As
        # são puladas (_pilar_atende) — só registram λ. Os registros saem na ordem original.
        ordem = sorted(range(len(combs)), key=lambda k: -max(-ln[1] + 2 * (abs(ln[5]) / h + abs(ln[6]) / b)
                                                              for ln in combs[k][1]))
        saida = {}
        for k in ordem:
            nome, lns = combs[k]
            regs, avs = saida.setdefault(k, ([], []))
            Ns = [ln[1] for ln in lns]
            i_N = min(range(len(Ns)), key=lambda k: Ns[k])  # mais comprimido (N < 0)
            Nd = -Ns[i_N]
            Mx, My = max(abs(ln[5]) for ln in lns), max(abs(ln[6]) for ln in lns)
            if Nd <= 0:  # pilar tracionado em todos os pontos: tirante + binário (a favor da segurança)
                i_N = max(range(len(Ns)), key=lambda k: Ns[k])
                Nt = Ns[i_N]
                # barra de canto (seção fissurada, n barras iguais): Nt/n + Mx/(zx·nb) + My/(zy·(nh + 2))
                As = (Nt + n * Mx / ((h - 2 * dl) * nb) + n * My / ((b - 2 * dl) * (nh + 2))) / fyd
                As = max(As, 0.004 * Ac)  # 17.3.5.3.1 (0,15·Nd/fyd não governa: Nd = 0)
                avs.append("pilar sob tração: dimensionado como tirante, As = n·(Nt/n + Mx/(zx·nb) + "
                           "My/(zy·(nh + 2)))/fyd ≥ 0,4% Ac (4 cantos: Nt + 2·Mx/zx + 2·My/zy)")
                regs.append(("As_pilar_max", As / As_max, "17.3.5.3.2", nome, i_N, dict(As=As, As_max=As_max)))
                As_nec = max(As_nec, As)
                continue
            if As_nec > 0:
                dirs, _ = _momentos_pilar(b, h, Nd, Mx, My, lex, ley, fck, ab)
                if _pilar_atende(b, h, Nd, Mx, My, dirs, As_nec, fck, fyk, dl, nb, nh):
                    lam = max(v["lam"] for v in dirs.values())
                    regs.append(("esbeltez", lam / LAMBDA_MAX, "15.8.3.3 (λ ≤ 90)", nome, i_N, dict(lam=lam)))
                    continue
            r = dimensionar_pilar(b, h, Nd, Mx, My, L, fck, fyk, lex=lex, ley=ley, dl=dl, alfa_b=ab, nb=nb, nh=nh,
                                  emenda=emenda)
            if (r.get("nb", nb), r.get("nh", nh)) != (nb, nh):
                return (r["nb"], r["nh"]), None
            avs += r["alertas"]
            lam = max(v["lam"] for v in r["direcoes"].values())
            regs.append(("esbeltez", lam / LAMBDA_MAX, "15.8.3.3 (λ ≤ 90)", nome, i_N, dict(lam=lam)))
            if "As" not in r:
                regs.append(("As_pilar_max", INSUFICIENTE, "17.3.5.3.2", nome, i_N, {}))
                det = det or r
            else:
                regs.append(("As_pilar_max", r["As"] / As_max, "17.3.5.3.2", nome, i_N,
                             dict(As=r["As"], As_min=r["As_min"], As_max=As_max, nu=r["nu"])))
                if det is None or r["As"] >= det.get("As", 0.0):  # compressão determinante
                    det = r
                As_nec = max(As_nec, r["As"])
        regs = [x for k in sorted(saida) for x in saida[k][0]]
        avs = [x for k in sorted(saida) for x in saida[k][1]]
        return None, (regs, avs, As_nec, det)

    disp = _disposicao_minima(b, h, dl)  # 18.4.2.2: espaçamento máximo (LC-006)
    while True:
        nova, res = rodada(*disp)
        if nova:
            disp = nova
            continue
        regs, avs, As_nec, det = res
        cabe = As_nec <= 0.08 * Ac and all(r[1] != INSUFICIENTE for r in regs)  # cabe (detalha mesmo > 4%)
        phi = _bitola_pilar(As_nec, b, h, dl, *disp) if cabe else None
        if phi is None and cabe:
            prox = _proxima_disposicao(b, h, dl, *disp)
            if prox:  # a maior As (outra combinação ou tirante) pede mais barras: refaz tudo
                disp = prox
                continue
        break
    for crit, taxa, item, nome, i, extra in regs:
        guarda(crit, taxa, item, nome, i, **extra)
        if taxa == INSUFICIENTE:
            insuf[0] = True
    for a in avs:
        aviso(a)
    n_bar = 2 * disp[0] + 2 * disp[1]
    if phi is None and cabe:
        insuf[0] = True
        aviso("pilar: nenhuma disposição de barras atende o As calculado com φl de 10 mm a b/8 e o "
              "espaçamento livre mínimo (18.4.2.1, 18.4.2.2): aumentar a seção")
        guarda("barras_pilar", 1.0, "18.4.2.1, 18.4.2.2", "", 0, As=As_nec, ok=False)
    Asw = Asl = 0.0
    for nome, lns in combs:
        for i, ln in enumerate(lns):
            asw, asl = cortante_torcao(nome, i, ln[1], ln[2], ln[3], ln[4], ln[5], ln[6])
            Asw, Asl = max(Asw, asw), max(Asl, asl)
    if As_nec > 0.04 * Ac and emenda == "luva":
        aviso("As > 4% Ac aceito só com emendas por luva/solda (params emenda = 'luva', 17.3.5.3.2)")
    arm = dict(As_cm2=None, As2_cm2=None, As_neg_cm2=None, Asw_s_cm2_m=Asw, Asl_torcao_cm2=Asl,
               barras=f"{n_bar} ϕ {phi:g}" if phi else None, As_pilar_cm2=As_nec)
    return arm, dict(pilar=det, le_x_m=lex, le_y_m=ley, alfa_b=alfa_b, n_barras=n_bar, nb=disp[0], nh=disp[1],
                     phi_l_mm=phi, emenda=emenda)
