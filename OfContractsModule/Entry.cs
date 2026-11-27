using ModLoader;
using mod;

namespace OfContractsModule;

/// <summary>Optional catalog. Unity-facing calls run on the module update thread.</summary>
public sealed class Entry : ILogic
{
    public string Name => "OF_SF 内容目录";
    public string Version => "0.1.0";

    private CancellationTokenSource? lifetime;
    private Task<List<CatalogItem>>? loading;
    private List<CatalogItem> items = new();

    public void OnLoad()
    {
        if (lifetime != null) return;
        lifetime = new CancellationTokenSource();
        MsgUI.i.RegisterCommand("ofcatalog", OnLocalCommand);
    }

    public void OnUnload()
    {
        // The published host only has RegisterCommand. Replace its delegate with a runtime-owned
        // no-op so collectible modules can unload and a later version can reclaim the alias.
        MsgUI.i.RegisterCommand("ofcatalog", TextWriter.Null.WriteLine);
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        // The task owns its HTTP resources; observe errors if it finishes after unload.
        if (loading != null)
            _ = loading.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        loading = null;
        items.Clear();
    }

    public void OnUpdate()
    {
        if (loading == null || !loading.IsCompleted) return;
        var done = loading;
        loading = null;
        try { items = done.GetAwaiter().GetResult(); ListItems(); }
        catch (Exception e) { Log("目录读取失败：" + e.Message); }
    }

    private void OnLocalCommand(string[] parts)
    {
        if (lifetime == null) return;
        if (parts.Length == 0 || (parts.Length == 1 && parts[0] == "refresh"))
        {
            if (loading != null) { Log("正在读取目录。"); return; }
            if (items.Count > 0 && parts.Length == 0) { ListItems(); return; }
            // Never launch content from the previously selected origin after a refresh.
            items.Clear();
            try
            {
                var settings = LocalSettings.Read();
                loading = RealmCatalog.FetchAsync(settings.Origin, settings.Ca, lifetime.Token);
            }
            catch (Exception e) { Log("读取区服设置失败：" + e.Message); }
            return;
        }
        if (parts.Length != 2 || parts[0] != "open") { Log("ofcatalog | ofcatalog refresh | ofcatalog open <内容 ID>"); return; }
        var item = items.Find(i => i.Id == parts[1]);
        if (item == null) { Log("未找到此内容，请先读取目录。"); return; }
        if (item.Text != null) { Log(item.Title + "\n" + item.Text); return; }

        if (item.Entry != null && RealmCatalog.IsModuleEntry(item.Entry))
        {
            var partsOfEntry = item.Entry.Split('.');
            try
            {
                // Resolve on click: every hot reload may supply a different instance or withdraw an action.
                var module = LogicLoader.Instance.GetModule(partsOfEntry[1]) as IServiceProvider;
                if (module?.GetService(typeof(IReadOnlyDictionary<string,Action>)) is IReadOnlyDictionary<string,Action> actions
                    && actions.TryGetValue(partsOfEntry[2], out var open) && open != null) open();
                else Log("此模块尚未加载或未注册对应入口。");
            }
            catch (Exception ex) { Log("模块入口执行失败：" + ex.Message); }
            return;
        }

        // Only locally compiled actions can dispatch. The baseline never supplies a command.
        var action = item.Entry switch
        {
            "jianx14.chess" => ("chinese_chess", "xq"),
            "jianx14.draw" => ("draw_guess", "dg"),
            _ => default
        };
        if (action.Item1 == null || !LogicLoader.Instance.HasModule(action.Item1))
        { Log("请安装并启用对应的本地小游戏模块。"); return; }
        var host = TipMsgCore.i;
        if (string.IsNullOrEmpty(host?.mainplayername)) { Log("请先进入角色。"); return; }
        host.updatecommand(host.mainplayername, action.Item2);
    }

    private void ListItems()
    {
        Log(items.Count == 0 ? "区服目录为空。" : "使用 ofcatalog open <内容 ID> 打开");
        foreach (var item in items) Log(item.Id + "  " + item.Title);
    }

    // The existing console renders Unity rich text. Display remote markup literally.
    private static void Log(string message) => MsgUI.i.Log(message.Replace("<", "＜").Replace(">", "＞"));
}
