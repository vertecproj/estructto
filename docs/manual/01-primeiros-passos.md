# 1. Começar um projeto

O Estructto v3 trabalha com um único modelo estrutural 3D, em um fluxo inspirado no AutoCAD Plant 3D. Planta e elevações são posições da câmera. O cálculo e a verificação usam o motor Python.

## Novo projeto em três passos

Na tela inicial, clique em **Novo projeto**, escolha um cartão de modelo ou pressione **Ctrl+N**. O mesmo fluxo está em **Arquivo › Novo**.

1. **Modelo:** escolha Vazio com grade, Galpão, Edifício, Pórtico, Treliça/cobertura ou Mezanino. Use Vazio com grade para desenhar do zero.
2. **Projeto:** preencha nome, pasta, cliente, engenheiro, empresa e normas. As unidades de cálculo são kN e m. Vento e sismo são definidos depois, em Ações.
3. **Grade e níveis:** informe os vãos em X e Y e as alturas dos níveis; confira a prévia, os perfis e o material quando houver geração de barras. Clique em **Criar**.

No passo 3, `3x6 2x5` significa três vãos de 6 m e dois de 5 m. `3x3,5` cria três alturas de 3,5 m. Separe itens por espaço ou ponto e vírgula; aqui a vírgula é decimal. Nos modelos de cobertura, confira os campos de vão, módulos, pé-direito e cumeeira.

O projeto é gravado como `nome.json` na pasta escolhida e abre em planta no primeiro nível ativado pelo assistente. Confira o nível indicado antes de desenhar. A grade mostra eixos numerados e filas com letras; os cruzamentos atraem o cursor. Veja [Modelar](02-modelo.md) para editar grade e níveis.

## Abrir, salvar e recuperar

**Ctrl+O** abre `.estructto`, `.json`, `.portico`, `.ftl` e `.ed3`. Os formatos antigos são convertidos ao abrir; revise restrições, seções e cargas da conversão. O original não é sobrescrito automaticamente: ao salvar um convertido, o programa solicita um nome e sugere `nome (3D).estructto`.

**Ctrl+S** salva. O formato `.estructto` v3 contém o modelo do motor, incluindo grade, níveis e projetos de ligações; `.json` salva o modelo do motor. Mantenha **Estructto.exe e a pasta motor/** juntos ao instalar a distribuição.

O salvamento automático cria cópias de recuperação; ajuste o intervalo em **Arquivo › Opções**. Na próxima abertura, aceite a recuperação oferecida e salve o documento recuperado. Ela não substitui seu salvamento normal.

A tela inicial lista recentes, permite fixar arquivos e abrir exemplos. Remover um recente da lista mantém o arquivo no disco. Em **Arquivo › Propriedades do projeto**, atualize os dados do carimbo.

## Unidades e sinais

| Grandeza | Unidade |
|---|---|
| Coordenadas e comprimento | m |
| Força e carga pontual | kN |
| Momento | kN·m |
| Carga distribuída / em área | kN/m / kN/m² |
| Materiais nos campos identificados | MPa |
| Chapas, parafusos e chumbadores na interface | mm, conforme o rótulo |
| Deslocamentos | modelo em m; tabelas em mm |

X e Y são horizontais; Z aponta para cima. Cargas gravitacionais têm Fz negativo. N positivo indica tração. Observe os eixos locais ao interpretar esforços. Nas coordenadas da linha de comando, use ponto decimal e vírgula entre componentes, como `#0,0,3.5`.

A opção de unidades exibidas nas Opções altera o texto do rodapé; o modelo continua em kN e m. Leia a unidade indicada em cada campo.

O motor da interface mantém um modelo ativo. Trocar de documento reinicia o histórico de desfazer quando ele é recolocado no servidor. Salve antes de alternar abas durante uma sequência de edições.

Continue em [Modelar](02-modelo.md), [Navegar](08-vista.md) e [Comandos e atalhos](12-comandos-e-atalhos.md). Leia as [limitações e responsabilidade técnica](11-limitacoes.md) antes de usar resultados em projeto.
