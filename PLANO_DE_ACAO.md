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

F0 a F10 estão concluídas (ver status abaixo). O trabalho continua na **Parte 2 do roadmap (F11 a F22)**, no fim deste arquivo, começando pela **F11 — Correções de precisão**.

---

## Status da implementação (26/11/2026)

| Fase | Status | Observações |
|---|---|---|
| F0 Fundação | ✅ | Motor de ajustes com simulação, backup, ponto de restauração e reversão |
| F1 Diagnóstico | ✅ | 24 regras, nota de saúde, relatório JSON |
| F2 Universal (níveis 1 e 2) | ✅ | Registro, serviços, tarefas, plano de energia próprio |
| F3 Latência & GPU | ✅ | MSI mode, timer, NVIDIA via NVAPI (modo de energia, filtragem), medição de DPC/ISR |
| F4 Perfis de hardware | ✅ | 20 perfis (X3D 1/2 CCDs, Zen 3/4/5, Intel 12-14 e Ultra, RTX/GTX, Radeon, Arc, RAM, notebook, placas) + checklist de BIOS |
| F5 Process Guardian | ✅ | Prioridades automáticas durante o jogo, ProBalance, processos inúteis, inicialização |
| F6 Cronogramas | ✅ | 5 tarefas no Agendador do Windows, limpeza segura, reaplicação após updates |
| F7 Benchmark | ✅ | PresentMon (FPS, 1%/0,1% low, p99, travadas) + latência, comparação antes × depois |
| F8 Jogos & Streaming | ✅ | 12 jogos + OBS, GPU dedicada e QoS por jogo, codificador por GPU |
| F9 Rede avançada | ✅ | Ping/jitter/perda, bufferbloat, DNS mais rápido (aplicável e reversível), placa de rede |
| F10 Lançamento | ✅ parcial | Build/assinatura/hash prontos; falta o certificado e a hospedagem para auto-update |

Fora do escopo por segurança: overclock, BIOS, mitigações de CPU, Low Latency Mode/cache de shader NVIDIA,
instalação limpa automática de driver. Detalhes em `docs/LANCAMENTO.md`.

---
---

# Parte 2 do roadmap — de "seguro" para "o melhor" (F11 a F22)

> Origem: raio-X do projeto de 27/11/2026. Conclusão: a arquitetura de segurança já lidera o mercado,
> mas a cobertura é rasa. O caminho para ser o melhor é **diagnóstico profundo + reparo + configuração
> dos jogos + prova estatística**, e não mais ajustes de registro.

## P2.0 Regras que valem para todas as fases novas

1. Tudo o que está em `AGENTS.md` continua valendo: backup antes, dry-run por padrão, `detect/apply/revert`
   testado, nada que acione anti-cheat, fakes nos testes, build sem warnings, log de toda ação.
2. **Todo tipo novo de ação** (BCD, arquivo de configuração, pacote Appx, perfil NVIDIA por app etc.) entra assim:
   1. `record` em `src/NovaisBoost.Core/TweakDefinition.cs` + `SettingState` em `SystemInterfaces.cs`;
   2. interface no Core (`IBcdStore`, `IConfigFiles`...) + implementação real no App + fake nos testes;
   3. definição no `tweak.schema.json`;
   4. testes de detect/apply/revert, de falha no meio do lote e de reversão após reinício interrompido.
3. **Evidência obrigatória.** Novo campo `evidence` em cada ajuste:
   `measured` (provado em benchmark), `documented` (documentação oficial da Microsoft/fabricante) ou
   `experimental`. Os níveis 1 e 2 só aceitam `measured` ou `documented`. `experimental` aparece só em
   Custom, com o aviso "efeito não comprovado". Teste novo: `LevelsOnlyContainProvenTweaks`.
4. Nenhum driver de kernel de terceiros. Em especial, **nunca WinRing0** (está na lista de drivers vulneráveis
   bloqueados pelo Defender). Leitura de sensores só por APIs de fabricante (NVML/NVAPI, ADLX) e contadores do Windows.
5. Cada fase termina com: README e `docs/LANCAMENTO.md` atualizados, itens novos no checklist manual e a linha
   da fase na tabela de status abaixo.

## P2.1 Visão geral e ordem

