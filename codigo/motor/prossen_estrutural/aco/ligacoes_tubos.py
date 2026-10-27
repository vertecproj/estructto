"""Tubo redondo soldado em tubo redondo (nó T/Y de perfis tubulares circulares) e perfil soldado em tubo.

tubo_tubo_soldado (docs/v3/W7-TUBO.md): ramal (barra que chega, `perfil_viga`) com a ponta em boca de peixe e filete em
todo o contorno da sela num receptor contínuo (`perfil_suporte`); os dois são tubos circulares do catálogo do motor.
Verificado pela NBR 16239:2013 (base EN 1993-1-8:2005 cap. 7 e CIDECT DG1): validade (6.2.1), plastificação da face do
receptor (modo A, tabela 2) com kp, punção (modo D), momentos (tabela 4) e interação N + M; ramal pela NBR 8800 (seção:
5.2/5.3 com χ = 1, flexão pelo anexo G, 5.4.3.5, 5.5.1.2); solda pela garganta mínima da NBR 16239 6.1.5.7 e por metro
(NBR 8800 6.2.5 / tabela 8, q = |N|/Lw + M/W_sela ⊕ V/Lw + T/(Lw·r)).

Entradas: perfil_viga, perfil_suporte (nomes do catálogo; o Realista do WPF pode mandar as medidas da seção do modelo
em mm — d_ramal, t_ramal, D_receptor, t_receptor —, que prevalecem), theta (°), N_receptor (kN, + tração) e M_receptor
(kN·m) do receptor no nó (função kp), b_solda, eletrodo, aco_ramal, aco_receptor. Referencial local: x ao longo do ramal
saindo do nó, receptor no plano xz a theta de x (eixo a = (cos θ, 0, sen θ) pelo nó); M (My) = momento no plano do nó
(xz, contém os dois eixos), Mz = fora do plano."""
from __future__ import annotations

import math

from .. import secoes
from . import ligacoes as L
from . import ligacoes_flexiveis as F
from . import pontas as P

MM = 1e-3
GA1 = 1.10        # NBR 8800 / NBR 16239
E_ACO = 200e6     # kN/m²
_TC = list(dict.fromkeys(lin["nome"] for lin in secoes._carrega_catalogo().values() if lin["forma"] == "TC"))
_MEDIDAS = ("d_ramal", "t_ramal", "D_receptor", "t_receptor")   # mm, opcionais (seção do modelo, Render3DMotor)


def _tc(rotulo, padrao):
    """Campo de perfil restrito aos tubos circulares do catálogo: `perfis` = nomes aceitos (o projetista mostra só
    esses no seletor de perfil)."""
    return dict(F._e(rotulo, "-", padrao), perfis=_TC)


def _sec(c, secao):
    """`secao` = seção do passo 2 do projetista (sem ela a tela deduz a seção pelo nome do campo)."""
    return dict(c, secao=secao)


_ENT = {
    "perfil_viga": _tc("Tubo do ramal (chega)", "TC 114,3x5,6"),
    "perfil_suporte": _tc("Tubo do receptor (passa)", "TC 168,3x6,4"),
    "theta": _sec(F._e("Ângulo ramal–receptor", "°", 45, mn=1, mx=90), "Tubos"),
    "N_receptor": _sec(F._e("Receptor: N no nó (+tração)", "kN", 0, mn=-1e5, mx=1e5), "Tubos"),
    "M_receptor": _sec(F._e("Receptor: M no nó", "kN·m", 0, mn=-1e4, mx=1e4), "Tubos"),
    "b_solda": F._e("Perna do filete", "mm", 10, mn=2, mx=20),
    "eletrodo": F._e("Eletrodo", "-", "E70", list(L.ELETRODOS)),
    "aco_ramal": F._e("Aço do ramal", "-", "A-572 GR.50", F._ACOS),
    "aco_receptor": F._e("Aço do receptor", "-", "A-572 GR.50", F._ACOS),
}
# padrão de fábrica (projeto.tipos_ligacao → exemplo): N = 100 kN de tração e 5 kN·m no plano do nó
_EXEMPLO = dict(dados=dict(perfil_viga="TC 114,3x5,6", perfil_suporte="TC 168,3x6,4"),
                esforcos=dict(N=100.0, V=0.0, Vz=0.0, M=5.0, Mz=0.0, T=0.0))


