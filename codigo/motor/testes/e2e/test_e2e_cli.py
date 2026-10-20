"""E2E da CLI (`python -m prossen_estrutural`) em subprocesso, com os 4 exemplos e entradas inválidas (ficha 023, caso 1).

Valores de referência independentes do código (cálculo manual, `exemplos/LEIAME.md` e resistência dos materiais):
- viga biapoiada W360x51, L = 6 m: g = 5 + 77·A (A = 64,80 cm²), Q = 10 kN/m, ELS frequente (NBR 8681, ψ1 = 0,6,
  uso comercial): flecha = 5·(g + 0,6·q)·L⁴/(384·E·I) com E = 200 GPa, I = 14 221 cm⁴ (Beer & Johnston, tabela
  de flechas de viga biapoiada com carga uniforme); limite L/350 (NBR 8800, anexo C).
- aço: NBR 8800:2008; concreto: NBR 6118:2023 (edições declaradas no relatório).
"""
import json
import math
import tempfile
import unittest
from pathlib import Path

from .utilitarios import (AVISO_ART, EXEMPLOS, MODELOS, NOMES_EXEMPLOS, cli_em_processo, nao_finitos, rodar_cli,
                          saida_cli)

# edição-alvo declarada (ESPEC §2) que o relatório deve citar, por exemplo
NORMAS_ESPERADAS = {
    "viga_biapoiada": {"NBR 8800": "2008"},
    "portico_aco": {"NBR 8800": "2008"},
    "galpao_aco": {"NBR 8800": "2008", "NBR 14762": None},   # a edição do 14762 não é fixada aqui
    "edificio_concreto": {"NBR 6118": "2023"},
}


