using System.Text.Json.Nodes;
using RustWikiScraper.Core;

namespace RustWikiScraper.Tests;

public class CatalogRulesTests
{
    static JsonObject Entry(string en, string tr, bool trCheck = false, bool removed = false) => new()
    {
        ["id"] = 1,
        ["name"] = new JsonObject { ["en"] = en, ["tr"] = tr },
        ["desc"] = new JsonObject { ["en"] = "d", ["tr"] = "açıklama" },
        ["icon"] = new JsonObject { ["src"] = "x.png", ["sha256"] = "abc", ["updated"] = "2026-10-21" },
        ["tr_check"] = trCheck,
        ["removed"] = removed,
    };

    [Fact]
    public void Turkish_is_kept_and_English_comes_from_rusthelp()
    {
        var e = CatalogRules.BuildEntry(Entry("Assault Rifle", "Saldırı Tüfeği"), JsonValue.Create(1545779598), "Assault Rifle", "High damage.", null, out var changed);
        Assert.False(changed);
        Assert.Equal("Saldırı Tüfeği", e["name"]!["tr"]!.GetValue<string>());
        Assert.Equal("açıklama", e["desc"]!["tr"]!.GetValue<string>());
        Assert.Equal("High damage.", e["desc"]!["en"]!.GetValue<string>());
        Assert.False(e["tr_check"]!.GetValue<bool>());
    }

    [Fact]
    public void Changed_English_name_sets_tr_check_and_it_stays_set()
    {
        var e = CatalogRules.BuildEntry(Entry("Old", "Eski"), JsonValue.Create(1), "New", "", null, out var changed);
        Assert.True(changed);
        Assert.True(e["tr_check"]!.GetValue<bool>());
        var again = CatalogRules.BuildEntry(e, JsonValue.Create(1), "New", "", null, out var changedAgain);
        Assert.False(changedAgain);
        Assert.True(again["tr_check"]!.GetValue<bool>());
    }

    [Fact]
    public void New_item_has_empty_Turkish_and_empty_icon()
    {
        var e = CatalogRules.BuildEntry(null, JsonValue.Create(-5), "Thing", "", null, out var changed);
        Assert.False(changed);
        Assert.Equal("", e["name"]!["tr"]!.GetValue<string>());
        Assert.Empty(e["icon"]!.AsObject());
        Assert.Equal(-5, e["id"]!.GetValue<int>());
    }

    [Fact]
    public void Gone_items_get_removed_and_unreached_items_stay_as_they_were()
    {
        var old = new JsonObject { ["gone.item"] = Entry("Gone", "Gitti"), ["slow.item"] = Entry("Slow", "Yavaş") };
        var fresh = new JsonObject();
        CatalogRules.KeepUnreached(old, fresh, new HashSet<string> { "slow.item" });
        Assert.True(fresh["gone.item"]!["removed"]!.GetValue<bool>());
        Assert.False(fresh["slow.item"]!["removed"]!.GetValue<bool>());
        Assert.Equal(["gone.item"], CatalogRules.Gone(old, new HashSet<string> { "slow.item" }));
    }

    [Fact]
    public void Already_removed_items_are_not_reported_again()
    {
        var old = new JsonObject { ["gone.item"] = Entry("Gone", "", removed: true) };
        Assert.Empty(CatalogRules.Gone(old, new HashSet<string>()));
    }

    [Fact]
    public void Icon_file_and_url_handle_spaces_in_shortnames()
    {
        Assert.Equal("mini-fridge.png", CatalogRules.IconFile("mini fridge"));
        Assert.Equal("https://cdn.rusthelp.com/images/public/mini%20fridge.png",
            Scraper.Escape("https://cdn.rusthelp.com/images/public/mini fridge.png"));
    }

    [Fact]
    public void Saved_file_is_sorted_and_keeps_Turkish_letters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"catalog-test-{Guid.NewGuid():N}.json");
        try
        {
            CatalogRules.Save(path, new JsonObject { ["b"] = Entry("B", "Şğü"), ["a"] = Entry("A", "") });
            var text = File.ReadAllText(path);
            Assert.Contains("Şğü", text);
            Assert.True(text.IndexOf("\"a\"", StringComparison.Ordinal) < text.IndexOf("\"b\"", StringComparison.Ordinal));
            Assert.Equal(2, CatalogRules.Load(path).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pictures_become_512_png()
    {
        var src = new System.Windows.Media.Imaging.WriteableBitmap(256, 256, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var png = ImageTools.To512Png(ms.ToArray());
        var back = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(png),
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(512, back.PixelWidth);
        Assert.Equal(512, back.PixelHeight);
    }
}
