"""W9-FLANGE-PERFIL (docs/v3/W9-FLANGE-PERFIL.md): emenda flangeada de perfis abertos (I/H e U) — viga–viga e pilar–pilar.

Em cada ponta uma chapa de topo (chapa de flange) soldada ao perfil; as duas chapas aparafusadas entre si. As
verificações são as da chapa de topo (T-stub de Struik–de Back por parafuso, distribuição plástica Σ T·h), com as duas
chapas iguais, mais solda das mesas e da alma, contato usinado (6.6.1), atrito (6.3.4.3) e disposições.

Conferência à mão (caso A: W310×38,7, ⅞" A325, chapa A-36 19 mm, 2 colunas a g = 80, pf = 40, sem linhas fora das mesas,
M = 60 kN·m, V = 100 kN) — refeita abaixo em `_mao()` só com as fórmulas, sem chamar o módulo:
  Ab = π·22,225²/4 = 387,9 mm²; Ft,Rd = 0,75·Ab·825/1,35 = 177,8 kN; Fv,Rd = 0,4·Ab·825/1,35 = 94,8 kN (6.3.3.1/2).
  linhas a z = ±(155 − 9,7 − 40) = ±105,3 mm; centro da mesa comprimida zc = −150,15 → h = 255,45 mm.
  chapa: largura = 2·max(165/2 + 15; 80/2 + 45) = 195 → p = 97,5 mm por parafuso.
  b = min(pf = 40; (g − tw)/2 = 37,1) = 37,1 (a alma governa) → a = borda lateral 57,5 ≤ 1,25b = 46,4.
  b' = b − d/2; a' = min(a; 1,25b) + d/2; δ = 1 − d'/p; Mp = p·t²/4·fy/1,10 (Kulak 17.10–17.12).
  Tcap = min(Mp(1 + δ)/b'; (Ft,Rd·a' + Mp)/(a' + b')) e MRd = 2·Tcap·h.
  Solda das mesas: F = M/(H − tf) = 60/0,3003 = 199,8 kN em bf + (bf − tw) = 324,2 mm de filete.
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes_rigidas as lr

VIGA, PILAR = "emenda_flangeada_viga", "emenda_flangeada_pilar"
CASO_A = dict(perfil_viga="W310x38.7", d_mm=22.225, tp=19.0, estendida="nao", n_colunas=2, g=80.0, pf=40.0,
              solda_mesa="filete", b_mesa=8.0, b_alma=5.0)


def _tipos():
    ts = P.tipos_ligacao()
    return {t["tipo"]: t for t in (ts if isinstance(ts, list) else ts["tipos"])}


def _v(r, inicio):
    return next(x for x in r["verificacoes"] if x["nome"].startswith(inicio))


def _mao():
    """Caso A só com as fórmulas (mm, N): Struik–de Back (Kulak 17.10–17.12) com resistências da NBR 8800."""
    d, t, fy, fub = 22.225, 19.0, 250.0, 825.0
    Ab = math.pi * d ** 2 / 4
    FtRd, FvRd = 0.75 * Ab * fub / 1.35, 0.4 * Ab * fub / 1.35
    dh = d + 1.5
    H, bf, tw, tf = 310.0, 165.0, 5.8, 9.7
    p = 2 * max(bf / 2 + 15, 80 / 2 + 45) / 2
    b = min(40.0, (80 - tw) / 2)
    a = 2 * max(bf / 2 + 15, 80 / 2 + 45) / 2 - 80 / 2
    b1, a1 = b - d / 2, min(a, 1.25 * b) + d / 2
    delta = 1 - dh / p
    Mp = p * t ** 2 / 4 * fy / 1.10
    Tcap = min(Mp * (1 + delta) / b1, (FtRd * a1 + Mp) / (a1 + b1) if FtRd * b1 > Mp else FtRd)
    h = (H / 2 - tf - 40) + (H / 2 - tf / 2)
    return dict(FtRd=FtRd / 1e3, FvRd=FvRd / 1e3, Tcap=Tcap / 1e3, MRd=2 * Tcap * h / 1e6, h=h,
                Ff=60e6 / (H - tf) / 1e3, Lf=bf + bf - tw)


class TestRegistro(unittest.TestCase):
    def test_dois_tipos_na_familia_emenda_achados_por_flange(self):
        ts = _tipos()
        for tipo, liga in ((VIGA, "emenda-viga"), (PILAR, "emenda-pilar")):
            with self.subTest(tipo=tipo):
                t = ts[tipo]
                self.assertEqual(t["familia"], "emenda")
                self.assertIn(liga, t["liga"])
                self.assertIn("flange", (t["nome"] + t["descricao"]).lower())
                self.assertTrue(t["sugerir"])
                self.assertEqual({e["nome"] for e in t["esforcos"]}, {"N", "V", "Vz", "M", "Mz", "T"})

    def test_seletor_de_perfil_com_I_H_e_U(self):
        c = {x["nome"]: x for x in _tipos()[VIGA]["campos"]}["perfil_viga"]
        self.assertTrue(c["resugerir"])
        for n in ("W310x38.7", "W250x73", "HP250x62", "IPE300", "HEA 200", "UPN 200", 'U 8" x 17,1', "U 100x50x2,65"):
            self.assertIn(n, c["perfis"])
        tipos = {secoes.catalogo(n).props["tipo"] for n in c["perfis"]}
        self.assertEqual(tipos, {"I", "U"})          # sem Ue, L, tubos (metalon e tubo redondo: ligacoes_tubos)

    def test_campos_de_chapa_parafusos_e_solda(self):
        nomes = {x["nome"] for x in _tipos()[PILAR]["campos"]}
        for k in ("tp", "forma", "b_chapa", "h_chapa", "estendida", "n_colunas", "g", "g2", "pf", "de",
                  "linhas_dentro", "s_linhas", "solda_mesa", "b_mesa", "b_alma", "contato", "atrito", "mu", "d_mm", "aco_chapa"):
            self.assertIn(k, nomes)


class TestCasoAMao(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.r = lr.verificar(VIGA, CASO_A, {"M": 60.0, "V": 100.0})
        cls.m = _mao()

    def test_momento_T_stub(self):
        v = _v(self.r, "momento: parafusos tracionados")
        self.assertAlmostEqual(v["solicitante"], 60.0, places=3)
        self.assertAlmostEqual(v["resistente"], self.m["MRd"], places=2)
        self.assertEqual(v["item"], "NBR 8800 6.3.3.1 / 6.3.5")

    def test_cisalhamento_dos_parafusos(self):
        v = _v(self.r, "parafusos: cisalhamento")
        self.assertAlmostEqual(v["solicitante"], 100.0 / 4, places=3)      # V/n, 4 parafusos
        self.assertAlmostEqual(v["resistente"], self.m["FvRd"], places=2)

    def test_tracao_com_alavanca_e_interacao(self):
        B = _v(self.r, "linha 1")   # flexão da chapa da 1ª linha tracionada
        self.assertIn("T-stub", B["nome"])
        self.assertAlmostEqual(B["solicitante"], 60.0 / (2 * self.m["h"] / 1e3), places=1)   # T = Mef/(2h)
        it = [x for x in self.r["verificacoes"] if x["item"] == "NBR 8800 6.3.3.4"]
        self.assertTrue(it)

    def test_solda_das_mesas(self):
        v = _v(self.r, "solda das mesas")
        self.assertAlmostEqual(v["solicitante"], self.m["Ff"], places=1)
        k = min(0.6 * 0.707 * 8 * 485 / 1.35, 0.6 * 8 * 250 / 1.10, 0.6 * 8 * 400 / 1.35)   # N/mm, metal base A-36
        self.assertAlmostEqual(v["resistente"], k * self.m["Lf"] / 1e3, places=1)

    def test_itens_de_norma(self):
        itens = {x["item"] for x in self.r["verificacoes"]}
        for i in ("NBR 8800 6.3.3.2", "NBR 8800 6.3.3.3", "NBR 8800 6.2.5 / tabela 8", "NBR 8800 6.3.9",
                  "NBR 8800 6.3.11.1 / tabela 14", "NBR 8800 6.5.5"):
            self.assertIn(i, itens)


class TestPadroes(unittest.TestCase):
    def test_padroes_de_fabrica_ate_0_90(self):
        ts = _tipos()
        for tipo, perfil in ((VIGA, "W310x38.7"), (PILAR, "W250x73")):
            with self.subTest(tipo=tipo):
                ex = ts[tipo]["exemplo"]
                self.assertEqual(ex["dados"]["perfil_" + ("viga" if tipo == VIGA else "pilar")], perfil)
                r = P.verificar_ligacao(tipo, ex["dados"], ex["esforcos"])
                self.assertTrue(r["ok"], r.get("criterio"))
                self.assertLessEqual(r["taxa"], 0.90 + 1e-9, r.get("criterio"))
        self.assertLess(ts[PILAR]["exemplo"]["esforcos"]["N"], 0)          # pilar comprimido
        self.assertGreater(ts[PILAR]["exemplo"]["esforcos"]["M"], 0)
        self.assertGreater(ts[VIGA]["exemplo"]["esforcos"]["M"], 0)

    def test_sugerir_upn_e_u_dobrado(self):
        for perfil, esf in (("UPN 200", {"M": 15.0, "V": 40.0}), ("U 200x75x3,00", {"M": 5.0, "V": 10.0})):
            with self.subTest(perfil=perfil):
                d = P.sugerir_ligacao(VIGA, {"perfil_viga": perfil}, esf)
                r = P.verificar_ligacao(VIGA, d, esf)
                self.assertTrue(r["ok"], r.get("criterio"))
                self.assertLessEqual(r["taxa"], 0.90 + 1e-9, r.get("criterio"))


class TestU(unittest.TestCase):
    def test_chapa_e_colunas_centradas_na_alma(self):
        p = secoes.catalogo("UPN 200").props
        d = P.sugerir_ligacao(VIGA, {"perfil_viga": "UPN 200"}, {"M": 15.0, "V": 40.0})
        r = lr.verificar(VIGA, d, {"M": 15.0, "V": 40.0})
        g = r["geometria"]
        yw = (p["tw"] / 2 - p["xg"]) * 1e3                      # plano médio da alma (o U abre para +y)
        ch = g["chapas"][0]
        ys = [q[0] for q in ch["contorno"]]
        self.assertAlmostEqual((min(ys) + max(ys)) / 2, yw, places=2)
        self.assertGreaterEqual(max(ys), (p["b"] - p["xg"]) * 1e3)   # cobre a ponta da mesa
        yb = sorted({round(b["origem"][1], 3) for b in g["parafusos"]})
        self.assertEqual(len(yb), 2)
        self.assertAlmostEqual(yb[0] + yb[1], 2 * yw, places=2)    # uma coluna dentro do U, outra atrás da alma
        self.assertLess(yb[0], -p["xg"] * 1e3)
        self.assertGreater(yb[1], (-p["xg"] + p["tw"]) * 1e3)
        self.assertTrue(any("cisalhamento" in a and "torção" in a for a in r["avisos"]))

    def test_abertura_negativa_espelha(self):
        d = dict(P.sugerir_ligacao(VIGA, {"perfil_viga": "UPN 200"}, {"M": 15.0, "V": 40.0}))
        g1 = lr.verificar(VIGA, d, {"M": 15.0})["geometria"]
        g2 = lr.verificar(VIGA, dict(d, abertura_y=-1), {"M": 15.0})["geometria"]
        self.assertEqual(sorted(-b["origem"][1] for b in g1["parafusos"]), sorted(b["origem"][1] for b in g2["parafusos"]))

    def test_recusas(self):
        for perfil in ("Ue 150x60x20x2,00", 'L 3" x 1/4"', "TC 168,3x6,4"):
            with self.subTest(perfil=perfil), self.assertRaises(ValueError):
                lr.verificar(VIGA, dict(CASO_A, perfil_viga=perfil), {"M": 10.0})


class TestGeometria(unittest.TestCase):
    def test_duas_chapas_encostadas_parafusos_soldas_e_trechos(self):
        r = lr.verificar(VIGA, dict(CASO_A, estendida="ambas", de=45.0), {"M": 60.0, "V": 100.0})
        g = r["geometria"]
        a, b = g["chapas"]
        self.assertEqual(a["nome"], "chapa de flange")
        self.assertEqual([a["origem"][0], b["origem"][0]], [-19.0, 0.0])          # face de A em −tp, B encosta em x = 0
        self.assertEqual(a["normal"], [1, 0, 0])
        self.assertEqual(a["contorno"], b["contorno"])
        zs = [q[1] for q in a["contorno"]]
        self.assertAlmostEqual(max(zs) - min(zs), 310 + 2 * (40 + 45), places=3)   # estendida dos dois lados
        self.assertEqual(len(g["parafusos"]), 4 * 2)                               # 4 linhas × 2 colunas
        for p in g["parafusos"]:
            self.assertEqual(p["origem"][0], -19.0)                                # cabeça na face de A
            self.assertEqual(p["eixo"], [1, 0, 0])                                 # porca do lado de B
            self.assertGreaterEqual(p["comprimento"], 2 * 19.0 + 0.9 * 22.225)
            self.assertEqual(p["campo"], "d_mm")
        xs = {s["pontos"][0][0] for s in g["soldas"]}
        self.assertEqual(xs, {-19.0, 19.0})                                        # solda em volta do perfil, nas duas chapas
        self.assertEqual(len(g["soldas"]), 2 * (2 * 3 + 2))                        # por chapa: 2 mesas × 3 cordões + 2 da alma
        m1, m2 = g["membros"]
        self.assertEqual(m1["campo"], "perfil_viga")
        x1 = [q[0] for pc in m1["pecas"] for q in pc["base"]]
        x2 = [q[0] for pc in m2["pecas"] for q in pc["base"]]
        self.assertAlmostEqual(max(x1), -19.0)
        self.assertAlmostEqual(min(x2), 19.0)
        self.assertEqual(len(m1["pecas"]), 3)                                      # alma + 2 mesas
        self.assertNotIn("vista", g)                                               # viga deitada

    def test_pilar_em_pe(self):
        ex = _tipos()[PILAR]["exemplo"]
        g = P.verificar_ligacao(PILAR, ex["dados"], ex["esforcos"])["geometria"]
        self.assertEqual(g["vista"], {"x": [0, 0, -1], "z": [1, 0, 0]})          # eixo da barra na vertical da elevação
        self.assertEqual(g["membros"][0]["barra"], "pilar")

    def test_quatro_colunas_e_chapa_quadrada(self):
        r2 = lr.verificar(VIGA, CASO_A, {"M": 60.0})
        r4 = lr.verificar(VIGA, dict(CASO_A, n_colunas=4, g2=140.0), {"M": 60.0})
        self.assertEqual(len(r4["geometria"]["parafusos"]), 2 * 4)
        self.assertGreater(_v(r4, "momento")["resistente"], _v(r2, "momento")["resistente"])
        q = lr.verificar(VIGA, dict(CASO_A, forma="quadrada"), {"M": 60.0})["geometria"]["chapas"][0]["contorno"]
        ys, zs = [p[0] for p in q], [p[1] for p in q]
        self.assertAlmostEqual(max(ys) - min(ys), max(zs) - min(zs), places=3)

    def test_linhas_dentro_e_fora(self):
        r = lr.verificar(VIGA, dict(CASO_A, estendida="ambas", linhas_dentro=0, de=45.0), {"M": 60.0})
        zs = sorted({p["origem"][2] for p in r["geometria"]["parafusos"]})
        self.assertEqual(zs, [-195.0, 195.0])                                     # só fora das mesas: ±(H/2 + pf)
        r = lr.verificar(VIGA, dict(CASO_A, linhas_dentro=2, s_linhas=75.0), {"M": 60.0})
        zs = sorted({p["origem"][2] for p in r["geometria"]["parafusos"]})
        self.assertEqual(zs, [-105.3, -30.3, 30.3, 105.3])                        # 2 linhas por mesa, passo 75
        with self.assertRaises(ValueError):                                       # sem parafusos
            lr.verificar(VIGA, dict(CASO_A, linhas_dentro=0), {"M": 60.0})

    def test_chapa_informada_menor_que_o_perfil_reprova(self):
        r = lr.verificar(VIGA, dict(CASO_A, b_chapa=150.0), {"M": 60.0})
        self.assertFalse(_v(r, "chapa cobre o perfil (largura)")["ok"])


class TestContatoAtritoEsforcos3D(unittest.TestCase):
    PIL = dict(perfil_pilar="W250x73", d_mm=22.225, tp=22.4, estendida="ambas", g=100.0, pf=40.0, de=45.0,
               b_mesa=8.0, b_alma=5.0)

    def test_contato_tira_a_compressao_da_solda(self):
        e = {"N": -1500.0, "M": 40.0, "V": 30.0}
        com = lr.verificar(PILAR, dict(self.PIL, contato=True), e)
        sem = lr.verificar(PILAR, dict(self.PIL, contato=False), e)
        braco = 0.253 - 0.0142
        self.assertAlmostEqual(_v(com, "solda das mesas")["solicitante"], 40 / braco, places=1)        # só a tração
        self.assertAlmostEqual(_v(sem, "solda das mesas")["solicitante"], 40 / braco + 750, places=1)  # M/braço + N/2
        c = _v(com, "contato usinado")
        self.assertEqual(c["item"], "NBR 8800 6.6.1")
        self.assertAlmostEqual(c["resistente"], 1.8 * 250e3 * 0.254 * 0.0142 / 1.35, places=1)       # fy da chapa A-36
        self.assertFalse(any(x["nome"].startswith("contato usinado") for x in sem["verificacoes"]))

    def test_atrito(self):
        r = lr.verificar(PILAR, dict(self.PIL, atrito=True, mu=0.35), {"V": 100.0, "M": 10.0})
        v = _v(r, "parafusos: deslizamento")
        self.assertEqual(v["item"], "NBR 8800 6.3.4.3")
        Ab = math.pi * 22.225 ** 2 / 4
        FTb = 0.70 * 825 * 0.75 * Ab / 1e3                                     # protensão mínima ≈ tabela 15 (173 kN)
        self.assertAlmostEqual(FTb, 168.0, delta=6.0)
        self.assertLessEqual(v["resistente"], 1.13 * 0.35 * FTb / 1.20 + 1e-6)
        with self.assertRaises(ValueError):
            lr.verificar(PILAR, dict(self.PIL, atrito=True, tipo_parafuso="A307"), {"V": 50.0})

    def test_mz_e_torcao(self):
        base = lr.verificar(PILAR, self.PIL, {"M": 40.0, "V": 30.0})
        r = lr.verificar(PILAR, self.PIL, {"M": 40.0, "V": 30.0, "Vz": 20.0, "Mz": 10.0, "T": 5.0})
        self.assertGreater(_v(r, "parafusos: cisalhamento")["solicitante"], _v(base, "parafusos: cisalhamento")["solicitante"])
        self.assertGreater(_v(r, "solda das mesas")["solicitante"], _v(base, "solda das mesas")["solicitante"])
        self.assertFalse(any("não considerados" in a for a in r["avisos"]))


if __name__ == "__main__":
    unittest.main()
