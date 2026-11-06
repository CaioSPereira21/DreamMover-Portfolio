using System.Collections.Generic;

namespace DreamMover.DTO
{
    public class RivalDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Portrait { get; set; }
        public int? Nivel { get; set; }
        public int RatingElo { get; set; }
        public int Vitorias { get; set; }
        public int Derrotas { get; set; }
        public int PartidasTotais { get; set; }
        public List<PartidaDTO> Partidas { get; set; }
    }
}
