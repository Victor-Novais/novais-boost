# NOVAIS boost — Plano de Ação Completo

> Otimizador de PC para Windows 11, executado 100% no terminal, para jogadores competitivos (CS2, COD, Fortnite, Valorant…) e streamers.
> Meta: **mais FPS, menos input lag, frametime estável**, com segurança e **prova medida** de cada ganho.

---

## 0. Visão e diferencial (como ser melhor que todos)

O mercado está dividido: cada ferramenta faz **uma parte**. Ninguém junta tudo, mede o resultado e ainda é reversível.

| O que os outros fazem | O que o NOVAIS boost faz |
|---|---|
| Lista fixa de tweaks, igual para qualquer PC | **Motor de perfis por hardware**: detecta CPU/GPU/RAM/placa-mãe/monitor e aplica o que é certo para *aquele* conjunto |
| "Confia em mim, ficou mais rápido" | **Benchmark antes/depois** (FPS médio, 1% low, frametime, latência DPC) com relatório |
| Tweaks irreversíveis ou mal documentados | **Cada tweak tem: detectar → aplicar → reverter**, com snapshot e ponto de restauração |
| Debloat cego que quebra coisas (ex.: remove Game Bar e estraga o 7950X3D) | **Regras de compatibilidade** por hardware, jogo e anti-cheat |
| Otimiza uma vez e esquece | **Cronogramas automáticos** + **Process Guardian** que mantêm o PC sempre no estado ideal |
| Interface genérica | Terminal com identidade visual forte (**NOVAIS** roxo + **boost** branco) |

---

## 1. Referências de mercado (mundiais)

| Ferramenta | Ponto forte | Lacuna que vamos cobrir |
|---|---|---|
| **Hone.gg** | Otimizador gamer pago, 1 clique | Genérico, pouca transparência, sem perfis profundos de hardware |
| **Chris Titus WinUtil** | Debloat + tweaks open source, muito popular | Não é gamer-first, sem benchmark, sem perfis de hardware |
| **Atlas OS / ReviOS / AME** | Windows modificado ao extremo | Exige reinstalar o Windows, quebra updates/anti-cheat em alguns casos |
| **Win11Debloat (Raphire) / Sophia Script** | Debloat e privacidade bem feitos | Só sistema; não mexe em GPU, rede, latência |
| **O&O ShutUp10++** | Privacidade/telemetria granular | Só privacidade |
| **Process Lasso (Bitsum)** | ProBalance, afinidade, planos de energia | Só processos; pago para recursos completos |
| **ISLC / Timer Resolution** | Limpeza da standby list, timer | Ferramentas soltas, sem contexto |
| **MSI Mode Utility** | Interrupções em modo MSI | Manual, sem perfil |
| **NVIDIA Profile Inspector / NVCleanstall / DDU** | Config profunda do driver NVIDIA, driver limpo | Três ferramentas separadas, uso técnico |
| **ExitLag / NoPing** | Rotas de rede para jogos | Pago, foca só em rota |
| **Razer Cortex** | "Game booster" simples | Superficial (só fecha processos) |
| **BleachBit / CCleaner** | Limpeza de arquivos | Sem agendamento inteligente; CCleaner com histórico ruim |
| **LatencyMon / CapFrameX / PresentMon** | Medição de latência e frametime | Só medem, não otimizam |
| **Guias: Calypto, FR33THY, Khorvie Tech** | Conhecimento profundo de latência | Manual, passo a passo |

**Estratégia:** juntar a profundidade dos guias (Calypto/FR33THY) + a automação do WinUtil + o Process Lasso + a medição do PresentMon/LatencyMon, tudo dentro de **um executável**, com perfis por hardware.

---

## 2. Princípios inegociáveis

