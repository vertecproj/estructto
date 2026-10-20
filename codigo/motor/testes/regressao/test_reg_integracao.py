"""Regressão da integração (projeto.calcular, gerar_acoes, gerador, relatório) — verificador-calculos, ficha 019.

Método (ficha 019): na MESMA execução, compara `calcular` com chamadas diretas aos motores, com os dados
montados aqui à mão: fatores de combinação da NBR 8681 (γg = γq = γw = 1,4 para ações agrupadas,
"grandes variabilidade"; ψ0 = 0,7 / ψ1 = 0,6 / ψ2 = 0,4 para Q comercial/escritório, ψ0 = 0,6 / ψ1 = 0,3 /
ψ2 = 0 para vento — NBR 8681 tabelas 2, 4 e 6), pesos por pavimento somados à mão a partir das cargas
do LEIAME dos exemplos, Cb pela NBR 8800 5.4.2.3, γz pela NBR 6118 15.5.3 e B2 pela NBR 8800 D.2.3.
Achados: `verificacao/2026-10-20-integracao.md`. Testes que falham hoje: `expectedFailure` + id.
"""
import copy
import json
import math
import re
import unittest
from pathlib import Path

import numpy as np

from prossen_estrutural import gerador, projeto as P, relatorio, solver as S
from prossen_estrutural.aco import nbr8800
from prossen_estrutural.acoes import vento
from prossen_estrutural.concreto import nbr6118

EX = Path(__file__).resolve().parents[2] / "exemplos"


def _ex(nome):
    return P.carregar(EX / f"{nome}.json")


def _comb(c, nome):
    return next(x for x in c.combinacoes if x["nome"] == nome)


# ---------------------------------------------------------------- combinação determinante (portico_aco)

