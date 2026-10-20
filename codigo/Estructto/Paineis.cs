// Paineis encaixaveis: Projeto (arvore), Propriedades (varios itens de uma vez), Tabelas de resultados e Avisos.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    // linhas das tabelas (DisplayName vira o cabecalho da coluna)
    public class LinhaNo
    {
        [Browsable(false)] public No Obj { get; set; }
        [DisplayName("Nó")] public int Id { get; set; }
        [DisplayName("X (m)")] public string X { get; set; }
        [DisplayName("Y (m)")] public string Y { get; set; }
        [DisplayName("Apoio")] public string Apoio { get; set; }
        [DisplayName("Desl. X (mm)")] public string DX { get; set; }
        [DisplayName("Desl. Y (mm)")] public string DY { get; set; }
        [DisplayName("Giro (rad)")] public string Giro { get; set; }
        [DisplayName("Rx (kN)")] public string Rx { get; set; }
        [DisplayName("Ry (kN)")] public string Ry { get; set; }
        [DisplayName("M (kN·m)")] public string Rm { get; set; }
    }

    public class LinhaBarra
    {
        [Browsable(false)] public Barra Obj { get; set; }
        [DisplayName("Barra")] public int Id { get; set; }
        [DisplayName("Nós")] public string Nos { get; set; }
        [DisplayName("Perfil")] public string Perfil { get; set; }
        [DisplayName("L (m)")] public string L { get; set; }
        [DisplayName("N mín/máx (kN)")] public string N { get; set; }
        [DisplayName("Q mín/máx (kN)")] public string Q { get; set; }
        [DisplayName("M mín/máx (kN·m)")] public string Mm { get; set; }
        [DisplayName("Flecha (mm)")] public string Flecha { get; set; }
    }

    public class LinhaVerif
    {
        [Browsable(false)] public Barra Obj { get; set; }
        [DisplayName("Barra")] public int Id { get; set; }
        [DisplayName("Perfil")] public string Perfil { get; set; }
        [DisplayName("Situação")] public string Situacao { get; set; }
        [DisplayName("Aproveitamento")] public string Ap { get; set; }
        [DisplayName("Limitado por")] public string Critico { get; set; }
        [DisplayName("Tensão")] public string Tensao { get; set; }
        [DisplayName("Corte")] public string Corte { get; set; }
        [DisplayName("Flecha")] public string Flecha { get; set; }
        [DisplayName("Flambagem")] public string Flamb { get; set; }
        [DisplayName("KL/r no plano")] public string LamPl { get; set; }
        [DisplayName("KL/r fora do plano")] public string LamFo { get; set; }
        [DisplayName("Esbeltez")] public string Esb { get; set; }
    }

    public partial class MainWindow
    {
        bool sincronizando, avisosJaMostrados;
        string erroCalculo = "";

        void SelecaoMudou()
        {
            bool barras = Vista.Sel.OfType<Barra>().Any(), nos = Vista.Sel.OfType<No>().Any();
            CtxBarra.Visibility = barras ? Visibility.Visible : Visibility.Collapsed;
            CtxNo.Visibility = nos ? Visibility.Visible : Visibility.Collapsed;
            if (barras) tgEixoFraco.IsChecked = Vista.Sel.OfType<Barra>().All(b => b.EixoFraco);
            MostrarPropriedades();
            SincronizarArvore();
            SincronizarTabelas();
            if (Vista.Sel.Count > 0)
                StatusMsg.Text = Vista.Sel.Count + (barras ? " barra(s)" : " nó(s)") + " selecionado(s) — edite em Propriedades ou use a aba contextual; Del apaga.";
        }

        void MostrarPropriedades()
        {
            if (Vista.Sel.Count == 0)
            {
                Prop.SelectedObject = M;
                PropTitulo.Text = "Modelo (nada selecionado)";
                return;
            }
            Prop.SelectedObjects = Vista.Sel.ToArray();
            if (Vista.Sel.Count == 1)
                PropTitulo.Text = Vista.Sel[0] is No n ? "Nó " + n.Id : "Barra " + ((Barra)Vista.Sel[0]).Id;
            else
                PropTitulo.Text = Vista.Sel.Count + (Vista.Sel[0] is No ? " nós" : " barras") + " — valores diferentes ficam em branco";
        }

        // Coluna dos nomes da grade de propriedades com ~58% da largura (o padrao corta "Considerar peso próprio").
        void AjustarDivisorPropriedades()
        {
            try
            {
                var f = typeof(System.Windows.Forms.PropertyGrid).GetField("_gridView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                     ?? typeof(System.Windows.Forms.PropertyGrid).GetField("gridView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                object gv = f?.GetValue(Prop);
                gv?.GetType().GetMethod("MoveSplitterTo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  ?.Invoke(gv, new object[] { (int)(Prop.Width * 0.58) });
            }
            catch { }
        }

        // ---------- arvore ----------

        void AtualizarArvore()
        {
            sincronizando = true;
            Arvore.Items.Clear();
            var raiz = Item("Modelo" + (arquivo != null ? " — " + System.IO.Path.GetFileNameWithoutExtension(arquivo) : ""), null, true, "Raiz do modelo atual. Expanda os grupos abaixo para inspecionar e selecionar nós, barras, seções, materiais, apoios e cargas.");
            var nos = Item("Nós (" + M.Nos.Count + ")", M.Nos.Cast<object>().ToList(), M.Nos.Count < 40, "Clique para selecionar todos os nós do modelo na vista.");
            foreach (No n in M.Nos)
                nos.Items.Add(Item("Nó " + n.Id + "   (" + Num.Fmt(n.X, 2) + "; " + Num.Fmt(n.Y, 2) + ")" + (n.TemApoio ? "   ▲ " + NomeApoio(n) : "") + (n.TemCarga ? "   ↓" : "") + (n.Placa != null ? "   placa" : ""), n, false, "Clique para selecionar o nó " + n.Id + " na vista e editar suas coordenadas, apoios ou cargas no painel Propriedades."));
            var barras = Item("Barras (" + M.Barras.Count + ")", M.Barras.Cast<object>().ToList(), M.Barras.Count < 40, "Clique para selecionar todas as barras do modelo na vista.");
            foreach (Barra b in M.Barras)
                barras.Items.Add(Item("Barra " + b.Id + "   " + b.Secao + (b.TemCarga ? "   ↓" : "") + (b.RotulaInicio || b.RotulaFim ? "   ○" : ""), b, false, "Clique para selecionar a barra " + b.Id + " (" + b.Secao + ", " + b.Material + ") na vista e editar suas propriedades."));
            var secs = Item("Seções em uso", null, true, "Agrupa as barras do modelo pelo perfil transversal aplicado.");
            foreach (var g in M.Barras.GroupBy(b => b.Secao)) secs.Items.Add(Item(g.Key + "   (" + g.Count() + ")", g.Cast<object>().ToList(), false, "Clique para selecionar todas as barras com o perfil \"" + g.Key + "\"."));
            var mats = Item("Materiais em uso", null, true, "Agrupa as barras do modelo pelo material estrutural aplicado.");
            foreach (var g in M.Barras.GroupBy(b => b.Material)) mats.Items.Add(Item(g.Key + "   (" + g.Count() + ")", g.Cast<object>().ToList(), false, "Clique para selecionar todas as barras com o material \"" + g.Key + "\"."));
            var apoios = Item("Apoios", M.Nos.Where(n => n.TemApoio).Cast<object>().ToList(), false, "Clique para selecionar todos os nós que possuem apoio ou mola.");
            var cargas = Item("Itens com carga", M.Nos.Where(n => n.TemCarga).Cast<object>().Concat(M.Barras.Where(b => b.TemCarga)).ToList(), false, "Clique para selecionar os itens que possuem carga aplicada no caso ativo.");
            raiz.Items.Add(nos); raiz.Items.Add(barras); raiz.Items.Add(secs); raiz.Items.Add(mats); raiz.Items.Add(apoios); raiz.Items.Add(cargas);
            Arvore.Items.Add(raiz);
            sincronizando = false;
        }

        static string NomeApoio(No n)
        {
            if (n.ApoioX && n.ApoioY && n.ApoioGiro) return "engaste";
            if (n.ApoioX && n.ApoioY) return "fixo";
            if (n.ApoioY) return "móvel";
            if (n.ApoioX) return "móvel em X";
            return "mola/giro";
        }

        static TreeViewItem Item(string txt, object tag, bool aberto, string dica = null)
        {
            return new TreeViewItem { Header = txt, Tag = tag, IsExpanded = aberto, Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)), FontSize = 12, ToolTip = dica };
        }

        void ArvoreMudou(object s, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sincronizando) return;
            var it = Arvore.SelectedItem as TreeViewItem;
            if (it == null || it.Tag == null) return;
            sincronizando = true;
            if (it.Tag is No || it.Tag is Barra) Vista.Selecionar(new[] { it.Tag });
            else if (it.Tag is List<object> lista)
            {
                // grupo: seleciona os itens de um tipo so (barras tem preferencia)
                var bs = lista.OfType<Barra>().Cast<object>().ToList();
                Vista.Selecionar(bs.Count > 0 ? bs : lista.OfType<No>().Cast<object>().ToList());
            }
            sincronizando = false;
        }

        void SincronizarArvore()
        {
            if (sincronizando || Vista.Sel.Count != 1) return;
            sincronizando = true;
            foreach (TreeViewItem raiz in Arvore.Items)
                foreach (TreeViewItem grupo in raiz.Items)
                    foreach (TreeViewItem it in grupo.Items)
                        if (it.Tag == Vista.Sel[0]) { grupo.IsExpanded = true; it.IsSelected = true; it.BringIntoView(); }
            sincronizando = false;
        }

        // ---------- tabelas ----------

        void AtualizarTabelas()
        {
            sincronizando = true;
            bool ok = Vista.Resolvido;
            TabNos.ItemsSource = M.Nos.Select(n => new LinhaNo
            {
                Obj = n, Id = n.Id, X = Num.Fmt(n.X, 3), Y = Num.Fmt(n.Y, 3), Apoio = n.TemApoio ? NomeApoio(n) : "",
                // na envoltoria, cada celula mostra "mínimo / máximo" entre as combinacoes
                DX = ok && n.Res != null ? FaixaNo(n, 0, 1000, 3) : "", DY = ok && n.Res != null ? FaixaNo(n, 1, 1000, 3) : "",
                Giro = ok && n.Res != null ? FaixaNo(n, 2, 1, 6) : "",
                Rx = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 3, 1, 2) : "", Ry = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 4, 1, 2) : "",
                Rm = ok && n.Res != null && n.TemApoio ? FaixaNo(n, 5, 1, 2) : ""
            }).ToList();
            TabBarras.ItemsSource = M.Barras.Select(b =>
            {
                var l = new LinhaBarra { Obj = b, Id = b.Id, Nos = b.N1 + " → " + b.N2, Perfil = b.Secao + (b.EixoFraco ? " (deitado)" : ""), L = Num.Fmt(M.Comp(b), 3) };
                if (ok && b.Res != null)
                {
                    l.N = FaixaBarra(b, 0); l.Q = FaixaBarra(b, 1); l.Mm = FaixaBarra(b, 2);
                    double f = Flecha(b);
                    l.Flecha = Num.Fmt(f * 1000, 2) + (Math.Abs(f) > 1e-9 ? "  (L/" + Math.Round(M.Comp(b) / Math.Abs(f)) + ")" : "");
                }
                return l;
            }).ToList();
            AbasTabela.ToolTip = ok ? "Resultado: " + NomeResultado() + ". Clique numa linha para destacar o item na vista." : "Tabelas de nós, barras, verificação e soldas (calcule a estrutura para ver os esforços e deslocamentos).";
            TabVerif.ItemsSource = M.Barras.Select(b =>
            {
                var l = new LinhaVerif { Obj = b, Id = b.Id, Perfil = b.Secao };
                if (!ok || b.Res == null) { l.Situacao = "sem resultados"; return l; }
                Verif v = Vista.Verificar(b);
                if (!v.Aplicavel) { l.Situacao = v.Motivo; return l; }
                l.Situacao = v.Passa ? "passa" : "NÃO PASSA"; l.Ap = Num.Fmt(v.Ap * 100, 0) + "%"; l.Critico = v.Critico;
                l.Tensao = Num.Fmt(v.ApTensao * 100, 0) + "%"; l.Corte = Num.Fmt(v.ApCorte * 100, 0) + "%"; l.Flecha = Num.Fmt(v.ApFlecha * 100, 0) + "%";
                l.Flamb = v.Nc > 1e-6 ? Num.Fmt(v.ApFlamb * 100, 0) + "%" : "—";
                Func<Flamb, string> lam = f => f == null ? "—" : f.Instavel ? "∞ (sem travamento)" : Num.Fmt(f.Lambda, 0) + "  (K " + Num.Fmt(f.K, 2) + ")";
                l.LamPl = lam(v.NoPlano); l.LamFo = lam(v.ForaPlano);
                l.Esb = v.LimEsbeltez > 0 ? Num.Fmt(v.ApEsbeltez * 100, 0) + "%  (" + (v.LimEsbeltez == 200 ? "KL/r" : "L/r") + " " + Num.Fmt(v.ApEsbeltez * v.LimEsbeltez, 0) + (v.ApEsbeltez > 1 ? " > " : " ≤ ") + Num.Fmt(v.LimEsbeltez, 0) + ")" : "—";
                return l;
            }).OrderByDescending(x => x.Situacao == "NÃO PASSA").ToList();
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif })
            {
                dg.AutoGeneratingColumn -= Coluna; dg.AutoGeneratingColumn += Coluna;
            }
            AtualizarSoldas();
            sincronizando = false;
        }

        static void Coluna(object s, DataGridAutoGeneratingColumnEventArgs e)
        {
            var pd = e.PropertyDescriptor as PropertyDescriptor;
            if (pd == null) return;
            if (!pd.IsBrowsable) { e.Cancel = true; return; }
            e.Column.Header = pd.DisplayName;
        }

        void SelecionarDaTabela(DataGrid dg)
        {
            if (sincronizando) return;
            var objs = new List<object>();
            foreach (var it in dg.SelectedItems)
            {
                if (it is LinhaNo ln) objs.Add(ln.Obj);
                else if (it is LinhaBarra lb) objs.Add(lb.Obj);
                else if (it is LinhaVerif lv) objs.Add(lv.Obj);
            }
            if (objs.Count == 0) return;
            sincronizando = true;
            Vista.Selecionar(objs);
            sincronizando = false;
        }

        void SincronizarTabelas()
        {
            if (sincronizando) return;
            sincronizando = true;
            foreach (var dg in new[] { TabNos, TabBarras, TabVerif })
            {
                dg.SelectedItems.Clear();
                if (dg.ItemsSource == null) continue;
                foreach (var it in dg.ItemsSource)
                {
                    object o = it is LinhaNo ln ? ln.Obj : it is LinhaBarra lb ? (object)lb.Obj : it is LinhaVerif lv ? lv.Obj : null;
                    if (o != null && Vista.Sel.Contains(o)) { dg.SelectedItems.Add(it); dg.ScrollIntoView(it); }
                }
            }
            sincronizando = false;
        }

        // ---------- avisos (checagem antes do calculo) ----------

        void Diagnosticar()
        {
            var lista = new List<(string, string, List<object>)>();   // nivel, texto, itens
            if (M.Barras.Count > 0 && !M.Nos.Any(n => n.TemApoio))
                lista.Add(("erro", "Nenhum apoio. Selecione os nós da base e use Estrutura → Engaste/Fixo/Móvel.", new List<object>()));
            foreach (Barra b in M.Barras)
            {
                if (Biblioteca.AcharSecao(b.Secao) == null) lista.Add(("erro", "Barra " + b.Id + ": seção \"" + b.Secao + "\" não reconhecida.", new List<object> { b }));
                if (Biblioteca.AcharMaterial(b.Material) == null) lista.Add(("erro", "Barra " + b.Id + ": material \"" + b.Material + "\" não reconhecido.", new List<object> { b }));
                if (M.Comp(b) < 1e-6) lista.Add(("erro", "Barra " + b.Id + " com comprimento zero.", new List<object> { b }));
            }
            foreach (No n in M.Nos.Where(x => !x.TemApoio))
            {
                var bs = M.Barras.Where(b => b.N1 == n.Id || b.N2 == n.Id).ToList();
                if (bs.Count == 0) { lista.Add(("aviso", "Nó " + n.Id + " solto (sem barra).", new List<object> { n })); continue; }
                bool rot = bs.All(b => b.N1 == n.Id ? b.RotulaInicio : b.RotulaFim);
                bool alinh = bs.All(b => Math.Abs(Math.Sin((M.Ang(b) - M.Ang(bs[0])) * Math.PI / 180)) < 1e-6);
                if (rot && alinh) lista.Add(("erro", "Nó " + n.Id + ": barras rotuladas e alinhadas — mecanismo (o nó não resiste na direção perpendicular).", new List<object> { n }));
            }
            bool temCarga = M.Nos.Any(n => n.TemCarga) || M.Barras.Any(b => b.TemCarga) || M.PesoProprio || M.Nos.Any(n => n.RecalqueX != 0 || n.RecalqueY != 0);
            if (M.Barras.Count > 0 && !temCarga) lista.Add(("info", "Nenhuma carga aplicada: os esforços serão nulos. Use a aba Cargas.", new List<object>()));
            if (Vista.Resolvido)
            {
                var falham = M.Barras.Where(b => { var v = Vista.Verificar(b); return v.Aplicavel && !v.Passa; }).Cast<object>().ToList();
                if (falham.Count > 0) lista.Add(("aviso", falham.Count + " barra(s) não passam na verificação. Clique para selecionar; Resultados → Comparar perfis sugere um perfil.", falham));
                foreach (No n in M.Nos.Where(x => x.Placa != null))
                {
                    ResPlaca rp = PlacaPior(n);
                    if (rp.Erro != "") lista.Add(("aviso", "Placa do nó " + n.Id + ": " + rp.Erro, new List<object> { n }));
                    else if (rp.TemEsforcos && (!rp.Passa || rp.EspNec > n.Placa.Esp))
                    {
                        var mot = new List<string>();
                        if (!rp.ChOk && rp.EspacOk) mot.Add(rp.Ch != null ? "chumbador" : "parafuso");
                        if (!rp.EspacOk) mot.Add("espaçamento entre chumbadores");
                        if (rp.EspNec > n.Placa.Esp) mot.Add("chapa");
                        if (rp.Inclinada) mot.Add("barra inclinada: chapa e solda não verificadas");
                        lista.Add(("aviso", "Placa do nó " + n.Id + " não passa (" + string.Join(", ", mot) + ").", new List<object> { n }));
                    }
                }
            }
            else if (M.Barras.Count > 0 && M.Nos.Any(n => n.TemApoio) && erroCalculo != "")
                lista.Add(("erro", erroCalculo, new List<object>()));

            ListaAvisos.Items.Clear();
            foreach (var (nivel, texto, objs) in lista)
            {
                var it = new ListBoxItem
                {
                    Content = (nivel == "erro" ? "⛔  " : nivel == "aviso" ? "⚠  " : "ℹ  ") + texto, Tag = objs.Count > 0 ? objs : null,
                    Foreground = nivel == "erro" ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)) : nivel == "aviso" ? new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x68)) : new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD8)),
                    Padding = new Thickness(6, 3, 6, 3), Cursor = objs.Count > 0 ? System.Windows.Input.Cursors.Hand : null,
                    ToolTip = objs.Count > 0 ? texto + " (clique para selecionar os itens envolvidos na vista)." : texto
                };
                ListaAvisos.Items.Add(it);
            }
            if (lista.Count == 0) ListaAvisos.Items.Add(new ListBoxItem { Content = "✓  Nenhum problema encontrado.", Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64)), Padding = new Thickness(6, 3, 6, 3), ToolTip = "A geometria, os apoios, as cargas e as verificações não apresentam alertas." });
            int erros = lista.Count(x => x.Item1 == "erro"), avisos = lista.Count(x => x.Item1 == "aviso");
            string resumo = (erros > 0 ? "⛔ " + erros + "  " : "") + (avisos > 0 ? "⚠ " + avisos : "");
            PainelAvisos.Title = resumo == "" ? "Avisos" : "Avisos (" + resumo.Trim() + ")";
            SeloAvisos.Content = string.Join("  ", new[] { erros > 0 ? "⛔ " + erros + (erros == 1 ? " erro" : " erros") : null, avisos > 0 ? "⚠ " + avisos + (avisos == 1 ? " aviso" : " avisos") : null }.Where(x => x != null));
            SeloAvisos.Visibility = resumo == "" ? Visibility.Collapsed : Visibility.Visible;
            // um erro novo traz o painel para frente uma vez
            if (erros > 0 && !avisosJaMostrados) { PainelAvisos.Show(); PainelAvisos.IsSelected = true; avisosJaMostrados = true; }
            if (erros == 0) avisosJaMostrados = false;
        }
    }
}
