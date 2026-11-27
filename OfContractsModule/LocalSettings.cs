using System.Text.Json;

namespace OfContractsModule;

internal static class LocalSettings
{
    public static (Uri Origin, string? Ca) Read()
    {
        var address = Environment.GetEnvironmentVariable("OF_SF_REALM_URL");
        if (!string.IsNullOrWhiteSpace(address))
            return (RealmCatalog.Origin(address), Environment.GetEnvironmentVariable("OF_SF_REALM_CA"));
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OF_SF", "mod-catalog.json");
        if (!File.Exists(path)) throw new InvalidDataException("请设置 OF_SF_REALM_URL 或配置 mod-catalog.json。");
        using var file = File.OpenRead(path);
        if (file.Length > 8192) throw new InvalidDataException("本地目录配置过大。");
        using var data = JsonDocument.Parse(file);
        string url = data.RootElement.GetProperty("origin").GetString() ?? "";
        string? ca = data.RootElement.TryGetProperty("ca", out var value) ? value.GetString() : null;
        if (!string.IsNullOrWhiteSpace(ca) && !Path.IsPathFullyQualified(ca))
            throw new InvalidDataException("ca 需为 PEM 文件的绝对路径。");
        return (RealmCatalog.Origin(url), ca);
    }
}
