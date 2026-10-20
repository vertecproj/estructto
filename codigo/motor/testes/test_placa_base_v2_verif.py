"""Verificação independente da placa de base v2 (método NBR), 20/10/2026.

Valores esperados derivados à mão (memória nos comentários), sem copiar as fórmulas do código:
- bloco de compressão: equilíbrio de forças e de momentos (AISC DG1 3.3/3.4, σc,Rd da NBR 8800 6.6.5);
- chapa: CBCA (t ≥ √[6M/(1,25·fy/γa1)], CBCA.md:342 e casos 1–4 de CBCA.md:358–362) e Pfeil/DG1 (Z = t²/4);
- ACI 318-19 cap. 17: 17.6.2 (hef' com 3 bordas), 17.6.3, 17.6.4, 17.7.2, 17.7.3, 17.8; φ = 0,70 (condição B).
Unidades do motor: kN, m, kN/m². Testes ``expectedFailure`` marcam achados V2-xx abertos.
"""
import math
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from prossen_estrutural.aco import placa_base as p

SIG20 = 20000/(1.4*1.4)              # NBR 8800 6.6.5, A2 = A1: 10 204 kN/m²
CO4 = [(y, z) for y in (-.2, .2) for z in (-.15, .15)]   # placa 0,50 × 0,40, borda 0,05 → f = 0,20 m
W = dict(h=.3, b=.2, tw=.01, tf=.015)


class Bloco(unittest.TestCase):
    def _equilibrio(self, N, M):
        """ΣFz e ΣM em torno do centro da placa, com as forças devolvidas pelo motor."""
        H, B = .5, .4
        r = p.forcas_bloco(N, M, CO4, H, B, SIG20)
        s = 1 if M >= 0 else -1
        C, Y = r['C'], r['Y']
        T = [t for _, _, t in r['paraf']]
        # ΣFz: C (concreto, para cima) = −N (compressão) + ΣT
        self.assertAlmostEqual(C, -N + sum(T), delta=1e-6*max(1, abs(N)))
        # ΣM: resultante do bloco em y = s·(H/2 − Y/2) (CBCA/DG1: bloco a partir da borda comprimida)
        mc = C*s*(H/2 - Y/2) if C > 0 else 0.
        mt = sum(-t*y for y, _, t in r['paraf'])          # tração em y gera momento −t·y
        self.assertAlmostEqual(mc + mt, M, delta=1e-6*max(1, abs(M)))
        # chumbadores do lado comprimido (s·y > 0) sem tração quando há contato
        if C > 0:
            self.assertTrue(all(t == 0 for y, _, t in r['paraf'] if s*y > 0))
        return r

    def test_regimes_equilibrio(self):
        # e pequeno (sem tração), e grande, compressão no limite, momento puro, tração com momento, só tração
        for N, M in [(-500, 10), (-500, 60), (-500, 150), (-500, -150), (-1500, 100),
                     (0, 30), (200, 60), (100, -40), (200, 10), (200, 0)]:
            with self.subTest(N=N, M=M):
                self._equilibrio(N, M)

    def test_valores_a_mao(self):
        # DG1 §4.1.1 (AISC-DG1.md): q = 10 204·0,40 = 4081,6 kN/m; f + H/2 = 0,45
        # Y = 0,45 − √(0,45² − 2·(150 + 500·0,20)/4081,6) = 0,45 − √0,0800 = 0,16716 m; T = qY − 500 = 182,27 kN
        r = p.forcas_bloco(-500, 150, CO4, .5, .4, SIG20)
        self.assertAlmostEqual(r['Y'], .45 - math.sqrt(.08), places=6)
        self.assertAlmostEqual(r['Tmax'], (4081.6327*(.45 - math.sqrt(.08)) - 500)/2, delta=.01)
        # e = 0,02 ≤ e_crit: Y = H − 2e = 0,46; fp = 500/(0,46·0,40) = 2717,4 kN/m²
        r = p.forcas_bloco(-500, 10, CO4, .5, .4, SIG20)
        self.assertEqual((r['T_total'], round(r['fp'], 1)), (0, 2717.4))
        # N = 200, M = 10: elástico 200/4 ± 10·0,2/(4·0,2²) = 62,5 / 37,5, sem contato
        r = p.forcas_bloco(200, 10, CO4, .5, .4, SIG20)
        self.assertEqual(sorted(round(t, 6) for _, _, t in r['paraf']), [37.5, 37.5, 62.5, 62.5])
        # N = 200, M = 60 (> N·f = 40): bloco; qY²/2 − q·0,45·Y + (60 − 200·0,2) = 0
        q = SIG20*.4
        Y = .45 - math.sqrt(.45**2 - 2*20/q)
        r = p.forcas_bloco(200, 60, CO4, .5, .4, SIG20)
        self.assertAlmostEqual(r['T_total'], q*Y + 200, places=6)

    def test_sigma_c_homotetico_e_teto(self):
        # placa 0,4 × 0,3 em bloco 0,8 × 0,9: k = min(2; 3) = 2 → 2·fck/1,96 = 20 408 > fck → fck
        self.assertAlmostEqual(p.resistencia_concreto(20000, .4, .3, .8, .9)['sigma_cRd'], 20000)
        # bloco 0,6 × 0,6: k = 1,5 → 1,5·10 204 = 15 306 kN/m²
        self.assertAlmostEqual(p.resistencia_concreto(20000, .4, .3, .6, .6)['sigma_cRd'], 1.5*SIG20, places=6)


