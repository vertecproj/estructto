// Tela inicial (sem documento aberto; Arquivo › Início): logo, ações principais, galeria de modelos com miniatura real do
// motor, recentes com miniatura/pino/remover e "Aprender". Tudo 3D (docs/v3/INICIO.md). Teclado: Ctrl+N, Ctrl+O, Tab/Enter.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Ui = NovoFtool.UiPlaca;

namespace NovoFtool
{
    // Eixos (X: "1, 2, 3…"; Y: "A, B, C…") e níveis (cotas) de um projeto novo. Texto "3x6 2x5" = 3 vãos de 6 m e 2 de 5 m.
    public sealed class GradeNiveis
    {
        public List<double> X = new List<double>(), Y = new List<double>();   // espaçamentos (m)
        public List<double> Z = new List<double>();                           // cotas dos níveis (m), crescentes, inclui 0
        public static List<double> Espacamentos(string s)
        {
            var r = new List<double>();
            foreach (string tok in (s ?? "").Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = tok.Trim().ToLowerInvariant();
                int i = t.IndexOf('x');
                int n = 1; string v = t;
                if (i > 0) { if (!int.TryParse(t.Substring(0, i), out n) || n < 1 || n > 200) throw new FormatException(tok); v = t.Substring(i + 1); }
                double d = double.Parse(v.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                if (!(d > 0) || d > 1000) throw new FormatException(tok);
                for (int k = 0; k < n; k++) r.Add(d);
            }
            return r;
        }
        // Níveis: alturas entre níveis ("3x3,5") → cotas acumuladas a partir de 0.
        public static List<double> Cotas(string alturas)
        {
            var z = new List<double> { 0 }; double a = 0;
            foreach (double h in Espacamentos(alturas)) { a += h; z.Add(Math.Round(a, 4)); }
            return z;
        }
        public static List<double> Acumular(List<double> esp) { var r = new List<double> { 0 }; double a = 0; foreach (double e in esp) { a += e; r.Add(Math.Round(a, 4)); } return r; }
        public static string NomeX(int i) => (i + 1).ToString();
        public static string NomeY(int j) => j < 26 ? ((char)('A' + j)).ToString() : "A" + (j - 25);
        public static string NomeZ(double z) => Math.Abs(z) < 1e-9 ? "N0" : "N" + (z > 0 ? "+" : "−") + Math.Abs(z).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        public double LargX => X.Sum(); public double LargY => Y.Sum();
        public JsonObject Json() => new JsonObject
        {
            ["x"] = new JsonArray(X.Select(v => (JsonNode)v).ToArray()), ["y"] = new JsonArray(Y.Select(v => (JsonNode)v).ToArray()),
            ["nomes_x"] = new JsonArray(Enumerable.Range(0, X.Count + 1).Select(i => (JsonNode)NomeX(i)).ToArray()),
            ["nomes_y"] = new JsonArray(Enumerable.Range(0, Y.Count + 1).Select(j => (JsonNode)NomeY(j)).ToArray()),
        };
    }

    // Miniatura isométrica (vista de +x, −y, +z) de barras + grade/níveis, como DrawingImage congelado (cache por chave).
    public static class Miniatura3D
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        public static void Esquecer(string chave) { foreach (string k in cache.Keys.Where(k => k.StartsWith(chave + "|")).ToList()) cache.Remove(k); }

        public static ImageSource Gerar(string chave, double w, double h, Func<(List<(Point3D, Point3D)> barras, List<(Point3D, Point3D)> axiais, GradeNiveis grade)> fonte)
        {
            string k = chave + "|" + w + "x" + h;
            if (cache.TryGetValue(k, out var r)) return r;
            try { var (b, a, g) = fonte(); r = Desenhar(b, a, g, w, h); } catch { r = null; }
            cache[k] = r;
            return r;
        }

        // Barras de um .json do motor (nos: {id:{x,y,z}}, barras: {id:{ni,nj,params.trelica}}).
        public static (List<(Point3D, Point3D)>, List<(Point3D, Point3D)>, GradeNiveis) DeModelo(JsonObject m)
        {
            var nos = m["nos"]?.AsObject().ToDictionary(kv => kv.Key, kv => new Point3D((double)kv.Value["x"], (double)kv.Value["y"], (double)kv.Value["z"])) ?? new Dictionary<string, Point3D>();
            var b = new List<(Point3D, Point3D)>(); var a = new List<(Point3D, Point3D)>();
            foreach (var kv in m["barras"]?.AsObject() ?? new JsonObject())
                if (nos.TryGetValue((string)kv.Value["ni"], out var p) && nos.TryGetValue((string)kv.Value["nj"], out var q))
                    (kv.Value["params"]?["trelica"] is JsonValue t && t.TryGetValue(out bool tr) && tr ? a : b).Add((p, q));
            return (b, a, GradeDe(m["config"]?["grade"] as JsonObject, m["pavimentos"]));
        }

