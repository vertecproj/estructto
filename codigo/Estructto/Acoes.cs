// Comandos do usuario: ferramentas, edicao, desfazer/refazer, arquivos, linha de comando e teclado.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
            Vista.Clique += CliqueNaVista;
            Vista.SelecaoMudou += SelecaoMudou;
            Vista.NoArrastado += (n, de, para) => Confirmar("Nó " + n.Id + " movido para (" + Num.Fmt(para.X, 3) + "; " + Num.Fmt(para.Y, 3) + ").");
            Vista.FimComando += () =>
            {
                if (Vista.InicioBarra != null) { Vista.InicioBarra = null; Log("Barra terminada."); }
                else if (Vista.MedidaIni != null && Vista.MedidaFim == null) { Vista.MedidaIni = null; Vista.Etiqueta = "MEDIR — clique o 1º ponto (ou digite x,y).   Botão direito ou Esc encerra."; Log("Medida cancelada."); }
                else AcionarFerramenta(Ferramenta.Selecionar);
                Vista.InvalidateVisual();
            };
            Vista.MenuContexto += MenuContexto;
            Vista.DuploClique += o =>
            {
                if (o is Barra b && Vista.Resolvido) AbrirVerificacao(b);
                else if (o is No n && n.Placa != null) AbrirPlaca(n);
                else { PainelProp.Show(); PainelProp.IsActive = true; }
            };
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
            TabVerif.MouseDoubleClick += (s, e) => { var b = Vista.Sel.OfType<Barra>().FirstOrDefault(); if (b != null) AbrirVerificacao(b); };
            ListaAvisos.SelectionChanged += (s, e) => { if (ListaAvisos.SelectedItem is ListBoxItem it && it.Tag is IEnumerable<object> objs) { if (objs.FirstOrDefault() is RefMotor r) SelecionarEsp(r); else Vista.Selecionar(objs); } };
            SeloAvisos.Click += (s, e) => { PainelAvisos.Show(); PainelAvisos.IsSelected = true; PainelAvisos.IsActive = true; };
            Comando.KeyDown += ComandoTecla;
            Comando.TextChanged += (s, e) => MostrarSugestao();
            var tmDica = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            tmDica.Tick += (s, e) => AtualizarDica(); tmDica.Start();
            PreviewKeyDown += Tecla;
            PreviewTextInput += TextoDigitado;
            Closing += (s, e) => { if (!PodeFecharTudo()) e.Cancel = true; else { Config.ApagarRecuperacao(); foreach (var x in docs) x.Esp?.Descartar(); } };
            autoSalvar.Tick += (s, e) => { if (alterado) Config.SalvarRecuperacao(atual, arquivo); };   // ultimo estado confirmado (nao a placa em edicao)
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
            stGrade.IsChecked = m.MostrarGrade; stAtrair.IsChecked = m.AtrairGrade;
            tgPeso.IsChecked = m.PesoProprio; tgTimo.IsChecked = m.Timoshenko;
            cGrade.Text = Num.Fmt(m.Grade, 3).TrimEnd('0').TrimEnd(',');
            cSeg.Text = Num.Fmt(m.Seguranca, 2); cFlecha.Text = Num.Fmt(m.LimiteFlecha, 0);
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
            tgPeso.IsChecked = m.PesoProprio; tgTimo.IsChecked = m.Timoshenko;
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
            if (d != Diagrama.Nenhum && Vista.Ferr != Ferramenta.Selecionar) UsarFerramenta(Ferramenta.Selecionar);
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

        void MudarCriterio()
        {
            double s, f;
            if (!Num.Ler(cSeg.Text, out s) || s <= 0) s = M.Seguranca;
            if (!Num.Ler(cFlecha.Text, out f) || f <= 0) f = M.LimiteFlecha;
            cSeg.Text = Num.Fmt(s, 2); cFlecha.Text = Num.Fmt(f, 0);
            if (s == M.Seguranca && f == M.LimiteFlecha) return;
            M.Seguranca = s; M.LimiteFlecha = f;
            Confirmar("Critérios: segurança " + Num.Fmt(s, 2) + ", flecha L/" + Num.Fmt(f, 0) + ".");
        }

        // ================= ferramentas =================

        // Regra Autodesk/CYPE: com itens selecionados, o comando de atributo aplica neles na hora;
        // sem selecao, entra no modo de clicar nos itens (botao direito ou Esc encerra).
        void AcionarFerramenta(Ferramenta f)
        {
            var nos = Vista.Sel.OfType<No>().ToList();
            var barras = Vista.Sel.OfType<Barra>().ToList();
            bool deNo = f >= Ferramenta.ApoioEngaste && f <= Ferramenta.ApoioLivre || f == Ferramenta.CargaNo || f == Ferramenta.Placa;
            if (deNo && nos.Count > 0)
            {
                if (f == Ferramenta.CargaNo) AplicarCargaNo(nos);
                else if (f == Ferramenta.Placa) AbrirPlaca(nos[0]);
                else AplicarApoio(f, nos);
                SincronizarFerramenta();
                return;
            }
            if (f == Ferramenta.CargaBarra && barras.Count > 0) { AplicarCargaBarra(barras); SincronizarFerramenta(); return; }
            UsarFerramenta(Vista.Ferr == f && f != Ferramenta.Selecionar ? Ferramenta.Selecionar : f);
        }

        internal void UsarFerramenta(Ferramenta f)
        {
            Vista.Ferr = f;
            Vista.InicioBarra = null;
            Vista.MedidaIni = null;
            Vista.MedidaFim = null;
            Vista.DinLimpar();
            Vista.LimparRastreio();   // pontos de rastreamento valem por comando, como no AutoCAD
            if (f != Ferramenta.Selecionar && Vista.Sel.Count > 0) { Vista.Sel.Clear(); SelecaoMudou(); }
            SincronizarFerramenta();
            string dica = f == Ferramenta.Barra ? "BARRA — clique o 1º ponto (ou digite x,y)."
                : f == Ferramenta.No ? "NÓ — clique onde inserir."
                : f == Ferramenta.Medir ? "MEDIR — clique o 1º ponto (ou digite x,y)."
                : f == Ferramenta.CargaNo ? "CARGA NO NÓ — clique nos nós (Fx/Fy/Mz da aba Ações)."
                : f == Ferramenta.CargaBarra ? "CARGA NA BARRA — clique nas barras."
                : f == Ferramenta.Rotula ? "RÓTULA — clique perto da ponta da barra."
                : f == Ferramenta.Placa ? "PLACA DE BASE — clique no nó do pé do pilar."
                : f >= Ferramenta.ApoioEngaste && f <= Ferramenta.ApoioLivre ? "APOIO — clique nos nós."
                : null;
            Vista.Etiqueta = dica == null ? null : dica + "   Botão direito ou Esc encerra.";
            if (dica != null) Log(dica);
            Vista.Cursor = f == Ferramenta.Selecionar ? Cursors.Arrow : Cursors.Cross;
            Vista.InvalidateVisual();
        }

        void SincronizarFerramenta()
        {
            foreach (var kv in btnFerr) foreach (var b in kv.Value) b.IsChecked = kv.Key == Vista.Ferr;
        }

        double? Ler(Fluent.TextBox t, string nome)
        {
            double v;
            if (Num.Ler(t.Text, out v)) return v;
            Log("Valor inválido em " + nome + ": \"" + t.Text + "\".");
            return null;
        }

        void AplicarApoio(Ferramenta f, IEnumerable<No> nos)
        {
            var lista = nos.ToList();
            if (lista.Count == 0) { Log("Selecione um ou mais nós."); return; }
            foreach (No n in lista)
            {
                n.ApoioX = f == Ferramenta.ApoioEngaste || f == Ferramenta.ApoioRotula || f == Ferramenta.ApoioRoloH;
                n.ApoioY = f == Ferramenta.ApoioEngaste || f == Ferramenta.ApoioRotula || f == Ferramenta.ApoioRoloV;
                n.ApoioGiro = f == Ferramenta.ApoioEngaste;
                if (f == Ferramenta.ApoioLivre) { n.MolaX = n.MolaY = n.MolaGiro = 0; n.RecalqueX = n.RecalqueY = 0; n.Placa = null; }
                if (!n.ApoioGiro && n.Placa != null && f != Ferramenta.ApoioEngaste) { }
            }
            Confirmar((f == Ferramenta.ApoioLivre ? "Apoio removido de " : "Apoio aplicado em ") + Pl.N(lista.Count, "nó", "nós") + ".");
        }

        void AplicarCargaNo(IEnumerable<No> nos)
        {
            var lista = nos.ToList();
            if (lista.Count == 0) { Log("Selecione um ou mais nós."); return; }
            double? fx = Ler(cFx, "Fx"), fy = Ler(cFy, "Fy"), mz = Ler(cMz, "Mz");
            if (fx == null || fy == null || mz == null) return;
            foreach (No n in lista) { n.Fx = fx.Value; n.Fy = fy.Value; n.Mz = mz.Value; }
            Confirmar("Carga aplicada em " + Pl.N(lista.Count, "nó", "nós") + ".");
        }

        void AplicarCargaBarra(IEnumerable<Barra> barras)
        {
            var lista = barras.ToList();
            double? qx = Ler(cQx, "qx"), qy = Ler(cQy, "qy"), qi = Ler(cQi, "q⊥ início"), qf = Ler(cQf, "q⊥ fim");
            if (qx == null || qy == null || qi == null || qf == null) return;
            foreach (Barra b in lista) { b.QGlobalX = qx.Value; b.QGlobalY = qy.Value; b.QLocalIni = qi.Value; b.QLocalFim = qf.Value; }
            Confirmar("Carga aplicada em " + Pl.N(lista.Count, "barra", "barras") + ".");
        }

        void RotularSel(bool ini, bool fim)
        {
            var bs = Vista.Sel.OfType<Barra>().ToList();
            if (bs.Count == 0) { Log("Selecione barras."); return; }
            bool novo = !(ini ? bs[0].RotulaInicio : bs[0].RotulaFim);
            foreach (Barra b in bs) { if (ini) b.RotulaInicio = novo; if (fim) b.RotulaFim = novo; }
            Confirmar("Rótulas alteradas em " + Pl.N(bs.Count, "barra", "barras") + ".");
        }

        void CliqueNaVista(double x, double y)
        {
            switch (Vista.Ferr)
            {
                case Ferramenta.Barra: PontoBarra(x, y); break;
                case Ferramenta.Medir: PontoMedida(x, y); break;
                case Ferramenta.No:
                    M.ObterNo(x, y, Vista.TolMundo);
                    Confirmar("Nó inserido em (" + Num.Fmt(x, 3) + "; " + Num.Fmt(y, 3) + ").");
                    break;
                case Ferramenta.ApoioEngaste: case Ferramenta.ApoioRotula: case Ferramenta.ApoioRoloV: case Ferramenta.ApoioRoloH: case Ferramenta.ApoioLivre:
                {
                    No n = Vista.NoEm(x, y);
                    if (n == null) { Log("Clique sobre um nó."); return; }
                    AplicarApoio(Vista.Ferr, new[] { n });
                    break;
                }
                case Ferramenta.Rotula:
                {
                    Barra b = Vista.BarraEm(x, y);
                    if (b == null) { Log("Clique sobre uma barra, perto da ponta."); return; }
                    No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                    if (Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) <= Math.Pow(c.X - x, 2) + Math.Pow(c.Y - y, 2)) b.RotulaInicio = !b.RotulaInicio; else b.RotulaFim = !b.RotulaFim;
                    Confirmar("Rótula alterada na barra " + b.Id + ".");
                    break;
                }
                case Ferramenta.CargaNo:
                {
                    No n = Vista.NoEm(x, y);
                    if (n == null) { Log("Clique sobre um nó."); return; }
                    AplicarCargaNo(new[] { n });
                    break;
                }
                case Ferramenta.CargaBarra:
                {
                    Barra b = Vista.BarraEm(x, y);
                    if (b == null) { Log("Clique sobre uma barra."); return; }
                    AplicarCargaBarra(new[] { b });
                    break;
                }
                case Ferramenta.Placa:
                {
                    No n = Vista.NoEm(x, y);
                    if (n == null) { Log("Clique no nó do pé do pilar."); return; }
                    AbrirPlaca(n);
                    break;
                }
            }
        }

        void PontoBarra(double x, double y)
        {
            Vista.DinLimpar();
            if (Vista.InicioBarra == null)
            {
                Vista.InicioBarra = new Point(x, y);
                Vista.Etiqueta = "BARRA — próximo ponto: clique, ou digite o comprimento (na direção do mouse), x,y, @dx,dy, @L<ângulo.   Botão direito/Esc termina.";
                Vista.InvalidateVisual();
                return;
            }
            Point a = Vista.InicioBarra.Value;
            if (Math.Abs(a.X - x) < 1e-9 && Math.Abs(a.Y - y) < 1e-9) return;
            if (!SecaoValida(cbSecao.Text)) { Log("Seção \"" + cbSecao.Text + "\" não reconhecida; corrija na aba Modelo."); return; }
            int antes = M.Barras.Count;
            No n1 = M.ObterNo(a.X, a.Y, 1e-6), n2 = M.ObterNo(x, y, 1e-6);
            M.AddBarra(n1, n2, 1e-6);
            for (int i = antes; i < M.Barras.Count; i++)
            {
                M.Barras[i].Secao = cbSecao.Text;
                if (Biblioteca.AcharMaterial(cbMaterial.Text) != null) M.Barras[i].Material = cbMaterial.Text;
            }
            Vista.InicioBarra = new Point(x, y);
            Confirmar("Barra até (" + Num.Fmt(x, 3) + "; " + Num.Fmt(y, 3) + ").");
            Vista.Garantir(new Point(x, y));
        }

        internal void PontoMedida(double x, double y)
        {
            Vista.DinLimpar();
            if (Vista.MedidaIni == null || Vista.MedidaFim != null)
            {
                Vista.MedidaIni = new Point(x, y);
                Vista.MedidaFim = null;
                Vista.Etiqueta = "MEDIR — clique o 2º ponto (ou digite x,y, @dx,dy, @L<ângulo).   Botão direito/Esc cancela.";
                Vista.InvalidateVisual();
                return;
            }
            Point a = Vista.MedidaIni.Value, b = new Point(x, y);
            if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) return;
            Vista.MedidaFim = b;
            string res = "Medida (" + Num.Fmt(a.X, 3) + "; " + Num.Fmt(a.Y, 3) + ") → (" + Num.Fmt(b.X, 3) + "; " + Num.Fmt(b.Y, 3) + "):  " + Vista.TextoMedida(a, b);
            StatusMsg.Text = res;
            Log(res);
            Vista.Etiqueta = "MEDIR — clique para iniciar nova medida.   Botão direito ou Esc encerra.";
            Vista.InvalidateVisual();
        }

        static bool SecaoValida(string s) { SecoesExtras.Garantir(s); return Biblioteca.AcharSecao(s) != null; }

        void AplicarSecao(bool todas)
        {
            if (!SecaoValida(cbSecao.Text)) { Log("Seção não reconhecida: " + cbSecao.Text + "."); return; }
            if (Biblioteca.AcharMaterial(cbMaterial.Text) == null) { Log("Material não reconhecido: " + cbMaterial.Text + "."); return; }
            var alvo = todas ? M.Barras.ToList() : Vista.Sel.OfType<Barra>().ToList();
            if (alvo.Count == 0) { Log("Nenhuma barra selecionada: " + cbSecao.Text + " vale para as próximas barras desenhadas."); return; }
            foreach (Barra b in alvo) { b.Secao = cbSecao.Text; b.Material = cbMaterial.Text; }
            SincronizarMembros(alvo);
            Confirmar("Seção " + cbSecao.Text + " aplicada em " + Pl.N(alvo.Count, "barra", "barras") + ".");
        }

        void Apagar()
        {
            if (Vista.Sel.Count == 0) { Log("Selecione nós ou barras para apagar."); return; }
            int qt = Vista.Sel.Count;
            foreach (object o in Vista.Sel.ToList())
            {
                if (o is Barra b && M.Barras.Contains(b)) M.RemoverBarra(b);
                else if (o is No n && M.Nos.Contains(n)) M.RemoverNo(n);
            }
            Vista.Sel.Clear();
            Confirmar(Pl.N(qt, "item apagado", "itens apagados") + ". Ctrl+Z desfaz.");
            SelecaoMudou();
        }

        void DividirSelecao()
        {
            if (Esp != null) { AbrirEdicaoEsp("dividir"); return; }
            var bs = Vista.Sel.OfType<Barra>().ToList();
            if (bs.Count == 0) { Log("Selecione as barras a dividir."); return; }
            string r = Pergunta.Mostrar(this, "Dividir barra", "Dividir cada barra selecionada em quantas partes iguais? (2 a 100)", "2");
            int n;
            if (r == null || !int.TryParse(r, out n) || n < 2 || n > 100) { if (r != null) Log("Número de partes inválido."); return; }
            foreach (Barra b in bs)
            {
                No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                double ax = a.X, ay = a.Y, cx = c.X, cy = c.Y;
                for (int i = 1; i < n; i++) M.ObterNo(ax + (cx - ax) * i / n, ay + (cy - ay) * i / n, 1e-6);
            }
            Vista.Sel.Clear();
            Confirmar(Pl.N(bs.Count, "barra dividida", "barras divididas") + " em " + n + ".");
            SelecaoMudou();
        }

        void LimparCargas()
        {
            var nos = Vista.Sel.OfType<No>().ToList(); var bs = Vista.Sel.OfType<Barra>().ToList();
            bool tudo = Vista.Sel.Count == 0;
            foreach (No n in tudo ? M.Nos : nos) { n.Fx = n.Fy = n.Mz = 0; }
            foreach (Barra b in tudo ? M.Barras : bs) { b.QGlobalX = b.QGlobalY = b.QLocalIni = b.QLocalFim = 0; }
            Confirmar(tudo ? "Todas as cargas removidas." : "Cargas removidas da seleção.");
        }

        void MudarGrade(string txt)
        {
            double g;
            if (!Num.Ler(txt, out g) || g <= 0) { cGrade.Text = Num.Fmt(M.Grade, 3); return; }
            if (Math.Abs(g - M.Grade) < 1e-12) return;
            M.Grade = g;
            Confirmar("Grade = " + Num.Fmt(g, 3) + " m.");
        }

        internal ContextMenu CriarMenuContexto(object o)
        {
            var cm = new ContextMenu();
            void Add(string t, Action a, string atalho = null, string dica = null, string ic = null)
            {
                var mi = new MenuItem { Header = t, InputGestureText = atalho, ToolTip = dica };
                if (ic != null) mi.Icon = Icone(ic, 16);
                mi.Click += (s, e) => a();
                cm.Items.Add(mi);
            }
            if (o is No)
            {
                var ap = new MenuItem { Header = "Apoio", Icon = Icone("engaste", 16), ToolTip = "Define o tipo de vinculação externa dos nós selecionados." };
                foreach (var par in new[]
                {
                    ("Engaste", Ferramenta.ApoioEngaste, "Trava deslocamentos X, Y e giro nos nós selecionados.", "engaste"),
                    ("Fixo (2º gênero)", Ferramenta.ApoioRotula, "Trava deslocamentos X e Y, deixando o giro livre.", "rotula"),
                    ("Móvel (1º gênero)", Ferramenta.ApoioRoloV, "Trava apenas o deslocamento vertical Y (rolete).", "rolo"),
                    ("Móvel horizontal", Ferramenta.ApoioRoloH, "Trava apenas o deslocamento horizontal X.", "roloh"),
                    ("Sem apoio", Ferramenta.ApoioLivre, "Remove apoios, molas, recalques e placa de base do nó.", "livre")
                })
                {
                    var mi = new MenuItem { Header = par.Item1, ToolTip = par.Item3, Icon = Icone(par.Item4, 16) };
                    var f = par.Item2;
                    mi.Click += (s, e) => AplicarApoio(f, Vista.Sel.OfType<No>());
                    ap.Items.Add(mi);
                }
                cm.Items.Add(ap);
                Add("Aplicar carga da aba Ações", () => AplicarCargaNo(Vista.Sel.OfType<No>()), null, "Aplica Fx, Fy e Mz preenchidos na aba Ações aos nós selecionados.", "cargano");
                Add("Placa de base...", () => AbrirPlaca(Vista.Sel.OfType<No>().FirstOrDefault()), null, "Abre a janela de dimensionamento e verificação da placa de base deste nó.", "placa");
                cm.Items.Add(new Separator());
                Add("Apagar", Apagar, "Del", "Remove os nós selecionados e as barras conectadas a eles.", "apagar");
            }
            else if (o is Barra)
            {
                Add("Verificar barra...", () => AbrirVerificacao(Vista.Sel.OfType<Barra>().FirstOrDefault()), null, "Abre a verificação detalhada da barra e compara opções de perfis da mesma família.", "verif");
                Add("Aplicar seção da aba Modelo", () => AplicarSecao(false), null, "Atribui a seção e o material escolhidos na aba Modelo às barras selecionadas.", "aplicar");
                Add("Aplicar carga distribuída da aba Ações", () => AplicarCargaBarra(Vista.Sel.OfType<Barra>()), null, "Aplica as cargas distribuídas qx, qy e q⊥ da aba Ações às barras selecionadas.", "cargadist");
                var ro = new MenuItem { Header = "Rótula", Icon = Icone("rotulab", 16), ToolTip = "Adiciona ou remove articulações (momento nulo) nas pontas das barras selecionadas." };
                foreach (var par in new[]
                {
                    ("No início", true, false, "Liga ou desliga a rótula na extremidade inicial da barra."),
                    ("No fim", false, true, "Liga ou desliga a rótula na extremidade final da barra."),
                    ("Nas duas pontas", true, true, "Liga ou desliga rótulas nas duas extremidades (barra de treliça).")
                })
                {
                    var mi = new MenuItem { Header = par.Item1, ToolTip = par.Item4 };
                    bool i1 = par.Item2, i2 = par.Item3;
                    mi.Click += (s, e) => RotularSel(i1, i2);
                    ro.Items.Add(mi);
                }
                cm.Items.Add(ro);
                Add("Tirar trecho da barra contínua", TirarTrecho, "TIRAR", "Tira os trechos selecionados da barra contínua (o resto continua unido).", "tirar");
                Add("Selecionar a barra contínua", SelecionarMembro, "SELMEMBRO", "Seleciona todos os trechos da barra contínua do trecho selecionado.", "selmembro");
                Add("Dividir...", DividirSelecao, null, "Divide as barras selecionadas em partes iguais criando nós intermediários.", "dividir");
                cm.Items.Add(new Separator());
                Add("Apagar", Apagar, "Del", "Remove as barras selecionadas do modelo.", "apagar");
            }
            else
            {
                Add("Desenhar barra", () => UsarFerramenta(Ferramenta.Barra), "B", "Ativa a ferramenta para desenhar novas barras clicando nos pontos.", "barra");
                Add("Medir distância", () => UsarFerramenta(Ferramenta.Medir), "Ctrl+M", "Mede a distância total, ΔX, ΔY e o ângulo entre dois pontos da vista.", "medir");
                Add("Enquadrar", () => Vista.ZoomTudo(), "F4", "Ajusta o zoom para mostrar o modelo inteiro na tela.", "zoomtudo");
                Add("Selecionar todas as barras", () => Vista.Selecionar(M.Barras.Cast<object>()), "Ctrl+A", "Seleciona todas as barras do modelo de uma só vez.", "sel");
                Add("Desfazer", Desfazer, "Ctrl+Z", "Desfaz a última alteração realizada no modelo.", "desfazer");
            }
            cm.Items.Add(new Separator());
            Add("Propriedades", () => { PainelProp.Show(); PainelProp.IsActive = true; }, null, "Mostra e ativa o painel Propriedades para editar os dados do item selecionado.", "painel");
            return cm;
        }

        void MenuContexto(object o, Point p)
        {
            var cm = CriarMenuContexto(o);
            cm.PlacementTarget = Vista; cm.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
            cm.HorizontalOffset = p.X; cm.VerticalOffset = p.Y; cm.IsOpen = true;
        }

        // ================= arquivos =================

        bool PodeDescartar()
        {
            if (!alterado || M.Barras.Count == 0 && arquivo == null) return true;
            var r = Caixa.SalvarAlteracoes(this, arquivo == null ? "Sem título" : System.IO.Path.GetFileName(arquivo));
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Salvar(false);
            return true;
        }

        void Novo()
        {
            GuardarDoc();
            docs.Add(new Documento());
            docAtivo = docs.Count - 1;
            NovoModelo(new Modelo(), null);
            Log("Novo modelo (aba nova).");
        }

        void AbrirDialogo()
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Modelos (*.estructto;*.portico;*.ftl;*.json;*.ed3)|*.estructto;*.portico;*.ftl;*.json;*.ed3|Modelo espacial do motor (*.json)|*.json|Obra do CYPE 3D (*.ed3)|*.ed3|Estructto (*.estructto)|*.estructto|Portico antigo (*.portico)|*.portico|Ftool original (*.ftl)|*.ftl" };
            if (d.ShowDialog(this) == true) foreach (string f in d.FileNames) Abrir(f);
        }

        void ImportarDialogo()
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Ftool original (*.ftl)|*.ftl" };
            if (d.ShowDialog(this) == true) foreach (string f in d.FileNames) Abrir(f);
        }

        internal void Abrir(string caminho)
        {
            if (EhEspacial(caminho) && !docs.Any(x => x.Arquivo != null && string.Equals(System.IO.Path.GetFullPath(x.Arquivo), System.IO.Path.GetFullPath(caminho), StringComparison.OrdinalIgnoreCase)))
            { _ = AbrirEspacialArquivo(caminho); return; }   // .json do motor / .ed3 do CYPE: DocumentoEspacial.cs
            // ja aberto: so troca para a aba dele
            GuardarDoc();
            int ja = docs.FindIndex(x => x.Arquivo != null && string.Equals(System.IO.Path.GetFullPath(x.Arquivo), System.IO.Path.GetFullPath(caminho), StringComparison.OrdinalIgnoreCase));
            if (ja >= 0) { TrocarDoc(ja); return; }
            PrepararDocNovo();
            try
            {
                if (caminho.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
                {
                    string aviso;
                    Modelo m = Ftl.Importar(caminho, out aviso);
                    NovoModelo(m, null);
                    alterado = true; AtualizarTitulo(); Atualizar();
                    DocModelo.Title = System.IO.Path.GetFileName(caminho) + " (importado) *";
                    foreach (string linha in aviso.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)) Log(linha);
                }
                else
                {
                    NovoModelo(Modelo.DeXml(File.ReadAllText(caminho)), caminho);
                    Config.AdicionarRecente(caminho);
                    Log("Aberto: " + caminho);
                }
            }
            catch (Exception ex)
            {
                Caixa.Erro(this, "Não foi possível abrir o arquivo", ex.Message);
            }
        }

        bool Salvar(bool como)
        {
            if (Esp != null) return SalvarEspacial(como);
            string c = arquivo;
            if (como || c == null)
            {
                var d = new SaveFileDialog { Filter = "Estructto (*.estructto)|*.estructto", FileName = c == null ? "modelo.estructto" : System.IO.Path.GetFileNameWithoutExtension(c) + ".estructto" };
                if (d.ShowDialog(this) != true) return false;
                c = d.FileName;
            }
            try
            {
                File.WriteAllText(c, M.ParaXml());
                arquivo = c; alterado = false;
                Config.AdicionarRecente(c);
                Config.ApagarRecuperacao();
                AtualizarTitulo(); AtualizarArvore();
                Log("Salvo: " + c);
                return true;
            }
            catch (Exception ex)
            {
                Caixa.Erro(this, "Não foi possível salvar", ex.Message);
                return false;
            }
        }

        void OferecerRecuperacao()
        {
            string xml, orig;
            if (!Config.LerRecuperacao(out xml, out orig)) return;
            var r = Caixa.Mostrar(this, "Recuperar o modelo?", "O Estructto fechou sem salvar da última vez" + (orig != null ? " (" + System.IO.Path.GetFileName(orig) + ")" : "") + ".",
                "Existe uma cópia salva automaticamente. Recuperar abre essa cópia; descartar apaga a cópia.",
                new[] { ("Recuperar", MessageBoxResult.Yes, true), ("Descartar", MessageBoxResult.No, false) }, "↺", JanelaEscura.BAzul);
            if (r == MessageBoxResult.Yes)
            {
                try { NovoModelo(Modelo.DeXml(xml), orig); alterado = true; AtualizarTitulo(); Atualizar(); Log("Modelo recuperado. Salve para não perder."); }
                catch { Log("Não foi possível recuperar o modelo."); }
            }
            Config.ApagarRecuperacao();
        }

        void ExportarCsv()
        {
            if (!Vista.Resolvido) { Log("Calcule a estrutura antes de exportar (há um erro ou falta apoio)."); return; }
            var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = (arquivo == null ? "resultados" : System.IO.Path.GetFileNameWithoutExtension(arquivo)) + ".csv" };
            if (d.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("NÓS");
            sb.AppendLine("Nó;X (m);Y (m);Desl. X (mm);Desl. Y (mm);Giro (rad);Reação X (kN);Reação Y (kN);Reação M (kN.m)");
            foreach (No n in M.Nos)
            {
                double[] r = n.Res ?? new double[6];
                sb.AppendLine(string.Join(";", n.Id, Num.Fmt(n.X, 3), Num.Fmt(n.Y, 3), Num.Fmt(r[0] * 1000, 3), Num.Fmt(r[1] * 1000, 3), r[2].ToString("0.000000"), Num.Fmt(r[3], 2), Num.Fmt(r[4], 2), Num.Fmt(r[5], 2)));
            }
            sb.AppendLine();
            sb.AppendLine("BARRAS");
            sb.AppendLine("Barra;Nó inicial;Nó final;Perfil;Material;L (m);N mín (kN);N máx (kN);Q mín (kN);Q máx (kN);M mín (kN.m);M máx (kN.m);Flecha (mm);Aproveitamento (%);Situação");
            foreach (Barra b in M.Barras)
            {
                if (b.Res == null) continue;
                double n0, n1, q0, q1, m0, m1;
                Extremos(b, 0, out n0, out n1); Extremos(b, 1, out q0, out q1); Extremos(b, 2, out m0, out m1);
                Verif v = Vista.Verificar(b);
                sb.AppendLine(string.Join(";", b.Id, b.N1, b.N2, b.Secao, b.Material, Num.Fmt(b.Res.L, 3), Num.Fmt(n0, 2), Num.Fmt(n1, 2), Num.Fmt(q0, 2), Num.Fmt(q1, 2), Num.Fmt(m0, 2), Num.Fmt(m1, 2),
                    Num.Fmt(Flecha(b) * 1000, 2), v.Aplicavel ? Num.Fmt(v.Ap * 100, 0) : "", v.Aplicavel ? (v.Passa ? "passa" : "não passa (" + v.Critico + ")") : v.Motivo));
            }
            try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); Log("Exportado: " + d.FileName); }
            catch (Exception ex) { Caixa.Erro(this, "Não foi possível exportar", ex.Message); }
        }

        // ================= janelas =================

        void AbrirVerificacao(Barra b)
        {
            if (b == null) b = Vista.Sel.OfType<Barra>().FirstOrDefault();
            if (b == null) { Log("Selecione uma barra (clique nela) e repita."); return; }
            if (!Vista.Resolvido || b.Res == null) { Log("Sem resultados: corrija os avisos e calcule (F5)."); return; }
            // envoltoria: a janela mostra a combinacao que mais solicita esta barra
            Secao sec = Biblioteca.AcharSecao(b.Secao);
            Barra bb = b;
            string crit = FixarCombinacaoCritica(i => { bb.Res = Vista.Envoltoria[bb][i]; var v = Verificador.Barra(M, bb, sec); return v.Aplicavel ? v.Ap : -1; });
            var j = new JanelaVerificacao(M, b) { Owner = this };
            if (crit != null) j.Title += " — combinação crítica: " + crit;
            bool trocou = j.ShowDialog() == true && j.PerfilEscolhido != null;
            if (j.MudouSeguranca) { cSeg.Text = Num.Fmt(M.Seguranca, 2); Confirmar("Coeficiente de segurança: " + Num.Fmt(M.Seguranca, 2) + "."); }
            if (crit != null) RestaurarResultado();
            if (trocou)
            {
                b.Secao = j.PerfilEscolhido;
                SincronizarMembros(new[] { b });   // barra continua: todos os trechos trocam juntos
                Confirmar((b.Membro != 0 ? "Barra contínua B" + b.Membro + " (" + M.Trechos(b.Membro).Count + " trechos)" : "Barra " + b.Id) + " trocada para " + j.PerfilEscolhido + ".");
            }
        }

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

        void AbrirPlaca(No n)
        {
            if (n == null) n = Vista.Sel.OfType<No>().FirstOrDefault() ?? M.Nos.FirstOrDefault(x => x.Placa != null);
            if (n == null) { Log("Selecione o nó do pé do pilar e repita."); return; }
            string erro;
            Secao s; double ri, ro;
            if (PlacaCalc.Pilar(M, n, out s, out ri, out ro, out erro) == null) { Log(erro); return; }
            var orig = n.Placa;
            No nn = n;
            string crit = n.Placa != null ? FixarCombinacaoCritica(i => { foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][i]; return NotaPlaca(PlacaCalc.Calcular(M, nn), nn); }) : null;
            if (crit == null && Vista.Envoltoria != null)
                crit = FixarCombinacaoCritica(i => { foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][i]; var r = PlacaCalc.Pilar(M, nn, out Secao s2, out double a2, out double b2, out string e2); return r != null && r.Res != null ? Math.Abs(r.Res.M(r.N1 == nn.Id ? 0 : r.Res.L)) : 0; });
            JanelaPlaca j;
            try { j = new JanelaPlaca(M, n) { Owner = this }; }
            catch (Exception ex) { n.Placa = orig; if (crit != null) RestaurarResultado(); Log("Erro ao abrir a placa: " + ex.Message); return; }
            if (crit != null) j.Title += " — combinação crítica: " + crit;
            // outros pés de pilar onde a mesma placa pode ir, avaliados cada um na sua pior combinação
            j.Candidatos = M.Nos.Where(x => x != nn && (x.Placa != null || x.TemApoio) && PlacaCalc.Pilar(M, x, out Secao s3, out double a3, out double b3, out string e3) != null).ToList();
            j.AvaliarEm = (no, pl) =>
            {
                var salvo = M.Barras.Select(b => b.Res).ToList();
                var p0 = no.Placa; no.Placa = pl;
                try
                {
                    if (Vista.Envoltoria == null) return PlacaCalc.Calcular(M, no);
                    ResPlaca pior = null; double nota = double.MinValue;
                    for (int i = 0; i < Vista.NomesEnv.Count; i++)
                    {
                        foreach (Barra b in M.Barras) b.Res = Vista.Envoltoria[b][i];
                        ResPlaca rp = PlacaCalc.Calcular(M, no);
                        double v = NotaPlaca(rp, no);
                        if (pior == null || v > nota) { pior = rp; nota = v; }
                    }
                    return pior;
                }
                finally { no.Placa = p0; for (int k = 0; k < M.Barras.Count; k++) M.Barras[k].Res = salvo[k]; }
            };
            j.ShowDialog();
            if (crit != null) RestaurarResultado();
            if (j.Mudou && n.Placa != null) foreach (No o in j.AplicarEm) o.Placa = n.Placa.Copiar();
            if (j.Mudou) Confirmar(n.Placa == null ? "Placa de base removida do nó " + n.Id + "." : "Placa de base do nó " + n.Id + " atualizada" + (j.AplicarEm.Count > 0 ? " e copiada para os nós " + string.Join(", ", j.AplicarEm.Select(o => o.Id)) : "") + ".");
        }

        void AbrirAssistente(string tipo)
        {
            Bastidor.IsOpen = false;
            var j = new JanelaAssistente(tipo, cbSecao.Text, cbMaterial.Text) { Owner = this };
            if (j.ShowDialog() == true && j.Resultado != null)
            {
                PrepararDocNovo();
                NovoModelo(j.Resultado, null);
                alterado = true; AtualizarTitulo(); Atualizar();
                Log("Modelo gerado pelo assistente: " + j.Resultado.Barras.Count + " barras. Confira e salve.");
            }
        }

        void MostrarAjuda() { new JanelaAjuda { Owner = this }.Show(); }

        // ================= teclado e linha de comando =================

        static readonly string[] Comandos = { "BARRA", "NO", "MEDIR", "DI", "DIST", "SEL", "APAGAR", "DIVIDIR", "ZE", "DESFAZER", "REFAZER", "CALC", "NOVO", "ABRIR", "SALVAR",
            "UNIR", "TIRAR", "SEPARAR", "SELMEMBRO", "DEF", "NORMAL", "CORTANTE", "MOMENTO", "CALOR", "MOVER", "COPIAR", "ESPELHAR", "QD", "FN", "SELNOS", "SELBASE", "SELTODAS", "REAC", "GRADE", "ORTO", "POLAR", "ENGASTE", "FIXO", "MOVEL", "ROTULA", "CARGA", "ASSISTENTE", "TABELAS", "CSV", "AJUDA", "ABA", "CASOS", "CASO", "NOVOCASO", "GERARCOMB", "RESULTADO", "RELATORIO" };

        void MostrarSugestao()
        {
            string t = Comando.Text.Trim().ToUpperInvariant();
            if (t.Length == 0 || t.Any(c => char.IsDigit(c) || c == ',' || c == '@' || c == '<' || c == ' ')) { Sugestao.Text = ""; return; }
            string s = Comandos.FirstOrDefault(c => c.StartsWith(t));
            Sugestao.Text = s != null ? new string(' ', Comando.Text.Length) + s.Substring(t.Length) + "   (Tab completa)" : "";
        }

        bool Editando()
        {
            return (Keyboard.FocusedElement is TextBox tb && tb != Comando) || Insp.IsKeyboardFocusWithin || Keyboard.FocusedElement is ComboBox
                || (Keyboard.FocusedElement is FrameworkElement fe && fe.TemplatedParent is ComboBox);
        }

        // Digitar com o mouse no desenho manda o texto para a linha de comando (como no AutoCAD).
        void TextoDigitado(object s, TextCompositionEventArgs e)
        {
            if (Editando() || Comando.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            if (!(Keyboard.FocusedElement == Vista || Vista.IsMouseOver)) return;
            char c = e.Text[0];
            // Entrada dinamica (barra/medir em andamento): digitos vao para a caixinha do campo ativo.
            if (Vista.DinAtivo)
            {
                if (Vista.DinTecla(e.Text, Key.None)) { e.Handled = true; return; }
                // virgula/ponto-e-virgula/< viram coordenadas na linha de comando: leva junto o que ja foi digitado
                string pend = Vista.Din[Vista.DinCampo];
                if (pend != "" && (c == ',' || c == ';' || c == '<')) { Vista.DinLimpar(); Comando.Focus(); Comando.Text = pend + e.Text; Comando.CaretIndex = Comando.Text.Length; e.Handled = true; return; }
            }
            // Como no AutoCAD: tudo o que se digita vai para a linha de comando e so executa no Enter/Espaco.
            // Assim "MOVEL" nao dispara "M" (momento) no meio da palavra. Atalhos de uma letra: B, M, Q, N, D, V + Enter.
            if (char.IsLetterOrDigit(c) || c == '@' || c == '-' || c == ',' || c == '.' || c == ';' || c == '<')
            {
                Comando.Focus();
                Comando.Text += e.Text; Comando.CaretIndex = Comando.Text.Length;
                e.Handled = true;
            }
        }

        // Diagrama pelo teclado: o mesmo comando liga e, repetido, desliga (igual ao botao da faixa).
        void AlternarDiagrama(Diagrama d) { MostrarDiagrama(Vista.Diag == d ? Diagrama.Nenhum : d); }

        // Enter na entrada dinamica: fecha o ponto com o comprimento/angulo digitados (e o que faltar vem do mouse, com snap/orto).
        internal void ConfirmarDinamica()
        {
            Point pd = Vista.PontoDinamico();
            bool medir = Vista.Ferr == Ferramenta.Medir;
            Vista.DinLimpar(); Vista.Focus();
            if (medir) PontoMedida(pd.X, pd.Y); else PontoBarra(pd.X, pd.Y);
            Vista.InvalidateVisual();
        }

        void Tecla(object s, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool editando = Editando();
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!editando && !ctrl && Comando.Text == "" && Tecla3D(k)) { e.Handled = true; return; }   // ferramentas da Vista3D (modelo espacial)
            if (!editando && !ctrl && Vista.DinAtivo && Comando.Text == "" && (k == Key.Tab || k == Key.Back || ((k == Key.Enter || k == Key.Return) && Vista.DinTravado)))
            {
                if (k == Key.Enter || k == Key.Return)
                {
                    ConfirmarDinamica(); e.Handled = true;
                }
                else if (Vista.DinTecla(null, k)) { Vista.Focus(); e.Handled = true; }
            }
            else if (ctrl && k == Key.K) { AbrirPaleta(); e.Handled = true; }
            else if (ctrl && k == Key.Z && !editando) { Desfazer(); e.Handled = true; }
            else if (ctrl && k == Key.Y && !editando) { Refazer(); e.Handled = true; }
            else if (ctrl && shift && k == Key.S) { Salvar(true); e.Handled = true; }
            else if (ctrl && k == Key.S) { Salvar(false); e.Handled = true; }
            else if (ctrl && k == Key.O) { AbrirDialogo(); e.Handled = true; }
            else if (ctrl && k == Key.N) { Novo(); e.Handled = true; }
            else if (ctrl && k == Key.Tab && docs.Count > 1) { TrocarDoc((docAtivo + (shift ? docs.Count - 1 : 1)) % docs.Count); e.Handled = true; }
            else if (ctrl && k == Key.F4) { FecharDoc(docAtivo); e.Handled = true; }
            else if (ctrl && k == Key.E) { ExportarCsv(); e.Handled = true; }
            else if (ctrl && k == Key.P) { AbrirRelatorio(); e.Handled = true; }
            else if (ctrl && k == Key.M && !editando) { AcionarFerramenta(Ferramenta.Medir); e.Handled = true; }
            else if (ctrl && k == Key.A && !editando) { Vista.Selecionar(M.Barras.Cast<object>()); e.Handled = true; }
            else if (k == Key.F1) { MostrarAjuda(); e.Handled = true; }
            else if (k == Key.F2) { MostrarHistorico(); e.Handled = true; }
            else if (k == Key.F3) { AlternarStatus(stObj, v => Vista.AtrairObjetos = v, "Atração a objetos"); e.Handled = true; }
            else if (k == Key.F11) { AlternarStatus(stRastro, v => { Vista.Rastrear = v; if (!v) Vista.LimparRastreio(); }, "Rastreamento"); e.Handled = true; }
            else if (k == Key.F4) { Vista.ZoomTudo(); e.Handled = true; }
            else if (k == Key.F5) { Calcular(); e.Handled = true; }
            else if (k == Key.F7) { AlternarStatus(stGrade, v => { M.MostrarGrade = v; Vista.InvalidateVisual(); }, "Grade"); e.Handled = true; }
            else if (k == Key.F8) { AlternarStatus(stOrto, v => Vista.Orto = v, "ORTO"); e.Handled = true; }
            else if (k == Key.F9) { AlternarStatus(stAtrair, v => M.AtrairGrade = v, "SNAP na grade"); e.Handled = true; }
            else if (k == Key.F10) { AlternarStatus(stPolar, v => Vista.Polar = v, "Polar"); e.Handled = true; }
            else if (k == Key.Escape)
            {
                // cancela em cascata: texto digitado > barra/medida em andamento > ferramenta > selecao
                if (Comando.Text != "") Comando.Text = "";
                else if (Vista.DinTravado) Vista.DinLimpar();
                else if (Vista.InicioBarra != null) { Vista.InicioBarra = null; Log("Barra terminada."); }
                else if (Vista.MedidaIni != null) { Vista.MedidaIni = null; Vista.MedidaFim = null; Vista.Etiqueta = "MEDIR — clique o 1º ponto (ou digite x,y).   Botão direito ou Esc encerra."; Log("Medida cancelada."); }
                else if (Vista.Ferr != Ferramenta.Selecionar) UsarFerramenta(Ferramenta.Selecionar);
                else if (Vista.Sel.Count > 0) { Vista.Sel.Clear(); SelecaoMudou(); }
                Vista.InvalidateVisual(); Vista.Focus();
                e.Handled = true;
            }
            else if (k == Key.Delete && !editando && Comando.Text == "") { Apagar(); e.Handled = true; }
            else if ((k == Key.Add || k == Key.OemPlus) && !editando && Comando.Text == "" && !Comando.IsKeyboardFocusWithin) { Escala(1.4); e.Handled = true; }
            else if ((k == Key.Subtract || k == Key.OemMinus) && !editando && Comando.Text == "" && !Comando.IsKeyboardFocusWithin && !(Vista.Ferr == Ferramenta.Barra && Vista.InicioBarra != null)) { Escala(1 / 1.4); e.Handled = true; }
        }

        void ComandoTecla(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                string t0 = Comando.Text.Trim().ToUpperInvariant();
                string c = Comandos.FirstOrDefault(x => t0.Length > 0 && x.StartsWith(t0));
                if (c != null) { Comando.Text = c; Comando.CaretIndex = c.Length; }
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            string t = Comando.Text.Trim();
            if (e.Key == Key.Space && (t.Length == 0 || char.IsDigit(t[0]) || t[0] == '-' || t[0] == '@' || t.Contains(",") || t.Contains(";"))) return;
            e.Handled = true;
            Comando.Text = "";
            Vista.Focus();
            if (t == "")
            {
                if (Vista.Ferr == Ferramenta.Barra && Vista.InicioBarra != null) { Vista.InicioBarra = null; Log("Barra terminada."); Vista.InvalidateVisual(); }
                return;
            }
            Log("» " + t);
            Executar(t);
        }

        bool LerPonto(string t, out Point p)
        {
            p = new Point();
            bool rel = t.StartsWith("@");
            if (rel) t = t.Substring(1);
            Point? iniAtivo = Vista.Ferr == Ferramenta.Medir && Vista.MedidaFim == null ? Vista.MedidaIni : Vista.InicioBarra;
            Point base0 = iniAtivo ?? new Point(0, 0);
            double l, ang;
            if (t.Contains("<"))
            {
                string[] pr = t.Split('<');
                if (pr.Length != 2 || !Num.Ler(pr[0], out l) || !Num.Ler(pr[1], out ang)) return false;
                double r = ang * Math.PI / 180;
                Point o = rel ? base0 : new Point(0, 0);   // como no AutoCAD: L<ang sem @ e a partir da origem
                p = new Point(o.X + l * Math.Cos(r), o.Y + l * Math.Sin(r));
                return double.IsFinite(p.X) && double.IsFinite(p.Y);
            }
            // so um numero: comprimento na direcao do mouse (entrada direta de distancia)
            // (decimal com ponto; a virgula separa x,y como no AutoCAD; com ";" a virgula volta a ser decimal: 2,5;3)
            if (!rel && iniAtivo != null && !t.Contains(",") && !t.Contains(";") && !t.Contains(" ") && Num.Ler(t, out l))
            {
                Point m = Vista.PontoMouse;
                Vector d = new Vector(m.X - base0.X, m.Y - base0.Y);
                if (d.Length < 1e-9) d = new Vector(1, 0);
                d.Normalize();
                p = new Point(base0.X + l * d.X, base0.Y + l * d.Y);
                return double.IsFinite(p.X) && double.IsFinite(p.Y);
            }
            string[] c2 = t.Contains(";") ? t.Split(';') : t.Contains(" ") ? t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : t.Split(',');
            if (c2.Length != 2) return false;
            double x, y;
            if (!Num.Ler(c2[0], out x) || !Num.Ler(c2[1], out y)) return false;
            p = rel ? new Point(base0.X + x, base0.Y + y) : new Point(x, y);
            return double.IsFinite(p.X) && double.IsFinite(p.Y);
        }

        void Executar(string t)
        {
            t = (t ?? "").Trim();
            if (t == "") { if (Vista.InicioBarra != null) { Vista.InicioBarra = null; Log("Barra terminada."); Vista.InvalidateVisual(); } return; }
            Point p;
            if (LerPonto(t, out p))
            {
                if (Vista.Ferr == Ferramenta.Medir) { PontoMedida(Math.Round(p.X, 9), Math.Round(p.Y, 9)); return; }
                if (Vista.Ferr != Ferramenta.Barra) UsarFerramenta(Ferramenta.Barra);
                PontoBarra(Math.Round(p.X, 9), Math.Round(p.Y, 9));
                return;
            }
            string[] w = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (w[0].ToUpperInvariant())
            {
                case "B": case "BARRA": case "L": case "LINE": UsarFerramenta(Ferramenta.Barra); break;
                case "MEDIR": case "DI": case "DIST": UsarFerramenta(Ferramenta.Medir); break;
                case "S": case "SEL": UsarFerramenta(Ferramenta.Selecionar); break;
                case "NO": case "NÓ": UsarFerramenta(Ferramenta.No); break;
                case "ZE": case "Z": case "ZOOM": Vista.ZoomTudo(); break;
                case "APAGAR": case "E": case "ERASE": Apagar(); break;
                case "DIVIDIR": DividirSelecao(); break;
                case "UNIR": UnirMembro(); break;
                case "TIRAR": TirarTrecho(); break;
                case "SEPARAR": SepararMembro(); break;
                case "SELMEMBRO": SelecionarMembro(); break;
                case "U": case "DESFAZER": Desfazer(); break;
                case "REFAZER": Refazer(); break;
                case "CALC": case "CALCULAR": Calcular(); break;
                case "NOVO": Novo(); break;
                case "ABRIR": AbrirDialogo(); break;
                case "SALVAR": Salvar(false); break;
                case "N": case "NORMAL": AlternarDiagrama(Diagrama.Normal); break;
                case "Q": case "CORTANTE": AlternarDiagrama(Diagrama.Cortante); break;
                case "M": case "MOMENTO": AlternarDiagrama(Diagrama.Momento); break;
                case "D": case "DEF": case "DEFORMADA": AlternarDiagrama(Diagrama.Deformada); break;
                case "V": case "VERIFICAR": case "CALOR": case "MAPA": MostrarDiagrama(Vista.Diag == Diagrama.Verificacao && Vista.MapaCalor ? Diagrama.Nenhum : Diagrama.Verificacao, true); break;
                case "COMPARAR": AbrirVerificacao(null); break;
                case "DIMENSIONAR": DimensionarPerfis(false); break;
                case "DIMSEL": DimensionarPerfis(true); break;
                case "ELETRODO":
                    if (w.Length > 1 && Array.IndexOf(Solda.Eletrodos, w[1].ToUpperInvariant().Replace("XX", "")) >= 0) { M.Eletrodo = w[1].ToUpperInvariant().Replace("XX", ""); Confirmar("Eletrodo das soldas: " + M.Eletrodo + "XX."); }
                    else Log("Uso: ELETRODO E60 | E70 | E80 | E90 | E100 | E110 | E120");
                    break;
                case "SOLDAS": PainelTabelas.Show(); AbaSoldas.IsSelected = true; break;
                case "PLACA": AbrirPlaca(null); break;
                case "MOVER": case "MOVE": case "MV": Transformar("mover", w); break;
                case "COPIAR": case "COPY": case "CP": Transformar("copiar", w); break;
                case "ESPELHAR": case "MIRROR": case "MI": Transformar("espelhar", w); break;
                case "QD":
                {
                    // QD qy [qx]: carga distribuida nas barras selecionadas
                    double qy, qx = 0;
                    if (w.Length < 2 || !Num.Ler(w[1], out qy) || (w.Length > 2 && !Num.Ler(w[2], out qx))) { Log("Uso: QD qy [qx]   (kN/m; ex.: QD -15)"); break; }
                    var bs = Vista.Sel.OfType<Barra>().ToList();
                    if (bs.Count == 0) { Log("Selecione as barras antes (clique ou janela) e repita QD."); break; }
                    foreach (Barra b in bs) { b.QGlobalY = qy; b.QGlobalX = qx; }
                    Confirmar("Carga qy = " + Num.Fmt(qy, 2) + (qx != 0 ? ", qx = " + Num.Fmt(qx, 2) : "") + " kN/m em " + Pl.N(bs.Count, "barra", "barras") + ".");
                    break;
                }
                case "FN":
                {
                    // FN fx fy [mz]: carga nos nos selecionados
                    double fx, fy, mz = 0;
                    if (w.Length < 3 || !Num.Ler(w[1], out fx) || !Num.Ler(w[2], out fy) || (w.Length > 3 && !Num.Ler(w[3], out mz))) { Log("Uso: FN fx fy [mz]   (kN, kN·m; ex.: FN 10 0)"); break; }
                    var ns = Vista.Sel.OfType<No>().ToList();
                    if (ns.Count == 0) { Log("Selecione os nós antes e repita FN."); break; }
                    foreach (No n in ns) { n.Fx = fx; n.Fy = fy; n.Mz = mz; }
                    Confirmar("Carga aplicada em " + Pl.N(ns.Count, "nó", "nós") + ".");
                    break;
                }
                case "REAC": tgReac.IsChecked = !tgReac.IsChecked; Vista.MostrarReacoes = tgReac.IsChecked == true; Resolver(false); break;
                case "ENGASTE": AcionarFerramenta(Ferramenta.ApoioEngaste); break;
                case "FIXO": AcionarFerramenta(Ferramenta.ApoioRotula); break;
                case "MOVEL": case "MÓVEL": AcionarFerramenta(Ferramenta.ApoioRoloV); break;
                case "ROTULA": case "RÓTULA": AcionarFerramenta(Ferramenta.Rotula); break;
                case "CARGA": AcionarFerramenta(Ferramenta.CargaNo); break;
                case "ASSISTENTE": AbrirAssistente(w.Length > 1 ? w[1].ToLowerInvariant() : null); break;
                case "TABELAS": PainelTabelas.Show(); break;
                case "CSV": ExportarCsv(); break;
                case "GRADE":
                    if (w.Length > 1) { cGrade.Text = w[1]; MudarGrade(w[1]); }
                    else AlternarStatus(stGrade, v => { M.MostrarGrade = v; Vista.InvalidateVisual(); }, "Grade");
                    break;
                case "ORTO": AlternarStatus(stOrto, v => Vista.Orto = v, "ORTO"); break;
                case "POLAR": AlternarStatus(stPolar, v => Vista.Polar = v, "Polar"); break;
                case "ABA":
                    foreach (var tab in Faixa.Tabs) if (w.Length > 1 && (tab.Header as string ?? "").StartsWith(w[1], StringComparison.OrdinalIgnoreCase)) Faixa.SelectedTabItem = tab;
                    break;
                case "SELTODAS": Vista.Selecionar(M.Barras.Cast<object>()); break;
                case "CASOS": AbrirCasosConforme(); break;
                case "RELATORIO": case "RELATÓRIO": AbrirRelatorio(); break;
                case "RELPNG": if (w.Length > 1) SalvarRelatorioPng(string.Join(" ", w.Skip(1))); else Log("Uso: RELPNG pasta"); break;
                case "NOVOCASO":
                {
                    // NOVOCASO tipo nome...   (tipo G, Q, V ou E) — cria o caso e o deixa ativo
                    string tp = w.Length > 1 ? w[1].ToUpperInvariant() : "Q";
                    if (!new[] { "G", "Q", "V", "E" }.Contains(tp)) { Log("Uso: NOVOCASO G|Q|V|E nome"); break; }
                    M.AdicionarCaso(new CasoCarga { Nome = w.Length > 2 ? string.Join(" ", w.Skip(2)) : tp + M.Casos.Count, Tipo = tp });
                    M.AtivarCaso(M.Casos.Count - 1);
                    Confirmar("Caso \"" + M.Casos[M.CasoAtivo].Nome + "\" criado e ativo.");
                    break;
                }
                case "CASO":
                {
                    int ci;
                    if (w.Length > 1 && int.TryParse(w[1], out ci) && ci >= 1 && ci <= M.Casos.Count) { M.AtivarCaso(ci - 1); Confirmar("Caso ativo: " + M.Casos[M.CasoAtivo].Nome + "."); }
                    else Log("Uso: CASO número (1 a " + M.Casos.Count + ").");
                    break;
                }
                case "GERARCOMB": M.Combinacoes = Analise.GerarNbr(M.Casos); Confirmar(M.Combinacoes.Count + " combinações NBR 8681/8800 geradas."); break;
                case "RESULTADO":
                    if (w.Length > 1) EscolherResultado(w[1].ToLowerInvariant().Replace("elu", "ELU").Replace("els", "ELS"));
                    else Log("Uso: RESULTADO ativo | caso:N | comb:N | env:ELU | env:ELS  (N a partir de 0)");
                    break;
                case "SELNOS": Vista.Selecionar(M.Nos.Cast<object>()); Log(M.Nos.Count + " nós selecionados."); break;
                case "SELBASE": { var b0 = M.Nos.Count == 0 ? new List<object>() : M.Nos.Where(n => Math.Abs(n.Y - M.Nos.Min(x => x.Y)) < 1e-6).Cast<object>().ToList(); Vista.Selecionar(b0); Log(Pl.N(b0.Count, "nó da base selecionado", "nós da base selecionados") + "."); break; }
                case "SELNO":
                {
                    int id; var n = w.Length > 1 && int.TryParse(w[1], out id) ? M.NoPorId(id) : null;
                    if (n != null) { Vista.Selecionar(new object[] { n }); Log("Nó " + n.Id + " selecionado."); }
                    else Log("⛔ Nó não encontrado. Nós existentes: " + string.Join(", ", M.Nos.Select(x => x.Id)) + ".");
                    break;
                }
                case "SELBARRA":
                {
                    int id; var b = w.Length > 1 && int.TryParse(w[1], out id) ? M.Barras.FirstOrDefault(x => x.Id == id) : null;
                    if (b != null) { Vista.Selecionar(new object[] { b }); Log("Barra " + b.Id + " selecionada."); }
                    else Log("⛔ Barra não encontrada. Barras existentes: " + string.Join(", ", M.Barras.Select(x => x.Id)) + ".");
                    break;
                }
                case "AJUDA": case "?":
                    Log("Comandos: " + string.Join(", ", Comandos) + ". Pontos: x,y  @dx,dy  @L<ângulo  ou só o comprimento.");
                    break;
                default:
                    double num;
                    if (Num.Ler(t, out num)) Log("Um número sozinho é o comprimento da barra: comece uma barra (B + Enter, clique o 1º ponto) e então digite " + t + ".");
                    else Log("Comando desconhecido: " + t + " — digite AJUDA ou tecle F1.");
                    break;
            }
        }

        // ---------- MOVER / COPIAR / ESPELHAR (sobre a selecao) ----------
        // MOVER dx dy · COPIAR dx dy [vezes] · ESPELHAR X valor | Y valor   (tambem aceita "dx,dy" e "@dx,dy")
        void Transformar(string modo, string[] w)
        {
            if (Esp != null) { AbrirEdicaoEsp(modo); return; }   // modelo espacial: EdicaoEspacial.cs
            var barras = Vista.Sel.OfType<Barra>().ToList();
            var nos = Vista.Sel.OfType<No>().ToList();
            if (barras.Count == 0 && (modo != "mover" || nos.Count == 0)) { Log("Selecione as barras" + (modo == "mover" ? " ou nós" : "") + " antes (clique, janela ou Ctrl+A) e repita."); return; }
            string args = string.Join(" ", w.Skip(1)).Replace("@", "").Trim();
            if (args == "")
            {
                string dica = modo == "espelhar" ? "Eixo do espelho: \"X 6\" (reta vertical x = 6) ou \"Y 0\" (horizontal y = 0)."
                            : modo == "copiar" ? "Deslocamento e número de cópias: \"dx dy vezes\" (m). Ex.: 6 0 2" : "Deslocamento \"dx dy\" (m). Ex.: 0 0,5";
                args = Pergunta.Mostrar(this, modo == "mover" ? "Mover" : modo == "copiar" ? "Copiar" : "Espelhar", dica, modo == "espelhar" ? "X 0" : modo == "copiar" ? "0 0 1" : "0 0");
                if (args == null) return;
            }
            string[] a = args.Replace(";", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 1 && a[0].Contains(",") && modo != "espelhar") a = a[0].Split(',');   // "dx,dy"

            if (modo == "espelhar")
            {
                double v;
                if (a.Length < 2 || !(a[0].ToUpperInvariant() == "X" || a[0].ToUpperInvariant() == "Y") || !Num.Ler(a[1], out v)) { Log("Uso: ESPELHAR X valor  ou  ESPELHAR Y valor"); return; }
                bool emX = a[0].ToUpperInvariant() == "X";
                int qt = CopiarBarras(barras, p => emX ? new Point(2 * v - p.X, p.Y) : new Point(p.X, 2 * v - p.Y), true);
                Confirmar(Pl.N(qt, "barra espelhada", "barras espelhadas") + " em " + (emX ? "x" : "y") + " = " + Num.Fmt(v, 3) + ".");
                return;
            }
            double dx, dy;
            if (a.Length < 2 || !Num.Ler(a[0], out dx) || !Num.Ler(a[1], out dy)) { Log("Uso: " + modo.ToUpperInvariant() + " dx dy" + (modo == "copiar" ? " [vezes]" : "")); return; }
            if (modo == "mover")
            {
                var alvo = new HashSet<No>(nos);
                foreach (Barra b in barras) { alvo.Add(M.NoPorId(b.N1)); alvo.Add(M.NoPorId(b.N2)); }
                foreach (No n in alvo) { n.X = Math.Round(n.X + dx, 9); n.Y = Math.Round(n.Y + dy, 9); }
                var sobrepostos = M.Nos.Where(n => !alvo.Contains(n) && alvo.Any(o => Math.Abs(o.X - n.X) < 1e-6 && Math.Abs(o.Y - n.Y) < 1e-6)).ToList();
                Confirmar(Pl.N(alvo.Count, "nó movido", "nós movidos") + " (" + Num.Fmt(dx, 3) + "; " + Num.Fmt(dy, 3) + ")." + (sobrepostos.Count > 0 ? " ⚠ Há nós sobrepostos: não foram fundidos." : ""));
                return;
            }
            int vezes = 1;
            if (a.Length > 2 && (!int.TryParse(a[2], out vezes) || vezes < 1 || vezes > 200)) { Log("Número de cópias: 1 a 200."); return; }
            int total = 0;
            for (int k = 1; k <= vezes; k++) { int kk = k; total += CopiarBarras(barras, p => new Point(p.X + kk * dx, p.Y + kk * dy), true); }
            Confirmar(Pl.N(total, "barra criada", "barras criadas") + " em " + Pl.N(vezes, "cópia", "cópias") + ".");
            Vista.ZoomTudo();
        }

        // Copia barras (secao, material, cargas, rotulas) e os atributos dos nos novos (apoio, carga, molas).
        int CopiarBarras(List<Barra> barras, Func<Point, Point> f, bool copiarNos)
        {
            int criadas = 0;
            var novoMembro = new Dictionary<int, int>();   // a copia de uma barra continua vira outra barra continua
            int baseMembro = M.ProxMembro();
            foreach (Barra b in barras.ToList())
            {
                No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                if (a == null || c == null) continue;
                Point pa = f(new Point(a.X, a.Y)), pc = f(new Point(c.X, c.Y));
                No na = NoNovoOuExistente(pa, a, copiarNos), nc = NoNovoOuExistente(pc, c, copiarNos);
                int antes = M.Barras.Count;
                M.AddBarra(na, nc, 1e-6);
                for (int i = antes; i < M.Barras.Count; i++)
                {
                    Barra nb = M.Barras[i];
                    nb.Secao = b.Secao; nb.Material = b.Material; nb.EixoFraco = b.EixoFraco;
                    nb.QGlobalX = b.QGlobalX; nb.QGlobalY = b.QGlobalY; nb.QLocalIni = b.QLocalIni; nb.QLocalFim = b.QLocalFim;
                    nb.RotulaInicio = b.RotulaInicio; nb.RotulaFim = b.RotulaFim;
                    if (b.Membro != 0) { if (!novoMembro.ContainsKey(b.Membro)) novoMembro[b.Membro] = baseMembro + novoMembro.Count; nb.Membro = novoMembro[b.Membro]; }
                    criadas++;
                }
            }
            return criadas;
        }

        No NoNovoOuExistente(Point p, No modelo, bool copiar)
        {
            bool existia = M.Nos.Any(n => Math.Abs(n.X - p.X) <= 1e-6 && Math.Abs(n.Y - p.Y) <= 1e-6);
            No n2 = M.ObterNo(Math.Round(p.X, 9), Math.Round(p.Y, 9), 1e-6);
            if (!existia && copiar)
            {
                n2.ApoioX = modelo.ApoioX; n2.ApoioY = modelo.ApoioY; n2.ApoioGiro = modelo.ApoioGiro;
                n2.MolaX = modelo.MolaX; n2.MolaY = modelo.MolaY; n2.MolaGiro = modelo.MolaGiro;
                n2.Fx = modelo.Fx; n2.Fy = modelo.Fy; n2.Mz = modelo.Mz;
            }
            return n2;
        }

        // F2: historico completo da linha de comando (como a janela de texto do AutoCAD)
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
