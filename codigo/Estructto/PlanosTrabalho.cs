// Níveis (pavimentos) e planos de trabalho do modelo espacial, como os níveis do CYPE/Revit (docs/MODELO-ESPACIAL.md, "Níveis e planos").
// - Níveis: os pavimentos do motor + "Térreo z=0". Criar, renomear, mudar a cota e apagar = /api/modelo/pavimento (dentro de Motor.Exclusivo).
// - Ativar um nível: vira o plano de trabalho; isolar mostra só as barras entre ele e o nível de baixo + fantasmas do resto, em planta.
// - Copiar pavimento para cima e Gerar pilares entre níveis: um POST /api/modelo/lote só (um Ctrl+Z desfaz tudo).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;
using F = Fluent;

namespace NovoFtool
{
    // Alvo do plano de trabalho. eixo 'z' (planta na cota), 'x' ou 'y' (elevação no valor); '\0' = sem plano (3D completo).
    // Isolar: só as barras com os dois nós entre zMin e zMax (null, null = tudo).
    public interface IPlanoAlvo
    {
        void DefinirPlano(char eixo, double valor);
        void Isolar(double? zMin, double? zMax);
    }

    public sealed class NivelPlano { public string Nome; public double Z; public bool Terreo; }

    // Regras puras sobre o JSON do motor (sem interface): testadas pelas capturas (CapturasPlanos).
    public static class Niveis
    {
        public const double Tol = 1e-3;   // mesma tolerância do motor (nó a até 1 mm pertence ao pavimento)

        static double Dz(JsonNode n, string c) => (double)n[c];
        static IEnumerable<KeyValuePair<string, JsonNode>> Nos(JsonObject m) => m["nos"]?.AsObject() ?? new JsonObject();
        static IEnumerable<KeyValuePair<string, JsonNode>> Barras(JsonObject m) => m["barras"]?.AsObject() ?? new JsonObject();

        // Pavimentos do motor do mais baixo ao mais alto, com "Térreo" (z = 0) se nenhum estiver na cota 0.
        public static List<NivelPlano> Lista(JsonObject m)
        {
            var l = (m["pavimentos"]?.AsObject() ?? new JsonObject()).Select(kv => new NivelPlano { Nome = kv.Key, Z = Dz(kv.Value, "z") }).ToList();
            if (!l.Any(n => Math.Abs(n.Z) <= Tol)) l.Add(new NivelPlano { Nome = "Térreo", Z = 0, Terreo = true });
            return l.OrderBy(n => n.Z).ToList();
        }

        public static double? Abaixo(List<NivelPlano> l, double z) => l.Where(n => n.Z < z - Tol).Select(n => (double?)n.Z).LastOrDefault();
        public static double? Acima(List<NivelPlano> l, double z) => l.Where(n => n.Z > z + Tol).Select(n => (double?)n.Z).FirstOrDefault();

        // Renomeia o pavimento (o motor não troca o nome por PATCH): a mesma entrada com o nome novo, no mesmo lugar da lista,
        // diafragma e o resto mantidos; z novo opcional. Vai inteiro num PUT /api/modelo: atômico, um Ctrl+Z.
        public static JsonObject Renomear(JsonObject m, string antigo, string novo, double? z = null)
        {
            var pav = m["pavimentos"] as JsonObject;
            if (pav?[antigo] == null) throw new ArgumentException("o nível " + antigo + " não existe");
            if (string.IsNullOrWhiteSpace(novo)) throw new ArgumentException("nome vazio");
            if (novo != antigo && pav[novo] != null) throw new ArgumentException("já existe um nível " + novo);
            var itens = pav.ToList(); pav.Clear();
            foreach (var (k, v) in itens)
            {
                if (k != antigo) { pav[k] = v; continue; }
                v["nome"] = novo; if (z != null) v["z"] = z.Value;
                pav[novo] = v;
            }
            return m;
        }

        // Cotas distintas dos nós num eixo ('x', 'y', 'z'), em ordem.
        public static List<double> Cotas(JsonObject m, string eixo)
        {
            var r = new List<double>();
            foreach (double v in Nos(m).Select(kv => Dz(kv.Value, eixo)).OrderBy(v => v))
                if (r.Count == 0 || v - r[^1] > Tol) r.Add(v);
            return r;
        }

