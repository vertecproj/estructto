"""Regressões dos achados RC01–RC11 (docs/v3/REVISAO-CODEX-LIGACOES.md), NBR 8800:2008. Valores à mão no teste.

Fontes: ABNT NBR 8800:2008 (6.2.2.2 d, 6.3.3, 6.4, 6.5); [PFEIL] W. Pfeil e M. Pfeil, "Estruturas de aço:
dimensionamento prático", 8ª ed., LTC — eq. 4.3 (p. 100) e problema 9.10.4, pino (p. 258–259).
"""
import math
import unittest

from prossen_estrutural.aco import ligacoes as lg
from prossen_estrutural.aco import ligacoes_contraventamento as C
from prossen_estrutural.aco import ligacoes_modelo as LM
from prossen_estrutural.aco import ligacoes_rigidas as R
from prossen_estrutural.aco import placa_base as P

SEC = dict(tipo="I", h=0.3, b=0.2, tw=0.008, tf=0.012)
FV_A325_19 = 0.4 * math.pi * 19.05 ** 2 / 4 * 825 / 1.35 / 1000        # 6.3.3.2, 1 plano: 69,672 kN
FT_A325_19 = 0.75 * math.pi * 19.05 ** 2 / 4 * 825 / 1.35 / 1000       # 6.3.3.1: 130,636 kN


