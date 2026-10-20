"""Regressão de aco/ligacoes.py — verificador-calculos, ficha 019 (2026-10-20). Tolerância 1 % (livro), exato (tabela).

Valores esperados de `verificacao/memorias/ligacoes-casos.py` (só stdlib, sem importar o código), com fontes:
  [NBR]    ABNT NBR 8800:2008: 6.2.2.2 c), 6.2.6 tab. 10, 6.3.3.1–6.3.3.4, 6.3.8, 6.3.9, 6.3.11 tab. 14, 6.3.12,
           6.5.3, 6.5.5, 6.5.6, tab. A.3 (fub: A325 825 até 24 mm / 1", 725 acima; A490 1035).
  [GERDAU] Baião Filho e Silva, "Ligações para estruturas de aço", Gerdau, 6ª ed., 2014, tab. 1–4 p. 15.
Achados: `verificacao/2026-10-20-ligacoes.md`.
"""
import json
import unittest

from prossen_estrutural.aco import ligacoes as L

MM = 1e-3
POL = 25.4


def _ver(r, nome):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(nome))


def _par(d, tipo="A325", **kw):
    dados = dict(d_mm=d, tipo_parafuso=tipo, t=0.010, lf=0.05, aco_chapa="A-36", **kw)
    return L.verificar_ligacao("parafuso", dados, {"Ft": 1.0, "Fv": 1.0})


class TestParafuso(unittest.TestCase):
    def test_tabelas_gerdau_a325(self):
        """[GERDAU] tab. 1 (Ft,Rd) e tab. 2 (Fv,Rd, rosca no plano), kN."""
        for pol, ft, fv in ((5 / 8, 90.8, 48.4), (3 / 4, 130.6, 69.7), (7 / 8, 177.8, 94.8), (1, 232.4, 123.9)):
            r = _par(pol * POL)
            self.assertAlmostEqual(_ver(r, "tração")["resistente"] / ft, 1, delta=0.01)
            self.assertAlmostEqual(_ver(r, "cisalhamento")["resistente"] / fv, 1, delta=0.01)

    def test_fub_tabela_a3(self):
        """[NBR] tab. A.3: A325 M24 → 825; M27 → 725; A490 → 1035. Ft = 0,75·Ab·fub/1,35."""
        import math
        ft = lambda d, fub: 0.75 * math.pi * d * d / 4 * fub / 1.35 / 1e3
        self.assertAlmostEqual(_ver(_par(24), "tração")["resistente"], ft(24, 825), delta=1e-6)
        self.assertAlmostEqual(_ver(_par(27), "tração")["resistente"], ft(27, 725), delta=1e-6)
        self.assertAlmostEqual(_ver(_par(19.05, "A490"), "tração")["resistente"], ft(19.05, 1035), delta=1e-6)
        r = _par(19.05, "A490", rosca_no_plano=False)
        self.assertAlmostEqual(_ver(r, "cisalhamento")["resistente"], 163.89 * 0.5 / 0.75, delta=0.01)

    def test_contato_gerdau(self):
        """[GERDAU] tab. 4 (5/8", A36, t = 10 mm, lf = 1,5d − furo/2 com furo 18 mm) = 52,6 kN;
        tab. 3 (3/4", A572-50, lf = 3d − 21 mm) = 144,6 kN."""
        r = L.verificar_ligacao("parafuso", dict(d_mm=15.875, t=0.010, lf=0.0148125, aco_chapa="A-36"), {"Fv": 1})
        self.assertAlmostEqual(_ver(r, "pressão")["resistente"] / 52.6, 1, delta=0.01)
        r = L.verificar_ligacao("parafuso", dict(d_mm=19.05, t=0.010, lf=0.03615, aco_chapa="A-572 GR.50"), {"Fv": 1})
        self.assertAlmostEqual(_ver(r, "pressão")["resistente"] / 144.6, 1, delta=0.01)

    def test_interacao(self):
        """[NBR] 6.3.3.4: (60/130,64)² + (40/69,67)² = 0,5406."""
        r = L.verificar_ligacao("parafuso", dict(d_mm=19.05, t=0.010, lf=0.05), {"Ft": 60, "Fv": 40})
        self.assertAlmostEqual(_ver(r, "tração +")["solicitante"], 0.5406, delta=1e-3)
        json.dumps(r)


class TestSolda(unittest.TestCase):
    def test_tabela10_e_maximo(self):
        """[NBR] tab. 10 (até 6,35 → 3; até 12,5 → 5; até 19 → 6; acima → 8) e 6.2.6.2.2 (t − 1,5 se t ≥ 6,35)."""
        for t, b in ((6.35, 3), (6.36, 5), (12.5, 5), (12.6, 6), (19.0, 6), (19.1, 8)):
            self.assertAlmostEqual(L.filete_minimo(t * MM) / MM, b, delta=1e-9, msg=t)
        for t, b in ((6.0, 6.0), (6.35, 4.85), (12.5, 11.0)):
            self.assertAlmostEqual(L.filete_maximo(t * MM) / MM, b, delta=1e-9, msg=t)

    def test_filete(self):
        """b = 5 mm, E70: metal 0,6·0,707·5·485/1,35 = 0,762 kN/mm; metal base (face de fusão, CBCA 5.1.6 p. 16)
        0,6·5·250/1,1 = 0,682 kN/mm (6,8 kN/cm no livro)."""
        r = L.verificar_ligacao("solda_filete", dict(b=0.005, L=0.2, t_base=0.008), {"F": 120})
        v = _ver(r, "solda: resistência")
        self.assertAlmostEqual(v["resistente"], 681.82, delta=0.01)
        self.assertAlmostEqual(v["solicitante"], 600.0, delta=1e-9)


