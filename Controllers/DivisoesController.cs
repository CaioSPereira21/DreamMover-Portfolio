using DreamMover.Helpers;
using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    internal sealed class DivisionMovementListRow
    {
        public long Id { get; set; }
        public int IdLutador { get; set; }
        public string LutadorNome { get; set; }
        public string Universo { get; set; }
        public string Portrait { get; set; }
        public string Tipo { get; set; }
        public byte? DivisaoAnterior { get; set; }
        public byte DivisaoNova { get; set; }
        public int RatingElo { get; set; }
        public int SequenciaAnterior { get; set; }
        public string AdversarioNome { get; set; }
        public DateTime CriadoEmUtc { get; set; }
    }

    public sealed class DivisoesController : Controller
    {
        private const int TamanhoPagina = 10;

        public ActionResult Index(string tipo, string busca, int pagina = 1)
        {
            var model = new DivisionMovementsViewModel
            {
                Busca = (busca ?? string.Empty).Trim(),
                FiltroTipo = NormalizarTipo(tipo),
                TamanhoPagina = TamanhoPagina
            };

            try
            {
                using (var db = new lutasEntities())
                {
                    const string sql = @"
SELECT m.Id, m.IdLutador, l.Nome LutadorNome, l.Universo, l.Portrait,
       m.Tipo, m.DivisaoAnterior, m.DivisaoNova, m.RatingElo,
       m.SequenciaAnterior, a.Nome AdversarioNome, m.CriadoEmUtc
FROM dbo.MovimentacaoDivisao m
INNER JOIN dbo.Lutador l ON l.Id = m.IdLutador
LEFT JOIN dbo.Lutador a ON a.Id = m.IdAdversario
WHERE m.Tipo IN ('PROMOCAO', 'REBAIXAMENTO')
ORDER BY m.CriadoEmUtc DESC, m.Id DESC";

                    List<DivisionMovementListRow> registros = db.Database
                        .SqlQuery<DivisionMovementListRow>(sql)
                        .ToList();

                    List<DivisionMovementViewModel> movimentacoes = registros
                        .Select(Mapear)
                        .ToList();

                    model.TotalPromocoes = movimentacoes.Count(x => x.Promocao);
                    model.TotalRebaixamentos = movimentacoes.Count(x => !x.Promocao);
                    model.UltimaMovimentacao = movimentacoes.Any()
                        ? (DateTime?)movimentacoes.Max(x => x.Data)
                        : null;

                    if (!string.IsNullOrWhiteSpace(model.FiltroTipo))
                    {
                        movimentacoes = movimentacoes
                            .Where(x => string.Equals(x.Tipo, model.FiltroTipo, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    if (!string.IsNullOrWhiteSpace(model.Busca))
                    {
                        movimentacoes = movimentacoes
                            .Where(x => Contem(x.LutadorNome, model.Busca)
                                || Contem(x.Universo, model.Busca)
                                || Contem(x.AdversarioNome, model.Busca))
                            .ToList();
                    }

                    model.TotalResultados = movimentacoes.Count;
                    model.TotalPaginas = Math.Max(1, (int)Math.Ceiling(model.TotalResultados / (double)TamanhoPagina));
                    model.PaginaAtual = Math.Max(1, Math.Min(pagina, model.TotalPaginas));
                    model.Movimentacoes = movimentacoes
                        .Skip((model.PaginaAtual - 1) * TamanhoPagina)
                        .Take(TamanhoPagina)
                        .ToList();
                    model.DadosDisponiveis = true;
                }
            }
            catch
            {
                // Mantem a navegacao acessivel durante setup local ou indisponibilidade do banco.
                model.DadosDisponiveis = false;
                model.PaginaAtual = 1;
                model.TotalPaginas = 1;
            }

            return View(model);
        }

        private static DivisionMovementViewModel Mapear(DivisionMovementListRow registro)
        {
            DateTime dataLocal = DateTime.SpecifyKind(registro.CriadoEmUtc, DateTimeKind.Utc).ToLocalTime();
            var cultura = CultureInfo.GetCultureInfo("pt-BR");

            return new DivisionMovementViewModel
            {
                Id = registro.Id,
                LutadorId = registro.IdLutador,
                LutadorNome = string.IsNullOrWhiteSpace(registro.LutadorNome) ? "Lutador" : registro.LutadorNome,
                Universo = string.IsNullOrWhiteSpace(registro.Universo) ? "Universo não informado" : registro.Universo,
                Portrait = ImageUrlVersioner.FromImages(registro.Portrait),
                Tipo = registro.Tipo,
                DivisaoAnterior = FormatarDivisao(registro.DivisaoAnterior),
                DivisaoNova = FormatarDivisao(registro.DivisaoNova),
                RatingElo = registro.RatingElo,
                SequenciaAnterior = registro.SequenciaAnterior,
                AdversarioNome = registro.AdversarioNome,
                Data = dataLocal,
                DataTexto = dataLocal.ToString("dd MMM yyyy 'às' HH:mm", cultura)
            };
        }

        private static string NormalizarTipo(string tipo)
        {
            if (string.Equals(tipo, "PROMOCAO", StringComparison.OrdinalIgnoreCase))
                return "PROMOCAO";

            if (string.Equals(tipo, "REBAIXAMENTO", StringComparison.OrdinalIgnoreCase))
                return "REBAIXAMENTO";

            return string.Empty;
        }

        private static string FormatarDivisao(byte? divisao)
        {
            return divisao.HasValue ? divisao.Value + "ª Divisão" : "Sem divisão";
        }

        private static bool Contem(string valor, string busca)
        {
            return !string.IsNullOrWhiteSpace(valor)
                && valor.IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
