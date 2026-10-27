// W7 (docs/v3/W7-ABA.md): biblioteca e projetista de ligações (JanelaLigacaoProjeto, um UserControl) abrem como ABA na barra
// de documentos, depois das abas de modelo, cobrindo a área de trabalho (painéis e vista); faixa e barra de status continuam.
// Todas as abas ficam vivas no HostLigacoes (só a ativa visível): trocar de aba não dispara Loaded/Unloaded nem perde estado.
// Clicar na aba do modelo (ou em Início) volta ao modelo; ✕, botão do meio, Fechar, Esc ou Ctrl+F4 fecham a aba.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovoFtool
{
    public partial class MainWindow
    {
        readonly List<JanelaLigacaoProjeto> ligAbas = new List<JanelaLigacaoProjeto>();
        internal JanelaLigacaoProjeto LigacaoAtiva { get; private set; }
        internal IReadOnlyList<JanelaLigacaoProjeto> LigacoesAbertas => ligAbas;

        // j = aba a mostrar (entra na barra se for nova); null = volta ao modelo
        internal void MostrarLigacao(JanelaLigacaoProjeto j)
        {
            if (j != null && !ligAbas.Contains(j))
            {
                ligAbas.Add(j); HostLigacoes.Children.Add(j);
                j.Fechou = () => FecharLigacao(j);
                j.AbaMudou = RedesenharAbasLig;
            }
            LigacaoAtiva = j;
            foreach (var x in ligAbas) x.Visibility = x == j ? Visibility.Visible : Visibility.Collapsed;
            HostLigacoes.Visibility = j == null ? Visibility.Collapsed : Visibility.Visible;
            RedesenharAbas(); RedesenharAbasLig();
            if (j != null) { HostLigacoes.UpdateLayout(); j.Focus(); }   // Esc e Tab já valem na aba
        }

        void FecharLigacao(JanelaLigacaoProjeto j)
        {
            int i = ligAbas.IndexOf(j); if (i < 0) return;
            ligAbas.RemoveAt(i); HostLigacoes.Children.Remove(j);
            if (LigacaoAtiva == j) MostrarLigacao(ligAbas.Count > 0 ? ligAbas[Math.Min(i, ligAbas.Count - 1)] : null);
            else RedesenharAbasLig();
        }

        // mesmo desenho das abas de modelo (Documentos.cs RedesenharAbas)
        void RedesenharAbasLig()
        {
            if (AbasLig == null) return;
            AbasLig.Children.Clear();
            foreach (var j in ligAbas)
            {
                var k = j; bool at = j == LigacaoAtiva;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = j.NomeAba, Foreground = at ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeight = at ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380, TextTrimming = TextTrimming.CharacterEllipsis });   // grupo de nome longo: o nome inteiro fica na dica
                var x = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto2, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = "Fecha esta aba de ligação (Esc ou Ctrl+F4)." };
                x.Click += (s, e) => { k.Close(); e.Handled = true; };
                sp.Children.Add(x);
                var aba = new Border { Background = at ? TemaPlant.BSup1 : TemaPlant.BSup0, Padding = new Thickness(12, 0, 6, 0), Margin = new Thickness(0, 0, 1, 0), Child = sp, Cursor = Cursors.Hand, ToolTip = j.NomeAba + " — clique para alternar para esta ligação; clique com o botão do meio para fechar." };
                aba.MouseLeftButtonUp += (s, e) => MostrarLigacao(k);
                aba.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) k.Close(); };
                AbasLig.Children.Add(aba);
            }
        }

        // teclas da janela (Acoes.cs Tecla) com uma aba de ligação na frente: true = a janela não trata (a tecla segue para a aba).
        // Só Ctrl+K/S/O/N valem; Ctrl+F4 fecha a aba.
        bool TeclaLigacao(Key k, bool ctrl, KeyEventArgs e)
        {
            if (LigacaoAtiva == null) return false;
            if (ctrl && k == Key.F4) { LigacaoAtiva.Close(); e.Handled = true; return true; }
            if (ctrl && (k == Key.O || k == Key.N)) { MostrarLigacao(null); return false; }   // abre/cria modelo: volta ao modelo antes
            return !(ctrl && (k == Key.K || k == Key.S));
        }
    }
}
