// W9-FLANGE-PERFIL (docs/v3/W9-FLANGE-PERFIL.md): emenda flangeada de perfil aberto — viga W (padrão de fábrica), pilar H
// (padrão, em pé) e viga UPN (pelo seletor, como o usuário) nas vistas juntas, elevação, planta, lateral e 3D, mais a
// busca "flange" na biblioteca. Só elas: ESTRUCTTO_CAPTURAS=w9fi.
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW9Fi(MainWindow w)
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
                var pior = (r?["verificacoes"] as JsonArray)?.OrderByDescending(v => D(v, "taxa")).Take(3).Select(v => $"{S(v, "nome")} {D(v, "taxa"):0.000}");
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} erro '{j.Erro}'");
                log.AppendLine("   piores: " + string.Join(" | ", pior ?? Enumerable.Empty<string>()));
                var g = r?["geometria"];
                log.AppendLine($"   peças: chapas {(g?["chapas"] as JsonArray)?.Count} parafusos {(g?["parafusos"] as JsonArray)?.Count} soldas {(g?["soldas"] as JsonArray)?.Count} membros {(g?["membros"] as JsonArray)?.Count} vista {g?["vista"]?.ToJsonString() ?? "-"}");
            }
            void Vistas(string nome)
            {
                foreach (var (v, sufixo) in new[] { (0, "vistas"), (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"90-w9fi-{nome}-{sufixo}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            j.Buscar("flange"); EsperarIdle();
            CapturarDialogo(j, "90-w9fi-biblioteca-flange.png");
            j.Buscar("");
            foreach (var (id, nome) in new[] { ("emenda_flangeada_viga", "viga-w"), ("emenda_flangeada_pilar", "pilar-h") })
            {
                Esperar(j.Escolher(id, false)); EsperarIdle();
                Registrar(nome);
                Vistas(nome);
            }
            {   // viga UPN 200: troca no seletor → Sugerir automático (campo com resugerir), M = 15 kN·m e V = 40 kN
                Esperar(j.Escolher("emenda_flangeada_viga", false)); EsperarIdle();
                j.DefinirEsforcos(new JsonObject { ["M"] = 15.0, ["V"] = 40.0 }); EsperarIdle();
                var tp = j.TrocarPerfil("perfil_viga", "UPN 200"); Esperar(tp); EsperarIdle();
                for (int k = 0; k < 100 && !Status().Contains("dimensionado") && !Status().Contains("= dados atuais"); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                Esperar(j.CalcularAgora()); EsperarIdle();
                log.AppendLine($"   seletor perfil_viga: gravado '{tp.Result.gravado}'; status '{Status()}'");
                Registrar("viga-upn");
                Vistas("viga-upn");
            }
            File.WriteAllText(Path.Combine(pastaSaida, "90-w9fi.txt"), log.ToString());
            j.Close();
        }
    }
}
