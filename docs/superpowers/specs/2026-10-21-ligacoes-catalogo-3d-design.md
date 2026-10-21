# Estructto v0.5 — ligações que encaixam, catálogo completo, vista 3D limpa

Data: 2026-10-21. Produto: Estructto (WPF .NET 8 em `codigo/Estructto`) + motor Python em `codigo/motor/prossen_estrutural/aco/`
(`software/` fora do escopo).

## Estado atual (mapa de 21/10, só leitura)
- Nós: `ligacoes_modelo.py:104-165` `_classificar_no` (pilar/viga/diagonal por ângulo; tipos base, viga_pilar_mesa/alma, viga_viga,
  emenda, gusset, no_trelica). Catálogo: `ligacoes_flexiveis.py`, `ligacoes_rigidas.py`, `ligacoes_contraventamento.py`, `placa_base.py`.
- Pontas/recortes: só na **renderização** — `Render3DGeometria.cs:150-262` `Juntas()`/`Extrudar` (diagonal limitada a 0,3·L, barra some se
  recorte engolir), `Recortar` (cope só I/U/L). O motor só manda `recuo`/`cope` em `viga_viga_recorte`. Geometria e cálculo divergem.
- Parafusos: `Render3DGeometria.cs:77-97,266-278` base `u` arbitrária, sem alinhar hexágono; suspeita de eixo/unidade do contrato.
- Otimizador: `ligacoes.py:763-850` guloso 60 passos; `_geometria` levanta "geometria impossível"; disposições só reprovam, não guiam.
- Editor: `JanelaLigacaoProjeto.cs:487-616`; nenhuma seleção por clique (`LigacaoDesenho.cs:265`, `Render3D.cs:339,349` `Marcar(null)`).
- Catálogo: `codigo/portico/perfis-gerdau.csv` + `perfis-outros.csv` (tubos ret/quad, L iguais, U); motor tem `dados/perfis_gerdau.csv`,
  `perfis_pff.csv`. Faltam T, L desiguais, UPN/IPE/HE, tubo redondo, barra chata. UI `JanelaCatalogo.cs`.
- Setas: `Vista3D.cs:479-531` em Viewport3D WPF sobreposto ao DX → sem profundidade. Sombra: nativa `Vista3D.cs:653-790` + DX `Render3D.cs:120`.
- Arquivo novo: `Inicio.cs:247-300` (vários POST, um por viga no `/lote`), motor a frio até 60 s (`Motor.cs:102-139`), prévia a cada 300 ms.

**Decisão do Claude:** pontas e recortes passam a ser calculados no **motor** (fonte única), devolvidos no contrato `geometria`
por ponta de barra, e o render/DWG/STEP só os aplicam.

## Intenção (por quê)
O usuário testou a v0.4 com modelos importados do CYPE e modelados à mão: **nada encaixa**. Perfis atravessam chapas, a diagonal
entra na gusset, parafusos ficam tortos. A raiz: a ligação não controla as pontas das barras — o perfil fica com o comprimento do
eixo, sem estender/aparar até a face, e o recorte é ruim. Por cima disso o cálculo da ligação quase sempre reprova pela medida das
chapas e o "otimizar" gera valores absurdos. Sucesso = abrir um galpão com pórticos, contraventamento e placa base, aplicar
ligações automáticas e ver tudo encaixado, sem interferência, com cálculo aprovado ou com explicação clara do porquê não.

## Objetivos
1. **Pontas e recortes** (frente G): toda ligação é dona das pontas das barras que chegam no nó.
   - Estender/aparar até a face do apoio (mesa/alma do pilar, chapa de topo, gusset, placa base) com folga (padrão 12 mm, editável).
   - Recortes: cope (L em mesa+alma, profundidade = tf+12, comprimento = bf/2+12, raio ≥ 10 mm), corte em ângulo da diagonal
     com folga à gusset, corte na face, meia-esquadria em quadro tubular, boca de peixe/esquadro tubo-tubo.
   - Recorte vira geometria real no Realista e no DWG/STEP.
