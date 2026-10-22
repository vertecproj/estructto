// Capturas da v0.6 Frente B: abas da faixa, painéis fechados/abertos, seletor de seção, projeto em branco.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturarV06(MainWindow w)
        {
            // B1: desenho da seção por forma (JSON reais do motor em amostras/perfis-formas.json)
            var formas = Amostras.Ler("perfis-formas").AsArray().ToList();
            var jf = new JanelaCatalogo(Amostras.Ler("catalogo"), n => System.Threading.Tasks.Task.FromResult(formas.FirstOrDefault(x => (string)x["nome"] == n)));
            PrepararDialogoForaDaTela(jf); jf.Show(); EsperarIdle();
            foreach (var f in formas)
            {
                jf.Escolher((string)f["nome"]); EsperarIdle();
                RenderizarJanela(jf, (int)jf.Width, (int)jf.Height, "46-catalogo-forma-" + (string)f["secao"]["props"]["tipo"] + "-" + (string)f["familia"] + ".png");
            }
            jf.Close();

            // B6: uma captura por aba nova da faixa (Estrutura = a antiga Modelo)
            foreach (var (aba, nome) in new[] { (w.abaModelo, "estrutura"), (w.abaDesenhar, "desenhar"), (w.abaEditar, "editar"), (w.abaApoios, "apoios") })
            {
                w.Faixa.SelectedTabItem = aba; EsperarIdle();
                CapturarPrincipal(w, "47-faixa-" + nome);
            }
            // B4: Avisos e Tabelas fechados por padrão; abertos só quando o usuário abre
            if (w.PainelTabelas.IsVisible) w.PainelTabelas.Hide();
            if (w.PainelAvisos.IsVisible) w.PainelAvisos.Hide();
            EsperarIdle(); CapturarPrincipal(w, "47-paineis-fechados");
            w.PainelTabelas.Show(); w.PainelAvisos.Show(); EsperarIdle();
            CapturarPrincipal(w, "47-paineis-abertos");
            w.PainelTabelas.Hide(); w.PainelAvisos.Hide(); EsperarIdle();

            // B2: seletor de seção (busca + árvore), em janela própria (o Popup não sai na captura)
            SeletorSecao.Injetar(Amostras.Ler("catalogo"), Amostras.Ler("catalogo-arvore"));
            string[] noModelo = { "W310x38.7", "W360x51", "Tubo 50x50x3,00" };
            foreach (var (termo, png) in new[] { ("", "47-seletor-secao-arvore.png"), ("tubo 50", "47-seletor-secao-busca.png") })
            {
                var sel = new SeletorSecao(() => noModelo, "W360x51", _ => { });
                var h = new Window { Content = sel, Width = 360, Height = 450, Background = TemaPlant.BSup1, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                PrepararDialogoForaDaTela(h); h.Show(); EsperarIdle();
                if (termo != "") { var tb = FindTextBox(sel); tb.Text = termo; EsperarIdle(); }
                else
                {
                    var tv = FindTree(sel);
                    if (tv != null) { var raiz = (TreeViewItem)tv.Items[0]; var n1 = raiz.Items.OfType<TreeViewItem>().FirstOrDefault(x => ((TextBlock)x.Header).Text.StartsWith("Metalon")) ?? (TreeViewItem)raiz.Items[0]; n1.IsExpanded = true; EsperarIdle(); }
                }
                CapturarDialogo(h, png);
                h.Close();
            }

            // B3/B1: projeto em branco, sem grade, barras nem mensagem de "modelo vazio"
            Esperar(w.AbrirEspacial(() => DocumentoEspacial.Vazio("Em branco"), "abrindo"));
            w.Faixa.SelectedTabItem = w.abaDesenhar; EsperarIdle();
            CapturarPrincipal(w, "47-em-branco");
        }

        static TextBox FindTextBox(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is TextBox t) return t;
                var r = FindTextBox(c); if (r != null) return r;
            }
            return null;
        }

        static TreeView FindTree(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is TreeView t) return t;
                var r = FindTree(c); if (r != null) return r;
            }
            return null;
        }
    }
}
