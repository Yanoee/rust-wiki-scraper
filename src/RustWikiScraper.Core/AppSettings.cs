using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustWikiScraper.Core;

public sealed class AppSettings
{
    [JsonPropertyName("out")] public string Out { get; set; } = AppContext.BaseDirectory;
    [JsonPropertyName("threads")] public int Threads { get; set; } = 8;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "RustWikiScraper.settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public string WorldDir => Path.Combine(Out, "rusthelp_world");
    public string ItemDir => Path.Combine(Out, "rusthelp_items");
    public string CatalogPath => Path.Combine(Out, "catalog.json");
}
