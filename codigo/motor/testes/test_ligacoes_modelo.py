"""Grupos de ligações do modelo (aco/ligacoes_modelo.py, docs/v3/LIGACOES-GRUPOS.md): identificação, edições do
usuário que sobrevivem à re-identificação, pior membro do grupo e placa de base dimensionada pela pior de 4 bases."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco import placa_base
from prossen_estrutural.modelo import CargaNodal

ACO = "A-572 GR.50"
W = {"pilar": "W310x38.7", "viga": "W360x51"}
CHAPA = dict(h=0.23, t=0.0095, d_mm=19.05, n=3, s=0.075, e=0.06)


def _portico():
    m = P.novo_modelo()
    P.gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": W, "vaos_x": [6, 6], "alturas": [3, 3]})
    m.add(CargaNodal("Q1", "N2-0-0", (0.0, 0.0, -40.0, 0.0, 0.0, 0.0)))   # assimetria: uma ponta governa
    return m


def _por_tipo(m):
    out = {}
    for g in m.ligacoes["grupos"].values():
        out.setdefault(g["tipo"], []).append(len(g["membros"]))
    return {k: sorted(v) for k, v in out.items()}


class TestIdentificar(unittest.TestCase):
    def test_portico_2_vaos_2_andares(self):
        """3 bases iguais (1 grupo); viga–pilar no 1º andar (pilar contínuo: 4 pontas) e no topo (pilar termina:
        4 pontas) = 2 grupos; pilares contínuos não são ligação. Total 3 grupos, 11 membros."""
        m = _portico()
        r = P.ligacoes_identificar(m)
        self.assertEqual((r["grupos"], r["membros"]), (3, 11))
        self.assertEqual(_por_tipo(m), {"base": [3], "viga_pilar_mesa": [4, 4]})
        assin = sorted(g["assinatura"] for g in m.ligacoes["grupos"].values())
        self.assertIn("viga_pilar_mesa|W360x51|W310x38.7|pilar continuo|90|rigida|A-572 GR.50", assin)
        # sobrevive ao JSON (versao_formato 2 só com grupos)
        d = P.para_dict(m)
        self.assertEqual(d["versao_formato"], 2)
        self.assertEqual(P.de_dict(d).ligacoes, m.ligacoes)
        self.assertEqual(P.para_dict(P.novo_modelo())["versao_formato"], 1)

    def test_galpao_tipos(self):
        m = P.abrir_exemplo("galpao_aco")
        P.ligacoes_identificar(m)
        tipos = _por_tipo(m)
        self.assertEqual(tipos["base"], [12])
        self.assertEqual(tipos["emenda"], [6])               # cumeeira dos 6 pórticos
        self.assertIn("gusset_contraventamento", tipos)

    def test_edicoes_sobrevivem(self):
        m = _portico()
        P.ligacoes_identificar(m)
        gs = m.ligacoes["grupos"]
        topo = next(k for k, g in gs.items() if "termina" in g["assinatura"])
        base = next(k for k, g in gs.items() if g["tipo"] == "base")
        P.ligacoes_editar(m, {"acao": "renomear", "grupo": topo, "nome": "Cobertura"})
        sep = dict(gs[topo]["membros"][0])
        novo = P.ligacoes_editar(m, {"acao": "dividir", "grupo": topo, "membros": [sep]})["grupo"]
        P.ligacoes_editar(m, {"acao": "travar", "grupo": base, "travado": True})
        with self.assertRaises(P.ErroEntrada):  # travado não aceita mover
            P.ligacoes_editar(m, {"acao": "mover", "membros": [gs[base]["membros"][0]], "destino": topo})
        # edição do modelo: viga do 1º andar troca de seção e uma viga do topo some
        m.barras["VX1-0-0"].secao = "W310x38.7"
        perdido = next(x for x in gs[topo]["membros"] if x["barra"] == "VX2-1-0")
        P.remover(m, "barra", "VX2-1-0")
        P.ligacoes_identificar(m)
        gs = m.ligacoes["grupos"]
        self.assertEqual(gs[topo]["nome"], "Cobertura")
        self.assertEqual(gs[novo]["membros"], [sep])
        self.assertTrue(gs[base]["travado"])
        self.assertEqual(len(gs[base]["membros"]), 3)
        todos = [x for g in gs.values() for x in g["membros"]]
        self.assertNotIn(perdido, todos)
        # a viga que mudou de seção saiu do grupo antigo (não manual) e ganhou grupo próprio
        sec_nova = [g for g in gs.values() if g["assinatura"].startswith("viga_pilar_mesa|W310x38.7|")]
        self.assertEqual(sum(len(g["membros"]) for g in sec_nova), 2)
        # unir devolve tudo a um grupo
        P.ligacoes_editar(m, {"acao": "unir", "grupos": [topo, novo]})
        self.assertNotIn(novo, m.ligacoes["grupos"])


class TestPior(unittest.TestCase):
    def test_pior_membro(self):
        """Chapa simples: a taxa cresce com V, logo o governante é a ponta de maior |V| entre as combinações ELU."""
        m = _portico()
        P.ligacoes_identificar(m)
        c = P.calcular(m)
        gid = next(k for k, g in m.ligacoes["grupos"].items() if "continuo" in g["assinatura"])
        P.ligacoes_editar(m, {"acao": "projeto", "grupo": gid, "tipo_ligacao": "chapa_simples", "dados": CHAPA})
        self.assertAlmostEqual(m.ligacoes["grupos"][gid]["projeto"]["dados"]["tw"], m.secoes["W360x51"].props["tw"])
        r = P.ligacoes_dimensionar(m, c, gid)
        V = {(x["barra"], x["ponta"]): max(math.hypot(e["Vy"], e["Vz"]) for e in
                                           lm.esforcos_ponta(c, x["barra"], x["ponta"]).values())
             for x in m.ligacoes["grupos"][gid]["membros"]}
        pior = max(V, key=V.get)
        self.assertEqual((r["governante"]["barra"], r["governante"]["ponta"]), pior)
        self.assertEqual(r["taxa_max"], max(x["taxa"] for x in r["membros"]))
        self.assertEqual(len(r["membros"]), 4)
        # sem projeto: governante pela demanda (|V| nas vigas)
        P.ligacoes_editar(m, {"acao": "projeto", "grupo": gid, "tipo_ligacao": None})
        r0 = P.ligacoes_dimensionar(m, c, gid)
        self.assertIsNone(r0["taxa_max"])
        self.assertEqual((r0["governante"]["barra"], r0["governante"]["ponta"]), pior)

    def test_placa_base_pior_de_4(self):
        """Edifício 1×1 vão, 1 andar: 4 bases iguais num grupo; carga só num canto. O grupo é dimensionado pela
        pior das 4 e a taxa bate com a placa verificada à mão com os esforços daquela base."""
        m = P.novo_modelo()
        P.gerar_estrutura(m, {"tipo": "edificio", "material": ACO, "secoes": W, "vaos_x": [6], "vaos_y": [5],
                              "alturas": [3]})
        m.add(CargaNodal("Q1", "N1-1-1", (15.0, 0.0, -80.0, 0.0, 0.0, 0.0)))
        P.ligacoes_identificar(m)
        c = P.calcular(m)
        gid, g = next((k, g) for k, g in m.ligacoes["grupos"].items() if g["tipo"] == "base")
        self.assertEqual(len(g["membros"]), 4)
        self.assertEqual(g["projeto"]["tipo_ligacao"], "placa_base")
        r = P.ligacoes_dimensionar(m, c, gid)
        self.assertEqual(r["governante"]["no"], "N0-1-1")
        taxas = [x["taxa"] for x in r["membros"]]
        self.assertEqual(r["taxa_max"], max(taxas))
        self.assertGreater(max(taxas), min(taxas))
        # conferência independente: placa do governante na combinação dele, nos dois eixos
        gov = next(x for x in r["membros"] if x["no"] == "N0-1-1")
        e = gov["esforcos"]
        V = math.hypot(e["Vy"], e["Vz"])
        sec = m.secoes["W310x38.7"].props
        mao = max(placa_base.verificar_placa_base(dict(pelo_perfil=True, eixo_fraco=f), dict(
            N=e["N"], V=V, M=M, N_barra=e["N"], V_barra=V, M_barra=M), sec)["taxa_max"]
            for M, f in ((e["My"], False), (e["Mz"], True)))
        self.assertAlmostEqual(r["taxa_max"], mao)


if __name__ == "__main__":
    unittest.main()
