using System;

namespace DreamMover.DTO
{
    public sealed class MovimentacaoDivisaoDTO
    {
        public long Id { get; set; }
        public int? PartidaId { get; set; }
        public string Tipo { get; set; }
        public int? DivisaoAnterior { get; set; }
        public string DivisaoAnteriorNome { get; set; }
        public int DivisaoNova { get; set; }
        public string DivisaoNovaNome { get; set; }
        public int RatingElo { get; set; }
        public int SequenciaAnterior { get; set; }
        public int? AdversarioId { get; set; }
        public string AdversarioNome { get; set; }
        public string Data { get; set; }
    }

    public sealed class DesafioDivisaoDTO
    {
        public int DesafianteId { get; set; }
        public string DesafianteNome { get; set; }
        public int ChefeId { get; set; }
        public string ChefeNome { get; set; }
        public string Tipo { get; set; }
        public int DivisaoOrigem { get; set; }
        public string DivisaoOrigemNome { get; set; }
        public int DivisaoDestino { get; set; }
        public string DivisaoDestinoNome { get; set; }
        public int PartidasColocacao { get; set; }
        public int TotalPartidasColocacao { get; set; }
    }
}
