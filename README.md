# AI HOT Desktop

一个安静悬浮在 Windows 桌面上的 AI 新闻小工具，内容来自
[AI HOT](https://aihot.virxact.com)。

它把信息分成两组：

- **当前热点**：AI HOT 当前聚合出的热门话题；
- **今日新闻**：当天更新的精选内容。

窗口会根据新闻数量调整高度，支持拖动、位置记忆、透明度和卡片颜色设置。
点击新闻会在浏览器中打开 AI HOT 原文页面。

## 下载

[下载 AIHotDesktop.exe](https://github.com/KeKeDou810/aihot-desktop/releases/latest/download/AIHotDesktop.exe)

这是一个免安装的 Windows x64 单文件程序。下载到桌面或任意固定位置后，
双击即可运行，不需要管理员权限或预装 .NET。

## 刷新

- 启动时请求一次；
- 每 15 分钟进行一次条件检查；
- 点击刷新按钮会立即检查；
- 两个分区分别缓存，其中一个请求失败不会清空另一个分区。

当前没有服务端事件订阅，因此应用不是秒级实时更新。

## 从源码运行

需要 Windows 10/11 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
git clone https://github.com/KeKeDou810/aihot-desktop.git
cd aihot-desktop
dotnet run --project .\AIHotDesktop.csproj
```

生成同款 Windows x64 便携版：

```powershell
dotnet publish .\AIHotDesktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\portable
```

生成结果为 `artifacts\portable\AIHotDesktop.exe`。当前程序尚未代码签名，
Windows 可能显示“未知发布者”提示。

## 数据与隐私

- 不需要登录；
- 不包含统计或遥测；
- 设置和缓存只保存在 `%LOCALAPPDATA%\AIHotDesktop`；
- 网络请求仅用于读取 AI HOT 内容，新闻链接始终指向 AI HOT。

这是一个独立的个人项目，与 AI HOT 官方没有隶属或背书关系。

## License

代码使用 [MIT License](LICENSE)。AI HOT 数据和第三方新闻内容仍受各自条款约束。