class Chapa(unittest.TestCase):
    def test_dg1_caso3_a_mao(self):
        # N = −500, M = 150, placa 0,50 × 0,40, W 300 × 200 (tf 15), fy 250 MPa, γa1 1,10.
        # m = (0,50 − 0,95·0,30)/2 = 0,1075; n = (0,40 − 0,8·0,20)/2 = 0,12.
        # CBCA caso 3 (e = 0,30 > H/3): fc = 4·500·(0,30 + 0,20)/(0,5·0,4·(0,5/3 + 0,20)) = 13 636 kN/m²
        fc = 4*500*.5/(.2*(.5/3 + .2))
        Mn = fc*.12**2/2                                   # 98,18 kN·m/m (governa sobre Mm = 67,5)
        fr = .1075/.25                                     # x = H/2 = 0,25 > m
        Mm = .1075**2*(2*fc + fc*(1 - fr))/6
        self.assertLess(Mm, Mn)
        # tração: x = 0,20 − 0,15 + 0,0075 = 0,0575; b = min(0,40; 2·(2x + 0,016)) = 0,262; Trow = 182,27
        Trow = 4081.6327*(.45 - math.sqrt(.08)) - 500
        Mt = Trow*.0575/.262
        fd = 1.25*250000/1.1
        # Pfeil: fp = max(10 204; 0,85·20 000/1,4 = 12 143) → Mn = 12 143·0,12²/2 = 87,43
        Mpf = .85*20000/1.4*.12**2/2
        f = p.forcas_bloco(-500, 150, CO4, .5, .4, SIG20)
        r = p.espessura_placa_nbr(f, .5, .4, W, 16)
        self.assertAlmostEqual(r['t_cbca'], math.sqrt(6*Mn/fd), places=5)      # 45,54 mm
        self.assertAlmostEqual(r['EspNecT'], math.sqrt(6*Mt/fd), delta=1e-5)   # 29,07 mm
        self.assertAlmostEqual(r['t_pfeil'], math.sqrt(4*Mpf*1.1/250000), places=5)  # 39,23 mm

    def test_caso1_trapezio(self):
        # N = −500, M = 40: e = 0,08 < H/6; fmax = 2500·(1 + 6·0,08/0,5) = 4900; Mn = 4900·0,12²/2 = 35,28
        f = p.forcas_bloco(-500, 40, CO4, .5, .4, SIG20)
        r = p.espessura_placa_nbr(f, .5, .4, W, 16)
        self.assertAlmostEqual(r['t_cbca'], math.sqrt(6*35.28/(1.25*250000/1.1)), places=6)

    @unittest.expectedFailure
    def test_V2_01_tracao_entre_mesas(self):
        # Achado V2-01 (CRÍTICO): chumbadores entre as mesas (x = |y| − d/2 + tf/2 ≤ 0) são descartados
        # no lado tracionado (placa_base.py:688); sob arrancamento a chapa sai com EspNec = 0 e passa sem aviso.
        # Limite inferior à mão: faixa biengastada entre mesas, L = d − tf = 0,285 m, P = 30 kN no meio:
        # M = PL/8 = 1,069 kN·m sobre a maior largura possível B = 0,30 → 3,56 kN·m/m → t ≥ 8,67 mm > 6,35.
        s = dict(h=.3, b=.2, tw=.01, tf=.015)
        d = dict(H=.4, B=.3, esp=.00635, borda=.05, fileiras=1, por_fileira=2, diametro_mm=20, classe='5.8')
        r = p.verificar_placa_base(d, dict(N=60., V=0., M=0.), s)
        t_min = math.sqrt(6*(30*.285/8/.3)/(1.25*250000/1.1))
        aviso = any('mesas' in a for a in r['avisos'])
        self.assertTrue(r['chapa']['EspNec'] >= t_min or aviso)


