"""K2: catálogo novo no motor (secoes.catalogo, árvore, aliases dos nomes antigos)."""
import csv
import glob
import os
import unittest

from prossen_estrutural import projeto, secoes

PASTA = os.path.join(os.path.dirname(secoes.__file__), "dados", "perfis")
PORTICO = os.path.join(os.path.dirname(__file__), "..", "..", "portico")
TABELADOS = ("tubos_vallourec", "u_laminado", "upn", "l_desiguais")  # Ix/Iy da tabela prevalecem
# tabela de terceiros incoerente com a geometria (L 3" x 1/4": Ix 50,0 vs 51,8 cm4)
# W do perfis-gerdau.csv do WPF que o motor nunca teve (linha antiga Gerdau); o WPF os leva ao modelo por conta própria
SO_WPF = {"W 250 x 62,0", "W 360 x 44,0", "W 360 x 57,8"}
EXCECOES = {'L 3" x 1/4"', 'T 7/8 x 1/8"'}


def _linhas():
    for f in sorted(glob.glob(os.path.join(PASTA, "*.csv"))):
        arq = os.path.basename(f)[:-4]
        with open(f, encoding="utf-8", newline="") as fh:
            for l in csv.DictReader(fh, delimiter=";"):
                yield arq, l


class TestCatalogoSecoes(unittest.TestCase):
    def test_todo_perfil_resolve_e_ix_confere(self):
        for arq, l in _linhas():
            s = secoes.catalogo(l["nome"])
            for eixo in ("Ix", "Iy"):
                calc, tab = s.props[eixo] * 1e8, float(l[eixo])
                if arq in TABELADOS:
                    self.assertAlmostEqual(calc, tab, delta=tab * 1e-6, msg=f"{l['nome']} {eixo}")
                elif l["nome"] not in EXCECOES:  # tabelas de bitola pequena têm 1 casa: tolera 0,4 cm4
                    self.assertTrue(abs(calc - tab) <= max(0.03 * tab, 0.4), f"{arq} {l['nome']} {eixo}: {calc:.4g} x {tab}")
            self.assertAlmostEqual(s.A * 1e4, float(l["A"]), delta=float(l["A"]) * (1e-6 if arq == "tubos_vallourec" else 0.05), msg=l["nome"])

    def test_formas_novas(self):
        for nome, tipo in (('T 3/4 x 1/8"', "T"), ("L 250x90x16", "L"), ("Tubo Ø12,7x0,90", "tubo_circular"),
                           ('BC 1/2" x 1/8"', "retangular"), ("TQ 50x50x4", "tubo_retangular"), ("IPE600", "I")):
            self.assertEqual(secoes.catalogo(nome).props["tipo"], tipo, nome)

    def test_secao_de_spec_forma_T(self):
        s = secoes.secao_de_spec({"forma": "T", "h": 0.1, "b": 0.1, "tw": 0.01, "tf": 0.012})
        self.assertAlmostEqual(s.A, 0.1 * 0.012 + 0.088 * 0.01)

    def test_aliases_antigos(self):
        """Review Focus 4: nomes dos CSV antigos e do WPF (portico/perfis-*.csv) continuam resolvendo."""
        for n in ("W 200 x 26,6", "W200x26.6", "w 200x26,6", "Ue 150x60x20x2,00", "W 150 x 13,0"):
            self.assertTrue(secoes.catalogo(n).nome)
        self.assertAlmostEqual(secoes.catalogo("W 200 x 26,6").A, secoes.catalogo("W200x26.6").A)
        for f in glob.glob(os.path.join(PORTICO, "perfis-*.csv")):
            with open(f, encoding="utf-8-sig", newline="") as fh:
                for l in csv.DictReader(fh, delimiter=";"):
                    if l["nome"] not in SO_WPF:
                        secoes.catalogo(l["nome"])  # KeyError = alias quebrado

    def test_arvore(self):
        arv = projeto.catalogo_arvore()
        self.assertEqual([n["nivel1"] for n in arv], ["Gerdau (ABNT)", "Europeu (EN/DIN)", "Tubos Vallourec",
                                                      "Metalon", "Formados a frio NBR 6355"])
        total = 0
        for n in arv:
            for fam in n["familias"]:
                for sr in fam["series"]:
                    self.assertTrue(sr["perfis"])
                    for p in sr["perfis"]:
                        secoes.catalogo(p)
                        total += 1
        self.assertEqual(total, sum(1 for _ in _linhas()))
        self.assertIn("IPE", [f["nome"] for f in arv[1]["familias"]])

    def test_lista_plana_inclui_novos(self):
        nomes = {p["nome"] for p in projeto.catalogo_perfis("IPE", "IPE")}
        self.assertIn("IPE600", nomes)
        self.assertTrue(projeto.catalogo_perfis("W200"))  # lista antiga intacta


if __name__ == "__main__":
    unittest.main()
