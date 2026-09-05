# AGENTS.md — guia da IA (leia primeiro; contexto novo começa aqui)

## 1. O projeto

- **GORT - Game Ocr RealTime**, de Lamartine Barbosa: tradutor de tela em
  tempo real (inglês e japonês → português do Brasil). O `README.md` da raiz
  é a página do GitHub, em português; o `MANUAL.md` da raiz é o manual
  de funções em texto (cada tela, botão e opção).
- **Linguagem e bibliotecas:** C# no .NET 9 (`net9.0`, `Nullable enable`),
  interface Avalonia 12 (Desktop, tema Fluent, fonte Inter, conta-gotas),
  desenho SkiaSharp. Reconhecimento: RapidOcrNet (moderno embarcado),
  Tesseract (clássico local), motor do sistema, ambiente interpretado e
  nuvem (somente modo pontual). Atalhos globais via SharpHook, voz via
  System.Speech, área de transferência via TextCopy, configuração em TOML
  (Tomlyn) com chaves estáveis.
- **Pastas:** `src/Gort/` é o programa, organizado por domínio (`Ocr`,
  `Translate`, `Loop`, `Regions`, `Imaging`, `Text`, `UI`, `Platform`,
  `Config`, `Store` e outros); `src/Gort.Tests/` são os testes de unidade;
  `src/Gort.VisualTests/` gera renders sem janela para PNG. `src/` contém
  somente fonte — tudo que é gerado mora em `releases/` (compilação,
  resultados de teste, PNGs visuais, `windows-x64`). `docs/imagens/` guarda
  as 11 imagens do README (`00-capa` mais `01-10`, versionadas).
- **Janela principal em 5 abas:** Início, Captura e Leitura, Tradução e
  Idiomas, Exibição, Sistema (mais Depuração oculta). O laço de tradução
  roda em contínuo ou pontual, e a tradução aparece em 4 modos: Escuro,
  Camada (padrão), Sobreposição e Substituição.
- **Sobreposição** (`overlay`): tradução desenhada em cima do original, no
  mesmo lugar e tamanho parecido (posição das palavras do OCR); sem fundo,
  o original segue visível por baixo; exige OCR com posição. **Substituição**
  (`replace`): igual, mas tampa o original com fundo opaco — só o português
  fica visível. Ambos: janela transparente a cliques e fora da captura (o
  OCR nunca lê a própria tradução).
- **Comandos** (`workdir=.../src`, PowerShell 5.1, encadear com `; if ($?)`,
  sem trocar de pasta no comando):

```powershell
dotnet build Gort.sln -c Release --nologo -v q
dotnet test Gort.sln -c Release --no-build --nologo -v q --results-directory ../releases/test-results
```

## 2. Decisões do dono (valem sempre)

- Dono e autor único: **Lamartine Barbosa** (`Lamartine-Brasil/GORT`,
  ramo `master`). Sem commit, push ou PR sem pedido.
- Reforma autorizada em 05/09/2026: a janela foi de 6 abas para 5 por
  jornada; o resto das regras segue valendo.
- Padrões que não se trocam sem pedido: modo **Camada**; fonte 14 pt;
  tradução inglês → português do Brasil (japonês → inglês por serviço);
  Google Tradutor web gratuito como serviço padrão; modelo padrão do
  Gemini `gemini-3.5-flash-lite`; o serviço de linguagem chama-se
  `Gemini (modelo de linguagem)`; versão 1.5 no `csproj`.
- Publicação: somente `releases/windows-x64/Gort.exe`, versão completa
  autocontida; `releases/` guarda sempre só a última versão (limpar a
  pasta antes); nunca publicar com o programa rodando (trava o dll).
- Tudo em português do Brasil, sem abreviação nem inglês na interface.
  Glossário: Área de OCR, Laço, Sobreposição, Substituição, Camada,
  Escuro, Modo pontual. Todos os links apontam para
  `github.com/Lamartine-Brasil/GORT`. Chaves ficam em `creds-*.toml` na
  pasta de dados (fora do repositório); nunca fixas no código nem em `.env`.
- Comportamentos pedidos que valem: trocar de modo e aplicar ajusta
  sozinho o pré-requisito (só o motor, só se incompatível — o resto
  pessoal permanece); quadros pretos avisam em torrada, nunca em modal;
  a saída nunca é relida pelo OCR. Pausado (não mexer): bloco 2 do OCR;
  o resto é refino.

## 3. Regras da IA (importantes)

