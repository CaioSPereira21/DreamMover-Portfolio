using DreamMover.Models;
using DreamMover.Helpers;
using DreamMover.Services;
using System;
using System.Linq;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            var dashboard = new HomeDashboardViewModel();

            try
            {
                using (var db = new lutasEntities())
                {
                    CenarioCatalogService.SincronizarPasta(db, MugenPaths.GetStagesPath());
                    dashboard.TotalLutadores = db.Lutador.Count();
                    dashboard.TotalPartidas = db.Partida.Count();
                    dashboard.TotalCenarios = db.Cenario.Count(x => !x.Excluido);
                    dashboard.TotalUniversos = db.Lutador
                        .Where(x => x.Universo != null && x.Universo != "")
                        .Select(x => x.Universo)
                        .Distinct()
                        .Count();

                    var destaques = db.Lutador
                        .OrderByDescending(x => x.RatingElo)
                        .ThenByDescending(x => x.Nivel)
                        .ThenByDescending(x => x.Exp)
                        .ThenBy(x => x.Nome)
                        .Take(2)
                        .ToList();

                    dashboard.LutadoresDestaque = destaques.Select(MapearLutador).ToList();
                    dashboard.NivelMaximo = destaques.Any() ? destaques.Max(x => x.Nivel ?? 1) : 0;
                    dashboard.RatingEloMaximo = destaques.Any() ? destaques.Max(x => x.RatingElo) : 0;

                    var ultimaPartida = db.Partida
                        .OrderByDescending(x => x.DataPartida)
                        .ThenByDescending(x => x.Id)
                        .FirstOrDefault();

                    if (ultimaPartida != null)
                    {
                        dashboard.UltimaPartida = new HomeMatchViewModel
                        {
                            LutadorCasa = ObterNome(ultimaPartida.LutadorCasa),
                            LutadorVisitante = ObterNome(ultimaPartida.LutadorVisitante),
                            Vencedor = ObterNome(ultimaPartida.LutadorVencedor),
                            PontosCasa = ultimaPartida.IdVencedor == ultimaPartida.IdLutadorCasa
                                ? ultimaPartida.PontosVencedor ?? 0
                                : ultimaPartida.PontosPerdedor ?? 0,
                            PontosVisitante = ultimaPartida.IdVencedor == ultimaPartida.IdLutadorVisitante
                                ? ultimaPartida.PontosVencedor ?? 0
                                : ultimaPartida.PontosPerdedor ?? 0,
                            Data = ultimaPartida.DataPartida,
                            Cenario = ultimaPartida.Cenario != null ? ultimaPartida.Cenario.Nome : null
                        };
                    }

                    dashboard.DadosDisponiveis = true;
                }
            }
            catch
            {
                // A home continua acessivel durante setup local ou indisponibilidade do banco.
                dashboard.DadosDisponiveis = false;
            }

            CompletarDestaques(dashboard.LutadoresDestaque);
            return View(dashboard);
        }

        private static HomeFighterViewModel MapearLutador(Lutador lutador)
        {
            return new HomeFighterViewModel
            {
                Id = lutador.Id,
                Nome = string.IsNullOrWhiteSpace(lutador.Nome) ? "Lutador" : lutador.Nome,
                Universo = string.IsNullOrWhiteSpace(lutador.Universo) ? "Universo desconhecido" : lutador.Universo,
                Stand = ImageUrlVersioner.FromImages(lutador.Stand, "Undefined.png"),
                Nivel = lutador.Nivel ?? 1,
                RatingElo = lutador.RatingElo
            };
        }

        private static string ObterNome(Lutador lutador)
        {
            return lutador == null || string.IsNullOrWhiteSpace(lutador.Nome) ? "A definir" : lutador.Nome;
        }

        private static void CompletarDestaques(System.Collections.Generic.ICollection<HomeFighterViewModel> destaques)
        {
            var reservas = new[]
            {
                new HomeFighterViewModel
                {
                    Nome = "Kung Fu Man",
                    Universo = "Demonstracao",
                    Stand = "/Imagens/Undefined.png",
                    Nivel = 5,
                    RatingElo = 1000
                },
                new HomeFighterViewModel
                {
                    Nome = "Kung Fu Man 720",
                    Universo = "Demonstracao",
                    Stand = "/Imagens/Undefined.png",
                    Nivel = 5,
                    RatingElo = 1000
                }
            };

            foreach (var reserva in reservas)
            {
                if (destaques.Count >= 2)
                    break;

                if (!destaques.Any(x => string.Equals(x.Nome, reserva.Nome, StringComparison.OrdinalIgnoreCase)))
                    destaques.Add(reserva);
            }
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";
            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";
            return View();
        }
    }
}
