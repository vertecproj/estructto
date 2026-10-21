# Sombra do chão (V2)

- Causa das manchas/linhas soltas: no Sombreado nativo, `Vista3D.Sombra()` desenhava um quad plano por barra projetado pela luz (textura alfa), com faixas soltas longe do modelo. No DX, o disco do chão ia até 3R e entrava na cobertura automática do `ShadowMap3D` (4096²), gastando resolução fora do modelo.
- Nativo (Sombreado): malha de sombra projetada removida; fica só o contato suave (`MatAo`) no pé dos pilares.
- DX (Realista): chão recebe `ShadowMap3D` (material PBR com `RenderShadowMap`); último anel do chão de 3,0R para 1,5R (ainda some no canvas) para a sombra cobrir só a cena útil.
- Vista › Mostrar › "Sombra" (`Vista3D.Sombra`, padrão ligado): liga/desliga o shadow mapping do DX. Decisão: botão na faixa Vista junto de "Cargas", não na página Opções do Backstage (é preferência de vista, não do programa).
- Prova: `ESTRUCTTO_CAPTURAS=sombra Estructto.exe --capturas <pasta> <modelo>` gera `59-sombra-{dx,nativo}-{alto,rasante}` (pórtico 2x2). Antes/depois em `docs/v3/render/v2/`.
- Regressão real: a mesma captura abre `testes-aceite/PLATAFORMA.ed3` (gitignored; pula se faltar) em Realista, vistas alto e rasante (`59-sombra-plataforma-*`; antes/depois em `v2/`). Correção extra: disco do chão = modelo + 0,9·raio (era 1,3·raio) e `SSAOSamplingRadius` = 0,012·raio da cena (0,1–1,5 m).
