# Diário de trabalho

Resumo do que foi feito em cada sessão, por máquina, para quem continua em outro computador sem o
histórico da conversa. **Entradas mais recentes no topo.** Ao encerrar uma sessão, acrescente a sua.
O estado consolidado do projeto está no `CLAUDE.md`; aqui fica a cronologia.

Máquinas: **trabalho** (Civil 3D 2026 build 25.1.179, .NET 10) · **PC de casa** (2026.2, .NET 8) ·
**notebook** (2026, .NET 8).

---

## 2026-10-27 — notebook (Inventor/IPE: estruturas com pontos de conexão)

- Instalados no notebook o Inventor Professional 2026.4 e o add-in Infrastructure Part Shape Utilities.
- O trabalho foi no **Civil3D2026Plugin** (não neste repositório). O método está em
  `docs/CATALOGO-PONTOS-CONEXAO.md` e os scripts em `tools/inventor/` (`New-BldcShape.ps1`,
  `ipe-catalogo/`).
- Descobertas:
  - pontos de conexão no Civil 3D = Reference Points no .ipt;
  - `param.xml` em cm;
  - a estrutura é a montagem moldura + caixa + tampa em `ContentLibrary\Templates\<id>`;
  - famílias do Part Builder não guardam pontos.
- No catálogo **FDX Metric Structures** (capítulo CSBIM-DRENAGEM) entraram `BLDC_IPE` e
  `BLSC_IPE`, com inserção na face da guia. Os backups estão em `C:\Temp\BLDC-IPE\`.
- Pendências:
  - rodar `C:\Temp\BLDC-IPE\install-fdx\Limpar-Catalogos-Teste.ps1` com o Civil fechado (move os
    catálogos FDX-BLD, FDX-IPE e as duas cópias antigas U3929348…/93920319…, e instala `PV_DNIT_IPE`);
  - ver o que ficou "estranho" na modelagem;
  - faixas de tamanho (ColumnRangeList) ainda não testadas;
  - estudar como mudar a cor dos grips (pedido do Heyvid): GRIPCOLOR/GRIPHOT/GRIPHOVER/GRIPCONTOUR do
    AutoCAD, os grips próprios das estruturas do Civil e o GripOverrule do HYDRO.
## 2026-10-21 — trabalho (sessão de origem do projeto)

- Publicação do repositório no GitHub (`heyttor-camelo/DeviceModeler`, público) com a DLL pronta em
  `dist/DeviceModeler.bundle` e o alvo .NET 8/10 automático no csproj.
- `CLAUDE.md`, `docs/SETUP-NOVO-PC.md` (Claude Code, REA, clonar, testar) e `docs/ESTUDO-SOLIDOS.md`
  (método e tabela SOLIDOS × DeviceModeler).
- Decisão do dia: o produto principal passa a ser o módulo **HYDRO no Civil3D2026Plugin**, que usa o
  motor do Pipe Network. Este repositório segue como a abordagem de geometria própria (YAML → CSG →
  Solid3d). Outras sessões, no mesmo dia, renomearam-no para HYDRO (F0–F4) — ver `CLAUDE.md`.
- Catálogos de peças CSBIM/Country Kit: trabalho feito no Civil3D2026Plugin (`tools/*.ps1`); ver o
  `docs/DIARIO.md` de lá.

## 2026-10-20 — trabalho

- Fase 1 (Core sem AutoCAD: linguagem YAML, expressões, dependências, fluxo, CSG, saídas, 114 testes)
  e Fase 2 (plugin: Solid3dBuilder, Xrecord, PropertyGrid, grips, tubos, superfície, SWMM, bundle).
- Nenhuma das duas foi testada dentro do Civil 3D.
