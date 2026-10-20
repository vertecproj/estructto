"""Testes do relatório (ficha 010): HTML A4 e Markdown a partir de dicts (ESPEC §5 e §6.5).

Os valores esperados vêm de conversões de unidade e arredondamentos feitos à mão sobre a fixture
(ex.: 0,0094 m = 9,4 mm; taxa 1,08 → "1,08"), nunca da saída do código.
"""
import copy
import unittest
from html.parser import HTMLParser

from prossen_estrutural import relatorio

VAZIOS = {"br", "meta", "link", "img", "hr", "input", "path"}  # elementos sem fechamento / SVG autofechado


def _no(i, x, z, apoio=False):
    return {"id": i, "x": x, "y": 0.0, "z": z, "apoio": [apoio] * 6}


MODELO = {
    "versao_formato": 1, "nome": "Pórtico de 1 vão",
    "nos": {"n1": _no("n1", 0.0, 0.0, True), "n2": _no("n2", 6.0, 0.0, True), "n3": _no("n3", 0.0, 3.0),
            "n4": _no("n4", 6.0, 3.0), "n5": _no("n5", 12.0, 3.0)},
    "materiais": {"A-572 GR.50": {"nome": "A-572 GR.50", "E": 2.0e8, "G": 7.7e7, "peso": 77.0, "fy": 345000.0,
                                  "fu": 450000.0, "fck": None},
                  "C30": {"nome": "C30", "E": 2.7e7, "G": 1.1e7, "peso": 25.0, "fy": None, "fu": None, "fck": 30000.0}},
    "secoes": {"W310x38.7": {"nome": "W310x38.7", "A": 0.00497, "Iy": 8.581e-5, "Iz": 7.27e-6, "J": 1.3e-7, "props": {}},
               "R 20x50": {"nome": "R 20x50", "A": 0.1, "Iy": 2.083e-3, "Iz": 3.33e-4, "J": 1.0e-3, "props": {}}},
    "barras": {
        "b1": {"id": "b1", "ni": "n1", "nj": "n3", "secao": "W310x38.7", "material": "A-572 GR.50", "tipo": "aco"},
        "b2": {"id": "b2", "ni": "n2", "nj": "n4", "secao": "W310x38.7", "material": "A-572 GR.50", "tipo": "aco"},
        "b3": {"id": "b3", "ni": "n3", "nj": "n4", "secao": "W310x38.7", "material": "A-572 GR.50", "tipo": "aco"},
        "b4": {"id": "b4", "ni": "n4", "nj": "n5", "secao": "R 20x50", "material": "C30", "tipo": "concreto"}},
    "casos": {"G1": {"nome": "G1", "tipo": "G", "subtipo": ""}, "Q1": {"nome": "Q1", "tipo": "Q", "subtipo": "residencial"},
              "V+X": {"nome": "V+X", "tipo": "V", "subtipo": ""}},
    "cargas_nodais": [{"id": "cn1", "caso": "Q1", "no": "n3", "F": [0, 0, -10, 0, 0, 0]},
                      {"id": "auto_1", "caso": "V+X", "no": "n3", "F": [4.0, 0, 0, 0, 0, 0]}],
    "cargas_distribuidas": [{"id": "cd1", "caso": "G1", "barra": "b3", "q": [0, 0, -5]}],
    "cargas_pontuais": [{"id": "cp1", "caso": "Q1", "barra": "b3", "a": 2.5, "F": [0, 0, -20]}],
    "pavimentos": {"P1": {"nome": "P1", "z": 3.0, "diafragma": True}},
    "peso_proprio": "G1",
    "config": {"tipo_uso": "comercial", "variabilidade": "grandes", "pdelta": False, "armadura": "CA-50",
               "cobrimento_m": 0.03, "caa": "II",
               "vento": {"V0": 35.0, "S1": 1.0, "categoria": "IV", "direcoes": ["+X", "-X"]}, "sismo": None},
}

REACAO = {"max": [5.1, 0.0, 96.4, 0.0, 8.7, 0.0], "min": [-5.1, 0.0, 21.3, 0.0, -8.7, 0.0],
          "comb_max": ["ELU V+X princ."] * 6, "comb_min": ["ELU G1+Q1 princ."] * 6}