1. **Imagens:** no máximo 5 por lote. Ler o código antes; só as PNGs
   necessárias.
2. **Multiplataforma:** nada específico de Windows fora de `Platform/`.
   Ícones são vetores Avalonia, nunca fonte externa.
3. **Travas:** o que está marcado com cadeado, `RF-xxx` ou `P-xx` não se
   recalibra. Conjuntos fechados são dados (`Catalogs.cs`), não literais.
4. **Laço:** síncrono em 1 thread (RF-009); mudança com laço vivo passa
   por `Controller.ApplyChange` (RF-012); nunca modal a partir do laço
   (P2) — avisar em torrada.
5. **Compilação com zero avisos:** aviso novo se corrige na hora. Conferir
   o resultado pelo código de saída, não por `grep` no texto.
6. Mudou → compilou → testou (0 erros, tudo verde). Revisão via `Task` com
   **2 validadores** só-leitura e limite de imagens no pedido.
7. Sem git sem pedido. Nunca editar nem criar arquivos pelo `bash` (o
   PowerShell 5.1 corrompe UTF-8 sem BOM) — usar as ferramentas próprias.
   Sem edição "sem efeito".
8. **Fim de tarefa: atualizar este arquivo se necessário** — registrar
   o aprendizado na seção 5; se a tarefa mudou algum fato das seções
   1 a 4 (padrão, regra, comportamento, lembrete), ajustar lá também.

## 4. Lembretes (pegadinhas vivas)

- **Testes:** a raiz é quem contém `src/Gort.sln` (compilação
  centralizada); `xUnit1031` vai com pragma onde o RF-009 exige. Teste
  sem janela precisa de `EnsureEngines()` (inicializar é leve, o modelo
  só carrega no reconhecer) e de `Show()` antes de afirmar a árvore
  visual; o conteúdo da aba mora no apresentador do `TabControl`, não sob
  o `TabItem`. Fechar janela só se visível (senão o diálogo de saída
  mascara o erro real). `AssertPainted` contra render vazio.
  `selection.png` em branco é esperado. Modelos de OCR vêm do NuGet (não
  versionar). PNGs somente em `releases/visual-tests/`.
- **Desenho:** `Bitmap.Save` com `PngBitmapEncoderOptions.Default`;
  `SKFont` com `Wrap(SKFont)`; contorno duplo configurável no perfil;
  fonte resolvida por família com cache (invalida ao trocar).
- **Interface:** `Req<T>` `fail-fast` nos `FindControl`; combo de 1 opção
  desabilitado (`SetLangCombo`); `SelectionChanged` com `ItemsSource`
  exige trava de reentrância; `ApplyHint` `inline` (nunca modal); falha no
  Aplicar recarrega a interface (o combo nunca mente); `Detach` no `Rebuild`
  (senão "already has a parent"); `MenuItemToggleType.CheckBox` com
  `IsChecked` em código; `_status` com altura mínima.
- **Laço e saída:** escuro e camada são apagados da captura
  (`BlackoutOutput` via `OutputOccluders`; sobreposição usa a exclusão do
  sistema); área de captura pontual limpa no `finally`; tudo escondido ao
  parar (`HideAll`); saída com 3 botões e `ExitApp()` público; rodapé
  `Memória · CPU` (delta de `TotalProcessorTime`, 2 s); sem eco `OCR: OCR:`.
- **Persistência:** cerca de 50 chaves TOML com `.bak` para restaurar; par
  de tradução por serviço vale sobre o global; flags de sobreposição
  moram no `Profile` (o trio antigo no `AdvancedOptions` era morto);
  normalizar antes de salvar.
- **Tradução:** a impressão digital (`TranslationFingerprint`: serviço,
  par, qualidade e parâmetros) zera o rastreador na troca — senão a troca
  "não pega" com a tela parada; erro 403 cai para qualidade baixa;
  dicionários separados por idioma.

## 5. Concluído (só o relevante)

- **Último estado verificado (05/09/2026):** build 0 erros, 305 testes
  verdes (290 de unidade + 15 visuais, com a cadeia do Aplicar coberta).
  Publicado `releases/windows-x64/Gort.exe` com autorização do dono.
- **Sobreposição/Substituição por área (pedido do dono):** em vez de
  uma janela na união das áreas (quebrava com monitores e escalas
  mistos), agora é uma janela por área, ancorada no retângulo com a
  escala do próprio monitor — monitores e escala se resolvem na hora,
  sem saber antes. Criação preguiçosa com reuso, sem roubar foco,
  Substitute e estado propagados; `OverlaySink` virou repasse ao
  gerente. Prova: `Overlay_PerRegion_Anchored` (tamanhos, tinta,
  poda). MANUAL ajustado.
