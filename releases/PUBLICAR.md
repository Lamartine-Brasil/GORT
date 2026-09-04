# releases/ — saídas publicadas do GORT (fora do código-fonte)

Esta pasta recebe os **binários publicados** (prontos para distribuir).
Ela NÃO é versionada (ver `.gitignore`): só este PUBLICAR.md fica no git.

## Como gerar (a partir de `src/`)

> Regra do dono: `releases/` guarda SEMPRE só a última versão — sem
> `v1/`, `v2/`, `-novo/`, `-final/` nem cópias. Publicar por cima da
> pasta fixa da plataforma (o publish sobrescreve; a limpeza abaixo
> remove resto de versão anterior).

```powershell
# Limpa a plataforma antes (evita resto de versão antiga)
Remove-Item ../releases/windows-x64 -Recurse -Force -ErrorAction SilentlyContinue

# Windows x64
dotnet publish Gort/Gort.csproj -c Release -r win-x64 --self-contained true -o ../releases/windows-x64

# Linux x64
dotnet publish Gort/Gort.csproj -c Release -r linux-x64 --self-contained true -o ../releases/linux-x64

# macOS x64
dotnet publish Gort/Gort.csproj -c Release -r osx-x64 --self-contained true -o ../releases/osx-x64

# macOS ARM64 (Apple Silicon)
dotnet publish Gort/Gort.csproj -c Release -r osx-arm64 --self-contained true -o ../releases/osx-arm64
```

## Saídas de teste (também aqui, nunca em `src/`)

- `visual-tests/` — PNGs dos 6 testes de render headless
  (`Gort.VisualTests`), regenerados a cada `dotnet test`.
- `test-results/` — resultados de teste (`.trx`, cobertura), via:
  `dotnet test ... --results-directory ../releases/test-results`.
- `build/` — `bin/` + `obj/` de TODOS os projetos, centralizados via
  `src/Directory.Build.props` (`ArtifactsPath`). O compilador exige
  essas pastas, mas elas moram aqui — `src/` contém só código-fonte.

## Regras

- NUNCA publicar para dentro de `src/` (`bin/`, `obj/`, `publish/`, `out/`):
  era essa a bagunça anterior (~5 GB de `linux-x64/publish` e
  `osx-x64/publish` dentro de `src/Gort/bin/`).
- `bin/`, `obj/` e `TestResults/` são lixo de build/teste: regeneráveis com
  `dotnet build` / `dotnet test`. Não copiar nada de lá para cá à mão.
- Os modelos de OCR (`models/`) vêm do pacote NuGet e são copiados
  automaticamente para a pasta de saída no publish.
- Para versionar uma entrega, compacte a subpasta da plataforma
  (ex.: `windows-x64.zip`) e anexe à release do git — o conteúdo solto
  desta pasta continua ignorado.
