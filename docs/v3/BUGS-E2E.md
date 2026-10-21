# Bugs achados pelos testes de fluxo (`--testes-fluxo`)

Suíte: `codigo/Estructto/TestesFluxo.cs`. Cada bug abaixo tem uma conferência marcada `[CONHECIDO #E2E-nn]` na suíte: ela conta à parte
e não reprova a rodada; quando o bug for corrigido, a linha sai como `ok … (era [CONHECIDO #…]: conferir e tirar a marca)`.
Nenhum bug foi corrigido aqui (regra da tarefa): só registrado.

## Como rodar

```
cd codigo\Estructto; dotnet build -c Release
# fluxos A–E (ESTRUCTTO_MOTOR = fonte; ver E2E-08)
set ESTRUCTTO_MOTOR=C:\dev\wt\e2e\codigo\motor
C:\dev\build\EstructtoV2-e2e\bin\Release\net8.0-windows\Estructto.exe --testes-fluxo <pasta> A,B,C,D,E
# só o motor empacotado (sem ESTRUCTTO_MOTOR): o exe ao lado de motor\ (o publish, ou uma pasta com o exe e um link para motor\)
Estructto.exe --testes-fluxo <pasta> A,B,C,D,E,F <raiz do repositório>
```

Saída: `testes-fluxo.txt` (resumo, processo do motor que rodou, "TODOS PASSARAM" = nenhuma falha nova), `fluxo-<X>.txt` (cada
conferência, gravada na hora), `fluxo-<X>-*.png` / `.pdf` (evidências). O 3º argumento é a raiz do repositório (exemplos e `.ed3`);
sem ele, vem de `ESTRUCTTO_MOTOR` ou de subir a partir do diretório atual.

| Fluxo | O que dirige |
|---|---|
| A | Galpão do zero: novo projeto (grade 4×6 m, 2 níveis), Pilar com atração e Ctrl+R, tesouras digitadas, beirais com atração, terças, X e Pratt pela galeria, G/Q/peso próprio/vento, F5, Verificação de barras, grupos de ligações, pior placa de base, chapa de topo no joelho, DWG/STEP, PDF, salvar/fechar/reabrir/F5 igual |
| B | Edifício 4 pavimentos: modelo da galeria, seção dos pilares em lote (+ desfazer/refazer), laje por pavimento, sismo, modal, estabilidade (B2 no aço), relatório |
| C | Legados: `guincho-*.estructto` e `.ftl` convertidos, F5, salvar no formato novo e reabrir; `.ed3` dos testes do motor: importar, F5, comparar com o CYPE |
| D | Edição: copiar, espelhar, rotacionar, matriz, aparar, estender, grips; Ctrl+Z/Ctrl+Y de cada; Medir |
| E | Robustez: motor morto (antes e durante o F5, e antes de editar), entradas inválidas, autosave/recuperação, janela visível em ≤ 10 s com cópia de recuperação, `--capturas` não toca na recuperação do usuário, modelo de 3069 barras (abrir, F5, quadro do Realista) |
| F | Só o motor empacotado (sem ESTRUCTTO_MOTOR, exige `estructto-motor.exe`): catálogo, 19 tipos de ligação com o próprio exemplo, memorial, cada modelo do novo projeto + F5, desenhar e calcular, `.ed3` real + comparar, os 4 exemplos + F5, dimensionar todos os grupos de ligação, projetista, placa de base, relatório |

## Resumo por severidade

| Severidade | Bugs |
|---|---|
| Alta | E2E-04 |
| Média | E2E-03, E2E-05 (corrigido), E2E-06, E2E-08, E2E-10 |
| Baixa | E2E-02, E2E-07 (corrigido), E2E-09, E2E-11 |

E2E-01 (a grade do novo projeto não aparecia na vista) já estava corrigido no `unificacao` quando a suíte rodou: a conferência voltou a ser normal.

## Bugs