| Fase | Nome | Depende de | Duração | Impacto |
|---|---|---|---|---|
| F11 | Correções de precisão | — | 1 semana | Credibilidade |
| F12 | Benchmark v2 (prova estatística) | F11 | 3 semanas | **S** |
| F13 | Diagnóstico profundo | F11 | 3 semanas | **S** |
| F14 | Modo Reparar | F13 | 2 semanas | **S** (exclusivo) |
| F15 | Configuração dentro dos jogos | F12 | 3 semanas | **S** |
| F16 | GPU dos três fabricantes | F13 | 3 semanas | A |
| F17 | Guardian v2 | F12, F13 | 3 semanas | A |
| F18 | Debloat completo | F11 | 2 semanas | A |
| F19 | Entrada, áudio, rede e armazenamento | F13 | 3 semanas | A |
| F20 | Confiança e distribuição | — (pode ir em paralelo) | 2–3 semanas | B (bloqueia adoção) |
| F21 | Base de dados anônima (opt-in) | F12, F20 | 3 semanas + backend | B (vantagem única) |
| F22 | Interface gráfica e inglês | F20 | 4–6 semanas | B (alcance) |

**Total:** ~32 a 36 semanas para uma pessoa. Marcos de versão:

- **v1.1** = F11 + F12 + F13 → "o PC é medido e diagnosticado de verdade".
- **v1.2** = F14 + F15 + F20 → "conserta o que outros estragaram, configura o jogo e é assinado".
- **v1.3** = F16 + F17 + F18 + F19 → profundidade técnica.
- **v2.0** = F21 + F22 → produto de massa.

---

## F11 — Correções de precisão (1 semana)

**Objetivo:** tirar do produto o que não tem efeito comprovado e corrigir os riscos encontrados.

| # | Tarefa | Onde | Pronto quando |
|---|---|---|---|
| 11.1 | Adicionar o campo `evidence` (ver P2.0 item 3) ao schema, ao `TweakDefinition` e a todos os 33 JSON | `tweak.schema.json`, `TweakDefinition.cs`, `data/tweaks/*.json` | Teste `LevelsOnlyContainProvenTweaks` passa |
| 11.2 | `priority-separation` → `experimental`, sai do nível 2. No Windows cliente o padrão (2) já equivale a 0x26 | `priority-separation.json` | Só aparece em Custom |
| 11.3 | `mmcss-gaming`: separar em `mmcss-responsiveness` (`SystemResponsiveness`, `NetworkThrottlingIndex`, documentados) e `mmcss-games-task` (`experimental`, só afeta threads registradas na tarefa "Games") | `mmcss-gaming.json` | Dois ajustes; o teste `EachSystemTargetBelongsToASingleTweak` passa |
| 11.4 | Guardian: prioridade padrão do jogo **AboveNormal**. High vira opção em `settings.json` (`guardian.gamePriority`) | `Guardian.cs`, `GuardianService.cs` | Teste da política cobre os dois modos |
| 11.5 | Plano de energia: valores `dc` próprios (estado mínimo 5%, boost "Eficiente agressivo", suspensão seletiva USB ligada na bateria). Em notebook, o `ac` continua igual | `power-plan.json` | Teste confere `dc` ≠ `ac` nos itens de CPU |
| 11.6 | `vbs-off`: corrigir o texto de risco. A Riot pode exigir HVCI de algumas contas (Vanguard "VAN: Restriction"); **verificar a política atual** antes de escrever. Adicionar `compat.antiCheat: ["Vanguard"]` com aviso na revisão | `vbs-off.json`, `Compatibility.cs` | Ao revisar com Valorant instalado, aparece o aviso |
| 11.7 | Renomear "Latência DPC" para **"Carga de DPC/ISR (%)"** em toda a interface até a F12 trazer a latência real | `Views.cs`, `Performance.cs` | Nenhum texto promete latência em µs |
| 11.8 | Revisar `hags-on`: no nível 1 só para RTX 30+/RX 6000+ (`compat.hardware: gpu-hags`) | `hags-on.json` | GTX não recebe o ajuste |

---

## F12 — Benchmark v2: prova estatística (3 semanas)

**Objetivo:** cada promessa vira número com margem de erro. É o que permite ao marketing dizer "+14% no 1% low" com honestidade.

### 12.1 PresentMon embutido
- `scripts/build.ps1` baixa o **PresentMon console** de uma versão fixa do GitHub da Intel, confere o **SHA256 fixado no script**
  e copia para `tools/` dentro do pacote. Licença MIT em `THIRD_PARTY_NOTICES.md`.
