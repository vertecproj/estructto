"""Frente C3: toda peça desenhada (chapa, cantoneira, parafuso, porca, solda) do contrato traz `campo` = campo editável
que existe no esquema do tipo (docs/v3/LIGACOES-EDITOR.md); otimizador sem solução traz `campos_limite`/`campo_limitante`."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L

CHAVES = ("chapas", "cantoneiras", "parafusos", "porcas", "soldas")


def _tipos():
    ts = P.tipos_ligacao()
    return ts if isinstance(ts, list) else ts["tipos"]


def _conferir(testcase, tipo, dados, esf):
    nomes = {c["nome"] for c in L.esquema(tipo)}
    g = P.verificar_ligacao(tipo, dados, esf)["geometria"]
    n = 0
    for k in CHAVES:
        for x in g.get(k, []):
            n += 1
            with testcase.subTest(tipo=tipo, peca=k, nome=x.get("nome") or x.get("perfil")):
                testcase.assertIn(x.get("campo"), nomes)
    return n


def _barra2():   # 2ª barra do nó = cópia da 1ª, soldada
    ex = {t["tipo"]: t["exemplo"] for t in _tipos()}["no_trelica_gusset"]["dados"]
    d = {k[:-1] + "2": v for k, v in ex.items() if k.endswith("_1")}
    return dict(d, ligacao_2="soldada", b_solda_2=6.0)


class TestCampos(unittest.TestCase):
    def test_todo_tipo_aponta_cada_peca_para_um_campo_real(self):
        total = 0
        for t in _tipos():
            total += _conferir(self, t["tipo"], t["exemplo"]["dados"], t["exemplo"].get("esforcos", {}))
        self.assertGreater(total, 100)

    def test_variantes_soldada_enrijecida_e_multibarra(self):
        base = {t["tipo"]: t["exemplo"] for t in _tipos()}
        for tipo, mud in (("gusset_contraventamento", dict(ligacao="soldada")),
                          ("cantoneira_simples_trelica", dict(ligacao="soldada")),
                          ("chapa_topo_estendida", dict(enrijecedor_chapa=True, t_enrijecedor=9.5)),
                          ("no_trelica_gusset", _barra2()),
                          ("apoio_cantoneira", dict(assento="enrijecido"))):
            ex = base[tipo]
            _conferir(self, tipo, dict(ex["dados"], **mud), ex.get("esforcos", {}))

    def test_solda_do_no_trelica_aponta_para_a_barra_dela(self):
        ex = {t["tipo"]: t["exemplo"] for t in _tipos()}["no_trelica_gusset"]
        d = dict(ex["dados"], **_barra2())
        g = P.verificar_ligacao("no_trelica_gusset", d, ex["esforcos"])["geometria"]
        self.assertIn("b_solda_2", {s["campo"] for s in g["soldas"]})

    def test_sem_solucao_traz_campo_limitante_estruturado(self):
        ex = {t["tipo"]: t["exemplo"] for t in _tipos()}["chapa_simples_alma"]
        r = L.otimizar("chapa_simples_alma", ex["dados"], dict(ex["esforcos"], V=5000.0))
        self.assertFalse(r["ok"])
        nomes = {c["nome"] for c in L.esquema("chapa_simples_alma")}
        self.assertTrue(r["campos_limite"])
        for c in r["campos_limite"]:
            self.assertIn(c["campo"], nomes)
            self.assertIn(c["tipo"], ("no_maximo", "degrau_barrado", "faixa_vazia"))
        self.assertIn(r["campo_limitante"], nomes)
        self.assertIn(r["campo_limitante"], [c["campo"] for c in r["campos_limite"]])


if __name__ == "__main__":
    unittest.main()
