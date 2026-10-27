// W8-RIG (docs/v3/W8-RIG.md): padrões de fábrica das rígidas e emendas revistos — painel de alma, emendas de viga e de
// pilar, chapas de topo ajustada e estendida — no padrão (vistas juntas) e nas quatro vistas grandes, mais o painel com
// enrijecedores e chapas de reforço. Só elas: ESTRUCTTO_CAPTURAS=w8rig.
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
        static void CapturasW8Rig(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            void Registrar(string id)
            {
                var r = j.Resultado;
                var pior = r?["verificacoes"]?.AsArray().OrderByDescending(v => D(v, "taxa")).FirstOrDefault();
                log.AppendLine($"{id}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} governa '{S(pior, "nome")}'");
                log.AppendLine("   dados: " + tipos.AsArray().FirstOrDefault(t => S(t, "tipo") == id)?["exemplo"]?["dados"]?.ToJsonString());
            }
            foreach (var id in new[] { "painel_alma_pilar", "emenda_viga", "emenda_pilar", "chapa_topo_ajustada", "chapa_topo_estendida" })
            {
                Esperar(j.Escolher(id, false)); EsperarIdle();
                Registrar(id);
                string n = id.Replace('_', '-');
                CapturarDialogo(j, $"80-w8rig-{n}.png");
                foreach (var (v, nome) in new[] { (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"80-w8rig-{n}-{nome}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            {   // painel com enrijecedores de continuidade e chapas de reforço (M = 180 kN·m pede os dois)
                Esperar(j.Escolher("painel_alma_pilar", false)); EsperarIdle();
                j.DefinirDados(new JsonObject { ["t_enr_pilar"] = 12.5, ["t_reforco"] = 9.5 });
                j.DefinirEsforcos(new JsonObject { ["M"] = 180.0 });
                Esperar(j.CalcularAgora()); EsperarIdle();
                Registrar("painel_alma_pilar (enrijecedores 12,5 + reforço 9,5, M = 180)");
                CapturarDialogo(j, "80-w8rig-painel-alma-pilar-enrijecido.png");
                j.MostrarVista(4); EsperarIdle();
                CapturarDialogo(j, "80-w8rig-painel-alma-pilar-enrijecido-3d.png");
                j.MostrarVista(0); EsperarIdle();
                // janela alta: o campo "Mesa da viga soldada" (sim/não) aparece no passo 2
                w.MostrarLigacao(j); EsperarIdle();
                RenderizarJanela(w, 1600, 1300, "80-w8rig-painel-alma-pilar-campos.png");
            }
            File.WriteAllText(Path.Combine(pastaSaida, "80-w8rig.txt"), log.ToString());
            j.Close();
        }
    }
}
