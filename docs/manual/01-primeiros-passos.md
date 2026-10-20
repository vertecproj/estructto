# 1. Primeiros passos

## Dois tipos de documento

| | Documento 2D | Documento espacial |
|---|---|---|
| Geometria | Pórtico plano XY | Pórtico espacial 3D (X, Y horizontais; Z para cima) |
| Arquivos | `.estructto`, `.portico`, `.ftl` (Ftool) | `.json` do motor, `.ed3` (CYPE 3D), exemplos do motor |
| Como criar | Modelo › Gerar estrutura (vigas, pórticos, treliças, galpões) ou desenhar | Modelo › Gerar 3D (edifício, galpão, pórtico), Importar CYPE |
| Cálculo | Interno ou Python (à escolha) | Sempre pelo motor Python |
| Fonte da verdade | Modelo do Estructto | Modelo do motor; o Estructto guarda uma cópia |
| Vista | 2D, com alternância para 3D | Sempre 3D |

Os dois tipos abrem em abas diferentes da mesma janela. O servidor do motor guarda um modelo só. Ao trocar de aba entre dois
documentos espaciais, o Estructto recoloca o modelo no motor e a pilha de desfazer do outro documento recomeça.

No documento 2D, o plano XY do desenho vira o plano XZ do motor (Y para cima vira Z para cima).

## Escolher o motor

Aba **Análise** › grupo **Opções** › **Motor**.

| Motor | O que é |
|---|---|
| **Interno (Nucleo.cs)** (padrão) | Cálculo original do Estructto. Sempre roda. Verificação por tensões admissíveis com coeficiente de segurança "n". |
| **Motor: Python (NBR completas)** | Motor Python do PROSSEN. Combinações ELU/ELS da NBR 8681 e verificação de barras pela NBR 8800 (e 14762, 6118). |

Com o motor Python, F5 também envia o modelo ao motor em segundo plano (barra de progresso na barra de status). Em Análise,
a lista **Mostrar** ganha "Python: envoltória ELU (NBR 8681/8800)", "Python: envoltória ELS" e cada combinação do motor.
Diagramas, tabelas, reações, inspetor e relatório passam a usar o resultado do motor (captura 40-motor-python-momento).
Qualquer edição do modelo descarta o resultado do motor: calcule de novo.

### O que cada motor verifica

| | Interno | Python |
|---|---|---|
| Análise | Rigidez direta, pórtico plano, Euler-Bernoulli ou Timoshenko | Rigidez direta, pórtico espacial |
| Segurança | Tensões admissíveis, "n" global sobre cargas de serviço | Combinações ELU da NBR 8681 (γ, ψ) contra resistências de cálculo (γa1 = 1,10) |
| Barra de aço | σ = N/A + M/W, τ, flambagem de Euler, esbeltez, flecha no aproveitamento | NBR 8800: FLT, FLM, FLA, interação 5.5.1.2, cortante, esbeltez 5.3.4 e 5.2.8 |
| Flecha | Entra no aproveitamento | Verificada à parte (ELS, só vigas) |
| Placa de base, soldas, "Dimensionar" | Sim | Continuam no interno |

### Diferenças documentadas (UNIFICACAO-MOTOR.md)

A análise (reações, deslocamentos, N, V, M) confere entre os dois motores: maior diferença relativa de 10⁻¹⁰ nos exemplos
`guincho-*` e em um pórtico sintético. A **verificação** difere, porque o critério é outro:

1. Segurança: tensão admissível com "n" contra ELU com γ e ψ. As taxas não são comparáveis diretamente.
2. Esbeltez de barra tracionada: o interno usa L/r com L destravado (sem K), correto pela 5.2.8. O motor usa K·L/r e pode
   dar taxa maior (escora do guincho: 1,92 antes de enviar K e 3,84 depois). Correção pendente no motor.
3. Plano instável (comprimento de flambagem infinito): o interno reprova (taxa 99,99). O motor usa k = 1 nesse plano.
4. Flecha: dentro do aproveitamento no interno; separada no motor.
5. Resistências: o motor aplica FLT/FLM/FLA e interação da NBR 8800; o interno não.

Envio ao motor: o Estructto envia comprimentos de flambagem kx, ky e Lb do cálculo interno. Seção do catálogo do Estructto
(A, I, Ay, Az) substitui a do motor. Fu do aço: 450 MPa (A572), 400 MPa (A36, A500), senão 1,25·fy.

Não vão ao motor: molas e recalques (o envio para com aviso; escolha o motor Interno ou retire-os), carga giratória
(só a posição lançada), ψ informativo e combinações digitadas no Estructto (o motor gera as da NBR 8681).

## Unidades e convenções

| Grandeza | Unidade |
|---|---|
| Força | kN |
| Comprimento | m |
| Momento | kN·m |
| Carga distribuída | kN/m |
| Tensão e módulo no modelo do motor | kN/m² |
| Materiais (entrada) | MPa |
| Deslocamento | m (tabelas em mm); giro em rad |

Eixos globais: X e Y horizontais, Z para cima. Gravidade tem Fz negativo. No documento 2D: Fx positivo para a direita,
Fy positivo para cima, Mz positivo anti-horário, N positivo em tração, momento desenhado do lado tracionado.

## Atalhos principais

Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Y, **F5** calcular, **F4** enquadrar, Ctrl+K paleta de comandos, F1 ajuda,
Ctrl+E exportar CSV. A barra de acesso rápido tem Novo, Abrir, Salvar, Desfazer, Refazer e Calcular.
