# Setas de carga e nós com profundidade (V1)

- Realista com DX (Viewport3DX): setas (`mCargas`) e nós escolhidos (`mNos`, octaedro) vão para o `Render3D` e passam pelo teste de profundidade; uma viga na frente os esconde.
- Sombreado (nativo): setas e nós escolhidos ficam na mesma cena `Viewport3D`. As setas sempre têm profundidade (a cena nativa não tem como desenhá-las por cima); "sempre visíveis" afeta só o nó escolhido.
- Analítico: barras e nós são linhas da sobreposição; setas e nós ficam por cima (padrão).
- Vista › Mostrar › "Cargas sempre visíveis": `Vista3D.CargasSempre` (null = padrão do estilo: ligado só no Analítico). Ao trocar de estilo volta a null e o botão acompanha o estado efetivo. No Realista DX, ligado desenha setas e nós na sobreposição, por cima de tudo.
- Prova: `ESTRUCTTO_CAPTURAS=cargasprof Estructto.exe --capturas <pasta>` (cwd no worktree) gera `58-cargas-profundidade-{dx,nativo}-{oculta,sempre}` com o pórtico 2x2: seta e nó escolhido atrás da viga da frente.
