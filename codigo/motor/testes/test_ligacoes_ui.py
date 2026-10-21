"""Projetista de ligações (docs/v3/LIGACOES-UI.md): esquema enriquecido da biblioteca, exemplo de cada tipo, geometria
esquemática dos tipos antigos, otimizador simples, memorial HTML, rotas web e verificador genérico dos grupos.

Conferência do otimizador à mão (chapa simples antiga, V = 180 kN, d = 16 mm A325, rosca no plano):
Fv,Rd = 0,4·(π·16²/4)·825/1,35 = 39,3 kN por parafuso → 3 parafusos com V·e não bastam (taxa 1,91); o otimizador tem
de chegar a um detalhe que passa e do qual nenhum item desce um degrau sem reprovar (mínimo local de material)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.modelo import CargaNodal

try:
    from test_web import BaseWeb
except ImportError:   # rodando de outra pasta
    from testes.test_web import BaseWeb

CHAPA = dict(h=0.23, t=0.0063, d_mm=16, n=3, s=0.075, e=0.06, tw=0.0058)


class TestBiblioteca(unittest.TestCase):
    def test_todo_tipo_tem_familia_e_exemplo_que_verifica_com_geometria(self):
        tipos = P.tipos_ligacao()
        self.assertGreaterEqual(len(tipos), 4)
        for t in tipos:
            with self.subTest(t["tipo"]):
                self.assertTrue(t["nome"] and t["familia"] and isinstance(t["liga"], list))
                r = P.verificar_ligacao(t["tipo"], t["exemplo"]["dados"], t["exemplo"]["esforcos"])
                self.assertIn("taxa", r)
                self.assertIn("taxa_max", r)
                g = r["geometria"]
                self.assertTrue(g["chapas"] or g["cantoneiras"] or g["membros"], "miniatura vazia")
                self.assertIsInstance(g["membros"], list)

    def test_esquema_antigo_ganha_faixa_e_inteiros(self):
        cs = next(t for t in P.tipos_ligacao() if t["tipo"] == "chapa_simples")
        n = next(c for c in cs["campos"] if c["nome"] == "n")
        self.assertEqual((n["min"], n["max"], n["inteiro"]), (2, 20, True))
        self.assertFalse(cs["sugerir"])

    def test_geometria_chapa_simples_antiga(self):
        g = L.geometria_basica("chapa_simples", CHAPA)
        zs = sorted(p["origem"][2] for p in g["parafusos"])
        self.assertEqual(zs, [-75.0, 0.0, 75.0])                  # n = 3, s = 75 mm, centrados
        self.assertEqual(g["chapas"][0]["contorno"][2], [100.0, 230.0])   # e + eh = 60 + 40; h = 230
        self.assertEqual(len(g["soldas"]), 2)


class TestOtimizar(unittest.TestCase):
    def test_chapa_simples_antiga_reprovada_passa_com_minimo_local(self):
        self.assertFalse(L.verificar_ligacao("chapa_simples", CHAPA, {"V": 180})["ok"])
        r = L.otimizar("chapa_simples", CHAPA, {"V": 180})
        self.assertTrue(r["ok"])
        self.assertTrue(L.verificar_ligacao("chapa_simples", r["dados"], {"V": 180})["ok"])
        self.assertTrue(r["mudancas"])
        for nome, _papel, lista in L.botoes_otimizar("chapa_simples"):   # nenhum item desce sem reprovar
            menores = [x for x in lista if x < float(r["dados"][nome]) - 1e-9]
            if not menores:
                continue
            d = dict(r["dados"], **{nome: int(max(menores)) if nome == "n" else max(menores)})
            try:
                ok = L.verificar_ligacao("chapa_simples", d, {"V": 180})["ok"]
            except ValueError:
                ok = False
            self.assertFalse(ok, f"{nome} ainda podia descer")

    def test_tipo_do_contrato(self):
        ex = next(t for t in P.tipos_ligacao() if t["tipo"] == "chapa_simples_alma")["exemplo"]
        esf = {"V": 160.0, "N": 0.0}
        self.assertFalse(L.verificar_ligacao("chapa_simples_alma", ex["dados"], esf)["ok"])
        r = P.otimizar_ligacao("chapa_simples_alma", ex["dados"], esf)
        self.assertTrue(r["ok"])
        self.assertIn("geometria", r["resultado"])

    def test_perna_acima_do_maximo_desce(self):
        # E2E-05: perna 8 mm em chapa de 5 mm (máx. = t, NBR 8800 6.2.6.2.2) — o otimizador desce a perna
        d, esf = dict(t_base=0.005, b=0.008, L=0.1), {"F": 20}
        r0 = L.verificar_ligacao("solda_filete", d, esf)
        x = next(v for v in r0["verificacoes"] if "dimensão máxima" in v["nome"])
        self.assertEqual((x["sentido"], x["parametro"]), ("desce", "b"))
        r = L.otimizar("solda_filete", d, esf)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["dados"]["b"], 0.005 + 1e-12)

    def test_chapa_topo_alma_fina_e2e05(self):
        # E2E-05 (fluxo A, joelho): W310x38.7 (tw = 5,8 mm) com o padrão b_alma = 6 mm → perna máxima 6,0/5,8
        d, esf = dict(perfil_viga="W310x38.7", perfil_pilar="W310x38.7"), {"M": 40, "V": 30}
        r0 = L.verificar_ligacao("chapa_topo_estendida", d, esf)
        x = next(v for v in r0["verificacoes"] if v["nome"] == "solda da alma: perna máxima")
        self.assertFalse(x["ok"])
        self.assertEqual((x["sentido"], x["parametro"]), ("desce", "b_alma"))
        r = L.otimizar("chapa_topo_estendida", d, esf)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["dados"]["b_alma"], 5.8)
        self.assertNotIn("b_mesa", r["mudancas"])   # solda de penetração nas mesas: b_mesa não tem efeito, fica

    def test_ja_passa_so_desce(self):
        r = L.otimizar("chapa_simples", dict(CHAPA, t=0.0125, d_mm=22.225), {"V": 60})
        self.assertTrue(r["ok"])
        self.assertTrue(all(p["para"] < p["de"] for p in r["passos"]))


class TestMemorial(unittest.TestCase):
    def test_secao_com_verificacoes_e_formula(self):
        h = P.memorial_ligacao("chapa_simples", dict(CHAPA, t=0.0095, d_mm=19.05), {"V": 120})["html"]
        self.assertTrue(h.startswith('<section class="memorial-ligacao">'))
        self.assertIn("6.3.3.2", h)
        self.assertIn("A<sub>b</sub>", h)                       # expressão do cisalhamento do parafuso
        self.assertIn("Espessura da chapa", h)                  # rótulo do campo, não o nome
        self.assertEqual(h.count("<tr class="), len(P.verificar_ligacao("chapa_simples", dict(CHAPA, t=0.0095, d_mm=19.05),
                                                                         {"V": 120})["verificacoes"]))


class TestWebProjetista(BaseWeb):
    def test_rotas(self):
        r = self.api("POST", "/api/ligacoes/otimizar", {"tipo": "chapa_simples", "dados": CHAPA, "esforcos": {"V": 180}})
        self.assertTrue(r["ok"] and r["resultado"]["ok"])
        s = self.api("POST", "/api/ligacoes/sugerir", {"tipo": "chapa_simples_alma",
                                                       "perfis": {"perfil_viga": "W310x38.7", "perfil_suporte": "W250x73"},
                                                       "esforcos": {"V": 150}})
        self.assertEqual(s["perfil_viga"], "W310x38.7")
        self.api("POST", "/api/ligacoes/sugerir", {"tipo": "chapa_simples", "perfis": {}, "esforcos": {}}, status=422)
        m = self.api("POST", "/api/ligacoes/memorial", {"tipo": "chapa_simples", "dados": dict(CHAPA, t=0.0095), "esforcos": {"V": 100}})
        self.assertIn("memorial-ligacao", m["html"])


class TestGrupoContrato(unittest.TestCase):
    def test_projeto_do_contrato_no_grupo(self):
        """Grupo viga–pilar com chapa_simples_alma: o verificador genérico recebe V = |Vz| (cortante no plano da alma)."""
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "portico_plano", "material": "A-572 GR.50",
                              "secoes": {"pilar": "W310x38.7", "viga": "W360x51"}, "vaos_x": [6], "alturas": [3]})
        m.add(CargaNodal("Q1", "N1-0-0", (0.0, 0.0, -10.0, 0.0, 0.0, 0.0)))
        P.ligacoes_identificar(m)
        gid = next(k for k, g in m.ligacoes["grupos"].items() if g["tipo"].startswith("viga_pilar"))
        dados = dict(perfil_viga="W360x51", perfil_suporte="W310x38.7", apoio="mesa_pilar")
        P.ligacoes_editar(m, {"acao": "projeto", "grupo": gid, "tipo_ligacao": "chapa_simples_alma", "dados": dados})
        c = P.calcular(m)
        r = P.ligacoes_dimensionar(m, c, gid)
        self.assertEqual(r["erros"], [])
        self.assertTrue(all(x["taxa"] is not None for x in r["membros"]))
        e = r["membros"][0]["esforcos"]
        self.assertEqual(lm.esforcos_contrato(e)["V"], abs(e["Vz"]))


if __name__ == "__main__":
    unittest.main()
