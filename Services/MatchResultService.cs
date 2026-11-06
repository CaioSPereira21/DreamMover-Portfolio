using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;

namespace DreamMover.Services
{
    public sealed class MatchParticipants
    {
        public int LutadorCasaId { get; set; }
        public string LutadorCasaNome { get; set; }
        public string LutadorCasaReferencia { get; set; }
        public string LutadorCasaUniverso { get; set; }
        public int LutadorVisitanteId { get; set; }
        public string LutadorVisitanteNome { get; set; }
        public string LutadorVisitanteReferencia { get; set; }
        public string LutadorVisitanteUniverso { get; set; }
    }

    public sealed class MatchRegistrationResult
    {
        public int PartidaId { get; set; }
        public int VencedorId { get; set; }
        public int PerdedorId { get; set; }
        public MatchProgressionResult Progressao { get; set; }
        public DivisionMatchResult Divisao { get; set; }
    }

    public static class MatchResultService
    {
        public static MatchParticipants CarregarParticipantes(
            lutasEntities db,
            int lutadorCasaId,
            int lutadorVisitanteId)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            if (lutadorCasaId <= 0 || lutadorVisitanteId <= 0)
                throw new ArgumentException("Selecione dois lutadores validos.");

            if (lutadorCasaId == lutadorVisitanteId)
                throw new ArgumentException("Um lutador nao pode enfrentar a si mesmo.");

            List<Lutador> lutadores = db.Lutador
                .AsNoTracking()
                .Where(x => x.Id == lutadorCasaId || x.Id == lutadorVisitanteId)
                .ToList();

            Lutador lutadorCasa = lutadores.FirstOrDefault(x => x.Id == lutadorCasaId);
            Lutador lutadorVisitante = lutadores.FirstOrDefault(x => x.Id == lutadorVisitanteId);

            if (lutadorCasa == null || lutadorVisitante == null)
                throw new ArgumentException("Um dos lutadores selecionados nao existe mais.");

            if (string.IsNullOrWhiteSpace(lutadorCasa.Nome) || string.IsNullOrWhiteSpace(lutadorVisitante.Nome))
                throw new ArgumentException("Um dos lutadores nao possui nome valido para iniciar a partida.");

            return new MatchParticipants
            {
                LutadorCasaId = lutadorCasa.Id,
                LutadorCasaNome = lutadorCasa.Nome,
                LutadorCasaReferencia = string.IsNullOrWhiteSpace(lutadorCasa.PastaPersonagem) ? lutadorCasa.Nome : lutadorCasa.PastaPersonagem,
                LutadorCasaUniverso = lutadorCasa.Universo,
                LutadorVisitanteId = lutadorVisitante.Id,
                LutadorVisitanteNome = lutadorVisitante.Nome,
                LutadorVisitanteReferencia = string.IsNullOrWhiteSpace(lutadorVisitante.PastaPersonagem) ? lutadorVisitante.Nome : lutadorVisitante.PastaPersonagem,
                LutadorVisitanteUniverso = lutadorVisitante.Universo
            };
        }

        public static MatchRegistrationResult RegistrarResultado(
            lutasEntities db,
            int lutadorCasaId,
            int lutadorVisitanteId,
            int pontosCasa,
            int pontosVisitante,
            int? cenarioId = null,
            DivisionMatchPlan planoDivisao = null)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            if (lutadorCasaId == lutadorVisitanteId)
                throw new ArgumentException("Um lutador nao pode enfrentar a si mesmo.");

            ProgressionCalculator.ValidarPlacar(pontosCasa, pontosVisitante);

            using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    List<Lutador> lutadores = db.Lutador
                        .Where(x => x.Id == lutadorCasaId || x.Id == lutadorVisitanteId)
                        .ToList();

                    Lutador lutadorCasa = lutadores.FirstOrDefault(x => x.Id == lutadorCasaId);
                    Lutador lutadorVisitante = lutadores.FirstOrDefault(x => x.Id == lutadorVisitanteId);

                    if (lutadorCasa == null || lutadorVisitante == null)
                        throw new ArgumentException("Um dos lutadores selecionados nao existe mais.");

                    if (cenarioId.HasValue && !db.Cenario.Any(x => x.Id == cenarioId.Value))
                        throw new ArgumentException("O cenario selecionado nao existe mais.");

                    int fatorKCasa = planoDivisao != null
                        && planoDivisao.Tipo == "COLOCACAO"
                        && planoDivisao.DesafianteId == lutadorCasa.Id
                        ? ProgressionCalculator.FatorKColocacao
                        : ProgressionCalculator.FatorK;
                    int fatorKVisitante = planoDivisao != null
                        && planoDivisao.Tipo == "COLOCACAO"
                        && planoDivisao.DesafianteId == lutadorVisitante.Id
                        ? ProgressionCalculator.FatorKColocacao
                        : ProgressionCalculator.FatorK;

                    MatchProgressionResult progressao = ProgressionCalculator.Calcular(
                        CriarEstado(lutadorCasa),
                        CriarEstado(lutadorVisitante),
                        pontosCasa,
                        pontosVisitante,
                        fatorKCasa,
                        fatorKVisitante);

                    AplicarProgressao(lutadorCasa, progressao.Casa);
                    AplicarProgressao(lutadorVisitante, progressao.Visitante);

                    bool casaVenceu = pontosCasa > pontosVisitante;
                    var partida = new Partida
                    {
                        IdLutadorCasa = lutadorCasa.Id,
                        IdLutadorVisitante = lutadorVisitante.Id,
                        IdVencedor = casaVenceu ? lutadorCasa.Id : lutadorVisitante.Id,
                        idPerdedor = casaVenceu ? lutadorVisitante.Id : lutadorCasa.Id,
                        PontosVencedor = casaVenceu ? pontosCasa : pontosVisitante,
                        PontosPerdedor = casaVenceu ? pontosVisitante : pontosCasa,
                        IdCenario = cenarioId,
                        DataPartida = DateTime.Now
                    };

                    db.Partida.Add(partida);
                    db.SaveChanges();

                    DivisionMatchResult resultadoDivisao = DivisionService.RegistrarResultado(
                        db,
                        planoDivisao,
                        partida.Id,
                        casaVenceu ? lutadorCasa : lutadorVisitante,
                        casaVenceu ? lutadorVisitante : lutadorCasa);
                    transaction.Commit();

                    return new MatchRegistrationResult
                    {
                        PartidaId = partida.Id,
                        VencedorId = partida.IdVencedor.Value,
                        PerdedorId = partida.idPerdedor.Value,
                        Progressao = progressao,
                        Divisao = resultadoDivisao
                    };
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static FighterProgressionState CriarEstado(Lutador lutador)
        {
            return new FighterProgressionState
            {
                Id = lutador.Id,
                Nome = lutador.Nome,
                Experiencia = NormalizarExperiencia(lutador.Exp),
                Nivel = lutador.Nivel ?? 1,
                RatingElo = lutador.RatingElo
            };
        }

        private static long NormalizarExperiencia(double? experiencia)
        {
            if (!experiencia.HasValue
                || double.IsNaN(experiencia.Value)
                || double.IsInfinity(experiencia.Value)
                || experiencia.Value <= 0)
            {
                return 0L;
            }

            return (long)Math.Min(long.MaxValue, Math.Floor(experiencia.Value));
        }

        private static void AplicarProgressao(Lutador lutador, FighterProgressionResult resultado)
        {
            lutador.Exp = resultado.ExperienciaAtual;
            lutador.Nivel = resultado.NivelAtual;
            lutador.RatingElo = resultado.RatingEloAtual;
        }
    }
}