class TestCliExemplos(unittest.TestCase):
    """Código 0, os 3 formatos, aviso de responsabilidade, normas com edição, todas as barras, taxas coerentes."""

    def casos(self):
        for nome in NOMES_EXEMPLOS:
            with self.subTest(exemplo=nome):
                yield nome, saida_cli(nome)

    def test_codigo_zero_e_tres_arquivos(self):
        for nome, s in self.casos():
            self.assertEqual(s["rc"], 0, s["stderr"][-600:])
            for ext in ("html", "md", "json"):
                self.assertGreater((s["pasta"] / f"{nome}.{ext}").stat().st_size, 2000, ext)
            self.assertEqual(s["stderr"].strip(), "", "a CLI não deve escrever em stderr num cálculo bem-sucedido")

    def test_resumo_no_console(self):
        for nome, s in self.casos():
            linhas = s["stdout"].splitlines()
            r = s["json"]["resumo"]
            self.assertIn(f"{r['n_barras']} barras", linhas[0])
            self.assertIn(f"Barras reprovadas: {r['n_reprovadas']} de {r['n_barras']}", s["stdout"])
            self.assertIn("Taxa máxima:", s["stdout"])
            self.assertIn(r["barra_critica"], s["stdout"])
            self.assertIn("Estabilidade global:", s["stdout"])

    def test_aviso_de_responsabilidade_em_html_e_md(self):
        for nome, s in self.casos():
            for formato in ("html", "md"):
                self.assertIn(AVISO_ART, s[formato], f"{nome}.{formato}")
                self.assertIn("ART", s[formato])
                self.assertIn("engenheiro responsável", s[formato])
            self.assertTrue(s["html"].lstrip().lower().startswith("<!doctype html"))
            self.assertIn("lang=\"pt-BR\"", s["html"])

    def test_normas_com_edicao(self):
        for nome, s in self.casos():
            normas = {n["norma"]: n for n in s["json"]["normas"]}
            self.assertTrue(normas, nome)
            for n in normas.values():
                self.assertRegex(str(n["edicao"]), r"^\d{4}$", (nome, n))
                self.assertTrue(n["modulo"])
            for norma, edicao in NORMAS_ESPERADAS[nome].items():
                self.assertIn(norma, normas, nome)
                if edicao:
                    self.assertEqual(normas[norma]["edicao"], edicao, nome)
                # o relatório mostra norma e edição juntas na tabela "Normas e edições"
                self.assertIn(f"| {norma} | {normas[norma]['edicao']} |", s["md"], nome)
            self.assertIn("Limitações declaradas", s["html"])

    def test_todas_as_barras_verificadas_e_no_relatorio(self):
        for nome, s in self.casos():
            from prossen_estrutural import projeto
            modelo = projeto.carregar(EXEMPLOS / f"{nome}.json")
            ids = set(modelo.barras)
            linhas = s["json"]["verificacao"]
            self.assertEqual({v["barra"] for v in linhas}, ids)
            self.assertEqual(len(linhas), len(ids))
            self.assertEqual(s["json"]["resumo"]["n_barras"], len(ids))
            self.assertEqual(s["json"]["resumo"]["n_verificadas"], len(ids))
            faltam_md = [b for b in ids if b not in s["md"]]
            faltam_html = [b for b in ids if b not in s["html"]]
            self.assertEqual(faltam_md, [], "barras ausentes do relatório MD")
            self.assertEqual(faltam_html, [], "barras ausentes do relatório HTML")

    def test_taxas_coerentes(self):
        for nome, s in self.casos():
            j = s["json"]
            linhas = j["verificacao"]
            for v in linhas:
                self.assertTrue(math.isfinite(v["taxa"]) and v["taxa"] >= 0, v)
                self.assertEqual(v["ok"], v["taxa"] <= 1.0, v)
                self.assertTrue(v["criterio"] and v["item"] and v["combinacao"], v)
            taxas = [v["taxa"] for v in linhas]
            self.assertAlmostEqual(j["resumo"]["taxa_max"], max(taxas), places=12)
            critica = max(linhas, key=lambda v: v["taxa"])
            self.assertEqual(j["resumo"]["barra_critica"], critica["barra"])
            self.assertEqual(j["resumo"]["n_reprovadas"], sum(1 for v in linhas if not v["ok"]))
            self.assertEqual(taxas, sorted(taxas, reverse=True), "verificação em ordem decrescente de taxa")

    def test_json_sem_nan_nem_inf(self):
        for nome, s in self.casos():
            self.assertEqual(nao_finitos(s["json"]), [], nome)  # json_estrito já recusou NaN/Infinity
            self.assertNotRegex(s["json_texto"], r"\b(NaN|Infinity)\b")

    def test_exemplos_aprovados_como_documentado(self):
        """LEIAME: os 4 exemplos entregam 0 barras reprovadas (modelos ajustados para passar)."""
        for nome, s in self.casos():
            self.assertEqual(s["json"]["resumo"]["n_reprovadas"], 0, nome)
            self.assertLessEqual(s["json"]["resumo"]["taxa_max"], 1.0, nome)

    def test_viga_flecha_por_resistencia_dos_materiais(self):
        s = saida_cli("viga_biapoiada")
        modelo = json.loads((EXEMPLOS / "viga_biapoiada.json").read_text(encoding="utf-8"))
        sec, mat = modelo["secoes"]["W360x51"], modelo["materiais"]["A-572 GR.50"]
        L, E, I = 6.0, 2.0e8, sec["Iy"]
        g = 5.0 + 77.0 * sec["A"]
        esperado = 5 * (g + 0.6 * 10.0) * L ** 4 / (384 * E * I)
        v = s["json"]["deslocamentos"]["verticais"][0]
        self.assertEqual(v["combinacao"], "ELS FREQ Q1 princ.")
        self.assertAlmostEqual(v["flecha_m"], esperado, delta=1e-4 * esperado)
        self.assertAlmostEqual(v["limite_m"], L / 350, places=12)
        self.assertEqual(v["razao"], "L/350")

    def test_pdelta_pelo_pacote(self):
        """--pdelta no pórtico plano: roda, registra a opção e B2 ≥ 1 (efeito de 2ª ordem amplifica)."""
        s = saida_cli("portico_aco", True)
        self.assertEqual(s["rc"], 0, s["stderr"][-400:])
        self.assertTrue(s["json"]["opcoes"]["pdelta"])
        self.assertGreaterEqual(s["json"]["estabilidade"]["B2"]["X"]["max"], 1.0)
        base = saida_cli("portico_aco")["json"]["resumo"]["taxa_max"]
        self.assertGreaterEqual(s["json"]["resumo"]["taxa_max"], base * (1 - 1e-9))  # 2ª ordem nunca alivia


