# AGENTS.md — guia obrigatório para IAs trabalhando neste repo

> O programa está CONSTRUÍDO e funcionando. A partir daqui é só
> REFINO (código bonito, textos PT-BR, visual). Nada de reforma,
> nada de arquitetura nova. A spec antiga (`instrucoes.md`) foi
> removida de propósito pelo dono — não recrie, não restaure.

## O projeto em 30 segundos

- **GORT**: tradutor de tela em tempo real, **Avalonia UI + .NET 9**,
  **multiplataforma** (Windows / Linux / macOS), interface em **PT-BR**.
- `src/Gort/` — aplicativo (~25 pastas: `Lifecycle`, `Loop`, `Ocr`,
  `Translate`, `UI`, `Overlay`, `Regions`, `Locale`, ...).
- `src/Gort.Tests/` — 190 testes xUnit. `src/Gort.VisualTests/` — 6 testes
  de render headless (PNGs em `releases/visual-tests/`, nunca no TEMP).
- `releases/` — TODA saída gerada: binários publicados, PNGs visuais,
  resultados de teste e o `build/` centralizado (`bin/`+`obj/` de todos
  os projetos via `src/Directory.Build.props`). Ignorado no git (só o
  PUBLICAR.md é versionado). Nada gerado fica dentro de `src/`.
  Regra do dono: SEMPRE só a última versão aqui — publicar por cima da
  pasta fixa da plataforma (limpar antes), nunca criar `v2/` ou `-copia/`.
- `README.md` (raiz) — página do GitHub em PT-BR (autor único, só links
  do dono). Criado via pipeline 3 criadores + 3 revisores + 1 aprovador;
  manter o padrão em refinos futuros.
- `bin/`, `obj/`, `TestResults/` são lixo regenerável. Não versionar,
  não copiar nada de lá.

## Comandos (Windows PowerShell 5.1, `workdir=.../src`)

```powershell
dotnet build Gort.sln -c Release --nologo -v q
dotnet test Gort.sln -c Release --no-build --nologo -v q --results-directory ../releases/test-results
```

- Padrão atual: **0 erros, 201/201 testes verdes, 0 warnings CS0618/xUnit**.
  Restam só warnings pré-existentes de nullable (`CS8602` etc.) — não sair
  caçando sem motivo (código funcionando = ajuste fino, não reforma).
- Encadear com `; if ($?) { ... }` (sem `&&`). Não usar `cd`; usar `workdir`.
- Operações de arquivo: ferramentas dedicadas (`read`/`edit`/`write`/
  `glob`/`grep`), nunca `bash` para ler/editar/criar arquivos.

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
- `selection.png` em branco é esperado (overlay transparente sem arrasto).
- Modelos OCR vêm do NuGet (`models/` copiado no publish) — não versionar.
- Varredura de código morto concluída (50 frentes): suite de testes reflete
  o estado atual; não ressuscitar membros removidos sem checar uso real.
- Dono/autor único: **Lamartine Barbosa** (`csproj Authors` + Sobre +
  `README.md#Contribuidores`).
  TODOS os links externos apontam para `https://github.com/Lamartine-Brasil/GORT`
  (`Catalogs.Links`, `Update.Dist`) — inclusive doações e atualização;
  ele ajusta os destinos depois. Não reintroduzir `gort.app`.
