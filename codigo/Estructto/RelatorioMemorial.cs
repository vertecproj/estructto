// Relatorio completo: memorial com o desenvolvimento de TODAS as contas, para conferir a mao.
// Cada verificacao e um bloco: titulo + selo (passa / nao passa), formula, valores substituidos (N e mm), limite
// e aproveitamento. Usa exatamente as mesmas formulas do programa (Solver, Verificador, PlacaCalc, Solda).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Portico;

namespace NovoFtool
{
    public partial class MainWindow
    {
        static string Fm(double v, int c = 2) { return double.IsInfinity(v) ? "∞" : double.IsNaN(v) ? "—" : Num.Fmt(v, c); }

        static readonly Brush Verde = TemaPlant.BImpVerde, Vermelho = TemaPlant.BImpVermelho;
        static readonly Brush FundoBloco = TemaPlant.BImpFundoBloco, AzulEsc = TemaPlant.BImpAzulEsc;
        static readonly FontFamily Mat = new FontFamily("Cambria Math"), Mono = new FontFamily("Consolas");

        // ---------- pecas de formatacao ----------

        static Paragraph Secao2(string s) { return new Paragraph(new Run(s)) { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = AzulEsc, Margin = new Thickness(0, 14, 0, 4) }; }
        static Paragraph Texto(string s) { return new Paragraph(new Run(s)) { Foreground = Cinza, Margin = new Thickness(0, 0, 0, 4) }; }

        class Bloco
        {
            public Section S;
            Paragraph cab;
            public Bloco(string titulo)
            {
                S = new Section { Background = FundoBloco, BorderBrush = Linha, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 8) };
                cab = new Paragraph(new Run(titulo) { FontWeight = FontWeights.SemiBold, FontSize = 11.5 }) { Margin = new Thickness(0, 0, 0, 4) };
                S.Blocks.Add(cab);
            }
            // selo no titulo e cor da faixa da esquerda
            public void Selo(bool? ok, string txt = null)
            {
                if (ok == null) return;
                S.BorderBrush = ok.Value ? Verde : Vermelho;
                cab.Inlines.Add(new Run("   " + (txt ?? (ok.Value ? "✓ PASSA" : "✗ NÃO PASSA"))) { Foreground = ok.Value ? Verde : Vermelho, FontWeight = FontWeights.Bold, FontSize = 10.5 });
            }
            public Bloco Formula(string s) { S.Blocks.Add(new Paragraph(new Run(s)) { FontFamily = Mat, FontSize = 13, Foreground = AzulEsc, Margin = new Thickness(8, 1, 0, 1) }); return this; }
            public Bloco Valores(string s) { S.Blocks.Add(new Paragraph(new Run(s)) { FontFamily = Mono, FontSize = 9.8, Margin = new Thickness(8, 0, 0, 1) }); return this; }
            public Bloco Nota(string s) { S.Blocks.Add(new Paragraph(new Run(s)) { FontSize = 9.5, Foreground = Cinza, Margin = new Thickness(8, 1, 0, 1) }); return this; }
            public Bloco Resultado(string s, bool? ok = null)
            {
                var p = new Paragraph { Margin = new Thickness(8, 3, 0, 0) };
                p.Inlines.Add(new Run(s) { FontWeight = FontWeights.SemiBold, Foreground = ok == null ? Tinta : ok.Value ? Verde : Vermelho });
                S.Blocks.Add(p); return this;
            }
            public Bloco Tab(Table t) { S.Blocks.Add(t); return this; }

            // equacao em LaTeX desenhada de verdade (frações, raízes, índices), como num livro
            public Bloco Eq(string latex)
            {
                UIElement el;
                try
                {
                    var fc = new WpfMath.Controls.FormulaControl { Formula = latex, Scale = 15, Foreground = AzulEsc, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
                    el = fc;
                }
                catch { el = new System.Windows.Controls.TextBlock { Text = latex, FontFamily = Mono, FontSize = 10 }; }   // formula com erro: mostra o texto para nao perder a conta
                S.Blocks.Add(new BlockUIContainer(el) { Margin = new Thickness(10, 3, 0, 3) });
                return this;
            }
        }

        // numero no formato do LaTeX (virgula decimal sem espaco extra)
        static string L(double v, int c = 2) { return Fm(v, c).Replace(",", "{,}").Replace("∞", "\\infty").Replace("—", "-"); }
        static string Un(string u) { return "\\;\\mathrm{" + u + "}"; }

        // tabela de dados: grandeza | simbolo | valor | unidade | origem
        static Table Dados(IEnumerable<string[]> linhas) { return Tabela(new[] { "Grandeza", "Símbolo", "Valor", "Unidade", "Origem" }, linhas); }

        static string Pct(double ap) { return Fm(ap * 100, 0) + "%"; }

        // ---------- memorial ----------

        void Memorial(FlowDocument doc, List<No> placas)
        {
            doc.Blocks.Add(Titulo("9. Memorial de cálculo"));
            doc.Blocks.Add(Texto("Cada verificação mostra a fórmula, os valores substituídos e o resultado. Nas contas de tensão, as forças vão em N e as medidas em mm, de modo que N/mm² = MPa. " +
                                 "Coeficiente de segurança das barras e da chapa: n = " + Fm(M.Seguranca) + ". Flecha limite: L/" + Fm(M.LimiteFlecha, 0) + ". Resultado: " + NomeResultado() +
                                 (resCasos != null && resCasos.Rotativa ? " — com carga giratória: cada resultado nas posições 0° e 180° (envoltória)" : "") + "."));
            var rot = M.Nos.Where(x => x.Rotativa).ToList();
            if (rot.Count > 0 || M.Psi > 0)
            {
                var dd = new Bloco("Dados do equipamento");
                dd.Tab(Dados(new[]
                {
                    new[] { "Nós com carga giratória", "—", rot.Count > 0 ? string.Join(", ", rot.Select(x => x.Id)) : "nenhum", "", "\"Carga giratória (guincho)\" no nó" },
                    new[] { "Posições analisadas", "—", rot.Count > 0 ? "0° e 180° (Fx e Mz com sinal trocado)" : "—", "", "pórtico plano: 90°/270° (momento fora do plano) não analisadas" },
                    new[] { "Coef. dinâmico já incluído nas cargas", "ψ", M.Psi > 0 ? Fm(M.Psi, 2) : "NÃO INFORMADO", "—", M.Psi > 0 ? "NBR 8400 (içamento), declarado pelo usuário; o programa não multiplica de novo" : "confirme se a carga do equipamento já inclui ψ (NBR 8400)" },
                }));
                if (M.Psi <= 0 && rot.Count > 0) { dd.Nota("⚠ Há carga giratória, mas o coeficiente dinâmico ψ não foi declarado: o memorial não sabe se a carga lançada já está majorada."); dd.S.BorderBrush = TemaPlant.BImpCarga; }
                doc.Blocks.Add(dd.S);
            }

            if (M.Vento != null) MemorialVento(doc);
            MemorialAnalise(doc);
            doc.Blocks.Add(Secao2("9.3 Verificação das barras"));
            foreach (Barra b in M.Barras) MemorialBarra(doc, b);
            if (placas.Count > 0)
            {
                doc.Blocks.Add(Secao2("9.4 Placas de base"));
                foreach (No n in placas) MemorialPlaca(doc, n);
            }
            doc.Blocks.Add(Secao2("9.5 Limitações (o que este memorial NÃO verifica)"));
            foreach (string lim in new[]
            {
                "Verificação simplificada em tensões admissíveis; não substitui a NBR 8800 (estados-limite).",
                "Pórtico plano: cargas, momentos e vento fora do plano não são analisados; equipamento giratório só nas posições 0° e 180°.",
                "Flambagem por barra contínua nos dois planos, com compressão uniforme igual à maior da barra (conservador). Fora do plano só travam os apoios e os nós declarados. Apoio do pórtico considerado igual nos dois planos (giro preso nos dois).",
                "Sem análise de 2ª ordem global (P-Δ); só a amplificação local B = 1/(1 − nN/Ne) na interação N + M, com Cm = 1.",
                "Sem flambagem lateral com torção (FLT), sem torção e sem flambagem local calculada (parede esbelta só avisada).",
                "Placa no plano do apoio (piso, teto ou parede: o eixo global mais próximo da barra); barra inclinada sobre a placa: chapa e solda não verificadas. Pressão no concreto, punção da laje e distância do chumbador à borda do concreto não verificadas.",
                "Cargas de chumbador = cargas últimas médias de ensaio do fabricante; coeficiente pelo tipo de carga e grau de risco (Walsywa, baixo / alto: estática 3 / 5, variável 4 / 5, dinâmica 7 / 15, choque 5 / 10).",
            }) doc.Blocks.Add(Texto("• " + lim));
        }

