"""Regressão — lógica do CYPE 2016 × PROSSEN em ações, combinações, sismo e estabilidade global.

Relatório: `verificacao/logica-cype-acoes-estabilidade.md` (ids VC-logica-acoes-NNN). Valores esperados da
norma (item no docstring) ou da memória independente `verificacao/memorias/logica-cype-acoes.py` (só stdlib),
nunca da saída do código. `expectedFailure` = divergência em que o CYPE segue a norma e nós não; o dono
remove o decorador ao corrigir. Os demais testes passam e protegem pontos em que estamos certos e o CYPE não.
"""
import importlib.util
import pathlib
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural.acoes import combinacoes, sismo, vento
from prossen_estrutural.concreto import nbr6118
from prossen_estrutural.modelo import CasoCarga

_RAIZ = pathlib.Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("logica_cype_acoes",
                                               _RAIZ / "verificacao" / "memorias" / "logica-cype-acoes.py")
mem = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mem)

COTAS = [3.0, 6.0, 9.0, 12.0]


def _casos(*defs):
    return {n: CasoCarga(n, t, s) for n, t, s in defs}


def _tem(lista, fatores, estado):
    alvo = {k: round(v, 6) for k, v in fatores.items()}
    return any(e == estado and {k: round(v, 6) for k, v in f.items()} == alvo for _, f, e in lista)


# ---------------------------------------------------------------- combinações

class TestCombinacoes(unittest.TestCase):
    EDIF = (("G1", "G", ""), ("G2", "G", ""), ("Q1", "Q", "residencial"),
            ("V+X", "V", "vento"), ("V-X", "V", "vento"), ("V+Y", "V", "vento"), ("V-Y", "V", "vento"))

    def test_coeficientes_iguais_ao_cype_6118(self):
        """NBR 8681:2003 tab. 2/5/6 = NBR 6118 tab. 11.1/11.2 = nb1!nb03comb do CYPE: γg 1,4, γq = γw = 1,4,
        ψ0 residencial 0,5, ψ0 vento 0,6 → 'V+X princ. + Q1' = {G 1,4; Q 0,7; V 1,4} e 'Q1 princ. + V+X' =
        {G 1,4; Q 1,4; V 0,84}."""
        L = combinacoes.gerar_combinacoes(_casos(*self.EDIF))
        self.assertTrue(_tem(L, {"G1": 1.4, "G2": 1.4, "Q1": 0.7, "V+X": 1.4}, "ELU"))
        self.assertTrue(_tem(L, {"G1": 1.4, "G2": 1.4, "Q1": 1.4, "V+X": 0.84}, "ELU"))
        self.assertTrue(_tem(L, {"G1": 1.0, "G2": 1.0, "V+X": 1.4}, "ELU"))  # sucção/tombamento

    def test_G_favoravel_com_Q_principal(self):
        """VC-logica-acoes-002: NBR 8681:2003 5.1.3.1 e tab. 2 — cada permanente entra com γg favorável (1,0)
        ou desfavorável (1,4), o que for pior. O CYPE (gestcomp, família permanente com γfav e γdesf) gera
        as duas em toda combinação; nós só usamos 1,0 com vento principal. Falta {G 1,0; Q 1,4}."""
        L = combinacoes.gerar_combinacoes(_casos(("G1", "G", ""), ("Q1", "Q", "residencial")))
        self.assertTrue(_tem(L, {"G1": 1.0, "Q1": 1.4}, "ELU"))

    def test_contagem_elu_igual_ao_cype(self):
        """VC-logica-acoes-002: contagem pela regra do CYPE (memória: G fav/desf × [só G + Q princ.(V aus./4 acomp.)
        + 4 V princ.(Q aus./acomp.)]) = 2 × 14 = 28 ELU normais; hoje 22."""
        L = combinacoes.gerar_combinacoes(_casos(*self.EDIF))
        self.assertEqual(sum(e == "ELU" for *_, e in L), mem.contagem_cype(2, [1, 4]))

    def test_sismo_sem_Q(self):
        """VC-logica-acoes-003: NBR 8681:2003 5.1.3.3 (ações variáveis só quando desfavoráveis) — a combinação
        sísmica também precisa de Q ausente (tração/tombamento com G favorável). O CYPE gera (família
        variável com γfav = 0); nós sempre somamos ψ2·Q. Falta {G 1,0; SX 1,0}."""
        L = combinacoes.gerar_combinacoes(_casos(("G1", "G", ""), ("Q1", "Q", "residencial"),
                                                 ("SX", "S", ""), ("SY", "S", "")))
        self.assertTrue(_tem(L, {"G1": 1.0, "SX": 1.0}, "ELU_EXC"))


# ---------------------------------------------------------------- vento

