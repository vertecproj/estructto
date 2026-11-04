// 64) W7-CALC: "calcular" do projetista de ligações (docs/v3/W7-ABA.md, seção Calcular).
// Teste (64-calc.txt): grupo SEM projeto abre com o tipo sugerido pelo motor e verificações; F5 (tecla de verdade na janela) e
// Calcular da faixa com a aba de ligação na frente recalculam a aba (resultado novo, sem "Nada para calcular"); calculadora solta
// só verifica; falha do /dimensionar vira erro na aba (sem esforços de exemplo). Só elas: ESTRUCTTO_CAPTURAS=w7calc.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW7Calc(MainWindow w)
        {
            var log = new StringBuilder(); int falhas = 0;
            void Ok(bool c, string m) { if (!c) falhas++; log.AppendLine((c ? "ok      " : "FALHA   ") + m); }
            void Fim() { File.WriteAllText(Path.Combine(pastaSaida, "64-calc.txt"), log.ToString()); if (falhas > 0) throw new InvalidOperationException("64) calcular do projetista: " + falhas + " falha(s); ver 64-calc.txt"); }
            // F5 de verdade: o PreviewKeyDown da janela (Acoes.cs Tecla) → TeclaLigacao → Calcular
            void F5()
            {
                var src = PresentationSource.FromVisual(w);
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, Key.F5) { RoutedEvent = UIElement.PreviewKeyDownEvent };
                w.RaiseEvent(e); log.AppendLine("F5 tratado pela janela: " + e.Handled);
            }
            void Espera(Func<bool> c) { for (int i = 0; i < 600 && !c(); i++) { EsperarIdle(); System.Threading.Thread.Sleep(50); } }
            foreach (var x in w.LigacoesAbertas.ToList()) x.Close();

            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("galpão não abriu");
            Esperar(w.CalcularEspacial()); EsperarIdle();
            var ger = w.NovoGerenciadorLigacoes(d);
            PrepararDialogoForaDaTela(ger); ger.Show(); EsperarIdle();
            Esperar(ger.Carregar(true)); EsperarIdle();
            var grupos = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
            var g0 = grupos.First(x => S(x, "tipo").StartsWith("viga_pilar"));
            // grupo sem projeto (o motor às vezes já grava solda direta como padrão)
            Esperar(d.Ligacoes("POST", "/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = S(g0, "id"), ["tipo_ligacao"] = null }));
            var g = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray().First(x => S(x, "id") == S(g0, "id"));
            Ok(g["projeto"] == null, "grupo " + S(g, "nome") + " (" + S(g, "tipo") + ") sem projeto gravado");

            // 1) grupo sem projeto: abre com o tipo sugerido e verificações (não para na biblioteca)
            var t1 = w.CriarProjetistaGrupo(d, g, ger); Esperar(t1);
            var j = t1.Result ?? throw new InvalidOperationException("projetista do grupo não abriu");
            w.MostrarLigacao(j); EsperarIdle();
            Ok(j.TemTipo && j.TipoSugerido, "sem projeto: abre no tipo sugerido pelo motor (" + j.TipoAtual + "), não na biblioteca");
            Esperar(j.CalcularAgora()); EsperarIdle();   // em captura o Loaded não calcula; na janela real ele chama CalcularAgora
            int nv = j.Resultado?["verificacoes"]?.AsArray().Count ?? 0;
            Ok(nv > 0 && j.Erro == null && j.ErroGrupo == null, "e já calculado: " + nv + " verificações");
            CapturarDialogo(j, "64-calc-grupo-sem-projeto.png");
            string nomeAba = j.NomeAba;

            // 2) Calcular da faixa/barra rápida (w.Calcular) com grupo do modelo: redimensiona e recarrega os esforços do membro
            w.StatusMsg.Text = "";
            var r1 = j.Resultado;
            w.Calcular();
            Espera(() => !ReferenceEquals(j.Resultado, r1) && !j.Ocupado);
            Ok(!ReferenceEquals(j.Resultado, r1) && j.Erro == null, "Calcular (faixa) com grupo: redimensiona o grupo e recalcula a aba");
            Ok(!w.StatusMsg.Text.Contains("Nada para calcular"), "Calcular (faixa): sem 'Nada para calcular' (status: " + w.StatusMsg.Text + ")");
            Ok(j.TemTipo && j.NomeAba == nomeAba, "a aba continua no mesmo tipo e nome");

            // 3) F5 (tecla de verdade) com a aba ativa; os dados editados na aba não são desfeitos
            var dadosAntes = j.DadosAtuais.ToJsonString();
            var r2 = j.Resultado; F5(); Espera(() => !ReferenceEquals(j.Resultado, r2) && !j.Ocupado);
            Ok(!ReferenceEquals(j.Resultado, r2) && j.Erro == null, "F5 com aba ativa: a ligação do grupo é recalculada");
            Ok(j.DadosAtuais.ToJsonString() == dadosAntes, "F5 não desfaz os dados da aba");
            Ok(!w.StatusMsg.Text.Contains("Nada para calcular"), "F5: sem 'Nada para calcular'");
            CapturarDialogo(j, "64-calc-depois-f5.png");

            // 4) esforços digitados: F5 só verifica de novo
            j.DefinirEsforcos(new JsonObject { ["V"] = 1 }); Esperar(j.CalcularAgora());
            var r3 = j.Resultado; F5(); Espera(() => !ReferenceEquals(j.Resultado, r3) && !j.Ocupado);
            Ok(!ReferenceEquals(j.Resultado, r3), "F5 com esforços digitados: só verifica");

            // 5) calculadora solta (sem grupo): só verifica
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var s = new JanelaLigacaoProjeto(tipos, "chapa_simples");
            w.MostrarLigacao(s); EsperarIdle(); Esperar(s.CalcularAgora());
            var rs = s.Resultado; F5(); Espera(() => !ReferenceEquals(s.Resultado, rs));
            Ok(!ReferenceEquals(s.Resultado, rs) && s.Erro == null, "calculadora solta: F5 só verifica");
            Ok(!w.StatusMsg.Text.Contains("Nada para calcular"), "calculadora solta: sem 'Nada para calcular'");
            CapturarDialogo(s, "64-calc-calculadora-solta.png");
            s.Close();

            // 6) /dimensionar falha: erro na aba, sem tipo nem esforços de exemplo
            var e1 = new JanelaLigacaoProjeto(tipos);
            e1.UsarGrupo(g, null, "motor: falha de teste no /dimensionar");
            w.MostrarLigacao(e1); EsperarIdle();
            Ok(e1.ErroGrupo != null && !e1.TemTipo, "falha do /dimensionar: erro na aba, sem tipo escolhido (sem esforços de exemplo)");
            CapturarDialogo(e1, "64-calc-erro-dimensionar.png");
            e1.Close();
            j.Close(); ger.Close(); EsperarIdle();
            w.FecharEspaciaisSemPerguntar(); EsperarIdle();
            Fim();
        }
    }
}
