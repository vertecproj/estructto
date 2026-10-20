// Leitura dos arquivos .ftl do Ftool original (formato texto, versao 400).
// O formato nao e documentado: o que se le aqui foi deduzido dos arquivos de exemplo.
//   material: 'Nome' 0  /  E(kN/m2) nu gama alfa
//   secao:    'Nome' tipo 0  /  dimensoes em m   (1 = retangulo b h, 8 = tubo circular D t, 11 = tubo retangular h b tw tf)
//   barra:    linha com 4 numeros cientificos = x1 x2 y1 y2; duas linhas depois: indice do material e da secao (base 0)
// Apoios, cargas aplicadas e casos de carga ainda nao sao importados (avisado ao usuario).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Portico;

namespace NovoFtool
{
    public static class SecoesExtras
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex TC = new Regex(@"^TC\s*([\d.,]+)\s*x\s*([\d.,]+)$", RegexOptions.IgnoreCase);
        static readonly Regex TR = new Regex(@"^TR\s*([\d.,]+)\s*x\s*([\d.,]+)\s*x\s*([\d.,]+)$", RegexOptions.IgnoreCase);

        static double D(string s) { double v; return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out v) && v > 0 && double.IsFinite(v) ? v : double.NaN; }

        // Registra na biblioteca as secoes do Ftool descritas pelo nome ("TC 48.3x3.7", "TR 300x200x9.5", em mm).
        public static void Garantir(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome) || Biblioteca.AcharSecao(nome) != null) return;
            Match m = TC.Match(nome.Trim());
            if (m.Success)
            {
                double d = D(m.Groups[1].Value) / 1000, t = D(m.Groups[2].Value) / 1000;
                if (!double.IsNaN(d + t) && 2 * t < d) Biblioteca.Perfis.Add(Tubo(nome.Trim(), d, t));
                return;
            }
            m = TR.Match(nome.Trim());
            if (m.Success)
            {
                double h = D(m.Groups[1].Value) / 1000, b = D(m.Groups[2].Value) / 1000, t = D(m.Groups[3].Value) / 1000;
                if (!double.IsNaN(h + b + t) && 2 * t < Math.Min(h, b)) Biblioteca.Perfis.Add(Caixa(nome.Trim(), h, b, t, t));
            }
        }

        public static Secao Tubo(string nome, double d, double t)
        {
            double di = d - 2 * t, a = Math.PI / 4 * (d * d - di * di), i = Math.PI / 64 * (Math.Pow(d, 4) - Math.Pow(di, 4));
            return new Secao { Nome = nome, Familia = "Ftool", Serie = "TC", Forma = "TC", A = a, Ix = i, Iy = i, AsX = a / 2, AsY = a / 2, H = d, Bf = d, Tw = t, Tf = t, Cx = d / 2, Cy = d / 2 };
        }

        public static Secao Caixa(string nome, double h, double b, double tw, double tf)
        {
            double hi = h - 2 * tf, bi = b - 2 * tw;
            double a = b * h - bi * hi;
            return new Secao
            {
                Nome = nome, Familia = "Ftool", Serie = "TR", Forma = "TR", A = a,
                Ix = (b * h * h * h - bi * hi * hi * hi) / 12, Iy = (h * b * b * b - hi * bi * bi * bi) / 12,
                AsX = 2 * h * tw, AsY = 2 * b * tf, H = h, Bf = b, Tw = tw, Tf = tf, Cx = h / 2, Cy = b / 2
            };
        }
    }

    public static class Ftl
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex Nomeado = new Regex(@"^'([^']*)'\s*(.*)$");
        static readonly Regex Cient = new Regex(@"^[+-]\d+\.\d+e[+-]\d+$");

        static double[] Nums(string linha)
        {
            List<double> r = new List<double>();
            foreach (string t in linha.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (!double.TryParse(t, NumberStyles.Float, Inv, out v)) return null;
                r.Add(v);
            }
            return r.ToArray();
        }

        // Os nomes do Ftool ("ASTM A36", "ASTM A572 Gr.50", "ASTM A500 Gr.B", "VMB 250"...) viram materiais da biblioteca, com fy,
        // para a verificacao funcionar; desconhecidos ficam "E=..." (sem fy).
        static string MaterialFtool(string nome, double eMPa, double nu, double gama)
        {
            Material mt = Biblioteca.AcharMaterial(nome);
            if (mt != null) return mt.Nome;
            string k = nome.ToUpperInvariant().Replace(" ", "");
            if (k.Contains("A36")) return "Aço ASTM A36";
            if (k.Contains("A572")) return "Aço ASTM A572 Gr.50";
            double fy = k.Contains("A500") ? 290 : k.StartsWith("VMB") && Num.Ler(new string(k.Where(char.IsDigit).ToArray()), out double f) ? f : 0;
            if (fy <= 0) return "E=" + eMPa.ToString("0.###", Inv);
            string nm = "Aço " + nome.Trim();
            if (Biblioteca.AcharMaterial(nm) == null)
                Biblioteca.Materiais.Add(new Material { Nome = nm, E = eMPa, Nu = nu > 0 ? nu : 0.3, Densidade = gama > 0 ? gama * 1000 / 9.80665 : 7850, Fy = fy });
            return nm;
        }

        public static Modelo Importar(string arquivo, out string aviso)
        {
            string[] l = File.ReadAllLines(arquivo, Encoding.GetEncoding("ISO-8859-1"));
            List<string> mats = new List<string>();
            List<string> secs = new List<string>();
            Modelo m = new Modelo();
            StringBuilder av = new StringBuilder();

            for (int i = 0; i + 1 < l.Length; i++)
            {
                Match nm = Nomeado.Match(l[i].Trim());
                if (!nm.Success) continue;
                string nome = nm.Groups[1].Value;
                double[] cab = Nums(nm.Groups[2].Value);
                double[] v = Nums(l[i + 1]);
                if (cab == null || v == null || v.Length == 0) continue;
                if (cab.Length == 1 && v.Length == 4 && v[0] > 1e5)
                {
                    // E em kN/m2 -> material "E=xxx" em MPa (o nome fica so como referencia)
                    mats.Add(MaterialFtool(nome, v[0] / 1000, v[1], v[2]));
                }
                else if (cab.Length == 2)
                {
                    int tipo = (int)cab[0];
                    string sec = null;
                    if (tipo == 8 && v.Length >= 2) { sec = nome; SecoesExtras.Garantir(nome); if (Biblioteca.AcharSecao(nome) == null) Biblioteca.Perfis.Add(SecoesExtras.Tubo(nome, v[0], v[1])); }
                    else if (tipo == 11 && v.Length >= 4) { sec = nome; SecoesExtras.Garantir(nome); if (Biblioteca.AcharSecao(nome) == null) Biblioteca.Perfis.Add(SecoesExtras.Caixa(nome, v[0], v[1], v[2], v[3])); }
                    else if (tipo == 1 && v.Length >= 2) sec = "Ret " + (v[0] * 100).ToString("0.##", Inv) + "x" + (v[1] * 100).ToString("0.##", Inv);
                    secs.Add(sec);
                }
            }

            int barras = 0;
            for (int i = 0; i < l.Length; i++)
            {
                string[] tk = l[i].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tk.Length != 4 || !Array.TrueForAll(tk, t => Cient.IsMatch(t))) continue;
                double[] c = Nums(l[i]);
                double x1 = c[0], x2 = c[1], y1 = c[2], y2 = c[3];
                if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 1e-9) continue;
                if (Array.Exists(c, v => Math.Abs(v) > 1e15)) continue;   // linha de limites da janela (+-1e30)
                int im = -1, isec = -1;
                if (i + 3 < l.Length)
                {
                    double[] a = Nums(l[i + 2]), b = Nums(l[i + 3]);
                    // indices em base 0 (no exemplo de tubos: material 1 = A500 Gr.B, secao 2 = TC 33.4x3.4)
                    if (a != null && a.Length == 1) im = (int)a[0];
                    if (b != null && b.Length == 1) isec = (int)b[0];
                }
                No n1 = m.ObterNo(x1, y1, 1e-6), n2 = m.ObterNo(x2, y2, 1e-6);
                Barra br = m.AddBarra(n1, n2, 1e-6);
                if (br == null) continue;
                barras++;
                if (im >= 0 && im < mats.Count) br.Material = mats[im];
                if (isec >= 0 && isec < secs.Count && secs[isec] != null) br.Secao = secs[isec];
                else av.AppendLine("Barra " + barras + ": seção do arquivo não reconhecida, usei " + br.Secao + ".");
            }
            if (barras == 0) av.AppendLine("Nenhuma barra encontrada no arquivo.");

            // Casos de carga e biblioteca de cargas nodais (antes do bloco de materiais):
            //   'Load Case 01' 1        <- caso: nome + um numero (a linha seguinte tem um so numero)
            //   'carga' 4365.5 0 0      <- carga nodal da biblioteca: nome + Fx Fy Mz (kN, kN.m)
            int fimCab = Array.FindIndex(l, x => { Match k = Nomeado.Match(x.Trim()); return k.Success && Nums(k.Groups[2].Value) is double[] h && h.Length == 1 && h[0] == 0; });
            if (fimCab < 0) fimCab = l.Length;
            var casos = new List<string>();
            var biblioteca = new List<string>();
            for (int i = 0; i + 1 < fimCab; i++)
            {
                Match nm = Nomeado.Match(l[i].Trim());
                if (!nm.Success) continue;
                double[] cab = Nums(nm.Groups[2].Value), prox = Nums(l[i + 1]);
                if (cab != null && cab.Length == 1 && prox != null && prox.Length == 1) casos.Add(nm.Groups[1].Value);
                else if (cab != null && cab.Length == 3)
                    biblioteca.Add("\"" + nm.Groups[1].Value + "\" (Fx = " + Num.Fmt(cab[0], 2) + ", Fy = " + Num.Fmt(cab[1], 2) + ", Mz = " + Num.Fmt(cab[2], 2) + ")");
            }
            m.GarantirCasos();
            for (int i = 0; i < casos.Count; i++)
            {
                if (i == 0) { m.Casos[0].Nome = casos[0]; continue; }
                m.AdicionarCaso(new CasoCarga { Nome = casos[i], Tipo = "Q" });
            }
            av.AppendLine("Importados: " + Pl.N(barras, "barra", "barras") + ", " + Pl.N(mats.Count, "material", "materiais") + ", " + Pl.N(secs.Count, "seção", "seções") + (casos.Count > 0 ? ", " + Pl.N(casos.Count, "caso", "casos") + " de carga" : "") + ".");
            if (biblioteca.Count > 0)
                av.AppendLine((biblioteca.Count == 1 ? "O arquivo define a carga nodal " : "O arquivo define as cargas nodais ") + string.Join(", ", biblioteca) + ", mas não foi possível ler em quais nós " + (biblioteca.Count == 1 ? "ela está aplicada." : "elas estão aplicadas."));
            // O trecho dos nos (depois das barras) e identico nos arquivos de exemplo, que nao tem apoio nem carga
            // atribuidos; sem um exemplo com apoio/carga aplicados nao da para saber onde o Ftool grava isso.
            av.Append("Apoios e cargas aplicadas não são lidos do .ftl (formato não documentado): aplique-os pelas abas Modelo e Ações.");
            aviso = av.ToString();
            return m;
        }
    }
}
