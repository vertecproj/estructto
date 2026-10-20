// Ponte das funções que nasceram no 2D (Nucleo.cs) para o documento 3D único (docs/MODELO-ESPACIAL.md, "Funções do 2D").
//
// Placa de base: a janela (JanelaPlaca) e o cálculo legado (PlacaCalc, o mesmo método de codigo/motor/.../placa_base.py)
// recebem um "pé de pilar" montado a partir do modelo 3D: o nó com apoio, a única barra que chega nele e os esforços da
// barra nesse nó vindos do MOTOR (N, Vz, My em cada combinação ELU). O pé vira um Modelo 2D mínimo (2 nós, 1 barra) no plano
// formado pelo eixo da barra e o eixo forte da seção; ResBarra é montada para reproduzir N, V e M exatos nas duas pontas.
// ponytail: só flexão no eixo forte (Mz/Vy ignorados, como a placa sempre foi); pilar inclinado com o eixo forte fora do
// plano vertical não é aceito. Quando o motor expuser a placa (placa_base.py por rota HTTP), trocar PlacaCalc por ela aqui.
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
        // Nós candidatos a pé de pilar: com apoio e uma só barra (ou que já têm placa).
        static IEnumerable<string> NosDePe(DocumentoEspacial d)
        {
            var cont = new Dictionary<string, int>();
            foreach (var kv in d.Json["barras"].AsObject()) foreach (string k in new[] { "ni", "nj" }) { string n = (string)kv.Value[k]; cont[n] = cont.TryGetValue(n, out int c) ? c + 1 : 1; }
            foreach (var kv in d.Json["nos"].AsObject())
                if (d.Placas.ContainsKey(kv.Key) || (cont.TryGetValue(kv.Key, out int c) && c == 1 && kv.Value["apoio"]?.AsArray().Any(x => x != null && (bool)x) == true)) yield return kv.Key;
        }

        // Garante o cálculo do documento ativo no motor (F5 se preciso). false = sem cálculo.
        internal async Task<bool> GarantirCalculoEsp()
        {
            var d = Esp; if (d == null) return false;
            if (!d.Calculado || Motor.Dono != d) await CalcularEspacial();
            return Esp == d && d.Calculado;
        }

        // Pés montados com os esforços de todas as combinações ELU (uma consulta por combinação).
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
                foreach (string c in combs)
                {
                    var r = await Motor.Get("/api/resultados?alvo=esforcos&combinacao=" + Uri.EscapeDataString(c) + "&ids=" + Uri.EscapeDataString(ids));
                    foreach (var p in ok) p.Ler(c, r?[p.BarraId]);
                }
            });
            foreach (var p in ok) if (p.PorComb.Count == 0) p.Erro = "O motor não devolveu esforços ELU da barra " + p.BarraId + ".";
            return pes;
        }

        // Pior combinação da placa do pé (maior aproveitamento de fixação ou chapa); deixa a barra com esse resultado.
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

        // Placa de base do nó (Ligações e bases › Placa de base, comando PLACA, duplo clique no nó com placa).
        internal async Task AbrirPlacaEsp(string noId)
        {
            var d = Esp; if (d == null) return;
            noId ??= vista3D.Selecao.Nos.FirstOrDefault() ?? d.Placas.Keys.FirstOrDefault() ?? NosDePe(d).FirstOrDefault();
            if (noId == null) { Log("Selecione o nó do pé do pilar (com apoio, uma só barra) e repita PLACA."); return; }
            if (!await GarantirCalculoEsp()) return;
            var pe = (await MontarPes(d, new[] { noId }))[0];
            if (pe.Erro != null) { Log("Placa de base no nó " + noId + ": " + pe.Erro); MostrarToast(pe.Erro, true); return; }
            d.Placas.TryGetValue(noId, out var orig);
            pe.No.Placa = orig?.Copiar();
            var (_, crit) = PiorPlaca(pe);
            JanelaPlaca j;
            try { j = new JanelaPlaca(pe.M, pe.No) { Owner = this }; }
            catch (Exception ex) { Log("Erro ao abrir a placa: " + ex.Message); return; }
            j.Title = "Placa de base — nó " + noId + (crit != null ? " — combinação crítica: " + crit : "");
            j.Candidatos = new List<No>();   // ponytail: "Aplicar também em" só no próprio nó; copiar a placa para outros nós pelo comando PLACA em cada um
            j.AvaliarEm = (no, pl) => { var p0 = no.Placa; no.Placa = pl; try { return PiorPlaca(pe).R; } finally { no.Placa = p0; } };
            if (Capturas.Ativo) { placaAberta = j; return; }
            j.ShowDialog();
            if (!j.Mudou || Esp != d) return;
            if (pe.No.Placa == null) d.Placas.Remove(noId); else d.Placas[noId] = pe.No.Placa.Copiar();
            alterado = true; AtualizarTitulo();
            Log(pe.No.Placa == null ? "Placa de base removida do nó " + noId + "." : "Placa de base do nó " + noId + " atualizada.");
        }
        internal JanelaPlaca placaAberta;   // capturas: a janela fica aberta para renderizar

        // Anexo do relatório do motor: tabela e memorial das placas (PlacaPior desviado para a ponte, nós com o número do motor).
        internal async Task<Func<FlowDocument>> AnexoPlacasEsp()
        {
            var d = Esp;
            if (d == null || d.Placas.Count == 0) return () => null;
            var pes = (await MontarPes(d, d.Placas.Keys.ToList())).Where(p => p.Erro == null).ToList();
            foreach (var p in pes) { p.No.Placa = d.Placas[p.NoId].Copiar(); p.No.Id = NumeroDe(p.NoId, p.No.Id); }
            return () =>
            {
                if (pes.Count == 0) return null;
                placaPiorDesvio = n => { var p = pes.First(x => x.No == n); return PiorPlaca(p).R ?? PlacaCalc.Calcular(p.M, n); };
                try { return AnexoDe(pes.Select(p => p.No).ToList()); }
                finally { placaPiorDesvio = null; }
            };
        }
        internal Func<No, ResPlaca> placaPiorDesvio;   // PlacaPior (Acoes.cs) usa este quando não é null

        static int NumeroDe(string id, int padrao) => id.Length > 1 && int.TryParse(id.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : padrao;

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
