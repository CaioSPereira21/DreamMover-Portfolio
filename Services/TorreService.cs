using DreamMover.DTO;
using DreamMover.Helpers;
using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;

namespace DreamMover.Services
{
    public sealed class TorreAndarExecucao
    {
        public int TorreId { get; set; }
        public int AndarId { get; set; }
        public int Ordem { get; set; }
        public int TotalAndares { get; set; }
        public int IdLutador { get; set; }
        public int IdAdversario { get; set; }
        public string UniversoAlvo { get; set; }
    }

    public static class TorreService
    {
        private const int TrofeuBronze = 1;

        public static void ValidarBanco(lutasEntities db)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            int preparado = db.Database.SqlQuery<int>(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.Torre', N'U') IS NOT NULL " +
                "AND OBJECT_ID(N'dbo.TorreAndar', N'U') IS NOT NULL " +
                "AND OBJECT_ID(N'dbo.ConquistaUniverso', N'U') IS NOT NULL " +
                "THEN 1 ELSE 0 END").Single();

            if (preparado != 1)
            {
                throw new InvalidOperationException(
                    "O banco da Torre ainda nao foi preparado. Execute a migracao 20260813_AddTorreUniverso.sql.");
            }
        }

        public static List<TorreUniversoOpcaoDTO> ObterUniversosElegiveis(
            lutasEntities db,
            int lutadorId)
        {
            ValidarBanco(db);

            Lutador lutador = db.Lutador.AsNoTracking().FirstOrDefault(x => x.Id == lutadorId);
            if (lutador == null)
                throw new ArgumentException("Lutador nao encontrado.");

            List<LutadorUniversoRow> elenco = db.Lutador
                .AsNoTracking()
                .Where(x => x.Id != lutadorId && x.Universo != null && x.Universo != "")
                .Select(x => new LutadorUniversoRow
                {
                    Id = x.Id,
                    Universo = x.Universo
                })
                .ToList();

            List<ConquistaRow> conquistas = db.Database.SqlQuery<ConquistaRow>(
                @"SELECT Universo, CAST(NivelTrofeu AS INT) AS NivelTrofeu
                  FROM dbo.ConquistaUniverso
                  WHERE IdLutador = @lutadorId",
                new SqlParameter("@lutadorId", lutadorId)).ToList();

            List<DominioAdversarioRow> derrotados = db.Database.SqlQuery<DominioAdversarioRow>(
                @"SELECT DISTINCT t.UniversoAlvo AS Universo, ta.IdAdversario
                  FROM dbo.Torre t
                  INNER JOIN dbo.TorreAndar ta ON ta.IdTorre = t.Id
                  WHERE t.IdLutador = @lutadorId
                    AND ta.Status = 'Vencido'",
                new SqlParameter("@lutadorId", lutadorId)).ToList();

            Dictionary<string, ConquistaRow> conquistaPorUniverso = conquistas
                .GroupBy(x => x.Universo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.NivelTrofeu).First(), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, HashSet<int>> derrotadosPorUniverso = derrotados
                .GroupBy(x => x.Universo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => new HashSet<int>(x.Select(y => y.IdAdversario)),
                    StringComparer.OrdinalIgnoreCase);

            return elenco
                .GroupBy(x => (x.Universo ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key)
                    && x.Count() >= TorreOpponentSelector.QuantidadeAndaresRapida)
                .Select(grupo =>
                {
                    ConquistaRow conquista;
                    conquistaPorUniverso.TryGetValue(grupo.Key, out conquista);

                    HashSet<int> idsDerrotados;
                    derrotadosPorUniverso.TryGetValue(grupo.Key, out idsDerrotados);
                    int dominados = idsDerrotados == null
                        ? 0
                        : grupo.Count(x => idsDerrotados.Contains(x.Id));

                    return new TorreUniversoOpcaoDTO
                    {
                        Nome = grupo.Key,
                        Imagem = "/Imagens/" + grupo.Key + ".png",
                        QuantidadeAdversarios = grupo.Count(),
                        UniversoNatal = string.Equals(
                            (lutador.Universo ?? string.Empty).Trim(),
                            grupo.Key,
                            StringComparison.OrdinalIgnoreCase),
                        Conquistado = conquista != null,
                        NivelTrofeu = conquista == null ? 0 : conquista.NivelTrofeu,
                        Dominio = new TorreDominioDTO
                        {
                            Derrotados = dominados,
                            TotalAtual = grupo.Count()
                        }
                    };
                })
                .OrderByDescending(x => x.UniversoNatal)
                .ThenByDescending(x => x.Conquistado)
                .ThenBy(x => x.Nome)
                .ToList();
        }

