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


def _vazio():
    """Modelo só com as seções/material do pórtico (nós e barras montados à mão no teste)."""
    m = P.novo_modelo()
    P.gerar_estrutura(m, {"tipo": "portico_plano", "material": ACO, "secoes": W, "vaos_x": [6], "alturas": [3]})
    m.nos.clear()
    m.barras.clear()
    return m


def _monta(m, nos, barras, apoios=()):
    from prossen_estrutural.modelo import Barra, No
    for nid, xyz in nos.items():
        m.add(No(nid, *xyz, apoio=(True,) * 6 if nid in apoios else (False,) * 6))
    for bid, (ni, nj, papel, params) in barras.items():
        m.add(Barra(bid, ni, nj, W[papel], ACO, params=dict(params)))
    return m


def _tipos_no(m, nid):
    return sorted(x["tipo"] for x in lm.membros(m) if x["no"] == nid)


class TestNosInclinados(unittest.TestCase):
    """G3: nós inclinados e de base; todo nó tem tipo ou motivo."""

    def test_mao_francesa(self):
        m = _monta(_vazio(), {"A": (0, 0, 0), "D": (0, 0, 2.2), "B": (0, 0, 3), "E": (0.8, 0, 3), "C": (4, 0, 3)},
                   {"P1": ("A", "D", "pilar", {}), "P2": ("D", "B", "pilar", {}), "V1": ("B", "E", "viga", {}),
                    "V2": ("E", "C", "viga", {}), "MF": ("D", "E", "viga", {"rotula_i": True, "rotula_j": True})},
                   apoios=("A",))
        self.assertEqual(_tipos_no(m, "D"), ["gusset_contraventamento"])
        self.assertEqual(_tipos_no(m, "E"), ["gusset_contraventamento"])

    def test_banzo_inclinado_trelica(self):
        """Cobertura 10°: banzo contínuo (barra comum) + diagonal e montante de treliça → nó de treliça."""
        t = math.tan(math.radians(10))
        m = _monta(_vazio(), {"S0": (0, 0, 3), "S1": (2, 0, 3 + 2 * t), "S2": (4, 0, 3 + 4 * t),
                              "I1": (2, 0, 2.5), "I2": (4, 0, 2.5)},
                   {"B1": ("S0", "S1", "viga", {}), "B2": ("S1", "S2", "viga", {}),
                    "M1": ("I1", "S1", "viga", {"trelica": True}), "D1": ("I2", "S1", "viga", {"trelica": True}),
                    "I": ("I1", "I2", "viga", {})})
        self.assertEqual(_tipos_no(m, "S1"), ["no_trelica", "no_trelica"])

    def test_rampa_papel_viga(self):
        m = _monta(_vazio(), {"A": (0, 0, 0), "B": (0, 0, 3), "C": (5, 0, 0), "D": (5, 0, 6.5)},
                   {"P1": ("A", "B", "pilar", {}), "P2": ("C", "D", "pilar", {}),
                    "R": ("B", "D", "viga", {"papel": "viga"})}, apoios=("A", "C"))
        self.assertIn(_tipos_no(m, "B")[0], ("viga_pilar_mesa", "viga_pilar_alma"))
        self.assertNotIn("gusset_contraventamento", [x["tipo"] for x in lm.membros(m)])

    def test_pilar_inclinado_base(self):
        m = _monta(_vazio(), {"A": (0, 0, 0), "B": (1, 0, 3), "C": (5, 0, 3), "D": (5, 0, 0)},
                   {"P1": ("A", "B", "pilar", {}), "V": ("B", "C", "viga", {}), "P2": ("D", "C", "pilar", {})},
                   apoios=("A", "D"))
        self.assertEqual(_tipos_no(m, "A"), ["base"])

    def test_diagonal_angulo_pequeno_motivo(self):
        """Review Focus 2: 2 vigas colineares + diagonal a ~11° da viga → sem ligação, motivo cita o ângulo."""
        m = _monta(_vazio(), {"A": (0, 0, 3), "B": (3, 0, 3), "C": (6, 0, 3), "D": (8, 0, 4)},
                   {"V1": ("A", "B", "viga", {}), "V2": ("B", "C", "viga", {}), "DG": ("B", "D", "viga", {})})
        mot = {}
        self.assertEqual([x for x in lm.membros(m, mot) if x["no"] == "B"], [])
        self.assertIn("ângulo", mot["B"])
        r = lm.identificar(m)
        self.assertIn("ângulo", r["sem_tipo"]["B"])

    def test_viga_viga_angulo_pequeno(self):
        m = _monta(_vazio(), {"A": (0, 0, 3), "B": (3, 0, 3), "C": (6, 0, 3), "D": (8, 0.5, 3)},
                   {"V1": ("A", "B", "viga", {}), "V2": ("B", "C", "viga", {}), "V3": ("B", "D", "viga", {})})
        mot = {}
        self.assertEqual([x for x in lm.membros(m, mot) if x["no"] == "B"], [])
        self.assertIn("ângulo", mot["B"])

    def test_contravento_parede_sobre_terca_sem_pilar(self):
        """Diagonal de parede (trelica) cruza longarina contínua sem pilar, plano vertical: nó de treliça."""
        m = _monta(_vazio(), {"A": (0, 0, 3), "B": (3, 0, 3), "C": (6, 0, 3), "D": (0, 0, 0), "E": (6, 0, 6)},
                   {"G1": ("A", "B", "viga", {}), "G2": ("B", "C", "viga", {}),
                    "X1": ("D", "B", "viga", {"trelica": True}), "X2": ("B", "E", "viga", {"trelica": True})})
        self.assertEqual(_tipos_no(m, "B"), ["no_trelica", "no_trelica"])

    def test_contravento_cobertura_30(self):
        """X no plano do telhado a 30° (normal longe da horizontal): gusset, não nó de treliça."""
        t = math.tan(math.radians(30))
        m = _monta(_vazio(), {"A": (0, 0, 3), "B": (2, 0, 3 + 2 * t), "C": (4, 0, 3 + 4 * t),
                              "D": (0, 5, 3), "E": (4, 5, 3 + 4 * t)},
                   {"R1": ("A", "B", "viga", {"papel": "viga"}), "R2": ("B", "C", "viga", {"papel": "viga"}),
                    "X1": ("B", "D", "viga", {"trelica": True}), "X2": ("B", "E", "viga", {"trelica": True})})
        self.assertEqual(_tipos_no(m, "B"), ["gusset_contraventamento", "gusset_contraventamento"])

    def test_contravento_cobertura_no_beiral(self):
        """G6: diagonal do telhado chega no topo do pilar quase na horizontal (82° com o pilar): o gusset vai no plano do
        telhado, apoiado na tesoura (a viga mais pesada do nó), não num canto viga–pilar vertical de 2,5 m."""
        m = _monta(_vazio(), {"A": (0, 0, 0), "B": (0, 0, 6), "C": (10, 0, 8), "D": (10, 6, 8), "E": (0, 6, 6)},
                   {"P": ("A", "B", "pilar", {}), "R": ("B", "C", "viga", {"papel": "viga"}),
                    "T": ("B", "E", "viga", {"papel": "viga", "rotula_i": True, "rotula_j": True}),
                    "X": ("B", "D", "viga", {"trelica": True})}, apoios=("A",))
        x = next(x for x in lm.membros(m) if x["no"] == "B" and x["barra"] == "X")
        self.assertEqual((x["tipo"], x["apoio"]), ("gusset_contraventamento", "R"))
        self.assertEqual(x["assinatura"].split("|")[3], "viga")

    def test_resumo_membro_traz_apoio(self):
        """G6: o resumo dos grupos diz a barra de apoio de cada membro (o WPF usa para a face e o gusset)."""
        m = _portico()
        P.ligacoes_identificar(m)
        vp = next(g for g in lm.resumo_grupos(m) if g["tipo"] == "viga_pilar_mesa")
        b = m.barras[vp["membros"][0]["apoio"]]
        self.assertEqual(m.secoes[b.secao].nome if hasattr(m.secoes[b.secao], "nome") else b.secao, W["pilar"])
        self.assertNotIn("apoio", m.ligacoes["grupos"][vp["id"]]["membros"][0])   # não vai para o arquivo

    def test_plataforma_escoras_sobre_apoio(self):
        raiz = os.path.join(os.path.dirname(__file__), "..", "..", "..", "testes-aceite")
        if not os.path.exists(os.path.join(raiz, "PLATAFORMA.ed3")):
            self.skipTest("testes-aceite/PLATAFORMA.ed3 ausente")
        m = P.importar_cype("PLATAFORMA.ed3", pasta=os.path.abspath(raiz))
        mb = {x["barra"]: x for x in lm.membros(m) if x["no"] in ("N1221", "N1228", "N1235")}
        for b in ("B1223", "B1230", "B1237"):
            self.assertEqual(mb[b]["tipo"], "base")
            self.assertIn("chapa de apoio, não placa de base", mb[b]["aviso"])

    def test_plataforma_todos_nos(self):
        raiz = os.path.join(os.path.dirname(__file__), "..", "..", "..", "testes-aceite")
        if not os.path.exists(os.path.join(raiz, "PLATAFORMA.ed3")):
            self.skipTest("testes-aceite/PLATAFORMA.ed3 ausente")
        m = P.importar_cype("PLATAFORMA.ed3", pasta=os.path.abspath(raiz))
        mot = {}
        com_tipo = {x["no"] for x in lm.membros(m, mot)}
        aco = {n for b in m.barras.values() if b.tipo != "concreto" for n in (b.ni, b.nj)}
        self.assertEqual(aco - com_tipo - set(mot), set())


