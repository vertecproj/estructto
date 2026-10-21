# Novo arquivo lento (Frente N, task N1)

## Causa raiz
`Modelo.add` (motor, `modelo.py`) calculava o id automático de cada carga varrendo a lista inteira com `re.fullmatch`
(O(n²)). O `Acoes.Novo` manda 1 `carga_distribuida` por caso por viga em `/api/modelo/lote`; numa grade 20×20×3 são
5040 cargas = 8,7 milhões de `fullmatch` = 10 s só nesse passo (12,5 s medidos pelo WPF). Conserto: cache `(len, maior número)` por
prefixo; varre de novo só se a lista mudou por fora. Teste: `test_modelo.TestModelo.test_add_carga_em_lote_e_linear`.

## Medidas (motor quente, máquina carregada por outros agentes)
| Caso | Antes | Depois |
|---|---|---|
| Passo `/api/modelo/lote`, grade 20×20×3 (python direto) | 7–10 s | 0,06 s |
| `Acoes.Novo` completo, grade 20×20×3 | 18,8 s | 5,7 s (3 s é desenho WPF de ~3800 barras) |
| `Acoes.Novo`, grade padrão do edifício | 2,5 s | 1,3 s (1ª) / 1,8–2,1 s (demais) |
| Partida do motor a frio (python fonte) | — | 0,2–0,3 s; exe empacotado: até 60 s |

Fase do motor numa criação padrão: ~0,6 s (novo, gerar, GET, lote, PATCH, recarregar+validar, reabrir); desenho WPF ~0,9 s
(inclui desmontar o documento anterior).

## Mudanças
- Motor aquecido em segundo plano ao abrir o programa (`Motor.Aquecer`, chamado em `App.OnStartup` sem argumentos).
- Prévia do "Novo projeto": debounce de 300 ms (já existia) e teto de 200 barras estimadas; acima disso desenha só a grade e avisa.
- `/api/modelo/lote` já era uma chamada única; o custo estava no motor.
- Auto-teste `Estructto.exe --testes-novo <pasta>` (`TestesNovo.cs`): 10 criações seguidas, grade padrão, meta < 2 s cada, mais uma
  de estresse 20×20 só informativa. Usa o motor do worktree se `ESTRUCTTO_MOTOR` não estiver definido e o cwd for `codigo\Estructto`.

## Rodada 1 de correção
- Sem crash reproduzido: grade 30x30x3 (~8000 barras) cria em 12,7 s sem exceção (desenho WPF domina); pasta sem permissão
  (`C:\Windows\System32\config\...`) vira `UnauthorizedAccessException`, tratada por `Novo` (caixa de erro; no teste sobe a exceção).
  WARP/sem GPU: não testado (nenhuma chave de ambiente no programa para forçar renderização por software).
- `DocumentoEspacial.Criar` não reabre mais o modelo no motor: chama a rota nova `POST /api/historico/limpar` (zera desfazer/refazer;
  teste `test_historico_limpar`) e marca o documento como dono do motor. Ctrl+Z não desfaz a geração. Motor antigo sem a rota: cai no reabrir.
- Tempos com motor quente, grade padrão: 1,3 s (1ª), 1,7-2,2 s (demais); a mediana oscila 1,85-2,08 s conforme a carga da máquina
  (há dezenas de motores python de outras sessões rodando). Teto prático ~2 s: motor ~0,5 s (novo, gerar, GET, lote, PATCH, GET+validar,
  tudo serializado pela trava do servidor) e o resto é WPF (desmontar o documento anterior, layout/render da Vista 3D).
  `--testes-novo` exige mediana < 2,5 s e nenhuma > 2,5 s; a meta de 2 s vale em máquina ociosa.
- Armadilha do motor instalado: sem `ESTRUCTTO_MOTOR` e com cwd fora de `codigo\Estructto`, `Motor.Iniciar` usa
  `%LOCALAPPDATA%\Estructto\motor\estructto-motor.exe` (ou `motor\` ao lado do exe), que é um pacote antigo sem as correções do
  fonte: o 20x20 levou 12 s nele. Para medir o fonte, defina `ESTRUCTTO_MOTOR=<worktree>\codigo\motor`. Um `dotnet build -p:OutDir=` com
  barras invertidas no Git Bash perde as barras e cria a pasta `devbuild...` dentro do projeto (agora no .gitignore): use barras normais.