CALCULO = {
    "versao": "0.9.0", "data": "2026-10-20T05:12:00-03:00", "modelo": "Pórtico de 1 vão",
    "opcoes": {"pdelta": False, "n_pontos": 13},
    "normas": [{"norma": "NBR 8800", "edicao": "2008", "modulo": "aco.nbr8800", "limitacao": "FLT da edição 2024 não confirmada"},
               {"norma": "NBR 6118", "edicao": "2023", "modulo": "concreto.nbr6118"}],
    "combinacoes": [{"nome": "ELU G1+Q1 princ.", "estado": "ELU", "fatores": {"G1": 1.25, "Q1": 1.5}},
                    {"nome": "ELU V+X princ.", "estado": "ELU", "fatores": {"G1": 1.25, "Q1": 1.05, "V+X": 1.4}},
                    {"nome": "ELS FREQ Q1 princ.", "estado": "ELS_FREQ", "fatores": {"G1": 1.0, "Q1": 0.6}}],
    "resumo": {"n_barras": 4, "n_verificadas": 4, "n_reprovadas": 1, "taxa_max": 1.08, "barra_critica": "b3", "tempo_s": 0.84},
    # ordem de entrada propositalmente fora de ordem: a reprovada (b3) vem depois das aprovadas
    "verificacao": [
        {"barra": "b1", "tipo": "aco", "norma": "NBR 8800:2008", "taxa": 0.71, "criterio": "compressao", "item": "5.3.2",
         "ok": True, "combinacao": "ELU G1+Q1 princ.", "x_m": 0.0, "armadura": None},
        {"barra": "b2", "tipo": "aco", "norma": "NBR 8800:2008", "taxa": 0.95, "criterio": "interacao", "item": "5.5.1.2",
         "ok": True, "combinacao": "ELU V+X princ.", "x_m": 0.0, "armadura": None},
        {"barra": "b3", "tipo": "aco", "norma": "NBR 8800:2008", "taxa": 1.08, "criterio": "flexao_FLT", "item": "5.4.2",
         "ok": False, "combinacao": "ELU G1+Q1 princ.", "x_m": 3.0, "armadura": None},
        {"barra": "b4", "tipo": "concreto", "norma": "NBR 6118:2023", "taxa": 0.62, "criterio": "cisalhamento_VRd2",
         "item": "17.4.2.2", "ok": True, "combinacao": "ELU G1+Q1 princ.", "x_m": 0.0,
         "armadura": {"As_cm2": 6.3, "As2_cm2": 0.0, "Asw_s_cm2_m": 2.5, "barras": "4 ϕ 16", "As_pilar_cm2": None}}],
    "deslocamentos": {
        "verticais": [{"barra": "b3", "elemento": "viga_piso", "flecha_m": 0.0094, "limite_m": 0.0143, "razao": "L/350",
                       "taxa": 0.66, "combinacao": "ELS FREQ Q1 princ.", "item": "NBR 8800 C.1", "ok": True}],
        "horizontais": [{"pavimento": "P1", "direcao": "X", "tipo": "entre_pisos", "valor_m": 0.0031, "limite_m": 0.006,
                         "razao": "h/500", "taxa": 0.52, "combinacao": "ELS FREQ V+X princ.", "item": "NBR 8800 C.1", "ok": True}]},
    "estabilidade": {"aplicavel": True, "gama_z": {},
                     "B2": {"X": {"max": 1.08, "pavimento": "P1", "classificacao": "media", "item": "NBR 8800 D.2"}}, "avisos": []},
    "reacoes_envoltoria": {"n1": REACAO, "n2": copy.deepcopy(REACAO)},
    "avisos": [{"nivel": "aviso", "codigo": "vento_dinamico", "mensagem": "Efeito dinâmico do vento não considerado.", "ref": None},
               {"nivel": "aviso", "codigo": "aviso_norma", "mensagem": "Torção não verificada.", "ref": {"tipo": "barra", "id": "b1"},
                "barras": ["b1", "b2", "b3"]}],
}


