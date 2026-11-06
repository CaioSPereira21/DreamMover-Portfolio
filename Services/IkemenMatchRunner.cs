using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace DreamMover.Services
{
    public sealed class IkemenMatchExecutionResult
    {
        public int PontosLutadorCasa { get; set; }
        public int PontosLutadorVisitante { get; set; }
        public Cenario Cenario { get; set; }
        public MatchRegistrationResult Registro { get; set; }
        public string VersaoMotor { get; set; }
        public string CaminhoLog { get; set; }
        public int DuracaoMs { get; set; }
    }

    public static class IkemenMatchRunner
    {
        public static IkemenMatchExecutionResult Executar(
            lutasEntities db,
            int lutadorCasaId,
            int lutadorVisitanteId,
            string dataDirectory,
            bool semSom,
            int? cenarioId = null,
            bool partidaDivisao = false,
            int? desafianteDivisaoId = null)
        {
            if (db == null)
                throw new ArgumentNullException("db");
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("Informe a pasta de resultados das partidas IKEMEN.", "dataDirectory");

            IkemenPilotConfiguration configuration = IkemenPilotConfiguration.Load();
            if (!configuration.IsConfigured)
                throw new InvalidOperationException(configuration.ConfigurationMessage);

            MatchParticipants participantes = MatchResultService.CarregarParticipantes(
                db,
                lutadorCasaId,
                lutadorVisitanteId);
            DivisionMatchPlan planoDivisao = DivisionService.PrepararPartida(
                db,
                lutadorCasaId,
                lutadorVisitanteId,
                partidaDivisao,
                desafianteDivisaoId);

            string stagesPath = Path.Combine(configuration.RootPath, "stages");
            if (!Directory.Exists(stagesPath))
                throw new DirectoryNotFoundException("A pasta stages do IKEMEN nao foi encontrada: " + stagesPath);

            Cenario cenario = CenarioCatalogService.SelecionarParaCombate(
                db,
                stagesPath,
                participantes.LutadorCasaUniverso,
                participantes.LutadorVisitanteUniverso,
                cenarioId);

            if (cenario == null)
                throw new InvalidOperationException("Nao ha cenarios ativos com arquivo .def disponivel no IKEMEN.");

            IkemenPilotExecutionResult execution;
            List<CenarioCenaConfiguration> cenas = CenarioSceneConfiguration.Deserializar(cenario.CenasJson);
            List<IkemenSceneStageSource> roteiro = CenarioCatalogService.ResolverRoteiro(
                db,
                stagesPath,
                cenas);
            using (IkemenSceneStageRuntime stageRuntime = IkemenSceneStageBuilder.Preparar(
                configuration.RootPath,
                cenario.ArquivoDef,
                roteiro))
            {
                var runner = new IkemenPilotRunner(configuration, dataDirectory);
                execution = runner.ExecutarPartida(
                    participantes.LutadorCasaReferencia,
                    participantes.LutadorVisitanteReferencia,
                    stageRuntime.StageArgument,
                    semSom,
                    null);
            }

            if (execution.TimedOut)
                throw new TimeoutException(execution.Message);
            if (!execution.Success)
                throw new InvalidOperationException(execution.Message);
            if (!execution.PontosPlayer1.HasValue || !execution.PontosPlayer2.HasValue)
                throw new InvalidOperationException("O IKEMEN encerrou a luta sem informar o placar.");

            int pontosCasa = execution.PontosPlayer1.Value;
            int pontosVisitante = execution.PontosPlayer2.Value;
            MatchRegistrationResult registro = MatchResultService.RegistrarResultado(
                db,
                participantes.LutadorCasaId,
                participantes.LutadorVisitanteId,
                pontosCasa,
                pontosVisitante,
                cenario.Id,
                planoDivisao);

            return new IkemenMatchExecutionResult
            {
                PontosLutadorCasa = pontosCasa,
                PontosLutadorVisitante = pontosVisitante,
                Cenario = cenario,
                Registro = registro,
                VersaoMotor = execution.EngineVersion,
                CaminhoLog = execution.LogPath,
                DuracaoMs = execution.DurationMs
            };
        }
    }
}