1. **Segurança primeiro**: ponto de restauração + backup de registro/serviços/plano de energia antes de qualquer alteração. Botão "Reverter tudo".
2. **Tudo reversível**: cada tweak é definido como dado (JSON/YAML) com `detect`, `apply`, `revert`, `risk`, `requires_reboot`, `compat`.
3. **Medido, não achismo**: tweaks só entram no nível padrão se mostrarem ganho real em benchmark. Placebo fica fora.
4. **Anti-cheat safe**: nada que dispare Vanguard, FACEIT, EAC, BattlEye, Ricochet (sem injeção, sem driver não assinado, sem desligar Secure Boot/TPM).
5. **Sem mitos perigosos** (ver seção 11).
6. **Transparência**: o usuário vê exatamente o que cada tweak faz antes de aplicar (modo "dry-run").

---

## 3. Arquitetura técnica

**Stack recomendada:** **C# / .NET 10** + **Spectre.Console** (UI de terminal rica: FIGlet, tabelas, barras de progresso, menus), publicado como **exe único, auto-contido**, com manifesto pedindo admin.

Por quê: acesso nativo a WMI/CIM, Registro, Task Scheduler, Serviços, ETW (para medir frametime/DPC), NVAPI/ADLX via P/Invoke — e o Spectre.Console entrega o visual "premium" no terminal.

```
NOVAIS boost.exe
├── Core
│   ├── HardwareProfiler      (WMI/CIM, SMBIOS, NVAPI, ADLX, DXGI, SetupAPI)
│   ├── TweakEngine           (carrega tweaks/*.json, detect/apply/revert, dry-run)
│   ├── SnapshotManager       (restore point, export de registro, estado dos serviços)
│   ├── ProfileMatcher        (casa hardware detectado → perfis/*.json)
│   └── CompatGuard           (regras de anti-cheat, jogos, hardware)
├── Modules
│   ├── System / CPU / Power / Memory / GPU / Storage / Network
│   ├── Input & Latency / Audio / Services / Privacy / Visual
│   ├── Games  (configs por jogo)   ├── Streaming (OBS/encoder)
│   ├── ProcessGuardian (serviço em background)
│   └── Scheduler (Task Scheduler)
├── Bench
│   ├── PresentMon (frametime/FPS/1% low)  ├── DPC/ISR latency (ETW)
│   └── Report (antes/depois em HTML/Markdown)
├── Data
│   ├── tweaks/*.json   ├── profiles/*.json   ├── bloat/*.json   └── games/*.json
└── UI (Spectre.Console: banner, menus, progresso, relatórios)
```

**Fluxo do usuário:**
`Abrir → Banner → Scan de hardware (10 s) → Diagnóstico (nota 0–100 + problemas) → Escolher nível → Benchmark "antes" (opcional) → Aplicar → Reboot → Benchmark "depois" → Relatório`

---

## 4. Níveis de otimização

| Nível | Para quem | Inclui | Risco |
|---|---|---|---|
| **1 · Seguro** | Qualquer usuário | Debloat leve, energia, limpeza, privacidade, rede básica, GPU básica | Muito baixo |
| **2 · Competitivo** (padrão gamer) | Jogador / streamer | Nível 1 + latência (MSI, interrupções, timer), serviços, GPU avançada, rede avançada, Process Guardian | Baixo |
| **3 · Extremo** | Pro player com PC dedicado | Nível 2 + desligar VBS/HVCI, mais serviços, debloat profundo, desligar recursos de segurança não essenciais (com aviso) | Médio — avisos explícitos |
| **Custom** | Avançado | Escolhe tweak por tweak, com descrição e risco | Variável |

Cada nível mostra a lista antes de aplicar e permite desmarcar itens.

---

## 5. Parte 1 — Otimização UNIVERSAL (serve para qualquer PC)

