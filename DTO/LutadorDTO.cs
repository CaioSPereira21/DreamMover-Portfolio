using DreamMover.Models;
using DreamMover.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace DreamMover.DTO
{
    public class LutadorDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Autor { get; set; }
        public string Universo { get; set; }
        public string UniversoNome { get; set; }
        public double? Exp { get; set; }
        public int? Nivel { get; set; }
        public int RatingElo { get; set; }
        public int Divisao { get; set; }
        public string DivisaoNome { get; set; }
        public int SequenciaDivisao { get; set; }
        public int PartidasDivisao { get; set; }
        public bool DesafioDivisaoPendente { get; set; }
        public string TipoDesafioDivisao { get; set; }
        public int? DivisaoDestino { get; set; }
        public string DivisaoDestinoNome { get; set; }
        public bool EmColocacao { get; set; }
        public int PartidasColocacao { get; set; }
        public int TotalPartidasColocacao { get; set; }
        public int? DivisaoTesteColocacao { get; set; }
        public string Portrait { get; set; }
        public string Stand { get; set; }
        public int Vitorias { get; set; }
        public int Derrotas { get; set; }
        public int Partidas { get; set; }
        public List<TagDTO> Tags { get; set; }
        public List<RivalDTO> Rivais { get; set; }
        public List<PartidaDTO> UltimasPartidas { get; set; }
        public List<EloHistoricoDTO> HistoricoRatingElo { get; set; }
        public List<MovimentacaoDivisaoDTO> HistoricoDivisao { get; set; }

        public static LutadorDTO ToDTO(Lutador entity)
        {
            return new LutadorDTO
            {
                Id = entity.Id,
                Nome = entity.Nome,
                Autor = entity.Autor,
                Universo = "/Imagens/" + entity.Universo + ".png",
                UniversoNome = entity.Universo,
                Exp = entity.Exp,
                Nivel = entity.Nivel,
                RatingElo = entity.RatingElo,
                Portrait = ImageUrlVersioner.FromImages(entity.Portrait),
                Stand = ImageUrlVersioner.FromImages(entity.Stand),
                Tags = entity.Tag
                    .OrderBy(x => x.Nome)
                    .Select(x => new TagDTO { Id = x.Id, Nome = x.Nome })
                    .ToList(),
            };
        }

    }

    public class TagDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; }
    }
}
