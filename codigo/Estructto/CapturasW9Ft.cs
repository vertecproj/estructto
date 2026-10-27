// W9-FT (docs/v3/W9-FLANGE-TUBO.md): emenda flangeada de tubo redondo e de tubo quadrado/retangular (metalon), em pilar
// e em viga, no padrão (vistas juntas), nas quatro vistas grandes e no 3D; a busca "flange" na biblioteca. Só elas:
// ESTRUCTTO_CAPTURAS=w9ft. Falha se um padrão de fábrica passar de 0,90 ou reprovar.
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
        static void CapturasW9Ft(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var achados = tipos.AsArray().Where(t => (S(t, "nome") + " " + S(t, "descricao")).ToLowerInvariant().Contains("flange")).Select(t => S(t, "tipo")).ToList();
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            log.AppendLine("busca 'flange': " + string.Join(", ", achados));
            void Vistas(string nome, bool padrao)
            {
                var r = j.Resultado;
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} erro '{j.Erro}'");
                foreach (var v in Lista(r?["verificacoes"])) log.AppendLine($"   {D(v, "taxa"):0.000}  {S(v, "nome")}  [{S(v, "item")}]");
                foreach (var a in Lista(r?["avisos"])) log.AppendLine("   aviso: " + (string)a);
                if (padrao && (!Bool(r, "ok") || D(r, "taxa") > 0.90 + 1e-9))
                    throw new InvalidOperationException($"W9-FT: padrão de fábrica {nome} com taxa {D(r, "taxa"):0.000} (ok {Bool(r, "ok")})");
                CapturarDialogo(j, $"90-w9ft-{nome}.png");
                foreach (var (v, vista) in new[] { (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"90-w9ft-{nome}-{vista}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            foreach (var (tipo, curto, outro) in new[] { ("emenda_flangeada_tubo_circular", "redondo", "viga"), ("emenda_flangeada_tubo_retangular", "metalon", "pilar") })
            {
                if (!achados.Contains(tipo)) throw new InvalidOperationException("W9-FT: '" + tipo + "' não aparece buscando 'flange'");
                Esperar(j.Escolher(tipo, false)); EsperarIdle();
                log.AppendLine("   dados: " + tipos.AsArray().FirstOrDefault(t => S(t, "tipo") == tipo)?["exemplo"]?["dados"]?.ToJsonString());
                string uso = (string)tipos.AsArray().First(t => S(t, "tipo") == tipo)["exemplo"]["dados"]["uso"];
                Vistas($"{curto}-{uso}", true);
                j.DefinirDados(new JsonObject { ["uso"] = outro });
                if (tipo.EndsWith("retangular")) j.DefinirDados(new JsonObject { ["lados"] = "4 lados", ["n"] = 1 });
                Esperar(j.CalcularAgora()); EsperarIdle();
                Vistas($"{curto}-{outro}", false);
            }
            File.WriteAllText(Path.Combine(pastaSaida, "90-w9ft.txt"), log.ToString());
            j.Close();
        }
    }
}