- `Benchmark.FindPresentMon` passa a preferir o embutido. A tela não pede mais para o usuário baixar nada.

### 12.2 Mais métricas por quadro
- Ler as colunas do PresentMon 2: `MsBetweenPresents`/`FrameTime`, `MsGPUBusy`, `MsCPUBusy`, `MsPCLatency`
  e `MsClickToPhotonLatency` (quando o jogo tem Reflex).
- **Classificação do gargalo:** se `GPUBusy` ≈ `FrameTime` → limitado pela GPU; senão → limitado pela CPU ou pelo sistema.
  Isso decide quais recomendações mostrar (ajuste de CPU não ajuda jogo limitado pela GPU).
- Gráfico de frametime no terminal (sparkline com Spectre.Console) e contagem de travadas (> 2× a mediana).

### 12.3 Sessão estatística (Core, 100% testável)
- Novo `BenchmarkSession` em `src/NovaisBoost.Core/Performance.cs`:
  - de 3 a 5 rodadas por lado (antes/depois), descartando os primeiros 10 s de cada uma (aquecimento);
  - média, desvio padrão e **intervalo de confiança de 95%** por bootstrap (10.000 reamostragens, semente fixa nos testes);
  - veredito: `GanhoReal`, `DentroDoRuído` ou `Piorou`, com o tamanho do efeito.
- Testes: dados sintéticos com ganho conhecido, só ruído e piora.

### 12.4 Teste A/B por grupo de ajustes
- Fluxo "Provar ajustes": benchmark antes → aplicar **um grupo** (energia, latência, serviços...) → reiniciar se o grupo exigir → benchmark depois → veredito.
- O resultado vira `measured` **local** para aquele PC (guardado em `%ProgramData%\NOVAIS boost\evidence.json`). Na F21, alimenta a base coletiva.
- O usuário pode reverter só o grupo que deu `DentroDoRuído` ou `Piorou`.

### 12.5 Latência real de DPC/ISR por driver (ETW)
- Pacote `Microsoft.Diagnostics.Tracing.TraceEvent`, sessão de kernel com `KernelTraceEventParser.Keywords.DeferedProcedureCalls | Interrupt | ImageLoad`.
- Para cada evento: duração em µs e endereço da rotina → módulo (`.sys`) pelos eventos de ImageLoad.
- Relatório: top 10 drivers por tempo máximo e por total. Alerta acima de 500 µs, crítico acima de 1.000 µs, com a dica por driver
  (`nvlddmkm.sys` → driver de vídeo, `ndis.sys`/`rt640x64.sys` → rede, `storport.sys` → disco, `ACPI.sys` → BIOS/energia).
- Interface `IKernelTrace` com fake que reproduz uma gravação salva (arquivo `.etl` de exemplo só nos testes).

### 12.6 Relatório para compartilhar
- `reports/benchmark-AAAAMMDD.html` auto-contido (CSS e SVG inline, sem internet): cartão da marca NOVAIS, hardware,
  antes × depois com intervalos, gráfico de frametime, ajustes aplicados.
- Botão "Abrir relatório" no terminal.

**Pronto quando:** uma sessão A/B completa com 3 rodadas gera o veredito e o HTML; os testes estatísticos passam; a latência ETW lista drivers numa VM.

---

## F13 — Diagnóstico profundo, estilo detetive (3 semanas)

**Objetivo:** achar os problemas que dão 10–30% de ganho. Cada regra nova é pura (Core) sobre o `HardwareSnapshot`; a coleta fica em `HardwareProbe.cs`.

