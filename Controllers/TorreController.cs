using DreamMover.Models;
using DreamMover.Helpers;
using DreamMover.Services;
using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public class TorreController : Controller
    {
        private readonly lutasEntities _db;

        public TorreController()
        {
            _db = new lutasEntities();
        }

        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult GetLutadores()
        {
            try
            {
                var lutadoresDb = _db.Lutador
                    .Include(x => x.Tag)
                    .Where(x => x.Universo != null && x.Universo != "")
                    .OrderBy(x => x.Nome)
                    .ToList();

                var lutadores = lutadoresDb.Select(x => new
                {
                    x.Id,
                    x.Nome,
                    UniversoNome = x.Universo,
                    Universo = "/Imagens/" + x.Universo + ".png",
                    Portrait = ImageUrlVersioner.FromImages(x.Portrait),
                    Stand = ImageUrlVersioner.FromImages(x.Stand),
                    Autor = x.Autor,
                    Nivel = x.Nivel ?? 1,
                    RatingElo = x.RatingElo > 0 ? x.RatingElo : ProgressionCalculator.RatingEloInicial,
                    Tags = x.Tag
                        .OrderBy(tag => tag.Nome)
                        .Select(tag => new { tag.Id, tag.Nome })
                        .ToList()
                }).ToList();

                return Json(new { sucesso = true, lutadores }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult GetUniversosElegiveis(int lutadorId)
        {
            try
            {
                var universos = TorreService.ObterUniversosElegiveis(_db, lutadorId);
                return Json(new { sucesso = true, universos }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Criar(int lutadorId, string universo)
        {
            try
            {
                var torre = TorreService.Criar(_db, lutadorId, universo);
                return Json(new { sucesso = true, torre }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Obter(int id)
        {
            try
            {
                var torre = TorreService.Obter(_db, id);
                return Json(new { sucesso = true, torre }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult GetConquistas(int lutadorId)
        {
            try
            {
                var conquistas = TorreService.ObterConquistas(_db, lutadorId);
                return Json(new { sucesso = true, conquistas }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult IniciarAndar(int torreId)
        {
            TorreAndarExecucao andar = null;
            Server.ScriptTimeout = int.MaxValue;

            try
            {
                andar = TorreService.ObterProximoAndar(_db, torreId);
                IkemenMatchExecutionResult resultado = IkemenMatchRunner.Executar(
                    _db,
                    andar.IdLutador,
                    andar.IdAdversario,
                    Server.MapPath("~/App_Data/IkemenMatches"),
                    false);

                var torre = TorreService.RegistrarResultadoAndar(_db, andar, resultado.Registro);
                var cenario = resultado.Cenario;

                return Json(new
                {
                    sucesso = true,
                    torre,
                    resultado = new
                    {
                        resultado.PontosLutadorCasa,
                        resultado.PontosLutadorVisitante,
                        resultado.Registro.VencedorId,
                        resultado.Registro.PerdedorId,
                        resultado.Registro.Progressao,
                        Engine = new
                        {
                            Nome = "IKEMEN GO",
                            Versao = resultado.VersaoMotor,
                            DuracaoMs = resultado.DuracaoMs
                        },
                        Cenario = cenario == null ? null : new
                        {
                            cenario.Id,
                            cenario.Nome,
                            cenario.Universo,
                            Imagem = string.IsNullOrWhiteSpace(cenario.Imagem)
                                ? null
                                : "/Imagens/Cenarios/" + cenario.Imagem
                        }
                    }
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                if (andar != null)
                {
                    try
                    {
                        TorreService.LiberarAndar(_db, andar);
                    }
                    catch
                    {
                        // A mensagem original e mais util para quem esta testando a luta.
                    }
                }

                return Erro(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();

            base.Dispose(disposing);
        }

        private JsonResult Erro(Exception ex)
        {
            string mensagem = ex is ArgumentException
                || ex is InvalidOperationException
                || ex is System.IO.IOException
                ? ex.Message
                : "Nao foi possivel concluir a operacao da torre. " + ex.Message;

            return Json(new { sucesso = false, mensagem }, JsonRequestBehavior.DenyGet);
        }
    }
}
