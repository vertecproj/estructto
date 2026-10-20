# 5. Aba Dimensionamento

Taxa de aproveitamento = solicitação / resistência. **Taxa maior que 1 = barra reprovada.**

## Verificar

| Botão | O que faz |
|---|---|
| **Verificação de barras** | Lista todas as barras com taxa, critério determinante, item da norma e combinação crítica; ordenável e filtrável; duplo clique abre a barra (capturas 42-verificacao-barras, 42-verificacao-barras-reprovadas). Calcula o modelo antes |
| **Mapa de aproveitamento** | Colore as barras pela taxa (captura 36-mapa-calor) |
| **Verificar barra** | Abre a barra selecionada: critério × item da norma × combinação × Sd ≤ Rd (capturas 43-verificar-barra-aco, 43-verificar-barra-concreto). No motor Interno, abre a janela antiga com a lista de perfis da família |

Normas verificadas pelo motor Python: aço laminado e soldado (NBR 8800), perfis formados a frio (NBR 14762), concreto armado
(NBR 6118). Itens e limitações: [página 11](11-limitacoes.md).

## Otimizar

- **Dimensionar**: troca cada barra pelo perfil mais leve da mesma família que passa, considerando a envoltória; recalcula e repete até estabilizar. Com barras selecionadas (aba Barra), atua só nelas. Roda no cálculo interno.
- **Catálogo de perfis**: perfis do motor com A, I, W, Z, r, J, Cw (captura 46-catalogo).

## Critérios

- **Segurança**: coeficiente "n" mínimo (motor Interno).
- **Flecha L/**: flecha admissível = vão / número (motor Interno). No motor Python a flecha é verificada à parte, em ELS, só em vigas.

## Como ler uma reprovação

1. Abra **Verificação de barras** e ordene pela taxa.
2. Veja o critério e o item. Esbeltez, FLT, interação, cortante e flecha pedem soluções diferentes.
3. Abra **Verificar barra** e confira Sd e Rd, e a combinação crítica.
4. Troque o perfil (ou use **Dimensionar**) e calcule de novo.

Itens "não verificado" também reprovam: o motor não aprova o que não sabe verificar (ver limitações).
