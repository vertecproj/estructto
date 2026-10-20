// Conversao do Modelo do Estructto (Nucleo.cs, portico plano XY) para o JSON do motor Python (ESPEC §5, 3D) e leitura
// dos resultados de volta. Ver docs/UNIFICACAO-MOTOR.md.
//
// Eixos: Estructto X -> motor X; Estructto Y -> motor Z (para cima); motor Y = 0. Os graus de liberdade fora do plano
// (uy, rx, rz) ficam travados em todos os nos. Giro do Estructto (anti-horario em XY) = -ry do motor.
// Ids: no N -> "nN", barra B -> "bB". Casos: o nome do caso do Estructto.
// Secao: montada pelo motor a partir da forma (para a verificacao NBR 8800 ter todas as propriedades) e depois A, Iy, Iz
// trocados pelos do catalogo do Estructto, para a analise usar exatamente a mesma rigidez do Nucleo.cs.
// Peso proprio: carga distribuida em -Z nos casos marcados (massa do catalogo ou densidade x area, igual ao Nucleo.cs).
// Barra com C < 0 (aponta para a esquerda) leva rot = pi: o eixo local z do motor fica igual ao y local do Estructto.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Portico;

namespace NovoFtool
{
    // Resultado de um caso ou combinacao, nas convencoes do Estructto.
    public class MotorEstado
    {
        public Dictionary<int, double[]> Nos = new Dictionary<int, double[]>();          // ux, uy, giro, Rx, Ry, Rm (m, rad, kN, kN.m)
        public Dictionary<int, MotorEsforcos> Barras = new Dictionary<int, MotorEsforcos>();
    }

    public class MotorEsforcos
    {
        public double[] X, N, V, M;   // x desde o no inicial (m); N tracao +; M tracao na fibra de baixo + (como ResBarra)
    }

    public class MotorVerif
    {
        public int Barra;
        public double? Taxa;
        public bool Ok;
        public string Norma, Criterio, Item, Combinacao;
    }

    public class VerifMotor : Verif { }   // verificacao do motor na tela: so taxa e criterio (sem as parcelas do Nucleo.cs)

    public class MotorResultado
    {
        public List<string> Combinacoes = new List<string>();
        public Dictionary<string, string> EstadoDe = new Dictionary<string, string>();   // nome -> ELU, ELS_FREQ...
        public Dictionary<string, MotorEstado> PorCombinacao = new Dictionary<string, MotorEstado>();
        public List<MotorVerif> Verificacao = new List<MotorVerif>();
        public List<string> Avisos = new List<string>();
        public JsonNode Resumo;
    }

    public static class MotorModelo
    {
        const double g = 9.80665;

        static string IdNo(int id) { return "n" + id; }
        static string IdBarra(int id) { return "b" + id; }
        static string Url(string s) { return Uri.EscapeDataString(s); }

