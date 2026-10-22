"""Placa de base: método NBR (v2) e compatibilidade com Nucleo.cs/Solda.cs (legado).

``verificar_placa_base`` usa o método NBR (docs/v3/PLACA-BASE-V2.md): esforços de
CÁLCULO (ELU), sem coeficiente global; concreto NBR 8800 6.6.5; bloco retangular
de compressão; chapa CBCA com conferência Pfeil/DG1; corte atrito → barra →
chumbadores; chumbador pré-concretado por NBR 8800 6.3.3 + ACI 318-19 cap. 17.
O caminho legado (pivô na borda, n global) segue em ``forcas_fixacoes``,
``espessura_placa``, ``verificar_parafuso_placa`` e no campo ``legado``.

API: kN, m, kN.m, m², m⁴ e kN/m²; diâmetro nominal em ``diametro_mm``.
Catálogo conserva mm, MPa, kgf nos campos com sufixos. Ver LIMITACOES.
"""
from __future__ import annotations

import copy
import csv
import math
from itertools import combinations
from pathlib import Path

from .ligacoes import (cisalhamento_parafuso, interacao_tracao_cisalhamento, pressao_contato,
                       tracao_parafuso)
from .nbr8800 import GA1, GA2
from .pontas import pontas_locais

METODO = 'legado simplificado Nucleo.cs / Solda.cs'
METODO_NBR = ('NBR 8800:2008 6.6.5 + bloco retangular (DG1) + chapa CBCA + ACI 318-19 cap. 17; '
              'esforços de cálculo (ELU), sem coeficiente global')
GC = GN = 1.4   # NBR 8800 6.6.5: γc e γn
PHI_ACI = .70   # ACI 318-19 17.5.3: concreto, pré-concretado, condição B
FONTES = ('codigo/portico/src/Nucleo.cs:864–1318', 'codigo/Estructto/Solda.cs:1–159')
COBERTURA = ('placa rígida', 'parafuso métrico', 'chumbadores de catálogo', 'solda de contorno')
LIMITACOES = (
    'Esforços de entrada são de cálculo (ELU); combinações e majoração ficam a cargo do solver.',
    'Momento só no plano H; sem flexão biaxial, corte em duas direções, enrijecedores nem efeito alavanca.',
    'Tração só na fileira mais afastada do lado tracionado; fileiras internas desprezadas.',
    "Chapa: balanços m/n do CBCA, sem λn' (Thornton) nem placa apoiada em três lados.",
    'A2 homotético dentro do bloco centrado na placa; bloco não informado → A2 = A1 e bordas infinitas.',
    'ACI 318-19 cap. 17 simplificado: bloco retangular centrado, corte só na direção do plano, sem armadura de ancoragem.',
    'ACI 318-19 17.8: interação só entre modos de concreto; o aço da haste (17.6.1/17.7.1) é verificado pela NBR 8800 6.3.3.4, fora da 17.8.',
    'ACI 318-19 17.10 (sismo: 0,75·φ, ductilidade da ancoragem, Ω0) não implementado.',
    'Chumbador tracionado entre mesas: faixa biengastada entre as mesas, sem a contribuição da alma (charneiras de Thornton não implementadas).',
    'Barra de cisalhamento: sem cone de corte à frente (ACI 349) e sem solda da barra.',
    'Catálogo pós-instalado: interação 5/3 e correção √fck são hipóteses de projeto; reduções WQA não calculadas.',
    'Solda de contorno pelo modelo legado (tensão admissível); sem 4b/40 mm nem máximo t−1,5 mm.',
    'Barra inclinada fora do modelo de chapa e solda.',
)
DIAMETROS_MM = (8, 10, 12, 14, 16, 20, 24, 30, 36)
AT_MM2 = (36.6, 58, 84.3, 115, 157, 245, 353, 561, 817)
AR_MM2 = (32.8, 52.3, 76.3, 104, 144, 225, 324, 519, 759)
CLASSES = ('4.6', '4.8', '5.8', '8.8', '9.8', '10.9')
SP_MPA = (225, 310, 380, 600, 650, 830)
CHAPAS = tuple(x / 1000 for x in (6.35, 8, 9.5, 12.7, 16, 19, 22.4, 25.4, 31.5, 38.1, 50.8))
PERNAS = tuple(x / 1000 for x in (2, 3, 5, 6, 8, 10, 11, 12, 16, 20, 22, 25))
ELETRODOS = dict(zip(('E60', 'E70', 'E80', 'E90', 'E100', 'E110', 'E120'), (124, 145, 165, 186, 207, 228, 248)))
_DADOS = Path(__file__).resolve().parents[1] / 'dados'


def _numeros(valores, positivos=(), nao_negativos=(), inteiros=()):
    """Validação agregada, inclusive dos esforços assinados N/M."""
    erros, out = [], {}
    for k, v in valores.items():
        try:
            x = float(v)
        except (TypeError, ValueError):
            erros.append(f'{k}: número esperado')
            continue
        if not math.isfinite(x):
            erros.append(f'{k}: número finito esperado')
        elif k in positivos and x <= 0:
            erros.append(f'{k}: deve ser positivo')
        elif k in nao_negativos and x < 0:
            erros.append(f'{k}: deve ser não negativo')
        elif k in inteiros and x != int(x):
            erros.append(f'{k}: inteiro esperado')
        else:
            out[k] = int(x) if k in inteiros else x
    if erros:
        raise ValueError('; '.join(erros))
    return out


def _secao(secao):
    if secao is None:
        raise ValueError('seção: medidas obrigatórias')
    s = secao.props if hasattr(secao, 'props') else secao
    s = dict(s)
    s.setdefault('b', s.get('h', 0))
    if s['b'] <= 0:
        s['b'] = s.get('h', 0)
    s.setdefault('tw', s.get('t', 0))
    s.setdefault('tf', s.get('tw', 0))
    s.setdefault('tipo', '')
    s.update(_numeros({k: s.get(k, 0) for k in ('h', 'b', 'tw', 'tf')}, positivos=('h', 'b'), nao_negativos=('tw', 'tf')))
    return s


def esquema_placa_base():
    """Campos de entrada; dimensões em m, resistências em kN/m²."""
    campos = [
        ('H', 'm', 'dimensão no plano', .4), ('B', 'm', 'dimensão transversal', .3),
        ('esp', 'm', 'espessura adotada', .019), ('borda', 'm', 'centro do furo à borda da chapa', .05),
        ('sobra', 'm', 'sobra de cada lado do perfil', .1), ('pelo_perfil', '', 'gerar dimensões', False),
        ('fileiras', '', 'fileiras de contorno', 2), ('por_fileira', '', 'fixações por fileira', 2),
        ('diametro_mm', 'mm', 'diâmetro nominal métrico', 16), ('classe', '', 'classe métrica', '8.8'),
        ('rosca_no_corte', '', 'rosca no plano de corte', True), ('pivo_na_mesa', '', 'giro na mesa', False),
        ('fy_chapa', 'kN/m²', 'escoamento da chapa; ≤0 usa A36', 250000),
        ('fck', 'kN/m²', 'concreto da laje; ≤0 usa C20', 20000),
        ('seguranca', '', 'segurança da placa; ≤0 usa segurança do modelo', 0),
        ('seguranca_modelo', '', 'segurança do modelo', 2),
        ('chumbador', '', 'código ou nome exato; vazio usa parafuso', ''),
        ('classe_barra', '', 'classe independente da haste química', '4.6'),
        ('tipo_carga', '', 'tipo; vazio/inválido usa automático', ''),
        ('risco', '', 'risco; vazio/inválido usa automático', ''),
        ('rotativa', '', 'presença de equipamento rotativo', False),
        ('coef_chumbador', '', 'coeficiente de ruptura; ≤0 usa tabela', 0),
        ('solda', 'm', 'perna; zero dimensiona explicitamente', 0),
        ('eletrodo', '', 'eletrodo; vazio/desconhecido usa E70', 'E70'),
        ('eixo_fraco', '', 'orientação do perfil', False),
        # ---- método NBR (v2); esforços de cálculo
        ('fu_chapa', 'kN/m²', 'ruptura da chapa; ≤0 usa A36', 400000),
        ('bloco_H', 'm', 'bloco de concreto na direção H; 0 = não informado (A2 = A1, sem bordas)', 0),
        ('bloco_B', 'm', 'bloco de concreto na direção B; 0 = não informado', 0),
        ('bloco_altura', 'm', 'altura do bloco (ψh,V do ACI); 0 = não informada', 0),
        ('hef', 'm', 'embutimento efetivo do chumbador pré-concretado; 0 = 12·Ø', 0),
        ('abrg', 'm²', 'área de apoio da cabeça/porca; 0 = porca pesada 1,163·Ø²', 0),
        ('fissurado', '', 'concreto fissurado (ACI ψc)', True),
        ('fub_chumbador', 'kN/m²', 'ruptura do chumbador; ≤0 usa a classe', 0),
        ('fyb_chumbador', 'kN/m²', 'escoamento do chumbador; ≤0 usa a classe', 0),
        ('atrito', '', 'coeficiente de cálculo do atrito sobre Nd de compressão (CBCA 0,10)', .10),
        ('lug_b', 'm', 'barra de cisalhamento: largura; 0 = sem barra', 0),
        ('lug_t', 'm', 'barra de cisalhamento: espessura', 0),
        ('lug_h', 'm', 'barra de cisalhamento: altura embutida no concreto', 0),
        ('graute', 'm', 'espessura do graute', 0),
        ('arruela_soldada', '', 'arruela soldada: todos os chumbadores trabalham ao corte', False),
    ]
    out = [dict(nome=n, unidade=u, descricao=t, padrao=p) for n, u, t, p in campos]
    for c in out:
        if c['nome'] in ('classe', 'classe_barra'):
            c['opcoes'] = list(CLASSES)
        elif c['nome'] == 'eletrodo':
            c['opcoes'] = list(ELETRODOS)
        elif c['nome'] == 'tipo_carga':
            c['opcoes'] = ['', 'estatica', 'variavel', 'dinamica', 'choque']
        elif c['nome'] == 'risco':
            c['opcoes'] = ['', 'baixo', 'alto']
    return out


