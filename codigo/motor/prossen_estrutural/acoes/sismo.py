"""Sismo — NBR 15421:2006. Acelerações em fração de g; pesos/forças em kN.

Edição: a NBR 15421:2023 (publicada em 2023) corrigiu o mapa de perigo sísmico (zonas 0 e 1
deslocadas, novas zonas 2) e ajustou o formato do espectro (patamar começa em período menor), mas
as tabelas e expressões novas não estão em fonte pública acessível (blog IBRACON "Alterações na
ABNT NBR 15421", masteremmodelagem.com.br, abece.com.br: só descrição qualitativa). Por regra do
ESPEC §2 fica a edição 2006 (EDICAO) e o fato entra em COBERTURA e nos avisos de `casos_sismo`.
A zona vem do usuário (cfg["zona"], ou ag explícito em cfg["ag_g"]); com a zona de 2006 em vez da
de 2023 o resultado pode estar contra a segurança em municípios que mudaram de zona.

Convenções de `casos_sismo`: F_kN é o MÓDULO da força; o sentido vem de `direcao`. O integrador
deve passar `pesos_kN` = G + fracao_Q_sismo(uso)·Q de cada pavimento.
"""
import logging
import math

from .combinacoes import PSI

log = logging.getLogger(__name__)

EDICAO = "NBR 15421:2006"
COBERTURA = [
    {"norma": "NBR 15421", "edicao": "2006", "item": "6.1 / 7.3.1 / 7.3.2 (zonas, ag; zona 0 dispensa; zona 1: 0,01·w)",
     "verificacao": "ag por zona (limite superior da faixa) ou ag informado; zona 0 → nenhum caso",
     "limitacao": "Edição 2023 não confirmada em fonte pública: mapa (zonas) e formato do espectro mudaram; "
                  "resultado de 2006."},
    {"norma": "NBR 15421", "edicao": "2006", "item": "6.3 tabela 3 (Ca, Cv), espectro de projeto",
     "verificacao": "Ca/Cv das classes A–E com interpolação em ag (0,10g–0,15g); espectro Sa(T)",
     "limitacao": "Espectro de 2006 (a 2023 alterou o patamar). Classe F exige estudo específico: não coberta."},
    {"norma": "NBR 15421", "edicao": "2006", "item": "9.1 (Cs), 9.2 (Ta = Ct·hn^x, T ≤ Cup·Ta), 9.3 (k, Cvx)",
     "verificacao": "Força horizontal equivalente, período aproximado, distribuição vertical, SX e SY",
     "limitacao": "Cup da tabela 9 por zona (2: 1,7; 3: 1,6; 4: 1,5; França 2021, Miranda 2011); com ag_g "
                  "informado usa o menor Cup entre a zona e a faixa de ag (conservador); só para estruturas "
                  "regulares; análise modal espectral não implementada; sem fator de amplificação torcional "
                  "Ax (irregularidade torsional, categoria C)."},
    {"norma": "NBR 15421", "edicao": "2006", "item": "tabela 6 (R, Ω0, Cd); deslocamentos δx = Cd·δxe/I e "
                                                     "limites de Δx (0,020/0,015/0,010·hsx); torção acidental 5%",
     "verificacao": "TAB6 por sistema básico (17 sistemas; R do sistema se cfg.R ausente), drift_sismico, "
                    "torcao_acidental (Mz = F·0,05·L perpendicular; casos SX_TOR/SY_TOR com ± nas combinações)",
     "limitacao": "Valores conferidos na reprodução de Santos (UFRJ/Poli, orient. S. H. C. Santos e S. S. Lima), "
                  "tabelas 4.6 e 5.2; números de item da norma não conferidos. Alvenaria estrutural e limites "
                  "de altura por sistema/categoria sísmica não tabelados. O momento acidental é por pavimento: "
                  "exige diafragma (ou distribuição em binário pelo integrador)."},
    {"norma": "NBR 15421", "edicao": "2006", "item": "peso sísmico w (25% da sobrecarga em armazenagem/estacionamento)",
     "verificacao": "fracao_Q_sismo(uso) = max(ψ2 da NBR 8681, 25% em armazenagem/estacionamento)",
     "limitacao": "ψ2 de edifícios residenciais/comerciais é adotado por conservadorismo "
                  "(a norma só exige 25% em armazenagem e estacionamento)."},
]

