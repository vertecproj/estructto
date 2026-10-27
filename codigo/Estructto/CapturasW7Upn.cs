// Capturas W7-UPN (docs/v3/W7-UPN.md): viga e apoio U (UPN, U laminado, U e Ue dobrados) no projetista, em cada tipo
// flexível que aceita U, e a recusa da rígida com o motivo. Só elas: ESTRUCTTO_CAPTURAS=upn (com ESTRUCTTO_MOTOR).
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
        static void CapturasW7Upn(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();   // W7: aba da janela principal
            var log = new StringBuilder();
            // (arquivo, tipo, viga, apoio, extra, tipo esperado depois do sugerir, papel do U)
            var casos = new (string arq, string tipo, string viga, string apoio, JsonObject extra, string esperado, string papelU)[]
            {
                ("solda-direta-upn200", "solda_direta_alma", "UPN 200", "W250x73", null, "solda_direta_alma", "viga"),   // o caso da imagem do usuário
                ("chapa-simples-upn300", "chapa_simples_alma", "UPN 300", "W250x73", null, "chapa_simples_alma", "viga"),
                ("chapa-simples-upn300-abertura-neg", "chapa_simples_alma", "UPN 300", "W250x73", new JsonObject { ["abertura_y"] = -1 }, "chapa_simples_alma", "viga"),
                ("cantoneira-dupla-upn300", "cantoneira_dupla_alma", "UPN 300", "W250x73", null, "cantoneira_dupla_alma", "viga"),
                ("chapa-extremidade-upn200", "chapa_extremidade_flexivel", "UPN 200", "W250x73", null, "chapa_extremidade_flexivel", "viga"),
                ("chapa-extremidade-u200-dobrado", "chapa_extremidade_flexivel", "U 200x75x3,00", "W250x73", null, "chapa_extremidade_flexivel", "viga"),
                ("viga-viga-upn300", "viga_viga_recorte", "UPN 300", "W460x52", null, "viga_viga_recorte", "viga"),
                ("apoio-cantoneira-u10", "apoio_cantoneira", "U 10\" x 22,7", "W250x73", null, "apoio_cantoneira", "viga"),
                ("solda-direta-ue200", "solda_direta_alma", "Ue 200x75x25x3,00", "W250x73", null, "solda_direta_alma", "viga"),
                ("viga-viga-apoio-upn300", "viga_viga_recorte", "W200x15", "UPN 300", null, "viga_viga_recorte", "suporte"),
                ("chapa-simples-upn200-troca", "chapa_simples_alma", "UPN 200", "W250x73", null, "solda_direta_alma", "viga"),   // não passa: o motor troca e diz por quê
            };
            foreach (var c in casos)
            {
                Esperar(j.Escolher(c.tipo, false)); EsperarIdle();
                var d = new JsonObject { ["perfil_viga"] = c.viga, ["perfil_suporte"] = c.apoio };
                if (c.extra != null) foreach (var kv in c.extra) d[kv.Key] = kv.Value?.DeepClone();
                j.DefinirDados(d); j.DefinirEsforcos(new JsonObject { ["V"] = 50, ["N"] = 0 });
                Esperar(j.Sugerir()); EsperarIdle();
                if (c.extra != null) { j.DefinirDados((JsonObject)c.extra.DeepClone()); Esperar(j.CalcularAgora()); EsperarIdle(); }
                var geo = j.Resultado?["geometria"];
                var mu = Lista(geo?["membros"]).FirstOrDefault(m => S(m, "barra") == c.papelU && S(m, "forma") == "U");
                var g = GeoLig.De(geo);
                int nU = g.Pecas.Count(p => p.Camada == 0 && p.Grupo == (c.papelU == "viga" ? "viga" : "suporte"));
                log.AppendLine($"{c.arq}: tipo {j.TipoAtual}, ok {Bool(j.Resultado, "ok")}, taxa {D(j.Resultado, "taxa"):0.00}, U em membros {mu != null}, peças do U {nU}, erro '{j.Erro}', status '{j.Status}'");
                if (j.Erro != null) throw new InvalidOperationException("w7-upn " + c.arq + ": motor recusou: " + j.Erro);
                if (j.TipoAtual != c.esperado) throw new InvalidOperationException("w7-upn " + c.arq + ": tipo " + j.TipoAtual + ", esperado " + c.esperado);
                if (mu == null || nU != 3) throw new InvalidOperationException("w7-upn " + c.arq + ": U não desenhado (membro " + (mu != null) + ", peças " + nU + ")");
                CapturarDialogo(j, "70-upn-" + c.arq + ".png");
                if (c.arq is "solda-direta-upn200" or "chapa-simples-upn300" or "chapa-extremidade-upn200")
                {
                    j.MostrarVista(4); EsperarIdle(); CapturarDialogo(j, "70-upn-" + c.arq + "-3d.png");
                    j.MostrarVista(2); EsperarIdle(); CapturarDialogo(j, "70-upn-" + c.arq + "-planta.png"); j.MostrarVista(0);
                }
            }
            // rígida com U: o motor recusa (G5) e a tela diz por quê, sem desenhar a ligação em silêncio
            Esperar(j.Escolher("chapa_topo_estendida", false)); EsperarIdle();
            j.DefinirDados(new JsonObject { ["perfil_viga"] = "UPN 200" }); Esperar(j.CalcularAgora()); EsperarIdle();
            log.AppendLine("rigida-upn200: erro '" + j.Erro + "'");
            if (j.Erro == null || !j.Erro.Contains("perfil U")) throw new InvalidOperationException("w7-upn: rígida com UPN sem o motivo da recusa: " + j.Erro);
            CapturarDialogo(j, "70-upn-rigida-recusa.png");
            j.Close();
            File.WriteAllText(Path.Combine(pastaSaida, "70-upn.txt"), log.ToString());
        }
    }
}
