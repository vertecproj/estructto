"""W11 — chapa de topo ajustada com viga U (docs/v3/W11-CHAPA-TOPO-U.md).

Pedido do usuário: "adicione VIGA UPN a esse tipo de conexão, será uma chapa do tamanho da viga escolhida com 5mm de
sobra pra cada lado e se as vigas forem do mesmo tamanho não tem essa folga. 2 parafusos por conexão e dentro da viga".

Contas à mão (unidades kN, mm; A325 fub = 825 MPa; chapa A-36 fy = 250; perfis A-572 GR.50 fy = 345; γa1 = 1,10,
γa2 = 1,35):

Caso A — UPN 200 (h 200, bf 75, tw 8,5, tf 11,5, xg 22,01) na mesa do W250×73 (d 253, bf 254, tw 8,6, tf 14,2),
⅝" A325, chapa 12,5, filetes 5, pf 23, c 36, V = 40 kN, M = 8 kN·m:
  Ab = π·15,875²/4 = 197,93 mm²; Ft,Rd = 0,75·197,93·825/1,35 = 90,72 kN; Fv,Rd = 0,4·197,93·825/1,35 = 48,38 kN.
  zb = 100 − 11,5 − 23 = 65,5 (s = 131); h = 65,5 + (200 − 11,5)/2 = 159,75 mm; T = 8000/159,75 = 50,08 kN.
  Linha dos parafusos: y = tw − xg + c = 8,5 − 22,01 + 36 = 22,49 mm.
  Vb = √((40/2)² + (40·0,02249/0,131)²) = √(20² + 6,867²) = 21,15 kN.
  Chapa (EN 1993-1-8): m = 36 − 0,8·5 = 32, m2 = 23 − 0,8·5 = 19, e = 75 − 8,5 − 36 + 5 = 35,5;
    leff = α·m (fig. 6.11, λ1 = 0,474, λ2 = 0,281 → α = 5,387) = 172,4 mm;
    Mpl = 0,25·172,4·12,5²·250/1,10 = 1530 kN·mm; modo 2 = (1530 + 35,5·90,72)/(32 + 35,5) = 70,38 kN;
    B = T·Ft,Rd/min(modo 2, Ft,Rd) = 50,08·90,72/70,38 = 64,55 kN → B/Ft,Rd = 0,712.
  6.3.3.4: (64,55/90,72)² + (21,15/48,38)² = 0,506 + 0,191 = 0,697.
  Mesa do pilar (Struik–de Back): b = 22,49 − 4,3 = 18,19; p = min(131; 3,5·18,19)/2 + 3,5·18,19/2 = 63,67;
    b' = 18,19 − 7,94 = 10,25; δ = 1 − 17,375/63,67 = 0,727; Mp = 63,67·14,2²/4·345/1,10 = 1006,7 kN·mm;
    T_chapa = Mp·(1 + δ)/b' = 169,6 kN.
  5.7.3: ln = 11,5 + 2·12,5 + 2·5 = 46,5; 1,10·(5·26,2 + 46,5)·8,6·345/1,10 = 526,6 kN.
  Contorno da solda: 200 (costas) + 2·75 (faces externas) + 2·11,5 (pontas) + 2·66,5 (faces internas das mesas)
    + 177 (face interna da alma) = 683 mm.

Caso B — UPN 100 (h 100, bf 50, tw 6, tf 8,5, xg 16,87) nas costas da alma do UPN 100 (mesmo tamanho: sem sobra), ½"
A325, chapa 8, filetes 4 (alma) e 6 (mesas), pf 21, c 19, V = 10 kN, M = 1,5 kN·m, terça/travessa (isenta de 6.1.5.2):
  Ft,Rd = 0,75·126,68·825/1,35 = 58,06 kN; zb = 50 − 8,5 − 21 = 20,5; h = 20,5 + 45,75 = 66,25; T = 1500/66,25 = 22,64.
  y = 6 − 16,87 + 19 = 8,13. Alma do pilar U entre as mesas: m = 41,5 − 8,13 − 0,8·8,5 = 26,57, e = 41,5 + 8,13 = 49,63,
    leff = min(2π·26,57; 4·26,57 + 1,25·49,63) = 166,9; Mpl = 0,25·166,9·6²·345/1,10 = 471,1 kN·mm;
    modo 1 = 2·471,1/26,57 = 35,46 kN → 22,64/35,46 = 0,638. Sem a isenção (T = 45 kN): 1,269.
  Compressão na alma do pilar U (face de tubo retangular, EN tab. 7.11): β = 50/100 = 0,5, η = (8,5 + 2·8)/100 = 0,245;
    N1,Rd = 345·6²/(1 − 0,5)·(2·0,245 + 4·√0,5)/1,10 = 74,94 kN.
"""
import math
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_rigidas as lr

