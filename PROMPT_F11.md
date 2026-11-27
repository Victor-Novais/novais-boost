Leia `AGENTS.md` e `PLANO_DE_ACAO.md` por completo antes de começar, com atenção à **Parte 2 do roadmap**
(seções P2.0, P2.1 e F11).

Tarefa: implementar **somente a Fase F11 (Correções de precisão)** do NOVAIS boost. Não comece a F12 nem nenhuma outra fase.
As fases F0 a F10 já estão prontas: leia o código existente antes de mudar qualquer coisa e mantenha o estilo dele.

## Antes de codar
- Rode `dotnet --version`. O `global.json` exige o SDK do .NET 10. Se ele não estiver instalado, pare e me avise
  (`winget install Microsoft.DotNet.SDK.10`).
- Rode `dotnet build NovaisBoost.slnx -c Release -warnaserror` e `dotnet test NovaisBoost.slnx -c Release` para confirmar
  que a base está verde antes das suas mudanças.

## Entregas

1. **Campo `evidence` (P2.0 item 3)**
   - Valores: `measured`, `documented`, `experimental`.
   - Adicionar ao `tweak.schema.json` (obrigatório), ao `TweakDefinition` e a todos os ajustes de `data/tweaks/*.json`.
     Ajustes gerados no código (`DeviceTweaks.cs`, `StartupApps`) também recebem o campo.
   - Classifique cada ajuste com base na documentação oficial da Microsoft/fabricante. Na dúvida, use `experimental`.
   - Os níveis `safe` e `competitive` só podem conter `measured` ou `documented`. `experimental` aparece só em Custom,
     com o aviso "Efeito não comprovado" na revisão.
   - Teste novo: `LevelsOnlyContainProvenTweaks`.

2. **`priority-separation`** → `experimental`, fora do nível 2.

3. **`mmcss-gaming` dividido em dois ajustes**
   - `mmcss-responsiveness`: `SystemResponsiveness` e `NetworkThrottlingIndex` (`documented`).
   - `mmcss-games-task`: valores da tarefa `Tasks\Games` (`experimental`).
   - Os backups já salvos com o id antigo `mmcss-gaming` precisam continuar revertendo. Garanta isso e cubra com teste.
   - O teste `EachSystemTargetBelongsToASingleTweak` precisa continuar passando.

4. **Prioridade do jogo no Guardian**
   - Padrão passa a ser `AboveNormal`. `High` vira opção em `settings.json` (`guardian.gamePriority`).
   - Testes da `GuardianPolicy` para os dois modos.

5. **Plano de energia na bateria**
   - Em `power-plan.json`, valores `dc` próprios: estado mínimo do processador 5%, modo de boost eficiente,
     suspensão seletiva USB ligada. Os valores `ac` não mudam.
   - Teste que confere `dc` diferente de `ac` nos itens de CPU.

6. **Aviso do `vbs-off`**
   - Corrigir o texto de risco: não afirmar que "não afeta Vanguard". Escreva que a Riot pode exigir a Integridade de
     Memória (HVCI) de algumas contas e que, se o Valorant pedir, é preciso religar.
   - `compat.antiCheat: ["Vanguard"]`, com aviso na tela de revisão quando o Valorant estiver instalado.

7. **Texto da medição de DPC**
   - Trocar "Latência DPC" por "Carga de DPC/ISR (%)" em toda a interface e nos relatórios. Nenhum texto pode prometer
     latência em microssegundos até a F12.

8. **`hags-on`**
   - Restringir a GPUs compatíveis (`compat.hardware: gpu-hags`). GTX e GPUs antigas não recebem o ajuste.
   - Teste com hardware falso GTX e RTX.

9. **Documentação**
   - README: explicar o campo `evidence` na seção "Para adicionar um ajuste".
   - `docs/LANCAMENTO.md`: itens novos no checklist manual (aviso do Vanguard, Guardian em AboveNormal, plano na bateria).
   - `PLANO_DE_ACAO.md`: marcar a F11 como ✅ na tabela P2.2, com observações.

## Critérios de aceite
- `dotnet build -warnaserror` sem warnings e `dotnet test` 100% verde.
- `--simular` e `--simular --extremo` rodam sem erro e não listam ajustes `experimental` nos níveis.
- Um backup antigo com `mmcss-gaming` ainda reverte corretamente (teste).
- Nenhum ajuste real executado nesta máquina: tudo testado com fakes (regra 7 do `AGENTS.md`).

## Ao terminar
Pare e me entregue: arquivos alterados, a classificação `evidence` de cada ajuste com o motivo, como testar,
decisões tomadas e o que ficou para a F12.
Se algo do plano estiver ambíguo, escolha a opção mais segura e registre a decisão; não pare para perguntar.
Não faça commit: eu reviso antes.

---

## Modelo para as próximas fases

Troque `F11` pela fase desejada e cole as entregas da seção correspondente do `PLANO_DE_ACAO.md`:

> Leia `AGENTS.md` e `PLANO_DE_ACAO.md` por completo, com atenção à P2.0 e à seção **FXX**.
> Implemente **somente a FXX**. As fases anteriores estão prontas; leia o código existente e mantenha o estilo.
> Confirme que build e testes estão verdes antes de começar. Siga as tarefas e o critério "Pronto quando" da FXX.
> Todo tipo novo de ação segue a receita da P2.0 item 2. Consulte a P2.3 e verifique os pontos externos da fase
> antes de codar; se não der para confirmar, marque o ajuste como `experimental`.
> Ao terminar, atualize README, `docs/LANCAMENTO.md` e a tabela P2.2, e me entregue: arquivos alterados, como testar,
> decisões tomadas e o que ficou pendente. Não faça commit.