def _ler(dados, ent=None, nome="tubo_tubo_soldado"):
    out, erros = {}, []
    for k, c in (ent or _ENT).items():
        v = (dados or {}).get(k, c["padrao"])
        if v is None:
            erros.append(f"{k}: obrigatório")
        elif "opcoes" in c:
            out[k] = next((o for o in c["opcoes"] if str(o).upper() == str(v).upper()), None)
            if out[k] is None:
                erros.append(f"{k}: '{v}' inválido")
        elif isinstance(c["padrao"], str):   # perfil do catálogo
            out[k] = str(v)
        else:
            x = float(v)
            if not (math.isfinite(x) and c["min"] <= x <= c["max"]):
                erros.append(f"{k}: {x:g} fora da faixa [{c['min']}, {c['max']}] {c['unidade']}")
            out[k] = x * MM if c["unidade"] == "mm" else x
    if erros:
        raise ValueError(f"ligação '{nome}': " + "; ".join(erros))
    return out


def _tubo(dados, campo, kd, kt):
    """(D, t) em m: medidas explícitas em mm (`kd`, `kt`) ou o tubo circular do catálogo em `campo`."""
    dados = dados or {}
    if dados.get(kd) is not None and dados.get(kt) is not None:
        D, t = float(dados[kd]) * MM, float(dados[kt]) * MM
    else:
        nome = dados.get(campo) or _ENT[campo]["padrao"]
        try:
            p = secoes.catalogo(str(nome)).props
        except KeyError:
            raise ValueError(f"{campo}: perfil '{nome}' não está no catálogo") from None
        if p.get("tipo") != "tubo_circular":
            raise ValueError(f"{campo}: '{nome}' não é tubo circular")
        D, t = p["h"], p.get("t", p.get("tw", 0.0))
    if not (D > 0 and 0 < 2 * t < D):
        raise ValueError(f"{campo}: tubo Ø{D / MM:g} × {t / MM:g} mm inválido")
    return D, t


def props_tubo(D, t):
    """Tubo circular (m): A, W elástico e Z plástico exatos da coroa."""
    di = D - 2 * t
    return dict(A=math.pi / 4 * (D * D - di * di), W=math.pi * (D ** 4 - di ** 4) / (32 * D), Z=(D ** 3 - di ** 3) / 6)


def kp(np_):
    """Função de tensão no receptor (NBR 16239 tabela 2; = EN 1993-1-8 7.2.2 kp): np = σ0p,Sd/fy0 com compressão
    negativa; np < 0: 1 + 0,3np − 0,3np²; tração ou receptor descarregado: 1."""
    return 1 + 0.3 * np_ - 0.3 * np_ * np_ if np_ < 0 else 1.0


def no_tubular(d0, t0, d1, th, fy0, kp_=1.0):
    """Resistências de cálculo do nó T/Y (kN, kN·m; entradas m, kN/m², rad). NBR 16239:2013 tabela 2 (axial) e tabela 4
    (momentos) = EN 1993-1-8:2005 tabelas 7.2 e 7.5 com os coeficientes × 1,10 e γa1 = 1,10:
    modo A (plastificação da face): N = γ^0,2·kp·fy0·t0²·(3,08 + 15,62β²)/(γa1·sen θ);
      Mip = 5,34·fy0·t0²·d1·√γ·β·kp/(γa1·sen θ); Mop = 2,97·fy0·t0²·d1·kp/(γa1·sen θ·(1 − 0,81β));
    modo D (punção, só com d1 ≤ d0 − 2t0): N = 0,66·fy0·t0·π·d1·(1 + sen θ)/(2·γa1·sen²θ);
      Mip = 0,66·fy0·t0·d1²·(1 + 3 sen θ)/(4·γa1·sen²θ); Mop = 0,66·fy0·t0·d1²·(3 + sen θ)/(4·γa1·sen²θ).
    β = d1/d0, γ = d0/(2t0). Conferência à mão: test_ligacoes_tubos.TestNoTubular."""
    b, g, s = d1 / d0, d0 / (2 * t0), math.sin(th)
    f = fy0 * t0 * t0
    out = dict(beta=b, gama=g, N_A=g ** 0.2 * kp_ * f * (3.08 + 15.62 * b * b) / (GA1 * s),
               Mip_A=5.34 * f * d1 * math.sqrt(g) * b * kp_ / (GA1 * s),
               Mop_A=2.97 * f * d1 * kp_ / (GA1 * s * (1 - 0.81 * b)))
    if d1 <= d0 - 2 * t0:
        v = 0.66 * fy0 * t0 / GA1
        out.update(N_D=v * math.pi * d1 * (1 + s) / (2 * s * s), Mip_D=v * d1 * d1 * (1 + 3 * s) / (4 * s * s),
                   Mop_D=v * d1 * d1 * (3 + s) / (4 * s * s))
    return out


