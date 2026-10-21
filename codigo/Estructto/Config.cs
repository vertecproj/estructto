// Arquivos recentes e salvamento automatico para recuperacao (pasta %AppData%\NovoFtool).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NovoFtool
{
    public static class Config
    {
        static readonly string Pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoFtool");
        static string ArqRecentes { get { return Path.Combine(Pasta, "recentes.txt"); } }
        static string ArqRecup { get { return Path.Combine(Pasta, "recuperacao.portico"); } }
        static string ArqRecupOrig { get { return Path.Combine(Pasta, "recuperacao.txt"); } }

        public static List<string> Recentes()
        {
            try { return File.Exists(ArqRecentes) ? File.ReadAllLines(ArqRecentes).Where(l => l.Trim() != "").Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static void AdicionarRecente(string caminho)
        {
            try
            {
                Directory.CreateDirectory(Pasta);
                var l = Recentes(); l.RemoveAll(x => string.Equals(x, caminho, StringComparison.OrdinalIgnoreCase)); l.Insert(0, caminho);
                File.WriteAllLines(ArqRecentes, l.Take(12));
            }
            catch { }
        }

        public static void RemoverRecente(string caminho)
        {
            try { var l = Recentes(); l.RemoveAll(x => string.Equals(x, caminho, StringComparison.OrdinalIgnoreCase)); File.WriteAllLines(ArqRecentes, l); } catch { }
            Fixar(caminho, false);
        }

        // Recentes fixados (pino na tela inicial): ficam no topo e não saem da lista.
        static string ArqFixados { get { return Path.Combine(Pasta, "fixados.txt"); } }
        public static List<string> Fixados()
        {
            try { return File.Exists(ArqFixados) ? File.ReadAllLines(ArqFixados).Where(l => l.Trim() != "").ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }
        public static bool Fixado(string caminho) => Fixados().Any(x => string.Equals(x, caminho, StringComparison.OrdinalIgnoreCase));
        public static void Fixar(string caminho, bool fixar)
        {
            try
            {
                Directory.CreateDirectory(Pasta);
                var l = Fixados(); l.RemoveAll(x => string.Equals(x, caminho, StringComparison.OrdinalIgnoreCase));
                if (fixar) l.Insert(0, caminho);
                File.WriteAllLines(ArqFixados, l);
            }
            catch { }
        }

        // Recentes com os fixados primeiro (um fixado não cai da lista de 12).
        public static List<(string caminho, bool fixado)> RecentesOrdenados()
        {
            var fx = Fixados();
            return fx.Select(c => (c, true)).Concat(Recentes().Where(c => !fx.Any(f => string.Equals(f, c, StringComparison.OrdinalIgnoreCase))).Select(c => (c, false))).ToList();
        }

        static string ArqCmd { get { return Path.Combine(Pasta, "recentes-cmd.txt"); } }
        public static List<string> RecentesComandos()
        {
            try { return File.Exists(ArqCmd) ? File.ReadAllLines(ArqCmd).Where(l => l.Trim() != "").Distinct().Take(6).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }
        public static void SalvarRecentesComandos(List<string> l)
        {
            try { Directory.CreateDirectory(Pasta); File.WriteAllLines(ArqCmd, l.Take(6)); } catch { }
        }

        // Vista 3D: modo "Realista" (perfis reais, sombras) ou analítico; lembra a última escolha.
        static string ArqVista3D { get { return Path.Combine(Pasta, "vista3d.txt"); } }
        public static bool Realista3D
        {
            get { try { return File.Exists(ArqVista3D) && File.ReadAllText(ArqVista3D).Trim() == "realista"; } catch { return false; } }
            set { try { Directory.CreateDirectory(Pasta); File.WriteAllText(ArqVista3D, value ? "realista" : "analitico"); } catch { } }
        }

        public static void SalvarRecuperacao(string xml, string original)
        {
            try { Directory.CreateDirectory(Pasta); File.WriteAllText(ArqRecup, xml); File.WriteAllText(ArqRecupOrig, original ?? ""); }
            catch { }
        }

        public static bool LerRecuperacao(out string xml, out string original)
        {
            xml = null; original = null;
            try
            {
                if (!File.Exists(ArqRecup)) return false;
                xml = File.ReadAllText(ArqRecup);
                original = File.Exists(ArqRecupOrig) ? File.ReadAllText(ArqRecupOrig).Trim() : "";
                if (original == "") original = null;
                return xml.Length > 0;
            }
            catch { return false; }
        }

        public static void ApagarRecuperacao()
        {
            try { if (File.Exists(ArqRecup)) File.Delete(ArqRecup); if (File.Exists(ArqRecupOrig)) File.Delete(ArqRecupOrig); }
            catch { }
        }

        // Opções do programa (Arquivo › Opções): chave=valor em %AppData%\NovoFtool\opcoes.txt.
        public static class Opcoes
        {
            static string Arq => Path.Combine(Pasta, "opcoes.txt");
            static Dictionary<string, string> cache;
            static Dictionary<string, string> Ler()
            {
                if (cache != null) return cache;
                cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(Arq))
                        foreach (string l in File.ReadAllLines(Arq)) { int i = l.IndexOf('='); if (i > 0) cache[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim(); }
                }
                catch { }
                return cache;
            }
            public static string Get(string chave, string padrao = "") => Ler().TryGetValue(chave, out var v) ? v : padrao;
            public static void Set(string chave, string valor)
            {
                Ler()[chave] = valor ?? "";
                try { Directory.CreateDirectory(Pasta); File.WriteAllLines(Arq, cache.Select(kv => kv.Key + "=" + kv.Value)); } catch { }
            }

            // Acentos oferecidos: nome → cor base (hover e pressionado derivados em TemaPlant.AplicarAcento).
            public static readonly (string nome, string hex)[] Acentos = { ("Ciano (padrão)", "#0EA5E9"), ("Azul", "#3B82F6"), ("Verde", "#22C55E"), ("Âmbar", "#F59E0B"), ("Violeta", "#A855F7") };
            public static string Acento { get => Get("acento", Acentos[0].hex); set => Set("acento", value); }
            public static string Autor { get => Get("autor"); set => Set("autor", value); }
            public static string Empresa { get => Get("empresa", RelatorioMotor.Empresa); set { Set("empresa", value); RelatorioMotor.Empresa = value; } }
            public static string PastaProjetos { get => Get("pasta", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Estructto")); set => Set("pasta", value); }
            public static string Unidades { get => Get("unidades", "kN · m"); set => Set("unidades", value); }   // só exibição (o modelo é sempre kN, m)
            public static int AutosalvarMin { get => int.TryParse(Get("autosalvar", "2"), out int v) && v >= 1 ? v : 2; set => Set("autosalvar", value.ToString()); }
        }
    }
}
