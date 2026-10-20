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
            DispatcherUnhandledException += (s, ex) =>
            {
                if (Capturas.Ativo) { Capturas.GravarErro(ex.Exception); ex.Handled = true; System.Environment.Exit(1); }
            };
            TemaPlant.RegistrarTokens(this);   // tokens antes de a janela abrir (StartupUri)
            base.OnStartup(e);
            ThemeManager.Current.ChangeTheme(this, "Dark.Cobalt");
            TemaPlant.AplicarFluent(this);   // cores do AutoCAD Plant 3D por cima do tema escuro
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Motor.Desligar();
            base.OnExit(e);
        }
    }
}
