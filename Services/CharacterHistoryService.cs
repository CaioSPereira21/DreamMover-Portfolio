using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DreamMover.Services
{
    public sealed class CharacterHistoryValue
    {
        public string Valor { get; set; }
        public string Imagem { get; set; }
    }

    public sealed class CharacterUpdateSearch
    {
        public string DataBusca { get; set; }
        public DateTime RegistradaUtc { get; set; }
        public string Observacao { get; set; }
        public string Fonte { get; set; }
        public string Responsavel { get; set; }
    }

    public sealed class CharacterHistorySnapshot
    {
        public int Id { get; set; }
        public string DefPath { get; set; }
        public bool ArquivosLidos { get; set; }
        public Dictionary<string, CharacterHistoryValue> Valores { get; set; } = new Dictionary<string, CharacterHistoryValue>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    public sealed class CharacterHistoryChange
    {
        public string Campo { get; set; }
        public string Anterior { get; set; }
        public string Atual { get; set; }
        public string ImagemAnterior { get; set; }
        public string ImagemAtual { get; set; }
    }

    public sealed class CharacterHistoryEvent
    {
        public string Id { get; set; }
        public DateTime DataUtc { get; set; }
        public string Origem { get; set; }
        public string Responsavel { get; set; }
        public bool Inicial { get; set; }
        public List<CharacterHistoryChange> Campos { get; set; }
    }

    public sealed class CharacterHistoryDocument
    {
        public int Versao { get; set; } = 1;
        public CharacterHistorySnapshot Atual { get; set; }
        public List<CharacterHistoryEvent> Eventos { get; set; } = new List<CharacterHistoryEvent>();
        public CharacterUpdateSearch UltimaBuscaAtualizacoes { get; set; }
    }

    // A snapshot precedes every supported edit, including overwrites of the same image filename.
    // The journal is replaced atomically; a cross-process lock protects read/modify/write.
    public static class CharacterHistoryService
    {
        public static IDisposable LockEdits(string root)
        {
            Directory.CreateDirectory(root);
            return Acquire(Path.Combine(root, "editing.lock"));
        }

        public static CharacterHistoryDocument Read(string root, int id)
        {
            string folder = Folder(root, id);
            Directory.CreateDirectory(folder);
            using (Acquire(Path.Combine(folder, "history.lock")))
                return ReadDocument(Path.Combine(folder, "history.json"));
        }

        public static CharacterHistoryDocument Record(string root, CharacterHistorySnapshot snapshot, string origin, string responsible)
        {
            string folder = Folder(root, snapshot.Id);
            Directory.CreateDirectory(folder);
            using (Acquire(Path.Combine(folder, "history.lock")))
            {
                string path = Path.Combine(folder, "history.json");
                CharacterHistoryDocument document = ReadDocument(path);
                bool initial = document.Atual == null;
                // An unavailable CNS or image is not evidence that its previous value was deleted.
                if (!initial)
                {
                    // The file fingerprint catalogue grew after the first baseline was created.
                    // Treat newly discovered references as part of that baseline, not as a user edit.
                    bool migratingFileCatalogue = !document.Atual.ArquivosLidos && snapshot.ArquivosLidos;
                    if (migratingFileCatalogue)
                        foreach (var item in snapshot.Valores.Where(x => x.Key.StartsWith("Arquivo: ", StringComparison.Ordinal)))
                            if (!document.Atual.Valores.ContainsKey(item.Key))
                                document.Atual.Valores[item.Key] = item.Value;

                    foreach (var item in document.Atual.Valores)
                        if (!snapshot.Valores.ContainsKey(item.Key))
                            snapshot.Valores[item.Key] = snapshot.ArquivosLidos && item.Key.StartsWith("Arquivo: ", StringComparison.Ordinal)
                                ? new CharacterHistoryValue { Valor = string.Empty } : item.Value;
                }

                var changes = Compare(initial ? null : document.Atual, snapshot);
                if (!initial && changes.Count == 0
                    && JsonConvert.SerializeObject(document.Atual) == JsonConvert.SerializeObject(snapshot))
                    return document;
                if (initial || changes.Count > 0)
                {
                    document.Eventos.Add(new CharacterHistoryEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        DataUtc = DateTime.UtcNow,
                        Origem = initial ? "Registro inicial" : origin,
                        Responsavel = responsible,
                        Inicial = initial,
                        Campos = changes
                    });
                }
                document.Atual = snapshot;
                WriteDocument(path, document);
                return document;
            }
        }

        public static CharacterUpdateSearch RecordUpdateSearch(string root, int id, string date, string notes, string source, string responsible)
        {
            DateTime parsed;
            if (!DateTime.TryParseExact(date ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
                throw new ArgumentException("A data da busca deve estar no formato AAAA-MM-DD.");

            string folder = Folder(root, id);
            Directory.CreateDirectory(folder);
            using (Acquire(Path.Combine(folder, "history.lock")))
            {
                string path = Path.Combine(folder, "history.json");
                CharacterHistoryDocument document = ReadDocument(path);
                if (document.Atual == null)
                    throw new InvalidDataException("Registre o estado inicial antes da busca de atualizações.");

                var result = new CharacterUpdateSearch
                {
                    DataBusca = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    RegistradaUtc = DateTime.UtcNow,
                    Observacao = (notes ?? string.Empty).Trim(),
                    Fonte = (source ?? string.Empty).Trim(),
                    Responsavel = string.IsNullOrWhiteSpace(responsible) ? "Usuário local" : responsible.Trim()
                };
                document.UltimaBuscaAtualizacoes = result;
                WriteDocument(path, document);
                return result;
            }
        }

        public static string Category(string field)
        {
            if ((field ?? string.Empty).StartsWith("Arquivo: ", StringComparison.Ordinal)) return "arquivos";
            if (field == "Retrato" || field == "Pose") return "imagens";
            if (new[] { "Vida", "Ataque", "Defesa", "Energia", "Escala horizontal", "Escala vertical" }.Contains(field)) return "atributos";
            return "cadastro";
        }

        public static List<CharacterHistoryEvent> FilterEvents(CharacterHistoryDocument document, string category, string search, string anchor)
        {
            var events = document.Eventos.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(anchor))
            {
                int index = document.Eventos.FindIndex(x => x.Id == anchor);
                if (index < 0) throw new ArgumentException("Atualize o histórico para continuar a consulta.");
                events = events.Take(index + 1);
            }
            string term = (search ?? string.Empty).Trim();
            var comparison = new CultureInfo("pt-BR").CompareInfo;
            Func<string, bool> matches = value => comparison.IndexOf(value ?? string.Empty, term,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            return events.Reverse().Where(e =>
                (string.IsNullOrWhiteSpace(category) || category == "todos" || e.Campos.Any(c => Category(c.Campo) == category))
                && (term.Length == 0 || matches(e.Origem) || matches(e.Responsavel)
                    || e.Campos.Any(c => matches(c.Campo) || matches(c.Anterior) || matches(c.Atual))))
                .ToList();
        }

        public static List<CharacterHistoryChange> Compare(CharacterHistorySnapshot before, CharacterHistorySnapshot after)
        {
            var result = new List<CharacterHistoryChange>();
            foreach (var item in after.Valores)
            {
                CharacterHistoryValue old = null;
                if (before != null) before.Valores.TryGetValue(item.Key, out old);
                if (before != null && old != null && old.Valor == item.Value.Valor && old.Imagem == item.Value.Imagem)
                    continue;
                result.Add(new CharacterHistoryChange
                {
                    Campo = item.Key,
                    Anterior = old == null ? null : old.Valor,
                    Atual = item.Value.Valor,
                    ImagemAnterior = old == null ? null : old.Imagem,
                    ImagemAtual = item.Value.Imagem
                });
            }
            return result;
        }

        public static void Set(CharacterHistorySnapshot snapshot, string field, string value)
        {
            snapshot.Valores[field] = new CharacterHistoryValue { Valor = value ?? string.Empty };
        }

        public static void CaptureImage(string root, CharacterHistorySnapshot snapshot, string field, string imageRoot, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) { Set(snapshot, field, string.Empty); return; }
            string name = Path.GetFileName(filename);
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (!Regex.IsMatch(extension, @"^\.(png|jpe?g|gif|webp)$"))
            {
                snapshot.Avisos.Add(field + ": formato de imagem indisponível para arquivamento.");
                return;
            }
            string source = Path.Combine(imageRoot, name);
            if (!File.Exists(source))
            {
                snapshot.Avisos.Add(field + ": imagem não encontrada.");
                return;
            }
            byte[] bytes = File.ReadAllBytes(source);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string archivedName = hash + extension;
            string images = Path.Combine(Folder(root, snapshot.Id), "images");
            Directory.CreateDirectory(images);
            string target = Path.Combine(images, archivedName);
            if (!File.Exists(target))
            {
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    try { File.Move(temporary, target); }
                    catch (IOException) { if (!File.Exists(target)) throw; }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            snapshot.Valores[field] = new CharacterHistoryValue { Valor = name, Imagem = archivedName };
        }

        public static void CaptureDefinition(CharacterHistorySnapshot snapshot, string defPath)
        {
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
            {
                snapshot.Avisos.Add("Pasta do personagem não localizada; atributos não puderam ser lidos.");
                return;
            }
            snapshot.DefPath = Path.GetFullPath(defPath);
            var info = ReadInfo(defPath);
            foreach (var field in new[] { new[] { "author", "Autor no arquivo" }, new[] { "versiondate", "Versão declarada" }, new[] { "mugenversion", "Versão do MUGEN" } })
            {
                string value;
                Set(snapshot, field[1], info.TryGetValue(field[0], out value) ? value : string.Empty);
            }
            Set(snapshot, "Arquivo do personagem", Path.GetFileName(Path.GetDirectoryName(defPath)) + "/" + Path.GetFileName(defPath));
            CaptureBehaviorFiles(snapshot, defPath);
            var inspection = MugenCharacterSecurityService.Inspect(defPath);
            if (!inspection.Success) { snapshot.Avisos.Add(inspection.Message); return; }
            var labels = new Dictionary<string, string>
            {
                { "life", "Vida" }, { "attack", "Ataque" }, { "defence", "Defesa" },
                { "power", "Energia" }, { "xscale", "Escala horizontal" }, { "yscale", "Escala vertical" }
            };
            foreach (var attribute in inspection.Attributes)
                Set(snapshot, labels[attribute.Key], attribute.Value.HasValue
                    ? attribute.Value.Value.ToString("G", CultureInfo.InvariantCulture)
                    : "Inválido: " + attribute.RawValue);
        }

        public static Dictionary<string, string> ReadInfo(string path)
        {
            var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inInfo = false;
            foreach (string raw in File.ReadAllLines(path, Encoding.Default))
            {
                string line = Regex.Replace(raw, ";.*$", "").Trim();
                if (line.StartsWith("[")) { inInfo = line.Equals("[Info]", StringComparison.OrdinalIgnoreCase); continue; }
                int equals = line.IndexOf('=');
                if (inInfo && equals > 0) info[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim().Trim('"');
            }
            return info;
        }

        private static void CaptureBehaviorFiles(CharacterHistorySnapshot snapshot, string defPath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(defPath)) + Path.DirectorySeparatorChar;
            var paths = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            paths[Path.GetFileName(defPath)] = defPath;
            bool inFiles = false;
            foreach (string raw in File.ReadAllLines(defPath, Encoding.Default))
            {
                string line = Regex.Replace(raw, ";.*$", "").Trim();
                if (line.StartsWith("[")) { inFiles = line.Equals("[Files]", StringComparison.OrdinalIgnoreCase); continue; }
                var match = Regex.Match(line, @"^(cmd|cns|st\d*|stcommon)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                if (!inFiles || !match.Success) continue;
                string relative = match.Groups[2].Value.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(folder, relative));
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) paths[relative] = full;
            }
            using (var sha = SHA256.Create())
                foreach (var file in paths)
                {
                    if (!File.Exists(file.Value)) { Set(snapshot, "Arquivo: " + file.Key.Replace('\\', '/'), "Arquivo ausente"); continue; }
                    using (var stream = File.OpenRead(file.Value))
                        Set(snapshot, "Arquivo: " + file.Key.Replace('\\', '/'), BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
                }
            snapshot.ArquivosLidos = true;
        }

        public static string ImagePath(string root, int id, string filename)
        {
            if (filename == null || !Regex.IsMatch(filename, @"\A[a-f0-9]{64}\.(png|jpe?g|gif|webp)\z"))
                throw new ArgumentException("Imagem de histórico inválida.");
            return Path.Combine(Folder(root, id), "images", filename);
        }

        private static string Folder(string root, int id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException("id");
            return Path.Combine(root, id.ToString(CultureInfo.InvariantCulture));
        }

        private static CharacterHistoryDocument ReadDocument(string path)
        {
            if (!File.Exists(path)) return new CharacterHistoryDocument();
            var document = JsonConvert.DeserializeObject<CharacterHistoryDocument>(File.ReadAllText(path, Encoding.UTF8));
            if (document == null || document.Versao != 1 || document.Eventos == null || document.Atual == null || document.Atual.Valores == null)
                throw new InvalidDataException("Histórico inválido. O arquivo existente foi preservado.");
            return document;
        }

        private static void WriteDocument(string path, CharacterHistoryDocument document)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static FileStream Acquire(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (attempt >= 100) throw; Thread.Sleep(50); }
            }
        }
    }
}
