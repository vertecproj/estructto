// Sistema de tokens visuais e tema escuro moderno do Estructto v2 (PESQUISA 4.1–4.3).
// Centraliza cores de superficie (0..3), bordas, tipografia, status, cores semanticas do canvas/diagramas
// e retinta Fluent.Ribbon + AvalonDock VS2013.
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AdKeys = AvalonDock.Themes.VS2013.Themes.ResourceKeys;

namespace NovoFtool
{
    public static class TemaPlant
    {
        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush B(string hex) { return B(C(hex)); }
        public static SolidColorBrush B(Color c, byte alpha) { return B(Color.FromArgb(alpha, c.R, c.G, c.B)); }
        public static int DwmColor(Color c) { return (c.B << 16) | (c.G << 8) | c.R; }

        // ---------- 4.1 Tokens de Superficie, Borda, Texto, Acento e Status ----------
        public static readonly Color
            BgCanvas       = C("#141820"),
            Surface0       = C("#1B202B"),   // Bg.Window / Surface.0 (barra de titulo, abas, rodape, fundo dock)
            Surface1       = C("#242B38"),   // Surface.1 (corpo da ribbon, paineis laterais, modais)
            Surface2       = C("#2D3646"),   // Surface.2 (cartoes, cabecalhos de secao, alternancia)
            Surface3       = C("#364154"),   // Surface.3 (inputs, botoes secundarios, hover)
            SurfaceAlt     = C("#283140"),   // linha alternada sutil em tabelas
            SurfaceHover   = C("#3E4B61"),   // realce suave de hover em controles
            SurfacePressed = C("#1E2530"),   // estado pressionado

            BordaSutil     = C("#333E50"),   // Border.Subtle (linhas de grade de tabela, divisores 1px)
            Borda          = C("#475569"),   // Border.Default (contorno de inputs, botoes, cartoes)
            BordaFoco      = C("#38BDF8"),   // Border.Focus (anel de foco, cota ativa, selecao)

            Texto1         = C("#F1F5F9"),   // Text.Primary
            Texto2         = C("#94A3B8"),   // Text.Secondary
            Texto3         = C("#7C8B9E"),   // Text.Tertiary (4.6:1, AA): metadados, legendas, dicas de rodape
            TextoMudo      = C("#64748B"),   // Text.Muted: so placeholders, atalhos e desabilitados
            TextoSobreAcento = C("#0F172A"), // texto sobre botao/indicador de acento

            Acento         = C("#0EA5E9"),   // Accent.Primary (botoes primarios, pilulas ativas)
            AcentoHover    = C("#38BDF8"),   // Accent.Hover (mais claro)
            AcentoPressed  = C("#0284C7"),   // Accent.Pressed (mais escuro)
            AcentoSutil    = C("#2638BDF8"), // Accent.Subtle (selecao, 15%)
            Realce         = C("#38BDF8"),   // Accent.Highlight (selecao no canvas, cotas clicaveis, icones ativos)
            RealceClaro    = C("#7DD3FC"),
            CorSelecao     = C("#1E3A5F"),   // fundo de item selecionado em listas/tabelas/arvore
            CorSelecaoHover = C("#254B7A"),
            CorSelecaoInat = C("#1E293B"),

            Sucesso        = C("#22C55E"),   // Status.Success
            SucessoClaro   = C("#86EFAC"),
            SucessoFundo   = C("#143828"),
            SucessoBorda   = C("#1E563B"),

            Alerta         = C("#F59E0B"),   // Status.Warning
            AlertaClaro    = C("#FDE68A"),
            AlertaFundo    = C("#3B2A14"),
            AlertaBorda    = C("#63451C"),

            Erro           = C("#F43F5E"),   // Status.Danger
            ErroClaro      = C("#FDA4AF"),
            ErroFundo      = C("#3F1A24"),
            ErroBorda      = C("#6E2536"),

            InfoFundo      = C("#172B42"),

            // projetista de ligações (LigacaoDesenho.cs): perfis neutros, chapas claras, solda quente
            LigPerfil3D    = C("#56657A"),
            LigChapa3D     = C("#C9D4E2"),
            LigSolda       = C("#F2B84B");

