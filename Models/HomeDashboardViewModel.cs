using System;
using System.Collections.Generic;

namespace DreamMover.Models
{
    public class HomeDashboardViewModel
    {
        public HomeDashboardViewModel()
        {
            LutadoresDestaque = new List<HomeFighterViewModel>();
        }

        public bool DadosDisponiveis { get; set; }
        public int TotalLutadores { get; set; }
        public int TotalPartidas { get; set; }
        public int TotalUniversos { get; set; }
        public int TotalCenarios { get; set; }
        public int NivelMaximo { get; set; }
        public int RatingEloMaximo { get; set; }
        public List<HomeFighterViewModel> LutadoresDestaque { get; set; }
        public HomeMatchViewModel UltimaPartida { get; set; }
    }

    public class HomeFighterViewModel
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Universo { get; set; }
        public string Stand { get; set; }
        public int Nivel { get; set; }
        public int RatingElo { get; set; }
    }

    public class HomeMatchViewModel
    {
        public string LutadorCasa { get; set; }
        public string LutadorVisitante { get; set; }
        public string Vencedor { get; set; }
        public int PontosCasa { get; set; }
        public int PontosVisitante { get; set; }
        public DateTime? Data { get; set; }
        public string Cenario { get; set; }
    }
}
