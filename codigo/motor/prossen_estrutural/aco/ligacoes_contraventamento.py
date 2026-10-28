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
from . import parafusos as _P
from .nbr8800 import GA1, GA2, compressao

MM = 1e-3
TAN30 = math.tan(math.radians(30))
SECOES = ["L", "2L", "U", "tubo_circular", "tubo_retangular", "barra_redonda"]
APOIOS = ["viga-pilar", "pilar", "viga", "base"]
N_MINIMO = L.F_MINIMA       # kN — NBR 8800 6.1.5.2 (p. 63); Pfeil p.238
FOLGA_RASGO = 2.0           # mm — rasgo do tubo = t_gusset + 2 mm. ponytail: valor usual de fábrica
# W8-CONTRAV: folga de montagem entre a região ligada (barra e parafusos) e as faces do apoio/banzo: max(2·tg, 25 mm).
# 2·tg = linha de dobra do gusset (AISC parte 13); 25 mm = folga usual de montagem e de chave [conferir]. Só no esquema
# novo (dados antigos continuam com 2·tg)
FOLGA_MONTAGEM = 0.025      # m
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


_CAT = secoes._carrega_catalogo()
_FAMILIAS_U = ("U laminado", "UPN", "U formado a frio")


def _nomes(teste):
    return list(dict.fromkeys(lin["nome"] for lin in _CAT.values() if teste(lin)))


# L laminadas (iguais e desiguais), U laminado/UPN, U dobrado (W12-OUT: parede fina pela NBR 14762, ligado pela alma) e
# tubos estruturais Vallourec. Fora: Ue (enrijecido: os lábios de borda e a distorção não são tratados aqui) e metalon.
_PERFIS_BARRA = _nomes(lambda x: x["forma"] == "L" or x["forma"] == "U" and x["familia"] in _FAMILIAS_U
                       or x["forma"] in ("TC", "TR") and x["familia"] == "Tubo Vallourec")
_PERFIS_BANZO = _nomes(lambda x: x["forma"] == "I")
_MED_BARRA = ("secao", "dim_a", "dim_b", "t_perfil", "tf_perfil", "A_perfil", "xg_perfil")

