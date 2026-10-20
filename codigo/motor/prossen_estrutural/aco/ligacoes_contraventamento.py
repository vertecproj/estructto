"""Ligações de contraventamento e de treliça — NBR 8800:2008, cap. 5 (barra tracionada) e 6 (ligações).

Tipos (contrato em docs/v3/LIGACOES-CONTRATO.md; detalhes em docs/v3/ligacoes/IMPL-CONTRAVENTAMENTO.md):
  gusset_contraventamento     diagonal (L, 2L, U, tubo, barra redonda) → gusset → viga/pilar/base
  no_trelica_gusset           nó de treliça: até 4 barras (diagonais, montante, banzo emendado) num gusset
  tirante_barra_redonda       barra redonda rosqueada com porca, ou com garfo e pino no gusset
  cantoneira_simples_trelica  cantoneira simples ligada por uma aba em treliça leve (excentricidade → E.1.4)

Unidades das entradas: mm, kN, graus; os cálculos usam as primitivas de `ligacoes.py` (m, kN, kN/m²).
Esforços de cálculo (ELU) na ponta da barra: N > 0 tração. Fontes dos exemplos de teste:
  [PFEIL] W. Pfeil e M. Pfeil, "Estruturas de aço: dimensionamento prático", 8ª ed., LTC — 2.3.5 (p.57),
          3.5.2 (p.79–81), 8.7.1 (p.233–239).
  [KULAK] Kulak, Fisher & Struik, "Guide to Design Criteria for Bolted and Riveted Joints", 2ª ed., cap. 15
          (Whitmore: espraiamento de 30°; bloco de cisalhamento no gusset).
  [AISC]  AISC Steel Construction Manual, parte 13 (método da força uniforme, fator de ductilidade 1,25 nas
          soldas da interface) e parte 9 (flambagem do gusset pelo comprimento de Thornton, K = 0,65).
Itens da NBR 8800:2008 marcados [confirmar] não foram conferidos no texto da norma (ver IMPL-CONTRAVENTAMENTO.md).
"""
from __future__ import annotations

import math

from .. import materiais, secoes
from ..modelo import MPa
from . import ligacoes as L
from .nbr8800 import GA1, GA2, compressao

MM = 1e-3
TAN30 = math.tan(math.radians(30))
SECOES = ["L", "2L", "U", "tubo_circular", "tubo_retangular", "barra_redonda"]
APOIOS = ["viga-pilar", "pilar", "viga", "base"]
N_MINIMO = 45.0             # kN — NBR 8800 6.1 [confirmar subitem]; Pfeil p.232
FOLGA_RASGO = 2.0           # mm — rasgo do tubo = t_gusset + 2 mm. ponytail: valor usual de fábrica
ACOS = list(materiais.ACOS)
_PRE = "NBR 8800 "


# ---------------------------------------------------------------- funções puras (SI: m, kN, kN/m²)

def coef_Ct(secao, ec, lc, D=None):
    """NBR 8800 5.2.5 → (Ct, nota). Perfis abertos e barra: Ct = 1 − ec/lc com teto 0,90; Ct < 0,60 volta como
    calculado e a nota pede outra ligação. Tubo circular (5.2.5 c): lc ≥ 1,3D → 1; D ≤ lc < 1,3D → 1 − ec/lc com
    ec = D/π; lc < D → None. Tubo retangular (5.2.5 d): lc ≥ H → 1 − ec/lc (ec do chamador); lc < H → None."""
    if lc <= 0:
        return None, "lc = 0: sem comprimento de ligação na direção da força"
    if secao == "tubo_circular":
        if lc >= 1.3 * D - 1e-12:
            return 1.0, ""
        if lc >= D - 1e-12:
            return 1 - (D / math.pi) / lc, ""
        return None, f"tubo circular com lc = {lc / MM:.0f} mm < D = {D / MM:.0f} mm: fora da 5.2.5 c)"
    if secao == "tubo_retangular":
        if lc < D - 1e-12:
            return None, f"tubo retangular com lc = {lc / MM:.0f} mm < H = {D / MM:.0f} mm: fora da 5.2.5 d)"
        return 1 - ec / lc, ""
    ct = min(1 - ec / lc, 0.90)
    return ct, ("Ct < 0,60: aumente o comprimento da ligação" if ct < 0.60 else "")


def largura_whitmore(largura_inicial, lc):
    """Seção de Whitmore [KULAK 15.2, fig. 15.4; PFEIL p.238]: largura no início da ligação + 2·lc·tg 30°."""
    return largura_inicial + 2 * lc * TAN30


def flambagem_chapa(Ag, t, KL, fy, E=materiais.E_ACO):
    """Chapa de ligação comprimida — NBR 8800 6.5.4: KL/r ≤ 25 → Ag·fy/γa1; senão 5.3 com Q = 1 (chapa
    contida pelas barras; r = t/√12)."""
    r = t / math.sqrt(12)
    esb = KL / r
    l0 = esb / math.pi * math.sqrt(fy / E)
    if esb <= 25:
        chi, item = 1.0, "6.5.4"
    else:
        chi, item = (0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2), "6.5.4 → 5.3.3"
    return dict(KL_r=esb, lambda0=l0, chi=chi, NcRd=chi * Ag * fy / GA1, item=item)


def forca_uniforme(P, theta, eb, ec, Lh, Lv):
    """Método da força uniforme [AISC parte 13], caso especial: β = β̄ = Lv/2 e α pela condição
    α − β·tgθ = eb·tgθ − ec; α ≠ ᾱ = Lh/2 → momento Mb = Vb·|α − ᾱ| na interface gusset–viga.
    θ: ângulo da diagonal com o pilar (rad). Retorna forças nas interfaces (mesmas unidades de P)."""
    tg = math.tan(theta)
    beta, alfa_b = Lv / 2, Lh / 2
    alfa = eb * tg - ec + beta * tg
    r = math.hypot(alfa + ec, beta + eb)
    Hc, Vc, Hb, Vb = ec / r * P, beta / r * P, alfa / r * P, eb / r * P
    return dict(alfa=alfa, beta=beta, alfa_barra=alfa_b, r=r, Hc=Hc, Vc=Vc, Hb=Hb, Vb=Vb,
                Mb=abs(Vb * (alfa - alfa_b)))


def filetes_balanceados(F, ec, aba, FwRd):
    """Filetes longitudinais de cantoneira com momento nulo no centroide [PFEIL 4.6.3 e 8.7.1]: o filete do
    canto (l1) leva F·(aba − ec)/aba e o da borda (l2) F·ec/aba; FwRd por unidade de comprimento."""
    return F * (aba - ec) / aba / FwRd, F * ec / aba / FwRd


