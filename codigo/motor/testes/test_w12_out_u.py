"""W12-OUT — perfis U (UPN, U laminado, U dobrado, Ue) na placa de base, no gusset de contraventamento e no perfil
soldado em tubo (docs/v3/W12-OUT-U.md).

Contas à mão (mm, kN):

Placa de base, UPN 200 (h 200, b 75, tw 8,5, tf 11,5, xg 22,01 das costas da alma):
  contorno da solda (costas, faces externas, pontas, faces internas das mesas, face interna da alma):
    200 + 2·75 + 2·11,5 + 2·(75 − 8,5) + (200 − 2·11,5) = 200 + 150 + 23 + 133 + 177 = 683 mm.
  momento de inércia do contorno por garganta unitária, em torno do eixo v (flexão no plano da altura):
    costas 2·100³/3 = 666 667; face interna da alma 2·88,5³/3 = 462 103; faces externas 2·75·100² = 1 500 000;
    pontas 2·(100³ − 88,5³)/3 = 204 564; faces internas das mesas 2·66,5·88,5² = 1 041 689 → 3 875 023 mm³; c = 100.
  centroide do contorno na largura (u, de −b/2 nas costas): [200·(−37,5) + 177·(−29) + 2·11,5·37,5 + 2·66,5·4,25]/683
    = −11 205,25/683 = −16,405 mm; centroide do perfil: xg − b/2 = 22,01 − 37,5 = −15,49 mm → e = 0,916 mm.
  excentricidade placa–centroide: |b/2 − xg| = 15,49 mm (placa centrada na caixa do perfil): no eixo fraco
    M_placa = M + |N|·e = 0 + 200·0,01549 = 3,098 kN·m.
Ue 150x60x20x3,00 (t = 3, D = 20): meio contorno 60 + 20 + 3 + 17 + 54 = 154 → 2·154 + 150 (costas) + 144 (alma interna)
  = 602 mm.

Gusset, U dobrado 150x50x3,00 (A 720,4 mm², xg 9,81 mm, t = 3), ½" A325, 2 parafusos s = 50:
  ℓ = 50 mm → Ct = 1 − 1,2·9,81/50 = 0,765 (NBR 14762 9.6.3); 4 parafusos s = 50: ℓ = 150 → 1 − 0,0785 = 0,92 → 0,90.
  contato/rasgamento na parede fina, db/t = 12,7/3 = 4,23 < 10 → C = 3,0; fc = 0,75·3·12,7·3·400/1,65 = 20,78 kN;
  rasgamento do 1º parafuso e1 = 25: 25·3·400/1,65 = 18,18 kN (< fc, governa).

Perfil U em tubo circular Ø 101,6 × 5 (EN 1993-1-8 tabela 7.4), UPN 100 (h1 = 100, b1 = 50), kp = 1:
  β = 50/101,6 = 0,4921; η = 100/101,6 = 0,9843;
  N1,Rd = 345 000·0,005²·(4 + 20·0,4921²)·(1 + 0,25·0,9843) = 8,625·8,8432·1,24608 = 95,05 kN.
  linha da solda do UPN 100: 100 + 2·50 + 2·8,5 + 2·(50 − 6) + (100 − 17) = 388 mm.
"""
import math
import unittest

from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_contraventamento as C
from prossen_estrutural.aco import ligacoes_tubos as T
from prossen_estrutural.aco import placa_base as pb

UPN200 = secoes.catalogo("UPN 200").props
UPN100 = secoes.catalogo("UPN 100").props
UE = secoes.catalogo("Ue 150x60x20x3,00").props
W250 = secoes.catalogo("W250x73").props


def _v(r, inicio):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(inicio))


# ------------------------------------------------------------------------------------------------ placa de base

