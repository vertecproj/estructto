// Vista3D: grips da seleção e pedidos de ponto/objeto dos comandos (docs/v3/PESQUISA-PLANT.md §3.7 e §5).
// Grips (AutoCAD): □ nas pontas das barras e nos nós escolhidos (estica: move o nó, as barras ligadas vão junto) e □ no meio
// da barra (move a barra). Clique no grip = quente (vermelho); o próximo clique (com atração, ORTO, entrada dinâmica ou x,y,z
// digitado) é o destino. Ctrl no destino = copia em vez de mover (ponta: barra nova da outra ponta até o destino; meio: cópia
// da barra; nó: nó novo). Esc esfria. Quem aplica no modelo é AplicarGrip (MainWindow, Comandos3D.cs).
// Pedidos: PedirPonto (com atração, elástico a partir da base) e PedirObjeto (clique escolhe nó/barra) para os comandos da
// linha de comando (M, CO, MI, RO, TR, EX, medir…). Esc cancela; botão direito/Enter encerra.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    // Grip solto no destino: Tipo "no" (Id = nó; Barra = barra dona da ponta, se veio de uma barra) ou "meio" (Barra = barra).
    public sealed class GripAcao
    {
        public string Tipo, Id, Barra; public Point3D De, Para; public bool Copiar;
    }

    public partial class Vista3D
    {
        public Func<GripAcao, Task> AplicarGrip;   // null = sem grips (vista só de leitura)
        public string Prompt;                       // etiqueta do comando em curso (null = a da ferramenta)

        sealed class Grip
        {
            public string Tipo, Id, Barra; public Point3D P;
            public override bool Equals(object o) => o is Grip g && g.Tipo == Tipo && g.Id == Id && g.Barra == Barra;
            public override int GetHashCode() => (Tipo + Id + Barra).GetHashCode();
        }

        Grip gripQuente, gripHover;
        public bool GripQuente => gripQuente != null;

        // Grips da seleção atual (sem ferramenta de desenho).
        List<Grip> Grips()
        {
            var r = new List<Grip>();
            if (AplicarGrip == null || Selecao.Count == 0 || ferr == Ferramenta3D.Barra || ferr == Ferramenta3D.Pilar || Selecao.Count > 400) return r;   // ponytail: sem grips em seleções enormes
            var vistos = new HashSet<string>();
            foreach (Barra3D b in mod.Barras)
            {
                if (!Selecao.Barras.Contains(Chave(b))) continue;
                No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj); if (a == null || c == null) continue;
                foreach (No3D n in new[] { a, c })
                    if (vistos.Add(Chave(n))) r.Add(new Grip { Tipo = "no", Id = Chave(n), Barra = Chave(b), P = P(n) });
                r.Add(new Grip { Tipo = "meio", Barra = Chave(b), P = P(a) + (P(c) - P(a)) / 2 });
            }
            foreach (No3D n in mod.Nos)
                if (Selecao.Nos.Contains(Chave(n)) && vistos.Add(Chave(n))) r.Add(new Grip { Tipo = "no", Id = Chave(n), P = P(n) });
            return r;
        }

        Grip GripSob(Point tela)
        {
            Grip melhor = null; double dm = 7;
            foreach (var g in Grips()) { double d = (Tela(g.P) - tela).Length; if (d < dm) { dm = d; melhor = g; } }
            return melhor;
        }

        void Esquentar(Grip g)
        {
            gripQuente = g; gripHover = null;
            inicio = g.P; DinLimpar(); coord = "";
            Prompt = (g.Tipo == "meio" ? "MOVER BARRA " + g.Barra : "ESTICAR " + (g.Barra != null ? "ponta da barra " + g.Barra : "nó " + g.Id))
                     + " — clique o destino (Ctrl copia) · digite x,y,z ou @dx,dy,dz · Esc cancela";
            Cursor = Cursors.Cross;
            AtualizarCursor();
        }

        void Esfriar()
        {
            gripQuente = null; inicio = null; Prompt = null; DinLimpar(); coord = "";
            Cursor = CursorFerramenta;
            AtualizarCursor();
        }

        async Task SoltarGrip(Point3D p)
        {
            var g = gripQuente;
            bool copiar = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            Esfriar();
            if ((p - g.P).Length < 1e-9 || AplicarGrip == null) return;
            await AplicarGrip(new GripAcao { Tipo = g.Tipo, Id = g.Id, Barra = g.Barra, De = g.P, Para = p, Copiar = copiar });
        }

        static readonly Pen PGrip = Caneta(new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x12)), 1);

        // □ 9 px sempre na frente (sem profundidade): frio azul, sob o mouse vermelho claro, quente vermelho.
        void DesenharGrips(DrawingContext dc)
        {
            if (Desenhando && gripQuente == null) return;
            foreach (var g in Grips())
            {
                Point s = Tela(g.P);
                Color c = g.Equals(gripQuente) ? TemaPlant.CorGripQuente : g.Equals(gripHover) ? TemaPlant.CorGripHover : TemaPlant.CorGripFrio;
                double r = g.Tipo == "meio" ? 3.5 : 4.5;
                dc.DrawRectangle(TemaPlant.B(c), PGrip, new Rect(s.X - r, s.Y - r, 2 * r, 2 * r));
            }
        }

        // ---------- eixos da grade estrutural (GR) ----------

        static readonly Pen PEixo = Caneta(new SolidColorBrush(Color.FromArgb(0x90, 0x7C, 0x8B, 0x9E)), 1, new double[] { 10, 3, 2, 3 });
        static readonly Pen PBalao = Caneta(new SolidColorBrush(Color.FromArgb(0xC0, 0x94, 0xA3, 0xB8)), 1);

        // Linhas traço-ponto no nível do plano de trabalho (horizontal) ou no térreo, com balões de nome fora da grade.
        void DesenharEixos(DrawingContext dc)
        {
            if (EixosX.Length == 0 && EixosY.Length == 0) return;
            double z = plano.Normal == 'Z' ? plano.Valor : 0;
            double x0 = EixosX.DefaultIfEmpty(0).Min(), x1 = EixosX.DefaultIfEmpty(0).Max(), y0 = EixosY.DefaultIfEmpty(0).Min(), y1 = EixosY.DefaultIfEmpty(0).Max();
            const double sobra = 1.5;
            void Linha(Point3D a, Point3D b, string nome)
            {
                Point pa = Tela(a), pb = Tela(b);
                dc.DrawLine(PEixo, pa, pb);
                if (string.IsNullOrEmpty(nome)) return;
                Vector u = pa - pb; if (u.Length < 1) return; u.Normalize();
                Point c = pa + u * 11;
                dc.DrawEllipse(TemaPlant.BCaixaHud, PBalao, c, 10, 10);
                var ft = new FormattedText(nome, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TfEntrada, 10.5, TemaPlant.BTexto1, 1.0);
                dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
            }
            for (int i = 0; i < EixosX.Length; i++) Linha(new Point3D(EixosX[i], y0 - sobra, z), new Point3D(EixosX[i], y1 + sobra, z), i < NomesX.Length ? NomesX[i] : null);
            for (int j = 0; j < EixosY.Length; j++) Linha(new Point3D(x0 - sobra, EixosY[j], z), new Point3D(x1 + sobra, EixosY[j], z), j < NomesY.Length ? NomesY[j] : null);
        }

        // ---------- pedidos dos comandos ----------

        Func<Point3D, Task<bool>> pedePonto;                         // true = continua pedindo (o ponto vira a base do próximo)
        Func<object, Point3D?, ModifierKeys, Task<bool>> pedeObjeto;   // true = continua pedindo
        Action aoCancelarPedido;
        public bool Pedindo => pedePonto != null || pedeObjeto != null;

        // Pede um ponto na vista (com atração e entrada dinâmica). base_ = 1º ponto do elástico (null = nenhum).
        public void PedirPonto(string prompt, Point3D? base_, Func<Point3D, Task<bool>> ao, Action cancelar = null)
        {
            LimparPedido(false);
            if (ferr == Ferramenta3D.Barra || ferr == Ferramenta3D.Pilar) Ferramenta = Ferramenta3D.Selecionar;
            pedePonto = ao; aoCancelarPedido = cancelar; Prompt = prompt; inicio = base_; DinLimpar(); coord = "";
            Cursor = Cursors.Cross;
            AtualizarCursor();
        }

        // Pede um objeto (nó ou barra) por clique; ao recebe (objeto ou null, ponto 3D clicado no plano, modificadores).
        public void PedirObjeto(string prompt, Func<object, Point3D?, ModifierKeys, Task<bool>> ao, Action cancelar = null)
        {
            LimparPedido(false);
            if (ferr == Ferramenta3D.Barra || ferr == Ferramenta3D.Pilar) Ferramenta = Ferramenta3D.Selecionar;
            pedeObjeto = ao; aoCancelarPedido = cancelar; Prompt = prompt;
            Cursor = Cursors.Hand;
            sobre.InvalidateVisual();
        }

        // Troca só a etiqueta do pedido em curso (o comando passou para o passo seguinte).
        public void MudarPrompt(string prompt) { Prompt = prompt; sobre.InvalidateVisual(); }

        public void LimparPedido(bool avisar = true)
        {
            var c = aoCancelarPedido;
            bool havia = Pedindo;
            pedePonto = null; pedeObjeto = null; aoCancelarPedido = null;
            if (havia) { Prompt = null; inicio = null; DinLimpar(); coord = ""; Cursor = CursorFerramenta; AtualizarCursor(); }
            if (havia && avisar) c?.Invoke();
        }

        async Task PontoPedido(Point3D p)
        {
            var f = pedePonto;
            bool mais = await f(p);
            if (pedePonto != f) return;   // o callback já trocou de pedido
            if (mais) { inicio = p; DinLimpar(); coord = ""; AtualizarCursor(); }
            else LimparPedido(false);
        }

        async Task EscolherPedido(Point tela, ModifierKeys mods)
        {
            var f = pedeObjeto;
            object o = Acertar(tela);
            bool mais = await f(o, NoPlano(tela), mods);
            if (pedeObjeto == f && !mais) LimparPedido(false);
        }

        // Objeto pelo código (capturas/testes): o mesmo caminho do clique.
        public Task EscolherPorCodigo(object o, ModifierKeys mods = ModifierKeys.None) => pedeObjeto?.Invoke(o, null, mods) ?? Task.CompletedTask;

        // Barra/nó do Modelo3D pelo id do motor (capturas/testes).
        public object ObjetoPorId(string tipo, string id) =>
            tipo == "no" ? (object)mod.Nos.FirstOrDefault(n => Chave(n) == id) : mod.Barras.FirstOrDefault(b => Chave(b) == id);

        // Ponto 3D de um nó pelo id do motor (null = não existe).
        public Point3D? PosicaoNo(string id) { var n = mod.Nos.FirstOrDefault(x => Chave(x) == id); return n == null ? (Point3D?)null : P(n); }

        // Pontas (ni, nj) de uma barra pelo id do motor.
        public (Point3D A, Point3D B)? PontasBarra(string id)
        {
            var b = mod.Barras.FirstOrDefault(x => Chave(x) == id); if (b == null) return null;
            No3D a = NoPorId(b.Ni), c = NoPorId(b.Nj);
            return a == null || c == null ? ((Point3D, Point3D)?)null : (P(a), P(c));
        }

        // Grip pelo código (capturas/testes): esquenta o grip da ponta/nó `id` ou do meio da barra `barra`.
        public bool EsquentarGrip(string tipo, string id)
        {
            var g = Grips().FirstOrDefault(x => x.Tipo == tipo && (tipo == "meio" ? x.Barra == id : x.Id == id));
            if (g == null) return false;
            Esquentar(g); return true;
        }
    }
}
