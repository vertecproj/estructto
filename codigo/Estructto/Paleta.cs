// Paleta de comandos (Ctrl+K): busca difusa em todos os comandos da faixa/linha de comando, perfis da biblioteca e objetos do modelo.
// Registro unico: cada botao da faixa se registra aqui (MainWindow.Reg); comandos so de teclado entram em RegistrarExtras().
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public enum TipoItem { Comando, Perfil, Objeto }

    public sealed class ItemPaleta
    {
        public TipoItem Tipo;
        public string Nome;        // texto mostrado e buscado
        public string Detalhe;     // dica curta (cinza)
        public string Atalho;      // mostrado a direita
        public string Icone;       // nome em Icones
        public string Cmd;         // comando digitavel equivalente (ex.: "MOMENTO")
        public string Sinonimos;   // palavras extras para a busca
        public string Chave;       // identidade (recentes)
        public Action Acao;
        public double Pontos;
    }

    public static class BuscaPaleta
    {
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        static bool Subseq(string alvo, string t)
        {
            int j = 0;
            foreach (char c in alvo) if (j < t.Length && c == t[j]) j++;
            return j == t.Length;
        }

        // Pontuacao de um item para os termos (todos precisam casar). < 0 = nao casa.
        public static double Pontuar(ItemPaleta it, string[] termos)
        {
            string nome = Norm(it.Nome), sin = Norm(it.Sinonimos), det = Norm(it.Detalhe), cmd = Norm(it.Cmd);
            string[] palavras = nome.Split(new[] { ' ', '-', '/', '(', ')', '.', ',', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            double total = 0;
            foreach (string t in termos)
            {
                double p = -1;
                if (nome == t || cmd == t) p = 130;
                else if (nome.StartsWith(t)) p = 115;
                else if (palavras.Any(w => w.StartsWith(t))) p = 100;
                else if (cmd.StartsWith(t)) p = 95;
                else if (nome.Contains(t)) p = 70;
                else if (sin.Split(' ').Any(w => w.StartsWith(t))) p = 60;
                else if (t.Length >= 3 && Subseq(nome, t)) p = 40;
                else if (t.Length >= 3 && det.Contains(t)) p = 20;
                if (p < 0) return -1;
                total += p;
            }
            return total / termos.Length - nome.Length * 0.05;
        }

        public static string[] Termos(string q) { return Norm(q).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); }
    }

    public partial class MainWindow
    {
        readonly List<ItemPaleta> cmds = new List<ItemPaleta>();
        bool extrasRegistrados;
        List<string> recentesCmd;

        // Mapa botao da faixa -> comando digitavel (para a lista ficar completa e a linha de comando ter o mesmo vocabulario).
        static readonly Dictionary<string, string> CmdDe = new Dictionary<string, string>
        {
            ["Barra"] = "BARRA", ["Nó"] = "NO", ["Medir"] = "MEDIR", ["Selecionar"] = "SEL", ["Apagar"] = "APAGAR", ["Dividir barra"] = "DIVIDIR",
            ["Enquadrar"] = "ZE", ["Desfazer"] = "DESFAZER", ["Refazer"] = "REFAZER", ["Calcular"] = "CALC", ["Novo"] = "NOVO", ["Abrir"] = "ABRIR",
            ["Salvar"] = "SALVAR", ["Unir trechos"] = "UNIR", ["Tirar trecho"] = "TIRAR", ["Separar tudo"] = "SEPARAR", ["Selecionar a barra"] = "SELMEMBRO",
            ["Deformada"] = "DEF", ["Normal"] = "NORMAL", ["Cortante"] = "CORTANTE", ["Momento"] = "MOMENTO",
            ["Mapa de aproveitamento"] = "CALOR", ["Mover"] = "MOVER", ["Copiar"] = "COPIAR", ["Espelhar"] = "ESPELHAR", ["Reações"] = "REAC",
            ["Engaste"] = "ENGASTE", ["Fixo"] = "FIXO", ["Móvel"] = "MOVEL", ["Rótula"] = "ROTULA", ["Aplicar no nó"] = "CARGA", ["Gerar estrutura"] = "ASSISTENTE",
            ["Tabelas"] = "TABELAS", ["Exportar CSV"] = "CSV", ["Atalhos e ajuda"] = "AJUDA", ["Relatório"] = "RELATORIO", ["Dimensionar"] = "DIMENSIONAR",
            ["Verificar barra"] = "COMPARAR", ["Placa de base"] = "PLACA"
        };

        static readonly Dictionary<string, string> SinDe = new Dictionary<string, string>
        {
            ["Barra"] = "linha elemento viga pilar desenhar line", ["Nó"] = "ponto node inserir", ["Medir"] = "distancia cota dist",
            ["Selecionar"] = "escolher cursor", ["Apagar"] = "excluir deletar remover erase", ["Calcular"] = "resolver analisar rodar processar",
            ["Momento"] = "fletor diagrama mom", ["Cortante"] = "esforco cortante diagrama v shear", ["Normal"] = "axial forca diagrama tracao compressao",
            ["Deformada"] = "deslocamento flecha diagrama", ["Enquadrar"] = "zoom extents tudo ajustar",
            ["Reações"] = "apoio reacao", ["Relatório"] = "memorial pdf imprimir", ["Exportar CSV"] = "excel planilha tabela exportar",
            ["Desfazer"] = "undo voltar", ["Refazer"] = "redo", ["Novo"] = "criar modelo em branco", ["Abrir"] = "arquivo carregar",
            ["Salvar"] = "gravar arquivo", ["Engaste"] = "apoio fixo travado", ["Fixo"] = "apoio rotulado segundo genero", ["Móvel"] = "apoio rolete primeiro genero",
            ["Dimensionar"] = "otimizar perfil escolher", ["Placa de base"] = "chumbador base pilar", ["Gerar estrutura"] = "assistente portico trelica galpao viga",
            ["Mapa de aproveitamento"] = "verificacao verificar mapa de calor cores norma passa heatmap", ["Dividir barra"] = "partir cortar", ["Mover"] = "deslocar move", ["Copiar"] = "duplicar copy",
            ["Espelhar"] = "mirror simetria", ["Verificar barra"] = "comparar perfis alternativos", ["Tabelas"] = "nos barras dados",
            ["Vento"] = "nbr 6123 v0 s1 s2 s3 pressao", ["Sismo"] = "nbr 15421 terremoto zona sismica", ["Pavimentos"] = "andares diafragma cota",
            ["Combinações"] = "nbr 8681 elu els gama psi", ["Modos de vibração"] = "modal frequencia periodo massa", ["Estabilidade"] = "gama z gamaz deslocamento limite",
            ["Verificação de barras"] = "nbr 8800 lista todas taxa", ["Catálogo de perfis"] = "perfil propriedades w hp", ["Importar CYPE"] = "comparar cype ed3 validar",
            ["Gerenciar ligações"] = "grupos ligacoes iguais pior dimensionar aplicar agrupar", ["Parafusos"] = "ligacao parafusada", ["Filete de solda"] = "ligacao soldada", ["Chapa simples"] = "shear tab ligacao", ["Ligação de tração"] = "area liquida tracionada"
        };

        void Reg(string icone, string texto, string atalho, string dica, Action acao)
        {
            if (string.IsNullOrEmpty(texto) || cmds.Any(c => c.Chave == texto)) return;
            string cmd; CmdDe.TryGetValue(texto, out cmd);
            string sin; SinDe.TryGetValue(texto, out sin);
            cmds.Add(new ItemPaleta
            {
                Tipo = TipoItem.Comando, Nome = texto, Icone = icone, Atalho = atalho, Detalhe = dica, Cmd = cmd, Sinonimos = sin, Chave = texto, Acao = acao
            });
        }

        void Extra(string nome, string cmd, string icone, string atalho, string sin, string dica, Action acao)
        {
            if (cmds.Any(c => c.Chave == nome)) return;
            cmds.Add(new ItemPaleta { Tipo = TipoItem.Comando, Nome = nome, Cmd = cmd, Icone = icone, Atalho = atalho, Sinonimos = sin, Detalhe = dica, Chave = nome, Acao = acao });
        }

        // Comandos que a faixa nao tem (so teclado/linha de comando). "Preenche" abre a linha de comando para digitar os argumentos.
        void RegistrarExtras()
        {
            if (extrasRegistrados) return;
            extrasRegistrados = true;
            RegistrarExportar();   // Exportar.cs
            Action<string> run = c => Executar(c);
            Action<string> preenche = c => { Comando.Focus(); Comando.Text = c + " "; Comando.CaretIndex = Comando.Text.Length; };
            Extra("Selecionar tudo", "SELTODAS", "sel", "Ctrl+A", "todas barras", "Seleciona todas as barras.", () => run("SELTODAS"));
            Extra("Selecionar todos os nós", "SELNOS", "no", null, "todos nos", "Seleciona todos os nós.", () => run("SELNOS"));
            Extra("Selecionar nós da base", "SELBASE", "no", null, "pe apoios base", "Seleciona os nós da cota mais baixa.", () => run("SELBASE"));
            Extra("Histórico de ações", null, "info", "F2", "log mensagens", "Mostra as últimas mensagens do programa.", MostrarHistorico);
            Extra("Alternar grade", "GRADE", "grade", "F7", "malha mostrar ocultar", "Liga/desliga a grade de desenho.", () => run("GRADE"));
            Extra("Alternar ortogonal", "ORTO", "ortho", "F8", "ortho horizontal vertical", "Força o desenho na horizontal/vertical.", () => run("ORTO"));
            Extra("Alternar polar", "POLAR", "polar", "F10", "angulo guia", "Guia de ângulos ao desenhar.", () => run("POLAR"));
            Extra("Alternar snap na grade", null, "snap", "F9", "atrair grade", "Encaixa os pontos na grade.", () => AlternarStatus(stAtrair, v => M.AtrairGrade = v, "SNAP na grade"));
            Extra("Alternar atração a objetos", null, "objsnap", "F3", "osnap nos pontos medios", "Encaixa em nós, meios e interseções.", () => AlternarStatus(stObj, v => Vista.AtrairObjetos = v, "Atração a objetos"));
            Extra("Alternar rastreamento", null, "rastro", "F11", "tracking guias", "Guias de rastreamento a partir dos pontos.", () => AlternarStatus(stRastro, v => { Vista.Rastrear = v; if (!v) Vista.LimparRastreio(); }, "Rastreamento"));
            Extra("Carga distribuída (QD)", "QD", "cargadist", "QD qy", "uniforme kn/m", "Aplica qy [qx] nas barras selecionadas.", () => preenche("QD"));
            Extra("Força no nó (FN)", "FN", "cargano", "FN fx fy", "carga concentrada", "Aplica fx fy [mz] nos nós selecionados.", () => preenche("FN"));
            Extra("Casos de carga", "CASOS", "tabela", null, "permanente acidental vento", "Abre a janela de casos de carga e combinações.", () => run("CASOS"));
            Extra("Ativar caso de carga", "CASO", "tabela", "CASO n", "caso ativo", "Torna ativo o caso de carga de número n.", () => preenche("CASO"));
            Extra("Novo caso de carga", "NOVOCASO", "mais", "NOVOCASO tipo", "criar adicionar", "Cria um caso G, Q, V ou E.", () => preenche("NOVOCASO"));
            Extra("Gerar combinações NBR", "GERARCOMB", "calc", null, "nbr 8681 elu els", "Gera as combinações NBR 8681/8800.", () => run("GERARCOMB"));
            Extra("Resultado a exibir", "RESULTADO", "valores", "RESULTADO ...", "envoltoria elu els comb", "Escolhe caso, combinação ou envoltória mostrada.", () => preenche("RESULTADO"));
            Extra("Tirar trecho da barra contínua", "TIRAR", "tirar", null, "membro remover trecho", "Tira os trechos selecionados da barra contínua.", () => run("TIRAR"));
            Extra("Selecionar a barra contínua", "SELMEMBRO", "selmembro", null, "membro trechos", "Seleciona todos os trechos da barra contínua.", () => run("SELMEMBRO"));
            Extra("Ir para aba da faixa", "ABA", "painel", "ABA nome", "ribbon", "Muda a aba ativa da faixa de opções.", () => preenche("ABA"));
            Extra("Ajuda: lista de comandos", null, "ajuda", "F1", "atalhos como usar", "Abre a ajuda com atalhos.", MostrarAjuda);
        }

        // Todos os comandos digitaveis precisam existir na paleta (verificado no modo --capturas).
        internal IEnumerable<string> ComandosSemItem()
        {
            RegistrarExtras();
            var tem = new HashSet<string>(cmds.Where(c => c.Cmd != null).Select(c => c.Cmd));
            return Comandos.Where(c => c != "DI" && c != "DIST" && !tem.Contains(c)).ToList();
        }

        List<string> Recentes()
        {
            if (recentesCmd == null) recentesCmd = Config.RecentesComandos();
            return recentesCmd;
        }

        internal bool RecentesDe(ItemPaleta it) { return Recentes().Contains(it.Chave); }

        internal List<ItemPaleta> ConsultarPaleta(string q)
        {
            RegistrarExtras();
            var res = new List<ItemPaleta>();
            q = (q ?? "").Trim();
            if (q == "")
            {
                foreach (string ch in Recentes())
                {
                    var it = cmds.FirstOrDefault(c => c.Chave == ch);
                    if (it != null) res.Add(it);
                }
                foreach (var it in cmds.Where(c => c.Cmd != null && !res.Contains(c)).Take(Math.Max(0, 10 - res.Count))) res.Add(it);
                return res;
            }

            // objetos do modelo: "nó 3", "barra 2", "b2", "n 3"
            var mo = Regex.Match(BuscaPaleta.Norm(q), @"^(n|no|nos|b|barra|barras)\s*(\d*)$");
            var objetos = new List<ItemPaleta>();
            if (mo.Success)
            {
                bool eNo = mo.Groups[1].Value.StartsWith("n");
                string dig = mo.Groups[2].Value;
                if (eNo)
                    foreach (No n in M.Nos.Where(x => dig == "" || x.Id.ToString().StartsWith(dig)).Take(6))
                    {
                        No nn = n;
                        objetos.Add(new ItemPaleta
                        {
                            Tipo = TipoItem.Objeto, Nome = "Nó " + n.Id, Detalhe = "(" + Num.Fmt(n.X, 2) + "; " + Num.Fmt(n.Y, 2) + ") m" + (n.TemApoio ? " · apoio" : ""),
                            Icone = "no", Chave = "no" + n.Id, Acao = () => IrParaObjeto(nn)
                        });
                    }
                else
                    foreach (Barra b in M.Barras.Where(x => dig == "" || x.Id.ToString().StartsWith(dig)).Take(6))
                    {
                        Barra bb = b;
                        objetos.Add(new ItemPaleta
                        {
                            Tipo = TipoItem.Objeto, Nome = "Barra " + b.Id, Detalhe = b.Secao + " · nós " + b.N1 + "–" + b.N2,
                            Icone = "barra", Chave = "barra" + b.Id, Acao = () => IrParaObjeto(bb)
                        });
                    }
                if (dig != "") res.AddRange(objetos);
            }

            string[] termos = BuscaPaleta.Termos(q);
            var achados = new List<ItemPaleta>();
            foreach (var it in cmds)
            {
                double p = BuscaPaleta.Pontuar(it, termos);
                if (p >= 0) { it.Pontos = p + (Recentes().Contains(it.Chave) ? 8 : 0); achados.Add(it); }
            }
            res.AddRange(achados.OrderByDescending(a => a.Pontos).ThenBy(a => a.Nome).Take(8));
            if (mo.Success && mo.Groups[2].Value == "") res.AddRange(objetos);

            // perfis da biblioteca: "W 200", "tubo 100"
            if (q.Length >= 2 && termos.Any(t => t.Any(char.IsDigit) || t.Length >= 3))
            {
                int n = 0;
                foreach (Secao s in Biblioteca.Perfis)
                {
                    string alvo = BuscaPaleta.Norm(s.Nome + " " + s.Familia);
                    if (termos.All(t => alvo.Contains(t)))
                    {
                        string nome = s.Nome;
                        res.Add(new ItemPaleta
                        {
                            Tipo = TipoItem.Perfil, Nome = s.Nome, Detalhe = "perfil · " + s.Familia + " — aplicar", Icone = "pincel", Chave = "perfil:" + s.Nome,
                            Acao = () => { cbSecao.Text = nome; AplicarSecao(false); }
                        });
                        if (++n >= 5) break;
                    }
                }
            }
            return res;
        }

        void IrParaObjeto(object o)
        {
            Vista.Selecionar(new[] { o });
            var pts = new List<Point>();
            var n = o as No;
            if (n != null) pts.Add(new Point(n.X, n.Y));
            var b = o as Barra;
            if (b != null)
            {
                No a = M.NoPorId(b.N1), c = M.NoPorId(b.N2);
                if (a != null) pts.Add(new Point(a.X, a.Y));
                if (c != null) pts.Add(new Point(c.X, c.Y));
            }
            Vista.EnquadrarPontos(pts);
            Log((n != null ? "Nó " + n.Id : "Barra " + b.Id) + " selecionado.");
        }

        internal void ExecutarItemPaleta(ItemPaleta it)
        {
            if (it == null) return;
            if (it.Tipo == TipoItem.Comando)
            {
                var r = Recentes();
                r.Remove(it.Chave); r.Insert(0, it.Chave);
                if (r.Count > 6) r.RemoveRange(6, r.Count - 6);
                if (!PaletaSemPersistir) Config.SalvarRecentesComandos(r);
            }
            it.Acao();
        }

        internal bool PaletaSemPersistir;

        // Testes do modo --capturas: paleta (busca e execucao) e entrada dinamica (comprimento + angulo). Devolve o numero de falhas.
        internal int TestesPaletaEDinamica(StringBuilder log)
        {
            int falhas = 0;
            Action<bool, string> ok = (c, m) => { log.AppendLine((c ? "OK     " : "FALHA  ") + m); if (!c) falhas++; };
            PaletaSemPersistir = true;
            RegistrarExtras();
            ok(cmds.Count >= 40, "comandos registrados na paleta: " + cmds.Count);
            var faltam = ComandosSemItem().ToList();
            ok(faltam.Count == 0, "todo comando digitavel tem item na paleta" + (faltam.Count > 0 ? " (faltam: " + string.Join(", ", faltam) + ")" : ""));

            var pv = new PaletaVisual(this);
            Func<string, string> topo = q => { pv.Caixa.Text = q; var s = pv.Selecionado; return s == null ? "(nada)" : s.Nome; };

            string t = topo("momen");
            ok(t == "Momento", "\"momen\" acha: " + t);
            ExecutarItemPaleta(pv.Selecionado);
            ok(Vista.Diag == Diagrama.Momento, "Enter em \"Momento\" liga o diagrama de momento (Diag = " + Vista.Diag + ")");
            MostrarDiagrama(Diagrama.Nenhum);

            ok(topo("calcr") == "Calcular", "busca difusa \"calcr\" acha: " + topo("calcr"));
            ok(topo("verificacao") == "Verificação de barras", "sem acento \"verificacao\" acha: " + topo("verificacao"));
            ok(topo("apagar") == "Apagar", "\"apagar\" acha: " + topo("apagar"));

            int idNo = M.NoPorId(3) != null ? 3 : (M.Nos.Count > 0 ? M.Nos[0].Id : -1);
            if (idNo >= 0)
            {
                Vista.Selecionar(new object[0]);
                t = topo("nó " + idNo);
                ok(t == "Nó " + idNo, "\"nó " + idNo + "\" acha: " + t);
                ExecutarItemPaleta(pv.Selecionado);
                ok(Vista.Sel.OfType<No>().Any(n => n.Id == idNo), "Enter em \"Nó " + idNo + "\" seleciona o nó " + idNo);
            }
            var b1 = M.Barras.FirstOrDefault();
            if (b1 != null)
            {
                Vista.Selecionar(new object[0]);
                t = topo("barra " + b1.Id);
                ok(t == "Barra " + b1.Id, "\"barra " + b1.Id + "\" acha: " + t);
                ExecutarItemPaleta(pv.Selecionado);
                ok(Vista.Sel.OfType<Barra>().Any(b => b.Id == b1.Id), "Enter em \"Barra " + b1.Id + "\" seleciona a barra");
            }
            Vista.Selecionar(new object[0]);

            string secaoAntes = cbSecao.Text;
            topo("w 200");
            var perfil = pv.Itens.FirstOrDefault(i => i.Tipo == TipoItem.Perfil);
            ok(perfil != null && perfil.Nome.StartsWith("W 200"), "\"w 200\" lista perfil: " + (perfil == null ? "(nenhum)" : perfil.Nome));
            if (perfil != null) { ExecutarItemPaleta(perfil); ok(cbSecao.Text == perfil.Nome, "Enter no perfil define a seção: " + cbSecao.Text); }
            topo("tubo 100");
            var tubo = pv.Itens.FirstOrDefault(i => i.Tipo == TipoItem.Perfil);
            ok(tubo != null, "\"tubo 100\" lista perfil: " + (tubo == null ? "(nenhum)" : tubo.Nome));
            cbSecao.Text = secaoAntes;
            pv.Caixa.Text = "";
            ok(pv.Itens.Count > 0, "paleta vazia mostra recentes/comandos (" + pv.Itens.Count + " itens)");

            // ---- entrada dinamica ----
            int barras0 = M.Barras.Count;
            UsarFerramenta(Ferramenta.Barra);
            PontoBarra(100, 100);
            Vista.SimularMouse(new Point(300, 200));
            Vista.DinTecla("3", Key.None); Vista.DinTecla(null, Key.Tab); Vista.DinTecla("3", Key.None); Vista.DinTecla("0", Key.None);
            ok(Vista.Din[0] == "3" && Vista.Din[1] == "30" && Vista.DinCampo == 1, "digitar 3, Tab, 30 trava comprimento 3 e ângulo 30");
            Point pd = Vista.PontoDinamico();
            double ex = 100 + 3 * Math.Cos(Math.PI / 6), ey = 100 + 3 * Math.Sin(Math.PI / 6);
            ok(Math.Abs(pd.X - ex) < 1e-6 && Math.Abs(pd.Y - ey) < 1e-6, "ponto dinâmico = (" + Num.Fmt(pd.X, 4) + "; " + Num.Fmt(pd.Y, 4) + "), esperado (" + Num.Fmt(ex, 4) + "; " + Num.Fmt(ey, 4) + ")");
            ConfirmarDinamica();
            ok(M.Barras.Count == barras0 + 1, "Enter cria 1 barra (" + barras0 + " -> " + M.Barras.Count + ")");
            if (M.Barras.Count == barras0 + 1)
            {
                Barra nb = M.Barras[M.Barras.Count - 1];
                No a = M.NoPorId(nb.N1), c = M.NoPorId(nb.N2);
                double comp = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y)), ang = Math.Atan2(c.Y - a.Y, c.X - a.X) * 180 / Math.PI;
                ok(Math.Abs(comp - 3) < 1e-6 && Math.Abs(ang - 30) < 1e-6, "barra criada: comprimento " + Num.Fmt(comp, 4) + " m, ângulo " + Num.Fmt(ang, 3) + "°");
            }
            ok(Vista.Din[0] == "" && Vista.Din[1] == "", "valores travados são limpos após confirmar");
            // so comprimento travado: a direcao vem do mouse
            Vista.SimularMouse(Vista.Tela(100 + 3 * Math.Cos(Math.PI / 6), 100 + 3 * Math.Sin(Math.PI / 6) + 5));
            Vista.DinTecla("2", Key.None); Vista.DinTecla(".", Key.None); Vista.DinTecla("5", Key.None);
            Point p2 = Vista.PontoDinamico(); Point ini = Vista.InicioBarra.Value;
            ok(Math.Abs(Math.Sqrt((p2.X - ini.X) * (p2.X - ini.X) + (p2.Y - ini.Y) * (p2.Y - ini.Y)) - 2.5) < 1e-6, "só comprimento 2.5 travado: distância = 2,5 m na direção do mouse");
            Vista.DinLimpar();
            UsarFerramenta(Ferramenta.Selecionar);
            for (int i = 0; i < 4 && M.Barras.Count > barras0; i++) Desfazer();
            ok(M.Barras.Count == barras0, "teste desfeito (barras = " + M.Barras.Count + ")");

            // medir com entrada dinamica
            UsarFerramenta(Ferramenta.Medir);
            PontoMedida(0, 0);
            Vista.SimularMouse(new Point(250, 250));
            Vista.DinTecla("4", Key.None); Vista.DinTecla(null, Key.Tab); Vista.DinTecla("9", Key.None); Vista.DinTecla("0", Key.None);
            ConfirmarDinamica();
            ok(Vista.MedidaFim != null && Math.Abs(Vista.MedidaFim.Value.X) < 1e-6 && Math.Abs(Vista.MedidaFim.Value.Y - 4) < 1e-6, "medir: 4 m a 90° termina em (0; 4)" + (Vista.MedidaFim != null ? " -> (" + Num.Fmt(Vista.MedidaFim.Value.X, 3) + "; " + Num.Fmt(Vista.MedidaFim.Value.Y, 3) + ")" : ""));
            Vista.MedidaIni = null; Vista.MedidaFim = null;
            UsarFerramenta(Ferramenta.Selecionar);
            PaletaSemPersistir = false;
            log.AppendLine(falhas == 0 ? "TUDO OK" : Pl.N(falhas, "FALHA", "FALHAS"));
            return falhas;
        }

        // Estado para a captura 39-entrada-dinamica: barra em andamento com comprimento 3 travado e o angulo 30 sendo digitado.
        internal void PrepararCapturaDinamica()
        {
            UsarFerramenta(Ferramenta.Barra);
            No n0 = M.Nos.OrderBy(x => x.Y).ThenBy(x => x.X).First();
            Vista.InicioBarra = new Point(n0.X, n0.Y);
        }

        internal void AjustarCapturaDinamica()
        {
            var ini = Vista.InicioBarra.Value;
            Point t0 = Vista.Tela(ini.X, ini.Y);
            Vista.SimularMouse(new Point(t0.X + 190, t0.Y - 110));
            Vista.DinLimpar();
            Vista.DinTecla("3", Key.None); Vista.DinTecla(null, Key.Tab); Vista.DinTecla("3", Key.None); Vista.DinTecla("0", Key.None);
        }

        internal void EncerrarCapturaDinamica()
        {
            Vista.DinLimpar(); Vista.InicioBarra = null; Vista.SimularMouse(null);
            UsarFerramenta(Ferramenta.Selecionar);
        }

        PaletaJanela paletaAberta;

        void AbrirPaleta()
        {
            if (paletaAberta != null) { paletaAberta.Close(); return; }
            Bastidor.IsOpen = false;
            paletaAberta = new PaletaJanela(this) { Owner = this };
            paletaAberta.Closed += (s, e) => { paletaAberta = null; };
            paletaAberta.Show();
        }
    }

    // Conteudo da paleta (usado na janela flutuante e nas capturas).
    public sealed class PaletaVisual : Border
    {
        readonly MainWindow w;
        public readonly TextBox Caixa = new TextBox();
        readonly StackPanel lista = new StackPanel();
        readonly ScrollViewer rolagem;
        List<ItemPaleta> itens = new List<ItemPaleta>();
        readonly List<Border> linhas = new List<Border>();
        public int Sel;
        public Action Fechar;

        public IReadOnlyList<ItemPaleta> Itens { get { return itens; } }

        public PaletaVisual(MainWindow w)
        {
            this.w = w;
            Width = 560;
            Background = TemaPlant.BSup1; BorderBrush = TemaPlant.BBorda; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8);
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var raiz = new DockPanel();
            var topo = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 0, 14, 0), Height = 44 };
            var gt = new Grid();
            gt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gt.ColumnDefinitions.Add(new ColumnDefinition());
            var lupa = MainWindow.Icone("busca", 16); ((FrameworkElement)lupa).VerticalAlignment = VerticalAlignment.Center; ((FrameworkElement)lupa).Margin = new Thickness(0, 0, 10, 0);
            gt.Children.Add(lupa);
            Caixa.Background = Brushes.Transparent; Caixa.BorderThickness = new Thickness(0); Caixa.Foreground = TemaPlant.BTexto1; Caixa.CaretBrush = TemaPlant.BTexto1;
            Caixa.FontFamily = TemaPlant.Fonte; Caixa.FontSize = 14; Caixa.VerticalContentAlignment = VerticalAlignment.Center;
            Caixa.FocusVisualStyle = null;
            Grid.SetColumn(Caixa, 1);
            gt.Children.Add(Caixa);
            var dica = new TextBlock { Text = "Buscar comando, perfil (W 200) ou objeto (nó 3)…", Foreground = TemaPlant.BTextoMudo, FontSize = 14, FontFamily = TemaPlant.Fonte, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Grid.SetColumn(dica, 1);
            gt.Children.Add(dica);
            Caixa.TextChanged += (s, e) => { dica.Visibility = Caixa.Text == "" ? Visibility.Visible : Visibility.Collapsed; Atualizar(); };
            topo.Child = gt;
            DockPanel.SetDock(topo, Dock.Top);
            raiz.Children.Add(topo);

            var rodape = new Border { BorderBrush = TemaPlant.BBordaSutil, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 6, 14, 6), Background = TemaPlant.BSup0 };
            rodape.Child = new TextBlock { Text = "↑ ↓ navegar     Enter executar     Esc fechar", FontSize = 10.5, Foreground = TemaPlant.BTexto3, FontFamily = TemaPlant.Fonte };
            DockPanel.SetDock(rodape, Dock.Bottom);
            raiz.Children.Add(rodape);

            rolagem = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 360, Content = lista, Padding = new Thickness(0, 4, 0, 4) };
            raiz.Children.Add(rolagem);
            Child = raiz;

            Caixa.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down) { Mover(1); e.Handled = true; }
                else if (e.Key == Key.Up) { Mover(-1); e.Handled = true; }
                else if (e.Key == Key.Enter || e.Key == Key.Return) { ExecutarSelecionado(); e.Handled = true; }
                else if (e.Key == Key.Escape) { if (Fechar != null) Fechar(); e.Handled = true; }
            };
            Atualizar();
        }

        public void Atualizar()
        {
            itens = w.ConsultarPaleta(Caixa.Text);
            Sel = 0;
            Reconstruir();
        }

        void Reconstruir()
        {
            lista.Children.Clear(); linhas.Clear();
            if (itens.Count == 0)
            {
                lista.Children.Add(new TextBlock { Text = "Nada encontrado para “" + Caixa.Text + "”. Tente outra palavra.", Foreground = TemaPlant.BTexto3, FontSize = 12, Margin = new Thickness(14, 12, 14, 12), FontFamily = TemaPlant.Fonte });
                return;
            }
            bool vazio = Caixa.Text.Trim() == "";
            for (int i = 0; i < itens.Count; i++)
            {
                var it = itens[i];
                string sec = vazio ? (w.RecentesDe(it) ? "RECENTES" : "COMANDOS") : it.Tipo == TipoItem.Comando ? "COMANDOS" : it.Tipo == TipoItem.Perfil ? "PERFIS" : "OBJETOS DO MODELO";
                string secAnt = i == 0 ? null : (vazio ? (w.RecentesDe(itens[i - 1]) ? "RECENTES" : "COMANDOS") : itens[i - 1].Tipo == TipoItem.Comando ? "COMANDOS" : itens[i - 1].Tipo == TipoItem.Perfil ? "PERFIS" : "OBJETOS DO MODELO");
                if (sec != secAnt)
                    lista.Children.Add(new TextBlock { Text = sec, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = TemaPlant.BTexto3, FontFamily = TemaPlant.Fonte, Margin = new Thickness(14, i == 0 ? 4 : 8, 14, 3) });
                lista.Children.Add(Linha(it, i));
            }
            Realcar();
        }

        Border Linha(ItemPaleta it, int idx)
        {
            var g = new Grid { Height = 30 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = it.Icone != null && Icones.Existe(it.Icone) ? MainWindow.Icone(it.Icone, 16) : new Border();
            ((FrameworkElement)ic).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)ic).VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            var tx = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = it.Nome, FontSize = 12.5, Foreground = TemaPlant.BTexto1, FontFamily = TemaPlant.Fonte, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(it.Detalhe))
                tx.Children.Add(new TextBlock { Text = Resumo(it.Detalhe), FontSize = 11, Foreground = TemaPlant.BTexto3, FontFamily = TemaPlant.Fonte, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(tx, 1);
            g.Children.Add(tx);
            if (!string.IsNullOrEmpty(it.Atalho))
            {
                var at = new Border { Background = TemaPlant.BSup3, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                at.Child = new TextBlock { Text = it.Atalho, FontSize = 10.5, Foreground = TemaPlant.BTexto2, FontFamily = TemaPlant.Fonte };
                Grid.SetColumn(at, 2);
                g.Children.Add(at);
            }
            var b = new Border { Child = g, Margin = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = idx };
            b.MouseEnter += (s, e) => { Sel = idx; Realcar(); };
            b.MouseLeftButtonUp += (s, e) => { Sel = idx; ExecutarSelecionado(); };
            linhas.Add(b);
            return b;
        }

        static string Resumo(string d)
        {
            int p = -1;   // fim da 1ª frase: ponto seguido de espaço (não corta ".estructto")
            for (int i = 0; i < d.Length; i++) if (d[i] == '.' && (i == d.Length - 1 || d[i + 1] == ' ')) { p = i; break; }
            string r = p > 0 ? d.Substring(0, p) : d;
            return r.Length > 46 ? r.Substring(0, 45).TrimEnd() + "…" : r;
        }

        void Realcar()
        {
            for (int i = 0; i < linhas.Count; i++)
            {
                bool s = i == Sel;
                linhas[i].Background = s ? TemaPlant.BAcentoSutil : Brushes.Transparent;
                linhas[i].BorderBrush = s ? TemaPlant.BAcento : Brushes.Transparent;
                linhas[i].BorderThickness = new Thickness(2, 0, 0, 0);
            }
            if (Sel >= 0 && Sel < linhas.Count) linhas[Sel].BringIntoView();
        }

        public void Mover(int d)
        {
            if (itens.Count == 0) return;
            Sel = (Sel + d + itens.Count) % itens.Count;
            Realcar();
        }

        public ItemPaleta Selecionado { get { return Sel >= 0 && Sel < itens.Count ? itens[Sel] : null; } }

        public void ExecutarSelecionado()
        {
            var it = Selecionado;
            if (it == null) return;
            if (Fechar != null) Fechar();
            w.Dispatcher.BeginInvoke(new Action(() => { w.ExecutarItemPaleta(it); }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    // Janela flutuante sem moldura, centralizada na janela principal.
    public sealed class PaletaJanela : Window
    {
        readonly PaletaVisual visual;
        bool fechando;

        public PaletaJanela(MainWindow dono)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; SizeToContent = SizeToContent.WidthAndHeight;
            Background = TemaPlant.BSup0; WindowStartupLocation = WindowStartupLocation.Manual;
            visual = new PaletaVisual(dono);
            visual.Fechar = FecharSeguro;
            Content = visual;
            Loaded += (s, e) =>
            {
                Left = dono.Left + (dono.ActualWidth - ActualWidth) / 2;
                Top = dono.Top + Math.Max(60, dono.ActualHeight * 0.14);
                visual.Caixa.Focus();
                Anim.Abrir(this);
            };
            Deactivated += (s, e) => FecharSeguro();
        }

        void FecharSeguro() { if (fechando) return; fechando = true; Close(); }
    }
}
