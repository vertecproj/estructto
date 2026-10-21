"""Combinações de ações — NBR 8681:2003 (tabelas 2, 4, 5 e 6) e NBR 6118:2014 (tabelas 11.1/11.2).

Convenções:
- Entrada: dict {nome: CasoCarga}. tipo G/Q/V/S/E.
- Q: `subtipo` = uso para ψ ("residencial", "comercial", "escritorio", "biblioteca",
  "garagem", "temperatura"); vazio -> `tipo_uso` de `gerar_combinacoes` (ou "comercial").
  Subtipo desconhecido levanta ValueError (nunca vira "comercial" em silêncio).
- V: `subtipo` = GRUPO. Casos de vento do mesmo grupo (ex.: +X, -X, +Y, -Y no grupo
  "vento") são mutuamente exclusivos: nunca entram juntos na mesma combinação.
- G com `subtipo="recalque"`: ação permanente indireta (recalques de apoio), γε da NBR 8681:2003 tab. 3
  (1,2 desfavorável, 0 favorável; excepcional 0/0; ELS 1,0). Não entra no γg nem na origem das ações separadas.
- S e E: cada caso é exclusivo dos demais S/E; entram só em combinações excepcionais.
- S com nome "<caso>_TOR" (ex.: "SX_TOR"): momentos da torção acidental do sismo <caso>
  (sismo.torcao_acidental). Nunca entram sozinhos: cada ação sísmica cuja direção principal (100%)
  tem caso _TOR é trocada por duas, com +1,0 e −1,0 desse _TOR (NBR 15421:2006, torção acidental;
  com as duas direções simultâneas, só o momento da direção principal).
Saída: lista de (nome, {caso: fator}, estado) com estado em
"ELU", "ELU_EXC", "ELS_QP", "ELS_FREQ", "ELS_RARA". Nomes únicos: a combinação com ação
principal P e secundárias A, B chama-se "ELU P princ. + A + B" (ELS: "ELS FREQ P princ. + A");
secundárias com fator nulo não entram no nome. Sufixo " G fav" = permanentes com γ favorável
(1,0), gerado em toda combinação última (NBR 8681:2003 5.1.3.1 / tab. 2: γg favorável quando a
permanente reduz o efeito; igual ao CYPE). Excepcionais: cada Q entra com ψ2 ou ausente (5.1.3.3:
variáveis só quando desfavoráveis; NBR 8800:2008 tab. 2 nota e); sufixo " sem Q1,Q2" = ausentes.
Casos S: fator ±1,0 (o sismo inverte de sentido); com "SX" e "SY" presentes entram também
±SX ± 0,3·SY e ±SY ± 0,3·SX (NBR 15421:2006 8.5: 100% + 30%; aplicado a todas as zonas por
conservadorismo).
γ por `tipo_edificacao` (NBR 8681:2003 tabela 2 = permanentes agrupadas, γg desfavorável/favorável;
tabela 5 = variáveis consideradas conjuntamente, γq aplicado a todas as variáveis, inclusive vento):
- "tipo1" (cargas acidentais > 5 kN/m², ex.: depósitos, bibliotecas): normal γg 1,35/1,0, γq 1,5
  (inclusive temperatura); excepcional γg 1,15/1,0. `variabilidade` é ignorada.
- "tipo2" (demais edificações): normal γg 1,40/1,0, γq 1,4 (temperatura 1,2, tab. 4); excepcional
  γg 1,20/1,0. `variabilidade` é ignorada.
- None (padrão): GAMA_G[(combinação, variabilidade)] e γq 1,4 (temperatura 1,2) — com "grandes"
  dá os mesmos valores de "tipo2" (conservador para concreto, NBR 6118 tab. 11.1 = 1,4/1,4).
- "separadas" (NBR 8800:2008 tab. 1, ações separadas, para estruturas de aço; = CYPE "NBR 8800:2008"):
  γg desfavorável pela origem do caso G (`subtipo` ∈ GAMA_G_8800; vazio = "geral", 1,50), favorável 1,0;
  γq: uso e ocupação 1,5, vento 1,4, temperatura 1,2. `variabilidade` é ignorada.
"""
from itertools import product

