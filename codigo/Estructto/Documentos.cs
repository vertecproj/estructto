// Varios modelos abertos ao mesmo tempo, um por aba na barra de arquivos (como os desenhos no AutoCAD).
// So o documento ativo fica "vivo" na vista; os outros guardam modelo, arquivo, estado e pilhas de desfazer.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        class Documento
        {
            public Modelo M; public string Arquivo; public bool Alterado; public string Atual;
            public DocumentoEspacial Esp;   // modelo espacial (DocumentoEspacial.cs); null = documento 2D
            public string[] Desfazer = new string[0], Refazer = new string[0];
            public string Nome { get { return Esp != null ? Esp.Nome : Arquivo == null ? "Sem título" : System.IO.Path.GetFileName(Arquivo); } }
        }

        readonly List<Documento> docs = new List<Documento> { new Documento() };
        int docAtivo;
        bool trocandoDoc;

        // copia o estado do documento ativo para a lista
        void GuardarDoc()
        {
            var d = docs[docAtivo];
            d.M = Vista.M; d.Arquivo = arquivo; d.Alterado = alterado; d.Atual = atual;
            d.Desfazer = desfazer.ToArray(); d.Refazer = refazer.ToArray();   // topo primeiro
        }

        void CarregarDoc(int i)
        {
            docAtivo = i;
            var d = docs[i];
            trocandoDoc = true;
            NovoModelo(d.M ?? new Modelo(), d.Arquivo);
            trocandoDoc = false;
            alterado = d.Alterado;
            if (d.Atual != null) atual = d.Atual;
            desfazer.Clear(); foreach (string x in d.Desfazer.Reverse()) desfazer.Push(x);
            refazer.Clear(); foreach (string x in d.Refazer.Reverse()) refazer.Push(x);
            AtualizarTitulo(); Atualizar();
            AplicarModoEspacial();
        }

        void TrocarDoc(int i)
        {
            if (i == docAtivo || i < 0 || i >= docs.Count) return;
            GuardarDoc();
            CarregarDoc(i);
            Log("Modelo ativo: " + docs[i].Nome + ".");
        }

        bool DocVazio { get { return Esp == null && M.Barras.Count == 0 && M.Nos.Count == 0 && arquivo == null && !alterado; } }

        // aba nova (ou reaproveita a atual se ela estiver vazia) antes de carregar outro modelo
        void PrepararDocNovo()
        {
            if (DocVazio) return;
            GuardarDoc();
            docs.Add(new Documento());
            docAtivo = docs.Count - 1;
        }

        void FecharDoc(int i)
        {
            if (i != docAtivo) TrocarDoc(i);
            if (!PodeDescartar()) return;
            docs[docAtivo].Esp?.Descartar();
            docs.RemoveAt(docAtivo);
            if (docs.Count == 0) docs.Add(new Documento());
            CarregarDoc(Math.Min(docAtivo, docs.Count - 1));
        }

        // ao fechar o programa: pergunta de cada modelo alterado
        bool PodeFecharTudo()
        {
            GuardarDoc();
            for (int i = 0; i < docs.Count; i++)
            {
                if (!docs[i].Alterado) continue;
                if (i != docAtivo) { GuardarDoc(); CarregarDoc(i); }
                if (!PodeDescartar()) return false;
            }
            return true;
        }

        void RedesenharAbas()
        {
            if (AbasDocs == null || trocandoDoc) return;
            if (docs.Count > 0 && docAtivo < docs.Count) GuardarDoc();
            AbasDocs.Children.Clear();
            for (int i = 0; i < docs.Count; i++)
            {
                int k = i;
                bool at = i == docAtivo;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = docs[i].Nome + (docs[i].Alterado ? " *" : ""), Foreground = at ? TemaPlant.BTexto1 : TemaPlant.BTexto2, FontWeight = at ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
                var x = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TemaPlant.BTexto2, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = "Fecha esta aba de modelo (pergunta se deseja salvar caso haja alterações pendentes)." };
                x.Click += (s, e) => { FecharDoc(k); e.Handled = true; };
                sp.Children.Add(x);
                var aba = new Border { Background = at ? TemaPlant.BSup1 : TemaPlant.BSup0, Padding = new Thickness(12, 0, 6, 0), Margin = new Thickness(0, 0, 1, 0), Child = sp, Cursor = Cursors.Hand, ToolTip = (docs[i].Arquivo ?? "Modelo sem título") + " — clique para alternar para este modelo; clique com o botão do meio para fechar." };
                aba.MouseLeftButtonUp += (s, e) => TrocarDoc(k);
                aba.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) FecharDoc(k); };   // roda do mouse fecha, como no navegador
                AbasDocs.Children.Add(aba);
            }
        }
    }
}
