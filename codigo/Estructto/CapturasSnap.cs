// 50–53) Snap e Medir 3D (Snap3D.cs, SnapOverlay.cs, Medir3D.cs, Vista3D.Snap.cs; docs/v3/SNAP-MEDIR.md).
// Pórtico espacial desenhado pelo motor; para cada modo, o cursor é posto perto do ponto e a captura sai inteira e
// ampliada 2× em volta do marcador (-zoom). Confere o tipo de snap e o valor medido. Só elas: ESTRUCTTO_CAPTURAS=snap.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasSnap(MainWindow w)
        {
            var log = new StringBuilder();
            void Falha(string s) => throw new InvalidOperationException("Snap 3D: " + s + " | " + w.StatusMsg.Text);
            bool real = Config.Realista3D;
            w.DefinirRealista(false);
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Vazio("Snap e medir"), "modelo vazio"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("Snap 3D: modelo vazio não abriu");
            Point3D Q(double x, double y, double z) => new Point3D(x, y, z);
            var segs = new (Point3D, Point3D)[]
            {
                (Q(0, 0, 0), Q(0, 0, 3)), (Q(6, 0, 0), Q(6, 0, 3)), (Q(0, 5, 0), Q(0, 5, 3)), (Q(6, 5, 0), Q(6, 5, 3)),   // pilares
                (Q(0, 0, 3), Q(6, 0, 3)), (Q(0, 5, 3), Q(6, 5, 3)), (Q(0, 0, 3), Q(0, 5, 3)), (Q(6, 0, 3), Q(6, 5, 3)),   // vigas
                (Q(0, 5, 0), Q(6, 5, 3)), (Q(6, 5, 0), Q(0, 5, 3)),                                                       // X de contraventamento (cruzam em (3; 5; 1,5))
                (Q(0, 0, 3), Q(6, 5, 3)),                                                                                 // diagonal da cobertura
            };
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(segs, "W310x38.7", true), "pórtico"));
            Esperar(d.Adicionar("secao", new JsonObject { ["forma"] = "tubo_circular", ["d"] = 0.2, ["t"] = 0.008, ["nome"] = "TC200x8" }));
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] { (Q(9, 0, 0), Q(9, 0, 3)) }, "TC200x8", true), "tubo"));
            if (d.Json["barras"].AsObject().Count != 12) Falha("esperado 12 barras, há " + d.Json["barras"].AsObject().Count);
            w.Faixa.SelectedTabItem = w.abaDesenhar;
            EsperarIdle();
            var v = w.vista3D;
            Point T(Point3D p) => v.Tela(p);
            void Vista() { v.MostrarVista(VistaPadrao.Iso, false); v.ZoomFator(1.15); }

            // foto inteira + recorte ampliado 2× em volta do ponto
            void Foto(string nome, Action prep, Func<Point3D> foco)
            {
                Action p2 = () => { Vista(); prep(); };
                CapturarPrincipal(w, nome, p2);
                p2(); EsperarIdle(); v.UpdateLayout();
                int lw = (int)v.ActualWidth, lh = (int)v.ActualHeight;
                if (lw < 50 || lh < 50) return;
                var rtb = new RenderTargetBitmap(lw, lh, 96, 96, PixelFormats.Pbgra32);
                var fundo = new DrawingVisual();
                using (var dc = fundo.RenderOpen()) dc.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, lw, lh));
                rtb.Render(fundo); rtb.Render(v);
                Point c = T(foco());
                int cw = Math.Min(lw, 420), ch = Math.Min(lh, 260);
                int x0 = (int)Math.Max(0, Math.Min(lw - cw, c.X - cw / 2.0)), y0 = (int)Math.Max(0, Math.Min(lh - ch, c.Y - ch / 2.0));
                var crop = new TransformedBitmap(new CroppedBitmap(rtb, new Int32Rect(x0, y0, cw, ch)), new ScaleTransform(2, 2));
                SalvarPng(crop, Path.Combine(pastaSaida, nome + "-zoom.png"));
            }
            void Confere(string oque, string esperado) { string t = v.TipoAtracao; log.AppendLine(oque + ": " + t + " | " + v.UltimoSnap?.Dica); if (t != esperado) Falha(oque + ": esperado " + esperado + ", veio " + t); }
            void Cursor(Point3D p, double dx, double dy) => v.MoverTela(T(p) + new Vector(dx, dy));
            // modos isolados: sem aquisição por pausa (senão o cursor parado na captura adquiriria o ponto)
            void SemPausa() { v.Snap.Rastreio = false; v.Snap.Modos &= ~ModoSnap.Extensao; v.Snap.LimparRastreio(); }
            ModoSnap padrao = v.Snap.Modos;

            w.UsarFerramenta3D(Ferramenta3D.Barra);
            v.PlanoAtual = PlanoTrabalho.Horizontal(3);
            Vista();
            SemPausa();

            void Modo(string nome, string tipo, Point3D alvo, double dx, double dy, Action antes = null)
            {
                Foto("50-snap-" + nome, () => { antes?.Invoke(); Cursor(alvo, dx, dy); }, () => alvo);
                Confere(nome, tipo);
            }
            Modo("extremidade", "Extremidade", Q(6, 0, 3), 4, 3);
            Modo("medio", "Ponto médio", Q(3, 0, 3), -3, 4);
            Modo("intersecao", "Interseção", Q(3, 5, 1.5), 3, -2);
            // interseção aparente: o 1º par de barras que se cruza na tela sem se tocar no 3D
            Vista();
            Point3D? app = null;
            var bs = segs.ToList();
            for (int i = 0; i < bs.Count && app == null; i++)
                for (int j = i + 1; j < bs.Count && app == null; j++)
                {
                    var (a1, b1) = bs[i]; var (a2, b2) = bs[j];
                    Snap3D.Segmentos(a1, b1, a2, b2, out _, out _, out Point3D c1, out Point3D c2);
                    if ((c1 - c2).Length < 0.3) continue;
                    Point p = T(a1), q = T(b1), r = T(a2), s = T(b2);
                    Vector e = q - p, f = s - r; double den = e.X * f.Y - e.Y * f.X; if (Math.Abs(den) < 1e-6) continue;
                    Vector g = r - p; double t = (g.X * f.Y - g.Y * f.X) / den, u = (g.X * e.Y - g.Y * e.X) / den;
                    if (!(t > 0.15 && t < 0.85 && u > 0.15 && u < 0.85)) continue;
                    Point x = p + e * t;   // longe (> 25 px) das pontas e dos meios das duas barras, para só a aparente valer
                    if (new[] { p, q, r, s, p + e / 2, r + f / 2 }.Any(o => (o - x).Length < 25)) continue;
                    app = a1 + (b1 - a1) * t;
                }
            if (app == null) Falha("nenhum par de barras com interseção aparente na vista Iso");
            Modo("aparente", "Interseção aparente", app.Value, 2, 2);
            Modo("no", "Nó", Q(0, 0, 3), 3, 3, () => v.Snap.Unico = ModoSnap.No);
            v.Snap.ConsumirUnico();
            Modo("proximo", "Mais próximo", Q(0, 2.2, 3), 0, 0, () => v.Snap.Unico = ModoSnap.Proximo);
            v.Snap.ConsumirUnico();
            // perpendicular: 1º ponto em (4,5; −3; 3), cursor sobre a viga V1 longe do pé
            Esperar(v.ClicarTela(T(Q(4.5, -3, 3))));
            if (v.InicioCorrente == null) Falha("1º ponto da barra não ficou");
            Modo("perpendicular", "Perpendicular", Q(1.8, 0, 3), 0, 2);
            if (!(v.UltimoSnap.P is Point3D pp) || (pp - Q(4.5, 0, 3)).Length > 1e-6) Falha("pé da perpendicular fora de (4,5; 0; 3)");
            // paralelo: pausa sobre a diagonal da cobertura e segue paralelo a ela a partir de (10; −2; 3)
            v.Tecla(System.Windows.Input.Key.Escape);
            Esperar(v.ClicarTela(T(Q(10, -2, 3))));
            Cursor(Q(2.4, 2, 3), 0, 0); v.EsperarSnap(600);
            if (v.Snap.Paralelas.Count != 1) Falha("pausa sobre a diagonal não adquiriu a direção");
            Vector3D dd6 = new Vector3D(6, 5, 0); dd6.Normalize();
            Modo("paralelo", "Paralelo", Q(10, -2, 3) + dd6 * 3, 2, -2);
            v.Tecla(System.Windows.Input.Key.Escape);
            v.Snap.LimparRastreio();

            // extensão: pausa na ponta (6; 0; 3) e segue além da viga V1
            v.Snap.Modos = padrao;
            Cursor(Q(6, 0, 3), 0, 0); v.EsperarSnap(600);
            if (v.Snap.Adquiridos.Count != 1) Falha("pausa na ponta não adquiriu: " + v.Snap.Adquiridos.Count);
            Modo("extensao", "Extensão", Q(8.2, 0, 3), 0, 3);
            v.Snap.LimparRastreio();

            // rastreio: adquire (9; 0; 3) e (6; 5; 3) e para no cruzamento dos caminhos em (9; 5; 3)
            v.Snap.Rastreio = true;
            Cursor(Q(9, 0, 3), 0, 0); v.EsperarSnap(600);
            Cursor(Q(6, 5, 3), 0, 0); v.EsperarSnap(600);
            if (v.Snap.Adquiridos.Count != 2) Falha("rastreio: " + v.Snap.Adquiridos.Count + " pontos adquiridos");
            Foto("51-snap-rastreio-intersecao", () => Cursor(Q(9, 5, 3), 4, -3), () => Q(9, 5, 3));
            Confere("rastreio", "Rastreio");
            if (!(v.UltimoSnap.P is Point3D pr) || (pr - Q(9, 5, 3)).Length > 1e-6 || v.UltimoSnap.Guias.Count != 2) Falha("cruzamento do rastreio fora de (9; 5; 3)");
            v.Snap.LimparRastreio();
            SemPausa();

            // polar 30° a partir de (6; 5; 3) e ORTO (F8)
            Esperar(v.ClicarTela(T(Q(6, 5, 3))));
            Point3D pol = Q(6, 5, 3) + new Vector3D(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0) * 4.5;
            Foto("52-snap-polar", () => Cursor(pol, 0, 0), () => pol);
            Confere("polar", "Polar");
            if (v.UltimoSnap.Dica != "Polar: 4,500 < 30°") Falha("tooltip do polar: " + v.UltimoSnap.Dica);
            v.Tecla(System.Windows.Input.Key.F8);
            Foto("52-snap-orto", () => Cursor(Q(9, 6.1, 3), 0, 0), () => Q(9, 5, 3));
            Confere("orto", "Ortogonal");
            v.Tecla(System.Windows.Input.Key.F10);   // volta o polar (desliga o orto)
            v.Tecla(System.Windows.Input.Key.Escape);

            // centro: Realista, passando na borda do tubo
            w.DefinirRealista(true);
            EsperarIdle();
            Modo("centro", "Centro", Q(9.1, 0, 3), 0, 0);
            if (!(v.UltimoSnap.P is Point3D pc) || (pc - Q(9, 0, 3)).Length > 1e-6) Falha("centro do tubo fora de (9; 0; 3)");
            Modo("extremidade-realista", "Extremidade", Q(0, 5, 3), 3, 3);

            // ---------- Medir ----------
            w.DefinirRealista(false);
            w.UsarFerramenta3D(Ferramenta3D.Medir);
            if (v.Ferramenta != Ferramenta3D.Medir) Falha("Medir não ligou");
            SemPausa();
            void Clicar(Point3D p, double dx = 0, double dy = 0) { Vista(); Cursor(p, dx, dy); Esperar(v.ClicarTela(T(p) + new Vector(dx, dy))); EsperarIdle(); }
            void Medida(string nome, string valor, Point3D foco, Point3D cursor)
            {
                Foto("53-medir-" + nome, () => v.MoverTela(new Point(v.ActualWidth * 0.12, v.ActualHeight * 0.3)), () => foco);   // cursor parado num canto vazio
                string r = v.Medidor.Res?.ValorTxt;
                log.AppendLine("medir " + nome + ": " + v.Medidor.Res?.Titulo + " = " + r);
                if (r != valor) Falha("medir " + nome + ": esperado " + valor + ", veio " + r);
            }
            Clicar(Q(0, 0, 0), 3, 2); Clicar(Q(6, 5, 3), 3, 2);
            Medida("distancia", "8,367 m", Q(3, 2.5, 1.5), Q(11, -3, 3));
            v.Tecla(System.Windows.Input.Key.Add);
            v.Tecla(System.Windows.Input.Key.Escape);
            Clicar(Q(6, 5, 3), 2, 2); Clicar(Q(0, 0, 3), 2, 2); Clicar(Q(6, 0, 3), -2, 2);
            Medida("angulo", "39,81°", Q(0, 0, 3), Q(11, -3, 3));
            v.Tecla(System.Windows.Input.Key.Escape);
            // barra + barra: tubo e diagonal da cobertura (reversas no ponto mais próximo), cliques no meio do vão
            // ponto de clique numa barra longe (> 25 px na tela) das outras barras e das próprias pontas/meio: só a barra vale
            Point3D Livre(Point3D a, Point3D b)
            {
                Vista();
                for (double t = 0.2; t <= 0.8; t += 0.025)
                {
                    Point3D p = a + (b - a) * t; Point s = T(p);
                    if (new[] { a, b, a + (b - a) / 2 }.Any(o => (T(o) - s).Length < 25)) continue;
                    if (segs.Any(x => !(x.Item1 == a && x.Item2 == b) && Snap3D.DistSeg(s, T(x.Item1), T(x.Item2), out _) < 25)) continue;
                    return p;
                }
                throw new InvalidOperationException("Snap 3D: barra sem ponto livre na tela");
            }
            Clicar(Livre(Q(9, 0, 0), Q(9, 0, 3))); Clicar(Livre(Q(0, 0, 3), Q(6, 5, 3)));
            Medida("minima", "5,762 m", Q(7, 2, 3), Q(11, -3, 3));
            if (!v.Medidor.Res.Linhas.Any(l => l.Val == "90,00°")) Falha("ângulo entre o tubo e a diagonal deveria ser 90°");
            v.Tecla(System.Windows.Input.Key.Escape);
            v.Medidor.TrocarModo(ModoMedir.Cadeia);
            Clicar(Q(0, 0, 0), 2, 2); Clicar(Q(0, 0, 3), 2, -2); Clicar(Q(6, 0, 3), 2, 2);
            v.Tecla(System.Windows.Input.Key.Subtract); Clicar(Q(3, 0, 3), 2, 3);
            v.Tecla(System.Windows.Input.Key.Add); Clicar(Q(3, 5, 3), 2, 3);
            Medida("cadeia", "11,000 m", Q(3, 2.5, 3), Q(11, -3, 3));
            v.Tecla(System.Windows.Input.Key.Enter);
            v.Medidor.TrocarModo(ModoMedir.Area);
            foreach (var p in new[] { Q(0, 0, 3), Q(6, 0, 3), Q(6, 5, 3), Q(0, 5, 3) }) Clicar(p, 2, 2);
            v.Tecla(System.Windows.Input.Key.Enter);
            Medida("area", "30,000 m²", Q(3, 2.5, 3), Q(11, -3, 3));
            v.Medidor.TrocarModo(ModoMedir.Auto);
            w.DefinirRealista(true);
            EsperarIdle();
            Clicar(Q(1.7, 0, 3));
            Medida("barra-realista", "6,000 m", Q(3, 0, 3), Q(11, -3, 3));
            if (!v.Medidor.Res.Linhas.Any(l => l.Rot == "Área da seção")) Falha("barra no Realista sem área da seção");
            v.Tecla(System.Windows.Input.Key.Escape); v.Tecla(System.Windows.Input.Key.Escape);
            if (v.Ferramenta != Ferramenta3D.Selecionar) Falha("2º Esc não saiu do Medir");

            v.Snap.Modos = padrao; v.Snap.Rastreio = true; v.Snap.LimparRastreio();
            w.DefinirRealista(real);
            File.WriteAllText(Path.Combine(pastaSaida, "50-snap.txt"), log.ToString(), new UTF8Encoding(false));
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
        }

        // 56) Pivô automático da órbita e zoom no cursor (Vista3D.Navegacao.cs): perto de uma junta do pórtico 2 × 2, Shift +
        // arrastar a partir da junta, com a marca do pivô na tela; Realista (Render3D quando há GPU) e analítico. Tempo do
        // pivô no pórtico de 2.430 barras em 56-tempos.txt. Só elas: ESTRUCTTO_CAPTURAS=navegacao.
        static void CapturasNavegacao(MainWindow w)
        {
            var v = w.vista3D;
            var log = new StringBuilder();
            void Falha(string s) => throw new InvalidOperationException("Navegação 3D: " + s);
            foreach (bool real in new[] { true, false })
            {
                string suf = real ? "realista" : "analitico";
                w.DefinirRealista(real);
                w.MostrarVista3D(true);
                EsperarIdle();
                v.Perspectiva = false;
                v.Carregar(Portico2x2(), false);
                Point3D J = new Point3D(6, 0, 3.5);   // junta viga-pilar da fachada da frente
                v.Focar(J + new Vector3D(0.5, 0, 0.25), 3, -40, 22);
                EsperarIdle(); v.UpdateLayout(); v.PrepararCaptura();
                Point c = v.Tela(J);
                var (pv, fonte) = v.PivoSob(c);
                log.AppendLine(suf + (v.UsandoDx ? " (Render3D)" : "") + ": pivô fonte " + fonte + " a " + (pv - J).Length.ToString("0.000", CultureInfo.InvariantCulture) + " m da junta");
                if (fonte != 'a' || (pv - J).Length > 0.4) Falha(suf + ": o pivô não caiu na junta sob o cursor (fonte " + fonte + ")");
                Point s0 = v.Tela(pv);
                v.SimularOrbita(c, c + new Vector(70, 25), 10, false);
                if (!(v.PivoAtual is Point3D pa) || (v.Tela(pa) - s0).Length > 1) Falha(suf + ": o pivô andou na tela durante a órbita");
                CapturarPrincipal(w, "56-orbita-pivo-" + suf);
                v.TerminarOrbita();

                // desempenho do pivô no pórtico grande (o mesmo das capturas 42)
                var g = new Modelo3D(); int nx = 12, ny = 10, nz = 6;
                int Id(int i, int j, int k) => 1 + i + (nx + 1) * (j + (ny + 1) * k);
                for (int k = 0; k <= nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                    g.Nos.Add(new No3D { Id = Id(i, j, k), X = i * 6, Y = j * 5, Z = k * 3.2, Apoios = k == 0 ? new[] { true, true, true, true, true, true } : new bool[6] });
                for (int k = 0; k < nz; k++) for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                {
                    g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k), Nj = Id(i, j, k + 1), Secao = "W 250 x 32,7" });
                    if (i < nx) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k + 1), Nj = Id(i + 1, j, k + 1), Secao = "W 310 x 38,7" });
                    if (j < ny) g.Barras.Add(new Barra3D { Id = g.Barras.Count + 1, Ni = Id(i, j, k + 1), Nj = Id(i, j + 1, k + 1), Secao = "W 310 x 38,7" });
                }
                v.Carregar(g, false);
                v.MostrarVista(VistaPadrao.Iso, false);
                EsperarIdle(); v.UpdateLayout(); v.PrepararCaptura();
                v.PivoSob(new Point(v.ActualWidth / 2, v.ActualHeight / 2));   // aquece
                var sw = System.Diagnostics.Stopwatch.StartNew(); var fontes = new StringBuilder();
                for (int i = 0; i < 20; i++) fontes.Append(v.PivoSob(new Point(v.ActualWidth * (0.25 + 0.025 * i), v.ActualHeight * (0.35 + 0.015 * i))).Fonte);
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: pivô em {1:0.00} ms por pedido ({2} barras; fontes {3})", suf, sw.Elapsed.TotalMilliseconds / 20, g.Barras.Count, fontes));
            }
            File.WriteAllText(Path.Combine(pastaSaida, "56-tempos.txt"), log.ToString());
            w.DefinirRealista(false);
        }
    }
}