def _dados(dados):
    d = {c['nome']: c['padrao'] for c in esquema_placa_base()}
    d.update(dados or {})
    nomes = ('H', 'B', 'esp', 'borda', 'sobra', 'fileiras', 'por_fileira', 'diametro_mm',
             'fy_chapa', 'fck', 'seguranca', 'seguranca_modelo', 'coef_chumbador', 'solda',
             'fu_chapa', 'bloco_H', 'bloco_B', 'bloco_altura', 'hef', 'abrg', 'fub_chumbador', 'fyb_chumbador',
             'atrito', 'lug_b', 'lug_t', 'lug_h', 'graute')
    d.update(_numeros({k: d[k] for k in nomes}, positivos=('seguranca_modelo', 'diametro_mm'),
                     nao_negativos=('esp', 'borda', 'sobra', 'solda', 'bloco_H', 'bloco_B', 'bloco_altura', 'hef',
                                    'abrg', 'atrito', 'lug_b', 'lug_t', 'lug_h', 'graute'),
                     inteiros=('fileiras', 'por_fileira')))
    return d


def geometria_placa(dados, secao):
    d, s = _dados(dados), _secao(secao)
    ri, ro = (s['b'], s['h']) if d['eixo_fraco'] else (s['h'], s['b'])
    H, B = d['H'], d['B']
    if d['pelo_perfil']:
        H = math.ceil(round((ri + 2 * d['sobra']) * 100, 12)) / 100
        B = math.ceil(round((ro + 2 * d['sobra']) * 100, 12)) / 100
    erros = [f'{k}: dimensão deve ser positiva e maior que duas bordas' for k, x in (('H', H), ('B', B))
             if x <= 0 or 2 * d['borda'] >= x]
    if erros:
        raise ValueError('; '.join(erros))
    nf, nq = max(1, d['fileiras']), max(1, d['por_fileira'])
    coords = [(0 if nf == 1 else -H/2 + d['borda'] + i*(H-2*d['borda'])/(nf-1),
               0 if nq == 1 else -B/2 + d['borda'] + j*(B-2*d['borda'])/(nq-1))
              for i in range(nf) for j in range(nq) if not (0 < i < nf-1 and 0 < j < nq-1)]
    espac = min((math.dist(p, q) for i, p in enumerate(coords) for q in coords[i+1:]), default=0)
    return dict(H=H, B=B, ref_in=ri, ref_out=ro, coordenadas=coords, espac_min=espac)


def esforcos_placa(extremidade, direcao_barra):
    """F local 2D [N1,Q1,M1,N2,Q2,M2], kN/kN.m; direção sai do nó.

    extremidade: F, C, S, n2 (bool), e opcionais N_barra/V_barra/M_barra,
    cargas_no_no. C/S orientam N1→N2; direção sai do nó avaliado inclusive N2.
    """
    ex, ey = direcao_barra
    a = _numeros(dict(ex=ex, ey=ey))
    ex, ey = a['ex'], a['ey']
    lb = math.hypot(ex, ey)
    if lb <= 0:
        raise ValueError('direcao_barra: comprimento nulo')
    nx, ny = (0, 1 if ey >= 0 else -1) if abs(ey) >= abs(ex) else (1 if ex >= 0 else -1, 0)
    yx, yy = -ny, nx
    ang = math.degrees(math.acos(min(1, abs(ex*nx + ey*ny)/lb)))
    e = dict(extremidade)
    f = e.get('F')
    if f is None or len(f) != 6:
        raise ValueError('F: seis forças de extremidade obrigatórias')
    f = list(_numeros({str(i): v for i, v in enumerate(f)}).values())
    sentido = -1 if e.get('n2', False) else 1
    cs = _numeros(dict(C=e.get('C', sentido*ex/lb), S=e.get('S', sentido*ey/lb)))
    k = 3 if e.get('n2', False) else 0
    rx, ry = cs['C']*f[k]-cs['S']*f[k+1], cs['S']*f[k]+cs['C']*f[k+1]
    avisos = []
    if ang > 2:
        avisos.append('Barra inclinada: chapa e solda fora do modelo.')
    if e.get('cargas_no_no'):
        avisos.append('Cargas no próprio nó seguem diretamente ao apoio; não entram nas forças da barra.')
    return dict(N=-(rx*nx+ry*ny), V=abs(rx*yx+ry*yy), M=-f[k+2], Rx=rx, Ry=ry, Rm=f[k+2],
                normal=(nx, ny), ang_normal=ang, inclinada=ang > 2,
                barra_y=(ex*yx+ey*yy)/lb, barra_n=(ex*nx+ey*ny)/lb,
                espelhar=yx < -1e-9 or (abs(yx) <= 1e-9 and yy < 0),
                N_barra=e.get('N_barra'), V_barra=e.get('V_barra'), M_barra=e.get('M_barra'), avisos=avisos)


def forcas_fixacoes(N, V, M, coordenadas, H, ref_in, pivo_na_mesa=False):
    vals = _numeros(dict(N=N, V=V, M=M, H=H, ref_in=ref_in), positivos=('H', 'ref_in'), nao_negativos=('V',))
    N, V, M, H, ref_in = (vals[k] for k in ('N', 'V', 'M', 'H', 'ref_in'))
    pontos = [tuple(_numeros(dict(y=p[0], z=p[1])).values()) for p in coordenadas]
    if not pontos:
        raise ValueError('coordenadas: pelo menos uma fixação')
    sinal = 1 if M >= 0 else -1
    u = [-sinal*p[0] for p in pontos]
    s2 = sum(x*x for x in u)
    t = [N/len(u) + (abs(M)*x/s2 if s2 > 0 else 0) for x in u]
    pivo, modelo = None, 'Todos tracionados'
    a, k = None, None
    if min(t) < -1e-9:
        a = (ref_in if pivo_na_mesa else H)/2
        ds = [max(0, x+a) for x in u]
        den = sum(x*x for x in ds)
        k = (abs(M)+N*a)/den if den > 0 else 0
        t = [k*x if k > 0 else 0 for x in ds]
        pivo = sinal*a
        modelo = 'Giro com contato' if k > 0 else 'Sem tração'
    paraf = [(y, z, max(0, ti)) for (y, z), ti in zip(pontos, t)]
    return dict(N=N, V=V, M=M, paraf=paraf, Tmax=max(p[2] for p in paraf), Fv=V/len(u),
                modelo=modelo, pivo_y=pivo, intermediarios=dict(u=u, S2=s2, a=a, k=k))


def _seg(num, den, lim=1e-9):
    return num/den if den > lim else math.inf


def _v(nome, sd, rd, unidade='', item=METODO):
    taxa = sd/rd if rd > 0 else (0 if sd == 0 else math.inf)
    return dict(nome=nome, solicitante=sd, resistente=rd, unidade=unidade, taxa=taxa, item=item)


def _resultado(v, avisos=()):
    resistentes = [x for x in v if x['unidade'] != 'm' or x.get('criterio_resistente', False)]
    pior = max(resistentes, key=lambda x: x['taxa'], default=dict(taxa=0, nome='', item=''))
    return dict(taxa_max=pior['taxa'], criterio=pior['nome'], item=pior['item'],
                ok=all(x['taxa'] <= 1 for x in v), verificacoes=v, avisos=list(avisos),
                metodo=METODO, fontes=list(FONTES))


def verificar_parafuso_placa(Tmax, Fv, diametro_mm=16, classe='8.8', rosca_no_corte=True, seguranca=2):
    a = _numeros(dict(Tmax=Tmax, Fv=Fv, diametro_mm=diametro_mm, seguranca=seguranca),
                  positivos=('diametro_mm', 'seguranca'), nao_negativos=('Tmax', 'Fv'))
    erros = []
    if a['diametro_mm'] not in DIAMETROS_MM:
        erros.append('diametro_mm: fora da tabela métrica')
    if classe not in CLASSES:
        erros.append('classe: fora da tabela métrica')
    if erros:
        raise ValueError('; '.join(erros))
    i = DIAMETROS_MM.index(a['diametro_mm'])
    at, ar, ad = AT_MM2[i], AR_MM2[i], math.pi*a['diametro_mm']**2/4
    sp = SP_MPA[CLASSES.index(classe)]
    sig, tau = a['Tmax']*1000/at, a['Fv']*1000/(ar if rosca_no_corte else ad)
    vm = math.sqrt(sig*sig+3*tau*tau)
    nc = _seg(sp, vm)
    fi = .75*at*sp/1000
    r = _resultado([_v('parafuso: von Mises', a['seguranca'], nc)])
    r.update(At=at*1e-6, Ar=ar*1e-6, Ad=ad*1e-6, Sp=sp*1000, sigma=sig*1000, tau=tau*1000,
             NT=_seg(sp, sig), NV=_seg(.577*sp, tau), NComb=nc, Fi=fi,
             torque_nm=.2*fi*a['diametro_mm'], intermediarios=dict(vm=vm*1000),
             ChOk=nc >= a['seguranca'], ApFix=a['seguranca']/nc)
    return r


