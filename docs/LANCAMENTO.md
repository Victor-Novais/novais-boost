# Lançamento do NOVAIS boost

## 1. Antes de cada versão

1. `pwsh .\scripts\build.ps1 -Version X.Y.Z` (compila sem avisos, roda os testes, publica e gera o SHA256).
2. Rode o checklist de testes manuais abaixo em uma **máquina virtual** com Windows 11 limpo e em pelo menos
   um PC real de cada tipo: AMD + NVIDIA, Intel + NVIDIA, AMD + Radeon, notebook.
3. Atualize o número da versão e anote as mudanças no commit/tag (`git tag vX.Y.Z`).

## 2. Checklist de testes manuais (com administrador)

Faça um snapshot da VM antes. Para cada item, confira o log em `C:\ProgramData\NOVAIS boost\logs`.

- [ ] `--simular --extremo` roda sem erros e lista os ajustes esperados.
- [ ] Nível 1 → Aplicar todos → ponto de restauração criado → plano "NOVAIS boost" aparece em Opções de energia.
- [ ] Nível 2 e 3 → aplicar → reiniciar → Windows inicia normalmente, rede e áudio funcionam.
- [ ] NVIDIA: após aplicar, o Painel de Controle NVIDIA mostra "Preferir desempenho máximo" e "Alto desempenho".
- [ ] Reverter tudo → reiniciar → `--simular` volta a mostrar a mesma lista de antes (nada sobrou).
- [ ] Plano NOVAIS boost removido e plano anterior ativo após reverter.
- [ ] Interromper o PC (desligar a VM) no meio de um lote → ao abrir, "Reverter tudo" conclui a reversão.
- [ ] Cronogramas → Ativar todas → as 5 tarefas aparecem em `\NOVAIS boost` no Agendador; "Executar" em cada uma
      termina com código 0 e sem janela aparecendo.
- [ ] Guardian: abrir um jogo → log mostra o jogo em **AboveNormal** e o navegador em BelowNormal; fechar o jogo → prioridades voltam.
- [ ] Guardian com `{ "guardian": { "gamePriority": "high" } }` em `C:\ProgramData\NOVAIS boost\settings.json` → jogo em High;
      com um valor inválido (ex.: `"realTime"`) → jogo em AboveNormal e aviso no log.
- [ ] Limpar agora: mostra os tamanhos, apaga só arquivos antigos, não apaga nada fora das pastas listadas.
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

## 3. Assinatura de código (evita alerta do SmartScreen e falso positivo de antivírus)

Um otimizador que mexe no registro e roda como administrador **precisa** ser assinado.

1. Compre um certificado de assinatura de código **EV** ou **OV** (Sectigo, DigiCert, SSL.com) ou use o
   **Azure Trusted Signing** (mais barato, exige empresa/CNPJ verificado).
2. Instale o Windows SDK (para o `signtool.exe`).
3. `pwsh .\scripts\build.ps1 -Version X.Y.Z -CertificateThumbprint <thumbprint>`.
4. Envie o exe assinado para análise da Microsoft (https://www.microsoft.com/wdsi/filesubmission) como
   "software developer" para evitar detecções heurísticas do Defender.

## 4. Distribuição e atualização

- Publique a pasta `artifacts\publish` inteira (`NOVAIS boost.exe`, `tools\`, `SHA256.txt`, `THIRD_PARTY_NOTICES.md`)
  em um GitHub Release (ou no seu site), como `.zip`. O `build.ps1` baixa o PresentMon da versão fixa e para se o SHA256
  não conferir. Para atualizar o PresentMon, troque a versão e o hash no `build.ps1` e em `PresentMonTool` (Core).
- Atualização automática ainda **não** está implementada: ela precisa de um endereço fixo de releases.
  Quando houver, o caminho recomendado é: o app consulta `releases/latest`, compara a versão, baixa o exe,
  confere a assinatura (Authenticode) e o SHA256 antes de substituir.

## 5. O que fica fora de propósito (decisões de segurança)

- Sem overclock automático, sem alteração de BIOS (só checklist), sem desligar mitigações de CPU,
  sem limpadores de registro, sem desligar o arquivo de paginação, sem remover a Xbox Game Bar.
- "Low Latency Mode" e tamanho do cache de shader da NVIDIA não são alterados: o driver não aceita os valores
  usados por guias antigos, e o padrão atual (cache de 16 GB) já é bom. Use NVIDIA Reflex no jogo.
- Instalação limpa de driver (estilo DDU/NVCleanstall) não é automatizada: exige Modo de Segurança e
  baixar drivers de terceiros. O NOVAIS boost avisa quando o driver está antigo.