# NBR 15421 6.1 / tabela 1: ag de projeto (limite superior da faixa; zona 4 = 0,15g)
AG_ZONA = {0: 0.025, 1: 0.05, 2: 0.10, 3: 0.15, 4: 0.15}
# NBR 15421 tabela 3: classe -> ((Ca ag≤0,10g, Ca ag=0,15g), (Cv ag≤0,10g, Cv ag=0,15g))
TAB3 = {
    "A": ((0.8, 0.8), (0.8, 0.8)),
    "B": ((1.0, 1.0), (1.0, 1.0)),
    "C": ((1.2, 1.2), (1.7, 1.7)),
    "D": ((1.6, 1.5), (2.4, 2.2)),
    "E": ((2.5, 2.1), (3.5, 3.4)),
}
FATOR_I = {"I": 1.0, "II": 1.25, "III": 1.5}  # categoria de utilização, tabela 4
# NBR 15421 9.2: (CT, x) de Ta = CT·hn^x. "aco_contraventado_trelica": pórticos de aço contraventados
# com treliças (Miranda 2011). "aco_excentrico" = mesmo valor, nome antigo mantido só porque a tela
# web (acoes.js, outro dono) ainda o envia — ponytail: remover quando a tela usar o nome novo.
CT_X = {"aco_portico": (0.0724, 0.8), "concreto_portico": (0.0466, 0.9),
        "aco_contraventado_trelica": (0.0731, 0.75), "aco_excentrico": (0.0731, 0.75),
        "outros": (0.0488, 0.75)}
# NBR 15421:2006 tabela 9: coeficiente de limitação do período Cup por zona sísmica
CUP_ZONA = {2: 1.7, 3: 1.6, 4: 1.5}
# NBR 15421:2006 tabela 6: sistema básico sismo-resistente -> (R, Ω0, Cd). Conferida na reprodução de
# Santos (UFRJ/Poli, tab. 4.6). "pp" = pilares-parede de concreto; dual_<pórticos>_<pilares-parede/treliça>.
TAB6 = {k: {"R": r, "Omega0": o, "Cd": cd} for k, (r, o, cd) in {
    "pilar_parede_concreto_especial": (5, 2.5, 5), "pilar_parede_concreto_usual": (4, 2.5, 4),
    "portico_concreto_especial": (8, 3, 5.5), "portico_concreto_intermediario": (5, 3, 4.5),
    "portico_concreto_usual": (3, 3, 2.5),
    "portico_aco_especial": (8, 3, 5.5), "portico_aco_intermediario": (4.5, 3, 4), "portico_aco_usual": (3.5, 3, 3),
    "contraventado_aco_especial": (6, 2, 5), "contraventado_aco_usual": (3.25, 2, 3.25),
    "dual_especial_pp_especial": (7, 2.5, 5.5), "dual_especial_pp_usual": (6, 2.5, 5),
    "dual_especial_contraventado_especial": (7, 2.5, 5.5), "dual_intermediario_pp_especial": (6.5, 2.5, 5),
    "dual_intermediario_pp_usual": (5.5, 2.5, 4.5), "dual_usual_pp_usual": (4.5, 2.5, 4),
    "pendulo_invertido": (2.5, 2, 2.5),
}.items()}
# NBR 15421:2006: limite do deslocamento relativo de pavimento Δx / hsx por categoria de utilização
# (reprodução de Santos, UFRJ/Poli, tab. 5.2)
LIMITE_DRIFT = {"I": 0.020, "II": 0.015, "III": 0.010}
EXC_ACIDENTAL = 0.05  # NBR 15421:2006: deslocamento do centro de massa = 5% da dimensão perpendicular às forças


def coef_terreno(ag, classe):
    """(Ca, Cv) da tabela 3 com interpolação linear entre 0,10g e 0,15g."""
    (ca1, ca2), (cv1, cv2) = TAB3[classe]
    t = min(max((ag - 0.10) / 0.05, 0.0), 1.0)
    return ca1 + (ca2 - ca1) * t, cv1 + (cv2 - cv1) * t


def espectro(ag, classe):
    """Sa(T) de projeto, NBR 15421 6.3 (ags0 = Ca·ag, ags1 = Cv·ag). Retorna T -> Sa (g)."""
    Ca, Cv = coef_terreno(ag, classe)
    ags0, ags1 = Ca * ag, Cv * ag

    def Sa(T):
        if T <= 0.08 * Cv / Ca:
            return ags0 * (18.75 * T * Ca / Cv + 1.0)
        if T <= 0.4 * Cv / Ca:
            return 2.5 * ags0
        return ags1 / T
    return Sa


def periodo_aproximado(hn, sistema="concreto_portico"):
    ct, x = CT_X[sistema]  # 9.2
    return ct * hn ** x