        // 9.1 e 9.2: metodo da rigidez, coeficientes de cada barra, esforcos nas pontas e equilibrio global
        // Vento: as mesmas contas do motor (VentoNbr), com os dados digitados na janela do vento.
        void MemorialVento(FlowDocument doc)
        {
            ParamVento p = M.Vento;
            int classe = VentoNbr.Classe(M, p);
            var lista = VentoNbr.Calcular(M, p);
            var b1 = new Bloco("Vento — ABNT NBR 6123:2023 (barras prismáticas expostas, seção 8.1)");
            b1.Tab(Dados(new[]
            {
                new[] { "Velocidade básica", "V0", Fm(p.V0, 1), "m/s", "Figura 1 (isopletas), informada pelo usuário" },
                new[] { "Fator topográfico", "S1", Fm(p.S1, 2), "—", "5.2" },
                new[] { "Categoria de rugosidade", "—", VentoNbr.Categorias[Math.Max(1, Math.Min(5, p.Categoria)) - 1], "—", "5.3.1" },
                new[] { "Classe", "—", VentoNbr.Classes[classe], "—", string.IsNullOrEmpty(p.Classe) ? "5.3.2, pela maior dimensão do pórtico" : "5.3.2, escolhida pelo usuário" },
                new[] { "Fator estatístico", "S3", Fm(p.S3, 2), "—", "Tabela 4" },
                new[] { "Fator de comprimento finito", "K", p.UsarK ? "Tabela 28" : "1,00", "—", p.UsarK ? "8.1.3 (l/c dobrado na barra que toca um apoio)" : "desligado (lado seguro)" },
            }));
            b1.Eq("V_k = V_0 \\, S_1 \\, S_2 \\, S_3 \\qquad S_2 = b_m \\, F_r \\left(\\frac{z}{10}\\right)^{p} \\qquad q = 0{,}613 \\, V_k^2");
            b1.Eq("w = C_f \\, q \\, K \\, c \\, \\mathrm{sen}^2\\alpha \\quad (\\mathrm{kN/m,\\;normal\\;à\\;barra})");
            b1.Nota("z = cota do ponto mais alto da barra acima do terreno; c = dimensão de referência da seção (Tabela 26, Nota 1); α = ângulo entre o vento (horizontal, no plano do pórtico) e o eixo da barra. " +
                    "As cargas estão nos casos \"" + MainWindow.CasoVentoDir + "\" e \"" + MainWindow.CasoVentoEsq + "\" (mesmo valor, sentidos opostos), que nunca entram juntos numa combinação.");
            b1.Tab(Tabela(new[] { "Barra", "z (m)", "S2", "Vk (m/s)", "q (N/m²)", "Cf", "c (mm)", "K", "sen²α", "w (kN/m)" },
                lista.Select(v => new[] { v.B.Id.ToString(), Fm(v.Z, 2), Fm(v.S2, 3), Fm(v.Vk, 1), Fm(v.Q * 1000, 0), Fm(v.Cf, 2), Fm(v.C * 1000, 0), Fm(v.K, 2), Fm(v.Sen2, 2), Math.Abs(v.W) < 1e-9 ? "—" : Fm(v.W, 3) })));
            foreach (var v in lista.Where(x => Math.Abs(x.W) > 1e-9)) b1.Nota("Barra " + v.B.Id + " (" + v.B.Secao + "): " + v.Fonte + ".");
            b1.Nota("Não incluído: vento sobre equipamentos, telhas e fechamentos presos à estrutura, vento fora do plano do pórtico e efeitos dinâmicos (seção 9).");
            doc.Blocks.Add(b1.S);
        }

        void MemorialAnalise(FlowDocument doc)
        {
            doc.Blocks.Add(Secao2("9.1 Análise estrutural (método da rigidez direta)"));
            doc.Blocks.Add(Texto("Cada barra é um elemento de pórtico plano com 3 graus de liberdade por nó (u, v, θ). A matriz de rigidez local é montada com os termos abaixo, " +
                                 "girada para o sistema global e somada na matriz da estrutura; K·U = F dá os deslocamentos dos nós (tabela 5) e, de volta em cada barra, " +
                                 "f = k·u + f₀ dá os esforços nas pontas (f₀ = esforços de engastamento perfeito das cargas na barra)."));
            doc.Blocks.Add(Tabela(new[] { "Barra", "L (m)", "EA (kN)", "EI (kN·m²)", "EA/L", "12EI/L³", "6EI/L²", "4EI/L", "2EI/L" },
                M.Barras.Where(b => b.Res != null).Select(b =>
                {
                    ResBarra r = b.Res; double L = r.L;
                    return new[] { b.Id.ToString(), Fm(L, 3), Fm(r.EA, 0), Fm(r.EI, 1), Fm(r.EA / L, 0), Fm(12 * r.EI / (L * L * L), 1), Fm(6 * r.EI / (L * L), 1), Fm(4 * r.EI / L, 1), Fm(2 * r.EI / L, 1) };
                })));
            doc.Blocks.Add(Texto("Unidades das colunas de rigidez: EA/L e 12EI/L³ em kN/m; 6EI/L² em kN/rad; 4EI/L e 2EI/L em kN·m/rad. " + (M.Timoshenko ? "Com deformação por cortante (Timoshenko) os termos de flexão são reduzidos por 1/(1+φ), φ = 12EI/(GAs·L²)." : "Sem deformação por cortante (Euler-Bernoulli).")));

            doc.Blocks.Add(Texto("Esforços nas pontas de cada barra (eixos locais: N + tração, Q cortante, M + traciona a fibra de baixo):"));
            doc.Blocks.Add(Tabela(new[] { "Barra", "Nó", "N (kN)", "Q (kN)", "M (kN·m)", "Nó", "N (kN)", "Q (kN)", "M (kN·m)" },
                M.Barras.Where(b => b.Res != null).Select(b =>
                {
                    ResBarra r = b.Res;
                    return new[] { b.Id.ToString(), b.N1.ToString(), Fm(r.N(0)), Fm(r.Q(0)), Fm(r.M(0)), b.N2.ToString(), Fm(r.N(r.L)), Fm(r.Q(r.L)), Fm(r.M(r.L)) };
                })));

            // equilibrio global: cargas aplicadas (com os fatores do resultado mostrado) x reacoes
            doc.Blocks.Add(Secao2("9.2 Equilíbrio global"));
            var apoios = M.Nos.Where(n => n.TemApoio && n.Res != null).ToList();
            double rx = apoios.Sum(n => n.Res[3]), ry = apoios.Sum(n => n.Res[4]), rm = apoios.Sum(n => n.Res[5] + n.X * n.Res[4] - n.Y * n.Res[3]);
            var bl = new Bloco("Reações de apoio");
            bl.Tab(Tabela(new[] { "Nó", "X (m)", "Y (m)", "Rx (kN)", "Ry (kN)", "M (kN·m)", "Momento em relação a (0;0) = M + x·Ry − y·Rx" },
                apoios.Select(n => new[] { n.Id.ToString(), Fm(n.X, 3), Fm(n.Y, 3), Fm(n.Res[3]), Fm(n.Res[4]), Fm(n.Res[5]), Fm(n.Res[5] + n.X * n.Res[4] - n.Y * n.Res[3]) })));
            bl.Valores("ΣRx = " + Fm(rx) + " kN     ΣRy = " + Fm(ry) + " kN     ΣM(0;0) = " + Fm(rm) + " kN·m");
            if (Vista.Envoltoria == null)
            {
                double fx, fy, fm; CargasAplicadas(out fx, out fy, out fm);
                bl.Valores("Cargas aplicadas (com peso próprio e fatores do resultado): ΣFx = " + Fm(fx) + " kN   ΣFy = " + Fm(fy) + " kN   ΣM(0;0) = " + Fm(fm) + " kN·m");
                double ex = rx + fx, ey = ry + fy, em = rm + fm;
                bool ok = Math.Abs(ex) < 1e-3 + 1e-6 * Math.Abs(fx) && Math.Abs(ey) < 1e-3 + 1e-6 * Math.Abs(fy) && Math.Abs(em) < 1e-3 + 1e-6 * Math.Abs(fm);
                bl.Resultado("Fechamento: ΣFx = " + Fm(ex, 4) + "   ΣFy = " + Fm(ey, 4) + "   ΣM = " + Fm(em, 4) + "  (deve ser 0)", ok);
                bl.Selo(ok, ok ? "✓ EQUILÍBRIO OK" : "✗ NÃO FECHA");
            }
            else bl.Nota("Na envoltória cada barra tem a sua combinação crítica: o equilíbrio vale combinação a combinação.");
            doc.Blocks.Add(bl.S);
        }

