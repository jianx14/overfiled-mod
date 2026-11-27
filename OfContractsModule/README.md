# OF_SF 区服内容目录

本模块适用 overfiled-mod `d97ad47648f4ac1d6cbd28b225ebdb30a8b3d089`。它在 Mod 已有的 Alt+9 控制台添加 `ofcatalog`。小游戏使用本地已加载模块的公开指令，指南可以由服主增删。原有中国象棋 F9、画板 F8、聊天和房间入口继续使用。区服存档由 OF_SF 管理，小游戏房间、同步、游戏规则和本地表现仍由 Mod 提供。

## 已安装 Mod 的玩家

1. 先退出游戏。在 PowerShell 中进入本模块 ZIP 的解压目录，运行下式，把示例路径改为自己安装 Mod 的目录。安装器只在已有宿主的缓存索引中更新 `ofsf_catalog` 一项，替换时在相邻目录保留上次文件备份。原有 `mod.dll`、游戏安装和原版客户端无需改动。

   ```powershell
   .\Install-Catalog.ps1 -ModsDirectory "D:\游戏副本\Mods"
   ```

2. 在 `%LOCALAPPDATA%\OF_SF` 创建 UTF-8 文件 `mod-catalog.json`，填写服主给的 HTTPS 根地址（含实际端口）。使用系统信任的证书时只需 `origin`。使用区服自建 CA 时加上该服 PEM 证书的绝对路径。

   ```json
   {"origin":"https://realm.example:18443","ca":"D:/区服证书/players-ca.pem"}
   ```

3. 启动已有 Mod 客户端，进入角色，按 **Alt+9** 输入 `ofcatalog`。输入 `ofcatalog open <内容 ID>` 打开列出的项目。服主更新或更换区服后，用 `ofcatalog refresh` 重新载入配置和内容。要停用目录，可在游戏关闭时将缓存 `manifest.json` 中 `ofsf_catalog` 的 `enabled` 设为 `false`。

| 本地入口编号 | 需要的已加载模块 | 行为 |
| --- | --- | --- |
| `jianx14.chess` | `chinese_chess` | 用模块已有的 `xq` 打开象棋面板 |
| `jianx14.draw` | `draw_guess` | 用模块已有的 `dg` 打开画板 |
| `kind: guide` | 内容目录模块 | 在控制台阅读纯文本指南 |

网络读取由玩家输入目录指令触发。模块只向配置的区服读取 `/of-contracts/v1/realm`、`/of-contracts/v1/content`。读取上限为 64 KiB，按原始字节验证 SHA-256，区服发布切换时最多重读一轮。不兼容或临时不可达会显示错误。未知入口被忽略。指南里的尖括号以全角字符显示，Unity 富文本不会解释其内容。扩展可以通过进程环境变量 `OF_SF_REALM_URL`、`OF_SF_REALM_CA` 指定同样配置；设置了 URL 时两者优先于文件。仅本机测试允许环回 HTTP。正式环境使用 HTTPS；指定私有 CA 时验证服务端用途、主机名和证书链，不查询私有 CA 的吊销列表。

宿主通过 `ILogic` 正常加载与卸载模块。卸载立即释放网络请求和目录内容；此版本宿主只有注册本地控制台指令的 API，卸载后 `ofcatalog` 名字可在帮助中保留到退出或模块重新加载。

## 注册自己的小游戏

新模块使用本地 `ILogic` 的模块编号，如 `puzzle`，同时实现 BCL 的 `IServiceProvider`。声明要公开的动作，每个委托只负责一个本地入口。已有模块可以将打开自身面板的方法注册为 `lobby`。模块编号和动作各自用一段小写字母、数字和连字符。

```csharp
public object? GetService(Type contract) =>
    contract == typeof(IReadOnlyDictionary<string, Action>)
        ? new Dictionary<string, Action> { ["lobby"] = OpenPuzzlePanel }
        : null;
```

服主可以添加 `{"id":"games.puzzle","kind":"minigame","title":"拼图","entry":"module.puzzle.lobby"}`。用户执行 `ofcatalog open games.puzzle` 时，目录实时向宿主查询当前加载的 `puzzle`，仅从它显式提供的动作表取 `lobby`。未安装或已卸载时显示不可用；重新加载后使用新实例。服务端与目录模块无需为每个新游戏重新编译。集成验证的 `PuzzlePanel` 示例覆盖缺少模块、尚未注册的动作、加载、卸载与同名重载。

## 开发与验证

源码项目编译时使用已安装宿主的 `mod.dll`，默认路径 `libs/mod.dll`。也可以直接指定其路径。生成的 `OfContractsModule.dll` 在 `GameMods/Mods`，由上游构建工具写入模块清单。已有多模块发布目录应在该目录重新生成完整清单。将适配补丁应用到上游源码时请先在上述固定提交执行 `git apply --check overfiled-mod-of-contracts-v1.patch`，然后用 `git apply` 应用。

```powershell
dotnet build OfContractsModule/OfContractsModule.csproj -c Release -p:ModHost="D:/游戏副本/Mods/mod.dll"
dotnet run --project tools/OfContractsVerification/Verify.csproj -c Release
dotnet run --project tools/OfContractsVerification/Verify.csproj -c Release -- --probe https://realm.example:18443 --ca "D:/区服证书/players-ca.pem"
```

模块目标 .NET 6，适配验证程序目标 .NET 9。验证程序包含 HTTP 协商、清单切换、内容边界、未知项目、纯文本、两个小游戏的指令分发、卸载以及不触发远端聊天输入的检查。这里的分发使用可控宿主夹具，实际游戏的画板和棋桌画面仍需玩家在匹配的 Mod 客户端上验证。
