"""Membro (peça física, params.membro): barra dividida continua uma peça na verificação, na flecha e nas ligações.
docs/MEMBROS.md. Números de referência: a mesma peça sem divisão (o resultado não pode depender de onde há nó)."""
import logging
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural import membros
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.geradores_extra import _Obra
from prossen_estrutural.modelo import Barra, CargaDistribuida, CargaNodal, CasoCarga, No


ACO = "A-572 GR.50"
LIV, ROT = (False,) * 6, (True, True, True, False, False, True)


def _base(secs):
    m = P.novo_modelo()
    P.adicionar(m, "material", {"aco": ACO})
    for s in secs:
        P.adicionar(m, "secao", {"perfil": s})
    m.add(CasoCarga("G1", "G"))
    m.add(CasoCarga("Q1", "Q"))
    m.peso_proprio = None
    return m


def _linha(c, bid):
    return next(l for l in c.verificacao if l["barra"] == bid)


def _pilar(modo):
    """W200x46.1, 6 m, rotulado; modo: inteiro | dividido | membro | membro_viga_x (viga rotulada chega em X a 3 m)."""
    m = _base(["W200x46.1", "W150x13"])
    m.add(No("A", 0, 0, 0, ROT))
    m.add(No("T", 0, 0, 6, (True, True, False, False, False, True)))
    if modo == "inteiro":
        m.add(Barra("P", "A", "T", "W200x46.1", ACO, params={"papel": "pilar"}))
    else:
        m.add(No("M", 0, 0, 3))
        pr = {"papel": "pilar", **({"membro": "P"} if modo.startswith("membro") else {})}
        m.add(Barra("P1", "A", "M", "W200x46.1", ACO, params=dict(pr)))
        m.add(Barra("P2", "M", "T", "W200x46.1", ACO, params=dict(pr)))
        if modo == "membro_viga_x":
            m.add(No("V", 5, 0, 3, (True, False, True, False, False, False)))
            m.add(Barra("V1", "M", "V", "W150x13", ACO, params={"papel": "viga", "rotula_i": True, "rotula_j": True}))
    m.add(CargaNodal("G1", "T", (0, 0, -500.0, 0, 0, 0)))
    m.add(CargaNodal("Q1", "T", (0, 0, -300.0, 0, 0, 0)))
    return m


def _viga(modo):
    """W310x38.7 biapoiada 8 m, 6 G + 6 Q kN/m; modo: inteira | dividida | membro."""
    m = _base(["W310x38.7"])
    m.add(No("A", 0, 0, 3, (True, True, True, True, False, False)))
    m.add(No("B", 8, 0, 3, (False, True, True, True, False, False)))
    if modo == "inteira":
        bs = [Barra("V", "A", "B", "W310x38.7", ACO, params={"papel": "viga"})]
    else:
        m.add(No("M", 4, 0, 3))
        pr = {"papel": "viga", **({"membro": "V"} if modo == "membro" else {})}
        bs = [Barra("V1", "A", "M", "W310x38.7", ACO, params=dict(pr)), Barra("V2", "M", "B", "W310x38.7", ACO, params=dict(pr))]
    for b in bs:
        m.add(b)
        for caso in ("G1", "Q1"):
            m.add(CargaDistribuida(caso, b.id, (0, 0, -6.0)))
    return m, [b.id for b in bs]


