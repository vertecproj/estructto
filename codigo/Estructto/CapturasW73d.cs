// W7-3D (docs/v3/W7-3D.md): o 3D do projetista com z-buffer. ESTRUCTTO_CAPTURAS=w73d (com ESTRUCTTO_MOTOR).
// Capturas da aba 3D girada (frente, lado, iso) da chapa de topo, chapa simples e cantoneira dupla, o tubo de exemplo feito à mão
// (ramal Ø114,3 × 6 a 45° num receptor Ø168,3 × 6,4) nas 4 vistas, a biblioteca, e o teste de ancoragem/ocultação dos parafusos
// (falha = exceção, vai para erros.txt). Medições em w73d-medicoes.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static readonly (string nome, double yaw, double pitch)[] AngulosW73d = { ("frente", -80, 10), ("lado", 12, 16), ("iso", 50, 22) };

        // tubo-tubo de exemplo no contrato "tubos" (sem o motor): ramal ao longo de x saindo do nó, receptor a 45° no plano xz
        public static JsonObject TuboExemplo()
        {
            var a = new Vector3D(Math.Cos(Math.PI / 4), 0, Math.Sin(Math.PI / 4));
            JsonArray A(Vector3D v) => new JsonArray(v.X, v.Y, v.Z);
            var ramal = new GeoLig.Tubo { O = new Point3D(), E = new Vector3D(1, 0, 0), L = 650, D = 114.3, T = 6, Corta = true, CO = new Point3D(), CE = a, CR = 168.3 / 2 };
            var (u, w) = ramal.Base();
            var cordao = new JsonArray();
            for (int k = 0; k <= 48; k++)
            {
                double th = 2 * Math.PI * (k % 48) / 48; var p0 = ramal.O + (u * Math.Cos(th) + w * Math.Sin(th)) * ramal.D / 2;
                cordao.Add(A((Vector3D)(p0 + ramal.E * ramal.Inicio(p0))));
            }
            return new JsonObject
            {
                ["tubos"] = new JsonArray(
                    new JsonObject { ["nome"] = "receptor", ["grupo"] = "suporte", ["campo"] = "t_receptor", ["origem"] = A(-a * 450), ["eixo"] = A(a), ["comprimento"] = 900, ["d"] = 168.3, ["t"] = 6.4, ["corte"] = null },
                    new JsonObject { ["nome"] = "ramal", ["grupo"] = "viga", ["campo"] = "t_ramal", ["origem"] = A(new Vector3D()), ["eixo"] = A(new Vector3D(1, 0, 0)), ["comprimento"] = 650, ["d"] = 114.3, ["t"] = 6,
                                     ["corte"] = new JsonObject { ["origem"] = A(new Vector3D()), ["eixo"] = A(a), ["r"] = 168.3 / 2 } }),
                ["soldas"] = new JsonArray(new JsonObject { ["tipo"] = "filete", ["perna"] = 5, ["pontos"] = cordao, ["campo"] = "b_solda" }),
            };
        }

        static (Window w, DesenhoLigacao d) Janela3DW73d(GeoLig g, int larg = 900, int alt = 680)
        {
            var d = new DesenhoLigacao(VistaLig.Iso) { Titulo = "3D" };
            var w = new Window { Content = d, Width = larg, Height = alt, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = TemaPlant.BBgCanvas };
            PrepararDialogoForaDaTela(w); w.Show(); EsperarIdle();
            d.Mostrar(g, false); EsperarIdle();
            return (w, d);
        }

        static void CapturasW73d(MainWindow mw)
        {
            var med = new StringBuilder("W7-3D: 3D do projetista (Viewport3D, z-buffer)\n");

            // 1) tubo de exemplo: 4 vistas e 3D girado
            var gt = GeoLig.De(TuboExemplo());
            if (gt.Tubos.Count != 2 || !gt.Tubos[1].Corta) throw new InvalidOperationException("w73d: tubos do exemplo não lidos");
            {
                var grade = new Grid { Background = TemaPlant.BBgCanvas };
                for (int i = 0; i < 2; i++) { grade.ColumnDefinitions.Add(new ColumnDefinition()); grade.RowDefinitions.Add(new RowDefinition()); }
                var vs = new[] { new DesenhoLigacao(VistaLig.Elevacao) { Titulo = "Elevação" }, new DesenhoLigacao(VistaLig.Planta) { Titulo = "Planta" },
                                 new DesenhoLigacao(VistaLig.Lateral) { Titulo = "Lateral" }, new DesenhoLigacao(VistaLig.Iso) { Titulo = "3D" } };
                for (int i = 0; i < 4; i++) { Grid.SetColumn(vs[i], i % 2); Grid.SetRow(vs[i], i / 2); vs[i].Margin = new Thickness(i % 2, i / 2, 0, 0); grade.Children.Add(vs[i]); }
                var w = new Window { Content = grade, Width = 1200, Height = 900, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = TemaPlant.BBorda };
                PrepararDialogoForaDaTela(w); w.Show(); EsperarIdle();
                foreach (var v in vs) v.Mostrar(gt, false);
                EsperarIdle();
                RenderizarJanela(w, 1200, 900, "w73d-tubo-vistas.png");
                if (vs[0].PecaEm(vs[0].Projecao?.Invoke(new Point3D(400, 0, 0)) ?? new Point()) != "t_ramal" && vs[0].Projecao != null)
                    throw new InvalidOperationException("w73d: clique no ramal (elevação) não achou o campo");
                w.Close();
            }
            {
                var (w, d) = Janela3DW73d(gt);
                foreach (var (nome, yaw, pitch) in AngulosW73d) { d.Girar(yaw, pitch); EsperarIdle(); RenderizarJanela(w, (int)w.Width, (int)w.Height, "w73d-tubo-3d-" + nome + ".png"); }
                med.AppendLine($"tubo de exemplo: {d.Pecas3D.Count} peças, reconstrução {d.UltimaReconstrucaoMs:0.0} ms");
                w.Close();
            }

            // 2) padrões do motor
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            mw.MostrarLigacao(j); EsperarIdle();   // W7: aba da janela principal
            Esperar(j.CarregarMiniaturas()); EsperarIdle();
            CapturarDialogo(j, "w73d-biblioteca.png");
            foreach (var t in new[] { "chapa_topo_estendida", "chapa_simples_alma", "cantoneira_dupla_alma" })
            {
                Esperar(j.Escolher(t, false)); Esperar(j.CalcularAgora()); EsperarIdle();
                var geom = j.Resultado?["geometria"];
                if (geom == null) throw new InvalidOperationException("w73d: sem geometria do motor para " + t);
                string arq = t.Replace('_', '-');
                CapturarDialogo(j, "w73d-" + arq + "-vistas.png");
                var g = GeoLig.De(geom);
                var (w, d) = Janela3DW73d(g);
                foreach (var (nome, yaw, pitch) in AngulosW73d) { d.Girar(yaw, pitch); EsperarIdle(); RenderizarJanela(w, (int)w.Width, (int)w.Height, "w73d-" + arq + "-3d-" + nome + ".png"); }
                TestarParafusos3D(t, d, med);
                var ms = new List<double>();
                for (int k = 0; k < 20; k++) { d.Mostrar(GeoLig.De(geom), false); ms.Add(d.UltimaReconstrucaoMs); }
                var sw = System.Diagnostics.Stopwatch.StartNew(); for (int k = 0; k < 20; k++) GeoLig.De(geom); double msLer = sw.Elapsed.TotalMilliseconds / 20;
                med.AppendLine($"{t}: {d.Pecas3D.Count} peças, {g.Parafs.Count} parafusos; reconstrução da cena média {ms.Average():0.00} ms (máx {ms.Max():0.00}), leitura da geometria {msLer:0.00} ms");
                w.Close();
            }
            j.MostrarVista(4); EsperarIdle();
            CapturarDialogo(j, "w73d-cantoneira-dupla-alma-aba-3d.png");
            j.Close();
            File.WriteAllText(Path.Combine(pastaSaida, "w73d-medicoes.txt"), med.ToString());
        }

        // Ancoragem: a malha de cada parafuso vai da cabeça na face de entrada (O, com a arruela) até a ponta depois da porca, e girar a
        // câmera não reconstrói nem move nada. Ocultação: olhando de lado (⟂ ao eixo), o ponto no meio da pega está dentro das chapas e o
        // 1º acerto do hit-test não é o parafuso (no pintor ele ficava por cima); olhando da cabeça, ela aparece.
        static void TestarParafusos3D(string nome, DesenhoLigacao d, StringBuilder med)
        {
            var g = d.Geo; var bolts = d.Pecas3D.Where(p => p.Tipo == "parafuso").ToList();
            if (bolts.Count == 0 || bolts.Count != g.Parafs.Count) throw new InvalidOperationException($"w73d {nome}: {bolts.Count} parafusos na cena, {g.Parafs.Count} na geometria");
            foreach (var pc in bolts)
            {
                var b = g.Parafs[pc.Indice]; var e = b.E; e.Normalize();
                var pos = ((MeshGeometry3D)pc.M.Geometry).Positions;
                double mn = pos.Min(p => Vector3D.DotProduct(p - b.O, e)), mx = pos.Max(p => Vector3D.DotProduct(p - b.O, e));
                var (ini, fim) = Cena3DLig.Extensao(b);
                if (Math.Abs(mn - ini) > 0.01 * b.D || Math.Abs(mx - fim) > 0.01 * b.D)
                    throw new InvalidOperationException($"w73d {nome}: parafuso {pc.Indice} fora do lugar: eixo de {mn:0.0} a {mx:0.0} mm, esperado {ini:0.0} a {fim:0.0}");
            }
            var limites = bolts.Select(p => p.M.Bounds).ToList(); int rec = d.Reconstrucoes; var modelos = bolts.Select(p => p.M).ToList();
            int giros = 0;
            for (double yaw = 0; yaw < 360; yaw += 30)
                foreach (double pitch in new[] { -60.0, 0, 60 })
                {
                    d.Girar(yaw, pitch); giros++;
                    var agora = d.Pecas3D.Where(p => p.Tipo == "parafuso").ToList();
                    if (d.Reconstrucoes != rec || agora.Count != modelos.Count || agora.Where((p, i) => !ReferenceEquals(p.M, modelos[i]) || p.M.Bounds != limites[i]).Any())
                        throw new InvalidOperationException($"w73d {nome}: parafuso mudou ao girar (yaw {yaw}, pitch {pitch})");
                }
            int ocultos = 0, visiveis = 0;
            bool Na(Point p) => p.X > 2 && p.Y > 2 && p.X < d.ActualWidth - 2 && p.Y < d.ActualHeight - 2;
            void Olhar(Vector3D F) { F.Normalize(); d.Girar(Math.Atan2(F.Y, F.X) * 180 / Math.PI, Math.Asin(F.Z) * 180 / Math.PI); }
            foreach (var pc in bolts)
            {
                var b = g.Parafs[pc.Indice]; var e = b.E; e.Normalize();
                var p1 = Vector3D.CrossProduct(e, Math.Abs(e.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0)); p1.Normalize();
                var p2 = Vector3D.CrossProduct(e, p1);
                foreach (var F in new[] { p1, -p1, p2, -p2 })
                {
                    if (Math.Abs(F.Z) > 0.99) continue;
                    Olhar(F);
                    var pt = d.Projecao(b.O + e * (Render3DGeometria.PegaEfetiva(b.D, b.L, b.Pega) / 2)); if (!Na(pt)) continue;
                    var h = d.Peca3DEm(pt);
                    if (h == pc) throw new InvalidOperationException($"w73d {nome}: parafuso {pc.Indice} visto através da chapa (olhando de {F})");
                    ocultos++;
                }
                if (Math.Abs(e.Z) < 0.99)
                {
                    Olhar(-e);
                    var pt = d.Projecao(b.O - e * 0.5 * b.D);
                    if (Na(pt) && d.Peca3DEm(pt) == pc) visiveis++;
                }
            }
            if (ocultos == 0) throw new InvalidOperationException($"w73d {nome}: nenhuma vista de lado testou a ocultação");
            if (visiveis == 0) throw new InvalidOperationException($"w73d {nome}: nenhuma cabeça de parafuso visível olhando de frente para ela");
            d.Girar(50, 22);
            med.AppendLine($"{nome}: ancoragem ok em {giros} ângulos sem reconstruir; {ocultos} vistas de lado com o fuste oculto; {visiveis}/{bolts.Count} cabeças visíveis de frente");
        }
    }
}
