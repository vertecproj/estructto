"""Vento — NBR 6123:2023 (estático). Unidades: m/s, m, kN/m², kN.

Edição: a NBR 6123:2023 mantém o procedimento de S1 (5.2) e de S2 (5.3.3, S2 = bm·Fr·(z/10)^p; em
2023 a tabela 1 traz bm e p e a tabela 2 passou a ser o fator de rajada Fr, com os valores da
tabela 1 de 1988) e o mapa de isopletas V0 (sem consenso para mudar). A tabela de S2 por altura
(tabela 2 de 1988) e a regra "categoria V: S2 constante até 10 m" (5.3.3 de 1988) não foram
confirmadas no texto de 2023: vale a de 1988 (a favor da segurança; ver COBERTURA). Mudou o S3 (tabela 4: grupos 1–5 = 1,11/1,06/1,00/0,95/0,83, fator 0,92 para vedações). Fontes
públicas: ISQ Brasil (isqbrasil.com.br, "NBR 6123 principais alterações na revisão da norma") e
"ABNT NBR6123:2023 principais mudanças" (calculistadeaco.com.br, comparativo antes/depois com as
tabelas). O coeficiente de arrasto Ca segue a fig. 4 da edição de 1988 (a 2023 reorganizou os
anexos em 6.x; mudança de valores não confirmada em fonte pública) — ver COBERTURA.

Convenções de `casos_vento`: F_kN é o MÓDULO da força (≥ 0); o sentido vem de `direcao`
(vetor unitário com sinal) e de `F_vet_kN` (= F_kN·direcao). Não multiplique F_kN pelo sinal de novo.
"""
import bisect
import logging
import math

log = logging.getLogger(__name__)

EDICAO = "NBR 6123:2023"
COBERTURA = [
    {"norma": "NBR 6123", "edicao": "2023", "item": "4.2 (q = 0,613·Vk²), 5.1 (Vk = V0·S1·S2·S3)",
     "verificacao": "Velocidade característica e pressão dinâmica", "limitacao": ""},
    {"norma": "NBR 6123", "edicao": "2023", "item": "5.2 (S1)",
     "verificacao": "Fator topográfico: terreno plano, vale e talude/morro (procedimento inalterado em 2023)",
     "limitacao": ""},
    {"norma": "NBR 6123", "edicao": "2023", "item": "5.3.3, tabela 1 (bm, p, zg) e tabela 2 (Fr)",
     "verificacao": "S2 = bm·Fr·(z/10)^p, categorias I–V, classes A–C, z ≤ zg; z < 5 m usa 5 m; "
                    "categoria V: z < 10 m usa 10 m",
     "limitacao": "Altura mínima de 5 m e S2 constante até 10 m na categoria V vêm da edição 1988 "
                  "(5.3.3 e tabela 2 de S2); não confirmadas no texto de 2023."},
    {"norma": "NBR 6123", "edicao": "2023", "item": "5.4 tabela 4 (S3)",
     "verificacao": "Grupos 1–5 (1,11/1,06/1,00/0,95/0,83) e fator 0,92 para vedações",
     "limitacao": ""},
    {"norma": "NBR 6123", "edicao": "1988",
     "item": "6.3 fig. 4 (Ca de edificações paralelepipédicas)",
     "verificacao": "Ca para vento de baixa turbulência (conservador frente à alta turbulência), "
                    "ábaco digitalizado a partir da reprodução da figura em trabalho acadêmico público",
     "limitacao": "Figura da edição 1988 (2023 não confirmada em fonte pública); erro de leitura "
                  "estimado ±0,03; coluna h/l1 = 0,5 refeita pelas leituras do verificador (l1/l2 ≥ 3 "
                  "sem leitura nessa coluna: mantido 1,30, a favor da segurança); fora do ábaco usa-se a borda (l1/l2 < 0,26: valor de 0,26). "
                  "Alta turbulência (fig. 5) não implementada."},
    {"norma": "NBR 6123", "edicao": "2023", "item": "estático equivalente por pavimento (Fa = Ca·q(z)·l1·h_infl)",
     "verificacao": "Forças de arrasto por pavimento nas 4 direções",
     "limitacao": "Sem platibanda/cobertura além da meia altura do último andar; sem excentricidade "
                  "(torção) de vento; sem efeitos de vizinhança nem vento dinâmico (cap. 9 e 10: só aviso)."},
    {"norma": "NBR 6123", "edicao": "1988",
     "item": "6.1 tabela 4 (paredes) e tabela 5 (telhados de duas águas), 6.2.5 (Cpi): galpões",
     "verificacao": "casos_vento_galpao: Ce de paredes e cobertura por pórtico transversal (vento 0°/180° e "
                    "90°/270°), (Ce − Cpi)·q·largura de influência; Cpi 0/−0,3 (quatro faces) ou +0,2/−0,3 "
                    "(duas faces opostas, envoltória) ou informado",
     "limitacao": "Tabelas da edição 1988 (2023 não confirmada em fonte pública), conferidas na reprodução de "
                  "Storte (UFU) e no exemplo de Oliveira (UFMG/DEES 2015). Só h/b ≤ 3/2, θ ≤ 60°, a ≥ b "
                  "(cumeeira ao longo do comprimento); a/b > 4 usa a linha 2 ≤ a/b ≤ 4; 3/2 < a/b < 2: valor "
                  "mais severo das duas linhas (interpolação não confirmada); A3/B3 e I/J = −0,2 para a/b ≥ 2 e "
                  "= A2/B2 e FH abaixo disso (nota da tabela não confirmada: adotado o mais severo). q no topo "
                  "(cumeeira), constante na altura. Sem Cpe médio (zonas locais de borda: terças, telhas), sem "
                  "paredes de oitão (C/D: contraventamento), sem lanternim, sem atrito (6.4)."},
]

