"""Limites de projeto das ligações: faixa (min, max, passos) de cada parâmetro geométrico (docs/v3/LIGACOES-LIMITES.md).

Base do otimizador limitado (Frente C): ele só busca dentro destas faixas. Valores na unidade do parâmetro
(mm nos tipos do catálogo; m na placa de base). `max`/`passos` None = sem limite / contínuo.
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


def _menor(vals):
    vals = [v for v in vals if v is not None]
    return min(vals) if vals else None


def _borda_min(d):
    return L.distancia_min_borda(d * L.MM) / L.MM


def _placa_base(membros, dados):
    ps = [_dim(m.get("perfil")) for m in membros]
    ps = [p for p in ps if p]
    d = float(dados.get("diametro_mm") or 16)
    bmin = max(_borda_min(d), 1.0) / 1000   # NBR 8800 Tab. 14: centro do furo à borda
    borda = float(dados.get("borda") or bmin)
    out = {"esp": (T_COMERCIAL[0] / 1000, T_COMERCIAL[-1] / 1000, tuple(t / 1000 for t in T_COMERCIAL)),
           "diametro_mm": (D_CHUMBADOR[0], D_CHUMBADOR[-1], D_CHUMBADOR),
           "borda": (bmin, None, None)}
    if ps:   # placa ≥ pilar + 2·borda em cada direção
        out["H"] = (max(p["h"] for p in ps) + 2 * borda, None, None)
        out["B"] = (max(p["b"] for p in ps) + 2 * borda, None, None)
    return out


def limites(tipo, membros, dados=None):
    """{parâmetro: (min, max, passos)} do tipo. `membros`: lista de {perfil: barra, perfil_apoio: apoio} (nome do
    catálogo ou dict h, b, tw, tf em m). `dados` (opcional): valores atuais, p.ex. `d` para espaçamento e borda."""
    membros, dados = membros or [], dados or {}
    if tipo == "placa_base":
        return _placa_base(membros, dados)
    reg = L.tipos_contrato().get(tipo)
    if reg is None:
        return {}
    ap = [_dim(m.get("perfil_apoio")) for m in membros]
    br = [_dim(m.get("perfil")) for m in membros]
    bf_ap = _menor(p["b"] * 1000 for p in ap if p)
    bf_br = _menor(p["b"] * 1000 for p in br if p)
    livre = _menor((p["h"] - 2 * p["tf"]) * 1000 - 2 * FOLGA for p in ap if p and "tf" in p)   # entre mesas
    d_atual = float(next((dados[k] for k in _DIAMETRO if dados.get(k)), 19.05))
    out = {}
    for k in reg["entradas"]:
        b = re.sub(r"_\d$", "", k)   # nó de treliça: e1_2 → e1
        if b in _ESPESSURA:
            out[k] = (T_COMERCIAL[0], T_COMERCIAL[-1], T_COMERCIAL)
        elif b in _DIAMETRO:
            out[k] = (D_PARAFUSO[0], D_PARAFUSO[-1], D_PARAFUSO)
        elif b in _ESPACAMENTO:
            out[k] = (3 * d_atual, S_MAX, None)   # 6.3.9: ≥ 3d (nunca < 2,7d)
        elif b in _BORDA:
            out[k] = (_borda_min(d_atual), BORDA_MAX, None)   # Tab. 14 (serra/tesoura), 6.3.12
        elif k in _ATE_BF_APOIO.get(tipo, ()) and bf_ap:
            out[k] = (2 * _borda_min(d_atual), bf_ap, None)
        elif b in _ATE_BF_BARRA and bf_br:
            out[k] = (2 * _borda_min(d_atual), bf_br, None)
        elif tipo in ("gusset_contraventamento",) and k in ("Lh", "Lv") and livre:
            out[k] = (0.0, max(livre, 0.0), None)   # 0 = automático; gusset cabe entre as mesas do apoio
    return out