### 5.1 Sistema / Windows 11
- Game Mode ligado; Game DVR/captura em segundo plano desligada (sem remover Game Bar — ver X3D).
- Otimizações de tela cheia e "Otimizações para jogos em janela" (configurar por jogo).
- HAGS (Agendamento de GPU acelerado por hardware): ligado em RTX 30/40/50 e RX 6000+ (necessário para Frame Generation).
- `Win32PrioritySeparation` ajustado (testar 0x26 vs 0x2A no benchmark).
- `MMCSS` (Multimedia\SystemProfile): `SystemResponsiveness`, perfil "Games" com prioridade de GPU/CPU alta.
- Desligar: Widgets, Copilot, Recall, dicas/sugestões, apps em segundo plano, inicialização rápida (Fast Startup causa problemas de driver/uptime).
- Remover bloatware UWP (Clipchamp, News, Solitaire, etc.) com lista segura.
- Desabilitar tarefas agendadas de telemetria (CEIP, Compatibility Appraiser, etc.).
- Windows Update: pausar drivers automáticos (evita o Windows substituir driver de GPU), adiar updates de recurso.

### 5.2 Processador
- Plano de energia próprio **"NOVAIS boost Ultimate"** (baseado em Ultimate Performance) com: core parking desligado, estado mínimo do processador ajustado, boost agressivo, sem throttling de energia.
- Desligar Power Throttling para jogos.
- Afinidade/prioridade por jogo (via Process Guardian) — com regras por arquitetura (X3D, Intel híbrido).
- Verificar driver de chipset atualizado (AMD Chipset / Intel MEI/DTT).

### 5.3 Energia
- USB Selective Suspend desligado, PCIe Link State Power Management desligado.
- Desligar economia de energia em adaptadores de rede, USB hubs e controladores (Gerenciador de Dispositivos).
- Hibernação: opcional (libera espaço igual à RAM no disco).
- Notebooks: perfil separado "na tomada / bateria".

### 5.4 Memória RAM
- **Verificar XMP/EXPO** (compara velocidade nominal × configurada — o ganho mais ignorado do mercado).
- Limpeza inteligente da *standby list* (estilo ISLC) apenas quando a memória livre cai abaixo de um limite — não limpeza agressiva constante.
- Compressão de memória: avaliar (desligar só com 32 GB+).
- Arquivo de paginação: tamanho gerenciado no SSD mais rápido (nunca desligar).
- SysMain/Superfetch: avaliar por tipo de disco.
- Detectar vazamentos de memória (processo crescendo continuamente) e alertar.

### 5.5 Placa de vídeo (GPU) e VRAM
**NVIDIA** (via NVAPI / perfil de driver):
- Modo de gerenciamento de energia: desempenho máximo preferido (perfil global ou por jogo).
- Modo de baixa latência / orientação para usar **Reflex** quando o jogo suportar.
- Cache de shader: tamanho grande (10 GB/ilimitado) — evita stutter.
- Otimização de threads, qualidade de filtragem de textura "Alto desempenho", V-Sync off no driver + G-Sync configurado corretamente (G-Sync + limite de FPS 3 abaixo do refresh).
- Instalação limpa do driver (debloat estilo NVCleanstall: sem telemetria, sem componentes inúteis) + fluxo DDU em Modo Seguro.

**AMD** (via ADLX): Anti-Lag 2, cache de shader, desligar recursos que adicionam latência, SAM.
**Intel Arc**: ReBAR obrigatório, driver atualizado.

**Geral:**
- Garantir que o jogo usa a **GPU dedicada** (Configurações de Gráficos do Windows por app) — importante quando existe iGPU ativa.
- Verificar **Resizable BAR** ativo.
- MPO (Multi-Plane Overlay): corrigir quando causa flicker/stutter.
- Limpar cache de shader corrompido (DirectX, NVIDIA, AMD) quando o driver muda.
- Monitorar VRAM e alertar quando o jogo está estourando.

### 5.6 Armazenamento
- TRIM ativo, otimização agendada de SSD (nunca desfragmentar SSD).
- NTFS: desligar "último acesso" e nomes 8.3.
- Cache de gravação do disco ligado.
- Verificar saúde (SMART/NVMe), temperatura e espaço livre — alertar < 15%.
- Verificar se o jogo está no SSD mais rápido.

