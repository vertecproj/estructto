"""NBR 8800:2008 — barras de aço laminado/soldado. Unidades kN, m, kN/m².
Notação da norma: x = eixo de maior inércia, y = menor (ver secoes.py).

Edição: a alvo é a 2024, mas as equações revisadas (FLT no novo anexo de flexão, área
efetiva por expressões diretas em 5.3.4.2) não foram encontradas em fonte pública; os
itens ficam na 2008 e a diferença conhecida vai em COBERTURA[].limitacao.
Fontes das mudanças 2008→2024: Vanin & Chamberlain, "Novidades da ABNT NBR 8800:2024",
blog Gerdau Mais, 10/10/2024 (voce.mais.gerdau.com.br/construcao-metalica/novidades-da-abnt-nbr-88002024);
R. Monteiro, "NBR 8800:2024 – principais mudanças", masteremmodelagem.com.br — acesso 2026-10-20.
"""
from __future__ import annotations

import logging
import math

import numpy as np

from .. import secoes as _secoes

log = logging.getLogger(__name__)

GA1, GA2 = 1.10, 1.35  # NBR 8800 tabela 3

EDICAO = "NBR 8800:2008"

_LIM_2024 = "2024 não confirmada em fonte pública: mantida a 2008"
COBERTURA = [
    dict(norma="NBR 8800", edicao="2008", item="5.2.2", verificacao="tração (escoamento e ruptura)",
         limitacao="An e Ct fornecidos pelo usuário (5.2.3/5.2.5 não calculados); 2024 mudou Ct de tubos (5.2.5): " + _LIM_2024),
    dict(norma="NBR 8800", edicao="2008", item="5.2.8", verificacao="esbeltez de barra tracionada L/r ≤ 300",
         limitacao="5.2.8 é recomendação (\"recomenda-se\"; o CYPE não verifica): mantida como critério que só "
                   "reprova (ESPEC §7.2, a favor da segurança), fora de taxa_max, com aviso"),
    dict(norma="NBR 8800", edicao="2008", item="5.3.2 / 5.3.3 / anexo E", verificacao="compressão: Ne (flexão, torção, flexo-torção), χ",
         limitacao="cantoneira simples: eixos centrais principais (E.1.2/E.1.3) com KL = max(KLx, KLy) nos dois eixos; "
                   "E.1.4 (ligada por uma aba) só quando informado (ligacao_uma_aba); 2024 (5.3.5.4) tem fórmula nova "
                   "para cantoneira simples; 2024 integrou o anexo E ao corpo (5.3): " + _LIM_2024),
    dict(norma="NBR 8800", edicao="2008", item="anexo F", verificacao="flambagem local na compressão: Q = Qs·Qa",
         limitacao="2024 (5.3.4.2) troca por área efetiva (pode reduzir Nc,Rd quando Q < 1 → aviso): " + _LIM_2024
                   + "; tubo circular com D/t > 0,45E/fy fora do escopo (reprova); espessura de cálculo 0,93t de "
                   "tubos com costura não aplicada (informe t reduzida)"),
    dict(norma="NBR 8800", edicao="2008", item="5.3.4", verificacao="esbeltez de barra comprimida KL/r ≤ 200",
         limitacao="mesmo limite na 2024 (5.3.7.1)"),
    dict(norma="NBR 8800", edicao="2008", item="5.4.2 / anexo G (FLT, FLM, FLA)", verificacao="flexão em x de I duplamente simétrico; FLM em y",
         limitacao="I e U (U sem torção: carga pelo centro de cisalhamento); tubos retangulares e barras retangulares "
                   "(FLT com Mr = (fy − σr)W, a favor); tubos circulares (D/t); alma esbelta (anexo H), cantoneira "
                   "simples, T e I monossimétrico não implementados → reprova (nao_verificado); "
                   "J e Cw do catálogo calculados sem concordâncias (J até ~10 % menor: a favor da segurança); "
                   "2024 revisou a FLT (Cb só no regime elástico, novo λr): " + _LIM_2024 + "; altura da carga "
                   "(ponto_aplicacao topo/base, opcional) pelo Mcr de 3 fatores do NCCI SN003b (C1 → Cb; C2 da tab. "
                   "3.2; topo sem C2 → 1,645, base sem C2 → sem benefício); topo também limita o regime inelástico "
                   "a Mcr; λr da G.1 inalterado"),
    dict(norma="NBR 8800", edicao="2008", item="5.4.2.3", verificacao="Cb pelo diagrama (quartos do trecho destravado), Rm = 1",
         limitacao="Cb ≤ 3,0 mantido (2024 retirou o limite: mais conservador manter); balanço livre: Cb = 1 via params; seção monossimétrica (Rm ≠ 1) não tratada"),
    dict(norma="NBR 8800", edicao="2008", item="5.4.3.1 / 5.4.3.5", verificacao="cortante paralela à alma e às mesas",
         limitacao="sem enrijecedores transversais (kv = 5); tubos retangulares com h = lado − 3t (5.4.3.2); tubos "
                   "circulares por 5.4.3.6: maior τcr entre 1,60E/(√(Lv/D)(D/t)^1,25) e 0,78E/(D/t)^1,5 (sem Lv nem L: "
                   "só o segundo, a favor); tubo com costura: espessura de cálculo 0,93t em todas as propriedades "
                   "(AISC 360-05 B3.12, parâmetro tubo_com_costura); cantoneira simples sem item na "
                   "2008 → Aw = b·t, kv = 1,2 (análogo de 5.4.3.5; aviso); maciças por Aw ≈ 0,6A (aproximação)"),
    dict(norma="NBR 8800", edicao="2008", item="5.5.1.2", verificacao="interação N + Mx + My", limitacao=""),
    dict(norma="NBR 8800", edicao="2008", item="5.5.2.2 / 5.5.2.3", verificacao="torção de tubos circulares e retangulares",
         limitacao="expressões iguais às do AISC 360-05 H3.1 (0,458π²E/(h/t)² no regime elástico; h/t > 260 fora do "
                   "escopo → reprova); sem L informado, tubo circular só com 0,60E/(D/t)^1,5 (a favor); com T > 0,2·TRd e "
                   "outros esforços: (N/NRd + Mx/MxRd + My/MyRd) + (Vy/VyRd + Vx/VxRd + T/TRd)² ≤ 1 (AISC 360-05 H3.2; "
                   "cortantes somadas, a favor)"),
    dict(norma="NBR 8800", edicao="2008", item="5.5.2 (seções abertas e maciças)",
         verificacao="torção: τ = T·t/J ≤ 0,6fy/γa1 e Von Mises (critério adicional, como o CYPE)",
         limitacao="tensões elásticas como no AISC 360-05 H3.3; torção uniforme com a espessura máxima (barra maciça: "
                   "menor lado / raio); com T > 0,2·TRd e outros esforços: Von Mises √(σ² + 3τ²) ≤ fy/γa1 por envelope "
                   "(σ e τ máximos somados, mesmo em pontos diferentes) com tensão de empenamento σw = B·ωn/Cw e "
                   "B ≤ Tmáx·√(E·Cw/(G·J)) (majorante das soluções de Seaburg & Carter, AISC Design Guide 9, 1997) — "
                   "conservador; flambagem sob tensão de cisalhamento de torção não verificada"),
    dict(norma="NBR 8800", edicao="2008", item="5.3.4 (barras compostas)",
         verificacao="barra composta de 2 perfis (U ou L): soldada contínua como seção única; com presilhas/chapas "
                     "espaçadoras: perfis em contato ou com chapas espaçadoras, l/r de cada perfil entre ligações ≤ 1/2 "
                     "do KL/r do conjunto",
         limitacao="presilhas com afastamento maior que chapa espaçadora, separação automática do CYPE (não lida), "
                   "ligação genérica e flexão em torno do eixo de simetria (ação conjunta pelas presilhas) sem item "
                   "na NBR 8800:2008 → não verificado (reprova); flexão em x e cortantes = 2 × perfil isolado; "
                   "J e Cw = soma dos perfis (a favor)"),
    dict(norma="NBR 8800", edicao="2008", item="anexo C, tabela C.1", verificacao="deslocamentos máximos",
         limitacao="2024 (anexo B) mudou terças para L/250 e vigas de rolamento: " + _LIM_2024 + "; combinação ELS adotada "
                   "(ver limite_deslocamento); terças com sucção (L/120), travessas e vigas de rolamento não cobertas"),
    dict(norma="NBR 8800", edicao="2008", item="4.9.4 / anexo D", verificacao="B2 e classificação da deslocabilidade",
         limitacao="2024 (4.10.4) mantém 1,1 / 1,4 sem imperfeições iniciais (1,13 / 1,55 com imperfeições: não implementado)"),
]


