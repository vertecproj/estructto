# W7-UI: nome da solda direta, ícones de perfil e rolagem do seletor (27/10/2026)

Pedidos do usuário e causa de cada um. Prova: `ESTRUCTTO_CAPTURAS=w7ui` (`CapturasW7Ui.cs`, também roda na bateria completa).

## 1. "Solda direta da alma": "por que é solda na alma? é só solda."
- Causa: o nome do padrão no motor era "Solda direta da alma", mas o padrão solda a alma e, com "soldar as mesas" (padrão no I), o contorno das mesas.
- Correção: `nome="Solda direta"` em `ligacoes_flexiveis.py` e a mesma troca nos motivos de `sugerir` (`ligacoes_flexiveis.py`, `ligacoes.py`). O id do tipo continua `solda_direta_alma` (arquivos .pcon salvos abrem).
- Teste: `testes/test_w7_nome_solda_direta.py`.

## 2. Ícone de perfil virava quadrado tracejado (reserva)
- Causa: `MiniaturaPerfil` só olhava `Biblioteca.AcharSecao` (núcleo do pórtico). O núcleo tem só W/HP (nome "W 250 x 73,0", não "W250x73"), L, U laminado e tubos metalon. Perfil do catálogo do motor (`/api/catalogo`: W250x73, UPN, IPE, HEA/HEB, Ue, U dobrado, L desigual, T, barra chata, tubos Vallourec) não achava seção e caía na reserva.
- Correção (ponto comum: todo ícone de perfil passa por `MiniaturaPerfil.Criar`, usada por `SeletorPerfil` no inspetor, projetista de ligações e lista): o que o núcleo não acha é lido do nome do motor (`Inspector.cs`, tabela `Nomes`) e a forma é desenhada por `DesenhoSecaoForma.Forma` (a mesma do desenho cotado do catálogo, tipos do motor: I, U, Ue, L, T, retangular, tubo_retangular, tubo_circular). Só nome livre ("A=.. I=..") e "(vários)" ficam na reserva.
- Prova: a captura pega um nome de cada família/prefixo do catálogo, pede o `props.tipo` ao motor e exige que TODOS os 2199 nomes do catálogo tenham a mesma forma (nenhum quadrado). `W7-miniaturas-familias.png`, `W7-miniaturas.txt`.
- Outros lugares que desenham ícone de perfil: nenhum além do `MiniaturaPerfil` (o catálogo e o gerenciador usam o desenho cotado do motor por `props.tipo`).

## 3. "sempre começa no fim da lista" (seletor de perfil)
- Causa: o ListBox é virtualizado (Recycling). Ao trocar o `ItemsSource` ao filtrar, o `ScrollViewer` guarda o deslocamento antigo e só o limita ao novo fim: com "upn" (20 itens, 8 visíveis) o offset ia de 25 a 12 e a lista aparecia rolada para o fim. Ao abrir, o `ScrollIntoView` rodava antes de o popup medir a lista e, quando rolava, deixava o item na borda de baixo (offset 20 para o item 27).
- Correção (`SeletorPerfil.Rolar`): a posição é dada depois do layout (no `Loaded` da lista e a cada `Filtrar`), em unidades lógicas: abrir = perfil atual 2 linhas abaixo do topo; filtrar ou limpar o filtro = topo com o 1º item selecionado.
- Prova (falhava no código antigo: "filtro upn: offset 12" e "ao abrir offset 20, esperado 25"): confere o `VerticalOffset` do `ScrollViewer` ao abrir (início, meio, perto do topo e último perfil), ao filtrar "upn" e ao limpar. `W7-seletor.txt`, `W7-seletor-*.png`.
