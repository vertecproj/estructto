"""Relatório de cálculo (memória) em HTML A4 imprimível e Markdown.

Só apresentação: recebe dicts JSON (modelo no formato ESPEC §5, cálculo no formato §6.5) e não
implementa nenhuma fórmula de norma. Um único montador produz uma lista de blocos; dois
renderizadores (HTML e Markdown) a imprimem, de modo que os dois formatos têm o mesmo conteúdo.
Estrutura de classes e CSS: software/design/IDENTIDADE.md §9 e web/estatico/relatorio.css.

Números em pt-BR (vírgula decimal, sem separador de milhar), casas fixas por grandeza:
kN 2, kN·m 2, m 3, mm 1, taxa 2 (3 quando reprovada arredondaria para 1,00).
"""
import math
import re
from datetime import datetime
from html import escape as _esc
from pathlib import Path

_EST = Path(__file__).parent / "relatorio_estilo"  # CSS, fontes e logotipo do relatório (ex-web/estatico)

AVISO_TITULO = "Ferramenta de apoio."
AVISO = ("Todo resultado precisa de verificação por engenheiro responsável (ART): o responsável técnico "
         "confere esta memória de cálculo e assina a ART. O software não substitui o julgamento de engenharia.")

_ESTADO = {"ok": "Aprovada", "atencao": "Atenção", "reprovada": "Reprovada", "nao_verificada": "Não verificada"}
_ESTADO_MD = {"ok": "Aprovada", "atencao": "Atenção", "reprovada": "**REPROVADA**",
              "nao_verificada": "**NÃO VERIFICADA**"}
_ESTADO_COMB = {"ELU": "Última normal", "ELU_EXC": "Última excepcional", "ELS_QP": "Quase permanente",
                "ELS_FREQ": "Frequente", "ELS_RARA": "Rara"}
_CLASSIF = {"nos_fixos": "nós fixos", "nos_moveis": "nós móveis", "fora_do_limite": "fora do limite de aplicação",
            "pequena": "pequena deslocabilidade", "media": "média deslocabilidade", "grande": "grande deslocabilidade"}
_TIPO_BARRA = {"aco": "Aço", "pff": "Perfil formado a frio", "concreto": "Concreto"}
_F6 = ("Fx", "Fy", "Fz", "Mx", "My", "Mz")
_ARMADURA = (("As_cm2", "As", "cm²"), ("As2_cm2", "As2 (comprimida)", "cm²"), ("Asw_s_cm2_m", "Asw/s", "cm²/m"),
             ("Asl_torcao_cm2", "Asl torção", "cm²"), ("As_pilar_cm2", "As pilar", "cm²"))
_MAX_CARGAS = 100
_MAX_MAJ = 30


# ----------------------------------------------------------------------------- formatação pt-BR
def _f(v, casas=2):
    """Número com vírgula e casas fixas; None/NaN/inf → '—'; sem '-0,00'."""
    try:
        x = float(v)
    except (TypeError, ValueError):
        return "—" if v is None else str(v)
    if not math.isfinite(x):
        return "—"
    s = f"{x:.{casas}f}"
    if float(s) == 0:
        s = f"{0:.{casas}f}"
    return s.replace(".", ",")


def _t(v, mx=3):
    """Valor genérico de dado de entrada: número sem zeros finais; bool, lista e dict legíveis."""
    if v is None:
        return "—"
    if isinstance(v, bool):
        return "sim" if v else "não"
    if isinstance(v, (int, float)):
        s = _f(v, mx)
        return s.rstrip("0").rstrip(",") if "," in s else s
    if isinstance(v, dict):
        return "; ".join(f"{k} = {_t(x)}" for k, x in v.items())
    if isinstance(v, (list, tuple)):
        return ", ".join(_t(x) for x in v)
    return str(v)


def _taxa(v, ok=None):
    """Taxa com 2 casas; reprovada que arredondaria para 1,00 mostra 3 casas."""
    s = _f(v, 2)
    if ok is False and s != "—" and float(s.replace(",", ".")) <= 1.0:
        s = _f(v, 3)
    return s


def _estado(ok, taxa):
    if ok is False and taxa is None:  # barra que o motor não pôde verificar (dados insuficientes): não é reprovação
        return "nao_verificada"
    if ok is False or (ok is None and taxa is not None and taxa > 1.0):
        return "reprovada"
    return "atencao" if taxa is not None and taxa > 0.9 else "ok"


def _faixa(taxa, est):
    """Faixa do medidor (limites 0,5 / 0,7 / 0,9 / 1,0 de IDENTIDADE.md §4.3)."""
    if est == "reprovada":
        return 5
    return 4 if taxa > 0.9 else 3 if taxa > 0.7 else 2 if taxa > 0.5 else 1


def _data(iso):
    """'2026-10-20T05:12:00-03:00' → ('20/10/2026', '20/10/2026 às 05:12'); ilegível → texto cru."""
    try:
        d = datetime.fromisoformat(str(iso))
    except ValueError:
        s = str(iso or "—")
        return s, s
    return d.strftime("%d/%m/%Y"), d.strftime("%d/%m/%Y às %H:%M")


