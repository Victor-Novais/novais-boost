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
- [ ] Guardian: abrir um jogo → log mostra jogo em High e navegador em BelowNormal; fechar o jogo → prioridades voltam.
- [ ] Limpar agora: mostra os tamanhos, apaga só arquivos antigos, não apaga nada fora das pastas listadas.
- [ ] Benchmark de latência e de FPS (com PresentMon na pasta `tools`) geram resultados e a comparação antes × depois.
- [ ] Rede: teste de ping e bufferbloat terminam; DNS mais rápido → aplicar → reverter volta ao DNS anterior (DHCP).
- [ ] Jogos com anti-cheat (Valorant, Fortnite, CS2/FACEIT, COD) abrem e jogam normalmente com o nível 3 aplicado.

## 3. Assinatura de código (evita alerta do SmartScreen e falso positivo de antivírus)

Um otimizador que mexe no registro e roda como administrador **precisa** ser assinado.

1. Compre um certificado de assinatura de código **EV** ou **OV** (Sectigo, DigiCert, SSL.com) ou use o
   **Azure Trusted Signing** (mais barato, exige empresa/CNPJ verificado).
2. Instale o Windows SDK (para o `signtool.exe`).
3. `pwsh .\scripts\build.ps1 -Version X.Y.Z -CertificateThumbprint <thumbprint>`.
4. Envie o exe assinado para análise da Microsoft (https://www.microsoft.com/wdsi/filesubmission) como
   "software developer" para evitar detecções heurísticas do Defender.

## 4. Distribuição e atualização

- Publique `NOVAIS boost.exe` e `SHA256.txt` em um GitHub Release (ou no seu site).
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
