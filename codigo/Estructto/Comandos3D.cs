// Linha de comando e teclado do Estructto 3D, como no AutoCAD/Plant 3D (docs/v3/PESQUISA-PLANT.md §5, docs/MODELO-ESPACIAL.md
// "Comandos e teclado"). Aliases + Enter/Espaço; Enter/Espaço com a linha vazia repete o último comando; Esc em cascata
// (Vista3D.Tecla); F1–F12; PgUp/PgDn troca o nível do plano de trabalho; Del apaga; Ctrl+A/Z/Y/1/M.
// Os comandos de edição trabalham sobre a seleção (escolha antes) ou pedem os objetos (escolha depois, Enter termina):
// M, CO, MI, RO, TR, EX, MA, E. Pontos com atração, ORTO, entrada dinâmica ou x,y,z / @dx,dy,dz digitados.
// Grips (Vista3D.Grips.cs) chegam aqui em AplicarGrip. Geometria pura em GeometriaEdicao (EdicaoEspacial.cs, testada).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        sealed class Cmd3D
        {
            public string Nome, Desc; public string[] Alias; public Action<string[]> Fazer; public bool SemModelo;
            public Cmd3D SemModeloOk() { SemModelo = true; return this; }
        }

        List<Cmd3D> cmds3D;
        string ultimoCmd;                 // Enter/Espaço com a linha vazia repete
        Func<double, Task> numeroPendente;   // RO: ângulo digitado na linha de comando

        IEnumerable<string> Comandos => Cmds3D.SelectMany(c => new[] { c.Nome }.Concat(c.Alias)).Distinct();

        List<Cmd3D> Cmds3D => cmds3D ??= new List<Cmd3D>
        {
            C("BARRA", "Barra (perfil das Configurações) em cadeia de cliques no plano de trabalho", w => Ferr(Ferramenta3D.Barra), "L", "B", "LINE", "VG", "VIGA", "MEMBRO"),
            C("PILAR", "Pilar: clique o ponto; a altura vem dos níveis", w => Ferr(Ferramenta3D.Pilar), "PI", "PL", "COLUNA"),
            C("SELECIONAR", "Selecionar (clique, janela, cruzamento)", w => Ferr(Ferramenta3D.Selecionar), "S", "SEL"),
            C("MOVER", "Mover a seleção: ponto base e segundo ponto", w => CmdMoverCopiar(false), "M", "MV", "MOVE"),
            C("COPIAR", "Copiar a seleção: ponto base e destinos (vários; Enter termina)", w => CmdMoverCopiar(true), "CO", "CP", "COPY"),
            C("ESPELHAR", "Cópia espelhada: dois pontos da linha do espelho (em planta)", w => CmdEspelhar(), "MI", "MIRROR"),
            C("ROTACIONAR", "Girar a seleção em torno do eixo vertical: ponto base e ângulo (digite) ou direção (clique)", w => CmdRotacionar(), "RO", "ROTATE"),
            C("MATRIZ", "Matriz retangular da seleção (painel)", w => AbrirEdicaoEsp("matriz"), "AR", "ARRAY"),
            C("APARAR", "Aparar barras na barra limite (o lado clicado sai)", w => CmdApararEstender(true), "TR", "TRIM"),
            C("ESTENDER", "Estender barras até a barra limite (a ponta mais perto do clique)", w => CmdApararEstender(false), "EX", "EXTEND"),
            C("DIVIDIR", "Dividir barra (painel)", w => AbrirEdicaoEsp("dividir"), "DIV"),
            C("UNIR", "Unir nós coincidentes (painel)", w => AbrirEdicaoEsp("unir"), "J", "JOIN"),
            C("APAGAR", "Apagar a seleção", w => CmdApagar(), "E", "ERASE", "DEL"),
            C("PROPRIEDADES", "Propriedades da escolha (com várias: em lote)", w => CmdPropriedades(), "PR", "PROPS"),
            C("COPIARPROPRIEDADES", "Copiar perfil, material e giro de uma barra para outras", w => CmdMatch(), "MA", "MATCHPROP"),
            C("MEDIR", "Medir distância entre dois pontos (ΔX, ΔY, ΔZ)", w => CmdMedir(), "DI", "DIST", "ME", "MED", "MEA"),
            C("ID", "Coordenadas de um ponto", w => CmdId()),
            C("ZOOM", "Z = enquadrar tudo; Z O = a seleção", w => { if (w.Length > 1 && w[1].ToUpperInvariant().StartsWith("O")) vista3D?.EnquadrarSelecao(); else vista3D?.Enquadrar(); }, "Z", "ZE"),
            C("ZO", "Zoom na seleção", w => vista3D?.EnquadrarSelecao()),
            C("GRADE", "Grade estrutural: eixos, filas e níveis (0,@6,@6)", w => CmdGrade(), "GR", "GRID"),
            C("PLACA", "Placa de base no nó do pé do pilar", w => _ = AbrirPlacaEsp(null)),
            C("ENGASTE", "Engastar os nós escolhidos", w => CmdApoio(Enumerable.Repeat(true, 6).ToArray(), "engaste")),
            C("ROTULA", "Apoio articulado nos nós escolhidos", w => CmdApoio(new[] { true, true, true, false, false, false }, "articulado"), "FIXO"),
            C("LIVRE", "Tirar o apoio dos nós escolhidos", w => CmdApoio(new bool[6], "livre")),
            C("ROTULAR", "Rótula nas duas pontas das barras escolhidas (liga/desliga)", w => CmdRotular(), "RB"),
            C("CALCULAR", "Calcular (F5)", w => Calcular(), "CALC"),
            C("DIMENSIONAR", "Perfil mais leve da família que passa (motor)", w => _ = DimensionarEspacial(false)),
            C("DIMSEL", "Dimensionar só as barras escolhidas", w => _ = DimensionarEspacial(true)),
            C("RELATORIO", "Relatório NBR do motor + placas", w => AbrirRelatorio(), "RELATÓRIO"),
            C("TABELAS", "Tabelas de nós, barras e verificação", w => PainelTabelas.Show()),
            C("CASOS", "Casos de carga", w => AbrirCasosConforme()),
            C("NORMAL", "Diagrama N", w => AlternarDiagrama(Diagrama.Normal), "N"),
            C("CORTANTE", "Diagrama V", w => AlternarDiagrama(Diagrama.Cortante), "Q"),
            C("MOMENTO", "Diagrama M", w => AlternarDiagrama(Diagrama.Momento)),
            C("DEFORMADA", "Deformada", w => AlternarDiagrama(Diagrama.Deformada), "DEF"),
            C("CALOR", "Mapa de aproveitamento", w => MostrarDiagrama(Vista.Diag == Diagrama.Verificacao && Vista.MapaCalor ? Diagrama.Nenhum : Diagrama.Verificacao, true), "MAPA", "APROV"),
            C("TOPO", "Vista de topo (planta)", w => vista3D?.MostrarVista(VistaPadrao.Topo), "PLANTA"),
            C("FRENTE", "Vista de frente (XZ)", w => vista3D?.MostrarVista(VistaPadrao.Frente)),
            C("LADO", "Vista lateral direita (YZ)", w => vista3D?.MostrarVista(VistaPadrao.Lado), "DIREITA"),
            C("ISO", "Vista isométrica", w => vista3D?.MostrarVista(VistaPadrao.Iso), "HOME"),
            C("PERSPECTIVA", "Liga/desliga a perspectiva", w => { if (vista3D != null) { vista3D.Perspectiva = !vista3D.Perspectiva; Log(vista3D.Perspectiva ? "Perspectiva." : "Ortográfica."); } }, "PERSP"),
            C("REALISTA", "Perfis reais (liga/desliga)", w => DefinirRealista(!Config.Realista3D)),
            C("NIVEL", "Plano de trabalho no nível acima (+) ou abaixo (-): NIVEL + | NIVEL -", w => TrocarNivel(w.Length > 1 && w[1].StartsWith("-") ? -1 : 1)),
            C("ORTO", "ORTO (F8)", w => AlternarSnap("orto"), "ORTHO"),
            C("POLAR", "Polar (F10)", w => AlternarSnap("polar")),
            C("OSNAP", "Snap a objetos (F3)", w => AlternarSnap("osnap"), "OS"),
            C("SELTODAS", "Selecionar tudo (Ctrl+A)", w => vista3D?.Selecionar(EspIds("nos"), EspIds("barras")), "TODOS"),
            C("SELNOS", "Selecionar todos os nós", w => vista3D?.Selecionar(EspIds("nos"), null)),
            C("SELBASE", "Selecionar os nós da cota mais baixa", w => CmdSelBase()),
            C("DESFAZER", "Desfazer (Ctrl+Z)", w => Desfazer(), "U", "UNDO"),
            C("REFAZER", "Refazer (Ctrl+Y)", w => Refazer(), "REDO"),
            C("NOVO", "Modelo em branco (Ctrl+N)", w => Novo(), "NEW").SemModeloOk(),
            C("ABRIR", "Abrir arquivo (Ctrl+O)", w => AbrirDialogo(), "OPEN").SemModeloOk(),
            C("SALVAR", "Salvar (Ctrl+S)", w => Salvar(false), "SAVE", "QSAVE"),
            C("ASSISTENTE", "Gerar estrutura pelo assistente", w => AbrirAssistente(w.Length > 1 ? w[1].ToLowerInvariant() : null)).SemModeloOk(),
            C("ABA", "Ir para a aba da faixa: ABA nome", w => { foreach (var tab in Faixa.Tabs) if (w.Length > 1 && (tab.Header as string ?? "").StartsWith(w[1], StringComparison.OrdinalIgnoreCase)) Faixa.SelectedTabItem = tab; }).SemModeloOk(),
            C("AJUDA", "Lista dos comandos", w => Log("Comandos: " + string.Join(" · ", Cmds3D.Select(c => (c.Alias.Length > 0 ? c.Alias[0] + "/" : "") + c.Nome)) + ". Pontos: x,y,z  @dx,dy,dz  ou o comprimento na direção do cursor."), "?").SemModeloOk(),
        };

        static Cmd3D C(string nome, string desc, Action<string[]> fazer, params string[] alias) => new Cmd3D { Nome = nome, Desc = desc, Fazer = fazer, Alias = alias };

        void Ferr(Ferramenta3D f) { if (vista3D != null) { vista3D.Ferramenta = f; FocarVista(); } }
        void V3(Action<Vista3D> f) { if (vista3D != null) { f(vista3D); vista3D.InvalidateVisual(); } }
        internal void FocarVista() { if (vista3D != null && vista3D.IsVisible) vista3D.Focus(); }
        List<string> EspIds(string col) => (Esp?.Json[col] as JsonObject)?.Select(kv => kv.Key).ToList() ?? new List<string>();
        List<string> SelNos => vista3D?.Selecao.Nos.ToList() ?? new List<string>();
        List<string> SelBarras => vista3D?.Selecao.Barras.ToList() ?? new List<string>();

        // Linha de comando (Enter/Espaço), paleta, --cmd e botões que passam por Executar.
        internal void ExecutarComando(string t)
        {
            t = (t ?? "").Trim();
            if (t == "") return;
            if (Comando3D(t)) return;   // ME/MEDIR e snap único END/MID/INT... (Vista3D.Snap.cs)
            if (numeroPendente != null && Num.Ler(t, out double num)) { var f = numeroPendente; numeroPendente = null; _ = f(num); return; }
            if (vista3D != null && vista3D.Desenhando && PareceCoordenada(t)) { vista3D.EntrarTexto(t); return; }   // x,y,z  @dx,dy,dz  comprimento
            string[] w = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string c = w[0].ToUpperInvariant();
            var cmd = Cmds3D.FirstOrDefault(x => x.Nome == c || x.Alias.Contains(c));
            if (cmd == null)
            {
                if (PareceCoordenada(t)) Log("Coordenada sem comando: comece uma barra (L) e então digite " + t + ".");
                else Log("Comando desconhecido: " + t + " — digite AJUDA ou tecle F1.");
                return;
            }
            if (Esp == null && !cmd.SemModelo) { Log("Nenhum modelo aberto: Ctrl+N cria um em branco, Ctrl+O abre."); return; }
            numeroPendente = null;
            ultimoCmd = w[0];
            cmd.Fazer(w);
        }

        static bool PareceCoordenada(string t) => t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '@' || t[0] == '.' || t[0] == '#');

        void RepetirUltimo()
        {
            if (ultimoCmd == null) return;
            Log("» " + ultimoCmd + " (repetido)", false);
            ExecutarComando(ultimoCmd);
        }

        // Teclas da janela que não são da ferramenta em curso. true = consumiu.
        bool TeclaGlobal3D(Key k, bool ctrl, bool shift, bool editando)
        {
            if (ctrl)
                switch (k)
                {
                    case Key.K: AbrirPaleta(); return true;
                    case Key.Z when !editando: Desfazer(); return true;
                    case Key.Y when !editando: Refazer(); return true;
                    case Key.S: Salvar(shift); return true;
                    case Key.O: AbrirDialogo(); return true;
                    case Key.N: Novo(); return true;
                    case Key.Tab when docs.Count > 1: TrocarDoc((docAtivo + (shift ? docs.Count - 1 : 1)) % docs.Count); return true;
                    case Key.F4: FecharDoc(docAtivo); return true;
                    case Key.P: AbrirRelatorio(); return true;
                    case Key.M when !editando: ExecutarComando("MEDIR"); return true;
                    case Key.A when !editando: ExecutarComando("SELTODAS"); return true;
                    case Key.D1 when !editando: case Key.NumPad1 when !editando: ExecutarComando("PROPRIEDADES"); return true;
                    case Key.F9: Calcular(); return true;
                    case Key.NumPad7: ExecutarComando("TOPO"); return true;
                    case Key.NumPad1: ExecutarComando("FRENTE"); return true;
                    case Key.NumPad3: ExecutarComando("LADO"); return true;
                    case Key.NumPad5: ExecutarComando("ISO"); return true;
                }
            switch (k)
            {
                case Key.F1: MostrarAjuda(); return true;
                case Key.F2: MostrarHistorico(); return true;
                case Key.F3: AlternarSnap("osnap"); return true;
                case Key.F4: vista3D?.Enquadrar(); return true;   // ponytail: F4 = 3D OSNAP no AutoCAD; aqui enquadrar até haver snap em faces
                case Key.F5: Calcular(); return true;
                case Key.F6: Log("F6 (UCS dinâmico) ainda não existe: use PgUp/PgDn ou Modelo › Planos para trocar o plano de trabalho."); return true;
                case Key.F7: AlternarStatus(stGrade, v => V3(x => x.MostrarGradePlano = v), "Grade do plano"); return true;
                case Key.F8: AlternarSnap("orto"); return true;
                case Key.F9: AlternarStatus(stAtrair, v => V3(x => { if (x.Snap.Modos.HasFlag(ModoSnap.Grade) != v) x.Snap.Modos ^= ModoSnap.Grade; }), "Snap na grade"); return true;
                case Key.F10: AlternarSnap("polar"); return true;
                case Key.F11: AlternarSnap("rastreio"); return true;
                case Key.F12: AlternarStatus(stDin, v => V3(x => x.EntradaDinamica = v), "Entrada dinâmica"); return true;
                case Key.PageUp when !editando: TrocarNivel(+1); return true;
                case Key.PageDown when !editando: TrocarNivel(-1); return true;
                case Key.Delete when !editando && Comando.Text == "": CmdApagar(); return true;
                case Key.Escape:
                    if (Comando.Text != "") Comando.Text = "";
                    numeroPendente = null;
                    FocarVista();
                    return true;
                case Key.Enter when !editando && Comando.Text == "":
                case Key.Space when !editando && Comando.Text == "":
                    RepetirUltimo(); return true;
                case Key.Add when !editando && Comando.Text == "" && Vista.Diag != Diagrama.Nenhum: Escala(1.4); return true;
                case Key.Subtract when !editando && Comando.Text == "" && Vista.Diag != Diagrama.Nenhum: Escala(1 / 1.4); return true;
            }
            return false;
        }

        // F3/F8/F10/F11 e os comandos OSNAP/ORTO/POLAR: o estado é do Snap3D (Vista3D.Snap.cs); o rodapé espelha (SincronizarStatus3D).
        void AlternarSnap(string qual)
        {
            if (vista3D == null) return;
            var s = vista3D.Snap;
            vista3D.AplicarStatus(qual == "osnap" ? !s.Osnap : s.Osnap, qual == "orto" ? !s.Orto : s.Orto, qual == "polar" ? !s.Polar : s.Polar, qual == "rastreio" ? !s.Rastreio : s.Rastreio);
            Log((qual == "osnap" ? "Snap a objetos" : qual == "orto" ? "ORTO" : qual == "polar" ? "Polar" : "Rastreamento") + ((qual == "osnap" ? s.Osnap : qual == "orto" ? s.Orto : qual == "polar" ? s.Polar : s.Rastreio) ? " ligado." : " desligado."), false);
        }

        // ---------- plano de trabalho: PgUp/PgDn ----------

        // Níveis do motor + Térreo + níveis da grade (sem pavimento ainda); o plano vai para o de cima/baixo do atual.
        void TrocarNivel(int sentido)
        {
            if (Esp == null || vista3D == null) return;
            var l = Niveis.Lista(Esp.Json).Select(n => (n.Z, n.Nome)).ToList();
            foreach (double z in (Esp.Grade?["z"] as JsonArray)?.Select(x => (double)x) ?? new double[0])
                if (!l.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol)) l.Add((z, "z = " + Num.Fmt(z, 2) + " m"));
            l = l.OrderBy(n => n.Z).ToList();
            var pl = vista3D.PlanoAtual;
            double z0 = pl.Normal == 'Z' ? pl.Valor : 0;
            var alvo = sentido > 0 ? l.Where(n => n.Z > z0 + Niveis.Tol).Select(n => ((double, string)?)n).FirstOrDefault() : l.Where(n => n.Z < z0 - Niveis.Tol).Select(n => ((double, string)?)n).LastOrDefault();
            if (alvo == null) { Log(sentido > 0 ? "Não há nível acima do plano atual." : "Não há nível abaixo do plano atual."); return; }
            vista3D.PlanoAtual = PlanoTrabalho.Horizontal(alvo.Value.Item1, alvo.Value.Item2 + " (z = " + Num.Fmt(alvo.Value.Item1, 2) + " m)");
            Log("Plano de trabalho: " + vista3D.PlanoAtual.Nome + ".", false);
            StatusMsg.Text = "Plano de trabalho: " + vista3D.PlanoAtual.Nome + "  (PgUp/PgDn troca o nível)";
        }

        // ---------- grips ----------

        async Task AplicarGrip(GripAcao g)
        {
            var d = Esp; if (d == null) return;
            Vector3D v = g.Para - g.De;
            var t = GeometriaEdicao.Translacao(v.X, v.Y, v.Z);
            string onde = "(" + Num.Fmt(g.Para.X, 2) + "; " + Num.Fmt(g.Para.Y, 2) + "; " + Num.Fmt(g.Para.Z, 2) + ")";
            if (g.Tipo == "meio")
                await Espacial(dd => dd.Aplicar(m => g.Copiar ? GeometriaEdicao.Copiar(m, new string[0], new[] { g.Barra }, new[] { t }) : GeometriaEdicao.Mover(m, new string[0], new[] { g.Barra }, t)),
                               (g.Copiar ? "Barra " + g.Barra + " copiada" : "Barra " + g.Barra + " movida") + " (grip).");
            else if (!g.Copiar)
                await Espacial(dd => dd.Aplicar(m => GeometriaEdicao.Mover(m, new[] { g.Id }, new string[0], t)), "Nó " + g.Id + " levado a " + onde + " (grip).");
            else if (g.Barra != null && vista3D.PontasBarra(g.Barra) is (Point3D a, Point3D b))
            {
                Point3D outra = (a - g.De).Length < (b - g.De).Length ? b : a;
                string sec = (string)d.Json["barras"][g.Barra]?["secao"];
                try { var ids = await d.DesenharBarras(new[] { (outra, g.Para) }, sec); DepoisDeMudarEspacial("Barra " + string.Join(", ", ids) + " copiada da " + g.Barra + " até " + onde + " (grip + Ctrl)."); }
                catch (Exception ex) { ErroMotor(ex); }
            }
            else await Espacial(dd => dd.Adicionar("no", new JsonObject { ["x"] = g.Para.X, ["y"] = g.Para.Y, ["z"] = g.Para.Z }), "Nó novo em " + onde + " (grip + Ctrl).");
        }

        // ---------- seleção: escolha antes ou depois ----------

        // Com seleção, segue; sem, pede os objetos (clique soma; Enter termina; Esc cancela).
        void ComSelecao(string nome, Action seguir)
        {
            if (vista3D == null) return;
            if (vista3D.Selecao.Count > 0) { seguir(); return; }
            vista3D.PedirObjeto(nome + " — escolha os objetos (clique soma) e Enter", (o, p, mods) =>
            {
                var ids = ParaIds(o);
                if (ids.no != null || ids.barra != null) vista3D.Selecionar(ids.no != null ? new[] { ids.no } : null, ids.barra != null ? new[] { ids.barra } : null, ModifierKeys.Shift);
                return Task.FromResult(true);
            }, () => Log(nome + " cancelado."), () => { if (vista3D.Selecao.Count > 0) seguir(); else Log(nome + ": nada escolhido."); });
        }

        static (string no, string barra) ParaIds(object o) =>
            ((o as No3D)?.Origem is RefMotor rn ? rn.Id : null, (o as Barra3D)?.Origem is RefMotor rb ? rb.Id : null);

        static string P3(Point3D p) => "(" + Num.Fmt(p.X, 3) + "; " + Num.Fmt(p.Y, 3) + "; " + Num.Fmt(p.Z, 3) + ")";

        // M / CO: ponto base e segundo ponto (CO repete até Enter). Um PUT por operação = um Ctrl+Z.
        void CmdMoverCopiar(bool copiar)
        {
            string nome = copiar ? "COPIAR" : "MOVER";
            ComSelecao(nome, () =>
            {
                var nos = SelNos; var bs = SelBarras;
                vista3D.PedirPonto(nome + " — ponto base", null, async b =>
                {
                    vista3D.PedirPonto(nome + " — segundo ponto" + (copiar ? " (Enter termina)" : "") + " · digite @dx,dy,dz", b, async q =>
                    {
                        Vector3D v = q - b; var t = GeometriaEdicao.Translacao(v.X, v.Y, v.Z);
                        await Espacial(d => d.Aplicar(m => copiar ? GeometriaEdicao.Copiar(m, nos, bs, new[] { t }) : GeometriaEdicao.Mover(m, nos, bs, t)),
                                       (copiar ? "Copiado" : "Movido") + " de " + P3(b) + " até " + P3(q) + ".");
                        return copiar;   // COPIAR continua (base fixa)
                    }, null, true);
                    return true;
                });
            });
        }

        // MI: dois pontos da linha do espelho (em planta); a cópia fica, o original também (como "Apagar origem? Não").
        void CmdEspelhar()
        {
            ComSelecao("ESPELHAR", () =>
            {
                var nos = SelNos; var bs = SelBarras;
                vista3D.PedirPonto("ESPELHAR — 1º ponto da linha do espelho", null, async a =>
                {
                    vista3D.PedirPonto("ESPELHAR — 2º ponto da linha do espelho", a, async b =>
                    {
                        if (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) < 1e-9) { Log("Espelho: a linha precisa ter direção em planta."); return true; }
                        await Espacial(d => d.Aplicar(m => GeometriaEdicao.Copiar(m, nos, bs, new[] { GeometriaEdicao.EspelhoReta(a.X, a.Y, b.X, b.Y) })), "Espelhado na linha " + P3(a) + " → " + P3(b) + ".");
                        return false;
                    });
                    return true;
                });
            });
        }

        // RO: ponto base; ângulo digitado (graus, anti-horário visto de cima) ou clique na direção (ângulo a partir de +X).
        void CmdRotacionar()
        {
            ComSelecao("ROTACIONAR", () =>
            {
                var nos = SelNos; var bs = SelBarras;
                vista3D.PedirPonto("ROTACIONAR — ponto base (eixo vertical)", null, async b =>
                {
                    async Task Girar(double ang)
                    {
                        vista3D.LimparPedido(false); numeroPendente = null;
                        await Espacial(d => d.Aplicar(m => GeometriaEdicao.Mover(m, nos, bs, GeometriaEdicao.Rotacao(b.X, b.Y, ang))), "Girado " + Num.Fmt(ang, 2) + "° em torno de " + P3(b) + ".");
                    }
                    numeroPendente = Girar;
                    vista3D.PedirPonto("ROTACIONAR — digite o ângulo (°) e Enter, ou clique a direção", b, async q => { await Girar(Math.Atan2(q.Y - b.Y, q.X - b.X) * 180 / Math.PI); return false; });
                    return true;
                });
            });
        }

        // TR / EX: barra limite, depois as barras (repete até Enter). Cruzamento das retas a até 1 cm.
        void CmdApararEstender(bool aparar)
        {
            string nome = aparar ? "APARAR" : "ESTENDER";
            vista3D.PedirObjeto(nome + " — escolha a barra limite", (o, p0, m0) =>
            {
                string lim = ParaIds(o).barra;
                if (lim == null) { Log(nome + ": clique numa barra."); return Task.FromResult(true); }
                vista3D.PedirObjeto(nome + " — escolha a barra a " + (aparar ? "aparar (o lado clicado sai)" : "estender (a ponta perto do clique)") + " · Enter termina", async (o2, clique, m2) =>
                {
                    string alvo = ParaIds(o2).barra;
                    if (alvo == null || alvo == lim) return true;
                    var j = Esp.Json; var B = j["barras"]; var N = j["nos"];
                    double[] X(string id) => GeometriaEdicao.Xyz(N[id]);
                    string ni = (string)B[alvo]["ni"], nj = (string)B[alvo]["nj"];
                    var x = GeometriaEdicao.Cruzamento(X(ni), X(nj), X((string)B[lim]["ni"]), X((string)B[lim]["nj"]));
                    if (x == null) { Log(nome + ": a barra " + alvo + " não cruza a reta da barra " + lim + " (nem prolongada)."); return true; }
                    double t = x.Value.T, tc = 0.5;
                    if (clique is Point3D c) { var a = X(ni); var b = X(nj); double[] ab = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }; double l2 = ab.Sum(v => v * v); tc = ((c.X - a[0]) * ab[0] + (c.Y - a[1]) * ab[1] + (c.Z - a[2]) * ab[2]) / l2; }
                    string ponta;
                    if (aparar)
                    {
                        if (t <= 1e-6 || t >= 1 - 1e-6) { Log("APARAR: o limite não corta a barra " + alvo + " por dentro (use ESTENDER)."); return true; }
                        ponta = tc > t ? nj : ni;
                    }
                    else
                    {
                        if (t > 1e-6 && t < 1 - 1e-6) { Log("ESTENDER: o limite já corta a barra " + alvo + " (use APARAR)."); return true; }
                        ponta = t >= 1 ? nj : ni;
                    }
                    await Espacial(d => d.Aplicar(m => GeometriaEdicao.MoverPonta(m, alvo, ponta, x.Value.P)), "Barra " + alvo + (aparar ? " aparada" : " estendida") + " na barra " + lim + ".");
                    return true;
                });
                return Task.FromResult(true);
            });
        }

        void CmdApagar()
        {
            if (Esp == null) return;
            ComSelecao("APAGAR", () =>
            {
                var nos = SelNos; var bs = SelBarras;
                vista3D.LimparSelecao();
                _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Apagar(m, nos, bs)), "Apagado: " + Pl.N(bs.Count, "barra", "barras") + " e " + Pl.N(nos.Count, "nó", "nós") + " escolhidos.");
            });
        }

        void CmdPropriedades()
        {
            PainelProp.Show(); PainelProp.IsActive = true;
            if (vista3D != null && vista3D.Selecao.Count > 1) AbrirEdicaoEsp("propriedades");
        }

        // MA: barra de origem, depois as barras que recebem seção, material e giro (Enter termina).
        void CmdMatch()
        {
            vista3D.PedirObjeto("COPIAR PROPRIEDADES — escolha a barra de origem", (o, p, m) =>
            {
                string src = ParaIds(o).barra;
                if (src == null) return Task.FromResult(true);
                var b = Esp.Json["barras"][src];
                string sec = (string)b["secao"], mat = (string)b["material"]; double rot = (double)(b["rot"] ?? 0.0) * 180 / Math.PI;
                vista3D.PedirObjeto("COPIAR PROPRIEDADES de " + src + " (" + sec + ") — escolha as barras · Enter termina", async (o2, p2, m2) =>
                {
                    string dst = ParaIds(o2).barra;
                    if (dst != null && dst != src) await Espacial(d => d.Aplicar(mm => GeometriaEdicao.Propriedades(mm, new string[0], new[] { dst }, sec, mat, rot, null)), "Barra " + dst + ": " + sec + ", " + mat + " (de " + src + ").");
                    return true;
                });
                return Task.FromResult(true);
            });
        }

        // Medida simples (o Medir3D do wt-snap assume quando ligado em IniciarMedir3D).
        void CmdMedir()
        {
            if (Medir3D()) return;   // Medir 3D completo (Vista3D.Snap.cs, Medir3D.cs)
            vista3D.PedirPonto("MEDIR — 1º ponto", null, async a =>
            {
                vista3D.PedirPonto("MEDIR — 2º ponto", a, async b =>
                {
                    Vector3D v = b - a;
                    string s = "Distância " + Num.Fmt(v.Length, 3) + " m  ·  ΔX " + Num.Fmt(v.X, 3) + "  ΔY " + Num.Fmt(v.Y, 3) + "  ΔZ " + Num.Fmt(v.Z, 3) + " m";
                    Log(s); StatusMsg.Text = s;
                    return false;
                });
                return true;
            });
        }

        void CmdId() => vista3D.PedirPonto("ID — clique o ponto", null, async p => { Log("Ponto " + P3(p) + " m."); return false; });

        void CmdApoio(bool[] ap, string nome)
        {
            var nos = SelNos;
            if (nos.Count == 0) { Log("Escolha os nós (clique ou janela) e repita."); return; }
            _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Propriedades(m, nos, new string[0], null, null, null, ap)), "Apoio " + nome + " em " + Pl.N(nos.Count, "nó", "nós") + ".");
        }

        void CmdRotular()
        {
            var bs = SelBarras;
            if (bs.Count == 0) { Log("Escolha as barras e repita ROTULAR."); return; }
            bool ligar = Esp.Json["barras"][bs[0]]?["params"]?["rotula_i"] == null;
            _ = Espacial(d => d.Aplicar(m =>
            {
                foreach (string b in bs)
                {
                    var p = m["barras"][b]["params"] as JsonObject ?? (JsonObject)(m["barras"][b]["params"] = new JsonObject());
                    if (ligar) { p["rotula_i"] = true; p["rotula_j"] = true; } else { p.Remove("rotula_i"); p.Remove("rotula_j"); }
                }
                return m;
            }), (ligar ? "Rótulas nas pontas de " : "Rótulas tiradas de ") + Pl.N(bs.Count, "barra", "barras") + ".");
        }

        void CmdSelBase()
        {
            var nos = Esp.Json["nos"].AsObject();
            if (nos.Count == 0) return;
            double z0 = nos.Min(kv => (double)kv.Value["z"]);
            vista3D.Selecionar(nos.Where(kv => Math.Abs((double)kv.Value["z"] - z0) <= Niveis.Tol).Select(kv => kv.Key), null);
        }

        // ---------- GR: grade estrutural (Plant 3D Create Grid) ----------

        void CmdGrade()
        {
            var d = Esp;
            var g = d.Grade;
            string Txt(string k, string padrao) => g?[k] is JsonArray a ? string.Join(",", a.Select(x => ((double)x).ToString("0.###", CultureInfo.InvariantCulture))) : padrao;
            var j = new JanelaGrade(Txt("x", "0,@6,@6"), Txt("y", "0,@5"), Txt("z", "0,@3")) { Owner = this };
            if (Capturas.Ativo) { janelaGrade = j; return; }
            if (j.ShowDialog() == true) _ = AplicarGrade(j.X, j.Y, j.Z);
        }
        internal JanelaGrade janelaGrade;   // capturas: a janela fica aberta para renderizar

        // Grava a grade no documento e cria um pavimento em cada nível acima do térreo que ainda não tem (um lote = um Ctrl+Z).
        internal async Task AplicarGrade(double[] xs, double[] ys, double[] zs)
        {
            var d = Esp; if (d == null) return;
            string Letra(int i) => i < 26 ? ((char)('A' + i)).ToString() : "A" + (i - 25);
            d.Grade = new JsonObject
            {
                ["x"] = new JsonArray(xs.Select(v => (JsonNode)v).ToArray()), ["y"] = new JsonArray(ys.Select(v => (JsonNode)v).ToArray()), ["z"] = new JsonArray(zs.Select(v => (JsonNode)v).ToArray()),
                ["nx"] = new JsonArray(xs.Select((v, i) => (JsonNode)(i + 1).ToString(CultureInfo.InvariantCulture)).ToArray()), ["ny"] = new JsonArray(ys.Select((v, i) => (JsonNode)Letra(i)).ToArray()),
            };
            var pav = Niveis.Lista(d.Json);
            var itens = new JsonArray();
            int k = 0;
            foreach (double z in zs.Where(z => z > Niveis.Tol))
                if (!pav.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol))
                {
                    string nome; do nome = "N" + (++k); while (d.Json["pavimentos"]?[nome] != null);
                    itens.Add(new JsonObject { ["tipo"] = "pavimento", ["dados"] = new JsonObject { ["nome"] = nome, ["z"] = z, ["diafragma"] = false } });
                }
            if (itens.Count > 0) await Espacial(dd => dd.Executar(() => Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens })), null);
            else { alterado = true; AtualizarTitulo(); }
            AplicarGradeNaVista();
            Log("Grade: " + Pl.N(xs.Length, "eixo", "eixos") + " em X, " + Pl.N(ys.Length, "fila", "filas") + " em Y, " + Pl.N(zs.Length, "nível", "níveis") +
                (itens.Count > 0 ? " (" + Pl.N(itens.Count, "pavimento novo", "pavimentos novos") + ")" : "") + ". Cruzamentos atraem o cursor; PgUp/PgDn sobe e desce o plano.");
        }

        internal void AplicarGradeNaVista()
        {
            if (vista3D == null) return;
            var g = Esp?.Grade;
            double[] A(string k) => (g?[k] as JsonArray)?.Select(x => (double)x).ToArray() ?? new double[0];
            string[] S(string k) => (g?[k] as JsonArray)?.Select(x => (string)x).ToArray() ?? new string[0];
            vista3D.EixosX = A("x"); vista3D.EixosY = A("y"); vista3D.NomesX = S("nx"); vista3D.NomesY = S("ny");
            vista3D.InvalidateVisual();
        }

        // Liga os ganchos da Vista3D com a janela (uma vez, ao criar a vista).
        void LigarComandos3D()
        {
            vista3D.AplicarGrip = AplicarGrip;
            vista3D.CursorMudou += p => Coord.Text = p is Point3D q ? "X " + Num.Fmt(q.X, 3).PadLeft(8) + "   Y " + Num.Fmt(q.Y, 3).PadLeft(8) + "   Z " + Num.Fmt(q.Z, 3).PadLeft(8) + " m" : "";
vista3D.MostrarGradePlano = stGrade.IsChecked == true; vista3D.EntradaDinamica = stDin.IsChecked == true;
            SincronizarStatus3D();
        }
    }

    // GR: eixos (X), filas (Y) e níveis (Z) com a sintaxe do Plant 3D (0,@6,@6). Nomes: 1, 2, 3… / A, B, C…
    public class JanelaGrade : JanelaEscura
    {
        public double[] X, Y, Z;
        readonly TextBox cx, cy, cz; readonly TextBlock erro = T("", 12, BVermelho);

        public JanelaGrade(string x, string y, string z)
        {
            Title = "Grade estrutural"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(T("Posições separadas por vírgula, em metros, ponto decimal. @ = a partir da anterior: 0,@6,@6,@7.5. Eixos em X recebem 1, 2, 3…; filas em Y recebem A, B, C…; cada nível acima de 0 vira um pavimento (plano de trabalho com PgUp/PgDn).", 11.5, BFraco));
            TextBox Linha(string rot, string v)
            {
                var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(T(rot, 12, BFraco));
                var tb = Campo(v, 250); tb.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(tb, 1); g.Children.Add(tb);
                sp.Children.Add(g); return tb;
            }
            cx = Linha("Eixos em X (m)", x); cy = Linha("Filas em Y (m)", y); cz = Linha("Níveis em Z (m)", z);
            sp.Children.Add(erro);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            bts.Children.Add(B("Cancelar", Close)); bts.Children.Add(B("Criar grade", Confirmar, true));
            sp.Children.Add(bts);
            Content = sp;
        }

        internal bool Ler()
        {
            try
            {
                X = GeometriaEdicao.LerCotas(cx.Text); Y = GeometriaEdicao.LerCotas(cy.Text); Z = GeometriaEdicao.LerCotas(cz.Text);
                if (X.Length == 0 || Y.Length == 0) throw new FormatException("informe ao menos um eixo e uma fila");
                return true;
            }
            catch (FormatException e) { erro.Text = e.Message; return false; }
        }

        void Confirmar() { if (Ler()) DialogResult = true; }
    }
}
