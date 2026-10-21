"""Equipamentos de levantamento — NBR 8400:1984: vento (5.5.4.1) e combinações dos casos I, II e III (5.6).

Porta para o motor do `Vento8400` e do `Analise.GerarNbr8400` do Estructto 2D (Bruno, origin/main 1f719d3;
critérios em docs/VENTO.md). Unidades: m, N/m² (Pa, como na norma) e kN/m (carga nas barras).

Vento (5.5.4.1), por barra, normal ao eixo (mesma geometria do vento por barra da 6123, `vento_barras`):
    Pa = vw²/1,6 (N/m²); serviço: vw digitado (tabela 7: 20 m/s → 250 N/m² em qualquer altura);
    máximo (fora de serviço, tabela 7): 800 N/m² (0–20 m), 1100 (20–100 m), 1300 (> 100 m);
    Fw = C·A·Pa → w = C·Pa·c·sen²α (kN/m), C da tabela 8.
Combinações (5.6, tensões admissíveis, cargas sem γ):
    Caso I   = Mx·(G + ψ·Q)              (serviço normal sem vento)
    Caso II  = Mx·(G + ψ·Q) + Vserviço   (um sentido por vez)
    Caso III = G + Vmáximo               (fora de serviço, sem carga; um sentido por vez)
    ELS (flecha): G + Q e G + Q + Vserviço.
Mx pela tabela 10 (grupo 1 a 6), ψ pela tabela 5 (digitado; padrão 1,15).

Critério do motor (não está na NBR 8400 assim): o motor verifica pelos estados limites (resistência de cálculo
Rk/γa1, NBR 8800). Para dar a mesma folga das tensões admissíveis σe/ν da 8400 (ν = 1,5 / 1,33 / 1,1 nos casos
I / II / III), cada combinação ELU é multiplicada por ν/γa1 (γa1 = 1,10): ν·σ ≤ σe ⇔ (ν/γa1)·σ ≤ σe/γa1. É exato
para os critérios de escoamento; na flambagem é aproximação (a 8400 tem procedimento próprio). No 2D o Bruno
verificava todos os casos com o mesmo coeficiente global (pendência registrada em docs/VENTO.md).

Hipóteses do programa (as do 2D): z = topo da barra acima da base; a tabela 8 é de vigas inteiras e é aplicada
barra a barra (perfis abertos e retangulares como "viga de alma cheia" com l/h = L/c; tubo redondo como
"elemento tubular"); sem redução por proteção entre vigas (tabela 9) nem efeito de comprimento finito.
Não inclui: vento na carga suspensa e em cabines (lançar no caso), efeitos horizontais dos movimentos (5.5.3),
choques, temperatura (5.5.4.2), tombamento e arrastamento (5.12 e 5.13).
"""
import math

from . import vento as _vento
from . import vento_barras as _vb
from .vento_barras import _br

EDICAO = "NBR 8400:1984"
GAMA_A1 = 1.10  # NBR 8800:2008 tab. 3 (o mesmo GA1 de aco/nbr8800.py)
MX = (1.0, 1.0, 1.0, 1.06, 1.12, 1.20)  # tabela 10, grupos 1 a 6
NU = {"I": 1.5, "II": 1.33, "III": 1.1}  # 5.6: tensão admissível σe/ν por caso
PSI_PADRAO = 1.15  # tabela 5: ponte rolante com vL ≤ 0,25 m/s
VSERV_PADRAO = 20.0  # tabela 7
SUB_SERVICO, SUB_MAXIMO = "vento", "vento_maximo"  # subtipo dos casos V gerados (grupo de exclusão)
COBERTURA = [
    {"norma": "NBR 8400", "edicao": "1984", "item": "5.5.4.1, tabelas 7 e 8",
     "verificacao": "Vento em equipamentos de levantamento (config.vento.tipo = 'nbr8400'): Pa = vw²/1,6 de serviço "
                    "e vento máximo 800/1100/1300 N/m² por altura; w = C·Pa·c·sen²α por barra, C da tabela 8 "
                    "(alma cheia por l/h; tubular por d·√Pa); casos VS±X/VS±Y e VM±X/VM±Y",
     "limitacao": "Tabela 8 aplicada barra a barra (l/h = L/c); sem proteção entre vigas (tabela 9), sem vento "
                  "na carga suspensa e em cabines, sem efeitos horizontais (5.5.3), choques e temperatura."},
    {"norma": "NBR 8400", "edicao": "1984", "item": "5.6 (casos I, II, III), tabelas 5 (ψ) e 10 (Mx)",
     "verificacao": "Combinações alternativas (config.combinacoes = 'nbr8400'): I = Mx·(G + ψ·Q); II = I + vento de "
                    "serviço; III = G + vento máximo; ELS G + Q (+ vento de serviço)",
     "limitacao": "Critério do motor: ELU multiplicadas por ν/γa1 (ν = 1,5/1,33/1,1, γa1 = 1,10) para reproduzir "
                  "as tensões admissíveis σe/ν com as resistências de cálculo da NBR 8800 (exato no escoamento, "
                  "aproximado na flambagem). Casos S/E não entram; tombamento (5.12) e arrastamento (5.13) não verificados."},
]