class TestCombinacaoDeterminante(unittest.TestCase):
    """portico_aco: G1, Q1 (comercial), V1. Combinações ELU montadas à mão (NBR 8681)."""

    FATORES = [{"G1": 1.4}, {"G1": 1.4, "V1": 1.4}, {"G1": 1.0, "V1": 1.4}, {"G1": 1.4, "Q1": 1.4},
               {"G1": 1.4, "Q1": 1.4, "V1": 0.6 * 1.4}, {"G1": 1.4, "Q1": 0.7 * 1.4, "V1": 1.4},
               {"G1": 1.0, "Q1": 0.7 * 1.4, "V1": 1.4},
               # VC-logica-acoes-002 (NBR 8681 5.1.3.1): G favorável também sem vento principal
               {"G1": 1.0}, {"G1": 1.0, "Q1": 1.4}, {"G1": 1.0, "Q1": 1.4, "V1": 0.6 * 1.4}]

    @classmethod
    def setUpClass(cls):
        cls.m = _ex("portico_aco")
        cls.c = P.calcular(cls.m)
        cls.caso = {k: S.analisar(cls.m, k, False, 13) for k in ("G1", "Q1", "V1")}

    def _direto(self, bid, f):
        """Verificação de aço chamada direto no motor, combinação por superposição à mão."""
        r = S.combinar(self.caso, f)
        linhas = r.esforcos[bid]
        esf = [nbr8800.esforcos_nbr(l) for l in linhas]
        b = self.m.barras[bid]
        L = math.dist(*[(self.m.nos[n].x, self.m.nos[n].y, self.m.nos[n].z) for n in (b.ni, b.nj)])
        # Cb à mão (NBR 8800 5.4.2.3, Rm = 1): pontos x = 0, L/4, L/2, 3L/4 com n_pontos = 13
        M = [abs(e["Mx"]) for e in esf]
        Mmax = max(M)
        Cb = min(12.5 * Mmax / (2.5 * Mmax + 3 * M[3] + 4 * M[6] + 3 * M[9]), 3.0) if Mmax > 1e-9 else 1.0
        sec, mat = self.m.secoes[b.secao], self.m.materiais[b.material]
        return nbr8800.verificar_barra_aco(sec, mat, esf, L, L, L, Cb, L)

    def test_combinacoes_iguais_as_da_mao(self):
        elu = [c["fatores"] for c in self.c.combinacoes if c["estado"] == "ELU"]
        self.assertEqual(len(elu), len(self.FATORES))
        for f in self.FATORES:
            self.assertTrue(any(set(f) == set(g) and all(abs(f[k] - g[k]) < 1e-12 for k in f) for g in elu), f)

    def test_pontos_13_quartos_exatos(self):
        x = [l[0] for l in self.c.resultados["ELU G"].esforcos["VX1-0-0"]]
        self.assertEqual(len(x), 13)
        np.testing.assert_allclose(x, np.linspace(0, 6.0, 13), atol=1e-12)

    def test_taxa_por_combinacao_igual_ao_motor(self):
        for bid in ("VX1-0-0", "P1-0-0"):
            por = {p["combinacao"]: p["taxa"] for p in self.c.por_combinacao[bid]}
            for f in self.FATORES:
                nome = next(c["nome"] for c in self.c.combinacoes
                            if c["estado"] == "ELU" and set(c["fatores"]) == set(f)
                            and all(abs(c["fatores"][k] - f[k]) < 1e-12 for k in f))
                self.assertAlmostEqual(por[nome], self._direto(bid, f)["taxa_max"], delta=1e-9)

    def test_detalhes_com_cb_do_trecho(self):
        """Interação da combinação determinante igual à do motor com Cb dos quartos de vão (1e-9)."""
        d = P.verificacao_barra(self.c, "VX1-0-0")
        f = _comb(self.c, d["combinacao"])["fatores"]
        self.assertAlmostEqual(d["detalhes"]["taxas"]["interacao"],
                               self._direto("VX1-0-0", f)["detalhes"]["taxas"]["interacao"], delta=1e-9)

    def test_determinante_pelos_criterios_de_esforco(self):
        for bid in ("VX1-0-0", "P1-0-0"):
            def esf(f):
                t = self._direto(bid, f)["detalhes"]["taxas"]
                return max(v for k, v in t.items() if k != "esbeltez")
            pior = max(self.FATORES, key=esf)
            linha = next(l for l in self.c.verificacao if l["barra"] == bid)
            self.assertEqual(_comb(self.c, linha["combinacao"])["fatores"], pior, bid)


# ---------------------------------------------------------------- eixos solver → NBR (viga com carga lateral)

class TestEixosVigaLateral(unittest.TestCase):
    """Viga W310x38.7 biapoiada (L = 6 m) ao longo de X com 2 kN/m em Y global: flexão no eixo FRACO.
    My,Sd(NBR) = 1,4·2·6²/8 = 12,6 kN·m; Mx,Sd = 0. Se os eixos fossem trocados a taxa cairia ~10×."""

    def test_flexao_lateral_vai_para_My_da_norma(self):
        m = P.novo_modelo("lateral")
        apoio1, apoio2 = [True, True, True, True, False, False], [False, True, True, True, False, False]
        P.adicionar_lote(m, [
            {"tipo": "material", "dados": {"aco": "A-572 GR.50"}},
            {"tipo": "secao", "dados": {"perfil": "W310x38.7"}},
            {"tipo": "no", "dados": {"id": "a", "x": 0, "y": 0, "z": 0, "apoio": apoio1}},
            {"tipo": "no", "dados": {"id": "b", "x": 6, "y": 0, "z": 0, "apoio": apoio2}},
            {"tipo": "barra", "dados": {"id": "v", "ni": "a", "nj": "b", "secao": "W310x38.7",
                                        "material": "A-572 GR.50", "params": {"papel": "viga"}}},
            {"tipo": "caso", "dados": {"nome": "G1", "tipo": "G"}},
            {"tipo": "carga_distribuida", "dados": {"caso": "G1", "barra": "v", "q": [0, 2.0, 0]}}])
        P.configurar(m, {"peso_proprio": None})
        c = P.calcular(m)
        d = P.verificacao_barra(c, "v")
        MRdx, MRdy = d["detalhes"]["flexao_x"]["MRd"], d["detalhes"]["flexao_y"]["MRd"]
        self.assertAlmostEqual(d["detalhes"]["taxas"]["interacao"], 12.6 / MRdy, delta=1e-6)
        self.assertGreater(abs(12.6 / MRdy - 12.6 / MRdx), 0.1)  # trocar os eixos mudaria a taxa