### 5.7 Rede
- Adaptador: desligar Interrupt Moderation (testar), Energy Efficient Ethernet, Green Ethernet, economia de energia; RSS ligado; buffers adequados.
- TCP: autotuning normal, heurísticas off, ajustes de ACK para jogos antigos.
- **Teste e escolha automática do DNS mais rápido** (Cloudflare, Google, Quad9, DNS da operadora).
- **QoS / DSCP** para os executáveis dos jogos (prioriza pacotes do jogo).
- Teste de **bufferbloat**, ping, jitter e perda de pacote para servidores dos jogos.
- Limitar banda do Windows Update / Delivery Optimization (P2P desligado).
- Wi-Fi: recomendar cabo; desligar scan em segundo plano durante o jogo.

### 5.8 Input e latência (o núcleo do "menor input lag")
- **MSI Mode** para GPU, NVMe, USB e rede (quando suportado) + afinidade de interrupções.
- Aceleração do mouse desligada (EnhancePointerPrecision), Raw Input.
- Timer: `GlobalTimerResolutionRequests` (Win11) e resolução de timer adequada.
- Desligar economia de energia dos controladores USB.
- Medir **latência DPC/ISR** (estilo LatencyMon via ETW) e apontar o driver culpado.
- Detectar monitor rodando em 60 Hz quando suporta 144/240/360 Hz (erro comum!).

### 5.9 Áudio
- Desligar realces de áudio, modo exclusivo configurado, frequência adequada; remover dispositivos de áudio inúteis (HDMI de monitor sem caixa).

### 5.10 Serviços
- Perfis de serviços por nível (manual/desligado): Fax, Xbox (só se não usa), Print Spooler (se não tem impressora), Remote Registry, Diagnóstico, Telemetria, Retail Demo, etc.
- **Nunca** desligar serviços críticos (lista de proteção).

### 5.11 Privacidade e telemetria
- Telemetria no mínimo, Advertising ID, rastreamento de atividade, histórico de localização, Cortana/Bing na busca.

### 5.12 Visual
- Efeitos visuais em "melhor desempenho" mantendo fontes suavizadas; transparência e animações off.

### 5.13 Jogos (configs por jogo)
- **CS2**: opções de inicialização, autoexec, video settings competitivos.
- **Fortnite**: `GameUserSettings.ini` (modo Performance, DX12 vs Performance mode por GPU).
- **COD (Warzone/BO)**: arquivo de config, shader preload, upscaler.
- **Valorant, Apex, R6, LoL**: configs competitivas.
- Detectar jogos instalados (Steam, Epic, Battle.net, Riot, EA, Xbox) automaticamente.

### 5.14 Streaming
- OBS: encoder certo por GPU (NVENC AV1/H.264 em RTX 40, AMF em AMD), preset, bitrate pela velocidade de upload medida.
- Prioridade do OBS acima do normal, OBS na GPU dedicada.
- Separar jogo/stream em núcleos (perfis multi-CCD).
- Checklist de dois PCs / placa de captura.

---

## 6. Parte 2 — Perfis por HARDWARE (o grande diferencial)

### 6.1 Detecção automática
| Item | Como detectar |
|---|---|
| CPU (modelo, núcleos, CCDs, X3D, híbrido P/E) | WMI `Win32_Processor` + CPUID + topologia |
| GPU (modelo, VRAM, driver, ReBAR) | DXGI, NVAPI, ADLX, `Win32_VideoController` |
| RAM (capacidade, velocidade nominal × atual, canais, slots) | `Win32_PhysicalMemory`, SMBIOS |
| Placa-mãe e BIOS (modelo, versão, data) | `Win32_BaseBoard`, `Win32_BIOS` |
| Discos (NVMe/SATA, saúde) | `MSFT_PhysicalDisk`, SMART |
| Monitor (resolução, Hz atual × máximo, G-Sync/FreeSync) | EDID / DisplayConfig API |
| Rede (cabo/Wi-Fi, chip Intel/Realtek/Killer) | `Win32_NetworkAdapter` |
| Notebook ou desktop, bateria | `Win32_SystemEnclosure`, `Win32_Battery` |
| Fonte (PSU) | **Não é detectável** → pergunta opcional ao usuário (usada para avisos de PBO/OC) |