        // Barra do andar [lo, z]: os dois nós na faixa e não deitada na cota de baixo (essa é do andar de baixo).
        // lo == z (térreo): só as barras deitadas na cota.
        public static bool NoAndar(double zi, double zj, double lo, double z)
        {
            bool dentro = zi >= lo - Tol && zi <= z + Tol && zj >= lo - Tol && zj <= z + Tol;
            return dentro && (z - lo <= Tol || Math.Max(zi, zj) > lo + Tol);
        }

        static Func<string> Gerador(IEnumerable<string> usados, string prefixo)
        {
            var u = new HashSet<string>(usados); int k = 0;
            return () => { string s; do s = prefixo + (++k); while (u.Contains(s)); u.Add(s); return s; };
        }

        static string Chave(double x, double y, double z) => Math.Round(x / Tol) + "|" + Math.Round(y / Tol) + "|" + Math.Round(z / Tol);

        static JsonObject Item(string tipo, JsonObject dados) => new JsonObject { ["tipo"] = tipo, ["dados"] = dados };

        // Nó em (x, y, z): o que já existe (no modelo ou criado neste lote) ou um novo no lote.
        sealed class Nos3
        {
            readonly Dictionary<string, string> porPos = new Dictionary<string, string>();
            readonly Func<string> novo; readonly JsonArray itens;
            public Nos3(JsonObject m, JsonArray itens)
            {
                this.itens = itens;
                foreach (var kv in Nos(m)) porPos[Chave(Dz(kv.Value, "x"), Dz(kv.Value, "y"), Dz(kv.Value, "z"))] = kv.Key;
                novo = Gerador(Nos(m).Select(kv => kv.Key), "n");
            }
            public string Em(double x, double y, double z)
            {
                string k = Chave(x, y, z);
                if (porPos.TryGetValue(k, out var id)) return id;
                id = porPos[k] = novo();
                itens.Add(Item("no", new JsonObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z }));
                return id;
            }
        }

        static JsonObject CopiaBarra(JsonNode b, string id, string ni, string nj)
        {
            var o = (JsonObject)b.DeepClone();
            o["id"] = id; o["ni"] = ni; o["nj"] = nj;
            return o;
        }