if __name__ == "__main__":
    unittest.main()


class TestDiagonalNaColuna(unittest.TestCase):
    """Diagonal com placa de base chegando na coluna (captura 6/7): o topo foi desenhado até a face do pilar, num nó
    próprio; o pilar não tem nó ali. Antes: 'ponta livre'. Agora: motivo certo + ajuste ligar_no → gusset."""

    def _modelo(self):
        m = _monta(_vazio(), {"n1": (0, 0, 0), "n2": (0, 0, 6), "n4": (4, 0, 0), "n5": (0.155, 0, 3)},
                   {"b1": ("n1", "n2", "pilar", {}), "b3": ("n4", "n5", "pilar", {})}, apoios=("n1", "n4"))
        m.add(CargaNodal("Q1", "n2", (5.0, 0.0, -20.0, 0.0, 0.0, 0.0)))
        return m

    def test_motivo_e_ajuste(self):
        m = self._modelo()
        r = P.ligacoes_identificar(m)
        self.assertIn("b3 chega em b1 a 155 mm do eixo", r["sem_tipo"]["n5"])
        self.assertNotIn("ponta livre", r["sem_tipo"]["n5"])
        aj = r["ajustes"]["n5"]
        self.assertEqual(aj["barra"], "b1")
        self.assertAlmostEqual(aj["t"], 0.5)

    def test_ligar_no_identifica_e_dimensiona(self):
        m = self._modelo()
        r = P.ligacoes_ligar_no(m, {"no": "n5"})
        self.assertEqual((r["barra"], r["barras"]), ("b1", ["b1.1", "b1.2"]))
        self.assertEqual((m.nos["n5"].x, m.nos["n5"].z), (0.0, 3.0))
        self.assertEqual(_tipos_no(m, "n5"), ["gusset_contraventamento"])
        idr = P.ligacoes_identificar(m)
        self.assertNotIn("n5", idr["sem_tipo"])
        gid = next(k for k, g in m.ligacoes["grupos"].items() if g["tipo"] == "gusset_contraventamento")
        c = P.calcular(m)
        d = P.ligacoes_dimensionar(m, c, gid)
        self.assertEqual(d["membros"][0]["barra"], "b3")
        self.assertGreater(d["membros"][0]["demanda"]["N"], 0.0)   # agora a diagonal trabalha (escora o pilar)
        with self.assertRaises(Exception):
            P.ligacoes_ligar_no(m, {"no": "n5"})   # já ligado: nada a ligar

    def test_ponta_livre_de_verdade(self):
        m = _monta(_vazio(), {"n1": (0, 0, 0), "n2": (0, 0, 3), "n3": (2, 0, 3)},
                   {"b1": ("n1", "n2", "pilar", {}), "b2": ("n2", "n3", "viga", {})}, apoios=("n1",))
        r = P.ligacoes_identificar(m)
        self.assertEqual(r["sem_tipo"]["n3"], "ponta livre: 1 barra, sem apoio")
        self.assertEqual(r["ajustes"], {})
