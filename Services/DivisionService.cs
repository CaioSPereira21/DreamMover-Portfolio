using DreamMover.DTO;
using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace DreamMover.Services
{
    public sealed class DivisionState
    {
        public int IdLutador { get; set; }
        public int DivisaoAtual { get; set; }
        public int Sequencia { get; set; }
        public int PartidasDivisao { get; set; }
    }

    public sealed class DivisionPlacementState
    {
        public int IdLutador { get; set; }
        public int PartidasRealizadas { get; set; }
        public int Vitorias { get; set; }
        public int Derrotas { get; set; }
        public int DivisaoTeste { get; set; }
        public DateTime CriadoEmUtc { get; set; }
    }

    public sealed class DivisionMatchPlan
    {
        public bool Contabilizar { get; set; }
        public string Tipo { get; set; }
        public int? DesafianteId { get; set; }
        public int? DivisaoOrigem { get; set; }
        public int? DivisaoDestino { get; set; }
    }

    public sealed class DivisionFighterUpdate
    {
        public int Id { get; set; }
        public int Divisao { get; set; }
        public string DivisaoNome { get; set; }
        public int Sequencia { get; set; }
        public int PartidasDivisao { get; set; }
        public bool DesafioPendente { get; set; }
        public string TipoDesafio { get; set; }
        public bool EmColocacao { get; set; }
        public int PartidasColocacao { get; set; }
        public int TotalPartidasColocacao { get; set; }
    }

    public sealed class DivisionMatchResult
    {
        public string Tipo { get; set; }
        public string Resultado { get; set; }
        public string Mensagem { get; set; }
        public List<DivisionFighterUpdate> Lutadores { get; set; }
        public DesafioDivisaoDTO ProximoDesafio { get; set; }
    }

    internal sealed class DivisionMovementRow
    {
        public long Id { get; set; }
        public int? IdPartida { get; set; }
        public string Tipo { get; set; }
        public byte? DivisaoAnterior { get; set; }
        public byte DivisaoNova { get; set; }
        public int RatingElo { get; set; }
        public int SequenciaAnterior { get; set; }
        public int? IdAdversario { get; set; }
        public string AdversarioNome { get; set; }
        public DateTime CriadoEmUtc { get; set; }
    }

    public static class DivisionService
    {
        public const int DivisaoMaisAlta = 0;
        public const int DivisaoMaisBaixa = 6;
        public const int DivisaoMaisBaixaNaColocacao = 5;
        public const int TotalPartidasColocacao = 7;
        public const int SequenciaDecisiva = 5;
        public const int SequenciaExtrema = 10;

        public static int ObterDivisaoInicial(int ratingElo)
        {
            if (ratingElo >= 1300) return 0;
            if (ratingElo >= 1200) return 1;
            if (ratingElo >= 1100) return 2;
            if (ratingElo >= 1000) return 3;
            if (ratingElo >= 900) return 4;
            return 5;
        }

        public static int ObterProximaDivisaoTeste(int divisaoAtual, bool venceu)
        {
            int divisaoNormalizada = Math.Max(DivisaoMaisAlta, Math.Min(DivisaoMaisBaixaNaColocacao, divisaoAtual));
            return venceu
                ? Math.Max(DivisaoMaisAlta, divisaoNormalizada - 1)
                : Math.Min(DivisaoMaisBaixaNaColocacao, divisaoNormalizada + 1);
        }

        public static string ObterNomeDivisao(int divisao)
        {
            return divisao >= DivisaoMaisAlta && divisao <= DivisaoMaisBaixa
                ? divisao + "ª Divisão"
                : "Sem divisão";
        }

        public static int AtualizarSequencia(int sequenciaAtual, bool venceu)
        {
            if (venceu)
                return sequenciaAtual > 0 ? Math.Min(SequenciaExtrema, sequenciaAtual + 1) : 1;

            return sequenciaAtual < 0 ? Math.Max(-SequenciaExtrema, sequenciaAtual - 1) : -1;
        }

        public static string ObterTipoDesafio(DivisionState estado)
        {
            if (estado == null)
                return null;

            if (estado.DivisaoAtual > 1 && estado.Sequencia >= SequenciaDecisiva)
                return "PROMOCAO";
            if (estado.DivisaoAtual < 5 && estado.Sequencia <= -SequenciaDecisiva)
                return "REBAIXAMENTO";

            return null;
        }

        public static int? ObterDivisaoMovimentoExtremo(DivisionState estado)
        {
            if (estado == null)
                return null;

            if (estado.DivisaoAtual == 1 && estado.Sequencia >= SequenciaExtrema)
                return 0;
            if (estado.DivisaoAtual == 5 && estado.Sequencia <= -SequenciaExtrema)
                return 6;

            return null;
        }

        public static int? ObterDivisaoDestino(DivisionState estado)
        {
            string tipo = ObterTipoDesafio(estado);
            if (tipo == "PROMOCAO") return estado.DivisaoAtual - 1;
            if (tipo == "REBAIXAMENTO") return estado.DivisaoAtual + 1;
            return null;
        }

        public static void GarantirColocacoesIniciais(lutasEntities db)
        {
            if (db == null) throw new ArgumentNullException("db");

            const string sql = @"
INSERT INTO dbo.LutadorColocacao
    (IdLutador, PartidasRealizadas, Vitorias, Derrotas, DivisaoTeste)
SELECT l.Id, 0, 0, 0, 3
FROM dbo.Lutador l
WHERE NOT EXISTS (SELECT 1 FROM dbo.LutadorDivisao d WHERE d.IdLutador = l.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.LutadorColocacao c WHERE c.IdLutador = l.Id);";

            try
            {
                db.Database.ExecuteSqlCommand(sql);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                throw new InvalidOperationException(
                    "A colocação competitiva ainda não existe no banco. Execute App_Data/Migrations/20260924_AddColocacaoE6Divisao.sql.", ex);
            }
        }

        public static Dictionary<int, DivisionPlacementState> ObterColocacoes(
            lutasEntities db,
            IEnumerable<int> ids = null)
        {
            GarantirColocacoesIniciais(db);
            List<int> filtro = ids == null ? null : ids.Distinct().ToList();
            string sql = @"SELECT IdLutador, PartidasRealizadas, Vitorias, Derrotas,
       CONVERT(int, DivisaoTeste) DivisaoTeste, CriadoEmUtc
FROM dbo.LutadorColocacao
WHERE FinalizadaEmUtc IS NULL";
            object[] parametros = new object[0];

            if (filtro != null && filtro.Count > 0)
            {
                var nomes = new List<string>();
                parametros = filtro.Select((id, indice) =>
                {
                    string nome = "@p" + indice;
                    nomes.Add(nome);
                    return (object)new SqlParameter(nome, id);
                }).ToArray();
                sql += " AND IdLutador IN (" + string.Join(",", nomes) + ")";
            }

            return db.Database.SqlQuery<DivisionPlacementState>(sql, parametros)
                .ToList()
                .ToDictionary(x => x.IdLutador);
        }

        public static Dictionary<int, DivisionState> ObterEstados(lutasEntities db, IEnumerable<int> ids = null)
        {
            GarantirColocacoesIniciais(db);
            List<int> filtro = ids == null ? null : ids.Distinct().ToList();
            string sql = "SELECT IdLutador, CONVERT(int, DivisaoAtual) DivisaoAtual, Sequencia, PartidasDivisao FROM dbo.LutadorDivisao";
            object[] parametros = new object[0];

            if (filtro != null && filtro.Count > 0)
            {
                var nomes = new List<string>();
                parametros = filtro.Select((id, indice) =>
                {
                    string nome = "@p" + indice;
                    nomes.Add(nome);
                    return (object)new SqlParameter(nome, id);
                }).ToArray();
                sql += " WHERE IdLutador IN (" + string.Join(",", nomes) + ")";
            }

            return db.Database.SqlQuery<DivisionState>(sql, parametros)
                .ToList()
                .ToDictionary(x => x.IdLutador);
        }

        public static void PreencherLutadores(lutasEntities db, IList<LutadorDTO> lutadores)
        {
            if (lutadores == null || lutadores.Count == 0)
                return;

            List<int> ids = lutadores.Select(x => x.Id).ToList();
            Dictionary<int, DivisionState> estados = ObterEstados(db, ids);
            Dictionary<int, DivisionPlacementState> colocacoes = ObterColocacoes(db, ids);
            foreach (LutadorDTO lutador in lutadores)
            {
                DivisionState estado;
                if (estados.TryGetValue(lutador.Id, out estado))
                {
                    AplicarEstado(lutador, estado);
                    continue;
                }

                DivisionPlacementState colocacao;
                if (colocacoes.TryGetValue(lutador.Id, out colocacao))
                    AplicarColocacao(lutador, colocacao);
            }
        }

        public static void PreencherLutador(lutasEntities db, LutadorDTO lutador)
        {
            if (lutador == null) return;
            PreencherLutadores(db, new List<LutadorDTO> { lutador });
            lutador.HistoricoDivisao = ObterHistorico(db, lutador.Id);
        }

        public static DivisionMatchPlan PrepararPartida(
            lutasEntities db,
            int lutadorCasaId,
            int lutadorVisitanteId,
            bool partidaDivisao,
            int? desafianteId)
        {
            if (!partidaDivisao)
                return new DivisionMatchPlan { Contabilizar = false };

            int[] participantes = { lutadorCasaId, lutadorVisitanteId };
            Dictionary<int, DivisionState> estados = ObterEstados(db, participantes);
            Dictionary<int, DivisionPlacementState> colocacoes = ObterColocacoes(db, participantes);

            if (desafianteId.HasValue)
            {
                if (desafianteId.Value != lutadorCasaId && desafianteId.Value != lutadorVisitanteId)
                    throw new InvalidOperationException("O desafiante não participa desta luta.");

                DivisionPlacementState colocacao;
                if (colocacoes.TryGetValue(desafianteId.Value, out colocacao))
                {
                    int chefeId = desafianteId.Value == lutadorCasaId ? lutadorVisitanteId : lutadorCasaId;
                    DivisionState chefeColocacao;
                    if (!estados.TryGetValue(chefeId, out chefeColocacao))
                        throw new InvalidOperationException("A colocação exige um adversário que já possua divisão.");

                    DesafioDivisaoDTO desafioEsperado = CriarDesafioColocacao(db, colocacao);
                    if (desafioEsperado == null || desafioEsperado.ChefeId != chefeId)
                        throw new InvalidOperationException("O adversário selecionado não corresponde ao próximo teste de colocação.");

                    return new DivisionMatchPlan
                    {
                        Contabilizar = true,
                        Tipo = "COLOCACAO",
                        DesafianteId = desafianteId.Value,
                        DivisaoOrigem = null,
                        DivisaoDestino = chefeColocacao.DivisaoAtual
                    };
                }

                if (!estados.ContainsKey(lutadorCasaId) || !estados.ContainsKey(lutadorVisitanteId))
                    throw new InvalidOperationException("Não foi possível carregar a divisão dos dois lutadores.");

                DivisionState desafiante = estados[desafianteId.Value];
                DivisionState chefe = estados[desafiante.IdLutador == lutadorCasaId ? lutadorVisitanteId : lutadorCasaId];
                string tipo = ObterTipoDesafio(desafiante);
                int? destino = ObterDivisaoDestino(desafiante);

                if (tipo == null || !destino.HasValue)
                    throw new InvalidOperationException("O personagem ainda não possui uma luta decisiva disponível.");
                if (chefe.DivisaoAtual != destino.Value)
                    throw new InvalidOperationException("O chefe precisa pertencer à divisão pretendida.");

                return new DivisionMatchPlan
                {
                    Contabilizar = true,
                    Tipo = tipo,
                    DesafianteId = desafiante.IdLutador,
                    DivisaoOrigem = desafiante.DivisaoAtual,
                    DivisaoDestino = destino.Value
                };
            }

            if (colocacoes.Count > 0)
                throw new InvalidOperationException("Personagens em colocação só podem disputar o próximo teste agendado.");
            if (!estados.ContainsKey(lutadorCasaId) || !estados.ContainsKey(lutadorVisitanteId))
                throw new InvalidOperationException("Não foi possível carregar a divisão dos dois lutadores.");

            DivisionState casa = estados[lutadorCasaId];
            DivisionState visitante = estados[lutadorVisitanteId];

            if (ObterTipoDesafio(casa) != null || ObterTipoDesafio(visitante) != null)
                throw new InvalidOperationException("Um dos lutadores possui uma luta decisiva pendente.");
            if (casa.DivisaoAtual != visitante.DivisaoAtual)
                throw new InvalidOperationException("Partidas regulares de divisão exigem lutadores da mesma divisão.");

            return new DivisionMatchPlan
            {
                Contabilizar = true,
                Tipo = "REGULAR",
                DivisaoOrigem = casa.DivisaoAtual,
                DivisaoDestino = casa.DivisaoAtual
            };
        }

        public static DivisionMatchResult RegistrarResultado(
            lutasEntities db,
            DivisionMatchPlan plano,
            int partidaId,
            Lutador vencedor,
            Lutador perdedor)
        {
            if (plano == null || !plano.Contabilizar)
                return null;

            int[] participantes = { vencedor.Id, perdedor.Id };
            string resultado = "REGULAR";
            string mensagem = null;

            if (plano.Tipo == "COLOCACAO")
            {
                Dictionary<int, DivisionPlacementState> colocacoes = ObterColocacoes(db, participantes);
                DivisionPlacementState colocacao;
                if (!plano.DesafianteId.HasValue
                    || !colocacoes.TryGetValue(plano.DesafianteId.Value, out colocacao))
                {
                    throw new InvalidOperationException("A colocação deste personagem não está mais pendente.");
                }

                Lutador desafianteDb = vencedor.Id == colocacao.IdLutador ? vencedor : perdedor;
                Lutador chefeDb = vencedor.Id == colocacao.IdLutador ? perdedor : vencedor;
                bool desafianteVenceu = vencedor.Id == colocacao.IdLutador;
                int partidasRealizadas = colocacao.PartidasRealizadas + 1;
                int divisaoTeste = ObterProximaDivisaoTeste(colocacao.DivisaoTeste, desafianteVenceu);
                bool finalizou = partidasRealizadas >= TotalPartidasColocacao;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.LutadorColocacao
SET PartidasRealizadas = @p1,
    Vitorias = Vitorias + @p2,
    Derrotas = Derrotas + @p3,
    DivisaoTeste = @p4,
    AtualizadoEmUtc = SYSUTCDATETIME(),
    FinalizadaEmUtc = CASE WHEN @p5 = 1 THEN SYSUTCDATETIME() ELSE NULL END
WHERE IdLutador = @p0 AND FinalizadaEmUtc IS NULL",
                    colocacao.IdLutador,
                    partidasRealizadas,
                    desafianteVenceu ? 1 : 0,
                    desafianteVenceu ? 0 : 1,
                    divisaoTeste,
                    finalizou ? 1 : 0);

                if (finalizou)
                {
                    int divisaoInicial = Math.Min(DivisaoMaisBaixaNaColocacao, ObterDivisaoInicial(desafianteDb.RatingElo));
                    db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.LutadorDivisao (IdLutador, DivisaoAtual, Sequencia, PartidasDivisao)
SELECT @p0, @p1, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.LutadorDivisao WHERE IdLutador = @p0)",
                        colocacao.IdLutador,
                        divisaoInicial);
                    RegistrarMovimentacao(db, colocacao.IdLutador, partidaId, "COLOCACAO",
                        null, divisaoInicial, desafianteDb.RatingElo, 0, chefeDb.Id);

                    resultado = "COLOCADO";
                    mensagem = desafianteDb.Nome + " concluiu a colocação na "
                        + ObterNomeDivisao(divisaoInicial) + ".";
                }
                else
                {
                    resultado = "EM_COLOCACAO";
                    mensagem = desafianteDb.Nome + " concluiu " + partidasRealizadas + " de "
                        + TotalPartidasColocacao + " partidas de colocação.";
                }
            }
            else
            {
                Dictionary<int, DivisionState> estados = ObterEstados(db, participantes);
                if (!estados.ContainsKey(vencedor.Id) || !estados.ContainsKey(perdedor.Id))
                    throw new InvalidOperationException("Não foi possível carregar a divisão dos dois lutadores.");

                if (plano.Tipo == "REGULAR")
                {
                    AtualizarEstadoRegular(db, estados[vencedor.Id], true, partidaId, vencedor, perdedor, ref resultado, ref mensagem);
                    AtualizarEstadoRegular(db, estados[perdedor.Id], false, partidaId, perdedor, vencedor, ref resultado, ref mensagem);
                }
                else
                {
                    DivisionState desafiante = estados[plano.DesafianteId.Value];
                    Lutador desafianteDb = vencedor.Id == desafiante.IdLutador ? vencedor : perdedor;
                    Lutador chefeDb = vencedor.Id == desafiante.IdLutador ? perdedor : vencedor;
                    bool desafianteVenceu = vencedor.Id == desafiante.IdLutador;
                    bool movimentou = plano.Tipo == "PROMOCAO" ? desafianteVenceu : !desafianteVenceu;

                    if (movimentou)
                    {
                        resultado = plano.Tipo == "PROMOCAO" ? "PROMOVIDO" : "REBAIXADO";
                        AtualizarDivisao(db, desafiante.IdLutador, plano.DivisaoDestino.Value, 0, true);
                        RegistrarMovimentacao(db, desafiante.IdLutador, partidaId, plano.Tipo,
                            plano.DivisaoOrigem, plano.DivisaoDestino.Value, desafianteDb.RatingElo,
                            desafiante.Sequencia, chefeDb.Id);
                        mensagem = desafianteDb.Nome + " foi "
                            + (resultado == "PROMOVIDO" ? "promovido para a " : "rebaixado para a ")
                            + ObterNomeDivisao(plano.DivisaoDestino.Value) + ".";
                    }
                    else
                    {
                        resultado = "MANTEVE";
                        AtualizarDivisao(db, desafiante.IdLutador, desafiante.DivisaoAtual, 0, true);
                        mensagem = desafianteDb.Nome + " permanece na " + ObterNomeDivisao(desafiante.DivisaoAtual) + ".";
                    }
                }
            }

            db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PartidaDivisao
    (IdPartida, Tipo, IdDesafiante, DivisaoOrigem, DivisaoDestino, Resultado)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5)",
                partidaId,
                plano.Tipo,
                (object)plano.DesafianteId ?? DBNull.Value,
                (object)plano.DivisaoOrigem ?? DBNull.Value,
                (object)plano.DivisaoDestino ?? DBNull.Value,
                resultado);

            var retorno = new DivisionMatchResult
            {
                Tipo = plano.Tipo,
                Resultado = resultado,
                Mensagem = mensagem,
                Lutadores = CriarAtualizacoes(db, participantes)
            };

            retorno.ProximoDesafio = ObterPrimeiroDesafioPendente(db);
            return retorno;
        }

        public static DesafioDivisaoDTO ObterPrimeiroDesafioPendente(lutasEntities db)
        {
            foreach (DivisionPlacementState colocacao in ObterColocacoes(db).Values
                .OrderBy(x => x.CriadoEmUtc)
                .ThenBy(x => x.IdLutador))
            {
                DesafioDivisaoDTO desafioColocacao = CriarDesafioColocacao(db, colocacao);
                if (desafioColocacao != null)
                    return desafioColocacao;
            }

            DivisionState estado = ObterEstados(db).Values
                .Where(x => ObterTipoDesafio(x) != null)
                .OrderByDescending(x => Math.Abs(x.Sequencia))
                .ThenBy(x => x.IdLutador)
                .FirstOrDefault();
            return CriarDesafioPendente(db, estado);
        }

        public static List<MovimentacaoDivisaoDTO> ObterHistorico(lutasEntities db, int lutadorId)
        {
            GarantirColocacoesIniciais(db);
            const string sql = @"
SELECT m.Id, m.IdPartida, m.Tipo, m.DivisaoAnterior, m.DivisaoNova, m.RatingElo,
       m.SequenciaAnterior, m.IdAdversario, a.Nome AdversarioNome, m.CriadoEmUtc
FROM dbo.MovimentacaoDivisao m
LEFT JOIN dbo.Lutador a ON a.Id = m.IdAdversario
WHERE m.IdLutador = @p0
ORDER BY m.CriadoEmUtc DESC, m.Id DESC";

            return db.Database.SqlQuery<DivisionMovementRow>(sql, lutadorId).ToList()
                .Select(x => new MovimentacaoDivisaoDTO
                {
                    Id = x.Id,
                    PartidaId = x.IdPartida,
                    Tipo = x.Tipo,
                    DivisaoAnterior = x.DivisaoAnterior,
                    DivisaoAnteriorNome = x.DivisaoAnterior.HasValue ? ObterNomeDivisao(x.DivisaoAnterior.Value) : null,
                    DivisaoNova = x.DivisaoNova,
                    DivisaoNovaNome = ObterNomeDivisao(x.DivisaoNova),
                    RatingElo = x.RatingElo,
                    SequenciaAnterior = x.SequenciaAnterior,
                    AdversarioId = x.IdAdversario,
                    AdversarioNome = x.AdversarioNome,
                    Data = x.CriadoEmUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
                }).ToList();
        }

        private static void AplicarEstado(LutadorDTO lutador, DivisionState estado)
        {
            lutador.Divisao = estado.DivisaoAtual;
            lutador.DivisaoNome = ObterNomeDivisao(estado.DivisaoAtual);
            lutador.SequenciaDivisao = estado.Sequencia;
            lutador.PartidasDivisao = estado.PartidasDivisao;
            lutador.TipoDesafioDivisao = ObterTipoDesafio(estado);
            lutador.DesafioDivisaoPendente = lutador.TipoDesafioDivisao != null;
            lutador.DivisaoDestino = ObterDivisaoDestino(estado);
            lutador.DivisaoDestinoNome = lutador.DivisaoDestino.HasValue
                ? ObterNomeDivisao(lutador.DivisaoDestino.Value)
                : null;
            lutador.EmColocacao = false;
            lutador.PartidasColocacao = 0;
            lutador.TotalPartidasColocacao = TotalPartidasColocacao;
            lutador.DivisaoTesteColocacao = null;
        }

        private static void AplicarColocacao(LutadorDTO lutador, DivisionPlacementState colocacao)
        {
            lutador.Divisao = -1;
            lutador.DivisaoNome = "Em colocação";
            lutador.SequenciaDivisao = 0;
            lutador.PartidasDivisao = 0;
            lutador.TipoDesafioDivisao = "COLOCACAO";
            lutador.DesafioDivisaoPendente = true;
            lutador.DivisaoDestino = colocacao.DivisaoTeste;
            lutador.DivisaoDestinoNome = ObterNomeDivisao(colocacao.DivisaoTeste);
            lutador.EmColocacao = true;
            lutador.PartidasColocacao = colocacao.PartidasRealizadas;
            lutador.TotalPartidasColocacao = TotalPartidasColocacao;
            lutador.DivisaoTesteColocacao = colocacao.DivisaoTeste;
        }

        private static DivisionFighterUpdate CriarAtualizacao(DivisionState estado)
        {
            return new DivisionFighterUpdate
            {
                Id = estado.IdLutador,
                Divisao = estado.DivisaoAtual,
                DivisaoNome = ObterNomeDivisao(estado.DivisaoAtual),
                Sequencia = estado.Sequencia,
                PartidasDivisao = estado.PartidasDivisao,
                DesafioPendente = ObterTipoDesafio(estado) != null,
                TipoDesafio = ObterTipoDesafio(estado),
                EmColocacao = false,
                PartidasColocacao = 0,
                TotalPartidasColocacao = TotalPartidasColocacao
            };
        }

        private static DivisionFighterUpdate CriarAtualizacao(DivisionPlacementState colocacao)
        {
            return new DivisionFighterUpdate
            {
                Id = colocacao.IdLutador,
                Divisao = -1,
                DivisaoNome = "Em colocação",
                Sequencia = 0,
                PartidasDivisao = 0,
                DesafioPendente = true,
                TipoDesafio = "COLOCACAO",
                EmColocacao = true,
                PartidasColocacao = colocacao.PartidasRealizadas,
                TotalPartidasColocacao = TotalPartidasColocacao
            };
        }

        private static List<DivisionFighterUpdate> CriarAtualizacoes(lutasEntities db, IEnumerable<int> ids)
        {
            List<int> listaIds = ids.Distinct().ToList();
            Dictionary<int, DivisionState> estados = ObterEstados(db, listaIds);
            Dictionary<int, DivisionPlacementState> colocacoes = ObterColocacoes(db, listaIds);
            var atualizacoes = new List<DivisionFighterUpdate>();

            foreach (int id in listaIds)
            {
                DivisionState estado;
                if (estados.TryGetValue(id, out estado))
                {
                    atualizacoes.Add(CriarAtualizacao(estado));
                    continue;
                }

                DivisionPlacementState colocacao;
                if (colocacoes.TryGetValue(id, out colocacao))
                    atualizacoes.Add(CriarAtualizacao(colocacao));
            }

            return atualizacoes;
        }

        private static void AtualizarEstadoRegular(
            lutasEntities db,
            DivisionState estado,
            bool venceu,
            int partidaId,
            Lutador lutador,
            Lutador adversario,
            ref string resultado,
            ref string mensagem)
        {
            int sequencia = AtualizarSequencia(estado.Sequencia, venceu);
            int divisao = estado.DivisaoAtual;
            int? novaDivisaoExtrema = ObterDivisaoMovimentoExtremo(new DivisionState
            {
                IdLutador = estado.IdLutador,
                DivisaoAtual = divisao,
                Sequencia = sequencia,
                PartidasDivisao = estado.PartidasDivisao
            });

            if (novaDivisaoExtrema.HasValue)
            {
                int novaDivisao = novaDivisaoExtrema.Value;
                string tipo = sequencia > 0 ? "PROMOCAO" : "REBAIXAMENTO";
                resultado = sequencia > 0 ? "PROMOVIDO" : "REBAIXADO";
                AtualizarDivisao(db, estado.IdLutador, novaDivisao, 0, true);
                RegistrarMovimentacao(db, estado.IdLutador, partidaId, tipo, divisao, novaDivisao,
                    lutador.RatingElo, sequencia, adversario.Id);
                mensagem = lutador.Nome + " foi "
                    + (sequencia > 0 ? "promovido para a " : "rebaixado para a ")
                    + ObterNomeDivisao(novaDivisao) + ".";
                return;
            }

            AtualizarDivisao(db, estado.IdLutador, divisao, sequencia, true);
        }

        private static void AtualizarDivisao(
            lutasEntities db,
            int lutadorId,
            int divisao,
            int sequencia,
            bool incrementarPartidas)
        {
            db.Database.ExecuteSqlCommand(@"
UPDATE dbo.LutadorDivisao
SET DivisaoAtual = @p1,
    Sequencia = @p2,
    PartidasDivisao = PartidasDivisao + @p3,
    AtualizadoEmUtc = SYSUTCDATETIME()
WHERE IdLutador = @p0", lutadorId, divisao, sequencia, incrementarPartidas ? 1 : 0);
        }

        private static void RegistrarMovimentacao(
            lutasEntities db,
            int lutadorId,
            int partidaId,
            string tipo,
            int? divisaoAnterior,
            int divisaoNova,
            int ratingElo,
            int sequencia,
            int adversarioId)
        {
            db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.MovimentacaoDivisao
    (IdLutador, IdPartida, Tipo, DivisaoAnterior, DivisaoNova, RatingElo, SequenciaAnterior, IdAdversario)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                lutadorId, partidaId, tipo, (object)divisaoAnterior ?? DBNull.Value,
                divisaoNova, ratingElo, sequencia, adversarioId);
        }

        private static DesafioDivisaoDTO CriarDesafioColocacao(
            lutasEntities db,
            DivisionPlacementState colocacao)
        {
            if (colocacao == null)
                return null;

            List<int> adversariosAnteriores = db.Database.SqlQuery<int>(@"
SELECT CASE
           WHEN p.IdLutadorCasa = @p0 THEN p.IdLutadorVisitante
           ELSE p.IdLutadorCasa
       END
FROM dbo.PartidaDivisao pd
INNER JOIN dbo.Partida p ON p.Id = pd.IdPartida
WHERE pd.Tipo = 'COLOCACAO'
  AND pd.IdDesafiante = @p0
  AND (p.IdLutadorCasa = @p0 OR p.IdLutadorVisitante = @p0)", colocacao.IdLutador).ToList();

            List<Lutador> candidatos = db.Lutador
                .Where(x => x.Id != colocacao.IdLutador)
                .ToList();
            Dictionary<int, DivisionState> estados = ObterEstados(db, candidatos.Select(x => x.Id));
            var adversariosUsados = new HashSet<int>(adversariosAnteriores);
            int ratingReferencia = ObterRatingReferenciaColocacao(colocacao.DivisaoTeste);

            Lutador chefe = candidatos
                .Where(x => !adversariosUsados.Contains(x.Id)
                    && estados.ContainsKey(x.Id)
                    && estados[x.Id].DivisaoAtual <= DivisaoMaisBaixaNaColocacao)
                .OrderBy(x => Math.Abs(estados[x.Id].DivisaoAtual - colocacao.DivisaoTeste))
                .ThenBy(x => Math.Abs(x.RatingElo - ratingReferencia))
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            Lutador desafiante = db.Lutador.FirstOrDefault(x => x.Id == colocacao.IdLutador);

            if (chefe == null || desafiante == null)
                return null;

            int divisaoChefe = estados[chefe.Id].DivisaoAtual;
            return new DesafioDivisaoDTO
            {
                DesafianteId = desafiante.Id,
                DesafianteNome = desafiante.Nome,
                ChefeId = chefe.Id,
                ChefeNome = chefe.Nome,
                Tipo = "COLOCACAO",
                DivisaoOrigem = -1,
                DivisaoOrigemNome = "Em colocação",
                DivisaoDestino = divisaoChefe,
                DivisaoDestinoNome = ObterNomeDivisao(divisaoChefe),
                PartidasColocacao = colocacao.PartidasRealizadas,
                TotalPartidasColocacao = TotalPartidasColocacao
            };
        }

        private static int ObterRatingReferenciaColocacao(int divisao)
        {
            switch (divisao)
            {
                case 0: return 1350;
                case 1: return 1250;
                case 2: return 1150;
                case 3: return 1050;
                case 4: return 950;
                default: return 850;
            }
        }

        private static DesafioDivisaoDTO CriarDesafioPendente(lutasEntities db, DivisionState estado)
        {
            if (estado == null)
                return null;

            string tipo = ObterTipoDesafio(estado);
            int? destino = ObterDivisaoDestino(estado);
            if (tipo == null || !destino.HasValue)
                return null;

            int limite = ObterLimiteEntrada(destino.Value, tipo);
            List<Lutador> candidatos = db.Lutador
                .Where(x => x.Id != estado.IdLutador)
                .ToList();
            Dictionary<int, DivisionState> estados = ObterEstados(db, candidatos.Select(x => x.Id));

            Lutador chefe = candidatos
                .Where(x => estados.ContainsKey(x.Id) && estados[x.Id].DivisaoAtual == destino.Value)
                .OrderBy(x => Math.Abs(x.RatingElo - limite))
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            Lutador desafiante = db.Lutador.FirstOrDefault(x => x.Id == estado.IdLutador);

            if (chefe == null || desafiante == null)
                return null;

            return new DesafioDivisaoDTO
            {
                DesafianteId = desafiante.Id,
                DesafianteNome = desafiante.Nome,
                ChefeId = chefe.Id,
                ChefeNome = chefe.Nome,
                Tipo = tipo,
                DivisaoOrigem = estado.DivisaoAtual,
                DivisaoOrigemNome = ObterNomeDivisao(estado.DivisaoAtual),
                DivisaoDestino = destino.Value,
                DivisaoDestinoNome = ObterNomeDivisao(destino.Value)
            };
        }

        private static int ObterLimiteEntrada(int divisao, string tipo)
        {
            int minimo;
            switch (divisao)
            {
                case 0: minimo = 1300; break;
                case 1: minimo = 1200; break;
                case 2: minimo = 1100; break;
                case 3: minimo = 1000; break;
                case 4: minimo = 900; break;
                case 5: minimo = 650; break;
                default: minimo = 100; break;
            }

            if (tipo == "PROMOCAO")
                return minimo;

            int maximo;
            switch (divisao)
            {
                case 1: maximo = 1299; break;
                case 2: maximo = 1199; break;
                case 3: maximo = 1099; break;
                case 4: maximo = 999; break;
                case 5: maximo = 899; break;
                default: maximo = 649; break;
            }
            return maximo;
        }
    }
}
