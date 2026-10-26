"""Chapa de topo com esforços fora do plano forte: Fy (chave Vz), Mz e Mx (chave T) — 26/10/2026, pedido do usuário.
Critério em docs/v3/LIGACOES-ESFORCOS-3D.md.

Conta à mão, W460×89 (CBCA LMPA: bf 192, tf 17,7 mm), chapa estendida em cima, 1", pf 40, g 110 mm, V = 102 kN, M = 290 kN·m:
  linhas z = +271,5 (externa), ±173,8 mm (internas) → 3 linhas, n = 6 parafusos, y = ±55 mm.
  Ip = 6·0,055² + 2·(0,2715² + 2·0,1738²) = 0,01815 + 0,26825 = 0,28640 m².
  Só Fy = 60: Vb = √(10² + 17²) = 19,72 kN.
  Só Mx = 10: no parafuso (y = +55, z = +271,5): fy = −10·0,2715/0,2864 = −9,480; fz = 17 + 10·0,055/0,2864 = 18,920
              → Vb = 21,16 kN (o maior do grupo).
  Só Mz = 20: dT = 2·20/(0,110·6) = 60,61 kN por parafuso; solda das mesas: força equivalente + 3·20/0,192 = +312,5 kN.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from prossen_estrutural import secoes  # noqa: E402
from prossen_estrutural.aco import ligacoes_rigidas as lr  # noqa: E402

DADOS = dict(perfil_viga="W460x89", aco_viga="AR-350", d_mm=25.4, tp=31.5, bp=220, g=110, pf=40, de=40, borda_laminada=True)
BASE = {"M": 290, "V": 102}


def rel(a, b):
    return abs(a - b) / abs(b)


def _v(r, prefixo):
    achados = [v for v in r["verificacoes"] if v["nome"].startswith(prefixo)]
    assert achados, f"verificação '{prefixo}' ausente"
    return achados[0]


def _calc(**extra):
    return lr.verificar("chapa_topo_estendida", DADOS, dict(BASE, **extra))


class TestChapaTopo3D(unittest.TestCase):
    def test_sem_esforco_3d_igual_antes(self):
        r = _calc()
        self.assertLess(rel(_v(r, "parafusos: cisalhamento (V/n")["solicitante"], 17.0), 1e-3)
        self.assertFalse(any("fora do plano forte" in a for a in r["avisos"]))
        self.assertFalse(any("Mz" in v["nome"] or "(Fy)" in v["nome"] for v in r["verificacoes"]))

    def test_fy_cortante_horizontal(self):
        r = _calc(Vz=60)
        self.assertLess(rel(_v(r, "parafusos: cisalhamento (grupo")["solicitante"], 19.72), 1e-3)
        self.assertEqual(_v(r, "chapa de topo: escoamento por cisalhamento horizontal")["solicitante"], 60)
        self.assertFalse(any("não considerados" in a for a in r["avisos"]))

    def test_mx_torcao_grupo_elastico(self):
        r = _calc(T=10)
        self.assertLess(rel(_v(r, "parafusos: cisalhamento (grupo")["solicitante"], 21.16), 2e-3)

    def test_mz_tracao_e_solda(self):
        r0, r = _calc(), _calc(Mz=20)
        self.assertLess(rel(_v(r, "parafusos fora da zona tracionada: tração por Mz")["solicitante"], 60.61), 1e-3)
        s0 = _v(r0, "solda das mesas")["solicitante"]
        self.assertLess(rel(_v(r, "solda das mesas")["solicitante"] - s0, 312.5), 1e-3)
        # parafusos tracionados recebem dT a mais (T por parafuso)
        L0, L1 = r0["intermediarios"]["linhas"][0], r["intermediarios"]["linhas"][0]
        self.assertLess(rel(L1["T"] - L0["T"], 60.61), 1e-3)

    def test_mz_flexao_obliqua_viga(self):
        p = secoes.catalogo("W460x89").props
        fy = 350e3 / 1.1
        esperado = 290 / (min(p["Zx"], 1.5 * p["Wx"]) * fy) + 20 / (min(p["Zy"], 1.5 * p["Wy"]) * fy)
        self.assertLess(rel(_v(_calc(Mz=20), "viga: flexão oblíqua")["solicitante"], esperado), 1e-3)

    def test_mz_grande_reprova(self):
        self.assertFalse(_calc(Mz=150)["ok"])


if __name__ == "__main__":
    unittest.main()