def espessura_placa(paraf, H, B, ref_in, diametro_mm, pivo_y, N, fy=250000, seguranca=2, esp=0):
    """Faixas em balanço; agrupamento arredondado a 0,001 mm como no C#."""
    a = _numeros(dict(H=H, B=B, ref_in=ref_in, diametro_mm=diametro_mm, N=N, fy=fy,
                      seguranca=seguranca, esp=esp), positivos=('H', 'B', 'ref_in', 'diametro_mm', 'seguranca'), nao_negativos=('esp',))
    H, B, ref_in, diametro_mm, N, seguranca, esp = (
        a[k] for k in ('H', 'B', 'ref_in', 'diametro_mm', 'N', 'seguranca', 'esp'))
    fy = a['fy'] if a['fy'] > 0 else 250000
    linhas = {}
    for y, z, t in paraf:
        _numeros(dict(y=y, z=z, T=t), nao_negativos=('T',))
        key = round(y*1000, 3)
        total, q = linhas.get(key, (0, 0))
        linhas[key] = total+t, q+1
    tt, tl, br, be = 0., 0., 0., 0.
    dia = diametro_mm/1000
    for ymm, (t, q) in linhas.items():
        y = ymm/1000
        if t <= 1e-9 or abs(y) < ref_in/2:
            continue
        e = max(abs(y)-ref_in/2, dia)
        b = min(B, q*(2*e+dia))
        nec = math.sqrt(6*t*e*seguranca/(b*fy))
        if nec > tt:
            tt, tl, br, be = nec, t, e, b
    cc = sum(p[2] for p in paraf)-N if pivo_y is not None else 0
    bc = abs(pivo_y)-ref_in/2 if pivo_y is not None else 0
    tc = math.sqrt(6*cc*bc*seguranca/(B*fy)) if cc > 1e-9 and bc > 0 else 0
    nec = max(tt, tc)
    # Espessura é resistência à flexão, não disposição construtiva: participa
    # da taxa máxima mesmo tendo unidade m.
    v = dict(_v('chapa: espessura', nec, esp, 'm'), criterio_resistente=True)
    r = _resultado([v])
    r.update(EspNecT=tt, EspNecC=tc, EspNec=nec, TLinha=tl, braco=br, largura_ef=be,
             CConc=cc, braco_c=bc, fy_chapa=fy, esp_comercial=next((x for x in CHAPAS if x >= nec), None),
             intermediarios=dict(fileiras=[dict(y=y/1000, T=t, q=q) for y, (t, q) in linhas.items()]))
    if r['esp_comercial'] is None:
        r['avisos'].append('Nenhuma espessura comercial da tabela atende.')
    # Espessura não fornecida nunca representa uma chapa verificada.
    r['ok'] = esp > 0 and nec <= esp
    return r


def carregar_chumbadores():
    """Nova cópia do catálogo, preservando ordem e ausência de dados como None."""
    with (_DADOS / 'chumbadores.csv').open(encoding='utf-8-sig', newline='') as f:
        itens = []
        for row in csv.DictReader(f, delimiter=';', quoting=csv.QUOTE_NONE):
            c = {k: (v or '').strip() for k, v in row.items()}
            for k in c:
                if k.endswith(('_mm', '_mm2', '_kgf', '_mpa', '_kgfm')):
                    try:
                        c[k] = float(c[k]) if c[k] else None
                    except ValueError:
                        c[k] = None
            c['nome'] = f"{c['linha']} {c['pol']} hef {c['hef_mm']:.0f} mm"
            c['quimico'] = c['tipo'] == 'quimico'
            itens.append(c)
    return itens


def achar_chumbador(chave):
    return next((c for c in carregar_chumbadores() if chave and chave in (c['codigo'], c['nome'])), None)


def fator_cones(chumbador, fixacoes):
    """Reproduz produto das uniões 1D legado, não união geométrica geral 2D."""
    if chumbador is None or not fixacoes:
        return 1.
    tr = [p for p in fixacoes if p[2] > 1e-9] or list(fixacoes)
    hef = chumbador['hef_mm']
    if not hef or hef <= 0:
        raise ValueError('hef_mm: positivo obrigatório para cones')
    h = 1.5*hef
    def uniao(xs):
        v = sorted(set(round(x*1000, 3) for x in xs))
        total, ini, fim = 0., v[0]-h, v[0]+h
        for x in v[1:]:
            if x-h <= fim:
                fim = x+h
            else:
                total += fim-ini
                ini, fim = x-h, x+h
        return total+fim-ini
    return min(1., uniao(p[0] for p in tr)*uniao(p[1] for p in tr)/(len(tr)*9*hef*hef))


def seguranca_chumbador(Tref, Vref, T, V):
    """Sem corte informado: termo de concreto omitido, nunca capacidade infinita."""
    a = (max(0, T)/Tref)**(5/3) if Tref > 0 else (math.inf if T > 1e-9 else 0)
    b = (max(0, V)/Vref)**(5/3) if Vref > 0 else 0
    return (a+b)**(-3/5) if a+b > 1e-12 else math.inf


def coeficientes_chumbador(dados):
    d = _dados(dados)
    tipos = ('estatica', 'variavel', 'dinamica', 'choque')
    tipo, risco = str(d['tipo_carga']).strip().lower(), str(d['risco']).strip().lower()
    auto, ra = tipo not in tipos, risco not in ('baixo', 'alto')
    it = (2 if d['rotativa'] else 1) if auto else tipos.index(tipo)
    alto = bool(d['rotativa']) if ra else risco == 'alto'
    tab = ((3, 5), (4, 5), (7, 15), (5, 10))[it][int(alto)]
    exig = d['coef_chumbador'] if d['coef_chumbador'] > 0 else tab
    avisos = []
    if d['coef_chumbador'] > 0 and exig < tab-1e-9:
        avisos.append('Coeficiente abaixo da tabela para o tipo de carga e risco selecionados.')
    elif d['coef_chumbador'] <= 0 and ra and not alto:
        avisos.append('Baixo risco presumido automaticamente: confirme o risco do projeto.')
    return dict(tipo_carga=it, tipo_auto=auto, risco_alto=alto, risco_auto=ra, NExig=exig,
                NSeg=d['seguranca'] if d['seguranca'] > 0 else d['seguranca_modelo'],
                coef_tabela=tab, avisos=avisos)


def avaliar_chumbador(chumbador, dados, solicitacoes):
    """Candidato sem redistribuir forças nem alterar a base ou o catálogo."""
    c = achar_chumbador(chumbador) if isinstance(chumbador, str) else copy.deepcopy(chumbador)
    if c is None:
        raise ValueError('chumbador: item desconhecido')
    d = _dados(dados)
    base = copy.deepcopy(solicitacoes)
    co = coeficientes_chumbador(d)
    co.update({k: base[k] for k in ('NExig', 'NSeg') if k in base})
    co.update(_numeros({k: co[k] for k in ('NExig', 'NSeg')}, positivos=('NExig', 'NSeg')))
    a = _numeros({k: base.get(k, 0) for k in ('Tmax', 'Fv', 'espac_min')}, nao_negativos=('Tmax', 'Fv', 'espac_min'))
    t, v, espac = a['Tmax'], a['Fv'], a['espac_min']
    fck = d['fck'] if d['fck'] > 0 else 20000
    ff = min(1., math.sqrt(fck/(1000*c['fck_mpa']))) if (c['fck_mpa'] or 0) > 0 else 1.
    psi = fator_cones(c, base.get('paraf', []))
    ruptura = (c['tu_kgf'] or 0) > 0
    tref = (c['tu_kgf'] if ruptura else c['tu_rec_kgf'] or 0)*.00980665*ff*psi
    rawv = c['vu_kgf'] if ruptura else c['vu_rec_kgf']
    vr = (rawv or 0)*.00980665
    nr = co['NExig'] if ruptura else 1
    nc = seguranca_chumbador(tref, vr, t, v)
    na, classe, sp = math.inf, '4.6', 0.
    ad = math.pi*c['rosca_mm']**2/4
    at, ar = ad, ad
    sig, tau = t*1000/ad, v*1000/ad
    if c['quimico']:
        classe = d['classe_barra'] if d['classe_barra'] in CLASSES else '4.6'
        sp = SP_MPA[CLASSES.index(classe)]
        at, ar = c['at_mm2'], c['ar_mm2']
        if not at or not ar or min(at, ar) <= 0:
            raise ValueError('at_mm2/ar_mm2: áreas positivas obrigatórias para haste química')
        sig, tau = t*1000/at, v*1000/ar
        na = _seg(sp, math.sqrt(sig*sig+3*tau*tau))
    eo = espac <= 0 or espac >= 6*c['rosca_mm']/1000-1e-12
    verificacoes = [_v('chumbador: concreto (interação 5/3)', nr, nc)]
    if c['quimico']:
        verificacoes.append(_v('chumbador: aço da haste', co['NSeg'], na))
    if espac > 0:
        verificacoes.append(_v('chumbador: espaçamento físico 6d', 6*c['rosca_mm']/1000, espac, 'm'))
    r = _resultado(verificacoes, co['avisos'])
    r.update({k: co[k] for k in co if k != 'avisos'})
    r.update(chumbador=c, Tmax=t, Fv=v, paraf=base.get('paraf', []), espac_min=espac,
             fck_laje=fck, FFck=ff, psi_cone=psi, Tref=tref, Vref=vr if rawv is not None else None,
             TuEf=tref, NRef=nr, NT=_seg(tref, t), NV=_seg(vr, v) if vr > 0 else math.inf,
             NComb=nc, NAco=na, classe_barra=classe, SpBarra=sp*1000,
             At=at*1e-6, Ar=ar*1e-6, Ad=ad*1e-6, sigma=sig*1000, tau=tau*1000,
             torque_nm=c['torque_kgfm']*9.80665 if c['torque_kgfm'] is not None else None,
             espac_ok=eo, ChOk=nc >= nr and na >= co['NSeg'] and eo,
             ApFix=max(nr/nc if nc > 0 else math.inf, co['NSeg']/na),
             T_admissivel=tref/nr, V_admissivel=vr/nr if rawv is not None else None,
             natureza='ruptura' if ruptura else 'recomendada',
             intermediarios=dict(cargas_originais=copy.deepcopy(c), coeficiente=nr, FFck=ff, psi=psi))
    r['ok'] = r['ChOk']
    if rawv is None:
        r['avisos'].append('Concreto ao cisalhamento não verificado: corte não informado; haste química mantém corte no aço.')
    if c['linha'].endswith('WQA'):
        r['avisos'].append('WQA: nRef=1; reduções não calculadas: −30% espaçamento mínimo; −40% tração/−50% corte na borda mínima; −30% entre 100 e 120 °C; −20% tração C15.')
    return r


