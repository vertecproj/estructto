"""Tubo redondo soldado em tubo redondo (v0.6 Frente A3, docs/v3/PONTAS-CONTRATO.md §v0.6).

Ponta do ramal em boca de peixe (pontas.pontas_do_no "boca_peixe") e um filete em todo o contorno da sela.
Verificado: solda pela NBR 8800 6.2.5 / tabela 8 (q = |N|/Lw + M/W_sela combinada vetorialmente com V/Lw), filete
mínimo/máximo (6.2.6.2) e β = d/D ≤ 1. NÃO verificado: o nó tubular da NBR 16239 (plastificação da parede do
receptor, punção, cisalhamento do banzo) — avisado no resultado.

Entradas em mm (o WPF lê h e t das seções do modelo): d_ramal, t_ramal, D_receptor, t_receptor, theta (°, entre os
eixos), b_solda, eletrodo, aco_ramal, aco_receptor. Referencial local: x ao longo do ramal saindo do nó, receptor no
plano xz a theta de x."""
from __future__ import annotations

import math

from . import ligacoes as L
from . import ligacoes_flexiveis as F
from . import pontas as P

MM = 1e-3
_ENT = {
    "d_ramal": F._e("Diâmetro do ramal", "mm", 114.3, mn=10, mx=2000),
    "t_ramal": F._e("Parede do ramal", "mm", 6.0, mn=1, mx=100),
    "D_receptor": F._e("Diâmetro do receptor", "mm", 168.3, mn=10, mx=3000),
    "t_receptor": F._e("Parede do receptor", "mm", 6.4, mn=1, mx=100),
    "theta": F._e("Ângulo ramal–receptor", "°", 45, mn=1, mx=90),
    "b_solda": F._e("Perna do filete", "mm", 5, mn=2, mx=30),
    "eletrodo": F._e("Eletrodo", "-", "E70", list(L.ELETRODOS)),
    "aco_ramal": F._e("Aço do ramal", "-", "A-572 GR.50", F._ACOS),
    "aco_receptor": F._e("Aço do receptor", "-", "A-572 GR.50", F._ACOS),
}


def _ler(dados):
    out, erros = {}, []
    for k, c in _ENT.items():
        v = (dados or {}).get(k, c["padrao"])
        if v is None:
            erros.append(f"{k}: obrigatório")
        elif "opcoes" in c:
            out[k] = next((o for o in c["opcoes"] if str(o).upper() == str(v).upper()), None)
            if out[k] is None:
                erros.append(f"{k}: '{v}' inválido")
        else:
            x = float(v)
            if not (math.isfinite(x) and c["min"] <= x <= c["max"]):
                erros.append(f"{k}: {x:g} fora da faixa [{c['min']}, {c['max']}] {c['unidade']}")
            out[k] = x * MM if c["unidade"] == "mm" else x
    if erros:
        raise ValueError("ligação 'tubo_tubo_soldado': " + "; ".join(erros))
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


