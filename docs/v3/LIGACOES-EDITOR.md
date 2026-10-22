# Editor da ligação por componente (Frente C, C3)

Pedido: "quando vai editar manualmente é muito ruim de achar os lugares para editar".

## Seções (passo 2 da JanelaLigacaoProjeto)
Chapa, Parafusos, Soldas, Enrijecedores, Recortes, Folgas (expansíveis; só aparecem as que o padrão tem). Regra em `Secao()`:
`enrij*`, `t_enr_pilar`, `t_reforco` = Enrijecedores; `folga*`, `x_face`, `x_max`, `abertura_y`, `sobra`, `recuo` = Folgas;
`recorte*`, `desnivel` = Recortes; nomes/rótulos com solda = Soldas; furos e diâmetros = Parafusos; o resto = Chapa.
Perfis ficam no passo 1 (seção "barras", sempre visível).

## Clique na peça
O MOTOR põe `campo` em cada peça de `geometria` (chapas, cantoneiras, parafusos, porcas, soldas e `membros`): `aco/ligacoes_campos.py`
(chapas pelo `nome` exato do contrato; `campo` posto pelo gerador vence, ex.: `b_solda_2` por barra do nó de treliça e `b_interface`
no gusset). `GeoLig` copia para `Tag`; a janela usa o 1º campo que o padrão tem (`CampoDoTag`). `IrParaCampo` abre a seção,
`BringIntoView` e destaca a célula inteira (rótulo + controle): preenchimento de acento + borda de 2 px, cheio por 0,9 s e some até 1,5 s.
Sem campo para a peça: faixa âmbar no passo 2 + barra de status (e, na Vista3D, toast).
Limites conhecidos: soldas de chapa de topo/flange plate apontam para `b_mesa`/`b_solda_mesa` (o contrato não separa mesa e alma);
a solda da ligação de tração aponta para `t` (perna = 0,6 t, derivada); a viga esquemática (tipos antigos, sem perfil) não é clicável.
- Desenho da ligação (4 vistas): clique sem arrastar (< 4 px). Parafuso e solda ganham da chapa; entre chapas, a mais perto do observador.
- Vista3D principal: chapas e cantoneiras viram `PecaLig` (Marcar) e parafusos entram numa lista própria (teste raio x eixo, sem octree).
  `Vista3D.CliquePecaLig` -> `MainWindow.AbrirLigacaoDaPeca` abre o projetista do grupo (`Ligacao3D.Grupo` = id do motor) no campo.
  Custo medido (galpão, 174 parafusos): Acertar completo 0,3 ms/clique; só o teste de parafusos 0,005 ms. Clique numa barra continua selecionando a barra.

## Sem solução
`/api/ligacoes/otimizar` com `ok = false`: nada muda; o motor devolve `campos_limite` (`{campo, valor, tipo: no_maximo|degrau_barrado|faixa_vazia}`)
e `campo_limitante` (campo do tipo de verificação que governa: solda, parafuso ou chapa). Faixa vermelha no topo do passo 2:
título, "Governa: <verificação> · taxa", "Limite atingido: <rótulo = valor>" e o link "Ir ao campo". Editar qualquer campo limpa a faixa.

## Prova
Motor: `testes/test_ligacoes_campos.py` (20 padrões + variantes: toda peça tem campo real; sem solução estruturado). Capturas (ESTRUCTTO_CAPTURAS=projetista): autoteste de cada padrão (toda peça resolve a um campo, `pecas-sem-campo.txt`), clique real na chapa e no parafuso da Vista3D abrindo o projetista no campo, clique na barra seleciona a barra, seções recolhidas, sem solução; `--testes-fluxo A,B,C,D,E` verde.
