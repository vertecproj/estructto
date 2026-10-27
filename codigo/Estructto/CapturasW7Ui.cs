// W7-UI (docs/v3/W7-UI.md). Só elas: ESTRUCTTO_CAPTURAS=w7ui. Falha = exceção (erros.txt), PNGs de prova na pasta de saída.
//  1) Ícone de perfil: UM nome de cada família do catálogo do motor (/api/catalogo) tem de ter a forma do motor (props.tipo) e nenhum
//     nome do catálogo inteiro pode cair na reserva tracejada (quadrado). PNG: W7-miniaturas-familias.png.
//  2) Seletor de perfil: ao abrir, o perfil atual fica perto do topo; ao filtrar (e ao limpar o filtro) a lista volta ao topo com o
//     1º item selecionado. Confere o VerticalOffset do ScrollViewer da lista. PNGs: W7-seletor-*.png.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static ScrollViewer RolagemDe(DependencyObject o)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var f = VisualTreeHelper.GetChild(o, i);
                if (f is ScrollViewer sv) return sv;
                var r = RolagemDe(f);
                if (r != null) return r;
            }
            return null;
        }

        static void CapturasW7Ui(MainWindow w)
        {
            var cat = Esperar2(Motor.Get("/api/catalogo")).AsArray();
            var nomes = cat.Select(x => (string)x["nome"]).ToList();
            var falhas = new List<string>();

            // ---- 1) ícones: grupo = família + prefixo do nome (Tubo Vallourec mistura TQ/TR e TC); a forma esperada vem do motor
            var grupos = cat.GroupBy(x => (string)x["familia"] + "|" + Regex.Match((string)x["nome"], @"^[A-Za-zØ]+").Value).ToList();
            var folha = new WrapPanel { Width = 760, Background = TemaPlant.BSup1 };
            foreach (var g in grupos)
            {
                string amostra = (string)g.First()["nome"];
                var p = Esperar2(Motor.Get("/api/catalogo/" + Uri.EscapeDataString(amostra)));
                string tipoMotor = (string)p["secao"]["props"]["tipo"];
                foreach (var x in g)
                {
                    string n = (string)x["nome"], t = MiniaturaPerfil.Tipo(n);
                    if (t != tipoMotor) { falhas.Add(g.Key + ": " + n + " -> " + (t ?? "RESERVA (quadrado)") + " (motor: " + tipoMotor + ")"); if (falhas.Count > 40) break; }
                }
                var cel = new StackPanel { Width = 190, Margin = new Thickness(4), Orientation = Orientation.Horizontal };
                cel.Children.Add(new System.Windows.Controls.Image { Width = 22, Height = 22, Margin = new Thickness(2, 0, 4, 0), Source = MiniaturaPerfil.Criar(amostra, 22) });
                cel.Children.Add(new System.Windows.Controls.Image { Width = 66, Height = 66, Margin = new Thickness(0, 0, 6, 0), Source = MiniaturaPerfil.Criar(amostra, 22), Stretch = Stretch.Fill });
                cel.Children.Add(new TextBlock { Text = g.Key.Split('|')[0] + "\n" + amostra + "\n" + tipoMotor, FontSize = 10.5, Foreground = TemaPlant.BTexto2, VerticalAlignment = VerticalAlignment.Center });
                folha.Children.Add(cel);
            }
            {   // reserva legítima: nome livre e "(vários)"
                if (MiniaturaPerfil.Tipo("A=45,7 I=3437") != null || MiniaturaPerfil.Tipo(null) != null) falhas.Add("nome livre / vários deveriam ficar na reserva");
                var cel = new StackPanel { Width = 190, Margin = new Thickness(4), Orientation = Orientation.Horizontal };
                cel.Children.Add(new System.Windows.Controls.Image { Width = 22, Height = 22, Margin = new Thickness(2, 0, 4, 0), Source = MiniaturaPerfil.Criar(null, 22) });
                cel.Children.Add(new System.Windows.Controls.Image { Width = 66, Height = 66, Margin = new Thickness(0, 0, 6, 0), Source = MiniaturaPerfil.Criar(null, 22), Stretch = Stretch.Fill });
                cel.Children.Add(new TextBlock { Text = "reserva (só nome livre\nou \"(vários)\")", FontSize = 10.5, Foreground = TemaPlant.BTexto3, VerticalAlignment = VerticalAlignment.Center });
                folha.Children.Add(cel);
            }
            var jf = new Window { Content = folha, Background = TemaPlant.BSup1 }; PrepararDialogoForaDaTela(jf); jf.Show(); EsperarIdle();
            RenderizarJanela(jf, 800, 20 + 80 * ((grupos.Count + 1 + 2) / 3), "W7-miniaturas-familias.png");
            jf.Close();
            File.WriteAllText(Path.Combine(pastaSaida, "W7-miniaturas.txt"), (falhas.Count == 0 ? "OK " : "FALHA ") + grupos.Count + " grupos, " + nomes.Count + " perfis do catalogo\n" + string.Join("\n", falhas));
            if (falhas.Count > 0) throw new InvalidOperationException("miniatura de perfil: " + falhas.Count + " nomes sem a forma do motor, ex.: " + string.Join(" | ", falhas.Take(5)));

            // ---- 2) seletor de perfil: rolagem ao abrir e ao filtrar (popup real numa janela de teste)
            string atual = "W250x73";
            if (!nomes.Contains(atual)) throw new InvalidOperationException("catálogo sem " + atual);
            var sel = new SeletorPerfil(() => nomes, () => atual, n => atual = n) { AceitaDigitado = false, Width = 220, Margin = new Thickness(20, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            sel.Mostrar(atual);
            var jh = new Window { Content = sel, Background = TemaPlant.BSup1, Width = 400, Height = 560 }; PrepararDialogoForaDaTela(jh); jh.Show(); EsperarIdle();
            Func<double> off = () => RolagemDe(sel.ListaInterna)?.VerticalOffset ?? -1;
            Action<string, bool> confere = (rotulo, ok) => { if (!ok) throw new InvalidOperationException("seletor de perfil: " + rotulo + " (offset " + off() + ", itens " + sel.ListaInterna.Items.Count + ", selecionado " + sel.ListaInterna.SelectedIndex + ")"); };
            var log = new List<string>();
            Action<string> anota = r => log.Add(r + ": offset " + off() + ", itens " + sel.ListaInterna.Items.Count + ", selecionado " + sel.ListaInterna.SelectedIndex);

            Action<string> abrirEConfere = nomeAtual =>
            {
                atual = nomeAtual; sel.Mostrar(atual);
                int idx = nomes.IndexOf(atual);
                sel.Abrir(); EsperarIdle(); EsperarIdle();
                anota("abrir com " + atual + " (índice " + idx + ")");
                confere("não abriu o popup", sel.Aberto);
                confere("ao abrir com " + atual + " o offset deveria ser " + Math.Max(0, idx - 2), Math.Abs(off() - Math.Max(0, idx - 2)) < 0.5 || (idx > nomes.Count - 10 && off() >= nomes.Count - 12));   // no fim a lista não rola além do último
                confere("o perfil atual deveria estar visível (item realizado na lista)", sel.ListaInterna.ItemContainerGenerator.ContainerFromItem(atual) != null);
                confere("o perfil atual deveria vir selecionado", (sel.ListaInterna.SelectedItem as string) == atual);
            };
            abrirEConfere("W250x73");
            RenderizarJanela(jh, 400, 560, "W7-seletor-1-aberto.png");

            // o caso do usuário: depois de aberto com o atual longe do topo, filtrar "upn" (20 itens) tem de começar no 1º UPN, não no fim
            sel.BuscaInterna.Text = "upn"; EsperarIdle(); EsperarIdle();
            anota("filtro upn");
            confere("filtro upn: 20 itens", sel.ListaInterna.Items.Count == 20);
            confere("filtro upn: a lista deveria voltar ao topo (offset 0)", Math.Abs(off()) < 0.5);
            confere("filtro upn: o 1º item deveria estar selecionado", sel.ListaInterna.SelectedIndex == 0);

            sel.BuscaInterna.Text = ""; EsperarIdle(); EsperarIdle();
            anota("filtro limpo");
            confere("filtro limpo: a lista deveria voltar ao topo (offset 0)", Math.Abs(off()) < 0.5);

            // reabrir com o atual bem adiante (a lista guardou o deslocamento da sessão anterior)
            sel.Fechar(); EsperarIdle();
            abrirEConfere(nomes[nomes.Count * 2 / 3]);
            sel.Fechar(); EsperarIdle();
            abrirEConfere(nomes[2]);   // perto do topo: offset 0
            sel.Fechar(); EsperarIdle();
            abrirEConfere(nomes[nomes.Count - 1]);   // último: a lista termina no fim, com o atual visível
            sel.Fechar(); EsperarIdle();
            File.WriteAllText(Path.Combine(pastaSaida, "W7-seletor.txt"), "OK\n" + string.Join("\n", log));

            // PNGs do conteúdo do popup (popup não entra na captura da janela): filtrado em "upn" e aberto no perfil atual
            foreach (var (busca, arq) in new[] { ("upn", "W7-seletor-2-filtro-upn.png"), ("", "W7-seletor-3-atual.png") })
            {
                atual = "W250x73"; sel.Mostrar(atual);
                sel.Preparar(busca);
                var c = sel.RetirarConteudo();
                var jc = new Window { Content = c, Background = TemaPlant.BSup1, Width = 330, Height = 420 }; PrepararDialogoForaDaTela(jc); jc.Show(); EsperarIdle(); EsperarIdle();
                var rol = RolagemDe(sel.ListaInterna);
                log.Add("png " + arq + ": offset " + (rol?.VerticalOffset ?? -1));
                if (busca == "upn" && (rol == null || Math.Abs(rol.VerticalOffset) > 0.5)) throw new InvalidOperationException("seletor filtrado (conteúdo hospedado): offset " + rol?.VerticalOffset);
                RenderizarJanela(jc, 330, 420, arq);
                jc.Content = null; jc.Close(); sel.DevolverConteudo(c);
            }
            File.WriteAllText(Path.Combine(pastaSaida, "W7-seletor.txt"), "OK\n" + string.Join("\n", log));
            jh.Close();
        }
    }
}
