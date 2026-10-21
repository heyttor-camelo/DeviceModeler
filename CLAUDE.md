# CLAUDE.md — contexto do projeto para o Claude Code

Leia isto antes de trabalhar no repositório. Resume o que já foi feito, as decisões tomadas e o
que falta. A especificação completa da linguagem YAML e dos comandos está no `README.md`.

## O que é

Modelador paramétrico de dispositivos de drenagem para o Civil 3D 2026, em C#: PVs, caixas, bocas
de bueiro e, no futuro, sarjetas e valetas.

- Cada dispositivo é definido em **YAML**, numa ideia parecida com o `.pkt` do Subassembly Composer.
- A definição tem parâmetros, variáveis e um `fluxo` que monta uma **árvore CSG abstrata**.
- As saídas são sólidos, conectores de tubo, nó hidráulico SWMM (junção) e quantitativos.
- Unidades: m, m², m³ e m³/s; ângulos de rotação e de conexão em graus.

Usuário: Heyttor (engenheiro civil, fala português). Responda em **português**.

## Estrutura

```
DeviceModeler.sln
├─ src/Modeler.Core          net8.0, SEM AutoCAD — linguagem, avaliação, CSG, codec, relatórios
├─ src/Modeler.Civil3D       plugin (net8.0-windows ou net10.0-windows, auto) — comandos DM_* e C3DHYDRO
│   ├─ Infra/                log comum (HydroLog) e progresso (Progress)
│   ├─ Ui/Common/            tema, estilos, ícones 32×32, janela de progresso e de log (molde do Viário)
│   └─ Ui/Panel/             painel HYDRO: janela, monitor e uma classe por categoria
├─ tests/Modeler.Core.Tests  xUnit — tudo que não depende do CAD
├─ catalog/exemplos/         pv_retangular.yaml (caso de teste principal)
├─ deploy/                   PackageContents.xml do bundle + aviso de terceiros
├─ dist/DeviceModeler.bundle bundle PRONTO (DLLs versionadas de propósito), gerado por tools/Build-Dist.ps1
├─ tools/                    Install-Bundle.ps1 (instala em %APPDATA%), Build-Dist.ps1 (regera dist/)
└─ refs/acad2026/            DLLs da Autodesk copiadas p/ compilar sem Civil 3D — NÃO versionar (gitignore)
```

## Compilar e testar

```bash
dotnet build DeviceModeler.sln
dotnet test
powershell -ExecutionPolicy Bypass -File tools/Build-Dist.ps1   # depois de mudar o plugin
```

- Só considere uma tarefa pronta com `dotnet build` sem avisos e `dotnet test` verde.
- O plugin só pode ser verificado de verdade dentro do Civil 3D. O roteiro manual está no README
  ("Roteiro de teste manual"). Diga claramente o que não foi testado no CAD.
- **Alvo .NET:** o csproj do plugin lê `acdbmgd.runtimeconfig.json` da instalação usada como
  referência (`AcadDir`). Se for `net10.0`, compila para `net10.0-windows`; senão, `net8.0-windows`.
  - Na máquina de origem, o Civil 3D 2026 build 25.1.179 tinha DLLs que exigem .NET 10.
  - No PC do usuário, o Civil 3D 2026 (AeccDbMgd 13.8.0.1516) **roda em .NET 8.0.13**.
  - O `dist/` é sempre net8, compilado contra `refs/acad2026` (25.1.60), e carrega nos dois.
  - Sem `refs/`, `Build-Dist.ps1` usa a instalação local. Num Civil 3D .NET 8 isso também dá net8.
