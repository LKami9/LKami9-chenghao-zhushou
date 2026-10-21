# 工单：LKami9称号助手 v1.1.6 —— 修 IME 候选栏主题 + 窗口压住 Omni 主体

> 执行者：codex
> 仓库：`_splatoon/lk_title/`（`git@github.com:LKami9/LKami9-chenghao-zhushou`，main）
> 唯一要改的代码文件：`Modules/LKami9TitleHelper.cs`（742 行 / 26798 B，UTF-8 + **LF**）
> 交付物：改好的 `module.cs`、更新的 `TreeHouseModules.json`、`dotnet build` 通过、验证记录

---

## 0. 一句话任务

在这一个文件里做两组改动：

1. 输入框换成 Omni 原生控件，并给 **Dalamud IME 候选栏**加一个「全局色桥」（只在输入框激活时生效、下一帧还原）；
2. 让独立窗口**贴在 Omni 主窗口侧边而不是压在上面**，并在 ImGui 层级上排到主窗口后面、点击不再抢到最前。

---

## 1. 非目标（明确禁止）

- 不改 `OmniThemeScope` 的 push/pop 结构（L597-678 现在是正确的，32 色 + 8 变量，Dispose 按计数弹回）。
- 不 hook / patch `Dalamud.Interface.Internal.DalamudIme`（internal 类型，动它就是改内存，禁止）。
- 不整体覆写全局 ImGui 样式（`style.Colors[*]` 全量改 = 坑③ 换姿势重来）。
- 不动 `Lumina` / `FFXIVClientStructs` 的数据读取逻辑、不改过滤/佩戴/分页行为。
- 不引入 `ImRaii`（在线模块 Roslyn 环境缺 `Dalamud.Bindings.ImPlot`，见 HANDOFF 坑⑤）。
- 不新增第三方包；`csproj` 不动。
- 不改行尾为 CRLF；不改编码。

---

## 2. 环境（已确认，直接用）

| 项 | 值 |
|---|---|
| 编译 | `cd _splatoon/lk_title && dotnet build -v q`（net10.0-windows，`AllowUnsafeBlocks=true`） |
| 引用 | Dalamud / ImGui / CS / Lumina 在 `D:\XIVLauncherCN\XIVLauncherCN\addon\Hooks\dev\` |
| Omni 程序集 | `D:\XIVLauncherCN\XIVLauncherCN\installedPlugins\OmniToolbox\1.1.7.8\`（`OmniToolbox.dll` / `OmniToolbox.Common.dll`） |
| 反编译工具 | `~/.dotnet/tools/ilspycmd.exe -t <全类型名> <dll>`（必要时查签名） |
| 同作者参考实现 | `D:\XIVLauncherCN\XIVLauncherCN\pluginConfigs\OmniToolbox\TreeHouseOnline\LKami9_LKami9-huanhua-zhushou-CEA09C88\LKami9GlamourHelper\module.cs`（独立窗口 + popup 背景 + 候选位摆放的正确写法） |

### 已逆向确认的 API（不要再怀疑，直接调）

```csharp
// 命名空间均已 using：Dalamud.Bindings.ImGui / OmniToolbox.UI / OmniToolbox.UI.Theme /
//                      OmniToolbox.UI.Controls（OmniControls 在 OmniToolbox.Common.dll 里）

ImGui.GetStyleColorVec4(ImGuiCol idx) -> Vector4*          // 指向全局色表，可直接写 *p = c
ImGui.GetCurrentContext()             -> ImGuiContextPtr    // .Handle->Windows / .Handle->FrameCount
ImGuiP.GetCurrentWindow()             -> ImGuiWindowPtr     // Begin 内部调用
ImGuiP.BringWindowToDisplayBehind(ImGuiWindowPtr window, ImGuiWindowPtr aboveWindow)
ImGuiP.BringWindowToDisplayBack(ImGuiWindowPtr window)
ImGuiP.FindWindowByName(ImU8String name)

ImGuiWindowPtr:  .Handle->Name (byte*, UTF-8) / .Handle->Pos / .Handle->Size /
                 .Handle->Hidden / .Handle->IsFallbackWindow / .Handle->ParentWindow /
                 .Handle->LastFrameActive / .IsNull
