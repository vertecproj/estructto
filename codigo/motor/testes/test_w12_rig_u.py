"""W12-RIG — perfil U nas ligações rígidas e emendas (docs/v3/W12-RIG-U.md).

Pedido do usuário (2026-10-28): "as outras ligações não têm os U e UPN também, revise isso".

Contas à mão (kN, mm; A325 fub = 825 MPa; perfil A-572 GR.50 fy = 345, fu = 450; chapa A-36 fy = 250, fu = 400;
γa1 = 1,10, γa2 = 1,35; furo dh = d + 1,5; área líquida dn = dh + 2):

Emenda de viga UPN 200 (h 200, bf 75, tw 8,5, tf 11,5, r = tf), V = 30, M = 15, ⅝" (Sugerir: 2 parafusos por lado e mesa,
s = 60, bordas 40, talas da mesa 12,5 × 75, talas da alma 6,3 nas costas e por dentro):
  Af = 75·11,5 = 862,5; Aw = (200 − 23)·8,5 = 1504,5; Aw/6 = 250,75 (Pfeil 9.1–9.3).
  Mf = 15·862,5/(862,5 + 250,75) = 11,621 kN·m; Mw = 3,379; Ft = Mf/H = 11,621/0,200 = 58,11 kN.
  Fv,Rd = 0,4·197,93·825/1,35 = 48,38 kN; por parafuso 58,11/2 = 29,05 → 0,600.
  Linha da mesa: c_min = max(db; cabeça/2 + r) = 31,16/2 + 11,5 = 27,08; lo = 8,5 + 27,08/0,9 = 38,59;
    hi = 75 − 29/0,9 = 42,78 → g_u = 41 (meio, a 1 mm).
  Mesa do U, seção líquida: (75 − 19,375)·11,5·450/1,35 = 213,2 kN.
  Colapso da mesa (1 linha): Lv = 40 + 60 = 100; Agv = 1150; Anv = (100 − 1,5·19,375)·11,5 = 815,8;
    Ant = (75 − 41 − 9,69)·11,5 = 279,6; (0,6·450·815,8 + 450·279,6)/1,35 = 256,4 kN.
"""
import unittest

from prossen_estrutural import projeto as P
from prossen_estrutural import secoes
from prossen_estrutural.aco import ligacoes as L
from prossen_estrutural.aco import ligacoes_modelo as lm
from prossen_estrutural.aco import ligacoes_rigidas as R
from prossen_estrutural.modelo import Barra, No


def _sug(tipo, perfis, esf):
    d = dict(perfis, **R.TIPOS[tipo]["sugerir"](perfis, esf))
    return d, R.verificar(tipo, d, esf)


def _v(r, nome):
    return next(v for v in r["verificacoes"] if v["nome"].startswith(nome))


class TestRegistro(unittest.TestCase):
    def test_campos_mandam_perfis_com_u(self):
        campos = {t["tipo"]: {c["nome"]: c for c in t["campos"]} for t in L.tipos_ligacao()}
        for tipo, campo in (("emenda_viga", "perfil_viga"), ("emenda_pilar", "perfil_pilar"),
                            ("flange_plate", "perfil_viga"), ("painel_alma_pilar", "perfil_viga")):
            ps = campos[tipo][campo]["perfis"]
            for n in ("UPN 200", "U 200x75x4,75", "W310x38.7"):
                self.assertIn(n, ps, (tipo, n))
            self.assertFalse(any(p.startswith("Ue ") for p in ps), tipo)
        # I/H só: a tela filtra (sem `perfis`)
        for tipo, campo in (("chapa_topo_estendida", "perfil_viga"), ("chapa_topo_estendida", "perfil_pilar"),
                            ("flange_plate", "perfil_pilar"), ("painel_alma_pilar", "perfil_pilar")):
            self.assertNotIn("perfis", campos[tipo][campo], (tipo, campo))


