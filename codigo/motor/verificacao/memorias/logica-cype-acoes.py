"""Memória independente — lógica do CYPE 2016 (ações, combinações, sismo, γz) × lógica da norma/PROSSEN,
recalculada à mão. Só stdlib; NÃO importa prossen_estrutural. Unidades: kN, m, kN/m², s.

Regras do CYPE descritas em linguagem de engenharia (relatório software/verificacao/
logica-cype-acoes-estabilidade.md, seção 1); constantes conferidas na .rdata das DLLs (nbr.dll,
sismo.dll, frontlib.dll) e cada valor tem item de norma. Nenhum trecho de pseudocódigo foi transcrito.

Rodar: python logica-cype-acoes.py  (imprime os casos numéricos do relatório, seção 3)
"""
import math

G_ACEL = 9.80665  # kN por tf (conversão do q do CYPE, que sai em tf/m²)

# ---------------------------------------------------------------- vento (NBR 6123)
# Tabela 1 da NBR 6123 (b, p) e Fr; categoria IV classe A e III classe B (iguais no CYPE, bloco 13)
B_P = {("IV", "A"): (0.86, 1.00, 0.12), ("III", "B"): (0.94, 0.98, 0.105)}


def s2(cat, cls, z, piso_5m=True):
    """5.3.3: S2 = b·Fr·(z/10)^p. piso_5m: a tabela 2 da NBR 6123 começa em z ≤ 5 m (valor de 5 m);
    o caminho veviento → nbr.dll do CYPE usa a cota sem esse piso (piso_5m=False)."""
    b, fr, p = B_P[(cat, cls)]
    zz = max(z, 5.0) if piso_5m else z
    return b * fr * (zz / 10.0) ** p


def q_norma(vk):
    """4.2: q = 0,613·Vk² (N/m²) → kN/m²."""
    return 0.613 * vk ** 2 / 1000.0


def q_cype(vk, kn_por_tf=G_ACEL):
    """CYPE (nbr.dll): q = Vk²/16,3/1000 em tf/m² → kN/m² (conversão tf→kN não lida: 9,80665 ou 10)."""
    return vk ** 2 / 16.3 / 1000.0 * kn_por_tf


def forcas_pavimento(cotas, largura, Ca, V0, cat, cls, S1=1.0, S3=1.0, cype=False):
    """F_i = Ca·q(z_i)·largura·(meia altura abaixo + meia altura acima); topo: só a de baixo.
    CYPE (vedatnbr): mesma forma (q·soma das meias alturas·largura), Ca digitado pelo usuário e S1²
    aplicado como coeficiente da hipótese; z sem piso de 5 m; q pelo 16,3."""
    out = []
    for k, z in enumerate(cotas):
        abaixo = z - (cotas[k - 1] if k else 0.0)
        acima = (cotas[k + 1] - z) if k + 1 < len(cotas) else 0.0
        vk = V0 * S1 * s2(cat, cls, z, piso_5m=not cype) * S3
        q = q_cype(vk) if cype else q_norma(vk)
        out.append(Ca * q * largura * (abaixo + acima) / 2)
    return out


# ---------------------------------------------------------------- sismo (NBR 15421:2006)
CA_CV = {"A": ((0.8, 0.8), (0.8, 0.8)), "C": ((1.2, 1.2), (1.7, 1.7))}  # tabela 3 (ag ≤ 0,10 / ≥ 0,15)


def coef_terreno(ag, cls):
    (a1, a2), (v1, v2) = CA_CV[cls]
    t = min(max((ag - 0.10) / 0.05, 0.0), 1.0)
    return a1 + (a2 - a1) * t, v1 + (v2 - v1) * t


def ta(hn, ct=0.0466, x=0.9):
    """9.2: Ta = Ct·hn^x (pórtico de concreto: 0,0466; 0,9)."""
    return ct * hn ** x


def cs_norma(ag, cls, R, I, T):
    """9.1: Cs = 2,5·ags0/(R/I) ≤ ags1/(T·R/I) e Cs ≥ 0,01."""
    ca, cv = coef_terreno(ag, cls)
    return max(min(2.5 * ca * ag / (R / I), cv * ag / (T * R / I)), 0.01)