        // Envia o modelo ao motor: PUT /api/modelo (nos, materiais, casos), lote (secoes, barras, cargas) e PATCH das secoes.
        public static async Task Enviar(Modelo m)
        {
            m.GarantirCasos();
            var nos = new JsonObject();
            foreach (No n in m.Nos)
                nos[IdNo(n.Id)] = new JsonObject
                {
                    ["x"] = n.X, ["y"] = 0.0, ["z"] = n.Y,
                    ["apoio"] = new JsonArray(n.ApoioX, true, n.ApoioY, true, n.ApoioGiro, true),
                    // mola de giro em ry (rigidez não tem sinal); recalque em mm -> m, só onde travado (igual ao Nucleo.cs)
                    ["molas"] = new JsonArray(n.MolaX, 0.0, n.MolaY, 0.0, n.MolaGiro, 0.0),
                    ["recalques"] = new JsonArray(n.ApoioX ? n.RecalqueX / 1000 : 0.0, 0.0, n.ApoioY ? n.RecalqueY / 1000 : 0.0, 0.0, 0.0, 0.0)
                };
            var materiais = new JsonObject();
            foreach (string nome in m.Barras.Select(b => b.Material).Distinct())
            {
                Material mt = Biblioteca.AcharMaterial(nome);
                if (mt == null) throw new MotorErro(0, "Material não reconhecido: \"" + nome + "\".");
                var j = new JsonObject { ["nome"] = nome, ["E"] = mt.E * 1000, ["G"] = mt.G * 1000, ["peso"] = mt.Densidade * g / 1000 };
                if (mt.Fy > 0) { j["fy"] = mt.Fy * 1000; j["fu"] = Fu(nome, mt.Fy) * 1000; }
                materiais[nome] = j;
            }
            // Modelo.MotorExtra (janelas Vento, Sismo, Pavimentos e Casos): config, pavimentos e tipo/subtipo dos casos voltam
            // ao motor; sem isso o PUT apagaria o que essas janelas gravaram.
            var extra = string.IsNullOrEmpty(m.MotorExtra) ? null : JsonNode.Parse(m.MotorExtra) as JsonObject;
            var casosExtra = extra?["casos"] as JsonObject ?? new JsonObject();
            var casos = new JsonObject();
            foreach (CasoCarga c in m.Casos)
            {
                var e = casosExtra[c.Nome];
                casos[c.Nome] = new JsonObject { ["nome"] = c.Nome, ["tipo"] = (string)e?["tipo"] ?? c.Tipo ?? "G", ["subtipo"] = (string)e?["subtipo"] ?? "" };
            }
            foreach (var kv in casosExtra)   // casos criados so no motor
                if (!casos.ContainsKey(kv.Key)) casos[kv.Key] = new JsonObject { ["nome"] = kv.Key, ["tipo"] = (string)kv.Value?["tipo"] ?? "G", ["subtipo"] = (string)kv.Value?["subtipo"] ?? "" };
            var corpo = new JsonObject
            {
                ["versao_formato"] = 1, ["nome"] = "Estructto", ["nos"] = nos, ["materiais"] = materiais,
                ["secoes"] = new JsonObject(), ["barras"] = new JsonObject(), ["casos"] = casos, ["peso_proprio"] = null,
                ["pavimentos"] = extra?["pavimentos"]?.DeepClone() ?? new JsonObject()
            };
            if (extra?["config"] is JsonObject cfg) corpo["config"] = cfg.DeepClone();
            await Motor.Chamar("PUT", "/api/modelo", corpo);

            var itens = new JsonArray();
            var secoes = m.Barras.Select(b => b.Secao).Distinct().ToList();
            foreach (string nome in secoes)
            {
                Secao s = Biblioteca.AcharSecao(nome);
                if (s == null) throw new MotorErro(0, "Perfil não reconhecido: \"" + nome + "\".");
                itens.Add(Item("secao", SpecSecao(nome, s)));
            }
            foreach (Barra b in m.Barras)
            {
                No a = m.NoPorId(b.N1), c = m.NoPorId(b.N2);
                double rot = (c.X - a.X < -1e-9 ? Math.PI : 0) + (b.EixoFraco ? Math.PI / 2 : 0);
                var p = new JsonObject();
                if (b.RotulaInicio) p["rotula_i"] = true;
                if (b.RotulaFim) p["rotula_j"] = true;
                if (m.Timoshenko) p["cortante"] = true;
                Flambar(m, b, p);
                itens.Add(Item("barra", new JsonObject { ["id"] = IdBarra(b.Id), ["ni"] = IdNo(b.N1), ["nj"] = IdNo(b.N2), ["secao"] = b.Secao, ["material"] = b.Material, ["tipo"] = "aco", ["rot"] = rot, ["params"] = p }));
            }
            for (int i = 0; i < m.Casos.Count; i++)
            {
                string caso = m.Casos[i].Nome;
                foreach (No n in m.Nos)
                {
                    double[] f = CargaNo(m, n, i);
                    if (f != null && (f[0] != 0 || f[1] != 0 || f[2] != 0))
                        itens.Add(Item("carga_nodal", new JsonObject { ["caso"] = caso, ["no"] = IdNo(n.Id), ["F"] = new JsonArray(f[0], 0.0, f[1], 0.0, -f[2], 0.0) }));
                }
                foreach (Barra b in m.Barras)
                {
                    double[] q = CargaBarra(m, b, i) ?? new double[4];
                    if (m.PesoProprio && m.Casos[i].PesoProprio)
                    {
                        Secao s = Biblioteca.AcharSecao(b.Secao); Material mt = Biblioteca.AcharMaterial(b.Material);
                        q[1] -= (s.Massa > 0 ? s.Massa : mt.Densidade * s.A) * g / 1000;
                    }
                    if (q[0] != 0 || q[1] != 0)
                        itens.Add(Item("carga_distribuida", new JsonObject { ["caso"] = caso, ["barra"] = IdBarra(b.Id), ["q"] = new JsonArray(q[0], 0.0, q[1]) }));
                    if (q[2] != 0 || q[3] != 0)   // perpendicular linear: q em ni, q2 em nj (globais, normal = (-S, C))
                    {
                        double L = m.Comp(b), C = (m.NoPorId(b.N2).X - m.NoPorId(b.N1).X) / L, S = (m.NoPorId(b.N2).Y - m.NoPorId(b.N1).Y) / L;
                        itens.Add(Item("carga_distribuida", new JsonObject
                        {
                            ["caso"] = caso, ["barra"] = IdBarra(b.Id),
                            ["q"] = new JsonArray(-S * q[2], 0.0, C * q[2]), ["q2"] = new JsonArray(-S * q[3], 0.0, C * q[3])
                        }));
                    }
                }
            }
            await Motor.Post("/api/modelo/lote", new JsonObject { ["itens"] = itens });

            // rigidez igual a do Nucleo.cs: Iy do motor = eixo forte (Ix do Estructto)
            foreach (string nome in secoes)
            {
                Secao s = Biblioteca.AcharSecao(nome);
                await Motor.Chamar("PATCH", "/api/modelo/secao/" + Url(nome), new JsonObject
                {
                    ["A"] = s.A, ["Iy"] = s.Ix, ["Iz"] = s.Iy,
                    ["props"] = new JsonObject { ["Az"] = s.AsX > 0 ? s.AsX : s.A, ["Ay"] = s.AsY > 0 ? s.AsY : s.A }
                });
            }
            // cargas automaticas de vento/sismo ja no envio (as janelas as mostram); o calculo gera de novo de qualquer forma
            if (extra?["config"]?["vento"] != null || extra?["config"]?["sismo"] != null)
                try { await Motor.Post("/api/acoes/gerar"); } catch (MotorErro) { }
        }

