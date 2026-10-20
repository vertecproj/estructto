using System.Windows;
using ControlzEx.Theming;

namespace NovoFtool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, ex) =>
            {
                if (Capturas.Ativo) { Capturas.GravarErro(ex.Exception); ex.Handled = true; System.Environment.Exit(1); }
            };
            TemaPlant.RegistrarTokens(this);   // tokens antes de a janela abrir (StartupUri)
            base.OnStartup(e);
            ThemeManager.Current.ChangeTheme(this, "Dark.Cobalt");
            TemaPlant.AplicarFluent(this);   // cores do AutoCAD Plant 3D por cima do tema escuro
        }
    }
}
