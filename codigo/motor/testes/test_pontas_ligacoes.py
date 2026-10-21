"""G2: toda ligação do motor preenche geometria["pontas"] (docs/v3/PONTAS-CONTRATO.md) e o cálculo usa essa geometria.

Conferência à mão (test_calculo_usa_geometria): chapa de topo de W310x38.7 (bf = 165 mm) na alma de um W150x13
(h = 148, tf = 4,9 mm). Vão livre entre mesas do pilar: 148/2 − 4,9 = 69,1 mm < bf/2 = 82,5 mm → cope esq e dir,
altura = tf + 12 = 16,9 mm cada. Largura real da ponta = 165 − 2·16,9 = 131,2 mm; solda das mesas (filetes dos dois
lados) resiste 2·131,2 mm·FwRd, não 2·165 mm·FwRd."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco import placa_base


class TestTodosOsTipos(unittest.TestCase):
    def test_19_tipos_tem_pontas(self):
        tipos = P.tipos_ligacao()
        self.assertEqual(len(tipos), 19)
        for t in tipos:
            with self.subTest(t["tipo"]):
                r = L.verificar_ligacao(t["tipo"], t["exemplo"]["dados"], t["exemplo"]["esforcos"])
                pts = r["geometria"]["pontas"]
                self.assertTrue(pts)
                for p in pts:
                    self.assertEqual(set(p), {"barra", "extremo", "plano", "folga", "recortes"})
                    self.assertAlmostEqual(math.hypot(*p["plano"]["normal"]), 1.0)

    def test_placa_base_ponta_na_face_superior(self):
        sec = secoes.catalogo("W310x38.7")
        r = placa_base.verificar_placa_base(dict(H=0.45, B=0.3, esp=0.025), dict(N=-100, V=10, M=20), sec.props)
        (p,) = r["geometria"]["pontas"]
        self.assertEqual(p["recortes"], [])
        self.assertAlmostEqual(p["plano"]["ponto"][0], 0.025)        # x local = ao longo do pilar, subindo
        self.assertEqual(p["plano"]["normal"], [-1.0, 0.0, 0.0])


class TestCalculoUsaGeometria(unittest.TestCase):
    D = dict(perfil_viga="W310x38.7", perfil_pilar="W150x13", tp=19, d_mm=19.05, solda_mesa="filete", b_mesa=8)
    E = {"M": 40, "V": 50}

    def _solda_mesas(self, r):
        return next(v for v in r["verificacoes"] if v["nome"].startswith("solda das mesas (filetes"))

    def test_calculo_usa_geometria(self):
        reto = L.verificar_ligacao("chapa_topo_ajustada", self.D, self.E)
        rec = L.verificar_ligacao("chapa_topo_ajustada", dict(self.D, contexto_pontas={"tipo_no": "viga_pilar_alma"}),
                                  self.E)
        (p,) = rec["geometria"]["pontas"]
        self.assertEqual(sorted(c["face"] for c in p["recortes"]), ["dir", "esq"])
        largura = 0.165 - 2 * (0.0049 + 0.012)
        fw_reto, fw_rec = self._solda_mesas(reto)["resistente"], self._solda_mesas(rec)["resistente"]
        self.assertAlmostEqual(fw_rec / fw_reto, largura / 0.165, places=3)
        # chapa encostada na alma do pilar: face da chapa a tw/2, ponta da viga a tw/2 + tp
        self.assertAlmostEqual(p["plano"]["ponto"][0], 0.0043 / 2 + 0.019, places=4)


class TestBarraCurta(unittest.TestCase):
    """Review Focus 1: recuo + recorte ≥ comprimento → aviso, ok = False, motivo; a barra fica."""

    def test_barra_curta_vira_aviso(self):
        d = dict(TestCalculoUsaGeometria.D, contexto_pontas={"comprimento": 0.05})
        r = L.verificar_ligacao("chapa_topo_ajustada", d, TestCalculoUsaGeometria.E)
        self.assertFalse(r["ok"])
        self.assertEqual(r["motivo"], "barra curta demais para a ligação")
        self.assertTrue(any("barra curta demais" in a for a in r["avisos"]))
        self.assertTrue(r["geometria"]["pontas"])

    def test_barra_curta_no_grupo(self):
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                              "secoes": {"pilar": "W310x38.7", "viga": "W360x51"}, "vaos_x": [0.15], "alturas": [3]})
        P.ligacoes_identificar(m)
        gid = next(k for k, g in m.ligacoes["grupos"].items() if g["tipo"] == "viga_pilar_mesa")
        m.ligacoes["grupos"][gid]["projeto"] = dict(tipo_ligacao="chapa_topo_ajustada",
                                                    dados=dict(perfil_viga="W360x51", perfil_pilar="W310x38.7"))
        c = P.calcular(m)
        r = lm.dimensionar_grupo(m, c, gid)
        self.assertFalse(r["ok"])
        self.assertTrue(all(x["motivo"] == "barra curta demais para a ligação" for x in r["membros"]))



# ---------------------------------------------------------------- rodada 1 de correções (G2)
_EX = {t["tipo"]: t["exemplo"] for t in P.tipos_ligacao()}


class TestCopeNoCalculo(unittest.TestCase):
    """Cope da ponta entra nas verificações da viga recortada (docs/v3/PONTAS-CONTRATO.md, G2 rodada 1).
    W310x38.7 na alma de W250x73 (tf = 14,2 mm), V = 100 kN: sem recorte (mesa do pilar) passa; cope da ponta com
    cope_folga_alt = 40 mm → altura 54,2 mm em cima e embaixo → início de escoamento da seção recortada ≈ 1,21."""

    def test_cope_profundo_reprova_sem_cope_passa(self):
        for t in ("chapa_simples_alma", "cantoneira_dupla_alma"):
            with self.subTest(t):
                e = _EX[t]
                reto = L.verificar_ligacao(t, dict(e["dados"], apoio="mesa_pilar"), e["esforcos"])
                self.assertTrue(reto["ok"])
                self.assertFalse(any("recortada" in v["nome"] for v in reto["verificacoes"]))
                fundo = L.verificar_ligacao(t, dict(e["dados"], apoio="alma_viga", contexto_pontas=dict(
                    params=dict(cope_folga_alt=0.040))), e["esforcos"])
                self.assertFalse(fundo["ok"])
                alturas = [c["altura"] for c in fundo["geometria"]["pontas"][0]["recortes"]]
                self.assertEqual([round(a, 4) for a in alturas], [0.0542, 0.0542])
                nomes = {v["nome"]: v for v in fundo["verificacoes"]}
                esc = nomes["viga recortada (superior): início de escoamento por flexão"]
                self.assertGreater(esc["taxa"], 1.0)
                self.assertIn("viga recortada (dois recortes): flambagem local da alma", nomes)
                self.assertIn("alma da viga recortada: colapso por rasgamento", nomes)

    def test_viga_viga_recorte_desenho_igual_calculo(self):
        """Recorte = max(Gerdau 3.3, cope da ponta); a ponta desenha o mesmo (W310x38.7 em W250x73, V = 100 kN)."""
        e = _EX["viga_viga_recorte"]
        r = L.verificar_ligacao("viga_viga_recorte", e["dados"], e["esforcos"])
        self.assertAlmostEqual(r["taxa"], 1.197, places=3)
        copes = {c["face"]: c for c in r["geometria"]["pontas"][0]["recortes"]}
        for f in ("sup", "inf"):
            self.assertAlmostEqual(copes[f]["altura"] * 1e3, r["valores"][f"recorte_{f}_h_mm"])
            self.assertAlmostEqual(copes[f]["comprimento"] * 1e3, r["valores"][f"recorte_{f}_c_mm"])


class TestComprimentoLivre(unittest.TestCase):
    D = dict(perfil_viga="W360x51", perfil_pilar="W310x38.7")

    def _grupo(self, vao):
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                              "secoes": {"pilar": "W310x38.7", "viga": "W360x51"}, "vaos_x": [vao], "alturas": [3]})
        P.ligacoes_identificar(m)
        gid = next(k for k, g in m.ligacoes["grupos"].items() if g["tipo"] == "viga_pilar_mesa")
        m.ligacoes["grupos"][gid]["projeto"] = dict(tipo_ligacao="chapa_topo_ajustada", dados=dict(self.D))
        return lm.dimensionar_grupo(m, P.calcular(m), gid)

    def test_duas_pontas_60_por_cento(self):
        x = L.pontas_de("chapa_topo_ajustada", self.D)[0]["plano"]["ponto"][0]   # h/2 + tp
        curto = self._grupo(round(x / 0.6, 3))
        self.assertTrue(all(m["motivo"] == "barra curta demais para a ligação" for m in curto["membros"]))
        longo = self._grupo(round(x / 0.4, 3))
        self.assertTrue(all(m["motivo"] is None for m in longo["membros"]))

    def test_desloc_reduz_comprimento_livre(self):
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                              "secoes": {"pilar": "W310x38.7", "viga": "W360x51"}, "vaos_x": [6], "alturas": [3]})
        P.ligacoes_identificar(m)
        g = next(g for g in m.ligacoes["grupos"].values() if g["tipo"] == "viga_pilar_mesa")
        mb = g["membros"][0]
        m.barras[mb["barra"]].params.update(desloc_i=0.5, desloc_j=0.25)
        ctx = lm._contexto_pontas(m, g["tipo"], mb)
        self.assertAlmostEqual(ctx["comprimento"], 6 - 0.75)
        self.assertAlmostEqual(ctx["perfil_apoio"]["h"], secoes.catalogo("W310x38.7").props["h"])


class TestGussetUsaPonta(unittest.TestCase):
    def test_folga_muda_whitmore_e_bordas(self):
        for t in ("gusset_contraventamento", "no_trelica_gusset"):
            with self.subTest(t):
                rs = [L.verificar_ligacao(t, dict(_EX[t]["dados"], contexto_pontas=dict(params=dict(folga=f))),
                                          {"N": -150}) for f in (0.012, 0.4)]
                xs = [r["geometria"]["pontas"][0]["plano"]["ponto"][0] for r in rs]
                self.assertGreater(xs[1], xs[0] + 0.1)
                comp = [next(v for v in r["verificacoes"] if "compressão (Whitmore" in v["nome"]) for r in rs]
                self.assertGreater(comp[1]["taxa"], comp[0]["taxa"])   # Whitmore mais longe da borda apoiada
                if t == "gusset_contraventamento":
                    self.assertAlmostEqual(rs[1]["geometria"]["recuo"] / 1e3, xs[1])   # desenho = cálculo
                    bor = [next(v for v in r["verificacoes"] if "afastada do pilar" in v["nome"]) for r in rs]
                    self.assertNotEqual(bor[0]["taxa"], bor[1]["taxa"])


class TestApoioSemPerfil(unittest.TestCase):
    def test_aviso_e_contexto(self):
        e = _EX["gusset_contraventamento"]
        r = L.verificar_ligacao("gusset_contraventamento", e["dados"], e["esforcos"])
        self.assertIn("apoio sem perfil: ponta aproximada", r["avisos"])
        ctx = dict(perfil_apoio=secoes.catalogo("W310x38.7").props)
        r = L.verificar_ligacao("gusset_contraventamento", dict(e["dados"], contexto_pontas=ctx), e["esforcos"])
        self.assertNotIn("apoio sem perfil: ponta aproximada", r["avisos"])


class TestBarraCurtaClasse(unittest.TestCase):
    def test_excecao_dedicada(self):
        from prossen_estrutural.aco import pontas
        self.assertTrue(issubclass(pontas.BarraCurta, ValueError))
        with self.assertRaises(pontas.BarraCurta):
            pontas.pontas_locais("emenda", recuo=0.5, comprimento=0.4)

    def test_placa_triagem_parcial_tem_pontas(self):
        casos = P._casos_placa(None, None, {"secao": "W310x38.7", "esforcos": [
            dict(N=-100, V=10, M=900), dict(N=-50, V=5, M=10)]})
        out = P._placa_pior(dict(H=0.45, B=0.3, esp=0.025), casos, 0)
        self.assertTrue(out["parcial"])
        self.assertAlmostEqual(out["resultado"]["geometria"]["pontas"][0]["plano"]["ponto"][0], 0.025)


if __name__ == "__main__":
    unittest.main()
