using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using RustWikiScraper.Core;

namespace RustWikiScraper;

public partial class MainWindow : Window
{
    readonly AppSettings settings = AppSettings.Load();
    readonly ObservableCollection<LogLine> log = [];
    readonly ConcurrentQueue<Action> pending = new();
    readonly DispatcherTimer pump = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(160) };
    CancellationTokenSource? cts;
    DateTime phaseStart;
    int phaseTotal;

    List<LibItem>? libItems, libWorld;
    List<LibItem> libShown = [];
    LibItem? selected;
    int libColumns;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (!Native.ApplyGlass(this))
                Root.Background = new LinearGradientBrush(Color.FromRgb(0x0B, 0x0D, 0x14), Color.FromRgb(0x16, 0x19, 0x26), 45);
        };
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            BtnMax.Tag = WindowState == WindowState.Maximized ? "" : "";
        };

        var view = CollectionViewSource.GetDefaultView(log);
        view.Filter = o => FilterAll.IsChecked == true || ((LogLine)o).Problem;
        LogList.ItemsSource = view;

        pump.Tick += (_, _) => Pump();
        pump.Start();
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); LibRender(); };

        OutPath.Text = settings.Out;
        foreach (var n in new[] { 1, 2, 4, 8, 12, 16 })
        {
            var rb = new RadioButton { Content = n.ToString(), Style = (Style)FindResource("Seg"), GroupName = "Threads", IsChecked = n == settings.Threads, MinWidth = 44 };
            rb.Checked += (_, _) => { settings.Threads = n; settings.Save(); };
            ThreadsPanel.Children.Add(rb);
        }
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        cts?.Cancel();
        base.OnClosing(e);
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageDownload is null) return;
        FrameworkElement page = sender == NavLibrary ? PageLibrary : sender == NavSettings ? PageSettings : PageDownload;
        foreach (var p in new FrameworkElement[] { PageDownload, PageLibrary, PageSettings })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        page.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        if (page == PageLibrary) LibLoad(force: false);
    }

    sealed class Sink(MainWindow w) : IScrapeSink
    {
        public void Log(string message, LogKind kind) => w.pending.Enqueue(() => w.AddLog(message, kind));
        public void Phase(string name, int total) => w.pending.Enqueue(() => w.SetPhase(name, total));
        public void Tick(int done, int saved, int skipped, int failed) => w.pending.Enqueue(() => w.SetTick(done, saved, skipped, failed));
        public void CheckResult(int added, int renamed, int gone, int missing) => w.pending.Enqueue(() =>
        {
            w.PhaseText.Text = $"{added} new · {renamed} renamed · {gone} gone · {missing} icons missing";
            w.PhaseText.Foreground = w.Brush(added + renamed + gone + missing > 0 ? "Yellow" : "Green");
        });
    }

    Brush Brush(string key) => (Brush)FindResource(key);

    void Pump()
    {
        var any = false;
        while (pending.TryDequeue(out var action)) { action(); any = true; }
        if (any && log.Count > 0) LogList.ScrollIntoView(LogList.Items.Count > 0 ? LogList.Items[^1] : null);
    }

    void AddLog(string message, LogKind kind)
    {
        var key = kind switch { LogKind.Ok => "Green", LogKind.Warn => "Yellow", LogKind.Err => "Red", LogKind.Info => "Teal", _ => "Text3" };
        log.Add(new LogLine(message, Brush(key), kind is LogKind.Warn or LogKind.Err));
        while (log.Count > 5000) log.RemoveAt(0);
    }

    void SetPhase(string name, int total)
    {
        phaseTotal = total;
        phaseStart = DateTime.Now;
        PhaseText.Text = "Downloading…";
        ProgressTitle.Text = $"{name} · 0 / {total}";
        Progress.Maximum = Math.Max(total, 1);
        Progress.Value = 0;
        PercentText.Text = "0%";
    }

    void SetTick(int done, int saved, int skipped, int failed)
    {
        Progress.Value = done;
        PercentText.Text = $"{done * 100 / Math.Max(phaseTotal, 1)}%";
        ProgressTitle.Text = $"{ProgressTitle.Text.Split(" · ")[0]} · {done} / {phaseTotal}";
        var rate = done / Math.Max((DateTime.Now - phaseStart).TotalSeconds, 0.001);
        var left = rate > 0 ? TimeSpan.FromSeconds((phaseTotal - done) / rate) : TimeSpan.Zero;
        StatSaved.Text = saved.ToString();
        StatSkipped.Text = skipped.ToString();
        StatFailed.Text = failed.ToString();
        StatSpeed.Text = $"{rate:0.0}/s";
        StatEta.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    void SetRunning(bool running, bool checking = false)
    {
        BtnStart.IsEnabled = BtnCheck.IsEnabled = !running;
        BtnStop.IsEnabled = running;
        foreach (var c in new[] { OptWorld, OptItems, OptCatalog, OptRedo }) c.IsEnabled = !running;
        Anim.SetSpin(BtnCheck, checking);
        StatusText.Text = running ? (checking ? "Checking" : "Running") : "Idle";
        StatusDot.Fill = Brush(running ? "Green" : "Text3");
        StatusDot.BeginAnimation(OpacityProperty, running
            ? new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }

    void ResetRun()
    {
        log.Clear();
        foreach (var t in new[] { StatSaved, StatSkipped, StatFailed, StatSpeed, StatEta }) t.Text = "-";
        Progress.Value = 0;
        PercentText.Text = "0%";
        PhaseText.Foreground = Brush("Text2");
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        var o = new ScrapeOptions(OptWorld.IsChecked == true, OptItems.IsChecked == true, OptCatalog.IsChecked == true, OptRedo.IsChecked == true, settings);
        if (!(o.World || o.Items || o.Catalog))
        {
            Toast("Pick something to download first", "", "Yellow");
            return;
        }
        ResetRun();
        PhaseText.Text = "Starting…";
        SetRunning(true);
        cts = new CancellationTokenSource();
        var scraper = new Scraper(new Sink(this));
        await Task.Run(() => scraper.RunAsync(o, cts.Token));
        Pump();
        SetRunning(false);
        var stopped = cts.IsCancellationRequested;
        PhaseText.Text = stopped ? "Stopped" : scraper.Failed.Count == 0 ? "Done" : $"Done · {scraper.Failed.Count} failed";
        PhaseText.Foreground = Brush(stopped || scraper.Failed.Count > 0 ? "Yellow" : "Green");
        if (!stopped)
        {
            StatEta.Text = "0:00";
            Toast($"Done · {scraper.Saved} saved", "", "Green");
        }
        libItems = libWorld = null;
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        ResetRun();
        PhaseText.Text = "Checking…";
        ProgressTitle.Text = "Checking rusthelp";
        SetRunning(true, checking: true);
        cts = new CancellationTokenSource();
        await Task.Run(() => new Scraper(new Sink(this)).CheckAsync(settings, cts.Token));
        Pump();
        SetRunning(false);
        ProgressTitle.Text = "Nothing running";
    }

    void Stop_Click(object sender, RoutedEventArgs e)
    {
        cts?.Cancel();
        PhaseText.Text = "Stopping…";
        PhaseText.Foreground = Brush("Yellow");
    }

    void Clear_Click(object sender, RoutedEventArgs e) => log.Clear();

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (LogList?.ItemsSource is ICollectionView view) view.Refresh();
    }

    void LibLoad(bool force)
    {
        if (libItems is not null && !force) { LibRender(); return; }
        var catalog = CatalogRules.Load(settings.CatalogPath);
        var files = Directory.Exists(settings.ItemDir)
            ? Directory.EnumerateFiles(settings.ItemDir, "*.png").ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            : [];
        var items = new List<LibItem>();
        foreach (var (key, node) in catalog)
        {
            if (node is not JsonObject v) continue;
            var src = v["icon"]?["src"]?.GetValue<string>() ?? CatalogRules.IconFile(key);
            files.Remove(src, out var file);
            items.Add(new LibItem
            {
                Key = key,
                File = file,
                En = CatalogRules.EnglishName(v),
                Tr = v["name"]?["tr"]?.GetValue<string>() ?? "",
                Id = v["id"]?.ToJsonString(),
                Desc = v["desc"]?["en"]?.GetValue<string>() ?? "",
                Updated = v["icon"]?["updated"]?.GetValue<string>() ?? "",
                Removed = v["removed"]?.GetValue<bool>() ?? false,
                TrCheck = v["tr_check"]?.GetValue<bool>() ?? false,
            });
        }
        foreach (var (_, file) in files)
            items.Add(new LibItem { Key = Path.GetFileNameWithoutExtension(file), File = file });
        libItems = items.OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
        libWorld = Directory.Exists(settings.WorldDir)
            ? Directory.EnumerateFiles(settings.WorldDir, "*.png").Order(StringComparer.Ordinal).Select(f =>
            {
                var slug = Path.GetFileNameWithoutExtension(f);
                return new LibItem { Key = slug, File = f, En = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' ')) };
            }).ToList()
            : [];
        LibRender();
    }

    void LibRender()
    {
        if (libItems is null || libWorld is null) return;
        var source = KindWorld.IsChecked == true ? libWorld : libItems;
        var q = SearchBox.Text.Trim();
        libShown = source.Where(i =>
                (TrOnly.IsChecked != true || i.TrCheck) &&
                (q.Length == 0 || i.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || i.En.Contains(q, StringComparison.OrdinalIgnoreCase)
                 || i.Tr.Contains(q, StringComparison.OrdinalIgnoreCase) || (i.Id?.Contains(q) ?? false)))
            .ToList();
        LibSub.Text = $"{libItems.Count} items ({libItems.Count(i => i.HasFile)} with icons) · {libWorld.Count} world pictures · showing {libShown.Count}";
        libColumns = 0;
        LibLayout();
        if (selected is null || !libShown.Contains(selected)) Select(libShown.FirstOrDefault());
    }

    void LibLayout()
    {
        var columns = Math.Max(1, (int)((LibList.ActualWidth - 20) / 132));
        if (columns == libColumns && LibList.ItemsSource is not null) return;
        libColumns = columns;
        LibList.ItemsSource = libShown.Chunk(columns).Select(c => new LibRow(c)).ToList();
        if (LibList.Items.Count > 0) LibList.ScrollIntoView(LibList.Items[0]);
    }

    void LibList_SizeChanged(object sender, SizeChangedEventArgs e) => LibLayout();

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchDelay.Stop();
        searchDelay.Start();
    }

    void Kind_Checked(object sender, RoutedEventArgs e) => LibRender();
    void TrOnly_Changed(object sender, RoutedEventArgs e) => LibRender();
    void Reload_Click(object sender, RoutedEventArgs e) { LibLoad(force: true); Toast("Library reloaded", "", "Teal"); }

    void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibItem item) Select(item);
    }

    void Select(LibItem? item)
    {
        if (selected is not null) selected.IsSelected = false;
        selected = item;
        DetailEmpty.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null) return;
        item.IsSelected = true;

        DetailImage.Source = item.File is null ? null : LibItem.Load(item.File, 320);
        DetailNoImage.Visibility = item.HasFile ? Visibility.Collapsed : Visibility.Visible;
        DetailName.Text = item.Name;
        DetailTr.Text = item.Tr;
        DetailTr.Visibility = item.Tr.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailShort.Text = item.Key;
        DetailId.Text = item.Id ?? "";
        DetailUpdated.Text = item.Updated;
        DetailIdLabel.Visibility = DetailId.Visibility = item.Id is null ? Visibility.Collapsed : Visibility.Visible;
        DetailUpdatedLabel.Visibility = DetailUpdated.Visibility = item.Updated.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DetailDesc.Text = item.Desc;
        BtnCopyId.Visibility = item.Id is null ? Visibility.Collapsed : Visibility.Visible;
        BtnOpenPic.Visibility = BtnShowFolder.Visibility = item.HasFile ? Visibility.Visible : Visibility.Collapsed;

        DetailBadges.Children.Clear();
        foreach (var (show, text, key) in new[] { (item.Removed, "REMOVED", "Red"), (item.TrCheck, "TR KONTROL ET", "Yellow"), (!item.HasFile, "NO ICON", "Text2") })
        {
            if (!show) continue;
            var color = ((SolidColorBrush)Brush(key)).Color;
            DetailBadges.Children.Add(new Border
            {
                Style = (Style)FindResource("Badge"),
                Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                Child = new TextBlock { Text = text, Foreground = Brush(key), FontSize = 10.5, FontWeight = FontWeights.Bold },
            });
        }
    }

    void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            Toast($"Copied {text}", "", "Blue");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Toast("Clipboard is busy, try again", "", "Yellow");
        }
    }

    void CopyShort_Click(object s, RoutedEventArgs e) { if (selected is not null) Copy(selected.Key); }
    void CopyId_Click(object s, RoutedEventArgs e) { if (selected?.Id is { } id) Copy(id); }
    void OpenPic_Click(object s, RoutedEventArgs e) { if (selected?.File is { } f) Open(f); }
    void ShowFolder_Click(object s, RoutedEventArgs e) { if (selected?.File is { } f) Process.Start("explorer.exe", $"/select,\"{f}\""); }

    static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (libItems is null) LibLoad(force: true);
        var dialog = new SaveFileDialog { FileName = "rusthelp_items.csv", Filter = "CSV|*.csv", InitialDirectory = settings.Out };
        if (dialog.ShowDialog(this) != true) return;
        static string Cell(string? v) => v is null ? "" : v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        var sb = new StringBuilder("shortname,id,name_en,name_tr,tr_check,removed,icon,description_en\r\n");
        foreach (var i in libItems!)
            sb.Append(string.Join(',', Cell(i.Key), Cell(i.Id), Cell(i.En), Cell(i.Tr), i.TrCheck, i.Removed,
                Cell(i.File is null ? "" : Path.GetFileName(i.File)), Cell(i.Desc))).Append("\r\n");
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        Toast($"Exported {libItems!.Count} items", "", "Green");
    }

    void ChangeOut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = settings.Out };
        if (dialog.ShowDialog(this) != true) return;
        settings.Out = dialog.FolderName;
        settings.Save();
        OutPath.Text = settings.Out;
        libItems = libWorld = null;
        Toast("Output folder changed", "", "Blue");
    }

    void OpenOut_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(settings.Out);
        Open(settings.Out);
    }

    void Toast(string message, string glyph, string colorKey)
    {
        ToastText.Text = message;
        ToastIcon.Text = glyph;
        ToastIcon.Foreground = Brush(colorKey);
        var move = (TranslateTransform)ToastBox.RenderTransform;
        var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var show = new DoubleAnimationUsingKeyFrames();
        show.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), ease));
        show.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        show.KeyFrames.Add(new EasingDoubleKeyFrame(-70, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2550)), new CubicEase { EasingMode = EasingMode.EaseIn }));
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2250))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2550))));
        move.BeginAnimation(TranslateTransform.YProperty, show);
        ToastBox.BeginAnimation(OpacityProperty, fade);
    }
}