# NBR 6123:2023 tabela 1 (bm, p por categoria e classe) e zg; tabela 2 (Fr, fator de rajada, da cat. II).
# Os valores coincidem com a tabela 1 da edição de 1988 (b = bm).
TAB21 = {
    "I": ({"A": 1.10, "B": 1.11, "C": 1.12}, {"A": 0.06, "B": 0.065, "C": 0.07}, 250.0),
    "II": ({"A": 1.00, "B": 1.00, "C": 1.00}, {"A": 0.085, "B": 0.09, "C": 0.10}, 300.0),
    "III": ({"A": 0.94, "B": 0.94, "C": 0.93}, {"A": 0.10, "B": 0.105, "C": 0.115}, 350.0),
    "IV": ({"A": 0.86, "B": 0.85, "C": 0.84}, {"A": 0.12, "B": 0.125, "C": 0.135}, 420.0),
    "V": ({"A": 0.74, "B": 0.73, "C": 0.71}, {"A": 0.15, "B": 0.16, "C": 0.175}, 500.0),
}
FR = {"A": 1.00, "B": 0.98, "C": 0.95}  # NBR 6123:2023 tabela 2
# NBR 6123:2023 tabela 4: grupo -> (S3, Tp em anos). Grupo 3 = residências, hotéis, comércio, indústrias.
S3_TAB = {1: (1.11, 100), 2: (1.06, 75), 3: (1.00, 50), 4: (0.95, 37), 5: (0.83, 15)}
S3_GRUPO = {g: v[0] for g, v in S3_TAB.items()}
S3_VEDACAO = 0.92  # NOTA da tabela 4: vedações podem usar 0,92·S3

DIRECOES = {"+X": (1.0, 0.0, 0.0), "-X": (-1.0, 0.0, 0.0), "+Y": (0.0, 1.0, 0.0), "-Y": (0.0, -1.0, 0.0)}


def s1(terreno="plano", z=0.0, d=1.0, theta_graus=0.0):
    """NBR 6123 5.2. 'plano' 1,0; 'vale' 0,9; 'talude': ponto no topo (5.2-b),
    d = desnível; S1 = 1 + (2,5 − z/d)·tg(θ − 3°) ≥ 1 (6° ≤ θ ≤ 17°), 1 + (2,5 − z/d)·0,31 ≥ 1
    (θ ≥ 45°), interpolação linear em 3°–6° e 17°–45°."""
    if terreno == "plano":
        return 1.0
    if terreno == "vale":
        return 0.9
    if terreno != "talude":
        raise ValueError(f"terreno desconhecido {terreno!r}; use 'plano', 'vale' ou 'talude'")
    if not d > 0:
        raise ValueError(f"desnível d deve ser > 0 (recebido {d!r})")
    a = max(0.0, 2.5 - z / d)
    t = theta_graus

    def f(th):  # 6° ≤ θ ≤ 17°
        return max(1.0, 1.0 + a * math.tan(math.radians(th - 3.0)))
    if t <= 3:
        return 1.0
    if t < 6:
        return 1.0 + (f(6) - 1.0) * (t - 3) / 3
    if t <= 17:
        return f(t)
    if t < 45:
        return f(17) + (1.0 + 0.31 * a - f(17)) * (t - 17) / 28
    return 1.0 + 0.31 * a