def cup(zona):
    """NBR 15421:2006 tabela 9: Cup por zona sísmica (2: 1,7; 3: 1,6; 4: 1,5). Zonas 0/1 não usam
    Cup (7.3); recebem o valor da zona 2 só para a função não falhar."""
    return CUP_ZONA[max(zona, 2)]


def _zona_de_ag(ag):
    """Zona da faixa de ag (NBR 15421:2006 6.1 tabela 1): ≤ 0,10g → 2; < 0,15g → 3; ≥ 0,15g → 4."""
    return 2 if ag <= 0.10 else 3 if ag < 0.15 else 4


def coef_sismico(ag, classe, R, I, T):
    """9.1: Cs = 2,5·ags0/(R/I) ≤ ags1/(T·R/I), e Cs ≥ 0,01."""
    Ca, Cv = coef_terreno(ag, classe)
    cs = min(2.5 * Ca * ag / (R / I), Cv * ag / (T * R / I))
    return max(cs, 0.01)


def fracao_Q_sismo(uso):
    """Fração da sobrecarga somada ao peso sísmico w: max(ψ2 da NBR 8681:2003 tabela 6, 25% em
    armazenagem/estacionamento — NBR 15421:2006, peso do pavimento). `uso` como em
    combinacoes.PSI ("residencial", "comercial", "escritorio", "biblioteca", "garagem").
    Item: NBR 15421:2006 (w) + NBR 8681:2003 tabela 6 (ψ2, combinações excepcionais)."""
    validos = sorted(k for k in PSI if k not in ("vento", "temperatura"))
    if uso not in validos:
        raise ValueError(f"uso desconhecido {uso!r}; use um de {validos}")
    armazenagem = 0.25 if uso in ("biblioteca", "garagem") else 0.0
    return max(PSI[uso][2], armazenagem)


def parametros_sismicos(alturas, zona, classe, R, I=1.0, sistema="concreto_portico", T=None, ag=None):
    """Cs, T usado, Ta, k, ags0, ags1, Cup para o método das forças equivalentes (zona ≥ 2).
    Cup (tabela 9) pela zona; com `ag` informado, o menor entre a zona e a faixa de ag (conservador)."""
    cu = cup(zona) if ag is None else min(cup(zona), cup(_zona_de_ag(ag)))
    ag = AG_ZONA[zona] if ag is None else ag
    Ta = periodo_aproximado(max(alturas), sistema)
    Ca, Cv = coef_terreno(ag, classe)
    Tu = Ta if T is None else min(T, cu * Ta)
    k = 1.0 if Tu <= 0.5 else 2.0 if Tu >= 2.5 else 1.0 + (Tu - 0.5) / 2.0  # 9.3
    return {"ag": ag, "Ca": Ca, "Cv": Cv, "ags0": Ca * ag, "ags1": Cv * ag, "Ta": Ta, "Cup": cu,
            "T": Tu, "k": k, "Cs": coef_sismico(ag, classe, R, I, Tu), "R": R, "I": I}


def _distribuir(pesos, cotas, p):
    """9.3: H = Cs·W distribuída por w·h^k (Cvx). Única implementação."""
    H = p["Cs"] * sum(pesos)
    s = [w * h ** p["k"] for w, h in zip(pesos, cotas)]
    return [H * x / sum(s) for x in s]


def forcas_equivalentes(pesos_pav, alturas, zona, classe, R, I=1.0,
                        sistema="concreto_portico", T=None):
    """Forças horizontais equivalentes por pavimento (kN), NBR 15421 9.
    alturas: cota de cada pavimento acima da base. T: período modal (limitado a Cup·Ta);
    None -> Ta."""
    if zona == 0:
        return [0.0] * len(pesos_pav)
    if zona == 1:
        return [0.01 * w for w in pesos_pav]  # 7.3.2
    p = parametros_sismicos(alturas, zona, classe, R, I, sistema, T)
    return _distribuir(pesos_pav, alturas, p)


def _inteiro(v, nome, lo, hi):
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v != int(v) or not lo <= v <= hi:
        raise ValueError(f"config.sismo.{nome} deve ser inteiro de {lo} a {hi} (recebido {v!r})")
    return int(v)


