// Estructto.exe --testes-atualizacao <pasta>: atualizador sem rede (fixtures locais). Resumo em <pasta>/testes-atualizacao.txt
// (docs/v3/ATUALIZACAO.md). Cobre versões, JSON de release, SHA-256, zip, script (execução real em pastas temporárias) e a faixa.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace NovoFtool
{
    public static partial class Capturas
    {
        const string FixtureReleases = @"[
 {""tag_name"":""v0.99"",""draft"":true,""prerelease"":false,""html_url"":""https://github.com/vertecproj/estructto/releases/tag/v0.99"",""assets"":[{""name"":""Estructto-0.99-win-x64.zip"",""browser_download_url"":""https://github.com/x/y.zip""}]},
 {""tag_name"":""v0.11-beta.1"",""draft"":false,""prerelease"":true,""html_url"":""https://github.com/vertecproj/estructto/releases/tag/v0.11-beta.1"",""assets"":[{""name"":""Estructto-0.11-beta.1-win-x64.zip"",""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.11-beta.1/b.zip""}]},
 {""tag_name"":""v0.10"",""draft"":false,""prerelease"":false,""body"":""Notas da 0.10"",""html_url"":""https://github.com/vertecproj/estructto/releases/tag/v0.10"",""assets"":[
   {""name"":""Estructto-0.10-win-x64.zip"",""size"":70000000,""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.10/Estructto-0.10-win-x64.zip""},
   {""name"":""Estructto-0.10-win-x64.zip.sha256"",""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.10/Estructto-0.10-win-x64.zip.sha256""}]},
 {""tag_name"":""v0.12"",""draft"":false,""prerelease"":true,""assets"":[{""name"":""Estructto-0.12-win-x64.zip"",""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.12/Estructto-0.12-win-x64.zip""}]},
 {""tag_name"":""v0.7"",""draft"":false,""prerelease"":false,""assets"":[{""name"":""Estructto-0.7-win-x64.zip"",""browser_download_url"":""https://evil.example.com/vertecproj/estructto/releases/download/v0.7/Estructto-0.7-win-x64.zip""}]},
 {""tag_name"":""v0.8"",""draft"":false,""prerelease"":false,""assets"":[{""name"":""Estructto-0.8-win-x64.zip"",""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.8/Estructto-0.8-win-x64.zip""},{""name"":""Estructto-0.8-win-x64.zip.sha256"",""browser_download_url"":""https://evil.example.com/x.sha256""}]},
 {""tag_name"":""v0.6"",""draft"":false,""prerelease"":false,""assets"":[{""name"":""Estructto-0.6-win-x64.zip"",""browser_download_url"":""https://github.com/vertecproj/estructto/releases/download/v0.6/r.zip""}]},
 {""tag_name"":""v0.5"",""draft"":false,""prerelease"":false,""assets"":[]},
 {""tag_name"":""nao-e-versao"",""draft"":false,""prerelease"":false,""assets"":[]}
]";

        internal static void ConfigurarAtualizacao(MainWindow w, string pasta)
        {
            pastaSaida = Path.GetFullPath(pasta);
            Directory.CreateDirectory(pastaSaida);
            Ativo = true;
            w.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            w.Left = -10000; w.Top = -10000; w.Width = 1600; w.Height = 900;
            w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Loaded += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => RodarAtualizacao(w)), DispatcherPriority.ApplicationIdle);
        }

        // Captura da faixa para o checklist visual (chamada por Executar).
        static void CapturaAtualizacao(MainWindow w)
        {
            try
            {
                w.AplicarAtualizacao(Atualizador.MaisNova(FixtureReleases, "0.4"), true);
                EsperarIdle();
                CapturarPrincipal(w, "atualizacao-faixa");
            }
            catch (Exception ex) { File.AppendAllText(Path.Combine(pastaSaida, "erros.txt"), "atualizacao-faixa: " + ex.Message + Environment.NewLine); }
            finally { w.EsconderFaixaAtualiza(); }
        }

        static void RodarAtualizacao(MainWindow w)
        {
            var sb = new StringBuilder(); int falhas = 0;
            void Ok(bool cond, string nome) { if (!cond) falhas++; sb.AppendLine((cond ? "ok    " : "FALHA ") + nome); }
            void Lanca(Action a, string nome) { try { a(); Ok(false, nome + " (não lançou)"); } catch (Exception) { Ok(true, nome); } }
            Ver V(string s) { Ver.TryParse(s, out Ver v); return v; }
            string tmp = Path.Combine(Path.GetTempPath(), "estructto-teste-atualizacao-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                // versões
                Ok(V("0.4").CompareTo(V("0.5")) < 0, "0.4 < 0.5");
                Ok(V("0.5").CompareTo(V("0.10")) < 0, "0.5 < 0.10 (numérico, não texto)");
                Ok(V("v0.5").CompareTo(V("0.5.0")) == 0, "v0.5 == 0.5.0");
                Ok(V("0.5-beta.1").CompareTo(V("0.5")) < 0, "0.5-beta.1 < 0.5");
                Ok(V("0.5-beta.2").CompareTo(V("0.5-beta.10")) < 0, "beta.2 < beta.10");
                Ok(V("0.5-rc.1").CompareTo(V("0.5-beta.9")) > 0, "rc > beta");
                Ok(V("0.4.1").CompareTo(V("0.5-beta.1")) < 0, "0.4.1 < 0.5-beta.1");
                Ok(!Ver.TryParse("abc", out _) && !Ver.TryParse("", out _) && !Ver.TryParse("1.x", out _), "versão inválida recusada");
                // JSON de release
                var lista = Atualizador.ParseReleases(FixtureReleases);
                Ok(lista.Count == 5, "parse: rascunho, tag sem versão, sem zip e zip de URL estranha ignorados (" + lista.Count + " de 5 esperadas)");
                var r10 = lista.First(r => r.Tag == "v0.10");
                Ok(r10.ZipNome == "Estructto-0.10-win-x64.zip" && r10.ShaUrl != null && r10.ShaUrl.EndsWith(".zip.sha256") && r10.ZipBytes == 70000000 && r10.Notas == "Notas da 0.10", "parse: zip, sha256, tamanho e notas da v0.10");
                Ok(lista.First(r => r.Tag == "v0.6").ShaUrl == null, "parse: release sem .sha256 fica com ShaUrl nulo");
                Ok(Atualizador.MaisNova(FixtureReleases, "0.4")?.Tag == "v0.10", "0.4 → v0.10 (v0.12 marcada prerelease no GitHub não vale para estável)");
                Ok(!lista.Any(r => r.Tag == "v0.7") && lista.First(r => r.Tag == "v0.8").ShaUrl == null, "URL de asset fora de github.com/vertecproj/estructto/releases/download/ rejeitada (zip: release ignorada; sha: nulo)");
                Ok(Atualizador.MaisNova(FixtureReleases, "0.10") == null, "0.10 → nada");
                Ok(Atualizador.MaisNova(FixtureReleases, "0.5-beta.1")?.Tag == "v0.12", "build pré-release vê pré-release (flag do GitHub)");
                Ok(Atualizador.MaisNova("[]", "0.4") == null, "lista vazia → nada");
                Ok(Atualizador.HostPermitido(new Uri("https://github.com/a")) && Atualizador.HostPermitido(new Uri("https://objects.githubusercontent.com/a")) && !Atualizador.HostPermitido(new Uri("http://github.com/a")) && !Atualizador.HostPermitido(new Uri("https://github.com.evil.com/a")) && !Atualizador.HostPermitido(new Uri("https://evil.com/a")), "hosts de download permitidos / recusados");
                // SHA-256
                string arq = Path.Combine(tmp, "Estructto-0.10-win-x64.zip");
                File.WriteAllBytes(arq, Encoding.UTF8.GetBytes("conteudo do pacote"));
                string hex; using (var s = File.OpenRead(arq)) hex = BitConverter.ToString(SHA256.Create().ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                string nome = Path.GetFileName(arq);
                Ok(Atualizador.ConferirHash(arq, hex, nome) == null, "hash correto aceita");
                Ok(Atualizador.ConferirHash(arq, hex.ToUpperInvariant() + "\r\n", nome) == null, "hash maiúsculo com CRLF aceita");
                Ok(Atualizador.ConferirHash(arq, hex + "  " + nome + "\n", nome) == null, "formato sha256sum (hash  arquivo) aceita");
                Ok(Atualizador.ConferirHash(arq, hex + " *" + nome, nome) == null, "formato sha256sum binário (*arquivo) aceita");
                Ok(Atualizador.ConferirHash(arq, new string('0', 64), nome) != null, "hash errado recusa");
                Ok(Atualizador.ConferirHash(arq, null, nome) != null && Atualizador.ConferirHash(arq, "  \n", nome) != null, "hash ausente/vazio recusa");
                Ok(Atualizador.ConferirHash(arq, "isto não é um hash", nome) != null, "texto sem SHA-256 recusa");
                Ok(Atualizador.ConferirHash(arq, hex + "  outro.zip", nome) != null, "hash de outro arquivo recusa");
                File.WriteAllBytes(arq, Encoding.UTF8.GetBytes("conteudo ADULTERADO"));
                Ok(Atualizador.ConferirHash(arq, hex, nome) != null, "arquivo adulterado depois do hash recusa");
                // zip
                string zOk = ZipDe(tmp, "ok.zip", ("Estructto/Estructto.exe", "n"), ("Estructto/motor/estructto-motor.exe", "n"));
                string raiz = Atualizador.Extrair(zOk, Path.Combine(tmp, "ex-ok"));
                Ok(File.Exists(Path.Combine(raiz, "Estructto.exe")) && File.Exists(Path.Combine(raiz, "motor", "estructto-motor.exe")), "zip do pacote extrai com Estructto.exe e motor\\");
                Lanca(() => Atualizador.Extrair(ZipDe(tmp, "semmotor.zip", ("Estructto/Estructto.exe", "n")), Path.Combine(tmp, "ex-sm")), "zip sem motor\\ recusado");
                Lanca(() => Atualizador.Extrair(ZipDe(tmp, "slip.zip", ("../fora.txt", "x"), ("Estructto/Estructto.exe", "n"), ("Estructto/motor/estructto-motor.exe", "n")), Path.Combine(tmp, "ex-slip")), "zip com caminho fora da pasta recusado");
                Ok(!File.Exists(Path.Combine(tmp, "fora.txt")), "nada gravado fora da pasta de extração");
                // script: conteúdo
                string sc = Atualizador.ScriptAtualizador;
                Ok(sc.Contains("WaitForExit") && sc.Contains("$ProcessoPid"), "script espera o processo (PID) terminar");
                Ok(sc.Contains("$Destino + '.bak'") && sc.Contains("Copy-Item -LiteralPath $Destino -Destination $bak"), "script faz backup <pasta>.bak");
                Ok(sc.Contains("restaurado do backup") && sc.Contains("Move-Item -LiteralPath $bak -Destination $Destino"), "script restaura o backup em erro");
                Ok(sc.Contains("-Verb RunAs") && sc.Contains("PodeEscrever"), "script pede elevação só sem permissão de escrita");
                Ok(sc.Contains("ProgramData") && sc.Contains("S-1-5-32-544") && sc.Contains("-EncodedCommand"), "script elevado roda de pasta protegida (%ProgramData%, só administradores)");
                // script: execução real, pasta temporária, sem rede
                RodaScript(tmp, true, Ok, "instalação");
                RodaScript(tmp, false, Ok, "pacote incompleto → restauração");
                // processo que não fecha: nada muda e não reabre (sem segunda cópia)
                {
                    string b = Path.Combine(tmp, "run-espera"), inst = Path.Combine(b, "inst", "Estructto"), pk = Path.Combine(b, "pacote");
                    Directory.CreateDirectory(Path.Combine(inst, "motor")); Directory.CreateDirectory(Path.Combine(pk, "motor"));
                    File.WriteAllText(Path.Combine(inst, "Estructto.exe"), "velho"); File.WriteAllText(Path.Combine(pk, "Estructto.exe"), "novo"); File.WriteAllText(Path.Combine(pk, "motor", "estructto-motor.exe"), "novo");
                    var preso = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                    string log = Path.Combine(Atualizador.PastaTemp, "atualizador.log"); long antes = File.Exists(log) ? new FileInfo(log).Length : 0;
                    var ps = Atualizador.IniciarScript(preso.Id, pk, inst, Path.Combine(Environment.SystemDirectory, "whoami.exe"), "", b, 1500);
                    bool fim = ps.WaitForExit(60000);
                    string novoLog = File.ReadAllText(log).Substring((int)antes);
                    Ok(fim && ps.ExitCode == 2 && File.ReadAllText(Path.Combine(inst, "Estructto.exe")) == "velho" && !Directory.Exists(inst + ".bak") && novoLog.Contains("nao reabre") && !novoLog.Contains("atualizado"), "processo não fecha: script desiste (saída 2), não altera nem reabre");
                    // Close cancelado: o programa encerra o script pendente
                    var pend = Atualizador.IniciarScript(preso.Id, pk, inst, Path.Combine(Environment.SystemDirectory, "whoami.exe"), "", b, 60000);
                    System.Threading.Thread.Sleep(1500); Atualizador.CancelarScript(pend);
                    Ok(pend.WaitForExit(10000), "Close cancelado: CancelarScript encerra o script pendente");
                    try { preso.Kill(); } catch { }
                }
                // faixa
                w.EsconderFaixaAtualiza();
                var nova = Atualizador.MaisNova(FixtureReleases, "0.4"); if (nova != null) w.AplicarAtualizacao(nova, false);
                EsperarIdle();
                Ok(w.FaixaAtualizaVisivel && w.TextoFaixaAtualiza.Contains("v0.10"), "faixa aparece com versão maior (v0.10 > 0.4)");
                CapturarPrincipal(w, "atualizacao-faixa");
                w.EsconderFaixaAtualiza();
                var igual = Atualizador.MaisNova(FixtureReleases, "0.10"); if (igual != null) w.AplicarAtualizacao(igual, false);
                Ok(!w.FaixaAtualizaVisivel, "faixa não aparece com versão igual");
                Config.Opcoes.AtualizacaoIgnorada = "0.10";
                w.AplicarAtualizacao(nova, false);
                Ok(!w.FaixaAtualizaVisivel, "\"Depois\" esconde a mesma versão");
                w.AplicarAtualizacao(nova, true);
                Ok(w.FaixaAtualizaVisivel, "procura manual mostra de novo");
                w.EsconderFaixaAtualiza(); Config.Opcoes.AtualizacaoIgnorada = "";
            }
            catch (Exception ex) { falhas++; sb.AppendLine("interrompido: " + ex); }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
                sb.AppendLine(falhas == 0 ? "TODOS PASSARAM" : falhas + " FALHA(S)");
                File.WriteAllText(Path.Combine(pastaSaida, "testes-atualizacao.txt"), sb.ToString(), new UTF8Encoding(false));
                w.alterado = false;
                Environment.Exit(falhas == 0 ? 0 : 1);
            }
        }

        static string ZipDe(string pasta, string nome, params (string entrada, string conteudo)[] itens)
        {
            string caminho = Path.Combine(pasta, nome);
            using (var z = ZipFile.Open(caminho, ZipArchiveMode.Create))
                foreach (var (e, c) in itens) using (var sw = new StreamWriter(z.CreateEntry(e).Open())) sw.Write(c);
            return caminho;
        }

        // sucesso: troca os arquivos, mantém o que é do usuário e deixa o .bak; incompleto: restaura tudo como estava
        static void RodaScript(string tmp, bool sucesso, Action<bool, string> ok, string rotulo)
        {
            string baseDir = Path.Combine(tmp, "run-" + (sucesso ? "ok" : "falha"));
            string inst = Path.Combine(baseDir, "inst", "Estructto"), pk = Path.Combine(baseDir, "pacote");
            Directory.CreateDirectory(Path.Combine(inst, "motor")); Directory.CreateDirectory(Path.Combine(pk, "motor"));
            File.WriteAllText(Path.Combine(inst, "Estructto.exe"), "velho"); File.WriteAllText(Path.Combine(inst, "motor", "estructto-motor.exe"), "velho");
            File.WriteAllText(Path.Combine(inst, "motor", "so-na-velha.dll"), "velho"); File.WriteAllText(Path.Combine(inst, "meu-arquivo.txt"), "do usuario");
            File.WriteAllText(Path.Combine(pk, "Estructto.exe"), "novo");
            if (sucesso) { File.WriteAllText(Path.Combine(pk, "motor", "estructto-motor.exe"), "novo"); File.WriteAllText(Path.Combine(pk, "motor", "nova.dll"), "novo"); }
            // "Estructto" que ainda está aberto: processo curto cujo PID o script tem de esperar
            var espera = Process.Start(new ProcessStartInfo("ping.exe", "-n 3 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            var sw = Stopwatch.StartNew();
            var ps = Atualizador.IniciarScript(espera.Id, pk, inst, Path.Combine(Environment.SystemDirectory, "whoami.exe"), "", baseDir);
            bool terminou = ps.WaitForExit(90000);
            ok(terminou, rotulo + ": script terminou");
            ok(sw.ElapsedMilliseconds >= 1500, rotulo + ": esperou o processo fechar (" + sw.ElapsedMilliseconds + " ms)");
            string L(string rel) { string p = Path.Combine(inst, rel); return File.Exists(p) ? File.ReadAllText(p) : null; }
            if (sucesso) ok(File.Exists(Path.Combine(baseDir, "inst", "Estructto.bak", "Estructto.exe")), rotulo + ": backup .bak criado");
            if (sucesso)
            {
                ok(ps.ExitCode == 0 && L("Estructto.exe") == "novo" && L("motor/estructto-motor.exe") == "novo" && L("motor/nova.dll") == "novo", rotulo + ": arquivos novos no lugar");
                ok(L("motor/so-na-velha.dll") == null, rotulo + ": motor\\ antigo substituído por inteiro");
                ok(L("meu-arquivo.txt") == "do usuario", rotulo + ": arquivo do usuário preservado");
                ok(File.ReadAllText(Path.Combine(baseDir, "inst", "Estructto.bak", "Estructto.exe")) == "velho", rotulo + ": .bak guarda a versão antiga");
            }
            else
            {
                ok(ps.ExitCode == 1 && L("Estructto.exe") == "velho" && L("motor/estructto-motor.exe") == "velho" && L("motor/so-na-velha.dll") == "velho" && L("meu-arquivo.txt") == "do usuario", rotulo + ": pasta restaurada igual à antiga, saída 1");
            }
        }
    }
}
