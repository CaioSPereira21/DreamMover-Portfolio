using System;
using System.Collections.Generic;

namespace DreamMover.Models
{
    public sealed class DivisionMovementsViewModel
    {
        public DivisionMovementsViewModel()
        {
            Movimentacoes = new List<DivisionMovementViewModel>();
        }

        public bool DadosDisponiveis { get; set; }
        public string Busca { get; set; }
        public string FiltroTipo { get; set; }
        public int TotalPromocoes { get; set; }
        public int TotalRebaixamentos { get; set; }
        public int PaginaAtual { get; set; }
        public int TamanhoPagina { get; set; }
        public int TotalResultados { get; set; }
        public int TotalPaginas { get; set; }
        public DateTime? UltimaMovimentacao { get; set; }
        public List<DivisionMovementViewModel> Movimentacoes { get; set; }

        public int PrimeiroResultado
        {
            get { return TotalResultados == 0 ? 0 : ((PaginaAtual - 1) * TamanhoPagina) + 1; }
        }

        public int UltimoResultado
        {
            get { return Math.Min(PaginaAtual * TamanhoPagina, TotalResultados); }
        }
    }

    public sealed class DivisionMovementViewModel
    {
        public long Id { get; set; }
        public int LutadorId { get; set; }
        public string LutadorNome { get; set; }
        public string Universo { get; set; }
        public string Portrait { get; set; }
        public string Tipo { get; set; }
        public string DivisaoAnterior { get; set; }
        public string DivisaoNova { get; set; }
        public int RatingElo { get; set; }
        public int SequenciaAnterior { get; set; }
        public string AdversarioNome { get; set; }
        public DateTime Data { get; set; }
        public string DataTexto { get; set; }

        public bool Promocao
        {
            get { return string.Equals(Tipo, "PROMOCAO", StringComparison.OrdinalIgnoreCase); }
        }
    }
}
