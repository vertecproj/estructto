// W12-RIG (docs/v3/W12-RIG-U.md): perfil U nas rígidas e emendas — emenda de viga UPN 200 e UPN 300, emenda de pilar
// UPN 240, emenda de viga U 200x75x4,75 dobrado, flange plate UPN 240 no W250×73 e painel de alma do W250×73 com viga
// UPN 200. Perfil trocado pelo seletor, como o usuário (o campo refaz o Sugerir), nas vistas juntas, elevação, planta e
// 3D. Só elas: ESTRUCTTO_CAPTURAS=w12rig.
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW12Rig(MainWindow w)
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
                log.AppendLine($"   chapas {(g?["chapas"] as JsonArray)?.Count} parafusos {(g?["parafusos"] as JsonArray)?.Count} soldas {(g?["soldas"] as JsonArray)?.Count} membros {(g?["membros"] as JsonArray)?.Count} abertura {g?["abertura_y"]}; g_u {D(i, "g_u")} talas da alma {D(i, "talas_alma")}");
                foreach (var a in (r?["avisos"] as JsonArray) ?? new JsonArray()) log.AppendLine("   aviso: " + a);
            }
            void Vistas(string nome)
            {
                foreach (var (v, sufixo) in new[] { (0, "vistas"), (1, "elevacao"), (2, "planta"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"93-w12rig-{nome}-{sufixo}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            void Caso(string nome, string tipo, string campo, string perfil, JsonObject esf, JsonObject extra = null)
            {
                Esperar(j.Escolher(tipo, false)); EsperarIdle();
                j.DefinirEsforcos(esf); EsperarIdle();
                if (extra != null) { j.DefinirDados(extra.DeepClone().AsObject()); EsperarIdle(); }
                string antes = Status();
                var tp = j.TrocarPerfil(campo, perfil); Esperar(tp); EsperarIdle();
                // espera o Sugerir do seletor (o cálculo automático com os dados antigos pode dar erro antes dele)
                bool Fim(string s) => s != antes && (s.Contains("dimensionado") || s.Contains("= dados atuais") || s.StartsWith("Motor"));
                for (int k = 0; k < 150 && !Fim(Status()); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                bool Calculando() => j.GetType().GetField("calculando", bf)?.GetValue(j) is true || j.GetType().GetField("resultadoVelho", bf)?.GetValue(j) is true;
                void Assentar()   // 5 leituras seguidas sem cálculo (> 250 ms do temporizador e a continuação do Sugerir)
                {
                    for (int k = 0, quietas = 0; k < 300 && quietas < 5; k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); quietas = Calculando() ? 0 : quietas + 1; }
                }
                Assentar();
                Esperar(j.CalcularAgora()); EsperarIdle();
                Assentar();
                log.AppendLine($"[{nome}] {tipo} {perfil}, esforços {esf.ToJsonString()}; seletor: {tp.Result.itens} itens, gravado '{tp.Result.gravado}'");
                Registrar(nome);
                Vistas(nome);
            }
            Caso("emenda-upn200", "emenda_viga", "perfil_viga", "UPN 200", new JsonObject { ["V"] = 30.0, ["M"] = 15.0 });
            Caso("emenda-upn300", "emenda_viga", "perfil_viga", "UPN 300", new JsonObject { ["V"] = 60.0, ["M"] = 40.0 });
            Caso("emenda-pilar-upn240", "emenda_pilar", "perfil_pilar", "UPN 240", new JsonObject { ["N"] = 300.0, ["M"] = 20.0 });
            Caso("emenda-u200-dobrado", "emenda_viga", "perfil_viga", "U 200x75x4,75", new JsonObject { ["V"] = 20.0, ["M"] = 8.0 });
            Caso("flange-upn240-w250", "flange_plate", "perfil_viga", "UPN 240", new JsonObject { ["V"] = 40.0, ["M"] = 25.0 }, new JsonObject { ["perfil_pilar"] = "W250x73" });
            Caso("painel-upn200-w250", "painel_alma_pilar", "perfil_viga", "UPN 200", new JsonObject { ["V"] = 40.0, ["M"] = 30.0 }, new JsonObject { ["perfil_pilar"] = "W250x73" });
            File.WriteAllText(Path.Combine(pastaSaida, "93-w12rig.txt"), log.ToString());
            j.Close();
        }
    }
}
