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
    }
}