def perna_minima_norma(t):
    """Entrada e saída em m; tabela original em mm, parte mais fina."""
    t = _numeros(dict(t=t))['t']*1000
    return (0 if t <= 0 or t >= 1e8 else 3 if t <= 6.35 else 5 if t <= 12.5 else 6 if t <= 19 else 8)/1000


def _eletrodo(el):
    el = str(el or 'E70').upper().replace('XX', '')
    return el if el in ELETRODOS else 'E70'


def _solda(secao, dados, esforcos, h):
    s = _secao(secao)
    e = _numeros({k: esforcos[k] for k in ('N', 'V', 'M')})
    h = _numeros(dict(solda=h), positivos=('solda',))['solda']*1000
    b, d = s['b']*1000, s['h']*1000
    if dados.get('eixo_fraco', False):
        b, d = d, b
    circular = s['tipo'] in ('TC', 'CIRC', 'tubo_circular', 'circular')
    # Só conta a solda que assenta na chapa (docs/v3/GEOMETRIA-LIGACOES.md §1): com H/B informados (pelo_perfil falso) a
    # chapa pode ser menor que o perfil; o contorno soldado fica limitado a min(perfil, chapa) em cada direção (d ao longo
    # de H, b ao longo de B). Tubo circular maior que a chapa: círculo inscrito na chapa (a favor da segurança).
    recortada = False
    if not dados.get('pelo_perfil', False):
        Hc, Bc = dados.get('H', 0)*1000, dados.get('B', 0)*1000
        if Hc > 0 and Bc > 0:
            if circular:
                recortada = s['h']*1000 > min(Hc, Bc) + 1e-9
            else:
                recortada = d > Hc + 1e-9 or b > Bc + 1e-9
                d, b = min(d, Hc), min(b, Bc)
    if circular:
        raio = s['h']*500
        if recortada:
            raio = min(Hc, Bc)/2
        area, iu, c = 1.414*math.pi*h*raio, math.pi*raio**3, raio
    else:
        area, iu, c = 1.414*h*(b+d), d*d*(3*b+d)/6, d/2
    inertia = .707*h*iu
    t1 = abs(e['V'])*1000/area
    t2 = abs(e['N'])*1000/area+abs(e['M'])*1e6*c/inertia
    tau = math.hypot(t1, t2)
    el = _eletrodo(dados.get('eletrodo'))
    adm = ELETRODOS[el]
    t = min(s['tf'], s['tw']) if s['tipo'] in ('I', 'U') else s['tw']
    chapa = dados.get('esp', 0)
    hn = perna_minima_norma(min(t if t > 0 else 1e6, chapa if chapa > 0 else 1e6))
    L = area/(.707*h)
    abaixo, maior = hn > 0 and h/1000 < hn-1e-12, t > 0 and h/1000 > t+1e-12
    return dict(grupo='contorno circular' if circular else 'contorno retangular',
                h=h/1000, A=area*1e-6, I=inertia*1e-12, tau1=t1*1000, tau2=t2*1000,
                tau=tau*1000, tau_adm=adm*1000, Ap=tau/adm, L=L/1000,
                LNec=L*min(1, tau/adm)/1000, massa=h*h/2*L*7850e-9,
                espessura=t, t_chapa=chapa, h_min_norma=hn, abaixo_minimo=abaixo,
                maior_que_parede=maior, NEquiv=_seg(.577*(adm/.30), tau, 1e-12),
                seguranca=seguranca_solda(tau, adm),
                passa=tau/adm <= 1 and not abaixo and not maior, eletrodo=el, recortada=recortada,
                intermediarios=dict(b=b/1000, d=d/1000, c=c/1000, Iu=iu*1e-9))


N_MIN_SOLDA = .577/.30   # ≈ 1,92: segurança que o admissível 0,30·FEXX já embute


def seguranca_solda(tau, tau_adm):
    """Critério do Bruno (origin/main 0cefecc), só informativo ao lado da verificação: n = 0,577·FEXX/τ ≥ 0,577/0,30.
    0,577·FEXX = resistência ao corte do metal de solda por von Mises; o admissível AWS/AISC τ ≤ 0,30·FEXX (aqui
    ``tau_adm``, MPa) já traz n ≈ 1,92, então "passa" e "n ≥ 1,92" são a mesma condição (não é critério novo).
    → dict(n, n_min, passa, criterio, informativo)."""
    n = _seg(.577*(tau_adm/.30), tau, 1e-12)
    return dict(n=n, n_min=N_MIN_SOLDA, passa=n >= N_MIN_SOLDA-1e-9, informativo=True,
                criterio='n = 0,577·FEXX / τ ≥ 0,577/0,30 ≈ 1,92 (equivale a τ ≤ 0,30·FEXX; informativo)')


def perna_minima_contorno(secao, dados, esforcos):
    d = _dados(dados)
    for h in PERNAS:
        r = _solda(secao, d, esforcos, h)
        if h >= r['h_min_norma']-1e-12 and r['Ap'] <= 1:
            return h
    return 0.


def verificar_solda_contorno(secao, dados, esforcos):
    """Esforços LOCAIS da barra, assinados; perna positiva obrigatória."""
    d = _dados(dados)
    r = _solda(secao, d, esforcos, d['solda'])
    v = [_v('solda: tensão admissível', r['tau'], r['tau_adm'], 'kN/m²')]
    if r['h_min_norma'] > 0:
        v.append(_v('solda: perna mínima', r['h_min_norma'], r['h'], 'm'))
    if r['espessura'] > 0:
        v.append(_v('solda: limite prático da parede', r['h'], r['espessura'], 'm'))
    r.update(_resultado(v))
    if r['recortada']:
        r.setdefault('avisos', []).append('Chapa menor que o perfil: a solda considerada é só o trecho do contorno sobre a '
                                          'chapa (min(perfil, chapa) em cada direção).')
    r['h_min'] = perna_minima_contorno(secao, d, esforcos)
    r['ok'] = r['passa']
    return r


def diagnosticar_solda(secao, dados, esforcos):
    d = _dados(dados)
    diag = dict(porque='', o_que_fazer='', eletrodo='', perna=0., perna_max=0., sem_solucao=False, passa=True)
    try:
        _secao(secao)
    except ValueError as exc:
        diag.update(porque=str(exc), o_que_fazer='Confira o perfil do pilar.', sem_solucao=True, passa=False)
        return diag
    r = verificar_solda_contorno(secao, d, esforcos)
    if r['passa']:
        return diag
    diag['passa'] = False
    t, hn = r['espessura'], r['h_min_norma']
    if t > 0 and hn > t+1e-12:
        diag.update(porque=f"parede do perfil {t*1000:.2f} mm < perna mínima de norma {hn*1000:g} mm".replace('.', ','),
                    perna_max=t, sem_solucao=True,
                    o_que_fazer='Use perfil com parede maior, chapa de reforço/luva ou detalhe a ligação à parte.')
        return diag
    diag['porque'] = ('perna maior que a parede do perfil' if r['maior_que_parede'] else
                      'perna abaixo do mínimo de norma' if r['abaixo_minimo'] else 'tensão acima do admissível')
    eletrodos = list(ELETRODOS)
    for el in eletrodos[eletrodos.index(r['eletrodo']):]:
        for h in PERNAS:
            if h < hn-1e-12 or (t > 0 and h > t+1e-12):
                continue
            rr = _solda(secao, dict(d, eletrodo=el), esforcos, h)
            if rr['Ap'] <= 1:
                diag.update(eletrodo=el, perna=h, o_que_fazer=f'Use {el} com perna de {h*1000:g} mm.')
                return diag
    diag.update(perna_max=min(t, .025) if t > 0 else .025, sem_solucao=True,
                o_que_fazer='Nenhuma perna da tabela que cabe passa com E120; use pilar maior, nervuras ou detalhe à parte.')
    return diag


# ====================================================================== método NBR (v2)
# Esforços de entrada são de CÁLCULO (ELU, já majorados). Nenhum coeficiente
# global n é aplicado aqui; n sobrevive só no resultado ``legado``.

def resistencia_concreto(fck, H, B, bloco_H=0., bloco_B=0.):
    """σc,Rd = fck/(γc·γn)·√(A2/A1) ≤ fck, √(A2/A1) ≤ 2.  # NBR 8800 6.6.5 (CBCA p. 204)

    A2 = maior retângulo homotético à placa contido no bloco centrado.
    Bloco não informado: A2 = A1 (lado seguro).
    """
    k = min(bloco_H/H, bloco_B/B) if bloco_H > 0 and bloco_B > 0 else 1.
    raiz = min(max(k, 1.), 2.)
    sig = min(fck/(GC*GN)*raiz, fck)
    return dict(sigma_cRd=sig, raiz_A2_A1=raiz, A1=H*B, A2=raiz**2*H*B, item='NBR 8800 6.6.5')


