// W10-SD (docs/v3/W10-SOLDA-DIRETA.md): solda direta com o contorno inteiro soldado e viga–viga de tamanhos diferentes.
// Os dois casos do usuário (UPN 50 na alma de UPN 50 e de UPN 100, esforços pequenos do modelo), o Sugerir e o Otimizar
// do UPN 100 e o padrão W310 na mesa do W250, cada um nas vistas juntas, nas 4 vistas grandes e no 3D.
// Só elas: ESTRUCTTO_CAPTURAS=w10sd (82-w10sd-*.png + 82-w10sd.txt).
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
        static void CapturasW10Sd(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            void Vistas(string nome)
            {
                var r = j.Resultado;
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")} status '{j.Status}' erro '{j.Erro}' sem solução {j.SemSolucao}");
                log.AppendLine("   dados: " + string.Join(", ", new[] { "b_solda", "soldar_mesas", "desnivel" }.Select(k => k + " = " + (j.DadosAtuais?[k]?.ToJsonString() ?? "—"))));
                foreach (var v in Lista(r?["verificacoes"])) log.AppendLine($"   {D(v, "taxa"):0.000}  {S(v, "nome")}  Sd {D(v, "solicitante"):0.0} Rd {D(v, "resistente"):0.0} {S(v, "unidade")}  [{S(v, "item")}]");
                foreach (var a in Lista(r?["avisos"])) log.AppendLine("   aviso: " + (string)a);
                int ns = 0; foreach (var s in Lista(r?["geometria"]?["soldas"])) { ns++; log.AppendLine($"   solda {ns}: perna {D(s, "perna")} pontos {Lista(s?["pontos"]).Count()}"); }
                CapturarDialogo(j, $"82-w10sd-{nome}.png");
                foreach (var (v, vista) in new[] { (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"82-w10sd-{nome}-{vista}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            // esforços da captura do usuário (vindos do modelo): N −0,35; Fy 0,25; Fz 0,04; Mx −0,04; My 0; Mz −0,16
            var esf = new JsonObject { ["N"] = -0.35, ["Vz"] = 0.25, ["V"] = 0.04, ["T"] = -0.04, ["M"] = 0.0, ["Mz"] = -0.16 };
            Esperar(j.Escolher("solda_direta_alma", false)); EsperarIdle();
            foreach (var (nome, sup) in new[] { ("upn50-upn50", "UPN 50"), ("upn50-upn100", "UPN 100") })
            {
                j.DefinirDados(new JsonObject { ["perfil_viga"] = "UPN 50", ["perfil_suporte"] = sup, ["apoio"] = "alma_viga", ["b_solda"] = 3 });
                j.DefinirEsforcos((JsonObject)esf.DeepClone());
                Esperar(j.CalcularAgora()); EsperarIdle();
                Vistas(nome);
                if (sup == "UPN 50") { Esperar(j.Sugerir()); EsperarIdle(); Vistas(nome + "-sugerir"); }
            }
            Esperar(j.Sugerir()); EsperarIdle();
            Vistas("upn50-upn100-sugerir");
            j.DefinirDados(new JsonObject { ["b_solda"] = 3 });
            Esperar(j.CalcularAgora()); EsperarIdle();
            Esperar(j.Otimizar()); EsperarIdle();
            Vistas("upn50-upn100-otimizar");
            // W200 na alma de W310 (menor que a altura livre) e W310 na alma de W310 (igual: recorte)
            foreach (var (nome, vg, sup) in new[] { ("w200-w310", "W200x15", "W310x38.7"), ("w310-w310", "W310x38.7", "W310x38.7") })
            {
                j.DefinirDados(new JsonObject { ["perfil_viga"] = vg, ["perfil_suporte"] = sup, ["apoio"] = "alma_viga" });
                j.DefinirEsforcos(new JsonObject { ["N"] = 0.0, ["Vz"] = 0.0, ["V"] = 50.0, ["T"] = 0.0, ["M"] = 0.0, ["Mz"] = 0.0 });
                Esperar(j.Sugerir()); EsperarIdle();
                Vistas(nome);
            }
            // padrão de fábrica: W310 na mesa do W250
            var ex = Lista(tipos).FirstOrDefault(t => S(t, "tipo") == "solda_direta_alma")?["exemplo"];
            j.DefinirDados((JsonObject)ex["dados"].DeepClone());
            j.DefinirEsforcos((JsonObject)ex["esforcos"].DeepClone());
            Esperar(j.CalcularAgora()); EsperarIdle();
            Vistas("w310-w250-mesa");
            File.WriteAllText(Path.Combine(pastaSaida, "82-w10sd.txt"), log.ToString());
            j.Close();
        }
    }
}
