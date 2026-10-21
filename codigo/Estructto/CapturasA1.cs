// 54) Produto 3D único (docs/MODELO-ESPACIAL.md, "Documento único 3D"): abre o guincho antigo (.estructto XML, convertido),
// orbita pela simulação do Shift + arrastar, vista de topo, desenha uma barra pela linha de comando ("L" e coordenadas
// digitadas), estica a ponta pelo grip, cria a grade estrutural, sobe o plano com PgUp, ViewCube, perspectiva, F5,
// salva o contêiner .estructto e reabre. Confere cada passo (54-a1.txt). Só elas: ESTRUCTTO_CAPTURAS=a1.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace NovoFtool
{
    public static partial class Capturas
    {
        static void CapturasA1(MainWindow w, string guincho)
        {
            var log = new StringBuilder();
            void Ok(bool c, string m) { log.AppendLine((c ? "ok    " : "FALHA ") + m); if (!c) { File.WriteAllText(Path.Combine(pastaSaida, "54-a1.txt"), log.ToString(), new UTF8Encoding(false)); throw new InvalidOperationException("54-a1: " + m); } }
            void Ate(Func<bool> cond, string oque) { var lim = DateTime.Now.AddSeconds(60); while ((!cond() || w.OcupadoEsp) && DateTime.Now < lim) { EsperarIdle(); System.Threading.Thread.Sleep(20); } Ok(cond(), oque); }
            int Nb() => w.EspacialAtivo.Json["barras"].AsObject().Count;
            int Nn() => w.EspacialAtivo.Json["nos"].AsObject().Count;

            w.FecharEspaciaisSemPerguntar();
            Esperar(w.AbrirArquivo(guincho));
            var d = w.EspacialAtivo;
            Ok(d != null && d.Origem != null, "guincho .estructto (XML 2D) abriu como documento 3D convertido");
            Ok(Nn() == 4 && Nb() == 3 && d.Placas.Count > 0, "conversão: 4 nós, 3 barras, " + d.Placas.Count + " placa(s) de base trazida(s)");
            var v = w.vista3D;
            v.MostrarVista(VistaPadrao.Frente, false);
            CapturarPrincipal(w, "54-a1-convertido");

            // órbita transparente: Shift + arrastar com a Barra ligada não cancela o comando
            w.ExecutarComando("L");
            Ok(v.Ferramenta == Ferramenta3D.Barra, "L liga a Barra");
            var c = new Point(v.ActualWidth / 2, v.ActualHeight / 2);
            v.SimularOrbita(c, c + new Vector(170, 60));
            Ok(v.Ferramenta == Ferramenta3D.Barra && v.NomeVista == "Órbita", "Shift + arrastar orbitou e a Barra continua ligada");
            CapturarPrincipal(w, "54-a1-orbita");

            // vista de topo e barra pela linha de comando: 1º ponto absoluto, 2º relativo
            w.ExecutarComando("TOPO");
            Ok(v.NomeVista == "Topo", "TOPO: câmera em planta");
            w.ExecutarComando("L");
            w.ExecutarComando("0,0,5");
            Ok(v.InicioCorrente is Point3D p0 && (p0 - new Point3D(0, 0, 5)).Length < 1e-9, "0,0,5 digitado: 1º ponto em (0; 0; 5)");
            w.ExecutarComando("@2,0,0");
            Ate(() => Nb() == 4, "@2,0,0: barra criada pela linha de comando (4 barras)");
            string nova = d.Json["barras"].AsObject().Last().Key;
            Point3D X(string id) { var n = w.EspacialAtivo.Json["nos"][id]; return new Point3D((double)n["x"], (double)n["y"], (double)n["z"]); }
            Point3D pa = X((string)d.Json["barras"][nova]["ni"]), pz = X((string)d.Json["barras"][nova]["nj"]);
            Ate(() => v.PontasBarra(nova) != null, "a vista mostra a barra nova");
            Ok(Math.Abs((pz - pa).Length - 2) < 1e-6 && Math.Abs(pa.Z - 5) < 1e-9, "barra " + nova + " de " + pa + " a " + pz + " (2,000 m esperado)");
            v.MoverTela(v.Tela(new Point3D(3.5, 1.2, 0)));
            CapturarPrincipal(w, "54-a1-linha-de-comando");
            v.Tecla(Key.Escape); v.Tecla(Key.Escape);
            Ok(v.Ferramenta == Ferramenta3D.Selecionar && v.InicioCorrente == null, "Esc em cascata: corrente e depois a ferramenta");

            // grip: escolhe a barra nova e estica a ponta solta 1 m para cima
            string nj = (string)d.Json["barras"][nova]["nj"];
            v.Selecionar(null, new[] { nova });
            Ok(v.EsquentarGrip("no", nj), "grip da ponta " + nj + " ficou quente");
            CapturarPrincipal(w, "54-a1-grip");
            v.EntrarTexto("@0,0,1");
            Ate(() => Math.Abs((double)w.EspacialAtivo.Json["nos"][nj]["z"] - 6) < 1e-6, "grip: @0,0,1 levou o nó " + nj + " a z = 6");
            v.LimparSelecao();

            // grade estrutural e PgUp: o plano sobe para o nível da grade
            Esperar(w.AplicarGrade(new[] { -2.5, 0, 2.5 }, new[] { -1.5, 1.5 }, new[] { 0.0, 2.5, 5 }));
            Ok(v.EixosX.Length == 3 && v.EixosY.Length == 2, "grade: 3 eixos e 2 filas na vista");
            Ok(w.EspacialAtivo.Json["pavimentos"].AsObject().Count == 2, "grade: níveis 2,5 e 5 viraram pavimentos (um lote)");
            w.ExecutarComando("NIVEL +");
            Ok(v.PlanoAtual.Normal == 'Z' && Math.Abs(v.PlanoAtual.Valor - 2.5) < 1e-9, "PgUp (NIVEL +): plano de trabalho em z = 2,5");
            w.ExecutarComando("ISO");
            CapturarPrincipal(w, "54-a1-grade");

            // ViewCube: canto superior frente-direita; perspectiva
            v.CuboClicar(0, 1, -1);
            Ok(v.NomeVista.Contains("Topo") && v.NomeVista.Contains("direita"), "ViewCube: canto topo/frente/direita (" + v.NomeVista + ")");
            v.Perspectiva = true;
            CapturarPrincipal(w, "54-a1-perspectiva");
            v.Perspectiva = false;

            // F5 no modelo editado
            Esperar(w.CalcularEspacial());
            Ok(w.EspacialAtivo.Calculado, "F5: calculado no motor");
            w.MostrarDiagrama(Diagrama.Verificacao, true);
            v.MostrarVista(VistaPadrao.Iso, false);
            CapturarPrincipal(w, "54-a1-f5");
            w.MostrarDiagrama(Diagrama.Nenhum);

            // salvar no contêiner novo e reabrir: placas, grade e modelo voltam
            string arq = Path.Combine(pastaSaida, "54-a1-guincho-3d.estructto");
            Ok(w.SalvarEspacialEm(arq), "salvo em " + Path.GetFileName(arq));
            var cont = JsonNode.Parse(File.ReadAllText(arq));
            Ok((string)cont["formato"] == "estructto" && (int)cont["versao"] == 3 && cont["modelo"]?["barras"] != null, "arquivo .estructto v3 (contêiner JSON)");
            w.FecharEspaciaisSemPerguntar();
            Esperar(w.AbrirArquivo(arq));
            Ok(w.EspacialAtivo != null && Nb() == 4 && w.EspacialAtivo.Placas.Count == d.Placas.Count && w.EspacialAtivo.Grade != null, "reaberto: 4 barras, placas e grade");
            w.FecharEspaciaisSemPerguntar();
            log.AppendLine("TUDO OK");
            File.WriteAllText(Path.Combine(pastaSaida, "54-a1.txt"), log.ToString(), new UTF8Encoding(false));
        }
    }
}