TIPO = "chapa_topo_ajustada"
A = dict(perfil_viga="UPN 200", perfil_pilar="W250x73", d_mm=15.875, tipo_parafuso="A325", tp=12.5, b_alma=5,
         b_mesa=5, solda_mesa="filete", pf=23, c_alma=36)
EA = dict(V=40.0, M=8.0)
B = dict(perfil_viga="UPN 100", perfil_pilar="UPN 100", d_mm=12.7, tipo_parafuso="A325", tp=8, b_alma=4, b_mesa=6,
         solda_mesa="filete", pf=21, c_alma=19, terca_ou_travessa=True)
EB = dict(V=10.0, M=1.5)


def _v(r, inicio):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(inicio))


def _sug(perfis, esf):
    d = lr.TIPOS[TIPO]["sugerir"](perfis, esf)
    return d, lr.verificar(TIPO, d, esf)


class TestRegistro(unittest.TestCase):
    def test_seletor_lista_u_so_na_ajustada(self):
        campos = {t["tipo"]: {c["nome"]: c for c in t["campos"]} for t in L.tipos_ligacao()}
        for campo in ("perfil_viga", "perfil_pilar"):
            ps = campos[TIPO][campo]["perfis"]
            for n in ("UPN 200", "UPN 50", "U 200x75x3,00", "Ue 200x75x25x3,00", "W310x38.7", "W250x73"):
                self.assertIn(n, ps)
            self.assertNotIn("L 3\" x 1/4\"", ps)
        for outro in ("chapa_topo_estendida", "flange_plate", "painel_alma_pilar", "emenda_viga", "emenda_pilar"):
            for c in campos[outro].values():
                self.assertFalse(any("UPN" in p for p in c.get("perfis") or []), (outro, c["nome"]))

    def test_outras_rigidas_continuam_recusando_u(self):
        casos = [("chapa_topo_estendida", dict(perfil_viga="UPN 200", perfil_pilar="W250x73")),
                 ("flange_plate", dict(perfil_viga="UPN 200", perfil_pilar="W250x73")),
                 ("painel_alma_pilar", dict(perfil_viga="UPN 200", perfil_pilar="W250x73")),
                 ("emenda_viga", dict(perfil_viga="UPN 200")), ("emenda_pilar", dict(perfil_pilar="UPN 200"))]
        for tipo, d in casos:
            with self.assertRaises(P.ErroEntrada, msg=tipo) as cm:
                P.verificar_ligacao(tipo, d, {"V": 40, "M": 8})
            self.assertIn("ligação rígida com perfil U não suportada", str(cm.exception))

    def test_viga_i_em_pilar_u_recusa_com_motivo(self):
        with self.assertRaises(P.ErroEntrada) as cm:
            P.verificar_ligacao(TIPO, dict(perfil_viga="W310x38.7", perfil_pilar="UPN 200"), {"V": 40, "M": 8})
        self.assertIn("só com viga U", str(cm.exception))


class TestChapa(unittest.TestCase):
    def _chapa(self, r):
        (ch,) = r["geometria"]["chapas"]
        ys, zs = [p[0] for p in ch["contorno"]], [p[1] for p in ch["contorno"]]
        return ch, min(ys), max(ys), min(zs), max(zs)

    def test_com_sobra_d_mais_10_por_bf_mais_10(self):
        r = lr.verificar(TIPO, A, EA)
        ch, y0, y1, z0, z1 = self._chapa(r)
        self.assertAlmostEqual(y1 - y0, 75 + 10, places=3)
        self.assertAlmostEqual(z1 - z0, 200 + 10, places=3)
        xg = 22.01
        self.assertAlmostEqual(y0, -xg - 5, delta=0.05)    # costas da alma − 5
        self.assertAlmostEqual(y1, 75 - xg + 5, delta=0.05)  # ponta da mesa + 5
        self.assertAlmostEqual(ch["origem"][0], 253 / 2, places=3)   # encostada na mesa do pilar
        self.assertEqual(r["intermediarios"]["sobra"], 5.0)

    def test_mesmo_tamanho_sem_sobra(self):
        r = lr.verificar(TIPO, B, EB)
        ch, y0, y1, z0, z1 = self._chapa(r)
        self.assertAlmostEqual(y1 - y0, 50.0, places=3)
        self.assertAlmostEqual(z1 - z0, 100.0, places=3)
        self.assertAlmostEqual(ch["origem"][0], 16.87, delta=0.05)   # costas da alma do pilar U (centroide no nó)

    def test_abertura_espelha(self):
        r = lr.verificar(TIPO, dict(A, abertura_y=-1), EA)
        _ch, y0, y1, _z0, _z1 = self._chapa(r)
        self.assertAlmostEqual(y0, -(75 - 22.01 + 5), delta=0.05)
        self.assertAlmostEqual(y1, 22.01 + 5, delta=0.05)
        self.assertTrue(all(p["origem"][1] < 0 for p in r["geometria"]["parafusos"]))


