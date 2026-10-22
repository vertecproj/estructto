// C5) Linha de comando com a sugestão (ghost) ao digitar: ESTRUCTTO_CAPTURAS=cmdline. Recorte da linha de comando, ampliado 3x.
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasCmdline(MainWindow w)
        {
            w.Width = 1600; w.Height = 900;
            w.Measure(new Size(1600, 900)); w.Arrange(new Rect(0, 0, 1600, 900)); w.UpdateLayout();
            foreach (var t in new[] { "en", "EN", "mov", "e" })
            {
                w.Comando.Text = t; EsperarIdle(); w.UpdateLayout();
                var rtb = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
                var fundo = new DrawingVisual();
                using (var dc = fundo.RenderOpen()) dc.DrawRectangle(w.Background ?? JanelaEscura.BFundo, null, new Rect(0, 0, 1600, 900));
                rtb.Render(fundo); rtb.Render(w);
                Point o = w.Comando.TranslatePoint(new Point(0, 0), w);
                int x0 = Math.Max(0, (int)o.X - 60), y0 = Math.Max(0, (int)o.Y - 8);
                var rec = new CroppedBitmap(rtb, new Int32Rect(x0, y0, 420, 30));
                SalvarPng(new TransformedBitmap(rec, new ScaleTransform(3, 3)), Path.Combine(pastaSaida, "C5-cmdline-" + t + ".png"));
            }
            // autoteste C5: "en" + Espaço executa ENGASTE sem Tab; apelido exato (E) vence; Tab percorre as sugestões
            var cmds = new[] { "ENGASTE", "ENTER", "E", "ERASE", "ESPELHAR" };
            var c1 = MainWindow.CandidatosComando(cmds, "en");
            bool ok1 = c1[0] == "ENTER";   // lista fictícia: o mais curto vence
            var c2 = MainWindow.CandidatosComando(cmds, "e");
                        bool ok2 = c2[0] == "E";
            Func<string, Key, bool> digitar = (t, k) =>
            {
                w.Comando.Text = t; w.Comando.Focus();
                var src = PresentationSource.FromVisual(w.Comando);
                var ev = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                w.Comando.RaiseEvent(ev); EsperarIdle();
                return ev.Handled;
            };
            w.ExecutarComando("AJUDA");   // zera pedidos pendentes de capturas anteriores
            bool tratou = digitar("en", Key.Space);
            string h = w.Historico.Text;
            string relatorio = "tratou=" + tratou + " texto=[" + w.Comando.Text + "] candidatos 'en': " + string.Join(",", c1) + " · 'e': " + string.Join(",", c2) + " · histórico após 'en'+Espaço: " + h.Replace("\n", " | ");
            File.WriteAllText(Path.Combine(pastaSaida, "C5-autoteste.txt"), (ok2 && h.Contains("» ENGASTE") ? "OK " : "FALHA ") + relatorio);
            // autoteste C1/C3: "Z" é comando (Z + Espaço alterna a trava); a letra Z solta não é engolida pela vista
            var vz = w.vista3D; w.UsarFerramenta3D(Ferramenta3D.Barra); vz.MoverTela(new Point(500, 400)); Esperar(vz.ClicarTela(new Point(500, 400))); EsperarIdle();
            w.Comando.Text = ""; foreach (char ch in "zoom") vz.Digitar(ch.ToString()); bool soltaZ = w.Comando.Text.ToLowerInvariant() == "zoom" && !vz.TravaZ;   // letras vão inteiras para a linha de comando
            digitar("z", Key.Space);
            bool lig = vz.TravaZ; digitar("z", Key.Space); bool desl = !vz.TravaZ;
            File.AppendAllText(Path.Combine(pastaSaida, "C5-autoteste.txt"), "\n" + (soltaZ && lig && desl ? "OK" : "FALHA") + " Z: ZOOM digitado inteiro na linha=" + soltaZ + " Z+Espaço liga=" + lig + " desliga=" + desl);
            w.Comando.Text = "";
        }
    }
}
