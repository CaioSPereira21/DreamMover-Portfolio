# Arquitetura do DreamMover

O DreamMover é um aplicativo web local para organizar recursos e competições de engines de jogos de luta. O navegador fornece a interface; o servidor ASP.NET executado no Windows acessa o banco e inicia a engine instalada na mesma máquina.

## Fluxo de uma partida

1. A view Razor e o componente Vue enviam os IDs dos lutadores e o cenário escolhido.
2. `PartidaController` solicita a execução a `IkemenMatchRunner`.
3. O serviço carrega os participantes do SQL Server, verifica as regras de divisão e prepara o cenário.
4. `IkemenPilotRunner` inicia o processo do IKEMEN e acompanha o encerramento com limite de tempo. A configuração temporária é removida ao final.
5. O placar do log é validado. `MatchResultService` registra a partida e a progressão em uma transação.
6. A resposta retorna à interface com o vencedor, o placar, a experiência, o Elo e os dados da divisão.

## Organização

| Pasta | Responsabilidade |
|---|---|
| `Controllers` | Rotas MVC, validação de entradas e respostas para a interface |
| `Services` | Regras de progressão, competições, catálogos, importação e execução de partidas |
| `Models` | Modelo Entity Framework database-first e modelos específicos das telas |
| `DTO` | Dados enviados às views e ao JavaScript |
| `Helpers` | Caminhos, imagens e leitura dos formatos das engines |
| `Views` e `Content/js` | Páginas Razor e interação com Vue |
| `App_Data/Migrations` | Estrutura e evolução do banco de demonstração |
| `Tests` | Validações automatizadas com casos de domínio e arquivos temporários |

## Decisões

- **Database-first:** o modelo EDMX descreve as tabelas utilizadas pelo Entity Framework. As tabelas de competição adicionais são preparadas por scripts SQL.
- **Regras de progressão separadas:** `ProgressionCalculator` calcula XP, níveis e Elo sem depender da interface nem do banco. Isso permite testar resultados e casos limites diretamente.
- **Transação no registro:** o placar e as alterações dos participantes precisam representar a mesma partida. Uma falha no registro não deve deixar apenas um lado atualizado.
- **Referência do personagem:** o nome apresentado e a pasta usada pela engine são campos distintos. A execução prioriza `PastaPersonagem`, preservando o nome para a interface.
- **Configuração local:** conexão e ajustes particulares ficam fora do Git. Os caminhos das engines podem ser relativos à aplicação.
- **Dependências gráficas:** a integração usa processos Windows e arquivos de log. Esse desenho é apropriado para a aplicação local; uma futura hospedagem exigiria separar a execução dos combates em um serviço isolado.

## Pontos para evolução

Centralizar a migração do banco em uma ferramenta própria, reduzir responsabilidades dos controllers maiores, automatizar testes de integração com o banco e ampliar o roteiro de demonstração com recursos cuja redistribuição esteja autorizada.
