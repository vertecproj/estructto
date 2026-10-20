# Exportar o modelo 3D (DWG, DXF, STEP)

Data: 20/10/2026. Código: `codigo/Estructto/Exportar.cs` (exportadores, testes, interface) e `codigo/Estructto/GeometriaPerfis.cs`
(extrusão dos perfis). Branch `wt-export`.

## Uso

- **Arquivo › Exportar** (bastidores): botões **DWG…**, **DXF…**, **STEP…**. Na paleta (Ctrl+K): "Exportar DWG", "Exportar DXF",
  "Exportar STEP".
- Vale para os dois tipos de documento: modelo espacial (JSON do motor, via `DocumentoEspacial.ParaVista`) e modelo 2D (via
  `Modelo3D.De`: o plano XY do 2D vira o plano XZ). Exporta a geometria, sem cargas nem resultados.
- A forma de cada seção é a mesma do modo Realista da Vista3D (`MainWindow.PerfilDaSecao`: `props` do motor ou catálogo do Estructto).

## Geometria

Cada barra vira a extrusão do contorno da seção de ni até nj, com o mesmo triedro da Vista3D (v = `Modelo3D.EixoY(e, rot + RotPerfil)`,
u = v × e): I/H, U/C (Ue sem enrijecedores), L, tubo retangular e caixão, tubo circular (24 segmentos), retângulo e círculo maciços.
Seção sem dados de forma: caixa de mesma A e I (como na Vista3D); sem nada: quadrado de 100 mm. Pontas retas, nós sem acabamento
(as peças se cruzam nos nós, como na vista).

`GeometriaPerfis.cs` é uma **cópia** de `CriarForma`/`Extrudar` da Vista3D (lá são privados e o arquivo é de outro agente;
comentário `ponytail:` no topo). Unificar: a Vista3D passa a usar `GeometriaPerfis.CriarContorno` e apaga a cópia dela.

Coordenadas do modelo em m → arquivos em **mm**.

## DWG / DXF (ACadSharp 3.8.0, MIT)

| Item | Como |
|---|---|
| Versão | AC1032 (AutoCAD 2018), DWG binário ou DXF texto |
| Unidades | `$INSUNITS` = milímetros, `$MEASUREMENT` = métrico |
| Sólido | **POLYFACE MESH** por barra (faces laterais em quadriláteros, tampas em triângulos). ACadSharp não gera os dados ACIS de um 3DSOLID, por isso malha |
| Modelo analítico | uma LINE por barra (ni → nj) na camada `ANALITICO` (cor 1) |
| Camadas | uma por seção e tipo: `ACO_<seção>` ou `CONCRETO_<seção>` (forma maciça = concreto, como na Vista3D). Caracteres proibidos e vírgula viram `_` |

## STEP AP214 (escrito à mão, ISO 10303-21)

- Esquema `AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }`, unidade mm (`SI_UNIT(.MILLI.,.METRE.)`), tolerância 0,01 mm.
- Montagem: um `PRODUCT` com o nome do modelo; cada barra é um `PRODUCT` (id = id da barra, nome = "id seção", por exemplo
  `P1-0-0 R 250x500`) ligado por `NEXT_ASSEMBLY_USAGE_OCCURRENCE` + `CONTEXT_DEPENDENT_SHAPE_REPRESENTATION` com transformação
  identidade (a posição já está nos pontos).
- Cada barra: `FACETED_BREP_SHAPE_REPRESENTATION` → `FACETED_BREP` → `CLOSED_SHELL` de `FACE_SURFACE` planas (`PLANE` pela normal de
  Newell) limitadas por `POLY_LOOP`; a tampa do tubo tem `FACE_OUTER_BOUND` + `FACE_BOUND` do furo. Pontos compartilhados entre faces.
- Texto fora do ASCII (Ø) em `\X2\hhhh\X0\`.

## Testes

`Estructto.exe --testes-exportar <pasta> [raiz do repo]` (sem motor; resultado em `<pasta>\testes-exportar.txt`, 38 asserções).
Exporta o guincho (`exemplos/guincho-coluna-escorada.estructto`, 2D) e o edifício do motor (`codigo/motor/exemplos/edificio_concreto.json`)
e confere:

- uma peça por barra; todo sólido fechado e bem orientado (cada aresta orientada aparece uma vez, e a oposta também);
- DWG e DXF relidos com ACadSharp: linhas = malhas = barras, linhas na camada `ANALITICO`, camadas esperadas, número de faces,
  unidades mm e versão AC1032, caixa das linhas = caixa dos nós (mm), caixa das malhas contém a dos nós e não passa dela mais de meia seção;
- STEP: número exato de entidades, todas as referências `#n` resolvidas, ids únicos, aspas e parênteses fechados, uma
  `FACETED_BREP` por barra e um `PRODUCT` por barra + a montagem.

Conferido também fora do Estructto: `steputils` (pip) lê os dois `.stp`; `ezdxf.recover` lê os dois `.dxf` sem erro nem correção.
Amostras em `C:\dev\build\EstructtoV2-export\amostras` (guincho e edifício em .dwg, .dxf e .stp).

## Limites

- DWG sem 3DSOLID (malha); o DWG só foi relido pelo próprio ACadSharp (sem ODA/AutoCAD nesta máquina). O DXF passou no ezdxf.
- Sem acabamento nos nós, sem chapas/ligações, sem cargas e resultados. Ue sem enrijecedores; 2L costas com costas sai como I.
- STEP só com faces planas (tubo circular em 24 facetas), sem cor/material.
