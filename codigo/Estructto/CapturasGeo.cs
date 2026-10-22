// Capturas da correção de geometria das ligações (docs/v3/GEOMETRIA-LIGACOES.md): solda limitada à chapa, enrijecedores
// fechados e soldados, enrijecedor do pilar dentro do pilar, placa de base menor que o perfil e placa no eixo fraco (deitada).
// Só elas: ESTRUCTTO_CAPTURAS=geo.
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasGeo(MainWindow w)
        {
            w.DefinirRealista(true);
            w.Faixa.SelectedTabItem = w.Faixa.Tabs.First(t => (t.Header as string) == "Vista");
            w.MostrarDiagrama(Diagrama.Nenhum);
            w.MostrarVista3D(true);
            EsperarIdle();
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            EsperarIdle();
            Esperar(Motor.Post("/api/ligacoes/identificar"));
            // cenários: (sufixo, dados da placa de base)
            var placas = new (string suf, JsonObject dados)[]
            {
                ("placa-pequena", new JsonObject { ["pelo_perfil"] = false, ["H"] = 0.20, ["B"] = 0.12, ["borda"] = 0.035, ["esp"] = 0.019 }),   // chapa menor que o perfil: solda só sobre a chapa
                ("placa-eixo-fraco", new JsonObject { ["pelo_perfil"] = true, ["sobra"] = 0.08, ["eixo_fraco"] = true, ["fileiras"] = 3, ["por_fileira"] = 2 }),
            };
            bool primeira = true;
            foreach (var (suf, dados) in placas)
            {
                var tg = Motor.Get("/api/ligacoes/grupos"); Esperar(tg);
                foreach (JsonNode g in tg.Result.AsArray())
                {
                    string t = (string)g["tipo"];
                    JsonObject dd = null; string tl = null;
                    if (t == "base") { tl = "placa_base"; dd = (JsonObject)dados.DeepClone(); }
                    else if (primeira && t == "viga_pilar_mesa") { tl = "chapa_topo_estendida"; dd = new JsonObject { ["enrijecedor_chapa"] = true, ["t_enrijecedor"] = 10.0, ["t_enr_pilar"] = 12.5 }; }
                    if (tl != null) Esperar(Motor.Post("/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = (string)g["id"], ["tipo_ligacao"] = tl, ["dados"] = dd }));
                }
                w.Atualizar3D(false);
                Esperar(w.TarefaLigacoes3D); EsperarIdle();
                if (primeira)
                {
                    foreach (var (alvo, dist, az, el, nome) in new[] { (new Point3D(0.3, 0, 6.0), 2.4, -40.0, 20.0, "chapa-topo"), (new Point3D(0.2, 0, 6.1), 1.8, -130.0, 35.0, "chapa-topo-tras"),
                                                                      (new Point3D(0.1, 0, 6.2), 1.6, -60.0, 60.0, "pilar-topo"),
                                                                      (new Point3D(0.05, 0, 5.85), 0.9, -90.0, 15.0, "pilar-enrijecedores"),
                                                                      (new Point3D(0.15, 0, 6.25), 0.6, -120.0, 25.0, "enrijecedor-chapa-topo"),
                                                                      (new Point3D(10, 0, 7.9), 1.4, -90.0, 10.0, "cumeeira") })
                    {
                        w.vista3D.Focar(alvo, dist, az, el);
                        CapturarPrincipal(w, "61-geo-" + nome);
                    }
                    primeira = false;
                }
                foreach (var (az, el, v, dist) in new[] { (-40.0, 24.0, "", 1.7), (-130.0, 30.0, "-tras", 1.7), (-90.0, 89.0, "-planta", 1.7), (-60.0, 30.0, "-perto", 0.55), (-150.0, 30.0, "-perto-tras", 0.55) })
                {
                    w.vista3D.Focar(new Point3D(0, 0, dist < 1 ? 0.03 : 0.1), dist, az, el);
                    CapturarPrincipal(w, "61-geo-" + suf + v);
                }
            }
            w.FecharEspaciaisSemPerguntar();
        }
    }
}
