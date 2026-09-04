# Sugestões de velocidade e qualidade do OCR — avaliação do dono

> Gerado pela varredura de 10 agentes (só leitura). Marque `[x]` no que
> aprovar e diga os números para implementar. Regra: nenhum valor 🔒 é
> recalibrado — só parâmetros novos/opt-in ao lado.
>
> Legenda — **Ganho**: porte do efeito. **Risco**: chance de quebrar algo.
>
> **Status: itens 1–6 IMPLEMENTADOS e validados (196/196 testes verdes,
> 2 validadores).** Notas: item 2 sem compartilhar buffer (parte média
> pulada); item 3 com ressalvas leves (mtime+tamanho, sem lock —
> single-thread RF-009).

## Bloco 1 — Ganhos grandes, risco baixo

- [x] **1. Cachear o regex do dicionário** — `Text/DictionaryStore.cs:53-73`.
  Hoje `Apply()` reconstrói `Sort` + `Join` + `Regex.Escape` + `new Regex`
  por bloco, a cada ciclo. Guardar `Regex` compilado + `map`, reconstruir
  só em `Load()`/`AddPair()`. **Ganho: alto. Risco: baixo.**
- [x] **2. Fundir cópias do pré-processamento** — `Imaging/Preprocess.cs:45,69-71,81-83`.
  `Clone()` + cópia byte a byte + `Resize` mesmo com zoom 1.0. Usar o
  buffer direto, early-out sem resize, compartilhar leitura no `disabled`.
  **Ganho: alto. Risco: baixo/médio.**
- [x] **3. Cache em memória da coletânea** — `Translate/Collectanea.cs:44-56`.
  Hoje lê + parseia arquivos por texto, todo ciclo. Dicionário em memória
  com invalidação por data/tamanho. **Ganho: alto (pré-rede ~0). Risco: baixo.**
- [x] **4. Hoistar `ResolveFont` + `SKFont`** — `UI/OverlayWindow.cs:502-541`,
  `UI/SkiaText.cs:60-80`. Cada medição resolve a fonte do zero (milhares
  de `FromFamilyName` por quadro). Resolver 1× e reutilizar por tamanho.
  **Ganho: alto. Risco: baixo.**
- [x] **5. Uma análise de cor por área** — `UI/OverlayWindow.cs:233-284`.
  Hoje repete `Analyze` idêntica por bloco da mesma área. Cachear por área
  no quadro. **Ganho: alto (com cor automática). Risco: baixo.**
- [x] **6. Persistir `_measure` entre quadros** — `UI/OverlayWindow.cs:37,155,504`.
  Hoje limpa por desenho + chave string com interpolação. Dicionário com
  teto LRU e chave por tupla. **Ganho: alto em regime. Risco: baixo.**

## Bloco 2 — Ganhos grandes, risco médio (pedem teste A/B)

- [ ] **7. Descartar antes do OCR (hash por área)** — `Loop/TranslationLoop.cs:102-178`,
  `Loop/ChangeTracker.cs:26-32`. Tela parada hoje paga OCR cheio; hash barato
  pula pré-processo + OCR mantendo `ChangeTracker` como decisão final.
  **Ganho: muito alto. Risco: médio (RF-192/193/198).**
- [ ] **8. Capturar só o retângulo (Linux/Mac)** — `Platform/Cli/CliCapture.cs:46-64`,
  `Platform/Linux/LinuxCapture.cs:46-66`, `Platform/Mac/MacCapture.cs:28-41`.
  Hoje: processo + PNG tela cheia + disco + decode + crop. Pedir a região
  (`grim -g`, `screencapture -R`), timeout 1,5–2 s, sem arquivo temp.
  **Ganho: −80–500 ms/área. Risco: médio.**
- [ ] **9. Teto de resolução antes do Det** — `Ocr/Modern/RapidOcrEngine.cs:88`
  (parâmetro NOVO, sem tocar P22 🔒). Reduzir lado maior p/ ~960–1536 px e
  reescalar caixas de volta. **Ganho: alto. Risco: médio.**
- [ ] **10. Caminho colorido para o motor moderno** — `Imaging/Preprocess.cs:60-73`.
  PP-OCR foi treinado em RGB; binarizar destrói textura. Ramificação só
  para `Id == "modern"`. **Ganho: médio-alto (jogos). Risco: médio.**
