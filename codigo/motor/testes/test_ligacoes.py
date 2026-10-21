"""Testes de aco/ligacoes.py (NBR 8800:2008, cap. 6). Tolerância 1 %.

Fonte principal: Instituto Aço Brasil/CBCA, "Ligações em estruturas metálicas", vol. 2,
4ª ed. rev., A. L. Vasconcellos (rev.), Rio de Janeiro, 2011 (série Manual de Construção em
Aço), exemplos 5.1 (ligação de peça tracionada, p. 10–17) e 5.2 (p. 17–21), ambos pela
NBR 8800:2008. Unidades do livro: kN e cm; aqui convertidas para kN e m.
Tração e combinação: "Ligações parafusadas", Curso de Projeto e Cálculo de Estruturas
Metálicas (calculistadeaco.com.br, 2021), exercício 03 — conferido também por fórmula fechada.
"""
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural.aco import ligacoes as lg  # noqa: E402


def rel(a, b):
    return abs(a - b) / abs(b)


def linha(res, prefixo):
    achados = [v for v in res["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"verificação '{prefixo}' ausente: {[v['nome'] for v in res['verificacoes']]}"
    return achados[0]


class TestComponentes(unittest.TestCase):
    def test_cisalhamento_parafuso_cbca_5_1_4(self):
        # CBCA vol. 2, 5.1.4(a), p. 12–13: A325 d = 19 mm, rosca no plano, Ab = 2,84 cm²,
        # fub = 82,5 kN/cm² → 0,4·2,84·82,5/1,35 = 69 kN por plano; 2 planos = 138 kN.
        r = lg.cisalhamento_parafuso(0.019, 825e3, rosca_no_plano=True, alta_resistencia=True)
        self.assertLess(rel(r, 69.0), 0.01)

    def test_cisalhamento_parafuso_cbca_5_2_3(self):
        # CBCA vol. 2, 5.2.3(a), p. 20: A325 d = 7/8" (22,225 mm), rosca no plano: Fv,Rd = 94,8 kN.
        self.assertLess(rel(lg.cisalhamento_parafuso(0.022225, 825e3, True, True), 94.8), 0.01)

    def test_coeficiente_cisalhamento(self):
        # NBR 8800 6.3.3.2: 0,5 fora da rosca só para alta resistência; comum sempre 0,4.
        a = lg.cisalhamento_parafuso(0.019, 825e3, False, True)
        b = lg.cisalhamento_parafuso(0.019, 825e3, True, True)
        c = lg.cisalhamento_parafuso(0.019, 415e3, False, False)
        self.assertAlmostEqual(a / b, 1.25)
        self.assertAlmostEqual(c, 0.4 * math.pi * 0.019 ** 2 / 4 * 415e3 / 1.35)

    def test_pressao_contato_cbca_5_1_4(self):
        # CBCA vol. 2, 5.1.4(b), p. 13: gusset t = 8 mm, A36 (fu = 40 kN/cm²), d = 19 mm,
        # furo 20,5 mm, deformação não limitada (1,5 / 3,0):
        # entre furos lf = 70 − 20,5 = 49,5 mm → limitado a 3,0·1,9·0,8·40/1,35 = 135 kN;
        # furo-borda lf = 45 − 10,25 = 34,75 mm → 1,5·3,475·0,8·40/1,35 = 124 kN.
        self.assertLess(rel(lg.pressao_contato(0.0495, 0.008, 0.019, 400e3, False), 135.0), 0.01)
        self.assertLess(rel(lg.pressao_contato(0.03475, 0.008, 0.019, 400e3, False), 124.0), 0.01)

    def test_pressao_contato_deformacao_limitada(self):
        # NBR 8800 6.3.3.3 a): 1,2·lf·t·fu/γa2 ≤ 2,4·db·t·fu/γa2 (fórmula fechada)
        # lf = 30 mm, t = 10 mm, fu = 400 MPa: 1,2·0,03·0,01·400000/1,35 = 106,67 kN;
        # teto 2,4·0,019·0,01·400000/1,35 = 135,1 kN → 106,67. lf = 60 mm → teto 135,1.
        self.assertLess(rel(lg.pressao_contato(0.03, 0.01, 0.019, 400e3), 106.67), 0.001)
        self.assertLess(rel(lg.pressao_contato(0.06, 0.01, 0.019, 400e3), 135.11), 0.001)

    def test_tracao_e_combinacao(self):
        # Curso calculistadeaco, ex. 03: A325 d = 12 mm (Ab = 1,13 cm²):
        # Ft,Rd = 0,75·1,13·82,5/1,35 = 51,79 kN; Fv,Rd = 0,4·1,13·82,5/1,35 = 27,62 kN;
        # (71,75/51,79)² + (2/27,62)² = 1,93 (NBR 8800 6.3.3.4).
        ft = lg.tracao_parafuso(0.012, 825e3)
        fv = lg.cisalhamento_parafuso(0.012, 825e3, True, True)
        self.assertLess(rel(ft, 51.79), 0.01)
        self.assertLess(rel(fv, 27.62), 0.01)
        self.assertLess(rel(lg.interacao_tracao_cisalhamento(71.75, ft, 2.0, fv), 1.93), 0.01)

    def test_solda_filete_cbca_5_1_6(self):
        # CBCA vol. 2, 5.1.6, p. 15–16: filete 5 mm, E70 (fw = 48,5 kN/cm²), A36.
        # metal base: 0,6·0,5·25/1,10 = 6,82 kN/cm; metal da solda: 0,6·0,5·0,707·48,5/1,35 = 7,62 kN/cm.
        r = lg.resistencia_filete(0.005, 485e3, 250e3, 400e3)
        self.assertLess(rel(r["metal_base"], 682.0), 0.01)   # kN/m
        self.assertLess(rel(r["metal_solda"], 762.0), 0.01)
        self.assertLess(rel(r["FwRd"], 682.0), 0.01)

    def test_solda_dimensao_minima_tabela10(self):
        # NBR 8800 tabela 10 (CBCA vol. 2 p. 15, 39, 60): chapa 6,3 mm → 3 mm; 8 mm → 5; 12,5 mm → 5
        self.assertAlmostEqual(lg.filete_minimo(0.0063), 0.003)
        self.assertAlmostEqual(lg.filete_minimo(0.008), 0.005)
        self.assertAlmostEqual(lg.filete_minimo(0.0125), 0.005)
        # máximo ao longo de borda: t − 1,5 mm (12,5 → 11 mm, CBCA p. 39)
        self.assertAlmostEqual(lg.filete_maximo(0.0125), 0.011)

    def test_colapso_rasgamento_cbca_5_1_7(self):
        # CBCA vol. 2, 5.1.7, p. 16–17 (NBR 8800 6.5.6), A36, Cts = 1:
        # cantoneira: Agv = 9,08, Anv = 6,64, Ant = 1,71 cm² → Fr,Rd = 151,5 kN;
        # gusset:     Agv = 9,20, Anv = 6,73, Ant = 7,17 cm² → Fr,Rd = 314,7 kN.
        self.assertLess(rel(lg.colapso_rasgamento(9.08e-4, 6.64e-4, 1.71e-4, 250e3, 400e3), 151.5), 0.01)
        self.assertLess(rel(lg.colapso_rasgamento(9.20e-4, 6.73e-4, 7.17e-4, 250e3, 400e3), 314.7), 0.01)

    def test_distancia_borda(self):
        # CBCA vol. 2 p. 13 (tabela 14 da NBR 8800): d = 19 mm, borda cortada com serra → 32 mm
        self.assertLess(rel(lg.distancia_min_borda(0.019), 0.032), 0.01)
        self.assertLess(lg.distancia_min_borda(0.019, laminada=True), lg.distancia_min_borda(0.019))


class TestTipologias(unittest.TestCase):
    def test_ligacao_tracao_cbca_5_1(self):
        """CBCA vol. 2, ex. 5.1 (p. 10–17): chapa de gusset 200 × 8 mm, A36, 2 parafusos A325
        d = 19 mm numa linha (s = 70 mm, e1 = 45 mm), corte duplo (cantoneiras dos dois lados),
        rosca no plano, deformação não limitada; N = 200 kN.
        Livro: escoamento 363 kN (p. 13); corte 138 kN/paraf.; contato 124 kN (furo-borda) ×
        100 kN/paraf. (p. 13); colapso por rasgamento 314,7 kN (p. 17, furo 2,06 cm; aqui
        dh + 2 mm = 2,25 cm, 5.2.4.1 → 312,6 kN, 0,7 %); An = 14,2 cm² (p. 12)."""
        dados = dict(b=0.20, t=0.008, aco_chapa="A-36", d_mm=19, tipo_parafuso="A325",
                     n_linhas=1, n_por_linha=2, s=0.070, e1=0.045, planos_corte=2,
                     rosca_no_plano=True, deformacao_limitada=False)
        r = lg.verificar_ligacao("ligacao_tracao", dados, {"N": 200})
        self.assertLess(rel(linha(r, "chapa: escoamento")["resistente"], 363.6), 0.01)
        self.assertLess(rel(linha(r, "parafusos: cisalhamento")["resistente"], 138.0), 0.01)
        self.assertLess(rel(linha(r, "parafusos: cisalhamento")["solicitante"], 100.0), 1e-9)
        # 6.3.3.3 soma por furo (VC-ligacoes-002): borda 1,5·3,475·0,8·40/1,35 = 123,6 (livro 124) + interno
        # min(1,5·4,95; 3,0·1,9)·0,8·40/1,35 = 135,1 → 258,7 kN contra N = 200 kN
        self.assertLess(rel(linha(r, "pressão de contato")["resistente"], 258.7), 0.01)
        self.assertLess(rel(linha(r, "chapa: colapso por rasgamento")["resistente"], 314.7), 0.01)
        # disposições: 6.3.9 mínimo 2,7db = 51,3 ≤ 70 (o livro confere o preferível 3db = 57); borda 32 ≤ 45;
        # borda lateral 100 mm > 12t = 96 mm (6.3.12), que o livro não verificou no gusset — a única "falha"
        # do exemplo: reprova sem definir taxa_max
        self.assertLess(rel(linha(r, "espaçamento mínimo")["taxa"], 2.7 * 0.019 / 0.070), 0.001)
        self.assertLess(rel(linha(r, "borda máxima (lateral)")["taxa"], 0.100 / 0.096), 0.001)
        self.assertFalse(r["ok"])
        self.assertTrue(r["criterio"].startswith("pressão de contato"))   # contato governa as resistências
        self.assertLess(rel(r["taxa_max"], 200.0 / 258.7), 0.01)

    def test_chapa_simples_manual(self):
        """Cálculo manual (NBR 8800:2008), V = 120 kN; chapa 230 × 8 mm A36; 3 parafusos A325
        d = 19 mm (furo 20,5; líquido 22,5 mm), s = 75 mm, ev = (230 − 150)/2 = 40 mm,
        eh = 40 mm; e = 70 mm; alma tw = 6,3 mm A36, borda 40 mm; filete 6 mm E70 dos dois lados.
        Parafusos (6.3.3.2, método elástico): Fv = 120/3 = 40; Fh = V·e·s/Σy² = 120·0,07·0,075/(2·0,075²)
          = 56,0 kN; R = √(40² + 56²) = 68,82 kN; Fv,Rd = 0,4·2,835·82,5/1,35 = 69,31 kN → 0,993.
        Contato (6.3.3.3 a): lf = min(75 − 20,5; 40 − 10,25) = 29,75 mm;
          chapa 1,2·2,975·0,8·40/1,35 = 84,62 kN; alma 1,2·2,975·0,63·40/1,35 = 66,64 kN.
        Cisalhamento da chapa (6.5.5): 0,6·25·23·0,8/1,10 = 250,9 kN; 0,6·40·(23 − 3·2,25)·0,8/1,35 = 231,1 kN.
        Rasgamento (6.5.6): Agv = 19·0,8 = 15,2; Anv = (19 − 2,5·2,25)·0,8 = 10,7; Ant = (4 − 1,125)·0,8 = 2,3 cm²
          → min(0,6·40·10,7 + 40·2,3; 0,6·25·15,2 + 40·2,3)/1,35 = 237,0 kN.
        Flexão da chapa (elástica): W = 0,8·23²/6 = 70,53 cm³ → 70,53·25/1,10 = 16,03 kN·m; MSd = 8,4 kN·m.
        Solda (6.2.2.2 c: comprimento total): Lw = 230 mm por lado; v = 120/(2·0,230) = 260,9 kN/m;
          f = 6·8,4/(2·0,230²) = 476,4 kN/m; r = 543,1 kN/m; metal base 0,6·0,006·250000/1,10 = 818,2 kN/m."""
        dados = dict(h=0.23, t=0.008, aco_chapa="A-36", d_mm=19, tipo_parafuso="A325", n=3,
                     s=0.075, eh=0.040, rosca_no_plano=True, b_solda=0.006, eletrodo="E70",
                     tw=0.0063, aco_alma="A-36", e_alma=0.040, e=0.070)
        r = lg.verificar_ligacao("chapa_simples", dados, {"V": 120})
        p = linha(r, "parafusos: cisalhamento")
        self.assertLess(rel(p["solicitante"], 68.82), 0.001)
        self.assertLess(rel(p["resistente"], 69.31), 0.001)
        self.assertLess(rel(linha(r, "pressão de contato na chapa")["resistente"], 84.62), 0.001)
        self.assertLess(rel(linha(r, "pressão de contato na alma")["resistente"], 66.64), 0.001)
        self.assertLess(rel(linha(r, "chapa: escoamento por cisalhamento")["resistente"], 250.9), 0.001)
        self.assertLess(rel(linha(r, "chapa: ruptura por cisalhamento")["resistente"], 231.1), 0.001)
        self.assertLess(rel(linha(r, "chapa: colapso por rasgamento")["resistente"], 237.0), 0.001)
        self.assertLess(rel(linha(r, "chapa: flexão")["resistente"], 16.03), 0.001)
        s = linha(r, "solda: resistência")
        self.assertLess(rel(s["solicitante"], 543.1), 0.001)
        self.assertLess(rel(s["resistente"], 818.2), 0.001)
        self.assertEqual(r["criterio"], "pressão de contato na alma")
        self.assertLess(rel(r["taxa_max"], 68.82 / 66.64), 0.001)
        self.assertFalse(r["ok"])

    def test_parafuso_isolado(self):
        dados = dict(d_mm=19, tipo_parafuso="A325", planos_corte=2, rosca_no_plano=True,
                     t=0.008, aco_chapa="A-36", lf=0.03475, deformacao_limitada=False)
        r = lg.verificar_ligacao("parafuso", dados, {"Fv": 100})
        self.assertLess(rel(linha(r, "cisalhamento")["resistente"], 138.0), 0.01)
        self.assertLess(rel(linha(r, "pressão de contato")["resistente"], 124.0), 0.01)
        self.assertTrue(r["ok"])

    def test_solda_isolada(self):
        # CBCA 5.1.6: dois filetes de 200 mm, 200 kN → 5 kN/cm < 6,8 kN/cm
        dados = dict(b=0.005, L=0.40, eletrodo="E70", aco_base="A-36", t_base=0.008)
        r = lg.verificar_ligacao("solda_filete", dados, {"F": 200})
        self.assertLess(rel(linha(r, "solda: resistência")["taxa"], 5.0 / 6.82), 0.01)
        self.assertAlmostEqual(linha(r, "solda: dimensão mínima")["taxa"], 1.0)  # 5 mm = mínimo p/ 8 mm
        self.assertTrue(r["ok"])
        # VC-ligacoes-005: a dimensão mínima (taxa 1,0) não esconde a taxa de resistência (5/6,82 = 0,733)
        self.assertTrue(r["criterio"].startswith("solda: resistência"))
        # 6.2.6.2.3: comprimento mínimo 4b = 20 mm e ≥ 40 mm → L = 30 mm reprova
        r = lg.verificar_ligacao("solda_filete", dict(dados, L=0.030), {"F": 10})
        self.assertAlmostEqual(linha(r, "solda: comprimento mínimo")["taxa"], 0.040 / 0.030)
        self.assertFalse(r["ok"])

    def test_espacamento_maximo(self):
        # 6.3.10: 24t ≤ 300 mm; chapa 8 mm → 192 mm; s = 200 mm reprova
        dados = dict(b=0.20, t=0.008, d_mm=19, n_linhas=1, n_por_linha=2, s=0.200, e1=0.045)
        r = lg.verificar_ligacao("ligacao_tracao", dados, {"N": 50})
        self.assertAlmostEqual(linha(r, "espaçamento máximo")["taxa"], 0.200 / 0.192)
        self.assertFalse(r["ok"])
        # LCA-012 — 6.3.10, peça sujeita à corrosão atmosférica (aço resistente à corrosão sem pintura):
        # 14t ≤ 180 mm → 112 mm; s = 100 mm passa no limite geral e reprova aqui (taxa 100/112)
        r = lg.verificar_ligacao("ligacao_tracao", dict(dados, s=0.100), {"N": 50})
        self.assertAlmostEqual(linha(r, "espaçamento máximo")["taxa"], 0.100 / 0.192)
        r = lg.verificar_ligacao("ligacao_tracao", dict(dados, s=0.100, sujeito_corrosao=True), {"N": 50})
        self.assertAlmostEqual(linha(r, "espaçamento máximo")["taxa"], 0.100 / 0.112)

    def test_espacamento_minimo_2_7db(self):
        # LCA-011 — 6.3.9: s ≥ 2,7db (de preferência 3db). d = 19 mm: s = 55 mm ≥ 51,3 mm passa, com aviso
        dados = dict(b=0.20, t=0.008, d_mm=19, n_linhas=1, n_por_linha=2, s=0.055, e1=0.045)
        r = lg.verificar_ligacao("ligacao_tracao", dados, {"N": 50})
        self.assertAlmostEqual(linha(r, "espaçamento mínimo")["taxa"], 2.7 * 0.019 / 0.055)
        self.assertTrue(any("3db" in a for a in r["avisos"]))
        r = lg.verificar_ligacao("ligacao_tracao", dict(dados, s=0.050), {"N": 50})
        self.assertFalse(r["ok"])


class TestContrato(unittest.TestCase):
    def test_tipos(self):
        tipos = {t["tipo"]: t for t in lg.tipos_ligacao()}
        self.assertLessEqual({"parafuso", "solda_filete", "chapa_simples", "ligacao_tracao"} | set(lg.tipos_contrato()),
                             set(tipos))
        for t in tipos.values():
            self.assertTrue(t["descricao"])
            for c in t["campos"]:
                self.assertTrue({"nome", "unidade", "descricao", "padrao"} <= set(c))
            for e in t["esforcos"]:
                self.assertEqual({"nome", "unidade"}, set(e))
        json.dumps(lg.tipos_ligacao())

    def test_campos_faltando(self):
        with self.assertRaises(ValueError) as cm:
            lg.verificar_ligacao("chapa_simples", {"t": 0.008}, {})
        msg = str(cm.exception)
        for nome in ("h", "d_mm", "n", "s", "tw", "e", "V"):
            self.assertIn(nome, msg)

    def test_fora_de_faixa_e_tipo(self):
        with self.assertRaises(ValueError) as cm:
            lg.verificar_ligacao("parafuso", dict(d_mm=500, tipo_parafuso="X", t=0.008, lf=0.03), {"Fv": -1})
        msg = str(cm.exception)
        for nome in ("d_mm", "tipo_parafuso", "Fv"):
            self.assertIn(nome, msg)
        with self.assertRaises(ValueError):
            lg.verificar_ligacao("inexistente", {}, {})

    def test_json(self):
        casos = [
            ("parafuso", dict(d_mm=16, tipo_parafuso="A307", t=0.0063, lf=0.03), {"Ft": 10, "Fv": 10}),
            ("solda_filete", dict(b=0.005, L=0.1, t_base=0.008), {"F": 50}),
            ("chapa_simples", dict(h=0.23, t=0.008, d_mm=19, n=3, s=0.075, tw=0.0063, e=0.07), {"V": 50}),
            ("ligacao_tracao", dict(b=0.2, t=0.008, d_mm=19, n_linhas=2, n_por_linha=3, s=0.07, g=0.1, e1=0.04),
             {"N": 300}),
        ]
        for tipo, d, e in casos:
            r = lg.verificar_ligacao(tipo, d, e)
            json.dumps(r)
            self.assertEqual({"taxa_max", "criterio", "item", "ok", "verificacoes", "avisos", "geometria"}, set(r))
            for v in r["verificacoes"]:
                base = {"nome", "solicitante", "resistente", "unidade", "taxa", "item"}
                self.assertTrue(base <= set(v) <= base | {"sentido", "parametro"}, set(v))   # dicas do otimizador

    def test_edicao_cobertura(self):
        self.assertTrue(lg.EDICAO.startswith("NBR 8800"))
        for c in lg.COBERTURA:
            self.assertEqual({"norma", "edicao", "item", "verificacao", "limitacao"}, set(c))
        self.assertTrue(any("alavanca" in c["limitacao"] for c in lg.COBERTURA))


class TestPegaEnchimento(unittest.TestCase):
    """LCA-014. NBR 8800:2008 6.3.7: pega > 5db → Fv,Rd reduzida em 1 % a cada 1,5 mm adicionais (adotado:
    cada fração conta, a favor); 6.5.7.2 a): chapas de enchimento com ts > 6 mm (≤ 19 mm) → Fv,Rd e Fc,Rd
    × [1 − 0,0154(ts − 6)], ts em mm; ts > 19 mm exige 6.5.7.2 b)/c) (não verificadas → reprova).
    A325 d = 20 mm, rosca no plano, 1 plano: Fv,Rd = 0,4·π·20²/4·825/1,35 = 76,79 kN;
    chapa A-36 t = 16 mm, lf = 50 mm: Fc,Rd = min(1,2·50; 2,4·20)·16·400/1,35 = 227,56 kN."""

    BASE = dict(d_mm=20, tipo_parafuso="A325", t=0.016, lf=0.050)

    def ver(self, **k):
        return lg.verificar_ligacao("parafuso", dict(self.BASE, **k), dict(Fv=50.0))

    def test_sem_pega_igual(self):
        self.assertLess(rel(linha(self.ver(), "cisalhamento")["resistente"], 76.79), 0.001)
        self.assertLess(rel(linha(self.ver(pega=0.100), "cisalhamento")["resistente"], 76.79), 0.001)

    def test_pega_longa(self):
        # pega 130 mm: 30 mm além de 5db = 100 mm → 20 × 1 % → 0,80 · 76,79 = 61,44 kN
        self.assertLess(rel(linha(self.ver(pega=0.130), "cisalhamento")["resistente"], 61.44), 0.001)
        # 101 mm: 1 mm além → uma parcela de 1,5 mm (a favor) → 0,99
        self.assertLess(rel(linha(self.ver(pega=0.101), "cisalhamento")["resistente"], 0.99 * 76.79), 0.001)

    def test_enchimento(self):
        # ts = 10 mm: 1 − 0,0154·4 = 0,9384 → Fv,Rd = 0,80·0,9384·76,79 = 57,65 kN; Fc,Rd = 0,9384·227,56 = 213,54
        r = self.ver(pega=0.130, t_enchimento=0.010)
        self.assertLess(rel(linha(r, "cisalhamento")["resistente"], 57.65), 0.001)
        self.assertLess(rel(linha(r, "pressão")["resistente"], 213.54), 0.001)
        self.assertTrue(r["ok"])
        r = self.ver(t_enchimento=0.006)
        self.assertLess(rel(linha(r, "cisalhamento")["resistente"], 76.79), 0.001)
        r = self.ver(t_enchimento=0.020)   # > 19 mm: só com 6.5.7.2 b) ou c)
        self.assertFalse(r["ok"])
        self.assertGreater(linha(r, "chapas de enchimento")["taxa"], 1.0)
        # tipologias com parafusos também aplicam os fatores
        d = dict(b=0.2, t=0.0125, d_mm=20, n_linhas=2, n_por_linha=3, s=0.07, g=0.1, e1=0.04)
        r0 = lg.verificar_ligacao("ligacao_tracao", d, dict(N=300))
        r = lg.verificar_ligacao("ligacao_tracao", dict(d, pega=0.130), dict(N=300))
        self.assertLess(rel(linha(r, "parafusos: cisalhamento")["resistente"],
                            0.80 * linha(r0, "parafusos: cisalhamento")["resistente"]), 1e-9)


if __name__ == "__main__":
    unittest.main()
