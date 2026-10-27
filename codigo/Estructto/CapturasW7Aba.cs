// 63) W7: biblioteca e projetista de ligações em ABA da janela principal (LigacaoAbas.cs, docs/v3/W7-ABA.md).
// Capturas: aba da biblioteca, aba do projetista (chapa de extremidade flexível), duas abas de ligação + modelo.
// Teste (63-aba.txt): abre pela faixa, troca de aba pelo clique (estado preservado, sem novo Loaded), "Biblioteca" troca na
// mesma aba, volta ao modelo pela aba do modelo, fecha com o botão do meio e com Esc. Só elas: ESTRUCTTO_CAPTURAS=w7aba.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        // o projetista agora é uma aba: a foto é a janela principal com a aba na frente
        static void CapturarDialogo(JanelaLigacaoProjeto j, string arquivo)
        {
            var w = (MainWindow)Window.GetWindow(j) ?? (MainWindow)Application.Current.MainWindow;
            w.MostrarLigacao(j); EsperarIdle();
            RenderizarJanela(w, 1600, 900, arquivo);
        }
        static void FotoDialogo(JanelaLigacaoProjeto j, string nome) { try { CapturarDialogo(j, "fluxo-" + nome + ".png"); } catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "fluxo-erros-png.txt"), nome + ": " + ex.Message + Environment.NewLine); } }

        static void CapturasW7Aba(MainWindow w)
        {
            var log = new StringBuilder(); int falhas = 0;
            void Ok(bool c, string m) { if (!c) falhas++; log.AppendLine((c ? "ok      " : "FALHA   ") + m); }
            void Clicar(UIElement e, MouseButton b)
            {
                var a = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, b) { RoutedEvent = b == MouseButton.Left ? UIElement.MouseLeftButtonUpEvent : UIElement.MouseDownEvent };
                e.RaiseEvent(a); EsperarIdle();
            }
            string Abas() => string.Join(" | ", w.AbasDocs.Children.OfType<Border>().Concat(w.AbasLig.Children.OfType<Border>())
                .Select(b => ((TextBlock)((StackPanel)b.Child).Children[0]).Text + (b.Background == TemaPlant.BSup1 ? " [ativa]" : "")));
            foreach (var x in w.LigacoesAbertas.ToList()) x.Close();
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));

            // 1) pela faixa (Ligações e bases › Projetar ligação): biblioteca numa aba
            w.AbrirProjetistaLigacao();
            for (int i = 0; i < 300 && w.LigacoesAbertas.Count == 0; i++) { EsperarIdle(); System.Threading.Thread.Sleep(50); }
            var a = w.LigacoesAbertas.FirstOrDefault();
            Ok(a != null && w.LigacaoAtiva == a && !a.TemTipo, "faixa › Projetar ligação abre a biblioteca numa aba");
            if (a == null) throw new InvalidOperationException("63) biblioteca não abriu");
            Ok(Application.Current.Windows.OfType<Window>().All(x => x == w || !x.IsVisible), "nenhuma janela separada aberta");
            Ok(w.HostLigacoes.Visibility == Visibility.Visible && a.IsVisible && w.Faixa.IsVisible && w.StatusMsg.IsVisible, "aba cobre a área de trabalho; faixa e barra de status continuam");
            Esperar(a.CarregarMiniaturas()); EsperarIdle();
            log.AppendLine("abas: " + Abas());
            RenderizarJanela(w, 1600, 900, "63-aba-biblioteca.png");

            // 2) segunda aba: projetista da chapa de extremidade flexível
            var b = new JanelaLigacaoProjeto(tipos, "chapa_extremidade_flexivel");
            w.MostrarLigacao(b); EsperarIdle();
            Esperar(b.CalcularAgora()); EsperarIdle();
            Ok(b.NomeAba == "Ligação · Chapa de extremidade flexível", "nome da aba: " + b.NomeAba);
            Ok(w.LigacoesAbertas.Count == 2 && w.AbasLig.Children.Count == 2 && !a.IsVisible && b.IsVisible, "duas ligações = duas abas, só a ativa visível");
            log.AppendLine("abas: " + Abas());
            RenderizarJanela(w, 1600, 900, "63-aba-projetista-chapa-extremidade.png");
            RenderizarJanela(w, 1280, 720, "63-aba-projetista-chapa-extremidade-1280x720.png");

            // 3) trocar de aba preserva o estado (mesmo resultado, sem recarregar)
            var r0 = b.Resultado;
            Clicar(w.AbasLig.Children[0], MouseButton.Left);
            Ok(w.LigacaoAtiva == a && a.IsVisible && !b.IsVisible, "clique na aba 1 mostra a biblioteca");
            // modelo vazio não tem aba (Documentos.cs: tela Início); aí volta pelo botão Início
            var abaModelo = w.AbasDocs.Children.OfType<UIElement>().LastOrDefault();
            if (abaModelo != null) Clicar(abaModelo, MouseButton.Left);
            else { w.ArqInicio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); EsperarIdle(); }
            Ok(w.LigacaoAtiva == null && w.HostLigacoes.Visibility == Visibility.Collapsed && w.LigacoesAbertas.Count == 2, "clique na aba do modelo volta ao modelo (as ligações continuam abertas)");
            log.AppendLine("abas: " + Abas());
            RenderizarJanela(w, 1600, 900, "63-aba-volta-ao-modelo.png");
            Clicar(w.AbasLig.Children[1], MouseButton.Left);
            Ok(w.LigacaoAtiva == b && ReferenceEquals(b.Resultado, r0) && b.TemTipo, "voltar à aba 2: mesmo projeto e mesmo resultado (estado preservado)");
            log.AppendLine("abas (duas ligações + modelo): " + Abas());
            RenderizarJanela(w, 1600, 900, "63-aba-duas-ligacoes-e-modelo.png");

            // 4) "Biblioteca" troca dentro da mesma aba; escolher volta ao padrão
            b.MostrarBiblioteca(); EsperarIdle();
            Ok(w.LigacoesAbertas.Count == 2 && ((TextBlock)((StackPanel)((Border)w.AbasLig.Children[1]).Child).Children[0]).Text == "Biblioteca de ligações", "Biblioteca troca na mesma aba (nome atualizado)");
            Esperar(b.Escolher("chapa_extremidade_flexivel", false)); EsperarIdle();
            Ok(b.NomeAba == "Ligação · Chapa de extremidade flexível" && b.Resultado?["verificacoes"] != null, "escolher o padrão de novo recalcula na mesma aba");

            // 5) fechar: botão do meio na aba 1; Esc na aba 2
            Clicar(w.AbasLig.Children[0], MouseButton.Middle);
            Ok(w.LigacoesAbertas.Count == 1 && w.LigacaoAtiva == b && !w.HostLigacoes.Children.Contains(a), "botão do meio fecha a aba 1");
            b.Focus();
            b.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent }); EsperarIdle();
            Ok(w.LigacoesAbertas.Count == 0 && w.LigacaoAtiva == null && w.HostLigacoes.Visibility == Visibility.Collapsed && w.AbasLig.Children.Count == 0, "Esc fecha a última aba e volta ao modelo");
            log.AppendLine("abas: " + Abas());

            File.WriteAllText(Path.Combine(pastaSaida, "63-aba.txt"), log.ToString() + (falhas == 0 ? "TODOS PASSARAM\n" : falhas + " FALHA(S)\n"));
            if (falhas > 0) throw new InvalidOperationException("63) abas de ligação: " + falhas + " falha(s), ver 63-aba.txt");
        }
    }
}