class TestParafusos(unittest.TestCase):
    def test_dois_parafusos_dentro_do_u(self):
        for d, e, (h, bf, tw, tf, xg) in ((A, EA, (200, 75, 8.5, 11.5, 22.01)), (B, EB, (100, 50, 6, 8.5, 16.87))):
            r = lr.verificar(TIPO, d, e)
            ps = r["geometria"]["parafusos"]
            self.assertEqual(len(ps), 2)
            (ya, za), (yb, zb) = [(p["origem"][1], p["origem"][2]) for p in ps]
            self.assertAlmostEqual(ya, yb)                       # um em cima do outro
            self.assertAlmostEqual(za, -zb)
            self.assertTrue(tw - xg < ya < bf - xg)              # do lado de dentro da alma, antes da ponta das mesas
            self.assertTrue(abs(za) < h / 2 - tf)                # entre as mesas
            self.assertTrue(all(p["eixo"] == [-1, 0, 0] for p in ps))   # cabeça dentro do U, porca no apoio
            i = r["intermediarios"]
            self.assertGreaterEqual(i["pf"], i["pf_min"])
            self.assertGreaterEqual(i["c_alma"], i["c_min"])
            self.assertGreaterEqual(i["s"], 2.7 * d["d_mm"])

    def test_upn_baixo_nao_cabe_com_motivo_e_diametro_maximo(self):
        for v in ("UPN 50", "UPN 65"):
            with self.assertRaises(ValueError) as cm:
                lr.TIPOS[TIPO]["sugerir"](dict(perfil_viga=v, perfil_pilar="UPN 120"), dict(V=5, M=1))
            msg = str(cm.exception)
            self.assertIn("não cabem dentro do U", msg)
            self.assertIn("Diâmetro máximo que cabe", msg)
            self.assertIn("menor que M12", msg)
        with self.assertRaises(P.ErroEntrada) as cm:
            P.verificar_ligacao(TIPO, dict(perfil_viga="UPN 50", perfil_pilar="UPN 120"), {"V": 5})
        self.assertIn("não cabem dentro do U", str(cm.exception))


class TestContasAMao(unittest.TestCase):
    def test_caso_a(self):
        r = lr.verificar(TIPO, A, EA)
        i = r["intermediarios"]
        self.assertAlmostEqual(i["FtRd"], 90.72, delta=0.01)
        self.assertAlmostEqual(i["FvRd"], 48.38, delta=0.01)
        self.assertAlmostEqual(i["h"], 159.75, places=2)
        self.assertAlmostEqual(i["T"], 50.08, delta=0.01)
        self.assertAlmostEqual(i["y_parafusos"], 22.49, delta=0.01)
        self.assertAlmostEqual(i["Vb"], 21.15, delta=0.01)
        self.assertAlmostEqual(i["leff"], 172.4, delta=0.1)
        self.assertAlmostEqual(_v(r, "parafuso tracionado: tração com alavanca na chapa")["solicitante"], 64.55, delta=0.02)
        self.assertAlmostEqual(_v(r, "parafuso tracionado: tração + cisalhamento")["taxa"], 0.697, delta=0.001)
        self.assertAlmostEqual(_v(r, "parafuso tracionado: flexão da mesa do pilar")["resistente"], 169.6, delta=0.1)
        self.assertAlmostEqual(_v(r, "escoamento local da alma do pilar")["resistente"], 526.6, delta=0.1)
        self.assertAlmostEqual(i["L_solda"], 683.0, places=1)
        self.assertTrue(r["ok"])

    def test_caso_b(self):
        r = lr.verificar(TIPO, B, EB)
        i = r["intermediarios"]
        self.assertAlmostEqual(i["FtRd"], 58.06, delta=0.01)
        self.assertAlmostEqual(i["h"], 66.25, places=2)
        self.assertAlmostEqual(i["T"], 22.64, delta=0.01)
        v = _v(r, "parafuso tracionado: flexão da alma do pilar U")
        self.assertAlmostEqual(v["resistente"], 35.46, delta=0.02)
        self.assertAlmostEqual(v["taxa"], 0.638, delta=0.001)
        self.assertAlmostEqual(_v(r, "alma do pilar U: plastificação")["resistente"], 74.94, delta=0.02)
        self.assertTrue(r["ok"])
        sem = lr.verificar(TIPO, dict(B, terca_ou_travessa=False), EB)   # 6.1.5.2: T = 45 kN
        self.assertAlmostEqual(sem["intermediarios"]["T"], 45.0, delta=0.01)
        self.assertAlmostEqual(_v(sem, "parafuso tracionado: flexão da alma do pilar U")["taxa"], 1.269, delta=0.001)

    def test_solda_mesmo_tamanho_so_contorno_interno(self):
        r = lr.verificar(TIPO, B, EB)
        self.assertEqual(len(r["geometria"]["soldas"]), 3)   # faces internas das 2 mesas e da alma
        self.assertTrue(any("sem solda" in a for a in r["avisos"]))
        self.assertEqual(len(lr.verificar(TIPO, A, EA)["geometria"]["soldas"]), 8)   # contorno inteiro do U

    def test_avisos_modelo_e_excentricidade(self):
        r = lr.verificar(TIPO, A, EA)
        self.assertTrue(any("centro de cisalhamento" in a and "não é verificada" in a for a in r["avisos"]))
        self.assertTrue(any(a.startswith("modelo: 2 parafusos") for a in r["avisos"]))

    def test_momento_alto_diz_o_motivo(self):
        d, r = _sug(dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), dict(V=40, M=40))
        self.assertFalse(r["ok"])
        self.assertTrue(any("momento além da capacidade de 2 parafusos dentro do U" in a for a in r["avisos"]))