def mrd_tubo(D, t, fy):
    """MRd (kN·m) de tubo circular pela NBR 8800 anexo G (FLA, tabela G.1): λ = D/t; λ ≤ 0,07E/fy: Z·fy; até
    0,31E/fy: (0,021E/λ + fy)·W; acima: 0,33E/λ·W; tudo /γa1."""
    p, lam = props_tubo(D, t), D / t
    if lam <= 0.07 * E_ACO / fy:
        return p["Z"] * fy / GA1
    if lam <= 0.31 * E_ACO / fy:
        return (0.021 * E_ACO / lam + fy) * p["W"] / GA1
    return 0.33 * E_ACO / lam * p["W"] / GA1


def garganta_minima(t1, fy1):
    """Garganta do filete que dispensa o cálculo da ductilidade (NBR 16239 6.1.5.7): a ≥ k·t do ramal, k = 1,0
    (fy ≤ 280 MPa), 1,1 (≤ 350), 1,5 (≤ 450; acima, fora da tabela: 1,5 e aviso)."""
    f = fy1 / F.MPa
    return (1.0 if f <= 280 else 1.1 if f <= 350 else 1.5) * t1


def _v(r, nome, sd, rd, un, item, sentido=None, parametro=None):
    """Verificação com o item já completo ("NBR 16239 …"; o `_Col.res` prefixaria "NBR 8800")."""
    conv = 1 / MM if un == "mm" else 1.0
    r.v.append(dict(nome=nome, item=item, solicitante=float(sd) * conv, resistente=float(rd) * conv, unidade=un,
                    taxa=float(sd / rd) if rd > 0 else F._INF, ok=bool(sd <= rd * (1 + 1e-9))))
    if sentido:
        L._dica(r.v[-1], sentido, parametro)


def _faixa(r, nome, x, lo, hi, item):
    """Limite de validade lo ≤ x ≤ hi (None = sem esse lado): registra o lado que governa (mínimo: exigido ≤ real)."""
    if lo is not None and (hi is None or lo / x >= x / hi):
        _v(r, nome, lo, x, "-", item)
    else:
        _v(r, nome, x, hi, "-", item)


def tubos_geo(pts, a, receptor, ramal=None):
    """Lista `tubos` (mm, docs/v3/LIGACOES-CONTRATO.md §tubos). pts = curva de contato (m, relativa ao nó), a = eixo do
    receptor (unitário, pelo nó); receptor/ramal = (d, t, campo) em m; ramal None quando a barra não é tubo circular.
    Receptor: um trecho de cada lado do nó (projeção da curva no eixo + d0). Ramal: do fundo da curva até 2·d1 além do
    topo, com o corte em boca de peixe pelo cilindro externo do receptor."""
    D0, t0, c0 = receptor
    ts = [P._dot(p, a) for p in pts]
    meio = max(-min(ts), max(ts)) + D0
    mm = lambda v: [round(x / MM, 3) for x in v]   # noqa: E731
    out = [dict(nome="receptor", grupo="suporte", campo=c0, origem=mm([-meio * x for x in a]), eixo=list(a),
                comprimento=round(2 * meio / MM, 3), d=D0 / MM, t=t0 / MM, corte=None)]
    if ramal:
        d1, t1, c1 = ramal
        xs = [p[0] for p in pts]
        out.insert(0, dict(nome="ramal", grupo="viga", campo=c1, origem=mm([min(xs), 0.0, 0.0]), eixo=[1.0, 0.0, 0.0],
                           comprimento=round((max(xs) - min(xs) + 2 * d1) / MM, 3), d=d1 / MM, t=t1 / MM,
                           corte=dict(origem=[0.0, 0.0, 0.0], eixo=list(a), r=round(D0 / 2 / MM, 3))))
    return out


def sela(r, R, theta, n=72):
    """Pontos (m, referencial local) da curva de contato do ramal (raio externo r) na superfície do receptor (raio R);
    receptor pelo nó com eixo a = (cos θ, 0, sen θ). Mesmo cálculo de pontas._sela."""
    u, a = [1.0, 0.0, 0.0], [math.cos(theta), 0.0, math.sin(theta)]
    c, s = math.cos(theta), math.sin(theta)
    e1 = [(a[i] - c * u[i]) / s for i in range(3)]
    e2 = P._cross(u, e1)
    out = []
    for k in range(n):
        f = 2 * math.pi * k / n
        cf = math.cos(f)
        b = c * r * s * cf
        disc = b * b - s * s * (r * r - (r * s * cf) ** 2 - R * R)
        t = (b + math.sqrt(max(0.0, disc))) / (s * s)
        out.append([t * u[i] + r * (cf * e1[i] + math.sin(f) * e2[i]) for i in range(3)])
    return out