EDICAO = "NBR 8681:2003"
COBERTURA = [
    {"norma": "NBR 8681", "edicao": "2003", "item": "5.1.3.1 (conf. ficha 005) / tabelas 2, 4, 5",
     "verificacao": "Combinações últimas normais (γg, γq, ψ0); vento e Q em grupos de exclusão; "
                    "γ por tipo de edificação (tipo 1: 1,35/1,5; tipo 2: 1,40/1,4)",
     "limitacao": "G favorável (γ = 1,0) e desfavorável em toda combinação, mas todos os G juntos "
                  "(não separa partes favoráveis e desfavoráveis do mesmo carregamento). Ações "
                  "indiretas só recalques de apoio (subtipo recalque, tab. 3: 1,2/0; excepcional 0). Sem combinações especiais/de construção. Sem tipo_edificacao: 1,4/1,4. "
                  "A NBR 8681 tem edição 09/2025, mas sem texto público confirmado: mantida a 2003."},
    {"norma": "NBR 8800", "edicao": "2008", "item": "4.7.6.1 tabela 1 (ações separadas) e tabela 2 (ψ)",
     "verificacao": "tipo_edificacao='separadas': γg por origem (metálica 1,25; pré-moldada 1,30; moldada no "
                    "local 1,35; industrializada com adições in loco 1,40; geral/equipamentos 1,50; "
                    "excepcional 1,10–1,30), γq uso 1,5, vento 1,4, temperatura 1,2; ψ de sobrecarga em "
                    "coberturas 0,8/0,7/0,6 (uso 'cobertura')",
     "limitacao": "Ações truncadas (1,2) e cargas móveis/pontes rolantes (tab. 2) não tabeladas."},
    {"norma": "NBR 8681", "edicao": "2003", "item": "5.1.3.3 (conf. ficha 005) / NBR 15421:2006 8.5",
     "verificacao": "Combinações últimas excepcionais com sismo: ΣγgGk ± S + Σψ2·Qk (cada Q presente ou "
                    "ausente), vento ausente (ψ2 = 0); ±SX ± 0,3·SY e ±SY ± 0,3·SX; ± torção acidental "
                    "(casos _TOR) da direção principal",
     "limitacao": "Casos E (excepcionais não sísmicos) entram só com fator +1,0."},
    {"norma": "NBR 8681", "edicao": "2003", "item": "5.1.5 (conf. ficha 005)",
     "verificacao": "ELS quase permanente, frequente e rara (ψ1, ψ2)", "limitacao": ""},
    {"norma": "NBR 8681", "edicao": "2003", "item": "tabela 6 (ψ0, ψ1, ψ2)",
     "verificacao": "ψ por tipo de uso (0,5/0,4/0,3; 0,7/0,6/0,4; 0,8/0,7/0,6) e vento 0,6/0,3/0",
     "limitacao": "Comercial e escritório usam o grupo 'predominância de pesos ou de pessoas' (0,7/0,6/0,4). "
                  "'cobertura' (0,8/0,7/0,6) vem da NBR 8800:2008 tab. 2 e é aplicada também a concreto "
                  "(a favor da segurança)."},
]

# γg agrupado (desfavorável, favorável) quando `tipo_edificacao` é None.
# "grandes": NBR 8681:2003 tabela 2, edificação tipo 2 (1,40/1,30/1,20) = NBR 6118:2014 tab. 11.1.
# "pequenas": normal 1,3 = NBR 6118:2014 tab. 11.1, nota (cargas permanentes de pequena variabilidade);
# especial 1,2 e excepcional 1,1 = mesmos valores da coluna "grandes pontes" da NBR 8681:2003 tab. 2,
# adotados por analogia (sem item próprio na 6118).
GAMA_G = {
    ("normal", "grandes"): (1.4, 1.0), ("normal", "pequenas"): (1.3, 1.0),
    ("especial", "grandes"): (1.3, 1.0), ("especial", "pequenas"): (1.2, 1.0),
    ("excepcional", "grandes"): (1.2, 1.0), ("excepcional", "pequenas"): (1.1, 1.0),
}
# NBR 8681:2003 tabela 5 (variáveis conjuntas), edificação tipo 2; temperatura 1,2 (tabela 4)
GAMA_Q = {"normal": 1.4, "especial": 1.2, "excepcional": 1.0}
GAMA_Q_TEMP = 1.2
# NBR 8681:2003 tabela 2 (γg agrupado) e tabela 5 (γq conjunto) por tipo de edificação
# (tipo 1: cargas acidentais > 5 kN/m²; tipo 2: as demais)
GAMA_G_TIPO = {"tipo1": {"normal": (1.35, 1.0), "especial": (1.25, 1.0), "excepcional": (1.15, 1.0)},
               "tipo2": {"normal": (1.40, 1.0), "especial": (1.30, 1.0), "excepcional": (1.20, 1.0)}}
