"""Verificação independente do porte placa_base/fixacoes contra Nucleo.cs/Solda.cs.

Valores esperados obtidos executando o C# legado (harness compilado com
Nucleo.cs + Solda.cs) ou derivados à mão das linhas citadas. Testes V-PB-xx protegem a compatibilidade com o legado.
"""
import json
import math
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from prossen_estrutural.aco import placa_base as p
from prossen_estrutural.aco import fixacoes as fx

TC = dict(h=.1143, b=.1143, tw=.0095, tf=.0095, tipo='TC')


class Divergencias(unittest.TestCase):
    def test_V_PB_01_pelo_perfil_arredondamento_binario(self):
        # N:1104: ceil((100 + 2·100)/10)·10 = 300 mm. Python: ceil(0.30000000000000004·100) → 310.
        g = p.geometria_placa(dict(pelo_perfil=True, sobra=.1, borda=.05), dict(h=.1, b=.1, tw=.01, tf=.01, tipo='I'))
        self.assertAlmostEqual(g['H'], .300, places=9)

    def test_V_PB_02_dimensionar_prefere_menos_fixacoes(self):
        # N:1258–1261: laço externo é o arranjo (2x2 antes de 2x3/3x3), interno o catálogo.
        # C# (guincho-coluna-escorada.estructto, nó 1): Parabolt PB 1/4" hef 35 mm, 2x2.
        dados = dict(chumbador='Parabolt PB 1/4" hef 35 mm', pelo_perfil=True, sobra=.1, esp=.00635, borda=.05,
                     fileiras=2, por_fileira=2, seguranca_modelo=1.5, rosca_no_corte=False)
        esf = dict(N=-9.016007805999635, V=1.7753350745945236, M=2.1684498810901958)
        r = p.dimensionar_chumbador(dados, esf, TC, metodo='legado')
        self.assertEqual((r['dados']['fileiras'], r['dados']['por_fileira']), (2, 2))
        self.assertEqual(r['resultado']['fixacao']['chumbador']['nome'], 'Parabolt PB 1/4" hef 35 mm')

    def test_V_PB_03_esforcos_padrao_CS_no_N2(self):
        # Barra N1=(0,3) → N2=(0,0), placa no N2. C/S da barra = (0,-1); docstring diz que C/S
        # orientam N1→N2, mas o padrão usa a direção saindo do nó (0,+1): sinal de N invertido.
        F = [0, 0, 0, 50, 0, 0]
        explicito = p.esforcos_placa(dict(F=F, C=0, S=-1, n2=True), (0, 3))
        padrao = p.esforcos_placa(dict(F=F, n2=True), (0, 3))
        self.assertAlmostEqual(padrao['N'], explicito['N'])

    def test_V_PB_04_aviso_fck_falso_com_linha_informativa(self):
        # N:2291–2295 descarta 'não aplicável' e compara fck formatado; CB (20) × PU 500 não gera aviso de concreto.
        av = fx.avisos_comparacao([fx.avaliar_fixacao('Walsywa CB'), fx.avaliar_fixacao('Walsywa PU 500')])
        self.assertFalse(any('Concreto de ensaio' in a for a in av))

    def test_V_PB_05_comparacao_herda_NExig_da_placa(self):
        for chumbador, esperado in (('', 1.5), ('Walsywa CB', 7)):
            with self.subTest(chumbador=chumbador):
                c = next(c for c in p.carregar_chumbadores() if c['linha'] == 'Walsywa CB')
                dados = dict(seguranca_modelo=1.5, coef_chumbador=7,
                             chumbador=c['codigo'] or c['nome'] if chumbador else '')
                base = p.verificar_placa_base(dados, dict(N=4, V=2, M=0), TC)
                self.assertEqual(base['intermediarios']['coeficientes']['NExig'], esperado)
                candidato = fx.avaliar_fixacao(c, base, dict(coef_chumbador=15))['resultado']
                self.assertEqual(candidato['NExig'], esperado)
                self.assertEqual(candidato['NRef'], esperado)
                self.assertEqual(candidato['NSeg'], 1.5)

    def test_V_PB_06_tensoes_chumbador_mecanico(self):
        c = next(c for c in p.carregar_chumbadores() if c['linha'] == 'Walsywa CB')
        ad = math.pi*c['rosca_mm']**2/4
        for rosca_no_corte in (True, False):
            with self.subTest(rosca_no_corte=rosca_no_corte):
                r = p.avaliar_chumbador(c, dict(rosca_no_corte=rosca_no_corte),
                                       dict(Tmax=4, Fv=2))
                self.assertAlmostEqual(r['sigma'], 4e6/ad)
                self.assertAlmostEqual(r['tau'], 2e6/ad)
                self.assertTrue(math.isinf(r['NAco']))


