"""W8-CONTRAV (docs/v3/W8-CONTRAV.md): gusset de contraventamento, nó de treliça com gusset e tirante de barra redonda com
barras do catálogo (seletor), propriedades do catálogo, barras desenhadas e padrão de fábrica com folga."""
import math
import unittest

from prossen_estrutural import projeto as P, secoes
from prossen_estrutural.aco import ligacoes_contraventamento as C
from prossen_estrutural.aco import ligacoes_modelo as LM

MEDIDAS = {"secao", "dim_a", "dim_b", "t_perfil", "tf_perfil", "A_perfil", "xg_perfil", "e_banzo"}


def _tipo(tipo):
    return {t["tipo"]: t for t in P.tipos_ligacao()}[tipo]


def _ver(res, prefixo):
    return next(v for v in res["verificacoes"] if v["nome"].startswith(prefixo))


def _gusset(**mud):
    ex = _tipo("gusset_contraventamento")["exemplo"]
    return P.verificar_ligacao("gusset_contraventamento", dict(ex["dados"], **mud), ex["esforcos"])


class TestEsquema(unittest.TestCase):
    def test_campos_sem_medidas_soltas_e_seletor_restrito(self):
        for tipo, campos in (("gusset_contraventamento", ["perfil_diagonal"]),
                             ("no_trelica_gusset", [f"perfil_barra_{i}" for i in "1234"])):
            t = _tipo(tipo)
            nomes = {c["nome"] for c in t["campos"]}
            self.assertFalse({n for n in nomes if any(n == m or n.startswith(m + "_") for m in MEDIDAS)}, tipo)
            for campo in campos:
                c = next(x for x in t["campos"] if x["nome"] == campo)
                self.assertTrue(c["resugerir"])
                self.assertIn('L 3" x 1/4"', c["perfis"])
                self.assertIn('U 6" x 12,2', c["perfis"])
                self.assertIn("TC 88,9x5", c["perfis"])
                self.assertNotIn("W310x38.7", c["perfis"])           # perfil I não é diagonal de gusset
                self.assertNotIn("Ue 75x40x15x2,00", c["perfis"])    # formado a frio: fora (cálculo é NBR 8800)
        banzo = next(c for c in _tipo("no_trelica_gusset")["campos"] if c["nome"] == "perfil_banzo")
        self.assertIn("W150x18", banzo["perfis"])
        self.assertNotIn('L 3" x 1/4"', banzo["perfis"])


class TestPadraoDeFabrica(unittest.TestCase):
    def test_os_tres_passam_com_folga(self):
        # antes: gusset e nó a 0,97 (borda 30 com Tab. 14 = 29), tirante 0,89 sem barra desenhada e comprimento 0
        for tipo in ("gusset_contraventamento", "no_trelica_gusset", "tirante_barra_redonda"):
            ex = _tipo(tipo)["exemplo"]
            r = P.verificar_ligacao(tipo, ex["dados"], ex["esforcos"])
            self.assertTrue(r["ok"], tipo)
            self.assertLessEqual(r["taxa"], 0.90, (tipo, r["criterio"]))
            self.assertFalse(any("disposições construtivas não atendidas" in a for a in r["avisos"]), tipo)

    def test_bordas_com_tolerancia_de_corte(self):
        d = _tipo("gusset_contraventamento")["exemplo"]["dados"]
        self.assertEqual((d["e1"], d["e_gusset"], d["g_aba"]), (35, 35, 45))   # Tab. 14 (29) + 3 → 35; Gerdau 45 na L76
        self.assertEqual(_tipo("tirante_barra_redonda")["exemplo"]["dados"]["comprimento"], 6000.0)

    def test_no_de_pratt_em_equilibrio(self):
        ex = _tipo("no_trelica_gusset")["exemplo"]
        self.assertEqual(ex["esforcos"]["N2"], -70.7)
        r = P.verificar_ligacao("no_trelica_gusset", ex["dados"], ex["esforcos"])
        f = r["forcas_interface"]
        self.assertAlmostEqual(f["V"], 100 * math.sin(math.pi / 4) - 70.7, places=6)   # ΣV ≈ 0
        self.assertAlmostEqual(f["H"], 100 * math.cos(math.pi / 4), places=6)
        hb = secoes.catalogo("W150x18").props["h"] * 1e3
        self.assertAlmostEqual(_ver(r, "geometria: ligações acima da face do banzo")["solicitante"], hb / 2)


