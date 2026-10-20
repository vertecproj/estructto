"""Regressão PORTAR-PLACA §9 / Testes.cs; só stdlib.

Valores esperados mantidos nas unidades originais quando há conversão explícita.
A fixture guincho resolve rigidez linear 2D com as propriedades tabuladas do C#;
não supõe números de esforços ausentes do guia. Não é um solver de produção.
"""
import copy
import hashlib
import json
import math
from pathlib import Path
import unittest

from prossen_estrutural.aco import placa_base as p
from prossen_estrutural.aco import fixacoes as fx

W = dict(h=.203, b=.203, tw=.0072, tf=.011, tipo='I')
TC = dict(h=.1143, b=.1143, tw=.0073, tf=.0073, tipo='TC')
TR = dict(h=.05, b=.03, tw=.002, tf=.002, tipo='TR')
D = dict(H=.4, B=.3, esp=.019, borda=.05, fileiras=2, por_fileira=2,
         diametro_mm=16, classe='8.8', rosca_no_corte=True, seguranca_modelo=2)


def guincho(oposta=False):
    """T:287–305; catálogo: A cm², Ix cm⁴ e massa kg/m, g=9,80665.

    Eliminação de Gauss com pivô parcial e elementos Euler–Bernoulli, nós
    1 e 4 engastados. Retorna forças de extremidade sem cargas diretas do nó.
    """
    nos = ((0., 0.), (0., 4.), (0., 5.), (-2.5, 0.))
    barras = ((0, 1, 24.539e-4, 352.818e-8, 19.26),
              (1, 2, 24.539e-4, 352.818e-8, 19.26),
              (3, 1, 2.971e-4, 9.748e-8, 2.33))
    kg = [[0.]*12 for _ in range(12)]
    carga = [0.]*12
    carga[7], carga[8] = -4.95, 4.95 if oposta else -4.95
    elementos = []
    for ni, nj, area, inertia, massa in barras:
        dx, dy = nos[nj][0]-nos[ni][0], nos[nj][1]-nos[ni][1]
        L = math.hypot(dx, dy)
        c, s = dx/L, dy/L
        ea, ei = 200e6*area/L, 200e6*inertia
        a, b, d, e = 12*ei/L**3, 6*ei/L**2, 4*ei/L, 2*ei/L
        k = [[ea,0,0,-ea,0,0], [0,a,b,0,-a,b], [0,b,d,0,-b,e],
             [-ea,0,0,ea,0,0], [0,-a,-b,0,a,-b], [0,b,e,0,-b,d]]
        t = [[c,s,0,0,0,0], [-s,c,0,0,0,0], [0,0,1,0,0,0],
             [0,0,0,c,s,0], [0,0,0,-s,c,0], [0,0,0,0,0,1]]
        peso = massa*9.80665/1000
        axial, transversal = -s*peso, -c*peso
        fe = [axial*L/2, transversal*L/2, transversal*L**2/12,
              axial*L/2, transversal*L/2, -transversal*L**2/12]
        ids = [3*ni+i for i in range(3)]+[3*nj+i for i in range(3)]
        for i, gi in enumerate(ids):
            carga[gi] += sum(t[j][i]*fe[j] for j in range(6))
            for j, gj in enumerate(ids):
                kg[gi][gj] += sum(t[a0][i]*k[a0][b0]*t[b0][j] for a0 in range(6) for b0 in range(6))
        elementos.append((ids, t, k, fe, c, s, (dx,dy)))
    livres = list(range(3,9))
    mat = [[kg[i][j] for j in livres]+[carga[i]] for i in livres]
    for col in range(6):
        piv = max(range(col,6), key=lambda i: abs(mat[i][col]))
        mat[col], mat[piv] = mat[piv], mat[col]
        den = mat[col][col]
        mat[col] = [v/den for v in mat[col]]
        for i in range(6):
            if i != col:
                fat = mat[i][col]
                mat[i] = [x-fat*y for x,y in zip(mat[i],mat[col])]
    u = [0.]*12
    for i, gi in enumerate(livres):
        u[gi] = mat[i][-1]
    saidas = []
    for ids, t, k, fe, c, s, direcao in elementos:
        ul = [sum(t[i][j]*u[ids[j]] for j in range(6)) for i in range(6)]
        f = [sum(k[i][j]*ul[j] for j in range(6))-fe[i] for i in range(6)]
        # N/Q/M locais na seção inicial: convenção do C#.
        ext = dict(F=f, C=c, S=s, N_barra=-f[0], V_barra=abs(f[1]), M_barra=-f[2])
        saidas.append((p.esforcos_placa(ext,direcao), ext))
    reacoes = [sum(kg[i][j]*u[j] for j in range(12))-carga[i] for i in range(12)]
    return saidas, reacoes


