// Botões da faixa que abrem as janelas do motor Python (ComandosAcoes, ComandosAnalise; REVISAO-TELAS 3.2).
// Antes de abrir, o modelo 2D vai para o motor (MotorModelo.Enviar, docs/UNIFICACAO-MOTOR.md); as janelas que dependem
// do cálculo (estabilidade, verificação) calculam antes, com o brilho de progresso na barra de status. Erro do motor = toast.
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        bool MotorAtivo => cbMotor != null && cbMotor.SelectedIndex == 1;

        void ErroMotor(Exception ex)
        {
            StatusMsg.Text = "⚠ Motor Python: " + ComandosAnalise.MensagemMotor(ex);
            Log(StatusMsg.Text);
            MostrarToast(StatusMsg.Text);
        }

        // calcular = false: só envia o modelo; true: envia, calcula (combinações NBR 8681 e NBR 8800) e guarda ResMotor.
        async Task<bool> PrepararMotor(bool calcular)
        {
            if (motorRodando) { MostrarToast("Aguarde: o motor Python ainda está trabalhando."); return false; }
            if (calcular)
            {
                if (!Vista.Resolvido) Resolver(true);
                if (!Vista.Resolvido) { MostrarToast("Corrija os avisos do modelo antes de calcular (F5)."); return false; }
                ResMotor = null;
                await CalcularMotor();
                return ResMotor != null;
            }
            motorRodando = true;
            var barra = Anim.Brilho(90, 4); barra.Margin = new Thickness(0, 0, 10, 0); barra.VerticalAlignment = VerticalAlignment.Center;
            StatusBotoes.Children.Insert(0, barra);
            var antes = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.AppStarting;
            StatusMsg.Text = "Motor Python: enviando o modelo…";
            try { var mod = Modelo.DeXml(M.ParaXml()); await Motor.Exclusivo(() => MotorModelo.Enviar(mod)); StatusMsg.Text = "Motor Python: modelo enviado."; return true; }
            catch (Exception ex) { ErroMotor(ex); return false; }
            finally { StatusBotoes.Children.Remove(barra); Mouse.OverrideCursor = antes; motorRodando = false; }
        }

        // abrir devolve Task (as janelas de análise buscam no motor antes de abrir): a fila do Motor.Exclusivo fica presa até a
        // busca terminar (antes, com async void, a trava soltava no primeiro await e outra aba podia trocar o modelo do servidor).
        void ComMotor(bool calcular, Action<Window> abrir) => _ = ComMotor(calcular, w => { abrir(w); return Task.CompletedTask; });
        internal async Task ComMotor(bool calcular, Func<Window, Task> abrir)
        {
            if (Esp != null && !calcular) { await JanelaMotorEsp(abrir); return; }   // CargasEspacial.cs: edita o modelo espacial
            if (Esp != null) { if (await GarantirCalculoEsp()) await JanelaMotorEsp(abrir); return; }   // janelas de resultado: o cálculo do documento 3D
            if (await PrepararMotor(calcular)) await abrir(this);
        }

        // Vento, Sismo, Pavimentos e Casos do motor: o que a janela mudou no motor vai para M.MotorExtra (arquivo, desfazer,
        // "modificado"); o próximo MotorModelo.Enviar devolve ao motor. As janelas são modais: nada envia no meio.
        async void ComMotorExtra(Action<Window> abrir)
        {
            if (Esp != null) { _ = JanelaMotorEsp(w => { abrir(w); return Task.CompletedTask; }); return; }   // CargasEspacial.cs: edita o modelo espacial
            if (!await PrepararMotor(false)) return;
            string antes;
            try { antes = await Motor.Exclusivo(MotorModelo.LerExtra); } catch (Exception ex) { ErroMotor(ex); return; }
            abrir(this);
            try
            {
                string depois = await Motor.Exclusivo(MotorModelo.LerExtra);
                if (depois != antes) { M.MotorExtra = depois; Confirmar("Ações do motor alteradas (vento, sismo, pavimentos ou casos)."); }
            }
            catch (Exception ex) { ErroMotor(ex); }
        }

        // Casos: com o motor Python, a janela nova (casos do motor); no interno, a janela antiga de casos e combinações.
        void AbrirCasosConforme()
        {
            if (MotorAtivo || Esp != null) ComMotorExtra(ComandosAcoes.AbrirCasos);
            else AbrirCasos();
        }

        // Verificar barra: com o motor Python, a verificação NBR 8800 detalhada; no interno, a janela antiga.
        void VerificarBarraConforme(Barra b)
        {
            if (Esp != null)
            {
                string id = vista3D.Selecao.Barras.FirstOrDefault();
                if (id == null) { MostrarToast("Selecione uma barra (clique nela) e repita."); return; }
                _ = ComMotor(true, w => ComandosAnalise.AbrirVerificarBarra(w, id));
                return;
            }
            b ??= Vista.Sel.OfType<Barra>().FirstOrDefault();
            if (b == null) { MostrarToast("Selecione uma barra (clique nela) e repita."); return; }
            _ = ComMotor(true, w => ComandosAnalise.AbrirVerificarBarra(w, "b" + b.Id));
        }

        // Ligação: tipo parafuso, solda_filete, chapa_simples ou ligacao_tracao (não depende do modelo).
        void AbrirLigacaoTipo(string tipo) { _ = ComandosAnalise.AbrirLigacao(this, tipo); }

        // Teste ponta a ponta (Estructto.exe --teste-motor=saida.png arquivo.estructto, com ESTRUCTTO_MOTOR ou motor\ empacotado):
        // liga o motor Python, faz o mesmo que o botão Dimensionamento › Verificação de barras e grava a janela aberta em PNG
        // (+ saida.txt com a verificação). Código de saída 0 = janela aberta com dados do motor.
        async void TesteMotor(string png)
        {
            string txt = System.IO.Path.ChangeExtension(png, ".txt");
            cbMotor ??= CriarCbMotor();   // legado escondido: Interno + motor (fora da faixa)
            cbMotor.SelectedIndex = 1;
            _ = ComMotor(true, ComandosAnalise.AbrirVerificacaoBarras);
            for (int i = 0; i < 1200; i++)   // ate 2 min
            {
                var j = OwnedWindows.OfType<JanelaVerificacaoBarras>().FirstOrDefault();
                if (j != null)
                {
                    await Task.Delay(800);
                    j.UpdateLayout();
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)j.ActualWidth, (int)j.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    var fundo = new System.Windows.Media.DrawingVisual();
                    using (var dc = fundo.RenderOpen()) dc.DrawRectangle(j.Background ?? JanelaEscura.BFundo, null, new Rect(0, 0, j.ActualWidth, j.ActualHeight));
                    rtb.Render(fundo); rtb.Render(j);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = System.IO.File.Create(png)) enc.Save(fs);
                    // documento 3D: a verificação vem do motor direto para a janela (ResMotor é do caminho 2D legado)
                    System.IO.File.WriteAllText(txt, StatusMsg.Text + "\n" + (ResMotor == null ? "janela aberta com o documento 3D" : string.Join("\n", ResMotor.Verificacao.Select(v =>
                        "barra " + v.Barra + ": " + (v.Taxa.HasValue ? Num.Fmt(v.Taxa.Value * 100, 1) + "%" : "-") + " · " + v.Criterio + " · " + v.Item + " · " + v.Combinacao))));
                    Motor.Desligar();
                    Environment.Exit(0);
                }
                if (i % 10 == 0) System.IO.File.WriteAllText(txt, "aguardando " + i / 10 + " s: " + StatusMsg.Text + " | janelas: " + string.Join(", ", OwnedWindows.Cast<Window>().Select(w => w.GetType().Name)));
                if (!motorRodando && i > 20 && ResMotor == null) break;   // erro do motor (toast)
                await Task.Delay(100);
            }
            System.IO.File.WriteAllText(txt, "FALHOU: " + StatusMsg.Text);
            Motor.Desligar();
            Environment.Exit(1);
        }
    }
}
