"""Gera os modelos de exemplo (*.json) desta pasta. Uso, a partir de `software/`:

    python exemplos/gerar_exemplos.py

Saída determinística (os JSON versionados são reproduzíveis). Valores de conferência: LEIAME.md.
Cargas de norma vêm de `acoes.cargas` (NBR 6120:2019); as demais cargas são hipóteses do exemplo.
"""
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(AQUI))

from prossen_estrutural import materiais, secoes  # noqa: E402
from prossen_estrutural.acoes import cargas  # noqa: E402
from prossen_estrutural.gerador import gerar_estrutura  # noqa: E402
from prossen_estrutural.modelo import (Barra, CargaDistribuida, CargaNodal, CasoCarga,  # noqa: E402
                                       Modelo, No)


def viga_biapoiada():
    """W360x51, A-572 GR.50, L = 6 m; G1 = peso próprio + 5 kN/m; Q1 = 10 kN/m."""
    m = Modelo(nome="Viga biapoiada W360x51, L = 6 m")
    m.add(materiais.aco("A-572 GR.50"))
    m.add(secoes.catalogo("W360x51"))
    m.add(No("n1", 0, 0, 0, apoio=(True, True, True, True, False, False)))  # apoio fixo + torção
    m.add(No("n2", 6, 0, 0, apoio=(False, True, True, False, False, False)))  # apoio móvel em x
    m.add(Barra("b1", "n1", "n2", "W360x51", "A-572 GR.50", tipo="aco",
                params={"papel": "viga", "elemento_desloc": "viga_piso"}))
    m.add(CasoCarga("G1", "G"))
    m.add(CasoCarga("Q1", "Q"))
    m.peso_proprio = "G1"
    m.add(CargaDistribuida("G1", "b1", (0, 0, -5.0)))
    m.add(CargaDistribuida("Q1", "b1", (0, 0, -10.0)))
    m.config = {"tipo_uso": "comercial", "variabilidade": "grandes", "pdelta": False}
    return m


def portico_aco():
    """Pórtico plano, 2 vãos de 6 m, 1 pavimento de 4 m; G1 8 kN/m, Q1 5 kN/m nas vigas;
    vento lateral manual V1 = 10 kN em +X no topo do pilar de barlavento."""
    m = Modelo(nome="Pórtico plano de aço, 2 vãos de 6 m x 4 m")
    gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                        "secoes": {"pilar": "W310x38.7", "viga": "W360x51"},
                        "vaos_x": [6.0, 6.0], "alturas": [4.0], "apoio": "engaste"})
    m.add(CasoCarga("V1", "V", "vento"))
    for v in ("VX1-0-0", "VX1-1-0"):
        m.add(CargaDistribuida("G1", v, (0, 0, -8.0)))
        m.add(CargaDistribuida("Q1", v, (0, 0, -5.0)))
    m.add(CargaNodal("V1", "N1-0-0", (10.0, 0, 0, 0, 0, 0)))
    m.config = {"tipo_uso": "comercial", "variabilidade": "grandes", "pdelta": False}
    return m


def galpao_aco():
    """Galpão de 20 m de vão, pilar 6 m, cumeeira a 8 m, 5 módulos de 6 m; Q1 = cobertura (NBR 6120
    6.4) sobre as vigas inclinadas, por metro de projeção horizontal × largura de influência.
    Vigas HP250x62: o banzo comprimido não tem travamento intermediário (terças só no beiral e na
    cumeeira), então KL/r = 10,2 m / ry tem de ficar ≤ 200 (NBR 8800 5.3.4); o W360x44.6 dava 271.
    Contraventamento (`contraventamento: true`): diagonais-treliça de tubo circular 219,1 x 6,35 mm nos
    vãos extremos (cobertura e paredes laterais), travando os pórticos nos dois sentidos."""
    m = Modelo(nome="Galpão de aço, 20 m de vão, 5 módulos de 6 m")
    gerar_estrutura(m, {"tipo": "galpao", "material": "A-572 GR.50",
                        "secoes": {"pilar": "W360x51", "viga": "HP250x62",
                                   "terca": {"perfil": "Ue 250x85x25x3,00"},
                                   "contraventamento": {"forma": "tubo_circular", "d": 0.2191, "t": 0.00635}},
                        "contraventamento": True,
                        "vao": 20.0, "altura_pilar": 6.0, "altura_cumeeira": 8.0,
                        "n_modulos": 5, "espacamento": 6.0, "apoio": "engaste"})
    inclin_pct = 100 * (8.0 - 6.0) / 10.0
    q = cargas.carga_cobertura(inclin_pct)["q_kN_m2"]  # kN/m² em projeção horizontal
    cos_t = 10.0 / math.hypot(10.0, 2.0)
    for j in range(6):
        largura = 3.0 if j in (0, 5) else 6.0  # pórticos de extremidade: meia faixa
        for b in (f"VX2-0-{j}", f"VX2-1-{j}"):
            m.add(CargaDistribuida("Q1", b, (0, 0, -q * largura * cos_t)))  # por m de barra
    m.config = {"tipo_uso": "comercial", "variabilidade": "grandes", "pdelta": False}
    return m