def tracao(sec, mat, An=None, Ct=1.0):
    # NBR 8800 5.2.2
    if not 0 < Ct <= 1.0:  # 5.2.5: Ct ≤ 1,0
        raise ValueError(f"Ct = {Ct!r}: coeficiente de redução da área líquida deve estar em (0; 1,0] (NBR 8800 5.2.5)")
    Nesc = sec.A * mat.fy / GA1
    Nrup = Ct * (An if An is not None else sec.A) * mat.fu / GA2
    r = dict(NtRd=min(Nesc, Nrup), Nt_escoamento=Nesc, Nt_ruptura=Nrup, item="5.2.2")
    if An is None:
        r["aviso"] = "tração: An não informada → An = Ag (sem furos) com o Ct informado (5.2.3/5.2.5)"
    return r


def forca_flambagem(sec, mat, KLx, KLy, KLz):
    """NBR 8800 anexo E: Nex, Ney, Nez (E.1.1) e Ne; seção sem dupla simetria: menor raiz da equação
    geral (E.1.3). x, y são os eixos centrais PRINCIPAIS: na cantoneira usa Iu, Iv e o centro de
    cisalhamento (u0, v0) e, como KLx/KLy vêm dos eixos geométricos, KL = max(KLx, KLy) nos dois."""
    p, E, G = sec.props, mat.E, mat.G
    Ix, Iy, x0, y0 = p["Ix"], p["Iy"], p["x0"], p["y0"]
    if "Iu" in p:  # cantoneira ou 2L em cruz — E.1.2/E.1.3: eixos principais (Ixy ≠ 0 nos geométricos)
        Ix, Iy, x0, y0 = p["Iu"], p["Iv"], p["u0"], p["v0"]
        KLx = KLy = max(KLx, KLy)
    Nex = math.pi ** 2 * E * Ix / KLx ** 2   # E.1.1 a)
    Ney = math.pi ** 2 * E * Iy / KLy ** 2   # E.1.1 b)
    r0 = p["r0"]                             # invariante: √(rx² + ry² + x0² + y0²)
    Nez = (math.pi ** 2 * E * p["Cw"] / KLz ** 2 + G * p["J"]) / r0 ** 2  # E.1.1 c)
    ax, ay = (x0 / r0) ** 2, (y0 / r0) ** 2
    if ax < 1e-9 and ay < 1e-9:
        Ne = min(Nex, Ney, Nez)
    else:
        # E.1.3: (N-Nex)(N-Ney)(N-Nez) - N²(N-Ney)(x0/r0)² - N²(N-Nex)(y0/r0)² = 0
        c = np.polysub(np.polymul(np.polymul([1, -Nex], [1, -Ney]), [1, -Nez]),
                       np.polyadd(np.polymul([ax, 0, 0], [1, -Ney]), np.polymul([ay, 0, 0], [1, -Nex])))
        raizes = [r.real for r in np.roots(c) if abs(r.imag) < 1e-6 * abs(r) and r.real > 0]
        Ne = min(raizes + [Nex, Ney])
    return dict(Nex=Nex, Ney=Ney, Nez=Nez, Ne=Ne)


def ne_cantoneira_uma_aba(sec, mat, L, aba="maior", trelica="plana"):
    """NBR 8800:2008 E.1.4 — cantoneira simples carregada nas extremidades pela mesma aba (solda ou ≥ 2
    parafusos na direção da força, sem ações transversais intermediárias): Ne = π²E·Ix1/(KxLx)², Ix1 e rx1
    em relação ao eixo central paralelo à aba conectada; a excentricidade fica no comprimento equivalente
    (sem flexo-torção). L: comprimento entre pontos de trabalho. aba: 'maior' ou 'menor' (conectada);
    trelica: 'plana' (barra individual ou treliça plana) ou 'espacial'. Expressões iguais às do
    AISC 360-05 E5 (adotado pela NBR 8800:2008). Abas desiguais (≥ 1,7) ligadas pela menor: fora do
    escopo → None. Entrada inválida → ValueError."""
    p = sec.props
    if p["tipo"] != "L":
        raise ValueError("E.1.4 só para cantoneira simples")
    if aba not in ("maior", "menor") or trelica not in ("plana", "espacial"):
        raise ValueError("ligacao_uma_aba: aba ∈ {maior, menor}, trelica ∈ {plana, espacial}")
    bmaior, bmenor = max(p["b"], p["h"]), min(p["b"], p["h"])
    conectada_b = (p["b"] >= p["h"]) == (aba == "maior")      # a aba b (horizontal) é a conectada?
    Ix1 = p["Ix"] if conectada_b else p["Iy"]                 # eixo central paralelo à aba conectada
    rx1 = math.sqrt(Ix1 / sec.A)
    esb = L / rx1
    if trelica == "plana":   # E.1.4 a) e b)
        KL = 72 * rx1 + 0.75 * L if esb <= 80 else min(32 * rx1 + 1.25 * L, 200 * rx1)
    else:                    # E.1.4 c) e d)
        KL = 60 * rx1 + 0.80 * L if esb <= 75 else 45 * rx1 + L
    if aba == "menor" and bmaior > bmenor:
        if bmaior / bmenor >= 1.7:
            return None
        KL += (4 if trelica == "plana" else 6) * ((bmaior / bmenor) ** 2 - 1) * rx1
        KL = max(KL, (0.95 if trelica == "plana" else 0.82) * L * rx1 / p["rmin"])
    Ne = math.pi ** 2 * mat.E * Ix1 / KL ** 2
    return dict(Ne=Ne, KxLx=KL, rx1=rx1, L_rx1=esb, item="E.1.4")


def _kc(p):
    # NBR 8800 F.2 c) e nota 6 da tabela G.1: kc = 4/√(h/tw), 0,35 ≤ kc ≤ 0,76
    return min(max(4 / math.sqrt(p["ht_alma"]), 0.35), 0.76)


def _componente(sec):
    """Perfil isolado de uma barra composta aberta (props tipo 'dupla', secoes.secao_dupla)."""
    return _secoes.secao_de_spec(dict(sec.props["composicao"]["componente"], nome=sec.props["composicao"]["nome_componente"]))


def _dupla_5_3_4(sec, KLx, KLy):
    """NBR 8800:2008 5.3.4: barra composta comprimida (perfis em contato ou com chapas espaçadoras) — l/r de
    cada perfil entre ligações ≤ 1/2 do maior KL/r do conjunto. Fora disso → NotImplementedError (reprova)."""
    c, p = sec.props["composicao"], sec.props
    if c["ligacao"] == "generica":
        raise NotImplementedError("barra composta com ligação genérica entre perfis: sem item na NBR 8800:2008")
    if c["s"] > 0 and not c.get("chapas_espacadoras"):
        raise NotImplementedError(f"barra composta com afastamento {c['s'] * 1e3:g} mm entre perfis (presilhas): "
                                  "a NBR 8800:2008 5.3.4 só cobre perfis em contato ou com chapas espaçadoras")
    l = c.get("l_ligacoes")
    if not l:
        raise NotImplementedError("barra composta sem a distância entre presilhas/chapas (composicao.l_ligacoes): "
                                  "5.3.4 não verificável")
    cp = _componente(sec).props
    ri = cp.get("rmin", min(cp["rx"], cp["ry"]))
    conj = max(KLx / p["rx"], KLy / p["ry"])
    li = l / ri
    if li > conj / 2:
        raise NotImplementedError(f"5.3.4: l/r de cada perfil entre ligações = {li:.0f} > 1/2 do KL/r do conjunto "
                                  f"({conj:.0f}/2): aproxime as presilhas")
    return dict(l_ri=li, KL_r_conjunto=conj, item="5.3.4")


