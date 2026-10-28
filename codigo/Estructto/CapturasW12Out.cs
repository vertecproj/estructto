// W12-OUT (docs/v3/W12-OUT-U.md): perfis U nas outras ligações — gusset de contraventamento com a diagonal UPN 160 e com
// o U dobrado 150x50x3,00 (seletor, como o usuário: o campo refaz o Sugerir), perfil UPN 100 soldado em tubo circular e a
// placa de base com pilar UPN 200 (exemplos/base-pilar-u.json). Só elas: ESTRUCTTO_CAPTURAS=w12out.
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
        static void CapturasW12Out(MainWindow w, string modelo)
        {
            var log = new StringBuilder();
            void Marca(string m) { log.AppendLine("# " + m); File.WriteAllText(Path.Combine(pastaSaida, "93-w12out.txt"), log.ToString(), new UTF8Encoding(false)); }
            try
            {
                // 1) placa de base (antes da janela de ligação: AbrirPlacaEsp precisa da aba do modelo à frente) com pilar UPN 200 (placa centrada na caixa do perfil; N atua no centroide)
                string arq = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelo), "base-pilar-u.json"));
                Marca("abrindo " + arq);
                w.FecharEspaciaisSemPerguntar();
                Esperar(w.AbrirArquivo(arq));
                Marca("aberto");
                Esperar(w.CalcularEspacial());
                Marca("calculado");
                w.placaAberta = null;
                Esperar(w.AbrirPlacaEsp("N0-0-0"));
                Marca("placa aberta? " + (w.placaAberta != null));
                var jp = w.placaAberta ?? throw new InvalidOperationException("w12out: a placa do nó N0-0-0 não abriu");
                PrepararDialogoForaDaTela(jp); Marca("preparada");
                jp.Show(); EsperarIdle(); Marca("mostrada");
                foreach (var (aba, nome) in new[] { (0, "planta"), (2, "forcas"), (4, "calculo"), (1, "3d") })
                {
                    RenderizarJanela(jp, 1600, 900, "93-w12out-placa-u-" + nome + ".png", () => { jp.MostrarLegado(false); jp.MostrarAba(aba); });
                    Marca("aba " + nome);
                }
                var rm = jp.ResultadoMotor;
                log.AppendLine("placa UPN 200: " + (rm.Valido ? $"governa {rm.Comb} eixo {rm.Eixo} N {rm.N:0.0} V {rm.V:0.0} M {rm.M:0.0} · taxas fixação {rm.Fix.Taxa:0.000} chapa {rm.Chapa.Taxa:0.000} solda {rm.Solda.Taxa:0.000} ok {rm.Ok} · {jp.Mensagem}" : "MOTOR: " + rm.Erro));
                jp.Close(); EsperarIdle();
                var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
                var j = new JanelaLigacaoProjeto(tipos);
                w.MostrarLigacao(j); EsperarIdle();
                var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                string Status() => j.GetType().GetField("status", bf)?.GetValue(j) as string ?? "";
                void Registrar(string nome)
                {
                    var r = j.Resultado;
                    var pior = (r?["verificacoes"] as JsonArray)?.OrderByDescending(v => D(v, "taxa")).Take(4).Select(v => $"{S(v, "nome")} {D(v, "taxa"):0.000}");
                    log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} erro '{j.Erro}' status '{Status()}'");
                    log.AppendLine("   piores: " + string.Join(" | ", pior ?? Enumerable.Empty<string>()));
                    var g = r?["geometria"];
                    log.AppendLine($"   peças: chapas {(g?["chapas"] as JsonArray)?.Count} perfis_barra {(g?["perfis_barra"] as JsonArray)?.Count} membros {(g?["membros"] as JsonArray)?.Count} parafusos {(g?["parafusos"] as JsonArray)?.Count} soldas {(g?["soldas"] as JsonArray)?.Count}");
                    foreach (var a in (r?["avisos"] as JsonArray) ?? new JsonArray()) log.AppendLine("   aviso: " + a);
                }
                void Vistas(string nome)
                {
                    foreach (var (v, sufixo) in new[] { (0, "vistas"), (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                    {
                        j.MostrarVista(v); EsperarIdle();
                        CapturarDialogo(j, $"93-w12out-{nome}-{sufixo}.png");
                    }
                    j.MostrarVista(0); EsperarIdle();
                }
                void Trocar(string campo, string perfil)   // como o usuário: seletor → Sugerir automático → recalcula
                {
                    var tp = j.TrocarPerfil(campo, perfil); Esperar(tp); EsperarIdle();
                    for (int k = 0; k < 150 && !Status().Contains("dimensionado") && !Status().Contains("= dados atuais"); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                    for (int k = 0; k < 20; k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                    Esperar(j.CalcularAgora()); EsperarIdle();
                    log.AppendLine($"   seletor {campo} = {perfil}: gravado '{tp.Result.gravado}'; status '{Status()}'");
                }
                // 1) gusset com a diagonal em U
                Esperar(j.Escolher("gusset_contraventamento", false)); EsperarIdle();
                j.DefinirEsforcos(new JsonObject { ["N"] = 150.0 }); EsperarIdle();
                Trocar("perfil_diagonal", "UPN 160");
                Registrar("gusset UPN 160, N = 150");
                Vistas("gusset-upn160");
                j.DefinirEsforcos(new JsonObject { ["N"] = 60.0 }); EsperarIdle();
                Trocar("perfil_diagonal", "U 150x50x3,00");
                Registrar("gusset U dobrado 150x50x3,00, N = 60");
                Vistas("gusset-u-dobrado");
                // 2) UPN 100 soldado num tubo circular Ø101,6 × 5
                Esperar(j.Escolher("perfil_tubo_soldado", false)); EsperarIdle();
                j.DefinirEsforcos(new JsonObject { ["N"] = 0.0, ["V"] = 6.0, ["M"] = 1.0 }); EsperarIdle();
                j.DefinirDados(new JsonObject { ["forma_ramal"] = "U", ["h_ramal"] = 100, ["b_ramal"] = 50, ["tw_ramal"] = 6, ["tf_ramal"] = 8.5,
                    ["forma_receptor"] = "tubo_circular", ["D_receptor"] = 101.6, ["t_receptor"] = 5.0, ["N_receptor"] = -60.0, ["b_solda"] = 4 });
                Esperar(j.CalcularAgora()); EsperarIdle();
                Registrar("UPN 100 em tubo Ø101,6 × 5");
                Vistas("perfil-tubo-u");
                File.WriteAllText(Path.Combine(pastaSaida, "93-w12out.txt"), log.ToString(), new UTF8Encoding(false));
                j.Close(); EsperarIdle();

            }
            finally { File.WriteAllText(Path.Combine(pastaSaida, "93-w12out.txt"), log.ToString(), new UTF8Encoding(false)); }
        }
    }
}
