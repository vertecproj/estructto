# Grupos de ligações: a pior do grupo dimensiona todas

Data: 20/10/2026 (branch `wt-grupos`, Fase 3 / L2 do [STATUS.md](STATUS.md)).
Pedido: "normalmente calculamos a pior ligação e aplicamos para o resto: identifique ligações iguais e agrupe (o usuário pode
mexer nos grupos), pegue a pior do grupo para dimensionar e aplique a todas".

Código: motor `codigo/motor/prossen_estrutural/aco/ligacoes_modelo.py` (testes `testes/test_ligacoes_modelo.py`);
interface `codigo/Estructto/JanelaLigacoesGrupos.cs` (janela, rotas no `DocumentoEspacial`, faixa, placa do grupo, capturas `49-*`).

## 1. Identificar

Ligação = **ponta de barra num nó** (`{no, barra, ponta: "i"|"j"}`). Entram os nós com 2 ou mais barras e os nós apoiados
(apoio = uz travado ou 2+ translações; o pórtico plano trava uy em todos os nós e isso não conta). Barras de concreto ficam fora
(ligação monolítica).

Classe da barra: `params.papel` (pilar/viga/diagonal) quando existe; `trelica` = diagonal; sem papel, pela geometria
(vertical > 0,99 = pilar; < 30° da horizontal = viga; senão diagonal). Rotulada na ponta = `rotula_i/j` ou `trelica`.

Em cada nó:

1. **Emenda**: só duas barras da mesma classe, sem apoio (reta, cumeeira, joelho) → uma ligação (membro = a barra de menor id).
   Também quando, sem pilar no nó, duas vigas rígidas se encontram e outras barras (terças, diagonais) chegam nelas (cumeeira do galpão).
2. **Par contínuo** (passa pelo nó, não é ligação): duas barras colineares (1°) da mesma classe e não rotuladas. Prioridade
   pilar > viga (só sem pilar no nó) > diagonal (só sem pilar e sem viga).
3. As demais pontas "terminam" e viram ligação:

| Tipo (`TIPOS`) | Regra | Projeto sugerido |
|---|---|---|
| `base` | ponta de pilar ou viga num nó apoiado | `placa_base` |
| `viga_pilar_mesa` / `viga_pilar_alma` | viga chegando num pilar (contínuo ou que termina): mesa se a viga corre no eixo local z do pilar (altura do perfil, `solver.eixos_locais`), senão alma | `chapa_simples` |
| `viga_viga` | viga chegando noutra viga (a contínua ou, sem par, a de maior área) | `chapa_simples` |
| `emenda` | ver acima | `parafuso` |
| `gusset_contraventamento` | diagonal chegando num nó com pilar ou viga | `ligacao_tracao` |
| `no_trelica` | todas as barras do nó são diagonais/treliça | `ligacao_tracao` |

Pilar que termina sem apoio (topo, pilar sobre viga) é o apoio das vigas: não vira ligação própria.

## 2. Assinatura e grupos

Assinatura = `tipo | seção da barra | seção do apoio | papel do apoio | ângulo | condição | material`:
papel do apoio = `pilar continuo`, `pilar termina`, `viga` ou `base` (gusset no pé do pilar ≠ gusset no topo); ângulo entre as
retas arredondado a 1° (base: com a vertical; emenda: desvio da reta); condição `rigida`/`rotulada` (ponta rotulada ou, na base,
rx e ry do apoio livres). Emenda: seções em ordem alfabética (independe da ordem das barras). Mesma assinatura = mesmo grupo.

JSON do modelo (chave nova, `versao_formato` 2 só quando há grupos; sem grupos o arquivo sai como 1 e os arquivos antigos abrem):

```json
"ligacoes": {"grupos": {"G1": {"nome": "Base de pilar W360x51", "tipo": "base", "assinatura": "base|W360x51|||0|rigida|A-572 GR.50",
  "membros": [{"no": "N0-0-0", "barra": "P1-0-0", "ponta": "i"}], "projeto": {"tipo_ligacao": "placa_base", "dados": {"pelo_perfil": true}},
  "travado": false, "manual": false}}}
```

## 3. Edições do usuário e re-identificação

`editar(modelo, {acao, ...})`: `renomear`, `travar`, `projeto` (tipo_ligacao + dados; chapa simples ganha `tw` da viga),
`mover` (membros → destino ou grupo novo), `dividir` (membros → grupo novo com o mesmo projeto), `unir` (no primeiro).
Mover/dividir/unir marcam o grupo como `manual`; grupo `travado` recusa mover/unir.

`identificar(modelo)` depois de editar o modelo:
- membro que sumiu (barra apagada, nó desfeito) sai do grupo;
- grupo travado ou manual mantém os membros que ainda existem; nos outros, membro cuja assinatura mudou sai;
- membro sem grupo vai para o primeiro grupo não travado com a mesma assinatura, ou para um grupo novo (`G<n>`, ids não reaproveitados);
- grupo vazio é apagado. Nome, trava e projeto ficam.

## 4. Pior do grupo e dimensionar

