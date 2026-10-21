# 3. Aba Ações

Casos de carga, cargas, peso próprio e ações automáticas (vento, sismo). As ações automáticas usam o motor Python;
o Estructto envia o modelo antes de abrir a janela.

## Casos de carga

Os casos têm tipo G (permanente), Q (variável), V (vento), S (sismo) ou E (excepcional). **Caso ativo** recebe as cargas que você lança.
A janela de casos é a do motor (captura 32-acoes-casos).

## Cargas

Escolha o nó ou a barra na vista: o painel **Propriedades** lança cargas do caso ativo: nodal (Fx, Fy, Fz), distribuída
(global, uniforme ou trapezoidal, kN/m) e pontual na barra. **Carga em área** (Ações › Cargas ou Modelo › Chapas › Laje)
distribui p (kN/m²) nas vigas de um nível. As setas do caso ativo aparecem na vista (Vista › Mostrar › Cargas).

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
e ELS (quase permanente, frequente, rara).

## Dica

Gerar vento e sismo cria casos V (vento) e S (sismo: SX, SY e as torções acidentais SX_TOR, SY_TOR). Depois rode **Calcular** (F5): as combinações incluem esses casos automaticamente.