def modulo_sela(pts):
    """Módulo resistente (m², linha fina: I [m³] / c [m]) da linha de solda à flexão, menor dos dois eixos ⊥ ao ramal (x local).
    Linha tratada como linha fina na projeção sobre o plano yz (comprimento projetado, conservador).
    Conferência à mão: a projeção da sela é o círculo de raio r ⇒ I = π r³, W = π r².
    Ex.: Ø114,3 em Ø168,3 a 45°, N=0, V=0, M=2 kN·m: W = π·0,05715² = 1,0261e-2 m², q = 2/1,0261e-2 = 194,9 kN/m.
    Retorna 0 se a linha for degenerada."""
    n = len(pts)
    seg = [(math.hypot(pts[(k + 1) % n][1] - pts[k][1], pts[(k + 1) % n][2] - pts[k][2]),
            [(pts[k][i] + pts[(k + 1) % n][i]) / 2 for i in (1, 2)]) for k in range(n)]
    L_ = sum(l for l, _ in seg)
    if L_ <= 0:
        return 0.0
    cy, cz = (sum(l * m[j] for l, m in seg) / L_ for j in (0, 1))
    W = math.inf
    for j, c in ((0, cy), (1, cz)):
        I = sum(l * (m[j] - c) ** 2 for l, m in seg)
        dmax = max(abs(p[j + 1] - c) for p in pts)
        if I <= 0 or dmax <= 0:
            return 0.0
        W = min(W, I / dmax)
    return W


_VAZIO = dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[], pontas=[], tubos=[])
_VAL = "NBR 16239 6.2.1"


