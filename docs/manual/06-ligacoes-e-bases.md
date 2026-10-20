# 6. Aba Ligações e bases

## Placa de base

Botão **Placa de base**: use no nó em que chega uma só barra (pé de pilar). Com um nó selecionado, abre a verificação dele.
A janela tem 3 passos, vista 3D, planta e ficha (capturas 20-placa-no1, 20-placa-no1-calculo). Verifica placa, chumbadores
ou parafusos e solda pilar-placa. O catálogo de chumbadores (Walsywa mecânico e químico, Indufix) está em
[CHUMBADORES.md](../CHUMBADORES.md); a escolha em [FIXACOES-GUIA.md](../FIXACOES-GUIA.md).

- Os valores de chumbador são cargas últimas médias de ensaio do fabricante; o coeficiente de segurança é do projetista
  (Walsywa, baixo / alto risco: estática 3 / 5, variável 4 / 5, dinâmica 7 / 15, choque 5 / 10).
- A placa é verificada no plano do apoio (piso, teto ou parede).
- Não verifica pressão no concreto, punção nem distância do chumbador à borda do concreto.

## Ligações (NBR 8800, motor Python)

| Botão | Verifica |
|---|---|
| **Parafusos** | Cisalhamento, pressão de contato e tração nos parafusos (captura 44-ligacao-parafuso) |
| **Filete de solda** | Metal de solda e metal-base |
| **Chapa simples** | Ligação flexível (shear tab): parafusos, chapa e solda (captura 44-ligacao-chapa-simples) |
| **Ligação de tração** | Área líquida efetiva, ruptura e escoamento |

Os campos de cada janela vêm do motor. As ligações não dependem do modelo aberto: informe os esforços de cálculo.
Grupo **Solda** › **Eletrodo**: eletrodo usado nas soldas das barras.

## Limitações das ligações

Efeito de alavanca e deslizamento (atrito) não implementados; furos alargados e alongados não tratados; chapa simples sem
interação V+M e sem rasgamento; apoio (mesa/alma do pilar) não verificado. Lista completa na [página 11](11-limitacoes.md).