def s2(categoria, classe, z):
    """NBR 6123 5.3.3: S2 = bm·Fr·(z/10)^p, z ≤ zg; z < 5 m usa 5 m (início da tabela 2 de 1988);
    categoria V: S2 constante até 10 m (5.3.3 de 1988; tabela 2: linha ≤ 5 m = linha de 10 m)."""
    b, ps, zg = TAB21[categoria]
    z = min(max(z, 10.0 if categoria == "V" else 5.0), zg)
    return b[classe] * FR[classe] * (z / 10.0) ** ps[classe]


def s3(grupo=3, vedacao=False):
    """NBR 6123:2023 tabela 4. `vedacao=True` aplica 0,92·S3 (nota da tabela, só para vedações)."""
    return S3_GRUPO[grupo] * (S3_VEDACAO if vedacao else 1.0)


def velocidade_caracteristica(V0, S1, cat, classe, z, S3):
    """NBR 6123 5.1: Vk = V0·S1·S2·S3 (m/s)."""
    return V0 * S1 * s2(cat, classe, z) * S3


def pressao_dinamica(V0, S1, cat, classe, z, S3):
    """NBR 6123 4.2: Vk = V0·S1·S2·S3; q = 0,613·Vk² (N/m²) -> kN/m²."""
    return 0.613 * velocidade_caracteristica(V0, S1, cat, classe, z, S3) ** 2 / 1000.0


# NBR 6123 6.3 fig. 4 — Ca de edificações paralelepipédicas, vento de baixa turbulência.
# Digitalizada (rastreio das 15 isolinhas 0,7…2,1 sobre a figura reproduzida em
# riut.utfpr.edu.br/jspui/bitstream/1/25471/1/analiseestruturaledificio.pdf, fig. 10; cada
# rótulo conferido contra a isolinha mais próxima). Entre isolinhas: interpolação linear (em
# ln h/l1); acima da isolinha mais alta presente: até +0,1; abaixo da mais baixa: o valor dela
# (conservador). Erro de leitura ≈ ±0,03. l1 = dimensão perpendicular ao vento, l2 = paralela.
# Coluna h/l1 = 0,5 refeita (VC-acoes-002: a 1ª digitalização ficou até +0,1 acima da curva) com as
# leituras do verificador na fig. 4 (verificacao/2026-10-20-acoes.md): l1/l2 0,4 → 0,71; 0,6 → 0,84;
# 1 → 1,00; 2 → 1,16. l1/l2 0,5 e 0,8: interpolação em ln entre essas leituras (0,78; 0,93).
# l1/l2 1,5 → 1,12: ajustado para reproduzir a leitura (l1/l2 1,667; h/l1 0,6) = 1,166 (curvas 1,2
# em h/l1 0,742 e 1,3 em 1,373). l1/l2 ≤ 0,3 (0,70, menor isolinha) e ≥ 3 (1,30): sem leitura,
# mantidos (a favor da segurança).
_L1L2 = [0.26, 0.3, 0.4, 0.5, 0.6, 0.8, 1.0, 1.5, 2.0, 3.0, 4.0]
_HL1 = [0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 15, 20, 30, 40]
_CA = [  # linhas: l1/l2 ; colunas: h/l1
    [0.70, 0.70, 0.70, 0.70, 0.70, 0.70, 0.70, 0.70, 0.75, 0.82, 0.84, 0.88, 0.91, 0.98, 1.10],
    [0.70, 0.71, 0.74, 0.76, 0.77, 0.78, 0.80, 0.83, 0.84, 0.87, 0.89, 0.95, 1.00, 1.06, 1.10],
    [0.71, 0.80, 0.82, 0.85, 0.87, 0.88, 0.91, 0.95, 0.97, 1.01, 1.05, 1.11, 1.17, 1.25, 1.30],
    [0.78, 0.85, 0.90, 0.93, 0.96, 0.99, 1.03, 1.06, 1.09, 1.13, 1.18, 1.26, 1.33, 1.43, 1.50],
    [0.84, 0.93, 0.98, 1.02, 1.05, 1.08, 1.12, 1.16, 1.19, 1.25, 1.31, 1.39, 1.45, 1.54, 1.60],
    [0.93, 1.06, 1.12, 1.17, 1.20, 1.24, 1.29, 1.33, 1.36, 1.41, 1.46, 1.56, 1.64, 1.76, 1.90],
    [1.00, 1.14, 1.21, 1.26, 1.29, 1.32, 1.36, 1.40, 1.44, 1.50, 1.56, 1.68, 1.78, 1.91, 1.99],
    [1.12, 1.24, 1.30, 1.35, 1.38, 1.41, 1.45, 1.49, 1.54, 1.62, 1.71, 1.86, 1.95, 2.06, 2.20],
    [1.16, 1.28, 1.34, 1.38, 1.42, 1.45, 1.49, 1.54, 1.59, 1.68, 1.77, 1.90, 1.99, 2.10, 2.20],
    [1.30, 1.32, 1.38, 1.42, 1.45, 1.48, 1.52, 1.57, 1.61, 1.69, 1.76, 1.89, 1.98, 2.09, 2.20],
    [1.30, 1.33, 1.39, 1.43, 1.45, 1.48, 1.53, 1.57, 1.61, 1.67, 1.72, 1.82, 1.91, 2.03, 2.10],
]
_LN_L1L2 = [math.log(x) for x in _L1L2]
_LN_HL1 = [math.log(x) for x in _HL1]