def _verificar(dados, esforcos):
    d = _ler(dados)
    r = F._Col()
    e = dict(esforcos or {})
    # contrato (ligacoes_modelo.esforcos_contrato): M = My (no plano do nó, xz), Mz (fora do plano), V = |Vz|, Vz = |Vy|, T
    N = float(e.get("N", 0.0) or 0.0)
    V = math.hypot(abs(float(e.get("V", 0.0) or 0.0)), abs(float(e.get("Vz", 0.0) or 0.0)))
    Mip, Mop = abs(float(e.get("M", 0.0) or 0.0)), abs(float(e.get("Mz", 0.0) or 0.0))
    M = math.hypot(Mip, Mop)
    T = abs(float(e.get("T", 0.0) or 0.0))
    dr, tr = _tubo(dados, "perfil_viga", "d_ramal", "t_ramal")
    Dr, tc = _tubo(dados, "perfil_suporte", "D_receptor", "t_receptor")
    beta = dr / Dr
    r.val.update(beta=beta, gama=Dr / (2 * tc), d1_mm=dr / MM, t1_mm=tr / MM, d0_mm=Dr / MM, t0_mm=tc / MM)
    _faixa(r, "ramal: β = d1/d0 (0,2 a 1,0)", beta, 0.2, 1.0, _VAL)
    if beta > 1:   # sela impossível: só a reprovação, sem geometria
        r.avisos.append("ramal maior que o receptor: tubo–tubo soldado impossível")
        return r.resultado(dict(_VAZIO))
    _faixa(r, "receptor: d0/t0 (10 a 50)", Dr / tc, 10.0, 50.0, _VAL)
    _faixa(r, "ramal: d1/t1 (10 a 50)", dr / tr, 10.0, 50.0, _VAL)
    _faixa(r, "ramal: ângulo θ ≥ 30°", d["theta"], 30.0, None, _VAL + " / EN 1993-1-8 tabela 7.1")
    th = math.radians(d["theta"])
    fyr, fur = F._aco(d["aco_ramal"])
    fyc, fuc = F._aco(d["aco_receptor"])

    # receptor: função de tensão kp com a fibra mais comprimida do receptor no nó (compressão negativa)
    p0 = props_tubo(Dr, tc)
    N0, M0 = d["N_receptor"], d["M_receptor"]
    s0 = N0 / p0["A"] - abs(M0) / p0["W"]
    np_ = min(0.0, s0) / fyc
    k = kp(np_)
    r.val.update(np=np_, kp=k)
    if N0 or M0:
        _v(r, "receptor: tensão no nó |σ0| ≤ fy0/γa1", (abs(N0) / p0["A"] + abs(M0) / p0["W"]) / F.MPa,
           fyc / GA1 / F.MPa, "MPa", "NBR 8800 5.5.1.2 (seção do receptor)")
    else:
        r.avisos.append("receptor sem esforços no nó (N e M do receptor = 0): kp = 1 — informe a compressão do "
                        "receptor, que reduz a resistência do nó (NBR 16239, função kp)")
    no = no_tubular(Dr, tc, dr, th, fyc, k)
    r.val.update({f"{x}_kN": no[x] for x in ("N_A", "N_D") if x in no})
    _v(r, "receptor: plastificação da face (modo A)", abs(N), no["N_A"], "kN", "NBR 16239 tabela 2 (modo A)")
    if "N_D" in no:
        _v(r, "receptor: punção da parede (modo D)", abs(N), no["N_D"], "kN", "NBR 16239 tabela 2 (modo D)")
    else:
        r.avisos.append("punção do receptor não se aplica (d1 > d0 − 2t0, NBR 16239 tabela 2)")
    if M > 0:
        NRd = min(no["N_A"], no.get("N_D", math.inf))
        MipRd, MopRd = min(no["Mip_A"], no.get("Mip_D", math.inf)), min(no["Mop_A"], no.get("Mop_D", math.inf))
        r.val.update(Mip_Rd=MipRd, Mop_Rd=MopRd)
        _v(r, "receptor: interação N + M no nó", abs(N) / NRd + (Mip / MipRd) ** 2 + Mop / MopRd, 1.0, "-",
           "NBR 16239 tabela 4 (EN 1993-1-8 7.4.2)")
        r.avisos.append("momentos do ramal: My = no plano do nó (plano dos dois eixos), Mz = fora do plano")

    # ramal (seção junto ao nó)
    p1 = props_tubo(dr, tr)
    n1 = abs(N) / (p1["A"] * fyr / GA1)
    m1 = M / mrd_tubo(dr, tr, fyr)
    _v(r, "ramal: seção, N + M", n1 + 8 / 9 * m1 if n1 >= 0.2 else n1 / 2 + m1, 1.0, "-",
       "NBR 8800 5.5.1.2 (5.2/5.3 com χ = 1, anexo G)")
    if V > 0:
        _v(r, "ramal: cortante", V, 0.3 * fyr * p1["A"] / GA1, "kN", "NBR 8800 5.4.3.5")
    if N < 0:
        r.avisos.append("ramal comprimido: a flambagem da barra (NBR 8800 5.3, χ) é verificada na barra, não aqui")

    # solda
    pts = sela(dr / 2, Dr / 2, th)
    Lw = sum(math.dist(pts[k], pts[(k + 1) % len(pts)]) for k in range(len(pts)))
    r.val.update(Lw_mm=Lw / MM)
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * F.MPa
    _v(r, "solda: garganta ≥ k·t do ramal (resistência da parede)", garganta_minima(tr, fyr), 0.707 * bs, "mm",
       "NBR 16239 6.1.5.7", "sobe", "b_solda")
    if fyr / F.MPa > 450:
        r.avisos.append("aço do ramal com fy > 450 MPa: fora da tabela da NBR 16239 6.1.5.7 (usado k = 1,5)")
    W = modulo_sela(pts)
    r.val.update(W_sela_cm3=W / 1e-6 if W else 0.0)
    if M > 0 and not W:
        r.avisos.append("solda: módulo resistente da sela não calculável com M ≠ 0 — solda não verificada")
        r.v.append(dict(nome="solda ramal–receptor: W_sela não calculável com M ≠ 0", item="NBR 8800 6.2.5",
                        solicitante=M, resistente=0.0, unidade="kN·m", taxa=F._INF, ok=False))
        return r.resultado(dict(_VAZIO))
    # torção: fluxo tangencial T/(Lw·r) somado (pior caso) ao cortante V/Lw, combinado vetorialmente com o normal
    q = math.hypot(abs(N) / Lw + (M / W if M else 0.0), V / Lw + T / (Lw * dr / 2))
    r.res("solda ramal–receptor (filete em todo o contorno da sela)", q, L.resistencia_filete(bs, fw, fyr, fur)["FwRd"],
          "kN/m", "6.2.5 / tabela 8")
    r.res("receptor: metal base junto à solda", q, L.resistencia_filete(bs, fw, fyc, fuc)["metal_base"], "kN/m",
          "6.2.5 / tabela 8")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(tr, tc)), bs, "6.2.6.2.1 / tabela 10", "b_solda")
    # sem perna máxima (6.2.6.2.2): a regra vale para filete AO LONGO da borda de uma chapa (junta sobreposta); aqui a
    # ponta do tubo encosta no receptor (junta em T) e a NBR 16239 6.1.5.7 pede garganta ≥ k·t (perna > t)
    ramal = dict(barra=0, extremo="i", dir=[1.0, 0.0, 0.0], eixo_d=[0.0, 0.0, 1.0], comprimento=math.inf,
                 perfil=dict(h=dr, b=dr))
    a = [math.cos(th), 0.0, math.sin(th)]
    rec = dict(barra=1, extremo="i", dir=a, eixo_d=[0.0, 1.0, 0.0], continuo=True, comprimento=math.inf,
               perfil=dict(h=Dr, b=Dr))
    geo = dict(_VAZIO,
               membros=[dict(barra="viga", perfil=f"{d['perfil_viga']} (ramal)", d=dr / MM),
                        dict(barra="pilar", perfil=f"{d['perfil_suporte']} (receptor)", d=Dr / MM, theta=d["theta"])],
               soldas=[dict(tipo="filete", campo="b_solda", perna=bs / MM, pontos=[[x / MM for x in p] for p in pts + pts[:1]])],
               tubos=tubos_geo(pts, a, (Dr, tc, "perfil_suporte"), (dr, tr, "perfil_viga")),
               pontas=P.pontas_do_no([0.0, 0.0, 0.0], [ramal, rec], "boca_peixe"))
    return r.resultado(geo)


