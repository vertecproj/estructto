"""Torre metálica do elevador anexa ao bloco F6 (UNIVALI, Itajaí-SC). Dados inventados/estimados:
elevador 10 pessoas (Q=750 kg, cabina 1,40x1,50, acessível NBR 313), MRL 2:1, paradas 0/3,6/7,2/10,8, topo 14,4 m.
Torre 2,30 x 2,40 m (eixos). Passarela 1,60 x 1,50 m até a laje do F6 em cada parada (apoio vertical no prédio)."""
import sys, json, collections
import os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "codigo", "motor"))
from prossen_estrutural import projeto as P

m = P.novo_modelo("Torre elevador F6")
X, Y = [0.0, 2.30], [0.0, 2.40]
ZP = [0.0, 3.6, 7.2, 10.8, 14.4]          # paradas + topo
ZM = [1.8, 5.4, 9.0, 12.6]                # suportes das guias (faces x)
Z = sorted(ZP + ZM)
it = []
def no(i, x, y, z, apoio=None):
    it.append({"tipo": "no", "dados": {"id": i, "x": x, "y": y, "z": z, **({"apoio": apoio} if apoio else {})}})
def sec(n): it.append({"tipo": "secao", "dados": {"perfil": n}})
it.append({"tipo": "material", "dados": {"aco": "A-572 GR.50"}})
for s in ["W150x22.5", "W150x13", "W200x15", "L 2.1/2\" x 1/4\"", "TQ 100x100x6,4", "TQ 100x100x4,5", "W200x22.5"]: sec(s)
MAT = "A-572 GR.50"
for iz, z in enumerate(Z):
    for ix, x in enumerate(X):
        for iy, y in enumerate(Y):
            no(f"C{ix}{iy}-{iz}", x, y, z, [True, True, True, False, False, False] if z == 0 else None)
nb = collections.Counter()
def barra(pre, ni, nj, s, **p):
    nb[pre] += 1
    it.append({"tipo": "barra", "dados": {"id": f"{pre}{nb[pre]}", "ni": ni, "nj": nj, "secao": s, "material": MAT,
                                          **({"params": p} if p else {})}})
    return f"{pre}{nb[pre]}"
# pilares (um elemento por trecho entre níveis: o modelo exige nó em cada encontro)
for ix in range(2):
    for iy in range(2):
        for iz in range(len(Z) - 1):
            barra("P", f"C{ix}{iy}-{iz}", f"C{ix}{iy}-{iz+1}", "W150x22.5", papel="pilar", membro=f"P{ix}{iy}")
# anéis nas paradas e no topo; nas cotas intermediárias só nas faces x (guias)
for iz, z in enumerate(Z):
    if z == 0: continue
    full = z in ZP
    for iy in range(2):   # vigas ao longo de X (faces y=0 e y=2,40)
        if full and not (iy == 1 and z in ZP[1:4]):
            barra("VX", f"C0{iy}-{iz}", f"C1{iy}-{iz}", "W150x13", papel="viga")
    for ix in range(2):   # faces x: vigas ao longo de Y
        barra("VY", f"C{ix}0-{iz}", f"C{ix}1-{iz}", "W150x13", papel="viga")
# passarelas: a viga da face da porta (y=2,40) recebe as duas longarinas a 0,35 e 1,95 m -> partida em 3
pas = {}
for z in ZP[1:4]:
    iz = Z.index(z)
    no(f"Pa{iz}", 0.35, 2.40, z); no(f"Pb{iz}", 1.95, 2.40, z)
    no(f"Qa{iz}", 0.35, 3.90, z, [True, False, True, False, False, False]); no(f"Qb{iz}", 1.95, 3.90, z, [True, False, True, False, False, False])
    barra("VP", f"C01-{iz}", f"Pa{iz}", "W200x22.5", papel="viga", membro=f"VP{iz}"); barra("VP", f"Pa{iz}", f"Pb{iz}", "W200x22.5", papel="viga", membro=f"VP{iz}")
    barra("VP", f"Pb{iz}", f"C11-{iz}", "W200x22.5", papel="viga", membro=f"VP{iz}")
    pas[z] = [barra("LO", f"Pa{iz}", f"Qa{iz}", "W150x13", papel="viga", rotula_i=True),
              barra("LO", f"Pb{iz}", f"Qb{iz}", "W150x13", papel="viga", rotula_i=True)]
    barra("TP", f"Qa{iz}", f"Qb{iz}", "W150x13", papel="viga", rotula_i=True, rotula_j=True)
