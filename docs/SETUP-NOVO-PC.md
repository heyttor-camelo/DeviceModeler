# Como continuar o projeto em outro PC

Passo a passo para instalar o Claude Code e o REA, baixar o projeto e continuar de onde parou.

## 1. Pré-requisitos

Instale, se ainda não tiver:

| Ferramenta | Para quê | Como |
|------------|----------|------|
| **Git** | baixar e enviar o código | `winget install --id Git.Git -e` |
| **.NET SDK 8** (e o 10, se o Civil 3D for .NET 10) | compilar e testar | `winget install Microsoft.DotNet.SDK.8` (e `Microsoft.DotNet.SDK.10`) |
| **Node.js LTS** | Claude Code via npm e o REA (npx) | `winget install OpenJS.NodeJS.LTS` |
| **GitHub CLI** (opcional) | login no GitHub e criação de PRs | `winget install GitHub.cli` |
| **Civil 3D 2026** | testar o plugin | instalação normal da Autodesk |

Depois de instalar, feche e reabra o terminal para o PATH ser atualizado.

## 2. Baixar o projeto

```bash
git clone https://github.com/heyttor-camelo/DeviceModeler.git
```

```bash
cd DeviceModeler
dotnet build DeviceModeler.sln
dotnet test
```

Se o `dotnet test` passar os 114 testes, o ambiente está certo.

Para **enviar** alterações, faça login no GitHub uma vez com `gh auth login`, ou deixe o Git pedir
as credenciais no primeiro `git push`.

## 3. Instalar o Claude Code

Escolha uma das opções:

- **App desktop do Claude (recomendado):** baixe em https://claude.ai/download, entre com a mesma
  conta e abra a aba **Code**. Escolha a pasta `DeviceModeler` como projeto.
- **Terminal (CLI):**
  ```bash
  npm install -g @anthropic-ai/claude-code
  ```
  Depois, dentro da pasta do projeto, rode `claude` e faça login na primeira vez.

Ao abrir o projeto, o Claude lê o [`CLAUDE.md`](../CLAUDE.md) automaticamente: estado do projeto,
decisões, convenções e próximos passos. Não é preciso repetir a conversa anterior.

Uma boa primeira mensagem:

> Leia o CLAUDE.md e o README. Vamos testar a Fase 2 no Civil 3D seguindo o roteiro do README.

## 4. Instalar o REA (engenharia reversa)

O REA é um servidor MCP distribuído pelo npm (`rea-agents`). Ele foi usado para estudar o SOLIDOS.

**Para o Claude Code CLI,** registre o servidor para o seu usuário:

```bash
claude mcp add rea --scope user -- cmd /d /c npx -y rea-agents@6.7.0 mcp
```

**Para o app desktop,** edite `%APPDATA%\Claude\claude_desktop_config.json` e acrescente:

```json
{
  "mcpServers": {
    "rea": {
      "command": "cmd.exe",
      "args": ["/d", "/c", "npx", "-y", "rea-agents@6.7.0", "mcp"]
    }
  }
}
```

Se o arquivo já tiver um `mcpServers`, só acrescente a entrada `"rea"`. Reinicie o app.

Para conferir que funciona:

```bash
npx -y rea-agents@6.7.0 doctor
```

**Skill do REA (opcional):** na máquina antiga existe a skill `reverse-engineer-anything` em
`%USERPROFILE%\.claude\skills\reverse-engineer-anything\`. Copie essa pasta para o mesmo lugar no
PC novo, ou veja como instalá-la em https://github.com/morluto/rea#readme. Ela ensina o Claude a
usar o REA, mas o REA funciona sem ela.

**Descompilar código .NET:** o REA lista tipos e membros, mas não mostra o corpo dos métodos.
Para isso use o ILSpy:

```bash
dotnet tool install -g ilspycmd
```

Exemplo: `ilspycmd -t SOLIDOS.SolRule caminho\SOLIDOS_2025.dll -o C:\temp\dec`. Gere a saída
sempre fora do repositório; o código descompilado é só para consulta.

## 5. Usar o plugin no Civil 3D

**Sem compilar:** copie a pasta inteira `dist\DeviceModeler.bundle` para
`%APPDATA%\Autodesk\ApplicationPlugins\` e abra o Civil 3D 2026.

**Compilando:** rode `dotnet build`, depois `NETLOAD` em `NETLOAD\Modeler.Civil3D.dll`. Para
instalar o bundle com o Civil 3D fechado:
`powershell -ExecutionPolicy Bypass -File tools/Install-Bundle.ps1`.

> **Importante:** `Modeler.Civil3D.dll`, `Modeler.Core.dll` e `YamlDotNet.dll` (mais a pasta
> `catalog\`) precisam estar **na mesma pasta**. Copiar só a DLL principal causa
> `FileNotFoundException: Could not load file or assembly 'Modeler.Core'`.

Depois de carregar, rode `DM_CATALOGO`; deve aparecer `pv_retangular`. Em seguida, siga o
"Roteiro de teste manual" do README.

## 6. O que foi feito até agora

| Fase | Conteúdo | Situação |
|------|----------|----------|
| 1 | Linguagem YAML, avaliador de expressões, dependências, fluxo, árvore CSG, saídas e o PV retangular | pronta, 114 testes |
| 2 | Plugin do Civil 3D: sólidos, Xrecord, janela de parâmetros, grips, ligação com tubos e superfície, quantitativos, SWMM, bundle | compila, **falta testar no Civil 3D** |
| Depois | categoria linear, catálogo DNIT (IPR 736) com presets, prévia nos grips, nós SWMM storage/outfall | planejado |

O histórico completo está em `git log`. O detalhe técnico está no `CLAUDE.md` e no `README.md`.
