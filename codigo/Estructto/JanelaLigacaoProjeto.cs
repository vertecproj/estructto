// Projetista de ligações metálicas (docs/v3/LIGACOES-UI.md). Tudo vem de GET /api/ligacoes/tipos (familia, liga, campos/
// entradas, esforcos, sugerir, exemplo): um tipo novo de qualquer módulo de família aparece sem mudar esta tela.
//  Biblioteca: cartões por família com miniatura isométrica da `geometria` do exemplo do tipo (verificar com os padrões),
//              nome e etiquetas "liga"; busca e filtro pelo tipo de nó do grupo.
//  Projeto:    ① barras e esforços (do pior membro do grupo ou digitados) · ② parâmetros gerados do esquema (caixas com
//              unidade, listas, passos para parafusos, seletor de diâmetro, espessura comercial, perna de solda) · desenho ao
//              vivo (elevação, planta, lateral e 3D, falhas em vermelho) · ③ verificações com item da NBR, Sd × Rd e barra.
//              Recalcula 250 ms depois de cada mudança; Sugerir (sugerir() do tipo), Otimizar (ligacoes.otimizar),
//              Gerar memorial (HTML), Aplicar ao grupo (grava o projeto e verifica todos os membros).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Portico;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;
using static NovoFtool.JanelaEscura;

namespace NovoFtool
{
    // W7 (docs/v3/W7-ABA.md): controle hospedado numa aba da barra de documentos da janela principal (LigacaoAbas.cs), não mais Window.
    public class JanelaLigacaoProjeto : UserControl
    {
        public string Title { get; set; }                     // ponytail: o cabeçalho da aba usa NomeAba; Title só por compatibilidade
        public Action Fechou;                                 // Close(): a aba sai (MainWindow.FecharLigacao)
        public Action AbaMudou;                               // biblioteca ↔ padrão: o nome da aba muda
        public string NomeAba => (tipo == null ? "Biblioteca de ligações" : "Ligação · " + NomeTipo(Tipo())) + (grupo != null ? " · grupo " + S(grupo, "nome") : "");
        public void Close() { espera.Stop(); Fechou?.Invoke(); }
        Window Dono => Window.GetWindow(this) ?? Application.Current.MainWindow;   // diálogos: a janela que hospeda a aba
        bool carregou;
        // rotas sem estado (verificar, sugerir, otimizar, memorial) e, com grupo, as do documento (editar, dimensionar)
        public Func<string, string, JsonObject, Task<JsonNode>> Api = (m, r, c) => Motor.Chamar(m, r, c);
        public Func<string, string, JsonObject, Task<JsonNode>> ApiGrupo;
        public Action<JanelaLigacaoProjeto> AbrirPlaca;       // cartão "Placa de base": a JanelaPlaca (núcleo) com os esforços
        public Action Aplicado;                               // o projeto foi gravado no grupo (o gerenciador recarrega)

        static readonly HashSet<string> Antigos = new HashSet<string> { "parafuso", "solda_filete", "chapa_simples", "ligacao_tracao" };
        static readonly (string id, string nome)[] Familias =
        { ("flexivel", "Flexíveis"), ("rigida", "Rígidas"), ("emenda", "Emendas"), ("contraventamento", "Contraventamento e treliça"), ("base", "Bases"), ("tubo", "Tubos"), ("componente", "Componentes") };
        static readonly double[] DiamMm = { 15.875, 16, 19.05, 20, 22.225, 24, 25.4, 27 };
        static readonly double[] EspMm = { 4.75, 6.3, 8, 9.5, 12.5, 16, 19, 22.4, 25, 31.5, 37.5, 44.5, 50 };
        static readonly double[] PernaMm = { 3, 4, 5, 6, 8, 10, 12, 14, 16 };
        static readonly Dictionary<string, GeoLig> miniaturas = new Dictionary<string, GeoLig>();   // por sessão

        readonly List<JsonNode> tipos;
        readonly Dictionary<string, JsonObject> dados = new Dictionary<string, JsonObject>(), esforcos = new Dictionary<string, JsonObject>();
        readonly Border hostCab = new Border(), hostCorpo = new Border(), hostRodape = new Border();
        readonly DesenhoLigacao[] vistas =
        {
            new DesenhoLigacao(VistaLig.Elevacao) { Titulo = "Elevação" }, new DesenhoLigacao(VistaLig.Planta) { Titulo = "Planta" },
            new DesenhoLigacao(VistaLig.Lateral) { Titulo = "Lateral" }, new DesenhoLigacao(VistaLig.Iso) { Titulo = "3D" }
        };
        readonly DispatcherTimer espera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly HashSet<string> realces = new HashSet<string>();
        readonly HashSet<string> abertos = new HashSet<string> { "Chapa", "Parafusos", "Soldas", "Barra 1" };
        readonly Dictionary<string, FrameworkElement> celulas = new Dictionary<string, FrameworkElement>();   // campo -> célula (clique na peça rola até ela)
        readonly DispatcherTimer tDestaque = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        ScrollViewer rolagemPassos;
        JsonNode semSolucao; string campoLimitante;   // otimizador sem solução: faixa vermelha com o motivo e o botão para o campo
        string tipo, busca = "", familiaFiltro = "", filtroComp, status = "", erro;
        bool soDoGrupo = true, calculando, ocupado;
        int seq, vistaAtiva, abaRes;   // vista: 0 = quatro vistas, 1..4 = uma; abaRes: 0 verificações, 1 grupo
        JsonNode resultado, grupo, dimGrupo, aplicado, catalogo;
        string membroSel;              // chave no|barra|ponta do membro cujos esforços estão no passo ①; "" = digitados
        Grid areaVistas; TextBlock lblCalc;