def _interp(xs, x):
    x = min(max(x, xs[0]), xs[-1])
    i = min(max(bisect.bisect_right(xs, x) - 1, 0), len(xs) - 2)
    return i, (x - xs[i]) / (xs[i + 1] - xs[i])


def coef_arrasto(l1, l2, h):
    """Ca de edificação paralelepipédica, NBR 6123 6.3 / fig. 4 (baixa turbulência), interpolação
    bilinear em (ln l1/l2, ln h/l1) sobre a tabela digitalizada."""
    i, u = _interp(_LN_L1L2, math.log(l1 / l2))
    j, v = _interp(_LN_HL1, math.log(h / l1))
    c = _CA
    return ((1 - u) * (1 - v) * c[i][j] + u * (1 - v) * c[i + 1][j]
            + (1 - u) * v * c[i][j + 1] + u * v * c[i + 1][j + 1])


def forcas_por_pavimento(cotas, l1, l2, V0, S1=1.0, cat="II", classe="B", S3=1.0, Ca=None):
    """Força de arrasto por pavimento: Fa = Ca·q(z)·l1·h_infl (NBR 6123 4.2.3).
    cotas: cotas crescentes dos pavimentos acima da base; h_infl = meia altura de cada
    andar adjacente. Retorna lista de (z, F em kN)."""
    Ca = Ca if Ca is not None else coef_arrasto(l1, l2, cotas[-1])
    out = []
    for k, z in enumerate(cotas):
        abaixo = z - (cotas[k - 1] if k else 0.0)
        acima = (cotas[k + 1] - z) if k + 1 < len(cotas) else 0.0
        q = pressao_dinamica(V0, S1, cat, classe, z, S3)
        out.append((z, Ca * q * l1 * (abaixo + acima) / 2))
    return out


def _num(cfg, chave, minimo_exclusivo=0.0, padrao=None, prefixo="config.vento"):
    v = cfg.get(chave, padrao)
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v <= minimo_exclusivo:
        raise ValueError(f"{prefixo}.{chave} deve ser número > {minimo_exclusivo:g} (recebido {v!r})")
    return float(v)