        // Parte do modelo do motor que so as janelas do motor editam (vai para Modelo.MotorExtra).
        public static async Task<string> LerExtra()
        {
            var d = await Motor.Get("/api/modelo");
            var casos = new JsonObject();
            foreach (var kv in d?["casos"] as JsonObject ?? new JsonObject())
                casos[kv.Key] = new JsonObject { ["tipo"] = kv.Value?["tipo"]?.DeepClone(), ["subtipo"] = kv.Value?["subtipo"]?.DeepClone() };
            return new JsonObject
            {
                ["config"] = d?["config"]?.DeepClone(), ["pavimentos"] = d?["pavimentos"]?.DeepClone(), ["casos"] = casos
            }.ToJsonString();
        }

        // Comprimentos de flambagem iguais aos do Nucleo.cs (Flambagem.Calcular: barra continua inteira, travamento fora do
        // plano, K >= 1). kx = eixo forte do motor (Iy do motor = Ix do Estructto), ky = eixo fraco; Lb = maior trecho
        // destravado no plano em que a barra flete pelo eixo fraco (fora do plano em pe; no plano se "deitado").
        // Plano instavel (Le infinito): nao envia; o motor usa k = 1 e a verificacao interna acusa a instabilidade.
        static void Flambar(Modelo m, Barra b, JsonObject p)
        {
            Secao s = Biblioteca.AcharSecao(b.Secao); Material mt = Biblioteca.AcharMaterial(b.Material);
            if (s == null || mt == null) return;
            var v = new Verif();
            Flambagem.Calcular(m, b, s, mt, v);
            Flamb forte = b.EixoFraco ? v.ForaPlano : v.NoPlano, fraco = b.EixoFraco ? v.NoPlano : v.ForaPlano;
            double L = m.Comp(b);
            if (!forte.Instavel) p["kx"] = forte.Le / L;
            if (!fraco.Instavel) p["ky"] = fraco.Le / L;
            if (fraco.LDestr > 0) p["Lb"] = fraco.LDestr;
        }