| # | Regra | Como coletar | Severidade |
|---|---|---|---|
| 13.1 | **GPU com PCIe abaixo da largura máxima** (x8/x4 por slot errado ou riser) | `Win32_PnPEntity.GetDeviceProperties` com `DEVPKEY_PciDevice_CurrentLinkWidth` / `MaxLinkWidth` / `CurrentLinkSpeed` / `MaxLinkSpeed` | Crítico se a largura for menor. A geração só vale sob carga (a GPU reduz a geração parada), então fica como Info |
| 13.2 | **Pentes de RAM no mesmo canal** | `Win32_PhysicalMemory.DeviceLocator`/`BankLabel` (padrões `ChannelA-DIMM1`, `DIMM_A2`...). Heurística por fabricante em `data/memory-slots.json` | Aviso |
| 13.3 | **Instabilidade de hardware** (erros WHEA nos últimos 30 dias) | Log System, provedor `Microsoft-Windows-WHEA-Logger`, IDs 17, 18, 19, 47 | Crítico se ≥ 1 erro fatal, Aviso se corrigidos |
| 13.4 | **Telas azuis e desligamentos** | Eventos `BugCheck 1001`, `Kernel-Power 41`; contagem em `%SystemRoot%\Minidump` | Crítico se > 2 em 30 dias |
| 13.5 | **Travamentos do driver de vídeo (TDR)** | Evento `Display 4101`; `nvlddmkm` 13/14/153 | Aviso |
| 13.6 | **Microcódigo Intel 13ª/14ª ger. desatualizado** (degradação Vmin) | `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\Update Revision`; versão mínima em `data/microcode.json` (0x12B; conferir a mais recente) | Crítico |
| 13.7 | **Temperatura e motivo de limitação da GPU** | NVML (`nvml.dll` do driver): `nvmlDeviceGetTemperature`, `nvmlDeviceGetCurrentClocksEventReasons`. AMD na F16 | Aviso se houver limitação térmica ou de energia sob carga |
| 13.8 | **CPU limitando sob carga** | Carga curta de 20 s (opcional, com aviso) + contadores `Processor Information\% Processor Performance` e `% of Maximum Frequency` | Aviso |
| 13.9 | **Jogo instalado em HD** | Pasta do jogo (`GameLibrary`) → volume → `MSFT_PhysicalDisk.MediaType` | Aviso |
| 13.10 | **Arquivo de paginação desligado ou em HD** | `Win32_PageFileUsage`, `Win32_ComputerSystem.AutomaticManagedPagefile` | Crítico se desligado (alimenta a F14) |
| 13.11 | **Overlays demais ao mesmo tempo** | Processos: Discord overlay, RTSS, NVIDIA overlay, Steam overlay, Game Bar | Info |
| 13.12 | **Monitor por HDMI limitando os Hz** e VRR desligado | `QueryDisplayConfig` (`outputTechnology`); `DirectXUserGlobalSettings` (`VRROptimizeEnable`) | Aviso |
| 13.13 | **TRIM desligado** | `fsutil behavior query DisableDeleteNotify` | Crítico em SSD |
| 13.14 | **Dois ou mais otimizadores instalados** (conflito) | Lista de executáveis e serviços conhecidos em `data/optimizers.json` | Info → leva ao Modo Reparar |
| 13.15 | **Nota de saúde v2** | Peso por categoria e teto por categoria (10 avisos de disco não podem zerar a nota) | — |

**Pronto quando:** cada regra tem pelo menos 2 testes (dispara / não dispara) com snapshots falsos; o relatório JSON inclui as fontes de cada dado.

---

## F14 — Modo Reparar (2 semanas) — exclusivo no mercado

**Objetivo:** desfazer o estrago de "pacotes de FPS" e otimizadores agressivos que muitos jogadores já rodaram.

### 14.1 Tipos novos de ação
- `BcdAction(string Element, string? Expected)`: lê e escreve com `bcdedit /enum {current}` e `/set` / `/deletevalue`.
  Backup = valor anterior. Interface `IBcdStore` + fake.
- `PagefileAction(bool SystemManaged)`: via `Win32_ComputerSystem.AutomaticManagedPagefile` + `Win32_PageFileSetting`.

### 14.2 Catálogo de reparos (`data/repairs/*.json`, mesmo schema com `category: "Reparo"`)
| Reparo | Detecta | Restaura |
|---|---|---|
| Relógio de plataforma forçado | `useplatformclock`, `useplatformtick`, `disabledynamictick`, `tscsyncpolicy` definidos | Remove os elementos (padrão do Windows) |
| Paginação desligada | Sem arquivo de paginação | Gerenciado pelo sistema |
| Serviços críticos desativados | Lista em `data/critical-services.json` com o tipo de início padrão por versão do Windows (Winmgmt, BITS, wuauserv, Audiosrv, AudioEndpointBuilder, Dnscache, CryptSvc, Schedule, EventLog, mpssvc, TrustedInstaller, ...) | Tipo de início padrão |
| Mitigações de CPU desligadas | `FeatureSettingsOverride`/`FeatureSettingsOverrideMask` em `Memory Management` | Remove os valores |
| Defender ou Windows Update desligados por política | `DisableAntiSpyware`, `NoAutoUpdate` e afins em `Policies` | Remove as políticas |
| Valores absurdos de latência | `Win32PrioritySeparation`, `SystemResponsiveness` fora da faixa válida, `LargeSystemCache`, `DisablePagingExecutive` | Padrão do Windows |
| Pilha TCP alterada | `netsh int tcp show global` diferente do padrão (autotuning desabilitado etc.) | `netsh int tcp set global` com o padrão |

