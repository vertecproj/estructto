"""Barras compostas de 2 perfis (secoes.secao_dupla, NBR 8800 5.3.4) e painéis de carga do CYPE.

Valores esperados calculados à mão a partir das dimensões (nunca da saída do código):
UPN 160 (h 160, b 65, tw 7,5, tf 10,5 mm; mesas paralelas, cantos vivos, como secoes.secao_U).
2 UPN 160 em caixão soldado (abas para dentro, pontas unidas) = caixão 130 × 160, almas 7,5, mesas 10,5:
  A  = 130·160 − 115·139 = 4815 mm²
  Ix = (130·160³ − 115·139³)/12 = 18 636 151 mm⁴;  Iy = (160·130³ − 139·115³)/12 = 11 676 531 mm⁴
  J  = 4·Am²/∮ds/t (Bredt), Am = 149,5·122,5 = 18 313,75 mm², ∮ = 2·122,5/10,5 + 2·149,5/7,5 = 63,2
     → J = 21 227 433 mm⁴ (soma aberta seria só ~1,5e5 mm⁴)
"""
import math
import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import importar_cype as ic, materiais, secoes
from prossen_estrutural.aco import nbr8800

UPN160 = dict(h=0.160, b=0.065, tw=0.0075, tf=0.0105)
A36 = materiais.aco("A-36")  # fy 250 MPa, E 200 GPa
PIPERACK = Path(r"C:\CYPE Ingenieros\Projetos\CYPE 3D\PIPERACK.ed3")
LENTO = os.environ.get("PROSSEN_PULAR_LENTOS") == "1"


def upn():
    return secoes.secao_U(**UPN160, nome="UPN 160")


class TestPropriedades(unittest.TestCase):
    def test_caixao_soldado_a_mao(self):
        s = secoes.secao_dupla(upn(), "caixao", "soldada")
        self.assertAlmostEqual(s.A, 4815e-6, delta=1e-9)
        self.assertAlmostEqual(s.Iy, 18_636_151e-12, delta=1e-10)   # Secao.Iy = Ix da norma (forte)
        self.assertAlmostEqual(s.Iz, 11_676_531e-12, delta=1e-10)
        self.assertAlmostEqual(s.J, 21_227_433e-12, delta=2e-10)
        self.assertEqual(s.props["tipo"], "tubo_retangular")
        self.assertEqual(s.props["Cw"], 0.0)
        self.assertEqual(s.props["composicao"]["ligacao"], "soldada")
        self.assertAlmostEqual(s.props["Az"], 2 * 0.160 * 0.0075)   # Vz pelas almas

    def test_A_I_iguais_ao_U_com_steiner(self):
        u = upn()
        for arr in ("caixao", "I"):
            for lig in ("soldada", "presilhas"):
                s = secoes.secao_dupla(u, arr, lig)
                self.assertAlmostEqual(s.A, 2 * u.A, places=12)
                self.assertAlmostEqual(s.props["Ix"], 2 * u.props["Ix"], places=12)

    def test_I_soldado_igual_a_I_de_alma_dupla(self):
        # 2U costas com costas soldados = I 160 × 130, alma 15, mesa 10,5: Iy = 2·(10,5·130³/12) + 139·15³/12
        s = secoes.secao_dupla(upn(), "I", "soldada")
        self.assertAlmostEqual(s.Iz, (2 * 10.5 * 130 ** 3 / 12 + 139 * 15 ** 3 / 12) * 1e-12, delta=1e-11)
        self.assertEqual(s.props["tipo"], "I")

    def test_presilhas_steiner_e_soma_aberta(self):
        u = upn()
        s = secoes.secao_dupla(u, "caixao", "presilhas", s=0.02)
        d = (2 * 0.065 + 0.02) / 2 - u.props["xg"]
        self.assertAlmostEqual(s.props["Iy"], 2 * (u.props["Iy"] + u.A * d * d), places=14)
        self.assertAlmostEqual(s.J, 2 * u.J, places=16)
        self.assertAlmostEqual(s.props["Zy"], 2 * u.A * d, places=14)
        self.assertEqual(s.props["tipo"], "dupla")

    def test_2L_costas_com_costas(self):
        L = secoes.secao_cantoneira(0.05, 0.05, 0.005)
        s = secoes.secao_dupla(L, "I", "presilhas", s=0.008)
        # L 50×5: A = 475 mm², xg = yg = (50·5·25 + 45·5·2,5)/475 = 14,342 mm
        xg = (50 * 5 * 25 + 45 * 5 * 2.5) / 475
        self.assertAlmostEqual(s.A, 950e-6, places=12)
        self.assertAlmostEqual(s.props["Iy"], 2 * (L.props["Iy"] + 475e-6 * ((4 + xg) * 1e-3) ** 2), places=14)
        self.assertAlmostEqual(s.props["y0"], (2.5 - xg) * 1e-3, places=9)   # CC no encontro das abas
        self.assertEqual(s.props["x0"], 0.0)

    def test_entrada_invalida(self):
        with self.assertRaises(ValueError):
            secoes.secao_dupla(upn(), "T", "soldada")
        with self.assertRaises(ValueError):
            secoes.secao_dupla(secoes.secao_I(0.2, 0.1, 0.005, 0.008), "I", "soldada")