# ---------------------------------------------------------------- P-Δ só nas ELU, por combinação

class TestPDelta(unittest.TestCase):
    def test_elu_por_combinacao_els_linear(self):
        m = _ex("portico_aco")
        lin, pd = P.calcular(m), P.calcular(m, {"pdelta": True})
        for c in pd.combinacoes:
            r = pd.resultados[c["nome"]]
            if c["estado"] in ("ELU", "ELU_EXC"):
                ref = S.analisar(m, c["fatores"], True, 13)
            else:
                ref = lin.resultados[c["nome"]]
            for no, u in ref.deslocamentos.items():
                np.testing.assert_allclose(r.deslocamentos[no], u, atol=1e-12, err_msg=c["nome"])

    # VC-integracao-002: corrigido (casos que só aparecem em ELU_EXC são analisados antes de combinar)
    def test_pdelta_com_sismo_nao_quebra(self):
        m = _ex("edificio_concreto")
        cfg = P.resumo(m)["config"]["sismo"]
        P.configurar(m, {"sismo": {**cfg, "zona": 2}})
        c = P.calcular(m, {"pdelta": True})
        self.assertTrue(c.estabilidade["gama_z"])


# ---------------------------------------------------------------- concreto: ELS QP/FREQ e flecha relativa à corda

class TestConcretoEls(unittest.TestCase):
    def test_viga_igual_ao_motor(self):
        m = _ex("edificio_concreto")
        c = P.calcular(m, {"barras": ["VX1-0-0"]})
        qp = [x for x in c.combinacoes if x["estado"] == "ELS_QP"]
        self.assertEqual(len(qp), 1)
        self.assertEqual(qp[0]["fatores"], {"G1": 1.0, "Q1": 0.4})       # ψ2 escritório = 0,4
        R = c.resultados
        fl = np.asarray(R[qp[0]["nome"]].flechas["VX1-0-0"], float)    # (x, ux, uy, uz)
        x = fl[:, 0]
        corda = lambda k: fl[0, k] + (fl[-1, k] - fl[0, k]) * (x - x[0]) / (x[-1] - x[0])
        flecha = max(np.abs(fl[:, 2] - corda(2)).max(), np.abs(fl[:, 3] - corda(3)).max())
        freq = [x["nome"] for x in c.combinacoes if x["estado"] == "ELS_FREQ"]
        fq = max(freq, key=lambda n: max(abs(l[5]) for l in R[n].esforcos["VX1-0-0"]))
        elu = [x["nome"] for x in c.combinacoes if x["estado"] in ("ELU", "ELU_EXC")]
        b = m.barras["VX1-0-0"]
        r = nbr6118.verificar_barra_concreto(
            m.secoes[b.secao], m.materiais[b.material],
            [{"combinacao": n, "linhas": R[n].esforcos["VX1-0-0"]} for n in elu],
            {"qp": {"combinacao": qp[0]["nome"], "linhas": R[qp[0]["nome"]].esforcos["VX1-0-0"],
                    "flecha_elastica_m": float(flecha)},
             "freq": {"combinacao": fq, "linhas": R[fq].esforcos["VX1-0-0"]}},
            5.0, "viga", {"fyk_MPa": 500.0, "caa": "II", "cobrimento_m": 0.03})
        linha = c.verificacao[0]
        self.assertAlmostEqual(linha["taxa"], r["taxa_max"], delta=1e-9)
        self.assertEqual(linha["combinacao"], r["combinacao"])
        self.assertGreater(abs(fl[0, 3]), 1e-5)  # a viga desce com os pilares: a corda importa


