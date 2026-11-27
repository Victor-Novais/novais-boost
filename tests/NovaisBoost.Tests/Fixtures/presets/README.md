# Fixtures dos presets de jogos

Reconstruções do formato real de cada arquivo, a partir de exemplos públicos (veja `sources` em `data/games/<id>.json`).
Ainda não são cópias de uma instalação: ao validar um jogo numa instalação real (checklist em `docs/LANCAMENTO.md`),
troque o arquivo daqui pelo arquivo real (sem dados pessoais) e preencha `testedVersion` no preset.
Cada preset precisa de uma pasta `<id>/` com o arquivo de mesmo nome (teste `EveryPresetAppliesAndRevertsOnItsFixture`).