class TestVerificacao(unittest.TestCase):
    def test_compressao_caixao_a_mao(self):
        s = secoes.secao_dupla(upn(), "caixao", "soldada")
        # Q = 1 (115/10,5 = 11,0 e 139/7,5 = 18,5 < 1,40√(E/fy) = 39,6); Ney = π²·E·Iy/KL², KL = 3 m
        Ney = math.pi ** 2 * 200e6 * 11_676_531e-12 / 9
        l0 = math.sqrt(4815e-6 * 250e3 / Ney)
        esperado = 0.658 ** (l0 ** 2) * 4815e-6 * 250e3 / 1.10
        r = nbr8800.compressao(s, A36, 3.0, 3.0)
        self.assertAlmostEqual(r["NcRd"], esperado, delta=0.005 * esperado)
        self.assertAlmostEqual(esperado, 898.9, delta=1.0)

    def test_flexao_caixao_a_mao(self):
        s = secoes.secao_dupla(upn(), "caixao", "soldada")
        # G.1 caixão: Zx = (130·160² − 115·139²)/4; λ = Lb/ry; λp = 0,13E√(JA)/Mpl; λr = 2E√(JA)/Mr, Mr = 0,7fyW
        Zx = (130 * 160 ** 2 - 115 * 139 ** 2) / 4 * 1e-9
        Wx = 2 * 18_636_151e-12 / 0.160
        Mpl, Mr = Zx * 250e3, 0.7 * 250e3 * Wx
        JA = math.sqrt(21_227_433e-12 * 4815e-6)
        lam = 3.0 / math.sqrt(11_676_531e-12 / 4815e-6)
        lp, lr = 0.13 * 200e6 * JA / Mpl, 2.0 * 200e6 * JA / Mr
        self.assertLess(lam, lp)   # 60,9 < 120: caixão compacto sem FLT → Mn = Mpl = 69,13 kN·m
        Mn = Mpl if lam <= lp else Mpl - (Mpl - Mr) * (lam - lp) / (lr - lp)
        r = nbr8800.flexao_x(s, A36, 3.0)
        self.assertAlmostEqual(Mpl, 69.13, delta=0.01)
        self.assertAlmostEqual(r["MRd"], Mn / 1.10, delta=0.005 * Mn)

    def test_presilhas_5_3_4(self):
        u = upn()
        conj = secoes.secao_dupla(u, "I", "presilhas", l_ligacoes=0.5)
        r = nbr8800.compressao(conj, A36, 3.0, 3.0)
        # 0,5 m / ry do UPN ≤ 1/2 · max(KL/r) do conjunto
        self.assertLessEqual(r["composta"]["l_ri"], r["composta"]["KL_r_conjunto"] / 2)
        longe = secoes.secao_dupla(u, "I", "presilhas", l_ligacoes=1.5)
        with self.assertRaises(NotImplementedError):
            nbr8800.compressao(longe, A36, 3.0, 3.0)
        sem_l = secoes.secao_dupla(u, "I", "presilhas")
        v = nbr8800.verificar_barra_aco(sem_l, A36, dict(N=-10.0), 3.0, 3.0, 3.0)
        self.assertFalse(v["ok"])
        self.assertEqual(v["criterio"], "nao_verificado")

    def test_presilhas_flexao_x_dobro_e_y_reprova(self):
        u = upn()
        conj = secoes.secao_dupla(u, "I", "presilhas", l_ligacoes=0.5)
        self.assertAlmostEqual(nbr8800.flexao_x(conj, A36, 2.0)["MRd"], 2 * nbr8800.flexao_x(u, A36, 2.0)["MRd"])
        v = nbr8800.verificar_barra_aco(conj, A36, dict(My=1.0), 3.0, 3.0, 3.0)
        self.assertFalse(v["ok"])