        static JsonObject Item(string tipo, JsonObject dados) { return new JsonObject { ["tipo"] = tipo, ["dados"] = dados }; }

        // ponytail: o Estructto nao guarda fu; tabela pelos nomes usuais, senao 1,25.fy
        static double Fu(string nome, double fy)
        {
            string n = nome.ToUpperInvariant();
            if (n.Contains("572")) return 450;
            if (n.Contains("A36") || n.Contains("A-36") || n.Contains("A 36")) return 400;
            if (n.Contains("A500")) return 400;
            return 1.25 * fy;
        }

        static JsonObject SpecSecao(string nome, Secao s)
        {
            switch (s.Forma)
            {
                case "TC": return new JsonObject { ["nome"] = nome, ["forma"] = "tubo_circular", ["d"] = s.H, ["t"] = s.Tw };
                case "TR": return new JsonObject { ["nome"] = nome, ["forma"] = "tubo_retangular", ["h"] = s.H, ["b"] = s.Bf, ["t"] = s.Tw };
                case "I": return new JsonObject { ["nome"] = nome, ["forma"] = "I", ["h"] = s.H, ["b"] = s.Bf, ["tw"] = s.Tw, ["tf"] = s.Tf };
                case "U": return new JsonObject { ["nome"] = nome, ["forma"] = "U", ["h"] = s.H, ["b"] = s.Bf, ["tw"] = s.Tw, ["tf"] = s.Tf };
                case "L": return new JsonObject { ["nome"] = nome, ["forma"] = "cantoneira", ["b"] = s.Bf, ["h"] = s.H, ["t"] = s.Tw };
                case "RET": return new JsonObject { ["nome"] = nome, ["forma"] = "retangular", ["b"] = s.Bf, ["h"] = s.H };
                default: return new JsonObject { ["nome"] = nome, ["A"] = s.A, ["Iy"] = s.Ix, ["Iz"] = s.Iy, ["J"] = s.Ix + s.Iy, ["props"] = new JsonObject() };
            }
        }

        // Fx, Fy, Mz do no no caso i (o caso ativo fica nos campos do no)
        static double[] CargaNo(Modelo m, No n, int i)
        {
            if (i == m.CasoAtivo) return new[] { n.Fx, n.Fy, n.Mz };
            var c = n.OutrasCargas?.Find(x => x.Caso == i);
            return c == null ? null : new[] { c.Fx, c.Fy, c.Mz };
        }

        // qx, qy, q perpendicular inicio e fim da barra no caso i
        static double[] CargaBarra(Modelo m, Barra b, int i)
        {
            if (i == m.CasoAtivo) return new[] { b.QGlobalX, b.QGlobalY, b.QLocalIni, b.QLocalFim };
            var c = b.OutrasCargas?.Find(x => x.Caso == i);
            return c == null ? null : new[] { c.QX, c.QY, c.QIni, c.QFim };
        }

        // ---------- leitura ----------

