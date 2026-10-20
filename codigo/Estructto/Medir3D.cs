// Ferramenta Medir da Vista3D: mistura do MEASUREGEOM do AutoCAD com o Measure do Inventor (docs/v3/SNAP-MEDIR.md,
// spec em docs/v3/PESQUISA-PLANT.md §4). Aqui: as contas (Medidas), o estado da medição (Medidor3D) e o painel lateral
// (PainelMedir). Quem liga na vista, desenha as cotas e repassa cliques/teclas: Vista3D.Snap.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;

namespace NovoFtool
{
    public enum ModoMedir { Auto, Distancia, Angulo, Area, Raio, Cadeia }

    // Barra clicada no Medir (o que a vista sabe dela).
    public sealed class EntMedida { public Point3D A, B; public string Nome = "", Secao = ""; public double? AreaSecao, Raio; public object Dono; }
    // Uma seleção: sempre tem o ponto clicado (com snap); Barra != null quando o clique foi numa barra sem ponto de snap.
    public sealed class ItemMedida { public Point3D P; public EntMedida Barra; }

    public sealed class CotaMedida { public Point3D A, B; public string Texto; public int Cor; public bool Tracejada, Fina; }   // Cor: 0 medida, 1 sel. 2, 2/3/4 eixos X/Y/Z
    public sealed class ArcoMedida { public Point3D V; public Vector3D D1, D2; public string Texto; }

    public sealed class ResultadoMedida
    {
        public string Tipo = "", Titulo = "", ValorTxt = "", Unidade = "";   // Tipo: grupo do acumulado (Comprimento, Ângulo, Área)
        public double Valor; public bool Completo, Acumulavel = true;
        public readonly List<(string Rot, string Val)> Linhas = new List<(string, string)>();
        public readonly List<CotaMedida> Cotas = new List<CotaMedida>();
        public readonly List<ArcoMedida> Arcos = new List<ArcoMedida>();
        public readonly List<(Point3D A, Point3D B, int Cor)> Destaques = new List<(Point3D, Point3D, int)>();
        public List<Point3D> Poligono;
    }

