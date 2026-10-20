// Host da Vista3D na janela principal: alternância, Realista e Modelo › Desenhar 3D (docs/MODELO-ESPACIAL.md).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Text.Json.Nodes;
using Portico;
using Material = System.Windows.Media.Media3D.Material;
using F = Fluent;


namespace NovoFtool
{
    // Alternância 2D/3D na janela principal (botão "3D" da aba Vista).
    public partial class MainWindow
    {
        internal Vista3D vista3D;
        Fluent.ToggleButton tgRealista;
        public bool Em3D { get { return vista3D != null && vista3D.Visibility == Visibility.Visible; } }

        // Botão "Realista" (Vista › Navegar): perfis reais com sombra ou vista analítica; a escolha fica em Config.
        public void DefinirRealista(bool ligar)
        {
            Config.Realista3D = ligar;
            if (tgRealista != null) tgRealista.IsChecked = ligar;
            if (vista3D != null) { vista3D.Realista = ligar; vista3D.Reconstruir(); }
            if (ligar && !Em3D) MostrarVista3D(true);
        }

        // Forma real da seção: do modelo do motor (props do JSON) ou do catálogo do Estructto (perfis-*.csv).
        Perfil3D PerfilDaSecao(string nome)
        {
            if (Esp?.Json?["secoes"]?[nome] is JsonObject s) return Perfil3D.DoMotor(s);
            return Perfil3D.DoCatalogo(Biblioteca.AcharSecao(nome));
        }

        public void AlternarVista3D() { MostrarVista3D(!Em3D); }

        public void MostrarVista3D(bool ligar)
        {
            AtualizarFaixa3D();
            if (ligar && vista3D == null)
            {
                vista3D = new Vista3D { Visibility = Visibility.Collapsed, MostrarLegenda = false, Realista = Config.Realista3D, Perfil = PerfilDaSecao, MostrarCargas = tgCargas?.IsChecked != false };
                GridVista.Children.Insert(GridVista.Children.IndexOf(Vista) + 1, vista3D);   // acima do 2D, abaixo do HUD e avisos
                vista3D.Escolheu += o =>
                {
                    if (Esp != null) return;   // modelo espacial: DocumentoEspacial.cs trata a escolha
                    object alvo = o is No3D n ? n.Origem : o is Barra3D b ? b.Origem : null;
                    Vista.Sel.Clear();
                    if (alvo != null) Vista.Sel.Add(alvo);
                    SelecaoMudou();
                };
                LigarDesenho3D();
            }
            if (vista3D == null || !ligar) return;   // não existe mais vista 2D: sempre 3D
            bool primeira = ligar && vista3D.Visibility != Visibility.Visible;
            vista3D.Visibility = Visibility.Visible;
            Vista.Visibility = Visibility.Hidden;   // o controle 2D (Vista.cs) fica só como guarda do estado do HUD (diagrama e escala)
            if (ligar) { Atualizar3D(primeira); vista3D.Focus(); } else Vista.Focus();
        }

        // Recarrega a vista 3D a partir do modelo 2D e do diagrama ativo (chamado depois de cada cálculo).
        internal void Atualizar3D(bool enquadrar = false)
        {
            if (Esp != null) { AtualizarEspacial3D(enquadrar); return; }
            if (!Em3D) return;
            Diagrama d = Vista.Diag;
            vista3D.Diagrama = !Vista.Resolvido ? Diagrama3D.Nenhum : d == Diagrama.Normal ? Diagrama3D.N : d == Diagrama.Cortante ? Diagrama3D.V : d == Diagrama.Momento ? Diagrama3D.M : Diagrama3D.Nenhum;
            vista3D.MostrarDeformada = Vista.Resolvido && d == Diagrama.Deformada;
            vista3D.MostrarAproveitamento = Vista.Resolvido && d == Diagrama.Verificacao;
            vista3D.EscalaDeformada = Vista.EscalaDef; vista3D.EscalaDiagrama = Vista.EscalaDiag;
            vista3D.Selecionado = Vista.Sel.Count == 1 ? Vista.Sel[0] : null;
            vista3D.Carregar(Modelo3D.De(M, Vista.Resolvido ? Vista.Verificar : null), enquadrar);
        }

        // ================= desenhar no modelo espacial (Modelo › Desenhar 3D) =================

        F.RibbonGroupBox grpDesenho3D, grpDesenho2D;
        readonly Dictionary<Ferramenta3D, F.ToggleButton> btnFerr3D = new Dictionary<Ferramenta3D, F.ToggleButton>();

