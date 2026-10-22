// Faixa "Nova versão disponível" no topo da janela principal e o fluxo Instalar (docs/v3/ATUALIZACAO.md).
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovoFtool
{
    public partial class MainWindow
    {
        ReleaseInfo atualizacao;
        CancellationTokenSource ctsAtualiza;
        bool baixandoAtualizacao;
        bool fechandoParaAtualizar;   // lido pelo Closing (Acoes.cs): o usuário já respondeu sobre os documentos não salvos

        internal bool FaixaAtualizaVisivel { get { return FaixaAtualiza.Visibility == Visibility.Visible; } }
        internal string TextoFaixaAtualiza { get; private set; } = "";

        static void LogAtualiza(string msg)
        {
            try { Directory.CreateDirectory(Config.Pasta); File.AppendAllText(Path.Combine(Config.Pasta, "atualizacao.log"), DateTime.Now.ToString("s") + " " + msg + Environment.NewLine); } catch { }
        }

        // Ao abrir: em segundo plano, 5 s de teto, falha silenciosa (só log).
        internal void IniciarChecagemAtualizacao()
        {
            if (Config.Opcoes.ProcurarAtualizacoes) ProcurarAtualizacao(false);
        }

        internal async void ProcurarAtualizacao(bool manual)
        {
            try
            {
                var r = await Atualizador.ProcurarAsync(Atualizador.VersaoAtual);
                if (r != null) AplicarAtualizacao(r, manual);
                else if (manual) MostrarToast("Você já está na versão mais recente (" + Atualizador.VersaoAtual + ").", true);
            }
            catch (Exception ex)
            {
                LogAtualiza("checagem falhou: " + ex.Message);
                if (manual) MostrarToast("Não foi possível procurar atualizações (sem internet ou GitHub fora do ar).", true);
            }
        }

        // "Depois" esconde até sair uma versão ainda mais nova; a busca manual mostra de novo.
        internal void AplicarAtualizacao(ReleaseInfo r, bool manual)
        {
            if (!manual && Ver.TryParse(Config.Opcoes.AtualizacaoIgnorada, out Ver ign) && r.Versao.CompareTo(ign) <= 0) return;
            atualizacao = r;
            MontarFaixaAtualiza("Nova versão v" + r.Versao + " disponível — o programa baixa, confere o SHA-256 e se atualiza sozinho.", true);
            FaixaAtualiza.Visibility = Visibility.Visible;
        }

        internal void EsconderFaixaAtualiza() { FaixaAtualiza.Visibility = Visibility.Collapsed; }

        void MontarFaixaAtualiza(string texto, bool botoes, double? progresso = null, bool erro = false)
        {
            TextoFaixaAtualiza = texto;
            var linha = new DockPanel();
            var ic = NovoFtool.Icones.Criar(erro ? "erro" : "info", 16); ic.Opacity = 1;
            DockPanel.SetDock(ic, Dock.Left); ((FrameworkElement)ic).Margin = new Thickness(0, 0, 10, 0); ((FrameworkElement)ic).VerticalAlignment = VerticalAlignment.Center;
            linha.Children.Add(ic);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(bts, Dock.Right);
            if (botoes)
            {
                bts.Children.Add(JanelaEscura.B("Instalar", Instalar, true, "Baixa a versão nova, confere o SHA-256, fecha o Estructto, troca os arquivos e reabre."));
                bts.Children.Add(JanelaEscura.B("Notas", () => AbrirNotasAtualizacao(), false, "Abre as notas da versão no GitHub."));
                bts.Children.Add(JanelaEscura.B("Depois", () => { if (atualizacao != null) Config.Opcoes.AtualizacaoIgnorada = atualizacao.Versao.ToString(); EsconderFaixaAtualiza(); }, false, "Esconde o aviso até sair uma versão mais nova."));
            }
            else if (progresso != null)
                bts.Children.Add(JanelaEscura.B("Cancelar", () => ctsAtualiza?.Cancel(), false, "Interrompe o download; nada é instalado."));
            linha.Children.Add(bts);
            var centro = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            centro.Children.Add(new TextBlock { Text = texto, FontSize = 12, Foreground = erro ? (Brush)(TryFindResource("Status.Error") ?? TemaPlant.BTexto1) : TemaPlant.BTexto1, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = texto });
            if (progresso != null)
                centro.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Max(0, progresso.Value), IsIndeterminate = progresso.Value < 0, Height = 4, Margin = new Thickness(0, 4, 12, 0) });
            linha.Children.Add(centro);
            FaixaAtualiza.Child = linha;
        }

        void AbrirNotasAtualizacao()
        {
            try
            {
                if (atualizacao != null && Uri.TryCreate(atualizacao.PaginaUrl, UriKind.Absolute, out Uri u) && Atualizador.HostPermitido(u))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { LogAtualiza("notas: " + ex.Message); }
        }

        async void Instalar()
        {
            var r = atualizacao;
            if (r == null || baixandoAtualizacao) return;
            string baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            if (!Directory.Exists(Path.Combine(baseDir, "motor")) || string.IsNullOrEmpty(Environment.ProcessPath))
            { MontarFaixaAtualiza("A instalação automática só funciona no pacote publicado (Estructto.exe com a pasta motor\\ ao lado).", true, null, true); return; }
            baixandoAtualizacao = true; ctsAtualiza = new CancellationTokenSource();
            string stage = null;
            try
            {
                if (string.IsNullOrEmpty(r.ShaUrl)) throw new InvalidOperationException("a release não publicou o arquivo .sha256; por segurança não instalo");
                stage = Path.Combine(Atualizador.PastaTemp, "v" + new string(r.Versao.ToString().Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-').ToArray()));
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                Directory.CreateDirectory(stage);
                string nomeZip = Path.GetFileName(r.ZipNome), zip = Path.Combine(stage, nomeZip);
                MontarFaixaAtualiza("Baixando v" + r.Versao + "…", false, -1);
                string sha = await Atualizador.BaixarTextoAsync(r.ShaUrl, ctsAtualiza.Token);
                var prog = new Progress<double>(p => MontarFaixaAtualiza("Baixando v" + r.Versao + (p >= 0 ? " — " + (int)(p * 100) + "%" : "…"), false, p));
                await Atualizador.BaixarAsync(r.ZipUrl, zip, prog, ctsAtualiza.Token);
                MontarFaixaAtualiza("Conferindo o SHA-256…", false, -1);
                string erroHash = await System.Threading.Tasks.Task.Run(() => Atualizador.ConferirHash(zip, sha, nomeZip));
                if (erroHash != null) { try { File.Delete(zip); } catch { } throw new InvalidDataException(erroHash); }
                MontarFaixaAtualiza("Preparando os arquivos…", false, -1);
                string raiz = await System.Threading.Tasks.Task.Run(() => Atualizador.Extrair(zip, Path.Combine(stage, "pacote")));
                if (!PodeFecharTudo()) { MontarFaixaAtualiza("Atualização adiada: há documento não salvo. Salve e clique em Instalar de novo.", true); return; }
                fechandoParaAtualizar = true;
                var script = Atualizador.IniciarScript(System.Diagnostics.Process.GetCurrentProcess().Id, raiz, baseDir, Environment.ProcessPath, stage);
                LogAtualiza("instalando v" + r.Versao + " de " + raiz);
                bool fechou = false; Closed += (s2, e2) => fechou = true;
                Close();
                if (!fechou) { Atualizador.CancelarScript(script); fechandoParaAtualizar = false; MontarFaixaAtualiza("O programa não fechou; a atualização foi cancelada. Clique em Instalar para tentar de novo.", true); }
            }
            catch (OperationCanceledException) { fechandoParaAtualizar = false; MontarFaixaAtualiza("Download cancelado. Nada foi instalado.", true); }
            catch (Exception ex)
            {
                fechandoParaAtualizar = false;
                LogAtualiza("instalar falhou: " + ex.Message);
                MontarFaixaAtualiza("Não foi possível atualizar: " + ex.Message, true, null, true);
            }
            finally { baixandoAtualizacao = false; ctsAtualiza?.Dispose(); ctsAtualiza = null; }
        }
    }
}
