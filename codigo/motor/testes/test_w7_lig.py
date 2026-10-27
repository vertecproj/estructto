"""W7-LIG (docs/v3/W7-LIG.md): padrões de fábrica da chapa de extremidade flexível e da cantoneira simples em treliça."""
import unittest

from prossen_estrutural import projeto as P, secoes
from prossen_estrutural.aco import ligacoes_contraventamento as C
from prossen_estrutural.aco import ligacoes_flexiveis as F

CE = F.TIPOS["chapa_extremidade_flexivel"]


def _exemplo(tipo):
    return {t["tipo"]: t for t in P.tipos_ligacao()}[tipo]


def _ver(res, prefixo):
    return next(v for v in res["verificacoes"] if v["nome"].startswith(prefixo))


class TestChapaExtremidadePadrao(unittest.TestCase):
    def test_fileiras_pela_altura_da_viga(self):
        # Lp = 2·40 + 75·(n − 1) ≥ 0,6·h (SCI P358): W250 → 2 (155 ≥ 150), W310 → 3 (230 ≥ 186), W410 → 4 (305 ≥ 242),
        # W530 → 5 (380 ≥ 316); espessura 8 mm (faixa 8–10 mm)
        for viga, n in (("W250x17.9", 2), ("W310x38.7", 3), ("W410x60", 4), ("W530x92", 5)):
            d = CE["sugerir"]({"viga": viga, "pilar": "W250x73"}, {"V": 100})
            h = secoes.catalogo(viga).props["h"] * 1e3
            Lp = 2 * d["e_v"] + (d["n"] - 1) * d["s"]
            self.assertEqual(d["n"], n, viga)
            self.assertGreaterEqual(Lp, 0.6 * h, viga)
            self.assertIn(d["t_chapa"], F.ESPESSURAS_CE)
            self.assertTrue(CE["verificar"](d, {"V": 100})["ok"], viga)

    def test_padrao_do_projetista_w310(self):
        # o que o usuário viu (imagem 8): 6,3 × 200 × 155 com 2 fileiras; agora 8 × 200 × 230 com 3 fileiras
        ex = _exemplo("chapa_extremidade_flexivel")["exemplo"]
        d = ex["dados"]
        self.assertEqual((d["n"], d["t_chapa"], d["largura_chapa"], d["gabarito"]), (3, 8.0, 200, 120))
        r = P.verificar_ligacao("chapa_extremidade_flexivel", d, ex["esforcos"])
        self.assertTrue(r["ok"])
        ch = r["geometria"]["chapas"][0]
        self.assertAlmostEqual(max(p[1] for p in ch["contorno"]), 230.0)   # altura = 40 + 2·75 + 40
        self.assertEqual(len(r["geometria"]["parafusos"]), 6)
        self.assertEqual(len(r["geometria"]["soldas"]), 2)                 # filete dos dois lados da alma
        self.assertFalse(any("0,6·h" in a for a in r["avisos"]))

    def test_folga_de_aperto(self):
        # W310x38,7 (tw 5,8) com solda 5: (g − tw)/2 − 5 ≥ 1,25·19,05 = 23,8 mm → g = 120 passa (52,1), g = 60 reprova (22,1)
        base = dict(perfil_viga="W310x38.7", perfil_suporte="W250x73", n=3, dist_topo_chapa=25, t_chapa=8)
        ok = _ver(CE["verificar"](base, {"V": 100}), "parafusos: folga de aperto até a solda")
        self.assertAlmostEqual(ok["solicitante"], 1.25 * 19.05)
        self.assertAlmostEqual(ok["resistente"], (120 - 5.8) / 2 - 5)
        r = CE["verificar"](dict(base, gabarito=60, largura_chapa=140), {"V": 100})
        self.assertFalse(_ver(r, "parafusos: folga de aperto até a solda")["ok"])
        self.assertFalse(r["ok"])

    def test_aviso_chapa_baixa(self):
        r = CE["verificar"](dict(perfil_viga="W310x38.7", perfil_suporte="W250x73", n=2, dist_topo_chapa=25),
                            {"V": 50})
        self.assertTrue(any("0,6·h" in a for a in r["avisos"]))


