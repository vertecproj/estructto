"""Comparação de fixações de catálogo; sem ranking por ruptura bruta.

Metadados permanecem textuais. Resultados numéricos em kN/m via placa_base.
"""
import csv
from .placa_base import _DADOS, carregar_chumbadores, achar_chumbador, avaliar_chumbador


def _ler(nome):
    with (_DADOS / nome).open(encoding='utf-8-sig', newline='') as f:
        # O leitor C# usa Split(';'), inclusive nos textos com ponto e vírgula.
        # Colunas extras são ignoradas, exatamente como os índices do legado.
        return [{k: (v or '').strip() for k, v in r.items() if k is not None}
                for r in csv.DictReader(f, delimiter=';', quoting=csv.QUOTE_NONE)]


def carregar_linhas():
    linhas = _ler('fixacoes.csv')
    for l in linhas:
        for k in ('informativa', 'sem_catalogo'):
            l[k] = l[k] == 'sim'
        l['nome_curto'] = l['linha'].replace('Walsywa ', '')
    return linhas


def carregar_pares():
    pares = _ler('fixacoes-pares.csv')
    for p in pares:
        for k in ('linhas_a', 'linhas_b'):
            p[k] = [x.strip() for x in p[k].split('|') if x.strip()]
        nomes = p['titulo'].split('×')
        p['nome_a'], p['nome_b'] = (nomes[0].strip(), nomes[1].strip()) if len(nomes) >= 2 else ('A', 'B')
    return pares


def achar_linha(chave):
    if not chave:
        return None
    s = chave.strip().casefold()
    linhas = carregar_linhas()
    achou = next((l for l in linhas if s in (l['linha'].casefold(), l['nome_curto'].casefold())), None)
    if achou:
        return achou
    c = achar_chumbador(chave.strip())
    return next((l for l in linhas if c and l['linha'] == c['linha']), None)


def informativas():
    return [l for l in carregar_linhas() if l['informativa']]


def _base_solicitacoes(base):
    if 'forcas' in base:
        coef = base['intermediarios']['coeficientes']
        return dict(base['forcas'], espac_min=base['geometria']['espac_min'],
                    NExig=coef['NExig'], NSeg=coef['NSeg'])
    return base


def avaliar_fixacao(chave, base=None, dados=None):
    if not chave:
        return None
    c = chave if isinstance(chave, dict) else achar_chumbador(chave.strip())
    if c is None:
        s = chave.strip().casefold()
        for item in carregar_chumbadores():
            curto = f"{item['linha'].replace('Walsywa ', '')} {item['pol']} hef {item['hef_mm']:.0f}"
            if s in (item['codigo'].casefold(), item['nome'].casefold(), curto.casefold()):
                c = item
                break
    info = achar_linha(c['linha'] if c else chave)
    if info is None:
        return None
    if c is None and not info['informativa']:
        c = next((x for x in carregar_chumbadores() if x['linha'] == info['linha']), None)
    res = None
    if c and base and base.get('tem_esforcos', False):
        res = avaliar_chumbador(c, dados, _base_solicitacoes(base))
    return dict(chumbador=c, info=info, resultado=res,
                titulo=c['codigo'] if c else info['linha'], chave=c['codigo'] if c else info['linha'],
                subtitulo=f"{c['linha']} Ø{c['pol']} hef {c['hef_mm']:g} mm" if c else 'fora do cálculo estrutural')


def pares_para(linhas):
    ls = list(dict.fromkeys(x for x in (linhas or []) if x))
    return [p for p in carregar_pares() if any(
        (a in p['linhas_a'] and b in p['linhas_b']) or (b in p['linhas_a'] and a in p['linhas_b'])
        for i, a in enumerate(ls) for b in ls[i+1:] if a != b)]


def avisos_comparacao(colunas, base=None):
    cols = [c for c in (colunas or []) if c and c['info']]
    if not cols:
        return []
    av = []
    ruptura = any((c['chumbador'] and (c['chumbador']['tu_kgf'] or 0) > 0) or 'ruptura' in c['info']['natureza_carga'] for c in cols)
    rec = any((c['chumbador'] and ((c['chumbador']['tu_kgf'] or 0) <= 0 or (c['chumbador']['tu_rec_kgf'] or 0) > 0)) or 'recomendada' in c['info']['natureza_carga'] for c in cols)
    if ruptura and rec:
        av.append('Ruptura × Recomendada: não comparar diretamente; divida ruptura pelo coeficiente aplicável (3 a 15). WQI/WQE informam ambas.')
    fcks = {f"{c['chumbador']['fck_mpa']:.0f} MPa"
            if c['chumbador'] and (c['chumbador']['fck_mpa'] or 0) > 0
            else c['info']['fck_ensaio'] for c in cols}
    fcks = {f for f in fcks if f and f != 'n\u00e3o aplic\u00e1vel'}
    tem_especial = any((c['chumbador'] and any(
        abs((c['chumbador']['fck_mpa'] or 0)-x) < 1e-6 for x in (28, 30)))
        or c['info']['fck_ensaio'] in ('28', '30') for c in cols)
    if len(fcks) > 1 or tem_especial:
        av.append('Concreto de ensaio: WQI/WQE 28 MPa, Parabolt PB 30 MPa, demais C20; FFck=min(1, raiz(fck laje/fck ensaio)), sem aumento por extrapolação.')
    if any(c['info']['corte_tabelado'] == 'não' for c in cols):
        av.append('Ausência de cisalhamento tabelado no concreto: WQI/WQE verificam corte apenas no aço da haste.')
    if any(c['info']['linha'] == 'Walsywa WQA' for c in cols):
        av.append('WQA usa nRef=1 na recomendada, favorecendo frente a n=15 na ruptura. Reduções não calculadas: −30% espaçamento mínimo; −40% tração/−50% corte na borda mínima; −30% de 100 a 120 °C; −20% tração C15.')
    if any(c['info']['informativa'] for c in cols):
        av.append('Linhas informativas fora do cálculo: WLT sem corte; Golpe de Metal para aplicações leves; PU 500 espuma sem resistência estrutural.')
    if any(c['info']['sem_catalogo'] for c in cols):
        av.append('Indufix Parabolt PB sem PDF/descrição completa no guia; atributos básicos e cálculo da tabela.')
    return av


def comparar_fixacoes(chaves, base=None, dados=None):
    colunas = [avaliar_fixacao(c, base, dados) for c in chaves]
    desconhecidas = [c for c, col in zip(chaves, colunas) if col is None]
    avisos = avisos_comparacao(colunas, base)
    avisos.extend(f'Fixação desconhecida: {c}' for c in desconhecidas)
    return dict(colunas=colunas, pares=pares_para([c['info']['linha'] for c in colunas if c]), avisos=avisos)