class TestPlacaBaseU(unittest.TestCase):
    def test_contorno_aberto_683_e_inercia_a_mao(self):
        g = pb._grupo_u(pb._secao(UPN200), False)
        self.assertAlmostEqual(g["L"], 683.0, places=6)
        self.assertAlmostEqual(g["iu"], 3875022.6, delta=1.0)
        self.assertAlmostEqual(g["c"], 100.0, places=6)
        self.assertEqual(g["e"], 0.0)   # eixo forte: o contorno é simétrico em v

    def test_centroide_do_grupo_no_eixo_fraco(self):
        s = pb._secao(UPN200)
        g = pb._grupo_u(s, True)
        self.assertAlmostEqual(g["e"], 16.405 - 15.490, delta=0.002)   # 0,916 mm
        self.assertAlmostEqual(s["xg"], UPN200["xg"])

    def test_u_dobrado_e_ue(self):
        self.assertAlmostEqual(pb._grupo_u(pb._secao(UE), False)["L"], 602.0, places=6)
        dob = pb._secao(secoes.catalogo("U 150x50x3,00").props)   # só t: tw = tf = t
        self.assertEqual((dob["tw"], dob["tf"], dob["D"]), (0.003, 0.003, 0.0))
        # contorno do U dobrado: 150 + 2·50 + 2·3 + 2·(50 − 3) + (150 − 6) = 494 mm
        self.assertAlmostEqual(pb._grupo_u(dob, False)["L"], 494.0, places=6)

    def test_solda_usa_o_contorno_do_u_e_nao_o_retangulo(self):
        dados = dict(pelo_perfil=True, sobra=.1, esp=.019)
        r = pb._solda(UPN200, dados, dict(N=0., V=0., M=10.), 0.006)
        # área = 0,707·h·L e c = 100 mm: τ2 = M·c/(0,707·h·iu), h = 6 mm
        esperado = 10e6 * 100 / (0.707 * 6 * 3875022.6)
        self.assertAlmostEqual(r["tau2"] / 1000, esperado, delta=0.01)
        self.assertAlmostEqual(r["A"], 0.707 * 6 * 683 * 1e-6, places=9)
        self.assertEqual(r["grupo"], "contorno retangular")   # o nome do grupo é o do I (compatível); a conta é a do U

    def test_n_na_excentricidade_do_grupo_no_eixo_fraco(self):
        dados = dict(pelo_perfil=True, sobra=.1, esp=.019, eixo_fraco=True)
        sem = pb._solda(UPN200, dados, dict(N=0., V=0., M=0.), 0.006)
        com = pb._solda(UPN200, dados, dict(N=-200., V=0., M=0.), 0.006)
        s = pb._secao(UPN200)
        g = pb._grupo_u(s, True)
        Mw = 200 * g["e"] * 1e3   # N·mm
        extra = Mw * g["c"] / (0.707 * 6 * g["iu"])
        self.assertAlmostEqual(com["tau2"] / 1000 - sem["tau2"] / 1000 - 200e3 / (0.707 * 6 * g["L"]), extra, delta=1e-6)

    def test_placa_menor_que_o_perfil_recorta_o_contorno(self):
        livre = pb._solda(UPN200, dict(pelo_perfil=True), dict(N=0., V=10., M=0.), 0.006)
        curta = pb._solda(UPN200, dict(pelo_perfil=False, H=0.15, B=0.2, esp=0.019), dict(N=0., V=10., M=0.), 0.006)
        self.assertFalse(livre["recortada"])
        self.assertTrue(curta["recortada"])
        self.assertLess(curta["L"], livre["L"])

    def test_excentricidade_da_placa_no_eixo_fraco(self):
        dados = dict(pelo_perfil=True, sobra=.1, esp=.019, fileiras=2, por_fileira=2, diametro_mm=16, eixo_fraco=True)
        esf = dict(N=-200., V=0., M=0., N_barra=-200., V_barra=0., M_barra=0.)
        r = pb.verificar_placa_base(dados, esf, UPN200)
        e = abs(UPN200["b"] / 2 - UPN200["xg"])
        self.assertAlmostEqual(e, 0.015490, places=6)
        self.assertAlmostEqual(r["forcas"]["M"], 200 * e, places=6)        # 3,098 kN·m
        self.assertEqual(r["esforcos"]["M"], 0.0)                          # o dado de entrada não muda
        self.assertTrue(any("eixo fraco" in a and "centroide" in a for a in r["avisos"]))
        self.assertTrue(any("centro de cisalhamento" in a for a in r["avisos"]))
        self.assertTrue(any("centro de cisalhamento" in a for a in r["solda"]["avisos"]))
        # eixo forte: o momento da placa é o de entrada (a excentricidade é fora do plano, só aviso)
        fo = pb.verificar_placa_base(dict(dados, eixo_fraco=False), dict(esf, M=10.), UPN200)
        self.assertAlmostEqual(fo["forcas"]["M"], 10.0, places=9)
        self.assertTrue(any("eixo forte" in a for a in fo["avisos"]))

    def test_i_e_tubo_nao_mudam(self):
        dados = dict(pelo_perfil=True, sobra=.1, esp=.019, fileiras=2, por_fileira=2, diametro_mm=16)
        r = pb.verificar_placa_base(dados, dict(N=-300., V=20., M=40., N_barra=-300., V_barra=20., M_barra=40.), W250)
        self.assertFalse(any("Pilar U" in a for a in r["avisos"]))
        s = pb._solda(W250, dados, dict(N=-300., V=20., M=40.), 0.006)
        # contorno retangular do I: área 1,414·h·(b + d); c = d/2
        b, d = W250["b"] * 1000, W250["h"] * 1000
        self.assertAlmostEqual(s["A"], 1.414 * 6 * (b + d) * 1e-6, places=9)
        self.assertAlmostEqual(s["intermediarios"]["c"], d / 2 / 1000, places=9)

    def test_sugerir_upn200_n200_m10(self):
        from prossen_estrutural import projeto
        c = dict(dados=dict(pelo_perfil=True, sobra=.1, esp=.019, diametro_mm=16, fileiras=2, por_fileira=2),
                 esforcos=dict(N=-200, V=5, My=10, Mz=0), secao="UPN 200")
        r = projeto.placa_base_dimensionar(None, None, c)
        self.assertTrue(r["achou"])
        v = r["verificacao"]
        self.assertTrue(v["ok"])
        self.assertLessEqual(v["taxa_max"], 0.90)
        self.assertLessEqual(max(p["taxa"] for p in v["passos"].values()), 0.90)
        self.assertLessEqual(v["biaxial"]["eixos"]["fraco (Mz)"], 0.90)