def mx_grupo(grupo):
    """NBR 8400 tabela 10: coeficiente de majoração Mx do grupo (1 a 6)."""
    g = int(grupo)
    if not 1 <= g <= 6:
        raise ValueError(f"nbr8400.grupo: inteiro de 1 a 6 (recebido {grupo!r})")
    return MX[g - 1]


def pa_servico(vw):
    """NBR 8400 5.5.4.1: Pa = vw²/1,6 (N/m²); vw = 20 m/s → 250 N/m² (tabela 7)."""
    return vw * vw / 1.6


def pa_maximo(z):
    """NBR 8400 tabela 7, vento máximo fora de serviço: 800 (0–20 m), 1100 (20–100 m), 1300 N/m² (> 100 m)."""
    return 800.0 if z <= 20 else 1100.0 if z <= 100 else 1300.0


def c_alma_cheia(lh):
    """NBR 8400 tabela 8, viga de alma cheia: l/h = 2 → 1,2; 5 → 1,3; 10 → 1,4; ≥ 20 → 1,6 (interpolado)."""
    return _vb._interp(lh, (2, 5, 10, 20), (1.2, 1.3, 1.4, 1.6))


def c_tubular(d, pa):
    """NBR 8400 tabela 8, elemento tubular (d em m, Pa em N/m²): d·√Pa/10 ≤ 1 → 1,2; > 1 → 0,7."""
    return 1.2 if d * math.sqrt(pa) / 10 <= 1 else 0.7


def forca_barra(props, z, L, sen2, normal_mesas, pa):
    """Força do vento NBR 8400 numa barra. → dict(z, Pa_N_m2, C, c_m, lh, sen2, w_kN_m, forma, fonte)."""
    co = _vb.coeficiente(props, normal_mesas, 0.0)
    c = co["c"]
    if (props or {}).get("tipo") in _vb.CIRCULARES:
        C, lh = c_tubular(c, pa), None
        x = c * math.sqrt(pa) / 10
        fonte = f"tabela 8, elemento tubular: d·√Pa/10 = {_br(x, 2)} {'≤' if x <= 1 else '>'} 1 → C = {_br(C, 1)}"
    else:
        lh = L / c if c > 0 else 1e9
        C = c_alma_cheia(lh)
        fonte = f"tabela 8, viga de alma cheia, l/h = {_br(min(lh, 999), 1)} (comprimento da barra / dimensão vista pelo vento) → C = {_br(C, 2)}"
    return dict(z=z, Pa_N_m2=pa, C=C, c_m=c, lh=lh, sen2=sen2, w_kN_m=C * pa / 1000.0 * c * sen2,
                forma=co["forma"], fonte=fonte)


