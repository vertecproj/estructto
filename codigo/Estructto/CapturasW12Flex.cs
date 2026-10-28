// W12-FLEX-U (docs/v3/W12-FLEX-U.md): perfil U como apoio nas ligações flexíveis (costas da alma do pilar/viga U, mesa do
// pilar U) e viga U no assento. Sugerir pela tela, como o usuário; elevação, planta e 3D de cada caso e a recusa das duas
// linhas de parafusos na mesa do U. Só elas: ESTRUCTTO_CAPTURAS=w12flex (com ESTRUCTTO_MOTOR).
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
        static void CapturasW12Flex(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            // (arquivo, tipo, viga, apoio (perfil), apoio (posição; null = viga–viga), V, tipo esperado; null = recusa)
            var casos = new (string arq, string tipo, string viga, string sup, string apoio, double V, string esperado)[]
            {
                ("cd-w310-alma-upn300", "cantoneira_dupla_alma", "W310x38.7", "UPN 300", "alma_pilar", 100, "cantoneira_dupla_alma"),
                ("cs-upn200-mesa-w250", "chapa_simples_alma", "UPN 200", "W250x73", "mesa_pilar", 40, "chapa_simples_alma"),
                ("vv-w310-upn300", "viga_viga_recorte", "W310x38.7", "UPN 300", null, 100, "viga_viga_recorte"),
                ("ap-w200-mesa-upn300", "apoio_cantoneira", "W200x26.6", "UPN 300", "mesa_pilar", 40, "apoio_cantoneira"),
                ("ap-upn200-mesa-w250", "apoio_cantoneira", "UPN 200", "W250x73", "mesa_pilar", 40, "apoio_cantoneira"),
                ("ce-upn200-alma-upn300", "chapa_extremidade_flexivel", "UPN 200", "UPN 300", "alma_pilar", 40, "chapa_extremidade_flexivel"),
                ("cd-w200-mesa-upn300-recusa", "cantoneira_dupla_alma", "W200x26.6", "UPN 300", "mesa_pilar", 40, null),
            };
            foreach (var c in casos)
            {
                Esperar(j.Escolher(c.tipo, false)); EsperarIdle();
                var d = new JsonObject { ["perfil_viga"] = c.viga, ["perfil_suporte"] = c.sup };
                if (c.apoio != null) d["apoio"] = c.apoio;
                j.DefinirDados(d); j.DefinirEsforcos(new JsonObject { ["V"] = c.V, ["N"] = 0 });
                Esperar(j.Sugerir()); EsperarIdle();
                var r = j.Resultado; var geo = r?["geometria"];
                var pior = (r?["verificacoes"] as JsonArray)?.OrderByDescending(v => D(v, "taxa")).Take(3).Select(v => $"{S(v, "nome")} {D(v, "taxa"):0.000}");
                var mu = Lista(geo?["membros"]).FirstOrDefault(m => S(m, "barra") == "suporte");
                log.AppendLine($"{c.arq}: tipo {j.TipoAtual}, ok {Bool(r, "ok")}, taxa {D(r, "taxa"):0.000}, apoio {S(mu, "perfil")} forma '{S(mu, "forma")}', erro '{j.Erro}', status '{j.Status}'");
                log.AppendLine("   piores: " + string.Join(" | ", pior ?? Enumerable.Empty<string>()));
                foreach (var p in Lista(geo?["parafusos"])) log.AppendLine($"   parafuso em {p?["origem"]?.ToJsonString()} eixo {p?["eixo"]?.ToJsonString()}");
                foreach (var a in (r?["avisos"] as JsonArray) ?? new JsonArray()) log.AppendLine("   aviso: " + a);
                if (c.esperado == null)
                {
                    string msg = (j.Erro ?? "") + " " + (j.Status ?? "");
                    if (!msg.Contains("não cabem na mesa do U") && !msg.Contains("fora da mesa do")) throw new InvalidOperationException("w12flex " + c.arq + ": sem o motivo da recusa: " + msg);
                    CapturarDialogo(j, "93-w12flex-" + c.arq + ".png");
                    continue;
                }
                if (j.Erro != null) throw new InvalidOperationException("w12flex " + c.arq + ": motor recusou: " + j.Erro);
                if (j.TipoAtual != c.esperado) throw new InvalidOperationException("w12flex " + c.arq + ": tipo " + j.TipoAtual + ", esperado " + c.esperado);
                if (D(r, "taxa") > 0.90 + 1e-9) throw new InvalidOperationException("w12flex " + c.arq + ": taxa " + D(r, "taxa") + " > 0,90");
                foreach (var (v, sufixo) in new[] { (1, "elevacao"), (2, "planta"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"93-w12flex-{c.arq}-{sufixo}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            j.Close();
            File.WriteAllText(Path.Combine(pastaSaida, "93-w12flex.txt"), log.ToString());
        }
    }
}
