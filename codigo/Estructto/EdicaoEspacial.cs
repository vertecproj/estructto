// Edição em lote no modelo espacial (docs/MODELO-ESPACIAL.md, "Editar a seleção"): mover, copiar, espelhar, rotacionar,
// matriz, dividir barra, unir nós coincidentes e propriedades em lote sobre uma seleção de nós e barras.
// GeometriaEdicao é pura: recebe o JSON do motor (ESPEC §5) e devolve o modelo novo. DocumentoEspacial.Aplicar manda o
// modelo inteiro num PUT /api/modelo (rota em MUTAM: uma entrada na pilha do motor, então um Ctrl+Z desfaz a operação toda).
// Painéis: janelas pequenas não modais (JanelaEscura: anima ao abrir), valores digitados; pontos = "x; y; z" digitados.
// Testes: Estructto.exe --testes-edicao <saida.txt> (TestesEdicao, sem motor).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    // Fonte da seleção (nós e barras do motor, por id).
    public interface IFonteSelecao
    {
        IReadOnlyList<string> Nos { get; }
        IReadOnlyList<string> Barras { get; }
        event Action Mudou;
    }

    // Transformação de um ponto e dos vetores (forças) que vão junto. Reflete: espelho (momento é pseudovetor: troca o sinal).
    public sealed class Transf
    {
        public Func<double[], double[]> P, V;
        public bool Reflete;
    }

    public static class GeometriaEdicao
    {
        public const double Tol = 0.001;   // m: nós a 1 mm ou menos são o mesmo nó

        // ---------- transformações ----------
        public static Transf Translacao(double dx, double dy, double dz) =>
            new Transf { P = p => new[] { p[0] + dx, p[1] + dy, p[2] + dz }, V = v => (double[])v.Clone() };

        // eixo 0/1/2 = plano X=c, Y=c, Z=c
        public static Transf Espelho(int eixo, double c) => new Transf
        {
            P = p => { var q = (double[])p.Clone(); q[eixo] = 2 * c - q[eixo]; return q; },
            V = v => { var q = (double[])v.Clone(); q[eixo] = -q[eixo]; return q; },
            Reflete = true,
        };

        // em torno do eixo vertical que passa por (cx, cy), ângulo em graus (anti-horário visto de cima)
        public static Transf Rotacao(double cx, double cy, double graus)
        {
            double a = graus * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            if (Math.Abs(c) < 1e-12) c = 0; if (Math.Abs(s) < 1e-12) s = 0;   // 90° exato
            return new Transf
            {
                P = p => new[] { cx + c * (p[0] - cx) - s * (p[1] - cy), cy + s * (p[0] - cx) + c * (p[1] - cy), p[2] },
                V = v => new[] { c * v[0] - s * v[1], s * v[0] + c * v[1], v[2] },
            };
        }

        // nx × ny × nz posições (a original incluída): devolve as nx·ny·nz − 1 translações das cópias
        public static List<Transf> Matriz(int nx, int ny, int nz, double ex, double ey, double ez)
        {
            var r = new List<Transf>();
            for (int k = 0; k < Math.Max(1, nz); k++) for (int j = 0; j < Math.Max(1, ny); j++) for (int i = 0; i < Math.Max(1, nx); i++)
                if (i + j + k > 0) r.Add(Translacao(i * ex, j * ey, k * ez));
            return r;
        }

        // ---------- operações (mudam e devolvem m) ----------

        // Move os nós da seleção (e das pontas das barras escolhidas). Nó que cai sobre outro (1 mm) funde com ele.
        public static JsonObject Mover(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras, Transf t)
        {
            var (sn, sb) = Selecao(m, nos, barras);
            var N = Col(m, "nos");
            var bs = Col(m, "barras");
            var levadas = bs.Where(kv => sn.Contains((string)kv.Value["ni"]) && sn.Contains((string)kv.Value["nj"])).Select(kv => kv.Key).ToHashSet();
            foreach (string id in levadas) GirarSecao(m, bs[id].AsObject(), Xyz(N[(string)bs[id]["ni"]]), Xyz(N[(string)bs[id]["nj"]]), t);   // antes de mover os nós
            foreach (string id in sn) Pos(N[id], t.P(Xyz(N[id])));
            foreach (var c in Lista(m, "cargas_nodais").Where(c => sn.Contains((string)c["no"]))) c["F"] = F6(c["F"], t);
            foreach (var c in Lista(m, "cargas_distribuidas").Concat(Lista(m, "cargas_pontuais")).Where(c => levadas.Contains((string)c["barra"]))) Vetores(c, t);
            foreach (string id in sn)
            {
                var alvo = Achar(N, Xyz(N[id]), x => !sn.Contains(x));
                if (alvo != null) Fundir(m, id, alvo);
            }
            Limpar(m);
            return m;
        }

        // Uma cópia da seleção por transformação. Barras copiadas levam seção, material, params e cargas, com rot e cardinal
        // transformados (GirarSecao); nós novos que
        // caem sobre nós existentes (1 mm) fundem com eles; barra que repetiria uma existente (mesmos nós) não é criada.
        public static JsonObject Copiar(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras, IEnumerable<Transf> ts)
        {
            var (sn, sb) = Selecao(m, nos, barras);
            var N = Col(m, "nos"); var B = Col(m, "barras");
            var cn = Lista(m, "cargas_nodais").ToList(); var cd = Lista(m, "cargas_distribuidas").ToList(); var cp = Lista(m, "cargas_pontuais").ToList();
            var idN = new Ids(N, "N"); var idB = new Ids(B, "B");
            foreach (var t in ts)
            {
                var mapa = new Dictionary<string, string>();
                foreach (string id in sn)
                {
                    var p = t.P(Xyz(N[id]));
                    string novo = Achar(N, p, _ => true);
                    if (novo == null)
                    {
                        novo = idN.Novo();
                        var o = N[id].DeepClone().AsObject(); o["id"] = novo; Pos(o, p);
                        N[novo] = o;
                        foreach (var c in cn.Where(c => (string)c["no"] == id)) Add(m, "cargas_nodais", c, x => { x["no"] = novo; x["F"] = F6(x["F"], t); });
                    }
                    mapa[id] = novo;
                }
                foreach (string id in sb)
                {
                    string ni = mapa[(string)B[id]["ni"]], nj = mapa[(string)B[id]["nj"]];
                    if (ni == nj || AcharBarra(B, ni, nj) != null) continue;
                    string novo = idB.Novo();
                    var o = B[id].DeepClone().AsObject(); o["id"] = novo; o["ni"] = ni; o["nj"] = nj;
                    GirarSecao(m, o, Xyz(N[(string)B[id]["ni"]]), Xyz(N[(string)B[id]["nj"]]), t);
                    B[novo] = o;
                    foreach (var c in cd.Concat(cp).Where(c => (string)c["barra"] == id))
                        Add(m, c.Parent == m["cargas_pontuais"] ? "cargas_pontuais" : "cargas_distribuidas", c, x => { x["barra"] = novo; Vetores(x, t); });
                }
            }
            return m;
        }

        // Divide cada barra em `partes` iguais, ou em duas a `distancia` (m) do nó inicial. Cargas distribuídas seguem
        // (trapezoidal reinterpolada); pontuais vão ao trecho onde caem. params "_i" ficam no 1º trecho e "_j" no último.
        public static JsonObject Dividir(JsonObject m, IEnumerable<string> barras, int partes, double distancia = 0)
        {
            var (_, sb) = Selecao(m, new string[0], barras);
            var N = Col(m, "nos"); var B = Col(m, "barras");
            var idN = new Ids(N, "N"); var idB = new Ids(B, "B");
            foreach (string id in sb)
            {
                var b = B[id].AsObject();
                var a = Xyz(N[(string)b["ni"]]); var c = Xyz(N[(string)b["nj"]]);
                double L = Dist(a, c);
                var ts = distancia > 0 ? new List<double> { distancia / L } : Enumerable.Range(1, partes - 1).Select(k => (double)k / partes).ToList();
                if (ts.Count == 0 || ts.Any(x => x <= Tol / L || x >= 1 - Tol / L)) throw new ArgumentException("barra " + id + ": ponto de divisão fora da barra (L = " + Num.Fmt(L, 3) + " m)");
                var cadeia = new List<string> { (string)b["ni"] };
                foreach (double x in ts)
                {
                    var p = new[] { a[0] + x * (c[0] - a[0]), a[1] + x * (c[1] - a[1]), a[2] + x * (c[2] - a[2]) };
                    string n = Achar(N, p, _ => true);
                    if (n == null) { n = idN.Novo(); N[n] = new JsonObject { ["id"] = n, ["x"] = p[0], ["y"] = p[1], ["z"] = p[2], ["apoio"] = new JsonArray(false, false, false, false, false, false) }; }
                    cadeia.Add(n);
                }
                cadeia.Add((string)b["nj"]);
                var cortes = new List<double> { 0 }; cortes.AddRange(ts); cortes.Add(1);
                var cd = Lista(m, "cargas_distribuidas").Where(x => (string)x["barra"] == id).ToList();
                var cp = Lista(m, "cargas_pontuais").Where(x => (string)x["barra"] == id).ToList();
                for (int k = 0; k + 1 < cadeia.Count; k++)
                {
                    string nova = idB.Novo();
                    var o = b.DeepClone().AsObject(); o["id"] = nova; o["ni"] = cadeia[k]; o["nj"] = cadeia[k + 1];
                    if (o["params"] is JsonObject pr)
                        foreach (string chave in pr.Select(kv => kv.Key).ToList())
                            if ((chave.EndsWith("_i") && k > 0) || (chave.EndsWith("_j") && k + 2 < cadeia.Count)) pr.Remove(chave);
                    B[nova] = o;
                    double t0 = cortes[k], t1 = cortes[k + 1];
                    foreach (var q in cd)
                        Add(m, "cargas_distribuidas", q, x =>
                        {
                            x["barra"] = nova;
                            if (q["q2"] is JsonArray q2) { x["q"] = Lerp(q["q"], q2, t0); x["q2"] = Lerp(q["q"], q2, t1); }
                        });
                    foreach (var q in cp)
                    {
                        double s = (double)q["a"] / L;
                        if (s < t0 || s > t1 || (s == t1 && k + 2 < cadeia.Count)) continue;
                        Add(m, "cargas_pontuais", q, x => { x["barra"] = nova; x["a"] = (s - t0) * L; });
                    }
                }
                RemoverBarra(m, id);
            }
            return m;
        }

        // Funde os nós a `tol` (m) ou menos um do outro (só entre os escolhidos; nada escolhido = todos). Fica o primeiro;
        // apoios somam; barras que zeram ou repetem somem com as cargas.
        public static JsonObject UnirNos(JsonObject m, IEnumerable<string> nos, double tol = Tol)
        {
            var N = Col(m, "nos");
            var l = (nos?.Any() == true ? nos : N.Select(kv => kv.Key)).Where(N.ContainsKey).ToList();
            // ponytail: O(n²) entre os nós escolhidos; grade espacial se modelos de dezenas de milhares de nós ficarem lentos
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                    if (N.ContainsKey(l[i]) && N.ContainsKey(l[j]) && Dist(Xyz(N[l[i]]), Xyz(N[l[j]])) <= tol) Fundir(m, l[j], l[i]);
            Limpar(m);
            return m;
        }

        // null = não muda. rot em graus. apoio: 6 GL (ux, uy, uz, rx, ry, rz).
        public static JsonObject Propriedades(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras,
                                              string secao, string material, double? rotGraus, bool[] apoio)
        {
            var (sn, sb) = SelecaoExata(m, nos, barras);
            var B = Col(m, "barras");
            if (secao != null && Col(m, "secoes")[secao] == null) throw new ArgumentException("seção " + secao + " não está no modelo");
            if (material != null && Col(m, "materiais")[material] == null) throw new ArgumentException("material " + material + " não está no modelo");
            foreach (string id in sb)
            {
                var b = B[id];
                if (secao != null) b["secao"] = secao;
                if (rotGraus != null) b["rot"] = rotGraus.Value * Math.PI / 180;
                if (material != null)
                {
                    b["material"] = material;
                    bool concreto = m["materiais"][material]?["fck"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number;
                    if (concreto) b["tipo"] = "concreto"; else if ((string)b["tipo"] == "concreto") b["tipo"] = "aco";
                }
            }
            if (apoio != null) foreach (string id in sn) Col(m, "nos")[id]["apoio"] = new JsonArray(apoio.Select(x => (JsonNode)x).ToArray());
            return m;
        }

        // Apoio (6 GL; null = mantém) e molas (kN/m, kN·m/rad, globais; null = mantém; todas 0 = tira) só dos nós dados.
        // GL com mola fica livre: a mola é o apoio desse GL (no motor, mola em GL travado não faz nada).
        public static JsonObject Apoio(JsonObject m, IEnumerable<string> nos, bool[] apoio, double[] molas)
        {
            var N = Col(m, "nos");
            foreach (string id in (nos ?? new string[0]).Where(N.ContainsKey))
            {
                var n = N[id].AsObject();
                var ap = apoio ?? (n["apoio"] as JsonArray)?.Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                if (molas != null)
                {
                    if (molas.All(k => k == 0)) n.Remove("molas");
                    else { n["molas"] = Arr(molas); ap = ap.Select((a, i) => a && molas[i] == 0).ToArray(); }
                }
                n["apoio"] = new JsonArray(ap.Select(x => (JsonNode)x).ToArray());
            }
            return m;
        }

        // Rótulas nas duas pontas (params.rotula_i/_j) das barras dadas: ligar põe, desligar tira.
        public static JsonObject Rotulas(JsonObject m, IEnumerable<string> barras, bool ligar)
        {
            var B = Col(m, "barras");
            foreach (string id in (barras ?? new string[0]).Where(B.ContainsKey))
            {
                var b = B[id].AsObject();
                var p = b["params"] as JsonObject ?? (JsonObject)(b["params"] = new JsonObject());
                if (ligar) { p["rotula_i"] = true; p["rotula_j"] = true; } else { p.Remove("rotula_i"); p.Remove("rotula_j"); }
            }
            return m;
        }

        // Ponto cardinal (1..10; 5 = centro, sai de params) só das barras dadas.
        public static JsonObject Cardinal(JsonObject m, IEnumerable<string> barras, int cardinal)
        {
            if (cardinal < 1 || cardinal > 10) throw new ArgumentException("cardinal " + cardinal + ": use 1 a 9 (teclado numérico) ou 10 (centroide)");
            var B = Col(m, "barras");
            foreach (string id in (barras ?? new string[0]).Where(B.ContainsKey))
            {
                var b = B[id].AsObject();
                var p = b["params"] as JsonObject ?? (JsonObject)(b["params"] = new JsonObject());
                if (cardinal == 5) p.Remove("cardinal"); else p["cardinal"] = cardinal;
            }
            return m;
        }

        // ---------- giro da seção (rot) e ponto cardinal sob a transformação ----------

        static double[] Cr(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static double Dt(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        // Eixos locais do motor (solver._rotacao, rot = 0): x = ni→nj; y = Z × x (barra vertical: +Y global); z = x × y.
        // A Vista3D desenha a seção em (u, v) = (y, z) locais do motor (GeometriaPerfis, Modelo3D.EixoY). null = comprimento nulo.
        public static (double[] X, double[] Y, double[] Z)? Triedro(double[] a, double[] b)
        {
            double[] x = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }; double l = Math.Sqrt(Dt(x, x));
            if (l < 1e-12) return null;
            for (int i = 0; i < 3; i++) x[i] /= l;
            double h = Math.Sqrt(x[0] * x[0] + x[1] * x[1]);
            double[] y = h < 1e-9 ? new double[] { 0, 1, 0 } : new[] { -x[1] / h, x[0] / h, 0 };
            return (x, y, Cr(x, y));
        }

        // rot (rad) e params.cardinal da barra levada por t (a, b = pontas antes). Giro próprio (rotação, translação): o eixo
        // local y vai junto (t.V) — barra horizontal não muda; vertical soma o ângulo com o sinal de ni→nj. Espelho (det −1):
        // o triedro do motor é direito e não pode espelhar; fica z' = M·z e y' = −M·y, ou seja a seção desenhada sai com
        // u → −u, que é o espelho exato de I, caixão, tubo e retângulo (simétricos em u). U (simétrico em v) leva +180° e L
        // +90° para a forma voltar ao espelho: exato no U e na cantoneira de abas iguais (abas desiguais trocam de lado:
        // ponytail). O cardinal acompanha (u → −u troca 1↔3, 4↔6, 7↔9; o giro extra gira a caixa).
        public static void GirarSecao(JsonObject m, JsonObject barra, double[] a, double[] b, Transf t)
        {
            var e0 = Triedro(a, b); var e1 = Triedro(t.P(a), t.P(b));
            if (e0 == null || e1 == null) return;
            double rot = barra["rot"] is JsonValue rv && rv.TryGetValue(out double r0) ? r0 : 0;
            var (_, y0, z0) = e0.Value; var (_, y1, z1) = e1.Value;
            double c = Math.Cos(rot), s = Math.Sin(rot);
            double[] y = { c * y0[0] + s * z0[0], c * y0[1] + s * z0[1], c * y0[2] + s * z0[2] };
            double[] yt = t.V(y);
            int card = DocumentoEspacial.CardinalDe(barra), novoCard = card;
            if (t.Reflete)
            {
                yt = yt.Select(v => -v).ToArray();
                string forma = m["secoes"]?[(string)barra["secao"] ?? ""]?["props"]?["tipo"] is JsonValue fv && fv.TryGetValue(out string fs) ? fs : null;
                double extra = forma == "U" || forma == "Ue" ? Math.PI : forma == "L" ? Math.PI / 2 : 0;
                var (su, sv) = CardinalSinais(card);
                double eu = -su, ev = sv;   // u → −u
                if (extra == Math.PI) { eu = -eu; ev = -ev; } else if (extra > 0) { (eu, ev) = (ev, -eu); }   // caixa no triedro girado de −extra
                if (card != 10) novoCard = CardinalDe((int)eu, (int)ev);
                yt = Girado(yt, e1.Value.X, extra);
            }
            double r = Math.Atan2(Dt(yt, z1), Dt(yt, y1));
            double dr = Math.IEEERemainder(r - rot, 2 * Math.PI);
            if (Math.Abs(dr) > 1e-12) { r = Math.IEEERemainder(r, 2 * Math.PI); barra["rot"] = Math.Abs(r) < 1e-12 ? 0.0 : Math.Round(r, 12); }
            if (novoCard != card)
            {
                var p = barra["params"] as JsonObject ?? (JsonObject)(barra["params"] = new JsonObject());
                if (novoCard == 5) p.Remove("cardinal"); else p["cardinal"] = novoCard;
            }
        }

        // y girado de ang em torno do eixo x (regra da mão direita): y·cos + (x × y)·sen.
        static double[] Girado(double[] y, double[] x, double ang)
        {
            if (ang == 0) return y;
            var z = Cr(x, y); double c = Math.Cos(ang), s = Math.Sin(ang);
            return new[] { c * y[0] + s * z[0], c * y[1] + s * z[1], c * y[2] + s * z[2] };
        }

        // Cardinal 1..9 ↔ sinais (su, sv) na caixa da seção (GeometriaPerfis.Cardinal: 1/4/7 = lado +u, 1/2/3 = base, −v).
        static (int su, int sv) CardinalSinais(int k) { int col = (k - 1) % 3, lin = (k - 1) / 3; return (col == 0 ? 1 : col == 1 ? 0 : -1, lin == 0 ? -1 : lin == 1 ? 0 : 1); }
        static int CardinalDe(int su, int sv) => (sv == -1 ? 0 : sv == 0 ? 1 : 2) * 3 + (su == 1 ? 0 : su == 0 ? 1 : 2) + 1;

        // ---------- auxiliares ----------

        // Seleção vazia = modelo inteiro. Nós = os escolhidos + as pontas das barras escolhidas.
        public static (HashSet<string> nos, List<string> barras) Selecao(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras)
        {
            var B = Col(m, "barras"); var N = Col(m, "nos");
            var bl = (barras ?? new string[0]).Where(B.ContainsKey).ToList();
            var nl = (nos ?? new string[0]).Where(N.ContainsKey).ToList();
            if (bl.Count == 0 && nl.Count == 0) { bl = B.Select(kv => kv.Key).ToList(); nl = N.Select(kv => kv.Key).ToList(); }
            var sn = new HashSet<string>(nl);
            foreach (string b in bl) { sn.Add((string)B[b]["ni"]); sn.Add((string)B[b]["nj"]); }
            return (sn, bl);
        }

        // Para propriedades: só o que foi escolhido (sem as pontas), vazio = tudo.
        static (List<string>, List<string>) SelecaoExata(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras)
        {
            var nl = (nos ?? new string[0]).Where(Col(m, "nos").ContainsKey).ToList();
            var bl = (barras ?? new string[0]).Where(Col(m, "barras").ContainsKey).ToList();
            if (nl.Count == 0 && bl.Count == 0) { nl = Col(m, "nos").Select(kv => kv.Key).ToList(); bl = Col(m, "barras").Select(kv => kv.Key).ToList(); }
            return (nl, bl);
        }

        sealed class Ids
        {
            readonly JsonObject col; readonly string pref; int k;
            public Ids(JsonObject col, string pref) { this.col = col; this.pref = pref; }
            public string Novo() { string s; do s = pref + (++k); while (col.ContainsKey(s)); return s; }
        }

        static JsonObject Col(JsonObject m, string k) => m[k] as JsonObject ?? (JsonObject)(m[k] = new JsonObject());
        static IEnumerable<JsonObject> Lista(JsonObject m, string k) => (m[k] as JsonArray ?? new JsonArray()).OfType<JsonObject>();
        public static double[] Xyz(JsonNode n) => new[] { (double)n["x"], (double)n["y"], (double)n["z"] };
        static void Pos(JsonNode n, double[] p) { n["x"] = Math.Round(p[0], 9); n["y"] = Math.Round(p[1], 9); n["z"] = Math.Round(p[2], 9); }
        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        static double[] Vet(JsonNode a) => a.AsArray().Select(x => (double)x).ToArray();
        static JsonArray Arr(IEnumerable<double> v) => new JsonArray(v.Select(x => (JsonNode)Math.Round(x, 12)).ToArray());
        static JsonArray Lerp(JsonNode a, JsonNode b, double t) { var x = Vet(a); var y = Vet(b); return Arr(x.Select((v, i) => v + t * (y[i] - v))); }

        // ponytail: busca linear (O(n) por nó novo); grade espacial se a cópia de modelos enormes ficar lenta
        static string Achar(JsonObject N, double[] p, Func<string, bool> aceita)
        {
            foreach (var kv in N) if (aceita(kv.Key) && Dist(Xyz(kv.Value), p) <= Tol) return kv.Key;
            return null;
        }

        static string AcharBarra(JsonObject B, string a, string b)
        {
            foreach (var kv in B)
            {
                string i = (string)kv.Value["ni"], j = (string)kv.Value["nj"];
                if ((i == a && j == b) || (i == b && j == a)) return kv.Key;
            }
            return null;
        }

        // Força nodal (Fx, Fy, Fz, Mx, My, Mz): forças pela transformação; momento é pseudovetor (espelho troca o sinal).
        static JsonArray F6(JsonNode f, Transf t)
        {
            var v = Vet(f);
            var r = t.V(v.Take(3).ToArray()).ToList();
            if (v.Length >= 6) r.AddRange(t.V(v.Skip(3).Take(3).ToArray()).Select(x => t.Reflete ? -x : x));
            return Arr(r);
        }

        static void Vetores(JsonObject c, Transf t)
        {
            foreach (string k in new[] { "q", "q2", "F" }) if (c[k] is JsonArray a) c[k] = Arr(t.V(Vet(a)));
        }

        // Cópia de uma carga (sem id: o motor numera) no fim da lista.
        static void Add(JsonObject m, string lista, JsonObject c, Action<JsonObject> mudar)
        {
            var x = c.DeepClone().AsObject(); x.Remove("id"); mudar(x);
            (m[lista] as JsonArray ?? (JsonArray)(m[lista] = new JsonArray())).Add(x);
        }

        // `de` some; barras e cargas nodais passam a `para`; apoios somam (ou lógico por GL).
        static void Fundir(JsonObject m, string de, string para)
        {
            var N = Col(m, "nos");
            if (N[de]?["apoio"] is JsonArray ad && N[para]["apoio"] is JsonArray ap)
                for (int i = 0; i < Math.Min(ad.Count, ap.Count); i++) ap[i] = (bool)ap[i] || (bool)ad[i];
            foreach (var kv in Col(m, "barras")) foreach (string k in new[] { "ni", "nj" }) if ((string)kv.Value[k] == de) kv.Value[k] = para;
            foreach (var c in Lista(m, "cargas_nodais")) if ((string)c["no"] == de) c["no"] = para;
            N.Remove(de);
        }

        // Barras de comprimento nulo ou repetidas (mesmo par de nós) somem com as cargas.
        static void Limpar(JsonObject m)
        {
            var vistos = new HashSet<string>();
            foreach (var kv in Col(m, "barras").ToList())
            {
                string i = (string)kv.Value["ni"], j = (string)kv.Value["nj"];
                string par = string.CompareOrdinal(i, j) < 0 ? i + "\n" + j : j + "\n" + i;
                if (i == j || !vistos.Add(par)) RemoverBarra(m, kv.Key);
            }
        }

        static void RemoverBarra(JsonObject m, string id)
        {
            Col(m, "barras").Remove(id);
            foreach (string l in new[] { "cargas_distribuidas", "cargas_pontuais" })
                if (m[l] is JsonArray a) foreach (var c in a.OfType<JsonObject>().Where(c => (string)c["barra"] == id).ToList()) a.Remove(c);
        }

        // ---------- comandos CAD (Comandos3D.cs) ----------

        // Apaga as barras e os nós escolhidos (nó leva as barras ligadas). Pontas que ficaram sem barra somem também; cargas vão junto.
        public static JsonObject Apagar(JsonObject m, IEnumerable<string> nos, IEnumerable<string> barras)
        {
            var B = Col(m, "barras"); var N = Col(m, "nos");
            var sn = new HashSet<string>((nos ?? new string[0]).Where(N.ContainsKey));
            var sb = new HashSet<string>((barras ?? new string[0]).Where(B.ContainsKey));
            foreach (var kv in B) if (sn.Contains((string)kv.Value["ni"]) || sn.Contains((string)kv.Value["nj"])) sb.Add(kv.Key);
            var pontas = sb.SelectMany(b => new[] { (string)B[b]["ni"], (string)B[b]["nj"] }).ToHashSet();
            foreach (string b in sb) RemoverBarra(m, b);
            var usados = B.SelectMany(kv => new[] { (string)kv.Value["ni"], (string)kv.Value["nj"] }).ToHashSet();
            foreach (string n in sn.Concat(pontas.Where(p => !usados.Contains(p))).ToList()) RemoverNo(m, n);
            return m;
        }

        static void RemoverNo(JsonObject m, string id)
        {
            Col(m, "nos").Remove(id);
            if (m["cargas_nodais"] is JsonArray a) foreach (var c in a.OfType<JsonObject>().Where(c => (string)c["no"] == id).ToList()) a.Remove(c);
        }

        // Leva a ponta `no` da barra até p (aparar/estender). Nó só desta barra = move o nó; nó compartilhado = a barra ganha
        // nó novo (as outras barras ficam). Nó existente a 1 mm de p = reaproveitado.
        public static JsonObject MoverPonta(JsonObject m, string barra, string no, double[] p)
        {
            var B = Col(m, "barras"); var N = Col(m, "nos");
            string k = (string)B[barra]["ni"] == no ? "ni" : "nj";
            string existe = Achar(N, p, x => x != no);
            bool soDela = B.Count(kv => kv.Key != barra && ((string)kv.Value["ni"] == no || (string)kv.Value["nj"] == no)) == 0;
            if (existe != null) { B[barra][k] = existe; if (soDela) RemoverNo(m, no); }
            else if (soDela) Pos(N[no], p);
            else
            {
                string novo = new Ids(N, "N").Novo();
                var o = new JsonObject { ["id"] = novo }; Pos(o, p); N[novo] = o;
                B[barra][k] = novo;
            }
            Limpar(m);
            return m;
        }

        // Retas AB e CD (infinitas): ponto de AB mais próximo de CD e o parâmetro t em AB, se as retas passam a até `tol` uma
        // da outra. null = paralelas ou reversas além da tolerância.
        public static (double[] P, double T)? Cruzamento(double[] a, double[] b, double[] c, double[] d, double tol = 0.01)
        {
            double[] u = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, v = { d[0] - c[0], d[1] - c[1], d[2] - c[2] }, w = { a[0] - c[0], a[1] - c[1], a[2] - c[2] };
            double Dot(double[] x, double[] y) => x[0] * y[0] + x[1] * y[1] + x[2] * y[2];
            double uu = Dot(u, u), uv = Dot(u, v), vv = Dot(v, v), uw = Dot(u, w), vw = Dot(v, w), den = uu * vv - uv * uv;
            if (uu < 1e-18 || vv < 1e-18 || Math.Abs(den) < 1e-12 * uu * vv) return null;
            double s = (uv * vw - vv * uw) / den, t = (uu * vw - uv * uw) / den;
            double[] p = { a[0] + s * u[0], a[1] + s * u[1], a[2] + s * u[2] }, q = { c[0] + t * v[0], c[1] + t * v[1], c[2] + t * v[2] };
            return Dist(p, q) <= tol ? (p, s) : ((double[], double)?)null;
        }

        // Espelho no plano vertical que contém a reta (a, b) da planta (MI do AutoCAD).
        public static Transf EspelhoReta(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l = Math.Sqrt(dx * dx + dy * dy); dx /= l; dy /= l;
            double[] R(double x, double y) { double t = x * dx + y * dy; return new[] { 2 * t * dx - x, 2 * t * dy - y }; }
            return new Transf
            {
                P = p => { var r = R(p[0] - ax, p[1] - ay); return new[] { ax + r[0], ay + r[1], p[2] }; },
                V = v => { var r = R(v[0], v[1]); return new[] { r[0], r[1], v[2] }; },
                Reflete = true,
            };
        }

        // "0,@6,@6,@7.5" (grade estrutural, sintaxe do Plant 3D): vírgula separa, @ = relativo ao anterior, ponto decimal.
        public static double[] LerCotas(string s)
        {
            var r = new List<double>();
            foreach (string t0 in (s ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = t0.Trim(); bool rel = t.StartsWith("@"); if (rel) t = t.Substring(1);
                if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
                    throw new FormatException("valor inválido: " + t0 + " (use 0,@6,@6 com ponto decimal)");
                r.Add(rel && r.Count > 0 ? r[^1] + v : v);
            }
            return r.ToArray();
        }
    }

    // ================= documento: uma operação = um PUT /api/modelo = um desfazer =================
    public partial class DocumentoEspacial
    {
        public Task Aplicar(Func<JsonObject, JsonObject> op) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            var novo = op(JsonNode.Parse(Json.ToJsonString()).AsObject());
            await Motor.Chamar("PUT", "/api/modelo", novo);
            Motor.Dono = this;   // PUT troca o modelo e zera o dono: é o mesmo documento
            TemHistorico = true;
            await Recarregar();
        });
    }

    // Seleção múltipla da Vista3D (clique, janela, cruzamento, árvore e tabelas): ids do motor. Nada escolhido = modelo inteiro.
    sealed class FonteSelecaoEsp : IFonteSelecao
    {
        readonly Vista3D v;
        public FonteSelecaoEsp(MainWindow w) { v = w.vista3D; v.SelecaoMudou += () => Mudou?.Invoke(); }
        public IReadOnlyList<string> Nos => v.Selecao.Nos.ToList();
        public IReadOnlyList<string> Barras => v.Selecao.Barras.ToList();
        public event Action Mudou;
    }

    // Painel pequeno não modal (flutuante): campos digitados + Aplicar. Fica aberto para repetir a operação.
    public class PainelEdicao : JanelaEscura
    {
        public readonly Dictionary<string, Control> Campos = new Dictionary<string, Control>();
        readonly StackPanel sp = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        readonly TextBlock lblSel = T("", 11, BFraco), erro = T("", 11.5, BVermelho);
        readonly IFonteSelecao fonte;
        readonly Func<PainelEdicao, Task> aplicar;

        public PainelEdicao(string titulo, string ajuda, IFonteSelecao fonte, Func<PainelEdicao, Task> aplicar)
        {
            this.fonte = fonte; this.aplicar = aplicar;
            Title = titulo; Width = 340; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            sp.Children.Add(T(titulo, 13.5, BTexto, FontWeights.SemiBold));
            sp.Children.Add(lblSel);
            var aj = T(ajuda, 11, BFraco); aj.Margin = new Thickness(0, 4, 0, 4); sp.Children.Add(aj);
            Content = sp;
            Loaded += (s, e) =>
            {
                erro.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(erro);
                var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                bts.Children.Add(B("Fechar", Close));
                bts.Children.Add(B("Aplicar", () => _ = Executar(), true, "Aplica à seleção (uma operação: Ctrl+Z desfaz tudo)."));
                sp.Children.Add(bts);
            };
            Action at = () => lblSel.Text = Resumo(fonte);
            at(); fonte.Mudou += at; Closed += (s, e) => fonte.Mudou -= at;
        }

        public static string Resumo(IFonteSelecao f) => f.Nos.Count + f.Barras.Count == 0 ? "Seleção: modelo inteiro (nada escolhido)"
            : "Seleção: " + (f.Nos.Count > 0 ? f.Nos.Count + (f.Nos.Count == 1 ? " nó " : " nós ") + string.Join(", ", f.Nos.Take(4)) : "")
              + (f.Nos.Count > 0 && f.Barras.Count > 0 ? " · " : "") + (f.Barras.Count > 0 ? f.Barras.Count + (f.Barras.Count == 1 ? " barra " : " barras ") + string.Join(", ", f.Barras.Take(4)) : "");

        public TextBox Campo(string chave, string rotulo, string valor, string dica = null) { var c = Campo(valor, 150, dica); Linha(chave, rotulo, c); return c; }

        public ComboBox Lista(string chave, string rotulo, IEnumerable<string> itens, int sel = 0)
        {
            var c = new ComboBox { Width = 150 };
            foreach (string i in itens) c.Items.Add(i);
            c.SelectedIndex = Math.Min(sel, c.Items.Count - 1);
            Linha(chave, rotulo, c); return c;
        }

        void Linha(string chave, string rotulo, Control c)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var t = T(rotulo, 12, BFraco); t.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(t);
            c.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(c, 1); g.Children.Add(c);
            Campos[chave] = c; sp.Children.Add(g);
        }

        public string Texto(string k) => Campos[k] is ComboBox cb ? cb.SelectedItem as string ?? cb.Text : ((TextBox)Campos[k]).Text.Trim();
        public bool Vazio(string k) => Texto(k) == "";
        public double D(string k) => Num.Ler(Texto(k), out double v) ? v : throw new FormatException(Rotulo(k) + ": número inválido");
        public int I(string k) => int.TryParse(Texto(k), out int v) && v >= 0 ? v : throw new FormatException(Rotulo(k) + ": inteiro ≥ 0");
        // ponto digitado "x; y; z" (ou separado por espaços); null se vazio
        public double[] Ponto(string k, int n = 3)
        {
            if (Vazio(k)) return null;
            var p = Texto(k).Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var r = new double[n];
            if (p.Length != n) throw new FormatException(Rotulo(k) + ": digite " + (n == 3 ? "x; y; z" : "x; y"));
            for (int i = 0; i < n; i++) if (!Num.Ler(p[i], out r[i])) throw new FormatException(Rotulo(k) + ": número inválido");
            return r;
        }
        string Rotulo(string k) => ((TextBlock)((Grid)Campos[k].Parent).Children[0]).Text;

        public async Task Executar()
        {
            erro.Text = "";
            try { await aplicar(this); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { erro.Text = ex.Message; }
        }
    }

    // ================= janela principal: comandos de edição do modelo espacial =================
    public partial class MainWindow
    {
        IFonteSelecao fonteEsp;
        PainelEdicao painelEdicao;
        internal PainelEdicao PainelEdicaoAberto => painelEdicao;

        void MostrarEdicao3D(bool esp) { if (!esp) painelEdicao?.Close(); }

        internal void AbrirEdicaoEsp(string op)
        {
            var d0 = Esp;
            if (d0 == null) { MostrarToast("Disponível no modelo espacial (Gerar 3D ou abrir um .json do motor)."); return; }
            fonteEsp ??= new FonteSelecaoEsp(this);
            var f = fonteEsp;
            painelEdicao?.Close();
            PainelEdicao p = null;
            // roda a operação no documento ativo: um PUT /api/modelo
            Func<string, Func<JsonObject, JsonObject>, Task> rodar = (msg, g) => Espacial(d => d.Aplicar(g), msg + " (" + PainelEdicao.Resumo(f).Replace("Seleção: ", "") + ")");
            var nos = (Func<IReadOnlyList<string>>)(() => f.Nos); var bs = (Func<IReadOnlyList<string>>)(() => f.Barras);
            Func<PainelEdicao, double[]> vetor = x =>
            {
                var a = x.Ponto("base"); var b = x.Ponto("destino");
                if ((a == null) != (b == null)) throw new FormatException("preencha o ponto base e o ponto destino, ou deixe os dois vazios");
                return a != null ? new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] } : new[] { x.D("dx"), x.D("dy"), x.D("dz") };
            };
            void Vetor(PainelEdicao x) { x.Campo("dx", "dx (m)", "0"); x.Campo("dy", "dy (m)", "0"); x.Campo("dz", "dz (m)", "0"); x.Campo("base", "ou ponto base", "", "x; y; z (m). Com o destino, substitui dx, dy, dz."); x.Campo("destino", "ponto destino", "", "x; y; z (m)"); }
            switch (op)
            {
                case "mover":
                    p = new PainelEdicao("Mover", "Desloca nós e barras. Nó que cair sobre outro (1 mm) se funde com ele.", f, async x =>
                    { var v = vetor(x); await rodar("Movido", m => GeometriaEdicao.Mover(m, nos(), bs(), GeometriaEdicao.Translacao(v[0], v[1], v[2]))); });
                    Vetor(p); break;
                case "copiar":
                    p = new PainelEdicao("Copiar", "Cópias em fila (matriz linear), com seção, material, params e cargas. Nós novos se fundem aos existentes (1 mm).", f, async x =>
                    {
                        var v = vetor(x); int n = x.I("n"); if (n < 1) throw new FormatException("Cópias: ao menos 1");
                        await rodar("Copiado " + n + "×", m => GeometriaEdicao.Copiar(m, nos(), bs(), Enumerable.Range(1, n).Select(k => GeometriaEdicao.Translacao(k * v[0], k * v[1], k * v[2]))));
                    });
                    Vetor(p); p.Campo("n", "Cópias", "1"); break;
                case "espelhar":
                    p = new PainelEdicao("Espelhar", "Cópia espelhada em relação a um plano X = a, Y = b ou Z = c. O giro (rot) e o cardinal se ajustam para a seção sair espelhada (U, L).", f, async x =>
                    {
                        int e = Array.IndexOf(new[] { "X", "Y", "Z" }, x.Texto("plano")); double c = x.D("valor");
                        await rodar("Espelhado em " + x.Texto("plano") + " = " + Num.Fmt(c, 3), m => GeometriaEdicao.Copiar(m, nos(), bs(), new[] { GeometriaEdicao.Espelho(e, c) }));
                    });
                    p.Lista("plano", "Plano", new[] { "X", "Y", "Z" }); p.Campo("valor", "Valor (m)", "0"); break;
                case "rotacionar":
                    p = new PainelEdicao("Rotacionar", "Em torno do eixo vertical que passa pelo ponto. Ângulo anti-horário visto de cima. Cópias = 0 gira a seleção.", f, async x =>
                    {
                        var c = x.Ponto("centro", 2) ?? new double[2]; double a = x.D("ang"); int n = x.I("n");
                        if (n == 0) await rodar("Rotacionado " + Num.Fmt(a, 1) + "°", m => GeometriaEdicao.Mover(m, nos(), bs(), GeometriaEdicao.Rotacao(c[0], c[1], a)));
                        else await rodar("Cópia polar " + n + "× " + Num.Fmt(a, 1) + "°", m => GeometriaEdicao.Copiar(m, nos(), bs(), Enumerable.Range(1, n).Select(k => GeometriaEdicao.Rotacao(c[0], c[1], k * a))));
                    });
                    p.Campo("centro", "Centro (x; y)", "0; 0", "Ponto do eixo vertical (m)."); p.Campo("ang", "Ângulo (°)", "90"); p.Campo("n", "Cópias", "0"); break;
                case "matriz":
                    p = new PainelEdicao("Matriz retangular", "nx × ny × nz posições contando a original; espaçamentos em m.", f, async x =>
                    {
                        int nx = Math.Max(1, x.I("nx")), ny = Math.Max(1, x.I("ny")), nz = Math.Max(1, x.I("nz"));
                        var ts = GeometriaEdicao.Matriz(nx, ny, nz, x.D("ex"), x.D("ey"), x.D("ez"));
                        if (ts.Count == 0) throw new FormatException("nx × ny × nz precisa ser maior que 1");
                        await rodar("Matriz " + nx + "×" + ny + "×" + nz, m => GeometriaEdicao.Copiar(m, nos(), bs(), ts));
                    });
                    p.Campo("nx", "nx", "2"); p.Campo("ny", "ny", "1"); p.Campo("nz", "nz", "1");
                    p.Campo("ex", "Espaçamento X (m)", "6"); p.Campo("ey", "Espaçamento Y (m)", "6"); p.Campo("ez", "Espaçamento Z (m)", "3"); break;
                case "dividir":
                    p = new PainelEdicao("Dividir barra", "Em partes iguais ou, com a distância preenchida, em duas a essa distância do nó inicial.", f, async x =>
                    {
                        double dist = x.Vazio("dist") ? 0 : x.D("dist"); int n = dist > 0 ? 2 : x.I("n");
                        if (dist <= 0 && n < 2) throw new FormatException("Partes: ao menos 2");
                        await rodar(dist > 0 ? "Dividido a " + Num.Fmt(dist, 3) + " m" : "Dividido em " + n, m => GeometriaEdicao.Dividir(m, bs(), n, dist));
                    });
                    p.Campo("n", "Partes", "2"); p.Campo("dist", "ou distância (m)", "", "A partir do nó inicial (ni)."); break;
                case "unir":
                    p = new PainelEdicao("Unir nós coincidentes", "Funde nós próximos (fica o primeiro; apoios somam). Barras que zeram ou repetem somem.", f, async x =>
                    {
                        double tol = x.D("tol") / 1000; if (tol <= 0) throw new FormatException("Tolerância: maior que 0");
                        await rodar("Nós unidos (" + x.Texto("tol") + " mm)", m => GeometriaEdicao.UnirNos(m, nos(), tol));
                    });
                    p.Campo("tol", "Tolerância (mm)", "1"); break;
                case "propriedades":
                    const string manter = "(manter)";
                    var secoes = new[] { manter }.Concat((d0.Json["secoes"] as JsonObject)?.Select(kv => kv.Key) ?? new string[0]);
                    var mats = new[] { manter }.Concat((d0.Json["materiais"] as JsonObject)?.Select(kv => kv.Key) ?? new string[0]);
                    var apoios = new Dictionary<string, bool[]> { [manter] = null, ["Engaste"] = Enumerable.Repeat(true, 6).ToArray(),
                        ["Articulado"] = new[] { true, true, true, false, false, false }, ["Livre"] = new bool[6] };
                    p = new PainelEdicao("Propriedades em lote", "Só muda o que não estiver em (manter) ou vazio. Seções e materiais do modelo.", f, async x =>
                    {
                        string s = x.Texto("secao"), mt = x.Texto("material"); double? r = x.Vazio("rot") ? (double?)null : x.D("rot");
                        await rodar("Propriedades em lote", m => GeometriaEdicao.Propriedades(m, nos(), bs(), s == manter ? null : s, mt == manter ? null : mt, r, apoios[x.Texto("apoio")]));
                    });
                    p.Lista("secao", "Seção", secoes); p.Lista("material", "Material", mats); p.Campo("rot", "Giro da seção (°)", "", "Vazio = manter.");
                    p.Lista("apoio", "Apoio dos nós", apoios.Keys); break;
                default: return;
            }
            painelEdicao = p;
            p.Owner = this;
            p.Closed += (s, e) => { if (painelEdicao == p) painelEdicao = null; };
            if (!Capturas.Ativo)
            {
                p.WindowStartupLocation = WindowStartupLocation.Manual;
                p.Left = Left + Math.Max(0, ActualWidth - 340 - 360); p.Top = Top + 170;
            }
            p.Show();
        }
    }

    // Estructto.exe --testes-edicao <saida.txt>: geometria pura, sem motor.
    public static class TestesEdicao
    {
        // Pórtico plano 6 m × 3 m: N1(0,0,0) N2(6,0,0) N3(0,0,3) N4(6,0,3); pilares B1, B2 e viga B3 com carga.
        static JsonObject Portico() => JsonNode.Parse(@"{""versao_formato"":1,""nos"":{
            ""N1"":{""id"":""N1"",""x"":0,""y"":0,""z"":0,""apoio"":[true,true,true,true,true,true]},
            ""N2"":{""id"":""N2"",""x"":6,""y"":0,""z"":0,""apoio"":[true,true,true,true,true,true]},
            ""N3"":{""id"":""N3"",""x"":0,""y"":0,""z"":3,""apoio"":[false,false,false,false,false,false]},
            ""N4"":{""id"":""N4"",""x"":6,""y"":0,""z"":3,""apoio"":[false,false,false,false,false,false]}},
            ""materiais"":{""A"":{""nome"":""A""},""C30"":{""nome"":""C30"",""fck"":30000}},""secoes"":{""W"":{""nome"":""W""},""V"":{""nome"":""V""}},
            ""barras"":{""B1"":{""id"":""B1"",""ni"":""N1"",""nj"":""N3"",""secao"":""W"",""material"":""A"",""rot"":0,""tipo"":""aco"",""params"":{""papel"":""pilar""}},
            ""B2"":{""id"":""B2"",""ni"":""N2"",""nj"":""N4"",""secao"":""W"",""material"":""A"",""rot"":0,""tipo"":""aco"",""params"":{}},
            ""B3"":{""id"":""B3"",""ni"":""N3"",""nj"":""N4"",""secao"":""V"",""material"":""A"",""rot"":0,""tipo"":""aco"",""params"":{""rotula_i"":true,""rotula_j"":true}}},
            ""cargas_nodais"":[{""id"":""cn1"",""caso"":""Q1"",""no"":""N3"",""F"":[10,0,0,0,0,5]}],
            ""cargas_distribuidas"":[{""id"":""cd1"",""caso"":""G1"",""barra"":""B3"",""q"":[0,0,-10],""q2"":[0,0,-20]}],
            ""cargas_pontuais"":[{""id"":""cp1"",""caso"":""G1"",""barra"":""B3"",""a"":4.5,""F"":[0,0,-7]}]}").AsObject();

        public static string Rodar()
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (!c) falhas++; }
            bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;
            int Nn(JsonObject m) => m["nos"].AsObject().Count; int Nb(JsonObject m) => m["barras"].AsObject().Count;
            int Nc(JsonObject m, string l) => m[l].AsArray().Count;
            string NoEm(JsonObject m, double x, double y, double z) => m["nos"].AsObject().FirstOrDefault(kv => Perto((double)kv.Value["x"], x) && Perto((double)kv.Value["y"], y) && Perto((double)kv.Value["z"], z)).Key;
            var sem = new string[0];

            // espelho X = 6: N2 e N4 ficam (fundem), N1→(12,0,0), N3→(12,0,3); B2 (sobre o plano) não duplica
            var m1 = GeometriaEdicao.Copiar(Portico(), sem, sem, new[] { GeometriaEdicao.Espelho(0, 6) });
            Ok("espelho X=6: 6 nós, 5 barras", Nn(m1) == 6 && Nb(m1) == 5);
            Ok("espelho X=6: nó em (12,0,3)", NoEm(m1, 12, 0, 3) != null);
            var cn = m1["cargas_nodais"].AsArray().Last();
            Ok("espelho X=6: Fx e Mz da carga nodal trocam o sinal certo", Perto((double)cn["F"][0], -10) && Perto((double)cn["F"][5], -5) && cn["id"] == null);
            Ok("espelho X=6: carga distribuída e pontual copiadas", Nc(m1, "cargas_distribuidas") == 2 && Nc(m1, "cargas_pontuais") == 2);

            // rotação 90° em torno de (0,0): (6,0,z) → (0,6,z); carga Fx → Fy
            var m2 = GeometriaEdicao.Mover(Portico(), sem, sem, GeometriaEdicao.Rotacao(0, 0, 90));
            Ok("rotação 90°: N4 em (0,6,3)", Perto((double)m2["nos"]["N4"]["x"], 0) && Perto((double)m2["nos"]["N4"]["y"], 6) && Perto((double)m2["nos"]["N4"]["z"], 3));
            var f2 = m2["cargas_nodais"][0]["F"];
            Ok("rotação 90°: Fx=10 vira Fy=10", Perto((double)f2[0], 0) && Perto((double)f2[1], 10));
            Ok("rotação 90°: ids mantidos", Nn(m2) == 4 && Nb(m2) == 3);

            // matriz 2 × 3 × 1: 5 cópias → 6 pórticos isolados
            var ts = GeometriaEdicao.Matriz(2, 3, 1, 20, 10, 0);
            var m3 = GeometriaEdicao.Copiar(Portico(), sem, sem, ts);
            Ok("matriz 2×3×1: 5 cópias, 24 nós, 18 barras", ts.Count == 5 && Nn(m3) == 24 && Nb(m3) == 18);
            Ok("matriz: cargas × 6", Nc(m3, "cargas_distribuidas") == 6 && Nc(m3, "cargas_nodais") == 6);

            // cópia linear 3× em Y = 5: 16 nós, 12 barras, params copiados
            var m4 = GeometriaEdicao.Copiar(Portico(), sem, new[] { "B1", "B2", "B3" }, Enumerable.Range(1, 3).Select(k => GeometriaEdicao.Translacao(0, 5 * k, 0)));
            Ok("cópia 3×: 16 nós, 12 barras", Nn(m4) == 16 && Nb(m4) == 12);
            Ok("cópia 3×: seção e params copiados", m4["barras"].AsObject().Count(kv => (string)kv.Value["secao"] == "V" && kv.Value["params"]?["rotula_i"] != null) == 4);

            // tolerância 1 mm: deslocamento de 0,5 mm funde tudo (nada novo); 2 mm cria
            var m5 = GeometriaEdicao.Copiar(Portico(), sem, sem, new[] { GeometriaEdicao.Translacao(0.0005, 0, 0) });
            Ok("tolerância: cópia a 0,5 mm não cria nó nem barra", Nn(m5) == 4 && Nb(m5) == 3);
            var m6 = GeometriaEdicao.Copiar(Portico(), sem, sem, new[] { GeometriaEdicao.Translacao(0.002, 0, 0) });
            Ok("tolerância: cópia a 2 mm cria 4 nós e 3 barras", Nn(m6) == 8 && Nb(m6) == 6);
            // mover a viga 6 m em X: N4 cai sobre... nada; mover N3 para (6,0,3) funde com N4 e B3 zera
            var m7 = GeometriaEdicao.Mover(Portico(), new[] { "N3" }, sem, GeometriaEdicao.Translacao(6.0004, 0, 0));
            Ok("mover sobre outro nó (0,4 mm): funde, B3 some com as cargas", Nn(m7) == 3 && Nb(m7) == 2 && Nc(m7, "cargas_distribuidas") == 0 && (string)m7["barras"]["B1"]["nj"] == "N4");

            // dividir B3 em 3: +2 nós, 3 trechos; q trapezoidal reinterpolada; pontual (a = 4,5) no 3º trecho, a = 0,5
            var m8 = GeometriaEdicao.Dividir(Portico(), new[] { "B3" }, 3);
            Ok("dividir em 3: 6 nós, 5 barras", Nn(m8) == 6 && Nb(m8) == 5 && m8["barras"]["B3"] == null);
            var qs = m8["cargas_distribuidas"].AsArray();
            Ok("dividir: q de -10 a -20 em 3 trechos", qs.Count == 3 && Perto((double)qs[1]["q"][2], -10 - 10.0 / 3) && Perto((double)qs[1]["q2"][2], -10 - 20.0 / 3));
            var cp = m8["cargas_pontuais"][0];
            Ok("dividir: pontual vai ao 3º trecho com a = 0,5", m8["cargas_pontuais"].AsArray().Count == 1 && Perto((double)cp["a"], 0.5) && (string)m8["barras"][(string)cp["barra"]]["nj"] == "N4");
            Ok("dividir: rotula_i só no 1º trecho, rotula_j só no último",
               m8["barras"].AsObject().Count(kv => kv.Value["params"]?["rotula_i"] != null) == 1 && (string)m8["barras"].AsObject().First(kv => kv.Value["params"]?["rotula_i"] != null).Value["ni"] == "N3"
               && (string)m8["barras"].AsObject().First(kv => kv.Value["params"]?["rotula_j"] != null).Value["nj"] == "N4");
            var m9 = GeometriaEdicao.Dividir(Portico(), new[] { "B3" }, 2, 1.5);
            Ok("dividir a 1,5 m: nó em (1,5; 0; 3)", Nb(m9) == 4 && NoEm(m9, 1.5, 0, 3) != null);

            // unir nós: N5 a 0,5 mm de N3 com barra até N4 → funde e a barra repetida some; a 2 mm fica
            var m10 = Portico();
            m10["nos"]["N5"] = new JsonObject { ["id"] = "N5", ["x"] = 0.0005, ["y"] = 0.0, ["z"] = 3.0, ["apoio"] = new JsonArray(true, false, false, false, false, false) };
            m10["barras"]["B9"] = new JsonObject { ["id"] = "B9", ["ni"] = "N5", ["nj"] = "N4", ["secao"] = "V", ["material"] = "A" };
            GeometriaEdicao.UnirNos(m10, sem, 0.001);
            Ok("unir nós (1 mm): N5 some, barra repetida some, apoio soma", Nn(m10) == 4 && Nb(m10) == 3 && (bool)m10["nos"]["N3"]["apoio"][0]);
            var m11 = Portico();
            m11["nos"]["N5"] = new JsonObject { ["id"] = "N5", ["x"] = 0.002, ["y"] = 0.0, ["z"] = 3.0, ["apoio"] = new JsonArray(false, false, false, false, false, false) };
            GeometriaEdicao.UnirNos(m11, sem, 0.001);
            Ok("unir nós: a 2 mm não funde", Nn(m11) == 5);

            // propriedades em lote
            var m12 = GeometriaEdicao.Propriedades(Portico(), sem, new[] { "B1", "B2" }, "V", "C30", 90, null);
            Ok("propriedades: seção, material (tipo concreto) e rot", (string)m12["barras"]["B1"]["secao"] == "V" && (string)m12["barras"]["B2"]["tipo"] == "concreto"
               && Perto((double)m12["barras"]["B1"]["rot"], Math.PI / 2) && (string)m12["barras"]["B3"]["secao"] == "V" && (string)m12["barras"]["B3"]["material"] == "A");
            var m13 = GeometriaEdicao.Propriedades(Portico(), new[] { "N3" }, sem, null, null, null, new[] { true, true, true, false, false, false });
            Ok("propriedades: apoio só no nó escolhido", (bool)m13["nos"]["N3"]["apoio"][2] && !(bool)m13["nos"]["N3"]["apoio"][3] && (bool)m13["nos"]["N1"]["apoio"][3]);

            // apoios, molas, rótulas e cardinal dos comandos (Comandos3D.cs, docs/v3/FLUXO-COMANDOS.md)
            var m19 = GeometriaEdicao.Apoio(Portico(), new[] { "N3" }, new[] { false, false, true, false, false, false }, new double[6]);
            Ok("apoio móvel: só uz travado no N3; N1 continua engastado", (bool)m19["nos"]["N3"]["apoio"][2] && !(bool)m19["nos"]["N3"]["apoio"][0] && (bool)m19["nos"]["N1"]["apoio"][5]);
            var m20 = GeometriaEdicao.Apoio(Portico(), new[] { "N1" }, null, new double[] { 0, 0, 5000, 0, 0, 0 });
            Ok("molas: kz = 5000 no N1, uz liberado (a mola é o apoio), ux continua travado", Perto((double)m20["nos"]["N1"]["molas"][2], 5000) && !(bool)m20["nos"]["N1"]["apoio"][2] && (bool)m20["nos"]["N1"]["apoio"][0]);
            GeometriaEdicao.Apoio(m20, new[] { "N1" }, new bool[6], new double[6]);
            Ok("livre: tira apoio e molas", m20["nos"]["N1"]["molas"] == null && !((JsonArray)m20["nos"]["N1"]["apoio"]).Any(x => (bool)x));
            var m21 = GeometriaEdicao.Rotulas(Portico(), new[] { "B1" }, true);
            Ok("rótulas: B1 ganha rotula_i e rotula_j; B2 não", m21["barras"]["B1"]["params"]["rotula_j"] != null && m21["barras"]["B2"]["params"]["rotula_i"] == null);
            GeometriaEdicao.Rotulas(m21, new[] { "B3" }, false);
            Ok("rótulas: desligar tira as duas da B3", m21["barras"]["B3"]["params"]["rotula_i"] == null && m21["barras"]["B3"]["params"]["rotula_j"] == null);
            var m22 = GeometriaEdicao.Cardinal(Portico(), new[] { "B3" }, 8);
            Ok("cardinal 8 na B3; 5 tira de params", DocumentoEspacial.CardinalDe(m22["barras"]["B3"]) == 8 && DocumentoEspacial.CardinalDe(GeometriaEdicao.Cardinal(m22, new[] { "B3" }, 5)["barras"]["B3"]) == 5 && m22["barras"]["B3"]["params"]["cardinal"] == null);
            bool recusouCard = false; try { GeometriaEdicao.Cardinal(Portico(), new[] { "B3" }, 11); } catch (ArgumentException) { recusouCard = true; }
            Ok("cardinal 11 é recusado", recusouCard);
            Ok("comandos: listas digitadas (0,0,-10 · 1;2;3 · 1.5)", MainWindow.LerLista("0,0,-10", 3, 6)?[2] == -10 && MainWindow.LerLista("1;2;3", 3, 3)?[1] == 2 && MainWindow.LerLista("1.5", 1, 1)?[0] == 1.5 && MainWindow.LerLista("1,2", 3, 6) == null && MainWindow.LerLista("a,b,c", 3, 3) == null);

            // comandos CAD (Comandos3D.cs)
            var m14 = GeometriaEdicao.Apagar(Portico(), sem, new[] { "B3" });
            Ok("apagar viga: 4 nós (pontas ainda usadas), 2 barras, cargas da viga somem", Nn(m14) == 4 && Nb(m14) == 2 && Nc(m14, "cargas_distribuidas") == 0 && Nc(m14, "cargas_pontuais") == 0);
            var m15 = GeometriaEdicao.Apagar(Portico(), new[] { "N3" }, sem);
            Ok("apagar nó N3: leva B1 e B3, N1 fica sem barra e some, carga nodal some", Nn(m15) == 2 && Nb(m15) == 1 && Nc(m15, "cargas_nodais") == 0);
            var x1 = GeometriaEdicao.Cruzamento(new double[] { 0, 0, 3 }, new double[] { 4, 0, 3 }, new double[] { 6, -1, 0 }, new double[] { 6, 1, 6 });
            Ok("cruzamento: viga (0..4) com pilar inclinado por (6,0,3) → (6,0,3), t = 1,5", x1 != null && Perto(x1.Value.P[0], 6) && Perto(x1.Value.P[2], 3) && Perto(x1.Value.T, 1.5));
            Ok("cruzamento: reversas a 1 m → null", GeometriaEdicao.Cruzamento(new double[] { 0, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, 1, 1 }) == null);
            var m16 = GeometriaEdicao.MoverPonta(Portico(), "B3", "N4", new[] { 4.0, 0, 3 });
            Ok("aparar: ponta compartilhada vira nó novo em (4,0,3); B2 continua em N4", Nn(m16) == 5 && NoEm(m16, 4, 0, 3) != null && (string)m16["barras"]["B2"]["nj"] == "N4" && (string)m16["barras"]["B3"]["nj"] != "N4");
            var m17 = Portico(); m17["barras"].AsObject().Remove("B2");
            GeometriaEdicao.MoverPonta(m17, "B3", "N4", new[] { 8.0, 0, 3 });
            Ok("estender: ponta só desta barra move o nó", Nn(m17) == 4 && Perto((double)m17["nos"]["N4"]["x"], 8));
            var esp = GeometriaEdicao.EspelhoReta(0, 0, 1, 1).P(new double[] { 2, 0, 5 });
            Ok("espelho na reta y = x: (2,0,5) → (0,2,5)", Perto(esp[0], 0) && Perto(esp[1], 2) && Perto(esp[2], 5));
            var cot = GeometriaEdicao.LerCotas("0,@6,@6,@7.5");
            Ok("grade 0,@6,@6,@7.5 → 0 6 12 19,5", cot.Length == 4 && Perto(cot[3], 19.5) && Perto(cot[1], 6));

            // giro (rot) e cardinal sob rotação e espelho: os pontos da seção desenhada (GeometriaPerfis, como a Vista3D) da
            // barra transformada = os pontos da seção original transformados. U e L (abas iguais) com giro e cardinal.
            var ms = JsonNode.Parse(@"{""secoes"":{
                ""U1"":{""nome"":""U1"",""props"":{""tipo"":""U"",""h"":0.2,""b"":0.08,""tw"":0.006,""tf"":0.01}},
                ""L1"":{""nome"":""L1"",""props"":{""tipo"":""L"",""h"":0.1,""b"":0.1,""t"":0.008}},
                ""I1"":{""nome"":""I1"",""props"":{""tipo"":""I"",""h"":0.3,""b"":0.15,""tw"":0.006,""tf"":0.01}}}}").AsObject();
            List<Point3D> Secao(JsonNode b, double[] a, double[] c)
            {
                var f = GeometriaPerfis.CriarContorno(Perfil3D.DoMotor(ms["secoes"][(string)b["secao"]] as JsonObject), 0.1);
                var e = new System.Windows.Media.Media3D.Vector3D(c[0] - a[0], c[1] - a[1], c[2] - a[2]);
                var v = Modelo3D.EixoY(e, (double)(b["rot"] ?? 0.0) * 180 / Math.PI); e.Normalize();
                var u = System.Windows.Media.Media3D.Vector3D.CrossProduct(v, e);
                var dc = GeometriaPerfis.Cardinal(f, DocumentoEspacial.CardinalDe(b));
                var o = new Point3D(a[0], a[1], a[2]);
                return f.Lacos.SelectMany(l => l).Select(q => o + u * (q.X + dc.X) + v * (q.Y + dc.Y)).ToList();
            }
            bool Mesmos(List<Point3D> x, List<Point3D> y) => x.Count == y.Count && x.All(p => y.Any(q => (p - q).Length < 1e-9)) && y.All(p => x.Any(q => (p - q).Length < 1e-9));
            Point3D P3(double[] p) => new Point3D(p[0], p[1], p[2]);
            var casos = new (string nome, string sec, double[] a, double[] c, double rot, int card)[]
            {
                ("U vertical para cima", "U1", new double[] { 1, 2, 0 }, new double[] { 1, 2, 3 }, 0.3, 1),
                ("L vertical para baixo", "L1", new double[] { 2, 1, 3 }, new double[] { 2, 1, 0 }, 0.2, 3),
                ("U inclinado", "U1", new double[] { 0, 0, 3 }, new double[] { 4, 1, 4 }, 0.5, 8),
                ("L horizontal", "L1", new double[] { 0, 0, 3 }, new double[] { 6, 0, 3 }, 0, 7),
                ("I horizontal", "I1", new double[] { 0, 0, 3 }, new double[] { 0, 5, 3 }, 0, 4),
            };
            var transf = new (string nome, Transf t)[]
            {
                ("rotação 37°", GeometriaEdicao.Rotacao(1, -2, 37)), ("espelho X = 3", GeometriaEdicao.Espelho(0, 3)),
                ("espelho na reta (0,0)-(1;0,3)", GeometriaEdicao.EspelhoReta(0, 0, 1, 0.3)), ("espelho Z = 5", GeometriaEdicao.Espelho(2, 5)),
            };
            foreach (var (cnome, sec, a, c, rot, card) in casos)
                foreach (var (tn, t) in transf)
                {
                    var b = new JsonObject { ["secao"] = sec, ["rot"] = rot, ["params"] = new JsonObject { ["cardinal"] = card } };
                    var antes = Secao(b, a, c).Select(p => P3(t.P(new[] { p.X, p.Y, p.Z }))).ToList();
                    GeometriaEdicao.GirarSecao(ms, b, a, c, t);
                    Ok("seção " + cnome + " × " + tn + ": desenho = transformado (rot " + Num.Fmt((double)b["rot"] * 180 / Math.PI, 1) + "°, cardinal " + DocumentoEspacial.CardinalDe(b) + ")",
                       Mesmos(Secao(b, t.P(a), t.P(c)), antes));
                }
            var bv = new JsonObject { ["secao"] = "U1", ["rot"] = 0.3 };
            GeometriaEdicao.GirarSecao(ms, bv, new double[] { 0, 0, 0 }, new double[] { 0, 0, 3 }, GeometriaEdicao.Rotacao(0, 0, 37));
            Ok("rotação: pilar para cima soma o ângulo ao rot", Perto((double)bv["rot"], 0.3 + 37 * Math.PI / 180));
            bv = new JsonObject { ["secao"] = "U1", ["rot"] = 0.3 };
            GeometriaEdicao.GirarSecao(ms, bv, new double[] { 0, 0, 3 }, new double[] { 0, 0, 0 }, GeometriaEdicao.Rotacao(0, 0, 37));
            Ok("rotação: pilar para baixo (nj embaixo) subtrai", Perto((double)bv["rot"], 0.3 - 37 * Math.PI / 180));
            var bh = new JsonObject { ["secao"] = "I1", ["rot"] = 0.0 };
            GeometriaEdicao.GirarSecao(ms, bh, new double[] { 0, 0, 3 }, new double[] { 6, 0, 3 }, GeometriaEdicao.Espelho(0, 3));
            Ok("espelho: viga I fica com rot 0 (sem virar de cabeça para baixo)", Perto((double)bh["rot"], 0));
            var m18 = GeometriaEdicao.Mover(Portico(), sem, sem, GeometriaEdicao.Rotacao(0, 0, 90));
            Ok("Mover/Rotacionar: pilar B1 recebe rot = 90°", Perto((double)m18["barras"]["B1"]["rot"], Math.PI / 2) && Perto((double)m18["barras"]["B3"]["rot"], 0));

            // níveis: renomear mantém diafragma e a ordem; z novo opcional
            var mp = JsonNode.Parse(@"{""pavimentos"":{""P1"":{""nome"":""P1"",""z"":3,""diafragma"":false},""P2"":{""nome"":""P2"",""z"":6,""diafragma"":true}}}").AsObject();
            Niveis.Renomear(mp, "P1", "Mezanino", 3.2);
            var pv = mp["pavimentos"].AsObject();
            Ok("renomear nível: nome novo, diafragma false mantido, z 3,2, ordem", pv["P1"] == null && (bool)pv["Mezanino"]["diafragma"] == false && Perto((double)pv["Mezanino"]["z"], 3.2) && pv.First().Key == "Mezanino" && (string)pv["Mezanino"]["nome"] == "Mezanino");
            bool recusou = false; try { Niveis.Renomear(mp, "Mezanino", "P2"); } catch (ArgumentException) { recusou = true; }
            Ok("renomear nível para um nome existente é recusado", recusou && pv["Mezanino"] != null);

            // grade estrutural: posições ↔ config.grade (vãos, x0/y0, nomes)
            var gj = GradeEstrutural.Json(new[] { 2.5, -2.5, 0 }, new[] { 0.0, 5 });
            var (gx, gy, gnx, gny) = GradeEstrutural.Ler(gj);
            Ok("grade: (−2,5; 0; 2,5) → x0 −2,5, vãos 2,5 2,5, eixos 1 2 3; filas A B", gx.Length == 3 && Perto(gx[0], -2.5) && Perto(gx[2], 2.5) && Perto((double)gj["x"][0], 2.5) && gnx[2] == "3" && gny[1] == "B" && gj["y0"] == null);
            var (wx, wy, _, wny) = GradeEstrutural.Ler(JsonNode.Parse(@"{""x"":[6,6],""y"":[],""nomes_x"":[""1"",""2"",""3""],""nomes_y"":[""A""]}"));
            Ok("grade do Novo projeto (vãos, sem fila): 3 eixos, 1 fila em y = 0", wx.Length == 3 && Perto(wx[2], 12) && wy.Length == 1 && wny[0] == "A");

            // contêiner antigo: cardinal inválido vira aviso (não trava), válido vai a params.cardinal; grade migra
            var avisos = new List<string>();
            var mod = Portico();
            DocumentoEspacial.Migrar(JsonNode.Parse(@"{""grade"":{""x"":[0,6],""y"":[0]},""cardinal"":{""B1"":""x"",""B2"":2,""B3"":12,""B9"":3}}").AsObject(), mod, avisos);
            Ok("migrar: B2 cardinal 2 em params, 3 avisos, grade em config.grade", DocumentoEspacial.CardinalDe(mod["barras"]["B2"]) == 2 && avisos.Count == 3 && GradeEstrutural.Ler(mod["config"]["grade"]).X.Length == 2);

            // salvar atômico: troca o arquivo existente, sem sobrar o temporário
            string arqT = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "estructto-atomico-" + Guid.NewGuid().ToString("N") + ".estructto");
            Config.GravarAtomico(arqT, "um"); Config.GravarAtomico(arqT, "dois");
            Ok("gravar atômico: conteúdo novo, sem .tmp", System.IO.File.ReadAllText(arqT) == "dois" && !System.IO.File.Exists(arqT + ".tmp"));
            System.IO.File.Delete(arqT);

            sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
            return sb.ToString();
        }
    }
}