class TestPropriedadesDoCatalogo(unittest.TestCase):
    def test_cantoneira(self):
        # L 3" x 1/4": A tabelada 9,29 cm², ec = x̄ = 21,4 mm, 3 furos a 50 → Ct = 1 − 21,4/100
        r = _gusset()
        p = secoes.catalogo('L 3" x 1/4"').props
        ec = (p["t"] / 2 - p["x0"]) * 1e3
        self.assertAlmostEqual(_ver(r, "barra: escoamento")["resistente"], 9.29e-4 * 250e3 / 1.1, places=3)
        self.assertAlmostEqual(_ver(r, "barra: Ct ≥ 0,60")["resistente"], 1 - ec / 100)
        r2 = _gusset(arranjo="dupla")   # 2L: duas áreas e dois planos de corte
        self.assertAlmostEqual(_ver(r2, "barra: escoamento")["resistente"], 2 * 9.29e-4 * 250e3 / 1.1, places=3)
        self.assertIn("2 plano(s)", _ver(r2, "parafusos: cisalhamento")["nome"])

    def test_u_e_tubo(self):
        pu = secoes.catalogo('U 6" x 12,2').props
        med = C.perfil_barra('U 6" x 12,2')
        self.assertEqual((med["forma"], med["dim_a"], med["dim_b"]), ("U", pu["h"] * 1e3, pu["b"] * 1e3))
        self.assertAlmostEqual(med["xg_perfil"], pu["xg"] * 1e3)
        self.assertAlmostEqual(med["tf_perfil"], pu["tf"] * 1e3)
        r = _gusset(perfil_diagonal='U 6" x 12,2')
        self.assertAlmostEqual(_ver(r, "barra: escoamento")["resistente"], pu["A_catalogo"] * 250e3 / 1.1, places=3)
        pt = secoes.catalogo("TC 88,9x5").props
        s = P.sugerir_ligacao("gusset_contraventamento", dict(perfil_diagonal="TC 88,9x5"), {"N": 100})
        self.assertEqual((s["ligacao"], s["arranjo"]), ("soldada", "simples"))   # tubo: rasgo soldado
        self.assertLessEqual(s["b_solda"], 0.9 * pt["t"] * 1e3)                    # não nasce na perna máxima
        r = P.verificar_ligacao("gusset_contraventamento", s, {"N": 100})
        self.assertTrue(r["ok"])
        self.assertAlmostEqual(_ver(r, "barra: escoamento")["resistente"], pt["A_catalogo"] * 250e3 / 1.1, places=3)

    def test_erros_de_perfil(self):
        with self.assertRaises(ValueError):
            C.verificar_gusset(dict(perfil_diagonal="W310x38.7", t_gusset=8), {"N": 50})
        with self.assertRaises(ValueError):   # dupla só para cantoneira
            C.verificar_gusset(dict(perfil_diagonal='U 6" x 12,2', arranjo="dupla", t_gusset=8), {"N": 50})