def _hull(pts):
    """Envoltória convexa (cadeia monótona); anti-horária."""
    pts = sorted(set((round(x, 9), round(y, 9)) for x, y in pts))
    if len(pts) < 3:
        return pts

    def lado(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    inf, sup = [], []
    for p in pts:
        while len(inf) >= 2 and lado(inf[-2], inf[-1], p) <= 0:
            inf.pop()
        inf.append(p)
    for p in reversed(pts):
        while len(sup) >= 2 and lado(sup[-2], sup[-1], p) <= 0:
            sup.pop()
        sup.append(p)
    return inf[:-1] + sup[:-1]


# ---------------------------------------------------------------- esquema e validação

def _e(rotulo, unidade, padrao, **kw):
    return dict(rotulo=rotulo, unidade=unidade, padrao=padrao, **kw)


_COMUNS = {
    "d_mm": _e("Diâmetro do parafuso", "mm", 16.0, min=10.0, max=40.0),
    "tipo_parafuso": _e("Tipo de parafuso", "-", "A325", opcoes=list(L.PARAFUSOS)),
    "rosca_no_plano": _e("Rosca no plano de corte", "-", True),
    "eletrodo": _e("Eletrodo", "-", "E70", opcoes=list(L.ELETRODOS)),
    "t_gusset": _e("Espessura do gusset", "mm", None, min=4.0, max=50.0),
    "aco_gusset": _e("Aço do gusset", "-", "A-36", opcoes=ACOS),
    "borda_laminada": _e("Bordas laminadas/maçarico", "-", False),
    "deformacao_limitada": _e("Deformação limitada", "-", True),
}


def _campos_membro(s="", rot="", obrig=True, secoes=SECOES, theta=None):
    req = None if obrig else 0.0
    c = {
        f"secao{s}": _e(f"Seção{rot}", "-", secoes[0] if obrig else "nenhum",
                        opcoes=list(secoes) + ([] if obrig else ["nenhum"])),
        f"dim_a{s}": _e(f"Aba ligada/altura/D{rot}", "mm", req, min=0.0, max=1000.0,
                        ajuda="aba ligada (L, 2L), altura da alma (U), diâmetro (tubo circular, barra), "
                                  "H no plano do gusset (tubo retangular)"),
        f"dim_b{s}": _e(f"Outra aba/mesa/B{rot}", "mm", 0.0, min=0.0, max=1000.0,
                        ajuda="outra aba (L), largura da mesa (U), B (tubo retangular); 0 = igual a dim_a"),
        f"t_perfil{s}": _e(f"Espessura do perfil{rot}", "mm", 0.0, min=0.0, max=60.0,
                           ajuda="aba (L), alma (U) ou parede (tubo); barra redonda: não usada"),
        f"tf_perfil{s}": _e(f"Espessura da mesa do U{rot}", "mm", 0.0, min=0.0, max=60.0,
                            ajuda="0 = igual a t_perfil"),
        f"A_perfil{s}": _e(f"Área de 1 perfil{rot}", "mm²", 0.0, min=0.0, max=1e6,
                           ajuda="0 = calculada (cantos vivos); informe a do catálogo"),
        f"xg_perfil{s}": _e(f"Excentricidade ec{rot}", "mm", 0.0, min=0.0, max=500.0,
                            ajuda="face de contato ao centroide (5.2.5); 0 = calculada"),
        f"aco_perfil{s}": _e(f"Aço do perfil{rot}", "-", "A-36", opcoes=ACOS),
        f"ligacao{s}": _e(f"Ligação ao gusset{rot}", "-", "parafusada", opcoes=["parafusada", "soldada"]),
        f"n_linhas{s}": _e(f"Linhas de parafusos{rot}", "-", 1, min=1, max=6),
        f"n_por_linha{s}": _e(f"Parafusos por linha{rot}", "-", 2, min=1, max=20),
        f"s{s}": _e(f"Espaçamento s{rot}", "mm", 50.0, min=0.0, max=500.0),
        f"g{s}": _e(f"Gabarito entre linhas{rot}", "mm", 60.0, min=0.0, max=500.0),
        f"g_aba{s}": _e(f"Gabarito à costa da aba{rot}", "mm", 0.0, min=0.0, max=500.0,
                        ajuda="linha de furos mais próxima do canto da cantoneira; 0 = dim_a/2"),
        f"e1{s}": _e(f"Borda na ponta do perfil{rot}", "mm", 30.0, min=0.0, max=500.0),
        f"e_gusset{s}": _e(f"Borda no gusset{rot}", "mm", 30.0, min=0.0, max=500.0),
        f"l1{s}": _e(f"Filete 1 (canto){rot}", "mm", 100.0, min=0.0, max=2000.0),
        f"l2{s}": _e(f"Filete 2 (borda){rot}", "mm", 0.0, min=0.0, max=2000.0,
                     ajuda="0 = igual a l1"),
        f"b_solda{s}": _e(f"Perna do filete{rot}", "mm", 5.0, min=0.0, max=30.0),
        f"recuo{s}": _e(f"Recuo ponto de trabalho{rot}", "mm", 0.0, min=0.0, max=3000.0,
                        ajuda="do ponto de trabalho à ponta do perfil; 0 = automático"),
    }
    if theta is not None:
        c[f"theta{s}"] = _e(f"Ângulo com o banzo{rot} (°)", "-", theta, min=-360.0, max=360.0)
    return c


_GUSSET = {
    **_campos_membro(),
    **_COMUNS,
    "apoio": _e("Apoio do gusset", "-", "viga-pilar", opcoes=APOIOS),
    "theta": _e("Ângulo com o pilar (°)", "-", 45.0, min=5.0, max=85.0),
    "eb": _e("Ponto de trabalho à viga", "mm", 150.0, min=0.0, max=2000.0,
             ajuda="meia altura da viga (eb); apoio pilar: início do gusset a partir do ponto de trabalho"),
    "ec": _e("Ponto de trabalho ao pilar", "mm", 150.0, min=0.0, max=2000.0,
             ajuda="meia altura do pilar (ec); apoio viga/base: início do gusset a partir do ponto de trabalho"),
    "Lh": _e("Interface gusset–viga", "mm", 0.0, min=0.0, max=3000.0, ajuda="0 = automático"),
    "Lv": _e("Interface gusset–pilar", "mm", 0.0, min=0.0, max=3000.0, ajuda="0 = automático"),
    "K_gusset": _e("K do gusset", "-", 0.65, min=0.5, max=2.0),
    "L_flambagem": _e("Comprimento de Thornton", "mm", 0.0, min=0.0, max=2000.0, ajuda="0 = calculado"),
    "b_interface": _e("Filete da interface", "mm", 6.0, min=2.0, max=30.0),
    "fator_ductilidade": _e("Fator de ductilidade", "-", 1.25, min=1.0, max=1.4),
}

_NO = {
    **_campos_membro("_1", " 1", secoes=["2L", "L", "U", "tubo_circular", "tubo_retangular"], theta=45.0),
    **_campos_membro("_2", " 2", obrig=False, secoes=["2L", "L", "U", "tubo_circular", "tubo_retangular"],
                     theta=135.0),
    **_campos_membro("_3", " 3", obrig=False, secoes=["2L", "L", "U", "tubo_circular", "tubo_retangular"],
                     theta=0.0),
    **_campos_membro("_4", " 4", obrig=False, secoes=["2L", "L", "U", "tubo_circular", "tubo_retangular"],
                     theta=180.0),
    **_COMUNS,
    "banzo": _e("Banzo", "-", "continuo", opcoes=["continuo", "emendado"]),
    "e_banzo": _e("Eixo do banzo à face", "mm", 0.0, min=0.0, max=1000.0,
                  ajuda="do eixo do banzo à borda soldada do gusset (banzo contínuo)"),
    "L_interface": _e("Solda gusset–banzo", "mm", 0.0, min=0.0, max=3000.0, ajuda="0 = automático"),
    "b_interface": _e("Filete da interface", "mm", 5.0, min=2.0, max=30.0),
    "K_gusset": _e("K do gusset", "-", 0.65, min=0.5, max=2.0),
    "fator_ductilidade": _e("Fator de ductilidade", "-", 1.0, min=1.0, max=1.4),
}

_TIRANTE = {
    "D_barra": _e("Diâmetro da barra", "mm", None, min=6.0, max=100.0),
    "aco_barra": _e("Aço da barra", "-", "A-36", opcoes=ACOS),
    "extremidade": _e("Extremidade", "-", "rosca_porca", opcoes=["rosca_porca", "garfo_pino"]),
    "t_chapa": _e("Espessura da chapa", "mm", None, min=3.0, max=60.0,
                  ajuda="chapa/aba onde apoia a porca, ou gusset do pino"),
    "aco_chapa": _e("Aço da chapa", "-", "A-36", opcoes=ACOS),
    "largura_chapa": _e("Largura da chapa no furo", "mm", 0.0, min=0.0, max=1000.0,
                        ajuda="garfo: largura do gusset na seção do pino; 0 = 4·d do pino"),
    "borda_pino": _e("Borda além do pino", "mm", 0.0, min=0.0, max=500.0,
                     ajuda="garfo: centro do furo à borda na direção da força; 0 = 2·d do pino"),
    "d_pino": _e("Diâmetro do pino", "mm", 0.0, min=0.0, max=100.0, ajuda="garfo: 0 = D_barra"),
    "tipo_pino": _e("Pino (especificação)", "-", "A325", opcoes=list(L.PARAFUSOS)),
    "comprimento": _e("Comprimento do tirante", "mm", 0.0, min=0.0, max=1e5),
    "borda_laminada": _e("Bordas laminadas/maçarico", "-", False),
    "deformacao_limitada": _e("Deformação limitada", "-", True),
}

_CANT = {
    **_campos_membro(secoes=["L"]),
    **_COMUNS,
    "trelica": _e("Treliça", "-", "plana", opcoes=["plana", "espacial"]),
    "comprimento": _e("Comprimento da barra", "mm", 0.0, min=0.0, max=1e5,
                      ajuda="entre pontos de trabalho; > 0 verifica a compressão por E.1.4"),
    "fadiga": _e("Sujeita à fadiga", "-", False),
}


def _ler(entradas, dados, tipo):
    """Valida `dados` contra `entradas` → dict com padrões aplicados. Erros juntos em ValueError."""
    dados, out, erros = dict(dados or {}), {}, []
    for k, c in entradas.items():
        v = dados.get(k, c["padrao"])
        pad = c["padrao"]
        if v is None or v == "":
            if pad is None:
                erros.append(f"{k}: obrigatório")
            continue
        if "opcoes" in c:
            achado = next((o for o in c["opcoes"] if str(o).upper() == str(v).upper()), None)
            if achado is None:
                erros.append(f"{k}: '{v}' inválido ({', '.join(map(str, c['opcoes']))})")
            out[k] = achado
        elif isinstance(pad, bool):
            out[k] = v if isinstance(v, bool) else str(v).lower() in ("1", "true", "sim", "s")
        else:
            try:
                x = float(v)
            except (TypeError, ValueError):
                erros.append(f"{k}: número esperado, recebido {v!r}")
                continue
            if not math.isfinite(x):
                erros.append(f"{k}: número finito esperado")
            elif isinstance(pad, int) and x != int(x):
                erros.append(f"{k}: inteiro esperado")
            elif not c.get("min", -math.inf) <= x <= c.get("max", math.inf):
                erros.append(f"{k}: {x:g} fora da faixa [{c.get('min', -math.inf):g}, {c.get('max', math.inf):g}]")
            else:
                out[k] = int(x) if isinstance(pad, int) else x
    if erros:
        raise ValueError(f"ligação '{tipo}': " + "; ".join(erros))
    return out


def _esf(esforcos, chaves=("N",)):
    e = {}
    for k in chaves:
        try:
            x = float((esforcos or {}).get(k, 0.0) or 0.0)
        except (TypeError, ValueError):
            raise ValueError(f"esforço {k}: número esperado") from None
        if not math.isfinite(x):
            raise ValueError(f"esforço {k}: número finito esperado")
        e[k] = x
    return e


# ---------------------------------------------------------------- verificações (formato do contrato)

def _vf(nome, sd, rd, unidade, item):
    if not (math.isfinite(rd) and rd > 0):
        raise ValueError(f"{nome}: resistência {rd:g} ≤ 0 — geometria ou dados inválidos")
    t = float(sd / rd)
    return dict(nome=nome, item=_PRE + item, solicitante=float(sd), resistente=float(rd), unidade=unidade,
                taxa=t, ok=t <= 1 + 1e-9)


def _de_legado(x):
    """Converte as disposições de `ligacoes.py` (m) para mm no formato do contrato."""
    k = 1e3 if x["unidade"] == "m" else 1.0
    return dict(nome=x["nome"], item=_PRE + x["item"], solicitante=x["solicitante"] * k,
                resistente=x["resistente"] * k, unidade="mm" if k > 1 else x["unidade"], taxa=x["taxa"],
                ok=x["taxa"] <= 1 + 1e-9)


def _min_mm(nome, minimo, real, item):   # disposição: taxa = mínimo/real
    if real <= 0:
        return dict(nome=nome, item=_PRE + item, solicitante=minimo / MM, resistente=0.0, unidade="mm",
                    taxa=99.0, ok=False)
    return _de_legado(L._minimo(nome, minimo, real, item))


def _resultado(v, avisos, geometria, **extra):
    v = [x for x in v if x]
    pior = max(v, key=lambda x: x["taxa"]) if v else None
    r = dict(ok=all(x["ok"] for x in v), taxa=pior["taxa"] if pior else 0.0, verificacoes=v,
             avisos=avisos, geometria=geometria)
    # chaves do formato antigo (projeto.verificar_ligacao / JanelaLigacao)
    r.update(taxa_max=r["taxa"], criterio=pior["nome"] if pior else "", item=pior["item"] if pior else "")
    r.update(extra)
    return r


# ---------------------------------------------------------------- barra ligada ao gusset

def _area_ec(sec, a, b, t, tf):
    """Área de UM perfil e ec (face de contato → centroide, 5.2.5), cantos vivos."""
    if sec in ("L", "2L"):
        A1, A2 = a * t, (b - t) * t
        return A1 + A2, (A1 * t / 2 + A2 * (t + (b - t) / 2)) / (A1 + A2)
    if sec == "U":
        A1, A2 = a * t, 2 * (b - t) * tf
        return A1 + A2, (A1 * t / 2 + A2 * (t + (b - t) / 2)) / (A1 + A2)
    if sec == "tubo_circular":
        return math.pi * (a ** 2 - (a - 2 * t) ** 2) / 4, a / math.pi
    if sec == "tubo_retangular":
        return a * b - (a - 2 * t) * (b - 2 * t), (b ** 2 + 2 * b * a) / (4 * (b + a))
    return math.pi * a ** 2 / 4, a / 2   # barra redonda soldada sobre a face do gusset


def _comum(d):
    """Parafuso, gusset e eletrodo (SI)."""
    db, fub, alta, dh, av = L._parafuso(d)
    fyg, fug = L._aco(d["aco_gusset"])
    return dict(db=db, fub=fub, alta=alta, dh=dh, dn=dh + L.FURO_DANO, avisos=av, fyg=fyg, fug=fug,
                tg=d["t_gusset"] * MM, fw=L.ELETRODOS[d["eletrodo"]] * MPa, lam=d["borda_laminada"],
                lim=d["deformacao_limitada"], rosca=d["rosca_no_plano"])


def _membro(d, s, N, c, v, av, rot=""):
    """Verifica a ligação de uma barra ao gusset (lado da barra, parafusos/soldas, gusset: Whitmore, bloco,
    flambagem fica para quem conhece o comprimento livre). Retorna info p/ geometria e gusset."""
    sec = d[f"secao{s}"]
    g_ = lambda k: d[f"{k}{s}"]  # noqa: E731
    a, t = g_("dim_a") * MM, g_("t_perfil") * MM
    b = (g_("dim_b") or g_("dim_a")) * MM
    tf = (g_("tf_perfil") or g_("t_perfil")) * MM
    erros = [f"{k}{s}: deve ser > 0" for k, x in (("dim_a", a), ("t_perfil", t)) if x <= 0
             and not (k == "t_perfil" and sec == "barra_redonda")]
    if sec in ("tubo_circular", "tubo_retangular") and 2 * t >= min(a, b):
        erros.append(f"t_perfil{s}: parede ≥ metade da seção")
    if erros:
        raise ValueError("; ".join(erros))
    fy, fu = L._aco(g_("aco_perfil"))
    npf = 2 if sec == "2L" else 1
    A, ec = _area_ec(sec, a, b, t, tf)
    A = g_("A_perfil") * MM ** 2 or A
    ec = g_("xg_perfil") * MM or ec
    F = abs(N)
    tg, db, dn, dh = c["tg"], c["db"], c["dn"], c["dh"]
    info = dict(sec=sec, a=a, b=b, t=t, A=A, ec=ec, npf=npf, parafusos=[], soldas=[])
    tracao = N > 0
    if tracao:
        v.append(_vf(f"barra{rot}: escoamento da seção bruta", F, npf * A * fy / GA1, "kN", "5.2.2 a)"))
    if g_("ligacao") == "parafusada":
        if sec not in ("L", "2L", "U"):
            raise ValueError(f"secao{s} = {sec}: ligação parafusada só para L, 2L e U (tubo/barra: soldada)")
        nl, np_, sp, gg = g_("n_linhas"), g_("n_por_linha"), g_("s") * MM, g_("g") * MM
        e1, eg = g_("e1") * MM, g_("e_gusset") * MM
        ga = (g_("g_aba") * MM) or a / 2
        regras = [(f"e1{s}", e1, dn / 2, "borda na ponta do perfil"), (f"e_gusset{s}", eg, dn / 2, "borda no gusset")]
        regras += [(f"s{s}", sp, dn, "espaçamento s")] if np_ > 1 else []
        regras += [(f"g{s}", gg, dn, "gabarito g")] if nl > 1 else []
        borda_ponta = a - ga - (nl - 1) * gg      # da última linha à ponta da aba (L) / borda da alma (U)
        if sec != "U":
            regras.append((f"g_aba{s}", borda_ponta, dn / 2, "borda livre da aba (dim_a − g_aba − (n−1)·g)"))
        L._geometria("contraventamento", dn, regras)
        nb, lc = nl * np_, (np_ - 1) * sp
        planos = 2 if sec == "2L" else 1
        FvRd = L.cisalhamento_parafuso(db, c["fub"], c["rosca"], c["alta"])
        k = L._fator_longa(lc, av)
        # 6.3.3.3 por furo: o da ponta com a borda, os internos com s − dh (compressão: a favor, mesma soma)
        fc = lambda tt, fuu, e: ([L.pressao_contato(e - dh / 2, tt, db, fuu, c["lim"])]  # noqa: E731
                                 + [L.pressao_contato(sp - dh, tt, db, fuu, c["lim"])] * (np_ - 1))
        fcg, fcm = fc(tg, c["fug"], eg), fc(t, fu, e1)
        v += [_vf(f"parafusos{rot}: cisalhamento ({planos} plano(s))", k * F / nb, planos * FvRd, "kN", "6.3.3.2"),
              _vf(f"parafusos{rot}: contato no gusset", k * F, nl * sum(fcg), "kN", "6.3.3.3"),
              _vf(f"parafusos{rot}: contato no perfil", k * F, nl * npf * sum(fcm), "kN", "6.3.3.3"),
              _vf(f"parafusos{rot}: grupo Σ min(Fv; Fc)", k * F,
                  nl * sum(min(planos * FvRd, x, npf * y) for x, y in zip(fcg, fcm)), "kN", "6.3.3.2 / 6.3.3.3")]
        if tracao:
            if np_ == 1:
                ct, nota = 0.60, "1 parafuso por linha: Ct = 0,60 (5.2.5 pede lc; prefira ≥ 2 parafusos)"
            else:
                ct, nota = coef_Ct(sec, ec, lc)
            if nota:
                av.append(f"barra{rot}: {nota}")
            An = A - nl * dn * t
            info["Ct"] = ct
            v.append(_vf(f"barra{rot}: ruptura da seção líquida (Ct = {ct:.3f})", F, npf * ct * An * fu / GA2, "kN",
                         "5.2.2 b) / 5.2.3 / 5.2.5"))
            v.append(_vf(f"barra{rot}: Ct ≥ 0,60", 0.60, ct, "-", "5.2.5"))
            Lv = e1 + lc
            Agv, Anv = Lv * t, (Lv - (np_ - 0.5) * dn) * t
            if sec == "U":
                if nl > 1:
                    v.append(_vf(f"barra{rot}: colapso por rasgamento (alma)", F,
                                 L.colapso_rasgamento(2 * Agv, 2 * Anv, (nl - 1) * (gg - dn) * t, fy, fu), "kN", "6.5.6"))
            else:
                v.append(_vf(f"barra{rot}: colapso por rasgamento (aba)", F,
                             npf * L.colapso_rasgamento(Agv, Anv, (a - ga - (nl - 0.5) * dn) * t, fy, fu), "kN", "6.5.6"))
            Lg = eg + lc
            if nl > 1:
                v.append(_vf(f"gusset{rot}: colapso por rasgamento", F,
                             L.colapso_rasgamento(2 * Lg * tg, 2 * (Lg - (np_ - 0.5) * dn) * tg,
                                                  (nl - 1) * (gg - dn) * tg, c["fyg"], c["fug"]), "kN", "6.5.6"))
            else:
                av.append(f"gusset{rot}: uma linha de parafusos — rasgamento do gusset coberto pelo contato (6.3.3.3)")
        w0 = (nl - 1) * gg
        Lw = largura_whitmore(w0, lc)
        Aw, Anw = Lw * tg, (Lw - nl * dn) * tg
        if tracao:
            tr = L.tracao_chapa(Aw, Anw, c["fyg"], c["fug"])
            v += [_vf(f"gusset{rot}: Whitmore, escoamento", F, tr["escoamento"], "kN", "6.5.3 → 5.2.2 a)"),
                  _vf(f"gusset{rot}: Whitmore, ruptura", F, tr["ruptura"], "kN", "6.5.3 → 5.2.2 b)")]
        tmin = min(t, tg)
        if np_ > 1:
            v += [_de_legado(L._espacamento_minimo(sp, db, av, f"{rot} (s)")),
                  _de_legado(L._espacamento_maximo(sp, tmin))]
        if nl > 1:
            v += [_de_legado(L._espacamento_minimo(gg, db, av, f"{rot} (g)")),
                  _de_legado(L._espacamento_maximo(gg, tmin))]
        leg = []
        L._bordas(leg, db, t, e1, laminada=c["lam"], nome=f"{rot} (ponta do perfil)")
        L._bordas(leg, db, tg, eg, laminada=c["lam"], nome=f"{rot} (gusset)")
        if sec != "U":
            L._bordas(leg, db, t, borda_ponta, laminada=True, nome=f"{rot} (borda da aba)")
        v += [_de_legado(x) for x in leg]
        comp = e1 + lc + eg
        info.update(lc=lc, Lw=Lw, Aw=Aw, w0=w0, comp=comp, meia=max(a / 2, w0 / 2 + eg), xw=e1)
        pega = tg + npf * t
        for i in range(np_):
            for j in range(nl):
                info["parafusos"].append(dict(x=e1 + i * sp, z=(j - (nl - 1) / 2) * gg, pega=pega))
    else:   # soldada
        l1 = g_("l1") * MM
        l2 = (g_("l2") * MM) or l1
        bs = g_("b_solda") * MM
        if l1 <= 0 or bs <= 0:
            raise ValueError(f"l1{s} e b_solda{s} devem ser > 0 na ligação soldada")
        fyb, fub_ = min(fy, c["fyg"]), min(fu, c["fug"])
        Fw = L.resistencia_filete(bs, c["fw"], fyb, fub_)["FwRd"]
        if sec in ("L", "2L"):
            Fp = F / npf
            linhas = [("canto", l1, Fp * (a - ec) / a), ("borda", l2, Fp * ec / a)]
            w0, lc = a, (l1 + l2) / 2
        elif sec == "U":
            linhas, w0, lc = [("borda 1", l1, F / 2), ("borda 2", l2, F / 2)], a, (l1 + l2) / 2
        elif sec == "barra_redonda":
            # solda de entalhe em superfície curva (flare bevel): garganta 5/16·R [AISC 360 tab. J2.2; confirmar NBR]
            Fw = 0.6 * (5 / 16) * (a / 2) * c["fw"] / L.GW2
            av.append(f"barra{rot}: solda em superfície curva com garganta 5/16·R (AISC 360 tab. J2.2) — confirmar")
            linhas, w0, lc = [("lado 1", l1, F / 2), ("lado 2", l2, F / 2)], a, (l1 + l2) / 2
        else:  # tubo com rasgo no gusset: 4 filetes de comprimento l1
            linhas, w0, lc = [(f"{i + 1}", l1, F / 4) for i in range(4)], a, l1
        for nm, li, Fi in linhas:
            if Fi > 0:
                v.append(_vf(f"solda{rot}: filete {nm} (l = {li / MM:.0f} mm)", Fi, Fw * li, "kN", "6.2.5 / tabela 8"))
        if sec != "barra_redonda":
            v += [_de_legado(L._minimo(f"solda{rot}: dimensão mínima", L.filete_minimo(min(t, tg)), bs,
                                       "6.2.6.2.1 / tabela 10")),
                  _de_legado(L._maximo(f"solda{rot}: dimensão máxima (borda do perfil)", bs, L.filete_maximo(t),
                                       "6.2.6.2.2"))]
        v.append(_de_legado(L._minimo(f"solda{rot}: comprimento mínimo (4b e 40 mm)", max(4 * bs, 0.040),
                                      min(l1, l2), "6.2.6.2.3")))
        if tracao:
            if sec in ("tubo_circular", "tubo_retangular"):
                ct, nota = coef_Ct(sec, ec, lc, D=a)
                An = A - 2 * t * (tg + FOLGA_RASGO * MM)
            else:
                ct, nota = coef_Ct(sec, ec, lc)
                An = A
            if nota:
                av.append(f"barra{rot}: {nota}")
            if ct is None:
                v.append(_min_mm(f"barra{rot}: comprimento da solda lc ≥ D/H (5.2.5)", a, lc, "5.2.5"))
            else:
                info["Ct"] = ct
                v.append(_vf(f"barra{rot}: ruptura da seção líquida (Ct = {ct:.3f})", F, npf * ct * An * fu / GA2,
                             "kN", "5.2.2 b) / 5.2.3 / 5.2.5"))
                if not sec.startswith("tubo"):
                    v.append(_vf(f"barra{rot}: Ct ≥ 0,60", 0.60, ct, "-", "5.2.5"))
            Lsh = 2 * l1 if sec.startswith("tubo") else l1 + l2
            v.append(_vf(f"gusset{rot}: colapso por rasgamento", F,
                         L.colapso_rasgamento(Lsh * tg, Lsh * tg, w0 * tg, c["fyg"], c["fug"]), "kN", "6.5.6"))
        Lw = largura_whitmore(w0, lc)
        Aw = Lw * tg
        if tracao:
            v += [_vf(f"gusset{rot}: Whitmore, escoamento", F, Aw * c["fyg"] / GA1, "kN", "6.5.3 → 5.2.2 a)"),
                  _vf(f"gusset{rot}: Whitmore, ruptura", F, Aw * c["fug"] / GA2, "kN", "6.5.3 → 5.2.2 b)")]
        info.update(lc=lc, Lw=Lw, Aw=Aw, w0=w0, comp=max(l1, l2) + 10 * MM, meia=a / 2 + 10 * MM, xw=0.0)
        lados = [1, -1] if (sec == "2L" or sec.startswith("tubo")) else [1]
        zs = [a / 2, -a / 2]
        for lado in lados:
            for (nm, li, _), z in zip(linhas[:2], zs):
                info["soldas"].append(dict(perna=bs, x0=0.0, x1=li, z=z, lado=lado))
    if sec in ("L", "2L"):
        info["dica"] = dict(Ct=info.get("Ct"), ec_mm=ec / MM)
    return info


# ---------------------------------------------------------------- geometria

def _frame(theta):
    """Direção da barra e normal no plano do gusset (referencial do apoio: h, v)."""
    return (math.sin(theta), math.cos(theta)), (-math.cos(theta), math.sin(theta))


def _regiao(info, recuo, dirv, perp):
    """Cantos da região ligada (ponta do perfil → fim da ligação, meia-largura) no referencial h, v."""
    xz = [(x, z) for x in (recuo, recuo + info["comp"]) for z in (info["meia"], -info["meia"])]
    return [(x * dirv[0] + z * perp[0], x * dirv[1] + z * perp[1]) for x, z in xz]


def _pontas_whitmore(info, recuo, dirv, perp, folga=0.0):
    x, z = recuo + info["xw"], info["Lw"] / 2 + folga
    return [(x * dirv[0] + w * perp[0], x * dirv[1] + w * perp[1]) for w in (z, -z)]


def _para_local(pts, dir1, perp1):
    """(h, v) → (x, z) local da ligação (x ao longo da barra 1)."""
    return [(h * dir1[0] + vv * dir1[1], h * perp1[0] + vv * perp1[1]) for h, vv in pts]


def _geo_membro(info, recuo, dirv, perp, dir1, perp1, tg, d_mm):
    """Parafusos e soldas de uma barra no referencial local (mm)."""
    def loc(x, z):
        h, vv = (recuo + x) * dirv[0] + z * perp[0], (recuo + x) * dirv[1] + z * perp[1]
        return h * dir1[0] + vv * dir1[1], h * perp1[0] + vv * perp1[1]
    par, sol = [], []
    for p in info["parafusos"]:
        X, Z = loc(p["x"], p["z"])
        y0 = tg / 2 + info["t"] + 0.004   # cabeça sobre a aba (2L: aba do lado +y)
        par.append(dict(d=d_mm, comprimento=round((p["pega"] + 0.025) / MM, 1),
                        origem=[X / MM, y0 / MM, Z / MM], eixo=[0.0, -1.0, 0.0]))
    for s_ in info["soldas"]:
        a_, b_ = loc(s_["x0"], s_["z"]), loc(s_["x1"], s_["z"])
        y = s_["lado"] * tg / 2
        sol.append(dict(tipo="filete", perna=s_["perna"] / MM,
                        pontos=[[a_[0] / MM, y / MM, a_[1] / MM], [b_[0] / MM, y / MM, b_[1] / MM]]))
    return par, sol


def _whitmore_cabe(casca, info, recuo, dirv, perp, av, rot=""):
    """Aviso se as pontas da seção de Whitmore saem do contorno (convexo, anti-horário) do gusset."""
    n = len(casca)
    for P in _pontas_whitmore(info, recuo, dirv, perp):
        if n >= 3 and any((casca[(i + 1) % n][0] - casca[i][0]) * (P[1] - casca[i][1])
                          - (casca[(i + 1) % n][1] - casca[i][1]) * (P[0] - casca[i][0]) < -1e-9 for i in range(n)):
            av.append(f"gusset{rot}: a seção de Whitmore ({info['Lw'] / MM:.0f} mm) sai do contorno da chapa — "
                      "a resistência calculada fica superestimada; aumente o gusset ou recorte a largura")
            return


def _chapa(nome, contorno_m, tg):
    return dict(nome=nome, contorno=[[round(x / MM, 2), round(z / MM, 2)] for x, z in contorno_m],
                origem=[0.0, tg / 2 / MM, 0.0], normal=[0.0, -1.0, 0.0], u=[1.0, 0.0, 0.0],
                espessura=tg / MM)


# ---------------------------------------------------------------- interface gusset–apoio

def _interface(v, nome, L_, Hpar, Vnor, M, c, b, fd):
    """Filete duplo (dois lados do gusset) e borda do gusset numa interface de comprimento L_ (6.2.5, 6.5.5,
    6.5.3). Hpar: força ao longo da interface; Vnor: normal; M: momento no plano."""
    tg = c["tg"]
    Fw = L.resistencia_filete(b, c["fw"], c["fyg"], c["fug"])["FwRd"]
    fn = Vnor / (2 * L_) + 6 * M / (2 * L_ ** 2)
    f = fd * math.hypot(Hpar / (2 * L_), fn)
    v += [_vf(f"{nome}: solda (filete duplo, ×{fd:g})", f * L_, Fw * L_, "kN", "6.2.5 / tabela 8"),
          _vf(f"{nome}: gusset, cisalhamento", Hpar, 0.6 * c["fyg"] * L_ * tg / GA1, "kN", "6.5.5"),
          _vf(f"{nome}: gusset, força normal + momento", Vnor + 6 * M / L_, L_ * tg * c["fyg"] / GA1, "kN",
              "6.5.3 → 5.2.2 a)"),
          _de_legado(L._minimo(f"{nome}: dimensão mínima do filete", L.filete_minimo(tg), b, "6.2.6.2.1 / tabela 10"))]


# ---------------------------------------------------------------- 1. gusset de contraventamento

def _auto_gusset(d, info, c, theta):
    """recuo, Lh e Lv automáticos: a região ligada fica a 2·tg das faces (folga da linha de dobra)."""
    dirv, perp = _frame(theta)
    folga = 2 * c["tg"]
    eb, ec = d["eb"] * MM, d["ec"] * MM
    hw, ap = info["meia"], d["apoio"]
    s, co = math.sin(theta), math.cos(theta)
    hwm, xw = info["Lw"] / 2, info["xw"]
    req = [0.0]
    if ap in ("viga-pilar", "pilar"):
        req += [(ec + folga + hw * co) / s, (ec + hwm * co) / s - xw]
    if ap in ("viga-pilar", "viga", "base"):
        req += [(eb + folga + hw * s) / co, (eb + hwm * s) / co - xw]
    recuo = d["recuo"] * MM or math.ceil(max(req) / 0.005) * 0.005
    hmin = ec if ap in ("viga-pilar", "pilar") else -math.inf
    vmin = eb if ap != "pilar" else -math.inf
    pts = _regiao(info, recuo, dirv, perp)
    pts += [(max(h, hmin), max(v_, vmin)) for h, v_ in _pontas_whitmore(info, recuo, dirv, perp, folga)]
    Lh = d["Lh"] * MM or max(0.05, math.ceil((max(p[0] for p in pts) - ec) / 0.01) * 0.01)
    Lv = d["Lv"] * MM or max(0.05, math.ceil((max(p[1] for p in pts) - eb) / 0.01) * 0.01)
    return recuo, Lh, Lv, pts


def verificar_gusset(dados, esforcos):
    d = _ler(_GUSSET, dados, "gusset_contraventamento")
    N = _esf(esforcos)["N"]
    c = _comum(d)
    v, av = [], list(c["avisos"])
    theta = math.radians(d["theta"])
    info = _membro(d, "", N, c, v, av)
    recuo, Lh, Lv, pts = _auto_gusset(d, info, c, theta)
    eb, ec, ap, tg = d["eb"] * MM, d["ec"] * MM, d["apoio"], c["tg"]
    dirv, perp = _frame(theta)
    reg = _regiao(info, recuo, dirv, perp)   # região ligada não pode invadir viga/pilar
    if ap in ("viga-pilar", "pilar"):
        v.append(_min_mm("geometria: ligação afastada do pilar", ec, min(p[0] for p in reg), "6.1 / detalhamento"))
    if ap in ("viga-pilar", "viga", "base"):
        v.append(_min_mm("geometria: ligação afastada da viga/base", eb, min(p[1] for p in reg), "6.1 / detalhamento"))
    # flambagem do gusset (Thornton): do centro da seção de Whitmore até a borda apoiada, ao longo do eixo
    if N < 0:
        s, co = math.sin(theta), math.cos(theta)
        xs = ([ec / s] if ap in ("viga-pilar", "pilar") else []) + ([eb / co] if ap != "pilar" else [])
        L1 = d["L_flambagem"] * MM or max(recuo + info["xw"] - max(xs), 0.0)
        fl = flambagem_chapa(info["Aw"], tg, d["K_gusset"] * L1, c["fyg"])
        v.append(_vf(f"gusset: compressão (Whitmore, KL/r = {fl['KL_r']:.1f}, χ = {fl['chi']:.3f})", -N, fl["NcRd"],
                     "kN", fl["item"]))
    # interfaces
    F = abs(N)
    forcas = {}
    fd, bi = d["fator_ductilidade"], d["b_interface"] * MM
    if ap == "viga-pilar":
        u = forca_uniforme(F, theta, eb, ec, Lh, Lv)
        forcas = {k: u[k] for k in ("Hb", "Vb", "Hc", "Vc", "Mb", "alfa", "beta", "alfa_barra")}
        if F > 0:
            _interface(v, "interface gusset–viga", Lh, u["Hb"], u["Vb"], u["Mb"], c, bi, fd)
            _interface(v, "interface gusset–pilar", Lv, u["Vc"], u["Hc"], 0.0, c, bi, fd)
        av.append("método da força uniforme (AISC parte 13): Vb e Hc+Hb entram na ligação viga–pilar e a "
                  "força na viga — verifique-as à parte (alma da viga e do pilar não verificadas)")
    elif F > 0:
        s, co = math.sin(theta), math.cos(theta)
        if ap == "pilar":   # interface vertical em h = ec, de v = eb até eb + Lv
            v0 = ec / math.tan(theta)
            M = F * s * abs(eb + Lv / 2 - v0)
            forcas = dict(Vc=F * co, Hc=F * s, Mc=M)
            _interface(v, "interface gusset–pilar", Lv, F * co, F * s, M, c, bi, fd)
        else:               # viga ou base: interface horizontal em v = eb, de h = ec até ec + Lh
            h0 = eb * math.tan(theta)
            M = F * co * abs(ec + Lh / 2 - h0)
            forcas = dict(Hb=F * s, Vb=F * co, Mb=M)
            _interface(v, "interface gusset–" + ("base" if ap == "base" else "viga"), Lh, F * s, F * co, M, c, bi, fd)
        if ap == "base":
            av.append("placa de base e chumbadores não verificados aqui (aco/placa_base.py)")
    if N == 0:
        av.append("sem esforço: só disposições construtivas")
    av.append("bordas livres do gusset (flambagem local da borda) e solda/parafusos sob fadiga não verificados")
    # contorno: cantos das interfaces + região ligada
    canto = []
    if ap == "viga-pilar":
        canto = [(ec, eb), (ec + Lh, eb), (ec, eb + Lv)]
    elif ap == "pilar":
        canto = [(ec, eb), (ec, eb + Lv)]
    else:
        canto = [(ec, eb), (ec + Lh, eb)]
    casca = _hull(canto + pts)
    _whitmore_cabe(casca, info, recuo, dirv, perp, av)
    cont = _para_local(casca, dirv, perp)
    par, sol = _geo_membro(info, recuo, dirv, perp, dirv, perp, tg, d["d_mm"])
    trechos = ([(canto[0], canto[1])] if ap != "pilar" else []) + \
              ([(canto[0], canto[-1])] if ap in ("viga-pilar", "pilar") else [])
    for P0, P1 in trechos:
        (x0, z0), (x1, z1) = _para_local([P0, P1], dirv, perp)
        for lado in (1, -1):
            y = lado * tg / 2 / MM
            sol.append(dict(tipo="filete", perna=bi / MM, pontos=[[x0 / MM, y, z0 / MM], [x1 / MM, y, z1 / MM]]))
    geo = dict(chapas=[_chapa("gusset", cont, tg)], cantoneiras=[], parafusos=par, soldas=sol,
               recortes=[dict(barra="diagonal", tipo="rasgo", parametros=dict(
                   comprimento=round((info["lc"] + FOLGA_RASGO * MM) / MM, 1),
                   largura=round(tg / MM + FOLGA_RASGO, 1)))] if info["sec"].startswith("tubo") else [],
               ponto_trabalho=[0.0, 0.0, 0.0], recuo=recuo / MM, Lh=Lh / MM, Lv=Lv / MM,
               largura_whitmore=info["Lw"] / MM)
    extra = dict(forcas_interface=forcas)
    if "dica" in info:
        extra["dica_barra"] = info["dica"]
    return _resultado(v, av, geo, **extra)


# ---------------------------------------------------------------- 2. nó de treliça com gusset

def verificar_no_trelica(dados, esforcos):
    d = _ler(_NO, dados, "no_trelica_gusset")
    e = _esf(esforcos, ("N", "N2", "N3", "N4"))
    c = _comum(d)
    v, av = [], list(c["avisos"])
    tg, eb = c["tg"], d["e_banzo"] * MM
    ativos = [i for i in "1234" if d[f"secao_{i}"] != "nenhum"]
    forcas = {i: e["N" if i == "1" else f"N{i}"] for i in ativos}
    pts_all, regs, membros, H, V = [], [], {}, 0.0, 0.0
    for i in ativos:
        Ni = forcas[i]
        Nd = math.copysign(max(abs(Ni), N_MINIMO), Ni or 1.0)
        if abs(Ni) < N_MINIMO:
            av.append(f"barra {i}: ligação dimensionada para o mínimo de 45 kN (NBR 8800 6.1; Pfeil p.232)")
        info = _membro(d, f"_{i}", Nd, c, v, av, rot=f" {i}")
        th = math.radians(d[f"theta_{i}"])
        # direção a partir do banzo: (cos, sin) no referencial (h ao longo do banzo, v para o gusset)
        dv, pp = (math.cos(th), math.sin(th)), (-math.sin(th), math.cos(th))
        sv = abs(math.sin(th))
        x_face = eb / sv if sv > 1e-6 else 0.0
        cs = abs(math.cos(th))
        auto = max((eb + 2 * tg + info["meia"] * cs) / sv, (eb + info["Lw"] / 2 * cs) / sv - info["xw"])             if sv > 1e-6 else 2 * tg
        recuo = d[f"recuo_{i}"] * MM or math.ceil(auto / 0.005) * 0.005
        if Nd < 0:
            L1 = max(recuo + info["xw"] - x_face, 0.0)
            fl = flambagem_chapa(info["Aw"], tg, d["K_gusset"] * L1, c["fyg"])
            v.append(_vf(f"gusset {i}: compressão (Whitmore, KL/r = {fl['KL_r']:.1f})", -Nd, fl["NcRd"], "kN",
                         fl["item"]))
        H += Ni * dv[0]
        V += Ni * dv[1]
        reg = _regiao(info, recuo, dv, pp)
        regs += reg
        vmin = eb if d["banzo"] == "continuo" else -math.inf
        pts_all += reg + [(h, max(v_, vmin)) for h, v_ in _pontas_whitmore(info, recuo, dv, pp, 2 * tg)]
        membros[i] = (info, recuo, dv, pp)
    av.append("barras com eixos concorrentes no ponto de trabalho: momento no nó desprezado (sem fadiga; "
              "Pfeil p.231–233)")
    forcas_if = {}
    if d["banzo"] == "continuo":
        xs = [p[0] for p in pts_all]
        Li = d["L_interface"] * MM or max(0.1, math.ceil((max(xs) - min(xs)) / 0.01) * 0.01)
        hc = (max(xs) + min(xs)) / 2 if not d["L_interface"] else 0.0
        M = abs(H * eb) + abs(V * hc)
        forcas_if = dict(H=H, V=V, M=M, L_interface=Li / MM)
        if abs(H) + abs(V) > 0:
            _interface(v, "interface gusset–banzo", Li, abs(H), abs(V), M, c, d["b_interface"] * MM,
                       d["fator_ductilidade"])
        canto = [(hc - Li / 2, eb), (hc + Li / 2, eb)]
        v.append(_min_mm("geometria: ligações acima da face do banzo", eb, min(p[1] for p in regs),
                         "6.1 / detalhamento"))
    else:
        canto = []
        if math.hypot(H, V) > 0.02 * max(abs(x) for x in forcas.values() or [1.0]) + 1e-6:
            av.append(f"banzo emendado: forças das barras não se equilibram (ΣH = {H:.1f}, ΣV = {V:.1f} kN) — "
                      "confira os esforços")
    casca = _hull(canto + pts_all)
    for i, (info, recuo, dv, pp) in membros.items():
        _whitmore_cabe(casca, info, recuo, dv, pp, av, rot=f" {i}")
    # geometria no referencial local: rotação que leva a barra 1 a +x
    b1 = membros["1"][2]
    rx, rz = (b1[0], b1[1]), (-b1[1], b1[0])
    to_loc = lambda P: (P[0] * rx[0] + P[1] * rx[1], P[0] * rz[0] + P[1] * rz[1])  # noqa: E731
    cont = [to_loc(P) for P in casca]
    par, sol = [], []
    for i, (info, recuo, dv, pp) in membros.items():
        # rotaciona a direção da barra para o referencial local
        dl, pl = to_loc(dv), to_loc(pp)
        p_, s_ = _geo_membro(info, recuo, dl, pl, (1.0, 0.0), (0.0, 1.0), tg, d["d_mm"])
        par += p_
        sol += s_
    for lado in (1, -1):
        if canto:
            a0, a1 = to_loc(canto[0]), to_loc(canto[1])
            sol.append(dict(tipo="filete", perna=d["b_interface"],
                            pontos=[[a0[0] / MM, lado * tg / 2 / MM, a0[1] / MM],
                                    [a1[0] / MM, lado * tg / 2 / MM, a1[1] / MM]]))
    geo = dict(chapas=[_chapa("gusset", cont, tg)], cantoneiras=[], parafusos=par, soldas=sol, recortes=[],
               ponto_trabalho=[0.0, 0.0, 0.0],
               barras={i: dict(recuo=m[1] / MM, largura_whitmore=m[0]["Lw"] / MM) for i, m in membros.items()})
    return _resultado(v, av, geo, forcas_interface=forcas_if)


# ---------------------------------------------------------------- 3. tirante de barra redonda

def tracao_barra_rosqueada(D, fy, fu):
    """NBR 8800 6.3.3.1: Ft,Rd = 0,75·Ab·fu/γa2 (área efetiva da rosca), limitada a Ab·fy/γa1 (5.2.2 a)
    para barras redondas rosqueadas) [PFEIL eq. 2.2, p.53]."""
    Ab = math.pi * D ** 2 / 4
    return dict(Ab=Ab, rosca=0.75 * Ab * fu / GA2, escoamento=Ab * fy / GA1)


def verificar_tirante(dados, esforcos):
    d = _ler(_TIRANTE, dados, "tirante_barra_redonda")
    N = _esf(esforcos)["N"]
    D = d["D_barra"] * MM
    fy, fu = L._aco(d["aco_barra"])
    fyc, fuc = L._aco(d["aco_chapa"])
    t = d["t_chapa"] * MM
    v, av = [], []
    r = tracao_barra_rosqueada(D, fy, fu)
    if N < 0:
        v.append(dict(nome="tirante comprimido (barra redonda só trabalha à tração)", item=_PRE + "5.2",
                      solicitante=-N, resistente=0.0, unidade="kN", taxa=99.0, ok=False))
        av.append("barra redonda não resiste à compressão: calcule com a diagonal comprimida desativada")
    F = max(N, 0.0)
    if F > 0:
        v += [_vf("barra: tração na rosca (0,75·Ab)", F, r["rosca"], "kN", "6.3.3.1"),
              _vf("barra: escoamento da seção bruta", F, r["escoamento"], "kN", "6.3.3.1 / 5.2.2 a)")]
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[], porcas=[])
    if d["extremidade"] == "rosca_porca":
        dh = D + L.FURO_FOLGA
        geo["chapas"].append(dict(nome="chapa de apoio da porca",
                                  contorno=[[-2 * D / MM, -2 * D / MM], [2 * D / MM, -2 * D / MM],
                                            [2 * D / MM, 2 * D / MM], [-2 * D / MM, 2 * D / MM]],
                                  origem=[0.0, 0.0, 0.0], normal=[1.0, 0.0, 0.0], u=[0.0, 1.0, 0.0],
                                  espessura=t / MM, furo=dh / MM))
        geo["porcas"].append(dict(d=D / MM, origem=[-t / MM, 0.0, 0.0], eixo=[-1.0, 0.0, 0.0],
                                  arruela=True, contraporca=True))
        av += ["porca e arruela: especifique classe compatível (ex.: ASTM A563 / F436) — resistência da porca não "
               "verificada", "flexão da chapa/aba sob a porca não verificada (use arruela de chapa)"]
    else:
        dp = (d["d_pino"] or d["D_barra"]) * MM
        tipo = d["tipo_pino"]
        p = dict(d_mm=dp / MM, tipo_parafuso=tipo)
        db, fub, alta, dh, avp = L._parafuso(p)
        av += avp
        dn = dh + L.FURO_DANO
        bw = (d["largura_chapa"] * MM) or 4 * dp
        a = (d["borda_pino"] * MM) or 2 * dp
        L._geometria("tirante_barra_redonda", dn, [("borda_pino", a, dn / 2, "borda além do pino"),
                                                   ("largura_chapa", bw / 2, dn / 2, "meia largura da chapa")])
        if F > 0:
            tr = L.tracao_chapa(bw * t, (bw - dn) * t, fyc, fuc)
            v += [_vf("pino: cisalhamento (2 planos, garfo)", F,
                      2 * L.cisalhamento_parafuso(db, fub, True, alta), "kN", "6.3.3.2"),
                  _vf("gusset: contato/rasgamento no furo do pino", F,
                      L.pressao_contato(a - dh / 2, t, db, fuc, d["deformacao_limitada"]), "kN", "6.3.3.3"),
                  _vf("gusset: escoamento na seção do pino", F, tr["escoamento"], "kN", "6.5.3 → 5.2.2 a)"),
                  _vf("gusset: ruptura na seção do pino", F, tr["ruptura"], "kN", "6.5.3 → 5.2.2 b)")]
        leg = []
        L._bordas(leg, db, t, a, laminada=d["borda_laminada"], nome=" (gusset, além do pino)")
        v += [_de_legado(x) for x in leg]
        av.append("garfo (clevis) e esticador: peças comerciais — use a carga de catálogo do fabricante ≥ N")
        geo["chapas"].append(dict(nome="gusset", contorno=[[0.0, -bw / 2 / MM], [(a + 3 * dp) / MM, -bw / 2 / MM],
                                                         [(a + 3 * dp) / MM, bw / 2 / MM], [0.0, bw / 2 / MM]],
                                  origem=[0.0, t / 2 / MM, 0.0], normal=[0.0, -1.0, 0.0], u=[1.0, 0.0, 0.0],
                                  espessura=t / MM))
        geo["parafusos"].append(dict(d=dp / MM, comprimento=round(t / MM + 2 * dp / MM + 20, 1),
                                     origem=[a / MM, t / 2 / MM + dp / MM, 0.0], eixo=[0.0, -1.0, 0.0]))
    if d["comprimento"] > 0:
        av.append(f"esbeltez L/r = {d['comprimento'] * MM / (D / 4):.0f}: barras redondas pré-tracionadas "
                  "dispensam L/r ≤ 300 (5.2.8); garanta a protensão com esticador")
    geo["barra"] = dict(D=D / MM, eixo=[1.0, 0.0, 0.0])
    return _resultado(v, av, geo, Ab_mm2=r["Ab"] / MM ** 2)