_RE_PERFIL = re.compile(r"^([A-Za-z]{1,3}) ?(\d+(?:[.,]\d+)?(?:[xX×]\d+(?:[.,]\d+)?)+)$")


def _perfil(nome):
    """'W360x51' → 'W 360 × 51'; 'W200x35.9' → 'W 200 × 35,9' (IDENTIDADE §5). Outros nomes ficam como estão."""
    g = _RE_PERFIL.match(str(nome))
    if not g:
        return str(nome)
    return g.group(1) + " " + " × ".join(p.replace(".", ",") for p in re.split("[xX×]", g.group(2)))


def _nat(s):
    return [int(p) if p.isdigit() else p for p in re.split(r"(\d+)", str(s))]


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


# ----------------------------------------------------------------------------- células e blocos
# Célula: str | ("estado", chave) | ("med", taxa, chave).  Coluna: (rótulo, unidade, tipo) com tipo
# "t" texto, "n" número, "x" taxa, "m" texto mudo, "g" medidor.  Linha: (células, classe).
def _linha_taxa(prefixo, taxa, ok):
    """Células taxa + medidor + estado de uma linha verificada."""
    est = _estado(ok, taxa)
    return list(prefixo) + [_taxa(taxa, ok), ("med", taxa or 0.0, est), ("estado", est)], ("reprovada" if est == "reprovada" else "")


def _ordem(itens, taxa="taxa"):
    """Reprovadas primeiro; dentro de cada grupo, taxa decrescente."""
    return sorted(itens, key=lambda i: (i.get("ok") is not False, -(i.get(taxa) if _num(i.get(taxa)) else -1.0)))


def _tab(cols, rows, vazio):
    return ("tab", cols, rows) if rows else ("p", vazio)


def _descricao_modelo(modelo_d):
    nos, barras = modelo_d.get("nos") or {}, modelo_d.get("barras") or {}
    por_tipo = {}
    for b in barras.values():
        por_tipo[b.get("tipo", "aco")] = por_tipo.get(b.get("tipo", "aco"), 0) + 1
    tipos = ", ".join(f"{n} {_TIPO_BARRA.get(t, t).lower()}" for t, n in por_tipo.items())
    apoios = sum(1 for n in nos.values() if any(n.get("apoio") or []))
    return f"{len(nos)} nós, {len(barras)} barras" + (f" ({tipos})" if tipos else "") + f", {apoios} nós com apoio"


