// Comandos do usuario: ferramentas, edicao, desfazer/refazer, arquivos, linha de comando e teclado.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        internal string arquivo;
        internal bool alterado;
        string atual;
        readonly Stack<string> desfazer = new Stack<string>(), refazer = new Stack<string>();
        readonly List<string> historico = new List<string>();
        readonly DispatcherTimer autoSalvar = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal Modelo M { get { return Vista.M; } }

        void LigarEventos()
        {
            Vista.MouseMundo += p => Coord.Text = "X " + Num.Fmt(p.X, 3).PadLeft(8) + " m   Y " + Num.Fmt(p.Y, 3).PadLeft(8) + " m";
            Vista.SelecaoMudou += SelecaoMudou;   // controle 2D escondido: só o estado do HUD (o desenho é a Vista3D)
            Vista.Verificar = b => VerificarMotor(b) ?? VerificarAtual(b, Biblioteca.AcharSecao(b.Secao));
            Insp.Verificar = b => Vista.Resolvido && b.Res != null ? Vista.Verificar(b) : null;
            Insp.Acoes = AcoesDoInspector;
            Insp.Alterado += (alvos, rotulo) =>
            {
                // mesmo caminho do antigo PropertyValueChanged: sincroniza barra continua e confirma (desfazer, recalculo, modificado)
                SincronizarMembros(alvos.OfType<Barra>());
                Confirmar("Propriedade alterada: " + rotulo + ".");
            };
            Arvore.SelectedItemChanged += ArvoreMudou;
            MontarFiltroVerif();
            TabNos.SelectionChanged += (s, e) => SelecionarDaTabela(TabNos);
            TabBarras.SelectionChanged += (s, e) => SelecionarDaTabela(TabBarras);
            TabVerif.SelectionChanged += (s, e) => SelecionarDaTabela(TabVerif);
            TabVerif.MouseDoubleClick += (s, e) => VerificarBarraConforme(null);
            ListaAvisos.SelectionChanged += (s, e) => { if (ListaAvisos.SelectedItem is ListBoxItem it && it.Tag is IEnumerable<object> objs) { if (objs.FirstOrDefault() is RefMotor r) SelecionarEsp(r); else Vista.Selecionar(objs); } };
            SeloAvisos.Click += (s, e) => { PainelAvisos.Show(); PainelAvisos.IsSelected = true; PainelAvisos.IsActive = true; };
            Comando.KeyDown += ComandoTecla;
            Comando.TextChanged += (s, e) => MostrarSugestao();
            var tmDica = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            tmDica.Tick += (s, e) => AtualizarDica(); tmDica.Start();
            PreviewKeyDown += Tecla;
            PreviewTextInput += TextoDigitado;
            Closing += (s, e) => { if (!PodeFecharTudo()) e.Cancel = true; else { Config.ApagarRecuperacao(); foreach (var x in docs) x.Esp?.Descartar(); } };
            autoSalvar.Tick += (s, e) => { if (alterado && Esp != null) Config.SalvarRecuperacao(Esp.ParaContainer(), Esp.Arquivo ?? Esp.Origem); };   // contêiner do documento ativo
            autoSalvar.Start();
        }

        // ================= estado e desfazer =================

        void NovoModelo(Modelo m, string caminho)
        {
            m.GarantirCasos();
            resSel = "ativo";
            foreach (Barra b in m.Barras) { b.Dono = m; SecoesExtras.Garantir(b.Secao); }
            Vista.M = m;
            Vista.Sel.Clear();
            Vista.InicioBarra = null;
            Vista.MedidaIni = null;
            Vista.MedidaFim = null;
            arquivo = caminho;
            alterado = false;
            atual = m.ParaXml();
            desfazer.Clear(); refazer.Clear();
            AtualizarTitulo();
            Atualizar();
            Vista.ZoomTudo();
            // de novo depois do layout (a tela inicial pode ter acabado de sumir e mudar o tamanho da vista)
            Dispatcher.BeginInvoke(new Action(() => Vista.ZoomTudo()), DispatcherPriority.Loaded);
        }

        // Depois de cada alteracao: guarda o estado anterior para o desfazer, recalcula e atualiza os paineis.
        void Confirmar(string msg)
        {
            foreach (Barra b in M.Barras) SecoesExtras.Garantir(b.Secao);
            M.ArrumarMembros();   // trecho apagado: barra continua com 1 trecho deixa de existir
            string novo = M.ParaXml();
            if (novo == atual) { Vista.InvalidateVisual(); return; }
            desfazer.Push(atual);
            refazer.Clear();
            atual = novo;
            alterado = true;
            AtualizarTitulo();
            Atualizar();
            if (msg != null) Log(msg);
        }

        void Atualizar()
        {
            AtualizarListasCasos();
            Resolver(false);
            AtualizarArvore();
            AtualizarTabelas();
            Diagnosticar();
            MostrarPropriedades();
            TelaInicial.Visibility = Esp == null && M.Barras.Count == 0 && arquivo == null && !alterado ? Visibility.Visible : Visibility.Collapsed;
            if (TelaInicial.Visibility == Visibility.Visible) MontarTelaInicial();
        }

        void Restaurar(string xml)
        {
            Modelo m = Modelo.DeXml(xml);
            foreach (Barra b in m.Barras) SecoesExtras.Garantir(b.Secao);
            // mantem a selecao pelos ids
            var ids = Vista.Sel.Select(o => o is No n ? "n" + n.Id : "b" + ((Barra)o).Id).ToList();
            Vista.M = m;
            Vista.Sel.Clear();
            foreach (No n in m.Nos) if (ids.Contains("n" + n.Id)) Vista.Sel.Add(n);
            foreach (Barra b in m.Barras) if (ids.Contains("b" + b.Id)) Vista.Sel.Add(b);
            atual = xml;
            alterado = true;
            AtualizarTitulo();
            Atualizar();
            SelecaoMudou();
        }

        internal void Desfazer()
        {
            if (Esp != null) { _ = TrocarEspacial(true); return; }   // modelo espacial: pilha do motor
            if (desfazer.Count == 0) { Log("Nada para desfazer."); return; }
            refazer.Push(atual); Restaurar(desfazer.Pop()); Log("Desfeito.");
        }

        void Refazer()
        {
            if (Esp != null) { _ = TrocarEspacial(false); return; }
            if (refazer.Count == 0) { Log("Nada para refazer."); return; }
            desfazer.Push(atual); Restaurar(refazer.Pop()); Log("Refeito.");
        }

        void AtualizarTitulo()
        {
            string nome = Esp != null ? Esp.Nome : arquivo == null ? "Sem título" : System.IO.Path.GetFileName(arquivo);
            DocModelo.Title = nome + (alterado ? " *" : "");
            Title = "Estructto — " + nome + (alterado ? " *" : "");
            RedesenharAbas();
        }

        // ================= calculo =================

        internal void Resolver(bool avisar)
        {
            if (Esp != null) { AtualizarEspacial3D(false); return; }   // modelo espacial: resultados do motor (DocumentoEspacial.cs)
            Vista.Resolvido = false;
            erroCalculo = "";
            Aviso.Visibility = Visibility.Collapsed;
            if (M.Barras.Count == 0) { StatusMsg.Text = "Modelo vazio: desenhe barras (B) ou use Modelo › Gerar estrutura."; AtualizarLegenda(); Vista.InvalidateVisual(); return; }
            // cada caso de carga e resolvido separadamente; combinacoes e envoltorias saem por superposicao
            resCasos = M.Nos.Any(n => n.TemApoio) ? Analise.ResolverCasos(M) : new ResultadosCasos { Erro = "Sem apoios: selecione um nó e aplique um apoio (aba Modelo)." };
            if (!string.IsNullOrEmpty(resCasos.Erro))
            {
                StatusMsg.Text = "⚠ " + resCasos.Erro;
                erroCalculo = resCasos.Erro;
                if (avisar || Vista.Diag != Diagrama.Nenhum || Vista.MostrarReacoes) { AvisoTxt.Text = resCasos.Erro; Aviso.Visibility = Visibility.Visible; }
                resCasos = null;
                foreach (No n in M.Nos) n.Res = null;
                foreach (Barra b in M.Barras) b.Res = null;
                Vista.Envoltoria = null; Vista.EnvNos = null;
            }
            else
            {
                AplicarResultado();
                Vista.Resolvido = true;
                int falham = M.Barras.Count(b => { var v = Vista.Verificar(b); return v.Aplicavel && !v.Passa; });
                double rx = 0, ry = 0, dmax = 0;
                foreach (No n in M.Nos.Where(x => x.Res != null)) { rx += n.Res[3]; ry += n.Res[4]; dmax = Math.Max(dmax, Math.Sqrt(n.Res[0] * n.Res[0] + n.Res[1] * n.Res[1])); }
                StatusMsg.Text = "Calculado — " + NomeResultado() + ".  " + (Vista.Envoltoria != null ? "Envoltória de " + Pl.N(resSel.StartsWith("py") ? Vista.NomesEnv.Count : CombinacoesDaEnvoltoria().Count,"combinação", "combinações")
                                 : "ΣRx " + Num.Fmt(rx, 2) + " · ΣRy " + Num.Fmt(ry, 2) + " kN · desl. máx. " + Num.Fmt(dmax * 1000, 3) + " mm") +
                                 (M.PesoProprio ? " (com peso próprio)" : "") + (falham > 0 ? " · ⚠ " + Pl.N(falham, "barra não passa", "barras não passam") : "");
                if (avisar) { Log(StatusMsg.Text); Anim.Contar(StatusMsg); }
            }
            AtualizarLegenda();
            if (avisar) Vista.EnquadrarLivre();
            Vista.InvalidateVisual();
            Atualizar3D();
        }

        internal void MostrarDiagrama(Diagrama d, bool calor = false)
        {
            bool trocou = Vista.Diag != d;
            Vista.Diag = d;
            if (trocou) Vista.Crescer();
            Vista.MapaCalor = calor && d == Diagrama.Verificacao;
            if (tgCalor != null) tgCalor.IsChecked = Vista.MapaCalor;
            foreach (var kv in btnDiag) kv.Value.IsChecked = kv.Key == d;
            if (d != Diagrama.Nenhum) Faixa.SelectedTabItem = d == Diagrama.Verificacao ? abaDimens : abaAnalise;
            Resolver(d != Diagrama.Nenhum);
        }

        void Escala(double f)
        {
            if (Vista.Diag == Diagrama.Deformada) Vista.EscalaDef *= f; else Vista.EscalaDiag *= f;
            AtualizarLegenda();
            Vista.InvalidateVisual();
            Atualizar3D();
        }

        // A legenda agora vive no HUD da vista (Hud.cs).
        void AtualizarLegenda()
        {
            if (Hud != null) Hud.Atualizar();
            foreach (var kv in btnDiag) kv.Value.IsChecked = kv.Key == Vista.Diag && !(Vista.MapaCalor && kv.Key == Diagrama.Verificacao);
            if (tgCalor != null) tgCalor.IsChecked = Vista.Diag == Diagrama.Verificacao && Vista.MapaCalor;
        }

        // (ferramentas do desenho 2D removidas: o desenho é a Vista3D — Comandos3D.cs, Vista3D.Grips.cs)

        // ================= arquivos =================

        bool PodeDescartar()
        {
            if (!alterado || Esp == null) return true;
            if (Esp.Arquivo == null && Esp.Json["barras"].AsObject().Count == 0 && Esp.Json["nos"].AsObject().Count == 0) return true;   // em branco
            var r = Caixa.SalvarAlteracoes(this, Esp.Nome);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Salvar(false);
            return true;
        }

        // Todo documento é 3D (docs/MODELO-ESPACIAL.md): Novo = modelo em branco do motor, em planta no térreo.
        void Novo() { _ = NovoEmBranco(); }

        void AbrirDialogo()
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Modelos (*.estructto;*.portico;*.ftl;*.json;*.ed3)|*.estructto;*.portico;*.ftl;*.json;*.ed3|Estructto (*.estructto)|*.estructto|Modelo do motor (*.json)|*.json|Obra do CYPE 3D (*.ed3)|*.ed3|Portico antigo (*.portico)|*.portico|Ftool original (*.ftl)|*.ftl" };
            if (d.ShowDialog(this) == true) foreach (string f in d.FileNames) Abrir(f);
        }

        void ImportarDialogo()
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Ftool original (*.ftl)|*.ftl" };
            if (d.ShowDialog(this) == true) foreach (string f in d.FileNames) Abrir(f);
        }

        // Abre qualquer arquivo como documento 3D. Antigos (.estructto XML, .ftl, .portico) são convertidos (DeModelo2D).
        internal void Abrir(string caminho) { _ = AbrirTarefa(caminho); }

        internal Task AbrirTarefa(string caminho)
        {
            int ja = docs.FindIndex(x => x.Esp?.Arquivo != null && string.Equals(System.IO.Path.GetFullPath(x.Esp.Arquivo), System.IO.Path.GetFullPath(caminho), StringComparison.OrdinalIgnoreCase));
            if (ja >= 0) { TrocarDoc(ja); return Task.CompletedTask; }   // já aberto: só troca para a aba dele
            return AbrirEspacialArquivo(caminho);
        }

        bool Salvar(bool como)
        {
            if (Esp != null) return SalvarEspacial(como);
            Log("Nada para salvar: crie (Ctrl+N) ou abra (Ctrl+O) um modelo.");
            return false;
        }

        void OferecerRecuperacao()
        {
            string txt, orig;
            if (!Config.LerRecuperacao(out txt, out orig)) return;
            var r = Caixa.Mostrar(this, "Recuperar o modelo?", "O Estructto fechou sem salvar da última vez" + (orig != null ? " (" + System.IO.Path.GetFileName(orig) + ")" : "") + ".",
                "Existe uma cópia salva automaticamente. Recuperar abre essa cópia; descartar apaga a cópia.",
                new[] { ("Recuperar", MessageBoxResult.Yes, true), ("Descartar", MessageBoxResult.No, false) }, "↺", JanelaEscura.BAzul);
            if (r == MessageBoxResult.Yes)
            {
                string titulo = (orig != null ? System.IO.Path.GetFileNameWithoutExtension(orig) : "Modelo") + " (recuperado)";
                Func<Task<DocumentoEspacial>> criar = txt.TrimStart().StartsWith("<")
                    ? () => DocumentoEspacial.DeModelo2D(Modelo.DeXml(txt), null, titulo)   // cópia antiga (2D)
                    : () => DocumentoEspacial.DeTexto(txt, titulo);
                _ = AbrirEspacial(criar, "recuperando o modelo").ContinueWith(_ => { if (Esp != null) { alterado = true; AtualizarTitulo(); Log("Modelo recuperado. Salve para não perder."); } }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            Config.ApagarRecuperacao();
        }

        // ================= janelas =================

        // Placa: no resultado da tela; na envoltoria, a combinacao com o pior aproveitamento (parafuso ou chapa).
        internal ResPlaca PlacaPior(No n)
        {
            if (Vista.Envoltoria == null) return PlacaCalc.Calcular(M, n);
            ResPlaca pior = null; double nota = double.MinValue;
            for (int i = 0; i < Vista.NomesEnv.Count; i++)
            {
                foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][i];
                ResPlaca rp = PlacaCalc.Calcular(M, n);
                double v = NotaPlaca(rp, n);
                if (pior == null || v > nota) { pior = rp; nota = v; }
            }
            RestaurarResultado();
            return pior;
        }

        internal double NotaPlaca(ResPlaca rp, No n)
        {
            if (!rp.TemEsforcos) return -1;
            double par = rp.ApFix;
            double ch = n.Placa != null && n.Placa.Esp > 0 ? rp.EspNec / n.Placa.Esp : 0;
            return Math.Max(par, ch);
        }

        // A placa de base é do documento 3D (Legado3D.cs): a janela verifica pelo motor (método NBR) com os esforços ELU do cálculo.
        void AbrirPlaca(No n)
        {
            if (Esp != null) { _ = AbrirPlacaEsp(null); return; }
            Log("Placa de base: abra ou converta o modelo em 3D (a verificação é a do motor, com as combinações do cálculo).");
        }

        void AbrirAssistente(string tipo)
        {
            Bastidor.IsOpen = false;
            var j = new JanelaAssistente(tipo, "W 200 x 26,6", Biblioteca.Materiais.Count > 0 ? Biblioteca.Materiais[0].Nome : "") { Owner = this };
            if (j.ShowDialog() == true && j.Resultado != null)   // o assistente desenha no plano XZ; o documento é 3D (mesma conversão dos arquivos antigos)
                _ = AbrirEspacial(() => DocumentoEspacial.DeModelo2D(j.Resultado, null, "Gerado (" + (tipo ?? "assistente") + ")"), "gerando o modelo")
                    .ContinueWith(_ => { if (Esp != null) { alterado = true; AtualizarTitulo(); Log("Modelo gerado pelo assistente: " + j.Resultado.Barras.Count + " barras. Confira e salve."); } }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void MostrarAjuda() { new JanelaAjuda { Owner = this }.Show(); }

        // ================= teclado e linha de comando =================

        void MostrarSugestao()
        {
            string t = Comando.Text.Trim().ToUpperInvariant();
            if (t.Length == 0 || t.Any(c => char.IsDigit(c) || c == ',' || c == '@' || c == '<' || c == ' ')) { Sugestao.Text = ""; return; }
            string s = Comandos.Where(c => c.StartsWith(t)).OrderBy(c => c.Length).FirstOrDefault();
            Sugestao.Text = s != null ? new string(' ', Comando.Text.Length) + s.Substring(t.Length) + "   (Tab completa)" : "";
        }

        bool Editando()
        {
            return (Keyboard.FocusedElement is TextBox tb && tb != Comando) || Insp.IsKeyboardFocusWithin || Keyboard.FocusedElement is ComboBox
                || (Keyboard.FocusedElement is FrameworkElement fe && fe.TemplatedParent is ComboBox);
        }

        // Digitar com o mouse no desenho manda o texto para a linha de comando (como no AutoCAD). Com uma ferramenta ou pedido
        // de ponto em curso, dígitos, vírgula, ponto e @ vão para a entrada dinâmica da Vista3D (Vista3D.Digitar).
        void TextoDigitado(object s, TextCompositionEventArgs e)
        {
            if (Editando() || Comando.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            if (vista3D == null || !(vista3D.IsKeyboardFocusWithin || vista3D.IsMouseOver || Keyboard.FocusedElement == null || Keyboard.FocusedElement is Window)) return;
            char c = e.Text[0];
            bool numerico = char.IsDigit(c) || c == '.' || c == '-' || c == ',' || c == ';' || c == '@';
            if (vista3D.Desenhando && numeroPendente == null && numerico && vista3D.IsKeyboardFocusWithin) return;   // Vista3D.OnTextInput
            if (char.IsLetterOrDigit(c) || numerico || c == '<' || c == '#' || c == '?')
            {
                Comando.Focus();
                Comando.Text += e.Text; Comando.CaretIndex = Comando.Text.Length;
                e.Handled = true;
            }
        }

        // Diagrama pelo teclado: o mesmo comando liga e, repetido, desliga (igual ao botao da faixa).
        void AlternarDiagrama(Diagrama d) { MostrarDiagrama(Vista.Diag == d ? Diagrama.Nenhum : d); }

        void Tecla(object s, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool editando = Editando();
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            // ferramenta/pedido em curso na Vista3D: Esc em cascata, Enter/Espaço, Tab, Backspace (Vista3D.Tecla)
            if (!editando && !ctrl && Comando.Text == "" && (k == Key.Escape || k == Key.Enter || k == Key.Return || k == Key.Space || k == Key.Tab || k == Key.Back) && Tecla3D(k))
            { e.Handled = true; return; }
            if ((editando || Comando.Text != "") && !ctrl && k != Key.Escape && !(k >= Key.F1 && k <= Key.F12)) return;   // digitando: a caixa trata
            if (TeclaGlobal3D(k, ctrl, shift, editando)) e.Handled = true;   // Comandos3D.cs
        }

        void ComandoTecla(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                string t0 = Comando.Text.Trim().ToUpperInvariant();
                string c = Comandos.Where(x => t0.Length > 0 && x.StartsWith(t0)).OrderBy(x => x.Length).FirstOrDefault();
                if (c != null) { Comando.Text = c; Comando.CaretIndex = c.Length; }
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            string t = Comando.Text.Trim();
            if (e.Key == Key.Space && t.Length > 0 && PareceCoordenada(t)) return;   // espaço dentro de coordenada/expressão
            e.Handled = true;
            Comando.Text = "";
            FocarVista();
            if (t == "") { RepetirUltimo(); return; }
            Log("» " + t);
            ExecutarComando(t);
        }

        void Executar(string t) => ExecutarComando(t);   // faixa, paleta, --cmd (Comandos3D.cs)

        // Botões Mover/Copiar/Espelhar da faixa: no 3D, como os comandos M, CO, MI.
        void Transformar(string modo, string[] w)
        {
            if (modo == "mover") ExecutarComando("MOVER");
            else if (modo == "copiar") ExecutarComando("COPIAR");
            else ExecutarComando("ESPELHAR");
        }

        void MostrarHistorico()
        {
            var j = new JanelaEscura { Title = "Histórico de comandos (F2)", Width = 760, Height = 480, Owner = this, ShowInTaskbar = false };
            var lb = new ListBox { Background = JanelaEscura.BPainel, Foreground = JanelaEscura.BTexto, BorderThickness = new Thickness(0), FontFamily = new System.Windows.Media.FontFamily("Consolas"), ToolTip = "Histórico de todos os comandos executados e mensagens exibidas nesta sessão." };
            foreach (string h in historico) lb.Items.Add(h);
            if (lb.Items.Count > 0) lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
            j.Content = lb;
            j.Show();
        }

        // Dica do campo vazio: diz o que o comando ativo espera (como o prompt do AutoCAD)
        void AtualizarDica()
        {
            string d;
            if (Vista.Ferr == Ferramenta.Barra)
                d = Vista.InicioBarra == null
                    ? "Barra — 1º ponto: clique ou digite x,y (ex.: 0,0)"
                    : "Próximo ponto: x,y  ·  @dx,dy  ·  @L<ângulo  ·  só o comprimento  ·  Enter termina";
            else if (Vista.Ferr == Ferramenta.Medir)
                d = Vista.MedidaIni == null || Vista.MedidaFim != null
                    ? "Medir — 1º ponto: clique num nó/ponto ou digite x,y"
                    : "Medir — 2º ponto: clique num nó/ponto ou digite x,y  ·  @dx,dy  ·  @L<ângulo";
            else if (Vista.Ferr == Ferramenta.No) d = "Nó — clique ou digite x,y";
            else d = "Digite um comando ou um ponto x,y (AJUDA lista os comandos)";
            if ((string)DicaComando.Text != d) DicaComando.Text = d;
        }

        void Log(string s, bool toast = true)
        {
            if (historico.Count > 0 && historico[historico.Count - 1] == s) return;   // nao repete a mesma linha
            historico.Add(s);
            if (historico.Count > 300) historico.RemoveAt(0);
            Historico.Text = string.Join("\n", historico.Skip(Math.Max(0, historico.Count - 8)));
            if (toast) MostrarToast(s);
        }

        // Mensagem discreta no canto da vista: some sozinha em 4 s (o historico completo fica na linha de comando e em F2).
        readonly System.Windows.Threading.DispatcherTimer tmToast = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        bool toastLigado;

        internal void MostrarToast(string s, bool forcar = false)
        {
            if (string.IsNullOrWhiteSpace(s) || (Capturas.Ativo && !forcar)) return;
            if (!toastLigado) { toastLigado = true; tmToast.Tick += (a, b) => { tmToast.Stop(); EsconderToast(); }; }
            ToastTxt.Text = s;
            Toast.BeginAnimation(OpacityProperty, null);
            Toast.Opacity = 1; Toast.Visibility = Visibility.Visible;
            tmToast.Stop(); tmToast.Start();
        }

        void EsconderToast()
        {
            var an = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            an.Completed += (a, b) => { if (!tmToast.IsEnabled) Toast.Visibility = Visibility.Collapsed; };
            Toast.BeginAnimation(OpacityProperty, an);
        }
    }
}
