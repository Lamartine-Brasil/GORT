# AGENTS.md — guia da IA (leia 1º; contexto novo começa aqui)

> Programa PRONTO e funcionando: só REFINO (visual, PT-BR, textos).
> Sem reforma/arquitetura nova. `instrucoes.md` removida pelo dono:
> não recrie. Dono/autor único: **Lamartine Barbosa**.

## Estado (05/09/2026 — revisão profunda)

- **Build 0 erros · 251/251 testes verdes** (245+6; antes 203+6).
  Novo `src/Gort.Tests/DeepReviewTests.cs` (+42 casos, puros: TomlFile
  GetDouble/GetSchema/GetInt, VersionFile Parse/IsMinor/ForceHttps,
  PipeFraming Encode/trunca/roundtrip, DisplayMemory pilha/expira,
  ChangeTracker P-47 exato, ReuseCache, TokenHelper, WebFree
  Parse/BuildUrl, BuildExportText cabeçalho). Validado por 2 revisores
  só-leitura. Nenhum código do app alterado.
- **Achados visuais (15 PNGs, corrigidos):** overlay.png vazio →
  teste agora desenha quadro demo + `AssertPainted` (era `ApplyRunning`
  sem `DrawOverlay`); overlay-source/content minúsculo → `AutoFontSize`
  no perfil do teste (padrão 14 pt intacto); Motor OCR c/ placeholder
  no headless → `EnsureEngines()` (`Initialize` é leve, modelo só no
  `Recognize`); rótulo planilha truncado → grade `Auto/Auto/Auto` c/
  `RowDefinitions` em `BuildTranslation` (SEM `RowDefinitions` tudo cai
  na linha 0!); rodapé × links = borda do `ScrollViewer` (rolagem
  prevista, sem correção); README + linha `Ctrl+Shift+C`.
  Prova real: `docs/imagens/03` (v1.0.0) já cortava "nuvem".
- **2 erros do dono (corrigidos):** quadros pretos abriam modal
  (`Notify`+`ShowDialog` do laço, contra P2) → `NotifyToast` (fundo
  preto, some em 6 s, sem roubar foco); `loop.Notice` roteado ao
  toast (App + `HotkeyActions`; duplicata removida); saída relida
  pelo OCR (afinidade Windows falha em silêncio) →   `BlackoutOutput`
  no laço apaga da captura os retângulos de escuro/camada via
  `IDisplaySink.OutputOccluders` (padrão vazio; sobreposição fora
  por desenho). Prova: `SelfCaptureTests` (+6) + fiação
  visível/escondida no `VisualRenderTests`. 257/257 verdes.
- **Visuais fortalecidos (17/17 janelas):** `Shot` ganhou `shown`
  (pós-`Show`; árvore visual só existe depois) + guarda `IsVisible`
  no `Close` (sem ele, falha no `setup` era mascarada pelo diálogo
  de saída no `OnClosing`); asserts: aba selecionada, combo modern,
  grade 3×10 (trava `RowDefinitions`), links do Sistema, rótulos do
  remoto (+ variante traduzindo "Parar" verde), áreas populadas,
  conta-gotas e moldura (`AreaFrameWindow` era a única sem render).
  Nota: conteúdo da aba mora no apresentador do `TabControl`, não
  sob o `TabItem`.
- **`docs/imagens/01-10` regenerados** dos renders atuais (v1.4.0;
  07/08 idênticos, sem mudança).
- **Anteriores (04/09/2026):** 209/209 verdes; resto abaixo vale.

- **Build 0 erros · 209/209 testes verdes** (203+6) · zero warnings
  `CS0618`/`xUnit` (só nullable antigos — não caçar).
- **Git:** `Lamartine-Brasil/GORT`, `master`, commits `1.0` + links.
  Sem commit/push/PR sem pedido. `user.name/email` local ok.
- **Versão:** 1.3 (`csproj`; Sobre/splash/rodapé automáticos).
- **Publicado:** `releases/windows-x64/Gort.exe` (sempre última versão,
  limpar pasta antes; NUNCA publicar com exe rodando — trava o dll).
