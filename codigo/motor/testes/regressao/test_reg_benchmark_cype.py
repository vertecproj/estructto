"""Benchmark: obra do CYPE 3D 2016 importada (importar_cype.ler_ed3) e calculada pelo nosso solver x
resultados que o próprio CYPE gravou no .ed3 (ler_resultados_ed3).

Relatório: verificacao/2026-10-20-benchmark-cype.md. O CYPE é referência de comparação, não fonte de valor:
diferença real e explicada fica como expectedFailure (com o motivo); ninguém ajusta tolerância para passar.
Grandezas comparadas (todas globais, sem depender da convenção de eixos locais de cada programa):
  ΣR  : soma das reações por hipótese (equilíbrio)      tolerância 0,5 %
  u   : max |Δu| nos nós / max |u| do CYPE              tolerância 5 %
  Rnó : max |ΔR| por apoio / max |R| do CYPE            tolerância 5 %
  M   : max √(My²+Mz²) em todas as seções               tolerância 5 %
O CYPE grava reação como ação no apoio: compara-se com −R do solver.
"""
import unittest
from pathlib import Path

import numpy as np

from prossen_estrutural import importar_cype as ic, solver

ORIG = Path(r"C:\CYPE Ingenieros\Projetos\CYPE 3D")
DADOS = Path(__file__).resolve().parents[1] / "dados_cype"
TOL_R, TOL_U, TOL_RNO, TOL_M = 0.005, 0.05, 0.05, 0.05
_CACHE = {}


def _arq(nome):
    p = DADOS / nome
    return p if p.exists() else ORIG / nome


def _par(nome):
    p = _arq(nome)
    if nome not in _CACHE:
        _CACHE[nome] = (ic.ler_ed3(p), ic.ler_resultados_ed3(p))
    return _CACHE[nome]


def metricas(nome, caso):
    m, r = _par(nome)
    k = r["hipoteses"].index(caso)
    res = solver.analisar(m, caso)
    Rn = -np.sum([v[:3] for v in res.reacoes.values()], axis=0)
    Rc = np.sum([v["reacao"][k][:3] for v in r["nos"].values()], axis=0)
    uc = np.array([r["nos"][n]["desloc"][k][:3] for n in r["nos"]])
    un = np.array([res.deslocamentos[n][:3] for n in r["nos"]])
    apoios = [n for n in r["nos"] if n in res.reacoes]
    rc = np.array([r["nos"][n]["reacao"][k][:3] for n in apoios])
    rn = np.array([-res.reacoes[n][:3] for n in apoios])
    mc = max(np.hypot(p[4], p[5]) for b in r["barras"].values() for p in b["esforcos"][k])
    mn = max(np.hypot(p[5], p[6]) for b in res.esforcos.values() for p in b)
    return {"R": np.linalg.norm(Rn - Rc) / np.linalg.norm(Rc),
            "u": np.max(np.linalg.norm(un - uc, axis=1)) / np.max(np.linalg.norm(uc, axis=1)),
            "Rno": np.max(np.linalg.norm(rn - rc, axis=1)) / np.max(np.linalg.norm(rc, axis=1)),
            "M": abs(mn - mc) / mc if mc > 0 else 0.0}


def _existe(nome):
    return _arq(nome).exists()


class _Base(unittest.TestCase):
    def confere(self, nome, casos, chaves, tol):
        for caso in casos:
            mt = metricas(nome, caso)
            for ch in chaves:
                with self.subTest(caso=caso, grandeza=ch):
                    self.assertLessEqual(mt[ch], tol[ch], f"{nome} {caso} {ch} = {mt[ch]:.4f}")


TOL = {"R": TOL_R, "u": TOL_U, "Rno": TOL_RNO, "M": TOL_M}


@unittest.skipUnless(_existe("PIPERACK TESTE 4.ed3"), "arquivo ausente")
class TestPorticoTeste4(_Base):
    """Pórtico plano (2 colunas W 200 x 22.5, viga W 150 x 13), só peso próprio + carga nodal."""

    def test_resultados_validos(self):
        self.assertTrue(_par("PIPERACK TESTE 4.ed3")[1]["valido"])

    def test_pp(self):
        self.confere("PIPERACK TESTE 4.ed3", ["PP"], ["R", "u", "Rno", "M"], TOL)


@unittest.skipUnless(_existe("PIPERACK TESTE 3.ed3"), "arquivo ausente")
class TestTeste3(_Base):
    def test_pp(self):  # só peso próprio (colunas): equilíbrio e encurtamento axial
        self.confere("PIPERACK TESTE 3.ed3", ["PP"], ["R", "u"], TOL)


@unittest.skipUnless((ORIG / "G00-MEC-002.ed3").exists(), "G00-MEC-002.ed3 (> 1 MB) só no caminho original")
class TestG00(_Base):
    """Suporte espacial com perfis U formados a frio girados, cargas permanentes e de vento em faixas."""
    CASOS = ["PP", "CP 1", "V 1", "V 2"]

    def test_hipoteses_na_ordem_do_cype(self):
        self.assertEqual(_par("G00-MEC-002.ed3")[1]["hipoteses"], self.CASOS)

    def test_equilibrio_deslocamentos_momentos(self):
        self.confere("G00-MEC-002.ed3", self.CASOS, ["R", "u", "M"], TOL)

    def test_reacoes_por_apoio(self):
        """Diferença real (até 29 % no PP): o equilíbrio global bate, mas a partilha do empuxo horizontal entre
        os 4 apoios difere — trechos de 0,2 m com perfis U, onde pesam deformação por cortante e o centro de
        cisalhamento (o nosso solver é Euler-Bernoulli sem acoplamento flexo-torção). Ver relatório."""
        self.confere("G00-MEC-002.ed3", self.CASOS, ["Rno"], TOL)


@unittest.skipUnless((ORIG / "PIPERACK.ed3").exists(), "PIPERACK.ed3 (2,4 MB) só no caminho original")
class TestPiperack(_Base):
    GRAV = ["PP", "CP 1", "CP 2", "T 1"]

    def test_equilibrio_e_reacoes_gravidade(self):
        self.confere("PIPERACK.ed3", self.GRAV, ["R", "Rno"], TOL)

    def test_rigidez_gravidade(self):
        """Diferença real: deslocamentos 6–10 % maiores e M máx. do PP 13 % maior no nosso modelo. O CYPE
        gravou trechos rígidos de até 0,196 m em 119 das 177 barras (dimensão dos nós), que o nosso solver
        não modela; com E = 205,9 GPa (o do CYPE) a diferença cai para 4–7 %. Ver relatório."""
        self.confere("PIPERACK.ed3", self.GRAV, ["u", "M"], TOL)

    def test_vento(self):
        """O vento (V 1, V 2) entra no CYPE por 2 painéis de carga superficial (ΣR 37,4 kN e 8,2 kN), agora
        distribuídos às barras pelo importador (antes só as cargas em barra chegavam: 3,3 kN)."""
        self.confere("PIPERACK.ed3", ["V 1", "V 2"], ["R"], TOL)


@unittest.skipUnless(_existe("DIMENSIONAMENTO DE JUNTA.ed3"), "arquivo ausente")
class TestJunta(unittest.TestCase):
    def test_resultado_desatualizado_nao_serve_de_referencia(self):
        self.assertFalse(_par("DIMENSIONAMENTO DE JUNTA.ed3")[1]["valido"])


if __name__ == "__main__":
    unittest.main()
