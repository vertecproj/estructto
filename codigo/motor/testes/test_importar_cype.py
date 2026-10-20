"""Importação de obras do CYPE 3D 2016 (.ed3) — importar_cype.ler_ed3 / ler_resultados_ed3.

Arquivos reais: cópias < 1 MB em testes/dados_cype/; os maiores só rodam se existirem no caminho original
(C:\\CYPE Ingenieros\\Projetos\\CYPE 3D, somente leitura). Valores esperados vêm da leitura manual do arquivo
(hexdump, ver software/docs/formato-ed3.md) ou de grandezas gravadas pelo próprio CYPE (coordenadas e
número de nós/barras do bloco de resultados), nunca da saída do importador.
"""
import math
import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import numpy as np

from prossen_estrutural import importar_cype as ic, projeto, solver

DADOS = Path(__file__).resolve().parent / "dados_cype"
ORIGINAL = Path(r"C:\CYPE Ingenieros\Projetos\CYPE 3D")


def arquivo(nome):
    p = DADOS / nome
    return p if p.exists() else ORIGINAL / nome


G00 = ORIGINAL / "G00-MEC-002.ed3"
PIPERACK = ORIGINAL / "PIPERACK.ed3"
PEQUENOS = ["novo.ed3", "DIMENSIONAMENTO DE JUNTA.ed3", "PIPERACK TESTE 2.ed3", "PIPERACK TESTE 3.ed3",
            "PIPERACK TESTE 4.ed3"]
_CACHE = {}


def ler(p):
    if str(p) not in _CACHE:
        _CACHE[str(p)] = ic.ler_ed3(p)
    return _CACHE[str(p)]


def todos():
    out = [arquivo(n) for n in PEQUENOS]
    return out + [p for p in (G00, PIPERACK) if p.exists()]


class TestNovo(unittest.TestCase):
    """novo.ed3: 4 nós em x = 0, z = 0 e y = 0, 10, 5 e 15 (listas de coordenadas lidas à mão);
    3 barras N5-N37-N7 (+ N40); engastes (tipo 3) em todos; 2 cargas uniformes na hipótese PP."""

    def test_geometria(self):
        m = ler(arquivo("novo.ed3"))
        self.assertEqual(sorted(m.nos), ["N37", "N40", "N5", "N7"])
        self.assertEqual({n: (v.x, v.y, v.z) for n, v in m.nos.items()},
                         {"N5": (0, 0, 0), "N7": (0, 10, 0), "N37": (0, 5, 0), "N40": (0, 15, 0)})
        self.assertTrue(all(all(n.apoio) for n in m.nos.values()))
        self.assertEqual((m.barras["B9"].ni, m.barras["B9"].nj), ("N5", "N37"))
        self.assertEqual((m.barras["B38"].ni, m.barras["B38"].nj), ("N37", "N7"))
        self.assertEqual(len(m.barras), 3)

    def test_cargas_pp_em_kN(self):
        m = ler(arquivo("novo.ed3"))
        self.assertEqual(m.peso_proprio, "PP")
        self.assertEqual(len(m.cargas_distribuidas), 2)
        for c in m.cargas_distribuidas:  # 0,0021 t/m para -Z (gravado em t/m) -> kN/m
            self.assertEqual(c.caso, "PP")
            self.assertAlmostEqual(c.q[2], -0.0021 * 9.80665, places=9)

    def test_sem_resultados(self):
        r = ic.ler_resultados_ed3(arquivo("novo.ed3"))
        self.assertFalse(r["calculado"])
        self.assertFalse(r["valido"])