class TestCompostasV06(unittest.TestCase):
    """v0.6 Frente D. Valores à mão (mm), mesas paralelas e cantos vivos:
    UPN 200 (h 200, b 75, tw 8,5, tf 11,5): A1 = 2·75·11,5 + 177·8,5 = 3229,5 mm²;
      xg = (2·862,5·37,5 + 1504,5·4,25)/3229,5 = 22,010; Ix1 = (75·200³ − 66,5·177³)/12 = 19 270 167;
      Iy1 = 2·(11,5·75³/12 + 862,5·15,49²) + 177·8,5³/12 + 1504,5·17,76² = 1 706 102.
    2 UPN 200 caixão soldado s = 0 = caixão 150 × 200 (almas 8,5, mesas 11,5):
      A = 150·200 − 133·177 = 6459; Ix = (150·200³ − 133·177³)/12 = 38 540 334; Iy = (200·150³ − 177·133³)/12
      = 21 548 604; J (Bredt) = 4·(188,5·141,5)²/(2·141,5/11,5 + 2·188,5/8,5) = 41 265 7xx.
    2 UPN 200 costas com costas s = 10: Ix = 2·Ix1 = 38 540 334; d = 5 + 22,010 = 27,010;
      Iy = 2·(1 706 102 + 3229,5·27,010²) = 8 124 304.
    2L 2"×1/4" (50,8 × 6,35): A1 = 322,58 + 282,26 = 604,84; xg = yg = 15,028; Ix1 = Iy1 = 144 678
      (Gerdau tabela: 14,6 cm⁴); Ixy1 = −84 980.
      costas com costas s = 6,3: Ix = 289 356; d = 3,15 + 15,028 = 18,178; Iy = 2·(144 678 + 604,84·330,44) = 689 088.
      em cruz s = 6,3: Ix = Iy = 689 088; Ixy = 2·(−84 980 + 604,84·18,178²) = 229 772; Imin = 459 316, rmin = 19,49.
    I 200 × 100 × 6 × 10 + chapa 100 × 10 na mesa superior: A = 3080 + 1000 = 4080; yc = 1000·105/4080 = 25,735;
      Ix = 20 982 667 + 3080·25,735² + 100·10³/12 + 1000·79,265² = 29 313 793."""

    def u200(self):
        return secoes.secao_U(0.2, 0.075, 0.0085, 0.0115, nome="UPN 200")

    def L2(self):
        return secoes.secao_cantoneira(0.0508, 0.0508, 0.00635, nome='L 2"x1/4"')

    def chk(self, v, esp, tol=0.005):
        self.assertAlmostEqual(v, esp, delta=abs(esp) * tol)

    def test_2upn200_caixao_s0(self):
        s = secoes.secao_dupla(self.u200(), "caixao", "soldada")
        self.chk(s.A * 1e6, 6459)
        self.chk(s.props["Ix"] * 1e12, 38_540_334)
        self.chk(s.props["Iy"] * 1e12, 21_548_604)
        self.chk(s.J * 1e12, 4 * (188.5 * 141.5) ** 2 / (2 * 141.5 / 11.5 + 2 * 188.5 / 8.5))

    def test_2upn200_costas_s10(self):
        s = secoes.secao_dupla(self.u200(), "I", "presilhas", s=0.010, chapas_espacadoras=True, l_ligacoes=0.6)
        self.chk(s.A * 1e6, 6459)
        self.chk(s.props["Ix"] * 1e12, 38_540_334)
        self.chk(s.props["Iy"] * 1e12, 8_124_304)
        r = nbr8800.compressao(s, A36, 3.0, 3.0)   # 5.3.4: 0,6/ry1 ≤ ½·KL/r do conjunto
        ry1 = math.sqrt(1_706_102 / 3229.5) * 1e-3
        self.chk(r["composta"]["l_ri"], 0.6 / ry1)

    def test_2L_costas_s63(self):
        s = secoes.secao_dupla(self.L2(), "I", "presilhas", s=0.0063, chapas_espacadoras=True)
        self.chk(s.A * 1e6, 1209.68)
        self.chk(s.props["Ix"] * 1e12, 289_356)
        self.chk(s.props["Iy"] * 1e12, 689_088)

    def test_2L_cruz_s63(self):
        s = secoes.secao_dupla(self.L2(), "cruz", "presilhas", s=0.0063, chapas_espacadoras=True, l_ligacoes=0.4)
        self.chk(s.props["Ix"] * 1e12, 689_088)
        self.chk(s.props["Ixy"] * 1e12, 229_772)
        self.chk(s.props["rmin"] * 1e3, 19.49)
        r = nbr8800.compressao(s, A36, 2.0, 2.0)   # Ne pelos eixos principais (Iv)
        self.chk(r["Ney"] if "Ney" in r else nbr8800.forca_flambagem(s, A36, 2, 2, 2)["Ney"],
                 math.pi ** 2 * 200e6 * 459_316e-12 / 4)

    def test_2I_lado_a_lado(self):
        i = secoes.secao_I(0.2, 0.1, 0.006, 0.010)
        s = secoes.secao_dupla(i, "lado_a_lado", "soldada", s=0.05)
        d = (0.1 + 0.05) / 2
        self.chk(s.props["Iy"], 2 * (i.props["Iy"] + i.A * d * d))
        self.chk(s.props["Zx"], 2 * i.props["Zx"])
        self.chk(nbr8800.flexao_x(s, A36, 2.0)["MRd"], 2 * nbr8800.flexao_x(i, A36, 2.0)["MRd"])

    def test_I_chapa(self):
        s = secoes.secao_de_spec({"forma": "dupla", "arranjo": "I_chapa",
                                  "base": {"forma": "I", "h": 0.2, "b": 0.1, "tw": 0.006, "tf": 0.010},
                                  "chapa": {"b": 0.1, "t": 0.010}})
        self.chk(s.A * 1e6, 4080)
        self.chk(s.props["Ix"] * 1e12, 29_313_793)
        self.assertGreater(s.props["y0"], 0)          # CC sobe para a mesa reforçada
        with self.assertRaises(NotImplementedError):  # flexão de I monossimétrico com chapa: fora da G.1
            nbr8800.flexao_x(s, A36, 2.0)

    def test_spec_dupla(self):
        s = secoes.secao_de_spec({"forma": "dupla", "base": {"forma": "U", "h": 0.2, "b": 0.075, "tw": 0.0085,
                                  "tf": 0.0115}, "arranjo": "caixao", "ligacao": "soldada", "s": 0})
        self.chk(s.props["Ix"] * 1e12, 38_540_334)
        with self.assertRaises(ValueError):
            secoes.secao_de_spec({"forma": "dupla", "base": {"forma": "U", "h": 0.2, "b": 0.075, "tw": 0.0085,
                                  "tf": 0.0115}, "arranjo": "cruz"})


