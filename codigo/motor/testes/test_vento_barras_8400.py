"""Vento por barra (NBR 6123:2023 8.1) e NBR 8400:1984 (vento e casos I/II/III).

Reproduz os valores do bloco "vento NBR 6123:2023" e "vento NBR 8400:1984" de codigo/portico/src/Testes.cs (Bruno,
origin/main 5d7133b e 1f719d3), agora no motor 3D: o pórtico plano XY do 2D vira o plano XZ (Y do 2D = z).
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from prossen_estrutural import projeto as P
from prossen_estrutural.acoes import nbr8400 as N8
from prossen_estrutural.acoes import vento as V
from prossen_estrutural.acoes import vento_barras as VB
from prossen_estrutural.modelo import CasoCarga

TUBO_114 = {"forma": "tubo_circular", "d": 0.1143, "t": 0.0073, "nome": "TC114"}
TUBO_203 = {"forma": "tubo_circular", "d": 0.2032, "t": 0.00375, "nome": "TC203"}


def _pilar(secao, vento):
    """Pilar em balanço de 6 m (Testes.cs: Novo(0, 0, 0, 6), engaste na base)."""
    m = P.novo_modelo()
    P.adicionar(m, "no", {"x": 0, "y": 0, "z": 0, "apoio": "engaste"})
    P.adicionar(m, "no", {"x": 0, "y": 0, "z": 6})
    P.adicionar(m, "material", {"aco": "A-36"})
    s = P.adicionar(m, "secao", secao)
    P.adicionar(m, "barra", {"ni": "n1", "nj": "n2", "secao": s["nome"], "material": "A-36"})
    P.adicionar(m, "caso", {"nome": "G", "tipo": "G"})
    P.configurar(m, {"vento": vento})
    return m


def _horizontal_e_45(vento):
    """Testes.cs: Novo(0, 3, 4, 3, 0, 0, 3, 3): barra horizontal (0,3)-(4,3) e barra a 45° (0,0)-(3,3), W 200 x 26,6."""
    m = P.novo_modelo()
    for x, z, ap in ((0, 3, None), (4, 3, None), (0, 0, "rotula"), (3, 3, None)):
        P.adicionar(m, "no", {"x": x, "y": 0, "z": z, **({"apoio": ap} if ap else {})})
    P.adicionar(m, "material", {"aco": "A-36"})
    w = P.adicionar(m, "secao", {"perfil": "W 200 x 26,6"})["nome"]
    P.adicionar(m, "barra", {"ni": "n1", "nj": "n2", "secao": w, "material": "A-36"})
    P.adicionar(m, "barra", {"ni": "n3", "nj": "n4", "secao": w, "material": "A-36"})
    P.adicionar(m, "caso", {"nome": "G", "tipo": "G"})
    P.configurar(m, {"vento": vento})
    return m


V6123 = {"tipo": "barras", "V0": 40, "S1": 1, "S3": 1, "categoria": "III", "direcoes": ["+X", "-X"]}
V8400 = {"tipo": "nbr8400", "v_servico": 20, "direcoes": ["+X", "-X"]}


class TestTabelas6123(unittest.TestCase):
    def test_s2_tabela3(self):
        # Testes.cs: S2(2,0,10) = 1,00; S2(4,0,3) = 0,79; S2(3,1,30) = 1,03; S2(5,2,100) = 1,01; S2(5,0,4) = 0,74; S2(1,0,250) = 1,33
        for cat, cl, z, v in (("II", "A", 10, 1.00), ("IV", "A", 3, 0.79), ("III", "B", 30, 1.03),
                              ("V", "C", 100, 1.01), ("V", "A", 4, 0.74), ("I", "A", 250, 1.33)):
            self.assertAlmostEqual(round(V.s2(cat, cl, z), 2), v, 9, (cat, cl, z))

    def test_k_ca_classe(self):
        self.assertAlmostEqual(VB.k_comprimento("plana", 20), 0.81, 12)  # tabela 28
        self.assertAlmostEqual(VB.k_comprimento("plana", 15), 0.75, 12)  # interpolado
        self.assertAlmostEqual(VB.k_comprimento("circular_sub", 2), 0.58, 12)
        self.assertEqual(VB.k_comprimento("plana", 150), 1.0)
        self.assertEqual(VB.ca_circular(3e5), 1.2)  # tabela 27
        self.assertEqual(VB.ca_circular(1e6), 0.7)
        self.assertEqual(VB.classe_por_dimensao(35), "B")  # 5.3.2
        self.assertAlmostEqual(0.613 * 40 ** 2 / 1000, 0.9808, 4)  # 4.2, q de Vk = 40 m/s

    def test_cf_por_forma(self):
        i = {"tipo": "I", "h": 0.207, "b": 0.133}
        t = (0.133 / 0.207 - 0.48) / 0.52
        self.assertAlmostEqual(VB.coeficiente(i, True, 30)["Cf"], 0.9 + t, 12)
        self.assertAlmostEqual(VB.coeficiente(i, False, 30)["Cf"], 2.05 - 0.45 * t, 12)
        self.assertEqual(VB.coeficiente({"tipo": "L", "h": 0.1, "b": 0.1}, True, 30)["Cf"], 2.1)
        self.assertEqual(VB.coeficiente({"tipo": "U", "h": 0.2, "b": 0.07}, True, 30)["Cf"], 0.6)
        r = VB.coeficiente({"tipo": "tubo_retangular", "h": 0.2, "b": 0.1}, True, 30)  # profundidade 0,2 / largura 0,1 = 2
        self.assertEqual((r["Cf"], r["c"]), (1.5, 0.1))
        with self.assertRaises(ValueError):
            VB.coeficiente({"tipo": "X"}, True, 30)


class TestVentoBarras(unittest.TestCase):
    def test_pilar_tubo_114(self):
        """Testes.cs: pilar de tubo 114,3x7,30, 6 m, V0 40, cat. III, classe A automática, S1 = S3 = 1."""
        m = _pilar(TUBO_114, V6123)
        g = P.gerar_acoes(m)
        self.assertEqual(g["casos"], ["V+X", "V-X"])
        f = g["forcas"]["V+X"][0]
        s2 = 0.94 * 1.0 * 0.6 ** 0.10
        qk = 0.613 * (40 * s2) ** 2 / 1000
        self.assertAlmostEqual(f["S2"], s2, 12)
        self.assertEqual(f["Cf"], 1.2)  # Re < 4,2e5 (tabela 27)
        self.assertEqual(f["K"], 1.0)  # l/c = 6/0,1143 dobrado na base > 100
        self.assertAlmostEqual(f["w_kN_m"], 1.2 * qk * 1.0 * 0.1143, 12)
        self.assertEqual(g["parametros"]["V+X"]["classe"], "A")
        r = P.analisar_caso(m, "V+X")
        reac = r["reacoes"]["n1"]
        self.assertAlmostEqual(reac[0], -f["w_kN_m"] * 6, 9)  # reação horizontal = −w·L (vento para +X)
        self.assertAlmostEqual(abs(reac[4]), f["w_kN_m"] * 18, 9)  # momento na base = w·L²/2
        r2 = P.analisar_caso(m, "V-X")
        self.assertAlmostEqual(r2["reacoes"]["n1"][0], f["w_kN_m"] * 6, 9)
        self.assertNotIn("G", {c["caso"] for c in P.listar(m, "carga_distribuida")})  # G sem carga de vento

    def test_tubo_203_subcritico_governa(self):
        """Testes.cs: tubo 203,2 a Vk ≈ 35 m/s, Re acima do crítico, vale a força subcrítica 1,2·q(Vcr)·d (8.1.3)."""
        m = _pilar(TUBO_203, dict(V6123, usar_K=False))
        f = P.gerar_acoes(m)["forcas"]["V+X"][0]
        self.assertGreaterEqual(f["Re"], 4.2e5)
        vcr, vk = 4.2e5 / (70000 * 0.2032), 40 * 0.94 * 0.6 ** 0.10
        esp = max(1.2 * 0.613 * vcr ** 2 / 1000, 0.6 * 0.613 * vk ** 2 / 1000) * 0.2032
        self.assertAlmostEqual(f["w_kN_m"], esp, 12)

    def test_horizontal_e_45_graus(self):
        m = _horizontal_e_45(V6123)
        g = P.gerar_acoes(m)
        fs = {f["barra"]: f for f in g["forcas"]["V+X"]}
        self.assertEqual(fs["b1"]["w_kN_m"], 0.0)  # barra horizontal paralela ao vento: sem força
        self.assertAlmostEqual(fs["b2"]["sen2"], 0.5, 12)
        self.assertAlmostEqual(fs["b2"]["c_m"], 0.207, 12)  # W com vento normal às mesas: c = d
        self.assertEqual({c["barra"] for c in P.listar(m, "carga_distribuida")}, {"b2"})  # b1 sem carga
        q = fs["b2"]["w_vet_kN_m"]  # normal à barra a 45° e de módulo w
        self.assertAlmostEqual(q[0] + q[2], 0.0, 12)
        self.assertAlmostEqual(math.hypot(*q), fs["b2"]["w_kN_m"], 12)
        self.assertGreater(q[0], 0)

    def test_vento_y_em_barra_do_plano_xz(self):
        """3D: vento em Y numa barra do plano XZ pega o perfil pelo eixo fraco (paralelo às mesas)."""
        m = _pilar({"perfil": "W 200 x 26,6"}, dict(V6123, direcoes=["+X", "+Y"]))
        g = P.gerar_acoes(m)
        fx, fy = g["forcas"]["V+X"][0], g["forcas"]["V+Y"][0]
        self.assertNotAlmostEqual(fx["Cf"], fy["Cf"])
        self.assertEqual(fy["w_vet_kN_m"][0], 0.0)
        self.assertGreater(fy["w_vet_kN_m"][1], 0)

    def test_regera_e_limpa(self):
        m = _pilar(TUBO_114, V6123)
        P.gerar_acoes(m)
        P.gerar_acoes(m)
        self.assertEqual(len(P.listar(m, "carga_distribuida")), 2)
        self.assertFalse([v for v in P.validar(m) if v["nivel"] == "erro"])  # sem pavimentos: ok no vento por barra
        P.configurar(m, {"vento": None})
        P.gerar_acoes(m)
        self.assertEqual(P.listar(m, "carga_distribuida"), [])

    def test_validacao(self):
        m = _pilar(TUBO_114, V6123)
        for ruim in ({"tipo": "barras", "V0": 40, "categoria": "VI"}, {"tipo": "xx", "V0": 40, "categoria": "II", "classe": "A"},
                     {"tipo": "barras", "V0": 40, "categoria": "II", "usar_K": "sim"}, {"tipo": "nbr8400", "v_servico": -1}):
            with self.assertRaises(P.ErroEntrada, msg=str(ruim)):
                P.configurar(m, {"vento": ruim})
        for ruim in ({"combinacoes": "nbr6118"}, {"nbr8400": {"grupo": 7}}, {"nbr8400": {"grupo": 2, "psi": 0}}):
            with self.assertRaises(P.ErroEntrada, msg=str(ruim)):
                P.configurar(m, ruim)


class Test8400(unittest.TestCase):
    def test_tabelas(self):
        # Testes.cs, bloco "vento NBR 8400:1984"
        self.assertEqual(N8.pa_servico(20), 250)
        self.assertEqual((N8.pa_maximo(10), N8.pa_maximo(50), N8.pa_maximo(150)), (800, 1100, 1300))
        self.assertEqual(N8.c_alma_cheia(2), 1.2)
        self.assertAlmostEqual(N8.c_alma_cheia(7.5), 1.35, 12)
        self.assertEqual(N8.c_alma_cheia(30), 1.6)
        self.assertEqual(N8.c_tubular(0.1143, 250), 1.2)
        self.assertEqual(N8.c_tubular(1.0, 800), 0.7)
        self.assertEqual((N8.mx_grupo(5), N8.mx_grupo(2)), (1.12, 1.00))
        with self.assertRaises(ValueError):
            N8.mx_grupo(0)

    def test_pilar_tubular(self):
        m = _pilar(TUBO_114, V8400)
        g = P.gerar_acoes(m)
        self.assertEqual(g["casos"], ["VS+X", "VS-X", "VM+X", "VM-X"])
        self.assertAlmostEqual(g["forcas"]["VS+X"][0]["w_kN_m"], 1.2 * 0.250 * 0.1143, 12)
        self.assertAlmostEqual(g["forcas"]["VM+X"][0]["w_kN_m"], 1.2 * 0.800 * 0.1143, 12)  # z ≤ 20 m
        self.assertEqual(g["forcas"]["VS+X"][0]["sen2"], 1.0)
        self.assertIn("vento_8400_sem_combinacoes", {a["codigo"] for a in g["avisos"]})
        self.assertEqual(m.casos["VM+X"].subtipo, "vento_maximo")

    def test_horizontal_e_45(self):
        m = _horizontal_e_45(V8400)
        fs = {f["barra"]: f for f in P.gerar_acoes(m)["forcas"]["VS+X"]}
        self.assertEqual(fs["b1"]["w_kN_m"], 0.0)
        self.assertAlmostEqual(fs["b2"]["w_kN_m"], N8.c_alma_cheia(math.sqrt(18) / 0.207) * 0.250 * 0.207 * 0.5, 12)

    def test_combinacoes_casos_I_II_III(self):
        casos = {n: CasoCarga(n, t, s) for n, t, s in (("G", "G", ""), ("Q", "Q", ""), ("VS+X", "V", "vento"),
                                                       ("VS-X", "V", "vento"), ("VM+X", "V", "vento_maximo"))}
        cs = {n: (f, e, d) for n, f, e, d in N8.gerar_combinacoes(casos, grupo=5, psi=1.15)}
        k1, k2, k3 = 1.5 / 1.1, 1.33 / 1.1, 1.0
        f, e, d = cs["ELU 8400 I"]
        self.assertEqual(e, "ELU")
        self.assertAlmostEqual(f["G"], round(1.12 * k1, 6))
        self.assertAlmostEqual(f["Q"], round(1.12 * 1.15 * k1, 6))
        self.assertEqual(set(f), {"G", "Q"})
        self.assertEqual((d["Mx"], d["nu"]), (1.12, 1.5))
        f = cs["ELU 8400 II + VS+X"][0]
        self.assertAlmostEqual(f["VS+X"], round(k2, 6))
        self.assertNotIn("VS-X", f)  # um sentido por vez
        f = cs["ELU 8400 III + VM+X"][0]
        self.assertEqual(f, {"G": round(k3, 6), "VM+X": round(k3, 6)})  # sem Q (5.6.3)
        self.assertNotIn("ELU 8400 II + VM+X", cs)
        self.assertEqual(cs["ELS 8400 G+Q"][:2], ({"G": 1.0, "Q": 1.0}, "ELS_RARA"))
        self.assertIn("ELS 8400 G+Q + VS-X", cs)
        self.assertEqual(len(cs), 1 + 2 + 1 + 1 + 2)

    def test_calculo_com_8400(self):
        m = _pilar(TUBO_114, V8400)
        P.configurar(m, {"combinacoes": "nbr8400", "nbr8400": {"grupo": 4, "psi": 1.2}, "peso_proprio": "G"})
        cs = P.combinacoes(m)
        self.assertTrue(all(c["nome"].startswith(("ELU 8400", "ELS 8400")) for c in cs))
        self.assertEqual(cs[0]["nbr8400"]["Mx"], 1.06)
        calc = P.calcular(m)
        self.assertTrue(calc.verificacao)
        self.assertIn("acoes.nbr8400", {n["modulo"] for n in calc.normas})

    def test_8681_nunca_junta_servico_e_maximo(self):
        m = _pilar(TUBO_114, V8400)
        P.gerar_acoes(m)
        for c in P.combinacoes(m):
            self.assertLessEqual(sum(k.startswith("V") for k in c["fatores"]), 1, c["nome"])


class TestCobertura(unittest.TestCase):
    def test_normas_implementadas(self):
        mods = {c["modulo"] for c in P.normas_implementadas()}
        self.assertTrue({"acoes.vento_barras", "acoes.nbr8400"} <= mods)


if __name__ == "__main__":
    unittest.main()