# ---------------------------------------------------------------- deslocamentos-limite (portico_aco)

class TestDeslocamentosLimite(unittest.TestCase):
    def test_vertical_e_horizontal(self):
        m = _ex("portico_aco")
        c = P.calcular(m)
        v = next(x for x in c.deslocamentos["verticais"] if x["barra"] == "VX1-0-0")
        lim = nbr8800.limite_deslocamento("viga_cobertura", 6.0)
        self.assertAlmostEqual(v["limite_m"], 6.0 / 250, delta=1e-12)      # NBR 8800 C.1: L/250
        nomes = [x["nome"] for x in c.combinacoes if x["estado"] == lim["estado"]]

        def rel(n):
            a = np.asarray(c.resultados[n].flechas["VX1-0-0"], float)
            t = a[:, 0] / a[-1, 0]
            return max(np.abs(a[:, k] - (a[0, k] + (a[-1, k] - a[0, k]) * t)).max() for k in (2, 3))
        n = max(nomes, key=rel)
        self.assertEqual(v["combinacao"], n)
        self.assertAlmostEqual(v["flecha_m"], rel(n), delta=1e-12)
        h = next(x for x in c.deslocamentos["horizontais"] if x["direcao"] == "X")
        self.assertAlmostEqual(h["limite_m"], 4.0 / 300, delta=1e-12)      # 1 pavimento: H/300
        freq_v = [x["nome"] for x in c.combinacoes if x["estado"] == "ELS_FREQ" and "V1" in x["fatores"]]
        ux = {n: abs(c.resultados[n].pavimentos["P1"][0]) for n in freq_v}
        self.assertAlmostEqual(h["valor_m"], max(ux.values()), delta=1e-12)


# ---------------------------------------------------------------- γz e B2 à mão

def _pesos_edificio():
    """Pesos por pavimento à mão (LEIAME): G = 4,0·150 + 85·0,2·0,5·25 + pilares (metade de cima e de baixo);
    Q = 2,5·150."""
    pil = 12 * 3.0 * 0.25 * 0.50 * 25
    G = np.array([600 + 212.5 + pil] * 3 + [600 + 212.5 + pil / 2])
    return G, np.full(4, 375.0)


def _modelo_rigidez_reduzida(m, fck_MPa=30.0):
    """Cópia de `m` com (EI)sec da NBR 6118 15.7.3: 0,4·Eci·Ic nas vigas e 0,8·Eci·Ic nos pilares, pela redução de
    Iy e Iz das seções (EA e GJ intactos). O material guarda Ecs = αi·Eci, αi = 0,8 + 0,2·fck/80 ≤ 1 (8.2.8);
    portanto I_ef = fator·Eci/Ecs·I = fator/αi · I."""
    alfa_i = min(0.8 + 0.2 * fck_MPa / 80, 1.0)
    out = copy.deepcopy(m)
    for b in out.barras.values():
        f = (0.8 if b.id.startswith("P") else 0.4) / alfa_i
        sec = copy.deepcopy(m.secoes[b.secao])
        sec.nome, sec.Iy, sec.Iz = f"{b.secao}_{b.id}", f * sec.Iy, f * sec.Iz
        out.secoes[sec.nome] = sec
        b.secao = sec.nome
    return out