        // soma das cargas do resultado mostrado (fatores dos casos), em relacao a (0;0)
        void CargasAplicadas(out double fx, out double fy, out double fm)
        {
            fx = fy = fm = 0;
            var fat = FatoresAtuais();
            for (int c = 0; c < M.Casos.Count && c < fat.Count; c++)
            {
                double f = fat[c]; if (f == 0) continue;
                foreach (No n in M.Nos)
                {
                    double[] q = CargaNo(n, c);
                    fx += f * q[0]; fy += f * q[1]; fm += f * (q[2] + n.X * q[1] - n.Y * q[0]);
                }
                foreach (Barra b in M.Barras)
                {
                    No a = M.NoPorId(b.N1), e = M.NoPorId(b.N2);
                    double L = M.Comp(b); if (L < 1e-12) continue;
                    double cx = (a.X + e.X) / 2, cy = (a.Y + e.Y) / 2, ux = (e.X - a.X) / L, uy = (e.Y - a.Y) / L;
                    double[] q = CargaBarra(b, c);
                    // uniformes globais, por metro de barra
                    double gx = q[0] * L, gy = q[1] * L;
                    if (M.PesoProprio && M.Casos[c].PesoProprio)
                    {
                        Secao s = Biblioteca.AcharSecao(b.Secao); Material mt = Biblioteca.AcharMaterial(b.Material);
                        if (s != null && mt != null) gy -= (s.Massa > 0 ? s.Massa : mt.Densidade * s.A) * 9.80665 / 1000 * L;
                    }
                    fx += f * gx; fy += f * gy; fm += f * (cx * gy - cy * gx);
                    // perpendicular linear (local +y = esquerda da barra): resultante e posicao do centroide
                    double res = (q[2] + q[3]) / 2 * L;
                    if (Math.Abs(res) > 1e-12)
                    {
                        double xr = Math.Abs(q[2] + q[3]) > 1e-12 ? L * (q[2] + 2 * q[3]) / (3 * (q[2] + q[3])) : L / 2;
                        double px = a.X + ux * xr, py = a.Y + uy * xr, nx = -uy, ny = ux;
                        fx += f * res * nx; fy += f * res * ny; fm += f * (px * res * ny - py * res * nx);
                    }
                }
            }
        }

