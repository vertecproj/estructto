"""Comparação memória independente (pff-mle.py) × código (aco/nbr14762.py).

python -X utf8 verificacao/memorias/pff-comparar.py   (a partir de software/)
"""
import importlib.util
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(AQUI)))
_sp = importlib.util.spec_from_file_location("mle", os.path.join(AQUI, "pff-mle.py"))
mle = importlib.util.module_from_spec(_sp)
_sp.loader.exec_module(mle)

from prossen_estrutural import secoes as s  # noqa: E402
from prossen_estrutural.modelo import Material, MPa  # noqa: E402
from prossen_estrutural.aco import nbr14762 as p  # noqa: E402

LIN = []


def cmp(caso, esp, obt, tol=0.01):
    d = abs(obt - esp) / abs(esp)
    LIN.append((caso, esp, obt, d, "OK" if d <= tol else ("DIF+" if obt > esp else "DIF-")))


def mat(fy, fu=None):
    return Material(f"fy{fy}", 200e3 * MPa, 77e3 * MPa, fy=fy * MPa, fu=(fu or 1.3 * fy) * MPa)


def secao_codigo(nome, h, b, D, t, ri):
    """Caminho real do software: catálogo (cantos vivos) e, se o pedido da 003 for aceito, ri gravado."""
    try:
        sec = s.catalogo(nome)
    except Exception:  # noqa: BLE001
        sec = s.secao_Ue(h / 1e3, b / 1e3, D / 1e3, t / 1e3)
    return sec


CASOS = [  # nome, h, b, D, t, fy, KLx, KLy, KLz (mm)
    ("Ue 250x85x25x2,00", 250, 85, 25, 2.00, 300, 2400, 1200, 1200),     # [UFAL-C]
    ("Ue 300x85x25x3,00", 300, 85, 25, 3.00, 240, 10290, 2572.5, 2572.5),  # [CBCA 4.12]
    ("Ue 150x60x20x2,00", 150, 60, 20, 2.00, 345, 3000, 1500, 1500),
    ("Ue 200x75x25x2,25", 200, 75, 25, 2.25, 345, 4000, 2000, 2000),
    ("Ue 100x50x17x1,20", 100, 50, 17, 1.20, 250, 2000, 1000, 1000),
]

for nome, h, b, D, t, fy, Lx, Ly, Lz in CASOS:
    u = mle.Ue(h, b, D, t, t)              # NBR 6355: ri = t
    m_ = mat(fy)
    rc = mle.compressao(u, fy, 200e3, 77e3, Lx, Ly, Lz)
    rf = mle.flexao(u, fy, 200e3, 77e3, Ly, 1.0)
    sec = secao_codigo(nome, h, b, D, t, t)
    ri_cod = sec.props.get("ri", 0.0)
    tag = f"{nome} (código ri={ri_cod * 1e3:g} mm)"
    cmp(f"{tag} A bruta", u.A * 1e-6, sec.A, 0.01)
    cc = p.compressao(sec, m_, Lx / 1e3, Ly / 1e3, Lz / 1e3)
    cmp(f"{tag} Ne", rc["Ne"] / 1e3, cc.get("Ne", cc.get("Ne_", float("nan"))) if "Ne" in cc else float("nan"))
    cmp(f"{tag} Nc,Rd", rc["NcRd"] / 1e3, cc["NcRd"])
    fx = p.flexao_x(sec, m_, Ly / 1e3, 1.0)
    cmp(f"{tag} MRd,esc (Wef·fy/1,1)", rf["MRd_esc"] / 1e6, fx["MRd_esc"])
    cmp(f"{tag} MRd,FLT", rf["MRd_FLT"] / 1e6, fx.get("MRd_FLT", fx["MRd"]))
    # mesma seção com ri = t gravado (o que o pedido 003→018 propõe)
    sec.props["ri"] = t / 1e3
    cc = p.compressao(sec, m_, Lx / 1e3, Ly / 1e3, Lz / 1e3)
    cmp(f"{nome} (código com ri=t, A de cantos vivos) Nc,Rd", rc["NcRd"] / 1e3, cc["NcRd"])

# cortante 9.8.3 (independente): h = altura plana da alma; kv = 5
for h, t, fy in ((250, 2.25, 350), (150, 2.0, 345), (300, 3.0, 250)):
    hp = h - 4 * t
    r_ = math.sqrt(200e3 * 5 / fy)
    lam = hp / t
    if lam <= 1.08 * r_:
        V = 0.6 * fy * hp * t
    elif lam <= 1.40 * r_:
        V = 0.65 * t ** 2 * math.sqrt(5 * fy * 200e3)
    else:
        V = 0.905 * 200e3 * 5 * t ** 3 / hp
    sec = s.secao_Ue(h / 1e3, 0.085, 0.025, t / 1e3)
    sec.props["ri"] = t / 1e3
    cmp(f"cortante Ue h={h} t={t} fy={fy} (h/t={lam:.1f})", V / 1.10 / 1e3, p.cortante(sec, mat(fy))["VRd"])

if __name__ == "__main__":
    w1 = max(len(c) for c, *_ in LIN)
    for c, e_, o, d, st in LIN:
        print(f"{st:4} {c:<{w1}}  esp={e_:.6g}  obt={o:.6g}  dif={d * 100:.2f}%")
    print(f"{sum(1 for *_, st in LIN if st != 'OK')} diferenças de {len(LIN)}")