class TestEmendaU(unittest.TestCase):
    def test_contas_upn200(self):
        d, r = _sug("emenda_viga", dict(perfil_viga="UPN 200"), dict(V=30, M=15))
        i = r["intermediarios"]
        self.assertAlmostEqual(i["Mf"], 11.621, 2)
        self.assertAlmostEqual(i["Ft"], 58.107, 2)
        self.assertEqual(i["g_u"], 41.0)
        self.assertAlmostEqual(i["c_min"], 27.081, 2)
        self.assertEqual(i["talas_alma"], 2)
        self.assertAlmostEqual(_v(r, "parafusos da mesa")["solicitante"], 29.05, 1)
        self.assertAlmostEqual(_v(r, "mesa do perfil U: ruptura")["resistente"], 213.2, 0)
        self.assertAlmostEqual(_v(r, "colapso por rasgamento (mesa do perfil")["resistente"], 256.4, 0)

    def test_sugerir_ate_090(self):
        casos = [("emenda_viga", dict(perfil_viga="UPN 200"), dict(V=30, M=15)),
                 ("emenda_viga", dict(perfil_viga="UPN 300"), dict(V=60, M=40)),
                 ("emenda_pilar", dict(perfil_pilar="UPN 240"), dict(N=300, M=20)),   # UPN 250 não está no catálogo
                 ("emenda_pilar", dict(perfil_pilar="UPN 260"), dict(N=300, M=20)),
                 ("emenda_pilar", dict(perfil_pilar="UPN 240"), dict(N=-300, M=20)),
                 ("emenda_viga", dict(perfil_viga="U 200x75x4,75"), dict(V=20, M=8)),
                 ("flange_plate", dict(perfil_viga="UPN 240", perfil_pilar="W250x73"), dict(V=40, M=25)),
                 ("flange_plate", dict(perfil_viga="UPN 260", perfil_pilar="W250x73"), dict(V=40, M=25)),
                 ("painel_alma_pilar", dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), dict(V=40, M=30))]
        for tipo, p, e in casos:
            _d, r = _sug(tipo, p, e)
            self.assertTrue(r["ok"], (tipo, p))
            self.assertLessEqual(r["taxa"], 0.90 + 1e-9, (tipo, p, r["criterio"]))

    def test_tracao_no_pilar_secao_liquida_do_u(self):
        _d, r = _sug("emenda_pilar", dict(perfil_pilar="UPN 240"), dict(N=300, M=20))
        v = _v(r, "perfil U: tração na seção líquida")
        self.assertAlmostEqual(v["solicitante"], 300.0)

    def test_so_tala_nas_costas_corte_simples(self):
        d, r2 = _sug("emenda_viga", dict(perfil_viga="UPN 300"), dict(V=60, M=40))
        r1 = R.verificar("emenda_viga", dict(d, tala_alma_interna=False), dict(V=60, M=40))
        v2, v1 = _v(r2, "parafusos da alma: cisalhamento"), _v(r1, "parafusos da alma: cisalhamento")
        self.assertIn("duplo", v2["nome"])
        self.assertIn("simples", v1["nome"])
        self.assertAlmostEqual(v1["resistente"] * 2, v2["resistente"], 3)
        self.assertEqual(len([c for c in r1["geometria"]["chapas"] if c["nome"] == "tala da alma"]), 1)
        self.assertTrue(any(v["nome"].startswith("tala das costas cabe") for v in r1["verificacoes"]))

    def test_geometria_u(self):
        """Costas da alma em y = −ab·xg; talas da alma nas costas (para fora) e por dentro; tala da mesa das costas até
        bf; parafusos da mesa na mesa, entre a face interna da alma e a ponta."""
        p = secoes.catalogo("UPN 200").props
        xg, tw, bf = p["xg"] * 1e3, p["tw"] * 1e3, p["b"] * 1e3
        for ab in (1, -1):
            d, _r = _sug("emenda_viga", dict(perfil_viga="UPN 200"), dict(V=30, M=15))
            g = R.verificar("emenda_viga", dict(d, abertura_y=ab), dict(V=30, M=15))["geometria"]
            self.assertEqual(g["abertura_y"], ab)
            talas = [c for c in g["chapas"] if c["nome"] == "tala da alma"]
            costas, dentro = talas
            self.assertAlmostEqual(costas["origem"][1], -ab * xg, 2)
            self.assertEqual(costas["normal"][1], -ab)
            self.assertAlmostEqual(dentro["origem"][1], ab * (tw - xg), 2)
            self.assertEqual(dentro["normal"][1], ab)
            sup = next(c for c in g["chapas"] if c["nome"] == "tala da mesa")
            ys = sorted(q[1] for q in sup["contorno"])
            self.assertAlmostEqual(ys[0], min(-ab * xg, ab * (bf - xg)), 2)
            self.assertAlmostEqual(ys[-1], max(-ab * xg, ab * (bf - xg)), 2)
            mesa = [q for q in g["parafusos"] if q["eixo"][2] != 0]
            for q in mesa:
                u = ab * q["origem"][1] + xg            # das costas da alma
                self.assertGreater(u, tw)
                self.assertLess(u, bf)
            alma = [q for q in g["parafusos"] if q["eixo"][1] != 0]
            for q in alma:   # da face de fora da tala das costas, atravessa as 2 talas e a alma
                self.assertAlmostEqual(q["origem"][1], -ab * (xg + 6.3), 2)
                self.assertEqual(q["eixo"][1], ab)

    def test_recusas(self):
        casos = [("emenda_viga", dict(perfil_viga="Ue 200x75x25x3,00"), "enrijecido"),
                 ("chapa_topo_estendida", dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), "chapa de topo ajustada"),
                 ("chapa_topo_estendida", dict(perfil_viga="W310x38.7", perfil_pilar="UPN 300"), "pilar U"),
                 ("flange_plate", dict(perfil_viga="UPN 200", perfil_pilar="UPN 300"), "pilar U"),
                 ("painel_alma_pilar", dict(perfil_viga="W310x38.7", perfil_pilar="UPN 300"), "pilar U"),
                 ("chapa_topo_ajustada", dict(perfil_viga="W310x38.7", perfil_pilar="UPN 300"), "só com viga U")]
        for tipo, d, txt in casos:
            with self.assertRaises(P.ErroEntrada, msg=tipo) as cm:
                P.verificar_ligacao(tipo, d, {"V": 40, "M": 8})
            self.assertIn(txt, str(cm.exception), tipo)
        with self.assertRaises(ValueError) as cm:   # o Sugerir da estendida também diz o motivo
            R.TIPOS["chapa_topo_estendida"]["sugerir"](dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), {"M": 10})
        self.assertIn("chapa de topo ajustada", str(cm.exception))

    def test_um_parafuso_na_alma_nao_resiste_a_m0(self):
        d, _r = _sug("emenda_viga", dict(perfil_viga="UPN 200"), dict(V=30, M=15))
        with self.assertRaises(ValueError) as cm:
            R.verificar("emenda_viga", dict(d, n_linhas_alma=1, n_colunas_alma=1), dict(V=30, M=15))
        self.assertIn("M0", str(cm.exception))

    def test_i_inalterado(self):
        """Padrões de fábrica I das emendas e da flange plate: os mesmos dados e taxas de antes."""
        for tipo in ("emenda_viga", "emenda_pilar", "flange_plate", "painel_alma_pilar"):
            t = next(x for x in P.tipos_ligacao() if x["tipo"] == tipo)
            r = P.verificar_ligacao(tipo, t["exemplo"]["dados"], t["exemplo"]["esforcos"])
            self.assertLessEqual(r["taxa"], 0.90 + 1e-9, tipo)
            self.assertNotIn("abertura_y", r["geometria"])


