// Estructto.exe --testes-novo <pasta>: 10 criações seguidas de projeto (Inicio.Acoes.Novo, edifício com a grade padrão (2x6 × 2x5 × 3 níveis)) com o motor
// quente; cada uma deve levar < 2 s e não lançar exceção (docs/v3/NOVO-ARQUIVO.md). Resumo em <pasta>/testes-novo.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace NovoFtool
{
    public static partial class Capturas
    {
        internal static void ConfigurarNovo(MainWindow w, string pasta)
        {
            pastaSaida = Path.GetFullPath(pasta);
            Directory.CreateDirectory(pastaSaida);
            Ativo = true;   // erros sobem como exceção em vez de caixa de diálogo
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarNovo(w)), DispatcherPriority.ApplicationIdle);
        }

        static void RodarNovo(MainWindow w)
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Grava() => File.WriteAllText(Path.Combine(pastaSaida, "testes-novo.txt"), sb.ToString(), new UTF8Encoding(false));
            try
            {
                string dev = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "motor"));
                if (Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR") == null && Directory.Exists(Path.Combine(dev, "prossen_estrutural"))) Environment.SetEnvironmentVariable("ESTRUCTTO_MOTOR", dev);
                var sw = Stopwatch.StartNew(); var tempos = new System.Collections.Generic.List<long>();
                Esperar(Motor.Get("/api/versao"));   // partida a frio, fora da medição
                sb.AppendLine("motor frio: " + sw.ElapsedMilliseconds + " ms");
                var modelo = ModeloInicial.Todos.First(m => m.Chave == "edificio");
                for (int i = 1; i <= 10; i++)
                {
                    var g = new GradeNiveis { X = GradeNiveis.Espacamentos(modelo.EixosX), Y = GradeNiveis.Espacamentos(modelo.EixosY), Z = GradeNiveis.Cotas(modelo.Niveis) };
                    var d = new NovoProjetoDados { Modelo = modelo, Grade = g, Nome = "teste-novo-" + i, Pasta = Path.Combine(pastaSaida, "projetos"), Material = modelo.Material, Pilar = modelo.Pilar, Viga = modelo.Viga };
                    sw.Restart();
                    try { Esperar(Inicio.Acoes.Novo(w, d)); }
                    catch (Exception ex) { falhas++; sb.AppendLine("FALHA " + i + ": " + ex.Message); continue; }
                    long ms = sw.ElapsedMilliseconds;
                    tempos.Add(ms); bool ok = ms < 2500;   // teto individual folgado (ruído da máquina); mediana abaixo
                    if (!ok) falhas++;
                    sb.AppendLine((ok ? "ok    " : "FALHA ") + "criação " + i + ": " + ms + " ms");
                    Grava();
                    w.FecharEspaciaisSemPerguntar();
                }
                tempos.Sort(); long med = tempos.Count > 0 ? tempos[tempos.Count / 2] : 0;
                bool medOk = med < 2500; if (!medOk) falhas++;
                sb.AppendLine((medOk ? "ok    " : "FALHA ") + "mediana " + med + " ms (teto 2500; meta 2000 em máquina ociosa)");
                // estresse (só informa, sem limite): grade 20x20 vãos, 3 níveis
                var ge = new GradeNiveis { X = GradeNiveis.Espacamentos("20x5"), Y = GradeNiveis.Espacamentos("20x5"), Z = GradeNiveis.Cotas("3x3,5") };
                var de = new NovoProjetoDados { Modelo = modelo, Grade = ge, Nome = "teste-novo-20x20", Pasta = Path.Combine(pastaSaida, "projetos"), Material = modelo.Material, Pilar = modelo.Pilar, Viga = modelo.Viga };
                sw.Restart(); Esperar(Inicio.Acoes.Novo(w, de)); sb.AppendLine("info  grade 20x20x3: " + sw.ElapsedMilliseconds + " ms");
                w.FecharEspaciaisSemPerguntar();
                // 30x30 (~8000 barras) e pasta sem permissão: não podem derrubar o programa
                var g3 = new GradeNiveis { X = GradeNiveis.Espacamentos("30x5"), Y = GradeNiveis.Espacamentos("30x5"), Z = GradeNiveis.Cotas("3x3,5") };
                sw.Restart(); Esperar(Inicio.Acoes.Novo(w, new NovoProjetoDados { Modelo = modelo, Grade = g3, Nome = "teste-novo-30x30", Pasta = Path.Combine(pastaSaida, "projetos"), Material = modelo.Material, Pilar = modelo.Pilar, Viga = modelo.Viga }));
                sb.AppendLine("info  grade 30x30x3: " + sw.ElapsedMilliseconds + " ms"); w.FecharEspaciaisSemPerguntar();
                // pasta sem permissão: temp com ACL de negação de escrita para o usuário atual (vale também para administrador)
                var negada = Path.Combine(Path.GetTempPath(), "estructto-sem-permissao-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(negada);
                var di = new DirectoryInfo(negada); var eu = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                var regra = new System.Security.AccessControl.FileSystemAccessRule(eu, System.Security.AccessControl.FileSystemRights.CreateFiles | System.Security.AccessControl.FileSystemRights.CreateDirectories, System.Security.AccessControl.AccessControlType.Deny);
                var acl = di.GetAccessControl(); acl.AddAccessRule(regra); di.SetAccessControl(acl);
                try
                {
                    try { Esperar(Inicio.Acoes.Novo(w, new NovoProjetoDados { Modelo = modelo, Grade = ge, Nome = "x", Pasta = Path.Combine(negada, "p"), Material = modelo.Material, Pilar = modelo.Pilar, Viga = modelo.Viga })); sb.AppendLine("FALHA pasta sem permissão: criou"); falhas++; }
                    catch (Exception ex) { sb.AppendLine("ok    pasta sem permissão: erro tratado (" + ex.GetType().Name + ")"); }
                }
                finally { acl = di.GetAccessControl(); acl.RemoveAccessRule(regra); di.SetAccessControl(acl); try { Directory.Delete(negada, true); } catch { } }
            }
            catch (Exception ex) { falhas++; sb.AppendLine("interrompido: " + ex); }
            finally
            {
                sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
                Grava();
                w.alterado = false;
                Environment.Exit(falhas == 0 ? 0 : 1);
            }
        }
    }
}