class TestCliErros(unittest.TestCase):
    """Entrada inválida: código 1 (erro de entrada/mecanismo) ou 2 (uso errado), mensagem em stderr que orienta.
    Em processo (`cli_em_processo`) para economizar a partida do Python; um caso usa subprocesso de verdade."""

    def rodar(self, modelo, *extra):
        with tempfile.TemporaryDirectory() as t:
            return cli_em_processo(modelo, "-o", t, *extra)

    def test_codigo_de_saida_real_no_subprocesso(self):
        with tempfile.TemporaryDirectory() as t:
            r = rodar_cli(MODELOS / "viga_hipostatica.json", "-o", t)
            self.assertEqual(r.returncode, 1)
            self.assertIn("erro:", r.stderr)
            self.assertEqual(r.stdout.strip(), "")
            self.assertEqual(list(Path(t).iterdir()), [])
            self.assertEqual(rodar_cli().returncode, 2)

    def test_arquivo_inexistente(self):
        r = self.rodar(MODELOS / "nao_existe.json")
        self.assertEqual(r.returncode, 1)
        self.assertIn("erro:", r.stderr)
        self.assertIn("não encontrado", r.stderr)
        self.assertNotIn("Traceback", r.stderr)

    def test_json_malformado(self):
        with tempfile.TemporaryDirectory() as t:
            arq = Path(t) / "quebrado.json"
            arq.write_text('{"versao_formato": 1, "nos": {', encoding="utf-8")
            r = cli_em_processo(arq, "-o", Path(t) / "saida")
            self.assertEqual(r.returncode, 1)
            self.assertIn("erro:", r.stderr)
            self.assertNotIn("Traceback", r.stderr)

    def test_referencia_inexistente_aponta_barra_e_no(self):
        with tempfile.TemporaryDirectory() as t:
            d = json.loads((EXEMPLOS / "viga_biapoiada.json").read_text(encoding="utf-8"))
            d["barras"]["b1"]["nj"] = "n9"
            arq = Path(t) / "ref.json"
            arq.write_text(json.dumps(d), encoding="utf-8")
            r = cli_em_processo(arq, "-o", Path(t) / "saida")
            self.assertEqual(r.returncode, 1)
            self.assertIn("b1", r.stderr)
            self.assertIn("n9", r.stderr)  # diz qual barra e qual nó
            self.assertFalse((Path(t) / "saida").exists() and list((Path(t) / "saida").iterdir()), "nada gravado")

    def test_somente_nos(self):
        r = self.rodar(MODELOS / "somente_nos.json")
        self.assertEqual(r.returncode, 1)
        self.assertIn("sem_barras", r.stderr)
        self.assertIn("sem_casos", r.stderr)  # todos os erros de uma vez, não um por vez

    def test_modelos_hipostaticos_dao_mecanismo(self):
        for arq in ("portico_rotula_dupla.json", "viga_hipostatica.json"):
            with self.subTest(modelo=arq):
                r = self.rodar(MODELOS / arq)
                self.assertEqual(r.returncode, 1)
                self.assertIn("mecanismo", r.stderr)
                self.assertRegex(r.stderr, r"suspeito: nó \w+, GL \w+")  # aponta onde olhar
                self.assertNotIn("Traceback", r.stderr)

    def test_pdelta_acima_da_critica_erro_claro(self):
        with tempfile.TemporaryDirectory() as t:
            r = cli_em_processo(MODELOS / "pdelta_acima_da_critica.json", "-o", t, "--pdelta")
            self.assertEqual(r.returncode, 1)
            self.assertIn("carga acima da crítica", r.stderr)
            self.assertIn("P-Δ", r.stderr)
            self.assertNotIn("Traceback", r.stderr)
            self.assertEqual(list(Path(t).iterdir()), [])
            # sem --pdelta o mesmo modelo calcula (1ª ordem) e a barra é reprovada, não um erro
            r1 = cli_em_processo(MODELOS / "pdelta_acima_da_critica.json", "-o", t, "--formato", "json")
            self.assertEqual(r1.returncode, 0, r1.stderr)
            j = json.loads((Path(t) / "pdelta_acima_da_critica.json").read_text(encoding="utf-8"))
            self.assertGreater(j["resumo"]["taxa_max"], 1.0)  # 5000 kN num W200x35.9: reprovada em 1ª ordem

    def test_uso_errado_codigo_2(self):
        with tempfile.TemporaryDirectory() as t:
            self.assertEqual(cli_em_processo().returncode, 2)  # sem argumentos
            r = cli_em_processo(EXEMPLOS / "viga_biapoiada.json", "-o", t, "--formato", "docx")
            self.assertEqual(r.returncode, 2)
            self.assertIn("--formato", r.stderr)
            self.assertEqual(cli_em_processo(EXEMPLOS / "viga_biapoiada.json").returncode, 2)  # falta -o

    def test_so_json_pelo_formato(self):
        with tempfile.TemporaryDirectory() as t:
            r = cli_em_processo(EXEMPLOS / "viga_biapoiada.json", "-o", t, "--formato", "json")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertEqual(sorted(p.name for p in Path(t).iterdir()), ["viga_biapoiada.json"])