def _dados_vento(cfg):
    """(V0, S1, S3, categoria, classe, direcoes) validados de config.vento."""
    V0 = _num(cfg, "V0")
    S1 = _num(cfg, "S1", padrao=1.0)
    S3v = _num(cfg, "S3", padrao=1.0)
    cat = str(cfg.get("categoria", "")).upper()
    classe = str(cfg.get("classe", "")).upper()
    if cat not in TAB21:
        raise ValueError(f"config.vento.categoria deve ser I–V (recebido {cfg.get('categoria')!r})")
    if classe not in FR:
        raise ValueError(f"config.vento.classe deve ser A–C (recebido {cfg.get('classe')!r})")
    direcoes = list(cfg.get("direcoes") or DIRECOES)
    ruins = [d for d in direcoes if d not in DIRECOES]
    if ruins:
        raise ValueError(f"config.vento.direcoes inválidas {ruins}; use +X, -X, +Y, -Y")
    return V0, S1, S3v, cat, classe, direcoes


def _aviso(codigo, mensagem):
    return {"nivel": "aviso", "codigo": codigo, "mensagem": mensagem, "ref": None}


def periodo_estimado_vento(h):
    """Estimativas de T1 (s) para o aviso de vento dinâmico (NBR 6123 cap. 9: T1 > 1 s).
    Retorna dict nome -> T1. Concreto: Ta = 0,0466·h^0,9 (NBR 15421:2006 9.2) e 0,05 + 0,015·h
    (expressão usual da NBR 6123/Blessmann, não confirmada em fonte aberta); aço momento-resistente:
    Ta = 0,0724·h^0,8 (NBR 15421:2006 9.2). O aviso usa o maior valor (conservador)."""
    return {"concreto_15421": 0.0466 * h ** 0.9, "concreto_0.05+0.015h": 0.05 + 0.015 * h,
            "aco_15421": 0.0724 * h ** 0.8}


def casos_vento(cfg, pavimentos, dimensoes=None):
    """Casos de vento estático por direção (ESPEC §7.3).

    cfg: V0 (m/s, >0), S1 (padrão 1,0), categoria (I–V), classe (A–C), S3 (>0, padrão 1,0),
    direcoes (padrão as 4), dimensoes {lx, ly} (m; o argumento `dimensoes` tem prioridade), Ca
    (None → fig. 4), T1_s (opcional, para o aviso de vento dinâmico).
    pavimentos: [(nome, z_m)] — z > 0 acima da base; z ≤ 0 é ignorado (base).
    Retorna [{"caso": "V+X", "direcao": [1,0,0], "forcas": [{"pavimento","z","F_kN","S2","Vk_m_s","q_kN_m2"}],
    "parametros": {...}, "avisos": [{nivel, codigo, mensagem, ref}]}]; F_kN ≥ 0 (módulo).
    ValueError com mensagem clara para entrada inválida."""
    cfg = cfg or {}
    V0, S1, S3v, cat, classe, direcoes = _dados_vento(cfg)
    dim = dimensoes or cfg.get("dimensoes") or {}
    lx, ly = _num(dim, "lx"), _num(dim, "ly")
    Ca_cfg = cfg.get("Ca")
    if Ca_cfg is not None:
        Ca_cfg = _num(cfg, "Ca")
    pav = sorted(((n, float(z)) for n, z in pavimentos if z > 1e-9), key=lambda t: t[1])
    if not pav:
        raise ValueError("vento_sem_pavimentos: nenhum pavimento acima da base (z > 0)")
    cotas = [z for _, z in pav]
    if any(b - a < 1e-6 for a, b in zip(cotas, cotas[1:])):
        raise ValueError(f"pavimentos com cotas repetidas: {cotas}")
    h = cotas[-1]
    zg = TAB21[cat][2]
    avisos = []
    if h > zg:
        avisos.append(_aviso("vento_s2_fora_faixa",
                             f"altura {h:g} m > zg = {zg:g} m da categoria {cat}: S2 limitado a z = zg (NBR 6123 5.3.3)"))
    T = periodo_estimado_vento(h)
    T1 = float(cfg["T1_s"]) if cfg.get("T1_s") else max(T.values())
    if T1 > 1.0:
        avisos.append(_aviso("vento_dinamico",
                             f"T1 ≈ {T1:.2f} s > 1 s: efeitos dinâmicos do vento (NBR 6123 cap. 9) não considerados; "
                             "analisar com modelo dinâmico"))
    out = []
    for d in direcoes:
        dx, dy, dz = DIRECOES[d]
        l1, l2 = (ly, lx) if dx else (lx, ly)  # l1 perpendicular ao vento, l2 paralela
        Ca = Ca_cfg if Ca_cfg is not None else coef_arrasto(l1, l2, h)
        fs = forcas_por_pavimento(cotas, l1, l2, V0, S1, cat, classe, S3v, Ca)
        Vk = velocidade_caracteristica(V0, S1, cat, classe, h, S3v)
        out.append({
            "caso": f"V{d}", "direcao": [dx, dy, dz],
            "forcas": [{"pavimento": n, "z": z, "F_kN": F, "F_vet_kN": [F * dx, F * dy, 0.0],
                        "S2": s2(cat, classe, z),
                        "Vk_m_s": velocidade_caracteristica(V0, S1, cat, classe, z, S3v),
                        "q_kN_m2": pressao_dinamica(V0, S1, cat, classe, z, S3v)}
                       for (n, _), (z, F) in zip(pav, fs)],
            "parametros": {"V0": V0, "S1": S1, "S3": S3v, "categoria": cat, "classe": classe, "Ca": Ca,
                           "l1": l1, "l2": l2, "h_topo": h, "h_l1": h / l1, "l1_l2": l1 / l2,
                           "S2_topo": s2(cat, classe, h), "Vk_topo_m_s": Vk,
                           "q_topo_kN_m2": pressao_dinamica(V0, S1, cat, classe, h, S3v),
                           "F_total_kN": sum(F for _, F in fs), "T1_estimado_s": T1, "edicao": EDICAO},
            "avisos": list(avisos),
        })
    log.debug("casos_vento: %d direções, h=%.2f m", len(out), h)
    return out