### 14.3 Fluxo
`Menu → Reparar → varredura (somente leitura, sem admin) → lista "alterações suspeitas" com a origem provável → simulação → backup → restaurar`.
O resultado aparece no diagnóstico como "Encontramos 7 alterações de outros otimizadores".

**Pronto quando:** cada reparo tem teste de detecção e restauração; uma VM "estragada" por um script de teste (`scripts/vm-damage.ps1`, **só para VM**) volta ao padrão.

---

## F15 — Configuração dentro dos jogos (3 semanas)

**Objetivo:** aplicar o preset competitivo nos arquivos do jogo, que rende mais que qualquer registro.
Os arquivos ficam fora do processo do jogo, então não há conflito com anti-cheat.

### 15.1 Tipo novo de ação: `ConfigFileAction`
- Campos: `path` (com tokens `%LOCALAPPDATA%`, `%USERPROFILE%`, `{steamUser}`, `{gameDir}`), `format` (`ini`, `keyValue`, `cfg`, `json`), `section`, `key`, `value`.
- Backup = **cópia do arquivo inteiro** antes da primeira mudança. Revert restaura a cópia, ou só a chave se o arquivo mudou depois.
- Regras: recusa aplicar com o jogo aberto (o jogo regrava ao fechar); não mexe em arquivo somente leitura sem perguntar; falha fechada se a chave não existir no formato esperado.

### 15.2 Presets por jogo (`data/games/<id>.json`, com `testedVersion` e `lastVerified`)
| Jogo | Arquivo | Exemplos de chave |
|---|---|---|
| CS2 | Steam `userdata\{steamUser}\730\local\cfg\cs2_video.txt` e `game\csgo\cfg\autoexec.cfg` | MSAA, sombras, `fps_max` |
| Fortnite | `%LOCALAPPDATA%\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini` | `FrameRateLimit`, `bUseVSync`, modo de renderização |
| Apex | `%USERPROFILE%\Saved Games\Respawn\Apex\local\videoconfig.txt` | sombras, `setting.fps_max` |
| R6 Siege | `Documents\My Games\Rainbow Six - Siege\<id>\GameSettings.ini` | `FPSLimit`, Reflex |
| Overwatch 2 | `Documents\Overwatch\Settings\Settings_v0.ini` | `FrameRateCap`, Reflex |
| Valorant | `%LOCALAPPDATA%\VALORANT\Saved\Config\...\GameUserSettings.ini` | Parte das opções fica no servidor: **verificar** o que o arquivo ainda controla |
| COD | `Documents\Call of Duty\players\options.*.cst` | Formato muda por título: marcar como **beta** |

### 15.3 Limite de FPS calculado
- Com VRR (G-Sync/FreeSync) + Reflex: limite ≈ `Hz − Hz²/3600` (ex.: 144 Hz → 138, 240 Hz → 224). Sem VRR: sem limite ou o limite do jogo.
- A fórmula fica no Core (`FrameCap.For(hz, vrr, reflex)`) com testes.

### 15.4 Opções de inicialização
- Steam: mostrar o texto pronto para copiar (não editar `localconfig.vdf`, que o Steam regrava).

**Pronto quando:** cada jogo tem fixture de arquivo real nos testes; aplicar → reverter devolve o arquivo byte a byte; com o jogo aberto, aplicar é recusado.

---

## F16 — GPU dos três fabricantes (3 semanas)

### 16.1 NVIDIA
- **Perfis por aplicativo** via DRS (`NvAPI_DRS_FindApplicationByName`, `CreateProfile`, `CreateApplication`). Novo `NvidiaProfileAction(string Exe, uint SettingId, uint Value)`.
- Ajustes por jogo: modo de energia, limite de FPS do driver (alternativa ao 15.3), V-Sync, modo de baixa latência.
  Validar os IDs e valores aceitos no driver atual antes de ativar (a decisão anterior de não mexer em Low Latency Mode deve ser revista com teste real).
