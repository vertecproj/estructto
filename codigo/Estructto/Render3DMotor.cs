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
// pedido por grupo e nenhum ao recarregar; os pedidos novos vão juntos num POST /api/ligacoes/geometrias (G6, sem teto).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

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

        static readonly HashSet<string> Flexiveis = new HashSet<string> { "chapa_simples_alma", "solda_direta_alma", "cantoneira_dupla_alma", "chapa_extremidade_flexivel", "apoio_cantoneira" };   // aceitam x_face (G6)
        static readonly Dictionary<string, JsonObject> cacheGeo = new Dictionary<string, JsonObject>();   // tipo|dados → geometria (null = o motor não desenha)

        // Tipo de ligação proposto para um grupo sem projeto (assinatura: tipo|seção|seção do apoio|papel do apoio|ângulo|condição|
        // material; emenda: emenda|seção|seção|ângulo|condição|material). null = proposta local (Render3D).
        static string TipoProposto(string tipo, string[] a, bool vertical, bool vigaU = false)
        {
            bool rigida = a.Contains("rigida");
            switch (tipo)
            {
                case "base": return "placa_base";
                case "viga_pilar_mesa": return rigida && !vigaU ? "chapa_topo_estendida" : "chapa_simples_alma";   // G6: U não tem rígida no motor: flexível
                case "viga_pilar_alma": case "viga_viga": return "chapa_simples_alma";
                case "gusset_contraventamento": return "gusset_contraventamento";
                case "emenda": return a.Length > 3 && int.TryParse(a[3], out int ang) && ang <= 5 ? (vertical ? "emenda_pilar" : "emenda_viga")
                                      : "chapa_simples_alma";   // G6: canto (> 5°) = viga–viga na outra barra (o membro traz o apoio), não proposta local
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
            string SuporteBarra(string no, string barra)
            {
                string melhor = null; double h = -1;
                foreach (var kv in bj)
                {
                    if (kv.Key == barra || ((string)kv.Value["ni"] != no && (string)kv.Value["nj"] != no)) continue;
                    double hh = Prop((string)kv.Value["secao"], "h");
                    if ((string)kv.Value["params"]?["papel"] == "pilar") return kv.Key;
                    if (hh > h) { h = hh; melhor = kv.Key; }
                }
                return melhor;
            }
            Vector3D Eixo(Barra3D q) => nosVista.TryGetValue(q.Ni, out No3D a) && nosVista.TryGetValue(q.Nj, out No3D c) ? new Vector3D(c.X - a.X, c.Y - a.Y, c.Z - a.Z) : new Vector3D();
            // G6: face de contato do apoio que não é I (U, Ue, composto, tubo: a alma não fica no eixo) = até onde o perfil do
            // apoio chega na direção da viga (mm do nó). O motor corta a viga e põe a chapa nela (x_face); I fica com o padrão.
            double? XFace(string apoioId, Barra3D b, bool emI, bool gusset = false, string no = null)
            {
                if (apoioId == null || !barras.TryGetValue(apoioId, out Barra3D q) || !(secoes?[(string)bj[apoioId]?["secao"] ?? ""] is JsonObject sj)) return null;
                if ((string)sj["props"]?["tipo"] is "I" or null) return null;
                var f = GeometriaPerfis.CriarContorno(Perfil3D.DoMotor(sj), 0.1);
                if (f.Concreto || f.Lacos.Count == 0) return null;
                Vector3D es = Eixo(q), xb = Eixo(b); if (es.Length < 1e-9 || xb.Length < 1e-9) return null;
                es.Normalize(); xb.Normalize(); if (!emI) xb = -xb;   // da ponta da viga para dentro dela
                if (gusset) { xb -= Vector3D.DotProduct(xb, es) * es; if (xb.Length < 1e-6) return null; xb.Normalize(); }   // gusset: no plano dele, ⟂ ao apoio
                double Ext(Barra3D r, GeometriaPerfis.Contorno fr, Vector3D er)
                {
                    Vector3D vr = Modelo3D.EixoY(er, r.Rot + r.RotPerfil), ur = Vector3D.CrossProduct(vr, er);
                    System.Windows.Point dr = GeometriaPerfis.Cardinal(fr, r.Cardinal);
                    return fr.Lacos[0].Max(p => Vector3D.DotProduct(ur * (p.X + dr.X) + vr * (p.Y + dr.Y), xb));
                }
                double ext = Ext(q, f, es);
                // G6 r1: as outras barras do nó quase ⟂ a esta (ex.: a viga na mesa de um pilar UPN 50 com a chapa nas costas da alma):
                // a viga para fora delas também; a folga do motor (12 mm) passa a espessura da chapa encostada nelas
                if (!gusset && no != null)
                    foreach (var kv in bj)
                    {
                        if (kv.Key == apoioId || !barras.TryGetValue(kv.Key, out Barra3D r) || r == b || ((string)kv.Value["ni"] != no && (string)kv.Value["nj"] != no)) continue;
                        if (!(secoes?[(string)kv.Value["secao"] ?? ""] is JsonObject sr)) continue;
                        Vector3D er = Eixo(r); if (er.Length < 1e-9) continue; er.Normalize();
                        if (Math.Abs(Vector3D.DotProduct(er, xb)) > 0.5) continue;
                        var fr = GeometriaPerfis.CriarContorno(Perfil3D.DoMotor(sr), 0.1);
                        if (!fr.Concreto && fr.Lacos.Count > 0) ext = Math.Max(ext, Ext(r, fr, er));
                    }
                return ext > 0 ? Math.Round(ext * 1000, 1) : null;
            }
            // G6: eixo do apoio do gusset no mundo (o apoio_dir do motor é este eixo no referencial local): pilar e viga-pilar = o
            // pilar (vertical); base = projeção horizontal da diagonal; viga = eixo da peça de apoio
            Vector3D? ApoioMundo(string apoio, Barra3D b, string no, string bid, string apoioMotor)
            {
                if (apoio == "pilar" || apoio == "viga-pilar") return new Vector3D(0, 0, 1);
                if (apoio == "base") { var e = Eixo(b); e.Z = 0; return e.Length > 1e-9 ? e : (Vector3D?)null; }
                string sb = apoioMotor ?? SuporteBarra(no, bid);   // o membro do motor diz qual barra apoia
                return sb != null && barras.TryGetValue(sb, out Barra3D q) ? Eixo(q) : (Vector3D?)null;
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
            // G6: uma passada coleta os pedidos que faltam no cache, um POST /api/ligacoes/geometrias resolve todos, a segunda
            // passada monta as ligações (sem teto de chamadas)
            Dictionary<string, (string tl, JsonObject dados, JsonObject perfis)> pedidos = null;
            Task<JsonObject> Geometria(string tl, JsonObject dados, JsonObject perfis)
            {
                string chave = tl + "|" + dados.ToJsonString() + "|" + (perfis?.ToJsonString() ?? "");
                lock (cacheGeo) if (cacheGeo.TryGetValue(chave, out JsonObject g0)) return Task.FromResult(g0);
                if (pedidos != null) pedidos[chave] = (tl, (JsonObject)dados.DeepClone(), (JsonObject)perfis?.DeepClone());
                return Task.FromResult<JsonObject>(null);
            }
            async Task Lote()
            {
                const int MaxLote = 500;   // = MAX_LOTE_LIGACOES do motor
                foreach (var parte in pedidos.Chunk(MaxLote))
                {
                var itens = new JsonArray();
                foreach (var p in parte) itens.Add(new JsonObject { ["tipo"] = p.Value.tl, ["dados"] = p.Value.dados.DeepClone(), ["perfis"] = p.Value.perfis?.DeepClone() });
                JsonArray res;
                try { res = await Motor.Post("/api/ligacoes/geometrias", new JsonObject { ["itens"] = itens }) as JsonArray; } catch { return; }   // não guarda: tenta na próxima carga
                if (res == null || res.Count != parte.Length) return;
                int k = 0;
                foreach (var chave in parte.Select(p => p.Key))
                {
                    var it = res[k++];
                    var geo = it?["geometria"] as JsonObject;
                    // barra curta demais para a ligação (G2: ok = false + motivo/aviso): fora do teste de interferência
                    if (geo != null && (((string)it["motivo"] ?? "").Contains("barra curta") || (it["avisos"] as JsonArray)?.Any(a => ((string)a ?? "").Contains("barra curta")) == true))
                        geo["excluir_interferencia"] = true;
                    if (geo != null && !(geo["chapas"] is JsonArray ca && ca.Count > 0) && !(geo["cantoneiras"] is JsonArray cc && cc.Count > 0) && !(geo["soldas"] is JsonArray sc && sc.Count > 0)) geo = null;   // sem peças: proposta local
                    geo?.Parent?.AsObject().Remove("geometria");
                    lock (cacheGeo) cacheGeo[chave] = geo;
                }
                }
            }
            pedidos = new Dictionary<string, (string, JsonObject, JsonObject)>();
            await Passo();
            await Lote();
            pedidos = null;
            r.Clear();
            await Passo();
            return r;
            async Task Passo()
            {
            var pontasComLig = new Dictionary<(string, string), string>();   // (barra, nó) com ligação → barra de apoio
            foreach (var m in ga.SelectMany(g => g["membros"]?.AsArray() ?? new JsonArray())) pontasComLig[((string)m["barra"], (string)m["no"])] = (string)m["apoio"];
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
                    string tl = proposta ? TipoProposto(tipoNo, assin, Vertical(b), (string)secoes?[(string)bj[bid]?["secao"] ?? ""]?["props"]?["tipo"] is "U" or "Ue") : (string)proj["tipo_ligacao"];
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
                        string apoioId = (string)mb["apoio"] ?? SuporteBarra(no, bid), sup = apoioId == null ? null : (string)bj[apoioId]?["secao"];   // G6: a barra de apoio que o motor escolheu
                        bool emINo = b.Ni == idN;
                        // G5: seção composta ("2UPN ... duplo") vai como medidas {d, bf, tw, tf} (mm, o motor G5 aceita; sem nova tentativa por nome); U leva abertura_y (±1): lado
                        // y local para onde abrem as mesas (contorno do U abre para +u; y = −u na ponta i, +u na ponta j)
                        JsonNode PerfilMotor(string s)
                        {
                            if ((string)secoes?[s ?? ""]?["props"]?["tipo"] != "dupla") return s;
                            double tw = Prop(s, "tw") > 0 ? Prop(s, "tw") : Prop(s, "t");
                            return new JsonObject { ["nome"] = s, ["d"] = Prop(s, "h") * 1000, ["bf"] = Prop(s, "b") * 1000, ["tw"] = tw * 1000, ["tf"] = (Prop(s, "tf") > 0 ? Prop(s, "tf") : tw) * 1000 };
                        }
                        bool ehU = (string)secoes?[sec ?? ""]?["props"]?["tipo"] is "U" or "Ue";
                        if (tl == "gusset_contraventamento")
                        {
                            if (proposta) perfis = PerfisGusset(sec, assin);
                            if (sup != null) { if (perfis != null) perfis["perfil_pilar"] = PerfilMotor(sup); else dados["perfil_pilar"] = PerfilMotor(sup); }
                            if (ehU) (perfis ?? dados)["abertura_y"] = emINo ? -1 : 1;
                            // G6: apoio que não é I: ec (pilar) / eb (viga) = até onde o perfil chega na direção da diagonal
                            string ap = (string)(perfis ?? dados)["apoio"];
                            if (ap != "base" && XFace(apoioId, b, emINo, true) is double ef) (perfis ?? dados)[ap == "viga" ? "eb" : "ec"] = ef;
                            if (pontasComLig.ContainsKey((bid, emINo ? (string)bj[bid]?["nj"] : (string)bj[bid]?["ni"])))   // G6 r3: as duas pontas com ligação
                                (perfis ?? dados)["x_max"] = Math.Round(Eixo(b).Length / 2 * 1000, 1);
                        }
                        else
                        {
                            if (sec != null) dados["perfil_viga"] = PerfilMotor(sec);
                            if (tl != null && Flexiveis.Contains(tl) && XFace(apoioId, b, emINo, false, no) is double xf) dados["x_face"] = xf;
                            // G6 r3: a outra ponta também tem ligação → esta só pode usar até o meio da barra (o motor projeta dentro ou recusa)
                            string noOutro = emINo ? (string)bj[bid]?["nj"] : (string)bj[bid]?["ni"];
                            if (tl != null && Flexiveis.Contains(tl) && pontasComLig.TryGetValue((bid, noOutro), out string apOutro))
                            {   // metade do vão livre entre as faces dos apoios, contada do nó
                                double L = Eixo(b).Length * 1000, f1 = (dados["x_face"] as JsonValue)?.GetValue<double>() ?? 0, f2 = XFace(apOutro, b, !emINo, false, noOutro) ?? 0;
                                dados["x_max"] = Math.Round(f1 + Math.Max(0, L - f1 - f2) / 2, 1);
                            }
                            if (sup != null) { dados["perfil_pilar"] = PerfilMotor(sup); dados["perfil_suporte"] = PerfilMotor(sup); }
                            if (tl == "emenda_pilar" && sec != null) dados["perfil_pilar"] = PerfilMotor(sec);
                            if (ehU) dados["abertura_y"] = emINo ? -1 : 1;
                            if (proposta) { perfis = new JsonObject(); foreach (var kv in dados) perfis[kv.Key] = kv.Value?.DeepClone(); dados = new JsonObject(); }
                        }
                        JsonObject geo = proposta && perfis == null ? null : await Geometria(tl, dados, perfis);
                        if (geo != null) lig = Ligacao3D.DoJson(idN, b.Id, geo, (string)g["nome"]);
                        // G6: o contrato tem o centroide do U no eixo (costas da alma em y = −abertura·xg); o WPF desenha o U pelo
                        // retângulo envolvente (costas em u = −B/2): o Render3D anda a origem DeslU em u
                        if (lig != null && ehU && tl != "gusset_contraventamento") lig.DeslU = Prop(sec, "xg") - Prop(sec, "b") / 2;
                        if (lig != null && tl == "gusset_contraventamento") lig.ApoioMundo = ApoioMundo((string)(perfis ?? dados)["apoio"] ?? "viga-pilar", b, no, bid, (string)mb["apoio"]);
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
            }
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
