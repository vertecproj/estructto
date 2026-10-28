// W12 diagnóstico: a vista 3D do projetista "vibra"? Amostra a câmera (largura e posição), o tamanho da vista e o número de
// reconstruções, parado e depois de um recálculo, para cada tipo de ligação. Só ela: ESTRUCTTO_CAPTURAS=vibra.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasVibra(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var log = new StringBuilder();
            DesenhoLigacao Iso() => ((DesenhoLigacao[])typeof(JanelaLigacaoProjeto).GetField("vistas", bf).GetValue(j))[3];
            string Amostra()
            {
                var v = Iso(); var cam = (OrthographicCamera)typeof(DesenhoLigacao).GetField("cam", bf).GetValue(v);
                return $"{v.ActualWidth:0}x{v.ActualHeight:0} w{cam.Width:0.0} p({cam.Position.X:0.0},{cam.Position.Y:0.0},{cam.Position.Z:0.0}) r{v.Reconstrucoes} ({v.UltimaReconstrucaoMs:0.0} ms) vis{v.IsVisible}";
            }
            int Quadros()
            {
                var r = typeof(Vista3D).GetField("render", bf)?.GetValue(w.vista3D);
                return r == null ? -1 : (int)typeof(Render3D).GetField("quadros", bf).GetValue(r);
            }
            int Mudancas(string nome, int n, Action antes = null)
            {
                int q0 = Quadros(); long t0 = Environment.TickCount64;
                antes?.Invoke();
                string ult = null; int m = 0; var seq = new StringBuilder(); double maxIt = 0;
                for (int k = 0; k < n; k++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    EsperarIdle(); maxIt = Math.Max(maxIt, sw.Elapsed.TotalMilliseconds); System.Threading.Thread.Sleep(15);
                    string a = Amostra();
                    if (a != ult) { m++; if (m <= 12) seq.AppendLine("      " + k + ": " + a); }
                    ult = a;
                }
                log.AppendLine($"   {nome}: {m} mudanças em {n} amostras; 3D principal: {Quadros() - q0} quadros em {Environment.TickCount64 - t0} ms; maior espera da UI {maxIt:0} ms"); log.Append(seq);
                return m;
            }
            var ids = (tipos as JsonArray ?? new JsonArray()).Select(t => S(t, "tipo")).Where(s => s != "").ToList();
            Anim.ForcarLigado = true;
            foreach (int vista in new[] { 4 })
                foreach (var id in ids)
                {
                    Esperar(j.Escolher(id, true)); EsperarIdle();
                    j.MostrarVista(vista); EsperarIdle(); System.Threading.Thread.Sleep(400); EsperarIdle();
                    log.AppendLine($"[{id}] vista {vista} erro '{j.Erro}'");
                    Mudancas("parado", 120);
                    Mudancas("recalculo", 60, () => Esperar(j.CalcularAgora()));
                    Mudancas("parado depois", 80);
                    var t = j.DadosAtuais?["t"] ?? j.DadosAtuais?["tp"];
                    string ch = j.DadosAtuais?["t"] != null ? "t" : "tp";
                    if (t != null && double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double tv))
                        Mudancas("troca " + ch, 60, () => { j.DefinirDados(new JsonObject { [ch] = tv * 1.5 }); Esperar(j.CalcularAgora()); });
                    Mudancas("parado final", 80);
                    if (vista == 4 && Environment.GetEnvironmentVariable("VIBRA_QUADROS") == "1") QuadrosVibra(j, Iso(), log, id);
                }
            Anim.ForcarLigado = false;
            File.WriteAllText(Path.Combine(pastaSaida, "vibra.txt"), log.ToString());
        }
        // quadros reais da vista 3D (RenderTargetBitmap) durante um recálculo com os mesmos dados e durante um giro de 1° por quadro
        static void QuadrosVibra(JanelaLigacaoProjeto j, DesenhoLigacao v, StringBuilder log, string nome)
        {
            byte[] Px()
            {
                int W = (int)v.ActualWidth, H = (int)v.ActualHeight; if (W < 2 || H < 2) return new byte[0];
                var rt = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32); rt.Render(v);
                var b = new byte[W * H * 4]; rt.CopyPixels(b, W * 4, 0); return b;
            }
            int Dif(byte[] a, byte[] b) { if (a.Length != b.Length) return -1; int n = 0; for (int i = 0; i < a.Length; i += 4) if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) n++; return n; }
            void Pump() { v.Dispatcher.Invoke(() => { }, DispatcherPriority.Render); }
            var final0 = Px();
            var t = j.CalcularAgora(); var difs = new StringBuilder(); byte[] ant = final0;
            for (int k = 0; k < 40; k++) { System.Threading.Thread.Sleep(12); Pump(); var p = Px(); difs.Append(Dif(ant, p)).Append('/').Append(Dif(final0, p)).Append(' '); ant = p; }
            Esperar(t); EsperarIdle();
            log.AppendLine($"   quadros recálculo (vs anterior/vs inicial, px): {difs}");
            log.AppendLine($"   final vs inicial: {Dif(final0, Px())} px");
            var g = new StringBuilder(); ant = Px();
            for (int k = 1; k <= 20; k++)
            {
                v.Girar(50 + k, 22); Pump(); var p = Px(); g.Append(Dif(ant, p)).Append(' '); ant = p;
                if (k <= 4) SalvarPng(Bmp(v), Path.Combine(pastaSaida, $"vibra-{nome}-giro{k}.png"));
            }
            v.Girar(50, 22); Pump();
            log.AppendLine($"   giro 1°/quadro (px que mudam): {g}");
        }
        static BitmapSource Bmp(FrameworkElement v) { var rt = new RenderTargetBitmap((int)v.ActualWidth, (int)v.ActualHeight, 96, 96, PixelFormats.Pbgra32); rt.Render(v); return rt; }
    }
}
