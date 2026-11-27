# Lançamento do NOVAIS boost

## 1. Antes de cada versão

1. `pwsh .\scripts\changelog.ps1 -Version X.Y.Z` e revise o `CHANGELOG.md`.
2. `pwsh .\scripts\build.ps1 -Version X.Y.Z` com a assinatura (abaixo): compila sem avisos, roda os testes, publica, assina e
   gera `SHA256.txt` e `latest.json`.
3. Numa VM: `pwsh .\scripts\vm-test.ps1 -IUnderstandThisIsAVm` (nível 3 → reverter → registro igual ao de antes).
4. Rode o checklist de testes manuais abaixo em uma **máquina virtual** com Windows 11 limpo e em pelo menos
   um PC real de cada tipo: AMD + NVIDIA, Intel + NVIDIA, AMD + Radeon, notebook.
5. `git tag vX.Y.Z`, crie o release `vX.Y.Z` no GitHub e envie `NOVAIS boost.exe`, `latest.json`, `SHA256.txt` e o `.zip` da pasta.
6. Envie o exe à Microsoft (WDSI, seção 3).

## 2. Checklist de testes manuais (com administrador)

Faça um snapshot da VM antes. Para cada item, confira o log em `C:\ProgramData\NOVAIS boost\logs`.

- [ ] `--simular --extremo` roda sem erros e lista os ajustes esperados.
- [ ] Nível 1 → Aplicar todos → ponto de restauração criado → plano "NOVAIS boost" aparece em Opções de energia.
- [ ] Nível 2 e 3 → aplicar → reiniciar → Windows inicia normalmente, rede e áudio funcionam.
- [ ] NVIDIA: após aplicar, o Painel de Controle NVIDIA mostra "Preferir desempenho máximo" e "Alto desempenho".
- [ ] Reverter tudo → reiniciar → `--simular` volta a mostrar a mesma lista de antes (nada sobrou).
- [ ] Plano NOVAIS boost removido e plano anterior ativo após reverter.
- [ ] Interromper o PC (desligar a VM) no meio de um lote → ao abrir, "Reverter tudo" conclui a reversão.
- [ ] Cronogramas → Ativar todas → as 7 tarefas aparecem em `\NOVAIS boost` no Agendador; "Executar" em cada uma
      termina com código 0 e sem janela aparecendo.
- [ ] Guardian: abrir um jogo → log mostra o jogo em **AboveNormal** e o navegador em BelowNormal; fechar o jogo → prioridades voltam.
- [ ] Guardian com `{ "guardian": { "gamePriority": "high" } }` em `C:\ProgramData\NOVAIS boost\settings.json` → jogo em High;
      com um valor inválido (ex.: `"realTime"`) → jogo em AboveNormal e aviso no log.
- [ ] Limpar agora: mostra os tamanhos, apaga só arquivos antigos, não apaga nada fora das pastas listadas.
- [ ] **Guardian v2:** parado, o processo do Guardian fica perto de 0% de CPU (Gerenciador de Tarefas por 5 min).
- [ ] Com o VALORANT ou o Fortnite abertos, o log diz "anti-cheat protegido" e o jogo mantém a prioridade Normal; com o CS2, vai a AboveNormal.
- [ ] Navegador aberto durante o jogo: aparece como "Eficiência" no Gerenciador de Tarefas e volta ao normal ao fechar o jogo.
- [ ] Intel 12ª+ / X3D de 2 CCDs: com `backgroundCpuSets`, o navegador roda só nos E-cores / no CCD sem V-Cache durante o jogo (Process Explorer > Afinidade de CPU Sets).
- [ ] `powerPlanDuringGame: true`: o plano NOVAIS boost entra com o jogo e o anterior volta ao fechar.
- [ ] `standbyPurge: true` com pouca RAM livre: o log mostra a limpeza no máximo a cada 5 min.
- [ ] Exclusões do Defender: aplicar → aparecem em Segurança do Windows > Exclusões; reverter → somem. Com a Proteção contra
      Adulteração bloqueando, a mensagem explica como fazer manualmente.
- [ ] **NVIDIA por jogo:** com um jogo sem perfil (ex.: um .exe qualquer cadastrado), aplicar → o NVIDIA Profile Inspector mostra
      o perfil "NOVAIS boost - jogo.exe" com "Prefer maximum performance"; reverter → o perfil some. Num jogo com perfil
      predefinido (CS2), reverter mantém o perfil e só a configuração volta.
