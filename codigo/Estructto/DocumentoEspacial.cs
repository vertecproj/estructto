// Documento do Estructto (docs/MODELO-ESPACIAL.md): o ÚNICO tipo de documento. A fonte da verdade é o modelo do motor Python
// (o JSON de codigo/motor, ESPEC §5). Abre .estructto (novo: contêiner JSON; antigo: XML 2D, convertido ao abrir), .ftl e
// .portico (convertidos), .json do motor, obras .ed3 do CYPE e os exemplos do motor; gera edifício, galpão e pórtico 3D;
// mostra na Vista3D; edita nó/barra por PATCH /api/modelo/{tipo}/{id}; desfazer/refazer pelo motor; F5 = POST /api/calcular.
// Salva o contêiner .estructto (modelo do motor + placas de base + grade estrutural) ou só o JSON do motor (.json).
// O servidor guarda um modelo só: antes de cada operação o documento ativo se recoloca lá (Garantir).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using Portico;

namespace NovoFtool
{
    // Nó ou barra do motor escolhido na vista (mesma instância entre recargas: a Vista3D compara por referência).
    public sealed class RefMotor { public string Tipo, Id; }

    // Linhas das tabelas no modelo espacial (ids do motor são texto; 3 coordenadas).
    public class LinhaEsp { [System.ComponentModel.Browsable(false)] public RefMotor Obj { get; set; } }

    public class LinhaNoEsp : LinhaEsp
    {
        [System.ComponentModel.DisplayName("Nó")] public string Id { get; set; }
        [System.ComponentModel.DisplayName("X (m)")] public string X { get; set; }
        [System.ComponentModel.DisplayName("Y (m)")] public string Y { get; set; }
        [System.ComponentModel.DisplayName("Z (m)")] public string Z { get; set; }
        [System.ComponentModel.DisplayName("Apoio")] public string Apoio { get; set; }
        [System.ComponentModel.DisplayName("ux (mm)")] public string DX { get; set; }
        [System.ComponentModel.DisplayName("uy (mm)")] public string DY { get; set; }
        [System.ComponentModel.DisplayName("uz (mm)")] public string DZ { get; set; }
    }

    public class LinhaBarraEsp : LinhaEsp
    {
        [System.ComponentModel.DisplayName("Barra")] public string Id { get; set; }
        [System.ComponentModel.DisplayName("Nós")] public string Nos { get; set; }
        [System.ComponentModel.DisplayName("Perfil")] public string Perfil { get; set; }
        [System.ComponentModel.DisplayName("Material")] public string Material { get; set; }
        [System.ComponentModel.DisplayName("L (m)")] public string L { get; set; }
        [System.ComponentModel.DisplayName("N mín/máx (kN)")] public string N { get; set; }
        [System.ComponentModel.DisplayName("Vz mín/máx (kN)")] public string Vz { get; set; }
        [System.ComponentModel.DisplayName("My mín/máx (kN·m)")] public string My { get; set; }
    }

    public class LinhaVerifEsp : LinhaEsp
    {
        [System.ComponentModel.DisplayName("Barra")] public string Id { get; set; }
        [System.ComponentModel.DisplayName("Perfil")] public string Perfil { get; set; }
        [System.ComponentModel.DisplayName("Situação")] public string Situacao { get; set; }
        [System.ComponentModel.DisplayName("Aproveitamento")] public string Ap { get; set; }
        [System.ComponentModel.DisplayName("Limitado por")] public string Critico { get; set; }
        [System.ComponentModel.DisplayName("Combinação")] public string Combinacao { get; set; }
        [System.ComponentModel.Browsable(false)] public double ApNum { get; set; } = -1;
        [System.ComponentModel.Browsable(false)] public bool Pass { get; set; } = true;
        [System.ComponentModel.Browsable(false)] public System.Windows.Media.Brush CorAp { get; set; } = TemaPlant.BTextoMudo;
        [System.ComponentModel.Browsable(false)] public Visibility BarraVis { get; set; } = Visibility.Collapsed;
        [System.ComponentModel.Browsable(false)] public string SitTxt { get; set; } = "";
        [System.ComponentModel.Browsable(false)] public System.Windows.Media.Brush SitFg { get; set; } = TemaPlant.BTexto2;
        [System.ComponentModel.Browsable(false)] public System.Windows.Media.Brush SitBg { get; set; } = TemaPlant.BSup2;
        [System.ComponentModel.Browsable(false)] public System.Windows.Media.Brush SitBd { get; set; } = TemaPlant.BBordaSutil;
    }

    public partial class DocumentoEspacial   // partial: desenho com o mouse em Vista3D.cs (DesenharBarras, Vazio)
    {
        public JsonObject Json;              // modelo do motor (cópia do último GET /api/modelo)
        public string Arquivo, Titulo;       // Arquivo: .json do motor (null = ainda não salvo)
        public List<string> Avisos = new List<string>();
        public JsonNode Calculo;             // POST /api/calcular (null = sem cálculo)
        public string CombDiagrama;          // combinação dos diagramas e da deformada; null = envoltória (esforços ELU, deslocamentos ELS)
        public string CasoAtivo;             // caso das cargas lançadas no painel
        public bool TemHistorico, HistoricoPerdido;   // desfazer do motor: só do documento ativo (ver Garantir)
        public JsonArray Validacao = new JsonArray();  // GET /api/validar do último Recarregar
        // Placas de base por nó do contêiner antigo: só migração (as placas vão para os grupos de ligação do motor; não gravar aqui).
        // O ponto cardinal fica em params.cardinal da barra e a grade estrutural em config.grade: ambos no motor (Ctrl+Z desfaz).
        public Dictionary<string, PlacaBase> Placas = new Dictionary<string, PlacaBase>();
        public string Origem;                          // arquivo antigo convertido ao abrir (null = nativo)
        public string Chave = Guid.NewGuid().ToString("N");   // cópia de recuperação deste documento (Config.SalvarRecuperacao)
        public Dictionary<string, JsonNode> PiorPorBarra = new Dictionary<string, JsonNode>();
        JsonObject esforcos, desl;
        readonly Dictionary<string, RefMotor> refs = new Dictionary<string, RefMotor>();
        static DocumentoEspacial noMotor;
        readonly string tmp = "_espacial_" + Guid.NewGuid().ToString("N") + ".json";   // por documento: dois Estructto não pisam um no outro

