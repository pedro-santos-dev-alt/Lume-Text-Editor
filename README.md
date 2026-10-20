# Lume

**Um espaço simples e cuidado para escrever no Windows.**

O Lume é um editor de texto de ambiente de trabalho, desenvolvido em C# com WPF e .NET 8. Esta é uma nova versão do projeto original [POO-PJ-TXT-Editor](https://github.com/pedro-santos-dev-alt/POO-PJ-TXT-Editor), que continua disponível no GitHub. A nova versão tem uma interface e uma base técnica renovadas, com o Lume como aplicação independente.

## Funcionalidades

- Página inicial para criar ou abrir documentos.
- Título obrigatório ao criar um documento; o nome escolhido aparece na janela e é sugerido ao guardar.
- Abrir ficheiros `.txt` e `.rtf`, incluindo abrir um ficheiro diretamente pelo Explorador do Windows.
- Guardar, Guardar como e imprimir.
- Formatação de texto: negrito, itálico e sublinhado.
- Localizar, substituir e substituir todas as ocorrências.
- Pesquisa com distinção entre maiúsculas e minúsculas, pesquisa de palavra inteira e feedback do resultado.
- Pesquisa enquanto escreves, com realce da correspondência no documento e indicação da linha e coluna.
- A correspondência encontrada mantém-se selecionada e visível no editor enquanto a pesquisa está aberta.
- Números de linha na margem do editor, sincronizados com o scroll.
- Abrir uma pasta de trabalho e alternar entre ficheiros `.txt` e `.rtf` pelo painel lateral; as subpastas são carregadas ao expandir.
- Temas claro e escuro, com preferência guardada entre sessões.
- Modo de foco, zoom, contagem de palavras e caracteres e posição do cursor.
- Recuperação de rascunhos após um fecho inesperado.
- Moldura e controlos de janela nativos do Windows, com uma identidade visual azul e violeta.
- Ícone da aplicação com o monograma Lume.
- Instalador para Windows com uma opção para disponibilizar o Lume como aplicação para ficheiros `.txt`.
- Verificação de novas versões ao iniciar e de seis em seis horas, com opção manual no menu **Ver**.
- Atualização automática: transfere a versão publicada no GitHub, confirma a integridade do ficheiro e atualiza a instalação atual sem desinstalação manual. Se estiveres a editar, a instalação aguarda até guardares o documento ou fechares a aplicação.

## Atalhos

| Atalho | Ação |
| --- | --- |
| `Ctrl + N` | Criar documento |
| `Ctrl + O` | Abrir documento |
| `Ctrl + S` | Guardar |
| `Ctrl + Shift + S` | Guardar como |
| `Ctrl + F` | Localizar e substituir |
| `Ctrl + B` / `Ctrl + I` / `Ctrl + U` | Negrito / itálico / sublinhado |
| `Ctrl + Shift + T` | Alternar tema |
| `F11` | Alternar modo de foco |
| `Ctrl + +` / `Ctrl + -` / `Ctrl + 0` | Zoom |

## Instalar

Descarrega `Lume-Setup-1.3.0-x64.exe` na página [Releases](https://github.com/pedro-santos-dev-alt/Lume-Text-Editor/releases) e executa o instalador. O runtime do .NET está incluído. Durante a instalação podes escolher se queres disponibilizar o Lume para abrir ficheiros `.txt`. Esta versão instala as atualizações automaticamente por cima da versão atual.

O Windows protege a escolha da aplicação predefinida. Depois de instalares, abre **Definições → Aplicações → Aplicações predefinidas**, procura `.txt` e seleciona **Lume**. Também podes usar **Abrir com** no Explorador de Ficheiros.

## Executar a partir do código

Abre `TxTEditor/TxTEditor.sln` no Visual Studio 2022 ou mais recente com a carga de trabalho **Desenvolvimento para desktop com .NET**, ou executa:

```powershell
dotnet run --project TxTEditor/TxTEditor.csproj
```

Para publicar uma versão autónoma e gerar o instalador, com o SDK .NET 8 e o Inno Setup 6 instalados:

```powershell
dotnet publish TxTEditor/TxTEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o TxTEditor/publish/win-x64
ISCC.exe Lume.iss
```

O instalador é criado em `dist/Lume-Setup-1.3.0-x64.exe`. Para publicar uma versão nova, atualiza a versão no projeto e em `Lume.iss`, cria uma tag `vX.Y.Z` e envia-a para o GitHub. A ação em `.github/workflows/release.yml` compila o instalador autónomo e publica-o como uma nova versão; o Lume transfere e instala essa versão automaticamente.
