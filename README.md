# DeviceModeler

Modelador paramétrico de dispositivos de drenagem (PVs, caixas, bocas de bueiro, sarjetas,
valetas) para o Civil 3D 2026. Cada dispositivo é descrito em **texto (YAML)**: parâmetros,
variáveis, um fluxo que monta uma **árvore CSG** e as saídas — sólidos, conectores de tubulação,
nó hidráulico (SWMM) e quantitativos.

```
DeviceModeler.sln
├─ src/Modeler.Core           net8.0, sem nenhuma referência ao AutoCAD
├─ src/Modeler.Civil3D        net8.0-windows, plugin do Civil 3D 2026 (comandos DM_*)
├─ tests/Modeler.Core.Tests   xUnit
├─ catalog/exemplos/          definições .yaml (pv_retangular.yaml)
├─ deploy/                    bundle de autoload (PackageContents.xml)
└─ tools/Install-Bundle.ps1   compila e instala o bundle
```

Unidades: **metros, m², m³, m³/s**; ângulos de rotação e de conexão em **graus**
(funções trigonométricas em radianos — use `rad()`/`graus()`).

Outro PC? Veja [docs/SETUP-NOVO-PC.md](docs/SETUP-NOVO-PC.md) (Claude Code, REA, clonar e testar).

```bash
dotnet build
```

```bash
dotnet test
```

## Uso da API

```csharp
using Modeler.Core.Definition;
using Modeler.Core.Results;

var def  = DeviceDefinition.Load("catalog/exemplos/pv_retangular.yaml");
var inst = def.Evaluate(new Dictionary<string, object>
{
    ["CotaFundo"] = 100.0,
    ["CotaTampa"] = 102.6,
    ["tampa"]     = "T900",
    ["Conexoes"]  = new[] { new ConnectionInput("saida", 0, 100.0, 0.60) },
});

inst.Solids["concreto"].Node         // árvore CSG (SubtractNode, PrismNode, ...)
inst.Connectors["saida"].Position    // Vec3 no sistema local
inst.Hydraulics[0]                   // SwmmJunction(InvertElevation, MaxDepth, ...)
inst.Quantities["concreto_m3"].Value // m³
inst.Variables["H"]                  // 2.6
inst.Grips, inst.Warnings, inst.Parameters
```

Parâmetros omitidos usam o `padrao`. Valores errados geram `ParameterValidationException`
(`parametro L: valor 5 m fora do intervalo permitido [0.8; 3]`). Erros da definição geram
`DefinitionException` com arquivo, linha, coluna e campo
(`pv.yaml(8,10): variaveis.Y: expressão inválida "A - * 3": '*' inesperado`). Erros ao avaliar
geram `EvaluationException`, no mesmo formato.

**Sistema local:** X e Y partem do ponto de inserção; **Z é a cota absoluta**. Na Fase 2 basta
aplicar translação em XY e rotação em Z.

---

## Especificação da linguagem

### Estrutura do arquivo

| Campo         | Obrig. | Descrição |
|---------------|:------:|-----------|
| `dispositivo` | sim | identificador estável (`pv_retangular`): letras, números e `_` |
| `versao`      | sim | versão da definição (`1.0.0`) |
| `categoria`   | sim | `pontual` (PV, caixa, boca) ou `linear` (sarjeta, valeta — ver Fase 2) |
| `nome`, `descricao` | | textos livres |
| `parametros`  | | entradas do usuário (mapa ordenado) |
| `variaveis`   | | valores calculados, em qualquer ordem |
| `validacoes`  | | regras verificadas antes do fluxo |
| `fluxo`       | | lista de instruções que montam os sólidos |
| `saidas`      | | sólidos, conectores, hidráulica e quantitativos declarativos |

O parser rejeita campos desconhecidos, sugerindo o nome provável:
`campo desconhecido 'minimo' (você quis dizer 'min'?)`.

### `parametros`

```yaml
parametros:
  L:
    tipo: comprimento      # obrigatório
    unidade: m             # opcional; validada contra o tipo
    padrao: 1.20           # obrigatório (exceto conexoes/booleano/texto com valores)
    min: 0.80
    max: 3.00
    valores: [1.00, 1.20, 1.50]   # lista de valores permitidos (número ou texto)
    grip: { eixo: x, posicao: "[Lext/2, 0, CotaTampa]", fator: 2 }
    alvo: superficie
    rotulo: Comprimento interno   # nome exibido (Fase 2: paleta de propriedades)
    grupo: Geometria              # categoria na paleta
    descricao: ...
```