### 6.2 Banco de perfis (`profiles/*.json`)
Cada perfil tem regras de correspondência e tweaks específicos. Os perfis se **combinam** (CPU + GPU + RAM + placa + monitor).

**Exemplos:**

- **AMD Ryzen X3D de 1 CCD (7800X3D, 9800X3D)**: plano de energia balanceado AMD ou o nosso, sem afinidade forçada, verificar BIOS/AGESA, EXPO, Curve Optimizer (recomendação, não aplica).
- **AMD X3D de 2 CCDs (7950X3D, 7900X3D, 9950X3D)**: **NÃO remover Xbox Game Bar** e manter o driver *3D V-Cache Performance Optimizer* — são eles que mandam o jogo para o CCD com cache. Process Guardian com afinidade por jogo como reforço.
- **Ryzen 7000/9000 não-X3D (ex.: 7600X)**: PBO / Curve Optimizer (recomendação), EXPO, temperaturas (95 °C é normal no Zen 4).
- **Intel 12ª–14ª ger. (híbrido P/E)**: Thread Director + Win11, jogo em P-cores, **verificar microcode da BIOS (problema de instabilidade 13ª/14ª ger.)**, limites de energia Intel Default.
- **Intel Core Ultra (200S)**: perfil próprio de energia e APO (Application Optimization).
- **NVIDIA RTX 40/50**: Reflex, DLSS/Frame Gen, HAGS ligado, ReBAR.
- **NVIDIA RTX 20/30 / GTX**: perfis com menos VRAM, cache de shader.
- **AMD RX 6000/7000/9000**: Anti-Lag 2, SAM, HYPR-RX desligado em competitivo.
- **CPU com iGPU ativa + GPU dedicada** (seu caso!): garantir jogos/OBS na dedicada; sugerir desligar iGPU na BIOS se não usa (ou usar para encoder/2º monitor).
- **RAM**: 16 GB (perfil conservador, paginação), 32 GB+ (pode desligar compressão), DDR5 sem EXPO → alerta crítico.
- **Placas-mãe**: lista de bloatware do fabricante (Armoury Crate, MSI Center, Gigabyte Control Center, iCUE) com opção de limpar serviços pesados sem perder RGB/fan.
- **Notebook**: MUX switch, modo de desempenho do fabricante, curva de ventoinha.

### 6.3 Checklist de BIOS (software não altera BIOS, mas orienta)
XMP/EXPO, Resizable BAR, Above 4G, PBO/Curve Optimizer, C-States (avaliar), BIOS atualizada, iGPU, fTPM/Secure Boot (manter para anti-cheat).

---

## 7. Processos inúteis — **Process Guardian**

Serviço leve em background (< 15 MB RAM, ~0% CPU):

1. **Base de conhecimento** (`bloat/*.json`) classifica processos: *crítico*, *útil*, *opcional*, *bloat*, *suspeito*.
   Ex.: OneDrive (opcional), Edge em segundo plano, Teams, Copilot, Widgets, Phone Link, updaters de Adobe/Google/OEM, launchers abertos sem necessidade.
2. **Diagnóstico em tempo real**: ranking de quem usa CPU/RAM/disco/rede/GPU de forma inútil.
3. **Modo Jogo automático**: ao detectar o jogo abrindo →
   - suspende/encerra processos da lista do usuário,
   - prioridade alta para o jogo e OBS, baixa para o resto,
   - afinidade por perfil (X3D/híbrido),
   - pausa Windows Update, indexação, OneDrive, Defender scan agendado,
   - limpa standby list se necessário.
   Ao fechar o jogo, tudo volta ao normal.
4. **ProBalance próprio**: rebaixa temporariamente processos que tentam monopolizar CPU durante o jogo.
5. **Inicialização**: lista de apps no startup com nota de impacto e recomendação.
6. **Exclusões do Defender** para pastas de jogos (em vez de desligar o antivírus).

---