- [ ] Limite de FPS do driver (experimental): conferir no Painel de Controle NVIDIA que "Taxa de quadros máx." mostra o valor.
- [ ] Radeon: a tela de Jogos mostra as recomendações do AMD Software e não tenta aplicar nada.
- [ ] **Presets dos jogos (CS2 e Fortnite):** com o jogo fechado, Jogos & Streaming → revisar presets → aplicar → abrir o jogo e
      conferir V-Sync, MSAA/shader (CS2) e limite de FPS (Fortnite) no menu. Reverter tudo → o arquivo fica idêntico ao original
      (`Get-FileHash` antes e depois).
- [ ] Com o jogo aberto, aplicar e reverter são recusados com "Feche o jogo".
- [ ] Fortnite com `GameUserSettings.ini` somente leitura → nada é alterado e a mensagem explica o motivo.
- [ ] Ao validar com o arquivo real, troque o fixture em `tests/NovaisBoost.Tests/Fixtures/presets/<jogo>/` pelo arquivo real
      (sem dados pessoais) e preencha `testedVersion` no preset.
- [ ] VRR: 144 Hz com G-Sync → Fortnite em 138 FPS; respondendo "não" ao G-Sync, o limite do jogo não muda.
- [ ] **Modo Reparar, só em VM:** snapshot → `pwsh .\scripts\vm-damage.ps1 -IUnderstandThisIsAVm` → reiniciar →
      `--reparar` lista BCD, paginação, BITS e Windows Update, mitigações, políticas, latência e TCP → menu [10] → restaurar →
      reiniciar → `--reparar` sem itens, e a VM inicia, tem áudio, rede e Windows Update funcionando.
- [ ] Depois do reparo, Reverter tudo devolve exatamente o estado "estragado" (confira com `bcdedit /enum {current}` e `netsh int tcp show global`).
- [ ] O `vm-damage.ps1` se recusa a rodar num PC físico.
- [ ] Num PC limpo, `--reparar` não encontra nada; com os ajustes do NOVAIS boost aplicados (níveis 1 a 3), também não.
- [ ] O diagnóstico mostra "Encontramos N alterações de outros otimizadores" quando há itens.
- [ ] Diagnóstico (com e sem administrador): o cartão de hardware mostra o PCIe da GPU (Gen e linhas), a temperatura (NVIDIA)
      e a conexão de cada monitor (HDMI/DisplayPort). Sem administrador, nada quebra (só a contagem de minidumps fica de fora).
- [ ] GPU num slot x4/x8 ou com riser → "Placa de vídeo em PCIe x8 (suporta x16)" como crítico. No slot certo, nada aparece.
- [ ] Dois pentes no mesmo canal (ex.: A1 e A2) → aviso; em A2 e B2 → nada. Conferir com o nome dos slots de placas ASUS, MSI e Gigabyte.
- [ ] PC com telas azuis ou erros WHEA recentes (VM com `NotMyFault` ou log real) → os avisos de estabilidade aparecem com a contagem.
- [ ] Intel 13ª/14ª geração com BIOS antiga → aviso de microcódigo com a versão atual e a mínima.
- [ ] Teste de CPU sob carga (20 s) no fim do diagnóstico: termina, as ventoinhas voltam ao normal e um notebook na bateria mostra o limite.
- [ ] Paginação desligada numa VM → crítico; voltando para "gerenciado pelo sistema" → some.
- [ ] Com RTSS, Discord e o overlay da NVIDIA abertos → info de overlays. Com Razer Cortex ou Process Lasso instalados → info de otimizador.
- [ ] O JSON em `reports\diagnostico-*.json` tem `sources` e `source` em cada problema novo.
- [ ] Benchmark de carga de DPC/ISR (%) e captura avulsa de FPS geram resultados e a comparação antes × depois.
      A tela de carga mostra só porcentagem; microssegundos aparecem só na opção de latência por driver (ETW).
- [ ] O pacote tem `tools\PresentMon-2.6.0-x64.exe` (assinado pela Intel) e o app o usa sem pedir download.
      Trocar o arquivo por outro faz o app recusá-lo (aviso de SHA256 no log).
- [ ] Captura avulsa num jogo mostra a linha de frametime (sparkline), o gargalo (GPU, CPU/sistema ou limite de FPS)
      e, em jogos com Reflex, a latência do PC.
