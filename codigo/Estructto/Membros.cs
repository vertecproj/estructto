// Barra continua (membro): varios trechos entre nos que sao a mesma peca na obra. Todos os trechos tem o mesmo
// perfil e material; o dimensionamento escolhe um perfil so para o membro inteiro.
using System.Collections.Generic;
using System.Linq;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        // Une as barras selecionadas numa barra continua. Se alguma ja for trecho de uma barra continua, as outras
        // entram nela ("adicionar trecho"); se forem de varias, tudo vira uma so.
        void UnirMembro()
        {
            var sel = Vista.Sel.OfType<Barra>().ToList();
            if (sel.Count == 0) { Log("Selecione os trechos (barras) que formam a barra contínua e use Unir."); return; }
            var existentes = sel.Where(b => b.Membro != 0).Select(b => b.Membro).Distinct().OrderBy(x => x).ToList();
            var todos = new HashSet<Barra>(sel);
            foreach (int mm in existentes) foreach (Barra b in M.Trechos(mm)) todos.Add(b);
            if (todos.Count < 2) { Log("Selecione pelo menos 2 trechos (ou 1 trecho novo + um trecho da barra contínua)."); return; }
            if (!Encadeados(todos.ToList())) { Log("⚠ Os trechos não formam uma linha contínua (cada nó interno deve ligar só 2 trechos). Nada foi unido."); return; }
            int alvo = existentes.Count > 0 ? existentes[0] : M.ProxMembro();
            Barra modelo = sel.FirstOrDefault(b => b.Membro == alvo) ?? sel[0];
            foreach (Barra b in todos) { b.Membro = alvo; b.Secao = modelo.Secao; b.Material = modelo.Material; b.EixoFraco = modelo.EixoFraco; }
            M.ArrumarMembros();
            int num = todos.First().Membro;
            Confirmar("Barra contínua B" + num + ": " + todos.Count + " trechos, " + Num.Fmt(M.CompMembro(num), 2) + " m, perfil " + modelo.Secao + ".");
        }

        void SepararMembro()
        {
            var mms = Vista.Sel.OfType<Barra>().Where(b => b.Membro != 0).Select(b => b.Membro).Distinct().ToList();
            if (mms.Count == 0) { Log("Selecione um trecho de barra contínua para separar."); return; }
            foreach (Barra b in M.Barras) if (mms.Contains(b.Membro)) b.Membro = 0;
            M.ArrumarMembros();
            Confirmar(Pl.N(mms.Count, "barra contínua separada", "barras contínuas separadas") + " em trechos independentes.");
        }

        // tira trechos selecionados da barra continua (o resto continua unido)
        void TirarTrecho()
        {
            var bs = Vista.Sel.OfType<Barra>().Where(b => b.Membro != 0).ToList();
            if (bs.Count == 0) { Log("Selecione os trechos a tirar da barra contínua."); return; }
            foreach (Barra b in bs) b.Membro = 0;
            M.ArrumarMembros();
            Confirmar(Pl.N(bs.Count, "trecho tirado", "trechos tirados") + " da barra contínua.");
        }

        void SelecionarMembro()
        {
            var mms = Vista.Sel.OfType<Barra>().Where(b => b.Membro != 0).Select(b => b.Membro).Distinct().ToList();
            if (mms.Count == 0) { Log("Selecione um trecho de barra contínua."); return; }
            Vista.Selecionar(M.Barras.Where(b => mms.Contains(b.Membro)).Cast<object>());
        }

        // os trechos formam um caminho simples (sem ramificacao): nenhum no e ponta de mais de 2 trechos, e e conexo
        static bool Encadeados(List<Barra> bs)
        {
            var grau = new Dictionary<int, int>();
            foreach (Barra b in bs) { grau[b.N1] = grau.TryGetValue(b.N1, out int g1) ? g1 + 1 : 1; grau[b.N2] = grau.TryGetValue(b.N2, out int g2) ? g2 + 1 : 1; }
            if (grau.Values.Any(g => g > 2)) return false;
            var vistos = new HashSet<Barra> { bs[0] }; var fila = new Queue<Barra>(); fila.Enqueue(bs[0]);
            while (fila.Count > 0)
            {
                Barra b = fila.Dequeue();
                foreach (Barra o in bs) if (!vistos.Contains(o) && (o.N1 == b.N1 || o.N1 == b.N2 || o.N2 == b.N1 || o.N2 == b.N2)) { vistos.Add(o); fila.Enqueue(o); }
            }
            return vistos.Count == bs.Count;
        }

        // perfil, material e eixo de um trecho valem para a barra continua inteira
        void SincronizarMembros(IEnumerable<Barra> mudadas)
        {
            foreach (Barra b in mudadas.Where(x => x.Membro != 0).ToList())
                foreach (Barra o in M.Trechos(b.Membro)) { o.Secao = b.Secao; o.Material = b.Material; o.EixoFraco = b.EixoFraco; }
        }
    }
}