- NVML (iniciado na F13) também para uso de VRAM durante o jogo (alerta no Guardian).

### 16.2 AMD (ADLX)
- Bindings C# gerados com SWIG a partir do ADLX SDK (licença MIT), carregando `amdadlx64.dll` do driver.
- Ajustes: Anti-Lag (ligado), Radeon Chill (desligado), Radeon Boost (desligado no competitivo), Enhanced Sync (desligado), HYPR-RX (desligado).
  Leitura de temperatura, clock e VRAM para o diagnóstico.
- Novo `AmdSettingAction` + `IAmdSettings` + fake.

### 16.3 Intel Arc
- Só leitura via IGCL (`ControlLib.dll`): ReBAR ativo, versão do driver, temperatura. Ajustes ficam para depois.

**Pronto quando:** o checklist manual cobre NVIDIA e Radeon reais; revert devolve o painel de cada fabricante ao estado anterior.

---

## F17 — Guardian v2, nível Process Lasso (3 semanas)

| # | Tarefa | Detalhe |
|---|---|---|
| 17.1 | Detecção por evento | Trocar a varredura a cada 2 s por `Win32_ProcessStartTrace`/`StopTrace` (WMI) ou ETW. Meta: < 0,1% de CPU parado |
| 17.2 | **Modo eficiência para o segundo plano** | `SetProcessInformation(ProcessPowerThrottling)` (EcoQoS, o "modo de eficiência" do próprio Windows) nos processos da lista durante o jogo. É mais seguro que mexer no jogo |
| 17.3 | **CPU Sets para o segundo plano** | `GetSystemCpuSetInformation` (`EfficiencyClass` para P/E, `LastLevelCacheIndex` para o CCD). Mover os processos pesados para os E-cores ou para o CCD sem cache. **O jogo não é tocado por padrão** |
| 17.4 | CPU Sets no jogo (experimental) | Só com opt-in e só se o benchmark da F12 provar ganho. Lista `noTouch` por jogo em `games.json` (jogos com anti-cheat protegido). Se o `OpenProcess` falhar, desistir sem tentar de novo |
| 17.5 | Plano de energia só durante o jogo | Equilibrado parado, plano NOVAIS no jogo. Em X3D de 2 CCDs continua no Equilibrado |
| 17.6 | Standby list inteligente | `NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)` só se a memória livre < 1 GB **e** a standby > 2 GB, no máximo 1 vez a cada 5 min, com log |
| 17.7 | Exclusões do Defender para as pastas de jogos | Opt-in, via `MSFT_MpPreference`, reversível, com aviso. Se a Proteção contra Adulteração bloquear, explicar como fazer pela interface do Windows |
| 17.8 | Alertas | Toast do Windows: VRAM > 95%, RAM > 90%, processo com vazamento (working set subindo por 30 min) |
| 17.9 | Configurável | `settings.json`: lista do usuário para baixar prioridade, eficiência ou ignorar |

**Pronto quando:** `GuardianPolicy` tem testes para cada regra nova; num jogo com anti-cheat, nada é aplicado ao processo do jogo sem opt-in.

---

## F18 — Debloat completo (2 semanas)

| # | Tarefa | Detalhe |
|---|---|---|
| 18.1 | Apps da Store | Novo `AppxPackageAction` via `Windows.Management.Deployment.PackageManager` (TFM `net10.0-windows10.0.19041.0`). **Revert = reinstalar pela Store** (`winget install --source msstore <id>`); o ID da Store fica no JSON e o revert é testado. Lista segura em `data/bloat/appx.json` (Clipchamp, News, Solitaire, Bing Weather...) |
| 18.2 | Edge em segundo plano | Políticas `BackgroundModeEnabled=0` e `StartupBoostEnabled=0` (registro, reversível) |
| 18.3 | OneDrive e Teams | Só tirar da inicialização (sem desinstalar) |
| 18.4 | Serviços de fabricantes | `data/bloat/oem.json`: Armoury Crate, MSI Center, Gigabyte Control Center, iCUE, Razer Synapse. Serviços pesados → **Manual** (nunca Desativado), com aviso de RGB e ventoinhas |
| 18.5 | Xbox | Serviços Xbox só em Manual e **nunca** em X3D de 2 CCDs (`compat.hardware: not-dual-ccd-x3d`) |

**Pronto quando:** cada pacote removido volta com o revert numa VM; o checklist manual inclui RGB funcionando após o 18.4.