class ACI(unittest.TestCase):
    # 4 Ø20 em (±0,20; ±0,15), T = 50 kN em cada um da fileira y = −0,20; V = 40 kN; n_ef = 2.
    # f'c = 25 MPa (√ = 5), fissurado, hef = 240 mm, Abrg = 1,163·20² = 465,2 mm², bloco 900 × 800 × 600 mm.
    PAR = [(-.2, -.15, 50.), (-.2, .15, 50.), (.2, -.15, 0.), (.2, .15, 0.)]

    def _r(self):
        return p.concreto_chumbador_aci(self.PAR, 40., 2, .24, .02, 1.163*.02**2, 25000, True, (.9, .8, .6))

    def test_tres_bordas_borda_pryout_interacao(self):
        v = {x['nome']: x for x in self._r()['verificacoes']}
        # 17.6.2.1.2: bordas 650, 250, 250, 250 mm; três < 1,5·240 = 360 → hef' = max(250/1,5; 300/3) = 166,7
        h = 250/1.5
        nb = 10*5*h**1.5                                     # 17.6.2.2.1, kc = 10 (SI)
        anc = ((-200 + 1.5*h) - (-450))*800                  # y: [−450; 50] = 500, z: [−400; 400] = 800
        ncbg = anc/(9*h*h)*1.0*nb                            # ψed = 1 (ca,min = 250 = 1,5·hef')
        self.assertAlmostEqual(v['concreto: cone de tração']['resistente'], .7*ncbg/1000, places=6)   # 120,49
        self.assertAlmostEqual(v['concreto: arrancamento da cabeça']['resistente'], .7*8*465.2*25/1000, places=6)
        # 17.7.2, fileira da frente (ca1 = 250): le = min(240; 8·20) = 160; 0,6·8^0,2·√20 = 4,07 > 3,7
        vb = 3.7*5*250**1.5
        avc = 800*375                                        # largura limitada ao bloco × 1,5·ca1 (< ha = 600)
        ped = .7 + .3*250/375
        vcbg = avc/(4.5*250**2)*ped*vb
        self.assertAlmostEqual(v['concreto: ruptura da borda ao corte']['resistente'], .7*vcbg/1000, places=6)  # 49,14
        # 17.7.3: grupo de 4, quatro bordas a 250 → hef' = max(166,7; 500/3) = 166,7; ANc = 900·800; kcp = 2
        ncp = 900*800/(9*h*h)*nb
        self.assertAlmostEqual(v['concreto: arrancamento por alavanca (pryout)']['resistente'], .7*2*ncp/1000, places=6)
        it = (100/(.7*ncbg/1000) + 40/(.7*vcbg/1000))/1.2   # 17.8: ambas as razões > 0,2
        self.assertAlmostEqual(v['concreto: interação tração–corte']['taxa'], it, places=6)   # 1,370

    def test_fendilhamento_grupo(self):
        # 17.6.4: hef = 300 > 2,5·ca1 = 125 (ca1 = 50 mm, bloco H = 500). s = 200 < 6·ca1 = 300 → grupo.
        # ca2 = 900 mm ≥ 3·ca1 → sem fator de canto. Nsb = 13·50·√465,2·5; Nsbg = (1 + 200/300)·Nsb.
        par = [(-.2, -.1, 50.), (-.2, .1, 50.), (.2, -.1, 0.), (.2, .1, 0.)]
        r = p.concreto_chumbador_aci(par, 0., 2, .3, .02, 1.163*.02**2, 25000, True, (.5, 2., 0))
        v = {x['nome']: x for x in r['verificacoes']}
        nsbg = (1 + 200/300)*13*50*math.sqrt(465.2)*5
        self.assertAlmostEqual(v['concreto: fendilhamento lateral']['resistente'], .7*nsbg/1000, places=6)
        self.assertAlmostEqual(v['concreto: fendilhamento lateral']['solicitante'], 100.)

    @unittest.expectedFailure
    def test_V2_02_pryout_so_chumbadores_efetivos(self):
        # Achado V2-02 (CRÍTICO, impacto baixo): com furo alargado só 2 chumbadores resistem ao corte
        # (n_ef = 2), mas o pryout (17.7.3) usa o cone do grupo de 4 (placa_base.py:846). O grupo que
        # sofre alavanca é o dos chumbadores que recebem corte: 2 numa fileira → Ncbg = 172,13 kN
        # (mesmo cone da fileira tracionada acima) → φVcpg = 0,7·2·172,13 = 241,0 kN, não 433,8.
        v = {x['nome']: x for x in self._r()['verificacoes']}
        h = 250/1.5
        ncb2 = 400000/(9*h*h)*10*5*h**1.5
        self.assertLessEqual(v['concreto: arrancamento por alavanca (pryout)']['resistente'], 1.01*.7*2*ncb2/1000)


if __name__ == '__main__':
    unittest.main()