        // Copia o andar [lo, z] (barras, nós novos e cargas) para `vezes` andares acima, cada um com altura h.
        // A cota de cada nó é mapeada em escala: o pé do andar vai para o topo da cópia anterior e o topo sobe h.
        // Cria também um pavimento em cada cota nova sem pavimento. Devolve os itens do /api/modelo/lote.
        public static JsonArray CopiarParaCima(JsonObject m, double lo, double z, int vezes, double h)
        {
            var itens = new JsonArray();
            if (vezes < 1 || h <= Tol) return itens;
            var nos = new Nos3(m, itens);
            var novaBarra = Gerador(Barras(m).Select(kv => kv.Key), "b");
            var nomesPav = new HashSet<string>((m["pavimentos"]?.AsObject() ?? new JsonObject()).Select(kv => kv.Key));
            var cotasPav = Lista(m).Where(n => !n.Terreo).Select(n => n.Z).ToList();
            var andar = Barras(m).Where(kv => NoAndar(Dz(m["nos"][(string)kv.Value["ni"]], "z"), Dz(m["nos"][(string)kv.Value["nj"]], "z"), lo, z)).ToList();
            var idsAndar = new HashSet<string>(andar.Select(kv => kv.Key));
            var nosAndar = andar.SelectMany(kv => new[] { (string)kv.Value["ni"], (string)kv.Value["nj"] }).ToHashSet();
            double alt = z - lo;
            for (int k = 1; k <= vezes; k++)
            {
                double baseK = z + (k - 1) * h;
                double Cota(double nz) => alt <= Tol ? nz + k * h : baseK + (nz - lo) / alt * h;
                var mapa = new Dictionary<string, string>();
                string No(string id) { if (!mapa.TryGetValue(id, out var n)) { var p = m["nos"][id]; n = mapa[id] = nos.Em(Dz(p, "x"), Dz(p, "y"), Cota(Dz(p, "z"))); } return n; }
                var barraNova = new Dictionary<string, string>();
                foreach (var kv in andar)
                {
                    string id = barraNova[kv.Key] = novaBarra();
                    itens.Add(Item("barra", CopiaBarra(kv.Value, id, No((string)kv.Value["ni"]), No((string)kv.Value["nj"]))));
                }
                // cargas: nodais só dos nós de cima do andar (os do pé são do andar de baixo); nas barras, todas
                foreach (var c in (m["cargas_nodais"] as JsonArray ?? new JsonArray()).Where(c => nosAndar.Contains((string)c["no"])))
                {
                    string no = (string)c["no"];
                    if (alt > Tol && Dz(m["nos"][no], "z") <= lo + Tol) continue;
                    var o = (JsonObject)c.DeepClone(); o.Remove("id"); o["no"] = No(no);
                    itens.Add(Item("carga_nodal", o));
                }
                foreach (var (lista, tipo) in new[] { ("cargas_distribuidas", "carga_distribuida"), ("cargas_pontuais", "carga_pontual") })
                    foreach (var c in (m[lista] as JsonArray ?? new JsonArray()).Where(c => idsAndar.Contains((string)c["barra"])))
                    {
                        var o = (JsonObject)c.DeepClone(); o.Remove("id"); o["barra"] = barraNova[(string)c["barra"]];
                        if (o["q2"] == null) o.Remove("q2");
                        itens.Add(Item(tipo, o));
                    }
                double zk = z + k * h;
                if (!cotasPav.Any(c => Math.Abs(c - zk) <= Tol))
                {
                    int i = cotasPav.Count(c => c < zk) + 1; string nome;
                    do nome = "P" + i++; while (nomesPav.Contains(nome));
                    nomesPav.Add(nome); cotasPav.Add(zk);
                    itens.Add(Item("pavimento", new JsonObject { ["nome"] = nome, ["z"] = zk, ["diafragma"] = true }));
                }
            }
            return itens;
        }

