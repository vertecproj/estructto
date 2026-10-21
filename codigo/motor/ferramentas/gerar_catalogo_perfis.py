"""Regenera dados/perfis/*.csv. Uso: python gerar_catalogo_perfis.py [brochura_vallourec.pdf]. Baixa o Orange Book (HTML) para cache; o PDF da Vallourec nao e versionado."""
import csv, re, html, os, sys, math
AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(AQUI)); sys.path.insert(0, AQUI)
from prossen_estrutural import secoes
R = os.path.dirname(os.path.dirname(AQUI))
OUT = R + '/motor/prossen_estrutural/dados/perfis'
CACHE = os.environ.get('CATALOGO_CACHE', os.path.join(os.environ.get('TEMP', '.'), 'orangebook'))
H = "fabricante_norma;familia;serie;nome;forma;massa;h;b;tw;tf;r;A;Ix;Iy;fonte".split(";")
def g(v):
    if v in ("", None): return ""
    return f"{float(str(v).replace(',','')):.6g}"
def save(fn, rows):
    with open(f"{OUT}/{fn}.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";", lineterminator="\n"); w.writerow(H)
        for r in rows: w.writerow(r)
    print(fn, len(rows))
def row(fab, fam, ser, nome, forma, m, h, b, tw, tf, r, A, Ix, Iy, fonte):
    return [fab, fam, ser, nome, forma] + [g(x) for x in (m, h, b, tw, tf, r, A, Ix, Iy)] + [fonte]
num = lambda s: float(s.replace(",", ""))
# W/HP
GER = "https://gsn.gerdau.com/sites/gsn_gerdau/files/downloadable_files/Folder%20Perfis%20Estruturais%20Gerdau%20-%20Segmento%20Industrial.pdf (Gerdau, Tabela de Bitolas, 10/18)"
rows = []
for l in csv.DictReader(x for x in open(R + '/motor/prossen_estrutural/dados/perfis_gerdau.csv', encoding="utf-8") if not x.startswith("#")):
    rows.append(row("Gerdau/NBR 15980", "W/HP", re.match("[A-Z]+", l["nome"]).group(), l["nome"], "I", l["massa"], l["d"], l["bf"], l["tw"], l["tf"], l["R"], l["A"], l["Ix"], l["Iy"], GER))
save("w_hp", rows)
# outros
OUTR = {"Cantoneira L": "l_iguais", "U laminado": "u_laminado", "Tubo quadrado (metalon)": "tubo_quadrado",
        "Tubo retangular (metalon)": "tubo_retangular", "Tubo redondo": "tubo_redondo"}
fab = {"l_iguais": "Gerdau/NBR 7007", "u_laminado": "Gerdau/NBR 7007", "tubo_quadrado": "NBR 6591 (metalon)", "tubo_retangular": "NBR 6591 (metalon)", "tubo_redondo": "NBR 5590/6591"}
acc = {v: [] for v in OUTR.values()}
FP = "https://www.fabricadoprojeto.com.br/tabelas-de-laminados-trefilados-e-itens-comerciais/"
FONTE_OUTROS = {
    "l_iguais": "bitolas, massa, A, Ix: " + FP + " (Tabela de Cantoneira de Abas Iguais, padrao americano, terceiros) via codigo/portico/gerar-perfis.ps1; sem fonte primaria Gerdau acessivel; conferido pela geometria",
    "u_laminado": "bitolas, massa, A, I: " + FP + " (Tabela de Viga U padrao americano, terceiros) via codigo/portico/gerar-perfis.ps1; sem fonte primaria Gerdau acessivel; conferido pela geometria",
    "tubo_quadrado": "bitolas: " + FP + " (tubo industrial); massa, A, I calculados pela geometria (raio externo 1,5t): calculado, sem fonte primaria",
    "tubo_retangular": "bitolas: " + FP + "tabelas-fapro-tubo-industrial-retangular/ ; massa, A, I calculados pela geometria (raio externo 1,5t): calculado, sem fonte primaria",
    "tubo_redondo": "bitolas: " + FP + " (tubo industrial); massa, A, I calculados pela geometria: calculado, sem fonte primaria",
}
for l in csv.DictReader(open(R + '/portico/perfis-outros.csv', encoding="utf-8"), delimiter=";"):
    fn = OUTR.get(l["familia"])
    if not fn: continue
    forma = l["forma"]
    acc[fn].append(row(fab[fn], l["familia"], l["serie"], l["nome"], forma, l["massa_kg_m"], l["h_mm"], l["b_mm"], l["tw_mm"], l["tf_mm"], "", l["A_cm2"], l["Ix_cm4"], l["Iy_cm4"], FONTE_OUTROS[fn]))
for k, v in acc.items(): save(k, v)
# T e chata
# 7/8 x 1/8": a fonte traz massa 0,99 (inconsistente com A = 1,34 cm2); corrigido para 1,05 = 7,85*A
T = """5/8 x 1/8";15.88;3.18;0.71;0.90;0.20;0.11
3/4 x 1/8";19.05;3.18;0.86;1.13;0.36;0.19
7/8 x 1/8";22.22;3.18;1.05;1.34;0.59;0.33
1 x 1/8";25.40;3.18;1.18;1.54;0.90;0.44
1.1/4 x 1/8";31.75;3.18;1.50;1.92;1.84;0.86
1.1/2 x 1/8";38.10;3.18;1.82;2.32;3.24;1.47
1.1/4 x 3/16";31.75;4.76;2.16;2.79;2.56;1.29
1.1/2 x 3/16";38.10;4.76;2.65;3.40;4.56;2.22
2 x 3/16";50.80;4.76;3.62;4.61;11.33;5.24
2 x 1/4";50.80;6.35;4.74;6.05;14.47;7.03"""
rows = []
for ln in T.split("\n"):
    n, d, t, m, A, Ix, Iy = ln.split(";")
    rows.append(row("Gerdau/NBR 7007", "T laminado", "T", f"T {n}", "T", m, d, d, t, t, "", A, Ix, Iy, "https://blog.famsteel.com.br/wp-content/uploads/2017/01/barras-e-perfis-lamina-perfil-t.pdf (Gerdau, lamina Perfil T; massa do 7/8x1/8 corrigida de 0,99 para 1,05)"))
save("t_laminado", rows)
rows = []
from fractions import Fraction as F
pol = {"1/2": 12.7, "5/8": 15.88, "3/4": 19.05, "1": 25.4, "1.1/4": 31.75, "1.1/2": 38.1, "2": 50.8, "2.1/2": 63.5, "3": 76.2, "4": 101.6, "5": 127.0, "6": 152.4, "8": 203.2}
esp = {"1/8": 3.18, "3/16": 4.76, "1/4": 6.35, "5/16": 7.94, "3/8": 9.53, "1/2": 12.7, "5/8": 15.88, "3/4": 19.05}
for bn, b in pol.items():
    for tn, t in esp.items():
        if t * 3 > b and not (b >= 25.4 and t <= 6.35) and b < 25.4: continue
        if t > b / 2: continue
        if b < 19 and t > 3.2: continue
        if b < 38 and t > 9.6: continue
        if b < 76 and t > 12.8: continue
        A = b * t / 100
        rows.append(row("Gerdau/NBR 7007", "Barra chata", "BC", f'BC {bn}" x {tn}"', "R", A * 0.785, b, t, t, t, "", A, t * b ** 3 / 12e4, b * t ** 3 / 12e4,
                        "calculado, sem fonte primaria: bitolas em polegadas usuais (lista comercial Gerdau nao encontrada), secao retangular nominal, massa = 7,85*A"))
# eixo: h=b? usar h=largura, b=espessura
rows2 = []
for r_ in rows: rows2.append(r_)
save("barra_chata", rows)
# Orange Book
OB = "https://orangebook.arcelormittal.com/node/%s (ArcelorMittal Orange Book, acesso 2026-10-21)"
def obrows(n):
    import urllib.request
    os.makedirs(CACHE, exist_ok=True)
    arq = os.path.join(CACHE, f'n{n}.html')
    if not os.path.exists(arq):
        urllib.request.urlretrieve(f'https://orangebook.arcelormittal.com/index.php/node/{n}', arq)
    t = open(arq, encoding='utf8').read()
    return [[html.unescape(re.sub('<.*?>', '', c)).strip() for c in re.findall(r'<t[dh].*?</t[dh]>', r, re.S)] for r in re.findall(r'<tr.*?</tr>', t, re.S)]
ipe, hea, heb = [], [], []
for r in obrows(6):
    if re.fullmatch(r"IPE \d+", r[0]):
        ipe.append(row("ArcelorMittal/EN 10365", "IPE", "IPE", r[0].replace(" ", ""), "I", r[2], r[3], r[4], r[5], r[6], r[7], r[-1].replace(",", ""), num(r[16]), num(r[17]), OB % 6))
for r in obrows(16):
    m = re.fullmatch(r"HE (\d+) ([AB])", r[0])
    if m:
        (hea if m[2] == "A" else heb).append(row("ArcelorMittal/EN 10365", "HEA" if m[2]=="A" else "HEB", "HE"+m[2], f"HE{m[2]}{m[1]}".replace("HEA","HEA ").replace("HEB","HEB ").replace("HE"+m[2]+m[1], "HE"+m[2]+" "+m[1]) if False else f"HE{m[2]} {m[1]}", "I", r[2], r[3], r[4], r[5], r[6], r[7], r[-1], num(r[16]), num(r[17]), OB % 16))
save("ipe", ipe); save("hea", hea); save("heb", heb)
upn = []
for r in obrows(265):
    if r[0].startswith("UPN"):
        upn.append(row("ArcelorMittal/DIN 1026", "UPN", "UPN", r[0], "U", r[2], r[3], r[4], r[5], r[6], r[7], r[-1], num(r[18]), num(r[19]), OB % 265))
for n, m, h, b, tw, tf, rr, A, Ix, Iy in (("UPN 30", 4.27, 30, 33, 5, 7, 7, 5.44, 6.39, 5.33), ("UPN 40", 4.87, 40, 35, 5, 7, 7, 6.21, 14.1, 6.68)):
    upn.append(row("DIN 1026", "UPN", "UPN", n, "U", m, h, b, tw, tf, rr, A, Ix, Iy, "DIN 1026-1 (UPN 30x33 e 40x35), valores de memoria, NAO conferidos em fonte online (pendente de conferencia); A*7,85 consistente"))
save("upn", sorted(upn, key=lambda x: float(x[6])))
# L desiguais (EN 10056, Orange Book node 225)
rows = []; sz = None
for r in obrows(225)[5:]:
    if len(r) < 20: continue
    if r[0]: sz = r[0]
    h, b = sz.split("x"); t = r[1]
    rows.append(row("ArcelorMittal/EN 10056", "L desigual", "L", f"L {h}x{b}x{t.rstrip('0').rstrip('.') if '.' in t else t}", "L", r[3], h, b, t, t, r[4], r[-1], num(r[8]), num(r[9]), OB % 225))
save("l_desiguais", rows)
# PFF
rows = {"U": [], "Ue": []}
for l in csv.DictReader(x for x in open(R + '/motor/prossen_estrutural/dados/perfis_pff.csv', encoding="utf-8") if not x.startswith("#")):
    s = secoes._secao_pff(l); p = s.props
    t = float(l["t"])
    rows[l["tipo"]].append(row("NBR 6355", "U formado a frio" if l["tipo"] == "U" else "Ue formado a frio", l["tipo"], l["nome"], l["tipo"], s.A * 7850,
        l["h"], l["b"], t, t, secoes.ri_nbr6355(t / 1000) * 1000, s.A * 1e4, p["Ix"] * 1e8, p["Iy"] * 1e8,
        "dimensoes nominais: https://marcegagliadobrasil.com/wp-content/uploads/2020/09/MMdoBrasil_Settembre2020_cold_formed_sections.pdf ; https://www.skylightestruturas.com.br/downloads/perfil-estrutural-tabela-de-comparacoes-entre-perfis.pdf ; https://api.aecweb.com.br/cls/catalogos/115/25404/perfis_estruturais.pdf (ver perfis_pff.csv); A, I calculados por secoes.py (ri NBR 6355)"))
save("u_pff", rows["U"]); save("ue_pff", rows["Ue"])

# Vallourec (opcional)
if len(sys.argv) > 1:
    import extrair_vallourec as ev
    rows = []
    for tipo, h, b, t, m, A, Ix, Iy in ev.extrair(sys.argv[1]):
        d = lambda x: f"{x:g}".replace(".", ",")
        if tipo == "TC": nome, forma = f"TC {d(h)}x{d(t)}", "TC"
        elif tipo == "TQ": nome, forma = f"TQ {d(h)}x{d(b)}x{d(t)}", "TR"
        else: nome, forma = f"TR {d(h)}x{d(b)}x{d(t)}", "TR"
        rows.append(row("Vallourec/EN 10210", "Tubo Vallourec", tipo, nome, forma, m, h, b if tipo != "TC" else h, t, t, "", A, Ix, Iy, ev.URL + " (pdfplumber; extrair_vallourec.py)"))
    save("tubos_vallourec", rows)