# ---------------------------------------------------------------- galpões (pórticos transversais)
# NBR 6123:1988 tabela 4 — Ce de paredes de edificações de planta retangular (conferida na reprodução de
# Storte, UFU, p. 32). Chave (faixa h/b, faixa a/b): h/b 1 = h/b ≤ 1/2, 2 = 1/2 < h/b ≤ 3/2; a/b 1 =
# 1 ≤ a/b ≤ 3/2, 2 = 2 ≤ a/b ≤ 4. Só as paredes longitudinais, que carregam os pórticos transversais:
# α = 0°: A1/B1 e A2/B2 (laterais); α = 90°: A (barlavento) e B (sotavento).
TAB4_PAREDES = {
    (1, 1): {"A1": -0.8, "A2": -0.5, "A": 0.7, "B": -0.4},
    (1, 2): {"A1": -0.8, "A2": -0.4, "A": 0.7, "B": -0.5},
    (2, 1): {"A1": -0.9, "A2": -0.5, "A": 0.7, "B": -0.5},
    (2, 2): {"A1": -0.9, "A2": -0.4, "A": 0.7, "B": -0.6},
}
# NBR 6123:1988 tabela 5 — Ce de telhados de duas águas simétricos (Storte, UFU, p. 31), por θ (graus).
# α = 90°: EF (água de barlavento), GH (sotavento); α = 0°: EG (faixa inicial), FH (resto).
TAB5_THETA = [0.0, 5.0, 10.0, 15.0, 20.0, 30.0, 45.0, 60.0]
TAB5_TELHADO = {
    1: {"EF": [-0.8, -0.9, -1.2, -1.0, -0.4, 0.0, 0.3, 0.7], "GH": [-0.4] * 6 + [-0.5, -0.6],
        "EG": [-0.8] * 4 + [-0.7] * 4, "FH": [-0.4, -0.4] + [-0.6] * 6},
    2: {"EF": [-0.8, -0.9, -1.1, -1.0, -0.7, -0.2, 0.2, 0.6], "GH": [-0.6] * 4 + [-0.5] * 4,
        "EG": [-1.0, -0.9] + [-0.8] * 6, "FH": [-0.6] * 5 + [-0.8] * 3},
}
# NBR 6123 6.2.5: a) duas faces opostas igualmente permeáveis: +0,2 (vento ⟂ face permeável) ou −0,3
# (⟂ face impermeável) — ponytail: as duas em todas as direções (envoltória, sem saber qual face é a
# permeável); b) quatro faces igualmente permeáveis: −0,3 ou 0 (o mais nocivo → as duas).
CPI = {"quatro_faces": (0.0, -0.3), "duas_faces_opostas": (0.2, -0.3)}


def _faixa_hb(h_b):
    if h_b <= 0.5:
        return 1
    if h_b <= 1.5:
        return 2
    raise ValueError(f"galpão com h/b = {h_b:.2f} > 3/2: fora das faixas implementadas das tabelas 4/5 da NBR 6123")