        public static TorreDTO Criar(lutasEntities db, int lutadorId, string universoAlvo)
        {
            ValidarBanco(db);

            string universo = (universoAlvo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(universo))
                throw new ArgumentException("Selecione o universo da torre.");

            Lutador lutador = db.Lutador
                .FirstOrDefault(x => x.Id == lutadorId);

            if (lutador == null)
                throw new ArgumentException("Lutador nao encontrado.");

            List<Lutador> adversarios = db.Lutador
                .Where(x => x.Id != lutadorId && x.Universo == universo)
                .ToList();

            if (adversarios.Count < TorreOpponentSelector.QuantidadeAndaresRapida)
            {
                throw new ArgumentException(
                    "Esse universo nao possui cinco adversarios disponiveis para o lutador selecionado.");
            }

            HashSet<int> idsJaDerrotados = new HashSet<int>(db.Database.SqlQuery<int>(
                @"SELECT DISTINCT ta.IdAdversario
                  FROM dbo.Torre t
                  INNER JOIN dbo.TorreAndar ta ON ta.IdTorre = t.Id
                  WHERE t.IdLutador = @lutadorId
                    AND t.UniversoAlvo = @universo
                    AND ta.Status = 'Vencido'",
                new SqlParameter("@lutadorId", lutadorId),
                new SqlParameter("@universo", universo)).ToList());

            List<TowerFighterCandidate> candidatos = adversarios.Select(x => new TowerFighterCandidate
            {
                Id = x.Id,
                Nome = x.Nome,
                RatingElo = x.RatingElo > 0 ? x.RatingElo : ProgressionCalculator.RatingEloInicial,
                Nivel = x.Nivel ?? 1,
                JaDerrotado = idsJaDerrotados.Contains(x.Id)
            }).ToList();

            List<TowerFighterCandidate> selecionados = TorreOpponentSelector.Selecionar(
                candidatos,
                TorreOpponentSelector.QuantidadeAndaresRapida);

            string tipo = string.Equals(
                (lutador.Universo ?? string.Empty).Trim(),
                universo,
                StringComparison.OrdinalIgnoreCase)
                ? "Prova"
                : "Invasao";

            int torreId;
            using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.Torre
                          SET Status = 'Abandonada', DataConclusao = SYSDATETIME()
                          WHERE IdLutador = @lutadorId AND Status = 'EmAndamento'",
                        new SqlParameter("@lutadorId", lutadorId));

                    torreId = db.Database.SqlQuery<int>(
                        @"INSERT INTO dbo.Torre
                            (IdLutador, UniversoAlvo, Tipo, Status, AndarAtual, TotalAndares,
                             QuantidadeElencoNoInicio, DataInicio)
                          VALUES
                            (@lutadorId, @universo, @tipo, 'EmAndamento', 1, @totalAndares,
                             @quantidadeElenco, SYSDATETIME());
                          SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new SqlParameter("@lutadorId", lutadorId),
                        new SqlParameter("@universo", universo),
                        new SqlParameter("@tipo", tipo),
                        new SqlParameter("@totalAndares", selecionados.Count),
                        new SqlParameter("@quantidadeElenco", adversarios.Count)).Single();

