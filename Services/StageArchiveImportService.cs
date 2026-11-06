using DreamMover.Helpers;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DreamMover.Services
{
    public static class StageArchiveImportService
    {
        private const int MaximoPacotes = 30;
        private const int MaximoEntradasPorPacote = 5000;
        private const long TamanhoMaximoUpload = 128L * 1024L * 1024L;
        private const long TamanhoMaximoExtraido = 512L * 1024L * 1024L;
        private static readonly TimeSpan TempoDeRetencao = TimeSpan.FromHours(12);

        private static readonly HashSet<string> ExtensoesPacote = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar"
        };

        private static readonly HashSet<string> ExtensoesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".def", ".sff", ".snd", ".air", ".cns", ".cmd", ".st", ".zss", ".chr", ".act", ".pal",
            ".glb", ".gltf", ".bin", ".hdr", ".dds", ".png", ".jpg", ".jpeg", ".pcx", ".bmp", ".tga",
            ".mp3", ".ogg", ".mid", ".wav"
        };

        public static StageArchiveBatchAnalysis Analisar(IEnumerable<StageArchiveUpload> uploads, string tempRoot)
        {
            List<StageArchiveUpload> pacotes = (uploads ?? Enumerable.Empty<StageArchiveUpload>()).ToList();
            if (pacotes.Count == 0)
                throw new InvalidOperationException("Selecione ao menos um arquivo .zip ou .rar.");
            if (pacotes.Count > MaximoPacotes)
                throw new InvalidOperationException("Analise no maximo " + MaximoPacotes + " pacotes por vez.");

            long tamanhoUpload = pacotes.Sum(x => x == null ? 0L : x.Length);
            if (tamanhoUpload <= 0 || tamanhoUpload > TamanhoMaximoUpload)
                throw new InvalidOperationException("O conjunto de pacotes nao pode ultrapassar 128 MB.");

            PrepararRaizTemporaria(tempRoot);
            LimparImportacoesExpiradas(tempRoot);

            string token = Guid.NewGuid().ToString("N");
            string batchPath = ObterBatchPath(tempRoot, token);
            Directory.CreateDirectory(batchPath);
            StageArchiveManifest manifest = new StageArchiveManifest
            {
                Token = token,
                CriadoEmUtc = DateTime.UtcNow,
                Pacotes = new List<StageArchivePackageManifest>()
            };

            try
            {
                long totalExtraido = 0;
                foreach (StageArchiveUpload upload in pacotes)
                    manifest.Pacotes.Add(AnalisarPacote(upload, batchPath, ref totalExtraido));

                SalvarManifest(batchPath, manifest);
                return CriarAnalise(manifest);
            }
            catch
            {
                ExcluirDiretorioSilenciosamente(batchPath);
                throw;
            }
        }

        public static List<StageImportedPackage> Importar(
            string token,
            IEnumerable<StagePackageSelection> selections,
            IEnumerable<StageAdditionalMusic> additionalMusic,
            string tempRoot,
            string stagesPath,
            bool sobrescrever)
        {
            StageArchiveManifest manifest = CarregarManifest(tempRoot, token);
            List<StagePackageSelection> escolhas = (selections ?? Enumerable.Empty<StagePackageSelection>())
                .Where(x => x != null && x.Importar)
                .ToList();
            if (escolhas.Count == 0)
                throw new InvalidOperationException("Mantenha ao menos um pacote selecionado para importar.");
            if (escolhas.Select(x => x.PacoteId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != escolhas.Count)
                throw new InvalidOperationException("Um pacote foi informado mais de uma vez.");

            Dictionary<string, StageAdditionalMusic> musicasAdicionais = (additionalMusic ?? Enumerable.Empty<StageAdditionalMusic>())
                .Where(x => x != null)
                .ToDictionary(x => x.PacoteId ?? string.Empty, x => x, StringComparer.OrdinalIgnoreCase);
            string batchPath = ObterBatchPath(tempRoot, token);
            List<StageImportFilePlan> arquivos = new List<StageImportFilePlan>();
            List<StageImportedPackage> importados = new List<StageImportedPackage>();
            long tamanhoTotal = 0;

            foreach (StagePackageSelection escolha in escolhas)
            {
                StageArchivePackageManifest pacote = manifest.Pacotes
                    .FirstOrDefault(x => string.Equals(x.Id, escolha.PacoteId, StringComparison.OrdinalIgnoreCase));
                if (pacote == null)
                    throw new InvalidOperationException("Um dos pacotes selecionados nao pertence a esta analise.");

                HashSet<string> idsSelecionados = new HashSet<string>(escolha.Arquivos ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                if (idsSelecionados.Count == 0)
                    throw new InvalidOperationException("Selecione os arquivos que deseja utilizar em " + pacote.Nome + ".");

                List<StageArchiveFileManifest> selecionados = pacote.Arquivos.Where(x => idsSelecionados.Contains(x.Id)).ToList();
                if (selecionados.Count != idsSelecionados.Count || selecionados.Any(x => !x.Suportado))
                    throw new InvalidOperationException("A selecao contem um arquivo invalido ou nao suportado.");

                List<StageArchiveFileManifest> defs = selecionados.Where(x => x.Tipo == "def").ToList();
                if (defs.Count != 1)
                    throw new InvalidOperationException("Selecione exatamente um arquivo .def em " + pacote.Nome + ".");

                List<StageArchiveFileManifest> musicasDoPacote = selecionados.Where(x => x.Tipo == "musica").ToList();
                StageAdditionalMusic musicaAdicional;
                musicasAdicionais.TryGetValue(pacote.Id, out musicaAdicional);
                int quantidadeMusicas = musicasDoPacote.Count + (musicaAdicional == null ? 0 : 1);
                if (quantidadeMusicas == 0)
                    throw new StageMusicRequiredException(pacote.Id, "O pacote " + pacote.Nome + " nao possui musica selecionada. Adicione uma musica ou cancele a adicao deste pacote.");
                if (quantidadeMusicas > 1)
                    throw new InvalidOperationException("Selecione somente uma musica para " + pacote.Nome + ".");

                StageArchiveFileManifest arquivoDef = defs[0];
                string prefixoDestino = ObterPrefixoDestino(pacote);
                string nomeDef = ObterCaminhoDestino(prefixoDestino, arquivoDef.Caminho);
                if (nomeDef.Length > 260)
                    throw new InvalidOperationException("O caminho do arquivo .def e muito longo para o catalogo: " + nomeDef + ".");
                string nomeMusica;
                foreach (StageArchiveFileManifest selecionado in selecionados)
                {
                    string nomeDestino = selecionado.Tipo == "musica"
                        ? MugenStageMusic.ObterNomeNormalizado(nomeDef, selecionado.Nome)
                        : ObterCaminhoDestino(prefixoDestino, selecionado.Caminho);
                    string sourcePath = ObterArquivoExtraido(batchPath, selecionado.StorageName);
                    if (!File.Exists(sourcePath))
                        throw new InvalidOperationException("Um arquivo temporario da analise nao foi encontrado. Analise os pacotes novamente.");

                    arquivos.Add(new StageImportFilePlan
                    {
                        SourcePath = sourcePath,
                        DestinationName = nomeDestino
                    });
                    tamanhoTotal += selecionado.Tamanho;
                }

                if (musicaAdicional != null)
                {
                    ValidarMusicaAdicional(musicaAdicional);
                    nomeMusica = MugenStageMusic.ObterNomeNormalizado(nomeDef, musicaAdicional.FileName);
                    arquivos.Add(new StageImportFilePlan
                    {
                        SourcePath = SalvarMusicaAdicional(batchPath, pacote.Id, musicaAdicional),
                        DestinationName = nomeMusica
                    });
                    tamanhoTotal += musicaAdicional.Length;
                }
                else
                {
                    nomeMusica = MugenStageMusic.ObterNomeNormalizado(nomeDef, musicasDoPacote[0].Nome);
                }

                importados.Add(new StageImportedPackage
                {
                    PacoteId = pacote.Id,
                    NomePacote = pacote.Nome,
                    ArquivoDef = nomeDef,
                    ArquivoMusica = nomeMusica
                });
            }

            if (tamanhoTotal > TamanhoMaximoExtraido)
                throw new InvalidOperationException("Os arquivos selecionados nao podem ultrapassar 512 MB descompactados.");

            ValidarDestinos(arquivos, stagesPath, sobrescrever);
            CopiarParaStages(arquivos, importados, batchPath, stagesPath);
            return importados;
        }

        public static void Cancelar(string token, string tempRoot)
        {
            ExcluirDiretorioSilenciosamente(ObterBatchPath(tempRoot, ValidarToken(token)));
        }

        private static StageArchivePackageManifest AnalisarPacote(StageArchiveUpload upload, string batchPath, ref long totalExtraido)
        {
            if (upload == null || upload.InputStream == null || upload.Length <= 0)
                throw new InvalidOperationException("Um dos pacotes enviados esta vazio.");

            string nomePacote = ObterNomeDestino(upload.FileName);
            string extensao = Path.GetExtension(nomePacote);
            if (!ExtensoesPacote.Contains(extensao))
                throw new InvalidOperationException("Tipo de pacote nao permitido: " + nomePacote + ". Use .zip ou .rar.");

            string pacoteId = Guid.NewGuid().ToString("N");
            string archivePath = Path.Combine(batchPath, pacoteId + extensao.ToLowerInvariant());
            using (FileStream output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                upload.InputStream.CopyTo(output);

            StageArchivePackageManifest pacote = new StageArchivePackageManifest
            {
                Id = pacoteId,
                Nome = nomePacote,
                Arquivos = new List<StageArchiveFileManifest>()
            };

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
                        if (pacote.Arquivos.Count >= MaximoEntradasPorPacote)
                            throw new InvalidOperationException("O pacote " + nomePacote + " possui arquivos demais.");

                        string caminho = NormalizarCaminhoInterno(entry.Key);
                        string nome = caminho.Split('/').Last();
                        string tipo = ObterTipoArquivo(nome);
                        bool suportado = tipo != "nao-suportado";
                        long tamanho = Math.Max(0L, entry.Size);
                        string arquivoId = Guid.NewGuid().ToString("N");
                        string storageName = null;
                        if (entry.IsEncrypted)
                            throw new InvalidOperationException("O pacote " + nomePacote + " possui um arquivo protegido por senha.");

                        if (suportado)
                        {
                            if (tamanho > TamanhoMaximoExtraido || totalExtraido + tamanho > TamanhoMaximoExtraido)
                                throw new InvalidOperationException("Os pacotes ultrapassam 512 MB descompactados.");

                            storageName = arquivoId + Path.GetExtension(nome).ToLowerInvariant();
                            using (Stream input = entry.OpenEntryStream())
                            using (FileStream output = new FileStream(ObterArquivoExtraido(batchPath, storageName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                long copiado = CopiarComLimite(input, output, TamanhoMaximoExtraido - totalExtraido);
                                totalExtraido += copiado;
                                tamanho = copiado;
                            }
                        }

                        pacote.Arquivos.Add(new StageArchiveFileManifest
                        {
                            Id = arquivoId,
                            Caminho = caminho,
                            Nome = nome,
                            Tipo = tipo,
                            Tamanho = tamanho,
                            Suportado = suportado,
                            StorageName = storageName,
                            DefDeCenario = tipo == "def"
                                && CenarioCatalogService.EhDefDeCenario(ObterArquivoExtraido(batchPath, storageName))
                        });
                    }
                }
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

            return pacote;
        }

        private static StageArchiveBatchAnalysis CriarAnalise(StageArchiveManifest manifest)
        {
            return new StageArchiveBatchAnalysis
            {
                Token = manifest.Token,
                Pacotes = manifest.Pacotes.Select(pacote =>
                {
                    bool musicaSelecionada = false;
                    StageArchiveFileManifest defPrincipal = pacote.Arquivos
                        .Where(x => x.Tipo == "def")
                        .OrderByDescending(x => x.DefDeCenario)
                        .ThenBy(x => x.Caminho.Count(c => c == '/'))
                        .FirstOrDefault();
                    return new StageArchivePackageAnalysis
                    {
                        Id = pacote.Id,
                        Nome = pacote.Nome,
                        PossuiDef = pacote.Arquivos.Any(x => x.Tipo == "def"),
                        PossuiMusica = pacote.Arquivos.Any(x => x.Tipo == "musica"),
                        Arquivos = pacote.Arquivos.Select(arquivo =>
                        {
                            bool selecionado = arquivo.Suportado;
                            if (arquivo.Tipo == "def")
                                selecionado = defPrincipal != null && arquivo.Id == defPrincipal.Id;
                            else if (arquivo.Tipo == "musica")
                            {
                                selecionado = !musicaSelecionada;
                                musicaSelecionada = true;
                            }

                            return new StageArchiveFileAnalysis
                            {
                                Id = arquivo.Id,
                                Caminho = arquivo.Caminho,
                                Nome = arquivo.Nome,
                                Tipo = arquivo.Tipo,
                                Tamanho = arquivo.Tamanho,
                                Suportado = arquivo.Suportado,
                                Selecionado = selecionado
                            };
                        }).ToList()
                    };
                }).ToList()
            };
        }

        private static void ValidarDestinos(List<StageImportFilePlan> arquivos, string stagesPath, bool sobrescrever)
        {
            if (string.IsNullOrWhiteSpace(stagesPath) || !Directory.Exists(stagesPath))
                throw new DirectoryNotFoundException("A pasta stages nao foi encontrada.");

            HashSet<string> destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StageImportFilePlan arquivo in arquivos)
            {
                if (!ExtensoesPermitidas.Contains(Path.GetExtension(arquivo.DestinationName)))
                    throw new InvalidOperationException("Tipo de arquivo nao permitido: " + arquivo.DestinationName);
                string destination = ObterCaminhoDestinoSeguro(stagesPath, arquivo.DestinationName);
                if (!destinos.Add(destination))
                    throw new InvalidOperationException("Mais de um arquivo seria salvo em stages com o nome " + arquivo.DestinationName + ". Ajuste a selecao dos pacotes.");
                if (!sobrescrever && File.Exists(destination))
                    throw new InvalidOperationException("O arquivo ja existe em stages: " + arquivo.DestinationName + ". Marque a opcao de sobrescrita para substitui-lo.");
            }
        }

        private static void CopiarParaStages(List<StageImportFilePlan> arquivos, List<StageImportedPackage> importados, string batchPath, string stagesPath)
        {
            string backupPath = Path.Combine(batchPath, "backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupPath);
            List<StageDestinationBackup> backups = new List<StageDestinationBackup>();
            try
            {
                foreach (StageImportFilePlan arquivo in arquivos)
                {
                    string destination = ObterCaminhoDestinoSeguro(stagesPath, arquivo.DestinationName);
                    string backup = null;
                    if (File.Exists(destination))
                    {
                        backup = Path.Combine(backupPath, Guid.NewGuid().ToString("N") + Path.GetExtension(destination));
                        File.Copy(destination, backup, true);
                    }

                    backups.Add(new StageDestinationBackup { DestinationPath = destination, BackupPath = backup });
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(arquivo.SourcePath, destination, true);
                }

                foreach (StageImportedPackage pacote in importados)
                    MugenStageMusic.CorrigirReferenciaNoDef(
                        ObterCaminhoDestinoSeguro(stagesPath, pacote.ArquivoDef),
                        pacote.ArquivoMusica);
            }
            catch
            {
                for (int i = backups.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        StageDestinationBackup backup = backups[i];
                        if (string.IsNullOrWhiteSpace(backup.BackupPath))
                            File.Delete(backup.DestinationPath);
                        else
                            File.Copy(backup.BackupPath, backup.DestinationPath, true);
                    }
                    catch
                    {
                    }
                }
                throw;
            }
            finally
            {
                ExcluirDiretorioSilenciosamente(backupPath);
            }
        }

        private static void ValidarMusicaAdicional(StageAdditionalMusic musica)
        {
            if (musica.InputStream == null || musica.Length <= 0)
                throw new InvalidOperationException("A musica adicional enviada esta vazia.");
            if (musica.Length > TamanhoMaximoUpload)
                throw new InvalidOperationException("A musica adicional nao pode ultrapassar 128 MB.");
            if (!MugenStageMusic.EhArquivoDeMusica(musica.FileName))
                throw new InvalidOperationException("Use uma musica .mp3, .ogg, .wav ou .mid.");
        }

        private static string SalvarMusicaAdicional(string batchPath, string pacoteId, StageAdditionalMusic musica)
        {
            string destination = Path.Combine(batchPath, "musica-" + pacoteId + Path.GetExtension(musica.FileName).ToLowerInvariant());
            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                CopiarComLimite(musica.InputStream, output, TamanhoMaximoUpload);
            return destination;
        }

        private static long CopiarComLimite(Stream input, Stream output, long limite)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int lidos;
            while ((lidos = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += lidos;
                if (total > limite)
                    throw new InvalidOperationException("Os arquivos ultrapassam o limite de tamanho descompactado.");
                output.Write(buffer, 0, lidos);
            }
            return total;
        }

        private static string NormalizarCaminhoInterno(string value)
        {
            string caminho = (value ?? string.Empty).Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(caminho) || caminho.StartsWith("/", StringComparison.Ordinal) || caminho.Contains(":"))
                throw new InvalidOperationException("O pacote possui um caminho de arquivo invalido.");

            string[] partes = caminho.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0 || partes.Any(x => x == "." || x == ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidOperationException("O pacote possui um caminho de arquivo inseguro.");
            return string.Join("/", partes);
        }

        private static string ObterNomeDestino(string value)
        {
            string nome = Path.GetFileName((value ?? string.Empty).Replace('/', '\\'));
            if (string.IsNullOrWhiteSpace(nome) || nome == "." || nome == ".." || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("Nome de arquivo invalido.");
            return nome;
        }

        private static string ObterPrefixoDestino(StageArchivePackageManifest pacote)
        {
            List<string> caminhos = pacote.Arquivos
                .Where(x => x.Suportado)
                .Select(x => x.Caminho)
                .ToList();
            string primeiraPasta = caminhos
                .Select(x => x.IndexOf('/') > 0 ? x.Substring(0, x.IndexOf('/')) : null)
                .FirstOrDefault();
            bool possuiRaizComum = !string.IsNullOrWhiteSpace(primeiraPasta)
                && caminhos.All(x => x.StartsWith(primeiraPasta + "/", StringComparison.OrdinalIgnoreCase));

            return possuiRaizComum ? string.Empty : ObterNomePastaPacote(pacote.Nome);
        }

        private static string ObterCaminhoDestino(string prefixo, string caminhoInterno)
        {
            string caminho = NormalizarCaminhoInterno(caminhoInterno);
            return string.IsNullOrWhiteSpace(prefixo)
                ? caminho
                : prefixo + "/" + caminho;
        }

        private static string ObterNomePastaPacote(string nomePacote)
        {
            string nome = Path.GetFileNameWithoutExtension(ObterNomeDestino(nomePacote));
            foreach (char invalido in Path.GetInvalidFileNameChars())
                nome = nome.Replace(invalido, '_');

            nome = nome.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(nome) || nome == "." || nome == ".."
                ? "cenario"
                : nome;
        }

        private static string ObterCaminhoDestinoSeguro(string stagesPath, string caminhoRelativo)
        {
            string relativo = NormalizarCaminhoInterno(caminhoRelativo)
                .Replace('/', Path.DirectorySeparatorChar);
            string raiz = Path.GetFullPath(stagesPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string destino = Path.GetFullPath(Path.Combine(raiz, relativo));
            if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O pacote possui um caminho fora da pasta stages.");
            return destino;
        }

        private static string ObterTipoArquivo(string fileName)
        {
            string extensao = Path.GetExtension(fileName ?? string.Empty);
            if (extensao.Equals(".def", StringComparison.OrdinalIgnoreCase))
                return "def";
            if (MugenStageMusic.EhArquivoDeMusica(fileName))
                return "musica";
            return ExtensoesPermitidas.Contains(extensao) ? "recurso" : "nao-suportado";
        }

        private static string ObterArquivoExtraido(string batchPath, string storageName)
        {
            if (string.IsNullOrWhiteSpace(storageName) || !string.Equals(Path.GetFileName(storageName), storageName, StringComparison.Ordinal))
                throw new InvalidOperationException("Referencia de arquivo temporario invalida.");
            return Path.Combine(batchPath, storageName);
        }

        private static void PrepararRaizTemporaria(string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(tempRoot))
                throw new InvalidOperationException("A pasta temporaria de importacao nao foi configurada.");
            Directory.CreateDirectory(tempRoot);
        }

        private static void LimparImportacoesExpiradas(string tempRoot)
        {
            foreach (string directory in Directory.EnumerateDirectories(tempRoot))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.Subtract(TempoDeRetencao))
                        Directory.Delete(directory, true);
                }
                catch
                {
                }
            }
        }

        private static string ObterBatchPath(string tempRoot, string token)
        {
            if (string.IsNullOrWhiteSpace(tempRoot))
                throw new InvalidOperationException("A pasta temporaria de importacao nao foi configurada.");
            return Path.Combine(Path.GetFullPath(tempRoot), ValidarToken(token));
        }

        private static string ValidarToken(string token)
        {
            Guid parsed;
            if (string.IsNullOrWhiteSpace(token) || !Guid.TryParseExact(token, "N", out parsed))
                throw new InvalidOperationException("A analise dos pacotes expirou ou e invalida.");
            return parsed.ToString("N");
        }

        private static void SalvarManifest(string batchPath, StageArchiveManifest manifest)
        {
            File.WriteAllText(Path.Combine(batchPath, "manifest.json"), JsonConvert.SerializeObject(manifest), Encoding.UTF8);
        }

        private static StageArchiveManifest CarregarManifest(string tempRoot, string token)
        {
            string tokenNormalizado = ValidarToken(token);
            string manifestPath = Path.Combine(ObterBatchPath(tempRoot, tokenNormalizado), "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("A analise dos pacotes expirou. Selecione os arquivos novamente.");

            StageArchiveManifest manifest = JsonConvert.DeserializeObject<StageArchiveManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
            if (manifest == null || !string.Equals(manifest.Token, tokenNormalizado, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A analise dos pacotes e invalida.");
            return manifest;
        }

        private static void ExcluirArquivoSilenciosamente(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void ExcluirDiretorioSilenciosamente(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        private sealed class StageArchiveManifest
        {
            public string Token { get; set; }
            public DateTime CriadoEmUtc { get; set; }
            public List<StageArchivePackageManifest> Pacotes { get; set; }
        }

        private sealed class StageArchivePackageManifest
        {
            public string Id { get; set; }
            public string Nome { get; set; }
            public List<StageArchiveFileManifest> Arquivos { get; set; }
        }

        private sealed class StageArchiveFileManifest
        {
            public string Id { get; set; }
            public string Caminho { get; set; }
            public string Nome { get; set; }
            public string Tipo { get; set; }
            public long Tamanho { get; set; }
            public bool Suportado { get; set; }
            public string StorageName { get; set; }
            public bool DefDeCenario { get; set; }
        }

        private sealed class StageImportFilePlan
        {
            public string SourcePath { get; set; }
            public string DestinationName { get; set; }
        }

        private sealed class StageDestinationBackup
        {
            public string DestinationPath { get; set; }
            public string BackupPath { get; set; }
        }
    }

    public sealed class StageArchiveUpload
    {
        public string FileName { get; set; }
        public long Length { get; set; }
        public Stream InputStream { get; set; }
    }

    public sealed class StageArchiveBatchAnalysis
    {
        public string Token { get; set; }
        public List<StageArchivePackageAnalysis> Pacotes { get; set; }
    }

    public sealed class StageArchivePackageAnalysis
    {
        public string Id { get; set; }
        public string Nome { get; set; }
        public bool PossuiDef { get; set; }
        public bool PossuiMusica { get; set; }
        public List<StageArchiveFileAnalysis> Arquivos { get; set; }
    }

    public sealed class StageArchiveFileAnalysis
    {
        public string Id { get; set; }
        public string Caminho { get; set; }
        public string Nome { get; set; }
        public string Tipo { get; set; }
        public long Tamanho { get; set; }
        public bool Suportado { get; set; }
        public bool Selecionado { get; set; }
    }

    public sealed class StagePackageSelection
    {
        public string PacoteId { get; set; }
        public bool Importar { get; set; }
        public List<string> Arquivos { get; set; }
    }

    public sealed class StageAdditionalMusic
    {
        public string PacoteId { get; set; }
        public string FileName { get; set; }
        public long Length { get; set; }
        public Stream InputStream { get; set; }
    }

    public sealed class StageImportedPackage
    {
        public string PacoteId { get; set; }
        public string NomePacote { get; set; }
        public string ArquivoDef { get; set; }
        public string ArquivoMusica { get; set; }
    }

    public sealed class StageMusicRequiredException : InvalidOperationException
    {
        public StageMusicRequiredException(string pacoteId, string message) : base(message)
        {
            PacoteId = pacoteId;
        }

        public string PacoteId { get; private set; }
    }
}
