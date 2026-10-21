// Desenho da ligação a partir da `geometria` do contrato (docs/v3/LIGACOES-CONTRATO.md, docs/v3/LIGACOES-UI.md):
// GeoLig = peças como prismas (chapa = contorno × espessura; cantoneira = duas abas; perfis = caixas e alma com recortes),
// parafusos e cordões de solda, no referencial local da ligação (mm). DesenhoLigacao projeta numa vista ortogonal
// (Elevação, Planta, Lateral: contorno, hachura da espessura, parafuso com cruz, símbolo de solda, cotas de passo,
// gabarito e bordas) ou isométrica (faces sombreadas, pintor), e anima a troca de geometria (Anim.Valor na propriedade Prog).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Portico;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    // Cada peça desenhada carrega "<secao>.<campo>|<campo alternativo>...": a janela de projeto usa o 1o campo que o padrao tem
    // (docs/v3/LIGACOES-EDITOR.md). Secao em JanelaLigacaoProjeto.Secao; "barras" = passo 1 (perfis).
    public static class TagLig
    {
        public const string Parafuso = "Parafusos.d|d_mm|d_mesa_mm|d_alma_mm|n";
        public const string Solda = "Soldas.b_solda|b_solda_mesa|b_solda_pilar|solda_mesa|b_solda_alma|b_interface|b";
        public const string Cantoneira = "Chapa.cantoneira|cantoneira_superior";
        public const string Viga = "barras.perfil_viga|perfil_1|perfil_barra";
        public const string Suporte = "barras.perfil_suporte|perfil_pilar|apoio";
        public static string Chapa(string nome)
        {
            string n = (nome ?? "").ToLowerInvariant();
            if (n.Contains("enrij")) return "Enrijecedores.t_enrijecedor|t_enr_pilar|t_reforco";
            if (n.Contains("tala") && n.Contains("alma")) return "Chapa.t_tala_alma";
            if (n.Contains("tala")) return "Chapa.t_tala_mesa";
            if (n.Contains("gusset")) return "Chapa.t_gusset|e_gusset|t_chapa";
            if (n.Contains("assento")) return "Chapa.t_assento|t_chapa";
            return "Chapa.t_chapa|tp|t|t_mesa|t_gusset|t_orelha";
        }
    }

    public class GeoLig
    {
        // Camada: 0 perfil ligado, 1 chapa, 2 cantoneira. Grupo de falha: viga, suporte, chapas, cantoneiras.
        public class Prisma { public string Nome = "", Grupo = ""; public Point3D[] Base; public Vector3D N; public double T; public int Camada; public bool Falha; public string Tag = ""; }
        public class Paraf { public Point3D O; public Vector3D E; public double D, L, Pega; public bool Falha; public string Tag = TagLig.Parafuso; }
        public class Cordao { public Point3D[] P; public double Perna; public bool Falha; public string Tag = TagLig.Solda; }

        public readonly List<Prisma> Pecas = new List<Prisma>();
        public readonly List<Paraf> Parafs = new List<Paraf>();
        public readonly List<Cordao> Soldas = new List<Cordao>();
        public bool Parede; public double XParede;          // apoio genérico (tipo sem perfil de apoio): face x = XParede
        public string NomeViga = "", NomeSuporte = "";

        static Point3D P3(JsonNode a) => a is JsonArray v && v.Count >= 3 ? new Point3D(D(v[0]), D(v[1]), D(v[2])) : new Point3D();
        static Vector3D V3(JsonNode a, Vector3D pad) => a is JsonArray v && v.Count >= 3 ? new Vector3D(D(v[0]), D(v[1]), D(v[2])) : pad;
        static bool Ok(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        public static Prisma Caixa(double x0, double x1, double y0, double y1, double z0, double z1, int camada, string grupo, string nome = "")
        {
            double zm = (z0 + z1) / 2;
            return new Prisma
            {
                Base = new[] { new Point3D(x0, y0, zm), new Point3D(x1, y0, zm), new Point3D(x1, y1, zm), new Point3D(x0, y1, zm) },
                N = new Vector3D(0, 0, 1), T = Math.Abs(z1 - z0), Camada = camada, Grupo = grupo, Nome = nome
            };
        }

        // falhas: grupos reprovados (parafusos, soldas, chapas, cantoneiras, viga, suporte)
        public static GeoLig De(JsonNode g, ICollection<string> falhas = null)
        {
            var r = new GeoLig();
            if (g == null) return r;
            bool F(string k) => falhas != null && falhas.Contains(k);
            foreach (var c in Lista(g["chapas"]))
            {
                var o = P3(c["origem"]); var n = V3(c["normal"], new Vector3D(0, 1, 0)); var u = V3(c["u"], new Vector3D(1, 0, 0));
                n.Normalize(); u.Normalize();
                var v = c["v"] is JsonArray ? V3(c["v"], new Vector3D()) : Vector3D.CrossProduct(n, u);
                var pts = Lista(c["contorno"]).OfType<JsonArray>().Where(p => p.Count >= 2).Select(p => o + D(p[0]) * u + D(p[1]) * v).ToArray();
                double t = D(c, "espessura");
                if (pts.Length >= 3 && Ok(t)) r.Pecas.Add(new Prisma { Base = pts, N = n, T = t, Camada = 1, Grupo = "chapas", Nome = S(c, "nome"), Falha = F("chapas"), Tag = TagLig.Chapa(S(c, "nome")) });
            }
            foreach (var c in Lista(g["cantoneiras"]))
            {
                var o = P3(c["origem"]); var e = V3(c["eixo"], new Vector3D(0, 0, 1)); var u = V3(c["u"], new Vector3D(1, 0, 0));
                e.Normalize(); u.Normalize();
                var w = c["w"] is JsonArray ? V3(c["w"], new Vector3D()) : Vector3D.CrossProduct(e, u);
                w.Normalize();
                double L = D(c, "comprimento"), t = D(c, "espessura"), b1 = D(c["abas"]?[0]), b2 = D(c["abas"]?[1]);
                var m = Regex.Match(S(c, "perfil"), @"L\s*(\d+(?:[.,]\d+)?)\s*[xX]\s*(\d+(?:[.,]\d+)?)(?:\s*[xX]\s*(\d+(?:[.,]\d+)?))?");
                double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
                if (m.Success && (!Ok(b1) || !Ok(t)))
                {
                    if (m.Groups[3].Success) { b1 = Num(m.Groups[1].Value); b2 = Num(m.Groups[2].Value); t = Num(m.Groups[3].Value); }
                    else { b1 = b2 = Num(m.Groups[1].Value); t = Num(m.Groups[2].Value); }
                }
                if (!Ok(b2)) b2 = b1;
                if (!Ok(L) || !Ok(b1) || !Ok(t)) continue;
                var o1 = o + w * t / 2; var o2 = o + u * t / 2;
                r.Pecas.Add(new Prisma { Base = new[] { o1, o1 + u * b1, o1 + u * b1 + e * L, o1 + e * L }, N = w, T = t, Camada = 2, Grupo = "cantoneiras", Nome = S(c, "perfil"), Falha = F("cantoneiras"), Tag = TagLig.Cantoneira });
                r.Pecas.Add(new Prisma { Base = new[] { o2, o2 + w * b2, o2 + w * b2 + e * L, o2 + e * L }, N = u, T = t, Camada = 2, Grupo = "cantoneiras", Nome = S(c, "perfil"), Falha = F("cantoneiras"), Tag = TagLig.Cantoneira });
            }
            foreach (var p in Lista(g["parafusos"]).Concat(Lista(g["porcas"])))
            {
                double d = D(p, "d"); if (!Ok(d)) d = D(p, "diametro");
                var e = V3(p["eixo"], new Vector3D(0, -1, 0)); e.Normalize();
                double L = D(p, "comprimento"); if (!Ok(L)) L = 1.2 * d;
                double pega = D(p, "pega"); if (!Ok(pega)) pega = Math.Max(d * 0.5, L - 1.2 * d);
                if (Ok(d) && p["origem"] is JsonArray) r.Parafs.Add(new Paraf { O = P3(p["origem"]), E = e, D = d, L = L, Pega = pega, Falha = F("parafusos"), Tag = TagLig.Parafuso });
            }
            foreach (var s in Lista(g["soldas"]))
            {
                var pts = Lista(s["pontos"]).OfType<JsonArray>().Select(P3).ToArray();
                if (pts.Length >= 2) r.Soldas.Add(new Cordao { P = pts, Perna = Ok(D(s, "perna")) ? D(s, "perna") : 5, Falha = F("soldas"), Tag = TagLig.Solda });
            }
            // perfis ligados (projeto.verificar_ligacao → geometria.membros) posicionados pelo contrato
            double xPonta = double.NaN, cs = 0, hs = 0, ci = 0, hi = 0;
            foreach (var rc in Lista(g["recortes"]))
            {
                var pr = rc["parametros"];
                if (S(rc, "tipo") == "recuo") xPonta = D(pr, "x_ponta");
                else if (S(rc, "tipo") == "cope")
                {
                    if (S(pr, "posicao") == "base") { hi = D(pr, "altura"); ci = D(pr, "comprimento"); }
                    else { hs = D(pr, "altura"); cs = D(pr, "comprimento"); }
                    if (Ok(D(pr, "x_inicio")) && !Ok(xPonta)) xPonta = D(pr, "x_inicio");
                }
            }
            var pecasLig = r.Pecas.ToList();
            double xMinLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Min(q => q.X) : 0;
            double xMaxLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Max(q => q.X) : 200;
            double zMinLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Min(q => q.Z) : -100;
            double zMaxLig = pecasLig.Count > 0 ? pecasLig.SelectMany(q => q.Base).Max(q => q.Z) : 100;
            JsonNode viga = Lista(g["membros"]).FirstOrDefault(m => S(m, "barra") == "viga");
            double dv = D(viga, "d");
            foreach (var mb in Lista(g["membros"]))
            {
                double d = D(mb, "d"), bf = D(mb, "bf"), tw = D(mb, "tw"), tf = D(mb, "tf");
                if (!Ok(d) || !Ok(bf) || !Ok(tw) || !Ok(tf)) continue;
                string papel = S(mb, "barra");
                bool passante = Lista(g["membros"]).Count() == 1 && xMinLig < -1;   // emenda: a barra passa pelo nó (x < 0 e x > 0)
                if (papel == "viga" || passante)
                {
                    r.NomeViga = S(mb, "perfil");
                    double x0 = passante ? xMinLig - 0.55 * d : Ok(xPonta) ? xPonta : xMinLig, x1 = Math.Max(x0 + 1.25 * d, xMaxLig + 0.55 * d);
                    bool fv = F("viga");
                    if (!Ok(hs)) hs = 0; if (!Ok(hi)) hi = 0; if (!Ok(cs)) cs = 0; if (!Ok(ci)) ci = 0;
                    var web = new List<Point>();
                    if (hi > 0) { web.Add(new Point(x0, -d / 2 + hi)); web.Add(new Point(x0 + ci, -d / 2 + hi)); web.Add(new Point(x0 + ci, -d / 2 + tf)); }
                    else web.Add(new Point(x0, -d / 2 + tf));
                    web.Add(new Point(x1, -d / 2 + tf)); web.Add(new Point(x1, d / 2 - tf));
                    if (hs > 0) { web.Add(new Point(x0 + cs, d / 2 - tf)); web.Add(new Point(x0 + cs, d / 2 - hs)); web.Add(new Point(x0, d / 2 - hs)); }
                    else web.Add(new Point(x0, d / 2 - tf));
                    r.Pecas.Add(new Prisma { Base = web.Select(q => new Point3D(q.X, 0, q.Y)).ToArray(), N = new Vector3D(0, 1, 0), T = tw, Camada = 0, Grupo = "viga", Nome = r.NomeViga, Falha = fv, Tag = TagLig.Viga });
                    r.Pecas.Add(Caixa(x0 + (hs > 0 ? cs : 0), x1, -bf / 2, bf / 2, d / 2 - tf, d / 2, 0, "viga")); r.Pecas[r.Pecas.Count - 1].Tag = TagLig.Viga;
                    r.Pecas.Add(Caixa(x0 + (hi > 0 ? ci : 0), x1, -bf / 2, bf / 2, -d / 2, -d / 2 + tf, 0, "viga")); r.Pecas[r.Pecas.Count - 1].Tag = TagLig.Viga;
                }
                else
                {
                    r.NomeSuporte = S(mb, "perfil");
                    string apoio = S(mb, "apoio");
                    double ext = Math.Max(Ok(dv) ? 0.95 * dv : 250, (zMaxLig - zMinLig) * 0.9);
                    double z0 = Math.Min(zMinLig, Ok(dv) ? -dv / 2 : zMinLig) - ext * 0.6, z1 = Math.Max(zMaxLig, Ok(dv) ? dv / 2 : zMaxLig) + ext * 0.6;
                    var lst = new List<Prisma>();
                    if (apoio.Contains("viga"))
                    {
                        double zt = Ok(dv) ? dv / 2 : zMaxLig, Ly = Math.Max(2.2 * bf, 380);
                        lst.Add(Caixa(-tw / 2, tw / 2, -Ly, Ly, zt - d + tf, zt - tf, 0, "suporte"));
                        lst.Add(Caixa(-bf / 2, bf / 2, -Ly, Ly, zt - tf, zt, 0, "suporte"));
                        lst.Add(Caixa(-bf / 2, bf / 2, -Ly, Ly, zt - d, zt - d + tf, 0, "suporte"));
                    }
                    else if (apoio.Contains("alma"))
                    {
                        lst.Add(Caixa(-tw / 2, tw / 2, -d / 2 + tf, d / 2 - tf, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(-bf / 2, bf / 2, d / 2 - tf, d / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(-bf / 2, bf / 2, -d / 2, -d / 2 + tf, z0, z1, 0, "suporte"));
                    }
                    else
                    {
                        lst.Add(Caixa(-d / 2 + tf, d / 2 - tf, -tw / 2, tw / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(d / 2 - tf, d / 2, -bf / 2, bf / 2, z0, z1, 0, "suporte"));
                        lst.Add(Caixa(-d / 2, -d / 2 + tf, -bf / 2, bf / 2, z0, z1, 0, "suporte"));
                    }
                    foreach (var q in lst) { q.Falha = F("suporte"); q.Nome = r.NomeSuporte; q.Tag = TagLig.Suporte; }
                    r.Pecas.InsertRange(0, lst);
                }
            }
            if (!Lista(g["membros"]).Any(m => S(m, "barra") != "viga") && r.Soldas.Any() && pecasLig.Count > 0 && r.Pecas.Any(q => q.Grupo == "viga"))
            {
                r.Parede = true; r.XParede = xMinLig;   // tipo antigo: apoio sem perfil
            }
            return r;
        }

        // Placa de base (miniatura da biblioteca): pilar W 250 sobre chapa 420 × 360 × 19 com 4 chumbadores
        public static GeoLig PlacaBase()
        {
            var r = new GeoLig();
            double d = 253, bf = 254, tw = 8.6, tf = 14.2, H = 600;
            r.Pecas.Add(Caixa(-d / 2 + tf, d / 2 - tf, -tw / 2, tw / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(d / 2 - tf, d / 2, -bf / 2, bf / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(-d / 2, -d / 2 + tf, -bf / 2, bf / 2, 0, H, 0, "suporte"));
            r.Pecas.Add(Caixa(-210, 210, -180, 180, -19, 0, 1, "chapas"));
            foreach (var (x, y) in new[] { (-165.0, -135.0), (165.0, -135.0), (165.0, 135.0), (-165.0, 135.0) })
                r.Parafs.Add(new Paraf { O = new Point3D(x, y, 30), E = new Vector3D(0, 0, -1), D = 19, L = 160, Pega = 49 });
            return r;
        }

        // interpolação (mesma estrutura) para animar a troca; null = estrutura diferente (o desenho faz fade)
        public static GeoLig Misturar(GeoLig a, GeoLig b, double t)
        {
            if (a == null || b == null || a.Pecas.Count != b.Pecas.Count || a.Parafs.Count != b.Parafs.Count || a.Soldas.Count != b.Soldas.Count) return null;
            for (int i = 0; i < a.Pecas.Count; i++) if (a.Pecas[i].Base.Length != b.Pecas[i].Base.Length) return null;
            for (int i = 0; i < a.Soldas.Count; i++) if (a.Soldas[i].P.Length != b.Soldas[i].P.Length) return null;
            Point3D LP(Point3D p, Point3D q) => new Point3D(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
            double L(double x, double y) => x + (y - x) * t;
            var r = new GeoLig { Parede = b.Parede, XParede = L(a.XParede, b.XParede), NomeViga = b.NomeViga, NomeSuporte = b.NomeSuporte };
            for (int i = 0; i < b.Pecas.Count; i++)
            {
                var pa = a.Pecas[i]; var pb = b.Pecas[i];
                r.Pecas.Add(new Prisma { Base = pb.Base.Select((q, k) => LP(pa.Base[k], q)).ToArray(), N = pb.N, T = L(pa.T, pb.T), Camada = pb.Camada, Grupo = pb.Grupo, Nome = pb.Nome, Falha = pb.Falha, Tag = pb.Tag });
            }
            for (int i = 0; i < b.Parafs.Count; i++)
            {
                var pa = a.Parafs[i]; var pb = b.Parafs[i];
                r.Parafs.Add(new Paraf { O = LP(pa.O, pb.O), E = pb.E, D = L(pa.D, pb.D), L = L(pa.L, pb.L), Pega = L(pa.Pega, pb.Pega), Falha = pb.Falha, Tag = pb.Tag });
            }
            for (int i = 0; i < b.Soldas.Count; i++)
                r.Soldas.Add(new Cordao { P = b.Soldas[i].P.Select((q, k) => LP(a.Soldas[i].P[k], q)).ToArray(), Perna = L(a.Soldas[i].Perna, b.Soldas[i].Perna), Falha = b.Soldas[i].Falha, Tag = b.Soldas[i].Tag });
            return r;
        }

        public IEnumerable<Point3D> Foco()
        {
            foreach (var p in Pecas.Where(q => q.Camada > 0)) foreach (var q in p.Base) yield return q;
            foreach (var b in Parafs) { yield return b.O; yield return b.O + b.E * b.Pega; }
            foreach (var s in Soldas) foreach (var q in s.P) yield return q;
        }

        public IEnumerable<Point3D> Pontos()
        {
            foreach (var p in Pecas) foreach (var q in p.Base) { yield return q + p.N * p.T / 2; yield return q - p.N * p.T / 2; }
            foreach (var b in Parafs) { yield return b.O - b.E * 0.7 * b.D; yield return b.O + b.E * b.L; }
            foreach (var s in Soldas) foreach (var q in s.P) yield return q;
        }
    }

    public enum VistaLig { Elevacao, Planta, Lateral, Iso }

    public class DesenhoLigacao : FrameworkElement
    {
        public static readonly DependencyProperty ProgProperty = DependencyProperty.Register("Prog", typeof(double), typeof(DesenhoLigacao),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Prog { get => (double)GetValue(ProgProperty); set => SetValue(ProgProperty, value); }

        public VistaLig Vista;
        public bool Miniatura;
        public string Titulo;
        GeoLig geo, anterior;
        double yaw = 104, pitch = 24;
        Point? arrasto; double yawIni, pitchIni;

        public DesenhoLigacao(VistaLig v, bool miniatura = false)
        {
            Vista = v; Miniatura = miniatura; ClipToBounds = true; SnapsToDevicePixels = true;
            if (v == VistaLig.Iso && !miniatura)
            {
                Cursor = Cursors.SizeAll;
                ToolTip = "Arraste para girar · clique numa peça = vai ao campo · duplo clique = vista inicial";
                System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 900);
            }
        }

        public GeoLig Geo => geo;

        // troca a geometria; a mudança é animada (posições interpoladas ou fade) com o valor final já gravado
        public void Mostrar(GeoLig g, bool animar = true)
        {
            anterior = geo == null ? null : (Prog < 1 ? GeoLig.Misturar(anterior, geo, Prog) ?? geo : geo);
            geo = g;
            if (animar && anterior != null) Anim.Valor(this, ProgProperty, 0, 1, 260);
            else { anterior = null; Prog = 1; InvalidateVisual(); }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Miniatura) return;
            descida = e.GetPosition(this);
            if (Vista != VistaLig.Iso) { CaptureMouse(); e.Handled = true; return; }
            if (e.ClickCount == 2) { yaw = 104; pitch = 24; InvalidateVisual(); e.Handled = true; return; }
            arrasto = e.GetPosition(this); yawIni = yaw; pitchIni = pitch; CaptureMouse(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (!Miniatura && ClicouPeca != null && !IsMouseCaptured) Cursor = PecaEm(p) != null ? Cursors.Hand : Vista == VistaLig.Iso ? Cursors.SizeAll : Cursors.Arrow;
            if (arrasto == null || !IsMouseCaptured) return;
            yaw = yawIni - (p.X - arrasto.Value.X) * 0.45;
            pitch = Math.Max(-80, Math.Min(85, pitchIni + (p.Y - arrasto.Value.Y) * 0.45));
            InvalidateVisual();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            arrasto = null; bool tinha = IsMouseCaptured; ReleaseMouseCapture();
            if (tinha && descida is Point d0 && (e.GetPosition(this) - d0).Length < 4 && PecaEm(d0) is string tag) ClicouPeca?.Invoke(tag);
            descida = null;
        }

        // base da vista: R (direita), U (cima), F (para o observador)
        (Vector3D R, Vector3D U, Vector3D F) Base()
        {
            switch (Vista)
            {
                case VistaLig.Elevacao: return (new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, -1, 0));
                case VistaLig.Planta: return (new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));
                case VistaLig.Lateral: return (new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), new Vector3D(1, 0, 0));
                default:
                    double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
                    var F = new Vector3D(Math.Cos(p) * Math.Cos(y), Math.Cos(p) * Math.Sin(y), Math.Sin(p));
                    var R = Vector3D.CrossProduct(new Vector3D(0, 0, 1), F); R.Normalize();
                    var U = Vector3D.CrossProduct(F, R);
                    return (R, U, F);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(TemaPlant.BBgCanvas, null, new Rect(0, 0, W, H));
            if (geo == null || W < 40 || H < 40) return;
            double t = Prog;
            var mix = anterior == null || t >= 1 ? null : GeoLig.Misturar(anterior, geo, t);
            if (mix != null) Desenhar(dc, mix, 1);
            else
            {
                if (anterior != null && t < 1) Desenhar(dc, anterior, 1 - t);
                Desenhar(dc, geo, anterior != null && t < 1 ? t : 1);
            }
        }

        // clique sem arrastar numa peca: tag "<secao>.<campo>" (TagLig) ou null
        public event Action<string> ClicouPeca;
        Func<Point3D, Point> proj; (Vector3D R, Vector3D U, Vector3D F) bs; double escUlt;
        Point? descida;

        // capturas/testes: um ponto da tela onde o clique cai numa chapa (varre a caixa da 1a chapa)
        public Point? CentroChapa()
        {
            var pc = geo?.Pecas.FirstOrDefault(q => q.Camada == 1);
            if (pc == null || proj == null) return null;
            var pts = pc.Base.Select(proj).ToList();
            double x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
            for (int a = 0; a <= 8; a++) for (int b = 0; b <= 8; b++)
            {
                var q = new Point(x0 + (x1 - x0) * (0.1 + 0.1 * a), y0 + (y1 - y0) * (0.1 + 0.1 * b));
                if (PecaEm(q) is string t && t.StartsWith("Chapa.")) return q;
            }
            return null;
        }

        public string PecaEm(Point p)
        {
            if (geo == null || proj == null) return null;
            foreach (var b in geo.Parafs)
            {
                double r = Math.Max(5, b.D * escUlt * 0.8);
                if ((proj(b.O) - p).Length <= r || (proj(b.O + b.E * b.Pega) - p).Length <= r) return b.Tag;
            }
            foreach (var s in geo.Soldas)
            {
                var sp = s.P.Select(proj).ToList();
                for (int i = 1; i < sp.Count; i++) if (DistSeg(p, sp[i - 1], sp[i]) <= 4) return s.Tag;
            }
            string melhor = null; double zm = double.NegativeInfinity;
            foreach (var pc in geo.Pecas.Where(q => !string.IsNullOrEmpty(q.Tag)))
            {
                var poly = Casco(pc.Base.SelectMany(q => new[] { proj(q + pc.N * pc.T / 2), proj(q - pc.N * pc.T / 2) }).ToList());
                if (poly.Count < 3 || !Dentro(poly, p)) continue;
                double z = pc.Base.Average(q => Dot(q, bs.F)) + pc.Camada * 1e4;   // pecas da ligacao por cima dos perfis
                if (z > zm) { zm = z; melhor = pc.Tag; }
            }
            return melhor;
        }
        static bool Dentro(List<Point> poly, Point p)
        {
            bool dentro = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) dentro = !dentro;
            return dentro;
        }
        static double DistSeg(Point p, Point a, Point b)
        {
            var ab = b - a; double l2 = ab.LengthSquared; if (l2 < 1e-9) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        void Desenhar(DrawingContext dc, GeoLig g, double opac)
        {
            double W = ActualWidth, H = ActualHeight;
            var (R, U, F) = Base();
            var pts = g.Pontos().ToList();
            if (pts.Count == 0) return;
            double x0 = pts.Min(p => Dot(p, R)), x1 = pts.Max(p => Dot(p, R)), y0 = pts.Min(p => Dot(p, U)), y1 = pts.Max(p => Dot(p, U));
            // enquadra a ligação (chapas, parafusos, soldas) com folga; os perfis continuam além da borda, como num detalhe
            var foco = g.Foco().ToList();
            if (foco.Count > 0)
            {
                double fx0 = foco.Min(p => Dot(p, R)), fx1 = foco.Max(p => Dot(p, R)), fy0 = foco.Min(p => Dot(p, U)), fy1 = foco.Max(p => Dot(p, U));
                double folga = Math.Max((Vista == VistaLig.Iso && !Miniatura ? 0.75 : 0.38) * Math.Max(fx1 - fx0, fy1 - fy0), Miniatura ? 70 : 90);
                x0 = Math.Max(x0, fx0 - folga); x1 = Math.Min(x1, fx1 + folga); y0 = Math.Max(y0, fy0 - folga); y1 = Math.Min(y1, fy1 + folga);
            }
            double mL = Miniatura ? 8 : 34, mR = Miniatura ? 8 : 92, mT = Miniatura ? 8 : 40, mB = Miniatura ? 8 : 58;
            if (Vista == VistaLig.Iso && !Miniatura) { mL = mR = 24; mT = 36; mB = 24; }
            double esc = Math.Min((W - mL - mR) / Math.Max(1, x1 - x0), (H - mT - mB) / Math.Max(1, y1 - y0));
            if (esc <= 0) return;
            double ox = mL + (W - mL - mR - (x1 - x0) * esc) / 2 - x0 * esc, oy = mT + (H - mT - mB - (y1 - y0) * esc) / 2 + y1 * esc;
            Point P(Point3D p) => new Point(ox + Dot(p, R) * esc, oy - Dot(p, U) * esc);
            if (g == geo) { proj = P; bs = (R, U, F); escUlt = esc; }
            if (opac < 1) dc.PushOpacity(opac);
            if (Vista == VistaLig.Iso) Iso(dc, g, R, U, F, P, esc);
            else Orto(dc, g, R, U, F, P, esc);
            if (!Miniatura && Titulo != null)
            {
                var tt = Txt(Titulo.ToUpperInvariant(), 10.5, TemaPlant.BTexto2, FontWeights.SemiBold);
                dc.DrawText(tt, new Point(12, 10));
            }
            if (opac < 1) dc.Pop();
        }

        static double Dot(Point3D p, Vector3D v) => p.X * v.X + p.Y * v.Y + p.Z * v.Z;

        FormattedText Txt(string s, double tam, Brush cor, FontWeight? peso = null) =>
            new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(TemaPlant.Fonte, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal), tam, cor, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        static StreamGeometry Poli(IList<Point> p, bool fechar = true)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(p[0], true, fechar); c.PolyLineTo(p.Skip(1).ToList(), true, true); }
            g.Freeze();
            return g;
        }

        static List<Point> Casco(List<Point> p)   // casco convexo (cadeia monótona)
        {
            var s = p.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
            if (s.Count < 3) return s;
            double X(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var h = new List<Point>();
            foreach (var q in s) { while (h.Count >= 2 && X(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var q = s[i]; while (h.Count >= lo && X(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        // ---------------------------------------------------------------- vistas ortogonais
        void Orto(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            double lw = Miniatura ? 0.8 : 1;
            // perfis, depois chapas e cantoneiras (as peças da ligação ficam por cima)
            foreach (var camada in new[] { 0, 1, 2 })
                foreach (var pc in g.Pecas.Where(q => q.Camada == camada))
                {
                    bool frente = Math.Abs(Vector3D.DotProduct(pc.N, F)) > 0.85;
                    var contorno = frente ? pc.Base.Select(q => P(q)).ToList()
                        : Casco(pc.Base.SelectMany(q => new[] { P(q + pc.N * pc.T / 2), P(q - pc.N * pc.T / 2) }).ToList());
                    if (contorno.Count < 3) continue;
                    var geom = Poli(contorno);
                    Brush fundo = pc.Falha ? TemaPlant.BErroFundo : camada == 0 ? TemaPlant.BLigPerfil : TemaPlant.BPlacaFundo;
                    Brush traco = pc.Falha ? TemaPlant.BErro : camada == 0 ? TemaPlant.BTexto2 : TemaPlant.BPlacaContorno;
                    dc.DrawGeometry(fundo, null, geom);
                    if (!frente && camada > 0)   // chapa de topo/de lado: hachura da espessura
                    {
                        dc.PushClip(geom);
                        var b = geom.Bounds; var ph = new Pen(pc.Falha ? TemaPlant.BErro : TemaPlant.BPlacaHachura, 1);
                        for (double d = -b.Height; d < b.Width + b.Height; d += Miniatura ? 4 : 5) dc.DrawLine(ph, new Point(b.Left + d, b.Top), new Point(b.Left + d - b.Height, b.Bottom));
                        dc.Pop();
                    }
                    dc.DrawGeometry(null, new Pen(traco, (camada == 0 ? 1.2 : 1.7) * lw) { LineJoin = PenLineJoin.Round }, geom);
                }
            if (g.Parede && !Miniatura && Math.Abs(R.X) > 0.9)
            {
                double x = P(new Point3D(g.XParede, 0, 0)).X;
                var pp = new Pen(TemaPlant.BBarra, 2);
                dc.DrawLine(pp, new Point(x, 30), new Point(x, ActualHeight - 50));
                var ph = new Pen(TemaPlant.BTexto3, 1);
                for (double y = 34; y < ActualHeight - 50; y += 9) dc.DrawLine(ph, new Point(x, y), new Point(x - 8, y + 8));
                var ta = Txt("apoio", 10.5, TemaPlant.BTexto3);
                dc.DrawText(ta, new Point(x - ta.Width - 12, 34));
            }
            // soldas: cordão com triângulos (símbolo de filete); de topo vira o triângulo do filete
            foreach (var s in g.Soldas)
            {
                var sp = s.P.Select(P).ToList();
                double comp = 0; for (int i = 1; i < sp.Count; i++) comp += (sp[i] - sp[i - 1]).Length;
                Brush cor = s.Falha ? TemaPlant.BErro : TemaPlant.BLigSolda;
                if (comp > 6)
                {
                    dc.DrawGeometry(null, new Pen(cor, Miniatura ? 1.6 : 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Poli(sp, false));
                    if (!Miniatura)
                        for (int i = 1; i < sp.Count; i++)
                        {
                            var a = sp[i - 1]; var v = sp[i] - a; double L = v.Length; if (L < 1) continue; v /= L;
                            var n = new Vector(-v.Y, v.X); double k = Math.Max(3, Math.Min(6, s.Perna * esc * 0.8));
                            for (double d = 6; d < L - 3; d += 11)
                            {
                                var q = a + v * d;
                                dc.DrawGeometry(cor, null, Poli(new[] { q, q + v * k, q + n * k }));
                            }
                        }
                }
                else
                {
                    var q = sp[0]; double k = Math.Max(4, s.Perna * esc * 1.2);
                    dc.DrawGeometry(cor, null, Poli(new[] { new Point(q.X, q.Y - k), new Point(q.X + k, q.Y), q }));
                }
            }
            // parafusos
            foreach (var b in g.Parafs)
            {
                double fe = Vector3D.DotProduct(b.E, F);
                Brush cor = b.Falha ? TemaPlant.BErro : TemaPlant.BRealce;
                if (Math.Abs(fe) > 0.85)
                {
                    var c = P(b.O); double r = Math.Max(Miniatura ? 1.5 : 3, b.D / 2 * esc);
                    if (!Miniatura && fe < 0)   // cabeça para o observador: sextavado
                    {
                        double s = 0.85 * b.D * esc;
                        var hx = Enumerable.Range(0, 6).Select(i => new Point(c.X + s * Math.Cos(Math.PI / 3 * i + Math.PI / 6), c.Y + s * Math.Sin(Math.PI / 3 * i + Math.PI / 6))).ToList();
                        dc.DrawGeometry(null, new Pen(TemaPlant.B(TemaPlant.Realce, 110), 1), Poli(hx));
                    }
                    dc.DrawEllipse(TemaPlant.BBgCanvas, new Pen(cor, Miniatura ? 1 : 1.6), c, r, r);
                    if (!Miniatura)
                    {
                        var pc = new Pen(cor, 0.9);
                        dc.DrawLine(pc, new Point(c.X - r * 1.6, c.Y), new Point(c.X + r * 1.6, c.Y));
                        dc.DrawLine(pc, new Point(c.X, c.Y - r * 1.6), new Point(c.X, c.Y + r * 1.6));
                    }
                }
                else
                {
                    var e2 = new Vector(Vector3D.DotProduct(b.E, R), -Vector3D.DotProduct(b.E, U)); if (e2.Length < 1e-6) continue; e2.Normalize();
                    var n = new Vector(-e2.Y, e2.X);
                    var a = P(b.O); double d = b.D * esc, L = b.L * esc, ph = 0.65 * b.D * esc, pg = b.Pega * esc;
                    Point Q(double ao, double tr) => a + e2 * ao + n * tr;
                    var pen = new Pen(cor, Miniatura ? 0.8 : 1.3);
                    dc.DrawGeometry(TemaPlant.BBgCanvas, pen, Poli(new[] { Q(0, -d / 2), Q(L, -d / 2), Q(L, d / 2), Q(0, d / 2) }));
                    dc.DrawGeometry(TemaPlant.B(TemaPlant.Realce, 60), pen, Poli(new[] { Q(-ph, -0.85 * d), Q(0, -0.85 * d), Q(0, 0.85 * d), Q(-ph, 0.85 * d) }));
                    if (pg + 0.8 * b.D * esc < L + 1)
                        dc.DrawGeometry(TemaPlant.B(TemaPlant.Realce, 60), pen, Poli(new[] { Q(pg, -0.85 * d), Q(pg + 0.8 * b.D * esc, -0.85 * d), Q(pg + 0.8 * b.D * esc, 0.85 * d), Q(pg, 0.85 * d) }));
                    if (!Miniatura) dc.DrawLine(new Pen(cor, 0.7) { DashStyle = new DashStyle(new double[] { 6, 3, 1, 3 }, 0) }, Q(-ph - 4, 0), Q(L + 4, 0));
                }
            }
            if (!Miniatura) { Cotas(dc, g, R, U, F, P, esc); Rotulos(dc, g, R, U, F, P, esc); }
        }

        // cotas: cadeias de borda → furos → borda na horizontal (embaixo) e na vertical (à direita) da chapa com mais furos
        void Cotas(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            var placas = g.Pecas.Where(q => q.Camada > 0).ToList();
            if (placas.Count == 0 || g.Parafs.Count == 0) return;
            foreach (var horizontal in new[] { true, false })
            {
                var eixo = horizontal ? R : U;
                var furos = g.Parafs.Where(b => Math.Abs(Vector3D.DotProduct(b.E, eixo)) < 0.3).ToList();
                if (furos.Count == 0) continue;
                (double a, double b, bool frente, double area, int n) Faixa(GeoLig.Prisma pc)
                {
                    var c = pc.Base.SelectMany(q => new[] { q + pc.N * pc.T / 2, q - pc.N * pc.T / 2 }).ToList();
                    double a = c.Min(q => Dot(q, eixo)), b = c.Max(q => Dot(q, eixo));
                    var outro = horizontal ? U : R;
                    double oa = c.Min(q => Dot(q, outro)), ob = c.Max(q => Dot(q, outro));
                    int n = furos.Count(f => Dot(f.O, eixo) > a - 0.5 && Dot(f.O, eixo) < b + 0.5 && Dot(f.O, outro) > oa - f.L && Dot(f.O, outro) < ob + f.L);
                    return (a, b, Math.Abs(Vector3D.DotProduct(pc.N, F)) > 0.85, (b - a) * (ob - oa), n);
                }
                var cand = placas.Select(pc => (pc, f: Faixa(pc))).Where(x => x.f.n > 0).OrderByDescending(x => x.f.n).ThenByDescending(x => x.f.frente).ThenBy(x => x.f.area).FirstOrDefault();
                if (cand.pc == null) continue;
                var pl = cand.pc;
                var vals = new List<double> { cand.f.a, cand.f.b };
                foreach (var f in furos) { double v = Dot(f.O, eixo); if (v > cand.f.a - 0.5 && v < cand.f.b + 0.5) vals.Add(v); }
                vals = vals.OrderBy(v => v).Aggregate(new List<double>(), (l, v) => { if (l.Count == 0 || v - l[l.Count - 1] > 0.5) l.Add(v); return l; });
                if (vals.Count < 3) continue;
                var cs = pl.Base.SelectMany(q => new[] { P(q + pl.N * pl.T / 2), P(q - pl.N * pl.T / 2) }).ToList();
                double pos = horizontal ? Math.Min(ActualHeight - 22, cs.Max(q => q.Y) + 26) : Math.Min(ActualWidth - 30, cs.Max(q => q.X) + 30);
                var pen = new Pen(TemaPlant.BTexto2, 1);
                var penL = new Pen(TemaPlant.BTextoMudo, 0.8) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                Point Tela(double v) { var p = P(new Point3D(0, 0, 0) + eixo * v); return horizontal ? new Point(p.X, pos) : new Point(pos, p.Y); }
                var tl = vals.Select(Tela).ToList();
                dc.DrawLine(pen, tl[0], tl[tl.Count - 1]);
                foreach (var q in tl)
                {
                    var d = new Vector(4, -4);
                    dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - d, q + d);
                    var desde = horizontal ? new Point(q.X, cs.Max(c => c.Y) + 3) : new Point(cs.Max(c => c.X) + 3, q.Y);
                    dc.DrawLine(penL, desde, q);
                }
                // trechos; trechos internos iguais seguidos viram "n × s"
                var seg = new List<(int i0, int i1, double v, int n)>();
                for (int i = 0; i < vals.Count - 1; i++)
                {
                    double v = vals[i + 1] - vals[i];
                    bool interno = i > 0 && i < vals.Count - 2;
                    if (interno && seg.Count > 0 && seg[seg.Count - 1].i0 > 0 && Math.Abs(seg[seg.Count - 1].v - v) < 0.5 && seg[seg.Count - 1].i1 == i) { var u = seg[seg.Count - 1]; seg[seg.Count - 1] = (u.i0, i + 1, v, u.n + 1); }
                    else seg.Add((i, i + 1, v, 1));
                }
                string Fmt(double v) => Num.Fmt(v, Math.Abs(v - Math.Round(v)) < 0.05 ? 0 : 1);
                bool cortou = false;
                foreach (var s in seg)
                {
                    string txt = (s.n > 1 ? s.n + " × " : "") + Fmt(s.v);
                    var ft = Txt(txt, 11, TemaPlant.BTexto1);
                    var m = new Point((tl[s.i0].X + tl[s.i1].X) / 2, (tl[s.i0].Y + tl[s.i1].Y) / 2);
                    double lpx = (tl[s.i1] - tl[s.i0]).Length;
                    if ((horizontal ? ft.Width : ft.Height) + 4 > lpx) { cortou = true; continue; }   // não cabe: fica só o traço (o total vai abaixo)
                    Rect rr = horizontal ? new Rect(m.X - ft.Width / 2 - 3, m.Y + 3, ft.Width + 6, ft.Height) : new Rect(m.X + 5, m.Y - ft.Height / 2, ft.Width + 6, ft.Height);
                    dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                    dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
                }
                if (cortou || seg.Count >= 3)   // cota total da chapa, uma linha mais para fora
                {
                    var off = horizontal ? new Vector(0, 22) : new Vector(46, 0);
                    Point a = tl[0] + off, b = tl[tl.Count - 1] + off;
                    dc.DrawLine(pen, a, b);
                    foreach (var q in new[] { a, b }) { dc.DrawLine(new Pen(TemaPlant.BTexto2, 1.3), q - new Vector(4, -4), q + new Vector(4, -4)); dc.DrawLine(penL, q - off, q); }
                    var ft = Txt(Fmt(vals[vals.Count - 1] - vals[0]), 11, TemaPlant.BTexto1, FontWeights.SemiBold);
                    var m = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    Rect rr = horizontal ? new Rect(m.X - ft.Width / 2 - 3, m.Y + 3, ft.Width + 6, ft.Height) : new Rect(m.X + 5, m.Y - ft.Height / 2, ft.Width + 6, ft.Height);
                    dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, rr, 2, 2);
                    dc.DrawText(ft, new Point(rr.X + 3, rr.Y));
                }
            }
        }

        // rótulos: perfis, chapa (espessura × altura) e a chamada de solda (linha de referência + triângulo + perna)
        void Rotulos(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            void Caixa(string s, Point p, Brush cor, bool esquerda = false)
            {
                var ft = Txt(s, 11, cor);
                var r = new Rect(esquerda ? p.X - ft.Width - 8 : p.X, p.Y, ft.Width + 8, ft.Height + 2);
                if (r.Right > ActualWidth - 4) r.X = ActualWidth - 4 - r.Width;
                if (r.X < 4) r.X = 4;
                dc.DrawRoundedRectangle(TemaPlant.BFundoTextoCanvas, null, r, 3, 3);
                dc.DrawText(ft, new Point(r.X + 4, r.Y + 1));
            }
            var viga = g.Pecas.Where(q => q.Grupo == "viga").SelectMany(q => q.Base).ToList();
            if (viga.Count > 0 && g.NomeViga != "" && Vista != VistaLig.Lateral)
            {
                var ps = viga.Select(P).ToList();
                Caixa(g.NomeViga.Replace("x", "×"), new Point(ps.Max(q => q.X) - 4, ps.Min(q => q.Y) - 20), TemaPlant.BRealceClaro, true);
            }
            var sup = g.Pecas.Where(q => q.Grupo == "suporte").SelectMany(q => q.Base).ToList();
            if (sup.Count > 0 && g.NomeSuporte != "")
            {
                var ps = sup.Select(P).ToList();
                Caixa(g.NomeSuporte.Replace("x", "×"), new Point(ps.Min(q => q.X), Math.Max(30, ps.Min(q => q.Y) + 2)), TemaPlant.BTexto2);
            }
            var ch = g.Pecas.FirstOrDefault(q => q.Camada > 0 && Math.Abs(Vector3D.DotProduct(q.N, F)) > 0.85);
            if (ch != null && Vista == VistaLig.Elevacao)
            {
                var ps = ch.Base.Select(P).ToList();
                double alt = ch.Base.Max(q => Dot(q, U)) - ch.Base.Min(q => Dot(q, U));
                string nome = ch.Camada == 2 ? ch.Nome : "ch " + Num.Fmt(ch.T, Math.Abs(ch.T - Math.Round(ch.T)) < 0.05 ? 0 : 1) + " × " + Num.Fmt(alt, 0);
                Caixa(nome, new Point(ps.Min(q => q.X) + 4, Math.Max(30, ps.Min(q => q.Y) - 22)), ch.Falha ? TemaPlant.BErro : TemaPlant.BTexto1);
            }
            var sd = g.Soldas.FirstOrDefault();
            if (sd != null && Vista != VistaLig.Planta)
            {
                var sp = sd.P.Select(P).ToList();
                double comp = 0; for (int i = 1; i < sp.Count; i++) comp += (sp[i] - sp[i - 1]).Length;
                if (comp > 10)
                {
                    var m = new Point((sp[0].X + sp[1].X) / 2, (sp[0].Y + sp[1].Y) / 2);
                    var cotov = new Point(m.X - 34, m.Y + 34); var fim = new Point(cotov.X - 46, cotov.Y);
                    Brush cor = sd.Falha ? TemaPlant.BErro : TemaPlant.BTexto2;
                    var pen = new Pen(cor, 1);
                    dc.DrawLine(pen, m, cotov); dc.DrawLine(pen, cotov, fim);
                    var v = m - cotov; v.Normalize(); var n = new Vector(-v.Y, v.X);
                    dc.DrawGeometry(cor, null, Poli(new[] { m, m - v * 7 + n * 3, m - v * 7 - n * 3 }));
                    double xm = (cotov.X + fim.X) / 2;
                    dc.DrawGeometry(cor, null, Poli(new[] { new Point(xm - 5, cotov.Y), new Point(xm + 5, cotov.Y), new Point(xm - 5, cotov.Y + 9) }));
                    var ft = Txt(Num.Fmt(sd.Perna, Math.Abs(sd.Perna - Math.Round(sd.Perna)) < 0.05 ? 0 : 1), 11, sd.Falha ? TemaPlant.BErro : TemaPlant.BTexto1, FontWeights.SemiBold);
                    dc.DrawText(ft, new Point(fim.X - ft.Width - 4, cotov.Y - ft.Height / 2));
                }
            }
        }

        // ---------------------------------------------------------------- isométrica (pintor + sombreamento plano)
        struct Face { public Point[] P; public double Z; public Color C; public bool Linha; public byte A; public int Plano; }

        void Iso(DrawingContext dc, GeoLig g, Vector3D R, Vector3D U, Vector3D F, Func<Point3D, Point> P, double esc)
        {
            var faces = new List<Face>();
            var luz = F + new Vector3D(0.35, -0.25, 0.85); luz.Normalize();
            Color Tom(Color c, Vector3D n) { double k = 0.52 + 0.48 * Math.Abs(Vector3D.DotProduct(n, luz)); return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)); }
            Color Cor(Brush b) => ((SolidColorBrush)b).Color;
            void Prisma(IList<Point3D> base_, Vector3D n, double t, Color c, byte alfa = 255, int plano = 1)
            {
                if (base_.Count < 3) return;
                var cen = new Point3D(base_.Average(q => q.X), base_.Average(q => q.Y), base_.Average(q => q.Z));
                var a = base_.Select(q => q + n * t / 2).ToArray(); var b = base_.Select(q => q - n * t / 2).ToArray();
                void Add(Point3D[] f, Vector3D nn)
                {
                    if (Vector3D.DotProduct(nn, F) <= 1e-6) return;
                    faces.Add(new Face { P = f.Select(P).ToArray(), Z = f.Average(q => Dot(q, F)), C = Tom(c, nn), Linha = true, A = alfa, Plano = plano });
                }
                Add(a, n); Add(b.Reverse().ToArray(), -n);
                for (int i = 0; i < base_.Count; i++)
                {
                    int j = (i + 1) % base_.Count;
                    var e = base_[j] - base_[i]; var nn = Vector3D.CrossProduct(e, n);
                    if (nn.Length < 1e-9) continue; nn.Normalize();
                    var mid = base_[i] + e / 2; if (Vector3D.DotProduct(nn, mid - cen) < 0) nn = -nn;
                    Add(new[] { a[i], a[j], b[j], b[i] }, nn);
                }
            }
            Color cPerfil = TemaPlant.LigPerfil3D, cPlaca = TemaPlant.LigChapa3D, cErro = TemaPlant.Erro, cPar = TemaPlant.Realce;
            foreach (var pc in g.Pecas) Prisma(pc.Base, pc.N, pc.T, pc.Falha ? cErro : pc.Camada == 0 ? cPerfil : cPlaca, pc.Camada == 0 ? (byte)215 : (byte)255, pc.Camada == 0 ? 0 : 1);   // perfis atrás e translúcidos: a ligação sempre aparece
            foreach (var b in g.Parafs)
            {
                var e = b.E; var ax = Vector3D.CrossProduct(e, Math.Abs(e.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0)); ax.Normalize();
                var ay = Vector3D.CrossProduct(e, ax);
                Point3D[] Anel(Point3D c, double r, int k) => Enumerable.Range(0, k).Select(i => c + ax * r * Math.Cos(2 * Math.PI * i / k) + ay * r * Math.Sin(2 * Math.PI * i / k)).ToArray();
                Color c = b.Falha ? cErro : cPar;
                // só o lado do observador (cabeça ou porca + ponta da rosca), por cima das chapas: o pintor não erra
                if (Vector3D.DotProduct(-e, F) > 0) Prisma(Anel(b.O - e * 0.33 * b.D, 0.85 * b.D, 6), e, 0.65 * b.D, c, 255, 2);
                else
                {
                    Prisma(Anel(b.O + e * (b.Pega + 0.4 * b.D), 0.85 * b.D, 6), e, 0.8 * b.D, c, 255, 2);
                    double ponta = b.L - b.Pega - 0.8 * b.D;
                    if (ponta > 1) Prisma(Anel(b.O + e * (b.Pega + 0.8 * b.D + ponta / 2), b.D / 2, 8), e, ponta, Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8)), 255, 2);
                }
            }
            foreach (var f in faces.OrderBy(f => f.Plano).ThenBy(f => f.Z))
            {
                var geo = Poli(f.P);
                var br = new SolidColorBrush(Color.FromArgb(f.A, f.C.R, f.C.G, f.C.B)); br.Freeze();
                dc.DrawGeometry(br, Miniatura ? null : new Pen(TemaPlant.B(TemaPlant.BgCanvas, 140), 0.6), geo);
            }
            foreach (var s in g.Soldas)
                dc.DrawGeometry(null, new Pen(s.Falha ? TemaPlant.BErro : TemaPlant.BLigSolda, Miniatura ? 1.6 : 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Poli(s.P.Select(P).ToList(), false));
        }
    }
}
