# Validação da edição de portfólio

Validação local realizada em 6 de novembro de 2026, no Windows.

- Compilação Release de `DreamMover.sln` com MSBuild do Visual Studio 2022, incluindo a pré-compilação de todas as views Razor.
- 13 scripts de teste PowerShell e 1 teste JavaScript aprovados por `tools/Test-Portfolio.ps1`.
- Criação do esquema e aplicação das 11 migrações em um banco LocalDB vazio, separado do banco pessoal.
- Segunda execução da preparação preservando as migrações já aplicadas.
- Download e extração das duas engines com verificação SHA-256.
- Acesso pelo navegador às telas de arena, elenco e confronto.
- HTTP 200 nas sete telas principais e HTTP 404 nas rotas dos recursos excluídos desta edição.
- Execução de uma partida real no IKEMEN, iniciada pela interface em modo exibição: Kung Fu Man venceu Kung Fu Man 720 por 2 a 1. O histórico foi registrado e os Elos passaram de 1000/1000 para 1016/984.

As capturas em `docs/screenshots` mostram essa validação. O banco e os logs usados no teste não fazem parte do código publicado; a preparação de uma instalação nova começa com dois personagens e nenhuma partida.