2. **Reconhecimento de nós** (G): nós com barras inclinadas (contraventamento, mão-francesa, banzo de treliça) e base de pilar
   identificados no gerenciador de ligações; nenhum nó estrutural fica "sem ligação proposta" sem motivo exibido.
3. **Cálculo e otimizador** (frente C):
   - Limites de projeto por tipo: espessuras comerciais de chapa, larguras ≤ largura disponível no apoio, distâncias de furo à borda
     e entre furos pela NBR 8800 (Tab. 14 e 6.3.9), nº de parafusos por linha compatível com a altura útil.
   - Otimizador busca só dentro dos limites, em ordem de custo (espessura → diâmetro → nº de parafusos → chapa maior).
     Sem solução: **não altera nada**, mostra a verificação que reprova e o limite que travou.
4. **Editor de ligação** (C): seções por componente (chapa, parafusos, soldas, enrijecedores, recortes);
   clicar numa peça na vista 3D **rola o painel até a seção dela e destaca o campo**.
5. **Parafusos 3D** (C): cabeça/porca/haste orientadas pelo eixo do furo (base ortonormal, trata caso antiparalelo), instanciados.
6. **Catálogo de perfis** (frente K): partir de `perfis-gerdau.csv`/`perfis-outros.csv` do Estructto e completar:
   W/HP Gerdau, L abas iguais e desiguais, U laminado, T, barra chata, I/U europeus (IPE, HEA, HEB, UPN — Orange Book ArcelorMittal),
   tubos quadrado, retangular e redondo (Vallourec + metalon), formados a frio U e Ue (NBR 6355 via catálogo de fabricante).
   Árvore **Norma/Fabricante → Família → Série → Perfil** no seletor, com busca. Cada linha com fonte (URL) registrada.