# ---------------------------------------------------------------- 4. cantoneira simples em treliça leve

def verificar_cantoneira_simples(dados, esforcos):
    d = _ler(_CANT, dados, "cantoneira_simples_trelica")
    N = _esf(esforcos)["N"]
    c = _comum(d)
    v, av = [], list(c["avisos"])
    Nd = math.copysign(max(abs(N), N_MINIMO), N or 1.0)
    if abs(N) < N_MINIMO:
        av.append("ligação dimensionada para o mínimo de 45 kN (NBR 8800 6.1; Pfeil p.232)")
    info = _membro(d, "", Nd, c, v, av)
    if d["fadiga"]:
        av.append("barra sob fadiga: a excentricidade da ligação NÃO pode ser desprezada (NBR 8800 6.1) — "
                  "use modelo de pórtico com o momento N·ec")
    else:
        av.append("cantoneira sob ação estática: excentricidade entre os centroides da barra e da ligação "
                  "desprezada na ligação (NBR 8800 6.1 [confirmar subitem]); na barra entra por E.1.4 (compressão) "
                  "e Ct (tração)")
    a, t = d["dim_a"] * MM, d["t_perfil"] * MM
    b = (d["dim_b"] or d["dim_a"]) * MM
    aba = "maior" if a >= b else "menor"           # dim_a é a aba ligada
    dica = dict(ligacao_uma_aba=dict(aba=aba, trelica=d["trelica"]), item="E.1.4", **info["dica"])
    if d["comprimento"] > 0:
        Lb = d["comprimento"] * MM
        dica["ligacao_uma_aba"]["L"] = Lb
        if N < 0:
            if d["A_perfil"]:
                av.append("E.1.4 com as propriedades de cantos vivos (A_perfil só entra na ligação)")
            rc = compressao(secoes.secao_cantoneira(a, b, t), materiais.aco(d["aco_perfil"]), Lb, Lb,
                            ligacao_uma_aba=dict(L=Lb, aba=aba, trelica=d["trelica"]))
            if "E14" in rc:
                v.append(_vf(f"barra: compressão com a excentricidade (E.1.4, KL = {rc['E14']['KxLx'] / MM:.0f} mm)",
                             -N, rc["NcRd"], "kN", "5.3 / E.1.4"))
                dica["E14"] = dict(KxLx_mm=rc["E14"]["KxLx"] / MM, Ne=rc["Ne"], NcRd=rc["NcRd"], chi=rc["chi"])
            av += rc.get("avisos", [])
    tg = c["tg"]
    dirv, perp = (1.0, 0.0), (0.0, 1.0)
    recuo = d["recuo"] * MM or 2 * tg
    pts = _regiao(info, recuo, dirv, perp)
    cont = _hull(pts + [(0.0, info["meia"]), (0.0, -info["meia"])])
    par, sol = _geo_membro(info, recuo, dirv, perp, dirv, perp, tg, d["d_mm"])
    geo = dict(chapas=[_chapa("chapa de nó", cont, tg)],
               cantoneiras=[dict(perfil=f"L{d['dim_a']:g}x{d['dim_b'] or d['dim_a']:g}x{d['t_perfil']:g}",
                                 origem=[recuo / MM, tg / 2 / MM, 0.0], eixo=[1.0, 0.0, 0.0], u=[0.0, 0.0, 1.0],
                                 comprimento=info["comp"] / MM)],
               parafusos=par, soldas=sol, recortes=[], ponto_trabalho=[0.0, 0.0, 0.0])
    return _resultado(v, av, geo, dica_barra=dica)


