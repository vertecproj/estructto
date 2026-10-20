// Ponte com o motor de calculo Python (codigo/motor, ex-PROSSEN Estrutural) - docs/UNIFICACAO.md.
// Sobe o servidor local do motor (so 127.0.0.1) e chama as rotas /api/... com JSON (tabela ROTAS em
// codigo/motor/prossen_estrutural/web/servidor.py). Fora de GET o servidor exige Origin da propria porta (CSRF).
// Onde esta o motor: motor\estructto-motor.exe ao lado do Estructto.exe (empacotado) ou, em desenvolvimento,
// a variavel ESTRUCTTO_MOTOR apontando para a pasta codigo\motor (roda "python -m prossen_estrutural.web").
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NovoFtool
{
    public static class Motor
    {
        static Process proc;
        static HttpClient http;
        static string origem;

        public static bool Ligado => proc != null && !proc.HasExited;

        // Pasta de trabalho do servidor (caminhos de abrir/salvar sao relativos a ela).
        public static string Pasta => Path.Combine(Path.GetTempPath(), "Estructto", "trabalho");

        // Quem deixou o modelo atual no servidor (DocumentoEspacial.cs). PUT /api/modelo (ponte 2D) troca o modelo: zera.
        public static object Dono;

        // Chamada unica a uma rota. metodo: GET, POST, PUT, PATCH, DELETE. rota: "/api/calcular", "/api/modal?n=6"...
        // Erro HTTP do motor vira MotorErro com a mensagem que o motor mandou.
        public static async Task<JsonNode> Chamar(string metodo, string rota, object corpo = null)
        {
            await Garantir();
            if (metodo == "PUT") Dono = null;
            var req = new HttpRequestMessage(new HttpMethod(metodo), origem + rota);
            if (metodo != "GET")
            {
                req.Headers.Add("Origin", origem);
                var json = corpo == null ? "null" : corpo is JsonNode n ? n.ToJsonString() : System.Text.Json.JsonSerializer.Serialize(corpo);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            var resp = await http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new MotorErro((int)resp.StatusCode, texto);
            return string.IsNullOrWhiteSpace(texto) ? null : JsonNode.Parse(texto);
        }

        public static Task<JsonNode> Get(string rota) => Chamar("GET", rota);
        public static Task<JsonNode> Post(string rota, object corpo = null) => Chamar("POST", rota, corpo);

        static async Task Garantir()
        {
            if (Ligado) return;
            Dono = null;
            int porta = PortaLivre();
            origem = "http://127.0.0.1:" + porta;
            string pasta = Pasta;
            Directory.CreateDirectory(pasta);
            string args = $"--porta {porta} --pasta \"{pasta}\"";
            var exe = Path.Combine(AppContext.BaseDirectory, "motor", "estructto-motor.exe");
            var dev = Environment.GetEnvironmentVariable("ESTRUCTTO_MOTOR");
            ProcessStartInfo psi;
            if (File.Exists(exe)) psi = new ProcessStartInfo(exe, args);
            else if (!string.IsNullOrEmpty(dev)) psi = new ProcessStartInfo("python", "-m prossen_estrutural.web " + args) { WorkingDirectory = dev };
            else throw new MotorErro(0, "motor de calculo nao encontrado (motor\\estructto-motor.exe ou variavel ESTRUCTTO_MOTOR)");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            proc = Process.Start(psi);
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Debug.WriteLine("motor: " + e.Data); };
            proc.BeginErrorReadLine();
            http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            for (int i = 0; i < 100; i++) // ate ~10 s para o servidor subir
            {
                if (proc.HasExited) throw new MotorErro(0, "motor de calculo encerrou ao iniciar");
                try { if ((await http.GetAsync(origem + "/api/versao")).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new MotorErro(0, "motor de calculo nao respondeu em 10 s");
        }

        public static void Desligar()
        {
            try { if (Ligado) proc.Kill(true); } catch { }
            proc = null;
        }

        static int PortaLivre()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }
    }

    public class MotorErro : Exception
    {
        public int Status;
        public MotorErro(int status, string msg) : base(msg) { Status = status; }
    }
}