class TestCliBordas(unittest.TestCase):
    """Modelos de borda de `modelos/`: valores de solução fechada (resistência dos materiais)."""

    def calcular(self, arq):
        with tempfile.TemporaryDirectory() as t:
            r = rodar_cli(MODELOS / arq, "-o", t, "--formato", "json")
            self.assertEqual(r.returncode, 0, r.stderr)
            return json.loads((Path(t) / (Path(arq).stem + ".json")).read_text(encoding="utf-8"),
                              parse_constant=lambda c: self.fail(f"constante {c} no JSON"))

    def test_barra_vertical_unica_e_trelica_calculam(self):
        for arq in ("barra_vertical_unica.json", "trelica_pura.json", "barra_com_rot.json"):
            with self.subTest(modelo=arq):
                j = self.calcular(arq)
                self.assertEqual(nao_finitos(j), [])
                self.assertEqual(j["resumo"]["n_verificadas"], j["resumo"]["n_barras"])
                self.assertEqual(len(j["verificacao"]), j["resumo"]["n_barras"])

    def test_reacao_da_barra_vertical_unica(self):
        """Balanço de 3 m: Q1 = (5 kN em +X, 50 kN para baixo) → Rx = −5, Rz = 50, My(base) = −5·3 = −15 kN·m,
        verificado na envoltória da combinação mais desfavorável: |R| = 1,4·(reação de G1 + de Q1)."""
        j = self.calcular("barra_vertical_unica.json")
        r = j["reacoes_envoltoria"]["n1"]
        peso = 77.0 * 0.0045715607346410204 * 3.0   # γ·A·L (A do catálogo, W200x35.9)
        # ELU: 1,4 (G1) + 1,4 (Q1) → Rz = 1,4·(peso + 50); Rx = 1,4·(−5); My = 1,4·(−15)
        self.assertAlmostEqual(max(r["max"][2], r["min"][2]), 1.4 * (peso + 50.0), delta=1e-6)
        self.assertAlmostEqual(min(r["max"][0], r["min"][0]), -1.4 * 5.0, delta=1e-6)
        self.assertAlmostEqual(min(r["max"][4], r["min"][4]), -1.4 * 15.0, delta=1e-6)


if __name__ == "__main__":
    unittest.main()
