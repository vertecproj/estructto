// Estructto.exe --testes-comandos <pasta>: cada comando nas duas ordens (docs/v3/FLUXO-COMANDOS.md) na janela real, fora da
// tela, com o motor (ESTRUCTTO_MOTOR ou o motor do worktree). Verbo→substantivo: comando sem seleção, confere o pedido (texto
// na vista e na linha de comando), escolhe com ClicarTela / SelecionarCaixa / Tecla(Enter) e responde na linha de comando.
// Substantivo→verbo: Selecionar e depois o comando. Cada caso confere o modelo e que um Ctrl+Z (U) volta ao de antes.
// Resumo em <pasta>/testes-comandos.txt ("TODOS PASSARAM").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Portico;

namespace NovoFtool
{
    public static partial class Capturas
    {
        internal static void ConfigurarComandos(MainWindow w, string pasta)
        {
            pastaSaida = Path.GetFullPath(pasta);
            Directory.CreateDirectory(pastaSaida);
            Ativo = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarComandos(w)), DispatcherPriority.ApplicationIdle);
        }

        static void RodarComandos(MainWindow w)
        {
            var sb = new StringBuilder(); int falhas = 0, oks = 0;
            bool Ok(string nome, bool c) { sb.AppendLine((c ? "ok    " : "FALHA ") + nome); if (c) oks++; else falhas++; File.WriteAllText(Path.Combine(pastaSaida, "testes-comandos.txt"), sb.ToString(), new UTF8Encoding(false)); return c; }
            try
            {
                string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(Path.Combine(dev, "prossen_estrutural"))) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                sb.AppendLine("Testes dos comandos (--testes-comandos) · ESTRUCTTO_MOTOR = " + (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") ?? "(não definida)"));
                w.DefinirRealista(false);
                Comandos(w, Ok);
            }
            catch (Exception ex) { Ok("interrompido: " + ex.GetType().Name + ": " + ex.Message + " | status: " + w.StatusMsg.Text, false); sb.AppendLine(ex.ToString()); }
            finally
            {
                sb.AppendLine(oks + " ok, " + falhas + " falhas");
                sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
                File.WriteAllText(Path.Combine(pastaSaida, "testes-comandos.txt"), sb.ToString(), new UTF8Encoding(false));
                try { w.FecharEspaciaisSemPerguntar(); } catch { }
                w.alterado = false;
                Environment.Exit(falhas == 0 ? 0 : 1);
            }
        }

        static void Comandos(MainWindow w, Func<string, bool, bool> Ok)
        {
            Esperar(w.NovoDocumento(null, "Comandos E2E"));
            var d0 = w.EspacialAtivo; var v = w.vista3D;
            if (!Ok("modelo em branco", d0 != null)) return;
            Point3D Q(double x, double y, double z) => new Point3D(x, y, z);
            // dois pórticos 6 × 3 m (y = 0 e y = 5) + vigas em Y no topo; corte (TR) e curta (EX) perto da viga y = 0
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] {
                (Q(0, 0, 0), Q(0, 0, 3)), (Q(6, 0, 0), Q(6, 0, 3)), (Q(0, 5, 0), Q(0, 5, 3)), (Q(6, 5, 0), Q(6, 5, 3)),
                (Q(0, 0, 3), Q(6, 0, 3)), (Q(0, 5, 3), Q(6, 5, 3)), (Q(0, 0, 3), Q(0, 5, 3)), (Q(6, 0, 3), Q(6, 5, 3)),
                (Q(3, -1.5, 3), Q(3, 1.5, 3)), (Q(4.5, -3, 3), Q(4.5, -1, 3)) }, null, false), "teste: pórticos"));
            Esperar(w.OperarEspacial(async dd =>
            {
                if (dd.CasoAtivo == null) await dd.Adicionar("caso", new JsonObject { ["nome"] = "Q1", ["tipo"] = "Q" });
                await dd.Adicionar("material", new JsonObject { ["aco"] = "A-36" });
            }, "teste: caso e material"));
            var J0 = J(w);
            Ok("modelo: 10 barras, 12 nós, caso " + w.EspacialAtivo.CasoAtivo + ", materiais " + string.Join("/", J0["materiais"].AsObject().Select(kv => kv.Key)), NBarras(w) == 10 && NNos(w) == 12 && w.EspacialAtivo.CasoAtivo != null && J0["materiais"]["A-36"] != null);
            string No(double x, double y, double z) => NoEm(J(w), x, y, z) ?? throw new InvalidOperationException("sem nó em " + x + "," + y + "," + z);
            string Bar(Point3D a, Point3D b) => BarraEntre(J(w), No(a.X, a.Y, a.Z), No(b.X, b.Y, b.Z)) ?? throw new InvalidOperationException("sem barra " + a + "–" + b);
            string vigaY0 = Bar(Q(0, 0, 3), Q(6, 0, 3)), vigaY5 = Bar(Q(0, 5, 3), Q(6, 5, 3)), vigaX0 = Bar(Q(0, 0, 3), Q(0, 5, 3)), vigaX6 = Bar(Q(6, 0, 3), Q(6, 5, 3));
            string corte = Bar(Q(3, -1.5, 3), Q(3, 1.5, 3)), curta = Bar(Q(4.5, -3, 3), Q(4.5, -1, 3)), pilar = Bar(Q(0, 0, 0), Q(0, 0, 3));
            string n000 = No(0, 0, 0), n600 = No(6, 0, 0), n050 = No(0, 5, 0), n650 = No(6, 5, 0), n003 = No(0, 0, 3), n603 = No(6, 0, 3);

            void Iso() { v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); EsperarIdle(); }
            Iso();
            Point T(Point3D p) => v.Tela(p);
            Point3D PNo(string id) => Pos(J(w), id);
            Point3D Meio(string b, double t = 0.5) { var bb = J(w)["barras"][b]; var a = PNo((string)bb["ni"]); var c = PNo((string)bb["nj"]); return a + (c - a) * t; }
            void Clicar(Point3D p) { v.MoverTela(T(p)); Esperar(v.ClicarTela(T(p))); EsperarIdle(); }
            void Enter() { v.Tecla(Key.Enter); EsperarIdle(); }
            void Esc() { v.Tecla(Key.Escape); EsperarIdle(); }
            void Cmd(string c) { w.ExecutarComando(c); EsperarIdle(); }
            bool Espera(Func<bool> c) => AteCond(c, 20, w);
            string Dica() { PrivCall(w, "AtualizarDica"); return w.DicaComando.Text; }
            bool[] Ap(string no) => (J(w)["nos"][no]["apoio"] as JsonArray)?.Select(x => (bool)x).ToArray() ?? new bool[6];
            bool ApIgual(string no, bool[] e) => Ap(no).SequenceEqual(e);

            // desfazer: U volta ao modelo de antes (texto do JSON do motor)
            void Desfaz(string nome, string antes)
            {
                Cmd("U");
                Ok(nome + ": Ctrl+Z (U) volta ao modelo de antes", Espera(() => J(w).ToJsonString() == antes));
                v.LimparSelecao(); EsperarIdle();
            }
            // verbo→substantivo: o comando sem seleção pede na vista com o texto esperado, e a linha de comando mostra o mesmo
            bool Pede(string cmd, string prompt)
            {
                v.LimparSelecao(); EsperarIdle();
                Cmd(cmd);
                string p = v.Prompt ?? "";
                return Ok(cmd + " sem seleção pede: \"" + p + "\"", v.Pedindo && p.StartsWith(prompt)) & Ok(cmd + ": a linha de comando mostra o pedido", Dica() == v.Prompt);
            }
            bool B(string b, Func<JsonNode, bool> f) => J(w)["barras"][b] is JsonNode x && f(x);
            double D(JsonNode n) => (double)n;
            int Cargas(string col) => (J(w)[col] as JsonArray)?.Count ?? 0;
            string antes;

            // ================= apoios =================
            var t6 = new[] { true, true, true, true, true, true }; var art = new[] { true, true, true, false, false, false }; var mov = new[] { false, false, true, false, false, false }; var liv = new bool[6];

            // ENGASTE ← o exemplo do usuário: sem seleção pede os nós; janela em elevação pega os da base; Shift + janela tira
            antes = J(w).ToJsonString();
            Pede("ENGASTE", "ENGASTE — selecione os nós (Enter termina, Esc cancela)");
            v.MoverTela(T(PNo(n003))); EsperarIdle();
            Ok("ENGASTE: pré-realce no nó sob o cursor", v.Realce is No3D);
            v.MoverTela(T(Meio(vigaY5))); EsperarIdle();
            Ok("ENGASTE: no meio de uma barra não realça (só nós valem)", v.Realce == null);
            v.MostrarVista(VistaPadrao.Frente, false); v.Enquadrar(false); EsperarIdle();
            Point a0 = T(Q(-0.5, 0, -0.3)), b0 = T(Q(6.5, 0, 0.3));
            v.SelecionarCaixa(a0, b0); EsperarIdle();
            Ok("ENGASTE: janela na base pega os 4 nós e nenhuma barra (" + v.Selecao.Nos.Count + " nós, " + v.Selecao.Barras.Count + " barras)", v.Selecao.Nos.Count == 4 && v.Selecao.Barras.Count == 0);
            v.SelecionarCaixa(T(Q(5.5, 0, -0.3)), T(Q(6.5, 0, 0.3)), ModifierKeys.Shift); EsperarIdle();
            Ok("ENGASTE: Shift + janela tira os 2 nós de x = 6", v.Selecao.Nos.Count == 2 && v.Selecao.Nos.Contains(n000) && v.Selecao.Nos.Contains(n050));
            Ok("ENGASTE: o pedido continua durante a janela", v.Pedindo);
            Enter();
            Ok("ENGASTE verbo→substantivo: 2 nós engastados, x = 6 livres", Espera(() => ApIgual(n000, t6) && ApIgual(n050, t6)) && ApIgual(n600, liv));
            Iso();
            Desfaz("ENGASTE", antes);
            v.Selecionar(new[] { n600 }, new[] { pilar }); EsperarIdle();   // seleção mista: só o nó vale
            Cmd("ENGASTE");
            Ok("ENGASTE substantivo→verbo (nó + barra escolhidos): aplica no nó, sem pedir", !v.Pedindo && Espera(() => ApIgual(n600, t6)) && ApIgual(n000, liv));
            Desfaz("ENGASTE escolhido antes", antes);

            foreach (var (cmd, nome, ap) in new[] { ("ROTULA", "ARTICULADO", art), ("MOVEL", "MÓVEL", mov) })
            {
                Pede(cmd, nome + " — selecione os nós");
                Clicar(PNo(n000)); Clicar(PNo(n600));
                Ok(cmd + ": dois cliques escolhem 2 nós", v.Selecao.Nos.Count == 2);
                Clicar(PNo(n600));   // sem Shift: clicar de novo não tira
                v.MoverTela(T(PNo(n600))); Esperar(v.ClicarTela(T(PNo(n600)), ModifierKeys.Shift)); EsperarIdle();
                Ok(cmd + ": Shift + clique tira o nó", v.Selecao.Nos.Count == 1 && v.Selecao.Nos.Contains(n000));
                Enter();
                Ok(cmd + " verbo→substantivo: apoio " + string.Join("", ap.Select(x => x ? "1" : "0")) + " no nó " + n000, Espera(() => ApIgual(n000, ap)) && ApIgual(n600, liv));
                Desfaz(cmd, antes);
                v.Selecionar(new[] { n650 }, null); EsperarIdle();
                Cmd(cmd);
                Ok(cmd + " substantivo→verbo", !v.Pedindo && Espera(() => ApIgual(n650, ap)));
                Desfaz(cmd + " escolhido antes", antes);
            }

            // MOLAS: pergunta a rigidez; resposta inválida pergunta de novo; Esc cancela sem mudar nada
            Pede("MOLAS", "MOLAS — selecione os nós");
            Clicar(PNo(n000)); Enter();
            Ok("MOLAS: pergunta kx,ky,kz com padrão <0,0,10000> na vista e na linha de comando", v.Pedindo && (v.Prompt ?? "").Contains("<0,0,10000>") && Dica() == v.Prompt);
            Cmd("abc");
            Ok("MOLAS: resposta inválida pergunta de novo", v.Pedindo && (v.Prompt ?? "").StartsWith("MOLAS") && J(w).ToJsonString() == antes);
            Cmd("0,0,5000");
            Ok("MOLAS verbo→substantivo: kz = 5000 e uz livre no nó " + n000, Espera(() => J(w)["nos"][n000]["molas"] is JsonArray k && D(k[2]) == 5000) && !Ap(n000)[2]);
            Desfaz("MOLAS", antes);
            v.Selecionar(new[] { n600, n650 }, null); EsperarIdle();
            Cmd("MOLAS");
            Ok("MOLAS substantivo→verbo: pergunta sem pedir os nós, padrão = a última resposta", v.Pedindo && (v.Prompt ?? "").Contains("2 nós") && (v.Prompt ?? "").Contains("<0,0,5000>"));
            Enter();
            Ok("MOLAS: Enter aceita o padrão nos 2 nós", Espera(() => J(w)["nos"][n650]["molas"] is JsonArray k && D(k[2]) == 5000 && J(w)["nos"][n600]["molas"] != null));
            Desfaz("MOLAS escolhido antes", antes);
            v.Selecionar(new[] { n600 }, null); EsperarIdle();
            Cmd("MOLAS"); Esc();
            Ok("MOLAS: Esc na pergunta cancela (modelo igual, nada pendente)", !v.Pedindo && J(w).ToJsonString() == antes);
            Cmd("0,0,1");
            Ok("MOLAS cancelado: número digitado depois não vira mola", J(w).ToJsonString() == antes);
            v.LimparSelecao();

            // LIVRE: tira o apoio (e as molas)
            Esperar(w.OperarEspacial(dd => dd.Aplicar(m => GeometriaEdicao.Apoio(m, new[] { n000, n600 }, t6, null)), "teste: base engastada"));
            string comBase = J(w).ToJsonString();
            Pede("LIVRE", "LIVRE — selecione os nós");
            Clicar(PNo(n000)); Enter();
            Ok("LIVRE verbo→substantivo", Espera(() => ApIgual(n000, liv)) && ApIgual(n600, t6));
            Desfaz("LIVRE", comBase);
            v.Selecionar(new[] { n600 }, null); EsperarIdle(); Cmd("LIVRE");
            Ok("LIVRE substantivo→verbo", Espera(() => ApIgual(n600, liv)));
            Desfaz("LIVRE escolhido antes", comBase);
            Cmd("U"); Espera(() => J(w).ToJsonString() == antes);

            // ================= rótulas na barra =================
            Pede("ROTULAR", "ROTULAR — selecione as barras");
            v.MoverTela(T(Meio(vigaY0, 0.25))); EsperarIdle();
            Ok("ROTULAR: pré-realce na barra", v.Realce is Barra3D);
            v.MoverTela(T(PNo(n050))); EsperarIdle();
            Ok("ROTULAR: sobre um nó não realça nó (só barras valem)", !(v.Realce is No3D));
            Clicar(Meio(vigaY0, 0.25)); Enter();
            Ok("ROTULAR verbo→substantivo: rotula_i e rotula_j na " + vigaY0, Espera(() => B(vigaY0, b => b["params"]?["rotula_i"] != null && b["params"]?["rotula_j"] != null)));
            Desfaz("ROTULAR", antes);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle(); Cmd("RB");
            Ok("ROTULAR substantivo→verbo", Espera(() => B(vigaY5, b => b["params"]?["rotula_i"] != null)));
            Desfaz("ROTULAR escolhido antes", antes);

            // ================= edição =================
            Pede("APAGAR", "APAGAR — selecione os objetos");
            Clicar(Meio(curta)); Enter();
            Ok("APAGAR verbo→substantivo: a barra curta saiu", Espera(() => J(w)["barras"][curta] == null) && NBarras(w) == 9);
            Desfaz("APAGAR", antes);
            v.Selecionar(null, new[] { corte }); EsperarIdle(); Cmd("E");
            Ok("APAGAR substantivo→verbo", Espera(() => J(w)["barras"][corte] == null));
            Desfaz("APAGAR escolhido antes", antes);

            Pede("MOVER", "MOVER — selecione os objetos");
            Clicar(Meio(vigaY5, 0.3)); Enter();
            Ok("MOVER: depois da escolha pede o ponto base", v.Pedindo && (v.Prompt ?? "").StartsWith("MOVER — ponto base"));
            Cmd("0,0,0"); Cmd("@0,0,1");
            Ok("MOVER verbo→substantivo: a viga y = 5 subiu 1 m", Espera(() => NoEm(J(w), 0, 5, 4) != null && NoEm(J(w), 6, 5, 4) != null));
            Desfaz("MOVER", antes);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle(); Cmd("M");
            Ok("MOVER substantivo→verbo: vai direto ao ponto base", v.Pedindo && (v.Prompt ?? "").StartsWith("MOVER — ponto base"));
            Cmd("0,0,0"); Cmd("@0,1,0");
            Ok("MOVER substantivo→verbo: y = 6", Espera(() => NoEm(J(w), 0, 6, 3) != null));
            Desfaz("MOVER escolhido antes", antes);

            Pede("COPIAR", "COPIAR — selecione os objetos");
            Clicar(Meio(vigaY0, 0.25)); Enter(); Cmd("0,0,0"); Cmd("@0,2,0");
            Ok("COPIAR verbo→substantivo: cópia em y = 2", Espera(() => NoEm(J(w), 0, 2, 3) != null) && NBarras(w) == 11);
            Enter();
            Desfaz("COPIAR", antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("CO"); Cmd("0,0,0"); Cmd("@0,2,0");
            Espera(() => NBarras(w) == 11); Cmd("@0,3,0");
            Ok("COPIAR substantivo→verbo: duas cópias (base fixa)", Espera(() => NBarras(w) == 12 && NoEm(J(w), 0, 3, 3) != null));
            Enter();
            Cmd("U"); Espera(() => NBarras(w) == 11);
            Desfaz("COPIAR escolhido antes (2 cópias = 2 Ctrl+Z)", antes);

            Pede("ESPELHAR", "ESPELHAR — selecione os objetos");
            Clicar(Meio(pilar)); Enter(); Cmd("0,-1,0"); Cmd("@6,0,0");
            Ok("ESPELHAR verbo→substantivo: pilar espelhado em y = −2", Espera(() => NoEm(J(w), 0, -2, 0) != null && NoEm(J(w), 0, -2, 3) != null));
            Desfaz("ESPELHAR", antes);
            v.Selecionar(null, new[] { pilar }); EsperarIdle(); Cmd("MI"); Cmd("-1,0,0"); Cmd("@0,6,0");
            Ok("ESPELHAR substantivo→verbo: x = −2", Espera(() => NoEm(J(w), -2, 0, 0) != null));
            Desfaz("ESPELHAR escolhido antes", antes);

            Pede("ROTACIONAR", "ROTACIONAR — selecione os objetos");
            Clicar(Meio(vigaY0, 0.25)); Enter(); Cmd("0,0,0"); Cmd("90");
            Ok("ROTACIONAR verbo→substantivo: viga y = 0 girada 90° (6; 0; 3) → (0; 6; 3)", Espera(() => NoEm(J(w), 0, 6, 3) != null));
            Desfaz("ROTACIONAR", antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("RO"); Cmd("0,0,0"); Esc();
            Ok("ROTACIONAR: Esc no ângulo cancela", !v.Pedindo);
            Cmd("90");
            Ok("ROTACIONAR cancelado: o 90 digitado depois não gira nada (estado não fica pendente)", J(w).ToJsonString() == antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("RO"); Cmd("0,0,0"); Cmd("90");
            Ok("ROTACIONAR substantivo→verbo", Espera(() => NoEm(J(w), 0, 6, 3) != null));
            Desfaz("ROTACIONAR escolhido antes", antes);

            // painéis: a escolha vem antes ou é pedida; o painel age nela
            void Painel(string cmd, string prompt, string filtroResumo, Action escolher, Action<PainelEdicao> preencher, Func<bool> efeito, string oque)
            {
                Pede(cmd, prompt);
                escolher(); Enter();
                var p = w.PainelEdicaoAberto;
                Ok(cmd + " verbo→substantivo: o painel abre com a escolha (" + (p == null ? "sem painel" : PainelEdicao.Resumo(new FonteSelecaoEsp(w))) + ")", p != null && PainelEdicao.Resumo(new FonteSelecaoEsp(w)).Contains(filtroResumo));
                if (p == null) return;
                PrepararDialogoForaDaTela(p); preencher(p); Esperar(p.Executar()); p.Close(); EsperarIdle();
                Ok(cmd + " verbo→substantivo: " + oque, Espera(efeito));
                Desfaz(cmd, antes);
            }
            Painel("MATRIZ", "MATRIZ — selecione os objetos", "1 barra", () => Clicar(Meio(vigaY5, 0.3)),
                   p => { ((TextBox)p.Campos["nx"]).Text = "1"; ((TextBox)p.Campos["nz"]).Text = "2"; ((TextBox)p.Campos["ez"]).Text = "3"; }, () => NoEm(J(w), 0, 5, 6) != null, "cópia da viga em z = 6");
            Painel("DIVIDIR", "DIVIDIR — selecione as barras", "1 barra", () => Clicar(Meio(vigaY5, 0.3)),
                   p => ((TextBox)p.Campos["n"]).Text = "2", () => NoEm(J(w), 3, 5, 3) != null && NBarras(w) == 11, "viga y = 5 dividida em 2");
            Painel("LOTE", "EM LOTE — selecione os objetos", "1 barra", () => Clicar(Meio(vigaY5, 0.3)),
                   p => ((ComboBox)p.Campos["material"]).SelectedItem = "A-36", () => B(vigaY5, b => (string)b["material"] == "A-36") && B(vigaY0, b => (string)b["material"] != "A-36"), "material A-36 só na viga y = 5");
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("DIV");
            Ok("DIVIDIR substantivo→verbo: o painel abre direto com 1 barra", w.PainelEdicaoAberto != null && !v.Pedindo && PainelEdicao.Resumo(new FonteSelecaoEsp(w)).Contains("1 barra"));
            w.PainelEdicaoAberto?.Close(); v.LimparSelecao(); EsperarIdle();

            // UNIR: nó a 0,5 mm do (0; 0; 3) com uma barra; Enter sem nada = todos
            Esperar(w.OperarEspacial(dd => dd.Aplicar(m =>
            {
                m["nos"]["NX"] = new JsonObject { ["id"] = "NX", ["x"] = 0.0005, ["y"] = 0.0, ["z"] = 3.0, ["apoio"] = new JsonArray(false, false, false, false, false, false) };
                m["barras"]["BX"] = new JsonObject { ["id"] = "BX", ["ni"] = "NX", ["nj"] = n050, ["secao"] = (string)m["barras"][vigaY0]["secao"], ["material"] = (string)m["barras"][vigaY0]["material"] };
                return m;
            }), "teste: nó quase coincidente"));
            string comNX = J(w).ToJsonString();
            Pede("UNIR", "UNIR — selecione os nós (Enter termina; Enter sem nada = todos");
            Enter();
            var pu = w.PainelEdicaoAberto;
            Ok("UNIR verbo→substantivo: Enter sem nada abre o painel para o modelo inteiro", pu != null && PainelEdicao.Resumo(new FonteSelecaoEsp(w)).Contains("modelo inteiro"));
            if (pu != null) { PrepararDialogoForaDaTela(pu); Esperar(pu.Executar()); pu.Close(); EsperarIdle(); }
            Ok("UNIR: o nó a 0,5 mm fundiu", Espera(() => J(w)["nos"]["NX"] == null));
            Desfaz("UNIR", comNX);
            int nosNX = NNos(w);
            v.Selecionar(new[] { "NX", n003 }, null); EsperarIdle(); Cmd("J");
            pu = w.PainelEdicaoAberto;
            Ok("UNIR substantivo→verbo: painel com os 2 nós", pu != null && PainelEdicao.Resumo(new FonteSelecaoEsp(w)).Contains("2 nós"));
            if (pu != null) { PrepararDialogoForaDaTela(pu); Esperar(pu.Executar()); pu.Close(); EsperarIdle(); }
            Ok("UNIR substantivo→verbo: fundiu (um nó a menos)", Espera(() => NNos(w) == nosNX - 1));
            Desfaz("UNIR escolhido antes", comNX);
            Cmd("U"); Espera(() => J(w).ToJsonString() == antes);

            // APARAR / ESTENDER: limites escolhidos (+ Enter) ou antes; depois as barras
            Pede("APARAR", "APARAR — selecione as barras limite");
            Clicar(Meio(vigaY0, 0.15)); Enter();
            Ok("APARAR: depois dos limites pede a barra a aparar", v.Pedindo && (v.Prompt ?? "").StartsWith("APARAR (1 limite)"));
            Clicar(Q(3, 1.1, 3)); Enter();
            Ok("APARAR verbo→substantivo: o lado clicado (y > 0) saiu", Espera(() => NoEm(J(w), 3, 1.5, 3) == null && B(corte, b => new[] { PNo((string)b["ni"]), PNo((string)b["nj"]) }.Any(p => (p - Q(3, 0, 3)).Length < 1e-6))));
            Desfaz("APARAR", antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("TR");
            Ok("APARAR substantivo→verbo: a barra escolhida é o limite", v.Pedindo && (v.Prompt ?? "").StartsWith("APARAR (1 limite)"));
            Clicar(Q(3, -1.1, 3)); Enter();
            Ok("APARAR substantivo→verbo: o lado y < 0 saiu", Espera(() => NoEm(J(w), 3, -1.5, 3) == null));
            Desfaz("APARAR escolhido antes", antes);
            Pede("ESTENDER", "ESTENDER — selecione as barras limite");
            Clicar(Meio(vigaY0, 0.15)); Enter(); Clicar(Q(4.5, -1.2, 3)); Enter();
            Ok("ESTENDER verbo→substantivo: a ponta perto do clique foi até a viga (4,5; 0; 3)", Espera(() => NoEm(J(w), 4.5, 0, 3) != null && NoEm(J(w), 4.5, -1, 3) == null));
            Desfaz("ESTENDER", antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("EX"); Clicar(Q(4.5, -1.2, 3)); Enter();
            Ok("ESTENDER substantivo→verbo", Espera(() => NoEm(J(w), 4.5, 0, 3) != null));
            Desfaz("ESTENDER escolhido antes", antes);

            // COPIAR PROPRIEDADES: a viga y = 5 ganha A-36, giro 90° e cardinal 8; depois vai para a y = 0
            Esperar(w.OperarEspacial(dd => dd.Aplicar(m => GeometriaEdicao.Cardinal(GeometriaEdicao.Propriedades(m, new string[0], new[] { vigaY5 }, null, "A-36", 90, null), new[] { vigaY5 }, 8)), "teste: origem"));
            string comOrigem = J(w).ToJsonString();
            bool Copiou(string b) => B(b, x => (string)x["material"] == "A-36" && Math.Abs(D(x["rot"]) - Math.PI / 2) < 1e-9 && DocumentoEspacial.CardinalDe(x) == 8);
            v.LimparSelecao(); EsperarIdle(); Cmd("MA");
            Ok("COPIAR PROPRIEDADES sem seleção pede a origem", v.Pedindo && (v.Prompt ?? "").StartsWith("COPIAR PROPRIEDADES — clique a barra de origem"));
            Clicar(Meio(vigaY5, 0.3));
            Ok("COPIAR PROPRIEDADES: depois da origem pede as que recebem", v.Pedindo && (v.Prompt ?? "").StartsWith("COPIAR PROPRIEDADES de " + vigaY5 + " — selecione as barras que recebem"));
            Clicar(Meio(vigaY0, 0.25)); Enter();
            Ok("COPIAR PROPRIEDADES verbo→substantivo: material, giro e cardinal na " + vigaY0, Espera(() => Copiou(vigaY0)) && !Copiou(vigaX0));
            Desfaz("COPIAR PROPRIEDADES", comOrigem);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle(); Cmd("MA");
            Ok("COPIAR PROPRIEDADES com 1 barra escolhida: ela é a origem", v.Pedindo && (v.Prompt ?? "").StartsWith("COPIAR PROPRIEDADES de " + vigaY5));
            Clicar(Meio(vigaX0)); Enter();
            Ok("COPIAR PROPRIEDADES substantivo→verbo (origem antes)", Espera(() => Copiou(vigaX0)));
            Desfaz("COPIAR PROPRIEDADES origem antes", comOrigem);
            v.Selecionar(null, new[] { vigaX0, vigaX6 }); EsperarIdle(); Cmd("MA"); Clicar(Meio(vigaY5, 0.3));
            Ok("COPIAR PROPRIEDADES com destinos escolhidos antes: um clique na origem aplica", Espera(() => Copiou(vigaX0) && Copiou(vigaX6)));
            Desfaz("COPIAR PROPRIEDADES destinos antes", comOrigem);
            Cmd("U"); Espera(() => J(w).ToJsonString() == antes);

            // ================= seção, material, giro, cardinal =================
            string secOutra = PerfilCatalogo("W150", "W150x13");
            Pede("SECAO", "SEÇÃO — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter();
            Ok("SEÇÃO: pergunta o perfil com o padrão das Configurações", v.Pedindo && (v.Prompt ?? "").Contains("perfil do modelo ou do catálogo <"));
            Cmd(secOutra);
            Ok("SEÇÃO verbo→substantivo: " + secOutra + " (do catálogo) na viga y = 5", Espera(() => B(vigaY5, b => (string)b["secao"] == secOutra)) && B(vigaY0, b => (string)b["secao"] != secOutra));
            Cmd("U"); Espera(() => B(vigaY5, b => (string)b["secao"] != secOutra));
            Desfaz("SEÇÃO (2 passos: perfil novo e troca)", antes);
            v.Selecionar(null, new[] { vigaY0, vigaY5 }); EsperarIdle(); Cmd("SECAO NAOEXISTE123");
            Ok("SEÇÃO com nome inválido: modelo igual e o motivo no histórico", Espera(() => true) && J(w).ToJsonString() == antes);
            string secModelo = (string)J(w)["barras"][pilar]["secao"];
            Esperar(w.OperarEspacial(dd => dd.GarantirSecao(secOutra), "teste: seção no modelo"));
            string comSec = J(w).ToJsonString();
            v.Selecionar(null, new[] { vigaY0, vigaY5 }); EsperarIdle(); Cmd("SEC " + secOutra.ToLowerInvariant());
            Ok("SEÇÃO substantivo→verbo com o nome na linha (sem pergunta; maiúsc./minúsc. tanto faz)", !v.Pedindo && Espera(() => B(vigaY0, b => (string)b["secao"] == secOutra) && B(vigaY5, b => (string)b["secao"] == secOutra)));
            Desfaz("SEÇÃO escolhido antes", comSec);

            Pede("MATERIAL", "MATERIAL — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter(); Cmd("PINHO");
            Ok("MATERIAL: nome fora do modelo pergunta de novo", v.Pedindo && (v.Prompt ?? "").StartsWith("MATERIAL"));
            Cmd("a-36");
            Ok("MATERIAL verbo→substantivo", Espera(() => B(vigaY5, b => (string)b["material"] == "A-36")));
            Desfaz("MATERIAL", comSec);
            v.Selecionar(null, new[] { pilar }); EsperarIdle(); Cmd("MAT"); Enter();
            Ok("MATERIAL substantivo→verbo: Enter = a última resposta (A-36)", Espera(() => B(pilar, b => (string)b["material"] == "A-36")));
            Desfaz("MATERIAL escolhido antes", comSec);

            Pede("GIRO", "GIRO — selecione as barras");
            Clicar(Meio(pilar)); Enter(); Cmd("90");
            Ok("GIRO verbo→substantivo: rot = 90°", Espera(() => B(pilar, b => Math.Abs(D(b["rot"]) - Math.PI / 2) < 1e-9)));
            Desfaz("GIRO", comSec);
            v.Selecionar(null, new[] { pilar }); EsperarIdle(); Cmd("GIRO 45");
            Ok("GIRO substantivo→verbo", Espera(() => B(pilar, b => Math.Abs(D(b["rot"]) - Math.PI / 4) < 1e-9)));
            Desfaz("GIRO escolhido antes", comSec);

            Pede("CARDINAL", "CARDINAL — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter(); Cmd("11");
            Ok("CARDINAL: 11 pergunta de novo", v.Pedindo && (v.Prompt ?? "").StartsWith("CARDINAL"));
            Cmd("8");
            Ok("CARDINAL verbo→substantivo: 8", Espera(() => B(vigaY5, b => DocumentoEspacial.CardinalDe(b) == 8)));
            Desfaz("CARDINAL", comSec);
            v.Selecionar(null, new[] { vigaY5 }); EsperarIdle(); Cmd("CARD 2");
            Ok("CARDINAL substantivo→verbo", Espera(() => B(vigaY5, b => DocumentoEspacial.CardinalDe(b) == 2)));
            Desfaz("CARDINAL escolhido antes", comSec);

            Esperar(w.UsarSecao3D(secOutra)); w.GirarDesenho3D(90); EsperarIdle();
            Pede("APLICARCONFIG", "APLICAR CONFIGURAÇÕES — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter();
            Ok("APLICARCONFIG verbo→substantivo: seção e giro das Configurações", Espera(() => B(vigaY5, b => (string)b["secao"] == secOutra && Math.Abs(D(b["rot"]) - Math.PI / 2) < 1e-9)));
            Desfaz("APLICARCONFIG", comSec);
            v.Selecionar(null, new[] { pilar }); EsperarIdle(); Cmd("APLICAR");
            Ok("APLICARCONFIG substantivo→verbo", Espera(() => B(pilar, b => (string)b["secao"] == secOutra)));
            Desfaz("APLICARCONFIG escolhido antes", comSec);
            w.GirarDesenho3D(-90); Esperar(w.UsarSecao3D(secModelo));
            Cmd("U"); Espera(() => J(w).ToJsonString() == antes);

            // ================= cargas =================
            int cn = Cargas("cargas_nodais"), cd = Cargas("cargas_distribuidas"), cp = Cargas("cargas_pontuais");
            Pede("CARGANO", "CARGA NODAL — selecione os nós");
            Clicar(PNo(n003)); Enter();
            Ok("CARGA NODAL: pergunta Fx,Fy,Fz com o caso ativo", v.Pedindo && (v.Prompt ?? "").Contains("(" + w.EspacialAtivo.CasoAtivo + ")") && (v.Prompt ?? "").Contains("<0,0,-10>"));
            Cmd("0,0,-12");
            Ok("CARGA NODAL verbo→substantivo: Fz = −12 no nó " + n003, Espera(() => Cargas("cargas_nodais") == cn + 1) && J(w)["cargas_nodais"].AsArray().Any(c => (string)c["no"] == n003 && D(c["F"][2]) == -12));
            Desfaz("CARGA NODAL", antes);
            v.Selecionar(new[] { n003, n603 }, null); EsperarIdle(); Cmd("CN"); Enter();
            Ok("CARGA NODAL substantivo→verbo: Enter = a última (−12) nos 2 nós, um Ctrl+Z", Espera(() => Cargas("cargas_nodais") == cn + 2));
            Desfaz("CARGA NODAL escolhido antes", antes);

            Pede("CARGADIST", "CARGA DISTRIBUÍDA — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter(); Cmd("0,0,-3/0,0,-6");
            Ok("CARGA DISTRIBUÍDA verbo→substantivo: trapezoidal −3 → −6", Espera(() => Cargas("cargas_distribuidas") == cd + 1) && J(w)["cargas_distribuidas"].AsArray().Any(c => (string)c["barra"] == vigaY5 && D(c["q2"][2]) == -6));
            Desfaz("CARGA DISTRIBUÍDA", antes);
            v.Selecionar(null, new[] { vigaY0, vigaY5 }); EsperarIdle(); Cmd("CD 0,0,-2");
            Ok("CARGA DISTRIBUÍDA substantivo→verbo (valor na linha)", Espera(() => Cargas("cargas_distribuidas") == cd + 2));
            Desfaz("CARGA DISTRIBUÍDA escolhido antes", antes);

            Pede("CARGAPONTUAL", "CARGA PONTUAL — selecione as barras");
            Clicar(Meio(vigaY5, 0.3)); Enter();
            Ok("CARGA PONTUAL: pergunta a distância (padrão M = meio)", v.Pedindo && (v.Prompt ?? "").Contains("<M>"));
            Cmd("99");
            Ok("CARGA PONTUAL: a = 99 m passa da barra e pergunta de novo", v.Pedindo && (v.Prompt ?? "").Contains("distância"));
            Enter();
            Ok("CARGA PONTUAL: depois pergunta a força", v.Pedindo && (v.Prompt ?? "").Contains("Fx,Fy,Fz"));
            Cmd("0,0,-7");
            Ok("CARGA PONTUAL verbo→substantivo: −7 kN no meio (a = 3)", Espera(() => Cargas("cargas_pontuais") == cp + 1) && J(w)["cargas_pontuais"].AsArray().Any(c => (string)c["barra"] == vigaY5 && Math.Abs(D(c["a"]) - 3) < 1e-9));
            Desfaz("CARGA PONTUAL", antes);
            v.Selecionar(null, new[] { vigaY0 }); EsperarIdle(); Cmd("CPT"); Cmd("1.5"); Enter();
            Ok("CARGA PONTUAL substantivo→verbo: a = 1,5", Espera(() => Cargas("cargas_pontuais") == cp + 1) && J(w)["cargas_pontuais"].AsArray().Any(c => (string)c["barra"] == vigaY0 && Math.Abs(D(c["a"]) - 1.5) < 1e-9));
            Desfaz("CARGA PONTUAL escolhido antes", antes);

            // carga em área: nível digitado (pergunta a direção), vigas clicadas, ou vigas escolhidas antes
            double QDe(string b) => J(w)["cargas_distribuidas"].AsArray().Where(c => (string)c["barra"] == b).Select(c => -D(c["q"][2])).DefaultIfEmpty(0).Sum();
            Pede("CARGAAREA", "CARGA EM ÁREA — selecione as vigas ou digite a cota z do nível");
            Cmd("3");
            Ok("CARGA EM ÁREA: nível digitado pergunta a direção", v.Pedindo && (v.Prompt ?? "").Contains("[X/Y]"));
            Enter(); Cmd("4");
            Ok("CARGA EM ÁREA nível z = 3, vigas em X: q = 4 × 2,5 = 10 kN/m nas 2 vigas", Espera(() => Math.Abs(QDe(vigaY0) - 10) < 1e-9 && Math.Abs(QDe(vigaY5) - 10) < 1e-9));
            Desfaz("CARGA EM ÁREA (nível)", antes);
            Pede("CARGAAREA", "CARGA EM ÁREA — selecione as vigas");
            Clicar(Meio(vigaY5, 0.3)); Enter(); Enter();
            Ok("CARGA EM ÁREA verbo→substantivo (viga clicada): só a y = 5 recebe 10 kN/m", Espera(() => Math.Abs(QDe(vigaY5) - 10) < 1e-9) && QDe(vigaY0) == 0);
            Desfaz("CARGA EM ÁREA (vigas)", antes);
            var esperadoY = DocumentoEspacial.CargaArea(J(w), 3, 2, false).ToDictionary(x => x.barra, x => x.q);   // vizinhas em Y: corte (x = 3) e as de borda
            v.Selecionar(null, new[] { vigaX0, vigaX6 }); EsperarIdle(); Cmd("LAJE"); Cmd("2");
            Ok("CARGA EM ÁREA substantivo→verbo (vigas em Y): q = 2 × largura de influência (" + Num.Fmt(esperadoY[vigaX0], 2) + " e " + Num.Fmt(esperadoY[vigaX6], 2) + " kN/m), só nas escolhidas",
               Espera(() => Math.Abs(QDe(vigaX0) - esperadoY[vigaX0]) < 1e-9 && Math.Abs(QDe(vigaX6) - esperadoY[vigaX6]) < 1e-9) && QDe(corte) == 0);
            Desfaz("CARGA EM ÁREA escolhido antes", antes);
            v.Selecionar(null, new[] { vigaY0, vigaX0 }); EsperarIdle(); Cmd("LAJE");
            Ok("CARGA EM ÁREA: vigas de duas direções são recusadas (sem pergunta, modelo igual)", !v.Pedindo && J(w).ToJsonString() == antes);
            v.LimparSelecao();

            // ================= sem efeito no modelo: só o pedido =================
            Pede("VERIFICAR", "VERIFICAR — clique a barra"); Esc();
            Ok("VERIFICAR: Esc cancela", !v.Pedindo);
            Pede("PLACA", "PLACA — clique o nó do pé do pilar"); Esc();
            Ok("PLACA: Esc cancela", !v.Pedindo);
            Pede("DIMSEL", "DIMSEL — selecione as barras"); Esc();
            Ok("DIMSEL: Esc cancela (antes dimensionava o modelo inteiro sem perguntar)", !v.Pedindo && J(w).ToJsonString() == antes);
            Cmd("ME");
            Ok("MEDIR continua verbo primeiro (ferramenta Medir)", v.Ferramenta == Ferramenta3D.Medir);
            Esc(); Esc();

            // ================= estado: desfazer no meio de um comando =================
            Pede("ENGASTE", "ENGASTE"); Clicar(PNo(n000));
            Cmd("U");
            Ok("Ctrl+Z no meio de um comando cancela o pedido (não fica estado velho)", !v.Pedindo);
        }
    }
}