def _montar(modelo_d, calculo_d):
    """Devolve (meta, blocos). Seções sem dado viram uma frase 'não aplicável'."""
    modelo_d, calculo_d = modelo_d or {}, calculo_d or {}
    nome = str(modelo_d.get("nome") or calculo_d.get("modelo") or "Sem nome")
    normas = calculo_d.get("normas") or []
    comb = calculo_d.get("combinacoes") or []
    ver = _ordem(calculo_d.get("verificacao") or [])
    est = calculo_d.get("estabilidade") or {}
    desl = calculo_d.get("deslocamentos") or {}
    opc = calculo_d.get("opcoes") or {}
    res = calculo_d.get("resumo") or {}
    cfg = modelo_d.get("config") or {}
    B = []

    # 1. Resumo
    reprov = [str(v.get("barra", "")) for v in ver if _estado(v.get("ok"), v.get("taxa")) == "reprovada"]
    nao_ver = [str(v.get("barra", "")) for v in ver if _estado(v.get("ok"), v.get("taxa")) == "nao_verificada"]
    atenc = [str(v.get("barra", "")) for v in ver if _estado(v.get("ok"), v.get("taxa")) == "atencao"]
    n_est = sum(1 for c in comb if str(c.get("estado", "")).startswith("ELS"))
    gz, b2 = est.get("gama_z") or {}, est.get("B2") or {}
    anal = ("Análise de 2ª ordem (P-Δ) em cada combinação última, serviço por superposição linear" if opc.get("pdelta")
            else "Linear elástica de 1ª ordem, combinações por superposição")
    pares = [("Normas", ", ".join(f"{n.get('norma', '')}:{n.get('edicao', '')}" for n in normas) or "—"),
             ("Unidades", "kN, m, kN·m; tensões em MPa; deslocamentos em mm"),
             ("Modelo", _descricao_modelo(modelo_d)),
             ("Ações", f"{len(modelo_d.get('casos') or {})} casos de carga, {len(comb) - n_est} combinações últimas "
                       f"e {n_est} de serviço"),
             ("Análise", anal)]
    if gz or b2:
        partes = [f"γz = {_f(x.get('valor'), 3)} ({d}, {_CLASSIF.get(x.get('classificacao'), x.get('classificacao'))})"
                  for d, x in gz.items()]
        partes += [f"B2 máx. = {_f(x.get('max'), 3)} ({d}, {_CLASSIF.get(x.get('classificacao'), x.get('classificacao'))})"
                   for d, x in b2.items()]
        pares.append(("Estabilidade global", "; ".join(partes)))
    if ver:
        # taxa_max = pior critério dependente de esforço (ESPEC §7.2): barra reprovada só por conformidade
        # (cobrimento, classe do concreto) tem taxa 0 e não é a "mais solicitada", mesmo vindo primeiro em `ver`
        pior = max((v for v in ver if _num(v.get("taxa"))), key=lambda v: v["taxa"], default=ver[0])
        pares += [("Taxa máxima", [f"{_taxa(pior.get('taxa'), pior.get('ok'))} em {pior.get('barra', '')}, ",
                                   ("estado", _estado(pior.get("ok"), pior.get("taxa")), True)]),
                  ("Barras verificadas", f"{res.get('n_verificadas', len(ver) - len(nao_ver))} de {len(modelo_d.get('barras') or ver)}"),
                  ("Reprovadas", f"{len(reprov)}" + (f" ({', '.join(reprov[:12])}{'…' if len(reprov) > 12 else ''})" if reprov else "")),
                  ("Em atenção", f"{len(atenc)}" + (f" ({', '.join(atenc[:12])}{'…' if len(atenc) > 12 else ''})" if atenc else ""))]
        if nao_ver:
            pares.append(("Não verificadas", f"{len(nao_ver)} ({', '.join(nao_ver[:12])}{'…' if len(nao_ver) > 12 else ''}): "
                                             "dados insuficientes; veja o motivo na tabela de verificação"))
    else:
        pares.append(("Verificação", "nenhuma barra verificada"))
    if res.get("tempo_s") is not None:
        pares.append(("Tempo de cálculo", f"{_f(res['tempo_s'], 2)} s"))
    B += [("h2", "Resumo"), ("dl", pares)]

    # 2. Normas
    linhas = []
    for n in normas:
        lim = n.get("limitacao") or n.get("limitacoes") or "—"
        linhas.append(([str(n.get("norma", "")), str(n.get("edicao", "")),
                        "; ".join(lim) if isinstance(lim, (list, tuple)) else str(lim)], ""))
    B += [("h2", "Normas e edições"),  # sem o nome do módulo (interno): o que vai para a ART é norma, edição e limitação
          _tab([("Norma", "", "t"), ("Edição", "", "t"), ("Limitações declaradas", "", "t")],
               linhas, "Seção não aplicável: o cálculo não registrou normas."),
          ("p", "Fórmulas e itens conforme as edições acima; o texto das normas não é reproduzido, apenas o item citado.")]

    # 3. Dados de entrada
    B += [("h2", "Dados de entrada"), ("h3", "Materiais")]
    mats = [([m.get("nome", k), _t(m.get("E") / 1e6 if _num(m.get("E")) else None, 1),
              _t(m.get("G") / 1e6 if _num(m.get("G")) else None, 1), _t(m.get("fy") / 1e3 if _num(m.get("fy")) else None, 1),
              _t(m.get("fu") / 1e3 if _num(m.get("fu")) else None, 1), _t(m.get("fck") / 1e3 if _num(m.get("fck")) else None, 1),
              _t(m.get("peso"), 2)], "") for k, m in (modelo_d.get("materiais") or {}).items()]
    B.append(_tab([("Material", "", "t"), ("E", "GPa", "n"), ("G", "GPa", "n"), ("fy", "MPa", "n"), ("fu", "MPa", "n"),
                   ("fck", "MPa", "n"), ("Peso específico", "kN/m³", "n")], mats, "Nenhum material cadastrado."))
    B.append(("h3", "Seções"))
    secs = [([_perfil(s.get("nome", k))] + [_f(s[c] * u, 2) if _num(s.get(c)) else "—" for c, u in
                                   (("A", 1e4), ("Iy", 1e8), ("Iz", 1e8), ("J", 1e8))], "")
            for k, s in (modelo_d.get("secoes") or {}).items()]
    B.append(_tab([("Seção", "", "t"), ("A", "cm²", "n"), ("Iy", "cm⁴", "n"), ("Iz", "cm⁴", "n"), ("J", "cm⁴", "n")],
                  secs, "Nenhuma seção cadastrada."))
    B.append(("h3", "Nós e barras"))
    nos = modelo_d.get("nos") or {}
    pares = [("Nós", f"{len(nos)}, dos quais {sum(1 for n in nos.values() if any(n.get('apoio') or []))} com apoio")]
    if nos:
        mn = [min(n.get(c, 0.0) for n in nos.values()) for c in "xyz"]
        mx = [max(n.get(c, 0.0) for n in nos.values()) for c in "xyz"]
        pares.append(("Dimensões do modelo (m)", " × ".join(f"{_f(b - a, 3)}" for a, b in zip(mn, mx)) + " (X × Y × Z)"))
    B.append(("dl", pares))
    grupos = {}
    for b in (modelo_d.get("barras") or {}).values():
        k = (_perfil(b.get("secao", "—")), b.get("material", "—"), _TIPO_BARRA.get(b.get("tipo", "aco"), b.get("tipo", "")))
        grupos[k] = grupos.get(k, 0) + 1
    B.append(_tab([("Seção", "", "t"), ("Material", "", "t"), ("Tipo", "", "t"), ("Barras", "", "n")],
                  [(list(k) + [str(n)], "") for k, n in grupos.items()], "Nenhuma barra cadastrada."))
    B.append(("h3", "Pavimentos"))
    pav = [([p.get("nome", k), _f(p.get("z"), 3), _t(p.get("diafragma"))], "") for k, p in (modelo_d.get("pavimentos") or {}).items()]
    B.append(_tab([("Pavimento", "", "t"), ("Cota z", "m", "n"), ("Diafragma rígido", "", "t")], pav,
                  "Sem pavimentos definidos (seção não aplicável)."))

    B.append(("h3", "Casos de carga"))
    contagem = {}
    for lista, col in (("cargas_nodais", 0), ("cargas_distribuidas", 1), ("cargas_pontuais", 2)):
        for c in modelo_d.get(lista) or []:
            contagem.setdefault(c.get("caso"), [0, 0, 0])[col] += 1
    pp = modelo_d.get("peso_proprio")
    casos = [([c.get("nome", k), c.get("tipo", ""), c.get("subtipo") or "—"] + [str(x) for x in contagem.get(k, [0, 0, 0])]
              + ["peso próprio" if k == pp else ""], "") for k, c in (modelo_d.get("casos") or {}).items()]
    B.append(_tab([("Caso", "", "t"), ("Tipo", "", "t"), ("Subtipo", "", "t"), ("Nodais", "", "n"),
                   ("Distribuídas", "", "n"), ("Pontuais", "", "n"), ("Observação", "", "m")], casos, "Nenhum caso de carga cadastrado."))
    cargas, n_auto = [], 0
    for lista, tipo, vet, rot, un in (("cargas_nodais", "nodal", "F", _F6, ("kN",) * 3 + ("kN·m",) * 3),
                                      ("cargas_distribuidas", "distribuída", "q", ("qx", "qy", "qz"), ("kN/m",) * 3),
                                      ("cargas_pontuais", "pontual", "F", ("Fx", "Fy", "Fz"), ("kN",) * 3)):
        for c in modelo_d.get(lista) or []:
            if str(c.get("id", "")).startswith("auto_"):
                n_auto += 1
                continue
            q2 = (c.get("q2") or []) if vet == "q" else []
            val = [f"{r} = {_f(x, 2)}" + (f" → {_f(q2[k], 2)}" if q2 else "") + f" {u}"
                   for k, (r, x, u) in enumerate(zip(rot, c.get(vet) or [], un))
                   if _num(x) and (x != 0 or (q2 and q2[k] != 0))]
            if tipo == "pontual":
                val.insert(0, f"a = {_f(c.get('a'), 3)} m")
            cargas.append(([str(c.get("id", "")), str(c.get("caso", "")), tipo, str(c.get("no") or c.get("barra") or "—"),
                            "; ".join(val) or "—"], ""))
    B.append(("h3", "Cargas"))
    B.append(_tab([("Id", "", "m"), ("Caso", "", "t"), ("Tipo", "", "t"), ("Nó ou barra", "", "t"), ("Valores (eixos globais)", "", "t")],
                  cargas[:_MAX_CARGAS], "Nenhuma carga explícita cadastrada."))
    if len(cargas) > _MAX_CARGAS:
        B.append(("p", f"Mostradas {_MAX_CARGAS} de {len(cargas)} cargas; as demais constam do arquivo do modelo."))
    if n_auto:
        B.append(("p", f"{n_auto} {'carga gerada' if n_auto == 1 else 'cargas geradas'} automaticamente a partir das "
                       "configurações de vento e sismo (ids auto_*), não listada" + ("." if n_auto == 1 else "s.")))

    B.append(("h3", "Configuração de cálculo"))
    pares = [("Tipo de uso", _t(cfg.get("tipo_uso"))), ("Variabilidade das ações", _t(cfg.get("variabilidade"))),
             ("Efeito P-Δ", _t(opc.get("pdelta", cfg.get("pdelta")))), ("Peso próprio no caso", _t(pp)),
             ("Armadura (concreto)", _t(cfg.get("armadura"))),
             ("Cobrimento (concreto)", f"{_f(cfg['cobrimento_m'], 3)} m" if _num(cfg.get("cobrimento_m")) else "—"),
             ("Classe de agressividade", _t(cfg.get("caa"))),
             ("Vento", _t(cfg["vento"]) if cfg.get("vento") else "não considerado"),
             ("Sismo", _t(cfg["sismo"]) if cfg.get("sismo") else "não considerado")]
    B.append(("dl", pares))

    # 4. Combinações
    ordem = list((modelo_d.get("casos") or {}).keys())
    usados = []
    for c in comb:
        for k in c.get("fatores") or {}:
            if k not in usados:
                usados.append(k)
    usados.sort(key=lambda k: (ordem.index(k) if k in ordem else len(ordem), k))
    maj = any(_num(c.get("majoracao_horizontal")) for c in comb)  # 2ª ordem global sem P-Δ (γz/B2)
    lin = [([c.get("nome", ""), _ESTADO_COMB.get(c.get("estado"), str(c.get("estado", "")))]
            + ([_f(c.get("majoracao_horizontal"), 3) if _num(c.get("majoracao_horizontal")) else "—"] if maj else [])
            + [_f(c["fatores"][k], 2) if k in (c.get("fatores") or {}) else "—" for k in usados], "") for c in comb]
    B += [("h2", "Combinações de ações"),
          _tab([("Combinação", "", "t"), ("Estado", "", "m")] + ([("Maj. horizontal", "", "n")] if maj else [])
               + [(k, "", "n") for k in usados], lin, "Seção não aplicável: nenhuma combinação registrada."),
          ("p", "Fatores já incluem os coeficientes de ponderação e os fatores de combinação (NBR 8681); \"—\" = caso ausente da combinação.")]
    if maj:
        B.append(("p", "Maj. horizontal: fator que multiplica as ações horizontais (vento, sismo) da combinação para considerar os "
                       "efeitos globais de 2ª ordem sem P-Δ: 0,95·γz no concreto (NBR 6118 15.7.2), B2 no aço (NBR 8800 4.9.4). "
                       "Os demais fatores da tabela são os nominais."))

    # 5. Resultados
    B += [("h2", "Resultados"), ("h3", "Deslocamentos verticais (flechas)")]
    cabdesl = lambda a, b, c, d: [(a, "", "t"), (b, "", "t"), (c, "mm", "n"), (d, "mm", "n"), ("Razão", "", "t"),
                                  ("Taxa", "", "x"), ("", "0 a 1,20", "g"), ("Estado", "", "t"), ("Combinação", "", "t"), ("Item", "", "m")]
    vert = []
    for d in _ordem(desl.get("verticais") or []):
        c, cl = _linha_taxa([d.get("barra", ""), str(d.get("elemento", "")).replace("_", " "), _f((d.get("flecha_m") or 0) * 1e3, 1),
                             _f((d.get("limite_m") or 0) * 1e3, 1), str(d.get("razao", ""))], d.get("taxa"), d.get("ok"))
        vert.append((c + [str(d.get("combinacao", "")), str(d.get("item", ""))], cl))
    B.append(_tab(cabdesl("Barra", "Elemento", "Flecha", "Limite"), vert, "Seção não aplicável: sem flechas verificadas."))
    B.append(("h3", "Deslocamentos horizontais"))
    hor = []
    for d in _ordem(desl.get("horizontais") or []):
        c, cl = _linha_taxa([d.get("pavimento", ""), f"{d.get('direcao', '')} ({str(d.get('tipo', '')).replace('_', ' ')})",
                             _f((d.get("valor_m") or 0) * 1e3, 1), _f((d.get("limite_m") or 0) * 1e3, 1), str(d.get("razao", ""))],
                            d.get("taxa"), d.get("ok"))
        hor.append((c + [str(d.get("combinacao", "")), str(d.get("item", ""))], cl))
    B.append(_tab(cabdesl("Pavimento", "Direção", "Deslocamento", "Limite"), hor,
                  "Seção não aplicável: sem pavimentos ou sem ações horizontais (deslocamentos horizontais não verificados)."))

    sis_cfg = cfg.get("sismo") if isinstance(cfg.get("sismo"), dict) else None
    if desl.get("sismicos") or (sis_cfg and _num(sis_cfg.get("zona")) and sis_cfg["zona"] >= 2):
        B.append(("h3", "Deslocamentos relativos do sismo (NBR 15421)"))
        sis = []
        for d in _ordem(desl.get("sismicos") or []):
            c, cl = _linha_taxa([d.get("pavimento", ""), f"{d.get('caso', '')} ({d.get('direcao', '')})",
                                 _f((d.get("valor_m") or 0) * 1e3, 1), _f((d.get("limite_m") or 0) * 1e3, 1), str(d.get("razao", ""))],
                                d.get("taxa"), d.get("ok"))
            sis.append((c + [str(d.get("combinacao", "")), str(d.get("item", ""))], cl))
        B.append(_tab(cabdesl("Pavimento", "Caso", "Δ = Cd·δxe/I", "Limite"), sis,
                      "Não verificados: informe o sistema resistente (tabela 6: R, Cd, Ω0) em config.sismo."))
    B.append(("h3", "Reações de apoio (envoltória)"))
    B.append(("p", "Envoltória das combinações últimas (ELU e ELU excepcionais); as combinações de serviço não entram."))
    rea = []
    for no, r in sorted((calculo_d.get("reacoes_envoltoria") or {}).items(), key=lambda kv: _nat(kv[0])):
        for i, rot in enumerate(_F6):
            mx_, mn_ = (r.get("max") or [None] * 6)[i], (r.get("min") or [None] * 6)[i]
            if all(not _num(x) or abs(x) < 1e-9 for x in (mx_, mn_)):
                continue
            cm, cn = (r.get("comb_max") or [""] * 6)[i], (r.get("comb_min") or [""] * 6)[i]
            rea.append(([no, f"{rot} ({'kN' if i < 3 else 'kN·m'})", _f(mx_, 2), str(cm), _f(mn_, 2), str(cn)], ""))
    B.append(_tab([("Nó", "", "t"), ("Componente", "", "t"), ("Máximo", "", "n"), ("Combinação", "", "m"), ("Mínimo", "", "n"),
                   ("Combinação", "", "m")], rea, "Seção não aplicável: sem reações de apoio registradas."))

    B.append(("h3", "Estabilidade global (γz e B2)"))
    gz_l = [([d, _f(x.get("valor"), 3), _CLASSIF.get(x.get("classificacao"), str(x.get("classificacao", ""))),
              str(x.get("combinacao", "")), str(x.get("item", ""))], "") for d, x in gz.items()]
    b2_l = [([d, _f(x.get("max"), 3), str(x.get("pavimento", "")), _CLASSIF.get(x.get("classificacao"), str(x.get("classificacao", ""))),
              str(x.get("item", ""))], "") for d, x in b2.items()]
    if gz_l:
        B.append(_tab([("Direção", "", "t"), ("γz", "", "n"), ("Classificação", "", "t"), ("Combinação", "", "m"), ("Item", "", "m")], gz_l, ""))
        rig = next((x["rigidez"] for x in gz.values() if x.get("rigidez")), None)
        if rig:
            B.append(("p", f"γz com deslocamentos de 1ª ordem calculados com a rigidez reduzida: {rig}."))
    if b2_l:
        B.append(_tab([("Direção", "", "t"), ("B2 máximo", "", "n"), ("Pavimento", "", "t"), ("Classificação", "", "t"), ("Item", "", "m")], b2_l, ""))
    if not (gz_l or b2_l):
        B.append(("p", "Seção não aplicável: " + str(est.get("motivo") or "sem pavimentos e ações horizontais, a estabilidade global não foi calculada.")))
    mj, por = est.get("majoracao") or {}, est.get("por_combinacao") or {}
    if mj:
        gzb = mj.get("base") == "gama_z"
        B.append(("p", f"Ações horizontais majoradas por {'0,95·γz (NBR 6118 15.7.2)' if gzb else 'B2 (NBR 8800 4.9.4, MAES simplificado)'} "
                       f"em {mj.get('n_majoradas', 0)} combinações últimas"
                       + (f" (fator máximo {_f(mj.get('fator_max'), 3)})" if _num(mj.get("fator_max")) else "")
                       + (f"; {mj['n_excedem']} excedem o limite ({'γz > 1,3' if gzb else 'B2 > 1,4'}) e não foram majoradas: "
                          "é necessária análise de 2ª ordem rigorosa (P-Δ)." if mj.get("n_excedem") else ".")))
        itens = sorted(por.items(), key=lambda kv: (kv[1].get("situacao") != "excede", -(kv[1].get("fator") or 0.0)))
        linhas_mj = [([n, v.get("direcao", ""), _f(v.get("gama_z" if gzb else "B2"), 3),
                       "majorada" if v.get("situacao") == "majorada" else "excede o limite", _f(v.get("fator"), 3)],
                      "reprovada" if v.get("situacao") == "excede" else "") for n, v in itens[:_MAX_MAJ]]
        B.append(_tab([("Combinação", "", "t"), ("Direção", "", "t"), ("γz" if gzb else "B2", "", "n"), ("Situação", "", "t"),
                       ("Fator aplicado", "", "n")], linhas_mj, ""))
        if len(itens) > _MAX_MAJ:
            B.append(("p", f"Mostradas {_MAX_MAJ} de {len(itens)} combinações."))
    for a in est.get("avisos") or []:
        B.append(("p", "Aviso: " + (a.get("mensagem", "") if isinstance(a, dict) else str(a))))

    # 6. Verificação por barra
    sec_de = lambda b: _perfil(((modelo_d.get("barras") or {}).get(b) or {}).get("secao", "—"))
    lin = []
    for v in ver:
        c, cl = _linha_taxa([v.get("barra", ""), str(sec_de(v.get("barra")))], v.get("taxa"), v.get("ok"))
        lin.append((c + [str(v.get("criterio", "")).replace("_", " "), f"{v.get('norma', '')} {v.get('item', '')}".strip(),
                         str(v.get("combinacao", "")), _f(v.get("x_m"), 3)], cl))
    B += [("h2", "Verificação por barra"),
          ("p", "Barras reprovadas primeiro, depois em ordem decrescente de taxa de aproveitamento. A combinação e a posição x "
                "(a partir do nó inicial) são as determinantes."),
          _tab([("Barra", "", "t"), ("Seção", "", "t"), ("Taxa", "", "x"), ("", "0 a 1,20", "g"), ("Estado", "", "t"),
                ("Critério", "", "t"), ("Norma e item", "", "m"), ("Combinação", "", "t"), ("x", "m", "n")],
               lin, "Seção não aplicável: nenhuma barra verificada.")]
    arm = [v for v in ver if v.get("armadura")]
    if arm:
        cols = [(r, u, "n") for k, r, u in _ARMADURA if any(_num(v["armadura"].get(k)) for v in arm)]
        chaves = [k for k, r, u in _ARMADURA if any(_num(v["armadura"].get(k)) for v in arm)]
        B += [("h3", "Armadura das barras de concreto"),
              _tab([("Barra", "", "t"), ("Estado", "", "t")] + cols + [("Barras", "", "t")],
                   [([v["barra"], ("estado", _estado(v.get("ok"), v.get("taxa")))] + [_f(v["armadura"].get(k), 2) for k in chaves]
                     + [str(v["armadura"].get("barras") or "—")], "reprovada" if v.get("ok") is False else "") for v in arm], "")]

    # 7. Avisos e limitações
    avisos = calculo_d.get("avisos") or []
    def ref(a):
        bs = [str(x) for x in a.get("barras") or []]
        if len(bs) > 1:
            return "barras " + ", ".join(bs[:6]) + (f" e mais {len(bs) - 6}" if len(bs) > 6 else "")
        return f"{a['ref'].get('tipo', '')} {a['ref'].get('id', '')}".strip() if isinstance(a.get("ref"), dict) else "—"
    B += [("h2", "Avisos e limitações"),  # sem o código interno do aviso: o texto é o que vai para a ART
          _tab([("Nível", "", "t"), ("Mensagem", "", "t"), ("Referência", "", "m")],
               [([str(a.get("nivel", "")), str(a.get("mensagem", "")), ref(a)], "") for a in avisos if isinstance(a, dict)],
               "Nenhum aviso registrado pelo cálculo.")]
    if sum(1 for n in normas if n.get("limitacao") or n.get("limitacoes")):
        B.append(("p", "As limitações declaradas de cada norma constam da seção 2."))

    d_curta, d_longa = _data(calculo_d.get("data"))
    meta = dict(nome=nome, versao=str(calculo_d.get("versao", "")), data=d_curta, data_longa=d_longa,
                normas=", ".join(dict.fromkeys(f"{n.get('norma', '')}:{n.get('edicao', '')}" for n in normas)) or "—")
    return meta, B


