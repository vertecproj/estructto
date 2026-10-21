"""Otimizador limitado (docs/v3/LIGACOES-OTIMIZADOR.md, Frente C2): só busca dentro de ligacoes_limites.limites e, sem
solução, não altera nada e explica (motivo, verificacao_limitante, limite_atingido).

Exemplos dos livros (docs/v3/ligacoes/): Pfeil 9.10.2 (2 cantoneiras, Vd = 210 kN), Pfeil 9.10.1 (emenda de viga
VS600×95, Md = 328 kN·m, Vd = 168 kN) e CBCA LMPA W460×89 (chapa de topo, 6 × 1" A325, chapa 31,5 × 220)."""
import os
import random
import sys
import unittest

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco.ligacoes_limites import limites, T_COMERCIAL
from test_ligacoes_flexiveis import PFEIL_9_10_2
import test_ligacoes_rigidas as _R

PERFIS = ("W150x13", "W200x26.6", "W250x25.3", "W310x38.7", "W360x51", "W410x60")


def fora_dos_limites(tipo, r):
    """Campos mudados pelo otimizador que saem da faixa de `limites` (recalculada nos dados finais)."""
    d = r["dados"]
    lim = limites(tipo, L._membro(d), dict(d, t=L._t_min(d)))
    out = []
    for k, v in r["mudancas"].items():
        f = lim.get(k)
        v = v[1]
        if not f or not isinstance(v, (int, float)):
            continue
        if (v < f["min"] - 1e-9 or (f["max"] is not None and v > f["max"] + 1e-9)
                or (f["passos"] and not any(abs(v - p) < 1e-9 for p in f["passos"]))):
            out.append((k, v, f))
    return out