def fator_Q(sec, mat):
    """NBR 8800 anexo F: Q = Qs·Qa (elementos AL e AA). Barra composta aberta: Q do perfil isolado."""
    if sec.props["tipo"] == "dupla":
        return fator_Q(_componente(sec), mat)
    if sec.props["tipo"] == "I_chapa":  # chapa dentro da largura da mesa: apoiada na mesa, Q do I
        c = sec.props["composicao"]
        if c["chapa"]["b"] > c["componente"]["b"] + 1e-9:
            raise NotImplementedError("I + chapa mais larga que a mesa: Q da chapa em balanço fora do escopo")
        return fator_Q(_componente(sec), mat)
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    raiz = math.sqrt(E / fy)
    Qs, Qa = 1.0, 1.0
    if tipo in ("I", "U", "L"):
        bt = p["bt_mesa"]
        if tipo == "L":                      # grupo 3 (F.2 a)
            lim1, lim2 = 0.45 * raiz, 0.91 * raiz
            Qs = 1 if bt <= lim1 else (1.340 - 0.76 * bt / raiz if bt <= lim2 else 0.53 * E / (fy * bt ** 2))
        elif p.get("soldado"):               # grupo 5 (F.2 c)
            kc = _kc(p)
            r2 = math.sqrt(E * kc / fy)
            Qs = 1 if bt <= 0.64 * r2 else (1.415 - 0.65 * bt / r2 if bt <= 1.17 * r2 else 0.90 * E * kc / (fy * bt ** 2))
        else:                                # grupo 4 (F.2 b)
            Qs = 1 if bt <= 0.56 * raiz else (1.415 - 0.74 * bt / raiz if bt <= 1.03 * raiz else 0.69 * E / (fy * bt ** 2))
    if tipo in ("I", "U"):                   # alma, grupo 2 (F.3), σ = fy (conservador, F.3.2)
        bt = p["ht_alma"]
        if bt > 1.49 * raiz:
            t, b = p["tw"], p["ht_alma"] * p["tw"]
            bef = min(1.92 * t * raiz * (1 - 0.34 / bt * raiz), b)
            Qa = (sec.A - (b - bef) * t) / sec.A
    elif tipo == "tubo_retangular":          # grupo 1, ca = 0,38
        t = p["t"]
        perda = 0.0
        for bt in (p["bt_mesa"], p["ht_alma"]):
            if bt > 1.40 * raiz:
                b = bt * t
                perda += 2 * (b - min(1.92 * t * raiz * (1 - 0.38 / bt * raiz), b)) * t
        Qa = (sec.A - perda) / sec.A
    elif tipo == "tubo_circular":            # F.4
        Dt = p["D_t"]
        if Dt > 0.45 * E / fy:  # F.4.2: limite de aplicação
            raise NotImplementedError(f"tubo circular com D/t = {Dt:.0f} > 0,45E/fy (F.4.2): fora do escopo")
        if Dt > 0.11 * E / fy:
            Qa = 0.038 * E / (fy * Dt) + 2 / 3
    return Qs * Qa, Qs, Qa


def compressao(sec, mat, KLx, KLy, KLz=None, ligacao_uma_aba=None):
    """NBR 8800 5.3.2 e 5.3.3. ligacao_uma_aba (só cantoneira simples): None → eixos principais com
    flexo-torção (E.1.2/E.1.3) e aviso; True ou dict(L=, aba='maior'|'menor', trelica='plana'|'espacial')
    → E.1.4 (L padrão: max(KLx, KLy))."""
    p = sec.props
    ne = forca_flambagem(sec, mat, KLx, KLy, KLz or max(KLx, KLy))
    avisos = []
    if p["tipo"] == "dupla":
        ne["composta"] = _dupla_5_3_4(sec, KLx, KLy)
    if p["tipo"] == "L":
        if ligacao_uma_aba:
            o = ligacao_uma_aba if isinstance(ligacao_uma_aba, dict) else {}
            e14 = ne_cantoneira_uma_aba(sec, mat, float(o.get("L") or max(KLx, KLy)),
                                        o.get("aba", "maior"), o.get("trelica", "plana"))
        if ligacao_uma_aba and e14:
            ne.update(Ne=e14["Ne"], E14=e14)
        elif ligacao_uma_aba:
            avisos.append("cantoneira ligada pela aba menor com bmaior/bmenor ≥ 1,7: fora de E.1.4; usados os "
                          "eixos principais com flexo-torção, SEM o efeito da excentricidade da ligação — confira")
        else:
            avisos.append("cantoneira simples: compressão nos eixos principais com flexo-torção (E.1.2/E.1.3); "
                          "se ligada por uma aba, informe ligacao_uma_aba para usar E.1.4")
    Q, Qs, Qa = fator_Q(sec, mat)
    if Q < 1:  # VC-aco-009
        avisos.append(f"Q = {Q:.3f} < 1 (elemento esbelto): a NBR 8800:2024 (5.3.4.2, área efetiva) pode "
                      "reduzir Nc,Rd em relação à 2008 aqui usada — confira")
    l0 = math.sqrt(Q * sec.A * mat.fy / ne["Ne"])
    chi = 0.658 ** (l0 ** 2) if l0 <= 1.5 else 0.877 / l0 ** 2
    esbeltez = max(KLx / p["rx"], KLy / p["ry"])
    if "rmin" in p:  # cantoneira: eixo principal de menor inércia (5.3.4)
        esbeltez = max(esbeltez, max(KLx, KLy) / p["rmin"])
    r = dict(NcRd=chi * Q * sec.A * mat.fy / GA1, chi=chi, Q=Q, Qs=Qs, Qa=Qa, lambda0=l0,
             esbeltez=esbeltez, esbeltez_ok=esbeltez <= 200, item="5.3", **ne)
    if avisos:
        r["avisos"] = avisos
    return r


def Cb(Mmax, MA, MB, MC, Rm=1.0):
    # NBR 8800 5.4.2.3
    return min(12.5 * abs(Mmax) / (2.5 * abs(Mmax) + 3 * abs(MA) + 4 * abs(MB) + 3 * abs(MC)) * Rm, 3.0)


def cb_de_diagrama(xs, Mx, a=0.0, b=None, balanco=False):
    """Cb (NBR 8800 5.4.2.3) do trecho destravado [a, b] (padrão: barra toda), Rm = 1
    (dupla simetria). MA, MB, MC nos quartos do trecho e Mmáx por interpolação linear nos
    pontos dados (use pontos nos quartos — n_pontos = 13 — para valores exatos).
    balanco=True (trecho em balanço com extremidade livre): Cb = 1,0 (5.4.2.3)."""
    if balanco:
        return 1.0
    xs = np.asarray(xs, dtype=float)
    Mx = np.asarray(Mx, dtype=float)
    b = xs[-1] if b is None else b
    if not xs[0] <= a < b <= xs[-1]:
        raise ValueError(f"trecho destravado [{a}, {b}] fora da barra [{xs[0]}, {xs[-1]}]")
    L = b - a
    MA, MB, MC = (abs(float(np.interp(a + k * L / 4, xs, Mx))) for k in (1, 2, 3))
    dentro = (xs > a) & (xs < b)
    Mmax = max([abs(float(np.interp(a, xs, Mx))), abs(float(np.interp(b, xs, Mx)))]
               + [float(v) for v in np.abs(Mx[dentro])])
    if Mmax == 0.0:
        return 1.0
    return Cb(Mmax, MA, MB, MC)


def _mn(Mpl, Mr, Mcr, lam, lp, lr, cb=1.0):
    if lam <= lp:
        return Mpl
    if lam <= lr:
        return min(cb * (Mpl - (Mpl - Mr) * (lam - lp) / (lr - lp)), Mpl)
    return min(Mcr, Mpl)


def _flm(p, E, fy):
    """FLM de mesa de I/U (tabela G.1, nota 6): (λ, λp, λr, Mcr/W); soldado com kc."""
    sr = 0.3 * fy
    lm, lpm = p["bt_mesa"], 0.38 * math.sqrt(E / fy)
    if p.get("soldado"):
        kc = _kc(p)
        return lm, lpm, 0.95 * math.sqrt(E * kc / (fy - sr)), 0.90 * E * kc / lm ** 2
    return lm, lpm, 0.83 * math.sqrt(E / (fy - sr)), 0.69 * E / lm ** 2


def _fora_g1(tipo):
    if tipo == "L":
        return NotImplementedError("flexão de cantoneira simples não implementada (fora da tabela G.1 da "
                                   "NBR 8800:2008)")
    return NotImplementedError(f"flexão de seção '{tipo}' não coberta pela tabela G.1 da NBR 8800:2008 "
                               "(cantoneira simples, T, I monossimétrico)")