        // ---------- 4.2 Tokens Semanticos do Canvas e Diagramas ----------
        public static readonly Color
            CorGradeMaior      = C("#232A36"),
            CorGradeMenor      = C("#1A202A"),
            CorEixoX           = C("#B4F43F5E"),
            CorEixoY           = C("#B422C55E"),
            CorBarra           = C("#E2E8F0"),
            CorBarraSel        = C("#38BDF8"),
            CorBarraHover      = C("#7DD3FC"),
            CorBarraHalo       = C("#3338BDF8"),
            CorApoio           = C("#38BDF8"),
            CorCarga           = C("#F59E0B"),
            CorCargaArea       = C("#26F59E0B"),
            CorReacao          = C("#10B981"),
            CorDeformada       = C("#2DD4BF"),
            CorDeformadaEnv    = C("#94A3B8"),
            CorNormalPos       = C("#3B82F6"),
            CorNormalPosArea   = C("#403B82F6"),
            CorNormalNeg       = C("#F43F5E"),
            CorNormalNegArea   = C("#40F43F5E"),
            CorCortante        = C("#A855F7"),
            CorCortanteArea    = C("#33A855F7"),
            CorCortanteNeg     = C("#F472B6"),   // V negativo (rosa): distingue do positivo (roxo)
            CorMomentoNeg      = C("#FB923C"),   // M negativo (laranja): distingue do positivo (verde)
            CorCalor1          = C("#22C55E"),   // mapa de calor: ate 75%
            CorCalor2          = C("#38BDF8"),   // 75-90%
            CorCalor3          = C("#F59E0B"),   // 90-100%
            CorCalor4          = C("#F43F5E"),   // acima de 100%
            CorMomento         = C("#22C55E"),
            CorMomentoArea     = C("#3322C55E"),
            CorGuiaSnap        = C("#CC22C55E"),
            CorMarcaSnap       = C("#22C55E"),
            // snap e medir 3D (Snap3D.cs, SnapOverlay.cs, Medir3D.cs): amarelo do AutoSnap, ciano do rastreio, laranja/azul do Medir
            CorSnapAuto        = C("#FFD400"),
            CorSnapContorno    = C("#E00A0D12"),
            CorRastreio        = C("#00C8FF"),
            CorMedida          = C("#FF9F1C"),
            CorMedida2         = C("#2F7DF6"),
            CorMedidaArea      = C("#2ECC71"),
            CorJanelaSel       = C("#38BDF8"),
            CorJanelaSelArea   = C("#2838BDF8"),
            CorCruzSel         = C("#22C55E"),
            CorCruzSelArea     = C("#2822C55E"),
            CorFundoTextoCanvas = C("#CC141820"),
            CorCaixaHud        = C("#E6181C24"),   // Surface.Overlay (HUD sobre o canvas)
            CorFundoMedida     = C("#EB181C24"),
            CorSombraModal     = C("#A60A0D14"),
            // vista 3D "Realista" (Vista3D.cs): materiais, luzes, chão, sombra e arestas
            CorAco3D           = C("#A9B3BF"),   // aço: cinza metálico frio
            CorConcreto3D      = C("#CFC6B8"),   // concreto: cinza claro quente
            CorBrilhoAco3D     = C("#5C6672"),   // especular do aço (discreto)
            CorLuzChave3D      = C("#E2DDD3"),   // luz principal (sol), levemente quente
            CorLuzEnch3D       = C("#4A5262"),   // luz de enchimento, fria
            CorLuzAmb3D        = C("#5C6068"),   // luz ambiente
            CorChao3D          = C("#2A313C"),   // centro do chão (some no fundo do canvas)
            CorSombra3D        = C("#000000"),   // sombra no chão (alfa no pincel)
            CorAresta3D        = C("#0A0D12");   // contorno das peças

        // ---------- Aliases de compatibilidade (nomes ja usados no codigo) ----------
        public static readonly Color
            Titulo        = Surface0,
            Abas          = Surface0,
            Painel        = Surface1,
            Cabecalho     = Surface2,
            Campo         = Surface3,
            Texto         = Texto1,
            TextoFraco    = Texto2,
            Destaque      = Acento,
            Hover         = Surface3,
            ContornoHover = BordaFoco,
            Modelo        = BgCanvas;

        // ---------- Brushes Estaticos Congelados (UI + Canvas + Impressao) ----------
        public static readonly SolidColorBrush
            BBgCanvas       = B(BgCanvas),
            BSup0           = B(Surface0),
            BSup1           = B(Surface1),
            BSup2           = B(Surface2),
            BSup3           = B(Surface3),
            BSupAlt         = B(SurfaceAlt),
            BSupHover       = B(SurfaceHover),
            BSupPressed     = B(SurfacePressed),