                    for (int i = 0; i < selecionados.Count; i++)
                    {
                        TowerFighterCandidate adversario = selecionados[i];
                        db.Database.ExecuteSqlCommand(
                            @"INSERT INTO dbo.TorreAndar
                                (IdTorre, Ordem, IdAdversario, RatingEloNoSorteio,
                                 NivelNoSorteio, Funcao, Status)
                              VALUES
                                (@torreId, @ordem, @adversarioId, @rating, @nivel, @funcao, 'Pendente')",
                            new SqlParameter("@torreId", torreId),
                            new SqlParameter("@ordem", i + 1),
                            new SqlParameter("@adversarioId", adversario.Id),
                            new SqlParameter("@rating", adversario.RatingElo),
                            new SqlParameter("@nivel", adversario.Nivel),
                            new SqlParameter("@funcao", adversario.Funcao));
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return Obter(db, torreId);
        }

        public static List<ConquistaUniversoDTO> ObterConquistas(
            lutasEntities db,
            int lutadorId)
        {
            ValidarBanco(db);

            if (!db.Lutador.AsNoTracking().Any(x => x.Id == lutadorId))
                throw new ArgumentException("Lutador nao encontrado.");

            List<ConquistaDetalheRow> conquistas = db.Database.SqlQuery<ConquistaDetalheRow>(
                @"SELECT
                    c.IdTorreOrigem,
                    c.Universo,
                    CAST(c.NivelTrofeu AS INT) AS NivelTrofeu,
                    c.QuantidadeElencoNaConquista,
                    c.DataPrimeiraConquista,
                    c.DataAtualizacao,
                    t.Tipo AS TorreTipo,
                    t.Status AS TorreStatus,
                    t.TotalAndares AS TorreTotalAndares,
                    t.DataInicio AS TorreDataInicio,
                    t.DataConclusao AS TorreDataConclusao
                  FROM dbo.ConquistaUniverso c
                  INNER JOIN dbo.Torre t ON t.Id = c.IdTorreOrigem
                  WHERE c.IdLutador = @lutadorId
                  ORDER BY c.NivelTrofeu DESC, c.Universo",
                new SqlParameter("@lutadorId", lutadorId)).ToList();

            return conquistas.Select(x => new ConquistaUniversoDTO
            {
                IdTorreOrigem = x.IdTorreOrigem,
                Universo = x.Universo,
                Imagem = "/Imagens/" + x.Universo + ".png",
                NivelTrofeu = x.NivelTrofeu,
                TrofeuNome = ObterNomeTrofeu(x.NivelTrofeu),
                QuantidadeElencoNaConquista = x.QuantidadeElencoNaConquista,
                DataPrimeiraConquista = x.DataPrimeiraConquista,
                DataAtualizacao = x.DataAtualizacao,
                Dominio = ObterDominio(db, lutadorId, x.Universo),
                TorreOrigem = new ConquistaTorreDTO
                {
                    Id = x.IdTorreOrigem,
                    Tipo = x.TorreTipo,
                    TipoNome = x.TorreTipo == "Prova" ? "Prova do Universo" : "Invasao de Universo",
                    Status = x.TorreStatus,
                    TotalAndares = x.TorreTotalAndares,
                    DataInicio = x.TorreDataInicio,
                    DataConclusao = x.TorreDataConclusao,
                    Andares = ObterAndares(db, x.IdTorreOrigem)
                }
            }).ToList();
        }

        public static TorreAndarExecucao ObterProximoAndar(lutasEntities db, int torreId)
        {
            ValidarBanco(db);

            using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    TorreAndarExecucao proximo = db.Database.SqlQuery<TorreAndarExecucao>(
                        @"SELECT TOP 1
                            t.Id AS TorreId,
                            ta.Id AS AndarId,
                            ta.Ordem,
                            t.TotalAndares,
                            t.IdLutador,
                            ta.IdAdversario,
                            t.UniversoAlvo
                          FROM dbo.Torre t
                          INNER JOIN dbo.TorreAndar ta ON ta.IdTorre = t.Id
                          WHERE t.Id = @torreId
                            AND t.Status = 'EmAndamento'
                            AND ta.Status = 'Pendente'
                          ORDER BY ta.Ordem",
                        new SqlParameter("@torreId", torreId)).FirstOrDefault();

                    if (proximo == null)
                        throw new ArgumentException("A torre nao possui um andar pendente.");

                    int reservado = db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.TorreAndar
                          SET Status = 'Executando'
                          WHERE Id = @andarId AND Status = 'Pendente'",
                        new SqlParameter("@andarId", proximo.AndarId));

