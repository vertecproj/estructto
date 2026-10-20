using System.Windows;
using ControlzEx.Theming;

namespace NovoFtool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            base.OnStartup(e);
            ThemeManager.Current.ChangeTheme(this, "Dark.Cobalt");
            TemaPlant.AplicarFluent(this);   // cores do AutoCAD Plant 3D por cima do tema escuro
        }
    }
}
