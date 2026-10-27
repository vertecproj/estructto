# W7-UPN: perfil U nas ligações (projetista e contrato)

Data: 27/10/2026. Branch `wt7-upn`. Pedido: "o upn não aparece no 3d e em nenhuma vista. esse perfil está completamente
bugado". Na imagem do usuário (Solda direta da alma, viga UPN, apoio W250×73), as vistas só mostravam o apoio e a solda.

## Causa raiz

`projeto._membros_geometria` monta `geometria.membros`, que o projetista usa para desenhar a viga e o apoio
(`GeoLig.De`, `LigacaoDesenho.cs`). Ela só aceitava perfil com `tipo == "I"`. U, UPN e Ue sumiam de `membros` em todos os
tipos. Por isso nenhuma vista (Elevação, Planta, Lateral, 3D) e nenhuma miniatura desenhava o U. O cálculo estava certo.

## Correções

| Onde | O quê |
|---|---|
| `projeto._membros_geometria` | Aceita U/Ue. O membro leva `forma: "U"`, `xg` (mm) e `abertura_y` (da `geometria` do tipo ou dos dados; padrão +1). No dobrado, `tw = tf = t` |
| `LigacaoDesenho.cs` (`GeoLig.De`, parse) | Viga U: alma com as costas em `y = −ab·xg` e as 2 mesas saindo das costas para `ab·y`, o mesmo referencial das chapas do contrato. Apoio U: alma no eixo, costas para a ligação (+x) e mesas para −x (alma do pilar e viga–viga). Na mesa do pilar: mesa centrada em y, alma em `y = −ab·(bf − tw)/2` |
| `ligacoes_flexiveis._chapa_extremidade` | Com viga U, chapa, parafusos e filetes ficam centrados no plano médio da alma, `yw = ab·(tw/2 − xg)`. Antes os filetes ficavam em ±tw/2, fora da alma do U. Isso também valia no 3D principal. Só a geometria muda; as verificações não mudam |
| `pontas._perfil` | U/Ue dobrado só tem `t`: `tw = tf = t`. Antes, a viga–viga com apoio U dobrado dava "perfil da barra 1 sem tw" |
| `ligacoes_flexiveis._governa` | Quando o resultado não passa, o motivo da troca cita a verificação que reprova. Exemplo: "cantoneira interna dentro da mesa do U (taxa 1,14)". Antes citava a maior resistência (0,42), que passava |
| Projetista, Sugerir | Se o `sugerir` devolve `tipo_ligacao`, a tela troca de tipo (perfis e esforços vão junto) e o rodapé diz o motivo. Antes, `tipo_ligacao` e `motivo` entravam como campos do tipo antigo |
| Projetista, erro do motor | Com erro (exemplo: rígida com U, G5), as vistas, os chips e o passo 3 não mostram mais o resultado antigo de outros perfis. O motivo fica no cabeçalho e no passo 3 |
| Projetista, rodapé | Status com largura máxima de 640: um texto longo alargava a janela |

O 3D principal (`Render3DMotor`) já mandava `abertura_y` e deslocava o U (`DeslU`). Ele não lê `membros`, porque desenha as
barras pelo modelo. A única mudança que ele recebe é a chapa de extremidade na alma.

## Provas

- Teste novo `testes/test_ligacoes_u.py::TestDesenhoU`. Perfis: UPN 200, U 8" × 17,1, U 200×75×3,00 e Ue 200×75×25×3,00.
  - Como viga, nos 6 tipos e nas duas aberturas: o U está em `membros` com d, bf, tw, tf e xg do catálogo, e as peças que tocam a alma estão na alma do U. No apoio em cantoneira, o assento cobre a mesa inteira.
  - Como apoio: chapa simples, viga–viga e solda direta.
  - Chapa de extremidade: os filetes ficam nas faces da alma.
- Capturas `ESTRUCTTO_CAPTURAS=upn` (`CapturasW7Upn.cs`, `70-upn-*`, log `70-upn.txt`). Cada caso exige o U em `membros` e as 3 peças desenhadas (alma e 2 mesas).
  - Solda direta: UPN 200 e Ue 200.
  - Chapa simples: UPN 300, também com abertura −1.
  - Cantoneira dupla: UPN 300.
  - Chapa de extremidade: UPN 200 e U 200×75×3 dobrado.
  - Viga–viga: UPN 300 e apoio UPN 300.
  - Apoio em cantoneira: U 10".
  - Troca chapa simples → solda direta: UPN 200, com o motivo.
  - Recusa da rígida com UPN 200.

## Pendências

- O `sugerir` da cantoneira dupla não tenta uma cantoneira menor quando a L76 não cabe dentro da mesa do U (UPN 200, U 10"). Ele troca para solda direta, com o motivo.
- No apoio U, o motor mede a face como no I: na alma, altura livre `d − 2(tf + r)`; na mesa, largura `bf` centrada. O desenho segue o motor.
