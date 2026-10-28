"""W12-FLEX-U — perfil U como apoio (e viga U no assento) nas ligações flexíveis (docs/v3/W12-FLEX-U.md).

Pedido do usuário (28/10/2026): "as outras ligações não têm os U e UPN também, revise isso". Antes o apoio U era medido
como um I (largura entre as mesas, abaixo da mesa da viga suporte, recorte da viga), sem conferir onde caem os furos.

Convenção (a do desenho W7-UPN): apoio U na alma (pilar ou viga) = costas da alma para a ligação, mesas do outro lado;
na mesa do pilar U = mesa centrada no nó, alma no lado −ab (ab = abertura_y).

Contas à mão (mm, kN; FOLGA_APERTO = 1,25db; tabela 14 em borda laminada: ½" 19, ⅝" 22, ¾" 26):

Caso A — W310×38,7 (tw 5,8) com dupla cantoneira L102×102×6,35, g 65, ¾", nas costas da alma do UPN 300 (h 300, tf 16,
alma reta 232 → tf + r = 34, parte plana ±116):
  furos em y = ±(5,8/2 + 65) = ±67,9; folga até o pé do raio das mesas = 116 − 67,9 = 48,1 ≥ 1,25·19,05 = 23,81 → 0,495;
  largura nas costas da alma 5,8 + 2·102 = 209,8 ≤ 300 → 0,699 (antes, como I: ≤ 300 − 2·34 = 232 → 0,904).

Caso B — W200×26,6 no assento L102×152×9,5, 1 coluna de ½", na mesa do UPN 300 (bf 100, tw 10, r 18):
  u = 0; pé do raio da alma em u = −(50 − 10 − 18) = −22 → folga 22 ≥ 1,25·12,7 = 15,875 → 0,722;
  borda até a ponta 50 ≥ 19 → 0,38; assento 5·⌊0,9·100/5⌋ = 90 ≤ 100 → 0,90.
  Duas linhas de ½" a ±(3,2 + 45) = ±48,2: o furo junto à alma vai a −48,2 − 7,1 = −55,3 < −22 → recusa.

Caso C — W310×38,7 na alma do UPN 300 viga–viga (topos alinhados): sem recorte (as mesas do U ficam do outro lado);
  cantoneiras de 75 − 40 = 35 a 150 + 40 = 190 ≤ 300 → 0,633; furos a 75 e 150 do topo, parte plana de 34 a 266:
  folga min(75 − 34, 266 − 150) = 41 → 23,81/41 = 0,581.

Caso D — UPN 200 (h 200, bf 75, tw 8,5, tf 11,5, alma reta 151 → r = 13; xg 22,01; |x0| 44,46) no assento L102×152,
  folga 10 (ln = 92), aço A-572 GR.50:
  5.7.3: k = 11,5 + 13 = 24,5; FRd = 1,10·(2,5·24,5 + 92)·345·8,5/1,10 = 449,41 kN;
  assento cobre a mesa: 2·máx(22,01; 75 − 22,01) = 105,98 ≤ 160;
  e (centro de cisalhamento → meio da mesa) = (44,46 − 22,01) + 75/2 = 59,95 mm.
"""
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes_flexiveis as F


def _v(r, inicio):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(inicio))


def _sug_ver(tipo, pf, esf):
    s = dict(P.sugerir_ligacao(tipo, pf, esf))
    t = s.pop("tipo_ligacao", None) or tipo
    mot = s.pop("motivo", None)
    d = dict(pf, **{k: v for k, v in s.items() if v is not None})
    r = P.verificar_ligacao(t, d, esf)
    return t, mot, d, r


A = dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300", apoio="alma_pilar", d=19.05, s=75, e_v=40, n=2,
         dist_topo_furo=75, cantoneira="L102x102x6.35", g_viga=65, g_apoio=65)