class TestVerificacaoMembro(unittest.TestCase):
    def test_pilar_dividido_como_membro_igual_ao_inteiro(self):
        inteiro = _linha(P.calcular(_pilar("inteiro")), "P")["taxa"]
        dividido = _linha(P.calcular(_pilar("dividido")), "P1")["taxa"]
        membro = _linha(P.calcular(_pilar("membro")), "P1")
        self.assertGreater(inteiro, 1.5)            # 6 m: reprova
        self.assertLess(dividido, 0.9)              # sem membro: 3 m, passaria (o erro antigo)
        self.assertAlmostEqual(membro["taxa"], inteiro, places=3)
        self.assertEqual(membro["membro"], "P")
        self.assertAlmostEqual(membro["Lfl_x_m"], 6.0)

    def test_viga_chegando_trava_so_um_eixo(self):
        c = P.calcular(_pilar("membro_viga_x"))
        l = _linha(c, "P1")
        # pilar vertical com rot 0: y local = Y global, z local = −X; a viga em X trava o deslocamento em z local
        # (KLx, eixo forte) e o eixo fraco continua com os 6 m
        self.assertAlmostEqual(l["Lfl_x_m"], 3.0)
        self.assertAlmostEqual(l["Lfl_y_m"], 6.0)

    def test_viga_membro_flt_e_flecha_do_vao(self):
        ci = P.calcular(_viga("inteira")[0])
        cm = P.calcular(_viga("membro")[0])
        cd = P.calcular(_viga("dividida")[0])
        self.assertAlmostEqual(_linha(cm, "V1")["taxa"], _linha(ci, "V")["taxa"], delta=0.02)
        self.assertLess(_linha(cd, "V1")["taxa"], 0.6 * _linha(ci, "V")["taxa"])   # o erro antigo
        fi = next(d for d in ci.deslocamentos["verticais"] if d["barra"] == "V")
        fm = next(d for d in cm.deslocamentos["verticais"] if d["barra"] == "V1")
        self.assertAlmostEqual(fm["limite_m"], fi["limite_m"])
        self.assertAlmostEqual(fm["flecha_m"], fi["flecha_m"], delta=0.02 * fi["flecha_m"])

    def test_cadeia_torta_fica_barra_a_barra(self):
        m = _base(["W150x13"])
        for nid, x, z in (("A", 0, 0), ("B", 3, 0), ("C", 3, 3)):
            m.add(No(nid, x, 0, z, ROT if nid == "A" else LIV))
        m.add(Barra("B1", "A", "B", "W150x13", ACO, params={"membro": "X"}))
        m.add(Barra("B2", "B", "C", "W150x13", ACO, params={"membro": "X"}))
        inv = []
        self.assertEqual(membros.cadeias(m, inv), {})
        self.assertEqual(inv, ["X"])


class TestLigacoesMembro(unittest.TestCase):
    def _no_m(self, extra=None):
        m, _ = _viga("membro")
        if extra:
            m.add(No("S", 4, 3, 3, (True, True, True, False, False, False)))
            m.add(Barra("VS", "M", "S", "W310x38.7", ACO, params={"papel": "viga", "rotula_i": True}))
        mot = {}
        return [x for x in lm.membros(m, mot) if x["no"] == "M"], mot

    def test_no_interno_sem_emenda(self):
        lig, mot = self._no_m()
        self.assertEqual(lig, [])
        self.assertIn("peça contínua", mot["M"])

    def test_secundaria_apoia_na_principal(self):
        lig, _ = self._no_m(extra=True)
        self.assertEqual([(x["tipo"], x["barra"]) for x in lig], [("viga_viga", "VS")])
        self.assertIn(lig[0]["apoio"], ("V1", "V2"))


class TestDividirMembro(unittest.TestCase):
    def test_dividir_grava_membro_e_pontas(self):
        m = _base(["W310x38.7"])
        m.add(No("A", 0, 0, 0, ROT))
        m.add(No("B", 6, 0, 0, ROT))
        m.add(No("M", 3, 0, 0))
        b = m.add(Barra("V", "A", "B", "W310x38.7", ACO, params={"trecho_rigido_i": 0.1, "trecho_rigido_j": 0.2,
                                                                    "rotula_j": True, "Lb": 2.0}))
        obra = _Obra(m, "")
        novos = obra.dividir(b, ["M"])
        b1, b2 = (obra.barras[n[0]] for n in novos)
        self.assertEqual((b1.params["membro"], b2.params["membro"]), ("V", "V"))
        self.assertEqual(b1.params.get("trecho_rigido_i"), 0.1)
        self.assertNotIn("trecho_rigido_j", b1.params)
        self.assertNotIn("trecho_rigido_i", b2.params)
        self.assertTrue(b2.params.get("rotula_j"))
        self.assertNotIn("Lb", b1.params)

    def test_validar_avisa_no_solto(self):
        m, _ = _viga("inteira")
        m.add(No("S", 4, 3, 3, (True, True, True, False, False, False)))
        m.add(No("E", 4, 0.05, 3))
        m.add(Barra("VS", "E", "S", "W310x38.7", ACO))
        cods = [v["codigo"] for v in P.validar(m)]
        self.assertIn("no_solto_sobre_barra", cods)


if __name__ == "__main__":
    unittest.main()
