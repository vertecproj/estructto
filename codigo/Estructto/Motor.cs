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
            if (TrocaModelo(metodo, rota)) Dono = null;
            var req = new HttpRequestMessage(new HttpMethod(metodo), origem + rota);
            if (metodo != "GET")
            {
                req.Headers.Add("Origin", origem);
                // o servidor recusa o corpo "null" (400 json_invalido): sem corpo no DELETE, "{}" nos demais
                if (corpo != null || metodo != "DELETE")
                {
                    var json = corpo == null ? "{}" : corpo is JsonNode n ? n.ToJsonString() : System.Text.Json.JsonSerializer.Serialize(corpo);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }
            }
            var resp = await http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new MotorErro((int)resp.StatusCode, texto);
            return string.IsNullOrWhiteSpace(texto) ? null : JsonNode.Parse(texto);
        }

        // Rotas que trocam o modelo inteiro do servidor: quem estava nele (Dono) deixa de estar.
        static bool TrocaModelo(string metodo, string rota)
        {
            string r = rota.Split('?')[0];
            return (metodo == "PUT" && r == "/api/modelo") || (metodo == "POST" && (r == "/api/importar_cype" || r == "/api/modelo/abrir"
                || r == "/api/modelo/novo" || r.StartsWith("/api/exemplos/")));
        }

        // Exclusao mutua entre operacoes compostas (ponte 2D: Enviar + calcular + ler resultados; documento espacial:
        // abrir/editar + calcular + ler). Sem isso um PUT de um caminho cai entre o /api/calcular e os GET do outro.
        // Uso: await Motor.Exclusivo(async () => { ...varias chamadas Motor.*... }). Nao reentrante: nao aninhar.
        static readonly System.Threading.SemaphoreSlim fila = new System.Threading.SemaphoreSlim(1, 1);
        public static async Task<T> Exclusivo<T>(Func<Task<T>> op)
        {
            await fila.WaitAsync();
            try { await Garantir(); return await op(); }
            finally { fila.Release(); }
        }
        public static Task Exclusivo(Func<Task> op) => Exclusivo(async () => { await op(); return true; });

        public static Task<JsonNode> Get(string rota) => Chamar("GET", rota);
        public static Task<JsonNode> Post(string rota, object corpo = null) => Chamar("POST", rota, corpo);

        // Uma partida por vez: chamadas simultaneas antes de o servidor responder esperam a mesma Task
        // (antes a segunda via Ligado = true e chamava a porta antes de o servidor subir).
        static readonly object trava = new object();
        static Task partida;
        static Task Garantir()
        {
            lock (trava)
            {
                if (partida == null || partida.IsFaulted || (partida.IsCompleted && !Ligado)) partida = Iniciar();
                return partida;
            }
        }

        static async Task Iniciar()
        {
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
            else if (!string.IsNullOrEmpty(dev)) psi = new ProcessStartInfo("python", "-m prossen_estrutural.web " + args + " --pai " + Environment.ProcessId) { WorkingDirectory = dev };   // --pai so no fonte: o exe empacotado antigo nao conhece a opcao (fica com o Job)
            else throw new MotorErro(0, "motor de calculo nao encontrado (motor\\estructto-motor.exe ou variavel ESTRUCTTO_MOTOR)");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            proc = Process.Start(psi);
            Job.Prender(proc);
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Debug.WriteLine("motor: " + e.Data); };
            proc.BeginErrorReadLine();
            http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            for (int i = 0; i < 600; i++) // ate ~60 s para o servidor subir (exe PyInstaller frio, antivirus)
            {
                if (proc.HasExited) throw new MotorErro(0, "motor de calculo encerrou ao iniciar");
                try { if ((await http.GetAsync(origem + "/api/versao")).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Desligar();   // nao deixa o processo que nao respondeu rodando (a proxima chamada tenta de novo)
            throw new MotorErro(0, "motor de calculo nao respondeu em 60 s");
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

    // Job Object com KILL_ON_JOB_CLOSE: se o Estructto morrer (ate a forca), o Windows mata o motor e os filhos dele
    // (o "python" do instalador e um lancador que abre outro python.exe). O --pai no servidor cobre o resto.
    static class Job
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr CreateJobObject(IntPtr a, string nome);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int classe, byte[] info, int tam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
        static IntPtr job;

        public static void Prender(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var info = new byte[IntPtr.Size == 8 ? 144 : 112];   // JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                    BitConverter.GetBytes(0x2000).CopyTo(info, 16);      // LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    SetInformationJobObject(job, 9, info, info.Length);
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch { }   // sem job: o --pai do servidor ainda encerra o motor
        }
    }

    public class MotorErro : Exception
    {
        public int Status;
        public MotorErro(int status, string msg) : base(msg) { Status = status; }
    }
}