            BBordaSutil     = B(BordaSutil),
            BBorda          = B(Borda),
            BBordaFoco      = B(BordaFoco),

            BTexto1         = B(Texto1),
            BTexto2         = B(Texto2),
            BTexto3         = B(Texto3),
            BTextoMudo      = B(TextoMudo),
            BTextoSobreAcento = B(TextoSobreAcento),

            BAcento         = B(Acento),
            BAcentoHover    = B(AcentoHover),
            BAcentoPressed  = B(AcentoPressed),
            BAcentoSutil    = B(AcentoSutil),
            BRealce         = B(Realce),
            BRealceClaro    = B(RealceClaro),
            BSelecao        = B(CorSelecao),
            BSelecaoHover   = B(CorSelecaoHover),
            BSelecaoInativa = B(CorSelecaoInat),

            BSucesso        = B(Sucesso),
            BSucessoClaro   = B(SucessoClaro),
            BSucessoFundo   = B(SucessoFundo),
            BSucessoBorda   = B(SucessoBorda),

            BAlerta         = B(Alerta),
            BAlertaClaro    = B(AlertaClaro),
            BAlertaFundo    = B(AlertaFundo),
            BAlertaBorda    = B(AlertaBorda),

            BErro           = B(Erro),
            BErroClaro      = B(ErroClaro),
            BErroFundo      = B(ErroFundo),
            BErroBorda      = B(ErroBorda),

            BInfoFundo      = B(InfoFundo),

            // Canvas e diagramas
            BGradeMaior       = B(CorGradeMaior),
            BGradeMenor       = B(CorGradeMenor),
            BEixoX            = B(CorEixoX),
            BEixoY            = B(CorEixoY),
            BBarra            = B(CorBarra),
            BBarraSel         = B(CorBarraSel),
            BBarraHover       = B(CorBarraHover),
            BBarraHalo        = B(CorBarraHalo),
            BApoio            = B(CorApoio),
            BCarga            = B(CorCarga),
            BCargaArea        = B(CorCargaArea),
            BReacao           = B(CorReacao),
            BDeformada        = B(CorDeformada),
            BDeformadaEnv     = B(CorDeformadaEnv),
            BNormalPos        = B(CorNormalPos),
            BNormalPosArea    = B(CorNormalPosArea),
            BNormalNeg        = B(CorNormalNeg),
            BNormalNegArea    = B(CorNormalNegArea),
            BCortante         = B(CorCortante),
            BCortanteArea     = B(CorCortanteArea),
            BMomento          = B(CorMomento),
            BMomentoArea      = B(CorMomentoArea),
            BCortanteNeg      = B(CorCortanteNeg),
            BMomentoNeg       = B(CorMomentoNeg),
            BGuiaSnap         = B(CorGuiaSnap),
            BMarcaSnap        = B(CorMarcaSnap),
            BSnapAuto         = B(CorSnapAuto),
            BRastreio         = B(CorRastreio),
            BMedida           = B(CorMedida),
            BMedida2          = B(CorMedida2),
            BJanelaSel        = B(CorJanelaSel),
            BJanelaSelArea    = B(CorJanelaSelArea),
            BCruzSel          = B(CorCruzSel),
            BCruzSelArea      = B(CorCruzSelArea),
            BFundoTextoCanvas = B(CorFundoTextoCanvas),
            BCaixaHud         = B(CorCaixaHud),
            BFundoMedida      = B(CorFundoMedida),
            BSombraModal      = B(CorSombraModal),

            // Placa de base / vista 2D e 3D
            BPlacaFundo       = B(Surface1),
            BPlacaHachura     = B(BordaSutil),
            BPlacaContorno    = B(CorBarra),
            BPlacaFace1       = B(Surface2),
            BPlacaFace2       = B(Surface1),
            BPlacaTopo        = B(Surface3),
            BPilar3DFace      = B("#960EA5E9"),
            BPilar3DTopo      = B("#C80284C7"),
            BPerfilArea       = B("#5A0EA5E9"),
            BLigPerfil        = B("#3394A3B8"),
            BLigSolda         = B(LigSolda),
            BCotaFundo        = B(InfoFundo),

