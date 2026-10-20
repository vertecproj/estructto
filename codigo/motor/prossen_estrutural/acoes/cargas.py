"""Cargas — NBR 6120:2019: pesos específicos (tabela 1, item 5.3) e cargas variáveis (tabela 10, 6.2).

Fonte dos valores (conferidos em duas cópias públicas independentes da norma/tabelas e em material
de aula): texto da NBR 6120:2019 reproduzido em pdfcoffee.com (tabelas 1, 10 e 13, itens 5.3, 6.4,
6.6.1), calculistadeaco.com.br/tabela-de-peso-especifico-aparente-dos-materiais-de-construção
(tabela 1) e notas de aula da UFAL (PFF — Ações e Segurança: 25,0 kN/m³ concreto armado,
78,5 aço, 1,5 dormitórios, 2,5 escritórios, 3,0 corredor de uso comum). Valores de faixa: a norma
(5.3) permite o médio entre parênteses; para aço adota-se o limite superior (78,5), conservador
para peso próprio. Texto da norma não é copiado (direito autoral ABNT): só item, uso e valor.

Valores característicos nominais MÍNIMOS: a norma manda validar caso a caso quando o uso real for
mais severo. As cargas não incluem divisórias de posição indefinida (tabela 11) nem o peso próprio.
"""
import math

EDICAO = "NBR 6120:2019"
COBERTURA = [
    {"norma": "NBR 6120", "edicao": "2019", "item": "5.3 tabela 1",
     "verificacao": "Peso específico aparente: concreto, aço, alumínio, blocos, argamassas e madeiras usuais",
     "limitacao": "Rochas, metais raros e madeiras naturais por espécie não incluídos."},
    {"norma": "NBR 6120", "edicao": "2019", "item": "6.2 tabela 10; 6.4; 6.6.1 tabela 13",
     "verificacao": "Cargas variáveis de uso usuais (residencial, escritórios, lojas, garagens, coberturas, "
                    "escadas e corredores, bibliotecas) com fator ψ da NBR 8681",
     "limitacao": "Só os usos listados em lista_cargas_uso(); sem redução de carga (6.12), sem cargas "
                  "concentradas, sem divisórias (tabela 11), sem forças horizontais em guarda-corpos. "
                  "Linhas de escadas por ordem de leitura da tabela (conferem com 2,5/3,0 da edição de 1980)."},
]

# NBR 6120:2019 tabela 1 (item 5.3): chave -> (valor kN/m³, descrição, faixa ou None)
_PESOS = {
    "concreto_armado": (25.0, "Concreto armado", None),
    "concreto_simples": (24.0, "Concreto simples", None),
    "aco": (78.5, "Aço (faixa 77–78,5, médio 77,8; adotado o limite superior)", (77.0, 78.5)),
    "aluminio": (28.0, "Alumínio e ligas", None),
    "bloco_concreto_vazado": (14.0, "Bloco de concreto vazado (estrutural, classes A e B)", None),
    "bloco_ceramico_vazado": (12.0, "Bloco cerâmico vazado com paredes vazadas (estrutural)", None),
    "bloco_ceramico_vazado_paredes_macicas": (14.0, "Bloco cerâmico vazado com paredes maciças (estrutural)", None),
    "bloco_ceramico_macico": (18.0, "Bloco cerâmico maciço", None),
    "bloco_celular_autoclavado_c25": (5.5, "Bloco de concreto celular autoclavado C25", None),
    "argamassa_cal_cimento_areia": (19.0, "Argamassa de cal, cimento e areia", None),
    "argamassa_cimento_areia": (21.0, "Argamassa de cimento e areia (faixa 19–23, médio 21)", (19.0, 23.0)),
    "argamassa_cal": (15.0, "Argamassa de cal (faixa 12–18, médio 15)", (12.0, 18.0)),
    "madeira_conifera_c20": (5.0, "Madeira maciça conífera C20 (U = 12%)", None),
    "madeira_conifera_c25": (5.5, "Madeira maciça conífera C25 (U = 12%)", None),
    "madeira_conifera_c30": (6.0, "Madeira maciça conífera C30 (U = 12%)", None),
    "madeira_dicotiledonea_c20": (6.5, "Madeira maciça dicotiledônea C20 (U = 12%)", None),
    "madeira_dicotiledonea_c30": (8.0, "Madeira maciça dicotiledônea C30 (U = 12%)", None),
    "madeira_dicotiledonea_c40": (9.5, "Madeira maciça dicotiledônea C40 (U = 12%)", None),
    "madeira_dicotiledonea_c60": (10.0, "Madeira maciça dicotiledônea C60 (U = 12%)", None),
}

