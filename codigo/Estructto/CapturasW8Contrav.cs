// W8-CONTRAV (docs/v3/W8-CONTRAV.md): gusset de contraventamento, nó de treliça com gusset e tirante de barra redonda no
// padrão de fábrica (vistas + elevação, planta, lateral e 3D grandes), variantes da diagonal (2L, U, tubo pelo seletor) e o
// garfo do tirante. Só elas: ESTRUCTTO_CAPTURAS=w8contrav.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW8Contrav(MainWindow w)
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
                log.AppendLine($"   peças: chapas {(g?["chapas"] as JsonArray)?.Count} perfis_barra {(g?["perfis_barra"] as JsonArray)?.Count} tubos {(g?["tubos"] as JsonArray)?.Count} parafusos {(g?["parafusos"] as JsonArray)?.Count} soldas {(g?["soldas"] as JsonArray)?.Count} linhas {(g?["linhas"] as JsonArray)?.Count}");
            }
            void Vistas(string id, string nome)
            {
                foreach (var (v, sufixo) in new[] { (0, "vistas"), (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    if (v == 4)
                    {
                        System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> Filhos(System.Windows.DependencyObject o) { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++) { var f = System.Windows.Media.VisualTreeHelper.GetChild(o, i); yield return f; foreach (var x in Filhos(f)) yield return x; } }
                        var d3 = Filhos(j).OfType<DesenhoLigacao>().FirstOrDefault(x => x.Vista == VistaLig.Iso);
                        if (d3 != null)
                        {
                            var b = System.Windows.Media.Media3D.Rect3D.Empty; foreach (var p in d3.Pecas3D) b.Union(p.M.Bounds);
                            var fo = d3.Geo?.Foco().ToList();
                            log.AppendLine($"   3D {nome}: {d3.Pecas3D.Count} peças, caixa {b}; foco x {fo?.Min(q => q.X):0}..{fo?.Max(q => q.X):0} y {fo?.Min(q => q.Y):0}..{fo?.Max(q => q.Y):0} z {fo?.Min(q => q.Z):0}..{fo?.Max(q => q.Z):0}; tela {d3.ActualWidth:0}×{d3.ActualHeight:0}");
                        }
                    }
                    CapturarDialogo(j, $"80-w8contrav-{nome}-{sufixo}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            void Trocar(string campo, string perfil)   // como o usuário: seletor → Sugerir automático → recalcula
            {
                var tp = j.TrocarPerfil(campo, perfil); Esperar(tp); EsperarIdle();
                for (int k = 0; k < 100 && !Status().Contains("dimensionado") && !Status().Contains("= dados atuais"); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                Esperar(j.CalcularAgora()); EsperarIdle();
                log.AppendLine($"   seletor {campo}: gravado '{tp.Result.gravado}'; status '{Status()}'");
            }
            foreach (var (id, nome) in new[] { ("gusset_contraventamento", "gusset"), ("no_trelica_gusset", "no-trelica"), ("tirante_barra_redonda", "tirante") })
            {
                Esperar(j.Escolher(id, false)); EsperarIdle();
                var t = tipos.AsArray().First(x => S(x, "tipo") == id);
                log.AppendLine($"== {id} (padrão de fábrica)");
                log.AppendLine("   dados: " + t["exemplo"]?["dados"]?.ToJsonString());
                log.AppendLine("   esforcos: " + t["exemplo"]?["esforcos"]?.ToJsonString());
                Registrar(id);
                if (!Bool(j.Resultado, "ok") || D(j.Resultado, "taxa") > 0.90 + 1e-9) throw new InvalidOperationException($"w8contrav: {id} no padrão de fábrica com taxa {D(j.Resultado, "taxa"):0.000} (ok {Bool(j.Resultado, "ok")})");
                Vistas(id, nome);
            }
            // variantes da diagonal do gusset
            Esperar(j.Escolher("gusset_contraventamento", false)); EsperarIdle();
            j.DefinirDados(new JsonObject { ["arranjo"] = "dupla" }); Esperar(j.CalcularAgora()); EsperarIdle();
            Registrar("gusset 2L (arranjo dupla)"); CapturarDialogo(j, "80-w8contrav-gusset-2L-vistas.png");
            Trocar("perfil_diagonal", "U 6\" x 12,2");
            Registrar("gusset U 6\" (seletor)"); CapturarDialogo(j, "80-w8contrav-gusset-U-vistas.png");
            Trocar("perfil_diagonal", "TC 88,9x5");
            Registrar("gusset tubo (seletor)"); CapturarDialogo(j, "80-w8contrav-gusset-tubo-vistas.png");
            if (S(j.Resultado?["geometria"]?["tubos"]?[0], "nome") == "" || !Bool(j.Resultado, "ok"))
                throw new InvalidOperationException($"w8contrav: tubo pelo seletor sem tubo desenhado ou reprovado (erro '{j.Erro}', status '{Status()}')");
            // garfo do tirante
            Esperar(j.Escolher("tirante_barra_redonda", false)); EsperarIdle();
            j.DefinirDados(new JsonObject { ["extremidade"] = "garfo_pino", ["t_chapa"] = 16.0, ["t_orelha"] = 12.5 }); Esperar(j.CalcularAgora()); EsperarIdle();
            Registrar("tirante com garfo"); CapturarDialogo(j, "80-w8contrav-tirante-garfo-vistas.png");
            File.WriteAllText(Path.Combine(pastaSaida, "80-w8contrav.txt"), log.ToString());
            j.Close();
        }
    }
}
