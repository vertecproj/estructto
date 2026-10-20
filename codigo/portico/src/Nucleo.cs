// Modelo, biblioteca de materiais/perfis e motor de calculo (metodo da rigidez direta, portico plano).
// Unidades internas: kN, m, rad. Na tela: m, kN, kN/m, kN.m, mm (deslocamentos), MPa (E), cm2/cm4 (secoes).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace Portico
{
    public static class Num
    {
        static readonly CultureInfo BR = CultureInfo.GetCultureInfo("pt-BR");

        // Aceita virgula ou ponto como decimal.
        public static bool Ler(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static string Fmt(double v, int casas)
        {
            if (Math.Abs(v) < 0.5 * Math.Pow(10, -casas)) v = 0;
            return v.ToString("F" + casas, BR);
        }
    }

    public class Material
    {
        public string Nome;
        public double E;          // MPa
        public double Nu;
        public double Densidade;  // kg/m3
        public double Fy;         // escoamento, MPa (0 = nao se verifica tensao)
        public double G { get { return E / (2 * (1 + Nu)); } }
    }

    public class Secao
    {
        public string Nome;

        // Nome por extenso para listas (o Nome curto continua sendo a chave gravada no modelo)
        public string Descricao
        {
            get
            {
                string n = Nome ?? "";
                if (Familia == "Tubo redondo") return "Tubo estrutural redondo " + n.Replace("Tubo ", "").Replace("x", " x ") + " mm";
                if (Familia == "Tubo quadrado (metalon)") return "Tubo estrutural quadrado " + n.Replace("Tubo ", "").Replace("x", " x ") + " mm";
                if (Familia == "Tubo retangular (metalon)") return "Tubo estrutural retangular " + n.Replace("Tubo ", "").Replace("x", " x ") + " mm";
                if (Familia == "Cantoneira L") return "Cantoneira de abas iguais " + n;
                if (Familia == "U laminado") return "Perfil U laminado " + n + " kg/m";
                if (Forma == "I") return (n.StartsWith("HP") ? "Perfil H laminado " : "Perfil I laminado ") + n + " kg/m";
                if (Forma == "TC") return "Tubo circular " + n;
                if (Forma == "TR") return "Tubo retangular " + n;
                return n;
            }
        }
        public double A;      // m2
        public double Ix;     // m4 (eixo forte)
        public double Iy;     // m4 (eixo fraco)
        public double AsX;    // area de cisalhamento para flexao no eixo forte, m2
        public double AsY;
        public double Massa;  // kg/m (0 = usar densidade do material)
        public double H;      // altura, m
        public double Bf, Tw, Tf;   // largura e espessuras, m (0 quando nao se aplica)
        public string Familia = "", Serie = "";
        public string Forma = "GEN";   // I, U, L, TR (tubo retangular), TC (tubo redondo), RET, CIRC, GEN
        public double Cx, Cy;          // distancia do centro a fibra mais afastada, flexao no eixo forte / fraco, m
        public double Wx { get { return Cx > 0 ? Ix / Cx : 0; } }
        public double Wy { get { return Cy > 0 ? Iy / Cy : 0; } }
    }

    public static class Biblioteca
    {
        public static readonly List<Material> Materiais = new List<Material>();
        public static readonly List<Secao> Perfis = new List<Secao>();
        public static string AvisoPerfis = "";

        static Biblioteca()
        {
            AddMat("Aço ASTM A572 Gr.50", 200000, 0.3, 7850);
            AddMat("Aço ASTM A36", 200000, 0.3, 7850);
            Materiais[0].Fy = 345; Materiais[1].Fy = 250;
            // Concreto: Ecs da NBR 6118:2014 (agregado granito, alfaE = 1)
            AddMat("Concreto C20", 21287, 0.2, 2500);
            AddMat("Concreto C25", 24150, 0.2, 2500);
            AddMat("Concreto C30", 26838, 0.2, 2500);
            AddMat("Concreto C35", 29403, 0.2, 2500);
            AddMat("Concreto C40", 31876, 0.2, 2500);
            CarregarPerfis();
        }

        static void AddMat(string nome, double e, double nu, double dens)
        {
            Materiais.Add(new Material { Nome = nome, E = e, Nu = nu, Densidade = dens });
        }

        public static readonly List<string> Familias = new List<string>();

        // Tabela embutida no executavel.
        internal static string[] Linhas(string nome)
        {
            Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome);
            if (st == null) return null;
            using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                return sr.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static List<Secao> DaFamilia(string familia, string serie)
        {
            List<Secao> r = new List<Secao>();
            foreach (Secao s in Perfis)
                if (s.Familia == familia && (serie == null || s.Serie == serie)) r.Add(s);
            return r;
        }

        // Cantoneiras, U laminado e tubos: familia;serie;nome;forma;massa;h;b;tw;tf;A;Ix;Iy (mm, cm2, cm4).
        static void CarregarOutros()
        {
            string[] linhas = Linhas("perfis-outros.csv");
            if (linhas == null) return;
            for (int i = 1; i < linhas.Length; i++)
            {
                string[] c = linhas[i].Trim().Split(';');
                if (c.Length < 12) continue;
                double[] v = new double[8];
                bool ok = true;
                for (int k = 0; k < 8; k++) ok &= Num.Ler(c[4 + k], out v[k]);
                if (!ok) continue;
                double h = v[1], b = v[2], tw = v[3], tf = v[4], a = v[5] * 1e-4;
                Secao s = new Secao
                {
                    Familia = c[0], Serie = c[1], Nome = c[2], Forma = c[3], Massa = v[0],
                    H = h / 1000, Bf = b / 1000, Tw = tw / 1000, Tf = tf / 1000, A = a, Ix = v[6] * 1e-8, Iy = v[7] * 1e-8
                };
                // area de cisalhamento aproximada: as paredes paralelas ao esforco
                if (s.Forma == "TC") { s.AsX = a / 2; s.AsY = a / 2; }
                else if (s.Forma == "TR") { s.AsX = 2 * h * tw * 1e-6; s.AsY = 2 * b * tw * 1e-6; }
                else if (s.Forma == "L") { s.AsX = h * tw * 1e-6; s.AsY = h * tw * 1e-6; }
                else { s.AsX = h * tw * 1e-6; s.AsY = 2 * b * tf * 1e-6 * 5.0 / 6.0; }
                // fibra mais afastada: metade da altura, menos na cantoneira e no U deitado (centro fora do meio)
                s.Cx = s.H / 2; s.Cy = s.Bf / 2;
                if (s.Forma == "L")
                {
                    double xb = (h * tw * h / 2 + (h - tw) * tw * tw / 2) / (tw * (2 * h - tw));
                    s.Cx = s.Cy = (h - xb) / 1000;
                }
                else if (s.Forma == "U")
                {
                    double ag = h * tw + 2 * (b - tw) * tf;
                    double xb = (h * tw * tw / 2 + 2 * tf * (b - tw) * (tw + (b - tw) / 2)) / ag;
                    s.Cy = (b - xb) / 1000;
                }
                if (!Familias.Contains(s.Familia)) Familias.Add(s.Familia);
                Perfis.Add(s);
            }
        }

        // Perfis W e HP: perfis-gerdau.csv.
        static void CarregarPerfis()
        {
            try
            {
                string[] linhas = Linhas("perfis-gerdau.csv");
                if (linhas == null) { AvisoPerfis = "Tabela de perfis não encontrada."; return; }
                Familias.Add("W e HP (Gerdau)");
                for (int i = 1; i < linhas.Length; i++)
                {
                    string[] c = linhas[i].Trim().Split(';');
                    if (c.Length < 9) continue;
                    double massa, d, bf, tw, tf, a, ix, iy;
                    if (!Num.Ler(c[1], out massa) || !Num.Ler(c[2], out d) || !Num.Ler(c[3], out bf) || !Num.Ler(c[4], out tw) ||
                        !Num.Ler(c[5], out tf) || !Num.Ler(c[6], out a) || !Num.Ler(c[7], out ix) || !Num.Ler(c[8], out iy)) continue;
                    string[] pn = c[0].Trim().Split(' ');
                    Perfis.Add(new Secao
                    {
                        Familia = "W e HP (Gerdau)", Serie = pn[0] + " " + pn[1], Forma = "I", Cx = d / 2000, Cy = bf / 2000,
                        Nome = c[0].Trim(), Massa = massa, H = d / 1000, Bf = bf / 1000, Tw = tw / 1000, Tf = tf / 1000,
                        A = a * 1e-4, Ix = ix * 1e-8, Iy = iy * 1e-8,
                        AsX = d * tw * 1e-6,                    // alma
                        AsY = 2 * bf * tf * 1e-6 * 5.0 / 6.0    // mesas
                    });
                }
            }
            catch (Exception ex) { AvisoPerfis = "Erro ao ler a tabela de perfis: " + ex.Message; }
            try { CarregarOutros(); }
            catch (Exception ex) { AvisoPerfis = "Erro ao ler a tabela de perfis: " + ex.Message; }
        }

        static string Chave(string s)
        {
            return (s ?? "").Replace(" ", "").Replace(".", ",").ToUpperInvariant();
        }

        public static Material AcharMaterial(string nome)
        {
            string k = Chave(nome);
            foreach (Material m in Materiais) if (Chave(m.Nome) == k) return m;
            // Personalizado: "E=25000" (MPa)
            if (k.StartsWith("E="))
            {
                double e;
                if (Num.Ler(k.Substring(2), out e) && e > 0) return new Material { Nome = nome, E = e, Nu = 0.3, Densidade = 0 };
            }
            return null;
        }

        static double[] Numeros(string s)
        {
            List<double> r = new List<double>();
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s + " ")
            {
                bool expoente = sb.Length > 0 && (ch == 'E' || ch == 'e');                                   // notacao cientifica: 1E6, 2,5e-3
                bool sinalExp = sb.Length > 0 && (ch == '-' || ch == '+') && char.ToUpperInvariant(sb[sb.Length - 1]) == 'E';
                if (char.IsDigit(ch) || ch == ',' || ch == '.' || expoente || sinalExp) sb.Append(ch);
                else if (sb.Length > 0)
                {
                    double v;
                    if (Num.Ler(sb.ToString(), out v)) r.Add(v);
                    sb.Length = 0;
                }
            }
            return r.ToArray();
        }

        // Perfil Gerdau pelo nome, ou secao digitada: "Ret 20x50" (b x h, cm), "Circ 30" (diametro, cm),
        // "A=45,7 I=3437" (cm2, cm4).
        public static Secao AcharSecao(string nome)
        {
            string k = Chave(nome);
            foreach (Secao s in Perfis) if (Chave(s.Nome) == k) return s;
            double[] n;
            if (k.StartsWith("RET"))
            {
                n = Numeros(k);
                if (n.Length == 2 && n[0] > 0 && n[1] > 0)
                {
                    double b = n[0] / 100, h = n[1] / 100;
                    return new Secao { Nome = nome, A = b * h, Ix = b * h * h * h / 12, Iy = h * b * b * b / 12, AsX = b * h * 5 / 6, AsY = b * h * 5 / 6, H = h, Bf = b, Forma = "RET", Cx = h / 2, Cy = b / 2 };
                }
            }
            else if (k.StartsWith("CIRC"))
            {
                n = Numeros(k);
                if (n.Length == 1 && n[0] > 0)
                {
                    double d = n[0] / 100, a = Math.PI * d * d / 4, i = Math.PI * d * d * d * d / 64;
                    return new Secao { Nome = nome, A = a, Ix = i, Iy = i, AsX = a * 0.9, AsY = a * 0.9, H = d, Bf = d, Forma = "CIRC", Cx = d / 2, Cy = d / 2 };
                }
            }
            else if (k.StartsWith("A="))
            {
                n = Numeros(k.Replace("I=", " "));
                if (n.Length == 2 && n[0] > 0 && n[1] > 0)
                    return new Secao { Nome = nome, A = n[0] * 1e-4, Ix = n[1] * 1e-8, Iy = n[1] * 1e-8, AsX = 0, AsY = 0, H = 0.2 };
            }
            return null;
        }
    }

    // ---------- conversores para a grade de propriedades ----------

    public class SimNao : BooleanConverter
    {
        public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cu, object v, Type t)
        {
            if (t == typeof(string) && v is bool) return (bool)v ? "Sim" : "Não";
            return base.ConvertTo(c, cu, v, t);
        }
        public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cu, object v)
        {
            string s = v as string;
            if (s != null) return s.Trim().ToLowerInvariant().StartsWith("s");
            return base.ConvertFrom(c, cu, v);
        }
    }

    public class ListaSecoes : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        {
            // so os perfis da familia da barra (a familia se escolhe na linha "Familia")
            Barra b = c != null ? c.Instance as Barra : null;
            object[] arr = c != null ? c.Instance as object[] : null;
            if (b == null && arr != null && arr.Length > 0) b = arr[0] as Barra;
            string fam = b != null ? b.Familia : null;
            List<string> l = new List<string>();
            foreach (Secao s in Biblioteca.Perfis) if (string.IsNullOrEmpty(fam) || fam == Barra.Generica || s.Familia == fam) l.Add(s.Nome);
            if (string.IsNullOrEmpty(fam) || fam == Barra.Generica) { l.Add("Ret 20x50"); l.Add("Circ 30"); }
            return new StandardValuesCollection(l);
        }
    }

    public class ListaFamilias : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        {
            List<string> l = new List<string>(Biblioteca.Familias);
            l.Add(Barra.Generica);
            return new StandardValuesCollection(l);
        }
    }

    public class ListaPosicao : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) { return new StandardValuesCollection(new[] { Barra.PosForte, Barra.PosFraco }); }
    }

    public class ListaMateriais : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        {
            List<string> l = new List<string>();
            foreach (Material m in Biblioteca.Materiais) l.Add(m.Nome);
            return new StandardValuesCollection(l);
        }
    }

    // ---------- modelo ----------

    public class No
    {
        [Browsable(false)] public int Id { get; set; }

        [Category("1. Posição"), DisplayName("X (m)")] public double X { get; set; }
        [Category("1. Posição"), DisplayName("Y (m)")] public double Y { get; set; }

        [Category("2. Apoio"), DisplayName("Trava X"), TypeConverter(typeof(SimNao))] public bool ApoioX { get; set; }
        [Category("2. Apoio"), DisplayName("Trava Y"), TypeConverter(typeof(SimNao))] public bool ApoioY { get; set; }
        [Category("2. Apoio"), DisplayName("Trava giro"), TypeConverter(typeof(SimNao))] public bool ApoioGiro { get; set; }
        [Category("2. Apoio"), DisplayName("Mola X (kN/m)")] public double MolaX { get; set; }
        [Category("2. Apoio"), DisplayName("Mola Y (kN/m)")] public double MolaY { get; set; }
        [Category("2. Apoio"), DisplayName("Mola giro (kN.m/rad)")] public double MolaGiro { get; set; }
        [Category("2. Apoio"), DisplayName("Recalque X (mm)"), Description("Só vale se a direção estiver travada.")] public double RecalqueX { get; set; }
        [Category("2. Apoio"), DisplayName("Recalque Y (mm)"), Description("Só vale se a direção estiver travada. Negativo = para baixo.")] public double RecalqueY { get; set; }

        [Category("3. Cargas no nó"), DisplayName("Fx (kN)"), Description("Positivo para a direita.")] public double Fx { get; set; }
        [Category("3. Cargas no nó"), DisplayName("Fy (kN)"), Description("Positivo para cima. Carga de gravidade = negativo.")] public double Fy { get; set; }
        [Category("3. Cargas no nó"), DisplayName("Mz (kN.m)"), Description("Positivo anti-horário.")] public double Mz { get; set; }

        [Category("3. Cargas no nó"), DisplayName("Carga giratória (guincho)"), TypeConverter(typeof(SimNao)),
         Description("Sim = o equipamento gira em torno da coluna: além da posição lançada, calcula a posição a 180° (Fx e Mz deste nó com o sinal trocado, em todos os casos) e verifica a envoltória das duas.")]
        public bool Rotativa { get; set; }

        [Browsable(false)] public PlacaBase Placa { get; set; }

        // Cargas dos outros casos de carga (as do caso ativo ficam em Fx, Fy, Mz).
        [Browsable(false)] public List<CargaNoCaso> OutrasCargas { get; set; }
        public bool ShouldSerializeOutrasCargas() { return OutrasCargas != null && OutrasCargas.Count > 0; }

        // ux, uy, giro, Rx, Ry, Rm
        [XmlIgnore, Browsable(false)] public double[] Res;

        [Category("4. Resultados"), DisplayName("Desloc. X (mm)")] public string DeslX { get { return Res == null ? "" : Num.Fmt(Res[0] * 1000, 3); } }
        [Category("4. Resultados"), DisplayName("Desloc. Y (mm)")] public string DeslY { get { return Res == null ? "" : Num.Fmt(Res[1] * 1000, 3); } }
        [Category("4. Resultados"), DisplayName("Giro (rad)")] public string Giro { get { return Res == null ? "" : Res[2].ToString("0.000000"); } }
        [Category("4. Resultados"), DisplayName("Reação X (kN)")] public string ReacaoX { get { return Res == null ? "" : Num.Fmt(Res[3], 2); } }
        [Category("4. Resultados"), DisplayName("Reação Y (kN)")] public string ReacaoY { get { return Res == null ? "" : Num.Fmt(Res[4], 2); } }
        [Category("4. Resultados"), DisplayName("Reação M (kN.m)")] public string ReacaoM { get { return Res == null ? "" : Num.Fmt(Res[5], 2); } }

        [Browsable(false)] public bool TemApoio { get { return ApoioX || ApoioY || ApoioGiro || MolaX != 0 || MolaY != 0 || MolaGiro != 0; } }
        [Browsable(false)] public bool TemCarga { get { return Fx != 0 || Fy != 0 || Mz != 0; } }
    }

    public class Barra
    {
        [XmlIgnore, Browsable(false)] public Modelo Dono;
        [XmlIgnore, Browsable(false)] public ResBarra Res;

        [Browsable(false)] public int Id { get; set; }
        [Browsable(false)] public int N1 { get; set; }
        [Browsable(false)] public int N2 { get; set; }
        [Browsable(false)] public int Membro { get; set; }   // 0 = barra isolada; >0 = trecho da barra continua de mesmo numero
        public bool ShouldSerializeMembro() { return Membro != 0; }

        [Category("1. Geometria"), DisplayName("Barra contínua"), XmlIgnore,
         Description("Trechos unidos (Modelo › Barra contínua › Unir) são uma peça só: mesmo perfil, verificados e dimensionados juntos.")]
        public string MembroTxt
        {
            get
            {
                if (Membro == 0 || Dono == null) return "—";
                return "B" + Membro + " (" + Dono.Trechos(Membro).Count + " trechos, " + Dono.CompMembro(Membro).ToString("0.00") + " m)";
            }
        }

        [Category("1. Geometria"), DisplayName("Comprimento (m)"), XmlIgnore,
         Description("Digite o comprimento: o nó final se desloca na direção da barra.")]
        public double Comprimento
        {
            get { return Dono == null ? 0 : Math.Round(Dono.Comp(this), 6); }
            set { if (Dono != null && value > 1e-6) Dono.DefinirPolar(this, value, Dono.Ang(this)); }
        }

        [Category("1. Geometria"), DisplayName("Ângulo (graus)"), XmlIgnore,
         Description("Ângulo com a horizontal: o nó final gira em torno do inicial.")]
        public double Angulo
        {
            get { return Dono == null ? 0 : Math.Round(Dono.Ang(this), 4); }
            set { if (Dono != null) Dono.DefinirPolar(this, Dono.Comp(this), value); }
        }

        [Category("2. Seção"), DisplayName("Perfil"), TypeConverter(typeof(ListaSecoes)),
         Description("Perfil Gerdau da lista, ou digite: \"Ret 20x50\" (b x h em cm), \"Circ 30\" (diâmetro em cm), \"A=45,7 I=3437\" (cm² e cm4).")]
        public string Secao { get; set; }

        public const string Generica = "Seção digitada (Ret, Circ, A= I=)";

        // Familia do perfil: escolher outra troca o perfil pelo mais leve dela; a lista de "Perfil" mostra so essa familia
        [Category("2. Seção"), DisplayName("Família"), TypeConverter(typeof(ListaFamilias)), XmlIgnore,
         Description("Tipo de perfil (W, tubos, cantoneiras, U...). Ao trocar, a barra recebe o perfil mais leve da família; depois escolha o tamanho em \"Perfil\".")]
        public string Familia
        {
            get { Secao s = Biblioteca.AcharSecao(Secao); return s != null && !string.IsNullOrEmpty(s.Familia) ? s.Familia : Generica; }
            set
            {
                if (value == Familia) return;
                if (value == Generica) { Secao = "Ret 20x50"; return; }
                Secao leve = null;
                foreach (Secao s in Biblioteca.DaFamilia(value, null))
                    if (leve == null || (s.Massa > 0 ? s.Massa : s.A * 7850) < (leve.Massa > 0 ? leve.Massa : leve.A * 7850)) leve = s;
                if (leve != null) Secao = leve.Nome;
            }
        }

        [Category("2. Seção"), DisplayName("Material"), TypeConverter(typeof(ListaMateriais)),
         Description("Material da lista, ou digite \"E=25000\" (MPa).")]
        public string Material { get; set; }

        [Browsable(false)] public bool EixoFraco { get; set; }   // gravado no arquivo; na tela aparece como "Posição do perfil"

        public const string PosForte = "Em pé: maior inércia (Ix) no plano", PosFraco = "Deitado: menor inércia (Iy) no plano";

        [Category("2. Seção"), DisplayName("Posição do perfil"), TypeConverter(typeof(ListaPosicao)), XmlIgnore,
         Description("Qual eixo da seção fica no plano do pórtico. Em pé: flexão e flambagem no plano usam Ix (maior inércia) e a flambagem fora do plano usa Iy. Deitado: o contrário. Em tubo redondo e quadrado tanto faz.")]
        public string Posicao
        {
            get { return EixoFraco ? PosFraco : PosForte; }
            set { EixoFraco = value == PosFraco; }
        }

        [Category("2. Seção"), DisplayName("Travamento fora do plano (nós)"),
         Description("Nós em que a barra é travada na direção perpendicular ao pórtico (ex.: \"2, 5\"), \"pontas\" ou \"todos\". Vazio = nenhum: só os apoios travam, e o comprimento de flambagem fora do plano é o da barra contínua inteira (lado seguro).")]
        public string TravFora { get; set; }
        public bool ShouldSerializeTravFora() { return !string.IsNullOrEmpty(TravFora); }

        [Category("3. Rótulas"), DisplayName("Rótula no início"), TypeConverter(typeof(SimNao))] public bool RotulaInicio { get; set; }
        [Category("3. Rótulas"), DisplayName("Rótula no fim"), TypeConverter(typeof(SimNao))] public bool RotulaFim { get; set; }

        [Category("4. Cargas na barra"), DisplayName("q vertical (kN/m)"), Description("Uniforme, por metro de barra. Carga de gravidade = negativo.")]
        public double QGlobalY { get; set; }
        [Category("4. Cargas na barra"), DisplayName("q horizontal (kN/m)"), Description("Uniforme, por metro de barra. Positivo para a direita.")]
        public double QGlobalX { get; set; }
        [Category("4. Cargas na barra"), DisplayName("q perpendicular início (kN/m)"), Description("Carga perpendicular à barra, variando linearmente do início ao fim.")]
        public double QLocalIni { get; set; }
        [Category("4. Cargas na barra"), DisplayName("q perpendicular fim (kN/m)")]
        public double QLocalFim { get; set; }

        [Category("5. Resultados"), DisplayName("Normal (kN)"), Description("Mínima / máxima. Positivo = tração.")]
        public string ResN { get { return Faixa(0, 2); } }
        [Category("5. Resultados"), DisplayName("Cortante (kN)")]
        public string ResQ { get { return Faixa(1, 2); } }
        [Category("5. Resultados"), DisplayName("Momento (kN.m)"), Description("Mínimo / máximo. Positivo = tração na fibra de baixo.")]
        public string ResM { get { return Faixa(2, 2); } }
        [Category("5. Resultados"), DisplayName("Flecha (mm)"), Description("Maior afastamento em relação à corda entre os nós deslocados.")]
        public string ResFlecha
        {
            get
            {
                if (Res == null) return "";
                double f = Res.FlechaMax();
                return Num.Fmt(f * 1000, 2) + (Math.Abs(f) > 1e-9 ? "  (L/" + Math.Round(Res.L / Math.Abs(f)) + ")" : "");
            }
        }

        string Faixa(int tipo, int casas)
        {
            if (Res == null) return "";
            double mn, mx;
            Res.Extremos(tipo, out mn, out mx);
            return Num.Fmt(mn, casas) + " / " + Num.Fmt(mx, casas);
        }

        [Browsable(false)] public bool TemCarga { get { return QGlobalX != 0 || QGlobalY != 0 || QLocalIni != 0 || QLocalFim != 0; } }

        // Cargas dos outros casos de carga (as do caso ativo ficam em QGlobalX/Y e QLocalIni/Fim).
        [Browsable(false)] public List<CargaBarraCaso> OutrasCargas { get; set; }
        public bool ShouldSerializeOutrasCargas() { return OutrasCargas != null && OutrasCargas.Count > 0; }

        public Barra Copiar()
        {
            Barra b = (Barra)MemberwiseClone();
            b.Res = null;
            if (OutrasCargas != null) { b.OutrasCargas = new List<CargaBarraCaso>(); foreach (CargaBarraCaso c in OutrasCargas) b.OutrasCargas.Add(c.Copiar()); }
            return b;
        }
    }

    // ---------- casos de carga e combinacoes ----------
    // Compatibilidade: os campos de carga de No e Barra (Fx, Fy, Mz, QGlobalX...) sao sempre as cargas do caso ATIVO;
    // as dos demais casos ficam em OutrasCargas. Arquivo antigo = um caso so ("G - Permanente") e nenhuma combinacao,
    // e o Portico, que nao conhece casos, continua lendo/gravando o caso ativo.

    public class CasoCarga
    {
        public string Nome { get; set; }
        public string Tipo { get; set; }            // G permanente, Q variavel, V vento, E excepcional
        public bool PesoProprio { get; set; }       // o peso proprio (se ligado no modelo) entra neste caso
        public CasoCarga Copiar() { return (CasoCarga)MemberwiseClone(); }
    }

    public class Combinacao
    {
        public string Nome { get; set; }
        public string Tipo { get; set; }            // ELU ou ELS
        public List<double> Fatores { get; set; }   // um por caso, na ordem de Modelo.Casos
        public Combinacao() { Fatores = new List<double>(); }
        public double Fator(int caso) { return caso < Fatores.Count ? Fatores[caso] : 0; }
    }

    public class CargaNoCaso
    {
        public int Caso { get; set; }
        public double Fx { get; set; }
        public double Fy { get; set; }
        public double Mz { get; set; }
        public bool Vazia { get { return Fx == 0 && Fy == 0 && Mz == 0; } }
        public CargaNoCaso Copiar() { return (CargaNoCaso)MemberwiseClone(); }
    }

    public class CargaBarraCaso
    {
        public int Caso { get; set; }
        public double QX { get; set; }
        public double QY { get; set; }
        public double QIni { get; set; }
        public double QFim { get; set; }
        public bool Vazia { get { return QX == 0 && QY == 0 && QIni == 0 && QFim == 0; } }
        public CargaBarraCaso Copiar() { return (CargaBarraCaso)MemberwiseClone(); }
    }

    public class Modelo
    {
        [Browsable(false)] public List<No> Nos { get; set; }
        [Browsable(false)] public List<Barra> Barras { get; set; }
        [Browsable(false)] public int ProxId { get; set; }

        [Category("Cálculo"), DisplayName("Considerar peso próprio"), TypeConverter(typeof(SimNao)),
         Description("Soma o peso das barras (massa do catálogo Gerdau, ou densidade do material x área).")]
        public bool PesoProprio { get; set; }

        [Category("Cálculo"), DisplayName("Deformação por cortante"), TypeConverter(typeof(SimNao)),
         Description("Não = viga de Euler-Bernoulli. Sim = viga de Timoshenko.")]
        public bool Timoshenko { get; set; }

        [Browsable(false)] public double Seguranca { get; set; }       // coeficiente de seguranca exigido nas verificacoes
        [Browsable(false)] public double LimiteFlecha { get; set; }    // flecha admissivel = L / este numero
        [Browsable(false)] public string Eletrodo { get; set; }        // eletrodo das soldas de filete ("E70"; vazio = E70)

        [Category("Cálculo"), DisplayName("Coef. dinâmico ψ já incluído nas cargas"),
         Description("Só informativo: o ψ (NBR 8400, içamento) que o projetista JÁ multiplicou nas cargas do equipamento. 0 = não informado (o relatório avisa quando há carga giratória). O programa não aplica ψ de novo.")]
        public double Psi { get; set; }
        public bool ShouldSerializePsi() { return Psi != 0; }

        [Browsable(false), XmlIgnore] public bool TemRotativa { get { return Nos.Any(n => n.Rotativa); } }

        // Posicao oposta do equipamento giratorio (180 graus): Fx e Mz dos nos giratorios trocam de sinal em todos os casos.
        // Fy (peso) nao muda. Hipotese de portico plano: as posicoes a 90/270 graus (momento fora do plano) nao sao analisadas.
        public Modelo PosicaoOposta()
        {
            Modelo c = DeXml(ParaXml());
            foreach (No n in c.Nos)
            {
                if (!n.Rotativa) continue;
                n.Fx = -n.Fx; n.Mz = -n.Mz;
                if (n.OutrasCargas != null) foreach (CargaNoCaso q in n.OutrasCargas) { q.Fx = -q.Fx; q.Mz = -q.Mz; }
            }
            return c;
        }

        [Category("Grade"), DisplayName("Mostrar grade"), TypeConverter(typeof(SimNao))] public bool MostrarGrade { get; set; }
        [Category("Grade"), DisplayName("Atrair para a grade"), TypeConverter(typeof(SimNao)),
         Description("Só afeta o clique do mouse. Medidas digitadas nunca são arredondadas.")]
        public bool AtrairGrade { get; set; }
        [Category("Grade"), DisplayName("Tamanho da grade (m)")] public double Grade { get; set; }

        [Browsable(false)] public List<CasoCarga> Casos { get; set; }
        [Browsable(false)] public List<Combinacao> Combinacoes { get; set; }
        [Browsable(false)] public int CasoAtivo { get; set; }
        public bool ShouldSerializeCasos() { return Casos != null && Casos.Count > 0; }
        public bool ShouldSerializeCombinacoes() { return Combinacoes != null && Combinacoes.Count > 0; }

        public Modelo()
        {
            Nos = new List<No>();
            Barras = new List<Barra>();
            Casos = new List<CasoCarga>();          // vazio aqui: o XmlSerializer acrescentaria ao que o construtor criasse
            Combinacoes = new List<Combinacao>();
            ProxId = 1;
            MostrarGrade = true;
            AtrairGrade = false;
            Grade = 0.5;
            Seguranca = 2;
            LimiteFlecha = 250;
        }

        // Numeracao separada para nos e barras (1, 2, 3...), como no Ftool. ProxId fica so por compatibilidade de arquivo.
        int ProxNo() { int m = 0; foreach (No n in Nos) m = Math.Max(m, n.Id); ProxId = Math.Max(ProxId, m + 2); return m + 1; }
        int ProxBarra() { int m = 0; foreach (Barra b in Barras) m = Math.Max(m, b.Id); ProxId = Math.Max(ProxId, m + 2); return m + 1; }

        public No NoPorId(int id)
        {
            foreach (No n in Nos) if (n.Id == id) return n;
            return null;
        }

        // ---------- barras continuas (membros): varios trechos entre nos que sao a mesma peca ----------
        public List<Barra> Trechos(int membro) { return Barras.Where(b => membro != 0 && b.Membro == membro).ToList(); }
        public int ProxMembro() { int m = 0; foreach (Barra b in Barras) m = Math.Max(m, b.Membro); return m + 1; }
        public double CompMembro(int membro) { double s = 0; foreach (Barra b in Trechos(membro)) s += Comp(b); return s; }

        // um membro com um trecho so deixa de ser membro; numeracao fica 1, 2, 3... sem buracos
        public void ArrumarMembros()
        {
            var usados = Barras.Where(b => b.Membro != 0).GroupBy(b => b.Membro).OrderBy(g => g.Key).ToList();
            int k = 1;
            foreach (var g in usados)
            {
                if (g.Count() < 2) { foreach (Barra b in g) b.Membro = 0; continue; }
                foreach (Barra b in g) b.Membro = k;
                k++;
            }
        }

        public double Comp(Barra b)
        {
            No a = NoPorId(b.N1), c = NoPorId(b.N2);
            return Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
        }

        public double Ang(Barra b)
        {
            No a = NoPorId(b.N1), c = NoPorId(b.N2);
            return Math.Atan2(c.Y - a.Y, c.X - a.X) * 180 / Math.PI;
        }

        public void DefinirPolar(Barra b, double comp, double angGraus)
        {
            No a = NoPorId(b.N1), c = NoPorId(b.N2);
            double t = angGraus * Math.PI / 180;
            c.X = Math.Round(a.X + comp * Math.Cos(t), 9);
            c.Y = Math.Round(a.Y + comp * Math.Sin(t), 9);
        }

        // ---------- casos de carga ----------

        // Arquivo antigo ou modelo novo: um caso permanente com o peso proprio.
        public void GarantirCasos()
        {
            if (Casos == null) Casos = new List<CasoCarga>();
            if (Combinacoes == null) Combinacoes = new List<Combinacao>();
            if (Casos.Count == 0) Casos.Add(new CasoCarga { Nome = "G - Permanente", Tipo = "G", PesoProprio = true });
            if (CasoAtivo < 0 || CasoAtivo >= Casos.Count) CasoAtivo = 0;
            foreach (Combinacao c in Combinacoes) while (c.Fatores.Count < Casos.Count) c.Fatores.Add(0);
        }

        // Troca o caso cujas cargas ficam nos campos Fx/Fy/Mz e QGlobal/QLocal (os que a tela e o Portico editam).
        public void AtivarCaso(int novo)
        {
            GarantirCasos();
            if (novo < 0 || novo >= Casos.Count || novo == CasoAtivo) return;
            int velho = CasoAtivo;
            foreach (No n in Nos)
            {
                if (n.OutrasCargas == null) n.OutrasCargas = new List<CargaNoCaso>();
                CargaNoCaso guarda = new CargaNoCaso { Caso = velho, Fx = n.Fx, Fy = n.Fy, Mz = n.Mz };
                CargaNoCaso vem = n.OutrasCargas.Find(delegate(CargaNoCaso c) { return c.Caso == novo; });
                n.OutrasCargas.RemoveAll(delegate(CargaNoCaso c) { return c.Caso == novo || c.Caso == velho; });
                if (!guarda.Vazia) n.OutrasCargas.Add(guarda);
                n.Fx = vem != null ? vem.Fx : 0; n.Fy = vem != null ? vem.Fy : 0; n.Mz = vem != null ? vem.Mz : 0;
                n.OutrasCargas.Sort(delegate(CargaNoCaso a, CargaNoCaso b) { return a.Caso.CompareTo(b.Caso); });
            }
            foreach (Barra br in Barras)
            {
                if (br.OutrasCargas == null) br.OutrasCargas = new List<CargaBarraCaso>();
                CargaBarraCaso guarda = new CargaBarraCaso { Caso = velho, QX = br.QGlobalX, QY = br.QGlobalY, QIni = br.QLocalIni, QFim = br.QLocalFim };
                CargaBarraCaso vem = br.OutrasCargas.Find(delegate(CargaBarraCaso c) { return c.Caso == novo; });
                br.OutrasCargas.RemoveAll(delegate(CargaBarraCaso c) { return c.Caso == novo || c.Caso == velho; });
                if (!guarda.Vazia) br.OutrasCargas.Add(guarda);
                br.QGlobalX = vem != null ? vem.QX : 0; br.QGlobalY = vem != null ? vem.QY : 0;
                br.QLocalIni = vem != null ? vem.QIni : 0; br.QLocalFim = vem != null ? vem.QFim : 0;
                br.OutrasCargas.Sort(delegate(CargaBarraCaso a, CargaBarraCaso b) { return a.Caso.CompareTo(b.Caso); });
            }
            CasoAtivo = novo;
        }

        public void AdicionarCaso(CasoCarga c)
        {
            GarantirCasos();
            Casos.Add(c);
            foreach (Combinacao k in Combinacoes) while (k.Fatores.Count < Casos.Count) k.Fatores.Add(0);
        }

        // Remove o caso e as cargas dele; os indices maiores descem um.
        public void RemoverCaso(int i)
        {
            GarantirCasos();
            if (Casos.Count <= 1 || i < 0 || i >= Casos.Count) return;
            if (CasoAtivo == i) AtivarCaso(i == 0 ? 1 : 0);
            foreach (No n in Nos) if (n.OutrasCargas != null)
            {
                n.OutrasCargas.RemoveAll(delegate(CargaNoCaso c) { return c.Caso == i; });
                foreach (CargaNoCaso c in n.OutrasCargas) if (c.Caso > i) c.Caso--;
            }
            foreach (Barra b in Barras) if (b.OutrasCargas != null)
            {
                b.OutrasCargas.RemoveAll(delegate(CargaBarraCaso c) { return c.Caso == i; });
                foreach (CargaBarraCaso c in b.OutrasCargas) if (c.Caso > i) c.Caso--;
            }
            foreach (Combinacao k in Combinacoes) if (i < k.Fatores.Count) k.Fatores.RemoveAt(i);
            Casos.RemoveAt(i);
            if (CasoAtivo > i) CasoAtivo--;
        }

        // Devolve o no existente nessa posicao ou cria um; se cair sobre uma barra, divide a barra.
        public No ObterNo(double x, double y, double tol)
        {
            foreach (No n in Nos)
                if (Math.Abs(n.X - x) <= tol && Math.Abs(n.Y - y) <= tol) return n;
            No novo = new No { Id = ProxNo(), X = x, Y = y };
            Nos.Add(novo);
            foreach (Barra b in Barras.ToArray())
            {
                No a = NoPorId(b.N1), c = NoPorId(b.N2);
                double dx = c.X - a.X, dy = c.Y - a.Y, l2 = dx * dx + dy * dy;
                if (l2 < 1e-18) continue;
                double t = ((x - a.X) * dx + (y - a.Y) * dy) / l2;
                if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                double px = a.X + t * dx - x, py = a.Y + t * dy - y;
                if (Math.Sqrt(px * px + py * py) > tol) continue;
                Barra nb = b.Copiar();
                nb.Id = ProxBarra();
                nb.N1 = novo.Id;
                nb.RotulaInicio = false;
                double qm = b.QLocalIni + (b.QLocalFim - b.QLocalIni) * t;
                nb.QLocalIni = qm;
                b.QLocalFim = qm;
                if (b.OutrasCargas != null)   // a carga perpendicular linear dos outros casos tambem e repartida no ponto
                    for (int k = 0; k < b.OutrasCargas.Count; k++)
                    {
                        CargaBarraCaso cb = b.OutrasCargas[k], cn = nb.OutrasCargas[k];
                        double qk = cb.QIni + (cb.QFim - cb.QIni) * t;
                        cn.QIni = qk; cb.QFim = qk;
                    }
                b.N2 = novo.Id;
                b.RotulaFim = false;
                Barras.Add(nb);
            }
            return novo;
        }

        // Cria a barra entre dois nos; se houver nos no caminho, cria um trecho por vao. Devolve a ultima barra criada.
        public Barra AddBarra(No a, No c, double tol)
        {
            if (a == c) return null;
            double dx = c.X - a.X, dy = c.Y - a.Y, l2 = dx * dx + dy * dy;
            List<KeyValuePair<double, No>> cam = new List<KeyValuePair<double, No>>();
            cam.Add(new KeyValuePair<double, No>(0, a));
            cam.Add(new KeyValuePair<double, No>(1, c));
            foreach (No n in Nos)
            {
                if (n == a || n == c) continue;
                double t = ((n.X - a.X) * dx + (n.Y - a.Y) * dy) / l2;
                if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                double px = a.X + t * dx - n.X, py = a.Y + t * dy - n.Y;
                if (Math.Sqrt(px * px + py * py) <= tol) cam.Add(new KeyValuePair<double, No>(t, n));
            }
            cam.Sort(delegate(KeyValuePair<double, No> p, KeyValuePair<double, No> q) { return p.Key.CompareTo(q.Key); });
            Barra modeloB = Barras.Count > 0 ? Barras[Barras.Count - 1] : null;
            Barra ult = null;
            for (int i = 0; i + 1 < cam.Count; i++)
            {
                int i1 = cam[i].Value.Id, i2 = cam[i + 1].Value.Id;
                bool existe = false;
                foreach (Barra b in Barras)
                    if ((b.N1 == i1 && b.N2 == i2) || (b.N1 == i2 && b.N2 == i1)) { existe = true; break; }
                if (existe) continue;
                ult = new Barra
                {
                    Id = ProxBarra(), N1 = i1, N2 = i2, Dono = this,
                    Secao = modeloB != null ? modeloB.Secao : (Biblioteca.Perfis.Count > 0 ? "W 200 x 26,6" : "Ret 20x50"),
                    Material = modeloB != null ? modeloB.Material : Biblioteca.Materiais[0].Nome,
                    EixoFraco = modeloB != null && modeloB.EixoFraco
                };
                Barras.Add(ult);
            }
            return ult;
        }

        public void RemoverBarra(Barra b)
        {
            Barras.Remove(b);
            LimparNosSoltos();
        }

        public void RemoverNo(No n)
        {
            Barras.RemoveAll(delegate(Barra b) { return b.N1 == n.Id || b.N2 == n.Id; });
            Nos.Remove(n);
            LimparNosSoltos();
        }

        void LimparNosSoltos()
        {
            Nos.RemoveAll(delegate(No n)
            {
                foreach (Barra b in Barras) if (b.N1 == n.Id || b.N2 == n.Id) return false;
                return true;
            });
        }

        public string ParaXml()
        {
            XmlSerializer xs = new XmlSerializer(typeof(Modelo));
            using (StringWriter sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                xs.Serialize(sw, this);
                return sw.ToString();
            }
        }

        public static Modelo DeXml(string xml)
        {
            XmlSerializer xs = new XmlSerializer(typeof(Modelo));
            using (StringReader sr = new StringReader(xml))
            {
                Modelo m = (Modelo)xs.Deserialize(sr);
                foreach (Barra b in m.Barras) b.Dono = m;
                if (m.Grade <= 0) m.Grade = 0.5;
                if (m.Seguranca <= 0) m.Seguranca = 2;
                if (m.LimiteFlecha <= 0) m.LimiteFlecha = 250;
                m.GarantirCasos();
                return m;
            }
        }
    }

    // ---------- placa de base e parafusos ----------
    // Tabelas e verificacoes conforme o material de referencia (Juncoes nao permanentes / Tabela Parafusos):
    // areas da Tabela 3 (rosca metrica grossa), classes da Tabela 6, tracao F/At contra a resistencia de prova,
    // corte F/Ar contra 0,577.Sp, pre-carga 0,75.Fp e torque T = 0,2.Fi.d.

    public static class Parafusos
    {
        public static readonly double[] D = { 8, 10, 12, 14, 16, 20, 24, 30, 36 };              // mm
        public static readonly double[] At = { 36.6, 58.0, 84.3, 115, 157, 245, 353, 561, 817 };   // area de tracao, mm2
        public static readonly double[] Ar = { 32.8, 52.3, 76.3, 104, 144, 225, 324, 519, 759 };   // area do diametro menor, mm2
        public static readonly string[] Classes = { "4.6", "4.8", "5.8", "8.8", "9.8", "10.9" };
        public static readonly double[] Sp = { 225, 310, 380, 600, 650, 830 };                     // resistencia de prova, MPa
        public static readonly string[] Faixa = { "M5 a M36", "M1,6 a M16", "M5 a M24", "M16 a M36", "M1,6 a M16", "M5 a M36" };
    }

    // Chumbadores de expansao e quimicos (catalogos dos fabricantes, chumbadores.csv). Cargas ULTIMAS medias de ensaio
    // (ou recomendada para WQA), em kgf, no concreto indicado (Fck): a verificacao divide pelo coeficiente de seguranca
    // do chumbador (tabela Walsywa: estatica 3 a 5, variavel 4 a 5, dinamica 7 a 15, choque 5 a 10; WQA usa n = 1).
    public class Chumbador
    {
        public string Codigo, Fabricante, Linha, Pol, Fonte;
        public bool Quimico;
        public double Rosca, Furo, Hef, Tu, Vu, TuRec, VuRec, Torque, S, C, Fck, AtB, ArB;   // mm, mm, mm, kgf..., kgf.m, mm (entre chumbadores), mm (borda), MPa, mm2
        public string Nome { get { return Linha + " " + Pol + " hef " + Hef.ToString("0") + " mm"; } }
        public double TuKN { get { return Tu * 0.00980665; } }
        public double VuKN { get { return Vu * 0.00980665; } }
        public double TuRecKN { get { return TuRec * 0.00980665; } }
        public double VuRecKN { get { return VuRec * 0.00980665; } }
    }

    public static class Chumbadores
    {
        public static readonly List<Chumbador> Lista = new List<Chumbador>();
        public static readonly double[] CoefSugeridos = { 3, 4, 5, 7, 10, 15 };

        static Chumbadores()
        {
            string[] linhas = Biblioteca.Linhas("chumbadores.csv");
            if (linhas == null || linhas.Length < 2) return;
            string[] cab = linhas[0].Trim().Split(';');
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int j = 0; j < cab.Length; j++) col[cab[j].Trim()] = j;
            Func<string[], string, string> S = (c, k) => col.ContainsKey(k) && col[k] < c.Length ? c[col[k]].Trim() : "";
            Func<string[], string, double> D = (c, k) =>
            {
                string s = S(c, k); double v;
                return s != "" && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
            };
            for (int i = 1; i < linhas.Length; i++)
            {
                string lin = linhas[i].Trim();
                if (lin == "") continue;
                string[] c = lin.Split(';');
                Lista.Add(new Chumbador
                {
                    Codigo = S(c, "codigo"), Quimico = S(c, "tipo") == "quimico", Fabricante = S(c, "fabricante"),
                    Linha = S(c, "linha"), Pol = S(c, "pol"), Rosca = D(c, "rosca_mm"), Furo = D(c, "furo_mm"), Hef = D(c, "hef_mm"),
                    Tu = D(c, "tu_kgf"), Vu = D(c, "vu_kgf"), TuRec = D(c, "tu_rec_kgf"), VuRec = D(c, "vu_rec_kgf"),
                    Fck = D(c, "fck_mpa"), Torque = D(c, "torque_kgfm"), S = D(c, "s_mm"), C = D(c, "c_mm"),
                    AtB = D(c, "at_mm2"), ArB = D(c, "ar_mm2"), Fonte = S(c, "fonte")
                });
            }
        }

        // Tipo de carga -> faixa do coeficiente de seguranca recomendada pela Walsywa (minimo, maximo)
        public static readonly string[] Tipos = { "estatica", "variavel", "dinamica", "choque" };
        public static readonly string[] NomesTipos = { "estática", "variável", "dinâmica", "choque" };
        // Tabela de coeficientes de seguranca da Walsywa (catalogo "Fixacao mecanica", pag. 07): por tipo de carga, BAIXO e ALTO risco
        public static readonly double[] CoefMin = { 3, 4, 7, 5 }, CoefMax = { 5, 5, 15, 10 };   // CoefMin = baixo risco, CoefMax = alto risco
        public static double Coef(int tipo, bool alto) { return alto ? CoefMax[tipo] : CoefMin[tipo]; }

        // Risco efetivo: o escolhido; vazio = automatico (alto com carga giratoria/equipamento de icamento, senao baixo com aviso)
        public static bool RiscoAlto(Modelo m, PlacaBase p, out bool automatico)
        {
            string r = p == null ? "" : (p.Risco ?? "").Trim().ToLowerInvariant();
            automatico = r != "alto" && r != "baixo";
            return automatico ? m != null && m.TemRotativa : r == "alto";
        }

        // Tipo efetivo: o escolhido; vazio = automatico (dinamica se ha carga giratoria/equipamento, senao variavel; nunca estatica por omissao)
        public static int TipoEfetivo(Modelo m, PlacaBase p, out bool automatico)
        {
            int i = p == null || string.IsNullOrEmpty(p.TipoCarga) ? -1 : Array.IndexOf(Tipos, p.TipoCarga.Trim().ToLowerInvariant());
            automatico = i < 0;
            if (i < 0) i = m != null && m.TemRotativa ? 2 : 1;
            return i;
        }

        public static Chumbador Achar(string nome) { return string.IsNullOrEmpty(nome) ? null : Lista.FirstOrDefault(c => c.Nome == nome || c.Codigo == nome); }   // vazio = parafuso (ha chumbadores sem codigo)
        public static List<Chumbador> DaLinha(string linha) { return Lista.Where(c => c.Linha == linha).ToList(); }
        public static List<string> Linhas() { return Lista.Select(c => c.Linha).Distinct().ToList(); }

        // Seguranca obtida com tracao T e corte V por chumbador (kN), contra cargas de referencia Tref (ja com FFck e psi) e Vref:
        // interacao (T/Tref)^5/3 + (V/Vref)^5/3 = (1/n)^5/3. Se Vref <= 0 (quimico sem corte no catalogo), termo de corte = 0.
        public static double Seguranca(double tref, double vref, double t, double v)
        {
            double a = tref > 0 ? Math.Pow(Math.Max(0, t) / tref, 5.0 / 3) : (t > 1e-9 ? double.PositiveInfinity : 0);
            double b = vref > 0 ? Math.Pow(Math.Max(0, v) / vref, 5.0 / 3) : 0;
            return a + b > 1e-12 ? Math.Pow(a + b, -3.0 / 5) : double.PositiveInfinity;
        }

        public static double Seguranca(Chumbador c, double t, double v, double psi = 1)
        {
            return Seguranca(c.TuKN * psi, c.VuKN, t, v);
        }

        // Sobreposicao dos cones de arrancamento (metodo das areas projetadas, ACI 318 cap. 17 / EN 1992-4):
        // cada chumbador tracionado tem um cone de base quadrada 3.hef x 3.hef (raio ~1,5.hef, catalogo Walsywa pag. 07).
        // psi = area da uniao dos quadrados / (nt . 9.hef2) <= 1. Com espacamento >= 3.hef, psi = 1 (sem reducao).
        // paraf: {y, z, tracao} em mm e kN. Borda do concreto nao entra (o modelo nao conhece a laje): declarado no memorial.
        public static double FatorCones(Chumbador c, List<double[]> paraf)
        {
            if (c == null || paraf.Count == 0) return 1;
            var tr = paraf.Where(p => p[2] > 1e-9).ToList();
            if (tr.Count == 0) tr = paraf;
            double h = 1.5 * c.Hef;
            Func<IEnumerable<double>, double> uniao = xs =>
            {
                var v = xs.Select(x => Math.Round(x, 3)).Distinct().OrderBy(x => x).ToList();
                double tot = 0, ini = v[0] - h, fim = v[0] + h;
                for (int i = 1; i < v.Count; i++)
                {
                    if (v[i] - h <= fim) fim = v[i] + h;
                    else { tot += fim - ini; ini = v[i] - h; fim = v[i] + h; }
                }
                return tot + fim - ini;
            };
            double a = uniao(tr.Select(p => p[0])) * uniao(tr.Select(p => p[1]));
            return Math.Min(1, a / (tr.Count * 9 * c.Hef * c.Hef));
        }

        // Espacamento minimo fisico entre chumbadores de expansao por torque: 6.d (ACI 318, 17.9.2). Abaixo disso nao instala.
        public static double EspacMinimo(Chumbador c) { return 6 * c.Rosca; }
    }

    public class PlacaBase
    {
        public string Chumbador { get; set; }    // nome do chumbador do catalogo; vazio = parafuso (tabela metrica)
        public double CoefChumbador { get; set; }   // coeficiente de seguranca do chumbador (0 = o minimo da faixa do tipo de carga)
        public string TipoCarga { get; set; }       // estatica | variavel | dinamica | choque (vazio = automatico: dinamica com carga giratoria, senao variavel)
        public string Risco { get; set; }           // baixo | alto (grau de risco da Walsywa; vazio = automatico)
        public bool PeloPerfil { get; set; }     // medidas = perfil do pilar + sobra de cada lado
        public double Sobra { get; set; }        // mm
        public double H { get; set; }            // mm, no plano do portico (direcao do momento)
        public double B { get; set; }            // mm, perpendicular ao plano
        public double Esp { get; set; }          // mm
        public int Fileiras { get; set; }        // ao longo de H
        public int PorFileira { get; set; }      // ao longo de B
        public double Borda { get; set; }        // mm, do centro do furo a borda da placa
        public double Diametro { get; set; }     // mm
        public string Classe { get; set; }
        public bool RoscaNoCorte { get; set; }
        public bool PivoNaMesa { get; set; }     // giro na mesa comprimida do perfil, em vez da borda da placa
        public double FyChapa { get; set; }      // escoamento do aco da chapa, MPa (0 = 250, ASTM A36)
        public double Fck { get; set; }          // fck do concreto da laje, MPa (0 = 20)
        public string ClasseBarra { get; set; }  // classe da barra roscada do chumbador quimico (vazio = 4.6, lado seguro)
        public double Solda { get; set; }        // perna do filete pilar-placa, mm (0 = dimensionar)
        public string Eletrodo { get; set; }     // "E70" etc. (vazio = E70)
        public double Seguranca { get; set; }    // coeficiente da placa (chapa, parafuso, aco da barra); 0 = o do modelo (aba Analise)

        public PlacaBase Copiar() { return (PlacaBase)MemberwiseClone(); }
    }

    public class ResPlaca
    {
        public string Erro = "";
        public bool TemEsforcos;
        public Secao Sec;
        public bool Deitado;
        public bool Espelhar;                    // o eixo y local do pilar aponta para a esquerda no desenho
        public double RefIn, RefOut, H, B;       // mm
        public double N, V, M;                   // nos eixos da PLACA (horizontal, normal = Y global): N tracao nas fixacoes +, V cortante no plano da placa, kN e kN.m
        public double Rx, Ry, Rm;                // reacao do no (apoio sobre a estrutura), de onde N/V/M saem: N = -Ry, V = |Rx|, M = -Rm
        public double NBarra, VBarra, MBarra;    // os mesmos esforcos nos eixos locais da barra (so informativo)
        public double NormX, NormY;              // normal da placa (eixo global), apontando para a barra
        public double AngNormal;                 // angulo entre o eixo da barra e a normal da placa, graus (0 = barra perpendicular a placa)
        public bool Inclinada;                   // barra inclinada sobre a placa: chapa e solda nao sao verificadas para esse caso
        public double BarraY, BarraN;            // direcao unitaria da barra nos eixos da placa (y no plano do portico, normal)
        public int TipoCarga; public bool TipoAuto;   // tipo de carga do chumbador (indice em Chumbadores.Tipos) e se foi o automatico
        public string AvisoCoef = "";            // coeficiente do chumbador abaixo do recomendado para o tipo de carga e o risco
        public bool RiscoAlto, RiscoAuto;        // grau de risco usado no coeficiente do chumbador e se foi o automatico
        public double EspNecT, CConc, BracoC, EspNecC;   // chapa: t pelo lado tracionado; reacao do concreto (kN), braco (mm) e t pelo lado comprimido
        public List<double[]> Paraf = new List<double[]>();   // y (mm, eixo local y do pilar), z (mm), tracao (kN)
        public string Modelo = "";
        public double PivoY = double.NaN;        // mm
        public double Tmax, Fv, At, Ar, Ad, Sp, Sigma, Tau, NT, NV, NComb, Fi, Torque;
        public double TLinha, Braco, LargEf, FyChapa, EspNec;   // chapa: tracao da fileira critica (kN), braco e largura (mm), espessura (mm)
        public Chumbador Ch;                     // null = parafuso
        public double NExig;                     // seguranca exigida (parafuso: a do modelo; chumbador: a do chumbador)
        public double NSeg;                      // seguranca do modelo (exigida para o aco da barra no chumbador quimico)
        public double FckLaje = 20, FFck = 1, NRef, Tref, Vref, NAco = double.PositiveInfinity, SpBarra;   // concreto da laje, fator fck, seguranca/cargas de ref no concreto e aco da barra (quimico)
        public string ClasseBarra = "4.6";
        public double Dia;                       // diametro do parafuso / rosca do chumbador, mm
        public double EspacMin;                  // menor distancia entre furos da placa, mm
        public string Aviso = "";
        public double PsiCone = 1, TuEf;          // fator dos cones sobrepostos e tracao ultima efetiva por chumbador (kN)
        public bool EspacOk { get { return Ch == null || EspacMin <= 0 || EspacMin >= Chumbadores.EspacMinimo(Ch) - 1e-9; } }   // minimo fisico 6.d; abaixo de 3.hef so reduz Tu
        public bool ChOk { get { return NComb >= (Ch != null ? NRef : NExig) && (Ch == null || !Ch.Quimico || NAco >= NSeg) && EspacOk; } }
        public double ApFix { get { double ac = double.IsInfinity(NComb) ? 0 : (Ch != null ? NRef : NExig) / NComb, aa = Ch != null && Ch.Quimico && !double.IsInfinity(NAco) ? NSeg / NAco : 0; return Math.Max(ac, aa); } }
        public bool Passa { get { return Erro == "" && ChOk && !Inclinada; } }
        public string Fixacao { get { return Ch != null ? Ch.Nome : "M" + Dia.ToString("0"); } }
    }

    public static class PlacaCalc
    {
        // Pilar ligado ao no e medidas do perfil no plano do portico e fora dele (mm).
        public static Barra Pilar(Modelo m, No n, out Secao sec, out double refIn, out double refOut, out string erro)
        {
            sec = null; refIn = refOut = 0; erro = "";
            Barra pilar = null; int qt = 0;
            foreach (Barra b in m.Barras) if (b.N1 == n.Id || b.N2 == n.Id) { pilar = b; qt++; }
            if (qt != 1) { erro = "A placa de base precisa ficar num nó onde chega uma só barra (o pilar)."; return null; }
            sec = Biblioteca.AcharSecao(pilar.Secao);
            if (sec == null) { erro = "Perfil do pilar não reconhecido."; return null; }
            double h = sec.H * 1000, bf = (sec.Bf > 0 ? sec.Bf : sec.H) * 1000;
            refIn = pilar.EixoFraco ? bf : h;
            refOut = pilar.EixoFraco ? h : bf;
            return pilar;
        }

        public static ResPlaca Calcular(Modelo m, No n)
        {
            ResPlaca r = new ResPlaca();
            PlacaBase p = n.Placa;
            if (p == null) { r.Erro = "Este nó não tem placa de base."; return r; }
            Barra pilar = Pilar(m, n, out r.Sec, out r.RefIn, out r.RefOut, out r.Erro);
            if (pilar == null) return r;
            r.Deitado = pilar.EixoFraco;
            No n1 = m.NoPorId(pilar.N1), n2 = m.NoPorId(pilar.N2);
            // Eixos da PLACA: a placa fica no plano do apoio (piso, teto ou parede), com a normal no eixo global (Y ou X)
            // mais proximo da barra, apontando para a barra. y da placa = normal girada +90 graus (no piso: -X global,
            // o mesmo do eixo y local de um pilar vertical que sobe). Barra fora da normal = inclinada sobre a placa.
            No outro = pilar.N1 == n.Id ? n2 : n1;
            double ex = outro.X - n.X, ey = outro.Y - n.Y, lb = Math.Sqrt(ex * ex + ey * ey);
            if (Math.Abs(ey) >= Math.Abs(ex)) { r.NormX = 0; r.NormY = ey >= 0 ? 1 : -1; } else { r.NormX = ex >= 0 ? 1 : -1; r.NormY = 0; }
            double yx = -r.NormY, yy = r.NormX;
            r.Espelhar = yx < -1e-9 || (Math.Abs(yx) <= 1e-9 && yy < 0);
            r.AngNormal = lb > 0 ? Math.Acos(Math.Min(1, Math.Abs(ex * r.NormX + ey * r.NormY) / lb)) * 180 / Math.PI : 0;
            r.Inclinada = r.AngNormal > 2;
            if (lb > 0) { r.BarraY = (ex * yx + ey * yy) / lb; r.BarraN = (ex * r.NormX + ey * r.NormY) / lb; }
            double nPl = p.Seguranca > 0 ? p.Seguranca : m.Seguranca;
            r.H = p.PeloPerfil ? Math.Ceiling((r.RefIn + 2 * p.Sobra) / 10) * 10 : p.H;
            r.B = p.PeloPerfil ? Math.Ceiling((r.RefOut + 2 * p.Sobra) / 10) * 10 : p.B;
            int nf = Math.Max(1, p.Fileiras), np = Math.Max(1, p.PorFileira);
            if (r.H <= 0 || r.B <= 0 || 2 * p.Borda >= r.H || 2 * p.Borda >= r.B) { r.Erro = "Medidas da placa incoerentes: os furos não cabem."; return r; }
            r.Ch = string.IsNullOrEmpty(p.Chumbador) ? null : Chumbadores.Achar(p.Chumbador);
            if (!string.IsNullOrEmpty(p.Chumbador) && r.Ch == null) { r.Erro = "Chumbador \"" + p.Chumbador + "\" não está no catálogo."; return r; }
            int id = Array.IndexOf(Parafusos.D, p.Diametro), ic = Array.IndexOf(Parafusos.Classes, p.Classe);
            if (r.Ch == null && (id < 0 || ic < 0)) { r.Erro = "Parafuso ou classe fora da tabela."; return r; }
            r.Dia = r.Ch != null ? r.Ch.Rosca : p.Diametro;
            r.NSeg = nPl;
            r.TipoCarga = Chumbadores.TipoEfetivo(m, p, out r.TipoAuto);
            r.RiscoAlto = Chumbadores.RiscoAlto(m, p, out r.RiscoAuto);
            double nTab = Chumbadores.Coef(r.TipoCarga, r.RiscoAlto);
            r.NExig = r.Ch != null ? (p.CoefChumbador > 0 ? p.CoefChumbador : nTab) : nPl;
            r.NRef = r.Ch != null && r.Ch.Tu <= 0 ? 1 : r.NExig;
            if (r.Ch != null && r.NExig < nTab - 1e-9)
                r.AvisoCoef = "Coeficiente do chumbador n = " + Num.Fmt(r.NExig, 1) + " abaixo do recomendado pela Walsywa para carga " + Chumbadores.NomesTipos[r.TipoCarga] +
                              ", " + (r.RiscoAlto ? "alto" : "baixo") + " risco (n = " + Num.Fmt(nTab, 0) + ").";
            else if (r.Ch != null && r.RiscoAuto && !r.RiscoAlto && p.CoefChumbador <= 0)
                r.AvisoCoef = "Grau de risco não informado: adotado BAIXO risco (n = " + Num.Fmt(nTab, 0) + "). Se a falha da fixação puder ferir pessoas ou derrubar o equipamento, use ALTO risco (n = " + Num.Fmt(Chumbadores.CoefMax[r.TipoCarga], 0) + ").";

            // parafusos no contorno da placa
            for (int i = 0; i < nf; i++)
                for (int j = 0; j < np; j++)
                {
                    if (i > 0 && i < nf - 1 && j > 0 && j < np - 1) continue;
                    double y = nf == 1 ? 0 : -r.H / 2 + p.Borda + i * (r.H - 2 * p.Borda) / (nf - 1);
                    double z = np == 1 ? 0 : -r.B / 2 + p.Borda + j * (r.B - 2 * p.Borda) / (np - 1);
                    r.Paraf.Add(new[] { y, z, 0.0 });
                }
            int nb = r.Paraf.Count;
            r.EspacMin = double.MaxValue;
            for (int i = 0; i < nb; i++) for (int j = i + 1; j < nb; j++)
                    r.EspacMin = Math.Min(r.EspacMin, Math.Sqrt(Math.Pow(r.Paraf[i][0] - r.Paraf[j][0], 2) + Math.Pow(r.Paraf[i][1] - r.Paraf[j][1], 2)));
            if (r.EspacMin == double.MaxValue) r.EspacMin = 0;
            if (r.Ch != null)
            {
                r.Ad = Math.PI * r.Dia * r.Dia / 4; r.At = r.Ad; r.Ar = r.Ad;
                r.Torque = r.Ch.Torque * 9.80665;        // kgf.m -> N.m (0 = catalogo nao informa)
            }
            else
            {
                r.At = Parafusos.At[id]; r.Ar = Parafusos.Ar[id]; r.Ad = Math.PI * p.Diametro * p.Diametro / 4; r.Sp = Parafusos.Sp[ic];
                r.Fi = 0.75 * r.At * r.Sp / 1000;        // pre-carga para conexao nao permanente, kN
                r.Torque = 0.2 * r.Fi * p.Diametro;      // N.m
            }

            if (pilar.Res == null) { r.Erro = "Sem esforços: a estrutura ainda não foi calculada."; return r; }
            double x = pilar.N1 == n.Id ? 0 : pilar.Res.L;
            r.NBarra = pilar.Res.N(x); r.VBarra = Math.Abs(pilar.Res.Q(x)); r.MBarra = pilar.Res.M(x);
            // Reacao do no = forca do no sobre a barra na ponta da placa, levada aos eixos globais (o no so tem esta barra).
            // Os chumbadores sentem a reacao inteira nos eixos da placa: N = -Ry (tracao +), V = |Rx|, M = -Rm.
            ResBarra rb = pilar.Res;
            int k0 = pilar.N1 == n.Id ? 0 : 3;
            r.Rx = rb.C * rb.F[k0] - rb.S * rb.F[k0 + 1];
            r.Ry = rb.S * rb.F[k0] + rb.C * rb.F[k0 + 1];
            r.Rm = rb.F[k0 + 2];
            // forca da estrutura sobre a placa = -R: tracao = (-R).normal, corte = (-R).y_placa, momento = -Rm
            r.N = -(r.Rx * r.NormX + r.Ry * r.NormY); r.V = Math.Abs(r.Rx * yx + r.Ry * yy); r.M = -r.Rm;
            r.TemEsforcos = true;
            bool cargaNoNo = n.TemCarga || (n.OutrasCargas != null && n.OutrasCargas.Any(c => !c.Vazia));
            if (cargaNoNo) r.Aviso = (r.Aviso != "" ? r.Aviso + " " : "") + "Há carga aplicada no próprio nó da placa: ela vai direto ao apoio e NÃO entra nas forças das fixações calculadas aqui.";
            if (r.Inclinada)
                r.Aviso = (r.Aviso != "" ? r.Aviso + " " : "") + "Barra inclinada " + Num.Fmt(r.AngNormal, 1) + "° em relação à normal da placa: as fixações foram calculadas com a reação do nó nos eixos da placa, mas a espessura da chapa e a solda (modelo do perfil perpendicular à placa) NÃO valem para barra inclinada. Placa não aprovada: detalhar a ligação (gusset/cantoneira) à parte.";
            if (r.AvisoCoef != "") r.Aviso = (r.Aviso != "" ? r.Aviso + " " : "") + r.AvisoCoef;

            // u = distancia ao centro, positiva para o lado tracionado pelo momento (m).
            // M > 0 traciona a fibra do lado -y local do pilar.
            double s = r.M >= 0 ? 1 : -1, ma = Math.Abs(r.M), su2 = 0;
            double[] u = new double[nb];
            for (int i = 0; i < nb; i++) { u[i] = -s * r.Paraf[i][0] / 1000; su2 += u[i] * u[i]; }
            double tmin = double.MaxValue;
            for (int i = 0; i < nb; i++)
            {
                r.Paraf[i][2] = r.N / nb + (su2 > 0 ? ma * u[i] / su2 : 0);
                tmin = Math.Min(tmin, r.Paraf[i][2]);
            }
            if (tmin >= -1e-9) r.Modelo = "Todos os parafusos tracionados: a placa não encosta na base.";
            else
            {
                // placa rigida girando em torno da borda comprimida: tracao proporcional a distancia ao giro
                double a = (p.PivoNaMesa ? r.RefIn : r.H) / 2000, sd2 = 0;
                double[] d = new double[nb];
                for (int i = 0; i < nb; i++) { d[i] = Math.Max(0, u[i] + a); sd2 += d[i] * d[i]; }
                double k = sd2 > 0 ? (ma + r.N * a) / sd2 : 0;
                for (int i = 0; i < nb; i++) r.Paraf[i][2] = k > 0 ? k * d[i] : 0;
                r.PivoY = s * a * 1000;
                r.Modelo = k > 0 ? "Placa rígida girando na " + (p.PivoNaMesa ? "mesa comprimida do perfil" : "borda comprimida da placa") + "."
                                 : "Sem tração nos parafusos: a compressão mantém a placa apoiada.";
            }
            foreach (double[] b in r.Paraf) { if (b[2] < 0) b[2] = 0; r.Tmax = Math.Max(r.Tmax, b[2]); }
            r.Fv = r.V / nb;
            r.Sigma = r.Tmax * 1000 / r.At;
            r.Tau = r.Fv * 1000 / (p.RoscaNoCorte ? r.Ar : r.Ad);
            if (r.Ch != null)
            {
                // chumbador: cargas de referencia no concreto (ruptura ou recomendada para WQA), com FFck e cones sobrepostos (psi),
                // mais verificacao do aco da barra roscada quando quimico
                AvaliarChumbador(r.Ch, p, m, r);
            }
            else
            {
                r.NT = r.Sigma > 1e-9 ? r.Sp / r.Sigma : double.PositiveInfinity;
                r.NV = r.Tau > 1e-9 ? 0.577 * r.Sp / r.Tau : double.PositiveInfinity;
                double vm = Math.Sqrt(r.Sigma * r.Sigma + 3 * r.Tau * r.Tau);
                r.NComb = vm > 1e-9 ? r.Sp / vm : double.PositiveInfinity;
            }

            // Espessura da chapa (modelo simplificado): a faixa de chapa entre a fileira de parafusos e a face
            // do perfil trabalha em balanco, puxada pela tracao da fileira. sigma = 6 M / (b t2) <= fy / n.
            r.FyChapa = p.FyChapa > 0 ? p.FyChapa : 250;
            Dictionary<double, double[]> linhas = new Dictionary<double, double[]>();   // y -> { tracao, quantidade }
            foreach (double[] b in r.Paraf)
            {
                double y = Math.Round(b[0], 3);
                if (!linhas.ContainsKey(y)) linhas[y] = new double[2];
                linhas[y][0] += b[2]; linhas[y][1] += 1;
            }
            foreach (KeyValuePair<double, double[]> l in linhas)
            {
                if (l.Value[0] <= 1e-9) continue;
                // fileira dentro da projecao do perfil: a chapa nao trabalha em balanco ali (vai direto ao perfil)
                if (Math.Abs(l.Key) < r.RefIn / 2) continue;
                double braco = Math.Max(Math.Abs(l.Key) - r.RefIn / 2, r.Dia);
                double larg = Math.Min(r.B, l.Value[1] * (2 * braco + r.Dia));
                double esp = Math.Sqrt(6 * l.Value[0] * 1000 * braco * nPl / (larg * r.FyChapa));
                if (esp > r.EspNec) { r.EspNec = esp; r.TLinha = l.Value[0]; r.Braco = braco; r.LargEf = larg; }
            }
            r.EspNecT = r.EspNec;
            // Lado comprimido: o modelo de placa rigida concentra a reacao do concreto C = soma(T) - N na linha de giro.
            // Essa faixa (da linha de giro ate a face do perfil) tambem trabalha em balanco: t = raiz(6.C.e.n/(B.fy)).
            if (!double.IsNaN(r.PivoY))
            {
                double somaT = 0; foreach (double[] b in r.Paraf) somaT += b[2];
                r.CConc = somaT - r.N;
                r.BracoC = Math.Abs(r.PivoY) - r.RefIn / 2;
                if (r.CConc > 1e-9 && r.BracoC > 0)
                {
                    r.EspNecC = Math.Sqrt(6 * r.CConc * 1000 * r.BracoC * nPl / (r.B * r.FyChapa));
                    if (r.EspNecC > r.EspNec) r.EspNec = r.EspNecC;
                }
            }
            return r;
        }

        // Escolhe sozinho o chumbador: o mais "barato" que passa (menos chumbadores, depois menor rosca, depois menor
        // embutimento), em todas as linhas do catalogo (ou so na linha dada). Arranjos tentados: 2x2, 2x3 (3 por fileira),
        // 3x3. Devolve a descricao do escolhido, ou "" se nenhum passa (a placa volta como estava).
        public static string DimensionarChumbador(Modelo m, No n, string linha = null)
        {
            PlacaBase p = n.Placa;
            if (p == null) return "";
            string ch0 = p.Chumbador; int f0 = p.Fileiras, q0 = p.PorFileira;
            var cands = (string.IsNullOrEmpty(linha) ? Chumbadores.Lista : Chumbadores.DaLinha(linha)).OrderBy(c => c.Rosca).ThenBy(c => c.Hef).ToList();
            foreach (int[] arr in new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 3, 3 } })
            {
                if (arr[0] * arr[1] < f0 * q0) continue;   // nao diminui o que o usuario ja pos
                foreach (Chumbador c in cands)
                {
                    p.Chumbador = c.Nome; p.Fileiras = Math.Max(arr[0], f0); p.PorFileira = Math.Max(arr[1], q0);
                    ResPlaca t = Calcular(m, n);
                    if (t.Erro == "" && t.TemEsforcos && t.ChOk)
                        return (t.Paraf.Count) + " × " + c.Nome + " (" + c.Fabricante + ")";
                }
            }
            p.Chumbador = ch0; p.Fileiras = f0; p.PorFileira = q0;
            return "";
        }

        // Avalia um chumbador candidato (concreto + aco da barra quando quimico) com os esforcos e arranjo ja em r.
        public static void AvaliarChumbador(Chumbador c, PlacaBase p, Modelo m, ResPlaca r)
        {
            r.Ch = c;
            r.FckLaje = p != null && p.Fck > 0 ? p.Fck : 20;
            r.FFck = c.Fck > 0 ? Math.Min(1, Math.Sqrt(r.FckLaje / c.Fck)) : 1;
            r.PsiCone = Chumbadores.FatorCones(c, r.Paraf);
            if (c.Tu > 0) { r.Tref = c.TuKN * r.FFck * r.PsiCone; r.Vref = c.Vu > 0 ? c.VuKN : 0; r.NRef = r.NExig; }
            else { r.Tref = c.TuRecKN * r.FFck * r.PsiCone; r.Vref = c.VuRecKN; r.NRef = 1; }
            r.TuEf = r.Tref;
            r.NT = r.Tmax > 1e-9 ? r.Tref / r.Tmax : double.PositiveInfinity;
            r.NV = r.Vref > 0 && r.Fv > 1e-9 ? r.Vref / r.Fv : double.PositiveInfinity;
            r.NComb = Chumbadores.Seguranca(r.Tref, r.Vref, r.Tmax, r.Fv);
            if (c.Quimico)
            {
                int ic = p != null ? Array.IndexOf(Parafusos.Classes, p.ClasseBarra) : -1;
                if (ic < 0) ic = 0;   // padrao 4.6
                r.ClasseBarra = Parafusos.Classes[ic];
                r.SpBarra = Parafusos.Sp[ic];
                r.At = c.AtB; r.Ar = c.ArB;
                r.Sigma = r.Tmax * 1000 / r.At;
                r.Tau = r.Fv * 1000 / r.Ar;
                double vm = Math.Sqrt(r.Sigma * r.Sigma + 3 * r.Tau * r.Tau);
                r.NAco = vm > 1e-9 ? r.SpBarra / vm : double.PositiveInfinity;
            }
            else r.NAco = double.PositiveInfinity;
        }

        public static ResPlaca CandidatoChumbador(ResPlaca rBase, PlacaBase p, Modelo m, Chumbador c)
        {
            ResPlaca t = new ResPlaca { NExig = rBase.NExig, NSeg = rBase.NSeg, Tmax = rBase.Tmax, Fv = rBase.Fv, EspacMin = rBase.EspacMin, Paraf = rBase.Paraf };
            AvaliarChumbador(c, p, m, t);
            return t;
        }

        // Seguranca combinada (tracao + corte) que o mesmo esforco daria com outro diametro e outra classe.
        public static double SegurancaCom(ResPlaca r, PlacaBase p, int iDiam, int iClasse)
        {
            double d = Parafusos.D[iDiam];
            double sig = r.Tmax * 1000 / Parafusos.At[iDiam];
            double tau = r.Fv * 1000 / (p.RoscaNoCorte ? Parafusos.Ar[iDiam] : Math.PI * d * d / 4);
            double vm = Math.Sqrt(sig * sig + 3 * tau * tau);
            return vm > 1e-9 ? Parafusos.Sp[iClasse] / vm : double.PositiveInfinity;
        }

        public static readonly double[] Chapas = { 6.35, 8, 9.5, 12.7, 16, 19, 22.4, 25.4, 31.5, 38.1, 50.8 };   // espessuras comerciais, mm
    }

    // ---------- verificacao simplificada das barras ----------
    // Tensao normal (N/A + M/W), corte (Q/As), flecha (L/limite) e flambagem de Euler/Johnson, com o coeficiente
    // de seguranca do modelo. NAO e a verificacao da NBR 8800: nao ha flambagem lateral nem flambagem local.

    public class Verif
    {
        public bool Aplicavel;
        public string Motivo = "";
        public double Sigma, SigmaAdm, Tau, TauAdm, Flecha, FlechaAdm, Nc, Ncr, Esbeltez;   // MPa, MPa, m, kN (Ncr e Esbeltez: plano que governa)
        public double ApTensao, ApCorte, ApFlecha, ApFlamb, ApEsbeltez, Ap;               // aproveitamento: 1 = no limite
        public string Critico = "";
        public double K = 1;   // coeficiente de flambagem do plano que governa
        public string Aviso = "";
        public string AvisoInversao = "";   // barra sem compressao: a flambagem nao foi verificada neste resultado
        public bool Balanco;// flecha medida na ponta solta (deslocamento relativo a tangente no engaste)
        public double Nt;                    // maior tracao, kN
        public double LimEsbeltez;           // 200 (comprimida) ou 300 (so tracionada); 0 = sem esforco normal
        public double ApInteracao, BAmp = 1, NeNoPlano, Mmax;   // interacao N + M com amplificacao B = 1/(1 - n.Nc/Ne)
        public Flamb NoPlano, ForaPlano;     // flambagem nos dois planos (sempre calculada, mesmo sem compressao)
        public bool Passa { get { return Aplicavel && Ap <= 1.0; } }
    }

    // Flambagem de uma barra continua num plano. "No plano" = plano do portico (usa o eixo que esta no plano, Ix ou Iy
    // conforme "Fletir no eixo fraco"); "fora do plano" = perpendicular, usa o outro eixo.
    public class Flamb
    {
        public string Plano = "", Eixo = "";
        public double I, R, Ncr, Le, Lref, K, Lambda, Scr, Ap;   // m4, m, kN, m, m, -, -, MPa
        public double LDestr, LambdaT;                           // maior trecho sem travamento (m) e L/r (regra de tracao)
        public bool Euler, Instavel;
        public string Travas = "";                               // descricao dos pontos travados e das condicoes
        public string Molas = "";                                // travamentos elasticos (outras barras): no, rigidez
        public bool KMinimo; public double KTeorico, NcrElastico;                                    // K teorico < 1 elevado a 1 (nao se conta com engaste perfeito)
    }

    // ---------- flambagem por barra continua (independe da divisao em trechos) ----------
    // A barra continua (ou a barra isolada) e discretizada internamente em ~24 elementos de viga, com os travamentos
    // laterais de cada plano, e resolve-se o autovalor K.phi = Ncr.Kg.phi (Kg = matriz geometrica para N = 1 kN uniforme).
    // Como a compressao e tomada uniforme e igual a maior da barra, o resultado e conservador.
    //   No plano: trava rigida = apoio que impede o deslocamento perpendicular a barra; trava elastica = mola de apoio e
    //             rigidez do RESTO da estrutura (as outras barras ligadas no no, sem esta barra), calculada pelo Solver.
    //   Fora do plano: trava rigida = apoio (qualquer translacao travada) e os nos declarados em "Travamento fora do plano".
    //   Giro preso: apoio com giro travado, nos dois planos (hipotese). Rotulas da barra valem nos dois planos (lado seguro).
    public static class Flambagem
    {
        public const int Elementos = 24;
        static readonly Dictionary<string, double[]> cache = new Dictionary<string, double[]>();

        class Ponto { public double S, Mola, MolaGiro; public bool Rigido, GiroFixo, Rotula; public int No; }

        // Trechos da barra continua em ordem, e os nos ao longo dela (n+1 nos).
        public static List<Barra> Cadeia(Modelo m, Barra b, out List<int> nos)
        {
            List<Barra> bs = b.Membro != 0 ? m.Trechos(b.Membro) : new List<Barra> { b };
            nos = new List<int>();
            if (bs.Count <= 1) { nos.Add(b.N1); nos.Add(b.N2); return new List<Barra> { b }; }
            Dictionary<int, int> grau = new Dictionary<int, int>();
            foreach (Barra t in bs) { grau[t.N1] = (grau.ContainsKey(t.N1) ? grau[t.N1] : 0) + 1; grau[t.N2] = (grau.ContainsKey(t.N2) ? grau[t.N2] : 0) + 1; }
            int ini = bs[0].N1;
            foreach (KeyValuePair<int, int> g in grau) if (g.Value == 1) { ini = g.Key; break; }
            List<Barra> ord = new List<Barra>(); HashSet<Barra> usadas = new HashSet<Barra>();
            int atual = ini; nos.Add(atual);
            while (ord.Count < bs.Count)
            {
                Barra prox = bs.FirstOrDefault(t => !usadas.Contains(t) && (t.N1 == atual || t.N2 == atual));
                if (prox == null) break;
                usadas.Add(prox); ord.Add(prox);
                atual = prox.N1 == atual ? prox.N2 : prox.N1; nos.Add(atual);
            }
            if (ord.Count != bs.Count) { nos = new List<int> { b.N1, b.N2 }; return new List<Barra> { b }; }   // nao e uma linha: trata o trecho sozinho
            return ord;
        }

        static HashSet<int> NosTravFora(List<Barra> cad, List<int> nos)
        {
            HashSet<int> r = new HashSet<int>();
            foreach (Barra t in cad)
            {
                string s = (t.TravFora ?? "").Trim().ToLowerInvariant();
                if (s == "") continue;
                if (s == "todos") { foreach (int id in nos) r.Add(id); continue; }
                if (s == "pontas") { r.Add(nos[0]); r.Add(nos[nos.Count - 1]); continue; }
                foreach (string p in s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                { int id; if (int.TryParse(p.Trim().TrimStart('n'), out id)) r.Add(id); }
            }
            return r;
        }

        static string Assinatura(Modelo m)
        {
            StringBuilder sb = new StringBuilder();
            foreach (No n in m.Nos) sb.Append(n.Id).Append(':').Append(n.X.ToString("R")).Append(',').Append(n.Y.ToString("R")).Append(n.ApoioX ? 'x' : '-').Append(n.ApoioY ? 'y' : '-').Append(n.ApoioGiro ? 'g' : '-')
                    .Append(n.MolaX.ToString("R")).Append('/').Append(n.MolaY.ToString("R")).Append('/').Append(n.MolaGiro.ToString("R")).Append(';');
            foreach (Barra b in m.Barras) sb.Append(b.Id).Append(':').Append(b.N1).Append('-').Append(b.N2).Append(b.Secao).Append('|').Append(b.Material).Append(b.EixoFraco ? 'f' : 'F')
                    .Append(b.RotulaInicio ? 'r' : '-').Append(b.RotulaFim ? 'r' : '-').Append(b.Membro).Append('|').Append(b.TravFora).Append(';');
            return sb.ToString();
        }

        // Rigidez (kN/m) que o resto da estrutura (sem as barras da peca) oferece ao no na direcao (px, py). 0 = nenhuma.
        static double RigidezResto(Modelo m, HashSet<int> peca, int no, double px, double py)
        {
            Modelo c = Modelo.DeXml(m.ParaXml());
            c.Barras.RemoveAll(t => peca.Contains(t.Id));
            if (c.Barras.Count == 0) return 0;
            c.PesoProprio = false;
            foreach (No n in c.Nos) { n.Fx = n.Fy = n.Mz = 0; n.RecalqueX = n.RecalqueY = 0; }
            foreach (Barra t in c.Barras) { t.QGlobalX = t.QGlobalY = t.QLocalIni = t.QLocalFim = 0; }
            No alvo = c.NoPorId(no);
            if (alvo == null || !c.Barras.Any(t => t.N1 == no || t.N2 == no)) return 0;
            alvo.Fx = px; alvo.Fy = py;
            // a propria peca segura o no na direcao do seu eixo (rigidez axial): biela rigida temporaria nessa direcao,
            // senao uma escora sozinha viraria um balanco solto e a trava sairia ~0
            int idn = c.Nos.Max(q => q.Id) + 1, idb = c.Barras.Max(q => q.Id) + 1;
            No fixo = new No { Id = idn, X = alvo.X + py, Y = alvo.Y - px, ApoioX = true, ApoioY = true };
            c.Nos.Add(fixo);
            c.Barras.Add(new Barra { Id = idb, N1 = idn, N2 = no, Secao = "A=1000 I=1", Material = "E=200000", RotulaInicio = true, RotulaFim = true, Dono = c });
            Resultado r = Solver.Resolver(c);
            if (!r.Ok || alvo.Res == null) return 0;
            double d = alvo.Res[0] * px + alvo.Res[1] * py;
            return d > 1e-15 ? 1 / d : 0;
        }

        // Calcula os dois planos para a barra continua de b, com o perfil sec (pode ser outro que nao o da barra).
        public static void Calcular(Modelo m, Barra b, Secao sec, Material mat, Verif v)
        {
            List<int> nos;
            List<Barra> cad = Cadeia(m, b, out nos);
            HashSet<int> ids = new HashSet<int>(cad.Select(t => t.Id));
            double E = mat.E * 1000;   // kN/m2
            bool cant = sec.Forma == "L";
            double iPl = b.EixoFraco ? sec.Iy : sec.Ix, iFo = b.EixoFraco ? sec.Ix : sec.Iy;
            if (cant) { iPl = iFo = 0.39 * Math.Min(sec.Ix, sec.Iy); }   // cantoneira: flamba no eixo principal menor nos dois planos
            string ePl = cant ? "z (principal menor, 0,39·Imín)" : b.EixoFraco ? "y (fraco)" : "x (forte)";
            string eFo = cant ? "z (principal menor, 0,39·Imín)" : b.EixoFraco ? "x (forte)" : "y (fraco)";

            // posicoes ao longo da peca e direcao de cada trecho
            List<double> s = new List<double> { 0 };
            List<double[]> dir = new List<double[]>();
            for (int i = 0; i < cad.Count; i++)
            {
                No a = m.NoPorId(nos[i]), c = m.NoPorId(nos[i + 1]);
                double dx = c.X - a.X, dy = c.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
                s.Add(s[i] + l); dir.Add(new[] { dx / l, dy / l });
            }
            double Lt = s[s.Count - 1];
            HashSet<int> trav = NosTravFora(cad, nos);
            string chave = Assinatura(m) + "#" + string.Join(",", cad.Select(t => t.Id.ToString()).ToArray());

            for (int plano = 0; plano < 2; plano++)
            {
                List<Ponto> pts = new List<Ponto>();
                Flamb f = new Flamb { Plano = plano == 0 ? "no plano do pórtico" : "fora do plano", Eixo = plano == 0 ? ePl : eFo, I = plano == 0 ? iPl : iFo };
                List<string> desc = new List<string>(), molas = new List<string>();
                for (int j = 0; j < nos.Count; j++)
                {
                    No no = m.NoPorId(nos[j]);
                    double[] d = dir[Math.Min(j, dir.Count - 1)];
                    double px = -d[1], py = d[0];   // perpendicular a barra, no plano
                    Ponto p = new Ponto { S = s[j], No = no.Id };
                    // rotula de algum trecho neste no
                    if (j > 0) { Barra t = cad[j - 1]; if (t.N2 == no.Id ? t.RotulaFim : t.RotulaInicio) p.Rotula = true; }
                    if (j < cad.Count) { Barra t = cad[j]; if (t.N1 == no.Id ? t.RotulaInicio : t.RotulaFim) p.Rotula = true; }
                    bool apoio = no.ApoioX || no.ApoioY;
                    if (plano == 0)
                    {
                        p.Rigido = (no.ApoioX && Math.Abs(px) > 0.1) || (no.ApoioY && Math.Abs(py) > 0.1);
                        if (!p.Rigido)
                        {
                            p.Mola = no.MolaX * px * px + no.MolaY * py * py;
                            if (m.Barras.Any(t => !ids.Contains(t.Id) && (t.N1 == no.Id || t.N2 == no.Id)))
                            {
                                string k = chave + "@" + no.Id;
                                double[] kr;
                                if (!cache.TryGetValue(k, out kr)) { kr = new[] { RigidezResto(m, ids, no.Id, px, py) }; Guardar(k, kr); }
                                if (kr[0] > 0) molas.Add("nó " + no.Id + ": k = " + Num.Fmt(kr[0], 0) + " kN/m (outras barras)");
                                p.Mola += kr[0];
                            }
                            else if (p.Mola > 0) molas.Add("nó " + no.Id + ": mola k = " + Num.Fmt(p.Mola, 0) + " kN/m");
                        }
                        p.GiroFixo = no.ApoioGiro && !p.Rotula;
                        p.MolaGiro = p.GiroFixo || p.Rotula ? 0 : no.MolaGiro;
                    }
                    else
                    {
                        p.Rigido = apoio || trav.Contains(no.Id);
                        p.GiroFixo = no.ApoioGiro && !p.Rotula && apoio;
                    }
                    if (p.Rigido) desc.Add("nó " + no.Id + (p.GiroFixo ? " (travado, giro preso)" : " (travado)"));
                    pts.Add(p);
                }
                f.Travas = desc.Count > 0 ? string.Join(", ", desc.ToArray()) : "nenhum ponto travado";
                f.Molas = string.Join("; ", molas.ToArray());

                // maior trecho sem travamento (rigido ou elastico), contando as pontas da peca
                List<double> marcos = pts.Where(p => p.Rigido || p.Mola > 0).Select(p => p.S).ToList();
                marcos.Add(0); marcos.Add(Lt); marcos.Sort();
                f.LDestr = 0; for (int i = 1; i < marcos.Count; i++) f.LDestr = Math.Max(f.LDestr, marcos[i] - marcos[i - 1]);
                f.Lref = f.LDestr;
                f.R = Math.Sqrt(f.I / sec.A);
                f.LambdaT = f.LDestr / f.R;

                string k2 = chave + "|" + plano + "|" + f.I.ToString("R") + "|" + mat.E.ToString("R");
                double[] res;
                if (!cache.TryGetValue(k2, out res)) { res = new[] { Autovalor(Lt, E * f.I, pts) }; Guardar(k2, res); }
                double ncr = res[0];
                if (ncr <= 0 || double.IsNaN(ncr) || double.IsInfinity(ncr)) { f.Instavel = true; f.Le = double.PositiveInfinity; f.K = double.PositiveInfinity; f.Lambda = double.PositiveInfinity; }
                else
                {
                    f.Le = Math.PI * Math.Sqrt(E * f.I / ncr);
                    f.KTeorico = f.Le / f.Lref; f.NcrElastico = ncr;
                    if (f.Le < f.Lref) { f.Le = f.Lref; f.KMinimo = true; }   // nao se conta com engaste perfeito: K >= 1 no maior trecho
                    f.K = f.Le / f.Lref;
                    f.Lambda = f.Le / f.R;
                }
                if (plano == 0) v.NoPlano = f; else v.ForaPlano = f;
            }
        }

        static void Guardar(string k, double[] v) { if (cache.Count > 20000) cache.Clear(); cache[k] = v; }

        // Menor carga critica (kN) da peca com compressao uniforme. EI em kN.m2. 0 = mecanismo (sem travamento suficiente).
        static double Autovalor(double Lt, double EI, List<Ponto> pts)
        {
            // pontos que importam: pontas e pontos com alguma condicao (independe da malha do usuario)
            List<Ponto> marcos = pts.Where((p, i) => i == 0 || i == pts.Count - 1 || p.Rigido || p.Mola > 0 || p.GiroFixo || p.MolaGiro > 0 || p.Rotula).ToList();
            // malha interna
            List<Ponto> nos = new List<Ponto>();
            for (int i = 0; i < marcos.Count; i++)
            {
                if (i > 0)
                {
                    double a = marcos[i - 1].S, c = marcos[i].S;
                    int ne = Math.Max(2, (int)Math.Ceiling(Elementos * (c - a) / Lt - 1e-9));
                    for (int k = 1; k < ne; k++) nos.Add(new Ponto { S = a + (c - a) * k / ne });
                }
                nos.Add(marcos[i]);
            }
            int nn = nos.Count;
            // graus de liberdade: v por no; giro por no (dois na rotula: esquerda e direita)
            int[] gv = new int[nn], gl = new int[nn], gr = new int[nn];
            int nd = 0;
            for (int i = 0; i < nn; i++) { gv[i] = nd++; gl[i] = nd++; gr[i] = nos[i].Rotula && i > 0 && i < nn - 1 ? nd++ : gl[i]; }
            double[,] K = new double[nd, nd], G = new double[nd, nd];
            for (int e = 0; e < nn - 1; e++)
            {
                double L = nos[e + 1].S - nos[e].S;
                if (L <= 1e-12) continue;
                int[] g = { gv[e], gr[e], gv[e + 1], gl[e + 1] };
                double f = EI / (L * L * L), q = 1 / (30 * L);
                double[,] ke = { { 12 * f, 6 * L * f, -12 * f, 6 * L * f }, { 6 * L * f, 4 * L * L * f, -6 * L * f, 2 * L * L * f }, { -12 * f, -6 * L * f, 12 * f, -6 * L * f }, { 6 * L * f, 2 * L * L * f, -6 * L * f, 4 * L * L * f } };
                double[,] kg = { { 36 * q, 3 * L * q, -36 * q, 3 * L * q }, { 3 * L * q, 4 * L * L * q, -3 * L * q, -L * L * q }, { -36 * q, -3 * L * q, 36 * q, -3 * L * q }, { 3 * L * q, -L * L * q, -3 * L * q, 4 * L * L * q } };
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { K[g[i], g[j]] += ke[i, j]; G[g[i], g[j]] += kg[i, j]; }
            }
            bool[] preso = new bool[nd];
            for (int i = 0; i < nn; i++)
            {
                Ponto p = nos[i];
                if (p.Rigido) preso[gv[i]] = true; else K[gv[i], gv[i]] += p.Mola;
                if (p.GiroFixo) { preso[gl[i]] = true; preso[gr[i]] = true; }
                else if (p.MolaGiro > 0) { K[gl[i], gl[i]] += p.MolaGiro; }
            }
            List<int> liv = new List<int>();
            for (int i = 0; i < nd; i++) if (!preso[i]) liv.Add(i);
            int n = liv.Count;
            if (n == 0) return 0;
            double[,] A = new double[n, n], B = new double[n, n];
            double kmax = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { A[i, j] = K[liv[i], liv[j]]; B[i, j] = G[liv[i], liv[j]]; kmax = Math.Max(kmax, Math.Abs(A[i, j])); }
            // LU com pivotamento parcial
            int[] pv = new int[n];
            for (int c = 0; c < n; c++)
            {
                int pi = c;
                for (int i = c + 1; i < n; i++) if (Math.Abs(A[i, c]) > Math.Abs(A[pi, c])) pi = i;
                if (Math.Abs(A[pi, c]) < 1e-10 * kmax) return 0;   // matriz singular: mecanismo, a peca nao e estavel
                pv[c] = pi;
                if (pi != c) for (int j = 0; j < n; j++) { double t = A[c, j]; A[c, j] = A[pi, j]; A[pi, j] = t; }
                for (int i = c + 1; i < n; i++) { A[i, c] /= A[c, c]; for (int j = c + 1; j < n; j++) A[i, j] -= A[i, c] * A[c, j]; }
            }
            Func<double[], double[]> resolver = rhs =>
            {
                double[] y = (double[])rhs.Clone();
                for (int c = 0; c < n; c++) { if (pv[c] != c) { double t = y[c]; y[c] = y[pv[c]]; y[pv[c]] = t; } }
                for (int i = 0; i < n; i++) for (int j = 0; j < i; j++) y[i] -= A[i, j] * y[j];
                for (int i = n - 1; i >= 0; i--) { for (int j = i + 1; j < n; j++) y[i] -= A[i, j] * y[j]; y[i] /= A[i, i]; }
                return y;
            };
            // iteracao inversa: converge para o menor Ncr
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = 1 + 0.37 * Math.Sin(1.3 * i + 0.4);
            double lam = 0, ant = double.MaxValue;
            for (int it = 0; it < 3000; it++)
            {
                double[] y = new double[n];
                for (int i = 0; i < n; i++) { double t = 0; for (int j = 0; j < n; j++) t += B[i, j] * x[j]; y[i] = t; }
                double[] z = resolver(y);
                double num = 0, den = 0;
                for (int i = 0; i < n; i++) { num += z[i] * y[i]; }
                double[] bz = new double[n];
                for (int i = 0; i < n; i++) { double t = 0; for (int j = 0; j < n; j++) t += B[i, j] * z[j]; bz[i] = t; den += z[i] * t; }
                // Rayleigh: lam = zT K z / zT B z, com K z = y  =>  zT y / zT B z
                lam = den > 0 ? num / den : 0;
                double nz = 0; for (int i = 0; i < n; i++) nz += z[i] * z[i]; nz = Math.Sqrt(nz);
                if (nz <= 0) return 0;
                for (int i = 0; i < n; i++) x[i] = z[i] / nz;
                if (Math.Abs(lam - ant) <= 1e-13 * Math.Abs(lam)) break;
                ant = lam;
            }
            return lam;
        }
    }

    public static class Verificador
    {
        // 1 = ponta inicial solta, 2 = final solta, 0 = nenhuma (ou as duas)
        static int PontaSolta(Modelo m, Barra b)
        {
            Func<int, bool> solta = id =>
            {
                No no = m.Nos.FirstOrDefault(x => x.Id == id);
                return no != null && !no.TemApoio && m.Barras.Count(x => x.N1 == id || x.N2 == id) == 1;
            };
            bool s1 = solta(b.N1), s2 = solta(b.N2);
            return s1 == s2 ? 0 : s1 ? 1 : 2;
        }

        // sec = perfil a testar (pode ser outro que nao o da barra): os esforcos sao os ja calculados.
        public static Verif Barra(Modelo m, Barra b, Secao sec)
        {
            Verif v = new Verif();
            ResBarra r = b.Res;
            Material mat = Biblioteca.AcharMaterial(b.Material);
            if (r == null || sec == null || mat == null) { v.Motivo = "Sem resultados."; return v; }
            if (mat.Fy <= 0) { v.Motivo = "O material não tem tensão de escoamento cadastrada."; return v; }
            double I = b.EixoFraco ? sec.Iy : sec.Ix, W = b.EixoFraco ? sec.Wy : sec.Wx, As = b.EixoFraco ? sec.AsY : sec.AsX;
            if (W <= 0 || sec.A <= 0) { v.Motivo = "Seção genérica: faltam as medidas para calcular a tensão."; return v; }
            double n = m.Seguranca, fy = mat.Fy, E = mat.E;
            v.Aplicavel = true;

            double qmax = 0, nmin = 0;
            List<double> xs = r.Pontos(2);
            foreach (double x in xs)
            {
                double N = r.N(x), M = r.M(x);
                v.Sigma = Math.Max(v.Sigma, (Math.Abs(N) / sec.A + Math.Abs(M) / W) / 1000);
                qmax = Math.Max(qmax, Math.Abs(r.Q(x)));
                nmin = Math.Min(nmin, N);
            }
            v.SigmaAdm = fy / n;
            v.ApTensao = v.Sigma / v.SigmaAdm;

            v.TauAdm = 0.577 * fy / n;
            if (As > 0) { v.Tau = qmax / As / 1000; v.ApCorte = v.Tau / v.TauAdm; }

            // flecha: a calculada, corrigida pela razao das rigidezes quando o perfil testado e outro
            // balanco (uma ponta solta, sem apoio e sem outra barra): flecha da ponta em relacao a tangente na outra ponta,
            // nao em relacao a corda (a corda gira junto e daria 1/4 do valor real no balanco com momento na ponta)
            // No balanco vale o deslocamento total da ponta (inclui o giro e o deslocamento do no onde o balanco nasce,
            // como no topo de um pilar escorado), comparado com a distancia ate o apoio mais proximo / limite.
            int solta = PontaSolta(m, b);
            v.Balanco = solta != 0;
            if (v.Balanco)
            {
                double proprio = solta == 2 ? r.V(r.L) - r.U[1] - r.U[2] * r.L : r.V(0) - r.U[4] + r.U[5] * r.L;   // so a flexao desta barra
                double total = solta == 2 ? r.U[4] : r.U[1];                                                         // deslocamento absoluto da ponta
                v.Flecha = Math.Abs(total - proprio + proprio * r.EI / (E * 1000 * I));                               // so a parcela da barra muda com o perfil
                No livre = m.Nos.FirstOrDefault(x => x.Id == (solta == 2 ? b.N2 : b.N1));
                double dist = double.MaxValue;
                if (livre != null)
                    foreach (No ap in m.Nos.Where(x => x.TemApoio))
                        dist = Math.Min(dist, Math.Sqrt((ap.X - livre.X) * (ap.X - livre.X) + (ap.Y - livre.Y) * (ap.Y - livre.Y)));
                if (dist == double.MaxValue) dist = r.L;
                v.FlechaAdm = Math.Max(dist, r.L) / m.LimiteFlecha;
            }
            else
            {
                v.Flecha = Math.Abs(r.FlechaMax()) * r.EI / (E * 1000 * I);
                v.FlechaAdm = r.L / m.LimiteFlecha;
            }
            v.ApFlecha = v.Flecha / v.FlechaAdm;

            v.Nc = -nmin;
            foreach (double x in xs) v.Nt = Math.Max(v.Nt, r.N(x));
            // Flambagem nos DOIS planos, pela barra continua inteira (Flambagem.Calcular). Cantoneira: eixo principal
            // menor (Iz ~ 0,39.Imin) nos dois planos. Euler/Johnson com lambda = Le/r de cada plano; governa o pior.
            Flambagem.Calcular(m, b, sec, mat, v);
            double l1 = Math.Sqrt(2 * Math.PI * Math.PI * E / fy);
            Flamb gov = null;
            foreach (Flamb f in new[] { v.NoPlano, v.ForaPlano })
            {
                if (f.Instavel) { f.Ncr = 0; f.Scr = 0; f.Ap = v.Nc > 1e-6 ? 99.99 : 0; }
                else
                {
                    f.Euler = f.Lambda > l1;
                    f.Scr = f.Euler ? Math.PI * Math.PI * E / (f.Lambda * f.Lambda) : fy - Math.Pow(fy * f.Lambda / (2 * Math.PI), 2) / E;   // MPa
                    f.Ncr = f.Scr * 1000 * sec.A;
                    f.Ap = v.Nc > 1e-6 ? v.Nc * n / f.Ncr : 0;
                }
                if (gov == null || f.Instavel && !gov.Instavel || (f.Instavel == gov.Instavel && f.Lambda > gov.Lambda)) gov = f;
            }
            v.K = gov.K; v.Esbeltez = gov.Lambda; v.Ncr = gov.Ncr;
            if (v.Nc > 1e-6) v.ApFlamb = Math.Max(v.NoPlano.Ap, v.ForaPlano.Ap);
            // Esbeltez limite (NBR 8800, 5.2.8 e 5.3.4): comprimida KL/r <= 200; so tracionada L/r <= 300 (L = maior trecho sem travamento)
            if (v.Nc > 1e-6) { v.LimEsbeltez = 200; v.ApEsbeltez = Math.Min(99.99, v.Esbeltez / 200); }
            else if (v.Nt > 1e-6)
            {
                v.LimEsbeltez = 300;
                double lt = Math.Max(v.NoPlano.LambdaT, v.ForaPlano.LambdaT);
                v.ApEsbeltez = lt / 300;
                // nao ha compressao NESTE resultado: a flambagem nao foi verificada (passaria por omissao)
                if (m.TemRotativa || m.Combinacoes == null || m.Combinacoes.Count == 0)
                    v.AvisoInversao = "barra sem compressão neste resultado: flambagem não verificada; confirme se o esforço pode inverter" +
                                      (m.TemRotativa ? " (há carga giratória: confira a envoltória das posições 0° e 180°)" : " (o modelo não tem combinações)");
            }
            // Interacao flexo-compressao com estabilidade (forma ASD/AISC H1 simplificada, Cm = 1 a favor da seguranca):
            //   n.Nc/Ncr + B.n.|M|max/(W.fy) <= 1,  B = 1/(1 - n.Nc/Ne),  Ne = pi2.E.I/Le2 elastica NO PLANO (onde ha momento)
            if (v.Nc > 1e-6)
            {
                double mmax = 0; foreach (double x in xs) mmax = Math.Max(mmax, Math.Abs(r.M(x)));
                v.Mmax = mmax;
                double ncrMin = Math.Min(v.NoPlano.Ncr, v.ForaPlano.Ncr);
                v.NeNoPlano = v.NoPlano.Instavel ? 0 : Math.PI * Math.PI * E * 1000 * v.NoPlano.I / (v.NoPlano.Le * v.NoPlano.Le);
                double razao = v.NeNoPlano > 0 ? n * v.Nc / v.NeNoPlano : double.PositiveInfinity;
                v.BAmp = razao < 1 ? 1 / (1 - razao) : double.PositiveInfinity;
                double parcN = ncrMin > 0 ? n * v.Nc / ncrMin : double.PositiveInfinity, parcM = mmax > 0 ? v.BAmp * n * mmax / (W * fy * 1000) : 0;
                v.ApInteracao = double.IsInfinity(parcN) || double.IsInfinity(parcM) || double.IsNaN(parcM) ? 99.99 : Math.Min(99.99, parcN + parcM);
            }
            v.Ap = v.ApTensao; v.Critico = "tensão";
            if (v.ApInteracao > v.Ap) { v.Ap = v.ApInteracao; v.Critico = "interação N + M (flambagem)"; }
            if (v.ApCorte > v.Ap) { v.Ap = v.ApCorte; v.Critico = "corte"; }
            if (v.ApFlecha > v.Ap) { v.Ap = v.ApFlecha; v.Critico = "flecha"; }
            if (v.ApFlamb > v.Ap) { v.Ap = v.ApFlamb; v.Critico = "flambagem " + (gov == v.ForaPlano ? "fora do plano" : "no plano"); }
            if (v.ApEsbeltez > v.Ap) { v.Ap = v.ApEsbeltez; v.Critico = "esbeltez " + (v.LimEsbeltez == 200 ? "KL/r (lim. 200)" : "L/r (lim. 300)"); }

            if (v.Nc > 1e-6 && (gov.Instavel || v.Esbeltez > 200))
                v.Aviso = gov.Instavel ? "flambagem " + gov.Plano + ": sem travamento suficiente (mecanismo) — declare o travamento fora do plano ou apoie a barra"
                                       : "esbeltez KL/r = " + v.Esbeltez.ToString("0") + " > 200 " + gov.Plano + " (limite da NBR 8800 para peças comprimidas)";
            else if (v.Nc <= 1e-6 && v.Nt > 1e-6 && v.ApEsbeltez > 1)
                v.Aviso = "esbeltez L/r = " + (v.ApEsbeltez * 300).ToString("0") + " > 300 (limite da NBR 8800 para peças tracionadas)";
            // parede fina: a verificacao nao cobre flambagem local; avisa quando passa dos limites da NBR 8800
            double esp = sec.Tw > 0 ? sec.Tw : 0;
            if (sec.Forma == "TC" && esp > 0)
            {
                double dt = sec.H / esp, lim = 0.11 * E / fy;
                if (dt > lim) v.Aviso += (v.Aviso != "" ? "; " : "") + "parede esbelta: D/t = " + dt.ToString("0") + " > 0,11·E/fy = " + lim.ToString("0") + " (flambagem local não verificada; resistência real menor)";
            }
            else if (sec.Forma == "TR" && esp > 0)
            {
                double bt = (Math.Max(sec.H, sec.Bf) - 3 * esp) / esp, lim = 1.40 * Math.Sqrt(E / fy);
                if (bt > lim) v.Aviso += (v.Aviso != "" ? "; " : "") + "parede esbelta: b/t = " + bt.ToString("0") + " > 1,40·√(E/fy) = " + lim.ToString("0") + " (flambagem local não verificada; resistência real menor)";
            }
            return v;
        }
    }

    // ---------- resultados ----------

    public class ResBarra
    {
        public double L, C, S, EA, EI, GAs;
        public double P1, P2, Q1, Q2;          // cargas distribuidas locais (axial e transversal), inicio e fim
        public double[] U = new double[6];     // deslocamentos locais dos nos
        public double[] F = new double[6];     // esforcos dos nos sobre a barra, eixos locais
        double th0;

        // Positivo: N tracao; M tracao na fibra de baixo (lado -y local).
        public double N(double x) { return -F[0] - P1 * x - (P2 - P1) * x * x / (2 * L); }
        public double Q(double x) { return F[1] + Q1 * x + (Q2 - Q1) * x * x / (2 * L); }
        public double M(double x) { return -F[2] + F[1] * x + Q1 * x * x / 2 + (Q2 - Q1) * x * x * x / (6 * L); }

        double Vb(double x)
        {
            double dq = Q2 - Q1, x2 = x * x, x3 = x2 * x;
            double v = (-F[2] * x2 / 2 + F[1] * x3 / 6 + Q1 * x2 * x2 / 24 + dq * x3 * x2 / (120 * L)) / EI;
            if (GAs > 0) v -= (F[1] * x + Q1 * x2 / 2 + dq * x3 / (6 * L)) / GAs;
            return v;
        }

        public void Fechar() { th0 = (U[4] - U[1] - Vb(L)) / L; }

        // Deslocamentos locais ao longo da barra (elastica exata para carga linear).
        public double V(double x) { return U[1] + th0 * x + Vb(x); }
        public double Ux(double x) { return U[0] + (-F[0] * x - P1 * x * x / 2 - (P2 - P1) * x * x * x / (6 * L)) / EA; }

        public double Valor(int tipo, double x) { return tipo == 0 ? N(x) : tipo == 1 ? Q(x) : M(x); }

        // Pontos onde procurar extremos: pontas, raizes da derivada e amostragem.
        public List<double> Pontos(int tipo)
        {
            List<double> xs = new List<double>();
            for (int i = 0; i <= 20; i++) xs.Add(L * i / 20.0);
            if (tipo == 2)
            {
                double a = (Q2 - Q1) / (2 * L), b = Q1, c = F[1];
                if (Math.Abs(a) < 1e-12) { if (Math.Abs(b) > 1e-12) xs.Add(-c / b); }
                else
                {
                    double d = b * b - 4 * a * c;
                    if (d >= 0) { xs.Add((-b + Math.Sqrt(d)) / (2 * a)); xs.Add((-b - Math.Sqrt(d)) / (2 * a)); }
                }
            }
            xs.RemoveAll(delegate(double x) { return x < 0 || x > L; });
            return xs;
        }

        public void Extremos(int tipo, out double mn, out double mx)
        {
            mn = double.MaxValue; mx = double.MinValue;
            foreach (double x in Pontos(tipo))
            {
                double v = Valor(tipo, x);
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
        }

        public double FlechaMax()
        {
            double best = 0;
            for (int i = 0; i <= 100; i++)
            {
                double x = L * i / 100.0;
                double f = V(x) - (U[1] + (U[4] - U[1]) * x / L);
                if (Math.Abs(f) > Math.Abs(best)) best = f;
            }
            return best;
        }
    }

    public class Resultado
    {
        public bool Ok;
        public string Erro = "";
        public double SomaRx, SomaRy;
        public double MaxDesl;   // maior deslocamento de no, m
    }

    public static class Solver
    {
        const double g = 9.80665;

        public static Resultado Resolver(Modelo m)
        {
            Resultado r = new Resultado();
            foreach (No n in m.Nos) n.Res = null;
            foreach (Barra b in m.Barras) b.Res = null;
            if (m.Barras.Count == 0) { r.Erro = "Desenhe pelo menos uma barra."; return r; }

            int nn = m.Nos.Count, nd = 3 * nn;
            Dictionary<int, int> idx = new Dictionary<int, int>();
            for (int i = 0; i < nn; i++) idx[m.Nos[i].Id] = i;

            double[,] K = new double[nd, nd];
            double[] F = new double[nd];
            List<ResBarra> rbs = new List<ResBarra>();
            List<double[,]> ks = new List<double[,]>();
            List<double[]> fes = new List<double[]>();

            foreach (Barra b in m.Barras)
            {
                No a = m.Nos[idx[b.N1]], c = m.Nos[idx[b.N2]];
                double dx = c.X - a.X, dy = c.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 1e-9) { r.Erro = "Há uma barra com comprimento zero."; return r; }
                Secao sec = Biblioteca.AcharSecao(b.Secao);
                if (sec == null) { r.Erro = "Perfil não reconhecido: \"" + b.Secao + "\"."; return r; }
                Material mat = Biblioteca.AcharMaterial(b.Material);
                if (mat == null) { r.Erro = "Material não reconhecido: \"" + b.Material + "\"."; return r; }

                ResBarra rb = new ResBarra();
                rb.L = L; rb.C = dx / L; rb.S = dy / L;
                double E = mat.E * 1000;   // MPa -> kN/m2
                rb.EA = E * sec.A;
                rb.EI = E * (b.EixoFraco ? sec.Iy : sec.Ix);
                double As = b.EixoFraco ? sec.AsY : sec.AsX;
                rb.GAs = (m.Timoshenko && As > 0) ? mat.G * 1000 * As : 0;

                double peso = 0;
                if (m.PesoProprio) peso = (sec.Massa > 0 ? sec.Massa : mat.Densidade * sec.A) * g / 1000;
                double qx = b.QGlobalX, qy = b.QGlobalY - peso;
                double p = rb.C * qx + rb.S * qy, q = -rb.S * qx + rb.C * qy;
                rb.P1 = p; rb.P2 = p;
                rb.Q1 = q + b.QLocalIni; rb.Q2 = q + b.QLocalFim;

                double phi = rb.GAs > 0 ? 12 * rb.EI / (rb.GAs * L * L) : 0;
                double[,] k = new double[6, 6];
                double ea = rb.EA / L, f = rb.EI / ((1 + phi) * L * L * L);
                k[0, 0] = ea; k[3, 3] = ea; k[0, 3] = -ea;
                k[1, 1] = 12 * f; k[1, 2] = 6 * f * L; k[1, 4] = -12 * f; k[1, 5] = 6 * f * L;
                k[2, 2] = (4 + phi) * f * L * L; k[2, 4] = -6 * f * L; k[2, 5] = (2 - phi) * f * L * L;
                k[4, 4] = 12 * f; k[4, 5] = -6 * f * L;
                k[5, 5] = (4 + phi) * f * L * L;
                for (int i = 0; i < 6; i++) for (int j = 0; j < i; j++) k[i, j] = k[j, i];

                // Cargas nodais equivalentes (locais) para carga linear.
                double[] fe = new double[6];
                fe[0] = L * (2 * rb.P1 + rb.P2) / 6;
                fe[3] = L * (rb.P1 + 2 * rb.P2) / 6;
                double m1 = L * L * (3 * rb.Q1 + 2 * rb.Q2) / 60, m2 = -L * L * (2 * rb.Q1 + 3 * rb.Q2) / 60;
                double sim = (m1 - m2) / 2, ant = (m1 + m2) / 2 / (1 + phi);
                m1 = sim + ant; m2 = -sim + ant;
                fe[1] = L * (2 * rb.Q1 + rb.Q2) / 6 + (m1 + m2) / L;
                fe[4] = L * (rb.Q1 + 2 * rb.Q2) / 6 - (m1 + m2) / L;
                fe[2] = m1; fe[5] = m2;

                if (b.RotulaInicio) Condensar(k, fe, 2);
                if (b.RotulaFim) Condensar(k, fe, 5);

                // Global = Tt k T
                double[,] T = Rot(rb.C, rb.S);
                int[] gl = { 3 * idx[b.N1], 3 * idx[b.N1] + 1, 3 * idx[b.N1] + 2, 3 * idx[b.N2], 3 * idx[b.N2] + 1, 3 * idx[b.N2] + 2 };
                double[,] kT = new double[6, 6];
                for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++)
                    { double s = 0; for (int t = 0; t < 6; t++) s += k[i, t] * T[t, j]; kT[i, j] = s; }
                for (int i = 0; i < 6; i++)
                {
                    double sf = 0;
                    for (int t = 0; t < 6; t++) sf += T[t, i] * fe[t];
                    F[gl[i]] += sf;
                    for (int j = 0; j < 6; j++)
                    {
                        double s = 0;
                        for (int t = 0; t < 6; t++) s += T[t, i] * kT[t, j];
                        K[gl[i], gl[j]] += s;
                    }
                }
                rbs.Add(rb); ks.Add(k); fes.Add(fe);
            }

            bool[] preso = new bool[nd];
            double[] u = new double[nd];
            double[] mola = new double[nd];
            for (int i = 0; i < nn; i++)
            {
                No n = m.Nos[i];
                F[3 * i] += n.Fx; F[3 * i + 1] += n.Fy; F[3 * i + 2] += n.Mz;
                mola[3 * i] = n.MolaX; mola[3 * i + 1] = n.MolaY; mola[3 * i + 2] = n.MolaGiro;
                preso[3 * i] = n.ApoioX; preso[3 * i + 1] = n.ApoioY; preso[3 * i + 2] = n.ApoioGiro;
                if (n.ApoioX) u[3 * i] = n.RecalqueX / 1000;
                if (n.ApoioY) u[3 * i + 1] = n.RecalqueY / 1000;
            }
            double kmax = 0;
            for (int i = 0; i < nd; i++) { K[i, i] += mola[i]; if (K[i, i] > kmax) kmax = K[i, i]; }
            // Grau de liberdade sem rigidez nenhuma (ex.: giro de no onde todas as barras sao rotuladas): fica parado.
            bool[] morto = new bool[nd];
            for (int i = 0; i < nd; i++)
                if (!preso[i] && Math.Abs(K[i, i]) < 1e-12 * kmax) { morto[i] = true; preso[i] = true; }
            // Travar um giro sem rigidez e inofensivo; ja uma forca/momento nessa direcao sumiria do calculo sem aviso.
            for (int i = 0; i < nd; i++)
                if (morto[i] && Math.Abs(F[i]) > 1e-9)
                {
                    string dir = i % 3 == 0 ? "horizontal" : i % 3 == 1 ? "vertical" : "de giro";
                    r.Erro = "Nó " + m.Nos[i / 3].Id + ": carga na direção " + dir + ", mas nenhuma barra resiste nessa direção (mecanismo). Ligue uma barra ou tire uma rótula.";
                    return r;
                }

            List<int> livres = new List<int>();
            for (int i = 0; i < nd; i++) if (!preso[i]) livres.Add(i);
            int nl = livres.Count;
            double[,] A = new double[nl, nl + 1];
            for (int i = 0; i < nl; i++)
            {
                double rhs = F[livres[i]];
                for (int j = 0; j < nd; j++) if (preso[j] && u[j] != 0) rhs -= K[livres[i], j] * u[j];
                for (int j = 0; j < nl; j++) A[i, j] = K[livres[i], livres[j]];
                A[i, nl] = rhs;
            }
            // Eliminacao de Gauss com pivotamento parcial.
            for (int c = 0; c < nl; c++)
            {
                int piv = c;
                for (int i = c + 1; i < nl; i++) if (Math.Abs(A[i, c]) > Math.Abs(A[piv, c])) piv = i;
                if (Math.Abs(A[piv, c]) < 1e-9 * kmax)
                {
                    r.Erro = "Estrutura instável (hipostática): faltam apoios ou há rótulas demais.";
                    return r;
                }
                if (piv != c) for (int j = c; j <= nl; j++) { double t = A[c, j]; A[c, j] = A[piv, j]; A[piv, j] = t; }
                for (int i = c + 1; i < nl; i++)
                {
                    double fct = A[i, c] / A[c, c];
                    if (fct == 0) continue;
                    for (int j = c; j <= nl; j++) A[i, j] -= fct * A[c, j];
                }
            }
            for (int i = nl - 1; i >= 0; i--)
            {
                double s = A[i, nl];
                for (int j = i + 1; j < nl; j++) s -= A[i, j] * u[livres[j]];
                u[livres[i]] = s / A[i, i];
            }

            for (int i = 0; i < nn; i++)
            {
                double[] res = new double[6];
                for (int d = 0; d < 3; d++)
                {
                    int gdl = 3 * i + d;
                    res[d] = u[gdl];
                    double reac = -mola[gdl] * u[gdl];
                    if (preso[gdl] && !morto[gdl])
                    {
                        double s = -F[gdl];
                        for (int j = 0; j < nd; j++) s += K[gdl, j] * u[j];
                        reac = s - mola[gdl] * u[gdl];
                    }
                    res[3 + d] = reac;
                }
                m.Nos[i].Res = res;
                r.SomaRx += res[3]; r.SomaRy += res[4];
                double dd = Math.Sqrt(res[0] * res[0] + res[1] * res[1]);
                if (dd > r.MaxDesl) r.MaxDesl = dd;
            }

            for (int e = 0; e < m.Barras.Count; e++)
            {
                Barra b = m.Barras[e];
                ResBarra rb = rbs[e];
                double[,] T = Rot(rb.C, rb.S);
                int[] gl = { 3 * idx[b.N1], 3 * idx[b.N1] + 1, 3 * idx[b.N1] + 2, 3 * idx[b.N2], 3 * idx[b.N2] + 1, 3 * idx[b.N2] + 2 };
                for (int i = 0; i < 6; i++)
                {
                    double s = 0;
                    for (int j = 0; j < 6; j++) s += T[i, j] * u[gl[j]];
                    rb.U[i] = s;
                }
                for (int i = 0; i < 6; i++)
                {
                    double s = -fes[e][i];
                    for (int j = 0; j < 6; j++) s += ks[e][i, j] * rb.U[j];
                    rb.F[i] = s;
                }
                rb.Fechar();
                b.Res = rb;
            }
            r.Ok = true;
            return r;
        }

        static double[,] Rot(double c, double s)
        {
            double[,] T = new double[6, 6];
            T[0, 0] = c; T[0, 1] = s; T[1, 0] = -s; T[1, 1] = c; T[2, 2] = 1;
            T[3, 3] = c; T[3, 4] = s; T[4, 3] = -s; T[4, 4] = c; T[5, 5] = 1;
            return T;
        }

        // Condensacao estatica do giro liberado pela rotula.
        static void Condensar(double[,] k, double[] fe, int r)
        {
            double krr = k[r, r];
            if (Math.Abs(krr) < 1e-30) return;
            double[] col = new double[6], lin = new double[6];
            for (int i = 0; i < 6; i++) { col[i] = k[i, r]; lin[i] = k[r, i]; }
            double fr = fe[r];
            for (int i = 0; i < 6; i++)
            {
                fe[i] -= col[i] * fr / krr;
                for (int j = 0; j < 6; j++) k[i, j] -= col[i] * lin[j] / krr;
            }
            for (int i = 0; i < 6; i++) { k[i, r] = 0; k[r, i] = 0; }
            fe[r] = 0;
        }
    }

    // ---------- comparacao de fixacoes (docs/FIXACOES-GUIA.md, fixacoes.csv e fixacoes-pares.csv) ----------

    public class FixacaoLinha
    {
        public string Linha, Fabricante, Familia, Acionamento, Elemento, FaixaDiametro, FckEnsaio;
        public string NaturezaCarga, TracaoTabelada, CorteTabelado, CargaDinamica, DistanciasReduzidas;
        public string MateriaisBase, Recuperavel, TempoCura, Acabamento, Paginas;
        public string TipoDesc, Caracteristicas, UsoRecomendado, UsosTipicos, PontosAtencao, FaixaCargas;
        public bool Informativa, SemCatalogo;
        public string NomeCurto { get { return (Linha ?? "").Replace("Walsywa ", ""); } }
    }

    public class ParFixacao
    {
        public string Id, Titulo, NomeA, NomeB, LinhasA, LinhasB;
        public string EmQueDiferem, EscolhaAQuando, EscolhaBQuando, MesmoDesempenho, Fonte;

        public bool Envolve(string linha1, string linha2)
        {
            if (string.IsNullOrEmpty(linha1) || string.IsNullOrEmpty(linha2) || linha1 == linha2) return false;
            string[] a = (LinhasA ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            string[] b = (LinhasB ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            bool a1 = a.Contains(linha1), b1 = b.Contains(linha1);
            bool a2 = a.Contains(linha2), b2 = b.Contains(linha2);
            return (a1 && b2) || (a2 && b1);
        }
    }

    public class ColunaComparacao
    {
        public Chumbador Ch;          // null nas linhas so informativas (WLT, Golpe de Metal, PU 500)
        public FixacaoLinha Info;     // atributos e textos da linha (fixacoes.csv)
        public ResPlaca Res;          // verificacao na placa atual por PlacaCalc.CandidatoChumbador (null se informativa ou sem esforcos)
        public string Chave { get { return Ch != null ? Ch.Codigo : (Info != null ? Info.Linha : ""); } }
        public string Titulo { get { return Ch != null ? Ch.Codigo : (Info != null ? Info.Linha : ""); } }
        public string Subtitulo { get { return Ch != null ? Ch.Linha + " · Ø" + Ch.Pol + " · hef " + Ch.Hef.ToString("0") + " mm" : (Info != null ? "Linha somente informativa (fora do cálculo)" : ""); } }
    }

    public static class Fixacoes
    {
        public static readonly List<FixacaoLinha> Linhas = new List<FixacaoLinha>();
        public static readonly List<ParFixacao> Pares = new List<ParFixacao>();

        static Fixacoes()
        {
            CarregarLinhas();
            CarregarPares();
        }

        static void CarregarLinhas()
        {
            string[] ls = Biblioteca.Linhas("fixacoes.csv");
            if (ls == null || ls.Length < 2) return;
            string[] cab = ls[0].Trim().Split(';');
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int j = 0; j < cab.Length; j++) col[cab[j].Trim()] = j;
            Func<string[], string, string> S = delegate(string[] c, string k)
            {
                return col.ContainsKey(k) && col[k] < c.Length ? c[col[k]].Trim() : "";
            };
            for (int i = 1; i < ls.Length; i++)
            {
                string lin = ls[i].Trim();
                if (lin == "") continue;
                string[] c = lin.Split(';');
                Linhas.Add(new FixacaoLinha
                {
                    Linha = S(c, "linha"),
                    Fabricante = S(c, "fabricante"),
                    Informativa = S(c, "informativa") == "sim",
                    SemCatalogo = S(c, "sem_catalogo") == "sim",
                    Familia = S(c, "familia"),
                    Acionamento = S(c, "acionamento"),
                    Elemento = S(c, "elemento"),
                    FaixaDiametro = S(c, "faixa_diametro"),
                    FckEnsaio = S(c, "fck_ensaio"),
                    NaturezaCarga = S(c, "natureza_carga"),
                    TracaoTabelada = S(c, "tracao_tabelada"),
                    CorteTabelado = S(c, "corte_tabelado"),
                    CargaDinamica = S(c, "carga_dinamica"),
                    DistanciasReduzidas = S(c, "distancias_reduzidas"),
                    MateriaisBase = S(c, "materiais_base"),
                    Recuperavel = S(c, "recuperavel"),
                    TempoCura = S(c, "tempo_cura"),
                    Acabamento = S(c, "acabamento"),
                    Paginas = S(c, "paginas"),
                    TipoDesc = S(c, "tipo_desc"),
                    Caracteristicas = S(c, "caracteristicas"),
                    UsoRecomendado = S(c, "uso_recomendado"),
                    UsosTipicos = S(c, "usos_tipicos"),
                    PontosAtencao = S(c, "pontos_atencao"),
                    FaixaCargas = S(c, "faixa_cargas")
                });
            }
        }

        static void CarregarPares()
        {
            string[] ls = Biblioteca.Linhas("fixacoes-pares.csv");
            if (ls == null || ls.Length < 2) return;
            string[] cab = ls[0].Trim().Split(';');
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int j = 0; j < cab.Length; j++) col[cab[j].Trim()] = j;
            Func<string[], string, string> S = delegate(string[] c, string k)
            {
                return col.ContainsKey(k) && col[k] < c.Length ? c[col[k]].Trim() : "";
            };
            for (int i = 1; i < ls.Length; i++)
            {
                string lin = ls[i].Trim();
                if (lin == "") continue;
                string[] c = lin.Split(';');
                string tit = S(c, "titulo");
                string[] ab = tit.Split(new[] { '×' }, StringSplitOptions.RemoveEmptyEntries);
                Pares.Add(new ParFixacao
                {
                    Id = S(c, "id"),
                    Titulo = tit,
                    NomeA = ab.Length > 0 ? ab[0].Trim() : "A",
                    NomeB = ab.Length > 1 ? ab[1].Trim() : "B",
                    LinhasA = S(c, "linhas_a"),
                    LinhasB = S(c, "linhas_b"),
                    EmQueDiferem = S(c, "em_que_diferem"),
                    EscolhaAQuando = S(c, "escolha_a_quando"),
                    EscolhaBQuando = S(c, "escolha_b_quando"),
                    MesmoDesempenho = S(c, "mesmo_desempenho"),
                    Fonte = S(c, "fonte")
                });
            }
        }

        public static FixacaoLinha AcharLinha(string nomeOuLinha)
        {
            if (string.IsNullOrEmpty(nomeOuLinha)) return null;
            string s = nomeOuLinha.Trim();
            FixacaoLinha f = Linhas.FirstOrDefault(x => string.Equals(x.Linha, s, StringComparison.OrdinalIgnoreCase) ||
                                                        string.Equals(x.NomeCurto, s, StringComparison.OrdinalIgnoreCase));
            if (f != null) return f;
            Chumbador ch = Chumbadores.Achar(s);
            if (ch != null) return Linhas.FirstOrDefault(x => x.Linha == ch.Linha);
            return null;
        }

        public static List<FixacaoLinha> Informativas()
        {
            return Linhas.Where(x => x.Informativa).ToList();
        }

        public static ColunaComparacao Avaliar(Chumbador c, ResPlaca rBase, PlacaBase p, Modelo m)
        {
            if (c == null) return null;
            FixacaoLinha info = AcharLinha(c.Linha);
            ResPlaca tc = rBase != null && rBase.TemEsforcos ? PlacaCalc.CandidatoChumbador(rBase, p, m, c) : null;
            return new ColunaComparacao { Ch = c, Info = info, Res = tc };
        }

        public static ColunaComparacao Avaliar(string nomeOuCodigo, ResPlaca rBase, PlacaBase p, Modelo m)
        {
            if (string.IsNullOrEmpty(nomeOuCodigo)) return null;
            string s = nomeOuCodigo.Trim();
            Chumbador c = Chumbadores.Achar(s);
            if (c == null)
                c = Chumbadores.Lista.FirstOrDefault(x => string.Equals(x.Codigo, s, StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(x.Nome, s, StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(x.Linha.Replace("Walsywa ", "") + " " + x.Pol + " hef " + x.Hef.ToString("0"), s, StringComparison.OrdinalIgnoreCase));
            if (c != null) return Avaliar(c, rBase, p, m);
            FixacaoLinha info = AcharLinha(s);
            if (info == null) return null;
            if (!info.Informativa)
            {
                Chumbador primeiro = Chumbadores.DaLinha(info.Linha).FirstOrDefault();
                if (primeiro != null) return Avaliar(primeiro, rBase, p, m);
            }
            return new ColunaComparacao { Ch = null, Info = info, Res = null };
        }

        public static List<ParFixacao> ParesPara(IEnumerable<string> linhasEscolhidas)
        {
            List<ParFixacao> res = new List<ParFixacao>();
            if (linhasEscolhidas == null) return res;
            List<string> ls = linhasEscolhidas.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            foreach (ParFixacao par in Pares)
            {
                bool achou = false;
                for (int i = 0; i < ls.Count && !achou; i++)
                    for (int j = i + 1; j < ls.Count && !achou; j++)
                        if (par.Envolve(ls[i], ls[j])) achou = true;
                if (achou) res.Add(par);
            }
            return res;
        }

        public static List<string> Avisos(IEnumerable<ColunaComparacao> colunas, ResPlaca rBase)
        {
            List<string> av = new List<string>();
            if (colunas == null) return av;
            List<ColunaComparacao> cols = colunas.Where(c => c != null && c.Info != null).ToList();
            if (cols.Count == 0) return av;

            // 1. Ruptura x Recomendada nao comparaveis direto
            bool temRuptura = cols.Any(c => (c.Ch != null && c.Ch.Tu > 0) || c.Info.NaturezaCarga.Contains("ruptura"));
            bool temRec = cols.Any(c => (c.Ch != null && (c.Ch.Tu <= 0 || c.Ch.TuRec > 0)) || c.Info.NaturezaCarga.Contains("recomendada"));
            if (temRuptura && temRec)
                av.Add("Base de comparação (Ruptura × Recomendada): CB, CBPL, CBE, CBEPL, WB, WLT, Golpe de Metal e Parabolt PB informam cargas de ruptura, enquanto WQA informa apenas carga recomendada e WQI 44 / WQE 500 informam ambas. Nunca compare ruptura diretamente com carga recomendada sem dividir a ruptura pelo coeficiente de segurança aplicável (3 a 15 conforme tipo de carga e risco, pág. 07).");

            // 2. Classes de concreto diferentes (20 x 28 x 30 MPa)
            List<string> fcks = cols.Select(c => c.Ch != null && c.Ch.Fck > 0 ? c.Ch.Fck.ToString("0") + " MPa" : c.Info.FckEnsaio)
                                    .Where(s => !string.IsNullOrEmpty(s) && s != "não aplicável").Distinct().ToList();
            bool tem28 = cols.Any(c => (c.Ch != null && Math.Abs(c.Ch.Fck - 28) < 1e-6) || c.Info.FckEnsaio == "28");
            bool tem30 = cols.Any(c => (c.Ch != null && Math.Abs(c.Ch.Fck - 30) < 1e-6) || c.Info.FckEnsaio == "30");
            if (fcks.Count > 1 || tem28 || tem30)
            {
                double fckLaje = rBase != null && rBase.FckLaje > 0 ? rBase.FckLaje : 20;
                av.Add("Diferença de concreto de ensaio (fck): os ensaios de catálogo do WQI 44 e WQE 500 foram realizados em concreto 28 MPa, os do Indufix Parabolt PB em 30 MPa e os demais mecânicos/WQA em 20 MPa. Na verificação para a laje (" + Num.Fmt(fckLaje, 0) + " MPa), a tração de referência é ajustada por ψc = min(1; √(fck laje / fck ensaio)).");
            }

            // 3. Linha sem corte tabelado (WQI 44, WQE 500, WLT, PU 500)
            List<string> semCorte = cols.Where(c => c.Info.CorteTabelado == "não").Select(c => c.Info.Linha).Distinct().ToList();
            if (semCorte.Count > 0)
                av.Add("Ausência de cisalhamento tabelado (" + string.Join(", ", semCorte.ToArray()) + "): o fabricante não informa carga de cisalhamento no concreto na tabela do catálogo. Nos químicos de injeção (WQI 44 e WQE 500), o corte é verificado apenas no aço da barra roscada (von Mises).");

            // 4. WQA verificada com n = 1 sobre carga recomendada + reducoes obrigatorias da pag. 05
            if (cols.Any(c => c.Info.Linha == "Walsywa WQA"))
                av.Add("Atenção ao WQA (ampola): o catálogo traz apenas carga recomendada (em kN) sem informar o fator de segurança embutido, sendo verificado com n,ref = 1 (o que favorece o WQA frente aos mecânicos verificados com n = 15 sobre ruptura). Além disso, o catálogo exige reduzir as cargas em 30% na distância mínima entre chumbadores (4 a 13 cm), reduzir tração em 40% e corte em 50% na distância mínima à borda (4 a 13 cm), -30% entre 100 e 120 °C e -20% na tração em concreto 15 MPa (pág. 05).");

            // 5. Linhas so informativas (WLT, Golpe de Metal, PU 500)
            List<string> infs = cols.Where(c => c.Info.Informativa).Select(c => c.Info.Linha).Distinct().ToList();
            if (infs.Count > 0)
                av.Add("Linha(s) somente informativa(s) no Estructto (" + string.Join(", ", infs.ToArray()) + "): não fazem parte do catálogo de cálculo estrutural da placa de base (WLT não possui corte tabelado, Golpe de Metal é restrito a aplicações leves em 3/16\" e 1/4\", e PU 500 é espuma não estrutural de vedação sem carga mecânica).");

            // 6. Indufix Parabolt sem descricao de catalogo no guia
            if (cols.Any(c => c.Info.SemCatalogo))
                av.Add("Indufix Parabolt PB: sem descrição de catálogo no guia (falta o PDF do catálogo Indufix na pasta REFERÊNCIA). São exibidos apenas os atributos básicos e a verificação numérica da tabela.");

            return av;
        }
    }
}
