# GORT — Manual de funções (versão 1.6)

Manual completo do programa: cada tela, cada botão, cada opção e o que
cada escolha representa. Serve para gente e para inteligência artificial.

## 1. O que o programa faz

O GORT traduz texto que aparece na tela — diálogos de jogos, programas
antigos, qualquer janela que mostre texto como imagem. Funciona assim:

1. Você marca uma ou mais **áreas de OCR** (retângulos sobre o texto).
2. O programa fotografa essas áreas várias vezes por segundo.
3. Um **motor de leitura** reconhece as letras (inglês ou japonês).
4. Um **serviço de tradução** traduz para o português do Brasil.
5. A tradução aparece numa **janela de tradução**, no modo que você
   escolheu (Escuro, Camada, Sobreposição ou Substituição).

Se a tela não mudou, o programa não processa de novo (economiza
processador). O rodapé mostra memória e processador ao vivo.

## 2. Primeiros passos (assistente da aba Início)

Na primeira vez, a aba **Início** mostra um assistente em 4 passos:

- **Passo 0 — "Qual é a cor do texto?"**: marque **Texto escuro**,
  **Texto claro** ou **Não sei**, e clique em **Avançar**. Serve para
  preparar o filtro de leitura.
- **Passo 1 — "Selecione na tela a área onde o texto aparece"**: clique
  em **Selecionar área…**, arraste um retângulo sobre o diálogo do jogo.
  O botão **Pular** pula sem criar área.
- **Passo 2**: mostra quantas áreas existem. **Gerenciar áreas de OCR…**
  abre o gerenciador; **Avançar** aplica a configuração inicial
  (escolhe motor e serviço, modo Camada, frequência 1 e filtro de cor,
  e para a tradução para recomeçar do limpo).
- **Passo 3**: texto de ajuda com botão **Manual** (abre a documentação)
  e **Concluir** (termina e volta ao começo).

O botão **Configuração rápida** reinicia esse assistente quando quiser.

## 3. Aba Início

- **Gerenciar áreas de OCR…**: abre a janela de áreas (ver seção 11).
- **Traduzir uma vez**: faz um único ciclo de tradução e mostra o
  resultado, sem ligar o modo contínuo.
