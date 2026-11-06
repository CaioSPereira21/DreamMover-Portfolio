using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DreamMover.Services
{
    public static class MugenCharacterSecurityService
    {
        private static readonly CharacterAttributeReference[] References =
        {
            new CharacterAttributeReference("life", "Life", "data", 1000d, true, true),
            new CharacterAttributeReference("attack", "Attack", "data", 100d, true, true),
            new CharacterAttributeReference("defence", "Defense", "data", 100d, true, true),
            new CharacterAttributeReference("xscale", "X Scale", "size", 1d, true, false),
            new CharacterAttributeReference("yscale", "Y Scale", "size", 1d, true, false),
            new CharacterAttributeReference("power", "Power", "data", 3000d, false, true)
        };

        public static MugenCharacterSecurityInspection Inspect(string defPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
                    return Fail("Arquivo DEF do personagem nao encontrado.");

                string cnsPath;
                string resolveError;
                if (!TryResolveCnsPath(defPath, out cnsPath, out resolveError))
                    return Fail(resolveError);

                bool dataSectionFound;
                IDictionary<string, string> values = ReadAttributeValues(cnsPath, out dataSectionFound);
                if (!dataSectionFound)
                    return Fail("A secao [Data] nao foi encontrada no CNS principal.", Path.GetFileName(cnsPath));

                var attributes = new List<MugenCharacterAttributeInspection>();
                bool hasHigherAttribute = false;
                bool hasInvalidValue = false;

                foreach (CharacterAttributeReference reference in References)
                {
                    string rawValue;
                    bool informed = values.TryGetValue(reference.Key, out rawValue);
                    double parsedValue = 0d;
                    bool valid = !informed || double.TryParse(
                        rawValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out parsedValue);
                    if (valid && informed && (double.IsNaN(parsedValue) || double.IsInfinity(parsedValue)))
                        valid = false;

                    double? effectiveValue = informed && valid
                        ? (double?)parsedValue
                        : (!informed ? (double?)reference.StandardValue : null);
                    bool higherThanStandard = valid
                        && reference.HasStandard
                        && effectiveValue.HasValue
                        && effectiveValue.Value > reference.StandardValue;

                    hasHigherAttribute = hasHigherAttribute || higherThanStandard;
                    hasInvalidValue = hasInvalidValue || !valid;
                    attributes.Add(new MugenCharacterAttributeInspection
                    {
                        Key = reference.Key,
                        Name = reference.Name,
                        Editable = reference.Editable,
                        HasStandard = reference.HasStandard,
                        Value = effectiveValue,
                        RawValue = informed ? rawValue : null,
                        StandardValue = reference.StandardValue,
                        Informed = informed,
                        Valid = valid,
                        HigherThanStandard = higherThanStandard
                    });
                }

                string message;
                if (hasInvalidValue)
                    message = "Um ou mais atributos nao possuem um valor numerico valido; a verificacao ficou inconclusiva.";
                else if (hasHigherAttribute)
                    message = "Foram detectados atributos acima dos limites padrao do MUGEN.";
                else
                    message = "Life, Power, Attack e Defense estao dentro dos limites padrao do MUGEN.";

                return new MugenCharacterSecurityInspection
                {
                    Success = true,
                    CnsFile = Path.GetFileName(cnsPath),
                    Reliable = !hasInvalidValue,
                    Safe = !hasInvalidValue && !hasHigherAttribute,
                    HasHigherAttribute = hasHigherAttribute,
                    HasInvalidValue = hasInvalidValue,
                    Message = message,
                    Attributes = attributes
                };
            }
            catch (Exception ex)
            {
                if (ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is ArgumentException
                    || ex is NotSupportedException)
                {
                    return Fail("Nao foi possivel ler os atributos do personagem. " + ex.Message);
                }

                throw;
            }
        }

        public static void UpdateEditableAttributes(string defPath, IDictionary<string, double> values)
        {
            if (values == null || values.Count == 0)
                return;

            string cnsPath;
            string resolveError;
            if (!TryResolveCnsPath(defPath, out cnsPath, out resolveError))
                throw new InvalidOperationException(resolveError);

            var normalizedValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> item in values)
            {
                string normalizedKey = NormalizeKey(item.Key);
                CharacterAttributeReference reference = References.FirstOrDefault(x =>
                    x.Editable && string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
                if (reference == null)
                    throw new ArgumentException("O atributo " + item.Key + " nao pode ser editado.", "values");

                if (double.IsNaN(item.Value) || double.IsInfinity(item.Value) || item.Value <= 0d)
                    throw new ArgumentException("O atributo " + reference.Name + " deve possuir um valor numerico maior que zero.", "values");

                normalizedValues[reference.Key] = item.Value;
            }

            var lines = File.ReadAllLines(cnsPath, Encoding.Default).ToList();
            foreach (IGrouping<string, CharacterAttributeReference> sectionReferences in References
                .Where(x => x.Editable && normalizedValues.ContainsKey(x.Key))
                .GroupBy(x => x.Section))
            {
                UpdateSectionValues(lines, sectionReferences.Key, sectionReferences, normalizedValues);
            }

            string temporaryPath = cnsPath + ".dreammover-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, lines, Encoding.Default);
                File.Replace(temporaryPath, cnsPath, null);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string ReadCnsReference(string defPath)
        {
            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(defPath, Encoding.Default))
            {
                string line = RemoveComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                if (section != "files")
                    continue;

                string key;
                string value;
                if (TrySplitAssignment(line, out key, out value)
                    && string.Equals(key, "cns", StringComparison.OrdinalIgnoreCase))
                {
                    return CleanValue(value);
                }
            }

            return null;
        }

        private static IDictionary<string, string> ReadAttributeValues(string cnsPath, out bool dataSectionFound)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            dataSectionFound = false;

            foreach (string rawLine in File.ReadAllLines(cnsPath, Encoding.Default))
            {
                string line = RemoveComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    if (section == "data")
                        dataSectionFound = true;
                    continue;
                }

                string key;
                string value;
                if (!TrySplitAssignment(line, out key, out value))
                    continue;

                string normalizedKey = NormalizeKey(key);
                CharacterAttributeReference reference = References.FirstOrDefault(x =>
                    string.Equals(x.Section, section, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
                if (reference != null)
                    values[normalizedKey] = CleanValue(value);
            }

            return values;
        }

        private static bool TryResolveCnsPath(string defPath, out string cnsPath, out string error)
        {
            cnsPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
            {
                error = "Arquivo DEF do personagem nao encontrado.";
                return false;
            }

            string cnsReference = ReadCnsReference(defPath);
            if (string.IsNullOrWhiteSpace(cnsReference))
            {
                error = "O arquivo DEF nao informa o CNS principal na secao [Files].";
                return false;
            }

            string characterFolder = Path.GetDirectoryName(Path.GetFullPath(defPath));
            if (!TryResolveFileInsideCharacterFolder(characterFolder, cnsReference, out cnsPath))
            {
                error = "O CNS principal aponta para fora da pasta do personagem.";
                return false;
            }

            if (!File.Exists(cnsPath))
            {
                error = "O arquivo CNS principal informado no DEF nao foi encontrado.";
                return false;
            }

            return true;
        }

        private static void UpdateSectionValues(
            IList<string> lines,
            string targetSection,
            IEnumerable<CharacterAttributeReference> references,
            IDictionary<string, double> values)
        {
            int sectionStart = -1;
            int sectionEnd = lines.Count;
            string section = string.Empty;

            for (int index = 0; index < lines.Count; index++)
            {
                string cleanLine = RemoveComment(lines[index]).Trim();
                if (!cleanLine.StartsWith("[") || !cleanLine.EndsWith("]"))
                    continue;

                section = cleanLine.Substring(1, cleanLine.Length - 2).Trim().ToLowerInvariant();
                if (sectionStart >= 0)
                {
                    sectionEnd = index;
                    break;
                }

                if (section == targetSection)
                    sectionStart = index;
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.Add(string.Empty);

                lines.Add("[" + ToSectionDisplayName(targetSection) + "]");
                sectionStart = lines.Count - 1;
                sectionEnd = lines.Count;
            }

            foreach (CharacterAttributeReference reference in references)
            {
                string formattedValue = values[reference.Key].ToString("0.################", CultureInfo.InvariantCulture);
                bool updated = false;
                for (int index = sectionStart + 1; index < sectionEnd; index++)
                {
                    string cleanLine = RemoveComment(lines[index]).Trim();
                    string key;
                    string currentValue;
                    if (!TrySplitAssignment(cleanLine, out key, out currentValue)
                        || !string.Equals(NormalizeKey(key), reference.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lines[index] = ReplaceAssignmentValue(lines[index], formattedValue);
                    updated = true;
                }

                if (!updated)
                {
                    lines.Insert(sectionEnd, reference.Key + " = " + formattedValue);
                    sectionEnd++;
                }
            }
        }

        private static string ReplaceAssignmentValue(string rawLine, string newValue)
        {
            int equalIndex = rawLine.IndexOf('=');
            if (equalIndex < 0)
                return rawLine;

            string suffix = rawLine.Substring(equalIndex + 1);
            int commentIndex = FindCommentIndex(suffix);
            string comment = commentIndex >= 0 ? suffix.Substring(commentIndex) : string.Empty;
            string whitespace = new string(suffix.TakeWhile(char.IsWhiteSpace).ToArray());
            if (whitespace.Length == 0)
                whitespace = " ";

            string commentSpacing = string.IsNullOrEmpty(comment) ? string.Empty : " ";
            return rawLine.Substring(0, equalIndex + 1) + whitespace + newValue + commentSpacing + comment.TrimStart();
        }

        private static int FindCommentIndex(string value)
        {
            bool insideDoubleQuotes = false;
            bool insideSingleQuotes = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '"' && !insideSingleQuotes)
                    insideDoubleQuotes = !insideDoubleQuotes;
                else if (current == '\'' && !insideDoubleQuotes)
                    insideSingleQuotes = !insideSingleQuotes;
                else if (current == ';' && !insideDoubleQuotes && !insideSingleQuotes)
                    return index;
            }

            return -1;
        }

        private static string NormalizeKey(string key)
        {
            string normalizedKey = (key ?? string.Empty).Trim().ToLowerInvariant();
            return normalizedKey == "defense" ? "defence" : normalizedKey;
        }

        private static string ToSectionDisplayName(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return string.Empty;

            return char.ToUpperInvariant(section[0]) + section.Substring(1).ToLowerInvariant();
        }

        private static bool TryResolveFileInsideCharacterFolder(string characterFolder, string relativePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(characterFolder) || string.IsNullOrWhiteSpace(relativePath))
                return false;

            string normalizedReference = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string normalizedFolder = Path.GetFullPath(characterFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string folderPrefix = normalizedFolder + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(normalizedFolder, normalizedReference));

            if (!candidate.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = candidate;
            return true;
        }

        private static bool TrySplitAssignment(string line, out string key, out string value)
        {
            key = null;
            value = null;
            int equalIndex = line.IndexOf('=');
            if (equalIndex < 1)
                return false;

            key = line.Substring(0, equalIndex).Trim();
            value = line.Substring(equalIndex + 1).Trim();
            return key.Length > 0;
        }

        private static string CleanValue(string value)
        {
            return RemoveComment(value).Trim().Trim('"').Trim('\'').Trim();
        }

        private static string RemoveComment(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool insideDoubleQuotes = false;
            bool insideSingleQuotes = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '"' && !insideSingleQuotes)
                    insideDoubleQuotes = !insideDoubleQuotes;
                else if (current == '\'' && !insideDoubleQuotes)
                    insideSingleQuotes = !insideSingleQuotes;
                else if (current == ';' && !insideDoubleQuotes && !insideSingleQuotes)
                    return value.Substring(0, index);
            }

            return value;
        }

        private static MugenCharacterSecurityInspection Fail(string message, string cnsFile = null)
        {
            return new MugenCharacterSecurityInspection
            {
                Success = false,
                CnsFile = cnsFile,
                Reliable = false,
                Safe = false,
                Message = message,
                Attributes = new List<MugenCharacterAttributeInspection>()
            };
        }

        private sealed class CharacterAttributeReference
        {
            public CharacterAttributeReference(
                string key,
                string name,
                string section,
                double standardValue,
                bool editable,
                bool hasStandard)
            {
                Key = key;
                Name = name;
                Section = section;
                StandardValue = standardValue;
                Editable = editable;
                HasStandard = hasStandard;
            }

            public string Key { get; private set; }
            public string Name { get; private set; }
            public string Section { get; private set; }
            public double StandardValue { get; private set; }
            public bool Editable { get; private set; }
            public bool HasStandard { get; private set; }
        }
    }

    public sealed class MugenCharacterSecurityInspection
    {
        public bool Success { get; set; }
        public string CnsFile { get; set; }
        public bool Reliable { get; set; }
        public bool Safe { get; set; }
        public bool HasHigherAttribute { get; set; }
        public bool HasInvalidValue { get; set; }
        public string Message { get; set; }
        public IList<MugenCharacterAttributeInspection> Attributes { get; set; }
    }

    public sealed class MugenCharacterAttributeInspection
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Editable { get; set; }
        public bool HasStandard { get; set; }
        public double? Value { get; set; }
        public string RawValue { get; set; }
        public double StandardValue { get; set; }
        public bool Informed { get; set; }
        public bool Valid { get; set; }
        public bool HigherThanStandard { get; set; }
    }
}
