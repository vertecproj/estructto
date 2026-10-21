// Vista3D: barras, nós e apoios do modo Analítico como linhas e pontos de largura fixa na tela (1,5 px, anti-serrilhado),
// desenhados pela sobreposição 2D a cada mudança de câmera (Sobre.OnRender), em vez de prismas e cubos no Viewport3D: o
// prisma fino "engordava" de perto e, de lado, mostrava duas faces de tons diferentes (parecia uma cantoneira); o cubo do
// nó virava uma caixa. O WPF 3D não tem linha; a linha do Helix (DX11) só existe com placa de vídeo e no Realista. A
// sobreposição funciona em qualquer máquina, na ortográfica e na perspectiva (o trecho atrás do plano perto é cortado) e usa
// a mesma projeção (Tela) do snap, dos grips, das setas e das cotas: tudo fica alinhado por construção.
// Também aqui: a deformada e os fantasmas (todos os estilos), a posição original no Realista e o nó escolhido.
// Picking: Vista3D.Navegacao.cs › Acertar (nó a 8 px, barra a 6 px do eixo na tela).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public partial class Vista3D
    {
        double linhaDef;   // fator da deformada da última Reconstruir (0 = sem)
        readonly SolidColorBrush pincelFantasma = new SolidColorBrush(Color.FromArgb(0x50, TemaPlant.BTexto3.Color.R, TemaPlant.BTexto3.Color.G, TemaPlant.BTexto3.Color.B));

        // Projeção de um quadro: a mesma conta de Tela (Vista3D.Navegacao.cs), com as direções calculadas uma vez.
        readonly struct Projetor
        {
            readonly bool persp; readonly Vector3D d, cima, dir; readonly Point3D alvo, olho; readonly double esc, f, w2, h2, perto;
            public Projetor(Vista3D v)
            {
                v.Direcoes(out d, out cima, out dir);
                persp = v.perspectiva; alvo = v.alvo; olho = v.Olho; esc = v.Escala; f = v.FocalPx;
                w2 = v.ActualWidth / 2; h2 = v.ActualHeight / 2; perto = Math.Max(1e-6, v.pcam.NearPlaneDistance);
            }
            double Z(Point3D q) => -Vector3D.DotProduct(q - olho, d);
            public Point Tela(Point3D q)
            {
                if (!persp) { Vector3D r = q - alvo; return new Point(w2 + Vector3D.DotProduct(r, dir) / esc, h2 - Vector3D.DotProduct(r, cima) / esc); }
                Vector3D ro = q - olho; double z = Z(q);
                if (z < 1e-9) return new Point(-1e6, -1e6);
                return new Point(w2 + Vector3D.DotProduct(ro, dir) / z * f, h2 - Vector3D.DotProduct(ro, cima) / z * f);
            }
            public bool Visivel(Point3D q) => !persp || Z(q) >= perto;
            // Segmento na tela; na perspectiva o trecho atrás do plano perto é cortado (senão a linha "vira" do outro lado).
            public bool Segmento(Point3D a, Point3D b, out Point pa, out Point pb)
            {
                pa = pb = default;
                if (persp)
                {
                    double za = Z(a), zb = Z(b);
                    if (za < perto && zb < perto) return false;
                    if (za < perto) a += (b - a) * ((perto - za) / (zb - za));
                    else if (zb < perto) b += (a - b) * ((perto - zb) / (za - zb));
                }
                pa = Tela(a); pb = Tela(b);
                return true;
            }
        }

        static readonly Dictionary<(Color, double, bool), Pen> canetasLinha = new Dictionary<(Color, double, bool), Pen>();
        static Pen CanetaLinha(Color c, double l, bool traco = false)
        {
            if (canetasLinha.TryGetValue((c, l, traco), out Pen p)) return p;
            p = new Pen(new SolidColorBrush(c), l) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (traco) p.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
            p.Freeze();
            return canetasLinha[(c, l, traco)] = p;
        }

        // Todos os segmentos de um estilo numa geometria só (um DrawGeometry por cor: 2.430 barras em ~2 ms).
        sealed class Lote
        {
            readonly StreamGeometry g = new StreamGeometry(); readonly StreamGeometryContext ctx; int n;
            public Lote() { ctx = g.Open(); }
            public void Add(Point a, Point b) { ctx.BeginFigure(a, false, false); ctx.LineTo(b, true, false); n++; }
            public void Desenhar(DrawingContext dc, Pen p) { ctx.Close(); if (n == 0) return; g.Freeze(); dc.DrawGeometry(null, p, g); }
        }

        void DesenharModelo(DrawingContext dc)
        {
            if (ActualWidth < 1 || (mod.Barras.Count == 0 && mod.Nos.Count == 0 && mod.Fantasmas.Count == 0)) return;
            var pr = new Projetor(this);
            if (mod.Fantasmas.Count > 0)
            {
                var lf = new Lote();
                foreach (var (a, b) in mod.Fantasmas) if (pr.Segmento(a, b, out Point pa, out Point pb)) lf.Add(pa, pb);
                lf.Desenhar(dc, new Pen(pincelFantasma, 1));   // o pincel anima a opacidade (fade ao isolar um plano)
            }
            bool def = linhaDef > 0;
            if (Realista)
            {
                if (def)   // as peças já estão deformadas: a posição original em tracejado fino
                {
                    var lo = new Lote();
                    foreach (Barra3D b in mod.Barras)
                    {
                        No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj);
                        if (a != null && c != null && pr.Segmento(P(a), P(c), out Point pa, out Point pb)) lo.Add(pa, pb);
                    }
                    lo.Desenhar(dc, CanetaLinha(TemaPlant.TextoMudo, 1, true));
                }
                if (CargasSempreVisiveis) foreach (No3D n in mod.Nos) if (EhSel(n, n.Origem) && pr.Visivel(P(n))) PontoNo(dc, pr.Tela(P(n)), true, 3);
                return;
            }

            // barras: largura e cor pelo estado; escolhidas por cima, com halo
            bool mudo = def || Diagrama != Diagrama3D.Nenhum;
            var lotes = new Dictionary<Color, Lote>(); var sel = new Lote(); double soma = 0; int nb = 0;
            foreach (Barra3D b in mod.Barras)
            {
                No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                if (!pr.Segmento(P(a), P(c), out Point pa, out Point pb)) continue;
                soma += (pb - pa).Length; nb++;
                if (EhSel(b, b.Origem)) { sel.Add(pa, pb); continue; }
                Color cor = MostrarAproveitamento && b.Aprov.HasValue ? CorAprov(b.Aprov.Value) : mudo ? TemaPlant.TextoMudo : TemaPlant.CorBarra;
                if (!lotes.TryGetValue(cor, out Lote l)) lotes[cor] = l = new Lote();
                l.Add(pa, pb);
            }
            foreach (var kv in lotes) kv.Value.Desenhar(dc, CanetaLinha(kv.Key, kv.Key == TemaPlant.CorBarra || kv.Key == TemaPlant.TextoMudo ? 1.5 : 2.25));
            if (def)
            {
                var ld = new Lote();
                foreach (Barra3D b in mod.Barras)
                {
                    No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj);
                    if (a != null && c != null && pr.Segmento(Deformado(a, linhaDef), Deformado(c, linhaDef), out Point pa, out Point pb)) ld.Add(pa, pb);
                }
                ld.Desenhar(dc, CanetaLinha(TemaPlant.CorDeformada, 2));
            }
            // a mesma geometria duas vezes: halo largo translúcido e a linha branca (não colide com as cores do aproveitamento)
            var selHalo = new Lote();
            foreach (Barra3D b in mod.Barras)
                if (EhSel(b, b.Origem) && NoPorId(b.Ni) is No3D a && NoPorId(b.Nj) is No3D c && pr.Segmento(P(a), P(c), out Point pa, out Point pb)) selHalo.Add(pa, pb);
            selHalo.Desenhar(dc, CanetaLinha(Color.FromArgb(0x59, TemaPlant.CorBarraSel.R, TemaPlant.CorBarraSel.G, TemaPlant.CorBarraSel.B), 7));
            sel.Desenhar(dc, CanetaLinha(TemaPlant.Texto1, 2.25));

            // apoios e nós (ponto menor quando as barras ficam curtas na tela: o modelo inteiro de um prédio grande)
            double r = nb > 0 && soma / nb < 24 ? 2 : 3;
            foreach (No3D n in mod.Nos)
            {
                Point3D q = P(n); if (!pr.Visivel(q)) continue;
                Point s = pr.Tela(q);
                if (s.X < -30 || s.Y < -30 || s.X > ActualWidth + 30 || s.Y > ActualHeight + 30) continue;
                if (n.Apoios.Any(x => x))
                {
                    Vector baixo = pr.Tela(q - new Vector3D(0, 0, raio * 0.05)) - s;   // o "para baixo" do mundo na tela
                    if (baixo.Length < 2 || !pr.Visivel(q - new Vector3D(0, 0, raio * 0.05))) baixo = new Vector(0, 1);
                    baixo.Normalize();
                    Apoio(dc, s, baixo, n.Apoios[3] || n.Apoios[4] || n.Apoios[5], n.Apoios[0] && n.Apoios[1] && n.Apoios[2] || !n.Apoios[1] && n.Apoios[0] && n.Apoios[2]);
                }
                PontoNo(dc, s, EhSel(n, n.Origem), r);
            }
        }

        static void PontoNo(DrawingContext dc, Point s, bool sel, double r)
        {
            if (sel) dc.DrawEllipse(TemaPlant.BBarraSel, CanetaLinha(TemaPlant.Texto1, 1.25), s, r + 1.5, r + 1.5);
            else dc.DrawEllipse(TemaPlant.B(TemaPlant.Texto2), CanetaLinha(TemaPlant.BgCanvas, 1), s, r, r);
        }

        // Símbolo de apoio na tela, do lado de baixo do nó (direção `baixo`): engaste = base hachurada encostada no nó;
        // articulado (translações travadas) = triângulo + base hachurada; móvel = triângulo + base com folga (rolete).
        static void Apoio(DrawingContext dc, Point s, Vector baixo, bool engaste, bool fixo)
        {
            Vector lado = new Vector(-baixo.Y, baixo.X);
            Pen p = CanetaLinha(TemaPlant.CorApoio, 1.5), ph = CanetaLinha(TemaPlant.CorApoio, 1);
            double hb;   // distância do nó até a base
            if (engaste) hb = 3;
            else
            {
                hb = 11;
                var tri = new StreamGeometry();
                using (var c = tri.Open())
                {
                    c.BeginFigure(s + baixo * 2.5, true, true);
                    c.LineTo(s + baixo * hb + lado * 7, true, true);
                    c.LineTo(s + baixo * hb - lado * 7, true, true);
                }
                tri.Freeze();
                dc.DrawGeometry(TemaPlant.B(TemaPlant.CorApoio, 0x38), p, tri);
                if (!fixo) { hb += 3; }
            }
            Point b0 = s + baixo * hb;
            double meia = engaste ? 10 : 10;
            dc.DrawLine(CanetaLinha(TemaPlant.CorApoio, engaste ? 2.25 : 1.5), b0 - lado * meia, b0 + lado * meia);
            for (int k = 0; k < 5; k++)   // hachura a 45°
            {
                Point a = b0 + lado * (-meia + 2 + k * (2 * meia - 4) / 4.0);
                dc.DrawLine(ph, a, a + baixo * 5 - lado * 4);
            }
        }
    }
}
