// Fluxo G (--testes-fluxo <pasta> G): torre metálica externa do elevador anexa ao bloco F6 (trabalho de estruturas metálicas,
// elevador para 10 pessoas com cadeirante). Medidas inventadas: poço 2,20 × 2,40 m, 4 andares (0; 3,5; 7; 10,5; 13,5 m),
// pilares W150, vigas de borda W150, passarela de 1,6 m até o prédio (apoio articulado na laje do F6).
// Dirige o que o engenheiro faz: novo projeto com grade e níveis, Pilar com clique, vigas digitadas, PgUp para o nível de
// cima, copiar pavimento, NÓ no meio da viga e do pilar, DIVIDIR no nó, JUNTAR, snap no Realista (só nas linhas), mudar a
// cota de um nível, F5. Problemas que este fluxo pegou: docs/v6/TORRE-ELEVADOR.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Portico;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void FluxoTorre(MainWindow w, Rel r)
        {
            const double X = 2.2, Y = 2.4;
            double[] zs = { 0, 3.5, 7, 10.5, 13.5 };
            var mi = ModeloInicial.Por("vazio");
            var g = new GradeNiveis { X = GradeNiveis.Espacamentos("2.2"), Y = GradeNiveis.Espacamentos("2.4"), Z = GradeNiveis.Cotas("3x3.5 3") };
            string pasta = System.IO.Path.Combine(pastaSaida, "fluxo-G-projeto");
            if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, true);
            var dados = new NovoProjetoDados { Modelo = mi, Grade = g, Nome = "Torre elevador F6", Pasta = pasta, Cliente = "Bloco F6", Engenheiro = "Equipe", Empresa = "Trabalho",
                                               Material = mi.Material, Pilar = mi.Pilar, Viga = mi.Viga, Normas = new List<string> { "NBR 8800" } };
            r.Exigir(Esperar(Inicio.Acoes.Novo(w, dados)), "novo projeto: grade 2,2 × 2,4 m e níveis " + string.Join("; ", g.Z.Select(z => Nf(z))));
            var v = w.vista3D;
            r.Checar(Niveis.Lista(J(w)).Count == 5, "5 níveis: " + string.Join(", ", Niveis.Lista(J(w)).Select(n => n.Nome + " " + Nf(n.Z))));
            Point T(double x, double y, double z) => v.Tela(new Point3D(x, y, z));
            var cantos = new[] { (0.0, 0.0), (X, 0.0), (X, Y), (0.0, Y) };
            bool Pilar(JsonObject m, double x, double y, double z0, double z1) => NoEm(m, x, y, z0) is string a && NoEm(m, x, y, z1) is string b && BarraEntre(m, a, b) != null;
            int Visiveis() => Priv<Modelo3D>(v, "mod").Barras.Count;
            string pPilar = PerfilCatalogo("W150", "W150x22.5", "W150x13");
            Esperar(w.UsarSecao3D(pPilar));

            // --- andar 1: Pilar com clique nos 4 cantos da grade (plano do novo projeto = 1º nível) e vigas digitadas
            void Pilares(double z)
            {
                w.UsarFerramenta3D(Ferramenta3D.Pilar);
                v.MostrarVista(VistaPadrao.Topo, false); v.Enquadrar(false); EsperarIdle();
                foreach (var (x, y) in cantos) { v.MoverTela(T(x, y, z) + new Vector(2, 1)); Esperar(v.ClicarTela(T(x, y, z) + new Vector(2, 1))); AteCond(() => true, 5, w); }
                v.Tecla(Key.Escape);
            }
            void Vigas(double z)
            {
                int nb0 = NBarras(w);
                Comando(w, "L"); Comando(w, "#0,0," + Nf(z));
                foreach (string d in new[] { "@2.2,0,0", "@0,2.4,0", "@-2.2,0,0", "@0,-2.4,0" }) { int k = NBarras(w); Comando(w, d); AteCond(() => NBarras(w) == k + 1, 20, w); }
                v.Tecla(Key.Enter); v.Tecla(Key.Escape);
                r.Checar(NBarras(w) == nb0 + 4, "4 vigas de borda em z = " + Nf(z) + " (" + (NBarras(w) - nb0) + ")");
            }
            w.ApoioBase3D = true;
            r.Checar(Math.Abs(v.PlanoAtual.Valor - 3.5) < 1e-9, "plano de trabalho no 1º nível (" + v.PlanoAtual.Nome + ")");
            Pilares(3.5);
            w.ApoioBase3D = false;
            r.Exigir(cantos.All(c => Pilar(J(w), c.Item1, c.Item2, 0, 3.5)), "4 pilares de 0 a 3,5 m (" + NBarras(w) + " barras)");
            Vigas(3.5);
            Foto(w, "G-01-andar1", () => v.MostrarVista(VistaPadrao.Iso, false));

            // --- PgUp: nível de cima. O plano, a vista isolada e o painel têm de andar juntos.
            Comando(w, "NIVEL +");
            r.Checar(Math.Abs(v.PlanoAtual.Valor - 7) < 1e-9, "NIVEL + (PgUp): plano em z = 7 (" + v.PlanoAtual.Nome + ")");
            r.Checar(w.DescricaoPlano.Contains("7"), "NIVEL +: a janela sabe que o nível ativo é o de 7 m (\"" + w.DescricaoPlano + "\")");
            Pilares(7);
            r.Checar(cantos.All(c => Pilar(J(w), c.Item1, c.Item2, 3.5, 7)), "Pilar no nível de 7 m desce até 3,5 m (andar 2)");
            r.Checar(Visiveis() >= 4, "os pilares recém-desenhados do andar 2 aparecem na vista (" + Visiveis() + " barras visíveis)");
            Vigas(7);

            // --- copiar o andar 2 para cima (10,5) e desenhar o último (13,5: pé-direito de 3 m)
            w.AtivarNivel(7);
            Esperar(w.Lote(Niveis.CopiarParaCima(J(w), 3.5, 7, 1, 3.5), "teste: copiar andar 2"));
            r.Checar(cantos.All(c => Pilar(J(w), c.Item1, c.Item2, 7, 10.5)), "copiar pavimento: andar 3 (7 → 10,5 m)");
            w.AtivarNivel(13.5);
            Pilares(13.5);
            r.Checar(cantos.All(c => Pilar(J(w), c.Item1, c.Item2, 10.5, 13.5)), "Pilar no topo (13,5 m) desce 3 m até 10,5 m");
            Vigas(13.5);
            // passarela até o prédio: 2 vigas de 1,6 m em cada pavimento, apoio articulado na face do F6
            for (int i = 1; i < zs.Length - 1; i++)
                Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] { (new Point3D(0, Y, zs[i]), new Point3D(0, Y + 1.6, zs[i])), (new Point3D(X, Y, zs[i]), new Point3D(X, Y + 1.6, zs[i])) }, null), "teste: passarela"));
            var apoios = J(w)["nos"].AsObject().Where(kv => Math.Abs((double)kv.Value["y"] - (Y + 1.6)) < 1e-6).Select(kv => kv.Key).ToList();
            Esperar(w.OperarEspacial(dd => dd.Aplicar(m => GeometriaEdicao.Apoio(m, apoios, new[] { true, true, true, false, false, false }, null)), "teste: apoio no prédio"));
            w.Completo3D();
            Foto(w, "G-02-torre", () => { v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); });
            r.L("torre: " + NBarras(w) + " barras, " + NNos(w) + " nós, " + Soltos(J(w)) + " nós soltos");

            // --- snap no Realista: só nos eixos (linhas e nós), nunca nas arestas do perfil
            w.DefinirRealista(true); EsperarIdle();
            w.AtivarNivel(3.5);
            w.UsarFerramenta3D(Ferramenta3D.Barra);
            v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); EsperarIdle();
            var m0 = J(w);
            var eixos = m0["barras"].AsObject().Select(kv => (Pos(m0, (string)kv.Value["ni"]), Pos(m0, (string)kv.Value["nj"]))).ToList();
            bool NoEixo(Point3D p) => eixos.Any(e => { Vector3D d = e.Item2 - e.Item1; double t = Math.Max(0, Math.Min(1, Vector3D.DotProduct(p - e.Item1, d) / d.LengthSquared)); return (e.Item1 + d * t - p).Length < 1e-6; });
            var fora = new List<string>(); int objetos = 0;
            for (double zz = 0.5; zz <= 3.5; zz += 0.5)
                for (double ang = 0; ang < 360; ang += 15)
                {
                    Point c = T(0, 0, zz) + new Vector(Math.Cos(ang * Math.PI / 180), Math.Sin(ang * Math.PI / 180)) * 14;
                    v.MoverTela(c);
                    var s = v.UltimoSnap;
                    if (s?.P == null || s.Tipo == TipoSnap.Plano || s.Tipo == TipoSnap.Grade || s.Tipo == TipoSnap.Polar || s.Tipo == TipoSnap.Ortogonal || s.Tipo == TipoSnap.Rastreio || s.Tipo == TipoSnap.Nenhum) continue;
                    objetos++;
                    Point3D q = s.P.Value;
                    bool grade = (Math.Abs(q.X) < 1e-6 || Math.Abs(q.X - X) < 1e-6) && (Math.Abs(q.Y) < 1e-6 || Math.Abs(q.Y - Y) < 1e-6) && zs.Any(z => Math.Abs(q.Z - z) < 1e-6);   // cruzamento da grade
                    if (!NoEixo(q) && !grade) fora.Add(s.Nome + " " + Nf(q.X) + ";" + Nf(q.Y) + ";" + Nf(q.Z));
                }
            r.Checar(fora.Count == 0, "Realista: snap de objeto só nos eixos/nós (" + objetos + " atrações, " + fora.Count + " fora do eixo" + (fora.Count > 0 ? ": " + string.Join(" | ", fora.Take(4)) : "") + ")");
            v.MoverTela(T(0, 0, 3.5) + new Vector(12, -8));
            Foto(w, "G-03-realista-snap", () => { });
            v.Tecla(Key.Escape);
            w.DefinirRealista(false);
            w.Completo3D();

            // --- NÓ no meio da viga e do pilar: divide a barra (o nó tem de ficar ligado)
            string vigaY0 = BarraEntre(J(w), NoEm(J(w), 0, 0, 3.5), NoEm(J(w), X, 0, 3.5));
            Comando(w, "NO"); Comando(w, "#1.1,0,3.5"); AteCond(() => NoEm(J(w), 1.1, 0, 3.5) != null, 20, w);
            Comando(w, "#0,0,1.75"); AteCond(() => NoEm(J(w), 0, 0, 1.75) != null, 20, w);
            v.Tecla(Key.Escape); EsperarIdle();
            var m1 = J(w);
            r.Checar(BarraEntre(m1, NoEm(m1, 0, 0, 3.5), NoEm(m1, 1.1, 0, 3.5)) != null && BarraEntre(m1, NoEm(m1, 1.1, 0, 3.5), NoEm(m1, X, 0, 3.5)) != null, "NÓ no meio da viga (1,1; 0; 3,5): a viga dividiu nele");
            r.Checar(BarraEntre(m1, NoEm(m1, 0, 0, 0), NoEm(m1, 0, 0, 1.75)) != null, "NÓ no meio do pilar (0; 0; 1,75): o pilar dividiu nele");
            r.Checar(Soltos(m1) == 0, "nenhum nó solto depois do NÓ (" + Soltos(m1) + ")");

            // --- DIVIDIR no nó que já está sobre a barra (nó solto de arquivo antigo, fora do meio): viga x = 2,2 do nível 7
            Esperar(w.OperarEspacial(async dd => await dd.Adicionar("no", new JsonObject { ["x"] = X, ["y"] = 0.8, ["z"] = 7.0 }), "teste: nó solto sobre a viga"));
            var m2 = J(w);
            string vigaX = BarraEntre(m2, NoEm(m2, X, 0, 7), NoEm(m2, X, Y, 7));
            r.Exigir(vigaX != null && NoEm(m2, X, 0.8, 7) != null, "nó solto (2,2; 0,8; 7) sobre a viga " + vigaX);
            v.Selecionar(null, new[] { vigaX }); EsperarIdle();
            Comando(w, "DIVIDIR");
            var pd = w.PainelEdicaoAberto; r.Exigir(pd != null, "DIVIDIR: painel abriu");
            PrepararDialogoForaDaTela(pd);
            bool temModo = pd.Campos.ContainsKey("modo");
            if (temModo) ((ComboBox)pd.Campos["modo"]).SelectedItem = "Nos nós sobre a barra";
            FotoDialogo(pd, "G-04-dividir");
            Esperar(pd.Executar()); pd.Close(); EsperarIdle();
            var m3 = J(w);
            r.Checar(temModo, "DIVIDIR tem a opção de dividir nos nós que já estão sobre a barra");
            r.Checar(BarraEntre(m3, NoEm(m3, X, 0, 7), NoEm(m3, X, 0.8, 7)) != null && Soltos(m3) == 0, "DIVIDIR: a viga dividiu no nó (2,2; 0,8; 7) e ele deixou de ser solto (" + Soltos(m3) + " soltos)");
            v.LimparSelecao();

            // --- JUNTAR: escolher um trecho da viga dividida pelo NÓ junta a peça inteira de volta
            m3 = J(w);
            string t1 = BarraEntre(m3, NoEm(m3, 0, 0, 3.5), NoEm(m3, 1.1, 0, 3.5));
            int nb1 = NBarras(w);
            v.Selecionar(null, new[] { t1 }); EsperarIdle();
            Comando(w, "JUNTARBARRAS"); AteCond(() => NBarras(w) == nb1 - 1, 20, w);
            var m4 = J(w);
            r.Checar(NBarras(w) == nb1 - 1 && BarraEntre(m4, NoEm(m4, 0, 0, 3.5), NoEm(m4, X, 0, 3.5)) != null && NoEm(m4, 1.1, 0, 3.5) == null, "JUNTAR com 1 trecho escolhido: a viga volta a ser uma só de 0 a 2,2 m (" + w.StatusMsg.Text + ")");
            string p1 = BarraEntre(m4, NoEm(m4, 0, 0, 0), NoEm(m4, 0, 0, 1.75)), p2 = BarraEntre(m4, NoEm(m4, 0, 0, 1.75), NoEm(m4, 0, 0, 3.5));
            v.Selecionar(null, new[] { p1, p2 }); EsperarIdle();
            Comando(w, "JUNTARBARRAS"); AteCond(() => NoEm(J(w), 0, 0, 1.75) == null, 20, w);
            r.Checar(Pilar(J(w), 0, 0, 0, 3.5) && NoEm(J(w), 0, 0, 1.75) == null, "JUNTAR com os 2 trechos do pilar: pilar inteiro de novo (" + w.StatusMsg.Text + ")");
            v.LimparSelecao();

            // --- nível: subir a cota do topo de 13,5 para 14 m leva junto os nós dele (pilares esticam)
            string topo = Niveis.Lista(J(w)).First(n => Math.Abs(n.Z - 13.5) < 1e-6).Nome;
            Esperar(w.MudarCotaNivel(topo, 14));
            var m5 = J(w);
            r.Checar(Niveis.Lista(m5).Any(n => n.Nome == "N+14,00" && Math.Abs(n.Z - 14) < 1e-9) && !Niveis.Lista(m5).Any(n => n.Nome == topo), "nível " + topo + " em 14 m, e o nome automático segue a cota (N+14,00)");
            r.Checar(cantos.All(c => Pilar(m5, c.Item1, c.Item2, 10.5, 14)) && NoEm(m5, 0, 0, 13.5) == null, "mudar a cota do nível leva os nós dele: pilares do último andar vão de 10,5 a 14 m");
            Comando(w, "U"); AteCond(() => NoEm(J(w), 0, 0, 13.5) != null, 20, w);
            r.Checar(NoEm(J(w), 0, 0, 13.5) != null && Niveis.Lista(J(w)).Any(n => Math.Abs(n.Z - 13.5) < 1e-9), "Ctrl+Z desfaz a cota e os nós de uma vez");
            w.AbrirNiveis(); EsperarIdle();
            var jn = Application.Current.Windows.OfType<JanelaNiveis>().FirstOrDefault();
            if (jn != null) { PrepararDialogoForaDaTela(jn); FotoDialogo(jn, "G-05-niveis"); jn.Close(); }

            // --- F5
            Esperar(w.CalcularEspacial());
            r.Checar(w.EspacialAtivo.Calculado, "torre calcula (" + w.StatusMsg.Text + ")");
            w.Completo3D();
            Foto(w, "G-06-calculada", () => { v.MostrarVista(VistaPadrao.Iso, false); v.Enquadrar(false); });
        }
    }
}
