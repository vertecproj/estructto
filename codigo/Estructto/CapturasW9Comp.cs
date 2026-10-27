// W9-COMPOSTO (docs/v3/W9-COMPOSTO.md): perfil duplo pela janela Disposição do perfil. Pórtico 6 × 5 m, pé-direito 4 m:
// pilares UPN 200 → 2U em caixão soldado (vira tubo), vigas em X UPN 200 → 2U costas com costas com chapas espaçadoras
// (s = 10, a = 500) e vigas em Y Ue 150x60x20x2,00 → 2Ue costas (NBR 14762). Aplica pela janela (um Ctrl+Z desfaz),
// salva .pstr, reabre, calcula e confere seção, tipo e norma de cada barra. Falha = exceção (erros.txt).
// Só elas: ESTRUCTTO_CAPTURAS=w9comp (com ESTRUCTTO_MOTOR).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasW9Comp(MainWindow w)
        {
            var log = new StringBuilder("W9: perfil duplo pela Disposição do perfil\n");
            void Falha(string s) => throw new InvalidOperationException("w9comp: " + s + " | " + w.StatusMsg.Text);
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Vazio("Pórtico 2U"), "modelo vazio"));
            DocumentoEspacial D() => w.EspacialAtivo ?? throw new InvalidOperationException("w9comp: documento sumiu");
            var eng = new JsonArray(true, true, true, true, true, true);
            JsonObject No(string id, double x, double y, double z, bool apoio) => new JsonObject { ["tipo"] = "no", ["dados"] = new JsonObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z, ["apoio"] = apoio ? eng.DeepClone() : new JsonArray(false, false, false, false, false, false) } };
            JsonObject Br(string id, string ni, string nj, string sec) => new JsonObject { ["tipo"] = "barra", ["dados"] = new JsonObject { ["id"] = id, ["ni"] = ni, ["nj"] = nj, ["secao"] = sec, ["material"] = "A-36" } };
            var itens = new JsonArray(
                new JsonObject { ["tipo"] = "material", ["dados"] = new JsonObject { ["aco"] = "A-36" } },
                new JsonObject { ["tipo"] = "secao", ["dados"] = new JsonObject { ["perfil"] = "UPN 200" } },
                new JsonObject { ["tipo"] = "secao", ["dados"] = new JsonObject { ["perfil"] = "Ue 150x60x20x2,00" } },
                new JsonObject { ["tipo"] = "caso", ["dados"] = new JsonObject { ["nome"] = "G", ["tipo"] = "G" } });
            var pilares = new List<string>(); var vigasX = new List<string>(); var vigasY = new List<string>();
            int k = 0;
            foreach (var (x, y) in new[] { (0.0, 0.0), (6.0, 0.0), (0.0, 5.0), (6.0, 5.0) })
            {
                k++;
                itens.Add(No("p" + k, x, y, 0, true)); itens.Add(No("t" + k, x, y, 4, false));
                itens.Add(Br("P" + k, "p" + k, "t" + k, "UPN 200")); pilares.Add("P" + k);
            }
            itens.Add(Br("VX1", "t1", "t2", "UPN 200")); itens.Add(Br("VX2", "t3", "t4", "UPN 200")); vigasX.AddRange(new[] { "VX1", "VX2" });
            itens.Add(No("c2", 7.5, 0, 4, false)); itens.Add(Br("VX3", "t2", "c2", "UPN 200")); vigasX.Add("VX3");   // balanço: ponta livre mostra a seção
            itens.Add(Br("VY1", "t1", "t3", "Ue 150x60x20x2,00")); itens.Add(Br("VY2", "t2", "t4", "Ue 150x60x20x2,00")); vigasY.AddRange(new[] { "VY1", "VY2" });
            foreach (var v in vigasX) itens.Add(new JsonObject { ["tipo"] = "carga_distribuida", ["dados"] = new JsonObject { ["caso"] = "G", ["barra"] = v, ["q"] = new JsonArray(0.0, 0.0, -3.0) } });
            Esperar(w.OperarEspacial(dd => dd.Lote(itens), "teste: pórtico UPN"));
            if (D().Json["barras"].AsObject().Count != 9) Falha("pórtico não entrou no motor");

            // a janela, como o usuário: escolhe o arranjo, captura, Aplicar
            string Aplicar(List<string> bs, string arr, string lig, double sMm, double aMm, string png)
            {
                var j = w.CriarDisposicao(bs) ?? throw new InvalidOperationException("w9comp: janela não abriu");
                PrepararDialogoForaDaTela(j); j.Show(); EsperarIdle();
                j.DefinirDuplo(arr, lig, sMm, aMm); EsperarIdle();
                if (png != null) CapturarDialogo(j, png);
                string alvo = j.NomeAlvo;
                j.Confirmar();
                Esperar(w.UltimaDisposicao); EsperarIdle();
                foreach (var b in bs) if ((string)D().Json["barras"][b]["secao"] != alvo) Falha("barra " + b + " ficou com " + (string)D().Json["barras"][b]["secao"] + ", esperado " + alvo);
                log.AppendLine("ok " + string.Join(",", bs) + " -> " + alvo + " (tipo " + (string)D().Json["barras"][bs[0]]["tipo"] + ")");
                return alvo;
            }
            // janela vazia de propósito (simples) só para a captura do estado inicial
            {
                var j0 = w.CriarDisposicao(pilares); PrepararDialogoForaDaTela(j0); j0.Show(); EsperarIdle();
                CapturarDialogo(j0, "w9-disposicao-simples.png"); j0.Close();
            }
            string secPil = Aplicar(pilares, "caixao", "soldada", 0, 0, "w9-disposicao-caixao.png");
            // um Ctrl+Z desfaz a troca inteira; refazer volta
            Esperar(w.DesfazerEspacial()); EsperarIdle();
            if ((string)D().Json["barras"]["P1"]["secao"] != "UPN 200") Falha("Ctrl+Z não voltou o pilar ao UPN 200");
            Esperar(w.RefazerEspacial()); EsperarIdle();
            if ((string)D().Json["barras"]["P1"]["secao"] != secPil) Falha("refazer não voltou o caixão");
            log.AppendLine("ok um Ctrl+Z desfaz a troca de seção das 4 barras");
            string secVx = Aplicar(vigasX, "I", "presilhas", 10, 500, "w9-disposicao-costas.png");
            string secVy = Aplicar(vigasY, "I", "presilhas", 10, 400, "w9-disposicao-costas-ue.png");
            var sp = D().Json["secoes"][secPil]["props"]; var sx = D().Json["secoes"][secVx]["props"]; var sy = D().Json["secoes"][secVy]["props"];
            if ((string)sp["tipo"] != "tubo_retangular" || (string)sp["composicao"]?["arranjo"] != "caixao") Falha("pilar não virou tubo (caixão soldado): " + sp.ToJsonString());
            if ((string)sx["tipo"] != "dupla" || (double)sx["composicao"]["l_ligacoes"] != 0.5 || (double)sx["composicao"]["s"] != 0.01) Falha("viga X sem composição costas/chapas");
            if ((string)D().Json["barras"]["VY1"]["tipo"] != "pff" || (string)D().Json["barras"]["VX1"]["tipo"] != "aco") Falha("tipo da barra não acompanhou a seção");
            log.AppendLine($"pilar {secPil}: A = {(double)D().Json["secoes"][secPil]["A"] * 1e4:0.00} cm² (2 × UPN 200 = {2 * (double)D().Json["secoes"]["UPN 200"]["A"] * 1e4:0.00})");

            // salva .pstr, reabre e calcula
            string tmp = Path.Combine(Path.GetTempPath(), "estructto-w9-comp.pstr");
            if (!w.SalvarEspacialEm(tmp)) Falha("salvar .pstr");
            Esperar(w.AbrirEspacialArquivo(tmp)); EsperarIdle();
            foreach (var (b, s) in new[] { ("P1", secPil), ("VX1", secVx), ("VY1", secVy) })
                if ((string)D().Json["barras"][b]["secao"] != s || D().Json["secoes"][s]?["props"]?["composicao"] == null) Falha(".pstr reaberto perdeu a seção composta de " + b);
            log.AppendLine("ok .pstr salvo e reaberto com as 3 seções compostas");
            Esperar(w.CalcularEspacial()); EsperarIdle();
            if (!D().Calculado) Falha("cálculo não rodou");
            foreach (var b in pilares.Concat(vigasX).Concat(vigasY))
            {
                if (!D().PiorPorBarra.TryGetValue(b, out var l)) Falha("barra " + b + " sem verificação");
                string norma = (string)l["norma"];
                bool esperado14762 = b.StartsWith("VY");
                if (norma == null || norma.Contains("14762") != esperado14762) Falha("barra " + b + " verificada por " + norma);
                log.AppendLine($"{b}: {(string)l["secao"]} · {norma} · taxa {DocumentoEspacial.Taxa(l):0.000} · {(string)l["criterio"]} {(string)l["item"]} · ok {l["ok"]}");
                // W9: My pequeno de pórtico espacial não reprova mais composta aberta (2 × perfil isolado)
                if (l["ok"] == null || !(bool)l["ok"]) Falha("barra " + b + " não passa: " + (string)l["criterio"] + " " + string.Join(" | ", l["avisos"]?.AsArray().Select(a => (string)a) ?? new string[0]));
            }
            var vv = Esperar2(D().Consultar("/api/verificacao/VX1"));
            if (vv?["detalhes"]?["compressao"]?["composta"] == null && vv?["detalhes"]?["compressao"] != null) Falha("verificação da viga sem o 5.3.4");
            log.AppendLine("VX1 itens: " + string.Join("; ", JanelaVerificarBarraMotor.Itens(vv).Select(i => i.Criterio + " " + i.NomeItem + " " + i.Taxa.ToString("0.000"))));
            foreach (var (b, png) in new[] { ("VX1", "w9-verificacao-viga-2u-costas.png"), ("P1", "w9-verificacao-pilar-2u-caixao.png"), ("VY1", "w9-verificacao-viga-2ue-costas.png") })
            {
                var jv = new JanelaVerificarBarraMotor(Esperar2(D().Consultar("/api/verificacao/" + b)));
                PrepararDialogoForaDaTela(jv); jv.Show(); EsperarIdle(); CapturarDialogo(jv, png); jv.Close();
            }

            // painel Propriedades (nome composto, miniatura e o atalho Duplo…)
            w.EscolherEspacial("barra", "P1"); EsperarIdle(); CapturarPrincipal(w, "w9-propriedades-pilar");
            w.EscolherEspacial("barra", "VX1"); EsperarIdle(); CapturarPrincipal(w, "w9-propriedades-viga");
            w.EscolherEspacial(null, null);

            // 3D: Realista (Render3D), Sombreado (Vista3D nativo) e Analítico (linhas, por desenho), de longe, no nó e na ponta do balanço
            var estilo0 = Config.Estilo3D;
            w.MostrarDiagrama(Diagrama.Nenhum); w.MostrarVista3D(true);
            foreach (var (est, nome) in new[] { (EstiloVisual.Realista, "realista"), (EstiloVisual.Sombreado, "sombreado"), (EstiloVisual.Analitico, "analitico") })
            {
                w.DefinirEstilo(est); EsperarIdle();
                w.vista3D.MostrarVista(VistaPadrao.Iso, false); EsperarIdle();
                CapturarPrincipal(w, "w9-3d-" + nome);
                w.vista3D.Focar(new Point3D(0, 0, 3.8), 1.6, -35, 25); EsperarIdle();
                CapturarPrincipal(w, "w9-3d-" + nome + "-no");
                w.vista3D.Focar(new Point3D(7.5, 0, 4), 1.5, 30, 20); EsperarIdle();
                CapturarPrincipal(w, "w9-3d-" + nome + "-ponta-balanco");
                w.vista3D.Focar(new Point3D(6, 0, 4.1), 1.4, 20, 60); EsperarIdle();
                CapturarPrincipal(w, "w9-3d-" + nome + "-topo-pilar");
            }
            w.DefinirEstilo(estilo0);

            // miniaturas (ícone do seletor) dos nomes compostos
            {
                var nomes = new[] { "UPN 200", secPil, secVx, "Ue 150x60x20x2,00", secVy, "2L 2\" x 1/4\" cruz soldado", "2L 2\" x 1/4\" costas chapas a=400 s=6,3", "2W200x15 lado-a-lado soldado s=50" };
                var pn = new StackPanel { Background = TemaPlant.BSup1, Margin = new Thickness(0) };
                foreach (var n in nomes)
                {
                    var l = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 0) };
                    l.Children.Add(new Image { Source = MiniaturaPerfil.Criar(n, 48), Width = 48, Height = 48 });
                    l.Children.Add(new Image { Source = MiniaturaPerfil.Criar(n, 22), Width = 22, Height = 22, Margin = new Thickness(10, 13, 10, 13) });
                    l.Children.Add(new TextBlock { Text = n, Foreground = TemaPlant.BTexto1, VerticalAlignment = VerticalAlignment.Center });
                    pn.Children.Add(l);
                }
                var hw = new Window { Content = pn, Width = 520, Height = 470, Background = TemaPlant.BSup1, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                PrepararDialogoForaDaTela(hw); hw.Show(); EsperarIdle(); CapturarDialogo(hw, "w9-miniaturas.png"); hw.Close();
            }
            File.WriteAllText(Path.Combine(pastaSaida, "w9-comp.txt"), log.ToString(), new UTF8Encoding(false));
            w.FecharEspaciaisSemPerguntar();
        }
    }
}
