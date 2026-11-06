using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DreamMover.DTO
{
    public class PartidaDTO
    {
        public int Id { get; set; }
        public int? IdVencedor { get; set; }
        public int? IdPerdedor { get; set; }
        public LutadorDTO Vencedor { get; set; }
        public LutadorDTO Perdedor { get; set; }
        public string DataPartida { get; set; }
        public DateTime? DatePartida { get; set; }
        public int? PontosVencedor { get; set; }
        public int? PontosPerdedor { get; set; }
        public int? IdCenario { get; set; }
        public string CenarioNome { get; set; }
        public string CenarioUniverso { get; set; }
        public string CenarioImagem { get; set; }


        public static PartidaDTO ToDTO(Partida entity)
        {
            return new PartidaDTO
            {
                Id = entity.Id,
                IdVencedor = entity.IdVencedor,
                IdPerdedor = entity.idPerdedor,
                Vencedor = entity.LutadorVencedor != null ? LutadorDTO.ToDTO(entity.LutadorVencedor) : null,
                Perdedor = entity.LutadorPerdedor != null ? LutadorDTO.ToDTO(entity.LutadorPerdedor) : null,
                DataPartida = entity.DataPartida.HasValue ? entity.DataPartida.Value.ToShortDateString() : "??/??/????",
                DatePartida = entity.DataPartida,
                PontosVencedor = entity.PontosVencedor,
                PontosPerdedor = entity.PontosPerdedor,
                IdCenario = entity.IdCenario,
                CenarioNome = entity.Cenario != null ? entity.Cenario.Nome : null,
                CenarioUniverso = entity.Cenario != null ? entity.Cenario.Universo : null,
                CenarioImagem = entity.Cenario != null && !string.IsNullOrWhiteSpace(entity.Cenario.Imagem)
                    ? "/Imagens/Cenarios/" + entity.Cenario.Imagem
                    : null,
            };
        }
    }
}