def forcas_bloco(N, M, coordenadas, H, B, sigma_cRd):
    """Bloco retangular de compressão (AISC DG1 3.3/3.4 com σc,Rd da NBR).

    N > 0 tração, M com sinal (M ≥ 0 comprime o lado +y). Ordem:
    1) N > 0 e distribuição elástica toda tracionada → sem contato;
    2) compressão sem tração: Y = H − 2e, fp = P/(Y·B) ≤ σc,Rd;
    3) bloco a σc,Rd + fileira tracionada mais afastada (equilíbrio):
       Y = (f + H/2) − √[(f + H/2)² − 2(|M| + P·f)/qmax], T = qmax·Y − P.
    Chumbadores do lado comprimido e fileiras internas não recebem tração.
    taxa_concreto ≤ 1 ⇔ o equilíbrio tem solução.
    """
    a = _numeros(dict(N=N, M=M, H=H, B=B, sigma_cRd=sigma_cRd), positivos=('H', 'B', 'sigma_cRd'))
    N, M, H, B, sig = (a[k] for k in ('N', 'M', 'H', 'B', 'sigma_cRd'))
    pts = [tuple(_numeros(dict(y=p[0], z=p[1])).values()) for p in coordenadas]
    if not pts:
        raise ValueError('coordenadas: pelo menos uma fixação')
    s = 1 if M >= 0 else -1
    Ma, P, q, n = abs(M), -N, sig*B, len(pts)
    ymin = min(s*y for y, _ in pts)
    f = -ymin                       # centro da placa → fileira tracionada
    lado = [i for i, (y, _) in enumerate(pts) if abs(s*y-ymin) < 1e-9]
    out = dict(N=N, M=M, qmax=q, sigma_cRd=sig, e=Ma/P if P > 0 else None,
               e_crit=H/2-P/(2*q) if P > 0 else None, f=f, Y=0., C=0., fp=0., T_total=0.)
    t = [0.]*n
    if Ma == 0 and P == 0:
        out.update(modelo='Sem esforço', taxa_concreto=0.)
    if N > 0:
        u = [-s*y for y, _ in pts]
        s2 = sum(x*x for x in u)
        te = [N/n+(Ma*x/s2 if s2 > 0 else 0) for x in u]
        # uma fileira (Σu² = 0) não equilibra M sem contato: vai ao bloco de compressão
        if min(te) >= -1e-9 and (s2 > 1e-12 or Ma == 0):
            t = [max(0, x) for x in te]
            out.update(modelo='Todos tracionados', taxa_concreto=0.)
    if 'modelo' not in out and P > 0 and H-2*Ma/P > 0 and P/(q*(H-2*Ma/P)) <= 1:
        Y = H-2*Ma/P
        out.update(modelo='Compressão sem tração', Y=Y, C=P, fp=P/(Y*B), taxa_concreto=P/(q*Y))
    if 'modelo' not in out:
        aa = f+H/2
        disc = aa*aa-2*(Ma+P*f)/q
        Y = aa-math.sqrt(max(disc, 0))
        T = q*Y-P
        if T > 0 or P <= 0:
            out.update(modelo='Bloco de compressão' if disc >= 0 else 'Bloco sem solução (placa pequena)',
                       Y=Y, C=q*Y, fp=sig, T_total=T, taxa_concreto=(Ma+P*f)/(q*aa*aa/2))
            for i in lado:
                t[i] = T/len(lado)
        else:   # esmagamento antes de precisar de tração
            Y = H-2*Ma/P
            out.update(modelo='Concreto esmagado', Y=max(Y, 0), C=P, fp=P/(Y*B) if Y > 0 else math.inf,
                       taxa_concreto=P/(q*Y) if Y > 0 else math.inf)
    paraf = [(y, z, ti) for (y, z), ti in zip(pts, t)]
    out.update(paraf=paraf, Tmax=max(t), T_total=sum(t), pivo_y=None,
               y_bloco=s*(H/2-out['Y']) if out['Y'] > 0 else None, lado_comprimido=s)
    return out


def _ks(secao):
    """Fatores do balanço: I/H 0,95d e 0,8bf; tubo retangular 0,95; circular 0,80 (DG1 3.1)."""
    t = str(secao.get('tipo', '')).upper()
    if t in ('TC', 'CIRC', 'TUBO_CIRCULAR', 'CIRCULAR'):
        return .8, .8
    if t in ('TR', 'RET', 'TUBO_RETANGULAR', 'TQ'):
        return .95, .95
    return .95, .8


def _pressao_cbca(P, Ma, H, B, a):
    """Distribuição elástica do CBCA PBE p. 236 (casos 1–4): fmax, comprimento x e inclinação."""
    e = Ma/P if P > 0 else math.inf
    if e <= H/6:
        fmax, fmin = P/(H*B)*(1+6*e/H), P/(H*B)*(1-6*e/H)
        return dict(caso=1, fmax=fmax, x=math.inf, incl=(fmax-fmin)/H)
    if e <= H/3:
        x = 3*(H/2-e)
        return dict(caso=2, fmax=2*P/(x*B), x=x, incl=2*P/(x*B)/x)
    fmax = 4*(Ma+P*a)/(H*B*(H/3+a))
    return dict(caso=3 if P != 0 else 4, fmax=max(fmax, 0), x=H/2, incl=max(fmax, 0)/(H/2))


def espessura_placa_nbr(forcas, H, B, secao, diametro_mm, fy=250000, fck=20000, esp=0, eixo_fraco=False):
    """Espessura da chapa. Primário: CBCA (cap. 10/11, p. 204/237), t ≥ √[6·Mmax/(1,25·fy/γa1)].

    Lado comprimido, por unidade de largura, balanços m = (H − k·d)/2 e n = (B − k'·bf)/2:
      CBCA: pressão elástica (casos 1–4) → Mm = m²(2fmax + fm)/6 (ou fmax·x/2·(m − x/3) se x < m);
            Mn = fmax·n²/2. Compressão centrada: Mmax = fc·ℓ²/2, ℓ = max(m, n).
      Conferência Pfeil/DG1 (Z = t²/4, t ≥ √(4·M·γa1/fy)): bloco retangular com
            fp = max(pressão do bloco, 0,85·fck/γc) (Pfeil 9.10.5, p. 260).
    Lado tracionado (fileiras fora das mesas, x = |y| − d/2 + tf/2 > 0): balanço DG1, M = T_fileira·x/b_ef,
      b_ef = união das faixas [z − x, z + x] (2x por chumbador, 45°, AISC-DG1.md §1.4.5) recortada na placa.
    Chumbadores tracionados entre as mesas (x ≤ 0): faixa biengastada nas linhas médias das mesas,
      vão L = d − tf, carga P a distâncias a e b das mesas: M = P·a·b·max(a, b)/L² (momento de engaste);
      b_ef = união de [w − min(a, b), w + min(a, b)] recortada na largura da mesa. Sem a alma (conservador).
    Casos 3/4 do CBCA (e > H/3): a fileira tracionada recebe a envoltória entre a tração do bloco e
      Td = fc·H·B/4 − Nd (CBCA p. 236).
    """
    s = _secao(secao)
    ri, ro = (s['b'], s['h']) if eixo_fraco else (s['h'], s['b'])
    kd, kb = _ks(s)
    ki, ko = (kb, kd) if eixo_fraco else (kd, kb)
    m, n = max((H-ki*ri)/2, 0), max((B-ko*ro)/2, 0)
    tf = 0. if eixo_fraco else s['tf']   # DG1: até o centro da mesa; eixo fraco: borda da mesa
    P, Ma = -forcas['N'], abs(forcas['M'])
    comp = forcas['modelo'] != 'Todos tracionados' and (forcas['C'] > 0 or P > 0)
    Mc = Mp = 0.
    pc = None
    if comp:
        pc = _pressao_cbca(P, Ma, H, B, forcas['f'])
        fm = pc['fmax']-pc['incl']*m
        Mm = m*m*(2*pc['fmax']+fm)/6 if m <= pc['x'] else pc['fmax']*pc['x']/2*(m-pc['x']/3)
        Mc = max(Mm, pc['fmax']*n*n/2)
        fp = max(forcas['fp'] if math.isfinite(forcas['fp']) else 0, .85*fck/1.4)
        Y = forcas['Y'] or H
        Mp = max(fp*m*m/2 if Y >= m else fp*Y*(m-Y/2), fp*n*n/2)
    paraf = list(forcas['paraf'])
    td = max(pc['fmax']*H*B/4-P, 0.) if pc and pc['caso'] >= 3 else 0.   # CBCA p. 236, casos 3/4
    if td > 0:
        sl = forcas.get('lado_comprimido', 1)
        ymin = min(sl*y for y, _, _ in paraf)
        fila = [i for i, (y, _, _) in enumerate(paraf) if abs(sl*y-ymin) < 1e-9]
        for i in fila:
            y, z, t = paraf[i]
            paraf[i] = (y, z, max(t, td/len(fila)))
    fora, dentro = {}, {}
    L, bf = s['h']-s['tf'], s['b']
    for y, z, t in paraf:
        if t <= 1e-9:
            continue
        u, w = (z, y) if eixo_fraco else (y, z)
        if abs(y)-ri/2+tf/2 > 0:
            fora.setdefault((abs(y)-ri/2+tf/2, round(y*1000, 3)), []).append((z, t, B/2))
        elif eixo_fraco and abs(z) > L/2 and not _ks(s) == (.8, .8):   # eixo fraco: além da mesa, balanço em z
            fora.setdefault((abs(z)-L/2, round(z*1000, 3)), []).append((y, t, min(H, bf)/2))
        else:   # entre as mesas: vão ao longo de y (eixo forte) ou de z (eixo fraco)
            dentro.setdefault(round(u*1000, 3), []).append((w, t))
    Mt, xt, bt, mod_t, avisos = 0., 0., 0., '', []
    for (x, _), g in fora.items():
        b = _largura([z for z, _, _ in g], x, g[0][2])
        M = sum(t for _, t, _ in g)*x/b
        if M > Mt:
            Mt, xt, bt, mod_t = M, x, b, 'balanço (DG1)'
    circ = _ks(s) == (.8, .8)
    for umm, g in dentro.items():
        a, b = L/2+umm/1000, L/2-umm/1000
        lim = min(H if eixo_fraco else B, bf)/2
        lw = _largura([w for w, _ in g], min(a, b), lim) if min(a, b) > 0 and not circ else 0.
        if lw <= 1e-9:
            avisos.append('Chumbador tracionado entre as mesas fora do modelo de faixa biengastada: chapa NÃO verificada à tração.')
            Mt, mod_t = math.inf, 'fora do modelo'
            continue
        M = sum(t for _, t in g)*a*b*max(a, b)/(L*L)/lw
        if M > Mt:
            Mt, xt, bt, mod_t = M, min(a, b), lw, 'faixa biengastada entre mesas'
    cb = lambda M: math.sqrt(6*M*GA1/(1.25*fy)) if M > 0 else 0.   # math.inf passa como inf
    pl = lambda M: math.sqrt(4*M*GA1/fy) if M > 0 else 0.
    tc, tt_ = cb(Mc), cb(Mt)
    nec = max(tc, tt_)
    pf = max(pl(Mp), pl(Mt))
    item = 'CBCA Manual de Ligações cap. 10/11 (p. 204, 237); NBR 8800 6.6.5'
    v = dict(_v('chapa: espessura', nec, esp, 'm', item), criterio_resistente=True)
    r = _resultado([v], avisos)
    r.update(metodo='CBCA (1,25·W)', modelo_tracao=mod_t, Td_cbca=td, EspNec=nec, EspNecC=tc, EspNecT=tt_, t_cbca=nec, t_pfeil=pf,
             governa='Pfeil/DG1 (conferência)' if pf > nec*(1+1e-9) else 'CBCA',
             m=m, n=n, Mmax_c=Mc, Mmax_t=Mt, Mmax_pfeil=Mp, braco=xt, largura_ef=bt, fy_chapa=fy,
             pressao_cbca=pc, esp_comercial=next((x for x in CHAPAS if x >= nec), None))
    if r['esp_comercial'] is None:
        r['avisos'].append('Nenhuma espessura comercial da tabela atende.')
    r['ok'] = esp > 0 and nec <= esp
    return r


