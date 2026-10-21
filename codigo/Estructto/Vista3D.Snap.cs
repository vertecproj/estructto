// Integração do snap (Snap3D.cs), do desenho do snap (SnapOverlay.cs) e do Medir (Medir3D.cs) na Vista3D.
// ponytail: superfície estreita de propósito (wt-so3d mexe na navegação e pode partir Vista3D.cs em partials). Em
// Vista3D.cs só há chamadas de uma linha para cá: AtrairSnap (no lugar do antigo Atrair), TeclaSnap, DigitarSnap,
// MouseSnap, PontoSnap, MedirClique, DesenharSnap, IniciarSnap, EstadoSnap/EtiquetaMedir e MontarMedir3D.
// Doc: docs/v3/SNAP-MEDIR.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using F = Fluent;

namespace NovoFtool
{
    public partial class Vista3D
    {
        public readonly Snap3D Snap = new Snap3D();
        public readonly Medidor3D Medidor = new Medidor3D();
        public ResultadoSnap UltimoSnap => snapRes;
        ResultadoSnap snapRes;
        readonly Stopwatch relogioSnap = Stopwatch.StartNew(); double relogioExtra;
        readonly DispatcherTimer timerPausa = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Snap3D.TempoAquisicao + 20) };
        GeoSnap geo; Modelo3D geoDe; bool geoRealista; Point pxPausa; (double[], double[], double) geoEixos; double[] geoNiveis;
        PainelMedir painelMedir;

        double AgoraSnap => relogioSnap.Elapsed.TotalMilliseconds + relogioExtra;
        Point3D? BaseSnap => inicio ?? (ferr == Ferramenta3D.Medir ? Medidor.Base : null);

        void IniciarSnap()
        {
            timerPausa.Tick += (s, e) => { timerPausa.Stop(); if (Desenhando && mouseDentro) AtualizarCursor(); };   // parado: o motor vê o tempo passar e adquire
            Medidor.Mudou += () => sobre.InvalidateVisual();
            FerramentaMudou += () =>
            {
                Snap.LimparRastreio(); Snap.ConsumirUnico();
                if (ferr == Ferramenta3D.Medir && painelMedir == null) { painelMedir = new PainelMedir(Medidor); Children.Add(painelMedir); }
                if (painelMedir != null) painelMedir.Visibility = ferr == Ferramenta3D.Medir ? Visibility.Visible : Visibility.Collapsed;
                if (ferr != Ferramenta3D.Medir) Medidor.Limpar();
            };
        }

        // Capturas/testes: simula o cursor parado (aquisição do rastreio sem esperar de verdade).
        public void EsperarSnap(double ms) { relogioExtra += ms; AtualizarCursor(); }

        // Geometria de atração: nós, eixos das barras e, no Realista, as arestas e os centros dos tubos do sólido.
        GeoSnap Geometria()
        {
            double zEixos = plano.Normal == 'Z' ? plano.Valor : 0;
            if (geo != null && geoDe == mod && geoRealista == Realista && geoEixos == (EixosX, EixosY, zEixos) && geoNiveis == NiveisEixos) return geo;
            var g = new GeoSnap();
            foreach (No3D n in mod.Nos) g.Nos.Add(P(n));
            g.Nos.AddRange(CruzamentosEixos());   // cruzamentos da grade estrutural em cada nível, o do plano primeiro (Vista3D.Grade.cs)
            geoEixos = (EixosX, EixosY, zEixos); geoNiveis = NiveisEixos;
            double h = raio * 0.006;
            foreach (Barra3D b in mod.Barras)
            {
                No3D na = NoPorId(b.Ni), nc = NoPorId(b.Nj); if (na == null || nc == null) continue;
                Point3D a = P(na), c = P(nc);
                g.Segs.Add(new SegSnap { A = a, B = c, Dono = b });
                if (!Realista) continue;
                Vector3D e = c - a; if (e.Length < 1e-9) continue;
                Vector3D v = Modelo3D.EixoY(e, b.Rot + b.RotPerfil), u = Vector3D.CrossProduct(v, Norm(e));
                Forma f = FormaDe(b.Secao, h * 2);
                for (int li = 0; li < f.Lacos.Count; li++)
                {
                    Laco l = f.Lacos[li];
                    if (l.Liso)   // tubo/redondo: centro nas duas pontas (só o contorno externo)
                    {
                        if (li == 0) foreach (Point3D o in new[] { a, c }) g.Circulos.Add(new CirculoSnap { C = o, N = e, R = ((Vector)l.P[0]).Length, Dono = b });
                        continue;
                    }
                    int k = l.P.Length;
                    for (int i = 0; i < k; i++)
                    {
                        Vector3D o1 = u * l.P[i].X + v * l.P[i].Y, o2 = u * l.P[(i + 1) % k].X + v * l.P[(i + 1) % k].Y;
                        g.Segs.Add(new SegSnap { A = a + o1, B = c + o1, Dono = b, Aresta = true });
                        g.Segs.Add(new SegSnap { A = a + o1, B = a + o2, Dono = b, Aresta = true });
                        g.Segs.Add(new SegSnap { A = c + o1, B = c + o2, Dono = b, Aresta = true });
                    }
                }
            }
            geo = g; geoDe = mod; geoRealista = Realista;
            return g;
        }

        // No lugar do antigo Atrair (nó > meio > orto/polar > grade > plano): o motor completo do AutoCAD.
        (Point3D? P, string Tipo) AtrairSnap(Point tela)
        {
            Snap.PassoGrade = PassoGrade;
            if (Orto != Snap.Orto) Snap.Orto = Orto;
            Direcoes(out Vector3D d, out Vector3D cima, out Vector3D dir);
            // o mesmo que Tela(), com a câmera calculada uma vez (o motor projeta milhares de pontos por movimento no Realista)
            double esc = Escala, cx = ActualWidth / 2, cy = ActualHeight / 2; Point3D al = alvo;
            bool persp = perspectiva;   // perspectiva (Vista3D.Navegacao.cs): a projeção completa
            Point Proj(Point3D q) { if (persp) return Tela(q); Vector3D r = q - al; return new Point(cx + Vector3D.DotProduct(r, dir) / esc, cy - Vector3D.DotProduct(r, cima) / esc); }
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            snapRes = Snap.Resolver(Geometria(), Proj, p => NoPlano(p), plano, tela, BaseSnap, AgoraSnap, shift, d);
            Orto = Snap.Orto;
            if (tela != pxPausa) { pxPausa = tela; timerPausa.Stop(); timerPausa.Start(); }   // só reinicia quando o cursor mexe
            return (snapRes.P, snapRes.Tipo == TipoSnap.Nenhum ? null : snapRes.Nome);
        }

        // Teclas do snap (F3, F8, F10, F11) e do Medir. true = consumiu.
        bool TeclaSnap(Key k)
        {
            switch (k)
            {
                case Key.F3: AplicarStatus(!Snap.Osnap, Snap.Orto, Snap.Polar, Snap.Rastreio); Aviso = "Snap a objetos " + (Snap.Osnap ? "ligado" : "desligado") + " (F3)."; return true;
                case Key.F8: AplicarStatus(Snap.Osnap, !Snap.Orto, Snap.Polar, Snap.Rastreio); Aviso = "Ortogonal " + (Orto ? "ligado (o polar desliga)" : "desligado") + " (F8)."; return true;
                case Key.F10: AplicarStatus(Snap.Osnap, Snap.Orto, !Snap.Polar, Snap.Rastreio); Aviso = "Polar " + (Snap.Polar ? "ligado, incremento " + Snap.PolarInc.ToString(CultureInfo.GetCultureInfo("pt-BR")) + "° (o ortogonal desliga)" : "desligado") + " (F10)."; return true;
                case Key.F11: AplicarStatus(Snap.Osnap, Snap.Orto, Snap.Polar, !Snap.Rastreio); Aviso = "Rastreamento " + (Snap.Rastreio ? "ligado" : "desligado") + " (F11)."; return true;
            }
            if (ferr != Ferramenta3D.Medir) return false;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            switch (k)
            {
                case Key.Escape:   // texto > seleções > sai do Medir
                    if (coord != "") coord = "";
                    else if (!Medidor.Limpar()) { Ferramenta = Ferramenta3D.Selecionar; return true; }
                    Snap.LimparRastreio(); AtualizarCursor(); return true;
                case Key.Enter:
                    if (coord != "") { Point3D? p = PontoDigitado(); coord = ""; if (p == null) Aviso = "Coordenada inválida: use x,y,z  @dx,dy,dz  #x,y,z  ou L<ângulo."; else PontoSnap(p.Value); }
                    else Medidor.Confirmar();
                    AtualizarCursor(); return true;
                case Key.Back:
                    if (coord != "") return false;
                    Medidor.Desfazer(); AtualizarCursor(); return true;
                case Key.OemPlus: case Key.Add: if (coord != "") return false; Medidor.Somar(1); return true;
                case Key.OemMinus: case Key.Subtract: if (coord != "") return false; Medidor.Somar(-1); return true;
                case Key.C when ctrl: try { if (Medidor.Res != null) Clipboard.SetText(Medidor.Res.ValorTxt); } catch { } return true;
            }
            return false;
        }

        // Estado da barra de status (F3/F8/F10/F11 ou os botões do rodapé). ORTO e Polar se excluem: ligar um desliga o outro.
        public void AplicarStatus(bool osnap, bool orto, bool polar, bool rastreio)
        {
            Snap.Osnap = osnap; Snap.Rastreio = rastreio; if (!rastreio) Snap.LimparRastreio();
            if (orto != Snap.Orto) Snap.Orto = orto;
            else if (polar != Snap.Polar) Snap.Polar = polar;
            Orto = Snap.Orto;
            AtualizarCursor();
            (Window.GetWindow(this) as MainWindow)?.SincronizarStatus3D();
        }

        // Letras digitadas na vista vão para a linha de comando (como no AutoCAD): END, MID, ME... + Enter.
        bool DigitarSnap(string texto)
        {
            if (string.IsNullOrEmpty(texto) || !char.IsLetter(texto[0])) return false;
            return (Window.GetWindow(this) as MainWindow)?.DigitarComando3D(texto) == true;
        }

        // Shift (ou Ctrl) + botão direito: menu de snap one-off no cursor.
        bool MouseSnap(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Right || !Desenhando) return false;
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0) return false;
            MenuSnap().IsOpen = true;
            e.Handled = true;
            return true;
        }

        static readonly (ModoSnap M, string Nome, string Ab)[] ModosMenu =
        {
            (ModoSnap.Extremidade, "Extremidade", "END"), (ModoSnap.Medio, "Ponto médio", "MID"), (ModoSnap.Intersecao, "Interseção", "INT"),
            (ModoSnap.Aparente, "Interseção aparente", "APP"), (ModoSnap.Extensao, "Extensão", "EXT"), (ModoSnap.Centro, "Centro", "CEN"),
            (ModoSnap.No, "Nó", "NOD"), (ModoSnap.Perpendicular, "Perpendicular", "PER"), (ModoSnap.Paralelo, "Paralelo", "PAR"), (ModoSnap.Proximo, "Mais próximo", "NEA"),
        };

        public ContextMenu MenuSnap()
        {
            var cm = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            MenuItem Item(string txt, string atalho, Action a, bool? marcado = null)
            {
                var mi = new MenuItem { Header = txt, InputGestureText = atalho ?? "" };
                if (marcado != null) { mi.IsCheckable = true; mi.IsChecked = marcado.Value; }
                mi.Click += (s, e) => { a(); AtualizarCursor(); };
                return mi;
            }
            foreach (var (m, nome, ab) in ModosMenu) cm.Items.Add(Item(nome, ab, () => { Snap.Unico = m; Aviso = "Snap único: " + nome + " (vale um clique)."; }));
            cm.Items.Add(Item("Nenhum", "NON", () => { Snap.Unico = ModoSnap.Desligar; Aviso = "Próximo clique sem snap."; }));
            cm.Items.Add(new Separator());
            var perm = new MenuItem { Header = "Snaps permanentes" };
            foreach (var (m, nome, ab) in ModosMenu.Append((ModoSnap.Grade, "Grade do plano", "")))
            {
                ModoSnap mm = m;
                perm.Items.Add(Item(nome, ab, () => Snap.Modos ^= mm, Snap.Modos.HasFlag(mm)));
            }
            cm.Items.Add(perm);
            var pol = new MenuItem { Header = "Ângulo polar" };
            foreach (double a in new[] { 90, 45, 30, 22.5, 15, 10, 5 })
            {
                double aa = a;
                pol.Items.Add(Item(a.ToString(CultureInfo.GetCultureInfo("pt-BR")) + "°", null, () => { Snap.PolarInc = aa; AplicarStatus(Snap.Osnap, false, true, Snap.Rastreio); }, Math.Abs(Snap.PolarInc - a) < 1e-9));
            }
            pol.Items.Add(new Separator());
            pol.Items.Add(Item("Rastrear em todos os ângulos polares", null, () => Snap.RastreioPolar = !Snap.RastreioPolar, Snap.RastreioPolar));
            cm.Items.Add(pol);
            cm.Items.Add(Item("Snap a objetos", "F3", () => AplicarStatus(!Snap.Osnap, Snap.Orto, Snap.Polar, Snap.Rastreio), Snap.Osnap));
            cm.Items.Add(Item("Ortogonal", "F8", () => AplicarStatus(Snap.Osnap, !Snap.Orto, Snap.Polar, Snap.Rastreio), Snap.Orto));
            cm.Items.Add(Item("Polar", "F10", () => AplicarStatus(Snap.Osnap, Snap.Orto, !Snap.Polar, Snap.Rastreio), Snap.Polar));
            cm.Items.Add(Item("Rastreamento", "F11", () => AplicarStatus(Snap.Osnap, Snap.Orto, Snap.Polar, !Snap.Rastreio), Snap.Rastreio));
            return cm;
        }

        // Ponto confirmado: gasta o snap one-off; no Medir, vai para o medidor. true = já tratado (Medir).
        bool PontoSnap(Point3D p)
        {
            Snap.ConsumirUnico();
            if (ferr != Ferramenta3D.Medir) return false;
            Medidor.Adicionar(new ItemMedida { P = p });
            return true;
        }

        // Clique no Medir: ponto de snap de objeto = ponto; sem ele, a barra sob o cursor = entidade (Inventor); senão o ponto do plano.
        void MedirClique(Point tela)
        {
            ResultadoSnap r = snapRes;
            bool pontoObjeto = r?.P != null && r.Tipo != TipoSnap.Plano && r.Tipo != TipoSnap.Grade && r.Tipo != TipoSnap.Nenhum && r.Tipo != TipoSnap.Polar
                               && r.Tipo != TipoSnap.Ortogonal && r.Tipo != TipoSnap.Rastreio;
            Barra3D b = null;
            if (!pontoObjeto)
            {
                b = Acertar(tela) as Barra3D;
                if (b == null)   // barra fina (vista analítica): a mais perto na tela, dentro da abertura
                {
                    double dm = Snap.Abertura;
                    foreach (Barra3D x in mod.Barras)
                    {
                        No3D na = NoPorId(x.Ni), nc = NoPorId(x.Nj); if (na == null || nc == null) continue;
                        double d = Snap3D.DistSeg(tela, Tela(P(na)), Tela(P(nc)), out _);
                        if (d < dm) { dm = d; b = x; }
                    }
                }
            }
            Snap.ConsumirUnico();
            if (b != null) { Medidor.Adicionar(new ItemMedida { P = r?.P ?? P(NoPorId(b.Ni)), Barra = Entidade(b) }); AtualizarCursor(); return; }
            if (r?.P == null) { Aviso = "O plano de trabalho está de perfil nesta vista: clique um ponto de snap ou uma barra."; sobre.InvalidateVisual(); return; }
            Aviso = null;
            Medidor.Adicionar(new ItemMedida { P = r.P.Value });
            AtualizarCursor();
        }

        EntMedida Entidade(Barra3D b)
        {
            Perfil3D p = null;
            try { p = Perfil?.Invoke(b.Secao ?? ""); } catch { }
            double? area = null, r = null;
            if (p != null && p.H > 0)
            {
                if (p.Forma == "TC" || p.Forma == "CIRC")
                {
                    r = p.H / 2; double ri = p.Forma == "TC" && p.Tw > 0 ? Math.Max(0, r.Value - p.Tw) : 0;
                    area = Math.PI * (r.Value * r.Value - ri * ri);
                }
                else area = Math.Abs(CriarForma(p, p.H).Lacos.Sum(l => Shoelace(l.P)));
            }
            return new EntMedida { A = P(NoPorId(b.Ni)), B = P(NoPorId(b.Nj)), Nome = Chave(b), Secao = b.Secao ?? "", AreaSecao = area, Raio = r, Dono = b };
        }
        static double Shoelace(Point[] q) { double s = 0; for (int i = 0; i < q.Length; i++) { Point a = q[i], c = q[(i + 1) % q.Length]; s += a.X * c.Y - c.X * a.Y; } return s / 2; }

        string EstadoSnap() => (Snap.Osnap ? " · OSNAP" : "") + (Snap.Orto ? " · ORTO" : "") + (Snap.Polar ? " · POLAR " + Snap.PolarInc.ToString(CultureInfo.GetCultureInfo("pt-BR")) + "°" : "")
                               + (Snap.Rastreio ? " · RASTREIO" : "") + (Snap.Unico != null ? " · único: " + (Snap.Unico == ModoSnap.Desligar ? "nenhum" : ModosMenu.FirstOrDefault(x => x.M == Snap.Unico).Nome) : "");
        string EtiquetaMedir() => "MEDIR (" + Medidor3D.NomeModo(Medidor.Modo) + ") — " + Medidor.Prompt + EstadoSnap();

        // Sobreposição: medidas, caminhos de rastreio, pontos adquiridos e tooltip. O marcador é o MarcaSnap (com fade).
        void DesenharSnap(DrawingContext dc)
        {
            var area = new Size(ActualWidth, ActualHeight);
            if (ferr == Ferramenta3D.Medir) DesenharMedida(dc);
            if (!Desenhando || !mouseDentro) return;
            if (snapRes != null && !DinTravado)
                foreach (GuiaSnap gs in snapRes.Guias) SnapOverlay.Guia(dc, gs, Tela, area);
            foreach (var (p, _) in Snap.Adquiridos) SnapOverlay.Adquirido(dc, Tela(p));
            if (snapRes?.P != null && !DinTravado && coord == "")
                SnapOverlay.Dica(dc, snapRes.Dica, Tela(snapRes.P.Value), area, inicio != null);
            if (BaseSnap != null)   // barra adquirida para o paralelo: // no meio dela (AutoCAD)
                foreach (SegSnap s in Snap.Paralelas) SnapOverlay.Marca(dc, TipoSnap.Paralelo, Tela(s.A + (s.B - s.A) / 2));
        }

        void DesenharMedida(DrawingContext dc)
        {
            ResultadoMedida r = Medidor.Res;
            if (r != null)
            {
                foreach (var (a, b, c) in r.Destaques) SnapOverlay.Destaque(dc, a, b, c, Tela);
                SnapOverlay.Poligono(dc, r.Poligono, Tela);
                foreach (CotaMedida k in r.Cotas.Where(x => x.Fina)) SnapOverlay.Cota(dc, k, Tela);
                foreach (CotaMedida k in r.Cotas.Where(x => !x.Fina)) SnapOverlay.Cota(dc, k, Tela);
                foreach (ArcoMedida k in r.Arcos) SnapOverlay.Arco(dc, k, Tela);
            }
            int i = 0;
            foreach (ItemMedida it in Medidor.Itens) if (it.Barra == null) SnapOverlay.PontoMedido(dc, Tela(it.P), i++ == 0 ? 0 : 1);
            // elástico ao vivo do último ponto até o cursor (medida rápida, sem clicar)
            Point3D? b0 = Medidor.Base, pf = PontoFinal();
            bool andamento = r == null || !r.Completo || (Medidor.Modo == ModoMedir.Cadeia || Medidor.Modo == ModoMedir.Area) && !Medidor.Fechada;
            if (andamento && b0 != null && pf != null && mouseDentro && (pf.Value - b0.Value).Length > 1e-9)
                SnapOverlay.Cota(dc, new CotaMedida { A = b0.Value, B = pf.Value, Texto = Medidas.M((pf.Value - b0.Value).Length), Tracejada = true, Cor = 1 }, Tela);
        }
    }

    public partial class MainWindow
    {
        // Letras digitadas na Vista3D: para a linha de comando (Enter executa; END/MID... viram snap único).
        internal bool DigitarComando3D(string t)
        {
            if (Comando == null) return false;
            Comando.Focus(); Comando.Text += t; Comando.CaretIndex = Comando.Text.Length;
            return true;
        }

        // Linha de comando no modelo espacial: ME/MEDIR abre o Medir 3D; END/MID/INT/PER/NEA... = snap único (one-off).
        internal bool Comando3D(string t)
        {
            if (Esp == null || vista3D == null || !vista3D.IsVisible) return false;
            string u = (t ?? "").Trim().ToUpperInvariant();
            if (u == "ME" || u == "MEDIR" || u == "MED" || u == "MEA" || u == "DI" || u == "DIST") { vista3D.Ferramenta = Ferramenta3D.Medir; vista3D.Focus(); return true; }
            ModoSnap? m = Snap3D.Palavra(u);
            if (m != null && vista3D.Desenhando)
            {
                vista3D.Snap.Unico = m; vista3D.Aviso = "Snap único: " + u + " (vale um clique)."; vista3D.MoverTela(Mouse.GetPosition(vista3D)); vista3D.Focus();
                return true;
            }
            return false;
        }

        // Rodapé (Orto, Polar, Objetos, Rastreio) = um estado só para o 2D e o 3D: espelha o snap da Vista3D.
        internal void SincronizarStatus3D()
        {
            if (vista3D == null || stObj == null) return;
            Snap3D s = vista3D.Snap;
            stObj.IsChecked = s.Osnap; stOrto.IsChecked = s.Orto; stPolar.IsChecked = s.Polar; stRastro.IsChecked = s.Rastreio;
            Vista.AtrairObjetos = s.Osnap; Vista.Orto = s.Orto; Vista.Polar = s.Polar; Vista.Rastrear = s.Rastreio;
        }

        // Clique num botão do rodapé (Orto, Polar, Objetos, Rastreio): o mesmo caminho das teclas F8/F10/F3/F11 (Snap3D via
        // AplicarStatus, que espelha de volta no rodapé). Sem vista 3D ainda, só o 2D guarda o estado.
        internal void StatusSnap3D()
        {
            if (stObj == null || stOrto == null || stPolar == null || stRastro == null) return;
            if (vista3D != null) vista3D.AplicarStatus(stObj.IsChecked == true, stOrto.IsChecked == true, stPolar.IsChecked == true, stRastro.IsChecked == true);
            else { Vista.AtrairObjetos = stObj.IsChecked == true; Vista.Orto = stOrto.IsChecked == true; Vista.Polar = stPolar.IsChecked == true; Vista.Rastrear = stRastro.IsChecked == true; }
        }

        // Ctrl+M: Medir 3D no modelo espacial (no 2D continua o Medir do desenho).
        internal bool Medir3D()
        {
            if (Esp == null || vista3D == null) return false;
            UsarFerramenta3D(Ferramenta3D.Medir);
            return true;
        }

        // Botão Medir em Modelo › Desenhar 3D e na aba Vista; "Medir 3D" na paleta (Ctrl+K).
        internal void MontarMedir3D()
        {
            const string dica = "Mede como no AutoCAD e no Inventor: ponto + ponto = distância (ΔX, ΔY, ΔZ); 1 barra = comprimento e seção; barra + barra = distância mínima e ângulo; 3 pontos = ângulo. Submodos no painel: Distância, Ângulo, Área, Raio, Cadeia (+/−). Usa os snaps.";
            var b = new F.ToggleButton { Header = "Medir", LargeIcon = Icone("medir", 32), Icon = Icone("medir", 16), Size = F.RibbonControlSize.Large, ToolTip = Dica("Medir 3D", dica, "Ctrl+M / ME") };
            Tamanho(b, false);
            b.Click += (s, e) => UsarFerramenta3D(Ferramenta3D.Medir);
            btnFerr3D[Ferramenta3D.Medir] = b; grpDesenho3D.Items.Add(b);
            Reg("medir", "Medir 3D", "Ctrl+M / ME", dica, () => UsarFerramenta3D(Ferramenta3D.Medir));
            var abaVista = Faixa.Tabs.FirstOrDefault(t => (t.Header as string) == "Vista");
            var nav = abaVista?.Groups.FirstOrDefault(g => (g.Header as string) == "Navegar");
            if (nav != null)
            {
                var bv = new F.Button { Header = "Medir", LargeIcon = Icone("medir", 32), Icon = Icone("medir", 16), Size = F.RibbonControlSize.Middle, ToolTip = Dica("Medir", dica, "Ctrl+M") };
                Tamanho(bv, true);
                bv.Click += (s, e) => Medir3D();
                nav.Items.Add(bv);
            }
        }
    }
}
