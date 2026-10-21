// Ponte das funções que nasceram no 2D (Nucleo.cs) para o documento 3D único (docs/MODELO-ESPACIAL.md, "Funções do 2D").
//
// Placa de base: a janela (JanelaPlaca) e o cálculo legado (PlacaCalc, o mesmo método de codigo/motor/.../placa_base.py)
// recebem um "pé de pilar" montado a partir do modelo 3D: o nó com apoio, a única barra que chega nele e os esforços da
// barra nesse nó vindos do MOTOR (N, Vz, My em cada combinação ELU). O pé vira um Modelo 2D mínimo (2 nós, 1 barra) no plano
// formado pelo eixo da barra e o eixo forte da seção; ResBarra é montada para reproduzir N, V e M exatos nas duas pontas.
// O pé 2D (só eixo forte) agora alimenta só o desenho e o "Comparar com método legado": a verificação da janela é a do motor
// (/api/placa_base/verificar com o membro {barra, ponta}: todas as combinações ELU, nos dois eixos; PlacaMotor.cs). Pilar
// inclinado com o eixo forte fora do plano vertical não é aceito.
//
// Relatório: o do motor (RelatorioMotor) + anexo das placas pela mesma ponte. Dimensionar: iterações no motor (abaixo).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Media3D;
using Portico;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    // Pé de pilar do modelo 3D como Modelo 2D mínimo, por combinação ELU do motor.
    public sealed class PePilar
    {
        public string NoId, BarraId; public No No; public Barra Barra; public Modelo M;
        public Dictionary<string, ResBarra> PorComb = new Dictionary<string, ResBarra>();
        public string Erro;

        // ResBarra com N, V (2D) e M exatos nas duas pontas: cargas lineares P1=P2 (axial) e Q1, Q2 (transversal) ajustadas.
        public static ResBarra Res(double L, double C, double S, double n0, double nu, double v0, double vu, double m0, double mu)
        {
            var r = new ResBarra { L = L, C = C, S = S, EA = 1, EI = 1 };
            r.F[0] = -n0; r.F[1] = v0; r.F[2] = -m0;
            r.F[3] = nu; r.F[4] = -vu; r.F[5] = mu;
            r.P1 = r.P2 = (n0 - nu) / L;
            double soma = 2 * (vu - v0) / L, t = (mu - m0 - v0 * L) / (L * L);
            r.Q1 = 6 * t - soma; r.Q2 = soma - r.Q1;
            return r;
        }

        // Monta o pé (geometria e seção) a partir do JSON do motor. Erro != null = nó sem pé de pilar válido.
        public static PePilar Montar(JsonObject j, string noId, int idMini)
        {
            var pe = new PePilar { NoId = noId };
            var barras = j["barras"].AsObject().Where(kv => (string)kv.Value["ni"] == noId || (string)kv.Value["nj"] == noId).ToList();
            if (barras.Count != 1) { pe.Erro = "A placa de base precisa ficar num nó onde chega uma só barra (o pilar)."; return pe; }
            var b = barras[0].Value; pe.BarraId = barras[0].Key;
            Point3D P(string id) { var n = j["nos"][id]; return new Point3D((double)n["x"], (double)n["y"], (double)n["z"]); }
            Point3D a = P((string)b["ni"]), c = P((string)b["nj"]);
            Vector3D e = c - a; double L = e.Length;
            if (L < 1e-9) { pe.Erro = "Barra de comprimento nulo."; return pe; }
            Vector3D z = Modelo3D.EixoY(e, (double)(b["rot"] ?? 0.0) * 180 / Math.PI);   // eixo local z do motor (eixo forte para cima)
            // plano do pé: eixo da barra e eixo z local; tem de ser vertical (ou a barra vertical)
            Vector3D h = new Vector3D(e.X, e.Y, 0);
            if (h.Length < 1e-6 * L) h = new Vector3D(z.X, z.Y, 0);
            else if (Math.Abs(Vector3D.DotProduct(Vector3D.CrossProduct(e, z), new Vector3D(0, 0, 1))) > 1e-3 * L) { pe.Erro = "Pilar inclinado com o eixo forte fora do plano vertical: placa não suportada."; return pe; }
            if (h.Length < 1e-9) h = new Vector3D(1, 0, 0);
            h.Normalize();
            // 2D: X = ao longo de h, Y = Z global. O y local 2D (giro +90° do eixo) tem de ser o z local do motor.
            double ex = Vector3D.DotProduct(e, h) / L, ey = e.Z / L;
            if (-ey * Vector3D.DotProduct(z, h) + ex * z.Z < 0) { h = -h; ex = -ex; }
            var m = new Modelo();
            string sec = (string)b["secao"];
            if (Biblioteca.AcharSecao(sec) == null && Secao2D(j["secoes"]?[sec] as JsonObject, sec) is Secao s2) Biblioteca.Perfis.Add(s2);
            if (Biblioteca.AcharSecao(sec) == null) { pe.Erro = "Perfil \"" + sec + "\" sem dimensões para a placa."; return pe; }
            bool baseNi = (string)b["ni"] == noId;
            int iA = baseNi ? idMini : idMini + 1, iC = baseNi ? idMini + 1 : idMini;
            var na = new No { Id = iA, X = 0, Y = a.Z }; var nc = new No { Id = iC, X = Vector3D.DotProduct(c - a, h), Y = c.Z };
            m.Nos.Add(na); m.Nos.Add(nc);
            var mat = Biblioteca.Materiais.FirstOrDefault()?.Nome ?? "";
            pe.Barra = new Barra { Id = idMini, N1 = iA, N2 = iC, Secao = sec, Material = mat };
            m.Barras.Add(pe.Barra);
            var ap = j["nos"][noId]["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
            pe.No = baseNi ? na : nc;
            pe.No.ApoioX = ap.Length > 0 && ap[0]; pe.No.ApoioY = ap.Length > 2 && ap[2]; pe.No.ApoioGiro = ap.Length > 4 && ap[4];
            pe.M = m;
            return pe;
        }

        // Seção do motor (props I/U/tubo) como Secao do Estructto, para perfis que não estão no catálogo local.
        static Secao Secao2D(JsonObject s, string nome)
        {
            var p = s?["props"] as JsonObject; if (p == null) return null;
            double V(string k) { try { return p[k] is JsonValue v && v.TryGetValue(out double d) ? d : 0; } catch { return 0; } }
            string tipo = (string)p["tipo"];
            string forma = tipo == "I" ? "I" : tipo == "U" || tipo == "Ue" ? "U" : tipo == "tubo_retangular" ? "TR" : tipo == "tubo_circular" ? "TC" : tipo == "retangular" ? "RET" : null;
            if (forma == null || V("h") <= 0) return null;
            return new Secao { Nome = nome, Forma = forma, H = V("h"), Bf = V("b") > 0 ? V("b") : V("h"), Tw = V("tw") > 0 ? V("tw") : V("t"), Tf = V("tf") > 0 ? V("tf") : V("t"),
                               A = V("A") > 0 ? V("A") : (double?)s["A"] ?? 0, Ix = V("Ix") > 0 ? V("Ix") : (double?)s["Iy"] ?? 0, Iy = V("Iy") > 0 ? V("Iy") : (double?)s["Iz"] ?? 0 };
        }

        // Esforços do motor (estações de ni a nj) -> ResBarra do pé (N, V = -Vz, M = -My, convenção de MotorModelo.Ler).
        public void Ler(string comb, JsonNode esf)
        {
            double[] A(string k) => esf?[k]?.AsArray().Select(x => x == null ? 0 : (double)x).ToArray();
            double[] n = A("N"), vz = A("Vz"), my = A("My");
            if (n == null || vz == null || my == null || n.Length < 2) return;
            No a = M.NoPorId(Barra.N1), c = M.NoPorId(Barra.N2);
            double L = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y)), u = n.Length - 1;
            PorComb[comb] = Res(L, (c.X - a.X) / L, (c.Y - a.Y) / L, n[0], n[(int)u], -vz[0], -vz[(int)u], -my[0], -my[(int)u]);
        }
    }

    public partial class MainWindow
    {
        // Nós candidatos a pé de pilar: com apoio e uma só barra (ou com placa nos grupos de ligações).
        static IEnumerable<string> NosDePe(DocumentoEspacial d)
        {
            var cont = new Dictionary<string, int>();
            foreach (var kv in d.Json["barras"].AsObject()) foreach (string k in new[] { "ni", "nj" }) { string n = (string)kv.Value[k]; cont[n] = cont.TryGetValue(n, out int c) ? c + 1 : 1; }
            var comPlaca = new HashSet<string>(NosComPlaca(d));
            foreach (var kv in d.Json["nos"].AsObject())
                if (comPlaca.Contains(kv.Key) || d.Placas.ContainsKey(kv.Key) || (cont.TryGetValue(kv.Key, out int c) && c == 1 && kv.Value["apoio"]?.AsArray().Any(x => x != null && (bool)x) == true)) yield return kv.Key;
        }

        // Fonte única da placa de base: o projeto "placa_base" dos grupos de ligações (modelo.ligacoes, aco/placa_base.py).
        // Grupos das bases com placa e seus nós.
        internal static IEnumerable<(string Id, JsonNode G)> GruposComPlaca(DocumentoEspacial d)
        {
            foreach (var kv in d.Json["ligacoes"]?["grupos"] as JsonObject ?? new JsonObject())
                if ((string)kv.Value?["tipo"] == "base" && (string)kv.Value["projeto"]?["tipo_ligacao"] == "placa_base") yield return (kv.Key, kv.Value);
        }
        internal static IEnumerable<string> NosComPlaca(DocumentoEspacial d) => GruposComPlaca(d).SelectMany(g => Lista(g.G["membros"]).Select(x => (string)x["no"]));

        // Migração: as placas antigas (d.Placas, de arquivos .estructto e da conversão do 2D) viram projetos dos grupos de
        // ligações (uma gravação por placa distinta, com os nós que a usam). Depois d.Placas fica vazio.
        internal async Task<bool> MigrarPlacasEsp(DocumentoEspacial d)
        {
            if (d.Placas.Count == 0) return false;
            int n = 0;
            foreach (var g in d.Placas.GroupBy(kv => PlacaMotor.Dados(kv.Value).ToJsonString()))
            {
                var membros = new JsonArray(g.Select(kv => (JsonNode)MembroDePe(d, kv.Key)).Where(x => x != null).ToArray());
                if (membros.Count == 0) continue;
                try { await d.Ligacoes("POST", "/api/placa_base/gravar", new JsonObject { ["membros"] = membros, ["dados"] = PlacaMotor.Dados(g.First().Value) }); n += membros.Count; }
                catch (Exception ex) { Log("Placa de base: não migrou a placa dos nós " + string.Join(", ", g.Select(kv => kv.Key)) + ": " + PlacaMotor.Mensagem(ex)); return false; }
            }
            d.Placas.Clear();
            alterado = true; AtualizarTitulo();
            Log("Placas de base: " + Pl.N(n, "placa antiga passou", "placas antigas passaram") + " para os grupos de ligações (projeto do motor, método NBR).");
            return true;
        }

        // Garante o cálculo do documento ativo no motor (F5 se preciso). false = sem cálculo.
        internal async Task<bool> GarantirCalculoEsp()
        {
            var d = Esp; if (d == null) return false;
            if (!d.Calculado || Motor.Dono != d) await CalcularEspacial();
            return Esp == d && d.Calculado;
        }

        // Pés montados com os esforços de todas as combinações ELU (uma consulta por combinação). Só para o desenho e o
        // "Comparar com método legado"; a verificação é a do motor. Dentro do Exclusivo o modelo do servidor é o deste documento.
        async Task<List<PePilar>> MontarPes(DocumentoEspacial d, IEnumerable<string> nos)
        {
            int k = 1;
            var pes = nos.Select(n => { var p = PePilar.Montar(d.Json, n, k); k += 2; return p; }).ToList();
            var ok = pes.Where(p => p.Erro == null).ToList();
            if (ok.Count == 0) return pes;
            var combs = d.Calculo?["combinacoes"]?.AsArray().Where(c => ((string)c["estado"] ?? "").StartsWith("ELU")).Select(c => (string)c["nome"]).ToList() ?? new List<string>();
            string ids = string.Join(",", ok.Select(p => p.BarraId).Distinct());
            await Motor.Exclusivo(async () =>
            {
                await d.GarantirCalculado();   // outro documento pode ter deixado o modelo dele no servidor
                foreach (string c in combs)
                {
                    var r = await Motor.Get("/api/resultados?alvo=esforcos&combinacao=" + Uri.EscapeDataString(c) + "&ids=" + Uri.EscapeDataString(ids));
                    foreach (var p in ok) p.Ler(c, r?[p.BarraId]);
                }
            });
            foreach (var p in ok) if (p.PorComb.Count == 0) p.Erro = "O motor não devolveu esforços ELU da barra " + p.BarraId + ".";
            return pes;
        }

        // Pior combinação da placa do pé pelo método LEGADO (comparação); deixa a barra com esse resultado.
        (ResPlaca R, string Comb) PiorPlaca(PePilar pe)
        {
            ResPlaca pior = null; string qual = null; double nota = double.MinValue;
            foreach (var kv in pe.PorComb)
            {
                pe.Barra.Res = kv.Value;
                ResPlaca rp = pe.No.Placa != null ? PlacaCalc.Calcular(pe.M, pe.No) : null;
                double v = rp != null ? NotaPlaca(rp, pe.No) : Math.Abs(kv.Value.M(pe.Barra.N1 == pe.No.Id ? 0 : kv.Value.L));
                if (qual == null || v > nota) { pior = rp; qual = kv.Key; nota = v; }
            }
            if (qual != null) pe.Barra.Res = pe.PorComb[qual];
            return (pior, qual);
        }

        // Placa de base do nó (Ligações e bases › Placa de base, comando PLACA, duplo clique no nó com placa). A placa é o
        // projeto do grupo de ligações das bases do nó; Aplicar grava pelo motor (/api/placa_base/gravar).
        internal async Task AbrirPlacaEsp(string noId)
        {
            var d = Esp; if (d == null) return;
            await MigrarPlacasEsp(d);
            noId ??= vista3D.Selecao.Nos.FirstOrDefault() ?? NosComPlaca(d).FirstOrDefault() ?? NosDePe(d).FirstOrDefault();
            if (noId == null) { Log("Selecione o nó do pé do pilar (com apoio, uma só barra) e repita PLACA."); return; }
            if (!await GarantirCalculoEsp()) return;
            var pe = (await MontarPes(d, new[] { noId }))[0];
            if (pe.Erro != null) { Log("Placa de base no nó " + noId + ": " + pe.Erro); MostrarToast(pe.Erro, true); return; }
            var mb = MembroDePe(d, noId);
            if (GrupoDaBase(d, noId) == null)   // grupos ainda não identificados: o motor identifica (não muda a estrutura)
                try { await d.Ligacoes("POST", "/api/ligacoes/identificar"); } catch (Exception ex) { Log("Ligações: " + PlacaMotor.Mensagem(ex)); }
            var grupo = GrupoDaBase(d, noId);
            var dadosG = grupo?.Value["projeto"] is JsonNode pj && (string)pj["tipo_ligacao"] == "placa_base" ? pj["dados"] : null;
            pe.No.Placa = dadosG != null ? PlacaMotor.Placa(dadosG) : null;
            var (_, crit) = PiorPlaca(pe);
            JanelaPlaca j;
            try { j = new JanelaPlaca(pe.M, pe.No) { Owner = this }; }
            catch (Exception ex) { Log("Erro ao abrir a placa: " + ex.Message); return; }
            j.Title = "Placa de base — nó " + noId;
            j.Api = d.Placa;
            j.CombLegado = crit;
            j.Membros.Add(mb);
            // "Aplicar também em": os outros pés de pilar selecionados na vista e o grupo de ligações das bases deste nó
            var nosComPlaca = new HashSet<string>(NosComPlaca(d));
            foreach (string o in vista3D.Selecao.Nos.Where(x => x != noId).Distinct())
                if (MembroDePe(d, o) is JsonObject mo) j.Outros.Add((o, mo, nosComPlaca.Contains(o)));
            List<JsonObject> membrosG = null;
            if (grupo != null)
            {
                membrosG = Lista(grupo.Value.Value["membros"]).Select(x => new JsonObject { ["barra"] = (string)x["barra"], ["ponta"] = (string)x["ponta"], ["no"] = (string)x["no"] }).ToList();
                j.Grupo = ((string)grupo.Value.Key, (string)grupo.Value.Value["nome"] ?? grupo.Value.Key, membrosG.Select(x => (string)x["no"]).ToList());
                j.GrupoMembros = membrosG;
                j.AplicarNoGrupo = dadosG != null && membrosG.Count > 1;   // a placa já é do grupo: editar vale para todo o grupo
            }
            if (Capturas.Ativo) { placaAberta = j; placaComparar = (pe.M, pe.No); return; }
            j.ShowDialog();
            if (!j.Mudou || Esp != d) return;
            await GravarPlacaEsp(d, mb, j, membrosG);
        }

        // Grava o resultado da janela no projeto do grupo de ligações (fonte única). Remover = sem projeto (o nó sai do grupo
        // se o grupo tem outras bases).
        internal async Task GravarPlacaEsp(DocumentoEspacial d, JsonObject mb, JanelaPlaca j, List<JsonObject> membrosG)
        {
            string noId = (string)mb["no"];
            try
            {
                if (j.Placa == null)
                {
                    var g = GrupoDaBase(d, noId);
                    if (g == null) return;
                    string gid = g.Value.Key;
                    if (Lista(g.Value.Value["membros"]).Count() > 1)
                        gid = (string)(await d.Ligacoes("POST", "/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "dividir", ["grupo"] = gid, ["membros"] = new JsonArray(mb.DeepClone()) }))["grupo"];
                    await d.Ligacoes("POST", "/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = gid, ["tipo_ligacao"] = null });
                    alterado = true; AtualizarTitulo(); Log("Placa de base removida do nó " + noId + ".");
                    return;
                }
                var membros = new JsonArray(mb.DeepClone());
                foreach (var o in j.Outros.Where(o => j.AplicarEm.Contains(o.No))) membros.Add(o.Membro.DeepClone());
                if (j.AplicarNoGrupo && membrosG != null) foreach (var x in membrosG) membros.Add(x.DeepClone());
                var r = await d.Ligacoes("POST", "/api/placa_base/gravar", new JsonObject { ["membros"] = membros, ["dados"] = PlacaMotor.Dados(j.Placa), ["grupo_inteiro"] = j.AplicarNoGrupo });
                alterado = true; AtualizarTitulo();
                var nos = membros.Select(x => (string)x["no"]).Distinct().ToList();
                Log("Placa de base gravada no grupo de ligações " + (string)r?["grupo"] + " (" + string.Join(", ", nos) + ").");
            }
            catch (Exception ex) { Log("Placa de base: não gravou: " + PlacaMotor.Mensagem(ex)); MostrarToast("Placa de base não gravada: " + PlacaMotor.Mensagem(ex), true); }
        }

        // Membro {barra, ponta, no} do pé de pilar (uma só barra no nó) para /api/placa_base/*; null se não é pé.
        internal static JsonObject MembroDePe(DocumentoEspacial d, string no)
        {
            var bs = d.Json["barras"].AsObject().Where(kv => (string)kv.Value["ni"] == no || (string)kv.Value["nj"] == no).ToList();
            if (bs.Count != 1) return null;
            return new JsonObject { ["barra"] = bs[0].Key, ["ponta"] = (string)bs[0].Value["ni"] == no ? "i" : "j", ["no"] = no };
        }

        // Grupo de ligações (tipo base) que contém o nó, se o modelo já tem grupos identificados.
        static KeyValuePair<string, JsonNode>? GrupoDaBase(DocumentoEspacial d, string no)
        {
            foreach (var kv in d.Json["ligacoes"]?["grupos"] as JsonObject ?? new JsonObject())
                if ((string)kv.Value?["tipo"] == "base" && Lista(kv.Value["membros"]).Any(x => (string)x["no"] == no)) return new KeyValuePair<string, JsonNode>(kv.Key, kv.Value);
            return null;
        }
        internal JanelaPlaca placaAberta;   // capturas: a janela fica aberta para renderizar
        internal (Modelo, No) placaComparar;   // capturas: pé de pilar da última placa (JanelaComparar)

        // Anexo do relatório do motor: as placas de base dos grupos de ligações verificadas pelo motor (método NBR, pior
        // combinação ELU de todas as bases do grupo, dois eixos), uma tabela-resumo e as verificações do governante.
        internal async Task<Func<FlowDocument>> AnexoPlacasEsp()
        {
            var d = Esp;
            if (d == null) return () => null;
            await MigrarPlacasEsp(d);
            var linhas = new List<(string Grupo, PlacaBase P, ResNbr R, int N)>();
            foreach (var (gid, g) in GruposComPlaca(d).ToList())
            {
                var membros = new JsonArray(Lista(g["membros"]).Select(x => (JsonNode)new JsonObject { ["barra"] = (string)x["barra"], ["ponta"] = (string)x["ponta"] }).ToArray());
                if (membros.Count == 0) continue;
                ResNbr r;
                try { r = ResNbr.De(await Task.Run(() => d.Placa("/api/placa_base/verificar", new JsonObject { ["dados"] = g["projeto"]["dados"].DeepClone(), ["membros"] = membros }))); }
                catch (Exception ex) { r = ResNbr.ComErro(PlacaMotor.Mensagem(ex)); }
                linhas.Add(((string)g["nome"] ?? gid, PlacaMotor.Placa(g["projeto"]["dados"]), r, membros.Count));
            }
            return () => linhas.Count == 0 ? null : AnexoPlacasMotor(linhas);
        }

        FlowDocument AnexoPlacasMotor(List<(string Grupo, PlacaBase P, ResNbr R, int N)> linhas)
        {
            string F(double v, int c) => double.IsNaN(v) || double.IsInfinity(v) ? "—" : Num.Fmt(v, c);
            string Pc(double t) => double.IsNaN(t) || double.IsInfinity(t) ? "—" : Num.Fmt(t * 100, 0) + "%";
            var doc = new FlowDocument { FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 10.5, Foreground = TemaPlant.BImpTinta, Background = System.Windows.Media.Brushes.White };
            doc.Blocks.Add(Titulo("Anexo — Placas de base (motor, método NBR)"));
            doc.Blocks.Add(P("NBR 8800:2008 6.6.5 (pressão no concreto) com bloco retangular de compressão (AISC DG1), chapa pelo CBCA (conferência Pfeil/DG1), " +
                             "chumbador pré-concretado pela NBR 8800 6.3.3 + ACI 318-19 cap. 17 ou de catálogo; esforços de cálculo de todas as combinações ELU de todas as " +
                             "bases do grupo. Flexão biaxial: My no eixo forte e Mz no eixo fraco verificados separadamente (vale o pior; os momentos não são somados)."));
            doc.Blocks.Add(Tabela(new[] { "Grupo", "Bases", "Placa (mm)", "Fixação", "Governa", "Fixação", "Chapa", "Solda", "Situação" },
                linhas.Select(l => new[]
                {
                    l.Grupo, l.N.ToString(), F(l.P.H, 0) + " × " + F(l.P.B, 0) + (l.P.PeloPerfil ? " (perfil + sobra)" : "") + " × " + F(l.P.Esp, 1),
                    l.P.Fileiras * l.P.PorFileira + " × " + (string.IsNullOrEmpty(l.P.Chumbador) ? "Ø" + F(l.P.Diametro, 0) + " cl. " + l.P.Classe + " pré-concretado" : l.P.Chumbador),
                    l.R.Valido ? l.R.Comb + " · " + l.R.Barra + " · " + l.R.Eixo : l.R.Erro,
                    Pc(l.R.Fix.Taxa), Pc(l.R.Chapa.Taxa) + " (t ≥ " + F(l.R.TNec, 1) + ")", Pc(l.R.Solda.Taxa),
                    l.R.Valido && l.R.Ok ? "passa" : "NÃO PASSA"
                })));
            foreach (var l in linhas.Where(x => x.R.Valido))
            {
                doc.Blocks.Add(Titulo("Grupo " + l.Grupo + " — governante " + l.R.Comb + " (" + l.R.Barra + ", " + l.R.Eixo + ")"));
                doc.Blocks.Add(P("N = " + F(l.R.N, 2) + " kN · V = " + F(l.R.V, 2) + " kN · M = " + F(l.R.M, 2) + " kN·m · " + l.R.Modelo + " · Y = " + F(l.R.Y, 1) + " mm · " +
                                 "Tmáx = " + F(l.R.Tmax, 2) + " kN · Fv = " + F(l.R.Fv, 2) + " kN (" + l.R.Mecanismo + ") · chapa CBCA " + F(l.R.TNec, 1) + " mm, Pfeil " + F(l.R.TPfeil, 1) + " mm"));
                doc.Blocks.Add(Tabela(new[] { "Verificação", "Solicitante", "Resistente", "Unidade", "Taxa", "Item" },
                    Lista(l.R.R?["verificacoes"]).Select(v => new[] { S(v, "nome"), F(D(v, "solicitante"), 3), F(D(v, "resistente"), 3), S(v, "unidade"), Pc(D(v, "taxa")), S(v, "item") })));
                if (l.R.Avisos.Count > 0) doc.Blocks.Add(P("Avisos: " + string.Join(" · ", l.R.Avisos)));
            }
            return doc;
        }

        // ---------- Dimensionar no motor ----------
        // Para cada barra (ou só as escolhidas) com perfil do catálogo do motor: taxa > 1 sobe na família; taxa < 0,75 tenta
        // o mais leve com massa ≥ massa·taxa/0,9 (capacidade ~ massa, palpite). Recalcula e repete (até 8 vezes) até todas
        // passarem e nada mudar. Cada rodada = um PUT (um passo de desfazer).
        // ponytail: palpite por massa, não ótimo global; trocar por busca por barra se o motor ganhar "verificar com perfil X".
        internal async Task DimensionarEspacial(bool soSelecao)
        {
            var d = Esp; if (d == null) return;
            if (!await GarantirCalculoEsp()) return;
            JsonArray cat;
            try { cat = (await Motor.Get("/api/catalogo")).AsArray(); } catch (Exception ex) { ErroMotor(ex); return; }
            var info = cat.ToDictionary(x => (string)x["nome"], x => (Fam: (string)x["familia"], Massa: (double)(x["massa_kg_m"] ?? 0.0)));
            var porFam = info.GroupBy(kv => kv.Value.Fam).ToDictionary(g => g.Key, g => g.OrderBy(kv => kv.Value.Massa).Select(kv => kv.Key).ToList());
            var alvo = (soSelecao && vista3D.Selecao.Barras.Count > 0 ? vista3D.Selecao.Barras.ToList() : d.Json["barras"].AsObject().Select(kv => kv.Key).ToList())
                       .Where(b => d.Json["barras"][b] != null && info.ContainsKey((string)d.Json["barras"][b]["secao"])).ToList();
            if (alvo.Count == 0) { Log("Dimensionar: nenhuma barra com perfil do catálogo do motor."); return; }
            int trocas = 0;
            for (int it = 0; it < 8; it++)
            {
                var novo = new Dictionary<string, string>();
                foreach (string b in alvo)
                {
                    string s = (string)d.Json["barras"][b]["secao"];
                    double t = d.PiorPorBarra.TryGetValue(b, out var l) ? DocumentoEspacial.Taxa(l) : -1;
                    if (t < 0 || !info.TryGetValue(s, out var inf)) continue;
                    var fam = porFam[inf.Fam]; int i = fam.IndexOf(s);
                    string esc = null;
                    if (t > 1) esc = fam.Skip(i + 1).FirstOrDefault(x => info[x].Massa >= inf.Massa * t / 0.9) ?? (i + 1 < fam.Count ? fam[fam.Count - 1] : null);
                    else if (t < 0.75 && it < 4) esc = fam.Take(i).FirstOrDefault(x => info[x].Massa >= inf.Massa * t / 0.9);
                    if (esc != null && esc != s) novo[b] = esc;
                }
                if (novo.Count == 0) break;
                var secoes = new Dictionary<string, JsonNode>();
                try { foreach (string s in novo.Values.Distinct().Where(s => d.Json["secoes"]?[s] == null)) secoes[s] = (await Motor.Get("/api/catalogo/" + Uri.EscapeDataString(s)))["secao"]; }
                catch (Exception ex) { ErroMotor(ex); return; }
                await Espacial(dd => dd.Aplicar(m =>
                {
                    foreach (var kv in secoes) m["secoes"][kv.Key] = kv.Value.DeepClone();
                    foreach (var kv in novo) m["barras"][kv.Key]["secao"] = kv.Value;
                    return m;
                }), null);
                trocas += novo.Count;
                await CalcularEspacial();
                if (Esp != d || !d.Calculado) return;
            }
            int reprov = alvo.Count(b => d.PiorPorBarra.TryGetValue(b, out var l) && l["ok"] != null && !(bool)l["ok"]);
            Log("Dimensionar (motor): " + Pl.N(trocas, "troca de perfil", "trocas de perfil") + " em " + Pl.N(alvo.Count, "barra", "barras") +
                (reprov == 0 ? "; todas passam." : "; ⚠ " + Pl.N(reprov, "barra ainda não passa", "barras ainda não passam") + " (fim da família?)."));
        }
    }
}
