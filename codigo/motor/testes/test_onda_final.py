"""Onda de correção final v0.5: troca aplicada por membro com os esforços reais, barra curta estruturada,
_gusset_minimo sem piso silencioso, aviso da solda do enrijecedor, motivo da solda direta cita o governante."""
import copy
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes_limites as LL
from prossen_estrutural.web import servidor  # noqa: F401  (rota registrada)
from testes.test_ligacoes_modelo import _portico

FORTE = dict(h=0.23, t=0.0095, d_mm=19.05, n=3, s=0.075, e=0.06)
FRACA = dict(h=0.15, t=0.003, d_mm=12.7, n=2, s=0.075, e=0.0375)


def _calculado():
    m = _portico()
    P.ligacoes_identificar(m)
    c = P.calcular(m)
    gid = next(k for k, g in m.ligacoes["grupos"].items() if "continuo" in g["assinatura"])
    return m, c, gid


class TestAplicarTroca(unittest.TestCase):
    def test_por_membro_com_esforcos_reais(self):
        m, c, gid = _calculado()
        mb = m.ligacoes["grupos"][gid]["membros"][0]
        n0 = len(m.ligacoes["grupos"][gid]["membros"])
        r = P.ligacoes_aplicar_troca(m, c, dict(grupo=gid, barra=mb["barra"], no=mb["no"],
                                                tipo_ligacao="chapa_simples", dados=FORTE))
        self.assertTrue(r["ok"])
        self.assertNotEqual(r["grupo"], gid)                       # grupo dividido: só este membro mudou
        novo = m.ligacoes["grupos"][r["grupo"]]
        self.assertEqual([(x["barra"], x["no"]) for x in novo["membros"]], [(mb["barra"], mb["no"])])
        self.assertEqual(novo["projeto"]["tipo_ligacao"], "chapa_simples")
        self.assertEqual(len(m.ligacoes["grupos"][gid]["membros"]), n0 - 1)
        self.assertIsNone(m.ligacoes["grupos"][gid]["projeto"])
        self.assertIsNotNone(r["combinacao"])                      # verificada nas combinações ELU do cálculo

    def test_falha_nao_persiste_e_cita_verificacao(self):
        m, c, gid = _calculado()
        antes = copy.deepcopy(m.ligacoes)
        mb = m.ligacoes["grupos"][gid]["membros"][0]
        with self.assertRaises(P.ErroEntrada) as cm:
            P.ligacoes_aplicar_troca(m, c, dict(grupo=gid, barra=mb["barra"], no=mb["no"],
                                                tipo_ligacao="chapa_simples", dados=FRACA))
        self.assertIn("troca não aplicada", str(cm.exception))
        self.assertIn("taxa", str(cm.exception))
        self.assertEqual(m.ligacoes, antes)

    def test_sem_calculo(self):
        m = _portico()
        P.ligacoes_identificar(m)
        with self.assertRaises(P.ErroEntrada):
            P.ligacoes_aplicar_troca(m, None, dict(grupo="G1", barra="x", no="y", tipo_ligacao="chapa_simples"))


class TestBarraCurta(unittest.TestCase):
    def test_flag_estruturado_no_lote(self):
        """x_max menor que a ligação: barra_curta/excluir_interferencia vêm como campos, não só no texto."""
        it = dict(tipo="chapa_simples_alma", dados=dict(perfil_viga="W 310 x 38,7", perfil_suporte="W 310 x 38,7",
                                                        apoio="mesa_pilar", x_max=20.0))
        s = P.geometrias_ligacoes([it])[0]
        self.assertTrue(s.get("barra_curta"))
        self.assertTrue(s.get("excluir_interferencia"))
        self.assertFalse(s.get("ok"))

    def test_lote_normal_sem_flag(self):
        it = dict(tipo="chapa_simples_alma", dados=dict(perfil_viga="W 310 x 38,7", perfil_suporte="W 310 x 38,7",
                                                        apoio="mesa_pilar"))
        s = P.geometrias_ligacoes([it])[0]
        self.assertNotIn("barra_curta", s)


class TestSemLigacaoValida(unittest.TestCase):
    def test_dimensionar_reporta_barra_curta(self):
        """Verificador que devolve barra_curta: o membro fica ok=False, sem_ligacao_valida e entra em `erros`
        (gerenciador e Calcular/memorial mostram; não some)."""
        from prossen_estrutural.aco import ligacoes_modelo as lm
        m, c, gid = _calculado()
        P.ligacoes_editar(m, {"acao": "projeto", "grupo": gid, "tipo_ligacao": "chapa_simples", "dados": FORTE})
        orig = lm.verificador
        lm.verificador = lambda tl: (lambda *a: dict(ok=False, taxa_max=0.5, motivo="barra curta demais para a ligação",
                                                      barra_curta=True, excluir_interferencia=True, verificacoes=[]))
        try:
            r = P.ligacoes_dimensionar(m, c, gid)
        finally:
            lm.verificador = orig
        self.assertFalse(r["ok"])
        self.assertTrue(all(x["sem_ligacao_valida"] for x in r["membros"]))
        self.assertTrue(any("sem ligação válida" in e for e in r["erros"]))


class TestAvisosEMotivos(unittest.TestCase):
    def test_solda_enrijecedor_aviso(self):
        r = P.verificar_ligacao("chapa_topo_estendida", dict(perfil_viga="W460x89", perfil_pilar="W310x97",
                                                              t_enr_pilar=16), {"M": 300})
        self.assertTrue(any("garganta 5 mm" in a for a in r["avisos"]))

    def test_motivo_perfil_baixo_cita_governante(self):
        s = P.sugerir_ligacao("chapa_simples_alma", dict(perfil_viga="UPN 50", perfil_suporte="UPN 50",
                                                         apoio="alma_viga"), {"V": 5})
        self.assertEqual(s["tipo_ligacao"], "solda_direta_alma")
        self.assertRegex(s["motivo"], r"governa: solda alma.*taxa 0,9\d")   # G5: a solda (0,98), não "dimensão mínima" (1,00 geométrico)


class TestGussetMinimo(unittest.TestCase):
    def test_dados_ruins_devolvem_aviso_com_causa(self):
        lh, lv, aviso = LL._gusset_minimo(dict(secao="tubo_circular", dim_a="abc"))
        self.assertEqual((lh, lv), (50.0, 50.0))
        self.assertTrue(aviso and "50 mm" in aviso and len(aviso) > 30)

    def test_limites_levam_o_aviso(self):
        out = LL.limites("gusset_contraventamento", {}, dict(secao="tubo_circular", dim_a="abc"))
        self.assertIn("aviso", out["Lh"])


if __name__ == "__main__":
    unittest.main()