class TestApoioU(unittest.TestCase):
    def test_caso_a_costas_da_alma(self):
        r = P.verificar_ligacao("cantoneira_dupla_alma", A, {"V": 100})
        v = _v(r, "parafusos no apoio: folga de aperto até as mesas do U")
        self.assertAlmostEqual(v["resistente"], 48.1, 2)
        self.assertAlmostEqual(v["solicitante"], 23.8125, 3)
        v = _v(r, "cantoneiras: largura nas costas da alma do U")
        self.assertAlmostEqual(v["solicitante"], 209.8)
        self.assertAlmostEqual(v["resistente"], 300.0)
        self.assertFalse(any(x["nome"].startswith("cantoneiras: largura entre as mesas") for x in r["verificacoes"]))
        # furos do apoio dentro da parte plana da alma (±116): |y| + dh/2
        ys = [p["origem"][1] for p in r["geometria"]["parafusos"] if p["eixo"] == [-1, 0, 0]]
        self.assertEqual(len(ys), 4)
        self.assertTrue(all(abs(y) + 20.65 / 2 <= 116 for y in ys))

    def test_furo_fora_da_alma_recusa(self):
        with self.assertRaises(P.ErroEntrada) as cm:
            P.verificar_ligacao("cantoneira_dupla_alma", dict(A, perfil_suporte="UPN 120", cantoneira="L76x76x6.35",
                                                              g_viga=45, g_apoio=45), {"V": 50})
        self.assertIn("fora da alma do UPN 120", str(cm.exception))

    def test_caso_b_mesa_u_uma_coluna(self):
        t, _m, d, r = _sug_ver("apoio_cantoneira", dict(perfil_viga="W200x26.6", perfil_suporte="UPN 300",
                                                         apoio="mesa_pilar"), {"V": 40})
        self.assertEqual((t, d["n_colunas"], d["d"], d["comprimento"]), ("apoio_cantoneira", 1, 12.7, 90))
        v = _v(r, "parafusos do assento: folga de aperto até a alma do U")
        self.assertAlmostEqual(v["solicitante"], 15.875)
        self.assertAlmostEqual(v["resistente"], 22.0)
        self.assertAlmostEqual(_v(r, "parafusos do assento: borda até a ponta")["resistente"], 50.0)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90 + 1e-9)
        self.assertTrue(any("arruela de compensação" in a for a in r["avisos"]))

    def test_caso_b_duas_linhas_na_mesa_u_recusa(self):
        dados = dict(perfil_viga="W200x26.6", perfil_suporte="UPN 300", apoio="mesa_pilar", d=12.7, s=38, e_v=22,
                     n=3, dist_topo_furo=60, cantoneira="L76x76x6.35", g_viga=45, g_apoio=45)
        with self.assertRaises(P.ErroEntrada) as cm:
            P.verificar_ligacao("cantoneira_dupla_alma", dados, {"V": 40})
        self.assertIn("fora da mesa do UPN 300", str(cm.exception))
        for tipo in ("cantoneira_dupla_alma", "chapa_extremidade_flexivel"):
            with self.assertRaises(P.ErroEntrada) as cm:
                P.sugerir_ligacao(tipo, dict(perfil_viga="W200x26.6", perfil_suporte="UPN 300", apoio="mesa_pilar"),
                                  {"V": 40})
            self.assertIn("duas linhas de parafusos não cabem na mesa do U", str(cm.exception))
            self.assertIn("alma_pilar", str(cm.exception))

    def test_caso_c_viga_viga_sem_recorte(self):
        t, _m, d, r = _sug_ver("viga_viga_recorte", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300"),
                               {"V": 100})
        self.assertEqual(t, "viga_viga_recorte")
        self.assertEqual((r["valores"]["recorte_sup_h_mm"], r["valores"]["recorte_inf_h_mm"]), (0.0, 0.0))
        self.assertFalse([c for c in r["geometria"]["recortes"] if c["tipo"] == "cope"])
        self.assertFalse([c for c in r["geometria"]["pontas"][0]["recortes"] if c["tipo"] == "cope"])
        v = _v(r, "cantoneiras: dentro da altura da viga suporte U")
        self.assertAlmostEqual(v["taxa"], 190 / 300, 4)
        self.assertAlmostEqual(_v(r, "parafusos no apoio: folga de aperto até as mesas")["resistente"], 41.0, 3)
        self.assertLessEqual(r["taxa"], 0.90)
        sup = next(m for m in r["geometria"]["membros"] if m["barra"] == "suporte")
        self.assertEqual((sup["forma"], sup["apoio"]), ("U", "alma_viga"))

    def test_folga_porca_na_mesa_da_viga_u(self):
        """U 150 na alma do UPN 200 (topos alinhados): furo a 37 mm do topo, pé do raio a 24,5 → 12,5 < 15,9 (reprova);
        o sugerir troca para a solda direta com o motivo."""
        t, mot, _d, r = _sug_ver("viga_viga_recorte", dict(perfil_viga="U 150x50x3,75", perfil_suporte="UPN 200"),
                                 {"V": 20})
        self.assertEqual(t, "solda_direta_alma")
        self.assertIn("folga de aperto até as mesas do U", mot)
        self.assertTrue(r["ok"])

    def test_solda_direta_viga_mais_larga_que_a_mesa_u(self):
        t, _m, d, r = _sug_ver("solda_direta_alma", dict(perfil_viga="W200x26.6", perfil_suporte="UPN 300",
                                                          apoio="mesa_pilar"), {"V": 40})
        self.assertEqual(d["soldar_mesas"], "não")
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)

    def test_face_apoio_u(self):
        sp = F._perfil("UPN 300", "s")
        self.assertAlmostEqual(F._face_apoio(sp, "alma_pilar"), 0.300)
        self.assertAlmostEqual(F._face_apoio(sp, "mesa_pilar"), 0.100)
        self.assertEqual(F.recortes_viga_viga(F._perfil("W310x38.7", "v"), sp, 0.010)["recorte_sup_h"], 0.0)


