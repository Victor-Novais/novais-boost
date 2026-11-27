# NOVAIS boost

Otimizador de PC para Windows 11, no terminal, feito para jogadores competitivos e streamers.
Mais FPS, menos input lag e frametime estável. Cada ajuste é simulado antes, tem backup e pode ser desfeito.

## Como usar

Execute `NOVAIS boost.exe` (pede administrador). O menu tem:

| # | Tela | O que faz |
|---|---|---|
| 1 | Diagnóstico completo | Lê o hardware, o log de eventos e os sensores e dá uma nota de 0 a 100 com os problemas encontrados |
| 2 | Otimização Universal | Níveis 1 (Seguro), 2 (Competitivo) e 3 (Extremo), ou ajustes individuais |
| 3 | Perfil do meu Hardware | Recomendações e checklist de BIOS para a sua CPU, GPU, RAM e placa-mãe |
| 4 | Jogos & Streaming | Detecta os jogos, preset competitivo nos arquivos do jogo, dicas, GPU dedicada e QoS por jogo, codificador do OBS |
| 5 | Process Guardian | Prioridades automáticas durante o jogo, processos inúteis, programas na inicialização |
| 6 | Cronogramas | Tarefas automáticas: reaplicar ajustes, limpeza semanal, SSD, saúde mensal, aviso de driver, teste de rede |
| 7 | Benchmark | Provar ajustes (teste A/B com estatística e relatório HTML), latência de DPC/ISR por driver (µs), FPS/1% low com PresentMon |
| 8 | Reverter / Backups | Desfaz tudo o que foi aplicado |
| 9 | Rede & Latência | Ping, jitter, perda, bufferbloat, DNS mais rápido, ajustes da placa de rede |
| 10 | Modo Reparar | Acha e desfaz o que "pacotes de FPS" e outros otimizadores deixaram alterado |

Linha de comando:

