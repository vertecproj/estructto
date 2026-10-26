"""Chapa de topo com esforços fora do plano forte: Fy (chave Vz), Mz e Mx (chave T) — 26/10/2026, pedido do usuário.
Critério em docs/v3/LIGACOES-ESFORCOS-3D.md.

Conta à mão, W460×89 (CBCA LMPA: bf 192, tf 17,7 mm), chapa estendida em cima, 1", pf 40, g 110 mm, V = 102 kN, M = 290 kN·m:
  linhas z = +271,5 (externa), ±173,8 mm (internas) → 3 linhas, n = 6 parafusos, y = ±55 mm.
  centroide zg = (271,5 + 173,8 − 173,8)/3 = 90,5 mm (grupo assimétrico); z − zg = 181,0; 83,3; −264,3 mm.
  Ip = 6·0,055² + 2·(0,181² + 0,0833² + 0,2643²) = 0,01815 + 0,21911 = 0,23726 m².
  Só Fy = 60: Vb = √(10² + 17²) = 19,72 kN.
  Só Mx = 10: no parafuso (y = +55, z = −173,8): fy = 10·0,2643/0,23726 = 11,140; fz = 17 + 10·0,055/0,23726 = 19,318
              → Vb = 22,30 kN (o maior do grupo).
  Penetração com só Fy = 60: cisalhamento por mesa 30 kN contra 0,6·bf·tf·fy/γa1 = 0,6·0,192·0,0177·350000/1,1 = 648,8 kN.
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
        self.assertLess(rel(_v(r, "parafusos: cisalhamento (grupo")["solicitante"], 22.30), 2e-3)

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
        self.assertLess(rel(_v(_calc(Mz=20), "viga: N + flexão oblíqua")["solicitante"], esperado), 1e-3)

    def test_mz_com_normal_grande(self):
        p = secoes.catalogo("W460x89").props
        fy = 350e3 / 1.1
        nr = 1500 / (p["A"] * fy)
        fm = 290 / (min(p["Zx"], 1.5 * p["Wx"]) * fy) + 20 / (min(p["Zy"], 1.5 * p["Wy"]) * fy)
        self.assertGreater(nr, 0.2)
        r = _calc(Mz=20, N=1500)
        self.assertLess(rel(_v(r, "viga: N + flexão oblíqua")["solicitante"], nr + 8 / 9 * fm), 1e-3)

    def test_penetracao_cisalhamento_06fy(self):
        v = _v(lr.verificar("chapa_topo_estendida", dict(DADOS, solda_mesa="penetracao"), dict(BASE, Vz=60)),
               "solda das mesas (penetração total): cisalhamento")
        self.assertLess(rel(v["solicitante"], 30.0), 1e-6)
        self.assertLess(rel(v["resistente"], 648.8), 1e-3)

    def test_mz_grande_reprova(self):
        self.assertFalse(_calc(Mz=150)["ok"])

    def test_inferior_espelha_superior(self):
        # alinhada em cima (estende embaixo) com M < 0 = alinhada embaixo com M > 0, espelhada em z
        a = lr.verificar("chapa_topo_estendida", dict(DADOS, estendida="superior"), {"M": 290, "V": 102})
        b = lr.verificar("chapa_topo_estendida", dict(DADOS, estendida="inferior"), {"M": -290, "V": 102})
        self.assertAlmostEqual(a["intermediarios"]["MRd"], b["intermediarios"]["MRd"], places=6)
        self.assertAlmostEqual(a["intermediarios"]["altura_chapa"], b["intermediarios"]["altura_chapa"], places=6)
        za = sorted(round(p["origem"][2], 6) for p in a["geometria"]["parafusos"])
        zb = sorted(round(-p["origem"][2], 6) for p in b["geometria"]["parafusos"])
        self.assertEqual(za, zb)
        self.assertTrue(any("lado não estendido" in x for x in
                            lr.verificar("chapa_topo_estendida", dict(DADOS, estendida="inferior"), BASE)["avisos"]))

    def test_u_recusado_com_alternativa(self):
        with self.assertRaisesRegex(ValueError, "solda direta"):
            lr.verificar("chapa_topo_estendida", dict(perfil_viga="UPN 50", perfil_pilar="UPN 120"), BASE)


if __name__ == "__main__":
    unittest.main()
