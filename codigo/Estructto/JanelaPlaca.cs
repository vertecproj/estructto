// Janela da placa de base (E5): veredito numa linha no topo + 3 chips (Fixação / Chapa / Solda),
// desenho à esquerda (Planta | 3D | Forças | Ficha | Cálculo) e, à direita, 3 passos numerados com o essencial visível
// e o resto em "mais". Rodapé: Dimensionar tudo, Aplicar também em…, Cancelar / Aplicar.
// O cálculo é o do MOTOR, método NBR (aco/placa_base.py por /api/placa_base/verificar e /dimensionar; PlacaMotor.cs):
// bloco de compressão, chapa CBCA (conferência Pfeil), chumbador NBR 8800 6.3.3 + ACI 318 cap. 17, na pior combinação ELU
// de todos os membros e nos dois eixos. O Nucleo.cs (PlacaCalc, pivô na borda, n global) só aparece recolhido em
// "Comparar com método legado" (planilha do guincho). O desenho usa a geometria do PlacaCalc com as forças do motor.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Portico;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaPlaca : JanelaEscura
    {
        public bool Mudou;
        readonly Modelo m; readonly No n;
        readonly PlacaBase original, edit;
        PlacaBase refInicial;
        readonly PlacaEdit pe;
        readonly PlanoPlaca plano = new PlanoPlaca();
        readonly Placa3D vista3d = new Placa3D();
        readonly Border cabecalho = new Border();
        readonly Border areaAba = new Border();
        readonly Border hostSeg = new Border();
        readonly TextBlock lblDicaVista = new TextBlock();
        readonly StackPanel colDir = new StackPanel();
        readonly ScrollViewer svDir = new ScrollViewer();
        readonly DockPanel rodape = new DockPanel();
        readonly Grid camadaRaiz = new Grid();
        readonly Grid overlayTrocar = new Grid { Visibility = Visibility.Collapsed };
        readonly Grid overlayAplicarEm = new Grid { Visibility = Visibility.Collapsed };
        readonly Grid overlaySugerir = new Grid { Visibility = Visibility.Collapsed };
        readonly Canvas canvasSugerir = new Canvas();
        readonly StackPanel listaTrocar = new StackPanel();
        readonly StackPanel listaAplicarEm = new StackPanel();
        readonly TextBlock lblAplicarEm = new TextBlock();

        // Motor: quem abre a janela liga a rota (síncrona) e os membros {barra, ponta} cujos esforços ELU valem (o pé ou o grupo)
        public Func<string, JsonObject, JsonNode> Api;
        public readonly JsonArray Membros = new JsonArray();
        public string CombLegado;                                      // combinação crítica do método legado (comparação)
        // "Aplicar também em": outros pés selecionados (nó, membro, já tem placa) e o grupo de ligações das bases do nó
        public readonly List<(string No, JsonObject Membro, bool TemPlaca)> Outros = new List<(string, JsonObject, bool)>();
        public (string Id, string Nome, List<string> Nos)? Grupo;
        public readonly List<string> AplicarEm = new List<string>();  // nós que recebem a mesma placa ao Aplicar
        public bool AplicarNoGrupo;                                    // gravar também como projeto do grupo de ligações
        readonly HashSet<int> marcados = new HashSet<int>();           // índices em Outros
        ResNbr nb = ResNbr.ComErro("");                                // último resultado do motor (governante)
        ResPlaca leg;                                                  // método legado (só comparação e geometria do desenho)
        List<(bool FixOk, double FixTaxa, bool Ok, double Taxa, double TNec, string Criterio)> regua = new List<(bool, double, bool, double, double, string)>();
        readonly Dictionary<string, JsonNode> cache = new Dictionary<string, JsonNode>();
        bool verLegado;

        int abaAtiva;                  // 0 = Planta, 1 = 3D, 2 = Forças, 3 = Ficha, 4 = Cálculo
        bool prefFuroMenor;            // false = Mais leve, true = Furo menor
        bool mostrarOutroArranjo;      // campos para digitar Fileiras × Por fileira
        bool maisFix, maisCh, maisSd;  // "mais" aberto em cada passo
        bool semSobreporCones;
        string filtroBuscaCh = "";
        int filtroTipoCh;              // 0 = Todos, 1 = Mecânico, 2 = Químico
        bool verTodosNaoServem;
        string chumbAntes = "";
        string msgDim = "";
        StackPanel btsFiltroTipoCh;
        Border cardFix, cardCh, cardSd, btSugerir;
        readonly DispatcherTimer tmRealce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        Border realcado;

        const double LargDir = 440;    // coluna dos 3 passos
        const double LargRot = 66;     // coluna dos rótulos dentro dos passos (alinha os campos dos 3 passos)

        // Solda do pilar na placa com os esforços da combinação/eixo que governa a solda no motor; perna digitada ou, com 0, a mínima.
        ResSolda SoldaPilar(ResPlaca r)
        {
            var p = nb.Solda;
            if (!r.TemEsforcos || r.Sec == null || !nb.Valido || double.IsNaN(p.N)) return null;
            bool deitado = r.Deitado != p.Fraco;
            double h = edit.Solda > 0 ? edit.Solda : Solda.Minima(r.Sec, deitado, p.N, p.V, p.M, edit.Eletrodo, edit.Esp);
            if (h <= 0) h = 25;
            return Solda.Calcular(r.Sec, deitado, p.N, p.V, p.M, h, edit.Eletrodo, edit.Esp);
        }

        static string NomeCurtoCh(Chumbador c)
        {
            if (c == null) return "—";
            return c.Linha.Replace("Walsywa ", "") + " " + c.Pol + " hef " + c.Hef.ToString("0");
        }

        static string FmtEsp(double e)
        {
            if (Math.Abs(e - Math.Round(e)) < 1e-6) return Num.Fmt(e, 0);
            if (Math.Abs(e * 10 - Math.Round(e * 10)) < 1e-6) return Num.Fmt(e, 1);
            return Num.Fmt(e, 2);
        }

        static string FmtN(double v, int c = 2) { return double.IsInfinity(v) ? "∞" : Num.Fmt(v, c); }

        string DescFixAtual(ResPlaca r)
        {
            return r.Ch != null ? r.Paraf.Count + " × " + NomeCurtoCh(r.Ch) : r.Paraf.Count + " × Ø" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe + " pré-concretado";
        }

        // ---------- motor (/api/placa_base/*) ----------
        JsonObject Corpo(PlacaBase pl, JsonArray membros = null) => new JsonObject { ["dados"] = PlacaMotor.Dados(pl), ["membros"] = (membros ?? Membros).DeepClone() };

        JsonNode Chamar(string rota, JsonObject corpo)
        {
            if (Api == null || Membros.Count == 0) throw new InvalidOperationException("motor de cálculo não ligado a esta janela");
            string k = rota + corpo.ToJsonString();
            if (!cache.TryGetValue(k, out var j)) { j = Api(rota, corpo); cache[k] = j; }
            return j;
        }

        // Verificação pelo motor; variantes (régua, trocar, sugerir) voltam resumidas, com triagem pela fixação.
        (ResNbr R, List<(bool FixOk, double FixTaxa, bool Ok, double Taxa, double TNec, string Criterio)> V) Verificar(PlacaBase pl, IList<PlacaBase> variantes = null)
        {
            var vazio = new List<(bool, double, bool, double, double, string)>();
            if (Api == null || Membros.Count == 0) return (ResNbr.ComErro("Motor de cálculo não ligado a esta janela: sem verificação."), vazio);
            var corpo = Corpo(pl);
            if (variantes != null && variantes.Count > 0) { corpo["variantes"] = new JsonArray(variantes.Select(v => (JsonNode)PlacaMotor.Dados(v)).ToArray()); corpo["triagem"] = "fixacao"; }
            try { var j = Chamar("/api/placa_base/verificar", corpo); return (ResNbr.De(j), Lista(j["variantes"]).Select(ResNbr.Resumo).ToList()); }
            catch (Exception ex) { return (ResNbr.ComErro("Motor: " + PlacaMotor.Mensagem(ex)), vazio); }
        }

        List<(bool FixOk, double FixTaxa, bool Ok, double Taxa, double TNec, string Criterio)> Variantes(IList<PlacaBase> vs) =>
            vs.Count == 0 ? new List<(bool, double, bool, double, double, string)>() : Verificar(edit, vs).V;

        // Variantes do passo 1: diâmetros (pré-concretado) ou os chumbadores do mesmo tipo (catálogo), para a régua e o "menor que passa".
        List<(PlacaBase pl, Chumbador ch)> VariantesPasso1()
        {
            var l = new List<(PlacaBase, Chumbador)>();
            var chAt = Chumbadores.Achar(edit.Chumbador);
            if (chAt == null) foreach (double d in Parafusos.D) { var p = edit.Copiar(); p.Diametro = d; l.Add((p, null)); }
            else foreach (var c in Chumbadores.Lista.Where(c => c.Quimico == chAt.Quimico).OrderBy(c => c.Rosca).ThenBy(c => c.Hef)) { var p = edit.Copiar(); p.Chumbador = c.Nome; l.Add((p, c)); }
            return l;
        }

        // Dimensiona pelo motor (pior combinação ELU dos membros, nos dois eixos) segundo a preferência ("Mais leve" ou "Furo menor").
        void Dimensionar(JsonArray membros = null)
        {
            JsonNode res;
            try { var corpo = Corpo(edit, membros); corpo["preferencia"] = prefFuroMenor ? "furo" : "leve"; res = Chamar("/api/placa_base/dimensionar", corpo); }
            catch (Exception ex) { msgDim = "Dimensionar: " + PlacaMotor.Mensagem(ex); return; }
            if (!Bool(res, "achou")) { msgDim = S(res, "descricao"); return; }
            var p = PlacaMotor.Placa(res["dados"]);
            edit.Chumbador = p.Chumbador; edit.Diametro = p.Diametro; edit.Classe = p.Classe; edit.Fileiras = p.Fileiras; edit.PorFileira = p.PorFileira;
            edit.Esp = p.Esp; edit.Solda = p.Solda; edit.Eletrodo = p.Eletrodo;
            int nm = (membros ?? Membros).Count;
            msgDim = "Escolhido pelo motor (NBR, pior combinação" + (nm > 1 ? " de " + nm + " bases" : "") + "): " + S(res, "descricao") + ".";
        }

        // Resultado do legado com as forças, a fixação e a chapa do motor: é o que os desenhos e os passos mostram.
        static ResPlaca Vista(ResPlaca leg, ResNbr nb)
        {
            var v = leg.Copiar();
            if (!nb.Valido) { v.TemEsforcos = false; v.Erro = nb.Erro != "" ? nb.Erro : leg.Erro; return v; }
            v.TemEsforcos = true; v.Erro = "";
            v.N = nb.N; v.V = nb.V; v.M = nb.M; v.Paraf = nb.Paraf.Select(p => (double[])p.Clone()).ToList();
            v.Tmax = nb.Tmax; v.Fv = nb.Fv; v.Modelo = nb.Modelo + (nb.Fraco ? " · eixo fraco (Mz)" : ""); v.PivoY = double.NaN; v.EspNec = nb.TNec; v.OkMotor = nb.Fix.Ok; v.ApMotor = nb.Fix.Taxa;
            return v;
        }

        public JanelaPlaca(Modelo m, No n)
        {
            this.m = m; this.n = n;
            original = n.Placa;
            edit = n.Placa != null ? n.Placa.Copiar() : new PlacaBase { PeloPerfil = true, Sobra = 100, H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", FyChapa = 250 };
            n.Placa = edit;
            pe = new PlacaEdit(edit);
            chumbAntes = original != null && !string.IsNullOrEmpty(original.Chumbador) ? original.Chumbador : "";
            refInicial = (original ?? edit).Copiar();

            Title = "Placa de base — nó " + n.Id;
            Rect wa = SystemParameters.WorkArea;
            Width = Math.Max(1100, Math.Min(1440, wa.Width - 60));
            Height = Math.Max(680, Math.Min(900, wa.Height - 40));
            MinWidth = 1024; MinHeight = 640;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            plano.AoEditar = Recalcular;
            tmRealce.Tick += (s, e) => { tmRealce.Stop(); if (realcado != null) { realcado.BorderBrush = TemaPlant.BBordaSutil; realcado.BorderThickness = new Thickness(1); realcado = null; } };

            var raiz = new Grid();
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1) Topo: veredito + chips
            cabecalho.Background = TemaPlant.BSup1;
            cabecalho.BorderBrush = TemaPlant.BBordaSutil;
            cabecalho.BorderThickness = new Thickness(0, 0, 0, 1);
            cabecalho.Padding = new Thickness(16, 7, 16, 7);
            raiz.Children.Add(cabecalho);

            // 2) Corpo: desenho (esquerda) + passos (direita)
            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LargDir) });
            Grid.SetRow(corpo, 1);

            var cardDesenho = new Border
            {
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Margin = new Thickness(12, 10, 10, 10), ClipToBounds = true
            };
            var dpDes = new DockPanel();
            var topoDes = new DockPanel { Margin = new Thickness(10, 8, 12, 8) };
            lblDicaVista.FontSize = 11.5; lblDicaVista.Foreground = TemaPlant.BTexto3; lblDicaVista.VerticalAlignment = VerticalAlignment.Center;
            lblDicaVista.TextTrimming = TextTrimming.CharacterEllipsis; lblDicaVista.TextAlignment = TextAlignment.Right; lblDicaVista.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(hostSeg, Dock.Left);
            topoDes.Children.Add(hostSeg);
            topoDes.Children.Add(lblDicaVista);
            DockPanel.SetDock(topoDes, Dock.Top);
            dpDes.Children.Add(topoDes);
            areaAba.BorderBrush = TemaPlant.BBordaSutil; areaAba.BorderThickness = new Thickness(0, 1, 0, 0);
            dpDes.Children.Add(areaAba);
            cardDesenho.Child = dpDes;
            corpo.Children.Add(cardDesenho);

            var dirBorder = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0) };
            svDir.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            svDir.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            svDir.Padding = new Thickness(10, 8, 10, 8);
            svDir.Content = colDir;
            dirBorder.Child = svDir;
            Grid.SetColumn(dirBorder, 1);
            corpo.Children.Add(dirBorder);
            raiz.Children.Add(corpo);

            // 3) Rodapé
            var rod = new Border { Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 7, 14, 7), Child = rodape };
            Grid.SetRow(rod, 2);
            raiz.Children.Add(rod);

            camadaRaiz.Children.Add(raiz);
            MontarOverlayTrocar(); camadaRaiz.Children.Add(overlayTrocar);
            MontarOverlayAplicarEm(); camadaRaiz.Children.Add(overlayAplicarEm);
            var fundoSug = new Border { Background = Brushes.Transparent };
            fundoSug.MouseLeftButtonDown += (s, e) => { overlaySugerir.Visibility = Visibility.Collapsed; e.Handled = true; };
            overlaySugerir.Children.Add(fundoSug);
            overlaySugerir.Children.Add(canvasSugerir);
            camadaRaiz.Children.Add(overlaySugerir);
            Content = camadaRaiz;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (overlayTrocar.Visibility == Visibility.Visible || overlayAplicarEm.Visibility == Visibility.Visible || overlaySugerir.Visibility == Visibility.Visible)
                {
                    FecharFlutuantes();
                    e.Handled = true;
                }
            };
            Closing += (s, e) => { if (!Mudou) n.Placa = original; };
            bool iniciou = false;
            Loaded += (s, e) =>   // Api/Membros chegam depois do construtor: placa nova é dimensionada pelo motor aqui
            {
                if (original == null && !iniciou) { iniciou = true; Dimensionar(); refInicial = edit.Copiar(); }
                Recalcular();
            };
            Recalcular();
        }

        // ================= API usada pelo modo --capturas =================
        public void MostrarAba(int aba) { abaAtiva = Math.Max(0, Math.Min(4, aba)); FecharFlutuantes(); Recalcular(); }
        public void AbrirTrocar() { AbrirOverlayTrocar(); }
        public void FecharTrocar() { overlayTrocar.Visibility = Visibility.Collapsed; }
        public void AbrirSugerir() { MostrarSugerir(); }
        public void AbrirAplicarEm() { MostrarAplicarEm(); }
        public void FecharFlutuantes()
        {
            overlayTrocar.Visibility = Visibility.Collapsed;
            overlayAplicarEm.Visibility = Visibility.Collapsed;
            overlaySugerir.Visibility = Visibility.Collapsed;
        }
        public void UsarModoParafuso(bool parafuso)
        {
            FecharFlutuantes();
            msgDim = "";
            MudarTipo(parafuso ? 2 : 0);
            Recalcular();
        }
        public ResNbr ResultadoMotor => nb;      // capturas: números do motor
        public ResPlaca ResultadoLegado => leg;  // capturas: números do legado (comparação)
        public void MostrarLegado(bool ver) { verLegado = ver; Recalcular(); }
        public PlacaBase Placa => n.Placa;
        public void DimensionarTudo() { Dimensionar(); Recalcular(); }   // capturas
        public string Mensagem => msgDim;      // resultado ao fechar (null = removida)

        // ================= recálculo e montagem =================
        void Recalcular()
        {
            plano.FecharEditor(false);
            leg = PlacaCalc.Calcular(m, n);
            (nb, regua) = Verificar(edit, VariantesPasso1().Select(x => x.pl).ToList());
            ResPlaca r = Vista(leg, nb);
            var sd = SoldaPilar(r);
            plano.R = r; plano.P = edit; plano.Nbr = nb;
            vista3d.R = r; vista3d.P = edit; vista3d.Sd = sd;

            MontarCabecalho(r, sd);
            MontarVista(r, sd);

            colDir.Children.Clear();
            cardFix = MontarPassoFixacao(r);
            cardCh = MontarPassoChapa(r);
            cardSd = MontarPassoSolda(r, sd);
            colDir.Children.Add(cardFix);
            colDir.Children.Add(cardCh);
            colDir.Children.Add(cardSd);
            MontarMudancas(r);
            MontarLegado();

            MontarRodape(r);
            if (overlayTrocar.Visibility == Visibility.Visible) PreencherOverlayTrocar();
            if (overlayAplicarEm.Visibility == Visibility.Visible) PreencherAplicarEm();
        }

        bool FixOk(ResPlaca r) { return r.TemEsforcos && nb.Fix.Ok; }
        bool ChapaOk(ResPlaca r) { return r.TemEsforcos && nb.Chapa.Ok; }
        double ApChapa(ResPlaca r) { return nb.Chapa.Taxa; }

        // ---------- 1) topo: veredito numa linha + chips dos passos + avisos compactos (tudo do motor) ----------
        void MontarCabecalho(ResPlaca r, ResSolda sd)
        {
            var dp = new DockPanel { LastChildFill = true };
            var falhas = new List<string>();
            if (r.TemEsforcos)
            {
                if (!nb.Fix.Ok) falhas.Add("fixação (" + nb.Fix.Criterio + ")");
                if (!nb.Chapa.Ok) falhas.Add(nb.Chapa.Criterio.StartsWith("concreto") ? "concreto sob a placa" : "chapa (" + nb.Chapa.Criterio + ")");
                if (!nb.Solda.Ok) falhas.Add("solda (" + (nb.Solda.Criterio == "" ? "não verificada" : nb.Solda.Criterio) + ")");
            }

            // chips dos 3 passos (direita)
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (r.TemEsforcos)
            {
                chips.Children.Add(ChipPasso(1, "Fixação", nb.Fix.Taxa, nb.Fix.Ok, () => IrPara(1)));
                chips.Children.Add(ChipPasso(2, "Chapa", nb.Chapa.Taxa, nb.Chapa.Ok, () => IrPara(2)));
                chips.Children.Add(ChipPasso(3, "Solda", nb.Solda.Taxa, nb.Solda.Ok, () => IrPara(3)));
            }
            DockPanel.SetDock(chips, Dock.Right);
            dp.Children.Add(chips);

            Nivel nv; string titulo, glifo;
            if (!r.TemEsforcos) { nv = Nivel.Neutro; titulo = "Sem verificação"; glifo = "–"; }
            else if (falhas.Count > 0) { nv = Nivel.Erro; titulo = "Não passa — " + string.Join(", ", falhas); glifo = "✕"; }
            else if (r.Inclinada) { nv = Nivel.Alerta; titulo = "Placa resolvida — falta detalhar a ligação inclinada"; glifo = "!"; }
            else { nv = Nivel.Ok; titulo = "Passa · usa " + Pct(nb.Taxa) + " no item mais carregado"; glifo = "✓"; }
            var cores = Cores(nv);
            var icone = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = cores.bg, BorderBrush = cores.bd, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = glifo, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = cores.fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            DockPanel.SetDock(icone, Dock.Left);
            dp.Children.Add(icone);

            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var lin1 = new WrapPanel { Orientation = Orientation.Horizontal };
            var tTit = Tx(titulo, 15, nv == Nivel.Erro ? TemaPlant.BTexto1 : nv == Nivel.Neutro ? TemaPlant.BTexto1 : cores.fg, FontWeights.SemiBold);
            tTit.Margin = new Thickness(0, 0, 10, 0);
            lin1.Children.Add(tTit);
            foreach (var av in AvisosCompactos(r))
            {
                av.Margin = new Thickness(0, 1, 6, 1);
                lin1.Children.Add(av);
            }
            txt.Children.Add(lin1);
            string sub;
            if (!r.TemEsforcos) sub = r.Erro;
            else
                sub = Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm · " + DescFixAtual(r) + (r.Sec != null ? " · pilar " + r.Sec.Nome.Replace("x", "×") : "") +
                      "      N = " + Num.Fmt(nb.N, 2) + " kN   V = " + Num.Fmt(nb.V, 2) + " kN   M = " + Num.Fmt(nb.M, 2) + " kN·m  ·  " + nb.Comb + (Membros.Count > 1 ? " · " + nb.Barra : "") + " · eixo " + nb.Eixo;
            var tSub = Tx(sub, 11.5, r.TemEsforcos ? TemaPlant.BTexto2 : TemaPlant.BErro);
            tSub.TextTrimming = TextTrimming.CharacterEllipsis;
            tSub.Margin = new Thickness(0, 2, 0, 0);
            Dica(tSub, sub + (r.TemEsforcos ? "\nN, V e M no pé do pilar na combinação que governa (N > 0 = tração nas fixações), esforços de cálculo do motor." : ""));
            txt.Children.Add(tSub);
            if (r.TemEsforcos)
            {
                string casos = Membros.Count > 1 ? Pl.N(nb.NCombs, "caso", "casos") + " (combinações ELU × " + Membros.Count + " bases)" : Pl.N(nb.NCombs, "combinação ELU", "combinações ELU");
                var tMet = Tx("Método NBR (motor): NBR 8800 6.6.5 + bloco de compressão · chapa CBCA · chumbador NBR 8800 6.3.3 + ACI 318 cap. 17 · pior de " + casos + ", eixos forte e fraco", 11, TemaPlant.BTexto3);
                tMet.TextTrimming = TextTrimming.CharacterEllipsis;
                tMet.Margin = new Thickness(0, 2, 0, 0);
                Dica(tMet, NotaBiaxial());
                txt.Children.Add(tMet);
            }
            dp.Children.Add(txt);
            cabecalho.Child = dp;
        }

        string NotaBiaxial()
        {
            var b = nb.J?["biaxial"];
            return S(b, "nota") + "\nPior taxa no eixo forte (My): " + Pct(D(b?["eixos"], "forte (My)")) + " · no eixo fraco (Mz): " + Pct(D(b?["eixos"], "fraco (Mz)")) +
                   ".\nGoverna: " + nb.Comb + " · eixo " + nb.Eixo + ".";
        }

        Border ChipPasso(int num, string nome, double ap, bool ok, Action clique)
        {
            var nv = NivelAp(ap, ok);
            var c = Cores(nv);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Tx(num + "  " + nome, 12, TemaPlant.BTexto1, FontWeights.SemiBold));
            var pct = Tx("  " + Pct(ap), 12, c.fg, FontWeights.Bold);
            Typography.SetNumeralAlignment(pct, FontNumeralAlignment.Tabular);
            sp.Children.Add(pct);
            sp.Children.Add(Tx(ok && ap <= 1 + 1e-9 ? "  ✓" : "  ✕", 12, c.fg, FontWeights.Bold));
            var b = new Border
            {
                Background = c.bg, BorderBrush = c.bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, Child = sp
            };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; clique(); };
            return Dica(b, nome + ": " + Pct(ap) + " da capacidade" + (ok ? "" : " — não passa") + ". Clique para ir ao passo " + num + ".");
        }

        List<Border> AvisosCompactos(ResPlaca r)
        {
            var l = new List<Border>();
            if (!r.TemEsforcos) return l;
            if (r.Inclinada)
            {
                string t = "Barra inclinada " + Num.Fmt(r.AngNormal, 1) + "° em relação à normal da placa: as fixações foram calculadas com a reação do nó nos eixos da placa, mas a chapa e a solda " +
                           "(modelo do perfil perpendicular à placa) não valem para barra inclinada. Detalhe a ligação (gusset/cantoneira) à parte.";
                l.Add(Chip("⚠ Ligação inclinada " + Num.Fmt(r.AngNormal, 0) + "°: detalhar", Nivel.Alerta, t, () => { abaAtiva = 1; Recalcular(); }));
            }
            if (nb.Fraco) l.Add(Chip("Governa o eixo fraco (Mz)", Nivel.Info, NotaBiaxial()));
            if (nb.Avisos.Count > 0)
                l.Add(Chip("⚠ " + Pl.N(nb.Avisos.Count, "aviso do motor", "avisos do motor"), Nivel.Alerta, "• " + string.Join("\n• ", nb.Avisos) + "\nClique para ver o cálculo.", () => { abaAtiva = 4; Recalcular(); }));
            return l;
        }

        // Rola até o passo e realça a borda por um instante
        void IrPara(int passo)
        {
            FecharFlutuantes();
            Border alvo = passo == 1 ? cardFix : passo == 2 ? cardCh : cardSd;
            if (alvo == null) return;
            if (realcado != null) { realcado.BorderBrush = TemaPlant.BBordaSutil; realcado.BorderThickness = new Thickness(1); }
            alvo.BorderBrush = TemaPlant.BBordaFoco; alvo.BorderThickness = new Thickness(1.5);
            realcado = alvo;
            alvo.BringIntoView();
            tmRealce.Stop(); tmRealce.Start();
        }

        // ---------- 2) esquerda: seletor de vista + desenho ----------
        static readonly string[] NomesVistas = { "Planta", "3D", "Forças", "Ficha", "Cálculo" };
        static readonly string[] DicasVistas =
        {
            "Vista de cima com as cotas. Clique numa cota azul para mudar a medida.",
            "Placa com espessura, pilar no ângulo real, concreto translúcido, chumbadores com o embutimento hef e cones de arrancamento. Arraste para girar.",
            "Tração (T) e corte (V) em cada furo na combinação crítica, com os esforços no pé do pilar.",
            "Tudo o que a oficina precisa: chapa, furos, chumbadores, torque e solda.",
            "Todas as verificações em tabela: grandeza, fórmula, valor e ✓ / ✕."
        };
        static readonly string[] SubVistas =
        {
            "vista de cima · clique numa cota azul para editar",
            "arraste para girar · roda = zoom · duplo clique = vista inicial",
            "T e V por furo na combinação crítica",
            "cortar, furar, soldar e apertar",
            "fórmulas e valores de cada verificação"
        };

        void MontarVista(ResPlaca r, ResSolda sd)
        {
            var itens = new List<(string, bool, Action, string)>();
            for (int i = 0; i < NomesVistas.Length; i++)
            {
                int k = i;
                itens.Add((NomesVistas[i], abaAtiva == i, (Action)(() => { abaAtiva = k; Recalcular(); }), DicasVistas[i]));
            }
            hostSeg.Child = Segmentado(itens);
            lblDicaVista.Text = SubVistas[abaAtiva];
            if (abaAtiva == 0 || abaAtiva == 2)
            {
                plano.ModoForcas = abaAtiva == 2;
                areaAba.Background = TemaPlant.BBgCanvas;
                areaAba.Child = plano;
                plano.InvalidateVisual();
            }
            else if (abaAtiva == 1)
            {
                areaAba.Background = TemaPlant.BBgCanvas;
                areaAba.Child = vista3d;
                vista3d.InvalidateVisual();
            }
            else if (abaAtiva == 3) { areaAba.Background = TemaPlant.BSup0; areaAba.Child = Ficha(r, sd); }
            else { areaAba.Background = TemaPlant.BSup0; areaAba.Child = AbaCalculo(r, sd); }
        }

        // ---------- 3) direita: 3 passos numerados ----------
        Border Passo(int num, string titulo, double ap, bool ok, bool aplicavel, bool maisAberto, Action alternarMais, UIElement corpo, Func<UIElement> mais)
        {
            var sp = new StackPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderBrush = TemaPlant.BAcento, BorderThickness = new Thickness(1.5), Background = TemaPlant.BAcentoSutil,
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = num.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TemaPlant.BRealce, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            esq.Children.Add(Tx(titulo.ToUpperInvariant(), 11, TemaPlant.BTexto2, FontWeights.SemiBold));
            DockPanel.SetDock(esq, Dock.Left);
            cab.Children.Add(esq);

            var dir = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (aplicavel)
            {
                var c = Cores(NivelAp(ap, ok));
                var pct = Tx(Pct(ap), 12, c.fg, FontWeights.Bold); pct.MinWidth = 38; pct.TextAlignment = TextAlignment.Right;
                Typography.SetNumeralAlignment(pct, FontNumeralAlignment.Tabular);
                dir.Children.Add(pct);
                dir.Children.Add(Chip(ok && ap <= 1 + 1e-9 ? "✓ passa" : "✕ não passa", NivelAp(ap, ok), null, null, 10.5));
                ((FrameworkElement)dir.Children[dir.Children.Count - 1]).Margin = new Thickness(8, 0, 0, 0);
            }
            if (mais != null)
            {
                var lk = Link(maisAberto ? "menos ▴" : "mais ▾", alternarMais, maisAberto ? "Esconder as opções extras e o cálculo deste passo." : "Mostrar opções extras e o cálculo deste passo.", 11.5);
                lk.Foreground = TemaPlant.BTexto2; lk.Margin = new Thickness(12, 0, 0, 0);
                dir.Children.Add(lk);
            }
            DockPanel.SetDock(dir, Dock.Right);
            cab.Children.Add(dir);
            var barra = aplicavel ? BarraCap(ap, ok, 5) : new Border();
            ((FrameworkElement)barra).Margin = new Thickness(12, 0, 4, 0);
            cab.Children.Add(barra);
            sp.Children.Add(cab);
            sp.Children.Add(corpo);
            if (mais != null && maisAberto)
            {
                var bm = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = mais() };
                sp.Children.Add(bm);
            }
            return new Border
            {
                Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 6), Child = sp
            };
        }

        // Linha "rótulo | controles" com a coluna de rótulos fixa (alinhamento entre passos)
        static Grid Linha(string rotulo, UIElement conteudo, double margemTopo = 6, string dica = null)
        {
            var g = new Grid { Margin = new Thickness(0, margemTopo, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LargRot) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Tx(rotulo, 12, TemaPlant.BTexto2);
            if (dica != null) Dica(l, dica);
            g.Children.Add(l);
            Grid.SetColumn((FrameworkElement)conteudo, 1);
            g.Children.Add(conteudo);
            return g;
        }

        static StackPanel Horiz(params UIElement[] els)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var e in els) if (e != null) sp.Children.Add(e);
            return sp;
        }

        static FrameworkElement Gap(double w) { return new Border { Width = w }; }

        // ① Fixação
        Border MontarPassoFixacao(ResPlaca r)
        {
            Chumbador chAt = r.Ch ?? Chumbadores.Achar(edit.Chumbador);
            bool modoCh = chAt != null;
            int tipo = !modoCh ? 2 : chAt.Quimico ? 1 : 0;
            var corpo = new StackPanel();

            // tipo + Sugerir
            var linTipo = new DockPanel();
            btSugerir = Botao("Sugerir", "dimens", MostrarSugerir, "Mostra as 3 melhores combinações fixação + chapa que passam no motor (pior combinação; preferência: " + (prefFuroMenor ? "furo menor" : "mais leve") + ").");
            DockPanel.SetDock(btSugerir, Dock.Right);
            linTipo.Children.Add(btSugerir);
            linTipo.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Mecânico", tipo == 0, () => MudarTipo(0), "Chumbador de expansão (catálogo): instalação imediata no concreto pronto."),
                ("Químico", tipo == 1, () => MudarTipo(1), "Chumbador com resina (ampola ou injeção): mais carga, precisa de cura."),
                ("Pré-concretado", tipo == 2, () => MudarTipo(2), "Chumbador com cabeça/porca concretado no bloco: aço pela NBR 8800 6.3.3 e concreto pelo ACI 318-19 cap. 17 (cone, arrancamento, fendilhamento, borda, alavanca).")
            }));
            corpo.Children.Add(linTipo);

            if (modoCh)
            {
                var vs = VariantesPasso1();
                int iMenor = -1;
                for (int i = 0; i < Math.Min(vs.Count, regua.Count); i++) if (regua[i].FixOk) { iMenor = i; break; }
                var menorPassa = iMenor >= 0 ? vs[iMenor].ch : null;
                var linNome = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var links = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var lkTrocar = Link("Trocar ›", AbrirOverlayTrocar, "Lista dos chumbadores do catálogo verificados pelo motor nesta placa: recomendados, maiores e os que não servem (com o critério).");
                lkTrocar.HorizontalAlignment = HorizontalAlignment.Right;
                var lkComp = Link("Comparar ›", () => AbrirComparar(chAt), "Compara 2 ou mais chumbadores lado a lado pelo catálogo (cargas e coeficientes do fabricante; tabela do método legado).");
                lkComp.HorizontalAlignment = HorizontalAlignment.Right; lkComp.Margin = new Thickness(0, 3, 0, 0);
                links.Children.Add(lkTrocar); links.Children.Add(lkComp);
                DockPanel.SetDock(links, Dock.Right);
                linNome.Children.Add(links);
                var info = new StackPanel();
                var wpNome = new WrapPanel();
                wpNome.Children.Add(Tx(r.Paraf.Count + " × " + NomeCurtoCh(chAt), 14, TemaPlant.BTexto1, FontWeights.Bold));
                if (menorPassa == chAt) { var tg = Chip("menor que passa", Nivel.Ok, "É o menor chumbador deste tipo que passa nesta placa (motor, pior combinação).", null, 10.5); tg.Margin = new Thickness(8, 1, 0, 0); wpNome.Children.Add(tg); }
                info.Children.Add(wpNome);
                string tq = chAt.Torque > 0 ? " · torque " + Num.Fmt(chAt.Torque, 1) + " kgf·m" : "";
                var det = Tx(chAt.Fabricante + " · furo Ø" + Num.Fmt(chAt.Furo, chAt.Furo == Math.Floor(chAt.Furo) ? 0 : 1) + " × " + Num.Fmt(chAt.Hef, 0) + " mm" + tq, 11.5, TemaPlant.BTexto2);
                det.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(det);
                linNome.Children.Add(info);
                corpo.Children.Add(linNome);

                UIElement extra = null;
                if (chAt.Quimico)
                    extra = Horiz(Gap(10), Dica(Tx("barra", 12, TemaPlant.BTexto2), "Classe da barra roscada do chumbador químico (aço verificado pela NBR 8800 6.3.3)."), Gap(6),
                                  Combo(Parafusos.Classes, pe.ClasseBarra, s => { pe.ClasseBarra = s; Recalcular(); }, 66, "Classe da barra roscada (4.6 = barra comum, lado seguro)."));
                corpo.Children.Add(Linha("Quantidade", Horiz(SegQuantidade(), extra), 8, "Fileiras × fixações por fileira, no contorno da placa."));
                if (mostrarOutroArranjo || !ArranjoPadrao()) corpo.Children.Add(Linha("", LinhaOutroArranjo(), 6));

                int iTipo = string.IsNullOrEmpty(edit.TipoCarga) ? 0 : Array.IndexOf(Chumbadores.Tipos, edit.TipoCarga.ToLowerInvariant()) + 1;
                var opsTipo = new List<string> { "auto: " + Chumbadores.NomesTipos[leg.TemEsforcos ? leg.TipoCarga : 1] };
                opsTipo.AddRange(Chumbadores.NomesTipos);
                string riscoAt = (edit.Risco ?? "").ToLowerInvariant();
                var opsRisco = new List<string> { "auto: " + (leg.RiscoAlto ? "alto" : "baixo"), "baixo risco", "alto risco" };
                int iRisco = riscoAt == "baixo" ? 1 : riscoAt == "alto" ? 2 : 0;
                var cbTipo = Combo(opsTipo, opsTipo[Math.Max(0, iTipo)], s => { int i = opsTipo.IndexOf(s); pe.TipoCarga = i <= 0 ? "automático" : Chumbadores.NomesTipos[i - 1]; Recalcular(); }, 104,
                    "Tipo de carga (tabela Walsywa, catálogo pág. 07). Baixo / alto risco: estática 3 / 5 · variável 4 / 5 · dinâmica 7 / 15 · choque 5 / 10. Auto = dinâmica se houver carga giratória no modelo, senão variável.");
                var cbRisco = Combo(opsRisco, opsRisco[iRisco], s => { int i = opsRisco.IndexOf(s); pe.Risco = i == 1 ? "baixo" : i == 2 ? "alto" : "automático"; Recalcular(); }, 106,
                    "Grau de risco. Alto = a falha pode ferir pessoas ou derrubar o equipamento (coeficiente maior). Auto = alto com carga giratória, senão baixo (com aviso).");
                corpo.Children.Add(Linha("Carga", Horiz(cbTipo, Gap(6), cbRisco), 8, "Condições que definem o coeficiente do chumbador (tabela Walsywa). O fck do concreto fica no passo 2."));
            }
            else
            {
                int icAt = Array.IndexOf(Parafusos.Classes, edit.Classe); if (icAt < 0) icAt = 3;
                double hef = edit.Hef > 0 ? edit.Hef : 12 * edit.Diametro;
                double furo = D(nb.R?["fixacao"], "furo") * 1000;
                var info = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                info.Children.Add(Tx(r.Paraf.Count + " × Ø" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe + " · pré-concretado", 14, TemaPlant.BTexto1, FontWeights.Bold));
                var det = Tx("hef " + Num.Fmt(hef, 0) + " mm" + (edit.Hef > 0 ? "" : " (12·Ø)") + (double.IsNaN(furo) ? "" : " · furo alargado na chapa Ø" + Num.Fmt(furo, 1) + " mm") + " · cabeça/porca pesada", 11.5, TemaPlant.BTexto2);
                det.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(det);
                corpo.Children.Add(info);
                corpo.Children.Add(Linha("Diâmetro", Regua(Parafusos.D.Length, i =>
                {
                    double d = Parafusos.D[i];
                    var v = i < regua.Count ? regua[i] : (FixOk: false, FixTaxa: double.NaN, Ok: false, Taxa: double.NaN, TNec: double.NaN, Criterio: "");
                    return ("Ø" + Num.Fmt(d, 0), v.FixOk, Math.Abs(d - edit.Diametro) < 1e-6, "Ø" + Num.Fmt(d, 0) + " · fixação " + (v.FixOk ? "usa " + Pct(v.FixTaxa) : "não passa (" + v.Criterio + ")") + " (motor, pior combinação)", () => { edit.Diametro = d; Recalcular(); });
                }), 8, "Verde = passa, vermelho = não passa, branco = atual (motor, pior combinação). Clique para escolher."));
                var itensCl = new List<(string, bool, Action, string)>();
                foreach (string cl in Parafusos.Classes) { string c = cl; itensCl.Add((cl, edit.Classe == cl, (Action)(() => { edit.Classe = c; Recalcular(); }), "Classe " + cl + " (ISO 898-1): fub = " + cl.Split('.')[0] + "00 MPa")); }
                corpo.Children.Add(Linha("Classe", Segmentado(itensCl, 28, 11.5), 6));
                corpo.Children.Add(Linha("Quantidade", SegQuantidade(), 6));
                if (mostrarOutroArranjo || !ArranjoPadrao()) corpo.Children.Add(Linha("", LinhaOutroArranjo(), 6));
                corpo.Children.Add(Linha("hef", Horiz(CaixaNum(Num.Fmt(edit.Hef, 0), "mm", 78, v => { edit.Hef = v; Recalcular(); },
                    "Embutimento efetivo do chumbador com cabeça/porca (ACI 318-19 17.6.2). 0 = 12·Ø (CBCA p. 237).", v => v >= 0), Gap(8), Tx("0 = 12·Ø", 11, TemaPlant.BTexto3)), 6, "Embutimento efetivo hef."));
            }
            corpo.Children.Add(Linha("Motor", TextoFixNbr(r), 8));
            return Passo(1, "Fixação", nb.Fix.Taxa, nb.Fix.Ok, r.TemEsforcos, maisFix, () => { maisFix = !maisFix; Recalcular(); }, corpo, () => MaisFixacao(r, chAt));
        }

        // Tração e corte nos chumbadores (motor) e o critério que governa a fixação.
        UIElement TextoFixNbr(ResPlaca r)
        {
            var sp = new StackPanel();
            if (!r.TemEsforcos) { sp.Children.Add(Tx("—", 12, TemaPlant.BTexto2)); return sp; }
            int nt = nb.Paraf.Count(p => p[2] > 1e-9);
            var l1 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = TemaPlant.BTexto2 };
            l1.Inlines.Add(new Run("T máx = " + Num.Fmt(nb.Tmax, 2) + " kN") { Foreground = TemaPlant.BTexto1, FontWeight = FontWeights.Bold });
            l1.Inlines.Add(new Run(" (" + Pl.N(nt, "tracionado", "tracionados") + ") · corte " +
                (nb.Mecanismo == "chumbadores" ? Num.Fmt(nb.Fv, 2) + " kN em " + Pl.N(nb.NEf, "chumbador", "chumbadores") + (edit.ArruelaSoldada ? " (arruela soldada)" : " (furo alargado)") : "por " + nb.Mecanismo)));
            sp.Children.Add(l1);
            var l2 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto2, Margin = new Thickness(0, 1, 0, 0) };
            l2.Inlines.Add(new Run("governa: " + nb.Fix.Criterio + " "));
            l2.Inlines.Add(new Run(Pct(nb.Fix.Taxa)) { Foreground = nb.Fix.Ok ? TemaPlant.BSucesso : TemaPlant.BErro, FontWeight = FontWeights.SemiBold });
            l2.Inlines.Add(new Run(" · " + nb.Fix.Comb + (nb.Fix.Fraco ? " · eixo fraco" : "")));
            sp.Children.Add(l2);
            var fx = nb.R?["fixacao"];
            if (r.Ch != null && !double.IsNaN(D(fx, "NRef")))
            {
                var l3 = Tx("catálogo: n = " + Num.Fmt(D(fx, "NRef"), 1) + " (" + (edit.CoefChumbador > 0 ? "digitado" : "tabela Walsywa") + ") sobre esforços de cálculo · obtido " + FmtN(D(fx, "NComb")), 11, TemaPlant.BTexto3, null, true);
                sp.Children.Add(l3);
            }
            return sp;
        }

        static CheckBox Marca(string texto, bool v, Action<bool> mudar, string dica)
        {
            var cb = new CheckBox { Content = Tx(texto, 12, TemaPlant.BTexto1), IsChecked = v, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (s, e) => mudar(true);
            cb.Unchecked += (s, e) => mudar(false);
            Dica(cb, dica);
            return cb;
        }

        UIElement MaisFixacao(ResPlaca r, Chumbador chAt)
        {
            var sp = new StackPanel();
            if (chAt != null)
            {
                double nTab = leg.TemEsforcos ? Chumbadores.Coef(leg.TipoCarga, leg.RiscoAlto) : 0;
                var st = Stepper(edit.CoefChumbador > 0 ? "n = " + Num.Fmt(edit.CoefChumbador, 1) : "tabela (" + Num.Fmt(nTab, 0) + ")",
                    PassoValor(ValoresNCh, edit.CoefChumbador, -1, v => edit.CoefChumbador = v), PassoValor(ValoresNCh, edit.CoefChumbador, +1, v => edit.CoefChumbador = v), 110,
                    () => { string resp = Pergunta.Mostrar(this, "Coeficiente do chumbador", "Coeficiente do chumbador no concreto (0 = tabela Walsywa):", Num.Fmt(edit.CoefChumbador, 1)); double v; if (resp != null && Num.Ler(resp, out v) && v >= 0) { edit.CoefChumbador = v; Recalcular(); } },
                    "Divide a carga de ruptura do catálogo. 'tabela' = pela tabela Walsywa (tipo de carga + grau de risco). Clique no valor para digitar.");
                sp.Children.Add(Linha("Coeficiente", Horiz(st, Gap(8), Tx("0 = pela tabela", 11, TemaPlant.BTexto3)), 0));
            }
            else
            {
                sp.Children.Add(Linha("Corte", Marca("Rosca no plano de corte", edit.RoscaNoCorte, v => { edit.RoscaNoCorte = v; Recalcular(); }, "Marcado = o plano de cisalhamento passa pela rosca (NBR 8800 6.3.3.2: 0,4·Ab·fub)."), 0));
                sp.Children.Add(Linha("Concreto", Marca("Não fissurado (ψc = 1,25)", edit.NaoFissurado, v => { edit.NaoFissurado = v; Recalcular(); }, "ACI 318-19 17.6.2.5: concreto não fissurado sob cargas de serviço. Desmarcado = fissurado (lado seguro)."), 6));
            }
            sp.Children.Add(Linha("Arruela", Marca("Arruela soldada (todos ao corte)", edit.ArruelaSoldada, v => { edit.ArruelaSoldada = v; Recalcular(); },
                "Com furo alargado só 2 chumbadores trabalham ao corte (DG1). Arruela soldada à placa faz todos trabalharem."), 8));
            sp.Children.Add(Linha("Barra cis.", Horiz(CaixaNum(Num.Fmt(edit.LugB, 0), "mm", 74, v => { edit.LugB = v; Recalcular(); }, "Largura b da barra de cisalhamento. 0 = sem barra.", v => v >= 0, "b"), Gap(4),
                CaixaNum(Num.Fmt(edit.LugT, 0), "mm", 66, v => { edit.LugT = v; Recalcular(); }, "Espessura t da barra.", v => v >= 0, "t"), Gap(4),
                CaixaNum(Num.Fmt(edit.LugH, 0), "mm", 74, v => { edit.LugH = v; Recalcular(); }, "Altura h embutida no concreto.", v => v >= 0, "h")), 6,
                "Barra de cisalhamento (shear lug): o corte vai por atrito (10% de Nd), depois pela barra, depois pelos chumbadores. Verifica pressão no concreto e flexão; cone ACI 349 e solda da barra não."));
            sp.Children.Add(Linha("Graute", CaixaNum(Num.Fmt(edit.Graute, 0), "mm", 78, v => { edit.Graute = v; Recalcular(); }, "Espessura do graute (braço da flexão da barra).", v => v >= 0), 6));
            sp.Children.Add(TituloSecao("Cálculo (motor, " + nb.Comb + ")", null, 10));
            sp.Children.Add(TabelaCompacta(CalcNbr("fixacao")));
            return sp;
        }

        bool ArranjoPadrao()
        {
            return edit.Fileiras == 2 && (edit.PorFileira == 2 || edit.PorFileira == 3) || edit.Fileiras == 3 && edit.PorFileira == 3;
        }

        UIElement SegQuantidade()
        {
            bool e22 = edit.Fileiras == 2 && edit.PorFileira == 2, e23 = edit.Fileiras == 2 && edit.PorFileira == 3, e33 = edit.Fileiras == 3 && edit.PorFileira == 3;
            bool outro = !e22 && !e23 && !e33;
            return Segmentado(new List<(string, bool, Action, string)>
            {
                ("2 × 2", e22, () => { edit.Fileiras = 2; edit.PorFileira = 2; mostrarOutroArranjo = false; Recalcular(); }, "2 fileiras × 2 = 4 fixações no contorno da placa."),
                ("2 × 3", e23, () => { edit.Fileiras = 2; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }, "2 fileiras × 3 = 6 fixações."),
                ("3 × 3", e33, () => { edit.Fileiras = 3; edit.PorFileira = 3; mostrarOutroArranjo = false; Recalcular(); }, "3 fileiras × 3 = 8 fixações no contorno (o centro fica livre)."),
                (outro ? edit.Fileiras + " × " + edit.PorFileira : "outro…", outro || mostrarOutroArranjo, () => { mostrarOutroArranjo = !mostrarOutroArranjo; Recalcular(); }, "Digitar outra quantidade de fileiras e de fixações por fileira."),
            });
        }

        UIElement LinhaOutroArranjo()
        {
            var bF = CaixaNum(edit.Fileiras.ToString(), "fil.", 70, v => { edit.Fileiras = Math.Max(1, (int)Math.Round(v)); Recalcular(); }, "Fileiras ao longo do plano do pórtico (H).", v => v >= 1 && v <= 10);
            var bP = CaixaNum(edit.PorFileira.ToString(), "por fil.", 84, v => { edit.PorFileira = Math.Max(1, (int)Math.Round(v)); Recalcular(); }, "Fixações em cada fileira (ao longo de B).", v => v >= 1 && v <= 10);
            return Horiz(bF, Tx("  ×  ", 12, TemaPlant.BTexto2), bP);
        }

        void MudarTipo(int tipo)
        {
            Chumbador atual = Chumbadores.Achar(edit.Chumbador);
            int tipoAt = atual == null ? 2 : atual.Quimico ? 1 : 0;
            if (tipo == tipoAt) return;
            if (tipo == 2) { edit.Chumbador = null; msgDim = "Tipo trocado para pré-concretado."; Recalcular(); return; }
            bool quim = tipo == 1;
            var lista = Chumbadores.Lista.Where(c => c.Quimico == quim).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).ToList();
            if (lista.Count == 0) return;
            double dRef = atual != null ? atual.Rosca : edit.Diametro;
            edit.Chumbador = lista.OrderBy(c => Math.Abs(c.Rosca - dRef)).ThenBy(c => c.Hef).First().Nome;   // base do tipo certo (coeficiente do chumbador)
            var res = Variantes(lista.Select(c => { var p = edit.Copiar(); p.Chumbador = c.Nome; return p; }).ToList());
            int i = res.FindIndex(x => x.FixOk);
            if (i >= 0) edit.Chumbador = lista[i].Nome;
            msgDim = "Tipo trocado para " + (quim ? "químico" : "mecânico") + ": " + NomeCurtoCh(Chumbadores.Achar(edit.Chumbador)) + (i >= 0 ? " (menor que passa no motor)." : " (nenhum do tipo passa).");
            Recalcular();
        }

        void AbrirComparar(Chumbador chAt)
        {
            var rc = PlacaCalc.Calcular(m, n);
            var ini = new List<string> { chAt.Nome };
            var alt = Chumbadores.Lista.Where(c => c != chAt && c.Linha != chAt.Linha && PlacaCalc.CandidatoChumbador(rc, edit, m, c).ChOk).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).Select(c => c.Nome).FirstOrDefault();
            if (alt != null) ini.Add(alt);
            var jc = new JanelaComparar(m, n, rc, edit, ini) { Owner = this };
            jc.ShowDialog();
            Recalcular();   // "Usar este chumbador" grava direto na placa em edição
        }

        // ② Chapa (e o concreto sob ela)
        Border MontarPassoChapa(ResPlaca r)
        {
            var corpo = new StackPanel();
            int idx = Array.FindIndex(PlacaCalc.Chapas, e => Math.Abs(e - edit.Esp) < 1e-4);
            Action menos = idx > 0 ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx - 1]; Recalcular(); })
                : (edit.Esp > PlacaCalc.Chapas[0] ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.LastOrDefault(e => e < edit.Esp); if (edit.Esp <= 0) edit.Esp = PlacaCalc.Chapas[0]; Recalcular(); }) : null);
            Action mais = idx >= 0 && idx + 1 < PlacaCalc.Chapas.Length ? (Action)(() => { edit.Esp = PlacaCalc.Chapas[idx + 1]; Recalcular(); })
                : (edit.Esp < PlacaCalc.Chapas.Last() ? (Action)(() => { edit.Esp = PlacaCalc.Chapas.First(e => e > edit.Esp); Recalcular(); }) : null);
            var st = Stepper(FmtEsp(edit.Esp) + " mm", menos, mais, 82, () =>
            {
                string resp = Pergunta.Mostrar(this, "Espessura da chapa", "Espessura da chapa (mm):", FmtEsp(edit.Esp));
                double v; if (resp != null && Num.Ler(resp, out v) && v > 0) { edit.Esp = v; Recalcular(); }
            }, "Espessuras comerciais. Clique no valor para digitar outra.");
            double massa = r.H * r.B * edit.Esp * 7850e-9;
            bool espOk = r.EspNec <= edit.Esp + 1e-9;
            var need = Tx(r.EspNec > 0 ? "CBCA " + Num.Fmt(r.EspNec, 1) + " mm · " + Num.Fmt(massa, 1) + " kg" : "sem flexão · " + Num.Fmt(massa, 1) + " kg", 11.5, espOk ? TemaPlant.BTexto2 : TemaPlant.BErro);
            need.HorizontalAlignment = HorizontalAlignment.Right;
            Dica(need, "Espessura necessária pelo CBCA (1,25·W, pressão atuante), a maior entre as combinações. Conferência Pfeil/DG1 (plástico, 0,85·fck/γc): " + Num.Fmt(nb.TPfeil, 1) + " mm (" + nb.Governa + " dá a mais grossa; a verificação usa o CBCA).");
            var dpEsp = new DockPanel();
            DockPanel.SetDock(need, Dock.Right); dpEsp.Children.Add(need); dpEsp.Children.Add(st);
            corpo.Children.Add(Linha("Espessura", dpEsp, 0));
            corpo.Children.Add(Linha("", Regua(PlacaCalc.Chapas.Length, i =>
            {
                double t = PlacaCalc.Chapas[i];
                return (FmtEsp(t), t >= r.EspNec - 1e-9, Math.Abs(t - edit.Esp) < 1e-4, FmtEsp(t) + " mm · " + SegChapa(r, t), () => { edit.Esp = t; Recalcular(); });
            }), 4));

            // tamanho H × B + Ajustar
            var bH = CaixaNum(Num.Fmt(r.H, 0), "mm", 78, v => { if (edit.PeloPerfil) { edit.PeloPerfil = false; edit.B = r.B; } edit.H = v; Recalcular(); },
                "Comprimento no plano do eixo forte do pilar (H). Digitar desliga o \"medir pelo perfil\".", v => v > 0);
            var bB = CaixaNum(Num.Fmt(r.B, 0), "mm", 78, v => { if (edit.PeloPerfil) { edit.PeloPerfil = false; edit.H = r.H; } edit.B = v; Recalcular(); },
                "Largura (B). Digitar desliga o \"medir pelo perfil\".", v => v > 0);
            var btAj = Botao("Ajustar tamanho", null, () => { AjustarTamanho(); Recalcular(); }, TextoRegrasTamanho());
            var dpTam = new DockPanel();
            DockPanel.SetDock(btAj, Dock.Right); dpTam.Children.Add(btAj);
            dpTam.Children.Add(Horiz(bH, Tx("  ×  ", 12, TemaPlant.BTexto2), bB));
            corpo.Children.Add(Linha("Tamanho", dpTam, 6, "H × B da chapa. Também dá para clicar nas cotas azuis do desenho."));

            var bFy = CaixaNum(Num.Fmt(r.FyChapa, 0), "MPa", 96, v => { edit.FyChapa = v; Recalcular(); }, "Tensão de escoamento do aço da chapa (A36 = 250 MPa).", v => v > 0 && v < 2000, "fy");
            corpo.Children.Add(Linha("Aço", bFy, 6));
            var bFck = CaixaNum(Num.Fmt(edit.Fck > 0 ? edit.Fck : 20, 0), "MPa", 90, v => { edit.Fck = v; Recalcular(); },
                "fck do concreto do bloco: pressão de contato (NBR 8800 6.6.5), modos de concreto do ACI 318 e redução do catálogo √(fck / fck do ensaio).", v => v > 0 && v < 200, "fck");
            var bBH = CaixaNum(Num.Fmt(edit.BlocoH, 0), "mm", 78, v => { edit.BlocoH = v; Recalcular(); }, "Bloco de concreto na direção H. 0 = não informado: A2 = A1 e bordas infinitas (com aviso).", v => v >= 0);
            var bBB = CaixaNum(Num.Fmt(edit.BlocoB, 0), "mm", 78, v => { edit.BlocoB = v; Recalcular(); }, "Bloco de concreto na direção B. 0 = não informado.", v => v >= 0);
            corpo.Children.Add(Linha("Concreto", Horiz(bFck, Gap(8), Tx("bloco", 12, TemaPlant.BTexto2), Gap(4), bBH, Tx(" × ", 12, TemaPlant.BTexto2), bBB), 6,
                "Bloco de concreto sob a placa (centrado): A2 homotético para σc,Rd = fck/(γc·γn)·√(A2/A1) e as distâncias às bordas do ACI 318."));
            var tc = Tx("σc,Rd = " + Num.Fmt(nb.SigmaRd, 2) + " MPa · " + (nb.TemBloco ? "bloco de compressão Y = " + Num.Fmt(nb.Y, 0) + " mm, fp = " + Num.Fmt(nb.Fp, 2) + " MPa" : nb.Modelo.ToLowerInvariant()), 11.5, TemaPlant.BTexto2, null, true);
            if (r.TemEsforcos) corpo.Children.Add(Linha("", tc, 4));

            return Passo(2, "Chapa", nb.Chapa.Taxa, nb.Chapa.Ok, r.TemEsforcos, maisCh, () => { maisCh = !maisCh; Recalcular(); }, corpo, () => MaisChapa(r));
        }

        UIElement MaisChapa(ResPlaca r)
        {
            var sp = new StackPanel();
            var cbPerfil = new CheckBox { Content = Tx("Medir pelo perfil (pilar + sobra de cada lado)", 12, TemaPlant.BTexto1), IsChecked = edit.PeloPerfil, Cursor = Cursors.Hand };
            cbPerfil.Checked += (s, e) => { edit.PeloPerfil = true; Recalcular(); };
            cbPerfil.Unchecked += (s, e) => { edit.PeloPerfil = false; edit.H = r.H; edit.B = r.B; Recalcular(); };
            Dica(cbPerfil, "Marcado: a chapa acompanha o perfil do pilar + a sobra (arredonda de 10 em 10 mm). Desmarcado: medidas digitadas.");
            sp.Children.Add(cbPerfil);
            if (edit.PeloPerfil)
                sp.Children.Add(Linha("Sobra", CaixaNum(Num.Fmt(edit.Sobra, 0), "mm", 78, v => { edit.Sobra = v; Recalcular(); }, "Folga da placa além do perfil, em cada lado.", v => v >= 0), 6));
            sp.Children.Add(Linha("Borda", Horiz(CaixaNum(Num.Fmt(edit.Borda, 0), "mm", 78, v => { edit.Borda = v; Recalcular(); }, "Do centro dos furos externos até a borda da chapa.", v => v > 0 && 2 * v < r.H && 2 * v < r.B),
                Gap(8), Tx("centro do furo até a borda", 11.5, TemaPlant.BTexto3)), 6));
            sp.Children.Add(Linha("Altura bloco", Horiz(CaixaNum(Num.Fmt(edit.BlocoAltura, 0), "mm", 78, v => { edit.BlocoAltura = v; Recalcular(); }, "Altura do bloco de concreto (ψh,V do ACI 318 17.7.2.6). 0 = não informada.", v => v >= 0),
                Gap(8), Tx("0 = não informada", 11, TemaPlant.BTexto3)), 6));
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var cbCone = new CheckBox { Content = Tx("Ajustar tamanho sem sobrepor cones (3·hef)", 12, TemaPlant.BTexto1), IsChecked = semSobreporCones, Cursor = Cursors.Hand, Margin = new Thickness(0, 8, 0, 0) };
                cbCone.Checked += (s, e) => semSobreporCones = true; cbCone.Unchecked += (s, e) => semSobreporCones = false;
                Dica(cbCone, "Marcado: \"Ajustar tamanho\" usa espaçamento ≥ 3·hef entre chumbadores, para o catálogo valer sem redução por cones sobrepostos (placa maior). Desmarcado: usa o mínimo para instalar e aplica a redução ψ.");
                sp.Children.Add(cbCone);
            }
            sp.Children.Add(TituloSecao("Cálculo (motor, " + nb.Comb + ")", null, 10));
            sp.Children.Add(TabelaCompacta(CalcNbr("chapa")));
            return sp;
        }

        // ③ Solda
        Border MontarPassoSolda(ResPlaca r, ResSolda sd)
        {
            var corpo = new StackPanel();
            bool sdOk = sd == null || sd.Passa;
            double hEf = sd != null ? sd.H : (edit.Solda > 0 ? edit.Solda : 3);
            int idxP = edit.Solda <= 0 ? -1 : Array.FindIndex(Solda.Pernas, p => Math.Abs(p - edit.Solda) < 1e-4);
            Action menos = edit.Solda <= 0 ? null : (Action)(() => { edit.Solda = idxP <= 0 ? 0 : Solda.Pernas[idxP - 1]; Recalcular(); });
            Action mais = () =>
            {
                if (edit.Solda <= 0) { double prox = Solda.Pernas.FirstOrDefault(p => p > hEf + 1e-9); edit.Solda = prox > 0 ? prox : Solda.Pernas.Last(); }
                else if (idxP >= 0 && idxP + 1 < Solda.Pernas.Length) edit.Solda = Solda.Pernas[idxP + 1];
                Recalcular();
            };
            var st = Stepper(edit.Solda <= 0 ? "auto (" + Num.Fmt(hEf, 0) + " mm)" : Num.Fmt(edit.Solda, 0) + " mm", menos, mais, 96, () => { edit.Solda = 0; Recalcular(); },
                "Perna do filete em todo o contorno do perfil. Auto = a menor perna da tabela que passa (respeitando o mínimo de norma). Clique no valor para voltar ao automático.");
            string elAt = string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo;
            var cbEl = Combo(Solda.Eletrodos.Select(e => e + "XX"), elAt + "XX", s => { edit.Eletrodo = s.Replace("XX", ""); Recalcular(); }, 98, "Eletrodo AWS (tensão admissível no filete pela tabela do eletrodo).");
            var cont = Tx(sd != null && sd.Erro == "" ? "contorno " + Num.Fmt(sd.L, 0) + " mm" : "", 11.5, TemaPlant.BTexto2);
            var dp = new DockPanel();
            DockPanel.SetDock(cont, Dock.Right); dp.Children.Add(cont);
            dp.Children.Add(Horiz(st, Gap(8), cbEl));
            corpo.Children.Add(Linha("Perna", dp, 0));

            if (sd == null)
                corpo.Children.Add(Linha("", Tx(r.TemEsforcos ? "Perfil do pilar sem medidas para a solda." : "Sem esforços: calcule a estrutura.", 11.5, TemaPlant.BTexto2, null, true), 6));
            else if (sdOk)
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto2 };
                tb.Inlines.Add(new System.Windows.Documents.Run("τ = " + Num.Fmt(sd.Tau, 0) + " ≤ " + Num.Fmt(sd.TauAdm, 0) + " MPa") { Foreground = TemaPlant.BSucesso, FontWeight = FontWeights.SemiBold });
                tb.Inlines.Add(new System.Windows.Documents.Run(" · filete contínuo · perna mínima de norma " + (sd.HMinNorma > 0 ? Num.Fmt(sd.HMinNorma, 0) + " mm" : "—")));
                corpo.Children.Add(Linha("Tensão", tb, 6));
            }
            else
            {
                var dg = Solda.Diagnosticar(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, sd.H, edit.Eletrodo, edit.Esp);
                var box = new StackPanel();
                var t1 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto1 };
                t1.Inlines.Add(new System.Windows.Documents.Run("Por quê: ") { FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BErroClaro });
                t1.Inlines.Add(new System.Windows.Documents.Run(dg.Porque));
                box.Children.Add(t1);
                var t2 = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto1, Margin = new Thickness(0, 3, 0, 0) };
                t2.Inlines.Add(new System.Windows.Documents.Run("O que fazer: ") { FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BErroClaro });
                t2.Inlines.Add(new System.Windows.Documents.Run(dg.OQueFazer));
                box.Children.Add(t2);
                if (dg.Perna > 0)
                {
                    string el2 = dg.Eletrodo;
                    double p2 = dg.Perna;
                    var bt = Botao("Usar " + (el2 != elAt ? el2 + "XX, " : "") + "perna " + Num.Fmt(p2, 0) + " mm", null, () => { edit.Solda = p2; edit.Eletrodo = el2; Recalcular(); }, "Aplica a sugestão (o cálculo é refeito).", 26);
                    bt.HorizontalAlignment = HorizontalAlignment.Left; bt.Margin = new Thickness(0, 6, 0, 0);
                    box.Children.Add(bt);
                }
                corpo.Children.Add(new Border
                {
                    Background = TemaPlant.BErroFundo, BorderBrush = TemaPlant.BErroBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(9, 5, 9, 6), Margin = new Thickness(0, 6, 0, 0), Child = box
                });
            }
            if (r.Inclinada && sdOk)
            {
                var ti = Tx("Barra inclinada: chapa e solda valem para o pilar perpendicular; detalhe a ligação à parte.", 11, TemaPlant.BAlerta, null, true);
                ti.Margin = new Thickness(0, 6, 0, 0);
                corpo.Children.Add(ti);
            }
            return Passo(3, "Solda pilar–placa", nb.Solda.Taxa, nb.Solda.Ok, r.TemEsforcos, maisSd, () => { maisSd = !maisSd; Recalcular(); }, corpo, () => TabelaCompacta(CalcSolda(sd)));
        }

        // "Comparado ao que estava" (linha discreta no fim da coluna)
        void MontarMudancas(ResPlaca r)
        {
            if (refInicial == null || r.H <= 0 || r.B <= 0) return;
            double h0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefIn + 2 * refInicial.Sobra) / 10) * 10 : refInicial.H;
            double b0 = refInicial.PeloPerfil ? Math.Ceiling((r.RefOut + 2 * refInicial.Sobra) / 10) * 10 : refInicial.B;
            double dm = r.H * r.B * edit.Esp * 7850e-9 - h0 * b0 * refInicial.Esp * 7850e-9;
            var muds = new List<string>();
            if (Math.Abs(refInicial.Esp - edit.Esp) > 1e-4) muds.Add("chapa " + FmtEsp(refInicial.Esp) + " → " + FmtEsp(edit.Esp) + " mm");
            if (Math.Abs(h0 - r.H) > 1e-4 || Math.Abs(b0 - r.B) > 1e-4) muds.Add("placa " + Num.Fmt(h0, 0) + "×" + Num.Fmt(b0, 0) + " → " + Num.Fmt(r.H, 0) + "×" + Num.Fmt(r.B, 0) + " mm");
            var c0 = Chumbadores.Achar(refInicial.Chumbador); var c1 = Chumbadores.Achar(edit.Chumbador);
            string f0 = c0 != null ? NomeCurtoCh(c0) : "M" + Num.Fmt(refInicial.Diametro, 0) + " cl. " + refInicial.Classe;
            string f1 = c1 != null ? NomeCurtoCh(c1) : "M" + Num.Fmt(edit.Diametro, 0) + " cl. " + edit.Classe;
            if (f0 != f1 || refInicial.Fileiras != edit.Fileiras || refInicial.PorFileira != edit.PorFileira)
                muds.Add(refInicial.Fileiras * refInicial.PorFileira + "× " + f0 + " → " + edit.Fileiras * edit.PorFileira + "× " + f1);
            if (muds.Count == 0) return;
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = TemaPlant.BTexto3, Margin = new Thickness(2, 2, 2, 4) };
            tb.Inlines.Add(new System.Windows.Documents.Run("Comparado ao que estava: ") { Foreground = TemaPlant.BTexto2 });
            string sinal = dm < -0.05 ? "−" : dm > 0.05 ? "+" : "±";
            tb.Inlines.Add(new System.Windows.Documents.Run(sinal + Num.Fmt(Math.Abs(dm), 1) + " kg de chapa") { Foreground = dm <= 0.05 ? TemaPlant.BSucesso : TemaPlant.BAlerta, FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(" · " + string.Join(" · ", muds)));
            colDir.Children.Add(tb);
        }

        // "Comparar com método legado" (recolhido): o Nucleo.cs com pivô na borda e coeficiente n global, só para conferir a planilha.
        void MontarLegado()
        {
            var sp = new StackPanel();
            var lk = Link(verLegado ? "Comparar com método legado ▴" : "Comparar com método legado ▾", () => { verLegado = !verLegado; Recalcular(); },
                "Método antigo do Estructto (Nucleo.cs): pivô na borda, sem bloco de compressão, coeficiente n global. Subestima a tração nos chumbadores (até ~3,3×). Só para comparar com a planilha.", 11.5);
            lk.Foreground = TemaPlant.BTexto3;
            sp.Children.Add(lk);
            if (verLegado)
            {
                var l = new List<(string, string, string, int)>();
                if (leg == null || !leg.TemEsforcos) l.Add(("Legado", "", leg?.Erro ?? "—", 0));
                else
                {
                    l.Add(("Combinação (legado)", "pior pelo critério do legado", CombLegado ?? "—", 0));
                    l.Add(("Esforços", "N, V, M no pé (eixo forte)", "N = " + Num.Fmt(leg.N, 2) + " · V = " + Num.Fmt(leg.V, 2) + " · M = " + Num.Fmt(leg.M, 2), 0));
                    l.Add(("Tração máx. · legado × NBR", "pivô na borda × bloco de compressão", Num.Fmt(leg.Tmax, 2) + " × " + Num.Fmt(nb.Tmax, 2) + " kN", 0));
                    l.Add(("Corte por fixação · legado × NBR", "V/n × atrito/barra/n_ef", Num.Fmt(leg.Fv, 2) + " × " + Num.Fmt(nb.Fv, 2) + " kN", 0));
                    l.Add(("Segurança (legado)", "n obtido ≥ n exigido", FmtN(leg.NComb) + " ≥ " + Num.Fmt(leg.Ch != null ? leg.NRef : leg.NExig, 2), leg.ChOk ? 1 : -1));
                    l.Add(("Chapa · legado × CBCA", "faixas elásticas com n × NBR", Num.Fmt(leg.EspNec, 1) + " × " + Num.Fmt(nb.TNec, 1) + " mm", 0));
                    l.Add(("Coeficiente n (legado)", edit.Seguranca > 0 ? "desta placa" : "do modelo", Num.Fmt(leg.NSeg, 2), 0));
                }
                var bd = new Border { Background = TemaPlant.BSup1, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4, 6, 6), Margin = new Thickness(0, 4, 0, 0) };
                var c = new StackPanel();
                c.Children.Add(Tx("Não usar para projeto: o veredito desta janela é o do motor (NBR).", 11, TemaPlant.BAlerta, FontWeights.SemiBold, true));
                c.Children.Add(TabelaCompacta(l));
                var stN = Stepper(edit.Seguranca > 0 ? "n = " + Num.Fmt(edit.Seguranca, 2) : "n auto " + Num.Fmt(m.Seguranca, 2),
                    PassoValor(ValoresN, edit.Seguranca, -1, v => edit.Seguranca = v), PassoValor(ValoresN, edit.Seguranca, +1, v => edit.Seguranca = v), 92, null,
                    "Coeficiente global n do método legado (0 = o do modelo). Não entra no método NBR (esforços de cálculo).");
                var cbPivo = Marca("Giro na mesa", edit.PivoNaMesa, v => { edit.PivoNaMesa = v; Recalcular(); }, "Legado: a placa gira na mesa comprimida do perfil (desmarcado: na borda da placa).");
                c.Children.Add(Linha("Legado", Horiz(stN, Gap(10), cbPivo), 6));
                bd.Child = c;
                sp.Children.Add(bd);
            }
            sp.Margin = new Thickness(2, 4, 2, 6);
            if (verLegado) sp.Loaded += (s, e) => sp.BringIntoView();
            colDir.Children.Add(sp);
        }

        // ---------- 4) rodapé ----------
        void MontarRodape(ResPlaca r)
        {
            rodape.Children.Clear();
            var dir = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (original != null) dir.Children.Add(B("Remover placa", () => { n.Placa = null; Mudou = true; Close(); }, false, "Tira a placa de base deste nó."));
            dir.Children.Add(B("Cancelar", () => { n.Placa = original; Close(); }, false, "Fecha sem mudar nada (Esc)."));
            int nOutros = marcados.Count;
            string rotAp = AplicarNoGrupo && Grupo != null ? "Aplicar ao grupo" + (nOutros > 0 ? " + " + nOutros : "") : nOutros > 0 ? "Aplicar em " + (nOutros + 1) + " nós" : "Aplicar";
            var btAp = B(rotAp, () => { AplicarEm.Clear(); AplicarEm.AddRange(marcados.Select(i => Outros[i].No)); Mudou = true; Close(); }, true,
                "Grava esta placa como projeto do grupo de ligações " + (AplicarNoGrupo && Grupo != null ? Grupo.Value.Nome + " (" + Grupo.Value.Nos.Count + " bases)" : "deste nó") +
                (nOutros > 0 ? " e nos nós " + string.Join(", ", marcados.Select(i => Outros[i].No)) : "") + ".");
            btAp.IsDefault = false;
            dir.Children.Add(btAp);
            foreach (Button b in dir.Children.OfType<Button>()) { b.Height = 28; b.MinWidth = 92; }
            DockPanel.SetDock(dir, Dock.Right);
            rodape.Children.Add(dir);

            var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(Botao("Dimensionar tudo", "dimens", () => { Dimensionar(); Recalcular(); },
                "O motor escolhe a menor fixação que passa em todas as combinações (e nos dois eixos), a chapa comercial ≥ t do CBCA e a perna da solda.", 28));
            esq.Children.Add(Gap(8));
            esq.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Mais leve", !prefFuroMenor, () => { prefFuroMenor = false; Recalcular(); }, "Ao dimensionar e sugerir, prefere a menor fixação e a chapa mais fina que passam."),
                ("Furo menor", prefFuroMenor, () => { prefFuroMenor = true; Recalcular(); }, "Ao dimensionar e sugerir, prefere o menor furo no concreto (broca menor).")
            }, 28));
            if (Api != null && (Outros.Count > 0 || Grupo != null))
            {
                esq.Children.Add(Gap(16));
                esq.Children.Add(new Border { Width = 1, Height = 22, Background = TemaPlant.BBordaSutil });
                esq.Children.Add(Gap(16));
                esq.Children.Add(Botao("Aplicar também em…" + (marcados.Count > 0 ? " (" + marcados.Count + ")" : AplicarNoGrupo ? " (grupo)" : ""), "copiar", MostrarAplicarEm,
                    "Outros pés de pilar selecionados e o grupo de ligações das bases, cada um verificado pelo motor na sua pior combinação.", 28));
            }
            DockPanel.SetDock(esq, Dock.Left);
            rodape.Children.Add(esq);
            var msg = Tx(msgDim, 11.5, TemaPlant.BTexto3);
            msg.TextTrimming = TextTrimming.CharacterEllipsis; msg.Margin = new Thickness(14, 0, 10, 0);
            Dica(msg, msgDim);
            rodape.Children.Add(msg);
        }

        // ================= flutuantes =================
        // Sugerir: as 3 melhores combinações num popup leve preso ao botão
        void MostrarSugerir()
        {
            FecharFlutuantes();
            ResPlaca r = Vista(leg, nb);
            var ops = MelhoresOpcoes(r);
            var sp = new StackPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var fx = Link("✕", () => overlaySugerir.Visibility = Visibility.Collapsed, "Fechar (Esc)", 13); fx.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(fx, Dock.Right); cab.Children.Add(fx);
            cab.Children.Add(Tx("Sugestões que passam", 13.5, TemaPlant.BTexto1, FontWeights.SemiBold));
            sp.Children.Add(cab);
            sp.Children.Add(Tx("Preferência: " + (prefFuroMenor ? "furo menor" : "mais leve") + " · motor, pior combinação, com as condições atuais", 11, TemaPlant.BTexto3));
            if (ops.Count == 0)
            {
                var t = Tx(r.TemEsforcos ? "Nenhuma combinação do catálogo passa. Aumente a placa (Ajustar tamanho) ou a quantidade." : "Sem esforços: calcule a estrutura.", 12, TemaPlant.BAlerta, null, true);
                t.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(t);
            }
            int i = 0;
            foreach (var o in ops)
            {
                i++;
                bool atual = DescFix(o.pl, 0) == DescFix(edit, 0) && o.pl.Fileiras == edit.Fileiras && o.pl.PorFileira == edit.PorFileira && Math.Abs(o.pl.Esp - edit.Esp) < 1e-4;
                var lin = new Border
                {
                    Background = atual ? TemaPlant.BAcentoSutil : TemaPlant.BSup2, BorderBrush = atual ? TemaPlant.BAcento : TemaPlant.BBordaSutil, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, i == 1 ? 10 : 6, 0, 0)
                };
                var dp = new DockPanel();
                UIElement acao;
                if (atual) acao = Chip("em uso", Nivel.Info);
                else
                {
                    var pl = o.pl;
                    acao = Botao("Usar", null, () => { edit.Chumbador = pl.Chumbador; edit.Diametro = pl.Diametro; edit.Classe = pl.Classe; edit.Fileiras = pl.Fileiras; edit.PorFileira = pl.PorFileira; edit.Esp = pl.Esp; overlaySugerir.Visibility = Visibility.Collapsed; msgDim = "Sugestão aplicada: " + DescFix(pl, pl.Fileiras * pl.PorFileira) + " · chapa " + FmtEsp(pl.Esp) + " mm."; Recalcular(); }, "Troca a fixação e a chapa por esta combinação.", 26);
                }
                DockPanel.SetDock(acao, Dock.Right); ((FrameworkElement)acao).Margin = new Thickness(10, 0, 0, 0); dp.Children.Add(acao);
                var num = Tx(i.ToString(), 12, TemaPlant.BRealce, FontWeights.Bold); num.Width = 16; num.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(num, Dock.Left); dp.Children.Add(num);
                var st = new StackPanel();
                st.Children.Add(Tx(o.txt, 12.5, TemaPlant.BTexto1, FontWeights.SemiBold, true));
                st.Children.Add(Tx("fixação usa " + Pct(o.ap) + " · chapa " + Num.Fmt(o.kg, 1) + " kg", 11, TemaPlant.BTexto2));
                dp.Children.Add(st);
                lin.Child = dp;
                sp.Children.Add(lin);
            }
            var card = new Border
            {
                Width = 380, Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 12, 14, 14), Child = sp,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            canvasSugerir.Children.Clear();
            canvasSugerir.Children.Add(card);
            Point pt = new Point(ActualWidth - 420, 120);
            if (btSugerir != null && btSugerir.IsLoaded)
            {
                try { pt = btSugerir.TranslatePoint(new Point(btSugerir.ActualWidth, btSugerir.ActualHeight + 6), camadaRaiz); pt.X -= 380; } catch { }
            }
            Canvas.SetLeft(card, Math.Max(8, Math.Min(pt.X, camadaRaiz.ActualWidth - 390)));
            Canvas.SetTop(card, Math.Max(8, pt.Y));
            overlaySugerir.Visibility = Visibility.Visible;
        }

        // Aplicar também em…: outros pés de pilar, pior caso e dimensionar pelo pior caso
        void MontarOverlayAplicarEm()
        {
            var sombra = new Border { Background = TemaPlant.BSombraModal };
            sombra.MouseLeftButtonDown += (s, e) => overlayAplicarEm.Visibility = Visibility.Collapsed;
            overlayAplicarEm.Children.Add(sombra);
            var painel = new Border
            {
                Width = 560, MaxHeight = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            var dp = new DockPanel();
            var cab = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            var lt = new DockPanel();
            var fx = Link("✕", () => overlayAplicarEm.Visibility = Visibility.Collapsed, "Fechar (Esc)", 14); fx.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(fx, Dock.Right); lt.Children.Add(fx);
            lt.Children.Add(Tx("Aplicar esta placa também em…", 15, TemaPlant.BTexto1, FontWeights.SemiBold));
            cab.Children.Add(lt);
            var sub = Tx("O motor verifica ESTA placa em cada base, na pior combinação ELU e nos dois eixos. Marque o grupo de ligações e/ou os outros pés selecionados que devem receber a mesma placa.", 11.5, TemaPlant.BTexto2, null, true);
            sub.Margin = new Thickness(0, 4, 0, 0);
            cab.Children.Add(sub);
            DockPanel.SetDock(cab, Dock.Top);
            dp.Children.Add(cab);
            var rod = new DockPanel { Margin = new Thickness(18, 10, 18, 14) };
            var btOk = B("Concluir", () => overlayAplicarEm.Visibility = Visibility.Collapsed, true, "Fecha esta lista (os nós marcados recebem a placa ao Aplicar).");
            btOk.IsDefault = false; btOk.Height = 30;
            DockPanel.SetDock(btOk, Dock.Right); rod.Children.Add(btOk);
            lblAplicarEm.FontSize = 11.5; lblAplicarEm.Foreground = TemaPlant.BTexto3; lblAplicarEm.TextWrapping = TextWrapping.Wrap; lblAplicarEm.VerticalAlignment = VerticalAlignment.Center;
            rod.Children.Add(lblAplicarEm);
            DockPanel.SetDock(rod, Dock.Bottom);
            dp.Children.Add(rod);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listaAplicarEm, Padding = new Thickness(18, 0, 18, 0) });
            painel.Child = dp;
            overlayAplicarEm.Children.Add(painel);
        }

        void MostrarAplicarEm()
        {
            FecharFlutuantes();
            PreencherAplicarEm();
            overlayAplicarEm.Visibility = Visibility.Visible;
        }

        void PreencherAplicarEm()
        {
            listaAplicarEm.Children.Clear();
            if (Api == null || (Outros.Count == 0 && Grupo == null)) { listaAplicarEm.Children.Add(Tx("Selecione outros pés de pilar na vista 3D (ou identifique os grupos de ligações) para aplicar a mesma placa.", 12, TemaPlant.BTexto2, null, true)); return; }
            // uma chamada: este pé, os outros selecionados e o grupo; o motor devolve o pior de cada membro
            var todos = new JsonArray(Membros.Select(x => x.DeepClone()).ToArray());
            foreach (var o in Outros) todos.Add(o.Membro.DeepClone());
            JsonNode res = null;
            try { res = Chamar("/api/placa_base/verificar", Corpo(edit, todos)); } catch (Exception ex) { listaAplicarEm.Children.Add(Tx("Motor: " + PlacaMotor.Mensagem(ex), 12, TemaPlant.BErro, null, true)); return; }
            var porNo = Lista(res["membros"]).ToDictionary(x => S(x, "no"), x => (Taxa: D(x, "taxa"), Ok: Bool(x, "ok"), Comb: S(x, "combinacao")));
            (double Taxa, bool Ok, string Comb) De(string no) => porNo.TryGetValue(no, out var v) ? v : (double.NaN, false, "");

            void LinhaNo(FrameworkElement esq, string no, bool pior)
            {
                var x = De(no);
                var g = new Grid { MinHeight = 32 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                g.Children.Add(esq);
                var bar = BarraCap(x.Taxa, x.Ok, 6); bar.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(bar, 1); g.Children.Add(bar);
                var pc = Tx(Pct(x.Taxa), 12, Cores(NivelAp(x.Taxa, x.Ok)).fg, FontWeights.SemiBold); pc.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(pc, 2); g.Children.Add(pc);
                Dica(g, "Nó " + no + ": pior combinação " + x.Comb + ".");
                if (pior || !x.Ok) { var tg = Chip(pior ? "pior caso" : "não passa", x.Ok ? Nivel.Alerta : Nivel.Erro, null, null, 10.5); tg.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(tg, 3); g.Children.Add(tg); }
                listaAplicarEm.Children.Add(new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = g });
            }
            string este = S(Membros[0], "no");
            string piorNo = porNo.OrderByDescending(kv => double.IsNaN(kv.Value.Taxa) ? double.MaxValue : kv.Value.Taxa).Select(kv => kv.Key).FirstOrDefault();
            var tit = Tx("Pior caso: nó " + piorNo + " — usa " + Pct(De(piorNo).Taxa) + (De(piorNo).Ok ? "" : ", não passa"), 12.5, De(piorNo).Ok ? TemaPlant.BTexto1 : TemaPlant.BErro, FontWeights.SemiBold);
            tit.Margin = new Thickness(0, 2, 0, 8);
            listaAplicarEm.Children.Add(tit);
            if (Grupo is var (gid, gnome, gnos))
            {
                var cbG = Marca("Grupo de ligações " + gnome + " (" + Pl.N(gnos.Count, "base", "bases") + ")", AplicarNoGrupo, v => { AplicarNoGrupo = v; Recalcular(); },
                    "O projeto da placa vale para o grupo inteiro (fonte única: grupos de ligações do modelo). Desmarcado: este nó sai do grupo e fica com a sua placa.");
                cbG.Margin = new Thickness(0, 4, 0, 4);
                listaAplicarEm.Children.Add(cbG);
                if (AplicarNoGrupo)
                {
                    var gm = new JsonArray(gnos.Select(no => (JsonNode)new JsonObject { ["no"] = no }).ToArray());
                    var vg = Grupo.Value.Nos.Count > 1 ? VerificarGrupo() : null;
                    if (vg != null) listaAplicarEm.Children.Add(Tx("Grupo inteiro: pior " + Pct(vg.Taxa) + (vg.Ok ? "" : " — não passa") + " (" + vg.Comb + " · " + vg.Barra + ")", 11.5, vg.Ok ? TemaPlant.BTexto2 : TemaPlant.BErro, null, true));
                }
            }
            LinhaNo(Tx("      Nó " + este + " (este)", 12, TemaPlant.BTexto1), este, piorNo == este);
            for (int i = 0; i < Outros.Count; i++)
            {
                int k = i; var o = Outros[i];
                var cb = Marca("Nó " + o.No + (o.TemPlaca ? " · já tem placa" : " · sem placa"), marcados.Contains(k), v => { if (v) marcados.Add(k); else marcados.Remove(k); Recalcular(); },
                    "Marque para gravar esta placa também no nó " + o.No + " ao clicar em Aplicar" + (o.TemPlaca ? " (substitui a placa que ele tem)." : "."));
                LinhaNo(cb, o.No, piorNo == o.No);
            }
            var bts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
            if (Outros.Count > 0)
            {
                bool todosMarc = Enumerable.Range(0, Outros.Count).All(marcados.Contains);
                bts.Children.Add(Botao(todosMarc ? "Desmarcar todos" : "Marcar todos", null, () => { if (todosMarc) marcados.Clear(); else for (int i = 0; i < Outros.Count; i++) marcados.Add(i); Recalcular(); }, "Marca ou desmarca os outros pés selecionados."));
                bts.Children.Add(Gap(8));
            }
            bts.Children.Add(Botao("Dimensionar pelo pior caso", "dimens", () => { Dimensionar(MembrosAplicar()); Recalcular(); },
                "O motor escolhe a menor fixação e a chapa que passam neste nó, nos marcados" + (AplicarNoGrupo ? " e no grupo" : "") + ", cada um em todas as combinações."));
            listaAplicarEm.Children.Add(bts);
            lblAplicarEm.Text = (AplicarNoGrupo && Grupo != null ? "Ao aplicar, a placa vai para o grupo " + Grupo.Value.Nome : "Ao aplicar, a placa vai só para este nó") +
                                (marcados.Count > 0 ? " e para " + string.Join(", ", marcados.Select(i => "nó " + Outros[i].No)) : "") + ".";
        }

        // Membros que recebem a placa: este, os marcados e (se marcado) o grupo inteiro; sem repetir.
        JsonArray MembrosAplicar()
        {
            var l = new JsonArray(Membros.Select(x => x.DeepClone()).ToArray());
            foreach (int i in marcados) l.Add(Outros[i].Membro.DeepClone());
            if (AplicarNoGrupo && GrupoMembros != null) foreach (var x in GrupoMembros) l.Add(x.DeepClone());
            var vistos = new HashSet<string>();
            return new JsonArray(l.Where(x => vistos.Add(S(x, "barra") + "/" + S(x, "ponta"))).Select(x => x.DeepClone()).ToArray());
        }
        public List<JsonObject> GrupoMembros;   // membros {barra, ponta, no} do grupo (quem abre informa junto com Grupo)

        ResNbr.Passo VerificarGrupo()
        {
            if (GrupoMembros == null) return null;
            try
            {
                var r = ResNbr.De(Chamar("/api/placa_base/verificar", Corpo(edit, new JsonArray(GrupoMembros.Select(x => (JsonNode)x.DeepClone()).ToArray()))));
                return new ResNbr.Passo { Taxa = r.Taxa, Ok = r.Ok, Comb = r.Comb, Barra = r.Barra };
            }
            catch { return null; }
        }

        // Trocar chumbador: lista agrupada (recomendados, maiores, não servem) com busca e filtro
        void MontarOverlayTrocar()
        {
            var sombra = new Border { Background = TemaPlant.BSombraModal };
            sombra.MouseLeftButtonDown += (s, e) => overlayTrocar.Visibility = Visibility.Collapsed;
            overlayTrocar.Children.Add(sombra);
            var painel = new Border
            {
                Width = 560, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 16, 16),
                Background = TemaPlant.BSup1, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            var dp = new DockPanel();
            var cabOv = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
            var linTit = new DockPanel();
            var btFechar = Link("✕", () => overlayTrocar.Visibility = Visibility.Collapsed, "Fechar (Esc)", 14); btFechar.Foreground = TemaPlant.BTexto2;
            DockPanel.SetDock(btFechar, Dock.Right);
            linTit.Children.Add(btFechar);
            linTit.Children.Add(Tx("Trocar chumbador", 15, TemaPlant.BTexto1, FontWeights.SemiBold));
            cabOv.Children.Add(linTit);

            var boxBusca = new Border { Height = 30, Margin = new Thickness(0, 10, 0, 10), Background = TemaPlant.BSup3, BorderBrush = TemaPlant.BBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
            var gB = new Grid();
            gB.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gB.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icB = (FrameworkElement)Icones.Criar("busca", 14); icB.Margin = new Thickness(8, 0, 4, 0); icB.VerticalAlignment = VerticalAlignment.Center;
            gB.Children.Add(icB);
            var tbBusca = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto1, CaretBrush = TemaPlant.BTexto1, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, Padding = new Thickness(2, 0, 4, 0) };
            var dicaBusca = Tx("Buscar modelo, fabricante ou diâmetro (ex.: 1/2\", WB, WQE)…", 12, TemaPlant.BTextoMudo);
            dicaBusca.Margin = new Thickness(4, 0, 0, 0); dicaBusca.IsHitTestVisible = false;
            tbBusca.TextChanged += (s, e) =>
            {
                filtroBuscaCh = tbBusca.Text ?? "";
                dicaBusca.Visibility = string.IsNullOrEmpty(filtroBuscaCh) ? Visibility.Visible : Visibility.Collapsed;
                PreencherOverlayTrocar();
            };
            tbBusca.GotKeyboardFocus += (s, e) => boxBusca.BorderBrush = TemaPlant.BBordaFoco;
            tbBusca.LostKeyboardFocus += (s, e) => boxBusca.BorderBrush = TemaPlant.BBorda;
            Grid.SetColumn(tbBusca, 1); Grid.SetColumn(dicaBusca, 1);
            gB.Children.Add(tbBusca); gB.Children.Add(dicaBusca);
            boxBusca.Child = gB;
            cabOv.Children.Add(boxBusca);

            btsFiltroTipoCh = new StackPanel { Orientation = Orientation.Horizontal };
            cabOv.Children.Add(btsFiltroTipoCh);
            var bdCab = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Child = cabOv };
            DockPanel.SetDock(bdCab, Dock.Top);
            dp.Children.Add(bdCab);
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listaTrocar });
            painel.Child = dp;
            overlayTrocar.Children.Add(painel);
        }

        void AbrirOverlayTrocar()
        {
            FecharFlutuantes();
            if (string.IsNullOrEmpty(chumbAntes) && !string.IsNullOrEmpty(edit.Chumbador)) chumbAntes = edit.Chumbador;
            verTodosNaoServem = false;
            PreencherOverlayTrocar();
            overlayTrocar.Visibility = Visibility.Visible;
        }

        void PreencherOverlayTrocar()
        {
            btsFiltroTipoCh.Children.Clear();
            btsFiltroTipoCh.Children.Add(Segmentado(new List<(string, bool, Action, string)>
            {
                ("Todos", filtroTipoCh == 0, () => { filtroTipoCh = 0; PreencherOverlayTrocar(); }, "Mecânicos e químicos."),
                ("Mecânico", filtroTipoCh == 1, () => { filtroTipoCh = 1; PreencherOverlayTrocar(); }, "Chumbadores de expansão: instalação imediata."),
                ("Químico", filtroTipoCh == 2, () => { filtroTipoCh = 2; PreencherOverlayTrocar(); }, "Chumbadores com resina: mais carga, precisa de cura.")
            }));

            listaTrocar.Children.Clear();
            ResPlaca r = Vista(leg, nb);
            string q = (filtroBuscaCh ?? "").Trim().ToLowerInvariant();
            var filtrados = Chumbadores.Lista.Where(c =>
            {
                if (filtroTipoCh == 1 && c.Quimico) return false;
                if (filtroTipoCh == 2 && !c.Quimico) return false;
                if (q != "" && !(c.Nome.ToLowerInvariant().Contains(q) || c.Fabricante.ToLowerInvariant().Contains(q) || c.Pol.ToLowerInvariant().Contains(q) || c.Codigo.ToLowerInvariant().Contains(q)))
                    return false;
                return true;
            }).ToList();
            var res = Variantes(filtrados.Select(c => { var pl = edit.Copiar(); pl.Chumbador = c.Nome; return pl; }).ToList());
            var todos = filtrados.Select((c, i) => i < res.Count ? new { Ch = c, Res = res[i], Ok = res[i].FixOk } : new { Ch = c, Res = (false, double.NaN, false, double.NaN, double.NaN, "sem resposta do motor"), Ok = false }).ToList();

            var passam = todos.Where(x => x.Ok).OrderBy(x => x.Ch.Rosca).ThenBy(x => x.Ch.Hef).ThenBy(x => x.Res.Item2).ToList();
            var recom = passam.Take(3).ToList();
            var maiores = passam.Skip(3).ToList();
            var naoServem = todos.Where(x => !x.Ok).OrderBy(x => x.Ch.Rosca).ThenBy(x => x.Ch.Hef).ToList();

            void TituloGrupo(string s, int qt)
            {
                var t = Tx(s.ToUpperInvariant() + "  ·  " + qt, 10.5, TemaPlant.BTexto2, FontWeights.SemiBold);
                t.Margin = new Thickness(16, 12, 16, 4);
                listaTrocar.Children.Add(t);
            }
            void LinhaCandidato(Chumbador c, (bool FixOk, double FixTaxa, bool Ok, double Taxa, double TNec, string Criterio) tc, bool ok)
            {
                bool emUso = r.Ch == c;
                bool eraAntes = !emUso && !string.IsNullOrEmpty(chumbAntes) && (c.Nome == chumbAntes || c.Codigo == chumbAntes);
                var bd = new Border
                {
                    Background = emUso ? TemaPlant.BAcentoSutil : Brushes.Transparent, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(emUso ? 3 : 0, 0, 0, 1),
                    Padding = new Thickness(emUso ? 13 : 16, 8, 16, 8), Cursor = Cursors.Hand
                };
                if (!emUso)
                {
                    bd.MouseEnter += (s, e) => bd.Background = TemaPlant.BSup2;
                    bd.MouseLeave += (s, e) => bd.Background = Brushes.Transparent;
                }
                if (emUso) bd.BorderBrush = TemaPlant.BAcento;
                var dp = new DockPanel();
                FrameworkElement dir = emUso ? (FrameworkElement)Chip("em uso", Nivel.Info, null, null, 10.5) : Tx(ok ? Pct(tc.FixTaxa) : "✕", 12, ok ? Cores(NivelAp(tc.FixTaxa, true)).fg : TemaPlant.BErro, FontWeights.SemiBold);
                dir.VerticalAlignment = VerticalAlignment.Center; dir.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(dir, Dock.Right); dp.Children.Add(dir);
                var diam = Tx(c.Pol, 14, ok ? TemaPlant.BTexto1 : TemaPlant.BTexto3, FontWeights.SemiBold); diam.Width = 50;
                DockPanel.SetDock(diam, Dock.Left); dp.Children.Add(diam);
                var meio = new StackPanel();
                var lin1 = new WrapPanel();
                lin1.Children.Add(Tx(c.Linha.Replace("Walsywa ", "") + " hef " + c.Hef.ToString("0"), 12.5, ok ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeights.SemiBold));
                lin1.Children.Add(Tx("  " + c.Fabricante + " · " + (c.Quimico ? "químico" : "mecânico") + " · furo Ø" + Num.Fmt(c.Furo, c.Furo == Math.Floor(c.Furo) ? 0 : 1) + " × " + Num.Fmt(c.Hef, 0), 11.5, TemaPlant.BTexto2));
                if (eraAntes) lin1.Children.Add(Tx("  · antes", 11.5, TemaPlant.BAlerta, FontWeights.SemiBold));
                meio.Children.Add(lin1);
                if (!ok)
                {
                    string motivo = (tc.Criterio == "" ? "não passa" : tc.Criterio) + (double.IsNaN(tc.FixTaxa) || double.IsInfinity(tc.FixTaxa) ? "" : " · " + Pct(tc.FixTaxa));
                    var tm = Tx(motivo, 11, TemaPlant.BErro); tm.Margin = new Thickness(0, 2, 0, 0);
                    meio.Children.Add(tm);
                }
                else
                {
                    var br = BarraCap(tc.FixTaxa, true, 4); br.Margin = new Thickness(0, 5, 0, 0);
                    meio.Children.Add(br);
                }
                dp.Children.Add(meio);
                bd.Child = dp;
                bd.MouseLeftButtonDown += (s, e) => { edit.Chumbador = c.Nome; overlayTrocar.Visibility = Visibility.Collapsed; Recalcular(); e.Handled = true; };
                listaTrocar.Children.Add(bd);
            }
            if (recom.Count > 0) { TituloGrupo("Recomendados", recom.Count); foreach (var x in recom) LinhaCandidato(x.Ch, x.Res, true); }
            if (maiores.Count > 0) { TituloGrupo("Maiores (passam com mais folga)", maiores.Count); foreach (var x in maiores) LinhaCandidato(x.Ch, x.Res, true); }
            if (naoServem.Count > 0)
            {
                TituloGrupo("Não servem aqui", naoServem.Count);
                var mostrar = verTodosNaoServem ? naoServem : naoServem.Take(6).ToList();
                foreach (var x in mostrar) LinhaCandidato(x.Ch, x.Res, false);
                if (!verTodosNaoServem && naoServem.Count > mostrar.Count)
                {
                    var mais = Link("+ " + (naoServem.Count - mostrar.Count) + " modelos ocultos · ver todos", () => { verTodosNaoServem = true; PreencherOverlayTrocar(); });
                    mais.Margin = new Thickness(16, 10, 16, 14);
                    listaTrocar.Children.Add(mais);
                }
            }
            if (todos.Count == 0) { var t = Tx("Nenhum chumbador com esse filtro.", 12, TemaPlant.BTexto2); t.Margin = new Thickness(16, 14, 16, 14); listaTrocar.Children.Add(t); }
        }

        // ================= peças auxiliares =================
        // Régua de valores clicáveis (verde = passa, vermelho = não passa, branco = atual)
        static Grid Regua(int count, Func<int, (string rotulo, bool passa, bool atual, string dica, Action clicar)> item)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var inf = item(i);
                var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0) };
                Dica(sp, inf.dica);
                sp.Children.Add(new Border
                {
                    Height = inf.atual ? 7 : 5, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, inf.atual ? 0 : 1, 2, 2),
                    Background = inf.atual ? TemaPlant.BTexto1 : inf.passa ? TemaPlant.BSucesso : TemaPlant.BErroBorda
                });
                sp.Children.Add(new TextBlock
                {
                    Text = inf.rotulo, FontSize = 10, FontWeight = inf.atual ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = inf.atual ? TemaPlant.BTexto1 : inf.passa ? TemaPlant.BTexto2 : TemaPlant.BTextoMudo, HorizontalAlignment = HorizontalAlignment.Center
                });
                Action ac = inf.clicar;
                sp.MouseLeftButtonDown += (s, e) => { ac(); e.Handled = true; };
                Grid.SetColumn(sp, i);
                g.Children.Add(sp);
            }
            return g;
        }

        static readonly double[] ValoresN = { 0, 1.5, 1.67, 2, 2.5, 3, 4, 5 };
        static readonly double[] ValoresNCh = { 0, 2, 3, 4, 5, 7, 10, 15 };

        Action PassoValor(double[] vals, double atual, int dir, Action<double> set)
        {
            int i = Array.FindIndex(vals, v => Math.Abs(v - atual) < 1e-6);
            if (dir < 0)
            {
                if (i > 0) return () => { set(vals[i - 1]); Recalcular(); };
                if (i < 0 && atual > 0) return () => { set(vals.LastOrDefault(v => v < atual)); Recalcular(); };
                return null;
            }
            if (i >= 0 && i + 1 < vals.Length) return () => { set(vals[i + 1]); Recalcular(); };
            if (i < 0 && atual < vals.Last()) return () => { set(vals.First(v => v > atual)); Recalcular(); };
            return null;
        }

        // Tabela compacta para a coluna estreita: grandeza | valor (a expressão vai na dica)
        static UIElement TabelaCompacta(List<(string rot, string expr, string val, int est)> linhas)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int i = 0;
            foreach (var l in linhas)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var fundo = new Border { Background = i % 2 == 0 ? TemaPlant.BSup1 : Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                Dica(fundo, string.IsNullOrEmpty(l.expr) ? null : l.expr);
                Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 2); g.Children.Add(fundo);
                var a = Tx(l.rot, 11.5, TemaPlant.BTexto2, null, true); a.Margin = new Thickness(8, 4, 8, 4); a.IsHitTestVisible = false;
                Grid.SetRow(a, i); g.Children.Add(a);
                Brush cor = l.est > 0 ? TemaPlant.BSucesso : l.est < 0 ? TemaPlant.BErro : TemaPlant.BTexto1;
                var b = Tx((l.est > 0 ? "✓ " : l.est < 0 ? "✕ " : "") + l.val, 11.5, cor, l.est != 0 ? FontWeights.SemiBold : FontWeights.Normal, true);
                b.TextAlignment = TextAlignment.Right; b.HorizontalAlignment = HorizontalAlignment.Right; b.Margin = new Thickness(0, 4, 8, 4); b.MaxWidth = 220; b.IsHitTestVisible = false;
                Grid.SetRow(b, i); Grid.SetColumn(b, 1); g.Children.Add(b);
                i++;
            }
            return g;
        }

        // Aproveitamento da chapa t pela flexão (CBCA): M cresce com t², então usa (t,nec/t)².
        string SegChapa(ResPlaca r, double t)
        {
            if (r.EspNec <= 0) return "sem flexão";
            double a = r.EspNec / t;
            return "flexão usa " + Pct(a * a);
        }

        // Valor com a unidade do motor: m → mm, kN/m² → MPa.
        static string ValMotor(double v, string un)
        {
            if (double.IsNaN(v)) return "—";
            if (double.IsInfinity(v)) return "∞";
            if (un == "m") return Num.Fmt(v * 1000, 1) + " mm";
            if (un == "kN/m²") return Num.Fmt(v / 1000, 1) + " MPa";
            return Num.Fmt(v, Math.Abs(v) >= 100 ? 0 : 2) + (string.IsNullOrEmpty(un) ? "" : " " + un);
        }

        // Verificações do motor do passo (combinação que governa a placa): grandeza (item), solicitante / resistente, taxa.
        List<(string, string, string, int)> CalcNbr(string passo)
        {
            var l = new List<(string, string, string, int)>();
            if (!nb.Valido) { l.Add(("Sem verificação", "", nb.Erro, 0)); return l; }
            foreach (var v in nb.Verificacoes(passo))
            {
                double t = D(v, "taxa"); string un = S(v, "unidade");
                l.Add((S(v, "nome"), S(v, "item"), ValMotor(D(v, "solicitante"), un) + " / " + ValMotor(D(v, "resistente"), un) + " · " + Pct(double.IsNaN(t) ? double.PositiveInfinity : t), !double.IsNaN(t) && t <= 1 + 1e-9 ? 1 : -1));
            }
            if (passo == "chapa")
            {
                l.Add(("Espessura CBCA × Pfeil", "t ≥ √(6·M/(1,25·fy/γa1)) × √(4·M·γa1/fy) com 0,85·fck/γc", Num.Fmt(nb.TNec, 1) + " × " + Num.Fmt(nb.TPfeil, 1) + " mm (" + nb.Governa + ")", 0));
                l.Add(("Concreto", "σc,Rd = fck/(γc·γn)·√(A2/A1) ≤ fck", Num.Fmt(nb.SigmaRd, 2) + " MPa · " + nb.Modelo + (nb.TemBloco ? ", Y = " + Num.Fmt(nb.Y, 0) + " mm" : ""), 0));
            }
            if (passo == "fixacao") l.Add(("Corte", "atrito → barra → chumbadores", nb.Mecanismo + (nb.Mecanismo == "chumbadores" ? ", " + nb.NEf + " efetivos, Fv = " + Num.Fmt(nb.Fv, 2) + " kN" : ""), 0));
            return l;
        }

        static List<(string, string, string, int)> CalcSolda(ResSolda sd)
        {
            var l = new List<(string, string, string, int)>();
            if (sd == null) { l.Add(("Solda", "", "sem esforços", 0)); return l; }
            if (sd.Erro != "") { l.Add(("Solda", "", sd.Erro, -1)); return l; }
            l.Add(("Grupo de solda", "filete em todo o contorno (Shigley)", sd.Grupo, 0));
            l.Add(("Área da garganta", "A = 1,414 · h · (b + d)  ou  1,414 · π · h · r", Num.Fmt(sd.A, 0) + " mm²", 0));
            l.Add(("Corte primário", "τ' = V / A", Num.Fmt(sd.Tau1, 1) + " MPa", 0));
            l.Add(("Normal na garganta", "τ'' = N / A + M · c / I,  I = 0,707 · h · Iu", Num.Fmt(sd.Tau2, 1) + " MPa", 0));
            l.Add(("Tensão resultante", "τ = √(τ'² + τ''²) ≤ τ adm do eletrodo", Num.Fmt(sd.Tau, 0) + (sd.Tau <= sd.TauAdm + 1e-9 ? " ≤ " : " > ") + Num.Fmt(sd.TauAdm, 0) + " MPa", sd.Tau <= sd.TauAdm + 1e-9 ? 1 : -1));
            if (sd.HMinNorma > 0) l.Add(("Perna mínima de norma", "NBR 8800 Tab. 10, pela peça mais fina", Num.Fmt(sd.HMinNorma, 0) + " mm", sd.AbaixoMinimo ? -1 : 1));
            if (sd.Espessura > 0) l.Add(("Parede do perfil", "perna ≤ parede", Num.Fmt(sd.Espessura, 2) + " mm", sd.MaiorQueParede ? -1 : 1));
            l.Add(("Cordão total / necessário", "L = A / (0,707 · h)", Num.Fmt(sd.L, 0) + " / " + Num.Fmt(sd.LNec, 0) + " mm", 0));
            l.Add(("Metal depositado", "h² / 2 · L · 7850", "≈ " + Num.Fmt(sd.Massa, 2) + " kg", 0));
            return l;
        }

        // ---------- aba Cálculo: tabelas legíveis (motor) ----------
        FrameworkElement AbaCalculo(ResPlaca r, ResSolda sd)
        {
            var sp = new StackPanel { Margin = new Thickness(18, 4, 18, 18) };
            if (!r.TemEsforcos)
            {
                sp.Children.Add(TituloSecao("Sem verificação", null, 12));
                sp.Children.Add(Tx(r.Erro, 12.5, TemaPlant.BTexto2, null, true));
                return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }
            UIElement Cab(double ap, bool ok)
            {
                var d = new StackPanel { Orientation = Orientation.Horizontal };
                var b = BarraCap(ap, ok, 6); b.Width = 120; b.Margin = new Thickness(0, 0, 10, 0);
                d.Children.Add(b);
                d.Children.Add(Tx(Pct(ap), 12, Cores(NivelAp(ap, ok)).fg, FontWeights.Bold));
                var ch = Chip(ok && ap <= 1 + 1e-9 ? "✓ passa" : "✕ não passa", NivelAp(ap, ok), null, null, 10.5); ch.Margin = new Thickness(10, 0, 0, 0);
                d.Children.Add(ch);
                return d;
            }
            var cab = Tx("Motor, método NBR · governa " + nb.Comb + (Membros.Count > 1 ? " (" + nb.Barra + ")" : "") + " · eixo " + nb.Eixo + " · N = " + Num.Fmt(nb.N, 2) + " kN, V = " + Num.Fmt(nb.V, 2) + " kN, M = " + Num.Fmt(nb.M, 2) + " kN·m", 12, TemaPlant.BTexto1, FontWeights.SemiBold, true);
            cab.Margin = new Thickness(0, 10, 0, 2);
            sp.Children.Add(cab);
            sp.Children.Add(Tx(NotaBiaxial(), 11.5, TemaPlant.BTexto3, null, true));
            sp.Children.Add(TituloSecao("1 · Fixação — " + DescFixAtual(r) + " · pior: " + nb.Fix.Comb + (nb.Fix.Fraco ? " (eixo fraco)" : ""), Cab(nb.Fix.Taxa, nb.Fix.Ok), 12));
            sp.Children.Add(TabelaCalc(CalcNbr("fixacao"), 260));
            var forcas = nb.Paraf.Select((p, i) => ("Chumbador " + (i + 1) + " (y; z) = (" + Num.Fmt(p[0], 0) + "; " + Num.Fmt(p[1], 0) + ") mm", "bloco de compressão", "T = " + Num.Fmt(p[2], 2) + " kN", 0)).ToList();
            sp.Children.Add(TabelaCalc(forcas, 260));
            sp.Children.Add(TituloSecao("2 · Chapa " + FmtEsp(edit.Esp) + " mm e concreto — pior: " + nb.Chapa.Comb + (nb.Chapa.Fraco ? " (eixo fraco)" : ""), Cab(nb.Chapa.Taxa, nb.Chapa.Ok), 16));
            sp.Children.Add(TabelaCalc(CalcNbr("chapa"), 260));
            sp.Children.Add(TituloSecao("3 · Solda pilar–placa" + (sd != null ? " — filete " + Num.Fmt(sd.H, 0) + " mm, " + (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX" : "") + " — pior: " + nb.Solda.Comb, Cab(nb.Solda.Taxa, nb.Solda.Ok), 16));
            sp.Children.Add(TabelaCalc(CalcSolda(sd)));
            var combs = Lista(nb.J?["combinacoes"]).Take(12).Select(c => (S(c, "combinacao") + (Membros.Count > 1 ? " · " + S(c, "barra") : ""), "eixo " + S(c, "eixo") + " · N = " + Num.Fmt(D(c, "N"), 1) + ", My = " + Num.Fmt(D(c, "My"), 1) + ", Mz = " + Num.Fmt(D(c, "Mz"), 1), Pct(D(c, "taxa")), Bool(c, "ok") ? 1 : -1)).ToList();
            sp.Children.Add(TituloSecao("4 · Combinações (as " + combs.Count + " piores de " + nb.NCombs + ")", null, 16));
            sp.Children.Add(TabelaCalc(combs, 260));
            if (nb.Avisos.Count > 0)
            {
                sp.Children.Add(TituloSecao("Avisos do motor", null, 16));
                foreach (string a in nb.Avisos) sp.Children.Add(Tx("• " + a, 11.5, TemaPlant.BTexto2, null, true));
            }
            var nota = Tx("Esforços de cálculo (ELU) do motor, sem coeficiente global. Limitações: " + string.Join(" ", Lista(nb.R?["limitacoes"]).Select(x => x.GetValue<string>())), 11, TemaPlant.BTexto3, null, true);
            nota.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(nota);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---------- aba Ficha de fabricação ----------
        FrameworkElement Ficha(ResPlaca r, ResSolda sd)
        {
            FrameworkElement Bloco(string titulo, List<(string k, string v)> linhas)
            {
                var sp = new StackPanel();
                sp.Children.Add(TituloSecao(titulo, null, 0));
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                int i = 0;
                foreach (var l in linhas)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var fundo = new Border { Background = i % 2 == 0 ? TemaPlant.BSup1 : Brushes.Transparent, CornerRadius = new CornerRadius(3) };
                    Grid.SetRow(fundo, i); Grid.SetColumnSpan(fundo, 2); g.Children.Add(fundo);
                    var a = Tx(l.k, 12, TemaPlant.BTexto2, null, true); a.Margin = new Thickness(8, 4, 8, 4); Grid.SetRow(a, i); g.Children.Add(a);
                    var b = Tx(l.v, 12, TemaPlant.BTexto1, null, true); b.Margin = new Thickness(0, 4, 8, 4); Grid.SetRow(b, i); Grid.SetColumn(b, 1); g.Children.Add(b);
                    i++;
                }
                sp.Children.Add(g);
                return new Border { Background = TemaPlant.BSup2, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 10), Child = sp };
            }
            double furoM = D(nb.R?["fixacao"], "furo") * 1000;
            double furo = r.Ch == null && !double.IsNaN(furoM) ? furoM : r.Dia + (r.Dia <= 24 ? 2 : 3);
            var chapa = new List<(string, string)>
            {
                ("Dimensões", Num.Fmt(r.H, 0) + " × " + Num.Fmt(r.B, 0) + " × " + FmtEsp(edit.Esp) + " mm (no plano × fora × espessura)"),
                ("Aço", "fy = " + Num.Fmt(r.FyChapa, 0) + " MPa"),
                ("Massa", Num.Fmt(r.H * r.B * edit.Esp * 7850e-9, 2) + " kg"),
                ("Espessura necessária", "CBCA " + Num.Fmt(r.EspNec, 1) + " mm (Pfeil " + Num.Fmt(nb.TPfeil, 1) + " mm) · " + SegChapa(r, edit.Esp)),
                ("Furos na chapa", r.Paraf.Count + " × Ø" + Num.Fmt(furo, 1) + " mm" + (r.Ch == null ? " (alargado, AISC tab. 14-2)" : " (folga padrão)")),
                ("Arranjo", Pl.N(edit.Fileiras, "fileira", "fileiras") + " × " + edit.PorFileira + " · borda " + Num.Fmt(edit.Borda, 0) + " mm até o centro do furo"),
                ("Posições (y; z) mm", string.Join("   ", r.Paraf.Select(p => "(" + Num.Fmt(p[0], 0) + "; " + Num.Fmt(p[1], 0) + ")"))),
            };
            var fix = new List<(string, string)>();
            if (r.Ch != null)
            {
                fix.Add(("Fixação", r.Paraf.Count + " × chumbador " + r.Ch.Nome + " (" + r.Ch.Fabricante + ")" + (r.Ch.Quimico ? " · barra classe " + r.ClasseBarra : "")));
                fix.Add(("Furo no concreto", "Ø" + Num.Fmt(r.Ch.Furo, 1) + " mm × " + Num.Fmt(r.Ch.Hef, 0) + " mm de embutimento"));
                fix.Add(("Distâncias", "entre chumbadores " + Num.Fmt(r.EspacMin, 0) + " mm (mín. 6·d = " + Num.Fmt(Chumbadores.EspacMinimo(r.Ch), 0) + " mm) · até a borda do concreto ≥ " + Num.Fmt(r.Ch.C > 0 ? r.Ch.C : 1.5 * r.Ch.Hef, 0) + " mm (conferir na obra)"));
                fix.Add(("Aperto", r.Torque > 0 ? "torque " + Num.Fmt(r.Ch.Torque, 1) + " kgf·m (" + Num.Fmt(r.Torque, 0) + " N·m), do catálogo" : "conforme o fabricante (catálogo não informa)"));
                fix.Add(("Coeficiente", "n = " + Num.Fmt(r.NRef, 1) + (r.Ch.Tu > 0 ? " sobre a carga última" : " (carga recomendada)") + (r.Ch.Quimico ? " · aço da barra n = " + Num.Fmt(r.NSeg, 1) : "")));
            }
            else
            {
                fix.Add(("Fixação", r.Paraf.Count + " × chumbador pré-concretado Ø" + Num.Fmt(edit.Diametro, 0) + " classe " + edit.Classe + " com porca pesada" + (edit.RoscaNoCorte ? " (rosca no plano de corte)" : "")));
                fix.Add(("Embutimento", "hef = " + Num.Fmt(edit.Hef > 0 ? edit.Hef : 12 * edit.Diametro, 0) + " mm" + (edit.ArruelaSoldada ? " · arruela soldada" : "")));
            }
            fix.Add(("Maior tração / corte", Num.Fmt(r.Tmax, 2) + " kN / " + Num.Fmt(r.Fv, 2) + " kN por fixação"));
            fix.Add(("Aproveitamento (motor)", Pct(nb.Fix.Taxa) + (nb.Fix.Ok ? " · passa" : " · NÃO PASSA") + " (" + nb.Fix.Criterio + ")"));
            var solda = new List<(string, string)>();
            if (sd != null && sd.Erro == "")
            {
                solda.Add(("Tipo", "filete contínuo em todo o contorno do perfil (" + sd.Grupo + ")"));
                solda.Add(("Eletrodo", (string.IsNullOrEmpty(edit.Eletrodo) ? "E70" : edit.Eletrodo) + "XX · τ adm " + Num.Fmt(sd.TauAdm, 0) + " MPa"));
                solda.Add(("Perna / garganta", Num.Fmt(sd.H, 0) + " mm / " + Num.Fmt(0.707 * sd.H, 1) + " mm" + (sd.HMinNorma > 0 ? " · mínima de norma " + Num.Fmt(sd.HMinNorma, 0) + " mm" : "")));
                solda.Add(("Comprimento", Num.Fmt(sd.L, 0) + " mm de cordão (necessário " + Num.Fmt(sd.LNec, 0) + " mm)"));
                solda.Add(("Metal depositado", "≈ " + Num.Fmt(sd.Massa, 2) + " kg"));
                solda.Add(("Situação", sd.Passa ? "passa (τ = " + Num.Fmt(sd.Tau, 0) + " MPa)" : "NÃO PASSA — ver passo 3"));
            }
            var esf = new List<(string, string)>
            {
                ("Esforços no pé", "N = " + Num.Fmt(r.N, 2) + " kN · V = " + Num.Fmt(r.V, 2) + " kN · M = " + Num.Fmt(r.M, 2) + " kN·m (" + nb.Comb + ", eixo " + nb.Eixo + ")"),
                ("Pilar", r.Sec != null ? r.Sec.Nome : "—"),
                ("Concreto", "fck " + Num.Fmt(edit.Fck > 0 ? edit.Fck : 20, 0) + " MPa · bloco " + (edit.BlocoH > 0 && edit.BlocoB > 0 ? Num.Fmt(edit.BlocoH, 0) + " × " + Num.Fmt(edit.BlocoB, 0) + " mm" : "não informado (A2 = A1)")),
                ("Método", "motor, NBR 8800 6.6.5 + CBCA + ACI 318 cap. 17, esforços de cálculo")
            };
            var g2 = new Grid { Margin = new Thickness(14, 12, 14, 4) };
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c1 = new StackPanel(); var c2 = new StackPanel();
            c1.Children.Add(Bloco("Chapa", chapa));
            c1.Children.Add(Bloco("Esforços e projeto", esf));
            c2.Children.Add(Bloco(r.Ch != null ? "Chumbadores" : "Parafusos", fix));
            if (solda.Count > 0) c2.Children.Add(Bloco("Solda pilar–placa", solda));
            Grid.SetColumn(c2, 2);
            g2.Children.Add(c1); g2.Children.Add(c2);
            return new ScrollViewer { Content = g2, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---------- Sugerir: até 3 combinações fixação + chapa que passam (motor) ----------
        static string DescFix(PlacaBase p, int qt)
        {
            var c = Chumbadores.Achar(p.Chumbador);
            return qt + " × " + (c != null ? NomeCurtoCh(c) : "Ø" + Num.Fmt(p.Diametro, 0) + " cl. " + p.Classe);
        }

        List<(PlacaBase pl, string txt, double kg, double ap)> MelhoresOpcoes(ResPlaca r)
        {
            var res = new List<(PlacaBase, string, double, double)>();
            if (!r.TemEsforcos) return res;
            var cands = new List<PlacaBase>();
            if (!string.IsNullOrEmpty(edit.Chumbador))
            {
                var chs = (prefFuroMenor ? Chumbadores.Lista.OrderBy(c => c.Furo).ThenBy(c => c.Rosca) : Chumbadores.Lista.OrderBy(c => c.Rosca)).ThenBy(c => c.Hef).ToList();
                foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
                    foreach (Chumbador c in chs) { var pl = edit.Copiar(); pl.Chumbador = c.Nome; pl.Fileiras = arr[0]; pl.PorFileira = arr[1]; cands.Add(pl); }
            }
            else
                foreach (double d in Parafusos.D) foreach (string cl in Parafusos.Classes) { var pl = edit.Copiar(); pl.Diametro = d; pl.Classe = cl; cands.Add(pl); }
            var v = Variantes(cands);
            var vistos = new HashSet<string>();
            for (int i = 0; i < Math.Min(v.Count, cands.Count) && res.Count < 3; i++)
            {
                var pl = cands[i];
                string chave = string.IsNullOrEmpty(pl.Chumbador) ? "d" + pl.Diametro : pl.Chumbador;
                if (!v[i].FixOk || double.IsNaN(v[i].TNec) || !vistos.Add(chave)) continue;
                double esp = PlacaCalc.Chapas.FirstOrDefault(e => e >= v[i].TNec - 1e-9);
                if (esp <= 0) continue;
                pl.Esp = esp;
                res.Add((pl, DescFix(pl, pl.Fileiras * pl.PorFileira) + " · chapa " + FmtEsp(esp) + " mm", r.H * r.B * esp * 7850e-9, v[i].FixTaxa));
            }
            return res;
        }

        // ---------- Ajustar tamanho da chapa ----------
        // Regras de espaçamento (múltiplos do diâmetro d da fixação) e do catálogo de chumbadores (6·d para instalar; 3·hef sem sobrepor cones).
        const double KBorda = 1.5;      // centro do furo até a borda da chapa ≥ KBorda·d
        const double KPasso = 3.0;      // entre centros de furos ≥ KPasso·d
        const double KChave = 1.5;      // centro do furo até a face do perfil ≥ KChave·d + folga (espaço para a chave)
        const double FolgaChave = 10;   // mm

        string TextoRegrasTamanho()
        {
            return "Calcula o menor comprimento, largura e borda da chapa, arredondados de 10 em 10 mm:\n" +
                   "• centro do furo até a borda da chapa ≥ " + Num.Fmt(KBorda, 1) + "·d\n" +
                   "• entre centros de furos ≥ " + Num.Fmt(KPasso, 1) + "·d (chumbador: também ≥ 6·d para instalar" + (semSobreporCones ? " e ≥ 3·hef" : "") + ")\n" +
                   "• centro do furo até a face do perfil ≥ " + Num.Fmt(KChave, 1) + "·d + " + Num.Fmt(FolgaChave, 0) + " mm (espaço para a chave)\n" +
                   "Mantém o número de fileiras e de fixações por fileira. Depois confira a espessura.\n" +
                   "Fonte: os slides de Elementos de Máquina (Aula 2) não trazem passo nem margem mínimos; dão a porca com 2·d entre quinas\n" +
                   "e a arruela com 1,5·d (págs. 18 e 49), de onde vem o espaço para a chave. Borda 1,5·d e passo 3·d são a prática usual adotada.";
        }

        void AjustarTamanho()
        {
            ResPlaca r = PlacaCalc.Calcular(m, n);
            if (r.RefIn <= 0) return;
            Chumbador c = Chumbadores.Achar(edit.Chumbador);
            double d = c != null ? c.Rosca : edit.Diametro;
            double borda = Math.Ceiling(KBorda * d / 5) * 5;
            double passo = KPasso * d;
            if (c != null) { passo = Math.Max(passo, Chumbadores.EspacMinimo(c)); if (semSobreporCones) passo = Math.Max(passo, 3 * c.Hef); }
            double aoPerfil = KChave * d + FolgaChave;
            int nf = Math.Max(1, edit.Fileiras), np = Math.Max(1, edit.PorFileira);
            double h = Math.Max(r.RefIn + 2 * (aoPerfil + borda), 2 * borda + (nf - 1) * passo);
            double b = Math.Max(r.RefOut + 2 * borda, 2 * borda + (np - 1) * passo);
            if (nf == 1) h = Math.Max(h, r.RefIn + 2 * borda);
            edit.PeloPerfil = false;
            edit.H = Math.Ceiling(h / 10) * 10; edit.B = Math.Ceiling(b / 10) * 10; edit.Borda = borda;
            msgDim = "Tamanho ajustado: " + Num.Fmt(edit.H, 0) + " × " + Num.Fmt(edit.B, 0) + " mm, borda " + Num.Fmt(borda, 0) + " mm (d = " + Num.Fmt(d, 1) + " mm, passo ≥ " + Num.Fmt(passo, 0) + " mm).";
        }
    }
}
