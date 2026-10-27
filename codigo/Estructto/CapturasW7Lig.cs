// W7-LIG (docs/v3/W7-LIG.md): todos os padrões da biblioteca no padrão de fábrica (exemplo do motor, sem grupo) e as vistas
// grandes da chapa de extremidade flexível e da cantoneira simples em treliça. Só elas: ESTRUCTTO_CAPTURAS=w7lig.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;
using static NovoFtool.UiPlaca;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW7Lig(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos) { Owner = w, Width = 1460, Height = 880 };
            PrepararDialogoForaDaTela(j); j.Show(); EsperarIdle();
            var log = new StringBuilder();
            int i = 0;
            foreach (var t in tipos.AsArray())
            {
                string id = S(t, "tipo");
                Esperar(j.Escolher(id, false)); EsperarIdle();
                var r = j.Resultado;
                log.AppendLine($"{++i:00} {id}: taxa {D(r, "taxa"):0.00} ok {Bool(r, "ok")}");
                log.AppendLine("   dados: " + t["exemplo"]?["dados"]?.ToJsonString());
                log.AppendLine("   esforcos: " + t["exemplo"]?["esforcos"]?.ToJsonString());
                CapturarDialogo(j, $"70-w7lig-{i:00}-{id.Replace('_', '-')}.png");
            }
            foreach (var id in new[] { "chapa_extremidade_flexivel", "cantoneira_simples_trelica" })
            {
                Esperar(j.Escolher(id, false)); EsperarIdle();
                foreach (var (v, nome) in new[] { (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"70-w7lig-{id.Replace('_', '-')}-{nome}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            {   // seletor de cantoneira: só perfis L do catálogo; a troca recalcula com as medidas do catálogo
                Esperar(j.Escolher("cantoneira_simples_trelica", false)); EsperarIdle();
                var tp = j.TrocarPerfil("perfil_cantoneira", "L 4\" x 5/16\""); Esperar(tp); EsperarIdle(); var (n, gravado) = tp.Result;
                // a troca dispara o Sugerir (assíncrono, status "Pré-dimensionado…"); depois recalcula como a tela
                var bfs = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                string Status() => j.GetType().GetField("status", bfs)?.GetValue(j) as string ?? "";
                for (int k = 0; k < 100 && !Status().Contains("dimensionado"); k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                for (int k = 0; k < 20; k++) { EsperarIdle(); System.Threading.Thread.Sleep(100); }
                var ca0 = j.Resultado?["geometria"]?["cantoneiras"]?[0];
                log.AppendLine($"depois do Sugerir automático (sem recalcular na captura): t {D(ca0, "espessura")} gabarito {D(ca0, "talao_z")}; status '{Status()}'");
                Esperar(j.CalcularAgora()); EsperarIdle();
                var ca = j.Resultado?["geometria"]?["cantoneiras"]?[0];
                log.AppendLine($"seletor de cantoneira: {n} perfis L; gravado '{gravado}'; abas {ca?["abas"]?.ToJsonString()} t {D(ca, "espessura")} gabarito {D(ca, "talao_z")}");
                if (n < 50 || n > 200 || gravado != "L 4\" x 5/16\"" || Math.Abs(D(ca, "espessura") - 7.94) > 0.05 || Math.Abs(D(ca, "talao_z") - 65) > 0.5)
                {
                    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    string st = j.GetType().GetField("status", bf)?.GetValue(j) as string, er = j.GetType().GetField("erro", bf)?.GetValue(j) as string;
                    throw new InvalidOperationException($"w7lig: seletor de cantoneira com {n} itens, gravado '{gravado}', t {D(ca, "espessura")}, g {D(ca, "talao_z")}; status '{st}', erro '{er}'");
                }
                CapturarDialogo(j, "70-w7lig-cantoneira-simples-trelica-L4.png");
            }
            File.WriteAllText(Path.Combine(pastaSaida, "70-w7lig-padroes.txt"), log.ToString());
            j.Close();
        }
    }
}