# Altura de aplicação da carga no Mcr (LCA-017; a G.1 supõe carga no centro de cisalhamento): expressão de
# 3 fatores do NCCI SN003b-EN-EU (Access Steel), eq. (2), k = kw = 1, com C1 → Cb (5.4.2.3).
# C2 sem informação: topo → 1,645 (maior C2 da tabela 3.2 do SN003b, a favor); base → 0 (benefício ignorado).
C2_TOPO = 1.645
_PONTOS = ("centro", "topo", "base")


def flexao_x(sec, mat, Lb, cb=1.0, ponto_aplicacao="centro", C2=None):
    """Momento resistente em torno de x (maior inércia), anexo G, tabela G.1: I duplamente simétrico e U
    (FLT, FLM, FLA); tubos e barras retangulares; tubos circulares; barra circular maciça.
    ponto_aplicacao (I/U): 'centro' (G.1), 'topo' (carga na mesa, dirigida ao centro de cisalhamento:
    desestabilizante, zg = +d/2) ou 'base' (zg = −d/2); C2 da tabela 3.2 do SN003b (ver C2_TOPO).
    Seção fora da tabela G.1 → NotImplementedError (a verificação reprova). Cb fora de (0; 3,0] → ValueError."""
    if not 0 < cb <= 3.0:
        raise ValueError(f"Cb = {cb!r}: deve estar em (0; 3,0] (NBR 8800 5.4.2.3)")
    if ponto_aplicacao not in _PONTOS:
        raise ValueError(f"ponto_aplicacao = {ponto_aplicacao!r}: use {', '.join(_PONTOS)}")
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    if tipo == "dupla":  # cada perfil fletido no seu eixo x (comum aos dois): 2 × perfil isolado
        r = flexao_x(_componente(sec), mat, Lb, cb, ponto_aplicacao, C2)
        r.update(MRd=2 * r["MRd"], item=r["item"] + " (barra composta: 2 × perfil isolado)")
        return r
    if tipo not in ("I", "U"):
        if tipo in ("tubo_retangular", "retangular"):
            r = _flexao_caixao(sec, mat, "x", Lb, cb)
        elif tipo == "tubo_circular":
            r = _flexao_tubo_circular(sec, mat, "x")
        elif tipo == "circular":
            r = _flexao_compacta(sec, mat, "x")
        else:
            raise _fora_g1(tipo)
        if ponto_aplicacao != "centro":
            r["avisos"] = [f"ponto_aplicacao = '{ponto_aplicacao}' não considerado (só FLT de I/U)"]
        return r
    if C2 is None:
        C2 = C2_TOPO if ponto_aplicacao == "topo" else 0.0
    zg = {"centro": 0.0, "topo": p["h"] / 2, "base": -p["h"] / 2}[ponto_aplicacao]
    c = C2 * zg
    sr = 0.3 * fy
    Mpl, W = p["Zx"] * fy, p["Wx"]
    Iy, J, Cw, ry = p["Iy"], p["J"], p["Cw"], p["ry"]
    # FLT (G.1: I duplamente simétrico e U não sujeito a torção; Cw do U pela nota 1, em secoes.secao_U)
    lam = Lb / ry
    lp = 1.76 * math.sqrt(E / fy)
    b1 = (fy - sr) * W / (E * J)
    lr = 1.38 * math.sqrt(Iy * J) / (ry * J * b1) * math.sqrt(1 + math.sqrt(1 + 27 * Cw * b1 ** 2 / Iy))
    # G.1 (0,039 = G/(π²E)); com zg: SN003b eq. (2) → √(a + c²) − c, c = C2·zg
    a = Cw / Iy * (1 + 0.039 * J * Lb ** 2 / Cw) if Lb > 0 else 0.0
    Mcr = cb * math.pi ** 2 * E * Iy / Lb ** 2 * (math.sqrt(a + c * c) - c) if Lb > 0 else math.inf
    Mflt = _mn(Mpl, (fy - sr) * W, Mcr, lam, lp, lr, cb)
    if c > 0:  # carga desestabilizante: também no regime inelástico Mn ≤ Mcr (a favor)
        Mflt = min(Mflt, Mcr)
    # FLM (U: λ = bf/tf; I: bf/(2tf))
    lm, lpm, lrm, mcr_w = _flm(p, E, fy)
    Mflm = _mn(Mpl, (fy - sr) * W, mcr_w * W, lm, lpm, lrm)
    # FLA
    la = p["ht_alma"]
    lpa, lra = 3.76 * math.sqrt(E / fy), 5.70 * math.sqrt(E / fy)
    if la > lra:
        raise NotImplementedError("alma esbelta (h/tw > λr): anexo H não implementado")
    Mfla = _mn(Mpl, fy * W, None, la, lpa, lra)
    Mn = min(Mflt, Mflm, Mfla)
    r = dict(MRd=min(Mn, 1.5 * W * fy) / GA1, Mn_FLT=Mflt, Mn_FLM=Mflm, Mn_FLA=Mfla, Mcr_FLT=Mcr,
             lambda_FLT=(lam, lp, lr), lambda_FLM=(lm, lpm, lrm), lambda_FLA=(la, lpa, lra),
             Mpl=Mpl, item="5.4.2 / anexo G")
    if ponto_aplicacao != "centro":
        r.update(zg=zg, C2=C2, ponto_aplicacao=ponto_aplicacao)
    if tipo == "U":
        r["aviso"] = "U: FLT pela tabela G.1 supõe a seção sem torção (carga pelo centro de cisalhamento)"
    return r


def flexao_y(sec, mat):
    """Momento resistente em torno de y (menor inércia), tabela G.1: I e U (FLM; U também FLA da alma),
    tubos e barras retangulares, tubos circulares, barra circular maciça. Fora da G.1 → NotImplementedError."""
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    if tipo == "dupla":
        raise NotImplementedError("flexão de barra composta aberta em torno do eixo de simetria (ação conjunta "
                                  "pelas presilhas): sem item na NBR 8800:2008")
    if tipo in ("tubo_retangular", "retangular"):
        return _flexao_caixao(sec, mat, "y", 0.0, 1.0)
    if tipo == "tubo_circular":
        return _flexao_tubo_circular(sec, mat, "y")
    if tipo == "circular":
        return _flexao_compacta(sec, mat, "y")
    if tipo not in ("I", "U"):
        raise _fora_g1(tipo)
    sr = 0.3 * fy
    Mpl, W = p["Zy"] * fy, p["Wy"]
    lm, lpm, lrm, mcr_w = _flm(p, E, fy)
    Mn = _mn(Mpl, (fy - sr) * W, mcr_w * W, lm, lpm, lrm)
    r = dict(Mn_FLM=Mn, lambda_FLM=(lm, lpm, lrm), item="5.4.2 / anexo G")
    if tipo == "U":  # G.1, U em torno do eixo de menor inércia: FLA da alma (λp = 1,12, λr = 1,40 √(E/fy))
        la, lpa, lra = p["ht_alma"], 1.12 * math.sqrt(E / fy), 1.40 * math.sqrt(E / fy)
        if la > lra:
            raise NotImplementedError("U fletido em y com alma esbelta (h/tw > 1,40√(E/fy)): Wef não implementado")
        Mfla = _mn(Mpl, fy * W, None, la, lpa, lra)  # até λr, bef = b (F.3.2) → Wef = W
        r.update(Mn_FLA=Mfla, lambda_FLA=(la, lpa, lra))
        Mn = min(Mn, Mfla)
    r["MRd"] = min(Mn, 1.5 * W * fy) / GA1
    return r


def _wef_tubo(sec, eixo, lam, raiz):
    """Wef (F.3.2, σ = fy, ca = 0,38) de tubo retangular com a parede comprimida de esbeltez lam."""
    p, t = sec.props, sec.props["t"]
    H = p["h"] if eixo == "x" else p["b"]          # dimensão na direção da flexão
    b = lam * t
    bef = min(1.92 * t * raiz * (1 - 0.38 / lam * raiz), b)
    dA, d = (b - bef) * t, (H - t) / 2              # área perdida na parede comprimida, braço
    e = dA * d / (sec.A - dA)                       # deslocamento da LN para o lado tracionado
    Ief = p["I" + eixo] - dA * d ** 2 - (sec.A - dA) * e ** 2
    return Ief / (H / 2 + e)


