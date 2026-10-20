"""Materiais: aços estruturais (NBR 8800 anexo A, NBR 14762), concretos (NBR 6118:2023 8.2) e
armaduras (8.3). Entradas tabeladas em MPa; Material sai em kN/m² (peso em kN/m³)."""
from __future__ import annotations

import math

from .modelo import Material, MPa

E_ACO = 200_000 * MPa   # NBR 8800 4.5.2.9
G_ACO = 77_000 * MPa
PESO_ACO = 77.0         # kN/m³

# (fy, fu) MPa — NBR 8800 tabelas A.1/A.2/A.3 e NBR 14762 tabela A.1 (aços para perfis a frio).
# Conferência em fonte pública (2026-10-20): o texto das tabelas da ABNT não está disponível;
# conferidos contra catálogo de fabricante/ASTM os marcados [conf.]; os demais [não conferido].
#   [conf.] MR-250 ≡ A-36 (250/400), AR-350 ≡ A-572 Gr.50 (350/450), AR-350-COR ≡ A-588 (350/485),
#           AR-415 (415/520): catálogo ArcelorMittal "Perfis e Barras" (NBR 7007 / ASTM).
#   [conf.] A-572 GR.50 345/450 (ASTM A572); ZAR-345 345/430 (NBR 7008, base TotalMateria).
ACOS = {
    "A-36": (250, 400),            # [conf.]
    "A-572 GR.50": (345, 450),     # [conf.]
    "A-572 GR.42": (290, 415),     # [não conferido]
    "A-992": (345, 450),           # [não conferido]
    "A-588": (345, 485),           # [não conferido]
    # ASTM A242/A242M, tabela de tração (VC-guia-003): t ≤ 19 mm 50/70 ksi; 19 < t ≤ 38 mm 46/67 ksi;
    # 38 < t ≤ 100 mm 42/63 ksi. Sem faixa ("A-242") → a menor, válida para qualquer t ≤ 100 mm (a favor da
    # segurança, como COS-AR-COR-500). "T<=x" vale para toda espessura até x mm.
    "A-242": (290, 435),
    "A-242 T<=19": (345, 485),
    "A-242 T<=38": (315, 460),
    "A-242 T<=100": (290, 435),
    "A-500 GR.B": (290, 400),      # [não conferido] (valor de tubo circular)
    "MR-250": (250, 400),          # [conf.]
    "AR-350": (350, 450),          # [conf.]
    "AR-350-COR": (350, 485),      # [conf.]
    "AR-415": (415, 520),          # [conf.]
    "CG-26": (255, 410),           # [não conferido]
    "CG-28": (275, 440),           # [não conferido]
    "CF-24": (240, 370),           # [não conferido]
    "CF-26": (260, 400),           # [não conferido]
    "CF-28": (280, 440),           # [não conferido]
    "CF-30": (300, 490),           # [não conferido]
    "ZAR-250": (250, 360),         # [não conferido] (fy 250 conferido; fu não)
    "ZAR-280": (280, 380),         # [não conferido]
    "ZAR-345": (345, 430),         # [conf.]
    "USI-SAC-300": (300, 400),     # [não conferido]
    "USI-SAC-350": (350, 500),     # [não conferido]
    # fy = 355 até 16 mm e 375 de 16 a 50,8 mm (ficha "Aços de qualidade estrutural resistentes à corrosão
    # atmosférica", nota 2 — VC-aco-011): um valor por aço → o menor (a favor da segurança). fu não conferido.
    "COS-AR-COR-500": (355, 490),
}


def _busca(tabela, nome, tipo):
    chave = str(nome).upper().replace("GR50", "GR.50")
    if chave not in tabela:
        raise ValueError(f"{tipo} '{nome}' desconhecido; válidos: {', '.join(tabela)}")
    return chave, tabela[chave]


def aco(nome):
    chave, (fy, fu) = _busca(ACOS, nome, "aço")
    return Material(chave, E_ACO, G_ACO, PESO_ACO, fy=fy * MPa, fu=fu * MPa)


