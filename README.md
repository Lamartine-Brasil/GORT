<p align="center">
  <img src="src/Gort/Assets/logo-512.png" width="160" alt="GORT">
</p>

<h1 align="center">GORT - Game Ocr RealTime</h1>

<p align="center">
  Jogando algo em japonês ou inglês e não entende nada?
  <br>
  O <b>GORT</b> lê o texto da tela e mostra a tradução em português
  <b>na hora</b>, sem você sair do jogo.
</p>

---

## O que é, em poucas palavras

Você marca **onde** o texto aparece na tela. O GORT fotografa aquele
pedacinho o tempo todo, reconhece as letras, traduz e mostra o resultado
em português. Funciona com jogos, programas antigos e qualquer janela que
mostre texto como imagem.

![Aba Traduzir](docs/imagens/01-aba-traduzir.png)

## Começando em 3 passos

**1. Marque a área** — clique em **Áreas** e arraste um retângulo sobre a
caixa de diálogo do jogo.

**2. Aperte Traduzir** — clique em **Traduzir** (ou `Ctrl+Shift+Z`). Pronto,
a tradução aparece sozinha a cada fala nova.

**3. Ajuste se precisar** — troque o serviço de tradução, a velocidade ou
a aparência, e clique em **Aplicar**.

![Aba Ler](docs/imagens/02-aba-ler.png)

## O controle remoto

Aquela barrinha pequena que fica por cima de tudo, sempre à mão:

![Controle remoto](docs/imagens/07-controle-remoto.png)

| Botão | Serve para |
|---|---|
| **Áreas** | Gerenciar as regiões que serão lidas |
| **Rápida** | Ler um pedaço novo rapidinho, sem salvar |
| **Instantâneo** | Traduzir um trecho agora, uma única vez |
| **Traduzir** | Liga/desliga (fica verde traduzindo) |
| **Ajustes** | Abre as configurações |

Ela pode ser arrastada, redimensionada pelo canto e escondida pelo `×`
(o programa continua rodando).

## De olho na tela: 4 jeitos de ver a tradução

| Modo | Como fica |
|---|---|
| **Camada** (padrão) | Texto flutuante numa janela transparente, fora da área do jogo |
| **Sobreposição** | Texto desenhado em cima do original, no mesmo lugar |
| **Substituição** | Como a sobreposição, mas tampando o original com fundo |
| **Escuro** | Janelinha escura com o texto, ideal para ler bastante coisa |

![Modo Escuro](docs/imagens/08-modo-escuro.png)
![Modo Camada](docs/imagens/09-modo-camada.png)
![Modo Sobreposição](docs/imagens/10-modo-sobreposicao.png)

## As telas do programa

Tudo em português, organizado em abas: Traduzir, Ler, Dicionário e
idiomas, Mostrar, Avançado e Sistema.

![Aba Dicionário](docs/imagens/03-aba-dicionario.png)
![Aba Mostrar](docs/imagens/04-aba-mostrar.png)
![Aba Avançado](docs/imagens/05-aba-avancado.png)
![Aba Sistema](docs/imagens/06-aba-sistema.png)

## Perguntas de quem está começando

**Precisa de internet?** Para traduzir pelo Google, sim. O dicionário e o
banco de dados funcionam sem internet.

**Pesa no jogo?** O rodapé mostra memória e CPU ao vivo. O programa evita
trabalho repetido (se a tela não mudou, ele nem processa de novo) e usa
no máximo 4 núcleos na leitura do texto.

**Tela cheia funciona?** Use o jogo em modo janela ou janela sem borda.
Tela cheia exclusiva não dá para capturar.

**Onde ficam meus dados?** Na sua máquina: perfis, dicionários e chaves
(em arquivos de texto simples, nada vai para a nuvem além da tradução).

**E minha chave do Google (Gemini)?** Fica só no seu computador, num
arquivo separado que nunca entra no GitHub. Na aba Traduzir tem botão
**Testar chave…** para conferir na hora.

## Atalhos que valem ouro

| Aperte | Acontece |
|---|---|
| `Ctrl+Shift+Z` | Liga / desliga a tradução |
| `Ctrl+Shift+A` | Traduz um trecho agora |
| `Ctrl+Shift+X` | Área rápida temporária |
| `Ctrl+Shift+F` | Área que segue o mouse |

## Baixar e instalar (Windows)

Na página de releases do GitHub, baixe o `Gort.exe` e execute — a versão
é completa (com o .NET junto) e não precisa instalar nada.

---

<details>
<summary><b>Para desenvolvedores (compilar do código)</b></summary>

Requisitos: SDK do .NET 9. Comandos a partir de `src/`:

```powershell
dotnet build Gort.sln -c Release --nologo -v q
dotnet test Gort.sln -c Release --no-build --nologo -v q --results-directory ../releases/test-results
```

Publicar (Windows x64, versão completa):

```powershell
Remove-Item ../releases/windows-x64 -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish Gort/Gort.csproj -c Release -r win-x64 --self-contained true -o ../releases/windows-x64
```

Estrutura: `src/Gort/` (app), `src/Gort.Tests/` (198 testes),
`src/Gort.VisualTests/` (6 renders em `releases/visual-tests/`),
`releases/` (tudo gerado; só `PUBLICAR.md` versionado).

</details>

## Links

| Destino | Endereço |
|---|---|
| Repositório | https://github.com/Lamartine-Brasil/GORT |
| Página do projeto | https://github.com/Lamartine-Brasil/GORT |
| Comunidade | https://github.com/Lamartine-Brasil/GORT |
| Manual | https://github.com/Lamartine-Brasil/GORT |
| Erros conhecidos | https://github.com/Lamartine-Brasil/GORT |
| Doações | https://github.com/Lamartine-Brasil/GORT |

## Autor

**Lamartine Barbosa** — autor e publicador único.

| Nome | Papel |
|---|---|
| [Lamartine Barbosa](https://github.com/Lamartine-Brasil) | Autor único — código, textos, visual e publicação |
