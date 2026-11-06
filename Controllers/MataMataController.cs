using DreamMover.DTO;
using DreamMover.Helpers;
using DreamMover.Models;
using DreamMover.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public class MataMataController : Controller
    {
        public ActionResult Index(int? detalhesLutadorId)
        {
            if (detalhesLutadorId.HasValue)
            {
                return DetalhesLutador(detalhesLutadorId.Value);
            }

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

        public MataMataController()
        {
            _db = new lutasEntities();
        }

        [HttpPost]
        public JsonResult IniciarPartida(LutadorDTO lutadorCasa, LutadorDTO lutadorVisitante)
        {
            Server.ScriptTimeout = int.MaxValue;

            try
            {
                if (lutadorCasa == null || lutadorVisitante == null)
                    throw new ArgumentException("Selecione dois lutadores validos.");

                IkemenMatchExecutionResult resultado = IkemenMatchRunner.Executar(
                    _db,
                    lutadorCasa.Id,
                    lutadorVisitante.Id,
                    Server.MapPath("~/App_Data/IkemenMatches"),
                    false);
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
                    engine = new
                    {
                        nome = "IKEMEN GO",
                        versao = resultado.VersaoMotor,
                        duracaoMs = resultado.DuracaoMs
                    }
                }, JsonRequestBehavior.DenyGet);
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
        }

    }
}
