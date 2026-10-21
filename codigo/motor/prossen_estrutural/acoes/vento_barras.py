"""Vento em barras prismáticas isoladas — NBR 6123:2023 seção 8.1. Unidades: m/s, m, kN/m², kN/m.

Porta para o motor do `VentoNbr` do Estructto 2D (Bruno, origin/main 5d7133b, `codigo/portico/src/Nucleo.cs`;
critérios em docs/VENTO.md). No 3D o vento é horizontal em ±X/±Y e cada barra recebe uma carga distribuída
global (kN/m) normal ao seu eixo:

    Vk = V0·S1·S2(z)·S3 (5.1), q = 0,613·Vk² (4.2), w = Cf·q·K·c·sen²α (8.1.1, 8.1.2)

com c = dimensão de referência da seção (tabela 26, nota 1), K = fator de comprimento finito (tabela 28) e
α = ângulo entre o vento e o eixo da barra. O vetor da carga é Cf·q·K·c·senα·d⊥ (d⊥ = componente do vento
normal à barra, |d⊥| = senα), de módulo Cf·q·K·c·sen²α — no plano do pórtico é a carga perpendicular do 2D.

Hipóteses do programa (as mesmas do 2D, docs/VENTO.md; não estão escritas assim na norma):
- z = cota do ponto mais alto da barra acima da base (menor cota dos nós apoiados): S2 constante na barra;
- barra inclinada: só a componente normal, F·sen²α (mesmo critério de 8.2, fios e cabos); barra paralela ao
  vento não recebe nada;
- orientação da seção: d⊥ projetado nos eixos locais; se a maior parte cai no eixo local z (o da altura h),
  o vento é "normal às mesas" (perfil em pé no 2D), senão "paralelo às mesas" (perfil deitado no 2D);
- perfil I: Cf interpolado entre o I estreito (b = 0,48h) e o H (b = h); tubo/barra retangular: interpolado
  pela relação profundidade/largura entre os retângulos; cantoneira 2,1 (maior |Cx|); U 0,6 / 2,05;
  seção sem forma conhecida: Cf = 2,0 com c = altura;
- extremo da barra num nó apoiado: l/c dobrado (8.1.3); l/c > 100: K = 1,0;
- circular acima do crítico: confere também o regime subcrítico (Ca = 1,2) com a velocidade logo abaixo da
  crítica (Re = 4,2·10⁵) e fica com a força maior (8.1.3, último parágrafo);
- classe (5.3.2) automática pela maior dimensão da superfície frontal: altura × largura perpendicular ao vento
  (ly para vento em X, lx para vento em Y) da caixa do modelo.
Não inclui: proteção entre barras (reticulados, 8.3–8.5), vizinhança, efeitos dinâmicos (seções 9 a 11) e
vento sobre equipamentos/fechamentos (lançar à mão no caso de vento).
"""
import math

from . import vento as _vento

EDICAO = "NBR 6123:2023"
COBERTURA = [
    {"norma": "NBR 6123", "edicao": "2023", "item": "8.1.1, 8.1.2 (força em barras), tabela 26 (Cf), 8.1.3 e tabela 28 (K)",
     "verificacao": "Vento por barra (config.vento.tipo = 'barras'): w = Cf·q(z)·K·c·sen²α normal à barra, casos "
                    "V±X/V±Y com carga distribuída; Cf por forma (I, U, L, tubo/barra retangular, circular), K por l/c "
                    "(l/c dobrado em extremo apoiado)",
     "limitacao": "z = topo da barra (S2 constante); Cf interpolado entre formas da tabela 26 (I estreito ↔ H; "
                  "retângulos); orientação da seção pela componente dominante do vento nos eixos locais; sem "
                  "proteção entre barras (reticulados 8.3–8.5), vizinhança nem efeitos dinâmicos."},
    {"norma": "NBR 6123", "edicao": "2023", "item": "tabela 27 (Ca de cilindros) e 8.1.3 (regime subcrítico)",
     "verificacao": "Seção circular por Re = 70 000·Vk·d; acima do crítico confere o subcrítico (Ca = 1,2) com a "
                    "velocidade logo abaixo da crítica e fica com a força maior",
     "limitacao": "Superfície lisa; cantos arredondados de tubo retangular tratados como vivos (lado seguro)."},
]

# NBR 6123:2023 tabela 28: linhas circular subcrítico, circular acima do crítico, faces planas; colunas l/c
KLC = (2, 5, 10, 20, 40, 50, 100)
KTAB = {"circular_sub": (0.58, 0.62, 0.68, 0.74, 0.82, 0.87, 0.98),
        "circular_super": (0.80, 0.80, 0.82, 0.90, 0.98, 0.99, 1.0),
        "plana": (0.62, 0.66, 0.69, 0.81, 0.87, 0.90, 0.95)}
RE_CRITICO = 4.2e5
CIRCULARES = ("tubo_circular", "circular")


