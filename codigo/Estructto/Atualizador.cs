// Atualização automática (docs/v3/ATUALIZACAO.md): procura a release nova no GitHub, baixa o .zip, confere o SHA-256 publicado
// junto, extrai em %TEMP% e entrega a um script PowerShell que troca a pasta do programa depois que o Estructto fechar.
// Regra de segurança: sem <zip>.sha256 batendo, nada é instalado nem executado.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NovoFtool
{
    /// <summary>Versão semântica simples: 0.4, v0.5.1, 0.5-beta.1 (build após + é ignorado).</summary>
    public sealed class Ver : IComparable<Ver>
    {
        public int[] N; public string[] Pre = new string[0];
        public bool PreRelease { get { return Pre.Length > 0; } }
        public static bool TryParse(string s, out Ver v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim(); if (s[0] == 'v' || s[0] == 'V') s = s.Substring(1);
            int mais = s.IndexOf('+'); if (mais >= 0) s = s.Substring(0, mais);
            string pre = ""; int tr = s.IndexOf('-'); if (tr >= 0) { pre = s.Substring(tr + 1); s = s.Substring(0, tr); }
            var partes = s.Split('.'); var n = new int[partes.Length];
            if (partes.Length > 4) return false;
            for (int i = 0; i < partes.Length; i++) if (!int.TryParse(partes[i], out n[i]) || n[i] < 0) return false;
            v = new Ver { N = n, Pre = pre == "" ? new string[0] : pre.Split('.') };
            return true;
        }
        public int CompareTo(Ver o)
        {
            for (int i = 0; i < Math.Max(N.Length, o.N.Length); i++)
            {
                int a = i < N.Length ? N[i] : 0, b = i < o.N.Length ? o.N[i] : 0;
                if (a != b) return a < b ? -1 : 1;
            }
            if (!PreRelease || !o.PreRelease) return PreRelease == o.PreRelease ? 0 : PreRelease ? -1 : 1;   // 0.5 > 0.5-beta
            for (int i = 0; i < Math.Min(Pre.Length, o.Pre.Length); i++)
            {
                bool na = int.TryParse(Pre[i], out int x), nb = int.TryParse(o.Pre[i], out int y);
                int c = na && nb ? x.CompareTo(y) : na ? -1 : nb ? 1 : string.CompareOrdinal(Pre[i], o.Pre[i]);
                if (c != 0) return c < 0 ? -1 : 1;
            }
            return Pre.Length.CompareTo(o.Pre.Length);
        }
        public override string ToString() { return string.Join(".", N) + (PreRelease ? "-" + string.Join(".", Pre) : ""); }
    }

    public sealed class ReleaseInfo
    {
        public string Tag, Notas, PaginaUrl, ZipNome, ZipUrl, ShaUrl; public Ver Versao; public long ZipBytes; public bool Pre;   // Pre = flag "prerelease" do GitHub
    }

    public static class Atualizador
    {
        const string UrlReleases = "https://api.github.com/repos/vertecproj/estructto/releases?per_page=30";
        public static readonly string PastaTemp = Path.Combine(Path.GetTempPath(), "Estructto-update");

        // AssemblyInformationalVersion = tag da release (publicar.ps1 passa -p:Version); o SDK pode acrescentar +commit.
        public static string VersaoAtual
        {
            get
            {
                string v = typeof(Atualizador).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(v)) return "0.0";
                int i = v.IndexOf('+'); return i >= 0 ? v.Substring(0, i) : v;
            }
        }

        // Só baixamos de github.com e dos hosts de download dele; o JSON nunca escolhe outro destino.
        public static bool HostPermitido(Uri u)
        {
            if (u == null || u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h == "api.github.com" || h.EndsWith(".githubusercontent.com");
        }

        /// <summary>Maior release do JSON de /releases (lista) que seja mais nova que a atual; null se não houver.</summary>
        public static ReleaseInfo MaisNova(string json, string atual)
        {
            if (!Ver.TryParse(atual, out Ver va)) return null;
            ReleaseInfo melhor = null;
            foreach (var r in ParseReleases(json))
            {
                if ((r.Versao.PreRelease || r.Pre) && !va.PreRelease) continue;   // pré-release só para quem já está em pré-release
                if (r.Versao.CompareTo(va) <= 0) continue;
                if (melhor == null || r.Versao.CompareTo(melhor.Versao) > 0) melhor = r;
            }
            return melhor;
        }

        public static List<ReleaseInfo> ParseReleases(string json)
        {
            var lista = new List<ReleaseInfo>();
            using (var doc = JsonDocument.Parse(json))
            {
                var itens = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
                foreach (var e in itens)
                {
                    if (Bool(e, "draft")) continue;
                    string tag = Str(e, "tag_name");
                    if (!Ver.TryParse(tag, out Ver v)) continue;
                    var r = new ReleaseInfo { Tag = tag, Versao = v, Notas = Str(e, "body"), PaginaUrl = Str(e, "html_url"), Pre = Bool(e, "prerelease") };
                    if (e.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        foreach (var a in assets.EnumerateArray())
                        {
                            string nome = Str(a, "name"), url = Str(a, "browser_download_url");
                            if (nome.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && r.ZipNome == null && UrlDaRelease(url))
                            { r.ZipNome = nome; r.ZipUrl = url; r.ZipBytes = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long b) ? b : 0; }
                        }
                    if (r.ZipNome == null) continue;
                    if (e.TryGetProperty("assets", out assets))
                        foreach (var a in assets.EnumerateArray())
                            if (string.Equals(Str(a, "name"), r.ZipNome + ".sha256", StringComparison.OrdinalIgnoreCase) && UrlDaRelease(Str(a, "browser_download_url"))) r.ShaUrl = Str(a, "browser_download_url");
                    lista.Add(r);
                }
            }
            return lista;
        }
        // O JSON só vale se os assets forem do repositório oficial; outros hosts só entram como redirecionamento do próprio GitHub.
        const string PrefixoDownload = "https://github.com/vertecproj/estructto/releases/download/";
        public static bool UrlDaRelease(string url) { return url != null && url.StartsWith(PrefixoDownload, StringComparison.Ordinal); }
        static string Str(JsonElement e, string k) { return e.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : ""; }
        static bool Bool(JsonElement e, string k) { return e.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.True; }

        static HttpClient Cliente()
        {
            var c = new HttpClient();
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Estructto-Atualizador/" + VersaoAtual);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Consulta o GitHub (timeout 5 s). Lança em falha de rede; quem chama decide se é silencioso.</summary>
        public static async Task<ReleaseInfo> ProcurarAsync(string atual)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using (var c = Cliente())
            {
                string json = await c.GetStringAsync(UrlReleases, cts.Token).ConfigureAwait(false);
                return MaisNova(json, atual);
            }
        }

        /// <summary>null = hash confere; senão a mensagem de erro. Aceita "hex" ou "hex  arquivo" (sha256sum).</summary>
        public static string ConferirHash(string arquivo, string textoSha, string nomeZip)
        {
            if (string.IsNullOrWhiteSpace(textoSha)) return "o arquivo .sha256 da release está vazio ou não existe";
            var tok = Tokens(textoSha);
            string esperado = tok[0].ToLowerInvariant();
            if (esperado.Length != 64 || !esperado.All(Uri.IsHexDigit)) return "o arquivo .sha256 não tem um SHA-256 válido";
            if (tok.Length > 1 && !string.Equals(tok[1].TrimStart('*'), nomeZip, StringComparison.OrdinalIgnoreCase)) return "o .sha256 é de outro arquivo (" + tok[1] + ")";
            string real;
            using (var s = File.OpenRead(arquivo)) real = BitConverter.ToString(SHA256.HashData(s)).Replace("-", "").ToLowerInvariant();
            return real == esperado ? null : "SHA-256 do download não confere com o publicado; nada foi instalado";
        }

        static string[] Tokens(string textoSha) => textoSha.Split('\n').Select(l => l.Trim()).First(l => l != "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>SHA-256 publicado (hex minúsculo): vai no comando do script e é conferido de novo na pasta protegida.</summary>
        public static string HashEsperado(string textoSha) => Tokens(textoSha)[0].ToLowerInvariant();

        /// <summary>Baixa url para destino (.part → renomeia), com progresso 0..1 (ou -1 se tamanho desconhecido).</summary>
        public static async Task BaixarAsync(string url, string destino, IProgress<double> prog, CancellationToken ct)
        {
            if (!UrlDaRelease(url)) throw new InvalidOperationException("endereço de download não permitido: " + url);
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            string part = destino + ".part";
            using (var c = Cliente())
            {
                c.Timeout = Timeout.InfiniteTimeSpan;
                using (var r = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    r.EnsureSuccessStatusCode();
                    if (!HostPermitido(r.RequestMessage.RequestUri)) throw new InvalidOperationException("redirecionamento para host não permitido: " + r.RequestMessage.RequestUri.Host);
                    long total = r.Content.Headers.ContentLength ?? -1, lido = 0;
                    using (var ent = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var sai = File.Create(part))
                    {
                        var buf = new byte[81920]; int n;
                        while ((n = await ent.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        { sai.Write(buf, 0, n); lido += n; prog?.Report(total > 0 ? (double)lido / total : -1); }
                    }
                }
            }
            if (File.Exists(destino)) File.Delete(destino);
            File.Move(part, destino);
        }

        public static async Task<string> BaixarTextoAsync(string url, CancellationToken ct)
        {
            if (!UrlDaRelease(url)) throw new InvalidOperationException("endereço não permitido: " + url);
            using (var c = Cliente()) return await c.GetStringAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>Extrai o zip em pasta vazia e devolve a pasta "Estructto" (a que tem Estructto.exe e motor\). Lança se faltar algo.</summary>
        public static string Extrair(string zip, string pasta)
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            ZipFile.ExtractToDirectory(zip, pasta);   // recusa entradas que escapam da pasta (zip-slip)
            string raiz = Directory.Exists(Path.Combine(pasta, "Estructto")) ? Path.Combine(pasta, "Estructto") : pasta;
            if (!File.Exists(Path.Combine(raiz, "Estructto.exe")) || !File.Exists(Path.Combine(raiz, "motor", "estructto-motor.exe")))
                throw new InvalidDataException("o pacote não tem Estructto.exe e motor\\estructto-motor.exe");
            return raiz;
        }

        // Roda depois que o Estructto fechou. Nada é lido do pacote além de copiar arquivos; configurações do usuário ficam em
        // %AppData%\NovoFtool (fora da pasta de instalação) e nunca são tocadas.
        public const string ScriptAtualizador = @"param(
  [Parameter(Mandatory=$true)][int]$ProcessoPid,
  [Parameter(Mandatory=$true)][string]$Origem,
  [Parameter(Mandatory=$true)][string]$Destino,
  [Parameter(Mandatory=$true)][string]$Exe,
  [string]$Limpar = '',
  [int]$EsperaMs = 120000,
  [string]$Zip = '',
  [string]$Sha = '',
  [switch]$Elevado,
  [switch]$DumpStub)
$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:TEMP 'Estructto-update'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'atualizador.log'
function Log([string]$m) { try { Add-Content -LiteralPath $log -Value ('{0:s} {1}' -f (Get-Date), $m) } catch {} }
$Destino = $Destino.TrimEnd('\')
$bak = $Destino + '.bak'
$falhou = $Destino + '.failed'
function Reabrir { if ($Elevado) { Start-Process explorer.exe -ArgumentList ('""' + $Exe + '""') } else { Start-Process -FilePath $Exe -WorkingDirectory $Destino } }
function PodeEscrever([string]$pasta) {
  $t = Join-Path $pasta ('.escrita-' + [guid]::NewGuid().ToString('N'))
  try { [IO.File]::WriteAllText($t, 'x'); Remove-Item -LiteralPath $t -Force; return $true } catch { return $false }
}
# valores vao em base64 (sem escape de aspas, inclusive aspas Unicode); -Limpar so entra se nao vazio
function V([string]$s) { ""([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) + ""')))"" }
function NovoStub {
  $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($PSCommandPath)))
  $l = ''; if ($Limpar -ne '') { $l = ' -Limpar ' + (V $Limpar) }
  ""`$t = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$b64')); & ([scriptblock]::Create(`$t)) -ProcessoPid $ProcessoPid -Origem $(V $Origem) -Destino $(V $Destino) -Exe $(V $Exe)$l -EsperaMs $EsperaMs -Zip $(V $Zip) -Sha $(V $Sha) -Elevado""
}
# Elevado: nada vem da pasta do usuario (%TEMP% pode ser trocado antes de o admin ler). O zip vai para uma pasta nova criada ja
# com ACL explicita (sem heranca; Administradores + SYSTEM), o SHA-256 e conferido LA contra o hash do comando e so entao o
# pacote e extraido ali. Sem ser admin (simulacao dos testes) o usuario atual entra na ACL; a conferencia e a mesma.
$segura = Join-Path $env:SystemRoot ('Temp\Estructto-update-' + [guid]::NewGuid().ToString('N'))
function PastaSegura {
  if ($Zip -eq '' -or $Sha -notmatch '^[0-9a-fA-F]{64}$') { throw 'elevacao sem zip e SHA-256 esperado: nada instalado' }
  $seg = $segura
  if (Test-Path -LiteralPath $seg) { throw 'pasta protegida ja existe: nada instalado' }
  $ds = New-Object Security.AccessControl.DirectorySecurity
  $ds.SetAccessRuleProtection($true, $false)
  $sids = @('S-1-5-32-544', 'S-1-5-18')
  $eu = [Security.Principal.WindowsIdentity]::GetCurrent()
  if (-not (New-Object Security.Principal.WindowsPrincipal $eu).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { $sids += $eu.User.Value }
  foreach ($s in $sids) { $ds.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier $s), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))) }
  [IO.Directory]::CreateDirectory($seg, $ds) | Out-Null
  $zs = Join-Path $seg 'pacote.zip'
  Copy-Item -LiteralPath $Zip -Destination $zs
  $h = (Get-FileHash -LiteralPath $zs -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($h -ne $Sha.ToLowerInvariant()) { throw 'SHA-256 do pacote na pasta protegida nao confere: nada instalado' }
  Log 'SHA-256 conferido na pasta protegida'
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $ex = Join-Path $seg 'pacote'
  [IO.Compression.ZipFile]::ExtractToDirectory($zs, $ex)
  $r = $ex; if (Test-Path -LiteralPath (Join-Path $ex 'Estructto')) { $r = Join-Path $ex 'Estructto' }
  if (-not (Test-Path -LiteralPath (Join-Path $r 'motor\estructto-motor.exe'))) { throw 'pacote sem motor\estructto-motor.exe' }
  return $r
}
if ($DumpStub) { [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes((NovoStub))); exit 0 }
$tocou = $false
try {
  Log ('inicio pid=' + $ProcessoPid + ' destino=' + $Destino + ' elevado=' + $Elevado)
  $p = Get-Process -Id $ProcessoPid -ErrorAction SilentlyContinue
  if ($p -and -not $p.WaitForExit($EsperaMs)) { Log 'o Estructto nao fechou a tempo: nada alterado, nao reabre'; exit 2 }
  Get-Process estructto-motor -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Destino + '\', [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  if (-not ((PodeEscrever $Destino) -and (PodeEscrever (Split-Path -Parent $Destino)))) {
    if ($Elevado) { throw 'sem permissao de escrita mesmo como administrador' }
    Log 'sem permissao de escrita: pedindo elevacao'
    # nenhum arquivo de script na elevacao (pasta gravavel pelo usuario = troca antes de rodar como admin): o script inteiro vai dentro do -EncodedCommand
    $stub = NovoStub
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($stub))
    Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-EncodedCommand',$enc) -WindowStyle Hidden -Wait
    exit 0
  }
  if ($Elevado) { $Origem = PastaSegura }
  if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Recurse -Force }
  if (Test-Path -LiteralPath $falhou) { Remove-Item -LiteralPath $falhou -Recurse -Force }
  Copy-Item -LiteralPath $Destino -Destination $bak -Recurse -Force
  Log ('backup em ' + $bak)
  $tocou = $true
  if (Test-Path -LiteralPath (Join-Path $Destino 'motor')) { Remove-Item -LiteralPath (Join-Path $Destino 'motor') -Recurse -Force }
  Get-ChildItem -LiteralPath $Origem -Force | Copy-Item -Destination $Destino -Recurse -Force
  if (-not (Test-Path -LiteralPath (Join-Path $Destino 'motor\estructto-motor.exe'))) { throw 'copia incompleta (sem motor\estructto-motor.exe)' }
  Log 'atualizado'
  if ($Elevado) { try { if (Test-Path -LiteralPath $segura) { Remove-Item -LiteralPath $segura -Recurse -Force } } catch {} }
  $tocou = $false
  if ($Limpar -ne '' -and $Limpar.StartsWith($logDir + '\', [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $Limpar -Recurse -Force -ErrorAction SilentlyContinue }
  Reabrir
} catch {
  Log ('ERRO: ' + $_.Exception.Message)
  if ($Elevado) { try { if (Test-Path -LiteralPath $segura) { Remove-Item -LiteralPath $segura -Recurse -Force } } catch {} }
  if ($tocou) {
    try {
      if (Test-Path -LiteralPath $falhou) { Remove-Item -LiteralPath $falhou -Recurse -Force }
      Move-Item -LiteralPath $Destino -Destination $falhou
      Move-Item -LiteralPath $bak -Destination $Destino
      Log 'restaurado do backup (pasta com falha em .failed)'
      Remove-Item -LiteralPath $falhou -Recurse -Force -ErrorAction SilentlyContinue
    } catch { Log ('FALHA ao restaurar: ' + $_.Exception.Message) }
  }
  try { Reabrir } catch { Log ('nao reabriu: ' + $_.Exception.Message) }
  exit 1
}
";

        // Close() cancelado depois de o script iniciar: o script não pode atualizar nem reabrir uma segunda cópia.
        public static void CancelarScript(Process p) { try { if (p != null && !p.HasExited) p.Kill(); } catch { } }

        /// <summary>Grava o script (UTF-8 com BOM, para o PowerShell 5.1) e o dispara oculto. Devolve o processo.</summary>
        public static string GravarScript(string pastaScript)
        {
            Directory.CreateDirectory(pastaScript);
            string script = Path.Combine(pastaScript, "atualizar-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(script, ScriptAtualizador, new UTF8Encoding(true));
            return script;
        }

        public static Process IniciarScript(int pid, string origem, string destino, string exe, string limpar, string pastaScript = null, int esperaMs = 120000, string zip = "", string sha = "")
        {
            string script = GravarScript(pastaScript ?? PastaTemp);
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -ProcessoPid " + pid + " -Origem \"" + origem + "\" -Destino \"" + destino.TrimEnd('\\') + "\" -Exe \"" + exe + "\"" + (string.IsNullOrEmpty(limpar) ? "" : " -Limpar \"" + limpar + "\"") + " -EsperaMs " + esperaMs + (string.IsNullOrEmpty(zip) ? "" : " -Zip \"" + zip + "\" -Sha " + sha))
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(psi);
        }
    }
}