### E2E-04 — Alta — Em planta, a atração "Extremidade" num pilar pega o pé (z = 0), não o topo no plano de trabalho
- Passos: novo projeto com 2 níveis; pilares de 0 a 6 m; plano de trabalho no nível de 6 m; vista Topo; ferramenta Barra; clicar perto do topo de dois pilares.
- Esperado: a barra liga os topos (z = 6 m), que estão no plano de trabalho e mais perto de quem olha.
- Obtido: a atração diz "Extremidade", mas o ponto é o pé do pilar (z = 0). As vigas de beiral saem no térreo, entre as bases, sem aviso. No modo
  Realista o mesmo empate pega a quina da mesa do perfil no pé (nós novos soltos, mecanismo no F5: "nó n30, GL uy").
- Evidência: `C:\dev\build\EstructtoV2-e2e\fluxo-pkg\fluxo-A-03a-beiral-errado.png`; `fluxo-A.txt` ("atração … planta: Extremidade z=0", lista dos beirais b21…b28 em z = 0).
- Suspeito: `codigo/Estructto/Snap3D.cs:387` — o desempate entre candidatos à mesma distância de tela usa só a prioridade do tipo e a ordem da
  lista (a ponta `A` do segmento vem primeiro: o pé). Não usa a profundidade (`olho`, já disponível, só serve à interseção aparente) nem o plano de trabalho.
- Contorno na suíte: desfazer e desenhar em isométrica.

### E2E-03 — Média — Motor morto durante o F5: a mensagem acusa o modelo ("não tem barras, não tem casos")
- Passos: modelo de 3069 barras (edifício 10×10 vãos, 9 pavimentos; F5 ≈ 9 s), F5 calculado; F5 de novo e matar `estructto-motor.exe`
  enquanto calcula. (No galpão de exemplo o F5 acaba antes do kill; por isso a suíte usa o modelo grande.)
- Esperado: mensagem dizendo que o motor parou e foi reiniciado (ou o F5 refeito com o modelo recolocado).
- Obtido: `⚠ Motor: {"erro": "entrada_invalida", "detalhe": "sem_barras: o modelo não tem barras; sem_casos: …"}`; o modelo tem 3069 barras.
  O documento continua "calculado" com o resultado anterior. O F5 seguinte funciona.
- Evidência: `fluxo-E.txt` (linha "motor morto durante o F5").
- Suspeito: `codigo/Estructto/DocumentoEspacial.cs:116-117` confere o dono do modelo uma vez, antes; o `POST /api/calcular` (`DocumentoEspacial.cs:364`)
  passa por `Motor.Chamar` → `Motor.Garantir` (`Motor.cs:39`), que sobe um motor novo, vazio, em silêncio e manda o F5 a ele.

### E2E-05 — Média — "Otimizar" não resolve reprovação de limite máximo (perna de solda maior que a chapa)
- Passos: fluxo A até o grupo do joelho (tesoura W310x38.7 na mesa do pilar W310x38.7); projetista do grupo; Chapa de topo estendida; Otimizar.
- Esperado: o otimizador baixa a solda da alma para ≤ 5,8 mm e a ligação passa.
- Obtido: continua reprovada, "solda da alma: perna máxima 6,0/5,8 mm (NBR 8800 6.2.6.2.2)", taxa 1,035. Baixando `b_alma` para 5 mm à mão, passa.
  O padrão do tipo (`b_alma` = 6 mm) já nasce acima do máximo para almas de 5,8 mm.
- Evidência: `fluxo-pkg\fluxo-A-13-joelho-chapa-topo.png`, `fluxo-A.txt` (linhas "joelho:" e "contorno").
- Suspeito: `codigo/motor/prossen_estrutural/aco/ligacoes.py:725` `otimizar` — enquanto reprova, só tenta subir degraus (`vizinho(..., True)`)
  e para em "nenhum aumento ajuda" (`ligacoes.py:783`); critérios de máximo pedem descer.
