using DreamMover.Helpers;
using DreamMover.Models;
using System;
using System.IO;
using System.Collections.Generic;
using DreamMover.Services;

namespace DreamMover.DTO
{
    public class CenarioFaixaMusicaDTO
    {
        public int Round { get; set; }
        public string Arquivo { get; set; }
        public string Url { get; set; }
    }

    public class CenarioDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string ArquivoDef { get; set; }
        public string Universo { get; set; }
        public string UniversoImagem { get; set; }
        public string Autor { get; set; }
        public string Imagem { get; set; }
        public string ArquivoMusica { get; set; }
        public string MusicaUrl { get; set; }
        public List<CenarioFaixaMusicaDTO> FaixasMusica { get; set; }
        public bool Ativo { get; set; }
        public bool Excluido { get; set; }
        public bool ArquivoEncontrado { get; set; }
        public DateTime DataCadastro { get; set; }
        public DateTime? DataAtualizacao { get; set; }
        public List<CenarioCenaConfiguration> Cenas { get; set; }
        public int QuantidadeCenas { get; set; }

        public static CenarioDTO ToDTO(Cenario entity, string stagesPath)
        {
            List<StageMusicTrack> faixas = MugenStageMusic.ListarFaixas(stagesPath, entity.ArquivoDef);
            string arquivoMusica = faixas.Find(x => x.Round == 1)?.Arquivo;
            string versao = (entity.DataAtualizacao ?? entity.DataCadastro).Ticks.ToString();

            List<CenarioCenaConfiguration> cenas = CenarioSceneConfiguration.Deserializar(entity.CenasJson);

            return new CenarioDTO
            {
                Id = entity.Id,
                Nome = entity.Nome,
                ArquivoDef = entity.ArquivoDef,
                Universo = entity.Universo,
                UniversoImagem = string.IsNullOrWhiteSpace(entity.Universo)
                    ? null
                    : "/Imagens/" + Uri.EscapeDataString(entity.Universo.Trim()) + ".png",
                Autor = entity.Autor,
                Imagem = string.IsNullOrWhiteSpace(entity.Imagem)
                    ? null
                    : "/Imagens/Cenarios/" + Path.GetFileName(entity.Imagem),
                ArquivoMusica = arquivoMusica,
                MusicaUrl = string.IsNullOrWhiteSpace(arquivoMusica)
                    ? null
                    : "/Cenario/Musica/" + entity.Id + "?round=1&v=" + versao,
                FaixasMusica = faixas.ConvertAll(x => new CenarioFaixaMusicaDTO
                {
                    Round = x.Round,
                    Arquivo = x.Arquivo,
                    Url = "/Cenario/Musica/" + entity.Id + "?round=" + x.Round + "&v=" + versao
                }),
                Ativo = entity.Ativo,
                Excluido = entity.Excluido,
                ArquivoEncontrado = File.Exists(Path.Combine(stagesPath, entity.ArquivoDef ?? string.Empty)),
                DataCadastro = entity.DataCadastro,
                DataAtualizacao = entity.DataAtualizacao,
                Cenas = cenas,
                QuantidadeCenas = cenas.Count
            };
        }
    }
}
