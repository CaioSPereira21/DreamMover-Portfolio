using DreamMover.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DreamMover.Helpers
{
    public sealed class IkemenStagePreviewGenerator
    {
        private const uint KeyDown = 0x0100;
        private const uint KeyUp = 0x0101;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint PrintWindowClientOnly = 0x00000001;
        private const uint PrintWindowRenderFullContent = 0x00000002;
        private const uint SetWindowPosNoSize = 0x0001;
        private const uint SetWindowPosNoMove = 0x0002;
        private const uint SetWindowPosShowWindow = 0x0040;
        private const int VirtualKeyF12 = 0x7B;
        private const int ShowRestore = 9;
        private static readonly IntPtr WindowTopMost = new IntPtr(-1);
        private static readonly IntPtr WindowNotTopMost = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
        private const string PreviewCharacterPrefix = "_DreamMoverIkemenPreview";

        private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr windowHandle, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr windowHandle, IntPtr deviceContext, uint flags);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private readonly IkemenPilotConfiguration _configuration;

        public IkemenStagePreviewGenerator()
            : this(IkemenPilotConfiguration.Load())
        {
        }

        public IkemenStagePreviewGenerator(IkemenPilotConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException("configuration");
        }

        public IkemenStagePreviewResult Generate(
            string stageName,
            string player1,
            string player2,
            string destinationPath)
        {
            if (!_configuration.IsConfigured)
                throw new InvalidOperationException(_configuration.ConfigurationMessage);
            if (string.IsNullOrWhiteSpace(stageName))
                throw new ArgumentException("Informe o arquivo do cenario.", "stageName");
            if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
                throw new InvalidOperationException("Configure dois personagens para a captura do cenario.");
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Informe o destino da imagem.", "destinationPath");

            string stageReference = NormalizarStage(stageName);
            string stagePath = Path.GetFullPath(Path.Combine(
                _configuration.RootPath,
                stageReference.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(stagePath))
                throw new FileNotFoundException("O cenario nao esta acessivel pelo IKEMEN.", stagePath);

            string previewPlayer1 = EnsurePreviewCharacter(player1, 1);
            string previewPlayer2 = EnsurePreviewCharacter(player2, 2);
            string executionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string runtimeDirectory = Path.Combine(
                Path.GetTempPath(),
                "DreamMover-Ikemen-StagePreview-" + executionId);
            string screenshotsDirectory = Path.Combine(runtimeDirectory, "screenshots");
            string logPath = Path.Combine(runtimeDirectory, "ikemen-preview.log");
            string configPath = Path.Combine(runtimeDirectory, "config.ini");
            Process process = null;

            Directory.CreateDirectory(screenshotsDirectory);

            try
            {
                CriarConfigTemporario(configPath, screenshotsDirectory);
                DateTime startedAtUtc = DateTime.UtcNow;
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _configuration.ExecutablePath,
                    WorkingDirectory = _configuration.RootPath,
                    Arguments = MontarArgumentos(
                        previewPlayer1,
                        previewPlayer2,
                        stageReference,
                        configPath,
                        logPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                    throw new InvalidOperationException("O IKEMEN nao iniciou para gerar a imagem.");

                DateTime captureAfter = DateTime.UtcNow.AddSeconds(4);
                DateTime directCaptureAfter = DateTime.UtcNow.AddSeconds(8);
                DateTime deadline = DateTime.UtcNow.AddSeconds(45);
                DateTime nextCaptureRequest = captureAfter;
                bool captureRequested = false;
                bool directCaptureRequested = false;
                string screenshot = null;

                while (DateTime.UtcNow < deadline)
                {
                    process.Refresh();
                    if (process.HasExited)
                        throw new InvalidOperationException(
                            "O IKEMEN encerrou antes de gerar a imagem do cenario. "
                            + ObterDetalheLog(logPath));

                    if (DateTime.UtcNow >= nextCaptureRequest)
                    {
                        IntPtr windowHandle = ObterJanelaDoProcesso(process.Id);
                        if (windowHandle != IntPtr.Zero)
                        {
                            if (!captureRequested)
                                SolicitarCaptura(windowHandle);
                            else
                                EnviarMensagensCaptura(windowHandle);
                            captureRequested = true;

                            if (!directCaptureRequested && DateTime.UtcNow >= directCaptureAfter)
                            {
                                string directCapturePath = Path.Combine(
                                    screenshotsDirectory,
                                    "ikemen-window.png");
                                CapturarJanela(windowHandle, directCapturePath);
                                directCaptureRequested = true;
                            }
                        }

                        nextCaptureRequest = DateTime.UtcNow.AddMilliseconds(750);
                    }

                    if (captureRequested)
                    {
                        screenshot = FindNewScreenshot(
                            ObterPastasDeCaptura(screenshotsDirectory),
                            startedAtUtc);
                        if (!string.IsNullOrWhiteSpace(screenshot))
                            break;
                    }

                    Thread.Sleep(100);
                }

                if (string.IsNullOrWhiteSpace(screenshot))
                    throw new TimeoutException(
                        "O IKEMEN nao gerou a captura no tempo esperado. "
                        + ObterDetalheLog(logPath));

                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                SaveAsPng(screenshot, destinationPath);

                using (Image image = Image.FromFile(destinationPath))
                {
                    return new IkemenStagePreviewResult
                    {
                        OutputPath = destinationPath,
                        Width = image.Width,
                        Height = image.Height
                    };
                }
            }
            finally
            {
                EncerrarProcesso(process);
                ExcluirDiretorioTemporario(runtimeDirectory);
            }
        }

        private string EnsurePreviewCharacter(string configuredCharacter, int playerNumber)
        {
            string charsRoot = Path.Combine(_configuration.RootPath, "chars");
            string sourceFolder = ResolveCharacterFolder(charsRoot, configuredCharacter);
            MugenCharacterFiles files = MugenCharacterFiles.FromFolder(sourceFolder);

            if (string.IsNullOrWhiteSpace(files.SffPath) || !File.Exists(files.SffPath))
                throw new FileNotFoundException("Nao encontrei o arquivo .sff do personagem de apoio.", sourceFolder);

            string characterName = PreviewCharacterPrefix + playerNumber;
            string characterFolder = Path.Combine(charsRoot, characterName);
            Directory.CreateDirectory(characterFolder);

            string spritePath = MakeRelativePath(characterFolder, files.SffPath);
            string animationPath = MakeRelativePath(characterFolder, files.AirPath);

            File.WriteAllText(
                Path.Combine(characterFolder, characterName + ".def"),
                BuildDefinition(characterName, playerNumber, spritePath, animationPath),
                Encoding.ASCII);
            File.WriteAllText(Path.Combine(characterFolder, "preview.cns"), BuildConstants(), Encoding.ASCII);
            File.WriteAllText(Path.Combine(characterFolder, "preview.st"), BuildStates(), Encoding.ASCII);
            File.WriteAllText(Path.Combine(characterFolder, "preview.cmd"), BuildCommands(), Encoding.ASCII);

            return characterName + "/" + characterName + ".def";
        }

        private static string ResolveCharacterFolder(string charsRoot, string configuredCharacter)
        {
            string normalized = EscapeArgument(configuredCharacter)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string directPath = Path.Combine(charsRoot, normalized);

            if (Directory.Exists(directPath))
                return directPath;

            string characterName = Path.GetFileNameWithoutExtension(normalized);
            string matchingFolder = Directory.GetDirectories(charsRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(folder =>
                    string.Equals(Path.GetFileName(folder), characterName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingFolder))
                return matchingFolder;

            throw new DirectoryNotFoundException(
                "Nao encontrei o personagem de apoio '" + configuredCharacter + "' no IKEMEN.");
        }

        private void CriarConfigTemporario(string destinationPath, string screenshotsDirectory)
        {
            string sourcePath = Path.Combine(_configuration.RootPath, "save", "config.ini");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("A configuracao do IKEMEN nao foi encontrada.", sourcePath);

            string content = File.ReadAllText(sourcePath);
            content = AtualizarConfiguracao(
                content,
                "ScreenshotFolder",
                screenshotsDirectory.Replace('\\', '/'));
            content = AtualizarConfiguracao(content, "Time", "999");
            content = AtualizarConfiguracao(content, "Match.Wins", "10");
            content = AtualizarConfiguracao(content, "Match.MaxDrawGames", "-1");
            content = AtualizarConfiguracao(content, "GameWidth", "960");
            content = AtualizarConfiguracao(content, "GameHeight", "540");
            content = AtualizarConfiguracao(content, "Fullscreen", "0");
            File.WriteAllText(destinationPath, content, new UTF8Encoding(false));
        }

        private static string AtualizarConfiguracao(string content, string key, string value)
        {
            Regex setting = new Regex(
                "^(?<prefix>\\s*" + Regex.Escape(key) + "\\s*=).*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (setting.IsMatch(content))
            {
                return setting.Replace(
                    content,
                    match => match.Groups["prefix"].Value + " " + value,
                    1);
            }

            return content.TrimEnd() + Environment.NewLine + key + " = " + value + Environment.NewLine;
        }

        private static string MontarArgumentos(
            string player1,
            string player2,
            string stage,
            string configPath,
            string logPath)
        {
            return string.Join(" ", new[]
            {
                "-p1", Quote(player1),
                "-p2", Quote(player2),
                "-p1.ai", "0",
                "-p2.ai", "0",
                "-config", Quote(configPath),
                "-nosound",
                "-s", Quote(stage),
                "-windowed",
                "-log", Quote(logPath)
            });
        }

        private IEnumerable<string> ObterPastasDeCaptura(string screenshotsDirectory)
        {
            yield return screenshotsDirectory;
            yield return _configuration.RootPath;
            yield return Path.Combine(_configuration.RootPath, "screenshots");
            yield return Path.Combine(_configuration.RootPath, "save", "screenshots");
        }

        private static IntPtr ObterJanelaDoProcesso(int processId)
        {
            IntPtr visibleWindow = IntPtr.Zero;
            long largestVisibleArea = -1;
            IntPtr hiddenWindow = IntPtr.Zero;
            long largestHiddenArea = -1;
            EnumWindows(delegate(IntPtr windowHandle, IntPtr parameter)
            {
                uint ownerProcessId;
                GetWindowThreadProcessId(windowHandle, out ownerProcessId);
                if (ownerProcessId == (uint)processId)
                {
                    NativeRectangle rectangle;
                    long area = 0;
                    if (GetClientRect(windowHandle, out rectangle))
                    {
                        area = Math.Max(0, rectangle.Right - rectangle.Left)
                            * (long)Math.Max(0, rectangle.Bottom - rectangle.Top);
                    }

                    if (IsWindowVisible(windowHandle))
                    {
                        if (area > largestVisibleArea)
                        {
                            visibleWindow = windowHandle;
                            largestVisibleArea = area;
                        }
                    }
                    else if (area > largestHiddenArea)
                    {
                        hiddenWindow = windowHandle;
                        largestHiddenArea = area;
                    }
                }

                return true;
            }, IntPtr.Zero);

            return visibleWindow != IntPtr.Zero ? visibleWindow : hiddenWindow;
        }

        private static void SolicitarCaptura(IntPtr windowHandle)
        {
            // IKEMEN le o teclado pela fila de eventos do SDL. As mensagens direcionadas
            // continuam sendo enviadas para compatibilidade, mas a tecla injetada faz o
            // SDL enxergar o F12 nas versoes atuais da engine.
            EnviarMensagensCaptura(windowHandle);

            IntPtr previousForeground = GetForegroundWindow();
            ShowWindowAsync(windowHandle, ShowRestore);
            SetWindowPos(
                windowHandle,
                WindowTopMost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
            SetForegroundWindow(windowHandle);
            Thread.Sleep(60);
            keybd_event((byte)VirtualKeyF12, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VirtualKeyF12, 0, KeyEventKeyUp, UIntPtr.Zero);
            Thread.Sleep(60);
            SetWindowPos(
                windowHandle,
                WindowNotTopMost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);

            if (previousForeground != IntPtr.Zero && previousForeground != windowHandle)
                SetForegroundWindow(previousForeground);
        }

        private static void EnviarMensagensCaptura(IntPtr windowHandle)
        {
            PostMessage(windowHandle, KeyDown, new IntPtr(VirtualKeyF12), IntPtr.Zero);
            PostMessage(windowHandle, KeyUp, new IntPtr(VirtualKeyF12), IntPtr.Zero);
        }

        private static void CapturarJanela(IntPtr windowHandle, string destinationPath)
        {
            NativeRectangle rectangle;
            if (!GetClientRect(windowHandle, out rectangle))
                throw new InvalidOperationException("Nao foi possivel medir a janela do IKEMEN.");

            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("A janela do IKEMEN ainda nao esta pronta para captura.");

            IntPtr previousForeground = GetForegroundWindow();
            ShowWindowAsync(windowHandle, ShowRestore);
            SetWindowPos(
                windowHandle,
                WindowTopMost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            Thread.Sleep(300);

            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                bool captured = false;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr deviceContext = graphics.GetHdc();
                    try
                    {
                        captured = PrintWindow(
                            windowHandle,
                            deviceContext,
                            PrintWindowClientOnly | PrintWindowRenderFullContent);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(deviceContext);
                    }
                }

                if (!captured || !ImagemTemConteudo(bitmap))
                {
                    NativePoint origin = new NativePoint();
                    if (!ClientToScreen(windowHandle, ref origin))
                        throw new InvalidOperationException("Nao foi possivel localizar a janela do IKEMEN.");

                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(
                            origin.X,
                            origin.Y,
                            0,
                            0,
                            new Size(width, height),
                            CopyPixelOperation.SourceCopy);
                    }
                }

                bitmap.Save(destinationPath, ImageFormat.Png);
            }

            SetWindowPos(
                windowHandle,
                WindowNotTopMost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);

            if (previousForeground != IntPtr.Zero && previousForeground != windowHandle)
                SetForegroundWindow(previousForeground);
        }

        private static bool ImagemTemConteudo(Bitmap bitmap)
        {
            var colors = new HashSet<int>();
            for (int y = 1; y <= 5; y++)
            {
                int sampleY = Math.Min(bitmap.Height - 1, bitmap.Height * y / 6);
                for (int x = 1; x <= 7; x++)
                {
                    int sampleX = Math.Min(bitmap.Width - 1, bitmap.Width * x / 8);
                    colors.Add(bitmap.GetPixel(sampleX, sampleY).ToArgb());
                }
            }

            return colors.Count >= 4;
        }

        private static IEnumerable<string> ObterArquivos(string folder)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    : Enumerable.Empty<string>();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private static string FindNewScreenshot(IEnumerable<string> folders, DateTime startedAtUtc)
        {
            return folders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(ObterArquivos)
                .Where(IsSupportedScreenshot)
                .Select(path => new FileInfo(path))
                .Where(file => file.LastWriteTimeUtc >= startedAtUtc.AddSeconds(-1) && file.Length > 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }

        private static bool IsSupportedScreenshot(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveAsPng(string sourcePath, string destinationPath)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            long previousLength = -1;
            DateTime previousWriteTimeUtc = DateTime.MinValue;
            int stableObservations = 0;
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    FileInfo screenshot = new FileInfo(sourcePath);
                    screenshot.Refresh();

                    if (!screenshot.Exists || screenshot.Length <= 0)
                    {
                        stableObservations = 0;
                    }
                    else if (screenshot.Length == previousLength
                        && screenshot.LastWriteTimeUtc == previousWriteTimeUtc)
                    {
                        stableObservations++;
                    }
                    else
                    {
                        stableObservations = 0;
                    }

                    previousLength = screenshot.Exists ? screenshot.Length : -1;
                    previousWriteTimeUtc = screenshot.Exists
                        ? screenshot.LastWriteTimeUtc
                        : DateTime.MinValue;

                    if (stableObservations >= 2)
                    {
                        byte[] imageBytes;
                        using (FileStream input = new FileStream(
                            sourcePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete))
                        using (MemoryStream buffer = new MemoryStream())
                        {
                            input.CopyTo(buffer);
                            imageBytes = buffer.ToArray();
                        }

                        using (MemoryStream inputImage = new MemoryStream(imageBytes, false))
                        using (Image source = Image.FromStream(inputImage, true, true))
                        using (Bitmap copy = new Bitmap(source))
                        using (MemoryStream outputImage = new MemoryStream())
                        {
                            copy.Save(outputImage, ImageFormat.Png);
                            File.WriteAllBytes(destinationPath, outputImage.ToArray());
                            return;
                        }
                    }
                }
                catch (Exception ex) when (EhFalhaTemporariaDaImagem(ex))
                {
                    lastError = ex;
                    stableObservations = 0;
                }

                Thread.Sleep(180);
            }

            throw new InvalidOperationException(
                "O IKEMEN criou a captura, mas o arquivo de imagem nao ficou pronto para leitura.",
                lastError);
        }

        private static bool EhFalhaTemporariaDaImagem(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is OutOfMemoryException
                || ex is ArgumentException
                || ex is ExternalException;
        }

        private static void EncerrarProcesso(Process process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private static void ExcluirDiretorioTemporario(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;

            try
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(directory);
                if (fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(fullPath).StartsWith(
                        "DreamMover-Ikemen-StagePreview-",
                        StringComparison.Ordinal))
                {
                    Directory.Delete(fullPath, true);
                }
            }
            catch
            {
            }
        }

        private static string ObterDetalheLog(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                    return string.Empty;

                string[] lines = File.ReadAllLines(logPath);
                string detail = lines.Reverse()
                    .FirstOrDefault(line =>
                        line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0);
                return string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizarStage(string stage)
        {
            string normalized = EscapeArgument(stage).Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("stages/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("stages/".Length);
            if (!normalized.EndsWith(".def", StringComparison.OrdinalIgnoreCase))
                normalized += ".def";
            return "stages/" + normalized;
        }

        private static string MakeRelativePath(string fromFolder, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return string.Empty;

            Uri from = new Uri(AppendDirectorySeparator(fromFolder));
            Uri target = new Uri(targetPath);
            return Uri.UnescapeDataString(from.MakeRelativeUri(target).ToString()).Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string BuildDefinition(
            string characterName,
            int playerNumber,
            string spritePath,
            string animationPath)
        {
            return string.Join("\r\n", new[]
            {
                "[Info]",
                "name = \"DreamMover IKEMEN Stage Preview " + playerNumber + "\"",
                "displayname = \"\"",
                "versiondate = 08,20,2026",
                "mugenversion = 1.0",
                "author = \"DreamMover\"",
                "pal.defaults = 1",
                string.Empty,
                "[Files]",
                "cmd = preview.cmd",
                "cns = preview.cns",
                "st = preview.st",
                "sprite = " + spritePath,
                "anim = " + animationPath,
                string.Empty,
                "[Arcade]",
                "intro.storyboard =",
                "ending.storyboard =",
                string.Empty
            });
        }

        private static string BuildConstants()
        {
            return string.Join("\r\n", new[]
            {
                "[Data]",
                "life = 1000",
                "power = 3000",
                "attack = 1",
                "defence = 100",
                "fall.defence_up = 50",
                "liedown.time = 60",
                "airjuggle = 15",
                "sparkno = 0",
                "guard.sparkno = 0",
                "ko.echo = 0",
                "volume = 0",
                "IntPersistIndex = 60",
                "FloatPersistIndex = 40",
                string.Empty,
                "[Size]",
                "xscale = 1",
                "yscale = 1",
                "ground.back = 15",
                "ground.front = 16",
                "air.back = 12",
                "air.front = 12",
                "height = 60",
                "attack.dist = 160",
                "proj.attack.dist = 90",
                "proj.doscale = 0",
                "head.pos = 0,-60",
                "mid.pos = 0,-40",
                "shadowoffset = 0",
                "draw.offset = 0,0",
                string.Empty,
                "[Velocity]",
                "walk.fwd = 0",
                "walk.back = 0",
                "run.fwd = 0,0",
                "run.back = 0,0",
                "jump.neu = 0,0",
                "jump.back = 0,0",
                "jump.fwd = 0,0",
                "runjump.back = 0,0",
                "runjump.fwd = 0,0",
                "airjump.neu = 0,0",
                "airjump.back = 0,0",
                "airjump.fwd = 0,0",
                string.Empty,
                "[Movement]",
                "airjump.num = 0",
                "airjump.height = 35",
                "yaccel = .44",
                "stand.friction = .85",
                "crouch.friction = .82",
                string.Empty
            });
        }

        private static string BuildStates()
        {
            return string.Join("\r\n", new[]
            {
                "[Statedef -2]",
                string.Empty,
                "[State -2, Hide character]",
                "type = AssertSpecial",
                "trigger1 = 1",
                "flag = invisible",
                string.Empty,
                "[State -2, Hide life bars]",
                "type = AssertSpecial",
                "trigger1 = 1",
                "flag = nobardisplay",
                string.Empty,
                "[State -2, Stop]",
                "type = VelSet",
                "trigger1 = 1",
                "x = 0",
                "y = 0",
                string.Empty,
                "[Statedef 0]",
                "type = S",
                "movetype = I",
                "physics = S",
                "anim = 0",
                "velset = 0,0",
                "ctrl = 0",
                "sprpriority = 0",
                string.Empty,
                "[State 0, Remain idle]",
                "type = ChangeState",
                "trigger1 = AnimTime = 0",
                "value = 0",
                string.Empty
            });
        }

        private static string BuildCommands()
        {
            return string.Join("\r\n", new[]
            {
                "[Remap]",
                "x = x",
                "y = y",
                "z = z",
                "a = a",
                "b = b",
                "c = c",
                "s = s",
                string.Empty,
                "[Defaults]",
                "command.time = 15",
                "command.buffer.time = 1",
                string.Empty,
                "[Command]",
                "name = \"nothing\"",
                "command = x",
                "time = 1",
                string.Empty,
                "[Statedef -1]",
                string.Empty,
                "[State -1, No commands]",
                "type = Null",
                "trigger1 = 1",
                string.Empty
            });
        }

        private static string Quote(string value)
        {
            return "\"" + EscapeArgument(value) + "\"";
        }

        private static string EscapeArgument(string value)
        {
            return (value ?? string.Empty).Replace("\"", string.Empty).Trim();
        }
    }

    public sealed class IkemenStagePreviewResult
    {
        public string OutputPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
