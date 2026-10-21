"""Extrai tubos estruturais Vallourec (circular, quadrado, retangular) do PDF da brochura para CSV.
Uso: python extrair_vallourec.py <brochura.pdf> <saida.csv>
O PDF não é versionado. A coluna de dimensões da brochura é uma célula mesclada; cada linha é atribuída
ao rótulo (dos 3 mais próximos em y) cuja área/inércia teórica de parede fina mais se aproxima da tabelada."""
import csv, math, re, sys
import pdfplumber

URL = "https://www.vallourec.com/app/uploads/sites/2/2023/10/Structural-Tubes_brochure.pdf"
PAGINAS = {"TQ": range(34, 39), "TR": range(39, 49), "TC": range(50, 54)}


def n(s):
    s = (s or "").replace("*", "").replace(" ", "").replace(".", "").replace(",", ".")
    return float(s)


def teor(tipo, lab, t):
    if tipo == "TC":
        d = lab[0]; di = d - 2 * t
        return math.pi / 4 * (d * d - di * di) / 100, math.pi / 64 * (d ** 4 - di ** 4) / 1e4
    h, b = lab if tipo == "TR" else (lab[0], lab[0])
    A = (h * b - (h - 2 * t) * (b - 2 * t)) / 100
    return A, (b * h ** 3 - (b - 2 * t) * (h - 2 * t) ** 3) / 12e4


def paginas(pdf):
    for tipo, rg in PAGINAS.items():
        for i in rg:
            yield tipo, pdf.pages[i]


def extrair(pdf_path):
    pdf = pdfplumber.open(pdf_path)
    saida, ant = [], []
    for tipo, p in paginas(pdf):
        ts = sorted(p.find_tables(), key=lambda t: -len(t.rows))
        if not ts or len(ts[0].rows) < 5:
            continue
        t = ts[0]
        y0 = t.rows[3].bbox[1] - 15
        labs = {}
        for w in p.extract_words():
            if w["x1"] < t.rows[3].cells[1][0] + 1 and y0 < w["top"] < t.bbox[3]:
                labs.setdefault(round(w["top"]), []).append(w["text"])
        L = []
        for y, ws in sorted(labs.items()):
            txt = " ".join(ws).replace(" x ", "x")
            m = re.fullmatch(r"(\d+(?:,\d+)?)x(\d+(?:,\d+)?)", txt) if tipo != "TC" else re.fullmatch(r"(\d+(?:,\d+)?)", txt)
            if m:
                L.append((y + 4, tuple(n(g) for g in m.groups())))
        cand = ant[-1:] + [(y, l) for y, l in L]
        for r in t.rows[3:]:
            cel = [(c and p.crop(c).extract_text() or "").strip() for c in r.cells]
            try:
                v = [n(x) for x in cel[1:10]]
            except ValueError:
                continue
            if not cel[1]:
                continue
            tt, m, A, I1 = v[0], v[1], v[2], v[3]
            yc = (r.bbox[1] + r.bbox[3]) / 2
            prox = sorted(cand, key=lambda c: abs(c[0] - yc) if c[0] > -1e8 else 1e9)[:3]
            def custo(c):
                a, i = teor(tipo, c[1], tt)
                ea, ei = (A - a) / a, (I1 - i) / i
                # cantos arredondados só tiram área/inércia: tabelado <= teórico (+2 %)
                if not (-0.12 <= ea <= 0.02 and -0.2 <= ei <= 0.03):
                    return 9.0
                return abs(ea) + 0.5 * abs(ei)
            y, lab = min(prox, key=custo)
            if custo((y, lab)) > 1:
                continue
            if tipo == "TR":
                saida.append((tipo, lab[0], lab[1], tt, m, A, I1, v[4]))
            else:
                saida.append((tipo, lab[0], lab[0] if tipo == "TQ" else lab[0], tt, m, A, I1, I1))
        ant = [(-1e9, L[-1][1])] if L else ant
    vistos, unicas = set(), []
    for r in saida:
        if r[:4] not in vistos:  # linha repetida = rótulo ambíguo: descarta a repetição
            vistos.add(r[:4]); unicas.append(r)
    return unicas


if __name__ == "__main__":
    rows = extrair(sys.argv[1])
    with open(sys.argv[2], "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["tipo", "h", "b", "t", "massa", "A", "Ix", "Iy"])
        w.writerows(rows)
    print(len(rows), "linhas", file=sys.stderr)
