# Editor da ligação por componente (Frente C, C3)

Pedido: "quando vai editar manualmente é muito ruim de achar os lugares para editar".

## Seções (passo 2 da JanelaLigacaoProjeto)
Chapa, Parafusos, Soldas, Enrijecedores, Recortes, Folgas (expansíveis; só aparecem as que o padrão tem). Regra em `Secao()`:
`enrij*`, `t_enr_pilar`, `t_reforco` = Enrijecedores; `folga*`, `x_face`, `x_max`, `abertura_y`, `sobra`, `recuo` = Folgas;
`recorte*`, `desnivel` = Recortes; nomes/rótulos com solda = Soldas; furos e diâmetros = Parafusos; o resto = Chapa.
Perfis ficam no passo 1 (seção "barras", sempre visível).

## Clique na peça
Cada peça de `GeoLig` carrega `Tag = "<secao>.<campo>|<alternativa>..."` (`TagLig`, LigacaoDesenho.cs). A janela usa o 1º
campo que o padrão realmente tem (`CampoDoTag`). `IrParaCampo`: abre a seção, `BringIntoView` na célula e fundo de destaque 1,5 s.
- Desenho da ligação (4 vistas): clique sem arrastar (< 4 px) em parafuso, solda ou chapa/cantoneira/perfil. Parafuso e solda ganham da chapa; entre chapas vale a mais próxima do observador.
- Vista3D principal: chapas e cantoneiras viram `PecaLig` (Render3D.Marcar); `Vista3D.CliquePecaLig` -> `MainWindow.AbrirLigacaoDaPeca` abre o projetista do grupo daquele nó e vai ao campo. Parafusos ficam de fora (a malha de parafusos não tem octree por desempenho).

## Sem solução
`/api/ligacoes/otimizar` com `ok = false`: nada muda, faixa vermelha no topo do passo 2 com `motivo` e o link "Ir ao campo limitante"
(1º nome de `limite_atingido` que for campo do padrão; senão, o grupo da `verificacao_limitante`). Editar qualquer campo limpa a faixa.

## Prova
Capturas `51-projetista-editor-secoes`, `-clique-chapa`, `-sem-solucao` (ESTRUCTTO_CAPTURAS=projetista); `--testes-fluxo A,B,C,D,E`: 226 ok.