        // Chamado uma vez ao montar a faixa: grupo "Desenhar 3D" ao lado do "Desenhar" 2D (só um dos dois aparece) e a paleta.
        internal void MontarDesenho3D()
        {
            grpDesenho2D = abaModelo.Groups.FirstOrDefault(g => (g.Header as string) == "Desenhar");
            grpDesenho3D = new F.RibbonGroupBox { Header = "Desenhar 3D", Visibility = Visibility.Collapsed };
            foreach (var (f, ic, txt, nome, dica, atalho) in new[]
            {
                (Ferramenta3D.Selecionar, "sel", "Selecionar", "Selecionar 3D", "Clique escolhe; arraste da esquerda p/ direita = janela, da direita p/ esquerda = cruzamento. Shift soma, Ctrl tira, Esc limpa.", "Esc"),
                (Ferramenta3D.Barra, "barra", "Barra", "Barra 3D", "Clique os pontos em sequência no plano de trabalho (atrai nós, meios e grade; F8/Shift ortogonal). Digite o comprimento, Tab o ângulo, ou x,y,z / @dx,dy,dz. Esc ou botão direito termina.", null),
                (Ferramenta3D.Pilar, "3d", "Pilar", "Pilar 3D", "Clique o ponto no plano de trabalho: num nível com outro abaixo, o pilar desce até ele; no nível mais baixo, sobe até o próximo (3 m sem níveis).", null),
            })
            {
                var b = new F.ToggleButton { Header = txt, LargeIcon = Icone(ic, 32), Icon = Icone(ic, 16), Size = F.RibbonControlSize.Large, ToolTip = Dica(nome, dica, atalho) };
                Tamanho(b, false);
                b.Click += (s, e) => UsarFerramenta3D(f);
                Reg(ic, nome, atalho, dica, () => UsarFerramenta3D(f));
                btnFerr3D[f] = b; grpDesenho3D.Items.Add(b);
            }
            MontarSecaoDesenho3D();
            int i = grpDesenho2D != null ? abaModelo.Groups.IndexOf(grpDesenho2D) + 1 : 1;
            abaModelo.Groups.Insert(Math.Min(i, abaModelo.Groups.Count), grpDesenho3D);
            var gerar = abaModelo.Groups.FirstOrDefault(g => (g.Header as string) == "Gerar");
            gerar?.Items.Add(Botao("3d", "Modelo 3D vazio", NovoEspacialVazio, "Modelo espacial em branco no motor Python, para desenhar com Barra e Pilar (Desenhar 3D).", null, true));
        }

        internal void NovoEspacialVazio() => _ = AbrirEspacial(() => DocumentoEspacial.Vazio("Modelo 3D"), "criando modelo vazio");

        internal void UsarFerramenta3D(Ferramenta3D f)
        {
            if (Esp == null || vista3D == null) { MostrarToast("Desenhar 3D vale no modelo espacial: Modelo › Gerar 3D ou Modelo 3D vazio.", true); SincronizarFerr3D(); return; }
            vista3D.Ferramenta = vista3D.Ferramenta == f && f != Ferramenta3D.Selecionar ? Ferramenta3D.Navegar : f;
            vista3D.Focus();
        }

        void SincronizarFerr3D()
        {
            foreach (var kv in btnFerr3D) kv.Value.IsChecked = vista3D != null && Esp != null && vista3D.Ferramenta == kv.Key;
        }

        void AtualizarFaixa3D()
        {
            if (grpDesenho3D == null) return;
            bool esp = Esp != null;
            grpDesenho3D.Visibility = esp ? Visibility.Visible : Visibility.Collapsed;
            if (grpDesenho2D != null) grpDesenho2D.Visibility = esp ? Visibility.Collapsed : Visibility.Visible;
            if (!esp && vista3D != null && vista3D.Ferramenta != Ferramenta3D.Navegar) vista3D.Ferramenta = Ferramenta3D.Navegar;   // 2D: vista 3D só de leitura
            SincronizarFerr3D();
        }

        void LigarDesenho3D()
        {
            vista3D.CriarBarras = DesenharEsp;
            vista3D.AlturaAndar = AlturaAndarEsp;
            vista3D.FerramentaMudou += SincronizarFerr3D;
        }

        // Teclas da ferramenta 3D antes dos atalhos da janela (Esc, Enter, Tab, Backspace, F8). Acoes.cs › Tecla.
        internal bool Tecla3D(Key k) => Esp != null && vista3D != null && vista3D.IsVisible && vista3D.Tecla(k);

