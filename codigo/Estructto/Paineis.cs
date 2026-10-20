// Paineis encaixaveis: Projeto (arvore), Propriedades (varios itens de uma vez), Tabelas de resultados e Avisos.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    // linhas das tabelas (DisplayName vira o cabecalho da coluna)
    public class LinhaNo
    {
        [Browsable(false)] public No Obj { get; set; }
        [DisplayName("Nó")] public int Id { get; set; }
        [DisplayName("X (m)")] public string X { get; set; }
        [DisplayName("Y (m)")] public string Y { get; set; }
        [DisplayName("Apoio")] public string Apoio { get; set; }
        [DisplayName("Desl. X (mm)")] public string DX { get; set; }
        [DisplayName("Desl. Y (mm)")] public string DY { get; set; }
        [DisplayName("Giro (rad)")] public string Giro { get; set; }
        [DisplayName("Rx (kN)")] public string Rx { get; set; }
        [DisplayName("Ry (kN)")] public string Ry { get; set; }
        [DisplayName("M (kN·m)")] public string Rm { get; set; }
    }

    public class LinhaBarra
    {
        [Browsable(false)] public Barra Obj { get; set; }
        [DisplayName("Barra")] public int Id { get; set; }
        [DisplayName("Nós")] public string Nos { get; set; }
        [DisplayName("Perfil")] public string Perfil { get; set; }
        [DisplayName("L (m)")] public string L { get; set; }
        [DisplayName("N mín/máx (kN)")] public string N { get; set; }
        [DisplayName("Q mín/máx (kN)")] public string Q { get; set; }
        [DisplayName("M mín/máx (kN·m)")] public string Mm { get; set; }
        [DisplayName("Flecha (mm)")] public string Flecha { get; set; }
    }

    public class LinhaVerif
    {
        [Browsable(false)] public Barra Obj { get; set; }
        [DisplayName("Barra")] public int Id { get; set; }
        [DisplayName("Perfil")] public string Perfil { get; set; }
        [DisplayName("Situação")] public string Situacao { get; set; }
        [DisplayName("Aproveitamento")] public string Ap { get; set; }
        [DisplayName("Limitado por")] public string Critico { get; set; }
        [DisplayName("Tensão")] public string Tensao { get; set; }
        [DisplayName("Corte")] public string Corte { get; set; }
        [DisplayName("Flecha")] public string Flecha { get; set; }
        [DisplayName("Flambagem")] public string Flamb { get; set; }
        [DisplayName("KL/r no plano")] public string LamPl { get; set; }
        [DisplayName("KL/r fora do plano")] public string LamFo { get; set; }
        [DisplayName("Esbeltez")] public string Esb { get; set; }
        [Browsable(false)] public double ApNum { get; set; } = -1;   // aproveitamento em % (barra de capacidade)
        [Browsable(false)] public bool Pass { get; set; } = true;
        [Browsable(false)] public Brush CorAp { get; set; } = TemaPlant.BTextoMudo;
        [Browsable(false)] public Visibility BarraVis { get; set; } = Visibility.Collapsed;
        [Browsable(false)] public string SitTxt { get; set; } = "";
        [Browsable(false)] public Brush SitFg { get; set; } = TemaPlant.BTexto2;
        [Browsable(false)] public Brush SitBg { get; set; } = TemaPlant.BSup2;
        [Browsable(false)] public Brush SitBd { get; set; } = TemaPlant.BBordaSutil;
    }

    public partial class MainWindow
    {
        bool sincronizando, avisosJaMostrados, soCriticos, verifHook;

        // Painel estreito (ex.: 1280x720): as colunas de apoio (flambagem, KL/r, esbeltez) saem; o essencial cabe sem rolagem horizontal.
        void AjustarColunasVerif()
        {
            double w = TabVerif.ActualWidth;
            bool medio = w > 0 && w < 1250, estreito = w > 0 && w < 980;
            foreach (var c in TabVerif.Columns)
            {
                string h = c.Header as string;
                if (h == "Esbeltez" || h == "KL/r fora") c.Visibility = medio ? Visibility.Collapsed : Visibility.Visible;
                else if (h == "Flambagem" || h == "KL/r plano") c.Visibility = estreito ? Visibility.Collapsed : Visibility.Visible;
            }
            TabVerif.ToolTip = medio ? "Painel estreito: flambagem, KL/r e esbeltez aparecem quando o painel é alargado (e sempre na janela Verificação)." : null;
        }
        string erroCalculo = "";

        void SelecaoMudou()
        {
            bool barras = Vista.Sel.OfType<Barra>().Any(), nos = Vista.Sel.OfType<No>().Any();
            CtxBarra.Visibility = barras ? Visibility.Visible : Visibility.Collapsed;
            CtxNo.Visibility = nos ? Visibility.Visible : Visibility.Collapsed;
            if (barras) tgEixoFraco.IsChecked = Vista.Sel.OfType<Barra>().All(b => b.EixoFraco);
            MostrarPropriedades();
            SincronizarArvore();
            SincronizarTabelas();
            if (Vista.Sel.Count > 0)
                StatusMsg.Text = (barras ? Pl.N(Vista.Sel.Count, "barra selecionada", "barras selecionadas") : Pl.N(Vista.Sel.Count, "nó selecionado", "nós selecionados")) + " — edite em Propriedades ou use a aba contextual; Del apaga.";
        }

        void MostrarPropriedades()
        {
            // Inspector WPF: sem selecao mostra o modelo; com selecao, os campos em comum (editar aplica a todos).
            Insp.Mostrar(M, Vista.Sel);
        }

        // Acoes rapidas do Inspector: apoio para nos, verificar para barras (mesmos caminhos da faixa).
        IEnumerable<AcaoRapida> AcoesDoInspector(IList<object> alvos)
        {
            var nos = alvos.OfType<No>().ToList();
            var barras = alvos.OfType<Barra>().ToList();
            if (nos.Count > 0 && barras.Count == 0)
            {
                Func<No, string> tipo = n => !n.TemApoio ? "livre" : n.ApoioX && n.ApoioY && n.ApoioGiro ? "engaste" : n.ApoioX && n.ApoioY && !n.ApoioGiro && n.MolaX == 0 && n.MolaY == 0 && n.MolaGiro == 0 ? "fixo" : !n.ApoioX && n.ApoioY && !n.ApoioGiro && n.MolaX == 0 && n.MolaY == 0 && n.MolaGiro == 0 ? "rolete" : "outro";
                string comum = nos.Select(tipo).Distinct().Count() == 1 ? tipo(nos[0]) : "";
                yield return new AcaoRapida { Grupo = "Apoio", Texto = "Engaste", Icone = "engaste", Ativo = comum == "engaste", Dica = "Trava X, Y e giro nos nós selecionados.", Executar = () => AplicarApoio(Ferramenta.ApoioEngaste, Vista.Sel.OfType<No>()) };
                yield return new AcaoRapida { Grupo = "Apoio", Texto = "Fixo", Icone = "rotula", Ativo = comum == "fixo", Dica = "Apoio fixo: trava X e Y, giro livre.", Executar = () => AplicarApoio(Ferramenta.ApoioRotula, Vista.Sel.OfType<No>()) };
                yield return new AcaoRapida { Grupo = "Apoio", Texto = "Móvel", Icone = "rolo", Ativo = comum == "rolete", Dica = "Apoio móvel: trava só Y.", Executar = () => AplicarApoio(Ferramenta.ApoioRoloV, Vista.Sel.OfType<No>()) };
                yield return new AcaoRapida { Grupo = "Apoio", Texto = "Livre", Icone = "livre", Ativo = comum == "livre", Dica = "Remove apoio, molas e placa de base dos nós selecionados.", Executar = () => AplicarApoio(Ferramenta.ApoioLivre, Vista.Sel.OfType<No>()) };
            }
            if (barras.Count > 0)
                yield return new AcaoRapida { Grupo = "", Texto = "Verificar", Icone = "verif", Dica = "Abre a verificação detalhada da barra (compara perfis da família).", Executar = () => AbrirVerificacao(Vista.Sel.OfType<Barra>().FirstOrDefault()) };
        }

        // ---------- arvore ----------

        // Cabecalho de item da arvore: icone + nome + detalhe discreto + (contagem em pilula | sinalizadores em icones)
        static FrameworkElement CabArvore(string icone, string nome, string detalhe = null, int? contagem = null, params string[] marcas)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icone != null) { var ic = Icone(icone, 14); ((FrameworkElement)ic).Margin = new Thickness(0, 0, 6, 0); ((FrameworkElement)ic).VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(ic); }
            sp.Children.Add(new TextBlock { Text = nome, Foreground = TemaPlant.BTexto1, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(detalhe))
                sp.Children.Add(new TextBlock { Text = detalhe, Foreground = TemaPlant.BTexto3, FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            foreach (string m in marcas)
            {
                if (m == null) continue;
                var ic = (FrameworkElement)Icone(m, 12); ic.Margin = new Thickness(6, 0, 0, 0); ic.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(ic);
            }
            if (contagem != null)
                sp.Children.Add(new Border
                {
                    Background = contagem.Value == 0 ? Brushes.Transparent : TemaPlant.BSup3, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = contagem.Value.ToString(), FontSize = 10.5, Foreground = contagem.Value == 0 ? TemaPlant.BTextoMudo : TemaPlant.BTexto2 }
                });
            return sp;
        }

        static string IconeApoio(No n)
        {
            if (!n.TemApoio) return null;
            if (n.ApoioX && n.ApoioY && n.ApoioGiro) return "engaste";
            if (n.ApoioX && n.ApoioY) return "rotula";
            if (n.ApoioY) return "rolo";
            if (n.ApoioX) return "roloh";
            return "engaste";
        }

        void AtualizarArvore()
        {
            sincronizando = true;
            Arvore.Items.Clear();
            var raiz = Item("painel", "Modelo", arquivo != null ? System.IO.Path.GetFileNameWithoutExtension(arquivo) : null, null, null, true, "Raiz do modelo atual. Expanda os grupos abaixo para inspecionar e selecionar nós, barras, seções, materiais, apoios e cargas.");
            var nos = Item("no", "Nós", null, M.Nos.Count, M.Nos.Cast<object>().ToList(), M.Nos.Count < 40, "Clique para selecionar todos os nós do modelo na vista.");
            foreach (No n in M.Nos)
            {
                var it = Item("no", "Nó " + n.Id, "(" + Num.Fmt(n.X, 2) + "; " + Num.Fmt(n.Y, 2) + ")", null, n, false,
                    "Nó " + n.Id + (n.TemApoio ? " · apoio: " + NomeApoio(n) : "") + (n.TemCarga ? " · com carga" : "") + (n.Placa != null ? " · com placa de base" : "") + ". Clique para selecionar e editar no painel Propriedades.",
                    IconeApoio(n), n.TemCarga ? "cargano" : null, n.Placa != null ? "placa" : null);
                nos.Items.Add(it);
            }
            var barras = Item("barra", "Barras", null, M.Barras.Count, M.Barras.Cast<object>().ToList(), M.Barras.Count < 40, "Clique para selecionar todas as barras do modelo na vista.");
            foreach (Barra b in M.Barras)
                barras.Items.Add(Item("barra", "Barra " + b.Id, b.Secao, null, b, false, "Barra " + b.Id + " (" + b.Secao + ", " + b.Material + ")" + (b.TemCarga ? " · com carga" : "") + (b.RotulaInicio || b.RotulaFim ? " · com rótula" : "") + ". Clique para selecionar e editar.",
                    b.TemCarga ? "cargadist" : null, b.RotulaInicio || b.RotulaFim ? "rotulab" : null));
            var secs = Item("eixo", "Seções em uso", null, M.Barras.Select(b => b.Secao).Distinct().Count(), null, true, "Agrupa as barras do modelo pelo perfil transversal aplicado.");
            foreach (var g in M.Barras.GroupBy(b => b.Secao)) secs.Items.Add(Item("eixo", g.Key, null, g.Count(), g.Cast<object>().ToList(), false, "Clique para selecionar todas as barras com o perfil \"" + g.Key + "\"."));
            var mats = Item("pincel", "Materiais em uso", null, M.Barras.Select(b => b.Material).Distinct().Count(), null, true, "Agrupa as barras do modelo pelo material estrutural aplicado.");
            foreach (var g in M.Barras.GroupBy(b => b.Material)) mats.Items.Add(Item("pincel", g.Key, null, g.Count(), g.Cast<object>().ToList(), false, "Clique para selecionar todas as barras com o material \"" + g.Key + "\"."));
            var apLista = M.Nos.Where(n => n.TemApoio).Cast<object>().ToList();
            var apoios = Item("engaste", "Apoios", null, apLista.Count, apLista, false, "Clique para selecionar todos os nós que possuem apoio ou mola.");
            var cgLista = M.Nos.Where(n => n.TemCarga).Cast<object>().Concat(M.Barras.Where(b => b.TemCarga)).ToList();
            var cargas = Item("cargano", "Com carga", null, cgLista.Count, cgLista, false, "Clique para selecionar os itens que possuem carga aplicada no caso ativo.");
            raiz.Items.Add(nos); raiz.Items.Add(barras); raiz.Items.Add(secs); raiz.Items.Add(mats); raiz.Items.Add(apoios); raiz.Items.Add(cargas);
            Arvore.Items.Add(raiz);
            sincronizando = false;
        }

        static string NomeApoio(No n)
        {
            if (n.ApoioX && n.ApoioY && n.ApoioGiro) return "engaste";
            if (n.ApoioX && n.ApoioY) return "fixo";
            if (n.ApoioY) return "móvel";
            if (n.ApoioX) return "móvel em X";
            return "mola/giro";
        }

        static TreeViewItem Item(string icone, string txt, string detalhe, int? contagem, object tag, bool aberto, string dica, params string[] marcas)
        {
            return new TreeViewItem { Header = CabArvore(icone, txt, detalhe, contagem, marcas), Tag = tag, IsExpanded = aberto, Foreground = TemaPlant.BTexto1, FontSize = 12, ToolTip = dica };
        }

        void ArvoreMudou(object s, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sincronizando) return;
            var it = Arvore.SelectedItem as TreeViewItem;
            if (it == null || it.Tag == null) return;
            sincronizando = true;
            if (it.Tag is No || it.Tag is Barra) Vista.Selecionar(new[] { it.Tag });
            else if (it.Tag is List<object> lista)
            {
                // grupo: seleciona os itens de um tipo so (barras tem preferencia)
                var bs = lista.OfType<Barra>().Cast<object>().ToList();
                Vista.Selecionar(bs.Count > 0 ? bs : lista.OfType<No>().Cast<object>().ToList());
            }
            sincronizando = false;
        }

        void SincronizarArvore()
        {
            if (sincronizando || Vista.Sel.Count != 1) return;
            sincronizando = true;
            foreach (TreeViewItem raiz in Arvore.Items)
                foreach (TreeViewItem grupo in raiz.Items)
                    foreach (TreeViewItem it in grupo.Items)
                        if (it.Tag == Vista.Sel[0]) { grupo.IsExpanded = true; it.IsSelected = true; it.BringIntoView(); }
            sincronizando = false;
        }

        // ---------- tabelas ----------

        internal void FiltrarCriticos(bool v) { soCriticos = v; MontarFiltroVerif(); AtualizarTabelas(); }

        // filtro da aba Verificação: todas as barras ou só as críticas (reprovadas ou acima de 90 %)
        void MontarFiltroVerif()
        {
            BarraVerif.Children.Remove(BarraVerif.Children.OfType<Border>().FirstOrDefault());
            var seg = UiPlaca.Segmentado(new List<(string, bool, Action, string)>
            {
                ("Todas", !soCriticos, () => { soCriticos = false; MontarFiltroVerif(); AtualizarTabelas(); }, "Mostra todas as barras, das mais críticas para as menos."),
                ("Só críticos", soCriticos, () => { soCriticos = true; MontarFiltroVerif(); AtualizarTabelas(); }, "Mostra só as barras que não passam ou usam mais de 90 % da capacidade."),
            }, 24, 11.5);
            BarraVerif.Children.Insert(0, seg);
        }

        void AtualizarTabelas()
        {
            sincronizando = true;
            bool ok = Vista.Resolvido;
            TabNos.ItemsSource = M.Nos.Select(n => new LinhaNo
            {
                Obj = n, Id = n.Id, X = Num.Fmt(n.X, 3), Y = Num.Fmt(n.Y, 3), Apoio = n.TemApoio ? NomeApoio(n) : "",
                // na envoltoria, cada celula mostra "mínimo / máximo" entre as combinacoes
                DX = ok && n.Res != null ? FaixaNo(n, 0, 1000, 3) : "", DY = ok && n.Res != null ? FaixaNo(n, 1, 1000, 3) : "",
                Giro = ok && n.Res != null ? FaixaNo(n, 2, 1, 6) : "",
                Rx = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 3, 1, 2) : "", Ry = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 4, 1, 2) : "",
                Rm = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 5, 1, 2) : ""
            }).ToList();
            TabBarras.ItemsSource = M.Barras.Select(b =>
            {
                var l = new LinhaBarra { Obj = b, Id = b.Id, Nos = b.N1 + " → " + b.N2, Perfil = b.Secao + (b.EixoFraco ? " (deitado)" : ""), L = Num.Fmt(M.Comp(b), 3) };
                if (ok && b.Res != null)
                {
                    l.N = FaixaBarra(b, 0); l.Q = FaixaBarra(b, 1); l.Mm = FaixaBarra(b, 2);
                    double f = Flecha(b);
                    l.Flecha = Num.Fmt(f * 1000, 2) + (Math.Abs(f) > 1e-9 ? "  (L/" + Math.Round(M.Comp(b) / Math.Abs(f)) + ")" : "");
                }
                return l;
            }).ToList();
            AbasTabela.ToolTip = ok ? "Resultado: " + NomeResultado() + ". Clique numa linha para destacar o item na vista." : "Tabelas de nós, barras, verificação e soldas (calcule a estrutura para ver os esforços e deslocamentos).";
            var lv = M.Barras.Select(b =>
            {
                var l = new LinhaVerif { Obj = b, Id = b.Id, Perfil = b.Secao };
                if (!ok || b.Res == null) { l.Situacao = "sem resultados"; l.SitTxt = "Sem resultados"; return l; }
                Verif v = Vista.Verificar(b);
                if (!v.Aplicavel) { l.Situacao = v.Motivo; l.SitTxt = v.Motivo; return l; }
                l.Situacao = v.Passa ? "passa" : "NÃO PASSA"; l.SitTxt = v.Passa ? "Passa" : "Não passa"; l.Ap = Num.Fmt(v.Ap * 100, 0) + "%"; l.Critico = v.Critico;
                l.ApNum = v.Ap * 100; l.Pass = v.Passa; l.CorAp = UiPlaca.CorAp(v.Ap, v.Passa); l.BarraVis = Visibility.Visible;
                var cs = UiPlaca.Cores(v.Passa ? (v.Ap > 0.9 ? UiPlaca.Nivel.Alerta : UiPlaca.Nivel.Ok) : UiPlaca.Nivel.Erro); l.SitFg = cs.fg; l.SitBg = cs.bg; l.SitBd = cs.bd;
                if (v is VerifMotor) { l.Tensao = l.Corte = l.Flecha = l.Flamb = l.LamPl = l.LamFo = l.Esb = "—"; return l; }   // motor Python: so taxa e criterio
                l.Tensao = Num.Fmt(v.ApTensao * 100, 0) + "%"; l.Corte = Num.Fmt(v.ApCorte * 100, 0) + "%"; l.Flecha = Num.Fmt(v.ApFlecha * 100, 0) + "%";
                l.Flamb = v.Nc > 1e-6 ? Num.Fmt(v.ApFlamb * 100, 0) + "%" : "—";
                Func<Flamb, string> lam = f => f == null ? "—" : f.Instavel ? "∞ (sem travamento)" : Num.Fmt(f.Lambda, 0) + "  (K " + Num.Fmt(f.K, 2) + ")";
                l.LamPl = lam(v.NoPlano); l.LamFo = lam(v.ForaPlano);
                l.Esb = v.LimEsbeltez > 0 ? Num.Fmt(v.ApEsbeltez * 100, 0) + "%  (" + (v.LimEsbeltez == 200 ? "KL/r" : "L/r") + " " + Num.Fmt(v.ApEsbeltez * v.LimEsbeltez, 0) + (v.ApEsbeltez > 1 ? " > " : " ≤ ") + Num.Fmt(v.LimEsbeltez, 0) + ")" : "—";
                return l;
            }).OrderBy(x => x.Pass ? 1 : 0).ThenByDescending(x => x.ApNum).ToList();
            int totVerif = lv.Count;
            if (soCriticos) lv = lv.Where(x => !x.Pass || x.ApNum > 90).ToList();
            TabVerif.ItemsSource = lv;
            ContVerif.Text = !ok ? "" : soCriticos ? lv.Count + " de " + totVerif + " barras" : totVerif + (totVerif == 1 ? " barra" : " barras");
            VazioNos.Visibility = M.Nos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VazioBarras.Visibility = M.Barras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VazioVerif.Visibility = M.Barras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VazioVerif.Text = M.Barras.Count == 0 ? "Sem barras para verificar." : "";
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif })
            {
                dg.AutoGeneratingColumn -= Coluna; dg.AutoGeneratingColumn += Coluna;
            }
            if (!verifHook)
            {
                verifHook = true;
                TabVerif.SizeChanged += (s2, e2) => AjustarColunasVerif();
                TabVerif.Loaded += (s2, e2) => AjustarColunasVerif();
            }
            AjustarColunasVerif();
            AtualizarSoldas();
            sincronizando = false;
        }

        // colunas de texto/identificacao ficam a esquerda; numeros (e unidades no cabecalho) a direita
        static readonly HashSet<string> ColEsq = new HashSet<string> { "Id", "Nos", "Perfil", "Apoio", "Situacao", "Critico", "Ponta", "Sit" };
        static Style estiloNumDir, estiloCabDir;

        static DataTemplate TemplateXaml(string xaml)
        {
            return (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + xaml + "</DataTemplate>");
        }

        internal static void Coluna(object s, DataGridAutoGeneratingColumnEventArgs e)
        {
            var pd = e.PropertyDescriptor as PropertyDescriptor;
            if (pd == null) return;
            if (!pd.IsBrowsable) { e.Cancel = true; return; }
            string nome = pd.Name, titulo = pd.DisplayName;
            if (nome == "Ap")
            {
                // barra de capacidade (GUIA 5.6): faixa colorida + valor
                e.Column = new DataGridTemplateColumn
                {
                    Header = "Aprov.", Width = 118, SortMemberPath = "ApNum", CanUserSort = true,
                    CellTemplate = TemplateXaml("<Grid VerticalAlignment=\"Center\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\" MinWidth=\"40\"/><ColumnDefinition Width=\"44\"/></Grid.ColumnDefinitions>" +
                        "<ProgressBar Minimum=\"0\" Maximum=\"100\" Value=\"{Binding ApNum, Mode=OneWay}\" Foreground=\"{Binding CorAp}\" Height=\"6\" Visibility=\"{Binding BarraVis}\"/>" +
                        "<TextBlock Grid.Column=\"1\" Text=\"{Binding Ap}\" TextAlignment=\"Right\" FontWeight=\"SemiBold\"/></Grid>")
                };
            }
            else if (nome == "Situacao")
            {
                e.Column = new DataGridTemplateColumn
                {
                    Header = titulo, SortMemberPath = "Situacao", MinWidth = 90,
                    CellTemplate = TemplateXaml("<Border HorizontalAlignment=\"Left\" CornerRadius=\"3\" Padding=\"6,1\" BorderThickness=\"1\" Background=\"{Binding SitBg}\" BorderBrush=\"{Binding SitBd}\">" +
                        "<TextBlock Text=\"{Binding SitTxt}\" FontSize=\"11\" FontWeight=\"SemiBold\" Foreground=\"{Binding SitFg}\"/></Border>")
                };
            }
            else if (e.Column is DataGridTextColumn tc && !ColEsq.Contains(nome))
            {
                if (estiloNumDir == null)
                {
                    estiloNumDir = new Style(typeof(TextBlock)); estiloNumDir.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                    estiloCabDir = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), Application.Current.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
                    estiloCabDir.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
                }
                tc.ElementStyle = estiloNumDir; tc.HeaderStyle = estiloCabDir;
            }
            e.Column.Header = nome == "Ap" ? "Aprov." : nome == "LamPl" ? "KL/r plano" : nome == "LamFo" ? "KL/r fora" : titulo;
        }

        void SelecionarDaTabela(DataGrid dg)
        {
            if (sincronizando) return;
            var objs = new List<object>();
            foreach (var it in dg.SelectedItems)
            {
                if (it is LinhaNo ln) objs.Add(ln.Obj);
                else if (it is LinhaBarra lb) objs.Add(lb.Obj);
                else if (it is LinhaVerif lv) objs.Add(lv.Obj);
            }
            if (objs.Count == 0) return;
            sincronizando = true;
            Vista.Selecionar(objs);
            sincronizando = false;
        }

        void SincronizarTabelas()
        {
            if (sincronizando) return;
            sincronizando = true;
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif })
            {
                dg.SelectedItems.Clear();
                if (dg.ItemsSource == null) continue;
                foreach (var it in dg.ItemsSource)
                {
                    object o = it is LinhaNo ln ? ln.Obj : it is LinhaBarra lb ? (object)lb.Obj : it is LinhaVerif lv ? lv.Obj : null;
                    if (o != null && Vista.Sel.Contains(o)) { dg.SelectedItems.Add(it); dg.ScrollIntoView(it); }
                }
            }
            sincronizando = false;
        }

        // ---------- avisos (checagem antes do calculo) ----------

        void Diagnosticar()
        {
            var lista = new List<(string, string, List<object>)>();   // nivel, texto, itens
            if (M.Barras.Count > 0 && !M.Nos.Any(n => n.TemApoio))
                lista.Add(("erro", "Nenhum apoio. Selecione os nós da base e use Modelo › Engaste/Fixo/Móvel.", new List<object>()));
            foreach (Barra b in M.Barras)
            {
                if (Biblioteca.AcharSecao(b.Secao) == null) lista.Add(("erro", "Barra " + b.Id + ": seção \"" + b.Secao + "\" não reconhecida.", new List<object> { b }));
                if (Biblioteca.AcharMaterial(b.Material) == null) lista.Add(("erro", "Barra " + b.Id + ": material \"" + b.Material + "\" não reconhecido.", new List<object> { b }));
                if (M.Comp(b) < 1e-6) lista.Add(("erro", "Barra " + b.Id + " com comprimento zero.", new List<object> { b }));
            }
            foreach (No n in M.Nos.Where(x => !x.TemApoio))
            {
                var bs = M.Barras.Where(b => b.N1 == n.Id || b.N2 == n.Id).ToList();
                if (bs.Count == 0) { lista.Add(("aviso", "Nó " + n.Id + " solto (sem barra).", new List<object> { n })); continue; }
                bool rot = bs.All(b => b.N1 == n.Id ? b.RotulaInicio : b.RotulaFim);
                bool alinh = bs.All(b => Math.Abs(Math.Sin((M.Ang(b) - M.Ang(bs[0])) * Math.PI / 180)) < 1e-6);
                if (rot && alinh) lista.Add(("erro", "Nó " + n.Id + ": barras rotuladas e alinhadas — mecanismo (o nó não resiste na direção perpendicular).", new List<object> { n }));
            }
            bool temCarga = M.Nos.Any(n => n.TemCarga) || M.Barras.Any(b => b.TemCarga) || M.PesoProprio || M.Nos.Any(n => n.RecalqueX != 0 || n.RecalqueY != 0);
            if (M.Barras.Count > 0 && !temCarga) lista.Add(("info", "Nenhuma carga aplicada: os esforços serão nulos. Use a aba Ações.", new List<object>()));
            if (Vista.Resolvido)
            {
                var falham = M.Barras.Where(b => { var v = Vista.Verificar(b); return v.Aplicavel && !v.Passa; }).Cast<object>().ToList();
                if (falham.Count > 0) lista.Add(("aviso", Pl.N(falham.Count, "barra não passa", "barras não passam") + " na verificação. Clique para selecionar; Dimensionamento › Verificar barra sugere um perfil.", falham));
                foreach (No n in M.Nos.Where(x => x.Placa != null))
                {
                    ResPlaca rp = PlacaPior(n);
                    if (rp.Erro != "") lista.Add(("aviso", "Placa do nó " + n.Id + ": " + rp.Erro, new List<object> { n }));
                    else if (rp.TemEsforcos && (!rp.Passa || rp.EspNec > n.Placa.Esp))
                    {
                        var mot = new List<string>();
                        if (!rp.ChOk && rp.EspacOk) mot.Add(rp.Ch != null ? "chumbador" : "parafuso");
                        if (!rp.EspacOk) mot.Add("espaçamento entre chumbadores");
                        if (rp.EspNec > n.Placa.Esp) mot.Add("chapa");
                        if (rp.Inclinada) mot.Add("barra inclinada: chapa e solda não verificadas");
                        lista.Add(("aviso", "Placa do nó " + n.Id + " não passa (" + string.Join(", ", mot) + ").", new List<object> { n }));
                    }
                }
            }
            else if (M.Barras.Count > 0 && M.Nos.Any(n => n.TemApoio) && erroCalculo != "")
                lista.Add(("erro", erroCalculo, new List<object>()));

            ListaAvisos.Items.Clear();
            foreach (var (nivel, texto, objs) in lista)
            {
                var it = new ListBoxItem
                {
                    Content = (nivel == "erro" ? "⛔  " : nivel == "aviso" ? "⚠  " : "ℹ  ") + texto, Tag = objs.Count > 0 ? objs : null,
                    Foreground = nivel == "erro" ? TemaPlant.BErroClaro : nivel == "aviso" ? TemaPlant.BAlertaClaro : TemaPlant.BTexto2,
                    Padding = new Thickness(6, 3, 6, 3), Cursor = objs.Count > 0 ? System.Windows.Input.Cursors.Hand : null,
                    ToolTip = objs.Count > 0 ? texto + " (clique para selecionar os itens envolvidos na vista)." : texto
                };
                ListaAvisos.Items.Add(it);
            }
            if (lista.Count == 0) ListaAvisos.Items.Add(new ListBoxItem { Content = "✓  Nenhum problema encontrado.", Foreground = TemaPlant.BSucesso, Padding = new Thickness(6, 3, 6, 3), ToolTip = "A geometria, os apoios, as cargas e as verificações não apresentam alertas." });
            int erros = lista.Count(x => x.Item1 == "erro"), avisos = lista.Count(x => x.Item1 == "aviso");
            string resumo = (erros > 0 ? "⛔ " + erros + "  " : "") + (avisos > 0 ? "⚠ " + avisos : "");
            PainelAvisos.Title = resumo == "" ? "Avisos" : "Avisos (" + resumo.Trim() + ")";
            SeloAvisos.Content = string.Join("  ", new[] { erros > 0 ? "⛔ " + erros + (erros == 1 ? " erro" : " erros") : null, avisos > 0 ? "⚠ " + avisos + (avisos == 1 ? " aviso" : " avisos") : null }.Where(x => x != null));
            SeloAvisos.Visibility = resumo == "" ? Visibility.Collapsed : Visibility.Visible;
            // um erro novo traz o painel para frente uma vez
            if (erros > 0 && !avisosJaMostrados) { PainelAvisos.Show(); PainelAvisos.IsSelected = true; avisosJaMostrados = true; }
            if (erros == 0) avisosJaMostrados = false;
        }
    }
}