def _sugerir(perfis, esforcos):
    """Mantém os tubos (vêm do modelo) e escolhe a menor perna da lista comercial que passa nas verificações da solda
    (garganta da NBR 16239 6.1.5.7, filete por metro e mínimo da tabela 10)."""
    base = {k: v for k, v in dict(perfis or {}).items() if k in _ENT or k in _MEDIDAS}
    for b in L._B_MM:
        try:
            res = _verificar(dict(base, b_solda=b), esforcos)
        except ValueError:
            return base
        if all(x["ok"] for x in res["verificacoes"] if x["nome"].startswith(("solda", "receptor: metal base"))):
            break
    return dict(base, b_solda=b)


# ---------------------------------------------------------------- perfil (I, U, tubo, retangular) soldado em tubo

_FORMAS_TUBO = ("tubo_circular", "tubo_retangular")
_ENT_PT = {
    "forma_ramal": _sec(F._e("Forma do perfil", "-", "I", list(P.FORMAS_RAMAL)), "Perfil"),
    "h_ramal": _sec(F._e("Altura do perfil", "mm", 310, mn=10, mx=2000), "Perfil"),
    "b_ramal": _sec(F._e("Largura do perfil", "mm", 165, mn=10, mx=2000), "Perfil"),
    "tw_ramal": _sec(F._e("Alma/parede do perfil", "mm", 5.8, mn=1, mx=100), "Perfil"),
    "tf_ramal": _sec(F._e("Mesa/parede do perfil", "mm", 9.7, mn=1, mx=100), "Perfil"),
    "forma_receptor": _sec(F._e("Forma do tubo", "-", "tubo_circular", list(_FORMAS_TUBO)), "Tubo"),
    "D_receptor": _sec(F._e("Tubo: Ø ou lado (D)", "mm", 323.8, mn=10, mx=3000), "Tubo"),
    "B_receptor": _sec(F._e("Tubo: largura da face (B)", "mm", 0, mn=0, mx=3000), "Tubo"),
    "t_receptor": _sec(F._e("Parede do tubo", "mm", 9.5, mn=1, mx=100), "Tubo"),
    "theta": _sec(F._e("Ângulo perfil–tubo", "°", 90, mn=1, mx=90), "Tubo"),
    "giro": _sec(F._e("Giro do tubo (0 = plano h)", "°", 0, mn=-180, mx=180), "Tubo"),
    "b_solda": F._e("Perna do filete", "mm", 5, mn=2, mx=30),
    "eletrodo": F._e("Eletrodo", "-", "E70", list(L.ELETRODOS)),
    "aco_ramal": F._e("Aço do perfil", "-", "A-572 GR.50", F._ACOS),
    "aco_receptor": F._e("Aço do tubo", "-", "A-572 GR.50", F._ACOS),
}
# padrão de fábrica: W310 (padrões acima) na parede do tubo 323,8 × 9,5 com cortante e momento moderados
_EXEMPLO_PT = dict(esforcos=dict(N=0.0, V=60.0, Vz=0.0, M=15.0, Mz=0.0, T=0.0))


