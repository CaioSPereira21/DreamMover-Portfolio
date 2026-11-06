using System;
using System.Collections.Generic;

namespace DreamMover.DTO
{
    public class TorreDTO
    {
        public TorreDTO()
        {
            Andares = new List<TorreAndarDTO>();
        }

        public int Id { get; set; }
        public int IdLutador { get; set; }
        public string LutadorNome { get; set; }
        public string LutadorPortrait { get; set; }
        public string LutadorStand { get; set; }
        public string LutadorUniverso { get; set; }
        public string UniversoAlvo { get; set; }
        public string UniversoImagem { get; set; }
        public string Tipo { get; set; }
        public string TipoNome { get; set; }
        public string Status { get; set; }
        public int AndarAtual { get; set; }
        public int TotalAndares { get; set; }
        public int QuantidadeElencoNoInicio { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime? DataConclusao { get; set; }
        public bool InsigniaConquistada { get; set; }
        public int NivelTrofeu { get; set; }
        public TorreDominioDTO Dominio { get; set; }
        public List<TorreAndarDTO> Andares { get; set; }

        public TorreAndarDTO ProximoAndar
        {
            get { return Andares.Find(x => x.Status == "Pendente"); }
        }

        public bool PodeIniciar
        {
            get { return Status == "EmAndamento" && ProximoAndar != null; }
        }
    }

    public class TorreAndarDTO
    {
        public int Id { get; set; }
        public int Ordem { get; set; }
        public int IdAdversario { get; set; }
        public string AdversarioNome { get; set; }
        public string Portrait { get; set; }
        public string Stand { get; set; }
        public int RatingEloNoSorteio { get; set; }
        public int NivelNoSorteio { get; set; }
        public string Funcao { get; set; }
        public string Status { get; set; }
        public int? IdPartida { get; set; }
        public DateTime? DataConclusao { get; set; }
    }

    public class TorreDominioDTO
    {
        public int Derrotados { get; set; }
        public int TotalAtual { get; set; }

        public int Percentual
        {
            get
            {
                if (TotalAtual <= 0)
                    return 0;

                return (int)Math.Round(Derrotados * 100.0 / TotalAtual, MidpointRounding.AwayFromZero);
            }
        }

        public int NovosDesafios
        {
            get { return Math.Max(0, TotalAtual - Derrotados); }
        }

        public bool DominioCompleto
        {
            get { return TotalAtual > 0 && Derrotados >= TotalAtual; }
        }
    }

    public class TorreUniversoOpcaoDTO
    {
        public string Nome { get; set; }
        public string Imagem { get; set; }
        public int QuantidadeAdversarios { get; set; }
        public bool UniversoNatal { get; set; }
        public bool Conquistado { get; set; }
        public int NivelTrofeu { get; set; }
        public TorreDominioDTO Dominio { get; set; }
    }

    public class ConquistaUniversoDTO
    {
        public int IdTorreOrigem { get; set; }
        public string Universo { get; set; }
        public string Imagem { get; set; }
        public int NivelTrofeu { get; set; }
        public string TrofeuNome { get; set; }
        public int QuantidadeElencoNaConquista { get; set; }
        public DateTime DataPrimeiraConquista { get; set; }
        public DateTime DataAtualizacao { get; set; }
        public TorreDominioDTO Dominio { get; set; }
        public ConquistaTorreDTO TorreOrigem { get; set; }
    }

    public class ConquistaTorreDTO
    {
        public ConquistaTorreDTO()
        {
            Andares = new List<TorreAndarDTO>();
        }

        public int Id { get; set; }
        public string Tipo { get; set; }
        public string TipoNome { get; set; }
        public string Status { get; set; }
        public int TotalAndares { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime? DataConclusao { get; set; }
        public List<TorreAndarDTO> Andares { get; set; }
    }
}
