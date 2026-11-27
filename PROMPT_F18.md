Leia `AGENTS.md` e `PLANO_DE_ACAO.md` por completo antes de começar, com atenção à **Parte 2 do roadmap**
(seções P2.0, P2.3, P2.4 e F18).

Tarefa: implementar **somente a Fase F18 (Debloat completo)** do NOVAIS boost. Não comece a F19 nem outra fase, e não
mexa nos itens do backlog P2.4. F0 a F17 já existem (algumas parciais): leia o código antes de mudar qualquer coisa e
mantenha o estilo dele.

## Antes de codar
- Rode `dotnet --version`. O `global.json` exige o SDK do .NET 10. Se ele não estiver instalado, pare e me avise
  (`winget install Microsoft.DotNet.SDK.10`).
- Rode `dotnet build NovaisBoost.slnx -c Release -warnaserror` e `dotnet test NovaisBoost.slnx -c Release` para confirmar
  que a base está verde antes das suas mudanças.
- Verifique os pontos externos da P2.3 que valem para esta fase (reinstalação de pacotes Appx pelo `winget --source msstore`).
  Se não der para confirmar um item, deixe-o `experimental` ou fora da lista, e registre o motivo.

## Entregas

1. **Apps da Store (18.1): novo tipo de ação `appxPackage`**
   - Siga a receita da P2.0 item 2: `record AppxPackageAction` em `TweakDefinition.cs`, `SettingState` em
     `SystemInterfaces.cs`, interface `IAppxPackages` no Core, implementação real no App com
     `Windows.Management.Deployment.PackageManager` (mude o TFM do **App** para `net10.0-windows10.0.19041.0`; o Core
     continua `net10.0`), fake nos testes e definição no `tweak.schema.json`.
   - Remoção só para o usuário atual (nunca `-AllUsers`, nunca pacotes provisionados do sistema).
   - Revert = reinstalar pela Store com o ID guardado no JSON (`winget install --source msstore <id>`). Sem ID da Store
     confirmado, o pacote não entra na lista.
   - Lista segura em `data/bloat/appx.json` (Clipchamp, Microsoft News, Solitaire Collection, Bing Weather, Get Help,
     Tips...). Nunca: Store, Xbox/Game Bar, `Microsoft.GamingApp`, Calculadora, Fotos, Terminal, extensões de vídeo/imagem.
   - Testes: detect/apply/revert, falha no meio do lote, reversão após reinício interrompido, reinstalação falhando
     (sem internet) com mensagem clara e o backup preservado.

2. **Edge em segundo plano (18.2)**: políticas `BackgroundModeEnabled=0` e `StartupBoostEnabled=0` em
   `HKLM\SOFTWARE\Policies\Microsoft\Edge` (`documented`). Confira que o Modo Reparar não as trata como estrago.

3. **OneDrive e Teams (18.3)**: só tirar da inicialização, reutilizando o mecanismo de `StartupApps` do Guardian. Nada é desinstalado.

4. **Serviços de fabricantes (18.4)**: `data/bloat/oem.json` com os serviços pesados de Armoury Crate, MSI Center,
   Gigabyte Control Center, iCUE e Razer Synapse. Só **Manual**, nunca Desativado, e aviso de RGB/ventoinhas na revisão.
   O ajuste só aparece quando o serviço existe no PC.

5. **Xbox (18.5)**: serviços Xbox (XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc) em Manual, fora dos níveis
   1 e 2, e bloqueados em X3D de 2 CCDs com `compat.hardware: not-dual-ccd-x3d`. Teste com CPU falsa 7950X3D e 7800X3D.

6. **Efeitos visuais (18.6)**: "melhor desempenho" mantendo fontes suavizadas e miniaturas. Não pode disputar valores com
   `visual-animations` e `visual-transparency` (teste `EachSystemTargetBelongsToASingleTweak`).

7. **Hibernação (18.7)**: só em Custom, mostrando o espaço que será liberado; nunca por padrão em notebook.
   Se precisar de um tipo novo de ação (`powercfg /hibernate`), siga a receita da P2.0 item 2.

8. **Adiar atualizações de recurso (18.8)**: `DeferFeatureUpdates` + período em dias (`documented`). Nunca adiar updates
   de qualidade/segurança. Garanta com teste que o reparo `windows-update-policies` não desfaz este ajuste
   (`RepairsNeverUndoANovaisBoostTweak`).

9. **Compressão de memória (18.9)**: `experimental`, só com 32 GB ou mais (novo `compat.hardware: ram-32gb`, com teste
   de 16 GB e 32 GB).

10. **Documentação**
    - README: seção "Debloat" com a lista do que é removido/desligado e como voltar.
    - `docs/LANCAMENTO.md`: itens novos no checklist (cada pacote Appx volta pelo revert numa VM; RGB e ventoinhas
      funcionam após o 18.4; Xbox intocado em X3D de 2 CCDs; Edge sem processos em segundo plano após fechar).
    - `PLANO_DE_ACAO.md`: marcar a F18 na tabela P2.2, com observações e o que ficou pendente.

## Critérios de aceite
- `dotnet build -warnaserror` sem warnings e `dotnet test` 100% verde.
- `LevelsOnlyContainProvenTweaks`, `EachSystemTargetBelongsToASingleTweak` e `RepairsNeverUndoANovaisBoostTweak` passam.
- `--simular` e `--simular --extremo` rodam sem erro e não listam itens `experimental`.
- Nenhum ajuste real executado nesta máquina: tudo testado com fakes (regra 7 do `AGENTS.md`). Nenhum pacote removido
  de verdade durante o desenvolvimento.

## Ao terminar
Pare e me entregue: arquivos alterados, a lista de pacotes/serviços com a `evidence` e o motivo de cada um, como testar,
decisões tomadas e o que ficou pendente.
Se algo do plano estiver ambíguo, escolha a opção mais segura e registre a decisão; não pare para perguntar.
Não faça commit: eu reviso antes.