def _linha_solda(cont):
    """Propriedades da solda tratada como linha (m) no plano da seção do perfil (comprimento projetado, conservador),
    polígono fechado [(yb, zh)]: L, centroide, I_forte (∫zh²), I_fraco (∫yb²) e as distâncias máximas; Lv_h / Lv_b =
    soma das projeções dos trechos na direção h / b (trechos que levam o cortante dessa direção). Segmento reto:
    ∫y² ds = l·(y1² + y1·y2 + y2²)/3."""
    n = len(cont)
    seg = [(cont[k], cont[(k + 1) % n]) for k in range(n)]
    ln = [math.dist(p, q) for p, q in seg]
    Lw = sum(ln)
    cy = sum(l * (p[0] + q[0]) / 2 for l, (p, q) in zip(ln, seg)) / Lw
    cz = sum(l * (p[1] + q[1]) / 2 for l, (p, q) in zip(ln, seg)) / Lw
    i2 = lambda j, c: sum(l * ((p[j] - c) ** 2 + (p[j] - c) * (q[j] - c) + (q[j] - c) ** 2) / 3 for l, (p, q) in zip(ln, seg))
    return dict(Lw=Lw, I_forte=i2(1, cz), I_fraco=i2(0, cy), zmax=max(abs(p[1] - cz) for p in cont),
                ymax=max(abs(p[0] - cy) for p in cont), rmax=max(math.hypot(p[0] - cy, p[1] - cz) for p in cont),
                Lv_h=sum(abs(q[1] - p[1]) for p, q in seg), Lv_b=sum(abs(q[0] - p[0]) for p, q in seg))


def _verificar_pt(dados, esforcos):
    d = _ler_pt(dados)
    r = F._Col()
    e = dict(esforcos or {})
    N = abs(float(e.get("N", 0.0) or 0.0))
    V, Vb = abs(float(e.get("V", 0.0) or 0.0)), abs(float(e.get("Vz", 0.0) or 0.0))   # V no plano da alma (h), Vz na largura
    M, Mz = abs(float(e.get("M", 0.0) or 0.0)), abs(float(e.get("Mz", 0.0) or 0.0))  # M eixo forte, Mz eixo fraco
    T = abs(float(e.get("T", 0.0) or 0.0))
    tubo_r = d["forma_ramal"] in ("tubo_retangular", "tubo_circular")
    perfil = dict(tipo=d["forma_ramal"], h=d["h_ramal"], b=d["h_ramal"] if d["forma_ramal"] == "tubo_circular" else d["b_ramal"],
                  tw=d["tw_ramal"], tf=d["tw_ramal"] if tubo_r else d["tf_ramal"])
    circ = d["forma_receptor"] == "tubo_circular"
    Dr = d["D_receptor"]
    Br = Dr if circ or d["B_receptor"] <= 0 else d["B_receptor"]
    th, g = math.radians(d["theta"]), math.radians(d["giro"])
    c, s = math.cos(th), math.sin(th)
    ramal = dict(barra=0, extremo="i", dir=[1.0, 0.0, 0.0], eixo_d=[0.0, 0.0, 1.0], comprimento=math.inf, perfil=perfil)
    # referencial do contrato: x = perfil saindo do nó, z = altura h dele; eixo do tubo a θ de x, girado `giro` de z para
    # y; eixo_d do tubo = do eixo dele para o perfil (lado D), largura da face B no terceiro eixo
    rec = dict(barra=1, extremo="i", dir=[c, s * math.sin(g), s * math.cos(g)],
               eixo_d=[s, -c * math.sin(g), -c * math.cos(g)], continuo=True, comprimento=math.inf,
               perfil=dict(tipo=d["forma_receptor"], h=Dr, b=Br))
    vazio = dict(_VAZIO)
    try:
        pts, _ts, beta = P.contato_tubo(ramal, rec)
    except ValueError as ex:
        r.res("geometria: β = largura do perfil / largura do tubo ≤ 1", math.inf, 1.0, "-", "geometria")
        r.avisos.append(str(ex))
        return r.resultado(vazio)
    r.val.update(beta=beta)
    r.res("geometria: β = largura do perfil / largura do tubo ≤ 1", beta, 1.0, "-", "geometria (perfil apoiado no tubo)")
    ls = _linha_solda(P.contorno(perfil))
    r.val.update(Lw_mm=ls["Lw"] / MM, W_forte_mm2=ls["I_forte"] / ls["zmax"] / 1e-6, W_fraco_mm2=ls["I_fraco"] / ls["ymax"] / 1e-6)
    # solda como linha (método elástico, mesmo critério de modulo_sela): normal = N/Lw + M/W_forte + Mz/W_fraco;
    # cortante = V/Lv_h ⊕ Vz/Lv_b (só os trechos paralelos ao cortante) + T·r/Ip; combinados vetorialmente
    qn = N / ls["Lw"] + M * ls["zmax"] / ls["I_forte"] + Mz * ls["ymax"] / ls["I_fraco"]
    qv = math.hypot(V / ls["Lv_h"], Vb / ls["Lv_b"]) + T * ls["rmax"] / (ls["I_forte"] + ls["I_fraco"])
    q = math.hypot(qn, qv)
    fyr, fur = F._aco(d["aco_ramal"])
    fyc, fuc = F._aco(d["aco_receptor"])
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * F.MPa
    r.res("solda perfil–tubo (filete em todo o contorno)", q, L.resistencia_filete(bs, fw, fyr, fur)["FwRd"], "kN/m",
          "6.2.5 / tabela 8")
    r.res("tubo: metal base junto à solda", q, L.resistencia_filete(bs, fw, fyc, fuc)["metal_base"], "kN/m",
          "6.2.5 / tabela 8")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(max(perfil["tw"], perfil["tf"]), d["t_receptor"])), bs,
             "6.2.6.2.1 / tabela 10", "b_solda")
    r.maximo("solda: dimensão máxima (chapa mais fina do perfil)", bs, L.filete_maximo(min(perfil["tw"], perfil["tf"])),
             "6.2.6.2.2", "b_solda")
    r.avisos.append("parede do tubo: plastificação, punção e cisalhamento (NBR 16239 / CIDECT) não verificados — "
                    "conferir; só a solda, o metal base e β verificados")
    tubos = []
    if circ:   # contrato `tubos` só tem tubo circular: o receptor retangular (e o ramal não circular) ficam nos membros
        tubos = tubos_geo(pts, rec["dir"], (Dr, d["t_receptor"], "D_receptor"),
                          (perfil["h"], perfil["tw"], "h_ramal") if d["forma_ramal"] == "tubo_circular" else None)
    geo = dict(vazio, membros=[dict(barra="viga", perfil=f"{d['forma_ramal']} {perfil['h'] / MM:g}×{perfil['b'] / MM:g}"),
                               dict(barra="pilar", perfil=f"{d['forma_receptor']} {Dr / MM:g}", theta=d["theta"])],
               soldas=[dict(tipo="filete", campo="b_solda", perna=bs / MM, pontos=[[x / MM for x in p] for p in pts + pts[:1]])],
               tubos=tubos, pontas=P.pontas_do_no([0.0, 0.0, 0.0], [ramal, rec], "solda_em_tubo"))
    return r.resultado(geo)


