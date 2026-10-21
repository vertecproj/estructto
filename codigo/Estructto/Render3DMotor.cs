// Ligações do motor no Realista: os grupos de ligações do modelo espacial (GET /api/ligacoes/grupos, ver
// JanelaLigacoesGrupos.cs e docs/v3/LIGACOES-GRUPOS.md) que têm projeto (tipo_ligacao + dados) viram geometria do contrato
// (POST /api/ligacoes/verificar, uma chamada por grupo, esforços nulos: a geometria só depende dos dados) e uma Ligacao3D por
// membro (nó, barra). Grupo "base" com placa_base vira placa + chumbadores pelo perfil (o verificador da placa não devolve o
// contrato; ponytail: medidas do esquema padrão do motor, esp 19 mm, borda 50 mm, sobra 100 mm). Sem grupo ou sem projeto,
// nada é desenhado (as ligações aparecem depois de Ligações › Identificar e de escolher o tipo).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NovoFtool
{
    public partial class DocumentoEspacial
    {
        // Chamar na thread da UI: o Json é lido aqui (cópia) e as chamadas ao motor vão na fila de Motor.Exclusivo,
        // com este modelo recolocado no servidor (Garantir) — senão os grupos lidos podem ser os de outra aba.
        public Task<List<Ligacao3D>> LigacoesParaVista(Modelo3D vista)
        {
            var json = (JsonObject)Json.DeepClone();
            return Motor.Exclusivo(async () =>
            {
                try { await Garantir(); return await LigacoesDe(json, vista); }
                catch { return new List<Ligacao3D>(); }
            });
        }

        static async Task<List<Ligacao3D>> LigacoesDe(JsonObject Json, Modelo3D vista)
        {
            var r = new List<Ligacao3D>();
            JsonNode grupos;
            try { grupos = await Motor.Get("/api/ligacoes/grupos"); } catch { return r; }
            if (!(grupos is JsonArray ga) || ga.Count == 0) return r;
            var idNo = new Dictionary<string, int>();
            foreach (var kv in Json["nos"].AsObject()) idNo[kv.Key] = idNo.Count + 1;   // a mesma ordem de ParaVista
            var barras = vista.Barras.Where(b => b.Origem is RefMotor).ToDictionary(b => ((RefMotor)b.Origem).Id, b => b);
            var bj = Json["barras"].AsObject();
            var secoes = Json["secoes"] as JsonObject;
            double Prop(string secao, string k) { try { return (secoes?[secao]?["props"]?[k] as JsonValue)?.GetValue<double>() ?? 0; } catch { return 0; } }
            // seção da peça de apoio no nó: a barra com papel "pilar"; senão a outra barra mais alta
            string Suporte(string no, string barra)
            {
                string melhor = null; double h = -1;
                foreach (var kv in bj)
                {
                    if (kv.Key == barra || ((string)kv.Value["ni"] != no && (string)kv.Value["nj"] != no)) continue;
                    string sec = (string)kv.Value["secao"]; double hh = Prop(sec, "h");
                    if ((string)kv.Value["params"]?["papel"] == "pilar") return sec;
                    if (hh > h) { h = hh; melhor = sec; }
                }
                return melhor;
            }
            var cache = new Dictionary<string, JsonObject>();
            foreach (JsonNode g in ga)
            {
                var proj = g["projeto"] as JsonObject; string tl = (string)proj?["tipo_ligacao"];
                foreach (JsonNode mb in g["membros"]?.AsArray() ?? new JsonArray())
                {
                    string no = (string)mb["no"], bid = (string)mb["barra"];
                    if (no == null || !idNo.TryGetValue(no, out int idN) || !barras.TryGetValue(bid, out Barra3D b)) continue;
                    string sec = (string)bj[bid]?["secao"];
                    if (tl == "placa_base")   // só com projeto, como as outras: ligações aparecem no Realista quando projetadas
                    {
                        if (secoes?[sec] is not JsonObject sj) continue;
                        var d = proj?["dados"] as JsonObject;
                        double Dd(string k, double padrao) { try { return (d?[k] as JsonValue)?.GetValue<double>() ?? padrao; } catch { return padrao; } }
                        var f = GeometriaPerfis.CriarContorno(Perfil3D.DoMotor(sj), 0.1);
                        if (f.Concreto) continue;
                        double sobra = Dd("sobra", 0.1) * 1000, borda = Dd("borda", 0.05) * 1000, esp = Dd("esp", 0.019) * 1000, dia = Dd("diametro_mm", 16);
                        bool peloPerfil = d?["pelo_perfil"] is JsonValue pv && pv.TryGetValue(out bool pp) ? pp : true;
                        double H = peloPerfil ? Math.Ceiling((f.H * 1000 + 2 * sobra) / 10) * 10 : Dd("H", 0.4) * 1000, B = peloPerfil ? Math.Ceiling((f.B * 1000 + 2 * sobra) / 10) * 10 : Dd("B", 0.3) * 1000;
                        var furos = new List<(double, double)> { (-H / 2 + borda, -B / 2 + borda), (H / 2 - borda, -B / 2 + borda), (H / 2 - borda, B / 2 - borda), (-H / 2 + borda, B / 2 - borda) };
                        r.Add(LigacoesAmostra.PlacaDeBase(idN, b, b.Ni == idN, f, H, B, esp, dia, furos, Dd("solda", 0) * 1000));
                        continue;
                    }
                    if (tl == null) continue;
                    var dados = proj["dados"]?.DeepClone() as JsonObject ?? new JsonObject();
                    if (sec != null) dados["perfil_viga"] = sec;
                    string sup = Suporte(no, bid);
                    if (sup != null) { dados["perfil_pilar"] = sup; dados["perfil_suporte"] = sup; }
                    string chave = tl + "|" + dados.ToJsonString();
                    if (!cache.TryGetValue(chave, out JsonObject geo))
                    {
                        try
                        {
                            var res = await Motor.Post("/api/ligacoes/verificar", new JsonObject { ["tipo"] = tl, ["dados"] = dados,
                                                       ["esforcos"] = new JsonObject { ["N"] = 0, ["V"] = 0, ["Vz"] = 0, ["M"] = 0, ["Mz"] = 0, ["T"] = 0 } });
                            geo = res?["geometria"] as JsonObject;
                        }
                        catch { geo = null; }
                        cache[chave] = geo;
                    }
                    if (geo != null) r.Add(Ligacao3D.DoJson(idN, b.Id, geo, (string)g["nome"]));
                }
            }
            return r;
        }
    }

    public partial class MainWindow
    {
        internal Task TarefaLigacoes3D = Task.CompletedTask;   // capturas esperam por ela

        // Depois de carregar a Vista3D com o modelo espacial no Realista: busca as ligações no motor e redesenha.
        void LigacoesEspacial3D(DocumentoEspacial d)
        {
            if (vista3D == null || !vista3D.Realista) return;
            var m = vista3D.ModeloAtual;
            TarefaLigacoes3D = d.LigacoesParaVista(m).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || Esp != d || vista3D.ModeloAtual != m) return;
                vista3D.DefinirLigacoes(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