def base_guincho(ch='Walsywa WB 5/8" hef 148 mm', borda=.05):
    e = guincho(True)[0][0][0]
    d = dict(D, pelo_perfil=True, sobra=.1, esp=.0127, borda=borda,
             chumbador=ch, seguranca_modelo=1.5, rotativa=True)
    return d, e, p.verificar_placa_base(d,e,TC)


class Compatibilidade(unittest.TestCase):
    def conf(self, obtido, esperado, rel=1e-6):
        # T:15–19: piso 1 no padrão; 1e-9 nos demais relTol.
        piso = 1 if rel == 1e-6 else 1e-9
        self.assertTrue(math.isclose(obtido, esperado, rel_tol=rel, abs_tol=rel*piso),
                        f'{obtido!r} != {esperado!r} (rel={rel:g})')

    def test_9_1_balanco(self):
        e = dict(N=0,V=10,M=30,barra=dict(N=0,V=10,M=30))
        r = p.verificar_placa_base(D,e,W)
        # Números do C# (Testes.cs): agora no resultado ``legado`` (pivô na borda, n global).
        L = r['legado']
        f, b, ch = L['forcas'], L['fixacao'], L['chapa']
        self.conf(abs(f['M']),30)
        self.conf(f['Tmax'],42)
        self.conf(sum(t*max(0,-y+.2) for y,z,t in f['paraf']),30)
        self.conf(f['Fv'],2.5)
        self.conf(b['sigma']/1000,42000/157)
        self.conf(b['NT'],600/(42000/157))
        self.conf(b['NV'],.577*600/(2500/144))
        self.conf(b['torque_nm'],.2*.75*157*600/1000*16)
        tr = p.forcas_fixacoes(100,0,0,r['geometria']['coordenadas'],.4,.203)
        self.conf(tr['Tmax'],25)
        g = p.geometria_placa(dict(D,pelo_perfil=True,sobra=.1),W)
        self.conf(g['H'],.410)
        self.conf(g['B'],.410)
        ee, be = 150-203/2,2*(2*(150-203/2)+16)
        self.conf(ch['braco']*1000,ee)
        self.conf(ch['largura_ef']*1000,be)
        self.conf(ch['EspNecT']*1000,math.sqrt(6*84000*ee*2/(be*250)))
        self.conf(ch['EspNecC']*1000,math.sqrt(6*(sum(q[2] for q in f['paraf'])-e['N'])*1000*(200-203/2)*2/(300*250)))
        self.conf(ch['EspNec'],max(ch['EspNecT'],ch['EspNecC']))
        m12 = p.verificar_parafuso_placa(f['Tmax'],f['Fv'],12,'8.8')
        self.assertLess(m12['NComb'],b['NComb'])

    def test_9_2_guincho_eixos(self):
        saidas, reac = guincho()
        e, ext = saidas[2]
        self.conf(e['V'],abs(reac[9]))
        self.conf(e['N'],-reac[10])
        self.conf(e['M'],-reac[11])
        self.assertGreater(e['V'],1)
        self.assertLess(e['V_barra'],.1)
        d = dict(D,chumbador='Parabolt PB 1/4" hef 35 mm',pelo_perfil=True,rotativa=True,seguranca_modelo=1.5)
        r = p.verificar_placa_base(d,e,TR)
        # Corte igual em todas as fixações é regra do legado; o método NBR usa atrito/2 efetivos.
        f = r['legado']['forcas']
        self.conf(f['Fv']*len(f['paraf']),abs(e['Rx']))
        if f['modelo'].startswith('Todos'):
            self.conf(sum(x[2] for x in f['paraf']),e['N'])
        else:
            self.assertGreaterEqual(sum(x[2] for x in f['paraf']),e['N']-1e-9)
        self.assertTrue(e['inclinada'])
        self.assertFalse(r['passa_legado'])
        self.assertTrue(any('inclinada' in a for a in r['avisos']))
        vertical = saidas[0][0]
        for a,b in (('N','N_barra'),('V','V_barra'),('M','M_barra')):
            self.assertLess(abs(vertical[a]-vertical[b]),1e-9)
        self.assertFalse(vertical['inclinada'])
        parede = p.esforcos_placa(dict(F=[0,3,6,0,-3,0],C=1,S=0),(2,0))
        self.assertFalse(parede['inclinada'])
        self.assertLess(abs(parede['N']+parede['Rx']),1e-9)
        self.conf(parede['V'],3)

    def test_9_2_coeficientes(self):
        r = p.coeficientes_chumbador(dict(rotativa=True))
        self.assertTrue(r['tipo_auto'] and r['risco_alto'])
        self.assertEqual((r['tipo_carga'],r['NExig']),(2,15))
        r = p.coeficientes_chumbador(dict(rotativa=True,risco='baixo'))
        self.assertEqual(r['NExig'],7)
        self.assertFalse(r['risco_auto'])
        r = p.coeficientes_chumbador({})
        self.assertEqual((r['tipo_carga'],r['risco_alto'],r['NExig']),(1,False,4))
        self.assertTrue(any('risco' in a for a in r['avisos']))
        r = p.coeficientes_chumbador(dict(rotativa=True,tipo_carga='dinamica',coef_chumbador=3))
        self.assertEqual(r['NExig'],3)
        self.assertTrue(r['avisos'])

    def test_9_3_catalogo_cones(self):
        self.assertEqual(len(p.carregar_chumbadores()),76)
        cb = p.achar_chumbador('CB 12300')
        self.assertIsNotNone(cb)
        self.assertEqual(cb,p.achar_chumbador('Walsywa CB 1/2" hef 76 mm'))
        d,e,r = base_guincho()
        ch = r['fixacao']
        f = r['forcas']
        wb = p.achar_chumbador(d['chumbador'])
        if sum(q[2]>1e-9 for q in f['paraf']) == 4:
            lado = r['geometria']['espac_min']*1000+444
            self.conf(ch['psi_cone'],lado**2/(4*9*148**2),1e-9)
        self.assertLess(r['geometria']['espac_min'],.444)
        self.conf(r['geometria']['espac_min'],.220)
        self.assertLess(ch['psi_cone'],1)
        self.assertTrue(ch['espac_ok'])
        self.conf(ch['TuEf'],wb['tu_kgf']*.00980665*ch['psi_cone'],1e-12)
        self.conf(ch['NComb'],((f['Tmax']/(wb['tu_kgf']*.00980665*ch['psi_cone']))**(5/3)+(f['Fv']/(wb['vu_kgf']*.00980665))**(5/3))**(-3/5),1e-12)
        self.conf(p.fator_cones(wb,[(-.3,0,1),(.3,0,1)]),1,1e-12)
        antes = copy.deepcopy(d)
        escolha = p.dimensionar_chumbador(d,e,TC)
        self.assertTrue(escolha['descricao'])
        self.assertTrue(escolha['resultado']['fixacao']['ChOk'])
        self.assertEqual(d,antes)

    def test_9_3_quimicos_guincho(self):
        d,e,r = base_guincho('WQE 500 5/8"')
        q = r['fixacao']
        self.conf(q['FFck'],math.sqrt(20/28),1e-12)
        self.conf(q['TuEf'],6595*.00980665*math.sqrt(20/28)*q['psi_cone'],1e-12)
        self.conf(q['NAco'],225/math.sqrt((q['Tmax']*1000/145.8)**2+3*(q['Fv']*1000/130.3)**2),1e-12)
        d,e,r = base_guincho('WQA 1/2"',.1)
        q = r['fixacao']
        self.conf(q['NRef'],1,1e-12)
        self.conf(q['Tref']/q['psi_cone'],1019.72*.00980665,1e-12)

    def test_9_3_planilha(self):
        for codigo,t,v,n,na,tol in (
            ('WQE 500 5/8"',3.9677,.9289,13.78,7.53,5e-4),
            ('WQA 1/2"',2.4770,1.8578,3.08,4.68,2e-3),
            ('CBPL 14300',3.9677,.9289,3.786,None,2e-4)):
            with self.subTest(codigo=codigo):
                r = p.avaliar_chumbador(codigo,dict(classe='8.8'),dict(NExig=15,NSeg=1.5,Tmax=t,Fv=v,paraf=[(0,0,t)],espac_min=0))
                self.conf(r['NComb'],n,tol)
                if na is not None:
                    self.conf(r['NAco'],na,tol)
                if codigo.startswith('WQE'):
                    self.assertEqual(r['classe_barra'],'4.6')
                    self.assertEqual(r['SpBarra']/1000,225)
                if codigo.startswith('WQA'):
                    self.assertEqual(r['NRef'],1)

    def test_9_4_solda_diagnostico(self):
        e = dict(N=-8.87,V=.5,M=2.32)
        d = dict(solda=.003,eletrodo='E70',esp=.008)
        self.conf(p.perna_minima_norma(.0073),.005)
        r = p.verificar_solda_contorno(TC,d,e)
        self.assertLess(r['Ap'],1)
        self.assertTrue(r['abaixo_minimo'])
        self.assertFalse(r['passa'])
        self.assertGreaterEqual(p.perna_minima_contorno(TC,d,e),.005)
        self.conf(r['NEquiv'],.577*(145/.30)/(r['tau']/1000),1e-9)
        dg = p.diagnosticar_solda(TC,d,e)
        self.assertFalse(dg['passa'])
        self.assertEqual(dg['perna'],.005)
        self.assertEqual(dg['eletrodo'],'E70')
        self.assertIn('mínimo',dg['porque'])
        self.assertTrue(p.verificar_solda_contorno(TC,dict(d,solda=dg['perna'],eletrodo=dg['eletrodo']),e)['passa'])
        fino = dict(h=.03,b=.02,tw=.00106,tf=.00106,tipo='TR')
        dg = p.diagnosticar_solda(fino,dict(d,esp=.00635),dict(N=.5,V=.3,M=.05))
        self.assertTrue(dg['sem_solucao'])
        self.assertLess(abs(dg['perna_max']*1000-1.06),1e-9)
        self.assertIn('1,06',dg['porque'])
        dg = p.diagnosticar_solda(TC,dict(d,solda=.005),e)
        self.assertTrue(dg['passa'])
        self.assertEqual(dg['o_que_fazer'],'')

    def test_9_5_metadados(self):
        self.assertEqual(len(fx.carregar_linhas()),12)
        self.assertEqual(len(fx.carregar_pares()),11)
        self.assertEqual(len(fx.informativas()),3)
        for linha in dict.fromkeys(c['linha'] for c in p.carregar_chumbadores()):
            with self.subTest(linha=linha):
                info = fx.achar_linha(linha)
                self.assertIsNotNone(info)
                self.assertFalse(info['informativa'])
                self.assertTrue(info['familia'])
        self.assertTrue(fx.achar_linha('Parabolt PB')['sem_catalogo'])
        for linha in ('Walsywa WLT','Walsywa Golpe de Metal','Walsywa PU 500'):
            self.assertTrue(fx.achar_linha(linha)['informativa'])

    def test_9_5_comparacao(self):
        d,e,r = base_guincho('CB 12300')
        base = dict(r['forcas'],espac_min=r['geometria']['espac_min'],NExig=15,NSeg=1.5,tem_esforcos=True)
        codigos = ['CB 12300','WB 12400','WQE 500 1/2"','WQA 1/2"','PB 1/2" hef 80']
        cols = []
        for cod in codigos:
            ch = p.achar_chumbador(cod)
            esperado = p.avaliar_chumbador(ch,d,base)
            col = fx.avaliar_fixacao(cod,base,d)
            cols.append(col)
            for campo in ('NComb','Tref','ApFix')+ (('NAco',) if ch['quimico'] else ()):
                self.conf(col['resultado'][campo],esperado[campo],1e-12)
        inf = fx.avaliar_fixacao('Walsywa WLT',base,d)
        self.assertTrue(inf['info']['informativa'])
        self.assertIsNone(inf['resultado'])
        pares = fx.pares_para(['Walsywa CB','Walsywa WB','Walsywa WQE 500'])
        self.assertTrue(any('Walsywa CB × Walsywa WB' in a['titulo'] for a in pares))
        self.assertTrue(any('Mecânico por torque' in a['titulo'] for a in pares))
        av = fx.avisos_comparacao(cols[:3],base)
        self.assertGreaterEqual(len(av),3)
        for texto in ('Ruptura','28 MPa','cisalhamento'):
            self.assertTrue(any(texto in a for a in av))


