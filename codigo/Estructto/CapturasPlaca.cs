// 55) Placa de base pelo motor (método NBR, docs/v3/PLACA-BASE-V2.md §10): o guincho antigo convertido para 3D (a placa
// migra para o grupo de ligações; antes = legado Nucleo.cs, depois = motor) e um pórtico com arrancamento na base
// (exemplos/base-com-arrancamento.json: bloco de compressão e chumbadores tracionados). Números em 55-placa.txt.
// Só elas: ESTRUCTTO_CAPTURAS=placa (com ESTRUCTTO_MOTOR).
using System;
using System.IO;
using System.Linq;
using System.Text;
using Portico;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasPlaca(MainWindow w, string guincho)
        {
            var log = new StringBuilder();
            string F(double v, int c = 2) => Num.Fmt(v, c);

            void Placa(string pre, string no)
            {
                w.placaAberta = null;
                Esperar(w.AbrirPlacaEsp(no));
                var jp = w.placaAberta ?? throw new InvalidOperationException("placa do nó " + no + " não abriu (ver o log)");
                PrepararDialogoForaDaTela(jp);
                jp.Show();
                EsperarIdle();
                foreach (var (aba, nome) in new[] { (0, "planta"), (2, "forcas"), (4, "calculo"), (3, "ficha"), (1, "3d") })
                    RenderizarJanela(jp, 1600, 900, pre + "-" + nome + ".png", () => { jp.MostrarLegado(false); jp.MostrarAba(aba); });
                RenderizarJanela(jp, 1600, 900, pre + "-legado.png", () => { jp.MostrarAba(0); jp.MostrarLegado(true); });
                var r = jp.ResultadoMotor; var l = jp.ResultadoLegado;
                log.AppendLine("== " + pre + " (nó " + no + ") ==");
                if (!r.Valido) log.AppendLine("MOTOR: " + r.Erro);
                else
                {
                    log.AppendLine("motor (NBR): governa " + r.Comb + " · " + r.Barra + " · eixo " + r.Eixo + " · N = " + F(r.N) + " V = " + F(r.V) + " M = " + F(r.M) + " · " + r.Modelo + " Y = " + F(r.Y, 1) + " mm");
                    log.AppendLine("  Tmax = " + F(r.Tmax) + " kN (" + r.Paraf.Count(p => p[2] > 1e-9) + " tracionados) · Fv = " + F(r.Fv) + " kN (" + r.Mecanismo + ", n_ef = " + r.NEf + ")");
                    log.AppendLine("  chapa CBCA = " + F(r.TNec, 1) + " mm · Pfeil = " + F(r.TPfeil, 1) + " mm (" + r.Governa + ") · σc,Rd = " + F(r.SigmaRd) + " MPa");
                    log.AppendLine("  taxas: fixação " + F(r.Fix.Taxa, 3) + " (" + r.Fix.Criterio + ") · chapa " + F(r.Chapa.Taxa, 3) + " · solda " + F(r.Solda.Taxa, 3) + " · ok = " + r.Ok + " · casos = " + r.NCombs);
                }
                if (l != null && l.TemEsforcos)
                    log.AppendLine("legado (Nucleo.cs, " + (jp.CombLegado ?? "—") + "): N = " + F(l.N) + " V = " + F(l.V) + " M = " + F(l.M) + " · Tmax = " + F(l.Tmax) + " kN · Fv = " + F(l.Fv) +
                                   " kN · NComb = " + F(l.NComb) + " (exigido " + F(l.Ch != null ? l.NRef : l.NExig) + ") · chapa = " + F(l.EspNec, 1) + " mm · " + l.Modelo);
                jp.Close();
                EsperarIdle();
            }

            try
            {
                Esperar(w.CalcularEspacial());
                var d = w.EspacialAtivo;
                string no = d.Placas.Keys.FirstOrDefault() ?? MainWindow.NosComPlaca(d).FirstOrDefault() ?? throw new InvalidOperationException("guincho sem placa de base");
                Placa("55-placa-guincho", no);
                log.AppendLine("migração: d.Placas = " + d.Placas.Count + " · grupos com placa = " + MainWindow.GruposComPlaca(d).Count());

                string arr = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(guincho), "base-com-arrancamento.json"));
                w.FecharEspaciaisSemPerguntar();
                Esperar(w.AbrirArquivo(arr));
                Esperar(w.CalcularEspacial());
                Placa("55-placa-arrancamento", "N0-0-0");
                // a mesma base dimensionada pelo motor (Dimensionar tudo)
                w.placaAberta = null;
                Esperar(w.AbrirPlacaEsp("N0-0-0"));
                var jd = w.placaAberta;
                PrepararDialogoForaDaTela(jd); jd.Show(); EsperarIdle();
                jd.DimensionarTudo();
                RenderizarJanela(jd, 1600, 900, "55-placa-arrancamento-dimensionada.png", () => jd.MostrarAba(0));
                RenderizarJanela(jd, 1600, 900, "55-placa-arrancamento-dimensionada-forcas.png", () => jd.MostrarAba(2));
                var rd = jd.ResultadoMotor;
                log.AppendLine("dimensionada: " + jd.Mensagem + " → taxa " + F(rd.Taxa, 3) + " · ok = " + rd.Ok + " (fixação " + F(rd.Fix.Taxa, 3) + ", chapa " + F(rd.Chapa.Taxa, 3) + ", solda " + F(rd.Solda.Taxa, 3) + " " + rd.Solda.Criterio + ")");
                jd.Close(); EsperarIdle();
            }
            finally { File.WriteAllText(Path.Combine(pastaSaida, "55-placa.txt"), log.ToString(), new UTF8Encoding(false)); }
        }
    }
}