def cs_cype(ag, cls, R, I, T):
    """CYPE (sismo.dll, estático): mesmo patamar/ramo 1/T com fator min(I/R, 1); sem o piso 0,01
    (constante ausente no código brasileiro) e com T = Ta ou o T digitado (sem limite Cup·Ta)."""
    ca, cv = coef_terreno(ag, cls)
    f = min(I / R, 1.0)
    t1 = 0.4 * cv / ca
    return (cv * ag / T if T > t1 else 2.5 * ca * ag) * f


def k_dist(T):
    """9.3: k = 1 (T ≤ 0,5 s); 2 (T ≥ 2,5 s); 0,75 + 0,5·T entre eles (mesma reta no CYPE)."""
    return 1.0 if T <= 0.5 else 2.0 if T >= 2.5 else 0.75 + 0.5 * T


def forcas_sismo(pesos, cotas, cs, k):
    """9.3: Fx = Cvx·Cs·W, Cvx = wx·hx^k/Σ wi·hi^k (CYPE frontlib: mesma regra, força de topo = 0)."""
    H = cs * sum(pesos)
    s = [w * h ** k for w, h in zip(pesos, cotas)]
    return [H * x / sum(s) for x in s]


# ---------------------------------------------------------------- γz (NBR 6118 15.5.3)
def gama_z(H, P, z, delta):
    """γz = 1/(1 − ΣP·δ/ΣH·z), todos de cálculo (mesma combinação)."""
    return 1.0 / (1.0 - sum(p * d for p, d in zip(P, delta)) / sum(h * zz for h, zz in zip(H, z)))


def gama_z_cype(gamas_grav, P_carac, z, H_vento_carac, delta_vento_carac):
    """CYPE (combs + elements): por hipótese de vento w e gravitacional g, r_gw = ΣN_g·Δ_w/ΣV_w·h
    (= ΣP_g·δ_w/ΣH_w·z); na combinação, θ = Σ γ_g·r_gw e o coeficiente do vento é multiplicado por
    1/(1 − θ). O γ do vento se cancela (δ e H são proporcionais a ele)."""
    M1 = sum(h * zz for h, zz in zip(H_vento_carac, z))
    th = sum(g * sum(p * d for p, d in zip(Pg, delta_vento_carac)) / M1 for g, Pg in zip(gamas_grav, P_carac))
    return 1.0 / (1.0 - th)


# ---------------------------------------------------------------- combinações (contagem)
def contagem_cype(n_g_opcoes, familias_var):
    """Regra do CYPE (gestcomp/gccombs, situação persistente): permanentes com γ favorável E desfavorável
    em todas as combinações; cada família variável: ausente, principal (γ·ψp = γ) ou acompanhamento
    (γ·ψ0); no máximo uma família principal e acompanhamento só com principal. Família com hipóteses
    incompatíveis (vento ±X ±Y) escolhe uma hipótese. familias_var = [nº de hipóteses incompatíveis]."""
    total = 1  # só permanentes
    for i, ni in enumerate(familias_var):
        resto = 1
        for j, nj in enumerate(familias_var):
            if j != i:
                resto *= 1 + nj  # ausente ou uma hipótese em acompanhamento
        total += ni * resto
    return n_g_opcoes * total


def contagem_prossen(familias_var):
    """Regra da nossa combinacoes.py (lida no código, não norma): G desfavorável sempre; G favorável só com
    vento principal. Famílias como acima; a 1ª família é Q, as demais vento."""
    total = 1
    for i, ni in enumerate(familias_var):
        resto = 1
        for j, nj in enumerate(familias_var):
            if j != i:
                resto *= 1 + nj
        total += ni * resto * (2 if i > 0 else 1)
    return total