class TestVigaUNoAssento(unittest.TestCase):
    def test_caso_d(self):
        t, _m, d, r = _sug_ver("apoio_cantoneira", dict(perfil_viga="UPN 200", perfil_suporte="W250x73",
                                                         apoio="mesa_pilar"), {"V": 40})
        self.assertEqual(t, "apoio_cantoneira")
        self.assertAlmostEqual(_v(r, "alma da viga: escoamento local")["resistente"], 449.41, 1)
        v = _v(r, "assento cobre a mesa do U")
        self.assertAlmostEqual(v["solicitante"], 105.98, 1)
        self.assertAlmostEqual(r["valores"]["e_centro_cisalhamento_mm"], 59.95, 1)
        self.assertTrue(any("viga U no assento" in a for a in r["avisos"]))
        self.assertEqual(r["geometria"]["abertura_y"], 1)
        self.assertLessEqual(r["taxa"], 0.90)

    def test_dobrado_aviso(self):
        _t, _m, _d, r = _sug_ver("apoio_cantoneira", dict(perfil_viga="U 200x75x4,75", perfil_suporte="W250x73",
                                                           apoio="mesa_pilar"), {"V": 30})
        self.assertTrue(any("formada a frio" in a for a in r["avisos"]))

    def test_assento_estreito_reprova(self):
        dados = dict(perfil_viga="UPN 200", perfil_suporte="W250x73", apoio="mesa_pilar", comprimento=100, n_colunas=1)
        self.assertFalse(_v(P.verificar_ligacao("apoio_cantoneira", dados, {"V": 40}), "assento cobre a mesa do U")["ok"])