7. **Vista 3D** (frente V): setas de carga respeitam profundidade no Realista (escondidas atrás de perfis; opção "cargas sempre
   visíveis" só no modo analítico); sombra no chão refeita sem artefatos (ou desligável).
8. **Arquivo novo** (frente N): criar arquivo em < 2 s, sem crash; causa raiz documentada.

## Não-objetivos
- Interface web (descartada). Novas normas de cálculo de barras. Novos tipos de ligação além de cobrir os nós inclinados e base.

## Anti-objetivos (falha mesmo que "funcione")
- Otimizador que aprova mudando chapa para fora de faixa comercial ou maior que o apoio.
- Recorte só visual (DWG/STEP/peso sem o recorte).
- Quebrar testes existentes do motor (`compilar.ps1 -Testes`, pytest do `software/`) ou o `--testes-fluxo`.

## Restrições
- Regras do `estructto/CLAUDE.md`: docs + linha no CHANGELOG; motor só muda com critério documentado e teste novo; push só se pedido.
- Agentes trabalham em worktrees `C:\dev\wt\<nome>` (branch `wt-<nome>`); nada editado no checkout principal.
- Valores de norma vêm da NBR 8800/livros em `docs/v3/LIGACOES-CATALOGO.md`; folgas de software (12 mm) marcadas como padrão editável.

## Decidido pelo usuário
- Clique na peça → painel rola até o campo. Otimizador sem solução → para e explica.
- Frentes em paralelo. Catálogo: melhorar o do Estructto.

## Fluxo e modelos (decisão do Claude)
0. Mapa: `/graphify` em `estructto/codigo` + `software/` (AST, sem custo LLM).
1. Plano (`superpowers:writing-plans`) com uma seção por frente.
2. Execução paralela (`superpowers:subagent-driven-development`), 1 worktree por frente:
   G = Opus (geometria é o núcleo); C, K, V, N = Sonnet; extração de tabelas de catálogo = Haiku.
   C depende de G só na interface "pontas/recortes da ligação" — G publica o contrato primeiro.
3. Verificação por frente: build + testes + capturas (`capturar.ps1`) + `--testes-fluxo`; revisor-codigo (Sonnet); verificador-calculos (Opus) só em C.
4. Ajuste: reprovou → volta ao mesmo agente com a lista de achados.
5. Entrega: merge em `unificacao`, CHANGELOG, `docs/v3/STATUS.md`, publicar exe local.

## Decisões do Claude após revisão (o usuário confere)
1. Contrato `geometria.pontas[]` por ponta de barra: `{barra, extremo, plano_corte (ponto+normal, m), folga, recortes[{tipo: cope|angulo|esquadria|boca_peixe, ...}]}`.
   Aditivo: se faltar, `Juntas()` atual segue como fallback (modelos antigos abrem). G define o contrato no dia 1 em `docs/v3/PONTAS-CONTRATO.md`.
2. Cálculo usa a geometria nova (solda, largura útil, bordas). Logo G → contrato primeiro; C começa pela parte independente (limites, editor, parafusos).
3. Limites: tarefa de pesquisa no início de C — chapas comerciais (Gerdau/Usiminas, 4,75–50 mm), largura útil por tipo
   (chapa de topo ≤ bf do apoio; gusset limitado pela folga às mesas; placa base ≥ pilar + 2·borda). Registrar em `docs/v3/LIGACOES-LIMITES.md`.
5. Perfis novos funcionam no motor (propriedades em `secoes.py`/CSV do motor) e no seletor; um CSV único por família em `codigo/motor/.../dados/`,
   o WPF lê do motor. Fontes: URL em `dados/FONTES.md` (dados dimensionais não ficam em `referencias/`). Formados a frio: tabela dimensional NBR 6355 por catálogo público (Perfilor/Brasilit-equivalente) + propriedades calculadas.
6. Árvore única: Norma/Fabricante (nível 1: "Gerdau (ABNT)", "Europeu (EN/DIN)", "Tubos Vallourec", "Metalon", "Formados a frio NBR 6355") → Família → Série → Perfil.
   Nomes atuais mantidos como alias; modelos antigos abrem.
7. Setas e nós desenhados no pipeline DX (com profundidade); o modo nativo usa um único Viewport3D com cena e setas juntos.
8. Arquivo novo: motor pré-aquecido ao abrir o programa; `/lote` em uma chamada; meta < 2 s com motor quente. Crash: reproduzir com grades grandes e sem GPU.
9. Cope/folgas: padrões de software (SDS2/Tekla), editáveis, valem para I/U/L laminados e soldados; cope longo recalcula a seção reduzida.
10. Posse de arquivos: G = `Render3DGeometria.cs` (pontas/recortes) + motor `ligacoes_modelo.py`; C = `Render3DGeometria.Parafuso/Tubo` só após merge de G,
    `JanelaLigacaoProjeto.cs`, `ligacoes.py` otimizador; V = `Vista3D.cs`/`Render3D.cs` cargas+sombra; N = `Inicio.cs`/`Motor.cs`; K = dados + `JanelaCatalogo.cs`.
    Merges feitos pelo Claude principal, na ordem G → K → V → N → C.

## Aceite (decidido pelo usuário)
Modelo real: `estructto/testes-aceite/PLATAFORMA.ed3` (cópia de `Z:\26111\6. 3D Model\6. Cype\PLATAFORMA.ed3`; não commitar se for de cliente).
Importar → propor ligações em todos os nós → 0 interferências sólido-sólido (teste automático de interseção de caixas/malhas),
todo nó com ligação ou motivo exibido, otimizador aprova ou explica, capturas do Realista iguais aos pontos das imagens do usuário.
Mais um galpão gerado pelo assistente (pórtico + contraventamento X + placa base) com os mesmos critérios.

## Deixado ao builder
Estrutura interna das classes de recorte, formato do arquivo de catálogo (CSV por família ou único), forma de destacar o campo no painel.
