namespace DreamMover.DTO
{
    public sealed class EloHistoricoDTO
    {
        public int PartidaId { get; set; }
        public string DataPartida { get; set; }
        public int AdversarioId { get; set; }
        public string AdversarioNome { get; set; }
        public string AdversarioPortrait { get; set; }
        public bool Vitoria { get; set; }
        public int RatingEloAnterior { get; set; }
        public int RatingEloAtual { get; set; }
        public int RatingEloAdversario { get; set; }
        public int VariacaoRatingElo { get; set; }
        public int? PontosLutador { get; set; }
        public int? PontosAdversario { get; set; }
    }
}
