# Diário de trabalho

Resumo do que foi feito em cada sessão, por máquina, para quem continua em outro computador sem o
histórico da conversa. **Entradas mais recentes no topo.** Ao encerrar uma sessão, acrescente a sua.
O estado consolidado do projeto está no `CLAUDE.md`; aqui fica a cronologia.

Máquinas: **trabalho** (Civil 3D 2026 build 25.1.179, .NET 10) · **PC de casa** (2026.2, .NET 8) ·
**notebook** (2026, .NET 8).

---

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
