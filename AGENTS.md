# AGENTS.md — guia obrigatório para IAs trabalhando neste repo

> O programa está CONSTRUÍDO e funcionando. A partir daqui é só
> REFINO (código bonito, textos PT-BR, visual). Nada de reforma,
> nada de arquitetura nova. A spec antiga (`instrucoes.md`) foi
> removida de propósito pelo dono — não recrie, não restaure.
>
> SE VOCÊ ACABOU DE CHEGAR (contexto novo): leia as seções
> "Estado atual", "Comandos" e "Regras de ferro". O resto é referência.

## Estado atual (04/09/2026 — continuar daqui)

- **Build:** 0 erros. **Testes: 204/204 verdes** (198 `Gort.Tests` + 6
  `Gort.VisualTests`). **Warnings:** zero `CS0618`/`xUnit`; restam só
  nullable pré-existentes (`CS8602` etc.) — não caçar sem motivo.
- **Git:** repo `https://github.com/Lamartine-Brasil/GORT`, branch `master`.
  Commits: `1.0` + `Links para o repositorio + contribuidor unico`.
  Dono configurou `user.name/email` local. Sem push/commit sem pedido.
- **Publicado em:** `releases/windows-x64/Gort.exe` (última versão sempre
  aqui, publicada por cima após limpar a pasta).
- **Últimos ciclos feitos:** teto de threads ONNX (4) + descarte de quadros
  parados por hash + `Sleep(1)` no anexo (CPU); `SnapshotArea` limpa no
  `finally` (bug do instantâneo grudado); saída com 3 botões
  (`App.ExitApp()` público); contorno unificado em `Profile.OverlayOutline`
  (default true) com checkbox em Mostrar; `SetLangCombo`; `ApplyHint`
  inline; CPU no rodapé; 403 tratado como 429; banco avisa se vazio;
  planilha com troca de código; `gemini-3.5-flash-lite` na lista;
  varredura de mortos (50 frentes) + testes reescritos p/ estado atual.
- **Último ciclo (visual EXCELENTE):** crash de recursão nos presets
  corrigido com trava; anexador mais largo com quebra; serviço LLM
  renomeado p/ Gemini com placeholder/ajuda/indicador de chave salva;
  `LlmCustom` só com custom; `Testar chave…`; `OnApply` fora da UI;
  `LlmDefaultModel` = 3.5-flash-lite.
- **Último ciclo (comportamento):** saída estreia fora das áreas
  (`PlaceOutside`, tela cheia → inf-direita); camada usa piso de fonte
  configurável (`AutoMinPt`, absoluto 6); parar fecha a saída
  (`HideAll`, pontual preservado); **padrão = camada**; roadmap em
  `docs/ROADMAP.md` (Fase 1: OCR ignora saída; Fase 2: sobreposição
  substitutiva como modo novo).
- **Pausado pelo dono (não mexer sem pedido):** otimizações bloco 2 de
  `docs/sugestoes-ocr.md` (itens 7–24); migração restante é só refino.

## O projeto em 30 segundos

- **GORT - Game Ocr RealTime**: tradutor de tela em tempo real,
  **Avalonia UI + .NET 9**, **multiplataforma** (Windows / Linux / macOS),
  interface em **PT-BR**. Japonês e inglês → PT-BR.
- `src/Gort/` — aplicativo (~25 pastas: `Lifecycle`, `Loop`, `Ocr`,
  `Translate`, `UI`, `Overlay`, `Regions`, `Locale`, ...).
- `src/Gort.Tests/` — 196 testes xUnit. `src/Gort.VisualTests/` — 6 testes
  de render headless (PNGs em `releases/visual-tests/`, nunca no TEMP).
- `src/Directory.Build.props` — centraliza `bin/`+`obj/` em
  `releases/build/` (`ArtifactsPath`). `src/` tem SÓ código-fonte.
- `releases/` — TODA saída gerada: `windows-x64/` (versão única),
  `visual-tests/`, `test-results/`, `build/`. Ignorado no git (só o
  PUBLICAR.md é versionado). Regra do dono: SEMPRE só a última versão —
  limpar a pasta da plataforma antes de republicar, nunca `v2/` ou `-copia/`.
- `docs/` — `imagens/` (10 PNGs curatorados do README, versionados) +
  `sugestoes-ocr.md` (24 ideias numeradas, itens 1–6 feitos).
- `README.md` (raiz) — página do GitHub em PT-BR (autor único, só links
  do dono). Criado via pipeline 3 criadores + 3 revisores + 1 aprovador;
  manter o padrão em refinos futuros.

## Comandos (Windows PowerShell 5.1, `workdir=.../src`)

```powershell
dotnet build Gort.sln -c Release --nologo -v q
dotnet test Gort.sln -c Release --no-build --nologo -v q --results-directory ../releases/test-results
```

- Encadear com `; if ($?) { ... }` (sem `&&`). Não usar `cd`; usar `workdir`.
- Operações de arquivo: ferramentas dedicadas (`read`/`edit`/`write`/
  `glob`/`grep`), nunca `bash` para ler/editar/criar arquivos.
- Publicar: limpar `../releases/windows-x64` antes; `dotnet publish
  Gort/Gort.csproj -c Release -r win-x64 --self-contained true
  -o ../releases/windows-x64`. NÃO publicar se o `Gort.exe` estiver
  rodando (trava o `.dll` — pedir ao dono fechar com Sair de verdade).

## Regras de ferro (todas aprendidas na marra)

1. **IMAGENS: no máximo 5–10 por lote.** Ler 50+ imagens travou o VC
   (sessão perdida). Renders de teste: ler o CÓDIGO primeiro, abrir só as
   PNGs necessárias, em lotes de ≤5.
