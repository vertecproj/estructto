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
            foreach (string id in sn) Pos(N[id], t.P(Xyz(N[id])));
            foreach (var c in Lista(m, "cargas_nodais").Where(c => sn.Contains((string)c["no"]))) c["F"] = F6(c["F"], t);
            var bs = Col(m, "barras");
            var levadas = bs.Where(kv => sn.Contains((string)kv.Value["ni"]) && sn.Contains((string)kv.Value["nj"])).Select(kv => kv.Key).ToHashSet();
            foreach (var c in Lista(m, "cargas_distribuidas").Concat(Lista(m, "cargas_pontuais")).Where(c => levadas.Contains((string)c["barra"]))) Vetores(c, t);
            foreach (string id in sn)
            {
                var alvo = Achar(N, Xyz(N[id]), x => !sn.Contains(x));
                if (alvo != null) Fundir(m, id, alvo);
            }
            Limpar(m);
            return m;
        }

        // Uma cópia da seleção por transformação. Barras copiadas levam seção, material, rot, params e cargas; nós novos que
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
        readonly List<UIElement> botoesSo3D = new List<UIElement>();
        internal PainelEdicao PainelEdicaoAberto => painelEdicao;

        // Faixa › Modelo › Modificar: botões que só existem no modelo espacial (Mover, Copiar, Espelhar e Dividir já estão
        // lá e passam para cá quando o documento é espacial). Os botões também entram na paleta (Botao → Reg).
        void MontarEdicao3D(F.RibbonGroupBox modificar)
        {
            foreach (var (ic, txt, op, dica) in new[] {
                ("refazer", "Rotacionar", "rotacionar", "Gira a seleção em torno de um eixo vertical (modelo espacial). Cópias = 0 gira; ≥ 1 faz cópias polares."),
                ("grade", "Matriz", "matriz", "Matriz retangular nx × ny × nz com espaçamentos (modelo espacial)."),
                ("unir", "Unir nós", "unir", "Funde nós coincidentes (tolerância em mm) (modelo espacial)."),
                ("pincel", "Propriedades em lote", "propriedades", "Seção, material, giro ou apoios em todos os elementos escolhidos (modelo espacial).") })
            {
                var b = Botao(ic, txt, () => AbrirEdicaoEsp(op), dica, null, true);
                b.Visibility = Visibility.Collapsed;
                botoesSo3D.Add(b); modificar.Items.Add(b);
            }
        }

        void MostrarEdicao3D(bool esp)
        {
            foreach (var b in botoesSo3D) b.Visibility = esp ? Visibility.Visible : Visibility.Collapsed;
            if (!esp) painelEdicao?.Close();
        }

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
                    p = new PainelEdicao("Espelhar", "Cópia espelhada em relação a um plano X = a, Y = b ou Z = c. O giro (rot) da seção não muda.", f, async x =>
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

            sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
            return sb.ToString();
        }
    }
}
