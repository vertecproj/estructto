"""Catálogo de perfis (dados/perfis/*.csv): consistência massa x área, duplicados, contagens mínimas."""
import csv
import glob
import os
import unittest

PASTA = os.path.join(os.path.dirname(__file__), "..", "prossen_estrutural", "dados", "perfis")


def _linhas():
    for f in sorted(glob.glob(os.path.join(PASTA, "*.csv"))):
        with open(f, encoding="utf-8", newline="") as fh:
            for l in csv.DictReader(fh, delimiter=";"):
                l["_arq"] = os.path.basename(f)
                yield l


ARQUIVOS = ("w_hp ipe hea heb upn l_iguais l_desiguais u_laminado t_laminado barra_chata tubo_quadrado "
            "tubo_retangular tubo_redondo tubos_vallourec u_pff ue_pff").split()
NUM = "massa h b tw tf A Ix Iy".split()


class TestCatalogo(unittest.TestCase):
    def test_estrutura(self):
        for a in ARQUIVOS:
            caminho = os.path.join(PASTA, a + ".csv")
            self.assertTrue(os.path.exists(caminho), a)
            with open(caminho, encoding="utf-8", newline="") as fh:
                linhas = list(csv.reader(fh, delimiter=";"))
            self.assertGreater(len(linhas), 1, f"{a} vazio")
            for i, l in enumerate(linhas):
                self.assertEqual(len(l), 15, f"{a} linha {i + 1}")
            cab = linhas[0]
            for l in linhas[1:]:
                d = dict(zip(cab, l))
                for c in NUM:
                    float(d[c])  # ValueError = coluna numérica inválida
                if d["r"]:
                    float(d["r"])
                self.assertTrue("http" in d["fonte"] or "sem fonte primaria" in d["fonte"]
                                or "pendente" in d["fonte"], f"{a} {d['nome']}: fonte")

    def test_massa_vs_area(self):
        for l in _linhas():
            m, A = float(l["massa"]), float(l["A"])
            ref = A * 1e-4 * 7850
            self.assertLessEqual(abs(m - ref) / ref, 0.04, f"{l['_arq']} {l['nome']}: massa {m} x A*7850 {ref:.3f}")

    def test_sem_duplicados(self):
        vistos = set()
        for l in _linhas():
            k = (l["familia"], l["nome"])
            self.assertNotIn(k, vistos, k)
            vistos.add(k)

    def test_contagens_minimas(self):
        n = {}
        for l in _linhas():
            n[l["familia"]] = n.get(l["familia"], 0) + 1
        for fam, minimo in (("W/HP", 88), ("UPN", 20), ("IPE", 18)):
            self.assertGreaterEqual(n.get(fam, 0), minimo, fam)


if __name__ == "__main__":
    unittest.main()
