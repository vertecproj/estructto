// B5 v0.6: comando NO (botão Nó em Desenhar): insere nós por clique (com atração) ou coordenada digitada x,y,z; Enter termina.
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace NovoFtool
{
    public partial class MainWindow
    {
        void CmdNo()
        {
            if (Esp == null || vista3D == null) { MostrarToast("Nenhum modelo aberto: Ctrl+N cria um em branco.", true); return; }
            vista3D.PedirPonto("NÓ — clique o ponto ou digite x,y,z (Enter termina)", null, async p =>
            {
                await Espacial(async d =>
                {
                    string id = await d.Adicionar("no", new JsonObject { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z });
                    if (id != null) selEsp = d.Ref("no", id);
                }, "Nó novo em " + P3(p) + " m.");
                return true;   // continua pedindo pontos até Enter/Esc
            }, () => Log("NÓ terminado."));
        }
    }
}
