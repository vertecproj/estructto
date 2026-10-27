// W11-CHAPA-TOPO-U (docs/v3/W11-CHAPA-TOPO-U.md): chapa de topo ajustada com viga U — UPN 200 na mesa do W250×73, UPN 100
// nas costas da alma do UPN 100 (mesmo tamanho: chapa sem sobra) e o caso antigo do usuário, UPN 50 no UPN 120 (2 parafusos
// não cabem: motivo e diâmetro máximo). Perfil trocado pelo seletor, como o usuário (o campo refaz o Sugerir), nas vistas
// juntas, elevação, planta, lateral e 3D. Só elas: ESTRUCTTO_CAPTURAS=w11u.
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW11U(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string Status() => j.GetType().GetField("status", bf)?.GetValue(j) as string ?? "";
            void Registrar(string nome)
            {
                var r = j.Resultado;
                var pior = (r?["verificacoes"] as JsonArray)?.OrderByDescending(v => D(v, "taxa")).Take(4).Select(v => $"{S(v, "nome")} {D(v, "taxa"):0.000}");
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} erro '{j.Erro}' status '{Status()}'");
                log.AppendLine("   piores: " + string.Join(" | ", pior ?? Enumerable.Empty<string>()));
                var g = r?["geometria"]; var i = r?["intermediarios"];
                var ch = (g?["chapas"] as JsonArray)?.FirstOrDefault()?["contorno"] as JsonArray;
                string dim = ch == null ? "-" : $"{ch.Max(p => D(p?[0])) - ch.Min(p => D(p?[0])):0.#} × {ch.Max(p => D(p?[1])) - ch.Min(p => D(p?[1])):0.#} mm";
                log.AppendLine($"   chapa {dim} (sobra {D(i, "sobra")}); parafusos {(g?["parafusos"] as JsonArray)?.Count} em y {D(i, "y_parafusos"):0.0}, z ±{D(i, "zb"):0.0}; soldas {(g?["soldas"] as JsonArray)?.Count}; membros {(g?["membros"] as JsonArray)?.Count}");
                log.AppendLine($"   dados: d {S(j.DadosAtuais, "d_mm")} tp {S(j.DadosAtuais, "tp")} pf {S(j.DadosAtuais, "pf")} c_alma {S(j.DadosAtuais, "c_alma")} b_alma {S(j.DadosAtuais, "b_alma")} b_mesa {S(j.DadosAtuais, "b_mesa")}");
                foreach (var a in (r?["avisos"] as JsonArray) ?? new JsonArray()) log.AppendLine("   aviso: " + a);
            }
            void Vistas(string nome)
            {
                foreach (var (v, sufixo) in new[] { (0, "vistas"), (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"92-w11u-{nome}-{sufixo}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            void Caso(string nome, string viga, string pilar, JsonObject esf, JsonObject extra = null)
            {
                Esperar(j.Escolher("chapa_topo_ajustada", false)); EsperarIdle();
                j.DefinirEsforcos(esf); EsperarIdle();
                var d = new JsonObject { ["perfil_pilar"] = pilar };
                foreach (var kv in extra ?? new JsonObject()) d[kv.Key] = kv.Value?.DeepClone();
                j.DefinirDados(d); EsperarIdle();
                string antes = Status();
                var tp = j.TrocarPerfil("perfil_viga", viga); Esperar(tp); EsperarIdle();
                // espera o Sugerir do seletor (o cálculo automático com os dados antigos pode dar erro antes dele)
                bool Fim(string s) => s != antes && (s.Contains("dimensionado") || s.Contains("= dados atuais") || s.StartsWith("Motor"));
                for (int k = 0; k < 150 && !Fim(Status()); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                // o cálculo do temporizador (troca do perfil) pode chegar depois e descartar o nosso (seq): espera parar e refaz
                bool Calculando() => j.GetType().GetField("calculando", bf)?.GetValue(j) is true || j.GetType().GetField("resultadoVelho", bf)?.GetValue(j) is true;
                void Assentar()   // 5 leituras seguidas sem cálculo (> 250 ms do temporizador e a continuação do Sugerir)
                {
                    for (int k = 0, quietas = 0; k < 300 && quietas < 5; k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); quietas = Calculando() ? 0 : quietas + 1; }
                }
                Assentar();
                Esperar(j.CalcularAgora()); EsperarIdle();
                Assentar();
                log.AppendLine($"[{nome}] {viga} em {pilar}, esforços {esf.ToJsonString()}; seletor: {tp.Result.itens} itens, gravado '{tp.Result.gravado}'");
                Registrar(nome);
                Vistas(nome);
            }
            Caso("upn200-w250", "UPN 200", "W250x73", new JsonObject { ["V"] = 40.0, ["M"] = 8.0 });
            Caso("upn100-upn100", "UPN 100", "UPN 100", new JsonObject { ["V"] = 10.0, ["M"] = 1.5 }, new JsonObject { ["terca_ou_travessa"] = true });
            Caso("upn50-upn120", "UPN 50", "UPN 120", new JsonObject { ["V"] = 5.0, ["M"] = 0.5 });
            File.WriteAllText(Path.Combine(pastaSaida, "92-w11u.txt"), log.ToString());
            j.Close();
        }
    }
}