def _flexao_caixao(sec, mat, eixo, Lb, cb):
    """Tabela G.1: seções caixão/tubulares retangulares (FLT, FLM com Wef, FLA) e sólidas retangulares (FLT).
    FLT só em torno do eixo de maior inércia: λ = Lb/r (outro eixo), λp = 0,13E√(JA)/Mpl,
    λr = 2,00E√(JA)/Mr, Mcr = 2,00·Cb·E·√(JA)/λ; Mr = (fy − σr)W adotado (a favor da segurança)."""
    p, E, fy = sec.props, mat.E, mat.fy
    o = "y" if eixo == "x" else "x"
    Mpl, W = p["Z" + eixo] * fy, p["W" + eixo]
    raiz = math.sqrt(E / fy)
    r = dict(Mpl=Mpl, item="5.4.2 / anexo G (tabela G.1)")
    Mn = Mpl
    if Lb and p["I" + eixo] > p["I" + o]:
        JA = math.sqrt(p["J"] * sec.A)
        Mr = 0.7 * fy * W
        lam, lp, lr = Lb / p["r" + o], 0.13 * E * JA / Mpl, 2.00 * E * JA / Mr
        Mcr = 2.00 * cb * E * JA / lam
        Mn = _mn(Mpl, Mr, Mcr, lam, lp, lr, cb)
        r.update(Mn_FLT=Mn, Mcr_FLT=Mcr, lambda_FLT=(lam, lp, lr))
    if p["tipo"] == "tubo_retangular":
        # parede comprimida (FLM) e paredes paralelas ao plano de flexão (FLA)
        lm, la = (p["bt_mesa"], p["ht_alma"]) if eixo == "x" else (p["ht_alma"], p["bt_mesa"])
        lpm, lrm = 1.12 * raiz, 1.40 * raiz
        if lm > lrm:
            Wef = _wef_tubo(sec, eixo, lm, raiz)
            Mflm = fy * Wef ** 2 / W                   # Mcr = fy·Wef²/W
            r["Wef"] = Wef
        else:
            Mflm = _mn(Mpl, fy * W, None, lm, lpm, lrm)  # Mr = fy·Wef, Wef = W até λr
        lpa, lra = 2.42 * raiz, 5.70 * raiz
        if la > lra:
            raise NotImplementedError("tubo retangular com alma esbelta (h/t > 5,70√(E/fy)): fora da tabela G.1")
        Mfla = _mn(Mpl, fy * W, None, la, lpa, lra)
        r.update(Mn_FLM=Mflm, Mn_FLA=Mfla, lambda_FLM=(lm, lpm, lrm), lambda_FLA=(la, lpa, lra))
        Mn = min(Mn, Mflm, Mfla)
    r["MRd"] = min(Mn, 1.5 * W * fy) / GA1
    return r


def _flexao_tubo_circular(sec, mat, eixo):
    """Tabela G.1, tubos circulares (FLL): λ = D/t, λp = 0,07E/fy, λr = 0,31E/fy;
    Mn = Mpl | (0,021E/(D/t) + fy)·W | 0,33E·W/(D/t); D/t ≤ 0,45E/fy (expressões iguais às do AISC 360-05 F8)."""
    p, E, fy = sec.props, mat.E, mat.fy
    Dt, W = p["D_t"], p["W" + eixo]
    Mpl = p["Z" + eixo] * fy
    if Dt > 0.45 * E / fy:
        raise NotImplementedError(f"tubo circular com D/t = {Dt:.0f} > 0,45E/fy: fora da tabela G.1")
    lp, lr = 0.07 * E / fy, 0.31 * E / fy
    if Dt <= lp:
        Mn = Mpl
    elif Dt <= lr:
        Mn = min((0.021 * E / Dt + fy) * W, Mpl)
    else:
        Mn = 0.33 * E * W / Dt
    return dict(MRd=min(Mn, 1.5 * W * fy) / GA1, Mn_FLL=Mn, lambda_FLL=(Dt, lp, lr), Mpl=Mpl,
                item="5.4.2 / anexo G (tubo circular)")


def _flexao_compacta(sec, mat, eixo):
    # barra circular maciça: sem FLT nem flambagem local → Mpl ≤ 1,5·W·fy (5.4.2.2)
    p = sec.props
    Mn = min(p["Z" + eixo] * mat.fy, 1.5 * p["W" + eixo] * mat.fy)
    return dict(MRd=Mn / GA1, item="5.4.2.2 (barra circular maciça)")


def _cortante(Aw, lam, kv, E, fy, item):
    # NBR 8800 5.4.3.1.1: λp = 1,10√(kv·E/fy), λr = 1,37√(kv·E/fy)
    Vpl = 0.6 * Aw * fy
    lp, lr = 1.10 * math.sqrt(kv * E / fy), 1.37 * math.sqrt(kv * E / fy)
    V = Vpl if lam <= lp else (lp / lam * Vpl if lam <= lr else 1.24 * (lp / lam) ** 2 * Vpl)
    return dict(VRd=V / GA1, Vpl=Vpl, Aw=Aw, kv=kv, lambda_=(lam, lp, lr), item=item)


def _cortante_tubo_circular(sec, mat, Lv=None):
    # 5.4.3.6: VRd = 0,5·τcr·Ag/γa1; τcr = maior entre 1,60E/(√(Lv/D)(D/t)^1,25) e 0,78E/(D/t)^1,5, ≤ 0,60fy.
    # Lv: distância entre as seções de cortante máxima e nula. Sem Lv: só o segundo termo (a favor da segurança)
    E, fy, Dt, D = mat.E, mat.fy, sec.props["D_t"], sec.props["h"]
    t2 = 0.78 * E / Dt ** 1.5
    t1 = 1.60 * E / (math.sqrt(Lv / D) * Dt ** 1.25) if Lv else 0.0
    tcr = min(max(t1, t2), 0.60 * fy)
    return dict(VRd=0.5 * tcr * sec.A / GA1, tau_cr=tcr, Lv=Lv, item="5.4.3.6")


def _cortante_cantoneira(sec, mat, b):
    r = _cortante(b * sec.props["t"], b / sec.props["t"], 1.2, mat.E, mat.fy, "5.4.3 (análogo de 5.4.3.5)")
    r["aviso"] = ("cantoneira simples: NBR 8800:2008 sem item de cortante; usado Aw = b·t, kv = 1,2 "
                  "(análogo de 5.4.3.5; AISC 360 G4)")
    return r


def _dobro(r):
    return dict(r, VRd=2 * r["VRd"], item=r["item"] + " (barra composta: 2 × perfil isolado)")


def cortante_y(sec, mat, kv=5.0, Lv=None):
    """Força cortante paralela à alma (eixo y): I/U (5.4.3.1, Aw = d·tw), tubo retangular (5.4.3.2,
    Aw = 2h·t com h = altura plana ≈ lado − 3t), tubo circular (5.4.3.6, Lv opcional), cantoneira (aba h, aviso)."""
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    if tipo == "I_chapa":  # só a alma do I (chapa desprezada)
        return cortante_y(_componente(sec), mat, kv, Lv)
    if tipo == "dupla":
        return _dobro(cortante_y(_componente(sec), mat, kv, Lv))
    if tipo in ("I", "U"):
        return _cortante(p["h"] * p["tw"], p["ht_alma"], kv, E, fy, "5.4.3.1")
    if tipo == "tubo_retangular":
        return _cortante(2 * p["ht_alma"] * p["t"] ** 2, p["ht_alma"], kv, E, fy, "5.4.3.2")
    if tipo == "tubo_circular":
        return _cortante_tubo_circular(sec, mat, Lv)
    if tipo == "L":
        return _cortante_cantoneira(sec, mat, p["h"])
    return dict(VRd=0.6 * fy * sec.A * 0.6 / GA1, item="5.4.3 (aprox.)")  # ponytail: Aw ≈ 0,6A em maciças


def cortante_x(sec, mat, Lv=None):
    """Cortante paralela às mesas: I/U (5.4.3.5, Aw = 2bf·tf, kv = 1,2, λ = b/t da mesa), tubo retangular
    (5.4.3.2 com as paredes de largura b), tubo circular (5.4.3.6), cantoneira (aba b, aviso)."""
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    if tipo == "I_chapa":
        return cortante_x(_componente(sec), mat, Lv)
    if tipo == "dupla":
        return _dobro(cortante_x(_componente(sec), mat, Lv))
    if tipo in ("I", "U"):
        return _cortante(2 * p["b"] * p["tf"], p["bt_mesa"], 1.2, E, fy, "5.4.3.5")
    if tipo == "tubo_retangular":
        return _cortante(2 * p["bt_mesa"] * p["t"] ** 2, p["bt_mesa"], 5.0, E, fy, "5.4.3.2")
    if tipo == "L":
        return _cortante_cantoneira(sec, mat, p["b"])
    return cortante_y(sec, mat, Lv=Lv)


