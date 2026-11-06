# Divisões competitivas

O sistema começou em 22/09/2026. Os resultados anteriores foram usados apenas para formar o rating Elo existente; nenhum deles é tratado retroativamente como promoção ou rebaixamento.

## Colocação inicial

Personagens novos permanecem com o estado **Em colocação** até concluírem sete partidas. A primeira usa a 3ª divisão como referência; depois de uma vitória o próximo teste sobe uma divisão e depois de uma derrota desce uma divisão. Adversários não se repetem durante a série.

O rating do personagem em colocação usa fator K ampliado. Ao final da sétima partida, o rating define a divisão inicial:

| Divisão | Rating ao concluir a colocação |
|---|---:|
| 0ª | 1300 ou mais |
| 1ª | 1200–1299 |
| 2ª | 1100–1199 |
| 3ª | 1000–1099 |
| 4ª | 900–999 |
| 5ª | abaixo de 900 |
| 6ª | Não é atribuída pela colocação |

Personagens que já estavam classificados permanecem nas divisões atuais. A movimentação `COLOCACAO` dos novos personagens só é gravada depois da sétima partida e referencia a partida final da série.

## Partidas e sequências

Somente partidas iniciadas com **Divisões ativas** na Arena alteram sequências. Torre, Mata-Mata, laboratório de IA e partidas no modo de exibição continuam atualizando Elo e experiência, mas não alteram divisões.

Uma vitória inicia ou amplia uma sequência positiva. Uma derrota inicia ou amplia uma sequência negativa; ao mudar o resultado, a sequência recomeça em `+1` ou `-1`.

- Nas divisões intermediárias, `+5` libera uma luta de promoção contra a divisão imediatamente superior.
- Da 0ª até a 4ª divisão, `-5` libera uma luta de rebaixamento contra a divisão imediatamente inferior.
- Da 1ª para a 0ª, a promoção ocorre com `+10`.
- Da 5ª para a 6ª, o rebaixamento ocorre somente com `-10`, sem luta de chefe.
- Da 6ª para a 5ª, o retorno exige `+5` e vitória na luta de promoção.

O chefe é escolhido na divisão de destino, priorizando o rating mais próximo da fronteira. Falhar na luta decisiva mantém a divisão atual e reinicia a sequência.

## Persistência

- `LutadorDivisao`: estado atual e sequência.
- `LutadorColocacao`: progresso das sete partidas de colocação.
- `PartidaDivisao`: classifica partidas regulares e decisivas.
- `MovimentacaoDivisao`: histórico de colocação, promoção e rebaixamento.

Para bancos existentes, a migração idempotente está em `App_Data/Migrations/20260924_AddColocacaoE6Divisao.sql`.
