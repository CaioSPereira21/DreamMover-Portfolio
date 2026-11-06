using DreamMover.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DreamMover.Services
{
    public static class CenarioCatalogService
    {
        private static readonly object SyncLock = new object();

        public static int SincronizarPasta(lutasEntities db, string stagesPath)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            if (!Directory.Exists(stagesPath))
                throw new DirectoryNotFoundException("Pasta stages do M.U.G.E.N. nao encontrada: " + stagesPath);

            lock (SyncLock)
            {
                List<Cenario> cadastrados = db.Cenario.ToList();
                Dictionary<string, Cenario> porArquivo = cadastrados
                    .Where(x => !string.IsNullOrWhiteSpace(x.ArquivoDef))
                    .GroupBy(x => NormalizarReferencia(x.ArquivoDef), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

                int adicionados = 0;
                foreach (string defPath in Directory.GetFiles(stagesPath, "*.def", SearchOption.AllDirectories))
                {
                    string arquivo = ObterReferenciaRelativa(stagesPath, defPath);
                    if (arquivo.Split('/').Any(x => x.StartsWith(IkemenSceneStageBuilder.RuntimePrefix, StringComparison.OrdinalIgnoreCase))
                        || !EhDefDeCenario(defPath)
                        || arquivo.Length > 260)
                        continue;
                    if (porArquivo.ContainsKey(arquivo))
                        continue;

                    CenarioDefInfo info = LerInformacoesDef(defPath);
                    Cenario cenario = new Cenario
                    {
                        Nome = Limitar(info.Nome, 255) ?? Path.GetFileNameWithoutExtension(arquivo),
                        ArquivoDef = arquivo,
                        Autor = Limitar(info.Autor, 255),
                        Universo = null,
                        Imagem = null,
                        Ativo = true,
                        Excluido = false,
                        DataCadastro = DateTime.Now
                    };

                    db.Cenario.Add(cenario);
                    porArquivo[arquivo] = cenario;
                    adicionados++;
                }

                if (adicionados > 0)
                    db.SaveChanges();

                return adicionados;
            }
        }

        public static List<string> ObterStagesAtivos(lutasEntities db, string stagesPath)
        {
            SincronizarPasta(db, stagesPath);

            return db.Cenario
                .AsNoTracking()
                .Where(x => x.Ativo && !x.Excluido)
                .OrderBy(x => x.Nome)
                .Select(x => x.ArquivoDef)
                .ToList()
                .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(Path.Combine(stagesPath, x)))
                .Select(RemoverExtensao)
                .ToList();
        }

        public static Cenario SelecionarParaCombate(
            lutasEntities db,
            string stagesPath,
            string universoCasa,
            string universoVisitante,
            int? cenarioId = null)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            SincronizarPasta(db, stagesPath);

            if (cenarioId.HasValue)
            {
                Cenario selecionado = db.Cenario
                    .AsNoTracking()
                    .FirstOrDefault(x => x.Id == cenarioId.Value && x.Ativo && !x.Excluido);
                if (selecionado == null)
                    throw new InvalidOperationException("O cenario selecionado nao esta disponivel para lutas.");
                if (string.IsNullOrWhiteSpace(selecionado.ArquivoDef)
                    || !File.Exists(Path.Combine(stagesPath, selecionado.ArquivoDef)))
                    throw new InvalidOperationException("O arquivo do cenario selecionado nao foi encontrado no IKEMEN.");

                return selecionado;
            }

            List<Cenario> disponiveis = db.Cenario
                .AsNoTracking()
                .Where(x => x.Ativo && !x.Excluido)
                .OrderBy(x => x.Nome)
                .ToList()
                .Where(x => !string.IsNullOrWhiteSpace(x.ArquivoDef)
                    && File.Exists(Path.Combine(stagesPath, x.ArquivoDef)))
                .ToList();

            if (disponiveis.Count == 0)
                return null;

            HashSet<string> universosDoCombate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(universoCasa))
                universosDoCombate.Add(universoCasa.Trim());
            if (!string.IsNullOrWhiteSpace(universoVisitante))
                universosDoCombate.Add(universoVisitante.Trim());

            List<Cenario> relacionados = disponiveis
                .Where(x => !string.IsNullOrWhiteSpace(x.Universo) && universosDoCombate.Contains(x.Universo.Trim()))
                .ToList();

            Random random = CriarRandom();
            bool priorizarRelacionado = relacionados.Count > 0 && random.Next(100) < 70;
            List<Cenario> origem = priorizarRelacionado ? relacionados : disponiveis;
            return origem[random.Next(origem.Count)];
        }

        public static List<IkemenSceneStageSource> ResolverRoteiro(
            lutasEntities db,
            string stagesPath,
            IEnumerable<CenarioCenaConfiguration> cenas)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            List<CenarioCenaConfiguration> solicitadas = (cenas ?? Enumerable.Empty<CenarioCenaConfiguration>())
                .OrderBy(x => x.Round)
                .ToList();
            if (solicitadas.Count == 0)
                return new List<IkemenSceneStageSource>();

            SincronizarPasta(db, stagesPath);
            List<Cenario> catalogo = db.Cenario
                .AsNoTracking()
                .Where(x => !x.Excluido)
                .ToList()
                .Where(x => !string.IsNullOrWhiteSpace(x.Nome)
                    && !string.IsNullOrWhiteSpace(x.ArquivoDef)
                    && File.Exists(Path.Combine(stagesPath, x.ArquivoDef)))
                .ToList();

            Dictionary<string, List<Cenario>> porNome = catalogo
                .GroupBy(x => x.Nome.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            List<IkemenSceneStageSource> resultado = new List<IkemenSceneStageSource>();
            foreach (CenarioCenaConfiguration cena in solicitadas)
            {
                List<Cenario> encontrados;
                if (string.IsNullOrWhiteSpace(cena.CenarioNome)
                    || !porNome.TryGetValue(cena.CenarioNome.Trim(), out encontrados))
                {
                    throw new InvalidOperationException(
                        "O cenario '" + (cena.CenarioNome ?? string.Empty) + "' do roteiro nao esta disponivel no catalogo.");
                }
                if (encontrados.Count > 1)
                    throw new InvalidOperationException("Existe mais de um cenario chamado '" + cena.CenarioNome + "'.");

                Cenario cenario = encontrados[0];
                resultado.Add(new IkemenSceneStageSource
                {
                    Round = cena.Round,
                    Nome = cenario.Nome,
                    StageReference = cenario.ArquivoDef,
                    Transicao = cena.Transicao,
                    DuracaoFrames = cena.DuracaoFrames
                });
            }

            return resultado;
        }

        public static CenarioDefInfo LerInformacoesDef(string defPath)
        {
            CenarioDefInfo info = new CenarioDefInfo();
            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(defPath, Encoding.Default))
            {
                string line = RemoveComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!section.Equals("Info", StringComparison.OrdinalIgnoreCase))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = Unquote(line.Substring(separator + 1).Trim());

                if (key.Equals("displayname", StringComparison.OrdinalIgnoreCase))
                    info.Nome = value;
                else if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(info.Nome))
                    info.Nome = value;
                else if (key.Equals("author", StringComparison.OrdinalIgnoreCase))
                    info.Autor = value;
            }

            return info;
        }

        public static bool EhDefDeCenario(string defPath)
        {
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
                return false;

            try
            {
                foreach (string rawLine in File.ReadLines(defPath, Encoding.Default))
                {
                    string line = RemoveComment(rawLine).Trim();
                    if (line.Length < 3 || line[0] != '[' || line[line.Length - 1] != ']')
                        continue;

                    string section = line.Substring(1, line.Length - 2).Trim();
                    if (section.Equals("BGDef", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return false;
        }

        private static string ObterReferenciaRelativa(string stagesPath, string arquivoPath)
        {
            string raiz = Path.GetFullPath(stagesPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string arquivo = Path.GetFullPath(arquivoPath);
            if (!arquivo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O arquivo de cenario nao pertence a pasta stages.");
            return NormalizarReferencia(arquivo.Substring(raiz.Length));
        }

        private static string NormalizarReferencia(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        }

        private static string RemoverExtensao(string value)
        {
            string referencia = NormalizarReferencia(value);
            return referencia.EndsWith(".def", StringComparison.OrdinalIgnoreCase)
                ? referencia.Substring(0, referencia.Length - 4)
                : referencia;
        }

        private static string RemoveComment(string value)
        {
            int index = value.IndexOf(';');
            return index >= 0 ? value.Substring(0, index) : value;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Trim();

            return value.Trim();
        }

        private static string Limitar(string value, int length)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return normalized != null && normalized.Length > length
                ? normalized.Substring(0, length)
                : normalized;
        }

        private static Random CriarRandom()
        {
            return new Random(unchecked(Environment.TickCount * 397 ^ Thread.CurrentThread.ManagedThreadId));
        }
    }

    public class CenarioDefInfo
    {
        public string Nome { get; set; }
        public string Autor { get; set; }
    }
}
