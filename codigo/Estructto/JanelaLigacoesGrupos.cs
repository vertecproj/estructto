// Gerenciador de ligações (docs/v3/LIGACOES-GRUPOS.md): o motor identifica as ligações nos nós e agrupa as iguais;
// aqui o usuário vê os grupos (cartões à esquerda), os membros do grupo com a taxa de cada um (meio, ordenável, arrastar
// para outro cartão move) e o projeto do grupo à direita ("Dimensionar pela pior": o detalhe vale para todo o grupo).
// Placa de base abre a JanelaPlaca com os esforços do pior membro. Escolher um grupo destaca os nós na Vista3D.
// Rotas: POST /api/ligacoes/identificar, GET /api/ligacoes/grupos, POST /api/ligacoes/grupos/editar,
// POST /api/ligacoes/grupos/{id}/dimensionar (DocumentoEspacial.Ligacoes, abaixo).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Portico;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaLigacoesGrupos : JanelaEscura
    {
        public Func<string, string, JsonObject, Task<JsonNode>> Api;    // (método, rota, corpo)
        public Action<IList<string>, IList<string>> Destacar;            // nós, barras → Vista3D
        public Func<Task> Calcular;                                      // F5 do documento
        public Func<JsonNode, JsonNode, JsonObject> AbrirPlaca;          // (grupo, dimensionamento) → dados novos da placa ou null
        public Action Mudou;                                             // o documento mudou (ligacoes no JSON)

        static readonly Dictionary<string, string> NomesLig = new Dictionary<string, string>
        { [""] = "Sem projeto", ["placa_base"] = "Placa de base", ["parafuso"] = "Parafuso", ["solda_filete"] = "Filete de solda", ["chapa_simples"] = "Chapa simples", ["ligacao_tracao"] = "Ligação de tração" };

        // glifos 32 × 32 (traço) por tipo de nó
        static readonly Dictionary<string, string> Glifos = new Dictionary<string, string>
        {
            ["base"] = "M16,3 V18 M4,18 H28 V23 H4 Z M8,23 V28 M24,23 V28",
            ["viga_pilar_mesa"] = "M4,3 H14 M4,29 H14 M9,3 V29 M14,12 H29 M14,20 H29",
            ["viga_pilar_alma"] = "M5,3 V29 M13,3 V29 M5,16 H13 M13,12 H29 M13,20 H29",
            ["viga_viga"] = "M3,7 H29 M3,13 H29 M16,13 V29 M11,29 H21",
            ["emenda"] = "M3,16 H12 M20,16 H29 M12,9 V23 M20,9 V23 M10,11 H22 V21 H10 Z",
            ["gusset_contraventamento"] = "M5,4 V28 H28 M5,13 L14,28 M5,28 L26,7",
            ["no_trelica"] = "M3,26 H29 M3,26 L16,6 L29,26 M16,6 V26",
        };
        const string Cadeado = "M10,15 V11 A6,6 0 0 1 22,11 V15 M8,15 H24 V28 H8 Z";
        const string CadeadoAberto = "M10,15 V9 A6,6 0 0 1 21,7 M8,15 H24 V28 H8 Z";

        JsonArray grupos = new JsonArray();
        JsonNode tipos, dim; string erro, gSel;
        readonly HashSet<string> gMarcados = new HashSet<string>(), mSel = new HashSet<string>();
        string ultimoMembro;
        int ordCol = 6; bool ordDesc = true;
        JsonObject dadosEdit; string tipoEdit, gEdit;
        bool ocupado;
        readonly Border hostCab = new Border(), hostGrupos = new Border(), hostMembros = new Border(), hostProjeto = new Border();

        public JanelaLigacoesGrupos()
        {
            Moldar(this, "Gerenciador de ligações", 1320, 780);
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            var esq = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(10), Child = hostGrupos };
            corpo.Children.Add(esq);
            hostMembros.Padding = new Thickness(12, 10, 12, 10);
            Grid.SetColumn(hostMembros, 1); corpo.Children.Add(hostMembros);
            var dir = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(12, 10, 12, 10),
                                   Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = hostProjeto } };
            Grid.SetColumn(dir, 2); corpo.Children.Add(dir);
            var nota = Tx("Prática de escritório: calcula-se a pior ligação de cada grupo e o mesmo detalhe vale para todas. Arraste ligações entre os grupos. Resultado do motor; exige conferência do engenheiro (ART).", 11.5, TemaPlant.BTexto3);
            nota.TextTrimming = TextTrimming.CharacterEllipsis;
            Content = Raiz(hostCab, corpo, Rodape(Dica(nota, nota.Text), B("Fechar", Close, false, "Fecha o gerenciador (Esc). As edições dos grupos já estão no modelo.")));
            Montar();
            Loaded += async (s, e) => { if (grupos.Count == 0 && !Capturas.Ativo) await Carregar(true); };
            Closed += (s, e) => Destacar?.Invoke(new string[0], new string[0]);
        }

        // ---------------------------------------------------------------- dados

        static string Chave(JsonNode m) => S(m, "no") + "|" + S(m, "barra") + "|" + S(m, "ponta");
        JsonNode Grupo(string id) => grupos.FirstOrDefault(g => S(g, "id") == id);

        // identificar = re-identifica (guarda as edições) antes de ler os grupos
        public async Task Carregar(bool identificar)
        {
            if (Api == null) return;
            await Rodar(async () =>
            {
                if (identificar) { await Api("POST", "/api/ligacoes/identificar", new JsonObject()); Mudou?.Invoke(); }
                if (tipos == null) tipos = await Api("GET", "/api/ligacoes/tipos", null);
                grupos = (await Api("GET", "/api/ligacoes/grupos", null)) as JsonArray ?? new JsonArray();
            });
            if (Grupo(gSel) == null) { gSel = S(grupos.FirstOrDefault(), "id"); mSel.Clear(); dim = null; }
            Montar(); Realcar();
        }

        async Task Rodar(Func<Task> op)
        {
            if (ocupado) return;
            ocupado = true; erro = null;
            try { await op(); }
            catch (Exception ex) { erro = ComandosAnalise.MensagemMotor(ex); }
            finally { ocupado = false; }
        }

        async Task Editar(JsonObject corpo, bool limparSel = true)
        {
            string novo = null;
            await Rodar(async () => { novo = S(await Api("POST", "/api/ligacoes/grupos/editar", corpo), "grupo"); Mudou?.Invoke(); });
            if (erro != null) { Montar(); return; }
            if (limparSel) mSel.Clear();
            await Carregar(false);
            if (novo != "" && Grupo(novo) != null && (string)corpo["acao"] != "renomear") Escolher(novo);
        }

        // API das capturas e do botão: dimensiona o grupo escolhido pela pior ligação
        public async Task DimensionarSelecionado()
        {
            var g = Grupo(gSel);
            if (g == null) return;
            string tl = S(g["projeto"], "tipo_ligacao");
            if (tipoEdit != null && gEdit == gSel && (tipoEdit != tl || dadosEdit?.ToJsonString() != g["projeto"]?["dados"]?.ToJsonString()) && tipoEdit != "placa_base")
            {
                await Editar(new JsonObject { ["acao"] = "projeto", ["grupo"] = gSel, ["tipo_ligacao"] = tipoEdit == "" ? null : tipoEdit, ["dados"] = dadosEdit?.DeepClone() }, false);
                g = Grupo(gSel);
            }
            if (Calcular != null && grupos.All(x => x["governante"] == null))   // sem cálculo: F5 antes
            {
                await Calcular(); await Carregar(false); g = Grupo(gSel);
            }
            JsonNode r = null;
            await Rodar(async () => r = await Api("POST", "/api/ligacoes/grupos/" + Uri.EscapeDataString(gSel) + "/dimensionar", new JsonObject()));
            dim = r;
            if (r != null && S(r, "tipo_ligacao") == "placa_base" && AbrirPlaca != null && !Capturas.Ativo)
            {
                var novo = AbrirPlaca(g, r);
                if (novo != null)
                {
                    await Editar(new JsonObject { ["acao"] = "projeto", ["grupo"] = gSel, ["tipo_ligacao"] = "placa_base", ["dados"] = novo }, false);
                    await Rodar(async () => dim = await Api("POST", "/api/ligacoes/grupos/" + Uri.EscapeDataString(gSel) + "/dimensionar", new JsonObject()));
                }
            }
            Montar();
        }

        public void Escolher(string id)
        {
            if (gSel != id) { mSel.Clear(); dim = null; }
            gSel = id; gEdit = null;
            Montar(); Realcar();
        }

        void Realcar()
        {
            var g = Grupo(gSel);
            if (g == null || Destacar == null) return;
            var ms = Lista(g["membros"]).Where(m => mSel.Count == 0 || mSel.Contains(Chave(m))).ToList();
            Destacar(ms.Select(m => S(m, "no")).Distinct().ToList(), ms.Select(m => S(m, "barra")).Distinct().ToList());
        }

        // ---------------------------------------------------------------- montagem

        void Montar()
        {
            MontarCab(); MontarGrupos(); MontarMembros(); MontarProjeto();
        }

        static double Taxa(JsonNode g) => D(g, "taxa_max");

        void MontarCab()
        {
            int nm = grupos.Sum(g => (int)D(g, "n_membros"));
            var pior = grupos.Where(g => !double.IsNaN(Taxa(g))).OrderByDescending(Taxa).FirstOrDefault();
            bool calc = grupos.Any(g => g["governante"] != null);
            var acoes = new StackPanel { Orientation = Orientation.Horizontal };
            if (!calc && Calcular != null) acoes.Children.Add(Botao("Calcular (F5)", "calc", async () => { await Rodar(Calcular); await Carregar(false); }, "Calcula o modelo para achar a pior ligação de cada grupo."));
            var re = Botao("Re-identificar", "busca", async () => await Carregar(true), "Identifica de novo as ligações do modelo e agrupa as iguais. Grupos travados e editados por você ficam como estão.");
            re.Margin = new Thickness(8, 0, 0, 0); acoes.Children.Add(re);
            string titulo = erro != null ? "Motor: " + erro : grupos.Count == 0 ? "Nenhuma ligação identificada" : Pl.N(grupos.Count, "grupo", "grupos") + " · " + Pl.N(nm, "ligação", "ligações");
            string sub = !calc ? "Sem cálculo: F5 calcula e mostra a pior ligação de cada grupo (todas as combinações ELU)."
                : pior == null ? "Escolha o projeto de cada grupo (à direita) para ver a taxa da pior ligação."
                : "Pior grupo: " + S(pior, "nome") + " · " + Pct(Taxa(pior)) + " em " + S(pior["governante"], "barra") + " (" + S(pior["governante"], "combinacao") + ")";
            var nv = erro != null ? Nivel.Erro : pior == null ? Nivel.Neutro : NivelAp(Taxa(pior), grupos.All(g => g["ok"] == null || Bool(g, "ok")));
            hostCab.Child = Cabecalho(nv, nv == Nivel.Erro ? "✕" : nv == Nivel.Neutro ? "≡" : nv == Nivel.Alerta ? "!" : "✓", titulo, sub, acoes);
        }

        static FrameworkElement Glifo(string d, double tam, Brush cor)
        {
            var p = new Path { Data = Geometry.Parse(d), Stroke = cor, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var c = new Canvas { Width = 32, Height = 32, Children = { p } };
            return new Viewbox { Width = tam, Height = tam, Child = c };
        }

        void MontarGrupos()
        {
            var sp = new StackPanel();
            var topo = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
            var unir = Botao("Unir " + (gMarcados.Count > 1 ? "(" + gMarcados.Count + ")" : ""), "unir", async () =>
            {
                if (gMarcados.Count < 2) return;
                var ids = new JsonArray(); ids.Add(gSel != null && gMarcados.Contains(gSel) ? gSel : gMarcados.First());
                foreach (var x in gMarcados) if (x != (string)ids[0]) ids.Add(x);
                gMarcados.Clear();
                await Editar(new JsonObject { ["acao"] = "unir", ["grupos"] = ids });
            }, "Une os grupos marcados (Ctrl + clique nos cartões) num só.");
            unir.IsEnabled = gMarcados.Count > 1; unir.Opacity = unir.IsEnabled ? 1 : 0.5;
            DockPanel.SetDock(unir, Dock.Right); topo.Children.Add(unir);
            topo.Children.Add(Tx("Grupos", 12.5, TemaPlant.BTexto2, FontWeights.SemiBold));
            sp.Children.Add(topo);
            if (grupos.Count == 0) sp.Children.Add(Vazio("unir", "Sem grupos", "Modelo sem ligações de aço (nós com 2 ou mais barras ou apoios)."));
            foreach (var g in grupos) sp.Children.Add(CartaoGrupo(g));
            hostGrupos.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            Anim.Escalonar(sp, 4);
        }

        Border CartaoGrupo(JsonNode g)
        {
            string id = S(g, "id");
            bool sel = id == gSel, marc = gMarcados.Contains(id), trav = Bool(g, "travado");
            double tx = Taxa(g);
            bool ok = g["ok"] == null || Bool(g, "ok");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Background = TemaPlant.BSup3, VerticalAlignment = VerticalAlignment.Top,
                                  Child = Glifo(Glifos.TryGetValue(S(g, "tipo"), out var gl) ? gl : Glifos["emenda"], 20, sel ? TemaPlant.BAcento : TemaPlant.BTexto2) };
            grid.Children.Add(ic);
            var txt = new StackPanel();
            var nome = Tx(S(g, "nome"), 12.5, TemaPlant.BTexto1, FontWeights.SemiBold); nome.TextTrimming = TextTrimming.CharacterEllipsis;
            txt.Children.Add(Dica(nome, S(g, "nome") + "\n" + S(g, "assinatura").Replace("|", " · ")));
            var gov = g["governante"];
            string linha2 = S(g, "rotulo") + " · " + Pl.N((int)D(g, "n_membros"), "ligação", "ligações");
            var l2 = Tx(linha2, 11, TemaPlant.BTexto3); l2.Margin = new Thickness(0, 1, 0, 0); txt.Children.Add(l2);
            var l3 = Tx(gov == null ? (g["projeto"] == null ? "sem projeto" : "sem cálculo") : "pior: " + S(gov, "barra") + " (" + S(gov, "ponta") + ") no nó " + S(gov, "no"), 11, gov == null ? TemaPlant.BTextoMudo : TemaPlant.BTexto2);
            l3.TextTrimming = TextTrimming.CharacterEllipsis; txt.Children.Add(l3);
            var barra = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var bc = BarraCap(double.IsNaN(tx) ? 0 : tx, ok, 5); bc.VerticalAlignment = VerticalAlignment.Center; barra.Children.Add(bc);
            var pc = Num1(double.IsNaN(tx) ? "—" : Pct(tx), double.IsNaN(tx) ? TemaPlant.BTexto3 : Cores(NivelAp(tx, ok)).fg, true);
            Grid.SetColumn(pc, 1); barra.Children.Add(pc);
            txt.Children.Add(barra);
            Grid.SetColumn(txt, 1); grid.Children.Add(txt);
            var cad = new Border { Background = Brushes.Transparent, Padding = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
                                   Child = Glifo(trav ? Cadeado : CadeadoAberto, 15, trav ? TemaPlant.BAlerta : TemaPlant.BTextoMudo) };
            Dica(cad, trav ? "Travado: a re-identificação não mexe e não aceita mover. Clique para destravar." : "Clique para travar o grupo.");
            cad.MouseLeftButtonDown += async (s, e) => { e.Handled = true; await Editar(new JsonObject { ["acao"] = "travar", ["grupo"] = id, ["travado"] = !trav }, false); };
            Grid.SetColumn(cad, 2); grid.Children.Add(cad);
            Brush fundo = sel ? TemaPlant.BAcentoSutil : TemaPlant.BSup2;
            var b = new Border
            {
                Background = fundo, BorderBrush = sel ? TemaPlant.BAcento : marc ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil, BorderThickness = new Thickness(sel || marc ? 1.5 : 1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 0, 0, 6), Child = grid, Cursor = Cursors.Hand, AllowDrop = !trav
            };
            b.MouseEnter += (s, e) => { if (!sel) b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { if (!sel) b.Background = fundo; };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (!gMarcados.Remove(id)) gMarcados.Add(id); if (gSel != null) gMarcados.Add(gSel); MontarGrupos(); return; }
                gMarcados.Clear(); Escolher(id);
            };
            b.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("membros-ligacao") && id != gSel ? DragDropEffects.Move : DragDropEffects.None; b.BorderBrush = TemaPlant.BBordaFoco; e.Handled = true; };
            b.DragLeave += (s, e) => b.BorderBrush = sel ? TemaPlant.BAcento : TemaPlant.BBordaSutil;
            b.Drop += async (s, e) =>
            {
                if (!(e.Data.GetData("membros-ligacao") is string lista) || id == gSel) return;
                await Mover(lista.Split('\n'), id);
            };
            return b;
        }

        async Task Mover(IEnumerable<string> chaves, string destino)
        {
            var arr = new JsonArray();
            foreach (var k in chaves) { var p = k.Split('|'); if (p.Length == 3) arr.Add(new JsonObject { ["no"] = p[0], ["barra"] = p[1], ["ponta"] = p[2] }); }
            if (arr.Count == 0) return;
            await Editar(new JsonObject { ["acao"] = "mover", ["membros"] = arr, ["destino"] = destino });
        }

        List<JsonNode> MembrosOrdenados(JsonNode g)
        {
            Func<JsonNode, IComparable> k = ordCol switch
            {
                0 => m => S(m, "no"), 1 => m => S(m, "barra") + S(m, "ponta"), 2 => m => S(m, "combinacao"),
                3 => m => Nz(D(m["demanda"], "V")), 4 => m => Nz(D(m["demanda"], "N")), 5 => m => Nz(D(m["demanda"], "M")),
                _ => m => Nz(D(m, "taxa")),
            };
            var l = Lista(g?["membros"]).ToList();
            return (ordDesc ? l.OrderByDescending(k) : l.OrderBy(k)).ToList();
        }
        static double Nz(double v) => double.IsNaN(v) ? -1 : v;

        static readonly (string t, double w, bool dir)[] Cols =
        { ("Nó", 76, false), ("Barra · ponta", 100, false), ("Combinação crítica", 0, false), ("|V| kN", 56, true), ("|N| kN", 56, true), ("|M| kN·m", 66, true), ("Taxa", 134, false) };

        void MontarMembros()
        {
            var g = Grupo(gSel);
            if (g == null) { hostMembros.Child = Vazio("unir", "Nenhum grupo escolhido", "Escolha um grupo à esquerda."); return; }
            bool trav = Bool(g, "travado");
            var dp = new DockPanel();
            // cabeçalho do grupo: nome editável + travar
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var bTrav = Botao(trav ? "Destravar" : "Travar", null, async () => await Editar(new JsonObject { ["acao"] = "travar", ["grupo"] = gSel, ["travado"] = !trav }, false),
                              "Grupo travado: a re-identificação não tira nem põe ligações, e mover/unir ficam bloqueados.");
            DockPanel.SetDock(bTrav, Dock.Right); cab.Children.Add(bTrav);
            var nome = new TextBox { Text = S(g, "nome"), FontSize = 14, FontWeight = FontWeights.SemiBold, Background = TemaPlant.BSup3, Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1,
                                     BorderBrush = TemaPlant.BBorda, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Dica(nome, "Nome do grupo: Enter grava.");
            string nome0 = nome.Text;
            async void Renomear() { if (nome.Text.Trim() != "" && nome.Text != nome0) { nome0 = nome.Text; await Editar(new JsonObject { ["acao"] = "renomear", ["grupo"] = gSel, ["nome"] = nome.Text.Trim() }, false); } }
            nome.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Renomear(); } };
            nome.LostKeyboardFocus += (s, e) => Renomear();
            cab.Children.Add(nome);
            DockPanel.SetDock(cab, Dock.Top); dp.Children.Add(cab);

            var info = Tx(S(g, "rotulo") + " · " + string.Join(" · ", S(g, "assinatura").Split('|').Where(x => x != "")) + (Bool(g, "manual") ? " · editado por você" : ""), 11.5, TemaPlant.BTexto3);
            info.TextTrimming = TextTrimming.CharacterEllipsis; info.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(info, Dock.Top); dp.Children.Add(info);

            // barra de ações dos membros
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var bMover = Botao("Mover para…", "mover", null, "Move as ligações marcadas para outro grupo (ou arraste-as até o cartão).");
            bMover.MouseLeftButtonDown += (s, e) => { e.Handled = true; MenuMover(bMover).IsOpen = true; };
            var bSep = Botao("Separar em grupo novo", "separar", async () =>
            {
                if (mSel.Count == 0) return;
                await Editar(new JsonObject { ["acao"] = "dividir", ["grupo"] = gSel, ["membros"] = Membros(mSel) });
            }, "As ligações marcadas viram um grupo novo (com o mesmo projeto).");
            foreach (var bt in new[] { bMover, bSep }) { bt.IsEnabled = mSel.Count > 0 && !trav; bt.Opacity = bt.IsEnabled ? 1 : 0.5; bt.Margin = new Thickness(0, 0, 8, 0); acoes.Children.Add(bt); }
            var todos = Link(mSel.Count > 0 ? "Limpar marcação (" + mSel.Count + ")" : "Marcar todas", () =>
            {
                if (mSel.Count > 0) mSel.Clear(); else foreach (var m in Lista(g["membros"])) mSel.Add(Chave(m));
                MontarMembros(); Realcar();
            });
            todos.VerticalAlignment = VerticalAlignment.Center; todos.Margin = new Thickness(6, 0, 0, 0);
            acoes.Children.Add(todos);
            DockPanel.SetDock(acoes, Dock.Top); dp.Children.Add(acoes);
            dp.Children.Add(TabelaMembros(g, trav));
            hostMembros.Child = dp;
        }

        static JsonArray Membros(IEnumerable<string> chaves)
        {
            var arr = new JsonArray();
            foreach (var k in chaves) { var p = k.Split('|'); arr.Add(new JsonObject { ["no"] = p[0], ["barra"] = p[1], ["ponta"] = p[2] }); }
            return arr;
        }

        ContextMenu MenuMover(FrameworkElement alvo)
        {
            var cm = new ContextMenu { PlacementTarget = alvo };
            foreach (var g in grupos.Where(x => S(x, "id") != gSel && !Bool(x, "travado")))
            {
                string id = S(g, "id");
                var it = new MenuItem { Header = S(g, "nome") + "  (" + Pl.N((int)D(g, "n_membros"), "ligação", "ligações") + ")" };
                it.Click += async (s, e) => await Mover(mSel.ToList(), id);
                cm.Items.Add(it);
            }
            if (cm.Items.Count > 0) cm.Items.Add(new Separator());
            var novo = new MenuItem { Header = "Grupo novo" };
            novo.Click += async (s, e) => { if (mSel.Count > 0) await Editar(new JsonObject { ["acao"] = "dividir", ["grupo"] = gSel, ["membros"] = Membros(mSel) }); };
            cm.Items.Add(novo);
            return cm;
        }

        FrameworkElement TabelaMembros(JsonNode g, bool trav)
        {
            Grid Linha()
            {
                var gr = new Grid();
                foreach (var c in Cols) gr.ColumnDefinitions.Add(new ColumnDefinition { Width = c.w > 0 ? new GridLength(c.w) : new GridLength(1, GridUnitType.Star) });
                return gr;
            }
            var cab = Linha();
            for (int i = 0; i < Cols.Length; i++)
            {
                int k = i;
                var t = Tx(Cols[i].t + (ordCol == i ? (ordDesc ? " ▾" : " ▴") : ""), 11, ordCol == i ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeights.SemiBold);
                t.HorizontalAlignment = Cols[i].dir ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                var bc = new Border { Padding = new Thickness(8, 0, 8, 0), Height = 30, Background = Brushes.Transparent, Child = t, Cursor = Cursors.Hand };
                bc.MouseLeftButtonDown += (s, e) => { e.Handled = true; ordDesc = ordCol == k ? !ordDesc : k >= 3; ordCol = k; MontarMembros(); };
                Dica(bc, "Clique para ordenar.");
                Grid.SetColumn(bc, i); cab.Children.Add(bc);
            }
            var cabB = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = cab };
            var corpo = new StackPanel();
            var lista = MembrosOrdenados(g);
            string govK = g["governante"] == null ? null : Chave(g["governante"]);
            for (int r = 0; r < lista.Count; r++)
            {
                var m = lista[r]; string key = Chave(m);
                var dm = m["demanda"];
                double tx = D(m, "taxa");
                bool ok = m["ok"] == null || Bool(m, "ok");
                var gl = Linha();
                var celulas = new UIElement[]
                {
                    Tx(S(m, "no"), 12, TemaPlant.BTexto1, key == govK ? FontWeights.SemiBold : FontWeights.Normal), Tx(S(m, "barra") + " · " + S(m, "ponta"), 12, TemaPlant.BTexto1),
                    Tx(S(m, "erro") != "" ? "⚠ " + S(m, "erro") : S(m, "combinacao") == "" ? "—" : S(m, "combinacao"), 11.5, S(m, "erro") != "" ? TemaPlant.BAlertaClaro : TemaPlant.BTexto2),
                    Num1(F(D(dm, "V"), 1)), Num1(F(D(dm, "N"), 1)), Num1(F(D(dm, "M"), 1)),
                    double.IsNaN(tx) ? (UIElement)Tx(key == govK ? "◆ pior pela demanda" : "—", 11.5, key == govK ? TemaPlant.BAlertaClaro : TemaPlant.BTextoMudo) : CelTaxa(tx, ok, 68)
                };
                for (int i = 0; i < celulas.Length; i++)
                {
                    if (celulas[i] is TextBlock tb) { tb.TextTrimming = TextTrimming.CharacterEllipsis; if (Cols[i].dir) tb.HorizontalAlignment = HorizontalAlignment.Right; if (i == 2) tb.ToolTip = tb.Text; }
                    var cel = new Border { Padding = new Thickness(8, 4, 8, 4), Child = celulas[i] };
                    ((FrameworkElement)celulas[i]).VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(cel, i); gl.Children.Add(cel);
                }
                bool sel = mSel.Contains(key);
                Brush fundo = sel ? TemaPlant.BAcentoSutil : r % 2 == 0 ? TemaPlant.BSup0 : TemaPlant.BSupAlt;
                var lb = new Border { MinHeight = 28, Background = fundo, BorderBrush = key == govK ? TemaPlant.BAlerta : sel ? TemaPlant.BAcento : TemaPlant.BBordaSutil,
                                      BorderThickness = key == govK || sel ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1), Child = gl, Cursor = Cursors.Hand };
                if (key == govK) Dica(lb, "Ligação que governa o grupo: o projeto é dimensionado por ela e vale para todas.");
                lb.MouseEnter += (s, e) => { if (!mSel.Contains(key)) lb.Background = TemaPlant.BSup3; };
                lb.MouseLeave += (s, e) => { if (!mSel.Contains(key)) lb.Background = fundo; };
                Point ini = new Point(); bool apertou = false;
                lb.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true; ini = e.GetPosition(this); apertou = true;
                    var mods = Keyboard.Modifiers;
                    if (mods.HasFlag(ModifierKeys.Shift) && ultimoMembro != null)
                    {
                        var ks = lista.Select(Chave).ToList(); int a = ks.IndexOf(ultimoMembro), b2 = ks.IndexOf(key);
                        if (a >= 0) for (int i = Math.Min(a, b2); i <= Math.Max(a, b2); i++) mSel.Add(ks[i]);
                    }
                    else if (mods.HasFlag(ModifierKeys.Control)) { if (!mSel.Remove(key)) mSel.Add(key); }
                    else if (!mSel.Contains(key)) { mSel.Clear(); mSel.Add(key); }
                    ultimoMembro = key;
                    MontarMembrosDepois();
                };
                lb.MouseLeftButtonUp += (s, e) => apertou = false;
                lb.MouseMove += (s, e) =>
                {
                    if (!apertou || trav || e.LeftButton != MouseButtonState.Pressed) return;
                    var p = e.GetPosition(this);
                    if (Math.Abs(p.X - ini.X) < 6 && Math.Abs(p.Y - ini.Y) < 6) return;
                    apertou = false;
                    if (!mSel.Contains(key)) { mSel.Clear(); mSel.Add(key); }
                    DragDrop.DoDragDrop(lb, new DataObject("membros-ligacao", string.Join("\n", mSel)), DragDropEffects.Move);
                };
                lb.MouseRightButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    if (!mSel.Contains(key)) { mSel.Clear(); mSel.Add(key); MontarMembrosDepois(); }
                    if (!trav) MenuMover(lb).IsOpen = true;
                };
                corpo.Children.Add(lb);
            }
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = corpo };
            sv.ScrollChanged += (s, e) => cabB.Padding = new Thickness(0, 0, Math.Max(0, sv.ActualWidth - sv.ViewportWidth), 0);
            var dp = new DockPanel { Background = TemaPlant.BSup0 };
            DockPanel.SetDock(cabB, Dock.Top); dp.Children.Add(cabB); dp.Children.Add(sv);
            return new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = dp };
        }

        // remonta depois do clique (o arrastar começa no MouseMove da mesma linha, que precisa continuar viva até lá)
        void MontarMembrosDepois() => Dispatcher.BeginInvoke(new Action(() => { MontarMembros(); Realcar(); }), System.Windows.Threading.DispatcherPriority.Input);

        void MontarProjeto()
        {
            var g = Grupo(gSel);
            if (g == null) { hostProjeto.Child = null; return; }
            var proj = g["projeto"];
            if (gEdit != gSel)
            {
                gEdit = gSel; tipoEdit = S(proj, "tipo_ligacao");
                dadosEdit = proj?["dados"] is JsonObject dd ? (JsonObject)dd.DeepClone() : new JsonObject();
            }
            var sp = new StackPanel();
            var opcoes = new List<string> { "" , "placa_base" };
            opcoes.AddRange(Lista(tipos).Select(t => S(t, "tipo")));
            var cb = Combo(opcoes.Select(o => NomesLig.TryGetValue(o, out var n) ? n : o), NomesLig.TryGetValue(tipoEdit ?? "", out var nn) ? nn : tipoEdit, x =>
            {
                string novo = opcoes.FirstOrDefault(o => (NomesLig.TryGetValue(o, out var n) ? n : o) == x) ?? "";
                if (novo == tipoEdit) return;
                tipoEdit = novo; dadosEdit = novo == "placa_base" ? new JsonObject { ["pelo_perfil"] = true } : new JsonObject();
                _ = Editar(new JsonObject { ["acao"] = "projeto", ["grupo"] = gSel, ["tipo_ligacao"] = novo == "" ? null : novo, ["dados"] = dadosEdit.DeepClone() }, false);
            }, 230, "Tipo de ligação do grupo. O mesmo projeto vale para todas as ligações do grupo.");
            var linhaTipo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var rt = Tx("Ligação", 12, TemaPlant.BTexto2); rt.Width = 120; rt.VerticalAlignment = VerticalAlignment.Center;
            linhaTipo.Children.Add(rt); linhaTipo.Children.Add(cb);
            var conteudo = new StackPanel();
            conteudo.Children.Add(linhaTipo);
            FrameworkElement campos = null;
            if (tipoEdit == "placa_base")
            {
                var d = dadosEdit;
                string Mm(string k, double pad) { double v = D(d, k); return Num.Fmt((double.IsNaN(v) ? pad : v) * 1000, 0) + " mm"; }
                conteudo.Children.Add(Ficha(new List<(string, string, string)>
                {
                    ("Medidas", Bool(d, "pelo_perfil") ? "perfil + sobra" : Mm("H", .4) + " × " + Mm("B", .3), null), ("Espessura", Mm("esp", .019), null),
                    ("Fixações", (double.IsNaN(D(d, "fileiras")) ? 2 : D(d, "fileiras")) + " × " + (double.IsNaN(D(d, "por_fileira")) ? 2 : D(d, "por_fileira")), null),
                    ("Parafuso", S(d, "chumbador") != "" ? S(d, "chumbador") : "M" + (double.IsNaN(D(d, "diametro_mm")) ? 16 : D(d, "diametro_mm")) + " cl. " + (S(d, "classe") == "" ? "8.8" : S(d, "classe")), null),
                }, 80));
                var nota = Tx("“Dimensionar pela pior” abre a janela da placa de base com os esforços da base que governa. Aplicar lá grava a placa para todo o grupo.", 11.5, TemaPlant.BTexto3, null, true);
                nota.Margin = new Thickness(0, 8, 0, 0); conteudo.Children.Add(nota);
            }
            else if (tipoEdit != "")
            {
                var t = Lista(tipos).FirstOrDefault(x => S(x, "tipo") == tipoEdit);
                campos = Campos(Lista(t?["campos"]).ToList(), dadosEdit);
            }
            else conteudo.Children.Add(Tx("Sem projeto: o pior membro sai pela demanda (|V| nas vigas, |N| nas diagonais, |M| nas bases e emendas). Escolha a ligação para dimensionar.", 11.5, TemaPlant.BTexto3, null, true));
            sp.Children.Add(Cartao("Projeto do grupo", conteudo));
            var bDim = B("Dimensionar pela pior", async () => await DimensionarSelecionado(), true,
                         "Verifica o projeto com os esforços de TODAS as ligações do grupo em todas as combinações ELU; a pior governa e o detalhe vale para o grupo.");
            bDim.Height = 32; bDim.Margin = new Thickness(0, 0, 0, 10); bDim.IsEnabled = tipoEdit != "" && !ocupado;
            sp.Children.Add(bDim);
            sp.Children.Add(Resultado(g));
            if (campos != null) sp.Children.Add(Cartao("Dados da ligação (" + (NomesLig.TryGetValue(tipoEdit, out var nl) ? nl : tipoEdit) + ")", campos));
            hostProjeto.Child = sp;
        }

        FrameworkElement Resultado(JsonNode g)
        {
            if (dim == null || S(dim, "grupo") != gSel)
                return Vazio("verif", "Sem dimensionamento", g["governante"] == null ? "Calcule (F5) e escolha a ligação." : "Clique em Dimensionar pela pior para ver as verificações da ligação que governa.");
            var sp = new StackPanel();
            var gov = dim["governante"]; var r = dim["resultado"];
            double tx = D(dim, "taxa_max"); bool ok = Bool(dim, "ok");
            var nv = double.IsNaN(tx) ? Nivel.Neutro : NivelAp(tx, ok);
            var c = Cores(nv);
            var veredito = new Border { Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10) };
            var vs = new StackPanel();
            vs.Children.Add(Tx(double.IsNaN(tx) ? "Pior pela demanda" : (nv == Nivel.Erro ? "Não passa — " : "Passa · ") + Pct(tx) + (r != null ? " em " + S(r, "criterio") : ""), 13, c.fg, FontWeights.SemiBold));
            vs.Children.Add(Tx(gov == null ? "—" : "Governa: " + S(gov, "barra") + " (" + S(gov, "ponta") + ") no nó " + S(gov, "no") + " · " + S(gov, "combinacao") + (S(r, "eixo") != "" ? " · eixo " + S(r, "eixo") : ""), 11.5, TemaPlant.BTexto2, null, true));
            vs.Children.Add(Tx("Aplicado a " + Pl.N(Lista(dim["membros"]).Count(), "ligação", "ligações") + " do grupo.", 11.5, TemaPlant.BTexto3));
            veredito.Child = vs; sp.Children.Add(veredito);
            // verificações (as 8 de maior taxa): nome + item numa linha, Sd/Rd e a barra de taxa embaixo
            foreach (var v in Lista(r?["verificacoes"]).Where(x => !double.IsNaN(D(x, "taxa"))).OrderByDescending(x => D(x, "taxa")).Take(8))
            {
                double t = D(v, "taxa"); bool okv = t <= 1 + 1e-9;
                var bl = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                var nomeV = Tx(S(v, "nome"), 12, TemaPlant.BTexto1, FontWeights.SemiBold, true);
                bl.Children.Add(Dica(nomeV, S(v, "nome") + (S(v, "item") != "" ? "\nitem " + S(v, "item") : "")));
                var lin = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                var ct = CelTaxa(t, okv, 90); DockPanel.SetDock(ct, Dock.Right); lin.Children.Add(ct);
                var sd = Tx("Sd " + F(D(v, "solicitante"), 2) + " ≤ Rd " + F(D(v, "resistente"), 2, S(v, "unidade")) + (S(v, "item") != "" ? " · " + S(v, "item") : ""), 11, TemaPlant.BTexto3);
                sd.TextTrimming = TextTrimming.CharacterEllipsis; sd.VerticalAlignment = VerticalAlignment.Center;
                lin.Children.Add(sd); bl.Children.Add(lin);
                sp.Children.Add(bl);
            }
            foreach (var e in Lista(dim["erros"]).Take(3)) { var t = Tx("⚠  " + e, 11.5, TemaPlant.BAlertaClaro, null, true); t.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(t); }
            foreach (var a in Lista(r?["avisos"]).Take(3)) { var t = Tx("⚠  " + a, 11, TemaPlant.BTexto3, null, true); t.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(t); }
            return Cartao("Ligação que governa", sp);
        }

        // campos do tipo (GET /api/ligacoes/tipos), uma coluna; metro na tela em mm
        FrameworkElement Campos(List<JsonNode> campos, JsonObject alvo)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < campos.Count; i++)
            {
                var c = campos[i];
                string nome = S(c, "nome"), un = S(c, "unidade");
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                string rot = S(c, "rotulo"); if (rot == "" || rot == nome) rot = nome == "pega" ? "Comprimento de pega" : nome == "t_enchimento" ? "Chapas de enchimento" : nome;
                var l = Tx(rot, 12, TemaPlant.BTexto2); l.TextTrimming = TextTrimming.CharacterEllipsis; l.VerticalAlignment = VerticalAlignment.Center;
                Dica(l, rot + (S(c, "descricao") != "" ? "\n" + S(c, "descricao") : ""));
                Grid.SetRow(l, i); g.Children.Add(l);
                FrameworkElement ctl;
                var atual = alvo[nome] ?? c["padrao"];
                if (c["opcoes"] is JsonArray op)
                    ctl = Combo(op.Select(x => x.ToString()), atual?.ToString(), x => alvo[nome] = x, 150, S(c, "descricao"));
                else if (c["padrao"] is JsonValue pv && pv.TryGetValue(out bool _))
                {
                    bool v = atual is JsonValue av && av.TryGetValue(out bool bb) && bb;
                    ctl = Segmentado(new List<(string, bool, Action, string)>
                    { ("Sim", v, () => { alvo[nome] = true; MontarProjeto(); }, null), ("Não", !v, () => { alvo[nome] = false; MontarProjeto(); }, null) }, 26, 11.5);
                }
                else
                {
                    bool metro = un == "m";
                    double v = D(atual);
                    string txt = double.IsNaN(v) ? "" : metro ? Num.Fmt(v * 1000, Math.Abs(v * 1000 - Math.Round(v * 1000)) < 1e-6 ? 0 : 1) : Num.Fmt(v, Math.Abs(v - Math.Round(v)) < 1e-9 ? 0 : 2);
                    ctl = CaixaNum(txt, metro ? "mm" : un, 150, x => alvo[nome] = metro ? x / 1000 : x, S(c, "descricao"));
                    if (double.IsNaN(v)) ((Border)ctl).BorderBrush = TemaPlant.BAlertaBorda;
                }
                ctl.HorizontalAlignment = HorizontalAlignment.Left; ctl.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(ctl, i); Grid.SetColumn(ctl, 1); g.Children.Add(ctl);
            }
            return g;
        }
    }

    // ---------------------------------------------------------------- documento espacial: rotas das ligações
    public partial class DocumentoEspacial
    {
        // Grupos de ligações: mudam só `ligacoes` do JSON, então o cálculo do documento continua valendo.
        // Se o servidor perdeu o cálculo (o documento foi recolocado), recalcula antes de ler os grupos/dimensionar.
        public Task<JsonNode> Ligacoes(string metodo, string rota, JsonObject corpo = null) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            bool le = metodo == "GET" || rota.EndsWith("/dimensionar");
            if (le && Calculado && await Motor.Get("/api/calculo") == null) await Motor.Post("/api/calcular", new JsonObject());
            var r = await Motor.Chamar(metodo, rota, corpo);
            if (!le) { Json = (JsonObject)await Motor.Get("/api/modelo"); TemHistorico = true; }
            return r;
        });
    }

    // ---------------------------------------------------------------- janela principal: faixa, paleta, placa do grupo
    public partial class MainWindow
    {
        JanelaLigacoesGrupos janLig;

        // Ligações e bases › Gerenciar ligações (e Ctrl+K)
        internal void AbrirGerenciadorLigacoes()
        {
            var d = Esp;
            if (d == null) { MostrarToast("Gerenciar ligações: abra ou gere um modelo 3D (espacial) primeiro — Modelo › Gerar 3D ou Arquivo › Exemplos 3D.", true); return; }
            if (janLig != null && janLig.IsVisible) { janLig.Activate(); return; }
            janLig = NovoGerenciadorLigacoes(d);
            janLig.Show();
        }

        internal JanelaLigacoesGrupos NovoGerenciadorLigacoes(DocumentoEspacial d) => new JanelaLigacoesGrupos
        {
            Owner = this,
            Api = (m, r, c) => d.Ligacoes(m, r, c),
            Destacar = (nos, barras) => { if (Esp == d && vista3D != null) vista3D.Selecionar(nos, barras); },
            Calcular = async () => { if (Esp == d) await CalcularEspacial(); },
            Mudou = () => { if (Esp == d) { alterado = true; AtualizarTitulo(); } },
            AbrirPlaca = (g, dim) =>
            {
                var j = PlacaDoGrupo(d, g, dim, out No nb);
                if (j == null) return null;
                j.ShowDialog();
                return j.Mudou && nb.Placa != null ? DadosDePlaca(nb.Placa) : null;
            }
        };

        // JanelaPlaca (pórtico 2D do núcleo) montada só com o pilar que governa: base engastada em (0, 0), pilar vertical,
        // ResBarra com N, V, M da pior combinação na base (N = −F0, V = F1, M = −F2 em x = 0; PlacaCalc lê daí).
        // Eixo fraco quando o pior caso do motor foi com Mz.
        internal JanelaPlaca PlacaDoGrupo(DocumentoEspacial d, JsonNode g, JsonNode dim, out No nb)
        {
            nb = null;
            var gov = dim?["governante"];
            var mb = Lista(dim?["membros"]).FirstOrDefault(x => S(x, "barra") == S(gov, "barra") && S(x, "ponta") == S(gov, "ponta"));
            var e = mb?["esforcos"];
            if (e == null) { MostrarToast("Placa do grupo: sem esforços (calcule com F5).", true); return null; }
            bool fraco = S(dim["resultado"], "eixo").StartsWith("fraco");
            string sec = (string)d.Json["barras"]?[S(gov, "barra")]?["secao"] ?? "";
            var s2d = Biblioteca.AcharSecao(sec) ?? Biblioteca.AcharSecao(sec + ".0");   // motor "W360x51" = núcleo "W 360 x 51,0"
            if (s2d == null) { MostrarToast("Placa do grupo: perfil \"" + sec + "\" não está no catálogo do núcleo.", true); return null; }
            sec = s2d.Nome;
            double N = D(e, "N"), V = Math.Sqrt(D(e, "Vy") * D(e, "Vy") + D(e, "Vz") * D(e, "Vz")), M = fraco ? D(e, "Mz") : D(e, "My");
            var m = new Modelo { Seguranca = 2, Eletrodo = "E70" };
            nb = new No { Id = 1, X = 0, Y = 0, ApoioX = true, ApoioY = true, ApoioGiro = true, Placa = PlacaDeDados(g?["projeto"]?["dados"]) };
            m.Nos.Add(nb); m.Nos.Add(new No { Id = 2, X = 0, Y = 3 });
            var b = new Barra { Id = 1, N1 = 1, N2 = 2, Secao = sec, Dono = m, EixoFraco = fraco };
            b.Res = new ResBarra { L = 3, C = 0, S = 1, EA = 1, EI = 1 };
            b.Res.F[0] = -N; b.Res.F[1] = V; b.Res.F[2] = -M; b.Res.F[3] = N; b.Res.F[4] = -V; b.Res.F[5] = M - 3 * V;
            m.Barras.Add(b); m.ProxId = 3;
            var j = new JanelaPlaca(m, nb) { Owner = this };
            j.Title += " — grupo " + S(g, "nome") + " · pior: " + S(gov, "barra") + " (" + S(gov, "combinacao") + ")";
            return j;
        }

        static PlacaBase PlacaDeDados(JsonNode d)
        {
            double Nn(string k, double pad) { double v = D(d, k); return double.IsNaN(v) ? pad : v; }
            string Sx(string k, string pad) { string s = S(d, k); return s == "" ? pad : s; }
            bool Bx(string k, bool pad) => d?[k] is JsonValue v && v.TryGetValue(out bool x) ? x : pad;
            return new PlacaBase
            {
                H = Nn("H", .4) * 1000, B = Nn("B", .3) * 1000, Esp = Nn("esp", .019) * 1000, Borda = Nn("borda", .05) * 1000, Sobra = Nn("sobra", .1) * 1000,
                PeloPerfil = Bx("pelo_perfil", false), Fileiras = (int)Nn("fileiras", 2), PorFileira = (int)Nn("por_fileira", 2), Diametro = Nn("diametro_mm", 16),
                Classe = Sx("classe", "8.8"), RoscaNoCorte = Bx("rosca_no_corte", true), PivoNaMesa = Bx("pivo_na_mesa", false),
                FyChapa = Nn("fy_chapa", 250000) / 1000, Fck = Nn("fck", 20000) / 1000, Seguranca = Nn("seguranca", 0), Chumbador = Sx("chumbador", ""),
                ClasseBarra = Sx("classe_barra", "4.6"), TipoCarga = Sx("tipo_carga", ""), Risco = Sx("risco", ""), CoefChumbador = Nn("coef_chumbador", 0),
                Solda = Nn("solda", 0) * 1000, Eletrodo = Sx("eletrodo", "E70")
            };
        }

        static JsonObject DadosDePlaca(PlacaBase p) => new JsonObject
        {
            ["H"] = p.H / 1000, ["B"] = p.B / 1000, ["esp"] = p.Esp / 1000, ["borda"] = p.Borda / 1000, ["sobra"] = p.Sobra / 1000, ["pelo_perfil"] = p.PeloPerfil,
            ["fileiras"] = p.Fileiras, ["por_fileira"] = p.PorFileira, ["diametro_mm"] = p.Diametro, ["classe"] = p.Classe ?? "8.8", ["rosca_no_corte"] = p.RoscaNoCorte,
            ["pivo_na_mesa"] = p.PivoNaMesa, ["fy_chapa"] = p.FyChapa * 1000, ["fck"] = p.Fck * 1000, ["seguranca"] = p.Seguranca, ["chumbador"] = p.Chumbador ?? "",
            ["classe_barra"] = p.ClasseBarra ?? "4.6", ["tipo_carga"] = p.TipoCarga ?? "", ["risco"] = p.Risco ?? "", ["coef_chumbador"] = p.CoefChumbador,
            ["solda"] = p.Solda / 1000, ["eletrodo"] = string.IsNullOrEmpty(p.Eletrodo) ? "E70" : p.Eletrodo
        };
    }

    // ---------------------------------------------------------------- capturas 49) gerenciador de ligações (motor real)
    public static partial class Capturas
    {
        // Galpão do motor calculado; o gerenciador com os grupos; o grupo das bases dimensionado (e a placa da pior base);
        // um grupo de viga–pilar com chapa simples dimensionado. Só elas: ESTRUCTTO_CAPTURAS=ligacoes.
        static void CapturasLigacoes(MainWindow w)
        {
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Exemplo("galpao_aco"), "exemplo"));
            var d = w.EspacialAtivo ?? throw new InvalidOperationException("galpão não abriu");
            Esperar(w.CalcularEspacial());
            EsperarIdle();
            w.vista3D.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "49-ligacoes-galpao-calculado");

            var j = w.NovoGerenciadorLigacoes(d);
            j.Width = 1320; j.Height = 780;
            PrepararDialogoForaDaTela(j);
            j.Show(); EsperarIdle();
            Esperar(j.Carregar(true));
            EsperarIdle();
            if (d.Json["ligacoes"]?["grupos"] is not JsonObject gs || gs.Count < 5) throw new InvalidOperationException("ligações: esperado ≥ 5 grupos no galpão");
            CapturarDialogo(j, "49-ligacoes-grupos.png");
            CapturarPrincipal(w, "49-ligacoes-destaque-3d");

            string base_ = gs.First(kv => (string)kv.Value["tipo"] == "base").Key;
            j.Escolher(base_); EsperarIdle();
            Esperar(j.DimensionarSelecionado()); EsperarIdle();
            CapturarDialogo(j, "49-ligacoes-base-dimensionada.png");
            var dimBase = Esperar2(d.Ligacoes("POST", "/api/ligacoes/grupos/" + base_ + "/dimensionar"));
            var gB = Esperar2(d.Ligacoes("GET", "/api/ligacoes/grupos")).AsArray().First(x => (string)x["id"] == base_);
            var jp = w.PlacaDoGrupo(d, gB, dimBase, out _);
            if (jp != null)
            {
                PrepararDialogoForaDaTela(jp);
                jp.Show(); EsperarIdle();
                CapturarDialogo(jp, "49-ligacoes-placa-pior-base.png");
                jp.Close();
            }

            // viga–pilar: chapa simples dimensionada pela pior
            string vp = gs.First(kv => ((string)kv.Value["tipo"]).StartsWith("viga_pilar")).Key;
            Esperar(d.Ligacoes("POST", "/api/ligacoes/grupos/editar", new JsonObject
            {
                ["acao"] = "projeto", ["grupo"] = vp, ["tipo_ligacao"] = "chapa_simples",
                ["dados"] = new JsonObject { ["h"] = 0.23, ["t"] = 0.0095, ["d_mm"] = 19.05, ["n"] = 3, ["s"] = 0.075, ["e"] = 0.06 }
            }));
            Esperar(j.Carregar(false));
            j.Escolher(vp); EsperarIdle();
            Esperar(j.DimensionarSelecionado()); EsperarIdle();
            CapturarDialogo(j, "49-ligacoes-viga-pilar-dimensionada.png");
            j.Close();
            w.FecharEspaciaisSemPerguntar();
            EsperarIdle();
        }

        static JsonNode Esperar2(Task<JsonNode> t) { Esperar(t); return t.Result; }
    }
}
