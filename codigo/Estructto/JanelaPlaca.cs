// Janela da placa de base (E5): veredito numa linha no topo + 3 chips (Fixação / Chapa / Solda),
// desenho à esquerda (Planta | 3D | Forças | Ficha | Cálculo) e, à direita, 3 passos numerados com o essencial visível
// e o resto em "mais". Rodapé: Dimensionar tudo, Aplicar também em…, Cancelar / Aplicar.
// O cálculo é sempre o do motor (PlacaCalc.Calcular, CandidatoChumbador, Solda.Calcular/Diagnosticar).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Portico;
using static NovoFtool.UiPlaca;

namespace NovoFtool
{
    public class JanelaPlaca : JanelaEscura
    {
        public bool Mudou;
        readonly Modelo m; readonly No n;
        readonly PlacaBase original, edit, refInicial;
        readonly PlacaEdit pe;
        readonly PlanoPlaca plano = new PlanoPlaca();
        readonly Placa3D vista3d = new Placa3D();
        readonly Border cabecalho = new Border();
        readonly Border areaAba = new Border();
        readonly Border hostSeg = new Border();
        readonly TextBlock lblDicaVista = new TextBlock();
        readonly StackPanel colDir = new StackPanel();
        readonly ScrollViewer svDir = new ScrollViewer();
        readonly DockPanel rodape = new DockPanel();
        readonly Grid camadaRaiz = new Grid();
        readonly Grid overlayTrocar = new Grid { Visibility = Visibility.Collapsed };
        readonly Grid overlayAplicarEm = new Grid { Visibility = Visibility.Collapsed };
        readonly Grid overlaySugerir = new Grid { Visibility = Visibility.Collapsed };
        readonly Canvas canvasSugerir = new Canvas();
        readonly StackPanel listaTrocar = new StackPanel();
        readonly StackPanel listaAplicarEm = new StackPanel();
        readonly TextBlock lblAplicarEm = new TextBlock();

        // Outras fixações: quem abre a janela informa os nós possíveis e como avaliar uma placa num nó (pior combinação)
        public Func<No, PlacaBase, ResPlaca> AvaliarEm;

        // Envoltória (várias combinações e as posições 0° / 180° da carga giratória): devolve o resultado da placa em edição na
        // combinação que maximiza a nota dada, e o nome dela. Cada item (fixação, chapa, solda) é verificado na SUA pior
        // combinação, recalculada a cada mudança: trocar o chumbador pode mudar qual combinação manda. null = um resultado só.
        public Func<Func<ResPlaca, double>, (ResPlaca r, string nome)> PiorCombinacao;
        public string TituloBase;

        (ResPlaca r, string nome) Pior(Func<ResPlaca, double> nota) { return PiorCombinacao != null ? PiorCombinacao(nota) : (PlacaCalc.Calcular(m, n), null); }
        static double NotaFix(ResPlaca t) { return t.TemEsforcos ? t.ApFix : -1; }
        static double NotaChapa(ResPlaca t) { return t.TemEsforcos ? t.EspNec : -1; }
        double NotaSolda(ResPlaca t)
        {
            if (!t.TemEsforcos || t.Sec == null) return -1;
            var s = Solda.Calcular(t.Sec, t.Deitado, t.NBarra, t.VBarra, t.MBarra, edit.Solda > 0 ? edit.Solda : 5, edit.Eletrodo, edit.Esp);
            return s.Erro == "" ? s.Tau : -1;
        }
        ResPlaca CalcFix() { return Pior(NotaFix).r; }
        ResPlaca CalcSd() { return Pior(NotaSolda).r; }
        // fixação na pior combinação dela, com os números da chapa da pior combinação da chapa
        ResPlaca CalcEnv() { var a = CalcFix(); CopiarChapa(Pior(NotaChapa).r, a); return a; }
        internal static void CopiarChapa(ResPlaca de, ResPlaca para)
        {
            para.EspNec = de.EspNec; para.EspNecT = de.EspNecT; para.EspNecC = de.EspNecC; para.CConc = de.CConc; para.BracoC = de.BracoC;
            para.TLinha = de.TLinha; para.Braco = de.Braco; para.LargEf = de.LargEf;
        }
        void AtualizarTitulo(string cFix, string cCh, string cSd)
        {
            if (TituloBase == null) TituloBase = Title;
            if (cFix == null) { Title = TituloBase; return; }
            if (cFix == cCh && cCh == cSd) Title = TituloBase + " — combinação crítica: " + cFix;
            else Title = TituloBase + " — críticas: fixação " + cFix + " · chapa " + cCh + " · solda " + cSd;
        }
        public List<No> Candidatos = new List<No>();
        public readonly List<No> AplicarEm = new List<No>();   // nós que recebem a mesma placa ao Aplicar
        readonly HashSet<int> marcados = new HashSet<int>();

        int abaAtiva;                  // 0 = Planta, 1 = 3D, 2 = Forças, 3 = Ficha, 4 = Cálculo
        bool prefFuroMenor;            // false = Mais leve, true = Furo menor
        bool mostrarOutroArranjo;      // campos para digitar Fileiras × Por fileira
        bool maisFix, maisCh, maisSd;  // "mais" aberto em cada passo
        bool semSobreporCones;
        string filtroBuscaCh = "";
        int filtroTipoCh;              // 0 = Todos, 1 = Mecânico, 2 = Químico
        readonly HashSet<string> famAbertas = new HashSet<string>();   // famílias abertas no painel Trocar (fabricante|linha)
        string chumbAntes = "";
        string msgDim = "";
        StackPanel btsFiltroTipoCh;
        Border cardFix, cardCh, cardSd, btSugerir;
        readonly DispatcherTimer tmRealce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        Border realcado;

        const double LargDir = 440;    // coluna dos 3 passos
        const double LargRot = 66;     // coluna dos rótulos dentro dos passos (alinha os campos dos 3 passos)

        // Solda do pilar na placa com os esforços do pé do pilar; perna digitada ou, com 0, a mínima da tabela.
        ResSolda SoldaPilar(ResPlaca r)
        {
            if (!r.TemEsforcos || r.Sec == null) return null;
            double h = edit.Solda > 0 ? edit.Solda : Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, edit.Eletrodo, edit.Esp);
            if (h <= 0) h = 25;
            return Solda.Calcular(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, h, edit.Eletrodo, edit.Esp);
        }

        static string NomeCurtoCh(Chumbador c)
        {
            if (c == null) return "—";
            return c.Linha.Replace("Walsywa ", "") + " " + c.Pol + " hef " + c.Hef.ToString("0");
        }

        static string FmtEsp(double e)
        {
            if (Math.Abs(e - Math.Round(e)) < 1e-6) return Num.Fmt(e, 0);
            if (Math.Abs(e * 10 - Math.Round(e * 10)) < 1e-6) return Num.Fmt(e, 1);
            return Num.Fmt(e, 2);
        }

        static string FmtN(double v, int c = 2) { return double.IsInfinity(v) ? "∞" : Num.Fmt(v, c); }

        string DescFixAtual(ResPlaca r)
        {
            return r.Ch != null ? r.Paraf.Count + " × " + NomeCurtoCh(r.Ch) : r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe;
        }