        // Um pilar sob cada nó da cota zSup até a cota zInf (nó de baixo existente ou novo), se ainda não houver barra ligando os dois.
        // Seção e material: os do primeiro pilar do modelo (papel "pilar" ou barra vertical); sem pilar, os da primeira barra.
        public static JsonArray GerarPilares(JsonObject m, double zSup, double zInf)
        {
            var itens = new JsonArray();
            if (zSup - zInf <= Tol) return itens;
            JsonNode No(string id) => m["nos"][id];
            bool Vertical(JsonNode b) => Math.Abs(Dz(No((string)b["ni"]), "x") - Dz(No((string)b["nj"]), "x")) <= Tol && Math.Abs(Dz(No((string)b["ni"]), "y") - Dz(No((string)b["nj"]), "y")) <= Tol;
            var modelo = Barras(m).Select(kv => kv.Value).FirstOrDefault(b => (string)b["params"]?["papel"] == "pilar" || Vertical(b)) ?? Barras(m).Select(kv => kv.Value).FirstOrDefault();
            if (modelo == null) return itens;
            var nos = new Nos3(m, itens);
            var novaBarra = Gerador(Barras(m).Select(kv => kv.Key), "b");
            var ligados = Barras(m).Select(kv => ((string)kv.Value["ni"], (string)kv.Value["nj"])).SelectMany(p => new[] { p, (p.Item2, p.Item1) }).ToHashSet();
            foreach (var kv in Nos(m).Where(kv => Math.Abs(Dz(kv.Value, "z") - zSup) <= Tol).ToList())
            {
                string baixo = nos.Em(Dz(kv.Value, "x"), Dz(kv.Value, "y"), zInf);
                if (ligados.Contains((baixo, kv.Key))) continue;
                var b = CopiaBarra(modelo, novaBarra(), baixo, kv.Key);
                b["rot"] = 0.0;
                var p = b["params"] as JsonObject ?? new JsonObject();
                b["params"] = new JsonObject { ["papel"] = "pilar" };
                foreach (string c in new[] { "kx", "ky", "kz" }) if (p[c] != null) b["params"][c] = p[c].DeepClone();
                itens.Add(Item("barra", b));
            }
            return itens;
        }
    }

    // ================= janela principal: planos =================
    public partial class MainWindow
    {
        // Plano e faixa ativos (só do documento planoDoc; trocar de documento volta ao 3D completo).
        char planoEixo; double planoValor; double? isoMin, isoMax;
        DocumentoEspacial planoDoc;
        ModelVisual3D fantasmas;
        IPlanoAlvo plano;
        internal IPlanoAlvo Plano => plano ??= new PlanoNaVista(this);
        JanelaNiveis janelaNiveis;

        // O plano escolhido vira o plano de desenho da Vista3D (PlanoAtual: Barra e Pilar desenham nele); Isolar filtra o
        // Modelo3D (FiltrarPlano) e o resto vai em Modelo3D.Fantasmas, que a Vista3D desenha sem picking nem atração.
        sealed class PlanoNaVista : IPlanoAlvo
        {
            readonly MainWindow w;
            public PlanoNaVista(MainWindow w) { this.w = w; }
            public void Isolar(double? zMin, double? zMax) { w.isoMin = zMin; w.isoMax = zMax; w.planoDoc = w.Esp; w.AtualizarEspacial3D(false); }
            public void DefinirPlano(char eixo, double valor)
            {
                w.planoEixo = eixo; w.planoValor = valor; w.planoDoc = w.Esp;
                string nome = eixo == 'z' ? Niveis.Lista(w.Esp.Json).FirstOrDefault(n => Math.Abs(n.Z - valor) <= Niveis.Tol)?.Nome : null;
                w.vista3D.PlanoAtual = eixo == 'x' ? PlanoTrabalho.EmX(valor) : eixo == 'y' ? PlanoTrabalho.EmY(valor)
                    : eixo == 'z' ? PlanoTrabalho.Horizontal(valor, nome == null ? null : nome + " (z = " + Num.Fmt(valor, 2) + " m)") : PlanoTrabalho.Horizontal(0);
                w.AtualizarEspacial3D(false);
                w.vista3D.MostrarVista(eixo == 'z' ? VistaPadrao.Topo : eixo == 'x' ? VistaPadrao.Lado : eixo == 'y' ? VistaPadrao.Frente : VistaPadrao.Iso, Anim.Ligado);
            }
        }

        bool PlanoLigado => planoDoc != null && planoDoc == Esp && (isoMin != null || isoMax != null || planoEixo == 'x' || planoEixo == 'y');

        internal string DescricaoPlano => !PlanoLigado && planoEixo == '\0' ? "3D completo"
            : planoEixo == 'x' ? "Elevação X = " + Num.Fmt(planoValor, 2) + " m" : planoEixo == 'y' ? "Elevação Y = " + Num.Fmt(planoValor, 2) + " m"
            : planoEixo == 'z' ? "Nível z = " + Num.Fmt(planoValor, 2) + " m" : "3D completo";

        // Chamado por AtualizarEspacial3D: o que fica na vista; o resto vira fantasma (linhas claras).
        internal Modelo3D FiltrarPlano(Modelo3D m)
        {
            if (!PlanoLigado) return m;
            var nos = m.Nos.ToDictionary(n => n.Id);
            bool Fica(Barra3D b)
            {
                if (!nos.TryGetValue(b.Ni, out var a) || !nos.TryGetValue(b.Nj, out var c)) return false;
                if (planoEixo == 'x' && (Math.Abs(a.X - planoValor) > Niveis.Tol || Math.Abs(c.X - planoValor) > Niveis.Tol)) return false;
                if (planoEixo == 'y' && (Math.Abs(a.Y - planoValor) > Niveis.Tol || Math.Abs(c.Y - planoValor) > Niveis.Tol)) return false;
                if (isoMin == null && isoMax == null) return true;
                double lo = isoMin ?? double.NegativeInfinity, hi = isoMax ?? double.PositiveInfinity;
                return Niveis.NoAndar(a.Z, c.Z, lo, hi);
            }
            var r = new Modelo3D { Cargas = m.Cargas };   // setas de barras/nós fora da vista: a Vista3D pula
            var fora = r.Fantasmas;
            foreach (var b in m.Barras)
            {
                if (Fica(b)) r.Barras.Add(b);
                else if (nos.TryGetValue(b.Ni, out var a) && nos.TryGetValue(b.Nj, out var c)) fora.Add((new Point3D(a.X, a.Y, a.Z), new Point3D(c.X, c.Y, c.Z)));
            }
            var usados = r.Barras.SelectMany(b => new[] { b.Ni, b.Nj }).ToHashSet();
            r.Nos.AddRange(m.Nos.Where(n => usados.Contains(n.Id)));
            if (r.Barras.Count == 0) { r.Nos.AddRange(m.Nos); fora.Clear(); r.Barras.AddRange(m.Barras); }   // plano vazio: mostra tudo
            return r;
        }

        // Ao trocar para um documento 2D: sem plano isolado, plano de desenho de volta a z = 0.
        internal void LimparPlano() { planoDoc = null; isoMin = isoMax = null; planoEixo = '\0'; if (vista3D != null) vista3D.PlanoAtual = PlanoTrabalho.Horizontal(0); }

        // ---------- ações (faixa, paleta, painel) ----------

        bool ExigirEspacial()
        {
            if (Esp != null) return true;
            MostrarToast("Níveis e planos valem para o modelo espacial (Estrutura › Gerar 3D ou um .json do motor).", true);
            return false;
        }

        // Ativar um nível: plano de trabalho na cota, só o andar dele (entre ele e o de baixo) e planta animada.
        internal void AtivarNivel(double z)
        {
            if (!ExigirEspacial()) return;
            var l = Niveis.Lista(Esp.Json);
            Plano.Isolar(Niveis.Abaixo(l, z) ?? z, z);
            Plano.DefinirPlano('z', z);
            Log("Plano de trabalho: " + (l.FirstOrDefault(n => Math.Abs(n.Z - z) <= Niveis.Tol)?.Nome ?? "nível") + " (z = " + Num.Fmt(z, 2) + " m).", false);
            janelaNiveis?.Montar();
        }

        internal void Elevacao(char eixo, double v)
        {
            if (!ExigirEspacial()) return;
            Plano.Isolar(null, null);
            Plano.DefinirPlano(eixo, v);
            janelaNiveis?.Montar();
        }

        internal void Completo3D()
        {
            if (Esp == null) return;
            Plano.Isolar(null, null);
            Plano.DefinirPlano('\0', 0);
            janelaNiveis?.Montar();
        }

        internal void AbrirNiveis()
        {
            if (!ExigirEspacial()) return;
            if (janelaNiveis != null) { janelaNiveis.Activate(); return; }
            janelaNiveis = new JanelaNiveis(this) { Owner = this };
            janelaNiveis.Closed += (s, e) => janelaNiveis = null;
            janelaNiveis.Show();
        }

        // Grupo "Planos" da aba Modelo.
        internal F.RibbonGroupBox GrupoPlanos()
        {
            F.DropDownButton Lista(string icone, string texto, string dica, Func<IEnumerable<(string rotulo, Action acao)>> itens)
            {
                var b = new F.DropDownButton { Header = texto, LargeIcon = Icone(icone, 32), Icon = Icone(icone, 16), Size = F.RibbonControlSize.Middle, ToolTip = Dica(texto, dica) };
                Tamanho(b, true);
                b.DropDownOpened += (s, e) =>
                {
                    b.Items.Clear();
                    var l = Esp == null ? null : itens().ToList();
                    if (l == null || l.Count == 0) { b.Items.Add(new F.MenuItem { Header = Esp == null ? "Só no modelo espacial" : "(vazio)", IsEnabled = false }); return; }
                    foreach (var (r, a) in l) { var mi = new F.MenuItem { Header = r }; mi.Click += (s2, e2) => a(); b.Items.Add(mi); }
                };
                return b;
            }
            IEnumerable<(string, Action)> Cotas(char eixo) => Niveis.Cotas(Esp.Json, eixo.ToString()).Select(v => (eixo.ToString().ToUpperInvariant() + " = " + Num.Fmt(v, 2) + " m", (Action)(() => Elevacao(eixo, v))));
            return Grupo("Planos",
                Lista("eixo", "Nível", "Ativa um nível: vira o plano de trabalho, mostra só o andar dele (barras entre ele e o nível de baixo) e os outros em fantasma, em planta.",
                      () => Niveis.Lista(Esp.Json).AsEnumerable().Reverse().Select(n => (n.Nome + "  (z = " + Num.Fmt(n.Z, 2) + " m)", (Action)(() => AtivarNivel(n.Z))))),
                Lista("eixo", "Elevação X", "Plano vertical X = valor (vista de lado, YZ): só as barras desse plano.", () => Cotas('x')),
                Lista("eixo", "Elevação Y", "Plano vertical Y = valor (vista de frente, XZ): só as barras desse plano.", () => Cotas('y')),
                Botao("3dcompleto", "3D completo", Completo3D, "Volta a mostrar o modelo inteiro, em isométrica.", null, true),
                Botao("niveis", "Níveis", AbrirNiveis, "Painel de níveis (pavimentos): criar, renomear, mudar a cota, apagar, detectar pela geometria, copiar pavimento para cima e gerar pilares.", null, true),
                Botao("copiarpav", "Copiar pavimento", AbrirNiveis, "Copia barras, nós e cargas de um nível para N níveis acima (painel Níveis; um Ctrl+Z desfaz).", null, true),
                Botao("pilaresniv", "Pilares entre níveis", AbrirNiveis, "Um pilar sob cada nó de um nível até o nível de baixo (painel Níveis).", null, true));
        }

        // Operação no motor (fila exclusiva + desfazer) a partir do painel.
        internal Task OperarNiveis(Func<Task> op, string msg) => Espacial(d => d.Executar(op), msg);

        internal async Task Lote(JsonArray itens, string msg)
        {
            if (itens.Count == 0) { MostrarToast("Nada para criar.", true); return; }
            await OperarNiveis(() => Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens }), msg);
            janelaNiveis?.Montar();
        }
    }

    // ================= painel Níveis =================
    public class JanelaNiveis : JanelaEscura
    {
        readonly MainWindow w;
        readonly Border host = new Border();
        readonly TextBlock status = UiAcoes.Status();
        double? escolhido;          // nível das ações de baixo (copiar, pilares)
        TextBox tVezes, tAltura;

        public JanelaNiveis(MainWindow w)
        {
            this.w = w;
            Title = "Níveis"; Width = 640; Height = 620; MinWidth = 560; MinHeight = 420; ShowInTaskbar = false;
            var sv = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 16, 8) };
            Content = UiAcoes.Montar(this, UiAcoes.Cabecalho("Níveis", null, "Pavimentos do modelo espacial. Ativar = plano de trabalho em planta, só com o andar do nível (barras entre ele e o de baixo); o resto fica em fantasma."),
                sv, UiAcoes.Rodape(status, B("3D completo", w.Completo3D, false, "Mostra o modelo inteiro de novo."), B("Fechar", Close)));
            Montar();
        }

        JsonObject Json => w.EspacialAtivo?.Json;

        internal void Montar()
        {
            var sp = new StackPanel();
            var j = Json;
            if (j == null) { host.Child = UiPlaca.Tx("Abra um modelo espacial.", 12, TemaPlant.BTexto2); return; }
            var l = Niveis.Lista(j);
            if (escolhido == null || !l.Any(n => Math.Abs(n.Z - escolhido.Value) <= Niveis.Tol)) escolhido = l.Last().Z;
            var cotas = Niveis.Cotas(j, "z");
            var detectar = UiPlaca.Botao("Detectar pela geometria", "busca", Detectar, "Cria um nível em cada cota de nós que ainda não tem nível (" + cotas.Count + " cotas).");
            sp.Children.Add(UiPlaca.TituloSecao("Níveis (" + l.Count + ")", detectar, 0));
            var cab = new Grid { Margin = new Thickness(0, 4, 0, 2) }; Colunas(cab);
            int c0 = 0;
            foreach (var t in new[] { "Nome", "Cota z", "Altura até o próximo", "", "" }) Cel(cab, UiPlaca.Tx(t, 11, TemaPlant.BTexto2, FontWeights.SemiBold), c0++);
            sp.Children.Add(cab);
            foreach (var n in l.AsEnumerable().Reverse())
            {
                var nv = n;
                bool sel = Math.Abs(n.Z - escolhido.Value) <= Niveis.Tol;
                var g = new Grid { Margin = new Thickness(0, 4, 0, 0) }; Colunas(g);
                var linha = new Border { Child = g, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(4), Background = sel ? TemaPlant.BSup3 : Brushes.Transparent };
                var tn = new TextBox { Text = n.Nome, Height = 26, IsReadOnly = n.Terreo, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = n.Terreo ? TemaPlant.BTexto2 : TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 8, 0),
                                       ToolTip = n.Terreo ? "Térreo (z = 0): base do modelo, não é pavimento do motor." : "Nome do nível; Enter ou sair do campo grava (diafragma mantido; um Ctrl+Z desfaz)." };
                Cel(g, tn, 0);
                var bz = UiAcoes.Num(out var tz, n.Z, "m", 96, "Cota do nível (m). Enter ou sair do campo grava.");
                tz.IsReadOnly = n.Terreo; Cel(g, bz, 1);
                double? acima = Niveis.Acima(l, n.Z);
                Cel(g, UiPlaca.Tx(acima == null ? "—" : Num.Fmt(acima.Value - n.Z, 2) + " m", 12, TemaPlant.BTexto2), 2);
                var ativar = UiPlaca.Link(sel ? "Ativo" : "Ativar", () => { escolhido = nv.Z; w.AtivarNivel(nv.Z); }, "Plano de trabalho neste nível: planta só com o andar dele.", 11.5);
                Cel(g, ativar, 3);
                if (!n.Terreo)
                {
                    var rem = UiPlaca.Link("Apagar", () => _ = Gravar(() => Motor.Chamar("DELETE", "/api/modelo/pavimento/" + Uri.EscapeDataString(nv.Nome)), "Nível apagado: " + nv.Nome + "."), "Apaga o nível (os nós e barras ficam).", 11.5);
                    rem.Foreground = TemaPlant.BTexto2; Cel(g, rem, 4);
                    bool gravado = false;   // Enter grava e o LostFocus que vem junto (a linha é remontada) não grava de novo
                    void Commit()
                    {
                        if (gravado) return;
                        string nome = tn.Text.Trim();
                        if (!UiAcoes.Ler(tz, out double z)) return;
                        bool mudouNome = nome != "" && nome != nv.Nome, mudouZ = Math.Abs(z - nv.Z) > 1e-9;
                        if (!mudouNome && !mudouZ) return;
                        gravado = true;
                        if (mudouNome)   // um PUT com o pavimento renomeado (diafragma mantido): atômico, um Ctrl+Z
                            _ = GravarModelo(m => Niveis.Renomear(m, nv.Nome, nome, mudouZ ? z : (double?)null), "Nível renomeado: " + nv.Nome + " → " + nome + ".");
                        else _ = Gravar(() => Motor.Chamar("PATCH", "/api/modelo/pavimento/" + Uri.EscapeDataString(nv.Nome), new JsonObject { ["z"] = z }), "Nível " + nv.Nome + ": z = " + Num.Fmt(z, 2) + " m.");
                    }
                    foreach (var tb in new[] { tn, tz })
                    {
                        tb.LostFocus += (s, e) => Commit();
                        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
                    }
                }
                linha.MouseLeftButtonDown += (s, e) => { if (escolhido != nv.Z) { escolhido = nv.Z; Montar(); } };
                sp.Children.Add(linha);
            }
            var novo = UiPlaca.Botao("+ Novo nível", null, () =>
            {
                double z = l.Max(n => n.Z) + 3; int i = l.Count(n => !n.Terreo) + 1; string nome;
                do nome = "P" + i++; while (l.Any(n => n.Nome == nome));
                _ = Gravar(() => Motor.Post("/api/modelo/pavimento", new JsonObject { ["nome"] = nome, ["z"] = z, ["diafragma"] = true }), "Nível novo: " + nome + ".");
            }, "Acrescenta um nível 3 m acima do mais alto.");
            novo.HorizontalAlignment = HorizontalAlignment.Left; novo.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(novo);

            // ações sobre o nível escolhido
            var esc = l.First(n => Math.Abs(n.Z - escolhido.Value) <= Niveis.Tol);
            double lo = Niveis.Abaixo(l, esc.Z) ?? esc.Z;
            sp.Children.Add(UiPlaca.TituloSecao("Nível escolhido: " + esc.Nome + " (z = " + Num.Fmt(esc.Z, 2) + " m)", null, 18));
            var g2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            g2.Children.Add(UiPlaca.Tx("Copiar para", 12, TemaPlant.BTexto1));
            var bv = UiAcoes.Num(out tVezes, 3, "níveis", 90, "Quantos níveis acima recebem a cópia.", 0); bv.Margin = new Thickness(8, 0, 8, 0); g2.Children.Add(bv);
            g2.Children.Add(UiPlaca.Tx("acima, altura", 12, TemaPlant.BTexto1));
            var ba = UiAcoes.Num(out tAltura, esc.Z - lo > Niveis.Tol ? esc.Z - lo : 3, "m", 90, "Altura de cada andar copiado."); ba.Margin = new Thickness(8, 0, 8, 0); g2.Children.Add(ba);
            g2.Children.Add(UiPlaca.Botao("Copiar pavimento para cima", "copiar", () => Copiar(lo, esc.Z), "Copia barras, nós e cargas do andar deste nível (entre ele e o de baixo). Um lote só no motor: um Ctrl+Z desfaz tudo."));
            sp.Children.Add(g2);
            foreach (FrameworkElement f in g2.Children) f.VerticalAlignment = VerticalAlignment.Center;
            var pil = UiPlaca.Botao("Gerar pilares entre níveis", "barra", () =>
            {
                if (lo >= esc.Z - Niveis.Tol) { status.Text = "✕ Não há nível abaixo de " + esc.Nome + "."; return; }
                _ = w.Lote(Niveis.GerarPilares(Json, esc.Z, lo), "Pilares gerados de " + esc.Nome + " até z = " + Num.Fmt(lo, 2) + " m.");
            }, "Um pilar sob cada nó deste nível até o nível de baixo (nó novo embaixo se não houver).");
            pil.HorizontalAlignment = HorizontalAlignment.Left; pil.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(pil);
            sp.Children.Add(new TextBlock { Text = "Plano na vista: " + w.DescricaoPlano, Foreground = TemaPlant.BTexto3, FontSize = 11, Margin = new Thickness(0, 14, 0, 0) });
            host.Child = sp;
        }

        void Copiar(double lo, double z)
        {
            if (!UiAcoes.Ler(tVezes, out double nv) || nv < 1 || nv != Math.Floor(nv) || !UiAcoes.Ler(tAltura, out double h) || h <= 0) { status.Text = "✕ Níveis: inteiro ≥ 1; altura > 0."; status.Foreground = TemaPlant.BErro; return; }
            _ = w.Lote(Niveis.CopiarParaCima(Json, lo, z, (int)nv, h), "Pavimento copiado " + nv + "× para cima (h = " + Num.Fmt(h, 2) + " m).");
        }

        void Detectar()
        {
            var l = Niveis.Lista(Json);
            var itens = new JsonArray(); int i = l.Count(n => !n.Terreo);
            var nomes = l.Select(n => n.Nome).ToHashSet();
            foreach (double z in Niveis.Cotas(Json, "z").Where(z => z > Niveis.Tol && !l.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol)))
            {
                string nome; do nome = "P" + ++i; while (nomes.Contains(nome));
                nomes.Add(nome);
                itens.Add(new JsonObject { ["tipo"] = "pavimento", ["dados"] = new JsonObject { ["nome"] = nome, ["z"] = z, ["diafragma"] = true } });
            }
            _ = w.Lote(itens, "Níveis detectados pela geometria: " + itens.Count + ".");
        }

        async Task Gravar(Func<Task> op, string msg)
        {
            await w.OperarNiveis(op, msg);
            Montar();
        }

        async Task GravarModelo(Func<JsonObject, JsonObject> op, string msg)
        {
            await w.OperarEspacial(d => d.Aplicar(op), msg);
            Montar();
        }

        static void Colunas(Grid g) { foreach (double c in new[] { 180, 130, 130, 60, 60.0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c) }); }
        static void Cel(Grid g, FrameworkElement e, int c) { e.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(e, c); g.Children.Add(e); }
    }
}