---

## F19 — Entrada, áudio, rede e armazenamento (3 semanas)

**Entrada**
- **Teste de polling rate do mouse**: janela invisível com `RegisterRawInputDevices(RIDEV_INPUTSINK)`, 5 s movendo o mouse →
  Hz médio e estabilidade. Alerta quando 4K/8K Hz pesam numa CPU fraca.
- **Mapa dos controladores USB** (SetupAPI, cadeia de dispositivos pai): mostra se mouse, teclado, headset e webcam/placa de captura dividem o controlador.
- **Otimizações para jogos em janela** (`DirectXUserGlobalSettings` → `SwapEffectUpgradeEnable=1;`), reversível.
- **MPO**: diagnóstico e reparo só para quem tem flicker/stutter (`experimental`; **verificar** se a chave ainda funciona no 24H2/25H2).

**Áudio**
- Listar dispositivos; desativar saídas HDMI/DisplayPort sem uso (reversível pela API de dispositivos); desligar realces de áudio por dispositivo.

**Rede**
- Ping até os **servidores reais de cada jogo** (`data/servers.json`; CS2 pela configuração pública de relays da Valve, os outros por região).
- Varredura em segundo plano do Wi-Fi desligada **só durante o jogo** (`WlanSetInterface`, integrado ao Guardian).
- Conferir RSS ligado e buffers da placa de rede.
- Fora do produto: desligar Nagle (`TcpAckFrequency`/`TCPNoDelay`), que não ajuda jogos modernos (fica documentado na seção 11).

**Armazenamento**
- Cache de gravação, estado do BypassIO (`fsutil bypassio state C:`), jogo no SSD mais rápido.

---

## F20 — Confiança e distribuição (2–3 semanas, pode correr em paralelo)

| # | Tarefa | Detalhe |
|---|---|---|
| 20.1 | Assinatura de código | Azure Trusted Signing (exige CNPJ) ou certificado OV/EV; `build.ps1` já aceita o thumbprint |
| 20.2 | Envio à Microsoft | Cada versão no portal WDSI como "software developer" |
| 20.3 | **Atualização automática** | `releases/latest.json` (versão, URL, SHA256) no GitHub Releases. O app baixa, confere **Authenticode + SHA256**, troca o exe ao reiniciar e guarda a versão anterior para voltar |
| 20.4 | **Proteção depois do reboot** | Ao aplicar algo com reinício, registrar uma tarefa de inicialização (SYSTEM, antes do logon) que conta tentativas de boot. Se o usuário não chegar ao logon em 2 boots seguidos, a tarefa executa "Reverter tudo" sozinha. A confirmação de boot saudável no logon apaga a tarefa |
| 20.5 | CI | GitHub Actions `windows-latest`: `dotnet build -warnaserror` + `dotnet test` em cada push |
| 20.6 | Teste em VM automatizado | `scripts/vm-test.ps1` (Hyper-V/Windows Sandbox): export do registro → aplicar nível 3 → reverter → export → diff vazio |
| 20.7 | Relatório de erro opt-in | Envia só a exceção e a versão, nunca dados pessoais |

---

## F21 — Base de dados anônima opt-in (3 semanas + backend)

**Objetivo:** saber qual ajuste funciona em qual hardware. É a vantagem que nenhum concorrente consegue copiar.

- **Consentimento explícito** na primeira execução, com a tela "Ver exatamente o que será enviado". Desligado por padrão. Política de privacidade conforme a **LGPD**.
- **O que é enviado:** modelo de CPU/GPU, RAM (GB e MT/s), build do Windows, ajustes aplicados e o resultado relativo do benchmark (F12).
  **Nunca:** nome, e-mail, número de série, IP salvo, lista de programas.
- **Backend simples:** Cloudflare Workers + D1 (ou Supabase), endpoint único `POST /v1/results` e `GET /v1/evidence?cpu=...&gpu=...`.
- **No app:** cada ajuste mostra "Testado em 214 PCs parecidos com o seu: +3,1% no 1% low (IC 95%: +1,8 a +4,4)".
- **Promoção automática:** um ajuste vira `measured` para uma classe de hardware com N ≥ 30 e intervalo de confiança acima de zero.

---

## F22 — Interface gráfica e inglês (4–6 semanas)