        internal string Troca => Path.Combine(Motor.Pasta, tmp);
        // Ao fechar a aba: apaga o arquivo de troca da pasta de trabalho.
        public void Descartar() { try { File.Delete(Troca); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        public bool Calculado => Calculo != null;
        public string Nome => Arquivo != null ? Path.GetFileName(Arquivo) : Titulo ?? "Modelo";
        static string Url(string s) => Uri.EscapeDataString(s);
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public RefMotor Ref(string tipo, string id)
        {
            string k = tipo + "\n" + id;
            if (!refs.TryGetValue(k, out var r)) refs[k] = r = new RefMotor { Tipo = tipo, Id = id };
            return r;
        }

        // Recoloca este modelo no servidor (por arquivo na pasta de trabalho: "abrir" zera as pilhas de desfazer).
        // ponytail: trocar de aba entre dois modelos espaciais perde o desfazer do outro; guardar as pilhas se fizer falta.
        // Recolocar zera a pilha de desfazer do servidor: HistoricoPerdido avisa a janela.
        // Sem trava: só dentro de Motor.Exclusivo (que não aninha). De fora, Ativar.
        async Task Garantir()
        {
            await Motor.Get("/api/versao");
            if (noMotor == this && Motor.Dono == this) return;
            File.WriteAllText(Path.Combine(Motor.Pasta, tmp), Json.ToJsonString());
            await Motor.Post("/api/modelo/abrir", new JsonObject { ["caminho"] = tmp });
            noMotor = this; Motor.Dono = this;
            if (TemHistorico) HistoricoPerdido = true;
            TemHistorico = false;
        }

        // Ao ativar a aba: recoloca o modelo no motor na fila das outras operações (F5 logo depois espera, não é pulado).
        public Task Ativar() => Motor.Exclusivo(Garantir);

        async Task Recarregar()
        {
            Json = (JsonObject)await Motor.Get("/api/modelo");
            Calculo = null; esforcos = desl = null; PiorPorBarra.Clear();
            try { Validacao = await Motor.Get("/api/validar") as JsonArray ?? new JsonArray(); } catch (MotorErro) { Validacao = new JsonArray(); }
            var casos = (Json["casos"] as JsonObject)?.Select(x => x.Key).ToList() ?? new List<string>();
            if (!casos.Contains(CasoAtivo)) CasoAtivo = casos.FirstOrDefault();
        }

        // O modelo que `preparar` deixou no servidor vira um documento novo (sem pilha de desfazer).
        internal static async Task<DocumentoEspacial> Criar(Func<Task> preparar, string titulo, string arquivo)
        {
            var d = new DocumentoEspacial { Titulo = titulo, Arquivo = arquivo };
            await Motor.Exclusivo(async () =>
            {
                noMotor = null;
                await preparar();
                await d.Recarregar();
                // o modelo do motor já é o deste documento: não reabre (~0,3 s); só zera o desfazer, como o abrir fazia
                try { await Motor.Post("/api/historico/limpar"); noMotor = d; Motor.Dono = d; d.TemHistorico = false; }
                catch (MotorErro ex) when (ex.Status == 404) { await d.Garantir(); }   // motor antigo sem a rota: reabre como antes
            });
            return d;
        }

        public static Task<DocumentoEspacial> Exemplo(string nome) => Criar(() => Motor.Post("/api/exemplos/" + Url(nome), new JsonObject()), nome, null);

        public static async Task<DocumentoEspacial> AbrirArquivo(string caminho)
        {
            string ext = Path.GetExtension(caminho).ToLowerInvariant();
            if (ext == ".ftl")
            {   // E2E-10: o aviso do Ftl (apoios e cargas não lidos) vai para os avisos do documento (log, painel de avisos e toast ao abrir)
                var df = await DeModelo2D(Ftl.Importar(caminho, out string avFtl), caminho);
                df.Avisos.AddRange(avFtl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                return df;
            }
            if (ext == ".estructto" || ext == ".portico")
            {
                string txt = File.ReadAllText(caminho);
                if (txt.TrimStart().StartsWith("<")) return await DeModelo2D(Modelo.DeXml(txt), caminho);   // formato antigo (XML do Nucleo.cs)
                var c = (JsonObject)JsonNode.Parse(txt);
                if (c["modelo"] is JsonObject) return await DoContainer(c, caminho);
            }
            if (caminho.EndsWith(".ed3", StringComparison.OrdinalIgnoreCase))
            {
                JsonNode r = null;
                var d = await Criar(async () => r = await Motor.Post("/api/importar_cype", new JsonObject
                {
                    ["nome"] = Path.GetFileName(caminho), ["conteudo_base64"] = Convert.ToBase64String(File.ReadAllBytes(caminho)),
                }), Path.GetFileNameWithoutExtension(caminho) + " (CYPE)", null);
                foreach (var a in r?["avisos"]?.AsArray() ?? new JsonArray()) d.Avisos.Add(a is JsonValue ? (string)a : a?.ToJsonString());
                return d;
            }
            var doc = new DocumentoEspacial { Arquivo = caminho, Json = (JsonObject)JsonNode.Parse(File.ReadAllText(caminho)) };
            await Motor.Exclusivo(async () =>
            {
                await doc.Garantir();      // o motor valida ao abrir
                await doc.Recarregar();
            });
            return doc;
        }

        // ---------- formato do arquivo ----------
        // .estructto (v3) = {"formato": "estructto", "versao": 3, "modelo": <JSON do motor>, "placas": {nó: PlacaBase}}. A grade
        // (config.grade) e o ponto cardinal (params.cardinal) vão dentro do modelo. Contêiner antigo com "grade"/"cardinal" ao
        // lado do modelo: migrado ao abrir (Migrar). O .estructto antigo em XML (Nucleo.cs) é convertido ao abrir (DeModelo2D).
        public string ParaContainer()
        {
            var placas = new JsonObject();
            foreach (var kv in Placas) placas[kv.Key] = JsonSerializer.SerializeToNode(kv.Value);
            var c = new JsonObject { ["formato"] = "estructto", ["versao"] = 3, ["modelo"] = Json.DeepClone(), ["placas"] = placas };
            return c.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        // Contêiner antigo: "grade" (posições absolutas) vira config.grade e "cardinal" {barra: 1..10} vira params.cardinal.
        // Valor inválido (texto, fora de 1..10, barra que não existe) é ignorado com aviso, sem travar a abertura.
        internal static void Migrar(JsonObject c, JsonObject modelo, List<string> avisos)
        {
            if (c["grade"] is JsonObject g && modelo["config"]?["grade"] == null)
            {
                double[] A(string k) => (g[k] as JsonArray)?.Select(GradeEstrutural.Num).Where(v => v != null).Select(v => v.Value).ToArray() ?? new double[0];
                var xs = A("x"); var ys = A("y");
                if (xs.Length > 0 || ys.Length > 0)
                {
                    var cfg = modelo["config"] as JsonObject ?? (JsonObject)(modelo["config"] = new JsonObject());
                    cfg["grade"] = GradeEstrutural.Json(xs, ys);
                }
            }
            foreach (var kv in c["cardinal"] as JsonObject ?? new JsonObject())
            {
                var b = modelo["barras"]?[kv.Key] as JsonObject;
                if (b == null || !(kv.Value is JsonValue v) || !v.TryGetValue(out int n) || n < 1 || n > 10)
                {
                    avisos.Add("ponto cardinal da barra " + kv.Key + " inválido (" + (kv.Value?.ToJsonString() ?? "null") + "): ignorado, fica o centro");
                    continue;
                }
                if (n == 5) continue;
                var p = b["params"] as JsonObject ?? (JsonObject)(b["params"] = new JsonObject());
                p["cardinal"] = n;
            }
        }

        // params.cardinal da barra (1..10; sem ou inválido = 5, centro).
        public static int CardinalDe(JsonNode barra) =>
            barra?["params"]?["cardinal"] is JsonValue v && v.TryGetValue(out int n) && n >= 1 && n <= 10 ? n : 5;

        static async Task<DocumentoEspacial> DoContainer(JsonObject c, string caminho)
        {
            var d = new DocumentoEspacial { Arquivo = caminho, Json = (JsonObject)c["modelo"].DeepClone() };
            Migrar(c, d.Json, d.Avisos);
            foreach (var kv in c["placas"] as JsonObject ?? new JsonObject())
                try { var p = kv.Value.Deserialize<PlacaBase>(); if (p != null) d.Placas[kv.Key] = p; } catch (JsonException) { d.Avisos.Add("placa do nó " + kv.Key + " ilegível: ignorada"); }
            await Motor.Exclusivo(async () => { await d.Garantir(); await d.Recarregar(); });
            return d;
        }

        // Contêiner lido de texto (recuperação automática): sem arquivo, marcado como alterado por quem chama.
        public static async Task<DocumentoEspacial> DeTexto(string txt, string titulo)
        {
            var d = await DoContainer((JsonObject)JsonNode.Parse(txt), null);
            d.Titulo = titulo;
            return d;
        }

        // Modelo 2D antigo (Nucleo.cs: .estructto XML, .ftl, .portico, assistentes) -> documento 3D pela mesma conversão do
        // motor (MotorModelo.Enviar: X -> X, Y do 2D -> Z, fora do plano travado). As placas de base vêm junto (nó N -> "nN").
        public static async Task<DocumentoEspacial> DeModelo2D(Modelo m, string origem, string titulo = null)
        {
            foreach (Barra b in m.Barras) SecoesExtras.Garantir(b.Secao);
            titulo ??= origem != null ? Path.GetFileNameWithoutExtension(origem) : "Modelo";
            var d = await Criar(() => MotorModelo.Enviar(m), titulo, null);
            d.Origem = origem;
            foreach (No n in m.Nos) if (n.Placa != null) d.Placas["n" + n.Id] = n.Placa.Copiar();
            return d;
        }

        // Modelo devolvido por POST /api/importar_cype (JanelaCype.Importado): vira um documento novo.
        public static async Task<DocumentoEspacial> DoModelo(JsonObject modelo, string titulo, JsonNode avisos)
        {
            var d = new DocumentoEspacial { Titulo = titulo, Json = modelo };
            foreach (var a in avisos?.AsArray() ?? new JsonArray()) d.Avisos.Add(a is JsonValue ? (string)a : a?.ToJsonString());
            await Motor.Exclusivo(async () =>
            {
                await d.Garantir();
                await d.Recarregar();
            });
            return d;
        }

        // Gerador do motor + cargas uniformes G e Q (kN/m, para baixo) em todas as barras com papel "viga".
        public static Task<DocumentoEspacial> Gerar(JsonObject dados, string titulo, double g, double q) => Criar(async () =>
        {
            await Motor.Post("/api/modelo/novo", new JsonObject { ["nome"] = titulo });
            await Motor.Post("/api/gerar-estrutura", dados);
            if (g == 0 && q == 0) return;
            var m = await Motor.Get("/api/modelo");
            var itens = new JsonArray();
            foreach (var kv in m["barras"].AsObject())
            {
                if ((string)kv.Value["params"]?["papel"] != "viga") continue;
                foreach (var (caso, v) in new[] { ("G1", g), ("Q1", q) })
                    if (v != 0) itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = kv.Key, ["q"] = new JsonArray(0.0, 0.0, -v) } });
            }
            await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });
        }, titulo, null);