def casos_sismo(cfg, pavimentos, pesos_kN, T=None):
    """Casos SX e SY por forças horizontais equivalentes (ESPEC §7.3, NBR 15421 9).

    cfg: zona (0–4), classe_terreno (A–E), categoria_utilizacao (I/II/III; padrão I), R (>0; opcional se
    `sistema_resistente`, chave de TAB6, for dado: R da tabela 6; com os dois vale o R informado e
    `parametros` ganha Cd e Omega0),
    sistema (chave de CT_X; padrão concreto_portico), ag_g (opcional: ag em fração de g, substitui o
    da zona). pavimentos: [(nome, z_m)], z acima da base; pesos_kN: w de cada pavimento (G + parcela
    de Q = fracao_Q_sismo(uso)·Q), na mesma ordem. T: período modal (s) ou None → Ta.
    Zona 0 → [] (o integrador gera o aviso `sismo_dispensado`). Zona 1 → F = 0,01·w (7.3.2).
    Retorna [{"caso": "SX", "direcao": [1,0,0], "forcas": [{"pavimento","z","F_kN"}],
    "parametros": {ag, Ca, Cv, Cs, T, Ta, k, H_kN, R, I, ...}, "avisos": [...]}]; F_kN ≥ 0."""
    cfg = cfg or {}
    zona = _inteiro(cfg.get("zona", 0), "zona", 0, 4)
    if zona == 0:
        return []
    classe = str(cfg.get("classe_terreno", "")).upper()
    if classe not in TAB3:
        raise ValueError(f"config.sismo.classe_terreno deve ser A–E (recebido {cfg.get('classe_terreno')!r}); "
                         "classe F exige estudo específico")
    cat = str(cfg.get("categoria_utilizacao", "I")).upper()
    if cat not in FATOR_I:
        raise ValueError(f"config.sismo.categoria_utilizacao deve ser I, II ou III (recebido {cfg.get('categoria_utilizacao')!r})")
    R = cfg.get("R")
    sr = cfg.get("sistema_resistente")
    if sr is not None and sr not in TAB6:
        raise ValueError(f"config.sismo.sistema_resistente desconhecido {sr!r}; use um de {sorted(TAB6)}")
    if R is None and sr is not None:
        R = TAB6[sr]["R"]  # tabela 6
    if isinstance(R, bool) or not isinstance(R, (int, float)) or not math.isfinite(R) or R <= 0:
        raise ValueError(f"config.sismo.R deve ser número > 0 (recebido {R!r})")
    sistema = cfg.get("sistema", "concreto_portico")
    if sistema not in CT_X:
        raise ValueError(f"config.sismo.sistema desconhecido {sistema!r}; use um de {sorted(CT_X)}")
    ag = cfg.get("ag_g")
    if ag is not None and (isinstance(ag, bool) or not isinstance(ag, (int, float)) or not math.isfinite(ag) or ag <= 0):
        raise ValueError(f"config.sismo.ag_g deve ser número > 0 (recebido {ag!r})")
    if len(pavimentos) != len(pesos_kN):
        raise ValueError(f"{len(pavimentos)} pavimentos e {len(pesos_kN)} pesos")
    pav = sorted(((n, float(z), float(w)) for (n, z), w in zip(pavimentos, pesos_kN) if z > 1e-9),
                 key=lambda t: t[1])
    if not pav:
        raise ValueError("sismo_sem_pavimentos: nenhum pavimento acima da base (z > 0)")
    if any(b[1] - a[1] < 1e-6 for a, b in zip(pav, pav[1:])):
        raise ValueError("pavimentos com cotas repetidas")
    if any(not math.isfinite(w) or w < 0 for *_, w in pav) or sum(w for *_, w in pav) <= 0:
        raise ValueError("pesos_kN devem ser finitos, ≥ 0 e com soma > 0")
    cotas, pesos = [z for _, z, _ in pav], [w for *_, w in pav]
    I = FATOR_I[cat]
    avisos = [{"nivel": "aviso", "codigo": "sismo_edicao_2006",
               "mensagem": "Ações sísmicas pela NBR 15421:2006; a edição 2023 alterou o mapa de zonas e o espectro "
                           "(valores não confirmados em fonte pública). Verificar zona e resultado.", "ref": None}]
    if zona == 1:
        avisos.append({"nivel": "aviso", "codigo": "sismo_zona1_simplificado",
                       "mensagem": "Zona 1: apenas forças horizontais de 1% do peso em cada direção (NBR 15421:2006 7.3.2).",
                       "ref": None})
        par = {"zona": zona, "ag": AG_ZONA[1] if ag is None else ag, "Cs": 0.01, "T": None, "Ta": None, "k": None,
               "R": R, "I": I, "classe_terreno": classe, "sistema": sistema, "edicao": EDICAO}
        F = [0.01 * w for w in pesos]
    else:
        p = parametros_sismicos(cotas, zona, classe, R, I, sistema, T, ag)
        if T is not None and T > p["Cup"] * p["Ta"]:
            avisos.append({"nivel": "aviso", "codigo": "sismo_T_limitado",
                           "mensagem": f"T = {T:.3f} s limitado a Cup·Ta = {p['Cup'] * p['Ta']:.3f} s (NBR 15421:2006 9.2)",
                           "ref": None})
        par = dict(p, zona=zona, classe_terreno=classe, sistema=sistema, edicao=EDICAO)
        F = _distribuir(pesos, cotas, p)
    par["H_kN"] = sum(F)
    par["W_kN"] = sum(pesos)
    if sr is not None:
        par.update(sistema_resistente=sr, Cd=TAB6[sr]["Cd"], Omega0=TAB6[sr]["Omega0"])
        if R > TAB6[sr]["R"]:
            avisos.append({"nivel": "aviso", "codigo": "sismo_R_acima_tabela6",
                           "mensagem": f"R = {R:g} maior que o da tabela 6 da NBR 15421:2006 para {sr} "
                                       f"({TAB6[sr]['R']:g}): forças sísmicas subestimadas", "ref": None})
    out = []
    for caso, d in (("SX", (1.0, 0.0, 0.0)), ("SY", (0.0, 1.0, 0.0))):
        out.append({"caso": caso, "direcao": list(d),
                    "forcas": [{"pavimento": n, "z": z, "F_kN": f, "F_vet_kN": [f * d[0], f * d[1], 0.0]}
                               for (n, z, _), f in zip(pav, F)],
                    "parametros": dict(par), "avisos": [dict(a) for a in avisos]})
    log.debug("casos_sismo: zona %d, H = %.1f kN", zona, par["H_kN"])
    return out


