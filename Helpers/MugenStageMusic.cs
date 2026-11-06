using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DreamMover.Helpers
{
    public sealed class StageMusicTrack
    {
        public int Round { get; set; }
        public string Arquivo { get; set; }
    }

    public static class MugenStageMusic
    {
        private static readonly string[] ExtensoesSuportadas = { ".mp3", ".ogg", ".wav", ".mid" };

        public static bool EhArquivoDeMusica(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return ExtensoesSuportadas.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public static string ObterNomeNormalizado(string arquivoDef, string arquivoMusica)
        {
            string referenciaDef = NormalizarReferenciaRelativa(arquivoDef);
            string nomeBase = Path.GetFileNameWithoutExtension(Path.GetFileName(referenciaDef));
            string extensao = Path.GetExtension(Path.GetFileName(arquivoMusica ?? string.Empty));

            if (string.IsNullOrWhiteSpace(nomeBase))
                throw new InvalidOperationException("O arquivo .def precisa ter um nome valido.");

            if (!EhArquivoDeMusica(arquivoMusica))
                throw new InvalidOperationException("O arquivo informado nao possui um formato de musica suportado.");

            string diretorio = Path.GetDirectoryName(referenciaDef.Replace('/', Path.DirectorySeparatorChar));
            string nome = nomeBase + extensao.ToLowerInvariant();
            return string.IsNullOrWhiteSpace(diretorio)
                ? nome
                : Path.Combine(diretorio, nome).Replace('\\', '/');
        }

        public static string ObterArquivo(string stagesPath, string arquivoDef)
        {
            return ObterArquivo(stagesPath, arquivoDef, 1);
        }

        public static string ObterArquivo(string stagesPath, string arquivoDef, int round)
        {
            if (string.IsNullOrWhiteSpace(stagesPath) || string.IsNullOrWhiteSpace(arquivoDef))
                return null;

            if (round < 1 || round > 99)
                return null;

            string defPath = Path.GetFullPath(Path.Combine(stagesPath, arquivoDef));
            if (EstaDentroDe(stagesPath, defPath) && File.Exists(defPath))
            {
                Dictionary<int, string> referencias = LerReferencias(defPath);
                string referencia;
                if (referencias.TryGetValue(round, out referencia))
                {
                    if (string.IsNullOrWhiteSpace(referencia))
                        return null;
                    string relativo = ResolverArquivo(stagesPath, referencia);
                    if (relativo != null)
                        return relativo;
                }
            }

            if (round != 1)
                return null;

            string referenciaMusica;
            try
            {
                referenciaMusica = ObterNomeNormalizado(arquivoDef, "musica.mp3");
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            foreach (string extensao in ExtensoesSuportadas)
            {
                string candidatoRelativo = Path.ChangeExtension(
                    referenciaMusica.Replace('/', Path.DirectorySeparatorChar),
                    extensao);
                string candidato = Path.Combine(stagesPath, candidatoRelativo);
                if (File.Exists(candidato))
                    return candidatoRelativo.Replace('\\', '/');
            }

            return null;
        }

        public static List<StageMusicTrack> ListarFaixas(string stagesPath, string arquivoDef)
        {
            List<StageMusicTrack> faixas = new List<StageMusicTrack>();
            if (string.IsNullOrWhiteSpace(stagesPath) || string.IsNullOrWhiteSpace(arquivoDef))
                return faixas;

            string defPath = Path.GetFullPath(Path.Combine(stagesPath, arquivoDef));
            if (!EstaDentroDe(stagesPath, defPath) || !File.Exists(defPath))
                return faixas;

            Dictionary<int, string> referencias = LerReferencias(defPath);
            foreach (int round in referencias.Keys.OrderBy(x => x))
            {
                string arquivo = ResolverArquivo(stagesPath, referencias[round]);
                if (arquivo != null)
                    faixas.Add(new StageMusicTrack { Round = round, Arquivo = arquivo });
            }

            if (!faixas.Any(x => x.Round == 1))
            {
                string arquivo = ObterArquivo(stagesPath, arquivoDef, 1);
                if (arquivo != null)
                    faixas.Insert(0, new StageMusicTrack { Round = 1, Arquivo = arquivo });
            }
            return faixas;
        }

        public static string ObterNomeDaFaixa(string arquivoDef, string arquivoMusica, int round)
        {
            return ObterNomeDaFaixa(arquivoDef, arquivoMusica, round, null);
        }

        public static string ObterNomeDaFaixa(string arquivoDef, string arquivoMusica, int round, string sufixo)
        {
            if (round < 1 || round > 99)
                throw new InvalidOperationException("O round da musica deve estar entre 1 e 99.");
            if (sufixo != null && (sufixo.Length == 0 || sufixo.Any(x => !char.IsLetterOrDigit(x))))
                throw new InvalidOperationException("O identificador da musica e invalido.");

            string nomeNormalizado = ObterNomeNormalizado(arquivoDef, arquivoMusica);
            string diretorio = Path.GetDirectoryName(nomeNormalizado.Replace('/', Path.DirectorySeparatorChar));
            string nome = Path.GetFileNameWithoutExtension(nomeNormalizado) + ".round" + round
                + (sufixo == null ? string.Empty : "-" + sufixo)
                + Path.GetExtension(nomeNormalizado);
            return string.IsNullOrWhiteSpace(diretorio)
                ? nome
                : Path.Combine(diretorio, nome).Replace('\\', '/');
        }

        public static string ObterContentType(string arquivoMusica)
        {
            switch (Path.GetExtension(arquivoMusica ?? string.Empty).ToLowerInvariant())
            {
                case ".mp3":
                    return "audio/mpeg";
                case ".ogg":
                    return "audio/ogg";
                case ".wav":
                    return "audio/wav";
                case ".mid":
                    return "audio/midi";
                default:
                    return "application/octet-stream";
            }
        }

        public static void CorrigirReferenciaNoDef(string defPath, string arquivoMusica)
        {
            CorrigirReferenciaNoDef(defPath, arquivoMusica, 1);
        }

        public static void CorrigirReferenciaNoDef(string defPath, string arquivoMusica, int round)
        {
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
                throw new FileNotFoundException("Arquivo .def nao encontrado para corrigir a musica.", defPath);

            if (round < 1 || round > 99)
                throw new InvalidOperationException("O round da musica deve estar entre 1 e 99.");

            string referenciaMusica = NormalizarReferenciaRelativa(arquivoMusica);
            if (!EhArquivoDeMusica(referenciaMusica))
                throw new InvalidOperationException("O arquivo informado nao possui um formato de musica suportado.");

            List<string> linhas = File.ReadAllLines(defPath, Encoding.Default).ToList();
            string secaoAtual = string.Empty;
            int inicioSecaoMusica = -1;
            int fimSecaoMusica = -1;
            bool atualizado = false;
            string referencia = "stages/" + referenciaMusica;
            string chave = round == 1 ? "bgmusic" : "round" + round + ".bgmusic";

            for (int i = 0; i < linhas.Count; i++)
            {
                string nomeSecao;
                if (TryObterSecao(linhas[i], out nomeSecao))
                {
                    if (inicioSecaoMusica >= 0 && fimSecaoMusica < 0
                        && !nomeSecao.Equals("Music", StringComparison.OrdinalIgnoreCase))
                        fimSecaoMusica = i;

                    secaoAtual = nomeSecao;
                    if (secaoAtual.Equals("Music", StringComparison.OrdinalIgnoreCase) && inicioSecaoMusica < 0)
                        inicioSecaoMusica = i;

                    continue;
                }

                if (!secaoAtual.Equals("Music", StringComparison.OrdinalIgnoreCase)
                    || !EhAtribuicao(linhas[i], chave))
                    continue;

                linhas[i] = CriarLinhaBgmusic(linhas[i], chave, referencia);
                atualizado = true;
                break;
            }

            if (!atualizado && inicioSecaoMusica >= 0)
            {
                int indice = fimSecaoMusica >= 0 ? fimSecaoMusica : linhas.Count;
                linhas.Insert(indice, chave + " = " + referencia);
            }
            else if (!atualizado)
            {
                if (linhas.Count > 0 && !string.IsNullOrWhiteSpace(linhas[linhas.Count - 1]))
                    linhas.Add(string.Empty);

                linhas.Add("[Music]");
                linhas.Add(chave + " = " + referencia);
            }

            File.WriteAllLines(defPath, linhas, Encoding.Default);
        }

        public static void RemoverReferenciaNoDef(string defPath, int round)
        {
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
                throw new FileNotFoundException("Arquivo .def nao encontrado para remover a musica.", defPath);
            if (round < 1 || round > 99)
                throw new InvalidOperationException("O round da musica deve estar entre 1 e 99.");

            List<string> linhas = File.ReadAllLines(defPath, Encoding.Default).ToList();
            string secao = string.Empty;
            int inicioSecaoMusica = -1;
            int fimSecaoMusica = -1;
            bool removida = false;
            string chave = round == 1 ? "bgmusic" : "round" + round + ".bgmusic";
            for (int i = 0; i < linhas.Count; i++)
            {
                string nomeSecao;
                if (TryObterSecao(linhas[i], out nomeSecao))
                {
                    if (inicioSecaoMusica >= 0 && fimSecaoMusica < 0
                        && !nomeSecao.Equals("Music", StringComparison.OrdinalIgnoreCase))
                        fimSecaoMusica = i;
                    secao = nomeSecao;
                    if (secao.Equals("Music", StringComparison.OrdinalIgnoreCase) && inicioSecaoMusica < 0)
                        inicioSecaoMusica = i;
                    continue;
                }

                if (!secao.Equals("Music", StringComparison.OrdinalIgnoreCase) || !EhAtribuicao(linhas[i], chave))
                    continue;

                if (round == 1 && !removida)
                    linhas[i] = CriarLinhaBgmusic(linhas[i], chave, string.Empty);
                else
                {
                    linhas.RemoveAt(i);
                    i--;
                }
                removida = true;
            }

            if (round != 1 || removida)
            {
                File.WriteAllLines(defPath, linhas, Encoding.Default);
                return;
            }

            if (inicioSecaoMusica >= 0)
                linhas.Insert(fimSecaoMusica >= 0 ? fimSecaoMusica : linhas.Count, "bgmusic =");
            else
            {
                linhas.Add(string.Empty);
                linhas.Add("[Music]");
                linhas.Add("bgmusic =");
            }
            File.WriteAllLines(defPath, linhas, Encoding.Default);
        }

        private static Dictionary<int, string> LerReferencias(string defPath)
        {
            Dictionary<int, string> referencias = new Dictionary<int, string>();
            string secao = string.Empty;
            foreach (string linha in File.ReadAllLines(defPath, Encoding.Default))
            {
                string nomeSecao;
                if (TryObterSecao(linha, out nomeSecao))
                {
                    secao = nomeSecao;
                    continue;
                }

                if (!secao.Equals("Music", StringComparison.OrdinalIgnoreCase))
                    continue;

                string valor = RemoverComentario(linha);
                int separador = valor.IndexOf('=');
                if (separador <= 0)
                    continue;

                string chave = valor.Substring(0, separador).Trim();
                int round;
                if (chave.Equals("bgmusic", StringComparison.OrdinalIgnoreCase))
                    round = 1;
                else if (!chave.StartsWith("round", StringComparison.OrdinalIgnoreCase)
                    || !chave.EndsWith(".bgmusic", StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(chave.Substring(5, chave.Length - 13), out round)
                    || round < 1 || round > 99)
                    continue;

                referencias[round] = valor.Substring(separador + 1).Trim().Trim('"');
            }
            return referencias;
        }

        private static string ResolverArquivo(string stagesPath, string referencia)
        {
            if (string.IsNullOrWhiteSpace(referencia) || !EhArquivoDeMusica(referencia))
                return null;

            try
            {
                string relativo = NormalizarReferenciaRelativa(referencia);
                string caminho = Path.GetFullPath(Path.Combine(stagesPath, relativo.Replace('/', Path.DirectorySeparatorChar)));
                return EstaDentroDe(stagesPath, caminho) && File.Exists(caminho) ? relativo : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool EstaDentroDe(string stagesPath, string caminho)
        {
            string raiz = Path.GetFullPath(stagesPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return caminho.StartsWith(raiz, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryObterSecao(string linha, out string nomeSecao)
        {
            string conteudo = RemoverComentario(linha).Trim();
            if (conteudo.Length >= 3 && conteudo[0] == '[' && conteudo[conteudo.Length - 1] == ']')
            {
                nomeSecao = conteudo.Substring(1, conteudo.Length - 2).Trim();
                return true;
            }

            nomeSecao = null;
            return false;
        }

        private static bool EhAtribuicao(string linha, string chaveEsperada)
        {
            string conteudo = RemoverComentario(linha);
            int separador = conteudo.IndexOf('=');
            if (separador <= 0)
                return false;

            string chave = conteudo.Substring(0, separador).Trim();
            return chave.Equals(chaveEsperada, StringComparison.OrdinalIgnoreCase);
        }

        private static string CriarLinhaBgmusic(string linhaOriginal, string chave, string referencia)
        {
            int primeiroCaractere = 0;
            while (primeiroCaractere < linhaOriginal.Length && char.IsWhiteSpace(linhaOriginal[primeiroCaractere]))
                primeiroCaractere++;

            string indentacao = linhaOriginal.Substring(0, primeiroCaractere);
            int indiceComentario = linhaOriginal.IndexOf(';');
            string comentario = indiceComentario >= 0
                ? " " + linhaOriginal.Substring(indiceComentario).TrimStart()
                : string.Empty;

            return indentacao + chave + " = " + referencia + comentario;
        }

        private static string RemoverComentario(string linha)
        {
            string valor = linha ?? string.Empty;
            int indice = valor.IndexOf(';');
            return indice >= 0 ? valor.Substring(0, indice) : valor;
        }

        private static string NormalizarReferenciaRelativa(string value)
        {
            string referencia = (value ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(referencia)
                || referencia.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(referencia.Replace('/', Path.DirectorySeparatorChar))
                || referencia.Contains(":"))
            {
                throw new InvalidOperationException("A referencia do arquivo precisa ser relativa a pasta stages.");
            }

            string[] partes = referencia.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0 || partes.Any(x => x == "." || x == ".."))
                throw new InvalidOperationException("A referencia do arquivo possui um caminho invalido.");

            string normalizada = string.Join("/", partes);
            return normalizada.StartsWith("stages/", StringComparison.OrdinalIgnoreCase)
                ? normalizada.Substring("stages/".Length)
                : normalizada;
        }
    }
}
