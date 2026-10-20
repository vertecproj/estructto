// Tema visual no padrao do AutoCAD Plant 3D 2027 (reconstruido em sala limpa: so cores, medidas e disposicao
// observadas na tela; nenhum arquivo, icone ou logotipo da Autodesk foi copiado).
//
// Especificacao observada (capturas em C:\dev\ux-teste\plant1.png / plant2.png):
//  - Barra de titulo + acesso rapido na mesma faixa, ardosia #454F61, texto do titulo centralizado cinza claro.
//  - Faixa das abas da ribbon em azul-marinho escuro #222933; aba ativa preenchida com a cor do painel #3B4453,
//    texto das abas cinza claro, sem sublinhado.
//  - Painel da ribbon #3B4453; grupos separados por fio escuro; titulo do grupo embaixo, centralizado, texto claro.
//  - Botoes grandes: icone ~32 px monocromatico claro, rotulo em ate 2 linhas embaixo; destaque ao passar o mouse
//    com caixa de contorno azul-acinzentado.
//  - Barra das abas de arquivo #222933 com icone "menu" a esquerda, separadores inclinados "/", aba ativa #3B4453
//    em negrito com "x", e "+" para novo.
//  - Paletas (Project Manager / Properties): fundo #3B4453; cabecalho em MAIUSCULAS sobre #2E3440 com pegador
//    pontilhado; secoes internas com faixa #2E3440 e titulo em negrito; campos de texto/busca #4E5A6E.
//  - Area de desenho (espaco do modelo) escura #212830; layout/papel cinza.
//  - Rodape #222933: abas "Model / Layout" a esquerda (ativa #3B4453), e a direita botoes-icone de status
//    agrupados em blocos #3B4453, ligados = icone azul.
//  - Fonte da interface: Artifakt Element se instalada (vem com os produtos Autodesk do usuario), senao Segoe UI.
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AdKeys = AvalonDock.Themes.VS2013.Themes.ResourceKeys;

namespace NovoFtool
{
    public static class TemaPlant
    {
        public static readonly Color Titulo = C("#454F61"), Abas = C("#222933"), Painel = C("#3B4453"), Cabecalho = C("#2E3440"),
            Campo = C("#4E5A6E"), Texto = C("#F0F2F5"), TextoFraco = C("#B4BCC8"), Destaque = C("#0696D7"), Hover = C("#4C5768"),
            ContornoHover = C("#6C7A90"), Modelo = C("#212830");

        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static FontFamily Fonte
        {
            get
            {
                foreach (var f in Fonts.SystemFontFamilies) if (f.Source == "Artifakt Element") return f;
                return new FontFamily("Segoe UI");
            }
        }

        static bool dicasConfiguradas;

