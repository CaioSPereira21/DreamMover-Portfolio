using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DreamMover.Services
{
    public sealed class IkemenPilotConfiguration
    {
        public string RootPath { get; private set; }
        public string ExecutablePath { get; private set; }
        public string Player1 { get; private set; }
        public string Player2 { get; private set; }
        public string DefaultStage { get; private set; }
        public bool IsConfigured { get; private set; }
        public string ConfigurationMessage { get; private set; }
        public string EngineVersion { get; private set; }

        public static IkemenPilotConfiguration Load()
        {
            var configuration = new IkemenPilotConfiguration
            {
                RootPath = NormalizarCaminho(ConfigurationManager.AppSettings["IkemenRootPath"]),
                ExecutablePath = NormalizarCaminho(ConfigurationManager.AppSettings["IkemenExecutablePath"]),
                Player1 = ObterTexto("IkemenPilotPlayer1", "kfm"),
                Player2 = ObterTexto("IkemenPilotPlayer2", "kfm"),
                DefaultStage = ObterTexto("IkemenPilotStage", null)
            };

            if (string.IsNullOrWhiteSpace(configuration.RootPath)
                && !string.IsNullOrWhiteSpace(configuration.ExecutablePath))
            {
                configuration.RootPath = Path.GetDirectoryName(configuration.ExecutablePath);
            }

            if (string.IsNullOrWhiteSpace(configuration.ExecutablePath)
                && !string.IsNullOrWhiteSpace(configuration.RootPath))
            {
                configuration.ExecutablePath = Path.Combine(configuration.RootPath, "Ikemen_GO.exe");
            }

            if (string.IsNullOrWhiteSpace(configuration.RootPath))
            {
                configuration.ConfigurationMessage = "Configure IkemenRootPath no Web.config para iniciar o piloto.";
                return configuration;
            }

            if (!Directory.Exists(configuration.RootPath))
            {
                configuration.ConfigurationMessage = "A pasta configurada do IKEMEN nao foi encontrada: " + configuration.RootPath;
                return configuration;
            }

            if (string.IsNullOrWhiteSpace(configuration.ExecutablePath)
                || !File.Exists(configuration.ExecutablePath))
            {
                configuration.ConfigurationMessage = "O executavel Ikemen_GO.exe nao foi encontrado na pasta configurada.";
                return configuration;
            }

            configuration.IsConfigured = true;
            configuration.ConfigurationMessage = "IKEMEN configurado e pronto para executar partidas.";

            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(configuration.ExecutablePath);
                configuration.EngineVersion = !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                    ? versionInfo.ProductVersion
                    : versionInfo.FileVersion;
            }
            catch
            {
                configuration.EngineVersion = null;
            }

            if (string.IsNullOrWhiteSpace(configuration.EngineVersion)
                || configuration.EngineVersion == "0.0.0.0")
            {
                configuration.EngineVersion = ObterVersaoPeloLog(configuration.RootPath);
            }

            if (string.IsNullOrWhiteSpace(configuration.EngineVersion))
                configuration.EngineVersion = Path.GetFileName(configuration.ExecutablePath);

            return configuration;
        }

        private static string NormalizarCaminho(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return DreamMover.Helpers.ProjectPaths.Resolve(path);
        }

        private static string ObterTexto(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static string ObterVersaoPeloLog(string rootPath)
        {
            try
            {
                string logsPath = Path.Combine(rootPath, "save", "logs");
                if (!Directory.Exists(logsPath))
                    return null;

                string latestLog = null;
                DateTime latestWrite = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(logsPath, "*.log"))
                {
                    DateTime write = File.GetLastWriteTimeUtc(file);
                    if (write > latestWrite)
                    {
                        latestWrite = write;
                        latestLog = file;
                    }
                }

                if (latestLog == null)
                    return null;

                foreach (string line in File.ReadLines(latestLog))
                {
                    if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                        return line.Substring("Version:".Length).Trim();
                }
            }
            catch
            {
            }

            return null;
        }
    }

    public sealed class IkemenPilotExecutionResult
    {
        public bool Success { get; set; }
        public bool ConfigurationError { get; set; }
        public bool TimedOut { get; set; }
        public string Message { get; set; }
        public int? WinSide { get; set; }
        public int? PontosPlayer1 { get; set; }
        public int? PontosPlayer2 { get; set; }
        public int? ExitCode { get; set; }
        public int DurationMs { get; set; }
        public string EngineVersion { get; set; }
        public string LogPath { get; set; }
        public string ErrorLogPath { get; set; }
    }

    public sealed class IkemenPilotRunner
    {
        public const int DefaultExecutionTimeoutMilliseconds = 5 * 60 * 1000;
        private const int ProcessTerminationWaitMilliseconds = 5000;
        private const int ShowMaximized = 3;
        private const uint SystemParametersInfoGetWorkArea = 0x0030;
        private const uint SetWindowPosNoZOrder = 0x0004;
        private const uint SetWindowPosNoActivate = 0x0010;
        private const uint SetWindowPosShowWindow = 0x0040;
        private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            out WindowRectangle rectangle,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        private static readonly Regex WinSideRegex = new Regex(
            @"\[""WinSide""\]\s*=>\s*(-?\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex WinsRegex = new Regex(
            @"\[""Wins""\]\s*=>\s*table:[^{]*\{\s*\[1\]\s*=>\s*(\d+)\s*\[2\]\s*=>\s*(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex DifficultyRegex = new Regex(
            @"^\s*Difficulty\s*=\s*(\d+)\s*(?:;.*)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private readonly IkemenPilotConfiguration _configuration;
        private readonly string _logsDirectory;
        private readonly string _configsDirectory;
        private readonly int _executionTimeoutMilliseconds;

        public IkemenPilotRunner(IkemenPilotConfiguration configuration, string dataDirectory)
            : this(configuration, dataDirectory, DefaultExecutionTimeoutMilliseconds)
        {
        }

        public IkemenPilotRunner(
            IkemenPilotConfiguration configuration,
            string dataDirectory,
            int executionTimeoutMilliseconds)
        {
            _configuration = configuration ?? throw new ArgumentNullException("configuration");

            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("Informe a pasta de dados do piloto.", "dataDirectory");
            if (executionTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException("executionTimeoutMilliseconds");

            _logsDirectory = Path.Combine(dataDirectory, "logs");
            _configsDirectory = Path.Combine(dataDirectory, "configs");
            _executionTimeoutMilliseconds = executionTimeoutMilliseconds;
            Directory.CreateDirectory(_logsDirectory);
            Directory.CreateDirectory(_configsDirectory);
        }

        public IkemenPilotExecutionResult ExecutarPartida(
            string player1,
            string player2,
            string stage,
            bool semSom,
            int? posicaoJanela)
        {
            if (posicaoJanela.HasValue && (posicaoJanela.Value < 0 || posicaoJanela.Value > 3))
                throw new ArgumentOutOfRangeException("posicaoJanela");

            return Executar(player1, player2, stage, "torneio", semSom, posicaoJanela);
        }

        private IkemenPilotExecutionResult Executar(
            string player1,
            string player2,
            string stage,
            string tipo,
            bool semSom,
            int? posicaoJanela)
        {
            var result = new IkemenPilotExecutionResult
            {
                EngineVersion = _configuration.EngineVersion
            };

            if (!_configuration.IsConfigured)
            {
                result.ConfigurationError = true;
                result.Message = _configuration.ConfigurationMessage;
                return result;
            }

            if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
            {
                result.ConfigurationError = true;
                result.Message = "Configure os dois personagens de apoio do piloto IKEMEN.";
                return result;
            }

            string stageArgument = NormalizarStage(stage ?? _configuration.DefaultStage);
            if (string.IsNullOrWhiteSpace(stageArgument))
            {
                result.ConfigurationError = true;
                result.Message = "Configure um cenario de apoio ou mantenha ao menos um cenario ativo no catalogo.";
                return result;
            }

            string player1Path;
            if (!ExisteLutador(player1, out player1Path))
            {
                result.ConfigurationError = true;
                result.Message = "O lutador '" + player1
                    + "' nao esta acessivel pelo IKEMEN. Arquivo esperado: " + player1Path
                    + ". Confira o vinculo da pasta chars com o acervo do MUGEN.";
                return result;
            }

            string player2Path;
            if (!ExisteLutador(player2, out player2Path))
            {
                result.ConfigurationError = true;
                result.Message = "O lutador de apoio '" + player2
                    + "' nao esta acessivel pelo IKEMEN. Arquivo esperado: " + player2Path
                    + ". Confira o vinculo da pasta chars com o acervo do MUGEN.";
                return result;
            }

            string expectedPath;
            if (!ExisteCenario(stageArgument, out expectedPath))
            {
                result.ConfigurationError = true;
                result.Message = "O cenario nao esta acessivel pelo IKEMEN. Arquivo esperado: "
                    + expectedPath + ". Confira o vinculo da pasta stages com o acervo do MUGEN.";
                return result;
            }

            string executionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string logPath = Path.Combine(_logsDirectory, tipo + "-" + executionId + ".txt");
            result.LogPath = logPath;

            var output = new StringBuilder();
            var stopwatch = Stopwatch.StartNew();
            string temporaryConfigPath = null;

            try
            {
                temporaryConfigPath = CriarConfigTemporario(executionId);
                int aiLevel = ObterNivelIa(temporaryConfigPath);
                string arguments = MontarArgumentos(
                    ObterReferenciaLutador(player1Path),
                    ObterReferenciaLutador(player2Path),
                    stageArgument,
                    logPath,
                    aiLevel,
                    temporaryConfigPath,
                    semSom);

                var startInfo = new ProcessStartInfo
                {
                    FileName = _configuration.ExecutablePath,
                    WorkingDirectory = _configuration.RootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            lock (output)
                                output.AppendLine(args.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            lock (output)
                                output.AppendLine(args.Data);
                        }
                    };

                    if (!process.Start())
                        throw new InvalidOperationException("O IKEMEN nao iniciou.");

                    var processStopwatch = Stopwatch.StartNew();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    AjustarJanela(process, posicaoJanela);

                    if (!AguardarConclusao(process, processStopwatch, _executionTimeoutMilliseconds))
                    {
                        result.TimedOut = true;
                        result.Message = "A luta excedeu o limite de 5 minutos e foi encerrada como erro. "
                            + "Nenhum resultado, XP ou Elo foi registrado.";
                        EncerrarProcesso(process);
                        result.ErrorLogPath = RegistrarErroExecucao(
                            logPath,
                            result.Message,
                            player1,
                            player2,
                            stageArgument,
                            ObterSaidaProcesso(output));
                        return result;
                    }

                    // Garante que toda a saida assincrona foi entregue antes de ler o resultado.
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }

                string processOutput = ObterSaidaProcesso(output);
                string detectedVersion = ExtrairVersao(processOutput);
                if (!string.IsNullOrWhiteSpace(detectedVersion))
                    result.EngineVersion = detectedVersion;

                if (result.ExitCode != 0)
                {
                    result.Message = MontarMensagemFalha(
                        "O IKEMEN encerrou com o codigo " + result.ExitCode + ".",
                        processOutput);
                    return result;
                }

                if (!File.Exists(logPath))
                {
                    result.Message = MontarMensagemFalha(
                        "O IKEMEN encerrou sem gerar o arquivo de resultado.",
                        processOutput);
                    return result;
                }

                string log = File.ReadAllText(logPath, Encoding.UTF8);
                Match winSideMatch = WinSideRegex.Match(log);
                Match winsMatch = WinsRegex.Match(log);
                int winSide = 0;
                int pontosPlayer1 = 0;
                int pontosPlayer2 = 0;
                bool possuiWinSide = winSideMatch.Success
                    && int.TryParse(winSideMatch.Groups[1].Value, out winSide);
                bool possuiPlacar = winsMatch.Success
                    && int.TryParse(winsMatch.Groups[1].Value, out pontosPlayer1)
                    && int.TryParse(winsMatch.Groups[2].Value, out pontosPlayer2);

                if (!possuiWinSide && !possuiPlacar)
                {
                    result.Message = "A luta terminou, mas o log nao contem o placar esperado.";
                    return result;
                }

                if (possuiPlacar)
                {
                    result.PontosPlayer1 = pontosPlayer1;
                    result.PontosPlayer2 = pontosPlayer2;
                    result.WinSide = pontosPlayer1 > pontosPlayer2
                        ? 1
                        : pontosPlayer2 > pontosPlayer1 ? 2 : 0;
                }
                else
                {
                    result.WinSide = winSide;
                }

                result.Success = true;
                result.Message = result.WinSide == 1 || result.WinSide == 2
                    ? "Conteudo carregado e luta concluida pelo lado " + result.WinSide + "."
                    : "Conteudo carregado e luta concluida com sucesso.";
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "Falha ao executar o piloto IKEMEN: " + ex.Message;
                return result;
            }
            finally
            {
                ExcluirConfigTemporario(temporaryConfigPath);
                stopwatch.Stop();
                result.DurationMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
            }
        }

        private static bool AguardarConclusao(
            Process process,
            Stopwatch processStopwatch,
            int executionTimeoutMilliseconds)
        {
            if (process == null)
                throw new ArgumentNullException("process");
            if (processStopwatch == null)
                throw new ArgumentNullException("processStopwatch");

            long remainingMilliseconds = executionTimeoutMilliseconds - processStopwatch.ElapsedMilliseconds;
            int waitMilliseconds = remainingMilliseconds <= 0
                ? 0
                : (int)Math.Min(int.MaxValue, remainingMilliseconds);

            return process.HasExited || process.WaitForExit(waitMilliseconds);
        }

        private static void EncerrarProcesso(Process process)
        {
            if (process == null)
                return;

            try
            {
                if (process.HasExited)
                    return;

                process.Kill();
                process.WaitForExit(ProcessTerminationWaitMilliseconds);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static string ObterSaidaProcesso(StringBuilder output)
        {
            lock (output)
                return output.ToString();
        }

        private static string RegistrarErroExecucao(
            string logPath,
            string message,
            string player1,
            string player2,
            string stage,
            string processOutput)
        {
            try
            {
                string directory = Path.GetDirectoryName(logPath);
                string fileName = Path.GetFileNameWithoutExtension(logPath) + "-erro.txt";
                string errorLogPath = Path.Combine(directory, fileName);
                var log = new StringBuilder()
                    .AppendLine("Data UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                    .AppendLine("Erro: " + message)
                    .AppendLine("Player 1: " + player1)
                    .AppendLine("Player 2: " + player2)
                    .AppendLine("Cenario: " + stage);

                if (!string.IsNullOrWhiteSpace(processOutput))
                {
                    log.AppendLine()
                        .AppendLine("Saida do IKEMEN:")
                        .Append(processOutput.Trim());
                }

                File.WriteAllText(errorLogPath, log.ToString(), Encoding.UTF8);
                return errorLogPath;
            }
            catch
            {
                return null;
            }
        }

        private static string MontarArgumentos(
            string player1,
            string player2,
            string stage,
            string logPath,
            int aiLevel,
            string configPath,
            bool semSom)
        {
            var arguments = new List<string>
            {
                "-p1", Quote(player1),
                "-p2", Quote(player2),
                "-p1.ai", aiLevel.ToString(),
                "-p2.ai", aiLevel.ToString()
            };

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                arguments.Add("-config");
                arguments.Add(Quote(configPath));
            }

            if (semSom)
                arguments.Add("-nosound");

            arguments.Add("-s");
            arguments.Add(Quote(stage));
            arguments.Add("-windowed");
            arguments.Add("-log");
            arguments.Add(Quote(logPath));

            return string.Join(" ", arguments);
        }

        private string CriarConfigTemporario(string executionId)
        {
            string sourcePath = Path.Combine(_configuration.RootPath, "save", "config.ini");
            if (!File.Exists(sourcePath))
                return null;

            string destinationPath = Path.Combine(_configsDirectory, "config-" + executionId + ".ini");
            File.Copy(sourcePath, destinationPath, false);
            return destinationPath;
        }

        private static int ObterNivelIa(string configPath)
        {
            const int defaultLevel = 8;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                return defaultLevel;

            Match match = DifficultyRegex.Match(File.ReadAllText(configPath));
            int level;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out level))
                return defaultLevel;

            return Math.Max(1, Math.Min(8, level));
        }

        private static void ExcluirConfigTemporario(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
                return;

            try
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);
            }
            catch
            {
            }
        }

        private static void AjustarJanela(Process process, int? posicaoJanela)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (process.HasExited)
                    return;

                IntPtr windowHandle = ObterJanelaDoProcesso(process.Id);
                if (windowHandle != IntPtr.Zero)
                {
                    if (posicaoJanela.HasValue)
                        PosicionarNoQuadrante(windowHandle, posicaoJanela.Value);
                    else
                        ShowWindowAsync(windowHandle, ShowMaximized);
                    return;
                }

                Thread.Sleep(100);
            }
        }

        private static void PosicionarNoQuadrante(IntPtr windowHandle, int posicao)
        {
            WindowRectangle workArea;
            if (!SystemParametersInfo(SystemParametersInfoGetWorkArea, 0, out workArea, 0))
            {
                ShowWindowAsync(windowHandle, ShowMaximized);
                return;
            }

            int[] rectangle = CalcularQuadrante(
                workArea.Left,
                workArea.Top,
                workArea.Right,
                workArea.Bottom,
                posicao);
            SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                rectangle[0],
                rectangle[1],
                rectangle[2],
                rectangle[3],
                SetWindowPosNoZOrder | SetWindowPosNoActivate | SetWindowPosShowWindow);
        }

        private static int[] CalcularQuadrante(
            int left,
            int top,
            int right,
            int bottom,
            int posicao)
        {
            int totalWidth = Math.Max(1, right - left);
            int totalHeight = Math.Max(1, bottom - top);
            int leftWidth = totalWidth / 2;
            int topHeight = totalHeight / 2;
            bool rightSide = posicao % 2 == 1;
            bool bottomSide = posicao >= 2;

            return new[]
            {
                rightSide ? left + leftWidth : left,
                bottomSide ? top + topHeight : top,
                rightSide ? totalWidth - leftWidth : leftWidth,
                bottomSide ? totalHeight - topHeight : topHeight
            };
        }

        private static IntPtr ObterJanelaDoProcesso(int processId)
        {
            IntPtr foundWindow = IntPtr.Zero;
            EnumWindows(delegate(IntPtr windowHandle, IntPtr parameter)
            {
                uint ownerProcessId;
                GetWindowThreadProcessId(windowHandle, out ownerProcessId);
                if (ownerProcessId == (uint)processId && IsWindowVisible(windowHandle))
                {
                    foundWindow = windowHandle;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return foundWindow;
        }

        private static string NormalizarStage(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage))
                return null;

            string normalized = stage.Trim().Replace('\\', '/');
            if (!normalized.EndsWith(".def", StringComparison.OrdinalIgnoreCase))
                normalized += ".def";

            if (!normalized.StartsWith("stages/", StringComparison.OrdinalIgnoreCase)
                && !Path.IsPathRooted(normalized))
            {
                normalized = "stages/" + normalized;
            }

            return normalized;
        }

        private bool ExisteLutador(string reference, out string expectedPath)
        {
            expectedPath = null;
            if (string.IsNullOrWhiteSpace(reference))
                return false;

            string normalized = reference.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
            {
                expectedPath = Path.GetFullPath(normalized);
                return File.Exists(expectedPath);
            }

            normalized = normalized.TrimStart(Path.DirectorySeparatorChar);
            if (normalized.StartsWith("chars" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(("chars" + Path.DirectorySeparatorChar).Length);

            if (normalized.EndsWith(".def", StringComparison.OrdinalIgnoreCase))
            {
                expectedPath = Path.Combine(_configuration.RootPath, "chars", normalized);
                return File.Exists(expectedPath);
            }

            string leafName = Path.GetFileName(normalized);
            expectedPath = Path.Combine(
                _configuration.RootPath,
                "chars",
                normalized,
                leafName + ".def");

            if (File.Exists(expectedPath))
                return true;

            string flatPath = Path.Combine(_configuration.RootPath, "chars", normalized + ".def");
            if (File.Exists(flatPath))
            {
                expectedPath = flatPath;
                return true;
            }

            string resolvedPath = LocalizarDefPorIdentidade(reference);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                expectedPath = resolvedPath;
                return true;
            }

            return false;
        }

        private string ObterReferenciaLutador(string defPath)
        {
            string charsRoot = Path.GetFullPath(Path.Combine(_configuration.RootPath, "chars"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(defPath);
            if (!fullPath.StartsWith(charsRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath.Replace('\\', '/');

            return fullPath.Substring(charsRoot.Length).Replace('\\', '/');
        }

        private string LocalizarDefPorIdentidade(string reference)
        {
            string normalizedReference = NormalizarIdentificador(reference);
            if (normalizedReference.Length == 0)
                return null;

            string bestPath = null;
            int bestScore = 0;
            bool tied = false;
            foreach (string defPath in ObterDefsPrincipais())
            {
                int score = PontuarIdentidade(
                    reference,
                    normalizedReference,
                    Path.GetFileNameWithoutExtension(defPath),
                    85);
                score = Math.Max(score, PontuarIdentidade(
                    reference,
                    normalizedReference,
                    Path.GetFileName(Path.GetDirectoryName(defPath)),
                    90));

                foreach (string identity in LerIdentidadesDef(defPath))
                    score = Math.Max(score, PontuarIdentidade(reference, normalizedReference, identity, 100));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = defPath;
                    tied = false;
                }
                else if (score > 0 && score == bestScore
                    && !string.Equals(bestPath, defPath, StringComparison.OrdinalIgnoreCase))
                {
                    tied = true;
                }
            }

            return bestScore > 0 && !tied ? bestPath : null;
        }

        private IEnumerable<string> ObterDefsPrincipais()
        {
            string charsPath = Path.Combine(_configuration.RootPath, "chars");
            foreach (string characterDirectory in Directory.GetDirectories(charsPath))
            {
                string directoryName = Path.GetFileName(characterDirectory);
                string preferredDef = Path.Combine(characterDirectory, directoryName + ".def");
                if (File.Exists(preferredDef))
                {
                    yield return preferredDef;
                    continue;
                }

                string[] definitions = Directory.GetFiles(characterDirectory, "*.def", SearchOption.TopDirectoryOnly);
                if (definitions.Length > 0)
                    yield return definitions[0];
            }

            foreach (string definition in Directory.GetFiles(charsPath, "*.def", SearchOption.TopDirectoryOnly))
                yield return definition;
        }

        private static IEnumerable<string> LerIdentidadesDef(string defPath)
        {
            var identities = new List<string>();
            try
            {
                string section = string.Empty;
                foreach (string rawLine in File.ReadAllLines(defPath, Encoding.Default))
                {
                    string line = RemoverComentario(rawLine).Trim();
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
                    if (!key.Equals("name", StringComparison.OrdinalIgnoreCase)
                        && !key.Equals("displayname", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line.Substring(separator + 1).Trim().Trim('"', '\'').Trim();
                    if (value.Length > 0)
                        identities.Add(value);
                }
            }
            catch
            {
            }

            return identities;
        }

        private static int PontuarIdentidade(
            string reference,
            string normalizedReference,
            string candidate,
            int exactScore)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return 0;
            if (string.Equals(reference.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                return exactScore;

            string normalizedCandidate = NormalizarIdentificador(candidate);
            if (normalizedCandidate == normalizedReference)
                return exactScore - 10;

            int lengthDifference = Math.Abs(normalizedCandidate.Length - normalizedReference.Length);
            bool prefix = normalizedCandidate.StartsWith(normalizedReference, StringComparison.Ordinal)
                || normalizedReference.StartsWith(normalizedCandidate, StringComparison.Ordinal);
            return prefix && lengthDifference > 0 && lengthDifference <= 3
                ? 40 - lengthDifference
                : 0;
        }

        private static string NormalizarIdentificador(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder();
            foreach (char character in value.Trim().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string RemoverComentario(string value)
        {
            int index = (value ?? string.Empty).IndexOf(';');
            return index >= 0 ? value.Substring(0, index) : value ?? string.Empty;
        }

        private bool ExisteCenario(string stage, out string expectedPath)
        {
            expectedPath = null;
            if (string.IsNullOrWhiteSpace(stage))
                return false;

            string normalized = stage.Trim().Replace('/', Path.DirectorySeparatorChar);
            expectedPath = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.Combine(_configuration.RootPath, normalized);

            return File.Exists(expectedPath);
        }

        private static string ExtrairVersao(string processOutput)
        {
            if (string.IsNullOrWhiteSpace(processOutput))
                return null;

            Match match = Regex.Match(
                processOutput,
                @"(?:^|\r?\n)Version:\s*([^\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string MontarMensagemFalha(string prefix, string processOutput)
        {
            if (string.IsNullOrWhiteSpace(processOutput))
                return prefix;

            string trimmed = processOutput.Trim();
            if (trimmed.Length > 1200)
                trimmed = trimmed.Substring(trimmed.Length - 1200);

            return prefix + " " + trimmed;
        }
    }
}
