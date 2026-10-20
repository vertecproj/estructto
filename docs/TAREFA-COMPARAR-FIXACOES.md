# Tarefa: função "Comparar fixações" no Estructto

Criada em 19/10/2026 (Gabriel). Para o Bruno enviar ao Claude dele. **Status: concluída em 19/10/2026 (Bruno + agy) — falta apenas ligar o botão "Comparar…" em `Janelas.cs` (que estava em edição concorrente).**

> Para o Claude que for executar: leia antes `ftool/README.md`, `docs/FIXACOES-GUIA.md` (seções 0 e 5),
> `docs/CHUMBADORES.md` e `docs/DELEGAR-AGY.md` (delegue ao agy o que for válido e confira o resultado).
> Ao terminar, registre tudo no `docs/CHANGELOG.md`.

## Objetivo

Na placa de base, o usuário escolhe **2 ou mais fixações** (linha + diâmetro + embutimento, ex.: `CB 12300`, `WB 12400`,
`WQE 500 1/2"`) e vê **lado a lado** a diferença entre elas, com texto de ajuda tirado dos catálogos. Serve para decidir
entre mecânico, químico etc., não só pelo número, mas pelo uso recomendado.

## O que a tela de comparação deve mostrar (uma coluna por fixação)

1. **Atributos** (tabela 5.1 do guia): família, acionamento, elemento de fixação, concreto do ensaio, natureza da carga
   (ruptura/recomendada), tração e corte tabelados, carga dinâmica, distâncias reduzidas, materiais-base, recuperável,
   tempo de cura, acabamento.
2. **Cargas:** valor do catálogo (com unidade e fck do ensaio) **e** valor ajustado usado na verificação
   (Tref, Vref com ψc = √(fck laje/fck ensaio) ≤ 1 e ψ dos cones).
3. **Verificação com os esforços da placa atual:** n no concreto ≥ n,ref; n do aço da barra (químicos); espaçamento;
   aproveitamento; passa / não passa. **Usar `PlacaCalc.CandidatoChumbador`** (já existe em `codigo/portico/src/Nucleo.cs`),
   que é o mesmo cálculo da placa — não criar fórmula nova.
4. **Uso recomendado / usos típicos / pontos de atenção** de cada linha (seção 2 do guia).
5. **Texto do par** quando houver (seção 5.2 do guia, ex.: "CB × WB"): "em que diferem", "escolha A quando", "escolha B quando".
6. **Avisos** (seção 5.3 do guia): ruptura × recomendada não comparáveis direto; concretos diferentes (20 × 28 × 30 MPa);
   linha sem corte tabelado (WQI 44, WQE 500); WQA verificada com n = 1 sobre carga recomendada; linhas só informativas.

## Como implementar (sugestão, respeitando a arquitetura atual)

1. **Dados:** transformar a tabela 5.1 em `codigo/portico/fixacoes.csv` (chave = campo `linha` do `chumbadores.csv`,
   ex.: `Walsywa WB`) e os textos (seção 2 e pares da 5.2) em um recurso embutido (ex.: `fixacoes-textos.md` ou CSV).
   Embutir igual aos outros CSV: `Estructto.csproj` (EmbeddedResource) e `compilar.ps1` (/resource).
   O guia `docs/FIXACOES-GUIA.md` continua sendo a fonte; os dados saem dele.
2. **Motor (`Nucleo.cs`):** carregar `fixacoes.csv` pelo mesmo helper `Biblioteca.Linhas`; uma classe simples com os
   atributos e os textos por linha e por par. Nada de cálculo novo.
3. **Interface (`codigo/Estructto/`):** um botão "Comparar…" na janela da placa (perto da tabela "Chumbadores (todos os
   catálogos)" em `Janelas.cs`) que abre a comparação: escolher 2+ itens da lista, mostrar as colunas acima.
   Seguir o estilo visual existente (medidores, cores de passa/não passa).
4. **Linhas só informativas** (WLT, Golpe de Metal, PU 500): não estão no `chumbadores.csv`; aparecem na comparação só
   com atributos e textos, sem verificação, com aviso.
5. **Indufix Parabolt:** está no cálculo mas não tem texto no guia (falta o PDF do catálogo). Mostrar só atributos
   básicos e aviso "sem descrição de catálogo" até termos o PDF.

## Não pode quebrar

- `codigo/portico/chumbadores.csv`, `PlacaCalc.Calcular`/`AvaliarChumbador` e o critério de `docs/CHUMBADORES.md`.
- A planilha (`ELEMENTOS DE MÁQUINA\PLANILHA DE CÁLCULO.xlsx`) não muda com esta tarefa.
- Não mover arquivos de `codigo/`, `exemplos/` nem o `Estructto.exe` (ver tabela de pastas no README).

## Pronto quando

- `codigo\portico\compilar.ps1 -Testes`: todos passam, com testes novos: `fixacoes.csv` carrega todas as linhas do
  `chumbadores.csv`; o n mostrado na comparação é igual ao de `PlacaCalc.CandidatoChumbador` para o mesmo item.
- `dotnet build` do Estructto sem erro; `Estructto.exe` republicado (comando no README) e aberto para conferir a tela.
- Comparação testada pela interface com pelo menos: `CB 12300` × `WB 12400` × `WQE 500 1/2"` numa placa calculada.
- `docs/CHANGELOG.md` atualizado e esta tarefa marcada como feita aqui.
