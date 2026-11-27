# NOVAIS boost

Otimizador de PC para Windows 11, no terminal, feito para jogadores competitivos e streamers.
Mais FPS, menos input lag e frametime estável. Cada ajuste é simulado antes, tem backup e pode ser desfeito.

## Como usar

Execute `NOVAIS boost.exe` (pede administrador). O menu tem:

| # | Tela | O que faz |
|---|---|---|
| 1 | Diagnóstico completo | Lê todo o hardware e dá uma nota de 0 a 100 com os problemas encontrados |
| 2 | Otimização Universal | Níveis 1 (Seguro), 2 (Competitivo) e 3 (Extremo), ou ajustes individuais |
| 3 | Perfil do meu Hardware | Recomendações e checklist de BIOS para a sua CPU, GPU, RAM e placa-mãe |
| 4 | Jogos & Streaming | Detecta os jogos, dicas por jogo, GPU dedicada e QoS por jogo, codificador do OBS |
| 5 | Process Guardian | Prioridades automáticas durante o jogo, processos inúteis, programas na inicialização |
| 6 | Cronogramas | Tarefas automáticas: reaplicar ajustes, limpeza semanal, SSD, saúde mensal |
| 7 | Benchmark | Carga de DPC/ISR (%) e FPS/1% low com PresentMon, antes × depois |
| 8 | Reverter / Backups | Desfaz tudo o que foi aplicado |
| 9 | Rede & Latência | Ping, jitter, perda, bufferbloat, DNS mais rápido, ajustes da placa de rede |

Linha de comando:

```
NOVAIS boost.exe --diagnostico         diagnóstico somente leitura (sem administrador)
NOVAIS boost.exe --simular [--extremo] mostra o que seria alterado, sem alterar nada
NOVAIS boost.exe --guardian            Process Guardian (usado pela tarefa de logon)
NOVAIS boost.exe --tarefa <id>         reaplicar | limpeza | ssd | saude
NOVAIS boost.exe --versao
```

## Segurança

- Antes de alterar qualquer coisa: ponto de restauração do Windows e backup do valor anterior
  em `C:\ProgramData\NOVAIS boost\backups`.
- Tudo é simulado e mostrado antes de aplicar. Um ajuste que falha no meio é desfeito na hora.
- Não desliga Secure Boot, TPM, assinatura de drivers nem o Defender, e não injeta nada em jogos
  (Vanguard, FACEIT, EAC, BattlEye e Ricochet). O nível 3 desliga a Integridade de memória (HVCI), que a Riot
  pode exigir de algumas contas: com o Valorant instalado, a revisão mostra um aviso antes de aplicar.
- Os níveis só têm ajustes com efeito documentado ou medido. Os experimentais ficam em "Ajustes individuais".
- Logs diários em `C:\ProgramData\NOVAIS boost\logs`.

## Configurações

Arquivo opcional `C:\ProgramData\NOVAIS boost\settings.json` (só administradores podem alterar a pasta):

```json
{ "guardian": { "gamePriority": "aboveNormal" } }
```

| Opção | Valores | Padrão |
|---|---|---|
| `guardian.gamePriority` | `aboveNormal` ou `high` | `aboveNormal` (prioridade "Acima do normal" para o jogo) |

Um arquivo inválido é ignorado (com aviso no log) e o padrão é usado.

## Desenvolvimento

Requer o SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`).

```
dotnet build NovaisBoost.slnx -c Release -warnaserror
dotnet test  NovaisBoost.slnx -c Release
pwsh .\scripts\build.ps1 -Version 1.0.0      # gera artifacts\publish\NOVAIS boost.exe
```

Estrutura:

```
src/NovaisBoost.Core   regras puras e testáveis: motor de ajustes, backups, diagnóstico, perfis,
                       Guardian, benchmark, rede, cronogramas
src/NovaisBoost.App    acesso ao Windows (registro, serviços, WMI, NVAPI, powercfg, schtasks) e telas
data/tweaks/*.json     catálogo de ajustes (validado por tweak.schema.json)
data/profiles.json     perfis de hardware        data/processes.json  base de processos
data/games.json        jogos e dicas
tests/                 testes xUnit (sem tocar no Windows real)
```

Para adicionar um ajuste: crie um JSON em `data/tweaks/` seguindo o schema. O teste
`EachSystemTargetBelongsToASingleTweak` garante que dois ajustes não disputem o mesmo valor.

Todo ajuste declara o campo obrigatório `evidence`, que diz como sabemos que ele funciona:

| Valor | Quando usar | Onde aparece |
|---|---|---|
| `measured` | Ganho provado em benchmark (antes × depois, ver F12) | Níveis e ajustes individuais |
| `documented` | A Microsoft ou o fabricante documenta a opção e o efeito (política, opção das Configurações, painel do driver, palavra-chave NDIS) | Níveis e ajustes individuais |
| `experimental` | Nenhum dos dois, ou na dúvida | Só em "Ajustes individuais", com o aviso "Efeito não comprovado" |

O teste `LevelsOnlyContainProvenTweaks` falha se um nível (ou um perfil de hardware) incluir um ajuste `experimental`.
Ajustes gerados no código (`DeviceTweaks.cs`, `StartupApps`, DNS) também informam a evidência.

`compat.hardware` bloqueia o ajuste quando o PC não atende ao requisito. `compat.antiCheat` (`Vanguard`, `FACEIT`,
`Easy Anti-Cheat`, `BattlEye`, `Ricochet`, `VAC`) não bloqueia: mostra um aviso na revisão quando um jogo com esse
anti-cheat está instalado.

Para renomear ou dividir um ajuste, crie ids novos e não reutilize o antigo. Os backups guardam o estado anterior de
cada valor, então "Reverter tudo" continua funcionando com o id antigo (teste `LegacyMmcssGamingBackupStillReverts`).

Guia de lançamento e checklist de testes: [docs/LANCAMENTO.md](docs/LANCAMENTO.md).