            // Modo de impressao / Memorial A4 (fundo claro)
            BImpTinta         = B("#0F172A"),
            BImpCinza         = B("#475569"),
            BImpLinha         = B("#CBD5E1"),
            BImpCabeca        = B("#E2E8F0"),
            BImpFundoBloco    = B("#F8FAFC"),
            BImpAzulEsc       = B("#0369A1"),
            BImpVerde         = B("#16A34A"),
            BImpVermelho      = B("#E11D48"),
            BImpApoio         = B("#0284C7"),
            BImpCarga         = B("#D97706"),
            BImpDef           = B("#0284C7"),
            BImpFundoTexto    = B("#D8FFFFFF");

        // ---------- 4.3 Tipografia ----------
        static FontFamily fonteUI;
        static FontFamily fonteMono;

        public static FontFamily Fonte
        {
            get
            {
                if (fonteUI == null)
                    fonteUI = new FontFamily("Segoe UI Variable Text, Segoe UI Variable Display, Segoe UI");
                return fonteUI;
            }
        }

        public static FontFamily FonteMono
        {
            get
            {
                if (fonteMono == null)
                    fonteMono = new FontFamily("Cascadia Mono, JetBrains Mono, Consolas");
                return fonteMono;
            }
        }

        // Barra de titulo do Windows 11 na cor do tema (modo escuro + cor Surface.0 + texto Text.Primary).
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(System.IntPtr h, int attr, ref int val, int size);
        public static void TituloEscuro(Window w)
        {
            try
            {
                System.IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                int um = 1; DwmSetWindowAttribute(h, 20, ref um, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
                int cor = DwmColor(Surface0); DwmSetWindowAttribute(h, 35, ref cor, 4);   // DWMWA_CAPTION_COLOR
                int txt = DwmColor(Texto1); DwmSetWindowAttribute(h, 36, ref txt, 4);     // DWMWA_TEXT_COLOR
                int borda = DwmColor(BordaSutil); DwmSetWindowAttribute(h, 34, ref borda, 4); // DWMWA_BORDER_COLOR
            }
            catch { }
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

        // Registra TODOS os tokens como recursos do aplicativo (chaves do GUIA-DESIGN + aliases) para uso com DynamicResource.
        // Fonte unica das cores: este arquivo. App.xaml nao define cor nenhuma.
        public static void RegistrarTokens(Application app)
        {
            var r = app.Resources;
            r["Font.Micro"] = 10.0; r["Font.Caption"] = 11.0; r["Font.Body"] = 12.5; r["Font.Subtitle"] = 14.0; r["Font.Title"] = 18.0;
            r["Shadow.Float"] = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black, Direction = 270 };

            // Superficies
            r["Bg.Canvas"] = BBgCanvas;
            r["Bg.Window"] = BSup0; r["Surface.0"] = BSup0; r["Surface.1"] = BSup1; r["Surface.2"] = BSup2; r["Surface.3"] = BSup3;
            r["Surface.Alt"] = BSupAlt; r["Surface.RowAlt"] = BSupAlt; r["Surface.Hover"] = BSupHover; r["Surface.Pressed"] = BSupPressed;
            r["Surface.Overlay"] = BCaixaHud;
            r["Surface.CmdHist"] = B(Color.FromArgb(0x99, Surface0.R, Surface0.G, Surface0.B));
            r["Surface.CmdBox"]  = B(Color.FromArgb(0xE6, Surface1.R, Surface1.G, Surface1.B));
            r["Surface.Legend"]  = B(Color.FromArgb(0xB3, Surface1.R, Surface1.G, Surface1.B));
            r["Surface.WarnBanner"] = B(Color.FromArgb(0xE6, ErroFundo.R, ErroFundo.G, ErroFundo.B));
            // Bordas
            r["Border.Subtle"] = BBordaSutil; r["Border.Default"] = BBorda; r["Border.Strong"] = BBorda; r["Border.Focus"] = BBordaFoco;
            // Texto
            r["Text.Primary"] = BTexto1; r["Text.Secondary"] = BTexto2; r["Text.Tertiary"] = BTexto3; r["Text.Muted"] = BTextoMudo;
            r["Text.OnAccent"] = BTextoSobreAcento;
            // Acento
            r["Accent.Primary"] = BAcento; r["Accent.Hover"] = BAcentoHover; r["Accent.Pressed"] = BAcentoPressed;
            r["Accent.Highlight"] = BRealce; r["Accent.Subtle"] = BAcentoSutil; r["Accent.SubtleBg"] = BAcentoSutil;
            r["Selection.Bg"] = BSelecao; r["Selection.Hover"] = BSelecaoHover; r["Selection.InactiveBg"] = BSelecaoInativa;
            // Semanticas
            r["Status.Success"] = BSucesso; r["Status.SuccessBg"] = BSucessoFundo; r["Status.SuccessBorder"] = BSucessoBorda;
            r["Status.Warning"] = BAlerta;  r["Status.WarningBg"] = BAlertaFundo;  r["Status.WarningBorder"] = BAlertaBorda;
            r["Status.Danger"] = BErro;     r["Status.DangerBg"] = BErroFundo;     r["Status.DangerBorder"] = BErroBorda;
            r["Status.Ok"] = BSucesso; r["Status.OkBg"] = BSucessoFundo; r["Status.Warn"] = BAlerta; r["Status.WarnBg"] = BAlertaFundo;
            r["Status.Error"] = BErro; r["Status.ErrorBg"] = BErroFundo;
            // Canvas CAD
            r["Cad.GridMajor"] = BGradeMaior; r["Cad.GridMinor"] = BGradeMenor;
            r["Cad.Member"] = BBarra; r["Cad.Selected"] = BBarraSel; r["Cad.Halo"] = BBarraHalo; r["Cad.Hover"] = BBarraHover;
            r["Cad.Support"] = BApoio; r["Cad.Load"] = BCarga; r["Cad.LoadArea"] = BCargaArea; r["Cad.Reaction"] = BReacao; r["Cad.Deformed"] = BDeformada;
            r["Cad.Diagram.Tension"] = BNormalPos; r["Cad.Diagram.TensionArea"] = BNormalPosArea;
            r["Cad.Diagram.Compression"] = BNormalNeg; r["Cad.Diagram.CompressionArea"] = BNormalNegArea;
            r["Cad.Diagram.Shear"] = BCortante; r["Cad.Diagram.ShearArea"] = BCortanteArea;
            r["Cad.Diagram.Moment"] = BMomento; r["Cad.Diagram.MomentArea"] = BMomentoArea;
            // Aliases em portugues
            r["Barra"] = BSup0; r["Painel"] = BSup1; r["Cabecalho"] = BSup2; r["CampoFundo"] = BSup3;
            r["BordaSutil"] = BBordaSutil; r["Borda"] = BBorda; r["BordaFoco"] = BBordaFoco;
            r["Texto"] = BTexto1; r["TextoFraco"] = BTexto2; r["TextoMudo"] = BTextoMudo; r["Destaque"] = BAcento; r["Realce"] = BRealce;
            // Cores do sistema (selecao de listas/arvores/menus nativos e controles sem estilo proprio)
            r[SystemColors.HighlightBrushKey] = BSelecao;
            r[SystemColors.HighlightTextBrushKey] = BTexto1;
            r[SystemColors.InactiveSelectionHighlightBrushKey] = BSup2;
            r[SystemColors.InactiveSelectionHighlightTextBrushKey] = BTexto1;
            r[SystemColors.ControlTextBrushKey] = BTexto1;
            r[SystemColors.WindowTextBrushKey] = BTexto1;
            r[SystemColors.WindowBrushKey] = BSup1;
            r[SystemColors.ControlBrushKey] = BSup1;
            r[SystemColors.ControlLightBrushKey] = BSup2;
            r[SystemColors.ControlDarkBrushKey] = BBorda;
            r[SystemColors.GrayTextBrushKey] = BTextoMudo;
            r[SystemColors.MenuBrushKey] = BSup2;
            r[SystemColors.MenuTextBrushKey] = BTexto1;
        }

        // Registra os tokens e retinta o Fluent.Ribbon (chamar DEPOIS de ThemeManager.ChangeTheme).
        public static void AplicarFluent(Application app)
        {
            ConfigurarDicas();
            RegistrarTokens(app);
            var r = app.Resources;

            // Chaves do Fluent.Ribbon
            void S(string k, SolidColorBrush b) { r["Fluent.Ribbon.Brushes." + k] = b; }
            S("RibbonWindow.TitleBackground", BSup0);
            S("RibbonWindow.TitleForeground", BTexto2);
            S("RibbonWindow.Background", BSup0);
            S("WindowCommands.CaptionButton.Background", BSup0);
            S("WindowCommands.CaptionButton.Foreground", BTexto1);
            S("WindowCommands.CaptionButton.MouseOver.Background", BSup3);
            S("Ribbon.Background", BSup0);
            S("RibbonTabControl.Background", BSup0);
            S("RibbonTabControl.TabsGrid.Background", BSup0);
            S("RibbonTabControl.TabsGrid.Foreground", BTexto1);
            S("RibbonTabControl.Foreground", BTexto1);
            S("RibbonTabControl.Content.Background", BSup1);
            S("RibbonTabControl.Content.Border", BBordaSutil);
            S("RibbonTabControl.Content.Foreground", BTexto1);
            S("RibbonTabItem.Active.Background", BSup1);
            S("RibbonTabItem.Border", BRealce);
            S("RibbonTabItem.Foreground", BTexto2);
            S("RibbonTabItem.Selected.Foreground", BTexto1);
            S("RibbonTabItem.MouseOver.Background", BSup2);
            S("RibbonTabItem.MouseOver.Foreground", BTexto1);
            S("RibbonTabItem.Selected.MouseOver.Foreground", BTexto1);
            S("RibbonGroupBox.Header.Foreground", BTexto2);
            S("GroupSeparator.Background", BBordaSutil);
            S("LabelText", BTexto1);
            S("IdealForeground", BTexto1);
            S("Button.MouseOver.Background", BSup3);
            S("Button.MouseOver.Border", BBorda);
            S("Button.Pressed.Background", BSup2);
            S("Button.Pressed.Border", BBordaFoco);
            S("ToggleButton.Checked.Background", BSelecao);
            S("ToggleButton.Checked.Border", BBordaFoco);
            S("ToggleButton.CheckedMouseOver.Background", BSelecaoHover);
            S("ToggleButton.CheckedMouseOver.Border", BBordaFoco);
            S("TextBox.Background", BSup3);
            S("TextBox.Border", BBorda);
            S("TextBox.Caret", BRealce);
            S("TextBox.MouseOver.Background", BSup3);
            S("TextBox.MouseOver.Border", BTexto2);
            S("TextBox.Focus.Background", BSup0);
            S("TextBox.Focus.Border", BBordaFoco);
            S("TextBox.Selection", BAcento);
            S("DropDown.Background", BSup2);
            S("DropDown.Border", BBorda);
            S("MenuItem.Background", BSup2);
            S("ScreenTip.Background", BSup0);
            S("ScreenTip.Border", BBorda);
            S("Backstage.Background", BSup1);
            S("Backstage.Foreground", BTexto1);
            S("BackstageTabControl.Background", BSup1);
            S("BackstageTabControl.ItemsPanelBackground", BSup0);
            S("BackstageTabItem.Header.Foreground", BTexto1);
            S("BackstageTabItem.Selected.Background", BAcento);
            S("BackstageTabItem.MouseOver.Background", BSup2);
            S("BackstageTabControl.Button.MouseOver.Background", BSup2);
            S("Backstage.BackButton.Background", BSup0);
            S("Backstage.BackButton.Foreground", BTexto1);
            S("KeyTip.Background", BSup2);
            S("KeyTip.Border", BBordaFoco);
            S("KeyTip.Foreground", BTexto1);
            S("Separator.Background", BBordaSutil);
            S("Separator.Border", BBordaSutil);
            S("AccentBase", BAcento);
            S("Accent80", BRealce);
            S("Accent60", BAcento);
            S("Accent40", BAcentoHover);
            S("Accent20", BAcentoPressed);
            S("Highlight", BRealce);
        }

        // Acerta o formato da faixa do Fluent:
        //  - painel do conteudo encostado nas bordas e de cantos retos;
        //  - separador entre grupos: fio discreto BordaSutil de 1 px de alto a baixo;
        //  - icone da janela: o selo do aplicativo.
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
                    r.Fill = BBordaSutil; r.Width = 1; r.Height = double.NaN; r.VerticalAlignment = VerticalAlignment.Stretch; r.Margin = new Thickness(0, 4, 0, 4);
                }
                else if (d is System.Windows.Controls.Border cb && cb.Name == "border" && VisualTreeHelper.GetParent(d) is System.Windows.Controls.Primitives.ToggleButton ptb && ptb.Name == "PART_ToggleButton")
                {
                    cb.Background = BSup3; cb.BorderBrush = BBorda;   // caixa combinada da faixa: sem o fundo preto do Fluent
                }
                else if (d is System.Windows.Controls.Image im && im.Name == "PART_Icon" && selo != null)
                {
                    im.Source = selo; im.Width = 46; im.Height = 24; im.Margin = new Thickness(6, 0, 4, 0); im.Stretch = Stretch.Uniform;
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Ir(VisualTreeHelper.GetChild(d, i));
            }
            Ir(raiz);
        }

