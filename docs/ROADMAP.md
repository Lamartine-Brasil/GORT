# ROADMAP — GORT - Game Ocr RealTime (direção do dono)

> Modo padrão: **Camada** — a janela de saída nunca nasce sobre a área
> de captura. A Sobreposição existe como opção em Mostrar, sem substituir
> o padrão.

## Fase 0 — Base pronta ✅

- Camada é o modo padrão (`Profile.WindowMode = "layer"`).
- Saída estreia fora das áreas (`PlaceOutside`; tela cheia → inf-direita).
- Laço síncrono, CPU sob controle (threads ONNX limitadas, descarte por hash).

## Fase 1 — OCR ignora a janela de saída ✅

**Motivo:** sem isso, o OCR lê a própria tradução e realimenta o laço.

- [x] Exclusão de captura para Escuro/Camada ao mostrar
      (`TranslationWindows.ShowExcluded` + `HideAll`; overlay já tinha).
- [x] Concealer cobre as próprias no Mac/Linux; afinidade C8 no Windows.
- [x] Teste da matemática de interseção (`Overlap_*`).

## Fase 2 — Sobreposição dinâmica substitutiva ✅ (modo novo)

**Meta:** o texto traduzido aparece exatamente sobre o original,
substituindo-o visualmente (fundo cobrindo o original). É um MODO NOVO
(`replace` / Substituição): não substitui a Camada padrão; selecionável
em Mostrar → Janela de tradução → Substituição.

- [x] Fundo opaco do bloco cobrindo o original (`OverlayWindow.Substitute`).
- [x] Mesmo laço/sink/cores do overlay (`MakeSink`, `EnsureOverlayOcr`).
- [x] Testes: catálogo + render `replace.png`.

## Fase 3+ — a definir pelo dono.