        // Atraso curto (~300 ms) e exibicao longa (~20 s) para todas as dicas (ToolTip / ScreenTip) do programa.
        public static void ConfigurarDicas()
        {
            if (dicasConfiguradas) return;
            dicasConfiguradas = true;
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(300));
            System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(20000));
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkContentElement), new FrameworkPropertyMetadata(300));
            System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkContentElement), new FrameworkPropertyMetadata(20000));
        }

        // Sobrescreve as cores do tema escuro do Fluent.Ribbon (as chaves no topo dos recursos vencem as do tema).
        public static void AplicarFluent(Application app)
        {
            ConfigurarDicas();
            var r = app.Resources;
            void S(string k, Color c) { r["Fluent.Ribbon.Brushes." + k] = B(c); }
            S("RibbonWindow.TitleBackground", Titulo); S("RibbonWindow.TitleForeground", TextoFraco);
            S("RibbonWindow.Background", Abas);
            S("WindowCommands.CaptionButton.Background", Titulo); S("WindowCommands.CaptionButton.Foreground", Texto);
            S("WindowCommands.CaptionButton.MouseOver.Background", Hover);
            S("Ribbon.Background", Abas);
            S("RibbonTabControl.Background", Abas); S("RibbonTabControl.TabsGrid.Background", Abas);
            S("RibbonTabControl.TabsGrid.Foreground", Texto); S("RibbonTabControl.Foreground", Texto);
            S("RibbonTabControl.Content.Background", Painel); S("RibbonTabControl.Content.Border", Abas); S("RibbonTabControl.Content.Foreground", Texto);
            S("RibbonTabItem.Active.Background", Painel); S("RibbonTabItem.Border", Painel);
            S("RibbonTabItem.Foreground", C("#D5DAE2")); S("RibbonTabItem.Selected.Foreground", Texto);
            S("RibbonTabItem.MouseOver.Background", C("#2E3542")); S("RibbonTabItem.MouseOver.Foreground", Texto); S("RibbonTabItem.Selected.MouseOver.Foreground", Texto);
            S("RibbonGroupBox.Header.Foreground", Texto);
            S("GroupSeparator.Background", Abas);
            S("LabelText", C("#D5DAE2")); S("IdealForeground", Texto); S("RibbonTabControl.Content.Foreground", C("#D5DAE2"));
            S("Button.MouseOver.Background", Hover); S("Button.MouseOver.Border", ContornoHover);
            S("Button.Pressed.Background", C("#2E3540")); S("Button.Pressed.Border", ContornoHover);
            S("ToggleButton.Checked.Background", C("#2B4E72")); S("ToggleButton.Checked.Border", C("#5C9BD1"));
            S("ToggleButton.CheckedMouseOver.Background", C("#33608C")); S("ToggleButton.CheckedMouseOver.Border", C("#7FB4E3"));
            S("TextBox.Background", Campo); S("TextBox.Border", C("#5A677C")); S("TextBox.Caret", Texto);
            S("TextBox.MouseOver.Background", Campo); S("TextBox.MouseOver.Border", ContornoHover);
            S("TextBox.Focus.Background", Campo); S("TextBox.Focus.Border", Destaque); S("TextBox.Selection", Destaque);
            S("DropDown.Background", Painel); S("DropDown.Border", Abas);
            S("MenuItem.Background", Painel);
            // dicas escuras: o texto do ScreenTip herda a cor clara do tema e sumia no fundo claro
            S("ScreenTip.Background", C("#1F242C")); S("ScreenTip.Border", C("#5A6578"));
            S("Backstage.Background", Painel); S("Backstage.Foreground", Texto);
            S("BackstageTabControl.Background", Painel); S("BackstageTabControl.ItemsPanelBackground", Cabecalho);
            S("BackstageTabItem.Header.Foreground", Texto); S("BackstageTabItem.Selected.Background", Painel);
            S("BackstageTabItem.MouseOver.Background", Hover); S("BackstageTabControl.Button.MouseOver.Background", Hover);
            S("Backstage.BackButton.Background", Cabecalho); S("Backstage.BackButton.Foreground", Texto);
            S("KeyTip.Background", C("#F4F5F7")); S("KeyTip.Border", C("#8A93A3"));
            S("Separator.Background", Abas); S("Separator.Border", Abas);
            S("AccentBase", Destaque); S("Accent80", Destaque); S("Accent60", C("#2F8FCB")); S("Accent40", C("#1F6E9F")); S("Accent20", C("#164E72"));
            S("Highlight", Destaque);
        }

        // Acerta o formato da faixa do Fluent para o do Plant 3D (medido pixel a pixel nas capturas):
        //  - painel do conteudo encostado nas bordas e de cantos retos (o Fluent usa cantos de 8 px e margem de 8 px);
        //  - separador entre grupos: fio escuro #222933 de 2 px de alto a baixo;
        //  - icone da janela: o selo do aplicativo, como o "A P3D" no canto da barra de titulo.
        public static void AjustarFaixa(DependencyObject raiz, ImageSource selo)
        {
            void Ir(DependencyObject d)
            {
                if (d is System.Windows.Controls.Border b && b.CornerRadius.TopLeft >= 4 && b.ActualWidth > 800)
                {
                    b.CornerRadius = new CornerRadius(0); b.Margin = new Thickness(0); b.BorderThickness = new Thickness(0);
                }
                else if (d is System.Windows.Shapes.Rectangle r && r.Name == "separator")
                {
                    r.Fill = B(Abas); r.Width = 2; r.Height = double.NaN; r.VerticalAlignment = VerticalAlignment.Stretch; r.Margin = new Thickness(0);
                }
                else if (d is System.Windows.Controls.Image im && im.Name == "PART_Icon" && selo != null)
                {
                    im.Source = selo; im.Width = 46; im.Height = 22; im.Margin = new Thickness(6, 0, 4, 0); im.Stretch = Stretch.Uniform;
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Ir(VisualTreeHelper.GetChild(d, i));
            }
            Ir(raiz);
        }

        // Logo do Estructto (icone da barra de titulo e botao do aplicativo)
        public static ImageSource Selo()
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage(new System.Uri("pack://application:,,,/estructto.png"));
            bi.Freeze();
            return bi;
        }

        // Lista a arvore visual da faixa (diagnostico: --arvore=arquivo.txt)
        public static void DespejarArvore(DependencyObject raiz, string arquivo)
        {
            var sb = new System.Text.StringBuilder();
            void Ir(DependencyObject d, int n)
            {
                string extra = "";
                if (d is System.Windows.Controls.Border b) extra = " CR=" + b.CornerRadius + " BT=" + b.BorderThickness + " M=" + b.Margin + " P=" + b.Padding + " BG=" + b.Background + " BB=" + b.BorderBrush;
                if (d is System.Windows.Shapes.Rectangle r) extra = " W=" + r.Width + " Fill=" + r.Fill;
                if (d is FrameworkElement fe) extra += " [" + fe.Name + "] " + (int)fe.ActualWidth + "x" + (int)fe.ActualHeight;
                sb.AppendLine(new string(' ', n * 2) + d.GetType().Name + extra);
                if (n < 40) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Ir(VisualTreeHelper.GetChild(d, i), n + 1);
            }
            Ir(raiz, 0);
            System.IO.File.WriteAllText(arquivo, sb.ToString());
        }

        // Paletas (AvalonDock): fundo #3B4453, cabecalho #2E3440, abas de arquivo como no Plant 3D.
        public static void AplicarDock(DockingManager d)
        {
            var r = d.Resources;
            void S(object k, Color c) { r[k] = B(c); }
            S(AdKeys.Background, Abas); S(AdKeys.PanelBorderBrush, Abas); S(AdKeys.TabBackground, Abas);
            S(AdKeys.ToolWindowCaptionActiveBackground, Cabecalho); S(AdKeys.ToolWindowCaptionInactiveBackground, Cabecalho);
            S(AdKeys.ToolWindowCaptionActiveText, Texto); S(AdKeys.ToolWindowCaptionInactiveText, C("#D5DAE2"));
            S(AdKeys.ToolWindowCaptionActiveGrip, C("#7D8796")); S(AdKeys.ToolWindowCaptionInactiveGrip, C("#5E6878"));
            S(AdKeys.ToolWindowCaptionButtonActiveGlyph, Texto); S(AdKeys.ToolWindowCaptionButtonInactiveGlyph, TextoFraco);
            S(AdKeys.ToolWindowCaptionButtonActiveHoveredBackground, Hover); S(AdKeys.ToolWindowCaptionButtonInactiveHoveredBackground, Hover);
            S(AdKeys.ToolWindowTabSelectedActiveBackground, Painel); S(AdKeys.ToolWindowTabSelectedInactiveBackground, Painel);
            S(AdKeys.ToolWindowTabSelectedActiveText, Texto); S(AdKeys.ToolWindowTabSelectedInactiveText, Texto);
            S(AdKeys.ToolWindowTabUnselectedBackground, Cabecalho); S(AdKeys.ToolWindowTabUnselectedText, TextoFraco);
            S(AdKeys.ToolWindowTabUnselectedHoveredBackground, Hover); S(AdKeys.ToolWindowTabUnselectedHoveredText, Texto);
            S(AdKeys.DocumentWellTabSelectedActiveBackground, Painel); S(AdKeys.DocumentWellTabSelectedInactiveBackground, Painel);
            S(AdKeys.DocumentWellTabSelectedActiveText, Texto); S(AdKeys.DocumentWellTabSelectedInactiveText, Texto);
            S(AdKeys.DocumentWellTabUnselectedBackground, Abas); S(AdKeys.DocumentWellTabUnselectedText, TextoFraco);
            S(AdKeys.DocumentWellTabUnselectedHoveredBackground, Hover); S(AdKeys.DocumentWellTabUnselectedHoveredText, Texto);
            S(AdKeys.AutoHideTabDefaultBackground, Cabecalho); S(AdKeys.AutoHideTabDefaultText, Texto); S(AdKeys.AutoHideTabDefaultBorder, Abas);
            S(AdKeys.AutoHideTabHoveredBackground, Hover); S(AdKeys.AutoHideTabHoveredText, Texto);
            S(AdKeys.FloatingToolWindowBackground, Painel); S(AdKeys.FloatingToolWindowBorder, Abas);
            S(AdKeys.FloatingDocumentWindowBackground, Painel); S(AdKeys.FloatingDocumentWindowBorder, Abas);
            S(AdKeys.ControlAccentBrushKey, Destaque);
        }
    }
}
