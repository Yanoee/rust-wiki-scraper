using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RustWikiScraper.Core;

public enum LogKind { Info, Ok, Dim, Warn, Err }

public sealed record ScrapeOptions(bool World, bool Items, bool Catalog, bool Redo, AppSettings Settings);

public interface IScrapeSink
{
    void Log(string message, LogKind kind);
    void Phase(string name, int total);
    void Tick(int done, int saved, int skipped, int failed);
    void CheckResult(int added, int renamed, int gone, int missingIcons) { }
}

public sealed partial class Scraper(IScrapeSink sink)
{
    public const string ItemListUrl = "https://rusthelp.com/downloads/admin-item-list-public.json";
    public const string WorldListUrl = "https://rusthelp.com/browse/world";
    const string Cdn = "https://cdn.rusthelp.com/images";

    static readonly HttpClient Http = CreateClient();
    static readonly int[] RetryWaits = [10, 30, 60];

    int saved, skipped, done;
    readonly ConcurrentBag<string> failed = [];

    public int Saved => saved;
    public int Skipped => skipped;
    public IReadOnlyCollection<string> Failed => failed;

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return client;
    }

    internal static string Escape(string url) => url.Replace(" ", "%20");

    async Task<byte[]?> GetAsync(string url, CancellationToken ct, bool missingIsNull = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await Http.GetAsync(Escape(url), ct);
            if (response.StatusCode == HttpStatusCode.NotFound && missingIsNull) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < RetryWaits.Length)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(RetryWaits[attempt]);
                sink.Log($"rate limited, waiting {wait.TotalSeconds:0}s", LogKind.Warn);
                await Task.Delay(wait, ct);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    async Task<string?> PictureAsync(string url, string dest, CancellationToken ct)
    {
        var raw = await GetAsync(url, ct, missingIsNull: true);
        if (raw is null) return null;
        var png = ImageTools.To512Png(raw);
        await File.WriteAllBytesAsync(dest, png, ct);
        return ImageTools.Sha256(png);
    }

    void Count(LogKind kind, string name, string message)
    {
        switch (kind)
        {
            case LogKind.Err: failed.Add(name); break;
            case LogKind.Dim: Interlocked.Increment(ref skipped); break;
            default: Interlocked.Increment(ref saved); break;
        }
        sink.Log(message, kind);
        sink.Tick(Interlocked.Increment(ref done), saved, skipped, failed.Count);
    }

    async Task ParallelAsync<T>(string phase, IReadOnlyList<T> tasks, Func<T, string> name, int threads,
        Func<T, CancellationToken, Task<(LogKind, string)>> work, CancellationToken ct)
    {
        done = 0;
        sink.Phase(phase, tasks.Count);
        await Parallel.ForEachAsync(tasks, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(threads, 1, 32), CancellationToken = ct },
            async (task, token) =>
            {
                try
                {
                    var (kind, message) = await work(task, token);
                    Count(kind, name(task), message);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Count(LogKind.Err, name(task), $"{name(task)}: {e.Message}");
                }
            });
    }

    public async Task RunAsync(ScrapeOptions o, CancellationToken ct)
    {
        try
        {
            if (o.World) await WorldAsync(o, ct);
            if (o.Items || o.Catalog) await ItemsAsync(o, ct);
            sink.Log($"Finished: {saved} saved, {skipped} skipped, {failed.Count} failed", LogKind.Info);
        }
        catch (OperationCanceledException)
        {
            sink.Log("Stopped.", LogKind.Warn);
        }
        catch (Exception e)
        {
            sink.Log($"Run failed: {e.Message}", LogKind.Err);
        }
        if (!failed.IsEmpty) sink.Log("Failed: " + string.Join(", ", failed.Order()), LogKind.Err);
    }

    [GeneratedRegex("/world/([a-z0-9-]+)")]
    private static partial Regex WorldLink();

    async Task WorldAsync(ScrapeOptions o, CancellationToken ct)
    {
        sink.Log("Fetching world list ...", LogKind.Info);
        var html = System.Text.Encoding.UTF8.GetString((await GetAsync(WorldListUrl, ct))!);
        var slugs = WorldLink().Matches(html).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();
        var dir = o.Settings.WorldDir;
        Directory.CreateDirectory(dir);
        sink.Log($"{slugs.Count} world entries -> {dir}", LogKind.Info);

        await ParallelAsync("World pictures", slugs, s => s, o.Settings.Threads, async (slug, token) =>
        {
            var dest = Path.Combine(dir, slug + ".png");
            if (File.Exists(dest) && !o.Redo) return (LogKind.Dim, $"skip  {slug}  (already saved)");
            if (await PictureAsync($"{Cdn}/source/{slug}.png", dest, token) is null)
                return (LogKind.Dim, $"skip  {slug}  (no picture, category page)");
            return (LogKind.Ok, $"saved {slug}");
        }, ct);
    }

    async Task<List<JsonObject>> ItemListAsync(CancellationToken ct) =>
        JsonNode.Parse((await GetAsync(ItemListUrl, ct))!)!.AsArray().OfType<JsonObject>().ToList();

    async Task ItemsAsync(ScrapeOptions o, CancellationToken ct)
    {
        sink.Log("Fetching item list ...", LogKind.Info);
        var items = await ItemListAsync(ct);
        var catalogPath = o.Settings.CatalogPath;
        var old = CatalogRules.Load(catalogPath);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var dir = o.Settings.ItemDir;
        sink.Log($"{items.Count} items on rusthelp, {old.Count} in catalog.json", LogKind.Info);
        if (o.Items) Directory.CreateDirectory(dir);

        var fresh = new ConcurrentDictionary<string, JsonObject>();
        var added = new ConcurrentBag<string>();
        var renamed = new ConcurrentBag<string>();
        var complete = false;
        try
        {
            await ParallelAsync("Items", items, it => it["shortName"]!.GetValue<string>(), o.Settings.Threads, async (it, token) =>
            {
                var shortName = it["shortName"]!.GetValue<string>();
                var prev = old[shortName] as JsonObject;
                var src = CatalogRules.IconFile(shortName);
                var icon = prev?["icon"];
                var notes = new List<string>();
                var kind = LogKind.Ok;
                if (o.Items)
                {
                    var dest = Path.Combine(dir, src);
                    if (File.Exists(dest) && !o.Redo)
                    {
                        if (icon?["src"]?.GetValue<string>() != src)
                            icon = Icon(src, ImageTools.Sha256(await File.ReadAllBytesAsync(dest, token)), today);
                        kind = LogKind.Dim;
                        notes.Add("icon already saved");
                    }
                    else
                    {
                        var url = it["iconUrl"]?.GetValue<string>() is { Length: > 0 } u ? u : $"{Cdn}/public/{shortName}.png";
                        var sha = await PictureAsync(url, dest, token);
                        if (sha is null)
                        {
                            kind = LogKind.Warn;
                            notes.Add("no icon on rusthelp");
                        }
                        else if (sha != icon?["sha256"]?.GetValue<string>())
                        {
                            icon = Icon(src, sha, today);
                        }
                    }
                }
                var en = it["displayName"]?.GetValue<string>() ?? "";
                var desc = it["description"]?.GetValue<string>() ?? "";
                var entry = CatalogRules.BuildEntry(prev, it["id"]!, en, desc, icon, out var enChanged);
                if (enChanged)
                {
                    kind = LogKind.Warn;
                    notes.Add("EN name changed -> TR kontrol et");
                    renamed.Add(shortName);
                }
                if (old.Count > 0 && prev is null) added.Add(shortName);
                fresh[shortName] = entry;
                if (kind == LogKind.Dim && o.Catalog) kind = LogKind.Ok;
                var verb = kind == LogKind.Dim ? "skip " : "saved";
                return (kind, $"{verb} {shortName,-34} {en}" + (notes.Count > 0 ? $"  ({string.Join(", ", notes)})" : ""));
            }, ct);
            complete = true;
        }
        finally
        {
            if (o.Catalog && !fresh.IsEmpty)
            {
                var listed = items.Select(it => it["shortName"]!.GetValue<string>()).ToHashSet();
                var catalog = new JsonObject();
                foreach (var (k, v) in fresh) catalog[k] = v;
                CatalogRules.KeepUnreached(old, catalog, listed);
                CatalogRules.Save(catalogPath, catalog);
                var gone = CatalogRules.Gone(old, listed).Order().ToList();
                sink.Log($"Saved catalog.json: {catalog.Count} items" + (complete ? "" : " (partial run)"), LogKind.Info);
                foreach (var (label, keys, kind) in new[] { ("New items", added.Order().ToList(), LogKind.Ok),
                             ("TR kontrol et", renamed.Order().ToList(), LogKind.Warn), ("Marked removed", gone, LogKind.Warn) })
                    if (keys.Count > 0) sink.Log($"{label} ({keys.Count}): {string.Join(", ", keys)}", kind);
            }
        }
    }

    static JsonObject Icon(string src, string sha, string today) => new() { ["src"] = src, ["sha256"] = sha, ["updated"] = today };

    public async Task CheckAsync(AppSettings settings, CancellationToken ct)
    {
        try
        {
            sink.Log("Checking rusthelp for changes ...", LogKind.Info);
            var items = (await ItemListAsync(ct)).ToDictionary(it => it["shortName"]!.GetValue<string>());
            var old = CatalogRules.Load(settings.CatalogPath);
            var listed = items.Keys.ToHashSet();
            var added = items.Keys.Where(k => !old.ContainsKey(k)).ToList();
            var renamed = items.Keys.Where(k => old.ContainsKey(k) && CatalogRules.EnglishName(old[k]) != (items[k]["displayName"]?.GetValue<string>() ?? "")).ToList();
            var gone = CatalogRules.Gone(old, listed).ToList();
            var noIcon = items.Keys.Where(k => !File.Exists(Path.Combine(settings.ItemDir, CatalogRules.IconFile(k)))).ToList();
            sink.Log($"rusthelp lists {items.Count} items, catalog.json has {old.Count}", LogKind.Info);
            foreach (var (label, keys, kind) in new[] { ("New on rusthelp", added, LogKind.Ok), ("English name changed (TR kontrol et)", renamed, LogKind.Warn),
                         ("Gone from rusthelp", gone, LogKind.Warn), ("Icon not downloaded yet", noIcon, LogKind.Dim) })
            {
                sink.Log($"{label}: {keys.Count}", keys.Count > 0 ? kind : LogKind.Dim);
                foreach (var k in keys.Take(40))
                    sink.Log($"    {k}  {items.GetValueOrDefault(k)?["displayName"]?.GetValue<string>()}", kind);
                if (keys.Count > 40) sink.Log($"    ... and {keys.Count - 40} more", kind);
            }
            sink.CheckResult(added.Count, renamed.Count, gone.Count, noIcon.Count);
        }
        catch (OperationCanceledException) { sink.Log("Stopped.", LogKind.Warn); }
        catch (Exception e) { sink.Log($"Check failed: {e.Message}", LogKind.Err); }
    }
}
