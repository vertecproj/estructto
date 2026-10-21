# Setas de carga com profundidade (V1)

- Realista com DX (Viewport3DX): as setas vão para a malha `mCargas` do `Render3D` e passam pelo teste de profundidade; uma viga na frente esconde a seta.
- Realista/Sombreado nativo: as setas já ficam no mesmo `Viewport3D` da cena, com profundidade.
- Analítico: barras e nós são linhas desenhadas pela sobreposição; as setas ficam por cima (padrão).
- Vista › Mostrar › "Cargas sempre visíveis": `Vista3D.CargasSempre` (null = padrão: ligado só no Analítico). No Realista DX ligado volta a desenhar as setas na sobreposição, por cima de tudo.
- Prova: `ESTRUCTTO_CAPTURAS=cargasprof Estructto.exe --capturas <pasta>` gera `58-cargas-profundidade-oculta` e `-sempre`.
- Os nós (pontos) continuam na sobreposição de linhas (Vista3D.Linhas.cs); não entraram nesta tarefa.
