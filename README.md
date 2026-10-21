# HYDRO

Modelador paramétrico de dispositivos de drenagem (PVs, caixas, bocas de bueiro, sarjetas,
valetas) para o Civil 3D 2026. Cada dispositivo é descrito em **texto (YAML)**: parâmetros,
variáveis, um fluxo que monta uma **árvore CSG** e as saídas — sólidos, conectores de tubulação,
nó hidráulico (SWMM) e quantitativos.

```
HYDRO.sln
├─ src/Hydro.Core           net8.0, sem nenhuma referência ao AutoCAD
├─ src/Hydro.Civil3D        net8.0-windows, plugin do Civil 3D 2026 (comandos HY_*)
├─ tests/Hydro.Core.Tests   xUnit
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
using Hydro.Core.Definition;
using Hydro.Core.Results;

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
| `familia`, `subtipo` | | textos de catálogo (família e material), para a lista, os rótulos e as tabelas |
| `codigo`      | | modelo de texto do código da instância (`"PV-{L*100:0:p}x{B*100:0:p}"`); vira o nome `codigo` nas validações, no fluxo e nas saídas |
| `modelos`     | | `nome` e `dica` (tooltip) da instância, modelos de texto |
| `predefinicoes` | | códigos de catálogo que preenchem dimensões (ver "Linguagem v2") |
| `ao_editar`   | | redundâncias resolvidas na edição (ex.: `H` ↔ elevações) |

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
- **`grip`**: `x`, `y` ou `z`, ou um mapa `{tipo, eixo, posicao, fator, centro, sentido}`. `posicao` é
  uma expressão `[x, y, z]` sobre parâmetros e variáveis. Na instância, os grips ficam em `inst.Grips`.
  - `tipo: esticar` (padrão): arrastar o grip uma distância *d* no `eixo` altera o parâmetro em
    *d* × `fator` (use `fator: 2` para dimensões simétricas). Desenho: seta verde.
  - `tipo: rotacionar`: só para parâmetro `angulo`; gira em planta em torno de `centro` (padrão: a
    origem, na elevação do grip). Desenho: círculo ciano ligado ao centro.
  - `tipo: perfil`: anda só na vertical; `sentido: cima` (padrão) ou `baixo` (para cima diminui).
    Aparece nas projeções em ProfileView (F4). Desenho: losango ciano.
  - Os valores respeitam `min`/`max`/`valores` e passam por `DeviceEditing.Edit` (códigos de catálogo
    e `ao_editar`).
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

### Linguagem v2

#### Perfis, extrusão e corte

| Função | Resultado | Descrição |
|--------|-----------|-----------|
| `polilinha(pontos, fechada=verdadeiro, convexidades=[], z=0)` | perfil | vértices `[x, y]` (na elevação `z`) ou `[x, y, z]`, coplanares; `convexidades` é o bulge de cada trecho, como no AutoCAD (positivo = arco anti-horário visto de cima, qualquer que seja a ordem dos vértices) |
| `retangulo(L, B, centro=[0,0,0], rotacao=0)` | perfil | retângulo L (X) × B (Y) centrado, girado em graus |
| `circulo(centro, raio)` | perfil | círculo em planta (dois arcos) |
| `extrudar(perfil, altura, direcao="z")` | sólido (`ExtrudeNode`) | extrusão de um perfil fechado; a direção não pode estar no plano do perfil |
| `plano(origem, normal)`, `plano3(p1, p2, p3)` | plano | `plano3`: normal = (p2 − p1) × (p3 − p1) |
| `cortar(solido, plano)` | sólido (`CutNode`) | **fica a parte do lado para onde a normal aponta** |

Pontos e vetores somam (`P1 + [0.3, 0, 0]`), subtraem e escalam (`2 * u`, `v / 2`). Há também
`distancia`, `distancia2d`, `normalizar`, `cruz` e `escalar` (produto escalar).

#### `nulo`, `existe()` e `ou()`

`nulo` é a ausência de valor. `existe(x)` e `ou(x, alternativa, ...)` são preguiçosas e absorvem
**só** a falta de valor (nulo, campo ausente num registro, índice fora da lista): `existe(c.sentido)`,
`ou(c.sentido, "entrada")`. Qualquer outro erro continua sendo erro (`ou(1/0, 2)` falha). O operador
`a ou b` continua valendo; `ou(` na posição de valor é a função.

#### `escolha` e `mensagem`

```yaml
fluxo:
  - escolha: Tipo
    casos:
      Simples: [ "ys = [0]" ]
      Duplo:   [ "ys = [-passo/2, passo/2]" ]
    padrao:
      - ys = [-passo, 0, passo]
  - mensagem: "Tipo {Tipo} com {tamanho(ys)} tubo(s)"   # vai para inst.Log
```

Se o valor é um parâmetro com `valores`, as chaves são conferidas na carga (com sugestão) e, sem
`padrao`, todos os valores precisam de caso. Sem caso nem `padrao` na avaliação, é erro.

#### Modelos de texto e formatação

Mensagens de validação, `codigo`, `modelos.nome`, `modelos.dica` e `mensagem` usam o mesmo motor:
campos `{expressão}` ou `{expressão:formato}`, com `{{`/`}}` para escrever chaves. Sem formato, o
valor sai como na linguagem (`1.5`); com formato, em pt-BR (vírgula decimal, ponto de milhar). As
partes do formato vão em qualquer ordem:

| Parte | Efeito | Exemplo |
|-------|--------|---------|
| unidade: `m cm mm km m2 cm2 ha km2 m3 m3/s % grau un kg t` | converte da unidade de base e põe o símbolo | `{H:cm:0}` → `150 cm`; `{i:%:2}` (i = 0,005) → `0,50 %` |
| número | casas decimais | `{H:m:3}` → `1,500 m` |
| `n` | sem o símbolo | `{H:m:2:n}` → `1,50` |
| `p` | ponto decimal, sem milhar | `{numero:0:p}` → `7` |
| `sinal` | sempre com + ou − | `+0,30 m` |
| `estaca` | estaca de 20 m | `{x:estaca}` → `12 + 3,45` |
| `maiusc`, `minusc`, `titulo` | caixa do texto | |

Litros (`l`, `l/s`) são recusados: o modelador trabalha em m³ e m³/s. Nos modelos `nome` e `dica`
valem parâmetros, variáveis, quantitativos e `codigo`, `nome`, `numero`, `familia`, `subtipo` e
`dispositivo` (`inst.Render(modelo, extras)`).

#### `predefinicoes`: códigos de catálogo

```yaml
predefinicoes:
  parametro: Catalogo            # texto; os valores permitidos viram "generico" + os códigos
  ao_editar_dimensao: generico   # ou reaplicar
  comuns: { esp: 0.15 }
  itens:
    CL-60x60: { L: 0.60, B: 0.60 }
    CL-100x100: { L: 1.00, B: 1.00, esp: 0.20 }
```

Com um código diferente de `generico`, as dimensões dele mandam na avaliação. Na edição
(`DeviceEditing.Edit`, usado pela janela de parâmetros, pelos grips e pelos alvos), escolher um
código aplica as dimensões; mudar uma dimensão que o código preenche troca o código para
`generico` (ou desfaz a mudança, com `reaplicar`). Tudo é conferido na carga: nomes, `min`/`max`,
`valores` e o padrão do parâmetro do código.

#### `ao_editar` e `editado()`

```yaml
ao_editar:
  H:         { CotaFundo: CotaTampa - H }        # editou H → recalcula CotaFundo
  CotaFundo: { H: CotaTampa - CotaFundo }
```

Roda na edição, antes da avaliação, com os parâmetros já editados; o resultado é validado
(`min`/`max`). Uma regra não pode reescrever o próprio parâmetro editado. `editado()` devolve o
nome do último parâmetro editado (vazio fora da edição).

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

Outros dispositivos do catálogo (`catalog/drenagem/`), que usam a linguagem v2. As dimensões dos
códigos são de referência e precisam ser conferidas com o álbum adotado no projeto:

| Arquivo | Dispositivo | Mostra |
|---------|-------------|--------|
| `caixa_ligacao.yaml` | caixa de ligação/passagem (códigos CL-60x60 a CL-120x120) | `extrudar`/`retangulo`/`circulo`, `predefinicoes`, `ao_editar` (H ↔ elevações), `escolha` da tampa (concreto, grelha, tampão), `codigo`, `modelos`, `mensagem` |
| `boca_bueiro.yaml` | boca de bueiro tubular simples, dupla ou tripla (BSTC/BDTC/BTTC) | alas por `polilinha` + `extrudar`, topo em rampa com `cortar(..., plano3(...))`, soma de vetores, `escolha` do número de tubos, um conector por tubo |
| `boca_lobo_simples.yaml` | boca de lobo com grelha, pela guia ou combinada | grelha com `array`, guia chapéu com seção no plano YZ extrudada em X e chanfro por `cortar` |

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

## Fase 2: plugin do Civil 3D (`Hydro.Civil3D`)

`net8.0-windows`, x64. Referencia `acdbmgd`, `acmgd`, `accoremgd`, `AeccDbMgd` e `AecBaseMgd` da
instalação do Civil 3D 2026 com `Private=False` (Copy Local = false). Se o Civil 3D não estiver
instalado, usa a cópia em `refs\acad2026\` (ignorada pelo git). Também dá para forçar uma pasta:
`dotnet build -p:AcadDir=...`.

### Compilar e carregar

```bash
dotnet build HYDRO.sln
```

A saída fica em `NETLOAD\`: **uma DLL só**, `Hydro.Civil3D.dll`, mais a pasta `catalog\`.
O `Hydro.Core.dll` e o `YamlDotNet.dll` vão embutidos nela como recurso (`EmbeddedAssemblies`
os carrega no `ModuleInitializer`), e o catálogo também: se só a DLL for copiada, as definições
são extraídas para `%APPDATA%\HYDRO\catalog`. No Civil 3D, rode `NETLOAD` em
`NETLOAD\Hydro.Civil3D.dll`. Para carregar automaticamente ao abrir o Civil 3D, instale o bundle
com o Civil 3D fechado:

```bash
powershell -ExecutionPolicy Bypass -File tools/Install-Bundle.ps1
```

`-Remove` desinstala o bundle.

**Sem compilar (outra máquina):** o repositório traz o bundle pronto em `dist/HYDRO.bundle`.
Copie a pasta inteira para `%APPDATA%\Autodesk\ApplicationPlugins\` e abra o Civil 3D 2026. A DLL é
net8.0, compilada contra o Civil 3D 2026 25.1.60, e carrega também nos builds mais novos, que rodam
em .NET 10. Para regenerar o bundle depois de mudar o código, rode
`powershell -ExecutionPolicy Bypass -File tools/Build-Dist.ps1`. As DLLs da Autodesk nunca vão para
o repositório.

**Alvo .NET 8 ou 10:** a partir do build 25.1.179, o Civil 3D 2026 roda em .NET 10. O csproj lê
`acdbmgd.runtimeconfig.json` da instalação usada como referência e compila para `net10.0-windows`
quando é o caso; caso contrário, compila para `net8.0-windows`. O manifesto `deploy/HYDRO.bundle/PackageContents.xml` vale
para a série R25.1 (Civil 3D 2026).

### Comandos

| Comando | O que faz |
|---------|-----------|
| `HY_INSERIR` | Abre uma janela com a lista do catálogo, o PropertyGrid dos parâmetros e um resumo que se recalcula a cada alteração (variáveis, quantitativos, avisos e erros; o OK só é habilitado quando tudo é válido). Depois pede o ponto e a rotação com jig e, se a definição tiver `alvo: superficie`, a superfície da cota da tampa |
| `HY_EDITAR` | Mesma janela, com os valores atuais; regenera ao confirmar |
| *grips* | Cada parâmetro com `grip:` ganha um grip (seta, círculo de rotação ou losango de perfil). Arraste: a prévia em aramado e o valor aparecem durante o arrasto, e o dispositivo se refaz ao soltar |
| `HY_CONECTAR` | Liga tubos de Pipe Network ao dispositivo (substitui ou adiciona). Preenche `Conexoes` (ângulo, cota de fundo, diâmetros interno e externo, handle) e os parâmetros com `alvo: tubo` (menor cota de fundo). O PV abre os furos e cria os conectores |
| `HY_SUPERFICIE` | Parâmetros com `alvo: superficie` passam a valer a elevação da superfície no ponto de inserção. Com "Acompanhar" (padrão), a elevação é refeita quando a superfície mudar |
| `HY_AUDITAR` | Lista os problemas dos dispositivos (esquema antigo, YAML ausente ou alterado, bloco compartilhado, nome ou id repetido, conexão para tubo apagado, superfície apagada) e oferece as correções automáticas. A categoria Verificação do painel mostra a mesma lista |
| `HY_OSNAP` | Liga ou desliga o osnap de conector |
| `HY_REDE` | Lançamento em cadeia: escolhe o dispositivo e os parâmetros uma vez (mesma janela do `HY_INSERIR`), o nome da rede de tubos, o diâmetro e a declividade, e a superfície (opcional). Cada clique insere um dispositivo (girado na direção do trecho) com uma estrutura nula da Pipe Network e o tubo nativo desde o anterior, já com furo e conector dos dois lados; a elevação de fundo desce pela declividade e a da tampa vem da superfície. O jig mostra o aramado do dispositivo e o trecho. Desfazer volta um trecho; no fim, oferece a numeração |
| `HY_NUMERAR` | Numera pelo fluxo (Global: das cabeceiras mais distantes para a descarga, de modo que o fluxo vá sempre do menor para o maior número; Invertido: a descarga é 1) e renomeia pelo `modelos.nome` (e a estrutura nula junto) |
| `HY_PONTAS` | Modo de recálculo das pontas soltas dos tubos ligados (sem estrutura): conector Fixo, Ajustar (o mais próximo), Melhor3d ou Melhor2d (o que deixa o tubo mais curto) × elevação Corrigir (a do conector) ou Manter. Grava no dispositivo e aplica já; depois, mover o dispositivo leva as pontas |
| `HY_ESTRUTURA` | Cria a estrutura nula da Pipe Network de cada dispositivo e liga a ela as pontas soltas dos tubos conectados: mover o dispositivo passa a mover a estrutura e os tubos |

| `HY_PROJETAR` | Projeta dispositivos numa vista de perfil: o sólido é **cortado** pelo plano vertical do alinhamento na estaca do dispositivo (tangente local) e o resultado vai para a vista com `FindXYAtStationAndElevation` (exagero e datum). Pede o estilo de projeção, o lado (Ambos/Esquerdo/Direito) e a distância máxima ao alinhamento |
| `HY_ROTULO` | Rótulo (MLeader) com um modelo de macros (padrão: a dica da definição); acompanha o dispositivo |
| `HY_TABELA` | Tabela nativa (Padrão: Nº, nome, dispositivo, código, X, Y, elevações e altura; Quantitativos: os quantitativos das definições); com Enter na seleção, pega todos os dispositivos, inclusive os que vierem depois |
| `HY_ESTILO` | Estilo de dispositivo: Aplicar aos dispositivos, Criar a partir de um dispositivo (uma linha por sólido) ou Atualizar (refazer depois de editar o estilo no painel) |
| `HY_SAIDAS` | Refaz todos os rótulos, tabelas e projeções |

**Estilos e saídas.** O **estilo de dispositivo** é uma tabela de componentes casada pelo nome dos
sólidos (`expor_solido("concreto", ...)` → componente "concreto"), com camada, visibilidade (o
invisível nem entra no bloco), cor, material e tipo de linha; "*" vale para os sólidos sem linha. O
**estilo de projeção** tem os componentes corte (arestas da face cortada), hachura (padrão, escala e
ângulo), contorno (silhueta), ocultas (arestas fora do plano, tracejadas) e eixo, mais o lado e a
distância máxima padrão. Os estilos ficam no desenho (`HYDRO_ESTILOS`, `HYDRO_ESTILOS_PROJECAO`) e
se editam no painel (Saídas → estilo → Componentes). Rótulos, tabelas e projeções guardam o vínculo
(`HYDRO_SAIDA`) e se refazem no fim do comando quando o dispositivo, a vista de perfil ou o
alinhamento mudam; os de um dispositivo apagado saem junto. Nas projeções, os grips `tipo: perfil`
(e os de esticar em Z) aparecem como losango e arrastam a elevação na vista, com prévia.

Campos dos modelos de rótulo e tabela: parâmetros, variáveis, quantitativos, `codigo`, `nome`,
`numero`, `rede`, `x`, `y`, `rotacao`, `elev_tampa`, `elev_fundo` e `altura`
(ex.: `"{nome}\nCT {elev_tampa:m:3}\nCF {elev_fundo:m:3}"`).

**Rede.** Uma rede é o conjunto de dispositivos ligados por tubos (um tubo que aparece nas conexões
de dois dispositivos é um trecho entre eles; o sentido vem das elevações de fundo). Os tubos são da
Pipe Network do Civil 3D; cada dispositivo pode ter uma estrutura nula no mesmo ponto, sincronizada
com ele (posição, rotação e elevações de tampa e fundo). Conectores têm filtro (lista de tipos:
`tubo`, `valeta`...) e direção: uma ponta só vai para um conector que aceita o tipo e de cujo lado o
tubo sai.
| `HY_ATUALIZAR` | Relê os tubos (pelo handle) e o catálogo e regenera; avisa quando a definição mudou (hash) |
| `HY_AUTO` | Liga ou desliga a atualização automática: se um tubo ligado mudar, o dispositivo se refaz ao fim do comando |
| `HY_QUANTITATIVOS` | Totais por quantitativo na linha de comando, mais CSV detalhado (`;`) |
| `HY_SWMM` | Grava `[JUNCTIONS]` e `[COORDINATES]` de todos os dispositivos num `.inp` |
| `HY_INFO` | Mostra parâmetros, variáveis, quantitativos, conectores (em coordenadas do desenho) e o nó SWMM |
| `HY_CATALOGO` | Mostra ou troca a pasta do catálogo (gravada em `%APPDATA%\HYDRO\settings.json`) e lista as definições e os erros |
| `C3DHYDRO` | Abre o painel HYDRO. `C3DHYDRO Dispositivos` (ou `Redes`, `Catalogo`, `Configuracao`...) abre direto na categoria; Enter abre na última usada |
| `HY_IR` | Seleciona e enquadra um dispositivo pelo handle; o painel usa antes de `HY_EDITAR`, `HY_INFO` etc. |

Os comandos que terminam com um dispositivo ou com o catálogo (`HY_INSERIR`, `HY_EDITAR`,
`HY_INFO`, `HY_CATALOGO`) levam o painel, se estiver aberto, até esse item.

### Painel HYDRO (`C3DHYDRO`)

Uma janela WPF não modal e de instância única, no molde do painel do Viário (Civil3D2026Plugin,
`docs/DEVELOPMENT.md` §8). De cima para baixo:

1. **Categorias** com contagem e aviso: Redes, Dispositivos, Bacias, Setores, Verificação, Saídas,
   Simulação, Catálogo e Configuração. As que ainda não têm conteúdo aparecem esmaecidas e mostram
   "em breve".
2. **Barra de comandos** só com ícones vetoriais 32×32 (desenhados em código, `HydroIcons`), com
   tooltip rico (título, desenho grande, descrição, passos e o nome do comando) e menus suspensos.
   À direita, "Localizar:" filtra a lista.
3. **Monitor** escuro: planta dos dispositivos do desenho (contorno de cada peça, furos tracejados,
   conectores em magenta e direção dos tubos em ciano) ou a planta do item do catálogo nos valores
   padrão. Roda = zoom, arrastar = pan, duplo clique = enquadrar.
4. **GridSplitter**, **lista** com marcação (espaço marca a seleção), coluna de aviso e menu no
   botão direito; duplo clique faz a primeira ação do menu (ir até o dispositivo).
5. **Abas do item**: Propriedades (com ações), Variáveis, Quantitativos, Conectores e Avisos.
6. **Barra de status**, Preferências (tema claro ou escuro) e Log.

O painel só manda comandos (`SendStringToExecute`); quem pede clique no desenho continua na linha de
comando. **Esc nunca fecha a janela.** O log comum fica em `%LOCALAPPDATA%\HYDRO\logs`
(Ctrl+Shift+L em qualquer janela do HYDRO) e as operações longas usam a janela de progresso comum.
A Ribbon ganha a aba **HYDRO**, com um botão grande por categoria (`C3DHYDRO <categoria>`) e os
atalhos Inserir, Editar e Conectar.

### Como fica no DWG

O desenho segue padrões comuns a plugins de dispositivos para Civil 3D (implementação própria):

- **Hospedeiro:** um `BlockReference` de um **bloco anônimo exclusivo**. Cada sólido de saída é um
  `Solid3d` dentro do bloco, na camada `HY-<NOME>` (ex.: `HY-CONCRETO`), com material quando
  existir um com esse nome no desenho. Sem o plugin, o desenho continua válido: é um bloco comum.
- **Dados:** Xrecord `HYDRO` no dicionário de extensão da referência, com marcador, versão
  do esquema, id, versão e hash da definição, caminho do YAML, nome da instância e os parâmetros em
  JSON (`ParameterCodec`), em pedaços com código 300. O **esquema 2** acrescenta um JSON de
  metadados (código 301): id da instância (GUID), superfície seguida e a impressão digital dela,
  número e rede. O esquema 1 continua legível e migra na próxima gravação (ou no `HY_AUDITAR`). O
  dicionário de objetos nomeados guarda `HYDRO_VERSAO` (versão do HYDRO e do esquema). Desenhos do
  tempo em que o plugin se chamava DeviceModeler (`DEVICEMODELER`, `DEVICEMODELER_VERSAO`) continuam
  legíveis; rode `HY_AUDITAR` neles para migrar (até lá, grips e cópias não reconhecem esses blocos).
- **Posição:** o bloco tem origem 0, e as coordenadas do bloco são o sistema local do dispositivo
  (Z = cota). Mover e girar a referência é suficiente.
- **Regeneração** (`DeviceService.Regenerate`):
  - reconcilia os valores com a definição atual (`DeviceEditing.Reconcile`);
  - se a referência foi deslocada em Z, soma o deslocamento às cotas e zera o Z;
  - restaura escala 1;
  - apaga o conteúdo do bloco e o recria. Se o bloco for compartilhado por cópias (COPY/ARRAY),
    a referência editada ganha um bloco novo antes;
  - **por impacto:** se só mudaram parâmetros que não chegam aos sólidos (`GeometryParameters`, uma
    análise estática para trás pelo fluxo — folga de escavação, lastro...), o bloco fica e só o
    registro é regravado.
- **Grips:** um `GripOverrule` registrado em `BlockReference`, filtrado pelo dicionário de extensão
  (`SetExtensionDictionaryEntryFilter`), com o desenho dos grips do SOLIDOS: seta verde (ACI 3),
  círculo ciano (ACI 4) ou losango ciano, todos com contorno ACI 7 e tamanho pelo `GRIPSIZE`. Cores
  fixas, como no SOLIDOS (ele não usa `GRIPCOLOR`/`GRIPHOT`). Ao arrastar, o grip desenha a prévia
  em aramado (avaliação do Core, sem ACIS) e o valor; ao soltar, a edição vai para a fila e o
  dispositivo se refaz no fim do `GRIP_STRETCH`. A geometria nunca é refeita dentro do overrule.
- **Fila no fim do comando** (`DeviceWatcher`): os eventos e overrules só enfileiram; no
  `CommandEnded`, com o documento travado, as fases rodam em ordem: grips → cópias → tubos apagados
  → tubos alterados → superfícies alteradas → dependentes de dispositivos apagados. Ignora UNDO/REDO.
- **Cópias e exclusões** (`DeviceObjectOverrule`): o `DeepClone` enfileira a cópia, que no fim do
  comando ganha bloco próprio, id e nome novos (pelo `modelos.nome`). O `Erase` avisa a fila.
- **Superfície seguida:** `HY_SUPERFICIE` grava a superfície e o `ComputeFingerPrint()` dela; se a
  superfície mudar (`ObjectModified`) e a impressão digital for outra, a elevação é refeita.
- **Osnap de conector** (`HY_OSNAP`): `CustomObjectSnapMode` que prende o cursor nos conectores
  (glifo: quadrado magenta com círculo).
- **Dica e menu:** ao parar o cursor sobre um dispositivo aparece o `modelos.dica` (ou nome e
  código) e os avisos; o botão direito traz Editar, Conectar, Atualizar, Informações e Mostrar no
  painel.

### Testes

- O que não depende do CAD está no Core e é coberto por `dotnet test`: `Placement`,
  `ConnectionGeometry`, `ParameterCodec`, `DeviceEditing` (grips, reconciliação, deslocamento de
  cotas) e `Reports` (CSV e SWMM).
- O plugin em si só pode ser verificado dentro do Civil 3D, com o roteiro abaixo.

**Roteiro de teste manual no Civil 3D 2026:**

1. `NETLOAD` e `HY_CATALOGO`: deve listar `pv_retangular`.
2. `HY_INSERIR` → PV retangular → mude `CotaTampa` para 103 e veja o resumo recalcular. Ponha
   `L = 5`: o PropertyGrid recusa. Clique em OK, escolha o ponto e a rotação, pule a superfície.
3. Confira as camadas `HY-CONCRETO`, `HY-DEGRAUS` (H > 2 m) e `HY-TAMPA`.
4. Selecione o PV: os grips de `L`, `B` e `CotaTampa` aparecem como setas verdes com contorno. Arraste
   o de `L`: o aramado amarelo e o valor acompanham o cursor; ao soltar, o PV se refaz. Esc no meio
   do arrasto não muda nada.
5. Desenhe uma rede de tubos chegando ao PV e rode `HY_CONECTAR`: aparecem os furos e
   `CotaFundo` passa a valer a menor cota de fundo.
6. Mova a ponta de um tubo: com `HY_AUTO` ligado, o furo acompanha.
7. `COPY` o PV e edite a cópia com `HY_EDITAR`: o original não muda.
8. `HY_QUANTITATIVOS` (salve o CSV), `HY_SWMM` (abra o `.inp` no SWMM 5) e `HY_INFO`.
9. `UNDO` depois de cada passo: tudo volta.
10. **DLL única:** num Civil 3D limpo, `NETLOAD` só em `Hydro.Civil3D.dll` (sem as outras DLLs ao
    lado) e `HY_CATALOGO`: deve listar o catálogo, extraído para `%APPDATA%\HYDRO\catalog`.
11. **Painel:** `C3DHYDRO`. Confira as nove categorias (as sem conteúdo dizem "em breve"), os
    tooltips da barra de comandos, a planta dos dispositivos no monitor (zoom, pan, duplo clique),
    a lista (marcação com espaço, botão direito, duplo clique leva ao dispositivo) e as abas. Troque
    o tema em Preferências e confira que **Esc não fecha** o painel. Ctrl+Shift+L abre o log.
12. **Ribbon:** a aba HYDRO tem um botão por categoria; cada um abre o painel na categoria.
13. **Cópias:** `COPY` de um PV: no fim do comando a cópia ganha nome novo (PV-2...) e bloco
    próprio (`HY_AUDITAR` não acusa bloco compartilhado). Apague um tubo ligado: a conexão sai e o
    furo some no fim do comando.
14. **Superfície:** `HY_SUPERFICIE` com Acompanhar; edite a superfície (suba um ponto): a tampa
    acompanha no fim do comando.
15. **Osnap e dica:** `HY_OSNAP Ligar`; desenhe uma linha e passe perto de um conector: o glifo
    magenta aparece. Pare o cursor sobre o PV: a dica mostra o `modelos.dica`. Botão direito sobre
    o PV selecionado: o grupo HYDRO tem Editar, Conectar, Atualizar, Informações e Mostrar no painel.
16. **Boca de bueiro:** insira `boca_bueiro`, gire a ala pelo grip circular (ciano) e confira o corte
    em rampa do topo das alas (`cortar`). Troque Tipo para Triplo e veja os três furos e conectores.
17. **Auditoria:** `HY_AUDITAR` num desenho com dispositivos da versão anterior: acusa esquema 1 e
    migra com Corrigir.
18. **Rede em cadeia:** `HY_REDE` → PV retangular → rede "Teste", Ø 0,40, 0,5 %, com superfície.
    Clique 4 pontos: cada PV nasce com estrutura nula e o tubo desde o anterior, com furos dos dois
    lados; a elevação de fundo desce 0,5 % por trecho. Desfazer remove o último trecho. Aceite numerar:
    PV-1 a PV-4 no sentido do fluxo.
19. **Seguir o PV:** MOVE de um PV da rede: a estrutura e as pontas dos tubos acompanham; os furos
    se refazem. Num PV com tubo solto (sem estrutura), `HY_PONTAS Ajustar Corrigir` e mova o PV.
20. **Painel Redes:** a rede aparece com dispositivos, trechos, extensão e as setas do fluxo no monitor;
    as abas Dispositivos e Trechos listam ordem, elevações e declividades.
21. **Projeção em perfil:** crie alinhamento, perfil e ProfileView passando pelos PVs. `HY_PROJETAR`
    (Enter = todos) → a vista → Padrão → Ambos → Enter. Cada PV aparece cortado: arestas da face
    cortada, hachura ANSI31, silhueta e eixo, nas camadas HY-PERFIL-*. Edite um PV (H maior): a
    projeção se refaz. Selecione a projeção: o losango do fundo arrasta a elevação de fundo com
    prévia; ao soltar, o PV e a projeção se refazem. Mova um PV para 15 m do eixo: com distância
    máxima 10 m ele some (a lista de Saídas diz por quê).
22. **Estilo de projeção:** no painel, Saídas → estilo Padrão → mude a hachura para AR-CONC, escala
    0,05, ligue "ocultas" → Salvar → Atualizar saídas.
23. **Rótulo e tabela:** `HY_ROTULO` num PV com o modelo padrão; edite o PV: o texto muda; mova o
    PV: o rótulo vai junto. `HY_TABELA` (Enter = todos, Padrao): insira um PV novo e veja a tabela
    ganhar a linha no fim do comando.
24. **Estilo de dispositivo:** `HY_ESTILO Criar` a partir de um PV ("PV prancha"); no painel, deixe
    "degraus" invisível e a camada do concreto C-PV → Salvar e refazer; `HY_ESTILO Aplicar`.

### Limitações atuais e próximos passos

- A categoria `linear` (seção varrida ao longo de alinhamento/perfil, para sarjetas e valetas)
  ainda não tem gerador.
- O grip pede o ponto final, mas ainda não mostra a prévia da geometria durante o arrasto.
- A superfície não é acompanhada automaticamente: rode `HY_SUPERFICIE` de novo.
- **Presets de catálogo:** escolher um código do álbum DNIT IPR 736 preencherá as dimensões, e
  editar uma dimensão manualmente mudará o código para "genérico".
- Nós SWMM de armazenamento (storage) e de descarga (outfall), e exportação dos condutos.