        async Task DesenharEsp(IList<(Point3D A, Point3D B)> segs)
        {
            var d = Esp;
            if (d == null) return;
            try
            {
                var ids = await d.DesenharBarras(segs, SecaoDesenho3D(d), ApoioBase3D);
                if (Esp == d) DepoisDeMudarEspacial(ids.Count == 1 ? "Barra " + ids[0] + " desenhada." : Pl.N(ids.Count, "barra desenhada", "barras desenhadas") + ".");
            }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; Log(StatusMsg.Text); }
            AvisarHistorico(d);
        }

        // Altura do pilar pelos níveis (Niveis.Lista: pavimentos + Térreo): clicado num nível com outro abaixo, desce até ele
        // (< 0: o andar do nível ativo, o que a vista isolada mostra); senão sobe até o nível de cima; no topo sem nível, a do
        // último andar; sem níveis, 3 m.
        double AlturaAndarEsp(double z)
        {
            if (Esp == null) return 3;
            var l = Niveis.Lista(Esp.Json);
            double? abaixo = Niveis.Abaixo(l, z), acima = Niveis.Acima(l, z);
            if (abaixo != null && l.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol)) return abaixo.Value - z;
            if (acima != null) return acima.Value - z;
            return l.Count >= 2 ? l[^1].Z - l[^2].Z : 3;
        }

        // ---------- Desenhar 3D › Seção e Apoio na base ----------
        F.ComboBox cbSecao3D; F.ToggleButton tgApoioBase3D;
        string secao3D;                                  // escolhida no seletor (null = a da última barra)
        bool montandoSecao3D;
        const string DoCatalogo = "Do catálogo…";
        internal bool ApoioBase3D { get => tgApoioBase3D?.IsChecked == true; set { if (tgApoioBase3D != null) tgApoioBase3D.IsChecked = value; } }

        // Seção das barras novas: a do seletor, se ainda estiver no modelo; senão a da última barra (null = DesenharBarras decide).
        string SecaoDesenho3D(DocumentoEspacial d) =>
            secao3D != null && d.Json["secoes"]?[secao3D] != null ? secao3D : (string)(d.Json["barras"] as JsonObject)?.LastOrDefault().Value?["secao"];

        void MontarSecaoDesenho3D()
        {
            cbSecao3D = new F.ComboBox { Header = "Seção", IsEditable = false, Width = 190,
                ToolTip = Dica("Seção", "Seção das barras e pilares desenhados: as do modelo, ou um perfil do catálogo do motor (entra no modelo ao escolher).") };
            cbSecao3D.DropDownOpened += (s, e) => ListarSecoes3D();
            cbSecao3D.SelectionChanged += (s, e) =>
            {
                if (montandoSecao3D || !(cbSecao3D.SelectedItem is string v)) return;
                if (v == DoCatalogo) { ListarSecoes3D(); _ = EscolherDoCatalogo3D(); return; }
                secao3D = v;
            };
            tgApoioBase3D = new F.ToggleButton { Header = "Apoio na base", Icon = Icone("engaste", 16), LargeIcon = Icone("engaste", 32), Size = F.RibbonControlSize.Middle,
                ToolTip = Dica("Apoio na base", "Nó novo desenhado no nível mais baixo nasce engastado (6 GL travados).") };
            grpDesenho3D.Items.Add(cbSecao3D);
            grpDesenho3D.Items.Add(tgApoioBase3D);
        }

        // Itens: seções do modelo + "Do catálogo…"; mostra a que vale agora.
        internal void ListarSecoes3D()
        {
            if (cbSecao3D == null) return;
            montandoSecao3D = true;
            cbSecao3D.Items.Clear();
            var d = Esp;
            if (d != null) foreach (var kv in d.Json["secoes"] as JsonObject ?? new JsonObject()) cbSecao3D.Items.Add(kv.Key);
            cbSecao3D.Items.Add(DoCatalogo);
            string atual = d == null ? null : SecaoDesenho3D(d);
            cbSecao3D.SelectedItem = atual != null && cbSecao3D.Items.Contains(atual) ? atual : null;
            montandoSecao3D = false;
        }

        async Task EscolherDoCatalogo3D()
        {
            if (Esp == null) return;
            JsonNode lista;
            try { lista = await Motor.Get("/api/catalogo"); }
            catch (Exception ex) { StatusMsg.Text = "⚠ Motor: " + ex.Message; return; }
            var j = new JanelaCatalogo(lista, nome => Motor.Get("/api/catalogo/" + Uri.EscapeDataString(nome))) { Owner = this };
            j.PerfilEscolhido += nome => _ = UsarSecao3D(nome);
            j.Show();
        }

        // Escolhe a seção do desenho; perfil do catálogo fora do modelo entra pelo motor (POST /api/modelo/secao, um desfazer).
        internal async Task UsarSecao3D(string nome)
        {
            var d = Esp;
            if (d == null) return;
            if (d.Json["secoes"]?[nome] == null) await Espacial(dd => dd.GarantirSecao(nome), "Seção " + nome + " do catálogo entrou no modelo.");
            if (Esp?.Json["secoes"]?[nome] == null) return;
            secao3D = nome;
            ListarSecoes3D();
        }
    }

    public partial class DocumentoEspacial
    {
        // Modelo espacial em branco (para desenhar na Vista3D).
        public static Task<DocumentoEspacial> Vazio(string titulo) => Criar(() => Motor.Post("/api/modelo/novo", new JsonObject { ["nome"] = titulo }), titulo, null);

        // Barras desenhadas na Vista3D: um POST /api/modelo/lote (atômico, um passo de desfazer). Ponta a até 1 mm de um nó
        // existente reaproveita o nó. Seção = a do seletor (Desenhar 3D › Seção, que já põe o perfil do catálogo no modelo; se
        // faltar, entra no mesmo lote); sem seção, a primeira do modelo ou W310x38.7. Material = o da última barra
        // (ou A-572 GR.50 no mesmo lote). apoioBase: nó novo na cota do nível mais baixo nasce engastado.
        // Devolve os ids das barras criadas.
        // ponytail: material não segue a seção (perfil de concreto do catálogo herda o aço da última barra); trocar em Propriedades em lote.
        public Task<List<string>> DesenharBarras(IList<(Point3D A, Point3D B)> segs, string secao = null, bool apoioBase = false) => Motor.Exclusivo(async () =>
        {
            await Garantir();
            var nos = Json["nos"].AsObject();
            var itens = new JsonArray(); var novos = new List<(string Id, Point3D P)>();
            var ult = (Json["barras"] as JsonObject)?.LastOrDefault().Value;
            secao ??= (Json["secoes"] as JsonObject)?.Select(x => x.Key).FirstOrDefault() ?? "W310x38.7";
            string material = (string)ult?["material"] ?? (Json["materiais"] as JsonObject)?.Select(x => x.Key).FirstOrDefault();
            double zBase = Niveis.Lista(Json).Min(n => n.Z);
            if (Json["secoes"]?[secao] == null) itens.Add(new JsonObject { ["tipo"] = "secao", ["dados"] = new JsonObject { ["perfil"] = secao } });
            if (material == null) { material = "A-572 GR.50"; itens.Add(new JsonObject { ["tipo"] = "material", ["dados"] = new JsonObject { ["aco"] = material } }); }
            int k = 0;
            string No(Point3D p)
            {
                p = new Point3D(Math.Round(p.X, 6), Math.Round(p.Y, 6), Math.Round(p.Z, 6));
                foreach (var kv in nos)
                    if (Math.Abs((double)kv.Value["x"] - p.X) < 1e-3 && Math.Abs((double)kv.Value["y"] - p.Y) < 1e-3 && Math.Abs((double)kv.Value["z"] - p.Z) < 1e-3) return kv.Key;
                foreach (var (id, q) in novos) if ((q - p).Length < 1e-3) return id;
                string nid;
                do nid = "n" + (nos.Count + ++k); while (nos.ContainsKey(nid));
                var no = new JsonObject { ["id"] = nid, ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
                if (apoioBase && Math.Abs(p.Z - zBase) <= Niveis.Tol) no["apoio"] = new JsonArray(true, true, true, true, true, true);
                itens.Add(new JsonObject { ["tipo"] = "no", ["dados"] = no });
                novos.Add((nid, p));
                return nid;
            }
            foreach (var (a, b) in segs)
            {
                string ni = No(a), nj = No(b);
                if (ni == nj) continue;
                itens.Add(new JsonObject { ["tipo"] = "barra", ["dados"] = new JsonObject { ["ni"] = ni, ["nj"] = nj, ["secao"] = secao, ["material"] = material } });
            }
            var r = await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });
            TemHistorico = true;
            await Recarregar();
            return r?["criados"]?.AsArray().Where(c => (string)c["tipo"] == "barra").Select(c => (string)c["id"]).ToList() ?? new List<string>();
        });
    }
}
