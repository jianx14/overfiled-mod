// Minimal module/UI fixture. The deployable module references the upstream mod.dll.
namespace ModLoader
{
    public interface ILogic { string Name {get;} string Version {get;} void OnLoad(); void OnUpdate(); void OnUnload(); }
    public sealed class LogicLoader
    {
        internal HashSet<string> Modules { get; } = new();
        internal Dictionary<string,ILogic> Loaded { get; } = new();
        public ILogic? GetModule(string id) => Loaded.GetValueOrDefault(id);
        public static LogicLoader Instance { get; } = new();
        public bool HasModule(string id) => Modules.Contains(id);
    }
}
internal sealed class PuzzlePanel(Action open) : ModLoader.ILogic, IServiceProvider
{
    public string Name => "PuzzlePanel";
    public string Version => "1.0.0";
    public object? GetService(Type type) => type == typeof(IReadOnlyDictionary<string,Action>)
        ? new Dictionary<string,Action> { ["lobby"] = open } : null;
    public void OnLoad() {}
    public void OnUpdate() {}
    public void OnUnload() {}
}
namespace mod
{
    public sealed class TipMsgCore
    {
        public static TipMsgCore i = new();
        public string mainplayername = "tester";
        public static event Action<string,string>? Tip_actions;
        public List<string> Commands { get; } = new();
        public void updatecommand(string name,string text) { Commands.Add(text); Tip_actions?.Invoke(name,text); }
    }
}
public sealed class MsgUI
{
    public static MsgUI i = new();
    internal List<string> Messages { get; } = new();
    internal Dictionary<string,Action<string[]>> Local {get;} = new();
    public void RegisterCommand(string name, Action<string[]> callback) => Local[name]=callback;
    public void Log(string message) => Messages.Add(message);
}