class Bordas(unittest.TestCase):
    def test_validacao_agregada(self):
        with self.assertRaisesRegex(ValueError,'H:.*B:'):
            p.geometria_placa(dict(D,H=-1,B=0),W)
        with self.assertRaisesRegex(ValueError,'N:.*V:.*M:'):
            p.forcas_fixacoes(float('nan'),-1,float('inf'),[(0,0)],.4,.2)
        with self.assertRaisesRegex(ValueError,'diametro_mm:.*classe:'):
            p.verificar_parafuso_placa(1,1,13,'x')
        with self.assertRaises(ValueError):
            p.verificar_solda_contorno(TC,dict(solda=0),dict(N=0,V=0,M=0))

    def test_n2_teto_parede_limiar(self):
        e = p.esforcos_placa(dict(F=[0,0,0,10,3,4],C=0,S=1,n2=True),(0,-3))
        self.assertEqual(e['normal'],(0,-1))
        self.assertEqual((e['N'],e['V'],e['M']),(10,3,-4))
        self.assertTrue(e['espelhar'] is False)
        for ang, esperado in ((1.999,False),(2.001,True)):
            a = math.radians(ang)
            e = p.esforcos_placa(dict(F=[0]*6),(math.sin(a),math.cos(a)))
            self.assertEqual(e['inclinada'],esperado)
        e = p.esforcos_placa(dict(F=[0]*6,cargas_no_no=True),(-1,0))
        self.assertEqual(e['normal'],(-1,0))
        self.assertTrue(e['avisos'])

    def test_contorno_compressao_pivo(self):
        g = p.geometria_placa(dict(D,fileiras=3,por_fileira=3),W)
        self.assertEqual(len(g['coordenadas']),8)
        self.assertNotIn((0,0),g['coordenadas'])
        r = p.forcas_fixacoes(-100,0,0,g['coordenadas'],.4,.203)
        self.assertEqual(r['Tmax'],0)
        self.assertEqual(r['modelo'],'Sem tração')
        r = p.forcas_fixacoes(0,10,30,g['coordenadas'],.4,.203,True)
        self.assertEqual(r['pivo_y'],.203/2)
        r = p.forcas_fixacoes(100,0,0,g['coordenadas'],.4,.203)
        self.assertIsNone(r['pivo_y'])

    def test_cones_sem_tracao_6d_ausencia_corte(self):
        c = p.achar_chumbador('WQE 500 5/8"')
        pts = [(-.11,-.11,0),(-.11,.11,0),(.11,-.11,0),(.11,.11,0)]
        self.assertEqual(p.fator_cones(c,pts),p.fator_cones(c,[(y,z,1) for y,z,t in pts]))
        for espac, esperado in ((6*c['rosca_mm']/1000,True),(6*c['rosca_mm']/1000-1e-5,False)):
            r = p.avaliar_chumbador(c,{},dict(Tmax=1,Fv=1,paraf=pts,espac_min=espac))
            self.assertEqual(r['espac_ok'],esperado)
            self.assertIsNone(r['Vref'])
            self.assertIsNone(r['V_admissivel'])
            self.assertTrue(any('cisalhamento' in a for a in r['avisos']))
        r = p.avaliar_chumbador(c,{},dict(Tmax=0,Fv=0))
        self.assertTrue(math.isinf(r['NComb']))
        json.dumps(p.para_json(r),allow_nan=False)

    def test_limites_perna_dimensionamento_sem_mutacao(self):
        for t,esperado in ((0,0),(.00635,.003),(.0063501,.005),(.0125,.005),(.0125001,.006),(.019,.006),(.0191,.008)):
            self.assertEqual(p.perna_minima_norma(t),esperado)
        fino = dict(h=.03,b=.02,tw=.00106,tipo='TR')
        e = dict(N=.5,V=.3,M=.05)
        self.assertGreater(p.perna_minima_contorno(fino,dict(esp=.00635),e),fino['tw'])
        d = copy.deepcopy(D)
        antes = copy.deepcopy(d)
        r = p.dimensionar_chumbador(d,dict(N=1e12,V=1e12,M=1e12),W)
        self.assertEqual(r['descricao'],'')
        self.assertIsNone(r['resultado'])
        self.assertEqual(d,antes)

    def test_agregacao_imutabilidade_comparacao(self):
        d,e,r = base_guincho()
        self.assertIsNotNone(r['solda'])
        antes_d, antes_e, antes_r = copy.deepcopy(d), copy.deepcopy(e), copy.deepcopy(r)
        p.verificar_placa_base(d,e,TC)
        fx.comparar_fixacoes(['CB 12300','WQI 44 1/2"'],r,d)
        self.assertEqual((d,e,r),(antes_d,antes_e,antes_r))
        sem_locais = {k:e[k] for k in ('N','V','M')}
        sem = p.verificar_placa_base(d,sem_locais,TC)
        self.assertIsNone(sem['solda'])
        self.assertFalse(sem['ok'])
        fino = p.verificar_placa_base(dict(d,esp=.00001),e,TC)
        self.assertFalse(fino['ok'])
        self.assertGreater(fino['taxa_max'],1)
        self.assertEqual(fino['criterio'],'chapa: espessura')
        self.assertEqual(fino['passa_legado'],fino['legado']['fixacao']['ChOk'])
        inc = p.verificar_placa_base(d,dict(e,inclinada=True),TC)
        self.assertFalse(inc['ok'])
        self.assertFalse(inc['passa_legado'])
        self.assertIsNone(inc['solda'])

    def test_copias_catalogos(self):
        origem = Path(__file__).resolve().parents[2]/'portico'
        for nome in ('chumbadores.csv','fixacoes.csv','fixacoes-pares.csv'):
            self.assertEqual(hashlib.sha256((origem/nome).read_bytes()).digest(),hashlib.sha256((p._DADOS/nome).read_bytes()).digest())


if __name__ == '__main__':
    unittest.main()