# ----------------------------------------------------------------------------- HTML
def _ler(rel):
    try:
        return (_EST / rel).read_text(encoding="utf-8")
    except OSError:
        return ""


def _h(s):
    return _esc(str(s))


def _cel_html(c):
    if isinstance(c, tuple):
        if c[0] == "estado":
            return f'<span class="estado {c[1]}">{_ESTADO[c[1]]}</span>'
        taxa = c[1]  # "med"
        return f'<span class="medidor"><i class="t{_faixa(taxa, c[2])}" style="width:{min(taxa / 1.2, 1.0) * 100:.1f}%"></i></span>'
    return _h(c)


def _inline_html(v):
    if isinstance(v, list):
        return "".join(f'<span class="estado {x[1]}">{_ESTADO[x[1]]}</span>' if isinstance(x, tuple) else _h(x) for x in v)
    return _h(v)


def _attr(valor):
    return f' class="{valor}"' if valor else ""


def _tab_html(cols, rows):
    cls = {"n": "num", "x": "num taxa", "m": "mudo"}
    th = "".join(f'<th{_attr("num" if k in "nx" else "")}>{_h(r)}' + (f'<span class="un">{_h(u)}</span>' if u else "") + "</th>"
                 for r, u, k in cols)
    out = [f"<table><thead><tr>{th}</tr></thead><tbody>"]
    for cells, rc in rows:
        tds = "".join(f"<td{_attr(cls.get(k))}>{_cel_html(c)}</td>" for c, (_, _, k) in zip(cells, cols))
        out.append(f"<tr{_attr(rc)}>{tds}</tr>")
    out.append("</tbody></table>")
    return "".join(out)