# ------------------------------------------------------------------------------------------------ gusset

class TestGussetU(unittest.TestCase):
    def test_perfil_barra_u_dobrado_so_com_t(self):
        m = C.perfil_barra("U 150x50x3,00")
        self.assertEqual((m["forma"], m["t_perfil"], m["tf_perfil"], m["formado"]), ("U", 3.0, 3.0, True))
        self.assertAlmostEqual(m["xg_perfil"], 9.8077, places=3)
        up = C.perfil_barra("UPN 160")
        self.assertFalse(up["formado"])
        self.assertEqual((up["t_perfil"], up["tf_perfil"]), (7.5, 10.5))
        self.assertIn("U 150x50x3,00", C._PERFIS_BARRA)
        self.assertNotIn("Ue 150x60x20x3,00", C._PERFIS_BARRA)

    def test_ue_recusado_com_mensagem(self):
        with self.assertRaises(ValueError) as e:
            C.perfil_barra("Ue 150x60x20x3,00")
        self.assertIn("U enrijecido", str(e.exception))
        self.assertIn("2L", str(e.exception))

    def _dados(self, **kw):
        base = dict(perfil_diagonal="U 150x50x3,00", t_gusset=6.3, d_mm=12.7, n_por_linha=2, s=50.0, e1=25.0,
                    e_gusset=25.0, eb=150.0, ec=150.0, apoio="viga-pilar", theta=45.0)
        return dict(base, **kw)

    def test_ct_e_ruptura_da_nbr_14762_a_mao(self):
        r = C.verificar_gusset(self._dados(), dict(N=30.0))
        ct = 1 - 1.2 * 9.8077 / 50
        self.assertAlmostEqual(ct, 0.7646, places=3)
        v = _v(r, "barra: ruptura da seção líquida")
        self.assertIn("Ct = 0.765", v["nome"])
        self.assertTrue(v["item"].startswith("NBR 14762:2010 9.6.2 c)"))
        # Rd = Ct·(A − n·dn·t)·fu/1,65, A-36: fu = 400 MPa, dn = dh + 2 mm; ½" A325: dh = 12,7 + 1,5
        A = secoes.catalogo("U 150x50x3,00").props["A"] * 1e6
        dn = 12.7 + 1.5 + 2.0
        self.assertAlmostEqual(v["resistente"], ct * (A - dn * 3.0) * 400 / 1.65 / 1000, delta=0.05)
        # 4 parafusos: ℓ = 150 → 1 − 0,0785 = 0,9215 → teto 0,90
        r4 = C.verificar_gusset(self._dados(n_por_linha=4), dict(N=30.0))
        self.assertIn("Ct = 0.900", _v(r4, "barra: ruptura da seção líquida")["nome"])
        self.assertIsNotNone(_v(r, "barra: ruptura da seção bruta fora da ligação"))

    def test_contato_na_parede_fina_a_mao(self):
        r = C.verificar_gusset(self._dados(), dict(N=30.0))
        v = _v(r, "barra: contato e rasgamento na parede fina")
        fc = 0.75 * 3.0 * 12.7 * 3 * 400 / 1.65 / 1000                  # 20,78 kN
        e1 = 25 * 3 * 400 / 1.65 / 1000                                   # 18,18 kN
        ent = (50 - (12.7 + 1.5) / 2) * 3 * 400 / 1.65 / 1000             # (s − dh/2)
        self.assertAlmostEqual(fc, 20.7818, places=3)
        self.assertAlmostEqual(v["resistente"], min(fc, e1) + min(fc, ent), delta=0.01)

    def test_um_parafuso_por_linha_recusado_em_tracao(self):
        with self.assertRaises(ValueError) as e:
            C.verificar_gusset(self._dados(n_por_linha=1), dict(N=30.0))
        self.assertIn("2 ou mais parafusos", str(e.exception))

    def test_aviso_da_parede_fina(self):
        r = C.verificar_gusset(self._dados(), dict(N=30.0))
        self.assertTrue(any("U dobrado" in a and "NBR 14762" in a for a in r["avisos"]))
        rl = C.verificar_gusset(self._dados(perfil_diagonal="UPN 160", e1=35.0, n_por_linha=3), dict(N=30.0))
        self.assertFalse(any("U dobrado" in a for a in rl["avisos"]))

    def test_upn_inalterado_pela_nbr_8800(self):
        r = C.verificar_gusset(self._dados(perfil_diagonal="UPN 160", e1=35.0, n_por_linha=3), dict(N=100.0))
        v = _v(r, "barra: ruptura da seção líquida")
        self.assertTrue(v["item"].startswith("NBR 8800 5.2.2 b)"))
        self.assertFalse(any("parede fina" in x["nome"] for x in r["verificacoes"]))

    def test_sugerir_upn160_n150(self):
        d = C.sugerir_gusset(dict(perfil_diagonal="UPN 160"), dict(N=150.0))
        r = C.verificar_gusset(d, dict(N=150.0))
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)

    def test_sugerir_u_dobrado_parafusado(self):
        for N in (60.0, -60.0):
            d = C.sugerir_gusset(dict(perfil_diagonal="U 150x50x3,00"), dict(N=N))
            r = C.verificar_gusset(d, dict(N=N))
            self.assertTrue(r["ok"], N)
            self.assertLessEqual(r["taxa"], 0.90, (N, r["criterio"]))
            self.assertEqual(d["d_mm"], 12.7)   # ⅝" não cabe na janela de borda de t = 3 mm; sai ½"
        # o diâmetro informado vale
        d = C.sugerir_gusset(dict(perfil_diagonal="U 150x50x3,00", d_mm=15.875), dict(N=60.0))
        self.assertEqual(d["d_mm"], 15.875)

    def test_u_dobrado_soldado_a_perna_e_o_limite_da_parede(self):
        d = C.sugerir_gusset(dict(perfil_diagonal="U 150x50x3,00", ligacao="soldada"), dict(N=60.0))
        r = C.verificar_gusset(d, dict(N=60.0))
        self.assertTrue(r["ok"])
        self.assertEqual(d["b_solda"], 3.0)   # mínima (tabela 10) = máxima (t = 3 mm): taxa 1,00 intrínseca
        self.assertTrue(any("parede fina do perfil" in x["nome"] for x in r["verificacoes"]))
        # filete longitudinal na parede fina: L/t = 80/3 < 100 → (1 − 0,01·26,7)·t·L·fu/1,65 por linha
        v = _v(r, "solda: parede fina do perfil, filete borda 1")
        l1 = d["l1"]
        pn = (1 - 0.01 * l1 / 3) * 3 * l1 * 400 / 1.65 / 1000
        self.assertAlmostEqual(v["resistente"], pn, delta=0.01)

    def test_face_do_u_no_apoio(self):
        d = C.sugerir_gusset(dict(perfil_diagonal="UPN 160", perfil_viga="UPN 200", perfil_pilar="UPN 200"), dict(N=100.0))
        self.assertAlmostEqual(d["eb"], 100.0)
        self.assertAlmostEqual(d["ec"], 100.0)
        d = C.sugerir_gusset(dict(perfil_diagonal="UPN 160", perfil_viga="UPN 200", perfil_pilar="UPN 200",
                                  face_u_viga="alma", face_u_pilar="mesa"), dict(N=100.0))
        self.assertAlmostEqual(d["eb"], UPN200["xg"] * 1000, places=6)   # 22,01: costas da alma
        self.assertAlmostEqual(d["ec"], 100.0)
        with self.assertRaises(ValueError):
            C.sugerir_gusset(dict(perfil_diagonal="UPN 160", perfil_viga="UPN 200", face_u_viga="topo"), dict(N=100.0))
        # I/H: sempre h/2
        d = C.sugerir_gusset(dict(perfil_diagonal="UPN 160", perfil_viga="W250x73", face_u_viga="alma"), dict(N=100.0))
        self.assertAlmostEqual(d["eb"], W250["h"] / 2 * 1000, places=6)


