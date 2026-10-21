using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustWikiScraper.Core;

public static class CatalogRules
{
    static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string IconFile(string shortName) => shortName.ToLowerInvariant().Replace(' ', '-') + ".png";

    public static JsonObject Load(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    public static void Save(string path, JsonObject catalog)
    {
        var sorted = new JsonObject();
        foreach (var (key, value) in catalog.OrderBy(p => p.Key, StringComparer.Ordinal))
            sorted[key] = value?.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sorted.ToJsonString(Write));
    }

    static string Str(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";

    public static string EnglishName(JsonNode? entry) => Str(entry?["name"], "en");

    public static JsonObject BuildEntry(JsonObject? prev, JsonNode id, string en, string desc, JsonNode? icon, out bool enChanged)
    {
        enChanged = prev is not null && EnglishName(prev) != en;
        return new JsonObject
        {
            ["id"] = id.DeepClone(),
            ["name"] = new JsonObject { ["en"] = en, ["tr"] = Str(prev?["name"], "tr") },
            ["desc"] = new JsonObject { ["en"] = desc, ["tr"] = Str(prev?["desc"], "tr") },
            ["icon"] = icon?.DeepClone() ?? new JsonObject(),
            ["tr_check"] = (prev?["tr_check"]?.GetValue<bool>() ?? false) || enChanged,
            ["removed"] = false,
        };
    }

    public static void KeepUnreached(JsonObject old, JsonObject fresh, ISet<string> listed)
    {
        foreach (var (key, value) in old)
        {
            if (fresh.ContainsKey(key) || value is null) continue;
            var copy = value.DeepClone().AsObject();
            if (!listed.Contains(key)) copy["removed"] = true;
            fresh[key] = copy;
        }
    }

    public static IEnumerable<string> Gone(JsonObject old, ISet<string> listed) =>
        old.Where(p => !listed.Contains(p.Key) && !(p.Value?["removed"]?.GetValue<bool>() ?? false)).Select(p => p.Key);
}