# NBR 6120:2019 tabela 10 (6.2) salvo indicação: chave -> (q kN/m², descrição, item, grupo ψ da NBR 8681)
_USOS = {
    "residencial_dormitorio": (1.5, "Residencial: dormitórios", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_sala_copa_cozinha": (1.5, "Residencial: sala, copa, cozinha", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_sanitario": (1.5, "Residencial: sanitários", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_despensa_servico_lavanderia": (2.0, "Residencial: despensa, área de serviço e lavanderia", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_corredor_interno": (1.5, "Residencial: corredores dentro de unidades autônomas", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_corredor_comum": (3.0, "Residencial: corredores de uso comum", "6.2 tabela 10, edifícios residenciais", "residencial"),
    "residencial_area_comum": (3.0, "Residencial: salão de festas/jogos e áreas de uso comum", "6.2 tabela 10, edifícios residenciais", "comercial"),
    "residencial_balcao": (2.5, "Balcões, sacadas, varandas e terraços (residencial)", "6.2 tabela 10, balcões", "residencial"),
    "escritorio_sala_geral": (2.5, "Escritórios: salas de uso geral e sanitários", "6.2 tabela 10, edifícios comerciais, corporativos e de escritórios", "escritorio"),
    "escritorio_corredor_comum": (3.0, "Escritórios: corredores de uso comum", "6.2 tabela 10, edifícios comerciais, corporativos e de escritórios", "escritorio"),
    "escritorio_arquivo_deslizante": (5.0, "Escritórios: regiões de arquivos deslizantes", "6.2 tabela 10, edifícios comerciais, corporativos e de escritórios", "biblioteca"),
    "loja": (4.0, "Lojas, centros comerciais: circulações e lojas em geral", "6.2 tabela 10, lojas", "comercial"),
    "biblioteca_leitura_sem_estantes": (3.0, "Bibliotecas: sala de leitura sem estantes", "6.2 tabela 10, bibliotecas", "biblioteca"),
    "biblioteca_leitura_com_estantes": (4.0, "Bibliotecas: sala de leitura com estantes", "6.2 tabela 10, bibliotecas", "biblioteca"),
    "biblioteca_estantes": (6.0, "Bibliotecas: sala com estantes de livros (6 até 2,2 m de estante + 2 kN/m² por metro excedente)", "6.2 tabela 10, bibliotecas", "biblioteca"),
    "biblioteca_arquivo_deslizante": (5.0, "Bibliotecas: regiões de arquivos deslizantes", "6.2 tabela 10, bibliotecas", "biblioteca"),
    "garagem_automoveis": (3.0, "Garagens e estacionamentos de automóveis (PBT ≤ 30 kN, categoria Ia; concentrada 12 kN)", "6.6.1 tabela 13", "garagem"),
    "cobertura_laje_manutencao": (1.0, "Coberturas em laje (concreto, mista, alvenaria estrutural) acessíveis só para manutenção", "6.2 tabela 10, coberturas", "cobertura"),
    "cobertura_sem_acesso": (0.5, "Coberturas leves sem acesso (q = 0,5·α, inclinação ≤ 2%; usar carga_cobertura(i) para i maior)", "6.4", "cobertura"),
    "escada_residencial_unidade": (2.5, "Escadas dentro de unidades autônomas residenciais/hotéis", "6.2 tabela 10, escadas e passarelas", "residencial"),
    "escada_sem_acesso_publico": (2.5, "Escadas sem acesso público", "6.2 tabela 10, escadas e passarelas", "residencial"),
    "escada_com_acesso_publico": (3.0, "Escadas com acesso público", "6.2 tabela 10, escadas e passarelas", "comercial"),
    "escada_uso_comum": (3.0, "Escadas de uso comum (residenciais, hotéis, comerciais, escritórios, escolas)", "6.2 tabela 10, escadas e passarelas", "comercial"),
}


def peso_especifico(material):
    """NBR 6120:2019 5.3 tabela 1. Retorna dict(valor_kN_m3, item, descricao[, faixa_kN_m3])."""
    try:
        v, desc, faixa = _PESOS[material]
    except KeyError:
        raise ValueError(f"material desconhecido {material!r}; use um de {sorted(_PESOS)}") from None
    r = {"valor_kN_m3": v, "item": "5.3 tabela 1", "descricao": desc}
    if faixa:
        r["faixa_kN_m3"] = list(faixa)
    return r


def carga_uso(uso):
    """NBR 6120:2019 6.2 tabela 10 (ou 6.4 / 6.6.1). Retorna dict(q_kN_m2, item, descricao, grupo_psi);
    `grupo_psi` é o uso de acoes.combinacoes.PSI (NBR 8681 tabela 6) para o caso Q."""
    try:
        q, desc, item, psi = _USOS[uso]
    except KeyError:
        raise ValueError(f"uso desconhecido {uso!r}; use um de {sorted(_USOS)}") from None
    return {"q_kN_m2": q, "item": item, "descricao": desc, "grupo_psi": psi}


def lista_cargas_uso():
    """Todos os usos disponíveis: [dict(uso, q_kN_m2, item, descricao, grupo_psi)]."""
    return [dict(uso=k, **carga_uso(k)) for k in _USOS]


def carga_cobertura(inclinacao_pct):
    """NBR 6120:2019 6.4: q = 0,50·α (0,25 ≤ q ≤ 0,50 kN/m²); α = 1,0 (i ≤ 2%), 2,0 − 0,5·i
    (2% < i < 3%), 0,5 (i ≥ 3%); i em %. Cobertura leve acessível só para manutenção, mínimo 1%."""
    i = inclinacao_pct
    if isinstance(i, bool) or not isinstance(i, (int, float)) or not math.isfinite(i) or i < 1.0:
        raise ValueError(f"inclinação deve ser ≥ 1% (6.4); recebido {i!r}")
    alfa = 1.0 if i <= 2.0 else 0.5 if i >= 3.0 else 2.0 - 0.5 * i
    return {"q_kN_m2": min(0.50, max(0.25, 0.50 * alfa)), "item": "6.4"}
