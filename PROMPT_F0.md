Leia `AGENTS.md` e `PLANO_DE_ACAO.md` por completo antes de começar.

Tarefa: implementar **somente a Fase F0 (Fundação)** do NOVAIS boost. Não comece F1 nem nenhuma otimização real.

## Entregas

1. **Solução .NET 10**
   - `src/NovaisBoost.App` (console, ponto de entrada)
   - `src/NovaisBoost.Core` (lógica, sem dependência de UI)
   - `tests/NovaisBoost.Tests` (xUnit)
   - `data/tweaks/` (catálogo de tweaks em JSON)
   - Publicação: exe único, auto-contido, `win-x64`, manifesto `requireAdministrator`.

2. **Banner e menu**
   - Recriar o banner de `banner.ps1` com Spectre.Console: NOVAIS em degradê roxo, boost em branco, linha com o subtítulo.
   - Card curto com dados básicos (Windows, CPU, GPU, RAM) via WMI/CIM.
   - Menu principal com as 8 opções do plano. Itens ainda não implementados mostram "Em breve".
   - Funcionar bem no Windows Terminal e no terminal do VS Code.

3. **Checagem de administrador**
   - Se não estiver elevado, mostrar mensagem clara e oferecer reabrir como admin.

4. **Logger**
   - Arquivo em `%ProgramData%\NOVAIS boost\logs\`, um por dia, com nível, horário e ação.

5. **SnapshotManager**
   - Criar ponto de restauração. Tratar: Proteção do Sistema desligada (oferecer ligar) e o limite de 1 ponto a cada 24 h do Windows (`SystemRestorePointCreationFrequency`).
   - Antes de cada tweak, salvar o valor atual de tudo que será alterado (registro, serviço, plano de energia) em `%ProgramData%\NOVAIS boost\backups\<data>\state.json`.
   - Comando "Reverter tudo" que restaura a partir desse arquivo.

6. **Formato de tweak e TweakEngine**
   - Schema JSON com: `id`, `name`, `description` (pt-BR), `category`, `level` (safe | competitive | extreme), `risk`, `requiresReboot`, `compat` (regras de hardware/anti-cheat), `actions` (lista de operações) e o estado esperado.
   - Tipos de operação na F0: `registry` (set/delete valor), `service` (startup type), `powercfg`.
   - O engine faz `detect` (já aplicado?), `apply`, `revert`, com **dry-run como padrão**.
   - Registro, serviços e powercfg atrás de interfaces (`IRegistry`, `IServiceController`, `IPowerConfig`) para permitir fakes nos testes.
   - Incluir **3 tweaks de exemplo** inofensivos e reversíveis (ex.: desligar aceleração do mouse, desligar Game DVR em segundo plano, efeitos visuais de transparência off).

7. **Testes**
   - Carregamento e validação do schema (JSON inválido gera erro claro).
   - detect/apply/revert de cada tipo de operação usando fakes.
   - Dry-run não altera nada.
   - Snapshot salva e restaura o estado corretamente.

## Critérios de aceite
- `dotnet build` sem warnings e `dotnet test` 100% verde.
- `dotnet publish` gera um único `.exe` que abre, mostra o banner e o menu.
- Nenhum tweak real é executado nos testes.
- Executar os 3 tweaks de exemplo e depois "Reverter tudo" deixa o sistema exatamente como antes.

## Ao terminar
Pare e me entregue: árvore de arquivos criada, como compilar/publicar/testar, decisões tomadas e o que ficou para a F1.
Se algo do plano estiver ambíguo, escolha a opção mais segura e registre a decisão — não pare para perguntar.