ImGuiContext:    .Windows (ImVector<ImGuiWindowPtr>, 有 Count / this[int]) / .FrameCount

OmniControls.InputTextWithHint(string id, string hint, ref string value, int maxLength, float width) -> bool
OmniControls.DrawPopupBackground()
OmniTheme.PopupPadding() -> Vector2
OmniTheme.ClampWindowPosition(Vector2 position, Vector2 size) -> Vector2
OmniTheme.Tokens / OmniTheme.Scale(float)
OmniWindowChrome.SetNextWindowPosition(string windowName, Vector2 position)   // 带坐标的重载，内部会 clamp
```

> Omni 程序集字符串被混淆加密，**二进制里 grep 不到主窗口的 `###xxx` 名字**，所以第 4 步必须运行期扫 `Windows` 列表找，不要硬编码。

---

## 3. Bug ①：输入框候选栏没吃 Omni 主题

### 3.1 根因（结论，别再重复验证）

- 候选栏是 **Dalamud 自己的 IME 浮层** `Dalamud.Interface.Internal.DalamudIme`，画在 viewport 的 **前景 draw list** 上，颜色取 `ImGui.GetColorU32(ImGuiCol.WindowBg / Text / NavHighlight / Separator)`，内边距取 `ImGui.GetStyle().WindowPadding` —— 全部是**全局**值，且在 `InterfaceManager.Draw` 链上读取，**不在任何窗口的 push 作用域内**。
- 因此 L381-382 `themeScope.PushAll()` 那 32 个颜色对它无效；这不是推少/推晚的问题。
- 另：L474 用的是裸 `ImGui.InputTextWithHint`，框体本身也没走 Omni 控件。

### 3.2 改动 A-1：输入框换 Omni 控件

原（L471-478 附近）：

```csharp
// ── 工具条 ──
ImGui.SetNextItemWidth(OmniTheme.Scale(260f));
var s = this.search;
if (ImGui.InputTextWithHint("###titlesearch", "搜索称号 / 成就 / 分类 / ID", ref s, 120))
{
    this.search = s;
    this.Rebuild();
}
```

改为：

```csharp
// ── 工具条 ──
var s = this.search;
if (OmniControls.InputTextWithHint("###titlesearch", "搜索称号 / 成就 / 分类 / ID",
        ref s, 120, OmniTheme.Scale(260f)))
{
    this.search = s;
    this.Rebuild();
}

this.inputActive = ImGui.IsItemActive();   // 供 IME 色桥判定，必须紧跟在输入框之后
```

新增字段（放在 `private int unlockedCount;` 附近）：

```csharp
private bool inputActive;
```

### 3.3 改动 A-2：IME 全局色桥（只覆写 4 个色，打字时生效，下一帧还原）

新增成员（放在 `#region 窗口` 内）：

```csharp
private static readonly ImGuiCol[] ImeCols =
{
    ImGuiCol.WindowBg, ImGuiCol.Text, ImGuiCol.NavHighlight, ImGuiCol.Separator,
};

private readonly System.Numerics.Vector4[] imeSaved = new System.Numerics.Vector4[4];
private bool imePaletteActive;

/// <summary>
/// Dalamud 的 IME 候选栏画在 viewport 前景层、读全局 style，
/// 窗口内的 PushStyleColor 结构上够不到它，所以只能直接改全局 4 个色值，
/// 并在下一帧开头原样写回（不是 push/pop 泄漏，是可逆的一次性覆写）。
/// </summary>
private unsafe void ApplyImePalette(bool on)
{
    if (!on)
    {
        if (!this.imePaletteActive) return;

        for (var i = 0; i < ImeCols.Length; i++)
            *ImGui.GetStyleColorVec4(ImeCols[i]) = this.imeSaved[i];

        this.imePaletteActive = false;
        return;
    }

    if (this.imePaletteActive) return;

    var t    = OmniTheme.Tokens;
    var back = Opaque(t.Background);
    var vals = new[]
    {
        back,                              // 候选条底
        Readable(t.Text, back),            // 候选字
        Readable(t.Accent, back),          // 当前选中项
        t.Border,                          // 组合串下的分隔线
    };

    for (var i = 0; i < ImeCols.Length; i++)
    {
        var p = ImGui.GetStyleColorVec4(ImeCols[i]);
        this.imeSaved[i] = *p;
        *p = vals[i];
    }

    this.imePaletteActive = true;
}
```