## 8. Cronogramas automáticos (Task Scheduler)

| Quando | Tarefa |
|---|---|
| **Diário** (ao ligar, silencioso) | Verificar espaço em disco, temperaturas, processos novos no startup |
| **Diário** | Checar se o Windows reverteu algum tweak (updates costumam reverter) e reaplicar |
| **Sexta-feira** | Limpeza de temporários: `%TEMP%`, Windows\Temp, Prefetch antigo, cache de miniaturas, logs, crash dumps, Lixeira > 30 dias |
| **Sexta-feira** | Limpeza de cache de navegadores (opcional), cache de launchers (Steam/Epic/Battle.net) |
| **Domingo** | Otimização do SSD (TRIM), verificação SMART |
| **Semanal** | Verificar driver novo de GPU/chipset (avisa, não instala sozinho) |
| **Semanal** | Teste de rede (ping/jitter/DNS) e troca de DNS se houver um mais rápido |
| **Mensal** | Limpeza do Windows Update (component store via DISM, de forma suportada), Delivery Optimization cache |
| **Mensal** | `sfc /scannow` + `DISM /RestoreHealth` (integridade do sistema) |
| **Mensal** | Novo ponto de restauração + relatório de saúde do PC |
| **Após update do driver de GPU** | Limpar cache de shader e reaplicar perfil do driver |
| **Após Windows Update grande** | Reaplicar debloat e tweaks revertidos |

Tudo configurável no menu "Cronogramas" (ligar/desligar, mudar dia/hora).

---

## 9. Benchmark e prova de resultado

- **PresentMon** integrado: FPS médio, **1% low, 0,1% low**, frametime, latência de renderização.
- **DPC/ISR latency** via ETW.
- **Teste de RAM/CPU/disco** rápido para detectar gargalo.
- Relatório final: "Antes × Depois", nota de otimização 0–100, lista de tweaks aplicados.
- Base anônima (opt-in, futuro): quais tweaks realmente funcionam em cada hardware → perfis melhores com o tempo (vantagem que nenhum concorrente tem).

---

## 10. Identidade visual (terminal)

- **NOVAIS** em roxo (degradê `#7C3AED → #A855F7 → #C084FC`) + **boost** em branco, fonte FIGlet grossa (**ANSI Shadow**).
- Paleta de apoio: roxo (marca), branco (texto), verde (OK), amarelo (aviso), vermelho (risco), cinza (secundário).
- Layout: banner → card de hardware detectado (igual ao estilo do fastfetch) → nota de saúde → menu.

Protótipo do banner: `banner.ps1` (rode `pwsh .\banner.ps1`).

```
 ███╗   ██╗ ██████╗ ██╗   ██╗ █████╗ ██╗███████╗
 ████╗  ██║██╔═══██╗██║   ██║██╔══██╗██║██╔════╝   (roxo)
 ██╔██╗ ██║██║   ██║██║   ██║███████║██║███████╗
 ██║╚██╗██║██║   ██║╚██╗ ██╔╝██╔══██║██║╚════██║
 ██║ ╚████║╚██████╔╝ ╚████╔╝ ██║  ██║██║███████║
 ╚═╝  ╚═══╝ ╚═════╝   ╚═══╝  ╚═╝  ╚═╝╚═╝╚══════╝
      ██████╗  ██████╗  ██████╗ ███████╗████████╗
      ██╔══██╗██╔═══██╗██╔═══██╗██╔════╝╚══██╔══╝  (branco)
      ██████╔╝██║   ██║██║   ██║███████╗   ██║
      ██╔══██╗██║   ██║██║   ██║╚════██║   ██║
      ██████╔╝╚██████╔╝╚██████╔╝███████║   ██║
      ╚═════╝  ╚═════╝  ╚═════╝ ╚══════╝   ╚═╝

  [1] Diagnóstico completo      [5] Process Guardian
  [2] Otimização Universal      [6] Cronogramas
  [3] Perfil do meu Hardware    [7] Benchmark antes/depois
  [4] Jogos & Streaming         [8] Reverter / Backups
```

