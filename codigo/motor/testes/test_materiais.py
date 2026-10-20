"""Testes de materiais.py (NBR 6118:2023 8.2; aços NBR 8800 / NBR 14762; spec da ESPEC §6.3)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural import materiais as m  # noqa: E402
from prossen_estrutural.modelo import MPa  # noqa: E402


class TestConcreto(unittest.TestCase):
    """NBR 6118:2023 8.2.8 (Bastos, Fundamentos do Concreto Armado, UNESP set/2023, p. 22,
    Eq. 2.11–2.13): Eci = αE·5600√fck (≤ C50); Eci = 21,5e3·αE·(fck/10 + 1,25)^(1/3) (> C50);
    Ecs = αi·Eci, αi = 0,8 + 0,2·fck/80 ≤ 1. Valores calculados à mão."""

    def test_C25(self):
        p = m.props_concreto(25)
        self.assertAlmostEqual(p["Eci"], 28000.0, delta=1e-6)       # 5600·5
        self.assertAlmostEqual(p["Ecs"], 0.8625 * 28000.0, delta=1e-6)  # 24 150 MPa
        self.assertAlmostEqual(m.concreto(25).E, 24150.0 * MPa, delta=1e-3)

    def test_C70(self):
        p = m.props_concreto(70)
        self.assertAlmostEqual(p["Eci"], 43443.0, delta=43443 * 1e-4)  # 21500·8,25^(1/3) = 21500·2,02062
        self.assertAlmostEqual(p["Ecs"], 0.975 * 43443.0, delta=43443 * 1e-4)
        # 8.2.5 (2023): fct,m = 2,12·ln(1 + 0,1·78) = 2,12·2,17475 = 4,6105 MPa
        self.assertAlmostEqual(p["fctm"], 4.6105, delta=1e-3)

    def test_basalto(self):
        self.assertAlmostEqual(m.props_concreto(25, alfa_E=1.2)["Eci"], 1.2 * 28000.0, delta=1e-6)

    def test_fora_da_faixa(self):
        with self.assertRaises(ValueError):
            m.props_concreto(95)

    def test_alfa_E_faixa_e_nome(self):
        """8.2.8: αE de 0,7 (arenito) a 1,2 (basalto); fora disso (ex.: 0, que daria E = 0) → erro.
        Dois C30 com αE diferentes não podem ter o mesmo nome no Modelo.materiais."""
        for a in (0.0, -1.0, 0.69, 1.21):
            with self.assertRaises(ValueError):
                m.concreto(30, a)
        self.assertEqual(m.concreto(30).nome, "C30")
        self.assertEqual(m.concreto(30, 0.9).nome, "C30 αE0,9")
        self.assertNotEqual(m.concreto(30, 0.9).nome, m.concreto(30, 1.2).nome)


class TestAcos(unittest.TestCase):
    def test_fy_fu(self):
        # Catálogo ArcelorMittal de perfis e barras (público): MR 250 ≡ A36 (250/400),
        # AR 350 ≡ A572 Gr.50 (350/450); ASTM A572 Gr.50: 345/450 MPa.
        self.assertEqual((m.aco("A-36").fy, m.aco("A-36").fu), (250 * MPa, 400 * MPa))
        self.assertEqual((m.aco("AR-350").fy, m.aco("AR-350").fu), (350 * MPa, 450 * MPa))
        self.assertEqual(m.aco("A-572 GR.50").fy, 345 * MPa)

    def test_armadura(self):
        self.assertEqual(m.aco_armadura("CA-50").fy, 500 * MPa)
        self.assertEqual(m.aco_armadura("CA-50").E, 210000 * MPa)  # NBR 6118 8.3.5


class TestMaterialDeSpec(unittest.TestCase):
    def test_aco(self):
        mat = m.material_de_spec({"aco": "A-572 GR.50"})
        self.assertEqual((mat.nome, mat.fy, mat.fu), ("A-572 GR.50", 345 * MPa, 450 * MPa))

    def test_concreto(self):
        mat = m.material_de_spec({"concreto": 30, "alfa_E": 1.2})
        self.assertEqual(mat.fck, 30 * MPa)
        self.assertAlmostEqual(mat.E, m.props_concreto(30, 1.2)["Ecs"] * MPa)
        self.assertEqual(m.material_de_spec({"concreto": 25}).nome, "C25")

    def test_armadura(self):
        self.assertEqual(m.material_de_spec({"armadura": "CA-60"}).fy, 600 * MPa)

    def test_explicito(self):
        mat = m.material_de_spec({"nome": "X", "E_MPa": 200000, "G_MPa": 77000, "peso_kN_m3": 77,
                                  "fy_MPa": 300, "fu_MPa": 400})
        self.assertEqual((mat.E, mat.G, mat.peso, mat.fy, mat.fu, mat.fck),
                         (2e8, 7.7e7, 77.0, 3e5, 4e5, None))

    def test_a242_por_espessura(self):
        """ASTM A242/A242M (tabela de tração): t ≤ 19 mm 50/70 ksi (345/485); 19–38 mm 46/67 ksi (315/460);
        38–100 mm 42/63 ksi (290/435). "A-242" sem faixa = o menor (vale para qualquer t ≤ 100 mm)."""
        for nome, fy, fu in (("A-242", 290, 435), ("A-242 T<=19", 345, 485), ("A-242 T<=38", 315, 460),
                             ("a-242 t<=100", 290, 435)):
            a = m.aco(nome)
            self.assertEqual((a.fy, a.fu), (fy * MPa, fu * MPa), nome)

    def test_erros(self):
        with self.assertRaisesRegex(ValueError, "A-36"):  # lista os válidos
            m.material_de_spec({"aco": "S355"})
        with self.assertRaisesRegex(ValueError, "CA-50"):
            m.material_de_spec({"armadura": "CA-70"})
        with self.assertRaisesRegex(ValueError, "E_MPa"):
            m.material_de_spec({"nome": "X", "G_MPa": 1, "peso_kN_m3": 1})
        with self.assertRaises(ValueError):
            m.material_de_spec("A-36")
        with self.assertRaises(ValueError):
            m.material_de_spec({"concreto": 100})


if __name__ == "__main__":
    unittest.main()