# ---------------------------------------------------------------- sugerir (pré-dimensionamento)

def _base(entradas, perfis):
    d = {k: c["padrao"] for k, c in entradas.items()}
    d.update({k: x for k, x in (perfis or {}).items() if k in entradas})
    return d


def _de_catalogo(perfis, d):
    """eb/ec pela meia altura dos perfis do catálogo (perfil_viga, perfil_pilar), se informados."""
    for chave, campo in (("perfil_viga", "eb"), ("perfil_pilar", "ec")):
        if (perfis or {}).get(chave) and campo in d:
            try:
                d[campo] = secoes.catalogo(perfis[chave]).props["h"] / 2 / MM
            except KeyError:
                pass


def _std_parafuso(d, sfx_list):
    db = d["d_mm"]
    for s in sfx_list:
        t = d.get(f"t_perfil{s}") or 0.0
        if t > 0:   # filete entre o mínimo (tabela 10) e o máximo na borda (6.2.6.2.2)
            bmax = L.filete_maximo(t * MM) / MM
            bmin = L.filete_minimo(min(t, d.get("t_gusset") or t) * MM) / MM
            d[f"b_solda{s}"] = max(bmin, min(d.get(f"b_solda{s}") or 5.0, math.floor(bmax)))
        if not d.get(f"n_linhas{s}"):
            continue
        emin = L.distancia_min_borda(db * MM, d.get("borda_laminada", False)) / MM
        d[f"s{s}"] = max(d.get(f"s{s}") or 0, math.ceil(3 * db / 5) * 5)
        d[f"e1{s}"] = max(d.get(f"e1{s}") or 0, math.ceil(emin / 5) * 5)
        d[f"e_gusset{s}"] = max(d.get(f"e_gusset{s}") or 0, math.ceil(emin / 5) * 5)