- **Situação: corrigido** (branch `wt-bugs-b`). As disposições construtivas levam a dica `sentido` ("sobe"/"desce") e, quando é um
  campo só, `parametro` (`docs/v3/LIGACOES-CONTRATO.md`); `otimizar` desce o campo que um máximo reprovado pede (ou desce a perna e
  engrossa a chapa num degrau duplo) e passou a mexer em `b_mesa`, `b_alma`, `b_solda_*`, `tp`, `t_mesa`, `t_alma`. No caso do joelho
  (W310x38.7, M = 40, V = 30) passa com `b_alma` = 3 mm. Testes: `test_ligacoes_ui.TestOtimizar` (`test_perna_acima_do_maximo_desce`,
  `test_chapa_topo_alma_fina_e2e05`). Falta tirar a marca `[CONHECIDO #E2E-05]` da suíte de fluxo.

### E2E-06 — Média — Erros do motor aparecem só pelo código ("entrada_invalida") nas janelas
- Passos: Importar CYPE › `novo.ed3` (obra sem resultados gravados pelo CYPE) › Comparar.
- Esperado: "a obra não tem resultados gravados pelo CYPE (calcule-a no CYPE 3D e salve)" (o motor manda isso em `detalhe`).
- Obtido: a janela mostra `entrada_invalida`.
- Evidência: `fluxo-C.txt` (linhas "comparar recusado pelo motor" e "[CONHECIDO #E2E-06]").
- Suspeito: `codigo/Estructto/ComandosAnalise.cs:41` `MensagemMotor` devolve o campo `erro` e descarta `detalhe`. Usada em 12 lugares
  (JanelaCype.cs:88, janelas de análise, ligações…).