| `tipo` | unidade | valor |
|--------|---------|-------|
| `numero` | qualquer das suportadas, ou nenhuma | real |
| `inteiro` | nenhuma ou `un` | inteiro |
| `comprimento`, `cota` | `m` | real |
| `area` | `m2` | real |
| `volume` | `m3` | real |
| `vazao` | `m3/s` | real |
| `angulo` | `grau` | real |
| `texto` | — | texto (use `valores` para uma lista fechada) |
| `booleano` | — | `verdadeiro`/`falso` (padrão `falso`) |
| `conexoes` | — | lista de `{nome, angulo, cota, diametro}` (padrão `[]`) |

- Unidades aceitas: `m`, `m2` (`m²`), `m3` (`m³`), `m3/s`, `grau`, `un`, `kg`.
- `min` e `max` são inclusivos e valem só para tipos numéricos. O `padrao` é validado já na carga.
- **`grip`**: `x`, `y` ou `z`, ou um mapa `{eixo, posicao, fator}`. `posicao` é uma expressão
  `[x, y, z]` sobre parâmetros e variáveis. Arrastar o grip uma distância *d* altera o parâmetro em
  *d* × `fator` (use `fator: 2` para dimensões simétricas). Na instância, os grips ficam em `inst.Grips`.
- **`alvo`** indica de onde a Fase 2 pode tirar o valor: `superficie`, `alinhamento`, `perfil`,
  `polilinha` ou `tubo`.
- **Conexões:** `angulo` em graus, medido em planta a partir de +X local no sentido anti-horário;
  `cota` é a geratriz inferior do tubo; `diametro` em m. Na API, informe
  `ConnectionInput(nome, angulo, cota, diametro)` ou dicionários com essas chaves. Uma conexão sem
  nome recebe `C1`, `C2`, ...

### `variaveis`

```yaml
variaveis:
  H:
    valor: CotaTampa - CotaFundo
    unidade: m
    descricao: Altura útil
  Lext: L + 2*esp          # forma curta
```

As variáveis podem usar parâmetros e outras variáveis, **em qualquer ordem**. O carregador monta o
grafo de dependências, avalia em ordem topológica e acusa ciclos:
`dependência circular: P -> Q -> R -> P`. Nomes inexistentes são erro de carga, com coluna e
sugestão de nome. Parâmetros e variáveis são somente leitura no fluxo.

### `validacoes`

```yaml
validacoes:
  - condicao: H >= 0.60
    mensagem: "Altura útil H = {H} m menor que o mínimo de 0.60 m"
    nivel: erro            # padrão; 'aviso' só registra em inst.Warnings
```

`{nome}` na mensagem é trocado pelo valor do parâmetro ou da variável.

### `fluxo`

Lista de instruções executadas em ordem:

| Instrução | Forma |
|-----------|-------|
| atribuição | `- nome = expressão` |
| saída | `- expor_solido("nome", sólido, "material")` ou `- conector(nome, posicao, direcao, diametro, filtro)` |
| condicional | `- se: condição` + `entao: [...]` + `senao: [...]` (opcional; aceita `então`/`senão`) |
| repetição | `- para cada: item em lista` + `faca: [...]` (aceita `faça`, `para_cada`, `in`) |

```yaml
fluxo:
  - corpo = subtrair(prisma(Lext, Bext, H + esp, [0, 0, CotaFundo - esp]),
                     prisma(L, B, H, [0, 0, CotaFundo]))
  - se: H > 2.0
    entao:
      - degraus = array_z(cilindro(0.025, 0.30, [-L/2 + 0.10, -0.15, CotaFundo + 0.30], "y"), n_degraus, 0.30)
    senao:
      - degraus = vazio()
  - para cada: c em Conexoes
    faca:
      - corpo = subtrair(corpo, rotacionar(cilindro(c.diametro + 0.10, 1, [0, 0, c.cota + c.diametro/2], "x"), c.angulo))
  - expor_solido("concreto", corpo, "Concreto")
```

- Nomes criados no fluxo valem até o fim do fluxo e também nas `saidas`. A variável do laço só
  existe dentro do `faca`.