def _ler_pt(dados):
    return _ler(dados, _ENT_PT, "perfil_tubo_soldado")


TIPOS = {
    "tubo_tubo_soldado": dict(
        nome="Tubo–tubo soldado (boca de peixe)", familia="tubo", liga=["tubo-tubo"], entradas=_ENT, esforcos=["N", "V", "Vz", "M", "Mz", "T"],
        descricao="Nó T/Y de tubos circulares: ramal com a ponta em boca de peixe soldado em todo o contorno num "
                  "receptor contínuo de diâmetro maior ou igual. NBR 16239: validade, plastificação da face, punção e "
                  "N + M no nó; ramal (NBR 8800) e solda (garganta da 16239 6.1.5.7 e NBR 8800 6.2.5).",
        verificar=_verificar, sugerir=_sugerir, exemplo=_EXEMPLO),
    "perfil_tubo_soldado": dict(
        nome="Perfil–tubo soldado (recorte)", familia="tubo", liga=["viga-tubo", "diagonal-tubo"], entradas=_ENT_PT,
        esforcos=["N", "V", "Vz", "M", "Mz", "T"],
        descricao="Perfil I/H, U, tubo ou barra retangular encostado num tubo (redondo: ponta em boca de peixe; "
                  "retangular: corte na face) com filete em todo o contorno de contato. Solda pela NBR 8800 6.2.5; "
                  "parede do tubo (NBR 16239) não verificada.",
        verificar=_verificar_pt, exemplo=_EXEMPLO_PT,
        sugerir=lambda perfis, esforcos: {k: v for k, v in dict(perfis or {}).items() if k in _ENT_PT}),
}
L.registrar_pontas(TIPOS)