def _largura(ws, c, lim):
    """Comprimento da união dos intervalos [w − c, w + c] recortados em [−lim, lim]."""
    iv = sorted((max(w-c, -lim), min(w+c, lim)) for w in ws)
    tot, (a, b) = 0., iv[0]
    for x, y in iv[1:]:
        if x > b:
            tot, a, b = tot+max(b-a, 0), x, y
        else:
            b = max(b, y)
    return tot+max(b-a, 0)


def cisalhamento_base(V, N, n_fix, dados, sigma_lug):
    """Ordem: atrito → barra de cisalhamento → chumbadores (sem somar mecanismos).

    Atrito: Fat,Rd = μd·Nc, μd = 0,10 por padrão (CBCA cap. 10/11: 10% de Nd; Pfeil eq. 9.17).
    Barra: pressão no concreto à frente σc,Rd(A2 = A1)·b·h e flexão M = V·(G + h/2) ≤ 1,25·b·t²/6·fy/γa1.
    Chumbadores: furo alargado conta só 2 (DG1); arruela soldada → todos.
    """
    d = dados
    fat = d['atrito']*max(0., -N)
    out = dict(V=V, Fat_Rd=fat, verificacoes=[], avisos=[], Fv=0., n_ef=0)
    if V <= fat+1e-12:
        out.update(mecanismo='atrito')
        out['verificacoes'].append(_v('corte: atrito chapa–concreto', V, fat, 'kN', 'CBCA cap. 10 (10% de Nd); Pfeil eq. 9.17'))
        return out
    if min(d['lug_b'], d['lug_t'], d['lug_h']) > 0:
        b, t, h = d['lug_b'], d['lug_t'], d['lug_h']
        M = V*(d['graute']+h/2)
        out.update(mecanismo='barra de cisalhamento', M_barra=M)
        out['verificacoes'] += [
            _v('corte: barra — pressão no concreto', V, sigma_lug*b*h, 'kN', 'NBR 8800 6.6.5 (A2 = A1); DG1 3.5'),
            _v('corte: barra — flexão', M, 1.25*b*t*t/6*d['fy_chapa']/GA1, 'kN·m', 'NBR 8800 5.4.2.2 (1,25·W, como CBCA)')]
        out['avisos'].append('Barra de cisalhamento: cone de corte à frente (ACI 349) e solda barra–placa não verificados.')
        return out
    ne = n_fix if d['arruela_soldada'] else min(n_fix, 2)
    out.update(mecanismo='chumbadores', n_ef=ne, Fv=V/ne)
    if not d['arruela_soldada'] and n_fix > 2:
        out['avisos'].append('Corte nos chumbadores: furo alargado → só 2 efetivos (DG1); use arruela soldada para contar todos.')
    return out


def _lado(lo, hi, h, L):
    a, b = lo-h, hi+h
    if L:
        a, b = max(a, -L/2), min(b, L/2)
    return b-a


def concreto_chumbador_aci(paraf, V, n_ef, hef, da, abrg, fck, fissurado=True, bloco=None):
    """ACI 318-19 cap. 17, chumbador pré-concretado com cabeça/porca, SI (N, mm, MPa). φ = 0,70.

    Tração: cone Ncbg = ANc/ANco·ψec·ψed·ψc·Nb, Nb = 10·√f'c·hef^1,5 (17.6.2), hef' com 3+ bordas
    (17.6.2.1.2); arrancamento Npn = ψc,P·8·Abrg·f'c (17.6.3); fendilhamento lateral
    Nsb = 13·ca1·√Abrg·√f'c se hef > 2,5·ca1 (17.6.4). Corte (direção ±y): borda
    Vcbg = AVc/AVco·ψed·ψc·ψh·Vb, Vb = min(0,6(le/da)^0,2·√da, 3,7)·√f'c·ca1^1,5 (17.7.2);
    pryout Vcpg = kcp·Ncpg (17.7.3). Interação trilinear 17.8 (≤ 1,2).
    Entradas em m/kN/kN·m²; bloco = (H, B, altura) centrado na placa, ou None (sem bordas).
    f'c = fck (limitado a 69 MPa, 17.3.1).
    """
    fc = min(fck/1000, 69.)
    sq = math.sqrt(fc)
    h0, da, ab = hef*1000, da*1000, abrg*1e6
    Hb, Bb, ha = ((bloco[0]*1000, bloco[1]*1000, (bloco[2] or 0)*1000) if bloco else (None, None, 0))
    pts = [(y*1000, z*1000, T*1000) for y, z, T in paraf]
    if Hb:   # chumbador na face ou fora do bloco: geometria inválida (ca = 0 zera/divide as fórmulas do ACI)
        fora = [(y, z) for y, z, _ in pts if min(Hb/2-abs(y), Bb/2-abs(z)) <= 1e-6]
        if fora:
            y, z = fora[0]
            raise ValueError(f'bloco: chumbador em (y, z) = ({y:.0f}, {z:.0f}) mm sem distância positiva às faces do '
                             f'bloco {Hb:.0f} × {Bb:.0f} mm; aumente o bloco ou reduza a borda/placa')
    tr = [p for p in pts if p[2] > 1e-6]
    psc = 1. if fissurado else 1.25
    v, av = [], []
    inf = math.inf

    def bordas(y, z):
        return (Hb/2-y, Hb/2+y, Bb/2-z, Bb/2+z) if Hb else (inf,)*4

    def ncb(g, ecc=(0., 0.)):
        ys, zs = [p[0] for p in g], [p[1] for p in g]
        h = h0
        if Hb:
            cs = [c for c in (Hb/2-max(ys), Hb/2+min(ys), Bb/2-max(zs), Bb/2+min(zs)) if c < 1.5*h0]
            if len(cs) >= 3:
                smax = max((math.dist(p[:2], q[:2]) for p in g for q in g), default=0)
                h = min(h0, max(max(cs)/1.5, smax/3))
        anc = _lado(min(ys), max(ys), 1.5*h, Hb)*_lado(min(zs), max(zs), 1.5*h, Bb)
        cmin = min(min(bordas(p[0], p[1])) for p in g)
        ped = 1. if cmin >= 1.5*h else .7+.3*cmin/(1.5*h)
        pec = math.prod(1/(1+e/(1.5*h)) for e in ecc)
        nb = 10*sq*h**1.5
        return dict(Ncbg=anc/(9*h*h)*pec*ped*psc*nb, ANc=anc, ANco=9*h*h, hef_ef=h, psi_ed=ped,
                    psi_ec=pec, psi_c=psc, Nb=nb, ca_min=cmin)

    out = dict(norma='ACI 318-19 cap. 17', phi=PHI_ACI, fc_MPa=fc)
    rn = rv = 0.
    if tr:
        T = sum(p[2] for p in tr)
        ecc = tuple(abs(sum(p[i]*p[2] for p in tr)/T-sum(p[i] for p in tr)/len(tr)) for i in (0, 1))
        cone = ncb(tr, ecc)
        Tmax = max(p[2] for p in tr)
        np_ = (1. if fissurado else 1.4)*8*ab*fc
        v += [_v('concreto: cone de tração', T/1000, PHI_ACI*cone['Ncbg']/1000, 'kN', 'ACI 318-19 17.6.2'),
              _v('concreto: arrancamento da cabeça', Tmax/1000, PHI_ACI*np_/1000, 'kN', 'ACI 318-19 17.6.3')]
        out.update(cone_tracao=cone, Npn=np_/1000)
        rn = max(T/(PHI_ACI*cone['Ncbg']), Tmax/(PHI_ACI*np_))
        lat = [(p, min(bordas(p[0], p[1]))) for p in tr]
        lat = [(p, c) for p, c in lat if h0 > 2.5*c]
        if lat:
            c1 = min(c for _, c in lat)
            p0 = next(p for p, c in lat if c == c1)
            b4 = sorted(bordas(p0[0], p0[1]))
            c2 = b4[2] if b4[1] == c1 else b4[1]   # menor distância à borda perpendicular
            ca = min(max(c2/c1, 1), 3)
            nsb = 13*c1*math.sqrt(ab)*sq*((1+ca)/4 if c2 < 3*c1 else 1)
            s = max((math.dist(p[:2], q[:2]) for p, _ in lat for q, _ in lat), default=0)
            if len(lat) > 1 and s < 6*c1:
                sol, res = sum(p[2] for p, _ in lat), (1+s/(6*c1))*nsb
            else:
                sol, res = max(p[2] for p, _ in lat), nsb
            v.append(_v('concreto: fendilhamento lateral', sol/1000, PHI_ACI*res/1000, 'kN', 'ACI 318-19 17.6.4'))
            out['Nsb'] = res/1000
            rn = max(rn, sol/(PHI_ACI*res))
    if V > 1e-9:
        Vn = V*1000
        if Hb:
            le = min(h0, 8*da)
            piores = []
            for sd in (1, -1):
                linhas = sorted({round(sd*p[0], 6) for p in pts})
                for yv, frac in ((linhas[-1], None), (linhas[0], 1.)):
                    fila = [p for p in pts if abs(sd*p[0]-yv) < 1e-6]
                    c1 = Hb/2-yv
                    if c1 <= 0:
                        continue
                    vb = min(.6*(le/da)**.2*math.sqrt(da), 3.7)*sq*c1**1.5
                    hv = min(1.5*c1, ha) if ha > 0 else 1.5*c1
                    zs = [p[1] for p in fila]
                    avc = min(_lado(min(zs), max(zs), 1.5*c1, Bb)*hv, len(fila)*4.5*c1*c1)
                    c2 = min(min(Bb/2-p[1], Bb/2+p[1]) for p in fila)
                    ped = 1. if c2 >= 1.5*c1 else .7+.3*c2/(1.5*c1)
                    ph = max(1., math.sqrt(1.5*c1/ha)) if ha > 0 else 1.
                    vc = avc/(4.5*c1*c1)*ped*(1. if fissurado else 1.4)*ph*vb
                    f = frac if frac is not None else min(len(fila), n_ef)/n_ef
                    piores.append((Vn*f/(PHI_ACI*vc), Vn*f, vc))
            r0 = max(piores)
            v.append(_v('concreto: ruptura da borda ao corte', r0[1]/1000, PHI_ACI*r0[2]/1000, 'kN', 'ACI 318-19 17.7.2'))
            rv = r0[0]
            if ha <= 0:
                av.append('Altura do bloco não informada: ψh,V = 1 (bloco ≥ 1,5·ca1).')
        else:
            av.append('Bloco não informado: ruptura da borda do concreto ao corte (ACI 17.7.2) NÃO verificada.')
        # 17.7.3: alavanca só no grupo que recebe corte (furo alargado → n_ef); o pior grupo de n_ef
        ne = min(max(int(n_ef), 1), len(pts))
        cp = min((ncb(list(g)) for g in combinations(pts, ne)), key=lambda c: c['Ncbg'])
        out['pryout'] = dict(cp, n=ne)
        kcp = 1. if h0 < 65 else 2.
        v.append(_v('concreto: arrancamento por alavanca (pryout)', V, PHI_ACI*kcp*cp['Ncbg']/1000, 'kN', 'ACI 318-19 17.7.3'))
        rv = max(rv, Vn/(PHI_ACI*kcp*cp['Ncbg']))
    if rn > 0 and rv > 0:
        it = rn if rv <= .2 else rv if rn <= .2 else (rn+rv)/1.2
        v.append(_v('concreto: interação tração–corte', it, 1., '', 'ACI 318-19 17.8'))
    if not Hb:
        av.append('Bloco não informado: distâncias às bordas tomadas como infinitas no cone (ψed = 1).')
    out.update(verificacoes=v, avisos=av, razao_N=rn, razao_V=rv)
    return out