def html(modelo_d, calculo_d):
    """Documento HTML autônomo (A4 retrato, sem JS): relatorio.css e logotipo embutidos."""
    meta, blocos = _montar(modelo_d, calculo_d)
    # relatorio.css traz url("fontes/…"), que não existe ao lado do HTML salvo: tira as @font-face dele e embute as
    # fontes em data URI (fontes/relatorio-fontes.css) logo depois; a última @font-face da família vence
    css = re.sub(r"@font-face\s*\{[^}]*\}", "", _ler("relatorio.css")) + "\n" + _ler("fontes/relatorio-fontes.css")
    n2 = n3 = 0
    corpo = []
    for b in blocos:
        if b[0] == "h2":
            n2 += 1
            n3 = 0
            corpo.append(f'<h2><span class="n">{n2}</span>{_h(b[1])}</h2>')
        elif b[0] == "h3":
            n3 += 1
            corpo.append(f"<h3>{n2}.{n3} {_h(b[1])}</h3>")
        elif b[0] == "p":
            corpo.append(f"<p>{_h(b[1])}</p>")
        elif b[0] == "dl":
            corpo.append('<dl class="dados">' + "".join(f"<dt>{_h(k)}</dt><dd>{_inline_html(v)}</dd>" for k, v in b[1]) + "</dl>")
        else:
            corpo.append(_tab_html(b[1], b[2]))
    leg = lambda rot, val: f'<span class="leg">{_h(rot)}</span>' + (f"<b>{_h(val)}</b>" if val else "")
    carimbo = (f'<footer class="carimbo"><div class="marca">{_ler("icones/marca.svg")}</div>'
               f'<div>{leg("Empresa", "PROSSEN ENGENHARIA")}</div><div>{leg("Projeto", meta["nome"])}</div>'
               f'<div class="sem-d">{leg("Data", meta["data"])}</div>'
               f'<div>{leg("Conteúdo", "Memória de cálculo estrutural")}</div><div>{leg("Normas", meta["normas"])}</div>'
               f'<div class="sem-d">{leg("Revisão", "0")}</div>'
               f'<div class="sem-b assinatura">{leg("Responsável técnico (nome e assinatura)", "")}</div>'
               f'<div class="sem-b">{leg("CREA e nº da ART", "")}</div>'
               f'<div class="sem-b sem-d">{leg("Software", "Estructto " + meta["versao"])}</div></footer>')
    return (f'<!doctype html>\n<html lang="pt-BR">\n<head>\n<meta charset="utf-8">\n'
            f'<meta name="viewport" content="width=device-width, initial-scale=1">\n'
            f'<title>Memória de cálculo — {_h(meta["nome"])}</title>\n<style>\n{css}\n</style>\n</head>\n'
            f'<body class="relatorio">\n<article class="folha">\n<table class="paginas" role="presentation"><tbody><tr><td>\n'
            f'<header class="cab">{_ler("icones/logo.svg")}<div><h1>Memória de cálculo estrutural</h1>'
            f'<div class="proj">{_h(meta["nome"])}</div></div>'
            f'<div class="meta">Calculado em {_h(meta["data_longa"])}<br>Estructto {_h(meta["versao"])}</div></header>\n'
            f'<div class="aviso-art"><b>{_h(AVISO_TITULO)}</b> {_h(AVISO)}</div>\n'
            + "\n".join(corpo) + f'\n</td></tr></tbody><tfoot><tr><td><div class="reserva"></div></td></tr></tfoot></table>\n{carimbo}\n</article>\n</body>\n</html>\n')