---

## 11. O que **NÃO** fazer (mitos e riscos — evita quebrar PCs e anti-cheat)

- ❌ Desligar mitigações Spectre/Meltdown por padrão (risco de segurança; só como opção Extremo com aviso).
- ❌ `bcdedit useplatformclock` / mexer em HPET às cegas (frequentemente piora).
- ❌ "Limpadores de RAM" que esvaziam working sets o tempo todo (causa stutter).
- ❌ Limpadores de registro (zero ganho, risco alto).
- ❌ Desligar o arquivo de paginação.
- ❌ Apagar WinSxS manualmente (usar DISM).
- ❌ Desligar Defender por completo (usar exclusões).
- ❌ Desligar Secure Boot / TPM / assinatura de drivers (quebra Vanguard/FACEIT).
- ❌ Remover Xbox Game Bar em Ryzen X3D de 2 CCDs.
- ❌ Overclock automático (só recomendação).

---

## 12. Roadmap de execução

| Fase | Entregas | Duração estimada |
|---|---|---|
| **F0 — Fundação** | Projeto .NET 10 + Spectre.Console, banner/marca, elevação admin, logger, SnapshotManager (restore point + backup), formato JSON de tweaks | 1–2 semanas |
| **F1 — Diagnóstico** | HardwareProfiler completo, nota de saúde, detecção de problemas (XMP off, 60 Hz, iGPU, disco cheio, driver velho) | 2 semanas |
| **F2 — Universal Nível 1 e 2** | Sistema, energia, CPU, RAM, armazenamento, privacidade, serviços, visual, rede básica, GPU básica — tudo com revert | 3–4 semanas |
| **F3 — Latência & GPU avançada** | MSI mode, interrupções, timer, NVAPI/ADLX, cache de shader, driver limpo, DPC latency | 3 semanas |
| **F4 — Perfis de Hardware** | ProfileMatcher + 20 perfis iniciais (X3D, Zen 4/5, Intel híbrido, RTX 30/40/50, RX 6000–9000, notebooks) + checklist BIOS | 3 semanas |
| **F5 — Process Guardian** | Serviço em background, base de bloat, modo jogo automático, ProBalance | 3 semanas |
| **F6 — Cronogramas** | Integração Task Scheduler + todas as tarefas da seção 8 | 1–2 semanas |
| **F7 — Benchmark** | PresentMon, ETW DPC, relatório antes/depois | 2–3 semanas |
| **F8 — Jogos & Streaming** | Configs CS2/Fortnite/COD/Valorant/etc., detecção de launchers, OBS | 2–3 semanas |
| **F9 — Rede avançada** | DNS benchmark, QoS/DSCP, bufferbloat, testes de rota | 2 semanas |
| **F10 — Lançamento** | Assinatura de código (evita SmartScreen/falso positivo), auto-update, testes em VMs e vários hardwares, docs | 2 semanas |

**MVP (fases F0–F2 + F6)** já entrega um produto utilizável em ~2 meses.

---

## 13. Diagnóstico inicial do SEU PC (pelo print)

Ryzen 5 7600X · RTX 4070 · 32 GB · Windows 11 25H2

- ⚠️ **RAM em 98% de uso** → algo está consumindo memória demais (primeiro caso de teste do Process Guardian).
- ⚠️ **Disco em 88%** → abaixo de 15% livre; afeta desempenho do SSD e do Windows Update.
- ⚠️ **iGPU Radeon ativa** junto com a RTX 4070 → garantir jogos/OBS na RTX; avaliar desligar na BIOS.
- ✅ Perfil sugerido: *Zen 4 não-X3D + RTX 40 + DDR5 32 GB* → verificar EXPO, ReBAR, HAGS on, Reflex, NVENC AV1 para stream.

---

## Próximo passo

Começar a **F0**: criar o projeto .NET 10 com o banner, a verificação de admin, o sistema de backup/restore e o formato de tweak em JSON — a base de onde todo o resto cresce.