调用点三处：

1. `DrawWindow()` 最开头（`if (!this.config.WindowOpen) return;` 之后）：`this.ApplyImePalette(false);`
2. `ImGui.End()`（L445）之后、`if (!open) this.config.WindowOpen = false;` 之前：
   `this.ApplyImePalette(this.config.WindowOpen && this.inputActive);`
3. `OnDisable()` 里追加：`this.ApplyImePalette(false);`

### 3.4 改动 A-3（顺手，防复发）

`PushAll()` 把 `WindowPadding` 推成 0，所以本窗口内任何 popup（combo/右键/自定义弹层）会贴边、像默认样式。以后新增 popup 时按幻化助手写法：

```csharp
ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, OmniTheme.PopupPadding());
try { /* BeginPopup / BeginCombo */ OmniControls.DrawPopupBackground(); ... }
finally { ImGui.PopStyleVar(); }
```

本次只要在 `DrawContent` 现有代码里**不加** popup 即可，不需要额外改。

### 3.5 必须实测：洋红探针（判定色桥是否成立）

1. 临时把 `vals` 全写成 `new System.Numerics.Vector4(1f, 0f, 1f, 1f)`。
2. 重载 OmniToolbox（停用模块 → 重载 → 更新 → 启用），开窗口，聚焦搜索框，打拼音。
3. 判定：
   - 候选条底色变**洋红** → 色桥成立（说明 `DalamudIme.Draw` 在插件窗口之后执行），改回主题色，进入验收。
   - **不变** → IME 在插件之前绘制，同帧覆写无效。此时**不要**改成"窗口打开期间持续全局覆写"（会污染卫月其它窗口），改为：保留 A-1（输入框美观对齐），把色桥代码删掉，在交付说明里写明「候选栏是 Dalamud internal 浮层，第三方模块无法安全着色」，并列出可选项（升级 Dalamud / 提 issue）。

---

## 4. Bug ②：窗口压在 Omni 主体上

### 4.1 根因

1. L376 `OmniWindowChrome.SetNextWindowPosition(WindowId)` 吃掉 Omni 记忆的位置（语义是"摆在主窗口那一带"），窗口矩形与主窗口重叠。
2. 我们在 `OnEnable` 里才 `UiBuilder.Draw += DrawWindow`，订阅晚于 Omni 自己的窗口；ImGui 按 `g.Windows` 顺序出图 → 后 Begin 的压在上面。
3. L386-388 的 flags 没有 `NoBringToFrontOnFocus`，点一下就被 `FocusWindow` 提到最前。

### 4.2 改动 B-1：运行期找 Omni 主窗口

新增方法：

```csharp
/// <summary>扫当前 ImGui 上下文，取名字含 Omni、本帧活跃、面积最大的根窗口。</summary>
private static unsafe ImGuiWindowPtr FindOmniMainWindow()
{
    var ctx   = ImGui.GetCurrentContext();
    var list  = ctx.Handle->Windows;
    var frame = ctx.Handle->FrameCount;

    var best = ImGuiWindowPtr.Null;
    for (var i = 0; i < list.Count; i++)
    {
        var w = list[i];
        if (w.IsNull) continue;
        if (w.Handle->Hidden || w.Handle->IsFallbackWindow) continue;
        if (w.Handle->ParentWindow  != null) continue;          // 只要根窗口
        if (w.Handle->LastFrameActive != frame) continue;       // 排除已关窗口残留

        var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)w.Handle->Name) ?? string.Empty;
        if (!name.Contains("Omni", StringComparison.OrdinalIgnoreCase)) continue;

        if (best.IsNull ||
            w.Handle->Size.X * w.Handle->Size.Y > best.Handle->Size.X * best.Handle->Size.Y)
            best = w;
    }

    return best;
}
```

