# Estudo do SOLIDOS com o REA: inspiração da arquitetura

O HYDRO foi desenhado a partir do estudo do SOLIDOS (TBN2NET), plugin comercial de
dispositivos de drenagem para Civil 3D instalado na máquina do autor. O estudo serviu para entender
**mecanismos**: como o SOLIDOS guarda dados no DWG, regenera, trata grips, cópias e tubos.
**Nenhum código foi copiado.** Todo o código do HYDRO foi escrito do zero.

## Onde fica o SOLIDOS

```
%APPDATA%\Autodesk\ApplicationPlugins\SOLIDOS.bundle\
  PackageContents.xml            autoload (Civil3D, R23.1 … R25.1)
  dotnet_8\SOLIDOS_2025.dll      versão .NET 8 (Civil 3D 2025+), escrita em VB.NET
  dotnet_4\SOLIDOS_2020/2021.dll versões .NET Framework
  Support\tutoriais\*.secb       exemplos de definição de dispositivo
  Support\TPU_DER_2018_0.csv     itens de quantitativo (código;descrição;unidade)
```

## Como foi feito (para repetir)

1. **Inventário com o REA (MCP `rea`).** Com as ferramentas de artefato gerenciado
   (`inspect_managed_artifact`, `inspect_managed_members`, `search_strings`), listamos tipos,
   membros, strings e recursos da `SOLIDOS_2025.dll`. Assim aparecem os nomes das classes
   (`SolGravityPointConstructor`, `ActivityConnector`, `SolRule`...), dos overrules e dos comandos.
2. **Definições `.secb`.** São XML comprimido com gzip. Depois de descomprimir, aparece um XML com
   metadados (nome, família, imagem, descrição) e um fluxograma **XAML** de "Activities" com
   expressões em **VB.NET**.
3. **Corpo dos métodos com o ILSpy.** O REA não descompila IL .NET (os provedores dele são para
   código nativo). Para ver como um mecanismo funciona, use:
   ```bash
   dotnet tool install -g ilspycmd
   ilspycmd -t <Namespace.Tipo> "%APPDATA%\Autodesk\ApplicationPlugins\SOLIDOS.bundle\dotnet_8\SOLIDOS_2025.dll" -o C:\temp\solidos-dec
   ```
   Gere a saída **fora do repositório**. O código descompilado é só para leitura e não deve ser
   versionado nem copiado.
4. **Perguntas guiadas.** O Claude recebeu perguntas objetivas, e as respostas estão resumidas
   abaixo: onde ficam os dados, como regenera, que overrules existem, como os tubos se ligam, como a
   superfície é seguida, como o autoload é feito.

Sugestão de pedido ao Claude para tirar uma dúvida nova:

> Use o REA (e o ilspycmd se precisar do corpo dos métodos) no SOLIDOS_2025.dll para entender
> como ele faz X. Quero só o mecanismo, explicado com suas palavras, para implementarmos do nosso jeito.

## O que se aprendeu × o que o HYDRO faz

| Tema | SOLIDOS | HYDRO |
|------|---------|---------------|
| Definição do dispositivo | fluxograma XAML com VB, compilado pelo Roslyn em tempo de execução | **YAML** com avaliador próprio; nunca executa código do catálogo |
| Parâmetros | propriedades com tipo, conversor de unidade e `ValueProvider` (Constante/Calculado/Lista); sem min/max | tipo, unidade, `padrao`, **`min`/`max`**, `valores`, `grip`, `alvo`, `rotulo`, `grupo` |
| Ordem dos cálculos | ordem do fluxograma | **grafo de dependências** com detecção de ciclo na carga |
| Hospedeiro no DWG | `BlockReference` de um **bloco anônimo próprio** | o mesmo (`DeviceService`) |
| Dados no DWG | Xrecord `SOLIDOS_` no dicionário de extensão, com pares chave/valor tipados | Xrecord `HYDRO` (antes `DEVICEMODELER`), com id, versão, hash, YAML, nome e **JSON** dos parâmetros |
| Sólidos de saída | entidades dentro do bloco, marcadas por componente (estilo, camada, material) | um `Solid3d` por saída nomeada, na camada `HY-<NOME>`, com material pelo nome |
| Regeneração | marca o que está "sujo" e processa no commit da transação externa; apaga o conteúdo do bloco e recria | `Regenerate`: apaga o conteúdo do bloco e recria; reconcilia valores com a nova versão da definição |
| Posição | `Location` e `Angle` nos parâmetros; transformação do bloco fixa em identidade | posição e rotação na própria transformação do bloco; Z vira soma nas cotas |
| Overrules | Object, Drawable, Grip, Geometry e Transform em BlockReference, filtrados pelo dicionário de extensão | `GripOverrule` com o mesmo filtro (`SetExtensionDictionaryEntryFilter`) |
| Grips | grips ligados a propriedades; prévia durante o arrasto | um grip por parâmetro com `grip:`; o clique dispara `HY_GRIP` (sem prévia ainda) |
| Cópias | `DeepClone` cria um bloco próprio para a cópia; entre desenhos, vira bloco comum | bloco compartilhado é separado na primeira edição |
| Tubos | dispositivos lineares próprios do SOLIDOS | **tubos da Pipe Network** (`HY_CONECTAR`): ângulo, cota, diâmetros e handle |
| Acompanhar mudanças | `ObjectModified` enfileira; processa no `CommandEnded`; ignora UNDO | o mesmo para tubos (`PipeWatcher`, `HY_AUTO`) |
| Superfície | `FindElevationAtXY` + offset `SurfAdj`, com hash da superfície para detectar mudança | `HY_SUPERFICIE` (o acompanhamento automático está nos próximos passos) |
| Regras | `SolRule` com expressões, prioridade e mensagem | `validacoes` com `condicao`, `mensagem` e `nivel` (erro/aviso) |
| Furos nas paredes | montados à mão em cada definição (fatiar e subtrair o tubo) | `para cada` conexão no YAML, com a cota e o diâmetro externo vindos do tubo |
| Autoload | bundle com `PackageContents.xml` (Platform Civil3D) | o mesmo (`deploy/`, `dist/`, `tools/Install-Bundle.ps1`) |

Ideias do SOLIDOS ainda não implementadas (estão nos próximos passos do `CLAUDE.md`):

- **Presets de catálogo:** escolher um código preenche as dimensões, e mexer numa dimensão muda o
  código para "genérico".
- Prévia da geometria durante o arrasto do grip.
- Seguir a superfície automaticamente.
- `DeepClone` e `WblockClone` próprios.
