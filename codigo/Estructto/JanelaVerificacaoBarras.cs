// Janela "Verificação de barras" (REVISAO-TELAS 3.4): toda a estrutura numa lista ordenável (clique no cabeçalho),
// filtros Só reprovadas / tipo (aço, formado a frio, concreto) e busca; barra de aproveitamento por linha.
// Duplo clique (ou "Verificar barra") abre o detalhe da barra (AbrirDetalhe). Dados: GET /api/verificacao.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaVerificacaoBarras : JanelaEscura
    {
        public Action<string> AbrirDetalhe;   // id da barra
        readonly List<JsonNode> todas;
        readonly Border hostFiltros = new Border(), hostTabela = new Border();
        readonly TextBlock lblConta = new TextBlock();
        bool soReprovadas; string tipo = "", busca = "";
        int colOrd = 6; bool desc = true;
        string selId;
        List<JsonNode> vis = new List<JsonNode>();

        static double Taxa(JsonNode n) { double t = D(n, "taxa"); return double.IsNaN(t) ? -1 : t; }
        static bool Reprovada(JsonNode n) { return !Bool(n, "ok") && S(n, "criterio") != "nao_verificada"; }

        public JanelaVerificacaoBarras(JsonNode d)
        {
            todas = Lista(d).ToList();
            Moldar(this, "Verificação de barras", 1240, 720);
            int nRep = todas.Count(Reprovada), nNao = todas.Count(n => S(n, "criterio") == "nao_verificada");
            var pior = todas.OrderByDescending(Taxa).FirstOrDefault();
            Border cab;
            if (todas.Count == 0) cab = Cabecalho(Nivel.Neutro, "–", "Sem verificação", "Calcule (F5) para verificar as barras pelas normas.");
            else
            {
                var nv = nRep > 0 ? Nivel.Erro : Taxa(pior) > 0.9 ? Nivel.Alerta : Nivel.Ok;
                string tit = nRep > 0 ? nRep + (nRep == 1 ? " barra não passa" : " barras não passam") + " de " + todas.Count
                                      : "Todas as " + todas.Count + " barras passam · usa " + Pct(Taxa(pior)) + " na mais carregada";
                string sub = "Mais carregada: " + S(pior, "barra") + " (" + S(pior, "secao") + ") — " + NomeCriterio(S(pior, "criterio")) + ", " + S(pior, "norma") + " " + S(pior, "item") +
                             (nNao > 0 ? " · " + nNao + " não verificadas" : "");
                var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var (rot, nvc, qt) in new[] { ("✓ passam", Nivel.Ok, todas.Count(n => Bool(n, "ok"))), (nRep == 1 ? "✕ reprovada" : "✕ reprovadas", Nivel.Erro, nRep), ("– não verificadas", Nivel.Neutro, nNao) })
                {
                    if (qt == 0 && nvc != Nivel.Ok) continue;
                    var c = Chip(qt + "  " + rot, nvc, null, null, 12); c.Margin = new Thickness(6, 0, 0, 0); c.Padding = new Thickness(10, 4, 10, 4); chips.Children.Add(c);
                }
                cab = Cabecalho(nv, nv == Nivel.Erro ? "✕" : nv == Nivel.Alerta ? "!" : "✓", tit, sub, chips);
            }

            var corpo = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
            var barraF = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var bus = CaixaBusca("Buscar barra ou seção", 220, t => { busca = t.Trim(); Montar(); }, out _);
            DockPanel.SetDock(bus, Dock.Right); barraF.Children.Add(bus);
            lblConta.FontSize = 11.5; lblConta.Foreground = TemaPlant.BTexto3; lblConta.VerticalAlignment = VerticalAlignment.Center; lblConta.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(lblConta, Dock.Right); barraF.Children.Add(lblConta);
            barraF.Children.Add(hostFiltros);
            DockPanel.SetDock(barraF, Dock.Top); corpo.Children.Add(barraF);
            corpo.Children.Add(hostTabela);

            var dica = Tx("Duplo clique numa barra abre a verificação detalhada, critério a critério.", 11.5, TemaPlant.BTexto3);
            Content = Raiz(cab, corpo, Rodape(dica,
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Verificar barra", () => { if (selId != null) AbrirDetalhe?.Invoke(selId); }, true, "Abre a verificação detalhada da barra selecionada.")));
            selId = S(pior, "barra");
            Montar();
        }

        // API das capturas
        public void Filtrar(bool reprovadas, string tipoBarra) { soReprovadas = reprovadas; tipo = tipoBarra ?? ""; Montar(); }
        public void Ordenar(int coluna, bool decrescente) { colOrd = coluna; desc = decrescente; Montar(); }

        void Montar()
        {
            var tiposPres = todas.Select(n => S(n, "tipo")).Distinct().OrderBy(x => x).ToList();
            var f1 = Segmentado(new List<(string, bool, Action, string)>
            {
                ("Todas", !soReprovadas, () => { soReprovadas = false; Montar(); }, "Mostra todas as barras."),
                ("Só reprovadas (" + todas.Count(Reprovada) + ")", soReprovadas, () => { soReprovadas = true; Montar(); }, "Mostra só as barras com taxa acima de 100%.")
            });
            var itensT = new List<(string, bool, Action, string)> { ("Todos os tipos", tipo == "", () => { tipo = ""; Montar(); }, null) };
            foreach (var t in tiposPres)
            {
                string tt = t;
                itensT.Add((NomeTipo(t) + " (" + todas.Count(n => S(n, "tipo") == tt) + ")", tipo == t, () => { tipo = tt; Montar(); }, "Só barras de " + NomeTipo(t).ToLowerInvariant() + "."));
            }
            var f2 = Segmentado(itensT); f2.Margin = new Thickness(10, 0, 0, 0);
            hostFiltros.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { f1, f2 } };

            IEnumerable<JsonNode> q = todas;
            if (soReprovadas) q = q.Where(Reprovada);
            if (tipo != "") q = q.Where(n => S(n, "tipo") == tipo);
            if (busca != "") q = q.Where(n => S(n, "barra").IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0 || S(n, "secao").IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0);
            Func<JsonNode, IComparable> chave = colOrd switch
            {
                0 => n => S(n, "barra"), 1 => n => NomeTipo(S(n, "tipo")), 2 => n => S(n, "secao"), 3 => n => D(n, "L_m"),
                4 => n => NomeCriterio(S(n, "criterio")), 5 => n => S(n, "combinacao"), _ => n => Taxa(n)
            };
            vis = (desc ? q.OrderByDescending(chave) : q.OrderBy(chave)).ToList();
            lblConta.Text = vis.Count + " de " + todas.Count + " barras";

            if (vis.Count == 0)
            {
                hostTabela.Child = soReprovadas && busca == ""
                    ? Vazio("ok", "Nenhuma barra reprovada", "Todas as barras" + (tipo != "" ? " de " + NomeTipo(tipo).ToLowerInvariant() : "") + " passam nas verificações da norma.")
                    : Vazio("busca", "Nenhuma barra encontrada", "Mude a busca ou os filtros.");
                return;
            }
            var cols = new[]
            {
                new Col("Barra", 96), new Col("Tipo", 112), new Col("Seção", 0), new Col("L [m]", 64, true),
                new Col("Critério", 170), new Col("Combinação determinante", 0), new Col("Aproveitamento", 160, false, "Taxa da verificação mais desfavorável (solicitante / resistente).")
            };
            var linhas = vis.Select(n =>
            {
                bool nao = S(n, "criterio") == "nao_verificada";
                var crit = new StackPanel { Orientation = Orientation.Horizontal };
                crit.Children.Add(Tx(NomeCriterio(S(n, "criterio")), 12, TemaPlant.BTexto1));
                if (!nao) crit.Children.Add(Tx("  " + S(n, "item"), 11, TemaPlant.BTexto3));
                return new UIElement[]
                {
                    Tx(S(n, "barra"), 12, TemaPlant.BTexto1, FontWeights.SemiBold), Tx(NomeTipo(S(n, "tipo")), 12, TemaPlant.BTexto2),
                    Tx(S(n, "secao"), 12, TemaPlant.BTexto1), Num1(F(D(n, "L_m"), 2)), crit, Tx(S(n, "combinacao"), 12, TemaPlant.BTexto2),
                    nao ? (UIElement)Tx("não verificada", 12, TemaPlant.BTexto3) : CelTaxa(D(n, "taxa"), Bool(n, "ok"), 94)
                };
            }).ToList();
            int sel = vis.FindIndex(n => S(n, "barra") == selId);
            TrocarTabela(hostTabela, Grade(cols, linhas, sel,
                i => { selId = S(vis[i], "barra"); Montar(); },
                i => { selId = S(vis[i], "barra"); Montar(); AbrirDetalhe?.Invoke(selId); },
                c => { if (colOrd == c) desc = !desc; else { colOrd = c; desc = c == 3 || c == 6; } Montar(); }, colOrd, desc,
                i => string.Join("\n", Lista(vis[i]["avisos"]).Select(a => "⚠ " + a)) is string av && av != "" ? av : null));
        }
    }
}
