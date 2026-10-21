"""Testes de secoes.py e materiais.py."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes as s, materiais as m  # noqa: E402


def rel(a, b):
    return abs(a - b) / abs(b)


class TestSecoes(unittest.TestCase):
    def test_retangular(self):
        r = s.secao_retangular(0.2, 0.5)
        self.assertAlmostEqual(r.Iy, 0.2 * 0.5 ** 3 / 12)
        self.assertAlmostEqual(r.props["Zx"] / r.props["Wx"], 1.5)

    def test_areas_de_cisalhamento(self):
        i = s.secao_I(0.3, 0.15, 0.006, 0.01)  # Az (Vz) = alma h·tw; Ay (Vy) = 5/6 das mesas
        self.assertAlmostEqual(i.props["Az"], 0.0018)
        self.assertAlmostEqual(i.props["Ay"], 5 / 6 * 2 * 0.15 * 0.01)
        r = s.secao_retangular(0.2, 0.5)
        self.assertAlmostEqual(r.props["Ay"], 0.08333333333, places=10)
        self.assertAlmostEqual(r.props["Az"], 0.08333333333, places=10)
        self.assertGreater(s.catalogo("W200x26.6").props["Az"], 0)
        self.assertGreater(s.secao_de_spec({"forma": "tubo_retangular", "h": 0.1, "b": 0.05, "t": 0.004}).props["Ay"], 0)

    def test_tubo_circular(self):
        t = s.secao_tubo_circular(0.1, 0.005)
        self.assertAlmostEqual(t.A, math.pi * (0.1 ** 2 - 0.09 ** 2) / 4)
        self.assertAlmostEqual(t.J, 2 * t.Iy)

    def test_W200x26_6_gerdau(self):
        # Gerdau, folder Segmento Industrial (10/18), tabela de bitolas, W 200 x 26,6:
        # A=34,2 cm², Ix=2611 cm⁴, Wx=252,3 cm³, Iy=330 cm⁴, Wy=49,6 cm³, ry=3,10 cm
        w = s.catalogo("W200x26.6")
        self.assertLess(rel(w.A, 34.2e-4), 0.01)
        self.assertLess(rel(w.props["Ix"], 2611e-8), 0.01)
        self.assertLess(rel(w.props["Wx"], 252.3e-6), 0.01)
        self.assertLess(rel(w.props["Iy"], 330e-8), 0.01)
        self.assertLess(rel(w.props["Wy"], 49.6e-6), 0.01)
        self.assertLess(rel(w.props["ry"], 3.10e-2), 0.01)

    def test_catalogo_area_confere(self):
        # para cada perfil da tabela Gerdau: A = 2tf·b + (h−2tf)tw + (4−π)R², Ix e Iy calculados
        # (secao_I com concordâncias) contra os valores tabelados (< 1 %)
        tab = {l["nome"]: l for l in s._ler_csv(s._CSV)}
        self.assertGreaterEqual(len(tab), 88)
        for nome, l in tab.items():
            p = s.catalogo(nome)
            self.assertLess(rel(p.A, float(l["A"]) * 1e-4), 0.01, nome)
            self.assertLess(rel(p.props["Ix"], float(l["Ix"]) * 1e-8), 0.01, nome)
            self.assertLess(rel(p.props["Iy"], float(l["Iy"]) * 1e-8), 0.01, nome)
            self.assertLess(rel(p.A * 7850, p.props["massa"]), 0.03, nome)  # massa do nome é nominal

    def test_lista_catalogo(self):
        w = s.lista_catalogo("W")
        self.assertTrue(w)
        self.assertTrue(all(i["familia"] == "W" for i in w))
        self.assertEqual(set(w[0]), {"nome", "familia", "massa_kg_m", "d_mm", "bf_mm", "A_cm2"})
        hp = {i["nome"] for i in s.lista_catalogo("HP")}
        self.assertTrue({"HP200x53", "HP310x125"} <= hp)
        self.assertGreaterEqual(len(s.lista_catalogo()), len(w) + len(hp) + len(s.lista_catalogo("Ue")) + len(s.lista_catalogo("U")))  # + catálogo novo (K2)
        # nomes tolerantes a espaço, vírgula e ",0"
        self.assertEqual(s.catalogo("W 200 x 52,0").nome, "W200x52")
        with self.assertRaises(KeyError):
            s.catalogo("W999x1")

    def test_catalogo_pff(self):
        if not os.path.exists(s._CSV_PFF):
            self.skipTest("perfis_pff.csv ainda não existe (ficha 003)")
        nomes = {i["nome"] for i in s.lista_catalogo("Ue")}
        self.assertTrue(nomes)
        if "Ue 150x60x20x2,00" in nomes:
            ue = s.catalogo("Ue 150x60x20x2,00")
            self.assertLess(rel(ue.A, 5.94e-4), 0.02)  # NBR 6355 (ver test_Ue_area)
        else:  # ponytail: perfil fora do perfis_pff.csv atual (ficha 003); confere a busca com outro Ue
            ue = s.catalogo("Ue 150x60x20x3")  # nome tolerante a ",00"
        self.assertEqual(ue.props["tipo"], "Ue")
        u = s.lista_catalogo("U")
        self.assertTrue(u)
        self.assertEqual(s.catalogo(u[0]["nome"]).props["tipo"], "U")

    def test_U_centro_cisalhamento(self):
        # U de parede fina: e = 3b²/(6b+h) a partir da linha média da alma (Timoshenko & Gere, Resist. dos Materiais)
        u = s.secao_U_pf(0.2, 0.0525, 0.005)
        b, h = 0.05, 0.195
        e = 3 * b ** 2 / (6 * b + h)
        self.assertLess(rel(-(u.props["xg"] + u.props["x0"]), e), 1e-6)
        # Cw = t·b³·h²/12 · (3b+2h)/(6b+h) (mesma fonte)
        self.assertLess(rel(u.props["Cw"], 0.005 * b ** 3 * h ** 2 / 12 * (3 * b + 2 * h) / (6 * b + h)), 1e-6)

    def test_Ue_area(self):
        # Ue 150x60x20x2,00 — NBR 6355 tabela: A ≈ 5,94 cm² (com raio de dobra); cantos vivos dão ~1,7% a mais
        ue = s.secao_Ue(0.15, 0.06, 0.02, 0.002)
        self.assertLess(rel(ue.A, 5.94e-4), 0.02)

    def test_cantoneira(self):
        # L 50x50x5 (cantos vivos): A=4,75 cm², ponto principal Imax+Imin = Ix+Iy
        L = s.secao_cantoneira(0.05, 0.05, 0.005)
        self.assertAlmostEqual(L.A, 4.75e-4)
        self.assertAlmostEqual(L.props["Imax"] + L.props["Imin"], L.props["Ix"] + L.props["Iy"])


class TestSecaoDeSpec(unittest.TestCase):
    """ESPEC §6.3: formatos de `secao`; valores esperados por fórmula fechada (área)."""

    def test_formas(self):
        casos = [
            (dict(forma="retangular", b=0.02, h=0.05), "R 20x50", 0.02 * 0.05),
            (dict(forma="circular", d=0.02), "C 20", math.pi * 0.02 ** 2 / 4),
            (dict(forma="I", h=0.3, b=0.15, tw=0.0063, tf=0.0095), "I 300x150x6,3x9,5",
             2 * 0.15 * 0.0095 + (0.3 - 0.019) * 0.0063),
            (dict(forma="tubo_circular", d=0.1, t=0.005), "TC 100x5", math.pi * (0.1 ** 2 - 0.09 ** 2) / 4),
            (dict(forma="tubo_retangular", h=0.1, b=0.05, t=0.004), "TR 100x50x4", 0.1 * 0.05 - 0.092 * 0.042),
            (dict(forma="U", h=0.2, b=0.06, tw=0.006, tf=0.01), "U 200x60x6x10", 2 * 0.06 * 0.01 + 0.18 * 0.006),
            # PFF sem ri: NBR 6355 (ri = t) → cada dobra troca 2·rm de reta por um arco π·rm/2 (rm = ri + t/2)
            (dict(forma="Ue", h=0.15, b=0.06, D=0.02, t=0.002), "Ue 150x60x20x2",
             0.002 * ((0.15 - 0.002) + 2 * (0.06 - 0.002) + 2 * (0.02 - 0.001) - 4 * 0.003 * (2 - math.pi / 2))),
            (dict(forma="Ue", h=0.15, b=0.06, D=0.02, t=0.002, ri=0), "Ue 150x60x20x2",
             0.002 * ((0.15 - 0.002) + 2 * (0.06 - 0.002) + 2 * (0.02 - 0.001))),
            (dict(forma="U_pf", h=0.1, b=0.05, t=0.002), "U 100x50x2",
             0.002 * ((0.1 - 0.002) + 2 * (0.05 - 0.001) - 2 * 0.003 * (2 - math.pi / 2))),
            (dict(forma="cantoneira", b=0.05, h=0.05, t=0.005), "L 50x50x5", 4.75e-4),
        ]
        for spec, nome, A in casos:
            sec = s.secao_de_spec(spec)
            self.assertEqual(sec.nome, nome)
            self.assertAlmostEqual(sec.A, A, delta=A * 1e-4, msg=nome)  # arcos por cordas: erro < 1e-4
        self.assertEqual(s.secao_de_spec(dict(forma="I", h=0.3, b=0.15, tw=0.0063, tf=0.0095, nome="VS")).nome, "VS")
        self.assertFalse(s.secao_de_spec(dict(forma="I", h=0.3, b=0.15, tw=0.0063, tf=0.0095, r=0.01)).props["soldado"])

    def test_perfil_e_explicita(self):
        self.assertEqual(s.secao_de_spec({"perfil": "W310x38.7"}).nome, "W310x38.7")
        x = s.secao_de_spec(dict(nome="X", A=1e-3, Iy=2e-6, Iz=1e-6, J=1e-8, props={"h": 0.1}))
        self.assertEqual((x.nome, x.A, x.Iy, x.Iz, x.J, x.props), ("X", 1e-3, 2e-6, 1e-6, 1e-8, {"h": 0.1}))

    def test_erros(self):
        with self.assertRaisesRegex(ValueError, r"secao\.tf"):
            s.secao_de_spec(dict(forma="I", h=0.3, b=0.15, tw=0.0063))
        with self.assertRaisesRegex(ValueError, r"secao\.forma"):
            s.secao_de_spec(dict(forma="Z", h=0.2))
        with self.assertRaisesRegex(ValueError, r"secao\.t"):
            s.secao_de_spec(dict(forma="tubo_circular", d=0.1, t=-0.005))
        with self.assertRaisesRegex(ValueError, r"secao\.perfil"):
            s.secao_de_spec({"perfil": "W999x1"})
        with self.assertRaisesRegex(ValueError, "secao"):
            s.secao_de_spec(dict(A=1e-3))
        with self.assertRaisesRegex(ValueError, r"secao\.tw"):  # U: alma mais espessa que a mesa é larga
            s.secao_de_spec(dict(forma="U", h=0.2, b=0.05, tw=0.06, tf=0.01))

    def test_cantoneira_sem_Z(self):
        # Z da cantoneira não é calculado (flexão de L fora da G.1): nada de constante sem fonte
        p = s.secao_cantoneira(0.1, 0.1, 0.01).props
        self.assertNotIn("Zx", p)
        self.assertNotIn("Zy", p)


class TestCoordenadaSetorial(unittest.TestCase):
    """ωn máximo (polo no centro de cisalhamento), Seaburg & Carter, "Torsional analysis of structural steel
    members", AISC Design Guide 9, 1997, apêndice C: I → Wno = h0·bf/4; U (linha média, h' = h − tf,
    b' = b − tw/2, e0 = 3b'²tf/(6b'tf + h'tw) da linha média da alma) → ωn = e0·h'/2 no canto e (b' − e0)·h'/2
    na ponta da mesa."""

    def test_I(self):
        self.assertAlmostEqual(s.wn_max(s.secao_I(0.207, 0.133, 0.0058, 0.0084).props),
                               (0.207 - 0.0084) * 0.133 / 4, places=12)

    def test_U_laminado(self):
        h, b, tw, tf = 0.200, 0.075, 0.008, 0.012
        hl, bl = h - tf, b - tw / 2
        e0 = 3 * bl ** 2 * tf / (6 * bl * tf + hl * tw)
        esp = max(e0, bl - e0) * hl / 2
        self.assertLess(abs(s.wn_max(s.secao_U(h, b, tw, tf).props) - esp) / esp, 1e-9)

    def test_U_pf_cantos_vivos(self):
        h, b, t = 0.150, 0.050, 0.002
        hl, bl = h - t, b - t / 2
        e0 = 3 * bl ** 2 / (6 * bl + hl)
        esp = max(e0, bl - e0) * hl / 2
        self.assertLess(abs(s.wn_max(s.secao_U_pf(h, b, t).props) - esp) / esp, 1e-9)


class TestMateriais(unittest.TestCase):
    def test_aco(self):
        a = m.aco("A-572 Gr.50")
        self.assertEqual(a.fy, 345e3)
        self.assertEqual(m.aco("MR-250").fu, 400e3)

    def test_concreto_tabela_8_1(self):
        # NBR 6118:2014 tabela 8.1 (granito): C30 Eci=31 GPa Ecs=27 GPa; C70 Eci=43 Ecs=42; C90 Eci=47 Ecs=47
        for fck, Eci, Ecs in ((30, 31, 27), (70, 43, 42), (90, 47, 47), (20, 25, 21)):
            p = m.props_concreto(fck)
            self.assertAlmostEqual(p["Eci"] / 1000, Eci, delta=0.6)
            self.assertAlmostEqual(p["Ecs"] / 1000, Ecs, delta=0.6)
        self.assertAlmostEqual(m.props_concreto(30)["fctm"], 2.90, places=2)

    def test_armadura(self):
        self.assertEqual(m.aco_armadura("CA-50").fy, 500e3)


if __name__ == "__main__":
    unittest.main()
