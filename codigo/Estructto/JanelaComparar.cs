// Janela de comparacao lado a lado de fixacoes (chumbadores mecanicos, quimicos e linhas informativas).
// Dados embutidos: fixacoes.csv (tabela 5.1 e secao 2 do guia) e fixacoes-pares.csv (secao 5.2 do guia).
// Verificacao estrutural: usa exclusivamente PlacaCalc.CandidatoChumbador(r, p, m, c).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public class JanelaComparar : JanelaEscura
    {
        public string ChumbadorEscolhido { get; private set; }

        readonly Modelo m;
        readonly No n;
        readonly ResPlaca r;
        readonly PlacaBase p;
        readonly List<string> selecionados = new List<string>();

        readonly StackPanel listaSeletor = new StackPanel();
        readonly StackPanel btsFiltro = new StackPanel { Orientation = Orientation.Vertical };
        readonly StackPanel areaAvisos = new StackPanel();
        readonly StackPanel painelColunas = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel areaPares = new StackPanel();
        readonly Border badgeContagem = new Border();
        readonly TextBlock lblContagem = new TextBlock();

        string filtroBusca = "";
        int filtroTipo; // 0 = Todas, 1 = Mecânico, 2 = Químico, 3 = Só passam, 4 = Informativas

        public JanelaComparar(Modelo m, No n, ResPlaca r, PlacaBase p) : this(m, n, r, p, null) { }

        public JanelaComparar(Modelo m, No n, ResPlaca r, PlacaBase p, IEnumerable<string> iniciais)
        {
            this.m = m;
            this.n = n;
            this.p = p ?? (n != null ? n.Placa : null) ?? new PlacaBase { PeloPerfil = true, Sobra = 100, H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", FyChapa = 250 };
            this.r = r ?? (m != null && n != null ? PlacaCalc.Calcular(m, n) : new ResPlaca());

            Title = "Comparar fixações" + (n != null ? " — nó " + n.Id : "");
            Width = 1340;
            Height = 860;
            MinWidth = 980;
            MinHeight = 620;

            InicializarSelecao(iniciais);

            var raiz = new DockPanel();

            // Rodapé
            var rod = new Border
            {
                Background = TemaPlant.BSup1,
                BorderBrush = TemaPlant.BBordaSutil,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var dpRod = new DockPanel();
            var btsDir = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btsDir, Dock.Right);
            btsDir.Children.Add(B("Fechar", Close, true, "Fecha a janela de comparação de fixações (Esc)."));
            dpRod.Children.Add(btsDir);

            var dpAtalhos = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lblAt = T("Exemplos rápidos:", 12, BFraco);
            lblAt.VerticalAlignment = VerticalAlignment.Center;
            lblAt.Margin = new Thickness(0, 0, 8, 0);
            dpAtalhos.Children.Add(lblAt);
            dpAtalhos.Children.Add(Pilula("CB 12300 × WB 12400 × WQE 500 1/2\"", false,
                () => DefinirSelecao("CB 12300", "WB 12400", "WQE 500 1/2\""),
                "Compara chumbador mecânico com parafuso (CB), mecânico pino/presilha (WB) e químico de injeção (WQE 500) na bitola 1/2\"."));
            dpAtalhos.Children.Add(Pilula("Mecânicos 1/2\" (CB × CBPL × CBE × WB × PB)", false,
                () => DefinirSelecao("CB 12300", "CBPL 12412", "CBE 12095", "WB 12400", "PB 1/2\" hef 80"),
                "Compara as linhas mecânicas de expansão por torque na bitola 1/2\" (incluindo prolongador e Indufix Parabolt)."));
            dpAtalhos.Children.Add(Pilula("Químicos 1/2\" (WQA × WQI 44 × WQE 500)", false,
                () => DefinirSelecao("WQA 1/2\"", "WQI 44 1/2\"", "WQE 500 1/2\""),
                "Compara ampola química (WQA), injeção de cura rápida (WQI 44) e injeção de cura lenta (WQE 500) na bitola 1/2\"."));
            dpAtalhos.Children.Add(Pilula("+ Linhas informativas (WLT, Golpe, PU 500)", false,
                () => DefinirSelecao("CB 12300", "Walsywa WLT", "Walsywa Golpe de Metal", "Walsywa PU 500"),
                "Inclui as linhas somente informativas do guia (sem cálculo estrutural na placa) para comparar atributos e limitações."));
            dpRod.Children.Add(dpAtalhos);
            rod.Child = dpRod;
            DockPanel.SetDock(rod, Dock.Bottom);
            raiz.Children.Add(rod);

            // Corpo principal: Esquerda (seletor 340 px) + Direita (comparação lado a lado)
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 1) Coluna esquerda: busca + filtros + lista de todas as linhas
            var esqBorder = new Border
            {
                Background = TemaPlant.BSup1,
                BorderBrush = TemaPlant.BBordaSutil,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var dpEsq = new DockPanel();
            var cabEsq = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };

            var linTitEsq = new DockPanel();
            badgeContagem.Background = TemaPlant.BAcentoSutil;
            badgeContagem.BorderBrush = TemaPlant.BAcento;
            badgeContagem.BorderThickness = new Thickness(1);
            badgeContagem.CornerRadius = new CornerRadius(3);
            badgeContagem.Padding = new Thickness(7, 2, 7, 2);
            badgeContagem.VerticalAlignment = VerticalAlignment.Center;
            lblContagem.FontSize = 11;
            lblContagem.FontWeight = FontWeights.SemiBold;
            lblContagem.Foreground = TemaPlant.BRealce;
            badgeContagem.Child = lblContagem;
            Dica(badgeContagem, "Quantidade de fixações selecionadas para comparação lado a lado.");
            DockPanel.SetDock(badgeContagem, Dock.Right);
            linTitEsq.Children.Add(badgeContagem);
            linTitEsq.Children.Add(Dica(T("Escolher fixações (2+)", 14, BTexto, FontWeights.SemiBold),
                "Marque 2 ou mais chumbadores (ou linhas informativas) na lista abaixo para comparar lado a lado."));
            cabEsq.Children.Add(linTitEsq);

            var subEsq = T("Clique nas linhas abaixo para adicionar ou remover colunas da comparação.", 11, BFraco);
            subEsq.Margin = new Thickness(0, 3, 0, 8);
            cabEsq.Children.Add(subEsq);

            // Caixa de busca
            var boxBusca = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 30 };
            boxBusca.Children.Add(new Border { Background = TemaPlant.BSup3, BorderBrush = BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
            var icBusca = (FrameworkElement)Icones.Criar("busca", 14);
            icBusca.HorizontalAlignment = HorizontalAlignment.Left; icBusca.VerticalAlignment = VerticalAlignment.Center; icBusca.Margin = new Thickness(9, 0, 0, 0); icBusca.IsHitTestVisible = false;
            boxBusca.Children.Add(icBusca);
            var tbBusca = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = BTexto,
                BorderThickness = new Thickness(0),
                CaretBrush = BTexto,
                Padding = new Thickness(28, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Dica(tbBusca, "Filtra as fixações por código (ex.: CB 12300), linha (ex.: WB, WQE 500, WLT), fabricante ou diâmetro (ex.: 1/2\").");
            var dicaBusca = T("Buscar código, linha ou Ø (ex.: 1/2\", WB, WQE)…", 11.5, TemaPlant.BTextoMudo);
            dicaBusca.Margin = new Thickness(32, 0, 0, 0);
            dicaBusca.VerticalAlignment = VerticalAlignment.Center;
            dicaBusca.IsHitTestVisible = false;
            tbBusca.TextChanged += (s, e) =>
            {
                filtroBusca = tbBusca.Text ?? "";
                dicaBusca.Visibility = string.IsNullOrEmpty(filtroBusca) ? Visibility.Visible : Visibility.Collapsed;
                PreencherSeletor();
            };
            boxBusca.Children.Add(tbBusca);
            boxBusca.Children.Add(dicaBusca);
            cabEsq.Children.Add(boxBusca);

            // Pílulas de filtro + Limpar
            var wpFiltros = new WrapPanel { Orientation = Orientation.Horizontal };
            cabEsq.Children.Add(btsFiltro);

            var bdCabEsq = new Border
            {
                BorderBrush = BFundo,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = cabEsq
            };
            DockPanel.SetDock(bdCabEsq, Dock.Top);
            dpEsq.Children.Add(bdCabEsq);

            var svEsq = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = listaSeletor
            };
            dpEsq.Children.Add(svEsq);
            esqBorder.Child = dpEsq;
            Grid.SetColumn(esqBorder, 0);
            corpo.Children.Add(esqBorder);

            // 2) Coluna direita: resumo dos esforços da placa + avisos + colunas lado a lado + comparação em pares
            var dpDir = new DockPanel();

            // Faixa de contexto da placa no topo
            var faixaPlaca = MontarFaixaPlaca();
            DockPanel.SetDock(faixaPlaca, Dock.Top);
            dpDir.Children.Add(faixaPlaca);

            // Scroll vertical geral da direita; dentro dele, as colunas têm ScrollViewer horizontal próprio
            var svDir = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 12, 16, 16)
            };
            var empilhadoDir = new StackPanel();

            // Avisos (seção 5.3)
            empilhadoDir.Children.Add(areaAvisos);

            // Rolagem horizontal das colunas de comparação
            var svColunas = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 14),
                Content = painelColunas
            };
            empilhadoDir.Children.Add(svColunas);

            // Texto dos pares (seção 5.2)
            empilhadoDir.Children.Add(areaPares);

            svDir.Content = empilhadoDir;
            dpDir.Children.Add(svDir);
            Grid.SetColumn(dpDir, 1);
            corpo.Children.Add(dpDir);

            raiz.Children.Add(corpo);
            Content = raiz;

            PreencherSeletor();
            AtualizarComparacao();
        }

        void InicializarSelecao(IEnumerable<string> iniciais)
        {
            selecionados.Clear();
            if (iniciais != null)
            {
                foreach (string s in iniciais)
                {
                    var col = Fixacoes.Avaliar(s, r, p, m);
                    if (col != null && !string.IsNullOrEmpty(col.Chave) && !selecionados.Contains(col.Chave))
                        selecionados.Add(col.Chave);
                }
            }
            if (selecionados.Count == 0 && p != null && !string.IsNullOrEmpty(p.Chumbador))
            {
                var colAt = Fixacoes.Avaliar(p.Chumbador, r, p, m);
                if (colAt != null && !string.IsNullOrEmpty(colAt.Chave))
                    selecionados.Add(colAt.Chave);
            }
            // Garante pelo menos 2+ fixações iniciais para comparação imediata (padrão da tarefa: CB 12300 × WB 12400 × WQE 500 1/2")
            foreach (string padrao in new[] { "CB 12300", "WB 12400", "WQE 500 1/2\"" })
            {
                if (selecionados.Count >= 3) break;
                if (!selecionados.Contains(padrao))
                    selecionados.Add(padrao);
            }
        }

        void DefinirSelecao(params string[] chaves)
        {
            selecionados.Clear();
            foreach (string k in chaves)
            {
                var col = Fixacoes.Avaliar(k, r, p, m);
                if (col != null && !selecionados.Contains(col.Chave))
                    selecionados.Add(col.Chave);
            }
            PreencherSeletor();
            AtualizarComparacao();
        }

        void AlternarSelecao(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return;
            if (selecionados.Contains(chave)) selecionados.Remove(chave);
            else selecionados.Add(chave);
            PreencherSeletor();
            AtualizarComparacao();
        }

        static UIElement Dica(FrameworkElement e, string dica)
        {
            if (!string.IsNullOrEmpty(dica))
            {
                e.ToolTip = dica;
                ToolTipService.SetInitialShowDelay(e, 250);
                ToolTipService.SetShowDuration(e, 25000);
            }
            return e;
        }

        // Pílula no padrão do GUIA (ghost; ativo = Accent.Subtle + borda de acento), raio 4
        static Border Pilula(string texto, bool ativo, Action clique, string dica = null)
        {
            var tb = new TextBlock { Text = texto, FontSize = 11.5, FontWeight = ativo ? FontWeights.SemiBold : FontWeights.Normal, Foreground = ativo ? TemaPlant.BRealce : TemaPlant.BTexto1, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Background = ativo ? TemaPlant.BAcentoSutil : TemaPlant.BSup2,
                BorderBrush = ativo ? TemaPlant.BAcento : TemaPlant.BBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 5, 4),
                Cursor = Cursors.Hand,
                Child = tb
            };
            b.MouseEnter += (s, e) => { if (!ativo) b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { if (!ativo) b.Background = TemaPlant.BSup2; };
            b.MouseLeftButtonDown += (s, e) => { clique(); e.Handled = true; };
            if (!string.IsNullOrEmpty(dica)) Dica(b, dica);
            return b;
        }

        static StackPanel BarraCapacidade(double ap, bool ok, string dica)
        {
            if (double.IsNaN(ap) || double.IsInfinity(ap)) ap = 0;
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
            Dica(sp, dica);
            var dp = new DockPanel();
            Brush cor = !ok || ap > 1 ? BVermelho : ap > 0.9 ? BAmarelo : BVerde;
            var pct = T((ap * 100).ToString("0") + "%", 12, cor, FontWeights.SemiBold);
            DockPanel.SetDock(pct, Dock.Right);
            dp.Children.Add(pct);
            dp.Children.Add(T("Capacidade usada", 11.5, BFraco));
            sp.Children.Add(dp);
            var trilho = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
            trilho.Children.Add(new Border { Background = BCinza, CornerRadius = new CornerRadius(3) });
            double f = Math.Max(0, Math.Min(1, ap));
            if (f > 0)
            {
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, f), GridUnitType.Star) });
                trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - f), GridUnitType.Star) });
                Grid.SetColumnSpan(trilho.Children[0], 2);
                trilho.Children.Add(new Border { Background = cor, CornerRadius = new CornerRadius(3) });
            }
            sp.Children.Add(trilho);
            return sp;
        }

        Border MontarFaixaPlaca()
        {
            var bd = new Border
            {
                Background = TemaPlant.BSup1,
                BorderBrush = TemaPlant.BBordaSutil,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var dp = new DockPanel();
            if (r != null && r.TemEsforcos)
            {
                int nb = r.Paraf != null ? r.Paraf.Count : Math.Max(1, p.Fileiras * p.PorFileira);
                double fckLaje = p != null && p.Fck > 0 ? p.Fck : (r.FckLaje > 0 ? r.FckLaje : 20);
                string clBarra = p != null && !string.IsNullOrEmpty(p.ClasseBarra) ? p.ClasseBarra : "4.6";
                string tipoCarga = r.TipoCarga >= 0 && r.TipoCarga < Chumbadores.NomesTipos.Length ? Chumbadores.NomesTipos[r.TipoCarga] : "dinâmica";
                string risco = r.RiscoAlto ? "alto risco" : "baixo risco";

                var sp = new StackPanel();
                sp.Children.Add(Dica(T(
                    "Placa atual: " + Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + Num.Fmt(p.Esp, 1) + " mm · " +
                    nb + " fixações (" + p.Fileiras + "×" + p.PorFileira + ", s,mín = " + Num.Fmt(r.EspacMin, 0) + " mm) · " +
                    "Esforços por fixação: Tmáx = " + Num.Fmt(r.Tmax, 2) + " kN, V = " + Num.Fmt(r.Fv, 2) + " kN",
                    12.5, BTexto, FontWeights.SemiBold),
                    "Todas as colunas ao lado são verificadas com estes mesmos esforços e geometria de furos usando PlacaCalc.CandidatoChumbador."));
                sp.Children.Add(Dica(T(
                    "Concreto da laje: fck = " + Num.Fmt(fckLaje, 0) + " MPa · " +
                    "Critério mecânicos/injeção: carga " + tipoCarga + ", " + risco + " → n,exig = " + Num.Fmt(r.NExig, 1) +
                    " (WQA: n,ref = 1 sobre carga recomendada) · Barra química: classe " + clBarra + " (n,aço ≥ " + Num.Fmt(r.NSeg, 1) + ")",
                    11.5, BFraco),
                    "Critério de cálculo conforme docs/CHUMBADORES.md e docs/FIXACOES-GUIA.md."));
                dp.Children.Add(sp);
            }
            else
            {
                dp.Children.Add(T("Placa sem esforços calculados — exibindo os atributos de catálogo, cargas nominais, usos recomendados e comparações entre pares.", 12, BAmarelo));
            }
            bd.Child = dp;
            return bd;
        }

        void PreencherSeletor()
        {
            lblContagem.Text = selecionados.Count + (selecionados.Count == 1 ? " selecionada" : " selecionadas");

            btsFiltro.Children.Clear();
            var wp = new WrapPanel { Orientation = Orientation.Horizontal };
            wp.Children.Add(Pilula("Todas", filtroTipo == 0, () => { filtroTipo = 0; PreencherSeletor(); }, "Mostra todas as linhas do catálogo e as linhas informativas."));
            wp.Children.Add(Pilula("Mecânico", filtroTipo == 1, () => { filtroTipo = filtroTipo == 1 ? 0 : 1; PreencherSeletor(); }, "Filtra apenas chumbadores mecânicos de expansão."));
            wp.Children.Add(Pilula("Químico", filtroTipo == 2, () => { filtroTipo = filtroTipo == 2 ? 0 : 2; PreencherSeletor(); }, "Filtra apenas chumbadores químicos (ampola e injeção)."));
            if (r != null && r.TemEsforcos)
                wp.Children.Add(Pilula("Só passam", filtroTipo == 3, () => { filtroTipo = filtroTipo == 3 ? 0 : 3; PreencherSeletor(); }, "Mostra apenas os chumbadores que passam nos esforços da placa atual."));
            wp.Children.Add(Pilula("Informativas", filtroTipo == 4, () => { filtroTipo = filtroTipo == 4 ? 0 : 4; PreencherSeletor(); }, "Mostra as linhas fora do cálculo estrutural (WLT, Golpe de Metal e PU 500)."));
            if (selecionados.Count > 0)
                wp.Children.Add(Pilula("Limpar (" + selecionados.Count + ")", false, () => { selecionados.Clear(); PreencherSeletor(); AtualizarComparacao(); }, "Remove todas as fixações selecionadas."));
            btsFiltro.Children.Add(wp);

            listaSeletor.Children.Clear();
            string q = (filtroBusca ?? "").Trim().ToLowerInvariant();

            // Agrupa por linha do fixacoes.csv mantendo a ordem padronizada
            foreach (FixacaoLinha fl in Fixacoes.Linhas)
            {
                if (filtroTipo == 1 && fl.Familia != "mecânico") continue;
                if (filtroTipo == 2 && fl.Familia != "químico") continue;
                if (filtroTipo == 4 && !fl.Informativa) continue;

                if (fl.Informativa)
                {
                    if (filtroTipo == 3) continue; // informativa não tem verificação estrutural
                    bool bateBusca = q == "" ||
                        fl.Linha.ToLowerInvariant().Contains(q) ||
                        fl.Fabricante.ToLowerInvariant().Contains(q) ||
                        fl.Familia.ToLowerInvariant().Contains(q) ||
                        fl.Elemento.ToLowerInvariant().Contains(q) ||
                        fl.Acionamento.ToLowerInvariant().Contains(q);
                    if (!bateBusca) continue;

                    AdicionarCabecalhoLinhaSeletor(fl);
                    AdicionarItemInformativoSeletor(fl);
                }
                else
                {
                    var itensLinha = Chumbadores.DaLinha(fl.Linha).Where(c =>
                    {
                        if (q != "" && !(c.Codigo.ToLowerInvariant().Contains(q) ||
                                         c.Nome.ToLowerInvariant().Contains(q) ||
                                         c.Linha.ToLowerInvariant().Contains(q) ||
                                         c.Fabricante.ToLowerInvariant().Contains(q) ||
                                         c.Pol.ToLowerInvariant().Contains(q)))
                            return false;
                        if (filtroTipo == 3 && r != null && r.TemEsforcos)
                        {
                            var tc = PlacaCalc.CandidatoChumbador(r, p, m, c);
                            if (!tc.ChOk) return false;
                        }
                        return true;
                    }).ToList();

                    if (itensLinha.Count == 0) continue;
                    AdicionarCabecalhoLinhaSeletor(fl);
                    foreach (Chumbador c in itensLinha)
                        AdicionarItemChumbadorSeletor(c, fl);
                }
            }
        }

        void AdicionarCabecalhoLinhaSeletor(FixacaoLinha fl)
        {
            var dp = new DockPanel { Margin = new Thickness(12, 10, 12, 4) };
            var tag = new Border
            {
                Background = fl.Informativa ? TemaPlant.BAlertaFundo : fl.Familia == "químico" ? TemaPlant.BInfoFundo : TemaPlant.BSup3,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Child = T(fl.Informativa ? "só informativa" : fl.Familia + " · " + fl.Acionamento, 10, fl.Informativa ? BAmarelo : BFraco)
            };
            DockPanel.SetDock(tag, Dock.Right);
            dp.Children.Add(tag);
            dp.Children.Add(T(fl.Linha + (fl.SemCatalogo ? " (" + fl.Fabricante + ")" : ""), 11.5, BFraco, FontWeights.SemiBold));
            Dica(dp, fl.TipoDesc + "\nUso recomendado: " + fl.UsoRecomendado);
            listaSeletor.Children.Add(dp);
        }

        void AdicionarItemChumbadorSeletor(Chumbador c, FixacaoLinha fl)
        {
            bool marcado = selecionados.Contains(c.Codigo) || selecionados.Contains(c.Nome);
            string chave = selecionados.Contains(c.Nome) ? c.Nome : c.Codigo;
            ResPlaca tc = r != null && r.TemEsforcos ? PlacaCalc.CandidatoChumbador(r, p, m, c) : null;
            bool ok = tc != null && tc.ChOk;

            var bd = new Border
            {
                Background = marcado ? TemaPlant.BSelecao : Brushes.Transparent,
                BorderBrush = marcado ? BAzul : BFundo,
                BorderThickness = new Thickness(marcado ? 1.5 : 0, 0, 0, 1),
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            var dp = new DockPanel();

            // Indicador passa / não passa à direita
            if (tc != null)
            {
                string txtStatus = ok
                    ? "✓ " + (tc.ApFix * 100).ToString("0") + "%"
                    : "✕ n=" + (double.IsInfinity(tc.NComb) ? "∞" : Num.Fmt(tc.NComb, 1));
                var lblSt = T(txtStatus, 11, ok ? BVerde : BVermelho, FontWeights.SemiBold);
                lblSt.VerticalAlignment = VerticalAlignment.Center;
                lblSt.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(lblSt, Dock.Right);
                dp.Children.Add(lblSt);
            }

            // Caixa de seleção visual à esquerda
            var chk = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = marcado ? BAzul : BFundo,
                BorderBrush = marcado ? BAzul : BBorda,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = marcado ? new TextBlock { Text = "✓", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BTextoSobreAcento, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null
            };
            DockPanel.SetDock(chk, Dock.Left);
            dp.Children.Add(chk);

            var meio = new StackPanel();
            var lin1 = new WrapPanel { Orientation = Orientation.Horizontal };
            lin1.Children.Add(T(c.Codigo, 12, BTexto, FontWeights.SemiBold));
            lin1.Children.Add(T("  Ø" + c.Pol + " · hef " + Num.Fmt(c.Hef, 0) + " mm", 11.5, BFraco));
            if (p != null && (p.Chumbador == c.Nome || p.Chumbador == c.Codigo))
                lin1.Children.Add(T("  (atual)", 11, BAzul, FontWeights.SemiBold));
            meio.Children.Add(lin1);

            string sub = "furo Ø" + Num.Fmt(c.Furo, c.Furo == Math.Floor(c.Furo) ? 0 : 1) + " mm · " +
                         (c.Tu > 0 ? "Tu " + Num.Fmt(c.Tu, 0) + " kgf" : "Trec " + Num.Fmt(c.TuRec, 0) + " kgf") +
                         (c.Vu > 0 ? " · Vu " + Num.Fmt(c.Vu, 0) + " kgf" : c.VuRec > 0 ? " · Vrec " + Num.Fmt(c.VuRec, 0) + " kgf" : " · s/ corte tab.") +
                         " (C" + Num.Fmt(c.Fck, 0) + ")";
            meio.Children.Add(T(sub, 10.5, BFraco));
            dp.Children.Add(meio);
            bd.Child = dp;

            Dica(bd, c.Nome + " (" + c.Fabricante + ")\n" + sub +
                     (tc != null ? "\nNa placa atual: n,concreto = " + (double.IsInfinity(tc.NComb) ? "∞" : Num.Fmt(tc.NComb, 2)) + " (exig. " + Num.Fmt(tc.NRef, 1) + ")" +
                     (c.Quimico ? " · n,aço = " + (double.IsInfinity(tc.NAco) ? "∞" : Num.Fmt(tc.NAco, 2)) : "") +
                     " · aproveitamento " + (tc.ApFix * 100).ToString("0") + "%" : "") +
                     "\nClique para " + (marcado ? "remover da" : "adicionar à") + " comparação.");

            bd.MouseLeftButtonDown += (s, e) => { AlternarSelecao(chave); e.Handled = true; };
            listaSeletor.Children.Add(bd);
        }

        void AdicionarItemInformativoSeletor(FixacaoLinha fl)
        {
            bool marcado = selecionados.Contains(fl.Linha);
            var bd = new Border
            {
                Background = marcado ? TemaPlant.BSelecao : Brushes.Transparent,
                BorderBrush = marcado ? BAzul : BFundo,
                BorderThickness = new Thickness(marcado ? 1.5 : 0, 0, 0, 1),
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            var dp = new DockPanel();
            var lblInfo = T("ℹ info", 11, BAmarelo, FontWeights.SemiBold);
            lblInfo.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(lblInfo, Dock.Right);
            dp.Children.Add(lblInfo);

            var chk = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = marcado ? BAzul : BFundo,
                BorderBrush = marcado ? BAzul : BBorda,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = marcado ? new TextBlock { Text = "✓", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BTextoSobreAcento, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null
            };
            DockPanel.SetDock(chk, Dock.Left);
            dp.Children.Add(chk);

            var meio = new StackPanel();
            meio.Children.Add(T(fl.Linha + " (" + fl.FaixaDiametro + ")", 12, BTexto, FontWeights.SemiBold));
            meio.Children.Add(T(fl.TipoDesc, 10.5, BFraco));
            dp.Children.Add(meio);
            bd.Child = dp;

            Dica(bd, fl.Linha + " — linha somente informativa (fora do cálculo estrutural)\n" + fl.UsoRecomendado +
                     "\nClique para " + (marcado ? "remover da" : "adicionar à") + " comparação.");
            bd.MouseLeftButtonDown += (s, e) => { AlternarSelecao(fl.Linha); e.Handled = true; };
            listaSeletor.Children.Add(bd);
        }

        void AtualizarComparacao()
        {
            areaAvisos.Children.Clear();
            painelColunas.Children.Clear();
            areaPares.Children.Clear();

            var cols = selecionados.Select(k => Fixacoes.Avaliar(k, r, p, m)).Where(c => c != null && c.Info != null).ToList();

            if (cols.Count == 0)
            {
                var vazio = new Border
                {
                    Background = BPainel,
                    BorderBrush = BBorda,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(24, 20, 24, 20),
                    Child = T("Selecione 2 ou mais fixações na lista à esquerda (ou clique num dos exemplos rápidos no rodapé) para comparar lado a lado.", 13, BFraco)
                };
                areaAvisos.Children.Add(vazio);
                return;
            }

            if (cols.Count == 1)
            {
                var dicaUm = new Border
                {
                    Background = TemaPlant.BSup3,
                    BorderBrush = BAzul,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = T("ℹ Há 1 fixação selecionada. Marque pelo menos mais uma fixação na lista à esquerda para ver as diferenças lado a lado e o comparativo do par.", 12, BTexto)
                };
                areaAvisos.Children.Add(dicaUm);
            }

            // 1) Avisos obrigatórios da seção 5.3
            List<string> avisos = Fixacoes.Avisos(cols, r);
            if (avisos.Count > 0)
            {
                var bdAv = new Border
                {
                    Background = TemaPlant.BAlertaFundo,
                    BorderBrush = TemaPlant.BAlertaBorda,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                // compacto: uma linha com um chip por aviso (texto completo na dica) + detalhes recolhíveis
                bdAv.Padding = new Thickness(12, 7, 12, 7);
                var spAv = new StackPanel();
                var linAv = new WrapPanel { Orientation = Orientation.Horizontal };
                var tAv = UiPlaca.Dica(UiPlaca.Tx("⚠  " + avisos.Count + (avisos.Count == 1 ? " cuidado" : " cuidados") + " ao comparar:", 12, BAmarelo, FontWeights.SemiBold),
                    "Cuidados obrigatórios ao comparar grandezas de linhas, concretos ou critérios diferentes (seção 5.3 do guia dos catálogos).");
                tAv.Margin = new Thickness(0, 0, 8, 0);
                linAv.Children.Add(tAv);
                foreach (string a in avisos)
                {
                    int ip = a.IndexOf(':');
                    string curto = ip > 0 && ip < 70 ? a.Substring(0, ip) : (a.Length > 60 ? a.Substring(0, 57) + "…" : a);
                    var ch = UiPlaca.Chip(curto, UiPlaca.Nivel.Alerta, a, null, 11);
                    ch.Margin = new Thickness(0, 2, 6, 2);
                    linAv.Children.Add(ch);
                }
                spAv.Children.Add(linAv);
                var spDet = new StackPanel();
                foreach (string a in avisos)
                {
                    var ta = T("• " + a, 11.5, BTexto);
                    ta.Margin = new Thickness(0, 4, 0, 0);
                    spDet.Children.Add(ta);
                }
                spAv.Children.Add(new Expander { Header = T("ver o texto completo", 11.5, BFraco), IsExpanded = false, Margin = new Thickness(0, 4, 0, 0), Content = spDet, Foreground = BFraco });
                bdAv.Child = spAv;
                areaAvisos.Children.Add(bdAv);
            }

            // 2) Uma coluna por fixação selecionada
            foreach (ColunaComparacao col in cols)
                painelColunas.Children.Add(MontarCartaoColuna(col));

            // 3) Comparações em pares da seção 5.2 quando houver
            var linhasDistintas = cols.Select(c => c.Info.Linha).Distinct().ToList();
            List<ParFixacao> pares = Fixacoes.ParesPara(linhasDistintas);
            if (pares.Count > 0)
            {
                areaPares.Children.Add(UiPlaca.TituloSecao("Comparação direta entre os pares selecionados (seção 5.2 do guia)", null, 4));

                foreach (ParFixacao par in pares)
                    areaPares.Children.Add(MontarCartaoPar(par));
            }
        }

        Border MontarCartaoColuna(ColunaComparacao col)
        {
            Chumbador c = col.Ch;
            FixacaoLinha fl = col.Info;
            ResPlaca tc = col.Res;
            bool temVerif = c != null && tc != null;
            bool ok = temVerif && tc.ChOk;

            var card = new Border
            {
                Width = 345,
                Background = TemaPlant.BSup1,
                BorderBrush = TemaPlant.BBordaSutil,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 12, 4),
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true
            };

            var sp = new StackPanel();
            // faixa de 3 px com a cor do status (o resto do cartão fica neutro)
            sp.Children.Add(new Border { Height = 3, Background = fl.Informativa ? TemaPlant.BAlerta : temVerif ? (ok ? TemaPlant.BSucesso : TemaPlant.BErro) : TemaPlant.BBorda });

            // Cabeçalho da coluna
            var cab = new Border
            {
                Background = TemaPlant.BSup2,
                BorderBrush = TemaPlant.BBordaSutil,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 9, 12, 10)
            };
            var spCab = new StackPanel();
            var topoCab = new DockPanel();

            var btRem = T("✕", 13, BFraco, FontWeights.Bold);
            btRem.Cursor = Cursors.Hand;
            btRem.Margin = new Thickness(8, 0, 0, 0);
            Dica(btRem, "Remover esta coluna da comparação.");
            string chaveCol = col.Chave;
            btRem.MouseLeftButtonDown += (s, e) => { AlternarSelecao(chaveCol); e.Handled = true; };
            DockPanel.SetDock(btRem, Dock.Right);
            topoCab.Children.Add(btRem);

            string seloTxt = fl.Informativa ? "ℹ só informativa (fora do cálculo)"
                           : !temVerif ? "sem esforços na placa"
                           : ok ? "✓ passa na placa atual · " + UiPlaca.Pct(tc.ApFix) : "✕ não passa na placa atual · " + UiPlaca.Pct(tc.ApFix);
            var nivel = fl.Informativa ? UiPlaca.Nivel.Alerta : !temVerif ? UiPlaca.Nivel.Neutro : ok ? UiPlaca.NivelAp(tc.ApFix, true) : UiPlaca.Nivel.Erro;
            var selo = UiPlaca.Chip(seloTxt, nivel, null, null, 10.5);
            selo.HorizontalAlignment = HorizontalAlignment.Left;
            topoCab.Children.Add(selo);
            spCab.Children.Add(topoCab);

            var tTit = T(col.Titulo, 16, TemaPlant.BTexto1, FontWeights.Bold);
            tTit.Margin = new Thickness(0, 6, 0, 0);
            spCab.Children.Add(tTit);
            var sub = T(col.Subtitulo, 11.5, BFraco);
            sub.Margin = new Thickness(0, 2, 0, 0);
            spCab.Children.Add(sub);

            if (c != null)
            {
                bool jaEmUso = p != null && (p.Chumbador == c.Nome || p.Chumbador == c.Codigo);
                var dpUsar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (jaEmUso)
                {
                    var bUso = new Border
                    {
                        Background = TemaPlant.BSelecao,
                        BorderBrush = BAzul,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = T("✓ Em uso na placa", 11, TemaPlant.BTexto1, FontWeights.SemiBold)
                    };
                    Dica(bUso, "Este chumbador já é o selecionado atualmente na placa de base.");
                    dpUsar.Children.Add(bUso);
                }
                else
                {
                    var bUsar = UiPlaca.Botao("Usar este chumbador na placa", null, () =>
                    {
                        ChumbadorEscolhido = c.Nome;
                        if (p != null) p.Chumbador = c.Nome;
                        DialogResult = true;
                        Close();
                    }, "Seleciona " + c.Nome + " na placa de base e fecha a janela de comparação.", 28);
                    bUsar.HorizontalAlignment = HorizontalAlignment.Left;
                    dpUsar.Children.Add(bUsar);
                }
                spCab.Children.Add(dpUsar);
            }
            cab.Child = spCab;
            sp.Children.Add(cab);

            // Corpo interno da coluna
            var corpoCol = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

            // 1. VERIFICAÇÃO COM OS ESFORÇOS DA PLACA ATUAL (PlacaCalc.CandidatoChumbador)
            corpoCol.Children.Add(TituloSecaoColuna("1. Verificação na placa atual",
                "Calculado por PlacaCalc.CandidatoChumbador(r, p, m, c) — exatamente o mesmo cálculo da janela da placa."));
            if (fl.Informativa)
            {
                corpoCol.Children.Add(CaixaAvisoColuna(
                    "Linha somente informativa no Estructto: não está em chumbadores.csv e não realiza verificação estrutural.",
                    BAmarelo));
            }
            else if (!temVerif)
            {
                corpoCol.Children.Add(T("Calcule a estrutura para ver os fatores de segurança e o aproveitamento na placa.", 11.5, BFraco));
            }
            else
            {
                corpoCol.Children.Add(BarraCapacidade(tc.ApFix, tc.ChOk,
                    "Aproveitamento máximo da fixação (concreto ou aço da barra): " + (tc.ApFix * 100).ToString("0") + "%."));

                string nConcStr = (double.IsInfinity(tc.NComb) ? "∞" : Num.Fmt(tc.NComb, 2)) +
                                  (tc.NComb >= tc.NRef ? " ≥ " : " < ") + Num.Fmt(tc.NRef, 1);
                string nTStr = double.IsInfinity(tc.NT) ? "∞" : Num.Fmt(tc.NT, 2);
                string nVStr = tc.Vref > 0 ? (double.IsInfinity(tc.NV) ? "∞" : Num.Fmt(tc.NV, 2)) : "s/ corte tab.";
                string nAcoStr = c.Quimico
                    ? (double.IsInfinity(tc.NAco) ? "∞" : Num.Fmt(tc.NAco, 2)) + (tc.NAco >= tc.NSeg ? " ≥ " : " < ") + Num.Fmt(tc.NSeg, 1) + " (cl. " + tc.ClasseBarra + ")"
                    : "incluído no ensaio";
                double espMinFis = Chumbadores.EspacMinimo(c);
                double coneCheio = c.S > 0 ? c.S : 3 * c.Hef;
                string espStr = Num.Fmt(tc.EspacMin, 0) + " mm (mín. 6d=" + Num.Fmt(espMinFis, 0) + " · 3P=" + Num.Fmt(coneCheio, 0) + ")";

                var linhasVerif = new List<(string rot, string val, int est, string dica)>
                {
                    ("n no concreto ≥ n,ref", nConcStr, tc.NComb >= tc.NRef ? 1 : -1,
                        "Interação no concreto: (T/Tref)^(5/3) + (V/Vref)^(5/3) = (1/n)^(5/3). Exigido n,ref = " + Num.Fmt(tc.NRef, 1) + "."),
                    ("n isolado (tração / corte)", nTStr + "  /  " + nVStr, 0,
                        "Fatores de segurança isolados no concreto: n,T = Tref / Tmáx e n,V = Vref / Fv."),
                    ("n do aço da barra", nAcoStr, c.Quimico ? (tc.NAco >= tc.NSeg ? 1 : -1) : 0,
                        c.Quimico ? "Von Mises na barra roscada (classe " + tc.ClasseBarra + ", Sp = " + Num.Fmt(tc.SpBarra, 0) + " MPa): σ = " + Num.Fmt(tc.Sigma, 0) + " MPa, τ = " + Num.Fmt(tc.Tau, 0) + " MPa."
                                  : "Nos chumbadores mecânicos, a resistência do aço já está contemplada no ensaio de ruptura do catálogo."),
                    ("Espaçamento na placa", espStr, tc.EspacOk ? (tc.PsiCone < 0.999 ? 0 : 1) : -1,
                        "Mínimo físico para instalar: 6·d = " + Num.Fmt(espMinFis, 0) + " mm. Abaixo de 3·P = " + Num.Fmt(coneCheio, 0) + " mm há sobreposição de cones (ψ = " + Num.Fmt(tc.PsiCone, 3) + ")."),
                    ("Resultado", tc.ChOk ? "PASSA (" + (tc.ApFix * 100).ToString("0") + "%)" : "NÃO PASSA (" + (tc.ApFix * 100).ToString("0") + "%)", tc.ChOk ? 1 : -1,
                        "Aprovação exige simultaneamente: n,concreto ≥ n,ref, n,aço ≥ n,seg (químicos) e espaçamento ≥ 6·d.")
                };
                corpoCol.Children.Add(TabelaLinhas(linhasVerif));
            }

            // 2. CARGAS (CATÁLOGO E AJUSTADA USADA NA VERIFICAÇÃO)
            corpoCol.Children.Add(TituloSecaoColuna("2. Cargas (catálogo e ajustada)",
                "Valor original da tabela do fabricante (com unidade e fck do ensaio) e valor ajustado usado na verificação (Tref, Vref)."));
            if (c != null)
            {
                string trCat;
                if (c.Tu > 0 && c.TuRec > 0)
                    trCat = Num.Fmt(c.Tu, 0) + " kgf (" + Num.Fmt(c.TuKN, 2) + " kN) rupt. · rec. " + Num.Fmt(c.TuRec, 0) + " kgf (" + Num.Fmt(c.TuRecKN, 2) + " kN)";
                else if (c.Tu > 0)
                    trCat = Num.Fmt(c.Tu, 0) + " kgf (" + Num.Fmt(c.TuKN, 2) + " kN) — ruptura";
                else
                    trCat = Num.Fmt(c.TuRec, 0) + " kgf (" + Num.Fmt(c.TuRecKN, 2) + " kN) — recomendada";

                string vCat = c.Vu > 0
                    ? Num.Fmt(c.Vu, 0) + " kgf (" + Num.Fmt(c.VuKN, 2) + " kN) — ruptura"
                    : c.VuRec > 0
                        ? Num.Fmt(c.VuRec, 0) + " kgf (" + Num.Fmt(c.VuRecKN, 2) + " kN) — recomendada"
                        : "não informado no catálogo";

                double fckLaje = tc != null ? tc.FckLaje : (p != null && p.Fck > 0 ? p.Fck : 20);
                double ffck = tc != null ? tc.FFck : (c.Fck > 0 ? Math.Min(1, Math.Sqrt(fckLaje / c.Fck)) : 1);
                double psiCone = tc != null ? tc.PsiCone : 1;
                double tref = tc != null ? tc.Tref : (c.Tu > 0 ? c.TuKN : c.TuRecKN) * ffck * psiCone;
                double vref = tc != null ? tc.Vref : (c.Vu > 0 ? c.VuKN : c.VuRecKN);

                var linhasCargas = new List<(string rot, string val, int est, string dica)>
                {
                    ("Concreto do ensaio", "fck = " + Num.Fmt(c.Fck, 0) + " MPa (laje " + Num.Fmt(fckLaje, 0) + " MPa)", 0,
                        "Classe do concreto em que o fabricante realizou o ensaio da tabela."),
                    ("Tração catálogo", trCat, 0,
                        "Carga de tração informada na tabela do catálogo para Ø" + c.Pol + " hef " + Num.Fmt(c.Hef, 0) + " mm."),
                    ("Corte catálogo", vCat, c.Vu > 0 || c.VuRec > 0 ? 0 : -1,
                        "Carga de cisalhamento informada na tabela do catálogo."),
                    ("Fator concreto ψc", Num.Fmt(ffck, 3) + (ffck < 0.999 ? " (reduzido)" : " (sem redução)"), 0,
                        "ψc = min(1; √(fck laje / fck ensaio)) = min(1; √(" + Num.Fmt(fckLaje, 0) + "/" + Num.Fmt(c.Fck, 0) + "))."),
                    ("Fator cones ψ", Num.Fmt(psiCone, 3) + (psiCone < 0.999 ? " (cones sobrepostos)" : " (cone cheio)"), 0,
                        "Redução pela sobreposição dos cones 3P × 3P na placa atual (ACI 318 / EN 1992-4)."),
                    ("Tração ajustada Tref", Num.Fmt(tref, 2) + " kN (" + Num.Fmt(tref / 0.00980665, 0) + " kgf)", 0,
                        "Tref = T,catálogo × ψc × ψ,cone usada na equação de interação."),
                    ("Corte ajustado Vref", vref > 0 ? Num.Fmt(vref, 2) + " kN (" + Num.Fmt(vref / 0.00980665, 0) + " kgf)" : "0 (verificado no aço)", 0,
                        "Vref usada na verificação de cisalhamento no concreto."),
                    ("Geometria / instalação", "furo Ø" + Num.Fmt(c.Furo, c.Furo == Math.Floor(c.Furo) ? 0 : 1) + " × " + Num.Fmt(c.Hef, 0) + " mm · s=" + Num.Fmt(c.S, 0) + " · c=" + Num.Fmt(c.C, 0) + " mm", 0,
                        "Diâmetro da broca, profundidade de embutimento P (hef), distância mínima catálogo entre pinos (s) e à borda (c).")
                };
                if (c.Torque > 0)
                    linhasCargas.Add(("Torque de aperto", Num.Fmt(c.Torque, 1) + " kgf·m (" + Num.Fmt(c.Torque * 9.80665, 0) + " N·m)", 0, "Torque de instalação informado no catálogo."));
                corpoCol.Children.Add(TabelaLinhas(linhasCargas));
            }
            else
            {
                var linhasCargasInfo = new List<(string rot, string val, int est, string dica)>
                {
                    ("Concreto do ensaio", fl.FckEnsaio + (fl.FckEnsaio == "20" ? " MPa" : ""), 0, "Concreto de referência informado no catálogo."),
                    ("Faixa no catálogo", fl.FaixaCargas, 0, "Resumo das faixas de diâmetros e cargas informadas na seção 2 do guia.")
                };
                corpoCol.Children.Add(TabelaLinhas(linhasCargasInfo));
            }

            // 3. ATRIBUTOS PADRONIZADOS (TABELA 5.1 DO GUIA)
            corpoCol.Children.Add(TituloSecaoColuna("3. Atributos (Tabela 5.1)",
                "Atributos padronizados da seção 5.1 de docs/FIXACOES-GUIA.md."));
            var linhasAtrib = new List<(string rot, string val, int est, string dica)>
            {
                ("Família", fl.Familia, 0, "mecânico (expansão/atrito), químico (aderência/intertravamento da resina) ou não estrutural."),
                ("Acionamento", fl.Acionamento, 0, "Modo de ativação: torque, percussão, ampola, injeção ou espuma."),
                ("Elemento de fixação", fl.Elemento, 0, "Tipo de haste/fixador: parafuso, prisioneiro, corpo roscado, rosca interna, pino, barra/vergalhão."),
                ("Faixa de diâmetro", fl.FaixaDiametro, 0, "Bitolas nominais disponíveis na linha."),
                ("Concreto do ensaio", fl.FckEnsaio + (fl.FckEnsaio == "20" || fl.FckEnsaio == "28" || fl.FckEnsaio == "30" ? " MPa" : ""), 0, "fck do concreto do ensaio de catálogo."),
                ("Natureza da carga", fl.NaturezaCarga, 0, "Se o catálogo informa carga de ruptura média, ruptura, recomendada ou ambas."),
                ("Tração / Corte tab.", "Tração: " + fl.TracaoTabelada + " · Corte: " + fl.CorteTabelado, fl.CorteTabelado == "sim" ? 1 : -1, "Indica se o catálogo traz valores numéricos de tração e de cisalhamento na tabela."),
                ("Carga dinâmica", fl.CargaDinamica, fl.CargaDinamica == "indicado" ? 1 : 0, "Comportamento frente a cargas dinâmicas, vibração e choque."),
                ("Distâncias reduzidas", fl.DistanciasReduzidas, fl.DistanciasReduzidas == "sim" ? 1 : 0, "Se permite menores distâncias entre chumbadores e à borda por não criar tensão de expansão."),
                ("Materiais-base", fl.MateriaisBase, 0, "Materiais-base aceitos pelo produto segundo o catálogo."),
                ("Recuperável", fl.Recuperavel, fl.Recuperavel == "sim" ? 1 : 0, "Se permite remover o parafuso/peça (Recuperabilidade expressa no catálogo)."),
                ("Tempo de cura", fl.TempoCura, fl.TempoCura == "nenhum" ? 1 : 0, "Tempo de espera para aplicar carga após a instalação."),
                ("Acabamento", fl.Acabamento, 0, "Material e revestimento superficial informados no catálogo."),
                ("Fonte", c != null && !string.IsNullOrEmpty(c.Fonte) ? c.Fonte : fl.Paginas, 0, "Catálogo técnico e páginas de origem.")
            };
            corpoCol.Children.Add(TabelaLinhas(linhasAtrib));

            // 4. USO RECOMENDADO, USOS TÍPICOS E PONTOS DE ATENÇÃO (SEÇÃO 2 DO GUIA)
            corpoCol.Children.Add(TituloSecaoColuna("4. Uso recomendado e limitações",
                "Textos extraídos da seção 2 de docs/FIXACOES-GUIA.md."));
            if (fl.SemCatalogo)
            {
                corpoCol.Children.Add(CaixaAvisoColuna(
                    "⚠ Sem descrição de catálogo no guia (falta o PDF do catálogo Indufix na pasta REFERÊNCIA). Exibindo apenas os atributos básicos e o cálculo numérico da tabela.",
                    BAmarelo));
            }
            else
            {
                corpoCol.Children.Add(BlocoTextoColuna("Uso recomendado pelo fabricante", fl.UsoRecomendado, BVerde));
                corpoCol.Children.Add(BlocoTextoColuna("Usos típicos", fl.UsosTipicos, BAzul));
                corpoCol.Children.Add(BlocoTextoColuna("Características principais", fl.Caracteristicas.Replace(" | ", "\n• "), BTexto, true));
                corpoCol.Children.Add(BlocoTextoColuna("Pontos de atenção / limitações", fl.PontosAtencao.Replace(" | ", "\n• "), BAmarelo, true));
            }

            sp.Children.Add(corpoCol);
            card.Child = sp;
            return card;
        }

        static UIElement TituloSecaoColuna(string titulo, string dica)
        {
            return UiPlaca.Dica(UiPlaca.TituloSecao(titulo, null, 10), dica);
        }

        static UIElement CaixaAvisoColuna(string texto, Brush corTexto)
        {
            return new Border
            {
                Background = TemaPlant.BAlertaFundo,
                BorderBrush = TemaPlant.BAlertaBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 2, 0, 6),
                Child = T(texto, 11.5, corTexto)
            };
        }

        static UIElement BlocoTextoColuna(string rotulo, string texto, Brush corRotulo, bool comBullets = false)
        {
            var bd = new Border
            {
                Background = BFundo,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var sp = new StackPanel();
            sp.Children.Add(T(rotulo, 11, corRotulo, FontWeights.SemiBold));
            string txt = comBullets && !string.IsNullOrEmpty(texto) && !texto.StartsWith("•") ? "• " + texto : texto;
            var tb = T(txt, 11.5, BTexto);
            tb.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(tb);
            bd.Child = sp;
            return bd;
        }

        static UIElement TabelaLinhas(List<(string rot, string val, int est, string dica)> linhas)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (var l in linhas)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var fundo = new Border
                {
                    Background = i % 2 == 0 ? TemaPlant.BSup2 : Brushes.Transparent,
                    CornerRadius = new CornerRadius(3)
                };
                if (!string.IsNullOrEmpty(l.dica)) Dica(fundo, l.dica);
                Grid.SetRow(fundo, i);
                Grid.SetColumnSpan(fundo, 2);
                g.Children.Add(fundo);

                var a = T(l.rot, 11, BFraco);
                a.Margin = new Thickness(6, 3.5, 6, 3.5);
                a.VerticalAlignment = VerticalAlignment.Top;
                if (!string.IsNullOrEmpty(l.dica)) Dica(a, l.dica);
                Grid.SetRow(a, i);
                g.Children.Add(a);

                Brush cor = l.est > 0 ? BVerde : l.est < 0 ? BVermelho : BTexto;
                var b = T((l.est > 0 ? "✓ " : l.est < 0 ? "✕ " : "") + l.val, 11, cor, l.est != 0 ? FontWeights.SemiBold : FontWeights.Normal);
                b.Margin = new Thickness(0, 3.5, 6, 3.5);
                b.TextAlignment = TextAlignment.Right;
                b.HorizontalAlignment = HorizontalAlignment.Right;
                if (!string.IsNullOrEmpty(l.dica)) Dica(b, l.dica);
                Grid.SetRow(b, i);
                Grid.SetColumn(b, 1);
                g.Children.Add(b);
                i++;
            }
            return g;
        }

        Border MontarCartaoPar(ParFixacao par)
        {
            var bd = new Border
            {
                Background = BPainel,
                BorderBrush = BBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var sp = new StackPanel();

            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var fnt = T("Fonte: " + par.Fonte, 11, BFraco);
            DockPanel.SetDock(fnt, Dock.Right);
            cab.Children.Add(fnt);
            cab.Children.Add(T(par.Titulo, 14, TemaPlant.BTexto1, FontWeights.Bold));
            sp.Children.Add(cab);

            // Em que diferem
            sp.Children.Add(BlocoTextoColuna("Em que diferem", par.EmQueDiferem.Replace(" | ", "\n• "), BAmarelo, true));

            // Escolha A quando × Escolha B quando lado a lado
            var gEscolha = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            gEscolha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gEscolha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            gEscolha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cA = BlocoTextoColuna("Escolha " + par.NomeA + " quando:", par.EscolhaAQuando, BVerde);
            Grid.SetColumn(cA, 0);
            gEscolha.Children.Add(cA);

            var cB = BlocoTextoColuna("Escolha " + par.NomeB + " quando:", par.EscolhaBQuando, BAzul);
            Grid.SetColumn(cB, 2);
            gEscolha.Children.Add(cB);
            sp.Children.Add(gEscolha);

            // Mesmo desempenho em
            sp.Children.Add(BlocoTextoColuna("Mesmo desempenho em", par.MesmoDesempenho, BFraco));

            bd.Child = sp;
            return bd;
        }
    }
}