# ----------------------------------------------------------------------------- Markdown
def _m(s):
    return re.sub(r"([\\|<>*`])", r"\\\1", str(s)).replace("\n", " ")


def _cel_md(c):
    if isinstance(c, tuple):
        return _ESTADO_MD[c[1]]
    return _m(c)


def markdown(modelo_d, calculo_d):
    """Mesmo conteúdo de `html` em Markdown (tabelas GFM, sem HTML)."""
    meta, blocos = _montar(modelo_d, calculo_d)
    out = ["# Memória de cálculo estrutural", "", f"**Projeto:** {_m(meta['nome'])}  ",
           f"**Calculado em:** {_m(meta['data_longa'])}  ", f"**Software:** Estructto {_m(meta['versao'])}  ",
           "**Responsável técnico / CREA / ART:** ________________", "", f"> **{AVISO_TITULO}** {AVISO}", ""]
    n2 = n3 = 0
    for b in blocos:
        if b[0] == "h2":
            n2 += 1
            n3 = 0
            out += [f"## {n2}. {_m(b[1])}", ""]
        elif b[0] == "h3":
            n3 += 1
            out += [f"### {n2}.{n3} {_m(b[1])}", ""]
        elif b[0] == "p":
            out += [_m(b[1]), ""]
        elif b[0] == "dl":
            out += [f"- **{_m(k)}:** " + ("".join(_ESTADO_MD[x[1]] if isinstance(x, tuple) else _m(x) for x in v)
                                         if isinstance(v, list) else _m(v)) for k, v in b[1]] + [""]
        else:
            cols = [(i, c) for i, c in enumerate(b[1]) if c[2] != "g"]  # medidor só no HTML
            out.append("| " + " | ".join(_m(r) + (f" ({_m(u)})" if u else "") for _, (r, u, _) in cols) + " |")
            out.append("|" + "|".join("---:" if k in "nx" else "---" for _, (_, _, k) in cols) + "|")
            out += ["| " + " | ".join(_cel_md(cells[i]) for i, _ in cols) + " |" for cells, _ in b[2]]
            out.append("")
    return "\n".join(out)