class TestVento(unittest.TestCase):
    def test_S2_abaixo_de_5m_usa_tabela(self):
        """VC-logica-acoes-006 (protege; CYPE diverge): NBR 6123 tab. 2, cat. IV classe A, z ≤ 5 m → 0,79.
        O caminho veviento → nbr.dll do CYPE usa a cota real (z = 3 m → 0,744, −6% em V, −11% em q)."""
        self.assertAlmostEqual(vento.s2("IV", "A", 3.0), 0.79, delta=0.0051)
        self.assertAlmostEqual(mem.s2("IV", "A", 3.0, piso_5m=False), 0.7443, places=4)

    def test_forca_por_pavimento(self):
        """NBR 6123 4.2.3/6.3: F = Ca·q(z)·l1·h_infl, mesma forma do CYPE (vedatnbr: q·(meias alturas)·largura).
        Caso 1 do relatório (V0 40, IV-A, Ca 1,2, fachada 12 m): memória [26,53; 27,72; 30,55; 16,37] kN."""
        cfg = {"V0": 40.0, "categoria": "IV", "classe": "A", "Ca": 1.2, "direcoes": ["+X"]}
        r = vento.casos_vento(cfg, [(f"P{i + 1}", z) for i, z in enumerate(COTAS)], {"lx": 20.0, "ly": 12.0})[0]
        esperado = mem.forcas_pavimento(COTAS, 12.0, 1.2, 40.0, "IV", "A")
        for f, e in zip(r["forcas"], esperado):
            self.assertAlmostEqual(f["F_kN"], e, delta=0.01 * e)

    def test_galpao_cpe_declarado(self):
        """VC-logica-acoes-004: galpões precisam de Cpe/Cpi de paredes e cobertura (NBR 6123 tab. 4 e 5; o CYPE
        tem as tabelas em nbr.dll, nbrvient_cargas_portico_*). Nosso vento é só arrasto por pavimento (sem
        sucção na cobertura: no caso do relatório ≈ 61 kN de sucção por pórtico ignorados). Mínimo: declarar
        a limitação na COBERTURA do módulo."""
        txt = " ".join(c["limitacao"] + c["verificacao"] for c in vento.COBERTURA).lower()
        self.assertTrue("cpe" in txt or "galp" in txt or "cobertura" in txt and "sucção" in txt)


# ---------------------------------------------------------------- sismo

class TestSismo(unittest.TestCase):
    def test_caso1_forcas(self):
        """NBR 15421:2006 9.1–9.3 (= CYPE sismobrasil + frontlib): ag 0,10 g, terreno C, R 3, I 1, hn 12 m →
        Ta = 0,4362 s, Cs = 0,10, k = 1, F = [82,56; 165,12; 247,68; 330,24] kN (W = 2064 kN/pav.)."""
        F = sismo.forcas_equivalentes([2064.0] * 4, COTAS, 2, "C", 3.0)
        for f, e in zip(F, [82.56, 165.12, 247.68, 330.24]):
            self.assertAlmostEqual(f, e, places=2)

    def test_T_limitado_a_Cup_Ta(self):
        """VC-logica-acoes-007 (protege; CYPE diverge): NBR 15421:2006 9.2 — T ≤ Cup·Ta (zona 2: 1,7). Com T modal
        1,2 s: T = 0,7415 s, Cs = 0,07642, k = 1,1207. O CYPE usa o T digitado sem limite (Cs = 0,0472, −38%)."""
        p = sismo.parametros_sismicos(COTAS, 2, "C", 3.0, 1.0, "concreto_portico", 1.2, 0.10)
        self.assertAlmostEqual(p["T"], 1.7 * mem.ta(12.0), places=4)
        self.assertAlmostEqual(p["Cs"], mem.cs_norma(0.10, "C", 3, 1, 1.7 * mem.ta(12.0)), places=5)
        self.assertAlmostEqual(p["k"], mem.k_dist(1.7 * mem.ta(12.0)), places=4)

    def test_Cs_minimo(self):
        """VC-logica-acoes-008 (protege; CYPE diverge): NBR 15421:2006 9.1 — Cs ≥ 0,01. ag 0,05 g, terreno A,
        R 8, T 2 s: 0,01 (o CYPE daria 0,0025: não há o piso no código brasileiro)."""
        self.assertAlmostEqual(sismo.coef_sismico(0.05, "A", 8.0, 1.0, 2.0), 0.01, places=6)


# ---------------------------------------------------------------- γz

class TestGamaZ(unittest.TestCase):
    def test_formula_igual_ao_cype(self):
        """NBR 6118 15.5.3 × CYPE (razões ΔM/M1 por hipótese, θ = Σγg·r): com δ só do vento os dois coincidem.
        Memória: combinação V+X princ. + Q acomp. (γ 1,4 e 0,98)."""
        H = [26.5, 27.7, 30.6, 16.4]
        dv = [0.002, 0.005, 0.008, 0.011]
        PG, PQ = [1920.0] * 4, [480.0] * 4
        pav = [{"z_m": z, "H_kN": 1.4 * h, "P_kN": 1.4 * g + 0.98 * q, "delta_m": 1.4 * d}
               for z, h, g, q, d in zip(COTAS, H, PG, PQ, dv)]
        self.assertAlmostEqual(nbr6118.gama_z(pav)["gama_z"], mem.gama_z_cype([1.4, 0.98], [PG, PQ], COTAS, H, dv),
                               places=9)

    def test_nos_moveis_sem_pdelta_amplifica_ou_avisa(self):  # VC-logica-acoes-001 corrigido (projeto._amplificar)
        """VC-logica-acoes-001: NBR 6118 15.4.2/15.7.2 — com 1,1 < γz ≤ 1,3 os efeitos globais de 2ª ordem são
        obrigatórios (majorar as ações horizontais por 0,95·γz ou análise de 2ª ordem). O CYPE multiplica o
        coeficiente do vento de cada combinação por γz (combs!fr_amplifica_factores). Nós, com pdelta=False,
        só informamos γz. Exemplo edificio_concreto com E × 0,35: γz(Y) ≈ 1,146. Aceita a correção mínima
        (aviso explícito de 2ª ordem não aplicada) ou a majoração."""
        m = P.carregar(_RAIZ / "exemplos" / "edificio_concreto.json")
        for mat in m.materiais.values():
            mat.E *= 0.35
        c = P.calcular(m, {"pdelta": False})
        gz = c.estabilidade["gama_z"]["Y"]
        self.assertTrue(1.1 < gz["valor"] <= 1.3)
        textos = [a.get("mensagem", "") for a in c.avisos] + list(c.estabilidade.get("avisos", []))
        self.assertTrue(any("2ª ordem" in t or "0,95" in t for t in textos), textos)


if __name__ == "__main__":
    unittest.main()
