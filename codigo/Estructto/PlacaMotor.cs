// Placa de base pelo motor: método NBR v2 de codigo/motor/prossen_estrutural/aco/placa_base.py (docs/v3/PLACA-BASE-V2.md §10).
// PlacaMotor: PlacaBase (mm, MPa) ↔ dados do motor (m, kN/m²) e a chamada síncrona das rotas /api/placa_base/*.
// ResNbr: o resultado de /api/placa_base/verificar (pior combinação ELU × membros × eixos) lido nos eixos da janela da placa.
// O Nucleo.cs (PlacaCalc) fica só como "Comparar com método legado" (pivô na borda, n global: subestima a tração).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Portico;
using static NovoFtool.UiAnalise;

namespace NovoFtool
{
    public static class PlacaMotor
    {
        public static JsonObject Dados(PlacaBase p) => new JsonObject
        {
            ["H"] = p.H / 1000, ["B"] = p.B / 1000, ["esp"] = p.Esp / 1000, ["borda"] = p.Borda / 1000, ["sobra"] = p.Sobra / 1000, ["pelo_perfil"] = p.PeloPerfil,
            ["fileiras"] = p.Fileiras, ["por_fileira"] = p.PorFileira, ["diametro_mm"] = p.Diametro, ["classe"] = p.Classe ?? "8.8", ["rosca_no_corte"] = p.RoscaNoCorte,
            ["pivo_na_mesa"] = p.PivoNaMesa, ["fy_chapa"] = p.FyChapa * 1000, ["fck"] = p.Fck * 1000, ["seguranca"] = p.Seguranca, ["chumbador"] = p.Chumbador ?? "",
            ["classe_barra"] = p.ClasseBarra ?? "4.6", ["tipo_carga"] = p.TipoCarga ?? "", ["risco"] = p.Risco ?? "", ["coef_chumbador"] = p.CoefChumbador,
            ["solda"] = p.Solda / 1000, ["eletrodo"] = string.IsNullOrEmpty(p.Eletrodo) ? "E70" : p.Eletrodo,
            ["bloco_H"] = p.BlocoH / 1000, ["bloco_B"] = p.BlocoB / 1000, ["bloco_altura"] = p.BlocoAltura / 1000, ["hef"] = p.Hef / 1000,
            ["fissurado"] = !p.NaoFissurado, ["lug_b"] = p.LugB / 1000, ["lug_t"] = p.LugT / 1000, ["lug_h"] = p.LugH / 1000, ["graute"] = p.Graute / 1000,
            ["arruela_soldada"] = p.ArruelaSoldada
        };

        public static PlacaBase Placa(JsonNode d)
        {
            double Nn(string k, double pad) { double v = D(d, k); return double.IsNaN(v) ? pad : v; }
            string Sx(string k, string pad) { string s = S(d, k); return s == "" ? pad : s; }
            bool Bx(string k, bool pad) => d?[k] is JsonValue v && v.TryGetValue(out bool x) ? x : pad;
            return new PlacaBase
            {
                H = Nn("H", .4) * 1000, B = Nn("B", .3) * 1000, Esp = Nn("esp", .019) * 1000, Borda = Nn("borda", .05) * 1000, Sobra = Nn("sobra", .1) * 1000,
                PeloPerfil = Bx("pelo_perfil", false), Fileiras = (int)Nn("fileiras", 2), PorFileira = (int)Nn("por_fileira", 2), Diametro = Nn("diametro_mm", 16),
                Classe = Sx("classe", "8.8"), RoscaNoCorte = Bx("rosca_no_corte", true), PivoNaMesa = Bx("pivo_na_mesa", false),
                FyChapa = Nn("fy_chapa", 250000) / 1000, Fck = Nn("fck", 20000) / 1000, Seguranca = Nn("seguranca", 0), Chumbador = Sx("chumbador", ""),
                ClasseBarra = Sx("classe_barra", "4.6"), TipoCarga = Sx("tipo_carga", ""), Risco = Sx("risco", ""), CoefChumbador = Nn("coef_chumbador", 0),
                Solda = Nn("solda", 0) * 1000, Eletrodo = Sx("eletrodo", "E70"),
                BlocoH = Nn("bloco_H", 0) * 1000, BlocoB = Nn("bloco_B", 0) * 1000, BlocoAltura = Nn("bloco_altura", 0) * 1000, Hef = Nn("hef", 0) * 1000,
                NaoFissurado = !Bx("fissurado", true), LugB = Nn("lug_b", 0) * 1000, LugT = Nn("lug_t", 0) * 1000, LugH = Nn("lug_h", 0) * 1000,
                Graute = Nn("graute", 0) * 1000, ArruelaSoldada = Bx("arruela_soldada", false)
            };
        }

