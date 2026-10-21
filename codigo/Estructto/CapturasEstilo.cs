// 57) Estilo visual (SeletorEstilo.cs): os três estilos no pórtico 2 × 2, o seletor da vista aberto, o chip da barra de
// status aberto e o comando VS. Só elas: ESTRUCTTO_CAPTURAS=estilo.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasEstilo(MainWindow w)
        {
            void Falha(string s) => throw new InvalidOperationException("Estilo visual: " + s);
            var antes = Config.Estilo3D;
            w.Faixa.SelectedTabItem = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
            w.MostrarDiagrama(Diagrama.Nenhum);
            w.MostrarVista3D(true);
            var esp = Portico2x2();
            w.vista3D.LigacoesAmostra = true;
            w.vista3D.Diagrama = Diagrama3D.Nenhum; w.vista3D.MostrarDeformada = false; w.vista3D.MostrarAproveitamento = false;
            foreach (var (e, nome) in new[] { (EstiloVisual.Analitico, "analitico"), (EstiloVisual.Sombreado, "sombreado"), (EstiloVisual.Realista, "realista") })
            {
                w.DefinirEstilo(e);
                w.vista3D.Carregar(esp, false);
                w.vista3D.MostrarVista(VistaPadrao.Iso, false);
                EsperarIdle();
                if (Config.Estilo3D != e) Falha("vista3d.txt não guardou " + e);
                if (w.vista3D.Realista != (e != EstiloVisual.Analitico)) Falha("Realista errado em " + e);
                if (w.vista3D.UsandoDx != (e == EstiloVisual.Realista && Render3D.Disponivel)) Falha("Direct3D errado em " + e);
                if (!w.TextoSeletorEstilo.StartsWith(e == EstiloVisual.Analitico ? "Analítico" : e == EstiloVisual.Sombreado ? "Sombreado" : "Realista"))
                    Falha("seletor não mostra " + e);
                CapturarPrincipal(w, "57-estilo-" + nome);
            }

            // seletor da vista aberto (abaixo do ViewCube) e o chip da barra de status aberto (para cima)
            foreach (var (alvo, modo, nome) in new[] { ((FrameworkElement)w.SeletorEstiloVista, PlacementMode.Bottom, "menu-vista"), (w.ChipEstilo, PlacementMode.Top, "menu-status") })
            {
                w.AbrirMenuEstilo(alvo, modo);
                EsperarIdle();
                var m = w.MenuEstilo;
                if (!m.IsOpen) Falha("menu não abriu (" + nome + ")");
                if (m.Items.OfType<System.Windows.Controls.MenuItem>().Count(i => i.IsChecked) != 1) Falha("menu sem o estilo atual marcado");
                CapturarPrincipal(w, "57-estilo-" + nome, null, m, () =>
                {
                    m.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Point p = alvo.TranslatePoint(new Point(0, 0), w);
                    double x = p.X + alvo.ActualWidth - m.DesiredSize.Width;   // o mesmo do AbrirMenuEstilo (TranslatePoint já inclui o deslocamento do seletor)
                    return new Point(Math.Max(0, x), modo == PlacementMode.Bottom ? p.Y + alvo.ActualHeight : p.Y - m.DesiredSize.Height);
                });
                m.IsOpen = false;
                EsperarIdle();
            }

            // VS passa para o próximo; VS sombreado escolhe pelo nome
            w.DefinirEstilo(EstiloVisual.Realista);
            w.ExecutarComando("VS");
            if (Config.Estilo3D != EstiloVisual.Analitico) Falha("VS não passou de Realista para Analítico");
            w.ExecutarComando("VS sombreado");
            if (Config.Estilo3D != EstiloVisual.Sombreado) Falha("VS sombreado não escolheu o Sombreado");
            w.ExecutarComando("ESTILO 3");
            if (Config.Estilo3D != EstiloVisual.Realista) Falha("ESTILO 3 não escolheu o Realista");
            w.DefinirEstilo(antes);
        }
    }
}