        // Barras de POST /api/gerar-estrutura/previa (nos: {id:[x,y,z]}, barras: [{ni,nj,trelica}]).
        public static (List<(Point3D, Point3D)>, List<(Point3D, Point3D)>) DePrevia(JsonNode p)
        {
            var nos = p["nos"].AsObject().ToDictionary(kv => kv.Key, kv => { var v = kv.Value.AsArray(); return new Point3D((double)v[0], (double)v[1], (double)v[2]); });
            var b = new List<(Point3D, Point3D)>(); var a = new List<(Point3D, Point3D)>();
            foreach (var x in p["barras"].AsArray())
                if (nos.TryGetValue((string)x["ni"], out var i) && nos.TryGetValue((string)x["nj"], out var j)) ((bool)x["trelica"] ? a : b).Add((i, j));
            return (b, a);
        }

        static GradeNiveis GradeDe(JsonObject g, JsonNode pavs)
        {
            if (g == null) return null;
            var r = new GradeNiveis();
            foreach (var v in g["x"]?.AsArray() ?? new JsonArray()) r.X.Add((double)v);
            foreach (var v in g["y"]?.AsArray() ?? new JsonArray()) r.Y.Add((double)v);
            r.Z.Add(0);
            foreach (var kv in pavs as JsonObject ?? new JsonObject()) { double z = (double)kv.Value["z"]; if (Math.Abs(z) > 1e-6) r.Z.Add(z); }
            r.Z.Sort();
            return r;
        }

        public static ImageSource Desenhar(List<(Point3D a, Point3D b)> barras, List<(Point3D a, Point3D b)> axiais, GradeNiveis grade, double w, double h)
        {
            var linhasGrade = new List<(Point3D, Point3D)>(); var niveis = new List<(Point3D, Point3D)>();
            if (grade != null && (grade.X.Count > 0 || grade.Y.Count > 0))
            {
                double lx = Math.Max(grade.LargX, 1), ly = Math.Max(grade.LargY, 1), ext = 0.08 * Math.Max(lx, ly);
                foreach (double x in GradeNiveis.Acumular(grade.X)) linhasGrade.Add((new Point3D(x, -ext, 0), new Point3D(x, ly + ext, 0)));
                foreach (double y in GradeNiveis.Acumular(grade.Y)) linhasGrade.Add((new Point3D(-ext, y, 0), new Point3D(lx + ext, y, 0)));
                foreach (double z in grade.Z.Skip(1))
                {
                    var c = new[] { new Point3D(0, 0, z), new Point3D(lx, 0, z), new Point3D(lx, ly, z), new Point3D(0, ly, z) };
                    for (int i = 0; i < 4; i++) niveis.Add((c[i], c[(i + 1) % 4]));
                }
            }
            var todos = barras.Concat(axiais).Concat(linhasGrade).Concat(niveis).SelectMany(s => new[] { s.Item1, s.Item2 }).ToList();
            if (todos.Count == 0) return null;
            const double c30 = 0.8660254, s30 = 0.5;
            Point Proj(Point3D q) => new Point((q.X + q.Y) * c30, q.Z + (q.Y - q.X) * s30);
            var pts = todos.Select(Proj).ToList();
            double x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
            double m = Math.Min(w, h) * 0.09 + 3, esc = Math.Min((w - 2 * m) / Math.Max(1e-6, x1 - x0), (h - 2 * m) / Math.Max(1e-6, y1 - y0));
            double ox = (w - (x1 - x0) * esc) / 2, oy = (h - (y1 - y0) * esc) / 2;
            Point T(Point3D q) { var r = Proj(q); return new Point(ox + (r.X - x0) * esc, h - oy - (r.Y - y0) * esc); }
            GeometryGroup G(IEnumerable<(Point3D a, Point3D b)> ss) { var g = new GeometryGroup(); foreach (var s in ss) g.Children.Add(new LineGeometry(T(s.a), T(s.b))); g.Freeze(); return g; }
            double e = Math.Max(1.0, Math.Min(1.8, Math.Min(w, h) / 70));
            var pGrade = new Pen(TemaPlant.B(TemaPlant.Texto3, 110), Math.Max(0.6, e * 0.5)); pGrade.Freeze();
            var pNivel = new Pen(TemaPlant.B(TemaPlant.AcentoHover, 150), Math.Max(0.6, e * 0.5)) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }; pNivel.Freeze();
            var pBarra = new Pen(TemaPlant.BTexto1, e) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pBarra.Freeze();
            var pAx = new Pen(TemaPlant.BRealce, e * 0.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pAx.Freeze();
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
            dg.Children.Add(new GeometryDrawing(null, pGrade, G(linhasGrade)));
            dg.Children.Add(new GeometryDrawing(null, pNivel, G(niveis)));
            dg.Children.Add(new GeometryDrawing(null, pAx, G(axiais)));
            dg.Children.Add(new GeometryDrawing(null, pBarra, G(barras)));
            dg.Freeze();
            var di = new DrawingImage(dg); di.Freeze();
            return di;
        }
    }

    // Modelos (templates) da tela inicial e do novo projeto: gerador do motor + grade/níveis padrão.
    public sealed class ModeloInicial
    {
        public string Chave, Titulo, Sub, Icone;
        public string EixosX, EixosY, Niveis;         // texto padrão do passo 3 ("3x6 2x5")
        public string RotX = "Eixos X (vãos)", RotY = "Eixos Y (vãos)", RotZ = "Alturas dos níveis";
        public double G, Q;                            // cargas uniformes nas vigas (kN/m, para baixo)
        public Func<GradeNiveis, string, string, string, JsonObject> Gerador;   // (grade, material, pilar, viga) → dados do gerador; null = só grade
        public string Pilar = "W310x38.7", Viga = "W360x51", Material = "A-572 GR.50";

