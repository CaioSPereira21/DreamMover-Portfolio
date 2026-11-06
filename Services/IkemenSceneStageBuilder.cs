using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamMover.Services
{
    public sealed class CenarioCenaConfiguration
    {
        public int Round { get; set; }
        public string CenarioNome { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Nome { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<int> BackgroundIds { get; set; }

        public string Transicao { get; set; }
        public int DuracaoFrames { get; set; }
    }

    public sealed class IkemenSceneStageSource
    {
        public int Round { get; set; }
        public string Nome { get; set; }
        public string StageReference { get; set; }
        public string Transicao { get; set; }
        public int DuracaoFrames { get; set; }
    }

    public static class CenarioSceneConfiguration
    {
        private const int MaximoCenas = 10;
        private const int MaximoJsonLength = 65536;

        public static List<CenarioCenaConfiguration> Deserializar(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<CenarioCenaConfiguration>();

            try
            {
                return Normalizar(
                    JsonConvert.DeserializeObject<List<CenarioCenaConfiguration>>(json),
                    null,
                    false);
            }
            catch (JsonException)
            {
                return new List<CenarioCenaConfiguration>();
            }
            catch (InvalidOperationException)
            {
                return new List<CenarioCenaConfiguration>();
            }
        }

        public static string ValidarESerializar(string json, IEnumerable<string> cenariosDisponiveis)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                return null;
            if (json.Length > MaximoJsonLength)
                throw new InvalidOperationException("O roteiro de cenarios ultrapassou o tamanho permitido.");

            List<CenarioCenaConfiguration> cenas;
            try
            {
                cenas = JsonConvert.DeserializeObject<List<CenarioCenaConfiguration>>(json);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("O roteiro de cenarios enviado e invalido.");
            }

            List<CenarioCenaConfiguration> normalizadas = Normalizar(cenas, cenariosDisponiveis, true);
            return normalizadas.Count == 0 ? null : JsonConvert.SerializeObject(normalizadas);
        }

        public static List<CenarioCenaConfiguration> Validar(
            IEnumerable<CenarioCenaConfiguration> cenas,
            IEnumerable<string> cenariosDisponiveis)
        {
            return Normalizar(cenas, cenariosDisponiveis, true);
        }

        private static List<CenarioCenaConfiguration> Normalizar(
            IEnumerable<CenarioCenaConfiguration> cenas,
            IEnumerable<string> cenariosDisponiveis,
            bool exigirRoteiroCompleto)
        {
            List<CenarioCenaConfiguration> origem = (cenas ?? Enumerable.Empty<CenarioCenaConfiguration>())
                .Where(x => x != null)
                .ToList();

            if (origem.Count == 0)
                return new List<CenarioCenaConfiguration>();
            if (origem.Count > MaximoCenas)
                throw new InvalidOperationException("Configure no maximo " + MaximoCenas + " cenarios por roteiro.");
            if (exigirRoteiroCompleto && origem.Count < 2)
                throw new InvalidOperationException("Um roteiro precisa possuir ao menos dois cenarios.");

            Dictionary<string, List<string>> catalogo = cenariosDisponiveis == null
                ? null
                : cenariosDisponiveis
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            HashSet<int> rounds = new HashSet<int>();
            List<CenarioCenaConfiguration> resultado = new List<CenarioCenaConfiguration>();

            foreach (CenarioCenaConfiguration cena in origem.OrderBy(x => x.Round))
            {
                if (cena.Round < 1 || cena.Round > 99)
                    throw new InvalidOperationException("O round de cada cenario deve estar entre 1 e 99.");
                if (!rounds.Add(cena.Round))
                    throw new InvalidOperationException("Existe mais de um cenario configurado para o round " + cena.Round + ".");

                string nome = !string.IsNullOrWhiteSpace(cena.CenarioNome)
                    ? cena.CenarioNome.Trim()
                    : (string.IsNullOrWhiteSpace(cena.Nome) ? null : cena.Nome.Trim());
                if (string.IsNullOrWhiteSpace(nome))
                    throw new InvalidOperationException("Selecione o cenario do round " + cena.Round + ".");
                if (nome.Length > 255)
                    throw new InvalidOperationException("O nome de um cenario ultrapassou 255 caracteres.");

                if (catalogo != null)
                {
                    List<string> encontrados;
                    if (!catalogo.TryGetValue(nome, out encontrados))
                        throw new InvalidOperationException("O cenario '" + nome + "' nao esta disponivel no catalogo.");
                    if (encontrados.Count > 1)
                        throw new InvalidOperationException("Existe mais de um cenario chamado '" + nome + "'. Renomeie um deles antes de montar o roteiro.");
                    nome = encontrados[0];
                }

                string transicao = string.Equals(cena.Transicao, "fade", StringComparison.OrdinalIgnoreCase)
                    ? "fade"
                    : (string.Equals(cena.Transicao, "queda", StringComparison.OrdinalIgnoreCase) ? "queda" : "corte");
                bool transicaoAnimada = transicao == "fade" || transicao == "queda";
                int duracao = transicaoAnimada ? cena.DuracaoFrames : 0;
                if (transicaoAnimada && (duracao < 1 || duracao > 180))
                    throw new InvalidOperationException("A duracao da transicao deve estar entre 1 e 180 frames.");
                if (cena.Round == 1)
                {
                    transicao = "corte";
                    duracao = 0;
                }

                resultado.Add(new CenarioCenaConfiguration
                {
                    Round = cena.Round,
                    CenarioNome = nome,
                    Transicao = transicao,
                    DuracaoFrames = duracao
                });
            }

            if (resultado[0].Round != 1)
                throw new InvalidOperationException("O primeiro cenario precisa comecar no round 1.");

            return resultado;
        }
    }

    public sealed class IkemenSceneStageRuntime : IDisposable
    {
        private readonly string _generatedStagePath;
        private readonly string _runtimeDirectory;

        internal IkemenSceneStageRuntime(string stageArgument, string generatedStagePath, string runtimeDirectory)
        {
            StageArgument = stageArgument;
            _generatedStagePath = generatedStagePath;
            _runtimeDirectory = runtimeDirectory;
        }

        public string StageArgument { get; private set; }
        public bool IsGenerated { get { return !string.IsNullOrWhiteSpace(_generatedStagePath); } }

        public void Dispose()
        {
            TryDeleteFile(_generatedStagePath);

            if (string.IsNullOrWhiteSpace(_runtimeDirectory))
                return;

            try
            {
                DirectoryInfo directory = new DirectoryInfo(_runtimeDirectory);
                if (directory.Name.StartsWith(IkemenSceneStageBuilder.RuntimePrefix, StringComparison.OrdinalIgnoreCase)
                    && directory.Exists)
                    directory.Delete(true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Name.StartsWith(IkemenSceneStageBuilder.RuntimePrefix, StringComparison.OrdinalIgnoreCase)
                    && file.Exists)
                    file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static class IkemenSceneStageBuilder
    {
        internal const string RuntimePrefix = "__dreammover-";
        private const int HelperId = 190001;
        private const int FirstActionNumber = 700000;
        private const int FirstBackgroundId = 800000;
        private const int FirstControllerId = 900000;
        private const int ResultAnnouncementMinimumFrames = 60;
        private const int BlackoutDurationFrames = 20;
        private static readonly Encoding StageEncoding = Encoding.GetEncoding(28591);
        private static readonly Regex SectionRegex = new Regex(
            @"^\s*\[(?<name>[^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex AssignmentRegex = new Regex(
            @"^\s*(?<key>[^=]+?)\s*=\s*(?<value>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex AttachedCharRegex = new Regex(
            @"^\s*attachedchar(?<index>[2-4])?\s*=",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex SctrlIdRegex = new Regex(
            @"^\s*sctrlid\s*=\s*(?<value>-?\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IkemenSceneStageRuntime Preparar(
            string ikemenRootPath,
            string stageReference,
            IEnumerable<IkemenSceneStageSource> cenas)
        {
            List<IkemenSceneStageSource> roteiro = NormalizarFontes(cenas);
            if (roteiro.Count == 0)
                return new IkemenSceneStageRuntime(stageReference, null, null);

            if (string.IsNullOrWhiteSpace(ikemenRootPath))
                throw new ArgumentException("Informe a pasta raiz do IKEMEN.", "ikemenRootPath");
            if (string.IsNullOrWhiteSpace(stageReference))
                throw new ArgumentException("Informe o arquivo do cenario.", "stageReference");

            string ikemenRoot = Path.GetFullPath(ikemenRootPath);
            string stagesRoot = Path.GetFullPath(Path.Combine(ikemenRoot, "stages"));
            List<ParsedStageSource> fontes = roteiro
                .Select(x => CarregarFonte(ikemenRoot, stagesRoot, x))
                .ToList();
            ValidarCompatibilidade(fontes);

            ParsedStageSource primeiraFonte = fontes[0];
            string token = Guid.NewGuid().ToString("N");
            string runtimeName = RuntimePrefix + token;
            string originalDirectory = Path.GetDirectoryName(primeiraFonte.StagePath);
            string runtimeDirectory = Path.Combine(originalDirectory, runtimeName);
            string generatedStagePath = Path.Combine(
                originalDirectory,
                runtimeName + "-" + Path.GetFileName(primeiraFonte.StagePath));

            Directory.CreateDirectory(runtimeDirectory);
            try
            {
                string mergedSpritePath = Path.Combine(runtimeDirectory, "scenes.sff");
                List<Dictionary<int, int>> groupMaps = MesclarSffV1(
                    fontes.Select(x => x.SpritePath).ToList(),
                    mergedSpritePath);

                int nextBackgroundId = FirstBackgroundId;
                int nextActionNumber = FirstActionNumber;
                List<RuntimeScene> runtimeScenes = new List<RuntimeScene>();
                StringBuilder backgrounds = new StringBuilder();
                for (int index = 0; index < fontes.Count; index++)
                {
                    RuntimeScene runtimeScene;
                    string transformed = TransformarCorpoDoStage(
                        fontes[index].BackgroundBody,
                        groupMaps[index],
                        fontes[index].Configuration,
                        fontes[index].LocalCoordHeight,
                        fontes[index].ZOffset,
                        index,
                        ref nextBackgroundId,
                        ref nextActionNumber,
                        out runtimeScene);
                    runtimeScenes.Add(runtimeScene);
                    backgrounds.AppendLine();
                    backgrounds.AppendLine("; Cena do catalogo: " + fontes[index].Configuration.Nome);
                    backgrounds.AppendLine(transformed.Trim());
                }

                int baseZOffset = runtimeScenes[0].ZOffset;
                foreach (RuntimeScene scene in runtimeScenes)
                    scene.BaseOffsetY = baseZOffset - scene.ZOffset;
                ValidarTransicoesDeQueda(runtimeScenes);

                string relativeSprite = GetRelativePath(ikemenRoot, mergedSpritePath).Replace('\\', '/');
                string baseStage = GerarCabecalhoDoStage(primeiraFonte.StageText, relativeSprite);
                string attachedCharKey = ObterSlotAttachedChar(baseStage);
                string controllerDefPath = Path.Combine(runtimeDirectory, "controller.def");
                string controllerZssPath = Path.Combine(runtimeDirectory, "controller.zss");
                string relativeController = GetRelativePath(stagesRoot, controllerDefPath).Replace('\\', '/');
                string relativeStage = GetRelativePath(stagesRoot, generatedStagePath).Replace('\\', '/');

                string stageWithoutControllers = AdicionarAttachedChar(baseStage, attachedCharKey, relativeController)
                    + Environment.NewLine
                    + backgrounds;
                HashSet<int> existingControllers = ObterSctrlIds(stageWithoutControllers);
                List<int> controllerIds = ReservarControllerIds(existingControllers, runtimeScenes.Count);
                string generatedStage = stageWithoutControllers
                    + Environment.NewLine
                    + GerarBackgroundControllers(runtimeScenes, controllerIds);

                File.WriteAllText(generatedStagePath, generatedStage, StageEncoding);
                File.WriteAllText(controllerDefPath, GerarControllerDef(baseStage), Encoding.Default);
                File.WriteAllText(controllerZssPath, GerarControllerZss(runtimeScenes, controllerIds), new UTF8Encoding(false));

                return new IkemenSceneStageRuntime("stages/" + relativeStage, generatedStagePath, runtimeDirectory);
            }
            catch
            {
                new IkemenSceneStageRuntime(stageReference, generatedStagePath, runtimeDirectory).Dispose();
                throw;
            }
        }

        private static List<IkemenSceneStageSource> NormalizarFontes(IEnumerable<IkemenSceneStageSource> cenas)
        {
            List<IkemenSceneStageSource> fontes = (cenas ?? Enumerable.Empty<IkemenSceneStageSource>())
                .Where(x => x != null)
                .OrderBy(x => x.Round)
                .ToList();
            if (fontes.Count == 0)
                return fontes;
            if (fontes.Count < 2)
                throw new InvalidOperationException("O roteiro precisa possuir ao menos dois cenarios.");
            if (fontes.Count > 10)
                throw new InvalidOperationException("Configure no maximo 10 cenarios por roteiro.");
            if (fontes[0].Round != 1)
                throw new InvalidOperationException("O primeiro cenario precisa comecar no round 1.");

            HashSet<int> rounds = new HashSet<int>();
            foreach (IkemenSceneStageSource fonte in fontes)
            {
                if (fonte.Round < 1 || fonte.Round > 99 || !rounds.Add(fonte.Round))
                    throw new InvalidOperationException("Os rounds do roteiro sao invalidos ou estao repetidos.");
                if (string.IsNullOrWhiteSpace(fonte.Nome) || string.IsNullOrWhiteSpace(fonte.StageReference))
                    throw new InvalidOperationException("Um cenario do roteiro nao possui nome ou arquivo .def.");
                fonte.Transicao = string.Equals(fonte.Transicao, "fade", StringComparison.OrdinalIgnoreCase)
                    ? "fade"
                    : (string.Equals(fonte.Transicao, "queda", StringComparison.OrdinalIgnoreCase) ? "queda" : "corte");
                if (fonte.Round == 1)
                {
                    fonte.Transicao = "corte";
                    fonte.DuracaoFrames = 0;
                }
                else if ((fonte.Transicao == "fade" || fonte.Transicao == "queda")
                    && (fonte.DuracaoFrames < 1 || fonte.DuracaoFrames > 180))
                {
                    throw new InvalidOperationException("A duracao da transicao deve estar entre 1 e 180 frames.");
                }
            }

            return fontes;
        }

        private static ParsedStageSource CarregarFonte(
            string ikemenRoot,
            string stagesRoot,
            IkemenSceneStageSource configuration)
        {
            string stagePath = ResolverStagePath(stagesRoot, configuration.StageReference);
            string stageText = File.ReadAllText(stagePath, StageEncoding);
            string spriteReference = ObterValorDaSecao(stageText, "BGDef", "spr");
            if (string.IsNullOrWhiteSpace(spriteReference))
                throw new InvalidOperationException("O cenario '" + configuration.Nome + "' nao informa o arquivo SFF em [BGDef].");

            string spritePath = ResolverRecurso(ikemenRoot, stagePath, spriteReference);
            if (!File.Exists(spritePath))
                throw new FileNotFoundException("O arquivo SFF do cenario '" + configuration.Nome + "' nao foi encontrado.", spritePath);

            string localCoord = NormalizarValor(ObterValorDaSecao(stageText, "StageInfo", "localcoord")) ?? "320,240";
            return new ParsedStageSource
            {
                Configuration = configuration,
                StagePath = stagePath,
                StageText = stageText,
                SpritePath = spritePath,
                BackgroundBody = ExtrairCorpoAposBgDef(stageText),
                LocalCoord = localCoord,
                LocalCoordHeight = ObterAlturaLocalCoord(localCoord),
                ZOffset = ObterInteiroDaSecao(stageText, "StageInfo", "zoffset", 0)
            };
        }

        private static string ResolverStagePath(string stagesRoot, string stageReference)
        {
            string normalized = (stageReference ?? string.Empty).Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
                throw new InvalidOperationException("O roteiro precisa usar caminhos relativos a pasta stages.");
            if (normalized.StartsWith("stages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(("stages" + Path.DirectorySeparatorChar).Length);

            string path = Path.GetFullPath(Path.Combine(stagesRoot, normalized));
            EnsureInside(stagesRoot, path);
            if (!File.Exists(path))
                throw new FileNotFoundException("Um arquivo .def do roteiro nao foi encontrado.", path);
            return path;
        }

        private static string ResolverRecurso(string ikemenRoot, string stagePath, string resourceReference)
        {
            string normalized = Unquote(resourceReference).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
                throw new InvalidOperationException("O arquivo SFF precisa estar dentro da pasta do IKEMEN.");

            string relativeToStage = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(stagePath), normalized));
            if (File.Exists(relativeToStage))
            {
                EnsureInside(ikemenRoot, relativeToStage);
                return relativeToStage;
            }

            string relativeToRoot = Path.GetFullPath(Path.Combine(ikemenRoot, normalized));
            EnsureInside(ikemenRoot, relativeToRoot);
            return relativeToRoot;
        }

        private static void ValidarCompatibilidade(IList<ParsedStageSource> fontes)
        {
            string localCoord = fontes[0].LocalCoord;
            foreach (ParsedStageSource fonte in fontes.Skip(1))
            {
                if (!string.Equals(localCoord, fonte.LocalCoord, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Os cenarios '" + fontes[0].Configuration.Nome + "' e '" + fonte.Configuration.Nome
                        + "' usam resolucoes internas diferentes e nao podem ser combinados automaticamente.");
                }
            }
        }

        private static string GerarCabecalhoDoStage(string stageText, string mergedSpriteReference)
        {
            MatchCollection matches = Regex.Matches(
                stageText,
                @"(?m)^\s*\[(?<name>[^\]]+)\].*$",
                RegexOptions.CultureInvariant);
            Match bgDef = matches.Cast<Match>()
                .FirstOrDefault(x => x.Groups["name"].Value.Trim().Equals("BGDef", StringComparison.OrdinalIgnoreCase));
            if (bgDef == null)
                throw new InvalidOperationException("O arquivo do cenario nao possui uma secao [BGDef] valida.");

            int bgDefIndex = matches.Cast<Match>().ToList().IndexOf(bgDef);
            int end = bgDefIndex + 1 < matches.Count ? matches[bgDefIndex + 1].Index : stageText.Length;
            string prefix = stageText.Substring(0, bgDef.Index);
            string block = stageText.Substring(bgDef.Index, end - bgDef.Index);
            Regex sprRegex = new Regex(
                @"(?im)^(?<prefix>\s*spr\s*=\s*)(?<value>[^;\r\n]*)(?<suffix>.*)$",
                RegexOptions.CultureInvariant);
            if (sprRegex.IsMatch(block))
                block = sprRegex.Replace(block, "${prefix}" + mergedSpriteReference + "${suffix}", 1);
            else
                block = block.TrimEnd() + Environment.NewLine + "spr = " + mergedSpriteReference + Environment.NewLine;

            Regex debugBgRegex = new Regex(
                @"(?im)^(?<prefix>\s*debugbg\s*=\s*)(?<value>[^;\r\n]*)(?<suffix>.*)$",
                RegexOptions.CultureInvariant);
            if (debugBgRegex.IsMatch(block))
                block = debugBgRegex.Replace(block, "${prefix}0${suffix}", 1);
            else
                block = block.TrimEnd() + Environment.NewLine + "debugbg = 0" + Environment.NewLine;

            return (prefix + block).TrimEnd();
        }

        private static string ExtrairCorpoAposBgDef(string stageText)
        {
            MatchCollection matches = Regex.Matches(
                stageText,
                @"(?m)^\s*\[(?<name>[^\]]+)\].*$",
                RegexOptions.CultureInvariant);
            for (int index = 0; index < matches.Count; index++)
            {
                if (!matches[index].Groups["name"].Value.Trim().Equals("BGDef", StringComparison.OrdinalIgnoreCase))
                    continue;

                int start = index + 1 < matches.Count ? matches[index + 1].Index : stageText.Length;
                return stageText.Substring(start);
            }

            throw new InvalidOperationException("O arquivo do cenario nao possui uma secao [BGDef] valida.");
        }

        private static string TransformarCorpoDoStage(
            string body,
            IDictionary<int, int> groupMap,
            IkemenSceneStageSource configuration,
            int localCoordHeight,
            int zOffset,
            int sceneIndex,
            ref int nextBackgroundId,
            ref int nextActionNumber,
            out RuntimeScene runtimeScene)
        {
            Dictionary<int, int> actionMap = new Dictionary<int, int>();
            foreach (string rawLine in SplitLines(body))
            {
                Match sectionMatch = SectionRegex.Match(RemoveComment(rawLine));
                int oldAction;
                if (sectionMatch.Success
                    && TryParseBeginAction(sectionMatch.Groups["name"].Value, out oldAction)
                    && !actionMap.ContainsKey(oldAction))
                    actionMap[oldAction] = nextActionNumber++;
            }

            List<int> generatedIds = new List<int>();
            List<RuntimeBackground> generatedBackgrounds = new List<RuntimeBackground>();
            Dictionary<int, int> oldBackgroundIds = new Dictionary<int, int>();
            List<string> lines = SplitLines(body).ToList();
            bool backgroundSection = false;
            int backgroundIndex = -1;
            for (int index = 0; index < lines.Count; index++)
            {
                Match sectionMatch = SectionRegex.Match(RemoveComment(lines[index]));
                if (!sectionMatch.Success)
                    continue;

                string sectionName = sectionMatch.Groups["name"].Value.Trim();
                backgroundSection = IsBackgroundSection(sectionName);
                if (!backgroundSection)
                    continue;

                backgroundIndex++;
                int generatedId = nextBackgroundId++;
                generatedIds.Add(generatedId);
                lines[index] = "[BG DreamMover Scene " + (sceneIndex + 1) + " Element " + (backgroundIndex + 1) + "]";

                int search = index + 1;
                bool idInserted = false;
                double? startY = 0d;
                while (search < lines.Count && !SectionRegex.IsMatch(RemoveComment(lines[search])))
                {
                    Match assignment = AssignmentRegex.Match(RemoveComment(lines[search]).Trim());
                    int oldId;
                    if (assignment.Success
                        && assignment.Groups["key"].Value.Trim().Equals("ID", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(assignment.Groups["value"].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out oldId))
                            oldBackgroundIds[oldId] = generatedId;
                        lines[search] = "ID = " + generatedId;
                        idInserted = true;
                    }
                    else if (assignment.Success
                        && assignment.Groups["key"].Value.Trim().Equals("start", StringComparison.OrdinalIgnoreCase))
                        startY = ObterSegundoNumero(assignment.Groups["value"].Value.Trim());
                    search++;
                }

                if (!idInserted)
                {
                    lines.Insert(index + 1, "ID = " + generatedId);
                    index++;
                }

                generatedBackgrounds.Add(new RuntimeBackground
                {
                    Id = generatedId,
                    StartY = startY
                });
            }

            StringBuilder result = new StringBuilder();
            bool actionSection = false;
            bool bgCtrlSection = false;
            bool bgCtrlAnim = false;
            foreach (string originalLine in lines)
            {
                string line = originalLine;
                Match sectionMatch = SectionRegex.Match(RemoveComment(line));
                if (sectionMatch.Success)
                {
                    string sectionName = sectionMatch.Groups["name"].Value.Trim();
                    int oldAction;
                    actionSection = TryParseBeginAction(sectionName, out oldAction);
                    bgCtrlSection = sectionName.StartsWith("BGCtrl", StringComparison.OrdinalIgnoreCase)
                        && !sectionName.StartsWith("BGCtrlDef", StringComparison.OrdinalIgnoreCase);
                    bgCtrlAnim = false;
                    int newAction;
                    if (actionSection && actionMap.TryGetValue(oldAction, out newAction))
                        line = "[Begin Action " + newAction + "]";
                    result.AppendLine(line);
                    continue;
                }

                Match assignmentMatch = AssignmentRegex.Match(RemoveComment(line).Trim());
                if (assignmentMatch.Success)
                {
                    string key = assignmentMatch.Groups["key"].Value.Trim();
                    string value = assignmentMatch.Groups["value"].Value.Trim();
                    if (key.Equals("spriteno", StringComparison.OrdinalIgnoreCase))
                        line = ReescreverParDeSprite(line, groupMap);
                    else if (key.Equals("actionno", StringComparison.OrdinalIgnoreCase))
                        line = ReescreverNumeroDaAtribuicao(line, actionMap);
                    else if (key.Equals("ctrlID", StringComparison.OrdinalIgnoreCase))
                        line = ReescreverListaDeIds(line, oldBackgroundIds);
                    else if (bgCtrlSection && key.Equals("type", StringComparison.OrdinalIgnoreCase))
                        bgCtrlAnim = value.Equals("anim", StringComparison.OrdinalIgnoreCase);
                    else if (bgCtrlSection && bgCtrlAnim && key.Equals("value", StringComparison.OrdinalIgnoreCase))
                        line = ReescreverNumeroDaAtribuicao(line, actionMap);
                }
                else if (actionSection)
                    line = ReescreverLinhaDeAnimacao(line, groupMap);

                result.AppendLine(line);
            }

            if (generatedIds.Count == 0)
                throw new InvalidOperationException("O cenario '" + configuration.Nome + "' nao possui elementos [BG] para combinar.");

            runtimeScene = new RuntimeScene
            {
                Round = configuration.Round,
                Nome = configuration.Nome,
                BackgroundIds = generatedIds,
                Backgrounds = generatedBackgrounds,
                Transicao = configuration.Transicao,
                DuracaoFrames = configuration.DuracaoFrames,
                LocalCoordHeight = localCoordHeight,
                ZOffset = zOffset
            };
            return result.ToString();
        }

        private static void ValidarTransicoesDeQueda(IList<RuntimeScene> cenas)
        {
            for (int index = 1; index < cenas.Count; index++)
            {
                RuntimeScene incoming = cenas[index];
                if (incoming.Transicao != "queda")
                    continue;

                RuntimeScene outgoing = cenas[index - 1];
                if (incoming.LocalCoordHeight <= 0)
                    throw new InvalidOperationException("A resolucao interna do cenario '" + incoming.Nome + "' e invalida para a transicao de queda.");
                if (outgoing.Backgrounds.Concat(incoming.Backgrounds).Any(x => !x.StartY.HasValue))
                {
                    throw new InvalidOperationException(
                        "A transicao de queda entre '" + outgoing.Nome + "' e '" + incoming.Nome
                        + "' exige valores numericos de start nos elementos de fundo.");
                }
            }
        }

        private static bool IsBackgroundSection(string sectionName)
        {
            return sectionName.Equals("BG", StringComparison.OrdinalIgnoreCase)
                || (sectionName.StartsWith("BG ", StringComparison.OrdinalIgnoreCase)
                    && !sectionName.StartsWith("BG Ctrl", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseBeginAction(string sectionName, out int actionNumber)
        {
            actionNumber = 0;
            Match match = Regex.Match(
                sectionName ?? string.Empty,
                @"^Begin\s+Action\s+(?<value>-?\d+)\s*$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            return match.Success
                && int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out actionNumber);
        }

        private static string ReescreverParDeSprite(string line, IDictionary<int, int> groupMap)
        {
            Match match = Regex.Match(
                line,
                @"^(?<prefix>\s*spriteno\s*=\s*)(?<group>-?\d+)(?<separator>\s*,\s*)(?<number>-?\d+)(?<suffix>.*)$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            int group;
            int mapped;
            if (!match.Success
                || !int.TryParse(match.Groups["group"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out group)
                || !groupMap.TryGetValue(group, out mapped))
                return line;

            return match.Groups["prefix"].Value + mapped + match.Groups["separator"].Value
                + match.Groups["number"].Value + match.Groups["suffix"].Value;
        }

        private static string ReescreverLinhaDeAnimacao(string line, IDictionary<int, int> groupMap)
        {
            Match match = Regex.Match(
                line,
                @"^(?<prefix>\s*)(?<group>-?\d+)(?<suffix>\s*,\s*-?\d+\s*,.*)$",
                RegexOptions.CultureInvariant);
            int group;
            int mapped;
            if (!match.Success
                || !int.TryParse(match.Groups["group"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out group)
                || !groupMap.TryGetValue(group, out mapped))
                return line;
            return match.Groups["prefix"].Value + mapped + match.Groups["suffix"].Value;
        }

        private static string ReescreverNumeroDaAtribuicao(string line, IDictionary<int, int> map)
        {
            Match match = Regex.Match(
                line,
                @"^(?<prefix>\s*[^=]+?\s*=\s*)(?<value>-?\d+)(?<suffix>.*)$",
                RegexOptions.CultureInvariant);
            int value;
            int mapped;
            if (!match.Success
                || !int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                || !map.TryGetValue(value, out mapped))
                return line;
            return match.Groups["prefix"].Value + mapped + match.Groups["suffix"].Value;
        }

        private static string ReescreverListaDeIds(string line, IDictionary<int, int> idMap)
        {
            Match match = Regex.Match(
                line,
                @"^(?<prefix>\s*ctrlID\s*=\s*)(?<values>[^;]*)(?<suffix>.*)$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return line;

            string[] values = match.Groups["values"].Value.Split(',');
            for (int index = 0; index < values.Length; index++)
            {
                int oldId;
                int newId;
                if (int.TryParse(values[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out oldId)
                    && idMap.TryGetValue(oldId, out newId))
                    values[index] = " " + newId;
            }

            return match.Groups["prefix"].Value + string.Join(",", values).TrimStart() + match.Groups["suffix"].Value;
        }

        private static List<Dictionary<int, int>> MesclarSffV1(IList<string> sourcePaths, string destinationPath)
        {
            List<SffV1File> sources = sourcePaths.Select(CarregarSffV1).ToList();
            int totalSprites = sources.Sum(x => x.Sprites.Count);
            if (totalSprites == 0)
                throw new InvalidOperationException("Os arquivos SFF selecionados nao possuem sprites.");
            if (totalSprites > ushort.MaxValue)
                throw new InvalidOperationException("Os cenarios possuem sprites demais para uma combinacao SFF v1.");

            List<Dictionary<int, int>> groupMaps = new List<Dictionary<int, int>>();
            int nextGroup = 0;
            foreach (SffV1File source in sources)
            {
                Dictionary<int, int> map = new Dictionary<int, int>();
                foreach (int group in source.Sprites.Select(x => (int)x.Group).Distinct().OrderBy(x => x))
                {
                    if (nextGroup > ushort.MaxValue)
                        throw new InvalidOperationException("Os cenarios possuem grupos de sprites demais para combinar.");
                    map[group] = nextGroup++;
                }
                groupMaps.Add(map);
            }

            int headerLength = sources[0].FirstSpriteOffset;
            byte[] header = new byte[headerLength];
            Buffer.BlockCopy(sources[0].Bytes, 0, header, 0, headerLength);
            WriteUInt32(header, 16, (uint)nextGroup);
            WriteUInt32(header, 20, (uint)totalSprites);
            WriteUInt32(header, 24, (uint)headerLength);
            WriteUInt32(header, 28, 32);

            using (MemoryStream output = new MemoryStream())
            {
                output.Write(header, 0, header.Length);
                int sourceSpriteBase = 0;
                int globalSpriteIndex = 0;
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    SffV1File source = sources[sourceIndex];
                    for (int spriteIndex = 0; spriteIndex < source.Sprites.Count; spriteIndex++)
                    {
                        SffV1Sprite sprite = source.Sprites[spriteIndex];
                        if (sourceIndex > 0 && spriteIndex == 0 && sprite.PaletteSame)
                        {
                            throw new InvalidOperationException(
                                "O arquivo '" + Path.GetFileName(source.Path)
                                + "' inicia com uma paleta compartilhada e nao pode ser combinado automaticamente.");
                        }

                        byte[] subheader = (byte[])sprite.Subheader.Clone();
                        long nextOffset = globalSpriteIndex == totalSprites - 1
                            ? 0
                            : output.Position + 32 + sprite.Data.Length;
                        WriteUInt32(subheader, 0, checked((uint)nextOffset));
                        WriteUInt32(subheader, 4, checked((uint)sprite.Data.Length));
                        WriteUInt16(subheader, 12, checked((ushort)groupMaps[sourceIndex][sprite.Group]));
                        if (sprite.Data.Length == 0)
                        {
                            if (sprite.LinkedSpriteIndex >= source.Sprites.Count)
                                throw new InvalidOperationException("Um sprite compartilhado do arquivo SFF aponta para uma posicao invalida.");
                            WriteUInt16(subheader, 16, checked((ushort)(sourceSpriteBase + sprite.LinkedSpriteIndex)));
                        }

                        output.Write(subheader, 0, subheader.Length);
                        output.Write(sprite.Data, 0, sprite.Data.Length);
                        globalSpriteIndex++;
                    }
                    sourceSpriteBase += source.Sprites.Count;
                }

                File.WriteAllBytes(destinationPath, output.ToArray());
            }

            return groupMaps;
        }

        private static SffV1File CarregarSffV1(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 512 || Encoding.ASCII.GetString(bytes, 0, 12) != "ElecbyteSpr\0")
                throw new InvalidOperationException("O arquivo '" + Path.GetFileName(path) + "' nao e um SFF valido.");
            if (bytes[15] != 1)
            {
                throw new InvalidOperationException(
                    "O cenario '" + Path.GetFileNameWithoutExtension(path)
                    + "' usa SFF v" + bytes[15] + ". A combinacao automatica atualmente aceita SFF v1.");
            }

            uint spriteCountValue = ReadUInt32(bytes, 20);
            uint firstOffsetValue = ReadUInt32(bytes, 24);
            if (spriteCountValue > ushort.MaxValue || firstOffsetValue < 32 || firstOffsetValue > bytes.Length)
                throw new InvalidOperationException("O cabecalho do arquivo SFF e invalido.");

            int spriteCount = (int)spriteCountValue;
            int offset = (int)firstOffsetValue;
            List<SffV1Sprite> sprites = new List<SffV1Sprite>();
            for (int index = 0; index < spriteCount; index++)
            {
                if (offset < 0 || offset + 32 > bytes.Length)
                    throw new InvalidOperationException("Um cabecalho de sprite do arquivo SFF esta incompleto.");

                uint nextOffsetValue = ReadUInt32(bytes, offset);
                uint dataLengthValue = ReadUInt32(bytes, offset + 4);
                if (dataLengthValue > int.MaxValue || offset + 32L + dataLengthValue > bytes.Length)
                    throw new InvalidOperationException("Os dados de um sprite do arquivo SFF estao incompletos.");

                int dataLength = (int)dataLengthValue;
                byte[] subheader = new byte[32];
                Buffer.BlockCopy(bytes, offset, subheader, 0, 32);
                byte[] data = new byte[dataLength];
                if (dataLength > 0)
                    Buffer.BlockCopy(bytes, offset + 32, data, 0, dataLength);

                sprites.Add(new SffV1Sprite
                {
                    Subheader = subheader,
                    Data = data,
                    Group = ReadUInt16(bytes, offset + 12),
                    LinkedSpriteIndex = ReadUInt16(bytes, offset + 16),
                    PaletteSame = bytes[offset + 18] != 0
                });

                if (index < spriteCount - 1)
                {
                    if (nextOffsetValue <= offset || nextOffsetValue > bytes.Length)
                        throw new InvalidOperationException("A sequencia de sprites do arquivo SFF e invalida.");
                    offset = (int)nextOffsetValue;
                }
            }

            return new SffV1File
            {
                Path = path,
                Bytes = bytes,
                FirstSpriteOffset = (int)firstOffsetValue,
                Sprites = sprites
            };
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                | bytes[offset + 1] << 8
                | bytes[offset + 2] << 16
                | bytes[offset + 3] << 24);
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        private static string ObterValorDaSecao(string text, string sectionName, string keyName)
        {
            bool inSection = false;
            foreach (string rawLine in SplitLines(text))
            {
                string clean = RemoveComment(rawLine).Trim();
                Match sectionMatch = SectionRegex.Match(clean);
                if (sectionMatch.Success)
                {
                    inSection = sectionMatch.Groups["name"].Value.Trim().Equals(sectionName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection)
                    continue;

                Match assignment = AssignmentRegex.Match(clean);
                if (assignment.Success && assignment.Groups["key"].Value.Trim().Equals(keyName, StringComparison.OrdinalIgnoreCase))
                    return Unquote(assignment.Groups["value"].Value.Trim());
            }
            return null;
        }

        private static int ObterInteiroDaSecao(string text, string sectionName, string keyName, int defaultValue)
        {
            int value;
            return int.TryParse(
                ObterValorDaSecao(text, sectionName, keyName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
                ? value
                : defaultValue;
        }

        private static int ObterAlturaLocalCoord(string localCoord)
        {
            double? height = ObterSegundoNumero(localCoord);
            if (!height.HasValue || height.Value <= 0 || height.Value > int.MaxValue)
                throw new InvalidOperationException("A resolucao interna do cenario e invalida.");
            return checked((int)Math.Round(height.Value, MidpointRounding.AwayFromZero));
        }

        private static double? ObterSegundoNumero(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            double number;
            if (parts.Length < 2
                || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.IsNaN(number)
                || double.IsInfinity(number))
                return null;
            return number;
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value) < 0.0000001d)
                value = 0d;
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string NormalizarValor(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : Regex.Replace(value, @"\s+", string.Empty).ToLowerInvariant();
        }

        private static string Unquote(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length >= 2 && normalized[0] == '"' && normalized[normalized.Length - 1] == '"')
                return normalized.Substring(1, normalized.Length - 2).Trim();
            return normalized;
        }

        private static string ObterSlotAttachedChar(string stageText)
        {
            HashSet<int> usados = new HashSet<int>();
            foreach (string rawLine in SplitLines(stageText))
            {
                Match match = AttachedCharRegex.Match(RemoveComment(rawLine));
                if (!match.Success)
                    continue;

                int index;
                usados.Add(int.TryParse(match.Groups["index"].Value, out index) ? index : 1);
            }

            for (int index = 1; index <= 4; index++)
            {
                if (!usados.Contains(index))
                    return index == 1 ? "attachedChar" : "attachedChar" + index;
            }

            throw new InvalidOperationException("O primeiro cenario ja utiliza os quatro slots de AttachedChar disponiveis.");
        }

        private static HashSet<int> ObterSctrlIds(string stageText)
        {
            HashSet<int> ids = new HashSet<int>();
            foreach (string rawLine in SplitLines(stageText))
            {
                Match match = SctrlIdRegex.Match(RemoveComment(rawLine));
                int value;
                if (match.Success && int.TryParse(match.Groups["value"].Value, out value))
                    ids.Add(value);
            }

            return ids;
        }

        private static List<int> ReservarControllerIds(HashSet<int> existing, int count)
        {
            List<int> result = new List<int>();
            int candidate = FirstControllerId;
            while (result.Count < count)
            {
                if (!existing.Contains(candidate))
                    result.Add(candidate);
                candidate++;
            }

            return result;
        }

        private static string AdicionarAttachedChar(string stageText, string key, string controllerReference)
        {
            Regex infoRegex = new Regex(
                @"^\s*\[Info\]\s*(?:;.*)?$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
            Match match = infoRegex.Match(stageText);
            if (!match.Success)
                throw new InvalidOperationException("O arquivo do cenario nao possui uma secao [Info] valida.");

            return stageText.Insert(
                match.Index + match.Length,
                Environment.NewLine + key + " = " + controllerReference + " ; gerado pelo DreamMover");
        }

        private static string GerarBackgroundControllers(
            IList<RuntimeScene> cenas,
            IList<int> controllerIds)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("; ==========================================================");
            builder.AppendLine("; Controladores temporarios gerados pelo DreamMover");
            for (int index = 0; index < cenas.Count; index++)
            {
                builder.AppendLine("[BGCtrlDef DreamMover Scene " + (index + 1) + "]");
                builder.AppendLine("looptime = -1");
                builder.AppendLine("ctrlID = " + string.Join(", ", cenas[index].BackgroundIds));
                builder.AppendLine("[BGCtrl DreamMover Scene " + (index + 1) + "]");
                builder.AppendLine("type = Enable");
                builder.AppendLine("time = 0, 999999, -1");
                builder.AppendLine("value = 1");
                builder.AppendLine("sctrlID = " + controllerIds[index]);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string GerarControllerDef(string stageText)
        {
            Match localCoord = Regex.Match(
                stageText,
                @"^\s*localcoord\s*=\s*(?<value>\d+\s*,\s*\d+)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
            string value = localCoord.Success ? localCoord.Groups["value"].Value : "320, 240";

            return "[Info]" + Environment.NewLine
                + "name = DreamMover Scene Controller" + Environment.NewLine
                + "author = DreamMover" + Environment.NewLine
                + "ikemenversion = 1.0" + Environment.NewLine
                + "localcoord = " + value + Environment.NewLine + Environment.NewLine
                + "[Files]" + Environment.NewLine
                + "st = controller.zss" + Environment.NewLine;
        }

        private static string GerarControllerZss(
            IList<RuntimeScene> cenas,
            IList<int> controllerIds)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Controlador temporario de cenas gerado pelo DreamMover");
            builder.AppendLine("[StateDef 5900;");
            builder.AppendLine("anim: -2;]");
            builder.AppendLine("changeState{value: 0}");
            builder.AppendLine();
            builder.AppendLine("[StateDef 0;");
            builder.AppendLine("anim: -2;]");
            builder.AppendLine();
            builder.AppendLine("[StateDef 175;");
            builder.AppendLine("anim: -2;]");
            builder.AppendLine();
            builder.AppendLine("[StateDef 200;");
            builder.AppendLine("type: S;");
            builder.AppendLine("movetype: I;");
            builder.AppendLine("physics: N;");
            builder.AppendLine("ctrl: 0;");
            builder.AppendLine("anim: -2;");
            builder.AppendLine("velset: 0, 0;]");
            builder.AppendLine("assertSpecial{flag: nohardcodedkeys; flag2: noautoturn}");
            builder.AppendLine("playerPush{value: 0}");
            builder.AppendLine("notHitBy{value: SCA}");
            builder.AppendLine("if map(DreamMoverRound) != roundNo {");
            builder.AppendLine("    map(DreamMoverRound) := roundNo;");
            builder.AppendLine("    map(DreamMoverFadeElapsed) := 0;");
            builder.AppendLine("    map(DreamMoverVictoryElapsed) := 0;");
            builder.AppendLine("    map(DreamMoverDropElapsed) := 0;");
            builder.AppendLine("    map(DreamMoverBlackElapsed) := 0;");
            builder.AppendLine("}");
            builder.AppendLine();

            for (int activeIndex = 0; activeIndex < cenas.Count; activeIndex++)
            {
                int startRound = cenas[activeIndex].Round;
                string condition;
                if (activeIndex == 0 && cenas.Count > 1)
                    condition = "roundNo < " + cenas[activeIndex + 1].Round;
                else if (activeIndex < cenas.Count - 1)
                    condition = "roundNo >= " + startRound + " && roundNo < " + cenas[activeIndex + 1].Round;
                else
                    condition = "roundNo >= " + startRound;

                builder.AppendLine("if " + condition + " {");
                for (int controllerIndex = 0; controllerIndex < controllerIds.Count; controllerIndex++)
                    builder.AppendLine("    modifyBGCtrl{ID: " + controllerIds[controllerIndex] + "; value: " + (controllerIndex == activeIndex ? 1 : 0) + "}");

                RuntimeScene activeScene = cenas[activeIndex];
                AdicionarPosicoesBase(builder, activeScene, "    ");
                if (activeScene.Transicao == "fade" && activeScene.DuracaoFrames > 0)
                {
                    builder.AppendLine("    if map(DreamMoverFadeElapsed) < " + activeScene.DuracaoFrames + " {");
                    builder.AppendLine("        map(DreamMoverFadeElapsed) := map(DreamMoverFadeElapsed) + 1;");
                    builder.AppendLine("        let dreamMoverFade = map(DreamMoverFadeElapsed) * 256 / " + activeScene.DuracaoFrames + ";");
                    foreach (int backgroundId in activeScene.BackgroundIds)
                        builder.AppendLine("        bgPalFX{ID: " + backgroundId + "; time: 2; mul: $dreamMoverFade, $dreamMoverFade, $dreamMoverFade}");
                    builder.AppendLine("    }");
                }

                builder.AppendLine("}");
                builder.AppendLine();
            }

            AdicionarTransicoesDeQueda(builder, cenas, controllerIds);

            if (cenas.Any(x => x.Transicao == "queda"))
                AdicionarEstadoDeQuedaDosJogadores(builder);

            builder.AppendLine("[StateDef -4]");
            builder.AppendLine("if isHelper = 0 {");
            builder.AppendLine("    if numHelper(" + HelperId + ") = 0 {");
            builder.AppendLine("        helper{");
            builder.AppendLine("            helpertype: normal;");
            builder.AppendLine("            name: \"DreamMover Scene Runtime\";");
            builder.AppendLine("            ID: " + HelperId + ";");
            builder.AppendLine("            stateno: 200;");
            builder.AppendLine("            keyctrl: 0;");
            builder.AppendLine("            preserve: 1;");
            builder.AppendLine("            standby: 0;");
            builder.AppendLine("            immortal: 1;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("    assertSpecial{flag: nohardcodedkeys; flag2: noautoturn}");
            builder.AppendLine("    tagout{}");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AdicionarPosicoesBase(StringBuilder builder, RuntimeScene scene, string indent)
        {
            foreach (RuntimeBackground background in scene.Backgrounds.Where(x => x.StartY.HasValue))
            {
                builder.AppendLine(indent + "modifyStageBG{ID: " + background.Id
                    + "; start.y: " + FormatNumber(background.StartY.Value + scene.BaseOffsetY) + "}");
            }
        }

        private static void AdicionarTransicoesDeQueda(
            StringBuilder builder,
            IList<RuntimeScene> cenas,
            IList<int> controllerIds)
        {
            for (int index = 1; index < cenas.Count; index++)
            {
                RuntimeScene incoming = cenas[index];
                if (incoming.Transicao != "queda")
                    continue;

                RuntimeScene outgoing = cenas[index - 1];
                int duration = incoming.DuracaoFrames;
                int distance = incoming.LocalCoordHeight;
                string offsetVariable = "dreamMoverDropOffset" + (index + 1);
                string jumpOffsetVariable = "dreamMoverJumpOffset" + (index + 1);
                string jumpHeightVariable = "dreamMoverJumpHeight" + (index + 1);
                string jumpHalfVariable = "dreamMoverJumpHalf" + (index + 1);
                string jumpStartVariable = "dreamMoverJumpStart" + (index + 1);

                builder.AppendLine("if roundNo = " + (incoming.Round - 1) + " && roundState = 4 && !matchOver {");
                builder.AppendLine("    map(DreamMoverVictoryElapsed) := map(DreamMoverVictoryElapsed) + 1;");
                builder.AppendLine("    let " + jumpStartVariable + " = max(fightScreenVar(round.over.wintime), "
                    + ResultAnnouncementMinimumFrames + ");");
                for (int controllerIndex = 0; controllerIndex < controllerIds.Count; controllerIndex++)
                {
                    string value = controllerIndex == index - 1
                        ? "map(DreamMoverDropElapsed) < " + duration
                        : "0";
                    builder.AppendLine("    modifyBGCtrl{ID: " + controllerIds[controllerIndex]
                        + "; value: " + value + "}");
                }
                builder.AppendLine("    if map(DreamMoverBlackElapsed) < " + BlackoutDurationFrames + " {");
                builder.AppendLine("        assertSpecial{flag: roundnotover; flag2: roundnotskip}");
                builder.AppendLine("    }");

                builder.AppendLine("    if map(DreamMoverVictoryElapsed) < $" + jumpStartVariable + " {");
                builder.AppendLine("        targetDrop{}");
                AdicionarLacoDosJogadores(builder, "        ", delegate(string playerId, string indent)
                {
                    builder.AppendLine(indent + "targetAdd{playerid: " + playerId + "}");
                }, "win", 203);
                builder.AppendLine("        targetState{value: 203}");
                builder.AppendLine("    }");

                builder.AppendLine("    if map(DreamMoverVictoryElapsed) >= $" + jumpStartVariable
                    + " && map(DreamMoverDropElapsed) = 0 {");
                builder.AppendLine("        targetDrop{}");
                AdicionarLacoDosJogadores(builder, "        ", delegate(string playerId, string indent)
                {
                    builder.AppendLine(indent + "targetAdd{playerid: " + playerId + "}");
                }, "win");
                builder.AppendLine("        targetState{value: 201}");
                builder.AppendLine("    }");

                builder.AppendLine("    if map(DreamMoverVictoryElapsed) >= $" + jumpStartVariable
                    + " && map(DreamMoverDropElapsed) < " + duration + " {");
                builder.AppendLine("        map(DreamMoverDropElapsed) := map(DreamMoverDropElapsed) + 1;");
                builder.AppendLine("    }");
                builder.AppendLine("    if map(DreamMoverDropElapsed) >= " + duration
                    + " && map(DreamMoverBlackElapsed) < " + BlackoutDurationFrames + " {");
                builder.AppendLine("        map(DreamMoverBlackElapsed) := map(DreamMoverBlackElapsed) + 1;");
                builder.AppendLine("    }");
                builder.AppendLine("    let " + offsetVariable + " = min(map(DreamMoverDropElapsed), " + duration
                    + ") * " + distance + " / " + duration + ";");
                builder.AppendLine("    let " + jumpHalfVariable + " = max(1, " + duration + " / 2);");
                builder.AppendLine("    let " + jumpHeightVariable + " = " + distance + " / 2;");
                builder.AppendLine("    let " + jumpOffsetVariable + " = ifElse(map(DreamMoverDropElapsed) <= $"
                    + jumpHalfVariable + ", -map(DreamMoverDropElapsed) * $" + jumpHeightVariable + " / $"
                    + jumpHalfVariable + ", -$" + jumpHeightVariable + " + (map(DreamMoverDropElapsed) - $"
                    + jumpHalfVariable + ") * $" + jumpHeightVariable + " / max(1, " + duration + " - $"
                    + jumpHalfVariable + "));");

                AdicionarLacoDosJogadores(builder, "    ", delegate(string playerId, string indent)
                {
                    builder.AppendLine(indent + "offset{y: $" + offsetVariable + "; redirectID: " + playerId + "}");
                }, "lose");
                AdicionarLacoDosJogadores(builder, "    ", delegate(string playerId, string indent)
                {
                    builder.AppendLine(indent + "offset{y: $" + jumpOffsetVariable + "; redirectID: " + playerId + "}");
                }, "win");

                foreach (RuntimeBackground background in outgoing.Backgrounds)
                {
                    double start = background.StartY.Value + outgoing.BaseOffsetY;
                    builder.AppendLine("    modifyStageBG{ID: " + background.Id + "; start.y: "
                        + FormatNumber(start) + " - $" + offsetVariable + "}");
                }
                builder.AppendLine("    if map(DreamMoverDropElapsed) >= " + duration + " {");
                builder.AppendLine("        assertSpecial{flag: nobardisplay; flag2: noBG; flag3: noFG}");
                builder.AppendLine("        envColor{value: 0, 0, 0; time: 2; under: 1}");
                builder.AppendLine("        allPalFX{time: 2; mul: 0, 0, 0}");
                builder.AppendLine("    }");
                builder.AppendLine("}");
                builder.AppendLine();
            }
        }

        private static void AdicionarLacoDosJogadores(
            StringBuilder builder,
            string indent,
            Action<string, string> adicionarComando,
            string resultado = null,
            int? estadoIgnorado = null)
        {
            const string playerIndex = "dreamMoverPlayer";
            string playerId = "player($" + playerIndex + "), ID";
            builder.AppendLine(indent + "for " + playerIndex + " = 1; 8; 1 {");
            builder.AppendLine(indent + "    if playerNoExist($" + playerIndex + ")");
            builder.AppendLine(indent + "    && player($" + playerIndex + "), isHelper = 0");
            builder.AppendLine(indent + "    && player($" + playerIndex + "), standby = 0");
            builder.AppendLine(indent + "    && player($" + playerIndex + "), teamSide > 0");
            if (!string.IsNullOrWhiteSpace(resultado))
                builder.AppendLine(indent + "    && player($" + playerIndex + "), " + resultado + " = 1");
            if (estadoIgnorado.HasValue)
                builder.AppendLine(indent + "    && player($" + playerIndex + "), stateNo != " + estadoIgnorado.Value);
            builder.AppendLine(indent + "    {");
            adicionarComando(playerId, indent + "        ");
            builder.AppendLine(indent + "    }");
            builder.AppendLine(indent + "}");
        }

        private static void AdicionarEstadoDeQuedaDosJogadores(StringBuilder builder)
        {
            builder.AppendLine("[StateDef 201;");
            builder.AppendLine("type: A;");
            builder.AppendLine("movetype: I;");
            builder.AppendLine("physics: N;");
            builder.AppendLine("ctrl: 0;");
            builder.AppendLine("anim: -1;");
            builder.AppendLine("velset: 0, 0;]");
            builder.AppendLine("assertSpecial{flag: noautoturn; flag2: noinput}");
            builder.AppendLine("playerPush{value: 0}");
            builder.AppendLine("notHitBy{value: SCA}");
            builder.AppendLine("screenBound{value: 0; movecamera: 0, 0; stagebound: 0}");
            builder.AppendLine("if time = 0 {");
            builder.AppendLine("    changeAnim{value: ifElse(selfAnimExist(40), 40, ifElse(selfAnimExist(41), 41, 0))}");
            builder.AppendLine("}");
            builder.AppendLine("if time = 6 {");
            builder.AppendLine("    changeAnim{value: ifElse(selfAnimExist(41), 41, 0)}");
            builder.AppendLine("}");
            builder.AppendLine("if time = 30 {");
            builder.AppendLine("    changeAnim{value: ifElse(selfAnimExist(44), 44, ifElse(selfAnimExist(41), 41, 0))}");
            builder.AppendLine("}");
            builder.AppendLine();

            builder.AppendLine("[StateDef 203;");
            builder.AppendLine("type: S;");
            builder.AppendLine("movetype: I;");
            builder.AppendLine("physics: N;");
            builder.AppendLine("ctrl: 0;");
            builder.AppendLine("anim: -1;");
            builder.AppendLine("velset: 0, 0;]");
            builder.AppendLine("assertSpecial{flag: noautoturn; flag2: noinput}");
            builder.AppendLine("playerPush{value: 0}");
            builder.AppendLine("notHitBy{value: SCA}");
            builder.AppendLine("if time = 0 {");
            builder.AppendLine("    changeAnim{value: 0}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static IEnumerable<string> SplitLines(string value)
        {
            return Regex.Split(value ?? string.Empty, "\\r?\\n");
        }

        private static string RemoveComment(string value)
        {
            int index = (value ?? string.Empty).IndexOf(';');
            return index >= 0 ? value.Substring(0, index) : value ?? string.Empty;
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(fullPath);
            EnsureInside(rootPath, target);
            return target.Substring(root.Length);
        }

        private static void EnsureInside(string rootPath, string fullPath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(fullPath);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O caminho do cenario saiu da pasta configurada do IKEMEN.");
        }

        private sealed class ParsedStageSource
        {
            public IkemenSceneStageSource Configuration { get; set; }
            public string StagePath { get; set; }
            public string StageText { get; set; }
            public string SpritePath { get; set; }
            public string BackgroundBody { get; set; }
            public string LocalCoord { get; set; }
            public int LocalCoordHeight { get; set; }
            public int ZOffset { get; set; }
        }

        private sealed class RuntimeScene
        {
            public int Round { get; set; }
            public string Nome { get; set; }
            public List<int> BackgroundIds { get; set; }
            public List<RuntimeBackground> Backgrounds { get; set; }
            public string Transicao { get; set; }
            public int DuracaoFrames { get; set; }
            public int LocalCoordHeight { get; set; }
            public int ZOffset { get; set; }
            public int BaseOffsetY { get; set; }
        }

        private sealed class RuntimeBackground
        {
            public int Id { get; set; }
            public double? StartY { get; set; }
        }

        private sealed class SffV1File
        {
            public string Path { get; set; }
            public byte[] Bytes { get; set; }
            public int FirstSpriteOffset { get; set; }
            public List<SffV1Sprite> Sprites { get; set; }
        }

        private sealed class SffV1Sprite
        {
            public byte[] Subheader { get; set; }
            public byte[] Data { get; set; }
            public ushort Group { get; set; }
            public ushort LinkedSpriteIndex { get; set; }
            public bool PaletteSame { get; set; }
        }
    }
}