        void MemorialBarra(FlowDocument doc, Barra b)
        {
            Secao s = Biblioteca.AcharSecao(b.Secao); Material mat = Biblioteca.AcharMaterial(b.Material);
            doc.Blocks.Add(new Paragraph(new Run("Barra " + b.Id + (b.Membro != 0 ? "  ·  trecho da barra contínua B" + b.Membro : "") + "  —  " + (s != null ? s.Descricao : b.Secao) + "  ·  " + b.Material))
            { FontWeight = FontWeights.SemiBold, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 2), BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 0.5) });
            if (s == null || mat == null || b.Res == null) { doc.Blocks.Add(Texto("Sem dados para o desenvolvimento.")); return; }
            // combinacao critica (envoltoria) = a de maior aproveitamento, como na verificacao
            ResBarra real = b.Res, critico = b.Res; string comb = "";
            if (Vista.Envoltoria != null && Vista.Envoltoria.ContainsKey(b))
            {
                double pior = -1; int i = 0;
                foreach (ResBarra rr in Vista.Envoltoria[b]) { b.Res = rr; var vv = Verificador.Barra(M, b, s); if (vv.Aplicavel && vv.Ap > pior) { pior = vv.Ap; critico = rr; comb = Vista.NomesEnv[i]; } i++; }
            }
            b.Res = critico;
            try { MemorialBarraCom(doc, b, s, mat, comb); }
            finally { b.Res = real; }   // o relatorio nao pode mudar o resultado mostrado
        }

        void MemorialBarraCom(FlowDocument doc, Barra b, Secao s, Material mat, string comb)
        {
            ResBarra r = b.Res;
            Verif v = Verificador.Barra(M, b, s);
            bool fraco = b.EixoFraco;
            double I = fraco ? s.Iy : s.Ix, W = fraco ? s.Wy : s.Wx, As = fraco ? s.AsY : s.AsX, c = fraco ? s.Cy : s.Cx, n = M.Seguranca, fy = mat.Fy, E = mat.E;
            if (comb != "") doc.Blocks.Add(Texto("Combinação crítica desta barra: " + comb + "."));

            var dados = new Bloco("Dados");
            dados.Tab(Dados(new[]
            {
                new[] { "Comprimento", "L", Fm(r.L * 1000, 0), "mm", "geometria" },
                new[] { "Área", "A", Fm(s.A * 1e6, 0), "mm²", "catálogo" },
                new[] { "Orientação da seção", "—", fraco ? "eixo y (fraco) no plano" : "eixo x (forte) no plano", "", "\"Posição do perfil\" = " + (fraco ? "deitado" : "em pé") },
                new[] { "Inércia eixo x (forte)", "Ix", Fm(s.Ix * 1e12, 0), "mm⁴", "catálogo · " + (fraco ? "flambagem fora do plano" : "flexão e flambagem no plano") },
                new[] { "Inércia eixo y (fraco)", "Iy", Fm(s.Iy * 1e12, 0), "mm⁴", "catálogo · " + (fraco ? "flexão e flambagem no plano" : "flambagem fora do plano") },
                new[] { "Raio de giração x / y", "rx / ry", Fm(Math.Sqrt(s.Ix / s.A) * 1000) + " / " + Fm(Math.Sqrt(s.Iy / s.A) * 1000), "mm", "√(I/A)" },
                new[] { "Inércia usada na tensão e na flecha", "I", Fm(I * 1e12, 0), "mm⁴", fraco ? "Iy (eixo no plano)" : "Ix (eixo no plano)" },
                new[] { "Distância à fibra mais afastada", "c", Fm(c * 1000, 1), "mm", "geometria da seção" },
                new[] { "Módulo resistente", "W = I/c", Fm(W * 1e9, 0), "mm³", Fm(I * 1e12, 0) + " / " + Fm(c * 1000, 1) },
                new[] { "Área de cisalhamento", "As", Fm(As * 1e6, 0), "mm²", s.Forma == "TC" ? "A/2 (tubo redondo)" : "alma" },
                new[] { "Escoamento do aço", "fy", Fm(fy, 0), "MPa", b.Material },
                new[] { "Módulo de elasticidade", "E", Fm(E, 0), "MPa", b.Material },
                new[] { "Coeficiente de segurança", "n", Fm(n), "—", "aba Análise" },
            }));
            doc.Blocks.Add(dados.S);
            if (!v.Aplicavel) { doc.Blocks.Add(Texto(v.Motivo)); return; }

            // ponto critico da tensao
            double xc = 0, nc = 0, mc = 0, sig = -1, qmax = 0, xq = 0;
            foreach (double x in r.Pontos(2))
            {
                double N = r.N(x), Mx = r.M(x), sg = (Math.Abs(N) / s.A + Math.Abs(Mx) / W) / 1000;
                if (sg > sig) { sig = sg; xc = x; nc = N; mc = Mx; }
                if (Math.Abs(r.Q(x)) > qmax) { qmax = Math.Abs(r.Q(x)); xq = x; }
            }
            double sN = Math.Abs(nc) / s.A / 1000, sM = Math.Abs(mc) / W / 1000;

            var t = new Bloco("1) Tensão normal — " + (Math.Abs(nc) > 1e-9 ? (nc < 0 ? "flexo-compressão" : "flexo-tração") : "flexão") + " no ponto crítico x = " + Fm(xc * 1000, 0) + " mm");
            t.Nota("Esforços nesse ponto: N = " + Fm(nc) + " kN, M = " + Fm(mc) + " kN·m. Forças em N e medidas em mm, logo N/mm² = MPa.");
            t.Eq("\\sigma = \\frac{|N|}{A} + \\frac{|M|}{W} \\leq \\sigma_{adm} = \\frac{f_y}{n}");
            t.Eq("\\sigma = \\frac{" + L(Math.Abs(nc) * 1000, 0) + "}{" + L(s.A * 1e6, 0) + "} + \\frac{" + L(Math.Abs(mc) * 1e6, 0) + "}{" + L(W * 1e9, 0) + "} = " + L(sN) + " + " + L(sM) + " = " + L(v.Sigma) + Un("MPa"));
            t.Eq("\\sigma_{adm} = \\frac{" + L(fy, 0) + "}{" + L(n) + "} = " + L(v.SigmaAdm, 1) + Un("MPa"));
            t.Resultado("Aproveitamento = σ / σadm = " + Fm(v.Sigma, 2) + " / " + Fm(v.SigmaAdm, 1) + " = " + Pct(v.ApTensao), v.ApTensao <= 1);
            t.Selo(v.ApTensao <= 1); doc.Blocks.Add(t.S);

            var q = new Bloco("2) Cisalhamento — maior cortante em x = " + Fm(xq * 1000, 0) + " mm");
            q.Eq("\\tau = \\frac{Q}{A_s} \\leq \\tau_{adm} = \\frac{0{,}577 \\, f_y}{n}");
            q.Eq("\\tau = \\frac{" + L(qmax * 1000, 0) + "}{" + L(As * 1e6, 0) + "} = " + L(v.Tau) + Un("MPa") + " \\;\\;\\;\\;\\;\\; \\tau_{adm} = \\frac{0{,}577 \\cdot " + L(fy, 0) + "}{" + L(n) + "} = " + L(v.TauAdm, 1) + Un("MPa"));
            q.Nota("0,577 = 1/√3 (critério de von Mises para cisalhamento puro).");
            q.Resultado("Aproveitamento = " + Fm(v.Tau, 2) + " / " + Fm(v.TauAdm, 1) + " = " + Pct(v.ApCorte), v.ApCorte <= 1);
            q.Selo(v.ApCorte <= 1); doc.Blocks.Add(q.S);

            var f = new Bloco("3) Deslocamento (flecha)");
            if (v.Balanco)
            {
                f.Nota("Barra em balanço (uma ponta sem apoio e sem outra barra): vale o deslocamento total da ponta livre, perpendicular à barra, que inclui o giro e o deslocamento do nó onde o balanço começa (resultado da análise). H = distância da ponta livre ao apoio mais próximo.");
                f.Eq("\\delta = " + L(v.Flecha * 1000) + Un("mm") + " \\;\\leq\\; \\delta_{lim} = \\frac{H}{" + L(M.LimiteFlecha, 0) + "} = \\frac{" + L(v.FlechaAdm * M.LimiteFlecha * 1000, 0) + "}{" + L(M.LimiteFlecha, 0) + "} = " + L(v.FlechaAdm * 1000) + Un("mm"));
                // referencia classica para conferir a mao: balanco com momento na ponta
                double mPonta = Math.Abs(r.M(r.L)) > Math.Abs(r.M(0)) ? Math.Abs(r.M(r.L)) : Math.Abs(r.M(0));
                if (mPonta > 1e-9)
                {
                    double dRef = mPonta * r.L * r.L / (2 * E * 1000 * I) * 1000;
                    f.Nota("Conferência: só a flexão deste trecho, como balanço engastado com o momento da ponta (sem o giro do nó de origem):");
                    f.Eq("\\delta_{trecho} = \\frac{M L^2}{2 E I} = \\frac{" + L(mPonta * 1e6, 0) + " \\cdot " + L(r.L * 1000, 0) + "^2}{2 \\cdot " + L(E, 0) + " \\cdot " + L(I * 1e12, 0) + "} = " + L(dRef) + Un("mm"));
                }
            }
            else
            {
                f.Nota("Barra entre nós: maior afastamento da elástica em relação à reta que liga as duas pontas deslocadas (flecha relativa), obtido da elástica exata da análise.");
                f.Eq("\\delta = \\max_x \\left| v(x) - \\left[ v_1 + (v_2 - v_1)\\frac{x}{L} \\right] \\right| = " + L(v.Flecha * 1000) + Un("mm"));
                f.Eq("\\delta \\leq \\delta_{lim} = \\frac{L}{" + L(M.LimiteFlecha, 0) + "} = \\frac{" + L(r.L * 1000, 0) + "}{" + L(M.LimiteFlecha, 0) + "} = " + L(v.FlechaAdm * 1000) + Un("mm"));
            }
            f.Resultado("Aproveitamento = " + Fm(v.Flecha * 1000, 2) + " / " + Fm(v.FlechaAdm * 1000, 2) + " = " + Pct(v.ApFlecha), v.ApFlecha <= 1);
            f.Selo(v.ApFlecha <= 1); doc.Blocks.Add(f.S);

            // 4a / 4b) flambagem nos dois planos, pela barra continua inteira (mesmo calculo do Verificador)
            doc.Blocks.Add(BlocoFlamb("4a) Flambagem no plano do pórtico", v.NoPlano, v, s, b, E, fy, n).S);
            doc.Blocks.Add(BlocoFlamb("4b) Flambagem fora do plano", v.ForaPlano, v, s, b, E, fy, n).S);

            // 5) interacao N + M com estabilidade (so com compressao)
            var it = new Bloco("5) Interação flexo-compressão com flambagem");
            if (v.Nc > 1e-6)
            {
                double ncrMin = Math.Min(v.NoPlano.Ncr, v.ForaPlano.Ncr);
                it.Nota("A tensão do item 1 compara N/A com fy/n; aqui a parcela axial é comparada com a resistência à flambagem do pior plano e o momento é amplificado pelo efeito P-δ da própria barra (Cm = 1, a favor da segurança).");
                it.Eq("\\frac{n \\, N_c}{N_{cr,min}} + B \\, \\frac{n \\, |M|_{max}}{W f_y} \\leq 1 \\;\\;\\;\\;\\;\\; B = \\frac{1}{1 - n N_c / N_e} \\;\\;\\;\\;\\;\\; N_e = \\frac{\\pi^2 E I}{L_e^2}\\;(\\mathrm{no\\;plano})");
                if (v.NeNoPlano > 0)
                    it.Eq("N_e = \\frac{\\pi^2 \\cdot " + L(E, 0) + " \\cdot " + L(v.NoPlano.I * 1e12, 0) + "}{" + L(v.NoPlano.Le * 1000, 0) + "^2} = " + L(v.NeNoPlano, 1) + Un("kN") +
                         " \\;\\;\\;\\;\\;\\; B = \\frac{1}{1 - " + L(n) + " \\cdot " + L(v.Nc) + " / " + L(v.NeNoPlano, 1) + "} = " + L(v.BAmp, 3));
                it.Eq("\\frac{" + L(n * v.Nc) + "}{" + L(ncrMin, 2) + "} + " + L(v.BAmp, 3) + " \\cdot \\frac{" + L(n * v.Mmax * 1e6, 0) + "}{" + L(W * 1e9, 0) + " \\cdot " + L(fy, 0) + "} = " + L(v.ApInteracao, 3));
                it.Resultado("Aproveitamento = " + Pct(v.ApInteracao), v.ApInteracao <= 1);
                it.Selo(v.ApInteracao <= 1);
            }
            else it.Nota("Sem compressão neste resultado: a interação com flambagem não se aplica" + (v.AvisoInversao != "" ? " — ⚠ " + v.AvisoInversao + "." : "."));
            doc.Blocks.Add(it.S);

            if (v.Aviso != "" || v.AvisoInversao != "")
            {
                var a = new Bloco("⚠ Atenção");
                if (v.Aviso != "") a.Nota(v.Aviso);
                if (v.AvisoInversao != "") a.Nota(v.AvisoInversao + ".");
                a.S.BorderBrush = TemaPlant.BImpCarga; doc.Blocks.Add(a.S);
            }

            var res = new Bloco("Resultado da barra " + b.Id);
            res.Tab(Tabela(new[] { "Tensão", "Cisalhamento", "Flecha", "Flamb. no plano", "Flamb. fora", "Esbeltez", "N + M", "Governa", "Situação" },
                new[] { new[] { Pct(v.ApTensao), Pct(v.ApCorte), Pct(v.ApFlecha), v.Nc > 1e-6 ? Pct(v.NoPlano.Ap) : "—", v.Nc > 1e-6 ? Pct(v.ForaPlano.Ap) : "—",
                                v.LimEsbeltez > 0 ? Pct(v.ApEsbeltez) + " (≤" + Fm(v.LimEsbeltez, 0) + ")" : "—", v.Nc > 1e-6 ? Pct(v.ApInteracao) : "—",
                                v.Critico + " (" + Pct(v.Ap) + ")", v.Passa ? (v.AvisoInversao != "" ? "passa com ⚠" : "passa") : "NÃO PASSA" } }));
            if (v.Passa && v.Aviso == "" && v.AvisoInversao != "") { res.Selo(true, "⚠ PASSA COM AVISO"); res.S.BorderBrush = TemaPlant.BImpCarga; }
            else res.Selo(v.Passa && v.Aviso == "");
            doc.Blocks.Add(res.S);
        }

        // bloco de flambagem de um plano: travamentos, autovalor da barra continua, K, r, lambda, Euler/Johnson, Ncr, KL/r
        Bloco BlocoFlamb(string titulo, Flamb f, Verif v, Secao s, Barra b, double E, double fy, double n)
        {
            var k = new Bloco(titulo + " — eixo " + f.Eixo);
            List<int> nos; var cad = Flambagem.Cadeia(M, b, out nos);
            k.Nota("Calculada para a " + (cad.Count > 1 ? "barra contínua B" + b.Membro + " inteira (" + cad.Count + " trechos, nós " + string.Join("–", nos) + ")" : "barra inteira (nós " + string.Join("–", nos) + ")") +
                   ", por análise de flambagem (autovalor) com compressão uniforme igual à maior da barra: o resultado não depende de como a barra foi dividida.");
            k.Nota("Pontos travados: " + f.Travas + (f.Molas != "" ? " · travamentos elásticos: " + f.Molas : "") + "." +
                   (f.Plano.StartsWith("fora") ? " Fora do plano só travam os apoios e os nós declarados em \"Travamento fora do plano\" (" + (string.IsNullOrEmpty(b.TravFora) ? "nenhum declarado" : b.TravFora) + ")." :
                                                 " Nó travado por outra barra é elástico (deslocável): entra com a rigidez do resto da estrutura; não há análise de 2ª ordem global (P-Δ)."));
            double l1 = Math.Sqrt(2 * Math.PI * Math.PI * E / fy);
            k.Eq("r = \\sqrt{\\frac{I}{A}} = \\sqrt{\\frac{" + L(f.I * 1e12, 0) + "}{" + L(s.A * 1e6, 0) + "}} = " + L(f.R * 1000) + Un("mm"));
            if (f.Instavel)
            {
                k.Nota("Sem travamento suficiente neste plano: a barra é um mecanismo (pode se deslocar sem resistência). Comprimento de flambagem infinito.");
                k.Resultado(v.Nc > 1e-6 ? "Não verificável: NÃO PASSA até declarar travamento ou apoio" : "Sem compressão neste resultado: L/r de tração = " + Fm(f.LambdaT, 0), v.Nc > 1e-6 ? false : (bool?)null);
                if (v.Nc > 1e-6) k.Selo(false);
                return k;
            }
            k.Eq("L_e = \\pi \\sqrt{\\frac{E I}{N_{cr,el}}} = " + L(f.Le * 1000, 0) + Un("mm") + " \\;\\;\\;\\;\\;\\; K = \\frac{L_e}{L} = \\frac{" + L(f.Le * 1000, 0) + "}{" + L(f.Lref * 1000, 0) + "} = " + L(f.K, 2) +
                 " \\;\\;\\;\\;\\;\\; \\lambda = \\frac{K L}{r} = \\frac{" + L(f.Le * 1000, 0) + "}{" + L(f.R * 1000) + "} = " + L(f.Lambda, 1));
            k.Nota("L = maior trecho entre pontos travados (rígidos ou elásticos) = " + Fm(f.Lref * 1000, 0) + " mm; K teórico do autovalor = " + Fm(f.KTeorico, 3) +
                   (f.KMinimo ? " < 1: adotado K = 1 (não se conta com engaste perfeito entre travamentos)." : "."));
            if (v.Nc > 1e-6)
            {
                k.Eq("\\lambda_1 = \\sqrt{\\frac{2 \\pi^2 E}{f_y}} = \\sqrt{\\frac{2 \\pi^2 \\cdot " + L(E, 0) + "}{" + L(fy, 0) + "}} = " + L(l1, 1));
                if (f.Euler) k.Eq("\\lambda > \\lambda_1 \\;(\\mathrm{Euler}): \\;\\; \\sigma_{cr} = \\frac{\\pi^2 E}{\\lambda^2} = \\frac{\\pi^2 \\cdot " + L(E, 0) + "}{" + L(f.Lambda, 1) + "^2} = " + L(f.Scr, 1) + Un("MPa"));
                else k.Eq("\\lambda \\leq \\lambda_1 \\;(\\mathrm{Johnson}): \\;\\; \\sigma_{cr} = f_y - \\frac{1}{E}\\left(\\frac{f_y \\lambda}{2\\pi}\\right)^2 = " + L(fy, 0) + " - \\frac{1}{" + L(E, 0) + "}\\left(\\frac{" + L(fy, 0) + " \\cdot " + L(f.Lambda, 1) + "}{2\\pi}\\right)^2 = " + L(f.Scr, 1) + Un("MPa"));
                k.Eq("N_{cr} = \\sigma_{cr} A = \\frac{" + L(f.Scr, 1) + " \\cdot " + L(s.A * 1e6, 0) + "}{1000} = " + L(f.Ncr, 2) + Un("kN") +
                     " \\;\\;\\;\\;\\;\\; n \\, N_c = " + L(n) + " \\cdot " + L(v.Nc) + " = " + L(n * v.Nc) + Un("kN") + " \\leq N_{cr}");
                bool ok = f.Ap <= 1 && f.Lambda <= 200;
                k.Resultado("Aproveitamento = " + Fm(n * v.Nc, 2) + " / " + Fm(f.Ncr, 2) + " = " + Pct(f.Ap) + (f.Lambda > 200 ? "   ·   KL/r = " + Fm(f.Lambda, 0) + " > 200 (NBR 8800)" : "   ·   KL/r ≤ 200 ✓"), ok);
                k.Selo(ok);
            }
            else if (v.Nt > 1e-6)
            {
                bool ok = f.LambdaT <= 300;
                k.Nota("Sem compressão neste resultado: Ncr não foi comparado com esforço nenhum (flambagem NÃO verificada)." + (v.AvisoInversao != "" ? " ⚠ " + v.AvisoInversao + "." : ""));
                k.Eq("\\frac{L}{r} = \\frac{" + L(f.LDestr * 1000, 0) + "}{" + L(f.R * 1000) + "} = " + L(f.LambdaT, 1) + " \\;\\leq\\; 300 \\;\\;(\\mathrm{tracao,\\;NBR\\;8800})");
                k.Resultado("Esbeltez de tração: " + Fm(f.LambdaT, 0) + (ok ? " ≤ 300" : " > 300"), ok);
                k.Selo(ok, ok ? "⚠ SÓ TRAÇÃO" : null);
            }
            else k.Nota("Sem esforço normal neste resultado.");
            return k;
        }

        void MemorialPlaca(FlowDocument doc, No n)
        {
            ResPlaca r = PlacaPior(n);
            PlacaBase p = n.Placa;
            doc.Blocks.Add(new Paragraph(new Run("Placa de base do nó " + n.Id + (r.Sec != null ? "  —  pilar " + r.Sec.Descricao : ""))) { FontWeight = FontWeights.SemiBold, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 2) });
            if (!r.TemEsforcos) { doc.Blocks.Add(Texto(r.Erro)); return; }
            int nb = r.Paraf.Count;

            var d = new Bloco("Dados");
            var linhas = new List<string[]>
            {
                new[] { "Reação do nó " + n.Id, "Rx / Ry / M", Fm(r.Rx) + " / " + Fm(r.Ry) + " / " + Fm(r.Rm), "kN / kN / kN·m", "análise (eixos globais)" },
                new[] { "Esforços nos eixos da PLACA", "N / V / M", Fm(r.N) + " / " + Fm(r.V) + " / " + Fm(r.M), "kN / kN / kN·m", (r.NormY != 0 ? "placa no piso/teto: N = " + (r.NormY > 0 ? "−Ry" : "+Ry") + ", V = |Rx|" : "placa na parede: N = " + (r.NormX > 0 ? "−Rx" : "+Rx") + ", V = |Ry|") + ", M = −M" },
                new[] { "Mesmos esforços nos eixos da barra", "N / V / M", Fm(r.NBarra) + " / " + Fm(r.VBarra) + " / " + Fm(r.MBarra), "kN / kN / kN·m", r.Inclinada ? "só informativo (barra a " + Fm(r.AngNormal, 1) + "° da normal)" : "iguais (barra perpendicular à placa)" },
                new[] { "Placa", "H × B × t", Fm(r.H, 0) + " × " + Fm(r.B, 0) + " × " + Fm(p.Esp, 1), "mm", p.PeloPerfil ? "perfil + " + Fm(p.Sobra, 0) + " mm de cada lado" : "digitado" },
                new[] { "Aço da chapa", "fy", Fm(r.FyChapa, 0), "MPa", "" },
                new[] { "Fixações", "nb", nb + " (" + p.Fileiras + " fileiras × " + p.PorFileira + ")", "—", "borda " + Fm(p.Borda, 0) + " mm" },
            };
            if (r.Ch != null)
            {
                linhas.Add(new[] { "Chumbador", "", r.Ch.Nome, "", r.Ch.Fonte != "" ? r.Ch.Fonte : r.Ch.Fabricante + ", concreto " + Fm(r.Ch.Fck, 0) + " MPa" });
                linhas.Add(new[] { "Concreto da laje / ensaio", "fck / fck,cat", Fm(r.FckLaje, 0) + " / " + Fm(r.Ch.Fck, 0), "MPa", "f_fck = min(1; √(fck/fck,cat)) = " + Fm(r.FFck, 3) });
                linhas.Add(new[] { "Tipo de carga do chumbador", "", Chumbadores.NomesTipos[r.TipoCarga], "", r.TipoAuto ? "automático (" + (M.TemRotativa ? "há carga giratória" : "nenhum escolhido; nunca estática por omissão") + ")" : "escolhido pelo usuário" });
                linhas.Add(new[] { "Coef. de segurança no concreto", "n", Fm(r.NRef, 1), "—", r.Ch.Tu <= 0 ? "carga RECOMENDADA do fabricante (limite n = 1; coeficiente embutido no catálogo não informado)"
                    : (p.CoefChumbador > 0 ? "digitado" : "tabela Walsywa, " + (r.RiscoAlto ? "alto" : "baixo") + " risco" + (r.RiscoAuto ? " (automático)" : "")) + " · Walsywa " + Chumbadores.NomesTipos[r.TipoCarga] + ": baixo risco " + Fm(Chumbadores.CoefMin[r.TipoCarga], 0) + ", alto risco " + Fm(Chumbadores.CoefMax[r.TipoCarga], 0) });
                if (r.Ch.Quimico)
                    linhas.Add(new[] { "Barra roscada (aço)", "classe / Sp", r.ClasseBarra + " / " + Fm(r.SpBarra, 0), "MPa", "At = " + Fm(r.At, 1) + " mm², Ar = " + Fm(r.Ar, 1) + " mm², n = " + Fm(r.NSeg, 1) + " (Tabela 6 ASTM/ISO, Aula 2)" });
            }
            else
                linhas.Add(new[] { "Parafuso", "d / classe", "M" + Fm(p.Diametro, 0) + " / " + p.Classe, "", "At = " + Fm(r.At, 1) + " mm², Sp = " + Fm(r.Sp, 0) + " MPa" });
            d.Tab(Dados(linhas)); doc.Blocks.Add(d.S);

            // distribuicao
            var dist = new Bloco("1) Força em cada fixação");
            dist.Nota(r.Modelo);
            if (!double.IsNaN(r.PivoY))
            {
                dist.Nota("Placa rígida girando em torno do eixo y₀ = " + Fm(r.PivoY, 0) + " mm. dᵢ = distância da fixação ao eixo de giro (só as do lado tracionado); a = distância do centro ao eixo de giro.");
                dist.Eq("T_i = k \\, d_i \\;\\;\\;\\;\\;\\; k = \\frac{M + N a}{\\sum d_i^2}");
            }
            else { dist.Nota("Todas as fixações tracionadas; uᵢ = distância ao centro da placa."); dist.Eq("T_i = \\frac{N}{n_b} + \\frac{M \\, u_i}{\\sum u_j^2}"); }
            dist.Tab(Tabela(new[] { "Fixação", "y (mm)", "z (mm)", "Tração T (kN)" }, r.Paraf.Select((q, i) => new[] { (i + 1).ToString(), Fm(q[0], 0), Fm(q[1], 0), Fm(q[2], 3) })));
            dist.Eq("F_v = \\frac{V}{n_b} = \\frac{" + L(r.V) + "}{" + nb + "} = " + L(r.Fv, 3) + Un("kN"));
            dist.Resultado("Tmáx = " + Fm(r.Tmax, 3) + " kN   ·   Fv = " + Fm(r.Fv, 3) + " kN");
            doc.Blocks.Add(dist.S);

            var fx = new Bloco(r.Ch != null ? "2) Chumbador (" + (r.Ch.Quimico ? "químico — concreto e aço da barra" : "cargas de ruptura do catálogo") + ")" : "2) Parafuso (resistência de prova)");
            if (r.Ch != null)
            {
                Chumbador c = r.Ch;
                double tCatKgf = c.Tu > 0 ? c.Tu : c.TuRec, tCatKN = c.Tu > 0 ? c.TuKN : c.TuRecKN;
                double vCatKgf = c.Tu > 0 ? c.Vu : c.VuRec, vCatKN = c.Tu > 0 ? c.VuKN : c.VuRecKN;
                if (c.Quimico)
                    fx.Nota("Origem: " + c.Fonte + " — " + (c.Tu > 0 ? "carga de ruptura (tabela Walsywa, n = " + Fm(r.NExig, 1) + ")."
                        : "carga RECOMENDADA do fabricante (limite n = 1; coeficiente embutido no catálogo não informado)."));
                fx.Eq("T_{cat} = " + L(tCatKgf, 0) + Un("kgf") + " \\times 0{,}00981 = " + L(tCatKN) + Un("kN") +
                      (vCatKN > 0 ? " \\;\\;\\;\\;\\;\\; V_{cat} = " + L(vCatKgf, 0) + Un("kgf") + " \\times 0{,}00981 = " + L(vCatKN) + Un("kN") : ""));
                if (c.Quimico || r.FFck < 0.999)
                {
                    fx.Nota("Fator do concreto (hipótese ACI 318 cap. 17, cone de arrancamento ∝ √fck, não dado do fabricante; nunca aumenta):");
                    fx.Eq("f_{fck} = \\min\\!\\left(1;\\;\\sqrt{\\frac{f_{ck}}{f_{ck,ensaio}}}\\right) = \\min\\!\\left(1;\\;\\sqrt{\\frac{" + L(r.FckLaje, 0) + "}{" + L(c.Fck, 0) + "}}\\right) = " + L(r.FFck, 3));
                }
                double s3 = c.S > 0 ? c.S : 3 * c.Hef, c15 = c.C > 0 ? c.C : 1.5 * c.Hef;
                if (r.PsiCone < 0.999)
                {
                    fx.Nota("Chumbadores a " + Fm(r.EspacMin, 0) + " mm < 3·hef = " + Fm(s3, 0) + " mm: os cones de arrancamento se sobrepõem (raio ≈ 1,5·hef, catálogo Walsywa). A tração de referência é reduzida pela razão das áreas projetadas (ACI 318 cap. 17): A = área da união dos quadrados 3·hef × 3·hef dos chumbadores tracionados.");
                    fx.Eq("\\psi = \\frac{A}{n_t \\cdot 9 \\, h_{ef}^2} = " + L(r.PsiCone, 3) + " \\;\\;\\;\\;\\;\\; T_{ref} = T_{cat} \\cdot f_{fck} \\cdot \\psi = " + L(tCatKN) + " \\cdot " + L(r.FFck, 3) + " \\cdot " + L(r.PsiCone, 3) + " = " + L(r.Tref) + Un("kN"));
                }
                else if (r.FFck < 0.999)
                    fx.Eq("T_{ref} = T_{cat} \\cdot f_{fck} = " + L(tCatKN) + " \\cdot " + L(r.FFck, 3) + " = " + L(r.Tref) + Un("kN"));
                double a = Math.Pow(r.Tmax / r.Tref, 5.0 / 3), bb = r.Vref > 0 ? Math.Pow(r.Fv / r.Vref, 5.0 / 3) : 0;
                if (r.Vref <= 0)
                {
                    fx.Nota("O catálogo não traz corte para WQI/WQE: corte verificado no aço da barra (termo de corte no concreto = 0).");
                    fx.Eq("n_{concreto} = \\frac{T_{ref}}{T_{max}} = \\frac{" + L(r.Tref) + "}{" + L(r.Tmax, 3) + "} = " + L(r.NComb) + " \\;\\geq\\; n_{ref} = " + L(r.NRef, 1));
                }
                else
                {
                    fx.Nota("Interação tração + corte no concreto (expoente 5/3):");
                    fx.Eq("\\left(\\frac{T}{T_{ref}}\\right)^{5/3} + \\left(\\frac{V}{V_{ref}}\\right)^{5/3} = \\left(\\frac{" + L(r.Tmax, 3) + "}{" + L(r.Tref) + "}\\right)^{5/3} + \\left(\\frac{" + L(r.Fv, 3) + "}{" + L(r.Vref) + "}\\right)^{5/3} = " + L(a, 5) + " + " + L(bb, 5) + " = " + L(a + bb, 5));
                    fx.Eq("n_{concreto} = \\left(" + L(a + bb, 5) + "\\right)^{-3/5} = " + L(r.NComb) + " \\;\\geq\\; n_{ref} = " + L(r.NRef, 1));
                }
                fx.Resultado("Concreto: aproveitamento = nref / nconcreto = " + Pct(r.NRef / r.NComb), r.NComb >= r.NRef);
                if (c.Quimico)
                {
                    fx.Nota("Verificação do aço da barra roscada — classe " + r.ClasseBarra + ", Sp = " + Fm(r.SpBarra, 0) + " MPa (Tabela 6 ASTM/ISO, Aula 2), At = " + Fm(r.At, 1) + " mm², Ar = " + Fm(r.Ar, 1) + " mm²:");
                    double vmB = Math.Sqrt(r.Sigma * r.Sigma + 3 * r.Tau * r.Tau);
                    fx.Eq("\\sigma = \\frac{T_{max}}{A_t} = \\frac{" + L(r.Tmax * 1000, 0) + "}{" + L(r.At, 1) + "} = " + L(r.Sigma) + Un("MPa") +
                          " \\;\\;\\;\\;\\;\\; \\tau = \\frac{F_v}{A_r} = \\frac{" + L(r.Fv * 1000, 0) + "}{" + L(r.Ar, 1) + "} = " + L(r.Tau) + Un("MPa"));
                    fx.Eq("\\sigma_{vm} = \\sqrt{\\sigma^2 + 3\\tau^2} = \\sqrt{" + L(r.Sigma) + "^2 + 3 \\cdot " + L(r.Tau) + "^2} = " + L(vmB) + Un("MPa") +
                          " \\;\\;\\;\\;\\;\\; n_{aco} = \\frac{S_p}{\\sigma_{vm}} = \\frac{" + L(r.SpBarra, 0) + "}{" + L(vmB) + "} = " + L(r.NAco) + " \\;\\geq\\; n = " + L(r.NSeg, 1));
                    fx.Resultado("Aço da barra: aproveitamento = n / naco = " + Pct(r.NSeg / r.NAco) + "   ·   aproveitamento do chumbador = " + Pct(r.ApFix), r.NAco >= r.NSeg);
                }
                fx.Nota("Instalação: furo Ø" + Fm(c.Furo, 1) + " mm, embutimento " + Fm(c.Hef, 0) + " mm" + (c.Torque > 0 ? ", torque " + Fm(c.Torque, 1) + " kgf·m = " + Fm(r.Torque, 0) + " N·m" : "") +
                        ". Distância entre chumbadores ≥ " + Fm(s3, 0) + " mm (placa: " + Fm(r.EspacMin, 0) + " mm); até a borda do concreto ≥ " + Fm(c15, 0) + " mm.");
            }
            else
            {
                fx.Eq("\\sigma = \\frac{T_{max}}{A_t} = \\frac{" + L(r.Tmax * 1000, 0) + "}{" + L(r.At, 1) + "} = " + L(r.Sigma) + Un("MPa") +
                      " \\;\\;\\;\\;\\;\\; \\tau = \\frac{F_v}{A_" + (p.RoscaNoCorte ? "r" : "d") + "} = \\frac{" + L(r.Fv * 1000, 0) + "}{" + L(p.RoscaNoCorte ? r.Ar : r.Ad, 1) + "} = " + L(r.Tau) + Un("MPa"));
                fx.Nota(p.RoscaNoCorte ? "Rosca no plano de corte: área do diâmetro menor (Ar)." : "Corpo liso no plano de corte: área nominal (Ad).");
                double vm = Math.Sqrt(r.Sigma * r.Sigma + 3 * r.Tau * r.Tau);
                fx.Eq("\\sigma_{vm} = \\sqrt{\\sigma^2 + 3\\tau^2} = \\sqrt{" + L(r.Sigma) + "^2 + 3 \\cdot " + L(r.Tau) + "^2} = " + L(vm) + Un("MPa"));
                fx.Eq("n_{obtido} = \\frac{S_p}{\\sigma_{vm}} = \\frac{" + L(r.Sp, 0) + "}{" + L(vm) + "} = " + L(r.NComb) + " \\;\\geq\\; n_{exigido} = " + L(r.NExig));
                fx.Resultado("Aproveitamento = " + Pct(r.NExig / r.NComb), r.NComb >= r.NExig);
                fx.Nota("Aperto: pré-carga Fi = 0,75·At·Sp = " + Fm(r.Fi, 1) + " kN;  torque T = 0,2·Fi·d = 0,2 · " + Fm(r.Fi, 1) + " · " + Fm(p.Diametro, 0) + " = " + Fm(r.Torque, 0) + " N·m.");
            }
            if (r.Ch != null)
                fx.Resultado("Espaçamento entre chumbadores " + Fm(r.EspacMin, 0) + " mm " + (r.EspacOk ? "≥" : "<") + " mínimo para instalar 6·d = " + Fm(Chumbadores.EspacMinimo(r.Ch), 0) + " mm" + (r.EspacOk ? "" : " — NÃO PASSA"), r.EspacOk);
            fx.Selo(r.ChOk); doc.Blocks.Add(fx.S);

            var ch = new Bloco("3) Espessura da chapa");
            if (r.Inclinada)
            {
                ch.Nota("⚠ Barra inclinada " + Fm(r.AngNormal, 1) + "° em relação à normal da placa: o modelo de faixa em balanço até a face do perfil supõe o perfil perpendicular à placa e NÃO vale aqui. Chapa não verificada.");
                ch.Selo(false, "⚠ NÃO VERIFICADO");
            }
            else if (r.EspNecT > 0 || r.EspNecC > 0)
            {
                if (r.EspNecT > 0)
                {
                    ch.Nota("Lado tracionado: a faixa de chapa entre a fileira tracionada e a face do perfil trabalha em balanço: M = T·e e σ = 6M/(b·t²) ≤ fy/n.");
                    ch.Eq("t_T = \\sqrt{\\frac{6 \\, T \\, e \\, n}{b \\, f_y}} = \\sqrt{\\frac{6 \\cdot " + L(r.TLinha * 1000, 0) + " \\cdot " + L(r.Braco, 0) + " \\cdot " + L(M.Seguranca) + "}{" + L(r.LargEf, 0) + " \\cdot " + L(r.FyChapa, 0) + "}} = " + L(r.EspNecT) + Un("mm"));
                    ch.Nota("T = tração da fileira crítica; e = distância da fileira à face do perfil; b = largura efetiva = nº de fixações × (2e + d), limitada à largura da placa.");
                }
                if (r.EspNecC > 0)
                {
                    ch.Nota("Lado comprimido: o modelo de placa rígida concentra a reação do concreto C = ΣT − N na linha de giro; a faixa da linha de giro até a face do perfil também trabalha em balanço (largura B da placa).");
                    ch.Eq("C = \\sum T_i - N = " + L(r.CConc + r.N, 3) + " - (" + L(r.N, 3) + ") = " + L(r.CConc, 3) + Un("kN") + " \\;\\;\\;\\;\\;\\; e_c = |y_0| - \\frac{d}{2} = " + L(Math.Abs(r.PivoY), 0) + " - " + L(r.RefIn / 2, 1) + " = " + L(r.BracoC, 1) + Un("mm"));
                    ch.Eq("t_C = \\sqrt{\\frac{6 \\, C \\, e_c \\, n}{B \\, f_y}} = \\sqrt{\\frac{6 \\cdot " + L(r.CConc * 1000, 0) + " \\cdot " + L(r.BracoC, 1) + " \\cdot " + L(M.Seguranca) + "}{" + L(r.B, 0) + " \\cdot " + L(r.FyChapa, 0) + "}} = " + L(r.EspNecC) + Un("mm"));
                    ch.Nota("Se a chapa não tiver essa rigidez, a reação se espalha (a placa não é rígida) e as trações dos chumbadores ficam maiores que as do item 1: use t ≥ t_C ou nervuras. A pressão no concreto não é verificada.");
                }
                ch.Resultado("t necessária = máx(t_T, t_C) = " + Fm(r.EspNec, 2) + " mm  " + (r.EspNec <= p.Esp ? "≤" : ">") + "  t adotada " + Fm(p.Esp, 1) + " mm", r.EspNec <= p.Esp);
                ch.Selo(r.EspNec <= p.Esp);
            }
            else ch.Nota("Sem fixação tracionada fora da projeção do perfil e sem reação concentrada do concreto: a chapa não trabalha à flexão neste modelo.");
            doc.Blocks.Add(ch.S);

            if (r.Sec != null)
            {
                double h = p.Solda > 0 ? p.Solda : Solda.Minima(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, p.Eletrodo, p.Esp);
                if (h <= 0) h = 25;
                ResSolda sd = Solda.Calcular(r.Sec, r.Deitado, r.NBarra, r.VBarra, r.MBarra, h, p.Eletrodo, p.Esp);
                var so = new Bloco("4) Solda de filete pilar–placa (" + sd.Grupo + ")");
                if (r.Inclinada) { so.Nota("⚠ Barra inclinada sobre a placa: o grupo de solda abaixo supõe o perfil perpendicular à chapa (cordão plano). Para barra inclinada o cordão é chanfrado e o cálculo não vale: solda NÃO verificada."); so.Selo(false, "⚠ NÃO VERIFICADO"); }
                else if (sd.Erro == "")
                {
                    so.Nota("Esforços da solda nos eixos da barra (N = " + Fm(r.NBarra) + " kN, V = " + Fm(r.VBarra) + " kN, M = " + Fm(r.MBarra) + " kN·m). N e M dão tensão na mesma direção (normal ao plano da garganta, ao longo do eixo da barra); somam-se na fibra mais afastada, que é o ponto crítico do contorno.");
                    so.Eq("a = 0{,}707 \\, h = 0{,}707 \\cdot " + L(h, 0) + " = " + L(0.707 * h) + Un("mm") + " \\;\\;\\;\\;\\;\\; A = a \\, L_c = " + L(0.707 * h) + " \\cdot " + L(sd.L, 0) + " = " + L(sd.A, 0) + Un("mm^2") + " \\;\\;\\;\\;\\;\\; I = a \\, I_u = " + L(sd.I, 0) + Un("mm^4"));
                    so.Eq("\\tau' = \\frac{V}{A} = \\frac{" + L(r.VBarra * 1000, 0) + "}{" + L(sd.A, 0) + "} = " + L(sd.Tau1) + Un("MPa"));
                    double tN = Math.Abs(r.NBarra) * 1000 / sd.A, tM = sd.Tau2 - tN, cS = tM > 1e-9 ? tM * sd.I / (Math.Abs(r.MBarra) * 1e6) : 0;
                    so.Eq("\\tau'' = \\frac{N}{A} + \\frac{M \\, c}{I} = \\frac{" + L(Math.Abs(r.NBarra) * 1000, 0) + "}{" + L(sd.A, 0) + "} + \\frac{" + L(Math.Abs(r.MBarra) * 1e6, 0) + " \\cdot " + L(cS, 1) + "}{" + L(sd.I, 0) + "} = " + L(tN) + " + " + L(tM) + " = " + L(sd.Tau2) + Un("MPa"));
                    so.Eq("\\tau = \\sqrt{\\tau'^2 + \\tau''^2} = \\sqrt{" + L(sd.Tau1) + "^2 + " + L(sd.Tau2) + "^2} = " + L(sd.Tau) + Un("MPa") + " \\;\\leq\\; \\tau_{adm} = 0{,}30 \\, F_{EXX} = " + L(sd.TauAdm, 0) + Un("MPa"));
                    so.Eq("n_{equiv} = \\frac{0{,}577 \\, F_{EXX}}{\\tau} = \\frac{0{,}577 \\cdot " + L(sd.TauAdm / 0.30, 0) + "}{" + L(sd.Tau) + "} = " + L(sd.NEquiv) + " \\;\\;(\\mathrm{von\\;Mises})");
                    so.Nota("Eletrodo " + (string.IsNullOrEmpty(p.Eletrodo) ? "E70" : p.Eletrodo) + "XX. τadm = 0,30·FEXX (AISC, tensões admissíveis) já embute a segurança; n_equiv é o fator equivalente pelo critério de von Mises sobre a resistência do eletrodo. Lc = comprimento do cordão; Iu = inércia do contorno como linha; c = fibra mais afastada.");
                    so.Eq("h \\geq h_{min} = " + L(sd.HMinNorma, 0) + Un("mm") + " \\;\\;(\\mathrm{NBR\\;8800\\;Tab.\\;10,\\;parte\\;mais\\;fina:\\;} t = " + L(Math.Min(sd.Espessura > 0 ? sd.Espessura : 1e9, p.Esp > 0 ? p.Esp : 1e9), 1) + Un("mm") + ")");
                    so.Resultado("Aproveitamento = " + Pct(sd.Ap) + "   ·   perna " + Fm(h, 0) + " mm " + (sd.AbaixoMinimo ? "< mínima " + Fm(sd.HMinNorma, 0) + " mm (NÃO PASSA)" : "≥ mínima " + Fm(sd.HMinNorma, 0) + " mm") + "   ·   comprimento necessário ≈ " + Fm(sd.LNec, 0) + " mm de " + Fm(sd.L, 0) + " mm", sd.Passa);
                    if (sd.MaiorQueParede) so.Nota("⚠ Perna maior que a parede do perfil (" + Fm(sd.Espessura, 1) + " mm): NÃO PASSA.");
                    so.Selo(sd.Passa);
                }
                else so.Nota(sd.Erro);
                doc.Blocks.Add(so.S);
            }
            if (r.Aviso != "") { var a = new Bloco("⚠ Atenção"); a.Nota(r.Aviso); a.S.BorderBrush = TemaPlant.BImpCarga; doc.Blocks.Add(a.S); }
        }
    }
}