- **Feito recente:** CPU (ONNX≤4 threads, hash-skip, `Sleep(1)`); snapshot
  limpa no `finally`; saída 3 botões (`ExitApp` público); contorno em
  `Profile.OverlayOutline=true`; `SetLangCombo`; `ApplyHint` 2,5s; CPU no
  rodapé; 403→baixa; banco/planilha orientam; Gemini padrão 3.5-flash-lite
  (`LlmDefaultModel`); `Testar chave…`; `OnApply` fora da UI; remoto com
  ícones (`IRemoteHost`); janelas independentes (sem `Show(owner)`);
  `PlaceOutside`; piso `AutoMinPt` (abs 6); `HideAll` ao parar; padrão
  **camada**; modo novo **Substituição** (`replace`); 403→baixa; saída
  fora da captura (exclusão); mortos removidos (50 frentes);
  caça-bugs corrigido; `RemoteWindow` sem botão duplicado.
- **Pausado (não mexer):** bloco 2 OCR; resto é refino.

## Projeto

- **GORT - Game Ocr RealTime**, Avalonia + .NET 9, Win/Linux/macOS, PT-BR.
- `src/Gort/` app · `src/Gort.Tests/` · `src/Gort.VisualTests/` (PNGs em
  `releases/visual-tests/`).
- `src/Directory.Build.props` → tudo gerado em `releases/build/`.
  `src/` = SÓ fonte. `releases/` = tudo gerado (só `PUBLICAR.md` no git).
- `docs/imagens/` = 10 PNGs do README (versionados).
- `README.md` raiz = página GitHub PT-BR (pipeline 3+3+1).

## Comandos (`workdir=.../src`, PowerShell 5.1, `; if ($?)`, sem `cd`)

```powershell
dotnet build Gort.sln -c Release --nologo -v q
dotnet test Gort.sln -c Release --no-build --nologo -v q --results-directory ../releases/test-results
```

## Regras de ferro

1. **Imagens ≤5 por lote** (50+ travou o VC). Ler código antes; só PNGs necessárias.
2. **Multiplataforma:** nada de Windows fora de `Platform/`. Ícones =
   vetores Avalonia, nunca fonte externa.
3. **🔒/RF-xxx**: não recalibrar. Conjuntos = dados (`Catalogs.cs`).
4. **Laço síncrono 1 thread** (RF-009); mudança com laço vivo via
   `Controller.ApplyChange` (RF-012); nunca modal do laço (P2).
5. **PT-BR** sem abreviação/inglês/`ê` corrompido (grep ` ê `). Glossário:
   Área de OCR, Laço, Sobreposição, Camada, Escuro, Modo pontual.
6. Mudou → compilou → testou. Revisão: Task com **≥2 validadores**
   só-leitura + limite de imagens no prompt.
7. Sem git sem pedido. `.gitignore` protege `Debug/`-fonte e `Assets/`.
8. **Fim do ciclo: grave o aprendizado aqui.**

## Pegadinhas

- `Bitmap.Save` c/ `PngBitmapEncoderOptions.Default`; `xUnit1031` c/ pragma (RF-009).
- Raiz dos testes: subir até `src/Gort.sln` (build centralizado).
- `SKFont`, `Wrap(SKFont)`; `MenuItemToggleType.CheckBox` + `IsChecked` em código.
- `DarkWindow` sem eco `OCR: OCR:`; `_status` c/ `MinHeight`.
- Rodapé `Memória: X MB · CPU: Y%` (delta `TotalProcessorTime`, 2 s).
- Saída 3 botões; `ExitApp()` encerra tudo.
- `SnapshotArea` limpa no `finally`; combos 1 opção desabilitados (`SetLangCombo`).
- `ApplyHint` inline 2,5 s, nunca modal.
- `selection.png` em branco = esperado. Modelos OCR do NuGet (não versionar).
- Chaves em `creds-*.toml` (pasta de dados, fora do repo, gitignore); nunca hardcode/`.env`. Instrução LLM calibrada.
- Links todos → `github.com/Lamartine-Brasil/GORT` (menos download .NET no README). Sem `gort.app`.
- Fonte padrão 14 pt (dono; era 15). Lista `SelectionChanged`+`ItemsSource` = trava reentrância.
- Serviço LLM = `Gemini (modelo de linguagem)`.
- Par de tradução por serviço (`ServiceSource/Target`) vale sobre o
  global; padrão continua en→pt-BR (ja→en por serviço).
- Flags de sobreposição moram no `Profile` (UI em Mostrar); o antigo
  trio no `AdvancedOptions` era morto. Min/max também no perfil.
