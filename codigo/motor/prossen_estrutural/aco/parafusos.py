"""Catálogo de parafusos estruturais: ASTM A325/A490 e A307 em polegada (UNC), ISO 898-1 métricos.
Diâmetros por especificação, cabeça, rosca, porca, arruela e comprimento comercial (pega + arruela + porca + 2 fios,
arredondado para o próximo comprimento de linha). Unidades: m nas funções, mm/pol nas tabelas.

Fontes (referências do consultor, 9. CONSULTOR DE ENGENHARIA/referencias/fixadores/md):
  [IDX-P007] Indufix, Linha Pesada ASTM, p. 06: A325 | A490 (ASME B18.2.6, UNC ASME B1.1): chave, altura da cabeça,
             comprimento de rosca e comprimentos de linha de 1" a 6" a cada ¼".
  [IDX-P008] Indufix p. 07: A307 B (ASME B18.2.1, UNC): idem.
  [IDX-P011] Indufix p. 10: porca A194 2H (altura máx.); [IDX-P012] p. 11: porca A563 (altura máx., mesmas medidas).
  [IDX-P014] Indufix p. 13: arruela F436 (espessura máx. 4,49 mm de ½" a 1½").
  [ALX-P012/P013, P016, P018–P020] Allenfix: A325 RP/RI e A490 (ASME B18.2.6), A307 RI/RP Gr. A/B (ASME B18.2.1).
  Métricos (ISO 4014/4017, porca ISO 4032, arruela ISO 7089): sem catálogo do fabricante; comprimento a cada 5 mm
  até 80 mm e a cada 10 mm acima.
"""
from __future__ import annotations

import math

POL = 25.4  # mm
POLEGADA = ("A307", "A325", "A490")   # especificações ASTM: só diâmetros em polegada
L_MAX_LINHA = 6.0  # pol: acima disso o comprimento é sob encomenda (tabelas de linha vão até 6")

# (rótulo, d pol, fios/pol UNC, chave pol, altura da cabeça mm, rosca pol, menor comprimento de linha pol)
_A325 = [  # [IDX-P007]; A490 tem as mesmas medidas (ASME B18.2.6)
    ("½\"", 1 / 2, 13, 7 / 8, 8.20, 1.0, 1.0), ("⅝\"", 5 / 8, 11, 1 + 1 / 16, 10.23, 1.25, 1.0),
    ("¾\"", 3 / 4, 10, 1.25, 12.26, 1.375, 1.0), ("⅞\"", 7 / 8, 9, 1 + 7 / 16, 14.30, 1.5, 1.25),
    ("1\"", 1.0, 8, 1.625, 15.92, 1.75, 1.25), ("1⅛\"", 1.125, 7, 1 + 13 / 16, 18.23, 2.0, 1.5),
    ("1¼\"", 1.25, 7, 2.0, 20.65, 2.0, 1.5), ("1⅜\"", 1.375, 6, 2 + 3 / 16, 22.30, 2.25, 1.75),
    ("1½\"", 1.5, 6, 2.375, 24.73, 2.25, 1.75)]
_A307 = [  # [IDX-P008]
    ("½\"", 1 / 2, 13, 7 / 8, 8.20, 1.25, 1.25), ("⅝\"", 5 / 8, 11, 1 + 1 / 16, 10.23, 1.5, 1.5),
    ("¾\"", 3 / 4, 10, 1.25, 12.26, 1.75, 1.5), ("⅞\"", 7 / 8, 9, 1 + 7 / 16, 14.30, 2.0, 1.75),
    ("1\"", 1.0, 8, 1.625, 15.92, 2.25, 2.0), ("1⅛\"", 1.125, 7, 1 + 13 / 16, 18.23, 2.25, 2.25),
    ("1¼\"", 1.25, 7, 2.0, 20.65, 2.75, 2.5), ("1⅜\"", 1.375, 6, 2.375, 21.00, 3.0, 2.75),
    ("1½\"", 1.5, 6, 2.375, 24.73, 3.25, 3.0), ("1¾\"", 1.75, 5, 2.75, 28.00, 4.0, 3.25),
    ("2\"", 2.0, 4.5, 3.125, 30.00, 4.5, 3.5)]
# porca pesada A194 2H / A563, altura máx. (mm) [IDX-P011, IDX-P012]
_PORCA_POL = {1 / 2: 12.80, 5 / 8: 16.02, 3 / 4: 19.25, 7 / 8: 22.47, 1.0: 25.70, 1.125: 28.93, 1.25: 31.77,
              1.375: 35.00, 1.5: 38.22, 1.75: 44.67, 2.0: 51.13}