class TestOtimizadorLimitado(unittest.TestCase):
    def test_apoio_menor_que_chapa_minima_para_e_nao_muda_nada(self):
        # Review Focus 5: apoio com bf = 50 mm < chapa mínima 2·borda(3/4") = 64 mm
        d = dict(perfil_viga="W250x25.3", perfil_pilar="W310x38.7", bp=150.0)
        membro = dict(perfil="W250x25.3", perfil_apoio=dict(h=0.2, b=0.05, tw=0.005, tf=0.008))
        r = L.otimizar("chapa_topo_ajustada", d, {"M": 40, "V": 30}, membro=membro)
        self.assertFalse(r["ok"])
        self.assertEqual((r["mudancas"], r["params"], r["passos"]), ({}, {}, []))
        self.assertEqual(r["dados"]["bp"], 150.0)
        self.assertIn("bp", r["motivo"])
        self.assertIn("50", r["motivo"])          # o máximo (bf do apoio) aparece na mensagem
        self.assertEqual(r["limite_atingido"], ["bp"])

    def test_chapa_topo_w250_em_w310_aprova_com_espessura_comercial(self):
        d = dict(perfil_viga="W250x25.3", perfil_pilar="W310x38.7", tp=20.0)   # 20 mm não é comercial
        r = L.otimizar("chapa_topo_ajustada", d, {"M": 30, "V": 30})
        self.assertTrue(r["ok"], r["motivo"])
        self.assertIn(r["dados"]["tp"], T_COMERCIAL)
        self.assertTrue(all(x["taxa"] <= 1 + 1e-9 for x in r["resultado"]["verificacoes"]))
        self.assertEqual(fora_dos_limites("chapa_topo_ajustada", r), [])
        self.assertIsNone(r["motivo"])
        self.assertEqual(r["params"], {k: v[1] for k, v in r["mudancas"].items()})

    def test_w250_em_w310_m40_resolve_com_enrijecedor_do_pilar(self):
        # sem enrijecedor a mesa do pilar governa (1,29); o último degrau põe enrijecedor ≥ tf da viga (8,4 → 9,5+)
        d = dict(perfil_viga="W250x25.3", perfil_pilar="W310x38.7")
        r = L.otimizar("chapa_topo_ajustada", d, {"M": 40, "V": 30})
        self.assertTrue(r["ok"], r["motivo"])
        self.assertIn(r["dados"]["t_enr_pilar"], T_COMERCIAL)
        self.assertGreaterEqual(r["dados"]["t_enr_pilar"], 8.4)
        self.assertEqual(fora_dos_limites("chapa_topo_ajustada", r), [])

    def test_motivo_cita_verificacao_que_governa_os_dados_originais(self):
        d = dict(perfil_viga="W250x25.3", perfil_pilar="W310x38.7")
        r = L.otimizar("chapa_topo_ajustada", d, {"M": 400, "V": 30})
        self.assertFalse(r["ok"])
        pior = max(r["resultado"]["verificacoes"], key=lambda x: x["taxa"])
        self.assertEqual(r["verificacao_limitante"], pior["nome"])
        self.assertIn(f"{pior['taxa']:.2f}", r["motivo"])

    def test_mais_parafusos_compacta_o_espacamento_em_vez_de_reprovar_a_chapa(self):
        # causa raiz do "erro pela medida das chapas": n + 1 com s fixo passava da alma (taxa inf) e era descartado
        r = L.otimizar("cantoneira_dupla_alma", PFEIL_9_10_2, {"V": 210.0})
        self.assertTrue(r["ok"], r["motivo"])
        self.assertGreater(r["dados"]["n"], PFEIL_9_10_2["n"])
        self.assertEqual(fora_dos_limites("cantoneira_dupla_alma", r), [])

    def test_sem_solucao_explica_e_devolve_dados_originais(self):
        ex = next(t for t in P.tipos_ligacao() if t["tipo"] == "tirante_barra_redonda")["exemplo"]
        esf = {k: v * 3 for k, v in ex["esforcos"].items()}
        r = L.otimizar("tirante_barra_redonda", ex["dados"], esf)
        self.assertFalse(r["ok"])
        self.assertEqual(r["mudancas"], {})
        self.assertTrue(r["verificacao_limitante"])
        self.assertIn(r["verificacao_limitante"], r["motivo"])
        self.assertIn("nada alterado", r["motivo"])

    def test_dados_iniciais_invalidos_nao_levantam(self):
        r = L.otimizar("cantoneira_dupla_alma", dict(PFEIL_9_10_2, cantoneira="XYZ"), {"V": 100})
        self.assertFalse(r["ok"])
        self.assertIn("dados iniciais inválidos", r["motivo"])
        r = P.otimizar_ligacao("cantoneira_dupla_alma", dict(PFEIL_9_10_2, cantoneira="XYZ"), {"V": 100})   # rota: 200, não 422
        self.assertIsNone(r["resultado"])

    def test_200_casos_aleatorios_nunca_saem_dos_limites(self):
        rnd = random.Random(20261021)
        tipos = [t for t in P.tipos_ligacao() if t["tipo"] in L.tipos_contrato()]
        for i in range(200):
            t = rnd.choice(tipos)
            d = dict(t["exemplo"]["dados"])
            if "perfil_viga" in d and isinstance(d["perfil_viga"], str):
                d["perfil_viga"] = rnd.choice(PERFIS)
            esf = {k: v * rnd.uniform(0.2, 3.0) for k, v in t["exemplo"]["esforcos"].items()}
            r = L.otimizar(t["tipo"], d, esf)
            ctx = f"caso {i}: {t['tipo']} {d.get('perfil_viga')} {esf}"
            self.assertEqual(fora_dos_limites(t["tipo"], r), [], ctx)
            if r["ok"]:
                self.assertTrue(all(x["taxa"] <= 1 + 1e-9 for x in r["resultado"]["verificacoes"]), ctx)
            else:
                self.assertEqual(r["mudancas"], {}, ctx)
                self.assertTrue(r["motivo"], ctx)


class TestExemplosLivros(unittest.TestCase):
    """Conferência dos 3 exemplos: a partir dos dados do livro, o otimizador entrega um detalhe dentro dos limites
    com todas as taxas ≤ 1,0 (o detalhe do livro em si reprova em alguma verificação extra do motor)."""

    def conferir(self, tipo, dados, esf):
        r = L.otimizar(tipo, dados, esf)
        self.assertTrue(r["ok"], r["motivo"])
        self.assertLessEqual(max(x["taxa"] for x in r["resultado"]["verificacoes"]), 1 + 1e-9)
        self.assertEqual(fora_dos_limites(tipo, r), [])
        self.assertTrue(L.verificar_ligacao(tipo, r["dados"], esf)["ok"])
        return r

    def test_pfeil_9_10_2(self):
        self.conferir("cantoneira_dupla_alma", PFEIL_9_10_2, {"V": 210.0})

    def test_pfeil_9_10_1_emenda(self):
        self.conferir("emenda_viga", _R.TestEmendaVigaPfeil.DADOS, {"M": 328.0, "V": 168.0})

    def test_cbca_lmpa_w460(self):
        r = self.conferir("chapa_topo_estendida", dict(_R.TestChapaTopoCBCA.DADOS, perfil_pilar="W310x97"),
                          {"M": 250.0, "V": 100.0})
        self.assertIn(r["dados"]["tp"], T_COMERCIAL)


if __name__ == "__main__":
    unittest.main()