class TestGamaZ(unittest.TestCase):
    """edificio_concreto: γz por direção = maior das combinações ELU com força horizontal (15.5.3), com a
    rigidez reduzida da 15.7.3 (VC-integracao-003)."""

    @classmethod
    def setUpClass(cls):
        cls.m = _ex("edificio_concreto")
        cls.c = P.calcular(cls.m)
        cls.acoes = P.gerar_acoes(cls.m)
        cls.red = _modelo_rigidez_reduzida(cls.m)

    def _gz(self, modelo, f, k):
        G, Q = _pesos_edificio()
        P_ = f.get("G1", 0) * G + f.get("Q1", 0) * Q
        z = np.array([3.0, 6.0, 9.0, 12.0])
        H = sum(v * np.array([x["F_vet_kN"][k] for x in self.acoes["forcas"][cs]])
                for cs, v in f.items() if cs in self.acoes["forcas"])
        r = S.analisar(modelo, f, False, 13)
        d = np.array([r.pavimentos[p][k] for p in ("P1", "P2", "P3", "P4")])
        return 1 / (1 - abs(P_ @ d) / abs(H @ z))

    def test_gama_z_a_mao(self):
        for d, k in (("X", 0), ("Y", 1)):
            g = self.c.estabilidade["gama_z"][d]
            f = _comb(self.c, g["combinacao"])["fatores"]
            self.assertAlmostEqual(g["valor"], self._gz(self.red, f, k), delta=1e-9)
            todos = [self._gz(self.red, x["fatores"], k) for x in self.c.combinacoes
                     if x["estado"] == "ELU" and any(c.startswith(f"V+{d}") or c.startswith(f"V-{d}")
                                                     for c in x["fatores"])]
            self.assertAlmostEqual(g["valor"], max(todos), delta=1e-9)

    def test_gama_z_com_nao_linearidade_fisica(self):
        """Rigidez nominal (Ecs·Ic cheio) subestimava γz − 1 em 31 %: a diferença tem de aparecer."""
        g = self.c.estabilidade["gama_z"]["Y"]
        f = _comb(self.c, g["combinacao"])["fatores"]
        self.assertAlmostEqual(g["valor"], self._gz(self.red, f, 1), delta=1e-9)
        self.assertGreater(g["valor"] - 1, 1.25 * (self._gz(self.m, f, 1) - 1))


class TestBase(unittest.TestCase):
    """O mesmo prédio com a base em z = 0 e em z = 3 m e forças horizontais manuais iguais: γz, B2 e o
    limite H/1700 do topo têm de ser iguais (braço de alavanca e H medidos a partir da base)."""

    def _modelo(self, z0):
        m = P.novo_modelo("base")
        P.gerar_estrutura(m, {"tipo": "edificio", "material": {"concreto": 30},
                              "secoes": {"pilar": {"forma": "retangular", "b": 0.5, "h": 0.25},
                                         "viga": {"forma": "retangular", "b": 0.2, "h": 0.5}},
                              "vaos_x": [5.0] * 2, "vaos_y": [5.0], "alturas": [3.0] * 4, "origem": [0, 0, z0]})
        P.adicionar(m, "caso", {"nome": "V1", "tipo": "V"})
        for pv in m.pavimentos.values():
            nos = [n for n in m.nos.values() if abs(n.z - pv.z) < 1e-6]
            for n in nos:
                P.adicionar(m, "carga_nodal", {"caso": "V1", "no": n.id, "F": [20.0 / len(nos), 0, 0, 0, 0, 0]})
        for b in list(m.barras.values()):
            if b.id.startswith("V"):
                P.adicionar(m, "carga_distribuida", {"caso": "Q1", "barra": b.id, "q": [0, 0, -10.0]})
        return m

    # VC-integracao-004: corrigido (cota da base = menor z dos nós apoiados)
    def test_invariancia_com_a_cota_da_base(self):
        a, b = P.calcular(self._modelo(0.0)), P.calcular(self._modelo(3.0))
        self.assertAlmostEqual(a.estabilidade["gama_z"]["X"]["valor"], b.estabilidade["gama_z"]["X"]["valor"],
                               delta=1e-6)
        ta = next(x for x in a.deslocamentos["horizontais"] if x["tipo"] == "topo" and x["direcao"] == "X")
        tb = next(x for x in b.deslocamentos["horizontais"] if x["tipo"] == "topo" and x["direcao"] == "X")
        self.assertAlmostEqual(ta["limite_m"], tb["limite_m"], delta=1e-9)