def _fu_fy(classe, fub=0, fyb=0):
    """Classe ISO 898-1 'a.b': fub = a·100 MPa, fyb = 0,a·b·fub; valores > 0 prevalecem (kN/m²)."""
    a, b = (int(x) for x in classe.split('.'))
    fu = fub if fub > 0 else a*100000
    return fu, fyb if fyb > 0 else (b/10*a*100000 if fub <= 0 else 0)


def aco_chumbador_nbr(T, Fv, d, fub, fyb=0, rosca_no_corte=True, esp=0, lf=0, fu_chapa=400000):
    """Haste: NBR 8800 6.3.3.1 (0,75·Ab·fub/γa2), 5.2.2 a) escoamento Ab·fy/γa1 (CBCA p. 237),
    6.3.3.2 corte 0,4/0,5·Ab·fub/γa2, 6.3.3.3 pressão no furo da chapa, 6.3.3.4 interação."""
    ab = math.pi*d*d/4
    ft, fv = tracao_parafuso(d, fub), cisalhamento_parafuso(d, fub, rosca_no_corte, True)
    v = [_v('chumbador: tração na haste', T, ft, 'kN', 'NBR 8800 6.3.3.1')]
    if fyb > 0:
        v.append(_v('chumbador: escoamento da haste', T, ab*fyb/GA1, 'kN', 'NBR 8800 5.2.2 a)'))
    if Fv > 0:
        v.append(_v('chumbador: corte na haste', Fv, fv, 'kN', 'NBR 8800 6.3.3.2'))
        if esp > 0:   # lf ≤ 0: furo aberto na borda, ligamento nulo → resistência 0 (reprova; nunca omitir)
            v.append(_v('chapa: pressão de contato no furo', Fv, pressao_contato(lf, esp, d, fu_chapa) if lf > 0 else 0.,
                        'kN', 'NBR 8800 6.3.3.3'))
        if T > 0:
            v.append(_v('chumbador: interação tração–corte', interacao_tracao_cisalhamento(T, ft, Fv, fv), 1., '', 'NBR 8800 6.3.3.4'))
    return dict(Ab=ab, Ft_Rd=ft, Fv_Rd=fv, verificacoes=v, Ag_nec_corte=None)


def _legado(d, e, g, secao):
    """Caminho Nucleo.cs inalterado (pivô na borda, n global). Só alimenta ``passa_legado``."""
    f = forcas_fixacoes(**e, coordenadas=g['coordenadas'], H=g['H'], ref_in=g['ref_in'], pivo_na_mesa=d['pivo_na_mesa'])
    co = coeficientes_chumbador(d)
    if not d['chumbador']:
        co['NExig'] = co['NSeg']
    base = dict(f, espac_min=g['espac_min'], NExig=co['NExig'], NSeg=co['NSeg'])
    if d['chumbador']:
        fix = avaliar_chumbador(d['chumbador'], d, base)
        dia = fix['chumbador']['rosca_mm']
    else:
        fix = verificar_parafuso_placa(f['Tmax'], f['Fv'], d['diametro_mm'], d['classe'], d['rosca_no_corte'], co['NSeg'])
        dia = d['diametro_mm']
    chapa = espessura_placa(f['paraf'], g['H'], g['B'], g['ref_in'], dia, f['pivo_y'], e['N'], d['fy_chapa'], co['NSeg'], d['esp'])
    return dict(metodo=METODO, forcas=f, fixacao=fix, chapa=chapa, coeficientes=co, passa=fix['ChOk'])