# vigas da máquina no topo (ao longo de X, apoiadas nas vigas VY das faces x a y=0,90 e 1,50): VY partidas
it_top = Z.index(14.4)
for k, y in enumerate([0.90, 1.50]):
    for ix in range(2): no(f"M{ix}{k}", X[ix], y, 14.4)
# refaz as VY do topo partidas nos apoios das vigas da máquina
it[:] = [x for x in it if not (x["tipo"] == "barra" and x["dados"]["id"].startswith("VY") and x["dados"]["ni"].endswith(f"-{it_top}"))]
for ix in range(2):
    barra("VY", f"C{ix}0-{it_top}", f"M{ix}0", "W150x13", papel="viga", membro=f"VYT{ix}"); barra("VY", f"M{ix}0", f"M{ix}1", "W150x13", papel="viga", membro=f"VYT{ix}")
    barra("VY", f"M{ix}1", f"C{ix}1-{it_top}", "W150x13", papel="viga", membro=f"VYT{ix}")
maq = [barra("VM", f"M0{k}", f"M1{k}", "W200x15", papel="viga", rotula_i=True, rotula_j=True) for k in range(2)]
# contraventamento: X por pavimento na face y=0; diagonal única por meio-pavimento nas faces x
for iz in range(len(Z) - 1):
    if Z[iz] in ZP:
        a, b = iz, Z.index(ZP[ZP.index(Z[iz]) + 1])
        no(f"X{a}", 1.15, 0.0, (Z[a] + Z[b]) / 2)   # X ligado no cruzamento: cada diagonal = 1 membro em 2 trechos
        for k, (p0, p1) in enumerate(((f"C00-{a}", f"C10-{b}"), (f"C10-{a}", f"C00-{b}"))):
            barra("D", p0, f"X{a}", "TQ 100x100x4,5", papel="diagonal", rotula_i=True, membro=f"DX{a}-{k}")
            barra("D", f"X{a}", p1, "TQ 100x100x4,5", papel="diagonal", rotula_j=True, membro=f"DX{a}-{k}")
    for ix in range(2):
        ni, nj = (f"C{ix}0-{iz}", f"C{ix}1-{iz+1}") if iz % 2 == 0 else (f"C{ix}1-{iz}", f"C{ix}0-{iz+1}")
        barra("D", ni, nj, "TQ 100x100x6,4", trelica=True)
P.adicionar_lote(m, it)
# casos e cargas
for c, t in [("G1", "G"), ("G2", "G"), ("Q1", "Q"), ("Q2", "Q")]:
    P.adicionar(m, "caso", {"nome": c, "tipo": t})
m.peso_proprio = "G1"
L = []
for b in list(m.barras.values()):
    pp = b.params.get("papel")
    if b.id.startswith(("VX", "VY", "VP")):
        L.append({"tipo": "carga_distribuida", "dados": {"caso": "G2", "barra": b.id, "q": [0, 0, -0.45]}})  # ACM + subestrutura
for z, ls in pas.items():
    for b in ls:
        L.append({"tipo": "carga_distribuida", "dados": {"caso": "G2", "barra": b, "q": [0, 0, -0.50]}})   # piso 0,6 kN/m² x 0,8 m
        L.append({"tipo": "carga_distribuida", "dados": {"caso": "Q1", "barra": b, "q": [0, 0, -4.0]}})    # 5 kN/m² x 0,8 m
for b in maq:   # suspensão MRL 2:1: (P 9 + Q 7,5 + CP 12,75 kN) x 2 (impacto) / 2 vigas
    L.append({"tipo": "carga_pontual", "dados": {"caso": "Q2", "barra": b, "a": 1.15, "F": [0, 0, -29.3]}})
P.adicionar_lote(m, L)
P.adicionar(m, "pavimento", {"nome": "P1", "z": 3.6, "diafragma": False})
for n, z in [("P2", 7.2), ("P3", 10.8), ("Topo", 14.4)]:
    P.adicionar(m, "pavimento", {"nome": n, "z": z, "diafragma": False})
P.configurar(m, {"tipo_uso": "comercial", "pdelta": True,
                 "vento": {"tipo": "edificio", "V0": 43, "S1": 1.0, "S3": 1.0, "categoria": "III", "classe": "A",
                           "dimensoes": {"lx": 2.5, "ly": 2.6}}})
print("validar:", P.validar(m))
P.salvar(m, os.path.join(os.path.dirname(os.path.abspath(__file__)), "torre-elevador-f6.json"))
print(P.resumo(m))