GAMA_Q_TIPO = {"tipo1": {"normal": 1.5, "especial": 1.3, "excepcional": 1.0}, "tipo2": GAMA_Q}
# NBR 8800:2008 tabela 1, ações permanentes diretas separadas (desfavorável; favorável = 1,00):
# subtipo do caso G -> {combinação: γg}
GAMA_G_8800 = {
    "metalica": {"normal": 1.25, "especial": 1.15, "excepcional": 1.10},  # PP de estruturas metálicas
    "pre_moldada": {"normal": 1.30, "especial": 1.20, "excepcional": 1.15},
    "moldada_local": {"normal": 1.35, "especial": 1.25, "excepcional": 1.15},  # e elementos industrializados
    "industrializada_in_loco": {"normal": 1.40, "especial": 1.30, "excepcional": 1.20},
    "geral": {"normal": 1.50, "especial": 1.40, "excepcional": 1.30},  # elementos construtivos em geral, equip.
}
# NBR 8800:2008 tabela 1, ações variáveis separadas (normais): uso e ocupação, vento, temperatura
GAMA_Q_8800 = {"uso": 1.5, "vento": 1.4, "temperatura": 1.2}
# NBR 8681:2003 tabela 3 (ações permanentes indiretas: recalques de apoio, retração): γε
# (desfavorável, favorável) por combinação: normal 1,2/0, especial ou de construção 1,2/0, excepcional 0/0.
# ponytail: conferido com a NBR 6118 tab. 11.1 (mesmos valores); sem o texto da NBR 8681 à mão.
GAMA_RECALQUE = {"normal": (1.2, 0.0), "especial": (1.2, 0.0), "excepcional": (0.0, 0.0)}
SUBTIPO_RECALQUE = "recalque"  # caso G com este subtipo = recalques de apoio (No.recalques), γε em vez de γg
TIPOS_EDIFICACAO = (None, "tipo1", "tipo2", "separadas")
TORCAO = "_TOR"  # sufixo dos casos de torção acidental do sismo

# NBR 8681 tabela 6 / NBR 6118 tabela 11.2: (ψ0, ψ1, ψ2)
PSI = {
    "residencial": (0.5, 0.4, 0.3),
    "comercial": (0.7, 0.6, 0.4),
    "escritorio": (0.7, 0.6, 0.4),
    "biblioteca": (0.8, 0.7, 0.6),
    "garagem": (0.8, 0.7, 0.6),
    "cobertura": (0.8, 0.7, 0.6),  # NBR 8800:2008 tab. 2: "sobrecargas em coberturas (ver B.5.1)"
    "vento": (0.6, 0.3, 0.0),
    "temperatura": (0.6, 0.5, 0.3),
}


USOS_Q = sorted(k for k in PSI if k != "vento")
MAX_CASOS_Q = 12  # cada Q é um grupo: as seleções crescem como 2ⁿ


def psi(caso, tipo_uso=None):
    """(ψ0, ψ1, ψ2) do caso. Q: subtipo -> `tipo_uso` -> "comercial"; subtipo/uso fora de PSI = ValueError."""
    if caso.tipo == "V":
        return PSI["vento"]
    uso = caso.subtipo or tipo_uso or "comercial"
    if uso not in USOS_Q:
        raise ValueError(f"caso {caso.nome!r}: uso/subtipo de Q desconhecido {uso!r}; use um de {USOS_Q}")
    return PSI[uso]