def _t_torcao(p):
    """Espessura da tensão máxima de torção uniforme τ = T·t/J: parede mais espessa; barra maciça retangular:
    menor lado; circular maciça: raio (τ = T·r/J exato)."""
    if "tw" in p:
        return max(p["tw"], p["tf"])
    if p["tipo"] == "retangular":
        return min(p["b"], p["h"])
    if p["tipo"] == "circular":
        return p["h"] / 2
    return p["t"]


def torcao(sec, mat, L=None, gama=GA1):
    """Momento de torção resistente de cálculo TRd. Tubo circular (5.5.2.2): WT = π(D − t)²t/2, τcr = maior
    entre 1,23E/(√(L/D)(D/t)^1,25) e 0,60E/(D/t)^1,5, ≤ 0,6fy (L = comprimento da barra; sem L só o segundo,
    a favor). Tubo retangular (5.5.2.3): WT = 2(B − t)(H − t)t − 4,5(4 − π)t³, h/t do lado maior (plano ≈ lado
    − 3t): ≤ 2,45√(E/fy) → 0,6fy; ≤ 3,07√(E/fy) → 0,6fy·2,45√(E/fy)/(h/t); ≤ 260 → 0,458π²E/(h/t)²; acima
    → fora do escopo. Expressões iguais às do AISC 360-05 H3.1. Seções abertas e maciças (5.5.2; critério
    do CYPE e AISC 360-05 H3.3 b): τ = T·t/J ≤ 0,6fy → TRd = 0,6fy·(J/t)/γ."""
    p, E, fy = sec.props, mat.E, mat.fy
    tipo = p["tipo"]
    if tipo == "tubo_circular":
        D, t = p["h"], p["t"]
        WT = math.pi * (D - t) ** 2 * t / 2
        t1 = 1.23 * E / (math.sqrt(L / D) * (D / t) ** 1.25) if L else 0.0
        tcr = min(max(t1, 0.60 * E / (D / t) ** 1.5), 0.6 * fy)
        r = dict(TRd=tcr * WT / gama, WT=WT, tau_cr=tcr, L=L, item="5.5.2.2")
        if not L:
            r["aviso"] = "tubo circular sem o comprimento L da barra: só 0,60E/(D/t)^1,5 (a favor)"
        return r
    if tipo == "tubo_retangular":
        t = p["t"]
        WT = 2 * (p["b"] - t) * (p["h"] - t) * t - 4.5 * (4 - math.pi) * t ** 3
        ht, raiz = max(p["ht_alma"], p["bt_mesa"]), math.sqrt(E / fy)
        if ht <= 2.45 * raiz:
            tcr = 0.6 * fy
        elif ht <= 3.07 * raiz:
            tcr = 0.6 * fy * 2.45 * raiz / ht
        elif ht <= 260:
            tcr = 0.458 * math.pi ** 2 * E / ht ** 2
        else:
            raise NotImplementedError(f"tubo retangular com h/t = {ht:.0f} > 260: torção fora do escopo (5.5.2.3)")
        return dict(TRd=tcr * WT / gama, WT=WT, tau_cr=tcr, lambda_=(ht, 2.45 * raiz, 3.07 * raiz), item="5.5.2.3")
    WT = p["J"] / _t_torcao(p)
    return dict(TRd=0.6 * fy * WT / gama, WT=WT, item="5.5.2 (seção aberta: τ = T·t/J ≤ 0,6fy/γ)")


def _tau_cortante(p, A, Vy, Vx):
    """τ máximo da força cortante, somando as duas direções (envelope): I/U/Ue pela fórmula de Jourawski na
    linha neutra (S = Zx/2, simetria em x) e parábola nas mesas (1,5·Vx/(2b·tf)); cantoneira 1,5·V/(aba·t);
    retangular 1,5·V/A; circular 4/3·V/A."""
    tipo = p["tipo"]
    if tipo in ("I", "U", "Ue"):
        tw, tf = p.get("tw", p.get("t")), p.get("tf", p.get("t"))
        return abs(Vy) * p["Zx"] / 2 / (p["Ix"] * tw) + 1.5 * abs(Vx) / (2 * p["b"] * tf)
    if tipo == "L":
        return 1.5 * (abs(Vy) / p["h"] + abs(Vx) / p["b"]) / p["t"]
    k = 4 / 3 if tipo == "circular" else 1.5
    return k * (abs(Vy) + abs(Vx)) / A


def von_mises_aberta(sec, mat, e, Tmax, gama=GA1):
    """Critério adicional de Von Mises (o CYPE o aplica quando TSd > 0,2·TRd e há outros esforços; não é da
    NBR 8800) com tensões elásticas (AISC 360-05 H3.3). Envelope conservador: σ = |N|/A + |Mx|/Wx + |My|/Wy
    + σw e τ = |T|·t/J + τV somam os máximos mesmo em pontos diferentes. Empenamento: σw = B·ωn,máx/Cw com
    B ≤ Tmáx·a, a = √(E·Cw/(G·J)) (majorante das soluções da torção não uniforme de Seaburg & Carter, AISC
    Design Guide 9, 1997, com empenamento impedido num apoio). taxa = √(σ² + 3τ²)/(fy/γ)."""
    p, E, G = sec.props, mat.E, mat.G
    N, Mx, My, Vy, Vx, T = (abs(float(e.get(k, 0.0))) for k in ("N", "Mx", "My", "Vy", "Vx", "T"))
    sig = N / sec.A + Mx / p["Wx"] + My / p["Wy"]
    Cw, wn = p.get("Cw") or 0.0, _secoes.wn_max(p)
    sw = abs(Tmax) * wn * math.sqrt(E / (G * p["J"] * Cw)) if Cw > 0 and wn > 0 else 0.0
    tau = T * _t_torcao(p) / p["J"] + _tau_cortante(p, sec.A, Vy, Vx)
    vm = math.sqrt((sig + sw) ** 2 + 3 * tau ** 2)
    return dict(taxa=vm / (mat.fy / gama), sigma=sig, sigma_w=sw, tau=tau, sigma_vm=vm,
                item="critério adicional de Von Mises (CYPE; tensões elásticas AISC 360-05 H3.3)")


def interacao(NSd, NRd, MxSd, MxRd, MySd, MyRd):
    # NBR 8800 5.5.1.2
    n = abs(NSd) / NRd
    m = abs(MxSd) / MxRd + abs(MySd) / MyRd
    return n + 8 / 9 * m if n >= 0.2 else n / 2 + m


def esforcos_nbr(linha):
    """Ponte solver → NBR (ESPEC §3), único lugar da tradução de eixos.
    linha = (x, N, Vy, Vz, T, My, Mz) locais do solver; Secao.Iy (local y) = Ix da NBR (forte).
    Mx(NBR) = My local; My(NBR) = Mz local; Vy(NBR, paralela à alma) = Vz local; Vx(NBR) = Vy local.
    Sinais preservados (N > 0 tração); as verificações usam valores absolutos."""
    _x, N, Vy, Vz, T, My, Mz = (float(v) for v in linha)
    return dict(N=N, Mx=My, My=Mz, Vy=Vz, Vx=Vy, T=T)