        static JsonArray Arr(IEnumerable<double> v) => new JsonArray(v.Select(x => (JsonNode)x).ToArray());

        public static readonly ModeloInicial[] Todos =
        {
            new ModeloInicial { Chave = "vazio", Titulo = "Vazio com grade", Sub = "só eixos e níveis, para desenhar", Icone = "grade", EixosX = "3x6", EixosY = "2x5", Niveis = "2x3,5", Gerador = null },
            new ModeloInicial { Chave = "galpao", Titulo = "Galpão", Sub = "pórticos de alma cheia, terças, contraventamento", Icone = "3d",
                EixosX = "20", EixosY = "5x6", Niveis = "6 2", RotX = "Vão do pórtico", RotY = "Módulos (espaçamento)", RotZ = "Pé-direito, acréscimo até a cumeeira",
                Pilar = "W360x51", Viga = "HP250x62", G = 1.5, Q = 1.5,
                Gerador = (g, mat, pil, vig) => new JsonObject { ["tipo"] = "galpao", ["material"] = mat, ["apoio"] = "engaste",
                    ["vao"] = g.LargX, ["altura_pilar"] = g.Z.Count > 1 ? g.Z[1] : 6, ["altura_cumeeira"] = g.Z.Count > 2 ? g.Z[2] : (g.Z.Count > 1 ? g.Z[1] : 6) + 2,
                    ["n_modulos"] = Math.Max(1, g.Y.Count), ["espacamento"] = g.Y.Count > 0 ? g.Y[0] : 6, ["contraventamento"] = true,
                    ["secoes"] = new JsonObject { ["pilar"] = pil, ["viga"] = vig, ["terca"] = new JsonObject { ["perfil"] = "Ue 250x85x25x3,00" }, ["contraventamento"] = new JsonObject { ["forma"] = "tubo_circular", ["d"] = 0.2191, ["t"] = 0.00635 } } } },
            new ModeloInicial { Chave = "edificio", Titulo = "Edifício", Sub = "pórticos espaciais, vários pavimentos", Icone = "pavimentos",
                EixosX = "2x6", EixosY = "2x5", Niveis = "3x3,5", G = 12, Q = 8,
                Gerador = (g, mat, pil, vig) => new JsonObject { ["tipo"] = "edificio", ["material"] = mat, ["apoio"] = "engaste",
                    ["vaos_x"] = Arr(g.X), ["vaos_y"] = Arr(g.Y), ["alturas"] = Arr(Alturas(g)), ["secoes"] = new JsonObject { ["pilar"] = pil, ["viga"] = vig } } },
            new ModeloInicial { Chave = "portico", Titulo = "Pórtico", Sub = "um plano de pilares e vigas", Icone = "barra",
                EixosX = "2x6", EixosY = "", Niveis = "2x3,5", RotY = "Eixos Y (nenhum: um plano)", G = 15, Q = 5,
                Gerador = (g, mat, pil, vig) => new JsonObject { ["tipo"] = "portico_plano", ["material"] = mat, ["apoio"] = "engaste",
                    ["vaos_x"] = Arr(g.X), ["alturas"] = Arr(Alturas(g)), ["secoes"] = new JsonObject { ["pilar"] = pil, ["viga"] = vig } } },
            new ModeloInicial { Chave = "trelica", Titulo = "Treliça / cobertura", Sub = "tesouras em série com terças", Icone = "3d",
                EixosX = "12", EixosY = "3x5", Niveis = "0,5 2", RotX = "Vão da tesoura", RotY = "Tesouras (espaçamento)", RotZ = "Altura nos apoios, acréscimo até a cumeeira",
                Pilar = "W150x13", Viga = "W150x13",
                Gerador = (g, mat, pil, vig) => new JsonObject { ["tipo"] = "trelicas_serie", ["material"] = mat, ["modelo"] = "pratt", ["banzo"] = "duas_aguas",
                    ["vao"] = g.LargX, ["altura"] = g.Z.Count > 1 ? g.Z[1] : 0.5, ["altura2"] = g.Z.Count > 2 ? g.Z[2] : 2.5, ["paineis"] = 6,
                    ["n"] = g.Y.Count + 1, ["espacamento"] = g.Y.Count > 0 ? g.Y[0] : 5,
                    ["secoes"] = new JsonObject { ["banzo"] = pil, ["diagonal"] = vig, ["terca"] = "Ue 150x60x20x2,65" } } },
            new ModeloInicial { Chave = "mezanino", Titulo = "Mezanino", Sub = "um piso: pilares e vigas em grelha", Icone = "tabela",
                EixosX = "3x5", EixosY = "2x4", Niveis = "3", G = 10, Q = 5,
                Gerador = (g, mat, pil, vig) => new JsonObject { ["tipo"] = "edificio", ["material"] = mat, ["apoio"] = "engaste",
                    ["vaos_x"] = Arr(g.X), ["vaos_y"] = Arr(g.Y), ["alturas"] = Arr(Alturas(g)), ["secoes"] = new JsonObject { ["pilar"] = pil, ["viga"] = vig } } },
        };