class TestGeometria(unittest.TestCase):
    def test_diagonal_cantoneira_sobre_o_gusset(self):
        r = _gusset()
        g, tg = r["geometria"], 6.3
        self.assertEqual(g["membros"], [])
        liga, saliente = g["perfis_barra"]
        self.assertEqual((liga["origem"][1], liga["normal"], liga["espessura"]), (tg / 2, [0.0, 1.0, 0.0], 6.35))
        self.assertEqual(saliente["normal"], [0.0, 0.0, 1.0])
        zs = [-p[1] for p in liga["contorno"]]   # v = y × x = −z
        z_talao = max(zs)
        self.assertAlmostEqual(z_talao - min(zs), 76.2)
        self.assertAlmostEqual(liga["origem"][0], g["recuo"])                       # ponta da diagonal = recuo
        for b in g["parafusos"]:
            self.assertAlmostEqual(z_talao - b["origem"][2], 45.0)                  # linha de gabarito
            self.assertTrue(liga["origem"][0] < b["origem"][0] < liga["origem"][0] + max(p[0] for p in liga["contorno"]))
        ch = g["chapas"][0]["contorno"]
        self.assertGreaterEqual(max(p[1] for p in ch), z_talao - 1e-6)             # o gusset cobre o talão
        w = g["linhas"][0]
        self.assertEqual(w["estilo"], "tracejada")
        self.assertAlmostEqual(abs(w["pontos"][1][2] - w["pontos"][2][2]), g["largura_whitmore"], places=1)
        vx, vz = g["vista"]["x"], g["vista"]["z"]
        self.assertAlmostEqual(sum(a * b for a, b in zip(vx, vz)), 0.0)
        self.assertAlmostEqual(vz[0], math.cos(math.radians(45)))                   # pilar na vertical da elevação

    def test_2L_U_e_tubo_desenhados(self):
        g = _gusset(arranjo="dupla")["geometria"]
        ys = sorted(p["origem"][1] for p in g["perfis_barra"] if p["normal"] == [0.0, 1.0, 0.0])
        self.assertEqual(len(g["perfis_barra"]), 4)
        self.assertAlmostEqual(ys[0], -6.3 / 2 - 6.35)     # 2ª cantoneira na face −y
        self.assertAlmostEqual(ys[1], 6.3 / 2)
        self.assertEqual(len(_gusset(perfil_diagonal='U 6" x 12,2')["geometria"]["perfis_barra"]), 3)   # alma + 2 mesas
        s = P.sugerir_ligacao("gusset_contraventamento", dict(perfil_diagonal="TC 88,9x5"), {"N": 100})
        tb = P.verificar_ligacao("gusset_contraventamento", s, {"N": 100})["geometria"]["tubos"][0]
        self.assertEqual((tb["d"], tb["t"], tb["campo"]), (88.9, 5.0, "perfil_diagonal"))
        self.assertEqual(tb["origem"][1], 0.0)             # gusset no rasgo, no plano médio do tubo

    def test_no_banzo_e_barras_sem_choque(self):
        ex = _tipo("no_trelica_gusset")["exemplo"]
        r = P.verificar_ligacao("no_trelica_gusset", ex["dados"], ex["esforcos"])
        g = r["geometria"]
        banzo = [p for p in g["perfis_barra"] if p.get("camada") == 0]
        self.assertEqual(len(banzo), 3)                    # alma + 2 mesas do W150x18
        self.assertEqual(len(g["perfis_barra"]) - 3, 8)   # 2L + 2L
        self.assertEqual(len(g["linhas"]), 2)
        self.assertEqual(g["membros"], [])
        # regiões ligadas das duas barras (diagonal a 45° e montante a 90°) separadas pela folga de montagem
        d, _novo = C._ler_barras(C._NO, C._NO_LEG, ex["dados"], "no_trelica_gusset",
                                 [(f"_{i}", f"perfil_barra_{i}") for i in "1234"])
        c = C._comum(d)
        infos = {i: C._membro(d, f"_{i}", 100.0, c, [], []) for i in "12"}
        regs = []
        for i in "12":
            th = math.radians(d[f"theta_{i}"])
            regs.append(C._hull(C._regiao(infos[i], g["barras"][i]["recuo"] / 1e3, (math.cos(th), math.sin(th)),
                                          (-math.sin(th), math.cos(th)))))
        self.assertFalse(C._cruzam(regs[0], regs[1], C.FOLGA_MONTAGEM - 1e-6))

    def test_cruzam(self):
        q = [(0, 0), (1, 0), (1, 1), (0, 1)]
        self.assertTrue(C._cruzam(q, [(0.5, 0.5), (2, 0.5), (2, 2), (0.5, 2)]))
        self.assertFalse(C._cruzam(q, [(1.1, 0), (2, 0), (2, 1), (1.1, 1)]))
        self.assertTrue(C._cruzam(q, [(1.1, 0), (2, 0), (2, 1), (1.1, 1)], 0.2))

    def test_tirante_desenhado(self):
        ex = _tipo("tirante_barra_redonda")["exemplo"]
        g = P.verificar_ligacao("tirante_barra_redonda", ex["dados"], ex["esforcos"])["geometria"]
        D, t = ex["dados"]["D_barra"], ex["dados"]["t_chapa"]
        barra, rosca = g["tubos"]
        self.assertEqual((barra["d"], barra["t"]), (D, D / 2))     # barra maciça
        self.assertAlmostEqual(rosca["d"], 0.85 * D)
        nomes = [c["nome"] for c in g["chapas"]]
        self.assertEqual(nomes, ["chapa de apoio da porca", "arruela", "porca", "contraporca"])
        xs = [c["origem"][0] for c in g["chapas"]]
        self.assertEqual(xs[:2], [0.0, -t])                        # arruela atrás da chapa, porca atrás da arruela
        self.assertLess(barra["origem"][0], xs[3] - g["chapas"][3]["espessura"])   # fios além da contraporca
        self.assertEqual(g["porcas"], [])                          # porca desenhada como peça (não como parafuso)
        self.assertTrue(all(c.get("campo") for c in g["chapas"]))
        self.assertIn("L = 6000", g["linhas"][0]["texto"])


class TestLegadoEModelo(unittest.TestCase):
    def test_dados_antigos_seguem_e_desenham_a_barra(self):
        r = C.verificar_gusset(dict(secao="L", dim_a=76.2, t_perfil=6.35, n_por_linha=3, t_gusset=8), {"N": 100})
        self.assertEqual(len(r["geometria"]["perfis_barra"]), 2)

    def test_no_com_varias_barras_pelo_modelo_recusa(self):
        ex = _tipo("no_trelica_gusset")["exemplo"]
        with self.assertRaises(ValueError):
            LM.verificador("no_trelica_gusset")(None, None, {"N": 100}, ex["dados"])


if __name__ == "__main__":
    unittest.main()