        public Task Editar(string tipo, string id, JsonObject campos) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            await Motor.Chamar("PATCH", "/api/modelo/" + tipo + "/" + Url(id), campos);
            TemHistorico = true;
            await Recarregar();
        });

        // Operação composta no motor (PlanosTrabalho.cs: níveis, lote): fila exclusiva, conta no desfazer, recarrega.
        public Task Executar(Func<Task> op) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            await op();
            TemHistorico = true;
            await Recarregar();
        });

        // POST /api/modelo/{tipo}; devolve o id (ou nome) criado.
        public Task<string> Adicionar(string tipo, JsonObject dados) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            var r = await Motor.Post("/api/modelo/" + tipo, dados);
            TemHistorico = true;
            await Recarregar();
            return (string)(r?["id"] ?? r?["nome"]);
        });

        // DELETE /api/modelo/{tipo}/{id}?cascata=true (nó leva barras e cargas; barra leva as cargas).
        public Task Remover(string tipo, string id) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            await Motor.Chamar("DELETE", "/api/modelo/" + tipo + "/" + Url(id) + "?cascata=true", new JsonObject());
            TemHistorico = true;
            await Recarregar();
        });

        // Perfil do catálogo do motor (/api/catalogo) que ainda não está no modelo entra antes de ser usado.
        public async Task GarantirSecao(string nome)
        {
            if (Json["secoes"]?[nome] == null) await Adicionar("secao", new JsonObject { ["perfil"] = nome });
        }

        public string NomeDiagrama => CombDiagrama ?? "envoltória ELU (deslocamentos: ELS)";
        public IEnumerable<string> Combinacoes => Calculo?["combinacoes"]?.AsArray().Select(x => (string)x["nome"]) ?? new string[0];

        // Troca a combinação dos diagramas (null = envoltória) sem recalcular.
        public async Task MudarDiagrama(string comb)
        {
            CombDiagrama = comb;
            if (!Calculado) return;
            await Motor.Exclusivo(async () =>
            {
                await Garantir();
                await CarregarDiagramas();
            });
        }

        async Task CarregarDiagramas()
        {
            string q = CombDiagrama == null ? "" : "&combinacao=" + Url(CombDiagrama);
            esforcos = (await Motor.Get("/api/resultados?alvo=esforcos" + q)) as JsonObject;
            desl = (await Motor.Get("/api/resultados?alvo=deslocamentos" + q)) as JsonObject;
        }

        // false = pilha vazia
        public Task<bool> Trocar(bool desfazer) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            try { await Motor.Post(desfazer ? "/api/desfazer" : "/api/refazer", new JsonObject()); }
            catch (MotorErro e) when (e.Status == 409) { return false; }
            await Recarregar();
            return true;
        });

        public Task Calcular() => Motor.Exclusivo(async () =>
        {
            await Garantir();
            var c = await Motor.Post("/api/calcular", new JsonObject());
            PiorPorBarra.Clear();
            foreach (var l in c["verificacao"]?.AsArray() ?? new JsonArray())
            {
                string b = (string)l["barra"];
                if (!PiorPorBarra.TryGetValue(b, out var at) || Taxa(l) > Taxa(at)) PiorPorBarra[b] = l;
            }
            Calculo = c;
            if (CombDiagrama != null && !Combinacoes.Contains(CombDiagrama)) CombDiagrama = null;
            await CarregarDiagramas();
        });

        public double[] Desl(string no) => Vetor(desl?[no])?.Take(3).ToArray();
        public double[] Esforco(string barra, string comp) => Vetor(esforcos?[barra]?[comp]);

        public static double Taxa(JsonNode l) => l?["taxa"] == null ? -1 : (double)l["taxa"];

        public string Resumo()
        {
            var j = Json; int nn = j["nos"].AsObject().Count, nb = j["barras"].AsObject().Count;
            string s = "Modelo espacial (motor) — " + Pl.N(nn, "nó", "nós") + " · " + Pl.N(nb, "barra", "barras");
            if (!Calculado) return s + " · F5 calcula (combinações NBR 8681, verificação por norma).";
            int ncomb = Calculo["combinacoes"]?.AsArray().Count ?? 0;
            int reprov = PiorPorBarra.Values.Count(l => l["ok"] != null && !(bool)l["ok"]);
            var p = PiorPorBarra.Values.OrderByDescending(Taxa).FirstOrDefault();
            return s + " · " + Pl.N(ncomb, "combinação", "combinações") + " · " + (reprov == 0 ? "todas as barras passam" : "⚠ " + Pl.N(reprov, "barra não passa", "barras não passam")) +
                   (p != null && Taxa(p) >= 0 ? " · pior: " + (string)p["barra"] + " " + Num.Fmt(Taxa(p) * 100, 0) + "% (" + (string)p["criterio"] + ")" : "") +
                   " · diagramas: " + NomeDiagrama;
        }

        public string JsonFormatado() => Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // Entrada neutra da Vista3D. O eixo local y da Vista3D é o z local do motor (mesma regra de solver.eixos_locais),
        // então os diagramas são N, Vz e My (My < 0 no vão de viga com gravidade = desenhado para baixo, lado tracionado).
        // ponytail: estações tratadas como igualmente espaçadas (carga pontual na barra desloca um pouco o desenho).
        public Modelo3D ParaVista()
        {
            var r = new Modelo3D();
            var ids = new Dictionary<string, int>();
            foreach (var kv in Json["nos"].AsObject())
            {
                var n = kv.Value; int id = ids.Count + 1; ids[kv.Key] = id;
                var ap = n["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                r.Nos.Add(new No3D { Id = id, X = (double)n["x"], Y = (double)n["y"], Z = (double)n["z"], Apoios = ap.Length == 6 ? ap : new bool[6], Origem = Ref("no", kv.Key),
                                     Desl = Desl(kv.Key) });
            }
            int ib = 0; var idsB = new Dictionary<string, Barra3D>();
            foreach (var kv in Json["barras"].AsObject())
            {
                var b = kv.Value;
                if (!ids.TryGetValue((string)b["ni"], out int ni) || !ids.TryGetValue((string)b["nj"], out int nj)) continue;
                var b3 = new Barra3D { Id = ++ib, Ni = ni, Nj = nj, Secao = (string)b["secao"] ?? "", Rot = (double)(b["rot"] ?? 0.0) * 180 / Math.PI, Origem = Ref("barra", kv.Key),
                                   Cardinal = CardinalDe(b) };
                var e = esforcos?[kv.Key];
                if (e != null) { b3.N = Vetor(e["N"]); b3.V = Vetor(e["Vz"]); b3.M = Vetor(e["My"]); }
                if (PiorPorBarra.TryGetValue(kv.Key, out var l) && Taxa(l) >= 0) b3.Aprov = Taxa(l);
                r.Barras.Add(b3);
                idsB[kv.Key] = b3;
            }
            CargasParaVista(r, ids, idsB);
            return r;
        }

        // Cargas do caso ativo (Ações › Caso ativo) para as setas da Vista3D: nodais (só as forças), pontuais (a em m -> fração
        // da barra) e distribuídas (q em ni, q2 em nj; sem q2 = uniforme). Tudo global, como no motor.
        void CargasParaVista(Modelo3D r, Dictionary<string, int> nos, Dictionary<string, Barra3D> barras)
        {
            if (CasoAtivo == null) return;
            IEnumerable<JsonNode> Do(string col) => (Json[col] as JsonArray ?? new JsonArray()).Where(c => (string)c?["caso"] == CasoAtivo);
            Vector3D V(JsonNode n) { var a = n as JsonArray; return a == null || a.Count < 3 ? new Vector3D() : new Vector3D((double)a[0], (double)a[1], (double)a[2]); }
            foreach (var c in Do("cargas_nodais"))
                if (nos.TryGetValue((string)c["no"], out int id)) r.Cargas.Add(new Carga3D { No = id, F = V(c["F"]) });
            foreach (var c in Do("cargas_pontuais"))
                if (barras.TryGetValue((string)c["barra"], out var b))
                {
                    var a = r.Nos[b.Ni - 1]; var z = r.Nos[b.Nj - 1];
                    double L = Math.Sqrt((z.X - a.X) * (z.X - a.X) + (z.Y - a.Y) * (z.Y - a.Y) + (z.Z - a.Z) * (z.Z - a.Z));
                    r.Cargas.Add(new Carga3D { Barra = b.Id, T = L < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (double)(c["a"] ?? 0.0) / L)), F = V(c["F"]) });
                }
            foreach (var c in Do("cargas_distribuidas"))
                if (barras.TryGetValue((string)c["barra"], out var b))
                    r.Cargas.Add(new Carga3D { Barra = b.Id, Distribuida = true, F = V(c["q"]), F2 = c["q2"] == null ? V(c["q"]) : V(c["q2"]) });
        }

        // Combinação: lista. Envoltória {max, min}: em cada ponto o de maior valor absoluto, com o sinal.
        // ponytail: um diagrama por barra; desenhar max e min juntos quando a Vista3D tiver duas curvas.
        static double[] Vetor(JsonNode n)
        {
            // ponytail: estação nula (null do motor) descarta o vetor da barra (sem diagrama), não inventa valor
            if (n is JsonArray a) return a.Any(x => x == null) ? null : a.Select(x => (double)x).ToArray();
            if (n is JsonObject o && o["max"] is JsonArray mx && o["min"] is JsonArray mn)
                return mx.Count != mn.Count || mx.Any(x => x == null) || mn.Any(x => x == null) ? null
                    : mx.Select((x, i) => { double p = (double)x, q = (double)mn[i]; return Math.Abs(p) >= Math.Abs(q) ? p : q; }).ToArray();
            return null;
        }
    }

    // ================= janela principal: modo "modelo espacial" =================
    public partial class MainWindow
    {
        DocumentoEspacial Esp => docAtivo >= 0 && docAtivo < docs.Count ? docs[docAtivo].Esp : null;
        RefMotor selEsp;
        bool modoEsp, escolhaEspLigada, espRodando;
        string ligarDe;                       // "Barra até outro nó": nó de partida, à espera do clique no outro nó
        static List<string> catalogoEsp;      // nomes do GET /api/catalogo (uma vez por sessão)
        readonly ContentControl painelEsp = new ContentControl();

        static bool avisouConversao;   // o aviso da conversão 2D -> 3D aparece uma vez por sessão

        internal async Task<DocumentoEspacial> AbrirEspacial(Func<Task<DocumentoEspacial>> criar, string oque, string recente = null, bool silencioso = false)
        {
            Bastidor.IsOpen = false;
            StatusMsg.Text = "Motor Python: " + oque + "…";
            var antes = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.AppStarting;
            DocumentoEspacial d;
            try { d = await criar(); }
            catch (Exception ex)
            {
                if (!silencioso) Caixa.Erro(this, "Não foi possível abrir o modelo", ex.Message);
                StatusMsg.Text = "⚠ " + ComandosAnalise.MensagemMotor(ex); Log(StatusMsg.Text, false); return null;
            }
            finally { Mouse.OverrideCursor = antes; }
            if (recente != null) Config.AdicionarRecente(recente);
            GuardarDoc();
            PrepararDocNovo();
            docs[docAtivo].Esp = d;
            selEsp = null;
            NovoModelo(new Modelo(), d.Arquivo);
            alterado = d.Arquivo == null;
            AtualizarTitulo();
            AplicarModoEspacial();
            Log("Modelo: " + d.Nome + " (" + d.Resumo() + ")", !silencioso);
            foreach (string a in d.Avisos) Log("  aviso: " + a, false);
            if (d.Origem != null)
            {
                Log("Convertido para 3D: " + Path.GetFileName(d.Origem) + " (Y do 2D virou Z; fora do plano travado). Salvar grava o formato novo (.estructto 3D).", false);
                if (!avisouConversao) { avisouConversao = true; MostrarToast("Modelo 2D convertido para 3D: o Y do desenho virou Z e o que sai do plano ficou travado. Ao salvar, o arquivo vai no formato novo (.estructto 3D); o original não é alterado.", true); }
            }
            string semApoios = d.Avisos.FirstOrDefault(a => a.StartsWith("Apoios e cargas"));   // .ftl (E2E-10): não deixa o F5 achar o mecanismo sozinho
            if (semApoios != null && !silencioso) MostrarToast(semApoios, true);
            return d;
        }

        internal Task AbrirEspacialArquivo(string caminho) => AbrirEspacial(() => DocumentoEspacial.AbrirArquivo(caminho), "abrindo " + Path.GetFileName(caminho), caminho);

        // Documento em branco: plano de desenho no térreo (z = 0) e vista de topo, como uma folha de CAD — até orbitar (Shift + arrastar).
        internal Task NovoEmBranco(bool silencioso = false) => NovoDocumento(null, null, silencioso);

        // ===== Pontos de entrada para a tela inicial, o "novo arquivo" e o Arquivo (worktree wt-inicio; docs/MODELO-ESPACIAL.md) =====

        // Documento novo numa aba nova. modelo = JSON do motor (gabarito: "nos", "barras", "secoes"... no formato de
        // GET /api/modelo) ou null = em branco. Abre em planta (vista de topo), plano de trabalho no térreo, ferramenta Selecionar.
        public async Task NovoDocumento(JsonObject modelo = null, string titulo = null, bool silencioso = false)
        {
            int n = docs.Count(x => x.Esp != null) + 1;
            titulo ??= "Modelo " + n;
            await AbrirEspacial(() => modelo == null ? DocumentoEspacial.Vazio(titulo) : DocumentoEspacial.DoModelo((JsonObject)modelo.DeepClone(), titulo, null),
                                "criando " + titulo, null, silencioso);
            if (Esp == null || vista3D == null) return;
            alterado = modelo != null; AtualizarTitulo();
            vista3D.PlanoAtual = PlanoTrabalho.Horizontal(0, "Térreo (z = 0,00 m)");
            vista3D.MostrarVista(VistaPadrao.Topo, false);
            vista3D.Ferramenta = Ferramenta3D.Selecionar;
        }

        // Abre qualquer arquivo como documento 3D (.estructto novo ou antigo, .portico, .ftl, .json, .ed3); já aberto = troca de aba.
        public Task AbrirArquivo(string caminho) => AbrirTarefa(caminho);

        // Últimos arquivos abertos/salvos (mais recente primeiro).
        public IReadOnlyList<string> ArquivosRecentes => Config.Recentes();

        // Chamado ao trocar de documento: liga ou desliga a vista 3D forçada e o painel de propriedades do motor.
        void AplicarModoEspacial()
        {
            var d = Esp;
            if (d == null)   // nenhum modelo aberto (tela inicial): a vista continua 3D, vazia; não existe mais vista 2D
            {
                modoEsp = false;
                LimparPlano();
                MostrarEdicao3D(false);
                Vista.ResumoExterno = null;
                MostrarVista3D(true);
                vista3D.Ferramenta = Ferramenta3D.Navegar;
                vista3D.Carregar(new Modelo3D(), true);
                painelEsp.Content = new TextBlock { Text = "Nenhum modelo aberto. Ctrl+N cria um modelo em branco (em planta, no térreo); Ctrl+O abre um arquivo (.estructto, .json, .ed3, .ftl).", Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
                PainelProp.Content = painelEsp;
                return;
            }
            modoEsp = true;
            MostrarEdicao3D(true);
            Vista.ResumoExterno = ResumoEspacial;
            TelaInicial.Visibility = Visibility.Collapsed;
            MostrarVista3D(true);
            vista3D.MostrarLegenda = true;
            if (!escolhaEspLigada)
            {
                escolhaEspLigada = true;
                vista3D.Escolheu += o =>
                {
                    if (Esp == null) return;
                    var r = (o as No3D)?.Origem as RefMotor ?? (o as Barra3D)?.Origem as RefMotor;
                    if (ligarDe != null && r?.Tipo == "no" && r.Id != ligarDe) { string de = ligarDe; ligarDe = null; _ = NovaBarraEsp(de, r.Id); return; }
                    ligarDe = null;
                    selEsp = r;
                    MontarPainelEspacial();
                    SincronizarEsp();
                };
            }
            PainelProp.Content = painelEsp;
            AtualizarEspacial3D(true);
            Aviso.Visibility = Visibility.Collapsed;
            _ = AtivarEspacial(d);
        }

        // Recoloca o modelo do documento no motor ao ativar a aba (o servidor guarda um modelo só).
        // Sem espRodando: a fila do Motor.Exclusivo ordena com as outras operações (antes um F5 logo após abrir era pulado).
        async Task AtivarEspacial(DocumentoEspacial d)
        {
            try { await d.Ativar(); }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; }
            AvisarHistorico(d);
        }

        void AvisarHistorico(DocumentoEspacial d)
        {
            if (!d.HistoricoPerdido) return;
            d.HistoricoPerdido = false;
            Log("Aviso: o motor guarda um modelo só e o desfazer vale só para o documento ativo. " + d.Nome + " voltou ao motor e o histórico de desfazer dele recomeçou.");
        }

        // Legenda do HUD no modelo espacial (o Vista 2D do documento é vazio).
        string ResumoEspacial()
        {
            var d = Esp;
            if (d == null || !d.Calculado) return "sem resultados";
            var bs = d.Json["barras"].AsObject().Select(kv => kv.Key).ToList();
            switch (Vista.Diag)
            {
                case Diagrama.Deformada:
                    double dm = d.Json["nos"].AsObject().Select(kv => d.Desl(kv.Key)).Where(v => v != null).Select(v => Math.Sqrt(v.Sum(x => x * x))).DefaultIfEmpty(0).Max();
                    return "máx. " + Num.Fmt(dm * 1000, 2) + " mm";
                case Diagrama.Normal: case Diagrama.Cortante: case Diagrama.Momento:
                    string c = Vista.Diag == Diagrama.Normal ? "N" : Vista.Diag == Diagrama.Cortante ? "Vz" : "My";
                    var v2 = bs.SelectMany(b => d.Esforco(b, c) ?? new double[0]).ToList();
                    if (v2.Count == 0) return "sem esforços";
                    return c + " máx. " + Num.Fmt(v2.Max(), 2) + "  ·  mín. " + Num.Fmt(v2.Min(), 2) + (c == "My" ? " kN·m" : " kN");
                case Diagrama.Verificacao:
                    var p = d.PiorPorBarra.Values.OrderByDescending(DocumentoEspacial.Taxa).FirstOrDefault();
                    return p == null ? "sem verificação" : "pior: " + (string)p["barra"] + " " + Num.Fmt(DocumentoEspacial.Taxa(p) * 100, 0) + "%";
            }
            return "";
        }

        // Seleção vinda da árvore, das tabelas ou dos avisos.
        void SelecionarEsp(RefMotor r)
        {
            if (Esp == null) return;
            selEsp = r; ligarDe = null;
            vista3D.Selecionado = r;
            vista3D.Selecionar(r?.Tipo == "no" ? new[] { r.Id } : null, r?.Tipo == "barra" ? new[] { r.Id } : null);   // Selecao = a da edição em lote; reconstrói
            MontarPainelEspacial();
            SincronizarEsp();
        }

        // Atualizar3D redireciona para cá quando o documento ativo é espacial.
        void AtualizarEspacial3D(bool enquadrar)
        {
            var d = Esp;
            if (d == null || vista3D == null) return;
            Diagrama dg = Vista.Diag;
            bool c = d.Calculado;
            vista3D.Diagrama = !c ? Diagrama3D.Nenhum : dg == Diagrama.Normal ? Diagrama3D.N : dg == Diagrama.Cortante ? Diagrama3D.V : dg == Diagrama.Momento ? Diagrama3D.M : Diagrama3D.Nenhum;
            vista3D.MostrarDeformada = c && dg == Diagrama.Deformada;
            vista3D.MostrarAproveitamento = c && dg == Diagrama.Verificacao;
            vista3D.EscalaDeformada = Vista.EscalaDef; vista3D.EscalaDiagrama = Vista.EscalaDiag;
            vista3D.Selecionado = selEsp;
            vista3D.Carregar(FiltrarPlano(d.ParaVista()), enquadrar);   // plano/nível isolado (PlanosTrabalho.cs)
            LigacoesEspacial3D(d);                                       // Realista: ligações dos grupos do motor (Render3DMotor.cs)
            Vista.Resolvido = c;   // o HUD (seletor de diagrama, escala) segue o cálculo do motor; o Modelo 2D do documento é vazio
            AtualizarLegenda();
            if (selEsp != null && vista3D.Selecionado == null) selEsp = null;
            StatusMsg.Text = d.Resumo();
            MontarPainelEspacial();
            AtualizarListasCasos(); AtualizarArvore(); AtualizarTabelas(); Diagnosticar();   // versões espaciais (Paineis.cs, Casos.cs)
            ListarSecoes3D();
            AplicarGradeNaVista();   // eixos da grade estrutural do documento (Comandos3D.cs)
            SincronizarEsp();
        }

        void DepoisDeMudarEspacial(string msg)
        {
            alterado = true;
            AtualizarTitulo();
            AtualizarEspacial3D(false);
            if (msg != null) Log(msg);
        }

        async Task Espacial(Func<DocumentoEspacial, Task> f, string msg)
        {
            var d = Esp;
            if (d == null) return;
            // o campo, o CheckBox e a ComboBox já mostram o valor novo: remonta o painel para voltar ao do modelo
            if (espRodando) { MontarPainelEspacial(); MostrarToast("Aguarde o cálculo terminar.", true); return; }
            espRodando = true;
            try { await f(d); if (Esp == d) DepoisDeMudarEspacial(msg); }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            finally { espRodando = false; }
            AvisarHistorico(d);
        }

        async Task TrocarEspacial(bool desfazer)
        {
            bool ok = false;
            await Espacial(async d => ok = await d.Trocar(desfazer), null);
            Log(ok ? (desfazer ? "Desfeito." : "Refeito.") : desfazer ? "Nada para desfazer." : "Nada para refazer.");
        }

        internal async Task CalcularEspacial()
        {
            var d = Esp;
            if (d == null) return;
            if (espRodando) { MostrarToast("Aguarde: o motor ainda está trabalhando. Tente F5 de novo em seguida.", true); return; }
            espRodando = true;
            var barra = new ProgressBar { IsIndeterminate = true, Width = 90, Height = 6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            StatusBotoes.Children.Insert(0, barra);
            StatusMsg.Text = "Motor Python: calculando (combinações NBR 8681 e verificação por norma)…";
            try
            {
                await d.Calcular();
                if (Esp != d) return;
                if (Vista.Diag == Diagrama.Nenhum) MostrarDiagrama(Diagrama.Verificacao);   // passa por Atualizar3D
                else AtualizarEspacial3D(false);
                Log(d.Resumo());
            }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            finally { StatusBotoes.Children.Remove(barra); espRodando = false; }
        }

        // .estructto = contêiner do formato novo (modelo do motor + placas + grade); .json = só o modelo do motor (outros programas).
        // Documento convertido de um arquivo antigo: "Salvar" pede o nome (sugere "<nome> (3D).estructto") e não sobrescreve o antigo.
        bool SalvarEspacial(bool como)
        {
            var d = Esp;
            string c = d.Arquivo;
            if (como || c == null)
            {
                string pasta = d.Origem != null ? Path.GetDirectoryName(d.Origem) : null;
                string nome = d.Origem != null ? Path.GetFileNameWithoutExtension(d.Origem) + " (3D)" : c == null ? d.Nome : Path.GetFileNameWithoutExtension(c);
                var dlg = new SaveFileDialog { Filter = "Estructto (*.estructto)|*.estructto|Modelo do motor, sem placas nem grade (*.json)|*.json", FileName = nome + ".estructto", InitialDirectory = pasta ?? "" };
                if (dlg.ShowDialog(this) != true) return false;
                c = dlg.FileName;
            }
            return SalvarEspacialEm(c);
        }

        internal bool SalvarEspacialEm(string c)
        {
            var d = Esp;
            try
            {
                bool json = c.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                Config.GravarAtomico(c, json ? d.JsonFormatado() : d.ParaContainer());   // Json = último GET /api/modelo; temporário + troca
                if (json && d.Placas.Count > 0) Log("Aviso: o .json do motor não leva as placas de base do arquivo antigo; use .estructto para guardar tudo.");
                d.Arquivo = c; d.Origem = null; arquivo = c; alterado = false;
                Config.AdicionarRecente(c);
                Config.ApagarRecuperacao(d.Chave);   // só a cópia deste documento: as das outras abas não salvas ficam
                AtualizarTitulo();
                Log("Salvo: " + c);
                return true;
            }
            catch (Exception ex) { Caixa.Erro(this, "Não foi possível salvar", ex.Message); return false; }
        }

        // ---------- painel de propriedades (nó, barra ou o modelo) ----------

        void MontarPainelEspacial()
        {
            var d = Esp;
            if (d == null) return;
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            Action<string, string> cab = (t, s) =>
            {
                sp.Children.Add(new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = TemaPlant.FonteTitulo, Foreground = TemaPlant.BTexto1 });
                sp.Children.Add(new TextBlock { Text = s, FontSize = 11.5, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
            };
            Action<string> secao = t => sp.Children.Add(new TextBlock { Text = t.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 10, 0, 4) });
            Action<string, UIElement> linha = (rot, ed) =>
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(new TextBlock { Text = rot, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(ed, 1); g.Children.Add(ed);
                sp.Children.Add(g);
            };
            Func<string, TextBlock> leitura = s => new TextBlock { Text = s, Foreground = TemaPlant.BTexto1, TextWrapping = TextWrapping.Wrap };
            // campo numérico: Enter ou sair do campo manda o PATCH
            Func<double, Action<double>, TextBox> num = (v, aplicar) =>
            {
                var tb = JanelaEscura.Campo(Num.Fmt(v, 3), 120);
                tb.HorizontalAlignment = HorizontalAlignment.Left;
                string orig = tb.Text;
                Action ok = () =>
                {
                    if (tb.Text == orig) return;
                    if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) { orig = tb.Text; aplicar(x); }
                    else tb.Text = orig;
                };
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { ok(); e.Handled = true; } };
                tb.LostKeyboardFocus += (s, e) => ok();
                return tb;
            };
            Func<IEnumerable<string>, string, Action<string>, ComboBox> combo = (itens, atual, aplicar) =>
            {
                var cb = new ComboBox { IsEditable = false };
                foreach (string i in itens) cb.Items.Add(i);
                cb.SelectedItem = atual;
                cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string x && x != atual) aplicar(x); };
                return cb;
            };
            // linha de campos curtos (x y z, Fx Fy Fz…) + botão; o botão lê os números e chama a ação
            Action<string[], double[], string, Action<double[]>> vetor = (rots, ini, txt, acao) =>
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
                var tbs = new List<TextBox>();
                for (int i = 0; i < rots.Length; i++)
                {
                    wp.Children.Add(new TextBlock { Text = rots[i], Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                    var tb = JanelaEscura.Campo(Num.Fmt(ini[i], 2), 52); tb.Margin = new Thickness(0, 0, 8, 2);
                    tbs.Add(tb); wp.Children.Add(tb);
                }
                var b = JanelaEscura.B(txt, () =>
                {
                    var v = new double[tbs.Count];
                    for (int i = 0; i < v.Length; i++)
                        if (!double.TryParse(tbs[i].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) { StatusMsg.Text = "⚠ Número inválido: " + tbs[i].Text; return; }
                    acao(v);
                });
                b.Margin = new Thickness(0, 0, 0, 2);
                wp.Children.Add(b);
                sp.Children.Add(wp);
            };
            void botoes(params (string, Action)[] bs)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
                foreach (var (t, a) in bs) { var b = JanelaEscura.B(t, a); b.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(b); }
                sp.Children.Add(wp);
            }
            // caso das cargas: o mesmo de Ações › Caso ativo
            Action casoCargas = () =>
            {
                var casos = (d.Json["casos"] as JsonObject)?.Select(x => x.Key).ToList() ?? new List<string>();
                if (casos.Count == 0) { sp.Children.Add(leitura("O modelo não tem casos de carga.")); return; }
                linha("Caso", combo(casos, d.CasoAtivo, v => { d.CasoAtivo = v; AtualizarEspacial3D(false); }));   // refaz listas, painel e setas
            };
            // cargas existentes deste nó/barra (todos os casos), com botão de apagar
            Action<string, string, string, string> cargas = (col, tipo, alvo, id) =>
            {
                foreach (var c in (d.Json[col] as JsonArray ?? new JsonArray()).Where(c => (string)c[alvo] == id))
                {
                    string cid = (string)c["id"], campo = tipo == "carga_nodal" ? "F" : "q";
                    var v = c[campo].AsArray().Take(3).Select(x => Num.Fmt((double)x, 2));
                    var dp = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var x = JanelaEscura.B("×", () => _ = Espacial(dd => dd.Remover(tipo, cid), "Carga " + cid + " apagada."), false, "Apaga esta carga");
                    x.Padding = new Thickness(6, 0, 6, 0); x.MinWidth = 0;
                    DockPanel.SetDock(x, Dock.Right); dp.Children.Add(x);
                    dp.Children.Add(leitura((string)c["caso"] + ": (" + string.Join("; ", v) + ")" + (tipo == "carga_nodal" ? " kN" : " kN/m")));
                    sp.Children.Add(dp);
                }
            };

            JsonNode el = selEsp == null ? null : d.Json[selEsp.Tipo == "no" ? "nos" : "barras"]?[selEsp.Id];
            if (el == null)
            {
                selEsp = null;
                var j = d.Json;
                cab((string)j["nome"] is string n && n != "" && n != "Estructto" ? n : d.Nome, "Modelo 3D (motor Python). Clique numa barra ou nó da vista para editar.");
                secao("Modelo");
                foreach (var (r, k) in new[] { ("Nós", "nos"), ("Barras", "barras"), ("Seções", "secoes"), ("Materiais", "materiais"), ("Casos", "casos"), ("Pavimentos", "pavimentos") })
                    linha(r, leitura((j[k] as JsonObject)?.Count.ToString() ?? "0"));
                linha("Casos", leitura(string.Join(", ", (j["casos"] as JsonObject)?.Select(x => x.Key) ?? new string[0])));
                secao("Novo nó (m)");
                vetor(new[] { "X", "Y", "Z" }, new double[3], "Adicionar", v => _ = Espacial(async dd =>
                {
                    string nid = await dd.Adicionar("no", new JsonObject { ["x"] = v[0], ["y"] = v[1], ["z"] = v[2] });
                    if (nid != null) selEsp = dd.Ref("no", nid);
                }, "Nó novo em (" + string.Join("; ", v.Select(x => Num.Fmt(x, 2))) + ") m."));
                sp.Children.Add(leitura("Barras: escolha um nó e use \"Barra até outro nó\" ou \"Nó + barra\"."));
                PainelCargaArea(sp, d, casoCargas);   // CargasEspacial.cs: casos e carga em área
                secao("Cálculo");
                sp.Children.Add(leitura(d.Calculado ? d.Resumo() : "Sem cálculo: F5 calcula pelo motor (NBR 8681, 8800, 14762, 6118)."));
                sp.Children.Add(new TextBlock { Text = "O motor guarda um modelo só: o desfazer (Ctrl+Z) vale para o documento ativo e recomeça ao voltar de outra aba.", FontSize = 11, Foreground = TemaPlant.BTexto2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            }
            else if (selEsp.Tipo == "no")
            {
                string id = selEsp.Id;
                cab("Nó " + id, "(" + Num.Fmt((double)el["x"], 2) + "; " + Num.Fmt((double)el["y"], 2) + "; " + Num.Fmt((double)el["z"], 2) + ") m");
                secao("Posição (m)");
                foreach (string k in new[] { "x", "y", "z" })
                {
                    string kk = k;
                    linha(k.ToUpperInvariant(), num((double)el[k], v => _ = Espacial(dd => dd.Editar("no", id, new JsonObject { [kk] = v }), "Nó " + id + ": " + kk + " = " + Num.Fmt(v, 3) + " m.")));
                }
                secao("Apoio (travado)");
                var ap = el["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                var wp = new WrapPanel();
                string[] gl = { "ux", "uy", "uz", "rx", "ry", "rz" };
                for (int i = 0; i < 6; i++)
                {
                    int k = i;
                    var ck = new CheckBox { Content = gl[i], IsChecked = ap[i], Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 2, 12, 2) };
                    ck.Click += (s, e) =>
                    {
                        var novo = (bool[])ap.Clone(); novo[k] = ck.IsChecked == true;
                        _ = Espacial(dd => dd.Editar("no", id, new JsonObject { ["apoio"] = new JsonArray(novo.Select(x => (JsonNode)x).ToArray()) }), "Nó " + id + ": apoio " + gl[k] + (novo[k] ? " travado." : " liberado."));
                    };
                    wp.Children.Add(ck);
                }
                sp.Children.Add(wp);
                var dl = d.Desl(id);
                if (dl != null)
                {
                    secao("Deslocamento (" + d.NomeDiagrama + ")");
                    linha("ux, uy, uz", leitura(string.Join(" · ", dl.Select(x => Num.Fmt(x * 1000, 2))) + " mm"));
                }
                secao("Barras");
                botoes(("Barra até outro nó", () => { ligarDe = id; StatusMsg.Text = "Clique no outro nó da barra (clique fora cancela)."; }),
                       ("Apagar nó", () => _ = Espacial(dd => dd.Remover("no", id), "Nó " + id + " apagado (com as barras e cargas dele).")));
                sp.Children.Add(leitura("Nó + barra: cria um nó deslocado deste e a barra entre os dois."));
                vetor(new[] { "dX", "dY", "dZ" }, new double[] { 0, 0, 3 }, "Nó + barra", v => _ = Espacial(async dd =>
                {
                    string nid = await dd.Adicionar("no", new JsonObject { ["x"] = (double)el["x"] + v[0], ["y"] = (double)el["y"] + v[1], ["z"] = (double)el["z"] + v[2] });
                    await dd.Adicionar("barra", DadosBarraNova(dd, id, nid));
                    selEsp = dd.Ref("no", nid);
                }, "Nó e barra novos a partir do nó " + id + "."));
                secao("Cargas no nó");
                cargas("cargas_nodais", "carga_nodal", "no", id);
                casoCargas();
                if (d.CasoAtivo != null)
                    vetor(new[] { "Fx", "Fy", "Fz" }, new double[] { 0, 0, -10 }, "Adicionar (kN)", v => _ = Espacial(dd => dd.Adicionar("carga_nodal", new JsonObject
                    { ["caso"] = dd.CasoAtivo, ["no"] = id, ["F"] = new JsonArray(v[0], v[1], v[2], 0.0, 0.0, 0.0) }), "Carga nodal no nó " + id + " (" + d.CasoAtivo + ")."));
            }
            else
            {
                string id = selEsp.Id;
                cab("Barra " + id + " · " + (string)el["secao"], (string)el["material"] + " · " + (string)el["tipo"] + " · " + (string)el["ni"] + " → " + (string)el["nj"]);
                secao("Barra");
                // seções do modelo + perfis do catálogo do motor; perfil novo entra no modelo antes do PATCH
                if (catalogoEsp == null) _ = CarregarCatalogoEsp();
                var noModelo = d.Json["secoes"].AsObject().Select(x => x.Key).ToList();
                linha("Seção", combo(noModelo.Concat((catalogoEsp ?? new List<string>()).Where(x => !noModelo.Contains(x))), (string)el["secao"], v => _ = Espacial(async dd =>
                {
                    await dd.GarantirSecao(v);
                    await dd.Editar("barra", id, new JsonObject { ["secao"] = v });
                }, "Barra " + id + ": seção " + v + (noModelo.Contains(v) ? "." : " (do catálogo, adicionada ao modelo)."))));
                linha("Material", combo(d.Json["materiais"].AsObject().Select(x => x.Key), (string)el["material"], v => _ = Espacial(dd => dd.Editar("barra", id, new JsonObject { ["material"] = v }), "Barra " + id + ": material " + v + ".")));
                linha("Giro (°)", num((double)(el["rot"] ?? 0.0) * 180 / Math.PI, v => _ = Espacial(dd => dd.Editar("barra", id, new JsonObject { ["rot"] = v * Math.PI / 180 }), "Barra " + id + ": giro " + Num.Fmt(v, 1) + "°.")));
                linha("Papel", leitura((string)el["params"]?["papel"] ?? "—"));
                if (d.PiorPorBarra.TryGetValue(id, out var v0))
                {
                    secao("Verificação (" + (string)v0["norma"] + ")");
                    double t = DocumentoEspacial.Taxa(v0);
                    linha("Taxa", new TextBlock { Text = t < 0 ? "—" : Num.Fmt(t * 100, 0) + "%", FontWeight = FontWeights.SemiBold, Foreground = t > 1 ? TemaPlant.BErro : t > 0.9 ? TemaPlant.BAlerta : TemaPlant.BSucesso });
                    linha("Critério", leitura((string)v0["criterio"] + " · " + (string)v0["item"]));
                    linha("Combinação", leitura((string)v0["combinacao"] ?? "—"));
                }
                else if (d.Calculado) { secao("Verificação"); sp.Children.Add(leitura("Barra não verificada pelo motor.")); }
                botoes(("Disposição do perfil…", () => AbrirDisposicao(new List<string> { id })),
                       ("Apagar barra", () => _ = Espacial(dd => dd.Remover("barra", id), "Barra " + id + " apagada (com as cargas dela).")));
                PainelCargasBarra(sp, d, id, casoCargas);   // CargasEspacial.cs: distribuídas (trapezoidais) e pontuais
            }
            painelEsp.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TemaPlant.BSup1 };
            if (selEsp != selAnimada) { selAnimada = selEsp; Anim.Entrar(sp, 6, Anim.Rapido); }   // só quando a escolha muda
        }
        RefMotor selAnimada;

        // Barra nova entre dois nós: seção, material e giro da última barra do modelo (ou a primeira seção/material).
        static JsonObject DadosBarraNova(DocumentoEspacial d, string ni, string nj)
        {
            var ult = (d.Json["barras"] as JsonObject)?.LastOrDefault().Value;
            return new JsonObject
            {
                ["ni"] = ni, ["nj"] = nj,
                ["secao"] = (string)ult?["secao"] ?? d.Json["secoes"].AsObject().Select(x => x.Key).FirstOrDefault(),
                ["material"] = (string)ult?["material"] ?? d.Json["materiais"].AsObject().Select(x => x.Key).FirstOrDefault(),
            };
        }

        Task NovaBarraEsp(string ni, string nj) => Espacial(async d =>
        {
            string id = await d.Adicionar("barra", DadosBarraNova(d, ni, nj));
            if (id != null) selEsp = d.Ref("barra", id);
        }, "Barra nova: " + ni + " → " + nj + ".");

        async Task CarregarCatalogoEsp()
        {
            catalogoEsp = new List<string>();   // evita buscar duas vezes
            try { catalogoEsp = (await Motor.Get("/api/catalogo")).AsArray().Select(x => (string)x["nome"]).ToList(); }
            catch (Exception ex) { Log("Catálogo do motor indisponível: " + ex.Message); return; }
            MontarPainelEspacial();
        }

        // ---------- árvore, tabelas, avisos e listas de resultado (chamados pelas versões 2D quando o documento é espacial) ----------

        void ArvoreEspacial()
        {
            var d = Esp; var j = d.Json;
            sincronizando = true;
            Arvore.Items.Clear();
            var raiz = Item("painel", "Modelo espacial", d.Nome, null, null, true, "Modelo do motor Python. Clique num nó ou numa barra para escolher na vista 3D.");
            var nosJ = j["nos"].AsObject(); var barJ = j["barras"].AsObject();
            var nos = Item("no", "Nós", null, nosJ.Count, null, nosJ.Count < 40, "Nós do modelo espacial.");
            var comCarga = new HashSet<string>((j["cargas_nodais"] as JsonArray ?? new JsonArray()).Select(c => (string)c["no"]));
            foreach (var kv in nosJ)
            {
                var n = kv.Value;
                bool ap = n["apoio"]?.AsArray().Any(x => x != null && (bool)x) == true;
                nos.Items.Add(Item("no", "Nó " + kv.Key, "(" + Num.Fmt((double)n["x"], 2) + "; " + Num.Fmt((double)n["y"], 2) + "; " + Num.Fmt((double)n["z"], 2) + ")", null, d.Ref("no", kv.Key), false,
                    "Nó " + kv.Key + (ap ? " · com apoio" : "") + (comCarga.Contains(kv.Key) ? " · com carga" : "") + ". Clique para escolher e editar em Propriedades.", ap ? "engaste" : null, comCarga.Contains(kv.Key) ? "cargano" : null));
            }
            var barras = Item("barra", "Barras", null, barJ.Count, null, barJ.Count < 40, "Barras do modelo espacial.");
            foreach (var kv in barJ)
            {
                var b = kv.Value; d.PiorPorBarra.TryGetValue(kv.Key, out var v);
                string tx = v != null && DocumentoEspacial.Taxa(v) >= 0 ? Num.Fmt(DocumentoEspacial.Taxa(v) * 100, 0) + "%" : null;
                barras.Items.Add(Item("barra", "Barra " + kv.Key, (string)b["secao"] + (tx != null ? " · " + tx : ""), null, d.Ref("barra", kv.Key), false,
                    "Barra " + kv.Key + " (" + (string)b["secao"] + ", " + (string)b["material"] + ", " + (string)b["ni"] + " → " + (string)b["nj"] + "). Clique para escolher e editar."));
            }
            Func<string, string, TreeViewItem> grupo = (chave, rot) =>
            {
                var o = j[chave] as JsonObject ?? new JsonObject();
                var g = Item(chave == "secoes" ? "eixo" : chave == "materiais" ? "pincel" : "tabela", rot, null, o.Count, null, false, rot + " do modelo espacial.");
                foreach (var kv in o) g.Items.Add(Item(chave == "secoes" ? "eixo" : chave == "materiais" ? "pincel" : "tabela", kv.Key, (string)kv.Value?["tipo"], null, null, false, kv.Key));
                return g;
            };
            raiz.Items.Add(nos); raiz.Items.Add(barras);
            raiz.Items.Add(grupo("secoes", "Seções")); raiz.Items.Add(grupo("materiais", "Materiais")); raiz.Items.Add(grupo("casos", "Casos de carga"));
            int ncargas = new[] { "cargas_nodais", "cargas_distribuidas", "cargas_pontuais" }.Sum(k => (j[k] as JsonArray)?.Count ?? 0);
            raiz.Items.Add(Item("cargano", "Cargas", null, ncargas, null, false, "Cargas nodais, distribuídas e pontuais de todos os casos."));
            Arvore.Items.Add(raiz);
            sincronizando = false;
        }

        void TabelasEspaciais()
        {
            var d = Esp; var j = d.Json;
            sincronizando = true;
            bool ok = d.Calculado;
            Func<double, string> mm = x => Num.Fmt(x * 1000, 2);
            TabNos.ItemsSource = j["nos"].AsObject().Select(kv =>
            {
                var n = kv.Value; var dl = ok ? d.Desl(kv.Key) : null;
                var ap = n["apoio"]?.AsArray().Select(x => x != null && (bool)x).ToArray() ?? new bool[6];
                string[] gl = { "ux", "uy", "uz", "rx", "ry", "rz" };
                return new LinhaNoEsp
                {
                    Obj = d.Ref("no", kv.Key), Id = kv.Key, X = Num.Fmt((double)n["x"], 3), Y = Num.Fmt((double)n["y"], 3), Z = Num.Fmt((double)n["z"], 3),
                    Apoio = ap.All(x => x) ? "engaste" : ap.Any(x => x) ? string.Join(" ", gl.Where((g, i) => ap[i])) : "",
                    DX = dl != null ? mm(dl[0]) : "", DY = dl != null ? mm(dl[1]) : "", DZ = dl != null ? mm(dl[2]) : "",
                };
            }).ToList();
            Func<double[], string> faixa = v => v == null || v.Length == 0 ? "" : Num.Fmt(v.Min(), 2) + " / " + Num.Fmt(v.Max(), 2);
            TabBarras.ItemsSource = j["barras"].AsObject().Select(kv =>
            {
                var b = kv.Value;
                double L = 0;
                var ni = j["nos"][(string)b["ni"]]; var nj = j["nos"][(string)b["nj"]];
                if (ni != null && nj != null) L = Math.Sqrt(new[] { "x", "y", "z" }.Sum(k => Math.Pow((double)nj[k] - (double)ni[k], 2)));
                return new LinhaBarraEsp
                {
                    Obj = d.Ref("barra", kv.Key), Id = kv.Key, Nos = (string)b["ni"] + " → " + (string)b["nj"], Perfil = (string)b["secao"], Material = (string)b["material"], L = Num.Fmt(L, 3),
                    N = ok ? faixa(d.Esforco(kv.Key, "N")) : "", Vz = ok ? faixa(d.Esforco(kv.Key, "Vz")) : "", My = ok ? faixa(d.Esforco(kv.Key, "My")) : "",
                };
            }).ToList();
            var lv = j["barras"].AsObject().Select(kv =>
            {
                var l = new LinhaVerifEsp { Obj = d.Ref("barra", kv.Key), Id = kv.Key, Perfil = (string)kv.Value["secao"] };
                if (!ok || !d.PiorPorBarra.TryGetValue(kv.Key, out var v)) { l.Situacao = l.SitTxt = ok ? "não verificada" : "sem resultados"; return l; }
                double t = DocumentoEspacial.Taxa(v);
                l.Critico = (string)v["criterio"] + " · " + (string)v["norma"] + " " + (string)v["item"]; l.Combinacao = (string)v["combinacao"];
                if (t < 0) { l.Situacao = l.SitTxt = "não verificada"; return l; }
                bool passa = v["ok"] == null || (bool)v["ok"];
                l.Situacao = passa ? "passa" : "NÃO PASSA"; l.SitTxt = passa ? "Passa" : "Não passa"; l.Pass = passa;
                l.Ap = Num.Fmt(t * 100, 0) + "%"; l.ApNum = t * 100; l.CorAp = UiPlaca.CorAp(t, passa); l.BarraVis = Visibility.Visible;
                var cs = UiPlaca.Cores(passa ? (t > 0.9 ? UiPlaca.Nivel.Alerta : UiPlaca.Nivel.Ok) : UiPlaca.Nivel.Erro); l.SitFg = cs.fg; l.SitBg = cs.bg; l.SitBd = cs.bd;
                return l;
            }).OrderBy(x => x.Pass ? 1 : 0).ThenByDescending(x => x.ApNum).ToList();
            int tot = lv.Count;
            if (soCriticos) lv = lv.Where(x => !x.Pass || x.ApNum > 90).ToList();
            TabVerif.ItemsSource = lv;
            ContVerif.Text = !ok ? "" : soCriticos ? lv.Count + " de " + tot + " barras" : Pl.N(tot, "barra", "barras");
            AbasTabela.ToolTip = ok ? "Modelo espacial (motor). Esforços e deslocamentos: " + d.NomeDiagrama + ". Clique numa linha para escolher na vista." : "Modelo espacial: F5 calcula pelo motor para preencher esforços, deslocamentos e verificação.";
            VazioNos.Visibility = j["nos"].AsObject().Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VazioBarras.Visibility = VazioVerif.Visibility = tot == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif }) { dg.AutoGeneratingColumn -= Coluna; dg.AutoGeneratingColumn += Coluna; }
            sincronizando = false;
        }

        // validação do motor + avisos da importação + barras reprovadas
        List<(string, string, List<object>)> AvisosEspaciais()
        {
            var d = Esp;
            var lista = new List<(string, string, List<object>)>();
            foreach (var a in d.Validacao)
            {
                var r = a?["ref"]; string tipo = (string)r?["tipo"], id = (string)r?["id"];
                var objs = tipo == "no" || tipo == "barra" ? new List<object> { d.Ref(tipo, id) } : new List<object>();
                lista.Add(((string)a["nivel"] == "erro" ? "erro" : "aviso", (string)a["mensagem"], objs));
            }
            foreach (string a in d.Avisos) lista.Add(("info", "Importação: " + a, new List<object>()));
            if (d.Calculado)
            {
                var falham = d.PiorPorBarra.Where(kv => kv.Value["ok"] != null && !(bool)kv.Value["ok"]).Select(kv => (object)d.Ref("barra", kv.Key)).ToList();
                if (falham.Count > 0) lista.Add(("aviso", Pl.N(falham.Count, "barra não passa", "barras não passam") + " na verificação do motor. Clique para escolher a primeira; troque a seção em Propriedades.", falham));
            }
            return lista;
        }

        void SincronizarEsp()
        {
            if (sincronizando || Esp == null) return;
            sincronizando = true;
            foreach (TreeViewItem raiz in Arvore.Items)
                foreach (TreeViewItem g in raiz.Items)
                    foreach (TreeViewItem it in g.Items)
                        if (selEsp != null && it.Tag == selEsp) { g.IsExpanded = true; it.IsSelected = true; it.BringIntoView(); }
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif })
            {
                dg.SelectedItems.Clear();
                foreach (var it in (dg.ItemsSource as System.Collections.IEnumerable) ?? new object[0])
                    if (selEsp != null && it is LinhaEsp le && le.Obj == selEsp) { dg.SelectedItems.Add(it); dg.ScrollIntoView(it); }
            }
            sincronizando = false;
        }

        // Ações › Caso ativo = caso das cargas do painel; Resultados › Mostrar = envoltória ou uma combinação do motor.
        void ListasEspaciais()
        {
            var d = Esp;
            listasOcupadas = true;
            cbCaso.Items.Clear();
            var casos = (d.Json["casos"] as JsonObject)?.ToList() ?? new List<KeyValuePair<string, JsonNode>>();
            foreach (var kv in casos) cbCaso.Items.Add(new ComboBoxItem { Content = kv.Key + "  [" + (string)kv.Value?["tipo"] + "]", Tag = kv.Key, ToolTip = "As cargas lançadas no painel Propriedades vão para o caso \"" + kv.Key + "\"." });
            cbCaso.SelectedItem = cbCaso.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == d.CasoAtivo);
            cbResultado.Items.Clear();
            cbResultado.Items.Add(new ComboBoxItem { Content = "Envoltória ELU (motor)", Tag = "esp:", ToolTip = "Diagramas: em cada ponto o maior valor absoluto entre as combinações ELU; deformada: envoltória ELS." });
            foreach (string c in d.Combinacoes) cbResultado.Items.Add(new ComboBoxItem { Content = c, Tag = "esp:" + c, ToolTip = "Diagramas, deformada e tabelas da combinação \"" + c + "\" do motor." });
            cbResultado.SelectedItem = cbResultado.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == "esp:" + d.CombDiagrama) ?? cbResultado.Items[0];
            listasOcupadas = false;
        }

        // Escolha em Resultados › Mostrar (Casos.cs) quando o documento é espacial.
        async Task MudarResultadoEsp(string comb)
        {
            var d = Esp;
            if (d == null || espRodando) return;
            espRodando = true;
            try { await d.MudarDiagrama(comb == "" ? null : comb); }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); return; }
            finally { espRodando = false; }
            if (Esp != d) return;
            AtualizarEspacial3D(false);
            Log("Mostrando: " + d.NomeDiagrama + ".");
        }

        // Para as capturas e testes: o documento espacial ativo e um jeito de fechar todos sem perguntar.
        internal DocumentoEspacial EspacialAtivo => Esp;
        internal bool OcupadoEsp => espRodando;   // capturas: operação no motor em andamento
        internal Task EditarEspacial(string tipo, string id, JsonObject campos) => Espacial(d => d.Editar(tipo, id, campos), "Editado: " + tipo + " " + id + ".");
        internal Task DesfazerEspacial() => TrocarEspacial(true);
        internal Task OperarEspacial(Func<DocumentoEspacial, Task> f, string msg) => Espacial(f, msg);
        internal Task MostrarCombinacaoEspacial(string comb) => MudarResultadoEsp(comb ?? "");
        internal string TextoResultado => NomeResultado();
        internal void EscolherEspacial(string tipo, string id) => SelecionarEsp(tipo == null ? null : Esp.Ref(tipo, id));
        internal void FecharEspaciaisSemPerguntar()
        {
            GuardarDoc();
            foreach (var x in docs) x.Esp?.Descartar();
            docs.RemoveAll(x => x.Esp != null);
            if (docs.Count == 0) docs.Add(new Documento());
            CarregarDoc(0);
        }

        // ---------- gerar, exemplos, tela inicial ----------

        internal void AbrirGerador3D(string tipo = null)
        {
            Bastidor.IsOpen = false;
            var j = new JanelaGerar3D(tipo) { Owner = this };
            if (j.ShowDialog() == true) _ = AbrirEspacial(() => DocumentoEspacial.Gerar(j.Dados, j.Titulo, j.G, j.Q), "gerando " + j.Titulo);
        }

        internal async void AbrirExemplosEspaciais()
        {
          try
          {
            Bastidor.IsOpen = false;
            JsonNode lista;
            try { lista = await Motor.Get("/api/exemplos"); }
            catch (Exception ex) { Caixa.Erro(this, "Motor de cálculo indisponível", ex.Message); return; }
            var jan = new JanelaEscura { Title = "Exemplos do motor", Width = 460, SizeToContent = SizeToContent.Height, Owner = this, ResizeMode = ResizeMode.NoResize };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(JanelaEscura.T("Modelos espaciais prontos do motor Python (abrem numa aba nova, em 3D).", 12, JanelaEscura.BFraco));
            foreach (var e in lista?.AsArray() ?? new JsonArray())
            {
                string nome = (string)e["nome"];
                var b = Cartao((string)e["descricao"], nome, () => { jan.Close(); _ = AbrirEspacial(() => DocumentoEspacial.Exemplo(nome), "abrindo o exemplo " + nome); }, "3d");
                b.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(b);
            }
            jan.Content = sp;
            jan.ShowDialog();
          }
          catch (Exception ex) { MostrarToast("⚠ Exemplos: " + ex.Message, true); }
        }
    }

    // Parâmetros do gerador do motor (ESPEC §6.3: edificio, galpao, portico_plano) + cargas nas vigas.
    public class JanelaGerar3D : JanelaEscura
    {
        public JsonObject Dados; public string Titulo; public double G, Q;
        readonly ComboBox cbTipo = new ComboBox { Width = 220 };
        readonly Dictionary<string, TextBox> c = new Dictionary<string, TextBox>();
        readonly Dictionary<string, FrameworkElement> linhas = new Dictionary<string, FrameworkElement>();
        readonly TextBlock erro = T("", 12, BVermelho);
        static readonly string[] Tipos = { "edificio", "galpao", "portico_plano" };
        static readonly string[] NomesTipo = { "Edifício (pórticos espaciais)", "Galpão", "Pórtico plano" };

        public JanelaGerar3D(string tipo)
        {
            Title = "Gerar estrutura 3D (motor)"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(T("Gera o modelo espacial no motor Python. Listas: valores separados por espaço (ex.: 6 6 5). Seções do catálogo (W310x38.7) ou \"Ret 25x50\" (cm) para concreto; material A-572 GR.50 ou C30.", 11.5, BFraco));
            foreach (string t in NomesTipo) cbTipo.Items.Add(t);
            Linha(sp, "tipo", "Tipo", cbTipo);
            foreach (var (k, r, v) in new[] {
                ("vaos_x", "Vãos em X (m)", "6 6"), ("vaos_y", "Vãos em Y (m)", "5 5"), ("alturas", "Alturas (m)", "3,5 3,5 3,5"),
                ("vao", "Vão (m)", "20"), ("altura_pilar", "Altura do pilar (m)", "6"), ("altura_cumeeira", "Cumeeira (m)", "8"),
                ("n_modulos", "Módulos", "5"), ("espacamento", "Espaçamento (m)", "6"), ("terca", "Terça", "Ue 250x85x25x3,00"),
                ("material", "Material", "A-572 GR.50"), ("pilar", "Pilar", "W310x38.7"), ("viga", "Viga", "W360x51"), ("apoio", "Apoio (engaste/rotula)", "engaste"),
                ("g", "G nas vigas (kN/m)", "12"), ("q", "Q nas vigas (kN/m)", "8") })
            {
                c[k] = Campo(v, 220);
                Linha(sp, k, r, c[k]);
            }
            sp.Children.Add(erro);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            bts.Children.Add(B("Cancelar", Close));
            bts.Children.Add(B("Gerar", Confirmar, true));
            sp.Children.Add(bts);
            Content = sp;
            cbTipo.SelectionChanged += (s, e) => Mostrar();
            cbTipo.SelectedIndex = Math.Max(0, Array.IndexOf(Tipos, tipo ?? "edificio"));
        }

        void Linha(StackPanel sp, string k, string rot, FrameworkElement ed)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(T(rot, 12, BFraco));
            ed.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(ed, 1); g.Children.Add(ed);
            linhas[k] = g; sp.Children.Add(g);
        }

        string TipoAtual => Tipos[Math.Max(0, cbTipo.SelectedIndex)];

        void Mostrar()
        {
            string t = TipoAtual;
            var so = new Dictionary<string, string[]> { ["edificio"] = new[] { "vaos_x", "vaos_y", "alturas" }, ["portico_plano"] = new[] { "vaos_x", "alturas" },
                                                        ["galpao"] = new[] { "vao", "altura_pilar", "altura_cumeeira", "n_modulos", "espacamento", "terca" } };
            var todos = so.Values.SelectMany(x => x).Distinct();
            foreach (string k in todos) linhas[k].Visibility = so[t].Contains(k) ? Visibility.Visible : Visibility.Collapsed;
            if (t == "galpao" && c["pilar"].Text == "W310x38.7") { c["pilar"].Text = "W360x51"; c["viga"].Text = "HP250x62"; c["g"].Text = "1,5"; c["q"].Text = "1,5"; }
        }

        static double D(string s) => double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        static JsonArray Lista(string s) => new JsonArray(s.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)D(x)).ToArray());

        // "Ret 25x50" (cm) -> seção retangular; o resto é nome do catálogo do motor
        static JsonNode Secao(string s)
        {
            s = s.Trim();
            if (s.StartsWith("Ret", StringComparison.OrdinalIgnoreCase))
            {
                var p = s.Substring(3).Split(new[] { 'x', 'X', '×' }, StringSplitOptions.RemoveEmptyEntries);
                return new JsonObject { ["forma"] = "retangular", ["b"] = D(p[0]) / 100, ["h"] = D(p[1]) / 100 };
            }
            return s;
        }

        void Confirmar() { if (Montar()) DialogResult = true; }

        // Lê os campos para Dados/Titulo/G/Q. false = campo inválido (mensagem na janela).
        internal bool Montar()
        {
            try
            {
                string t = TipoAtual;
                string mat = c["material"].Text.Trim();
                var d = new JsonObject
                {
                    ["tipo"] = t,
                    ["material"] = mat.Length > 1 && (mat[0] == 'C' || mat[0] == 'c') && char.IsDigit(mat[1]) ? new JsonObject { ["concreto"] = D(mat.Substring(1)) } : (JsonNode)mat,
                    ["apoio"] = c["apoio"].Text.Trim().ToLowerInvariant().Replace("ó", "o"),
                };
                var sec = new JsonObject { ["pilar"] = Secao(c["pilar"].Text), ["viga"] = Secao(c["viga"].Text) };
                if (t == "galpao")
                {
                    foreach (string k in new[] { "vao", "altura_pilar", "altura_cumeeira", "espacamento" }) d[k] = D(c[k].Text);
                    if (!int.TryParse(c["n_modulos"].Text.Trim(), out int nm) || nm < 1) { erro.Text = "Módulos: número inteiro de 1 em diante."; return false; }
                    d["n_modulos"] = nm;
                    if (c["terca"].Text.Trim() != "") sec["terca"] = new JsonObject { ["perfil"] = c["terca"].Text.Trim() };
                    // sem contraventamento o galpão é hipostático na direção longitudinal (igual ao exemplo galpao_aco do motor)
                    sec["contraventamento"] = new JsonObject { ["forma"] = "tubo_circular", ["d"] = 0.2191, ["t"] = 0.00635 };
                    d["contraventamento"] = true;
                }
                else
                {
                    d["vaos_x"] = Lista(c["vaos_x"].Text); d["alturas"] = Lista(c["alturas"].Text);
                    if (t == "edificio") d["vaos_y"] = Lista(c["vaos_y"].Text);
                }
                d["secoes"] = sec;
                Dados = d; G = D(c["g"].Text); Q = D(c["q"].Text);
                Titulo = t == "galpao" ? "Galpão " + c["vao"].Text + " m × " + c["n_modulos"].Text + " módulos"
                       : (t == "edificio" ? "Edifício " : "Pórtico ") + ((JsonArray)d["vaos_x"]).Count + (t == "edificio" ? "×" + ((JsonArray)d["vaos_y"]).Count : "") + " vãos, " + Pl.N(((JsonArray)d["alturas"]).Count, "pavimento", "pavimentos");
                return true;
            }
            catch (FormatException) { erro.Text = "Confira os números (use espaço entre os valores das listas)."; }
            catch (IndexOutOfRangeException) { erro.Text = "Seção retangular: \"Ret b x h\" em cm (ex.: Ret 25x50)."; }
            return false;
        }
    }
}
