"""Limites de projeto das ligações: faixa dict(min, max, passos, unidade) de cada parâmetro geométrico (docs/v3/LIGACOES-LIMITES.md).

Base do otimizador limitado (Frente C): ele só busca dentro destas faixas. Cada entrada é
dict(min, max, passos, unidade) e vale na unidade do próprio parâmetro (mm no catálogo, m na placa de base). `max`/`passos` None = sem limite / contínuo.
"""
import re

from .. import secoes
from . import ligacoes as L

FOLGA = 12.0   # mm, folga padrão (global.md)
T_COMERCIAL = (4.75, 6.3, 8.0, 9.5, 12.5, 16.0, 19.0, 22.4, 25.0, 31.5, 37.5, 44.5, 50.0)   # mm, Usiminas/Gerdau
D_PARAFUSO = tuple(float(x) for x in L._D_MM)
D_CHUMBADOR = (12.0, 16.0, 20.0, 24.0, 30.0, 36.0)   # métrico, placa de base
S_MAX = 300.0      # mm, NBR 8800 6.3.12: 24t ≤ 300 mm (t da chapa mais fina não conhecida aqui)
BORDA_MAX = 150.0  # mm, NBR 8800 6.3.12: 12t ≤ 150 mm

_ESPESSURA = {"t_chapa", "t_gusset", "tp", "t_mesa", "t_tala_mesa", "t_tala_alma", "t_alma", "t_assento",
              "t_enrijecedor"}
_DIAMETRO = {"d", "d_mm", "d_alma_mm", "d_mesa_mm"}
_ESPACAMENTO = {"s", "s_mesa", "s_alma", "sh_alma"}
_BORDA = {"e_v", "e_h", "e1", "e1_mesa"}
_ATE_BF_APOIO = {"chapa_topo_ajustada": ("bp",), "chapa_topo_estendida": ("bp",), "flange_plate": ("b_chapa",),
                 "chapa_extremidade_flexivel": ("largura_chapa",)}
_ATE_BF_BARRA = {"b_tala_mesa"}   # talas de mesa de emenda: ≤ bf do perfil emendado


def _dim(p):
    """Perfil (nome do catálogo, ou dict com h, b, tw, tf em m) → dict em m; None se ausente/inválido."""
    if not p:
        return None
    if isinstance(p, str):
        try:
            p = secoes.catalogo(p).props
        except KeyError:
            return None
    return p if "h" in p and "b" in p else None


def _borda_min_mm(d_mm):
    return L.distancia_min_borda(d_mm * L.MM) / L.MM


def _f(mn, mx=None, passos=None, un="mm"):
    return dict(min=mn, max=mx, passos=passos, unidade=un)


def _placa_base(ap, dados):
    p = _dim(ap)   # na placa de base o pilar é a barra
    d = float(dados.get("diametro_mm") or 16)
    bmin = _borda_min_mm(d) / 1000   # m
    borda = float(dados.get("borda") or bmin)
    out = {"esp": _f(T_COMERCIAL[0] / 1000, T_COMERCIAL[-1] / 1000, tuple(t / 1000 for t in T_COMERCIAL), "m"),
           "diametro_mm": _f(D_CHUMBADOR[0], D_CHUMBADOR[-1], D_CHUMBADOR),
           "borda": _f(bmin, None, None, "m")}
    if p:   # placa ≥ pilar + 2·borda em cada direção
        out["H"] = _f(p["h"] + 2 * borda, None, None, "m")
        out["B"] = _f(p["b"] + 2 * borda, None, None, "m")
    return out


def _gusset_minimo(dados):
    """Lh, Lv automáticos (mm) = mínimo geométrico de _auto_gusset, lido do resultado com Lh = Lv = 0."""
    from . import ligacoes_contraventamento as C
    try:
        g = C.verificar("gusset_contraventamento", dict(dados, Lh=0.0, Lv=0.0), {"N": 100.0})["geometria"]
        return g["Lh"], g["Lv"]
    except Exception:   # dados incompletos: piso do cálculo automático (0,05 m)
        return 50.0, 50.0


def limites(tipo, membro, dados=None):
    """{parâmetro: dict(min, max, passos, unidade)} de UMA ligação. `membro`: {perfil: barra, perfil_apoio: apoio,
    comp_viga, comp_pilar (m, livres)} com perfil = nome do catálogo ou dict h, b, tw, tf em m. `dados`
    (opcional): valores atuais (`d`, `t` da chapa mais fina em mm, `apoio`, `gusset_na_alma`, `borda` da placa)."""
    membro, dados = membro or {}, dados or {}
    if tipo == "placa_base":
        return _placa_base(membro.get("perfil"), dados)
    reg = L.tipos_contrato().get(tipo)
    if reg is None:
        return {}
    ap, br = _dim(membro.get("perfil_apoio")), _dim(membro.get("perfil"))
    bf_ap = ap["b"] * 1000 if ap else None
    bf_br = br["b"] * 1000 if br else None
    d_atual = float(next((dados[k] for k in _DIAMETRO if dados.get(k)), 19.05))
    t = float(dados.get("t") or 0)
    s_max = min(24 * t, S_MAX) if t > 0 else S_MAX     # 6.3.12
    e_max = min(12 * t, BORDA_MAX) if t > 0 else BORDA_MAX
    out = {}
    for k in reg["entradas"]:
        b = re.sub(r"_\d$", "", k)   # nó de treliça: e1_2 → e1
        if b in _ESPESSURA:
            out[k] = _f(T_COMERCIAL[0], T_COMERCIAL[-1], T_COMERCIAL)
        elif b in _DIAMETRO:
            out[k] = _f(D_PARAFUSO[0], D_PARAFUSO[-1], D_PARAFUSO)
        elif b in _ESPACAMENTO:
            out[k] = _f(3 * d_atual, s_max)   # 6.3.9: ≥ 3d (nunca < 2,7d)
        elif b in _BORDA:
            out[k] = _f(_borda_min_mm(d_atual), e_max)   # Tab. 14 (serra/tesoura), 6.3.12
        elif k in _ATE_BF_APOIO.get(tipo, ()) and bf_ap:
            out[k] = _f(2 * _borda_min_mm(d_atual), bf_ap)
        elif b in _ATE_BF_BARRA and bf_br:
            out[k] = _f(2 * _borda_min_mm(d_atual), bf_br)
        elif tipo == "gusset_contraventamento" and k in ("Lh", "Lv"):
            mn = _gusset_minimo(dados)[0 if k == "Lh" else 1]
            comp = membro.get("comp_viga" if k == "Lh" else "comp_pilar")   # ao longo da viga / do pilar
            mx = comp * 1000 if comp else 3000.0
            if dados.get("gusset_na_alma") and ap:   # soldado na alma entre as mesas do apoio
                mx = min(mx, (ap["h"] - 2 * ap["tf"]) * 1000 - 2 * FOLGA)
            out[k] = _f(mn, max(mx, mn))
    return out