        // Dimensiona segundo a preferência ativa ("Mais leve" ou "Furo menor").
        void Dimensionar()
        {
            ResPlaca r = CalcFix();
            if (!r.TemEsforcos) return;
            bool achou = false;
            if (r.Ch != null || !string.IsNullOrEmpty(edit.Chumbador))
            {
                if (!prefFuroMenor)
                {
                    string esc = PlacaCalc.DimensionarChumbador(m, n);
                    achou = esc != "";
                    msgDim = achou ? "Escolhido automaticamente: " + esc + "." : "Nenhum chumbador do catálogo passa com até 8 chumbadores nesta placa: aumente a placa (sobra) ou use chumbador químico/concretado.";
                }
                else
                {
                    string ch0 = edit.Chumbador; int f0 = edit.Fileiras, q0 = edit.PorFileira;
                    var cands = Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca).ThenBy(c => c.Hef).ToList();
                    foreach (Chumbador c in cands)
                    {
                        foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                        {
                            if (arr[0] * arr[1] < f0 * q0) continue;
                            edit.Chumbador = c.Nome; edit.Fileiras = Math.Max(arr[0], f0); edit.PorFileira = Math.Max(arr[1], q0);
                            ResPlaca t = CalcFix();
                            if (t.Erro == "" && t.TemEsforcos && t.ChOk)
                            {
                                achou = true;
                                msgDim = "Escolhido automaticamente (furo menor): " + t.Paraf.Count + " × " + c.Nome + " (" + c.Fabricante + ").";
                                break;
                            }
                        }
                        if (achou) break;
                    }
                    if (!achou) { edit.Chumbador = ch0; edit.Fileiras = f0; edit.PorFileira = q0; msgDim = "Nenhum chumbador do catálogo passa nesta placa."; }
                }
            }
            else
            {
                int ic0 = Math.Max(0, Array.IndexOf(Parafusos.Classes, edit.Classe));
                if (!prefFuroMenor)
                {
                    for (int ic = ic0; ic < Parafusos.Classes.Length && !achou; ic++)
                        for (int id = 0; id < Parafusos.D.Length && !achou; id++)
                        {
                            edit.Classe = Parafusos.Classes[ic]; edit.Diametro = Parafusos.D[id];
                            ResPlaca t = CalcFix();
                            if (t.Erro == "" && t.ChOk) achou = true;
                        }
                }
                else
                {
                    for (int id = 0; id < Parafusos.D.Length && !achou; id++)
                        for (int ic = ic0; ic < Parafusos.Classes.Length && !achou; ic++)
                        {
                            edit.Classe = Parafusos.Classes[ic]; edit.Diametro = Parafusos.D[id];
                            ResPlaca t = CalcFix();
                            if (t.Erro == "" && t.ChOk) achou = true;
                        }
                }
                msgDim = achou ? "Escolhido automaticamente: " + edit.Fileiras * edit.PorFileira + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe + "." : "Nenhum parafuso da tabela passa nesta placa.";
            }
            r = CalcEnv();
            double ch = PlacaCalc.Chapas.FirstOrDefault(e => e >= r.EspNec - 1e-9);
            if (ch > 0) edit.Esp = ch;
            var rs0 = CalcSd();   // solda pela pior combinação dela
            if (rs0.Sec != null) { double h = Solda.Minima(rs0.Sec, rs0.Deitado, rs0.NBarra, rs0.VBarra, rs0.MBarra, edit.Eletrodo, edit.Esp); edit.Solda = h > 0 ? h : 25; }
        }

        public JanelaPlaca(Modelo m, No n)
        {
            this.m = m; this.n = n;
            original = n.Placa;
            edit = n.Placa != null ? n.Placa.Copiar() : new PlacaBase { PeloPerfil = true, Sobra = 100, H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", FyChapa = 250 };
            n.Placa = edit;
            pe = new PlacaEdit(edit);
            chumbAntes = original != null && !string.IsNullOrEmpty(original.Chumbador) ? original.Chumbador : "";
            if (original == null) Dimensionar();
            refInicial = (original ?? edit).Copiar();

            Title = "Placa de base — nó " + n.Id;
            Rect wa = SystemParameters.WorkArea;
            Width = Math.Max(1100, Math.Min(1440, wa.Width - 60));
            Height = Math.Max(680, Math.Min(900, wa.Height - 40));
            MinWidth = 1024; MinHeight = 640;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            plano.AoEditar = Recalcular;
            tmRealce.Tick += (s, e) => { tmRealce.Stop(); if (realcado != null) { realcado.BorderBrush = TemaPlant.BBordaSutil; realcado.BorderThickness = new Thickness(1); realcado = null; } };

            var raiz = new Grid();
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1) Topo: veredito + chips
            cabecalho.Background = TemaPlant.BSup1;
            cabecalho.BorderBrush = TemaPlant.BBordaSutil;
            cabecalho.BorderThickness = new Thickness(0, 0, 0, 1);
            cabecalho.Padding = new Thickness(16, 7, 16, 7);
            raiz.Children.Add(cabecalho);

            // 2) Corpo: desenho (esquerda) + passos (direita)
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LargDir) });
            Grid.SetRow(corpo, 1);

            var cardDesenho = new Border
            {
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Margin = new Thickness(12, 10, 10, 10), ClipToBounds = true
            };
            var dpDes = new DockPanel();
            var topoDes = new DockPanel { Margin = new Thickness(10, 8, 12, 8) };
            lblDicaVista.FontSize = 11.5; lblDicaVista.Foreground = TemaPlant.BTexto3; lblDicaVista.VerticalAlignment = VerticalAlignment.Center;
            lblDicaVista.TextTrimming = TextTrimming.CharacterEllipsis; lblDicaVista.TextAlignment = TextAlignment.Right; lblDicaVista.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(hostSeg, Dock.Left);
            topoDes.Children.Add(hostSeg);
            topoDes.Children.Add(lblDicaVista);
            DockPanel.SetDock(topoDes, Dock.Top);
            dpDes.Children.Add(topoDes);
            areaAba.BorderBrush = TemaPlant.BBordaSutil; areaAba.BorderThickness = new Thickness(0, 1, 0, 0);
            dpDes.Children.Add(areaAba);
            cardDesenho.Child = dpDes;
            corpo.Children.Add(cardDesenho);

            var dirBorder = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0) };
            svDir.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            svDir.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            svDir.Padding = new Thickness(10, 8, 10, 8);
            svDir.Content = colDir;
            dirBorder.Child = svDir;
            Grid.SetColumn(dirBorder, 1);
            corpo.Children.Add(dirBorder);
            raiz.Children.Add(corpo);

            // 3) Rodapé
            var rod = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 7, 14, 7), Child = rodape };
            Grid.SetRow(rod, 2);
            raiz.Children.Add(rod);

            camadaRaiz.Children.Add(raiz);
            MontarOverlayTrocar(); camadaRaiz.Children.Add(overlayTrocar);
            MontarOverlayAplicarEm(); camadaRaiz.Children.Add(overlayAplicarEm);
            var fundoSug = new Border { Background = Brushes.Transparent };
            fundoSug.MouseLeftButtonDown += (s, e) => { overlaySugerir.Visibility = Visibility.Collapsed; e.Handled = true; };
            overlaySugerir.Children.Add(fundoSug);
            overlaySugerir.Children.Add(canvasSugerir);
            camadaRaiz.Children.Add(overlaySugerir);
            Content = camadaRaiz;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (overlayTrocar.Visibility == Visibility.Visible || overlayAplicarEm.Visibility == Visibility.Visible || overlaySugerir.Visibility == Visibility.Visible)
                {
                    FecharFlutuantes();
                    e.Handled = true;
                }
            };
            Closing += (s, e) => { if (!Mudou) n.Placa = original; };
            // Candidatos/AvaliarEm/PiorCombinacao chegam depois do construtor: placa nova é redimensionada já pela envoltória
            Loaded += (s, e) => { if (original == null && PiorCombinacao != null) Dimensionar(); Recalcular(); };
            Recalcular();
        }

        // ================= API usada pelo modo --capturas =================
        public void MostrarAba(int aba) { abaAtiva = Math.Max(0, Math.Min(4, aba)); FecharFlutuantes(); Recalcular(); }
        public void AbrirTrocar() { AbrirOverlayTrocar(); }
        public void FecharTrocar() { overlayTrocar.Visibility = Visibility.Collapsed; }
        public void AbrirSugerir() { MostrarSugerir(); }
        public void AbrirAplicarEm() { MostrarAplicarEm(); }
        public void FecharFlutuantes()
        {
            overlayTrocar.Visibility = Visibility.Collapsed;
            overlayAplicarEm.Visibility = Visibility.Collapsed;
            overlaySugerir.Visibility = Visibility.Collapsed;
        }
        public void UsarModoParafuso(bool parafuso)
        {
            FecharFlutuantes();
            msgDim = "";
            if (parafuso) edit.Chumbador = null;
            else if (string.IsNullOrEmpty(edit.Chumbador))
            {
                PlacaCalc.DimensionarChumbador(m, n);
                if (string.IsNullOrEmpty(edit.Chumbador))
                {
                    var perto = Chumbadores.Lista.OrderBy(x => Math.Abs(x.Rosca - edit.Diametro)).ThenBy(x => x.Hef).FirstOrDefault();
                    if (perto != null) edit.Chumbador = perto.Nome;
                }
            }
            Recalcular();
        }

        // ================= recálculo e montagem =================
        void Recalcular()
        {
            plano.FecharEditor(false);
            var pf = Pior(NotaFix); var pc = Pior(NotaChapa); var ps = Pior(NotaSolda);
            ResPlaca r = pf.r, rCh = pc.r, rSd = ps.r;
            CopiarChapa(rCh, r);   // cabeçalho, desenho e cálculo: fixação na pior dela + chapa na pior dela
            var sd = SoldaPilar(rSd);
            AtualizarTitulo(pf.nome, pc.nome, ps.nome);
            plano.R = r; plano.P = edit;
            vista3d.R = r; vista3d.P = edit; vista3d.Sd = sd;

            MontarCabecalho(r, sd);
            MontarVista(r, sd);

            colDir.Children.Clear();
            cardFix = MontarPassoFixacao(r);
            cardCh = MontarPassoChapa(rCh);
            cardSd = MontarPassoSolda(rSd, sd);
            colDir.Children.Add(cardFix);
            colDir.Children.Add(cardCh);
            colDir.Children.Add(cardSd);
            MontarMudancas(r);

            MontarRodape(r);
            if (overlayTrocar.Visibility == Visibility.Visible) PreencherOverlayTrocar();
            if (overlayAplicarEm.Visibility == Visibility.Visible) PreencherAplicarEm();
        }

        bool FixOk(ResPlaca r) { return r.TemEsforcos && r.ChOk; }
        bool ChapaOk(ResPlaca r) { return r.EspNec <= edit.Esp + 1e-9; }
        double ApChapa(ResPlaca r) { return edit.Esp > 0 ? r.EspNec / edit.Esp : 0; }

        // ---------- 1) topo: veredito numa linha + chips dos passos + avisos compactos ----------
        void MontarCabecalho(ResPlaca r, ResSolda sd)
        {
            var dp = new DockPanel { LastChildFill = true };
            bool sdOk = sd == null || sd.Passa;
            var falhas = new List<string>();
            if (r.TemEsforcos)
            {
                bool capOk = r.NComb >= (r.Ch != null ? r.NRef : r.NExig) && (r.Ch == null || !r.Ch.Quimico || r.NAco >= r.NSeg);
                if (!capOk) falhas.Add(r.Ch != null ? "chumbador insuficiente" : "parafuso insuficiente");
                if (!r.EspacOk) falhas.Add("chumbadores muito próximos");
                if (!ChapaOk(r)) falhas.Add("chapa fina demais");
                if (!sdOk) falhas.Add("solda não passa");
            }

            // chips dos 3 passos (direita)
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (r.TemEsforcos)
            {
                chips.Children.Add(ChipPasso(1, "Fixação", r.ApFix, FixOk(r), () => IrPara(1)));
                chips.Children.Add(ChipPasso(2, "Chapa", ApChapa(r), ChapaOk(r), () => IrPara(2)));
                chips.Children.Add(ChipPasso(3, "Solda", sd != null ? sd.Ap : 0, sdOk, () => IrPara(3)));
            }
            DockPanel.SetDock(chips, Dock.Right);
            dp.Children.Add(chips);

            Nivel nv; string titulo, glifo;
            if (!r.TemEsforcos) { nv = Nivel.Neutro; titulo = "Sem verificação"; glifo = "–"; }
            else if (falhas.Count > 0) { nv = Nivel.Erro; titulo = "Não passa — " + string.Join(", ", falhas); glifo = "✕"; }
            else if (r.Inclinada) { nv = Nivel.Alerta; titulo = "Placa resolvida — falta detalhar a ligação inclinada"; glifo = "!"; }
            else
            {
                double apMax = Math.Max(r.ApFix, Math.Max(ApChapa(r), sd != null ? sd.Ap : 0));
                nv = Nivel.Ok; titulo = "Passa · usa " + Pct(apMax) + " no item mais carregado"; glifo = "✓";
            }
            var cores = Cores(nv);
            var icone = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = cores.bg, BorderBrush = cores.bd, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = glifo, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = cores.fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            DockPanel.SetDock(icone, Dock.Left);
            dp.Children.Add(icone);

            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var lin1 = new WrapPanel { Orientation = Orientation.Horizontal };
            var tTit = Tx(titulo, 15, nv == Nivel.Erro ? TemaPlant.BTexto1 : nv == Nivel.Neutro ? TemaPlant.BTexto1 : cores.fg, FontWeights.SemiBold);
            tTit.Margin = new Thickness(0, 0, 10, 0);
            lin1.Children.Add(tTit);
            foreach (var av in AvisosCompactos(r))
            {
                av.Margin = new Thickness(0, 1, 6, 1);
                lin1.Children.Add(av);
            }
            txt.Children.Add(lin1);
            string sub;
            if (!r.TemEsforcos) sub = r.Erro;
            else
                sub = Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm · " + DescFixAtual(r) + (r.Sec != null ? " · pilar " + r.Sec.Nome.Replace("x", "×") : "") +
                      "      N = " + Num.Fmt(r.N, 2) + " kN   V = " + Num.Fmt(r.V, 2) + " kN   M = " + Num.Fmt(r.M, 2) + " kN·m";
            var tSub = Tx(sub, 11.5, TemaPlant.BTexto2);
            tSub.TextTrimming = TextTrimming.CharacterEllipsis;
            tSub.Margin = new Thickness(0, 2, 0, 0);
            Dica(tSub, sub + (r.TemEsforcos ? "\nN, V e M no pé do pilar, nos eixos da placa (N > 0 = tração nas fixações)." : ""));
            txt.Children.Add(tSub);
            dp.Children.Add(txt);
            cabecalho.Child = dp;
        }

        Border ChipPasso(int num, string nome, double ap, bool ok, Action clique)
        {
            var nv = NivelAp(ap, ok);
            var c = Cores(nv);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Tx(num + "  " + nome, 12, TemaPlant.BTexto1, FontWeights.SemiBold));
            var pct = Tx("  " + Pct(ap), 12, c.fg, FontWeights.Bold);
            Typography.SetNumeralAlignment(pct, FontNumeralAlignment.Tabular);
            sp.Children.Add(pct);
            sp.Children.Add(Tx(ok && ap <= 1 + 1e-9 ? "  ✓" : "  ✕", 12, c.fg, FontWeights.Bold));
            var b = new Border
            {
                Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, Child = sp
            };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; clique(); };
            return Dica(b, nome + ": " + Pct(ap) + " da capacidade" + (ok ? "" : " — não passa") + ". Clique para ir ao passo " + num + ".");
        }

        List<Border> AvisosCompactos(ResPlaca r)
        {
            var l = new List<Border>();
            if (!r.TemEsforcos) return l;
            if (r.Inclinada)
            {
                string t = "Barra inclinada " + Num.Fmt(r.AngNormal, 1) + "° em relação à normal da placa: as fixações foram calculadas com a reação do nó nos eixos da placa, mas a chapa e a solda " +
                           "(modelo do perfil perpendicular à placa) não valem para barra inclinada. Detalhe a ligação (gusset/cantoneira) à parte.";
                l.Add(Chip("⚠ Ligação inclinada " + Num.Fmt(r.AngNormal, 0) + "°: detalhar", Nivel.Alerta, t, () => { abaAtiva = 1; Recalcular(); }));
            }
            if (r.Ch != null && !string.IsNullOrEmpty(r.AvisoCoef))
            {
                bool risco = r.AvisoCoef.StartsWith("Grau de risco");
                l.Add(Chip(risco ? "⚠ Risco não informado (adotado baixo)" : "⚠ Coeficiente abaixo do recomendado", Nivel.Alerta, r.AvisoCoef + "\nClique para ajustar no passo 1.", () => IrPara(1)));
            }
            if (!string.IsNullOrEmpty(r.Aviso) && r.Aviso.Contains("carga aplicada no próprio nó"))
                l.Add(Chip("⚠ Carga no nó não entra", Nivel.Alerta, "Há carga aplicada no próprio nó da placa: ela vai direto ao apoio e não entra nas forças das fixações calculadas aqui."));
            return l;
        }

        // Rola até o passo e realça a borda por um instante
        void IrPara(int passo)
        {
            FecharFlutuantes();
            Border alvo = passo == 1 ? cardFix : passo == 2 ? cardCh : cardSd;
            if (alvo == null) return;
            if (realcado != null) { realcado.BorderBrush = TemaPlant.BBordaSutil; realcado.BorderThickness = new Thickness(1); }
            alvo.BorderBrush = TemaPlant.BBordaFoco; alvo.BorderThickness = new Thickness(1.5);
            realcado = alvo;
            alvo.BringIntoView();
            tmRealce.Stop(); tmRealce.Start();
        }

        // ---------- 2) esquerda: seletor de vista + desenho ----------
        static readonly string[] NomesVistas = { "Planta", "3D", "Forças", "Ficha", "Cálculo" };
        static readonly string[] DicasVistas =
        {
            "Vista de cima com as cotas. Clique numa cota azul para mudar a medida.",
            "Placa com espessura, pilar no ângulo real, concreto translúcido, chumbadores com o embutimento hef e cones de arrancamento. Arraste para girar.",
            "Tração (T) e corte (V) em cada furo na combinação crítica, com os esforços no pé do pilar.",
            "Tudo o que a oficina precisa: chapa, furos, chumbadores, torque e solda.",
            "Todas as verificações em tabela: grandeza, fórmula, valor e ✓ / ✕."
        };
        static readonly string[] SubVistas =
        {
            "vista de cima · clique numa cota azul para editar",
            "arraste para girar · roda = zoom · duplo clique = vista inicial",
            "T e V por furo na combinação crítica",
            "cortar, furar, soldar e apertar",
            "fórmulas e valores de cada verificação"
        };

        void MontarVista(ResPlaca r, ResSolda sd)
        {
            var itens = new List<(string, bool, Action, string)>();
            for (int i = 0; i < NomesVistas.Length; i++)
            {
                int k = i;
                itens.Add((NomesVistas[i], abaAtiva == i, (Action)(() => { abaAtiva = k; Recalcular(); }), DicasVistas[i]));
            }
            hostSeg.Child = Segmentado(itens);
            lblDicaVista.Text = SubVistas[abaAtiva];
            if (abaAtiva == 0 || abaAtiva == 2)
            {
                plano.ModoForcas = abaAtiva == 2;
                areaAba.Background = TemaPlant.BBgCanvas;
                areaAba.Child = plano;
                plano.InvalidateVisual();
            }
            else if (abaAtiva == 1)
            {
                areaAba.Background = TemaPlant.BBgCanvas;
                areaAba.Child = vista3d;
                vista3d.InvalidateVisual();
            }
            else if (abaAtiva == 3) { areaAba.Background = TemaPlant.BSup0; areaAba.Child = Ficha(r, sd); }
            else { areaAba.Background = TemaPlant.BSup0; areaAba.Child = AbaCalculo(r, sd); }
        }

        // ---------- 3) direita: 3 passos numerados ----------
        Border Passo(int num, string titulo, double ap, bool ok, bool aplicavel, bool maisAberto, Action alternarMais, UIElement corpo, Func<UIElement> mais)
        {
            var sp = new StackPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderBrush = TemaPlant.BAcento, BorderThickness = new Thickness(1.5), Background = TemaPlant.BAcentoSutil,
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = num.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BRealce, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            esq.Children.Add(Tx(titulo.ToUpperInvariant(), 11, TemaPlant.BTexto2, FontWeights.SemiBold));
            DockPanel.SetDock(esq, Dock.Left);
            cab.Children.Add(esq);

            var dir = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (aplicavel)
            {
                var c = Cores(NivelAp(ap, ok));
                var pct = Tx(Pct(ap), 12, c.fg, FontWeights.Bold); pct.MinWidth = 38; pct.TextAlignment = TextAlignment.Right;
                Typography.SetNumeralAlignment(pct, FontNumeralAlignment.Tabular);
                dir.Children.Add(pct);
                dir.Children.Add(Chip(ok && ap <= 1 + 1e-9 ? "✓ passa" : "✕ não passa", NivelAp(ap, ok), null, null, 10.5));
                ((FrameworkElement)dir.Children[dir.Children.Count - 1]).Margin = new Thickness(8, 0, 0, 0);
            }
            if (mais != null)
            {
                var lk = Link(maisAberto ? "menos ▴" : "mais ▾", alternarMais, maisAberto ? "Esconder as opções extras e o cálculo deste passo." : "Mostrar opções extras e o cálculo deste passo.", 11.5);
                lk.Foreground = TemaPlant.BTexto2; lk.Margin = new Thickness(12, 0, 0, 0);
                dir.Children.Add(lk);
            }
            DockPanel.SetDock(dir, Dock.Right);
            cab.Children.Add(dir);
            var barra = aplicavel ? BarraCap(ap, ok, 5) : new Border();
            ((FrameworkElement)barra).Margin = new Thickness(12, 0, 4, 0);
            cab.Children.Add(barra);
            sp.Children.Add(cab);
            sp.Children.Add(corpo);
            if (mais != null && maisAberto)
            {
                var bm = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = mais() };
                sp.Children.Add(bm);
            }
            return new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 6), Child = sp
            };
        }

        // Linha "rótulo | controles" com a coluna de rótulos fixa (alinhamento entre passos)
        static Grid Linha(string rotulo, UIElement conteudo, double margemTopo = 6, string dica = null)
        {
            var g = new Grid { Margin = new Thickness(0, margemTopo, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LargRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Tx(rotulo, 12, TemaPlant.BTexto2);
            if (dica != null) Dica(l, dica);
            g.Children.Add(l);
            Grid.SetColumn((FrameworkElement)conteudo, 1);
            g.Children.Add(conteudo);
            return g;
        }

        static StackPanel Horiz(params UIElement[] els)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var e in els) if (e != null) sp.Children.Add(e);
            return sp;
        }

        static FrameworkElement Gap(double w) { return new Border { Width = w }; }

        // ① Fixação
        Border MontarPassoFixacao(ResPlaca r)
        {
            Chumbador chAt = r.Ch ?? Chumbadores.Achar(edit.Chumbador);
            bool modoCh = chAt != null;
            int tipo = !modoCh ? 2 : chAt.Quimico ? 1 : 0;
            var corpo = new StackPanel();

            // tipo + Sugerir
            var linTipo = new DockPanel();
            btSugerir = Botao("Sugerir", "dimens", MostrarSugerir, "Mostra as 3 melhores combinações fixação + chapa que passam com os coeficientes atuais (preferência: " + (prefFuroMenor ? "furo menor" : "mais leve") + ").");
            DockPanel.SetDock(btSugerir, Dock.Right);
            linTipo.Children.Add(btSugerir);
            linTipo.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Mecânico", tipo == 0, () => MudarTipo(0), "Chumbador de expansão (catálogo): instalação imediata no concreto pronto."),
                ("Químico", tipo == 1, () => MudarTipo(1), "Chumbador com resina (ampola ou injeção): mais carga, precisa de cura."),
                ("Parafuso", tipo == 2, () => MudarTipo(2), "Parafuso métrico verificado pela classe do aço (Sp / √(σ² + 3τ²)).")
            }));
            corpo.Children.Add(linTipo);

            if (modoCh)
            {
                var menorPassa = r.TemEsforcos
                    ? Chumbadores.Lista.Where(c => c.Quimico == chAt.Quimico).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).FirstOrDefault(c => PlacaCalc.CandidatoChumbador(r, edit, m, c).ChOk)
                    : null;
                var linNome = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var links = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var lkTrocar = Link("Trocar ›", AbrirOverlayTrocar, "Lista de todos os chumbadores do catálogo: recomendados, maiores e os que não servem aqui (com o motivo).");
                lkTrocar.HorizontalAlignment = HorizontalAlignment.Right;
                var lkComp = Link("Comparar ›", () => AbrirComparar(chAt), "Compara 2 ou mais chumbadores lado a lado: catálogo, cargas, verificação nesta placa, usos e avisos.");
                lkComp.HorizontalAlignment = HorizontalAlignment.Right; lkComp.Margin = new Thickness(0, 3, 0, 0);
                links.Children.Add(lkTrocar); links.Children.Add(lkComp);
                DockPanel.SetDock(links, Dock.Right);
                linNome.Children.Add(links);
                var info = new StackPanel();
                var wpNome = new WrapPanel();
                wpNome.Children.Add(Tx(r.Paraf.Count + " × " + NomeCurtoCh(chAt), 14, TemaPlant.BTexto1, FontWeights.Bold));
                if (menorPassa == chAt) { var tg = Chip("menor que passa", Nivel.Ok, "É o menor chumbador deste tipo que passa nesta placa.", null, 10.5); tg.Margin = new Thickness(8, 1, 0, 0); wpNome.Children.Add(tg); }
                info.Children.Add(wpNome);
                string tq = chAt.Torque > 0 ? " · torque " + Num.Fmt(chAt.Torque, 1) + " kgf·m" : "";
                var det = Tx(chAt.Fabricante + " · furo Ø" + Num.Fmt(chAt.Furo, chAt.Furo == Math.Floor(chAt.Furo) ? 0 : 1) + " × " + Num.Fmt(chAt.Hef, 0) + " mm" + tq, 11.5, TemaPlant.BTexto2);
                det.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(det);
                linNome.Children.Add(info);
                corpo.Children.Add(linNome);

                // atalho: − / + troca o diâmetro dentro da mesma família (mesma linha do fabricante); ao lado, o hef do diâmetro
                var familia = Chumbadores.Lista.Where(c => c.Linha == chAt.Linha && c.Fabricante == chAt.Fabricante).ToList();
                var roscas = familia.Select(c => c.Rosca).Distinct().OrderBy(x => x).ToList();
                var hefs = familia.Where(c => c.Rosca == chAt.Rosca).OrderBy(c => c.Hef).ToList();
                int iRosca = roscas.IndexOf(chAt.Rosca), iHef = hefs.IndexOf(chAt);
                Action Trocar(Chumbador alvo) => alvo == null ? null : () =>
                {
                    if (string.IsNullOrEmpty(chumbAntes)) chumbAntes = edit.Chumbador;
                    edit.Chumbador = alvo.Nome; Recalcular();
                };
                Chumbador NoDiametro(int j) => j < 0 || j >= roscas.Count ? null
                    : familia.Where(c => c.Rosca == roscas[j]).OrderBy(c => Math.Abs(c.Hef - chAt.Hef)).ThenBy(c => c.Hef).First();
                var stDiam = Stepper(chAt.Pol, Trocar(NoDiametro(iRosca - 1)), Trocar(NoDiametro(iRosca + 1)), 58, AbrirOverlayTrocar,
                    "Diâmetro anterior / próximo da linha " + chAt.Linha.Replace("Walsywa ", "") + " (" + roscas.Count + " diâmetros). Clique no valor para ver a lista completa.");
                UIElement stHef = hefs.Count > 1
                    ? Horiz(Gap(12), Tx("hef", 12, TemaPlant.BTexto2), Gap(6),
                            Stepper(chAt.Hef.ToString("0") + " mm", Trocar(iHef > 0 ? hefs[iHef - 1] : null), Trocar(iHef < hefs.Count - 1 ? hefs[iHef + 1] : null), 64, null,
                                    "Profundidade de ancoragem (hef) disponível neste diâmetro."))
                    : null;
                corpo.Children.Add(Linha("Diâmetro", Horiz(stDiam, stHef), 8, "Troca rápida dentro da mesma família. Para outra família, use Trocar."));

                // quantidade (+ classe da barra se químico)
                UIElement extra = null;
                if (chAt.Quimico)
                    extra = Horiz(Gap(10), Dica(Tx("barra", 12, TemaPlant.BTexto2), "Classe da barra roscada do chumbador químico (aço verificado por von Mises com Sp)."), Gap(6),
                                  Combo(Parafusos.Classes, pe.ClasseBarra, s => { pe.ClasseBarra = s; Recalcular(); }, 66, "Classe da barra roscada (4.6 = barra comum, lado seguro)."));
                corpo.Children.Add(Linha("Quantidade", Horiz(SegQuantidade(), extra), 8, "Fileiras × fixações por fileira, no contorno da placa."));
                if (mostrarOutroArranjo || !ArranjoPadrao()) corpo.Children.Add(Linha("", LinhaOutroArranjo(), 6));

                // coeficiente com a origem + os 3 controles que mudam o n
                int iTipo = string.IsNullOrEmpty(edit.TipoCarga) ? 0 : Array.IndexOf(Chumbadores.Tipos, edit.TipoCarga.ToLowerInvariant()) + 1;
                var opsTipo = new List<string> { "auto: " + Chumbadores.NomesTipos[r.TemEsforcos ? r.TipoCarga : 1] };
                opsTipo.AddRange(Chumbadores.NomesTipos);
                string riscoAt = (edit.Risco ?? "").ToLowerInvariant();
                var opsRisco = new List<string> { "auto: " + (r.RiscoAlto ? "alto" : "baixo"), "baixo risco", "alto risco" };
                int iRisco = riscoAt == "baixo" ? 1 : riscoAt == "alto" ? 2 : 0;
                var cbTipo = Combo(opsTipo, opsTipo[Math.Max(0, iTipo)], s => { int i = opsTipo.IndexOf(s); pe.TipoCarga = i <= 0 ? "automático" : Chumbadores.NomesTipos[i - 1]; Recalcular(); }, 104,
                    "Tipo de carga (tabela Walsywa, catálogo pág. 07). Baixo / alto risco: estática 3 / 5 · variável 4 / 5 · dinâmica 7 / 15 · choque 5 / 10. Auto = dinâmica se houver carga giratória no modelo, senão variável.");
                var cbRisco = Combo(opsRisco, opsRisco[iRisco], s => { int i = opsRisco.IndexOf(s); pe.Risco = i == 1 ? "baixo" : i == 2 ? "alto" : "automático"; Recalcular(); }, 106,
                    "Grau de risco. Alto = a falha pode ferir pessoas ou derrubar o equipamento (coeficiente maior). Auto = alto com carga giratória, senão baixo (com aviso).");
                var bxFck = CaixaNum(Num.Fmt(edit.Fck > 0 ? edit.Fck : 20, 0), "MPa", 90, v => { edit.Fck = v; Recalcular(); },
                    "fck do concreto da laje. Se for menor que o do ensaio do catálogo, a carga no concreto é reduzida por √(fck / fck do ensaio).", v => v > 0 && v < 200, "fck");
                corpo.Children.Add(Linha("Carga", Horiz(cbTipo, Gap(6), cbRisco, Gap(6), bxFck), 8, "Condições que definem o coeficiente do chumbador (tabela Walsywa) e a resistência do concreto."));
                corpo.Children.Add(Linha("Exigido", TextoCoefChumbador(r), 6));
            }
            else
            {
                int idAt = Array.IndexOf(Parafusos.D, edit.Diametro); if (idAt < 0) idAt = 4;
                int icAt = Array.IndexOf(Parafusos.Classes, edit.Classe); if (icAt < 0) icAt = 3;
                var info = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                info.Children.Add(Tx(r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe, 14, TemaPlant.BTexto1, FontWeights.Bold));
                var det = Tx("pré-carga " + Num.Fmt(r.Fi, 1) + " kN · torque " + Num.Fmt(r.Torque, 0) + " N·m · furo na chapa Ø" + Num.Fmt(edit.Diametro + (edit.Diametro <= 24 ? 2 : 3), 0) + " mm", 11.5, TemaPlant.BTexto2);
                det.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(det);
                corpo.Children.Add(info);
                corpo.Children.Add(Linha("Diâmetro", Regua(Parafusos.D.Length, i =>
                {
                    double d = Parafusos.D[i];
                    double seg = r.TemEsforcos ? PlacaCalc.SegurancaCom(r, edit, i, icAt) : double.PositiveInfinity;
                    return ("M" + Num.Fmt(d, 0), seg >= r.NExig - 1e-9, Math.Abs(d - edit.Diametro) < 1e-6, "M" + Num.Fmt(d, 0) + " · segurança " + FmtN(seg), () => { edit.Diametro = d; Recalcular(); });
                }), 8, "Verde = passa, vermelho = não passa, branco = atual. Clique para escolher."));
                var itensCl = new List<(string, bool, Action, string)>();
                foreach (string cl in Parafusos.Classes) { string c = cl; int ic = Array.IndexOf(Parafusos.Classes, cl); itensCl.Add((cl, edit.Classe == cl, (Action)(() => { edit.Classe = c; Recalcular(); }), "Classe " + cl + " (ISO 898-1), Sp = " + Num.Fmt(Parafusos.Sp[ic], 0) + " MPa")); }
                corpo.Children.Add(Linha("Classe", Segmentado(itensCl, 28, 11.5), 6));
                corpo.Children.Add(Linha("Quantidade", SegQuantidade(), 6));
                if (mostrarOutroArranjo || !ArranjoPadrao()) corpo.Children.Add(Linha("", LinhaOutroArranjo(), 6));
                var tc = Tx("n = " + Num.Fmt(r.NExig, 2) + " (coeficiente da placa, passo 2)" + (r.TemEsforcos ? " → obtido " + FmtN(r.NComb) : ""), 11.5, r.TemEsforcos && r.NComb < r.NExig ? TemaPlant.BErro : TemaPlant.BTexto1, null, true);
                corpo.Children.Add(Linha("Exigido", tc, 8));
            }

            return Passo(1, "Fixação", r.ApFix, FixOk(r), r.TemEsforcos, maisFix, () => { maisFix = !maisFix; Recalcular(); }, corpo, () => MaisFixacao(r, chAt));
        }

        // Coeficiente de seguranca da solda. O admissivel 0,30.FEXX (AWS / AISC, metodo das tensoes admissiveis) ja traz a
        // seguranca embutida: comparado com a resistencia ao corte do metal de solda por von Mises (0,577.FEXX), equivale a
        // n = 0,577 / 0,30 = 1,92. Por isso "passa" e "n >= 1,92" sao a mesma condicao (nao e um criterio novo).
        const string DicaCsSolda = "n = 0,577·FEXX / τ (resistência ao corte do metal de solda por von Mises, dividida pela tensão). " +
                                   "A norma não pede um n separado: o limite τ ≤ 0,30·FEXX já embute n ≈ 1,92, que é o mínimo mostrado.";

        UIElement TextoCsSolda(ResSolda sd)
        {
            double nMin = 0.577 / 0.30;
            bool ok = sd.NEquiv >= nMin - 1e-9;
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto2 };
            tb.Inlines.Add(new System.Windows.Documents.Run("n = " + FmtN(sd.NEquiv) + (ok ? " ≥ " : " < ") + Num.Fmt(nMin, 2)) { Foreground = ok ? TemaPlant.BSucesso : TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(" · mínimo que a norma já embute no 0,30·FEXX"));
            return tb;
        }

        UIElement TextoCoefChumbador(ResPlaca r)
        {
            var sp = new StackPanel();
            if (!r.TemEsforcos || r.Ch == null) { sp.Children.Add(Tx("—", 12, TemaPlant.BTexto2)); return sp; }
            double nTab = Chumbadores.Coef(r.TipoCarga, r.RiscoAlto);
            string condicao = "carga " + Chumbadores.NomesTipos[r.TipoCarga] + " com " + (r.RiscoAlto ? "alto" : "baixo") + " risco";
            bool digitado = r.Ch.Tu > 0 && edit.CoefChumbador > 0, abaixo = digitado && edit.CoefChumbador < nTab - 1e-9;
            string origem;
            if (r.Ch.Tu <= 0) origem = "carga recomendada do fabricante";
            else if (abaixo) origem = "digitado por você, ABAIXO do que a Walsywa recomenda: n = " + Num.Fmt(nTab, 0) + " para " + condicao;
            else if (digitado) origem = "digitado por você (a Walsywa recomenda " + Num.Fmt(nTab, 0) + " para " + condicao + ")";
            else origem = "recomendado pela Walsywa para " + condicao;
            var l1 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = abaixo ? TemaPlant.BAlerta : TemaPlant.BTexto2 };
            l1.Inlines.Add(new System.Windows.Documents.Run("n = " + Num.Fmt(r.NRef, r.NRef == Math.Floor(r.NRef) ? 0 : 1)) { Foreground = TemaPlant.BTexto1, FontWeight = FontWeights.Bold });
            l1.Inlines.Add(new System.Windows.Documents.Run(" · " + origem));
            sp.Children.Add(l1);
            if (digitado)
            {
                var volta = Link("usar o recomendado (n = " + Num.Fmt(nTab, 0) + ")", () => { edit.CoefChumbador = 0; Recalcular(); },
                    "O coeficiente vem da tabela da Walsywa (catálogo pág. 07), conforme o tipo de carga e o grau de risco escolhidos acima.");
                volta.Margin = new Thickness(0, 2, 0, 0);
                volta.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(volta);
            }
            bool cOk = r.NComb >= r.NRef;
            var l2 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 1, 0, 0) };
            l2.Inlines.Add(new System.Windows.Documents.Run("obtido no concreto "));
            l2.Inlines.Add(new System.Windows.Documents.Run(FmtN(r.NComb) + (cOk ? " ≥ " : " < ") + Num.Fmt(r.NRef, 1)) { Foreground = cOk ? TemaPlant.BSucesso : TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
            if (r.Ch.Quimico)
            {
                bool aOk = r.NAco >= r.NSeg;
                l2.Inlines.Add(new System.Windows.Documents.Run(" · aço da barra "));
                l2.Inlines.Add(new System.Windows.Documents.Run(FmtN(r.NAco) + (aOk ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 1)) { Foreground = aOk ? TemaPlant.BSucesso : TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
            }
            if (!r.EspacOk)
            {
                l2.Inlines.Add(new System.Windows.Documents.Run(" · furos a " + Num.Fmt(r.EspacMin, 0) + " mm < 6·d = " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + " mm (não instala)") { Foreground = TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
            }
            sp.Children.Add(l2);
            return sp;
        }

        UIElement MaisFixacao(ResPlaca r, Chumbador chAt)
        {
            var sp = new StackPanel();
            if (chAt != null)
            {
                double nTab = r.TemEsforcos ? Chumbadores.Coef(r.TipoCarga, r.RiscoAlto) : 0;
                var st = Stepper(edit.CoefChumbador > 0 ? "n = " + Num.Fmt(edit.CoefChumbador, 1) : "tabela (" + Num.Fmt(nTab, 0) + ")",
                    PassoValor(ValoresNCh, edit.CoefChumbador, -1, v => edit.CoefChumbador = v), PassoValor(ValoresNCh, edit.CoefChumbador, +1, v => edit.CoefChumbador = v), 110,
                    () => { string resp = Pergunta.Mostrar(this, "Coeficiente do chumbador", "Coeficiente do chumbador no concreto (0 = tabela Walsywa):", Num.Fmt(edit.CoefChumbador, 1)); double v; if (resp != null && Num.Ler(resp, out v) && v >= 0) { edit.CoefChumbador = v; Recalcular(); } },
                    "Divide a carga de ruptura do catálogo. 'tabela' = pela tabela Walsywa (tipo de carga + grau de risco). Clique no valor para digitar.");
                sp.Children.Add(Linha("Coeficiente", Horiz(st, Gap(8), Tx("0 = pela tabela", 11, TemaPlant.BTexto3)), 0));
            }
            else
            {
                var cbRosca = new CheckBox { Content = Tx("Rosca no plano de corte", 12, TemaPlant.BTexto1), IsChecked = edit.RoscaNoCorte, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                cbRosca.Checked += (s, e) => { edit.RoscaNoCorte = true; Recalcular(); };
                cbRosca.Unchecked += (s, e) => { edit.RoscaNoCorte = false; Recalcular(); };
                Dica(cbRosca, "Marcado = o plano de cisalhamento passa pela rosca (área resistente menor).");
                sp.Children.Add(Linha("Corte", cbRosca, 0));
            }
            sp.Children.Add(TituloSecao("Cálculo", null, 10));
            sp.Children.Add(TabelaCompacta(CalcFix(r)));
            return sp;
        }

        bool ArranjoPadrao()
        {
            return edit.Fileiras == 2 && (edit.PorFileira == 2 || edit.PorFileira == 3) || edit.Fileiras == 3 && edit.PorFileira == 3;
        }

        UIElement SegQuantidade()
        {
            bool e22 = edit.Fileiras == 2 && edit.PorFileira == 2, e23 = edit.Fileiras == 2 && edit.PorFileira == 3, e33 = edit.Fileiras == 3 && edit.PorFileira == 3;
            bool outro = !e22 && !e23 && !e33;
            return Segmentado(new List<(string, bool, Action, string)>
            {
                ("2 × 2", e22, () => { edit.Fileiras = 2; edit.PorFileira = 2; mostrarOutroArranjo = false; Recalcular(); }, "2 fileiras × 2 = 4 fixações no contorno da placa."),
                ("2 × 3", e23, () => { edit.Fileiras = 2; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }, "2 fileiras × 3 = 6 fixações."),
                ("3 × 3", e33, () => { edit.Fileiras = 3; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }, "3 fileiras × 3 = 8 fixações no contorno (o centro fica livre)."),
                (outro ? edit.Fileiras + " × " + edit.PorFileira : "outro…", outro || mostrarOutroArranjo, () => { mostrarOutroArranjo = !mostrarOutroArranjo; Recalcular(); }, "Digitar outra quantidade de fileiras e de fixações por fileira."),
            });
        }

        UIElement LinhaOutroArranjo()
        {
            var bF = CaixaNum(edit.Fileiras.ToString(), "fil.", 70, v => { edit.Fileiras = Math.Max(1, (int)Math.Round(v)); Recalcular(); }, "Fileiras ao longo do plano do pórtico (H).", v => v >= 1 && v <= 10);
            var bP = CaixaNum(edit.PorFileira.ToString(), "por fil.", 84, v => { edit.PorFileira = Math.Max(1, (int)Math.Round(v)); Recalcular(); }, "Fixações em cada fileira (ao longo de B).", v => v >= 1 && v <= 10);
            return Horiz(bF, Tx("  ×  ", 12, TemaPlant.BTexto2), bP);
        }

        void MudarTipo(int tipo)
        {
            Chumbador atual = Chumbadores.Achar(edit.Chumbador);
            int tipoAt = atual == null ? 2 : atual.Quimico ? 1 : 0;
            if (tipo == tipoAt) return;
            if (tipo == 2) { edit.Chumbador = null; Recalcular(); return; }
            bool quim = tipo == 1;
            var lista = Chumbadores.Lista.Where(c => c.Quimico == quim).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).ToList();
            if (lista.Count == 0) return;
            double dRef = atual != null ? atual.Rosca : edit.Diametro;
            edit.Chumbador = lista.OrderBy(c => Math.Abs(c.Rosca - dRef)).ThenBy(c => c.Hef).First().Nome;   // base do tipo certo (coeficiente do chumbador)
            ResPlaca rb = CalcFix();
            if (rb.TemEsforcos)
            {
                var ok = lista.FirstOrDefault(c => PlacaCalc.CandidatoChumbador(rb, edit, m, c).ChOk);
                if (ok != null) edit.Chumbador = ok.Nome;
            }
            msgDim = "Tipo trocado para " + (quim ? "químico" : "mecânico") + ": " + NomeCurtoCh(Chumbadores.Achar(edit.Chumbador)) + ".";
            Recalcular();
        }

        void AbrirComparar(Chumbador chAt)
        {
            var rc = CalcFix();
            var ini = new List<string> { chAt.Nome };
            var alt = Chumbadores.Lista.Where(c => c != chAt && c.Linha != chAt.Linha && PlacaCalc.CandidatoChumbador(rc, edit, m, c).ChOk).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).Select(c => c.Nome).FirstOrDefault();
            if (alt != null) ini.Add(alt);
            var jc = new JanelaComparar(m, n, rc, edit, ini) { Owner = this };
            jc.ShowDialog();
            Recalcular();   // "Usar este chumbador" grava direto na placa em edição
        }

        // ② Chapa
        Border MontarPassoChapa(ResPlaca r)
        {
            var corpo = new StackPanel();
            int idx = Array.FindIndex(PlacaCalc.Chapas, e => Math.Abs(e - edit.Esp) < 1e-4);
            Action menos = idx > 0 ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx - 1]; Recalcular(); })
                : (edit.Esp > PlacaCalc.Chapas[0] ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.LastOrDefault(e => e < edit.Esp); if (edit.Esp <= 0) edit.Esp = PlacaCalc.Chapas[0]; Recalcular(); }) : null);
            Action mais = idx >= 0 && idx + 1 < PlacaCalc.Chapas.Length ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx + 1]; Recalcular(); })
                : (edit.Esp < PlacaCalc.Chapas.Last() ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.First(e => e > edit.Esp); Recalcular(); }) : null);
            var st = Stepper(FmtEsp(edit.Esp) + " mm", menos, mais, 82, () =>
            {
                string resp = Pergunta.Mostrar(this, "Espessura da chapa", "Espessura da chapa (mm):", FmtEsp(edit.Esp));
                double v; if (resp != null && Num.Ler(resp, out v) && v > 0) { edit.Esp = v; Recalcular(); }
            }, "Espessuras comerciais. Clique no valor para digitar outra.");
            double massa = r.H * r.B * edit.Esp * 7850e-9;
            var need = Tx(r.EspNec > 0 ? "necessária " + Num.Fmt(r.EspNec, 1) + " mm · " + Num.Fmt(massa, 1) + " kg" : "sem flexão · " + Num.Fmt(massa, 1) + " kg", 11.5, ChapaOk(r) ? TemaPlant.BTexto2 : TemaPlant.BErro);
            need.HorizontalAlignment = HorizontalAlignment.Right;
            var dpEsp = new DockPanel();
            DockPanel.SetDock(need, Dock.Right); dpEsp.Children.Add(need); dpEsp.Children.Add(st);
            corpo.Children.Add(Linha("Espessura", dpEsp, 0));
            corpo.Children.Add(Linha("", Regua(PlacaCalc.Chapas.Length, i =>
            {
                double t = PlacaCalc.Chapas[i];
                return (FmtEsp(t), t >= r.EspNec - 1e-9, Math.Abs(t - edit.Esp) < 1e-4, FmtEsp(t) + " mm · segurança " + SegChapa(r, t), () => { edit.Esp = t; Recalcular(); });
            }), 4));

            // tamanho H × B + Ajustar
            var bH = CaixaNum(Num.Fmt(r.H, 0), "mm", 78, v => { if (edit.PeloPerfil) { edit.PeloPerfil = false; edit.B = r.B; } edit.H = v; Recalcular(); },
                "Comprimento no plano do pórtico (H). Digitar desliga o \"medir pelo perfil\".", v => v > 0);
            var bB = CaixaNum(Num.Fmt(r.B, 0), "mm", 78, v => { if (edit.PeloPerfil) { edit.PeloPerfil = false; edit.H = r.H; } edit.B = v; Recalcular(); },
                "Largura fora do plano (B). Digitar desliga o \"medir pelo perfil\".", v => v > 0);
            var btAj = Botao("Ajustar tamanho", null, () => { AjustarTamanho(); Recalcular(); }, TextoRegrasTamanho());
            var dpTam = new DockPanel();
            DockPanel.SetDock(btAj, Dock.Right); dpTam.Children.Add(btAj);
            dpTam.Children.Add(Horiz(bH, Tx("  ×  ", 12, TemaPlant.BTexto2), bB));
            corpo.Children.Add(Linha("Tamanho", dpTam, 6, "H × B da chapa. Também dá para clicar nas cotas azuis do desenho."));

            // aço + coeficiente da placa (e do parafuso / aço da barra) na mesma linha
            var bFy = CaixaNum(Num.Fmt(r.FyChapa, 0), "MPa", 96, v => { edit.FyChapa = v; Recalcular(); }, "Tensão de escoamento do aço da chapa (A36 = 250 MPa).", v => v > 0 && v < 2000, "fy");
            bool modoCh = !string.IsNullOrEmpty(edit.Chumbador);
            var stN = Stepper(edit.Seguranca > 0 ? "n = " + Num.Fmt(edit.Seguranca, 2) : "n auto " + Num.Fmt(m.Seguranca, 2),
                PassoValor(ValoresN, edit.Seguranca, -1, v => edit.Seguranca = v), PassoValor(ValoresN, edit.Seguranca, +1, v => edit.Seguranca = v), 92,
                () => { string resp = Pergunta.Mostrar(this, "Coeficiente da placa", "Coeficiente da chapa" + (modoCh ? " e do aço da barra" : " e do parafuso") + " (0 = o do modelo):", Num.Fmt(edit.Seguranca, 2)); double v; if (resp != null && Num.Ler(resp, out v) && v >= 0) { edit.Seguranca = v; Recalcular(); } },
                "Coeficiente de segurança da chapa" + (modoCh ? " e do aço da barra roscada (químico)" : " e do parafuso (Sp / √(σ² + 3τ²))") + ". Origem: " + (edit.Seguranca > 0 ? "digitado nesta placa" : "o do modelo (aba Análise), n = " + Num.Fmt(m.Seguranca, 2)) + ". Clique no valor para digitar.");
            var orig = Tx(edit.Seguranca > 0 ? "desta placa" : "do modelo", 11.5, TemaPlant.BTexto3);
            corpo.Children.Add(Linha("Aço", Horiz(bFy, Gap(10), stN, Gap(8), orig), 6));

            return Passo(2, "Chapa", ApChapa(r), ChapaOk(r), r.TemEsforcos, maisCh, () => { maisCh = !maisCh; Recalcular(); }, corpo, () => MaisChapa(r));
        }

        UIElement MaisChapa(ResPlaca r)
        {
            var sp = new StackPanel();
            var cbPerfil = new CheckBox { Content = Tx("Medir pelo perfil (pilar + sobra de cada lado)", 12, TemaPlant.BTexto1), IsChecked = edit.PeloPerfil, Cursor = Cursors.Hand };
            cbPerfil.Checked += (s, e) => { edit.PeloPerfil = true; Recalcular(); };
            cbPerfil.Unchecked += (s, e) => { edit.PeloPerfil = false; edit.H = r.H; edit.B = r.B; Recalcular(); };
            Dica(cbPerfil, "Marcado: a chapa acompanha o perfil do pilar + a sobra (arredonda de 10 em 10 mm). Desmarcado: medidas digitadas.");
            sp.Children.Add(cbPerfil);
            if (edit.PeloPerfil)
                sp.Children.Add(Linha("Sobra", CaixaNum(Num.Fmt(edit.Sobra, 0), "mm", 78, v => { edit.Sobra = v; Recalcular(); }, "Folga da placa além do perfil, em cada lado.", v => v >= 0), 6));
            sp.Children.Add(Linha("Borda", Horiz(CaixaNum(Num.Fmt(edit.Borda, 0), "mm", 78, v => { edit.Borda = v; Recalcular(); }, "Do centro dos furos externos até a borda da chapa.", v => v > 0 && 2 * v < r.H && 2 * v < r.B),
                Gap(8), Tx("centro do furo até a borda", 11.5, TemaPlant.BTexto3)), 6));
            var cbPivo = new CheckBox { Content = Tx("Giro na mesa comprimida do perfil", 12, TemaPlant.BTexto1), IsChecked = edit.PivoNaMesa, Cursor = Cursors.Hand, Margin = new Thickness(0, 8, 0, 0) };
            cbPivo.Checked += (s, e) => { edit.PivoNaMesa = true; Recalcular(); };
            cbPivo.Unchecked += (s, e) => { edit.PivoNaMesa = false; Recalcular(); };
            Dica(cbPivo, "Marcado = a placa gira na mesa comprimida do perfil; desmarcado = na borda comprimida da placa.");
            sp.Children.Add(cbPivo);
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cbCone = new CheckBox { Content = Tx("Ajustar tamanho sem sobrepor cones (3·hef)", 12, TemaPlant.BTexto1), IsChecked = semSobreporCones, Cursor = Cursors.Hand, Margin = new Thickness(0, 8, 0, 0) };
                cbCone.Checked += (s, e) => semSobreporCones = true; cbCone.Unchecked += (s, e) => semSobreporCones = false;
                Dica(cbCone, "Marcado: \"Ajustar tamanho\" usa espaçamento ≥ 3·hef entre chumbadores, para o catálogo valer sem redução por cones sobrepostos (placa maior). Desmarcado: usa o mínimo para instalar e aplica a redução ψ.");
                sp.Children.Add(cbCone);
            }
            sp.Children.Add(TituloSecao("Cálculo", null, 10));
            sp.Children.Add(TabelaCompacta(CalcChapa(r)));
            return sp;
        }

        // ③ Solda
        Border MontarPassoSolda(ResPlaca r, ResSolda sd)
        {
            var corpo = new StackPanel();
            bool sdOk = sd == null || sd.Passa;
            double hEf = sd != null ? sd.H : (edit.Solda > 0 ? edit.Solda : 3);
            int idxP = edit.Solda <= 0 ? -1 : Array.FindIndex(Solda.Pernas, p => Math.Abs(p - edit.Solda) < 1e-4);
            Action menos = edit.Solda <= 0 ? null : (Action)(() => { edit.Solda = idxP <= 0 ? 0 : Solda.Pernas[idxP - 1]; Recalcular(); });
            Action mais = () =>
            {
                if (edit.Solda <= 0) { double prox = Solda.Pernas.FirstOrDefault(p => p > hEf + 1e-9); edit.Solda = prox > 0 ? prox : Solda.Pernas.Last(); }
                else if (idxP >= 0 && idxP + 1 < Solda.Pernas.Length) edit.Solda = Solda.Pernas[idxP + 1];
                Recalcular();
            };
            var st = Stepper(edit.Solda <= 0 ? "auto (" + Num.Fmt(hEf, 0) + " mm)" : Num.Fmt(edit.Solda, 0) + " mm", menos, mais, 96, () => { edit.Solda = 0; Recalcular(); },
                "Perna do filete em todo o contorno do perfil. Auto = a menor perna da tabela que passa (respeitando o mínimo de norma). Clique no valor para voltar ao automático.");
            string elAt = string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo;
            var cbEl = Combo(Solda.Eletrodos.Select(e => e + "XX"), elAt + "XX", s => { edit.Eletrodo = s.Replace("XX", ""); Recalcular(); }, 98, "Eletrodo AWS (tensão admissível no filete pela tabela do eletrodo).");
            var cont = Tx(sd != null && sd.Erro == "" ? "contorno " + Num.Fmt(sd.L, 0) + " mm" : "", 11.5, TemaPlant.BTexto2);
            var dp = new DockPanel();
            DockPanel.SetDock(cont, Dock.Right); dp.Children.Add(cont);
            dp.Children.Add(Horiz(st, Gap(8), cbEl));
            corpo.Children.Add(Linha("Perna", dp, 0));

            if (sd == null)
                corpo.Children.Add(Linha("", Tx(r.TemEsforcos ? "Perfil do pilar sem medidas para a solda." : "Sem esforços: calcule a estrutura.", 11.5, TemaPlant.BTexto2, null, true), 6));
            else if (sdOk)
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto2 };
                tb.Inlines.Add(new System.Windows.Documents.Run("τ = " + Num.Fmt(sd.Tau, 0) + " ≤ " + Num.Fmt(sd.TauAdm, 0) + " MPa") { Foreground = TemaPlant.BSucesso, FontWeight = FontWeights.SemiBold });
                tb.Inlines.Add(new System.Windows.Documents.Run(" · filete contínuo · perna mínima de norma " + (sd.HMinNorma > 0 ? Num.Fmt(sd.HMinNorma, 0) + " mm" : "—")));
                corpo.Children.Add(Linha("Tensão", tb, 6));
                corpo.Children.Add(Linha("Segurança", TextoCsSolda(sd), 4, DicaCsSolda));
            }
            else
            {
                corpo.Children.Add(Linha("Segurança", TextoCsSolda(sd), 6, DicaCsSolda));
                var dg = Solda.Diagnosticar(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, sd.H, edit.Eletrodo, edit.Esp);
                var box = new StackPanel();
                var t1 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto1 };
                t1.Inlines.Add(new System.Windows.Documents.Run("Por quê: ") { FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BErroClaro });
                t1.Inlines.Add(new System.Windows.Documents.Run(dg.Porque));
                box.Children.Add(t1);
                var t2 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 3, 0, 0) };
                t2.Inlines.Add(new System.Windows.Documents.Run("O que fazer: ") { FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BErroClaro });
                t2.Inlines.Add(new System.Windows.Documents.Run(dg.OQueFazer));
                box.Children.Add(t2);
                if (dg.Perna > 0)
                {
                    string el2 = dg.Eletrodo;
                    double p2 = dg.Perna;
                    var bt = Botao("Usar " + (el2 != elAt ? el2 + "XX, " : "") + "perna " + Num.Fmt(p2, 0) + " mm", null, () => { edit.Solda = p2; edit.Eletrodo = el2; Recalcular(); }, "Aplica a sugestão (o cálculo é refeito).", 26);
                    bt.HorizontalAlignment = HorizontalAlignment.Left; bt.Margin = new Thickness(0, 6, 0, 0);
                    box.Children.Add(bt);
                }
                corpo.Children.Add(new Border
                {
                    Background = TemaPlant.BErroFundo, BorderBrush = TemaPlant.BErroBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(9, 5, 9, 6), Margin = new Thickness(0, 6, 0, 0), Child = box
                });
            }
            if (r.Inclinada && sdOk)
            {
                var ti = Tx("Barra inclinada: chapa e solda valem para o pilar perpendicular; detalhe a ligação à parte.", 11, TemaPlant.BAlerta, null, true);
                ti.Margin = new Thickness(0, 6, 0, 0);
                corpo.Children.Add(ti);
            }
            return Passo(3, "Solda pilar–placa", sd != null ? sd.Ap : 0, sdOk, sd != null, maisSd, () => { maisSd = !maisSd; Recalcular(); }, corpo, () => TabelaCompacta(CalcSolda(sd)));
        }

        // "Comparado ao que estava" (linha discreta no fim da coluna)
        void MontarMudancas(ResPlaca r)
        {
            if (refInicial == null || r.H <= 0 || r.B <= 0) return;
            double h0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefIn + 2 * refInicial.Sobra) / 10) * 10 : refInicial.H;
            double b0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefOut + 2 * refInicial.Sobra) / 10) * 10 : refInicial.B;
            double dm = r.H * r.B * edit.Esp * 7850e-9 - h0 * b0 * refInicial.Esp * 7850e-9;
            var muds = new List<string>();
            if (Math.Abs(refInicial.Esp - edit.Esp) > 1e-4) muds.Add("chapa " + FmtEsp(refInicial.Esp) + " → " + FmtEsp(edit.Esp) + " mm");
            if (Math.Abs(h0 - r.H) > 1e-4 || Math.Abs(b0 - r.B) > 1e-4) muds.Add("placa " + Num.Fmt(h0, 0) + "×" + Num.Fmt(b0, 0) + " → " + Num.Fmt(r.H, 0) + "×" + Num.Fmt(r.B, 0) + " mm");
            var c0 = Chumbadores.Achar(refInicial.Chumbador); var c1 = Chumbadores.Achar(edit.Chumbador);
            string f0 = c0 != null ? NomeCurtoCh(c0) : "M" + Num.Fmt(refInicial.Diametro, 0) + " cl. " + refInicial.Classe;
            string f1 = c1 != null ? NomeCurtoCh(c1) : "M" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe;
            if (f0 != f1 || refInicial.Fileiras != edit.Fileiras || refInicial.PorFileira != edit.PorFileira)
                muds.Add(refInicial.Fileiras * refInicial.PorFileira + "× " + f0 + " → " + edit.Fileiras * edit.PorFileira + "× " + f1);
            if (muds.Count == 0) return;
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto3, Margin = new Thickness(2, 2, 2, 4) };
            tb.Inlines.Add(new System.Windows.Documents.Run("Comparado ao que estava: ") { Foreground = TemaPlant.BTexto2 });
            string sinal = dm < -0.05 ? "−" : dm > 0.05 ? "+" : "±";
            tb.Inlines.Add(new System.Windows.Documents.Run(sinal + Num.Fmt(Math.Abs(dm), 1) + " kg de chapa") { Foreground = dm <= 0.05 ? TemaPlant.BSucesso : TemaPlant.BAlerta, FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(" · " + string.Join(" · ", muds)));
            colDir.Children.Add(tb);
        }

        // ---------- 4) rodapé ----------
        void MontarRodape(ResPlaca r)
        {
            rodape.Children.Clear();
            var dir = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (original != null) dir.Children.Add(B("Remover placa", () => { n.Placa = null; Mudou = true; Close(); }, false, "Tira a placa de base deste nó."));
            dir.Children.Add(B("Cancelar", () => { n.Placa = original; Close(); }, false, "Fecha sem mudar nada (Esc)."));
            var btAp = B(marcados.Count > 0 ? "Aplicar em " + (marcados.Count + 1) + " nós" : "Aplicar", () => { AplicarEm.Clear(); AplicarEm.AddRange(Candidatos.Where(x => marcados.Contains(x.Id))); Mudou = true; Close(); }, true,
                "Grava esta placa no nó " + n.Id + (marcados.Count > 0 ? " e em " + string.Join(", ", Candidatos.Where(c => marcados.Contains(c.Id)).Select(c => "nó " + c.Id)) : "") + ".");
            btAp.IsDefault = false;
            dir.Children.Add(btAp);
            foreach (Button b in dir.Children.OfType<Button>()) { b.Height = 28; b.MinWidth = 92; }
            DockPanel.SetDock(dir, Dock.Right);
            rodape.Children.Add(dir);

            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(Botao("Dimensionar tudo", "dimens", () => { Dimensionar(); Recalcular(); },
                "Escolhe sozinho a menor fixação, a chapa e a solda que passam neste nó, segundo a preferência ao lado.", 28));
            esq.Children.Add(Gap(8));
            esq.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Mais leve", !prefFuroMenor, () => { prefFuroMenor = false; Recalcular(); }, "Ao dimensionar e sugerir, prefere a menor fixação e a chapa mais fina que passam."),
                ("Furo menor", prefFuroMenor, () => { prefFuroMenor = true; Recalcular(); }, "Ao dimensionar e sugerir, prefere o menor furo no concreto (broca menor).")
            }, 28));
            if (AvaliarEm != null && Candidatos.Count > 0)
            {
                esq.Children.Add(Gap(16));
                esq.Children.Add(new Border { Width = 1, Height = 22, Background = TemaPlant.BBordaSutil });
                esq.Children.Add(Gap(16));
                esq.Children.Add(Botao("Aplicar também em…" + (marcados.Count > 0 ? " (" + marcados.Count + ")" : ""), "copiar", MostrarAplicarEm,
                    "Outros pés de pilar que podem receber a mesma placa, com o pior caso de cada um; dimensionar pelo pior caso.", 28));
            }
            DockPanel.SetDock(esq, Dock.Left);
            rodape.Children.Add(esq);
            var msg = Tx(msgDim, 11.5, TemaPlant.BTexto3);
            msg.TextTrimming = TextTrimming.CharacterEllipsis; msg.Margin = new Thickness(14, 0, 10, 0);
            Dica(msg, msgDim);
            rodape.Children.Add(msg);
        }

        // ================= flutuantes =================
        // Sugerir: as 3 melhores combinações num popup leve preso ao botão
        void MostrarSugerir()
        {
            FecharFlutuantes();
            ResPlaca r = CalcEnv();
            var ops = MelhoresOpcoes(r);
            var sp = new StackPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var fx = Link("✕", () => overlaySugerir.Visibility = Visibility.Collapsed, "Fechar (Esc)", 13); fx.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(fx, Dock.Right); cab.Children.Add(fx);
            cab.Children.Add(Tx("Sugestões que passam", 13.5, TemaPlant.BTexto1, FontWeights.SemiBold));
            sp.Children.Add(cab);
            sp.Children.Add(Tx("Preferência: " + (prefFuroMenor ? "furo menor" : "mais leve") + " · com os coeficientes e condições atuais", 11, TemaPlant.BTexto3));
            if (ops.Count == 0)
            {
                var t = Tx(r.TemEsforcos ? "Nenhuma combinação do catálogo passa. Aumente a placa (Ajustar tamanho) ou a quantidade." : "Sem esforços: calcule a estrutura.", 12, TemaPlant.BAlerta, null, true);
                t.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(t);
            }
            int i = 0;
            foreach (var o in ops)
            {
                i++;
                bool atual = DescFix(o.pl, 0) == DescFix(edit, 0) && o.pl.Fileiras == edit.Fileiras && o.pl.PorFileira == edit.PorFileira && Math.Abs(o.pl.Esp - edit.Esp) < 1e-4;
                var lin = new Border
                {
                    Background = atual ? TemaPlant.BAcentoSutil : TemaPlant.BSup2, BorderBrush = atual ? TemaPlant.BAcento : TemaPlant.BBordaSutil, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, i == 1 ? 10 : 6, 0, 0)
                };
                var dp = new DockPanel();
                UIElement acao;
                if (atual) acao = Chip("em uso", Nivel.Info);
                else
                {
                    var pl = o.pl;
                    acao = Botao("Usar", null, () => { edit.Chumbador = pl.Chumbador; edit.Diametro = pl.Diametro; edit.Classe = pl.Classe; edit.Fileiras = pl.Fileiras; edit.PorFileira = pl.PorFileira; edit.Esp = pl.Esp; overlaySugerir.Visibility = Visibility.Collapsed; msgDim = "Sugestão aplicada: " + DescFix(pl, pl.Fileiras * pl.PorFileira) + " · chapa " + FmtEsp(pl.Esp) + " mm."; Recalcular(); }, "Troca a fixação e a chapa por esta combinação.", 26);
                }
                DockPanel.SetDock(acao, Dock.Right); ((FrameworkElement)acao).Margin = new Thickness(10, 0, 0, 0); dp.Children.Add(acao);
                var num = Tx(i.ToString(), 12, TemaPlant.BRealce, FontWeights.Bold); num.Width = 16; num.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(num, Dock.Left); dp.Children.Add(num);
                var st = new StackPanel();
                st.Children.Add(Tx(o.txt, 12.5, TemaPlant.BTexto1, FontWeights.SemiBold, true));
                st.Children.Add(Tx("fixação usa " + Pct(o.ap) + " · chapa " + Num.Fmt(o.kg, 1) + " kg", 11, TemaPlant.BTexto2));
                dp.Children.Add(st);
                lin.Child = dp;
                sp.Children.Add(lin);
            }
            var card = new Border
            {
                Width = 380, Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 12, 14, 14), Child = sp,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            canvasSugerir.Children.Clear();
            canvasSugerir.Children.Add(card);
            Point pt = new Point(ActualWidth - 420, 120);
            if (btSugerir != null && btSugerir.IsLoaded)
            {
                try { pt = btSugerir.TranslatePoint(new Point(btSugerir.ActualWidth, btSugerir.ActualHeight + 6), camadaRaiz); pt.X -= 380; } catch { }
            }
            Canvas.SetLeft(card, Math.Max(8, Math.Min(pt.X, camadaRaiz.ActualWidth - 390)));
            Canvas.SetTop(card, Math.Max(8, pt.Y));
            overlaySugerir.Visibility = Visibility.Visible;
        }

        // Aplicar também em…: outros pés de pilar, pior caso e dimensionar pelo pior caso
        void MontarOverlayAplicarEm()
        {
            var sombra = new Border { Background = TemaPlant.BSombraModal };
            sombra.MouseLeftButtonDown += (s, e) => overlayAplicarEm.Visibility = Visibility.Collapsed;
            overlayAplicarEm.Children.Add(sombra);
            var painel = new Border
            {
                Width = 560, MaxHeight = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            var dp = new DockPanel();
            var cab = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            var lt = new DockPanel();
            var fx = Link("✕", () => overlayAplicarEm.Visibility = Visibility.Collapsed, "Fechar (Esc)", 14); fx.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(fx, Dock.Right); lt.Children.Add(fx);
            lt.Children.Add(Tx("Aplicar esta placa também em…", 15, TemaPlant.BTexto1, FontWeights.SemiBold));
            cab.Children.Add(lt);
            var sub = Tx("Cada nó é avaliado com ESTA placa na sua pior combinação de cargas. Marque os nós que devem receber a mesma placa ao clicar em Aplicar.", 11.5, TemaPlant.BTexto2, null, true);
            sub.Margin = new Thickness(0, 4, 0, 0);
            cab.Children.Add(sub);
            DockPanel.SetDock(cab, Dock.Top);
            dp.Children.Add(cab);
            var rod = new DockPanel { Margin = new Thickness(18, 10, 18, 14) };
            var btOk = B("Concluir", () => overlayAplicarEm.Visibility = Visibility.Collapsed, true, "Fecha esta lista (os nós marcados recebem a placa ao Aplicar).");
            btOk.IsDefault = false; btOk.Height = 30;
            DockPanel.SetDock(btOk, Dock.Right); rod.Children.Add(btOk);
            lblAplicarEm.FontSize = 11.5; lblAplicarEm.Foreground = TemaPlant.BTexto3; lblAplicarEm.TextWrapping = TextWrapping.Wrap; lblAplicarEm.VerticalAlignment = VerticalAlignment.Center;
            rod.Children.Add(lblAplicarEm);
            DockPanel.SetDock(rod, Dock.Bottom);
            dp.Children.Add(rod);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listaAplicarEm, Padding = new Thickness(18, 0, 18, 0) });
            painel.Child = dp;
            overlayAplicarEm.Children.Add(painel);
        }

        void MostrarAplicarEm()
        {
            FecharFlutuantes();
            PreencherAplicarEm();
            overlayAplicarEm.Visibility = Visibility.Visible;
        }

        static bool PassaEm(ResPlaca t, PlacaBase pl) { return t.TemEsforcos ? t.Erro == "" && t.ChOk && t.EspNec <= pl.Esp + 1e-9 : t.Erro.StartsWith("Sem esforços"); }
        static double NotaEm(ResPlaca t, PlacaBase pl) { return !t.TemEsforcos ? 0 : Math.Max(t.ApFix, pl.Esp > 0 ? t.EspNec / pl.Esp : 0); }

        void PreencherAplicarEm()
        {
            listaAplicarEm.Children.Clear();
            if (AvaliarEm == null || Candidatos.Count == 0) { listaAplicarEm.Children.Add(Tx("Não há outros pés de pilar onde esta placa caiba.", 12, TemaPlant.BTexto2)); return; }
            ResPlaca r = CalcEnv();
            var todos = new List<(No no, double nota, bool passa)> { (n, NotaEm(r, edit), PassaEm(r, edit)) };
            todos.AddRange(Candidatos.Select(no => { var t = AvaliarEm(no, edit.Copiar()); return (no, NotaEm(t, edit), PassaEm(t, edit)); }));
            var pior = todos.OrderByDescending(x => x.nota).First();

            var tit = Tx("Pior caso: nó " + pior.no.Id + (pior.no == n ? " (este)" : "") + " — usa " + Pct(pior.nota) + (pior.passa ? "" : ", não passa"), 12.5, pior.passa ? TemaPlant.BTexto1 : TemaPlant.BErro, FontWeights.SemiBold);
            tit.Margin = new Thickness(0, 2, 0, 8);
            listaAplicarEm.Children.Add(tit);
            foreach (var x in todos)
            {
                var no = x.no;
                var g = new Grid { Margin = new Thickness(0, 0, 0, 0), MinHeight = 32 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                string rot = "Nó " + no.Id + (no == n ? " (este)" : no.Placa != null ? " · já tem placa" : " · sem placa");
                FrameworkElement esq;
                if (no == n) { esq = Tx("      " + rot, 12, TemaPlant.BTexto1); }
                else
                {
                    var cb = new CheckBox { Content = Tx(rot, 12, TemaPlant.BTexto1), IsChecked = marcados.Contains(no.Id), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                    cb.Checked += (s, e) => { marcados.Add(no.Id); Recalcular(); };
                    cb.Unchecked += (s, e) => { marcados.Remove(no.Id); Recalcular(); };
                    Dica(cb, "Marque para copiar esta placa para o nó " + no.Id + " ao clicar em Aplicar" + (no.Placa != null ? " (substitui a placa que ele tem)." : "."));
                    esq = cb;
                }
                g.Children.Add(esq);
                var bar = BarraCap(x.nota, x.passa, 6); bar.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(bar, 1); g.Children.Add(bar);
                var pc = Tx(Pct(x.nota), 12, Cores(NivelAp(x.nota, x.passa)).fg, FontWeights.SemiBold); pc.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(pc, 2); g.Children.Add(pc);
                if (no == pior.no) { var tg = Chip("pior caso", x.passa ? Nivel.Alerta : Nivel.Erro, null, null, 10.5); tg.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(tg, 3); g.Children.Add(tg); }
                else if (!x.passa) { var tg = Chip("não passa", Nivel.Erro, null, null, 10.5); tg.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(tg, 3); g.Children.Add(tg); }
                listaAplicarEm.Children.Add(new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = g });
            }
            var bts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
            bool todosMarc = Candidatos.All(c => marcados.Contains(c.Id));
            bts.Children.Add(Botao(todosMarc ? "Desmarcar todos" : "Marcar todos", null, () => { if (todosMarc) marcados.Clear(); else foreach (No c in Candidatos) marcados.Add(c.Id); Recalcular(); }, "Marca ou desmarca todos os nós de apoio onde cabe uma placa."));
            bts.Children.Add(Gap(8));
            bts.Children.Add(Botao("Dimensionar pelo pior caso", "dimens", () => { DimensionarGrupo(); Recalcular(); },
                marcados.Count > 0 ? "Escolhe a menor fixação e chapa que passam neste nó E nos marcados, cada um na sua pior combinação." : "Escolhe a menor fixação e chapa que passam em TODOS os nós da lista, cada um na sua pior combinação."));
            listaAplicarEm.Children.Add(bts);
            lblAplicarEm.Text = marcados.Count > 0 ? "Ao aplicar, a placa vai também para: " + string.Join(", ", Candidatos.Where(c => marcados.Contains(c.Id)).Select(c => "nó " + c.Id)) + "." : (msgDim.StartsWith("Dimensionado pelo pior") ? msgDim : "Nenhum outro nó marcado.");
        }

        // Menor fixação (na preferência ativa) que passa em todos os nós do grupo; chapa pela maior espessura necessária
        void DimensionarGrupo()
        {
            if (AvaliarEm == null) { Dimensionar(); return; }
            var grupo = new List<No> { n };
            grupo.AddRange(marcados.Count > 0 ? Candidatos.Where(c => marcados.Contains(c.Id)) : Candidatos);
            Func<PlacaBase, bool> fixOk = pl => grupo.All(no => { var t = AvaliarEm(no, pl); return t.TemEsforcos ? t.Erro == "" && t.ChOk : t.Erro.StartsWith("Sem esforços"); });
            PlacaBase achou = null;
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cands = (prefFuroMenor ? Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca) : Chumbadores.Lista.OrderBy(c => c.Rosca)).ThenBy(c => c.Hef).ToList();
                foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                {
                    foreach (Chumbador c in cands) { var pl = edit.Copiar(); pl.Chumbador = c.Nome; pl.Fileiras = arr[0]; pl.PorFileira = arr[1]; if (fixOk(pl)) { achou = pl; break; } }
                    if (achou != null) break;
                }
            }
            else
            {
                for (int id = 0; id < Parafusos.D.Length && achou == null; id++)
                    for (int ic = 0; ic < Parafusos.Classes.Length; ic++) { var pl = edit.Copiar(); pl.Diametro = Parafusos.D[id]; pl.Classe = Parafusos.Classes[ic]; if (fixOk(pl)) { achou = pl; break; } }
            }
            if (achou == null) { msgDim = "Nenhuma fixação do catálogo passa em todos os nós escolhidos."; return; }
            double espNec = grupo.Max(no => { var t = AvaliarEm(no, achou); return t.TemEsforcos ? t.EspNec : 0; });
            double ch = PlacaCalc.Chapas.FirstOrDefault(e => e >= espNec - 1e-9);
            achou.Esp = ch > 0 ? ch : PlacaCalc.Chapas.Last();
            edit.Chumbador = achou.Chumbador; edit.Diametro = achou.Diametro; edit.Classe = achou.Classe; edit.Fileiras = achou.Fileiras; edit.PorFileira = achou.PorFileira; edit.Esp = achou.Esp;
            var r = CalcSd();
            if (r.Sec != null) { double h = Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, edit.Eletrodo, edit.Esp); edit.Solda = h > 0 ? h : 25; }
            msgDim = "Dimensionado pelo pior caso entre os nós " + string.Join(", ", grupo.Select(g => g.Id)) + ".";
        }

        // Trocar chumbador: lista agrupada (recomendados, maiores, não servem) com busca e filtro
        void MontarOverlayTrocar()
        {
            var sombra = new Border { Background = TemaPlant.BSombraModal };
            sombra.MouseLeftButtonDown += (s, e) => overlayTrocar.Visibility = Visibility.Collapsed;
            overlayTrocar.Children.Add(sombra);
            var painel = new Border
            {
                Width = 560, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 16, 16),
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            var dp = new DockPanel();
            var cabOv = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
            var linTit = new DockPanel();
            var btFechar = Link("✕", () => overlayTrocar.Visibility = Visibility.Collapsed, "Fechar (Esc)", 14); btFechar.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(btFechar, Dock.Right);
            linTit.Children.Add(btFechar);
            linTit.Children.Add(Tx("Trocar chumbador", 15, TemaPlant.BTexto1, FontWeights.SemiBold));
            cabOv.Children.Add(linTit);

            var boxBusca = new Border { Height = 30, Margin = new Thickness(0, 10, 0, 10), Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
            var gB = new Grid();
            gB.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gB.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icB = (FrameworkElement)Icones.Criar("busca", 14); icB.Margin = new Thickness(8, 0, 4, 0); icB.VerticalAlignment = VerticalAlignment.Center;
            gB.Children.Add(icB);
            var tbBusca = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, Padding = new Thickness(2, 0, 4, 0) };
            var dicaBusca = Tx("Buscar modelo, fabricante ou diâmetro (ex.: 1/2\", WB, WQE)…", 12, TemaPlant.BTextoMudo);
            dicaBusca.Margin = new Thickness(4, 0, 0, 0); dicaBusca.IsHitTestVisible = false;
            tbBusca.TextChanged += (s, e) =>
            {
                filtroBuscaCh = tbBusca.Text ?? "";
                dicaBusca.Visibility = string.IsNullOrEmpty(filtroBuscaCh) ? Visibility.Visible : Visibility.Collapsed;
                PreencherOverlayTrocar();
            };
            tbBusca.GotKeyboardFocus += (s, e) => boxBusca.BorderBrush = TemaPlant.BBordaFoco;
            tbBusca.LostKeyboardFocus += (s, e) => boxBusca.BorderBrush = TemaPlant.BBorda;
            Grid.SetColumn(tbBusca, 1); Grid.SetColumn(dicaBusca, 1);
            gB.Children.Add(tbBusca); gB.Children.Add(dicaBusca);
            boxBusca.Child = gB;
            cabOv.Children.Add(boxBusca);

            btsFiltroTipoCh = new StackPanel { Orientation = Orientation.Horizontal };
            cabOv.Children.Add(btsFiltroTipoCh);
            var bdCab = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = cabOv };
            DockPanel.SetDock(bdCab, Dock.Top);
            dp.Children.Add(bdCab);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listaTrocar });
            painel.Child = dp;
            overlayTrocar.Children.Add(painel);
        }

        void AbrirOverlayTrocar()
        {
            FecharFlutuantes();
            if (string.IsNullOrEmpty(chumbAntes) && !string.IsNullOrEmpty(edit.Chumbador)) chumbAntes = edit.Chumbador;
            famAbertas.Clear();
            Chumbador emUso = Chumbadores.Achar(edit.Chumbador);
            if (emUso != null) famAbertas.Add(emUso.Fabricante + "|" + emUso.Linha);
            PreencherOverlayTrocar();
            overlayTrocar.Visibility = Visibility.Visible;
        }

        void PreencherOverlayTrocar()
        {
            btsFiltroTipoCh.Children.Clear();
            btsFiltroTipoCh.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Todos", filtroTipoCh == 0, () => { filtroTipoCh = 0; PreencherOverlayTrocar(); }, "Mecânicos e químicos."),
                ("Mecânico", filtroTipoCh == 1, () => { filtroTipoCh = 1; PreencherOverlayTrocar(); }, "Chumbadores de expansão: instalação imediata."),
                ("Químico", filtroTipoCh == 2, () => { filtroTipoCh = 2; PreencherOverlayTrocar(); }, "Chumbadores com resina: mais carga, precisa de cura.")
            }));

            listaTrocar.Children.Clear();
            ResPlaca r = CalcFix();
            string q = (filtroBuscaCh ?? "").Trim().ToLowerInvariant();
            var todos = Chumbadores.Lista.Where(c =>
            {
                if (filtroTipoCh == 1 && c.Quimico) return false;
                if (filtroTipoCh == 2 && !c.Quimico) return false;
                if (q != "" && !(c.Nome.ToLowerInvariant().Contains(q) || c.Fabricante.ToLowerInvariant().Contains(q) || c.Pol.ToLowerInvariant().Contains(q) || c.Codigo.ToLowerInvariant().Contains(q)))
                    return false;
                return true;
            }).Select(c => { ResPlaca tc = PlacaCalc.CandidatoChumbador(r, edit, m, c); return new { Ch = c, Res = tc, Ok = tc.ChOk }; }).ToList();

            // Cabeçalho de uma família (linha do fabricante): clique abre/fecha; mostra quantos passam e o menor que passa
            void TituloFamilia(string chave, Chumbador c0, int qt, int qtOk, Chumbador menor, bool aberta)
            {
                var bd = new Border { Padding = new Thickness(12, 9, 16, 9), BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Background = TemaPlant.BSup2, Cursor = Cursors.Hand };
                bd.MouseEnter += (s, e) => bd.Background = TemaPlant.BSupHover;
                bd.MouseLeave += (s, e) => bd.Background = TemaPlant.BSup2;
                var dp = new DockPanel();
                var dir = new TextBlock { FontSize = 11.5, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center };
                dir.Inlines.Add(new System.Windows.Documents.Run(qt + (qt == 1 ? " modelo · " : " modelos · ")));
                dir.Inlines.Add(new System.Windows.Documents.Run(qtOk + " passa" + (qtOk == 1 ? "" : "m")) { Foreground = qtOk > 0 ? TemaPlant.BSucesso : TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
                if (menor != null && !aberta) dir.Inlines.Add(new System.Windows.Documents.Run(" · menor: " + menor.Pol + " hef " + menor.Hef.ToString("0")));
                DockPanel.SetDock(dir, Dock.Right); dp.Children.Add(dir);
                var esq = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                esq.Inlines.Add(new System.Windows.Documents.Run(aberta ? "▾  " : "▸  ") { Foreground = TemaPlant.BTexto2 });
                esq.Inlines.Add(new System.Windows.Documents.Run(c0.Linha.Replace("Walsywa ", "")) { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BTexto1 });
                esq.Inlines.Add(new System.Windows.Documents.Run("   " + c0.Fabricante + " · " + (c0.Quimico ? "químico" : "mecânico")) { FontSize = 11.5, Foreground = TemaPlant.BTexto2 });
                dp.Children.Add(esq);
                bd.Child = dp;
                bd.MouseLeftButtonDown += (s, e) => { if (!famAbertas.Remove(chave)) famAbertas.Add(chave); PreencherOverlayTrocar(); e.Handled = true; };
                listaTrocar.Children.Add(bd);
            }
            void LinhaCandidato(Chumbador c, ResPlaca tc, bool ok, bool mostraDiam, bool menorQuePassa)
            {
                bool emUso = r.Ch == c;
                bool eraAntes = !emUso && !string.IsNullOrEmpty(chumbAntes) && (c.Nome == chumbAntes || c.Codigo == chumbAntes);
                var bd = new Border
                {
                    Background = emUso ? TemaPlant.BAcentoSutil : Brushes.Transparent, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(emUso ? 3 : 0, 0, 0, 1),
                    Padding = new Thickness(emUso ? 13 : 16, 8, 16, 8), Cursor = Cursors.Hand
                };
                if (!emUso)
                {
                    bd.MouseEnter += (s, e) => bd.Background = TemaPlant.BSup2;
                    bd.MouseLeave += (s, e) => bd.Background = Brushes.Transparent;
                }
                if (emUso) bd.BorderBrush = TemaPlant.BAcento;
                var dp = new DockPanel();
                FrameworkElement dir = emUso ? (FrameworkElement)Chip("em uso", Nivel.Info, null, null, 10.5) : Tx(ok ? Pct(tc.ApFix) : "✕", 12, ok ? Cores(NivelAp(tc.ApFix, true)).fg : TemaPlant.BErro, FontWeights.SemiBold);
                dir.VerticalAlignment = VerticalAlignment.Center; dir.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(dir, Dock.Right); dp.Children.Add(dir);
                // o diâmetro só aparece na primeira linha de cada diâmetro: a lista fica "família → diâmetro → hef"
                var diam = Tx(mostraDiam ? c.Pol : "", 14, ok ? TemaPlant.BTexto1 : TemaPlant.BTexto3, FontWeights.SemiBold); diam.Width = 50;
                DockPanel.SetDock(diam, Dock.Left); dp.Children.Add(diam);
                var meio = new StackPanel();
                var lin1 = new WrapPanel();
                lin1.Children.Add(Tx("hef " + c.Hef.ToString("0") + " mm", 12.5, ok ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeights.SemiBold));
                lin1.Children.Add(Tx("  furo Ø" + Num.Fmt(c.Furo, c.Furo == Math.Floor(c.Furo) ? 0 : 1) + " × " + Num.Fmt(c.Hef, 0), 11.5, TemaPlant.BTexto2));
                if (menorQuePassa) { var tg = Chip("menor que passa", Nivel.Ok, "O menor desta família que passa nesta placa.", null, 10); tg.Margin = new Thickness(8, 0, 0, 0); lin1.Children.Add(tg); }
                if (eraAntes) lin1.Children.Add(Tx("  · antes", 11.5, TemaPlant.BAlerta, FontWeights.SemiBold));
                meio.Children.Add(lin1);
                if (!ok)
                {
                    string motivo;
                    if (!tc.EspacOk) motivo = "não cabe: furos a " + Num.Fmt(tc.EspacMin, 0) + " mm < mín. " + Num.Fmt(Chumbadores.EspacMinimo(c), 0) + " mm";
                    else if (c.Quimico && tc.NAco < tc.NSeg && tc.NComb >= tc.NRef) motivo = "aço da barra: segurança " + Num.Fmt(tc.NAco, 1) + " < " + Num.Fmt(tc.NSeg, 1) + " exigida";
                    else motivo = "segurança " + FmtN(tc.NComb, 1) + " < " + Num.Fmt(tc.NRef, 1) + " exigida";
                    var tm = Tx(motivo, 11, TemaPlant.BErro); tm.Margin = new Thickness(0, 2, 0, 0);
                    meio.Children.Add(tm);
                }
                else
                {
                    var br = BarraCap(tc.ApFix, true, 4); br.Margin = new Thickness(0, 5, 0, 0);
                    meio.Children.Add(br);
                }
                dp.Children.Add(meio);
                bd.Child = dp;
                bd.MouseLeftButtonDown += (s, e) => { edit.Chumbador = c.Nome; overlayTrocar.Visibility = Visibility.Collapsed; Recalcular(); e.Handled = true; };
                listaTrocar.Children.Add(bd);
            }
            // Agrupado por família (linha do fabricante) e, dentro dela, por diâmetro e hef. A família em uso vem
            // primeiro e aberta; as outras fechadas, mostrando quantas passam e a menor que passa. Com busca, tudo aberto.
            bool buscando = q != "";
            var familias = todos.GroupBy(x => x.Ch.Fabricante + "|" + x.Ch.Linha)
                .OrderByDescending(gr => r.Ch != null && gr.Any(x => x.Ch == r.Ch))
                .ThenByDescending(gr => gr.Any(x => x.Ok))
                .ThenBy(gr => gr.First().Ch.Quimico)
                .ThenBy(gr => gr.Min(x => x.Ch.Rosca))
                .ToList();
            foreach (var gr in familias)
            {
                var itens = gr.OrderBy(x => x.Ch.Rosca).ThenBy(x => x.Ch.Hef).ToList();
                var menor = itens.FirstOrDefault(x => x.Ok);
                bool aberta = buscando || famAbertas.Contains(gr.Key);
                TituloFamilia(gr.Key, itens[0].Ch, itens.Count, itens.Count(x => x.Ok), menor?.Ch, aberta);
                if (!aberta) continue;
                double roscaAnt = -1;
                foreach (var x in itens)
                {
                    LinhaCandidato(x.Ch, x.Res, x.Ok, x.Ch.Rosca != roscaAnt, menor != null && x.Ch == menor.Ch);
                    roscaAnt = x.Ch.Rosca;
                }
            }
            if (todos.Count == 0) { var t = Tx("Nenhum chumbador com esse filtro.", 12, TemaPlant.BTexto2); t.Margin = new Thickness(16, 14, 16, 14); listaTrocar.Children.Add(t); }
        }

        // ================= peças auxiliares =================
        // Régua de valores clicáveis (verde = passa, vermelho = não passa, branco = atual)
        static Grid Regua(int count, Func<int, (string rotulo, bool passa, bool atual, string dica, Action clicar)> item)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var inf = item(i);
                var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0) };
                Dica(sp, inf.dica);
                sp.Children.Add(new Border
                {
                    Height = inf.atual ? 7 : 5, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, inf.atual ? 0 : 1, 2, 2),
                    Background = inf.atual ? TemaPlant.BTexto1 : inf.passa ? TemaPlant.BSucesso : TemaPlant.BErroBorda
                });
                sp.Children.Add(new TextBlock
                {
                    Text = inf.rotulo, FontSize = 10, FontWeight = inf.atual ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = inf.atual ? TemaPlant.BTexto1 : inf.passa ? TemaPlant.BTexto2 : TemaPlant.BTextoMudo, HorizontalAlignment = HorizontalAlignment.Center
                });
                Action ac = inf.clicar;
                sp.MouseLeftButtonDown += (s, e) => { ac(); e.Handled = true; };
                Grid.SetColumn(sp, i);
                g.Children.Add(sp);
            }
            return g;
        }

        static readonly double[] ValoresN = { 0, 1.5, 1.67, 2, 2.5, 3, 4, 5 };
        static readonly double[] ValoresNCh = { 0, 2, 3, 4, 5, 7, 10, 15 };

        Action PassoValor(double[] vals, double atual, int dir, Action<double> set)
        {
            int i = Array.FindIndex(vals, v => Math.Abs(v - atual) < 1e-6);
            if (dir < 0)
            {
                if (i > 0) return () => { set(vals[i - 1]); Recalcular(); };
                if (i < 0 && atual > 0) return () => { set(vals.LastOrDefault(v => v < atual)); Recalcular(); };
                return null;
            }
            if (i >= 0 && i + 1 < vals.Length) return () => { set(vals[i + 1]); Recalcular(); };
            if (i < 0 && atual < vals.Last()) return () => { set(vals.First(v => v > atual)); Recalcular(); };
            return null;
        }

        // Tabela compacta para a coluna estreita: grandeza | valor (a expressão vai na dica)
        static UIElement TabelaCompacta(List<(string rot, string expr, string val, int est)> linhas)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int i = 0;
            foreach (var l in linhas)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var fundo = new Border { Background = i % 2 == 0 ? TemaPlant.BSup1 : Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                Dica(fundo, string.IsNullOrEmpty(l.expr) ? null : l.expr);
                Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 2); g.Children.Add(fundo);
                var a = Tx(l.rot, 11.5, TemaPlant.BTexto2, null, true); a.Margin = new Thickness(8, 4, 8, 4); a.IsHitTestVisible = false;
                Grid.SetRow(a, i); g.Children.Add(a);
                Brush cor = l.est > 0 ? TemaPlant.BSucesso : l.est < 0 ? TemaPlant.BErro : TemaPlant.BTexto1;
                var b = Tx((l.est > 0 ? "✓ " : l.est < 0 ? "✕ " : "") + l.val, 11.5, cor, l.est != 0 ? FontWeights.SemiBold : FontWeights.Normal, true);
                b.TextAlignment = TextAlignment.Right; b.HorizontalAlignment = HorizontalAlignment.Right; b.Margin = new Thickness(0, 4, 8, 4); b.MaxWidth = 220; b.IsHitTestVisible = false;
                Grid.SetRow(b, i); Grid.SetColumn(b, 1); g.Children.Add(b);
                i++;
            }
            return g;
        }

        // A espessura necessária já inclui n e a flexão cresce com t², então a segurança real de uma chapa t é n·(t/tnec)²
        string SegChapa(ResPlaca r, double t)
        {
            if (r.EspNec <= 0) return "∞";
            return Num.Fmt(r.NSeg * (t / r.EspNec) * (t / r.EspNec), 2);
        }

        // ---------- linhas de cálculo (grandeza, expressão, valor, estado) ----------
        List<(string, string, string, int)> CalcFix(ResPlaca r)
        {
            var l = new List<(string, string, string, int)>();
            if (!r.TemEsforcos) { l.Add(("Sem verificação", "", r.Erro, 0)); return l; }
            l.Add(("Esforços no pé do pilar", "combinação crítica, eixos da placa", "N = " + Num.Fmt(r.N, 2) + " · V = " + Num.Fmt(r.V, 2) + " kN · M = " + Num.Fmt(r.M, 2) + " kN·m", 0));
            l.Add(("Tração no mais carregado", "placa rígida: " + (string.IsNullOrEmpty(r.Modelo) ? "" : char.ToLowerInvariant(r.Modelo[0]) + r.Modelo.Substring(1)), Num.Fmt(r.Tmax, 2) + " kN", 0));
            l.Add(("Corte por fixação", "Fv = V / " + r.Paraf.Count, Num.Fmt(r.Fv, 2) + " kN", 0));
            if (r.Ch != null)
            {
                var c = r.Ch;
                l.Add(("Carga do catálogo", c.Tu > 0 ? "Tu (ruptura) e Vu, concreto do ensaio fck = " + Num.Fmt(c.Fck, 0) + " MPa" : "Trec e Vrec (recomendadas)",
                    (c.Tu > 0 ? Num.Fmt(c.TuKN, 2) : Num.Fmt(c.TuRecKN, 2)) + " / " + (c.Tu > 0 ? (c.Vu > 0 ? Num.Fmt(c.VuKN, 2) : "—") : Num.Fmt(c.VuRecKN, 2)) + " kN", 0));
                l.Add(("Redução pelo concreto ψc", "min(1; √(fck laje / fck ensaio)) = √(" + Num.Fmt(r.FckLaje, 0) + " / " + Num.Fmt(c.Fck, 0) + ")", Num.Fmt(r.FFck, 3), 0));
                l.Add(("Cones sobrepostos ψ", "área da união dos cones 3·hef / (n · 9·hef²)", Num.Fmt(r.PsiCone, 3), 0));
                l.Add(("Tração de referência", "Tref = T,cat · ψc · ψ", Num.Fmt(r.Tref, 2) + " kN", 0));
                l.Add(("Corte de referência", "Vref = V,cat", r.Vref > 0 ? Num.Fmt(r.Vref, 2) + " kN" : "no aço da barra", 0));
                l.Add(("Coeficiente exigido", c.Tu > 0 ? (edit.CoefChumbador > 0 ? "digitado" : "tabela Walsywa: " + Chumbadores.NomesTipos[r.TipoCarga] + ", " + (r.RiscoAlto ? "alto" : "baixo") + " risco") : "carga recomendada → n = 1", "n = " + Num.Fmt(r.NRef, 1), 0));
                l.Add(("Segurança no concreto", "n = [(T/Tref)^5/3 + (V/Vref)^5/3]^−3/5", FmtN(r.NComb) + (r.NComb >= r.NRef ? " ≥ " : " < ") + Num.Fmt(r.NRef, 1), r.NComb >= r.NRef ? 1 : -1));
                if (c.Quimico)
                    l.Add(("Aço da barra (cl. " + r.ClasseBarra + ")", "Sp / √(σ² + 3τ²) = " + Num.Fmt(r.SpBarra, 0) + " / √(" + Num.Fmt(r.Sigma, 0) + "² + 3·" + Num.Fmt(r.Tau, 0) + "²)", FmtN(r.NAco) + (r.NAco >= r.NSeg ? " ≥ " : " < ") + Num.Fmt(r.NSeg, 1), r.NAco >= r.NSeg ? 1 : -1));
                l.Add(("Espaçamento entre furos", "s ≥ 6·d = " + Num.Fmt(Chumbadores.EspacMinimo(c), 0) + " mm (sem redução a partir de 3·hef = " + Num.Fmt(c.S > 0 ? c.S : 3 * c.Hef, 0) + " mm)", Num.Fmt(r.EspacMin, 0) + " mm", r.EspacOk ? 1 : -1));
            }
            else
            {
                l.Add(("Tensão normal", "σ = Tmáx / At", Num.Fmt(r.Sigma, 0) + " MPa", 0));
                l.Add(("Tensão de corte", "τ = Fv / " + (edit.RoscaNoCorte ? "Ar (rosca)" : "Ad (fuste)"), Num.Fmt(r.Tau, 0) + " MPa", 0));
                l.Add(("Segurança do parafuso", "n = Sp / √(σ² + 3τ²), Sp = " + Num.Fmt(r.Sp, 0) + " MPa", FmtN(r.NComb) + (r.NComb >= r.NExig ? " ≥ " : " < ") + Num.Fmt(r.NExig, 2), r.NComb >= r.NExig ? 1 : -1));
                l.Add(("Pré-carga / torque", "Fi = 0,75·At·Sp · T = 0,2·Fi·d", Num.Fmt(r.Fi, 1) + " kN / " + Num.Fmt(r.Torque, 0) + " N·m", 0));
            }
            return l;
        }

        List<(string, string, string, int)> CalcChapa(ResPlaca r)
        {
            var l = new List<(string, string, string, int)>();
            if (!r.TemEsforcos) { l.Add(("Sem verificação", "", r.Erro, 0)); return l; }
            if (r.EspNec <= 0) { l.Add(("Flexão da chapa", "sem tração nos furos e sem balanço comprimido", "não trabalha", 1)); }
            else
            {
                if (r.EspNecT > 0)
                {
                    l.Add(("Fileira crítica", "tração somada da fileira", Num.Fmt(r.TLinha, 2) + " kN", 0));
                    l.Add(("Braço até o perfil", "a = |y| − h/2 (≥ d)", Num.Fmt(r.Braco, 0) + " mm", 0));
                    l.Add(("Largura efetiva", "b = mín(B; n · (2a + d))", Num.Fmt(r.LargEf, 0) + " mm", 0));
                    l.Add(("t pelo lado tracionado", "t = √(6 · T · a · n / (b · fy))", Num.Fmt(r.EspNecT, 1) + " mm", 0));
                }
                if (r.EspNecC > 0)
                    l.Add(("t pelo lado comprimido", "t = √(6 · C · e · n / (B · fy)), C = " + Num.Fmt(r.CConc, 1) + " kN, e = " + Num.Fmt(r.BracoC, 0) + " mm", Num.Fmt(r.EspNecC, 1) + " mm", 0));
                l.Add(("Espessura necessária", "o maior dos dois lados", Num.Fmt(r.EspNec, 1) + " mm", 0));
            }
            l.Add(("Chapa adotada", "t ≥ t,nec · segurança real n·(t/t,nec)² = " + SegChapa(r, edit.Esp), FmtEsp(edit.Esp) + " mm", ChapaOk(r) ? 1 : -1));
            l.Add(("Coeficiente / aço", "n da placa · fy da chapa", Num.Fmt(r.NSeg, 2) + " · " + Num.Fmt(r.FyChapa, 0) + " MPa", 0));
            return l;
        }

        static List<(string, string, string, int)> CalcSolda(ResSolda sd)
        {
            var l = new List<(string, string, string, int)>();
            if (sd == null) { l.Add(("Solda", "", "sem esforços", 0)); return l; }
            if (sd.Erro != "") { l.Add(("Solda", "", sd.Erro, -1)); return l; }
            l.Add(("Grupo de solda", "filete em todo o contorno (Shigley)", sd.Grupo, 0));
            l.Add(("Área da garganta", "A = 1,414 · h · (b + d)  ou  1,414 · π · h · r", Num.Fmt(sd.A, 0) + " mm²", 0));
            l.Add(("Corte primário", "τ' = V / A", Num.Fmt(sd.Tau1, 1) + " MPa", 0));
            l.Add(("Normal na garganta", "τ'' = N / A + M · c / I,  I = 0,707 · h · Iu", Num.Fmt(sd.Tau2, 1) + " MPa", 0));
            l.Add(("Tensão resultante", "τ = √(τ'² + τ''²) ≤ τ adm do eletrodo", Num.Fmt(sd.Tau, 0) + (sd.Tau <= sd.TauAdm + 1e-9 ? " ≤ " : " > ") + Num.Fmt(sd.TauAdm, 0) + " MPa", sd.Tau <= sd.TauAdm + 1e-9 ? 1 : -1));
            if (sd.HMinNorma > 0) l.Add(("Perna mínima de norma", "NBR 8800 Tab. 10, pela peça mais fina", Num.Fmt(sd.HMinNorma, 0) + " mm", sd.AbaixoMinimo ? -1 : 1));
            if (sd.Espessura > 0) l.Add(("Parede do perfil", "perna ≤ parede", Num.Fmt(sd.Espessura, 2) + " mm", sd.MaiorQueParede ? -1 : 1));
            l.Add(("Cordão total / necessário", "L = A / (0,707 · h)", Num.Fmt(sd.L, 0) + " / " + Num.Fmt(sd.LNec, 0) + " mm", 0));
            l.Add(("Metal depositado", "h² / 2 · L · 7850", "≈ " + Num.Fmt(sd.Massa, 2) + " kg", 0));
            return l;
        }

        // ---------- aba Cálculo: tabelas legíveis ----------
        FrameworkElement AbaCalculo(ResPlaca r, ResSolda sd)
        {
            var sp = new StackPanel { Margin = new Thickness(18, 4, 18, 18) };
            if (!r.TemEsforcos)
            {
                sp.Children.Add(TituloSecao("Sem verificação", null, 12));
                sp.Children.Add(Tx(r.Erro, 12.5, TemaPlant.BTexto2, null, true));
                return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }
            UIElement Cab(string nome, double ap, bool ok)
            {
                var d = new StackPanel { Orientation = Orientation.Horizontal };
                var b = BarraCap(ap, ok, 6); b.Width = 120; b.Margin = new Thickness(0, 0, 10, 0);
                d.Children.Add(b);
                d.Children.Add(Tx(Pct(ap), 12, Cores(NivelAp(ap, ok)).fg, FontWeights.Bold));
                var ch = Chip(ok && ap <= 1 + 1e-9 ? "✓ passa" : "✕ não passa", NivelAp(ap, ok), null, null, 10.5); ch.Margin = new Thickness(10, 0, 0, 0);
                d.Children.Add(ch);
                return d;
            }
            sp.Children.Add(TituloSecao("1 · Fixação — " + (r.Ch != null ? r.Paraf.Count + " × " + r.Ch.Nome + " (" + r.Ch.Fabricante + ")" : r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe), Cab("", r.ApFix, FixOk(r)), 12));
            sp.Children.Add(TabelaCalc(CalcFix(r)));
            sp.Children.Add(TituloSecao("2 · Chapa " + FmtEsp(edit.Esp) + " mm", Cab("", ApChapa(r), ChapaOk(r)), 16));
            sp.Children.Add(TabelaCalc(CalcChapa(r)));
            if (sd != null)
            {
                sp.Children.Add(TituloSecao("3 · Solda pilar–placa — filete " + Num.Fmt(sd.H, 0) + " mm, " + (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX", Cab("", sd.Ap, sd.Passa), 16));
                sp.Children.Add(TabelaCalc(CalcSolda(sd)));
            }
            var nota = Tx((r.Ch != null ? "Cargas de catálogo valem para o concreto indicado e as distâncias mínimas do fabricante (borda do concreto: conferir na obra). " : "") +
                          "Não verifica pressão no concreto nem ancoragem." + (r.Inclinada ? " Barra inclinada: chapa e solda acima valem para o pilar perpendicular; detalhar a ligação à parte." : ""), 11.5, TemaPlant.BTexto3, null, true);
            nota.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(nota);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---------- aba Ficha de fabricação ----------
        FrameworkElement Ficha(ResPlaca r, ResSolda sd)
        {
            FrameworkElement Bloco(string titulo, List<(string k, string v)> linhas)
            {
                var sp = new StackPanel();
                sp.Children.Add(TituloSecao(titulo, null, 0));
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                int i = 0;
                foreach (var l in linhas)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var fundo = new Border { Background = i % 2 == 0 ? TemaPlant.BSup1 : Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                    Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 2); g.Children.Add(fundo);
                    var a = Tx(l.k, 12, TemaPlant.BTexto2, null, true); a.Margin = new Thickness(8, 4, 8, 4); Grid.SetRow(a, i); g.Children.Add(a);
                    var b = Tx(l.v, 12, TemaPlant.BTexto1, null, true); b.Margin = new Thickness(0, 4, 8, 4); Grid.SetRow(b, i); Grid.SetColumn(b, 1); g.Children.Add(b);
                    i++;
                }
                sp.Children.Add(g);
                return new Border { Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 10), Child = sp };
            }
            double furo = r.Dia + (r.Dia <= 24 ? 2 : 3);
            var chapa = new List<(string, string)>
            {
                ("Dimensões", Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm (no plano × fora × espessura)"),
                ("Aço", "fy = " + Num.Fmt(r.FyChapa, 0) + " MPa"),
                ("Massa", Num.Fmt(r.H * r.B * edit.Esp * 7850e-9, 2) + " kg"),
                ("Espessura necessária", Num.Fmt(r.EspNec, 1) + " mm · segurança " + SegChapa(r, edit.Esp)),
                ("Furos na chapa", r.Paraf.Count + " × Ø" + Num.Fmt(furo, 0) + " mm (folga padrão)"),
                ("Arranjo", Pl.N(edit.Fileiras, "fileira", "fileiras") + " × " + edit.PorFileira + " · borda " + Num.Fmt(edit.Borda, 0) + " mm até o centro do furo"),
                ("Posições (y; z) mm", string.Join("   ", r.Paraf.Select(p => "(" + Num.Fmt(p[0], 0) + "; " + Num.Fmt(p[1], 0) + ")"))),
            };
            var fix = new List<(string, string)>();
            if (r.Ch != null)
            {
                fix.Add(("Fixação", r.Paraf.Count + " × chumbador " + r.Ch.Nome + " (" + r.Ch.Fabricante + ")" + (r.Ch.Quimico ? " · barra classe " + r.ClasseBarra : "")));
                fix.Add(("Furo no concreto", "Ø" + Num.Fmt(r.Ch.Furo, 1) + " mm × " + Num.Fmt(r.Ch.Hef, 0) + " mm de embutimento"));
                fix.Add(("Distâncias", "entre chumbadores " + Num.Fmt(r.EspacMin, 0) + " mm (mín. 6·d = " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + " mm) · até a borda do concreto ≥ " + Num.Fmt(r.Ch.C > 0 ? r.Ch.C : 1.5 * r.Ch.Hef, 0) + " mm (conferir na obra)"));
                fix.Add(("Aperto", r.Torque > 0 ? "torque " + Num.Fmt(r.Ch.Torque, 1) + " kgf·m (" + Num.Fmt(r.Torque, 0) + " N·m), do catálogo" : "conforme o fabricante (catálogo não informa)"));
                fix.Add(("Coeficiente", "n = " + Num.Fmt(r.NRef, 1) + (r.Ch.Tu > 0 ? " sobre a carga última" : " (carga recomendada)") + (r.Ch.Quimico ? " · aço da barra n = " + Num.Fmt(r.NSeg, 1) : "")));
            }
            else
            {
                fix.Add(("Fixação", r.Paraf.Count + " × M" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe + (edit.RoscaNoCorte ? " (rosca no plano de corte)" : "")));
                fix.Add(("Aperto", "pré-carga " + Num.Fmt(r.Fi, 1) + " kN · torque " + Num.Fmt(r.Torque, 0) + " N·m"));
            }
            fix.Add(("Maior tração / corte", Num.Fmt(r.Tmax, 2) + " kN / " + Num.Fmt(r.Fv, 2) + " kN por fixação"));
            fix.Add(("Segurança", FmtN(r.NComb)));
            var solda = new List<(string, string)>();
            if (sd != null && sd.Erro == "")
            {
                solda.Add(("Tipo", "filete contínuo em todo o contorno do perfil (" + sd.Grupo + ")"));
                solda.Add(("Eletrodo", (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX · τ adm " + Num.Fmt(sd.TauAdm, 0) + " MPa"));
                solda.Add(("Perna / garganta", Num.Fmt(sd.H, 0) + " mm / " + Num.Fmt(0.707 * sd.H, 1) + " mm" + (sd.HMinNorma > 0 ? " · mínima de norma " + Num.Fmt(sd.HMinNorma, 0) + " mm" : "")));
                solda.Add(("Comprimento", Num.Fmt(sd.L, 0) + " mm de cordão (necessário " + Num.Fmt(sd.LNec, 0) + " mm)"));
                solda.Add(("Metal depositado", "≈ " + Num.Fmt(sd.Massa, 2) + " kg"));
                solda.Add(("Situação", sd.Passa ? "passa (τ = " + Num.Fmt(sd.Tau, 0) + " MPa)" : "NÃO PASSA — ver passo 3"));
            }
            var esf = new List<(string, string)>
            {
                ("Esforços no pé", "N = " + Num.Fmt(r.N, 2) + " kN · V = " + Num.Fmt(r.V, 2) + " kN · M = " + Num.Fmt(r.M, 2) + " kN·m"),
                ("Pilar", r.Sec != null ? r.Sec.Nome : "—"),
                ("Coef. da placa", "n = " + Num.Fmt(r.NSeg, 2) + (edit.Seguranca > 0 ? " (desta placa)" : " (do modelo)"))
            };
            var g2 = new Grid { Margin = new Thickness(14, 12, 14, 4) };
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c1 = new StackPanel(); var c2 = new StackPanel();
            c1.Children.Add(Bloco("Chapa", chapa));
            c1.Children.Add(Bloco("Esforços e projeto", esf));
            c2.Children.Add(Bloco(r.Ch != null ? "Chumbadores" : "Parafusos", fix));
            if (solda.Count > 0) c2.Children.Add(Bloco("Solda pilar–placa", solda));
            Grid.SetColumn(c2, 2);
            g2.Children.Add(c1); g2.Children.Add(c2);
            return new ScrollViewer { Content = g2, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---------- Sugerir: até 3 combinações fixação + chapa que passam ----------
        ResPlaca Testar(PlacaBase pl) { n.Placa = pl; try { return CalcEnv(); } finally { n.Placa = edit; } }

        static string DescFix(PlacaBase p, int qt)
        {
            var c = Chumbadores.Achar(p.Chumbador);
            return qt + " × " + (c != null ? NomeCurtoCh(c) : "M" + Num.Fmt(p.Diametro, 0) + " cl. " + p.Classe);
        }

        List<(PlacaBase pl, string txt, double kg, double ap)> MelhoresOpcoes(ResPlaca r)
        {
            var res = new List<(PlacaBase, string, double, double)>();
            if (!r.TemEsforcos) return res;
            void Add(PlacaBase pl, ResPlaca t)
            {
                double esp = PlacaCalc.Chapas.FirstOrDefault(e => e >= t.EspNec - 1e-9);
                if (esp <= 0) return;
                pl.Esp = esp;
                res.Add((pl, DescFix(pl, t.Paraf.Count) + " · chapa " + FmtEsp(esp) + " mm", t.H * t.B * esp * 7850e-9, t.ApFix));
            }
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cands = (prefFuroMenor ? Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca) : Chumbadores.Lista.OrderBy(c => c.Rosca)).ThenBy(c => c.Hef).ToList();
                foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                {
                    foreach (Chumbador c in cands)
                    {
                        if (res.Count >= 3) break;
                        var pl = edit.Copiar(); pl.Chumbador = c.Nome; pl.Fileiras = arr[0]; pl.PorFileira = arr[1];
                        var t = Testar(pl);
                        if (t.Erro == "" && t.TemEsforcos && t.ChOk && !res.Any(x => x.Item1.Chumbador == c.Nome)) Add(pl, t);
                    }
                    if (res.Count >= 3) break;
                }
            }
            else
            {
                for (int id = 0; id < Parafusos.D.Length && res.Count < 3; id++)
                    for (int ic = 0; ic < Parafusos.Classes.Length; ic++)
                    {
                        var pl = edit.Copiar(); pl.Diametro = Parafusos.D[id]; pl.Classe = Parafusos.Classes[ic];
                        var t = Testar(pl);
                        if (t.Erro == "" && t.TemEsforcos && t.ChOk) { Add(pl, t); break; }
                    }
            }
            return res;
        }

        // ---------- Ajustar tamanho da chapa ----------
        // Regras de espaçamento (múltiplos do diâmetro d da fixação) e do catálogo de chumbadores (6·d para instalar; 3·hef sem sobrepor cones).
        const double KBorda = 1.5;      // centro do furo até a borda da chapa ≥ KBorda·d
        const double KPasso = 3.0;      // entre centros de furos ≥ KPasso·d
        const double KChave = 1.5;      // centro do furo até a face do perfil ≥ KChave·d + folga (espaço para a chave)
        const double FolgaChave = 10;   // mm

        string TextoRegrasTamanho()
        {
            return "Calcula o menor comprimento, largura e borda da chapa, arredondados de 10 em 10 mm:\n" +
                   "• centro do furo até a borda da chapa ≥ " + Num.Fmt(KBorda, 1) + "·d\n" +
                   "• entre centros de furos ≥ " + Num.Fmt(KPasso, 1) + "·d (chumbador: também ≥ 6·d para instalar" + (semSobreporCones ? " e ≥ 3·hef" : "") + ")\n" +
                   "• centro do furo até a face do perfil ≥ " + Num.Fmt(KChave, 1) + "·d + " + Num.Fmt(FolgaChave, 0) + " mm (espaço para a chave)\n" +
                   "Mantém o número de fileiras e de fixações por fileira. Depois confira a espessura.\n" +
                   "Fonte: os slides de Elementos de Máquina (Aula 2) não trazem passo nem margem mínimos; dão a porca com 2·d entre quinas\n" +
                   "e a arruela com 1,5·d (págs. 18 e 49), de onde vem o espaço para a chave. Borda 1,5·d e passo 3·d são a prática usual adotada.";
        }

        void AjustarTamanho()
        {
            ResPlaca r = CalcFix();
            if (r.RefIn <= 0) return;
            Chumbador c = Chumbadores.Achar(edit.Chumbador);
            double d = c != null ? c.Rosca : edit.Diametro;
            double borda = Math.Ceiling(KBorda * d / 5) * 5;
            double passo = KPasso * d;
            if (c != null) { passo = Math.Max(passo, Chumbadores.EspacMinimo(c)); if (semSobreporCones) passo = Math.Max(passo, 3 * c.Hef); }
            double aoPerfil = KChave * d + FolgaChave;
            int nf = Math.Max(1, edit.Fileiras), np = Math.Max(1, edit.PorFileira);
            double h = Math.Max(r.RefIn + 2 * (aoPerfil + borda), 2 * borda + (nf - 1) * passo);
            double b = Math.Max(r.RefOut + 2 * borda, 2 * borda + (np - 1) * passo);
            if (nf == 1) h = Math.Max(h, r.RefIn + 2 * borda);
            edit.PeloPerfil = false;
            edit.H = Math.Ceiling(h / 10) * 10; edit.B = Math.Ceiling(b / 10) * 10; edit.Borda = borda;
            msgDim = "Tamanho ajustado: " + Num.Fmt(edit.H, 0) + " × " + Num.Fmt(edit.B, 0) + " mm, borda " + Num.Fmt(borda, 0) + " mm (d = " + Num.Fmt(d, 1) + " mm, passo ≥ " + Num.Fmt(passo, 0) + " mm).";
        }
    }
}