        static double D(JsonNode n) { return n == null ? 0 : n.GetValue<double>(); }
        static int Num(string id) { return id != null && id.Length > 1 && int.TryParse(id.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : throw new MotorErro(0, "resposta inesperada do motor: id \"" + id + "\""); }

        // deslocamentos/reacoes/esforcos (formato do ResultadoDict) -> convencoes do Estructto
        static MotorEstado Ler(Modelo m, JsonNode desl, JsonNode reac, JsonNode esf)
        {
            var e = new MotorEstado();
            foreach (No n in m.Nos) e.Nos[n.Id] = new double[6];
            foreach (var kv in desl?.AsObject() ?? new JsonObject())
            {
                if (!e.Nos.TryGetValue(Num(kv.Key), out var v) || kv.Value is not JsonArray d) throw new MotorErro(0, "resposta inesperada do motor: deslocamento do no " + kv.Key);
                v[0] = D(d[0]); v[1] = D(d[2]); v[2] = -D(d[4]);
            }
            foreach (var kv in reac?.AsObject() ?? new JsonObject())
            {
                if (!e.Nos.TryGetValue(Num(kv.Key), out var v) || kv.Value is not JsonArray r) throw new MotorErro(0, "resposta inesperada do motor: reacao do no " + kv.Key);
                v[3] = D(r[0]); v[4] = D(r[2]); v[5] = -D(r[4]);
            }
            foreach (var kv in esf?.AsObject() ?? new JsonObject())
            {
                Barra b = m.Barras.FirstOrDefault(x => x.Id == Num(kv.Key)) ?? throw new MotorErro(0, "resposta inesperada do motor: barra " + kv.Key + " nao existe no modelo");
                double[] A(string c) { return kv.Value[c].AsArray().Select(D).ToArray(); }
                // em pe: flexao no plano = My, cortante = Vz; deitado (rot pi/2): Mz e Vy
                e.Barras[b.Id] = b.EixoFraco
                    ? new MotorEsforcos { X = A("x"), N = A("N"), V = A("Vy").Select(x => -x).ToArray(), M = A("Mz") }
                    : new MotorEsforcos { X = A("x"), N = A("N"), V = A("Vz").Select(x => -x).ToArray(), M = A("My").Select(x => -x).ToArray() };
            }
            return e;
        }

        // ResBarra (formulas fechadas do Nucleo.cs) a partir dos esforcos e deslocamentos do motor, para a vista, as
        // tabelas e as janelas usarem o resultado do motor. Rigidez e geometria vem de "base" (mesma barra no Nucleo.cs).
        // Sem carga pontual, N e V sao quadraticos em x: F das pontas e cargas lineares (P1, P2, Q1, Q2) saem de x = 0,
        // de um ponto interno e de x = L.
        public static ResBarra ParaResBarra(Modelo m, Barra b, ResBarra base_, MotorEstado e)
        {
            var r = new ResBarra { L = base_.L, C = base_.C, S = base_.S, EA = base_.EA, EI = base_.EI, GAs = base_.GAs };
            MotorEsforcos s = e.Barras[b.Id];
            int k = 0; double L = r.L;
            for (int i = 1; i < s.X.Length - 1; i++) if (Math.Abs(s.X[i] - L / 2) < Math.Abs(s.X[k] - L / 2)) k = i;
            // f(x) = f0 + a.x + (c - a).x^2/(2L): (a, c) pelos valores em x = xm e x = L
            void Linear(double[] f, out double a, out double c)
            {
                double xm = s.X[k], ym = f[k] - f[0], yL = f[f.Length - 1] - f[0];
                // ym = a.xm + (c - a).xm^2/(2L) ; yL = (a + c).L/2
                double t = xm * xm / (2 * L), det = (xm - t) * (L / 2) - t * (L / 2);
                a = k == 0 ? yL / L : (ym * (L / 2) - t * yL) / det;
                c = k == 0 ? yL / L : ((xm - t) * yL - (L / 2) * ym) / det;
            }
            double[] nNeg = s.N.Select(x => -x).ToArray();
            Linear(nNeg, out r.P1, out r.P2);   // -N = F0 + P1.x + ...
            Linear(s.V, out r.Q1, out r.Q2);
            int u = s.X.Length - 1;
            r.F[0] = -s.N[0]; r.F[1] = s.V[0]; r.F[2] = -s.M[0];
            r.F[3] = s.N[u]; r.F[4] = -s.V[u]; r.F[5] = s.M[u];
            double[] di = e.Nos[b.N1], dj = e.Nos[b.N2];
            r.U[0] = di[0] * r.C + di[1] * r.S; r.U[1] = -di[0] * r.S + di[1] * r.C; r.U[2] = di[2];
            r.U[3] = dj[0] * r.C + dj[1] * r.S; r.U[4] = -dj[0] * r.S + dj[1] * r.C; r.U[5] = dj[2];
            r.Fechar();
            return r;
        }

        // Analise de um caso de carga isolado (sem combinacao): GET /api/analise/caso/{nome}. Chamar Enviar antes.
        public static async Task<MotorEstado> AnalisarCaso(Modelo m, string caso)
        {
            var r = await Motor.Get("/api/analise/caso/" + Url(caso));
            return Ler(m, r["deslocamentos"], r["reacoes"], r["esforcos"]);
        }

        // Fluxo completo: envia, calcula (combinacoes NBR 8681, verificacao NBR 8800) e le os resultados de cada combinacao.
        // Sob Motor.Exclusivo: nenhum outro caminho troca o modelo entre o /api/calcular e os GET dos resultados.
        public static Task<MotorResultado> Calcular(Modelo m, IProgress<string> progresso = null) => Motor.Exclusivo(() => CalcularSemFila(m, progresso));

        static async Task<MotorResultado> CalcularSemFila(Modelo m, IProgress<string> progresso)
        {
            progresso?.Report("enviando o modelo");
            await Enviar(m);
            progresso?.Report("calculando (combinações e NBR 8800)");
            var calc = await Motor.Post("/api/calcular", new JsonObject());
            var res = new MotorResultado { Resumo = calc?["resumo"] };
            foreach (var c in calc?["combinacoes"] as JsonArray ?? throw new MotorErro(0, "resposta inesperada do motor: /api/calcular sem combinacoes"))
            {
                string nome = (string)c["nome"];
                res.Combinacoes.Add(nome);
                res.EstadoDe[nome] = (string)c["estado"];
            }
            foreach (var a in calc["avisos"]?.AsArray() ?? new JsonArray()) res.Avisos.Add((string)a["mensagem"]);
            int k = 0;
            foreach (string nome in res.Combinacoes)
            {
                progresso?.Report("lendo resultados " + ++k + "/" + res.Combinacoes.Count);
                string q = "&combinacao=" + Url(nome);
                res.PorCombinacao[nome] = Ler(m, await Motor.Get("/api/resultados?alvo=deslocamentos" + q),
                    await Motor.Get("/api/resultados?alvo=reacoes" + q), await Motor.Get("/api/resultados?alvo=esforcos" + q));
            }
            foreach (var l in (await Motor.Get("/api/verificacao")).AsArray())
                res.Verificacao.Add(new MotorVerif
                {
                    Barra = Num((string)l["barra"]), Taxa = l["taxa"] == null ? (double?)null : D(l["taxa"]), Ok = l["ok"] != null && (bool)l["ok"],
                    Norma = (string)l["norma"], Criterio = (string)l["criterio"], Item = (string)l["item"], Combinacao = (string)l["combinacao"]
                });
            return res;
        }

        // ---------- conferencia interno x motor (Estructto.exe --conferir-motor <saida.txt> <arq.estructto>...) ----------
        // Compara, caso a caso, reacoes, deslocamentos e N/V/M (nos pontos x do motor) do Nucleo.cs com o motor Python.
        public static async Task<string> Conferir(IEnumerable<string> arquivos)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            double piorGeral = 0;
            foreach (string arq in arquivos)
            {
                Modelo m = Modelo.DeXml(File.ReadAllText(arq));
                sb.AppendLine("== " + Path.GetFileName(arq));
                ResultadosCasos ri = Analise.ResolverCasos(m);
                if (!string.IsNullOrEmpty(ri.Erro)) { sb.AppendLine("  interno: " + ri.Erro); continue; }
                await Enviar(m);
                for (int i = 0; i < m.Casos.Count; i++)
                {
                    MotorEstado me = await AnalisarCaso(m, m.Casos[i].Nome);
                    sb.AppendLine("  caso \"" + m.Casos[i].Nome + "\"");
                    double pior = 0;
                    sb.AppendLine("    nó   grandeza         interno        motor      dif");
                    string[] nomes = { "ux (mm)", "uz (mm)", "giro (mrad)", "Rx (kN)", "Rz (kN)", "M (kN.m)" };
                    double[] esc = { 1000, 1000, 1000, 1, 1, 1 };
                    foreach (No n in m.Nos)
                    {
                        double[] a = ri.Nos[i][n.Id], b = me.Nos[n.Id];
                        for (int k = 0; k < 6; k++)
                        {
                            if (k >= 3 && !n.TemApoio) continue;
                            double va = a[k] * esc[k], vb = b[k] * esc[k], d = Math.Abs(va - vb);
                            pior = Math.Max(pior, Rel(va, vb));
                            sb.AppendLine(string.Format(ci, "    {0,-4} {1,-12} {2,12:0.0000} {3,12:0.0000} {4,8:0.0e+0}", n.Id, nomes[k], va, vb, d));
                        }
                    }
                    sb.AppendLine("    barra  x(m)      N int     N mot     V int     V mot     M int     M mot");
                    foreach (Barra b in m.Barras)
                    {
                        ResBarra rb = ri.Barras[i][b.Id]; MotorEsforcos e = me.Barras[b.Id];
                        for (int k = 0; k < e.X.Length; k += Math.Max(1, (e.X.Length - 1) / 4))
                        {
                            double x = e.X[k], n1 = rb.N(x), v1 = rb.Q(x), m1 = rb.M(x);
                            pior = Math.Max(pior, Math.Max(Rel(n1, e.N[k]), Math.Max(Rel(v1, e.V[k]), Rel(m1, e.M[k]))));
                            sb.AppendLine(string.Format(ci, "    {0,-5} {1,5:0.000} {2,9:0.000} {3,9:0.000} {4,9:0.000} {5,9:0.000} {6,9:0.000} {7,9:0.000}", b.Id, x, n1, e.N[k], v1, e.V[k], m1, e.M[k]));
                        }
                    }
                    sb.AppendLine(string.Format(ci, "    maior diferença relativa: {0:0.0e+0}", pior));
                    piorGeral = Math.Max(piorGeral, pior);
                }
                // verificacao: interna (tensoes admissiveis, n do modelo, caso ativo) x motor (NBR 8800, combinacoes ELU)
                Modelo mi = Modelo.DeXml(m.ParaXml());
                Solver.Resolver(mi);
                MotorResultado rm = await Calcular(m);
                sb.AppendLine("  verificação (" + rm.Combinacoes.Count + " combinações do motor: " + string.Join(", ", rm.Combinacoes) + ")");
                foreach (Barra b in mi.Barras)
                {
                    Verif v = Verificador.Barra(mi, b, Biblioteca.AcharSecao(b.Secao));
                    MotorVerif mv = rm.Verificacao.FirstOrDefault(x => x.Barra == b.Id);
                    sb.AppendLine(string.Format(ci, "    barra {0}: interno {1:0.000} ({2}, n = {3:0.##})  |  motor {4} ({5}, {6}, {7})", b.Id, v.Ap, v.Critico, m.Seguranca,
                        mv?.Taxa == null ? "-" : mv.Taxa.Value.ToString("0.000", ci), mv?.Criterio, mv?.Item, mv?.Combinacao));
                }
            }
            sb.AppendLine(string.Format(ci, "RESUMO: maior diferença relativa em todos os arquivos = {0:0.0e+0} ({1})", piorGeral, piorGeral < 1e-3 ? "CONFERE" : "DIFERE"));
            return sb.ToString();
        }

        // diferenca relativa com piso absoluto (valores ~0 nao explodem)
        static double Rel(double a, double b) { return Math.Abs(a - b) / Math.Max(1e-3, Math.Max(Math.Abs(a), Math.Abs(b))); }
    }
}