class Protecao(unittest.TestCase):
    def test_parafuso_balanco_valores_fechados(self):
        # T:162–171, à mão: 2x2 M16 8.8, M=30 kN·m, H=400, borda 50 → d=0,35/0,05 m, a=0,2 m.
        # k = 30/(2·0,35²+2·0,05²) = 120 kN/m → T = 42 kN; σ = 42000/157 MPa; Fi = 0,75·157·600/1000.
        f = p.forcas_fixacoes(0, 10, 30, [(-.15, -.1), (-.15, .1), (.15, -.1), (.15, .1)], .4, .203)
        self.assertAlmostEqual(f['Tmax'], 42, places=9)
        r = p.verificar_parafuso_placa(f['Tmax'], f['Fv'], 16, '8.8', True, 2)
        self.assertAlmostEqual(r['NComb'], 600/math.hypot(42000/157, math.sqrt(3)*2500/144), places=9)
        self.assertAlmostEqual(r['torque_nm'], 226.08, places=9)

    def test_solda_circular_shigley(self):
        # S:52–67: r=57,15 mm, h=5: A=1,414·π·5·r; I=0,707·5·π·r³; τ2 = N/A + M·c/I.
        r = p.verificar_solda_contorno(dict(h=.1143, b=.1143, tw=.0073, tf=.0073, tipo='TC'),
                                       dict(solda=.005, esp=.008), dict(N=-8.87, V=.5, M=2.32))
        raio = 57.15
        A, I = 1.414*math.pi*5*raio, .707*5*math.pi*raio**3
        tau = math.hypot(500/A, 8870/A + 2.32e6*raio/I)
        self.assertAlmostEqual(r['tau']/1000, tau, places=9)
        self.assertAlmostEqual(r['Ap'], tau/145, places=12)


W310_52 = dict(h=.317, b=.167, tw=.0076, tf=.0132, tipo='I')     # Gerdau: d = 317, bf = 167
W310_387 = dict(h=.310, b=.165, tw=.0058, tf=.0097, tipo='I')    # d = 310, bf = 165
W250_73 = dict(h=.253, b=.254, tw=.0086, tf=.0142, tipo='I')     # d = 253, bf = 254
CS300_62 = dict(h=.300, b=.300, tw=.008, tf=.0095, tipo='I')     # Pfeil 9.10.5: d = bf = 300


def perto(tc, obtido, esperado, tol=.01):
    tc.assertLessEqual(abs(obtido/esperado-1), tol, f'{obtido!r} × {esperado!r}')