class TestPainel(unittest.TestCase):
    def test_equilibrio_e_faixas_biapoiadas(self):
        # painel 4 × 6, transmissão em x; barras em x = 0, 2, 4 (paralelas a y): faixas de 2 m biapoiadas
        poly = [(0, 0), (4, 0), (4, 6), (0, 6)]
        barras = {"E": ((0, 0), (0, 6)), "M": ((2, 0), (2, 6)), "D": ((4, 0), (4, 6)), "X": ((0, 0), (4, 0))}
        out, perdida = ic.distribuir_painel(poly, (1, 0), 2.0, barras)
        tot = {b: sum(f for _, f in fs) for b, fs in out.items()}
        self.assertAlmostEqual(sum(tot.values()), 2.0 * 24, places=9)   # pressão × área
        self.assertAlmostEqual(tot["E"], 2.0 * 6 * 1, places=9)
        self.assertAlmostEqual(tot["M"], 2.0 * 6 * 2, places=9)
        self.assertNotIn("X", out)   # paralela à transmissão
        self.assertEqual(perdida, 0.0)

    def test_balanco_vai_para_a_barra_e_sem_barra_perde(self):
        poly = [(0, 0), (5, 0), (5, 1), (0, 1)]
        out, perdida = ic.distribuir_painel(poly, (1, 0), 1.0, {"A": ((1, -1), (1, 2))})
        self.assertAlmostEqual(sum(f for _, f in out["A"]), 5.0, places=9)
        out, perdida = ic.distribuir_painel(poly, (1, 0), 1.0, {"A": ((1, 5), (1, 7))})
        self.assertAlmostEqual(perdida, 5.0, places=9)

    @unittest.skipUnless(PIPERACK.exists() and not LENTO, "PIPERACK.ed3 ausente ou testes lentos pulados")
    def test_piperack_paineis_equilibrio_com_cype(self):
        # 2 painéis verticais de 0,124 t/m²: 2 × 14 m (V 1, +X) e 2 × 2 m (V 2, +Y); o restante de V 1/V 2 são
        # cargas em barra. ΣR do CYPE (bloco de resultados): V 1 Fx = 37,383 kN, V 2 Fy = 8,198 kN.
        m = ic.ler_ed3(PIPERACK)
        soma = {}
        for c in m.cargas_distribuidas + m.cargas_pontuais:
            if c.caso in ("V 1", "V 2"):
                b = m.barras[c.barra]
                L = math.dist(*[(m.nos[n].x, m.nos[n].y, m.nos[n].z) for n in (b.ni, b.nj)])
                F = c.q if hasattr(c, "q") else c.F
                k = L if hasattr(c, "q") else 1.0
                soma[c.caso] = [a + k * f for a, f in zip(soma.get(c.caso, [0, 0, 0]), F)]
        self.assertAlmostEqual(soma["V 1"][0], 37.383, delta=0.01)
        self.assertAlmostEqual(soma["V 2"][1], 8.198, delta=0.01)
        self.assertFalse(any("painel(éis) de carga superficial (cargas" in a for a in m.avisos))