class TestPiperackTeste4(unittest.TestCase):
    """Pórtico no plano YZ: colunas W 200 x 22.5 (y = 0 e -3), viga com W 150 x 13 e U 101.6 x 6.3."""

    def test_contagens_e_coordenadas_iguais_ao_bloco_de_resultados(self):
        p = arquivo("PIPERACK TESTE 4.ed3")
        m, r = ler(p), ic.ler_resultados_ed3(p)
        self.assertTrue(r["valido"])
        self.assertEqual(set(m.nos), set(r["nos"]))
        self.assertEqual(set(m.barras), set(r["barras"]))
        for n, v in r["nos"].items():
            self.assertEqual((m.nos[n].x, m.nos[n].y, m.nos[n].z), tuple(v["xyz"]))
        self.assertEqual(sorted(n for n, v in m.nos.items() if all(v.apoio)), ["N18", "N5"])

    def test_perfis_do_catalogo(self):
        m = ler(arquivo("PIPERACK TESTE 4.ed3"))
        self.assertEqual(set(m.secoes), {"W 200 x 22.5", "W 150 x 13", "U 101.6 x 6.3"})
        self.assertAlmostEqual(m.secoes["W 200 x 22.5"].A, 29.0e-4, delta=0.3e-4)  # Gerdau: 29,0 cm²
        self.assertEqual(set(m.materiais), {"A-36"})  # índice 0 da tabela NBR 8800 do CYPE

    def test_coluna_vertical_girada_90(self):
        """CYPE: perfil de coluna com alma no plano do pórtico (conferido pelos deslocamentos gravados)."""
        m = ler(arquivo("PIPERACK TESTE 4.ed3"))
        self.assertAlmostEqual(m.barras["B9"].rot, math.pi / 2)
        self.assertEqual(m.barras["B15"].rot, 0.0)


class TestTodos(unittest.TestCase):
    def test_importa_valida_e_analisa(self):
        for p in todos():
            with self.subTest(arquivo=p.name):
                m = ler(p)
                self.assertGreater(len(m.barras), 0)
                for n in m.nos.values():
                    self.assertTrue(all(math.isfinite(c) and abs(c) < 1e5 for c in (n.x, n.y, n.z)))
                for b in m.barras.values():
                    a, c = m.nos[b.ni], m.nos[b.nj]
                    self.assertGreater(math.dist((a.x, a.y, a.z), (c.x, c.y, c.z)), 1e-6)
                erros = [v for v in projeto.validar(m) if v["nivel"] == "erro"]
                self.assertEqual(erros, [])
                for caso in m.casos:
                    solver.analisar(m, caso)  # sem ErroMecanismo

    def test_equilibrio(self):
        """Σ reações = − Σ cargas aplicadas (+ peso próprio A·γ·L no caso PP)."""
        for p in todos():
            m = ler(p)
            for caso in m.casos:
                with self.subTest(arquivo=p.name, caso=caso):
                    F = np.zeros(3)
                    for c in m.cargas_nodais:
                        F += np.array(c.F[:3]) * (c.caso == caso)
                    for c in m.cargas_distribuidas + m.cargas_pontuais:
                        if c.caso != caso:
                            continue
                        b = m.barras[c.barra]
                        L = math.dist(*[(m.nos[x].x, m.nos[x].y, m.nos[x].z) for x in (b.ni, b.nj)])
                        F += np.array(c.q) * L if hasattr(c, "q") else np.array(c.F)
                    if caso == m.peso_proprio:
                        for b in m.barras.values():
                            L = math.dist(*[(m.nos[x].x, m.nos[x].y, m.nos[x].z) for x in (b.ni, b.nj)])
                            F[2] -= m.secoes[b.secao].A * m.materiais[b.material].peso * L
                    res = solver.analisar(m, caso)
                    R = np.sum([v[:3] for v in res.reacoes.values()], axis=0)
                    np.testing.assert_allclose(R, -F, atol=1e-6 + 1e-6 * np.abs(F).max())

    def test_avisos_sao_texto(self):
        for p in todos():
            self.assertTrue(all(isinstance(a, str) and a for a in ler(p).avisos))


class TestLigacoesEditaveis(unittest.TestCase):
    def test_juntas_puladas_com_aviso(self):
        m = ler(arquivo("DIMENSIONAMENTO DE JUNTA.ed3"))
        self.assertEqual(len(m.nos), 3)
        self.assertTrue(any("ligação" in a for a in m.avisos))

    def test_resultado_desatualizado(self):
        r = ic.ler_resultados_ed3(arquivo("DIMENSIONAMENTO DE JUNTA.ed3"))
        self.assertTrue(r["calculado"])
        self.assertFalse(r["valido"])