class TestB2(unittest.TestCase):
    """Edifício de aço 2×1 vãos × 3 pavimentos, vento manual: B2 por pavimento à mão (NBR 8800 D.2.3,
    Rs = 0,85): ΣN = cargas gravitacionais do andar e acima; Δh relativo; ΣH = cortante do andar."""

    def test_b2_por_pavimento(self):
        m = P.novo_modelo("b2")
        r = P.gerar_estrutura(m, {"tipo": "edificio", "material": {"aco": "A-572 GR.50"},
                                  "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}},
                                  "vaos_x": [6.0] * 2, "vaos_y": [6.0], "alturas": [4.0, 3.0, 3.0]})
        for b in r["ids"]["barras"]:
            if b.startswith("V"):
                P.adicionar(m, "carga_distribuida", {"caso": "Q1", "barra": b, "q": [0, 0, -12.0]})
        P.adicionar(m, "caso", {"nome": "V1", "tipo": "V"})
        Hpav = [10.0, 12.0, 8.0]
        pavs = sorted(m.pavimentos.values(), key=lambda p: p.z)
        for F, pv in zip(Hpav, pavs):
            nos = [n for n in m.nos.values() if abs(n.z - pv.z) < 1e-6]
            for n in nos:
                P.adicionar(m, "carga_nodal", {"caso": "V1", "no": n.id, "F": [F / len(nos), 0, 0, 0, 0, 0]})
        c = P.calcular(m)
        B2 = c.estabilidade["B2"]["X"]
        f = _comb(c, B2["combinacao"])["fatores"]
        lin = S.analisar(m, f, False, 13)
        pesos = S.pesos_nodais(m, f)
        Ppav = [sum(w for no, w in pesos.items() if abs(m.nos[no].z - pv.z) < 1e-6) for pv in pavs]
        d = [lin.pavimentos[pv.nome][0] for pv in pavs]
        h = [4.0, 3.0, 3.0]
        esperado = []
        for i in range(3):
            sN, sH = sum(Ppav[i:]), f["V1"] * sum(Hpav[i:])
            dh = abs(d[i] - (d[i - 1] if i else 0.0))
            esperado.append(1 / (1 - (1 / 0.85) * (dh / h[i]) * (sN / sH)))
        por = {x["pavimento"]: x for x in B2["por_pavimento"]}
        # a combinação de cada pavimento pode ser outra; a do máximo tem de bater
        i = [pv.nome for pv in pavs].index(B2["pavimento"])
        self.assertAlmostEqual(B2["max"], esperado[i], delta=1e-9)
        self.assertEqual(B2["max"], max(x["B2"] for x in por.values()))


# ---------------------------------------------------------------- gerar_acoes

class TestGerarAcoes(unittest.TestCase):
    def test_soma_por_pavimento_e_sem_duplicata(self):
        m = _ex("edificio_concreto")
        a1 = P.gerar_acoes(m)
        n1 = (len(m.cargas_nodais), sorted(m.casos))
        a2 = P.gerar_acoes(m)
        self.assertEqual((len(m.cargas_nodais), sorted(m.casos)), n1)
        self.assertEqual(len({c.id for c in m.cargas_nodais}), len(m.cargas_nodais))
        cfg = P.resumo(m)["config"]["vento"]
        ref = vento.casos_vento(cfg, [(p.nome, p.z) for p in sorted(m.pavimentos.values(), key=lambda p: p.z)],
                                cfg["dimensoes"])
        for r in ref:
            for f in r["forcas"]:
                s = np.zeros(2)
                for c in m.cargas_nodais:
                    if c.caso == r["caso"] and abs(m.nos[c.no].z - f["z"]) < 1e-6:
                        s += c.F[:2]
                np.testing.assert_allclose(s, f["F_vet_kN"][:2], atol=1e-9)
        self.assertEqual(a1["forcas"], a2["forcas"])


# ---------------------------------------------------------------- envoltória, normas