def _br(x, casas):
    """Número com vírgula decimal (textos `fonte` para a interface)."""
    return f"{x:.{casas}f}".replace(".", ",")


def _interp(x, xs, ys):
    """Linear em x; fora da faixa, o valor da ponta."""
    if x <= xs[0]:
        return ys[0]
    for i in range(1, len(xs)):
        if x <= xs[i]:
            return ys[i - 1] + (x - xs[i - 1]) / (xs[i] - xs[i - 1]) * (ys[i] - ys[i - 1])
    return ys[-1]


def classe_por_dimensao(dim):
    """NBR 6123 5.3.2: A (≤ 20 m), B (≤ 50 m), C (> 50 m) pela maior dimensão da superfície frontal."""
    return "A" if dim <= 20 else "B" if dim <= 50 else "C"


def k_comprimento(regime, lc):
    """NBR 6123 tabela 28: K por l/c (interpolado); l/c > 100 → 1,0. regime ∈ KTAB."""
    if lc > 100:
        return 1.0
    return _interp(lc, KLC, KTAB[regime])


def ca_circular(re):
    """NBR 6123 tabela 27 (cilindro liso): Re < 4,2·10⁵ 1,2; < 8,4·10⁵ 0,6; < 2,3·10⁶ 0,7; acima 0,8."""
    return 1.2 if re < RE_CRITICO else 0.6 if re < 8.4e5 else 0.7 if re < 2.3e6 else 0.8


def coeficiente(props, normal_mesas, vk):
    """Cf e c (m) da seção (tabela 26, coeficientes referidos a c — nota 1; tabela 27 para circular).
    `normal_mesas`: o vento corre ao longo da altura h (perfil "em pé" no 2D). → dict(Cf, c, Re, forma, fonte)."""
    p = props or {}
    tipo = p.get("tipo")
    h = p.get("h") or p.get("d") or p.get("D")
    b = p.get("b") or h
    if not h:
        raise ValueError(f"seção tipo {tipo!r} sem dimensões h/b: vento por barra não calculado")
    fora, plano = (b, h) if normal_mesas else (h, b)  # largura vista pelo vento / profundidade no sentido dele
    if tipo in CIRCULARES:
        c = h
        re = 70000.0 * vk * c
        reg = "subcrítico" if re < RE_CRITICO else "acima do crítico"
        return dict(Cf=ca_circular(re), c=c, Re=re, forma="circular",
                    fonte=f"tabela 27, regime {reg} (Re = {_br(re / 1e5, 2)}·10⁵)")
    if tipo == "I":
        t = (max(0.48, min(1.0, b / h)) - 0.48) / 0.52
        if normal_mesas:
            return dict(Cf=0.9 + t * (1.9 - 0.9), c=h, Re=0.0, forma="perfil I",
                        fonte="tabela 26, perfil I com vento normal às mesas (90°), interpolado entre I (b = 0,48h) e H (b = h); c = h")
        return dict(Cf=2.05 + t * (1.6 - 2.05), c=h, Re=0.0, forma="perfil I",
                    fonte="tabela 26, perfil I com vento paralelo às mesas (0°), interpolado entre I (b = 0,48h) e H (b = h); c = h")
    if tipo in ("U", "Ue"):
        if normal_mesas:
            return dict(Cf=0.6, c=h, Re=0.0, forma="perfil U", fonte="tabela 26, perfil U com vento normal às abas (90°); c = altura")
        return dict(Cf=2.05, c=h, Re=0.0, forma="perfil U", fonte="tabela 26, perfil U com vento na alma (0°, maior valor); c = altura")
    if tipo == "L":
        return dict(Cf=2.1, c=h, Re=0.0, forma="cantoneira",
                    fonte="tabela 26, cantoneira de abas iguais: maior |Cx| entre as direções (2,1); c = aba")
    if tipo in ("tubo_retangular", "retangular"):
        r = plano / fora if fora > 0 else 1.0
        return dict(Cf=_interp(r, (0.5, 1.0, 2.0), (2.1, 2.0, 1.5)), c=fora, Re=0.0,
                    forma="tubo retangular" if tipo == "tubo_retangular" else "barra retangular",
                    fonte=f"tabela 26, seção retangular, interpolado pela relação profundidade/largura = {_br(r, 2)}; c = largura vista pelo vento")
    return dict(Cf=2.0, c=fora, Re=0.0, forma="seção genérica",
                fonte="seção sem forma conhecida: adotado Cf = 2,0 com c = altura informada")