@unittest.skipUnless(G00.exists(), "G00-MEC-002.ed3 (> 1 MB) só no caminho original")
class TestG00(unittest.TestCase):
    def test_hipoteses_e_cargas(self):
        m = ler(G00)
        self.assertEqual({k: c.tipo for k, c in m.casos.items()}, {"PP": "G", "CP 1": "G", "V 1": "V", "V 2": "V"})
        self.assertEqual(len(m.nos), 50)
        self.assertEqual(len(m.barras), 72)
        self.assertTrue(all(b.tipo == "pff" for b in m.barras.values() if b.secao.startswith("U")))
        r = ic.ler_resultados_ed3(G00)
        self.assertEqual(r["hipoteses"], ["PP", "CP 1", "V 1", "V 2"])
        self.assertEqual(len(r["aproveitamento"]), 72)


class TestTrechosRigidos(unittest.TestCase):
    def _confere(self, p):
        m = ler(p)
        self.assertTrue(m.barras and all(b.params.get("cortante") is True for b in m.barras.values()))
        return [b for b in m.barras.values() if b.params.get("trecho_rigido_i", 0) > 0
                or b.params.get("trecho_rigido_j", 0) > 0]

    def test_cortante_em_todas_as_barras(self):
        self._confere(arquivo("PIPERACK TESTE 4.ed3"))

    @unittest.skipUnless(PIPERACK.exists(), "PIPERACK.ed3 original ausente")
    def test_piperack_tem_trechos_rigidos(self):
        com = self._confere(PIPERACK)
        self.assertTrue(com)
        self.assertTrue(all(b.params.get("trecho_rigido_i", 0) >= 0 and b.params.get("trecho_rigido_j", 0) >= 0
                            for b in com))


class TestFormato(unittest.TestCase):
    def test_arquivo_estranho(self):
        import tempfile
        with tempfile.NamedTemporaryFile(suffix=".ed3", delete=False) as f:
            f.write(b"nao e cype")
        try:
            with self.assertRaises(ic.ErroFormato):
                ic.ler_ed3(f.name)
        finally:
            os.unlink(f.name)


class TestArquivoCorrompido(unittest.TestCase):
    """Guardas do importador (revisão 2026-10-20): tamanho, contagens, janela do resync e prazo. Arquivo ruim
    termina rápido com ErroFormato (nunca trava a interface nem vaza IndexError/struct.error)."""

    def setUp(self):
        import tempfile
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.real = (DADOS / "PIPERACK TESTE 4.ed3").read_bytes()

    def tenta(self, dados):
        import time
        p = Path(self.tmp.name) / "x.ed3"
        p.write_bytes(dados)
        t = time.monotonic()
        with self.assertRaises(ic.ErroFormato):
            ic.ler_ed3(p)
        with self.assertRaises(ic.ErroFormato):
            ic.ler_resultados_ed3(p)
        self.assertLess(time.monotonic() - t, 2.0)

    def test_truncado(self):
        for n in (0, 5, 16, 100, 0x400, 5000, len(self.real) // 2):
            with self.subTest(n=n):
                self.tenta(self.real[:n])

    def test_lixo_depois_do_cabecalho(self):
        cab = self.real[:16]
        for lixo in (os.urandom(300_000), bytes(300_000), bytes([255]) * 300_000):
            self.tenta(cab + lixo)

    def test_limite_de_tamanho_e_prazo(self):
        p = Path(self.tmp.name) / "x.ed3"
        p.write_bytes(self.real)
        antes = ic.LIMITE_BYTES
        try:
            ic.LIMITE_BYTES = 1000
            with self.assertRaises(ic.ErroFormato) as c:
                ic.ler_ed3(p)
            self.assertIn("acima", str(c.exception))
        finally:
            ic.LIMITE_BYTES = antes
        junta = DADOS / "DIMENSIONAMENTO DE JUNTA.ed3"  # usa o resync das ligações editáveis
        antes = ic.PRAZO_S
        try:
            ic.PRAZO_S = -1.0
            with self.assertRaises(ic.ErroFormato) as c:
                ic.ler_ed3(junta)
            self.assertIn("interrompida", str(c.exception))
        finally:
            ic.PRAZO_S = antes
        self.assertTrue(ic.ler_ed3(junta).barras)

    def test_pela_camada_projeto_vira_erro_de_entrada(self):
        with self.assertRaises(projeto.ErroEntrada):
            projeto.importar_cype(self.real[:16] + os.urandom(100_000))


if __name__ == "__main__":
    unittest.main()
