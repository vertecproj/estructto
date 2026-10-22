// B4 v0.6: Avisos e Tabelas nascem fechados; o que o usuário abrir/fechar fica lembrado (opcoes.txt: painel_tabelas, painel_avisos).
using System.ComponentModel;
using System.Windows.Threading;
using AvalonDock.Layout;

namespace NovoFtool
{
    public partial class MainWindow
    {
        bool restaurandoPaineis;

        void LigarEstadoPaineis()
        {
            Loaded += (s, e) => Dispatcher.BeginInvoke(new System.Action(() =>
            {
                restaurandoPaineis = true;
                foreach (var (p, chave) in new[] { (PainelTabelas, "painel_tabelas"), (PainelAvisos, "painel_avisos") })
                    if (Config.Opcoes.Get(chave, "0") != "1" && p.IsVisible) p.Hide();
                restaurandoPaineis = false;
                foreach (var (p, chave) in new[] { (PainelTabelas, "painel_tabelas"), (PainelAvisos, "painel_avisos") })
                {
                    string k = chave; LayoutAnchorable pp = p;
                    pp.PropertyChanged += (o, a) => { if (!restaurandoPaineis && !Capturas.Ativo && a.PropertyName == nameof(LayoutAnchorable.IsVisible)) Config.Opcoes.Set(k, pp.IsVisible ? "1" : "0"); };
                }
            }), DispatcherPriority.Loaded);
        }
    }
}