`dimensionar_grupo(modelo, calculo, grupo)`: esforços na ponta (linha 0 ou última do `solver.Resultado.esforcos`, N > 0 tração)
em **todas as combinações ELU/ELU_EXC**; para cada membro e combinação roda o verificador do `tipo_ligacao`; taxa do membro = pior
combinação; **governante = membro de maior taxa**. O projeto é um só para o grupo ("aplicar" = o mesmo detalhe vale para todos).

| tipo_ligacao | Esforços passados | Função |
|---|---|---|
| `placa_base` | N, V = √(Vy²+Vz²), M = My (eixo forte) e, numa segunda verificação, M = Mz com `eixo_fraco`; vale a pior | `placa_base.verificar_placa_base` (modelo plano) |
| `chapa_simples` | V = √(Vy²+Vz²) | `ligacoes.verificar_ligacao` |
| `ligacao_tracao` | N = \|N\| (compressão tratada como tração: chapa sem flambagem, `ponytail:`) | idem |
| `parafuso` | Ft = max(N, 0), Fv = √(Vy²+Vz²) | idem |
| `solda_filete` | F = √(N²+Vy²+Vz²) | idem |

Sem projeto: governante pelo proxy de demanda (vigas: |V|, depois |N|, |M|; diagonais e treliça: |N|; base e emenda: |M|).
Erro de dados de um membro fica em `membros[].erro` e `erros` (os outros continuam).

**Estender** (outros agentes vão criar tipos): tipo de nó novo → `TIPOS` + regra em `_classificar_no`; tipo de ligação novo →
`VERIFICADORES[tipo] = f(modelo, membro, esforcos_da_ponta, dados) → {taxa_max, ok, ...}` (tipo do contrato sem entrada aqui: `verificador()` usa um genérico com `esforcos_contrato`, ver [LIGACOES-UI.md](LIGACOES-UI.md)) (e, se for de `aco.ligacoes`, só a
linha em `_ESFORCOS_LIG`). A interface lista os tipos de `GET /api/ligacoes/tipos` sozinha.

## 5. Onde está exposto

| Camada | Itens |
|---|---|
| `projeto.py` | `ligacoes_identificar`, `ligacoes_grupos(modelo, calculo=None)`, `ligacoes_editar`, `ligacoes_dimensionar` |
| Web (`servidor.py`) | `POST /api/ligacoes/identificar`, `GET /api/ligacoes/grupos`, `POST /api/ligacoes/grupos/editar`, `POST /api/ligacoes/grupos/{id}/dimensionar` |
| MCP | `ligacoes_identificar`, `ligacoes_grupos`, `ligacoes_grupo_editar`, `ligacoes_grupo_dimensionar` (46 ferramentas) |

Identificar/editar **empilham desfazer mas não descartam o cálculo** (só mexem em `ligacoes`). `GET /api/ligacoes/grupos` com
cálculo devolve cada grupo já dimensionado (taxa por membro, governante, taxa máxima).

## 6. Interface: Gerenciador de ligações

Faixa **Ligações e bases › Grupos › Gerenciar ligações** (e Ctrl+K "Gerenciar ligações"), só no modelo 3D (espacial).
Janela não modal (`JanelaLigacoesGrupos`):

- **Esquerda**: cartões dos grupos com glifo do tipo, quantidade, pior ligação, barra de taxa e cadeado (clique trava/destrava).
  Ctrl + clique marca cartões para **Unir**. Soltar ligações arrastadas num cartão = mover para ele.
- **Meio**: nome editável (Enter grava), Travar, assinatura; tabela das ligações (nó, barra · ponta, combinação crítica, |V|, |N|,
  |M|, taxa) ordenável pelo cabeçalho; clique/Ctrl/Shift marcam; arrastar, "Mover para…" ou botão direito movem; "Separar em grupo novo".
  A ligação que governa tem a borda laranja.
- **Direita**: projeto do grupo (tipo + campos do motor, em mm), **Dimensionar pela pior** e a ligação que governa com as
  verificações. Em placa de base, o botão abre a **JanelaPlaca** com o pilar que governa (pórtico 2D de uma barra com N, V, M da
  pior combinação na base; eixo fraco se o pior foi com Mz) e Aplicar grava a placa no projeto do grupo.
- Escolher um grupo (ou ligações) seleciona os nós e as barras na Vista3D (`Vista3D.Selecionar`).
- Sem cálculo, Dimensionar roda o F5 antes. Re-identificar guarda as edições (seção 3).

Capturas: `ESTRUCTTO_CAPTURAS=ligacoes` (galpão do motor calculado, gerenciador, destaque 3D, base dimensionada, placa da pior base,
viga–pilar com chapa simples). Também entram na rodada completa.

## Limites

- Placa de base é o modelo plano do núcleo: My e Mz verificados separados (não biaxial).
- Esforços na ponta = esforço na seção da barra; carga aplicada no próprio nó não entra (vai direto ao apoio).
- Emenda com o mesmo projeto `parafuso` (um parafuso) é só uma sugestão; emendas de verdade esperam os tipos novos (L1).
- Purlins/terças rotuladas que chegam no topo do pilar viram `viga_pilar_*` (o apoio real é a viga de cobertura); o usuário move.
- Gusset em compressão usa a verificação de tração da chapa (sem flambagem da chapa).