def coef_telhado(faixa_hb, theta_graus):
    """NBR 6123 tabela 5: {EF, GH, EG, FH} com interpolação linear em θ (0° a 60°)."""
    if not 0.0 <= theta_graus <= 60.0:
        raise ValueError(f"inclinação do telhado θ = {theta_graus:.1f}° fora da tabela 5 da NBR 6123 (0° a 60°)")
    i, u = _interp(TAB5_THETA, theta_graus)
    return {k: v[i] + (v[i + 1] - v[i]) * u for k, v in TAB5_TELHADO[faixa_hb].items()}


def coef_paredes(faixa_hb, a_b):
    """NBR 6123 tabela 4: {A1, A2, A, B}. 3/2 < a/b < 2: o mais severo (maior |Ce|) das duas linhas
    (interpolação da nota não confirmada); a/b > 4: linha 2 ≤ a/b ≤ 4."""
    l1, l2 = TAB4_PAREDES[(faixa_hb, 1)], TAB4_PAREDES[(faixa_hb, 2)]
    if a_b <= 1.5:
        return dict(l1)
    if a_b >= 2.0:
        return dict(l2)
    return {k: max(l1[k], l2[k], key=abs) for k in l1}


def _media(s0, s1, limites, valores):
    """Média de uma função constante por trechos [limites[i], limites[i+1]) sobre [s0, s1]."""
    tot = sum(max(0.0, min(s1, b) - max(s0, a)) * v for a, b, v in zip(limites, limites[1:], valores))
    return tot / (s1 - s0)