def edificio_concreto():
    """3 x 2 vãos de 5 m, 4 pavimentos de 3 m, C30, pilares 25x50 (50 cm em X), vigas 20x50.
    G1 = peso próprio + laje h = 12 cm (γ concreto armado de NBR 6120 5.3) + 1,0 kN/m² de
    revestimento (hipótese); Q1 = escritório, sala de uso geral (NBR 6120 6.2). Cada painel
    descarrega q·a·b/(2(a+b)) por metro em cada uma das 4 vigas de borda."""
    vx, vy, h = [5.0] * 3, [5.0] * 2, [3.0] * 4
    m = Modelo(nome="Edifício de concreto, 3x2 vãos de 5 m, 4 pavimentos")
    m.add(CasoCarga("G1", "G"))
    m.add(CasoCarga("Q1", "Q", "escritorio"))
    gerar_estrutura(m, {"tipo": "edificio", "material": {"concreto": 30},
                        "secoes": {"pilar": {"forma": "retangular", "b": 0.25, "h": 0.50},
                                   "viga": {"forma": "retangular", "b": 0.20, "h": 0.50}},
                        "vaos_x": vx, "vaos_y": vy, "alturas": h, "apoio": "engaste"})
    gama_c = cargas.peso_especifico("concreto_armado")["valor_kN_m3"]
    qg = 0.12 * gama_c + 1.0
    qq = cargas.carga_uso("escritorio_sala_geral")["q_kN_m2"]

    def borda(a, b):  # kN/m por painel a x b e por kN/m² de carga de área
        return a * b / (2 * (a + b))
    for k in range(1, len(h) + 1):
        for j in range(len(vy) + 1):
            for i, a in enumerate(vx):
                f = sum(borda(a, vy[jj]) for jj in (j - 1, j) if 0 <= jj < len(vy))
                m.add(CargaDistribuida("G1", f"VX{k}-{i}-{j}", (0, 0, -qg * f)))
                m.add(CargaDistribuida("Q1", f"VX{k}-{i}-{j}", (0, 0, -qq * f)))
        for i in range(len(vx) + 1):
            for j, b in enumerate(vy):
                f = sum(borda(vx[ii], b) for ii in (i - 1, i) if 0 <= ii < len(vx))
                m.add(CargaDistribuida("G1", f"VY{k}-{i}-{j}", (0, 0, -qg * f)))
                m.add(CargaDistribuida("Q1", f"VY{k}-{i}-{j}", (0, 0, -qq * f)))
    m.config = {"tipo_uso": "escritorio", "variabilidade": "grandes", "pdelta": False,
                "armadura": "CA-50", "cobrimento_m": 0.03, "caa": "II",
                "vento": {"V0": 35.0, "S1": 1.0, "categoria": "IV", "classe": "B", "S3": 1.0,
                          "direcoes": ["+X", "-X", "+Y", "-Y"],
                          "dimensoes": {"lx": sum(vx), "ly": sum(vy)}, "Ca": None},
                "sismo": {"zona": 0, "classe_terreno": "C", "categoria_utilizacao": "I", "R": 3.0,
                          "sistema": "concreto_portico"}}
    return m


EXEMPLOS = {"viga_biapoiada": viga_biapoiada, "portico_aco": portico_aco,
            "galpao_aco": galpao_aco, "edificio_concreto": edificio_concreto}


def gerar(destino=AQUI):
    """Grava os JSON em `destino` e devolve os caminhos."""
    os.makedirs(destino, exist_ok=True)
    saidas = []
    for nome, f in EXEMPLOS.items():
        caminho = os.path.join(destino, nome + ".json")
        f().to_json(caminho)
        saidas.append(caminho)
    return saidas


if __name__ == "__main__":
    for c in gerar():
        print(c)