class TestEnvoltoriaNormas(unittest.TestCase):
    def test_envoltoria_reacoes(self):
        c = P.calcular(_ex("portico_aco"))
        elu = [x["nome"] for x in c.combinacoes if x["estado"] in ("ELU", "ELU_EXC")]
        e = c.reacoes_envoltoria["N0-0-0"]
        for k in range(6):
            v = {n: c.resultados[n].reacoes["N0-0-0"][k] for n in elu}
            self.assertAlmostEqual(e["max"][k], max(v.values()), delta=1e-12)
            self.assertAlmostEqual(v[e["comb_max"][k]], e["max"][k], delta=1e-12)
            self.assertAlmostEqual(v[e["comb_min"][k]], e["min"][k], delta=1e-12)

    def test_normas_refletem_edicao_do_modulo(self):
        import importlib
        for linha in P.normas_implementadas():
            mod = importlib.import_module(f"prossen_estrutural.{linha['modulo']}")
            ano = re.search(r":(\d{4})", mod.EDICAO).group(1)
            if linha["norma"] in mod.EDICAO and linha["edicao"] != ano:
                # item mantido na edição anterior: só se declarado na limitação (ESPEC §2)
                self.assertIn(linha["edicao"] if linha["edicao"] in linha["limitacao"] else ano,
                              linha["limitacao"], linha)
        mods = {l["modulo"] for l in P.normas_implementadas()}
        self.assertIn("aco.ligacoes", mods)
        c = P.calcular(_ex("portico_aco"))
        for n in c.normas:
            mod = importlib.import_module(f"prossen_estrutural.{n['modulo']}")
            self.assertEqual(f"{n['norma']}:{n['edicao']}", re.match(r"NBR \d+:\d{4}", mod.EDICAO).group(0))


# ---------------------------------------------------------------- gerador e exemplos