- `AcadDir` pode ser forçado: `dotnet build -p:AcadDir=C:\caminho`.
- Saída local: `NETLOAD\` (ignorado pelo git): **DLL única** `Modeler.Civil3D.dll` + `catalog\`.
  O alvo `EmbedDependencies` do csproj embute `Modeler.Core.dll` e `YamlDotNet.dll` como recursos
  `Embedded.<nome>.dll`, e o `EmbeddedAssemblies` (com `[ModuleInitializer]`) os carrega no mesmo
  `AssemblyLoadContext` do plugin. O catálogo também vai embutido (`Catalog/...`) e é extraído para
  `%APPDATA%\DeviceModeler\catalog` quando não há `catalog\` ao lado da DLL. Antes disso, copiar só
  a DLL principal dava `FileNotFoundException: Modeler.Core`. Restos antigos de `Modeler.Core.dll`
  na saída são apagados pelo alvo `RemoveStaleDependencies`.

## Convenções

- **Mensagens, DSL, comentários e docs em português.** A API C# tem nomes em inglês
  (`DeviceDefinition.Evaluate`, `inst.Quantities`). No YAML as chaves são em português (`parametros`,
  `fluxo`, `se/entao/senao`, `para cada`).
- Erros sempre com local: `arquivo(linha,coluna): campo: motivo` (`ModelerException` e subclasses).
- Siga o estilo do código vizinho: `sealed`, records para dados, `<summary>` curtos em português e
  chaves em todos os `if`.
- **Commits pequenos e descritivos, em português**, terminando com
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Nunca versione DLLs da Autodesk: a licença não permite, e o repositório é **público**
  (github.com/heyttor-camelo/DeviceModeler). O YamlDotNet (MIT) pode ir no `dist/`.
- Não copie código de terceiros descompilado (ver "SOLIDOS" abaixo).

## Decisões importantes (não refaça sem conversar)

- **Avaliador de expressões próprio, sem NCalc e sem compilar código.**
  - `Expressions/`: Lexer → ExpressionParser → Evaluator.
  - `se()` é preguiçosa; `e`, `ou` e `nao` têm curto-circuito.
  - A igualdade numérica tem tolerância de 1e-9.
  - Trigonometria em radianos; `rotacionar`, `polar` e o ângulo das conexões em graus.
- **Variáveis em ordem de dependência** (`DependencyResolver`), com ciclo virando erro na carga.
  Nomes e funções desconhecidos são erro na carga, com sugestão ("você quis dizer...").
- **YAML lido com o RepresentationModel do YamlDotNet**, para guardar linha e coluna; campos
  desconhecidos são rejeitados.
- **CSG só com dados** (`Csg/`); o plugin implementa `ICsgVisitor<Solid3d?>` (`Solid3dBuilder`).
- **Sistema local:** X e Y relativos ao ponto de inserção; **Z = cota absoluta**.
- **No DWG:**
  - Hospedeiro: `BlockReference` de um **bloco anônimo exclusivo**, com um `Solid3d` por saída em
    camadas `DM-<NOME>`.
  - Dados: Xrecord `DEVICEMODELER` no dicionário de extensão da referência (`DeviceRecord`), com id,
    versão, hash, caminho do YAML, nome e parâmetros em JSON (código 300, pedaços de 250 caracteres).
  - Posição e rotação ficam no transform do bloco.
  - Regeneração: `DeviceEditing.Reconcile`, deslocamento em Z vira soma nas cotas, escala volta a 1,
    o conteúdo do bloco é apagado e recriado, e um bloco compartilhado por cópia é separado.
    Se só mudaram parâmetros fora de `GeometryParameters` (análise de impacto), o bloco fica.
- **Grips:** `GripOverrule` em BlockReference com `SetExtensionDictionaryEntryFilter`, com o desenho
  dos grips do SOLIDOS (seta verde ACI 3, círculo ACI 4, losango ACI 4, contorno ACI 7, cores fixas).
  Mudança de out/2026 (pedido do usuário: grips iguais aos do SOLIDOS, com prévia): o `DM_GRIP` saiu;
  `MoveGripPointsAt` só calcula o valor e o aramado da prévia (Core, sem ACIS) e o `GripEnd` põe a
  edição na fila. **Continua valendo: nunca refaça geometria dentro do overrule.**
- **Fila no fim do comando (`DeviceWatcher`, antes `PipeWatcher`):** `ObjectModified`,
  `ObjectErased`, o `DeepClone` e os grips só enfileiram; o processamento é no `CommandEnded`, com
  `LockDocument`, em fases (grips → cópias → tubos apagados → tubos → superfícies → dependentes),
  ignorando UNDO/REDO. Tubos e superfícies são ignorados no fim dos próprios DM_*.
- **Conexões:** `{nome, angulo, cota (geratriz inferior interna), diametro (interno),
  diametro_externo, ref (handle do tubo)}`. O furo do PV usa `diametro_externo + 0.10`, centrado em
  `cota + diametro/2`.

## SOLIDOS (referência de arquitetura)

O método completo, a tabela comparativa e as ideias pendentes estão em `docs/ESTUDO-SOLIDOS.md`.
A instalação do REA está em `docs/SETUP-NOVO-PC.md` §4.

O usuário tem o plugin comercial SOLIDOS (TBN2NET) instalado em
`%APPDATA%\Autodesk\ApplicationPlugins\SOLIDOS.bundle` e autorizou estudá-lo para se inspirar.
Use REA ou ILSpy para tirar dúvidas de **mecanismo**, mas escreva o código do zero.

O que se aprendeu:

- Cada dispositivo é um BlockReference com BTR anônimo próprio. Os dados ficam num Xrecord do
  dicionário de extensão (chave `SOLIDOS_`).
- Os overrules (Object, Drawable, Grip, Geometry, Transform) são registrados em BlockReference com
  `SetExtensionDictionaryEntryFilter`.
- As edições marcam flags de "sujo", que são processadas no commit da transação mais externa. A
  regeneração apaga o conteúdo do BTR e recria.
- `DeepClone` dá à cópia um BTR próprio. Entre desenhos (`WblockClone`), o bloco vira um bloco comum.
- Mudanças em objetos ligados vão para uma fila em `ObjectModified` e são processadas no
  `CommandEnded`.
- A superfície entra por `FindElevationAtXY` mais um offset (`SurfAdj`), com hash da superfície para
  detectar mudança.
- No SOLIDOS os tubos são dispositivos lineares próprios; nós ligamos tubos da **Pipe Network**.
- As definições do SOLIDOS são fluxogramas XAML com VB compilado pelo Roslyn. Nós evitamos isso de
  propósito.

## Estado atual

**Fase 1, concluída:** o Core completo e o `pv_retangular.yaml`.
- Degraus a cada 0,30 m quando H > 2,0 m, furo e conector por conexão, junção SWMM.
- Quantitativos: concreto, fôrma, escavação, lastro e degraus.

**Fase 2, implementada:** compila sem avisos, mas **ainda não foi testada no Civil 3D**.
- Comandos: DM_INSERIR, DM_EDITAR, DM_CONECTAR, DM_SUPERFICIE, DM_ATUALIZAR, DM_AUTO,
  DM_QUANTITATIVOS, DM_SWMM, DM_INFO e DM_CATALOGO.
- Janela com PropertyGrid e resumo ao vivo, jig de inserção e bundle de autoload.

**HYDRO F0 (fundação), implementada (out/2026), não testada no CAD:**
- DLL única (Core, YamlDotNet e catálogo embutidos).
- Painel `C3DHYDRO` no molde do Viário (`Ui/Panel`), com as nove categorias da proposta B §6.1.
  Funcionam Dispositivos, Catálogo e Configuração; as outras mostram "em breve".
- Log comum (`HydroLog`, Ctrl+Shift+L), progresso (`Progress` + `ProgressWindow`), Ribbon HYDRO
  com um botão por categoria, e `DM_IR` (seleciona por handle).
- Core: `Affine` e `PlanOutline` (planta esquemática da árvore CSG para o monitor).

**HYDRO F1 (linguagem v2), implementada e coberta por testes no Core:**
- `polilinha`/`retangulo`/`circulo` (perfil), `extrudar` (`ExtrudeNode`), `plano`/`plano3`,
  `cortar` (`CutNode`, fica o lado da normal), vetores (`+ - *`), `nulo`/`existe()`/`ou()`.
- `escolha`/`casos`/`padrao`, `mensagem` (`inst.Log`).
- `familia`, `subtipo`, `codigo` (vira o nome `codigo`), `modelos.nome`/`modelos.dica`.
- Motor de macros `Text/Template` + `FormatSpec` (`{H:m:3}`, pt-BR, sem litros).
- `predefinicoes` e `ao_editar`/`editado()`, aplicados por `DeviceEditing.Edit` (janela de
  parâmetros, os grips e os alvos tubo/superfície passam por ele).
- Catálogo: `catalog/drenagem/` com caixa de ligação, boca de bueiro e boca de lobo simples.
- `cortar` no plugin usa `Solid3d.Slice` e confere o lado pelo centroide (não testado no CAD).

**HYDRO F2 (comportamento no DWG), implementada, não testada no CAD:**
- Regeneração por impacto (`DeviceDefinition.GeometryParameters`/`ChangesGeometry`).
- Grips iguais aos do SOLIDOS com prévia em aramado (`Wireframe` no Core); tipos `esticar`,
  `rotacionar` (parâmetro angulo, com `centro`) e `perfil` (só nas projeções, F4).
- `DeviceObjectOverrule` (DeepClone → bloco, id e nome novos no fim do comando; Erase → fila).
- Superfície seguida por `ComputeFingerPrint()` (ulong) gravado no registro.
- `DeviceRecord` esquema 2 (metadados JSON código 301: id GUID, superfície, número, rede) e
  `DEVICEMODELER_VERSAO` no NOD; `DM_AUDITAR` (lista e corrige) e categoria Verificação no painel.
- Osnap de conector (`ConnectorSnap`, `DM_OSNAP`), dica no cursor (`modelos.dica`) e menu de
  contexto HYDRO (`DeviceInteraction`). `InstanceCache` compartilhado (painel, dica, osnap, grips).

**HYDRO F3 (rede e lançamento), implementada, não testada no CAD:**
- Core `Network/`: `ConnectorMatch` (filtro por lista de tipos, direção, modos Fixo/Ajustar/
  Melhor3D/Melhor2D × Corrigir/Manter) e `FlowGraph` (sentido pelas elevações, componentes, ciclos,
  numeração Global/Invertido).
- `PipeNetworkLink`: rede, tamanhos de tubo da lista de peças, estrutura nula por dispositivo
  (`StructureHandle` no registro), tubo nativo ligado às estruturas.
- `NetworkSync` + fase "dispositivos alterados" do `DeviceWatcher`: a estrutura acompanha o
  dispositivo; pontas soltas vão ao conector pelo modo (`EndMode` no registro ou o padrão).
- Comandos `DM_REDE` (jig em cadeia, `ChainJig`), `DM_NUMERAR`, `DM_PONTAS`, `DM_ESTRUTURA`.
- Painel: categoria Redes (planta com setas do fluxo, abas Dispositivos e Trechos); Dispositivos
  com Nº e Rede.

**HYDRO F4 (desenho), implementada, não testada no CAD:**
- Core `Drawing/`: `DeviceStyle`/`ProjectionStyle` (componentes, JSON) e `ProfileProjection`
  (`AlignmentFrame`: estaca/offset/elevação, lado, distância máxima, silhueta).
- Plugin `Drawing/`: `StyleStore` (NOD `HYDRO_ESTILOS*`), estilo aplicado no `FillBlock`,
  `OutputRecord` (`HYDRO_SAIDA`) ligando rótulos (MLeader), tabelas nativas e projeções aos
  dispositivos, `OutputsUpdater` (última fase da fila: dispositivos, vistas e alinhamentos
  alterados), `Projections` (corte por `Solid3d.GetSection` no plano do alinhamento + mapeamento por
  `FindXYAtStationAndElevation`), `ProjectionGripOverrule` (losango na vista de perfil).
- Comandos `DM_PROJETAR`, `DM_ROTULO`, `DM_TABELA`, `DM_ESTILO`, `DM_SAIDAS`; categoria Saídas com
  editor de estilos.

**Padrão de interface:** o painel do Viário (github.com/heyttor-camelo/Civil3D2026Plugin,
`docs/DEVELOPMENT.md` §8). Esc nunca fecha janela; ícones vetoriais 32×32 com cores de significado;
"elevação" para altura de ponto em textos de interface ("cota" é dimensão).

**Próximos passos (em ordem sugerida):**
1. Testar a Fase 2 no Civil 3D com o roteiro do README e corrigir o que aparecer.
2. ~~DLL única~~ (feito na F0).
3. Prévia da geometria durante o arrasto do grip (DrawJig ou transient graphics).
4. Acompanhamento automático da superfície (guardar o handle da superfície e o hash).
5. Categoria `linear` (sarjeta, valeta): seção varrida ao longo de alinhamento e perfil.
6. Catálogo DNIT (álbum IPR 736, 5ª edição com emendas) com presets por código. Editar uma dimensão
   manualmente muda o código para "genérico" (ideia do SOLIDOS).
7. Nós SWMM storage e outfall, e exportação de condutos.

## Ferramentas úteis

- **REA** (MCP `rea`, pacote npm `rea-agents`): engenharia reversa. Para .NET ele lista tipos e
  membros, mas **não descompila IL**. Para o corpo dos métodos, use o ILSpy:
  `dotnet tool install -g ilspycmd` e depois
  `ilspycmd -t <Tipo> <dll> -o <pasta-temp>`, sempre fora do repositório.
- Python pode não estar disponível no Windows. Prefira PowerShell, Bash ou as ferramentas de edição.
