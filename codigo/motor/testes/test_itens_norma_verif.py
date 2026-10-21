"""Verificação independente dos itens de norma marcados "a conferir" (docs/v3/ITENS-NORMA.md).

Valores esperados derivados do texto das normas, não do código:
  [8681]  ABNT NBR 8681:2003, 5.1.4.1 b) e tabela 3 (p. 8 e 10).
  [8800]  ABNT NBR 8800:2008, 5.4.3.1.1 (p. 50), 5.7.7.1 (p. 61), 5.7.9.5 (p. 62), 6.1.5.2 (p. 63).
  [14762] ABNT NBR 14762:2010, tabelas 11 e 14 (dispensa da distorcional).
O id do achado (IN0x) está no nome do teste e no ITENS-NORMA.md; todos corrigidos no motor.
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes  # noqa: E402
from prossen_estrutural.acoes import combinacoes as cb  # noqa: E402
from prossen_estrutural.aco import ligacoes_contraventamento as C  # noqa: E402
from prossen_estrutural.aco import ligacoes_rigidas as lr  # noqa: E402
from prossen_estrutural.aco import nbr14762 as pf  # noqa: E402

E = 200000.0  # MPa


class TestNBR8681Recalque(unittest.TestCase):
    def test_tabela3_gama_epsilon(self):
        # [8681] tab. 3: normal 1,2/0; especial ou de construção 1,2/0; excepcional 0/0
        self.assertEqual(cb.GAMA_RECALQUE, {"normal": (1.2, 0.0), "especial": (1.2, 0.0),
                                            "excepcional": (0.0, 0.0)})


class TestNBR8800Ligacoes(unittest.TestCase):
    def test_IN01_gusset_minimo_45kN(self):
        # [8800] 6.1.5.2: ligação com força de cálculo < 45 kN é dimensionada para 45 kN (diagonal de
        # contraventamento em 2L não está entre as exceções). Pfeil 3.5.2 com N = 10 kN.
        P = dict(secao="2L", dim_a=63, t_perfil=6.3, A_perfil=768, xg_perfil=18, aco_perfil="A-36",
                 ligacao="parafusada", n_linhas=1, n_por_linha=5, s=40, e1=25, e_gusset=25, g_aba=32, d_mm=12.7,
                 tipo_parafuso="A307", t_gusset=6.3, aco_gusset="A-36", apoio="viga-pilar", theta=45, eb=150, ec=150)
        r = C.verificar_gusset(P, {"N": 10.0})
        grupo = next(v for v in r["verificacoes"] if v["nome"].startswith("parafusos: grupo"))
        self.assertGreaterEqual(grupo["solicitante"], 45.0 - 1e-9)

    def test_IN02_painel_alma_esbelta_5_4_3(self):
        # [8800] 5.7.7.1: VRd do painel pela 5.4.3 com a = distância entre centros das mesas da viga.
        # Pilar soldado d = 400, tw = 4,75, tf = 12,5 (A-572 Gr. 50, fy = 345 MPa); viga W610x155 (d = 611, tf = 19).
        fy, d, tw, h = 345.0, 400.0, 4.75, 400.0 - 2 * 12.5          # MPa, mm
        a = 611.0 - 19.0                                              # 592 mm
        lam = h / tw                                                  # 78,95
        kv = 5.0 if (a / h > 3 or a / h > (260 / lam) ** 2) else 5 + 5 / (a / h) ** 2   # 7,006
        lp, lr_ = 1.10 * math.sqrt(kv * E / fy), 1.37 * math.sqrt(kv * E / fy)       # 70,10; 87,31
        Vpl = 0.60 * d * tw * fy / 1000.0                             # kN (Aw = d·tw)
        fator = 1.0 if lam <= lp else (lp / lam if lam <= lr_ else 1.24 * (lp / lam) ** 2)
        VRd = fator * Vpl / 1.10                                      # ≈ 317,5 kN
        r = lr.verificar("painel_alma_pilar", dict(perfil_viga="W610x155", aco_pilar="A-572 GR.50",
                                                   perfil_pilar=dict(d=400, bf=300, tw=4.75, tf=12.5, r=0)),
                         {"M": 100})
        painel = next(v for v in r["verificacoes"] if v["nome"].startswith("cisalhamento do painel"))
        self.assertLessEqual(painel["resistente"], VRd * 1.01)       # motor: 357,5 kN (Vpl, sem 5.4.3)

    def test_IN03_enrijecedor_t_minimo_b_15(self):
        # [8800] 5.7.9.5 b): t ≥ b/15. W310x97 (bf ≈ 305, tw ≈ 9,9): b ≈ 137,5 mm → t ≥ 9,17 mm; t = 9,0 reprova.
        r = lr.verificar("painel_alma_pilar", dict(perfil_viga="W460x89", perfil_pilar="W310x97",
                                                   aco_pilar="A-36", t_enr_pilar=9.0), {"M": 300})
        enr = [v for v in r["verificacoes"] if v["nome"].startswith("enrijecedor do pilar")]
        self.assertTrue(any(not v["ok"] for v in enr), [(v["nome"], v["taxa"]) for v in enr])

    def test_IN04_item_enrijecedores_5_7_9(self):
        # [8800] 5.7.8 = apoios sem restrição à rotação; enrijecedores para forças localizadas = 5.7.9
        r = lr.verificar("painel_alma_pilar", dict(perfil_viga="W460x89", perfil_pilar="W310x97",
                                                   t_enr_pilar=16), {"M": 300})
        itens = {v["item"] for v in r["verificacoes"] if "enrijecedor" in v["nome"]}
        self.assertTrue(itens and all("5.7.9" in i and "5.7.8" not in i for i in itens), itens)


    def test_IN01_barra_redonda_isenta(self):
        # [8800] 6.1.5.2: tirantes de barras redondas são exceção ao mínimo de 45 kN
        P = dict(secao="barra_redonda", dim_a=16, aco_perfil="A-36", ligacao="soldada", t_gusset=8, aco_gusset="A-36",
                 apoio="viga-pilar", theta=45, eb=150, ec=150)
        r = C.verificar_gusset(P, {"N": 10.0})
        self.assertFalse(any("6.1.5.2" in a for a in r["avisos"]), r["avisos"])

    def test_IN05_reforco_dois_lados_150mm(self):
        # [8800] 5.7.7.2: chapas dos dois lados da alma, 150 mm além das forças; sem o (dz + wz)/t ≤ 90 do AISC 341
        r = lr.verificar("painel_alma_pilar", dict(perfil_viga="W460x89", perfil_pilar="W310x97", t_enr_pilar=16,
                                                   t_reforco=6.3), {"M": 300})
        ch = [c for c in r["geometria"]["chapas"] if c["nome"] == "chapa de reforço da alma"]
        self.assertEqual(sorted(c["origem"][1] for c in ch)[0], -sorted(c["origem"][1] for c in ch)[1])
        vg = secoes.catalogo("W460x89").props
        zr = max(p[1] for p in ch[0]["contorno"])
        self.assertAlmostEqual(zr, (vg["h"] / 2 - vg["tf"] / 2 + 0.150) * 1000, places=2)
        self.assertFalse(any("90" in v["nome"] for v in r["verificacoes"]))

    def test_IN06_furo_maior_que_tab12_exige_arruela_soldada(self):
        # [8800] 6.3.6.1 / tab. 12: furo da placa de base (DG1) maior que db + 5 → arruela soldada
        from prossen_estrutural.aco import placa_base as p
        W = dict(h=.203, b=.203, tw=.0072, tf=.011, tipo='I')
        D = dict(H=.4, B=.3, esp=.019, borda=.05, fileiras=2, por_fileira=2, diametro_mm=16, classe='8.8',
                 rosca_no_corte=True, seguranca_modelo=2)
        e = dict(N=0, V=10, M=30)
        self.assertIn("6.3.6.1", str(p.verificar_placa_base(D, e, W)))
        self.assertNotIn("6.3.6.1", str(p.verificar_placa_base(dict(D, arruela_soldada=True), e, W)))

    def test_IN08_flexivel_minimo_45kN(self):
        # [8800] 6.1.5.2: V = 10 kN → 45 kN; terça/travessa de fechamento é exceção
        from prossen_estrutural.aco import ligacoes_flexiveis as lf
        base = dict(perfil_viga="W360x51", perfil_suporte="W530x92", apoio="alma_viga", cantoneira="L76x76x6.35",
                    d=19.05, n=3, s=75, e_v=40, dist_topo_furo=75, g_viga=45, g_apoio=45, folga=10,
                    excentricidade_apoio=False)
        par = lambda r: next(v for v in r["verificacoes"] if v["nome"].startswith("parafusos no apoio"))  # noqa: E731
        self.assertAlmostEqual(par(lf.TIPOS["cantoneira_dupla_alma"]["verificar"](base, {"V": 10}))["solicitante"], 45 / 6, 3)
        self.assertAlmostEqual(par(lf.TIPOS["cantoneira_dupla_alma"]["verificar"](dict(base, terca_ou_travessa=True),
                                                {"V": 10}))["solicitante"], 10 / 6, 3)

    def test_IN08_rigida_minimo_45kN(self):
        r = lr.verificar("chapa_topo_ajustada", dict(perfil_viga="W310x38.7", d_mm=19.05, tp=19), {"V": 10})
        cis = next(v for v in r["verificacoes"] if v["nome"].startswith("parafusos: cisalhamento"))
        self.assertAlmostEqual(cis["solicitante"] * 4, 45.0, 3)


class TestNBR14762Distorcional(unittest.TestCase):
    def test_ue150_flexao_nao_dispensada_e_fora_do_aisi(self):
        # [14762] tab. 14, bf/bw = 0,4: D/bw mín. = 0,25 (bw/t = 50) a 0,12 (100) → 0,233 em bw/t = 56,6;
        # D/bw = 20/150 = 0,133 → não dispensa. bo/t = 60/2,65 = 22,6 < 25 (limite do AISI) → não calcula.
        s = secoes.secao_Ue(0.150, 0.060, 0.020, 0.00265)
        disp = pf.dispensa_distorcional(s, "flexao")
        self.assertFalse(disp["dispensada"])
        self.assertAlmostEqual(disp["D_bw_min"], 0.25 - 0.13 * (150 / 2.65 - 50) / 50, places=6)
        self.assertTrue(pf.dispensa_distorcional(s, "compressao")["dispensada"])   # tab. 11: 0,075 ≤ 0,133


if __name__ == "__main__":
    unittest.main()
