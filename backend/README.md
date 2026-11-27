# Backend da base anônima (F21)

Cloudflare Workers + D1. Dois endpoints:

| Método | Caminho | O que faz |
|---|---|---|
| `POST` | `/v1/results` | Recebe o resultado de um "Provar ajustes" (formato `CommunityResult` do app). Campos extras são recusados. |
| `GET` | `/v1/evidence?cpu=...&gpu=...` | Média e IC 95% da mudança no 1% low por ajuste, para aquela CPU e GPU. Ajustes com menos de 3 resultados não aparecem. |

O app só promove um ajuste a `measured` com **N ≥ 30** e o intervalo inteiro acima de zero (`Community.IsProven`).

## Publicar

```
cd backend
npm test                                  # node --test (lógica pura)
npx wrangler d1 create novais-boost-results   # cole o database_id no wrangler.toml
npm run db:init
npm run deploy
```

Depois, no app, o endereço publicado (ex.: `https://novais-boost-results.<conta>.workers.dev`) vai em
`CommunityClient.DefaultEndpoint` (para todos) ou em `community.endpoint` no `settings.json` (só neste PC).
Enquanto não houver endereço, o app não mostra a tela de consentimento e não envia nada.

## Privacidade

Nenhuma coluna guarda IP, nome, e-mail, número de série, nome do PC ou lista de programas; a data é só o mês.
Mantenha o `observability` desligado no `wrangler.toml`. Política completa: [docs/PRIVACIDADE.md](../docs/PRIVACIDADE.md).