### 4.3 改动 B-2：贴边定位（右 → 左 → 下 → 视口右下）

```csharp
private System.Numerics.Vector2 ComputeDockedPosition(ImGuiWindowPtr omni, System.Numerics.Vector2 mySize)
{
    var vp  = ImGui.GetMainViewport();
    var gap = OmniTheme.Scale(12f);

    if (omni.IsNull)
        return OmniTheme.ClampWindowPosition(
            vp.WorkPos + vp.WorkSize - mySize - new System.Numerics.Vector2(gap, gap), mySize);

    var oPos = omni.Handle->Pos;
    var oSz  = omni.Handle->Size;

    var candidates = new[]
    {
        new System.Numerics.Vector2(oPos.X + oSz.X + gap, oPos.Y),                 // 右
        new System.Numerics.Vector2(oPos.X - mySize.X - gap, oPos.Y),              // 左
        new System.Numerics.Vector2(oPos.X, oPos.Y + oSz.Y + gap),                 // 下
    };

    foreach (var cand in candidates)
    {
        var clamped = OmniTheme.ClampWindowPosition(cand, mySize);
        if (MathF.Abs(clamped.X - cand.X) < 1f && MathF.Abs(clamped.Y - cand.Y) < 1f)
            return clamped;                          // 这一侧真放得下
    }

    return OmniTheme.ClampWindowPosition(candidates[0], mySize);   // 都放不下就贴右边挤
}
```

### 4.4 改动 B-3：接进 `DrawWindow`

原 L366-376：

```csharp
if (this.resizePending) { ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always); this.resizePending = false; }
else if (!this.collapsed) { ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(this.expandedSize), ImGuiCond.FirstUseEver); }

OmniWindowChrome.SetNextWindowPosition(WindowId);
```

改为：

```csharp
if (this.resizePending) { ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always); this.resizePending = false; }
else if (!this.collapsed) { ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(this.expandedSize), ImGuiCond.FirstUseEver); }

var omni = FindOmniMainWindow();
var pos  = this.config.DockToOmni
    ? this.ComputeDockedPosition(omni, targetSize)
    : OmniTheme.ClampWindowPosition(this.lastPos, targetSize);

OmniWindowChrome.SetNextWindowPosition(WindowId, pos);   // 带坐标的重载，不要再只传名字
ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
this.lastPos = pos;
```

新增字段：

```csharp
private System.Numerics.Vector2 lastPos;
```

配置类 `LKami9TitleHelperConfig` 追加：

```csharp
public bool DockToOmni { get; set; } = true;
```

`DrawSettings()` 里加一个 checkbox（放在"启用时自动扫描称号"下面）：

```csharp
var dock = this.config.DockToOmni;
if (ImGui.Checkbox("窗口贴靠 Omni 主窗口", ref dock)) this.config.DockToOmni = dock;
```

> 自由拖动模式（`DockToOmni=false`）本次仍用 `ImGuiCond.Always` + `lastPos`，先保证"不压主窗口"这一条；真正的拖动记忆可留到 1.1.7。

### 4.5 改动 B-4：z 序

1. `ImGui.Begin` 的 flags 加 `ImGuiWindowFlags.NoBringToFrontOnFocus`。
2. `Begin` 返回 true 时第一行取自己：`var self = ImGuiP.GetCurrentWindow();`（变量声明在 `Begin` 外，初值 `ImGuiWindowPtr.Null`）。
3. `ImGui.End()` 之后：

```csharp
if (!self.IsNull)
{
    if (!omni.IsNull) ImGuiP.BringWindowToDisplayBehind(self, omni);
    else              ImGuiP.BringWindowToDisplayBack(self);
}
```

> `BringWindowToDisplayBehind` 只重排 `g.Windows`，在 `ImGui.Render` 之前调用即生效；我们本来就在 Omni 之后 Begin，时序天然满足。

---

## 5. 改完的自检（顺序执行，全绿才算完）

