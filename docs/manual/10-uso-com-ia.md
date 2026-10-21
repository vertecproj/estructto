# 10. Usar o Estructto por IA (MCP)

O motor também é um servidor MCP, chamado `estructto`. Um cliente de IA (por exemplo Claude Code) monta, calcula e verifica
modelos pelas mesmas funções da interface.

> **AVISO.** Ferramenta de apoio. Todo resultado exige verificação por engenheiro responsável, com ART.
> A IA não substitui essa verificação.

## Configuração

`codigo/.mcp.json`:

```json
{
  "mcpServers": {
    "estructto": {
      "type": "stdio",
      "command": "python",
      "args": ["-m", "prossen_estrutural.servidor_mcp"],
      "env": { "PYTHONPATH": "motor" }
    }
  }
}
```

Abra o cliente na pasta `codigo/`. Arquivos só dentro da pasta de trabalho do servidor (caminhos relativos a ela).

## Fluxo típico

`modelo_novo` → `estrutura_gerar` (ou `no_adicionar`, `secao_adicionar`, `material_adicionar`, `barra_adicionar`) →
`caso_adicionar` → `carga_adicionar` → `config_definir` → `projeto_calcular` → `verificacao_listar` → `relatorio_gerar`.
Para partir de um modelo pronto: `modelo_exemplos` + `modelo_abrir_exemplo`. Se algo falhar, comece por `modelo_validar`.
Editar o modelo descarta o último cálculo: rode `projeto_calcular` de novo. Taxa maior que 1 = reprovada.

## Ferramentas por tarefa

| Tarefa | Ferramentas |
|---|---|
| Arquivo e modelo | `modelo_novo`, `modelo_abrir`, `modelo_salvar`, `modelo_exemplos`, `modelo_abrir_exemplo`, `modelo_exportar_json`, `modelo_importar_json`, `modelo_importar_cype` |
| Consultar o modelo | `modelo_resumo`, `modelo_validar`, `modelo_geometria`, `elementos_listar` |
| Montar | `no_adicionar`, `material_adicionar`, `secao_adicionar`, `barra_adicionar`, `pavimento_adicionar`, `lote_adicionar`, `estrutura_gerar` |
| Editar e remover | `elemento_editar`, `elemento_remover` |
| Ações | `caso_adicionar`, `carga_adicionar`, `config_definir`, `acoes_gerar`, `combinacoes_listar` |
| Análise | `analise_caso`, `analise_combinacao`, `analise_modal`, `projeto_calcular`, `resultados_consultar`, `estabilidade_consultar` |
| Verificação | `verificacao_listar`, `verificacao_barra` |
| Ligações | `ligacao_tipos`, `ligacao_verificar`, `ligacoes_identificar`, `ligacoes_grupos`, `ligacoes_grupo_editar`, `ligacoes_grupo_dimensionar` |
| Catálogos | `perfis_listar`, `perfil_consultar`, `materiais_listar` |
| CYPE | `modelo_importar_cype`, `cype_comparar` |
| Normas e limitações | `normas_implementadas` |
| Relatório | `relatorio_gerar` |
| Recursos | `recurso_modelo`, `recurso_calculo` |

Listas grandes aceitam `limite` e `deslocamento` e informam o total.

## Unidades

kN, m, kN·m; tensões e módulos do modelo em kN/m². MPa só em chaves com sufixo `_MPa` (`fck_MPa`, `fy_MPa`, `E_MPa`).
Eixos globais: X e Y horizontais, Z para cima (peso próprio e cargas gravitacionais têm Fz negativo). Deslocamentos em m e rad.

## Dica de uso

Peça sempre a `normas_implementadas` antes de confiar num resultado fora do caso comum: ela traz, por item de norma, o que é
verificado e a limitação declarada.

## Relação com a interface

O MCP e a interface usam o mesmo motor, mas o servidor MCP guarda o seu próprio modelo. Para ver o resultado na interface,
salve o JSON (`modelo_salvar`) e abra-o como documento espacial. Na configuração de ações, o motor oferece vento de edificação,
por barra e NBR 8400; confira casos e combinações como descrito em [Cargas e vento](03-acoes.md).

Para ligações, identifique os grupos, revise seus membros e projetos e dimensione todos antes de adotar o detalhe.
Consulte [Ligações e bases](06-ligacoes-e-bases.md) para interpretar a pior do grupo e as limitações da placa.