### E2E-08 — Média — `ESTRUCTTO_MOTOR` é ignorada quando há motor instalado em `%LOCALAPPDATA%\Estructto\motor`
- Passos: com `%LOCALAPPDATA%\Estructto\motor` presente (pacote completo), definir `ESTRUCTTO_MOTOR` para a fonte e abrir o Estructto de `bin\`.
- Esperado: a variável, escolha explícita do desenvolvedor, decide (ou ao menos o programa diz qual motor usa).
- Obtido: roda o pacote de `%LOCALAPPDATA%`, em silêncio. Mudanças no motor da fonte não são testadas pela interface.
  Consequência nesta rodada: a rodada "com ESTRUCTTO_MOTOR" também usou o motor empacotado (a pasta do usuário estava em uso por outras sessões e não foi afastada).
- Evidência: `C:\dev\build\EstructtoV2-e2e\fluxo\testes-fluxo.txt` ("Processo do motor que rodou: …\AppData\Local\Estructto\motor\estructto-motor.exe")
  e a conferência `[CONHECIDO #E2E-08]` no início de `fluxo-A.txt`.
- Suspeito: `codigo/Estructto/Motor.cs:104-109` (ordem: `motor\` ao lado do exe, `%LOCALAPPDATA%`, só depois `ESTRUCTTO_MOTOR`).

### E2E-10 — Média — Abrir um `.ftl` não avisa que apoios e cargas não foram lidos
- Passos: abrir `exemplos/untitled.ftl` (ou Importar › Ftool); F5.
- Esperado: o aviso que `Ftl.Importar` monta ("Apoios e cargas aplicadas não são lidos do .ftl…") aparece ao abrir.
- Obtido: nada; o F5 só diz `mecanismo … nó n1, GL uz` (ver E2E-09).
- Evidência: `fluxo-C.txt` (linhas "[CONHECIDO #E2E-10]"), `fluxo-pkg\fluxo-C-untitled.png`.
- Suspeito: `codigo/Estructto/DocumentoEspacial.cs:156` — `Ftl.Importar(caminho, out _)` descarta o aviso (`Ftl.cs:185`).
  Importar › Ftool passa pelo mesmo caminho (`Acoes.cs:245`).

### E2E-02 — Baixa — O relatório do documento 3D não usa a empresa do novo projeto no carimbo
- Passos: novo projeto com Empresa = "PROSSEN E2E"; F5; Relatório.
- Esperado: "PROSSEN E2E" no campo Empresa (o projeto guarda `config.projeto.empresa`).
- Obtido: vem o `empresa.txt` guardado (ou vazio).
- Evidência: `fluxo-pkg\fluxo-A-14-relatorio.pdf`, `fluxo-A.txt`.
- Suspeito: `codigo/Estructto/Relatorio.cs:20-26` (ramo 3D) não define `RelatorioMotor.EmpresaProjeto`; só o ramo 2D o faz (`Relatorio.cs:32`), onde `Esp` é sempre null.

### E2E-07 — Baixa — Manual diz que sismo gera casos "E"; o motor gera "S"
- Esperado (manual): "Os casos têm tipo G, Q, V (vento) ou E (sismo)"; "Gerar vento e sismo cria casos V e E".
- Obtido: SX, SY, SX_TOR, SY_TOR com tipo "S" (`projeto.py:180`, `_TIPOS_CASO`; "E" é outro tipo).
- Evidência: `fluxo-B.txt` (casos de sismo).
- Suspeito: `docs/manual/03-acoes.md:8` e `:37`.
- **Situação: corrigido** (branch `wt-bugs-b`): o manual diz S (sismo: SX, SY, SX_TOR, SY_TOR) e E (excepcional), como o motor.
  Falta tirar a marca `[CONHECIDO #E2E-07]` da suíte de fluxo, se houver.

### E2E-09 — Baixa — Barra de status mostra o JSON cru do motor
- Passos: F5 num modelo sem apoios (ex.: `.ftl` convertido), ou entrada inválida na galeria de treliças (vão = 0).
- Esperado: texto legível ("Mecanismo: o nó n1 está livre em uz; ponha apoios").
- Obtido: `⚠ Motor: {"erro": "mecanismo", "detalhe": "matriz de rigidez singular (mecanismo); suspeito: nó n1, GL uz"}`.
- Evidência: `fluxo-C.txt` (linhas "[CONHECIDO #E2E-09]"), `fluxo-E.txt` (treliça com vão 0), `fluxo-pkg\fluxo-E-04-trelica-invalida.png`.
- Suspeito: `codigo/Estructto/DocumentoEspacial.cs:667` (`"⚠ Motor: " + ex.Message`), e as irmãs em `DocumentoEspacial.cs:581, 696, 1119`,
  `Desenho3D.cs:155, 245`, `CargasEspacial.cs:99`. Mesma raiz de E2E-06.

### E2E-11 — Baixa — Execução automática ainda grava `%AppData%\NovoFtool\empresa.txt` do usuário
- Passos: `--testes-fluxo` ou `--capturas` abrindo o relatório do motor.
- Esperado: nada no `%AppData%` do usuário (as outras configurações já vão para `%TEMP%\Estructto\config-automacao`, `Config.cs`).
- Obtido: cada abertura do relatório regrava `empresa.txt` (a data muda; com E2E-02 corrigido, o conteúdo também mudaria). A suíte devolve o arquivo.
- Evidência: `fluxo-A.txt`, `fluxo-B.txt`, `fluxo-F.txt` (linhas "[CONHECIDO #E2E-11]").
- Suspeito: `codigo/Estructto/RelatorioMotor.cs:21` (`ArqEmpresa` fixo no `%AppData%`) e `:159` (`Atualizar` grava ao abrir, chamado em `:129`).

## Correções de hipótese da suíte (não são bugs)
- `.ed3` sem resultados gravados pelo CYPE (`novo.ed3`, `PIPERACK TESTE 2.ed3`): o motor recusa a comparação, corretamente.
- Edifício de aço: o motor dá B2 (NBR 8800 anexo D), não γz (NBR 6118, só concreto).
- Catálogo não tem tubos ("Tubo"): famílias W, U, Ue, HP (199 perfis), igual na fonte e no pacote.
- Medir em planta entre pontos que coincidem na tela (topo e pé de pilar): ambíguo; a suíte mede em isométrica.
