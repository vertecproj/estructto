# 3. Aba Ações

Casos de carga, cargas, peso próprio e ações automáticas (vento, sismo). As ações automáticas usam o motor Python;
o Estructto envia o modelo antes de abrir a janela.

## Casos de carga

Os casos têm tipo G (permanente), Q (variável), V (vento) ou E (sismo). **Caso ativo** recebe as cargas que você lança.
Com o motor Python, a janela de casos é a do motor; com o Interno, é a janela antiga de casos e combinações (captura 24-casos-combinacoes).

## Cargas (documento 2D)

| Grupo | Campos | Convenção |
|---|---|---|
| Carga no nó | Fx (kN), Fy (kN), Mz (kN·m) | Fx para a direita; Fy para cima (gravidade é negativo); Mz anti-horário. **Aplicar no nó** substitui a carga existente |
| Carga distribuída | qx, qy (kN/m); q⊥ início e q⊥ fim (kN/m) | qy de gravidade é negativo; q⊥ é perpendicular à barra e varia linearmente do início ao fim |
| Outras | Peso próprio; Limpar cargas | Peso próprio soma massa do catálogo (ou densidade × área). Limpar atua na seleção, ou no modelo todo sem seleção |

Nada selecionado: clique nos nós ou barras depois de apertar o botão. Com seleção: aplica nos selecionados.

## Ações automáticas

### Pavimentos
Cotas dos pavimentos e diafragma rígido. Vento, sismo e deslocamentos entre pavimentos usam essas cotas. Defina antes de gerar vento ou sismo.

### Vento (NBR 6123)
Entrada: V0, S1, S2 (categoria e classe), S3 e coeficientes de arrasto da edificação. Saída: Vk = V0·S1·S2·S3, pressão dinâmica
q = 0,613·Vk² (N/m², com Vk em m/s) e forças estáticas equivalentes por pavimento (kN) (captura 29-acoes-vento).
Não calcula vento dinâmico, efeitos de vizinhança, torção por excentricidade nem alta turbulência.

### Sismo (NBR 15421)
Entrada: zona sísmica, classe do terreno, categoria de utilização. Saída: forças horizontais equivalentes
por pavimento e momento de torção acidental. Só estruturas regulares; sem análise modal espectral. Edição da norma usada: 2006.

### Combinações (NBR 8681)
Define o uso da edificação (ψ0, ψ1, ψ2) e o tipo de edificação (γ). O motor gera as combinações ELU (normais e com sismo)
e ELS (quase permanente, frequente, rara). Combinações digitadas pelo usuário no documento 2D não vão ao motor.

## Dica

Gerar vento e sismo cria casos V e E. Depois rode **Calcular** (F5): as combinações incluem esses casos automaticamente.