2. **Multiplataforma sempre.** Nenhuma dependência de Windows fora de
   `Platform/` (que isola por SO com guardas `IsWindows()` etc.).
   Ícones: vetores do Avalonia (`Shapes`/`Path`/`Geometry`), nunca fonte
   de ícones externa. `RemoteWindow` usa `IRemoteHost` (stub no teste
   headless) — nunca volte a exigir `App` real no construtor.
   Janelas de tradução são independentes da principal (sem dono): nunca
   usar `Show(owner)` — minimizar uma não pode minimizar a outra.
3. **Números com 🔒 e `RF-xxx` vêm da spec original** (hoje vivem em
   `Core/Params.cs` e nos comentários do código). Não "melhorar" valor
   calibrado. Conjuntos (idiomas, motores, serviços) são dados
   (`Config/Catalogs.cs`), nunca código no núcleo.
4. **Laço de tradução é síncrono numa thread dedicada** (RF-009). Mudança
   de config com laço vivo: `Controller.ApplyChange(..., P03)` (RF-012).
   Nunca modal/foco roubado a partir do laço (P2).
5. **Textos da UI em PT-BR**, seguindo o glossário do produto
   (Área de OCR, Laço, Sobreposição, Camada, Escuro, Modo pontual...).
   Sem abreviações (`Cfg`, `Grp`, `Snap`), sem inglês cru, sem `ê`
   corrompido (era `—` mal decodificado; grep por ` ê ` após mexer em texto).
6. **Implementou → compilou → testou.** `dotnet test` após cada mudança.
   Para revisão: criar agentes (Task) sendo **≥2 só-validadores**
   (só leitura, sem editar), com limite de imagens explícito no prompt.
7. **Git:** sem commits, amend, push ou PR sem pedido explícito.
   `.gitignore` protege `src/Gort/Debug/` (fonte!) e `Assets/` — não
   reintroduzir `debug/` genérico nem `*.png` sem exceção.
8. **Ao final de cada ciclo, grave o aprendizado aqui.** Se descobrir
   algo que a próxima IA precisaria saber (pegadinha, caminho, comando,
   decisão do dono), edite este AGENTS.md antes de encerrar.

## Pegadinhas conhecidas

- `Bitmap.Save` exige `PngBitmapEncoderOptions.Default`; teste headless usa
  `.GetAwaiter().GetResult()` com pragma `xUnit1031` justificado (RF-009).
- Testes nunca assumem `bin/` local: localizar a raiz subindo até achar
  `src/Gort.sln` (com fallback), pois o build é centralizado em
  `releases/build/` (`HardeningTests.SrcDir`, `VisualRenderTests.OutDir`).
- `SkiaText` usa `SKFont` (não `SKPaint.TextSize`); `Wrap` recebe `SKFont`.
- `NativeMenuItem` usa `MenuItemToggleType.CheckBox`; `IsChecked` é
  definido por código (menu nativo varia por SO).
- `DarkWindow` evita eco duplo `OCR: OCR:`; `_status` tem `MinHeight`.
- Rodapé mostra `Memória: X MB · CPU: Y%` (CPU por delta de
  `TotalProcessorTime`/núcleos no `_memTimer` de 2 s — sem API de SO).
- `tmp-remote.png` era artefato manual obsoleto (apagado); `remote.png`
  agora é gerado pelo teste `Render_AuxWindows`.
- Diálogo de saída tem 3 botões (sair de verdade = `App.ExitApp()` público,
  que encerra tudo; fechar a principal com outra janela aberta não sai).
- Contorno de texto é `Profile.OverlayOutline` (default true); o antigo
  `AdvancedOptions.OverlayOutline` era morto (escrevia sem leitura).
- `SnapshotArea` limpa no `finally` do `SnapshotAsync` (sem isso gruda).
- Combos de idioma com 1 opção ficam desabilitados com tooltip
  (`SetLangCombo`); fonte da verdade continua nos rádios en/ja.
- Aplicar confirma inline no rodapé (`ApplyHint`, some em 2,5 s) —
  nunca modal a partir do Aplicar.
- Listas com `SelectionChanged` que recarregam `ItemsSource` precisam de
  trava de reentrância (senão: recursão → estouro de pilha → fecha o app).
- Serviço LLM se chama `Gemini (modelo de linguagem)` (padrão 3.5-flash-lite).
- `selection.png` em branco é esperado (overlay transparente sem arrasto).
- Modelos OCR vêm do NuGet (`models/` copiado no publish) — não versionar.
- Varredura de código morto concluída (50 frentes): suite de testes reflete
  o estado atual; não ressuscitar membros removidos sem checar uso real.
- CPU: ONNX limitado a 4 threads (`OcrThreadCount`); quadros parados pulam
  OCR via hash FNV + fingerprint (qualquer mudança de config invalida);
  `ChangeTracker` textual continua decidindo o redesenho.
- Web gratuito: 403 tratado como 429 (cai p/ baixa); baixa bloqueada =
  mensagem p/ aguardar ou trocar de serviço.
- Chaves (GEMINI etc.) vão em `creds-<serviço>.toml` via Aplicar/KeyManager,
  na pasta de dados (fora do repo); `.gitignore` barra `creds-*.toml`;
  nunca hardcoded, nunca `.env`. Instrução do LLM é calibrada (não mexer).
- Dono/autor único: **Lamartine Barbosa** (`csproj Authors` + Sobre +
  `README.md#Contribuidores`).
  TODOS os links externos apontam para `https://github.com/Lamartine-Brasil/GORT`
  (`Catalogs.Links`, `Update.Dist`) — inclusive doações e atualização;
  ele ajusta os destinos depois. Não reintroduzir `gort.app`.
- Fonte padrão 14 pt (decisão do dono; era 15).