class TestCantoneiraCatalogo(unittest.TestCase):
    def test_campos_sem_medidas_soltas(self):
        t = _exemplo("cantoneira_simples_trelica")
        nomes = {c["nome"] for c in t["campos"]}
        self.assertIn("perfil_cantoneira", nomes)
        self.assertFalse(nomes & {"secao", "dim_a", "dim_b", "t_perfil", "tf_perfil", "A_perfil", "xg_perfil"})
        d = t["exemplo"]["dados"]
        self.assertEqual(d["perfil_cantoneira"], 'L 3" x 1/4"')
        self.assertEqual(d["g_aba"], 45)                                  # Gerdau 2.4.5: 45 mm na L76
        self.assertGreater(d["comprimento"], 0)
        self.assertGreaterEqual(d["n_por_linha"], 2)

    def test_propriedades_do_catalogo(self):
        # L 3" x 1/4": A tabelada 9,29 cm²; x̄ = 21,4 mm (cantos vivos); Ct = 1 − 21,4/100 com 3 furos a 50
        ex = _exemplo("cantoneira_simples_trelica")["exemplo"]
        r = C.verificar_cantoneira_simples(ex["dados"], {"N": 100})
        p = secoes.catalogo('L 3" x 1/4"').props
        ec = (p["t"] / 2 - p["x0"]) * 1e3
        self.assertAlmostEqual(ec, 21.40, places=1)
        self.assertAlmostEqual(r["dica_barra"]["ec_mm"], ec)
        self.assertAlmostEqual(r["dica_barra"]["Ct"], 1 - ec / 100)
        self.assertAlmostEqual(_ver(r, "barra: escoamento")["resistente"], 9.29e-4 * 250e3 / 1.1, places=3)
        self.assertTrue(r["ok"])

    def test_aba_menor_de_cantoneira_desigual(self):
        med, _ = C.cantoneira_catalogo("L 150x100x12", "menor")
        self.assertEqual((med["dim_a"], med["dim_b"]), (100.0, 150.0))
        med2, _ = C.cantoneira_catalogo("L 150x100x12", "maior")
        self.assertEqual((med2["dim_a"], med2["dim_b"]), (150.0, 100.0))
        self.assertGreater(med["xg_perfil"], med2["xg_perfil"])   # ligada pela menor: centroide mais longe da face
        with self.assertRaises(ValueError):
            C.cantoneira_catalogo("W310x38.7")

    def test_geometria_duas_abas_no_gusset(self):
        ex = _exemplo("cantoneira_simples_trelica")["exemplo"]
        g = C.verificar_cantoneira_simples(ex["dados"], {"N": 100})["geometria"]
        tg = 6.3
        ca = g["cantoneiras"][0]
        self.assertEqual((ca["abas"], ca["espessura"]), ([76.2, 76.2], 6.35))
        self.assertEqual(ca["origem"][1], tg / 2)            # talão encostado na face do gusset
        self.assertEqual(ca["w"], [0.0, 1.0, 0.0])            # aba saliente para fora do gusset
        self.assertEqual(ca["u"], [0.0, 0.0, -1.0])           # aba ligada desce do talão, sobre o gusset
        zt = ca["origem"][2]
        ch = g["chapas"][0]
        zs = [p[1] for p in ch["contorno"]]
        xs = [p[0] for p in ch["contorno"]]
        self.assertTrue(min(zs) < zt - 76.2 and max(zs) > zt)   # o gusset cobre a aba ligada inteira
        for b in g["parafusos"]:
            self.assertAlmostEqual(zt - b["origem"][2], 45.0)   # linha de gabarito a g_aba do talão
            self.assertGreater(b["origem"][1], tg / 2 + 6.35)   # cabeça por fora da aba
            self.assertTrue(min(xs) < b["origem"][0] < max(xs))
        self.assertGreater(ca["origem"][0] + ca["comprimento"], max(xs))   # a barra segue além do gusset

    def test_legado_com_medidas(self):
        r = C.verificar_cantoneira_simples(dict(dim_a=50.8, t_perfil=4.76, n_por_linha=2, s=40, e1=25, e_gusset=25,
                                                d_mm=12.7, tipo_parafuso="A307", t_gusset=6.3), {"N": 40})
        self.assertEqual(r["geometria"]["cantoneiras"][0]["abas"], [50.8, 50.8])


if __name__ == "__main__":
    unittest.main()