class TestSugerir(unittest.TestCase):
    def _alvo(self, r):
        return max(v["taxa"] for v in r["verificacoes"] if not v.get("limite_sobra"))

    def test_upn200_em_w250(self):
        d, r = _sug(dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), EA)
        self.assertTrue(r["ok"])
        self.assertLessEqual(self._alvo(r), 0.90)
        # perna = sobra de 5 mm = mínima da tabela 10 (tf 11,5): a única verificação no limite, marcada
        for v in r["verificacoes"]:
            if v.get("limite_sobra"):
                self.assertIn("perna mínima", v["nome"])
                self.assertAlmostEqual(v["taxa"], 1.0)
        self.assertEqual(len(r["geometria"]["soldas"]), 8)
        self.assertGreater(d["pf"], 0)
        self.assertGreater(d["c_alma"], 0)

    def test_upn100_em_upn100(self):
        d, r = _sug(dict(perfil_viga="UPN 100", perfil_pilar="UPN 100", terca_ou_travessa=True), EB)
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)
        self.assertEqual(r["intermediarios"]["b_chapa"], 50.0)
        self.assertIn(d["d_mm"], (12.7, 12.0))

    def test_padrao_w310_inalterado(self):
        ex = next(t for t in P.tipos_ligacao() if t["tipo"] == TIPO)["exemplo"]
        self.assertEqual(ex["dados"]["perfil_viga"], "W310x38.7")
        self.assertEqual((ex["dados"]["d_mm"], ex["dados"]["tp"]), (22.225, 19.0))
        r = P.verificar_ligacao(TIPO, ex["dados"], ex["esforcos"])
        self.assertTrue(r["ok"])
        self.assertLessEqual(r["taxa"], 0.90)


class TestDesenho(unittest.TestCase):
    def test_membros_com_pecas(self):
        r = P.verificar_ligacao(TIPO, A, EA)
        g = r["geometria"]
        viga, pilar = g["membros"]
        self.assertEqual((viga["barra"], viga["forma"], len(viga["pecas"])), ("viga", "U", 3))
        self.assertEqual((pilar["barra"], len(pilar["pecas"])), ("pilar", 3))
        xs = 253 / 2 + 12.5
        self.assertTrue(all(abs(p["origem"][0] - xs) < 1e-6 for p in g["parafusos"]))
        self.assertTrue(all(abs(s["pontos"][0][0] - xs) < 1e-6 for s in g["soldas"]))
        self.assertEqual({s["campo"] for s in g["soldas"]}, {"b_alma", "b_mesa"})
        self.assertEqual(g["chapas"][0]["campo"], "tp")
        ue = P.verificar_ligacao(TIPO, dict(A, perfil_viga="Ue 200x75x25x3,00", b_alma=3, b_mesa=3), {"V": 10, "M": 2})
        self.assertEqual(len(ue["geometria"]["membros"][0]["pecas"]), 5)   # alma, 2 mesas, 2 enrijecedores de borda
        pu = P.verificar_ligacao(TIPO, B, EB)["geometria"]["membros"][1]
        self.assertEqual((pu["forma"], len(pu["pecas"])), ("U", 3))


if __name__ == "__main__":
    unittest.main()