```bash
cd _splatoon/lk_title

# 1) 编译
dotnet build -v q

# 2) 行尾必须 LF（Windows 下 CRLF 会让清单哈希对不上）
file Modules/LKami9TitleHelper.cs          # 期望 "UTF-8 text"（不能出现 "with CRLF line terminators"）
git diff --stat                            # 期望只有 Modules/LKami9TitleHelper.cs 与 TreeHouseModules.json

# 3) push/pop 配平：OmniThemeScope 之外不允许出现裸 PushStyleColor/PopStyleColor 不成对
grep -n "PushStyleColor\|PopStyleColor" Modules/LKami9TitleHelper.cs

# 4) 不允许出现被删掉的关键平衡
grep -n "PushStyleVar\|PopStyleVar" Modules/LKami9TitleHelper.cs
```

期望：`PushAll()` 内仍是 32 个 `this.Color(` + 8 个 `this.Var(`；`Dispose()` 仍是按计数弹；`ImGui.Begin/End`、`BeginChild/EndChild`、`PushID/PopID`、`Indent/Unindent`、`BeginDisabled/EndDisabled` 全部成对。

---

## 6. 版本与清单（v1.1.5 → v1.1.6）

```bash
python - <<'PY'
import hashlib, json, pathlib
p = pathlib.Path("Modules/LKami9TitleHelper.cs")
data = p.read_bytes().replace(b"\r\n", b"\n")
p.write_bytes(data)                                  # 强制 LF 落盘
sha = hashlib.sha256(data).hexdigest().upper()

m = pathlib.Path("TreeHouseModules.json")
j = json.loads(m.read_text(encoding="utf-8"))
mod = j["Modules"][0]
mod["Version"] = "1.1.6"
mod["Sha256"]  = sha
m.write_text(json.dumps(j, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print("bytes =", len(data), "sha256 =", sha)
PY

git show HEAD:Modules/LKami9TitleHelper.cs | wc -c   # 与上面 bytes 对比，必须一致
```

提交信息格式：

```
v1.1.6：输入框改用 Omni 控件并桥接 IME 候选栏配色；独立窗口贴靠主窗口侧边、层级降到主窗口之后
```

---

## 7. 交付格式（codex 必须回）

1. `git diff` 全文（只允许两个文件）。
2. `dotnet build` 的完整输出尾部（Success / 0 Warning 0 Error）。
3. 新的 `bytes` + `sha256`，以及 `git show HEAD:... | wc -c` 的一致性结论。
4. 洋红探针结论：成立 / 不成立（若不成立，说明删了哪些行）。
5. 三条自检 grep 的输出。

---

## 8. 验收清单（给用户在游戏里点）

- [ ] 搜索框外观与 Omni 本体输入框一致（圆角、底色、内边距）
- [ ] 打拼音时候选条是 Omni 主题色（洋红测试先过）
- [ ] 点我们的窗口，Omni 主窗口不再被压到下面
- [ ] 拖动 Omni 主窗口 → 我们的窗户贴着侧边跟着走，不重叠
- [ ] 关掉 Omni 主窗口后我们的窗口仍可用（退回视口右下）
- [ ] 关模块后卫月其它窗口配色立刻回原样
- [ ] 佩戴按钮、过滤、分页、折叠、关闭全部照旧

---

## 9. 失败回退

| 情况 | 处理 |
|---|---|
| `ImGuiP` 在在线模块 Roslyn 里编译不过 | 先保留 B-1/B-2/B-3（纯定位，不用 ImGuiP），B-4 改成只加 `NoBringToFrontOnFocus` 并记录「层级需 Dalamud 提供 API」 |
| 洋红探针不变色 | 删 A-2 全块，保留 A-1，交付里写明原因与可选项 |
| `ctx.Handle->Windows` 访问崩溃 | 加 `if (ctx.Handle == null) return ImGuiWindowPtr.Null;` 并改用 `ImGuiP.FindWindowByName` 硬编码 ID（先加临时命令打印全部窗口名，再定 ID） |
| 编译出现 ImPlot 相关错误 | 说明误用了 `ImRaii`，改回自建 RAII（本文件已有 `OmniThemeScope`） |