def verificar_placa_base(dados, esforcos, secao):
    """Placa de base pelo método NBR (v2). Esforços N/V/M de CÁLCULO (ELU).

    Concreto: NBR 8800 6.6.5; forças nos chumbadores: bloco retangular (``forcas_bloco``);
    chapa: CBCA (+ conferência Pfeil/DG1); corte: atrito → barra → chumbadores;
    chumbador pré-concretado (``chumbador`` vazio): aço NBR 8800 6.3.3 + concreto ACI 318-19
    cap. 17; chumbador de catálogo (pós-instalado): caminho de catálogo legado + aço NBR na
    haste química. ``legado``/``passa_legado`` mantêm o Nucleo.cs (n global) intacto.
    Solda: modelo legado de contorno (tensão admissível). Não depende de GUI/solver.
    """
    d = _dados(dados)
    g = geometria_placa(d, secao)
    e = _numeros({k: esforcos.get(k) for k in ('N', 'V', 'M')}, nao_negativos=('V',))
    avisos = list(esforcos.get('avisos', []))
    try:
        leg = _legado(d, e, g, secao)
    except ValueError as exc:
        leg = None
        avisos.append(f'Método legado não calculado: {exc}')
    co = leg['coeficientes'] if leg else coeficientes_chumbador(d)
    fck = d['fck'] if d['fck'] > 0 else 20000
    fy = d['fy_chapa'] if d['fy_chapa'] > 0 else 250000
    H, B = g['H'], g['B']
    conc = resistencia_concreto(fck, H, B, d['bloco_H'], d['bloco_B'])
    f = forcas_bloco(e['N'], e['M'], g['coordenadas'], H, B, conc['sigma_cRd'])
    f['V'] = e['V']
    P = -e['N']
    v = [_v('concreto: pressão de contato', f['taxa_concreto'], 1., '', conc['item'])]
    conc.update(taxa=f['taxa_concreto'], fp=f['fp'], A1_nec=P/conc['sigma_cRd'] if P > 0 else 0.)
    cis = cisalhamento_base(e['V'], e['N'], len(g['coordenadas']), d, fck/(GC*GN))
    f['Fv'] = cis['Fv']
    v += cis['verificacoes']
    avisos += cis['avisos']
    s = _secao(secao)
    bloco = (d['bloco_H'], d['bloco_B'], d['bloco_altura']) if d['bloco_H'] > 0 and d['bloco_B'] > 0 else None
    if d['chumbador']:
        c = achar_chumbador(d['chumbador'])
        if c is None:
            raise ValueError('chumbador: item desconhecido')
        fix = avaliar_chumbador(c, d, dict(f, espac_min=g['espac_min'], NExig=co['NExig'], NSeg=1))
        dia = c['rosca_mm']
        if c['quimico']:
            fu, fyb = _fu_fy(d['classe_barra'] if d['classe_barra'] in CLASSES else '4.6')
            nbr = aco_chumbador_nbr(f['Tmax'], f['Fv'], dia/1000, fu, fyb, True)
            fix['verificacoes'] += nbr['verificacoes']
            fix['aco_nbr'] = nbr
            fix['ChOk'] = fix['ChOk'] and all(x['taxa'] <= 1 for x in nbr['verificacoes'])
        fix['avisos'].append('Catálogo pós-instalado: coeficiente de ruptura do fabricante aplicado sobre esforços de cálculo (conservador).')
        fix['tipo'] = 'pós-instalado (catálogo)'
    else:
        dia = d['diametro_mm']
        dm = dia/1000
        if d['classe'] not in CLASSES:
            raise ValueError('classe: fora da tabela métrica')
        fu, fyb = _fu_fy(d['classe'], d['fub_chumbador'], d['fyb_chumbador'])
        # furo alargado da placa de base (AISC Manual tab. 14-2 / DG1 tab. 2.3), coerente com n_ef = 2 no corte
        dh = dm+(.0143 if dm <= .01905 else .0175 if dm <= .0223 else .0206 if dm <= .0381 else .0254 if dm <= .0445 else .0318)
        # NBR 8800 tab. 12 (furo alargado): db+5 (db ≤ 24), 33 (27), db+8 (≥ 30) mm. ponytail: 24 < db < 30 → db+6
        dh12 = dm+(.005 if dm <= .024 else .006 if dm < .030 else .008)
        if dh > dh12+1e-9 and not d['arruela_soldada']:
            avisos.append(f'Furo de {dh*1000:.1f} mm > furo alargado da NBR 8800 tab. 12 ({dh12*1000:.1f} mm): a 6.3.6.1 '
                          'exige arruelas especialmente dimensionadas soldadas à placa (arruela_soldada).')
        lf = d['borda']-dh/2
        if lf <= 0 and f['Fv'] > 0:
            avisos.append('Furo alargado alcança a borda (lf ≤ 0): sem ligamento entre furo e borda — pressão de '
                          'contato com resistência nula (reprovado); aumente a borda.')
        nbr = aco_chumbador_nbr(f['Tmax'], f['Fv'], dm, fu, fyb, d['rosca_no_corte'], d['esp'], lf,
                                d['fu_chapa'] if d['fu_chapa'] > 0 else 400000)
        hef = d['hef'] if d['hef'] > 0 else 12*dm
        abrg = d['abrg'] if d['abrg'] > 0 else 1.163*dm*dm
        aci = concreto_chumbador_aci(f['paraf'], f['Fv']*cis['n_ef'], max(cis['n_ef'], 1), hef, dm, abrg, fck,
                                     d['fissurado'], bloco)
        fix = _resultado(nbr['verificacoes']+aci['verificacoes'], aci['avisos'])
        if d['hef'] <= 0:
            fix['avisos'].append(f'hef não informado: adotado 12·Ø = {hef*1000:.0f} mm (CBCA p. 237).')
        if d['abrg'] <= 0:
            fix['avisos'].append('Abrg não informado: porca hexagonal pesada, Abrg ≈ 1,163·Ø².')
        fix.update(tipo='pré-concretado (ACI 318-19 cap. 17)', aco=nbr, concreto=aci, hef=hef, abrg=abrg, furo=dh, lf=lf,
                   fub=fu, fyb=fyb, Tmax=f['Tmax'], Fv=f['Fv'],
                   Ag_nec_corte=e['V']*GA2/(max(cis['n_ef'], 1)*.4*fu) if cis['mecanismo'] == 'chumbadores' else 0.)
        fix['ChOk'] = fix['ok']
        espac = 4*dm   # ACI 318-19 17.9.2: pré-concretado 4·da
        if g['espac_min'] > 0:
            fix['verificacoes'].append(_v('chumbador: espaçamento mínimo 4·Ø', espac, g['espac_min'], 'm', 'ACI 318-19 17.9.2'))
    chapa = espessura_placa_nbr(f, H, B, secao, dia, fy, fck, d['esp'], d['eixo_fraco'])
    inclinada = bool(esforcos.get('inclinada', False))
    barra = esforcos.get('barra')
    if barra is None and all(esforcos.get(k) is not None for k in ('N_barra', 'V_barra', 'M_barra')):
        barra = dict(N=esforcos['N_barra'], V=esforcos['V_barra'], M=esforcos['M_barra'])
    solda = None
    avisos += fix['avisos']+chapa['avisos']
    if inclinada:
        avisos.append('Barra inclinada: chapa e solda não verificadas.')
    elif barra is not None:
        if d['solda'] == 0:
            d['solda'] = perna_minima_contorno(secao, d, barra)
        if d['solda'] > 0:
            solda = verificar_solda_contorno(secao, d, barra)
        else:
            avisos.append('Nenhuma perna comercial atende à tensão da solda.')
    else:
        avisos.append('Solda não verificada: forneça esforços locais da barra.')
    if esforcos.get('sismo') or d.get('sismo'):
        avisos.append('Sismo: ACI 318-19 17.10 (0,75·φ no concreto, ancoragem dúctil ou Ω0) NÃO aplicado; '
                      'modos de aço da haste fora da interação 17.8 (verificados pela NBR 8800 6.3.3.4).')
    if f['modelo'] in ('Bloco sem solução (placa pequena)', 'Concreto esmagado'):
        avisos.append(f"{f['modelo']}: aumente a placa ou o bloco.")
    v += fix['verificacoes']+chapa['verificacoes']+(solda['verificacoes'] if solda else [])
    r = _resultado(v, avisos)
    r.update(metodo=METODO_NBR, geometria=g, esforcos=copy.deepcopy(esforcos), forcas=f, concreto=conc,
             cisalhamento=cis, fixacao=fix, chapa=chapa, solda=solda, legado=leg,
             passa_legado=bool(leg and leg['passa']) and not inclinada, tem_esforcos=True,
             intermediarios=dict(geometria=g, distribuicao=dict(modelo=f['modelo'], Y=f['Y'], f=f['f'],
                                 e=f['e'], e_crit=f['e_crit'], qmax=f['qmax']), coeficientes=co),
             limitacoes=list(LIMITACOES))
    r['ok'] = all(x['taxa'] <= 1 for x in v) and fix['ChOk'] and chapa['ok'] and solda is not None and solda['ok'] and not inclinada
    # G2: ponta do pilar na face superior da placa (x local subindo ao longo do pilar), sem recorte
    g['pontas'] = pontas_locais('base', secao, recuo=d['esp'])
    return r


def dados_no_eixo(dados, fraco):
    """A mesma placa física vista no eixo fraco do pilar: H↔B, fileiras↔por_fileira, bloco_H↔bloco_B e o perfil gira
    (eixo_fraco invertido). Os chumbadores (y, z) do resultado no eixo fraco são os (z, y) da placa no eixo forte."""
    d = dict(dados or {})
    if fraco:
        pad = {c['nome']: c['padrao'] for c in esquema_placa_base()}
        for a, b in (('H', 'B'), ('fileiras', 'por_fileira'), ('bloco_H', 'bloco_B')):
            d[a], d[b] = d.get(b, pad[b]), d.get(a, pad[a])
        d['eixo_fraco'] = not bool(d.get('eixo_fraco', False))
    return d


def casos_biaxiais(dados, esforcos):
    """Flexão biaxial como duas verificações uniaxiais (o método é uniaxial, ver LIMITACOES): My no eixo forte e Mz no
    eixo fraco da mesma placa (``dados_no_eixo``). esforcos: N, V (módulo no plano), My (ou M), Mz e os opcionais
    (sismo, inclinada...). → [(eixo, dados, esforcos uniaxiais)]; a solda recebe os mesmos N, V, M."""
    base = {k: v for k, v in esforcos.items() if k not in ('M', 'My', 'Mz')}
    out = []
    for eixo, M, fraco in (('forte (My)', esforcos.get('My', esforcos.get('M', 0.)), False),
                           ('fraco (Mz)', esforcos.get('Mz', 0.), True)):
        out.append((eixo, dados_no_eixo(dados, fraco), dict(base, M=M, N_barra=esforcos['N'], V_barra=esforcos['V'], M_barra=M)))
    return out


def verificar_placa_base_biaxial(dados, esforcos, secao):
    """Pior das duas verificações uniaxiais de ``casos_biaxiais`` (campo ``eixo`` no resultado)."""
    pior = None
    for eixo, d, e in casos_biaxiais(dados, esforcos):
        r = verificar_placa_base(d, e, secao)
        r['eixo'] = eixo
        t = r['taxa_max'] if math.isfinite(r['taxa_max']) else math.inf
        if pior is None or t > pior[0]:
            pior = (t, r)
    return pior[1]


def dimensionar_chumbador(dados, esforcos, secao, linha=None, metodo='nbr'):
    """Heurística estável por arranjo/rosca/hef; não estima preços nem muta dados.

    metodo='legado' reproduz o C# (aceita pela fixação do Nucleo.cs); 'nbr' exige a fixação v2.
    """
    d = _dados(dados)
    itens = [c for c in carregar_chumbadores() if not linha or c['linha'] == linha]
    itens.sort(key=lambda c: (c['rosca_mm'], c['hef_mm']))
    f0, q0 = d['fileiras'], d['por_fileira']
    for nf, nq in ((2, 2), (2, 3), (3, 3)):
        if nf*nq < f0*q0:
            continue
        for c in itens:
            candidato = dict(d, chumbador=c['codigo'] or c['nome'], fileiras=max(nf, f0), por_fileira=max(nq, q0))
            try:
                r = verificar_placa_base(candidato, esforcos, secao)
            except ValueError:
                continue
            fix = r['legado']['fixacao'] if metodo == 'legado' and r['legado'] else r['fixacao']
            if r['tem_esforcos'] and fix['ChOk']:
                n = len(r['forcas']['paraf'])
                return dict(descricao=f"{n} × {c['nome']} ({c['fabricante']})", dados=candidato, resultado=r)
    return dict(descricao='', dados=None, resultado=None)


def para_json(resultado):
    """Inf/NaN → nulo com caminhos explícitos; permite json.dumps(allow_nan=False)."""
    ausentes = []
    def limpar(v, caminho):
        if isinstance(v, float) and not math.isfinite(v):
            # Inf em taxa pode significar falta de capacidade; não rotular
            # esse caso como ausência de solicitação.
            seguranca = caminho.rsplit('.', 1)[-1] in ('NT', 'NV', 'NComb', 'NAco', 'NEquiv')
            ausentes.append(dict(campo=caminho, estado=(
                'sem solicitação ou componente não aplicável' if math.isinf(v) and seguranca
                else 'capacidade insuficiente ou valor indisponível')))
            return None
        if isinstance(v, dict):
            return {k: limpar(x, f'{caminho}.{k}') for k, x in v.items()}
        if isinstance(v, (tuple, list)):
            return [limpar(x, f'{caminho}[{i}]') for i, x in enumerate(v)]
        return v
    return dict(resultado=limpar(resultado, 'resultado'), estados_nao_finitos=ausentes)
