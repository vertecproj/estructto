// Estilo visual da Vista3D, onde os programas 3D põem: seletor "Estilo ▾" na vista, logo abaixo do ViewCube e do
// "Ortográfica ▾" (os controles de viewport "[Vista][Estilo]" do AutoCAD/Plant 3D), chip na barra de status (a View Control
// Bar do Revit) e "Estilo visual" na aba Vista. Os três abrem o mesmo menu; comando ESTILO / VS (VSCURRENT do AutoCAD)
// troca para o próximo. docs/v3/PESQUISA-PLANT.md 2.6.
//   Analítico = linhas e nós; Sombreado = o Realista nativo (Viewport3D, o fallback sem GPU); Realista = Render3D (DX11).
// ponytail: sem Raio-X; pede transparência nos materiais do Realista (Render3D*.cs/Vista3D.cs), que outro worktree reescreve.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using F = Fluent;

namespace NovoFtool
{
    public enum EstiloVisual { Analitico, Sombreado, Realista }

    public partial class MainWindow
    {
        static readonly (EstiloVisual E, string Nome, string Desc)[] Estilos =
        {
            (EstiloVisual.Analitico, "Analítico", "Linhas e nós do modelo de cálculo"),
            (EstiloVisual.Sombreado, "Sombreado", "Perfis reais sombreados, leve"),
            (EstiloVisual.Realista, "Realista", "Perfis, sombras entre peças, ligações (Direct3D)"),
        };
        static string NomeEstilo(EstiloVisual e) => Estilos.First(x => x.E == e).Nome;

        Button seletorEstilo, chipEstilo;
        TextBlock txSeletorEstilo, txChipEstilo;
        ContextMenu menuEstilo;
        F.DropDownButton faixaEstilo;

        public EstiloVisual Estilo => Config.Estilo3D;

        // Ligar = Realista, desligar = Analítico (botão antigo, testes e capturas).
        public void DefinirRealista(bool ligar) { DefinirEstilo(ligar ? EstiloVisual.Realista : EstiloVisual.Analitico); }

        public void DefinirEstilo(EstiloVisual e)
        {
            bool mudou = e != Config.Estilo3D;
            Config.Estilo3D = e;
            AtualizarEstiloUi(mudou);
            if (vista3D != null) { if (mudou) vista3D.CargasSempre = null; vista3D.Realista = e != EstiloVisual.Analitico; vista3D.SemDx = e == EstiloVisual.Sombreado; vista3D.Reconstruir(); if (Esp != null && vista3D.Realista && vista3D.ModeloAtual.Ligacoes.Count == 0) LigacoesEspacial3D(Esp); }   // vindo do Analítico: busca as ligações
            if (tgCargasTopo != null && mudou) tgCargasTopo.IsChecked = e == EstiloVisual.Analitico;   // estado efetivo do estilo
            if (e != EstiloVisual.Analitico && !Em3D) MostrarVista3D(true);
        }

        void ProximoEstilo() { DefinirEstilo((EstiloVisual)(((int)Config.Estilo3D + 1) % Estilos.Length)); Log("Estilo visual: " + NomeEstilo(Config.Estilo3D) + "."); }

        // ESTILO / VS [nome ou começo do nome | 1-3]; sem argumento = o próximo.
        void CmdEstilo(string[] w)
        {
            if (w.Length < 2) { ProximoEstilo(); return; }
            string a = w[1].ToLowerInvariant().Replace('í', 'i');
            var achou = Estilos.Where((x, i) => a == (i + 1).ToString() || x.E.ToString().ToLowerInvariant().StartsWith(a)).ToList();
            if (achou.Count != 1) { Log("Estilos: " + string.Join(", ", Estilos.Select(x => x.Nome)) + " (VS sem nome passa para o próximo).", false); return; }
            DefinirEstilo(achou[0].E);
            Log("Estilo visual: " + achou[0].Nome + ".");
        }

        string DescEstilo(EstiloVisual e) =>
            e == EstiloVisual.Realista && !Render3D.Disponivel ? "Sem placa de vídeo Direct3D: igual ao Sombreado" : Estilos.First(x => x.E == e).Desc;

        // Aba Vista › Navegar: lista com os mesmos estilos (substitui o antigo botão Realista).
        F.DropDownButton BotaoEstilo()
        {
            const string dica = "Como o modelo aparece na vista 3D: Analítico (linhas e nós), Sombreado (perfis reais, leve) ou Realista (sombras entre peças, ligações). Também no canto da vista, abaixo do ViewCube, e na barra de status. A escolha fica guardada.";
            faixaEstilo = new F.DropDownButton { Header = "Estilo visual", LargeIcon = Icone("realista", 32), Icon = Icone("realista", 16), Size = F.RibbonControlSize.Large, ToolTip = Dica("Estilo visual", dica, "VS") };
            Tamanho(faixaEstilo, false);
            faixaEstilo.DropDownOpened += (s, e) =>
            {
                faixaEstilo.Items.Clear();
                foreach (var x in Estilos)
                {
                    var mi = new F.MenuItem { Header = x.Nome, Description = DescEstilo(x.E), IsCheckable = true, IsChecked = x.E == Config.Estilo3D };
                    mi.Click += (s2, e2) => { DefinirEstilo(x.E); FocarVista(); };
                    faixaEstilo.Items.Add(mi);
                }
            };
            // Ctrl+K: o "Realista" de antes continua (liga/desliga), mais um item por estilo e o próximo
            Reg("realista", "Realista", null, "Perfis reais com sombras (liga/desliga; desligado = Analítico).", () => DefinirRealista(Config.Estilo3D != EstiloVisual.Realista));
            Reg("realista", "Estilo visual", "VS", "Passa para o próximo estilo visual: Analítico, Sombreado, Realista.", ProximoEstilo);
            foreach (var x in Estilos.Where(x => x.E != EstiloVisual.Realista)) Reg("realista", "Estilo " + x.Nome, null, x.Desc + ".", () => DefinirEstilo(x.E));
            return faixaEstilo;
        }