def props_concreto(fck, alfa_E=1.0):
    """fck em MPa -> dict em MPa. NBR 6118:2023 8.2.5 e 8.2.8."""
    if not 20 <= fck <= 90:
        raise ValueError("NBR 6118 cobre C20 a C90")
    if not 0.7 <= alfa_E <= 1.2:  # 8.2.8: 1,2 basalto/diabásio; 1,0 granito/gnaisse; 0,9 calcário; 0,7 arenito
        raise ValueError(f"alfa_E = {alfa_E}: NBR 6118 8.2.8 admite αE de 0,7 (arenito) a 1,2 (basalto)")
    if fck <= 50:
        fctm = 0.3 * fck ** (2 / 3)                       # 8.2.5
        Eci = alfa_E * 5600 * math.sqrt(fck)              # 8.2.8
    else:
        fctm = 2.12 * math.log(1 + 0.1 * (fck + 8))       # 8.2.5 (2023; a 2014 usava 0,11·fck)
        Eci = 21.5e3 * alfa_E * (fck / 10 + 1.25) ** (1 / 3)  # 8.2.8
    alfa_i = min(0.8 + 0.2 * fck / 80, 1.0)               # 8.2.8
    return dict(fck=fck, Eci=Eci, Ecs=alfa_i * Eci, alfa_i=alfa_i, fctm=fctm,
                fctk_inf=0.7 * fctm, fctk_sup=1.3 * fctm)


def concreto(fck, alfa_E=1.0):
    """fck em MPa (20..90). alfa_E: 1,2 basalto; 1,0 granito; 0,9 calcário; 0,7 arenito."""
    p = props_concreto(fck, alfa_E)
    Ecs = p["Ecs"] * MPa
    # G = Ecs/2,4 (NBR 6118 8.2.9, ν = 0,2)
    nome = f"C{fck:g}" if alfa_E == 1.0 else f"C{fck:g} αE{alfa_E:g}".replace(".", ",")  # nomes únicos no Modelo
    return Material(nome, Ecs, Ecs / 2.4, 25.0, fck=fck * MPa)


# NBR 7480 / NBR 6118 8.3: fyk e fst mínimo (MPa); Es = 210 GPa.
# fst mínimo = relação fst/fy da NBR 7480:2007 (CA-25 1,20; CA-50 1,08; CA-60 1,05). CA-50 1,08·LE
# conferido em 2026-10-20 no catálogo ArcelorMittal "Aços Longos" (propriedades mínimas, NBR 7480);
# 1,10 (Bastos 2023) não confirmado na fonte primária. fu não entra em nenhum cálculo de CA.
ARMADURAS = {"CA-25": (250, 300), "CA-50": (500, 540), "CA-60": (600, 630)}
ES_ARMADURA = 210_000 * MPa


def aco_armadura(nome):
    chave, (fy, fu) = _busca(ARMADURAS, nome, "armadura")
    return Material(chave, ES_ARMADURA, ES_ARMADURA / 2.6, 78.5, fy=fy * MPa, fu=fu * MPa)


def material_de_spec(dados):
    """Spec de material da ESPEC §6.3 -> Material (kN/m²; peso em kN/m³).
    {"aco": nome} | {"concreto": fck_MPa, "alfa_E"?} | {"armadura": nome} |
    explícito {nome, E_MPa, G_MPa, peso_kN_m3, fy_MPa?, fu_MPa?, fck_MPa?}. Inválido → ValueError."""
    if not isinstance(dados, dict):
        raise ValueError(f"material: esperado objeto, recebido {type(dados).__name__}")
    if "aco" in dados:
        return aco(dados["aco"])
    if "armadura" in dados:
        return aco_armadura(dados["armadura"])
    if "concreto" in dados:
        return concreto(float(dados["concreto"]), float(dados.get("alfa_E", 1.0)))
    falta = [k for k in ("nome", "E_MPa", "G_MPa", "peso_kN_m3") if k not in dados]
    if falta:
        raise ValueError("material: use {aco}, {concreto}, {armadura} ou o explícito; "
                         f"faltam os campos {', '.join(falta)}")
    opc = {k: (float(dados[f"{k}_MPa"]) * MPa if dados.get(f"{k}_MPa") is not None else None)
           for k in ("fy", "fu", "fck")}
    return Material(str(dados["nome"]), float(dados["E_MPa"]) * MPa, float(dados["G_MPa"]) * MPa,
                    float(dados["peso_kN_m3"]), **opc)