- **Textos:** extrair todos os textos de interface para recursos (`pt-BR` e `en-US`). Teste que falha se houver texto fixo em `Views.cs`.
- **Interface gráfica:** Avalonia sobre o mesmo `NovaisBoost.Core` (publica como exe único). O terminal continua existindo (`--terminal`) para entusiastas e para as tarefas agendadas.
- **Fluxo de 1 clique:** Diagnóstico → "Recomendado para o seu PC" (com a evidência da F21) → Revisar → Aplicar → Provar (F12).
- **Marca:** mesmas cores e banner (NOVAIS roxo, boost branco), tema escuro primeiro.

---

## P2.2 Status da Parte 2

| Fase | Status | Observações |
|---|---|---|
| F11 Correções de precisão | ✅ | Campo `evidence` em todos os ajustes (34: 29 `documented`, 5 `experimental`, nenhum `measured` até a F12); níveis só com ajustes comprovados. `mmcss-gaming` dividido em `mmcss-responsiveness` e `mmcss-games-task`, e backups antigos continuam revertendo. Guardian em AboveNormal (High via `settings.json`). Plano de energia com valores próprios na bateria. `vbs-off` com aviso de Vanguard/HVCI. Texto "Carga de DPC/ISR (%)". HAGS só em RTX 30+/RX 6000+/Arc |
| F12 Benchmark v2 | ✅ | PresentMon 2.6.0 embutido em `tools\` (SHA256 fixo no build e conferido no app). Métricas por quadro (CPU/GPU busy, PC latency, click-to-photon), gargalo, sparkline, travada > 2× a mediana. `BenchmarkSession` com 3 a 5 rodadas, aquecimento de 10 s, IC 95% por bootstrap e veredito. "Provar ajustes" por grupo, com reinício no meio, evidência local em `evidence.json` e reversão só do grupo. DPC/ISR por driver via ETW (TraceEvent 3.2.8). Relatório HTML auto-contido. Falta validar o ETW numa VM e num PC real (checklist) |
| F13 Diagnóstico profundo | ✅ | 14 regras novas, todas puras no Core com testes de disparo/não disparo: PCIe da GPU (cfgmgr32, com subida pelo switch interno das Radeon), RAM no mesmo canal (`memory-slots.json`), WHEA, telas azuis/Kernel-Power 41, TDR, microcódigo Intel (0x12B mínimo, 0x12F recomendado, `microcode.json`), sensores NVML, CPU sob carga (teste opcional de 20 s), jogo/paginação em HD, paginação desligada, overlays, HDMI/VRR, TRIM, outros otimizadores (`optimizers.json`). Nota v2 com teto por categoria. Fontes no JSON (`sources` no snapshot, `source` em cada problema). AMD (ADLX) fica para a F16 |
| F14 Modo Reparar | ✅ | Tipos novos `BcdAction` (bcdedit, valores canônicos yes/no), `PagefileAction` (WMI) e `TcpGlobalAction` (netsh, leitura por WMI), com `IBcdStore`/`IPagefile`/`ITcpSettings`, fakes e testes de falha no lote e de reversão interrompida. Aceite por faixa e por bits no registro e por tipo de início nos serviços. 7 reparos em `data/repairs/` + 1 por serviço de `data/critical-services.json`. Menu [10], `--reparar` somente leitura e aviso no diagnóstico. `scripts/vm-damage.ps1` com trava de VM. Validação na VM pendente (checklist) |
| F15 Configuração dos jogos | ⏳ | |
| F16 GPU três fabricantes | ⏳ | |
| F17 Guardian v2 | ⏳ | |
| F18 Debloat completo | ⏳ | |
| F19 Entrada, áudio, rede, armazenamento | ⏳ | |
| F20 Confiança e distribuição | ⏳ | |
| F21 Base de dados opt-in | ⏳ | |
| F22 Interface gráfica e inglês | ⏳ | |

## P2.3 Itens a verificar antes de implementar

Pontos que dependem de informação externa que muda com o tempo:

- Política atual da Riot/Vanguard sobre HVCI (F11.6) e de COD/Battlefield sobre Secure Boot/TPM.
- Microcódigo Intel mínimo recomendado para 13ª/14ª ger. (F13.6).
- Chave de MPO no Windows 11 24H2/25H2 (F19).
- O que o `GameUserSettings.ini` do Valorant ainda controla e o formato atual do `.cst` do COD (F15.2).
- IDs e valores aceitos de Low Latency Mode no driver NVIDIA atual (F16.1).
