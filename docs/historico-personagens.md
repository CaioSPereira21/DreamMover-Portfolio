# Histórico de alterações dos personagens

O DreamMover agora registra um histórico por personagem a partir de 08/09/2026. A nova aba **Histórico de alterações** fica na tela de detalhes e mostra o estado inicial, alterações posteriores, autor/responsável, origem, data, vida, ataque, defesa, energia, escalas, nome, universo, tags, imagem e fingerprints dos arquivos usados pelo `.def`.

## O que é registrado

- Cadastro: nome, autor, universo e tags.
- Arquivos do personagem: autor e versão declarados no `[Info]`, versão do MUGEN, `.def` principal e hashes dos arquivos `cmd`, `cns` e `st` referenciados em `[Files]`.
- Atributos encontrados no `.cns`: `life`, `attack`, `defence`, `power`, `xscale` e `yscale`.
- Portrait e stand: nome do arquivo e uma cópia arquivada por hash. Se o mesmo nome de imagem for sobrescrito, a imagem anterior continua disponível no evento antigo.
- Pesquisa de atualizações: data da última busca, fonte ou link, observação e responsável pelo registro.

Alterações de arquivo são exibidas recolhidas em cada evento para manter a leitura dos atributos simples. Valores que não puderam ser lidos são preservados, e referências ausentes aparecem como aviso; o sistema não transforma uma falha de leitura em “valor zero”.

## Quando grava

As rotas de edição do cadastro, universo, portrait, stand, importação de pacote e cadastro coletivo são envolvidas automaticamente. O histórico também faz uma conferência quando a aba é aberta, permitindo registrar mudanças feitas fora do DreamMover na próxima consulta. Falhas parciais ficam identificadas como **Alteração parcial detectada após falha**.

O registro inicial dos 416 personagens existentes foi criado uma vez, sem apagar histórico anterior. Ele representa o estado encontrado na data da implantação; não é uma reconstrução de alterações antigas.

## Consulta otimizada

- Busca em todo o histórico, incluindo autor/responsável, origem, nomes de arquivos e valores anteriores e posteriores. Não diferencia maiúsculas ou acentos.
- Filtros por atributos, imagens, cadastro e arquivos/IA. Um evento que corresponde ao filtro continua mostrando seu contexto completo.
- Eventos recolhíveis com resumo e data. O registro inicial começa fechado; arquivos e suas identificações ficam em níveis de detalhe separados.
- Imagens anteriores e posteriores podem ser ampliadas. As miniaturas só entram na tela quando o evento é aberto.
- Consulta em páginas de 20 registros, mantendo a posição mesmo se novos eventos forem registrados enquanto o histórico está aberto.
- A abertura inicial da aba e **Verificar alterações** conferem os arquivos atuais. Buscar, filtrar e carregar registros anteriores consultam apenas o histórico salvo, sem repetir essa conferência. Uma conferência sem mudanças também evita regravar o histórico.

## Persistência e backup

Os registros ficam localmente em `App_Data/CharacterHistory/<id>/history.json`, com as imagens em `images/`. Essa pasta está fora do controle de versão pelo `.gitignore`, então deve entrar no backup do usuário junto com o banco e as imagens de personagens. Um backup consistente deve copiar a pasta inteira, sem copiar apenas o JSON e esquecer `images`.

O histórico é protegido por bloqueios de edição e gravação atômica. O endpoint de imagem aceita somente nomes de arquivo derivados de SHA-256, impedindo traversal de caminho. Arquivos JSON corrompidos não são substituídos silenciosamente.

## Limites atuais

O histórico não altera arquivos de jogo, não instala patches de IA e não mede partidas. XP, nível, rating Elo e resultados de combate continuam sendo estatísticas de jogo, não alterações de cadastro. Para cada possível patch encontrado na internet, é preciso fazer backup, comparar a pasta, testar e depois retornar ao DreamMover para que a conferência registre a mudança detectada.

Na seção **Pesquisa de atualizações** da aba de histórico, use **Registrar busca** depois de consultar a internet. A data pode ser ajustada para o dia real da pesquisa; a fonte pode ser o nome do site ou um link; a observação pode registrar se havia uma versão melhor, se o patch foi descartado ou qual teste ficou pendente. O registro mais recente fica visível no detalhe do personagem e é substituído quando uma nova busca é salva.