if __name__ == "__main__":
    cotas = [3.0, 6.0, 9.0, 12.0]
    print("== Caso 1: edifício 4 pav., 20 m (X) × 12 m (Y), V0 = 40 m/s, cat. IV, classe A, S1 = S3 = 1, Ca = 1,2")
    for nome, larg in (("V+X (fachada 12 m)", 12.0), ("V+Y (fachada 20 m)", 20.0)):
        fn = forcas_pavimento(cotas, larg, 1.2, 40.0, "IV", "A")
        fc = forcas_pavimento(cotas, larg, 1.2, 40.0, "IV", "A", cype=True)
        print(f"  {nome}: norma/PROSSEN {[round(f, 2) for f in fn]} Σ={sum(fn):.2f} | "
              f"CYPE {[round(f, 2) for f in fc]} Σ={sum(fc):.2f}")
    print(f"  S2(z=3): tabela 2 = 0,79; com piso de 5 m {s2('IV', 'A', 3):.4f}; CYPE {s2('IV', 'A', 3, False):.4f}")
    print(f"  q(Vk=40): 0,613 → {q_norma(40):.4f}; CYPE 16,3 (9,80665) → {q_cype(40):.4f}; "
          f"(10) → {q_cype(40, 10.0):.4f} kN/m²")
    print("== Caso 1 sismo: ag = 0,10 g, terreno C, R = 3, I = 1, W = 2064 kN/pav.")
    T = ta(12.0)
    cs = cs_norma(0.10, "C", 3, 1.0, T)
    print(f"  Ta = {T:.4f} s, Cs = {cs:.4f}, k = {k_dist(T)}, F = {[round(f, 2) for f in forcas_sismo([2064] * 4, cotas, cs, k_dist(T))]}")
    Tu = min(1.2, 1.7 * T)
    print(f"  T modal 1,2 s: norma T = min(1,2; Cup·Ta = {1.7 * T:.4f}) → Cs = {cs_norma(0.10, 'C', 3, 1, Tu):.5f}, "
          f"k = {k_dist(Tu):.4f}; CYPE T = 1,2 → Cs = {cs_cype(0.10, 'C', 3, 1, 1.2):.5f}, k = {k_dist(1.2):.4f}")
    print(f"  Cs mínimo: ag 0,05 g, terreno A, R = 8, T = 2 s → norma {cs_norma(0.05, 'A', 8, 1, 2.0):.4f}, "
          f"CYPE {cs_cype(0.05, 'A', 8, 1, 2.0):.4f}")
    print("== Combinações ELU normais (G1, G2, Q1, V±X, V±Y)")
    print(f"  CYPE {contagem_cype(2, [1, 4])} · PROSSEN {contagem_prossen([1, 4])}")
    print("== Galpão (Q cobertura, V0°, V90°)")
    print(f"  CYPE {contagem_cype(2, [1, 2])} · PROSSEN {contagem_prossen([1, 2])}")
    # γz: 4 pavimentos, combinação V+X princ. + Q acomp., G desfavorável
    H = [26.5, 27.7, 30.6, 16.4]                 # kN, vento característico por pavimento
    dv = [0.002, 0.005, 0.008, 0.011]            # m, deslocamento do vento característico (1ª ordem)
    PG, PQ = [1920.0] * 4, [480.0] * 4
    gz_n = gama_z([1.4 * h for h in H], [1.4 * g + 0.98 * q for g, q in zip(PG, PQ)], cotas, [1.4 * d for d in dv])
    gz_c = gama_z_cype([1.4, 0.98], [PG, PQ], cotas, H, dv)
    print(f"== γz (V+X princ. + Q acomp.): norma {gz_n:.4f} · CYPE {gz_c:.4f} → CYPE amplifica o vento 1,4 → "
          f"{1.4 * gz_c:.4f}; 0,95·γz = {0.95 * gz_n:.4f}")
    # galpão: q no topo (8 m), cat. III classe B, V0 = 40; Cpe da tabela 5 (h/b ≤ 1/2, α = 90°) por θ
    vk = 40.0 * s2("III", "B", 8.0)
    qg = q_norma(vk)
    th = math.degrees(math.atan(2.0 / 10.0))
    cpe_ef = -1.2 + (th - 10.0) / 5.0 * (-1.0 + 1.2)
    print(f"== Galpão 20 × 30 m, h = 6/8 m, θ = {th:.2f}°: q(8 m) = {qg:.4f} kN/m²; Cpe EF = {cpe_ef:.3f}, GH = -0,4")
    for cpi in (0.0, 0.2):
        w_ef, w_gh = (cpe_ef - cpi) * qg * 5.0, (-0.4 - cpi) * qg * 5.0
        L = math.hypot(10.0, 2.0)
        print(f"  Cpi = {cpi}: pórtico interno (5 m): EF {w_ef:.3f} kN/m, GH {w_gh:.3f} kN/m; "
              f"sucção vertical total ≈ {-(w_ef + w_gh) * L * math.cos(math.radians(th)):.1f} kN")
