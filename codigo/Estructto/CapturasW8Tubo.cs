// W8-TUBO (docs/v3/W8-TUBO.md): perfil–tubo soldado no padrão de fábrica (I W310 no tubo Ø323,8, parede verificada) nas
// 4 vistas grandes e no 3D, a mesma ligação num tubo retangular (paredes do receptor como chapas) e a solda direta
// (perna acima da mínima). Só elas: ESTRUCTTO_CAPTURAS=w8tubo.
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW8Tubo(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();
            var log = new StringBuilder();
            void Vistas(string nome)
            {
                var r = j.Resultado;
                log.AppendLine($"{nome}: taxa {D(r, "taxa"):0.000} ok {Bool(r, "ok")}");
                foreach (var v in Lista(r?["verificacoes"])) log.AppendLine($"   {D(v, "taxa"):0.000}  {S(v, "nome")}  [{S(v, "item")}]");
                foreach (var a in Lista(r?["avisos"])) log.AppendLine("   aviso: " + (string)a);
                CapturarDialogo(j, $"80-w8tubo-{nome}.png");
                foreach (var (v, vista) in new[] { (1, "elevacao"), (2, "planta"), (3, "lateral"), (4, "3d") })
                {
                    j.MostrarVista(v); EsperarIdle();
                    CapturarDialogo(j, $"80-w8tubo-{nome}-{vista}.png");
                }
                j.MostrarVista(0); EsperarIdle();
            }
            Esperar(j.Escolher("perfil_tubo_soldado", false)); EsperarIdle();
            Vistas("perfil-tubo");
            j.DefinirDados(new JsonObject { ["forma_receptor"] = "tubo_retangular", ["D_receptor"] = 300, ["B_receptor"] = 300, ["t_receptor"] = 9.5 });
            Esperar(j.CalcularAgora()); EsperarIdle();
            Vistas("perfil-tubo-retangular");
            Esperar(j.Escolher("solda_direta_alma", false)); EsperarIdle();
            var rs = j.Resultado;
            log.AppendLine($"solda-direta: taxa {D(rs, "taxa"):0.000} ok {Bool(rs, "ok")} perna {D(rs?["geometria"]?["soldas"]?[0], "perna")}");
            CapturarDialogo(j, "80-w8tubo-solda-direta.png");
            File.WriteAllText(Path.Combine(pastaSaida, "80-w8tubo.txt"), log.ToString());
            j.Close();
        }
    }
}
