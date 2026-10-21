using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustWikiScraper;

public sealed record LogLine(string Text, Brush Brush, bool Problem);

public sealed class LibItem : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public string? File { get; init; }
    public string En { get; init; } = "";
    public string Tr { get; init; } = "";
    public string? Id { get; init; }
    public string Desc { get; init; } = "";
    public string Updated { get; init; } = "";
    public bool Removed { get; init; }
    public bool TrCheck { get; init; }

    public string Name => En.Length > 0 ? En : Key;
    public bool HasFile => File is not null;
    public Visibility FileVisible => HasFile ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoFileVisible => HasFile ? Visibility.Collapsed : Visibility.Visible;
    public Brush NameBrush => (Brush)Application.Current.Resources[Removed ? "Red" : TrCheck ? "Yellow" : "Text2"];

    bool selected;
    public bool IsSelected
    {
        get => selected;
        set { if (selected != value) { selected = value; Changed(); } }
    }

    ImageSource? thumb;
    bool loading;

    public ImageSource? Thumb
    {
        get
        {
            if (thumb is null && !loading && File is { } path)
            {
                loading = true;
                Task.Run(() => Load(path, 96)).ContinueWith(t =>
                {
                    thumb = t.Result;
                    Changed();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            return thumb;
        }
    }

    public static ImageSource? Load(string path, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record LibRow(IReadOnlyList<LibItem> Items);