def _verificar(dados, esforcos):
    d = _ler(dados)
    r = F._Col()
    e = dict(esforcos or {})
    N, V = float(e.get("N", 0.0) or 0.0), abs(float(e.get("V", 0.0) or 0.0))
    M = abs(float(e.get("M", 0.0) or 0.0))
    dr, Dr = d["d_ramal"], d["D_receptor"]
    beta = dr / Dr
    r.val.update(beta=beta)
    r.res("geometria: β = d/D ≤ 1 (ramal não maior que o receptor)", beta, 1.0, "-", "geometria: NBR 16239 (validade)")
    if beta > 1:   # sela impossível: só a reprovação, sem geometria
        r.avisos.append("ramal maior que o receptor: tubo–tubo soldado impossível")
        return r.resultado(dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[], pontas=[]))
    th = math.radians(d["theta"])
    pts = sela(dr / 2, Dr / 2, th)
    Lw = sum(math.dist(pts[k], pts[(k + 1) % len(pts)]) for k in range(len(pts)))
    r.val.update(Lw_mm=Lw / MM)
    fyr, fur = F._aco(d["aco_ramal"])
    fyc, fuc = F._aco(d["aco_receptor"])
    bs, fw = d["b_solda"], L.ELETRODOS[d["eletrodo"]] * F.MPa
    W = modulo_sela(pts)
    r.val.update(W_sela_cm3=W / 1e-6 if W else 0.0)
    if M > 0 and not W:
        r.avisos.append("solda: módulo resistente da sela não calculável com M ≠ 0 — solda não verificada")
        r.v.append(dict(nome="solda ramal–receptor: W_sela não calculável com M ≠ 0", item="NBR 8800 6.2.5",
                        solicitante=M, resistente=0.0, unidade="kN·m", taxa=F._INF, ok=False))
        return r.resultado(dict(chapas=[], cantoneiras=[], parafusos=[], soldas=[], recortes=[], pontas=[]))
    q = math.hypot(abs(N) / Lw + (M / W if M else 0.0), V / Lw)
    r.res("solda ramal–receptor (filete em todo o contorno da sela)", q, L.resistencia_filete(bs, fw, fyr, fur)["FwRd"],
          "kN/m", "6.2.5 / tabela 8")
    r.res("receptor: metal base junto à solda", q, L.resistencia_filete(bs, fw, fyc, fuc)["metal_base"], "kN/m",
          "6.2.5 / tabela 8")
    r.minimo("solda: dimensão mínima", L.filete_minimo(min(d["t_ramal"], d["t_receptor"])), bs, "6.2.6.2.1 / tabela 10",
             "b_solda")
    r.maximo("solda: dimensão máxima (parede do ramal)", bs, L.filete_maximo(d["t_ramal"]), "6.2.6.2.2", "b_solda")
    r.avisos.append("nó tubular: só a solda e β verificados; plastificação da parede, punção e cisalhamento do "
                    "receptor (NBR 16239) não verificados")
    ramal = dict(barra=0, extremo="i", dir=[1.0, 0.0, 0.0], eixo_d=[0.0, 0.0, 1.0], comprimento=math.inf,
                 perfil=dict(h=dr, b=dr))
    rec = dict(barra=1, extremo="i", dir=[math.cos(th), 0.0, math.sin(th)], eixo_d=[0.0, 1.0, 0.0], continuo=True,
               comprimento=math.inf, perfil=dict(h=Dr, b=Dr))
    geo = dict(chapas=[], cantoneiras=[], parafusos=[], recortes=[],
               membros=[dict(barra="viga", perfil=f"tubo Ø{dr / MM:g} (ramal)", d=dr / MM),
                        dict(barra="pilar", perfil=f"tubo Ø{Dr / MM:g} (receptor)", d=Dr / MM, theta=d["theta"])],
               soldas=[dict(tipo="filete", campo="b_solda", perna=bs / MM, pontos=[[x / MM for x in p] for p in pts + pts[:1]])],
               pontas=P.pontas_do_no([0.0, 0.0, 0.0], [ramal, rec], "boca_peixe"))
    return r.resultado(geo)


def _sugerir(perfis, esforcos):
    return {k: v for k, v in dict(perfis or {}).items() if k in _ENT}


TIPOS = {
    "tubo_tubo_soldado": dict(
        nome="Tubo–tubo soldado (boca de peixe)", familia="tubo", liga=["tubo-tubo"], entradas=_ENT, esforcos=["N", "V", "M"],
        descricao="Tubo redondo com a ponta em boca de peixe soldado em todo o contorno num tubo redondo de diâmetro "
                  "maior ou igual. Solda pela NBR 8800 6.2.5; nó tubular (NBR 16239) não verificado.",
        verificar=_verificar, sugerir=_sugerir),
}
L.registrar_pontas(TIPOS)