def torcao_acidental(casos, dimensoes):
    """Momento torsional acidental por pavimento (NBR 15421:2006, distribuição horizontal das forças): o
    centro de massa é deslocado de e = 5% da dimensão da estrutura perpendicular às forças, Mz = F·e.
    `casos`: saída de `casos_sismo`; `dimensoes`: {lx, ly} (m). SX (forças em X) usa ly; SY usa lx.
    Retorna [{"caso": "SX_TOR", "sismo": "SX", "momentos": [{"pavimento", "z", "Mz_kNm"}],
    "parametros": {"e_m", "L_perp_m"}}]; Mz ≥ 0 (módulo, sentido anti-horário em torno de +Z). O sinal
    ± é dado pelas combinações (`combinacoes.TORCAO`): caso tipo "S" com o mesmo nome."""
    out = []
    for c in casos:
        dx, dy = c["direcao"][0], c["direcao"][1]
        L = float(dimensoes["ly"] if abs(dx) > abs(dy) else dimensoes["lx"])
        if not L > 0:
            raise ValueError(f"dimensões da planta devem ser > 0 (recebido {dimensoes!r})")
        e = EXC_ACIDENTAL * L
        out.append({"caso": c["caso"] + "_TOR", "sismo": c["caso"],
                    "momentos": [{"pavimento": f["pavimento"], "z": f["z"], "Mz_kNm": f["F_kN"] * e}
                                 for f in c["forcas"]],
                    "parametros": {"e_m": e, "L_perp_m": L, "edicao": EDICAO}})
    return out


def drift_sismico(deslocamentos_m, cotas_m, Cd, categoria="I"):
    """Deslocamentos sísmicos (NBR 15421:2006): δx = Cd·δxe/I (δxe da análise elástica com as forças de
    projeto, no centro de massa); Δx = δx(i) − δx(i−1) (base: 0) ≤ LIMITE_DRIFT[categoria]·hsx.
    `deslocamentos_m` e `cotas_m` (acima da base) por pavimento, em ordem crescente de cota.
    Retorna [{"z", "hsx_m", "delta_x_m", "Delta_m", "limite_m", "razao", "ok"}]."""
    cat = str(categoria).upper()
    if cat not in LIMITE_DRIFT:
        raise ValueError(f"categoria de utilização deve ser I, II ou III (recebido {categoria!r})")
    if len(deslocamentos_m) != len(cotas_m):
        raise ValueError(f"{len(deslocamentos_m)} deslocamentos e {len(cotas_m)} cotas")
    I = FATOR_I[cat]
    out, z0, d0 = [], 0.0, 0.0
    for de, z in zip(deslocamentos_m, cotas_m):
        hsx = z - z0
        if not hsx > 0:
            raise ValueError(f"cotas devem ser crescentes e acima da base (recebido {list(cotas_m)})")
        dx = Cd * de / I
        D = abs(dx - d0)
        lim = LIMITE_DRIFT[cat] * hsx
        out.append({"z": z, "hsx_m": hsx, "delta_x_m": dx, "Delta_m": D, "limite_m": lim, "razao": D / lim,
                    "ok": D <= lim})
        z0, d0 = z, dx
    return out
