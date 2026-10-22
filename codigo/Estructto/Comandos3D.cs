// Linha de comando e teclado do Estructto 3D, como no AutoCAD/Plant 3D (docs/v3/PESQUISA-PLANT.md §5, docs/MODELO-ESPACIAL.md
// "Comandos e teclado"). Aliases + Enter/Espaço; Enter/Espaço com a linha vazia repete o último comando; Esc em cascata
// (Vista3D.Tecla); F1–F12; PgUp/PgDn troca o nível do plano de trabalho; Del apaga; Ctrl+A/Z/Y/1/M.
// Todo comando que age sobre objetos funciona nas duas ordens (docs/v3/FLUXO-COMANDOS.md): com seleção, age nela; sem, pede
// na vista (ComEscolha: clique soma, Shift tira, janela/cruzamento, Enter aplica, Esc cancela) e depois o resto (ponto base,
// destino, valor na linha de comando — PedirValor). Pontos com atração, ORTO, entrada dinâmica ou x,y,z / @dx,dy,dz digitados.
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
            C("NO", "Nó: clique o ponto ou digite x,y,z (vários; Enter termina)", w => CmdNo(), "NÓ", "NODE", "PT"),
            C("PILAR", "Pilar: clique o ponto; a altura vem dos níveis", w => Ferr(Ferramenta3D.Pilar), "PI", "PL", "COLUNA"),
            C("SELECIONAR", "Selecionar (clique, janela, cruzamento)", w => Ferr(Ferramenta3D.Selecionar), "S", "SEL"),
            C("MOVER", "Mover a seleção: ponto base e segundo ponto", w => CmdMoverCopiar(false), "M", "MV", "MOVE"),
            C("COPIAR", "Copiar a seleção: ponto base e destinos (vários; Enter termina)", w => CmdMoverCopiar(true), "CO", "CP", "COPY"),
            C("ESPELHAR", "Cópia espelhada: dois pontos da linha do espelho (em planta)", w => CmdEspelhar(), "MI", "MIRROR"),
            C("ROTACIONAR", "Girar a seleção em torno do eixo vertical: ponto base e ângulo (digite) ou direção (clique)", w => CmdRotacionar(), "RO", "ROTATE"),
            C("MATRIZ", "Matriz retangular: escolha os objetos, depois o painel", w => ComEscolha("MATRIZ", null, (n, b) => AbrirEdicaoEsp("matriz")), "AR", "ARRAY"),
            C("APARAR", "Aparar: barras limite, Enter, e as barras (o lado clicado sai)", w => CmdApararEstender(true), "TR", "TRIM"),
            C("ESTENDER", "Estender: barras limite, Enter, e as barras (a ponta perto do clique)", w => CmdApararEstender(false), "EX", "EXTEND"),
            C("DIVIDIR", "Dividir barras: escolha as barras, depois o painel", w => ComEscolha("DIVIDIR", "barra", (n, b) => AbrirEdicaoEsp("dividir")), "DIV"),
            C("UNIR", "Unir nós coincidentes: escolha os nós (Enter sem nada = todos), depois o painel", w => ComEscolha("UNIR", "no", (n, b) => AbrirEdicaoEsp("unir"), null, true), "J", "JOIN"),
            C("APAGAR", "Apagar (escolha antes ou depois)", w => CmdApagar(), "E", "ERASE", "DEL"),
            C("PROPRIEDADES", "Propriedades da escolha (com várias: em lote)", w => CmdPropriedades(), "PR", "PROPS"),
            C("LOTE", "Propriedades em lote: escolha, depois o painel (seção, material, giro, apoio)", w => ComEscolha("EM LOTE", null, (n, b) => AbrirEdicaoEsp("propriedades")), "EMLOTE"),
            C("COPIARPROPRIEDADES", "Copiar seção, material, giro e cardinal de uma barra para outras", w => CmdMatch(), "MA", "MATCHPROP"),
            C("SECAO", "Seção nas barras: perfil do modelo ou do catálogo (Enter = a das Configurações)", w => CmdSecao(Arg(w)), "SEC", "PERFIL"),
            C("MATERIAL", "Material nas barras (Enter = o das Configurações)", w => CmdMaterial(Arg(w)), "MAT"),
            C("GIRO", "Giro da seção (°) nas barras (Enter = o das Configurações)", w => CmdGiro(Arg(w))),
            C("CARDINAL", "Ponto cardinal (1..9, 10 centroide) nas barras", w => CmdCardinal(Arg(w)), "CARD"),
            C("DISPOSICAO", "Disposição do perfil: giro, espelho e cardinal com desenho", w => CmdDisposicao(), "DISP"),
            C("APLICARCONFIG", "Seção, material, giro e cardinal das Configurações nas barras", w => CmdAplicarConfig(), "APLICAR"),
            C("CARGANO", "Carga nodal no caso ativo: Fx,Fy,Fz[,Mx,My,Mz] nos nós", w => CmdCargaNodal(Arg(w)), "CN", "CARGA", "CARGANODAL"),
            C("CARGADIST", "Carga distribuída (kN/m, global) nas barras; trapezoidal: q1/q2", w => CmdCargaDist(Arg(w)), "CD", "CARGADISTRIBUIDA"),
            C("CARGAPONTUAL", "Carga pontual nas barras: a (m desde ni ou M) e Fx,Fy,Fz", w => CmdCargaPontual(), "CPT"),
            C("CARGAAREA", "Carga em área (kN/m²) nas vigas escolhidas ou de um nível", w => CmdCargaArea(), "CAREA", "LAJE"),
            C("VERIFICAR", "Verificação detalhada de uma barra (escolha antes ou depois)", w => CmdVerificar(), "VB", "COMPARAR"),
            C("MEDIR", "Medir distância entre dois pontos (ΔX, ΔY, ΔZ)", w => CmdMedir(), "DI", "DIST", "ME", "MED", "MEA"),
            C("ID", "Coordenadas de um ponto", w => CmdId()),
            C("ZOOM", "Z = enquadrar tudo; Z O = a seleção", w => { if (w.Length > 1 && w[1].ToUpperInvariant().StartsWith("O")) vista3D?.EnquadrarSelecao(); else vista3D?.Enquadrar(); }, "Z", "ZE"),
            C("ZO", "Zoom na seleção", w => vista3D?.EnquadrarSelecao()),
            C("GRADE", "Grade estrutural: eixos, filas e níveis (0,@6,@6)", w => CmdGrade(), "GR", "GRID"),
            C("PLACA", "Placa de base no nó do pé do pilar (escolha antes ou depois)", w => CmdPlaca()),
            C("ENGASTE", "Engaste (6 GL travados) nos nós", w => CmdApoio(Engaste6, "ENGASTE", "engaste")),
            C("ROTULA", "Apoio articulado (translações travadas, giros livres) nos nós", w => CmdApoio(Articulado6, "ARTICULADO", "articulado"), "FIXO", "ARTICULADO"),
            C("MOVEL", "Apoio móvel (rolete: só uz travado) nos nós", w => CmdApoio(Movel6, "MÓVEL", "móvel"), "ROLETE"),
            C("LIVRE", "Tira apoio e molas dos nós", w => CmdApoio(new bool[6], "LIVRE", "livre (sem apoio nem molas)")),
            C("MOLAS", "Molas nos nós: kx,ky,kz[,krx,kry,krz] (kN/m, kN·m/rad)", w => CmdMolas(Arg(w)), "MOLA"),
            C("ROTULAR", "Rótula nas duas pontas das barras (liga/desliga)", w => CmdRotular(), "RB"),
            C("CALCULAR", "Calcular (F5)", w => Calcular(), "CALC"),
            C("DIMENSIONAR", "Perfil mais leve da família que passa (motor)", w => _ = DimensionarEspacial(false)),
            C("DIMSEL", "Dimensionar só as barras escolhidas (escolha antes ou depois)", w => ComEscolha("DIMSEL", "barra", (n, b) => _ = DimensionarEspacial(true))),
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
            C("REALISTA", "Perfis reais (liga/desliga)", w => DefinirRealista(Config.Estilo3D != EstiloVisual.Realista)),
            C("ESTILO", "Estilo visual: ESTILO analitico|sombreado|realista (sem nome = o próximo)", w => CmdEstilo(w), "VS", "VSCURRENT").SemModeloOk(),   // estilo não depende de modelo aberto
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
            if (valorPendente != null && vista3D?.Pedindo == true) { _ = valorPendente(t); return; }   // resposta da pergunta em curso (PedirValor)
            valorPendente = null;   // o pedido acabou (Esc, outra ferramenta): nada fica pendente
            if (Comando3D(t)) return;   // ME/MEDIR e snap único END/MID/INT... (Vista3D.Snap.cs)
            if (numeroPendente != null && vista3D?.Pedindo != true) numeroPendente = null;   // RO cancelado: o número não gira mais nada
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
            numeroPendente = null; valorPendente = null;
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
                    case Key.N: NovoProjeto(); return true;
                    case Key.Tab when docs.Count > 1: TrocarDoc((docAtivo + (shift ? docs.Count - 1 : 1)) % docs.Count); return true;
                    case Key.F4: FecharDoc(docAtivo); return true;
                    case Key.P: AbrirRelatorio(); return true;
                    case Key.M when !editando: ExecutarComando("MEDIR"); return true;
                    case Key.A when !editando: ExecutarComando("SELTODAS"); return true;
                    case Key.D1 when !editando: case Key.NumPad1 when !editando: ExecutarComando("PROPRIEDADES"); return true;
                    case Key.F9: Calcular(); return true;
                    case Key.R when vista3D?.Desenhando == true: GirarDesenho3D(90); return true;   // Ctrl+R gira o perfil das próximas barras
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
                case Key.F6: Log("F6 (UCS dinâmico) ainda não existe: use PgUp/PgDn ou Estrutura › Planos para trocar o plano de trabalho."); return true;
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
                    else if (vista3D?.Pedindo == true) vista3D.LimparPedido();   // Esc com o foco na linha de comando cancela o comando também
                    numeroPendente = null; valorPendente = null;
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

        // Níveis do motor + Térreo (os níveis da grade viram pavimentos no GR); o plano vai para o de cima/baixo do atual.
        void TrocarNivel(int sentido)
        {
            if (Esp == null || vista3D == null) return;
            var l = Niveis.Lista(Esp.Json).Select(n => (n.Z, n.Nome)).OrderBy(n => n.Z).ToList();
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
                var src = d.Json["barras"][g.Barra];   // a cópia leva seção, material, giro e cardinal da barra de origem
                try { var ids = await d.DesenharBarras(new[] { (outra, g.Para) }, (string)src?["secao"], false, (string)src?["material"], (double)(src?["rot"] ?? 0.0), DocumentoEspacial.CardinalDe(src));DepoisDeMudarEspacial("Barra " + string.Join(", ", ids) + " copiada da " + g.Barra + " até " + onde + " (grip + Ctrl)."); }
                catch (Exception ex) { ErroMotor(ex); }
            }
            else await Espacial(dd => dd.Adicionar("no", new JsonObject { ["x"] = g.Para.X, ["y"] = g.Para.Y, ["z"] = g.Para.Z }), "Nó novo em " + onde + " (grip + Ctrl).");
        }

        // ---------- seleção: escolha antes ou depois (docs/v3/FLUXO-COMANDOS.md) ----------

        static readonly List<string> Nada = new List<string>();
        static readonly bool[] Engaste6 = { true, true, true, true, true, true }, Articulado6 = { true, true, true, false, false, false }, Movel6 = { false, false, true, false, false, false };
        static string Arg(string[] w) => w.Length > 1 ? string.Join(" ", w.Skip(1)) : null;
        static string Ids(List<string> l) => string.Join(", ", l.Take(6)) + (l.Count > 6 ? "…" : "");

        // Substantivo→verbo: a seleção (só o tipo que o comando aceita) vale e o comando segue na hora. Verbo→substantivo: sem
        // seleção válida, pede na vista ("Selecione objetos": clique soma, Shift tira, janela/cruzamento, pré-realce do tipo;
        // Enter, Espaço ou botão direito aplica; Esc cancela). tipo: "no", "barra" ou null (os dois). vazioTudo: Enter sem nada
        // escolhido segue com as listas vazias (= modelo inteiro, UNIR).
        void ComEscolha(string nome, string tipo, Action<List<string>, List<string>> seguir, string oque = null, bool vazioTudo = false)
        {
            if (vista3D == null || Esp == null) return;
            List<string> N() => tipo == "barra" ? new List<string>() : SelNos;
            List<string> B() => tipo == "no" ? new List<string>() : SelBarras;
            var n0 = N(); var b0 = B();
            if (n0.Count + b0.Count > 0) { seguir(n0, b0); return; }
            vista3D.LimparSelecao();   // a escolha de outro tipo não vale para este comando
            oque ??= tipo == "no" ? "os nós" : tipo == "barra" ? "as barras" : "os objetos";
            vista3D.PedirSelecao(nome + " — selecione " + oque + " (Enter termina" + (vazioTudo ? "; Enter sem nada = todos" : "") + ", Esc cancela)", tipo, () =>
            {
                var n = N(); var b = B();
                if (n.Count + b.Count == 0 && !vazioTudo) Log(nome + ": nada selecionado.");
                else seguir(n, b);
            }, () => { valorPendente = null; vista3D.LimparSelecao(); Log(nome + " cancelado."); });
        }

        static (string no, string barra) ParaIds(object o) =>
            ((o as No3D)?.Origem is RefMotor rn ? rn.Id : null, (o as Barra3D)?.Origem is RefMotor rb ? rb.Id : null);

        static string P3(Point3D p) => "(" + Num.Fmt(p.X, 3) + "; " + Num.Fmt(p.Y, 3) + "; " + Num.Fmt(p.Z, 3) + ")";

        // ---------- perguntas na linha de comando (valor, nome, lista) ----------

        Func<string, Task> valorPendente;   // resposta digitada do pedido em curso (ExecutarComando entrega)
        readonly Dictionary<string, string> ultimoValor = new Dictionary<string, string>();   // a última resposta vira o padrão

        // AutoCAD: "Pergunta <padrão>:". Enter/Espaço com a linha vazia ou botão direito = padrão; Esc cancela. aceitar devolve
        // false para resposta inválida (e já explicou no histórico): pergunta de novo.
        void PedirValor(string chave, string prompt, string padrao, Func<string, Task<bool>> aceitar)
        {
            if (ultimoValor.TryGetValue(chave, out var u)) padrao = u;
            async Task Responder(string t)
            {
                valorPendente = null; vista3D.LimparPedido(false);
                if (await aceitar(t.Trim())) ultimoValor[chave] = t.Trim();
                else PedirValor(chave, prompt, padrao, aceitar);
            }
            vista3D.PedirValor(prompt + (padrao != null ? " <" + padrao + ">" : "") + " (Enter " + (padrao != null ? "aceita" : "termina") + ", Esc cancela)",
                () => { if (padrao != null) _ = Responder(padrao); else { valorPendente = null; Log(prompt.Split(' ')[0] + ": nada digitado, cancelado."); } },
                () => { valorPendente = null; Log(prompt.Split(' ')[0] + " cancelado."); });
            valorPendente = Responder;
            FocarVista();
        }

        // Com o valor já na linha de comando (SECAO W310x38.7), usa; senão pergunta.
        void Perguntar(string chave, string arg, string prompt, string padrao, Func<string, Task<bool>> aceitar)
        {
            if (arg != null) _ = aceitar(arg.Trim());
            else PedirValor(chave, prompt, padrao, aceitar);
        }

        // "0,0,-10" / "1;2;3": de min a max números (ponto decimal); null = inválido.
        internal static double[] LerLista(string t, int min, int max)
        {
            var p = (t ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < min || p.Length > max) return null;
            var r = new double[p.Length];
            for (int i = 0; i < p.Length; i++)
                if (!double.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]) || !double.IsFinite(r[i])) return null;
            return r;
        }

        static JsonArray Vetor(IEnumerable<double> v) => new JsonArray(v.Select(x => (JsonNode)x).ToArray());
        static string Nome(JsonNode col, string t) => (col as JsonObject)?.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, t, StringComparison.OrdinalIgnoreCase));

        // ---------- edição ----------

        // M / CO: ponto base e segundo ponto (CO repete até Enter). Um PUT por operação = um Ctrl+Z.
        void CmdMoverCopiar(bool copiar)
        {
            string nome = copiar ? "COPIAR" : "MOVER";
            ComEscolha(nome, null, (nos, bs) =>
            {
                vista3D.PedirPonto(nome + " — ponto base · digite x,y,z", null, async b =>
                {
                    vista3D.PedirPonto(nome + " — segundo ponto" + (copiar ? " (Enter termina)" : "") + " · digite @dx,dy,dz", b, async q =>
                    {
                        Vector3D v = q - b; var t = GeometriaEdicao.Translacao(v.X, v.Y, v.Z);
                        await Espacial(d => d.Aplicar(m => copiar ? GeometriaEdicao.Copiar(m, nos, bs, new[] { t }) : GeometriaEdicao.Mover(m, nos, bs, t)),
                                       (copiar ? "Copiado" : "Movido") + " de " + P3(b) + " até " + P3(q) + ".");
                        return copiar;   // COPIAR continua (base fixa)
                    }, () => Log(nome + " terminado."), true);
                    return true;
                }, () => Log(nome + " cancelado."));
            });
        }

        // MI: dois pontos da linha do espelho (em planta); a cópia fica, o original também (como "Apagar origem? Não").
        void CmdEspelhar()
        {
            ComEscolha("ESPELHAR", null, (nos, bs) =>
            {
                vista3D.PedirPonto("ESPELHAR — 1º ponto da linha do espelho", null, async a =>
                {
                    vista3D.PedirPonto("ESPELHAR — 2º ponto da linha do espelho", a, async b =>
                    {
                        if (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) < 1e-9) { Log("ESPELHAR: a linha precisa ter direção em planta; clique outro ponto."); return true; }
                        await Espacial(d => d.Aplicar(m => GeometriaEdicao.Copiar(m, nos, bs, new[] { GeometriaEdicao.EspelhoReta(a.X, a.Y, b.X, b.Y) })), "Espelhado na linha " + P3(a) + " → " + P3(b) + ".");
                        return false;
                    }, () => Log("ESPELHAR cancelado."));
                    return true;
                }, () => Log("ESPELHAR cancelado."));
            });
        }

        // RO: ponto base; ângulo digitado (graus, anti-horário visto de cima) ou clique na direção (ângulo a partir de +X).
        void CmdRotacionar()
        {
            ComEscolha("ROTACIONAR", null, (nos, bs) =>
            {
                vista3D.PedirPonto("ROTACIONAR — ponto base (eixo vertical)", null, async b =>
                {
                    async Task Girar(double ang)
                    {
                        vista3D.LimparPedido(false); numeroPendente = null;
                        await Espacial(d => d.Aplicar(m => GeometriaEdicao.Mover(m, nos, bs, GeometriaEdicao.Rotacao(b.X, b.Y, ang))), "Girado " + Num.Fmt(ang, 2) + "° em torno de " + P3(b) + ".");
                    }
                    numeroPendente = Girar;
                    vista3D.PedirPonto("ROTACIONAR — digite o ângulo (°) e Enter, ou clique a direção", b, async q => { numeroPendente = null; await Girar(Math.Atan2(q.Y - b.Y, q.X - b.X) * 180 / Math.PI); return false; },
                                       () => { numeroPendente = null; Log("ROTACIONAR cancelado."); });
                    return true;
                }, () => Log("ROTACIONAR cancelado."));
            });
        }

        // TR / EX: barras limite (escolhidas antes, ou escolha + Enter), depois as barras a aparar/estender (repete até Enter).
        // Cruzamento das retas a até 1 cm; com vários limites vale o cruzamento mais perto do clique (aparar) ou da ponta clicada (estender).
        void CmdApararEstender(bool aparar)
        {
            string nome = aparar ? "APARAR" : "ESTENDER";
            ComEscolha(nome, "barra", (nn, lims) =>
            {
                string fim = nome + " terminado.";
                vista3D.PedirObjeto(nome + " (" + Pl.N(lims.Count, "limite", "limites") + ") — clique a barra a " + (aparar ? "aparar (o lado clicado sai)" : "estender (a ponta perto do clique)") + " (Enter termina, Esc cancela)", async (o2, clique, m2) =>
                {
                    string alvo = ParaIds(o2).barra;
                    if (alvo == null || lims.Contains(alvo)) return true;
                    var j = Esp.Json; var B = j["barras"]; var N = j["nos"];
                    double[] X(string id) => GeometriaEdicao.Xyz(N[id]);
                    string ni = (string)B[alvo]["ni"], nj = (string)B[alvo]["nj"];
                    double tc = 0.5;
                    if (clique is Point3D c) { var a = X(ni); var b = X(nj); double[] ab = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }; double l2 = ab.Sum(v => v * v); tc = ((c.X - a[0]) * ab[0] + (c.Y - a[1]) * ab[1] + (c.Z - a[2]) * ab[2]) / l2; }
                    var xs = lims.Where(l => B[l] != null).Select(l => (l, x: GeometriaEdicao.Cruzamento(X(ni), X(nj), X((string)B[l]["ni"]), X((string)B[l]["nj"])))).Where(c => c.x != null).Select(c => (c.l, P: c.x.Value.P, T: c.x.Value.T)).ToList();
                    if (xs.Count == 0) { Log(nome + ": a barra " + alvo + " não cruza a reta de " + (lims.Count == 1 ? "a barra " + lims[0] : "nenhum limite") + " (nem prolongada)."); return true; }
                    string ponta; (string l, double[] P, double T) esc;
                    if (aparar)
                    {
                        var dentro = xs.Where(c => c.T > 1e-6 && c.T < 1 - 1e-6).ToList();
                        if (dentro.Count == 0) { Log("APARAR: o limite não corta a barra " + alvo + " por dentro (use ESTENDER)."); return true; }
                        esc = dentro.OrderBy(c => Math.Abs(c.T - tc)).First();
                        ponta = tc > esc.T ? nj : ni;
                    }
                    else
                    {
                        var fora = xs.Where(c => c.T <= 1e-6 || c.T >= 1 - 1e-6).ToList();
                        if (fora.Count == 0) { Log("ESTENDER: o limite já corta a barra " + alvo + " (use APARAR)."); return true; }
                        var lado = fora.Where(c => tc > 0.5 ? c.T >= 1 - 1e-6 : c.T <= 1e-6).ToList();   // a ponta perto do clique, se der
                        esc = (lado.Count > 0 ? lado : fora).OrderBy(c => c.T >= 1 - 1e-6 ? c.T - 1 : -c.T).First();
                        ponta = esc.T >= 1 - 1e-6 ? nj : ni;
                    }
                    await Espacial(d => d.Aplicar(m => GeometriaEdicao.MoverPonta(m, alvo, ponta, esc.P)), "Barra " + alvo + (aparar ? " aparada" : " estendida") + " na barra " + esc.l + ".");
                    return true;
                }, () => { vista3D.LimparSelecao(); Log(fim); }, () => { vista3D.LimparSelecao(); Log(fim); }, "barra");
            }, "as barras limite");
        }

        void CmdApagar()
        {
            if (Esp == null) return;
            ComEscolha("APAGAR", null, (nos, bs) =>
            {
                vista3D.LimparSelecao();
                _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Apagar(m, nos, bs)), "Apagado: " + Pl.N(bs.Count, "barra", "barras") + " e " + Pl.N(nos.Count, "nó", "nós") + " escolhidos.");
            });
        }

        void CmdPropriedades()
        {
            PainelProp.Show(); PainelProp.IsActive = true;
            if (vista3D != null && vista3D.Selecao.Count > 1) AbrirEdicaoEsp("propriedades");
        }

        // MA: barra de origem (uma escolhida antes, ou clique) e as barras que recebem seção, material, giro e cardinal
        // (escolhidas antes junto com a origem clicada, ou escolha + Enter). Uma operação = um Ctrl+Z.
        void CmdMatch()
        {
            if (vista3D == null || Esp == null) return;
            void Destinos(string src)
            {
                var b = Esp.Json["barras"][src]; if (b == null) return;
                string sec = (string)b["secao"], mat = (string)b["material"]; double rot = (double)(b["rot"] ?? 0.0) * 180 / Math.PI; int card = DocumentoEspacial.CardinalDe(b);
                ComEscolha("COPIAR PROPRIEDADES de " + src, "barra", (nn, dst) =>
                {
                    dst = dst.Where(x => x != src).ToList();
                    if (dst.Count == 0) { Log("COPIAR PROPRIEDADES: escolha barras diferentes da origem " + src + "."); return; }
                    _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Cardinal(GeometriaEdicao.Propriedades(m, Nada, dst, sec, mat, rot, null), dst, card)),
                                 Pl.N(dst.Count, "barra", "barras") + " (" + Ids(dst) + ") com " + sec + ", " + mat + ", giro " + Num.Fmt(rot, 1) + "°, cardinal " + card + " (de " + src + ").");
                }, "as barras que recebem " + sec + " / " + mat);
            }
            var sel = SelBarras;
            if (sel.Count == 1) { vista3D.LimparSelecao(); Destinos(sel[0]); return; }
            vista3D.PedirObjeto("COPIAR PROPRIEDADES — clique a barra de origem" + (sel.Count > 1 ? " (as " + sel.Count + " escolhidas recebem)" : "") + " (Esc cancela)", (o, p, m) =>
            {
                string src = ParaIds(o).barra;
                if (src != null) Destinos(src);
                return Task.FromResult(src == null);
            }, () => Log("COPIAR PROPRIEDADES cancelado."), null, "barra");
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

        // ---------- apoios e rótulas ----------

        // Apoio nos nós; tira as molas (o apoio novo é o que vale).
        void CmdApoio(bool[] ap, string nome, string rot) => ComEscolha(nome, "no", (nos, bb) =>
            _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Apoio(m, nos, ap, new double[6])), "Apoio " + rot + " em " + Pl.N(nos.Count, "nó", "nós") + " (" + Ids(nos) + ")."));

        void CmdMolas(string arg) => ComEscolha("MOLAS", "no", (nos, bb) =>
            Perguntar("molas", arg, "MOLAS em " + Pl.N(nos.Count, "nó", "nós") + " — kx,ky,kz[,krx,kry,krz] (kN/m, kN·m/rad; 0 = sem mola)", "0,0,10000", async t =>
            {
                var k = LerLista(t, 3, 6);
                if (k == null || k.Any(x => x < 0)) { Log("MOLAS: digite 3 ou 6 rigidezes ≥ 0 separadas por vírgula, ponto decimal (ex.: 0,0,10000)."); return false; }
                var k6 = k.Concat(new double[6 - k.Length]).ToArray();
                await Espacial(d => d.Aplicar(m => GeometriaEdicao.Apoio(m, nos, null, k6)), "Molas (" + t + ") em " + Pl.N(nos.Count, "nó", "nós") + " (" + Ids(nos) + "); GL com mola ficou livre.");
                return true;
            }));

        void CmdRotular() => ComEscolha("ROTULAR", "barra", (nn, bs) =>
        {
            bool ligar = Esp.Json["barras"][bs[0]]?["params"]?["rotula_i"] == null;   // liga/desliga pelo estado da primeira
            _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Rotulas(m, bs, ligar)), (ligar ? "Rótulas nas pontas de " : "Rótulas tiradas de ") + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
        });

        // ---------- seção, material, giro e cardinal das barras existentes ----------

        void CmdSecao(string arg) => ComEscolha("SEÇÃO", "barra", (nn, bs) =>
            Perguntar("secao", arg, "SEÇÃO de " + Pl.N(bs.Count, "barra", "barras") + " — perfil do modelo ou do catálogo", Esp == null ? null : SecaoDesenho3D(Esp), async t =>
            {
                string nome = Nome(Esp?.Json["secoes"], t) ?? t;
                bool ok = false;
                await Espacial(async d => { await d.GarantirSecao(nome); await d.Aplicar(m => GeometriaEdicao.Propriedades(m, Nada, bs, nome, null, null, null)); ok = true; },
                               "Seção " + nome + " em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                if (!ok) Log("SEÇÃO: " + t + " não está no modelo nem no catálogo do motor.");
                return ok;
            }));

        void CmdMaterial(string arg) => ComEscolha("MATERIAL", "barra", (nn, bs) =>
            Perguntar("material", arg, "MATERIAL de " + Pl.N(bs.Count, "barra", "barras") + " — " + string.Join(" / ", (Esp.Json["materiais"] as JsonObject)?.Select(kv => kv.Key) ?? new string[0]),
                      Esp == null ? null : MaterialDesenho3D(Esp) ?? (string)Esp.Json["barras"][bs[0]]?["material"], async t =>
            {
                string nome = Nome(Esp?.Json["materiais"], t);
                if (nome == null) { Log("MATERIAL: " + t + " não está no modelo (Ações › materiais do motor)."); return false; }
                await Espacial(d => d.Aplicar(m => GeometriaEdicao.Propriedades(m, Nada, bs, null, nome, null, null)), "Material " + nome + " em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                return true;
            }));

        void CmdGiro(string arg) => ComEscolha("GIRO", "barra", (nn, bs) =>
            Perguntar("giro", arg, "GIRO da seção de " + Pl.N(bs.Count, "barra", "barras") + " (°)", giro3D.ToString("0.###", CultureInfo.InvariantCulture), async t =>
            {
                if (!Num.Ler(t, out double g) || !double.IsFinite(g)) { Log("GIRO: digite o ângulo em graus (ex.: 90)."); return false; }
                await Espacial(d => d.Aplicar(m => GeometriaEdicao.Propriedades(m, Nada, bs, null, null, g, null)), "Giro " + Num.Fmt(g, 1) + "° em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                return true;
            }));

        void CmdCardinal(string arg) => ComEscolha("CARDINAL", "barra", (nn, bs) =>
            Perguntar("cardinal", arg, "CARDINAL de " + Pl.N(bs.Count, "barra", "barras") + " — 1 a 9 como o teclado numérico (5 centro), 10 centroide", cardinal3D.ToString(CultureInfo.InvariantCulture), async t =>
            {
                if (!int.TryParse(t, out int c) || c < 1 || c > 10) { Log("CARDINAL: digite 1 a 9 (7 8 9 em cima, 1 2 3 embaixo) ou 10 (centroide)."); return false; }
                await Espacial(d => d.Aplicar(m => GeometriaEdicao.Cardinal(m, bs, c)), "Cardinal " + c + " em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                return true;
            }));

        void CmdDisposicao() => ComEscolha("DISPOSIÇÃO DO PERFIL", "barra", (nn, bs) => AbrirDisposicao(bs));

        // Janela Disposição do perfil (v0.6 D2): giro e cardinal da 1ª barra como ponto de partida, aplicados a todas (um Ctrl+Z).
        internal void AbrirDisposicao(List<string> bs)
        {
            var d0 = Esp; if (d0 == null || bs.Count == 0) return;
            var b0 = d0.Json["barras"]?[bs[0]];
            if (b0 == null) return;
            double g0 = (double)(b0["rot"] ?? 0.0) * 180 / Math.PI;
            int c0 = b0["params"]?["cardinal"] is JsonValue cv && cv.TryGetValue(out int ci) ? ci : 5;
            bool e0 = b0["params"]?["espelho"] is JsonValue ev && ev.TryGetValue(out bool eb) && eb;
            var j = new JanelaDisposicao(PerfilDaSecao((string)b0["secao"]), g0, c0, bs.Count, e0) { Owner = this };
            j.Aplicar += (g, c, es) => _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Espelho(GeometriaEdicao.Cardinal(GeometriaEdicao.Propriedades(m, Nada, bs, null, null, g, null), bs, c), bs, es)),
                                                "Disposição em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + "): giro " + Num.Fmt(g, 1) + "°, cardinal " + c + (es ? ", espelhado." : "."));
            j.ShowDialog();
        }

        // Desenhar › Configurações nas barras escolhidas: seção, material, giro e cardinal de uma vez (um Ctrl+Z).
        void CmdAplicarConfig() => ComEscolha("APLICAR CONFIGURAÇÕES", "barra", (nn, bs) =>
        {
            var d0 = Esp; string sec = SecaoDesenho3D(d0), mat = MaterialDesenho3D(d0); double g = giro3D; int c = cardinal3D;
            _ = Espacial(d => d.Aplicar(m => GeometriaEdicao.Cardinal(GeometriaEdicao.Propriedades(m, Nada, bs, sec, mat, g, null), bs, c)),
                         "Configurações em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + "): " + (sec ?? "seção mantida") + ", " + (mat ?? "material mantido") + ", giro " + Num.Fmt(g, 1) + "°, cardinal " + c + ".");
        });

        // ---------- cargas no caso ativo ----------

        string CasoOuAviso(string nome)
        {
            string c = Esp?.CasoAtivo;
            if (c == null) Log(nome + ": o modelo não tem caso de carga ativo (Ações › Casos de carga).");
            return c;
        }

        void CmdCargaNodal(string arg)
        {
            string caso = CasoOuAviso("CARGA NODAL"); if (caso == null) return;
            ComEscolha("CARGA NODAL", "no", (nos, bb) =>
                Perguntar("cargano", arg, "CARGA NODAL (" + caso + ") em " + Pl.N(nos.Count, "nó", "nós") + " — Fx,Fy,Fz[,Mx,My,Mz] (kN, kN·m; global, Z para cima)", "0,0,-10", async t =>
                {
                    var f = LerLista(t, 3, 6);
                    if (f == null) { Log("CARGA NODAL: digite Fx,Fy,Fz ou Fx,Fy,Fz,Mx,My,Mz (ponto decimal; ex.: 0,0,-10)."); return false; }
                    var f6 = f.Concat(new double[6 - f.Length]).ToArray();
                    var itens = new JsonArray(nos.Select(n => (JsonNode)new JsonObject { ["tipo"] = "carga_nodal", ["dados"] = new JsonObject { ["caso"] = caso, ["no"] = n, ["F"] = Vetor(f6) } }).ToArray());
                    await Espacial(d => d.Lote(itens), "Carga nodal (" + t + ") no caso " + caso + " em " + Pl.N(nos.Count, "nó", "nós") + " (" + Ids(nos) + ").");
                    return true;
                }));
        }

        void CmdCargaDist(string arg)
        {
            string caso = CasoOuAviso("CARGA DISTRIBUÍDA"); if (caso == null) return;
            ComEscolha("CARGA DISTRIBUÍDA", "barra", (nn, bs) =>
                Perguntar("cargadist", arg, "CARGA DISTRIBUÍDA (" + caso + ") em " + Pl.N(bs.Count, "barra", "barras") + " — qx,qy,qz (kN/m, global); trapezoidal: q em ni / q em nj", "0,0,-5", async t =>
                {
                    var partes = t.Split('/');
                    var q1 = LerLista(partes[0], 3, 3); var q2 = partes.Length == 2 ? LerLista(partes[1], 3, 3) : null;
                    if (q1 == null || partes.Length > 2 || (partes.Length == 2 && q2 == null)) { Log("CARGA DISTRIBUÍDA: digite qx,qy,qz (ex.: 0,0,-5) ou qx,qy,qz/qx2,qy2,qz2 (trapezoidal)."); return false; }
                    var itens = new JsonArray(bs.Select(b =>
                    {
                        var dados = new JsonObject { ["caso"] = caso, ["barra"] = b, ["q"] = Vetor(q1) };
                        if (q2 != null) dados["q2"] = Vetor(q2);
                        return (JsonNode)new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = dados };
                    }).ToArray());
                    await Espacial(d => d.Lote(itens), "Carga distribuída (" + t + ") kN/m no caso " + caso + " em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                    return true;
                }));
        }

        double Comprimento(string barra)
        {
            var j = Esp.Json; var b = j["barras"][barra];
            var a = GeometriaEdicao.Xyz(j["nos"][(string)b["ni"]]); var c = GeometriaEdicao.Xyz(j["nos"][(string)b["nj"]]);
            return Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (c[i] - a[i]) * (c[i] - a[i])));
        }

        // CPT: escolha as barras, a distância a (desde ni; M = meio de cada barra) e a força.
        void CmdCargaPontual()
        {
            string caso = CasoOuAviso("CARGA PONTUAL"); if (caso == null) return;
            ComEscolha("CARGA PONTUAL", "barra", (nn, bs) =>
                PedirValor("cpt_a", "CARGA PONTUAL (" + caso + ") em " + Pl.N(bs.Count, "barra", "barras") + " — distância a desde ni (m) ou M (meio)", "M", async ta =>
                {
                    bool meio = ta.Equals("M", StringComparison.OrdinalIgnoreCase) || ta.Equals("MEIO", StringComparison.OrdinalIgnoreCase);
                    double a = 0;
                    if (!meio && (!Num.Ler(ta, out a) || a < 0)) { Log("CARGA PONTUAL: digite a distância em m (≥ 0) ou M para o meio."); return false; }
                    var longe = meio ? null : bs.FirstOrDefault(b => a > Comprimento(b) + 1e-9);
                    if (longe != null) { Log("CARGA PONTUAL: a = " + Num.Fmt(a, 3) + " m passa do fim da barra " + longe + " (L = " + Num.Fmt(Comprimento(longe), 3) + " m)."); return false; }
                    PedirValor("cpt_f", "CARGA PONTUAL (" + caso + ") a = " + (meio ? "meio" : Num.Fmt(a, 3) + " m") + " — Fx,Fy,Fz (kN, global)", "0,0,-10", async tf =>
                    {
                        var f = LerLista(tf, 3, 3);
                        if (f == null) { Log("CARGA PONTUAL: digite Fx,Fy,Fz (ponto decimal; ex.: 0,0,-10)."); return false; }
                        var itens = new JsonArray(bs.Select(b => (JsonNode)new JsonObject { ["tipo"] = "carga_pontual", ["dados"] = new JsonObject { ["caso"] = caso, ["barra"] = b, ["a"] = meio ? Comprimento(b) / 2 : a, ["F"] = Vetor(f) } }).ToArray());
                        await Espacial(d => d.Lote(itens), "Carga pontual (" + tf + ") kN a " + (meio ? "meio vão" : Num.Fmt(a, 3) + " m de ni") + " no caso " + caso + " em " + Pl.N(bs.Count, "barra", "barras") + " (" + Ids(bs) + ").");
                        return true;
                    });
                    return true;
                }));
        }

        // Carga em área (laje): vigas escolhidas (antes ou na hora; um nível e uma direção) ou a cota do nível digitada durante
        // a escolha (pergunta a direção). Depois p (kN/m²). Larguras de influência: CargaArea (todas as paralelas do nível).
        void CmdCargaArea()
        {
            var d0 = Esp; if (d0 == null || vista3D == null) return;
            string caso = CasoOuAviso("CARGA EM ÁREA"); if (caso == null) return;
            void PedirP(double z, bool emX, List<string> so) =>
                PedirValor("area_p", "CARGA EM ÁREA (" + caso + ") no nível z = " + Num.Fmt(z, 2) + " m, vigas em " + (emX ? "X" : "Y") + (so != null ? " (" + so.Count + " escolhidas)" : "") + " — p (kN/m², para baixo)", "3", async t =>
                {
                    if (!Num.Ler(t, out double p) || p <= 0) { Log("CARGA EM ÁREA: digite p > 0 em kN/m² (ex.: 3)."); return false; }
                    await DistribuirArea(z, p, emX, so);
                    return true;
                });
            void DeVigas(List<string> bs)
            {
                var j = Esp.Json; var N = j["nos"];
                var info = bs.Select(b => { var a = GeometriaEdicao.Xyz(N[(string)j["barras"][b]["ni"]]); var c = GeometriaEdicao.Xyz(N[(string)j["barras"][b]["nj"]]); return (b, a, c); }).ToList();
                if (info.Any(x => Math.Abs(x.a[2] - x.c[2]) > 0.001) || info.Any(x => Math.Abs(x.a[2] - info[0].a[2]) > 0.001)) { Log("CARGA EM ÁREA: escolha vigas horizontais de um nível só."); return; }
                bool emX = info.All(x => Math.Abs(x.c[1] - x.a[1]) <= 1e-3 * Math.Max(1e-9, Math.Abs(x.c[0] - x.a[0])));
                bool emY = info.All(x => Math.Abs(x.c[0] - x.a[0]) <= 1e-3 * Math.Max(1e-9, Math.Abs(x.c[1] - x.a[1])));
                if (!emX && !emY) { Log("CARGA EM ÁREA: escolha vigas de uma direção só (paralelas a X ou a Y): a laje vai numa direção."); return; }
                PedirP(info[0].a[2], emX, bs);
            }
            var sel = SelBarras;
            if (sel.Count > 0) { DeVigas(sel); return; }
            vista3D.LimparSelecao();
            vista3D.PedirSelecao("CARGA EM ÁREA — selecione as vigas ou digite a cota z do nível (Enter termina, Esc cancela)", "barra", () =>
            {
                valorPendente = null;
                if (SelBarras.Count == 0) Log("CARGA EM ÁREA: nada selecionado.");
                else DeVigas(SelBarras);
            }, () => { valorPendente = null; vista3D.LimparSelecao(); Log("CARGA EM ÁREA cancelada."); });
            valorPendente = async t =>
            {
                if (!Num.Ler(t, out double z)) { Log("CARGA EM ÁREA: digite a cota z do nível em m (ex.: 3) ou escolha as vigas."); return; }
                var nivel = Esp.NiveisComVigas().Where(x => Math.Abs(x - z) <= 0.001).Select(x => (double?)x).FirstOrDefault();
                if (nivel == null) { Log("CARGA EM ÁREA: não há vigas no nível z = " + Num.Fmt(z, 2) + " m (há: " + string.Join(", ", Esp.NiveisComVigas().Select(x => Num.Fmt(x, 2))) + ")."); return; }
                valorPendente = null; vista3D.LimparPedido(false); vista3D.LimparSelecao();
                PedirValor("area_dir", "CARGA EM ÁREA no nível z = " + Num.Fmt(nivel.Value, 2) + " m — vigas que recebem [X/Y]", "X", async dir =>
                {
                    string u = dir.ToUpperInvariant();
                    if (u != "X" && u != "Y") { Log("CARGA EM ÁREA: responda X (vigas paralelas a X) ou Y."); return false; }
                    PedirP(nivel.Value, u == "X", null);
                    return true;
                });
                await Task.CompletedTask;
            };
        }

        // ---------- outros que dependiam da seleção ----------

        // Verificação detalhada de uma barra: a escolhida ou um clique.
        void CmdVerificar()
        {
            if (vista3D == null || Esp == null) return;
            if (SelBarras.Count > 0) { VerificarBarraConforme(null); return; }
            vista3D.PedirObjeto("VERIFICAR — clique a barra (Esc cancela)", (o, p, m) =>
            {
                string b = ParaIds(o).barra; if (b == null) return Task.FromResult(true);
                vista3D.Selecionar(null, new[] { b });
                VerificarBarraConforme(null);
                return Task.FromResult(false);
            }, () => Log("VERIFICAR cancelado."), null, "barra");
        }

        // Placa de base: o nó escolhido ou um clique; Enter sem clicar = o primeiro pé de pilar (o de antes).
        void CmdPlaca()
        {
            if (vista3D == null || Esp == null) return;
            if (SelNos.Count > 0) { _ = AbrirPlacaEsp(null); return; }
            vista3D.PedirObjeto("PLACA — clique o nó do pé do pilar (Enter = o primeiro pé, Esc cancela)", (o, p, m) =>
            {
                string n = ParaIds(o).no; if (n == null) return Task.FromResult(true);
                _ = AbrirPlacaEsp(n);
                return Task.FromResult(false);
            }, () => Log("PLACA cancelada."), () => _ = AbrirPlacaEsp(null), "no");
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
            var (gx, gy, _, _) = GradeEstrutural.Ler(d.Json["config"]?["grade"]);
            string Txt(IEnumerable<double> a, string padrao) => a.Any() ? string.Join(",", a.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) : padrao;
            var j = new JanelaGrade(Txt(gx, "0,@6,@6"), Txt(gy, "0,@5"), Txt(Niveis.Lista(d.Json).Select(n => n.Z), "0,@3")) { Owner = this };
            if (Capturas.Ativo) { janelaGrade = j; return; }
            if (j.ShowDialog() == true) _ = AplicarGrade(j.X, j.Y, j.Z);
        }
        internal JanelaGrade janelaGrade;   // capturas: a janela fica aberta para renderizar

        // Grava a grade em config.grade do motor (a mesma chave do Novo projeto) e cria um pavimento em cada nível acima do
        // térreo que ainda não tem: um PUT /api/modelo = um Ctrl+Z desfaz a grade e os pavimentos juntos.
        internal async Task AplicarGrade(double[] xs, double[] ys, double[] zs)
        {
            var d = Esp; if (d == null) return;
            var pav = Niveis.Lista(d.Json);
            var novos = new List<(string Nome, double Z)>();
            int k = 0;
            foreach (double z in zs.Where(z => z > Niveis.Tol).Distinct())
                if (!pav.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol) && !novos.Any(n => Math.Abs(n.Z - z) <= Niveis.Tol))
                {
                    string nome; do nome = "N" + (++k); while (d.Json["pavimentos"]?[nome] != null);
                    novos.Add((nome, z));
                }
            await Espacial(dd => dd.Aplicar(m => GradeEstrutural.Aplicar(m, xs, ys, novos)), null);
            AplicarGradeNaVista();
            Log("Grade: " + Pl.N(xs.Length, "eixo", "eixos") + " em X, " + Pl.N(ys.Length, "fila", "filas") + " em Y, " + Pl.N(zs.Length, "nível", "níveis") +
                (novos.Count > 0 ? " (" + Pl.N(novos.Count, "pavimento novo", "pavimentos novos") + ")" : "") + ". Cruzamentos atraem o cursor; PgUp/PgDn sobe e desce o plano; Ctrl+Z desfaz.");
        }

        // Eixos da grade (config.grade do motor, gravada pelo Novo projeto ou pelo GR) e níveis (pavimentos + térreo) na vista.
        internal void AplicarGradeNaVista()
        {
            if (vista3D == null) return;
            var j = Esp?.Json;
            var (x, y, nx, ny) = GradeEstrutural.Ler(j?["config"]?["grade"]);
            vista3D.EixosX = x; vista3D.EixosY = y; vista3D.NomesX = nx; vista3D.NomesY = ny;
            vista3D.NiveisEixos = j == null ? new double[0] : Niveis.Lista(j).Select(n => n.Z).ToArray();
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