        // Logo do Estructto (icone da barra de titulo e botao do aplicativo)
        // px > 0: quadro do estructto.ico com esse tamanho exato (16/24/32/48/64/128/256; os pequenos sao desenhados a mao), sem reescalar.
        public static ImageSource Selo(int px = 0)
        {
            if (px > 0)
            {
                var dec = new System.Windows.Media.Imaging.IconBitmapDecoder(new System.Uri("pack://application:,,,/estructto.ico"),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var q = dec.Frames.Where(f => f.PixelWidth >= px).OrderBy(f => f.PixelWidth).FirstOrDefault() ?? dec.Frames.OrderBy(f => f.PixelWidth).Last();
                q.Freeze();
                return q;
            }
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

        // Paletas (AvalonDock): retintado com os tokens Surface.0..3, Border.Subtle e Accent.Primary.
        public static void AplicarDock(DockingManager d)
        {
            var r = d.Resources;
            void S(object k, SolidColorBrush b) { r[k] = b; }
            S(AdKeys.Background, BSup0);
            S(AdKeys.PanelBorderBrush, BBordaSutil);
            S(AdKeys.TabBackground, BSup0);
            S(AdKeys.ToolWindowCaptionActiveBackground, BSup2);
            S(AdKeys.ToolWindowCaptionInactiveBackground, BSup0);
            S(AdKeys.ToolWindowCaptionActiveText, BTexto1);
            S(AdKeys.ToolWindowCaptionInactiveText, BTexto2);
            S(AdKeys.ToolWindowCaptionActiveGrip, BRealce);
            S(AdKeys.ToolWindowCaptionInactiveGrip, BBorda);
            S(AdKeys.ToolWindowCaptionButtonActiveGlyph, BTexto1);
            S(AdKeys.ToolWindowCaptionButtonInactiveGlyph, BTexto2);
            S(AdKeys.ToolWindowCaptionButtonActiveHoveredBackground, BSup3);
            S(AdKeys.ToolWindowCaptionButtonInactiveHoveredBackground, BSup3);
            S(AdKeys.ToolWindowTabSelectedActiveBackground, BSup1);
            S(AdKeys.ToolWindowTabSelectedInactiveBackground, BSup1);
            S(AdKeys.ToolWindowTabSelectedActiveText, BTexto1);
            S(AdKeys.ToolWindowTabSelectedInactiveText, BTexto1);
            S(AdKeys.ToolWindowTabUnselectedBackground, BSup0);
            S(AdKeys.ToolWindowTabUnselectedText, BTexto2);
            S(AdKeys.ToolWindowTabUnselectedHoveredBackground, BSup2);
            S(AdKeys.ToolWindowTabUnselectedHoveredText, BTexto1);
            S(AdKeys.DocumentWellTabSelectedActiveBackground, BSup1);
            S(AdKeys.DocumentWellTabSelectedInactiveBackground, BSup1);
            S(AdKeys.DocumentWellTabSelectedActiveText, BTexto1);
            S(AdKeys.DocumentWellTabSelectedInactiveText, BTexto1);
            S(AdKeys.DocumentWellTabUnselectedBackground, BSup0);
            S(AdKeys.DocumentWellTabUnselectedText, BTexto2);
            S(AdKeys.DocumentWellTabUnselectedHoveredBackground, BSup2);
            S(AdKeys.DocumentWellTabUnselectedHoveredText, BTexto1);
            S(AdKeys.AutoHideTabDefaultBackground, BSup2);
            S(AdKeys.AutoHideTabDefaultText, BTexto1);
            S(AdKeys.AutoHideTabDefaultBorder, BBordaSutil);
            S(AdKeys.AutoHideTabHoveredBackground, BSup3);
            S(AdKeys.AutoHideTabHoveredText, BTexto1);
            S(AdKeys.FloatingToolWindowBackground, BSup1);
            S(AdKeys.FloatingToolWindowBorder, BBorda);
            S(AdKeys.FloatingDocumentWindowBackground, BSup1);
            S(AdKeys.FloatingDocumentWindowBorder, BBorda);
            S(AdKeys.ControlAccentBrushKey, BAcento);
        }
    }
}
