using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using mod;
using ModLoader;
using OfContractsModule;

if (args.Length > 0)
{
    if (!(args.Length is 2 or 4) || args[0] != "--probe" || (args.Length == 4 && args[2] != "--ca"))
        throw new ArgumentException("--probe ORIGIN [--ca CA.pem]");
    try
    {
        var result = await RealmCatalog.FetchAsync(RealmCatalog.Origin(args[1]), args.Length == 4 ? args[3] : null, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Discovery failed: " + ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
const string version = "0.1.0";
var rows = new object[] {
    new { id = "ofsf.guide", kind = "guide", title = "指南", text = "您好\n<a>示例</a>" },
    new { id = "other.alias", kind = "minigame", title = "对弈", entry = "jianx14.chess" },
    new { id = "jianx14.draw", kind = "minigame", title = "绘画", entry = "jianx14.draw" },
    new { id = "unknown.future", kind = "future" },
    new { id = "games.puzzle", kind = "minigame", title = "拼图", entry = "module.puzzle.lobby" },
    new { id = "games.unknown", kind = "minigame", title = "无本地动作", entry = "module.puzzle.unknown" },
    new { id = "unknown.game", kind = "minigame", title = "未装", entry = "other.unknown" }
};
byte[] Content(object[] items, string revision = version) => JsonSerializer.SerializeToUtf8Bytes(new { contract_version = revision, items });
byte[] Realm(byte[] content, string revision = version, bool available = true) => JsonSerializer.SerializeToUtf8Bytes(new {
    contract_id = "ofsf.extensions.v1", contract_version = revision, baseline = "ofsf.native", client_profile = "ofsf.pc.native.v1",
    ruleset_revision = (string?)null, content_manifest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
    capabilities = available ? new[] {"content.catalog.v1"} : Array.Empty<string>()
});
var data = Content(rows);
var metadata = Realm(data);
async Task<List<CatalogItem>> Read(params byte[][] bodies)
{
    var fake = new Responses(bodies);
    using var http = new HttpClient(fake) { BaseAddress = new Uri("https://example.test") };
    return await RealmCatalog.ReadAsync(http, CancellationToken.None);
}
async Task Reject(params byte[][] bodies)
{
    try { await Read(bodies); }
    catch (Exception e) when (e is InvalidDataException or HttpRequestException or JsonException) {return;}
    throw new Exception("expected rejection");
}
Check((await Read(metadata, data)).Count == 5, "known data");
Check((await Read(metadata, Content(Array.Empty<object>()), metadata, data)).Count == 5, "rotating manifest");
await Reject(metadata, Content([]), metadata, Content([]));
await Reject(Realm(data, "1.2.0"));
await Reject(Realm(data, available:false));
var mismatch = Content(rows, "0.1.1"); await Reject(Realm(mismatch), mismatch);
var duplicate = Content([rows[0],rows[0]]); await Reject(Realm(duplicate), duplicate);
var giant = new byte[65537]; await Reject(Realm(giant), giant);
foreach (var address in new[]{"http://example.test","https://user@example.test","https://example.test/root","https://example.test?x=1"})
{
    try { RealmCatalog.Origin(address); throw new Exception("bad origin accepted"); }
    catch (FormatException) {}
}

// Exercise the actual optional module, a loopback HTTP server and both allowlisted native Mod commands.
using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);
listener.Start();
var addr = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
Environment.SetEnvironmentVariable("OF_SF_REALM_URL", addr);
Environment.SetEnvironmentVariable("OF_SF_REALM_CA", null);
using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
async Task Serve()
{
    foreach (var part in new[]{("realm",metadata),("content",data)})
    {
        using var tcp = await listener.AcceptTcpClientAsync(quit.Token);
        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        string? line = await reader.ReadLineAsync(quit.Token);
        Check(line == "GET /of-contracts/v1/" + part.Item1 + " HTTP/1.1", "request route");
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(quit.Token))) {}
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: " + part.Item2.Length + "\r\n\r\n");
        await stream.WriteAsync(head,quit.Token); await stream.WriteAsync(part.Item2,quit.Token);
    }
}
var serving = Serve();
var module = new Entry();
module.OnLoad();
void User(string text) => MsgUI.i.Local["ofcatalog"](text.Split(' ').Skip(1).ToArray());
User("ofcatalog");
while (!MsgUI.i.Messages.Any(m => m.Contains("ofsf.guide")))
{
    module.OnUpdate(); await Task.Delay(10,quit.Token);
}
await serving;
User("ofcatalog open ofsf.guide");
Check(MsgUI.i.Messages.Last().Contains("＜a＞"),"guide display");
User("ofcatalog open other.alias"); Check(MsgUI.i.Messages.Last().Contains("安装"),"missing module");
LogicLoader.Instance.Modules.UnionWith(["chinese_chess","draw_guess"]);
User("ofcatalog open other.alias"); Check(TipMsgCore.i.Commands.Last()=="xq","local chess");
User("ofcatalog open jianx14.draw"); Check(TipMsgCore.i.Commands.Last()=="dg","local draw");
int first = 0, second = 0;
void OpenPuzzle() => User("ofcatalog open games.puzzle");
OpenPuzzle(); Check(MsgUI.i.Messages.Last().Contains("尚未加载"), "provider absent");
LogicLoader.Instance.Loaded["puzzle"] = new PuzzlePanel(() => first++);
OpenPuzzle(); Check(first == 1, "local third-party panel");
User("ofcatalog open games.unknown"); Check(first == 1 && MsgUI.i.Messages.Last().Contains("尚未加载"), "unregistered intent");
LogicLoader.Instance.Loaded.Remove("puzzle");
OpenPuzzle(); Check(first == 1, "provider unloaded");
LogicLoader.Instance.Loaded["puzzle"] = new PuzzlePanel(() => second++);
OpenPuzzle(); Check(first == 1 && second == 1, "reloaded provider");
int before = MsgUI.i.Messages.Count;
TipMsgCore.i.updatecommand("other","ofcatalog"); Check(before == MsgUI.i.Messages.Count,"foreign event");
module.OnUnload(); User("ofcatalog"); Check(before == MsgUI.i.Messages.Count,"unloaded");
Console.WriteLine("PASS HTTP discovery, rotation, bounds, unknown items, guide, both local commands, standalone unload, third-party extension install/unload/reload");

internal sealed class Responses(byte[][] responses) : HttpMessageHandler
{
    private int i;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
    {
        string name = i % 2 == 0 ? "realm" : "content";
        if (request.RequestUri?.AbsolutePath != "/of-contracts/v1/"+name) throw new Exception("route mismatch");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(responses[i++]) });
    }
}