def _gq(caso, comb="normal", tipo_edificacao=None):
    if tipo_edificacao == "separadas":  # NBR 8800:2008 tab. 1 (excepcionais: só ψ2 é usado)
        return GAMA_Q_8800["vento" if caso.tipo == "V" else "temperatura" if caso.subtipo == "temperatura" else "uso"]
    if tipo_edificacao == "tipo1":  # tab. 5: o γq conjunto vale para todas as variáveis
        return GAMA_Q_TIPO["tipo1"][comb]
    if comb == "normal" and caso.subtipo == "temperatura":
        return GAMA_Q_TEMP
    return GAMA_Q[comb]


def _grupos_variaveis(casos):
    """Lista de grupos; cada grupo = lista de casos mutuamente exclusivos."""
    grupos, vento = [], {}
    for c in casos.values():
        if c.tipo == "Q":
            grupos.append([c])
        elif c.tipo == "V":
            # vento máximo da NBR 8400 (acoes/nbr8400.py) é o mesmo vento: nunca junto com o de serviço
            vento.setdefault("vento" if c.subtipo == "vento_maximo" else c.subtipo or "vento", []).append(c)
    return grupos + list(vento.values())


def _selecoes(grupos):
    """Todas as escolhas de no máximo um caso por grupo (sem a vazia)."""
    for esc in product(*[[None] + g for g in grupos]):
        sel = [c for c in esc if c is not None]
        if sel:
            yield sel


