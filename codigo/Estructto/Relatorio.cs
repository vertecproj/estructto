// Relatorio (memorial de calculo): FlowDocument com modelo, casos, combinacoes, figuras, reacoes, esforcos,
// verificacao e placas. Imprime pela caixa de impressao do Windows ("Microsoft Print to PDF" gera o PDF).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        async void AbrirRelatorio()
        {
            if (M.Barras.Count == 0) { Log("Modelo vazio: nada para relatar."); return; }
            if (MotorAtivo)   // motor Python: relatório NBR do motor + anexo das placas do Estructto (docs/RELATORIO-UNIFICADO.md)
            {
                if (!await PrepararMotor(true)) return;
                RelatorioMotor.EmpresaProjeto = (string)Esp?.Json["config"]?["projeto"]?["empresa"];
                RelatorioMotor.Abrir(this, NomeRelatorio(), AnexoPlacas);
                return;
            }
            if (!Vista.Resolvido) Log("⚠ Estrutura sem resultados: o relatório sai só com os dados do modelo. Veja o painel Avisos.");
            var j = new JanelaRelatorio(MontarRelatorio, NomeRelatorio()) { Owner = this };
            j.Show();
        }

        string NomeRelatorio() { return arquivo == null ? "modelo" : System.IO.Path.GetFileNameWithoutExtension(arquivo); }

        // Anexo do relatório do motor: tabela e memorial das placas de base (chumbadores e solda), que o motor não calcula.
        // null = modelo sem placa (o relatório do motor sai sozinho).
        internal FlowDocument AnexoPlacas()
        {
            var placas = M.Nos.Where(n => n.Placa != null).ToList();
            if (placas.Count == 0) return null;
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, Foreground = Tinta, Background = Brushes.White };
            TabelaPlacas(doc, placas, true);
            foreach (No n in placas) MemorialPlaca(doc, n);
            return doc;
        }

        // Teste/automacao: grava as paginas do relatorio como PNG (A4) na pasta indicada.
        void SalvarRelatorioPng(string pasta, bool completo = false)
        {
            try
            {
                Directory.CreateDirectory(pasta);
                var doc = MontarRelatorio(completo);
                int n = JanelaRelatorio.PaginasPng(doc, pasta);
                Log("Relatório: " + Pl.N(n, "página", "páginas") + " em " + pasta);
            }
            catch (Exception ex) { Log("Erro no relatório: " + ex.Message); }
        }

        // ---------- montagem ----------

        static readonly Brush Tinta = TemaPlant.BImpTinta;
        static readonly Brush Cinza = TemaPlant.BImpCinza;
        static readonly Brush Linha = TemaPlant.BImpLinha;
        static readonly Brush Cabeca = TemaPlant.BImpCabeca;

        FlowDocument MontarRelatorio() { return MontarRelatorio(false); }

        // completo = basico + memorial de calculo com o desenvolvimento de todas as contas
        internal FlowDocument MontarRelatorio(bool completo)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, Foreground = Tinta, Background = Brushes.White,
                PagePadding = new Thickness(56), ColumnWidth = double.PositiveInfinity, PageWidth = 794, PageHeight = 1123
            };
            string nome = arquivo == null ? "Sem título" : System.IO.Path.GetFileName(arquivo);
            doc.Blocks.Add(new Paragraph(new Run((completo ? "Memorial de cálculo completo" : "Relatório de cálculo") + " — pórtico plano")) { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            doc.Blocks.Add(P("Arquivo: " + nome + "   ·   " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "   ·   Estructto", Cinza));
            doc.Blocks.Add(P("Resultados mostrados: " + (Vista.Resolvido ? NomeResultado() : "— (sem cálculo: " + (erroCalculo == "" ? "modelo incompleto" : erroCalculo) + ")") +
                "   ·   Unidades: m, kN, kN/m, kN·m; deslocamentos em mm.", Cinza));
            doc.Blocks.Add(P("Análise linear elástica de pórtico plano (método da rigidez direta" + (M.Timoshenko ? ", vigas de Timoshenko" : ", vigas de Euler-Bernoulli") + ")" +
                (M.PesoProprio ? "; peso próprio incluído no caso marcado" : "") + ".", Cinza));

            // 1. Modelo
            doc.Blocks.Add(Titulo("1. Modelo"));
            doc.Blocks.Add(Figura(Diagrama.Nenhum, true, "Geometria, apoios e cargas do caso " + M.Casos[M.CasoAtivo].Nome));
            doc.Blocks.Add(Tabela(new[] { "Nó", "X (m)", "Y (m)", "Apoio", "Molas / recalques" },
                M.Nos.Select(n => new[] { n.Id.ToString(), Num.Fmt(n.X, 3), Num.Fmt(n.Y, 3), n.TemApoio ? NomeApoio(n) : "—",
                    (n.MolaX != 0 || n.MolaY != 0 || n.MolaGiro != 0 ? "k = " + Num.Fmt(n.MolaX, 0) + "/" + Num.Fmt(n.MolaY, 0) + "/" + Num.Fmt(n.MolaGiro, 0) : "") +
                    (n.RecalqueX != 0 || n.RecalqueY != 0 ? " rec. " + Num.Fmt(n.RecalqueX, 1) + "/" + Num.Fmt(n.RecalqueY, 1) + " mm" : "") })));
            doc.Blocks.Add(Tabela(new[] { "Barra", "Nós", "Perfil", "Material", "L (m)", "Rótulas" },
                M.Barras.Select(b => new[] { b.Id.ToString(), b.N1 + " → " + b.N2, b.Secao + (b.EixoFraco ? " (deitado)" : ""), b.Material, Num.Fmt(M.Comp(b), 3),
                    (b.RotulaInicio ? "início " : "") + (b.RotulaFim ? "fim" : "") })));

            // 2. Casos de carga
            doc.Blocks.Add(Titulo("2. Casos de carga"));
            for (int c = 0; c < M.Casos.Count; c++)
            {
                CasoCarga cc = M.Casos[c];
                doc.Blocks.Add(new Paragraph(new Run(cc.Nome + "  (" + TipoCaso(cc.Tipo) + ")" + (cc.PesoProprio && M.PesoProprio ? " + peso próprio" : ""))) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
                var nos = M.Nos.Select(n => new { n, q = CargaNo(n, c) }).Where(x => x.q[0] != 0 || x.q[1] != 0 || x.q[2] != 0).ToList();
                var bars = M.Barras.Select(b => new { b, q = CargaBarra(b, c) }).Where(x => x.q.Any(v => v != 0)).ToList();
                if (nos.Count == 0 && bars.Count == 0) { doc.Blocks.Add(P(cc.PesoProprio && M.PesoProprio ? "Só peso próprio." : "Sem cargas.", Cinza)); continue; }
                if (nos.Count > 0)
                    doc.Blocks.Add(Tabela(new[] { "Nó", "Fx (kN)", "Fy (kN)", "Mz (kN·m)" }, nos.Select(x => new[] { x.n.Id.ToString(), Num.Fmt(x.q[0], 2), Num.Fmt(x.q[1], 2), Num.Fmt(x.q[2], 2) })));
                if (bars.Count > 0)
                    doc.Blocks.Add(Tabela(new[] { "Barra", "qx (kN/m)", "qy (kN/m)", "q⊥ início", "q⊥ fim" }, bars.Select(x => new[] { x.b.Id.ToString(), Num.Fmt(x.q[0], 2), Num.Fmt(x.q[1], 2), Num.Fmt(x.q[2], 2), Num.Fmt(x.q[3], 2) })));
            }

            // 3. Combinacoes
            doc.Blocks.Add(Titulo("3. Combinações"));
            if (M.Combinacoes.Count == 0) doc.Blocks.Add(P("Nenhuma combinação definida: resultados por caso isolado.", Cinza));
            else
                doc.Blocks.Add(Tabela(new[] { "Combinação", "Tipo" }.Concat(M.Casos.Select(c => c.Nome)).ToArray(),
                    M.Combinacoes.Select(k => new[] { k.Nome, k.Tipo }.Concat(Enumerable.Range(0, M.Casos.Count).Select(i => Num.Fmt(k.Fator(i), 2))).ToArray())));

            if (!Vista.Resolvido) return doc;

            // 4. Diagramas
            doc.Blocks.Add(Titulo("4. Diagramas — " + NomeResultado()));
            doc.Blocks.Add(Figura(Diagrama.Deformada, false, "Deformada (ampliada)." + Vista.LegendaDeformada()));
            doc.Blocks.Add(Figura(Diagrama.Normal, false, "Esforço normal N (kN): + tração."));
            doc.Blocks.Add(Figura(Diagrama.Cortante, false, "Esforço cortante Q (kN)."));
            doc.Blocks.Add(Figura(Diagrama.Momento, false, "Momento fletor M (kN·m), do lado tracionado; + traciona a fibra de baixo."));
            if (Vista.Envoltoria != null) doc.Blocks.Add(P("Envoltória: as duas curvas são o máximo e o mínimo entre as combinações " + string.Join(", ", Vista.NomesEnv) + ".", Cinza));

            // 5. Reacoes e deslocamentos
            doc.Blocks.Add(Titulo("5. Reações e deslocamentos"));
            doc.Blocks.Add(Tabela(new[] { "Nó", "Rx (kN)", "Ry (kN)", "M (kN·m)" },
                M.Nos.Where(n => n.TemApoio).Select(n => new[] { n.Id.ToString(), FaixaNo(n, 3, 1, 2), FaixaNo(n, 4, 1, 2), FaixaNo(n, 5, 1, 2) })));
            doc.Blocks.Add(Tabela(new[] { "Nó", "Desl. X (mm)", "Desl. Y (mm)", "Giro (rad)" },
                M.Nos.Select(n => new[] { n.Id.ToString(), FaixaNo(n, 0, 1000, 3), FaixaNo(n, 1, 1000, 3), FaixaNo(n, 2, 1, 6) })));

            // 6. Esforcos
            doc.Blocks.Add(Titulo("6. Esforços nas barras (mínimo / máximo)"));
            doc.Blocks.Add(Tabela(new[] { "Barra", "N (kN)", "Q (kN)", "M (kN·m)", "Flecha (mm)" },
                M.Barras.Select(b => new[] { b.Id.ToString(), FaixaBarra(b, 0), FaixaBarra(b, 1), FaixaBarra(b, 2), Num.Fmt(Flecha(b) * 1000, 2) })));

            // 7. Verificacao
            doc.Blocks.Add(Titulo("7. Verificação das barras"));
            doc.Blocks.Add(Figura(Diagrama.Verificacao, false, "Aproveitamento: verde passa, amarelo acima de 90%, vermelho não passa."));
            doc.Blocks.Add(P("Critério simplificado: tensão |N|/A + |M|/W ≤ fy/n, corte Q/As ≤ 0,577·fy/n, flecha ≤ L/" + Num.Fmt(M.LimiteFlecha, 0) +
                " (balanço: deslocamento total da ponta ≤ H/" + Num.Fmt(M.LimiteFlecha, 0) + "), flambagem de Euler/Johnson nos dois planos (por barra contínua, K pela análise de flambagem), interação N + M, KL/r ≤ 200 (só tração: L/r ≤ 300); n = " + Num.Fmt(M.Seguranca, 2) + ". Não substitui a NBR 8800 (sem flambagem lateral com torção nem local; sem 2ª ordem global).", Cinza));
            doc.Blocks.Add(Tabela(new[] { "Barra", "Perfil", "Situação", "Aprov.", "Limitado por", "Tensão", "Corte", "Flecha", "Flamb." },
                M.Barras.Select(b =>
                {
                    Verif v = Vista.Verificar(b);
                    if (!v.Aplicavel) return new[] { b.Id.ToString(), b.Secao, v.Motivo, "", "", "", "", "", "" };
                    if (v is VerifMotor) return new[] { b.Id.ToString(), b.Secao, v.Passa ? "passa" : "NÃO PASSA", Num.Fmt(v.Ap * 100, 0) + "%", v.Critico, "—", "—", "—", "—" };
                    return new[] { b.Id.ToString(), b.Secao, v.Passa ? "passa" : "NÃO PASSA", Num.Fmt(v.Ap * 100, 0) + "%", v.Critico,
                        Num.Fmt(v.ApTensao * 100, 0) + "%", Num.Fmt(v.ApCorte * 100, 0) + "%", Num.Fmt(v.ApFlecha * 100, 0) + "%", v.Nc > 1e-6 ? Num.Fmt(v.ApFlamb * 100, 0) + "%" : "—" };
                })));

            // 8. Placas
            var placas = M.Nos.Where(n => n.Placa != null).ToList();
            if (placas.Count > 0)
            {
                doc.Blocks.Add(Titulo("8. Placas de base"));
                TabelaPlacas(doc, placas, completo);
            }
            if (completo) Memorial(doc, placas);
            return doc;
        }

        void TabelaPlacas(FlowDocument doc, List<No> placas, bool completo)
        {
            doc.Blocks.Add(Tabela(new[] { "Nó", "Placa (mm)", "Parafusos", "N / V / M no pé", "Tração máx. (kN)", "Segurança paraf.", "Esp. necessária", "Situação" },
                placas.Select(n =>
                {
                    ResPlaca r = PlacaPior(n);
                    if (r.Erro != "" && !r.TemEsforcos) return new[] { n.Id.ToString(), "", "", "", "", "", "", r.Erro };
                    bool ok = r.Passa && r.EspNec <= n.Placa.Esp;
                    return new[] { n.Id.ToString(), Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + Num.Fmt(n.Placa.Esp, 1), r.Paraf.Count + " × " + (r.Ch != null ? r.Ch.Nome : "M" + n.Placa.Diametro + " " + n.Placa.Classe),
                        Num.Fmt(r.N, 1) + " / " + Num.Fmt(r.V, 1) + " / " + Num.Fmt(r.M, 1), Num.Fmt(r.Tmax, 2), double.IsInfinity(r.NComb) ? "∞" : Num.Fmt(r.NComb, 2),
                        Num.Fmt(r.EspNec, 1) + " mm", ok ? "passa" : "NÃO PASSA" };
                })));
            doc.Blocks.Add(P("Modelo de placa rígida (ver Placa de base no programa). Não verifica pressão no concreto." + (completo ? "" : " O desenvolvimento (parafusos/chumbadores, chapa e solda) está no relatório completo."), Cinza));
        }

        static string TipoCaso(string t) { return t == "G" ? "permanente" : t == "Q" ? "variável" : t == "V" ? "vento" : t == "E" ? "excepcional" : t; }

        // carga de um caso (o ativo esta nos campos; os outros em OutrasCargas)
        double[] CargaNo(No n, int caso)
        {
            if (caso == M.CasoAtivo) return new[] { n.Fx, n.Fy, n.Mz };
            var c = n.OutrasCargas == null ? null : n.OutrasCargas.FirstOrDefault(x => x.Caso == caso);
            return c == null ? new double[3] : new[] { c.Fx, c.Fy, c.Mz };
        }

        double[] CargaBarra(Barra b, int caso)
        {
            if (caso == M.CasoAtivo) return new[] { b.QGlobalX, b.QGlobalY, b.QLocalIni, b.QLocalFim };
            var c = b.OutrasCargas == null ? null : b.OutrasCargas.FirstOrDefault(x => x.Caso == caso);
            return c == null ? new double[4] : new[] { c.QX, c.QY, c.QIni, c.QFim };
        }

        static Paragraph P(string s, Brush cor = null) { return new Paragraph(new Run(s)) { Foreground = cor ?? Tinta, Margin = new Thickness(0, 2, 0, 4) }; }

        static Paragraph Titulo(string s)
        {
            return new Paragraph(new Run(s)) { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6), BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 3) };
        }

        static Table Tabela(string[] cab, IEnumerable<string[]> linhas)
        {
            var t = new Table { CellSpacing = 0, BorderBrush = Linha, BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 4, 0, 8), FontSize = 9.5 };
            foreach (var _ in cab) t.Columns.Add(new TableColumn());
            var g = new TableRowGroup();
            var h = new TableRow { Background = Cabeca, FontWeight = FontWeights.SemiBold };
            foreach (string s in cab) h.Cells.Add(Celula(s));
            g.Rows.Add(h);
            foreach (string[] l in linhas)
            {
                var r = new TableRow();
                foreach (string s in l) r.Cells.Add(Celula(s, s == "NÃO PASSA" ? TemaPlant.BImpVermelho : null));
                g.Rows.Add(r);
            }
            t.RowGroups.Add(g);
            return t;
        }

        static TableCell Celula(string s, Brush cor = null)
        {
            return new TableCell(new Paragraph(new Run(s ?? "")) { Margin = new Thickness(0), Foreground = cor ?? Tinta }) { Padding = new Thickness(4, 2, 4, 2), BorderBrush = Linha, BorderThickness = new Thickness(0.5) };
        }

        Block Figura(Diagrama d, bool cargas, string legenda)
        {
            var v = new Vista
            {
                M = M, Impressao = true, Diag = d, Resolvido = Vista.Resolvido, MostrarCargas = cargas, MostrarReacoes = d == Diagrama.Nenhum ? false : false,
                MostrarValores = true, EscalaDiag = Vista.EscalaDiag, EscalaDef = Vista.EscalaDef,
                Envoltoria = Vista.Envoltoria, EnvNos = Vista.EnvNos, NomesEnv = Vista.NomesEnv, Verificar = Vista.Verificar
            };
            BitmapSource img = v.Imagem(680, 330);
            var sec = new Section { Margin = new Thickness(0, 4, 0, 8) };
            sec.Blocks.Add(new BlockUIContainer(new Image { Source = img, Width = 680, Height = 330, Stretch = Stretch.Uniform }));
            sec.Blocks.Add(new Paragraph(new Run(legenda)) { FontSize = 9.5, Foreground = Cinza, Margin = new Thickness(0, 2, 0, 0), TextAlignment = TextAlignment.Center });
            return sec;
        }
    }

    // ---------- janela de visualizacao/impressao ----------

    public class JanelaRelatorio : JanelaEscura
    {
        FlowDocument doc;
        readonly Func<bool, FlowDocument> montar;
        readonly FlowDocumentScrollViewer leitor = new FlowDocumentScrollViewer { Background = TemaPlant.BBgCanvas, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly string nome;

        public JanelaRelatorio(Func<bool, FlowDocument> montar, string nome)
        {
            this.montar = montar; this.nome = nome;
            Width = 900; Height = 860; ShowInTaskbar = true;
            var raiz = new DockPanel();
            var barra = new DockPanel { Margin = new Thickness(0), LastChildFill = true };
            var faixa = new Border { Background = BPainel, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 8, 14, 8), Child = barra };
            var btImp = B("Imprimir / Salvar PDF…", Imprimir, true); btImp.ToolTip = "Abre a caixa de impressão do Windows (escolha \"Microsoft Print to PDF\" para salvar o memorial em PDF) (Enter).";
            var btFechar = B("Fechar", Close); btFechar.ToolTip = "Fecha a visualização do relatório (Esc).";
            var dir = new StackPanel { Orientation = Orientation.Horizontal };
            dir.Children.Add(btImp); dir.Children.Add(btFechar);
            DockPanel.SetDock(dir, Dock.Right); barra.Children.Add(dir);
            segHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(segHost, Dock.Left); barra.Children.Add(segHost);
            var dica = T("Para PDF, escolha a impressora \"Microsoft Print to PDF\".", 11, TemaPlant.BTexto3); dica.Margin = new Thickness(14, 0, 0, 0); dica.VerticalAlignment = VerticalAlignment.Center; dica.TextTrimming = TextTrimming.CharacterEllipsis;
            barra.Children.Add(dica);
            DockPanel.SetDock(faixa, Dock.Top);
            raiz.Children.Add(faixa);
            leitor.ToolTip = "Pré-visualização do memorial de cálculo. Role a página ou use os controles de zoom no canto inferior direito.";
            raiz.Children.Add(leitor);
            Content = raiz;
            Alternar(false);
        }

        readonly ContentControl segHost; bool completoAtual;

        void Alternar(bool completo)
        {
            completoAtual = completo;
            segHost.Content = UiPlaca.Segmentado(new List<(string, bool, Action, string)>
            {
                ("Resumo", !completo, () => { if (completoAtual) Alternar(false); }, "Geometria, cargas, diagramas, tabelas de esforços, deslocamentos e resumo das verificações."),
                ("Completo", completo, () => { if (!completoAtual) Alternar(true); }, "Inclui o memorial de cálculo passo a passo, com fórmulas e substituições numéricas de cada barra, solda e placa de base."),
            }, 28, 12);
            Mostrar(completo);
        }

        void Mostrar(bool completo)
        {
            doc = montar(completo);
            leitor.Document = doc;
            Title = (completo ? "Relatório completo — " : "Relatório — ") + nome;
        }

        void Imprimir()
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            double w = doc.PageWidth, h = doc.PageHeight;
            doc.PageWidth = dlg.PrintableAreaWidth; doc.PageHeight = dlg.PrintableAreaHeight;
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, Title);
            doc.PageWidth = w; doc.PageHeight = h;
        }

        // Pagina o documento em A4 e grava cada pagina como PNG (fundo branco). Devolve o numero de paginas.
        public static int PaginasPng(FlowDocument doc, string pasta)
        {
            doc.PageWidth = 794; doc.PageHeight = 1123; doc.ColumnWidth = double.PositiveInfinity;
            var pag = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            pag.PageSize = new Size(794, 1123);
            pag.ComputePageCount();
            for (int i = 0; i < pag.PageCount; i++)
            {
                DocumentPage p = pag.GetPage(i);
                // fundo branco e depois a pagina tal como sera impressa (VisualBrush esticaria o conteudo)
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 794, 1123));
                var bmp = new RenderTargetBitmap(794, 1123, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                bmp.Render(p.Visual);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(pasta, "pagina" + (i + 1) + ".png"))) enc.Save(fs);
            }
            return pag.PageCount;
        }
    }
}