        public JanelaLigacaoProjeto(JsonNode tiposJson, string tipoInicial = null)
        {
            tipos = Lista(tiposJson).ToList();
            foreach (var t in tipos)
            {
                string id = S(t, "tipo");
                dados[id] = t["exemplo"]?["dados"] is JsonObject d ? (JsonObject)d.DeepClone() : new JsonObject();
                esforcos[id] = t["exemplo"]?["esforcos"] is JsonObject e ? (JsonObject)e.DeepClone() : new JsonObject();
                foreach (var c in Campos(t)) if (dados[id][S(c, "nome")] == null && c["padrao"] != null) dados[id][S(c, "nome")] = c["padrao"].DeepClone();
            }
            Title = "Projetar ligação";
            Background = BFundo; Foreground = BTexto; FontFamily = TemaPlant.Fonte; FontSize = 12;   // o que a JanelaEscura dava
            UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Focusable = true; FocusVisualStyle = null;
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);   // Tab circula dentro da aba
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };   // Esc fecha, como na janela
            Content = Raiz(hostCab, hostCorpo, hostRodape);
            espera.Tick += async (s, e) => { espera.Stop(); await CalcularAgora(); };
            tDestaque.Tick += (s, e) => { tDestaque.Stop(); if (celDestaque != null) { celDestaque.Background = Brushes.Transparent; celDestaque.BorderBrush = Brushes.Transparent; } celDestaque = null; campoDestaque = null; };
            foreach (var v in vistas) v.ClicouPeca += IrParaTag;
            tipo = tipos.Any(x => S(x, "tipo") == tipoInicial) ? tipoInicial : null;
            Montar();
            Loaded += async (s, e) =>
            {
                if (carregou || Capturas.Ativo) return;
                carregou = true;
                if (tipo != null) await CalcularAgora(); else await CarregarMiniaturas();
            };
        }

        // ================================================================ dados
        JsonNode Tipo(string id = null) => tipos.FirstOrDefault(x => S(x, "tipo") == (id ?? tipo));
        static IEnumerable<JsonNode> Campos(JsonNode t)
        {
            if (t?["campos"] is JsonArray a) return a.Where(x => x != null);
            if (t?["entradas"] is JsonObject o) return o.Select(kv => { var c = (JsonObject)kv.Value.DeepClone(); c["nome"] = kv.Key; return (JsonNode)c; }).ToList();
            return Enumerable.Empty<JsonNode>();
        }
        static IEnumerable<string> NomesEsforcos(JsonNode t) => Lista(t?["esforcos"]).Select(x => x is JsonValue ? x.ToString() : S(x, "nome"));
        // ligação entre barras: os 6 esforços da ponta sempre na tela (eixos do desenho: x = eixo da barra, y = fora do plano da
        // alma, z = vertical). O motor recebe todos e avisa os que o tipo não verifica (ligacoes_rigidas/_flexiveis._esforcos).
        static readonly string[] Seis = { "N", "Vz", "V", "T", "M", "Mz" };
        static IEnumerable<string> EsforcosTela(JsonNode t)
        {
            var ns = NomesEsforcos(t).ToList();
            return ns.Count > 0 && ns.All(Seis.Contains) ? Seis : ns;
        }
        JsonObject Dados => dados[tipo];
        JsonObject Esf => esforcos[tipo];
        static string Norm(string s)
        {
            var n = (s ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Replace('-', '_');
        }
        static string NomeFamilia(string f) => Familias.FirstOrDefault(x => x.id == f).nome ?? (string.IsNullOrEmpty(f) ? "Outras" : char.ToUpper(f[0]) + f.Substring(1));
        static string NomeLiga(string l)
        {
            switch (Norm(l))
            {
                case "viga_pilar_mesa": return "viga–pilar · mesa";
                case "viga_pilar_alma": return "viga–pilar · alma";
                case "viga_viga": return "viga–viga";
                case "gusset_contraventamento": return "contraventamento";
                case "no_trelica": return "nó de treliça";
                default: return (l ?? "").Replace('_', ' ').Replace('-', ' ');
            }
        }
        string NomeTipo(JsonNode t) => S(t, "nome") != "" ? S(t, "nome") : S(t, "tipo");

        // grupo de componente de uma verificação (cor no desenho e chips do topo). ponytail: pelas palavras do nome.
        static string GrupoDe(string nome)
        {
            string n = Norm(nome);
            if (n.Contains("solda") || n.Contains("filete") || n.Contains("metal base")) return "soldas";
            if (n.StartsWith("ramal")) return "viga";   // tubo–tubo: ramal = barra que chega, receptor = apoio
            if (n.StartsWith("receptor")) return "suporte";
            if (n.Contains("alma da viga") || n.Contains("na alma") || n.Contains("viga recortada") || n.Contains("viga:") || n.StartsWith("viga")) return "viga";
            if (n.Contains("cantoneira")) return "cantoneiras";
            if (n.Contains("pilar") || n.Contains("suporte") || n.Contains("apoio")) return "suporte";
            if (n.Contains("parafuso") || n.Contains("espacamento") || n.Contains("rosca") || n.Contains("pino") || n.Contains("barra redonda")) return "parafusos";
            return "chapas";
        }
        static readonly Dictionary<string, string> NomeGrupo = new Dictionary<string, string>
        { ["parafusos"] = "Parafusos", ["chapas"] = "Chapa", ["soldas"] = "Solda", ["cantoneiras"] = "Cantoneiras", ["viga"] = "Viga", ["suporte"] = "Apoio" };

        static bool Passa(JsonNode v) => !(D(v, "taxa") > 1 + 1e-9) && !(v?["ok"] is JsonValue o && o.TryGetValue(out bool b) && !b);
        static double TaxaDe(JsonNode r) { double t = D(r, "taxa"); return double.IsNaN(t) ? D(r, "taxa_max") : t; }

        // esforços do modelo na ponta (N, Vy, Vz, T, My, Mz) → esforços do tipo. Igual a ligacoes_modelo (_ESFORCOS_LIG e
        // esforcos_contrato): antigos com V = √(Vy²+Vz²); contrato com V = |Vz| (plano da alma), Vz = |Vy|, M = My.
        static JsonObject EsforcosDoMembro(JsonNode e, JsonNode t)
        {
            double Z(string k) { double v = D(e, k); return double.IsNaN(v) ? 0 : v; }
            double N = Z("N"), Vy = Z("Vy"), Vz = Z("Vz"), My = Z("My"), Mz = Z("Mz"), T = Z("T");
            bool antigo = Antigos.Contains(S(t, "tipo"));
            var o = new JsonObject();
            foreach (var n in EsforcosTela(t))
                o[n] = Math.Round(n switch
                {
                    "Ft" => Math.Max(N, 0), "Fv" => Math.Sqrt(Vy * Vy + Vz * Vz), "F" => Math.Sqrt(N * N + Vy * Vy + Vz * Vz),
                    "V" => antigo ? Math.Sqrt(Vy * Vy + Vz * Vz) : Math.Abs(Vz), "N" => N,   // com sinal: ligação de tração recusa compressão no motor (RC11), em vez de verificar |N| como tração
                    "Vz" => Math.Abs(Vy), "M" => My, "Mz" => Mz, "T" => T, _ => 0
                }, 3);
            return o;
        }

        // ================================================================ grupo
        static string Chave(JsonNode m) => S(m, "no") + "|" + S(m, "barra") + "|" + S(m, "ponta");
        List<JsonNode> MembrosGrupo() => Lista(dimGrupo?["membros"]).Where(m => m["esforcos"] != null).OrderByDescending(m => Math.Max(D(m, "taxa") is double t && !double.IsNaN(t) ? t : 0, 0))
            .ThenByDescending(m => D(m["demanda"], "V") + D(m["demanda"], "N") + D(m["demanda"], "M")).ToList();
        JsonNode Membro(string k) => Lista(dimGrupo?["membros"]).FirstOrDefault(m => Chave(m) == k);
        string TipoNoGrupo => S(grupo, "tipo");

        // contexto do grupo: esforços do membro que governa, perfis pela assinatura, filtro pelo tipo de nó
        public void UsarGrupo(JsonNode g, JsonNode dim)
        {
            grupo = g; dimGrupo = dim;
            var gov = dim?["governante"] ?? dim?["por_demanda"];
            membroSel = gov != null ? Chave(gov) : MembrosGrupo().Select(Chave).FirstOrDefault() ?? "";
            Title = "Projetar ligação — grupo " + S(g, "nome");
            var p = S(g, "assinatura").Split('|');
            foreach (var t in tipos)
            {
                string id = S(t, "tipo");
                var cs = Campos(t).ToList();
                foreach (var c in cs)
                {
                    string n = S(c, "nome");
                    if (n == "perfil_viga" && p.Length > 1 && p[1] != "") dados[id][n] = p[1];
                    if ((n == "perfil_suporte" || n == "perfil_pilar") && p.Length > 2 && p[2] != "") dados[id][n] = p[2];
                    if (n == "apoio" && c["opcoes"] is JsonArray op)
                    {
                        var ops = op.Select(x => x.ToString()).ToList();
                        string alvo = TipoNoGrupo == "viga_pilar_mesa" ? ops.FirstOrDefault(o => o.Contains("mesa")) : TipoNoGrupo == "viga_pilar_alma" ? ops.FirstOrDefault(o => o.Contains("alma") && o.Contains("pilar"))
                                    : TipoNoGrupo == "viga_viga" ? ops.FirstOrDefault(o => o.Contains("viga")) : null;
                        if (alvo != null) dados[id][n] = alvo;
                    }
                }
                var m = Membro(membroSel);
                if (m != null) esforcos[id] = EsforcosDoMembro(m["esforcos"], t);
            }
            string tl = S(g["projeto"], "tipo_ligacao");
            if (Tipo(tl) != null)
            {
                foreach (var kv in (g["projeto"]?["dados"] as JsonObject) ?? new JsonObject()) dados[tl][kv.Key] = kv.Value?.DeepClone();
                tipo = tl;
            }
            Montar();
        }

        // ================================================================ cálculo
        void Agendar() { resultadoVelho = true; espera.Stop(); espera.Start(); MontarStatus(); }
        bool resultadoVelho;

        public async Task CalcularAgora()
        {
            if (tipo == null) return;
            int meu = ++seq;
            calculando = true; MontarStatus();
            try
            {
                var r = await Api("POST", "/api/ligacoes/verificar", Corpo());
                if (meu != seq) return;
                resultado = r; erro = null;
            }
            catch (Exception ex) { if (meu != seq) return; erro = ComandosAnalise.MensagemMotor(ex); }
            finally { if (meu == seq) { calculando = false; resultadoVelho = false; } }
            Atualizar();
        }

        JsonObject Corpo() => new JsonObject { ["tipo"] = tipo, ["dados"] = Dados.DeepClone(), ["esforcos"] = Esf.DeepClone() };

        public async Task CarregarMiniaturas()
        {
            foreach (var t in tipos)
            {
                string id = S(t, "tipo");
                if (miniaturas.ContainsKey(id) || t["exemplo"] == null) continue;
                try
                {
                    var r = await Api("POST", "/api/ligacoes/verificar", new JsonObject { ["tipo"] = id, ["dados"] = t["exemplo"]["dados"].DeepClone(), ["esforcos"] = t["exemplo"]["esforcos"].DeepClone() });
                    miniaturas[id] = GeoLig.De(r?["geometria"]);
                }
                catch (Exception) { miniaturas[id] = null; }
            }
            if (tipo == null) Montar();
        }

        // API das capturas
        public async Task Escolher(string id, bool sugerir = true)
        {
            if (Tipo(id) == null) return;
            tipo = id; resultado = null; erro = null; filtroComp = null; aplicado = null; abaRes = 0; realces.Clear(); status = ""; semSolucao = null; aviso = null;
            Montar();
            if (sugerir && grupo != null && Bool(Tipo(), "sugerir")) await Sugerir(); else await CalcularAgora();
        }
        public void DefinirEsforcos(JsonObject e) { foreach (var kv in e) Esf[kv.Key] = kv.Value?.DeepClone(); membroSel = ""; Montar(); }
        public void DefinirDados(JsonObject d) { foreach (var kv in d) Dados[kv.Key] = kv.Value?.DeepClone(); Montar(); }
        // Arquivo de conexão (.pcon, ConexaoArquivo.cs): salvar/abrir a entrada desta ligação. Sem caminho, pergunta.
        string arquivoConexao;
        public void SalvarConexao()
        {
            if (tipo == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Conexão do Estructto (*" + ConexaoArquivo.Ext + ")|*" + ConexaoArquivo.Ext, FileName = arquivoConexao != null ? Path.GetFileName(arquivoConexao) : tipo + ConexaoArquivo.Ext };
            if (dlg.ShowDialog(Dono) != true) return;
            try { ConexaoArquivo.Gravar(dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName), tipo, Dados, Esf); arquivoConexao = dlg.FileName; status = "Conexão salva em " + dlg.FileName; }
            catch (Exception ex) { status = "Nenhuma conexão salva: " + ex.Message; }
            MontarRodape();
        }
        public async Task AbrirConexao(string caminho = null)
        {
            if (caminho == null)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Conexão do Estructto (*" + ConexaoArquivo.Ext + ")|*" + ConexaoArquivo.Ext };
                if (dlg.ShowDialog(Dono) != true) return;
                caminho = dlg.FileName;
            }
            try
            {
                var c = ConexaoArquivo.Ler(File.ReadAllText(caminho));
                if (Tipo(c.tipo) == null) { status = "Nenhuma conexão aberta: o tipo '" + c.tipo + "' não existe nesta versão do motor."; MontarRodape(); return; }
                await Escolher(c.tipo, false);
                DefinirDados(c.dados); DefinirEsforcos(c.esforcos);
                arquivoConexao = caminho; status = "Conexão aberta: " + Path.GetFileName(caminho);
                await CalcularAgora();
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException) { status = "Nenhuma conexão aberta: " + ex.Message; MontarRodape(); }
        }
        public void MostrarVista(int v) { vistaAtiva = v; Montar(); }
        public void MostrarBiblioteca() { tipo = null; Montar(); }
        public void Buscar(string s) { busca = s ?? ""; Montar(); }
        public JsonNode Resultado => resultado;
        public JsonObject DadosAtuais => tipo == null ? null : Dados;   // capturas W10-SD
        public string TipoAtual => tipo; public string Erro => erro; public string Status => status;   // capturas W7-UPN
        // capturas/testes: clica (hit-test real) no centro da 1a chapa da vista 3D; devolve o tag achado
        public string ClicarChapa3D()
        {
            var c = vistas[3].CentroChapa(); if (c == null) return null;
            string tag = vistas[3].PecaEm(c.Value);
            if (tag != null) IrParaTag(tag);
            return tag;
        }
        public void AbrirTodasSecoes() { foreach (var c in Campos(Tipo())) { string sc = Secao(c); if (sc != "barras") abertos.Add(sc); } Montar(); }
        public bool TemTipo => tipo != null;
        // teste: cada peça da geometria atual (perfis, chapas, cantoneiras, parafusos, soldas) tem um Tag que resolve a um campo do padrão
        public List<string> PecasSemCampo()
        {
            var r = new List<string>(); var g = GeoLig.De(resultado?["geometria"]);
            foreach (var p in g.Pecas.Where(q => q.Camada > 0 || q.Tag != "")) if (CampoDoTag(p.Tag) == null) r.Add((p.Camada == 0 ? "perfil " : "") + p.Grupo + " " + p.Nome + " [" + p.Tag + "]");
            foreach (var b in g.Parafs) if (CampoDoTag(b.Tag) == null) r.Add("parafuso [" + b.Tag + "]");
            foreach (var s in g.Soldas) if (CampoDoTag(s.Tag) == null) r.Add("solda [" + s.Tag + "]");
            return r.Distinct().ToList();
        }
        public bool SemSolucao => semSolucao != null;
        public IEnumerable<string> NomesSecoes(string id)
        {
            var ant = tipo; tipo = id;
            try { return Campos(Tipo(id)).Select(Secao).Where(x => x != "barras" && x != "Materiais e opções" && !x.StartsWith("Barra")).Distinct().ToList(); }
            finally { tipo = ant; }
        }
        public void RecolherSecoes() { abertos.Clear(); Montar(); }

        public async Task Sugerir()
        {
            if (tipo == null || ocupado) return;
            var perfis = new JsonObject();
            foreach (var kv in Dados)
                if (kv.Key.StartsWith("perfil_") || kv.Key == "apoio" || kv.Key.StartsWith("secao") || kv.Key.StartsWith("dim_") || kv.Key.Contains("_perfil") || kv.Key.StartsWith("t_perfil"))
                    perfis[kv.Key] = kv.Value?.DeepClone();
            await Rodar(async () =>
            {
                var r = await Api("POST", "/api/ligacoes/sugerir", new JsonObject { ["tipo"] = tipo, ["perfis"] = perfis, ["esforcos"] = Esf.DeepClone() });
                realces.Clear();
                // G5: viga U sem parafusada que caiba/passe → o motor troca de tipo (tipo_ligacao + motivo); a tela segue a troca e diz por quê
                string troca = S(r, "tipo_ligacao"), porque = "";
                if (troca != "" && troca != tipo && Tipo(troca) != null)
                {
                    porque = "Trocado para " + S(Tipo(troca), "nome") + (S(r, "motivo") != "" ? ": " + S(r, "motivo") : "") + ". ";
                    foreach (var kv in perfis) dados[troca][kv.Key] = kv.Value?.DeepClone();
                    foreach (var kv in Esf) esforcos[troca][kv.Key] = kv.Value?.DeepClone();
                    tipo = troca; resultado = null; erro = null; filtroComp = null; semSolucao = null;
                }
                if (r is JsonObject o)
                    foreach (var kv in o)
                    {
                        if (kv.Value == null || kv.Key == "tipo_ligacao" || kv.Key == "motivo") continue;
                        if (Dados[kv.Key]?.ToJsonString() != kv.Value.ToJsonString()) realces.Add(kv.Key);
                        Dados[kv.Key] = kv.Value.DeepClone();
                    }
                status = porque + (realces.Count > 0 ? "Pré-dimensionado pelo motor: " + realces.Count + " campos ajustados (em destaque)." : "Sugestão do motor = dados atuais.");
            });
            Montar();
            await CalcularAgora();
        }

        public async Task Otimizar()
        {
            if (tipo == null || ocupado) return;
            await Rodar(async () =>
            {
                var r = await Api("POST", "/api/ligacoes/otimizar", Corpo());
                semSolucao = null; campoLimitante = null;
                if (r != null && r["ok"] != null && !Bool(r, "ok"))   // sem solução: o motor devolve os dados originais; para e explica
                {
                    semSolucao = r; campoLimitante = CampoLimitante(r);
                    // sem resultado novo (ex.: motor recusou o perfil) mantém o erro: antes o resultado antigo voltava como se fosse deste perfil
                    if (r["resultado"] != null) { resultado = r["resultado"]; erro = null; } seq++; realces.Clear();
                    status = "Sem solução — nada foi alterado.";
                    return;
                }
                if (r?["dados"] is JsonObject d) foreach (var kv in d) Dados[kv.Key] = kv.Value?.DeepClone();
                realces.Clear();
                var partes = new List<string>();
                foreach (var kv in (r?["mudancas"] as JsonObject) ?? new JsonObject())
                {
                    realces.Add(kv.Key);
                    var c = Campos(Tipo()).FirstOrDefault(x => S(x, "nome") == kv.Key);
                    partes.Add(Rotulo(c, kv.Key) + " " + Valor(c, kv.Value?[0]) + " → " + Valor(c, kv.Value?[1]));
                }
                if (r?["resultado"] != null) { resultado = r["resultado"]; erro = null; } seq++;
                status = (Bool(r, "ok") ? "Otimizado (menor detalhe que passa): " : "Nenhum aumento resolve sozinho — melhor encontrado: ") + (partes.Count > 0 ? string.Join(" · ", partes) : "sem mudanças");
            });
            Montar();
        }

        public async Task AplicarAoGrupo()
        {
            if (grupo == null || ApiGrupo == null || tipo == null) return;
            string gid = S(grupo, "id");
            await Rodar(async () =>
            {
                await ApiGrupo("POST", "/api/ligacoes/grupos/editar", new JsonObject { ["acao"] = "projeto", ["grupo"] = gid, ["tipo_ligacao"] = tipo, ["dados"] = Dados.DeepClone() });
                aplicado = await ApiGrupo("POST", "/api/ligacoes/grupos/" + Uri.EscapeDataString(gid) + "/dimensionar", new JsonObject());
                dimGrupo = aplicado;
                abaRes = 1;
                int n = Lista(aplicado?["membros"]).Count();
                status = "Projeto gravado no grupo " + S(grupo, "nome") + " e verificado nas " + n + " ligações (todas as combinações ELU).";
            });
            Aplicado?.Invoke();
            Montar();
        }

        public async Task GerarMemorial()
        {
            if (tipo == null) return;
            await Rodar(async () =>
            {
                var r = await Api("POST", "/api/ligacoes/memorial", Corpo());
                string pasta = Path.Combine(Path.GetTempPath(), "Estructto");
                Directory.CreateDirectory(pasta);
                string arq = Path.Combine(pasta, "memorial-ligacao-" + tipo + ".html");
                File.WriteAllText(arq, PaginaMemorial(S(r, "html")), Encoding.UTF8);
                status = "Memorial salvo em " + arq;
                if (!Capturas.Ativo) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(arq) { UseShellExecute = true });
            });
            MontarRodape();
        }

        // seção do motor (projeto.memorial_ligacao) numa página para imprimir; o relatório do motor não tem gancho de ligações
        static string PaginaMemorial(string secao) =>
            "<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Memorial da ligação</title><style>" +
            "body{font:13px 'Segoe UI',sans-serif;color:#0f172a;margin:24px 32px}h2{font-size:18px;margin:0 0 8px}h3{font-size:13px;margin:18px 0 6px;text-transform:uppercase;color:#334155}" +
            "table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #cbd5e1;padding:4px 6px;text-align:left;vertical-align:top}th{background:#f1f5f9;font-weight:600}" +
            ".n{text-align:right;font-variant-numeric:tabular-nums}.f{font-family:'Cambria Math',serif}.falha td{color:#be123c}.veredito{font-weight:700;font-size:14px}" +
            ".veredito.ok{color:#15803d}.veredito.falha{color:#be123c}table.dados{width:auto}table.dados td:first-child{padding-right:24px;color:#475569}.nota{color:#64748b;margin-top:16px}" +
            "</style></head><body>" + secao + "</body></html>";

        async Task Rodar(Func<Task> op)
        {
            if (ocupado) return;
            ocupado = true; MontarRodape();
            try { await op(); }
            catch (Exception ex) { status = "Motor: " + ComandosAnalise.MensagemMotor(ex); }
            finally { ocupado = false; }
            MontarRodape();
        }

        // ================================================================ montagem
        void Montar()
        {
            if (tipo == null) MontarBiblioteca(); else MontarProjeto();
            MontarRodape();
            AbaMudou?.Invoke();
        }

        void Atualizar()
        {
            if (tipo == null) return;
            MontarCabecalho(); MontarDesenho(); MontarResultados(); MontarStatus(); MontarRodape();
        }

        // ---------------------------------------------------------------- biblioteca
        List<JsonNode> TiposVisiveis()
        {
            string q = Norm(busca.Trim());
            return tipos.Where(t =>
            {
                if (familiaFiltro != "" && S(t, "familia") != familiaFiltro) return false;
                if (grupo != null && soDoGrupo && TipoNoGrupo != "" && !Lista(t["liga"]).Any(l => Norm(l.ToString()) == Norm(TipoNoGrupo))) return false;
                if (q == "") return true;
                string tudo = Norm(NomeTipo(t) + " " + S(t, "descricao") + " " + S(t, "tipo") + " " + NomeFamilia(S(t, "familia")) + " " + string.Join(" ", Lista(t["liga"]).Select(l => NomeLiga(l.ToString()))));
                return q.Split(' ').All(p => tudo.Contains(p));
            }).ToList();
        }

        bool BaseVisivel()
        {
            if (familiaFiltro != "" && familiaFiltro != "base") return false;
            if (grupo != null && soDoGrupo && TipoNoGrupo != "" && TipoNoGrupo != "base") return false;
            string q = Norm(busca.Trim());
            return q == "" || q.Split(' ').All(p => Norm("placa de base chumbador pilar bases").Contains(p));
        }

        void MontarBiblioteca()
        {
            int nf = tipos.Select(t => S(t, "familia")).Append("base").Distinct().Count();
            hostCab.Child = Cabecalho(Nivel.Info, "≡", "Biblioteca de ligações · " + (tipos.Count + 1) + " padrões em " + nf + " famílias",
                grupo != null ? "Grupo " + S(grupo, "nome") + " · " + S(grupo, "rotulo") + " — escolha o padrão; o motor pré-dimensiona com os esforços do pior membro e você ajusta."
                              : "Escolha um padrão: o motor calcula com os valores típicos e você ajusta chapas, parafusos e soldas vendo o desenho e as verificações.");
            var dp = new DockPanel { Margin = new Thickness(16, 12, 16, 0) };
            var barra = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var cx = CaixaBusca("Buscar (chapa, cantoneira, gusset, viga–viga…)", 300, s => { busca = s; MontarListaBiblioteca(); }, out TextBox tb);
            tb.Text = busca;
            DockPanel.SetDock(cx, Dock.Left); barra.Children.Add(cx);
            var fams = new List<(string, bool, Action, string)> { ("Todas", familiaFiltro == "", () => { familiaFiltro = ""; MontarBiblioteca(); }, null) };
            foreach (var f in Familias.Where(f => f.id == "base" || tipos.Any(t => S(t, "familia") == f.id)))
                fams.Add((f.nome, familiaFiltro == f.id, () => { familiaFiltro = familiaFiltro == f.id ? "" : f.id; MontarBiblioteca(); }, null));
            foreach (var f in tipos.Select(t => S(t, "familia")).Distinct().Where(f => Familias.All(x => x.id != f)))
                fams.Add((NomeFamilia(f), familiaFiltro == f, () => { familiaFiltro = familiaFiltro == f ? "" : f; MontarBiblioteca(); }, null));
            var seg = Segmentado(fams); seg.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(seg, Dock.Left); barra.Children.Add(seg);
            if (grupo != null)
            {
                var ch = Chip((soDoGrupo ? "✓ " : "") + "Só para " + S(grupo, "rotulo"), soDoGrupo ? Nivel.Info : Nivel.Neutro, "Mostra só os padrões que servem para o tipo de nó deste grupo (campo liga do tipo).",
                              () => { soDoGrupo = !soDoGrupo; MontarBiblioteca(); }, 11.5);
                ch.Margin = new Thickness(12, 0, 0, 0); ch.Height = 28;
                DockPanel.SetDock(ch, Dock.Left); barra.Children.Add(ch);
            }
            barra.Children.Add(new Border());
            DockPanel.SetDock(barra, Dock.Top); dp.Children.Add(barra);
            hostLista.Margin = new Thickness(0);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Soltar(hostLista) });
            hostCorpo.Child = dp;
            MontarListaBiblioteca();
        }
        readonly Border hostLista = new Border();

        void MontarListaBiblioteca()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var vis = TiposVisiveis();
            var ordem = Familias.Select(f => f.id).Concat(vis.Select(t => S(t, "familia")).Distinct().Where(f => Familias.All(x => x.id != f))).ToList();
            int total = 0;
            foreach (var f in ordem)
            {
                var lista = vis.Where(t => S(t, "familia") == f).ToList();
                bool base_ = f == "base" && BaseVisivel();
                if (lista.Count == 0 && !base_) continue;
                var wp = new WrapPanel();
                foreach (var t in lista) wp.Children.Add(CartaoTipo(t));
                if (base_) wp.Children.Add(CartaoPlaca());
                total += wp.Children.Count;
                sp.Children.Add(TituloSecao(NomeFamilia(f) + " · " + wp.Children.Count, null, sp.Children.Count == 0 ? 2 : 14));
                sp.Children.Add(wp);
                Anim.Escalonar(wp, 4);
            }
            if (total == 0) sp.Children.Add(Vazio("busca", "Nenhum padrão encontrado", grupo != null && soDoGrupo ? "Nenhum tipo do motor serve para " + S(grupo, "rotulo") + " com esta busca. Desligue o filtro do grupo ou limpe a busca." : "Limpe a busca ou escolha outra família."));
            hostLista.Child = sp;
        }

        Border CartaoBib(FrameworkElement mini, string nome, string desc, IEnumerable<Border> chips, Action clique, string dica)
        {
            var sp = new StackPanel();
            sp.Children.Add(new Border { Height = 132, Background = TemaPlant.BBgCanvas, CornerRadius = new CornerRadius(5, 5, 0, 0), ClipToBounds = true, Child = mini });
            var corpo = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
            var tn = Tx(nome, 12.5, TemaPlant.BTexto1, FontWeights.SemiBold, true); tn.MaxHeight = 34; tn.TextTrimming = TextTrimming.CharacterEllipsis;
            corpo.Children.Add(tn);
            var td = Tx(desc, 11, TemaPlant.BTexto3); td.TextTrimming = TextTrimming.CharacterEllipsis; td.Margin = new Thickness(0, 2, 0, 6);
            corpo.Children.Add(td);
            var wc = new WrapPanel();
            foreach (var c in chips) { c.Margin = new Thickness(0, 0, 4, 4); wc.Children.Add(c); }
            corpo.Children.Add(wc);
            sp.Children.Add(corpo);
            var b = new Border
            {
                Width = 248, Height = 236, Margin = new Thickness(0, 0, 12, 12), Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Child = sp, Cursor = Cursors.Hand, ClipToBounds = true
            };
            b.MouseEnter += (s, e) => { b.BorderBrush = TemaPlant.BAcento; b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { b.BorderBrush = TemaPlant.BBordaSutil; b.Background = TemaPlant.BSup2; };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; clique(); };
            System.Windows.Automation.AutomationProperties.SetName(b, nome);
            return Dica(b, dica);
        }

        Border CartaoTipo(JsonNode t)
        {
            string id = S(t, "tipo");
            var mini = new DesenhoLigacao(VistaLig.Iso, true) { IsHitTestVisible = false };
            if (miniaturas.TryGetValue(id, out var g) && g != null) mini.Mostrar(g, false);
            var chips = Lista(t["liga"]).Select(l => Chip(NomeLiga(l.ToString()), Nivel.Neutro, null, null, 10)).ToList();
            if (Bool(t, "sugerir")) chips.Add(Chip("pré-dimensiona", Nivel.Info, "O motor sugere chapas, parafusos e soldas a partir dos perfis e esforços.", null, 10));
            return CartaoBib(mini, NomeTipo(t), S(t, "descricao"), chips, () => _ = Escolher(id), NomeTipo(t) + "\n" + S(t, "descricao"));
        }

        Border CartaoPlaca()
        {
            var mini = new DesenhoLigacao(VistaLig.Iso, true) { IsHitTestVisible = false };
            mini.Mostrar(GeoLig.PlacaBase(), false);
            return CartaoBib(mini, "Placa de base", "Placa, chumbadores, concreto e solda do pilar (janela da placa de base)",
                new[] { Chip("base", Nivel.Neutro, null, null, 10), Chip("pré-dimensiona", Nivel.Info, null, null, 10) },
                () => { if (AbrirPlaca != null) AbrirPlaca(this); else status = "Placa de base: abra pelo nó do pilar (Ligações e bases › Placa de base)."; MontarRodape(); },
                "Abre a janela da placa de base (NBR 8800 + ACI 318 cap. 17) com os esforços do pé do pilar.");
        }

        // ---------------------------------------------------------------- projeto
        readonly Border hostPassos = new Border(), hostDesenho = new Border(), hostRes = new Border(), hostStatusDes = new Border();

        void MontarProjeto()
        {
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(408) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(392) });
            var esq = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 1, 0),
                                   Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 10, 10, 6), Content = Soltar(hostPassos) } };
            rolagemPassos = (ScrollViewer)esq.Child;
            corpo.Children.Add(esq);
            Grid.SetColumn(Soltar(hostDesenho), 1); corpo.Children.Add(hostDesenho);
            var dir = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 10, 10, 10), Child = Soltar(hostRes) };
            Grid.SetColumn(dir, 2); corpo.Children.Add(dir);
            hostCorpo.Child = corpo;
            MontarPassos(); MontarCabecalho(); MontarDesenho(); MontarResultados(); MontarStatus();
        }

        void Mudou(string campo = null)
        {
            if (campo != null) realces.Remove(campo);
            status = ""; aplicado = null; semSolucao = null; aviso = null; if (abaRes == 1 && grupo == null) abaRes = 0;
            MontarPassos();
            Agendar();
        }

        void MontarCabecalho()
        {
            var t = Tipo();
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var vs = erro != null ? new List<JsonNode>() : Lista(resultado?["verificacoes"]).ToList();   // W7-UPN: com erro, nada do resultado antigo
            foreach (var gr in vs.GroupBy(v => GrupoDe(S(v, "nome"))).OrderBy(g => Array.IndexOf(new[] { "parafusos", "chapas", "soldas", "cantoneiras", "viga", "suporte" }, g.Key)))
            {
                double tx = gr.Max(v => double.IsNaN(D(v, "taxa")) || double.IsInfinity(D(v, "taxa")) ? 9.99 : D(v, "taxa"));
                bool ok = gr.All(Passa);
                string k = gr.Key;
                var c = Chip((ok ? "✓ " : "✕ ") + NomeGrupo[k] + " " + Pct(tx), NivelAp(tx, ok), "Clique para ver só as verificações de " + NomeGrupo[k].ToLowerInvariant() + ".",
                             () => { filtroComp = filtroComp == k ? null : k; MontarCabecalho(); MontarResultados(); }, 11.5);
                c.Margin = new Thickness(6, 0, 0, 0);
                if (filtroComp == k) c.BorderBrush = TemaPlant.BTexto1;
                chips.Children.Add(c);
            }
            string nome = NomeTipo(t);
            if (erro != null) { hostCab.Child = Cabecalho(Nivel.Erro, "✕", "Não foi possível verificar — " + nome, erro, chips); return; }
            if (resultado == null) { hostCab.Child = Cabecalho(Nivel.Neutro, "…", nome, "Calculando com os valores do padrão…", chips); return; }
            double taxa = TaxaDe(resultado);
            bool passa = Bool(resultado, "ok");
            var nv = NivelAp(taxa, passa);
            var pior = vs.OrderByDescending(v => D(v, "taxa")).FirstOrDefault();
            string tit = (nv == Nivel.Erro ? "Não passa — " : "Passa · ") + "usa " + Pct(taxa) + (pior != null ? " em " + S(pior, "nome") : "");
            string sub = nome + " · " + NomeFamilia(S(t, "familia")) + (pior != null ? " · governa " + Item(S(pior, "item")) : "") + " · " + vs.Count + " verificações" +
                         (grupo != null ? " · grupo " + S(grupo, "nome") : "");
            hostCab.Child = Cabecalho(nv, nv == Nivel.Erro ? "✕" : nv == Nivel.Alerta ? "!" : "✓", tit, sub, chips);
        }

        static string Item(string it) => it == "" ? "" : char.IsDigit(it[0]) ? "NBR 8800 " + it : it;   // contrato já traz "NBR 8800 …"

        // ① barras e esforços  ·  ② parâmetros
        void MontarPassos()
        {
            var t = Tipo();
            var sp = new StackPanel();
            celulas.Clear();
            if (aviso != null) sp.Children.Add(FaixaAviso());
            if (semSolucao != null) sp.Children.Add(FaixaSemSolucao());
            // tipo escolhido
            var topo = new DockPanel { Margin = new Thickness(2, 0, 0, 10) };
            var trocar = Link("Trocar padrão", () => { tipo = null; Montar(); _ = CarregarMiniaturas(); }, "Volta à biblioteca de ligações.");
            DockPanel.SetDock(trocar, Dock.Right); topo.Children.Add(trocar);
            var tt = new StackPanel();
            tt.Children.Add(Tx(NomeTipo(t), 14, TemaPlant.BTexto1, FontWeights.SemiBold));
            var d0 = Tx(S(t, "descricao"), 11, TemaPlant.BTexto3, null, true); d0.Margin = new Thickness(0, 2, 0, 0); tt.Children.Add(d0);
            topo.Children.Add(tt);
            sp.Children.Add(topo);

            var campos = Campos(t).ToList();
            // ① barras e esforços
            var p1 = new StackPanel();
            if (grupo != null)
            {
                var ms = MembrosGrupo();
                var nomes = new List<string> { "Digitados" };
                string Rot(JsonNode m) => S(m, "barra") + " · " + S(m, "ponta") + " no nó " + S(m, "no") + " — " + S(m, "combinacao") + (dimGrupo?["governante"] != null && Chave(dimGrupo["governante"]) == Chave(m) ? "  (governa)" : "");
                nomes.AddRange(ms.Select(Rot));
                var sel = ms.FirstOrDefault(m => Chave(m) == membroSel);
                var cb = Combo(nomes, sel != null ? Rot(sel) : "Digitados", x =>
                {
                    var m = ms.FirstOrDefault(q => Rot(q) == x);
                    membroSel = m != null ? Chave(m) : "";
                    if (m != null) esforcos[tipo] = EsforcosDoMembro(m["esforcos"], t);
                    Mudou();
                }, 376, "Esforços de cálculo da ponta de barra (pior combinação ELU daquele membro). O pior do grupo vem primeiro.");
                p1.Children.Add(Celula("Esforços de (" + Pl.N(ms.Count, "ligação", "ligações") + " do grupo)", cb, true));
            }
            var gp = GradeCampos();
            foreach (var c in campos.Where(c => Secao(c) == "barras")) AddCelula(gp, Rotulo(c, S(c, "nome")), Controle(c), Largo(c), false, S(c, "nome"));
            foreach (var n in EsforcosTela(t))
            {
                string un = n.StartsWith("M") || n == "T" ? "kN·m" : "kN";
                double v = D(Esf, n);
                var cx = CaixaNum(double.IsNaN(v) ? "" : Num.Fmt(v, Math.Abs(v - Math.Round(v)) < 1e-9 ? 0 : 2), un, 176, x => { Esf[n] = x; membroSel = ""; Mudou(); }, AjudaEsforco(n));
                AddCelula(gp, RotuloEsforco(n), cx, false);
            }
            p1.Children.Add(gp);
            sp.Children.Add(Cartao("Passo 1 · Barras e esforços de cálculo", p1));

            // ② parâmetros
            var p2 = new StackPanel();
            var ordem = new List<string> { "Tubos", "Perfil", "Tubo", "Chapa", "Parafusos", "Soldas", "Enrijecedores", "Recortes", "Folgas" };
            var secoes = campos.Where(c => Secao(c) != "barras").GroupBy(Secao).OrderBy(g => g.Key.StartsWith("Barra") ? 10 + g.Key[g.Key.Length - 1] : ordem.IndexOf(g.Key) < 0 ? 99 : ordem.IndexOf(g.Key)).ToList();
            foreach (var s in secoes)
            {
                bool aberto = abertos.Contains(s.Key) || s.Any(c => realces.Contains(S(c, "nome")));
                string nomeS = s.Key;
                var cab = new DockPanel { Margin = new Thickness(0, p2.Children.Count == 0 ? 0 : 8, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var seta = Tx(aberto ? "▾" : "▸", 11, TemaPlant.BTexto3); seta.Width = 14;
                cab.Children.Add(seta);
                cab.Children.Add(Tx(nomeS.ToUpperInvariant() + "  ·  " + s.Count(), 10.5, TemaPlant.BTexto2, FontWeights.SemiBold));
                cab.MouseLeftButtonDown += (a, e) => { e.Handled = true; if (!abertos.Remove(nomeS)) abertos.Add(nomeS); MontarPassos(); };
                p2.Children.Add(cab);
                if (!aberto) continue;
                var g = GradeCampos();
                foreach (var c in s) AddCelula(g, Rotulo(c, S(c, "nome")), Controle(c), Largo(c), realces.Contains(S(c, "nome")), S(c, "nome"));
                p2.Children.Add(g);
            }
            var acoes = new StackPanel { Orientation = Orientation.Horizontal };
            var bs = Botao("Sugerir", "dimens", () => _ = Sugerir(), Bool(t, "sugerir") ? "Pré-dimensionamento do motor a partir dos perfis e dos esforços (sugerir do tipo)." : "Este tipo não tem pré-dimensionamento no motor: use Otimizar.", 24);
            bs.IsEnabled = Bool(t, "sugerir") && !ocupado; bs.Opacity = bs.IsEnabled ? 1 : 0.45;
            var bo = Botao("Otimizar", "calc", () => _ = Otimizar(), "Busca simples: aumenta a quantidade e o diâmetro dos parafusos, a espessura da chapa ou a perna da solda até passar e depois reduz o que sobrar (mínimo de material).", 24);
            bo.Margin = new Thickness(6, 0, 0, 0); bo.IsEnabled = !ocupado; bo.Opacity = bo.IsEnabled ? 1 : 0.45;
            acoes.Children.Add(bs); acoes.Children.Add(bo);
            sp.Children.Add(Cartao("Passo 2 · Parâmetros", p2, acoes));
            hostPassos.Child = sp;
        }

        // ---- editor por componente: clique na peça -> seção aberta, campo rolado à vista e destacado 1,5 s
        // tag = campo que o motor pôs na peça (contrato `campo`), ou candidatos "a|b" (perfis); vale o 1º que o padrão tem
        string CampoDoTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tipo == null) return null;
            var nomes = Campos(Tipo()).Select(c => S(c, "nome")).ToHashSet();
            return tag.Split('|').FirstOrDefault(nomes.Contains);
        }

        public void IrParaTag(string tag)
        {
            string campo = CampoDoTag(tag);
            if (campo != null) { IrParaCampo(campo); return; }
            Avisar("Esta peça não tem um campo para editar neste padrão.");
        }

        // faixa âmbar no topo do passo 2 (some sozinha em 5 s ou ao editar), além da barra de status
        string aviso; readonly DispatcherTimer tAviso = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        void Avisar(string msg)
        {
            aviso = msg; status = msg; MontarPassos(); MontarStatus();
            tAviso.Stop(); tAviso.Tick -= FimAviso; tAviso.Tick += FimAviso; tAviso.Start();
        }
        void FimAviso(object s, EventArgs e) { tAviso.Stop(); if (aviso == null) return; aviso = null; MontarPassos(); }

        Border celDestaque; string campoDestaque;
        public string CampoDoDestaque => campoDestaque;

        public void IrParaCampo(string campo)
        {
            var c = Campos(Tipo()).FirstOrDefault(x => S(x, "nome") == campo);
            if (c == null) return;
            string sec = Secao(c);
            if (sec != "barras" && abertos.Add(sec)) MontarPassos();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!celulas.TryGetValue(campo, out var el) || !(el is Border bd)) return;
                el.BringIntoView();
                if (celDestaque != null && celDestaque != bd) { celDestaque.BeginAnimation(Border.OpacityProperty, null); celDestaque.Background = Brushes.Transparent; celDestaque.BorderBrush = Brushes.Transparent; }
                celDestaque = bd; campoDestaque = campo;
                var ac = TemaPlant.Acento;
                var fundo = new SolidColorBrush(Color.FromArgb(120, ac.R, ac.G, ac.B)); var borda = new SolidColorBrush(ac);
                bd.Background = fundo; bd.BorderBrush = borda;
                if (!Capturas.Ativo)   // 1,5 s: cheio por 0,9 s e some em 0,6 s (nas capturas fica cheio, para a imagem mostrar)
                {
                    ColorAnimationUsingKeyFrames Anim(Color de) => new ColorAnimationUsingKeyFrames
                    {
                        KeyFrames = { new LinearColorKeyFrame(de, TimeSpan.Zero), new LinearColorKeyFrame(de, TimeSpan.FromMilliseconds(900)),
                                      new LinearColorKeyFrame(Color.FromArgb(0, de.R, de.G, de.B), TimeSpan.FromMilliseconds(1500)) }
                    };
                    fundo.BeginAnimation(SolidColorBrush.ColorProperty, Anim(fundo.Color)); borda.BeginAnimation(SolidColorBrush.ColorProperty, Anim(borda.Color));
                }
                tDestaque.Stop(); if (!Capturas.Ativo) tDestaque.Start();   // capturas: o destaque fica (a imagem o mostra)
            }));
        }

        // campo(s) onde o otimizador parou: estruturado (campo_limitante / campos_limite do motor), sem ler texto
        string CampoLimitante(JsonNode r)
        {
            var nomes = Campos(Tipo()).Select(c => S(c, "nome")).ToHashSet();
            string c1 = S(r, "campo_limitante");
            return nomes.Contains(c1) ? c1 : Lista(r["campos_limite"]).Select(x => S(x, "campo")).FirstOrDefault(nomes.Contains);
        }

        FrameworkElement FaixaSemSolucao()
        {
            var f = new StackPanel();
            f.Children.Add(Tx("✕ Sem solução — nada foi alterado", 12.5, TemaPlant.BErro, FontWeights.SemiBold));
            string ver = S(semSolucao, "verificacao_limitante");
            double tx = semSolucao["resultado"] != null ? TaxaDe(semSolucao["resultado"]) : double.NaN;
            var campos = Campos(Tipo()).ToList();
            JsonNode Cp(string n) => campos.FirstOrDefault(x => S(x, "nome") == n);
            var lims = Lista(semSolucao["campos_limite"]).Take(2).Select(x =>
            {
                string n = S(x, "campo"); var cc = Cp(n);
                return S(x, "tipo") == "faixa_vazia" ? Rotulo(cc, n) + ": faixa vazia (mínimo > máximo)" : Rotulo(cc, n) + (x["valor"] != null ? " = " + Valor(cc, x["valor"]) : "");
            }).ToList();
            string txt = (ver != "" ? "Governa: " + (ver.Length > 46 ? ver.Substring(0, 45) + "…" : ver) + (double.IsNaN(tx) ? "" : " · " + Pct(tx)) + "." : S(semSolucao, "motivo")) + (lims.Count > 0 ? "\nLimite atingido: " + string.Join("; ", lims) + "." : "");
            var m = Tx(txt, 11.5, TemaPlant.BTexto1, null, true);
            m.Margin = new Thickness(0, 4, 0, 0); f.Children.Add(m);
            if (campoLimitante != null)
            {
                var l = Link("Ir ao campo: " + Rotulo(Cp(campoLimitante), campoLimitante), () => IrParaCampo(campoLimitante), "Abre a seção e destaca o campo que travou a busca.", 11.5);
                l.Margin = new Thickness(0, 6, 0, 0); f.Children.Add(l);
            }
            return new Border { Background = TemaPlant.BErroFundo, BorderBrush = TemaPlant.BErro, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 8, 10, 9), Margin = new Thickness(0, 0, 0, 10), Child = f };
        }

        FrameworkElement FaixaAviso() =>
            new Border { Background = TemaPlant.BAlertaFundo, BorderBrush = TemaPlant.BAlertaBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 10),
                         Child = Tx("! " + aviso, 11.5, TemaPlant.BAlerta, FontWeights.SemiBold, true) };

        static Grid GradeCampos()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        static FrameworkElement Celula(string rotulo, FrameworkElement ctl, bool realce)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            var l = Tx(rotulo, 11, realce ? TemaPlant.BRealce : TemaPlant.BTexto2, realce ? FontWeights.SemiBold : FontWeights.Normal);
            l.TextTrimming = TextTrimming.CharacterEllipsis; l.Margin = new Thickness(1, 0, 0, 3);
            Dica(l, rotulo);
            sp.Children.Add(l);
            ctl.HorizontalAlignment = HorizontalAlignment.Left;
            if (realce && ctl is Border bd) { bd.BorderBrush = TemaPlant.BAcento; bd.BorderThickness = new Thickness(1.5); }
            sp.Children.Add(ctl);
            return sp;
        }

        void AddCelula(Grid g, string rotulo, FrameworkElement ctl, bool largo, bool realce = false, string nome = null)
        {
            int n = g.Children.Count == 0 ? 0 : g.Children.Cast<UIElement>().Max(c => Grid.GetRow(c) * 2 + (Grid.GetColumnSpan(c) > 1 ? 2 : Grid.GetColumn(c) == 2 ? 2 : 1));
            int lin = (n + (largo && n % 2 == 1 ? 1 : 0)) / 2, col = largo ? 0 : n % 2 == 0 ? 0 : 2;
            if (largo && n % 2 == 1) lin = (n + 1) / 2;
            while (g.RowDefinitions.Count <= lin) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var c = Celula(rotulo, ctl, realce);
            FrameworkElement caixa = c;
            if (nome != null) { caixa = new Border { Child = c, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(-2) }; celulas[nome] = caixa; }
            c = caixa;
            Grid.SetRow(c, lin); Grid.SetColumn(c, col);
            if (largo) Grid.SetColumnSpan(c, 3);
            g.Children.Add(c);
        }

        static readonly Dictionary<string, string> RotEsf = new Dictionary<string, string>
        { ["N"] = "Fx  normal N (tração +)", ["V"] = "Fz  cortante na alma (vertical)", ["Vz"] = "Fy  cortante fora do plano", ["M"] = "My  momento eixo forte", ["Mz"] = "Mz  momento eixo fraco",
          ["T"] = "Mx  torção", ["Ft"] = "Ft  tração no parafuso", ["Fv"] = "Fv  cortante no parafuso", ["F"] = "F  força no filete" };
        static string RotuloEsforco(string n) => RotEsf.TryGetValue(n, out var r) ? r : n.StartsWith("N") ? n + "  (barra " + n.Substring(1) + ")" : n;
        static string AjudaEsforco(string n) => "Esforço de cálculo (ELU, já majorado) na ponta da barra que chega à ligação. " + (n.StartsWith("M") || n == "T" ? "kN·m." : "kN.");

        static string Rotulo(JsonNode c, string nome)
        {
            string r = S(c, "rotulo");
            return r == "" || r == nome ? nome.Replace('_', ' ') : r;
        }

        static string Ajuda(JsonNode c)
        {
            string a = S(c, "ajuda"), d = S(c, "descricao"), r = S(c, "rotulo");
            string txt = a != "" ? a : d != r ? d : "";
            double mn = D(c, "min"), mx = D(c, "max");
            string un = S(c, "unidade") == "m" ? "mm" : S(c, "unidade");
            double k = S(c, "unidade") == "m" ? 1000 : 1;
            if (!double.IsNaN(mn) && !double.IsNaN(mx)) txt += (txt != "" ? "\n" : "") + "Faixa: " + Num.Fmt(mn * k, 2) + " a " + Num.Fmt(mx * k, 2) + (un == "-" || un == "" ? "" : " " + un);
            return (r != "" ? r + "\n" : "") + txt;
        }

        string Secao(JsonNode c)
        {
            string n = S(c, "nome"), r = Norm(S(c, "rotulo") + " " + S(c, "ajuda"));
            var suf = System.Text.RegularExpressions.Regex.Match(n, @"_(\d)$");
            if (suf.Success) return "Barra " + suf.Groups[1].Value;
            if (n.StartsWith("perfil_") || n == "apoio") return "barras";
            if (S(c, "secao") != "") return S(c, "secao");   // seção dada pelo motor (tubos: "Tubos", "Perfil", "Tubo")
            if (n.StartsWith("recorte") || n == "desnivel") return "Recortes";
            if (n == "estendida") return "Chapa";   // posição (centralizada / alinhada em cima / embaixo) junto da chapa, não em opções
            if (n.StartsWith("aco_")) return "Materiais e opções";
            if (n.Contains("enrij") || n == "t_enr_pilar" || n == "t_reforco") return "Enrijecedores";
            if (n.StartsWith("folga") || new[] { "x_face", "x_max", "abertura_y", "sobra", "excentricidade_apoio", "recuo" }.Contains(n)) return "Folgas";
            if (n == "eletrodo" || n.StartsWith("b_solda") || n == "perna" || n.Contains("solda") || n.Contains("soldada") || r.Contains("solda") || r.Contains("filete") || tipo == "solda_filete") return "Soldas";
            if (c["opcoes"] is JsonArray || c["padrao"] is JsonValue pv && pv.TryGetValue(out bool _)) return "Materiais e opções";
            if (new[] { "d", "d_mm", "n", "s", "g", "e_v", "e_h", "eh", "e1", "n_linhas", "n_por_linha", "n_colunas", "g_colunas", "planos_corte", "pega", "t_enchimento", "lf", "gabarito", "dist_topo_furo", "g_viga", "g_apoio" }.Contains(n)
                || r.Contains("parafus") || r.Contains("furo") || r.Contains("diametro") || r.Contains("espacamento") || r.Contains("gabarito")) return "Parafusos";
            return "Chapa";
        }

        static bool Largo(JsonNode c)
        {
            string n = S(c, "nome");
            if ((n == "d" || n == "d_mm") && S(c, "unidade") == "mm" || n == "estendida") return true;
            if (c["opcoes"] is JsonArray op && op.Count <= 3 && op.Sum(x => x.ToString().Length) <= 34 && op.Count > 0) return op.Sum(x => x.ToString().Length) > 16;
            return n.StartsWith("perfil_") && false;
        }

        // valor para a tela (m → mm)
        static string Valor(JsonNode c, JsonNode v)
        {
            if (v == null) return "—";
            if (v is JsonValue jv && jv.TryGetValue(out bool b)) return b ? "sim" : "não";
            double x = D(v);
            if (double.IsNaN(x)) return v.ToString().Replace('_', ' ');
            string un = S(c, "unidade");
            if (un == "m") x *= 1000;
            return Num.Fmt(x, Math.Abs(x - Math.Round(x)) < 1e-6 ? 0 : Math.Abs(x * 10 - Math.Round(x * 10)) < 1e-6 ? 1 : 2) + (un == "m" ? " mm" : un == "-" || un == "" ? "" : " " + un);
        }

        FrameworkElement Controle(JsonNode c)
        {
            string nome = S(c, "nome"), un = S(c, "unidade");
            var atual = Dados[nome];
            string dica = Ajuda(c);
            bool metro = un == "m";
            double k = metro ? 1000 : 1;
            double mn = D(c, "min"), mx = D(c, "max");
            bool Dentro(double mm) => (double.IsNaN(mn) || mm / k >= mn - 1e-9) && (double.IsNaN(mx) || mm / k <= mx + 1e-9);
            void Gravar(JsonNode v) { Dados[nome] = v; Mudou(nome); }

            // posição da chapa de topo estendida: nomes do motor (superior/ambas/inferior) em termos de alinhamento
            if (nome == "estendida" && c["opcoes"] is JsonArray ope)
            {
                string sel = atual?.ToString() ?? "superior";
                // W9: emenda flangeada também tem "nao" (só parafusos entre as mesas = ajustada)
                var nomes = new[] { ("nao", "Ajustada"), ("ambas", "Centralizada"), ("inferior", "Alinhada em cima"), ("superior", "Alinhada embaixo") };
                return Segmentado(nomes.Where(x => ope.Any(o => o.ToString() == x.Item1)).Select(x => (x.Item2, x.Item1 == sel, (Action)(() => Gravar(x.Item1)), dica)).ToList(), 28, 11.5);
            }
            if (c["opcoes"] is JsonArray op)
            {
                var ops = op.Select(x => x.ToString()).ToList();
                string sel = atual?.ToString() ?? ops.FirstOrDefault();
                if (ops.Count <= 3 && ops.Sum(x => x.Length) <= 34)
                    return Segmentado(ops.Select(o => (o.Replace('_', ' '), o == sel, (Action)(() => Gravar(o)), dica)).ToList(), 28, 11.5);
                var cb = Combo(ops.Select(o => o.Replace('_', ' ')), sel?.Replace('_', ' '), x => Gravar(ops.First(o => o.Replace('_', ' ') == x)), 176, dica);
                return cb;
            }
            if (c["padrao"] is JsonValue pv && pv.TryGetValue(out bool _) || atual is JsonValue av && av.TryGetValue(out bool _))
            {
                bool v = Bool(Dados, nome);
                return Segmentado(new List<(string, bool, Action, string)> { ("Sim", v, () => Gravar(true), dica), ("Não", !v, () => Gravar(false), dica) }, 28, 11.5);
            }
            bool texto = c["padrao"] is JsonValue ps && ps.TryGetValue(out string _) || atual is JsonValue at && at.TryGetValue(out string _) || nome.StartsWith("perfil_");
            if (texto)
            {
                // W7-LIG: trocar a cantoneira refaz o pré-dimensionamento (gabarito, bordas e parafusos vêm do perfil)
                if (nome.StartsWith("perfil_")) return CaixaPerfil(nome, atual?.ToString() ?? "", dica, x => { Gravar(x); if ((nome == "perfil_cantoneira" || Bool(c, "resugerir")) && Bool(Tipo(), "sugerir")) _ = Sugerir(); }, c["perfis"] as JsonArray);
                var tb = new TextBox { Text = atual?.ToString() ?? "", Width = 176, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, BorderBrush = TemaPlant.BBorda, CaretBrush = TemaPlant.BTexto1, Padding = new Thickness(6, 0, 6, 0) };
                string ini = tb.Text;
                void Ok() { if (tb.Text != ini) { ini = tb.Text; Gravar(tb.Text.Trim()); } }
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Ok(); } };
                tb.LostKeyboardFocus += (s, e) => Ok();
                return Dica(tb, dica);
            }
            double val = D(atual);
            double mm = double.IsNaN(val) ? double.NaN : val * k;
            JsonNode Num_(double x) => metro ? JsonValue.Create(Math.Round(x / 1000, 6)) : JsonValue.Create(x);
            bool inteiro = (un == "-" || un == "") && (Bool(c, "inteiro") || c["padrao"] is JsonValue pi && pi.TryGetValue(out int _) && pi.ToJsonString().IndexOf('.') < 0);
            // seletor de diâmetro (parafusos usuais)
            if ((nome == "d" || nome == "d_mm") && (un == "mm" || un == ""))
            {
                var itens = DiamMm.Where(Dentro).Select(x => (NomeDiam(x), Math.Abs(x - mm) < 0.05, (Action)(() => Gravar(Num_(x))), dica)).ToList();
                if (!double.IsNaN(mm) && !DiamMm.Any(x => Math.Abs(x - mm) < 0.05)) itens.Add((Num.Fmt(mm, 1), true, (Action)null, dica));
                return Segmentado(itens, 28, 11.5);
            }
            double[] lista = null; string su = metro ? "mm" : un == "-" ? "" : un;
            if (new[] { "t", "t_chapa", "t_gusset", "t_placa", "esp", "espessura", "t_assento", "t_enrijecedor" }.Contains(nome)) lista = EspMm;
            else if (nome.StartsWith("b_solda") || nome == "perna" || (tipo == "solda_filete" && nome == "b")) lista = PernaMm;
            if (lista != null)
            {
                var ok = lista.Where(Dentro).ToArray();
                double? Prox(bool sobe) { var q = sobe ? ok.Where(x => x > mm + 1e-6).Cast<double?>().FirstOrDefault() : ok.Where(x => x < mm - 1e-6).Cast<double?>().LastOrDefault(); return double.IsNaN(mm) ? ok.FirstOrDefault() : q; }
                var a = Prox(false); var b2 = Prox(true);
                return Stepper(double.IsNaN(mm) ? "—" : Num.Fmt(mm, Math.Abs(mm - Math.Round(mm)) < 1e-6 ? 0 : 1) + " " + su,
                    a != null ? () => Gravar(Num_(a.Value)) : (Action)null, b2 != null ? () => Gravar(Num_(b2.Value)) : (Action)null, 96, null, dica + "\nValores comerciais: − e + trocam pelo vizinho da lista.");
            }
            if (inteiro)
            {
                int n = double.IsNaN(val) ? (int)(double.IsNaN(mn) ? 1 : mn) : (int)Math.Round(val);
                bool podeMenos = double.IsNaN(mn) || n - 1 >= mn, podeMais = double.IsNaN(mx) || n + 1 <= mx;
                return Stepper(n.ToString(), podeMenos ? () => Gravar(JsonValue.Create(n - 1)) : (Action)null, podeMais ? () => Gravar(JsonValue.Create(n + 1)) : (Action)null, 96, null, dica);
            }
            string txt = double.IsNaN(mm) ? "" : Num.Fmt(mm, Math.Abs(mm - Math.Round(mm)) < 1e-6 ? 0 : 2);
            var cx = CaixaNum(txt, su, 176, x => Gravar(Num_(x)), dica);
            if (double.IsNaN(mm)) cx.BorderBrush = TemaPlant.BAlertaBorda;
            return cx;
        }

        static string NomeDiam(double mm)
        {
            switch (mm)
            {
                case 15.875: return "⅝\"";
                case 19.05: return "¾\"";
                case 22.225: return "⅞\"";
                case 25.4: return "1\"";
                default: return "M" + Num.Fmt(mm, 0);
            }
        }

        // mesmo seletor do inspetor (SeletorPerfil: miniatura, busca, lista); nas rígidas e emendas só I/H (o motor recusa U, G5)
        static readonly HashSet<string> FamiliasI = new HashSet<string> { "W", "HP", "IPE", "HEA", "HEB" };
        bool SoPerfilI => S(Tipo(), "familia") is "rigida" or "emenda";
        IEnumerable<string> PerfisDoTipo() => Lista(catalogo).Where(x => !SoPerfilI || FamiliasI.Contains(S(x, "familia"))).Select(x => S(x, "nome")).Where(x => x != "");
        // W7-LIG: campo de cantoneira (perfil_cantoneira) só lista perfis L do catálogo
        static readonly HashSet<string> FamiliasL = new HashSet<string> { "Cantoneira L", "L desigual" };
        IEnumerable<string> PerfisDoCampo(string campo) => campo.Contains("cantoneira") ? Lista(catalogo).Where(x => FamiliasL.Contains(S(x, "familia"))).Select(x => S(x, "nome")).Where(x => x != "") : PerfisDoTipo();

        // `so` = nomes aceitos pelo campo (o motor manda em `perfis`; tubo–tubo: só tubos circulares)
        FrameworkElement CaixaPerfil(string nome, string atual, string dica, Action<string> gravar, JsonArray so = null)
        {
            SeletorPerfil sel = null;
            Func<IEnumerable<string>> fonte = so != null ? () => so.Select(x => x?.ToString() ?? "").Where(x => x != "") : () => PerfisDoCampo(nome);
            sel = new SeletorPerfil(fonte, () => atual, s => { if (s != atual) { atual = s; sel.Mostrar(s); gravar(s); } })
            { AceitaDigitado = false, RotuloContagem = so != null ? " perfis aceitos" : nome.Contains("cantoneira") ? " cantoneiras no catálogo" : SoPerfilI ? " perfis I/H (ligação rígida)" : " perfis no catálogo", Width = 176, Tag = nome };
            sel.Mostrar(atual);
            // catálogo vem do motor (assíncrono): carrega já; a abertura do seletor usa o que tiver chegado
            if (!Capturas.Ativo) _ = CarregarCatalogo();
            // 1º clique antes do catálogo chegar: espera e abre já com a lista (senão abria vazia)
            sel.PreviewMouseLeftButtonUp += async (s, e) => { if (Lista(catalogo).Any()) return; e.Handled = true; await CarregarCatalogo(); sel.Abrir(); };
            return Dica(sel, dica + "\nPerfil do catálogo do motor (digite para filtrar).");
        }

        // capturas (projetista): abre o combo do campo como o usuário, espera o catálogo e escolhe `novo`; devolve (itens, valor gravado)
        internal async Task<(int itens, string gravado)> TrocarPerfil(string campo, string novo)
        {
            IEnumerable<DependencyObject> Filhos(DependencyObject o) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) { var f = VisualTreeHelper.GetChild(o, i); yield return f; foreach (var x in Filhos(f)) yield return x; } }
            var sel = Filhos(this).OfType<SeletorPerfil>().FirstOrDefault(c => (c.Tag as string) == campo) ?? throw new InvalidOperationException("sem seletor " + campo);
            await CarregarCatalogo();
            int n = PerfisDoCampo(campo).Count();
            sel.Preparar(novo); sel.EscolherPrimeiro();
            return (n, S(dados[tipo], campo));
        }

        // capturas: clique REAL do mouse no seletor (janela na tela) e confere se a lista continua aberta depois de parar o mouse sobre ela
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
        internal bool CliqueRealNoSeletor(string campo, Action esperar)
        {
            IEnumerable<DependencyObject> Filhos(DependencyObject o) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) { var f = VisualTreeHelper.GetChild(o, i); yield return f; foreach (var x in Filhos(f)) yield return x; } }
            var sel = Filhos(this).OfType<SeletorPerfil>().First(c => (c.Tag as string) == campo);
            sel.BringIntoView(); esperar();
            var p = sel.PointToScreen(new Point(sel.ActualWidth / 2, sel.ActualHeight / 2));
            SetCursorPos((int)p.X, (int)p.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            for (int i = 0; i < 6; i++) { esperar(); System.Threading.Thread.Sleep(100); }
            SetCursorPos((int)p.X, (int)p.Y + 120);   // mouse parado sobre a lista: a dica antiga aparecia aqui e fechava o popup
            for (int i = 0; i < 15; i++) { esperar(); System.Threading.Thread.Sleep(100); }
            return sel.Aberto;
        }

        Task cargaCatalogo;
        Task CarregarCatalogo()   // uma carga para todos os combos; se o motor ainda não respondeu (lista vazia), a próxima abertura tenta de novo
        {
            if (cargaCatalogo != null && !(cargaCatalogo.IsCompleted && Lista(catalogo).Count() == 0)) return cargaCatalogo;
            return cargaCatalogo = Carregar();
            async Task Carregar() { try { catalogo = await Api("GET", "/api/catalogo", null) ?? new JsonArray(); } catch (Exception) { catalogo ??= new JsonArray(); } }
        }

        // ---------------------------------------------------------------- desenho
        // os hosts são reaproveitados entre montagens: solta do pai antigo antes de pôr no novo
        static T Soltar<T>(T e) where T : FrameworkElement
        {
            switch (e.Parent)
            {
                case Panel p: p.Children.Remove(e); break;
                case ContentControl c: c.Content = null; break;
                case Decorator d: d.Child = null; break;
            }
            return e;
        }

        void MontarDesenho()
        {
            var card = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(10), ClipToBounds = true };
            var dp = new DockPanel();
            var topo = new DockPanel { Margin = new Thickness(10, 8, 12, 8) };
            var seg = Segmentado(new[] { "Vistas", "Elevação", "Planta", "Lateral", "3D" }.Select((s, i) => (s, vistaAtiva == i, (Action)(() => { vistaAtiva = i; MontarDesenho(); }),
                i == 0 ? "Elevação, planta, lateral e 3D juntas" : i == 4 ? "3D girável (arraste)" : "Só a " + s.ToLowerInvariant() + ", maior")).ToList());
            DockPanel.SetDock(seg, Dock.Left); topo.Children.Add(seg);
            topo.Children.Add(Soltar(hostStatusDes));
            DockPanel.SetDock(topo, Dock.Top); dp.Children.Add(topo);
            var leg = Legenda(); DockPanel.SetDock(leg, Dock.Bottom); dp.Children.Add(leg);
            areaVistas = new Grid { Background = TemaPlant.BBordaSutil };
            foreach (var v in vistas) Soltar(v);
            if (vistaAtiva == 0)
            {
                areaVistas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
                areaVistas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                areaVistas.RowDefinitions.Add(new RowDefinition()); areaVistas.RowDefinitions.Add(new RowDefinition());
                for (int i = 0; i < 4; i++)
                {
                    vistas[i].Margin = new Thickness(i % 2 == 1 ? 1 : 0, i >= 2 ? 1 : 0, 0, 0);
                    Grid.SetColumn(vistas[i], i % 2); Grid.SetRow(vistas[i], i / 2);
                    areaVistas.Children.Add(vistas[i]);
                }
            }
            else { vistas[vistaAtiva - 1].Margin = new Thickness(0); Grid.SetColumn(vistas[vistaAtiva - 1], 0); Grid.SetRow(vistas[vistaAtiva - 1], 0); areaVistas.Children.Add(vistas[vistaAtiva - 1]); }
            areaVistas.Margin = new Thickness(0, 0, 0, 0);
            dp.Children.Add(new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 1), Child = areaVistas });
            card.Child = dp;
            hostDesenho.Child = card;
            // geometria atual com as falhas
            var falhas = new HashSet<string>(Lista(resultado?["verificacoes"]).Where(v => !Passa(v)).Select(v => GrupoDe(S(v, "nome"))));
            var g = resultado?["geometria"] != null ? GeoLig.De(resultado["geometria"], falhas) : null;
            foreach (var v in vistas) if (g != null && (v.Geo == null || !ReferenceEquals(ultimoRes, resultado))) v.Mostrar(g);
            ultimoRes = resultado;
            vistaEsmaecida = erro != null || resultadoVelho;
            // W7-UPN: com erro do motor o desenho antigo (outros perfis) some; o motivo fica no cabeçalho e no passo 3
            areaVistas.Visibility = erro != null ? Visibility.Hidden : Visibility.Visible;
        }
        JsonNode ultimoRes; bool vistaEsmaecida;

        FrameworkElement Legenda()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 12, 7) };
            void L(string s, Brush cor, string txt) { var a = Tx(s, 12, cor, FontWeights.Bold); a.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(a); var b = Tx(txt, 11, TemaPlant.BTexto3); b.Margin = new Thickness(0, 0, 16, 0); sp.Children.Add(b); }
            L("⊕", TemaPlant.BRealce, "parafuso"); L("▲", TemaPlant.BTexto1, "solda (perna em mm)"); L("▨", TemaPlant.BPlacaContorno, "chapa de topo"); L("▭", TemaPlant.BBarra, "perfil");
            L("■", TemaPlant.BErro, "não passa"); L("↔", TemaPlant.BTexto2, "cotas em mm");
            return sp;
        }

        void MontarStatus()
        {
            if (tipo == null) return;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (calculando || resultadoVelho)
            {
                var br = Anim.Brilho(70, 3); br.VerticalAlignment = VerticalAlignment.Center; br.Margin = new Thickness(0, 0, 8, 0);
                sp.Children.Add(br);
                sp.Children.Add(Tx("recalculando…", 11, TemaPlant.BTexto3));
            }
            else if (resultado != null)
                sp.Children.Add(Tx("ao vivo · " + Lista(resultado["verificacoes"]).Count() + " verificações · arraste o 3D para girar", 11, TemaPlant.BTexto3));
            hostStatusDes.Child = sp;
        }

        // ---------------------------------------------------------------- ③ resultados
        void MontarResultados()
        {
            var dp = new DockPanel();
            var tit = TituloSecao("Passo 3 · Verificações", null, 0); DockPanel.SetDock(tit, Dock.Top); dp.Children.Add(tit);
            if (resultado == null || erro != null)   // W7-UPN: o resultado antigo é de outros dados (ex.: rígida que recusou o U)
            {
                dp.Children.Add(Vazio("verif", erro != null ? "Corrija os dados" : "Calculando…", erro ?? "O motor verifica a ligação a cada mudança."));
                hostRes.Child = dp; return;
            }
            var vs = Lista(resultado["verificacoes"]).OrderByDescending(v => double.IsNaN(D(v, "taxa")) ? 0 : D(v, "taxa")).ToList();
            double taxa = TaxaDe(resultado); bool ok = Bool(resultado, "ok");
            var nv = NivelAp(taxa, ok); var c = Cores(nv);
            var pior = vs.FirstOrDefault();
            // veredito
            var ban = new Border { Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 10) };
            var bg = new Grid();
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bt = new StackPanel();
            bt.Children.Add(Tx(nv == Nivel.Erro ? "✕  NÃO PASSA" : nv == Nivel.Alerta ? "!  PASSA NO LIMITE" : "✓  PASSA", 13, c.fg, FontWeights.Bold));
            var gov = Tx(pior == null ? "sem verificações" : "governa: " + S(pior, "nome"), 11.5, TemaPlant.BTexto1, null, true); gov.Margin = new Thickness(0, 3, 0, 0); bt.Children.Add(gov);
            if (pior != null) { var it = Tx(Item(S(pior, "item")), 11, TemaPlant.BTexto2); it.Margin = new Thickness(0, 1, 0, 0); bt.Children.Add(it); }
            bg.Children.Add(bt);
            var pc = Tx(Pct(taxa), 24, c.fg, FontWeights.Bold); pc.VerticalAlignment = VerticalAlignment.Top; Typography.SetNumeralAlignment(pc, FontNumeralAlignment.Tabular);
            Grid.SetColumn(pc, 1); bg.Children.Add(pc);
            ban.Child = bg;
            DockPanel.SetDock(ban, Dock.Top); dp.Children.Add(ban);
            if (grupo != null)
            {
                var seg = Segmentado(new List<(string, bool, Action, string)>
                {
                    ("Verificações · " + vs.Count, abaRes == 0, () => { abaRes = 0; MontarResultados(); }, null),
                    ("Grupo · " + Pl.N(Lista(dimGrupo?["membros"]).Count(), "ligação", "ligações"), abaRes == 1, () => { abaRes = 1; MontarResultados(); }, "Taxa de cada ligação do grupo com o projeto aplicado")
                });
                seg.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(seg, Dock.Top); dp.Children.Add(seg);
            }
            var lista = new StackPanel();
            if (abaRes == 1 && grupo != null) ListaGrupo(lista);
            else
            {
                if (filtroComp != null)
                {
                    var f = Link("✕  só " + NomeGrupo[filtroComp].ToLowerInvariant() + " — mostrar todas", () => { filtroComp = null; MontarCabecalho(); MontarResultados(); }, null, 11.5);
                    f.Margin = new Thickness(0, 0, 0, 6); lista.Children.Add(f);
                }
                foreach (var v in vs.Where(v => filtroComp == null || GrupoDe(S(v, "nome")) == filtroComp)) lista.Children.Add(LinhaVerif(v, v == pior));
                var av = Lista(resultado["avisos"]).ToList();
                if (av.Count > 0)
                {
                    lista.Children.Add(TituloSecao("Avisos · " + av.Count, null, 12));
                    foreach (var a in av) { var t = Tx("⚠  " + a, 11.5, TemaPlant.BAlertaClaro, null, true); t.Margin = new Thickness(0, 0, 0, 5); lista.Children.Add(t); }
                }
            }
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = lista });
            hostRes.Child = dp;
        }

        FrameworkElement LinhaVerif(JsonNode v, bool governa)
        {
            double t = D(v, "taxa"); bool ok = Passa(v);
            string un = S(v, "unidade"); double k = un == "m" ? 1000 : 1; if (un == "m") un = "mm";
            double s = D(v, "solicitante") * k, r = D(v, "resistente") * k;
            string nome = S(v, "nome"), nn = Norm(nome);
            string val = nn.Contains("minim") || nn.Contains(" min") ? "exigido " + F(s, 1) + "  ≤  " + F(r, 1, un)
                       : nn.Contains("maxim") || nn.Contains(" max") ? F(s, 1) + "  ≤  máx " + F(r, 1, un)
                       : un == "" || un == "-" ? F(s, 3) + "  ≤  " + F(r, 2) : "Sd " + F(s, 1) + "  ≤  Rd " + F(r, 1, un);
            var g = new Grid { Margin = new Thickness(governa ? 8 : 10, 6, 8, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tn = Tx(nome, 12, TemaPlant.BTexto1, governa ? FontWeights.SemiBold : FontWeights.Normal, true); g.Children.Add(tn);
            var tp = Num1(double.IsInfinity(t) ? "∞" : Pct(t), Cores(NivelAp(t, ok)).fg, true); tp.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(tp, 1); g.Children.Add(tp);
            var tv = Tx(val + "   ·   " + Item(S(v, "item")), 11, TemaPlant.BTexto3); tv.TextTrimming = TextTrimming.CharacterEllipsis; tv.Margin = new Thickness(0, 2, 0, 4);
            Typography.SetNumeralAlignment(tv, FontNumeralAlignment.Tabular);
            Grid.SetRow(tv, 1); Grid.SetColumnSpan(tv, 2); g.Children.Add(Dica(tv, val + "\n" + Item(S(v, "item"))));
            var bc = BarraCap(double.IsInfinity(t) ? 1 : t, ok, 5); Grid.SetRow(bc, 2); Grid.SetColumnSpan(bc, 2); g.Children.Add(bc);
            return new Border
            {
                Child = g, Background = governa ? TemaPlant.BSup2 : Brushes.Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 2),
                BorderBrush = governa ? Cores(NivelAp(t, ok)).fg : TemaPlant.BBordaSutil, BorderThickness = governa ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 0, 1)
            };
        }

        void ListaGrupo(StackPanel sp)
        {
            var d = aplicado ?? dimGrupo;
            if (aplicado == null)
            {
                var t = Tx("Ainda não aplicado: a lista mostra o projeto anterior do grupo. “Aplicar ao grupo” grava este detalhe e verifica cada ligação em todas as combinações ELU.", 11.5, TemaPlant.BTexto3, null, true);
                t.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(t);
            }
            else
            {
                double tx = D(d, "taxa_max"); bool ok = Bool(d, "ok");
                var gov = d["governante"];
                var c = Cores(NivelAp(tx, ok));
                var b = new Border { Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 8) };
                var s2 = new StackPanel();
                s2.Children.Add(Tx((ok ? "✓ Todas passam" : "✕ Há ligações que não passam") + " · pior " + Pct(tx), 12.5, c.fg, FontWeights.SemiBold));
                s2.Children.Add(Tx("Governa " + S(gov, "barra") + " (" + S(gov, "ponta") + ") no nó " + S(gov, "no") + " · " + S(gov, "combinacao"), 11, TemaPlant.BTexto2));
                b.Child = s2; sp.Children.Add(b);
            }
            string govK = d?["governante"] != null ? Chave(d["governante"]) : null;
            var cols = new[] { new Col("Nó", 84), new Col("Barra · ponta", 96), new Col("Comb.", 0), new Col("Taxa", 128) };
            var linhas = new List<UIElement[]>();
            var ms = Lista(d?["membros"]).OrderByDescending(m => double.IsNaN(D(m, "taxa")) ? -1 : D(m, "taxa")).ToList();
            foreach (var m in ms)
            {
                double t = D(m, "taxa"); bool ok = m["ok"] == null || Bool(m, "ok");
                bool g = Chave(m) == govK;
                linhas.Add(new UIElement[]
                {
                    Tx((g ? "◆ " : "") + S(m, "no"), 12, g ? TemaPlant.BAlerta : TemaPlant.BTexto1, g ? FontWeights.SemiBold : FontWeights.Normal),
                    Tx(S(m, "barra") + " · " + S(m, "ponta"), 12, TemaPlant.BTexto1), Tx(S(m, "erro") != "" ? "⚠ " + S(m, "erro") : S(m, "combinacao"), 11, TemaPlant.BTexto2),
                    double.IsNaN(t) ? (UIElement)Tx("—", 11.5, TemaPlant.BTextoMudo) : CelTaxa(t, ok, 60)
                });
            }
            sp.Children.Add(Grade(cols, linhas, -1, i => { membroSel = Chave(ms[i]); esforcos[tipo] = EsforcosDoMembro(ms[i]["esforcos"], Tipo()); abaRes = 0; Mudou(); }, null, null, -1, false,
                i => "Clique para projetar com os esforços desta ligação."));
        }

        // ---------------------------------------------------------------- rodapé
        void MontarRodape()
        {
            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (tipo != null)
            {
                var bib = Botao("Biblioteca", "tabela", () => { tipo = null; Montar(); _ = CarregarMiniaturas(); }, "Volta à galeria de padrões de ligação.");
                bib.Margin = new Thickness(0, 0, 8, 0); esq.Children.Add(bib);
                if (grupo != null)
                {
                    var mem = Botao("Gerar memorial", "relatorio", () => _ = GerarMemorial(), "Seção HTML do memorial: verificações com a expressão da norma, valores e taxa (abre no navegador para imprimir/PDF).");
                    mem.Margin = new Thickness(0, 0, 8, 0); esq.Children.Add(mem);
                }
            }
            var abrC = Botao(grupo != null ? "Importar .pcon" : "Abrir .pcon", "abrir", () => _ = AbrirConexao(), grupo != null ? "Importa uma conexão salva (.pcon) para a ligação do grupo " + S(grupo, "nome") + ": tipo, dados e esforços do arquivo preenchem este projeto; depois \"Aplicar ao grupo\" grava no modelo." : "Abre uma conexão salva (.pcon) nesta calculadora.");
            abrC.Margin = new Thickness(0, 0, 8, 0); esq.Children.Add(abrC);
            if (tipo != null)
            {
                var salC = Botao("Salvar .pcon", "salvar", SalvarConexao, "Salva tipo, dados e esforços desta ligação num arquivo .pcon (calculadora de conexão, sem modelo).");
                salC.Margin = new Thickness(0, 0, 8, 0); esq.Children.Add(salC);
            }
            if (ocupado) { var br = Anim.Brilho(80, 3); br.VerticalAlignment = VerticalAlignment.Center; br.Margin = new Thickness(4, 0, 10, 0); esq.Children.Add(br); }
            var st = Tx(status != "" ? status : tipo == null ? "Resultado do motor; exige conferência do engenheiro (ART)." : "Ligação isolada pela NBR 8800:2008. Resultado do motor; exige conferência do engenheiro (ART).",
                        11.5, status.StartsWith("Motor:") || status.StartsWith("Nenhum") ? TemaPlant.BAlertaClaro : status != "" ? TemaPlant.BTexto1 : TemaPlant.BTexto3);
            st.TextTrimming = TextTrimming.CharacterEllipsis; st.MaxWidth = 640;   // no StackPanel o corte só vale com largura máxima (texto longo alargava a janela)
            esq.Children.Add(Dica(st, st.Text));
            var bts = new List<Button> { B("Fechar", Close, false, "Fecha esta aba (Esc).") };
            if (tipo != null && grupo != null)
            {
                var ap = B("Aplicar ao grupo", () => _ = AplicarAoGrupo(), true, "Grava este projeto no grupo " + S(grupo, "nome") + " e verifica todas as ligações do grupo em todas as combinações ELU.");
                ap.IsEnabled = !ocupado && resultado != null; bts.Add(ap);
            }
            else if (tipo != null)
            {
                var mem = B("Gerar memorial", () => _ = GerarMemorial(), true, "Seção HTML do memorial: verificações com a expressão da norma, valores e taxa (abre no navegador).");
                mem.IsEnabled = !ocupado && resultado != null; bts.Add(mem);
            }
            hostRodape.Child = Rodape(esq, bts.ToArray());
        }
    }

    // ---------------------------------------------------------------- integração: faixa, Ctrl+K, gerenciador, placa
    public partial class MainWindow
    {
        // Ligações e bases › Projetar ligação (Ctrl+K "Projetar ligação"); tipo = abre direto no tipo
        internal async void AbrirProjetistaLigacao(string tipo = null, string conexao = null)   // conexao: .pcon a abrir na calculadora
        {
            var d = await ComandosAnalise.BuscarTipos(this);
            if (d == null) return;
            var j = new JanelaLigacaoProjeto(d, tipo);
            j.AbrirPlaca = jp => { MostrarLigacao(null); _ = AbrirPlacaEsp(null); };   // placa do pé de pilar (Legado3D.cs): nó selecionado ou pede o clique (na aba do modelo)
            MostrarLigacao(j);
            if (conexao != null) await j.AbrirConexao(conexao);
        }

        // clique numa chapa da Vista3D: abre o projetista do grupo daquela ligação, na seção e no campo da peça
        internal async void AbrirLigacaoDaPeca(Ligacao3D l, string tag)
        {
            var d = Esp; if (d == null || l == null) return;
            if (l.Proposta) { await AbrirGerenciadorNoGrupo(d, l.Grupo); return; }   // C3: peça de proposta → gerenciador no grupo (Aplicar troca / escolher padrão)
            try
            {
                var gs = await d.Ligacoes("GET", "/api/ligacoes/grupos");
                var g = Lista(gs).FirstOrDefault(x => l.Grupo != "" && S(x, "id") == l.Grupo);   // Ligacao3D.Grupo = id do motor (os ids No/Barra da vista não são os do motor)
                if (g == null) { MostrarToast("Esta chapa não pertence a nenhum grupo de ligações do modelo.", true); return; }
                var j = await CriarProjetistaGrupo(d, g, null);
                if (j == null) return;
                MostrarLigacao(j);
                if (j.TemTipo) j.IrParaTag(tag);
                else MostrarToast("O grupo " + S(g, "nome") + " ainda não tem projeto: escolha o padrão na biblioteca.", false);
            }
            catch (Exception ex) { MostrarToast("Ligação: " + ComandosAnalise.MensagemMotor(ex), true); }
        }

        internal async Task AbrirGerenciadorNoGrupo(DocumentoEspacial d, string grupo)
        {
            if (janLig == null || !janLig.IsVisible) { janLig = NovoGerenciadorLigacoes(d); await janLig.Carregar(false); janLig.Show(); }
            else janLig.Activate();
            if (!string.IsNullOrEmpty(grupo)) janLig.Escolher(grupo);
        }

        // gerenciador › Projetar: esforços do pior membro do grupo (dimensionar), projeto gravado no grupo ao aplicar
        internal async Task AbrirProjetistaGrupo(DocumentoEspacial d, JsonNode g, JanelaLigacoesGrupos ger)
        {
            var j = await CriarProjetistaGrupo(d, g, ger);
            if (j != null) { MostrarLigacao(j); Activate(); }   // a aba fica na janela principal (o gerenciador continua por cima)
        }

        internal async Task<JanelaLigacaoProjeto> CriarProjetistaGrupo(DocumentoEspacial d, JsonNode g, JanelaLigacoesGrupos ger)
        {
            var tipos = await ComandosAnalise.BuscarTipos(ger ?? (Window)this);
            if (tipos == null) return null;
            JsonNode dim = null;
            try
            {
                if (!d.Calculado) await CalcularEspacial();
                dim = await d.Ligacoes("POST", "/api/ligacoes/grupos/" + Uri.EscapeDataString(S(g, "id")) + "/dimensionar");
            }
            catch (Exception ex) { MostrarToast("Projetar: " + ComandosAnalise.MensagemMotor(ex), true); }
            var j = new JanelaLigacaoProjeto(tipos) { ApiGrupo = (m, r, c) => d.Ligacoes(m, r, c) };
            j.UsarGrupo(g, dim);
            j.Aplicado = () => { if (Esp == d) { alterado = true; AtualizarTitulo(); } if (ger != null) _ = ger.Carregar(false); };
            j.AbrirPlaca = jp => { jp.Close(); if (ger != null) { ger.Escolher(S(g, "id")); _ = ger.DimensionarSelecionado(); } };
            return j;
        }
    }

    // ---------------------------------------------------------------- capturas 51) projetista (motor real)
    public static partial class Capturas
    {
        // Biblioteca; chapa simples (shear tab) ao vivo; caso que não passa (vermelho); otimizado; galpão: grupo viga–pilar
        // projetado e aplicado (lista por membro). Só elas: ESTRUCTTO_CAPTURAS=projetista (com ESTRUCTTO_MOTOR).
        // C3: clique de verdade (Vista3D.TesteClique = botão solto sem arrasto) numa chapa e num parafuso do galpão com projeto aplicado
        // abre o projetista do grupo no campo; clique numa barra ainda seleciona a barra (e não abre nada). Custo do picking em
        // `custo-picking.txt`.
        static void CliqueVista3DPrincipal(MainWindow w, string obra = "galpao")
        {
            var log = new StringBuilder();
            string Fim() { File.WriteAllText(Path.Combine(pastaSaida, "custo-picking-" + obra + ".txt"), log.ToString()); return log.ToString(); }
            w.DefinirRealista(true); w.Atualizar3D(false); EsperarIdle(); Esperar(w.TarefaLigacoes3D); EsperarIdle();
            var v = w.vista3D;
            if (!v.UsandoDx) { log.AppendLine("sem Direct3D: clique na Vista3D não testado"); Fim(); throw new InvalidOperationException("Render3D (Direct3D 11) indisponível"); }
            Render3D.Coleta = new List<Render3D.Peca3D>(); v.DefinirLigacoes(v.ModeloAtual.Ligacoes); EsperarIdle();
            var col = Render3D.Coleta; Render3D.Coleta = null;
            System.Windows.Media.Media3D.Point3D Centro(Render3D.Peca3D p) { var ps = p.M.P; return new System.Windows.Media.Media3D.Point3D(ps.Average(q => q.X), ps.Average(q => q.Y), ps.Average(q => q.Z)); }
            Ligacao3D gotL = null; string gotTag = null;
            Action<Ligacao3D, string> ouvir = (l, t) => { gotL = l; gotTag = t; };
            v.CliquePecaLig += ouvir;
            JanelaLigacaoProjeto Aberta() => w.LigacoesAbertas.FirstOrDefault(x => x.IsVisible && x.CampoDoDestaque != null);   // W7: abas, não janelas
            JanelaLigacaoProjeto Clicar(string tipoPeca, string nome, Func<string, bool> tagOk)
            {
                var centros = tipoPeca == "parafuso" ? v.CentrosParafusos() : col.Where(x => x.Tipo == tipoPeca && x.Lig != null && !x.Lig.Proposta).Take(250).Select(Centro).ToList();
                foreach (var c in centros)
                {
                    gotL = null; gotTag = null;
                    foreach (double az in new[] { 30.0, 150.0, 210.0, 330.0 })   // o parafuso só aparece de um lado da chapa
                    {
                        v.Focar(c, tipoPeca == "parafuso" ? 0.5 : 1.0, az, 20); EsperarIdle(); EsperarIdle();
                        v.TesteClique(v.Tela(c));
                        if (gotL != null && tagOk(gotTag)) break;
                        gotL = null; gotTag = null;
                    }
                    if (gotL == null || !tagOk(gotTag)) { foreach (var j0 in w.LigacoesAbertas.ToList()) j0.Close(); continue; }
                    JanelaLigacaoProjeto jp = null;
                    for (int i = 0; i < 300 && jp == null; i++) { EsperarIdle(); System.Threading.Thread.Sleep(100); jp = Aberta(); }
                    if (jp == null) { var qq = w.LigacoesAbertas.ToList(); throw new InvalidOperationException("clique na Vista3D (" + nome + ") não abriu o projetista no campo; tag " + gotTag + " lig " + gotL.No + "/" + gotL.Barra + "; janelas " + qq.Count + " " + string.Join(",", qq.Select(x => x.IsVisible + "/" + x.TemTipo + "/" + x.CampoDoDestaque))); }
                    Esperar(jp.CalcularAgora());   // nas capturas a janela não calcula sozinha ao abrir
                    EsperarIdle();
                    log.AppendLine(nome + ": clique abriu o projetista, tag " + gotTag + ", campo destacado " + jp.CampoDoDestaque);
                    return jp;
                }
                throw new InvalidOperationException("clique na Vista3D: nenhum(a) " + nome + " acertou");
            }
            log.AppendLine("coleta: chapas " + col.Count(x => x.Tipo == "chapa") + ", fora de proposta " + col.Count(x => x.Tipo == "chapa" && x.Lig != null && !x.Lig.Proposta) + ", com campo " + col.Count(x => x.Tipo == "chapa" && x.Lig != null && !x.Lig.Proposta && x.Lig.Chapas.Any(c => !string.IsNullOrEmpty(c.Campo))) + ", parafusos clicáveis " + v.MedirClique(1).parafusos + ", ligações " + v.ModeloAtual.Ligacoes.Count); Fim();
            { var L = v.ModeloAtual.Ligacoes; log.AppendLine("ligações: com grupo do motor " + L.Count(l => l.Grupo != "")) ; }
            { var L = v.ModeloAtual.Ligacoes; log.AppendLine("proposta " + L.Count(l => l.Proposta) + ", motor com chapas " + L.Count(l => !l.Proposta && l.Chapas.Count > 0) + ", motor com parafusos " + L.Count(l => !l.Proposta && l.Parafusos.Count > 0) + ", motor sem peças " + L.Count(l => !l.Proposta && l.Chapas.Count + l.Parafusos.Count + l.Cantoneiras.Count == 0) + ", chapas do motor sem campo " + L.Count(l => !l.Proposta && l.Chapas.Any(c => string.IsNullOrEmpty(c.Campo))) + ", Grupo vazio entre as não-proposta " + L.Count(l => !l.Proposta && l.Grupo == "")); Fim(); }
            var jc = Clicar("chapa", "chapa", t => !string.IsNullOrEmpty(t));
            if (!jc.TemTipo) throw new InvalidOperationException("projetista aberto sem tipo");
            w.MostrarLigacao(null); CapturarPrincipal(w, "51-vista3d-clique-chapa-" + obra);   // a aba aberta pelo clique cobre a vista: foto do modelo
            CapturarDialogo(jc, "51-projetista-clique-vista3d-chapa-" + obra + ".png"); jc.Close(); EsperarIdle();
            var jb = Clicar("parafuso", "parafuso", t => t != null && t.StartsWith("d"));
            CapturarDialogo(jb, "51-projetista-clique-vista3d-parafuso-" + obra + ".png"); jb.Close(); EsperarIdle();
            v.CliquePecaLig -= ouvir;
            // barra: ainda seleciona a barra e não abre o projetista
            v.Selecionar(new string[0], new string[0]); EsperarIdle();
            bool achou = false;
            foreach (var b in v.ModeloAtual.Barras.OrderByDescending(x => { var a = v.ModeloAtual.Nos.First(n => n.Id == x.Ni); var d2 = v.ModeloAtual.Nos.First(n => n.Id == x.Nj); return Math.Abs(a.X - d2.X) + Math.Abs(a.Y - d2.Y) + Math.Abs(a.Z - d2.Z); }).Take(30))
            {
                var na = v.ModeloAtual.Nos.First(n => n.Id == b.Ni); var nb = v.ModeloAtual.Nos.First(n => n.Id == b.Nj);
                var m = new System.Windows.Media.Media3D.Point3D((na.X + nb.X) / 2, (na.Y + nb.Y) / 2, (na.Z + nb.Z) / 2);
                v.Focar(m, 6, 30, 20); EsperarIdle(); EsperarIdle();
                if (!(v.Acertar(v.Tela(m)) is Barra3D)) continue;
                v.TesteClique(v.Tela(m)); EsperarIdle();
                if (v.Selecao.Barras.Count != 1) throw new InvalidOperationException("clique na barra não a selecionou");
                if (w.LigacoesAbertas.Count > 0) throw new InvalidOperationException("clique na barra abriu o projetista");
                log.AppendLine("barra " + b.Id + ": clique seleciona a barra, projetista não abre"); achou = true; break;
            }
            if (!achou) throw new InvalidOperationException("clique na barra: nenhuma barra acertada no meio");
            v.Enquadrar(-60, 30, false); EsperarIdle();
            var (ms, np) = v.MedirClique();
            log.AppendLine(obra + ": " + np + " parafusos clicáveis, Acertar completo = " + ms.ToString("0.00") + " ms/clique");
            int nb2 = col.Count(x => x.Tipo == "parafuso");
            log.AppendLine(obra + ": " + nb2 + " parafusos no total; teste de parafusos sozinho (mesmo código) = " + Render3D.MedirParafusos(nb2).ToString("0.000") + " ms/clique");
            Fim();
        }

        static void CapturasProjetista(MainWindow w)
        {
            var tipos = Esperar2(Motor.Get("/api/ligacoes/tipos"));
            var j = new JanelaLigacaoProjeto(tipos);
            w.MostrarLigacao(j); EsperarIdle();   // W7: aba da janela principal
            Esperar(j.CarregarMiniaturas()); EsperarIdle();
            CapturarDialogo(j, "51-projetista-biblioteca.png");
            RenderizarJanela(w, 1280, 720, "51-projetista-biblioteca-1280x720.png");
            Esperar(j.Escolher("chapa_simples_alma", false));
            j.DefinirEsforcos(new JsonObject { ["V"] = 100, ["N"] = 0 }); Esperar(j.CalcularAgora()); EsperarIdle();
            CapturarDialogo(j, "51-projetista-chapa-simples.png");
            j.MostrarVista(1); EsperarIdle();
            CapturarDialogo(j, "51-projetista-chapa-simples-elevacao.png");
            j.MostrarVista(4); EsperarIdle();
            CapturarDialogo(j, "51-projetista-chapa-simples-3d.png");
            j.MostrarVista(0);
            j.DefinirEsforcos(new JsonObject { ["V"] = 165, ["N"] = 0 }); Esperar(j.CalcularAgora()); EsperarIdle();
            if (Bool(j.Resultado, "ok")) throw new InvalidOperationException("projetista: esperado um caso que não passa com V = 165 kN");
            CapturarDialogo(j, "51-projetista-falha.png");
            Esperar(j.Otimizar()); EsperarIdle();
            CapturarDialogo(j, "51-projetista-otimizado.png");
            {   // 26/10/2026: o combo de perfil vinha vazio (lista montada antes de o catálogo chegar) e a solda direta só soldava a alma
                Esperar(j.Escolher("solda_direta_alma", false)); EsperarIdle();
                var tp1 = j.TrocarPerfil("perfil_viga", "W360x51"); Esperar(tp1); EsperarIdle(); var (n1, g1) = tp1.Result;
                var tp2 = j.TrocarPerfil("perfil_suporte", "W250x73"); Esperar(tp2); EsperarIdle(); var (n2, g2) = tp2.Result;
                if (n1 < 50 || n2 < 50 || g1 != "W360x51" || g2 != "W250x73")
                    throw new InvalidOperationException($"projetista: troca de perfil no combo: itens {n1}/{n2}, gravado '{g1}'/'{g2}'");
                {   // 27/10/2026: a lista do seletor abria e fechava sozinha (clique real, janela na tela). W7: a janela principal com a aba
                    double l0 = w.Left, t0 = w.Top; w.Width = 1600; w.Height = 900; w.Left = 0; w.Top = 0; w.Topmost = true; w.Activate(); EsperarIdle();
                    bool aberto = j.CliqueRealNoSeletor("perfil_viga", EsperarIdle);
                    bool ativa = w.IsActive;
                    w.Topmost = false; w.Left = l0; w.Top = t0; EsperarIdle();

                    if (!aberto) throw new InvalidOperationException("projetista: a lista de perfis fechou sozinha depois do clique; janela ativa: " + ativa);
                }
                j.DefinirEsforcos(new JsonObject { ["V"] = 100, ["N"] = 0 }); Esperar(j.CalcularAgora()); EsperarIdle();
                var sd = j.Resultado?["geometria"]?["soldas"]?.AsArray();
                if (sd == null || sd.Count != 1 || sd[0]["pontos"].AsArray().Count != 13)
                    throw new InvalidOperationException("projetista: solda direta de W sem o contorno das mesas");
                CapturarDialogo(j, "51-projetista-solda-direta-mesas.png");
                j.MostrarVista(4); EsperarIdle(); CapturarDialogo(j, "51-projetista-solda-direta-mesas-3d.png"); j.MostrarVista(0);
            }
            // C3: seções (as 6 existem; capturas recolhidas e abertas), toda peça de todo padrão tem campo, clique -> campo, sem solução
            var secs = new HashSet<string>();
            foreach (var t in Lista(tipos)) foreach (var x in j.NomesSecoes(S(t, "tipo"))) secs.Add(x);
            foreach (var n in new[] { "Chapa", "Parafusos", "Soldas", "Enrijecedores", "Recortes", "Folgas" })
                if (!secs.Contains(n)) throw new InvalidOperationException("projetista: nenhum padrão tem a seção " + n);
            var semCampo = new List<string>();
            foreach (var t in Lista(tipos))
            {
                Esperar(j.Escolher(S(t, "tipo"), false)); Esperar(j.CalcularAgora());
                foreach (var m in j.PecasSemCampo()) semCampo.Add(S(t, "tipo") + ": " + m);
            }
            File.WriteAllText(Path.Combine(pastaSaida, "pecas-sem-campo.txt"), semCampo.Count == 0 ? "todas as pecas dos 20 padroes tem campo\n" : string.Join("\n", semCampo));
            if (semCampo.Count > 0) throw new InvalidOperationException("projetista: peças sem campo: " + string.Join(" | ", semCampo.Take(6)));
            Esperar(j.Escolher("chapa_simples_alma", false)); j.RecolherSecoes(); EsperarIdle();
            CapturarDialogo(j, "51-projetista-secoes-recolhidas.png");
            Esperar(j.Escolher("apoio_cantoneira", false)); j.RecolherSecoes(); EsperarIdle();
            CapturarDialogo(j, "51-projetista-secoes-recolhidas-apoio-cantoneira.png");
            Esperar(j.Escolher("chapa_topo_estendida", false)); EsperarIdle();
            string tg = j.ClicarChapa3D(); EsperarIdle(); EsperarIdle();
            if (tg == null) throw new InvalidOperationException("projetista: clique na chapa não achou a peça");
            if (j.CampoDoDestaque == null) throw new InvalidOperationException("projetista: clique na chapa não destacou o campo");
            CapturarDialogo(j, "51-projetista-clique-chapa.png");
            j.IrParaTag("campo_que_nao_existe"); EsperarIdle();
            CapturarDialogo(j, "51-projetista-clique-sem-campo.png");
            Esperar(j.Escolher("chapa_simples_alma", false));
            j.DefinirEsforcos(new JsonObject { ["V"] = 5000, ["N"] = 0 }); Esperar(j.CalcularAgora()); EsperarIdle();
            Esperar(j.Otimizar()); EsperarIdle();
            if (!j.SemSolucao) throw new InvalidOperationException("projetista: V = 5000 kN deveria dar sem solução");
            CapturarDialogo(j, "51-projetista-sem-solucao.png");
            foreach (var t in new[] { "chapa_topo_estendida", "emenda_viga", "gusset_contraventamento", "cantoneira_dupla_alma", "tubo_tubo_soldado", "perfil_tubo_soldado" })
            {
                if (Lista(tipos).All(x => S(x, "tipo") != t)) continue;
                Esperar(j.Escolher(t, false)); EsperarIdle();
                CapturarDialogo(j, "51-projetista-" + t.Replace('_', '-') + ".png");
            }
            j.Close();

            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("galpão não abriu");
            Esperar(w.CalcularEspacial()); EsperarIdle();
            var ger = w.NovoGerenciadorLigacoes(d);
            PrepararDialogoForaDaTela(ger); ger.Show(); EsperarIdle();
            Esperar(ger.Carregar(true)); EsperarIdle();
            var grupos = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
            var g = grupos.First(x => S(x, "tipo").StartsWith("viga_pilar"));
            var t2 = w.CriarProjetistaGrupo(d, g, ger); Esperar(t2);
            var jg = t2.Result ?? throw new InvalidOperationException("projetista do grupo não abriu");
            w.MostrarLigacao(jg); EsperarIdle();
            CapturarDialogo(jg, "51-projetista-grupo-biblioteca.png");
            Esperar(jg.Escolher("chapa_simples_alma")); EsperarIdle();
            CapturarDialogo(jg, "51-projetista-grupo-projeto.png");
            Esperar(jg.AplicarAoGrupo()); EsperarIdle();
            CapturarDialogo(jg, "51-projetista-grupo-aplicado.png");
            jg.Close(); ger.Close(); EsperarIdle();
            CliqueVista3DPrincipal(w);
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
            // custo do picking na obra real (PLATAFORMA.ed3, ~30 m; fora do git): parafusos da cena + medição com o mesmo número
            string ed3 = new[] { "C:/dev/wt/v5-vista/testes-aceite/PLATAFORMA.ed3" }.FirstOrDefault(File.Exists);
            if (ed3 != null)
            {
                Esperar(w.AbrirTarefa(ed3)); EsperarIdle();
                var dP = w.EspacialAtivo ?? throw new InvalidOperationException("PLATAFORMA não abriu");
                var gerP = w.NovoGerenciadorLigacoes(dP); PrepararDialogoForaDaTela(gerP); gerP.Show(); EsperarIdle();
                Esperar(gerP.Carregar(true)); EsperarIdle(); gerP.Close(); EsperarIdle();   // identifica os grupos (como o --testes-interferencia)
                // PLATAFORMA só tem projeto do motor de solda direta (sem chapa/parafuso) e propostas: o engenheiro aplica chapa simples em alguns grupos viga–pilar
                var gruposP = Esperar2(dP.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray();
                int aplicados = 0;
                foreach (var gp in gruposP.Where(x => S(x, "tipo").StartsWith("viga_pilar")).Take(6))
                {
                    try
                    {
                        var tp = w.CriarProjetistaGrupo(dP, gp, null); Esperar(tp); var jp = tp.Result; if (jp == null) continue;
                        w.MostrarLigacao(jp); EsperarIdle();
                        Esperar(jp.Escolher("chapa_simples_alma")); Esperar(jp.AplicarAoGrupo()); jp.Close(); EsperarIdle(); aplicados++;
                    }
                    catch (Exception) { }
                }
                File.AppendAllText(Path.Combine(pastaSaida, "custo-picking-plataforma-aplicados.txt"), "grupos viga-pilar com chapa simples aplicada: " + aplicados + Environment.NewLine);
                w.Atualizar3D(false); EsperarIdle(); Esperar(w.TarefaLigacoes3D); EsperarIdle();
                CliqueVista3DPrincipal(w, "plataforma");   // PLATAFORMA já tem projetos do motor: clique em chapa e parafuso abre o projetista
                w.FecharEspaciaisSemPerguntar();
            }
        }
    }
}