class MetodoNBR(unittest.TestCase):
    """Placa de base v2 (docs/v3/PLACA-BASE-V2.md). Esforços de cálculo; tolerância 1%."""

    def test_bloco_de_compressao_tracao_3_3x(self):
        # AISC-DG1.md §4.1.1: N = −500, M = 150, placa 0,50 × 0,40, 4 chumbadores em y = ±0,20, fck 20.
        pts = [(-.2, -.15), (-.2, .15), (.2, -.15), (.2, .15)]
        q = 20000/1.96*.4
        Y = .45-math.sqrt(.45**2-2*(150+500*.2)/q)
        f = p.forcas_bloco(-500, 150, pts, .5, .4, 20000/1.96)
        self.assertEqual(f['modelo'], 'Bloco de compressão')
        self.assertAlmostEqual(f['Y'], Y, places=12)
        self.assertAlmostEqual(f['T_total'], q*Y-500, places=9)
        perto(self, f['T_total'], 182.4, .002)
        self.assertEqual([t for y, z, t in f['paraf'] if y > 0], [0, 0])   # lado comprimido sem tração
        self.assertAlmostEqual(f['C']*(.25-Y/2)+f['T_total']*.2, 150, places=9)   # momentos no centro
        leg = p.forcas_fixacoes(-500, 0, 150, pts, .5, .2)
        self.assertGreater(f['Tmax']/leg['Tmax'], 3.3)

    def test_compressao_sem_tracao_e_esmagamento(self):
        pts = [(-.2, -.15), (-.2, .15), (.2, -.15), (.2, .15)]
        f = p.forcas_bloco(-500, 20, pts, .5, .4, 20000/1.96)
        self.assertEqual(f['modelo'], 'Compressão sem tração')
        self.assertEqual(f['Tmax'], 0)
        self.assertAlmostEqual(f['fp'], 500/((.5-2*.04)*.4))
        f = p.forcas_bloco(-5000, 0, pts, .5, .4, 20000/1.96)
        self.assertGreater(f['taxa_concreto'], 1)
        f = p.forcas_bloco(100, 0, pts, .5, .4, 20000/1.96)
        self.assertEqual((f['modelo'], f['Tmax']), ('Todos tracionados', 25))

    def test_cbca_pbr_tabela_p219(self):
        # CBCA Manual de Ligações, PBR p. 204 (fórmulas) e p. 219 (tabela); t = √[6·Mmax/(1,25·fy/1,1)].
        for sec, H, B, t, Nd, bh, bb in ((W310_52, .45, .30, 31.5, 1833, .65, .45),
                                        (W310_52, .35, .25, 19.0, 883, .45, .35),
                                        (W310_387, .35, .30, 31.5, 1398, .45, .50)):
            with self.subTest(H=H, B=B, Nd=Nd):
                d = dict(H=H, B=B, esp=t/1000, borda=.075, fileiras=1, por_fileira=2, diametro_mm=25,
                         classe='4.6', bloco_H=bh, bloco_B=bb)
                r = p.verificar_placa_base(d, dict(N=-Nd, V=0, M=0), sec)
                perto(self, r['chapa']['t_cbca']*1000, t)
                self.assertEqual(r['chapa']['metodo'], 'CBCA (1,25·W)')
                # A2 homotético (NBR 6.6.5) < área do bloco que o CBCA usa: a linha W310×38,7
                # fica 1,5% acima no concreto (PLACA-BASE-V2.md §5). A chapa governa a tabela.
                self.assertLessEqual(r['concreto']['taxa'], 1.02)
                self.assertEqual(r['forcas']['Tmax'], 0)

    def test_cbca_pbe_tabela_p266(self):
        # CBCA PBE p. 236 (caso 1) e p. 266: W250×73, placa 500 × 350 × 50: (1966; 49,6) e (1843; 62,0).
        for Nd, Md in ((1966, 49.6), (1843, 62.0)):
            with self.subTest(Nd=Nd):
                d = dict(H=.5, B=.35, esp=.05, borda=.05, fileiras=2, por_fileira=2, diametro_mm=25,
                         classe='4.6', bloco_H=.8, bloco_B=.65)
                r = p.verificar_placa_base(d, dict(N=-Nd, V=0, M=Md), W250_73)
                self.assertEqual(r['chapa']['pressao_cbca']['caso'], 1)
                perto(self, r['chapa']['t_cbca']*1000, 50.0)
                perto(self, r['chapa']['Mmax_c'], 118.35, .002)   # kN·m/m = 118,35 kN·cm/cm

    def test_pfeil_9_10_5(self):
        # Pfeil & Pfeil, Probl. 9.10.5 (p. 260–261): Nd 1120, Hd 84, placa 35 × 35, fck 20, SAE 1020.
        d = dict(H=.35, B=.35, esp=.02, borda=.06, fileiras=1, por_fileira=2, diametro_mm=22, classe='4.6',
                 fub_chumbador=390000, atrito=0)
        r = p.verificar_placa_base(d, dict(N=-1120, V=84, M=0), CS300_62)
        perto(self, r['concreto']['A1_nec']*1e4, 1098, .001)
        perto(self, r['chapa']['t_pfeil']*100, 1.79)
        perto(self, r['fixacao']['Ag_nec_corte']*1e4, 3.63, .002)
        self.assertEqual(r['cisalhamento']['mecanismo'], 'chumbadores')
        corte = next(v for v in r['verificacoes'] if v['nome'] == 'chumbador: corte na haste')
        self.assertLess(corte['taxa'], 1)   # 2 Ø22 adotados no livro
        self.assertIsNone(r['legado'])      # Ø22 fora da tabela métrica do legado
        self.assertTrue(any('legado' in a for a in r['avisos']))

    def test_sem_coeficiente_global(self):
        d = dict(H=.5, B=.4, esp=.025, borda=.05, diametro_mm=20, classe='8.8')
        e = dict(N=-200, V=10, M=60)
        a = p.verificar_placa_base(dict(d, seguranca_modelo=1.5), e, W310_52)
        b = p.verificar_placa_base(dict(d, seguranca_modelo=3), e, W310_52)
        self.assertEqual([v['taxa'] for v in a['verificacoes']], [v['taxa'] for v in b['verificacoes']])
        self.assertNotEqual(a['legado']['chapa']['EspNec'], b['legado']['chapa']['EspNec'])

    def test_ordem_do_corte(self):
        d = dict(H=.5, B=.4, esp=.025, borda=.05, fileiras=2, por_fileira=3, diametro_mm=20, classe='8.8')
        r = p.verificar_placa_base(d, dict(N=-200, V=15, M=0), W310_52)
        self.assertEqual((r['cisalhamento']['mecanismo'], r['forcas']['Fv']), ('atrito', 0))
        r = p.verificar_placa_base(d, dict(N=-200, V=40, M=0), W310_52)
        self.assertEqual(r['cisalhamento']['n_ef'], 2)
        self.assertAlmostEqual(r['forcas']['Fv'], 20)
        r = p.verificar_placa_base(dict(d, arruela_soldada=True), dict(N=-200, V=40, M=0), W310_52)
        self.assertAlmostEqual(r['forcas']['Fv'], 40/6)
        r = p.verificar_placa_base(dict(d, lug_b=.1, lug_t=.025, lug_h=.1, graute=.03), dict(N=-200, V=40, M=0), W310_52)
        self.assertEqual(r['cisalhamento']['mecanismo'], 'barra de cisalhamento')
        flex = next(v for v in r['verificacoes'] if v['nome'] == 'corte: barra — flexão')
        self.assertAlmostEqual(flex['solicitante'], 40*(.03+.05))
        self.assertAlmostEqual(flex['resistente'], 1.25*.1*.025**2/6*250000/1.1)

    def test_aci_318_cap17_a_mao(self):
        # Ø20, hef 200, fck 25 (√f'c = 5), fissurado. Bloco 0,30 (y) × 2,0 (z): ca = 150 mm em y.
        nb = 10*5*200**1.5
        ncb = 300*600/(9*200**2)*(.7+.3*150/300)*nb          # ANc/ANco·ψed·Nb
        npn = 8*1.163*20**2*25                              # 8·Abrg·f'c
        vb = 3.7*5*150**1.5                                 # 17.7.2.2.1: o termo 3,7 governa
        r = p.concreto_chumbador_aci([(0, 0, 30)], 10, 1, .2, .02, 1.163*.02**2, 25000, True, (.3, 2., 0))
        v = {x['nome']: x for x in r['verificacoes']}
        self.assertAlmostEqual(v['concreto: cone de tração']['resistente'], .7*ncb/1000, places=9)
        self.assertAlmostEqual(v['concreto: arrancamento da cabeça']['resistente'], .7*npn/1000, places=9)
        self.assertAlmostEqual(v['concreto: ruptura da borda ao corte']['resistente'], .7*vb/1000, places=9)
        self.assertAlmostEqual(v['concreto: arrancamento por alavanca (pryout)']['resistente'], .7*2*ncb/1000, places=9)
        it = (30/(.7*ncb/1000)+10/(.7*vb/1000))/1.2
        self.assertAlmostEqual(v['concreto: interação tração–corte']['taxa'], it, places=9)
        self.assertNotIn('concreto: fendilhamento lateral', v)   # hef ≤ 2,5·ca1
        r = p.concreto_chumbador_aci([(0, 0, 30)], 0, 1, .2, .02, 1.163*.02**2, 25000, True, (.15, 2., 0))
        self.assertIn('concreto: fendilhamento lateral', {x['nome'] for x in r['verificacoes']})

    def test_guincho_v2_equilibrio_e_json(self):
        from test_placa_base import base_guincho
        d, e, r = base_guincho()
        f = r['forcas']
        self.assertEqual(f['modelo'], 'Bloco de compressão')
        self.assertAlmostEqual(f['C']-f['T_total'], -e['N'], places=9)
        self.assertAlmostEqual(f['C']*(r['geometria']['H']-f['Y'])/2+f['T_total']*f['f'], abs(e['M']), places=9)
        self.assertGreater(f['Tmax'], r['legado']['forcas']['Tmax'])
        self.assertEqual(sum(t > 0 for y, z, t in f['paraf']), 2)
        json.dumps(p.para_json(r), allow_nan=False)


if __name__ == '__main__':
    unittest.main()
