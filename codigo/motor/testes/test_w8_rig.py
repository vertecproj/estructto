"""W8-RIG (docs/v3/W8-RIG.md): padrões de fábrica das rígidas e emendas com folga (taxa ≤ 0,90), campos sim/não do
módulo de rígidas sem "opcoes" (a tela mostrava true/false e gravava o texto "false", que o bool() lia como verdadeiro)
e o painel de alma desenhando a viga soldada.

Valores à mão (NBR 8800 tabela 14, serra/tesoura; CBCA 2.4.3 e 6.1.1):
  borda = ⌈mín/0,9⌉₅: ⅝" 29 → 32,2 → 35; ¾" 32 → 35,6 → 40; ⅞" 38 → 42,2 → 45; 1" 44 → 48,9 → 50 mm.
  passo = max(CBCA 60/75; ⌈3d⌉₅) = 60, 75, 75, 80 mm (3d = 2,7d/0,9).
  pf = ⌈pf_mín/0,9⌉₅: 30 → 35 (⅝", ¾"), 35 → 40 (⅞"), 40 → 45 (1").
  Emenda W310×38,7 (d 310, bf 165, tw 5,8, tf 9,7, r 10): hw − 2r = 310 − 19,4 − 20 = 270,6 → tala da alma ≤ 0,9·270,6 =
  243,5 → 240 (3 linhas a 85 + 2 × 35); tala da mesa ≥ tf 9,7 → 12,5 (comercial); g = ⌊min(165/2 + 10; 165 − 2·29/0,9)⌋₅ = 90.
  Emenda W250×73 (tf 14,2): tala da mesa 16."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes_rigidas as lr

RIGIDAS = ("chapa_topo_ajustada", "chapa_topo_estendida", "flange_plate", "painel_alma_pilar", "emenda_viga",
           "emenda_pilar")


def _exemplos():
    ts = P.tipos_ligacao()
    return {t["tipo"]: t["exemplo"] for t in (ts if isinstance(ts, list) else ts["tipos"])}


class TestRegrasDeDetalhe(unittest.TestCase):
    def test_borda_passo_e_pf_com_folga(self):
        for d, borda, passo in ((15.875, 35, 60), (19.05, 40, 75), (22.225, 45, 75), (25.4, 50, 80)):
            with self.subTest(d=d):
                self.assertEqual(lr._borda(d), borda)
                self.assertEqual(lr._passo(d), passo)
        self.assertEqual([lr._com_folga(p) for p in (30, 35, 40)], [35, 40, 45])
        self.assertEqual(lr._comercial(9.7), 12.5)
        self.assertEqual(lr._comercial(14.2), 16)

    def test_buscar_sem_alvo_devolve_a_primeira_que_passa(self):
        # alvo 0: nenhuma combinação chega; volta o comportamento antigo (1ª aprovada da grade)
        base = dict(_exemplos()["emenda_viga"]["dados"])
        e = {"M": 80, "V": 100}       # n = 1 e 2 reprovam; n = 3 passa a 0,94 (acima do alvo)
        grade = [dict(n_por_linha_mesa=n) for n in (1, 2, 3, 4)]
        oks = [lr.verificar("emenda_viga", dict(base, **x), e)["ok"] for x in grade]
        self.assertFalse(oks[0])          # 1 parafuso por linha não passa: o 1º aprovado não é o 1º da grade
        d = lr._buscar("emenda_viga", base, grade, e, alvo=0.0)
        self.assertEqual(d["n_por_linha_mesa"], grade[oks.index(True)]["n_por_linha_mesa"])


class TestPadroesDeFabrica(unittest.TestCase):
    """Os seis padrões da biblioteca (W310×38,7 / W250×73, V = 100 kN, M = 60 kN·m): taxa ≤ 0,90 em tudo, inclusive
    nas disposições construtivas (antes: chapa de topo 1,00 no pf; emendas 0,97 na borda da tala; flange plate 0,96)."""

    def test_taxa_governante_ate_0_90(self):
        ex = _exemplos()
        for tipo in RIGIDAS:
            with self.subTest(tipo=tipo):
                r = P.verificar_ligacao(tipo, ex[tipo]["dados"], ex[tipo]["esforcos"])
                self.assertTrue(r["ok"])
                self.assertLessEqual(r["taxa"], 0.90 + 1e-9, r.get("criterio"))

    def test_emenda_de_viga_coerente(self):
        d = _exemplos()["emenda_viga"]["dados"]
        self.assertEqual(d["t_tala_mesa"], 12.5)
        self.assertEqual(d["g_mesa"], 90)
        self.assertEqual(d["d_mesa_mm"], d["d_alma_mm"])
        ev, s = lr._borda(d["d_alma_mm"]), lr._passo(d["d_alma_mm"])
        self.assertEqual(d["e_tala_alma"], ev)
        self.assertEqual(d["s_mesa"], s)
        self.assertGreaterEqual(d["s_alma"], s)
        self.assertEqual((d["h_tala_alma"] - (d["n_linhas_alma"] - 1) * d["s_alma"]) / 2, ev)   # borda vertical
        self.assertLessEqual(d["h_tala_alma"], 0.9 * 270.6)
        self.assertEqual(d["h_tala_alma"], 240)

    def test_emenda_de_pilar_tala_comercial(self):
        self.assertEqual(_exemplos()["emenda_pilar"]["dados"]["t_tala_mesa"], 16)

    def test_chapa_de_topo_pf_com_folga(self):
        ex = _exemplos()
        for tipo in ("chapa_topo_ajustada", "chapa_topo_estendida"):
            with self.subTest(tipo=tipo):
                d = ex[tipo]["dados"]
                pf_min = next(p for dd, p in lr._PF_MIN if d["d_mm"] <= dd + 0.05) * 1000
                self.assertEqual(d["pf"], lr._com_folga(pf_min))
                r = P.verificar_ligacao(tipo, d, ex[tipo]["esforcos"])
                v = next(x for x in r["verificacoes"] if x["nome"].startswith("furo → face da mesa"))
                self.assertLessEqual(v["taxa"], 0.90)


class TestCamposSimNao(unittest.TestCase):
    def test_booleanos_sem_opcoes(self):
        for tipo, reg in lr.TIPOS.items():
            for nome, ent in reg["entradas"].items():
                if isinstance(ent["padrao"], bool):
                    with self.subTest(tipo=tipo, campo=nome):
                        self.assertNotIn("opcoes", ent)

    def test_texto_false_e_falso(self):
        base = dict(perfil_viga="W310x38.7", perfil_pilar="W250x73")
        e = {"M": 60, "V": 100}
        for v, tem in ((True, True), ("true", True), ("sim", True), (False, False), ("false", False), ("não", False)):
            with self.subTest(v=v):
                r = lr.verificar("painel_alma_pilar", dict(base, mesa_soldada=v), e)
                self.assertEqual(any(x["item"] == "NBR 8800 5.7.2" for x in r["verificacoes"]), tem)
                self.assertEqual(bool(r["geometria"]["soldas"]), tem)


class TestPainelDesenho(unittest.TestCase):
    """Viga soldada na mesa do W250×73 (d = 253): face em x = 126,5 mm; viga encostada (folga 0); mesas com penetração
    total (4 cordões, nas duas faces de cada mesa) e alma com filete dos dois lados (perna 5 mm ≤ tw 5,8)."""

    def test_viga_soldada_na_face(self):
        ex = _exemplos()["painel_alma_pilar"]
        g = P.verificar_ligacao("painel_alma_pilar", ex["dados"], ex["esforcos"])["geometria"]
        pen = [s for s in g["soldas"] if s["tipo"] == "penetracao"]
        fil = [s for s in g["soldas"] if s["tipo"] == "filete"]
        self.assertEqual((len(pen), len(fil)), (4, 2))
        self.assertTrue(all(p[0] == 126.5 for s in g["soldas"] for p in s["pontos"]))
        self.assertEqual([abs(s["pontos"][0][1]) for s in fil], [2.9, 2.9])
        self.assertEqual(fil[0]["perna"], 5.0)
        self.assertAlmostEqual(g["pontas"][0]["plano"]["ponto"][0], 0.1265)
        self.assertEqual(g["pontas"][0]["folga"], 0.0)
        self.assertTrue(all(s["campo"] == "mesa_soldada" for s in g["soldas"]))

    def test_com_enrijecedores_e_reforco_desenha_as_chapas(self):
        d = dict(perfil_viga="W310x38.7", perfil_pilar="W250x73", t_enr_pilar=12.5, t_reforco=9.5)
        g = lr.verificar("painel_alma_pilar", d, {"M": 180, "V": 100})["geometria"]
        nomes = [c["nome"] for c in g["chapas"]]
        self.assertEqual(nomes.count("enrijecedor do pilar"), 4)
        self.assertEqual(nomes.count("chapa de reforço da alma"), 2)


if __name__ == "__main__":
    unittest.main()
