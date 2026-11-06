using DreamMover.DTO;
using DreamMover.Helpers;
using DreamMover.Models;
using DreamMover.Services;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public class PartidaController : Controller
    {
        private const int RequestTimeoutSeconds = 6 * 60;

        public ActionResult Index()
        {
            return View();
        }
        public ActionResult DetalhesLutador(int id)
        {
            // Carrega os dados do lutador do banco de dados
            var lutador = _db.Lutador.Where(x => x.Id == id).ToList()
                .Select(x => LutadorDTO.ToDTO(x)).FirstOrDefault();

            if (lutador == null)
            {
                return HttpNotFound();
            }

            PreencherDetalhesLutador(lutador);

            // Retorna a View com os dados do lutador
            ViewBag.Lutador = lutador;  // Passa o objeto lutador para o ViewBag
            return PartialView("~/Views/DetalhesLutador/Index.cshtml");
        }

        private lutasEntities _db;

        public PartidaController()
        {
            _db = new lutasEntities();
        }

        [HttpPost]
        public JsonResult IniciarPartida(
            LutadorDTO lutadorCasa,
            LutadorDTO lutadorVisitante,
            int? cenarioId,
            bool partidaDivisao = false,
            int? desafianteDivisaoId = null)
        {
            Server.ScriptTimeout = RequestTimeoutSeconds;

            try
            {
                if (lutadorCasa == null || lutadorVisitante == null)
                    throw new ArgumentException("Selecione dois lutadores validos.");

                IkemenMatchExecutionResult resultado = IkemenMatchRunner.Executar(
                    _db,
                    lutadorCasa.Id,
                    lutadorVisitante.Id,
                    Server.MapPath("~/App_Data/IkemenMatches"),
                    false,
                    cenarioId,
                    partidaDivisao,
                    desafianteDivisaoId);
                MatchRegistrationResult registro = resultado.Registro;
                Cenario cenario = resultado.Cenario;

                return Json(new
                {
                    sucesso = true,
                    pontosLutadorCasa = resultado.PontosLutadorCasa,
                    pontosLutadorVisitante = resultado.PontosLutadorVisitante,
                    vencedorId = registro.VencedorId,
                    perdedorId = registro.PerdedorId,
                    cenario = new
                    {
                        cenario.Id,
                        cenario.Nome,
                        cenario.Universo,
                        imagem = string.IsNullOrWhiteSpace(cenario.Imagem) ? null : "/Imagens/Cenarios/" + cenario.Imagem
                    },
                    progressao = registro.Progressao,
                    divisao = registro.Divisao,
                    engine = new
                    {
                        nome = "IKEMEN GO",
                        versao = resultado.VersaoMotor,
                        duracaoMs = resultado.DuracaoMs
                    }
                }, JsonRequestBehavior.DenyGet);
            }
            catch (TimeoutException ex)
            {
                return Json(new
                {
                    sucesso = false,
                    codigoErro = "PARTIDA_TIMEOUT",
                    mensagem = ex.Message
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult GetDesafioDivisaoPendente()
        {
            try
            {
                DesafioDivisaoDTO desafio = DivisionService.ObterPrimeiroDesafioPendente(_db);
                return Json(new { sucesso = true, desafio }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        private List<RivalDTO> GetRivais(int idLutador)
        {
            var grupos = _db.Partida
                .Where(partida => partida.IdLutadorCasa == idLutador || partida.IdLutadorVisitante == idLutador)
                .GroupBy(partida => partida.IdLutadorCasa == idLutador ? partida.IdLutadorVisitante : partida.IdLutadorCasa)
                .OrderByDescending(g => g.Count())
                .ToList();

            var rivais = new List<RivalDTO>();

            foreach (var grupo in grupos)
            {
                var rivalDb = _db.Lutador.FirstOrDefault(x => x.Id == grupo.Key);
                if (rivalDb == null)
                    continue;

                var partidasContraRival = grupo
                    .OrderByDescending(partida => partida.DataPartida ?? DateTime.MinValue)
                    .ToList();

                rivais.Add(new RivalDTO
                {
                    Id = rivalDb.Id,
                    Nome = rivalDb.Nome,
                    Nivel = rivalDb.Nivel,
                    RatingElo = rivalDb.RatingElo,
                    Portrait = ImageUrlVersioner.FromImages(rivalDb.Portrait),
                    Vitorias = partidasContraRival.Count(partida => partida.IdVencedor == idLutador),
                    Derrotas = partidasContraRival.Count(partida => partida.idPerdedor == idLutador),
                    PartidasTotais = partidasContraRival.Count,
                    Partidas = partidasContraRival.Take(5).Select(partida => new PartidaDTO
                    {
                        Id = partida.Id,
                        IdVencedor = partida.IdVencedor,
                        IdPerdedor = partida.idPerdedor,
                    }).ToList()
                });
            }

            return rivais;
        }

        private void PreencherDetalhesLutador(LutadorDTO lutador)
        {
            var partidas = _db.Partida
                .Where(x => x.IdLutadorCasa == lutador.Id || x.IdLutadorVisitante == lutador.Id)
                .ToList();

            lutador.Vitorias = partidas.Count(x => x.IdVencedor == lutador.Id);
            lutador.Derrotas = partidas.Count(x => x.idPerdedor == lutador.Id);
            lutador.Partidas = lutador.Vitorias + lutador.Derrotas;
            lutador.Rivais = GetRivais(lutador.Id);
            lutador.UltimasPartidas = partidas
                .OrderByDescending(x => x.DataPartida ?? DateTime.MinValue)
                .Take(8)
                .Select(PartidaDTO.ToDTO)
                .ToList();
            lutador.HistoricoRatingElo = ObterHistoricoRatingElo(lutador.Id, lutador.RatingElo);
            DivisionService.PreencherLutador(_db, lutador);
        }

        private List<EloHistoricoDTO> ObterHistoricoRatingElo(int lutadorId, int ratingEloAtual)
        {
            var partidas = _db.Partida
                .AsNoTracking()
                .Where(x => x.IdVencedor.HasValue
                    && x.idPerdedor.HasValue
                    && x.IdVencedor.Value != x.idPerdedor.Value)
                .Select(x => new EloHistoryMatch
                {
                    Id = x.Id,
                    DataPartida = x.DataPartida,
                    IdVencedor = x.IdVencedor,
                    IdPerdedor = x.idPerdedor,
                    PontosVencedor = x.PontosVencedor,
                    PontosPerdedor = x.PontosPerdedor
                })
                .ToList();

            List<EloHistoryPoint> pontos = EloHistoryService.Calcular(partidas, lutadorId, ratingEloAtual);
            List<int> adversariosIds = pontos
                .Select(x => x.AdversarioId)
                .Distinct()
                .ToList();

            Dictionary<int, Lutador> adversarios = _db.Lutador
                .AsNoTracking()
                .Where(x => adversariosIds.Contains(x.Id))
                .ToList()
                .ToDictionary(x => x.Id);

            return pontos.Select(ponto =>
            {
                Lutador adversario;
                adversarios.TryGetValue(ponto.AdversarioId, out adversario);

                return new EloHistoricoDTO
                {
                    PartidaId = ponto.PartidaId,
                    DataPartida = ponto.DataPartida.HasValue
                        ? ponto.DataPartida.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
                        : "Data desconhecida",
                    AdversarioId = ponto.AdversarioId,
                    AdversarioNome = adversario != null ? adversario.Nome : "Lutador removido",
                    AdversarioPortrait = ImageUrlVersioner.FromImages(adversario != null ? adversario.Portrait : null),
                    Vitoria = ponto.Vitoria,
                    RatingEloAnterior = ponto.RatingEloAnterior,
                    RatingEloAtual = ponto.RatingEloAtual,
                    RatingEloAdversario = ponto.RatingEloAdversario,
                    VariacaoRatingElo = ponto.VariacaoRatingElo,
                    PontosLutador = ponto.PontosLutador,
                    PontosAdversario = ponto.PontosAdversario
                };
            }).ToList();
        }

    }
}