class TestBordas(unittest.TestCase):
    TAB14 = {12.7: (22, 19), 16: (29, 22), 19.05: (32, 26), 20: (35, 27), 22: (38, 29), 24: (42, 31),
             25.4: (44, 32), 27: (50, 38), 30: (53, 39), 36: (64, 46)}

    def test_tabela14_exata(self):
        for d, (serra, lam) in self.TAB14.items():
            self.assertAlmostEqual(L.distancia_min_borda(d * MM) / MM, serra, delta=1e-9, msg=d)
            self.assertAlmostEqual(L.distancia_min_borda(d * MM, laminada=True) / MM, lam, delta=1e-9, msg=d)

    def test_tabela14_nunca_menor_que_a_norma_por_mais_de_1mm(self):
        for d, (serra, lam) in self.TAB14.items():
            self.assertGreater(L.distancia_min_borda(d * MM) / MM, serra - 1.01, d)
            self.assertGreater(L.distancia_min_borda(d * MM, laminada=True) / MM, lam - 1.01, d)

    def test_item_borda_minima(self):
        r = L.verificar_ligacao("ligacao_tracao", dict(b=0.2, t=0.0095, d_mm=19.05, n_linhas=2, n_por_linha=3,
                                                       s=0.07, g=0.1, e1=0.04), {"N": 100})
        self.assertIn("6.3.11", _ver(r, "borda mínima")["item"])


CS = dict(h=0.23, t=0.0063, aco_chapa="A-36", d_mm=19.05, n=3, s=0.075, eh=0.04, b_solda=0.005, tw=0.0066,
          aco_alma="A-572 GR.50", e_alma=0.04, e=0.075)


class TestChapaSimples(unittest.TestCase):
    """Geometria LCHS 30-B [GERDAU] p. 94, método elástico (memória ligacoes-casos.py): V resistente por critério."""
    ESPERADO = {"parafusos: cisalhamento": 115.94, "pressão de contato na chapa": 110.80,
                "pressão de contato na alma": 130.59, "chapa: escoamento por cisalhamento": 197.59,
                "chapa: ruptura por cisalhamento": 181.83, "chapa: colapso por rasgamento": 186.62}

    def test_componentes(self):
        r = L.verificar_ligacao("chapa_simples", CS, {"V": 100})
        for nome, vrd in self.ESPERADO.items():
            self.assertAlmostEqual(_ver(r, nome)["taxa"], 100 / vrd, delta=0.01 * 100 / vrd, msg=nome)
        self.assertAlmostEqual(_ver(r, "chapa: flexão")["taxa"], 100 / 168.32, delta=1e-3)
        self.assertEqual(r["criterio"], "pressão de contato na chapa")
        self.assertLessEqual(100 / r["taxa_max"], 120.0)  # elástico ≤ Gerdau (centro instantâneo) 120 kN

    def test_solda_comprimento_total(self):
        r = L.verificar_ligacao("chapa_simples", CS, {"V": 100})
        # solda: 100 kN·hypot(1/(2·230), 75/(2·230²/6)) = 0,4777 kN/mm
        self.assertAlmostEqual(_ver(r, "solda: resistência")["solicitante"], 477.66, delta=0.01 * 477.66)


TR = dict(b=0.2, t=0.0095, aco_chapa="A-36", d_mm=19.05, n_linhas=2, n_por_linha=3, s=0.07, g=0.1, e1=0.04)


class TestTracao(unittest.TestCase):
    """Chapa 200 × 9,5 A36, 2 × 3 φ 3/4" A325 (memória ligacoes-casos.py)."""

    def test_componentes(self):
        r = L.verificar_ligacao("ligacao_tracao", TR, {"N": 400})
        self.assertAlmostEqual(_ver(r, "parafusos: cisalhamento")["resistente"] * 6, 418.03, delta=4.2)
        self.assertAlmostEqual(_ver(r, "chapa: escoamento")["resistente"], 431.82, delta=4.3)
        self.assertAlmostEqual(_ver(r, "chapa: ruptura")["resistente"], 436.01, delta=4.4)
        self.assertAlmostEqual(_ver(r, "chapa: colapso")["resistente"], 598.01, delta=6.0)
        json.dumps(r)

    def test_contato_soma_por_furo(self):
        r = L.verificar_ligacao("ligacao_tracao", TR, {"N": 400})
        v = _ver(r, "pressão de contato")
        self.assertAlmostEqual(v["taxa"], 400 / 715.58, delta=0.01 * 400 / 715.58)

    def test_ligacao_longa(self):
        d = {**TR, "n_por_linha": 6, "s": 0.3, "b": 0.3, "t": 0.02}   # comprimento 5·0,3 = 1,5 m > 1,27 m
        r = L.verificar_ligacao("ligacao_tracao", d, {"N": 600})
        self.assertAlmostEqual(_ver(r, "parafusos: cisalhamento")["solicitante"], 1.25 * 600 / 12, delta=1e-6)


if __name__ == "__main__":
    unittest.main()