def gerar_combinacoes(casos, tipo_uso=None, variabilidade="grandes", tipo_edificacao=None):
    """NBR 8681 5.1.3 (ELU) e 5.1.5 (ELS). `tipo_uso` vale para os Q sem subtipo.
    `tipo_edificacao`: None, "tipo1", "tipo2" ou "separadas" (γ da docstring do módulo)."""
    if tipo_edificacao not in TIPOS_EDIFICACAO:
        raise ValueError(f"tipo_edificacao deve ser um de {TIPOS_EDIFICACAO} (recebido {tipo_edificacao!r})")

    def gama_g(comb):
        """{caso G: (γ desfavorável, γ favorável)}."""
        if tipo_edificacao == "separadas":
            out = {}
            for g in G:
                origem = casos[g].subtipo or "geral"
                if origem not in GAMA_G_8800:
                    raise ValueError(f"caso {g!r}: origem da permanente {origem!r} desconhecida para ações "
                                     f"separadas (NBR 8800:2008 tab. 1); use um de {sorted(GAMA_G_8800)}")
                out[g] = (GAMA_G_8800[origem][comb], 1.0)
            return out
        par = GAMA_G_TIPO[tipo_edificacao][comb] if tipo_edificacao else GAMA_G[(comb, variabilidade)]
        return {g: par for g in G}

    def ps(c):
        return psi(c, tipo_uso)

    nq = sum(c.tipo == "Q" for c in casos.values())
    if nq > MAX_CASOS_Q:
        raise ValueError(f"{nq} casos Q: o máximo é {MAX_CASOS_Q} (combinações crescem como 2ⁿ); agrupe sobrecargas")
    for c in casos.values():
        if c.tipo == "Q":
            psi(c, tipo_uso)  # recusa subtipo desconhecido já na entrada
    G = [c.nome for c in casos.values() if c.tipo == "G" and c.subtipo != SUBTIPO_RECALQUE]
    REC = [c.nome for c in casos.values() if c.tipo == "G" and c.subtipo == SUBTIPO_RECALQUE]
    gn = gama_g("normal")
    grupos = _grupos_variaveis(casos)
    sel_all = list(_selecoes(grupos))
    out, vistos = [], set()

    def add(nome, f, est):
        f = {k: v for k, v in f.items() if v}
        chave = (est, tuple(sorted((k, round(v, 6)) for k, v in f.items())))
        if chave not in vistos:
            vistos.add(chave)
            out.append((nome, f, est))

    def permanentes(gm, fav, comb="normal"):
        f = {g: gm[g][1 if fav else 0] for g in G}
        f.update({r: GAMA_RECALQUE[comb][1 if fav else 0] for r in REC})  # NBR 8681:2003 tab. 3
        return f

    def nome(prefixo, p, f, sufixo=""):
        sec = [k for k in f if k != p.nome and k not in G and k not in REC]
        return f"{prefixo} {p.nome} princ." + "".join(f" + {k}" for k in sec) + sufixo

    # ELU normal: Fd = Σγg·Gk + γq·Q1k + Σγq·ψ0j·Qjk  — NBR 8681 5.1.3.1; cada combinação com os G
    # desfavoráveis e com os G favoráveis (γg = 1,0, tab. 2)
    for fav in (False, True):
        if G or REC:
            add("ELU G" + (" fav" if fav else ""), permanentes(gn, fav), "ELU")
        for sel in sel_all:
            for p in sel:
                f = permanentes(gn, fav)
                for c in sel:
                    f[c.nome] = _gq(c, "normal", tipo_edificacao) * (1.0 if c is p else ps(c)[0])
                add(nome("ELU", p, f, " G fav" if fav else ""), f, "ELU")

    # ELU excepcional/sísmica: ΣγgGk + Fexc + Σψ2·Qk  — NBR 8681 5.1.3.3 / NBR 15421 (γ = 1,0)
    # S entra com ±1,0 (o sismo inverte); SX+SY: 100% + 30% (NBR 15421:2006 8.5). Vento: ψ2 = 0.
    gexc = gama_g("excepcional")
    exc = [c for c in casos.values() if c.tipo in ("S", "E") and not c.nome.endswith(TORCAO)]
    acoes = []  # (rótulo, {caso: fator})
    for e in exc:
        for sg in ((1.0, -1.0) if e.tipo == "S" else (1.0,)):
            acoes.append((("-" if sg < 0 else "") + e.nome, {e.nome: sg}))
    if "SX" in casos and "SY" in casos:
        for a, b in (("SX", "SY"), ("SY", "SX")):
            for sa, sb in product((1.0, -1.0), repeat=2):
                acoes.append((f"{'-' if sa < 0 else ''}{a} {'-' if sb < 0 else '+'} 0.3*{b}",
                              {a: sa, b: 0.3 * sb}))
    # torção acidental: ± o caso _TOR da direção principal (fator ±1,0) no lugar da ação sem torção
    com_tor = []
    for rotulo, fe in acoes:
        tor = next((k + TORCAO for k, v in fe.items() if abs(v) == 1.0), None)
        if tor in casos:
            com_tor += [(f"{rotulo} {s} {tor}", dict(fe, **{tor: v})) for s, v in (("+", 1.0), ("-", -1.0))]
        else:
            com_tor.append((rotulo, fe))
    qs = [grp[0] for grp in grupos if grp[0].tipo == "Q"]
    for rotulo, fe in com_tor:
        for fav in (False, True):
            for pres in product((True, False), repeat=len(qs)):  # cada Q com ψ2 ou ausente (5.1.3.3)
                f = permanentes(gexc, fav, "excepcional")
                f.update(fe)
                f.update({q.nome: ps(q)[2] for q, on in zip(qs, pres) if on})
                aus = [q.nome for q, on in zip(qs, pres) if not on]
                add(f"ELU EXC {rotulo}" + (f" sem {','.join(aus)}" if aus else "") + (" G fav" if fav else ""),
                    f, "ELU_EXC")

    # ELS — NBR 8681 5.1.5 / NBR 6118 11.8.3
    f = {g: 1.0 for g in G + REC}  # ELS: γε = 1,0
    for grp in grupos:
        f[grp[0].nome] = ps(grp[0])[2]  # vento tem ψ2 = 0 -> some
    add("ELS QP", f, "ELS_QP")
    for sel in sel_all:
        for p in sel:
            ff = {g: 1.0 for g in G + REC}
            fr = dict(ff)
            for c in sel:
                ff[c.nome] = ps(c)[1] if c is p else ps(c)[2]
                fr[c.nome] = 1.0 if c is p else ps(c)[1]
            add(nome("ELS FREQ", p, {k: v for k, v in ff.items() if v}), ff, "ELS_FREQ")
            add(nome("ELS RARA", p, {k: v for k, v in fr.items() if v}), fr, "ELS_RARA")
    return out