```
NOVAIS boost.exe --diagnostico         diagnóstico somente leitura (sem administrador)
NOVAIS boost.exe --simular [--extremo] mostra o que seria alterado, sem alterar nada
NOVAIS boost.exe --reparar             procura alterações de outros otimizadores, sem alterar nada
NOVAIS boost.exe --guardian            Process Guardian (usado pela tarefa de logon)
NOVAIS boost.exe --tarefa <id>         reaplicar | limpeza | ssd | saude | drivers | rede
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

## Diagnóstico profundo

Além de RAM, disco, monitor, energia e rede, o diagnóstico procura os problemas que mais custam FPS e estabilidade:

| Verificação | De onde vem o dado |
|---|---|
| GPU em menos linhas PCIe do que suporta (slot errado, riser) | Propriedades PCI do dispositivo (`DEVPKEY_PciDevice_*`) |
| Pentes de RAM no mesmo canal | Nome do slot (`Win32_PhysicalMemory`) + `data/memory-slots.json` |
| Erros de hardware WHEA, telas azuis, desligamentos inesperados, driver de vídeo reiniciado (TDR) | Log do Sistema dos últimos 30 dias e `%SystemRoot%\Minidump` |
| Microcódigo desatualizado nos Intel 13ª/14ª geração (mínimo 0x12B, recomendado 0x12F) | Registro do processador + `data/microcode.json` |
| GPU limitada por temperatura ou energia | NVML (driver NVIDIA) |
| CPU limitada sob carga (teste opcional de 20 s no fim do diagnóstico) | Contadores `% Performance Limit` e `% Processor Performance` |
| Jogo ou arquivo de paginação em HD, paginação desligada, TRIM desligado | Pastas dos jogos, `Win32_PageFileUsage`, `fsutil` |
| Monitor por HDMI preso em 60/120 Hz, VRR do Windows desligado | `QueryDisplayConfig`, configurações de gráficos do DirectX |
| Overlays demais abertos, outro otimizador instalado | Processos, programas instalados + `data/optimizers.json` |

**Nota v2:** cada categoria (estabilidade, memória, vídeo, armazenamento, monitor, energia, Windows, rede, programas) tem
um teto de pontos que pode tirar. Dez avisos de disco não zeram a nota, e a estabilidade é a que pesa mais. O relatório
JSON (`reports\diagnostico-*.json`) traz a fonte de cada dado e de cada problema.

## Configuração dentro dos jogos

Em Jogos & Streaming, o NOVAIS boost aplica um preset competitivo direto no arquivo de configuração do jogo
(`data/games/<id>.json`), com a mesma revisão, backup e reversão dos outros ajustes:

| Jogo | Arquivo | O que muda | Evidência |
|---|---|---|---|
| Counter-Strike 2 | `Steam\userdata\<conta>\730\local\cfg\cs2_video.txt` (uma vez por conta Steam que já jogou) | V-Sync desligado, MSAA 2x, shader baixo | `experimental` (chaves vistas em arquivos de jogadores; a Valve não documenta) |
| Fortnite | `%LOCALAPPDATA%\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini` | V-Sync desligado e limite de FPS para VRR | `documented` (UGameUserSettings do Unreal Engine) |

- **Regras:** recusa aplicar ou reverter com o jogo aberto (ele regrava o arquivo ao fechar), não mexe em arquivo
  somente leitura e falha fechada se a chave não existir ou aparecer repetida. Nunca cria arquivo nem chave.
- **Backup:** o arquivo inteiro vai para o backup antes da primeira mudança. Ao reverter, ele volta byte a byte; se o jogo
  regravou o arquivo depois, só as chaves do preset voltam, e o resto do que o jogo salvou fica.
- **Limite de FPS:** com G-Sync/FreeSync e Reflex, `Hz − Hz²/3600` (144 Hz → 138, 240 Hz → 224); com VRR sem Reflex,
  3 abaixo do Hz; sem VRR, o limite do jogo não é alterado (`FrameCap.For`).
- **Fora por enquanto:** VALORANT (as configurações ficam no servidor da Riot), Call of Duty (o nome e o formato do `.cst`
  mudam a cada título), Apex, Rainbow Six Siege e Overwatch 2 (chaves ainda não confirmadas). Esses jogos mostram só as dicas.
- Opções de inicialização do Steam aparecem prontas para copiar (o `localconfig.vdf` não é editado).

## Process Guardian v2

- Acorda por evento quando um processo abre (`Win32_ProcessStartTrace`), sem varrer a lista de processos com o PC parado.
- Durante o jogo: prioridades, modo de eficiência e CPU Sets do segundo plano, plano de energia (opcional), lista standby
  (opcional) e alertas de RAM acima de 90%, VRAM acima de 95% (NVIDIA) e processo vazando memória (crescendo por 30 min).
  Os alertas aparecem na janela do Guardian e no log. Tudo volta ao normal quando o jogo fecha.
- O processo do jogo só é alterado se não tiver anti-cheat de kernel, ou com `guardian.touchProtectedGames`.
- Exclusões do Defender para as pastas dos jogos: opcional, no menu do Guardian, reversível (o Defender continua ligado).

## GPU por jogo

- **NVIDIA:** em Jogos & Streaming, cada jogo detectado pode ter o modo de energia em "Preferir desempenho máximo" no
  próprio perfil do driver (DRS), sem deixar a GPU em clock alto o tempo todo. Se o jogo não tiver perfil, um perfil
  "NOVAIS boost - jogo.exe" é criado; ao reverter, ele é apagado. Perfis predefinidos da NVIDIA nunca são apagados: só a
  configuração volta. Opcional: limite de FPS do driver para G-Sync (`experimental`). Os IDs foram conferidos no driver
  instalado (`NvAPI_DRS_GetSettingNameFromId`); o Low Latency Mode continua fora (use Reflex no jogo).
- **AMD:** a estrutura para ADLX está pronta (Anti-Lag ligado; Chill, Boost, Enhanced Sync e HYPR-RX desligados), mas a
  integração com o driver ainda não está nesta versão; a tela mostra as recomendações para fazer no AMD Software.
- **Intel Arc:** leitura via IGCL ainda não implementada.

## Entrada, áudio, rede e armazenamento

| Onde | O quê |
|---|---|
| Benchmark → 8 | **Teste de polling do mouse** (Raw Input numa janela invisível, 5 s): Hz medido, configuração provável e estabilidade. Avisa quando 4.000/8.000 Hz pesam numa CPU com menos de 8 núcleos |
| Diagnóstico | **Mapa USB**: mouse no mesmo controlador que webcam ou placa de captura. **SMART/NVMe**: temperatura, desgaste, erros de leitura e horas ligado (`MSFT_StorageReliabilityCounter`), cache de gravação. **Placa de rede**: RSS desligado e poucos buffers. **Afinidade de interrupções** forçada por outro otimizador. **Jogo em SSD SATA** com um NVMe disponível. BypassIO no cartão de hardware |
| Nível 1 | **Otimizações para jogos em janela** (`SwapEffectUpgradeEnable` dentro de `DirectXUserGlobalSettings`, sem apagar VRR/Auto HDR) |
| Nível 2 | **Áudio sem melhorias** nos dispositivos de reprodução ativos |
| Individual | **MPO desligado** (`experimental`, só para flicker; pode não ter efeito no 24H2/25H2). **Saída de áudio HDMI/DisplayPort** desativada, uma por placa (`experimental`) |
| Rede → 4 | **Ping até os servidores dos jogos**: relays oficiais da Valve (CS2, Dota 2) pela configuração pública do Steam, e a região da AWS para Fortnite, Apex e PUBG (`data/servers.json`) |
| Guardian | **Varredura de Wi-Fi em segundo plano pausada** durante o jogo (`guardian.wifiScanOff`, padrão ligado) e **OBS nos núcleos de segundo plano** (`guardian.obsCpuSets`, opt-in) |
| Jogos & Streaming | **Bitrate sugerido** pelo upload medido (Twitch, Kick, YouTube) e checklist de dois PCs / placa de captura |

Tipos de ação da F19: `directxSetting` (uma entrada de `DirectXUserGlobalSettings`) e `device` (ligar/desligar um dispositivo pelo `pnputil`).
Desligar o Nagle (`TcpAckFrequency`/`TCPNoDelay`) continua fora: não ajuda jogos modernos.

## Debloat

| Item | O que faz | Nível | Como voltar |
|---|---|---|---|
| Apps da Store (`data/bloat/appx.json`) | Remove só da sua conta: Clipchamp, Notícias, Clima, Solitaire, Obter Ajuda, Hub de Comentários, To Do, Power Automate, Family Safety (um ajuste por app, só os instalados) | 2 | Reverter reinstala pela Microsoft Store (`winget --source msstore`, precisa de internet). Se falhar, o backup fica guardado para tentar de novo |
| Edge em segundo plano | Políticas `BackgroundModeEnabled=0` e `StartupBoostEnabled=0` | 1 | Reverter |
| OneDrive e Teams | Só saem da inicialização (igual ao Gerenciador de Tarefas), nada é desinstalado | 1 | Reverter |
| Serviços de fabricantes (`data/bloat/oem.json`) | Atualização/diagnóstico de ASUS, MSI, Gigabyte, Corsair e Razer em **Manual** (nunca Desativado). Iluminação, ventoinhas e o serviço principal de cada app ficam fora | Individual (`experimental`) | Reverter |
| Serviços Xbox | Manual (o padrão do Windows), **nunca** em Ryzen X3D de 2 CCDs | 3 | Reverter |
| Efeitos visuais | "Melhor desempenho" mantendo fontes suavizadas e miniaturas | 1 | Reverter |
| Adiar atualizações de recurso | 90 dias (Windows Update para Empresas; não funciona no Home). Segurança continua chegando | 2 | Reverter |
| Hibernação | Desliga e libera o `hiberfil.sys` (mostra o tamanho). Só em desktop | Individual (`experimental`) | Reverter |
| Compressão de memória | Desliga com 32 GB+ (exige SysMain ligado) | Individual (`experimental`) | Reverter |

Nunca são removidos: Microsoft Store, Instalador de Aplicativo (winget), Xbox/Game Bar, Calculadora, Fotos, Terminal,
Segurança do Windows e as extensões de vídeo/imagem (`Debloat.NeverRemove`).

## Modo Reparar

Muitos jogadores já rodaram "pacotes de FPS" e otimizadores agressivos que deixam o Windows pior. O Modo Reparar
(menu 10, ou `--reparar` só para ver) procura e volta ao padrão do Windows:

| Reparo | Detecta | Restaura |
|---|---|---|
| Relógio de plataforma | `useplatformclock`, `useplatformtick`, `disabledynamictick`, `tscsyncpolicy` no BCD | Remove os elementos |
| Paginação | Nenhum arquivo de paginação | Gerenciado pelo sistema (um tamanho manual seu é mantido) |
| Serviços essenciais | WMI, BITS, Windows Update, áudio, DNS, DHCP, criptografia, agendador, log de eventos, firewall... **desativados** (`data/critical-services.json`) | Tipo de início padrão |
| Mitigações da CPU | `FeatureSettingsOverride` desligando Spectre v2/Meltdown | Remove o valor (valores que só ligam proteções extras ficam) |
| Defender e Windows Update | Políticas `DisableAntiSpyware`, `DisableRealtimeMonitoring`, `NoAutoUpdate`, `DisableWindowsUpdateAccess`... | Remove as políticas |
| Latência | `Win32PrioritySeparation` > 0x3F, `SystemResponsiveness` > 100, `LargeSystemCache`/`DisablePagingExecutive` ligados | Padrão do Windows |
| Pilha TCP | Ajuste automático da janela diferente de "normal", RSS desligado | `netsh int tcp set global` com o padrão |

A varredura é somente leitura e roda sem administrador (o BCD precisa de administrador para ser lido). Os reparos passam
pela mesma simulação, ponto de restauração e backup dos ajustes, e podem ser desfeitos em Reverter / Backups. Eles nunca
entram nos níveis, só agem em valores estragados e não desfazem nenhum ajuste do próprio NOVAIS boost (teste
`RepairsNeverUndoANovaisBoostTweak`). O diagnóstico avisa: "Encontramos N alterações de outros otimizadores".

Tipos de ação da F18: `appxPackage` (PackageManager, revert pelo winget) e `systemFeature` (hibernação e compressão de memória).
Requisitos novos em `compat.hardware`: `ram-32gb` e `not-home`.

Novos tipos de ação: `bcd` (bcdedit), `pagefile` (WMI) e `tcpGlobal` (netsh), além de `acceptRange` e
`acceptIfBitsClear` no registro e `accept` nos serviços, para que um reparo só aja em valores fora do padrão.

## Benchmark e prova de resultado

- **Provar ajustes** (menu 7 → 1): de 3 a 5 rodadas de 60 s **antes**, aplicar **um grupo** de ajustes (uma categoria:
  Energia, CPU, Rede...), reiniciar se o grupo pedir e repetir as rodadas **depois**. Os primeiros 10 s de cada rodada
  são descartados. O teste fica salvo entre um passo e outro, então dá para reiniciar no meio.
- **Veredito**: média, desvio padrão e intervalo de confiança de 95% por bootstrap (10.000 reamostragens das rodadas)
  para FPS médio, 1% low, 0,1% low e frametime p99. "Ganho real" ou "Piorou" só quando o intervalo inteiro fica de um
  lado e a mudança passa de 1%; senão, "Dentro do ruído". Uma piora no FPS médio ou no 1% low vence qualquer ganho.
- O resultado vira **evidência local** (`C:\ProgramData\NOVAIS boost\evidence.json`): um ganho real torna o ajuste
  `measured` neste PC (ele pode entrar nos níveis aqui), e uma piora tira o ajuste dos níveis aqui. Um grupo que deu
  "Dentro do ruído" ou "Piorou" pode ser revertido sozinho no fim do teste.
- **Relatório** `reports\benchmark-AAAAMMDD-HHMMSS.html`, auto-contido (sem internet, sem scripts), com a marca,
  o hardware, antes × depois com intervalos, gráfico de frametime e os ajustes aplicados.
- **Gargalo**: com os tempos de CPU e GPU do PresentMon, cada captura diz se o jogo está limitado pela GPU,
  pela CPU/sistema ou por um limite de FPS.
- **Latência de DPC/ISR por driver** (menu 7 → 2): gravação ETW de 20 s, somente leitura, com o top 10 de drivers pela
  chamada mais longa e pelo tempo total. Alerta acima de 500 µs, crítico acima de 1.000 µs, com a dica de cada driver.
- O **PresentMon 2.6.0** (Intel, MIT) vem na pasta `tools\` do pacote. O app confere o SHA256 antes de executá-lo.
  Licenças em [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Manutenção

| Tarefa | Quando | O que faz |
|---|---|---|
| Limpeza semanal | sexta | Temporários, despejos de erro, logs, cache de miniaturas, itens da **Lixeira com mais de 30 dias** (pela data em que foram apagados), cache da Otimização de Entrega. Com opt-in: cache dos navegadores (Chrome, Edge, Brave) e dos launchers (Steam, Battle.net), só com o programa fechado. A pasta Prefetch nunca é apagada |
| Saúde mensal | dia 1 | Diagnóstico salvo, `DISM /RestoreHealth`, `sfc /scannow`, limpeza de componentes e ponto de restauração |
| Aviso de driver | quarta | Notificação quando o driver da placa de vídeo passou de 6 meses (não instala nada) |
| Teste de rede | quinta | Ping/jitter no log. Se você já aplicou "DNS mais rápido" e outro ficou 10 ms mais rápido, troca para ele (desfaz em Reverter tudo) |

Os alertas do Guardian, o aviso de driver e a troca de DNS aparecem como **notificação do Windows**.
Benchmark → 7 faz o **teste rápido de gargalo** (CPU, banda da memória e leitura do disco, ~30 s) e avisa quando a memória
ou o disco rendem menos da metade do esperado para a classe deles (`data/baselines.json`). Em Perfil do meu Hardware dá
para informar a **fonte (PSU)**: com uma fonte abaixo do recomendado para a placa de vídeo, o diagnóstico avisa.

## Configurações

Arquivo opcional `C:\ProgramData\NOVAIS boost\settings.json` (só administradores podem alterar a pasta):

```json
{ "guardian": { "gamePriority": "aboveNormal" } }
```

| Opção | Valores | Padrão |
|---|---|---|
| `guardian.gamePriority` | `aboveNormal` ou `high` | `aboveNormal` (prioridade "Acima do normal" para o jogo) |
| `guardian.touchProtectedGames` | `true`/`false` | `false`: jogos com anti-cheat de kernel (Vanguard, EAC, BattlEye, Ricochet...) nunca têm o processo alterado |
| `guardian.efficiencyMode` | `true`/`false` | `true`: processos pesados em segundo plano vão para o modo de eficiência (EcoQoS) durante o jogo |
| `guardian.backgroundCpuSets` | `true`/`false` | `true`: em Intel híbrido, o segundo plano vai para os E-cores; em X3D de 2 CCDs, para o CCD sem V-Cache |
| `guardian.gameCpuSets` | `true`/`false` | `false` (experimental): fixa o jogo nos P-cores / CCD com V-Cache; nunca um jogo protegido |
| `guardian.powerPlanDuringGame` | `true`/`false` | `false`: plano NOVAIS boost só com o jogo aberto (nunca em X3D de 2 CCDs) |
| `guardian.standbyPurge` | `true`/`false` | `false`: limpa a lista standby só com menos de 1 GB livre e mais de 2 GB em standby, no máximo a cada 5 min |
| `guardian.lower` / `efficiency` / `ignore` | lista de processos | vazias: baixar prioridade, modo de eficiência ou nunca mexer |
| `guardian.wifiScanOff` | `true`/`false` | `true`: pausa a varredura de redes Wi-Fi em segundo plano enquanto o jogo roda |
| `guardian.obsCpuSets` | `true`/`false` | `false`: OBS/Streamlabs nos E-cores / CCD sem V-Cache durante o jogo |
| `cleanup.browsers` / `cleanup.launchers` | `true`/`false` | `false`: limpar o cache dos navegadores / launchers na limpeza semanal |
| `pc.psuWatts` / `pc.psuRating` | 200 a 3000 / `bronze`, `gold`... | não informado: potência e certificação da fonte, usadas nos avisos |

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
| `measured` | Ganho provado em benchmark (antes × depois). No PC do usuário, um "Ganho real" em Provar ajustes também conta | Níveis e ajustes individuais |
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