def casos_vento_galpao(cfg, geometria):
    """Casos de vento por pórtico transversal de galpão de duas águas (NBR 6123:1988 tab. 4 e 5, 6.2.5).

    cfg: V0, S1, S3, categoria, classe, direcoes (como `casos_vento`), permeabilidade ("quatro_faces",
    padrão, ou "duas_faces_opostas") ou cpi (lista de Cpi informados, ex. abertura dominante 6.2.5 c).
    geometria (convenção do gerador `galpao`): vao (X), altura_pilar (beiral), altura_cumeeira, n_modulos,
    espacamento (pórticos em y = j·espacamento). b = vão, a = comprimento (exige a ≥ b), h = altura_pilar;
    q no topo (cumeeira, a favor da segurança). Vento ±X: α = 90° (A/B, EF/GH); ±Y: α = 0° (zonas
    A1/A2/A3 e EG/FH/IJ a partir da extremidade de barlavento: a1 = max(b/3, a/4) ≤ 2h, A2 até a/2).
    Retorna um caso por (direção, Cpi): [{"caso": "V+X Cpi=-0.3", "direcao", "alfa_graus", "Cpi",
    "porticos": [{"portico": j, "y", "largura_m", "barras": {pilar_esq|pilar_dir|agua_esq|agua_dir:
    {"Ce", "Cpi", "C", "w_kN_m", "w_vet_kN_m"}}}], "parametros", "avisos"}]. C = Ce − Cpi; w = C·q·largura
    (kN/m por metro de barra) > 0 = pressão para dentro; w_vet_kN_m = w·normal interna em coordenadas
    globais (aplicar como carga distribuída global na barra)."""
    cfg = cfg or {}
    V0, S1, S3v, cat, classe, direcoes = _dados_vento(cfg)
    g = geometria or {}
    vao, hp, hc, esp = (_num(g, k, prefixo="geometria") for k in ("vao", "altura_pilar", "altura_cumeeira",
                                                                   "espacamento"))
    n = g.get("n_modulos")
    if isinstance(n, bool) or not isinstance(n, int) or n < 1:
        raise ValueError(f"geometria.n_modulos deve ser inteiro ≥ 1 (recebido {n!r})")
    if hc < hp:
        raise ValueError(f"altura_cumeeira ({hc:g} m) menor que altura_pilar ({hp:g} m)")
    a, b, h = n * esp, vao, hp
    if a < b:
        raise ValueError(f"comprimento {a:g} m < vão {b:g} m: tabelas 4/5 aplicadas com a cumeeira ao longo do "
                         "comprimento (a ≥ b); fora do escopo")
    faixa = _faixa_hb(h / b)
    theta = math.degrees(math.atan2(hc - hp, b / 2))
    tel = coef_telhado(faixa, theta)
    par = coef_paredes(faixa, a / b)
    if "cpi" in cfg:
        cpis = cfg["cpi"]
        if not isinstance(cpis, (list, tuple)) or not cpis or any(
                isinstance(c, bool) or not isinstance(c, (int, float)) or not math.isfinite(c) for c in cpis):
            raise ValueError(f"config.vento.cpi deve ser lista de números (recebido {cpis!r})")
    else:
        perm = cfg.get("permeabilidade", "quatro_faces")
        if perm not in CPI:
            raise ValueError(f"config.vento.permeabilidade deve ser um de {sorted(CPI)} (recebido {perm!r})")
        cpis = CPI[perm]
    q = pressao_dinamica(V0, S1, cat, classe, hc, S3v)
    a1 = min(max(b / 3, a / 4), 2 * h)
    lim = [0.0, a1, a / 2, a]
    longo = a / b >= 2.0  # A3/B3 e I/J = −0,2 (a/b ≥ 2); abaixo: o valor da zona anterior (mais severo)
    zonas_par = [par["A1"], par["A2"], -0.2 if longo else par["A2"]]
    zonas_tel = [tel["EG"], tel["FH"], -0.2 if longo else tel["FH"]]
    st, ct = math.sin(math.radians(theta)), math.cos(math.radians(theta))
    normal = {"pilar_esq": (1.0, 0.0, 0.0), "pilar_dir": (-1.0, 0.0, 0.0),
              "agua_esq": (st, 0.0, -ct), "agua_dir": (-st, 0.0, -ct)}  # normais internas
    avisos = []
    if a / b > 4:
        avisos.append(_aviso("vento_galpao_ab_fora_tabela", f"a/b = {a / b:.2f} > 4: usada a linha 2 ≤ a/b ≤ 4 "
                                                            "da tabela 4 da NBR 6123"))
    param = {"V0": V0, "S1": S1, "S3": S3v, "categoria": cat, "classe": classe, "q_kN_m2": q, "z_q_m": hc,
             "a_m": a, "b_m": b, "h_m": h, "h_b": h / b, "a_b": a / b, "theta_graus": theta, "a1_m": a1,
             "Ce_paredes": par, "Ce_telhado": tel, "edicao": "NBR 6123:1988 tab. 4/5, 6.2.5"}
    out = []
    for d in direcoes:
        dx, dy, _ = DIRECOES[d]
        faixas = []  # (j, y, largura, {barra: Ce})
        for j in range(n + 1):
            y = j * esp
            y0, y1 = max(0.0, y - esp / 2), min(a, y + esp / 2)
            if dx:  # α = 90°: barlavento do lado de onde vem o vento
                bar, sot = ("esq", "dir") if dx > 0 else ("dir", "esq")
                ce = {f"pilar_{bar}": par["A"], f"pilar_{sot}": par["B"], f"agua_{bar}": tel["EF"],
                      f"agua_{sot}": tel["GH"]}
            else:  # α = 0°: s medido a partir da extremidade de barlavento
                s0, s1 = (y0, y1) if dy > 0 else (a - y1, a - y0)
                cp, cc = _media(s0, s1, lim, zonas_par), _media(s0, s1, lim, zonas_tel)
                ce = {"pilar_esq": cp, "pilar_dir": cp, "agua_esq": cc, "agua_dir": cc}
            faixas.append((j, y, y1 - y0, ce))
        for cpi in cpis:
            porticos = [{"portico": j, "y": y, "largura_m": larg, "barras": {
                k: {"Ce": c, "Cpi": cpi, "C": c - cpi, "w_kN_m": (c - cpi) * q * larg,
                    "w_vet_kN_m": [(c - cpi) * q * larg * x for x in normal[k]]} for k, c in ce.items()}}
                for j, y, larg, ce in faixas]
            out.append({"caso": f"V{d} Cpi={cpi:g}", "direcao": [dx, dy, 0.0], "alfa_graus": 90.0 if dx else 0.0,
                        "Cpi": cpi, "porticos": porticos, "parametros": dict(param), "avisos": list(avisos)})
    log.debug("casos_vento_galpao: %d casos, θ = %.1f°, q = %.3f kN/m²", len(out), theta, q)
    return out
