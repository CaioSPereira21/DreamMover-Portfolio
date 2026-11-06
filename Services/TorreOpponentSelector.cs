using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamMover.Services
{
    public class TowerFighterCandidate
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public int RatingElo { get; set; }
        public int Nivel { get; set; }
        public bool JaDerrotado { get; set; }
        public string Funcao { get; set; }
    }

    public static class TorreOpponentSelector
    {
        public const int QuantidadeAndaresRapida = 5;

        public static List<TowerFighterCandidate> Selecionar(
            IEnumerable<TowerFighterCandidate> candidatos,
            int quantidade)
        {
            if (candidatos == null)
                throw new ArgumentNullException("candidatos");

            if (quantidade <= 0)
                throw new ArgumentOutOfRangeException("quantidade");

            List<TowerFighterCandidate> unicos = candidatos
                .Where(x => x != null && x.Id > 0)
                .GroupBy(x => x.Id)
                .Select(x => Copiar(x.First()))
                .ToList();

            if (unicos.Count < quantidade)
            {
                throw new ArgumentException(
                    "O universo precisa possuir pelo menos " + quantidade + " adversarios disponiveis.");
            }

            List<TowerFighterCandidate> novos = OrdenarPorDificuldade(
                unicos.Where(x => !x.JaDerrotado)).ToList();
            List<TowerFighterCandidate> selecionados;

            if (novos.Count >= quantidade)
            {
                selecionados = SelecionarDistribuidos(novos, quantidade);
            }
            else
            {
                selecionados = novos.ToList();
                HashSet<int> idsSelecionados = new HashSet<int>(selecionados.Select(x => x.Id));
                List<TowerFighterCandidate> antigos = OrdenarPorDificuldade(
                    unicos.Where(x => !idsSelecionados.Contains(x.Id))).ToList();

                selecionados.AddRange(SelecionarDistribuidos(
                    antigos,
                    quantidade - selecionados.Count));
            }

            selecionados = OrdenarPorDificuldade(selecionados).ToList();
            ClassificarAndares(selecionados);
            return selecionados;
        }

        private static IEnumerable<TowerFighterCandidate> OrdenarPorDificuldade(
            IEnumerable<TowerFighterCandidate> candidatos)
        {
            return candidatos
                .OrderBy(x => x.RatingElo)
                .ThenBy(x => x.Nivel)
                .ThenBy(x => x.Nome);
        }

        private static List<TowerFighterCandidate> SelecionarDistribuidos(
            List<TowerFighterCandidate> ordenados,
            int quantidade)
        {
            var resultado = new List<TowerFighterCandidate>();
            if (quantidade <= 0)
                return resultado;

            if (ordenados.Count < quantidade)
                throw new ArgumentException("Nao ha candidatos suficientes para completar a torre.");

            if (quantidade == 1)
            {
                resultado.Add(ordenados[ordenados.Count - 1]);
                return resultado;
            }

            for (int i = 0; i < quantidade; i++)
            {
                int indice = (int)Math.Round(
                    i * (ordenados.Count - 1.0) / (quantidade - 1.0),
                    MidpointRounding.AwayFromZero);

                TowerFighterCandidate candidato = ordenados[indice];
                if (!resultado.Any(x => x.Id == candidato.Id))
                    resultado.Add(candidato);
            }

            if (resultado.Count < quantidade)
            {
                resultado.AddRange(ordenados
                    .Where(x => resultado.All(y => y.Id != x.Id))
                    .Take(quantidade - resultado.Count));
            }

            return resultado;
        }

        private static void ClassificarAndares(List<TowerFighterCandidate> selecionados)
        {
            for (int i = 0; i < selecionados.Count; i++)
            {
                TowerFighterCandidate candidato = selecionados[i];

                if (i == selecionados.Count - 1)
                    candidato.Funcao = "Chefe";
                else if (i == selecionados.Count - 2)
                    candidato.Funcao = "Forte";
                else
                    candidato.Funcao = "Comum";
            }
        }

        private static TowerFighterCandidate Copiar(TowerFighterCandidate origem)
        {
            return new TowerFighterCandidate
            {
                Id = origem.Id,
                Nome = origem.Nome,
                RatingElo = origem.RatingElo,
                Nivel = origem.Nivel,
                JaDerrotado = origem.JaDerrotado,
                Funcao = origem.Funcao
            };
        }
    }
}