class TestGerador(unittest.TestCase):
    def test_contagens(self):
        """Fórmulas: pórtico (nv+1)(np+1) nós e (nv+1)·np + nv·np barras; edifício (nx+1)(ny+1)(np+1) nós,
        (nx+1)(ny+1)·np pilares + np·[nx(ny+1) + ny(nx+1)] vigas; galpão 5(nm+1) nós, 4(nm+1) + 3nm barras."""
        casos = [({"tipo": "portico_plano", "vaos_x": [5, 6, 4], "alturas": [3, 3]}, 12, 14, 2),
                 ({"tipo": "edificio", "vaos_x": [5, 5], "vaos_y": [4, 4, 4], "alturas": [3, 3]}, 36, 24 + 2 * 17, 2),
                 ({"tipo": "galpao", "vao": 20, "altura_pilar": 6, "altura_cumeeira": 8, "n_modulos": 4,
                   "espacamento": 6}, 25, 20 + 12, 0)]
        for dados, nn, nb, npv in casos:
            m = P.novo_modelo("g")
            sec = {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}}
            if dados["tipo"] == "galpao":
                sec["terca"] = {"perfil": "Ue 200x75x25x3,00"}
            r = P.gerar_estrutura(m, {**dados, "material": {"aco": "A-572 GR.50"}, "secoes": sec})
            self.assertEqual((r["nos_criados"], r["barras_criadas"], r["pavimentos_criados"]), (nn, nb, npv), dados)

    def test_geometria_galpao(self):
        m = P.novo_modelo("g")
        P.gerar_estrutura(m, {"tipo": "galpao", "vao": 20, "altura_pilar": 6, "altura_cumeeira": 8, "n_modulos": 2,
                              "espacamento": 6, "material": {"aco": "A-572 GR.50"},
                              "secoes": {"pilar": {"perfil": "W310x38.7"}, "viga": {"perfil": "W360x51"}}})
        zs = sorted({round(n.z, 9) for n in m.nos.values()})
        self.assertEqual(zs, [0.0, 6.0, 8.0])
        ys = sorted({round(n.y, 9) for n in m.nos.values()})
        self.assertEqual(ys, [0.0, 6.0, 12.0])
        xs_cumeeira = {round(n.x, 9) for n in m.nos.values() if abs(n.z - 8) < 1e-9}
        self.assertEqual(xs_cumeeira, {10.0})

    def test_exemplos_conferem_com_o_leiame(self):
        alvo = {("viga_biapoiada", "Q1"): 60.0, ("portico_aco", "Q1"): 60.0, ("portico_aco", "G1"): 106.58,
                ("galpao_aco", "Q1"): 150.0, ("edificio_concreto", "G1"): 3700.0, ("edificio_concreto", "Q1"): 1500.0}
        for (ex, caso), Rz in alvo.items():
            r = P.analisar_caso(_ex(ex), caso)
            self.assertAlmostEqual(sum(v[2] for v in r["reacoes"].values()), Rz, delta=0.01, msg=(ex, caso))

    def test_galpao_peso_proprio_leiame(self):
        """VC-integracao-005: o LEIAME traz o ΣRz de G1 do galpão (12 pilares, 6 pórticos × 2); confere com o modelo."""
        r = P.analisar_caso(_ex("galpao_aco"), "G1")
        texto = (EX / "LEIAME.md").read_text(encoding="utf-8")
        g1 = next(l for l in texto.splitlines() if l.startswith("- G1: só peso próprio"))
        valor = float(re.search(r"\*\*([\d ,]+) kN\*\*", g1).group(1).replace(" ", "").replace(",", "."))
        self.assertIn("12 pilares", g1)
        self.assertAlmostEqual(sum(v[2] for v in r["reacoes"].values()), valor, delta=0.01)

    def test_galpao_peso_proprio_a_mao(self):
        m = _ex("galpao_aco")
        peso_esperado = 0.0
        for b in m.barras.values():
            L = math.dist(*[(m.nos[n].x, m.nos[n].y, m.nos[n].z) for n in (b.ni, b.nj)])
            # Soma manual A × L × γ de todas as barras, inclusive DC e DP.
            peso_esperado += m.secoes[b.secao].A * L * m.materiais[b.material].peso
        tipos = {}
        for b in m.barras.values():
            tipos[b.id[0]] = tipos.get(b.id[0], 0) + 1
        self.assertEqual(tipos, {"P": 12, "V": 12, "T": 15, "D": 8})
        r = P.analisar_caso(m, "G1")
        self.assertAlmostEqual(sum(v[2] for v in r["reacoes"].values()), peso_esperado, delta=1e-6)


# ---------------------------------------------------------------- relatório

class TestRelatorio(unittest.TestCase):
    def test_conteudo_e_numeros_ptbr(self):
        m = _ex("edificio_concreto")
        c = P.calcular(m)
        d = c.to_dict()
        md, ht = relatorio.markdown(P.para_dict(m), d), relatorio.html(P.para_dict(m), d)
        for txt in (md, ht):
            self.assertIn("Ferramenta de apoio", txt)
            self.assertIn("engenheiro responsável (ART)", txt)
            for n in d["normas"]:
                self.assertIn(n["norma"], txt)
                self.assertIn(n["edicao"], txt)
        sec6 = md.split("## 6.")[1].split("\n## ")[0]
        linhas = {l.split("|")[1].strip(): l for l in sec6.splitlines() if l.startswith("| ")}
        for v in d["verificacao"]:
            self.assertIn(v["barra"], linhas, v["barra"])
        amostra = d["verificacao"][::max(1, len(d["verificacao"]) // 10)][:10]
        self.assertEqual(len(amostra), 10)
        for v in amostra:
            esperado = f"{v['taxa']:.2f}".replace(".", ",")
            if v["taxa"] > 1 and esperado == "1,00":
                esperado = f"{v['taxa']:.3f}".replace(".", ",")
            self.assertIn(f"| {esperado} |", linhas[v["barra"]], v["barra"])
        gz = d["estabilidade"]["gama_z"]["Y"]["valor"]
        self.assertIn(f"{gz:.3f}".replace(".", ","), md)


if __name__ == "__main__":
    unittest.main()