- O carregador verifica os nomes estaticamente: usar um nome antes de criá-lo é erro de carga.
- Uma expressão solta, sem atribuição e sem ser uma saída, é rejeitada ("instrução sem efeito").
- Sólidos vazios (`vazio()`, ou `array_z` com n = 0) não são expostos.
- Proteções: no máximo 100 000 iterações somadas e 10 000 elementos por `intervalo`/`array`.
- **Cuidado com o YAML:** se a expressão tiver `: ` ou `#`, ou começar com `[`, `{`, `"` ou `'`,
  ponha a expressão inteira entre aspas.

### `saidas`

```yaml
saidas:
  solidos:
    tampa: cilindro(d_tampa, 0.05, [0, 0, CotaTampa - 0.05], "z")
    corpo: { solido: corpo, material: Concreto }
  conectores:
    - nome: saida
      posicao: "[Lext/2, 0, CotaFundo + 0.30]"
      direcao: "[1, 0, 0]"          # normalizada automaticamente
      diametro: 0.60
      filtro: concreto              # opcional: restringe quais tubos podem ligar
  hidraulica:
    swmm_juncao:                    # [JUNCTIONS] do SWMM
      cota_fundo: CotaFundo         # Invert Elev.
      profundidade_maxima: H        # Max. Depth
      profundidade_inicial: 0       # opcionais (padrão 0)
      sobrecarga: 0                 # Surcharge Depth
      area_alagamento: 0            # Ponded Area (m²)
  quantitativos:
    concreto_m3: Lext * Bext * (H + esp) - L * B * H
    forma_m2: { valor: 2*(Lext + Bext)*(H + esp), unidade: m2, descricao: Fôrma externa }
```

A unidade de um quantitativo sai do sufixo do nome (`_m3`, `_m2`, `_m`, `_un`, `_kg`) ou do campo
`unidade`.

### Expressões

| Elemento | Sintaxe |
|----------|---------|
| números | `1`, `1.20`, `1e-3` (ponto decimal; a vírgula separa argumentos) |
| textos | `"T600"` ou `'T600'` (aspas repetidas escapam: `'d''água'`) |
| lógicos | `verdadeiro`/`falso` (`true`/`false`) |
| listas e pontos | `[x, y, z]`, `lista[0]`, `p.x`/`p.y`/`p.z` |
| registros | `c.diametro`, `c.angulo`, `c.cota`, `c.nome` |
| aritmética | `+ - * / % ^` (`^` é associativo à direita e mais forte que o `-` unário: `-2^2 = -4`); `+` com texto concatena |
| comparação | `==  !=  <>  <  <=  >  >=`. A igualdade numérica tolera 1e-9 relativo. Não pode encadear: `a < b < c` é erro |
| lógica | `e`/`and`/`&&`, `ou`/`or`/`\|\|`, `nao`/`não`/`not`/`!` (com curto-circuito) |
| constante | `pi` |
| argumentos nomeados | `cilindro(0.6, 1.0, eixo = "x")` |

Precedência, da mais fraca para a mais forte: `ou` → `e` → `nao` → comparações → `+ -` →
`* / %` → unário `-` → `^` → `.campo`/`[i]`/chamada.

#### Funções matemáticas e de lista

| Função | Descrição |
|--------|-----------|
| `se(cond, a, b)` (`if`) | condicional preguiçosa: só avalia o ramo escolhido |
| `min(...)`, `max(...)`, `soma(...)` | aceitam vários números ou uma lista |
| `abs(x)`, `sqrt(x)` (`raiz`) | `sqrt` de número negativo é erro |
| `round(x, casas=0)` (`arred`) | o meio arredonda para longe de zero |
| `ceil(x)` (`teto`), `floor(x)` (`piso`) | |
| `sin`, `cos`, `tan` (`sen`) | em radianos |
| `rad(graus)`, `graus(rad)` | conversão de ângulo |
| `tamanho(lista)` | número de itens |
| `intervalo(inicio, fim, passo=1)` | `[inicio, inicio+passo, ...)`, sem incluir `fim` |
| `ponto(x, y, z)` (`vetor`) | o mesmo que `[x, y, z]` |
| `polar(angulo, distancia, z=0)` | ponto a `distancia` da origem no ângulo dado (graus) |

#### Operações geométricas (árvore CSG)

