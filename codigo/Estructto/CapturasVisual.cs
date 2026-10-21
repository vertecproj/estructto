// 58) Close-ups da vista 3D (docs/VISTA3D.md, "Parafusos, ligações propostas e linhas do analítico"): parafusos da chapa de
// topo em várias órbitas (Realista), barras e nós do analítico em ângulos oblíquos (onde a barra parecia uma cantoneira) e o
// galpao_aco do motor só com Ligações › Identificar (as ligações sem projeto aparecem como propostas). Só a vista (recorte da
// janela de 1600 × 900). Só elas: ESTRUCTTO_CAPTURAS=visual.
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        // Só a área da Vista3D, renderizando a janela inteira (o D3DImage do Render3D só entra assim).
        static void FotoVista(MainWindow w, string nome, Action prep = null)
        {
            w.Width = 1600; w.Height = 900;
            w.Measure(new Size(1600, 900)); w.Arrange(new Rect(0, 0, 1600, 900)); w.UpdateLayout();
            prep?.Invoke(); EsperarIdle(); w.UpdateLayout();
            prep?.Invoke(); EsperarIdle();
            w.vista3D.PrepararCaptura();
            var rtb = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen()) dc.DrawRectangle(w.Background ?? JanelaEscura.BFundo, null, new Rect(0, 0, 1600, 900));
            rtb.Render(fundo); rtb.Render(w);
            var v = w.vista3D;
            Point o = v.TranslatePoint(new Point(0, 0), w);
            int x0 = Math.Max(0, (int)o.X), y0 = Math.Max(0, (int)o.Y);
            int lw = Math.Min(1600 - x0, (int)v.ActualWidth), lh = Math.Min(900 - y0, (int)v.ActualHeight);
            SalvarPng(new CroppedBitmap(rtb, new Int32Rect(x0, y0, lw, lh)), Path.Combine(pastaSaida, nome + ".png"));
        }

        static void CapturasVisual(MainWindow w)
        {
            var v = w.vista3D;
            var abaVista = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
            Func<int, int, int, int> nid = (i, j, k) => 1 + i + 3 * j + 9 * k;
            Point3D PNo(Modelo3D m, int id) { var n = m.Nos.First(x => x.Id == id); return new Point3D(n.X, n.Y, n.Z); }

            // parafusos da chapa de topo (exemplo do contrato) em várias órbitas: cabeça, arruela e porca fechadas, sem piscar na chapa
            w.DefinirRealista(true);
            w.Faixa.SelectedTabItem = abaVista;
            w.MostrarDiagrama(Diagrama.Nenhum);
            w.MostrarVista3D(true);
            EsperarIdle();
            v.LigacoesAmostra = true;
            v.Diagrama = Diagrama3D.Nenhum; v.MostrarDeformada = false; v.MostrarAproveitamento = false;
            var esp = Portico2x2();
            v.Carregar(esp, false);
            v.LimparSelecao(); v.Escolher(null);
            Point3D J = PNo(esp, nid(2, 0, 1)), C = J + new Vector3D(-0.2, 0, -0.05);   // chapa de topo da viga que chega pelo -x
            w.Toast.Visibility = Visibility.Collapsed;
            foreach (var (az, el) in new[] { (-35.0, 18.0), (-70.0, 35.0), (-110.0, 6.0), (-150.0, 25.0), (-60.0, -14.0), (-95.0, 60.0) })
            {
                string n = "58-parafusos-az" + az.ToString("0") + "-el" + el.ToString("0");
                FotoVista(w, n, () => v.Focar(C, 1.0, az, el));
            }
            v.Perspectiva = true;
            FotoVista(w, "58-parafusos-perspectiva", () => v.Focar(C, 1.0, -35, 18));
            v.Perspectiva = false;
            // longe: o modelo inteiro com as ligações (precisão de profundidade na distância)
            FotoVista(w, "58-parafusos-longe", () => v.Focar(J, 5, -35, 18));

            // analítico: barras e nós em ângulos oblíquos, seleção e aproveitamento
            w.DefinirRealista(false);
            w.Faixa.SelectedTabItem = abaVista;
            w.MostrarVista3D(true);
            EsperarIdle();
            v.Carregar(esp, false);
            v.LimparSelecao(); v.Escolher(null);
            foreach (var (az, el, larg) in new[] { (-55.0, 28.0, 0.0), (-35.0, 18.0, 4.0), (-80.0, 6.0, 2.5), (10.0, 62.0, 3.0) })
            {
                string n = "58-analitico-" + (larg == 0 ? "iso" : "az" + az.ToString("0") + "-el" + el.ToString("0"));
                FotoVista(w, n, () => { if (larg == 0) v.MostrarVista(VistaPadrao.Iso, false); else v.Focar(J, larg, az, el); });
            }
            v.Perspectiva = true;
            FotoVista(w, "58-analitico-perspectiva", () => v.Focar(J, 3, -35, 18));
            v.Perspectiva = false;
            v.MostrarAproveitamento = true;
            v.Carregar(esp, false);
            v.Escolher(esp.Barras[4]);
            FotoVista(w, "58-analitico-aproveitamento-selecao", () => v.MostrarVista(VistaPadrao.Iso, false));
            v.MostrarAproveitamento = false;
            v.Escolher(null);
            v.Carregar(esp, false);

            // galpao_aco do motor: só Identificar (nenhum projeto à mão): os grupos sem projeto aparecem como propostas
            w.MostrarVista3D(false);
            EsperarIdle();
            w.DefinirRealista(true);
            v.LigacoesAmostra = false;
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            EsperarIdle();
            Esperar(Motor.Post("/api/ligacoes/identificar"));
            w.Atualizar3D(false);
            Esperar(w.TarefaLigacoes3D); EsperarIdle();
            FotoVista(w, "58-galpao-propostas", () => v.MostrarVista(VistaPadrao.Iso, false));
            FotoVista(w, "58-galpao-propostas-joelho", () => v.Focar(new Point3D(0, 0, 6), 2.6, -40, 20));
            FotoVista(w, "58-galpao-propostas-base", () => v.Focar(new Point3D(0, 0, 0.2), 1.6, -40, 24));
            FotoVista(w, "58-galpao-propostas-cumeeira", () => v.Focar(new Point3D(10, 0, 7.9), 3, -50, 18));
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
            w.DefinirRealista(false);
        }
    }
}