def linha(r, prefixo):
    achados = [v for v in r["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"'{prefixo}' ausente: {[v['nome'] for v in r['verificacoes']]}"
    return achados[0]


def esf(N):
    return dict(N=N, Vy=0.0, Vz=0.0, T=0.0, My=0.0, Mz=0.0)


class TestRC(unittest.TestCase):
    def test_rc01_compressao_nao_cancela_excentricidade(self):
        # y = ±75 mm, Σy² = 0,01125 m²; M = 100·0,075 = 7,5 kN·m; extremo: 150/3 + 7,5·0,075/0,01125 = 100 kN
        r = lg.verificar_ligacao("chapa_simples_alma", dict(perfil_viga="W360x51", perfil_suporte="W530x92", n=3, s=75,
                                 a=75, e_v=40, dist_topo_furo=75, t_chapa=8, b_solda=5), dict(V=100, N=-150))
        v = linha(r, "parafusos: cisalhamento")
        self.assertAlmostEqual(v["solicitante"], math.hypot(100 / 3, 100), delta=0.01)   # 105,409 kN
        self.assertAlmostEqual(v["resistente"], FV_A325_19, delta=0.01)
        self.assertFalse(r["ok"])

    def test_rc02_chapa_topo_tracao_axial(self):
        # N = 900, M = 0: cada mesa 450 kN; mesa inferior com 2 parafusos A325 φ19,05: 2·130,636 = 261,27 kN
        r = R.verificar_chapa_topo_estendida(dict(perfil_viga=dict(d=400, bf=200, tw=8, tf=12), tp=50, bp=220, g=100,
                                                  pf=40, de=40, b_alma=5, estendida="superior"), dict(N=900, V=0, M=0))
        v = linha(r, "tração axial: linhas da mesa oposta")
        self.assertAlmostEqual(v["solicitante"], 450.0, delta=0.01)
        self.assertAlmostEqual(v["resistente"], 2 * FT_A325_19, delta=0.01)
        self.assertFalse(r["ok"])

    def test_rc03_uma_fileira_vai_ao_bloco(self):
        # σc,Rd = 20/(1,4·1,4) MPa; q = σ·B; Y = H/2 − √[(H/2)² − 2M/q]; T = q·Y + N (equilíbrio)
        r = P.verificar_placa_base(dict(H=0.5, B=0.4, esp=0.05, diametro_mm=24, borda=0.05, solda=0.005, fileiras=1),
                                   dict(N=100, V=0, M=10, N_barra=100, V_barra=0, M_barra=10), SEC)
        q = 20000 / 1.96 * 0.4
        Y = 0.25 - math.sqrt(0.25 ** 2 - 2 * 10 / q)
        f = r["forcas"]
        self.assertEqual(f["modelo"], "Bloco de compressão")
        self.assertAlmostEqual(f["T_total"], q * Y + 100, delta=0.01)      # 140,82 kN ≥ 140 (cota da revisão)
        self.assertAlmostEqual(f["Tmax"], (q * Y + 100) / 2, delta=0.01)
        self.assertAlmostEqual(f["C"] * (0.25 - f["Y"] / 2), 10.0, delta=1e-6)   # ΣM = M em torno da fileira

    def test_rc04_no_trelica_varias_barras_nao_aprova(self):
        d = dict(dim_a_1=100, dim_a_2=100, t_perfil_1=10, t_perfil_2=10, t_gusset=10, secao_2="2L",
                 n_por_linha_1=4, n_por_linha_2=4)
        with self.assertRaisesRegex(ValueError, "N2–N4"):
            LM.verificador("no_trelica_gusset")(None, None, esf(100), d)

    def test_rc05_talas_alma_parcela_axial(self):
        # Aw = 575·8 = 4600, A = 12100 mm² → Nw = 2000·4600/12100 = 760,33 kN; An = 2·(300 − 4·22,55)·4,75 =
        # 1993,1 mm² → 1993,1·400/1,35 = 590,55 kN; Ag·fy/γa1 = 2850·250/1,10 = 647,73 kN
        r = R.verificar_emenda_pilar(dict(perfil_pilar=dict(d=600, bf=300, tw=8, tf=12.5), contato=False,
                                          t_tala_mesa=25, n_por_linha_mesa=6, d_mesa_mm=25.4, e1_mesa=45,
                                          e_mesa_perfil=45, t_tala_alma=4.75, h_tala_alma=300, n_linhas_alma=4,
                                          n_colunas_alma=6, s_alma=75, sh_alma=70, d_alma_mm=19.05,
                                          e_emenda_alma=40, e_tala_alma=40), dict(N=2000))
        rup = linha(r, "talas da alma: tração Nw (ruptura")
        self.assertAlmostEqual(rup["solicitante"], 2000 * 4600 / 12100, delta=0.01)
        self.assertAlmostEqual(rup["resistente"], 1993.1 * 400 / 1.35 / 1000, delta=0.01)
        self.assertAlmostEqual(linha(r, "talas da alma: tração Nw (escoamento")["resistente"], 2850 * 250 / 1.1 / 1000,
                               delta=0.01)
        self.assertFalse(r["ok"])
        # compressão sem contato também chega às talas (6.5.4)
        rc = R.verificar_emenda_pilar(dict(perfil_pilar=dict(d=600, bf=300, tw=8, tf=12.5), contato=False,
                                           t_tala_mesa=25, n_por_linha_mesa=6, d_mesa_mm=25.4, e1_mesa=45,
                                           e_mesa_perfil=45, t_tala_alma=4.75, h_tala_alma=300, n_linhas_alma=4,
                                           n_colunas_alma=6, s_alma=75, sh_alma=70, d_alma_mm=19.05,
                                           e_emenda_alma=40, e_tala_alma=40), dict(N=-2000))
        self.assertAlmostEqual(linha(rc, "talas da alma: compressão Nw")["solicitante"], 2000 * 4600 / 12100,
                               delta=0.01)

    def test_rc06_painel_cortante_invertido(self):
        # W360x51: d − tf = 355 − 11,6 = 343,4 mm; Vp = 20/0,3434 − 1000 = −941,76 kN → |Vp|
        r = R.verificar_painel_alma(dict(perfil_viga="W360x51", perfil_pilar="W310x38.7", V_pilar=1000), dict(M=20))
        v = linha(r, "cisalhamento do painel")
        self.assertAlmostEqual(v["solicitante"], 1000 - 20 / 0.3434, delta=0.01)
        self.assertFalse(r["ok"])

    def test_rc07_beta_filete_longo(self):
        self.assertAlmostEqual(lg.beta_filete_longo(1.0, 0.005), 0.8)     # 1,2 − 0,002·200
        self.assertEqual(lg.beta_filete_longo(0.4, 0.005), 1.0)          # L/b = 80 ≤ 100
        self.assertEqual(lg.beta_filete_longo(5.0, 0.005), 0.6)          # limite inferior
        # 0,6·0,707·5·485/1,35 = 761,99 kN/m; Le = 0,8 m → 650/(0,8·761,99) = 1,0663
        r = lg.verificar_ligacao("solda_filete", dict(b=0.005, L=1, t_base=0.01, eletrodo="E70",
                                 aco_base="A-572 GR.50", L_longitudinal=1), dict(F=650))
        self.assertAlmostEqual(r["taxa_max"], 650 / (0.8 * 0.6 * 0.707 * 5 * 485 / 1.35), delta=1e-4)
        self.assertFalse(r["ok"])

    def test_rc08_pino_6_4(self):
        # ISO 4.6, fy = 235 MPa; d = 20 mm: Fv,Rd = 2·0,6·0,75·314,16·235/1,10 = 60,404 kN (6.4.2.2);
        # M = N·t/8 = 65·0,05/8 = 0,40625 kN·m contra 1,2·785,4·235/1,10 = 0,20135 kN·m (6.4.2.1)
        r = C.verificar_tirante(dict(D_barra=30, extremidade="garfo_pino", d_pino=20, tipo_pino="ISO 4.6", t_chapa=50,
                                     largura_chapa=120, borda_pino=40), dict(N=65))
        self.assertAlmostEqual(linha(r, "pino: cisalhamento")["resistente"], 60.404, delta=0.01)
        fl = linha(r, "pino: flexão")
        self.assertAlmostEqual(fl["solicitante"], 0.40625, delta=1e-5)
        self.assertAlmostEqual(fl["resistente"], 0.20135, delta=1e-4)
        self.assertFalse(r["ok"])
        # [PFEIL] 9.10.4 (p. 259): Nd = 1,4·600 kN, gusset 50,8 mm, orelhas 25,4 mm → M = 1066 kN·cm; d ≥ 7,36 cm
        p = C.pino_garfo(840, 0.08, 250e3, 0.0508, 0.0254)
        self.assertLess(abs(p["M"] * 100 - 1066) / 1066, 0.01)
        d_min = (p["M"] * 32 * 1.1 / (1.2 * math.pi * 250e3)) ** (1 / 3)
        self.assertLess(abs(d_min - 0.0736) / 0.0736, 0.01)

    def test_rc09_furo_na_borda_reprova(self):
        r = P.verificar_placa_base(dict(H=0.5, B=0.4, esp=0.05, diametro_mm=16, borda=0.008, solda=0.005),
                                   dict(N=-100, V=20, M=0, N_barra=-100, V_barra=20, M_barra=0), SEC)
        v = linha(r, "chapa: pressão de contato no furo")     # lf = 8 − 30,3/2 = −7,15 mm
        self.assertEqual(v["resistente"], 0.0)
        self.assertFalse(r["ok"])

    def test_rc10_chumbador_na_face_do_bloco(self):
        # yext = 0,25 − 0,05 = 0,20 m = meia altura do bloco → c1 = 0
        with self.assertRaisesRegex(ValueError, "bloco"):
            P.verificar_placa_base(dict(H=0.5, B=0.4, esp=0.05, diametro_mm=16, borda=0.05, solda=0.005, bloco_H=0.4,
                                        bloco_B=0.6), dict(N=10, V=0, M=0, N_barra=10, V_barra=0, M_barra=0), SEC)

    def test_rc11_ligacao_tracao_recusa_compressao(self):
        d = dict(b=0.1, t=0.006, d_mm=16, n_linhas=1, n_por_linha=2, e1=0.05, s=0.05)
        with self.assertRaisesRegex(ValueError, "compressão"):
            LM.verificador("ligacao_tracao")(None, None, esf(-50), d)
        self.assertTrue(LM.verificador("ligacao_tracao")(None, None, esf(50), d)["ok"])


if __name__ == "__main__":
    unittest.main()