- **Configuração rápida**: volta ao assistente da seção 2.
- **Mostrar controle remoto**: reabre a barrinha flutuante (seção 9).
- **Linha de estado**: avisa se não há área ("use o assistente ou
  Gerenciar áreas") ou confirma quantas áreas estão prontas.
- **Iniciar tradução** (botão grande): liga e desliga a tradução
  contínua. Parado, mostra **Iniciar tradução**; traduzindo, mostra
  **Parar tradução**. Também atende por `Ctrl+Shift+Z`.
- **Usando atual**: mostra o serviço de tradução em uso (só leitura).
  O botão **Trocar** leva para a aba Tradução e Idiomas.

## 4. Aba Captura e Leitura

### Fonte de captura

- **Capturar da janela ativa**: em vez de áreas fixas, fotografa a
  janela que está na frente. Desligado em sistemas sem suporte.
- **Ampliação**: aumenta a imagem antes de ler (padrão 2). Números
  maiores leem letra pequena, mas pesam mais.
- **Restaurar padrão**: volta a ampliação para 2.
- **Capturar de janela anexada…**: escolhe uma janela para ler mesmo
  coberta por outras (só no Windows; nos outros sistemas, use áreas).

### Idioma do texto e motor de leitura

- **Inglês / Japonês**: idioma do texto original. Sincroniza todos os
  ajustes de idioma por motor.
- **Motor de OCR** (lista): quem reconhece as letras.
  - **Motor de reconhecimento moderno embarcado** (padrão): já vem
    junto, lê inglês e japonês, informa a posição das palavras
    (necessário para Sobreposição e Substituição).
  - **Motor do sistema operacional**: usa o leitor do sistema (se
    indisponível na máquina, aparece marcado como indisponível). O
    botão **Adicionar idioma** abre as configurações de idioma do
    Windows.
  - **Motor local clássico**: leitor tradicional local, sem rede.
    Pede **Conjunto de dados de idioma**, **Idioma** e tem **Modo
    rápido** (lê mais rápido com menos precisão).
  - **Motor baseado em ambiente interpretado**: lê por um ambiente
    externo. Pede **Idioma**; o botão **Instalar…** prepara o ambiente.
    Não informa posição de palavra (não serve para Sobreposição).
  - **Motor de nuvem (somente modo pontual)**: lê pela nuvem com a
    melhor qualidade. Pede **Idioma**, **Credencial da nuvem**
    (arquivo da conta) e mostra o **Uso mensal**. Só funciona no modo
    pontual (traduzir uma vez), nunca no contínuo.
- **Exibir resultado do OCR**: mostra o texto original reconhecido junto
  com a tradução (na janela Escura, após "OCR:").
- **Gravar resultado em arquivo**: salva cada leitura num arquivo.
- **Copiar para a área de transferência**: copia o resultado
  automaticamente ao traduzir.
- **Idioma** (do motor moderno): quais modelos carregar.
- **Linhas verticais (reordenar por coluna)**: para texto japonês em
  coluna vertical, lê na ordem certa.

### Frequência

De quanto em quanto tempo fotografa: **1 (300 ms)**, **2 (1 s)**,
**3 (1,5 s)**, **4 (2 s)**, **5 (2,5 s)**. Mais rápido acompanha fala
ligeira; mais lento pesa menos.

### Avançado — correção de imagem

- **Filtro de cor** (só um por vez, ou nenhum): **RGB exato** (só passa
  a cor exata), **Faixas HSV** (passa uma faixa de cor), **Limiar**
  (preto e branco pelo corte).
- **Limiar** (0 a 255): o corte do preto e branco.
- **Erosão**: afina os traços das letras antes de ler.
- **Grupo de cor**: conjuntos de cor salvos. A lista mostra
  **[adicionar grupo]** e **[remover este grupo]** no começo, depois um
  item por grupo (`R`, `G`, `B`, faixas `S` e `V`). Trocar de item salva
  o grupo anterior e carrega o novo.
- **R, G, B** (0 a 255): a cor procurada.
- **S1, S2, V1, V2** (0 a 100): faixas de saturação e brilho.
- **Ver resultado da imagem**: mostra como a imagem tratada está
  ficando, para ajustar o filtro vendo.
- **Priorizar nuvem no pontual**: no traduzir-uma-vez, prefere o motor
  de nuvem quando disponível.

## 5. Aba Tradução e Idiomas

### Serviço de tradução

A lista tem 10 serviços (o 9 só aparece se a biblioteca existir na
máquina; o 10 aceita itens extras criados por você):

1. **Google Tradutor (web gratuito)** (padrão): sem chave, pela rede.
   Opção **Qualidade**: **Automática** (alta, cai para baixa se
   bloquear), **Alta** (sempre alta; bloqueio vira erro), **Baixa
   (rápida)**. A linha de estado mostra a qualidade em uso.
2. **Banco de dados local**: traduz por um arquivo seu de pares
   (funciona sem internet). Pede **Arquivo do banco**, **Ignorar
   maiúsculas** e **Correspondência parcial**.
3. **Tradutor web sem chave**: outro tradutor público, sem cadastro.
4. **Tradutor comercial por chave (KR)**: serviço pago coreano. Pede
   **Identificador**, **Segredo** e tem **Gerenciar chaves**.
5. **Tradutor por planilha em nuvem**: traduz por uma planilha sua no
   Google. Pede **Planilha**, **Identificador de cliente**, um campo
   de segredo sem rótulo, **Autenticar…**, **Apagar todos os
   tokens**, campo de código (**Cole aqui o código do Google…**),
   **Trocar código por token** e mostra se há token.
6. **Tradutor por navegador embutido**: traduz pelo Edge sem janela.
   Mostra o estado (**ocioso**, por exemplo) e tem **Verificar estado**.
7. **Tradutor comercial por chave (EU)**: serviço pago europeu. Pede
   **Chave** e o tipo de conta: **Endpoint gratuito** ou **Endpoint
   pago**.
8. **Gemini (modelo de linguagem)**: traduz por modelo do Google AI
   Studio. Pede **Chave** (**Cole aqui a chave do Google AI
   Studio…**, fica só na sua máquina), **Modelo** (lista com
   `gemini-3.5-flash-lite` de padrão, mais `gemini-2.0-flash`,
   `gemini-2.0-pro`, `gemini-1.5-flash`, `gemini-1.5-pro` e
   personalizado) e tem **Testar chave…** (mostra o resultado na hora).
   Se o modelo bloquear o conteúdo, cai na reserva Google; se a
   reserva falhar, o erro diz de onde veio cada falha.
9. **Tradutor local por processo auxiliar**: traduz num programa
   vizinho, sem rede.
10. **API personalizada** (mais itens `Custom – nome` que você criar):
    chama um endereço seu. Os campos moram nas opções avançadas.

- **Par global**: o par vigente (exibe `en → pt-BR`, só leitura; o
  ajuste fino é na grade abaixo).
- **Abrir documentação de tradução**: abre a ajuda do serviço atual.
- **Idiomas por serviço** (grade de 10 linhas): origem e destino
  próprios de cada serviço. O que está aqui vale sobre o par global.
  O padrão é inglês → português, com japonês → inglês por serviço.
  Linha vazia = serviço não oferece aquele idioma.

### Dicionário

- **Usar dicionário**: liga a correção antes de traduzir.
- Campo de arquivo: qual arquivo de pares usar.
- **Por palavra**: ligado, corrige palavra inteira; desligado, corrige
  qualquer trecho igual.
- **Passadas adicionais** (0 a 3): quantas rodadas extras de correção.
- **Abrir editor**: abre o editor de pares (ver seção 12).

### Voz

- **Leitura em voz alta**: fala a tradução no alto-falante (se o
  sistema não tiver voz, aparece desligado com explicação).
- **Aguardar fim da leitura**: só fotografa de novo depois que
  terminar de falar.

## 6. Aba Exibição

### Janela de tradução

Quatro escolhas (só uma vale), mais uma caixa:

- **Escuro**: janelinha escura rolável com o texto e o estado
  (traduzindo ou parado). Ideal para ler bastante coisa.
- **Camada** (padrão): texto flutuante com contorno duplo, fora da
  área do jogo. Fica **sempre na frente** (clicar no jogo não cobre)
  e o OCR continua ignorando ela.
- **Sobreposição**: tradução desenhada em cima do original, no mesmo
  lugar e tamanho parecido. O original segue visível por baixo.
  Exige motor com posição de palavra; ignora cliques e fica fora da
  captura.
- **Substituição**: igual à sobreposição, mas tampa o original com
  fundo opaco — só o português fica visível. Mesmas exigências.
- **Sempre no topo**: mantém a janela escura na frente das outras
  (a camada já é sempre na frente; a sobreposição cobre a tela toda).

Trocar de modo e aplicar ajusta sozinho o pré-requisito: se a
Sobreposição ou Substituição precisar de motor com posição e o atual
não servir, o programa troca para o moderno (ou sistema, ou clássico)
e avisa no rodapé. Todo o resto pessoal permanece.

### Camada — tamanho

- **Ajustar ao texto (até o máximo)**: a janela cola no texto.
- **Largura máxima** e **Altura máxima**: o teto do ajuste
  (**0 = livre**, sem teto).

### Camada — posição inicial

Onde a camada estreia (depois você arrasta e ela lembra):

- **Fora das áreas (padrão)**: no primeiro canto livre da tela.
- **Em cima, dentro da captura**: no topo do retângulo.
- **Embaixo, dentro da captura**: na base do retângulo.

Trocar aqui limpa a posição salva: aberta, reposiciona na hora;
fechada, na próxima estreia.

### Texto

- **Fonte…**: abre a lista de fontes do sistema; o campo mostra a
  escolhida (vazio = fonte do sistema).
- **Tamanho**: tamanho em pontos (padrão 14).
- Quatro botões de cor: **Texto**, **Contorno 1**, **Contorno 2** e
  **Fundo** (abrem o conta-gotas de cor). **Restaurar cores padrão**
  volta ao branco com contorno cinza e preto e fundo escuro
  translúcido.
- **Centralizar**: alinha o texto no centro em vez da esquerda.
- **Remover espaços do OCR**: junta o texto sem espaços (para
  japonês).
- **Usar cor de fundo**: desenha o retângulo de fundo atrás do texto.
- **Exibir número da área**: prefixa cada bloco com `1 :`, `2 :`…
- **Usar contorno de fonte**: desenha o contorno duplo ao redor das
  letras (sem ele, o texto puro fica mais legível fora do jogo).
- **Pré-visualização**: mostra na hora como o texto vai ficar.

### Sobreposição

- **Tamanho automático de fonte**: imita o tamanho da letra original
  em vez do tamanho fixo.
- **Fusão automática de blocos**: junta linhas próximas num bloco só.
- **Preservar direção do original**: mantém texto vertical como
  vertical.
- **Cor automática**, **Cor de fonte automática**, **Cor de fundo
  automática**: copiam as cores do jogo em vez das escolhidas.

## 7. Aba Sistema

- **Carregar perfil…**: abre um arquivo de configuração salvo.
- **Salvar perfil…**: guarda a configuração atual num arquivo.
- **Restaurar padrões**: volta tudo ao original.
- **Buscar configurações da comunidade…**: abre a janela da
  comunidade para pegar perfis prontos.
- **Exportar configuração**: gera um arquivo para compartilhar.
- **Configuração avançada…**: abre a janela de opções avançadas
  (seção 8).
- **Verificar a última versão ao iniciar**: avisa se saiu versão nova.
- **Começar na Configuração rápida**: abre o programa no assistente.
- Atalhos (7 linhas): cada ação mostra a combinação atual e tem botões
  **Padrão** (volta ao original) e **Limpar** (deixa sem atalho).
  Clicar no campo e apertar teclas grava combinação nova. Ver seção 14.
- Links: **Manual**, **Erros conhecidos**, **Repositório**, **Página
  do projeto** e **Comunidade** (todos abrem a página do projeto no
  GitHub).

## 8. Configuração avançada

Janela à parte, em grupos. Edita uma cópia: **Aplicar** grava,
**Restaurar padrões** volta (com confirmação).

- **Aplicativo**: **Modo bandeja** (fechar esconde em vez de
  perguntar); **Da direita para a esquerda** (inverte o leiaute);
  **Controle remoto sempre no topo**; **Modo compatível do
  segue-mouse**; **Usar somente a área que segue o mouse** (ignora as
  fixas); **Borda amarela na captura** (marca a janela anexada);
  **Cores da seleção** (**Cor de fundo da seleção**, **Cor de
  destaque**, **Pré-visualizar**, **Restaurar padrões**).
- **Atalhos avançados**: teclas para **Abrir perfil 1 a 4** (cada um
  com arquivo e botões de escolher e limpar), **Transparência
  forçada** e **Trocar serviço** (uma tecla por serviço: banco,
  planilha, Google, coreano, auxiliar, navegador, sem chave).
- **Janela de tradução**: em Sobreposição, **Usar transparência do
  fundo**, **Tamanho mínimo** e **Tamanho máximo** da fonte
  automática, **Permanência do instantâneo (s)** (quanto tempo o
  traduzir-uma-vez fica na tela); em Escuro, **Fonte do modo
  escuro…**; em Camada, **Alinhamento inferior** e **Alinhamento à
  direita**; em Geral, **Sempre no topo só durante a tradução**,
  **Ignorar tradução vazia**, **Ocultar também alterna a tradução**;
  **Memória de exibição** (repete a última tradução: caixa, quantidade
  e tempo).
- **Coletânea de tradução**: marca quais arquivos de tradução pronta
  valem (**Marcar todos** / **Desmarcar todos**), texto de informação,
  **Modo banco de dados** e **Ignorar maiúsculas**.
- **Tradução**: **Tradução ponte** (traduz pelo japonês no meio quando
  ajuda); **Tradutor alternativo em caso de erro** (reserva do
  navegador); grupo **API personalizada** (**Adicionar**, **Remover**,
  **Nome**, **URL**, **Cabeçalhos**, **Modelo de requisição**,
  **Modelo de resposta**, **Usar os mesmos códigos do tradutor web**,
  **Origem**, **Destino**, **URL base**); grupo **Modelo de
  linguagem** (**Instrução personalizada**, **Modelo personalizado**,
  **Não enviar a instrução padrão**, **Padrão** / **Econômico** /
  **Personalizado**, **Temperatura**, **Nível de raciocínio**,
  **Limite de saída**); grupo **Área de transferência** (**Traduzir a
  área de transferência** — traduz o que você copia quando parado —,
  **Exibir original**, **Exibir "traduzindo"**, **Formato da cópia**).
- **OCR**: **Priorizar nuvem no pontual** (também aparece na Captura).
- **Dicionário**: **Passadas adicionais** de 0 a 3 (também aparece no
  cartão do dicionário).

## 9. Controle remoto

Barrinha flutuante que fica por cima de tudo, para jogar sem abrir a
janela grande. Dá para arrastar, redimensionar pelo canto e esconder
no `×` (o programa continua rodando; **Mostrar controle remoto** na
aba Início traz de volta).

- **Gerenciar áreas**: abre a janela de áreas.
- **Área rápida**: lê um pedaço novo rapidinho, sem salvar.
- **Área instantânea**: traduz um trecho agora, uma única vez.
- Botão central: **Iniciar tradução** parado, **Parar** (fundo verde)
  traduzindo. Liga e desliga o laço.
- **Sistema**: abre as configurações.

## 10. Bandeja do sistema

O ícone na bandeja (perto do relógio) tem menu com: **Mostrar GORT
(configurações)** (abre a janela principal), **Mostrar controle
remoto**, **Gerenciar áreas de OCR…**, **Mostrar janela de tradução**
(traz a do modo atual), **Iniciar tradução**, **Janela de tradução
sempre no topo** (marcável), **Dicionário de correção** (marcável),
**Salvar perfil…**, **Carregar perfil…**, **Restaurar padrões**,
**Verificar atualização**, **Sair** e **Sobre**.

## 11. Áreas de OCR

Janela **Áreas de OCR** (botão **Gerenciar áreas…**):

- **Adicionar área**: cria um retângulo novo para ler.
- **Adicionar área de exclusão**: cria um retângulo que o leitor
  pula (placar, logotipo).
- **Limpar tudo**: apaga áreas e exclusões.
- **Ver resultado da imagem**: mostra a primeira área tratada.
- Lista cada item como `Área N — largura×altura @ (X,Y) [grupos]` (e
  `Exclusão N — …` para as de exclusão), cada um com **Remover**.
- **Aplicar** confirma; fechar sem aplicar desfaz o que foi mexido.

Tipos de área:

- **Fixa**: a que você desenhou; fica onde está.
- **Rápida** (`Ctrl+Shift+X`): seleção temporária, some depois de ler.
- **Instantânea** (`Ctrl+Shift+A`): traduz um trecho agora, uma vez
  só; o resultado fica na tela pelo tempo da permanência.
- **Segue-mouse** (`Ctrl+Shift+F`): um retângulo que acompanha o
  cursor. Nas opções avançadas dá para usar só ela ou o modo
  compatível.

Na tela, cada área vira uma moldura com título (`Área N — …`): borda
verde a normal, vermelha a de exclusão, roxa a que segue o mouse.
A moldura tem botão de **Cor** (conta-gotas), **Grupos** (qual filtro
de cor ela usa) e **X** (remover; a de exclusão só tem o X). Arrasta
pela barra, redimensiona pela borda e some enquanto traduz.

**Conta-gotas**: clicando na cor da moldura, abre a lupa que mostra o
pixel original (com ampliação de 1 a 4 vezes e leitura `R G B H S V`) e a
versão binarizada (preto = passa no filtro), com **Atualizar** para
recarregar.

## 12. Dicionário de correção

Troca termos que o leitor erra sempre antes de traduzir. O editor
(**Abrir editor**) tem **Texto reconhecido** (vem preenchido com o
que foi lido), **Correção**, **Aceitar** (grava e já vale, sem
reiniciar) e **Cancelar**.

O arquivo é texto simples em blocos: linha `/s`, linha de origem,
linha de destino e linha em branco. Com **Por palavra** ligado,
corrige a palavra inteira (a maior primeiro); desligado, corrige
qualquer trecho igual. **Passadas adicionais** repetem a correção de
1 a 4 rodadas, sem encadear dentro da mesma rodada. Dá para abrir o
editor pelo atalho (`Ctrl+Shift+S`) mesmo traduzindo.

## 13. Modos de exibição em detalhe

- **Escuro**: caixa rolável com a tradução e, se pedido, o original
  após "OCR:". Mostra se está traduzindo ou parado. Dá para arrastar
  pelo corpo; fechar só esconde.
- **Camada**: janela sem borda com o texto e contorno duplo. Parada,
  mostra fundo escuro com borda azul e dá para arrastar e
  redimensionar (ou limitar por números); traduzindo, fica
  transparente e atravessável, e acompanha o texto até o máximo
  configurado (encolhendo a fonte se precisar). Botão direito abre
  menu rápido (centralizar, remover espaços, transparência forçada,
  fechar).
- **Sobreposição**: abre uma janela invisível por área de captura,
  ancorada no retângulo com a escala do próprio monitor, e desenha
  cada bloco traduzido no lugar do original, com fonte parecida e
  quebra dentro do retângulo.
- **Substituição**: igual, mas pinta o retângulo de fundo opaco antes
  — o original some.

Escuro e camada são apagados da captura para o OCR nunca ler a
própria tradução; sobreposição e substituição ficam fora da captura
pelo sistema. Clicar no jogo nunca cobre a camada. Se a captura vier
toda preta 3 vezes seguidas, o programa avisa em vez de travar: use
o jogo em janela ou sem borda (tela cheia exclusiva não captura).

## 14. Atalhos de teclado

Todos globais (funcionam com o jogo na frente) e trocáveis na aba
Sistema:

| Aperte | Acontece |
|---|---|
| `Ctrl+Shift+Z` | Liga e desliga a tradução |
| `Ctrl+Shift+C` | Traduz uma vez |
| `Ctrl+Shift+A` | Traduz um trecho agora |
| `Ctrl+Shift+X` | Área rápida temporária |
| `Ctrl+Shift+F` | Área que segue o mouse |
| `Ctrl+Shift+S` | Abre o editor de dicionário |
| `Ctrl+Shift+D` | Oculta e exibe a janela |

## 15. Rodapé e saída

O rodapé da janela principal tem, da esquerda para a direita:

- **Aplicar**: grava tudo que foi mudado nas abas. Com a tradução
  rodando, ela pausa, aplica e retoma sozinha; se não parar a tempo,
  avisa e não aplica nada (a tela recarrega com o valor salvo, para
  nunca mentir). A confirmação aparece escrita no rodapé e some
  sozinha. Toda troca de serviço, modo ou posição vale na hora.
- **Doar** (coração): abre a página do projeto.
- Versão do programa, memória usada e processador usado.

Fechar a janela pergunta antes: sair de vez, ocultar (continua
rodando na bandeja) ou cancelar. A saída também mora na bandeja
(**Sair** encerra tudo).

## 16. Arquivos e dados

Tudo fica na sua máquina, em texto simples legível:

- **Perfil** (`profile.toml`): todas as escolhas das abas.
- **Avançado, aplicativo e atalhos**: arquivos próprios ao lado do
  perfil. Cópia `.bak` guarda a versão anterior.
- **Chaves** (`creds-*.toml`, um por serviço): ficam na pasta de
  dados, fora do repositório, e nunca entram no GitHub.
- **Dicionário** (`myDic.txt` de padrão) e **banco** (`empty.txt` de
  padrão): texto simples que dá para editar fora do programa.
- **Perfis da comunidade**: arquivos que você baixa e carrega pelo
  **Carregar perfil…**.

Nada sai da máquina além do texto enviado ao serviço de tradução
escolhido.