def _buscar(fn, d, passos, esforcos):
    ultimo = None
    for p in passos:
        cand = dict(d, **p)
        try:
            r = fn(cand, esforcos)
        except ValueError:
            continue
        ultimo = cand
        if r["ok"]:
            return cand
    return ultimo or d


_TG = (6.3, 8.0, 9.5, 12.5, 16.0, 19.0, 22.4, 25.0)


def _passos(sfx, ligacao, interface=(None,)):
    out = []
    for tg, bi in ((tg, bi) for bi in interface for tg in _TG):
        for k in range(1, 9):
            p = dict(t_gusset=tg, **({"b_interface": bi} if bi else {}))
            for s in sfx:
                if ligacao.get(s) == "parafusada":
                    p[f"n_por_linha{s}"] = k + 1
                else:
                    p[f"l1{s}"] = 40.0 * k + 40
                    p[f"l2{s}"] = 0.0
            out.append(p)
    return out


def sugerir_gusset(perfis, esforcos):
    d = _base(_GUSSET, perfis)
    _de_catalogo(perfis, d)
    d["t_gusset"] = d.get("t_gusset") or 8.0
    if d["secao"] in ("tubo_circular", "tubo_retangular", "barra_redonda"):
        d["ligacao"] = "soldada"
    _std_parafuso(d, [""])
    return _buscar(verificar_gusset, d, _passos([""], {"": d["ligacao"]}, (6.0, 8.0, 10.0, 12.0)), esforcos)