        static IEnumerable<double> Alturas(GradeNiveis g) { for (int i = 1; i < g.Z.Count; i++) yield return Math.Round(g.Z[i] - g.Z[i - 1], 4); }

        public static ModeloInicial Por(string chave) => Todos.FirstOrDefault(m => m.Chave == chave) ?? Todos[0];

        public GradeNiveis GradePadrao()
        {
            var g = new GradeNiveis { X = GradeNiveis.Espacamentos(EixosX), Y = GradeNiveis.Espacamentos(EixosY) };
            g.Z = GradeNiveis.Cotas(Niveis);
            return g;
        }

        public JsonObject Dados(GradeNiveis g, string material = null, string pilar = null, string viga = null) =>
            Gerador?.Invoke(g, material ?? Material, pilar ?? Pilar, viga ?? Viga);

        // Miniatura do modelo com a grade padrão (prévia do motor; sem motor, só a grade).
        public async Task<ImageSource> Miniatura(double w, double h)
        {
            var g = GradePadrao();
            List<(Point3D, Point3D)> b = new List<(Point3D, Point3D)>(), a = new List<(Point3D, Point3D)>();
            var d = Dados(g);
            if (d != null)
            {
                try { (b, a) = Miniatura3D.DePrevia(await DocumentoEspacial.PreviaVazia(d)); }
                catch (Exception) { return null; }   // sem motor: cartão com ícone
            }
            return Miniatura3D.Gerar("modelo-" + Chave, w, h, () => (b, a, Chave == "vazio" || Chave == "edificio" || Chave == "mezanino" || Chave == "portico" ? g : null));
        }
    }

    // Dados do diálogo de novo projeto (JanelaNovoProjeto) → Inicio.Acoes.Novo.
    public sealed class NovoProjetoDados
    {
        public ModeloInicial Modelo; public GradeNiveis Grade;
        public string Nome, Pasta, Cliente, Engenheiro, Empresa, Material, Pilar, Viga;
        public List<string> Normas = new List<string>();
        public string Arquivo => Path.Combine(Pasta, Nome + ".json");
    }

    public static class Inicio
    {
        // ponytail: adaptador entre a tela inicial e o documento espacial. O ramo wt-so3d vai expor NovoDocumento(template),
        // AbrirArquivo(path) e ArquivosRecentes: trocar só aqui depois do merge.
        public static class Acoes
        {
            public static List<(string caminho, bool fixado)> Recentes() => Config.RecentesOrdenados();
            public static void Fixar(string caminho, bool fixar) => Config.Fixar(caminho, fixar);
            public static void Remover(string caminho) => Config.RemoverRecente(caminho);
            public static void Abrir(MainWindow w, string caminho) => w.Abrir(caminho);
            public static void AbrirDialogo(MainWindow w) => w.AbrirDialogo();
            public static void ImportarCype(MainWindow w) => ComandosAnalise.AbrirCype(w);
            public static void ImportarFtool(MainWindow w) => w.ImportarDialogo();
            public static void Exemplos(MainWindow w) => w.AbrirExemplosEspaciais();
            public static Task<JsonNode> Previa(JsonObject dados) => DocumentoEspacial.PreviaVazia(dados);