- **Sobreposição em lugar errado (diagnóstico + correção):** a conta
  de origem fecha no monitor único (origem, zoom único, recorte).
  Defeitos reais achados: o limite pela posição do cliente usava o
  (0,0) de ausência como piso (deslocava/zerava com origem negativa
  ou janela ativa) — agora só com cliente (`HasClient`, só anexada)
  via `ClampToClient` puro; e a janela era posicionada por
  `InvokeAsync` (origem defasada um quadro) — agora direto na
  thread da UI. Provas: 2 testes do limite + conta auditada. Se
  persistir, pedir monitores/escala do usuário.
- **Troca para LLM mostrando erro do Google (diagnóstico):** a fiação
  da troca confere (perfil fresco por ciclo, impressão digital com
  serviço, pipeline resolve por chamada, Aplicar salva). O vetor
  certo: bloqueio do modelo cai na reserva Google (RF-277) e o erro
  dela vinha sem assinatura — agora diz "bloqueado pelo modelo;
  reserva … também falhou". Provas: 2 testes do reserva + 1 da
  impressão digital. Sem chave, o erro é "Informe a chave", nunca
  Google — Google na tela com LLM escolhido é reserva ou troca não
  aplicada.
- **Camada sempre na frente + posição inicial (pedido do dono):**
  confirmado que a estreia era fora das áreas (canto livre), não
  dentro em cima. `Topmost` forçado na camada (construtor, `Apply`,
  `ShowForMode`) — clicar no jogo não cobre mais; exceção autorizada
  ao RF-319/320 só para a camada (o escuro segue "Sempre no topo").
  OCR segue ignorando (exclusão + oclusores vivos, intocados). Nova
  opção `layer_place` (`outside` padrão, `top`, `bottom`) com 3 rádios
  na Exibição; trocar limpa a geometria salva e reposiciona (aberta
  na hora, fechada na estreia). Provas: `LayerPlacementTests` (+8) e
  `Layer_StaysOnTop`. README (imagem e linha da Camada) refeito.
- **Reforma 5 abas (05/09/2026, autorizada):** `AdvancedPanel` em modo
  distribuído servindo as abas; seções em `Expander` nativo; remoto
  unificado; `docs/imagens/01-09` e README nos nomes novos; lógica
  (laço, OCR, tradução, travas) e chaves TOML intactas. Pós-reforma:
  abridor da janela avançada no Sistema, ressincronia do painel após o
  perfil e `Detach` no `Rebuild`.
- **Auditoria completa em 6 etapas:** corrigiu persistência total, travas
  do dicionário, oclusores, timeouts, memória e resto da lista longa —
  o código atual já incorpora tudo; os detalhes por item foram podados.
- **Troca de serviço que "não pegava":** a impressão digital de tradução
  força a retradução com a tela parada.
- **Sobreposição e Substituição (diagnóstico):** fiação confere
  (reuso da janela com `Substitute`, mesmo sink, laço tratando os dois
  juntos); mensagem de pré-requisito citava só "Sobreposição" e agora
  cita os dois modos. Prova: `Render_Overlay_Texts` (5 textos mais 1
  Substituição) com PNGs inspecionados.
- **Modo auto-ajusta pré-requisito (pedido do dono):** novo
  `Config/ModeRequirements.cs` puro mais gancho no Aplicar; prova em
  `ModeRequirementsTests` (+8).
- **Cobertura pura:** `DeepReviewTests.cs` (+42 casos sem interface:
  TOML, versão, molduras, memória, rastreador, cache, tradutor web).
- **MANUAL.md (pedido do dono):** manual de funções em texto na raiz
  (16 seções: telas, serviços, avançada, remoto, bandeja, áreas,
  dicionário, atalhos, arquivos). Prova: 2 revisores (acharão 8
  divergências de rótulo, todas corrigidas).
- **README 1.5 (pedido do dono):** página nova padrão capa com selos,
  capa `00-capa.png` composta na identidade (índigo/violeta) e 10
  capturas frescas dos renders com cantos arredondados e moldura;
  textos conferidos (277+12, 7 atalhos, 5 motores, 10 serviços).
  Prova: 2 revisores (acharão `renders` × testes e inglês na página).