def sugerir_no(perfis, esforcos):
    d = _base(_NO, perfis)
    d["t_gusset"] = d.get("t_gusset") or 8.0
    ativos = [f"_{i}" for i in "1234" if d[f"secao_{i}"] != "nenhum"]
    for s in ativos:
        if d[f"secao{s}"].startswith("tubo"):
            d[f"ligacao{s}"] = "soldada"
    _std_parafuso(d, ativos)
    return _buscar(verificar_no_trelica, d, _passos(ativos, {s: d[f"ligacao{s}"] for s in ativos}, (5.0, 6.0, 8.0)),
                   esforcos)


DIAMETROS_BARRA_MM = (9.525, 12.7, 15.875, 19.05, 22.225, 25.4, 28.575, 31.75, 34.925, 38.1, 41.275, 44.45,
                      50.8, 57.15, 63.5)   # 3/8" a 2 1/2"


def sugerir_tirante(perfis, esforcos):
    d = _base(_TIRANTE, perfis)
    d["t_chapa"] = d.get("t_chapa") or 8.0
    passos = [dict(D_barra=D, t_chapa=t) for D in DIAMETROS_BARRA_MM for t in _TG]
    return _buscar(verificar_tirante, d, passos, esforcos)


def sugerir_cantoneira(perfis, esforcos):
    d = _base(_CANT, perfis)
    d["t_gusset"] = d.get("t_gusset") or 6.3
    _std_parafuso(d, [""])
    return _buscar(verificar_cantoneira_simples, d, _passos([""], {"": d["ligacao"]}), esforcos)


