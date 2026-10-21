using System.Linq;
using System.Windows;
using ControlzEx.Theming;

namespace NovoFtool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Estructto.exe --conferir-motor <saida.txt> <arq.estructto>... : interno x motor Python (docs/UNIFICACAO-MOTOR.md)
            if (e.Args.Length >= 2 && e.Args[0] == "--conferir-motor")
            {
                int cod = 0;
                try { System.IO.File.WriteAllText(e.Args[1], System.Threading.Tasks.Task.Run(() => MotorModelo.Conferir(e.Args.Skip(2))).GetAwaiter().GetResult()); }
                catch (System.Exception ex) { System.IO.File.WriteAllText(e.Args[1], "ERRO: " + ex); cod = 1; }
                Motor.Desligar();
                System.Environment.Exit(cod);
            }
            // Estructto.exe --testes-edicao <saida.txt> : geometria da edição do modelo espacial (EdicaoEspacial.cs)
            if (e.Args.Length >= 2 && e.Args[0] == "--testes-edicao")
            {
                string r = TestesEdicao.Rodar();
                System.IO.File.WriteAllText(e.Args[1], r);
                System.Environment.Exit(r.Contains("TODOS PASSARAM") ? 0 : 1);
            }
            // Estructto.exe --testes-snap <saida.txt> : motor de snap e medidas da Vista3D (Snap3D.cs, Medir3D.cs)
            if (e.Args.Length >= 2 && e.Args[0] == "--testes-snap")
            {
                string r;
                try { r = TestesSnap.Rodar(); } catch (System.Exception ex) { r = "ERRO: " + ex; }
                System.IO.File.WriteAllText(e.Args[1], r);
                System.Environment.Exit(r.Contains("TODOS PASSARAM") ? 0 : 1);
            }
            // Estructto.exe --testes-navegacao <saida.txt> : pivô automático, órbita e zoom no cursor da Vista3D (Vista3D.Navegacao.cs)
            if (e.Args.Length >= 2 && e.Args[0] == "--testes-navegacao")
            {
                string r;
                try { r = TestesNavegacao.Rodar(); } catch (System.Exception ex) { r = "ERRO: " + ex; }
                System.IO.File.WriteAllText(e.Args[1], r);
                System.Environment.Exit(r.Contains("TODOS PASSARAM") ? 0 : 1);
            }
            // Estructto.exe --testes-exportar <pasta> [raiz do repo] : DWG/DXF/STEP do guincho e do edifício (Exportar.cs)
            if (e.Args.Length >= 2 && e.Args[0] == "--testes-exportar")
            {
                string r;
                try { r = TestesExportar.Rodar(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : System.IO.Directory.GetCurrentDirectory()); }
                catch (System.Exception ex) { r = "ERRO: " + ex; }
                System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1], "testes-exportar.txt"), r);
                System.Environment.Exit(r.Contains("TODOS PASSARAM") ? 0 : 1);
            }
            DispatcherUnhandledException += (s, ex) =>
            {
                if (Capturas.Ativo) { Capturas.GravarErro(ex.Exception); ex.Handled = true; System.Environment.Exit(1); }
            };
            TemaPlant.RegistrarTokens(this);   // tokens antes de a janela abrir (StartupUri)
            base.OnStartup(e);
            ThemeManager.Current.ChangeTheme(this, "Dark.Cobalt");
            TemaPlant.AplicarFluent(this);   // cores do AutoCAD Plant 3D por cima do tema escuro
            if (!e.Args.Contains("--capturas")) TemaPlant.AplicarAcento(this, Config.Opcoes.Acento);   // Arquivo › Opções
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Motor.Desligar();
            base.OnExit(e);
        }
    }
}