def casos_vento_8400(cfg, barras, base):
    """Casos VS±X/VS±Y (serviço) e VM±X/VM±Y (máximo). cfg: v_servico (m/s, padrão 20), direcoes.
    barras como em vento_barras.casos_vento_barras. → [{caso, subtipo, direcao, barras, parametros, avisos}]."""
    vw = float(cfg.get("v_servico") or VSERV_PADRAO)
    dirs = cfg.get("direcoes") or list(_vento.DIRECOES)
    out = []
    for maximo, pre, sub in ((False, "VS", SUB_SERVICO), (True, "VM", SUB_MAXIMO)):
        for dn in dirs:
            w = _vento.DIRECOES[dn]
            avisos, linhas, total = [], [], 0.0
            for b in barras:
                L, sen2, dp, nm = _vb.geometria_barra(b["xi"], b["xj"], b["eixos"], w)
                if L < 1e-9:
                    continue
                z = max(b["xi"][2], b["xj"][2]) - base
                try:
                    f = forca_barra(b["props"], z, L, sen2, nm, pa_maximo(z) if maximo else pa_servico(vw))
                except ValueError as e:
                    avisos.append(_vento._aviso("vento_barra_sem_secao", f"barra {b['id']}: {e}"))
                    continue
                s = math.sqrt(sen2)
                f["w_vet_kN_m"] = [f["C"] * f["Pa_N_m2"] / 1000.0 * f["c_m"] * s * x for x in dp]
                f["barra"] = b["id"]
                linhas.append(f)
                total += f["w_kN_m"] * L
            out.append(dict(caso=f"{pre}{dn}", subtipo=sub, direcao=dn, barras=linhas, avisos=avisos,
                            parametros=dict(norma=EDICAO, vento="máximo (fora de serviço)" if maximo else "limite de serviço",
                                            v_servico_m_s=vw, F_total_kN=total)))
    return out


def gerar_combinacoes(casos, grupo=3, psi=PSI_PADRAO, gama_a1=GAMA_A1):
    """NBR 8400:1984 5.6 (casos I, II, III) + ELS G + Q. `casos` = {nome: CasoCarga}. V de subtipo
    'vento_maximo' = vento máximo; os outros V = vento de serviço. → [(nome, {caso: fator}, estado, detalhe)]
    com detalhe = {caso_8400, Mx, psi, nu, conversao = ν/γa1}; o fator já inclui a conversão (docstring do módulo)."""
    mx = mx_grupo(grupo)
    if not psi > 0:
        raise ValueError(f"nbr8400.psi: número > 0 (recebido {psi!r})")
    G = [c.nome for c in casos.values() if c.tipo == "G"]
    Q = [c.nome for c in casos.values() if c.tipo == "Q"]
    VM = [c.nome for c in casos.values() if c.tipo == "V" and c.subtipo == SUB_MAXIMO]
    VS = [c.nome for c in casos.values() if c.tipo == "V" and c.subtipo != SUB_MAXIMO]
    out = []

    def elu(caso, fg, fq, vento=None):
        k = NU[caso] / gama_a1
        f = {g: fg * k for g in G}
        f.update({q: fq * k for q in Q})
        if vento:
            f[vento] = k
        f = {a: round(v, 6) for a, v in f.items() if v}
        if f:
            nome = f"ELU 8400 {caso}" + (f" + {vento}" if vento else "")
            out.append((nome, f, "ELU", dict(caso_8400=caso, Mx=mx, psi=psi, nu=NU[caso], conversao=k)))

    elu("I", mx, mx * psi)
    for v in VS:
        elu("II", mx, mx * psi, v)
    for v in VM:
        elu("III", 1.0, 0.0, v)
    base = {a: 1.0 for a in G + Q}
    if base:
        out.append(("ELS 8400 G+Q", dict(base), "ELS_RARA", dict(caso_8400="ELS", Mx=1.0, psi=1.0, nu=1.0, conversao=1.0)))
    for v in VS:
        out.append((f"ELS 8400 G+Q + {v}", dict(base, **{v: 1.0}), "ELS_RARA",
                    dict(caso_8400="ELS", Mx=1.0, psi=1.0, nu=1.0, conversao=1.0)))
    return out