        // Síncrono: a janela da placa recalcula a cada clique e o motor responde em milissegundos. Task.Run tira as continuações
        // do contexto da UI (sem deadlock); 2 min de limite para não travar a janela se o motor estiver preso em outra operação.
        public static JsonNode Esperar(Func<Task<JsonNode>> chamada)
        {
            var t = Task.Run(chamada);
            try { if (!t.Wait(TimeSpan.FromMinutes(2))) throw new MotorErro(0, "o motor não respondeu (ocupado em outra operação?)"); }
            catch (AggregateException ex) { throw ex.InnerException ?? ex; }
            return t.Result;
        }

        // Mensagem do erro do motor ({"erro", "mensagem"}) ou da exceção.
        public static string Mensagem(Exception ex)
        {
            if (ex is MotorErro me) { try { var j = JsonNode.Parse(me.Message); string s = S(j, "mensagem"); if (s != "") return s; } catch { } }
            return ex.Message;
        }
    }

    public partial class DocumentoEspacial
    {
        // Rotas da placa de base (/api/placa_base/*): o modelo do documento no servidor e o cálculo valendo (refaz se o servidor o perdeu).
        public JsonNode Placa(string rota, JsonObject corpo) => PlacaMotor.Esperar(() => Motor.Exclusivo(async () =>
        {
            await GarantirCalculado();
            return await Motor.Post(rota, corpo);
        }));

        // Dentro de um Motor.Exclusivo: o modelo deste documento no servidor (outro documento pode ter deixado o dele) e o
        // cálculo valendo (refaz se o servidor o perdeu).
        internal async Task GarantirCalculado()
        {
            await Garantir();
            if (Calculado && await Motor.Get("/api/calculo") == null) await Motor.Post("/api/calcular", new JsonObject());
        }
    }

    // Resultado do motor nos eixos da janela: y ao longo de H, z ao longo de B (mm, kN, kN·m). Quando governa o eixo fraco
    // (Mz), o motor girou a placa (dados_no_eixo): os (y, z) dele são os (z, y) da janela e o bloco de compressão corre em z.
    public sealed class ResNbr
    {
        public sealed class Passo
        {
            public double Taxa; public bool Ok = true; public string Criterio = "", Comb = "", Eixo = "", Barra = "";
            public double N, V, M;
            public bool Fraco => Eixo.StartsWith("fraco");
        }

        public JsonNode J;
        public string Erro = "";
        public bool Valido => Erro == "" && J != null;
        public bool Ok; public double Taxa;
        public Passo Fix = new Passo(), Chapa = new Passo(), Solda = new Passo();
        public string Comb = "", Eixo = "", Barra = "";
        public bool Fraco;
        public double N, V, M;                 // governante, nos eixos da placa (N > 0 = tração nas fixações)
        public double TNec, TPfeil, HSolda;    // mm: chapa CBCA (máximo nas combinações), conferência Pfeil, maior perna mínima
        public string Governa = "";
        public List<double[]> Paraf = new List<double[]>();   // y, z (mm, eixos da janela), T (kN)
        public double Fv, Tmax, Y, BlocoDe, BlocoAte, Fp, SigmaRd;   // Y e bloco em mm (em y, ou em z se Fraco); MPa
        public bool TemBloco;
        public string Modelo = "", Mecanismo = "";
        public int NEf, NCombs;
        public List<string> Avisos = new List<string>();
        public JsonNode R => J?["resultado"];

        public static ResNbr ComErro(string e) => new ResNbr { Erro = e };

        static double Tx(double v) => double.IsNaN(v) ? double.PositiveInfinity : v;   // taxa infinita vem como null

