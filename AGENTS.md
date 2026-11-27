# AGENTS.md — NOVAIS boost

Regras permanentes para qualquer agente que trabalhe neste repositório.

## Projeto
Otimizador de PC para Windows 11, executado no terminal, para jogadores competitivos e streamers.
O plano completo está em `PLANO_DE_ACAO.md`. Leia-o antes de qualquer tarefa e siga o roadmap fase por fase.

## Stack
- C# / .NET 10 (LTS), app de console, `win-x64`, publicado como exe único e auto-contido.
- UI: Spectre.Console. Testes: xUnit.
- Manifesto `asInvoker`: os modos somente leitura rodam sem UAC; o menu e as tarefas pedem elevação no código.
- Dados (embutidos no exe pelo `NovaisBoost.Core.csproj`): `data/tweaks/*.json` (catálogo + schema), `data/repairs/*.json`
  (Modo Reparar), `data/games/*.json` (presets por jogo), `data/profiles.json`, `data/processes.json`, `data/games.json` e as
  tabelas de apoio `critical-services.json`, `memory-slots.json`, `microcode.json`, `optimizers.json`.
- Build de release: `pwsh .\scripts\build.ps1 -Version X.Y.Z`. Checklist de testes: `docs/LANCAMENTO.md`.
- Interface gráfica: `src/NovaisBoost.Gui` (Avalonia, só Win32). Textos novos de interface vão para `data/i18n/*.json` via `Strings.T`.
- Testes: `tests/NovaisBoost.Tests` (Core, roda em qualquer SO) e `tests/NovaisBoost.App.Tests` (telas/adaptadores; os que usam
  APIs do Windows usam `[WindowsFact]`). Versão única em `Directory.Build.props`.
- Código, nomes e comentários em inglês. Textos exibidos ao usuário em português do Brasil.

## Regras de segurança (inegociáveis)
1. Nenhuma alteração no sistema sem backup antes (ponto de restauração + export do que será alterado).
2. Todo ajuste ("tweak") é declarado como dado em `data/tweaks/*.json` e implementa: `detect`, `apply`, `revert`.
3. Um tweak sem `revert` testado não pode ser mergeado.
4. O modo padrão é **dry-run**: mostra o que faria, sem alterar nada. Aplicar exige confirmação explícita.
5. Proibido: desligar Secure Boot, TPM, assinatura de drivers ou o Defender; limpadores de registro; desligar o arquivo de paginação; apagar WinSxS manualmente; overclock automático; remover a Xbox Game Bar em Ryzen X3D de 2 CCDs.
6. Nada que possa acionar anti-cheats (Vanguard, FACEIT, EAC, BattlEye, Ricochet): sem injeção, sem drivers não assinados.
7. Nunca executar tweaks reais na máquina de desenvolvimento durante testes. Use abstrações (interfaces para registro, serviços, processos) e fakes nos testes.

## Qualidade
- `dotnet build` sem warnings e `dotnet test` passando antes de concluir qualquer tarefa.
- Toda ação é registrada em log (`%ProgramData%\NOVAIS boost\logs`).
- Ao final de cada tarefa, informe: o que foi feito, como testar e o que ficou pendente.

## Marca
- **NOVAIS** em roxo (degradê `#7C3AED → #A855F7 → #C084FC`), **boost** em branco, fonte grossa estilo ANSI Shadow.
- Referência visual: `banner.ps1`.
