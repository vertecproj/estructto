// Pecas compartilhadas pelos dialogos e tabelas: colunas com barra de capacidade e chip de situacao, estados vazios, cabecalho de dialogo.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ui = NovoFtool.UiPlaca;

namespace NovoFtool
{
    public static class UiTabela
    {
        public static DataTemplate Modelo(string xaml)
        {
            return (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + xaml + "</DataTemplate>");
        }

        static Style estiloCabDir;
        public static Style CabecalhoDireita
        {
            get
            {
                if (estiloCabDir == null)
                {
                    estiloCabDir = new Style(typeof(DataGridColumnHeader), Application.Current.TryFindResource(typeof(DataGridColumnHeader)) as Style);
                    estiloCabDir.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
                }
                return estiloCabDir;
            }
        }

        static Style estiloNumDir;
        public static Style NumeroDireita
        {
            get
            {
                if (estiloNumDir == null)
                {
                    estiloNumDir = new Style(typeof(TextBlock));
                    estiloNumDir.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                    estiloNumDir.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                }
                return estiloNumDir;
            }
        }

        // texto simples; numeros ficam a direita (cabecalho tambem)
        public static DataGridTextColumn Texto(string titulo, string prop, double larg = double.NaN, bool numero = false, string opacidade = null)
        {
            var c = new DataGridTextColumn { Header = titulo, Binding = new System.Windows.Data.Binding(prop) };
            if (!double.IsNaN(larg)) c.Width = larg;
            if (numero) { c.ElementStyle = NumeroDireita; c.HeaderStyle = CabecalhoDireita; }
            return c;
        }

        // barra de capacidade (GUIA 5.6): propriedades esperadas no item: ApNum (%), CorAp (Brush), BarraVis (Visibility), Ap (texto)
        public static DataGridTemplateColumn Capacidade(string titulo, double larg = 150)
        {
            return new DataGridTemplateColumn
            {
                Header = titulo, Width = larg, SortMemberPath = "ApNum", CanUserSort = true,
                CellTemplate = Modelo("<Grid VerticalAlignment=\"Center\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\" MinWidth=\"40\"/><ColumnDefinition Width=\"46\"/></Grid.ColumnDefinitions>" +
                    "<ProgressBar Minimum=\"0\" Maximum=\"100\" Value=\"{Binding ApNum, Mode=OneWay}\" Foreground=\"{Binding CorAp}\" Height=\"6\" Visibility=\"{Binding BarraVis}\"/>" +
                    "<TextBlock Grid.Column=\"1\" Text=\"{Binding Ap}\" TextAlignment=\"Right\" FontWeight=\"SemiBold\"/></Grid>")
            };
        }

        // chip de situacao: SitTxt, SitFg, SitBg, SitBd
        public static DataGridTemplateColumn Chip(string titulo, string ordem, double larg = double.NaN)
        {
            var c = new DataGridTemplateColumn
            {
                Header = titulo, SortMemberPath = ordem, MinWidth = 90,
                CellTemplate = Modelo("<Border HorizontalAlignment=\"Left\" CornerRadius=\"3\" Padding=\"6,1\" BorderThickness=\"1\" Background=\"{Binding SitBg}\" BorderBrush=\"{Binding SitBd}\">" +
                    "<TextBlock Text=\"{Binding SitTxt}\" FontSize=\"11\" FontWeight=\"SemiBold\" Foreground=\"{Binding SitFg}\"/></Border>")
            };
            if (!double.IsNaN(larg)) c.Width = larg;
            return c;
        }

        // nome do perfil + marcador discreto (atual / mais leve): propriedades Perfil e Marca
        public static DataGridTemplateColumn PerfilMarca(string titulo, double larg)
        {
            return new DataGridTemplateColumn
            {
                Header = titulo, Width = larg, SortMemberPath = "Perfil",
                CellTemplate = Modelo("<DockPanel><TextBlock DockPanel.Dock=\"Right\" Text=\"{Binding Marca}\" FontSize=\"10.5\" Margin=\"8,0,0,0\" VerticalAlignment=\"Center\" Foreground=\"{DynamicResource Accent.Highlight}\"/><TextBlock Text=\"{Binding Perfil}\" TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/></DockPanel>")
            };
        }

        // estado vazio centralizado (icone discreto + texto)
        public static FrameworkElement Vazio(string icone, string titulo, string detalhe)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var ic = (FrameworkElement)Icones.Criar(icone, 24); ic.Opacity = 0.55; ic.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) });
            if (!string.IsNullOrEmpty(detalhe)) sp.Children.Add(new TextBlock { Text = detalhe, FontSize = 11.5, Foreground = TemaPlant.BTexto3, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 320 });
            return sp;
        }

        public static string PctTxt(double ap)
        {
            if (double.IsNaN(ap) || double.IsInfinity(ap)) return "—";
            return ap > 9.99 ? ">999%" : (ap * 100).ToString("0") + "%";
        }
    }
}