        // Seletor na vista (abaixo do ViewCube) e chip na barra de status; chamado uma vez, depois de MontarStatus.
        void MontarEstiloVisual()
        {
            txSeletorEstilo = new TextBlock { FontFamily = TemaPlant.Fonte, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2 };
            seletorEstilo = new Button
            {
                Content = txSeletorEstilo, Style = EstiloBotaoStatus(), Focusable = false,
                // ViewCube: 132 px de largura a 8 px da direita, texto centrado 6 px à direita do meio, "Ortográfica ▾" até ~152 px
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 154, 8 + 66 - 6, 0),
                RenderTransform = new TranslateTransform(), ToolTip = Dica("Estilo visual", "Analítico, Sombreado ou Realista. O comando VS passa para o próximo.", "VS"),
            };
            seletorEstilo.SizeChanged += (s, e) => ((TranslateTransform)seletorEstilo.RenderTransform).X = e.NewSize.Width / 2;   // centro na coluna do cubo
            seletorEstilo.Click += (s, e) => AbrirMenuEstilo(seletorEstilo, PlacementMode.Bottom);
            GridVista.Children.Add(seletorEstilo);

            txChipEstilo = new TextBlock { FontSize = 11, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center };
            var marca = IconeStatus("M2,6 L9,2.5 L16,6 L9,9.5 Z M2,6 V13 L9,16.5 V9.5 M16,6 V13 L9,16.5");
            marca.Width = marca.Height = 12; marca.StrokeThickness = 1.3; marca.Margin = new Thickness(0, 0, 5, 0); marca.VerticalAlignment = VerticalAlignment.Center;
            var linha = new StackPanel { Orientation = Orientation.Horizontal };
            linha.Children.Add(marca); linha.Children.Add(txChipEstilo);
            chipEstilo = new Button
            {
                Style = EstiloBotaoStatus(), Focusable = false, VerticalAlignment = VerticalAlignment.Center,
                Content = new Border { Child = linha, CornerRadius = new CornerRadius(3), Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 7, 1) },
                ToolTip = Dica("Estilo visual", "Clique para trocar: Analítico, Sombreado ou Realista.", "VS"),
            };
            chipEstilo.Click += (s, e) => AbrirMenuEstilo(chipEstilo, PlacementMode.Top);
            NovoBloco();
            blocoStatus.Children.Add(chipEstilo);
            AtualizarEstiloUi(false);
        }

        void AtualizarEstiloUi(bool animar)
        {
            if (txSeletorEstilo == null) return;
            string n = NomeEstilo(Config.Estilo3D);
            txSeletorEstilo.Text = n + " ▾";
            txChipEstilo.Text = n;
            if (animar) { Anim.Surgir(txSeletorEstilo, Anim.Rapido); Anim.Surgir(txChipEstilo, Anim.Rapido); }
        }

        // O mesmo menu para o seletor da vista e o chip; marca o estilo atual.
        internal ContextMenu MenuEstilo
        {
            get
            {
                if (menuEstilo != null) return menuEstilo;
                menuEstilo = new ContextMenu { MinWidth = 236 };
                foreach (var x in Estilos)
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = x.Nome, Foreground = TemaPlant.BTexto1 });
                    var desc = new TextBlock { FontSize = 10.5, Foreground = TemaPlant.BTexto3, Margin = new Thickness(0, 1, 0, 0) };
                    sp.Children.Add(desc);
                    var mi = new MenuItem { Header = sp, Tag = x.E };
                    mi.Click += (s, e) => { DefinirEstilo(x.E); FocarVista(); };
                    menuEstilo.Items.Add(mi);
                }
                menuEstilo.Items.Add(new Separator());
                var prox = new MenuItem { Header = "Próximo estilo", InputGestureText = "VS" };
                prox.Click += (s, e) => { ProximoEstilo(); FocarVista(); };
                menuEstilo.Items.Add(prox);
                menuEstilo.Opened += (s, e) => MarcarMenuEstilo();
                return menuEstilo;
            }
        }

        void MarcarMenuEstilo()
        {
            foreach (var mi in MenuEstilo.Items.OfType<MenuItem>().Where(m => m.Tag is EstiloVisual))
            {
                var e = (EstiloVisual)mi.Tag;
                mi.IsChecked = e == Config.Estilo3D;
                ((TextBlock)((StackPanel)mi.Header).Children[1]).Text = DescEstilo(e);
            }
        }

        // Alinhado pela direita do alvo (o seletor fica junto da borda direita da vista, o chip no fim da barra de status).
        internal void AbrirMenuEstilo(FrameworkElement alvo, PlacementMode modo)
        {
            var m = MenuEstilo;
            m.PlacementTarget = alvo; m.Placement = PlacementMode.Custom;
            m.CustomPopupPlacementCallback = (menu, alvoT, _) => new[] { new CustomPopupPlacement(new Point(alvoT.Width - menu.Width, modo == PlacementMode.Top ? -menu.Height : alvoT.Height), PopupPrimaryAxis.Horizontal) };
            MarcarMenuEstilo();
            m.IsOpen = true;
        }

        internal Button SeletorEstiloVista => seletorEstilo;
        internal Button ChipEstilo => chipEstilo;
        internal string TextoSeletorEstilo => txSeletorEstilo.Text;
    }
}
