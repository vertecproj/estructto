"""Placa de base: compatibilidade com Nucleo.cs e Solda.cs (método legado).

API: kN, m, kN.m, m², m⁴ e kN/m²; diâmetro nominal em ``diametro_mm``.
Catálogo conserva mm, MPa, kgf nos campos com sufixos. Contas de solda e
parafuso convertem internamente para N/mm/MPa e retornam unidades da API.
Não constitui verificação normativa completa: ver LIMITACOES.
"""
from __future__ import annotations

import copy
import csv
import math
from pathlib import Path

METODO = 'legado simplificado Nucleo.cs / Solda.cs'
FONTES = ('codigo/portico/src/Nucleo.cs:864–1318', 'codigo/Estructto/Solda.cs:1–159')
COBERTURA = ('placa rígida', 'parafuso métrico', 'chumbadores de catálogo', 'solda de contorno')
LIMITACOES = (
    'Chapa por faixas simplificadas; pressão no concreto e punção não verificadas.',
    'Geometria, espessura, armadura da laje e borda do concreto não conhecidas.',
    'Interação 5/3 e correção raiz de fck são hipóteses de projeto; sem extrapolação para cima.',
    'Solda de contorno idealizado; sem verificação do metal base, 4b/40 mm ou máximo t−1,5 mm.',
    'Barra inclinada fora do modelo de chapa e solda.',
    'Reduções especiais WQA por borda, temperatura e C15 não calculadas.',
    'Laje ≥ 2 hef e reaperto sob dinâmica são orientações, não verificações.',
    'O guia diz que comparação não existe e planilha reprova s<s_min; aqui prevalece o C# com ψ e 6d.',
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
             'fy_chapa', 'fck', 'seguranca', 'seguranca_modelo', 'coef_chumbador', 'solda')
    d.update(_numeros({k: d[k] for k in nomes}, positivos=('seguranca_modelo', 'diametro_mm'),
                     nao_negativos=('esp', 'borda', 'sobra', 'solda'), inteiros=('fileiras', 'por_fileira')))
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
    if circular:
        raio = s['h']*500
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
                passa=tau/adm <= 1 and not abaixo and not maior, eletrodo=el,
                intermediarios=dict(b=b/1000, d=d/1000, c=c/1000, Iu=iu*1e-9))


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


def verificar_placa_base(dados, esforcos, secao):
    """Esforços resolvidos de placa N/V/M; locais opcionais em ``barra``.

    A solda só é aprovada com esforços locais explícitos. Geometria/forças
    permanecem disponíveis mesmo quando inclinada. Não depende de GUI/solver.
    """
    d = _dados(dados)
    g = geometria_placa(d, secao)
    e = _numeros({k: esforcos.get(k) for k in ('N', 'V', 'M')}, nao_negativos=('V',))
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
    inclinada = bool(esforcos.get('inclinada', False))
    barra = esforcos.get('barra')
    if barra is None and all(esforcos.get(k) is not None for k in ('N_barra', 'V_barra', 'M_barra')):
        barra = dict(N=esforcos['N_barra'], V=esforcos['V_barra'], M=esforcos['M_barra'])
    solda = None
    avisos = list(esforcos.get('avisos', []))+fix['avisos']+chapa['avisos']
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
    v = fix['verificacoes']+chapa['verificacoes']+(solda['verificacoes'] if solda else [])
    r = _resultado(v, avisos)
    r.update(geometria=g, esforcos=copy.deepcopy(esforcos), forcas=f, fixacao=fix, chapa=chapa, solda=solda,
             passa_legado=fix['ChOk'] and not inclinada, tem_esforcos=True,
             intermediarios=dict(geometria=g, distribuicao=f['intermediarios'], coeficientes=co),
             limitacoes=list(LIMITACOES))
    r['ok'] = fix['ChOk'] and chapa['ok'] and solda is not None and solda['ok'] and not inclinada
    return r


def dimensionar_chumbador(dados, esforcos, secao, linha=None):
    """Heurística estável por arranjo/rosca/hef; não estima preços nem muta dados."""
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
            if r['tem_esforcos'] and r['fixacao']['ChOk']:
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