class TestSugerirU(unittest.TestCase):
    """Todo caso de U aceito sai com taxa ≤ 0,90 (o tipo pode trocar para a solda direta, com o motivo)."""
    CASOS = [
        ("chapa_simples_alma", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300", apoio="alma_pilar"), 100,
         "chapa_simples_alma"),
        ("cantoneira_dupla_alma", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300", apoio="alma_pilar"), 100,
         "cantoneira_dupla_alma"),
        ("chapa_extremidade_flexivel", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300", apoio="alma_pilar"), 100,
         "chapa_extremidade_flexivel"),
        ("apoio_cantoneira", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300", apoio="alma_pilar"), 100,
         "apoio_cantoneira"),
        ("viga_viga_recorte", dict(perfil_viga="W310x38.7", perfil_suporte="UPN 300"), 100, "viga_viga_recorte"),
        ("chapa_simples_alma", dict(perfil_viga="UPN 200", perfil_suporte="W250x73", apoio="mesa_pilar"), 40,
         "chapa_simples_alma"),
        ("chapa_simples_alma", dict(perfil_viga="W200x26.6", perfil_suporte="UPN 300", apoio="mesa_pilar"), 40,
         "chapa_simples_alma"),
        ("chapa_simples_alma", dict(perfil_viga="U 200x75x4,75", perfil_suporte="W250x73", apoio="mesa_pilar"), 30,
         "chapa_simples_alma"),
        ("chapa_simples_alma", dict(perfil_viga="W250x32.7", perfil_suporte="U 300x100x4,75", apoio="alma_pilar"), 40,
         "chapa_simples_alma"),
        ("cantoneira_dupla_alma", dict(perfil_viga="W250x32.7", perfil_suporte="U 300x100x4,75", apoio="alma_pilar"),
         40, "cantoneira_dupla_alma"),
        ("chapa_extremidade_flexivel", dict(perfil_viga="UPN 200", perfil_suporte="UPN 300", apoio="alma_pilar"), 40,
         "chapa_extremidade_flexivel"),
        ("apoio_cantoneira", dict(perfil_viga="UPN 200", perfil_suporte="UPN 300", apoio="alma_pilar"), 40,
         "apoio_cantoneira"),
        ("cantoneira_dupla_alma", dict(perfil_viga="UPN 160", perfil_suporte="UPN 200", apoio="alma_viga"), 30,
         "solda_direta_alma"),
        ("viga_viga_recorte", dict(perfil_viga="UPN 160", perfil_suporte="UPN 200"), 30, "solda_direta_alma"),
        ("chapa_simples_alma", dict(perfil_viga="U 150x50x3,75", perfil_suporte="UPN 200", apoio="alma_pilar"), 20,
         "solda_direta_alma"),
    ]

    def test_taxas(self):
        for tipo, pf, V, esperado in self.CASOS:
            with self.subTest(tipo=tipo, **pf):
                t, mot, _d, r = _sug_ver(tipo, pf, {"V": V})
                self.assertEqual(t, esperado)
                self.assertTrue(r["ok"])
                self.assertLessEqual(r["taxa"], 0.90 + 1e-9)
                if t != tipo:
                    self.assertTrue(mot)

    def test_viga_viga_solda_direta_na_alma_da_viga(self):
        """A troca da viga–viga para a solda direta escolhe a perna com o apoio alma_viga (antes: como na mesa)."""
        t, _m, d, _r = _sug_ver("viga_viga_recorte", dict(perfil_viga="UPN 160", perfil_suporte="UPN 200"), {"V": 30})
        self.assertEqual((t, d["apoio"]), ("solda_direta_alma", "alma_viga"))

    def test_padroes_so_com_u(self):
        self.assertEqual(len(F._padroes(F._perfil("W310x38.7", "v"), F._perfil("W250x73", "s"))), 1)
        ps = F._padroes(F._perfil("UPN 160", "v"), F._perfil("W250x73", "s"))
        self.assertEqual([p["d"] for p in ps], [12.7, 12.7, 15.875, 19.05])   # ⅝" não cabe na alma reta de 115 mm
        self.assertEqual((ps[1]["s"], ps[1]["e_v"]), (40, 25))                 # 3·12,7 → 40; 22/0,9 → 25


if __name__ == "__main__":
    unittest.main()