            // Cria o modelo no motor (grade, níveis, barras do modelo e cargas), grava em pasta\nome.json, abre e ativa o 1º nível.
            public static async Task<bool> Novo(MainWindow w, NovoProjetoDados d)
            {
                DocumentoEspacial doc;
                var antes = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.AppStarting;
                try
                {
                    Directory.CreateDirectory(d.Pasta);
                    doc = await DocumentoEspacial.Criar(async () =>
                    {
                        await Motor.Post("/api/modelo/novo", new JsonObject { ["nome"] = d.Nome });
                        var ger = d.Modelo.Dados(d.Grade, d.Material, d.Pilar, d.Viga);
                        if (ger != null) await Motor.Post("/api/gerar-estrutura", ger);
                        var m = await Motor.Get("/api/modelo");
                        var itens = new JsonArray();
                        // níveis da grade que o gerador não criou (vazio, galpão, treliça)
                        var pavs = ((m["pavimentos"] as JsonObject)?.Select(kv => (double)kv.Value["z"]) ?? Enumerable.Empty<double>()).ToList();   // pavimentos: {id: {nome, z}}
                        foreach (double z in d.Grade.Z.Where(z => z > 1e-6 && !pavs.Any(p => Math.Abs(p - z) < 1e-3)))
                            itens.Add(new JsonObject { ["tipo"] = "pavimento", ["dados"] = new JsonObject { ["nome"] = GradeNiveis.NomeZ(z), ["z"] = z } });
                        if (ger != null && (d.Modelo.G != 0 || d.Modelo.Q != 0))
                            foreach (var kv in m["barras"].AsObject())
                            {
                                if ((string)kv.Value["params"]?["papel"] != "viga") continue;
                                foreach (var (caso, v) in new[] { ("G1", d.Modelo.G), ("Q1", d.Modelo.Q) })
                                    if (v != 0) itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = kv.Key, ["q"] = new JsonArray(0.0, 0.0, -v) } });
                            }
                        if (itens.Count > 0) await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });
                        await Motor.Chamar("PATCH", "/api/config", new JsonObject
                        {
                            ["projeto"] = new JsonObject { ["cliente"] = d.Cliente ?? "", ["engenheiro"] = d.Engenheiro ?? "", ["empresa"] = d.Empresa ?? "", ["normas"] = new JsonArray(d.Normas.Select(n => (JsonNode)n).ToArray()), ["modelo"] = d.Modelo.Chave },
                            ["grade"] = d.Grade.Json(),
                        });
                    }, d.Nome, d.Arquivo);
                    File.WriteAllText(d.Arquivo, doc.JsonFormatado());
                }
                catch (Exception ex) { if (Capturas.Ativo) throw; Caixa.Erro(w, "Não foi possível criar o projeto", ex.Message); return false; }
                finally { Mouse.OverrideCursor = antes; }
                await w.AbrirEspacial(() => Task.FromResult(doc), "abrindo " + d.Nome, d.Arquivo);
                if (w.EspacialAtivo == doc)
                {
                    double z = d.Grade.Z.Count > 1 && d.Modelo.Chave != "galpao" && d.Modelo.Chave != "trelica" ? d.Grade.Z[1] : 0;
                    w.AtivarNivel(z);   // planta do 1º nível, pronto para desenhar
                    w.Log("Projeto criado: " + d.Arquivo);
                }
                return true;
            }
        }
    }

    public partial class MainWindow
    {
        static string QuandoTxt(DateTime t)
        {
            var d = DateTime.Now.Date - t.Date;
            return d.Days == 0 ? "hoje " + t.ToString("HH:mm") : d.Days == 1 ? "ontem " + t.ToString("HH:mm") : t.ToString("dd/MM/yyyy");
        }

        static Border Quadro(ImageSource img, double w, double h, string vazioIcone = "3d")
        {
            FrameworkElement conteudo;
            if (img != null) conteudo = new Image { Source = img, Width = w, Height = h, Stretch = Stretch.None, SnapsToDevicePixels = true };
            else
            {
                var ic = (FrameworkElement)Icone(vazioIcone, 22); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center; ic.Opacity = 0.45;
                var g = new Grid { Width = w, Height = h }; g.Children.Add(ic); conteudo = g;
            }
            return new Border { Background = TemaPlant.BBgCanvas, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = conteudo, HorizontalAlignment = HorizontalAlignment.Center };
        }

        Button CartaoBotao(UIElement conteudo, Action acao, string dica, Thickness pad, string nomeAcessivel = null)
        {
            var b = new Button { Content = conteudo, Style = (Style)FindResource("CartaoInicio"), Padding = pad, ToolTip = dica };
            b.Click += (s, e) => { Bastidor.IsOpen = false; acao(); };
            System.Windows.Automation.AutomationProperties.SetName(b, nomeAcessivel ?? dica);
            return b;
        }

        // Cartão de modelo (galeria): miniatura do motor carregada depois (MiniaturasInicio), título e subtítulo.
        internal Button CartaoModelo(ModeloInicial m, Action<ModeloInicial> acao, double tw = 160, double th = 96, bool compacto = false)
        {
            var sp = new StackPanel();
            var q = Quadro(null, tw, th, m.Icone); sp.Children.Add(q);
            sp.Children.Add(new TextBlock { Text = m.Titulo, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 8, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
            if (!compacto) sp.Children.Add(new TextBlock { Text = m.Sub, FontSize = 11, Foreground = TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = CartaoBotao(sp, () => acao(m), m.Titulo + ": " + m.Sub + ". Abre o novo projeto com este modelo escolhido.", new Thickness(10), m.Titulo);
            b.Tag = m;
            miniaturasPendentes.Add((m, q, tw, th));
            return b;
        }

        readonly List<(ModeloInicial m, Border quadro, double w, double h)> miniaturasPendentes = new List<(ModeloInicial, Border, double, double)>();
        internal Task MiniaturasInicio = Task.CompletedTask;

        // Preenche as miniaturas pendentes com a prévia do motor (uma de cada vez; sem motor fica o ícone).
        internal async Task CarregarMiniaturas()
        {
            var lote = miniaturasPendentes.ToList(); miniaturasPendentes.Clear();
            foreach (var (m, q, w, h) in lote)
            {
                ImageSource img;
                try { img = await m.Miniatura(w, h); } catch (Exception) { continue; }
                if (img == null || !q.IsLoaded && !Capturas.Ativo) continue;
                q.Child = new Image { Source = img, Width = w, Height = h, Stretch = Stretch.None, SnapsToDevicePixels = true };
                if (!Capturas.Ativo) Anim.Entrar(q.Child as FrameworkElement, 0, Anim.Rapido);
            }
        }

        static ImageSource MiniaturaArquivo(string caminho, double w, double h)
        {
            if (!File.Exists(caminho) || !caminho.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            var info = new FileInfo(caminho);
            if (info.Length > 8_000_000) return null;   // ponytail: modelos enormes ficam sem miniatura (leitura síncrona)
            return Miniatura3D.Gerar(caminho + info.LastWriteTimeUtc.Ticks, w, h, () => Miniatura3D.DeModelo((JsonObject)JsonNode.Parse(File.ReadAllText(caminho))));
        }

        static Button BotaoIcone(string icone, string dica, Action acao, double tam = 14)
        {
            var b = new Button { Content = Icone(icone, tam), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(5), Cursor = Cursors.Hand, ToolTip = dica, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            b.Click += (s, e) => { e.Handled = true; acao(); };
            b.MouseEnter += (s, e) => b.Opacity = 1; b.MouseLeave += (s, e) => b.Opacity = 0.7;
            System.Windows.Automation.AutomationProperties.SetName(b, dica);
            return b;
        }

        // Linha de recente: miniatura, nome, pasta · data, pino e remover. Remonta a tela ao fixar/remover.
        Button CartaoRecente(string caminho, bool fixado, bool compacto = false, Action remontar = null)
        {
            double tw = compacto ? 56 : 72, th = compacto ? 36 : 46;
            bool existe = File.Exists(caminho);
            string nome = Path.GetFileNameWithoutExtension(caminho), pasta = Path.GetDirectoryName(caminho) ?? "";
            var dp = new DockPanel();
            var q = Quadro(existe ? MiniaturaArquivo(caminho, tw, th) : null, tw, th, existe ? "3d" : "aviso"); q.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(q, Dock.Left); dp.Children.Add(q);
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            acoes.Children.Add(BotaoIcone(fixado ? "ok" : "mais", fixado ? "Desafixar: deixa de ficar no topo dos recentes." : "Fixar no topo dos recentes.", () => { Inicio.Acoes.Fixar(caminho, !fixado); remontar?.Invoke(); }));
            acoes.Children.Add(BotaoIcone("fechar", "Remover da lista de recentes (o arquivo não é apagado).", () => { Inicio.Acoes.Remover(caminho); remontar?.Invoke(); }));
            DockPanel.SetDock(acoes, Dock.Right); dp.Children.Add(acoes);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var tit = new StackPanel { Orientation = Orientation.Horizontal };
            tit.Children.Add(new TextBlock { Text = nome, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = existe ? TemaPlant.BTexto1 : TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
            if (fixado) { var pin = Ui.Chip("fixado", Ui.Nivel.Info, "Fica no topo dos recentes."); pin.Margin = new Thickness(8, 0, 0, 0); pin.VerticalAlignment = VerticalAlignment.Center; tit.Children.Add(pin); }
            sp.Children.Add(tit);
            if (existe)
                sp.Children.Add(new TextBlock { Text = QuandoTxt(File.GetLastWriteTime(caminho)) + "  ·  " + pasta, FontSize = 11, Foreground = TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0), ToolTip = caminho });
            else
            {
                var chip = Ui.Chip("arquivo não encontrado", Ui.Nivel.Alerta, "O arquivo foi movido ou apagado.");
                chip.HorizontalAlignment = HorizontalAlignment.Left; chip.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(chip);
            }
            dp.Children.Add(sp);
            return CartaoBotao(dp, () => Inicio.Acoes.Abrir(this, caminho), (existe ? "Abre " : "Tenta abrir ") + caminho, compacto ? new Thickness(6) : new Thickness(8), nome);
        }

        static TextBlock Rotulo(string t, Thickness m) { return new TextBlock { Text = t, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = m, VerticalAlignment = VerticalAlignment.Center }; }

        static Border Tecla(string t)
        {
            return new Border
            {
                Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = t, FontSize = 10.5, Foreground = TemaPlant.BTexto2, FontFamily = TemaPlant.FonteMono }
            };
        }

        // Botão de ação principal da coluna esquerda: ícone, texto, tecla à direita; primário = acento.
        Button AcaoInicio(string icone, string texto, string sub, string tecla, Action acao, bool primario, string dica)
        {
            var dp = new DockPanel();
            var ic = (FrameworkElement)Icone(icone, 18); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ic, Dock.Left); dp.Children.Add(ic);
            if (tecla != null) { var k = Tecla(tecla); DockPanel.SetDock(k, Dock.Right); k.Margin = new Thickness(10, 0, 0, 0); dp.Children.Add(k); }
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = texto, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = primario ? TemaPlant.BTextoSobreAcento : TemaPlant.BTexto1 });
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = primario ? TemaPlant.B(TemaPlant.TextoSobreAcento, 190) : TemaPlant.BTexto3, TextTrimming = TextTrimming.CharacterEllipsis });
            dp.Children.Add(sp);
            var b = CartaoBotao(dp, acao, dica, new Thickness(12, 8, 10, 8), texto);
            if (primario) b.Style = EstiloHero();
            b.Margin = new Thickness(0, 0, 0, 6);
            return b;
        }

        // "Estructto" com o "o" final em acento (lockup da logo, docs/v3/LOGO.md).
        static TextBlock Marca(double tam, Brush cor)
        {
            var t = new TextBlock { FontSize = tam, FontWeight = FontWeights.SemiBold, Foreground = cor, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
            t.Inlines.Add("Estructt");
            t.Inlines.Add(new System.Windows.Documents.Run("o") { Foreground = TemaPlant.BAcento });
            return t;
        }

        bool inicioCompacto, inicioHookado;

        internal void MontarTelaInicial()
        {
            if (!inicioHookado)
            {
                inicioHookado = true;
                TelaInicial.SizeChanged += (s, e) =>
                {
                    bool c = TelaInicial.ActualHeight > 0 && TelaInicial.ActualHeight < 560;
                    if (c != inicioCompacto && TelaInicial.Visibility == Visibility.Visible) { inicioCompacto = c; MontarTelaInicial(); }
                    else inicioCompacto = c;
                };
            }
            bool comp = inicioCompacto;
            miniaturasPendentes.Clear();
            var raiz = new Grid();
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(comp ? 268 : 300) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- coluna esquerda: marca, ações principais, aprender ----
            var esq = new DockPanel { Margin = new Thickness(comp ? 20 : 28, comp ? 18 : 28, comp ? 20 : 24, comp ? 14 : 20) };
            var cab = new StackPanel { Margin = new Thickness(0, 0, 0, comp ? 16 : 26) };
            var lk = new StackPanel { Orientation = Orientation.Horizontal };
            lk.Children.Add(new Image { Source = TemaPlant.Selo(comp ? 32 : 48), Width = comp ? 32 : 48, Height = comp ? 32 : 48, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true });
            lk.Children.Add(Marca(comp ? 22 : 28, TemaPlant.BTexto1));
            cab.Children.Add(lk);
            cab.Children.Add(new TextBlock { Text = "Modelagem e cálculo de estruturas 3D", FontSize = 12, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 8, 0, 0) });
            cab.Children.Add(new TextBlock { Text = "NBR 8800 · NBR 6118 · NBR 14762 · kN, m", FontSize = 10.5, Foreground = TemaPlant.BTexto3, Margin = new Thickness(0, 2, 0, 0) });
            DockPanel.SetDock(cab, Dock.Top); esq.Children.Add(cab);

            var rodape = new StackPanel();
            rodape.Children.Add(Rotulo("APRENDER", new Thickness(0, 0, 0, 6)));
            foreach (var (ic, t, s, k, a, dica) in new (string, string, string, string, Action, string)[]
            {
                ("abrir", "Exemplos prontos", "galpão, edifício, treliça", null, () => Inicio.Acoes.Exemplos(this), "Abre um dos modelos de exemplo do motor numa aba nova."),
                ("ajuda", "Atalhos e ajuda", "passo a passo, sinais", "F1", MostrarAjuda, "Janela de ajuda com o passo a passo, convenções de sinais e todos os atalhos."),
                ("busca", "Paleta de comandos", "busca qualquer comando", "Ctrl+K", AbrirPaleta, "Paleta: digite o nome de um comando, perfil ou objeto do modelo."),
            })
                rodape.Children.Add(AcaoInicio(ic, t, comp ? null : s, k, a, false, dica));
            DockPanel.SetDock(rodape, Dock.Bottom); esq.Children.Add(rodape);

            var acoes = new StackPanel();
            acoes.Children.Add(AcaoInicio("novo", "Novo projeto", comp ? null : "modelo, dados do projeto, grade e níveis", "Ctrl+N", () => NovoProjeto(), true, "Cria um projeto 3D em três passos: modelo, dados e grade de eixos/níveis. O arquivo já nasce salvo na pasta escolhida."));
            acoes.Children.Add(AcaoInicio("abrir", "Abrir…", comp ? null : "modelo .json do motor", "Ctrl+O", () => Inicio.Acoes.AbrirDialogo(this), false, "Abre um projeto salvo (.json do motor)."));
            acoes.Children.Add(AcaoInicio("cype", "Importar CYPE 3D", comp ? null : "obra .ed3", null, () => Inicio.Acoes.ImportarCype(this), false, "Importa uma obra do CYPE 3D (.ed3) como projeto novo, com avisos do que não foi convertido."));
            acoes.Children.Add(AcaoInicio("importar", "Importar Ftool", comp ? null : "arquivo .ftl", null, () => Inicio.Acoes.ImportarFtool(this), false, "Importa a geometria, seções, materiais e casos de um arquivo .ftl do Ftool."));
            esq.Children.Add(acoes);
            var esqBorda = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 1, 0), Child = esq };
            raiz.Children.Add(esqBorda);

            // ---- direita: modelos e recentes ----
            var dir = new StackPanel { Margin = new Thickness(comp ? 24 : 36, comp ? 16 : 26, comp ? 24 : 36, 16) };
            var cabMod = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var dicaEnter = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            dicaEnter.Children.Add(Tecla("Tab")); dicaEnter.Children.Add(new TextBlock { Text = " escolhe  ", FontSize = 11, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
            dicaEnter.Children.Add(Tecla("Enter")); dicaEnter.Children.Add(new TextBlock { Text = " abre", FontSize = 11, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(dicaEnter, Dock.Right); cabMod.Children.Add(dicaEnter);
            cabMod.Children.Add(Rotulo("COMEÇAR COM UM MODELO", new Thickness(0)));
            dir.Children.Add(cabMod);
            double tw = comp ? 128 : 160, th = comp ? 72 : 96;
            var galeria = new WrapPanel { Margin = new Thickness(0, 0, 0, comp ? 12 : 22) };
            foreach (var m in ModeloInicial.Todos) { var c = CartaoModelo(m, mm => NovoProjeto(mm.Chave), tw, th, comp); c.Margin = new Thickness(0, 0, 10, 10); galeria.Children.Add(c); }
            dir.Children.Add(galeria);

            var rec = Inicio.Acoes.Recentes().Take(comp ? 4 : 8).ToList();
            var cabRec = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var abrirPasta = Ui.Link("Procurar…", () => Inicio.Acoes.AbrirDialogo(this), "Abre o diálogo de arquivos (Ctrl+O).", 11.5); abrirPasta.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(abrirPasta, Dock.Right); cabRec.Children.Add(abrirPasta);
            cabRec.Children.Add(Rotulo("RECENTES", new Thickness(0)));
            dir.Children.Add(cabRec);
            if (rec.Count == 0)
            {
                var vazio = new Border { Background = TemaPlant.B(TemaPlant.Surface1, 140), BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 14, 16, 14) };
                var vsp = new StackPanel { Orientation = Orientation.Horizontal };
                var iv = (FrameworkElement)Icone("abrir", 20); iv.Opacity = 0.5; iv.Margin = new Thickness(0, 0, 12, 0); iv.VerticalAlignment = VerticalAlignment.Center; vsp.Children.Add(iv);
                vsp.Children.Add(new TextBlock { Text = "Nenhum projeto ainda. Os projetos que você criar ou abrir aparecem aqui, com miniatura, pasta e data.", FontSize = 12, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                vazio.Child = vsp;
                dir.Children.Add(vazio);
            }
            else
            {
                var g = new UniformGrid { Columns = 2 };
                foreach (var (c, fx) in rec) { var cartao = CartaoRecente(c, fx, comp, MontarTelaInicial); cartao.Margin = new Thickness(0, 0, 10, 6); g.Children.Add(cartao); }
                dir.Children.Add(g);
            }
            var sv = new ScrollViewer { Content = dir, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(sv, 1); raiz.Children.Add(sv);

            ConteudoInicio.Content = raiz;
            Anim.Escalonar(dir);
            MiniaturasInicio = CarregarMiniaturas();
        }

        // Novo projeto (Ctrl+N, "+", Arquivo › Novo, cartões da tela inicial): diálogo em 3 passos, cria e abre já salvo.
        internal void NovoProjeto(string modelo = null)
        {
            Bastidor.IsOpen = false;
            var j = new JanelaNovoProjeto(this, modelo) { Owner = this };
            if (j.ShowDialog() == true) _ = Inicio.Acoes.Novo(this, j.Dados);
        }

        static Style estiloHero;
        internal static Style EstiloHero()
        {
            if (estiloHero != null) return estiloHero;
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "b");
            bd.SetValue(Border.BackgroundProperty, TemaPlant.BAcento);
            bd.SetValue(Border.BorderBrushProperty, TemaPlant.BAcento);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); bd.AppendChild(cp);
            t.VisualTree = bd;
            var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.BAcentoHover, "b"));
            t.Triggers.Add(hov);
            var foc = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            foc.Setters.Add(new Setter(Border.BorderBrushProperty, TemaPlant.BTexto1, "b"));
            t.Triggers.Add(foc);
            var prs = new Trigger { Property = Button.IsPressedProperty, Value = true };
            prs.Setters.Add(new Setter(Border.BackgroundProperty, TemaPlant.BAcentoPressed, "b"));
            t.Triggers.Add(prs);
            estiloHero = new Style(typeof(Button));
            estiloHero.Setters.Add(new Setter(Control.TemplateProperty, t));
            estiloHero.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            estiloHero.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return estiloHero;
        }

        // ponytail: legado 2D usado só pela JanelaAssistente (Janelas.cs); sai junto com ela no merge do wt-so3d.
        internal static List<(string titulo, string sub, string tipo, Func<Portico.Modelo> modelo)> AssistentesInicio()
        {
            return new List<(string, string, string, Func<Portico.Modelo>)>
            {
                ("Viga contínua", "vãos e apoios", "viga", () => Assistentes.Viga(new List<double> { 5, 5, 5 }, -10, false, "", "")),
                ("Pórtico", "vários andares", "portico", () => Assistentes.Portico(new List<double> { 5, 5 }, new List<double> { 3.5, 3 }, true, -15, 5, "", "", "")),
                ("Treliça", "Pratt, Howe, Warren", "trelica", () => Assistentes.Trelica("Pratt", 12, 2, 6, -5, "", "")),
                ("Galpão", "duas águas", "galpao", () => Assistentes.Galpao(12, 5, 2, -8, true, "", "", "")),
            };
        }
    }

    // ponytail: miniatura 2D legada (JanelaAssistente); sai com o wt-so3d.
    public static class Miniatura
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();

        public static ImageSource Gerar(string chave, Func<Portico.Modelo> fonte, double w, double h)
        {
            string k = chave + "|" + w + "x" + h;
            if (cache.TryGetValue(k, out var r)) return r;
            Portico.Modelo m;
            try { m = fonte(); } catch { return null; }
            if (m == null || m.Nos.Count == 0) return null;
            var segs = m.Barras.Select(b => (m.NoPorId(b.N1), m.NoPorId(b.N2))).Where(p => p.Item1 != null && p.Item2 != null)
                .Select(p => (new Point3D(p.Item1.X, 0, p.Item1.Y), new Point3D(p.Item2.X, 0, p.Item2.Y))).ToList();
            r = Miniatura3D.Desenhar(segs, new List<(Point3D, Point3D)>(), null, w, h);
            cache[k] = r;
            return r;
        }
    }
}
