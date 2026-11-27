using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OfContractsModule;

internal sealed record CatalogItem(string Id, string Title, string? Entry, string? Text);

internal static class RealmCatalog
{
    private const int MaxBytes = 65536;
    private static readonly Regex Name = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Supported = new(@"^0\.1\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);

    public static Uri Origin(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        bool local = uri.Host is "127.0.0.1" or "[::1]" or "localhost";
        if (!(uri.Scheme == "https" || (local && uri.Scheme == "http")) ||
            uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("请输入区服 HTTPS 根地址，可在本机使用环回 HTTP。");
        return uri;
    }

    public static async Task<List<CatalogItem>> FetchAsync(Uri origin, string? caFile, CancellationToken token)
    {
        using var root = string.IsNullOrWhiteSpace(caFile) ? null : X509Certificate2.CreateFromPem(File.ReadAllText(caFile));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        if (root != null)
            handler.ServerCertificateCustomValidationCallback = (_, cert, presented, errors) =>
            {
                if (cert == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // TLS server
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Local private CA without a CRL.
                if (presented != null)
                    foreach (var element in presented.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                return chain.Build(cert);
            };
        using var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = MaxBytes };
        return await ReadAsync(client, token).ConfigureAwait(false);
    }

    internal static async Task<List<CatalogItem>> ReadAsync(HttpClient client, CancellationToken token)
    {
        async Task<byte[]> Get(string name)
        {
            using var response = await client.GetAsync("/of-contracts/v1/" + name, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(MaxBytes).WaitAsync(token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        token = deadline.Token;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var realm = JsonDocument.Parse(await Get("realm").ConfigureAwait(false));
            var info = realm.RootElement;
            var version = info.GetProperty("contract_version").GetString();
            if (version == null || !Supported.IsMatch(version) ||
                info.GetProperty("contract_id").GetString() != "ofsf.extensions.v1" ||
                info.GetProperty("baseline").GetString() != "ofsf.native" ||
                info.GetProperty("client_profile").GetString() != "ofsf.pc.native.v1" ||
                !info.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "content.catalog.v1"))
                throw new InvalidDataException("此区服未提供兼容的内容目录。");
            var expected = info.GetProperty("content_manifest").GetString();
            var bytes = await Get("content").ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), expected, StringComparison.Ordinal))
                continue;
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.GetProperty("contract_version").GetString() != version)
                throw new InvalidDataException("目录和区服声明的版本不同。");
            return Items(doc.RootElement.GetProperty("items"));
        }
        throw new InvalidDataException("内容清单摘要不匹配。");
    }

    private static string Field(JsonElement row, string key, int limit)
    {
        string value = row.GetProperty(key).GetString() ?? "";
        if (string.IsNullOrWhiteSpace(value) || value.EnumerateRunes().Count() > limit ||
            value.Any(c => char.IsControl(c) && !(key == "text" && c == '\n')))
            throw new InvalidDataException("目录字段格式有误：" + key);
        return value;
    }

    internal static bool IsModuleEntry(string entry)
    {
        var parts = entry.Split('.');
        return parts.Length == 3 && parts[0] == "module";
    }

    private static List<CatalogItem> Items(JsonElement entries)
    {
        if (entries.GetArrayLength() > 128) throw new InvalidDataException("目录超过 128 项。");
        var result = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in entries.EnumerateArray())
        {
            var id = Field(row, "id", 96);
            if (!Name.IsMatch(id) || !seen.Add(id)) throw new InvalidDataException("目录 ID 无效或重复。");
            string kind = row.GetProperty("kind").GetString() ?? "";
            if (kind is not ("guide" or "minigame")) continue;
            var title = Field(row, "title", 80);
            if (kind == "guide")
                result.Add(new(id, title, null, Field(row, "text", 2048)));
            else
            {
                var entry = Field(row, "entry", 96);
                if (!Name.IsMatch(entry)) throw new InvalidDataException("小游戏入口格式有误。");
                if (entry is "jianx14.chess" or "jianx14.draw" || IsModuleEntry(entry)) result.Add(new(id, title, entry, null));
            }
        }
        return result;
    }
}