class _Checa(HTMLParser):
    """Pilha de tags: acusa fechamento sem abertura ou tag aberta no fim (bem formado)."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.pilha, self.erros, self.abertas, self.fechadas = [], [], {}, {}

    def handle_starttag(self, tag, attrs):
        self.abertas[tag] = self.abertas.get(tag, 0) + 1
        if tag not in VAZIOS:
            self.pilha.append(tag)

    def handle_startendtag(self, tag, attrs):
        pass

    def handle_endtag(self, tag):
        self.fechadas[tag] = self.fechadas.get(tag, 0) + 1
        if tag in VAZIOS:
            return
        if not self.pilha or self.pilha[-1] != tag:
            self.erros.append(f"</{tag}> com pilha {self.pilha[-3:]}")
        else:
            self.pilha.pop()


def _secao(texto, titulo, ate=None):
    i = texto.index(titulo)
    return texto[i:texto.index(ate, i + 1)] if ate else texto[i:]


class TestRelatorio(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.h = relatorio.html(MODELO, CALCULO)
        cls.m = relatorio.markdown(MODELO, CALCULO)

    def test_aviso_de_responsabilidade(self):
        for txt in (self.h, self.m):
            self.assertIn("Ferramenta de apoio.", txt)
            self.assertIn("engenheiro responsável", txt)
            self.assertIn("ART", txt)

    def test_todas_as_barras_e_normas_com_edicao(self):
        for txt in (self.h, self.m):
            for b in MODELO["barras"]:
                self.assertIn(b, _secao(txt, "Verificação por barra", "Avisos e limitações"))
            self.assertIn("NBR 8800:2008", txt)
            self.assertIn("NBR 6118:2023", txt)

    def test_reprovada_antes_das_aprovadas(self):
        for txt in (self.h, self.m):
            sec = _secao(txt, "Verificação por barra", "Armadura das barras")
            pos = {b: min(i for i in (sec.find(f">{b}<"), sec.find(f"| {b} |")) if i >= 0) for b in ("b1", "b2", "b3", "b4")}
            self.assertLess(pos["b3"], pos["b2"])  # reprovada primeiro
            self.assertLess(pos["b2"], pos["b1"])  # depois taxa decrescente
            self.assertLess(pos["b1"], pos["b4"])

    def test_estado_em_texto_alem_da_cor(self):
        self.assertIn('<span class="estado reprovada">Reprovada</span>', self.h)
        self.assertIn('<span class="estado atencao">Atenção</span>', self.h)  # b2: 0,95
        self.assertIn('<span class="estado ok">Aprovada</span>', self.h)
        self.assertIn('<tr class="reprovada">', self.h)
        self.assertIn("**REPROVADA**", self.m)

    def test_virgula_decimal_e_casas_por_grandeza(self):
        for txt in (self.h, self.m):
            for esperado in ("1,08", "0,95", "9,4", "14,3", "96,40", "-8,70", "3,000", "1,080", "49,70"):
                self.assertIn(esperado, txt, esperado)  # A = 0,00497 m² = 49,70 cm²
        self.assertNotIn("1.08", self.m)
        self.assertNotIn("96.4", self.m)

    def test_formatacao(self):
        f = relatorio._f
        self.assertEqual(f(-0.0001, 2), "0,00")
        self.assertEqual(f(float("nan")), "—")
        self.assertEqual(f(None), "—")
        self.assertEqual(f(1234.5678, 2), "1234,57")
        self.assertEqual(relatorio._taxa(1.004, False), "1,004")  # reprovada não pode aparecer como 1,00
        self.assertEqual(relatorio._taxa(1.004, True), "1,00")

    def test_armadura_do_concreto(self):
        for txt in (self.h, self.m):
            sec = _secao(txt, "Armadura das barras de concreto")
            for esperado in ("6,30", "2,50", "4 ϕ 16"):
                self.assertIn(esperado, sec)
            self.assertNotIn("As pilar", sec)  # coluna só aparece se algum valor existir

    def test_dados_de_entrada_e_combinacoes(self):
        for txt in (self.h, self.m):
            for esperado in ("A-572 GR.50", "W 310 × 38,7", "200", "345", "comercial", "ELS FREQ Q1 princ.", "peso próprio",
                             "Fz = -10,00 kN", "a = 2,500 m", "1,25", "Frequente"):
                self.assertIn(esperado, txt, esperado)
            self.assertIn("1 carga gerada automaticamente", txt)  # auto_1 resumida, não listada
            self.assertNotIn("auto_1</td>", txt)

    def test_nome_malicioso_escapado(self):
        mod = copy.deepcopy(MODELO)
        mod["nome"] = "<script>alert(1)</script>"
        mod["secoes"]["W310x38.7"]["nome"] = "<img src=x onerror=alert(2)>"
        mod["barras"]["b1"]["secao"] = "<img src=x onerror=alert(2)>"
        calc = copy.deepcopy(CALCULO)
        calc["avisos"][0]["mensagem"] = "<b>negrito</b> | pipe"
        h, m = relatorio.html(mod, calc), relatorio.markdown(mod, calc)
        self.assertNotIn("<script>", h)
        self.assertNotIn("<img src=x", h)
        self.assertNotIn("<b>negrito", h)
        self.assertIn("&lt;script&gt;alert(1)&lt;/script&gt;", h)
        self.assertNotIn("<script>", m)
        self.assertNotRegex(m, r"(?<!\\)<[a-zA-Z/]")  # nenhum '<' sem escape
        self.assertIn("\\|", m)
        self.assertEqual(h.count("<script"), 0)

    def test_html_bem_formado(self):
        p = _Checa()
        p.feed(self.h)
        p.close()
        self.assertEqual(p.erros, [])
        self.assertEqual(p.pilha, [])
        self.assertEqual(p.abertas["table"], p.fechadas["table"])
        self.assertGreater(p.abertas["table"], 5)
        self.assertEqual(p.abertas["thead"], p.abertas["table"] - 1)  # todas, menos a tabela de layout, têm cabeçalho repetível

    def test_html_autonomo_com_css_e_logotipo(self):
        self.assertTrue(self.h.startswith("<!doctype html>"))
        self.assertIn('lang="pt-BR"', self.h)
        self.assertIn(".relatorio .folha", self.h)  # relatorio.css embutido
        # fontes embutidas em data URI (fontes/relatorio-fontes.css); url relativa de fonte não resolve num arquivo solto
        self.assertIn("@font-face", self.h)
        self.assertIn("data:", self.h)
        self.assertNotIn('url("fontes/', self.h)
        self.assertEqual(self.h.count('font-family: "Barlow";') > 0, True)
        self.assertNotIn("<link", self.h)
        self.assertGreaterEqual(self.h.count("<svg"), 2)  # logotipo no cabeçalho e marca no carimbo
        self.assertIn('<footer class="carimbo">', self.h)
        self.assertIn("PROSSEN ENGENHARIA", self.h)
        self.assertIn("CREA e nº da ART", self.h)
        self.assertIn("20/10/2026 às 05:12", self.h)
        self.assertIn("Estructto 0.9.0", self.h)

    def test_markdown_sem_html_e_tabelas_gfm(self):
        self.assertNotRegex(self.m, r"</?(table|div|span|svg|style|tr|td)\b")
        linhas = self.m.splitlines()
        seps = [i for i, l in enumerate(linhas) if l.startswith("|") and set(l) <= set("|-: ")]
        self.assertGreater(len(seps), 5)
        for i in seps:  # cabeçalho e separador com o mesmo número de colunas
            self.assertEqual(linhas[i - 1].count("|"), linhas[i].count("|"))

    def test_secoes_sem_dados_dizem_nao_aplicavel(self):
        calc = {k: v for k, v in CALCULO.items() if k not in ("estabilidade", "deslocamentos", "reacoes_envoltoria")}
        mod = {k: v for k, v in MODELO.items() if k != "pavimentos"}
        for txt in (relatorio.html(mod, calc), relatorio.markdown(mod, calc)):
            self.assertGreaterEqual(txt.count("não aplicável"), 4)
            self.assertIn("b3", txt)

    def test_entradas_vazias_nao_quebram(self):
        for txt in (relatorio.html({}, {}), relatorio.markdown({}, {}), relatorio.html(None, None)):
            self.assertIn("Ferramenta de apoio.", txt)
            self.assertIn("não aplicável", txt)
        p = _Checa()
        p.feed(relatorio.html({}, {}))
        self.assertEqual(p.erros, [])

    def test_gamaz_e_classificacao(self):
        calc = copy.deepcopy(CALCULO)
        calc["estabilidade"]["gama_z"] = {"X": {"valor": 1.08, "combinacao": "ELU V+X princ.", "classificacao": "nos_fixos",
                                                 "item": "NBR 6118 15.5.3"}}
        for txt in (relatorio.html(MODELO, calc), relatorio.markdown(MODELO, calc)):
            self.assertIn("nós fixos", txt)
            self.assertIn("média deslocabilidade", txt)
            self.assertIn("NBR 6118 15.5.3", txt)

    def test_perfis_escritos_como_na_identidade(self):
        """IDENTIDADE §5: 'W360x51' → 'W 360 × 51'; 'W200x35.9' → 'W 200 × 35,9'; nomes sem medidas ficam."""
        f = relatorio._perfil
        self.assertEqual(f("W360x51"), "W 360 × 51")
        self.assertEqual(f("W200x35.9"), "W 200 × 35,9")
        self.assertEqual(f("R 20x50"), "R 20 × 50")
        self.assertEqual(f("I 300x150x6,3x9,5"), "I 300 × 150 × 6,3 × 9,5")
        self.assertEqual(f("Ue 100x50x17x2.00"), "Ue 100 × 50 × 17 × 2,00")
        self.assertEqual(f("C30"), "C30")
        self.assertEqual(f("A-572 GR.50"), "A-572 GR.50")
        for txt in (self.h, self.m):
            self.assertIn("W 310 × 38,7", _secao(txt, "Verificação por barra", "Avisos e limitações"))
            self.assertIn("R 20 × 50", txt)
            self.assertNotIn("W310x38.7", txt)

    def test_sem_nomes_internos(self):
        """O que vai para a ART não traz nome de módulo nem código de aviso (design/revisao-telas.md, pedido ao integrador)."""
        for txt in (self.h, self.m):
            for interno in ("aco.nbr8800", "concreto.nbr6118", "aviso_norma", "vento_dinamico", "Módulo", "Código"):
                self.assertNotIn(interno, txt, interno)
            sec = _secao(txt, "Avisos e limitações")
            self.assertIn("Efeito dinâmico do vento não considerado.", sec)
            self.assertIn("barras b1, b2, b3", sec)  # aviso agrupado lista as barras

    def test_css_de_impressao_vem_do_relatorio_css(self):
        self.assertFalse(hasattr(relatorio, "_CSS_IMPRESSAO"))
        for regra in ("table.paginas", ".reserva", "print-color-adjust", "position: fixed", "@page"):
            self.assertIn(regra, self.h)
        self.assertEqual(self.h.count("table.paginas > tbody > tr, .relatorio table.paginas > tfoot > tr"), 1)  # sem duplicata

    def test_majoracao_horizontal_e_sismo_no_relatorio(self):
        calc = copy.deepcopy(CALCULO)
        calc["combinacoes"][1]["majoracao_horizontal"] = 1.1081
        calc["estabilidade"]["gama_z"] = {"X": {"valor": 1.1664, "combinacao": "ELU V+X princ.", "classificacao": "nos_moveis",
                                                 "item": "NBR 6118 15.5.3"}}
        calc["estabilidade"]["majoracao"] = {"base": "gama_z", "n_majoradas": 1, "n_excedem": 1, "fator_max": 1.1081}
        calc["estabilidade"]["por_combinacao"] = {
            "ELU V+X princ.": {"direcao": "X", "gama_z": 1.1664, "B2": None, "instavel": False, "fator": 1.1081, "situacao": "majorada"},
            "ELU V+X princ. G fav": {"direcao": "X", "gama_z": 1.4, "B2": None, "instavel": False, "situacao": "excede"}}
        calc["deslocamentos"]["sismicos"] = [{"caso": "SX", "pavimento": "P1", "direcao": "X", "tipo": "entre_pisos", "valor_m": 0.0326,
                                              "limite_m": 0.08, "razao": "0,02·h", "taxa": 0.4074, "combinacao": "SX",
                                              "item": "NBR 15421:2006 (deslocamento relativo, δx = Cd·δxe/I)", "ok": True}]
        mod = copy.deepcopy(MODELO)
        mod["config"]["sismo"] = {"zona": 2, "sistema_resistente": "portico_aco_especial"}
        for txt in (relatorio.html(mod, calc), relatorio.markdown(mod, calc)):
            comb = _secao(txt, "Combinações de ações", "Resultados")
            self.assertIn("Maj. horizontal", comb)
            self.assertIn("1,108", comb)  # 1,1081 com 3 casas
            self.assertIn("0,95·γz", comb)
            est = _secao(txt, "Estabilidade global (γz e B2)", "Verificação por barra")
            self.assertIn("majoradas por 0,95·γz", est)
            self.assertIn("1 excedem o limite", est)
            self.assertIn("excede o limite", est)
            sis = _secao(txt, "Deslocamentos relativos do sismo", "Reações de apoio")
            for esperado in ("SX (X)", "32,6", "80,0", "0,02·h", "0,41"):
                self.assertIn(esperado, sis)
        # sem a majoração, a coluna e o texto somem
        sem = relatorio.markdown(MODELO, CALCULO)
        self.assertNotIn("Maj. horizontal", sem)
        self.assertNotIn("Deslocamentos relativos do sismo", sem)

    def test_pdelta_declarado(self):
        calc = copy.deepcopy(CALCULO)
        calc["opcoes"]["pdelta"] = True
        self.assertIn("P-Δ", relatorio.markdown(MODELO, calc))
        self.assertIn("1ª ordem", self.m)


if __name__ == "__main__":
    unittest.main()
