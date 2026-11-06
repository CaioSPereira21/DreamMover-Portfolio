using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamMover.Services
{
    public sealed class EloHistoryMatch
    {
        public int Id { get; set; }
        public DateTime? DataPartida { get; set; }
        public int? IdVencedor { get; set; }
        public int? IdPerdedor { get; set; }
        public int? PontosVencedor { get; set; }
        public int? PontosPerdedor { get; set; }
    }

    public sealed class EloHistoryPoint
    {
        public int PartidaId { get; set; }
        public DateTime? DataPartida { get; set; }
        public int AdversarioId { get; set; }
        public bool Vitoria { get; set; }
        public int RatingEloAnterior { get; set; }
        public int RatingEloAtual { get; set; }
        public int RatingEloAdversario { get; set; }
        public int VariacaoRatingElo { get; set; }
        public int? PontosLutador { get; set; }
        public int? PontosAdversario { get; set; }
    }

    public static class EloHistoryService
    {
        public static List<EloHistoryPoint> Calcular(
            IEnumerable<EloHistoryMatch> partidas,
            int lutadorId,
            int? ratingEloAtualEsperado = null)
        {
            var historico = new List<EloHistoryPoint>();
            var ratings = new Dictionary<int, int>();

            if (partidas == null || lutadorId <= 0)
                return historico;

            foreach (EloHistoryMatch partida in partidas
                .Where(EhPartidaValida)
                .OrderBy(x => x.DataPartida ?? DateTime.MinValue)
                .ThenBy(x => x.Id))
            {
                int vencedorId = partida.IdVencedor.Value;
                int perdedorId = partida.IdPerdedor.Value;
                int ratingVencedor = ObterRating(ratings, vencedorId);
                int ratingPerdedor = ObterRating(ratings, perdedorId);

                MatchProgressionResult progressao = ProgressionCalculator.Calcular(
                    CriarEstado(vencedorId, ratingVencedor),
                    CriarEstado(perdedorId, ratingPerdedor),
                    1,
                    0);

                ratings[vencedorId] = progressao.Casa.RatingEloAtual;
                ratings[perdedorId] = progressao.Visitante.RatingEloAtual;

                if (lutadorId != vencedorId && lutadorId != perdedorId)
                    continue;

                bool venceu = lutadorId == vencedorId;
                FighterProgressionResult resultadoLutador = venceu
                    ? progressao.Casa
                    : progressao.Visitante;

                historico.Add(new EloHistoryPoint
                {
                    PartidaId = partida.Id,
                    DataPartida = partida.DataPartida,
                    AdversarioId = venceu ? perdedorId : vencedorId,
                    Vitoria = venceu,
                    RatingEloAnterior = resultadoLutador.RatingEloAnterior,
                    RatingEloAtual = resultadoLutador.RatingEloAtual,
                    RatingEloAdversario = venceu ? ratingPerdedor : ratingVencedor,
                    VariacaoRatingElo = resultadoLutador.VariacaoRatingElo,
                    PontosLutador = venceu ? partida.PontosVencedor : partida.PontosPerdedor,
                    PontosAdversario = venceu ? partida.PontosPerdedor : partida.PontosVencedor
                });
            }

            AjustarAoRatingAtual(historico, ratingEloAtualEsperado);

            return historico;
        }

        private static void AjustarAoRatingAtual(
            IList<EloHistoryPoint> historico,
            int? ratingEloAtualEsperado)
        {
            if (historico == null
                || historico.Count == 0
                || !ratingEloAtualEsperado.HasValue
                || ratingEloAtualEsperado.Value <= 0)
            {
                return;
            }

            int diferenca = ratingEloAtualEsperado.Value - historico[historico.Count - 1].RatingEloAtual;
            if (diferenca == 0)
                return;

            // Bases antigas podem ter recebido migracoes ou correcoes manuais sem snapshots por luta.
            // Diluir o residuo preserva a forma da curva, o Elo inicial e o rating atual persistido.
            int ratingAnterior = historico[0].RatingEloAnterior;

            for (int indice = 0; indice < historico.Count; indice++)
            {
                EloHistoryPoint ponto = historico[indice];
                int ajusteAcumulado = (int)Math.Round(
                    diferenca * ((indice + 1.0) / historico.Count),
                    MidpointRounding.AwayFromZero);
                int ratingAjustado = ponto.RatingEloAtual + ajusteAcumulado;

                ponto.RatingEloAnterior = ratingAnterior;
                ponto.RatingEloAtual = ratingAjustado;
                ponto.VariacaoRatingElo = ratingAjustado - ratingAnterior;
                ratingAnterior = ratingAjustado;
            }
        }

        private static bool EhPartidaValida(EloHistoryMatch partida)
        {
            return partida != null
                && partida.IdVencedor.HasValue
                && partida.IdPerdedor.HasValue
                && partida.IdVencedor.Value > 0
                && partida.IdPerdedor.Value > 0
                && partida.IdVencedor.Value != partida.IdPerdedor.Value;
        }

        private static int ObterRating(IDictionary<int, int> ratings, int lutadorId)
        {
            int rating;
            return ratings.TryGetValue(lutadorId, out rating)
                ? rating
                : ProgressionCalculator.RatingEloInicial;
        }

        private static FighterProgressionState CriarEstado(int lutadorId, int rating)
        {
            return new FighterProgressionState
            {
                Id = lutadorId,
                Nivel = 1,
                Experiencia = 0,
                RatingElo = rating
            };
        }
    }
}
