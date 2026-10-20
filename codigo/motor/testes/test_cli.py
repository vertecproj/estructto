"""CLI `python -m prossen_estrutural` (ficha 008): códigos de saída, arquivos e mensagens."""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from prossen_estrutural import projeto as P

RAIZ = Path(__file__).resolve().parent.parent  # software/
ENV = {**os.environ, "PYTHONIOENCODING": "utf-8"}


def rodar(*args):
    return subprocess.run([sys.executable, "-m", "prossen_estrutural", *map(str, args)], cwd=RAIZ, env=ENV,
                          capture_output=True, text=True, encoding="utf-8", timeout=120)


def modelo_temp(pasta):
    m = P.abrir_exemplo("portico_aco")
    return P.salvar(m, Path(pasta) / "meu_modelo.json")["caminho"]


class TestCLI(unittest.TestCase):
    def test_gera_arquivos_e_resumo(self):
        with tempfile.TemporaryDirectory() as t:
            r = rodar(modelo_temp(t), "-o", Path(t) / "saida" / "sub")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertEqual(r.stderr, "")
            saida = Path(t) / "saida" / "sub"
            self.assertEqual(sorted(p.name for p in saida.iterdir()), ["meu_modelo.html", "meu_modelo.json", "meu_modelo.md"])
            linhas = r.stdout.strip().splitlines()
            self.assertEqual(len(linhas), 5, r.stdout)
            self.assertIn("5 barras", linhas[0])
            self.assertIn("reprovadas", linhas[1])
            self.assertIn("Taxa máxima", linhas[2])
            self.assertIn("B2", linhas[3])
            self.assertIn("meu_modelo.html", linhas[4])
            d = json.loads((saida / "meu_modelo.json").read_text(encoding="utf-8"))
            self.assertEqual(d["resumo"]["n_barras"], 5)
            # números iguais aos da chamada direta
            direto = P.calcular(P.abrir_exemplo("portico_aco")).resumo()
            self.assertAlmostEqual(d["resumo"]["taxa_max"], direto["taxa_max"], delta=1e-12)
            self.assertIn("ART", (saida / "meu_modelo.md").read_text(encoding="utf-8"))  # aviso do ESPEC §11

    def test_formato_e_pdelta(self):
        with tempfile.TemporaryDirectory() as t:
            r = rodar(modelo_temp(t), "-o", t, "--pdelta", "--formato", "json")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertTrue((Path(t) / "meu_modelo.json").exists())
            self.assertFalse((Path(t) / "meu_modelo.html").exists())
            d = json.loads((Path(t) / "meu_modelo.json").read_text(encoding="utf-8"))
            self.assertTrue(d["opcoes"]["pdelta"])

    def test_arquivo_inexistente_codigo_1(self):
        with tempfile.TemporaryDirectory() as t:
            r = rodar(Path(t) / "nao_existe.json", "-o", Path(t) / "s")
            self.assertEqual(r.returncode, 1)
            self.assertIn("erro:", r.stderr)
            self.assertIn("não encontrado", r.stderr)
            self.assertEqual(r.stdout, "")
            self.assertFalse((Path(t) / "s").exists())

    def test_modelo_invalido_e_mecanismo_codigo_1(self):
        with tempfile.TemporaryDirectory() as t:
            ruim = Path(t) / "ruim.json"
            ruim.write_text('{"nos": {"n1": {"x": "a"}}}', encoding="utf-8")
            r = rodar(ruim, "-o", t)
            self.assertEqual(r.returncode, 1)
            self.assertIn("nos.n1.x", r.stderr)
            m = P.abrir_exemplo("portico_aco")
            for n in m.nos.values():
                n.apoio = (False,) * 6
            vazio = P.salvar(m, Path(t) / "livre.json")["caminho"]
            r = rodar(vazio, "-o", t)
            self.assertEqual(r.returncode, 1)  # sem apoio: erro de validação

    def test_uso_errado_codigo_2(self):
        self.assertEqual(rodar().returncode, 2)
        with tempfile.TemporaryDirectory() as t:
            r = rodar(modelo_temp(t), "-o", t, "--formato", "pdf")
            self.assertEqual(r.returncode, 2)
            self.assertIn("--formato", r.stderr)


if __name__ == "__main__":
    unittest.main()