_COMUNS = {
    "d_mm": _e("Diâmetro do parafuso", "mm", 15.875, min=10.0, max=40.0),   # ⅝" (A325 é em polegada)
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


_GUSSET_LEG = {   # dados antigos (secao + medidas soltas); o esquema do projetista é _GUSSET, abaixo
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
    "x_max": _e("Comprimento livre na barra", "mm", 0.0, min=0.0, max=100000.0, ajuda="G6: 0 = sem limite"),
}

_NO_LEG = {   # dados antigos; o esquema do projetista é _NO, abaixo
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


def _campos_barra(s, rot, campo, padrao, arranjo, opcoes_arranjo, theta=None, secao=None):
    """W8-CONTRAV: campos de uma barra ligada ao gusset — perfil do catálogo (seletor), arranjo e aba ligada; as medidas
    soltas do esquema antigo saem (vêm do catálogo)."""
    ext = {} if secao is None else dict(secao=secao)
    c = {campo: _e(f"Perfil{rot}", "-", padrao, perfis=_PERFIS_BARRA, resugerir=True,
                   ajuda="L, U ou tubo do catálogo: medidas, área e excentricidade ec vêm dele"),
         f"arranjo{s}": _e(f"Arranjo{rot}", "-", arranjo, opcoes=opcoes_arranjo, **ext,
                           ajuda="dupla = 2 cantoneiras costas com costas, gusset entre elas (2L); simples = 1 perfil "
                                 "numa face do gusset (L, U) ou tubo com rasgo"),
         f"aba_ligada{s}": _e(f"Aba ligada{rot}", "-", "maior", opcoes=["maior", "menor"], **ext,
                              ajuda="cantoneira de abas desiguais: aba encostada no gusset")}
    c.update({k: v for k, v in _campos_membro(s, rot, theta=theta).items()
              if k not in tuple(m + s for m in _MED_BARRA)})
    c[f"g_aba{s}"] = dict(c[f"g_aba{s}"], ajuda="do talão à linha de furos (cantoneira); 0 = gabarito usual da aba")
    return c


_GUSSET = {
    **_campos_barra("", "", "perfil_diagonal", 'L 3" x 1/4"', "simples", ["simples", "dupla"], secao="barras"),
    **{k: v for k, v in _GUSSET_LEG.items() if k not in _campos_membro()},
}
_NO = {
    **_campos_barra("_1", " 1", "perfil_barra_1", 'L 2.1/2" x 1/4"', "dupla", ["dupla", "simples"], theta=45.0),
    **_campos_barra("_2", " 2", "perfil_barra_2", 'L 2.1/2" x 1/4"', "nenhuma", ["nenhuma", "dupla", "simples"], theta=90.0),
    **_campos_barra("_3", " 3", "perfil_barra_3", 'L 2" x 3/16"', "nenhuma", ["nenhuma", "dupla", "simples"], theta=135.0),
    **_campos_barra("_4", " 4", "perfil_barra_4", 'L 2" x 3/16"', "nenhuma", ["nenhuma", "dupla", "simples"], theta=0.0),
    "perfil_banzo": _e("Perfil do banzo", "-", "W150x18", perfis=_PERFIS_BANZO,
                       ajuda="banzo contínuo: perfil I com a alma no plano da treliça; o gusset é soldado na face da "
                             "mesa (eixo do banzo à face = d/2)"),
    **{k: v for k, v in _NO_LEG.items() if k in _COMUNS or k in ("L_interface", "b_interface", "K_gusset",
                                                               "fator_ductilidade")},
    "banzo": dict(_NO_LEG["banzo"], secao="barras"),
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
    "t_orelha": _e("Espessura da orelha", "mm", 0.0, min=0.0, max=100.0,
                   ajuda="flexão do pino (6.4.1); 0 = não informada: momento mínimo N·t/8 (aviso)"),
    "folga_garfo": _e("Folga orelha–gusset", "mm", 0.0, min=0.0, max=50.0, ajuda="cada lado; flexão do pino (6.4.1)"),
    "comprimento": _e("Comprimento do tirante", "mm", 6000.0, min=0.0, max=1e5,
                      ajuda="entre pontos de trabalho (X de cobertura entre tesouras a ~6 m); entra no aviso de "
                            "esbeltez (5.2.8) e na cota do desenho; 0 = não informado"),
    "borda_laminada": _e("Bordas laminadas/maçarico", "-", False),
    "deformacao_limitada": _e("Deformação limitada", "-", True),
}

# W7-LIG (docs/v3/W7-LIG.md §2): a cantoneira vem do catálogo (abas, t, A, x̄); as medidas soltas (dim_a, t_perfil...) só
# valem nos dados antigos sem `perfil_cantoneira` (legado, lidos por _CANT_MEDIDAS)
_MEDIDAS_L = ("dim_a", "dim_b", "t_perfil", "A_perfil", "xg_perfil")
_CANT_MEDIDAS = {k: v for k, v in _campos_membro(secoes=["L"]).items() if k in _MEDIDAS_L}
_CANT = {
    "perfil_cantoneira": _e("Cantoneira", "-", 'L 3" x 1/4"',
                            ajuda="perfil L do catálogo: abas, espessura, área e x̄ (excentricidade) vêm dele"),
    "aba_ligada": _e("Aba ligada ao gusset", "-", "maior", opcoes=["maior", "menor"],
                     ajuda="só muda alguma coisa em cantoneira de abas desiguais"),
    **{k: v for k, v in _campos_membro(secoes=["L"]).items() if k not in _MEDIDAS_L + ("secao", "tf_perfil")},
    **_COMUNS,
    "trelica": _e("Treliça", "-", "plana", opcoes=["plana", "espacial"]),
    "comprimento": _e("Comprimento da barra", "mm", 1500.0, min=0.0, max=1e5,
                      ajuda="entre pontos de trabalho; compressão: verifica a barra por E.1.4; 0 = não verifica"),
    "fadiga": _e("Sujeita à fadiga", "-", False),
}
_CANT["g_aba"] = dict(_CANT["g_aba"], ajuda="do talão (canto) à linha de furos; 0 = gabarito usual da aba (AISC/Gerdau)")
# gabaritos usuais na aba (pol): AISC Manual tab. 1-7A "workable gages" [conferir]; Gerdau 2.4.5: 45 mm em L76, 65 mm em
# L102 (= arredondados para cima a 5 mm)
_GABARITOS_POL = ((8, 4.5), (7, 4.0), (6, 3.5), (5, 3.0), (4, 2.5), (3.5, 2.0), (3, 1.75), (2.5, 1.375), (2, 1.125),
                  (1.75, 1.0), (1.5, 0.875), (1.25, 0.75), (1, 0.625))


def gabarito_usual(aba_mm):
    """Linha de furos a partir do talão (mm) para a aba `aba_mm`: maior aba tabelada ≤ a aba, para cima a 5 mm."""
    g = next((g for b, g in _GABARITOS_POL if aba_mm >= b * 25.4 - 0.5), None)
    return aba_mm / 2 if g is None else math.ceil(g * 25.4 / 5 - 1e-9) * 5


def cantoneira_catalogo(nome, aba="maior"):
    """Cantoneira do catálogo → medidas da ligação (mm): dim_a (aba ligada), dim_b, t_perfil, A_perfil (mm², tabelada) e
    xg_perfil = ec (face da aba ligada → centroide, 5.2.5), mais a Secao (m) para a E.1.4."""
    try:
        s = secoes.catalogo(str(nome))
    except KeyError:
        raise ValueError(f"perfil_cantoneira: '{nome}' não está no catálogo") from None
    p = s.props
    if p.get("tipo") != "L":
        raise ValueError(f"perfil_cantoneira: '{nome}' não é cantoneira (L)")
    b, h, t = p["b"], p["h"], p["t"]
    lig_h = (h >= b) == (aba == "maior")   # a aba h (vertical no catálogo) é a ligada?
    # secao_cantoneira: x0 = t/2 − xg (xg ao longo de b, da face da aba h), y0 = t/2 − yg
    ec = (t / 2 - p["x0"]) if lig_h else (t / 2 - p["y0"])
    a, o = (h, b) if lig_h else (b, h)
    return dict(dim_a=a / MM, dim_b=o / MM, t_perfil=t / MM, A_perfil=(p.get("A_catalogo") or s.A) / MM ** 2,
                xg_perfil=ec / MM), s


# ---------------------------------------------------------------- W8-CONTRAV (docs/v3/W8-CONTRAV.md): barras do catálogo
# Gusset e nó de treliça: o perfil de cada barra vem do catálogo (seletor do projetista, `perfis` = nomes aceitos) e as
# medidas (dim_a, dim_b, t, tf, A, ec) saem dele; dados antigos com `secao`/`dim_a` e sem perfil seguem o esquema antigo.
_FORMA = {"L": "L", "U": "U", "tubo_circular": "tubo_circular", "tubo_retangular": "tubo_retangular"}


def perfil_barra(nome, aba="maior"):
    """Perfil do catálogo da barra ligada ao gusset → medidas da ligação (mm, como os campos antigos): forma (L, U,
    tubo_circular, tubo_retangular), dim_a (aba ligada / altura da alma / D / H no plano do gusset), dim_b (outra aba /
    mesa / B), t_perfil (aba, alma ou parede), tf_perfil (mesa do U), A_perfil (tabelada, mm²) e xg_perfil = ec (5.2.5:
    face de contato → centroide; tubos: 0 = calculado por _area_ec)."""
    if str(nome).split(" ")[0] == "L":
        med, _s = cantoneira_catalogo(nome, aba)
        return dict(med, forma="L", tf_perfil=0.0)
    try:
        p = secoes.catalogo(str(nome)).props
    except KeyError:
        raise ValueError(f"perfil '{nome}' não está no catálogo") from None
    if p.get("tipo") == "Ue":
        raise ValueError(f"perfil '{nome}': U enrijecido (Ue) não é aceito na barra do gusset — os lábios de borda e a "
                         "distorção da seção (NBR 14762) não são tratados aqui; use U simples (UPN, U laminado, U dobrado), "
                         "2L ou tubo")
    forma = _FORMA.get(p.get("tipo"))
    if forma not in ("U", "tubo_circular", "tubo_retangular"):
        raise ValueError(f"perfil '{nome}': a barra do gusset é L, 2L, U ou tubo (não {p.get('tipo')})")
    A = (p.get("A_catalogo") or p["A"]) / MM ** 2
    if forma == "U":   # U dobrado só tem t (tw = tf = t); o laminado tem tw e tf
        t = p.get("t")
        return dict(forma=forma, dim_a=p["h"] / MM, dim_b=p["b"] / MM, t_perfil=(p.get("tw") or t) / MM,
                    tf_perfil=(p.get("tf") or t) / MM, A_perfil=A, xg_perfil=p["xg"] / MM, formado=t is not None)
    return dict(forma=forma, dim_a=p["h"] / MM, dim_b=p["b"] / MM, t_perfil=p["t"] / MM, tf_perfil=0.0, A_perfil=A,
                xg_perfil=0.0)


def _medidas(d, s, campo):
    """Medidas da barra `s` (sufixo) a partir de d[campo] e do arranjo (dupla = 2L) → chaves antigas com o sufixo."""
    med = perfil_barra(d[campo], d.get(f"aba_ligada{s}", "maior"))
    forma = med.pop("forma")
    if d[f"arranjo{s}"] == "dupla" and forma != "L":
        raise ValueError(f"{campo}: arranjo duplo só para cantoneira (2L); '{d[campo]}' é {forma}")
    med["secao"] = "2L" if d[f"arranjo{s}"] == "dupla" else forma
    med.setdefault("formado", False)   # U dobrado (parede fina): checagens da NBR 14762 em _membro
    return {k + s: v for k, v in med.items()}


def _legado(dados, s=""):
    """Dados antigos: medidas informadas (dim_a) e nenhum perfil do catálogo na barra."""
    campo = "perfil_diagonal" if s == "" else f"perfil_barra{s}"
    return not dados.get(campo) and bool(dados.get(f"dim_a{s}"))


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
        if isinstance(pad, str) and "opcoes" not in c:   # nome de perfil do catálogo (perfil_cantoneira)
            out[k] = str(v).strip()
        elif "opcoes" in c:
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
    return dict(nome=nome, item=item if item.startswith("NBR 14762") else _PRE + item, solicitante=float(sd),
                resistente=float(rd), unidade=unidade, taxa=t, ok=t <= 1 + 1e-9)


# ---------------------------------------------------------------- U dobrado (parede fina, NBR 14762) — W12-OUT
G14_RUP, G14_RUP0 = 1.65, 1.35   # NBR 14762:2010 9.6.2 c) ruptura da seção líquida na ligação; b) fora da ligação
G14_LIG = 1.65                   # contato/rasgamento/solda em chapa fina: φ = 0,60 do AISI S100 (1/0,60 ≈ 1,65), a favor
MF_SEM_ARRUELA = 0.75            # AISI S100 E3.3 tab. E3.3-2: cisalhamento simples sem arruelas (a favor da segurança)


def coef_Ct_frio(ec, lc):
    """NBR 14762:2010 9.6.3 (= AISI S100 E5.2): perfil aberto ligado só por um elemento (U pela alma), Ct = 1 − 1,2·x̄/ℓ ≤
    0,90, x̄ = excentricidade (face de contato → centroide) e ℓ = distância entre o primeiro e o último parafuso (ou o
    comprimento dos filetes) na direção da força. → (Ct, nota). Ct < 0,60: aumente ℓ. [item da edição a conferir]"""
    ct = min(1 - 1.2 * ec / lc, 0.90)
    return ct, ("Ct < 0,60: aumente o comprimento da ligação" if ct < 0.60 else "")


def _contato_fino(rot, F, k, nl, np_, sp, e1, db, dh, t, fu):
    """U dobrado: pressão de contato e rasgamento por parafuso na parede fina. Contato Fc = mf·C·db·t·fu/γ (AISI S100 E3.3:
    C = 3,0 se db/t < 10; 4 − 0,1·db/t até 22; 1,8 acima; mf = 0,75 sem arruelas) e rasgamento Fe = e·t·fu/γ (E3.1, e do
    centro do furo à borda do furo vizinho ou à ponta), γ = 1,65. A resistência vale o MENOR dos dois por parafuso."""
    dt = db / t
    C = 3.0 if dt < 10 else 4 - 0.1 * dt if dt <= 22 else 1.8
    fc = MF_SEM_ARRUELA * C * db * t * fu / G14_LIG
    por_linha = [min(fc, e1 * t * fu / G14_LIG)] + [min(fc, (sp - dh / 2) * t * fu / G14_LIG)] * (np_ - 1)
    return _vf(f"barra{rot}: contato e rasgamento na parede fina (t = {t / MM:.2f} mm)", k * F, nl * sum(por_linha), "kN",
               "NBR 14762:2010 9.5 (= AISI S100 E3.1/E3.3; fórmulas e γ a conferir)")


def _solda_fina(rot, nm, li, Fi, t, fu):
    """U dobrado: filete longitudinal na parede fina, AISI S100 J2.2 b): Pn = (1 − 0,01·L/t)·t·L·fu (L/t < 100) ou
    0,75·t·L·fu, γ = 1,65 (φ = 0,60)."""
    r = li / t
    Pn = (1 - 0.01 * r if r < 100 else 0.75) * t * li * fu
    return _vf(f"solda{rot}: parede fina do perfil, filete {nm}", Fi, Pn / G14_LIG, "kN",
               "NBR 14762:2010 (= AISI S100 J2.2 b; item a conferir)")


def _de_legado(x):
    """Converte as disposições de `ligacoes.py` (m) para mm no formato do contrato."""
    k = 1e3 if x["unidade"] == "m" else 1.0
    return dict(nome=x["nome"], item=_PRE + x["item"], solicitante=x["solicitante"] * k,
                resistente=x["resistente"] * k, unidade="mm" if k > 1 else x["unidade"], taxa=x["taxa"],
                ok=x["taxa"] <= 1 + 1e-9, **{c: x[c] for c in ("sentido", "parametro") if c in x})


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
    info = dict(sec=sec, a=a, b=b, t=t, tf=tf, A=A, ec=ec, npf=npf, parafusos=[], soldas=[])
    tracao = N > 0
    frio = sec == "U" and bool(d.get(f"formado{s}"))   # W12-OUT: U dobrado (parede fina) → NBR 14762 na barra
    if frio:
        av.append(f"barra{rot}: U dobrado (t = {t / MM:.2f} mm): escoamento pela NBR 8800 5.2.2 a) (γ = 1,10, igual à NBR "
                  "14762 9.6.2 a); seção líquida com Ct = 1 − 1,2·x̄/ℓ e γ = 1,65; contato, rasgamento e solda na parede "
                  "fina por AISI S100 E3/J2.2 (NBR 14762, itens a conferir). Compressão, flambagem local/distorcional e "
                  "esbeltez da barra: verifique à parte (NBR 14762)")
    if tracao:
        v.append(_vf(f"barra{rot}: escoamento da seção bruta", F, npf * A * fy / GA1, "kN", "5.2.2 a)"))
        if frio:
            v.append(_vf(f"barra{rot}: ruptura da seção bruta fora da ligação", F, npf * A * fu / G14_RUP0, "kN",
                         "NBR 14762:2010 9.6.2 b)"))
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
        if frio:   # a pressão de contato da NBR 8800 6.3.3.3 acima não vale na parede fina: soma-se a da NBR 14762
            v.append(_contato_fino(rot, F, k, nl, np_, sp, e1, db, dh, t, fu))
        if tracao:
            if frio:   # U dobrado: Ct = 1 − 1,2·x̄/ℓ e γ = 1,65 (NBR 14762 9.6.2 c / 9.6.3)
                if np_ == 1:
                    raise ValueError(f"barra{rot}: U dobrado tracionado e parafusado precisa de 2 ou mais parafusos por "
                                     "linha (Ct = 1 − 1,2·x̄/ℓ pede ℓ > 0, NBR 14762 9.6.3)")
                ct, nota = coef_Ct_frio(ec, lc)
            elif np_ == 1:
                ct, nota = 0.60, "1 parafuso por linha: Ct = 0,60 (5.2.5 pede lc; prefira ≥ 2 parafusos)"
            else:
                ct, nota = coef_Ct(sec, ec, lc)
            if nota:
                av.append(f"barra{rot}: {nota}")
            An = A - nl * dn * t
            info["Ct"] = ct
            v.append(_vf(f"barra{rot}: ruptura da seção líquida (Ct = {ct:.3f})", F,
                         npf * ct * An * fu / (G14_RUP if frio else GA2), "kN",
                         "NBR 14762:2010 9.6.2 c) / 9.6.3" if frio else "5.2.2 b) / 5.2.3 / 5.2.5"))
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
        # W8-CONTRAV: na L o talão fica a g_aba da linha de furos (não a dim_a/2): a região ligada cobre a aba inteira
        zt = (nl - 1) / 2 * gg + ga if sec != "U" else a / 2
        info.update(lc=lc, Lw=Lw, Aw=Aw, w0=w0, comp=comp, meia=max(zt, a - zt, w0 / 2 + eg), xw=e1, z_talao=zt)
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
        for nm, li, Fi in linhas:   # filetes longitudinais da ligação extrema da barra axial: β da 6.2.2.2 d)
            if Fi > 0:
                bt = L.beta_filete_longo(li, bs)
                v.append(_vf(f"solda{rot}: filete {nm} (l = {li / MM:.0f} mm" + (f", β = {bt:.3f}" if bt < 1 else "")
                             + ")", Fi, Fw * bt * li, "kN", "6.2.5 / tabela 8" + (" / 6.2.2.2 d)" if bt < 1 else "")))
                if frio:
                    v.append(_solda_fina(rot, nm, li, Fi, t, fu))
        if sec != "barra_redonda":
            v += [_de_legado(L._minimo(f"solda{rot}: dimensão mínima", L.filete_minimo(min(t, tg)), bs,
                                       "6.2.6.2.1 / tabela 10", f"b_solda{s}")),
                  _de_legado(L._maximo(f"solda{rot}: dimensão máxima (borda do perfil)", bs, L.filete_maximo(t),
                                       "6.2.6.2.2", f"b_solda{s}"))]
        v.append(_de_legado(L._minimo(f"solda{rot}: comprimento mínimo (4b e 40 mm)", max(4 * bs, 0.040),
                                      min(l1, l2), "6.2.6.2.3")))
        if tracao:
            if sec in ("tubo_circular", "tubo_retangular"):
                ct, nota = coef_Ct(sec, ec, lc, D=a)
                An = A - 2 * t * (tg + FOLGA_RASGO * MM)
            else:
                ct, nota = coef_Ct_frio(ec, lc) if frio else coef_Ct(sec, ec, lc)
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
        info.update(lc=lc, Lw=Lw, Aw=Aw, w0=w0, comp=max(l1, l2) + 10 * MM, meia=a / 2 + 10 * MM, xw=0.0, z_talao=a / 2)
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
        t_, fol = info["t"], 0.004   # fol: 4 mm entre a face da aba e a cabeça (arruela)
        pega = p["pega"] + fol       # cabeça → fim da pilha: aba + gusset (+ 2ª cantoneira no 2L)
        pilha = [[fol, fol + t_]] + ([[fol + t_ + tg, fol + 2 * t_ + tg]] if info["npf"] == 2 else [])
        comp = _P.comprimento(pega, d_mm * MM)   # catálogo: pega + arruela + porca + 2 fios (o render não encurta a pega)
        par.append(dict(d=d_mm, comprimento=round(comp / MM, 1), pega=round(pega / MM, 1),
                        pilha=[[round(x / MM, 1), round(y / MM, 1)] for x, y in pilha],   # peças apertadas fora de `chapas` (mm a partir da cabeça)
                        origem=[X / MM, y0 / MM, Z / MM], eixo=[0.0, -1.0, 0.0]))
    for s_ in info["soldas"]:
        a_, b_ = loc(s_["x0"], s_["z"]), loc(s_["x1"], s_["z"])
        y = s_["lado"] * tg / 2
        sol.append(dict(tipo="filete", perna=s_["perna"] / MM,
                        pontos=[[a_[0] / MM, y / MM, a_[1] / MM], [b_[0] / MM, y / MM, b_[1] / MM]]))
    return par, sol


def _cruzam(A, B, folga=0.0):
    """Polígonos convexos (anti-horários, m) a menos de `folga` um do outro? Separação por eixos (normais das arestas)."""
    for P in (A, B):
        for i in range(len(P)):
            ex, ez = P[(i + 1) % len(P)][0] - P[i][0], P[(i + 1) % len(P)][1] - P[i][1]
            n = math.hypot(ex, ez)
            if n < 1e-12:
                continue
            pa, pb = [(x * -ez + z * ex) / n for x, z in A], [(x * -ez + z * ex) / n for x, z in B]
            if max(pa) + folga <= min(pb) or max(pb) + folga <= min(pa):
                return False
    return True


def _whitmore_cabe(casca, info, recuo, dirv, perp, av, rot=""):
    """Aviso se as pontas da seção de Whitmore saem do contorno (convexo, anti-horário) do gusset."""
    n = len(casca)
    for P in _pontas_whitmore(info, recuo, dirv, perp):
        if n >= 3 and any((casca[(i + 1) % n][0] - casca[i][0]) * (P[1] - casca[i][1])
                          - (casca[(i + 1) % n][1] - casca[i][1]) * (P[0] - casca[i][0]) < -1e-9 for i in range(n)):
            av.append(f"gusset{rot}: a seção de Whitmore ({info['Lw'] / MM:.0f} mm) sai do contorno da chapa — "
                      "a resistência calculada fica superestimada; aumente o gusset ou recorte a largura")
            return


def _cruz(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _caixa(nome, campo, O, ex, ez, x0, x1, y0, y1, z0, z1):
    """W8-CONTRAV: placa [x0, x1] × [y0, y1] × [z0, z1] (mm) no referencial da barra (O; ex ao longo dela, ez no plano do
    gusset, y normal ao gusset) → item de `perfis_barra`, no formato de `chapas` (origem numa face; sólido até origem +
    espessura·normal). A normal é a direção mais fina (y ou z)."""
    ey = (0.0, 1.0, 0.0)

    def P(x, y, z):
        return [O[i] + x * ex[i] + y * ey[i] + z * ez[i] for i in range(3)]
    if y1 - y0 <= z1 - z0:
        n, t, o = list(ey), y1 - y0, P(x0, y0, 0.0)
        cantos = [P(x, y0, z) for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))]
    else:
        n, t, o = list(ez), z1 - z0, P(x0, 0.0, z0)
        cantos = [P(x, y, z0) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    u, v = list(ex), _cruz(n, ex)
    pr = lambda p, e: sum((p[i] - o[i]) * e[i] for i in range(3))  # noqa: E731
    return dict(nome=nome, perfil=nome, campo=campo, contorno=[[round(pr(p, u), 2), round(pr(p, v), 2)] for p in cantos],
                origem=[round(x, 3) for x in o], normal=[round(x, 12) for x in n], u=[round(x, 12) for x in u],
                espessura=round(t, 3))


def _nome_barra(d, s, campo):
    nome = d.get(campo) or f"{d[f'secao{s}']} {d[f'dim_a{s}']:g}x{d.get(f'dim_b{s}') or d[f'dim_a{s}']:g}"
    return "2" + nome if d.get(f"secao{s}") == "2L" and not nome.startswith("2") else nome


def _geo_perfil(info, x0, x1, O, ex, ez, tg, campo, nome):
    """W8-CONTRAV: a barra ligada ao gusset desenhada com as medidas reais (mm), da ponta (x0) até x1: `perfis_barra` (placas:
    abas da L/2L encostadas nas faces do gusset com o talão em z_talao, alma e mesas do U numa face, paredes do tubo
    retangular em volta do gusset) e `tubos` (tubo circular com o gusset no rasgo; barra redonda sobre a face)."""
    a, b, t, tf = (info[k] / MM for k in ("a", "b", "t", "tf"))
    zt, h, sec = info.get("z_talao", info["a"] / 2) / MM, tg / 2 / MM, info["sec"]
    cx = lambda *k: _caixa(nome, campo, O, ex, ez, x0, x1, *k)  # noqa: E731
    barras, tubos = [], []
    if sec in ("L", "2L"):
        for lado in ((1,) if sec == "L" else (1, -1)):
            y0, y1 = sorted((lado * h, lado * (h + t))), sorted((lado * h, lado * (h + b)))
            barras += [cx(*y0, zt - a, zt), cx(*y1, zt - t, zt)]
    elif sec == "U":
        barras += [cx(h, h + t, -a / 2, a / 2), cx(h, h + b, a / 2 - tf, a / 2), cx(h, h + b, -a / 2, -a / 2 + tf)]
    elif sec == "tubo_retangular":
        barras += [cx(-b / 2, b / 2, a / 2 - t, a / 2), cx(-b / 2, b / 2, -a / 2, -a / 2 + t),
                   cx(b / 2 - t, b / 2, -a / 2 + t, a / 2 - t), cx(-b / 2, -b / 2 + t, -a / 2 + t, a / 2 - t)]
    else:   # tubo circular (gusset no rasgo, eixo no plano médio) ou barra redonda soldada sobre a face +y
        yc = h + a / 2 if sec == "barra_redonda" else 0.0
        tubos.append(dict(nome=nome, grupo="viga", campo=campo, origem=[O[i] + x0 * ex[i] + (yc if i == 1 else 0.0)
                                                                        for i in range(3)],
                          eixo=list(ex), comprimento=x1 - x0, d=a, t=a / 2 if sec == "barra_redonda" else t, corte=None))
    return barras, tubos


def _linha_whitmore(info, recuo, O, ex, ez, tg, rot=""):
    """W8-CONTRAV: espraiamento de 30° (linhas tracejadas) do fim da ligação até a seção de Whitmore, na face −y."""
    x1, x0 = (recuo + info["xw"] + info["lc"]) / MM, (recuo + info["xw"]) / MM
    w0, Lw, y = info["w0"] / 2 / MM, info["Lw"] / 2 / MM, -tg / 2 / MM
    P = lambda x, z: [round(O[i] + x * ex[i] + z * ez[i] + (y if i == 1 else 0.0), 2) for i in range(3)]  # noqa: E731
    return dict(nome=f"Whitmore{rot}", estilo="tracejada", texto=f"Whitmore{rot} {info['Lw'] / MM:.0f}",
                pontos=[P(x1, w0), P(x0, Lw), P(x0, -Lw), P(x1, -w0)])


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

def _x_ponta(tipo, dados, s=""):
    """Posição da ponta da diagonal no eixo (m), pela geometria de pontas (pontas.py: face do apoio + folga)."""
    d = dict(dados or {})
    if s:   # nó de treliça: a barra s vira a "barra 1" para pontas_de
        d.update(dim_a_1=d.get(f"dim_a{s}"), dim_b_1=d.get(f"dim_b{s}"), theta_1=d.get(f"theta{s}", 45.0),
                 perfil_barra_1=d.get(f"perfil_barra{s}"))
        t = math.radians(float(d["theta_1"]))
        if abs(math.sin(t)) < 1e-6:   # barra ao longo do banzo: sem ponta de gusset
            return 0.0
    try:
        pts = L.pontas_de(tipo, d, d.get("contexto_pontas"))
    except ValueError:   # BarraCurta etc.: com_pontas trata; aqui só a posição
        return 0.0
    return pts[0]["plano"]["ponto"][0] if pts else 0.0


def _ponta_em(x, barra=0):
    """Ponta da diagonal no recuo usado no cálculo (desenho = cálculo)."""
    return dict(barra=barra, extremo="i", plano=dict(ponto=[x, 0.0, 0.0], normal=[-1.0, 0.0, 0.0]), folga=0.0,
                recortes=[])


def _auto_gusset(d, info, c, theta, x_min=0.0):
    """recuo, Lh e Lv automáticos: a região ligada fica a 2·tg das faces (folga da linha de dobra) e nunca antes da
    ponta da diagonal (x_min, pontas.py)."""
    dirv, perp = _frame(theta)
    folga = max(2 * c["tg"], d.get("_folga", 0.0))
    eb, ec = d["eb"] * MM, d["ec"] * MM
    hw, ap = info["meia"], d["apoio"]
    s, co = math.sin(theta), math.cos(theta)
    hwm, xw = info["Lw"] / 2, info["xw"]
    req = [0.0]
    if ap in ("viga-pilar", "pilar"):
        req += [(ec + folga + hw * co) / s, (ec + hwm * co) / s - xw]
    if ap in ("viga-pilar", "viga", "base"):
        req += [(eb + folga + hw * s) / co, (eb + hwm * s) / co - xw]
    recuo = d["recuo"] * MM or math.ceil(max(req + [x_min]) / 0.005) * 0.005
    hmin = ec if ap in ("viga-pilar", "pilar") else -math.inf
    vmin = eb if ap != "pilar" else -math.inf
    pts = _regiao(info, recuo, dirv, perp)
    pts += [(max(h, hmin), max(v_, vmin)) for h, v_ in _pontas_whitmore(info, recuo, dirv, perp, folga)]
    Lh = d["Lh"] * MM or max(0.05, math.ceil((max(p[0] for p in pts) - ec) / 0.01) * 0.01)
    Lv = d["Lv"] * MM or max(0.05, math.ceil((max(p[1] for p in pts) - eb) / 0.01) * 0.01)
    return recuo, Lh, Lv, pts


def _ler_barras(entradas, entradas_leg, dados, tipo, barras):
    """W8-CONTRAV: esquema novo (perfil do catálogo) ou antigo (medidas soltas, sem perfil) → d com as medidas antigas
    (dim_a, t_perfil...) de cada barra ativa. barras: [(sufixo, campo do perfil)]. Devolve (d, novo)."""
    bruto = dict(dados or {})
    if any(_legado(bruto, s) for s, _c in barras):
        return _ler(entradas_leg, bruto, tipo), False
    d = _ler(entradas, bruto, tipo)
    for s, campo in barras:
        if d.get(f"arranjo{s}", "simples") == "nenhuma":
            d[f"secao{s}"] = "nenhum"
            continue
        d.update(_medidas(d, s, campo))
        if d[f"secao{s}"] in ("L", "2L") and not d[f"g_aba{s}"]:
            d[f"g_aba{s}"] = gabarito_usual(d[f"dim_a{s}"])
    return d, True


def verificar_gusset(dados, esforcos):
    d, novo = _ler_barras(_GUSSET, _GUSSET_LEG, dados, "gusset_contraventamento", [("", "perfil_diagonal")])
    d["_folga"] = FOLGA_MONTAGEM if novo else 0.0
    N = _esf(esforcos)["N"]
    c = _comum(d)
    v, av = [], list(c["avisos"])
    # 6.1.5.2: mínimo de 45 kN; tirante de barra redonda é exceção
    _k, aviso = L.fator_minimo_6152(N, isenta=d["secao"] == "barra_redonda")
    if aviso:
        N = math.copysign(N_MINIMO, N or 1.0)
        av.append(aviso)
    theta = math.radians(d["theta"])
    info = _membro(d, "", N, c, v, av)
    pdados = dict(dados or {}, dim_a=d["dim_a"], dim_b=d["dim_b"]) if novo else dados   # pontas.py lê as medidas
    recuo, Lh, Lv, pts = _auto_gusset(d, info, c, theta, _x_ponta("gusset_contraventamento", pdados))
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
    for x in par:
        x["campo"] = "d_mm"
    for x in sol:
        x["campo"] = "b_solda"
    trechos = ([(canto[0], canto[1])] if ap != "pilar" else []) + \
              ([(canto[0], canto[-1])] if ap in ("viga-pilar", "pilar") else [])
    for P0, P1 in trechos:
        (x0, z0), (x1, z1) = _para_local([P0, P1], dirv, perp)
        for lado in (1, -1):
            y = lado * tg / 2 / MM
            sol.append(dict(tipo="filete", perna=bi / MM, campo="b_interface", pontos=[[x0 / MM, y, z0 / MM], [x1 / MM, y, z1 / MM]]))
    geo = dict(chapas=[_chapa("gusset", cont, tg)], cantoneiras=[], parafusos=par, soldas=sol,
               recortes=[dict(barra="diagonal", tipo="rasgo", parametros=dict(
                   # G6: o rasgo cobre a chapa dentro do tubo (l1 e a borda do contorno além dele) + a folga
                   comprimento=round((max(info["lc"], max(p[0] for p in cont) - recuo) + FOLGA_RASGO * MM) / MM, 1),
                   largura=round(tg / MM + FOLGA_RASGO, 1)))] if info["sec"].startswith("tubo") else [],
               ponto_trabalho=[0.0, 0.0, 0.0], recuo=recuo / MM, Lh=Lh / MM, Lv=Lv / MM,
               largura_whitmore=info["Lw"] / MM, pontas=[_ponta_em(recuo)])
    geo.update(_orientacao(ap, dirv, perp))
    # W8-CONTRAV: a diagonal desenhada (só no projetista: `perfis_barra`/`tubos`/`linhas` não entram no Realista, que desenha a
    # barra do modelo); membros = [] para o perfil da diagonal não virar "viga" no desenho
    barras, tubos = _geo_perfil(info, recuo / MM, (recuo + info["comp"] + max(0.15, 2 * info["a"])) / MM, (0.0, 0.0, 0.0),
                                (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), tg, "perfil_diagonal", _nome_barra(d, "", "perfil_diagonal"))
    (hx, hz), (vx, vz) = _para_local([(1.0, 0.0), (0.0, 1.0)], dirv, perp)   # vista: viga na horizontal, pilar na vertical
    geo.update(perfis_barra=barras, tubos=tubos, membros=[], linhas=[_linha_whitmore(info, recuo, (0.0, 0.0, 0.0), (1.0, 0.0, 0.0),
                                                                                (0.0, 0.0, 1.0), tg)],
               vista=dict(x=[round(hx, 12), 0.0, round(hz, 12)], z=[round(vx, 12), 0.0, round(vz, 12)]))
    if d.get("x_max"):   # G6 r3: chapa e parafusos cabem no trecho da diagonal disponível para esta ponta?
        alc = max([p[0] for p in geo["chapas"][0]["contorno"]] + [p["origem"][0] for p in par])
        v.append(_vf("geometria: comprimento disponível na barra", alc, d["x_max"], "mm", "detalhamento"))
    for r in geo["recortes"]:   # G6: o rasgo também vai na ponta do tubo (m), para o Realista e a exportação abrirem o tubo
        if r["tipo"] == "rasgo":
            geo["pontas"][0]["recortes"].append(dict(tipo="rasgo", largura=r["parametros"]["largura"] * MM,
                                                     comprimento=r["parametros"]["comprimento"] * MM))
    extra = dict(forcas_interface=forcas)
    if "dica" in info:
        extra["dica_barra"] = info["dica"]
    return _resultado(v, av, geo, **extra)


def _orientacao(ap, dirv, perp):
    """G5: eixo do apoio e face onde a chapa fica, no referencial local (x ao longo da diagonal, chapa no plano xz,
    normal [0, −1, 0]). `apoio_dir` = eixo do pilar (pilar, viga-pilar) ou da viga/base; a chapa fica do lado
    `lado` · (normal × apoio_dir)."""
    eixo, lado_hv = ((0.0, 1.0), (1.0, 0.0)) if ap in ("pilar", "viga-pilar") else ((1.0, 0.0), (0.0, 1.0))
    (ax, az), (sx, sz) = _para_local([eixo, lado_hv], dirv, perp)
    # normal [0, −1, 0] × [ax, 0, az] = [−az, 0, ax]
    lado = 1 if -az * sx + ax * sz > 0 else -1
    return dict(apoio_dir=[round(ax, 12), 0.0, round(az, 12)], lado=lado)


# ---------------------------------------------------------------- 2. nó de treliça com gusset

def verificar_no_trelica(dados, esforcos):
    d, novo = _ler_barras(_NO, _NO_LEG, dados, "no_trelica_gusset", [(f"_{i}", f"perfil_barra_{i}") for i in "1234"])
    banzo = None
    if novo:   # W8-CONTRAV: eixo do banzo à face da mesa = d/2 do perfil do banzo (banzo contínuo)
        try:
            banzo = secoes.catalogo(d["perfil_banzo"]).props if d["banzo"] == "continuo" else None
        except KeyError:
            raise ValueError(f"perfil_banzo: '{d['perfil_banzo']}' não está no catálogo") from None
        if banzo is not None and banzo.get("tipo") != "I":
            raise ValueError(f"perfil_banzo: '{d['perfil_banzo']}' não é perfil I (alma no plano da treliça)")
        d["e_banzo"] = banzo["h"] / 2 / MM if banzo else 0.0
    pdados = dict(dados or {}, **{k: x for k, x in d.items() if k.startswith(("dim_a_", "dim_b_"))}) if novo else dados
    e = _esf(esforcos, ("N", "N2", "N3", "N4"))
    c = _comum(d)
    v, av = [], list(c["avisos"])
    tg, eb = c["tg"], d["e_banzo"] * MM
    fol = max(2 * tg, FOLGA_MONTAGEM if novo else 0.0)
    ativos = [i for i in "1234" if d[f"secao_{i}"] != "nenhum"]
    forcas = {i: e["N" if i == "1" else f"N{i}"] for i in ativos}
    pts_all, regs, membros, H, V = [], [], {}, 0.0, 0.0
    for i in ativos:
        Ni = forcas[i]
        # 6.1.5.2: tirante de barra redonda é exceção ao mínimo de 45 kN
        _k, aviso = L.fator_minimo_6152(Ni, isenta=d[f"secao_{i}"] == "barra_redonda" and Ni >= 0)
        Nd = math.copysign(N_MINIMO, Ni or 1.0) if aviso else Ni
        if aviso:
            av.append(f"barra {i}: {aviso}")
        info = _membro(d, f"_{i}", Nd, c, v, av, rot=f" {i}")
        th = math.radians(d[f"theta_{i}"])
        # direção a partir do banzo: (cos, sin) no referencial (h ao longo do banzo, v para o gusset)
        dv, pp = (math.cos(th), math.sin(th)), (-math.sin(th), math.cos(th))
        sv = abs(math.sin(th))
        x_face = eb / sv if sv > 1e-6 else 0.0
        cs = abs(math.cos(th))
        auto = max((eb + fol + info["meia"] * cs) / sv, (eb + info["Lw"] / 2 * cs) / sv - info["xw"])             if sv > 1e-6 else fol
        auto = max(auto, _x_ponta("no_trelica_gusset", pdados, f"_{i}"))
        recuo = d[f"recuo_{i}"] * MM or math.ceil(auto / 0.005) * 0.005
        if not d[f"recuo_{i}"]:   # W8-CONTRAV: recuo automático afasta a barra das já postas (regiões ligadas sem choque)
            for _n in range(400):
                if not any(_cruzam(_hull(_regiao(info, recuo, dv, pp)), _hull(_regiao(*m)), fol) for m in membros.values()):
                    break
                recuo += 0.005
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
        pts_all += reg + [(h, max(v_, vmin)) for h, v_ in _pontas_whitmore(info, recuo, dv, pp, fol)]
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
        for x in p_:
            x["campo"] = "d_mm"
        for x in s_:
            x["campo"] = f"b_solda_{i}"
        par += p_
        sol += s_
    for lado in (1, -1):
        if canto:
            a0, a1 = to_loc(canto[0]), to_loc(canto[1])
            sol.append(dict(tipo="filete", perna=d["b_interface"], campo="b_interface",
                            pontos=[[a0[0] / MM, lado * tg / 2 / MM, a0[1] / MM],
                                    [a1[0] / MM, lado * tg / 2 / MM, a1[1] / MM]]))
    geo = dict(chapas=[_chapa("gusset", cont, tg)], cantoneiras=[], parafusos=par, soldas=sol, recortes=[],
               ponto_trabalho=[0.0, 0.0, 0.0],
               barras={i: dict(recuo=m[1] / MM, largura_whitmore=m[0]["Lw"] / MM) for i, m in membros.items()},
               pontas=[_ponta_em(m[1], int(i)) for i, m in membros.items()])   # cada uma no referencial da sua barra
    # W8-CONTRAV: barras (abas/mesas reais), banzo e Whitmore desenhados, como no gusset (só no projetista)
    pb, tb, lin = [], [], []
    for i, (info, recuo, dv, pp) in membros.items():
        ex, ez = (lambda q: (q[0], 0.0, q[1]))(to_loc(dv)), (lambda q: (q[0], 0.0, q[1]))(to_loc(pp))
        p_, t_ = _geo_perfil(info, recuo / MM, (recuo + info["comp"] + max(0.15, 2 * info["a"])) / MM, (0.0, 0.0, 0.0),
                             ex, ez, tg, f"perfil_barra_{i}", _nome_barra(d, f"_{i}", f"perfil_barra_{i}"))
        pb += p_
        tb += t_
        lin.append(_linha_whitmore(info, recuo, (0.0, 0.0, 0.0), ex, ez, tg, f" {i}"))
    if banzo:   # perfil I com a alma no plano do gusset e a mesa de cima em v = e_banzo, além do gusset dos dois lados
        hb, bb, tw, tf = (banzo[k] / MM for k in ("h", "b", "tw", "tf"))
        ex, ez = (lambda q: (q[0], 0.0, q[1]))(to_loc((1.0, 0.0))), (lambda q: (q[0], 0.0, q[1]))(to_loc((0.0, 1.0)))
        xs = [P[0] for P in casca]
        x0, x1 = min(xs) / MM - max(150.0, hb), max(xs) / MM + max(150.0, hb)
        cx = lambda *k: dict(_caixa(d["perfil_banzo"], "perfil_banzo", (0.0, 0.0, 0.0), ex, ez, x0, x1, *k),  # noqa: E731
                             camada=0)   # perfil (o desenho põe por baixo do gusset, na cor dos perfis)
        pb += [cx(-tw / 2, tw / 2, -hb / 2 + tf, hb / 2 - tf), cx(-bb / 2, bb / 2, hb / 2 - tf, hb / 2),
               cx(-bb / 2, bb / 2, -hb / 2, -hb / 2 + tf)]
    (hx, hz), (vx, vz) = to_loc((1.0, 0.0)), to_loc((0.0, 1.0))   # vista: banzo na horizontal, gusset para cima
    geo.update(perfis_barra=pb, tubos=tb, linhas=lin, membros=[],
               vista=dict(x=[round(hx, 12), 0.0, round(hz, 12)], z=[round(vx, 12), 0.0, round(vz, 12)]))
    return _resultado(v, av, geo, forcas_interface=forcas_if)


# ---------------------------------------------------------------- 3. tirante de barra redonda

def tracao_barra_rosqueada(D, fy, fu):
    """NBR 8800 6.3.3.1: Ft,Rd = 0,75·Ab·fu/γa2 (área efetiva da rosca), limitada a Ab·fy/γa1 (5.2.2 a)
    para barras redondas rosqueadas) [PFEIL eq. 2.2, p.53]."""
    Ab = math.pi * D ** 2 / 4
    return dict(Ab=Ab, rosca=0.75 * Ab * fu / GA2, escoamento=Ab * fy / GA1)


def pino_garfo(F, d, fy, t, t_orelha=0.0, folga=0.0):
    """Pino de garfo (2 orelhas, gusset central de espessura t), NBR 8800 6.4 (unidades m, kN, kN/m²).
    6.4.1: contato uniforme em cada parte → M = F/2·(t_orelha/2 + folga + t/4) no meio do pino [Pfeil 9.10.4,
    p. 259]; 6.4.2.1 MRd = 1,2·W·fy/γa1; 6.4.2.2 Fv,Rd = 0,6·(0,75·Ag)·fy/γa1 por plano (2 planos);
    6.4.2.3 σRd = 1,5·fy/γa1 contra a maior tensão de contato F/(d·t) e F/2/(d·t_orelha)."""
    Ag = math.pi * d ** 2 / 4
    sig = F / (d * t)
    if t_orelha > 0:
        sig = max(sig, F / 2 / (d * t_orelha))
    return dict(M=F / 2 * (t_orelha / 2 + folga + t / 4), MRd=1.2 * (math.pi * d ** 3 / 32) * fy / GA1,
                FvRd=2 * 0.6 * 0.75 * Ag * fy / GA1, sigma=sig, sigmaRd=1.5 * fy / GA1)


# W8-CONTRAV: desenho do tirante (mm; x ao longo da barra, saindo do nó). Peças comerciais com as proporções usuais
# [conferir no fabricante]: porca sextavada pesada ASTM A563 / ASME B18.2.2 (abertura 1,5·D + 3,2 mm, altura ≈ D),
# contraporca (altura 0,6·D), arruela ASTM F436 (Ø 2·D × 4 mm), 3 fios (≈ 0,4·D) além da contraporca e rosca até 2·D
# além da chapa (folga de ajuste). Só o trecho junto ao nó é desenhado (10·D); o comprimento vai na cota.
ARRUELA_MM = 4.0


def _disco(nome, campo, x, n, raio, esp, lados):
    """Prisma de `lados` faces (sextavado, círculo) ⟂ x: face em x, sólido para o lado n·x."""
    pts = [[round(raio * math.cos(2 * math.pi * (k + 0.5) / lados), 2),
            round(raio * math.sin(2 * math.pi * (k + 0.5) / lados), 2)] for k in range(lados)]
    return dict(nome=nome, campo=campo, contorno=pts, origem=[round(x, 2), 0.0, 0.0], normal=[float(n), 0.0, 0.0],
                u=[0.0, 1.0, 0.0], espessura=round(esp, 2))


def _cota(a, b, lado, texto):
    """W8-CONTRAV: cota explícita (contrato `linhas`, estilo "cota"): de a até b (mm), traçada para o lado `lado`."""
    return dict(estilo="cota", pontos=[[round(x, 2) for x in a], [round(x, 2) for x in b]], lado=list(lado), texto=texto)


def _barra_tubo(D, x0, x1, nome="barra"):
    return dict(nome=nome, grupo="viga", campo="D_barra", origem=[round(x0, 2), 0.0, 0.0], eixo=[1.0, 0.0, 0.0],
                comprimento=round(x1 - x0, 2), d=round(D, 3), t=round(D / 2, 3), corte=None)


def _geo_rosca(D, t, dh, comp):
    """Chapa de apoio (face em x = 0, do lado da barra; espessura para −x), arruela, porca e contraporca atrás dela, a barra
    (tubo maciço) e a rosca (tubo no diâmetro do núcleo ≈ 0,85·D: no desenho, a linha fina da rosca)."""
    H, Hc, F = D, 0.6 * D, 1.5 * D + 3.2
    x = [-t, -t - ARRUELA_MM, -t - ARRUELA_MM - H, -t - ARRUELA_MM - H - Hc]
    ponta, Ld = x[3] - 0.4 * D, 10 * D
    chapas = [dict(nome="chapa de apoio da porca", contorno=[[-2 * D, -2 * D], [2 * D, -2 * D], [2 * D, 2 * D], [-2 * D, 2 * D]],
                   origem=[0.0, 0.0, 0.0], normal=[-1.0, 0.0, 0.0], u=[0.0, 1.0, 0.0], espessura=t, furo=round(dh, 2)),
              _disco("arruela", "D_barra", x[0], -1, D, ARRUELA_MM, 24),
              _disco("porca", "D_barra", x[1], -1, F / math.sqrt(3), H, 6),
              _disco("contraporca", "D_barra", x[2], -1, F / math.sqrt(3), Hc, 6)]
    z = 2 * D
    n = lambda v: f"{v:.1f}".rstrip("0").rstrip(".").replace(".", ",")  # noqa: E731
    linhas = [_cota((0.0, D / 2, D / 2), (5 * D, D / 2, D / 2), (0, 1, 1), f"barra Ø{n(D)}" + (f" · L = {comp:.0f}" if comp else "")),
              _cota((ponta, -z, -z), (2 * D, -z, -z), (0, -1, -1), f"rosca {2 * D - ponta:.0f}"),
              _cota((x[3], z, z), (0.0, z, z), (0, 1, 1), f"{n(t)} + {n(ARRUELA_MM)} + {n(H)} + {n(Hc)}")]
    return dict(chapas=chapas, tubos=[_barra_tubo(D, ponta, Ld), _barra_tubo(0.85 * D, ponta, 2 * D, "rosca")],
                linhas=linhas, porcas=[], barra=dict(D=D, eixo=[1.0, 0.0, 0.0], ponta=round(ponta, 2),
                                                     rosca=round(2 * D - ponta, 2), comprimento=comp))


def _geo_garfo(D, t, dp, a, bw, te, gf, comp):
    """Pino no nó (origem); gusset (plano xz) do apoio (x = −3·dp) até a borda a além do pino, no sentido da força (+x);
    garfo comercial: duas orelhas (espessura t_orelha; 0 = 0,5·dp só no desenho) com folga gf de cada lado do gusset,
    corpo e a barra."""
    te = te or 0.5 * dp
    yo, w, xc = t / 2 + gf + te, 1.1 * dp, a + 10.0
    chapas = [dict(nome="gusset", campo="t_chapa", contorno=[[-3 * dp, -bw / 2], [a, -bw / 2], [a, bw / 2], [-3 * dp, bw / 2]],
                   origem=[0.0, t / 2, 0.0], normal=[0.0, -1.0, 0.0], u=[1.0, 0.0, 0.0], espessura=t)]
    for lado in (1, -1):
        chapas.append(dict(nome="garfo (orelha)", campo="t_orelha", contorno=[[0.0, -w], [xc + w, -w], [xc + w, w], [0.0, w]],
                           origem=[-w, lado * (t / 2 + gf), 0.0], normal=[0.0, float(lado), 0.0], u=[1.0, 0.0, 0.0],
                           espessura=te))
    chapas.append(dict(nome="garfo (corpo)", campo="D_barra", contorno=[[-yo, -w], [yo, -w], [yo, w], [-yo, w]],
                       origem=[xc, 0.0, 0.0], normal=[1.0, 0.0, 0.0], u=[0.0, 1.0, 0.0], espessura=1.5 * D))
    pino = dict(d=dp, campo="d_pino", comprimento=round(2 * yo + 1.2 * dp + 6, 1), pega=round(2 * yo, 1),
                origem=[0.0, yo, 0.0], eixo=[0.0, -1.0, 0.0])
    x0, Ld = xc + 1.5 * D, xc + 1.5 * D + 10 * D
    linhas = [_cota((x0, 0.0, D / 2), (x0 + 5 * D, 0.0, D / 2), (0, 1, 1), f"barra Ø{D:.1f}".rstrip("0").rstrip(".").replace(".", ",")
                    + (f" · L = {comp:.0f}" if comp else "")),
              _cota((0.0, 0.0, -bw / 2), (a, 0.0, -bw / 2), (0, -1, -1), f"borda {a:.0f}")]
    return dict(chapas=chapas, parafusos=[pino], tubos=[_barra_tubo(D, x0, Ld)], linhas=linhas,
                barra=dict(D=D, eixo=[1.0, 0.0, 0.0], comprimento=comp))


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
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[], porcas=[], membros=[])
    if d["extremidade"] == "rosca_porca":
        dh = D + L.FURO_FOLGA
        geo.update(_geo_rosca(D / MM, t / MM, dh / MM, d["comprimento"]))
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
        fyp = L.PARAFUSOS[tipo]["fyb"]
        if tipo == "A325" and fub < 825 * MPa:
            fyp = 560   # tabela A.3: 24 < db ≤ 36 mm
        if fyp is None:
            raise ValueError(f"tipo_pino {tipo}: fy não definido na NBR 8800 tabela A.3 — pino (6.4) não verificável")
        te, gf = d["t_orelha"] * MM, d["folga_garfo"] * MM
        if te <= 0:
            av.append("garfo: espessura das orelhas não informada — flexão do pino com o momento mínimo N·t/8 "
                      "(orelhas e folgas aumentam o momento; informe t_orelha)")
        if F > 0:
            tr = L.tracao_chapa(bw * t, (bw - dn) * t, fyc, fuc)
            pn = pino_garfo(F, db, fyp * MPa, t, te, gf)
            v += [_vf("pino: cisalhamento (2 planos, 0,6·0,75·Ag·fy/γa1)", F, pn["FvRd"], "kN", "6.4.2.2"),
                  _vf("pino: flexão (contato uniforme, 1,2·W·fy/γa1)", pn["M"], pn["MRd"], "kN·m", "6.4.1 / 6.4.2.1"),
                  _vf("pino: esmagamento (1,5·fy/γa1)", pn["sigma"] / MPa, pn["sigmaRd"] / MPa, "MPa", "6.4.2.3"),
                  _vf("gusset: contato/rasgamento no furo do pino", F,
                      L.pressao_contato(a - dh / 2, t, db, fuc, d["deformacao_limitada"]), "kN", "6.3.3.3"),
                  _vf("gusset: escoamento na seção do pino", F, tr["escoamento"], "kN", "6.5.3 → 5.2.2 a)"),
                  _vf("gusset: ruptura na seção do pino", F, tr["ruptura"], "kN", "6.5.3 → 5.2.2 b)")]
        leg = []
        L._bordas(leg, db, t, a, laminada=d["borda_laminada"], nome=" (gusset, além do pino)")
        v += [_de_legado(x) for x in leg]
        av.append("garfo (clevis) e esticador: peças comerciais — use a carga de catálogo do fabricante ≥ N")
        geo.update(_geo_garfo(D / MM, t / MM, dp / MM, a / MM, bw / MM, te / MM, gf / MM, d["comprimento"]))
    if d["comprimento"] > 0:
        av.append(f"esbeltez L/r = {d['comprimento'] * MM / (D / 4):.0f}: barras redondas pré-tracionadas "
                  "dispensam L/r ≤ 300 (5.2.8); garanta a protensão com esticador")
    return _resultado(v, av, geo, Ab_mm2=r["Ab"] / MM ** 2)


# ---------------------------------------------------------------- 4. cantoneira simples em treliça leve

def verificar_cantoneira_simples(dados, esforcos):
    bruto = dict(dados or {})
    legado = not bruto.get("perfil_cantoneira") and bool(bruto.get("dim_a"))   # dados antigos: medidas informadas
    d = _ler(_CANT, bruto, "cantoneira_simples_trelica")
    sec_cat = None
    if legado:
        d.update(_ler(_CANT_MEDIDAS, bruto, "cantoneira_simples_trelica"))
        d["aba_ligada"] = "maior" if d["dim_a"] >= (d["dim_b"] or d["dim_a"]) else "menor"
        d["perfil_cantoneira"] = f"L{d['dim_a']:g}x{d['dim_b'] or d['dim_a']:g}x{d['t_perfil']:g}"
    else:
        med, sec_cat = cantoneira_catalogo(d["perfil_cantoneira"], d["aba_ligada"])
        d.update(med)
        if not d["g_aba"]:
            d["g_aba"] = gabarito_usual(d["dim_a"])
    d.update(secao="L", tf_perfil=0.0)
    N = _esf(esforcos)["N"]
    c = _comum(d)
    v, av = [], list(c["avisos"])
    _k, aviso = L.fator_minimo_6152(N)                 # 6.1.5.2
    Nd = math.copysign(N_MINIMO, N or 1.0) if aviso else N
    if aviso:
        av.append(aviso)
    info = _membro(d, "", Nd, c, v, av)
    if d["fadiga"]:
        av.append("barra sob fadiga: a excentricidade da ligação NÃO pode ser desprezada (NBR 8800 6.1.8.2) — "
                  "use modelo de pórtico com o momento N·ec")
    else:
        av.append("cantoneira sob ação estática: excentricidade entre os centroides da barra e da ligação "
                  "desprezada na ligação (NBR 8800 6.1.8.2); na barra entra por E.1.4 (compressão) "
                  "e Ct (tração)")
    a, t = d["dim_a"] * MM, d["t_perfil"] * MM
    b = (d["dim_b"] or d["dim_a"]) * MM
    aba = d["aba_ligada"] if abs(a - b) > 1e-9 else "maior"   # dim_a é a aba ligada
    dica = dict(ligacao_uma_aba=dict(aba=aba, trelica=d["trelica"]), item="E.1.4", **info["dica"])
    if d["comprimento"] > 0:
        Lb = d["comprimento"] * MM
        dica["ligacao_uma_aba"]["L"] = Lb
        if N < 0:
            if legado and d["A_perfil"]:
                av.append("E.1.4 com as propriedades de cantos vivos (A_perfil só entra na ligação)")
            rc = compressao(sec_cat or secoes.secao_cantoneira(a, b, t), materiais.aco(d["aco_perfil"]), Lb, Lb,
                            ligacao_uma_aba=dict(L=Lb, aba=aba, trelica=d["trelica"]))
            if "E14" in rc:
                v.append(_vf(f"barra: compressão com a excentricidade (E.1.4, KL = {rc['E14']['KxLx'] / MM:.0f} mm)",
                             -N, rc["NcRd"], "kN", "5.3 / E.1.4"))
                dica["E14"] = dict(KxLx_mm=rc["E14"]["KxLx"] / MM, Ne=rc["Ne"], NcRd=rc["NcRd"], chi=rc["chi"])
            av += rc.get("avisos", [])
    geo = _geo_cantoneira(d, info, c)
    return _resultado(v, av, geo, dica_barra=dica)


def _geo_cantoneira(d, info, c):
    """W7-LIG: gusset no plano xz (y ∈ [−tg/2, tg/2]); a aba ligada encosta na face +y do gusset e desce do talão; a aba
    saliente sai do talão para +y. A linha de furos (gabarito g_aba do talão) é a linha de trabalho z = 0 (6.1.8.2:
    excentricidade desprezada); na soldada, os filetes ficam no talão e na ponta da aba. O gusset cobre a aba ± 10 mm e
    vai do nó até a borda e_gusset além do último furo; a cantoneira passa além do gusset (é a barra)."""
    tg, a, b, t = c["tg"], info["a"], info["b"], info["t"]
    dirv, perp = (1.0, 0.0), (0.0, 1.0)
    recuo = d["recuo"] * MM or 2 * tg
    parafusada = d["ligacao"] == "parafusada"
    if parafusada:
        nl, gg = d["n_linhas"], d["g"] * MM
        z_talao = (nl - 1) / 2 * gg + (d["g_aba"] * MM or a / 2)
    else:   # filetes de comprimento l1 (talão, z = +a/2) e l2 (ponta, z = −a/2), como em _membro
        z_talao = a / 2
    par, sol = _geo_membro(info, recuo, dirv, perp, dirv, perp, tg, d["d_mm"])   # filetes da L: lado +y (face da aba)
    folga = 10 * MM
    x_fim = recuo + info["comp"]
    cont = [(0.0, z_talao + folga), (x_fim, z_talao + folga), (x_fim, z_talao - a - folga), (0.0, z_talao - a - folga)]
    ext = max(0.15, 2 * a)   # trecho da barra além do gusset (desenho)
    nome = d.get("perfil_cantoneira") or f"L{a / MM:g}x{b / MM:g}x{t / MM:g}"
    return dict(chapas=[_chapa("chapa de nó", cont, tg)],
                cantoneiras=[dict(perfil=nome, abas=[a / MM, b / MM], espessura=t / MM,
                                  origem=[recuo / MM, tg / 2 / MM, z_talao / MM], eixo=[1.0, 0.0, 0.0],
                                  u=[0.0, 0.0, -1.0], w=[0.0, 1.0, 0.0], comprimento=(info["comp"] + ext) / MM,
                                  talao_z=z_talao / MM, ec=info["ec"] / MM)],
                parafusos=par, soldas=sol, recortes=[], ponto_trabalho=[0.0, 0.0, 0.0])


# ---------------------------------------------------------------- sugerir (pré-dimensionamento)

def _base(entradas, perfis):
    d = {k: c["padrao"] for k, c in entradas.items()}
    d.update({k: x for k, x in (perfis or {}).items() if k in entradas})
    return d


def _face_u(pr, face):
    """W12-OUT: distância do ponto de trabalho (eixo da barra, no centroide) à face do U/Ue onde o gusset é soldado (m).
    "mesa" (padrão): a face externa da mesa, h/2 — o U com as mesas para fora do plano do gusset, o filete duplo cai na
    mesa (como no I). "alma": a face de trás da alma, xg — o U com a alma voltada ao gusset (mesas para dentro do apoio)."""
    return pr["xg"] if face == "alma" else pr["h"] / 2


def _de_catalogo(perfis, d):
    """eb/ec pela meia altura dos perfis do catálogo (perfil_viga, perfil_pilar), se informados. U/Ue: a face soldada é a da
    mesa (h/2) ou a de trás da alma (xg), escolhida em `face_u_viga` / `face_u_pilar` ("mesa" | "alma"; padrão "mesa")."""
    for chave, campo, fc in (("perfil_viga", "eb", "face_u_viga"), ("perfil_pilar", "ec", "face_u_pilar")):
        if (perfis or {}).get(chave) and campo in d and not (perfis or {}).get(campo):   # informado vence (G6)
            pr = L.perfil_pontas(perfis[chave])   # nome do catálogo ou medidas {d, bf, tw, tf} (composto, G6)
            if pr:
                face = str((perfis or {}).get(fc) or "mesa").lower()
                if face not in ("mesa", "alma"):
                    raise ValueError(f"{fc}: use 'mesa' ou 'alma', recebido {face!r}")
                d[campo] = (_face_u(pr, face) if pr.get("tipo") in ("U", "Ue") else pr["h"] / 2) / MM


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


def _buscar(fn, d, passos, esforcos, alvo=None):
    """Primeiro passo que passa; com `alvo` (W12-OUT, U dobrado: 0,90), o primeiro com taxa ≤ alvo, senão o primeiro que passa."""
    ultimo = primeiro_ok = None
    for p in passos:
        cand = dict(d, **p)
        try:
            r = fn(cand, esforcos)
        except ValueError:
            continue
        ultimo = cand
        if r["ok"]:
            if alvo is None or r["taxa"] <= alvo:
                return cand
            primeiro_ok = primeiro_ok or cand
    return primeiro_ok or ultimo or d


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


def _prep_barra(d, s, campo):
    """W8-CONTRAV (sugerir): arranjo e ligação coerentes com o perfil do catálogo, gabarito usual da L e bordas com 3 mm
    de tolerância de corte sobre a Tab. 14 (o padrão não nasce "no limite"). Põe t_perfil{s} provisório (filete de
    _std_parafuso) e devolve a espessura do perfil (o gusset não fica mais fino que ela)."""
    med = perfil_barra(d[campo], d.get(f"aba_ligada{s}", "maior"))
    if med["forma"] != "L":
        d[f"arranjo{s}"] = "simples"
    if med["forma"].startswith("tubo"):
        d[f"ligacao{s}"] = "soldada"
    if med["forma"] == "L":
        d[f"g_aba{s}"] = d.get(f"g_aba{s}") or gabarito_usual(med["dim_a"])
    d[f"t_perfil{s}"] = med["t_perfil"]
    d[f"formado{s}"] = bool(med.get("formado"))   # U dobrado: o diâmetro e a borda e1 saem da janela da parede fina
    return med["t_perfil"]


def _diametro_frio(d, perfis, sfx):
    """W12-OUT: U dobrado (parede fina, t ≈ 3 mm): a borda e1 precisa ficar entre emin/0,9 e 0,9·12t (tab. 14 e 6.3.12, com a
    folga de 10 %); com ⅝" (emin 29 mm) e t = 3 mm a janela [32,2; 32,4] não admite borda em mm inteiros → ½" (emin 22 mm).
    Só troca o diâmetro padrão: o informado pelo projetista vale."""
    tf = [d[f"t_perfil{s}"] for s in sfx if d.get(f"formado{s}")]
    if not tf or (perfis or {}).get("d_mm"):
        return
    for dm in (15.875, 12.7):
        d["d_mm"] = dm
        emin = L.distancia_min_borda(dm * MM, d.get("borda_laminada", False)) / MM
        if math.ceil(emin / 0.9) <= 0.9 * min(12 * min(tf), 150):
            return


def _bordas_folga(d, sfx):
    emin = L.distancia_min_borda(d["d_mm"] * MM, d.get("borda_laminada", False)) / MM
    for s in sfx:
        t = d.pop(f"t_perfil{s}", None)
        if t:   # perna do filete a até 90 % da máxima na borda (6.2.6.2.2): na parede de 5 mm, 4 mm (e não 5 = no limite)
            bmin = L.filete_minimo(min(t, d.get("t_gusset") or t) * MM) / MM
            d[f"b_solda{s}"] = max(bmin, min(d[f"b_solda{s}"], math.floor(0.9 * L.filete_maximo(t * MM) / MM)))
        for k in ("e1", "e_gusset"):
            d[f"{k}{s}"] = max(d[f"{k}{s}"], math.ceil((emin + 3) / 5) * 5)
        if t and d.get(f"formado{s}"):   # janela estreita da parede fina: e1 em mm inteiros, ≥ emin/0,9 (taxa ≤ 0,90)
            d[f"e1{s}"] = math.ceil(max(emin / 0.9, emin + 3))


def sugerir_gusset(perfis, esforcos):
    novo = not _legado(dict(perfis or {}))
    d = _base(_GUSSET if novo else _GUSSET_LEG, perfis)
    _de_catalogo(perfis, d)
    tmin = _prep_barra(d, "", "perfil_diagonal") if novo else 0.0
    d["t_gusset"] = d.get("t_gusset") or next((x for x in _TG if x >= max(tmin, 8.0) - 0.1), 8.0)
    if not novo and d["secao"] in ("tubo_circular", "tubo_retangular", "barra_redonda"):
        d["ligacao"] = "soldada"
    if novo:
        _diametro_frio(d, perfis, [""])
    _std_parafuso(d, [""])
    if novo:
        _bordas_folga(d, [""])
    passos = [p for p in _passos([""], {"": d["ligacao"]}, (6.0, 8.0, 10.0, 12.0)) if p["t_gusset"] >= tmin - 0.1]
    return _buscar(verificar_gusset, d, passos, esforcos, 0.90 if d.get("formado") else None)


def sugerir_no(perfis, esforcos):
    novo = not any(_legado(dict(perfis or {}), f"_{i}") for i in "1234")
    d = _base(_NO if novo else _NO_LEG, perfis)
    d["t_gusset"] = d.get("t_gusset") or 8.0
    tmin = 0.0
    if novo:
        ativos = [f"_{i}" for i in "1234" if d[f"arranjo_{i}"] != "nenhuma"]
        tmin = max([_prep_barra(d, s, f"perfil_barra{s}") for s in ativos] + [0.0])
    else:
        ativos = [f"_{i}" for i in "1234" if d[f"secao_{i}"] != "nenhum"]
        for s in ativos:
            if d[f"secao{s}"].startswith("tubo"):
                d[f"ligacao{s}"] = "soldada"
    if novo:
        _diametro_frio(d, perfis, ativos)
    _std_parafuso(d, ativos)
    if novo:
        _bordas_folga(d, ativos)
    passos = [p for p in _passos(ativos, {s: d[f"ligacao{s}"] for s in ativos}, (5.0, 6.0, 8.0))
              if p["t_gusset"] >= tmin - 0.1]
    return _buscar(verificar_no_trelica, d, passos, esforcos, 0.90 if any(d.get(f"formado{s}") for s in ativos) else None)


DIAMETROS_BARRA_MM = (9.525, 12.7, 15.875, 19.05, 22.225, 25.4, 28.575, 31.75, 34.925, 38.1, 41.275, 44.45,
                      50.8, 57.15, 63.5)   # 3/8" a 2 1/2"


def sugerir_tirante(perfis, esforcos):
    d = _base(_TIRANTE, perfis)
    d["t_chapa"] = d.get("t_chapa") or 8.0
    passos = [dict(D_barra=D, t_chapa=t) for D in DIAMETROS_BARRA_MM for t in _TG]
    return _buscar(verificar_tirante, d, passos, esforcos)


def sugerir_cantoneira(perfis, esforcos):
    d = _base(_CANT, perfis)
    legado = not (perfis or {}).get("perfil_cantoneira") and (perfis or {}).get("dim_a")
    if legado:
        d.update({k: perfis[k] for k in _MEDIDAS_L if k in perfis})
        d.pop("perfil_cantoneira", None)
    else:   # W7-LIG: medidas do catálogo, gabarito usual e gusset nunca mais fino que a aba
        med, _ = cantoneira_catalogo(d["perfil_cantoneira"], d["aba_ligada"])
        d["g_aba"] = d.get("g_aba") or gabarito_usual(med["dim_a"])
        d["t_perfil"] = med["t_perfil"]   # só para o filete de _std_parafuso (não é campo de _CANT)
    tmin = d.get("t_perfil") or 0.0
    d["t_gusset"] = d.get("t_gusset") or next((x for x in _TG if x >= tmin - 0.1), 6.3)   # 6,3 serve na aba de 1/4"
    _std_parafuso(d, [""])
    if not legado:
        d.pop("t_perfil", None)
        # W7-LIG: bordas com 3 mm de tolerância de corte sobre a Tab. 14 (o padrão não nasce "no limite" por 1 mm)
        emin = L.distancia_min_borda(d["d_mm"] * MM, d.get("borda_laminada", False)) / MM
        for k in ("e1", "e_gusset"):
            d[k] = max(d[k], math.ceil((emin + 3) / 5) * 5)
    passos = [p for p in _passos([""], {"": d["ligacao"]}) if p["t_gusset"] >= tmin - 0.1]
    return _buscar(verificar_cantoneira_simples, d, passos, esforcos)


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
        entradas=_NO, esforcos=["N", "N2", "N3", "N4"], verificar=verificar_no_trelica, sugerir=sugerir_no,
        # W8-CONTRAV: padrão de fábrica = nó do banzo inferior de uma Pratt: diagonal 2L a 45° tracionada e montante 2L
        # comprimido; ΣV = 100·sen 45° − 70,7 = 0 e a interface leva ΣH = 70,7 kN
        exemplo=dict(dados=dict(arranjo_2="dupla"), esforcos=dict(N=100.0, N2=-70.7, N3=0.0, N4=0.0))),
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


L.registrar_pontas(TIPOS)   # G2: geometria["pontas"] em todo resultado
