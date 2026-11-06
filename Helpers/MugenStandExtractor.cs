using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamMover.Helpers
{
    public class MugenStandExtractor
    {
        private const int StandActionNumber = 0;
        private const int MugenTicksPerSecond = 60;
        private readonly string charsPath;
        private readonly string outputPath;

        public MugenStandExtractor(string charsPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(charsPath))
                throw new ArgumentException("O caminho da pasta chars do MUGEN nao foi configurado.", "charsPath");

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("O caminho da pasta de saida das imagens nao foi configurado.", "outputPath");

            this.charsPath = charsPath;
            this.outputPath = outputPath;
        }

        public MugenStandExtractionResult ExtractStandFromCharacter(
            string characterName,
            string characterFolder = null,
            string outputFileName = null,
            bool overwrite = true,
            int maxFrames = 80,
            bool useActPalette = true,
            int padding = 0,
            int? transparentIndex = null,
            bool preserveSpriteCanvas = false)
        {
            if (string.IsNullOrWhiteSpace(characterName) && string.IsNullOrWhiteSpace(characterFolder))
                throw new ArgumentException("Informe o nome do personagem ou a pasta de origem.");

            Directory.CreateDirectory(outputPath);

            string resolvedFolder = ResolveCharacterFolder(characterName, characterFolder);
            string safeOutputFileName = NormalizeGifFileName(outputFileName, characterName, resolvedFolder);
            string destinationPath = Path.Combine(outputPath, safeOutputFileName);

            if (!overwrite && File.Exists(destinationPath))
            {
                return new MugenStandExtractionResult
                {
                    Sucesso = true,
                    Ignorado = true,
                    Mensagem = "O stand ja existe e sobrescrever esta desativado.",
                    CharacterFolder = resolvedFolder,
                    OutputPath = destinationPath,
                    OutputFileName = safeOutputFileName
                };
            }

            MugenCharacterFiles files = MugenCharacterFiles.FromFolder(resolvedFolder);
            AirAnimation action = AirFileReader.GetAction(files.AirPath, StandActionNumber);
            List<AirFrame> actionFrames = StandLoopSelector.SelectFrames(action);

            if (maxFrames > 0 && actionFrames.Count > maxFrames)
                actionFrames = actionFrames.Take(maxFrames).ToList();

            if (actionFrames.Count == 0)
                actionFrames.Add(new AirFrame(0, 0, 0, 0, 8, false, false));

            byte? transparentIndexOverride = NormalizeTransparentIndex(transparentIndex);
            ISpriteSource spriteSource = CreateSpriteSource(files, transparentIndexOverride);
            byte[] actPalette = !string.IsNullOrWhiteSpace(files.PalettePath)
                ? PaletteReader.ReadActPalette(files.PalettePath)
                : null;
            byte[] referencePalette = spriteSource.GetReferencePalette();

            RenderedAnimation animation = StandRenderer.Render(
                actionFrames,
                spriteSource,
                useActPalette ? actPalette : null,
                referencePalette ?? actPalette,
                padding,
                preserveSpriteCanvas,
                referencePalette != null);
            GifAnimationWriter.Write(destinationPath, animation);

            return new MugenStandExtractionResult
            {
                Sucesso = true,
                Mensagem = "Stand gerado com sucesso.",
                CharacterFolder = resolvedFolder,
                DefPath = files.DefPath,
                AirPath = files.AirPath,
                SffPath = files.SffPath,
                PalettePath = files.PalettePath,
                OutputPath = destinationPath,
                OutputFileName = safeOutputFileName,
                FrameCount = animation.Frames.Count,
                Width = animation.Width,
                Height = animation.Height,
                UsouPaletaAct = useActPalette && actPalette != null,
                Padding = padding,
                PreservouCanvasSprite = preserveSpriteCanvas,
                IndiceTransparenteForcado = transparentIndexOverride.HasValue ? transparentIndexOverride.Value.ToString(CultureInfo.InvariantCulture) : null
            };
        }

        public MugenSpriteExtractionResult ExtractPortraitFromCharacter(
            string characterName,
            string characterFolder = null,
            string outputFileName = null,
            bool overwrite = true,
            bool useActPalette = true,
            int group = 9000,
            int image = 0,
            int? transparentIndex = null)
        {
            if (string.IsNullOrWhiteSpace(characterName) && string.IsNullOrWhiteSpace(characterFolder))
                throw new ArgumentException("Informe o nome do personagem ou a pasta de origem.");

            Directory.CreateDirectory(outputPath);

            string resolvedFolder = ResolveCharacterFolder(characterName, characterFolder);
            string safeOutputFileName = NormalizePngFileName(outputFileName, characterName, resolvedFolder, "Portrait");
            string destinationPath = Path.Combine(outputPath, safeOutputFileName);

            if (!overwrite && File.Exists(destinationPath))
            {
                return new MugenSpriteExtractionResult
                {
                    Sucesso = true,
                    Ignorado = true,
                    Mensagem = "O portrait ja existe e sobrescrever esta desativado.",
                    CharacterFolder = resolvedFolder,
                    OutputPath = destinationPath,
                    OutputFileName = safeOutputFileName,
                    Group = group,
                    Image = image
                };
            }

            MugenCharacterFiles files = MugenCharacterFiles.FromFolder(resolvedFolder);
            byte? transparentIndexOverride = NormalizeTransparentIndex(transparentIndex);
            ISpriteSource spriteSource = CreateSpriteSource(files, transparentIndexOverride);
            byte[] actPalette = !string.IsNullOrWhiteSpace(files.PalettePath)
                ? PaletteReader.ReadActPalette(files.PalettePath)
                : null;
            byte[] referencePalette = spriteSource.GetReferencePalette();

            IndexedSprite portraitSprite = spriteSource.GetSprite(group, image);
            if (portraitSprite == null || portraitSprite.VisibleBounds == null)
                throw new InvalidDataException("Nao encontrei o sprite de portrait " + group.ToString(CultureInfo.InvariantCulture) + "," + image.ToString(CultureInfo.InvariantCulture) + " neste personagem.");

            List<AirFrame> frames = new List<AirFrame>
            {
                new AirFrame(group, image, 0, 0, 1, false, false)
            };

            RenderedAnimation animation = StandRenderer.Render(
                frames,
                spriteSource,
                useActPalette ? actPalette : null,
                referencePalette ?? actPalette,
                0,
                false,
                referencePalette != null);
            PngImageWriter.Write(destinationPath, animation);

            return new MugenSpriteExtractionResult
            {
                Sucesso = true,
                Mensagem = "Portrait gerado com sucesso.",
                CharacterFolder = resolvedFolder,
                DefPath = files.DefPath,
                AirPath = files.AirPath,
                SffPath = files.SffPath,
                PalettePath = files.PalettePath,
                OutputPath = destinationPath,
                OutputFileName = safeOutputFileName,
                Group = group,
                Image = image,
                Width = animation.Width,
                Height = animation.Height,
                UsouPaletaAct = useActPalette && actPalette != null,
                IndiceTransparenteForcado = transparentIndexOverride.HasValue ? transparentIndexOverride.Value.ToString(CultureInfo.InvariantCulture) : null
            };
        }

        private ISpriteSource CreateSpriteSource(MugenCharacterFiles files, byte? transparentIndexOverride)
        {
            if (!string.IsNullOrWhiteSpace(files.SffPath) && File.Exists(files.SffPath))
                return SffSpriteSource.Load(files.SffPath, transparentIndexOverride);

            return FolderPcxSpriteSource.Load(files.CharacterFolder, transparentIndexOverride);
        }

        private static byte? NormalizeTransparentIndex(int? transparentIndex)
        {
            if (!transparentIndex.HasValue)
                return null;

            if (transparentIndex.Value < 0 || transparentIndex.Value > 255)
                throw new ArgumentOutOfRangeException("transparentIndex", "O indice transparente precisa estar entre 0 e 255.");

            return (byte)transparentIndex.Value;
        }

        private string ResolveCharacterFolder(string characterName, string characterFolder)
        {
            if (!string.IsNullOrWhiteSpace(characterFolder))
            {
                string folder = Path.IsPathRooted(characterFolder)
                    ? characterFolder
                    : Path.Combine(charsPath, characterFolder);

                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException("Pasta do personagem nao encontrada: " + folder);

                return folder;
            }

            string exactFolder = Path.Combine(charsPath, characterName);
            if (Directory.Exists(exactFolder))
                return exactFolder;

            if (!Directory.Exists(charsPath))
                throw new DirectoryNotFoundException("Pasta chars do MUGEN nao encontrada: " + charsPath);

            string match = Directory
                .GetDirectories(charsPath)
                .FirstOrDefault(x => string.Equals(Path.GetFileName(x), characterName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
                return match;

            string defMatch = Directory
                .GetFiles(charsPath, "*.def", SearchOption.AllDirectories)
                .FirstOrDefault(x => string.Equals(Path.GetFileNameWithoutExtension(x), characterName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(defMatch))
                return Path.GetDirectoryName(defMatch);

            throw new DirectoryNotFoundException("Nao encontrei a pasta do personagem em chars: " + characterName);
        }

        private static string NormalizeGifFileName(string outputFileName, string characterName, string characterFolder)
        {
            string fileName = outputFileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                string baseName = !string.IsNullOrWhiteSpace(characterName)
                    ? characterName
                    : Path.GetFileName(characterFolder);

                fileName = SanitizeFileName(baseName) + "Stand.gif";
            }

            fileName = Path.GetFileName(fileName);

            if (!fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                fileName += ".gif";

            return fileName;
        }

        private static string NormalizePngFileName(string outputFileName, string characterName, string characterFolder, string suffix)
        {
            string fileName = outputFileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                string baseName = !string.IsNullOrWhiteSpace(characterName)
                    ? characterName
                    : Path.GetFileName(characterFolder);

                fileName = SanitizeFileName(baseName) + suffix + ".png";
            }

            fileName = Path.GetFileName(fileName);

            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".png";

            return fileName;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Lutador";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (invalidChars.Contains(c))
                    continue;

                builder.Append(c);
            }

            string sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Lutador" : sanitized;
        }
    }

    public class MugenStandExtractionResult
    {
        public bool Sucesso { get; set; }
        public bool Ignorado { get; set; }
        public string Mensagem { get; set; }
        public string CharacterFolder { get; set; }
        public string DefPath { get; set; }
        public string AirPath { get; set; }
        public string SffPath { get; set; }
        public string PalettePath { get; set; }
        public string OutputPath { get; set; }
        public string OutputFileName { get; set; }
        public int FrameCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool UsouPaletaAct { get; set; }
        public int Padding { get; set; }
        public bool PreservouCanvasSprite { get; set; }
        public string IndiceTransparenteForcado { get; set; }
    }

    public class MugenSpriteExtractionResult
    {
        public bool Sucesso { get; set; }
        public bool Ignorado { get; set; }
        public string Mensagem { get; set; }
        public string CharacterFolder { get; set; }
        public string DefPath { get; set; }
        public string AirPath { get; set; }
        public string SffPath { get; set; }
        public string PalettePath { get; set; }
        public string OutputPath { get; set; }
        public string OutputFileName { get; set; }
        public int Group { get; set; }
        public int Image { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool UsouPaletaAct { get; set; }
        public string IndiceTransparenteForcado { get; set; }
    }

    internal sealed class MugenCharacterFiles
    {
        public string CharacterFolder { get; private set; }
        public string DefPath { get; private set; }
        public string AirPath { get; private set; }
        public string SffPath { get; private set; }
        public string PalettePath { get; private set; }

        public static MugenCharacterFiles FromFolder(string characterFolder)
        {
            string folderName = Path.GetFileName(characterFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string preferredDef = Path.Combine(characterFolder, folderName + ".def");
            string defPath = File.Exists(preferredDef)
                ? preferredDef
                : Directory.GetFiles(characterFolder, "*.def", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(defPath))
                throw new FileNotFoundException("Nao encontrei arquivo .def na pasta do personagem.", characterFolder);

            Dictionary<string, string> filesSection = DefFileReader.ReadFilesSection(defPath);

            string airPath = ResolveFirstExisting(characterFolder, filesSection, new[] { "anim", "air" }, "*.air");
            string sffPath = ResolveFirstExisting(characterFolder, filesSection, new[] { "sprite", "spr", "sff" }, "*.sff");
            string palettePath = ResolvePalettePath(characterFolder, filesSection);

            if (string.IsNullOrWhiteSpace(airPath))
                throw new FileNotFoundException("Nao encontrei o arquivo .air do personagem.", characterFolder);

            return new MugenCharacterFiles
            {
                CharacterFolder = characterFolder,
                DefPath = defPath,
                AirPath = airPath,
                SffPath = sffPath,
                PalettePath = palettePath
            };
        }

        private static string ResolveFirstExisting(
            string characterFolder,
            Dictionary<string, string> filesSection,
            IEnumerable<string> keys,
            string fallbackPattern,
            bool required = true)
        {
            foreach (string key in keys)
            {
                string value;
                if (!filesSection.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
                    continue;

                string path = ResolvePath(characterFolder, value);
                if (File.Exists(path))
                    return path;
            }

            string fallback = Directory.GetFiles(characterFolder, fallbackPattern, SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (required && string.IsNullOrWhiteSpace(fallback))
                return null;

            return fallback;
        }

        private static string ResolvePalettePath(string characterFolder, Dictionary<string, string> filesSection)
        {
            string pal1Path = ResolveExistingValue(characterFolder, filesSection, "pal1");
            if (!string.IsNullOrWhiteSpace(pal1Path))
                return pal1Path;

            foreach (string defaultPaletteName in new[] { "000.act", "0.act", "001.act", "1.act" })
            {
                string defaultPalettePath = Path.Combine(characterFolder, defaultPaletteName);
                if (File.Exists(defaultPalettePath))
                    return defaultPalettePath;
            }

            for (int i = 2; i <= 12; i++)
            {
                string palettePath = ResolveExistingValue(characterFolder, filesSection, "pal" + i.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(palettePath))
                    return palettePath;
            }

            return Directory.GetFiles(characterFolder, "*.act", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        private static string ResolveExistingValue(string characterFolder, Dictionary<string, string> filesSection, string key)
        {
            string value;
            if (!filesSection.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
                return null;

            string path = ResolvePath(characterFolder, value);
            return File.Exists(path) ? path : null;
        }

        private static string ResolvePath(string baseFolder, string value)
        {
            string normalized = value.Trim().Trim('"');
            normalized = normalized.Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalized))
                return normalized;

            return Path.Combine(baseFolder, normalized);
        }
    }

    internal static class DefFileReader
    {
        public static Dictionary<string, string> ReadFilesSection(string defPath)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string currentSection = string.Empty;

            foreach (string rawLine in File.ReadAllLines(defPath))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Trim('[', ']').Trim();
                    continue;
                }

                if (!string.Equals(currentSection, "Files", StringComparison.OrdinalIgnoreCase))
                    continue;

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                    continue;

                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim();

                if (key.Length > 0 && value.Length > 0)
                    values[key] = value;
            }

            return values;
        }

        private static string StripComment(string value)
        {
            int commentIndex = value.IndexOf(';');
            return commentIndex >= 0 ? value.Substring(0, commentIndex) : value;
        }
    }

    internal static class AirFileReader
    {
        private static readonly Regex BeginActionRegex = new Regex(
            @"^\s*\[\s*Begin\s+Action\s+(-?\d+)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<AirFrame> GetActionFrames(string airPath, int actionNumber)
        {
            return GetAction(airPath, actionNumber).Frames;
        }

        public static AirAnimation GetAction(string airPath, int actionNumber)
        {
            List<AirFrame> frames = new List<AirFrame>();
            bool readingRequestedAction = false;
            int? loopStartFrameIndex = null;

            foreach (string rawLine in File.ReadAllLines(airPath))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                Match actionMatch = BeginActionRegex.Match(line);
                if (actionMatch.Success)
                {
                    if (readingRequestedAction)
                        break;

                    int currentAction = int.Parse(actionMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    readingRequestedAction = currentAction == actionNumber;
                    continue;
                }

                if (!readingRequestedAction)
                    continue;

                if (line.Equals("LoopStart", StringComparison.OrdinalIgnoreCase))
                {
                    if (!loopStartFrameIndex.HasValue)
                        loopStartFrameIndex = frames.Count;

                    continue;
                }

                AirFrame frame;
                if (TryParseFrame(line, out frame))
                    frames.Add(frame);
            }

            return new AirAnimation(frames, loopStartFrameIndex);
        }

        private static bool TryParseFrame(string line, out AirFrame frame)
        {
            frame = null;

            string[] parts = line.Split(',');
            if (parts.Length < 5)
                return false;

            int group;
            int image;
            int x;
            int y;
            int duration;

            if (!TryParseInt(parts[0], out group) ||
                !TryParseInt(parts[1], out image) ||
                !TryParseInt(parts[2], out x) ||
                !TryParseInt(parts[3], out y) ||
                !TryParseInt(parts[4], out duration))
            {
                return false;
            }

            bool flipHorizontal = false;
            bool flipVertical = false;

            for (int i = 5; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Equals("H", StringComparison.OrdinalIgnoreCase))
                    flipHorizontal = true;

                if (token.Equals("V", StringComparison.OrdinalIgnoreCase))
                    flipVertical = true;

                if (token.Equals("HV", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("VH", StringComparison.OrdinalIgnoreCase))
                {
                    flipHorizontal = true;
                    flipVertical = true;
                }
            }

            frame = new AirFrame(group, image, x, y, duration, flipHorizontal, flipVertical);
            return true;
        }

        private static bool TryParseInt(string value, out int result)
        {
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static string StripComment(string value)
        {
            int commentIndex = value.IndexOf(';');
            return commentIndex >= 0 ? value.Substring(0, commentIndex) : value;
        }
    }

    internal sealed class AirAnimation
    {
        public AirAnimation(List<AirFrame> frames, int? loopStartFrameIndex)
        {
            Frames = frames ?? new List<AirFrame>();
            LoopStartFrameIndex = loopStartFrameIndex;
        }

        public List<AirFrame> Frames { get; private set; }
        public int? LoopStartFrameIndex { get; private set; }
    }

    internal static class StandLoopSelector
    {
        public static List<AirFrame> SelectFrames(AirAnimation action)
        {
            if (action == null || action.Frames == null)
                return new List<AirFrame>();

            List<AirFrame> frames = action.Frames;
            if (!action.LoopStartFrameIndex.HasValue)
                return new List<AirFrame>(frames);

            int loopStart = action.LoopStartFrameIndex.Value;
            if (loopStart < 0 || loopStart >= frames.Count)
                return new List<AirFrame>(frames);

            int loopFrameCount = frames.Count - loopStart;
            if (loopFrameCount > 1)
                return frames.Skip(loopStart).ToList();

            // Um LoopStart com apenas um frame representa uma pose final estatica no
            // MUGEN. Para o GIF de stand, preserve uma introducao animada somente
            // quando ela ja fecha um ciclo completo antes da transicao para essa pose.
            AirFrame firstFrame = frames.FirstOrDefault();
            for (int i = loopStart - 1; i > 0; i--)
            {
                if (HasSamePose(firstFrame, frames[i]))
                    return frames.Take(i + 1).ToList();
            }

            return frames.Skip(loopStart).ToList();
        }

        private static bool HasSamePose(AirFrame left, AirFrame right)
        {
            return left != null
                && right != null
                && left.Group == right.Group
                && left.Image == right.Image
                && left.X == right.X
                && left.Y == right.Y
                && left.FlipHorizontal == right.FlipHorizontal
                && left.FlipVertical == right.FlipVertical;
        }
    }

    internal sealed class AirFrame
    {
        public AirFrame(int group, int image, int x, int y, int duration, bool flipHorizontal, bool flipVertical)
        {
            Group = group;
            Image = image;
            X = x;
            Y = y;
            Duration = duration;
            FlipHorizontal = flipHorizontal;
            FlipVertical = flipVertical;
        }

        public int Group { get; private set; }
        public int Image { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Duration { get; private set; }
        public bool FlipHorizontal { get; private set; }
        public bool FlipVertical { get; private set; }
    }

    internal interface ISpriteSource
    {
        IndexedSprite GetSprite(int group, int image);
        IndexedSprite GetFallbackSprite();
        byte[] GetReferencePalette();
    }

    internal static class SffSpriteSource
    {
        public static ISpriteSource Load(string sffPath, byte? transparentIndexOverride)
        {
            byte[] version;

            using (FileStream stream = File.OpenRead(sffPath))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(12)).TrimEnd('\0');
                if (!string.Equals(signature, "ElecbyteSpr", StringComparison.Ordinal))
                    throw new InvalidDataException("Arquivo SFF invalido: " + sffPath);

                version = reader.ReadBytes(4);
            }

            int majorVersion = GetMajorVersion(version);
            if (majorVersion == 2)
                return SffV2SpriteSource.Load(sffPath, transparentIndexOverride, version);

            if (majorVersion == 1)
                return SffV1SpriteSource.Load(sffPath, transparentIndexOverride);

            throw new NotSupportedException("Versao SFF nao suportada neste importador: " + FormatVersion(version));
        }

        private static int GetMajorVersion(byte[] version)
        {
            if (version == null || version.Length < 4)
                return 0;

            if (version[3] == 1 || version[3] == 2)
                return version[3];

            if (version[2] == 1 || version[2] == 2)
                return version[2];

            return version.Max(x => (int)x);
        }

        private static string FormatVersion(byte[] version)
        {
            if (version == null || version.Length < 4)
                return "desconhecida";

            return version[3].ToString(CultureInfo.InvariantCulture) + "." +
                   version[2].ToString(CultureInfo.InvariantCulture) + "." +
                   version[1].ToString(CultureInfo.InvariantCulture) + "." +
                   version[0].ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class SffV2SpriteSource : ISpriteSource
    {
        private readonly Dictionary<long, IndexedSprite> sprites;

        private SffV2SpriteSource(Dictionary<long, IndexedSprite> sprites)
        {
            this.sprites = sprites;
        }

        public static SffV2SpriteSource Load(string sffPath, byte? transparentIndexOverride, byte[] version)
        {
            Dictionary<long, IndexedSprite> sprites = new Dictionary<long, IndexedSprite>();
            List<IndexedSprite> spriteByIndex = new List<IndexedSprite>();

            using (FileStream stream = File.OpenRead(sffPath))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                SffV2Header header = ReadHeader(reader, sffPath);
                List<SffV2Palette> palettes = ReadPalettes(reader, header, version);

                if (header.SpriteCount <= 0 || header.FirstSpriteHeaderOffset <= 0)
                    throw new InvalidDataException("Cabecalho SFF v2 invalido: " + sffPath);

                for (int i = 0; i < header.SpriteCount; i++)
                {
                    long headerOffset = (long)header.FirstSpriteHeaderOffset + (i * 28L);
                    if (headerOffset < 0 || headerOffset + 28 > stream.Length)
                        break;

                    stream.Position = headerOffset;
                    SffV2SpriteHeader spriteHeader = ReadSpriteHeader(reader, header);
                    IndexedSprite linkedSprite = spriteHeader.Link < spriteByIndex.Count
                        ? spriteByIndex[spriteHeader.Link]
                        : null;

                    IndexedSprite sprite = null;

                    if (spriteHeader.DataLength == 0 && linkedSprite != null)
                    {
                        sprite = new IndexedSprite(
                            spriteHeader.Group,
                            spriteHeader.Image,
                            spriteHeader.AxisX,
                            spriteHeader.AxisY,
                            linkedSprite.Width,
                            linkedSprite.Height,
                            linkedSprite.Pixels,
                            linkedSprite.Palette,
                            transparentIndexOverride ?? linkedSprite.TransparentIndex);
                    }
                    else if (spriteHeader.DataLength > 0)
                    {
                        try
                        {
                            sprite = DecodeSprite(reader, spriteHeader, palettes, transparentIndexOverride);
                        }
                        catch (Exception ex)
                        {
                            if (!(ex is InvalidDataException) && !(ex is NotSupportedException) && !(ex is ArgumentException))
                                throw;
                        }
                    }

                    spriteByIndex.Add(sprite);

                    if (sprite != null && sprite.VisibleBounds != null)
                        sprites[SpriteKey(sprite.Group, sprite.Image)] = sprite;
                }
            }

            if (sprites.Count == 0)
                throw new InvalidDataException("Nao foi possivel extrair sprites do SFF v2 informado.");

            return new SffV2SpriteSource(sprites);
        }

        public IndexedSprite GetSprite(int group, int image)
        {
            IndexedSprite sprite;
            sprites.TryGetValue(SpriteKey(group, image), out sprite);
            return sprite;
        }

        public IndexedSprite GetFallbackSprite()
        {
            return GetSprite(0, 0) ?? sprites.Values.FirstOrDefault();
        }

        public byte[] GetReferencePalette()
        {
            IndexedSprite reference = GetSprite(9000, 0) ?? GetSprite(0, 0);
            if (reference != null && reference.Palette != null)
                return reference.Palette;

            return sprites.Values
                .Where(sprite => sprite != null && sprite.Palette != null)
                .Select(sprite => sprite.Palette)
                .FirstOrDefault();
        }

        private static SffV2Header ReadHeader(BinaryReader reader, string sffPath)
        {
            reader.BaseStream.Position = 0;
            string signature = Encoding.ASCII.GetString(reader.ReadBytes(12)).TrimEnd('\0');
            if (!string.Equals(signature, "ElecbyteSpr", StringComparison.Ordinal))
                throw new InvalidDataException("Arquivo SFF invalido: " + sffPath);

            reader.ReadBytes(4);
            reader.ReadUInt32();

            for (int i = 0; i < 4; i++)
                reader.ReadUInt32();

            return new SffV2Header
            {
                FirstSpriteHeaderOffset = reader.ReadUInt32(),
                SpriteCount = reader.ReadUInt32(),
                FirstPaletteHeaderOffset = reader.ReadUInt32(),
                PaletteCount = reader.ReadUInt32(),
                LDataOffset = reader.ReadUInt32(),
                LDataLength = reader.ReadUInt32(),
                TDataOffset = reader.ReadUInt32()
            };
        }

        private static List<SffV2Palette> ReadPalettes(BinaryReader reader, SffV2Header header, byte[] version)
        {
            List<SffV2Palette> palettes = new List<SffV2Palette>();
            bool forceSff20Alpha = version != null && version.Length > 1 && version[1] == 0;

            for (int i = 0; i < header.PaletteCount; i++)
            {
                long paletteHeaderOffset = (long)header.FirstPaletteHeaderOffset + (i * 16L);
                if (paletteHeaderOffset < 0 || paletteHeaderOffset + 16 > reader.BaseStream.Length)
                    break;

                reader.BaseStream.Position = paletteHeaderOffset;
                SffV2PaletteHeader paletteHeader = new SffV2PaletteHeader
                {
                    Group = reader.ReadUInt16(),
                    Item = reader.ReadUInt16(),
                    ColorCount = reader.ReadUInt16(),
                    Link = reader.ReadUInt16(),
                    DataOffset = reader.ReadUInt32(),
                    DataLength = reader.ReadUInt32()
                };

                SffV2Palette palette = null;

                if (paletteHeader.DataLength == 0 && paletteHeader.Link < palettes.Count)
                {
                    palette = palettes[paletteHeader.Link];
                }
                else if (paletteHeader.DataLength > 0)
                {
                    long paletteDataOffset = (long)header.LDataOffset + paletteHeader.DataOffset;
                    if (paletteDataOffset >= 0 && paletteDataOffset < reader.BaseStream.Length)
                        palette = ReadPalette(reader, paletteDataOffset, paletteHeader.DataLength, forceSff20Alpha);
                }

                palettes.Add(palette ?? SffV2Palette.CreateDefault());
            }

            return palettes;
        }

        private static SffV2Palette ReadPalette(BinaryReader reader, long offset, uint dataLength, bool forceSff20Alpha)
        {
            reader.BaseStream.Position = offset;

            byte[] rgb = new byte[768];
            byte[] alpha = new byte[256];
            int colorCount = Math.Min(256, (int)dataLength / 4);

            for (int i = 0; i < 256; i++)
                alpha[i] = 255;

            for (int i = 0; i < colorCount; i++)
            {
                int paletteOffset = i * 3;
                rgb[paletteOffset] = reader.ReadByte();
                rgb[paletteOffset + 1] = reader.ReadByte();
                rgb[paletteOffset + 2] = reader.ReadByte();
                alpha[i] = reader.ReadByte();
            }

            if (forceSff20Alpha)
            {
                alpha[0] = 0;
                for (int i = 1; i < alpha.Length; i++)
                    alpha[i] = 255;
            }

            return new SffV2Palette(rgb, alpha);
        }

        private static SffV2SpriteHeader ReadSpriteHeader(BinaryReader reader, SffV2Header header)
        {
            SffV2SpriteHeader sprite = new SffV2SpriteHeader
            {
                Group = reader.ReadUInt16(),
                Image = reader.ReadUInt16(),
                Width = reader.ReadUInt16(),
                Height = reader.ReadUInt16(),
                AxisX = reader.ReadInt16(),
                AxisY = reader.ReadInt16(),
                Link = reader.ReadUInt16(),
                Format = reader.ReadByte(),
                ColorDepth = reader.ReadByte(),
                DataOffset = reader.ReadUInt32(),
                DataLength = reader.ReadUInt32(),
                PaletteIndex = reader.ReadUInt16()
            };

            ushort flags = reader.ReadUInt16();
            sprite.AbsoluteDataOffset = (flags & 1) == 0
                ? (long)header.LDataOffset + sprite.DataOffset
                : (long)header.TDataOffset + sprite.DataOffset;

            return sprite;
        }

        private static IndexedSprite DecodeSprite(BinaryReader reader, SffV2SpriteHeader header, List<SffV2Palette> palettes, byte? transparentIndexOverride)
        {
            if (header.Width == 0 || header.Height == 0)
                throw new InvalidDataException("Sprite SFF v2 sem dimensoes validas.");

            if (header.AbsoluteDataOffset < 0 || header.AbsoluteDataOffset >= reader.BaseStream.Length)
                throw new InvalidDataException("Offset de dados SFF v2 invalido.");

            SffV2Palette palette = header.PaletteIndex < palettes.Count
                ? palettes[header.PaletteIndex]
                : SffV2Palette.CreateDefault();

            byte[] pixels;
            byte[] spritePalette = palette.Rgb;
            byte transparentIndex;

            if (header.Format == 0)
            {
                reader.BaseStream.Position = header.AbsoluteDataOffset;
                byte[] data = reader.ReadBytes((int)Math.Min(header.DataLength, int.MaxValue));
                byte[] rawPalette;
                pixels = DecodeRaw(data, header.Width, header.Height, header.ColorDepth, out rawPalette);
                if (rawPalette != null)
                    spritePalette = rawPalette;

                transparentIndex = transparentIndexOverride ?? ResolveTransparentIndex(pixels, header.Width, header.Height, palette);
            }
            else if (header.Format >= 2 && header.Format <= 4)
            {
                byte[] compressed = ReadCompressedPayload(reader, header);
                int expectedLength = header.Width * header.Height;

                if (header.Format == 2)
                    pixels = DecodeRle8(compressed, expectedLength);
                else if (header.Format == 3)
                    pixels = DecodeRle5(compressed, expectedLength);
                else
                    pixels = DecodeLz5(compressed, expectedLength);

                transparentIndex = transparentIndexOverride ?? ResolveTransparentIndex(pixels, header.Width, header.Height, palette);
            }
            else if (header.Format >= 10 && header.Format <= 12)
            {
                byte[] pngBytes = ReadCompressedPayload(reader, header);
                SffV2BitmapImage png = SffV2BitmapImage.FromPng(pngBytes, transparentIndexOverride);
                pixels = png.Pixels;

                // PNG8 sprites in SFF v2 store palette indexes in the PNG while
                // the real colors remain in the SFF palette referenced by the
                // sprite header. Some characters deliberately embed a black
                // placeholder PLTE, so replacing the SFF palette creates a
                // completely black silhouette.
                if (header.Format == 10 && header.ColorDepth == 8)
                {
                    transparentIndex = transparentIndexOverride ?? ResolveTransparentIndex(pixels, header.Width, header.Height, palette);
                }
                else
                {
                    spritePalette = png.Palette;
                    transparentIndex = png.TransparentIndex;
                }
            }
            else
            {
                throw new NotSupportedException("Formato de sprite SFF v2 nao suportado: " + header.Format.ToString(CultureInfo.InvariantCulture));
            }

            if (pixels == null || pixels.Length != header.Width * header.Height)
                throw new InvalidDataException("Dados do sprite SFF v2 nao batem com as dimensoes informadas.");

            return new IndexedSprite(
                header.Group,
                header.Image,
                header.AxisX,
                header.AxisY,
                header.Width,
                header.Height,
                pixels,
                spritePalette,
                transparentIndex);
        }

        private static byte[] ReadCompressedPayload(BinaryReader reader, SffV2SpriteHeader header)
        {
            if (header.DataLength < 4)
                throw new InvalidDataException("Dados comprimidos SFF v2 truncados.");

            reader.BaseStream.Position = header.AbsoluteDataOffset + 4;
            return reader.ReadBytes((int)Math.Min(header.DataLength - 4, int.MaxValue));
        }

        private static byte[] DecodeRaw(byte[] data, int width, int height, byte colorDepth, out byte[] palette)
        {
            int pixelCount = width * height;
            if (colorDepth == 8)
            {
                if (data.Length < pixelCount)
                    throw new InvalidDataException("Dados raw 8-bit SFF v2 truncados.");

                palette = null;
                byte[] pixels = new byte[pixelCount];
                Buffer.BlockCopy(data, 0, pixels, 0, pixelCount);
                return pixels;
            }

            if (colorDepth == 24 || colorDepth == 32)
                return DecodeRawTrueColor(data, width, height, colorDepth, out palette);

            throw new NotSupportedException("Profundidade de cor SFF v2 nao suportada: " + colorDepth.ToString(CultureInfo.InvariantCulture));
        }

        private static byte[] DecodeRawTrueColor(byte[] data, int width, int height, byte colorDepth, out byte[] palette)
        {
            int bytesPerPixel = colorDepth == 32 ? 4 : 3;
            int expectedLength = width * height * bytesPerPixel;
            if (data.Length < expectedLength)
                throw new InvalidDataException("Dados truecolor SFF v2 truncados.");

            palette = SffV2BitmapImage.CreateRgbCubePalette();
            byte[] pixels = new byte[width * height];

            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * bytesPerPixel;
                byte r = data[offset];
                byte g = data[offset + 1];
                byte b = data[offset + 2];
                byte a = bytesPerPixel == 4 ? data[offset + 3] : (byte)255;
                pixels[i] = a < 128 ? (byte)0 : SffV2BitmapImage.MapToRgbCube(r, g, b);
            }

            return pixels;
        }

        private static byte[] DecodeRle8(byte[] data, int expectedLength)
        {
            byte[] output = new byte[expectedLength];
            int inputIndex = 0;
            int outputIndex = 0;

            while (outputIndex < expectedLength)
            {
                byte value = ReadByte(data, ref inputIndex);
                int count = 1;

                if ((value & 0xC0) == 0x40)
                {
                    count = value & 0x3F;
                    value = ReadByte(data, ref inputIndex);
                }

                for (int i = 0; i < count && outputIndex < expectedLength; i++)
                    output[outputIndex++] = value;
            }

            return output;
        }

        private static byte[] DecodeRle5(byte[] data, int expectedLength)
        {
            byte[] output = new byte[expectedLength];
            int inputIndex = 0;
            int outputIndex = 0;

            while (outputIndex < expectedLength)
            {
                int runLength = ReadByte(data, ref inputIndex);
                byte descriptor = ReadByte(data, ref inputIndex);
                int dataLength = descriptor & 0x7F;
                byte value = 0;

                if ((descriptor & 0x80) != 0)
                    value = ReadByte(data, ref inputIndex);

                while (true)
                {
                    if (outputIndex < expectedLength)
                        output[outputIndex++] = value;

                    runLength--;
                    if (runLength < 0)
                    {
                        dataLength--;
                        if (dataLength < 0)
                            break;

                        byte packed = ReadByte(data, ref inputIndex);
                        value = (byte)(packed & 0x1F);
                        runLength = packed >> 5;
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeLz5(byte[] data, int expectedLength)
        {
            byte[] output = new byte[expectedLength];
            int inputIndex = 0;
            int outputIndex = 0;
            int control = ReadByte(data, ref inputIndex);
            int controlShift = 0;
            byte recycledDistanceBits = 0;
            int recycledBitCount = 0;

            while (outputIndex < expectedLength)
            {
                int value = ReadByte(data, ref inputIndex);

                if ((control & (1 << controlShift)) != 0)
                {
                    int distance;
                    int count;

                    if ((value & 0x3F) == 0)
                    {
                        distance = ((value << 2) | ReadByte(data, ref inputIndex)) + 1;
                        count = ReadByte(data, ref inputIndex) + 2;
                    }
                    else
                    {
                        recycledDistanceBits |= (byte)((value & 0xC0) >> recycledBitCount);
                        recycledBitCount += 2;
                        count = value & 0x3F;

                        if (recycledBitCount < 8)
                        {
                            distance = ReadByte(data, ref inputIndex) + 1;
                        }
                        else
                        {
                            distance = recycledDistanceBits + 1;
                            recycledDistanceBits = 0;
                            recycledBitCount = 0;
                        }
                    }

                    while (true)
                    {
                        if (distance <= 0 || outputIndex - distance < 0)
                            throw new InvalidDataException("Referencia LZ5 invalida no SFF v2.");

                        if (outputIndex < expectedLength)
                        {
                            output[outputIndex] = output[outputIndex - distance];
                            outputIndex++;
                        }

                        count--;
                        if (count < 0)
                            break;
                    }
                }
                else
                {
                    int count;
                    if ((value & 0xE0) == 0)
                    {
                        count = ReadByte(data, ref inputIndex) + 8;
                    }
                    else
                    {
                        count = value >> 5;
                        value &= 0x1F;
                    }

                    for (int i = 0; i < count && outputIndex < expectedLength; i++)
                        output[outputIndex++] = (byte)value;
                }

                controlShift++;
                if (controlShift >= 8 && outputIndex < expectedLength)
                {
                    control = ReadByte(data, ref inputIndex);
                    controlShift = 0;
                }
            }

            return output;
        }

        private static byte ReadByte(byte[] data, ref int index)
        {
            if (index >= data.Length)
                throw new InvalidDataException("Dados comprimidos SFF v2 truncados.");

            return data[index++];
        }

        private static byte ResolveTransparentIndex(byte[] pixels, int width, int height, SffV2Palette palette)
        {
            byte alphaIndex;
            if (palette != null && palette.TryGetTransparentIndex(out alphaIndex))
                return alphaIndex;

            return DetectBorderTransparentIndex(pixels, width, height);
        }

        private static byte DetectBorderTransparentIndex(byte[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
                return 0;

            Dictionary<byte, int> borderCounts = new Dictionary<byte, int>();

            for (int x = 0; x < width; x++)
            {
                AddBorderColor(borderCounts, pixels[x]);
                AddBorderColor(borderCounts, pixels[(height - 1) * width + x]);
            }

            for (int y = 1; y < height - 1; y++)
            {
                AddBorderColor(borderCounts, pixels[y * width]);
                AddBorderColor(borderCounts, pixels[y * width + width - 1]);
            }

            return borderCounts.Count == 0
                ? (byte)0
                : borderCounts.OrderByDescending(x => x.Value).First().Key;
        }

        private static void AddBorderColor(Dictionary<byte, int> counts, byte color)
        {
            int count;
            counts.TryGetValue(color, out count);
            counts[color] = count + 1;
        }

        private static long SpriteKey(int group, int image)
        {
            return ((long)group << 32) ^ (uint)image;
        }

        private sealed class SffV2Header
        {
            public uint FirstSpriteHeaderOffset { get; set; }
            public uint SpriteCount { get; set; }
            public uint FirstPaletteHeaderOffset { get; set; }
            public uint PaletteCount { get; set; }
            public uint LDataOffset { get; set; }
            public uint LDataLength { get; set; }
            public uint TDataOffset { get; set; }
        }

        private sealed class SffV2PaletteHeader
        {
            public ushort Group { get; set; }
            public ushort Item { get; set; }
            public ushort ColorCount { get; set; }
            public ushort Link { get; set; }
            public uint DataOffset { get; set; }
            public uint DataLength { get; set; }
        }

        private sealed class SffV2SpriteHeader
        {
            public int Group { get; set; }
            public int Image { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int AxisX { get; set; }
            public int AxisY { get; set; }
            public ushort Link { get; set; }
            public byte Format { get; set; }
            public byte ColorDepth { get; set; }
            public uint DataOffset { get; set; }
            public uint DataLength { get; set; }
            public ushort PaletteIndex { get; set; }
            public long AbsoluteDataOffset { get; set; }
        }

        private sealed class SffV2Palette
        {
            public SffV2Palette(byte[] rgb, byte[] alpha)
            {
                Rgb = RenderedAnimation.EnsurePalette(rgb);
                Alpha = alpha ?? CreateOpaqueAlpha();
            }

            public byte[] Rgb { get; private set; }
            public byte[] Alpha { get; private set; }

            public bool TryGetTransparentIndex(out byte transparentIndex)
            {
                if (Alpha != null && Alpha.Length > 0)
                {
                    if (Alpha[0] < 128)
                    {
                        transparentIndex = 0;
                        return true;
                    }

                    for (int i = 1; i < Alpha.Length; i++)
                    {
                        if (Alpha[i] < 128)
                        {
                            transparentIndex = (byte)i;
                            return true;
                        }
                    }
                }

                transparentIndex = 0;
                return false;
            }

            public static SffV2Palette CreateDefault()
            {
                return new SffV2Palette(PaletteReader.CreateDefaultPalette(), CreateOpaqueAlpha());
            }

            private static byte[] CreateOpaqueAlpha()
            {
                byte[] alpha = new byte[256];
                for (int i = 0; i < alpha.Length; i++)
                    alpha[i] = 255;

                alpha[0] = 0;
                return alpha;
            }
        }

        private sealed class SffV2BitmapImage
        {
            private SffV2BitmapImage(byte[] pixels, byte[] palette, byte transparentIndex)
            {
                Pixels = pixels;
                Palette = palette;
                TransparentIndex = transparentIndex;
            }

            public byte[] Pixels { get; private set; }
            public byte[] Palette { get; private set; }
            public byte TransparentIndex { get; private set; }

            public static SffV2BitmapImage FromPng(byte[] bytes, byte? transparentIndexOverride)
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Bitmap bitmap = new Bitmap(stream))
                {
                    if (bitmap.PixelFormat == PixelFormat.Format8bppIndexed)
                        return FromIndexedBitmap(bitmap, transparentIndexOverride);

                    return FromTrueColorBitmap(bitmap);
                }
            }

            public static byte[] CreateRgbCubePalette()
            {
                byte[] palette = new byte[768];
                for (int r = 0; r < 6; r++)
                {
                    for (int g = 0; g < 6; g++)
                    {
                        for (int b = 0; b < 6; b++)
                        {
                            int index = 1 + (r * 36) + (g * 6) + b;
                            int offset = index * 3;
                            palette[offset] = (byte)(r * 51);
                            palette[offset + 1] = (byte)(g * 51);
                            palette[offset + 2] = (byte)(b * 51);
                        }
                    }
                }

                return palette;
            }

            public static byte MapToRgbCube(byte r, byte g, byte b)
            {
                int red = Math.Min(5, (r * 6) / 256);
                int green = Math.Min(5, (g * 6) / 256);
                int blue = Math.Min(5, (b * 6) / 256);
                return (byte)(1 + (red * 36) + (green * 6) + blue);
            }

            private static SffV2BitmapImage FromIndexedBitmap(Bitmap bitmap, byte? transparentIndexOverride)
            {
                byte[] pixels = ReadIndexedPixels(bitmap);
                byte[] palette = new byte[768];
                byte[] alpha = new byte[256];
                Color[] entries = bitmap.Palette.Entries;

                for (int i = 0; i < alpha.Length; i++)
                    alpha[i] = 255;

                for (int i = 0; i < entries.Length && i < 256; i++)
                {
                    int offset = i * 3;
                    palette[offset] = entries[i].R;
                    palette[offset + 1] = entries[i].G;
                    palette[offset + 2] = entries[i].B;
                    alpha[i] = entries[i].A;
                }

                SffV2Palette sffPalette = new SffV2Palette(palette, alpha);
                byte transparentIndex = transparentIndexOverride ?? ResolveTransparentIndex(pixels, bitmap.Width, bitmap.Height, sffPalette);
                return new SffV2BitmapImage(pixels, palette, transparentIndex);
            }

            private static SffV2BitmapImage FromTrueColorBitmap(Bitmap bitmap)
            {
                byte[] pixels = new byte[bitmap.Width * bitmap.Height];
                byte[] palette = CreateRgbCubePalette();

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        pixels[y * bitmap.Width + x] = color.A < 128
                            ? (byte)0
                            : MapToRgbCube(color.R, color.G, color.B);
                    }
                }

                return new SffV2BitmapImage(pixels, palette, 0);
            }

            private static byte[] ReadIndexedPixels(Bitmap bitmap)
            {
                Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] raw = new byte[stride * bitmap.Height];
                    Marshal.Copy(data.Scan0, raw, 0, raw.Length);

                    byte[] pixels = new byte[bitmap.Width * bitmap.Height];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        int sourceOffset = data.Stride >= 0
                            ? y * stride
                            : (bitmap.Height - 1 - y) * stride;

                        Buffer.BlockCopy(raw, sourceOffset, pixels, y * bitmap.Width, bitmap.Width);
                    }

                    return pixels;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }
    }

    internal sealed class SffV1SpriteSource : ISpriteSource
    {
        private readonly Dictionary<long, IndexedSprite> sprites;

        private SffV1SpriteSource(Dictionary<long, IndexedSprite> sprites)
        {
            this.sprites = sprites;
        }

        public static SffV1SpriteSource Load(string sffPath, byte? transparentIndexOverride)
        {
            Dictionary<long, IndexedSprite> sprites = new Dictionary<long, IndexedSprite>();
            Dictionary<int, byte[]> linkedPcxData = new Dictionary<int, byte[]>();

            using (FileStream stream = File.OpenRead(sffPath))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(12)).TrimEnd('\0');
                if (!string.Equals(signature, "ElecbyteSpr", StringComparison.Ordinal))
                    throw new InvalidDataException("Arquivo SFF invalido: " + sffPath);

                byte[] version = reader.ReadBytes(4);
                if (version.Length == 4 && version.Any(x => x >= 2))
                    throw new NotSupportedException("SFF v2 ainda nao esta implementado neste importador. Use personagens SFF v1/PCX ou exporte os PCX pelo Fighter Factory.");

                int groupCount = reader.ReadInt32();
                int imageCount = reader.ReadInt32();
                int firstSubfileOffset = reader.ReadInt32();
                int subHeaderSize = reader.ReadInt32();

                if (groupCount < 0 || imageCount <= 0 || firstSubfileOffset <= 0 || subHeaderSize < 32)
                    throw new InvalidDataException("Cabecalho SFF v1 invalido: " + sffPath);

                int offset = firstSubfileOffset;
                int imageIndex = 0;

                while (offset > 0 && imageIndex < imageCount && offset < stream.Length)
                {
                    stream.Position = offset;

                    int nextOffset = reader.ReadInt32();
                    int dataLength = reader.ReadInt32();
                    short axisX = reader.ReadInt16();
                    short axisY = reader.ReadInt16();
                    short group = reader.ReadInt16();
                    short image = reader.ReadInt16();
                    short previousCopy = reader.ReadInt16();

                    reader.ReadByte();
                    reader.ReadBytes(13);

                    if (subHeaderSize > 32)
                        reader.ReadBytes(subHeaderSize - 32);

                    byte[] pcxData = null;
                    if (dataLength > 0)
                    {
                        pcxData = reader.ReadBytes(dataLength);
                        linkedPcxData[imageIndex] = pcxData;
                    }
                    else if (previousCopy >= 0)
                    {
                        linkedPcxData.TryGetValue(previousCopy, out pcxData);
                    }

                    if (pcxData != null && pcxData.Length > 0)
                    {
                        try
                        {
                            PcxImage pcx = PcxImage.FromBytes(pcxData);
                            byte transparentIndex = transparentIndexOverride ?? pcx.TransparentIndex;
                            IndexedSprite sprite = new IndexedSprite(group, image, axisX, axisY, pcx.Width, pcx.Height, pcx.Pixels, pcx.Palette, transparentIndex);
                            sprites[SpriteKey(group, image)] = sprite;
                        }
                        catch (Exception ex)
                        {
                            if (!(ex is InvalidDataException) && !(ex is NotSupportedException))
                                throw;
                        }
                    }

                    if (nextOffset <= offset)
                        break;

                    offset = nextOffset;
                    imageIndex++;
                }
            }

            if (sprites.Count == 0)
                throw new InvalidDataException("Nao foi possivel extrair sprites PCX do SFF informado.");

            return new SffV1SpriteSource(sprites);
        }

        public IndexedSprite GetSprite(int group, int image)
        {
            IndexedSprite sprite;
            sprites.TryGetValue(SpriteKey(group, image), out sprite);
            return sprite;
        }

        public IndexedSprite GetFallbackSprite()
        {
            return GetSprite(0, 0) ?? sprites.Values.FirstOrDefault();
        }

        public byte[] GetReferencePalette()
        {
            IndexedSprite reference = GetSprite(9000, 0) ?? GetSprite(0, 0);
            if (reference != null && reference.Palette != null)
                return reference.Palette;

            return sprites.Values
                .Where(sprite => sprite != null && sprite.Palette != null)
                .Select(sprite => sprite.Palette)
                .FirstOrDefault();
        }

        private static long SpriteKey(int group, int image)
        {
            return ((long)group << 32) ^ (uint)image;
        }
    }

    internal sealed class FolderPcxSpriteSource : ISpriteSource
    {
        private readonly Dictionary<long, IndexedSprite> sprites;

        private FolderPcxSpriteSource(Dictionary<long, IndexedSprite> sprites)
        {
            this.sprites = sprites;
        }

        public static FolderPcxSpriteSource Load(string characterFolder, byte? transparentIndexOverride)
        {
            Dictionary<long, IndexedSprite> sprites = new Dictionary<long, IndexedSprite>();
            foreach (string pcxPath in Directory.GetFiles(characterFolder, "*.pcx", SearchOption.TopDirectoryOnly))
            {
                int group;
                int image;
                if (!TryReadGroupAndImage(Path.GetFileNameWithoutExtension(pcxPath), out group, out image))
                    continue;

                PcxImage pcx = PcxImage.FromBytes(File.ReadAllBytes(pcxPath));
                byte transparentIndex = transparentIndexOverride ?? pcx.TransparentIndex;
                sprites[SpriteKey(group, image)] = new IndexedSprite(group, image, 0, 0, pcx.Width, pcx.Height, pcx.Pixels, pcx.Palette, transparentIndex);
            }

            if (sprites.Count == 0)
                throw new FileNotFoundException("Nao encontrei SFF nem PCX nomeados como grupo/imagem na pasta do personagem.", characterFolder);

            return new FolderPcxSpriteSource(sprites);
        }

        public IndexedSprite GetSprite(int group, int image)
        {
            IndexedSprite sprite;
            sprites.TryGetValue(SpriteKey(group, image), out sprite);
            return sprite;
        }

        public IndexedSprite GetFallbackSprite()
        {
            return GetSprite(0, 0) ?? sprites.Values.FirstOrDefault();
        }

        public byte[] GetReferencePalette()
        {
            IndexedSprite reference = GetSprite(9000, 0) ?? GetSprite(0, 0);
            if (reference != null && reference.Palette != null)
                return reference.Palette;

            return sprites.Values
                .Where(sprite => sprite != null && sprite.Palette != null)
                .Select(sprite => sprite.Palette)
                .FirstOrDefault();
        }

        private static bool TryReadGroupAndImage(string fileName, out int group, out int image)
        {
            group = 0;
            image = 0;

            Match match = Regex.Match(fileName, @"(-?\d+)\D+(-?\d+)");
            if (!match.Success)
                return false;

            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out group) &&
                   int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out image);
        }

        private static long SpriteKey(int group, int image)
        {
            return ((long)group << 32) ^ (uint)image;
        }
    }

    internal sealed class IndexedSprite
    {
        public IndexedSprite(int group, int image, int axisX, int axisY, int width, int height, byte[] pixels, byte[] palette, byte transparentIndex)
        {
            Group = group;
            Image = image;
            AxisX = axisX;
            AxisY = axisY;
            Width = width;
            Height = height;
            Pixels = pixels;
            Palette = palette;
            TransparentIndex = transparentIndex;
            VisibleBounds = SpriteBounds.FromPixels(pixels, width, height, transparentIndex);
        }

        public int Group { get; private set; }
        public int Image { get; private set; }
        public int AxisX { get; private set; }
        public int AxisY { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Pixels { get; private set; }
        public byte[] Palette { get; private set; }
        public byte TransparentIndex { get; private set; }
        public SpriteBounds VisibleBounds { get; private set; }
    }

    internal sealed class PcxImage
    {
        private PcxImage(int width, int height, byte[] pixels, byte[] palette, byte transparentIndex)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
            Palette = palette;
            TransparentIndex = transparentIndex;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Pixels { get; private set; }
        public byte[] Palette { get; private set; }
        public byte TransparentIndex { get; private set; }

        public static PcxImage FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 128)
                throw new InvalidDataException("PCX invalido ou vazio.");

            if (bytes[0] != 10 || bytes[2] != 1 || bytes[3] != 8)
                throw new NotSupportedException("Somente PCX 8-bit indexado e RLE e suportado.");

            ushort minX = ReadUInt16(bytes, 4);
            ushort minY = ReadUInt16(bytes, 6);
            ushort maxX = ReadUInt16(bytes, 8);
            ushort maxY = ReadUInt16(bytes, 10);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int colorPlanes = bytes[65];
            int bytesPerLine = ReadUInt16(bytes, 66);

            if (width <= 0 || height <= 0 || colorPlanes != 1 || bytesPerLine <= 0)
                throw new NotSupportedException("Somente PCX 8-bit com um plano de cor e suportado.");

            byte[] palette = ReadPcxPalette(bytes);
            byte[] decodedRows = DecodeRle(bytes, 128, bytes.Length - (palette != null ? 769 : 0), bytesPerLine * height);
            byte[] pixels = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(decodedRows, y * bytesPerLine, pixels, y * width, width);
            }

            return new PcxImage(width, height, pixels, palette, DetectTransparentIndex(pixels, width, height));
        }

        private static byte DetectTransparentIndex(byte[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length == 0)
                return 0;

            Dictionary<byte, int> borderCounts = new Dictionary<byte, int>();

            for (int x = 0; x < width; x++)
            {
                AddBorderColor(borderCounts, pixels[x]);
                AddBorderColor(borderCounts, pixels[(height - 1) * width + x]);
            }

            for (int y = 1; y < height - 1; y++)
            {
                AddBorderColor(borderCounts, pixels[y * width]);
                AddBorderColor(borderCounts, pixels[y * width + width - 1]);
            }

            if (borderCounts.Count == 0)
                return 0;

            return borderCounts
                .OrderByDescending(x => x.Value)
                .First()
                .Key;
        }

        private static void AddBorderColor(Dictionary<byte, int> counts, byte color)
        {
            int count;
            counts.TryGetValue(color, out count);
            counts[color] = count + 1;
        }

        private static byte[] DecodeRle(byte[] bytes, int start, int end, int expectedLength)
        {
            byte[] output = new byte[expectedLength];
            int inputIndex = start;
            int outputIndex = 0;

            while (inputIndex < end && outputIndex < expectedLength)
            {
                byte value = bytes[inputIndex++];
                int count = 1;

                if ((value & 0xC0) == 0xC0)
                {
                    count = value & 0x3F;
                    if (inputIndex >= end)
                        break;

                    value = bytes[inputIndex++];
                }

                for (int i = 0; i < count && outputIndex < expectedLength; i++)
                    output[outputIndex++] = value;
            }

            if (outputIndex < expectedLength)
                throw new InvalidDataException("PCX truncado: dados RLE insuficientes.");

            return output;
        }

        private static byte[] ReadPcxPalette(byte[] bytes)
        {
            if (bytes.Length < 769 || bytes[bytes.Length - 769] != 12)
                return null;

            byte[] palette = new byte[768];
            Buffer.BlockCopy(bytes, bytes.Length - 768, palette, 0, palette.Length);
            return palette;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }
    }

    internal static class PaletteReader
    {
        public static byte[] ReadActPalette(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 768)
                throw new InvalidDataException("Arquivo ACT invalido: " + path);

            byte[] palette = new byte[768];
            Buffer.BlockCopy(bytes, 0, palette, 0, palette.Length);
            return palette;
        }

        public static byte[] ReversePalette(byte[] palette)
        {
            byte[] source = RenderedAnimation.EnsurePalette(palette);
            byte[] reversed = new byte[768];

            for (int i = 0; i < 256; i++)
            {
                int sourceOffset = (255 - i) * 3;
                int targetOffset = i * 3;
                reversed[targetOffset] = source[sourceOffset];
                reversed[targetOffset + 1] = source[sourceOffset + 1];
                reversed[targetOffset + 2] = source[sourceOffset + 2];
            }

            return reversed;
        }

        public static byte[] CreateDefaultPalette()
        {
            byte[] palette = new byte[768];
            for (int i = 0; i < 256; i++)
            {
                palette[i * 3] = (byte)i;
                palette[i * 3 + 1] = (byte)i;
                palette[i * 3 + 2] = (byte)i;
            }

            return palette;
        }
    }

    internal static class StandRenderer
    {
        private const int MugenTicksPerSecond = 60;

        public static RenderedAnimation Render(
            List<AirFrame> frames,
            ISpriteSource spriteSource,
            byte[] preferredPalette,
            byte[] fallbackPalette,
            int padding,
            bool preserveSpriteCanvas = false,
            bool hasReliableFallbackPalette = false)
        {
            if (frames == null || frames.Count == 0)
                throw new InvalidDataException("A animacao de stand nao possui frames.");

            int canvasPadding = Math.Max(0, padding);
            List<Tuple<AirFrame, IndexedSprite, SpriteBounds>> drawableFrames = new List<Tuple<AirFrame, IndexedSprite, SpriteBounds>>();

            foreach (AirFrame frame in frames)
            {
                if (frame.Group < 0 || frame.Image < 0)
                    continue;

                IndexedSprite sprite = spriteSource.GetSprite(frame.Group, frame.Image);
                if (sprite == null || sprite.VisibleBounds == null)
                    continue;

                SpriteBounds bounds = preserveSpriteCanvas ? GetSpriteCanvasBounds(frame, sprite) : GetBounds(frame, sprite);
                drawableFrames.Add(Tuple.Create(frame, sprite, bounds));
            }

            if (drawableFrames.Count == 0)
            {
                IndexedSprite fallbackSprite = spriteSource.GetFallbackSprite();
                if (fallbackSprite == null || fallbackSprite.VisibleBounds == null)
                    throw new InvalidDataException("Nao encontrei nenhum sprite valido para renderizar o stand.");

                AirFrame fallbackFrame = new AirFrame(fallbackSprite.Group, fallbackSprite.Image, 0, 0, 8, false, false);
                SpriteBounds fallbackBounds = preserveSpriteCanvas ? GetSpriteCanvasBounds(fallbackFrame, fallbackSprite) : GetBounds(fallbackFrame, fallbackSprite);
                drawableFrames.Add(Tuple.Create(fallbackFrame, fallbackSprite, fallbackBounds));
            }

            int minLeft = drawableFrames.Min(x => x.Item3.Left) - canvasPadding;
            int minTop = drawableFrames.Min(x => x.Item3.Top) - canvasPadding;
            int maxRight = drawableFrames.Max(x => x.Item3.Right) + canvasPadding;
            int maxBottom = drawableFrames.Max(x => x.Item3.Bottom) + canvasPadding;
            int width = Math.Max(1, maxRight - minLeft);
            int height = Math.Max(1, maxBottom - minTop);
            byte[] indexedPalette = GlobalPaletteBuilder.Build(drawableFrames.Select(x => x.Item2), fallbackPalette, 0);
            byte[] globalPalette = ChooseIndexedPalette(
                drawableFrames.Select(x => x.Item2),
                preferredPalette,
                indexedPalette,
                fallbackPalette,
                hasReliableFallbackPalette);

            globalPalette = RenderedAnimation.EnsurePalette(globalPalette);

            RenderedAnimation animation = new RenderedAnimation(width, height, globalPalette);

            foreach (Tuple<AirFrame, IndexedSprite, SpriteBounds> item in drawableFrames)
            {
                byte transparentIndex = item.Item2.TransparentIndex;
                byte[] canvas = CreateTransparentCanvas(width, height, transparentIndex);
                byte[] paletteMap = PaletteMapper.CreateIdentityMap();

                DrawSprite(
                    canvas,
                    width,
                    height,
                    item.Item2,
                    GetSpriteLeft(item.Item1, item.Item2) - minLeft,
                    GetSpriteTop(item.Item1, item.Item2) - minTop,
                    item.Item1,
                    paletteMap);

                if (HasVisiblePixels(canvas, transparentIndex))
                    animation.Frames.Add(new GifFrame(canvas, ConvertTicksToGifDelay(item.Item1.Duration), globalPalette, transparentIndex));
            }

            if (animation.Frames.Count == 0)
                throw new InvalidDataException("A animacao foi lida, mas todos os frames ficaram transparentes. O indice de transparencia do PCX provavelmente nao bate com este personagem.");

            return preserveSpriteCanvas ? animation : TrimTransparentEdges(animation, canvasPadding);
        }

        private static byte[] ChooseIndexedPalette(
            IEnumerable<IndexedSprite> sprites,
            byte[] preferredPalette,
            byte[] indexedPalette,
            byte[] fallbackPalette,
            bool hasReliableFallbackPalette)
        {
            if (preferredPalette == null)
                return indexedPalette;

            List<IndexedSprite> spriteList = sprites
                .Where(sprite => sprite != null && sprite.VisibleBounds != null)
                .ToList();

            byte[] reversedPreferredPalette = PaletteReader.ReversePalette(preferredPalette);
            IndexedSprite firstSprite = spriteList.FirstOrDefault();
            byte transparentIndex = firstSprite == null ? (byte)0 : firstSprite.TransparentIndex;

            IndexedSprite referenceSprite = spriteList.FirstOrDefault(sprite => sprite.Palette != null);
            if (hasReliableFallbackPalette || referenceSprite != null)
            {
                IEnumerable<IndexedSprite> referenceSprites = hasReliableFallbackPalette
                    ? (IEnumerable<IndexedSprite>)spriteList
                    : new[] { referenceSprite };
                long preferredScore = GlobalPaletteBuilder.Score(referenceSprites, preferredPalette, fallbackPalette);
                long reversedPreferredScore = GlobalPaletteBuilder.Score(referenceSprites, reversedPreferredPalette, fallbackPalette);

                if (preferredScore != reversedPreferredScore)
                    return reversedPreferredScore < preferredScore ? reversedPreferredPalette : preferredPalette;
            }

            return ShouldReverseForTransparentColor(preferredPalette, transparentIndex)
                || ShouldReverseForHighIndexedSprites(spriteList, transparentIndex)
                ? reversedPreferredPalette
                : preferredPalette;
        }

        private static bool ShouldReverseForHighIndexedSprites(IEnumerable<IndexedSprite> sprites, byte transparentIndex)
        {
            if (transparentIndex != 0)
                return false;

            long visiblePixels = 0;
            long highIndexPixels = 0;

            foreach (IndexedSprite sprite in sprites)
            {
                if (sprite == null || sprite.VisibleBounds == null)
                    continue;

                for (int y = sprite.VisibleBounds.Top; y < sprite.VisibleBounds.Bottom; y++)
                {
                    int rowOffset = y * sprite.Width;
                    for (int x = sprite.VisibleBounds.Left; x < sprite.VisibleBounds.Right; x++)
                    {
                        byte colorIndex = sprite.Pixels[rowOffset + x];
                        if (colorIndex == sprite.TransparentIndex)
                            continue;

                        visiblePixels++;
                        if (colorIndex >= 128)
                            highIndexPixels++;
                    }
                }
            }

            return visiblePixels > 0 && highIndexPixels * 100 >= visiblePixels * 85;
        }

        private static bool ShouldReverseForTransparentColor(byte[] palette, byte transparentIndex)
        {
            byte[] source = RenderedAnimation.EnsurePalette(palette);
            return transparentIndex == 0
                && IsTransparentMagenta(source, 255)
                && !IsTransparentMagenta(source, 0);
        }

        private static bool IsTransparentMagenta(byte[] palette, int index)
        {
            int offset = index * 3;
            return palette[offset] >= 240
                && palette[offset + 1] <= 16
                && palette[offset + 2] >= 240;
        }

        private static SpriteBounds GetBounds(AirFrame frame, IndexedSprite sprite)
        {
            int left = GetSpriteLeft(frame, sprite);
            int top = GetSpriteTop(frame, sprite);

            SpriteBounds visible = sprite.VisibleBounds;
            int visibleLeft = frame.FlipHorizontal ? sprite.Width - visible.Right : visible.Left;
            int visibleRight = frame.FlipHorizontal ? sprite.Width - visible.Left : visible.Right;
            int visibleTop = frame.FlipVertical ? sprite.Height - visible.Bottom : visible.Top;
            int visibleBottom = frame.FlipVertical ? sprite.Height - visible.Top : visible.Bottom;

            return new SpriteBounds(
                left + visibleLeft,
                top + visibleTop,
                left + visibleRight,
                top + visibleBottom);
        }

        private static SpriteBounds GetSpriteCanvasBounds(AirFrame frame, IndexedSprite sprite)
        {
            int left = GetSpriteLeft(frame, sprite);
            int top = GetSpriteTop(frame, sprite);

            return new SpriteBounds(
                left,
                top,
                left + sprite.Width,
                top + sprite.Height);
        }

        private static int GetSpriteLeft(AirFrame frame, IndexedSprite sprite)
        {
            return frame.FlipHorizontal
                ? frame.X - (sprite.Width - sprite.AxisX)
                : frame.X - sprite.AxisX;
        }

        private static int GetSpriteTop(AirFrame frame, IndexedSprite sprite)
        {
            return frame.FlipVertical
                ? frame.Y - (sprite.Height - sprite.AxisY)
                : frame.Y - sprite.AxisY;
        }

        private static int ConvertTicksToGifDelay(int ticks)
        {
            if (ticks <= 0)
                ticks = 8;

            return Math.Max(2, (int)Math.Round(ticks * 100.0 / MugenTicksPerSecond));
        }

        private static void DrawSprite(byte[] canvas, int canvasWidth, int canvasHeight, IndexedSprite sprite, int left, int top, AirFrame frame, byte[] paletteMap)
        {
            for (int y = 0; y < sprite.Height; y++)
            {
                int sourceY = frame.FlipVertical ? sprite.Height - 1 - y : y;
                int destinationY = top + y;

                if (destinationY < 0 || destinationY >= canvasHeight)
                    continue;

                for (int x = 0; x < sprite.Width; x++)
                {
                    int sourceX = frame.FlipHorizontal ? sprite.Width - 1 - x : x;
                    int destinationX = left + x;

                    if (destinationX < 0 || destinationX >= canvasWidth)
                        continue;

                    byte colorIndex = sprite.Pixels[sourceY * sprite.Width + sourceX];
                    if (colorIndex == sprite.TransparentIndex)
                        continue;

                    canvas[destinationY * canvasWidth + destinationX] = paletteMap[colorIndex];
                }
            }
        }

        private static byte[] CreateTransparentCanvas(int width, int height, byte transparentIndex)
        {
            byte[] canvas = new byte[width * height];
            if (transparentIndex == 0)
                return canvas;

            for (int i = 0; i < canvas.Length; i++)
                canvas[i] = transparentIndex;

            return canvas;
        }

        private static bool HasVisiblePixels(byte[] pixels, byte transparentIndex)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] != transparentIndex)
                    return true;
            }

            return false;
        }

        private static RenderedAnimation TrimTransparentEdges(RenderedAnimation animation, int padding)
        {
            SpriteBounds visibleBounds = GetAnimationVisibleBounds(animation);
            if (visibleBounds == null)
                return animation;

            int left = Math.Max(0, visibleBounds.Left - padding);
            int top = Math.Max(0, visibleBounds.Top - padding);
            int right = Math.Min(animation.Width, visibleBounds.Right + padding);
            int bottom = Math.Min(animation.Height, visibleBounds.Bottom + padding);
            int width = Math.Max(1, right - left);
            int height = Math.Max(1, bottom - top);

            if (left == 0 && top == 0 && width == animation.Width && height == animation.Height)
                return animation;

            RenderedAnimation trimmed = new RenderedAnimation(width, height, animation.Palette);
            foreach (GifFrame frame in animation.Frames)
            {
                byte[] pixels = CreateTransparentCanvas(width, height, frame.TransparentIndex);

                for (int y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(
                        frame.Pixels,
                        ((top + y) * animation.Width) + left,
                        pixels,
                        y * width,
                        width);
                }

                trimmed.Frames.Add(new GifFrame(pixels, frame.Delay, frame.Palette, frame.TransparentIndex));
            }

            return trimmed;
        }

        private static SpriteBounds GetAnimationVisibleBounds(RenderedAnimation animation)
        {
            int minX = animation.Width;
            int minY = animation.Height;
            int maxX = -1;
            int maxY = -1;

            foreach (GifFrame frame in animation.Frames)
            {
                for (int y = 0; y < animation.Height; y++)
                {
                    int rowOffset = y * animation.Width;
                    for (int x = 0; x < animation.Width; x++)
                    {
                        if (frame.Pixels[rowOffset + x] == frame.TransparentIndex)
                            continue;

                        if (x < minX)
                            minX = x;

                        if (y < minY)
                            minY = y;

                        if (x > maxX)
                            maxX = x;

                        if (y > maxY)
                            maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
                return null;

            return new SpriteBounds(minX, minY, maxX + 1, maxY + 1);
        }
    }

    internal sealed class SpriteBounds
    {
        public SpriteBounds(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Right { get; private set; }
        public int Bottom { get; private set; }

        public static SpriteBounds FromPixels(byte[] pixels, int width, int height, byte transparentIndex)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[rowOffset + x] == transparentIndex)
                        continue;

                    if (x < minX)
                        minX = x;

                    if (y < minY)
                        minY = y;

                    if (x > maxX)
                        maxX = x;

                    if (y > maxY)
                        maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return null;

            return new SpriteBounds(minX, minY, maxX + 1, maxY + 1);
        }
    }

    internal sealed class RenderedAnimation
    {
        public RenderedAnimation(int width, int height, byte[] palette)
        {
            Width = width;
            Height = height;
            Palette = EnsurePalette(palette);
            Frames = new List<GifFrame>();
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Palette { get; private set; }
        public List<GifFrame> Frames { get; private set; }

        public static byte[] EnsurePalette(byte[] palette)
        {
            byte[] result = new byte[768];

            if (palette != null)
                Buffer.BlockCopy(palette, 0, result, 0, Math.Min(result.Length, palette.Length));

            return result;
        }
    }

    internal sealed class GifFrame
    {
        public GifFrame(byte[] pixels, int delay, byte[] palette, byte transparentIndex)
        {
            Pixels = pixels;
            Delay = delay;
            Palette = RenderedAnimation.EnsurePalette(palette);
            TransparentIndex = transparentIndex;
        }

        public byte[] Pixels { get; private set; }
        public int Delay { get; private set; }
        public byte[] Palette { get; private set; }
        public byte TransparentIndex { get; private set; }
    }

    internal static class PaletteMapper
    {
        public static byte[] CreateIdentityMap()
        {
            byte[] map = new byte[256];
            for (int i = 0; i < map.Length; i++)
                map[i] = (byte)i;

            return map;
        }

        public static byte[] CreateMap(byte[] sourcePalette, byte[] targetPalette, byte sourceTransparentIndex, byte targetTransparentIndex)
        {
            byte[] source = RenderedAnimation.EnsurePalette(sourcePalette);
            byte[] target = RenderedAnimation.EnsurePalette(targetPalette);
            byte[] map = new byte[256];
            Dictionary<int, byte> exactColors = BuildExactColorMap(target, targetTransparentIndex);

            for (int i = 0; i < 256; i++)
            {
                if (i == sourceTransparentIndex)
                {
                    map[i] = targetTransparentIndex;
                    continue;
                }

                int key = ColorKey(source, i);
                byte exactIndex;
                map[i] = exactColors.TryGetValue(key, out exactIndex)
                    ? exactIndex
                    : FindNearestColor(source, target, i, targetTransparentIndex);
            }

            return map;
        }

        private static Dictionary<int, byte> BuildExactColorMap(byte[] palette, byte transparentIndex)
        {
            Dictionary<int, byte> colors = new Dictionary<int, byte>();

            for (int i = 0; i < 256; i++)
            {
                if (i == transparentIndex)
                    continue;

                int key = ColorKey(palette, i);
                if (!colors.ContainsKey(key))
                    colors[key] = (byte)i;
            }

            return colors;
        }

        private static byte FindNearestColor(byte[] sourcePalette, byte[] targetPalette, int sourceIndex, byte transparentIndex)
        {
            int sourceOffset = sourceIndex * 3;
            int sourceR = sourcePalette[sourceOffset];
            int sourceG = sourcePalette[sourceOffset + 1];
            int sourceB = sourcePalette[sourceOffset + 2];
            int bestIndex = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < 256; i++)
            {
                if (i == transparentIndex)
                    continue;

                int targetOffset = i * 3;
                int r = sourceR - targetPalette[targetOffset];
                int g = sourceG - targetPalette[targetOffset + 1];
                int b = sourceB - targetPalette[targetOffset + 2];
                int distance = (r * r) + (g * g) + (b * b);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return (byte)bestIndex;
        }

        private static int ColorKey(byte[] palette, int index)
        {
            int offset = index * 3;
            return (palette[offset] << 16) | (palette[offset + 1] << 8) | palette[offset + 2];
        }
    }

    internal static class GlobalPaletteBuilder
    {
        public static byte[] Build(IEnumerable<IndexedSprite> sprites, byte[] fallbackPalette, byte transparentIndex)
        {
            long[] red = new long[256];
            long[] green = new long[256];
            long[] blue = new long[256];
            long[] weights = new long[256];

            foreach (IndexedSprite sprite in sprites)
            {
                if (sprite == null || sprite.VisibleBounds == null)
                    continue;

                byte[] palette = RenderedAnimation.EnsurePalette(sprite.Palette ?? fallbackPalette);

                for (int y = sprite.VisibleBounds.Top; y < sprite.VisibleBounds.Bottom; y++)
                {
                    int rowOffset = y * sprite.Width;
                    for (int x = sprite.VisibleBounds.Left; x < sprite.VisibleBounds.Right; x++)
                    {
                        byte colorIndex = sprite.Pixels[rowOffset + x];
                        if (colorIndex == sprite.TransparentIndex)
                            continue;

                        int offset = colorIndex * 3;
                        red[colorIndex] += palette[offset];
                        green[colorIndex] += palette[offset + 1];
                        blue[colorIndex] += palette[offset + 2];
                        weights[colorIndex]++;
                    }
                }
            }

            byte[] result = RenderedAnimation.EnsurePalette(fallbackPalette ?? PaletteReader.CreateDefaultPalette());

            for (int i = 0; i < 256; i++)
            {
                if (weights[i] == 0)
                    continue;

                int offset = i * 3;
                result[offset] = (byte)(red[i] / weights[i]);
                result[offset + 1] = (byte)(green[i] / weights[i]);
                result[offset + 2] = (byte)(blue[i] / weights[i]);
            }

            int transparentOffset = transparentIndex * 3;
            result[transparentOffset] = 0;
            result[transparentOffset + 1] = 0;
            result[transparentOffset + 2] = 0;
            return result;
        }

        public static long Score(IEnumerable<IndexedSprite> sprites, byte[] candidatePalette, byte[] fallbackPalette)
        {
            byte[] candidate = RenderedAnimation.EnsurePalette(candidatePalette);
            long score = 0;
            long pixels = 0;

            foreach (IndexedSprite sprite in sprites)
            {
                if (sprite == null || sprite.VisibleBounds == null)
                    continue;

                byte[] source = RenderedAnimation.EnsurePalette(sprite.Palette ?? fallbackPalette);

                for (int y = sprite.VisibleBounds.Top; y < sprite.VisibleBounds.Bottom; y++)
                {
                    int rowOffset = y * sprite.Width;
                    for (int x = sprite.VisibleBounds.Left; x < sprite.VisibleBounds.Right; x++)
                    {
                        byte colorIndex = sprite.Pixels[rowOffset + x];
                        if (colorIndex == sprite.TransparentIndex)
                            continue;

                        int offset = colorIndex * 3;
                        int r = source[offset] - candidate[offset];
                        int g = source[offset + 1] - candidate[offset + 1];
                        int b = source[offset + 2] - candidate[offset + 2];
                        score += (r * r) + (g * g) + (b * b);
                        pixels++;
                    }
                }
            }

            return pixels == 0 ? long.MaxValue : score / pixels;
        }
    }

    internal static class PngImageWriter
    {
        public static void Write(string path, RenderedAnimation animation)
        {
            if (animation.Frames.Count == 0)
                throw new InvalidDataException("Nao ha frame para salvar no PNG.");

            GifFrame frame = animation.Frames[0];
            byte[] palette = RenderedAnimation.EnsurePalette(frame.Palette);

            using (Bitmap bitmap = new Bitmap(animation.Width, animation.Height, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < animation.Height; y++)
                {
                    int rowOffset = y * animation.Width;
                    for (int x = 0; x < animation.Width; x++)
                    {
                        byte colorIndex = frame.Pixels[rowOffset + x];
                        if (colorIndex == frame.TransparentIndex)
                        {
                            bitmap.SetPixel(x, y, Color.Transparent);
                            continue;
                        }

                        int paletteOffset = colorIndex * 3;
                        bitmap.SetPixel(
                            x,
                            y,
                            Color.FromArgb(255, palette[paletteOffset], palette[paletteOffset + 1], palette[paletteOffset + 2]));
                    }
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }

    internal static class GifAnimationWriter
    {
        public static void Write(string path, RenderedAnimation animation)
        {
            if (animation.Frames.Count == 0)
                throw new InvalidDataException("Nao ha frames para salvar no GIF.");

            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                WriteAscii(writer, "GIF89a");
                WriteShort(writer, animation.Width);
                WriteShort(writer, animation.Height);
                writer.Write((byte)0xF7);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write(animation.Palette, 0, 768);

                WriteLoopExtension(writer);

                foreach (GifFrame frame in animation.Frames)
                {
                    WriteGraphicControlExtension(writer, frame.Delay, frame.TransparentIndex);
                    WriteImageDescriptor(writer, animation.Width, animation.Height, false);
                    writer.Write((byte)8);
                    WriteSubBlocks(writer, GifLzwEncoder.Encode(frame.Pixels, 8));
                }

                writer.Write((byte)0x3B);
            }
        }

        private static void WriteGraphicControlExtension(BinaryWriter writer, int delay, byte transparentIndex)
        {
            writer.Write((byte)0x21);
            writer.Write((byte)0xF9);
            writer.Write((byte)0x04);
            writer.Write((byte)0x09);
            WriteShort(writer, delay);
            writer.Write(transparentIndex);
            writer.Write((byte)0);
        }

        private static void WriteImageDescriptor(BinaryWriter writer, int width, int height, bool hasLocalPalette)
        {
            writer.Write((byte)0x2C);
            WriteShort(writer, 0);
            WriteShort(writer, 0);
            WriteShort(writer, width);
            WriteShort(writer, height);
            writer.Write(hasLocalPalette ? (byte)0x87 : (byte)0);
        }

        private static void WriteLoopExtension(BinaryWriter writer)
        {
            writer.Write((byte)0x21);
            writer.Write((byte)0xFF);
            writer.Write((byte)0x0B);
            WriteAscii(writer, "NETSCAPE2.0");
            writer.Write((byte)0x03);
            writer.Write((byte)0x01);
            WriteShort(writer, 0);
            writer.Write((byte)0);
        }

        private static void WriteSubBlocks(BinaryWriter writer, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = Math.Min(255, bytes.Length - offset);
                writer.Write((byte)count);
                writer.Write(bytes, offset, count);
                offset += count;
            }

            writer.Write((byte)0);
        }

        private static void WriteAscii(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
        }

        private static void WriteShort(BinaryWriter writer, int value)
        {
            writer.Write((byte)(value & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
        }
    }

    internal static class GifLzwEncoder
    {
        public static byte[] Encode(byte[] pixels, int minimumCodeSize)
        {
            int clearCode = 1 << minimumCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minimumCodeSize + 1;
            BitWriter bitWriter = new BitWriter();

            // Keep the stream deliberately simple: emit raw pixel codes and clear
            // before the decoder needs to grow from 9 to 10 bits.
            const int maxRawCodesPerClear = 250;
            int codesSinceClear = 0;

            bitWriter.Write(clearCode, codeSize);

            for (int i = 0; i < pixels.Length; i++)
            {
                if (codesSinceClear >= maxRawCodesPerClear)
                {
                    bitWriter.Write(clearCode, codeSize);
                    codesSinceClear = 0;
                }

                bitWriter.Write(pixels[i], codeSize);
                codesSinceClear++;
            }

            bitWriter.Write(endCode, codeSize);
            return bitWriter.ToArray();
        }
    }

    internal sealed class BitWriter
    {
        private readonly List<byte> bytes = new List<byte>();
        private int currentByte;
        private int bitCount;

        public void Write(int code, int size)
        {
            for (int i = 0; i < size; i++)
            {
                currentByte |= ((code >> i) & 1) << bitCount;
                bitCount++;

                if (bitCount == 8)
                {
                    bytes.Add((byte)currentByte);
                    currentByte = 0;
                    bitCount = 0;
                }
            }
        }

        public byte[] ToArray()
        {
            if (bitCount > 0)
            {
                bytes.Add((byte)currentByte);
                currentByte = 0;
                bitCount = 0;
            }

            return bytes.ToArray();
        }
    }
}
