// W12-PARAFUSOS (docs/v3/W12-PARAFUSOS.md): seletor de diâmetro pela especificação (A325 em polegada, ISO 8.8 métrico) e a
// lista de compra dos parafusos com o comprimento comercial do catálogo. Só elas: ESTRUCTTO_CAPTURAS=w12paraf.
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW12Paraf(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            void Registrar(string nome)
            {
                var r = j.Resultado;
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} erro '{j.Erro}' d {S(j.DadosAtuais, "d_mm")}{S(j.DadosAtuais, "d")} tipo {S(j.DadosAtuais, "tipo_parafuso")}");
                foreach (var p in (r?["lista_parafusos"] as JsonArray) ?? new JsonArray()) log.AppendLine($"   {S(p, "quantidade")} × {S(p, "descricao")} ({S(p, "comprimento_mm")} mm)");
                foreach (var a in (r?["avisos"] as JsonArray) ?? new JsonArray()) log.AppendLine("   aviso: " + a);
            }
            // passos rolados até PARAFUSOS (seletor de diâmetro) e verificações até o fim (lista de compra)
            void Rolar()
            {
                j.UpdateLayout();
                foreach (var t in Filhos<System.Windows.Controls.TextBlock>(j).Where(t => (t.Text ?? "").ToUpperInvariant().StartsWith("PARAFUSOS")).ToList())
                {
                    var sv = Filhos<System.Windows.Controls.ScrollViewer>(j).Where(s => s.IsAncestorOf(t)).LastOrDefault();
                    if (sv == null) continue;
                    if (sv == j.GetType().GetField("rolagemPassos", bf)?.GetValue(j)) sv.ScrollToVerticalOffset(sv.VerticalOffset + t.TransformToAncestor(sv).Transform(new System.Windows.Point()).Y - 8);
                    else sv.ScrollToEnd();
                    sv.UpdateLayout();
                }
                EsperarIdle();
            }
            foreach (var tipo in new[] { "chapa_topo_estendida", "chapa_simples_alma" })
            {
                Esperar(j.Escolher(tipo, true)); EsperarIdle();
                j.MostrarVista(4); EsperarIdle();
                Registrar(tipo + " A325");
                Rolar(); CapturarDialogo(j, $"93-w12paraf-{tipo}-a325.png");
                j.DefinirDados(new JsonObject { ["tipo_parafuso"] = "ISO 8.8" });
                typeof(JanelaLigacaoProjeto).GetMethod("TrocarFamilia", bf).Invoke(j, new object[] { "tipo_parafuso" });
                j.DefinirDados(new JsonObject());
                Esperar(j.CalcularAgora()); EsperarIdle();
                Registrar(tipo + " ISO 8.8");
                Rolar(); CapturarDialogo(j, $"93-w12paraf-{tipo}-iso88.png");
            }
            File.WriteAllText(Path.Combine(pastaSaida, "93-w12paraf.txt"), log.ToString());
            j.Close();
        }
    }
}
