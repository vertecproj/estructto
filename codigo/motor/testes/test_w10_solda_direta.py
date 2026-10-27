"""W10 (docs/v3/W10-SOLDA-DIRETA.md): solda direta com o contorno inteiro soldado (grupo de solda elástico), viga–viga
de tamanhos diferentes (desnível automático ou recorte) e padrão sem "no limite" só pela perna mínima.

Conta à mão 1 — I com momento: W310×38,7 (d 310, bf 165, tw 5,8, tf 9,7) na mesa do W250×73, filete 6 mm E70,
V = 100 kN, N = 50 kN, M = 20 kN·m. Contorno (linhas de garganta unitária, mm):
  mesas por fora 2 × 165 = 330; por dentro 4 × (165 − 5,8)/2 = 318,4; pontas 4 × 9,7 = 38,8; alma 2 × 290,6 = 581,2
  L = 1268,4 mm; centroide no centro (simétrico).
  Iy = Σ∫z² = 2·165·155² + 4·79,6·145,3² + 4·9,7·(145,3² + 145,3·155 + 155²)/3 + 2·290,6³/12 = 19 615 506 mm³
  topo da alma (z = 145,3): fx = N/L + M·z/Iy = 39,42 + 20e3·145,3/19 615 506·1e3 = 39,42 + 148,15 = 187,6 kN/m
                            fz = V/(2·290,6) = 172,06 kN/m  → q = √(187,6² + 172,06²) = 254,5 kN/m
  canto da mesa (z = 155): fx = 39,42 + 158,04 = 197,5 kN/m (sem V) → governa o topo da alma: 254,5 ≤ FwRd(6) = 914,4.

Conta à mão 2 — o caso do usuário: UPN 50 (h 50, bf 38, tw 5, tf 7, xg 14,83) na alma de UPN 100, esforços do modelo
N −0,35; Vz 0,25; V 0,04; T −0,04; Mz −0,16 (kN, kN·m), filete 4 mm:
  desnível auto: apoio U (costas da alma para a viga) → a perna por cima da mesa superior pede 4 mm → 5 mm (múltiplo de 5)
  contorno L = costas 50 + face interna 36 + mesas por dentro 2·33 + por fora 2·38 + pontas 2·7 = 242 mm
  Iy = 89 717 mm³, Iz = Σ∫(y − yc)² = 41 383 mm³ (yc = −0,06 mm), Ip = 131 100 mm³
  6.1.5.2: F = √(0,04² + 0,35² + 0,25²) = 0,432 kN < 45 → × 104,17: V = 4,17; N = 36,46; Vz = 26,04 kN
  ponta da mesa (y = 23,2; z = ±25): fx = 36,46/0,242 + 160·23,23/41 383·1e3 = 150,7 + 89,8 = 240,5 kN/m
      fy = Vz/(lados horizontais 142 mm) + T·dz/Ip = 183,4 + 7,6 = 191,0;  fz = T·dy/Ip = 7,1
      q = √(240,5² + 191,0² + 7,1²) = 307,2 kN/m ≤ FwRd(4) = 609,6 kN/m (0,50)."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L

ESF_MODELO = {"N": -0.35, "Vz": 0.25, "V": 0.04, "T": -0.04, "M": 0.0, "Mz": -0.16}
CONTORNO = "solda: contorno (grupo elástico, ponto mais solicitado)"


def _v(r, nome):
    return next(x for x in r["verificacoes"] if x["nome"].startswith(nome))


def _ver(d, esf=ESF_MODELO):
    return P.verificar_ligacao("solda_direta_alma", d, esf)


def _sug(d, esf=ESF_MODELO):
    s = P.sugerir_ligacao("solda_direta_alma", d, esf)
    return dict(d, **{k: v for k, v in s.items() if k not in ("tipo_ligacao", "motivo")})


class TestContornoAMao(unittest.TestCase):
    def test_i_com_momento(self):
        r = _ver(dict(perfil_viga="W310x38.7", perfil_suporte="W250x73", apoio="mesa_pilar", b_solda=6),
                 {"V": 100, "N": 50, "M": 20})
        self.assertAlmostEqual(r["valores"]["L_contorno_mm"], 1268.4, 6)
        self.assertAlmostEqual(r["valores"]["Iy_grupo_cm3"] * 1e3, 19615506.1, delta=1)
        c = _v(r, CONTORNO)
        q = math.hypot(50 / 1.2684 + 20e3 * 145.3 / 19615506.1 * 1e3, 100 / 0.5812)
        self.assertAlmostEqual(c["solicitante"], q, delta=0.05)
        self.assertAlmostEqual(c["solicitante"], 254.5, delta=0.05)
        self.assertAlmostEqual(c["resistente"], 0.6 * 0.707 * 6 * 485 / 1.35, delta=0.1)
        self.assertFalse(any("ignorados" in a for a in r["avisos"]))   # M entra no grupo (antes: ignorado)

    def test_upn50_upn100_esforcos_do_modelo(self):
        r = _ver(dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="alma_viga", b_solda=4))
        self.assertTrue(r["ok"])
        self.assertAlmostEqual(r["valores"]["desnivel_mm"], 5.0)
        self.assertAlmostEqual(r["valores"]["L_contorno_mm"], 242.0, 6)
        self.assertAlmostEqual(_v(r, CONTORNO)["solicitante"], 307.2, delta=0.05)
        self.assertAlmostEqual(_v(r, CONTORNO)["taxa"], 307.19 / 609.59, delta=1e-3)
        g = r["geometria"]
        self.assertEqual(len(g["soldas"]), 1)
        p = g["soldas"][0]["pontos"]
        self.assertEqual((len(p), p[0]), (9, p[-1]))   # contorno do U fechado: 8 cantos
        self.assertAlmostEqual(g["z_topo_suporte"], 30.0)   # topo do UPN 100 a 5 mm acima do topo do UPN 50
        self.assertLessEqual(r["taxa"], 0.90)


class TestTamanhosDiferentes(unittest.TestCase):
    """Antes: 'solda: abaixo da mesa da viga suporte' comparava tf + r dos dois perfis (UPN 50 em UPN 100: 18,0 > 14,5,
    124 %, sem solução no Sugerir/Otimizar; UPN 50 em UPN 50: 14,5 = 14,5, 100 %)."""

    def test_verificacao_antiga_saiu(self):
        for sup in ("UPN 50", "UPN 100"):
            r = _ver(dict(perfil_viga="UPN 50", perfil_suporte=sup, apoio="alma_viga", b_solda=4))
            self.assertFalse(any("abaixo da mesa" in x["nome"] for x in r["verificacoes"]), sup)

    def test_upn50_upn100_sugerir_e_otimizar(self):
        base = dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="alma_viga")
        r = _ver(_sug(base))
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        o = L.otimizar("solda_direta_alma", dict(base, b_solda=3), ESF_MODELO)   # a perna da captura do usuário
        self.assertTrue(o["ok"])
        self.assertEqual(o["dados"]["b_solda"], 4)   # sobe da mínima (1,00) para 4 mm (0,75)
        self.assertLessEqual(o["resultado"]["taxa"], 0.90)

    def test_upn50_upn50_mesma_altura(self):
        """Mesma altura e apoio U: topos alinhados; as mesas por fora não têm face do apoio (o UPN 50 acaba ali) e ficam sem
        solda (aviso); o resto do contorno (costas, face interna, mesas por dentro e pontas = 166 mm) é soldado."""
        d = _sug(dict(perfil_viga="UPN 50", perfil_suporte="UPN 50", apoio="alma_viga"))
        r = _ver(d)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertAlmostEqual(r["valores"]["desnivel_mm"], 0.0)
        self.assertAlmostEqual(r["valores"]["L_contorno_mm"], 166.0, 6)
        self.assertTrue(any("sem solda" in a for a in r["avisos"]))

    def test_w200_na_alma_de_w310(self):
        """Menor que a altura livre (310 − 2·(9,7 + 10) = 270,6 ≥ 200 + 2·4): desce 9,7 + 10 + 4 = 23,7 → 25 mm e solda
        o contorno inteiro do I (13 pontos), sem recorte."""
        r = _ver(_sug(dict(perfil_viga="W200x15", perfil_suporte="W310x38.7", apoio="alma_viga"), {"V": 50}), {"V": 50})
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertAlmostEqual(r["valores"]["desnivel_mm"], 25.0)
        self.assertEqual(len(r["geometria"]["soldas"][0]["pontos"]), 13)
        self.assertEqual([x["tipo"] for x in r["geometria"]["recortes"]], ["recuo"])

    def test_w310_na_alma_de_w310(self):
        """Mesma altura (apoio I): topos alinhados, as duas mesas recortadas (tf + 10 + b = 23,7 mm; comprimento
        165/2 − 5,8/2 + 10 − 2 = 87,6 mm) e o filete fechado em volta da alma que sobra."""
        r = _ver(_sug(dict(perfil_viga="W310x38.7", perfil_suporte="W310x38.7", apoio="alma_viga"), {"V": 50}), {"V": 50})
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        copes = [x["parametros"] for x in r["geometria"]["recortes"] if x["tipo"] == "cope"]
        self.assertEqual(len(copes), 2)
        for c in copes:
            self.assertAlmostEqual(c["altura"], 23.7, 6)
            self.assertAlmostEqual(c["comprimento"], 87.6, 6)
        p = r["geometria"]["soldas"][0]["pontos"]
        self.assertEqual((len(p), p[0]), (5, p[-1]))
        self.assertTrue(any(x["nome"].startswith("viga recortada") for x in r["verificacoes"]))

    def test_desnivel_informado(self):
        """Desnível 0 (topos alinhados) com o UPN 50 no UPN 100: a mesa superior fica sem solda por cima (aviso)."""
        r = _ver(dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="alma_viga", b_solda=4, desnivel=0))
        self.assertAlmostEqual(r["valores"]["L_contorno_mm"], 242.0 - 38.0, 6)
        self.assertTrue(any("sem solda" in a for a in r["avisos"]))


class TestPadrao(unittest.TestCase):
    def test_soldar_mesas_sim_por_padrao(self):
        ent = {t["tipo"]: t for t in P.tipos_ligacao()}["solda_direta_alma"]
        c = next(x for x in ent["campos"] if x["nome"] == "soldar_mesas")
        self.assertEqual((c["padrao"], c["opcoes"]), ("sim", ["sim", "não"]))
        r = _ver(dict(perfil_viga="UPN 50", perfil_suporte="UPN 100", apoio="alma_viga", b_solda=4, soldar_mesas="não"))
        self.assertTrue(any("só a alma soldada" in a for a in r["avisos"]))

    def test_padrao_de_fabrica_abaixo_de_090(self):
        ex = {t["tipo"]: t for t in P.tipos_ligacao()}["solda_direta_alma"]["exemplo"]
        r = P.verificar_ligacao("solda_direta_alma", ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertEqual(len(r["geometria"]["soldas"][0]["pontos"]), 13)

    def test_otimizar_nao_desce_ate_a_minima(self):
        """W10 (= W8-TUBO): a descida do otimizar não leva a perna à mínima da tabela 10 (taxa 1,00 só por disposição)."""
        o = L.otimizar("solda_direta_alma", dict(perfil_viga="W310x38.7", perfil_suporte="W250x73", apoio="mesa_pilar",
                                                 b_solda=10), {"V": 100})
        self.assertTrue(o["ok"])
        self.assertEqual(o["dados"]["b_solda"], 6)   # mínima 5 (mesa 9,7 mm): 5/6 = 0,83
        self.assertLessEqual(o["resultado"]["taxa"], 0.90)


if __name__ == "__main__":
    unittest.main()
