"""Testes do contrato de dados (ESPEC §5): ids de carga, (de)serialização v1 e leitura v0."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural.modelo import (Modelo, No, Material, Secao, Barra, CasoCarga, CargaNodal,
                                       CargaDistribuida, CargaPontual, Pavimento)


def exemplo():
    m = Modelo(nome="Pórtico teste")
    m.add(No("n1", 0, 0, 0, apoio=(True,) * 6))
    m.add(No("n2", 0, 0, 3.0))
    m.add(No("n3", 4.0, 0, 3.0))
    m.add(Material("aco", 2.0e8, 7.7e7, 77.0, fy=345000.0, fu=450000.0))
    m.add(Secao("s", 0.005, 8.6e-5, 7.3e-6, 1.3e-7, {"tipo": "I", "h": 0.31}))
    m.add(Barra("b1", "n1", "n2", "s", "aco"))
    m.add(Barra("b2", "n2", "n3", "s", "aco", params={"rotula_j": True}))
    m.add(CasoCarga("G1", "G"))
    m.add(CasoCarga("Q1", "Q", "comercial"))
    m.add(CargaNodal("Q1", "n3", (0, 0, -10.0, 0, 0, 0)))
    m.add(CargaDistribuida("G1", "b2", (0, 0, -5.0)))
    m.add(CargaPontual("Q1", "b2", 2.5, (0, 0, -20.0)))
    m.add(Pavimento("P1", 3.0))
    m.peso_proprio = "G1"
    m.config = {"tipo_uso": "comercial", "vento": {"V0": 35.0}}
    return m


class TestModelo(unittest.TestCase):
    def test_ids_atribuidos_sem_reaproveitar(self):
        """ESPEC §5: ids cn<n>/cd<n>/cp<n>; contador = maior já usado + 1; não reaproveita."""
        m = exemplo()
        self.assertEqual(m.cargas_nodais[0].id, "cn1")
        self.assertEqual(m.cargas_distribuidas[0].id, "cd1")
        self.assertEqual(m.cargas_pontuais[0].id, "cp1")
        m.add(CargaNodal("Q1", "n2", (1, 0, 0, 0, 0, 0), id="cn7"))
        self.assertEqual(m.add(CargaNodal("Q1", "n2", (1, 0, 0, 0, 0, 0))).id, "cn8")
        m.cargas_nodais.pop()  # remove cn8
        self.assertEqual(m.add(CargaNodal("Q1", "n2", (1, 0, 0, 0, 0, 0))).id, "cn9")
        self.assertEqual(m.add(CargaNodal("Q1", "n2", (0,) * 6, id="auto_v1")).id, "auto_v1")

    def test_ida_e_volta_v1(self):
        m = exemplo()
        d = m.to_dict()
        self.assertEqual(d["versao_formato"], 1)
        self.assertEqual(d["nos"]["n1"]["apoio"], [True] * 6)
        self.assertEqual(d["cargas_pontuais"][0], {"caso": "Q1", "barra": "b2", "a": 2.5,
                                                   "F": [0, 0, -20.0], "id": "cp1"})
        self.assertEqual(d["pavimentos"]["P1"], {"nome": "P1", "z": 3.0, "diafragma": True})
        json.dumps(d)  # só tipos JSON
        m2 = Modelo.from_dict(d)
        self.assertEqual(m2, m)
        self.assertEqual(m2.to_dict(), d)
        d["config"]["vento"]["V0"] = 0.0  # to_dict é cópia profunda
        self.assertEqual(m.config["vento"]["V0"], 35.0)

    def test_json_arquivo(self):
        m = exemplo()
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "m.json")
            m.to_json(p)
            self.assertEqual(Modelo.from_json(p), m)

    def test_le_v0_sem_ids(self):
        """Arquivo do núcleo antigo (asdict sem versao_formato, ids, pontuais, pavimentos)."""
        d = {"nos": {"a": {"id": "a", "x": 0, "y": 0, "z": 0, "apoio": [1, 1, 1, 0, 0, 0]}},
             "materiais": {}, "secoes": {}, "barras": {},
             "casos": {"G": {"nome": "G", "tipo": "G", "subtipo": ""}},
             "cargas_nodais": [{"caso": "G", "no": "a", "F": [0, 0, -1, 0, 0, 0]},
                               {"caso": "G", "no": "a", "F": [1, 0, 0, 0, 0, 0]}],
             "cargas_distribuidas": [], "peso_proprio": None, "config": {}}
        m = Modelo.from_dict(d)
        self.assertEqual([c.id for c in m.cargas_nodais], ["cn1", "cn2"])
        self.assertEqual(m.nos["a"].apoio, (True, True, True, False, False, False))
        self.assertEqual(m.cargas_nodais[0].F, (0, 0, -1, 0, 0, 0))
        self.assertEqual((m.cargas_pontuais, m.pavimentos, m.versao_formato), ([], {}, 1))

    def test_chave_diferente_do_id_e_id_de_carga_repetido(self):
        """Revisão 2026-10-20 (MENOR): chave do dict ≠ id/nome e id de carga repetido -> ValueError."""
        d = exemplo().to_dict()
        d["nos"]["outro"] = d["nos"].pop("n2")
        with self.assertRaisesRegex(ValueError, "outro"):
            Modelo.from_dict(d)
        d = exemplo().to_dict()
        d["casos"]["X"] = d["casos"].pop("G1")
        with self.assertRaisesRegex(ValueError, "X"):
            Modelo.from_dict(d)
        d = exemplo().to_dict()
        d["cargas_distribuidas"].append(dict(d["cargas_distribuidas"][0]))
        with self.assertRaisesRegex(ValueError, "cd1"):
            Modelo.from_dict(d)

    def test_versao_futura_recusada(self):
        with self.assertRaises(ValueError):
            Modelo.from_dict({"versao_formato": 99})

    def test_apoio_sempre_6_bool(self):
        self.assertEqual(No("x", 0, 0, 0, apoio=[1, 0, 0, 0, 0, 1]).apoio,
                         (True, False, False, False, False, True))
        with self.assertRaises(ValueError):
            No("x", 0, 0, 0, apoio=(True, True))


if __name__ == "__main__":
    unittest.main()


class TestMolasRecalquesJSON(unittest.TestCase):
    def test_ida_e_volta(self):
        m = Modelo()
        m.add(No("a", 0, 0, 0, apoio=(True,) * 6, molas=(0, 0, 5e3, 0, 0, 0), recalques=(0, 0, -0.01, 0, 0, 0)))
        m.add(No("b", 1, 0, 0))
        d = m.to_dict()
        self.assertEqual(d["nos"]["a"]["recalques"], [0, 0, -0.01, 0, 0, 0])
        self.assertNotIn("molas", d["nos"]["b"])  # sem mola: JSON igual ao de antes
        m2 = Modelo.from_dict(json.loads(json.dumps(d)))
        self.assertEqual(m2.nos["a"].molas, (0, 0, 5e3, 0, 0, 0))
        self.assertEqual(m2.nos["a"].recalques, (0, 0, -0.01, 0, 0, 0))
        from prossen_estrutural.projeto import _esquema
        self.assertEqual(_esquema(d), [])
        d["nos"]["b"]["molas"] = [1, 2]
        self.assertEqual(_esquema(d)[0]["campo"], "nos.b.molas")
