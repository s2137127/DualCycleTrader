namespace DualCycleTrader.Data;

public static class AppDataPaths
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public static string Root
    {
        get
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            EnsureInitialized(root);
            return root;
        }
    }

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string StockUniverse => Path.Combine(Root, "stock-universe.json");
    public static string MarketState => Path.Combine(Root, "market-state.json");
    public static string MarketCache => Path.Combine(Root, "MarketCache");

    private static void EnsureInitialized(string root)
    {
        lock (Sync)
        {
            if (_initialized) return;
            Directory.CreateDirectory(root);

            // Preserve data created by older versions that used %LocalAppData%.
            string oldRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DualCycleTrader");
            CopyIfMissing(Path.Combine(oldRoot, "settings.json"), Path.Combine(root, "settings.json"));
            CopyIfMissing(Path.Combine(oldRoot, "stock-universe.json"), Path.Combine(root, "stock-universe.json"));

            string oldCache = Path.Combine(oldRoot, "MarketCache");
            string newCache = Path.Combine(root, "MarketCache");
            Directory.CreateDirectory(newCache);
            if (Directory.Exists(oldCache))
            {
                foreach (string source in Directory.EnumerateFiles(oldCache))
                    CopyIfMissing(source, Path.Combine(newCache, Path.GetFileName(source)));
            }

            _initialized = true;
        }
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(source) && !File.Exists(destination))
            File.Copy(source, destination);
    }
}
