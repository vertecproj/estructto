// Carga de vento da ABNT NBR 6123:2023 nas barras do pórtico: janela com V0, S1, S2 (categoria e classe), S3 e a prévia
// barra a barra. O cálculo é o do motor (VentoNbr em Nucleo.cs); critérios e hipóteses em docs/VENTO.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        public const string CasoVentoDir = "Vento → (0°)", CasoVentoEsq = "Vento ← (180°)";
        public const string CasoVentoServDir = "Vento serviço → (0°)", CasoVentoServEsq = "Vento serviço ← (180°)";
        public const string CasoVentoMaxDir = "Vento máximo → (0°)", CasoVentoMaxEsq = "Vento máximo ← (180°)";
        static readonly string[] CasosNbr6123 = { CasoVentoDir, CasoVentoEsq };
        static readonly string[] CasosNbr8400 = { CasoVentoServDir, CasoVentoServEsq, CasoVentoMaxDir, CasoVentoMaxEsq };

        void ApagarCasosVento(string[] nomes)
        {
            foreach (string nome in nomes)
            {
                int i = M.Casos.FindIndex(c => c.Nome == nome);
                if (i >= 0 && M.Casos.Count > 1) M.RemoverCaso(i);
            }
        }

        void AbrirVento()
        {
            var j = new JanelaVento(M) { Owner = this };
            if (j.ShowDialog() != true) return;
            if (j.Remover)
            {
                ApagarCasosVento(CasosNbr6123); ApagarCasosVento(CasosNbr8400);
                M.Vento = null;
                if (j.GerarComb) M.Combinacoes = Analise.Gerar(M);
                Confirmar("Vento removido (casos de vento apagados).");
                return;
            }
            var p = j.Param;
            if (p.Norma == "8400")
            {
                ApagarCasosVento(CasosNbr6123);   // não misturar as duas normas no mesmo modelo
                var serv = Vento8400.Calcular(M, p, false); var maxi = Vento8400.Calcular(M, p, true);
                VentoNbr.Aplicar(M, serv, VentoNbr.GarantirCaso(M, CasoVentoServDir), +1);
                VentoNbr.Aplicar(M, serv, VentoNbr.GarantirCaso(M, CasoVentoServEsq), -1);
                VentoNbr.Aplicar(M, maxi, VentoNbr.GarantirCaso(M, CasoVentoMaxDir), +1);
                VentoNbr.Aplicar(M, maxi, VentoNbr.GarantirCaso(M, CasoVentoMaxEsq), -1);
                M.Vento = p.Copiar();
                if (j.GerarComb) M.Combinacoes = Analise.Gerar(M);
                Confirmar("Vento NBR 8400 aplicado em " + Pl.N(serv.Count(v => Math.Abs(v.W) > 1e-9), "barra", "barras") + " (casos de serviço e de vento máximo, nos dois sentidos)" +
                          (j.GerarComb ? "; combinações NBR 8400 (casos I, II e III) geradas." : "; as combinações atuais não foram mudadas."));
                return;
            }
            ApagarCasosVento(CasosNbr8400);
            var lista = VentoNbr.Calcular(M, p);
            int cDir = VentoNbr.GarantirCaso(M, CasoVentoDir), cEsq = VentoNbr.GarantirCaso(M, CasoVentoEsq);
            VentoNbr.Aplicar(M, lista, cDir, +1);
            VentoNbr.Aplicar(M, lista, cEsq, -1);
            M.Vento = p.Copiar();
            if (j.GerarComb) M.Combinacoes = Analise.Gerar(M);
            int carregadas = lista.Count(v => Math.Abs(v.W) > 1e-9);
            Confirmar("Vento NBR 6123 aplicado em " + Pl.N(carregadas, "barra", "barras") + " nos casos \"" + CasoVentoDir + "\" e \"" + CasoVentoEsq + "\"" +
                      (j.GerarComb ? "; combinações NBR 8681 regeradas." : "; as combinações atuais não foram mudadas."));
        }
    }

    public class JanelaVento : JanelaEscura
    {
        public ParamVento Param;
        public bool Remover, GerarComb = true;
        readonly Modelo m;
        readonly StackPanel previa = new StackPanel();
        readonly TextBlock resumo = T("", 12.5, TemaPlant.BTexto1, FontWeights.SemiBold);
        readonly TextBlock erro = T("", 11.5, BVermelho);
        TextBox tbV0, tbS1, tbVServ, tbPsi;
        StackPanel f6123, f8400;
        ComboBox cbS1, cbCat, cbClasse, cbS3, cbNorma, cbGrupo;
        bool Nbr8400 { get { return cbNorma.SelectedIndex == 1; } }
        CheckBox ckK, ckComb;
        TextBlock notaDir;
        bool montando = true;

        static readonly string[] DescCat =
        {
            "I — mar calmo, lagos, rios (superfície lisa > 5 km)",
            "II — campo aberto, poucos obstáculos isolados (≤ 1 m)",
            "III — sítios, sebes, muros, casas esparsas (≈ 3 m)",
            "IV — obstáculos numerosos: cidade pequena, área industrial (≈ 10 m)",
            "V — obstáculos grandes e altos: centro de cidade, complexo industrial (≥ 25 m)"
        };
        static readonly string[] DescS3 =
        {
            "Grupo 1 — ruína afeta socorro (hospital, bombeiros), inflamáveis/tóxicos · 1,11",
            "Grupo 2 — risco à vida em aglomerações (> 300 pessoas, escolas, creches) · 1,06",
            "Grupo 3 — residências, comércio, indústrias; estruturas desmontáveis · 1,00",
            "Grupo 4 — sem ocupação humana e sem pessoas no entorno (depósitos, silos) · 0,95",
            "Grupo 5 — temporárias não reutilizáveis; estruturas durante a construção · 0,83"
        };

        public JanelaVento(Modelo modelo)
        {
            m = modelo;
            Title = "Carga de vento — ABNT NBR 6123:2023"; Width = 1180; Height = 720; MinWidth = 900; MinHeight = 520;
            ParamVento p = m.Vento != null ? m.Vento.Copiar() : new ParamVento();

            var raiz = new DockPanel();
            var rod = new DockPanel { Margin = new Thickness(18, 8, 18, 12) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            if (m.Vento != null) bts.Children.Add(B("Remover vento", () => { Remover = true; GerarComb = ckComb.IsChecked == true; DialogResult = true; }, false, "Apaga os dois casos de vento e os dados do vento do modelo."));
            bts.Children.Add(B("Aplicar", Aplicar, true, "Cria (ou atualiza) os casos \"" + MainWindow.CasoVentoDir + "\" e \"" + MainWindow.CasoVentoEsq + "\" com estas cargas."));
            bts.Children.Add(B("Cancelar", Close, false, "Fecha sem mudar o modelo (Esc)."));
            DockPanel.SetDock(bts, Dock.Right); rod.Children.Add(bts);
            rod.Children.Add(erro);
            DockPanel.SetDock(rod, Dock.Bottom); raiz.Children.Add(rod);

            // coluna da esquerda: dados
            var form = new StackPanel { Margin = new Thickness(18, 12, 14, 8), Width = 420 };
            f6123 = new StackPanel(); f8400 = new StackPanel();
            form.Children.Add(UiPlaca.TituloSecao("Norma do vento", null, 0));
            cbNorma = Combo(new[] { "ABNT NBR 6123:2023 — edificações e estruturas em geral", "ABNT NBR 8400:1984 — equipamentos de elevação" }, 420);
            cbNorma.SelectedIndex = p.Norma == "8400" ? 1 : 0;
            cbNorma.ToolTip = "Escolhe qual norma calcula a força do vento nas barras. Cada norma tem seus próprios casos de carga e suas próprias combinações.";
            form.Children.Add(cbNorma);
            form.Children.Add(f6123); form.Children.Add(f8400);
            f6123.Children.Add(UiPlaca.TituloSecao("Velocidade básica V0", null, 12));
            tbV0 = Campo(Num.Fmt(p.V0, 1), 80, "Velocidade básica do vento no local (m/s): Figura 1 da NBR 6123 (isopletas, de 5 em 5 m/s).");
            f6123.Children.Add(Linha("V0 (m/s)", tbV0));
            f6123.Children.Add(Nota("Leia no mapa de isopletas (Figura 1 da norma) para a cidade da obra. Rajada de 3 s, a 10 m, campo aberto, período de retorno de 50 anos (5.1)."));

            f6123.Children.Add(UiPlaca.TituloSecao("Topografia — S1 (5.2)", null, 12));
            cbS1 = Combo(new[] { "Terreno plano ou pouco acidentado — 1,00", "Vale profundo, protegido de ventos — 0,90", "Talude ou morro — digitar S1 calculado" }, 400);
            int iS1 = Math.Abs(p.S1 - 1) < 1e-9 ? 0 : Math.Abs(p.S1 - 0.9) < 1e-9 ? 1 : 2;
            cbS1.SelectedIndex = iS1;
            tbS1 = Campo(Num.Fmt(p.S1, 2), 70, "S1 calculado pela 5.2 b) para talude ou morro (ponto B: 1,0 + (2,5 − z/dt)·tg(θt − 3°) ...).");
            f6123.Children.Add(cbS1);
            f6123.Children.Add(Linha("S1", tbS1));

            f6123.Children.Add(UiPlaca.TituloSecao("Rugosidade e tamanho — S2 (5.3)", null, 12));
            cbCat = Combo(DescCat, 400); cbCat.SelectedIndex = Math.Max(0, Math.Min(4, p.Categoria - 1));
            f6123.Children.Add(Linha("Categoria", cbCat, 70));
            cbClasse = Combo(new[] { "", "Classe A — maior dimensão até 20 m", "Classe B — de 20 a 50 m", "Classe C — acima de 50 m" }, 400);
            cbClasse.SelectedIndex = Math.Max(0, Array.IndexOf(VentoNbr.Classes, p.Classe ?? "") + 1);
            f6123.Children.Add(Linha("Classe", cbClasse, 70));

            f6123.Children.Add(UiPlaca.TituloSecao("Fator estatístico — S3 (Tabela 4)", null, 12));
            cbS3 = Combo(DescS3, 420);
            int iS3 = Array.FindIndex(VentoNbr.S3Grupos, v => Math.Abs(v - p.S3) < 1e-9);
            cbS3.SelectedIndex = iS3 >= 0 ? iS3 : 2;
            f6123.Children.Add(cbS3);

            f6123.Children.Add(UiPlaca.TituloSecao("Opções", null, 12));
            ckK = new CheckBox { Content = "Fator K de barra de comprimento finito (Tabela 28)", IsChecked = p.UsarK, Foreground = BTexto, Margin = new Thickness(0, 2, 0, 4),
                ToolTip = "Reduz o coeficiente de barras curtas (l/c pequeno). Desligado = K = 1,0 (lado seguro)." };
            ckComb = new CheckBox { Content = "Gerar as combinações da norma escolhida com o vento (substitui as atuais)", IsChecked = true, Foreground = BTexto, Margin = new Thickness(0, 2, 0, 4),
                ToolTip = "Sem isso, os casos de vento são criados com fator 0 nas combinações que já existem." };
            f6123.Children.Add(ckK);

            // NBR 8400: vento limite de serviço e vento máximo (Tabela 7), grupo (Mx) e coeficiente dinâmico
            f8400.Children.Add(UiPlaca.TituloSecao("Vento limite de serviço (5.5.4.1)", null, 12));
            tbVServ = Campo(Num.Fmt(p.VServ, 1), 80, "Velocidade do vento acima da qual o equipamento deve parar de operar. A Tabela 7 da norma dá 20 m/s (72 km/h), que resulta em 250 N/m² em qualquer altura.");
            f8400.Children.Add(Linha("vw serviço (m/s)", tbVServ, 140));
            f8400.Children.Add(Nota("Pressão aerodinâmica Pa = vw²/1,6. Com 20 m/s, Pa = 250 N/m² em qualquer altura. O vento deve ser tomado na direção mais desfavorável (o programa gera os dois sentidos)."));
            f8400.Children.Add(UiPlaca.TituloSecao("Vento máximo, fora de serviço (Tabela 7)", null, 12));
            f8400.Children.Add(Nota("Fixo pela norma, conforme a altura da barra acima do terreno: 800 N/m² (0 a 20 m, 36 m/s), 1100 N/m² (20 a 100 m, 42 m/s) e 1300 N/m² (acima de 100 m, 46 m/s). Equipamento sem carga."));
            f8400.Children.Add(UiPlaca.TituloSecao("Grupo da estrutura e carga de serviço", null, 12));
            cbGrupo = Combo(new[] { "Grupo 1 — Mx 1,00", "Grupo 2 — Mx 1,00", "Grupo 3 — Mx 1,00", "Grupo 4 — Mx 1,06", "Grupo 5 — Mx 1,12", "Grupo 6 — Mx 1,20" }, 260);
            cbGrupo.SelectedIndex = Math.Max(0, Math.Min(5, p.Grupo - 1));
            cbGrupo.ToolTip = "Grupo da estrutura (Tabela 4 da norma, pela classe de utilização e estado de carga). Define o coeficiente de majoração Mx (Tabela 10) das cargas permanentes e de serviço.";
            f8400.Children.Add(Linha("Grupo", cbGrupo, 140));
            tbPsi = Campo(Num.Fmt(p.Psi, 2), 80, "Coeficiente dinâmico ψ da carga de serviço (Tabela 5): pontes e pórticos rolantes 1,15 (vL ≤ 0,25 m/s), 1 + 0,6·vL (até 1 m/s) e 1,60 (vL ≥ 1 m/s).");
            f8400.Children.Add(Linha("ψ (carga de serviço)", tbPsi, 140));
            f8400.Children.Add(Nota("As combinações geradas seguem a seção 5.6: Caso I = Mx·(G + ψ·Q); Caso II = Caso I + vento de serviço; Caso III = G + vento máximo. Efeitos horizontais de movimentos, choques e temperatura não são gerados: lance-os como casos próprios."));

            form.Children.Add(UiPlaca.TituloSecao("Combinações", null, 12));
            form.Children.Add(ckComb);

            // coluna da direita: prévia barra a barra
            var dir = new StackPanel { Margin = new Thickness(6, 12, 18, 8) };
            dir.Children.Add(UiPlaca.TituloSecao("Prévia — força do vento em cada barra", null, 0));
            dir.Children.Add(resumo);
            dir.Children.Add(previa);
            notaDir = (TextBlock)Nota("");
            dir.Children.Add(notaDir);

            var corpo = new DockPanel();
            var sc1 = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(sc1, Dock.Left); corpo.Children.Add(sc1);
            corpo.Children.Add(new Border { Width = 1, Background = TemaPlant.BBordaSutil });
            corpo.Children.Add(new ScrollViewer { Content = dir, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            raiz.Children.Add(corpo);
            Content = raiz;

            foreach (var tb in new[] { tbV0, tbS1, tbVServ, tbPsi }) tb.TextChanged += (s, e) => Atualizar();
            foreach (var cb in new[] { cbS1, cbCat, cbClasse, cbS3, cbNorma, cbGrupo }) cb.SelectionChanged += (s, e) => Atualizar();
            ckK.Checked += (s, e) => Atualizar(); ckK.Unchecked += (s, e) => Atualizar();
            montando = false;
            Atualizar();
        }

        static ComboBox Combo(string[] itens, double larg)
        {
            var c = new ComboBox { Width = larg, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4) };
            foreach (string s in itens) c.Items.Add(s);
            return c;
        }

        static FrameworkElement Linha(string rotulo, FrameworkElement campo, double largRot = 110)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var t = T(rotulo, 12, BFraco); t.Width = largRot; t.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(t); sp.Children.Add(campo);
            return sp;
        }

        static UIElement Nota(string s) { var t = T(s, 11.5, TemaPlant.BTexto3); t.Margin = new Thickness(0, 6, 0, 4); t.MaxWidth = 700; t.HorizontalAlignment = HorizontalAlignment.Left; return t; }

        // Lê a janela; devolve null (com a mensagem em "erro") se algum valor estiver fora.
        ParamVento Ler()
        {
            erro.Text = "";
            if (Nbr8400)
            {
                double vs, psi;
                if (!Num.Ler(tbVServ.Text, out vs) || vs < 5 || vs > 40) { erro.Text = "Vento de serviço entre 5 e 40 m/s (a Tabela 7 da norma usa 20 m/s)."; return null; }
                if (!Num.Ler(tbPsi.Text, out psi) || psi < 1 || psi > 2) { erro.Text = "ψ entre 1,00 e 2,00 (Tabela 5: de 1,15 a 1,60 em pontes rolantes)."; return null; }
                return new ParamVento { Norma = "8400", VServ = vs, Grupo = Math.Max(0, cbGrupo.SelectedIndex) + 1, Psi = psi };
            }
            double v0, s1;
            if (!Num.Ler(tbV0.Text, out v0) || v0 < 20 || v0 > 70) { erro.Text = "V0 entre 20 e 70 m/s (Figura 1 da norma vai de 30 a 50 m/s)."; return null; }
            if (cbS1.SelectedIndex == 0) s1 = 1.0;
            else if (cbS1.SelectedIndex == 1) s1 = 0.9;
            else if (!Num.Ler(tbS1.Text, out s1) || s1 < 0.9 || s1 > 2.5) { erro.Text = "S1 de talude ou morro entre 0,90 e 2,50."; return null; }
            return new ParamVento
            {
                V0 = v0, S1 = s1, Categoria = cbCat.SelectedIndex + 1,
                Classe = cbClasse.SelectedIndex <= 0 ? "" : VentoNbr.Classes[cbClasse.SelectedIndex - 1],
                S3 = VentoNbr.S3Grupos[Math.Max(0, cbS3.SelectedIndex)], UsarK = ckK.IsChecked == true
            };
        }

        void Atualizar()
        {
            if (montando) return;
            montando = true;   // mexer nas listas e campos abaixo dispara os eventos de novo
            try { AtualizarTudo(); } finally { montando = false; }
        }

        void Previa8400()
        {
            var p = Ler();
            if (p == null) { resumo.Text = ""; return; }
            double paS = Vento8400.PaServ(p.VServ);
            resumo.Text = "Vento de serviço: vw = " + Num.Fmt(p.VServ, 1) + " m/s → Pa = " + Num.Fmt(paS, 0) + " N/m²   ·   Vento máximo: 800 / 1100 / 1300 N/m² (por altura)   ·   Mx = " + Num.Fmt(Vento8400.MxGrupo(p.Grupo), 2);
            resumo.Margin = new Thickness(0, 0, 0, 8);
            var serv = Vento8400.Calcular(m, p, false); var maxi = Vento8400.Calcular(m, p, true);
            string[] cab = { "Barra", "z (m)", "Seção", "C", "c (mm)", "sen²α", "Pa serv. (N/m²)", "w serv. (kN/m)", "Pa máx. (N/m²)", "w máx. (kN/m)" };
            double[] larguras = { 50, 56, 130, 46, 60, 54, 90, 90, 90, 90 };
            var g = new Grid();
            for (int i = 0; i < cab.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(larguras[i]) });
            int linha = 0;
            void Cel(string s, int col, bool negrito = false, Brush cor = null, string dica = null)
            {
                var t = T(s, 11.5, cor ?? (negrito ? BTexto : BFraco), negrito ? FontWeights.SemiBold : FontWeights.Normal);
                t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 3, 6, 3); t.ToolTip = dica ?? s;
                Grid.SetRow(t, linha); Grid.SetColumn(t, col); g.Children.Add(t);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < cab.Length; i++) Cel(cab[i], i, true);
            for (int k = 0; k < serv.Count; k++)
            {
                var v = serv[k]; var x = maxi[k];
                linha++;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool zero = Math.Abs(v.W) < 1e-9;
                Brush cor = zero ? TemaPlant.BTexto3 : null;
                Cel(v.B.Id.ToString(), 0, false, cor);
                Cel(Num.Fmt(v.Z, 2), 1, false, cor);
                Cel(v.B.Secao, 2, false, cor, v.B.Secao + " — " + v.Forma + (v.B.EixoFraco ? " (deitado)" : ""));
                Cel(Num.Fmt(v.Cf, 2), 3, false, cor, v.Fonte);
                Cel(Num.Fmt(v.C * 1000, 0), 4, false, cor, "Dimensão vista pelo vento: " + Num.Fmt(v.C * 1000, 0) + " mm");
                Cel(Num.Fmt(v.Sen2, 2), 5, false, cor, zero ? "Barra paralela ao vento: sem força no plano." : "sen²α do ângulo entre o vento (horizontal) e o eixo da barra");
                Cel(Num.Fmt(v.Q * 1000, 0), 6, false, cor);
                Cel(zero ? "—" : Num.Fmt(v.W, 3), 7, !zero, zero ? TemaPlant.BTexto3 : TemaPlant.BAcento);
                Cel(Num.Fmt(x.Q * 1000, 0), 8, false, cor, x.Fonte);
                Cel(zero ? "—" : Num.Fmt(x.W, 3), 9, !zero, zero ? TemaPlant.BTexto3 : TemaPlant.BAcento, x.Fonte);
            }
            previa.Children.Add(g);
            if (serv.Count == 0) previa.Children.Add(T("Nenhuma barra com seção reconhecida.", 12, BFraco));
        }

        void AtualizarTudo()
        {
            bool n8400 = Nbr8400;
            f6123.Visibility = n8400 ? Visibility.Collapsed : Visibility.Visible;
            f8400.Visibility = n8400 ? Visibility.Visible : Visibility.Collapsed;
            Title = n8400 ? "Carga de vento — ABNT NBR 8400:1984" : "Carga de vento — ABNT NBR 6123:2023";
            if (n8400)
            {
                notaDir.Text = "Vento horizontal no plano do pórtico, nos dois sentidos, em quatro casos: \"" + MainWindow.CasoVentoServDir + "\", \"" + MainWindow.CasoVentoServEsq + "\" (vento limite de serviço) e \"" +
                    MainWindow.CasoVentoMaxDir + "\", \"" + MainWindow.CasoVentoMaxEsq + "\" (vento máximo). Força por metro = C · Pa · c (5.5.4.1, Fw = C·A·Pa), C pela Tabela 8; " +
                    "barra inclinada recebe a componente normal (× sen²α). Sem redução por proteção entre vigas (Tabela 9). O vento sobre a carga suspensa (C = 1, maior superfície) e sobre cabines e equipamentos não entra: lance-o como carga no caso de vento.";
                previa.Children.Clear();
                Previa8400();
                return;
            }
            notaDir.Text = "Vento horizontal no plano do pórtico, nos dois sentidos (casos " + MainWindow.CasoVentoDir + " e " + MainWindow.CasoVentoEsq + "). " +
                "Força por metro = Cf · q · K · c (8.1), com q = 0,613 Vk² e Vk = V0·S1·S2·S3, S2 no ponto mais alto da barra. Barra horizontal não recebe vento no plano; " +
                "barra inclinada recebe a componente normal (× sen²α). Equipamentos, telhas e fechamentos presos à estrutura não entram: lance-os como carga no caso de vento.";
            tbS1.IsEnabled = cbS1.SelectedIndex == 2;
            if (cbS1.SelectedIndex == 0) tbS1.Text = "1,00"; else if (cbS1.SelectedIndex == 1) tbS1.Text = "0,90";
            // texto da classe automática com a dimensão do pórtico
            double z0 = VentoNbr.Terreno(m);
            double alt = m.Nos.Count > 0 ? m.Nos.Max(n => n.Y) - z0 : 0, larg = m.Nos.Count > 0 ? m.Nos.Max(n => n.X) - m.Nos.Min(n => n.X) : 0;
            int cAuto = VentoNbr.ClassePorDimensao(Math.Max(alt, larg));
            int selCl = cbClasse.SelectedIndex;
            cbClasse.Items[0] = "Automática — maior dimensão " + Num.Fmt(Math.Max(alt, larg), 1) + " m → classe " + VentoNbr.Classes[cAuto];
            if (selCl >= 0) cbClasse.SelectedIndex = selCl;

            previa.Children.Clear();
            var p = Ler();
            if (p == null) { resumo.Text = ""; return; }
            int classe = VentoNbr.Classe(m, p);
            double vk10 = p.V0 * p.S1 * VentoNbr.S2(p.Categoria, classe, 10) * p.S3;
            resumo.Text = "A 10 m do terreno: Vk = " + Num.Fmt(vk10, 1) + " m/s · q = " + Num.Fmt(VentoNbr.Q(vk10) * 1000, 0) + " N/m²   (categoria " + VentoNbr.Categorias[p.Categoria - 1] +
                          ", classe " + VentoNbr.Classes[classe] + ")";
            resumo.Margin = new Thickness(0, 0, 0, 8);

            var lista = VentoNbr.Calcular(m, p);
            string[] cab = { "Barra", "z (m)", "S2", "Vk (m/s)", "q (N/m²)", "Seção", "Cf", "c (mm)", "K", "sen²α", "w (kN/m)" };
            double[] larguras = { 50, 56, 50, 66, 66, 150, 46, 60, 46, 54, 70 };
            var g = new Grid();
            for (int i = 0; i < cab.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(larguras[i]) });
            int linha = 0;
            void Cel(string s, int col, bool negrito = false, Brush cor = null, string dica = null)
            {
                var t = T(s, 11.5, cor ?? (negrito ? BTexto : BFraco), negrito ? FontWeights.SemiBold : FontWeights.Normal);
                t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 3, 6, 3); t.ToolTip = dica ?? s;
                Grid.SetRow(t, linha); Grid.SetColumn(t, col); g.Children.Add(t);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < cab.Length; i++) Cel(cab[i], i, true);
            foreach (var v in lista)
            {
                linha++;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool zero = Math.Abs(v.W) < 1e-9;
                Brush cor = zero ? TemaPlant.BTexto3 : null;
                Cel(v.B.Id.ToString(), 0, false, cor);
                Cel(Num.Fmt(v.Z, 2), 1, false, cor);
                Cel(Num.Fmt(v.S2, 3), 2, false, cor);
                Cel(Num.Fmt(v.Vk, 1), 3, false, cor);
                Cel(Num.Fmt(v.Q * 1000, 0), 4, false, cor);
                Cel(v.B.Secao, 5, false, cor, v.B.Secao + " — " + v.Forma + (v.B.EixoFraco ? " (deitado)" : ""));
                Cel(Num.Fmt(v.Cf, 2), 6, false, cor, v.Fonte);
                Cel(Num.Fmt(v.C * 1000, 0), 7, false, cor, "Dimensão de referência c da seção: " + Num.Fmt(v.C * 1000, 0) + " mm");
                Cel(Num.Fmt(v.K, 2), 8, false, cor, p.UsarK ? "Tabela 28 com l/c = " + Num.Fmt(v.LC, 1) + " (dobrado se a barra toca um apoio)" : "K desligado (1,0)");
                Cel(Num.Fmt(v.Sen2, 2), 9, false, cor, zero ? "Barra paralela ao vento: sem força no plano." : "sen²α do ângulo entre o vento (horizontal) e o eixo da barra");
                Cel(zero ? "—" : Num.Fmt(v.W, 3), 10, !zero, zero ? TemaPlant.BTexto3 : TemaPlant.BAcento);
            }
            previa.Children.Add(g);
            if (lista.Count == 0) previa.Children.Add(T("Nenhuma barra com seção reconhecida.", 12, BFraco));
        }

        void Aplicar()
        {
            var p = Ler();
            if (p == null) return;
            Param = p; GerarComb = ckComb.IsChecked == true;
            DialogResult = true;
        }
    }
}
