// Confere o motor de calculo contra solucoes classicas de tabela.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Portico
{
    static class Testes
    {
        static int falhas = 0, total = 0;
        const string SEC = "A=100 I=10000";   // EA = 2e6 kN, EI = 20000 kN.m2 com E = 200000 MPa
        const string MAT = "E=200000";
        const double EI = 20000;

        static void Conf(string nome, double obtido, double esperado, double relTol = 1e-6)
        {
            total++;
            double tol = relTol * Math.Max(relTol == 1e-6 ? 1 : 1e-9, Math.Abs(esperado));
            bool ok = Math.Abs(obtido - esperado) <= tol;
            if (!ok) falhas++;
            Console.WriteLine("{0}  {1,-52} obtido {2,14:0.000000}  esperado {3,14:0.000000}", ok ? "ok   " : "FALHA", nome, obtido, esperado);
        }

        static Modelo Novo(params double[] xy)
        {
            Modelo m = new Modelo();
            for (int i = 0; i < xy.Length; i += 2) m.Nos.Add(new No { Id = m.ProxId++, X = xy[i], Y = xy[i + 1] });
            return m;
        }

        static Barra B(Modelo m, int i, int j)
        {
            Barra b = new Barra { Id = m.ProxId++, N1 = m.Nos[i].Id, N2 = m.Nos[j].Id, Secao = SEC, Material = MAT, Dono = m };
            m.Barras.Add(b);
            return b;
        }

        static void Engaste(No n) { n.ApoioX = n.ApoioY = n.ApoioGiro = true; }
        static void Fixo(No n) { n.ApoioX = n.ApoioY = true; }

        static int Main()
        {
            Modelo m; Barra b, b2; Resultado r;
            double q = 10, L = 6;

            Console.WriteLine("-- viga biapoiada, carga uniforme");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.QGlobalY = -q; Fixo(m.Nos[0]); m.Nos[1].ApoioY = true;
            r = Solver.Resolver(m);
            Conf("reacao", m.Nos[0].Res[4], q * L / 2);
            Conf("momento no meio = qL2/8", b.Res.M(L / 2), q * L * L / 8);
            Conf("flecha = 5qL4/384EI", b.Res.V(L / 2), -5 * q * Math.Pow(L, 4) / (384 * EI));
            Conf("cortante no inicio", b.Res.Q(0), q * L / 2);

            Console.WriteLine("-- viga biengastada, carga uniforme");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.QGlobalY = -q; Engaste(m.Nos[0]); Engaste(m.Nos[1]);
            r = Solver.Resolver(m);
            Conf("momento no engaste = -qL2/12", b.Res.M(0), -q * L * L / 12);
            Conf("momento no meio = qL2/24", b.Res.M(L / 2), q * L * L / 24);
            Conf("flecha = qL4/384EI", b.Res.V(L / 2), -q * Math.Pow(L, 4) / (384 * EI));

            Console.WriteLine("-- viga biengastada, carga triangular (0 no inicio, w no fim)");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.QLocalFim = -q; Engaste(m.Nos[0]); Engaste(m.Nos[1]);
            r = Solver.Resolver(m);
            Conf("momento inicio = -wL2/30", b.Res.M(0), -q * L * L / 30);
            Conf("momento fim = -wL2/20", b.Res.M(L), -q * L * L / 20);
            Conf("reacao inicio = 3wL/20", m.Nos[0].Res[4], 3 * q * L / 20);
            Conf("reacao fim = 7wL/20", m.Nos[1].Res[4], 7 * q * L / 20);

            Console.WriteLine("-- engastada e apoiada (rotula na ponta de uma biengastada)");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.QGlobalY = -q; b.RotulaFim = true; Engaste(m.Nos[0]); Engaste(m.Nos[1]);
            r = Solver.Resolver(m);
            Conf("momento no engaste = -qL2/8", b.Res.M(0), -q * L * L / 8);
            Conf("reacao na rotula = 3qL/8", m.Nos[1].Res[4], 3 * q * L / 8);
            Conf("momento na rotula = 0", b.Res.M(L), 0);
            Conf("momento positivo max = 9qL2/128", MaxM(b), 9 * q * L * L / 128);

            Console.WriteLine("-- balanco com carga na ponta");
            m = Novo(0, 0, 3, 0); b = B(m, 0, 1); Engaste(m.Nos[0]); m.Nos[1].Fy = -50;
            r = Solver.Resolver(m);
            Conf("flecha = PL3/3EI", m.Nos[1].Res[1], -50 * 27 / (3 * EI));
            Conf("giro = PL2/2EI", m.Nos[1].Res[2], -50 * 9 / (2 * EI));
            Conf("momento no engaste = -PL", b.Res.M(0), -150);
            Conf("reacao de momento", m.Nos[0].Res[5], 150);

            Console.WriteLine("-- balanco com deformacao por cortante (Timoshenko)");
            m = Novo(0, 0, 2, 0); b = B(m, 0, 1); b.Secao = "Ret 20x50"; b.Material = "E=25000"; Engaste(m.Nos[0]); m.Nos[1].Fy = -100;
            m.Timoshenko = true;
            r = Solver.Resolver(m);
            double ei = 25000e3 * 0.2 * Math.Pow(0.5, 3) / 12, gas = 25000e3 / 2.6 * (0.1 * 5 / 6);
            Conf("flecha = PL3/3EI + PL/GAs", m.Nos[1].Res[1], -(100 * 8 / (3 * ei) + 100 * 2 / gas));
            Conf("elastica no meio (formula fechada)", b.Res.V(1), -(100.0 / (6 * ei) * (3 * 2 - 1) + 100 * 1 / gas));

            Console.WriteLine("-- biengastada com Timoshenko e carga triangular: equilibrio e simetria da uniforme");
            m = Novo(0, 0, 2, 0); b = B(m, 0, 1); b.Secao = "Ret 20x50"; b.Material = "E=25000"; b.QGlobalY = -q; Engaste(m.Nos[0]); Engaste(m.Nos[1]);
            m.Timoshenko = true;
            r = Solver.Resolver(m);
            Conf("uniforme: momento no engaste = -qL2/12", b.Res.M(0), -q * 4 / 12);
            Conf("uniforme: flecha = qL4/384EI + qL2/8GAs", b.Res.V(1), -(q * 16 / (384 * ei) + q * 4 / (8 * gas)));

            Console.WriteLine("-- recalque de apoio em biengastada");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); Engaste(m.Nos[0]); Engaste(m.Nos[1]); m.Nos[1].RecalqueY = -10;
            r = Solver.Resolver(m);
            Conf("momento = 6EI.d/L2", Math.Abs(b.Res.M(0)), 6 * EI * 0.010 / (L * L));
            Conf("cortante = 12EI.d/L3", Math.Abs(b.Res.Q(0)), 12 * EI * 0.010 / (L * L * L));

            Console.WriteLine("-- mola: balanco apoiado em mola na ponta");
            m = Novo(0, 0, 3, 0); b = B(m, 0, 1); Engaste(m.Nos[0]); m.Nos[1].Fy = -50; m.Nos[1].MolaY = 3 * EI / 27;   // mola = rigidez da viga
            r = Solver.Resolver(m);
            Conf("metade da carga vai para a mola", m.Nos[1].Res[4], 25);

            Console.WriteLine("-- viga Gerber (rotula interna em no com as duas barras rotuladas)");
            m = Novo(0, 0, 4, 0, 8, 0); b = B(m, 0, 1); b2 = B(m, 1, 2); b.RotulaFim = true; b2.RotulaInicio = true;
            Engaste(m.Nos[0]); m.Nos[2].ApoioY = true; b2.QGlobalY = -q;
            r = Solver.Resolver(m);
            Conf("reacao no apoio movel = qL/2", m.Nos[2].Res[4], q * 4 / 2);
            Conf("momento no engaste = -(qL/2).L", b.Res.M(0), -q * 4 / 2 * 4);
            Conf("momento na rotula = 0", b2.Res.M(0), 0);

            Console.WriteLine("-- barra inclinada 3-4-5 biapoiada, carga vertical por metro de barra");
            m = Novo(0, 0, 4, 3); b = B(m, 0, 1); b.QGlobalY = -q; Fixo(m.Nos[0]); m.Nos[1].ApoioY = true;
            r = Solver.Resolver(m);
            Conf("reacao vertical = q.5/2", m.Nos[0].Res[4], 25);
            Conf("reacao horizontal = 0", m.Nos[0].Res[3], 0);
            Conf("momento no meio = (q.5).4/8", b.Res.M(2.5), q * 5 * 4 / 8);
            Conf("normal no inicio = -25.sen", b.Res.N(0), -25 * 0.6);

            Console.WriteLine("-- portico engastado com carga lateral: equilibrio e antissimetria");
            m = Novo(0, 0, 0, 4, 6, 4, 6, 0); B(m, 0, 1); b = B(m, 1, 2); B(m, 2, 3);
            Engaste(m.Nos[0]); Engaste(m.Nos[3]); m.Nos[1].Fx = 40; b.QGlobalY = -q;
            r = Solver.Resolver(m);
            Conf("soma das reacoes X = -40", r.SomaRx, -40);
            Conf("soma das reacoes Y = q.L", r.SomaRy, q * 6);
            double mo = m.Nos[0].Res[5] + m.Nos[3].Res[5] + m.Nos[3].Res[4] * 6 - 40 * 4 - q * 6 * 3;
            Conf("soma dos momentos em relacao ao no 1 = 0", mo, 0);
            Conf("equilibrio do no: M da viga = M do pilar", m.Barras[0].Res.M(4), b.Res.M(0));

            Console.WriteLine("-- trelica simples (barras birrotuladas)");
            m = Novo(0, 0, 4, 0, 2, 2); B(m, 0, 1); B(m, 0, 2); B(m, 1, 2);
            foreach (Barra t in m.Barras) { t.RotulaInicio = true; t.RotulaFim = true; }
            Fixo(m.Nos[0]); m.Nos[1].ApoioY = true; m.Nos[2].Fy = -100;
            r = Solver.Resolver(m);
            Conf("normal na diagonal = -50.raiz(2)", m.Barras[1].Res.N(0), -50 * Math.Sqrt(2));
            Conf("normal no banzo = +50", m.Barras[0].Res.N(0), 50);

            Console.WriteLine("-- estrutura hipostatica deve ser recusada");
            m = Novo(0, 0, L, 0); B(m, 0, 1); m.Nos[0].ApoioY = true; m.Nos[1].ApoioY = true;
            r = Solver.Resolver(m);
            Conf("detectou instabilidade", r.Ok ? 0 : 1, 1);

            Console.WriteLine("-- peso proprio com perfil Gerdau");
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.Secao = "W 310 x 38,7"; b.Material = "Aço ASTM A572 Gr.50"; Fixo(m.Nos[0]); m.Nos[1].ApoioY = true;
            m.PesoProprio = true;
            r = Solver.Resolver(m);
            Conf("reacao = 38,7 kg/m x g x L / 2", m.Nos[0].Res[4], 38.7 * 9.80665 / 1000 * L / 2);
            Conf("flecha com Ix = 8581 cm4", b.Res.V(L / 2), -5 * (38.7 * 9.80665 / 1000) * Math.Pow(L, 4) / (384 * 200000e3 * 8581e-8));

            Console.WriteLine("-- placa de base: pilar em balanco, 4 parafusos M16 8.8, placa 400 x 300, furo a 50 da borda");
            m = Novo(0, 0, 0, 3); b = B(m, 0, 1); b.Secao = "W 200 x 46,1"; b.Material = "Aço ASTM A572 Gr.50"; Engaste(m.Nos[0]); m.Nos[1].Fx = 10;
            m.Nos[0].Placa = new PlacaBase { H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", RoscaNoCorte = true };
            r = Solver.Resolver(m);
            ResPlaca rp = PlacaCalc.Calcular(m, m.Nos[0]);
            Conf("momento na base = 30 kN.m", Math.Abs(rp.M), 30);
            Conf("tracao maior = M.d/soma(d2) = 42 kN", rp.Tmax, 42);
            double somaTd = 0;
            foreach (double[] pf in rp.Paraf) somaTd += pf[2] * (0.2 + Math.Abs(pf[0]) / 1000 * (pf[2] > 10 ? 1 : -1));
            Conf("equilibrio: soma(T.d) em torno do giro = M", somaTd, 30);
            Conf("corte por parafuso = V/4", rp.Fv, 2.5);
            Conf("tensao de tracao = T/At", rp.Sigma, 42000 / 157.0);
            Conf("seguranca a tracao = Sp/sigma", rp.NT, 600 / (42000 / 157.0));
            Conf("seguranca ao corte = 0,577.Sp/(Fv/Ar)", rp.NV, 0.577 * 600 / (2500 / 144.0));
            Conf("torque = 0,2 x (0,75.At.Sp) x d", rp.Torque, 0.2 * 0.75 * 157 * 600 / 1000 * 16);
            m.Nos[1].Fx = 0; m.Nos[1].Fy = 100;
            r = Solver.Resolver(m);
            rp = PlacaCalc.Calcular(m, m.Nos[0]);
            Conf("so tracao de 100 kN: 25 kN por parafuso", rp.Tmax, 25);
            m.Nos[0].Placa = new PlacaBase { PeloPerfil = true, Sobra = 100, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8" };
            rp = PlacaCalc.Calcular(m, m.Nos[0]);
            Conf("placa pelo perfil: comprimento = d + 2 x sobra (arredondado)", rp.H, 410);
            Conf("placa pelo perfil: largura = bf + 2 x sobra (arredondado)", rp.B, 410);

            Console.WriteLine("-- espessura da chapa e verificacao simplificada da barra");
            m = Novo(0, 0, 0, 3); b = B(m, 0, 1); b.Secao = "W 200 x 46,1"; b.Material = "Aço ASTM A572 Gr.50"; Engaste(m.Nos[0]); m.Nos[1].Fx = 10;
            m.Nos[0].Placa = new PlacaBase { H = 400, B = 300, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8", RoscaNoCorte = true };
            r = Solver.Resolver(m);
            rp = PlacaCalc.Calcular(m, m.Nos[0]);
            double braco = 150 - 203 / 2.0, largEf = 2 * (2 * braco + 16);
            Conf("chapa: braco = furo ate a face do perfil", rp.Braco, braco);
            Conf("chapa, lado tracionado: t = raiz(6.T.e.n / (b.fy))", rp.EspNecT, Math.Sqrt(6 * 84000 * braco * 2 / (largEf * 250)));
            Conf("chapa, lado comprimido: C = soma T - N no giro, e = H/2 - d/2, largura B", rp.EspNecC, Math.Sqrt(6 * (rp.Paraf.Sum(pq => pq[2]) - rp.N) * 1000 * (200 - 203 / 2.0) * 2 / (300 * 250)));
            Conf("chapa: t necessaria = max(tracionado, comprimido)", rp.EspNec, Math.Max(rp.EspNecT, rp.EspNecC));
            Conf("parafuso M12 com os mesmos esforcos: seguranca menor que a do M16", PlacaCalc.SegurancaCom(rp, m.Nos[0].Placa, 2, 3) < rp.NComb ? 1 : 0, 1);
            Secao w200 = Biblioteca.AcharSecao("W 200 x 46,1");
            Verif vb = Verificador.Barra(m, b, w200);
            Conf("barra: tensao = M/W com W = Ix/(d/2)", vb.Sigma, 30 / (4543e-8 / 0.1015) / 1000);
            Conf("barra: aproveitamento da tensao = sigma.n/fy", vb.ApTensao, vb.Sigma * 2 / 345);
            Conf("barra: flecha admissivel = L/250", vb.FlechaAdm, 3.0 / 250);
            Conf("barra: perfil mais fraco tem aproveitamento maior", Verificador.Barra(m, b, Biblioteca.AcharSecao("W 150 x 13,0")).Ap > vb.Ap ? 1 : 0, 1);
            m = Novo(0, 0, 0, 3); b = B(m, 0, 1); b.Secao = "Tubo 50x50x2,00"; b.Material = "Aço ASTM A36"; Fixo(m.Nos[0]); m.Nos[1].ApoioX = true; m.Nos[1].Fy = -20;
            r = Solver.Resolver(m);
            Secao tq2 = Biblioteca.AcharSecao("Tubo 50x50x2,00");
            vb = Verificador.Barra(m, b, tq2);
            double lam = 3 / Math.Sqrt(tq2.Ix / tq2.A), lam1 = Math.Sqrt(2 * Math.PI * Math.PI * 200000 / 250);
            Conf("flambagem: esbeltez = L/r", vb.Esbeltez, lam);
            Conf("flambagem: Euler quando a esbeltez passa do limite", vb.Ncr, lam > lam1 ? Math.PI * Math.PI * 200000e3 * tq2.A / (lam * lam) : -1);
            Conf("flambagem: aproveitamento = N.n/Ncr", vb.ApFlamb, 20 * 2 / vb.Ncr);

            Console.WriteLine("-- vento NBR 6123:2023: fatores e forcas em barras");
            Conf("S2 cat. II classe A a 10 m (Tabela 3) = 1,00", Math.Round(VentoNbr.S2(2, 0, 10), 2), 1.00);
            Conf("S2 cat. IV classe A ate 5 m (Tabela 3) = 0,79", Math.Round(VentoNbr.S2(4, 0, 3), 2), 0.79);
            Conf("S2 cat. III classe B a 30 m (Tabela 3) = 1,03", Math.Round(VentoNbr.S2(3, 1, 30), 2), 1.03);
            Conf("S2 cat. V classe C a 100 m (Tabela 3) = 1,01", Math.Round(VentoNbr.S2(5, 2, 100), 2), 1.01);
            Conf("S2 cat. V constante ate 10 m (= 0,74)", Math.Round(VentoNbr.S2(5, 0, 4), 2), 0.74);
            Conf("S2 cat. I classe A a 250 m (Tabela 3) = 1,33", Math.Round(VentoNbr.S2(1, 0, 250), 2), 1.33);
            Conf("q = 0,613 Vk2 (Vk = 40 m/s -> 0,981 kN/m2)", VentoNbr.Q(40), 0.613 * 1600 / 1000);
            Conf("K faces planas, l/c = 20 (Tabela 28) = 0,81", VentoNbr.K(2, 20), 0.81);
            Conf("K faces planas, l/c = 15 (interpolado) = 0,75", VentoNbr.K(2, 15), 0.75);
            Conf("K circular subcritico, l/c = 2 = 0,58", VentoNbr.K(0, 2), 0.58);
            Conf("Ca circular subcritico (Tabela 27) = 1,2", VentoNbr.CaCircular(3e5), 1.2);
            Conf("Ca circular Re entre 8,4e5 e 2,3e6 = 0,7", VentoNbr.CaCircular(1e6), 0.7);
            Conf("classe pela maior dimensao: 35 m -> B", VentoNbr.ClassePorDimensao(35), 1);
            // pilar em balanco de tubo redondo 114,3 x 7,30, 6 m, V0 40, cat. III, classe A (automatica), S1 = S3 = 1
            m = Novo(0, 0, 0, 6); b = B(m, 0, 1); b.Secao = "Tubo " + (char)0xD8 + "114,3x7,30"; b.Material = "Aço ASTM A36"; Engaste(m.Nos[0]);
            var pv = new ParamVento { V0 = 40, S1 = 1, Categoria = 3, Classe = "", S3 = 1, UsarK = true };
            var lv = VentoNbr.Calcular(m, pv);
            double s2p = 0.94 * 1.0 * Math.Pow(0.6, 0.10), vkp = 40 * s2p, qp = 0.613 * vkp * vkp / 1000;
            Conf("vento no pilar: S2 a 6 m (cat. III, classe A)", lv[0].S2, s2p);
            Conf("vento no pilar: Re < 4,2e5 -> Ca = 1,2 (Tabela 27)", lv[0].Cf, 1.2);
            Conf("vento no pilar: l/c dobrado na base > 100 -> K = 1,0", lv[0].K, 1.0);
            Conf("vento no pilar: w = Ca q K d (kN/m)", lv[0].W, 1.2 * qp * 1.0 * 0.1143);
            // tubo de 203,2 mm a Vk ~ 35 m/s: Re acima do critico (Ca 0,6), mas o subcritico a Vcr da forca maior (8.1.3)
            Modelo mt = Novo(0, 0, 0, 6); Barra bt = B(mt, 0, 1); bt.Secao = "Tubo " + (char)0xD8 + "203,2x3,75"; Engaste(mt.Nos[0]);
            var lt = VentoNbr.Calcular(mt, new ParamVento { V0 = 40, S1 = 1, Categoria = 3, S3 = 1, UsarK = false });
            double vcr = 4.2e5 / (70000 * 0.2032), vkt = 40 * s2p;
            Conf("tubo 203: Re acima do critico", lt[0].Re >= 4.2e5 ? 1 : 0, 1);
            Conf("tubo 203: vale a forca subcritica 1,2.q(Vcr).d (> 0,6.q(Vk).d)", lt[0].W, Math.Max(1.2 * 0.613 * vcr * vcr / 1000, 0.6 * 0.613 * vkt * vkt / 1000) * 0.2032);
            int cv = VentoNbr.GarantirCaso(m, "Vento → (0°)");
            VentoNbr.Aplicar(m, lv, cv, +1);
            m.AtivarCaso(cv);
            r = Solver.Resolver(m);
            Conf("vento no pilar: reacao horizontal na base = -w.L (vento para +X)", m.Nos[0].Res[3], -lv[0].W * 6);
            Conf("vento no pilar: momento na base = w.L2/2", Math.Abs(m.Nos[0].Res[5]), lv[0].W * 18);
            m.AtivarCaso(0);
            Conf("caso G continua sem a carga de vento", b.QLocalIni, 0);
            // barra horizontal nao recebe vento no plano (vento paralelo ao eixo); barra a 45 graus recebe sen2 = 0,5
            m = Novo(0, 3, 4, 3, 0, 0, 3, 3); b = B(m, 0, 1); b.Secao = "W 200 x 26,6"; b2 = B(m, 2, 3); b2.Secao = "W 200 x 26,6";
            m.Nos[2].ApoioX = m.Nos[2].ApoioY = true;
            lv = VentoNbr.Calcular(m, pv);
            Conf("barra horizontal: forca do vento no plano = 0", lv[0].W, 0);
            Conf("barra a 45 graus: sen2(alfa) = 0,5", lv[1].Sen2, 0.5);
            Conf("perfil W com vento normal as mesas: c = d", lv[1].C, 0.207);

            Console.WriteLine("-- vento NBR 8400:1984: pressao, coeficientes (Tabelas 7, 8 e 10) e forca");
            Conf("Pa de servico, vw = 20 m/s: 250 N/m2 (Tabela 7)", Vento8400.PaServ(20), 250);
            Conf("Pa do vento maximo 0 a 20 m = 800", Vento8400.PaMax(10), 800);
            Conf("Pa do vento maximo 20 a 100 m = 1100", Vento8400.PaMax(50), 1100);
            Conf("Pa do vento maximo acima de 100 m = 1300", Vento8400.PaMax(150), 1300);
            Conf("C viga de alma cheia, l/h = 2 -> 1,2 (Tabela 8)", Vento8400.CAlmaCheia(2), 1.2);
            Conf("C viga de alma cheia, l/h = 7,5 -> 1,35 (interpolado)", Vento8400.CAlmaCheia(7.5), 1.35);
            Conf("C viga de alma cheia, l/h = 30 -> 1,6", Vento8400.CAlmaCheia(30), 1.6);
            Conf("C tubular d.raiz(Pa)/10 <= 1 -> 1,2", Vento8400.CTubular(0.1143, 250), 1.2);
            Conf("C tubular d.raiz(Pa)/10 > 1 -> 0,7", Vento8400.CTubular(1.0, 800), 0.7);
            Conf("Mx do grupo 5 = 1,12 (Tabela 10)", Vento8400.MxGrupo(5), 1.12);
            Conf("Mx do grupo 2 = 1,00 (Tabela 10)", Vento8400.MxGrupo(2), 1.00);
            m = Novo(0, 0, 0, 6); b = B(m, 0, 1); b.Secao = "Tubo " + (char)0xD8 + "114,3x7,30"; b.Material = "Aço ASTM A36"; Engaste(m.Nos[0]);
            var p84 = new ParamVento { Norma = "8400", VServ = 20 };
            var l84s = Vento8400.Calcular(m, p84, false); var l84m = Vento8400.Calcular(m, p84, true);
            Conf("8400 pilar tubular: w servico = C.Pa.d (kN/m)", l84s[0].W, 1.2 * 0.250 * 0.1143);
            Conf("8400 pilar tubular: w vento maximo (z <= 20 m) = C.800.d (kN/m)", l84m[0].W, 1.2 * 0.800 * 0.1143);
            Conf("8400 pilar tubular: sen2 = 1 (barra vertical)", l84s[0].Sen2, 1);
            Modelo m84 = Novo(0, 3, 4, 3, 0, 0, 3, 3); Barra b84 = B(m84, 0, 1); b84.Secao = "W 200 x 26,6"; Barra b85 = B(m84, 2, 3); b85.Secao = "W 200 x 26,6";
            m84.Nos[2].ApoioX = m84.Nos[2].ApoioY = true;
            var l84 = Vento8400.Calcular(m84, p84, false);
            Conf("8400 barra horizontal: forca do vento no plano = 0", l84[0].W, 0);
            Conf("8400 barra a 45 graus: w = C.Pa.c.sen2 (alma cheia, l/h = L/c)", l84[1].W, Vento8400.CAlmaCheia(Math.Sqrt(18) / 0.207) * 0.250 * 0.207 * 0.5);

            Console.WriteLine("-- biblioteca Gerdau: " + Biblioteca.Perfis.Count + " perfis " + Biblioteca.AvisoPerfis);
            Conf("perfis W e HP carregados", Biblioteca.DaFamilia("W e HP (Gerdau)", null).Count, 88);
            int ruins = 0;
            foreach (Secao s in Biblioteca.DaFamilia("W e HP (Gerdau)", null))
                if (Math.Abs(s.A * 7850 / s.Massa - 1) > 0.04) { ruins++; Console.WriteLine("   massa x area nao bate: " + s.Nome); }
            Conf("area coerente com a massa em todos", ruins, 0);

            Console.WriteLine("-- outras familias: " + string.Join(", ", Biblioteca.Familias.ToArray()) + " (" + Biblioteca.Perfis.Count + " perfis no total)");
            Conf("familias de perfil", Biblioteca.Familias.Count, 6);
            Secao tc = Biblioteca.AcharSecao("Tubo " + (char)0xD8 + "50,8x2,00");
            Conf("tubo redondo 50,8 x 2: area exata (cm2)", tc.A * 1e4, Math.Round(Math.PI / 4 * (50.8 * 50.8 - 46.8 * 46.8) / 100, 3));
            Conf("tubo redondo 50,8 x 2: inercia exata (cm4)", Math.Round(tc.Ix * 1e8, 3), Math.Round(Math.PI / 64 * (Math.Pow(50.8, 4) - Math.Pow(46.8, 4)) / 1e4, 3));
            Secao tq = Biblioteca.AcharSecao("Tubo 100x50x3,00");
            Conf("tubo 100x50x3: eixo forte maior que o fraco", tq.Ix > 2.5 * tq.Iy ? 1 : 0, 1);
            Conf("tubo 100x50x3: area entre 95% e 100% da de cantos vivos", tq.A * 1e4 > 0.95 * 8.64 && tq.A * 1e4 < 8.64 ? 1 : 0, 1);
            Secao cl = Biblioteca.AcharSecao("L 2\" x 1/4\"");
            Conf("cantoneira 2 x 1/4: Ix de tabela (cm4)", cl.Ix * 1e8, 14.6);
            m = Novo(0, 0, L, 0); b = B(m, 0, 1); b.Secao = "Tubo 100x50x3,00"; b.Material = "Aço ASTM A36"; Fixo(m.Nos[0]); m.Nos[1].ApoioY = true; b.QGlobalY = -1;
            r = Solver.Resolver(m);
            Conf("viga de tubo 100x50x3 calcula (flecha = 5qL4/384EI)", b.Res.V(L / 2), -5 * Math.Pow(L, 4) / (384 * 200000e3 * tq.Ix));

            Console.WriteLine("-- balanco com momento na ponta: verificacao usa o deslocamento da ponta");
            m = Novo(0, 0, 0, 5); b = B(m, 0, 1); b.Secao = "Tubo " + (char)0xD8 + "152,4x5,00"; b.Material = "Aço ASTM A36"; Engaste(m.Nos[0]); m.Nos[1].Mz = -4.36545;
            r = Solver.Resolver(m);
            Secao t152 = Biblioteca.AcharSecao(b.Secao);
            double eiT = 200000e3 * t152.Ix;
            Conf("deslocamento da ponta = ML2/2EI (m)", Math.Abs(m.Nos[1].Res[0]), 4.36545 * 25 / (2 * eiT));
            vb = Verificador.Barra(m, b, t152);
            Conf("verificacao: flecha = ML2/2EI (nao 1/4)", vb.Flecha, 4.36545 * 25 / (2 * eiT));
            Conf("verificacao: admissivel = H/250", vb.FlechaAdm, 5.0 / 250);
            Conf("verificacao: tensao = M/W (W = I/(D/2))", vb.Sigma, 4.36545 / (t152.Ix / 0.0762) / 1000);
            Conf("verificacao: perfil mais rigido muda a flecha na razao das inercias", Verificador.Barra(m, b, Biblioteca.AcharSecao("Tubo " + (char)0xD8 + "203,2x5,40")).Flecha,
                4.36545 * 25 / (2 * 200000e3 * Biblioteca.AcharSecao("Tubo " + (char)0xD8 + "203,2x5,40").Ix));

            Console.WriteLine("-- pilar engastado, travado lateralmente a 3 m, balanco de 2 m com momento no topo (guincho escorado)");
            m = Novo(0, 0, 0, 3, 0, 5); b = B(m, 0, 1); b2 = B(m, 1, 2); Engaste(m.Nos[0]); m.Nos[1].ApoioX = true; m.Nos[2].Mz = -4.36545;
            r = Solver.Resolver(m);
            double M0 = 4.36545, a = 3, c = 2, giro = M0 * a / (4 * EI);
            Conf("giro no apoio = M.a/4EI", Math.Abs(m.Nos[1].Res[2]), giro);
            Conf("momento na base = M/2", Math.Abs(b.Res.M(0)), M0 / 2);
            Conf("reacao lateral = 1,5.M/a", Math.Abs(m.Nos[1].Res[3]), 1.5 * M0 / a);
            Conf("topo = giro.c + M.c2/2EI (formula da planilha)", Math.Abs(m.Nos[2].Res[0]), giro * c + M0 * c * c / (2 * EI));
            b2.Material = "Aço ASTM A36";   // mesmo E, mas com fy para a verificacao
            Verif vb2 = Verificador.Barra(m, b2, new Secao { Nome = SEC, A = 0.01, Ix = 1e-4, Iy = 1e-4, AsX = 0.005, AsY = 0.005, H = 0.2, Bf = 0.2, Cx = 0.1, Cy = 0.1 });
            Conf("verificacao do balanco: flecha = deslocamento total do topo", vb2.Flecha, giro * c + M0 * c * c / (2 * EI));
            Conf("verificacao do balanco: admissivel = distancia ao apoio mais proximo / 250", vb2.FlechaAdm, 2.0 / 250);

            Console.WriteLine("-- caso do guincho modelado como no Estructto: pilar 5 m engastado + escora birrotulada a 3 m");
            m = Novo(0, 0, 0, 3, 0, 5, -2.5, 0); b = B(m, 0, 1); b2 = B(m, 1, 2); Barra esc = B(m, 3, 1);
            esc.RotulaInicio = esc.RotulaFim = true; esc.Secao = "A=10000000 I=1"; b.Secao = b2.Secao = "A=10000000 I=10000";   // axialmente rigidos: so flexao   // escora muito rigida axialmente
            Engaste(m.Nos[0]); Fixo(m.Nos[3]); m.Nos[2].Mz = -4.36545;
            r = Solver.Resolver(m);
            Conf("escora rigida = trava lateral: momento na base = M/2", Math.Abs(b.Res.M(0)), M0 / 2, 1e-3);
            Conf("topo = giro.c + M.c2/2EI", Math.Abs(m.Nos[2].Res[0]), giro * c + M0 * c * c / (2 * EI), 2e-3);
            b2.Material = "Aço ASTM A36";
            vb2 = Verificador.Barra(m, b2, new Secao { Nome = SEC, A = 0.01, Ix = 1e-4, Iy = 1e-4, AsX = 0.005, AsY = 0.005, H = 0.2, Bf = 0.2, Cx = 0.1, Cy = 0.1 });
            Conf("verificacao: admissivel = 5 m (base) / 250", vb2.FlechaAdm, 5.0 / 250);

            Console.WriteLine("-- pilar em balanco comprimido: flambagem com K = 2");
            m = Novo(0, 0, 0, 3); b = B(m, 0, 1); b.Secao = "Tubo 50x50x2,00"; b.Material = "Aço ASTM A36"; Engaste(m.Nos[0]); m.Nos[1].Fy = -5;
            r = Solver.Resolver(m);
            vb = Verificador.Barra(m, b, tq2);
            double lamK = 2 * 3 / Math.Sqrt(tq2.Ix / tq2.A);
            Conf("esbeltez = 2L/r", vb.Esbeltez, lamK);
            Conf("Ncr de Euler com 2L", vb.Ncr, Math.PI * Math.PI * 200000e3 * tq2.A / (lamK * lamK));
            Secao fino = Biblioteca.AcharSecao("Tubo " + (char)0xD8 + "139,7x1,50");
            Conf("tubo 139,7 x 1,5 em A572: avisa parede esbelta (D/t 93 > 64)", Verificador.Barra(m, b, fino).Aviso != "" ? 1 : 0, 1);
            Conf("tubo 50x50x2 em A36: sem aviso de parede", vb.Aviso.Contains("parede") ? 0 : 1, 1);
            Conf("pilar em balanco com KL/r = 306: avisa limite de 200", vb.Aviso.Contains("200") ? 1 : 0, 1);

            Guincho();

            Console.WriteLine();
            Console.WriteLine(falhas == 0 ? "TODOS OS " + total + " TESTES PASSARAM" : falhas + " de " + total + " FALHARAM");
            return falhas;
        }

        static void Sim(string nome, bool cond) { Conf(nome, cond ? 1 : 0, 1); }

        const string A36 = "Aço ASTM A36";
        const string COL = "Tubo Ø114,3x7,30", ESC = "Tubo 50x30x2,00", A572 = "Aço ASTM A572 Gr.50";

        // guincho de coluna escorada (exemplo guincho-coluna-escorada.estructto): coluna engastada 5 m, escora do no 4
        // (-2,5; 0) ao no 2 (0; 4), carga do guincho no topo Fy = -4,95 kN e Mz = -4,95 kN.m. divCol = trechos da coluna
        // entre a base e o no 2 (o trecho 2-3 sempre existe).
        static Modelo ModeloGuincho(int divCol = 1)
        {
            Modelo m = new Modelo();
            No n1 = new No { Id = 1, X = 0, Y = 0 }, n2 = new No { Id = 2, X = 0, Y = 4 }, n3 = new No { Id = 3, X = 0, Y = 5, Fy = -4.95, Mz = -4.95, Rotativa = true }, n4 = new No { Id = 4, X = -2.5, Y = 0 };
            Engaste(n1); Engaste(n4);
            m.Nos.AddRange(new[] { n1, n2, n3, n4 });
            n4.Placa = new PlacaBase { Chumbador = "Parabolt PB 1/4\" hef 35 mm", PeloPerfil = true, Sobra = 100, Esp = 19, Fileiras = 2, PorFileira = 2, Borda = 50, Diametro = 16, Classe = "8.8" };
            int idb = 1, idn = 5; int ant = 1;
            for (int k = 1; k < divCol; k++) { No x = new No { Id = idn++, X = 0, Y = 4.0 * k / divCol }; m.Nos.Add(x); m.Barras.Add(new Barra { Id = idb++, N1 = ant, N2 = x.Id, Membro = 1, Secao = COL, Material = A572, Dono = m }); ant = x.Id; }
            m.Barras.Add(new Barra { Id = idb++, N1 = ant, N2 = 2, Membro = 1, Secao = COL, Material = A572, Dono = m });
            m.Barras.Add(new Barra { Id = idb++, N1 = 2, N2 = 3, Membro = 1, Secao = COL, Material = A572, Dono = m });
            m.Barras.Add(new Barra { Id = 99, N1 = 4, N2 = 2, Secao = ESC, Material = A572, Dono = m });
            m.PesoProprio = true; m.Seguranca = 1.5; m.LimiteFlecha = 400;
            return m;
        }

        static void Guincho()
        {
            Console.WriteLine("-- guincho de coluna escorada: flambagem fora do plano, eixo fraco, inversao, placa inclinada, malha");
            Modelo m = ModeloGuincho();
            Resultado r = Solver.Resolver(m);
            Sim("modelo do guincho resolve", r.Ok);
            Barra col = m.Barras[0], esc = m.Barras.First(b => b.Id == 99);
            Secao sc = Biblioteca.AcharSecao(COL), se = Biblioteca.AcharSecao(ESC);
            No n4 = m.NoPorId(4);
            Console.WriteLine("   escora N = {0:0.000} kN; reacao no 4: Rx = {1:0.000}, Ry = {2:0.000}, M = {3:0.000}", esc.Res.N(0), n4.Res[3], n4.Res[4], n4.Res[5]);

            // P1: coluna sem travamento fora do plano = engastada-livre em 5 m
            Verif vc = Verificador.Barra(m, col, sc);
            Conf("P1 coluna fora do plano: K = 2 (balanco de 5 m)", vc.ForaPlano.K, 2, 1e-4);
            Conf("P1 coluna fora do plano: lambda = 2.5000/37,92 = 263,7", vc.ForaPlano.Lambda, 2 * 5 / Math.Sqrt(sc.Iy / sc.A), 1e-4);
            Sim("P1 coluna: KL/r > 200 -> NAO PASSA", !vc.Passa && vc.Critico.Contains("esbeltez"));
            Sim("P1 coluna: os dois planos foram calculados", vc.NoPlano != null && vc.ForaPlano != null && vc.NoPlano.Lambda < vc.ForaPlano.Lambda);
            // P6: no 2 travado no plano pela escora (elastico), nao por apoio
            Console.WriteLine("   coluna no plano: " + vc.NoPlano.Molas + " / Le = " + vc.NoPlano.Le + " Kteo " + vc.NoPlano.KTeorico + " Lref " + vc.NoPlano.Lref);
            Sim("P6 coluna no plano: no 2 e travamento elastico (mola da escora), nao rigido", vc.NoPlano.Molas.Contains("nó 2") && !vc.NoPlano.Travas.Contains("nó 2"));

            // P2: escora com o eixo certo em cada plano
            Verif ve = Verificador.Barra(m, esc, se);
            Conf("P2 escora: inercia no plano = Ix (97480 mm4)", ve.NoPlano.I * 1e12, 97480, 1e-6);
            Conf("P2 escora: inercia fora do plano = Iy (43680 mm4)", ve.ForaPlano.I * 1e12, 43680, 1e-6);
            Sim("P3 escora tracionada na posicao lancada (0 graus)", ve.Nc < 1e-6 && ve.Nt > 0);
            Sim("P3 escora so tracionada com carga giratoria: aviso de inversao", ve.AvisoInversao != "");
            Sim("P3 escora tracionada: L/r = 4717/12,1 = 389 > 300 -> NAO PASSA", !ve.Passa && ve.LimEsbeltez == 300);

            // P3: posicao oposta (180 graus): a escora comprime
            Modelo m2 = m.PosicaoOposta();
            Solver.Resolver(m2);
            Barra esc2 = m2.Barras.First(b => b.Id == 99);
            Verif ve2 = Verificador.Barra(m2, esc2, se);
            Sim("P3 posicao 180 graus: Mz do topo trocou de sinal", m2.NoPorId(3).Mz == 4.95 && m2.NoPorId(3).Fy == -4.95);
            Sim("P3 posicao 180 graus: escora comprimida", ve2.Nc > 1);
            Conf("P3 escora comprimida, sem travamento: fora do plano K = 2", ve2.ForaPlano.K, 2, 1e-4);
            Sim("P3 escora comprimida: NAO PASSA (KL/r > 200)", !ve2.Passa && ve2.Esbeltez > 200);
            esc2.TravFora = "2";
            Verif ve3 = Verificador.Barra(m2, esc2, se);
            double ry = Math.Sqrt(se.Iy / se.A);
            Conf("P2 escora travada no no 2: lambda = L/ry (Imin) = 4717/12,1", ve3.ForaPlano.Lambda, Math.Sqrt(2.5 * 2.5 + 16) / ry, 1e-4);
            Sim("P2 escora travada: lambda entre 370 e 400 (conta a mao: 383) -> NAO PASSA", ve3.ForaPlano.Lambda > 370 && ve3.ForaPlano.Lambda < 400 && !ve3.Passa);
            Sim("P3 escora comprimida: sem aviso de inversao", ve2.AvisoInversao == "");

            // P4: placa do no 4 nos eixos da placa = reacao do no
            ResPlaca rp = PlacaCalc.Calcular(m, n4);
            Conf("P4 placa no 4: V = |Rx| da reacao (nao o cortante local da barra)", rp.V, Math.Abs(n4.Res[3]), 1e-6);
            Conf("P4 placa no 4: N = -Ry", rp.N, -n4.Res[4], 1e-6);
            Conf("P4 placa no 4: M = -Rm", rp.M, -n4.Res[5], 1e-6);
            Sim("P4 placa no 4: V > 1 kN (antes saia 0,01)", rp.V > 1 && rp.VBarra < 0.1);
            Conf("P4/11 placa no 4: soma dos cortes nas fixacoes = |Rx|", rp.Fv * rp.Paraf.Count, Math.Abs(n4.Res[3]), 1e-6);
            double somaT = 0; foreach (double[] pf in rp.Paraf) somaT += pf[2];
            if (rp.Modelo.StartsWith("Todos")) Conf("P4/11 placa no 4: soma das tracoes = N (todas tracionadas)", somaT, rp.N, 1e-6);
            else Sim("P4/11 placa no 4: soma das tracoes >= N (o resto e compressao no concreto)", somaT >= rp.N - 1e-9);
            Sim("P4 barra inclinada sobre a placa: chapa/solda nao aprovadas", rp.Inclinada && !rp.Passa && rp.Aviso.Contains("inclinada"));
            // pilar vertical: o mesmo resultado de antes (eixos da placa = eixos locais)
            ResPlaca rp1; { Modelo mv = ModeloGuincho(); mv.NoPorId(1).Placa = n4.Placa.Copiar(); mv.NoPorId(4).Placa = null; Solver.Resolver(mv); rp1 = PlacaCalc.Calcular(mv, mv.NoPorId(1)); }
            Sim("P4 pilar vertical: eixos da placa = eixos locais (N, V, M iguais)", Math.Abs(rp1.N - rp1.NBarra) < 1e-9 && Math.Abs(rp1.V - rp1.VBarra) < 1e-9 && Math.Abs(rp1.M - rp1.MBarra) < 1e-9 && !rp1.Inclinada);

            // placa na parede (generico): barra horizontal saindo de um apoio lateral
            { Modelo mw = new Modelo(); mw.Nos.Add(new No { Id = 1, X = 0, Y = 0 }); mw.Nos.Add(new No { Id = 2, X = 2, Y = 0, Fy = -3 }); Engaste(mw.Nos[0]);
              mw.Barras.Add(new Barra { Id = 1, N1 = 1, N2 = 2, Secao = "Tubo 50x50x2,00", Material = A36, Dono = mw });
              mw.Nos[0].Placa = n4.Placa.Copiar(); Solver.Resolver(mw); ResPlaca rw = PlacaCalc.Calcular(mw, mw.Nos[0]);
              Sim("placa na parede: barra horizontal nao e inclinada; N = -Rx, V = |Ry| = 3", !rw.Inclinada && Math.Abs(rw.N + mw.Nos[0].Res[3]) < 1e-9 && Math.Abs(rw.V - 3) < 1e-6); }

            // P7: coeficiente do chumbador pelo tipo de carga
            Sim("P7 automatico com carga giratoria = dinamica, ALTO risco (Walsywa), n = 15", rp.TipoAuto && rp.TipoCarga == 2 && rp.RiscoAlto && rp.NExig == 15);
            n4.Placa.Risco = "baixo"; rp = PlacaCalc.Calcular(m, n4);
            Sim("P7 dinamica, baixo risco escolhido: n = 7", rp.NExig == 7 && !rp.RiscoAuto);
            n4.Placa.Risco = "";
            { Modelo ms = ModeloGuincho(); ms.NoPorId(3).Rotativa = false; Solver.Resolver(ms); ResPlaca rs = PlacaCalc.Calcular(ms, ms.NoPorId(4));
              Sim("P7 sem carga giratoria e sem escolha: variavel, baixo risco, n = 4, com aviso de risco", rs.TipoCarga == 1 && !rs.RiscoAlto && rs.NExig == 4 && rs.AvisoCoef.Contains("risco")); }
            n4.Placa.CoefChumbador = 3; n4.Placa.TipoCarga = "dinamica";
            rp = PlacaCalc.Calcular(m, n4);
            Sim("P7 n = 3 com carga dinamica: aviso de faixa (7 a 15)", rp.AvisoCoef != "" && rp.NExig == 3);

            // cones sobrepostos: reduz Tu em vez de reprovar; escolha automatica do chumbador
            {
                Conf("catalogo chumbadores: 76 itens carregados de chumbadores.csv", Chumbadores.Lista.Count, 76);
                Sim("Achar por codigo == Achar por nome", Chumbadores.Achar("CB 12300") != null && Chumbadores.Achar("CB 12300") == Chumbadores.Achar("Walsywa CB 1/2\" hef 76 mm"));
                Modelo mp = ModeloGuincho(); mp.NoPorId(4).Placa = null;
                mp.NoPorId(1).Placa = new PlacaBase { Chumbador = "Walsywa WB 5/8\" hef 148 mm", PeloPerfil = true, Sobra = 100, Esp = 12.7, Fileiras = 2, PorFileira = 2, Borda = 50 };
                Modelo mo = mp.PosicaoOposta(); Solver.Resolver(mo);
                ResPlaca rc = PlacaCalc.Calcular(mo, mo.NoPorId(1));
                Chumbador wb = Chumbadores.Achar("Walsywa WB 5/8\" hef 148 mm");
                double lado = rc.EspacMin + 3 * 148;   // dois cones de 444 mm com centros a EspacMin
                Conf("cones: psi = area da uniao / (nt.9.hef2)", rc.PsiCone, rc.Paraf.Count(q => q[2] > 1e-9) == 4 ? lado * lado / (4 * 9 * 148.0 * 148) : rc.PsiCone, 1e-9);
                Sim("cones: espacamento 220 < 3.hef = 444 reduz Tu (psi < 1) mas nao reprova sozinho", rc.PsiCone < 1 && rc.EspacOk);
                Conf("cones: Tu efetivo = Tu.psi", rc.TuEf, wb.TuKN * rc.PsiCone, 1e-12);
                Conf("mecanico WB 5/8 hef 148: NComb exato de antes", rc.NComb, Math.Pow(Math.Pow(rc.Tmax / (wb.TuKN * rc.PsiCone), 5.0 / 3) + Math.Pow(rc.Fv / wb.VuKN, 5.0 / 3), -3.0 / 5), 1e-12);
                Conf("cones: espacamento >= 3.hef da psi = 1", Chumbadores.FatorCones(wb, new List<double[]> { new[] { -300.0, 0, 1 }, new[] { 300.0, 0, 1 } }), 1, 1e-12);
                string escolha = PlacaCalc.DimensionarChumbador(mo, mo.NoPorId(1));
                ResPlaca rd = PlacaCalc.Calcular(mo, mo.NoPorId(1));
                Console.WriteLine("   dimensionamento automatico: " + escolha + " · n = " + rd.NComb.ToString("0.00") + " >= " + rd.NRef);
                Sim("dimensionamento automatico acha um chumbador que passa", escolha != "" && rd.ChOk);

                // quimico WQE 500 5/8"
                mo.NoPorId(1).Placa = new PlacaBase { Chumbador = "WQE 500 5/8\"", PeloPerfil = true, Sobra = 100, Esp = 12.7, Fileiras = 2, PorFileira = 2, Borda = 50 };
                ResPlaca rq = PlacaCalc.Calcular(mo, mo.NoPorId(1));
                Conf("quimico WQE 500 5/8: FFck com fck 20 = raiz(20/28)", rq.FFck, Math.Sqrt(20.0 / 28.0), 1e-12);
                Conf("quimico WQE 500 5/8: TuEf = 6595*0.00980665*raiz(20/28)*psi", rq.TuEf, 6595 * 0.00980665 * Math.Sqrt(20.0 / 28.0) * rq.PsiCone, 1e-12);
                double sigB = rq.Tmax * 1000 / 145.8, tauB = rq.Fv * 1000 / 130.3, vmB = Math.Sqrt(sigB * sigB + 3 * tauB * tauB);
                Conf("quimico WQE 500 5/8: NAco = Sp/vm com classe 4.6 (Sp = 225)", rq.NAco, 225.0 / vmB, 1e-12);

                // quimico WQA 1/2" (so carga recomendada)
                mo.NoPorId(1).Placa = new PlacaBase { Chumbador = "WQA 1/2\"", PeloPerfil = true, Sobra = 100, Esp = 12.7, Fileiras = 2, PorFileira = 2, Borda = 100 };
                ResPlaca rwqa = PlacaCalc.Calcular(mo, mo.NoPorId(1));
                Conf("quimico WQA 1/2: NRef == 1", rwqa.NRef, 1, 1e-12);
                Conf("quimico WQA 1/2: Tref = 1019.72 kgf em kN (psi = " + rwqa.PsiCone.ToString("0.000") + ")", rwqa.Tref / rwqa.PsiCone, 1019.72 * 0.00980665, 1e-12);

                // mesma conta da planilha (aba COLUNA ESCORADA, mesmas forcas por chumbador, psi = 1): os dois tem de bater
                PlacaBase pp = new PlacaBase { Classe = "8.8" };   // classe do parafuso NAO vale para a barra do quimico (padrao 4.6)
                Func<string, double, double, ResPlaca> cand = (nome, t, v) => PlacaCalc.CandidatoChumbador(
                    new ResPlaca { NExig = 15, NSeg = 1.5, Tmax = t, Fv = v, Paraf = new List<double[]> { new[] { 0.0, 0, t } } }, pp, null, Chumbadores.Achar(nome));
                ResPlaca x1 = cand("WQE 500 5/8\"", 3.9677, 0.9289), x2 = cand("WQA 1/2\"", 2.4770, 1.8578), x3 = cand("CBPL 14300", 3.9677, 0.9289);
                Conf("= planilha: base WQE 500 5/8 n concreto 13,78", x1.NComb, 13.78, 5e-4);
                Conf("= planilha: base WQE 500 5/8 n aco (4.6) 7,53", x1.NAco, 7.53, 5e-4);
                Conf("= planilha: pe WQA 1/2 n concreto 3,08 (n,ref = 1)", x2.NComb, 3.08, 2e-3);
                Conf("= planilha: pe WQA 1/2 n aco 4,68", x2.NAco, 4.68, 2e-3);
                Conf("= planilha: base CBPL 14300 (mecanico) n 3,786", x3.NComb, 3.786, 2e-4);
                Sim("barra do quimico usa 4.6 mesmo com parafuso 8.8", x1.ClasseBarra == "4.6" && x1.SpBarra == 225);
            }
            n4.Placa.CoefChumbador = 0; n4.Placa.TipoCarga = "";

            // P5: a divisao da coluna nao muda flambagem nem flecha
            double[] lp = new double[3], lf = new double[3], top = new double[3], ap = new double[3];
            int[] divs = { 1, 2, 5 };
            for (int i = 0; i < 3; i++)
            {
                Modelo md = ModeloGuincho(divs[i]); Solver.Resolver(md);
                Barra b0 = md.Barras[0];
                Verif vd = Verificador.Barra(md, b0, sc);
                lp[i] = vd.NoPlano.Lambda; lf[i] = vd.ForaPlano.Lambda; top[i] = md.NoPorId(3).Res[0];
                ap[i] = md.Barras.Where(b => b.Membro == 1).Max(b => Verificador.Barra(md, b, sc).ApFlamb + Verificador.Barra(md, b, sc).ApEsbeltez);
            }
            for (int i = 1; i < 3; i++)
            {
                Conf("P5 coluna em " + divs[i] + " trechos: lambda no plano igual", lp[i], lp[0], 1e-9);
                Conf("P5 coluna em " + divs[i] + " trechos: lambda fora do plano igual", lf[i], lf[0], 1e-9);
                Conf("P5 coluna em " + divs[i] + " trechos: deslocamento do topo igual", top[i], top[0], 1e-9);
                Conf("P5 coluna em " + divs[i] + " trechos: aproveitamento de flambagem igual", ap[i], ap[0], 1e-9);
            }
            // pilar simples em balanco, 1/2/5/10 trechos
            double l0 = 0;
            foreach (int nd in new[] { 1, 2, 5, 10 })
            {
                Modelo mb = new Modelo();
                for (int k = 0; k <= nd; k++) mb.Nos.Add(new No { Id = k + 1, X = 0, Y = 3.0 * k / nd });
                Engaste(mb.Nos[0]); mb.Nos[nd].Fy = -5;
                for (int k = 0; k < nd; k++) mb.Barras.Add(new Barra { Id = k + 1, N1 = k + 1, N2 = k + 2, Membro = nd > 1 ? 1 : 0, Secao = "Tubo 50x50x2,00", Material = "Aço ASTM A36", Dono = mb });
                Solver.Resolver(mb);
                Verif vb = Verificador.Barra(mb, mb.Barras[nd - 1], Biblioteca.AcharSecao("Tubo 50x50x2,00"));
                if (nd == 1) l0 = vb.Esbeltez; else Conf("P5 balanco em " + nd + " trechos: KL/r igual ao de 1 trecho", vb.Esbeltez, l0, 1e-9);
            }

            // K teoricos classicos (autovalor) e minimo K = 1 entre travamentos
            Func<bool, bool, bool, bool, Flamb> classico = (g0, r1, g1, l1) =>
            {
                Modelo mk = new Modelo();
                mk.Nos.Add(new No { Id = 1, X = 0, Y = 0, ApoioX = true, ApoioY = true, ApoioGiro = g0 });
                mk.Nos.Add(new No { Id = 2, X = 0, Y = 3, ApoioX = r1, ApoioGiro = g1, Fy = -1 });
                mk.Barras.Add(new Barra { Id = 1, N1 = 1, N2 = 2, Secao = "Tubo 50x50x2,00", Material = "Aço ASTM A36", Dono = mk });
                Solver.Resolver(mk);
                return Verificador.Barra(mk, mk.Barras[0], Biblioteca.AcharSecao("Tubo 50x50x2,00")).NoPlano;
            };
            Conf("autovalor: biarticulada K = 1", classico(false, true, false, false).KTeorico, 1, 1e-5);
            Conf("autovalor: engastada-livre K = 2", classico(true, false, false, false).KTeorico, 2, 1e-5);
            Conf("autovalor: engastada-articulada K = 0,699", classico(true, true, false, false).KTeorico, 0.69915, 1e-4);
            Conf("autovalor: biengastada K = 0,5", classico(true, true, true, false).KTeorico, 0.5, 1e-5);
            Conf("biengastada: K adotado = 1 (nao conta com engaste perfeito)", classico(true, true, true, false).K, 1, 1e-9);

            // P3: modelo sem compressao com carga giratoria -> aviso
            Modelo mt = new Modelo();
            mt.Nos.Add(new No { Id = 1, X = 0, Y = 3 }); mt.Nos.Add(new No { Id = 2, X = 0, Y = 0, Fy = -5, Rotativa = true });
            Engaste(mt.Nos[0]);
            mt.Barras.Add(new Barra { Id = 1, N1 = 1, N2 = 2, Secao = "Tubo 50x50x2,00", Material = "Aço ASTM A36", Dono = mt });
            Solver.Resolver(mt);
            Verif vt = Verificador.Barra(mt, mt.Barras[0], Biblioteca.AcharSecao("Tubo 50x50x2,00"));
            Sim("P3 barra pendurada com carga giratoria: aviso de inversao", vt.Nc < 1e-6 && vt.AvisoInversao.Contains("inverter"));

            // 10: KL/r > 200 agora reprova no motor (antes so o selo do memorial reprovava)
            Modelo me = new Modelo();
            me.Nos.Add(new No { Id = 1, X = 0, Y = 0 }); me.Nos.Add(new No { Id = 2, X = 0, Y = 6, Fy = -0.1 });
            Engaste(me.Nos[0]);
            me.Barras.Add(new Barra { Id = 1, N1 = 1, N2 = 2, Secao = "Tubo 50x50x2,00", Material = "Aço ASTM A36", Dono = me });
            Solver.Resolver(me);
            Verif vE = Verificador.Barra(me, me.Barras[0], Biblioteca.AcharSecao("Tubo 50x50x2,00"));
            Sim("10 KL/r > 200 com aproveitamento baixo: motor NAO PASSA", vE.ApFlamb < 1 && !vE.Passa);

            // solda: perna minima normativa pela parte mais fina (tubo 7,3 mm, chapa 8 mm -> 5 mm)
            Conf("solda: perna minima NBR 8800 Tab.10 (t = 7,3 mm) = 5 mm", NovoFtool.Solda.PernaMinimaNorma(7.3), 5);
            var sd3 = NovoFtool.Solda.Calcular(sc, false, -8.87, 0.5, 2.32, 3, "E70", 8);
            Sim("solda: perna 3 mm em tubo 7,3 / chapa 8 -> abaixo do minimo, NAO PASSA mesmo com tensao baixa", sd3.Ap < 1 && sd3.AbaixoMinimo && !sd3.Passa);
            Sim("solda: Minima() nunca devolve perna abaixo da normativa", NovoFtool.Solda.Minima(sc, false, -8.87, 0.5, 2.32, "E70", 8) >= 5);
            Conf("solda: n equivalente = 0,577.FEXX/tau (E70: FEXX = 145/0,30)", sd3.NEquiv, 0.577 * (145 / 0.30) / sd3.Tau, 1e-9);
            // E5: diagnostico da solda reprovada (por que e o que fazer)
            var dg1 = NovoFtool.Solda.Diagnosticar(sc, false, -8.87, 0.5, 2.32, 3, "E70", 8);
            Sim("solda diag: perna 3 abaixo do minimo -> sugere a menor perna que passa (5 mm, mesmo eletrodo)", !dg1.Passa && dg1.Perna == 5 && dg1.Eletrodo == "E70" && dg1.Porque.Contains("mínimo"));
            Sim("solda diag: a sugestao passa de fato no Calcular", NovoFtool.Solda.Calcular(sc, false, -8.87, 0.5, 2.32, dg1.Perna, dg1.Eletrodo, 8).Passa);
            Secao tuboFino = Biblioteca.AcharSecao("Tubo 30x20x1,06");
            var dg2 = NovoFtool.Solda.Diagnosticar(tuboFino, false, 0.5, 0.3, 0.05, 3, "E70", 6.35);
            Sim("solda diag: tubo 30x20x1,06 -> parede 1,06 mm < perna minima 3 mm, sem solucao, perna max ~1,06", dg2.SemSolucao && Math.Abs(dg2.PernaMax - 1.06) < 1e-9 && dg2.Porque.Contains("1,06"));
            var dg3 = NovoFtool.Solda.Diagnosticar(sc, false, -8.87, 0.5, 2.32, 5, "E70", 8);
            Sim("solda diag: solda que passa nao tem diagnostico", dg3.Passa && dg3.OQueFazer == "");
            // 7: interacao N + M com amplificacao
            Modelo m3 = m.PosicaoOposta(); Solver.Resolver(m3);
            Verif vi = Verificador.Barra(m3, m3.Barras[0], sc);
            Sim("7 interacao: B = 1/(1 - n.Nc/Ne) > 1 e parcela de momento amplificada", vi.Nc > 0 && vi.BAmp > 1 && vi.ApInteracao > vi.ApFlamb);
            Conf("7 interacao: valor = n.Nc/Ncr,min + B.n.Mmax/(W.fy)", vi.ApInteracao, 1.5 * vi.Nc / Math.Min(vi.NoPlano.Ncr, vi.ForaPlano.Ncr) + vi.BAmp * 1.5 * vi.Mmax / (sc.Wx * 345 * 1000), 1e-9);

            // 11: equilibrio local do no 2 (soma das forcas das barras sobre o no = 0, sem carga no no)
            double fx = 0, fy = 0, mz = 0;
            foreach (Barra b in m.Barras.Where(b => b.N1 == 2 || b.N2 == 2))
            {
                int k0 = b.N1 == 2 ? 0 : 3; ResBarra rb = b.Res;
                fx += rb.C * rb.F[k0] - rb.S * rb.F[k0 + 1]; fy += rb.S * rb.F[k0] + rb.C * rb.F[k0 + 1]; mz += rb.F[k0 + 2];
            }
            Conf("11 equilibrio do no 2: soma Fx = 0", fx, 0, 1e-6); Conf("11 equilibrio do no 2: soma Fy = 0", fy, 0, 1e-6); Conf("11 equilibrio do no 2: soma M = 0", mz, 0, 1e-6);

            // 12: comparacao de fixacoes (fixacoes.csv, fixacoes-pares.csv e PlacaCalc.CandidatoChumbador)
            Console.WriteLine("-- comparacao de fixacoes: fixacoes.csv, fixacoes-pares.csv e CandidatoChumbador");
            Conf("fixacoes.csv: 12 linhas carregadas (9 calculaveis + 3 informativas)", Fixacoes.Linhas.Count, 12);
            Conf("fixacoes-pares.csv: 11 pares da secao 5.2 carregados", Fixacoes.Pares.Count, 11);
            Conf("fixacoes informativas (WLT, Golpe de Metal, PU 500)", Fixacoes.Informativas().Count, 3);
            int semLinha = 0;
            foreach (string linCh in Chumbadores.Linhas())
            {
                FixacaoLinha fl = Fixacoes.AcharLinha(linCh);
                if (fl == null || fl.Informativa || string.IsNullOrEmpty(fl.Familia)) semLinha++;
            }
            Conf("fixacoes.csv cobre todas as linhas de chumbadores.csv", semLinha, 0);
            Sim("Indufix Parabolt PB marcado como sem descricao de catalogo", Fixacoes.AcharLinha("Parabolt PB") != null && Fixacoes.AcharLinha("Parabolt PB").SemCatalogo);
            Sim("WLT, Golpe de Metal e PU 500 sao so informativas", Fixacoes.AcharLinha("Walsywa WLT").Informativa && Fixacoes.AcharLinha("Walsywa Golpe de Metal").Informativa && Fixacoes.AcharLinha("Walsywa PU 500").Informativa);

            {
                Modelo mc = ModeloGuincho().PosicaoOposta();
                mc.NoPorId(4).Placa = null;
                PlacaBase pc = new PlacaBase { Chumbador = "CB 12300", PeloPerfil = true, Sobra = 100, Esp = 12.7, Fileiras = 2, PorFileira = 2, Borda = 50 };
                mc.NoPorId(1).Placa = pc;
                Solver.Resolver(mc);
                ResPlaca rBase = PlacaCalc.Calcular(mc, mc.NoPorId(1));
                string[] cods = { "CB 12300", "WB 12400", "WQE 500 1/2\"", "WQA 1/2\"", "PB 1/2\" hef 80" };
                List<ColunaComparacao> cols = new List<ColunaComparacao>();
                foreach (string cod in cods)
                {
                    Chumbador ch = Chumbadores.Achar(cod);
                    ResPlaca esp = PlacaCalc.CandidatoChumbador(rBase, pc, mc, ch);
                    ColunaComparacao cc = Fixacoes.Avaliar(cod, rBase, pc, mc);
                    cols.Add(cc);
                    Conf("comparacao n == CandidatoChumbador (" + cod + ")", cc.Res.NComb, esp.NComb, 1e-12);
                    Conf("comparacao Tref == CandidatoChumbador (" + cod + ")", cc.Res.Tref, esp.Tref, 1e-12);
                    Conf("comparacao ApFix == CandidatoChumbador (" + cod + ")", cc.Res.ApFix, esp.ApFix, 1e-12);
                    if (ch.Quimico) Conf("comparacao NAco == CandidatoChumbador (" + cod + ")", cc.Res.NAco, esp.NAco, 1e-12);
                }
                ColunaComparacao cWlt = Fixacoes.Avaliar("Walsywa WLT", rBase, pc, mc);
                Sim("linha informativa WLT nao gera verificacao estrutural (Res == null)", cWlt != null && cWlt.Info.Informativa && cWlt.Res == null);
                List<ParFixacao> pares3 = Fixacoes.ParesPara(new[] { "Walsywa CB", "Walsywa WB", "Walsywa WQE 500" });
                Sim("pares para CB x WB x WQE 500 incluem CB x WB e Mecanico x Quimico",
                    pares3.Any(p => p.Titulo.Contains("Walsywa CB × Walsywa WB")) && pares3.Any(p => p.Titulo.Contains("Mecânico por torque")));
                List<string> avs = Fixacoes.Avisos(cols.Take(3), rBase);
                Sim("avisos para CB 12300 x WB 12400 x WQE 500 1/2 cobrem ruptura/recomendada, fck 28 MPa e corte",
                    avs.Count >= 3 && avs.Any(a => a.Contains("Ruptura")) && avs.Any(a => a.Contains("28 MPa")) && avs.Any(a => a.Contains("cisalhamento")));
            }
        }

        static double MaxM(Barra b)
        {
            double mn, mx;
            b.Res.Extremos(2, out mn, out mx);
            return mx;
        }
    }
}
