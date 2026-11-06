using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DreamMover.Services
{
    public static class CharacterArchiveImportService
    {
        private const int MinimoPacotes = 2;
        private const int MaximoPacotes = 6;
        private const int MaximoEntradasPorPacote = 15000;
        private const int TamanhoMaximoParte = 8 * 1024 * 1024;
        private const long EspacoLivreReservado = 256L * 1024L * 1024L;
        private const int LutasSimultaneasIkemen = 4;
        private static readonly TimeSpan TempoDeRetencao = TimeSpan.FromHours(12);
        private static readonly object TournamentLock = new object();

        private static readonly HashSet<string> ExtensoesPacote = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar"
        };

        public static CharacterArchiveBatchAnalysis Analisar(
            IEnumerable<CharacterArchiveUpload> uploads,
            string tempRoot,
            string nome)
        {
            string nomeNormalizado = NormalizarNomeObrigatorio(nome);
            List<CharacterArchiveUpload> pacotes = (uploads ?? Enumerable.Empty<CharacterArchiveUpload>()).ToList();
            if (pacotes.Count < MinimoPacotes)
                throw new InvalidOperationException("Envie ao menos dois personagens para comparar nas lutas.");
            if (pacotes.Count > MaximoPacotes)
                throw new InvalidOperationException("Compare no maximo " + MaximoPacotes + " personagens por torneio.");

            if (pacotes.Any(x => x == null || x.Length <= 0 || (x.InputStream == null && string.IsNullOrEmpty(x.SourcePath))))
                throw new InvalidOperationException("Um dos pacotes enviados esta vazio.");

            PrepararRaizTemporaria(tempRoot);
            LimparImportacoesExpiradas(tempRoot);

            string token = Guid.NewGuid().ToString("N");
            string batchPath = ObterBatchPath(tempRoot, token);
            Directory.CreateDirectory(batchPath);
            CharacterArchiveManifest manifest = new CharacterArchiveManifest
            {
                Token = token,
                Nome = nomeNormalizado,
                CriadoEmUtc = DateTime.UtcNow,
                Candidatos = new List<CharacterArchiveCandidateManifest>()
            };

            try
            {
                long totalExtraido = 0;
                int indice = 0;
                foreach (CharacterArchiveUpload upload in pacotes)
                {
                    indice++;
                    manifest.Candidatos.Add(AnalisarPacote(upload, batchPath, indice, ref totalExtraido));
                }

                SalvarManifest(batchPath, manifest);
                return CriarAnalise(manifest);
            }
            catch
            {
                ExcluirDiretorioSilenciosamente(batchPath);
                throw;
            }
        }

        public static string IniciarEnvio(IEnumerable<CharacterArchiveFileDescriptor> arquivos, string tempRoot)
        {
            List<CharacterArchiveFileDescriptor> lista = (arquivos ?? Enumerable.Empty<CharacterArchiveFileDescriptor>()).ToList();
            if (lista.Count < MinimoPacotes || lista.Count > MaximoPacotes)
                throw new InvalidOperationException("Selecione de 2 a 6 pacotes para comparar.");
            foreach (CharacterArchiveFileDescriptor arquivo in lista)
            {
                string nome = Path.GetFileName(arquivo == null ? null : arquivo.FileName);
                if (arquivo == null || arquivo.Length <= 0 || string.IsNullOrWhiteSpace(nome)
                    || !ExtensoesPacote.Contains(Path.GetExtension(nome)))
                    throw new InvalidOperationException("Selecione apenas pacotes .zip ou .rar nao vazios.");
                arquivo.FileName = nome;
            }

            PrepararRaizTemporaria(tempRoot);
            LimparImportacoesExpiradas(tempRoot);
            string token = Guid.NewGuid().ToString("N");
            string stagePath = ObterBatchPath(tempRoot, token);
            Directory.CreateDirectory(stagePath);
            File.WriteAllText(Path.Combine(stagePath, "upload.json"), JsonConvert.SerializeObject(lista), new UTF8Encoding(false));
            return token;
        }

        public static long ReceberParte(string token, int indice, long offset, Stream input, int tamanho, string tempRoot)
        {
            string stagePath = ObterBatchPath(tempRoot, token);
            List<CharacterArchiveFileDescriptor> arquivos = CarregarEnvio(stagePath);
            if (indice < 0 || indice >= arquivos.Count || input == null || tamanho <= 0 || tamanho > TamanhoMaximoParte)
                throw new InvalidOperationException("Parte de pacote invalida.");
            CharacterArchiveFileDescriptor arquivo = arquivos[indice];
            if (offset < 0 || offset > arquivo.Length || tamanho > arquivo.Length - offset)
                throw new InvalidOperationException("A parte enviada excede o tamanho do pacote.");

            string path = Path.Combine(stagePath, "staged-" + indice + Path.GetExtension(arquivo.FileName).ToLowerInvariant());
            if (new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace - EspacoLivreReservado < tamanho)
                throw new InvalidOperationException("Espaco insuficiente para receber os pacotes.");
            using (FileStream output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                if (output.Length != offset)
                    throw new InvalidOperationException("Parte fora de ordem. Recomece o envio dos pacotes.");
                output.Position = offset;
                try
                {
                    long copiado = CopiarComLimite(input, output, tamanho, "A parte enviada excede 8 MB.");
                    if (copiado != tamanho)
                        throw new InvalidOperationException("A parte enviada esta incompleta.");
                }
                catch
                {
                    output.SetLength(offset);
                    throw;
                }
            }
            Directory.SetLastWriteTimeUtc(stagePath, DateTime.UtcNow);
            return offset + tamanho;
        }

        public static CharacterArchiveBatchAnalysis AnalisarEnvio(string token, string tempRoot, string nome)
        {
            string stagePath = ObterBatchPath(tempRoot, token);
            List<CharacterArchiveFileDescriptor> arquivos = CarregarEnvio(stagePath);
            List<CharacterArchiveUpload> uploads = new List<CharacterArchiveUpload>();
            for (int i = 0; i < arquivos.Count; i++)
            {
                string path = Path.Combine(stagePath, "staged-" + i + Path.GetExtension(arquivos[i].FileName).ToLowerInvariant());
                if (!File.Exists(path) || new FileInfo(path).Length != arquivos[i].Length)
                    throw new InvalidOperationException("O envio do pacote " + arquivos[i].FileName + " ainda nao terminou.");
                uploads.Add(new CharacterArchiveUpload { FileName = arquivos[i].FileName, Length = arquivos[i].Length, SourcePath = path });
            }
            try
            {
                return Analisar(uploads, tempRoot, nome);
            }
            finally
            {
                ExcluirDiretorioSilenciosamente(stagePath);
            }
        }

        private static List<CharacterArchiveFileDescriptor> CarregarEnvio(string stagePath)
        {
            string manifestPath = Path.Combine(stagePath, "upload.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("O envio expirou. Selecione os pacotes novamente.");
            List<CharacterArchiveFileDescriptor> arquivos = JsonConvert.DeserializeObject<List<CharacterArchiveFileDescriptor>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            if (arquivos == null || arquivos.Count < MinimoPacotes || arquivos.Count > MaximoPacotes)
                throw new InvalidOperationException("O envio de pacotes e invalido.");
            return arquivos;
        }

        public static CharacterTournamentResult ExecutarTorneio(
            string token,
            string tempRoot,
            string charsPath,
            IkemenPilotConfiguration ikemenConfiguration,
            string stage,
            string logsDirectory)
        {
            CharacterArchiveManifest manifest = CarregarManifest(tempRoot, token);
            if (manifest.Candidatos == null || manifest.Candidatos.Count < MinimoPacotes)
                throw new InvalidOperationException("A analise nao possui personagens suficientes para um torneio.");
            if (string.IsNullOrWhiteSpace(stage))
                throw new InvalidOperationException("Nao ha cenario ativo disponivel para os testes.");
            if (!Directory.Exists(charsPath))
                throw new DirectoryNotFoundException("Pasta chars do M.U.G.E.N. nao encontrada: " + charsPath);
            if (ikemenConfiguration == null || !ikemenConfiguration.IsConfigured)
                throw new InvalidOperationException(ikemenConfiguration == null
                    ? "O IKEMEN nao foi configurado para o torneio."
                    : ikemenConfiguration.ConfigurationMessage);
            if (string.IsNullOrWhiteSpace(logsDirectory))
                throw new InvalidOperationException("A pasta de resultados do torneio IKEMEN nao foi configurada.");

            if (!Monitor.TryEnter(TournamentLock))
                throw new InvalidOperationException("Ja existe um torneio de personagens em andamento. Aguarde a conclusao.");

            List<string> pastasTemporarias = new List<string>();
            try
            {
                if (IkemenEstaEmExecucao(ikemenConfiguration.ExecutablePath))
                    throw new InvalidOperationException("Feche a luta do IKEMEN que esta em andamento antes de iniciar o torneio.");

                Dictionary<string, string> nomesDeTeste = PrepararCandidatosParaTeste(
                    manifest,
                    ObterBatchPath(tempRoot, manifest.Token),
                    charsPath,
                    pastasTemporarias);

                List<CharacterTournamentFightResult> lutas = new List<CharacterTournamentFightResult>();
                var runner = new IkemenPilotRunner(ikemenConfiguration, logsDirectory);
                List<CharacterTournamentPairing> confrontos = new List<CharacterTournamentPairing>();
                for (int i = 0; i < manifest.Candidatos.Count; i++)
                {
                    for (int j = i + 1; j < manifest.Candidatos.Count; j++)
                    {
                        CharacterArchiveCandidateManifest primeiro = manifest.Candidatos[i];
                        CharacterArchiveCandidateManifest segundo = manifest.Candidatos[j];
                        bool inverterLados = ((i + j) % 2) != 0;
                        CharacterArchiveCandidateManifest casa = inverterLados ? segundo : primeiro;
                        CharacterArchiveCandidateManifest visitante = inverterLados ? primeiro : segundo;

                        confrontos.Add(new CharacterTournamentPairing
                        {
                            Casa = casa,
                            Visitante = visitante,
                            NomeCasa = nomesDeTeste[casa.Id],
                            NomeVisitante = nomesDeTeste[visitante.Id]
                        });
                    }
                }

                for (int offset = 0; offset < confrontos.Count; offset += LutasSimultaneasIkemen)
                {
                    List<CharacterTournamentPairing> lote = confrontos
                        .Skip(offset)
                        .Take(LutasSimultaneasIkemen)
                        .ToList();
                    Task<CharacterTournamentFightResult>[] tarefas = lote
                        .Select((confronto, posicaoJanela) => Task.Run(() => ExecutarLutaDeTesteIkemen(
                            confronto.Casa,
                            confronto.Visitante,
                            confronto.NomeCasa,
                            confronto.NomeVisitante,
                            runner,
                            stage,
                            posicaoJanela)))
                        .ToArray();

                    try
                    {
                        Task.WaitAll(tarefas);
                    }
                    catch (AggregateException ex)
                    {
                        Exception causa = ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex;
                        throw new InvalidOperationException(causa.Message, causa);
                    }

                    lutas.AddRange(tarefas.Select(x => x.Result));
                }

                CharacterTournamentResult resultado = Classificar(CriarAnalise(manifest).Candidatos, lutas);
                manifest.Resultado = resultado;
                SalvarManifest(ObterBatchPath(tempRoot, manifest.Token), manifest);
                return resultado;
            }
            finally
            {
                foreach (string pasta in pastasTemporarias)
                    ExcluirDiretorioSilenciosamente(pasta);
                Monitor.Exit(TournamentLock);
            }
        }

        public static CharacterImportedPackage Importar(
            string token,
            string candidatoId,
            string nome,
            string tempRoot,
            string charsPath,
            bool sobrescrever,
            bool exigirTorneio)
        {
            CharacterArchiveManifest manifest = CarregarManifest(tempRoot, token);
            string nomeNormalizado = NormalizarNomeObrigatorio(nome);
            if (!string.Equals(nomeNormalizado, manifest.Nome, StringComparison.Ordinal))
                throw new InvalidOperationException("O nome foi alterado depois da analise. Cancele e analise os pacotes novamente.");
            if (exigirTorneio && manifest.Resultado == null)
                throw new InvalidOperationException("Execute o torneio antes de escolher o personagem definitivo.");

            CharacterArchiveCandidateManifest candidato = manifest.Candidatos
                .FirstOrDefault(x => string.Equals(x.Id, candidatoId, StringComparison.OrdinalIgnoreCase));
            if (candidato == null)
                throw new InvalidOperationException("O personagem escolhido nao pertence a esta analise.");
            if (!Directory.Exists(charsPath))
                throw new DirectoryNotFoundException("Pasta chars do M.U.G.E.N. nao encontrada: " + charsPath);

            string batchPath = ObterBatchPath(tempRoot, manifest.Token);
            string sourceDefPath = ObterDefDoCandidato(batchPath, candidato);
            string sourceRoot = Path.GetDirectoryName(sourceDefPath);
            string destinationPath = Path.Combine(charsPath, nomeNormalizado);
            string tempDestination = Path.Combine(charsPath, "__dmimport_" + manifest.Token.Substring(0, 12));
            string backupPath = Path.Combine(charsPath, "__dmbackup_" + manifest.Token.Substring(0, 12));

            if ((Directory.Exists(destinationPath) || File.Exists(destinationPath)) && !sobrescrever)
                throw new InvalidOperationException("Ja existe um personagem chamado " + nomeNormalizado + " em chars. Marque a opcao de sobrescrita para substitui-lo.");

            ExcluirDiretorioSilenciosamente(tempDestination);
            ExcluirDiretorioSilenciosamente(backupPath);
            bool backupCriado = false;
            try
            {
                CopiarDiretorio(sourceRoot, tempDestination);
                PrepararDefPrincipal(
                    tempDestination,
                    Path.GetFileName(sourceDefPath),
                    nomeNormalizado);

                if (File.Exists(destinationPath))
                    throw new InvalidOperationException("Existe um arquivo com o mesmo nome reservado para a pasta do personagem: " + nomeNormalizado);
                if (Directory.Exists(destinationPath))
                {
                    Directory.Move(destinationPath, backupPath);
                    backupCriado = true;
                }

                Directory.Move(tempDestination, destinationPath);
                if (backupCriado)
                    ExcluirDiretorioSilenciosamente(backupPath);
            }
            catch
            {
                ExcluirDiretorioSilenciosamente(tempDestination);
                if (backupCriado && !Directory.Exists(destinationPath) && Directory.Exists(backupPath))
                    Directory.Move(backupPath, destinationPath);
                throw;
            }

            CharacterImportedPackage importado = new CharacterImportedPackage
            {
                Nome = nomeNormalizado,
                Pasta = nomeNormalizado,
                ArquivoDef = nomeNormalizado + ".def",
                Pacote = candidato.Pacote
            };
            ExcluirDiretorioSilenciosamente(batchPath);
            return importado;
        }

        public static void Cancelar(string token, string tempRoot)
        {
            ExcluirDiretorioSilenciosamente(ObterBatchPath(tempRoot, ValidarToken(token)));
        }

        public static CharacterTournamentResult Classificar(
            IEnumerable<CharacterArchiveCandidateAnalysis> candidatos,
            IEnumerable<CharacterTournamentFightResult> lutas)
        {
            List<CharacterArchiveCandidateAnalysis> listaCandidatos = (candidatos ?? Enumerable.Empty<CharacterArchiveCandidateAnalysis>()).ToList();
            List<CharacterTournamentFightResult> listaLutas = (lutas ?? Enumerable.Empty<CharacterTournamentFightResult>()).ToList();
            if (listaCandidatos.Count == 0)
                throw new InvalidOperationException("Nao ha candidatos para classificar.");

            List<CharacterTournamentRankingItem> ranking = listaCandidatos.Select(candidato =>
            {
                List<CharacterTournamentFightResult> disputadas = listaLutas
                    .Where(x => string.Equals(x.CandidatoCasaId, candidato.Id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.CandidatoVisitanteId, candidato.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                int vitorias = disputadas.Count(x => string.Equals(x.VencedorId, candidato.Id, StringComparison.OrdinalIgnoreCase));
                int empates = disputadas.Count(x => string.IsNullOrWhiteSpace(x.VencedorId));
                int roundsPro = disputadas.Sum(x => string.Equals(x.CandidatoCasaId, candidato.Id, StringComparison.OrdinalIgnoreCase)
                    ? x.PontosCasa
                    : x.PontosVisitante);
                int roundsContra = disputadas.Sum(x => string.Equals(x.CandidatoCasaId, candidato.Id, StringComparison.OrdinalIgnoreCase)
                    ? x.PontosVisitante
                    : x.PontosCasa);

                return new CharacterTournamentRankingItem
                {
                    CandidatoId = candidato.Id,
                    Pacote = candidato.Pacote,
                    NomeOriginal = candidato.NomeOriginal,
                    Autor = candidato.Autor,
                    Partidas = disputadas.Count,
                    Vitorias = vitorias,
                    Derrotas = disputadas.Count - vitorias - empates,
                    Empates = empates,
                    RoundsPro = roundsPro,
                    RoundsContra = roundsContra,
                    SaldoRounds = roundsPro - roundsContra
                };
            })
            .OrderByDescending(x => x.Vitorias)
            .ThenByDescending(x => x.SaldoRounds)
            .ThenByDescending(x => x.RoundsPro)
            .ThenBy(x => x.Pacote, StringComparer.OrdinalIgnoreCase)
            .ToList();

            for (int i = 0; i < ranking.Count; i++)
                ranking[i].Posicao = i + 1;

            CharacterTournamentRankingItem lider = ranking[0];
            lider.Recomendado = true;
            bool empateNaLideranca = ranking.Skip(1).Any(x =>
                x.Vitorias == lider.Vitorias
                && x.SaldoRounds == lider.SaldoRounds
                && x.RoundsPro == lider.RoundsPro);

            return new CharacterTournamentResult
            {
                RecomendadoId = lider.CandidatoId,
                EmpateNaLideranca = empateNaLideranca,
                Ranking = ranking,
                Lutas = listaLutas
            };
        }

        public static string NormalizarNomeObrigatorio(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new InvalidOperationException("Informe o nome obrigatorio do personagem.");

            string normalizado = string.Join(" ", nome.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (normalizado.Length > 80)
                throw new InvalidOperationException("O nome do personagem deve possuir no maximo 80 caracteres.");
            if (normalizado == "." || normalizado == ".." || normalizado.EndsWith(".", StringComparison.Ordinal) || normalizado.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidOperationException("O nome do personagem nao pode terminar com ponto ou espaco.");
            if (normalizado.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || normalizado.IndexOf(';') >= 0 || normalizado.Any(char.IsControl))
                throw new InvalidOperationException("O nome do personagem possui caracteres invalidos para a pasta ou para o arquivo .def.");

            string baseName = normalizado.Split('.')[0].Trim();
            string[] reservados = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reservados.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("O nome informado e reservado pelo Windows.");

            return normalizado;
        }

        public static void NormalizarIdentidadeNoDef(string defPath, string nome)
        {
            string nomeNormalizado = NormalizarNomeObrigatorio(nome);
            Encoding encoding = ObterEncoding(defPath);
            List<string> linhas = File.ReadAllLines(defPath, encoding).ToList();
            int infoInicio = -1;
            int infoFim = linhas.Count;
            for (int i = 0; i < linhas.Count; i++)
            {
                string trimmed = RemoverComentario(linhas[i]).Trim();
                if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
                    continue;

                string secao = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (infoInicio >= 0)
                {
                    infoFim = i;
                    break;
                }
                if (secao.Equals("Info", StringComparison.OrdinalIgnoreCase))
                    infoInicio = i;
            }

            if (infoInicio < 0)
            {
                linhas.Insert(0, "");
                linhas.Insert(0, "displayname = \"" + nomeNormalizado + "\"");
                linhas.Insert(0, "name = \"" + nomeNormalizado + "\"");
                linhas.Insert(0, "[Info]");
            }
            else
            {
                bool encontrouName = false;
                bool encontrouDisplayName = false;
                for (int i = infoInicio + 1; i < infoFim; i++)
                {
                    string semComentario = RemoverComentario(linhas[i]);
                    int separador = semComentario.IndexOf('=');
                    if (separador <= 0)
                        continue;

                    string chave = semComentario.Substring(0, separador).Trim();
                    if (!chave.Equals("name", StringComparison.OrdinalIgnoreCase)
                        && !chave.Equals("displayname", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string indentacao = linhas[i].Substring(0, linhas[i].Length - linhas[i].TrimStart().Length);
                    string comentario = ObterComentario(linhas[i]);
                    linhas[i] = indentacao + chave.ToLowerInvariant() + " = \"" + nomeNormalizado + "\"" + comentario;
                    if (chave.Equals("name", StringComparison.OrdinalIgnoreCase))
                        encontrouName = true;
                    else
                        encontrouDisplayName = true;
                }

                int inserirEm = infoFim;
                if (!encontrouName)
                {
                    linhas.Insert(inserirEm, "name = \"" + nomeNormalizado + "\"");
                    inserirEm++;
                }
                if (!encontrouDisplayName)
                    linhas.Insert(inserirEm, "displayname = \"" + nomeNormalizado + "\"");
            }

            File.WriteAllLines(defPath, linhas, encoding);
        }

        private static CharacterArchiveCandidateManifest AnalisarPacote(
            CharacterArchiveUpload upload,
            string batchPath,
            int indice,
            ref long totalExtraido)
        {
            if (upload == null || (upload.InputStream == null && string.IsNullOrEmpty(upload.SourcePath)) || upload.Length <= 0)
                throw new InvalidOperationException("Um dos pacotes enviados esta vazio.");

            string nomePacote = Path.GetFileName(upload.FileName ?? string.Empty);
            string extensao = Path.GetExtension(nomePacote);
            if (string.IsNullOrWhiteSpace(nomePacote) || !ExtensoesPacote.Contains(extensao))
                throw new InvalidOperationException("Tipo de pacote nao permitido: " + nomePacote + ". Use .zip ou .rar.");

            string candidatoId = Guid.NewGuid().ToString("N");
            string packagePath = Path.Combine(batchPath, "pacote-" + candidatoId);
            string archivePath = Path.Combine(batchPath, "upload-" + candidatoId + extensao.ToLowerInvariant());
            Directory.CreateDirectory(packagePath);
            if (!string.IsNullOrEmpty(upload.SourcePath))
                File.Move(upload.SourcePath, archivePath);
            else
                using (FileStream output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    upload.InputStream.CopyTo(output);

            int quantidadeArquivos = 0;
            long tamanhoPacote = 0;
            HashSet<string> destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                using (IArchive archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath), new ReaderOptions { LeaveStreamOpen = false }))
                {
                    string formato = archive.Type.ToString();
                    if (!formato.Equals("Zip", StringComparison.OrdinalIgnoreCase) && !formato.Equals("Rar", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(nomePacote + " nao e um arquivo ZIP ou RAR valido.");
                    if (archive.IsEncrypted)
                        throw new InvalidOperationException("O pacote " + nomePacote + " esta protegido por senha.");

                    foreach (IArchiveEntry entry in archive.Entries)
                    {
                        if (entry.IsDirectory)
                            continue;
                        if (++quantidadeArquivos > MaximoEntradasPorPacote)
                            throw new InvalidOperationException("O pacote " + nomePacote + " possui arquivos demais.");
                        if (entry.IsEncrypted)
                            throw new InvalidOperationException("O pacote " + nomePacote + " possui um arquivo protegido por senha.");

                        string relativo = NormalizarCaminhoInterno(entry.Key);
                        if (!destinos.Add(relativo))
                            throw new InvalidOperationException("O pacote " + nomePacote + " possui caminhos duplicados: " + relativo);

                        string destination = ObterCaminhoSeguro(packagePath, relativo);
                        long espacoDisponivel = Math.Max(0L, new DriveInfo(Path.GetPathRoot(destination)).AvailableFreeSpace - EspacoLivreReservado);
                        if (entry.Size > espacoDisponivel)
                            throw new InvalidOperationException("Espaco insuficiente para descompactar os personagens.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (Stream input = entry.OpenEntryStream())
                        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            long copiado = CopiarComLimite(input, output, espacoDisponivel, "Espaco insuficiente para descompactar os personagens.");
                            totalExtraido = checked(totalExtraido + copiado);
                            tamanhoPacote = checked(tamanhoPacote + copiado);
                        }
                    }
                }

                CharacterDefCandidate principal = LocalizarDefPrincipal(packagePath, nomePacote);
                return new CharacterArchiveCandidateManifest
                {
                    Id = candidatoId,
                    Ordem = indice,
                    Pacote = nomePacote,
                    NomeOriginal = principal.Info.DisplayName ?? principal.Info.Name ?? Path.GetFileNameWithoutExtension(principal.Path),
                    Autor = principal.Info.Author,
                    ArquivoDef = Path.GetFileName(principal.Path),
                    DefRelativo = ObterCaminhoRelativo(packagePath, principal.Path),
                    Tamanho = tamanhoPacote,
                    QuantidadeArquivos = quantidadeArquivos
                };
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Nao foi possivel abrir o pacote " + nomePacote + ". Verifique se ele nao esta corrompido ou protegido por senha.", ex);
            }
            finally
            {
                ExcluirArquivoSilenciosamente(archivePath);
            }
        }

        private static CharacterDefCandidate LocalizarDefPrincipal(string packagePath, string nomePacote)
        {
            string pacoteBase = Path.GetFileNameWithoutExtension(nomePacote);
            List<CharacterDefCandidate> candidatos = Directory.GetFiles(packagePath, "*.def", SearchOption.AllDirectories)
                .Select(path =>
                {
                    CharacterDefFileInfo info = LerDef(path);
                    int score = 0;
                    if (info.HasInfo) score += 40;
                    if (info.HasFiles) score += 70;
                    if (info.HasCmd) score += 30;
                    if (info.HasCns) score += 25;
                    if (info.HasSprite) score += 25;
                    if (info.HasAnim) score += 20;
                    string defBase = Path.GetFileNameWithoutExtension(path);
                    string pasta = Path.GetFileName(Path.GetDirectoryName(path));
                    if (string.Equals(defBase, pasta, StringComparison.OrdinalIgnoreCase)) score += 50;
                    if (string.Equals(defBase, pacoteBase, StringComparison.OrdinalIgnoreCase)) score += 35;
                    int profundidade = ObterCaminhoRelativo(packagePath, path).Count(x => x == '/');
                    score -= profundidade;
                    return new CharacterDefCandidate { Path = path, Info = info, Score = score };
                })
                .Where(x => x.Info.HasInfo && x.Info.HasFiles && x.Info.HasCmd && x.Info.HasSprite)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidatos.Count == 0)
                throw new InvalidOperationException("O pacote " + nomePacote + " nao possui um .def principal de personagem valido.");
            if (candidatos.Count > 1 && candidatos[0].Score == candidatos[1].Score)
                throw new InvalidOperationException("O pacote " + nomePacote + " possui mais de um .def principal possivel. Mantenha somente uma versao do personagem no pacote.");
            return candidatos[0];
        }

        private static CharacterDefFileInfo LerDef(string path)
        {
            CharacterDefFileInfo info = new CharacterDefFileInfo();
            string secao = string.Empty;
            foreach (string rawLine in File.ReadAllLines(path, ObterEncoding(path)))
            {
                string line = RemoverComentario(rawLine).Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    secao = line.Substring(1, line.Length - 2).Trim();
                    if (secao.Equals("Info", StringComparison.OrdinalIgnoreCase)) info.HasInfo = true;
                    if (secao.Equals("Files", StringComparison.OrdinalIgnoreCase)) info.HasFiles = true;
                    continue;
                }

                int separador = line.IndexOf('=');
                if (separador <= 0)
                    continue;
                string key = line.Substring(0, separador).Trim();
                string value = LimparValor(line.Substring(separador + 1));
                if (secao.Equals("Info", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Equals("displayname", StringComparison.OrdinalIgnoreCase)) info.DisplayName = value;
                    else if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) info.Name = value;
                    else if (key.Equals("author", StringComparison.OrdinalIgnoreCase)) info.Author = value;
                }
                else if (secao.Equals("Files", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    if (key.Equals("cmd", StringComparison.OrdinalIgnoreCase)) info.HasCmd = true;
                    else if (key.Equals("cns", StringComparison.OrdinalIgnoreCase)) info.HasCns = true;
                    else if (key.Equals("sprite", StringComparison.OrdinalIgnoreCase)) info.HasSprite = true;
                    else if (key.Equals("anim", StringComparison.OrdinalIgnoreCase)) info.HasAnim = true;
                }
            }
            return info;
        }

        private static Dictionary<string, string> PrepararCandidatosParaTeste(
            CharacterArchiveManifest manifest,
            string batchPath,
            string charsPath,
            List<string> pastasTemporarias)
        {
            Dictionary<string, string> nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < manifest.Candidatos.Count; i++)
            {
                CharacterArchiveCandidateManifest candidato = manifest.Candidatos[i];
                string testName = "__dmtest_" + manifest.Token.Substring(0, 8) + "_" + (i + 1);
                string destination = Path.Combine(charsPath, testName);
                if (Directory.Exists(destination) || File.Exists(destination))
                    throw new InvalidOperationException("A pasta temporaria de teste ja existe: " + testName);

                string sourceDefPath = ObterDefDoCandidato(batchPath, candidato);
                CopiarDiretorio(Path.GetDirectoryName(sourceDefPath), destination);
                PrepararDefPrincipal(destination, Path.GetFileName(sourceDefPath), testName);
                pastasTemporarias.Add(destination);
                nomes[candidato.Id] = testName;
            }
            return nomes;
        }

        private static void PrepararDefPrincipal(string folderPath, string originalDefName, string finalName)
        {
            string originalDefPath = Path.Combine(folderPath, originalDefName);
            if (!File.Exists(originalDefPath))
                throw new FileNotFoundException("O .def principal nao foi copiado.", originalDefPath);

            string finalDefPath = Path.Combine(folderPath, finalName + ".def");
            if (!string.Equals(originalDefPath, finalDefPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(finalDefPath))
                    File.Delete(finalDefPath);
                File.Move(originalDefPath, finalDefPath);
            }
            NormalizarIdentidadeNoDef(finalDefPath, finalName);
        }

        private static CharacterTournamentFightResult ExecutarLutaDeTesteIkemen(
            CharacterArchiveCandidateManifest casa,
            CharacterArchiveCandidateManifest visitante,
            string nomeCasa,
            string nomeVisitante,
            IkemenPilotRunner runner,
            string stage,
            int posicaoJanela)
        {
            IkemenPilotExecutionResult resultado = runner.ExecutarPartida(
                nomeCasa,
                nomeVisitante,
                stage,
                true,
                posicaoJanela);

            if (!resultado.Success)
                throw new InvalidOperationException(
                    "O IKEMEN nao concluiu a luta entre " + casa.Pacote + " e "
                    + visitante.Pacote + ". " + resultado.Message);
            if (!resultado.PontosPlayer1.HasValue || !resultado.PontosPlayer2.HasValue)
                throw new InvalidOperationException(
                    "O IKEMEN concluiu a luta, mas nao informou o placar entre "
                    + casa.Pacote + " e " + visitante.Pacote + ".");

            int pontosCasa = resultado.PontosPlayer1.Value;
            int pontosVisitante = resultado.PontosPlayer2.Value;

            return new CharacterTournamentFightResult
            {
                CandidatoCasaId = casa.Id,
                CandidatoCasaPacote = casa.Pacote,
                CandidatoVisitanteId = visitante.Id,
                CandidatoVisitantePacote = visitante.Pacote,
                PontosCasa = pontosCasa,
                PontosVisitante = pontosVisitante,
                VencedorId = pontosCasa > pontosVisitante ? casa.Id : pontosVisitante > pontosCasa ? visitante.Id : null
            };
        }

        private static bool IkemenEstaEmExecucao(string executablePath)
        {
            string processName = Path.GetFileNameWithoutExtension(executablePath);
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Any(x => !x.HasExited);
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static CharacterArchiveBatchAnalysis CriarAnalise(CharacterArchiveManifest manifest)
        {
            return new CharacterArchiveBatchAnalysis
            {
                Token = manifest.Token,
                Nome = manifest.Nome,
                Candidatos = manifest.Candidatos.OrderBy(x => x.Ordem).Select(x => new CharacterArchiveCandidateAnalysis
                {
                    Id = x.Id,
                    Pacote = x.Pacote,
                    NomeOriginal = x.NomeOriginal,
                    Autor = x.Autor,
                    ArquivoDef = x.ArquivoDef,
                    Tamanho = x.Tamanho,
                    QuantidadeArquivos = x.QuantidadeArquivos
                }).ToList()
            };
        }

        private static string ObterDefDoCandidato(string batchPath, CharacterArchiveCandidateManifest candidato)
        {
            string packagePath = Path.Combine(batchPath, "pacote-" + candidato.Id);
            string defPath = ObterCaminhoSeguro(packagePath, candidato.DefRelativo);
            if (!File.Exists(defPath))
                throw new InvalidOperationException("O arquivo temporario de " + candidato.Pacote + " nao foi encontrado. Analise os pacotes novamente.");
            return defPath;
        }

        private static void CopiarDiretorio(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            foreach (string directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relative = ObterCaminhoRelativo(sourcePath, directory);
                Directory.CreateDirectory(ObterCaminhoSeguro(destinationPath, relative));
            }
            foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relative = ObterCaminhoRelativo(sourcePath, file);
                string destination = ObterCaminhoSeguro(destinationPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, false);
            }
        }

        private static void PrepararRaizTemporaria(string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(tempRoot))
                throw new InvalidOperationException("A pasta temporaria de personagens nao foi configurada.");
            Directory.CreateDirectory(Path.GetFullPath(tempRoot));
        }

        private static void LimparImportacoesExpiradas(string tempRoot)
        {
            foreach (string directory in Directory.GetDirectories(tempRoot))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.Subtract(TempoDeRetencao))
                        Directory.Delete(directory, true);
                }
                catch
                {
                    // Uma importacao em uso ou sem permissao fica para uma proxima limpeza.
                }
            }
        }

        private static void SalvarManifest(string batchPath, CharacterArchiveManifest manifest)
        {
            string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(Path.Combine(batchPath, "manifest.json"), json, new UTF8Encoding(false));
        }

        private static CharacterArchiveManifest CarregarManifest(string tempRoot, string token)
        {
            string validToken = ValidarToken(token);
            string batchPath = ObterBatchPath(tempRoot, validToken);
            string manifestPath = Path.Combine(batchPath, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("A analise expirou ou nao foi encontrada. Envie os pacotes novamente.");

            CharacterArchiveManifest manifest = JsonConvert.DeserializeObject<CharacterArchiveManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
            if (manifest == null || !string.Equals(manifest.Token, validToken, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A analise de personagens e invalida.");
            return manifest;
        }

        private static string ObterBatchPath(string tempRoot, string token)
        {
            return Path.Combine(Path.GetFullPath(tempRoot), ValidarToken(token));
        }

        private static string ValidarToken(string token)
        {
            Guid parsed;
            if (string.IsNullOrWhiteSpace(token) || !Guid.TryParseExact(token, "N", out parsed))
                throw new InvalidOperationException("Token da analise de personagens invalido.");
            return parsed.ToString("N");
        }

        private static string NormalizarCaminhoInterno(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("O pacote contem um caminho vazio.");

            List<string> parts = new List<string>();
            foreach (string rawPart in path.Replace('\\', '/').Trim('/').Split('/'))
            {
                string part = rawPart.Trim();
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.IndexOf(':') >= 0)
                    throw new InvalidOperationException("O pacote contem um caminho inseguro: " + path);
                parts.Add(part);
            }
            if (parts.Count == 0)
                throw new InvalidOperationException("O pacote contem um caminho vazio.");
            return string.Join("/", parts);
        }

        private static string ObterCaminhoSeguro(string rootPath, string relativePath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string relative = NormalizarCaminhoInterno(relativePath).Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O pacote tentou gravar fora da pasta permitida.");
            return fullPath;
        }

        private static string ObterCaminhoRelativo(string rootPath, string path)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Foi encontrado um arquivo fora do pacote analisado.");
            return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static long CopiarComLimite(Stream input, Stream output, long limite, string mensagem)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limite)
                    throw new InvalidOperationException(mensagem);
                output.Write(buffer, 0, read);
            }
            return total;
        }

        private static string RemoverComentario(string value)
        {
            int index = (value ?? string.Empty).IndexOf(';');
            return index >= 0 ? value.Substring(0, index) : value ?? string.Empty;
        }

        private static string ObterComentario(string value)
        {
            int index = (value ?? string.Empty).IndexOf(';');
            return index >= 0 ? " " + value.Substring(index).TrimStart() : string.Empty;
        }

        private static string LimparValor(string value)
        {
            return RemoverComentario(value).Trim().Trim('"').Trim('\'').Trim();
        }

        private static Encoding ObterEncoding(string path)
        {
            byte[] prefix = new byte[3];
            using (FileStream stream = File.OpenRead(path))
            {
                int read = stream.Read(prefix, 0, prefix.Length);
                if (read >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
                    return new UTF8Encoding(true);
                if (read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
                    return Encoding.Unicode;
                if (read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
                    return Encoding.BigEndianUnicode;
            }
            return Encoding.Default;
        }

        private static void ExcluirArquivoSilenciosamente(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void ExcluirDiretorioSilenciosamente(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        private sealed class CharacterArchiveManifest
        {
            public string Token { get; set; }
            public string Nome { get; set; }
            public DateTime CriadoEmUtc { get; set; }
            public List<CharacterArchiveCandidateManifest> Candidatos { get; set; }
            public CharacterTournamentResult Resultado { get; set; }
        }

        private sealed class CharacterTournamentPairing
        {
            public CharacterArchiveCandidateManifest Casa { get; set; }
            public CharacterArchiveCandidateManifest Visitante { get; set; }
            public string NomeCasa { get; set; }
            public string NomeVisitante { get; set; }
        }

        private sealed class CharacterArchiveCandidateManifest
        {
            public string Id { get; set; }
            public int Ordem { get; set; }
            public string Pacote { get; set; }
            public string NomeOriginal { get; set; }
            public string Autor { get; set; }
            public string ArquivoDef { get; set; }
            public string DefRelativo { get; set; }
            public long Tamanho { get; set; }
            public int QuantidadeArquivos { get; set; }
        }

        private sealed class CharacterDefCandidate
        {
            public string Path { get; set; }
            public CharacterDefFileInfo Info { get; set; }
            public int Score { get; set; }
        }

        private sealed class CharacterDefFileInfo
        {
            public bool HasInfo { get; set; }
            public bool HasFiles { get; set; }
            public bool HasCmd { get; set; }
            public bool HasCns { get; set; }
            public bool HasSprite { get; set; }
            public bool HasAnim { get; set; }
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string Author { get; set; }
        }
    }

    public sealed class CharacterArchiveUpload
    {
        public string FileName { get; set; }
        public long Length { get; set; }
        public Stream InputStream { get; set; }
        public string SourcePath { get; set; }
    }

    public sealed class CharacterArchiveFileDescriptor
    {
        public string FileName { get; set; }
        public long Length { get; set; }
    }

    public sealed class CharacterArchiveBatchAnalysis
    {
        public string Token { get; set; }
        public string Nome { get; set; }
        public List<CharacterArchiveCandidateAnalysis> Candidatos { get; set; }
    }

    public sealed class CharacterArchiveCandidateAnalysis
    {
        public string Id { get; set; }
        public string Pacote { get; set; }
        public string NomeOriginal { get; set; }
        public string Autor { get; set; }
        public string ArquivoDef { get; set; }
        public long Tamanho { get; set; }
        public int QuantidadeArquivos { get; set; }
    }

    public sealed class CharacterTournamentResult
    {
        public string RecomendadoId { get; set; }
        public bool EmpateNaLideranca { get; set; }
        public List<CharacterTournamentRankingItem> Ranking { get; set; }
        public List<CharacterTournamentFightResult> Lutas { get; set; }
    }

    public sealed class CharacterTournamentRankingItem
    {
        public int Posicao { get; set; }
        public string CandidatoId { get; set; }
        public string Pacote { get; set; }
        public string NomeOriginal { get; set; }
        public string Autor { get; set; }
        public int Partidas { get; set; }
        public int Vitorias { get; set; }
        public int Derrotas { get; set; }
        public int Empates { get; set; }
        public int RoundsPro { get; set; }
        public int RoundsContra { get; set; }
        public int SaldoRounds { get; set; }
        public bool Recomendado { get; set; }
    }

    public sealed class CharacterTournamentFightResult
    {
        public string CandidatoCasaId { get; set; }
        public string CandidatoCasaPacote { get; set; }
        public string CandidatoVisitanteId { get; set; }
        public string CandidatoVisitantePacote { get; set; }
        public int PontosCasa { get; set; }
        public int PontosVisitante { get; set; }
        public string VencedorId { get; set; }
    }

    public sealed class CharacterImportedPackage
    {
        public string Nome { get; set; }
        public string Pasta { get; set; }
        public string ArquivoDef { get; set; }
        public string Pacote { get; set; }
    }
}