        static Passo LerPasso(JsonNode p)
        {
            var x = new Passo();
            if (p == null) return x;
            x.Taxa = Tx(D(p, "taxa")); x.Ok = Bool(p, "ok"); x.Criterio = S(p, "criterio"); x.Comb = S(p, "combinacao"); x.Eixo = S(p, "eixo"); x.Barra = S(p, "barra");
            x.N = D(p["esforcos"], "N"); x.V = D(p["esforcos"], "V"); x.M = D(p["esforcos"], "M");
            return x;
        }

        public static ResNbr De(JsonNode j)
        {
            if (j == null) return ComErro("o motor não respondeu");
            var r = new ResNbr { J = j, Ok = Bool(j, "ok"), Taxa = Tx(D(j, "taxa_max")) };
            var g = j["governante"];
            r.Comb = S(g, "combinacao"); r.Eixo = S(g, "eixo"); r.Barra = S(g, "barra"); r.Fraco = r.Eixo.StartsWith("fraco");
            r.N = D(g, "N"); r.V = D(g, "V"); r.M = D(g, "M");
            var ps = j["passos"];
            r.Fix = LerPasso(ps?["fixacao"]); r.Chapa = LerPasso(ps?["chapa"]); r.Solda = LerPasso(ps?["solda"]);
            r.TNec = D(ps?["chapa"], "t_nec") * 1000; r.HSolda = D(ps?["solda"], "h_max") * 1000;
            r.NCombs = Lista(j["combinacoes"]).Count();
            var res = j["resultado"]; var f = res?["forcas"]; var geo = res?["geometria"];
            r.Fv = D(f, "Fv"); r.Tmax = D(f, "Tmax"); r.Y = D(f, "Y") * 1000; r.Fp = D(f, "fp") / 1000; r.SigmaRd = D(f, "sigma_cRd") / 1000; r.Modelo = S(f, "modelo");
            foreach (var p in Lista(f?["paraf"]))
            {
                double y = D(p[0]) * 1000, z = D(p[1]) * 1000, t = D(p[2]);
                r.Paraf.Add(r.Fraco ? new[] { z, y, t } : new[] { y, z, t });
            }
            double yb = D(f, "y_bloco"), lado = D(f, "lado_comprimido"), Hr = D(geo, "H");
            if (!double.IsNaN(yb) && r.Y > 0) { r.TemBloco = true; r.BlocoDe = (double.IsNaN(lado) ? 1 : lado) * Hr / 2 * 1000; r.BlocoAte = yb * 1000; }
            r.TPfeil = D(res?["chapa"], "t_pfeil") * 1000; r.Governa = S(res?["chapa"], "governa");
            r.Mecanismo = S(res?["cisalhamento"], "mecanismo");
            double ne = D(res?["cisalhamento"], "n_ef"); r.NEf = double.IsNaN(ne) ? 0 : (int)ne;
            foreach (var a in Lista(res?["avisos"])) { string s = a.GetValue<string>(); if (!r.Avisos.Contains(s)) r.Avisos.Add(s); }
            return r;
        }

        // Verificações do governante por passo da janela (o motor agrupa igual em projeto._passo_placa).
        public static string PassoDe(string nome) => nome.StartsWith("solda") ? "solda" : nome.StartsWith("chapa") || nome.StartsWith("concreto: pressão de contato") ? "chapa" : "fixacao";
        public IEnumerable<JsonNode> Verificacoes(string passo) => Lista(R?["verificacoes"]).Where(v => PassoDe(S(v, "nome")) == passo);

        // Resumo de uma variante (/api/placa_base/verificar com "variantes" e "triagem").
        public static (bool FixOk, double FixTaxa, bool Ok, double Taxa, double TNec, string Criterio) Resumo(JsonNode v)
        {
            var fx = v?["passos"]?["fixacao"];
            bool parcial = Bool(v, "parcial");
            return (!parcial && Bool(fx, "ok"), Tx(D(fx, "taxa")), Bool(v, "ok"), Tx(D(v, "taxa_max")), parcial ? double.NaN : D(v?["passos"]?["chapa"], "t_nec") * 1000, S(fx, "criterio"));
        }
    }
}