class TestImportacaoComposta(unittest.TestCase):
    def _pz(self, tipo, sep=None, presilha=None):
        pz = [1, tipo, [1, 0, 7, "UPN", "UPN 160"]] + [None] * 13
        pz[5], pz[10] = sep, presilha
        return pz

    def test_tipo_3_duplo_em_tubo_soldado(self):
        s, nota = ic._secao_composta(upn(), ic._COMPOSTAS[3], self._pz(3), "UPN 160")
        self.assertIsNone(nota)
        self.assertAlmostEqual(s.A, 4815e-6, delta=1e-9)
        self.assertIn("duplo em tubo soldado", s.nome)

    def test_tipo_8_presilhas_com_separacao_do_usuario(self):
        s, nota = ic._secao_composta(upn(), ic._COMPOSTAS[8], self._pz(8, [1, 0.0, 0.0],
                                                                       [1, 0, 0, "", 1, 0.6]), "UPN 160")
        self.assertEqual(s.props["composicao"]["l_ligacoes"], 0.6)
        self.assertEqual(s.props["composicao"]["arranjo"], "I")

    def test_tipo_4_chapas_nao_importa(self):
        s, nota = ic._secao_composta(upn(), ic._COMPOSTAS[4], self._pz(4), "UPN 160")
        self.assertIsNone(s)
        self.assertIn("chapas", nota)


def _plataforma():
    for base in (Path(r"C:\CYPE Ingenieros\Projetos"), Path(__file__).resolve().parents[1] / "trabalho"):
        for p in base.rglob("PLATAFORMA.ed3") if base.exists() else ():
            return p
    return None


class TestPlataforma(unittest.TestCase):
    @unittest.skipUnless(_plataforma(), "PLATAFORMA.ed3 não encontrado (Projetos do CYPE ou trabalho/)")
    def test_importa_compostas_e_paineis(self):
        m = ic.ler_ed3(_plataforma())  # só leitura
        self.assertFalse([a for a in m.avisos if "perfil composto (tipo de peça 3)" in a])
        self.assertFalse([a for a in m.avisos if "painel(éis) de carga superficial (cargas" in a])
        self.assertTrue(any(sec.props.get("composicao") for sec in m.secoes.values()))


if __name__ == "__main__":
    unittest.main()
