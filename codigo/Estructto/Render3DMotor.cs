// Ligações do motor no Realista: os grupos de ligações do modelo espacial (GET /api/ligacoes/grupos, ver
// JanelaLigacoesGrupos.cs e docs/v3/LIGACOES-GRUPOS.md) viram geometria do contrato e uma Ligacao3D por membro (nó, barra).
// - Grupo com projeto (tipo_ligacao + dados): POST /api/ligacoes/verificar (esforços nulos: a geometria só depende dos dados).
// - Grupo sem projeto: PROPOSTA (desenhada esmaecida, chip "Ligação proposta" na vista). Tipo padrão do tipo de nó
//   (TipoProposto), POST /api/ligacoes/sugerir com os perfis do grupo e esforços típicos e depois /verificar com o que ele
//   sugeriu. Se o motor não desenha esse caso (perfil formado a frio, nó de treliça, emenda em ângulo), a Ligacao3D vai
//   "Pendente" e o Render3D faz a proposta do tipo de nó com as juntas (LigacoesAmostra.Proposta).
// - Grupo "base" com placa_base (o padrão do motor) vira placa + chumbadores pelo perfil (o verificador da placa não devolve o
//   contrato; ponytail: medidas do esquema padrão do motor, esp 19 mm, borda 50 mm, sobra 100 mm).
// - Modelo sem grupos (Ligações › Identificar nunca rodou): um marcador Pendente = "*" e o Render3D propõe em todas as juntas.
// Desempenho: cache estático por (tipo, dados) — os grupos já são por assinatura (perfis + ângulo + condição), então é uma
// chamada por grupo e nenhuma ao recarregar; no máximo MaxChamadas chamadas novas por carga (o resto fica Pendente).
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

        static readonly Dictionary<string, JsonObject> cacheGeo = new Dictionary<string, JsonObject>();   // tipo|dados → geometria (null = o motor não desenha)
        const int MaxChamadas = 40;   // ponytail: chamadas novas ao motor por carga; o resto vira proposta local

        // Tipo de ligação proposto para um grupo sem projeto (assinatura: tipo|seção|seção do apoio|papel do apoio|ângulo|condição|
        // material; emenda: emenda|seção|seção|ângulo|condição|material). null = proposta local (Render3D).
        static string TipoProposto(string tipo, string[] a, bool vertical)
        {
            bool rigida = a.Contains("rigida");
            switch (tipo)
            {
                case "base": return "placa_base";
                case "viga_pilar_mesa": return rigida ? "chapa_topo_estendida" : "chapa_simples_alma";
                case "viga_pilar_alma": case "viga_viga": return "chapa_simples_alma";
                case "gusset_contraventamento": return "gusset_contraventamento";
                case "emenda": return a.Length > 3 && int.TryParse(a[3], out int ang) && ang <= 5 ? (vertical ? "emenda_pilar" : "emenda_viga") : null;
            }
            return null;
        }

        static async Task<List<Ligacao3D>> LigacoesDe(JsonObject Json, Modelo3D vista)
        {
            var r = new List<Ligacao3D>();
            JsonNode grupos;
            try { grupos = await Motor.Get("/api/ligacoes/grupos"); } catch { return r; }
            if (!(grupos is JsonArray ga)) return r;
            if (ga.Count == 0) { r.Add(new Ligacao3D { Pendente = "*", Proposta = true }); return r; }
            var idNo = new Dictionary<string, int>();
            foreach (var kv in Json["nos"].AsObject()) idNo[kv.Key] = idNo.Count + 1;   // a mesma ordem de ParaVista
            var barras = vista.Barras.Where(b => b.Origem is RefMotor).ToDictionary(b => ((RefMotor)b.Origem).Id, b => b);
            var nosVista = vista.Nos.ToDictionary(n => n.Id);
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
            bool Vertical(Barra3D b) => nosVista.TryGetValue(b.Ni, out No3D a) && nosVista.TryGetValue(b.Nj, out No3D c)
                                        && Math.Abs(c.Z - a.Z) > 0.95 * Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y) + (c.Z - a.Z) * (c.Z - a.Z));
            // gusset: o motor pede a forma e as medidas do perfil da diagonal (mm), não o nome do catálogo
            JsonObject PerfisGusset(string sec, string[] a)
            {
                string t = (string)secoes?[sec]?["props"]?["tipo"];
                string forma = t == "tubo_circular" || t == "tubo_retangular" || t == "L" || t == "U" ? t : null;
                if (forma == null) return null;
                double h = Prop(sec, "h") * 1000, b = Prop(sec, "b") * 1000, tt = (Prop(sec, "t") > 0 ? Prop(sec, "t") : Prop(sec, "tw")) * 1000;
                string papel = a.Length > 3 ? a[3] : "";
                int ang = a.Length > 4 && int.TryParse(a[4], out int g) ? g : 45;
                string apoio = papel == "base" ? "base" : papel == "viga" ? "viga" : papel.StartsWith("pilar termina") ? "viga-pilar" : "pilar";
                double theta = Math.Max(5, Math.Min(85, apoio == "viga" ? 90 - ang : ang));
                var o = new JsonObject { ["secao"] = forma, ["dim_a"] = h, ["t_perfil"] = tt, ["apoio"] = apoio, ["theta"] = theta };
                if (forma != "tubo_circular") o["dim_b"] = b;
                if (forma == "U" && Prop(sec, "tf") > 0) o["tf_perfil"] = Prop(sec, "tf") * 1000;
                return o;
            }
            int chamadas = 0;
            async Task<JsonObject> Geometria(string tl, JsonObject dados, JsonObject perfis)
            {
                string chave = tl + "|" + dados.ToJsonString() + "|" + (perfis?.ToJsonString() ?? "");
                lock (cacheGeo) if (cacheGeo.TryGetValue(chave, out JsonObject g0)) return g0;
                if (chamadas++ >= MaxChamadas) return null;   // não guarda: na próxima carga tenta de novo
                JsonObject geo = null;
                try
                {
                    if (perfis != null)   // proposta: o que o pré-dimensionamento do motor sugere para esses perfis
                    {
                        var sug = await Motor.Post("/api/ligacoes/sugerir", new JsonObject { ["tipo"] = tl, ["perfis"] = perfis.DeepClone(),
                                                   ["esforcos"] = new JsonObject { ["N"] = 50, ["V"] = 50, ["M"] = 30 } }) as JsonObject;
                        foreach (var kv in perfis) dados[kv.Key] = kv.Value?.DeepClone();
                        if (sug != null) foreach (var kv in sug) if (kv.Value != null) dados[kv.Key] = kv.Value.DeepClone();
                    }
                    var res = await Motor.Post("/api/ligacoes/verificar", new JsonObject { ["tipo"] = tl, ["dados"] = dados,
                                               ["esforcos"] = new JsonObject { ["N"] = 0, ["V"] = 0, ["Vz"] = 0, ["M"] = 0, ["Mz"] = 0, ["T"] = 0 } });
                    geo = res?["geometria"] as JsonObject;
                    if (geo != null && !(geo["chapas"] is JsonArray ca && ca.Count > 0) && !(geo["cantoneiras"] is JsonArray cc && cc.Count > 0)) geo = null;   // sem peças: proposta local
                }
                catch { geo = null; }
                lock (cacheGeo) cacheGeo[chave] = geo;
                return geo;
            }
            int nProp = 0;
            foreach (JsonNode g in ga)
            {
                var proj = g["projeto"] as JsonObject; string tipoNo = (string)g["tipo"] ?? "";
                string[] assin = ((string)g["assinatura"] ?? "").Split('|');
                bool proposta = proj == null || (string)proj["tipo_ligacao"] == null;
                foreach (JsonNode mb in g["membros"]?.AsArray() ?? new JsonArray())
                {
                    string no = (string)mb["no"], bid = (string)mb["barra"];
                    if (no == null || !idNo.TryGetValue(no, out int idN) || !barras.TryGetValue(bid, out Barra3D b)) continue;
                    if (proposta && nProp >= Render3D.MaxPropostas) continue;
                    string sec = (string)bj[bid]?["secao"];
                    string tl = proposta ? TipoProposto(tipoNo, assin, Vertical(b)) : (string)proj["tipo_ligacao"];
                    Ligacao3D lig = null;
                    if (tl == "placa_base")
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
                        lig = LigacoesAmostra.PlacaDeBase(idN, b, b.Ni == idN, f, H, B, esp, dia, furos, Dd("solda", 0) * 1000);
                    }
                    else if (tl != null)
                    {
                        var dados = proposta ? new JsonObject() : proj["dados"]?.DeepClone() as JsonObject ?? new JsonObject();
                        JsonObject perfis = null;
                        string sup = Suporte(no, bid);
                        if (tl == "gusset_contraventamento") { if (proposta) perfis = PerfisGusset(sec, assin); if (sup != null) { if (perfis != null) perfis["perfil_pilar"] = sup; else dados["perfil_pilar"] = sup; } }
                        else
                        {
                            if (sec != null) dados["perfil_viga"] = sec;
                            if (sup != null) { dados["perfil_pilar"] = sup; dados["perfil_suporte"] = sup; }
                            if (tl == "emenda_pilar" && sec != null) dados["perfil_pilar"] = sec;
                            if (proposta) { perfis = new JsonObject(); foreach (var kv in dados) perfis[kv.Key] = kv.Value?.DeepClone(); dados = new JsonObject(); }
                        }
                        JsonObject geo = proposta && perfis == null ? null : await Geometria(tl, dados, perfis);
                        if (geo != null) lig = Ligacao3D.DoJson(idN, b.Id, geo, (string)g["nome"]);
                    }
                    if (lig == null)
                    {
                        if (!proposta) continue;   // projeto que o motor não desenha: nada (como antes)
                        lig = new Ligacao3D { No = idN, Barra = b.Id, Nome = (string)g["nome"] ?? "", Pendente = tipoNo + "|" + string.Join("|", assin) };
                    }
                    lig.Proposta = proposta;
                    if (mb["excluir_interferencia"] is JsonValue ev && ev.TryGetValue(out bool ex) && ex) lig.Excluir = true;   // G5: barra curta marcada no membro
                    if (proposta) nProp++;
                    r.Add(lig);
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
