// Janela "Ligação" (REVISAO-TELAS 3.4): mesmo esqueleto da Placa — veredito no topo, tipo no segmentado
// (Parafuso, Filete de solda, Chapa simples, Ligação de tração), ① dados e ② esforços à esquerda (campos montados a partir
// de GET /api/ligacoes/tipos) e as verificações com item da NBR 8800 à direita (POST /api/ligacoes/verificar).
// Campos em metro no motor aparecem em mm na tela (convertidos na ida).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static NovoFtool.UiPlaca;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public class JanelaLigacao : JanelaEscura
    {
        static readonly Dictionary<string, string> NomesTipo = new Dictionary<string, string>
        { ["parafuso"] = "Parafuso", ["solda_filete"] = "Filete de solda", ["chapa_simples"] = "Chapa simples", ["ligacao_tracao"] = "Ligação de tração" };

        // rótulos que o motor ainda devolve com o nome do campo
        static readonly Dictionary<string, string> Rotulos = new Dictionary<string, string>
        {
            ["pega"] = "Comprimento de pega", ["t_enchimento"] = "Chapas de enchimento", ["Ft"] = "Tração Ft", ["Fv"] = "Cortante Fv",
            ["F"] = "Força F", ["V"] = "Cortante V", ["N"] = "Tração N"
        };

        readonly List<JsonNode> tipos;
        readonly Func<JsonObject, Task<JsonNode>> verificar;
        readonly Dictionary<string, JsonObject> dados = new Dictionary<string, JsonObject>(), esforcos = new Dictionary<string, JsonObject>();
        readonly Border hostCab = new Border(), hostTipo = new Border(), hostEntradas = new Border(), hostResultado = new Border();
        string tipo;
        JsonNode resultado; string erro;
        bool calculando;

        public JanelaLigacao(JsonNode tiposJson, string tipoInicial, Func<JsonObject, Task<JsonNode>> verificar)
        {
            tipos = Lista(tiposJson).ToList();
            this.verificar = verificar;
            tipo = tipos.Any(t => S(t, "tipo") == tipoInicial) ? tipoInicial : S(tipos.FirstOrDefault(), "tipo");
            foreach (var t in tipos)
            {
                var dd = new JsonObject();
                foreach (var c in Lista(t["campos"])) if (c["padrao"] != null) dd[S(c, "nome")] = c["padrao"].DeepClone();
                dados[S(t, "tipo")] = dd;
                esforcos[S(t, "tipo")] = new JsonObject();
            }
            Moldar(this, "Ligação", 1180, 720);

            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(600) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var esq = new DockPanel { Margin = new Thickness(12, 10, 10, 10) };
            hostTipo.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(hostTipo, Dock.Top); esq.Children.Add(hostTipo);
            esq.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = hostEntradas });
            corpo.Children.Add(esq);
            var dir = new Border { Background = TemaPlant.BSup0, BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(12, 10, 12, 10), Child = hostResultado };
            Grid.SetColumn(dir, 1); corpo.Children.Add(dir);

            var nota = Tx("Ligação isolada pela NBR 8800:2008 (6.2 e 6.3). Resultado do motor; exige conferência do engenheiro (ART).", 11.5, TemaPlant.BTexto3);
            nota.TextTrimming = TextTrimming.CharacterEllipsis;
            Content = Raiz(hostCab, corpo, Rodape(nota,
                B("Fechar", Close, false, "Fecha a janela (Esc)."),
                B("Verificar ligação", Verificar, true, "Envia os dados ao motor e verifica pela NBR 8800 (Enter).")));
            Montar();
        }

        // API das capturas: preenche e mostra um resultado sem chamar o motor
        public void Preencher(string tipoLig, JsonObject d, JsonObject esf, JsonNode res)
        {
            tipo = tipoLig;
            foreach (var kv in d) dados[tipo][kv.Key] = kv.Value?.DeepClone();
            foreach (var kv in esf) esforcos[tipo][kv.Key] = kv.Value?.DeepClone();
            resultado = res; erro = null;
            Montar();
        }

        async void Verificar()
        {
            try
            {
                if (calculando || verificar == null) return;
                calculando = true;
                try
                {
                    var corpo = new JsonObject { ["tipo"] = tipo, ["dados"] = dados[tipo].DeepClone(), ["esforcos"] = esforcos[tipo].DeepClone() };
                    resultado = await verificar(corpo); erro = null;
                }
                catch (Exception ex) { resultado = null; erro = ComandosAnalise.MensagemMotor(ex); }
                finally { calculando = false; }
                Montar();
            }
            catch (Exception ex) { Caixa.Erro(this, Title, ex.Message); }   // async void: exceção solta derruba o programa
        }

        void Mudou() { resultado = null; erro = null; Montar(); }

        void Montar()
        {
            var t = tipos.FirstOrDefault(x => S(x, "tipo") == tipo);
            // tipo
            hostTipo.Child = Segmentado(tipos.Select(x =>
            {
                string tt = S(x, "tipo");
                return (NomesTipo.TryGetValue(tt, out var n) ? n : tt, tt == tipo, (Action)(() => { tipo = tt; Mudou(); }), S(x, "descricao"));
            }).ToList());

            // entradas
            var sp = new StackPanel();
            var desc = Tx(S(t, "descricao"), 11.5, TemaPlant.BTexto3, null, true); desc.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(desc);
            sp.Children.Add(Cartao("① Dados da ligação", Campos(Lista(t?["campos"]).ToList(), dados[tipo]), null));
            sp.Children.Add(Cartao("② Esforços de cálculo", Campos(Lista(t?["esforcos"]).ToList(), esforcos[tipo]), null));
            hostEntradas.Child = sp;

            // resultado + cabeçalho
            var itens = new List<JanelaVerificarBarraMotor.Item>();
            foreach (var v in Lista(resultado?["verificacoes"]))
            {
                string un = S(v, "unidade");
                double s = D(v, "solicitante"), r = D(v, "resistente");
                itens.Add(new JanelaVerificarBarraMotor.Item
                {
                    Criterio = S(v, "nome"), Taxa = D(v, "taxa"), NomeItem = "8800 " + S(v, "item"),
                    Valor = un == "" ? "Σ = " + F(s, 3) + "  ≤  " + F(r, 2) : "Sd = " + F(s, 2) + "  ≤  Rd = " + F(r, 2, un)
                });
            }
            itens = itens.OrderByDescending(x => x.Taxa).ToList();
            if (resultado == null)
            {
                hostCab.Child = erro != null
                    ? Cabecalho(Nivel.Erro, "✕", "Não foi possível verificar", erro)
                    : Cabecalho(Nivel.Neutro, "–", "Sem verificação", "Preencha os dados e os esforços e clique em Verificar ligação.");
                hostResultado.Child = Vazio("parafuso", erro != null ? "Corrija os dados" : "Nenhuma verificação executada", erro ?? "Os campos sem valor são obrigatórios.");
                return;
            }
            double tx = D(resultado, "taxa_max");
            bool ok = Bool(resultado, "ok");
            var nv = NivelAp(tx, ok);
            hostCab.Child = Cabecalho(nv, nv == Nivel.Erro ? "✕" : nv == Nivel.Alerta ? "!" : "✓",
                (nv == Nivel.Erro ? "Não passa — " : "Passa · ") + "usa " + Pct(tx) + " em " + S(resultado, "criterio"),
                (NomesTipo.TryGetValue(tipo, out var nt) ? nt : tipo) + " · determinante: " + S(resultado, "item") + " · " + itens.Count + " verificações");
            var lista = new StackPanel();
            foreach (var it in itens) lista.Children.Add(JanelaVerificarBarraMotor.LinhaCriterio(it));
            foreach (var a in Lista(resultado["avisos"]))
            {
                var av = Tx("⚠  " + a, 11.5, TemaPlant.BAlertaClaro, null, true); av.Margin = new Thickness(0, 4, 0, 0);
                lista.Children.Add(av);
            }
            hostResultado.Child = new DockPanel { Children = { TopoSecao("Verificações"), new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lista } } };
        }

        static FrameworkElement TopoSecao(string t) { var f = TituloSecao(t, null, 0); DockPanel.SetDock(f, Dock.Top); return f; }

        // Campos em grade 2 × (rótulo | controle): opções → lista; verdadeiro/falso → Sim/Não; número → caixa com unidade.
        FrameworkElement Campos(List<JsonNode> campos, JsonObject alvo)
        {
            var g = new Grid();
            foreach (var w in new[] { 146.0, 122, 12, 146, 122 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            for (int i = 0; i < campos.Count; i++)
            {
                var c = campos[i];
                string nome = S(c, "nome"), un = S(c, "unidade");
                int lin = i / 2, col = (i % 2) * 3;
                while (g.RowDefinitions.Count <= lin) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
                string rot = S(c, "rotulo");
                if (rot == "" || rot == nome) rot = Rotulos.TryGetValue(nome, out var r2) ? r2 : nome;
                var l = Tx(rot, 12, TemaPlant.BTexto2); l.TextTrimming = TextTrimming.CharacterEllipsis; l.Margin = new Thickness(0, 0, 8, 0);
                Dica(l, rot + (S(c, "descricao") != "" ? "\n" + S(c, "descricao") : ""));
                Grid.SetRow(l, lin); Grid.SetColumn(l, col); g.Children.Add(l);
                FrameworkElement ctl;
                var atual = alvo[nome];
                if (c["opcoes"] is JsonArray op)
                    ctl = Combo(op.Select(x => x.ToString()), atual?.ToString(), x => { alvo[nome] = x; Mudou(); }, 118, S(c, "descricao"));
                else if (c["padrao"] is JsonValue pv && pv.TryGetValue(out bool _))
                {
                    bool v = Bool(alvo, nome);
                    ctl = Segmentado(new List<(string, bool, Action, string)>
                    {
                        ("Sim", v, () => { alvo[nome] = true; Mudou(); }, null), ("Não", !v, () => { alvo[nome] = false; Mudou(); }, null)
                    }, 26, 11.5);
                }
                else
                {
                    bool metro = un == "m";
                    double v = D(atual);
                    string txt = double.IsNaN(v) ? "" : metro ? Portico.Num.Fmt(v * 1000, Math.Abs(v * 1000 - Math.Round(v * 1000)) < 1e-6 ? 0 : 1) : Portico.Num.Fmt(v, Math.Abs(v - Math.Round(v)) < 1e-9 ? 0 : 2);
                    ctl = CaixaNum(txt, metro ? "mm" : un, 118, x => { alvo[nome] = metro ? x / 1000 : x; Mudou(); }, S(c, "descricao"));
                    if (double.IsNaN(v)) ((Border)ctl).BorderBrush = TemaPlant.BAlertaBorda;
                }
                ctl.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetRow(ctl, lin); Grid.SetColumn(ctl, col + 1); g.Children.Add(ctl);
            }
            return g;
        }
    }
}