- [ ] **11. Nearest em binária + HSV inteiro** — `Imaging/Preprocess.cs:225-272`,
  `Imaging/ColorFilter.cs:20-66`. Bilinear cria cinzas em imagem 0/255;
  `double` por pixel no HSV. **Ganho: 2–5× no pré-processo. Risco: baixo/médio.**
- [ ] **12. Erosão depois do zoom (ou condicional)** — `Imaging/Preprocess.cs:75-76`
  (RF-112). Erosão 3×3 antes do zoom apaga traço de 1–2 px. **Ganho: qualidade
  em fonte pequena. Risco: médio.**
- [ ] **13. Tradução: dedup + cache da ponte + token posicional** —
  `Translate/TranslationPipeline.cs:79-112`, `Translate/Tokens.cs:62-70`.
  Enviar só textos únicos; cachear perna `src→ja`; não descartar partes
  vazias (desloca blocos). **Ganho: menos rede + fim das trocas. Risco: baixo/médio.**
- [ ] **14. Memória legível no flush + despejo parcial** —
  `Translate/ResultMemory.cs:76-96` (RF-210/212: muda semântica, não valor).
  Leituras concorrentes + FIFO 10–20% em vez de `Clear()` aos 10 mil.
  **Ganho: sem colapso de cache. Risco: médio.**
- [ ] **15. `Grouping` O(n²) → varredura ordenada** — `Text/Grouping.cs:165-313`
  (P34/P35/P44 🔒 intactos). Particionar por orientação/faixa + mediana
  incremental. **Ganho: alto em telas cheias. Risco: baixo/médio.**
- [ ] **16. Camada: bitmap cacheado + `Wrap` único** — `UI/LayerWindow.cs:171-275`.
  Hoje: bitmap novo + cópia integral + texto medido 2× por quadro.
  **Ganho: alto (modo camada). Risco: baixo/médio.**
- [ ] **17. Windows: recorte da interseção + `PrintWindow` condicional** —
  `Platform/Windows/WindowsCapture.cs:113-142`, `Windows/AttachedCapture.cs:95-165`.
  `BitBlt` só do pedido; `PrintWindow` só se ocluída; `Sleep(1)`.
  **Ganho: −5–200 ms. Risco: baixo/médio.**

## Bloco 3 — Parâmetros novos / opt-in (sem tocar calibrados)

- [ ] **18. Modelo Rec japonês de verdade** — `Ocr/Modern/RapidOcrEngine.cs:70-131,230-245`.
  Hoje japonês roda com Rec latino (`FindJapanesePair` só enfeita lista).
  Sessão por idioma. **Ganho: altíssimo (JP). Risco: alto.**
- [ ] **19. `OcrThreads` configurável** — `Ocr/Modern/RapidOcrEngine.cs:194,204`
  (`numThread: 0`). Default `0` preservado; antes confirmar o que `0`
  significa na 4.1.0. **Ganho: 0–60%. Risco: baixo.**
- [ ] **20. `ReturnWordBox` só no overlay com cor auto** — `Ocr/Modern/RapidOcrEngine.cs:89`.
  **Ganho: alto/médio. Risco: médio.**
- [ ] **21. Limiar Otsu opt-in** — `Imaging/ColorFilter.cs:62` (P21 `127` intacto).
  Checkbox `ThresholdAuto`. **Ganho: médio. Risco: baixo.**
- [ ] **22. Teto de zoom 3–4×** — `Core/Params.cs:49` (P24 `10.0` → faixa UI menor).
  Zoom 10× = 100× pixels. **Ganho: alto. Risco: baixo.**
- [ ] **23. Polls de parada em fatias menores** — `Loop/TranslationLoop.cs:190,363`
  (P126 intacto). `Wait(10)` acumulando 50 ms. **Ganho: −30–80 ms p/ parar. Risco: baixo.**
- [ ] **24. Nuvem/venv: `languageHints`, JPEG~85, cota pós-sucesso, `Reader` por idioma** —
  `Ocr/Cloud/CloudEngine.cs:73-181`, `Ocr/Cloud/CloudQuota.cs:48-58`,
  `Ocr/Venv/VenvEngine.cs:86-95,177-182`. Inclui bug real: troca eng↔jpn
  no venv reaproveita `Reader` errado. **Ganho: alto. Risco: baixo/médio.**