class TestFlangePlateU(unittest.TestCase):
    def test_upn_no_w250(self):
        _d, r = _sug("flange_plate", dict(perfil_viga="UPN 240", perfil_pilar="W250x73"), dict(V=40, M=25))
        g = r["geometria"]
        p = secoes.catalogo("UPN 240").props
        xg = p["xg"] * 1e3
        ch = next(c for c in g["chapas"] if c["nome"] == "chapa de mesa superior")
        ys = sorted(q[1] for q in ch["contorno"])
        self.assertAlmostEqual(ys[0], -xg, 2)
        self.assertAlmostEqual(ys[-1], p["b"] * 1e3 - xg, 2)
        alma = next(c for c in g["chapas"] if c["nome"] == "chapa de alma")
        self.assertAlmostEqual(alma["origem"][1], -xg, 2)     # nas costas do U, para fora
        self.assertEqual(alma["normal"][1], -1)
        self.assertIn("g_u", r["intermediarios"])
        self.assertTrue(any(v["nome"].startswith("apoio: mesa do pilar cobre") for v in r["verificacoes"]))


class TestModelo(unittest.TestCase):
    def _modelo(self, viga):
        m = P.novo_modelo()
        for nome in (viga, "W250x73"):
            m.add(secoes.catalogo(nome))
        m.add(No("A", 0, 0, 0, apoio=(True,) * 6))
        m.add(No("B", 0, 0, 3))
        m.add(No("C", 4, 0, 3))
        m.add(Barra("P1", "A", "B", "W250x73", "A-36", params={"papel": "pilar"}))
        m.add(Barra("V1", "B", "C", viga, "A-36", params={"papel": "viga"}))
        return m

    def test_viga_u_rigida_tem_tipo(self):
        """W12-RIG: viga U rígida na mesa do pilar não é mais sem_tipo (a tela propõe a chapa de topo ajustada); U baixo
        demais para 2 parafusos dentro do U (UPN 50) → projeto padrão solda direta."""
        for viga, padrao in (("UPN 200", None), ("UPN 50", "solda_direta_alma")):
            m = self._modelo(viga)
            (mb,) = [x for x in lm.membros(m) if x["tipo"] == "viga_pilar_mesa"]
            self.assertNotIn("sem_tipo", mb)
            self.assertIn("rigida", mb["assinatura"])
            lm.identificar(m)
            (g,) = [x for x in m.ligacoes["grupos"].values() if x["tipo"] == "viga_pilar_mesa"]
            self.assertEqual((g["projeto"] or {}).get("tipo_ligacao"), padrao, viga)
        # a proposta da tela (chapa de topo ajustada) dimensiona o UPN 200 no W250×73
        d, r = _sug("chapa_topo_ajustada", dict(perfil_viga="UPN 200", perfil_pilar="W250x73"), dict(V=40, M=8))
        self.assertTrue(r["ok"])


if __name__ == "__main__":
    unittest.main()