def _json(v):
    """detalhes só com tipos JSON (também usada pela NBR 14762): numpy → float, tupla → lista,
    chave → str, ±inf/nan → None."""
    if isinstance(v, dict):
        return {str(k): _json(x) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [_json(x) for x in v]
    if isinstance(v, (bool, np.bool_)):
        return bool(v)
    if isinstance(v, (int, np.integer)):
        return int(v)
    if isinstance(v, (float, np.floating)):
        return float(v) if math.isfinite(v) else None
    return v


_ESFORCOS = ("N", "Mx", "My", "Vy", "Vx", "T")


def _validar_entrada(funcao, pontos, positivos, nao_negativos):
    """Comprimentos/coeficientes finitos (> 0 ou ≥ 0) e esforços finitos; senão ValueError com o campo."""
    erros = []
    for nome, v in {**positivos, **nao_negativos}.items():
        try:
            x = float(v)
        except (TypeError, ValueError):
            x = math.nan
        if not math.isfinite(x) or x < 0 or (x == 0 and nome in positivos):
            erros.append(f"{nome} = {v!r}: número finito {'> 0' if nome in positivos else '≥ 0'} obrigatório")
    for i, e in enumerate(pontos):
        for k in _ESFORCOS:
            try:
                x = float(e.get(k, 0.0))
            except (TypeError, ValueError):
                x = math.nan
            if not math.isfinite(x):
                erros.append(f"esforcos[{i}].{k} = {e.get(k)!r}: número finito obrigatório")
    if erros:
        raise ValueError(f"{funcao}: " + "; ".join(erros))


_ITENS = {"normal": "5.2/5.3", "esbeltez": "5.2.8.1/5.3.4", "cortante_y": "5.4.3.1", "cortante_x": "5.4.3.5",
          "interacao": "5.5.1.2", "torcao": "5.5.2", "torcao_interacao": "5.5.2 (AISC 360-05 H3.2)",
          "von_mises": "critério adicional (Von Mises)", "nao_verificado": "ver avisos"}

_TUBOS = ("tubo_circular", "tubo_retangular")


def tubo_espessura_calculo(sec):
    """Tubo com costura (soldado por resistência elétrica): espessura de cálculo 0,93t em todas as
    propriedades (AISC 360-05 B3.12; o CYPE aplica 0,93t no D/t do cortante de tubo circular)."""
    p = sec.props
    if p.get("tipo") == "tubo_circular":
        return _secoes.secao_tubo_circular(p["h"], 0.93 * p["t"], nome=sec.nome)
    if p.get("tipo") == "tubo_retangular":
        return _secoes.secao_tubo_retangular(p["h"], p["b"], 0.93 * p["t"], nome=sec.nome)
    raise ValueError(f"tubo_com_costura só se aplica a tubos (seção '{sec.nome}' é '{p.get('tipo')}')")


def verificar_barra_aco(secao, material, esforcos, Lx, Ly, Lb, Cb=1.0, Lz=None, An=None, Ct=1.0,
                        ligacao_uma_aba=None, L=None, Lv=None, tubo_com_costura=False,
                        ponto_aplicacao="centro", C2=None, Lt=None):
    """esforcos: dict (ou lista de dicts, um por ponto da barra) N (+ tração, − compressão), Mx, My,
    Vy (paralela à alma), Vx, T — valores de cálculo na notação da NBR (ver esforcos_nbr).
    Lx, Ly, Lz: comprimentos de flambagem (K·L; Lz None ou 0 → max(Lx, Ly)); Lb: comprimento destravado.
    L: comprimento da barra (torção de tubo circular, 5.5.2.2); Lv: cortante de tubo circular (5.4.3.6,
    padrão L). Sem L/Lv: só os termos que não dependem deles (a favor). tubo_com_costura: espessura 0,93t.
    Comprimento ≤ 0 (Lb < 0), esforço não finito, Cb > 3 ou Ct fora de (0; 1] → ValueError.
    ligacao_uma_aba: cantoneira simples ligada por uma aba (E.1.4), ver compressao.
    ponto_aplicacao ('centro' | 'topo' | 'base') e C2: altura da carga no Mcr da FLT (ver flexao_x).
    Lt: comprimento destravado da barra tracionada (5.2.8: L/r, SEM K), número ou (Ltx, Lty); padrão L;
    sem L nem Lt usa Lx, Ly (equivale a K = 1).
    Resistências calculadas uma vez; devolve o ponto determinante (ResultadoNorma, ESPEC §4)."""
    pontos = [esforcos] if isinstance(esforcos, dict) else list(esforcos)
    if not pontos:
        raise ValueError("esforcos: lista vazia")
    _validar_entrada("verificar_barra_aco", pontos, dict(Lx=Lx, Ly=Ly, Cb=Cb),
                     dict(Lb=Lb, Lz=Lz or 0.0, L=L or 0.0, Lv=Lv or 0.0, C2=C2 or 0.0))
    if Cb > 3.0:
        raise ValueError(f"verificar_barra_aco: Cb = {Cb:g} > 3,0 (NBR 8800 5.4.2.3)")
    if not 0 < Ct <= 1.0:
        raise ValueError(f"verificar_barra_aco: Ct = {Ct!r} fora de (0; 1,0] (NBR 8800 5.2.5)")
    g = lambda e, k: float(e.get(k, 0.0))  # noqa: E731
    d, avisos = {}, []
    if tubo_com_costura:
        secao = tubo_espessura_calculo(secao)
        avisos.append("tubo com costura: espessura de cálculo 0,93t (AISC 360-05 B3.12)")

    def calc(nome, f, *args):
        try:
            d[nome] = f(secao, material, *args)
        except KeyError as e:
            avisos.append(f"{nome}: perfil sem dados ({e.args[0]}) — verificação não feita")
        except NotImplementedError as e:
            avisos.append(f"{nome}: {e} — verificação não feita")
        return d.get(nome)

    tem_tracao = any(g(e, "N") > 0 for e in pontos)
    tem_compr = any(g(e, "N") < 0 for e in pontos)
    if tem_tracao:
        calc("tracao", tracao, An, Ct)
    if tem_compr:
        calc("compressao", compressao, Lx, Ly, Lz, ligacao_uma_aba)
    calc("flexao_x", flexao_x, Lb, Cb, ponto_aplicacao, C2)
    calc("flexao_y", flexao_y)
    calc("cortante_y", cortante_y, 5.0, Lv or L)
    calc("cortante_x", cortante_x, Lv or L)
    Tmax = max(abs(g(e, "T")) for e in pontos)
    if Tmax > 0:
        calc("torcao", torcao, L)
    for nome, v in list(d.items()):
        for a in ([v["aviso"]] if "aviso" in v else []) + v.get("avisos", []):
            avisos.append(f"{nome}: {a}")

    # esbeltez: 5.3.4 (KL/r ≤ 200, compressão) e 5.2.8 (L/r ≤ 300, tração) como taxa.
    # NBR 8800 5.2.8.1: "L é o comprimento destravado" — sem o K, que é da compressão (5.3.4).
    p = secao.props
    taxa_esb = 0.0
    try:
        def esb_de(ax, ay):
            v = max(ax / p["rx"], ay / p["ry"])
            return max(v, max(ax, ay) / p["rmin"]) if "rmin" in p else v  # cantoneira: eixo de menor inércia
        Ltx, Lty = (Lt if isinstance(Lt, (tuple, list)) else (Lt, Lt)) if Lt else ((L, L) if L else (Lx, Ly))
        cand = []  # (taxa, valor, limite, item)
        if tem_compr:
            e_c = esb_de(Lx, Ly)
            cand.append((e_c / 200, e_c, 200, "5.3.4"))
        if tem_tracao:
            e_t = esb_de(float(Ltx), float(Lty))
            cand.append((e_t / 300, e_t, 300, "5.2.8.1"))
        if cand:
            taxa_esb, esb, lim, it = max(cand)
            d["esbeltez"] = dict(valor=esb, limite=lim, item=it)
            if taxa_esb > 1:
                avisos.append(f"esbeltez {esb:.0f} > {lim} ({it})" + (
                    "; a 5.2.8 é recomendação, reprovada aqui a favor da segurança" if lim == 300 else ""))
    except KeyError as e:
        avisos.append(f"esbeltez: perfil sem dados ({e.args[0]})")

    def R(nome, chave):
        return d[nome][chave] if nome in d else None

    NtRd, NcRd = R("tracao", "NtRd"), R("compressao", "NcRd")
    MxRd, MyRd = R("flexao_x", "MRd"), R("flexao_y", "MRd")
    VyRd, VxRd = R("cortante_y", "VRd"), R("cortante_x", "VRd")
    TRd = R("torcao", "TRd")
    # esforço desprezível (ruído numérico): |S| ≤ 1e-6 × resistência plástica de referência (A·fy, W·fy, J·fy)
    fy = material.fy
    tol = {k: 1e-6 * fy * (p.get(c) or 0.0) for k, c in
           (("N", "A"), ("Mx", "Wx"), ("My", "Wy"), ("Vy", "A"), ("Vx", "A"), ("T", "J"))}
    pior, sem_resist, vm_pior = None, set(), None
    for i, e in enumerate(pontos):
        N, Mx, My, Vy, Vx, T = (g(e, k) if abs(g(e, k)) > tol[k] else 0.0
                                for k in ("N", "Mx", "My", "Vy", "Vx", "T"))
        NRd = NtRd if N > 0 else (NcRd if N < 0 else 1.0)
        sem_resist |= {k for k, v, R_ in (("N", N, NRd), ("Mx", Mx, MxRd), ("My", My, MyRd), ("Vy", Vy, VyRd),
                                          ("Vx", Vx, VxRd), ("T", T, TRd)) if v != 0.0 and R_ is None}
        taxas = {"esbeltez": taxa_esb}
        if NRd is not None:
            taxas["normal"] = abs(N) / NRd
        if VyRd is not None:
            taxas["cortante_y"] = abs(Vy) / VyRd
        if VxRd is not None:
            taxas["cortante_x"] = abs(Vx) / VxRd
        mx_rd = MxRd if MxRd is not None else (1.0 if Mx == 0 else None)  # momento nulo dispensa a resistência
        my_rd = MyRd if MyRd is not None else (1.0 if My == 0 else None)
        if None not in (NRd, mx_rd, my_rd):
            taxas["interacao"] = interacao(N, NRd, Mx, mx_rd, My, my_rd)
        if TRd is not None:
            taxas["torcao"] = abs(T) / TRd
            relevante = abs(T) > 0.2 * TRd and any((N, Mx, My, Vy, Vx))
            if relevante and p["tipo"] in _TUBOS and None not in (NRd, mx_rd, my_rd):
                # AISC 360-05 H3.2 (tubos com T > 0,2·TRd); cortantes das duas direções somadas (a favor)
                v = (abs(Vy) / VyRd if VyRd else 0.0) + (abs(Vx) / VxRd if VxRd else 0.0)
                taxas["torcao_interacao"] = (abs(N) / NRd + abs(Mx) / mx_rd + abs(My) / my_rd) \
                    + (v + abs(T) / TRd) ** 2
            elif relevante and p["tipo"] not in _TUBOS:
                try:
                    vm = von_mises_aberta(secao, material, dict(N=N, Mx=Mx, My=My, Vy=Vy, Vx=Vx, T=T), Tmax)
                except KeyError:  # perfil sem dados para as tensões: torção não verificada → reprova
                    vm = None
                    sem_resist.add("T")
                if vm:
                    taxas["von_mises"] = vm["taxa"]
                    if vm_pior is None or vm["taxa"] > vm_pior["taxa"]:
                        vm_pior = dict(vm, indice=i)
        # VC-integracao-001 / ESPEC §7.2: taxa_max = pior critério de esforço; a esbeltez (5.2.8/5.3.4,
        # independente do esforço) só reprova e fica em detalhes.taxas
        crit = max((k for k in taxas if k != "esbeltez"), key=taxas.get, default="esbeltez")
        if pior is None or taxas[crit] > pior[0]:
            pior = (taxas[crit], crit, i, taxas)
    taxa, crit, indice, taxas = pior
    d["taxas"] = taxas
    if vm_pior is not None:
        d["von_mises"] = vm_pior
        avisos.append("torção > 0,2·TRd com outros esforços: Von Mises por envelope (critério adicional, não é da "
                      "NBR 8800), com empenamento majorado (B ≤ T·a, AISC Design Guide 9)")
    ok = taxa <= 1.0 and taxa_esb <= 1.0
    if sem_resist:  # esforço presente sem resistência calculada: conservador, reprova
        ok, crit = False, "nao_verificado"
        avisos.append(f"esforço sem resistência calculada ({', '.join(sorted(sem_resist))}): barra reprovada "
                      "(nao_verificado)")
    for a in avisos:
        log.debug("verificar_barra_aco %s: %s", secao.nome, a)
    return dict(taxa_max=float(taxa), criterio=crit, item=_ITENS[crit], ok=bool(ok), indice=indice,
                detalhes=_json(d), avisos=avisos)


# NBR 8800:2008 tabela C.1 (deslocamentos máximos). Fonte pública da tabela e notas:
# L. B. dos Santos, "Ações e segurança — notas de aula ECIV059 Estruturas de Aço", UFAL, 04/2026
# (eduCAPES, CC BY-NC-SA); "Deslocamentos máximos em vigas NBR 8800:2008", Portal do Projetista,
# 01/11/2022 (nota da viga de piso: com alvenaria solidarizada, também ≤ 15 mm). Acesso 2026-10-20.
# Combinação ELS: a norma deixa a escolha ao responsável (4.7.7); adotado, a favor da segurança:
# deformação elástica reversível → frequente (em vez da quase permanente de "aparência");
# dano a alvenaria/pilar apoiado (irreversível) → rara; horizontais → frequente (ESPEC §6.5 regra 4).
_LIMITES = {
    "viga_cobertura": (250, "L", None, "ELS_FREQ", "C.1 vigas de cobertura"),
    "viga_piso": (350, "L", None, "ELS_FREQ", "C.1 vigas de piso"),
    "viga_alvenaria": (350, "L", 0.015, "ELS_RARA", "C.1 vigas de piso com alvenaria solidarizada (nota h): também ≤ 15 mm"),
    "viga_pilar": (500, "L", None, "ELS_RARA", "C.1 vigas que suportam pilares"),
    "terca": (180, "L", None, "ELS_RARA", "C.1 terças de cobertura (ações de mesmo sentido da permanente)"),
    "balanco_cobertura": (250, "2L", None, "ELS_FREQ", "C.1 nota a (L = 2 × balanço), linha vigas de cobertura"),
    "balanco": (350, "2L", None, "ELS_FREQ", "C.1 nota a (L = 2 × balanço), linha vigas de piso"),
    "topo_pilar": (400, "H", None, "ELS_FREQ", "C.1 edifícios de dois ou mais pavimentos, topo em relação à base"),
    "entre_pisos": (500, "h", None, "ELS_FREQ", "C.1 edifícios de dois ou mais pavimentos, entre pisos"),
    "galpao_topo": (300, "H", None, "ELS_FREQ", "C.1 galpões e edifícios de um pavimento, topo em relação à base"),
}


def limite_deslocamento(elemento, L_m):
    """Deslocamento máximo (NBR 8800:2008 anexo C, tabela C.1). L_m: vão (balanço: comprimento
    do balanço; a função dobra), H (altura total) ou h (altura do andar), em m."""
    if elemento not in _LIMITES:
        raise ValueError(f"elemento '{elemento}' desconhecido; use um de {sorted(_LIMITES)}")
    if not L_m > 0:
        raise ValueError("L_m deve ser > 0")
    div, base, absoluto, estado, nota = _LIMITES[elemento]
    Lef = 2 * L_m if base == "2L" else L_m
    lim = Lef / div
    razao = f"{base}/{div}" if base != "2L" else f"2L/{div}"
    if absoluto is not None and absoluto < lim:
        lim = absoluto
    if absoluto is not None:
        razao += f" e {absoluto * 1000:g} mm"
    return dict(limite_m=lim, razao=razao, item=f"NBR 8800 {nota}", estado=estado)


def coef_B2(soma_NSd, delta_h, soma_HSd, h, Rs=0.85):
    """NBR 8800:2008 anexo D (D.2.3): B2 = 1 / (1 − (1/Rs)·Δh·ΣNSd / (h·ΣHSd)).
    ΣNSd: força vertical total no andar (kN); Δh: deslocamento horizontal relativo do andar (m)
    da análise de 1ª ordem; ΣHSd: força cortante no andar (kN); h: altura do andar (m).
    Rs = 0,85 quando o sistema resistente a ações horizontais é só de pórticos rígidos; 1,0 nos demais.
    Fontes públicas: Bellei & Bellei, "Edifícios de pequeno porte estruturados em aço", CBCA, 4ª ed.
    2011, pp. 40–41; Medeiros & Resende, TCC UFSJ 2023 (método da amplificação dos esforços)."""
    if soma_HSd == 0 or h <= 0 or Rs <= 0:
        raise ValueError("coef_B2: ΣHSd = 0 (ou h, Rs ≤ 0) — B2 indefinido sem força horizontal no andar")
    k = abs(delta_h) * abs(soma_NSd) / (Rs * h * abs(soma_HSd))
    if k >= 1:
        raise ValueError(f"coef_B2: Δh·ΣN/(Rs·h·ΣH) = {k:.3f} ≥ 1 — andar instável em 2ª ordem")
    return 1 / (1 - k)


def deslocabilidade(B2):
    """NBR 8800 4.9.4 (2008; mesmos limites em 4.10.4 da 2024 sem imperfeições iniciais)."""
    return "pequena" if B2 <= 1.1 else ("media" if B2 <= 1.4 else "grande")
