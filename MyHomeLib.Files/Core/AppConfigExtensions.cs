using MonoTorrent;

public static class AppConfigExtensions
{
    public static string DataFolder(this AppConfig config)
    {
        return config.CacheDirectory.EnsureExists();
    }

    private static string EnsureExists(this string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    public static string CacheDirectory(this AppConfig config) =>
        Path.Combine(config.DataFolder(), ".cache").EnsureExists();

    public static string BooksDirectory(this AppConfig config, string infoHash, string archive, string? bookName = null)
    {
        var booksDirectory = Path.Combine(config.TorrentsFolder(infoHash)).EnsureExists();
        return bookName != null
            ? Path.Combine(booksDirectory, bookName)
            : booksDirectory;
    }

    public static string TorrentsFolder(this AppConfig config, string subPath = null)
    {
        var folder = Path.Combine(config.DataFolder(), "torrents").EnsureExists();

        return subPath == null ? folder : Path.Combine(folder, subPath).EnsureExists();
    }

    public static string TorrentPath(this AppConfig config, InfoHash hash) =>
        Path.Combine(config.TorrentsFolder(), $"{hash.ToHex()}.torrent");
}