"""Verificação independente do porte placa_base/fixacoes contra Nucleo.cs/Solda.cs.

Valores esperados obtidos executando o C# legado (harness compilado com
Nucleo.cs + Solda.cs) ou derivados à mão das linhas citadas. Testes V-PB-xx protegem a compatibilidade com o legado.
"""
import math
import unittest

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
        r = p.dimensionar_chumbador(dados, esf, TC)
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


if __name__ == '__main__':
    unittest.main()
