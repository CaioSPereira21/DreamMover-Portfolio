using System;

namespace DreamMover.Services
{
    public sealed class FighterProgressionState
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public long Experiencia { get; set; }
        public int Nivel { get; set; }
        public int RatingElo { get; set; }
    }

    public sealed class FighterProgressionResult
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public int ExperienciaGanha { get; set; }
        public long ExperienciaAnterior { get; set; }
        public long ExperienciaAtual { get; set; }
        public int NivelAnterior { get; set; }
        public int NivelAtual { get; set; }
        public int RatingEloAnterior { get; set; }
        public int RatingEloAtual { get; set; }
        public int VariacaoRatingElo { get; set; }

        public bool SubiuDeNivel
        {
            get { return NivelAtual > NivelAnterior; }
        }
    }

    public sealed class MatchProgressionResult
    {
        public FighterProgressionResult Casa { get; set; }
        public FighterProgressionResult Visitante { get; set; }
    }

    public static class ProgressionCalculator
    {
        public const int RatingEloInicial = 1000;
        public const int FatorK = 32;
        public const int FatorKColocacao = 96;

        private const int ExperienciaParticipacao = 50;
        private const int ExperienciaPorRound = 75;
        private const int ExperienciaVitoria = 100;
        private const int RatingEloMinimo = 100;

        public static MatchProgressionResult Calcular(
            FighterProgressionState lutadorCasa,
            FighterProgressionState lutadorVisitante,
            int pontosCasa,
            int pontosVisitante,
            int fatorKCasa = FatorK,
            int fatorKVisitante = FatorK)
        {
            if (lutadorCasa == null)
                throw new ArgumentNullException("lutadorCasa");

            if (lutadorVisitante == null)
                throw new ArgumentNullException("lutadorVisitante");

            if (lutadorCasa.Id == lutadorVisitante.Id)
                throw new ArgumentException("Um lutador nao pode enfrentar a si mesmo.");

            ValidarPlacar(pontosCasa, pontosVisitante);

            if (fatorKCasa <= 0 || fatorKVisitante <= 0)
                throw new ArgumentOutOfRangeException("fatorKCasa", "O fator K deve ser maior que zero.");

            bool casaVenceu = pontosCasa > pontosVisitante;
            int ratingCasa = NormalizarRating(lutadorCasa.RatingElo);
            int ratingVisitante = NormalizarRating(lutadorVisitante.RatingElo);
            int variacaoCasa = CalcularVariacaoElo(ratingCasa, ratingVisitante, casaVenceu, fatorKCasa);
            int variacaoVisitante = CalcularVariacaoElo(ratingVisitante, ratingCasa, !casaVenceu, fatorKVisitante);

            var resultadoCasa = CalcularCarreira(
                lutadorCasa,
                pontosCasa,
                casaVenceu,
                ratingCasa,
                Math.Max(RatingEloMinimo, ratingCasa + variacaoCasa));

            var resultadoVisitante = CalcularCarreira(
                lutadorVisitante,
                pontosVisitante,
                !casaVenceu,
                ratingVisitante,
                Math.Max(RatingEloMinimo, ratingVisitante + variacaoVisitante));

            return new MatchProgressionResult
            {
                Casa = resultadoCasa,
                Visitante = resultadoVisitante
            };
        }

        public static void ValidarPlacar(int pontosCasa, int pontosVisitante)
        {
            bool pontosNoIntervalo = pontosCasa >= 0 && pontosCasa <= 10
                && pontosVisitante >= 0 && pontosVisitante <= 10;
            bool partidaEncerrada = pontosCasa != pontosVisitante
                && Math.Max(pontosCasa, pontosVisitante) > 0;

            if (!pontosNoIntervalo || !partidaEncerrada)
                throw new ArgumentException("Placar invalido. A partida deve terminar com um vencedor e no maximo 10 rounds por lado.");
        }

        public static long ExperienciaParaProximoNivel(int nivel)
        {
            int nivelNormalizado = Math.Max(1, nivel);
            return (long)nivelNormalizado * 1000L;
        }

        private static FighterProgressionResult CalcularCarreira(
            FighterProgressionState estado,
            int roundsVencidos,
            bool venceu,
            int ratingAnterior,
            int ratingAtual)
        {
            int nivelAnterior = Math.Max(1, estado.Nivel);
            int nivelAtual = nivelAnterior;
            long experienciaAnterior = Math.Max(0L, estado.Experiencia);
            long experienciaAtual = experienciaAnterior;
            int experienciaGanha = ExperienciaParticipacao
                + (roundsVencidos * ExperienciaPorRound)
                + (venceu ? ExperienciaVitoria : 0);

            experienciaAtual += experienciaGanha;

            while (experienciaAtual >= ExperienciaParaProximoNivel(nivelAtual))
            {
                experienciaAtual -= ExperienciaParaProximoNivel(nivelAtual);

                if (nivelAtual == int.MaxValue)
                    break;

                nivelAtual++;
            }

            return new FighterProgressionResult
            {
                Id = estado.Id,
                Nome = estado.Nome,
                ExperienciaGanha = experienciaGanha,
                ExperienciaAnterior = experienciaAnterior,
                ExperienciaAtual = experienciaAtual,
                NivelAnterior = nivelAnterior,
                NivelAtual = nivelAtual,
                RatingEloAnterior = ratingAnterior,
                RatingEloAtual = ratingAtual,
                VariacaoRatingElo = ratingAtual - ratingAnterior
            };
        }

        private static int CalcularVariacaoElo(int ratingCasa, int ratingVisitante, bool casaVenceu, int fatorK)
        {
            double expectativaCasa = 1.0 / (1.0 + Math.Pow(10.0, (ratingVisitante - ratingCasa) / 400.0));
            double resultadoCasa = casaVenceu ? 1.0 : 0.0;
            return (int)Math.Round(fatorK * (resultadoCasa - expectativaCasa), MidpointRounding.AwayFromZero);
        }

        private static int NormalizarRating(int rating)
        {
            return rating > 0 ? rating : RatingEloInicial;
        }
    }
}