                    if (reservado != 1)
                        throw new InvalidOperationException("O andar ja esta sendo executado.");

                    transaction.Commit();
                    return proximo;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void LiberarAndar(lutasEntities db, TorreAndarExecucao andar)
        {
            if (db == null || andar == null)
                return;

            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.TorreAndar
                  SET Status = 'Pendente'
                  WHERE Id = @andarId
                    AND IdTorre = @torreId
                    AND Status = 'Executando'",
                new SqlParameter("@andarId", andar.AndarId),
                new SqlParameter("@torreId", andar.TorreId));
        }

        public static TorreDTO RegistrarResultadoAndar(
            lutasEntities db,
            TorreAndarExecucao andar,
            MatchRegistrationResult partida)
        {
            if (andar == null)
                throw new ArgumentNullException("andar");

            if (partida == null)
                throw new ArgumentNullException("partida");

            bool venceu = partida.VencedorId == andar.IdLutador;

            using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    int atualizados = db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.TorreAndar
                          SET Status = @status,
                              IdPartida = @partidaId,
                              DataConclusao = SYSDATETIME()
                          WHERE Id = @andarId
                            AND IdTorre = @torreId
                            AND Status = 'Executando'",
                        new SqlParameter("@status", venceu ? "Vencido" : "Derrotado"),
                        new SqlParameter("@partidaId", partida.PartidaId),
                        new SqlParameter("@andarId", andar.AndarId),
                        new SqlParameter("@torreId", andar.TorreId));

                    if (atualizados != 1)
                        throw new InvalidOperationException("O andar ja foi processado ou nao pertence a esta torre.");

                    bool concluiu = venceu && andar.Ordem >= andar.TotalAndares;
                    if (!venceu)
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.Torre
                              SET Status = 'Fracassada', DataConclusao = SYSDATETIME()
                              WHERE Id = @torreId AND Status = 'EmAndamento'",
                            new SqlParameter("@torreId", andar.TorreId));
                    }
                    else if (concluiu)
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.Torre
                              SET Status = 'Concluida',
                                  AndarAtual = TotalAndares,
                                  DataConclusao = SYSDATETIME()
                              WHERE Id = @torreId AND Status = 'EmAndamento'",
                            new SqlParameter("@torreId", andar.TorreId));

                        RegistrarConquista(db, andar);
                    }
                    else
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.Torre
                              SET AndarAtual = @proximoAndar
                              WHERE Id = @torreId AND Status = 'EmAndamento'",
                            new SqlParameter("@proximoAndar", andar.Ordem + 1),
                            new SqlParameter("@torreId", andar.TorreId));
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return Obter(db, andar.TorreId);
        }

        public static TorreDTO Obter(lutasEntities db, int torreId)
        {
            ValidarBanco(db);

            TorreRow torre = db.Database.SqlQuery<TorreRow>(
                @"SELECT
                    t.Id,
                    t.IdLutador,
                    l.Nome AS LutadorNome,
                    l.Portrait AS LutadorPortrait,
                    l.Stand AS LutadorStand,
                    l.Universo AS LutadorUniverso,
                    t.UniversoAlvo,
                    t.Tipo,
                    t.Status,
                    t.AndarAtual,
                    t.TotalAndares,
                    t.QuantidadeElencoNoInicio,
                    t.DataInicio,
                    t.DataConclusao,
                    CAST(ISNULL(c.NivelTrofeu, 0) AS INT) AS NivelTrofeu
                  FROM dbo.Torre t
                  INNER JOIN dbo.Lutador l ON l.Id = t.IdLutador
                  LEFT JOIN dbo.ConquistaUniverso c
                    ON c.IdLutador = t.IdLutador AND c.Universo = t.UniversoAlvo
                  WHERE t.Id = @torreId",
                new SqlParameter("@torreId", torreId)).FirstOrDefault();

            if (torre == null)
                throw new ArgumentException("Torre nao encontrada.");

            List<TorreAndarDTO> andares = ObterAndares(db, torreId);

            TorreDominioDTO dominio = ObterDominio(db, torre.IdLutador, torre.UniversoAlvo);

            return new TorreDTO
            {
                Id = torre.Id,
                IdLutador = torre.IdLutador,
                LutadorNome = torre.LutadorNome,
                LutadorPortrait = Imagem(torre.LutadorPortrait),
                LutadorStand = Imagem(torre.LutadorStand),
                LutadorUniverso = torre.LutadorUniverso,
                UniversoAlvo = torre.UniversoAlvo,
                UniversoImagem = "/Imagens/" + torre.UniversoAlvo + ".png",
                Tipo = torre.Tipo,
                TipoNome = torre.Tipo == "Prova" ? "Prova do Universo" : "Invasao de Universo",
                Status = torre.Status,
                AndarAtual = torre.AndarAtual,
                TotalAndares = torre.TotalAndares,
                QuantidadeElencoNoInicio = torre.QuantidadeElencoNoInicio,
                DataInicio = torre.DataInicio,
                DataConclusao = torre.DataConclusao,
                InsigniaConquistada = torre.NivelTrofeu > 0,
                NivelTrofeu = torre.NivelTrofeu,
                Dominio = dominio,
                Andares = andares
            };
        }

        private static List<TorreAndarDTO> ObterAndares(lutasEntities db, int torreId)
        {
            List<TorreAndarDTO> andares = db.Database.SqlQuery<TorreAndarDTO>(
                @"SELECT
                    ta.Id,
                    ta.Ordem,
                    ta.IdAdversario,
                    l.Nome AS AdversarioNome,
                    CASE WHEN l.Portrait IS NULL OR l.Portrait = '' THEN '/Imagens/Undefined.png'
                         ELSE '/Imagens/' + l.Portrait END AS Portrait,
                    CASE WHEN l.Stand IS NULL OR l.Stand = '' THEN '/Imagens/Undefined.png'
                         ELSE '/Imagens/' + l.Stand END AS Stand,
                    ta.RatingEloNoSorteio,
                    ta.NivelNoSorteio,
                    ta.Funcao,
                    ta.Status,
                    ta.IdPartida,
                    ta.DataConclusao
                  FROM dbo.TorreAndar ta
                  INNER JOIN dbo.Lutador l ON l.Id = ta.IdAdversario
                  WHERE ta.IdTorre = @torreId
                  ORDER BY ta.Ordem",
                new SqlParameter("@torreId", torreId)).ToList();

            foreach (TorreAndarDTO andar in andares)
            {
                andar.Portrait = ImageUrlVersioner.WithVersion(andar.Portrait);
                andar.Stand = ImageUrlVersioner.WithVersion(andar.Stand);
            }

            return andares;
        }

        private static TorreDominioDTO ObterDominio(
            lutasEntities db,
            int lutadorId,
            string universo)
        {
            DominioRow row = db.Database.SqlQuery<DominioRow>(
                @"SELECT
                    COUNT(DISTINCT atual.Id) AS TotalAtual,
                    COUNT(DISTINCT CASE WHEN vencidos.IdAdversario IS NOT NULL THEN atual.Id END) AS Derrotados
                  FROM dbo.Lutador atual
                  LEFT JOIN
                  (
                      SELECT DISTINCT ta.IdAdversario
                      FROM dbo.Torre t
                      INNER JOIN dbo.TorreAndar ta ON ta.IdTorre = t.Id
                      WHERE t.IdLutador = @lutadorId
                        AND t.UniversoAlvo = @universo
                        AND ta.Status = 'Vencido'
                  ) vencidos ON vencidos.IdAdversario = atual.Id
                  WHERE atual.Id <> @lutadorId
                    AND atual.Universo = @universo",
                new SqlParameter("@lutadorId", lutadorId),
                new SqlParameter("@universo", universo)).Single();

            return new TorreDominioDTO
            {
                Derrotados = row.Derrotados,
                TotalAtual = row.TotalAtual
            };
        }

        private static void RegistrarConquista(lutasEntities db, TorreAndarExecucao andar)
        {
            db.Database.ExecuteSqlCommand(
                @"IF EXISTS
                  (
                      SELECT 1
                      FROM dbo.ConquistaUniverso
                      WHERE IdLutador = @lutadorId AND Universo = @universo
                  )
                  BEGIN
                      UPDATE dbo.ConquistaUniverso
                      SET NivelTrofeu = CASE WHEN NivelTrofeu < @nivel THEN @nivel ELSE NivelTrofeu END,
                          QuantidadeElencoNaConquista = CASE
                              WHEN QuantidadeElencoNaConquista <
                                  (SELECT QuantidadeElencoNoInicio FROM dbo.Torre WHERE Id = @torreId)
                              THEN (SELECT QuantidadeElencoNoInicio FROM dbo.Torre WHERE Id = @torreId)
                              ELSE QuantidadeElencoNaConquista
                          END,
                          DataAtualizacao = SYSDATETIME()
                      WHERE IdLutador = @lutadorId AND Universo = @universo;
                  END
                  ELSE
                  BEGIN
                      INSERT INTO dbo.ConquistaUniverso
                          (IdLutador, Universo, NivelTrofeu, IdTorreOrigem,
                           QuantidadeElencoNaConquista, DataPrimeiraConquista, DataAtualizacao)
                      SELECT @lutadorId, @universo, @nivel, @torreId,
                             QuantidadeElencoNoInicio, SYSDATETIME(), SYSDATETIME()
                      FROM dbo.Torre
                      WHERE Id = @torreId;
                  END",
                new SqlParameter("@lutadorId", andar.IdLutador),
                new SqlParameter("@universo", andar.UniversoAlvo),
                new SqlParameter("@nivel", TrofeuBronze),
                new SqlParameter("@torreId", andar.TorreId));
        }

        private static string Imagem(string arquivo)
        {
            return ImageUrlVersioner.FromImages(arquivo);
        }

        private static string ObterNomeTrofeu(int nivel)
        {
            switch (nivel)
            {
                case 4:
                    return "Platina";
                case 3:
                    return "Ouro";
                case 2:
                    return "Prata";
                default:
                    return "Bronze";
            }
        }

        private sealed class LutadorUniversoRow
        {
            public int Id { get; set; }
            public string Universo { get; set; }
        }

        private sealed class ConquistaRow
        {
            public string Universo { get; set; }
            public int NivelTrofeu { get; set; }
        }

        private sealed class DominioAdversarioRow
        {
            public string Universo { get; set; }
            public int IdAdversario { get; set; }
        }

        private sealed class DominioRow
        {
            public int Derrotados { get; set; }
            public int TotalAtual { get; set; }
        }

        private sealed class TorreRow
        {
            public int Id { get; set; }
            public int IdLutador { get; set; }
            public string LutadorNome { get; set; }
            public string LutadorPortrait { get; set; }
            public string LutadorStand { get; set; }
            public string LutadorUniverso { get; set; }
            public string UniversoAlvo { get; set; }
            public string Tipo { get; set; }
            public string Status { get; set; }
            public int AndarAtual { get; set; }
            public int TotalAndares { get; set; }
            public int QuantidadeElencoNoInicio { get; set; }
            public DateTime DataInicio { get; set; }
            public DateTime? DataConclusao { get; set; }
            public int NivelTrofeu { get; set; }
        }

        private sealed class ConquistaDetalheRow
        {
            public int IdTorreOrigem { get; set; }
            public string Universo { get; set; }
            public int NivelTrofeu { get; set; }
            public int QuantidadeElencoNaConquista { get; set; }
            public DateTime DataPrimeiraConquista { get; set; }
            public DateTime DataAtualizacao { get; set; }
            public string TorreTipo { get; set; }
            public string TorreStatus { get; set; }
            public int TorreTotalAndares { get; set; }
            public DateTime TorreDataInicio { get; set; }
            public DateTime? TorreDataConclusao { get; set; }
        }
    }
}