# ---------------------------------------------------------------- registro

TIPOS = {
    "gusset_contraventamento": dict(
        nome="Gusset de contraventamento", familia="contraventamento",
        liga=["diagonal-viga-pilar", "diagonal-pilar", "diagonal-viga", "diagonal-base"],
        descricao="Diagonal (L, 2L, U, tubo ou barra redonda) parafusada ou soldada ao gusset; gusset soldado à "
                  "viga e ao pilar (força uniforme) ou só a um deles/à base. Whitmore, flambagem (Thornton), "
                  "rasgamento, seção líquida com Ct, parafusos, soldas e interfaces.",
        entradas=_GUSSET, esforcos=["N"], verificar=verificar_gusset, sugerir=sugerir_gusset),
    "no_trelica_gusset": dict(
        nome="Nó de treliça com gusset", familia="contraventamento", liga=["no-trelica"],
        descricao="Até 4 barras (diagonais, montante, banzo emendado) ligadas a um gusset, banzo contínuo soldado "
                  "ao gusset ou emendado no nó; mínimo de 45 kN por ligação; Whitmore e flambagem por barra.",
        entradas=_NO, esforcos=["N", "N2", "N3", "N4"], verificar=verificar_no_trelica, sugerir=sugerir_no),
    "tirante_barra_redonda": dict(
        nome="Tirante de barra redonda", familia="contraventamento",
        liga=["tirante-gusset", "tirante-viga", "tirante-pilar"],
        descricao="Barra redonda rosqueada com porca sobre chapa, ou com garfo e pino no gusset: tração na área "
                  "efetiva da rosca (6.3.3.1), pino e gusset no furo.",
        entradas=_TIRANTE, esforcos=["N"], verificar=verificar_tirante, sugerir=sugerir_tirante),
    "cantoneira_simples_trelica": dict(
        nome="Cantoneira simples em treliça", familia="contraventamento", liga=["no-trelica", "diagonal-gusset"],
        descricao="Cantoneira ligada por uma aba (parafusos numa linha ou filetes): mínimo de 45 kN, Ct da "
                  "excentricidade e dica para a barra (E.1.4).",
        entradas=_CANT, esforcos=["N"], verificar=verificar_cantoneira_simples, sugerir=sugerir_cantoneira),
}


def verificar(tipo, dados, esforcos):
    return TIPOS[tipo]["verificar"](dados, esforcos)
