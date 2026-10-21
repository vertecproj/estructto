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
            int Pavs() => (w.EspacialAtivo.Json["pavimentos"] as JsonObject)?.Count ?? 0;
            int pav0 = Pavs();
            Esperar(w.AplicarGrade(new[] { -2.5, 0, 2.5 }, new[] { -1.5, 1.5 }, new[] { 0.0, 2.5, 5 }));
            Ok(v.EixosX.Length == 3 && v.EixosY.Length == 2, "grade: 3 eixos e 2 filas na vista");
            var gl = GradeEstrutural.Ler(w.EspacialAtivo.Json["config"]?["grade"]);
            Ok(gl.X.Length == 3 && Math.Abs(gl.X[0] + 2.5) < 1e-9 && gl.NY[1] == "B", "grade em config.grade do motor (x0 = −2,5; filas A, B)");
            Ok(Pavs() == pav0 + 2, "grade: níveis 2,5 e 5 viraram pavimentos (um PUT)");
            Ok(v.NiveisEixos.Length >= 3, "grade desenhada em " + v.NiveisEixos.Length + " níveis");
            Esperar(w.OperarEspacial(dd => dd.Trocar(true), null));
            Ok(w.EspacialAtivo.Json["config"]?["grade"] == null && Pavs() == pav0 && v.EixosX.Length == 0, "Ctrl+Z desfaz a grade e os pavimentos dela (um passo do motor)");
            Esperar(w.OperarEspacial(dd => dd.Trocar(false), null));
            Ok(v.EixosX.Length == 3 && Pavs() == pav0 + 2, "Ctrl+Y refaz a grade");
            w.ExecutarComando("NIVEL +");
            Ok(v.PlanoAtual.Normal == 'Z' && Math.Abs(v.PlanoAtual.Valor - 2.5) < 1e-9, "PgUp (NIVEL +): plano de trabalho em z = 2,5");
            w.ExecutarComando("ISO");
            CapturarPrincipal(w, "54-a1-grade");
            v.MostrarVista(VistaPadrao.Topo, false); v.Enquadrar(); v.ZoomFator(0.45);
            CapturarPrincipal(w, "54-a1-grade-planta");
            w.ExecutarComando("ISO");

            // cardinal na barra (params do motor): desenhada com cardinal 8, Ctrl+Z leva barra e cardinal juntos
            int nb0 = Nb();
            Esperar(w.OperarEspacial(dd => dd.DesenharBarras(new[] { (new Point3D(-2.5, -1.5, 2.5), new Point3D(2.5, -1.5, 2.5)) }, null, false, null, 0.3, 8), "teste: barra com cardinal 8"));
            string bc = w.EspacialAtivo.Json["barras"].AsObject().Last().Key;
            var jb = w.EspacialAtivo.Json["barras"][bc];
            Ok(Nb() == nb0 + 1 && (int)jb["params"]["cardinal"] == 8 && w.EspacialAtivo.ParaVista().Barras.Last().Cardinal == 8, "cardinal 8 em params.cardinal da barra " + bc + " e na vista");
            Esperar(w.OperarEspacial(dd => dd.Trocar(true), null));
            Ok(Nb() == nb0, "Ctrl+Z tira a barra com o cardinal");
            Esperar(w.OperarEspacial(dd => dd.Trocar(false), null));
            // grip + Ctrl (cópia da ponta): a barra nova leva seção, material, giro e cardinal da origem
            var pj = v.PontasBarra(bc).Value.B;
            Esperar(v.AplicarGrip(new GripAcao { Tipo = "no", Id = (string)jb["nj"], Barra = bc, De = pj, Para = pj + new Vector3D(0, 3, 0), Copiar = true }));
            var jc = w.EspacialAtivo.Json["barras"].AsObject().Last().Value;
            jb = w.EspacialAtivo.Json["barras"][bc];
            Ok(Nb() == nb0 + 2 && (string)jc["secao"] == (string)jb["secao"] && (string)jc["material"] == (string)jb["material"] && Math.Abs((double)jc["rot"] - 0.3) < 1e-9 && DocumentoEspacial.CardinalDe(jc) == 8,
               "grip + Ctrl: cópia com seção, material, giro 0,3 rad e cardinal 8");
            Esperar(w.OperarEspacial(dd => dd.Trocar(true), null)); Esperar(w.OperarEspacial(dd => dd.Trocar(true), null));
            Ok(Nb() == nb0, "Ctrl+Z 2×: as duas barras soltas do teste saem (o F5 abaixo precisa do modelo sem mecanismo)");

            // rodapé: o botão Orto liga o ORTO do Snap3D (o mesmo das teclas F8)
            var stOrto = (System.Windows.Controls.Primitives.ToggleButton)typeof(MainWindow).GetField("stOrto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w);
            var clicar = typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bool orto0 = v.Snap.Orto;
            clicar.Invoke(stOrto, null);
            Ok(v.Snap.Orto == !orto0 && stOrto.IsChecked == v.Snap.Orto, "rodapé › Orto troca o ORTO do Snap3D");
            clicar.Invoke(stOrto, null);
            Ok(v.Snap.Orto == orto0, "rodapé › Orto de novo volta");

            // janela de análise espera a busca dentro da fila do motor (antes async void: a trava soltava antes do GET)
            Esperar(w.ComMotor(false, ComandosAnalise.AbrirModal));
            var jm = w.OwnedWindows.OfType<JanelaModal>().FirstOrDefault();
            Ok(jm != null, "Modos de vibração: a janela já está aberta quando ComMotor termina");
            jm?.Close();

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
            int nbSalvo = cont["modelo"]["barras"].AsObject().Count;
            Ok(w.EspacialAtivo != null && Nb() == nbSalvo && w.EspacialAtivo.Placas.Count == d.Placas.Count && w.EspacialAtivo.Json["config"]?["grade"] != null && cont["grade"] == null,
               "reaberto: " + nbSalvo + " barras, placas e grade (dentro do modelo, não ao lado)");
            Ok(!File.Exists(arq + ".tmp"), "salvar: temporário trocado pelo arquivo (sem .tmp)");

            // contêiner antigo: "grade" e "cardinal" ao lado do modelo migram; cardinal inválido vira aviso, não trava
            var velho = JsonNode.Parse(File.ReadAllText(arq)).AsObject();
            velho["modelo"]["config"].AsObject().Remove("grade");
            var bs = velho["modelo"]["barras"].AsObject().Select(kv => kv.Key).ToList();
            velho["grade"] = new JsonObject { ["x"] = new JsonArray(0.0, 6.0), ["y"] = new JsonArray(0.0, 5.0), ["z"] = new JsonArray(0.0) };
            velho["cardinal"] = new JsonObject { [bs[0]] = "abc", [bs[1]] = 7, ["nao-existe"] = 2, [bs[2]] = 99 };
            string arqVelho = Path.Combine(pastaSaida, "54-a1-conteiner-antigo.estructto");
            File.WriteAllText(arqVelho, velho.ToJsonString());
            w.FecharEspaciaisSemPerguntar();
            Esperar(w.AbrirArquivo(arqVelho));
            var dv = w.EspacialAtivo;
            Ok(dv != null && GradeEstrutural.Ler(dv.Json["config"]?["grade"]).X.Length == 2 && DocumentoEspacial.CardinalDe(dv.Json["barras"][bs[1]]) == 7
               && dv.Avisos.Count(a => a.Contains("cardinal")) == 3, "contêiner antigo: grade e cardinal 7 migrados; 3 cardinais inválidos viraram aviso");

            // recuperação: uma cópia por documento; a cópia só sai depois de abrir; salvar apaga só a do documento salvo
            Config.SalvarRecuperacao("captura-ruim", "{ isto não é json", null);
            Ok(!Esperar(w.OferecerRecuperacao("captura-ruim", "{ isto não é json", null, false)) && Config.TemRecuperacao("captura-ruim"), "cópia ilegível: não abriu e continua guardada");
            Config.ApagarRecuperacao("captura-ruim");
            Config.SalvarRecuperacao("captura-boa", File.ReadAllText(arq), arq);
            Ok(Esperar(w.OferecerRecuperacao("captura-boa", File.ReadAllText(arq), arq, false)), "cópia boa: documento recuperado");
            var dr = w.EspacialAtivo;
            Ok(!Config.TemRecuperacao("captura-boa") && Config.TemRecuperacao(dr.Chave), "a cópia antiga saiu só depois de abrir; o recuperado já tem a dele");
            w.TrocarParaDoc(dv); w.alterado = true; w.SalvarRecuperacoes();
            Ok(Config.TemRecuperacao(dv.Chave) && Config.TemRecuperacao(dr.Chave) && dv.Chave != dr.Chave, "salvamento automático: uma cópia por aba alterada");
            Ok(w.SalvarEspacialEm(arqVelho), "salva a aba do contêiner antigo");
            Ok(!Config.TemRecuperacao(dv.Chave) && Config.TemRecuperacao(dr.Chave), "salvar apaga só a cópia do documento salvo (a da outra aba fica)");
            Config.ApagarRecuperacao(dr.Chave);
            w.FecharEspaciaisSemPerguntar();
            log.AppendLine("TUDO OK");
            File.WriteAllText(Path.Combine(pastaSaida, "54-a1.txt"), log.ToString(), new UTF8Encoding(false));
        }
    }
}