# ------------------------------------------------------------------------------------------------ perfil em tubo

U_CHS = dict(forma_ramal="U", h_ramal=100, b_ramal=50, tw_ramal=6, tf_ramal=8.5, forma_receptor="tubo_circular",
             D_receptor=101.6, t_receptor=5.0, theta=90, giro=0, b_solda=5)
ESF = dict(N=10, V=5, Vz=0, M=1.0, Mz=0, T=0)
TP = T.TIPOS["perfil_tubo_soldado"]


class TestPerfilTuboU(unittest.TestCase):
    def test_parede_do_tubo_pela_tabela_7_4_a_mao(self):
        r = TP["verificar"](U_CHS, ESF)
        v = r["valores"]
        self.assertAlmostEqual(v["beta"], 50 / 101.6, places=6)
        self.assertAlmostEqual(v["eta"], 100 / 101.6, places=6)
        self.assertAlmostEqual(v["N1_Rd_kN"], 8.625 * (4 + 20 * (50 / 101.6) ** 2) * (1 + 0.25 * 100 / 101.6), delta=1e-6)
        self.assertAlmostEqual(v["N1_Rd_kN"], 95.05, places=2)
        self.assertAlmostEqual(v["Lw_mm"], 388.0, places=6)
        self.assertTrue(r["ok"])

    def test_aviso_diz_o_que_a_tabela_nao_cobre(self):
        r = TP["verificar"](U_CHS, ESF)
        a = next(x for x in r["avisos"] if x.startswith("perfil U"))
        self.assertIn("alma fica na borda", a)
        self.assertIn("centro de cisalhamento", a)
        self.assertFalse(any("tratado como I" in x for x in r["avisos"]))
        # o I não leva o aviso
        ri = TP["verificar"](dict(U_CHS, forma_ramal="I"), ESF)
        self.assertFalse(any(x.startswith("perfil U") for x in ri["avisos"]))

    def test_mesmas_mesas_mesma_resistencia_do_i(self):
        ru, ri = TP["verificar"](U_CHS, ESF), TP["verificar"](dict(U_CHS, forma_ramal="I"), ESF)
        self.assertAlmostEqual(ru["valores"]["N1_Rd_kN"], ri["valores"]["N1_Rd_kN"], places=9)
        self.assertAlmostEqual(ru["valores"]["Lw_mm"], 388.0)   # contorno real do U (no I também 388: a alma tem 2 faces, o U 1 face + 1 mesa a mais)

    def test_ue_recusado_com_mensagem(self):
        with self.assertRaises(ValueError) as e:
            TP["verificar"](dict(U_CHS, forma_ramal="Ue"), ESF)
        self.assertIn("U enrijecido", str(e.exception))
        self.assertIn("UPN", str(e.exception))

    def test_sugerir_upn100_em_tubo_circular(self):
        base = dict(U_CHS, N_receptor=-60)
        base.pop("b_solda")
        for esf in (dict(N=0, V=6, Vz=0, M=1.0, Mz=0, T=0), dict(N=15, V=3, Vz=0, M=0.3, Mz=0, T=0)):
            d = TP["sugerir"](base, esf)
            r = TP["verificar"](d, esf)
            self.assertTrue(r["ok"])
            self.assertLessEqual(r["taxa"], 0.90, r["verificacoes"])

    def test_sugerir_upn100_em_tubo_retangular_150x150x6(self):
        base = dict(forma_ramal="U", h_ramal=100, b_ramal=50, tw_ramal=6, tf_ramal=8.5, forma_receptor="tubo_retangular",
                    D_receptor=150, B_receptor=150, t_receptor=6, theta=90, giro=0)
        esf = dict(N=0, V=15, Vz=0, M=4, Mz=0, T=0)
        d = TP["sugerir"](base, esf)
        r = TP["verificar"](d, esf)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertTrue(any("tubo retangular" in a and "não verificada" in a for a in r["avisos"]))   # limite existente


if __name__ == "__main__":
    unittest.main()