def geometria_barra(xi, xj, eixos, direcao):
    """(L, sen²α, d⊥, normal_mesas): d⊥ = componente do vento (unitário horizontal) normal ao eixo da barra;
    normal_mesas = d⊥ cai mais no eixo local z (altura h) que no y. `eixos` = (ex, ey, ez) globais."""
    d = [xj[k] - xi[k] for k in range(3)]
    L = math.sqrt(sum(x * x for x in d))
    ex, ey, ez = eixos
    w = direcao
    c = sum(w[k] * ex[k] for k in range(3))
    dp = [w[k] - c * ex[k] for k in range(3)]
    sen2 = max(0.0, 1.0 - c * c)
    py = abs(sum(dp[k] * ey[k] for k in range(3)))
    pz = abs(sum(dp[k] * ez[k] for k in range(3)))
    return L, sen2, dp, pz >= py


def forca_barra(cfg, props, z, L, sen2, normal_mesas, apoiada, classe):
    """Força do vento numa barra (NBR 6123:2023 8.1). cfg: V0, S1, S3, categoria, usar_K (padrão true).
    z = topo da barra acima da base; apoiada = um extremo num nó com apoio (l/c dobrado, 8.1.3).
    → dict(z, S2, Vk_m_s, q_kN_m2, Cf, c_m, Re, lc, K, sen2, w_kN_m, forma, fonte)."""
    cat = str(cfg.get("categoria", "II")).upper()
    S1, S3 = float(cfg.get("S1", 1.0)), float(cfg.get("S3", 1.0))
    usar_k = cfg.get("usar_K", True) is not False
    S2 = _vento.s2(cat, classe, z)
    vk = float(cfg["V0"]) * S1 * S2 * S3
    q = 0.613 * vk * vk / 1000.0
    co = coeficiente(props, normal_mesas, vk)
    c = co["c"]
    lc = L / c if c > 0 else 1e9
    if apoiada:
        lc *= 2
    circ = (props or {}).get("tipo") in CIRCULARES
    reg = ("circular_sub" if co["Re"] < RE_CRITICO else "circular_super") if circ else "plana"
    K = k_comprimento(reg, lc) if usar_k else 1.0
    cf, fonte = co["Cf"], co["fonte"]
    w = cf * q * K * c * sen2
    if circ and co["Re"] >= RE_CRITICO and c > 0:  # 8.1.3, último parágrafo
        vcr = RE_CRITICO / (70000.0 * c)
        k_sub = k_comprimento("circular_sub", lc) if usar_k else 1.0
        w_sub = 1.2 * (0.613 * vcr * vcr / 1000.0) * k_sub * c * sen2
        if w_sub > w:
            w, K = w_sub, k_sub
            cf = 1.2 * (0.613 * vcr * vcr / 1000.0) / q
            fonte += f"; governa o regime subcrítico logo abaixo de Vcr = {_br(vcr, 1)} m/s (Ca = 1,2 com q de Vcr; Cf equivalente)"
    return dict(z=z, S2=S2, Vk_m_s=vk, q_kN_m2=q, Cf=cf, c_m=c, Re=co["Re"], lc=lc, K=K, sen2=sen2, w_kN_m=w,
                forma=co["forma"], fonte=fonte)


def _aviso(codigo, mensagem):
    return _vento._aviso(codigo, mensagem)


def casos_vento_barras(cfg, barras, base, caixa):
    """Casos V±X/V±Y do vento por barra. barras: [{id, xi, xj, eixos, props, apoiada}] (xi/xj absolutos);
    base = cota do terreno; caixa = {min, max}. cfg.classe vazia = automática (5.3.2) por direção.
    → [{caso, direcao, barras: [{barra, w_vet_kN_m, ...forca_barra}], parametros, avisos}]."""
    alt = caixa["max"][2] - base
    lx, ly = caixa["max"][0] - caixa["min"][0], caixa["max"][1] - caixa["min"][1]
    dirs = cfg.get("direcoes") or list(_vento.DIRECOES)
    out = []
    for dn in dirs:
        w = _vento.DIRECOES[dn]
        frontal = max(alt, ly if dn.endswith("X") else lx)
        classe = str(cfg.get("classe") or "").upper() or classe_por_dimensao(frontal)
        avisos, linhas, total = [], [], 0.0
        for b in barras:
            L, sen2, dp, nm = geometria_barra(b["xi"], b["xj"], b["eixos"], w)
            if L < 1e-9:
                continue
            z = max(b["xi"][2], b["xj"][2]) - base
            try:
                f = forca_barra(cfg, b["props"], z, L, sen2, nm, b["apoiada"], classe)
            except ValueError as e:
                avisos.append(_aviso("vento_barra_sem_secao", f"barra {b['id']}: {e}"))
                continue
            s = math.sqrt(sen2)
            f["w_vet_kN_m"] = [f["Cf"] * f["q_kN_m2"] * f["K"] * f["c_m"] * s * x for x in dp]
            f["barra"] = b["id"]
            linhas.append(f)
            total += f["w_kN_m"] * L
        out.append(dict(caso=f"V{dn}", direcao=dn, barras=linhas, avisos=avisos,
                        parametros=dict(norma=EDICAO, classe=classe, dimensao_frontal_m=frontal,
                                        F_total_kN=total)))
    return out
