# Sons privados (fora do repositório)

Estes três arquivos são usados pelo mod mas não podem ser publicados, então ficam só na máquina local:

| Arquivo | O que é | Onde o mod usa |
|---|---|---|
| `ost_main.wav` | "80s Horror Throwback Scary Creeper Bed" (APM Music, comercial), 0-75.1 s com crossfade de 2 s no loop | Trilha principal, sobe com a tensão |
| `countdown.wav` | Música da contagem (enviada pelo jogador, origem não registrada) | Evento "Contagem" e final reserva |
| `final_god.wav` | "god-is-coming", 45 s (enviada pelo jogador, origem não registrada) | Final da fita 8 |

Sem eles: a trilha fica só com a atmosfera e os drones sintetizados, o evento "Contagem" não acontece, e sem
`final_god` o final cai na versão reserva (`FinaleCountdown`), que usa `countdown`. Um build sem nenhum dos
três ainda não foi testado.