- [ ] Provar ajustes com 3 rodadas: antes → aplicar um grupo → (reiniciar se pedir; ao abrir de novo, o teste continua) →
      depois → veredito com intervalo de confiança, `evidence.json` atualizado e relatório HTML em `reports\`.
- [ ] O relatório HTML abre no navegador sem internet, como usuário comum (não elevado), e mostra marca, hardware,
      tabela antes × depois e o gráfico.
- [ ] Com veredito "Dentro do ruído" ou "Piorou", "Reverter só este grupo" desfaz apenas os ajustes do grupo; os anteriores continuam.
- [ ] Um ajuste experimental com "Ganho real" passa a aparecer no nível dele neste PC, com "Medido neste PC" na revisão.
- [ ] Latência de DPC/ISR por driver (ETW), numa VM e num PC real: lista drivers (ex.: `nvlddmkm.sys`, `ndis.sys`,
      `storport.sys`) com máximo e total. Com o LatencyMon aberto ao mesmo tempo, os dois continuam funcionando.
- [ ] Rede: teste de ping e bufferbloat terminam; DNS mais rápido → aplicar → reverter volta ao DNS anterior (DHCP).
- [ ] Jogos com anti-cheat (Valorant, Fortnite, CS2/FACEIT, COD) abrem e jogam normalmente com o nível 3 aplicado.
      Se o Valorant mostrar "VAN: RESTRICTION" (HVCI), Reverter tudo religa a Integridade de memória e o jogo volta a abrir.
- [ ] Com o Valorant instalado, a revisão do nível 3 mostra o aviso de Vanguard no ajuste "Desligar VBS / Integridade de memória".
      Sem o Valorant, o aviso não aparece.
- [ ] `--simular` e `--simular --extremo` não listam ajustes experimentais. Em Ajustes individuais, eles aparecem marcados
      "experimental · efeito não comprovado" e a revisão mostra "Efeito não comprovado".
- [ ] GPU GTX (ou RX 5000): o nível 1 mostra "Ligar o agendamento de GPU por hardware" como "não se aplica". RTX 30+ recebe o ajuste.
- [ ] Plano na bateria (desktop com no-break que informa bateria ao Windows): com o plano NOVAIS boost ativo,
      `powercfg /q 4e6f7661-6973-426f-6f73-740000000001 54533251-82be-4824-96c1-47b60b740d00` mostra estado mínimo
      100% na tomada e 5% na bateria, e boost 2 (agressivo) na tomada e 4 (eficiente agressivo) na bateria.
      A suspensão seletiva USB fica desligada na tomada e ligada na bateria.
- [ ] Com um backup antigo que contém `mmcss-gaming`, Reverter tudo devolve os valores do MMCSS e a tarefa "reaplicar"
      não o reaplica (registra no log).

- [ ] **Debloat (F18), só em VM:** Ajustes individuais → remover cada app da Store → some do menu Iniciar → Reverter tudo →
      cada app volta pela Microsoft Store. Sem internet, a reversão falha com a mensagem da Store e funciona depois de reconectar.
- [ ] Antes do primeiro release, conferir cada `storeId` de `data/bloat/appx.json` com `winget show --id <id> --source msstore`.
- [ ] Serviços de fabricantes: num PC com Armoury Crate / iCUE / MSI Center, aplicar → reiniciar → RGB e ventoinhas funcionam
      e o app do fabricante abre. Conferir no `services.msc` os nomes de `data/bloat/oem.json` (nomes errados são ignorados).
- [ ] Ryzen 7950X3D/9950X3D: "Serviços Xbox sob demanda" aparece como "não se aplica" e a Game Bar continua funcionando.
- [ ] Edge: fechar o Edge → nenhum `msedge.exe` no Gerenciador de Tarefas; reverter → o modo em segundo plano volta.
- [ ] OneDrive e Teams fora da inicialização: reiniciar → não abrem; abrir manualmente → sincronizam.
- [ ] Hibernação: aplicar → `hiberfil.sys` some e o espaço aparece livre; reverter → `powercfg /a` mostra Hibernação disponível.
- [ ] Compressão de memória (32 GB+): aplicar → reiniciar → `Get-MMAgent` mostra `MemoryCompression False`; com SysMain desligado,
      a mensagem explica que ele precisa estar ligado.
- [ ] Windows Pro: "Adiar atualizações de recurso" → `gpedit`/Configurações mostram o adiamento de 90 dias. No Home, "não se aplica".

- [ ] **Manutenção v2 (F23):** Lixeira com itens de ontem e de 2 meses atrás → Limpar agora → só os antigos somem.
- [ ] `cleanup.browsers: true` com o Chrome aberto → o cache dele é ignorado (log); fechado → é limpo e o Chrome abre normalmente, logado.
- [ ] A pasta `C:\Windows\Prefetch` continua intacta depois da limpeza.
- [ ] `--tarefa saude` termina; o log mostra DISM /RestoreHealth e sfc /scannow (ok ou aviso com CBS.log).
- [ ] `--tarefa drivers` com driver de vídeo antigo → notificação "Driver de vídeo antigo"; com driver novo → nada.
- [ ] `--tarefa rede` sem "DNS mais rápido" aplicado → só registra ping no log; com ele aplicado → só troca se outro for 10 ms mais rápido.
- [ ] Notificações do Windows: o alerta de RAM do Guardian aparece como toast com o nome "NOVAIS boost".
- [ ] Teste rápido de gargalo: termina em ~30 s, o arquivo temporário some de `ProgramData\NOVAIS boost\temp`, e um PC com um pente
      só ou XMP desligado mostra "Memória lenta".
- [ ] Perfil do meu Hardware → informar fonte de 550 W numa RTX 4080 → aviso de fonte no diagnóstico; `settings.json` ganha `pc`.

- [ ] **F19:** Benchmark → 8 com mouse de 1.000 Hz → ~1.000 Hz e estabilidade alta; num mouse de 4K/8K Hz, comparar com o software do fabricante.
- [ ] Diagnóstico num PC com webcam/placa de captura no mesmo controlador do mouse → "Mouse no mesmo controlador USB"; trocando a porta → some.
- [ ] Cartão de hardware mostra temperatura, desgaste e horas dos SSDs NVMe (como administrador) e a linha BypassIO.
- [ ] Otimizações para jogos em janela: aplicar → Configurações > Sistema > Tela > Gráficos > "Otimizações para jogos em janela" ligado;
      a opção de VRR continua como estava; reverter → volta ao estado anterior.
- [ ] Áudio sem melhorias: aplicar → Configurações > Som > dispositivo → "Melhorias de áudio" desligado; reverter → volta. Se o Windows negar
      a escrita, o ajuste falha sozinho e o resto do lote continua.
- [ ] Saída HDMI da placa de vídeo desativada → some do Gerenciador de Dispositivos (desativado); reverter → volta e o som pelo monitor funciona.
- [ ] Rede → 4 → CS2: lista relays da Valve com ping (São Paulo primeiro no Brasil); Fortnite: regiões da AWS com ping por TCP.
- [ ] Guardian em notebook com Wi-Fi: abrir um jogo → log "varredura Wi-Fi pausada"; fechar → "religada".
- [ ] `guardian.obsCpuSets: true` num Intel híbrido ou X3D de 2 CCDs com OBS aberto → OBS só nos E-cores / CCD sem V-Cache durante o jogo.
- [ ] Jogos & Streaming com OBS instalado → teste de upload termina e mostra o bitrate sugerido.

- [ ] **F20 — proteção depois do reinício (VM):** aplicar o nível 2 → aparecem `\NOVAIS boost\protecao-boot` (SYSTEM) e
      `protecao-logon` → reiniciar e entrar → as duas tarefas somem e o log diz "PC iniciou normalmente".
- [ ] Simular falha: aplicar o nível 2 → reiniciar duas vezes **sem entrar** (desligar na tela de bloqueio) → no terceiro boot o
      log mostra "Desfazendo tudo"; ao entrar, notificação de reversão e `--simular` igual ao estado inicial.
- [ ] `--aplicar 2` sem `--confirmar` → recusa com código 2; `vm-test.ps1` termina com "OK".
- [ ] Atualização: publicar um release de teste assinado com versão maior → ao abrir, oferta de atualização → aceitar → o exe é
      trocado e `NOVAIS boost.anterior.exe` existe → `--voltar-versao` volta. Com o SHA256 errado no `latest.json`, recusa.
      Com o exe atual sem assinatura, recusa.
- [ ] Termo de uso: primeira abertura mostra o termo; recusar → sai; `--diagnostico` continua funcionando sem aceite.
- [ ] [11] Configurações: mudar uma opção → `settings.json` atualizado e válido.
- [ ] Relatório de erros ligado: forçar um erro (ex.: settings.json bloqueado) → `reports\errors\erro-*.json` sem nome de usuário,
      caminhos ou nome do PC.
- [ ] Desinstalar: tarefas removidas, plano "NOVAIS boost" removido, perfis NVIDIA "NOVAIS boost - *" removidos e, se escolhido,
      a pasta `ProgramData\NOVAIS boost` apagada.
- [ ] A CI no GitHub (Actions) passa no push.

- [ ] **F21:** `cd backend && npm test` passa. Com o Worker publicado e `community.endpoint` no settings.json: a tela de consentimento
      aparece uma vez e "Ver exatamente o que seria enviado" mostra o JSON sem nada pessoal; recusar → nada é enviado (log).
- [ ] Aceitar → "Provar ajustes" → o log mostra "resultado enviado (201)"; `GET /v1/evidence` lista o ajuste depois de 3 envios.
- [ ] Sem endereço configurado, a tela de consentimento não aparece e nada é enviado.

- [ ] **F22:** `NOVAIS boost GUI.exe` abre no tema escuro com a marca; sem administrador, Diagnosticar e Recomendado funcionam e
      Aplicar pede elevação; como administrador, Aplicar cria o ponto de restauração e o backup e Reverter tudo desfaz.
- [ ] Windows em inglês (ou `language: en-US`): menu, termo, Configurações e a interface gráfica em inglês.

## 3. Assinatura de código (evita alerta do SmartScreen e falso positivo de antivírus)

Um otimizador que mexe no registro e roda como administrador **precisa** ser assinado.

1. Compre um certificado de assinatura de código **EV** ou **OV** (Sectigo, DigiCert, SSL.com) ou use o
   **Azure Trusted Signing** (mais barato, exige empresa/CNPJ verificado).
2. Instale o Windows SDK (para o `signtool.exe`).
3. Certificado: `pwsh .\scripts\build.ps1 -Version X.Y.Z -CertificateThumbprint <thumbprint>`.
   Azure Trusted Signing: `pwsh .\scripts\build.ps1 -Version X.Y.Z -TrustedSigningMetadata .\signing.json -TrustedSigningDlib <Azure.CodeSigning.Dlib.dll>`,
   com `signing.json` = `{ "Endpoint": "https://<região>.codesigning.azure.net", "CodeSigningAccountName": "...", "CertificateProfileName": "..." }`
   e login feito com `az login`.
4. Envie o exe assinado para análise da Microsoft (https://www.microsoft.com/wdsi/filesubmission) como
   "software developer" para evitar detecções heurísticas do Defender.

## 4. Distribuição e atualização

- Publique a pasta `artifacts\publish` inteira (`NOVAIS boost.exe`, `tools\`, `SHA256.txt`, `THIRD_PARTY_NOTICES.md`)
  em um GitHub Release (ou no seu site), como `.zip`. O `build.ps1` baixa o PresentMon da versão fixa e para se o SHA256
  não conferir. Para atualizar o PresentMon, troque a versão e o hash no `build.ps1` e em `PresentMonTool` (Core).
- Atualização automática: o app lê `https://github.com/Victor-Novais/novais-boost/releases/latest/download/latest.json`
  (gerado pelo `build.ps1`). Por isso o `latest.json` e o exe precisam estar no release marcado como "Latest". Se o repositório
  for privado, o download falha e o app só registra no log; torne os releases públicos (ou troque `Updates.ManifestUrl`).

## 5. O que fica fora de propósito (decisões de segurança)

- Sem overclock automático, sem alteração de BIOS (só checklist), sem desligar mitigações de CPU,
  sem limpadores de registro, sem desligar o arquivo de paginação, sem remover a Xbox Game Bar.
- "Low Latency Mode" e tamanho do cache de shader da NVIDIA não são alterados: o driver não aceita os valores
  usados por guias antigos, e o padrão atual (cache de 16 GB) já é bom. Use NVIDIA Reflex no jogo.
- Instalação limpa de driver (estilo DDU/NVCleanstall) não é automatizada: exige Modo de Segurança e
  baixar drivers de terceiros. O NOVAIS boost avisa quando o driver está antigo.