_ARRUELA_F436 = 4.49  # mm, espessura máx. [IDX-P014]
# métricos: (d mm, passo mm, altura da porca ISO 4032 mm, arruela ISO 7089 mm)
_ISO = [(12, 1.75, 10.8, 2.5), (16, 2.0, 14.8, 3.0), (20, 2.5, 18.0, 3.0), (22, 2.5, 19.4, 3.0),
        (24, 3.0, 21.5, 4.0), (27, 3.0, 23.8, 4.0), (30, 3.5, 25.6, 4.0)]


def _tabela(tipo):
    return _A307 if tipo == "A307" else _A325


def diametros(tipo):
    """Diâmetros de linha (mm) da especificação: polegada para A307/A325/A490, métrico para as classes ISO."""
    if tipo in POLEGADA:
        return [round(x[1] * POL, 3) for x in _tabela(tipo)]
    return [float(x[0]) for x in _ISO]


def polegada(db_mm):
    """Entrada da tabela em polegada para o diâmetro (mm), ou None se não é diâmetro em polegada."""
    return next((x for x in _A307 if abs(x[1] * POL - db_mm) < 0.05), None)


def rotulo(db_mm):
    p = polegada(db_mm)
    return p[0] if p else f"M{db_mm:g}"


def _iso(db_mm):
    return next((x for x in _ISO if abs(x[0] - db_mm) < 0.05), None)


def comprimento(pega, db, tipo=None):
    """Comprimento comercial (m) para a pega (m, soma das espessuras apertadas) e o diâmetro db (m): pega + arruela +
    porca + 2 fios de ponta, arredondado para cima. Polegada: comprimentos de linha a cada ¼" do menor da tabela até 6"
    ([IDX-P007/P008]); acima, a cada ½" (sob encomenda). Métrico: a cada 5 mm até 80 e a cada 10 mm acima."""
    dmm = db * 1000
    p = polegada(dmm)
    if p is not None:
        tab = next((x for x in _tabela(tipo if tipo in POLEGADA else "A325") if abs(x[1] - p[1]) < 1e-9), p)
        nec = (pega * 1000 + _ARRUELA_F436 + _PORCA_POL[p[1]] + 2 * POL / tab[2]) / POL
        L = max(tab[6], math.ceil(nec * 4 - 1e-6) / 4)
        if L > L_MAX_LINHA + 1e-9:
            L = math.ceil(nec * 2 - 1e-6) / 2
        return L * POL / 1000
    m = _iso(dmm)
    passo, porca, arruela = (m[1], m[2], m[3]) if m else (0.15 * dmm, 0.9 * dmm, 4.0)
    nec = pega * 1000 + arruela + porca + 2 * passo
    passo_L = 5 if nec <= 80 else 10
    return math.ceil(nec / passo_L - 1e-6) * passo_L / 1000


def sob_encomenda(L, db):
    return polegada(db * 1000) is not None and L / (POL / 1000) > L_MAX_LINHA + 1e-9


def _pol(x):
    """Comprimento em polegada com fração de ¼ (2.25 → 2¼")."""
    inteiro, fr = int(x + 1e-9), x - int(x + 1e-9)
    s = {0: "", 1: "¼", 2: "½", 3: "¾"}[round(fr * 4) % 4]
    return (f"{inteiro}" if inteiro else "") + s + "\"" if (inteiro or s) else "0\""


def descricao(db, L, tipo):
    """'⅞" × 2½" A325' ou 'M20 × 70 ISO 8.8' (db e L em m)."""
    dmm = db * 1000
    if polegada(dmm) is not None:
        txt = f"{rotulo(dmm)} × {_pol(L * 1000 / POL)} {tipo}"
        return txt + (" (sob encomenda)" if sob_encomenda(L, db) else "")
    return f"{rotulo(dmm)} × {L * 1000:.0f} {tipo}"


if __name__ == "__main__":
    # ⅞" A325, pega 2 × 12,5 mm: 25 + 4,49 + 22,47 + 5,64 = 57,6 mm = 2,27" → 2½"
    assert abs(comprimento(0.025, 0.022225, "A325") - 2.5 * POL / 1000) < 1e-9
    assert descricao(0.022225, comprimento(0.025, 0.022225, "A325"), "A325") == "⅞\" × 2½\" A325"
    # ½" A325 pega 3 mm: 3 + 4,49 + 12,8 + 3,9 = 24,2 mm < 1" → 1" (menor de linha); A307 ½": menor 1¼"
    assert abs(comprimento(0.003, 0.0127, "A325") - POL / 1000) < 1e-9
    assert abs(comprimento(0.003, 0.0127, "A307") - 1.25 * POL / 1000) < 1e-9
    # M20 ISO 8.8 pega 30: 30 + 3 + 18 + 5 = 56 → 60
    assert abs(comprimento(0.030, 0.020, "ISO 8.8") - 0.060) < 1e-9
    assert sob_encomenda(comprimento(0.140, 0.0254), 0.0254)
    assert diametros("A325")[:3] == [12.7, 15.875, 19.05] and diametros("ISO 8.8")[0] == 12.0
    print("ok")