    public static class Medidas
    {
        public static string M(double v) => Num.Fmt(v, 3) + " m";
        public static string G(double v) => Num.Fmt(v, 2) + "°";
        public static double Graus(Vector3D a, Vector3D b)
        {
            double la = a.Length, lb = b.Length; if (la < 1e-12 || lb < 1e-12) return 0;
            return Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.DotProduct(a, b) / (la * lb)))) * 180 / Math.PI;
        }
        // área (vetorial, vale para polígono plano em qualquer orientação) e perímetro fechado
        public static double Area(IList<Point3D> p, out double perim)
        {
            perim = 0; var s = new Vector3D();
            for (int i = 0; i < p.Count; i++)
            {
                Point3D a = p[i], b = p[(i + 1) % p.Count];
                s += Vector3D.CrossProduct((Vector3D)a, (Vector3D)b); perim += (b - a).Length;
            }
            return p.Count < 3 ? 0 : s.Length / 2;
        }
        // distância do ponto ao segmento: pé e parâmetro (0..1)
        public static double PontoSeg(Point3D p, Point3D a, Point3D b, out Point3D pe, out double t)
        {
            Vector3D e = b - a; double l2 = e.LengthSquared;
            t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, Vector3D.DotProduct(p - a, e) / l2));
            pe = a + e * t; return (p - pe).Length;
        }
    }

    public sealed class Medidor3D
    {
        public ModoMedir Modo = ModoMedir.Auto;
        public readonly List<ItemMedida> Itens = new List<ItemMedida>();
        readonly List<int> sinais = new List<int>();   // Cadeia: +1/−1 de cada trecho (sinais[i] = trecho que chega ao item i)
        public int Sinal = 1;                           // Cadeia: sinal do próximo trecho (+ / −)
        public bool Fechada;                            // área/cadeia fechada com Enter
        public ResultadoMedida Res;
        public readonly List<string> Historico = new List<string>();                                       // mais novo primeiro
        public readonly List<(string Tipo, double Valor, string Unid)> Acumulado = new List<(string, double, string)>();
        public event Action Mudou;
        bool completoAntes;

        public static string NomeModo(ModoMedir m) => m switch
        {
            ModoMedir.Distancia => "Distância", ModoMedir.Angulo => "Ângulo", ModoMedir.Area => "Área", ModoMedir.Raio => "Raio", ModoMedir.Cadeia => "Cadeia", _ => "Automático",
        };

        public Point3D? Base => Itens.Count == 0 || Fechada || Modo == ModoMedir.Raio || Itens[^1].Barra != null && Modo == ModoMedir.Auto ? (Point3D?)null : Itens[^1].P;

        public string Prompt
        {
            get
            {
                int n = Itens.Count;
                switch (Modo)
                {
                    case ModoMedir.Distancia: return n == 0 ? "clique o 1º ponto" : n == 1 ? "clique o 2º ponto" : "clique para nova medida";
                    case ModoMedir.Angulo: return n == 0 ? "clique o vértice (ou a 1ª barra)" : Itens[0].Barra != null ? (n == 1 ? "clique a 2ª barra" : "clique para novo ângulo") : n == 1 ? "clique o 1º lado" : n == 2 ? "clique o 2º lado" : "clique para novo ângulo";
                    case ModoMedir.Area: return Fechada ? "clique para nova área" : n < 3 ? "clique os vértices (ou uma barra = área da seção)" : "próximo vértice · Enter fecha";
                    case ModoMedir.Raio: return "clique um tubo ou barra redonda";
                    case ModoMedir.Cadeia: return Fechada ? "clique para nova cadeia" : (n == 0 ? "clique o 1º ponto" : "próximo ponto · + soma / − subtrai · Enter fecha") + (Sinal < 0 ? " · subtraindo" : "");
                }
                return n == 0 ? "clique um ponto ou uma barra" : "clique outro ponto ou barra (Esc limpa)";
            }
        }

        public void TrocarModo(ModoMedir m) { Modo = m; Limpar(); Avisar(); }

        // Esc: limpa as seleções (true se havia o que limpar)
        public bool Limpar()
        {
            bool havia = Itens.Count > 0 || Res != null;
            Itens.Clear(); sinais.Clear(); Sinal = 1; Fechada = false; Res = null; completoAntes = false;
            if (havia) Avisar();
            return havia;
        }

        void Avisar() => Mudou?.Invoke();

        public void Adicionar(ItemMedida it)
        {
            int n = Itens.Count;
            bool reinicia = Fechada || Modo switch
            {
                ModoMedir.Distancia => n >= 2,
                ModoMedir.Angulo => n > 0 && (Itens[0].Barra != null ? n >= 2 : n >= 3),
                ModoMedir.Raio => n >= 1,
                ModoMedir.Area => n == 1 && Itens[0].Barra != null,
                ModoMedir.Cadeia => false,
                _ => n >= 3 || n == 2 && (Itens[0].Barra != null || Itens[1].Barra != null || it.Barra != null),
            };
            if (reinicia) { Itens.Clear(); sinais.Clear(); Sinal = 1; Fechada = false; completoAntes = false; }
            if (Modo == ModoMedir.Distancia || Modo == ModoMedir.Cadeia || Modo == ModoMedir.Area && Itens.Count > 0) it = new ItemMedida { P = it.P };   // só pontos
            if (Modo == ModoMedir.Angulo && Itens.Count > 0 && (Itens[0].Barra != null) != (it.Barra != null))
                it = Itens[0].Barra != null ? it : new ItemMedida { P = it.P };   // vértice + 2 pontos, ou 2 barras
            if (Modo == ModoMedir.Angulo && Itens.Count > 0 && Itens[0].Barra != null && it.Barra == null) return;   // 2ª seleção do ângulo entre barras tem de ser barra
            Itens.Add(it); sinais.Add(Sinal);
            Recalcular();
        }

        public void Desfazer()   // Backspace: tira o último ponto
        {
            if (Itens.Count == 0) return;
            Itens.RemoveAt(Itens.Count - 1); sinais.RemoveAt(sinais.Count - 1); Fechada = false; completoAntes = false;
            Recalcular();
        }

        public void Confirmar()   // Enter: fecha área/cadeia
        {
            if ((Modo == ModoMedir.Area && Itens.Count >= 3 || Modo == ModoMedir.Cadeia && Itens.Count >= 2) && !Fechada) { Fechada = true; Recalcular(); }
        }

        // + / −: Cadeia troca o sinal dos próximos trechos; nos outros modos soma/subtrai o valor ao acumulado (Inventor)
        public void Somar(int s)
        {
            if (Modo == ModoMedir.Cadeia && !Fechada) { Sinal = s; Avisar(); return; }
            if (Res == null || !Res.Acumulavel || !Res.Completo) return;
            Acumulado.Add((Res.Tipo, s * Res.Valor, Res.Unidade));
            Avisar();
        }
        public void LimparAcumulado(string tipo) { Acumulado.RemoveAll(a => tipo == null || a.Tipo == tipo); Avisar(); }

        public string TextoTudo()
        {
            if (Res == null) return "";
            var sb = new StringBuilder(Res.Titulo + "\t" + Res.ValorTxt + "\r\n");
            foreach (var (r, v) in Res.Linhas) sb.Append(r + "\t" + v + "\r\n");
            return sb.ToString();
        }

        void Recalcular()
        {
            Res = Calcular();
            if (Res != null && Res.Completo && !completoAntes) { Historico.Insert(0, Res.Titulo + "  " + Res.ValorTxt); if (Historico.Count > 20) Historico.RemoveAt(20); }
            completoAntes = Res?.Completo == true;
            Avisar();
        }

        ResultadoMedida Calcular()
        {
            var it = Itens; int n = it.Count;
            if (n == 0) return null;
            switch (Modo)
            {
                case ModoMedir.Distancia: return n == 1 ? Coordenada(it[0].P) : Distancia(it[0].P, it[1].P);
                case ModoMedir.Raio: return Raio(it[0]);
                case ModoMedir.Cadeia: return Cadeia();
                case ModoMedir.Area:
                    if (n == 1 && it[0].Barra != null) return AreaSecao(it[0].Barra);
                    return AreaPoligono();
                case ModoMedir.Angulo:
                    if (it[0].Barra != null) return n == 1 ? Barra(it[0].Barra) : AnguloBarras(it[0].Barra, it[1].Barra);
                    return n == 1 ? Coordenada(it[0].P) : n == 2 ? Distancia(it[0].P, it[1].P, false) : Angulo(it[1].P, it[0].P, it[2].P);
            }
            // Automático (Inventor): depende do que foi clicado
            if (n == 1) return it[0].Barra != null ? Barra(it[0].Barra) : Coordenada(it[0].P);
            if (n == 2)
            {
                EntMedida a = it[0].Barra, b = it[1].Barra;
                if (a != null && b != null) return EntreBarras(a, b);
                if (a != null || b != null) return PontoBarra(a != null ? it[1].P : it[0].P, a ?? b);
                return Distancia(it[0].P, it[1].P);
            }
            return Angulo(it[0].P, it[1].P, it[2].P);   // 3 pontos: ângulo no do meio
        }

        static ResultadoMedida Coordenada(Point3D p)
        {
            var r = new ResultadoMedida { Tipo = "Coordenada", Titulo = "Ponto", ValorTxt = "(" + Num.Fmt(p.X, 3) + "; " + Num.Fmt(p.Y, 3) + "; " + Num.Fmt(p.Z, 3) + ")", Acumulavel = false };
            r.Linhas.Add(("X", Medidas.M(p.X))); r.Linhas.Add(("Y", Medidas.M(p.Y))); r.Linhas.Add(("Z", Medidas.M(p.Z)));
            return r;
        }

        public static ResultadoMedida Distancia(Point3D a, Point3D b, bool completo = true)
        {
            Vector3D d = b - a; double l = d.Length, h = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            var r = new ResultadoMedida { Tipo = "Comprimento", Titulo = "Distância", Valor = l, ValorTxt = Medidas.M(l), Unidade = "m", Completo = completo };
            r.Linhas.Add(("ΔX", Medidas.M(d.X))); r.Linhas.Add(("ΔY", Medidas.M(d.Y))); r.Linhas.Add(("ΔZ", Medidas.M(d.Z)));
            r.Linhas.Add(("Ângulo no plano XY", Medidas.G(h < 1e-12 ? 0 : (Math.Atan2(d.Y, d.X) * 180 / Math.PI + 360) % 360)));
            r.Linhas.Add(("Inclinação (do XY)", Medidas.G(l < 1e-12 ? 0 : Math.Atan2(d.Z, h) * 180 / Math.PI)));
            r.Cotas.Add(new CotaMedida { A = a, B = b, Texto = Medidas.M(l) });
            // escada ΔX, ΔY, ΔZ nas cores dos eixos, a partir do ponto 1
            Point3D p1 = a + new Vector3D(d.X, 0, 0), p2 = p1 + new Vector3D(0, d.Y, 0);
            if (Math.Abs(d.X) > 1e-9 && l - Math.Abs(d.X) > 1e-9) r.Cotas.Add(new CotaMedida { A = a, B = p1, Cor = 2, Tracejada = true, Fina = true });
            if (Math.Abs(d.Y) > 1e-9 && l - Math.Abs(d.Y) > 1e-9) r.Cotas.Add(new CotaMedida { A = p1, B = p2, Cor = 3, Tracejada = true, Fina = true });
            if (Math.Abs(d.Z) > 1e-9 && l - Math.Abs(d.Z) > 1e-9) r.Cotas.Add(new CotaMedida { A = p2, B = b, Cor = 4, Tracejada = true, Fina = true });
            return r;
        }

        static ResultadoMedida Barra(EntMedida e)
        {
            double l = (e.B - e.A).Length;
            var r = new ResultadoMedida { Tipo = "Comprimento", Titulo = "Comprimento da barra" + (e.Nome != "" ? " " + e.Nome : ""), Valor = l, ValorTxt = Medidas.M(l), Unidade = "m", Completo = true };
            if (e.Secao != "") r.Linhas.Add(("Seção", e.Secao));
            if (e.AreaSecao != null) r.Linhas.Add(("Área da seção", Num.Fmt(e.AreaSecao.Value * 1e4, 2) + " cm²"));
            if (e.Raio != null) { r.Linhas.Add(("Raio", Medidas.M(e.Raio.Value))); r.Linhas.Add(("Diâmetro", Medidas.M(2 * e.Raio.Value))); }
            if (e.AreaSecao != null) r.Linhas.Add(("Peso (aço 7850 kg/m³)", Num.Fmt(e.AreaSecao.Value * l * 7850, 1) + " kg"));
            Vector3D d = e.B - e.A;
            r.Linhas.Add(("ΔX / ΔY / ΔZ", Num.Fmt(d.X, 3) + " / " + Num.Fmt(d.Y, 3) + " / " + Num.Fmt(d.Z, 3)));
            r.Cotas.Add(new CotaMedida { A = e.A, B = e.B, Texto = Medidas.M(l) });
            r.Destaques.Add((e.A, e.B, 0));
            return r;
        }

        static ResultadoMedida AreaSecao(EntMedida e)
        {
            var r = new ResultadoMedida { Tipo = "Área", Titulo = "Área da seção " + e.Secao, Unidade = "m²", Completo = e.AreaSecao != null };
            r.Destaques.Add((e.A, e.B, 0));
            if (e.AreaSecao == null) { r.ValorTxt = "sem dados"; r.Acumulavel = false; return r; }
            r.Valor = e.AreaSecao.Value; r.ValorTxt = Num.Fmt(r.Valor * 1e4, 2) + " cm²";
            r.Linhas.Add(("Em m²", Num.Fmt(r.Valor, 6) + " m²"));
            return r;
        }

        static ResultadoMedida Raio(ItemMedida it)
        {
            var e = it.Barra;
            var r = new ResultadoMedida { Tipo = "Comprimento", Titulo = "Raio", Unidade = "m" };
            if (e == null || e.Raio == null) { r.ValorTxt = e == null ? "clique uma barra" : "seção não é redonda"; r.Acumulavel = false; if (e != null) r.Destaques.Add((e.A, e.B, 0)); return r; }
            r.Valor = e.Raio.Value; r.ValorTxt = Medidas.M(r.Valor); r.Completo = true; r.Titulo = "Raio " + e.Secao;
            r.Linhas.Add(("Diâmetro", Medidas.M(2 * r.Valor)));
            r.Destaques.Add((e.A, e.B, 0));
            return r;
        }

        static ResultadoMedida PontoBarra(Point3D p, EntMedida e)
        {
            double d = Medidas.PontoSeg(p, e.A, e.B, out Point3D pe, out double t);
            double l = (e.B - e.A).Length;
            var r = new ResultadoMedida { Tipo = "Comprimento", Titulo = "Distância ponto–barra", Valor = d, ValorTxt = Medidas.M(d), Unidade = "m", Completo = true };
            r.Linhas.Add(("Ao longo da barra", Medidas.M(t * l) + "  (" + Num.Fmt(100 * t, 1) + "%)"));
            r.Cotas.Add(new CotaMedida { A = p, B = pe, Texto = Medidas.M(d) });
            r.Destaques.Add((e.A, e.B, 1));
            return r;
        }

        public static ResultadoMedida EntreBarras(EntMedida a, EntMedida b)
        {
            Snap3D.Segmentos(a.A, a.B, b.A, b.B, out double s, out double t, out Point3D c1, out Point3D c2);
            double d = (c2 - c1).Length;
            Vector3D da = a.B - a.A, db = b.B - b.A;
            double ang = Medidas.Graus(da, db); if (ang > 90) ang = 180 - ang;
            Vector3D n = Vector3D.CrossProduct(da, db);
            bool paralelas = n.Length < 1e-9 * da.Length * db.Length;
            bool coplanares = paralelas || Math.Abs(Vector3D.DotProduct(b.A - a.A, n)) < 1e-6 * n.Length;
            var r = new ResultadoMedida { Tipo = "Comprimento", Titulo = "Distância mínima", Valor = d, ValorTxt = Medidas.M(d), Unidade = "m", Completo = true };
            r.Linhas.Add(("Ângulo entre eixos", Medidas.G(ang)));
            r.Linhas.Add(("Centro a centro", Medidas.M(((b.A + (b.B - b.A) / 2) - (a.A + (a.B - a.A) / 2)).Length)));
            r.Linhas.Add(("Posição", paralelas ? "paralelas" : coplanares ? "coplanares (concorrentes)" : "reversas"));
            r.Linhas.Add(("Ponto em 1", "(" + Num.Fmt(c1.X, 3) + "; " + Num.Fmt(c1.Y, 3) + "; " + Num.Fmt(c1.Z, 3) + ")"));
            r.Linhas.Add(("Ponto em 2", "(" + Num.Fmt(c2.X, 3) + "; " + Num.Fmt(c2.Y, 3) + "; " + Num.Fmt(c2.Z, 3) + ")"));
            r.Destaques.Add((a.A, a.B, 0)); r.Destaques.Add((b.A, b.B, 1));
            if (d > 1e-9) r.Cotas.Add(new CotaMedida { A = c1, B = c2, Texto = Medidas.M(d) });
            if (!paralelas)
            {
                // arco do ângulo agudo no ponto mais próximo da barra 1, com as direções para dentro das barras
                Vector3D u1 = s > 0.5 ? -da : da, u2 = t > 0.5 ? -db : db;
                if (Vector3D.DotProduct(u1, u2) < 0) u2 = -u2;
                r.Arcos.Add(new ArcoMedida { V = c1, D1 = u1, D2 = u2, Texto = Medidas.G(ang) });
            }
            return r;
        }

        static ResultadoMedida AnguloBarras(EntMedida a, EntMedida b)
        {
            var r = EntreBarras(a, b);
            double ang = Medidas.Graus(a.B - a.A, b.B - b.A); if (ang > 90) ang = 180 - ang;
            var q = new ResultadoMedida { Tipo = "Ângulo", Titulo = "Ângulo entre barras", Valor = ang, ValorTxt = Medidas.G(ang), Unidade = "°", Completo = true };
            q.Linhas.Add(("Suplemento", Medidas.G(180 - ang)));
            q.Linhas.Add(("Distância mínima", r.ValorTxt));
            q.Destaques.AddRange(r.Destaques); q.Arcos.AddRange(r.Arcos);
            return q;
        }

        public static ResultadoMedida Angulo(Point3D p1, Point3D v, Point3D p2)
        {
            double ang = Medidas.Graus(p1 - v, p2 - v);
            var r = new ResultadoMedida { Tipo = "Ângulo", Titulo = "Ângulo", Valor = ang, ValorTxt = Medidas.G(ang), Unidade = "°", Completo = true };
            r.Linhas.Add(("Replemento", Medidas.G(360 - ang)));
            r.Linhas.Add(("Lado 1", Medidas.M((p1 - v).Length))); r.Linhas.Add(("Lado 2", Medidas.M((p2 - v).Length)));
            r.Cotas.Add(new CotaMedida { A = v, B = p1, Fina = true }); r.Cotas.Add(new CotaMedida { A = v, B = p2, Fina = true, Cor = 1 });
            r.Arcos.Add(new ArcoMedida { V = v, D1 = p1 - v, D2 = p2 - v, Texto = Medidas.G(ang) });
            return r;
        }

        ResultadoMedida AreaPoligono()
        {
            var pts = Itens.Select(i => i.P).ToList();
            double a = Medidas.Area(pts, out double per);
            var r = new ResultadoMedida { Tipo = "Área", Titulo = Fechada ? "Área" : "Área (aberta)", Valor = a, ValorTxt = Num.Fmt(a, 3) + " m²", Unidade = "m²", Completo = Fechada, Poligono = pts };
            r.Linhas.Add(("Perímetro", Medidas.M(per))); r.Linhas.Add(("Vértices", pts.Count.ToString()));
            for (int i = 0; i + 1 < pts.Count; i++) r.Cotas.Add(new CotaMedida { A = pts[i], B = pts[i + 1], Fina = true, Cor = 5 });
            if (Fechada && pts.Count >= 3) r.Cotas.Add(new CotaMedida { A = pts[^1], B = pts[0], Fina = true, Cor = 5 });
            return r;
        }

        ResultadoMedida Cadeia()
        {
            double tot = 0, soma = 0, sub = 0;
            var r = new ResultadoMedida { Tipo = "Comprimento", Unidade = "m", Completo = Fechada };
            for (int i = 1; i < Itens.Count; i++)
            {
                double l = (Itens[i].P - Itens[i - 1].P).Length; int s = sinais[i];
                tot += s * l; if (s > 0) soma += l; else sub += l;
                r.Cotas.Add(new CotaMedida { A = Itens[i - 1].P, B = Itens[i].P, Texto = (s > 0 ? "+" : "−") + Num.Fmt(l, 3), Cor = s > 0 ? 0 : 1, Tracejada = s < 0 });
                r.Linhas.Add(("Trecho " + i, (s > 0 ? "+ " : "− ") + Medidas.M(l)));
            }
            r.Valor = tot; r.ValorTxt = Medidas.M(tot); r.Titulo = Fechada ? "Cadeia" : "Cadeia (em andamento)";
            r.Linhas.Insert(0, ("Somado / subtraído", Num.Fmt(soma, 3) + " / " + Num.Fmt(sub, 3)));
            return r;
        }
    }

    // Painel lateral do Medir (dock à direita da Vista3D, 280 px; Inventor: valor principal, linhas, acumulado, histórico).
    public sealed class PainelMedir : Border
    {
        readonly Medidor3D md;
        readonly StackPanel pilha = new StackPanel();
        public static readonly SolidColorBrush[] Cores = { TemaPlant.BMedida, TemaPlant.BMedida2 };

        public PainelMedir(Medidor3D m)
        {
            md = m;
            Width = 280; HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(0, 52, 12, 48); Padding = new Thickness(12, 10, 12, 12);
            Background = TemaPlant.BSup1; BorderBrush = TemaPlant.BBordaSutil; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
            Child = new ScrollViewer { Content = pilha, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };   // margem de baixo: a etiqueta da ferramenta fica visível
            md.Mudou += Montar;
            Montar();
        }

        static TextBlock T(string s, Brush b, double tam, FontWeight? w = null) => new TextBlock { Text = s, Foreground = b, FontSize = tam, FontWeight = w ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };

        static void Copiar(string s) { try { if (!string.IsNullOrEmpty(s)) Clipboard.SetText(s); } catch { } }

        Border Botao(string txt, bool ativo, Action acao, string dica = null)
        {
            var t = T(txt, ativo ? TemaPlant.BTexto1 : TemaPlant.BTexto2, 11.5);
            var b = new Border { Child = t, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, ToolTip = dica,
                                 Background = ativo ? TemaPlant.BAcentoSutil : TemaPlant.BSup2, BorderBrush = ativo ? TemaPlant.BBordaFoco : TemaPlant.BBordaSutil, BorderThickness = new Thickness(1) };
            b.MouseEnter += (s, e) => { if (!ativo) b.Background = TemaPlant.BSup3; };
            b.MouseLeave += (s, e) => { if (!ativo) b.Background = TemaPlant.BSup2; };
            b.MouseLeftButtonDown += (s, e) => { acao(); e.Handled = true; };
            return b;
        }

        UIElement Secao(string titulo) => new TextBlock { Text = titulo.ToUpperInvariant(), Foreground = TemaPlant.BTexto3, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };

        UIElement Linha(string rot, string val, Brush cor = null)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Clique para copiar " + val };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var r = T(rot, TemaPlant.BTexto2, 11.5); var v = T(val, cor ?? TemaPlant.BTexto1, 11.5);
            Grid.SetColumn(v, 1); g.Children.Add(r); g.Children.Add(v);
            g.MouseEnter += (s, e) => g.Background = TemaPlant.BSup2; g.MouseLeave += (s, e) => g.Background = Brushes.Transparent;
            g.MouseLeftButtonDown += (s, e) => { Copiar(val); e.Handled = true; };
            return g;
        }

        void Montar()
        {
            pilha.Children.Clear();
            var cab = new DockPanel();
            cab.Children.Add(T("MEDIR", TemaPlant.BTexto1, 12, FontWeights.SemiBold));
            var modo = T(Medidor3D.NomeModo(md.Modo), TemaPlant.BTexto3, 11); modo.HorizontalAlignment = HorizontalAlignment.Right; cab.Children.Add(modo);
            pilha.Children.Add(cab);
            var modos = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (ModoMedir m in Enum.GetValues(typeof(ModoMedir)))
            {
                ModoMedir mm = m;
                modos.Children.Add(Botao(m == ModoMedir.Auto ? "Auto" : Medidor3D.NomeModo(m), md.Modo == m, () => md.TrocarModo(mm)));
            }
            pilha.Children.Add(modos);

            // seleções (chips laranja e azul, como no Inventor)
            var sels = md.Itens.Where(i => i.Barra != null).Take(2).ToList();
            if (sels.Count > 0)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                for (int i = 0; i < sels.Count; i++)
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Cores[i], Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(T((sels[i].Barra.Nome != "" ? "Barra " + sels[i].Barra.Nome : "Barra") + (sels[i].Barra.Secao != "" ? " · " + sels[i].Barra.Secao : ""), TemaPlant.BTexto1, 11));
                    wp.Children.Add(new Border { Child = sp, Padding = new Thickness(7, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(9), Background = TemaPlant.BSup2, BorderBrush = Cores[i], BorderThickness = new Thickness(1) });
                }
                pilha.Children.Add(wp);
            }

            ResultadoMedida r = md.Res;
            if (r == null)
                pilha.Children.Add(new TextBlock { Text = "Clique um ponto (com snap) ou uma barra. Ponto + ponto = distância; barra + barra = distância mínima e ângulo; 3 pontos = ângulo.",
                                                   Foreground = TemaPlant.BTexto2, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            else
            {
                pilha.Children.Add(new TextBlock { Text = r.Titulo, Foreground = TemaPlant.BTexto2, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 0) });
                pilha.Children.Add(new TextBlock { Text = r.ValorTxt, Foreground = TemaPlant.BTexto1, FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = TemaPlant.FonteMono, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
                var acoes = new WrapPanel();
                acoes.Children.Add(Botao("Copiar", false, () => Copiar(r.ValorTxt), "Copia o valor principal (Ctrl+C)"));
                acoes.Children.Add(Botao("Copiar tudo", false, () => Copiar(md.TextoTudo()), "Copia a tabela (texto com tabulação)"));
                if (r.Acumulavel && r.Completo && md.Modo != ModoMedir.Cadeia)
                {
                    acoes.Children.Add(Botao("+", false, () => md.Somar(1), "Soma ao acumulado (+)"));
                    acoes.Children.Add(Botao("−", false, () => md.Somar(-1), "Subtrai do acumulado (−)"));
                }
                if (md.Modo == ModoMedir.Cadeia && !md.Fechada)
                {
                    acoes.Children.Add(Botao("+ somar", md.Sinal > 0, () => md.Somar(1), "Próximos trechos somam (+)"));
                    acoes.Children.Add(Botao("− subtrair", md.Sinal < 0, () => md.Somar(-1), "Próximos trechos subtraem (−)"));
                }
                pilha.Children.Add(acoes);
                foreach (var (rot, val) in r.Linhas)
                {
                    Brush c = rot == "ΔX" ? TemaPlant.BErro : rot == "ΔY" ? TemaPlant.BSucesso : rot == "ΔZ" ? TemaPlant.BRealce : null;
                    pilha.Children.Add(Linha(rot, val, c));
                }
            }

            if (md.Acumulado.Count > 0)
            {
                pilha.Children.Add(Secao("Acumulado"));
                foreach (var gr in md.Acumulado.GroupBy(a => a.Tipo))
                {
                    string un = gr.First().Unid, tipo = gr.Key;
                    double tot = gr.Sum(a => a.Valor);
                    var dp = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var lix = Botao("×", false, () => md.LimparAcumulado(tipo), "Apaga o grupo"); DockPanel.SetDock(lix, Dock.Right); lix.Margin = new Thickness(6, 0, 0, 0);
                    dp.Children.Add(lix);
                    dp.Children.Add(Linha(tipo + " (" + gr.Count() + ")", (un == "m²" ? Num.Fmt(tot, 3) : un == "°" ? Num.Fmt(tot, 2) : Num.Fmt(tot, 3)) + " " + un));
                    pilha.Children.Add(dp);
                }
            }
            if (md.Historico.Count > 0)
            {
                pilha.Children.Add(Secao("Histórico"));
                foreach (string h in md.Historico.Take(6))
                {
                    int k = h.LastIndexOf("  ", StringComparison.Ordinal);
                    pilha.Children.Add(Linha(k > 0 ? h.Substring(0, k) : h, k > 0 ? h.Substring(k + 2) : ""));
                }
            }
            pilha.Children.Add(new TextBlock { Text = "Esc limpa · Enter fecha área/cadeia · Backspace desfaz · + / − acumula · Ctrl+C copia · Shift+botão direito: snap",
                                               Foreground = TemaPlant.BTexto3, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        }
    }

    // Asserções das medidas (rodam dentro de --testes-snap).
    public static class TestesMedir
    {
        public static void Rodar(Action<string, bool> Ok)
        {
            bool P(double a, double b) => Math.Abs(a - b) < 1e-6;
            var md = new Medidor3D();
            md.Adicionar(new ItemMedida { P = new Point3D(0, 0, 0) });
            md.Adicionar(new ItemMedida { P = new Point3D(3, 4, 12) });
            Ok("medir: 2 pontos = 13,000 m com ΔX/ΔY/ΔZ", md.Res.Tipo == "Comprimento" && P(md.Res.Valor, 13) && md.Res.Linhas[0].Val == "3,000 m" && md.Res.Linhas[2].Val == "12,000 m");
            Ok("medir: resultado completo entra no histórico", md.Historico.Count == 1 && md.Historico[0].Contains("13,000"));
            md.Adicionar(new ItemMedida { P = new Point3D(3, 4, 0) });
            Ok("medir: 3º ponto = ângulo no do meio", md.Res.Tipo == "Ângulo" && P(md.Res.Valor, Math.Acos(12.0 / 13) * 180 / Math.PI));
            md.Somar(1); md.Limpar();
            md.Adicionar(new ItemMedida { P = new Point3D(0, 0, 0) }); md.Adicionar(new ItemMedida { P = new Point3D(0, 0, 2) });
            md.Somar(1);
            Ok("medir: acumulado separa ângulo e comprimento", md.Acumulado.Count == 2 && md.Acumulado[1].Tipo == "Comprimento" && P(md.Acumulado[1].Valor, 2));

            // barra + barra: reversas a 3 m, 90°
            var b1 = new EntMedida { A = new Point3D(0, 0, 0), B = new Point3D(4, 0, 0), Nome = "B1" };
            var b2 = new EntMedida { A = new Point3D(2, -2, 3), B = new Point3D(2, 2, 3), Nome = "B2" };
            md.Limpar();
            md.Adicionar(new ItemMedida { P = b1.A, Barra = b1 });
            Ok("medir: 1 barra = comprimento 4,000 m", md.Res.Titulo.StartsWith("Comprimento") && P(md.Res.Valor, 4));
            md.Adicionar(new ItemMedida { P = b2.A, Barra = b2 });
            Ok("medir: 2 barras reversas = mínima 3,000 m, 90°", md.Res.Titulo == "Distância mínima" && P(md.Res.Valor, 3) && md.Res.Linhas[0].Val == "90,00°" && md.Res.Linhas[2].Val == "reversas");
            var b3 = new EntMedida { A = new Point3D(0, 1, 0), B = new Point3D(4, 1 + 4 * Math.Tan(Math.PI / 6), 0) };
            var r3 = Medidor3D.EntreBarras(b1, b3);
            Ok("medir: barras concorrentes a 30° (fora do trecho) = mínima 1,000 m", P(r3.Valor, 1) && r3.Linhas[0].Val == "30,00°" && r3.Linhas[2].Val.StartsWith("coplanares"));
            md.Adicionar(new ItemMedida { P = new Point3D(2, 5, 0) });
            Ok("medir: 3ª seleção após 2 barras reinicia (ponto)", md.Itens.Count == 1 && md.Res.Tipo == "Coordenada");
            md.Limpar();
            md.Adicionar(new ItemMedida { P = b1.A, Barra = b1 }); md.Adicionar(new ItemMedida { P = new Point3D(1, 2, 0) });
            Ok("medir: barra + ponto = distância perpendicular 2,000 m a 25%", P(md.Res.Valor, 2) && md.Res.Linhas[0].Val.Contains("25,0%"));

            // cadeia com + e −
            md.TrocarModo(ModoMedir.Cadeia);
            md.Adicionar(new ItemMedida { P = new Point3D(0, 0, 0) }); md.Adicionar(new ItemMedida { P = new Point3D(5, 0, 0) });
            md.Somar(-1); md.Adicionar(new ItemMedida { P = new Point3D(3, 0, 0) });
            md.Somar(1); md.Adicionar(new ItemMedida { P = new Point3D(3, 4, 0) });
            Ok("cadeia: 5 − 2 + 4 = 7,000 m", P(md.Res.Valor, 7) && md.Res.Cotas.Count == 3 && md.Res.Cotas[1].Tracejada);
            md.Desfazer();
            Ok("cadeia: Backspace tira o último trecho (3,000 m)", P(md.Res.Valor, 3));
            md.Confirmar();
            Ok("cadeia: Enter fecha e vai ao histórico", md.Fechada && md.Historico[0].StartsWith("Cadeia"));

            // área de polígono (vertical, 2 × 3) e área da seção
            md.TrocarModo(ModoMedir.Area);
            foreach (var p in new[] { new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3) }) md.Adicionar(new ItemMedida { P = p });
            md.Confirmar();
            Ok("área: retângulo vertical 2 × 3 = 6,000 m², perímetro 10", P(md.Res.Valor, 6) && md.Res.Linhas[0].Val == "10,000 m");
            md.Adicionar(new ItemMedida { P = b1.A, Barra = new EntMedida { A = b1.A, B = b1.B, Secao = "W", AreaSecao = 0.00495 } });
            Ok("área: clique numa barra depois de fechar = área da seção 49,50 cm²", md.Res.ValorTxt == "49,50 cm²");

            // ângulo por vértice e raio
            md.TrocarModo(ModoMedir.Angulo);
            md.Adicionar(new ItemMedida { P = new Point3D(1, 1, 0) }); md.Adicionar(new ItemMedida { P = new Point3D(3, 1, 0) }); md.Adicionar(new ItemMedida { P = new Point3D(1, 3, 0) });
            Ok("ângulo: vértice primeiro = 90°", P(md.Res.Valor, 90));
            md.TrocarModo(ModoMedir.Raio);
            md.Adicionar(new ItemMedida { P = b1.A, Barra = new EntMedida { A = b1.A, B = b1.B, Secao = "TC", Raio = 0.0508 } });
            Ok("raio: tubo Ø 101,6 = 0,051 m (Ø 0,102)", md.Res.ValorTxt == "0,051 m" && md.Res.Linhas[0].Val == "0,102 m");
            md.Adicionar(new ItemMedida { P = b1.A, Barra = b1 });
            Ok("raio: barra não redonda avisa", md.Res.ValorTxt == "seção não é redonda");
        }
    }
}