| Função | Nó | Descrição |
|--------|----|-----------|
| `prisma(L, B, H, base=[0,0,0])` | `PrismNode` | caixa L (X) × B (Y) × H (Z); `base` é o centro da face inferior |
| `cilindro(d, h, base=[0,0,0], eixo="z")` | `CylinderNode` | `base` é o centro da face inicial; `eixo` pode ser `"x"`, `"-y"`, ... ou `[dx, dy, dz]` |
| `vazio()` | `EmptyNode` | elemento neutro |
| `unir(a, b, ...)` | `UnionNode` | aceita listas; uniões aninhadas são achatadas |
| `subtrair(a, b, ...)` | `SubtractNode` | `a` menos os demais |
| `intersectar(a, b, ...)` | `IntersectNode` | |
| `mover(s, [dx, dy, dz])` ou `mover(s, dx, dy, dz)` | `TranslateNode` | |
| `rotacionar(s, angulo, eixo="z", centro=[0,0,0])` | `RotateNode` | graus, regra da mão direita |
| `array_z(s, n, passo)` | `ArrayNode` | n cópias empilhadas a cada `passo` em Z |
| `array(s, n, [dx, dy, dz])` | `ArrayNode` | n cópias com passo vetorial |
| `expor_solido(nome, s, material?)` | — | publica o sólido; só no fluxo |
| `conector(nome, posicao, direcao, diametro, filtro?)` | — | publica um conector; só no fluxo |

Dimensões precisam ser maiores que zero:
`prisma: 'H' deve ser maior que zero (recebido 0)`.

---

## Exemplo completo

[`catalog/exemplos/pv_retangular.yaml`](catalog/exemplos/pv_retangular.yaml) é um PV retangular
com estes parâmetros: `L`, `B`, `esp`, `CotaFundo`, `CotaTampa`, `tampa` (lista T600/T900),
`folga`, `lastro` e `Conexoes`. Ele mostra:

- as variáveis `H`, `Lext`, `Bext`, `d_tampa` e `n_degraus`;
- `corpo = prisma externo − vazio interno`;
- degraus a cada 0,30 m (`array_z`) só quando `H > 2,0`;
- um furo e um conector para cada conexão, com o volume dos furos descontado do concreto;
- um nó de junção SWMM;
- os quantitativos `concreto_m3`, `forma_m2`, `escavacao_m3`, `lastro_m3` e `degraus_un`.

Com os valores padrão (L = B = 1,20; esp = 0,15; H = 1,50):

- concreto = 1,50 × 1,50 × 1,65 − 1,20 × 1,20 × 1,50 = **1,5525 m³**
- escavação = 2,10 × 2,10 × 1,75 = **7,7175 m³**

---

## Arquitetura do Core

| Pasta | Conteúdo |
|-------|----------|
| `Expressions/` | `Lexer`, `ExpressionParser` (descendente recursivo), `Evaluator`, `FunctionRegistry` + `BuiltinFunctions`, `DependencyResolver`, `Expression` (texto + AST + posição no arquivo) |
| `Yaml/` | `DefinitionParser`: YamlDotNet RepresentationModel (guarda linha e coluna), validação estrutural e de nomes |
| `Definition/` | `DeviceDefinition`, `ParameterDef` (conversão e validação de valores), variáveis, validações, instruções e saídas |
| `Flow/` | `DeviceEvaluator`: parâmetros → variáveis → validações → fluxo → saídas |
| `Csg/` | `CsgNode` imutáveis, `ICsgVisitor<T>`, `Vec3`, `NaiveVolume` (volume para conferência) |
| `Results/` | `DeviceInstance`, `NamedSolid`, `Connector`, `SwmmJunction`, `Quantity`, `GripInstance`, `ConnectionInput` |
| `Geometry/` | `Placement` (local ↔ desenho) e `ConnectionGeometry` (tubo → conexão) |
| `Persistence/` | `ParameterCodec`: parâmetros ↔ JSON (gravado no Xrecord) |
| `Editing/` | `DeviceEditing`: valor por grip, reconciliação com nova versão, deslocamento de cotas |
| `Export/` | `Reports`: totais, CSV de quantitativos, `[JUNCTIONS]`/`[COORDINATES]` do SWMM |

Decisões de projeto:

- **Avaliador próprio, sem NCalc e sem compilar código.** Uma definição de catálogo nunca executa
  código arbitrário. A linguagem é estável, em português, com erros posicionados e extração de
  dependências.
- **Ordem por dependência, e não pela ordem de declaração**, com detecção de ciclos na carga.
- **Validação estática na carga:** campos, tipos, unidades, nomes e funções. Um catálogo quebrado
  falha ao abrir, e não quando o usuário insere a peça.
- **Árvore CSG só com dados:** nada de malha nem de AutoCAD no Core. A geometria real fica com quem
  implementa `ICsgVisitor<T>`.

## Fase 2: plugin do Civil 3D (`Modeler.Civil3D`)

`net8.0-windows`, x64. Referencia `acdbmgd`, `acmgd`, `accoremgd`, `AeccDbMgd` e `AecBaseMgd` da
instalação do Civil 3D 2026 com `Private=False` (Copy Local = false). Se o Civil 3D não estiver
instalado, usa a cópia em `refs\acad2026\` (ignorada pelo git). Também dá para forçar uma pasta:
`dotnet build -p:AcadDir=...`.

### Compilar e carregar

```bash
dotnet build DeviceModeler.sln
```

A saída fica em `NETLOAD\`: `Modeler.Civil3D.dll`, `Modeler.Core.dll`, `YamlDotNet.dll` e
`catalog\`. No Civil 3D, rode `NETLOAD` em `NETLOAD\Modeler.Civil3D.dll`. Para carregar
automaticamente ao abrir o Civil 3D, instale o bundle com o Civil 3D fechado:

```bash
powershell -ExecutionPolicy Bypass -File tools/Install-Bundle.ps1
```

`-Remove` desinstala o bundle.

**Sem compilar (outra máquina):** o repositório traz o bundle pronto em `dist/DeviceModeler.bundle`.
Copie a pasta inteira para `%APPDATA%\Autodesk\ApplicationPlugins\` e abra o Civil 3D 2026. A DLL é
net8.0, compilada contra o Civil 3D 2026 25.1.60, e carrega também nos builds mais novos, que rodam
em .NET 10. Para regenerar o bundle depois de mudar o código, rode
`powershell -ExecutionPolicy Bypass -File tools/Build-Dist.ps1`. As DLLs da Autodesk nunca vão para
o repositório.

**Alvo .NET 8 ou 10:** a partir do build 25.1.179, o Civil 3D 2026 roda em .NET 10. O csproj lê
`acdbmgd.runtimeconfig.json` da instalação usada como referência e compila para `net10.0-windows`
quando é o caso; caso contrário, compila para `net8.0-windows`. O manifesto `deploy/DeviceModeler.bundle/PackageContents.xml` vale
para a série R25.1 (Civil 3D 2026).

### Comandos

| Comando | O que faz |
|---------|-----------|
| `DM_INSERIR` | Abre uma janela com a lista do catálogo, o PropertyGrid dos parâmetros e um resumo que se recalcula a cada alteração (variáveis, quantitativos, avisos e erros; o OK só é habilitado quando tudo é válido). Depois pede o ponto e a rotação com jig e, se a definição tiver `alvo: superficie`, a superfície da cota da tampa |
| `DM_EDITAR` | Mesma janela, com os valores atuais; regenera ao confirmar |
| *grips* | Cada parâmetro com `grip:` ganha um grip. Clicar nele inicia `DM_GRIP`, que pede o novo ponto e aplica fator e min/max |
| `DM_CONECTAR` | Liga tubos de Pipe Network ao dispositivo (substitui ou adiciona). Preenche `Conexoes` (ângulo, cota de fundo, diâmetros interno e externo, handle) e os parâmetros com `alvo: tubo` (menor cota de fundo). O PV abre os furos e cria os conectores |
| `DM_SUPERFICIE` | Parâmetros com `alvo: superficie` passam a valer a cota da superfície no ponto de inserção |
| `DM_ATUALIZAR` | Relê os tubos (pelo handle) e o catálogo e regenera; avisa quando a definição mudou (hash) |
| `DM_AUTO` | Liga ou desliga a atualização automática: se um tubo ligado mudar, o dispositivo se refaz ao fim do comando |
| `DM_QUANTITATIVOS` | Totais por quantitativo na linha de comando, mais CSV detalhado (`;`) |
| `DM_SWMM` | Grava `[JUNCTIONS]` e `[COORDINATES]` de todos os dispositivos num `.inp` |
| `DM_INFO` | Mostra parâmetros, variáveis, quantitativos, conectores (em coordenadas do desenho) e o nó SWMM |
| `DM_CATALOGO` | Mostra ou troca a pasta do catálogo (gravada em `%APPDATA%\DeviceModeler\settings.json`) e lista as definições e os erros |

### Como fica no DWG

O desenho segue padrões comuns a plugins de dispositivos para Civil 3D (implementação própria):

- **Hospedeiro:** um `BlockReference` de um **bloco anônimo exclusivo**. Cada sólido de saída é um
  `Solid3d` dentro do bloco, na camada `DM-<NOME>` (ex.: `DM-CONCRETO`), com material quando
  existir um com esse nome no desenho. Sem o plugin, o desenho continua válido: é um bloco comum.
- **Dados:** Xrecord `DEVICEMODELER` no dicionário de extensão da referência, com marcador, versão
  do esquema, id, versão e hash da definição, caminho do YAML, nome da instância e os parâmetros em
  JSON (`ParameterCodec`), em pedaços com código 300.
- **Posição:** o bloco tem origem 0, e as coordenadas do bloco são o sistema local do dispositivo
  (Z = cota). Mover e girar a referência é suficiente.
- **Regeneração** (`DeviceService.Regenerate`):
  - reconcilia os valores com a definição atual (`DeviceEditing.Reconcile`);
  - se a referência foi deslocada em Z, soma o deslocamento às cotas e zera o Z;
  - restaura escala 1;
  - apaga o conteúdo do bloco e o recria. Se o bloco for compartilhado por cópias (COPY/ARRAY),
    a referência editada ganha um bloco novo antes.
- **Grips:** um `GripOverrule` registrado em `BlockReference`, filtrado pelo dicionário de extensão
  (`SetExtensionDictionaryEntryFilter`). A geometria nunca é refeita dentro do overrule: o clique
  no grip dispara o comando `DM_GRIP`.
- **Tubos:** `ObjectModified` só enfileira o handle do tubo; o processamento acontece em
  `CommandEnded`, com o documento travado, ignorando UNDO/REDO e os próprios comandos.

### Testes

- O que não depende do CAD está no Core e é coberto por `dotnet test`: `Placement`,
  `ConnectionGeometry`, `ParameterCodec`, `DeviceEditing` (grips, reconciliação, deslocamento de
  cotas) e `Reports` (CSV e SWMM).
- O plugin em si só pode ser verificado dentro do Civil 3D, com o roteiro abaixo.

**Roteiro de teste manual no Civil 3D 2026:**

1. `NETLOAD` e `DM_CATALOGO`: deve listar `pv_retangular`.
2. `DM_INSERIR` → PV retangular → mude `CotaTampa` para 103 e veja o resumo recalcular. Ponha
   `L = 5`: o PropertyGrid recusa. Clique em OK, escolha o ponto e a rotação, pule a superfície.
3. Confira as camadas `DM-CONCRETO`, `DM-DEGRAUS` (H > 2 m) e `DM-TAMPA`.
4. Selecione o PV, clique no grip de `L` e arraste: o PV se refaz com o novo comprimento.
5. Desenhe uma rede de tubos chegando ao PV e rode `DM_CONECTAR`: aparecem os furos e
   `CotaFundo` passa a valer a menor cota de fundo.
6. Mova a ponta de um tubo: com `DM_AUTO` ligado, o furo acompanha.
7. `COPY` o PV e edite a cópia com `DM_EDITAR`: o original não muda.
8. `DM_QUANTITATIVOS` (salve o CSV), `DM_SWMM` (abra o `.inp` no SWMM 5) e `DM_INFO`.
9. `UNDO` depois de cada passo: tudo volta.

### Limitações atuais e próximos passos

- A categoria `linear` (seção varrida ao longo de alinhamento/perfil, para sarjetas e valetas)
  ainda não tem gerador.
- O grip pede o ponto final, mas ainda não mostra a prévia da geometria durante o arrasto.
- A superfície não é acompanhada automaticamente: rode `DM_SUPERFICIE` de novo.
- **Presets de catálogo:** escolher um código do álbum DNIT IPR 736 preencherá as dimensões, e
  editar uma dimensão manualmente mudará o código para "genérico".
- Nós SWMM de armazenamento (storage) e de descarga (outfall), e exportação dos condutos.
