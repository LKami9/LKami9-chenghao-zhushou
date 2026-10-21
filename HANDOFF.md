# LKami9称号助手 —— 交接文档

> 最后更新：2026-10-21 23:05（本机时间）
> 状态：**v1.1.5 已推送并通过哈希校验，等待实机验收**

---

## 1. 这是什么

OmniToolbox（卫月插件）的**在线模块**：检索 FFXIV 称号、一键佩戴、查看未解锁称号的获取方式。独立窗口，配色跟随 Omni 本体主题。

| 项 | 值 |
|---|---|
| 模块名 | LKami9称号助手 |
| InternalName | `LKami9TitleHelper` |
| 作者 | LKami9 |
| 分类 | `ModuleCategory.Interface` |
| 当前版本 | **1.1.5** |
| 最低 Omni 版本 | 1.1.7.0（实机为 1.1.7.8） |
| 本地目录 | `_splatoon/lk_title/` |
| 仓库 | https://github.com/LKami9/LKami9-chenghao-zhushou （**main** 分支） |
| 模块文件 | `Modules/LKami9TitleHelper.cs`（742 行 / 26,798 字节） |
| 当前 SHA256 | `D91F24FBD8D133CF6D9EA28C120527AA95F0FE1D8A0C893C28E936864244EC5F` v1.1.5 |

### 安装地址（重要：只能用 raw 或 jsDelivr@main，不要 @master）

```
https://raw.githubusercontent.com/LKami9/LKami9-chenghao-zhushou/main/TreeHouseModules.json
https://cdn.jsdelivr.net/gh/LKami9/LKami9-chenghao-zhushou@main/TreeHouseModules.json
```

同一作者的其它模块（同为 main 分支，可作格式参考）：milu / huankabao / housing / huanhua / lifu。

---

## 2. 功能清单（已实现）

| 功能 | 实现方式 |
|---|---|
| 检索称号 | 搜索框匹配 称号名(男/女式) / 成就名 / 成就描述 / 成就分类 / 称号 ID |
| 过滤 | 仅已解锁 / 仅未解锁 / 仅前缀 / 仅后缀 / 正序（互斥处理：解锁两选一、前后缀两选一） |
| 一键佩戴 | 已解锁行显示「佩戴」按钮 → `TitleController.SendTitleIdUpdate(ushort)` |
| 未解锁看获取方式 | 展开显示：关联成就名 + 成就分类 + 成就点数 + 成就描述（换行自动折行） |
| 统计 | 顶部「共 N 个称号 · 已解锁 M · 当前列表 K 条」+「上次佩戴」 |
| 分页 | 列表默认 300 条，超出显示「显示更多」 |
| 独立窗口 | `UiBuilder.Draw` 挂载，`OmniWindowChrome` 提供标题栏/折叠/关闭，位置由 Omni 记忆 |
| 主题一致 | 32 个颜色 + 8 个样式变量取自 `OmniTheme.Tokens` / `OmniTheme.Scale`，通过自建 RAII 作用域推入弹出 |
| 可读性 | `Readable()` 按背景亮度把文字色推到对比度 ≥ 4.5:1（浅色主题自动压深、深色自动提亮） |
| 命令 | `window` / `refresh` / `search <kw>` / `equip <id>` |

### 用到的接口（全部公开 API，不写内存）

```
UIState.Instance()->TitleList.IsTitleUnlocked(ushort)      // 解锁判定
UIState.Instance()->TitleController.SendTitleIdUpdate(ushort) // 佩戴
UIState.Instance()->TitleList.RequestTitleList()           // 触发称号列表拉取
Lumina: Title 表（Masculine/Feminine/IsPrefix/Order）
Lumina: Achievement 表（Name/Description/Title/AchievementCategory/Points）
```

### 代码结构（742 行）

```
LKami9TitleHelper : ModuleBase
├─ 生命周期   OnEnable / OnDisable / ResetSettings / TryHandleCommand / ToggleWindow
├─ 数据       Build（扫表建索引）/ RefreshUnlocked / IsUnlocked / Condense / Equip / Rebuild / OnFrameworkUpdate
├─ 窗口       DrawWindow（RAII 主题 + chrome + 内容容器）/ DrawContent（工具条/过滤/摘要/列表）
├─ 设置页     DrawSettings（仅「开关窗口 / 重新扫描 / 自动扫描」）
├─ 主题       OmniThemeScope（自建 RAII，push 自计数、Dispose 按计数弹）
└─ 工具       Opaque / Lum / Contrast / Readable（对比度自适应）
LKami9TitleHelperConfig { WindowOpen, Ascending, ScanOnOpen }
```

---

## 3. 版本演进（含每一次踩坑）

| 版本 | 时间 | 改动 | 结果 |
|---|---|---|---|
| v1.0.0 | 10-21 21:25 | 首版：UI 塞在模块设置页里 | 能用，但不像独立工具 |
| — | 10-21 21:27 | 统一 LF 行尾 + 修清单哈希 | **修坑 ①** |
| v1.1.0 | 10-21 21:45 | 改独立窗口 + Omni 主题（`OmniWindowChrome`） | 窗口出来，但**浅色主题下文字几乎看不见** |
| v1.1.1 | 10-21 21:49 | `Readable()` 对比度自适应 + 内容区铺不透明底 | 文字可读，但**引入样式栈泄漏** |
| v1.1.2 | 10-21 22:24 | `PopStyleColor(30)` → `(32)` | 亮色污染消失（数字仍是硬编码） |
| v1.1.3 | 10-21 22:28 | 改成计数器自平衡（push/pop 同一计数） | 结构正确，但**用户端未重载插件**，旧实例仍在泄漏 |
| v1.1.4 | 10-21 22:34 | 干脆删掉全部 32 个颜色 push | UI 不再泄漏，但**窗口退回 Dalamud 默认深色**，不跟主题 |
| **v1.1.5** | **10-21 22:58** | 恢复配色 + 自建 `OmniThemeScope`（RAII） | **当前版本**，配色与安全兼得 |

---

## 4. 踩过的坑（按严重度排序，都带结论）

### 坑 ①：CRLF/LF 导致清单哈希与线上不一致

- **现象**：`TreeHouseModules.json` 里的 SHA256 与仓库里实际文件对不上，模块加载失败。
- **原因**：用 Python 在 Windows 上写文件时默认把 `\n` 转成 `\r\n`（本地 15,397 B，仓库 14,967 B）；清单按 CRLF 版本算哈希。
- **结论**：所有 `.cs` 必须 LF。仓库已加 `.gitattributes`（`* text=auto eol=lf`）；写文件时显式 `newline="\n"`。
- **自查**：`git show HEAD:Modules/xxx.cs | wc -c` 与本地字节数必须一致。

### 坑 ②：仓库地址格式不匹配 → 「在线模块操作失败」

- **现象**：粘贴 jsDelivr `@master` 地址，Omni 报「在线模块操作失败」。
- **原因**：作者其它仓库全是 `https://raw.githubusercontent.com/<user>/<repo>/main/TreeHouseModules.json` 这一种形式；`@master` + jsDelivr 是另一种，插件解析失败（且当时 jsDelivr 正好 503）。
- **结论**：**分支必须是 `main`**；地址优先 raw，备选 jsDelivr `@main`。已把仓库 master 改名 main 并设为默认分支。

### 坑 ③：样式栈泄漏污染整个 Dalamud UI（最严重，连续栽两次）

- **现象 1（变亮）**：装上模块后**整个卫月 UI** 变米白刺眼。
- **现象 2（变黑）**：更新后再看，**整个卫月 UI** 变深色。
- **根因**：`PushOmniWindowStyle()` 推了 **32** 个 `PushStyleColor`，`PopOmniWindowStyle()` 只弹 **30** 个。
  ImGui 颜色栈后进先出，漏掉的是最早推的 `ImGuiCol.WindowBg` 和 `ImGuiCol.ChildBg`（被我设成主题背景色）→ 同一帧后续所有窗口都吃到这两个颜色。
- **日志实证**：
  ```
  2026-10-21 22:26:16.492 [WRN] ImGui assertion failed:
    SizeOfColorStack >= g.ColorStack.Size && "PushStyleColor/PopStyleColor Mismatch!"
  ```
- **修复路径**：v1.1.2 改数字 → v1.1.3 计数器 → v1.1.4 全删 → **v1.1.5 自建 RAII**：

  ```csharp
  using var themeScope = new OmniThemeScope();
  themeScope.PushAll();   // 内部每次 push 就 colors++ / vars++

  public void Dispose()   // 作用域结束必定执行
  {
      if (this.vars   > 0) ImGui.PopStyleVar(this.vars);
      if (this.colors > 0) ImGui.PopStyleColor(this.colors);
  }
  ```

- **教训（写进规范）**：任何 `PushStyleColor/PushStyleVar` 必须走 RAII 或计数，**禁止手写常量**。改动后必须用脚本核对 push 数 == pop 数。

### 坑 ④：模块更新后必须重载插件

- **现象**：推了 v1.1.2/v1.1.3 后用户仍看到泄漏。
- **原因**：Omni 只替换了 `module.cs` 文件，**运行中的编译产物仍是旧的**。
- **结论**：更新流程固定为「停用模块 → 重载 OmniToolbox → 刷新仓库 → 更新 → 启用」。

### 坑 ⑤：`ImRaii` 不能在在线模块里用

- **现象**：编译报 `类型"ImPlotCol"在未引用的程序集中定义`。
- **原因**：Dalamud 的 `ImRaii.PushColor/PushStyle` 重载签名牵涉 `Dalamud.Bindings.ImPlot`，而在线模块的 Roslyn 环境不一定引用它。
- **结论**：在线模块只用 `Dalamud.Bindings.ImGui` + 自建 RAII，不引入 ImRaii。

### 坑 ⑥：CDN 缓存

- **现象**：push 后 raw / jsDelivr 仍返回旧版本（raw 约 3–5 分钟，jsDelivr 分支缓存需 purge）。
- **处理**：`https://purge.jsdelivr.net/gh/<user>/<repo>@main/<file>`；或用 commit 固定 URL 兜底：
  `https://cdn.jsdelivr.net/gh/LKami9/LKami9-chenghao-zhushou@<commit>/TreeHouseModules.json`
- **注意**：本机 `curl` 解析不了 `raw.githubusercontent.com`（DNS），但 **.NET/插件能解析** —— 验证时用 `Invoke-WebRequest`，别被 curl 的失败误导。

### 坑 ⑦：浅色/玻璃主题对比度

- **现象**：截图里文字几乎不可见。
- **原因**：用户主题是液态玻璃（`LiquidGlassEnabled=true`、`GlassMaskOpacity=0.2`），`Tokens.Accent/Secondary/Success` 都是浅色，直接当文字色用就是白底白字。
- **处理**：`Readable(fg,bg)` 迭代到对比度 ≥ 4.5；内容底用 `Background` 与 `Surface` 混合（深色主题不发黑）。

---

## 5. 当前状态与验收清单

### 已完成

- [x] v1.1.5 推送 main，raw/jsDelivr 均返回 v1.1.5，SHA256 一致
- [x] 编译校验：对已装 **OmniToolbox 1.1.7.8** 程序集 `dotnet build` 通过
- [x] 结构自检：`PushAll` = 32 色 + 8 变量；pop 由 `OmniThemeScope.Dispose` 按计数执行；`Begin/End`、`BeginChild/EndChild`、`PushID/PopID`、`Indent/Unindent`、`BeginDisabled/EndDisabled` 全配平
- [x] 数据侧证据：MCP `data` 工具查 `Title` 470 = 「2019群狼盛宴冠军」`IsPrefix=true`，与界面显示一致

### 待用户实机验收

- [ ] 窗口配色是否与 Omni 主题一致（浅色玻璃主题下）
- [ ] 卫月其它窗口颜色是否不再被污染
- [ ] 佩戴按钮是否真的改称号
- [ ] 「获取方式」文案是否准确（尤其冷门称号）
- [ ] 折叠/关闭/拖拽/缩放在高 DPI 下是否正常

### 已知风险 / 未验证

1. **首次进游戏时 `TitleList` 可能还没拉取**：`Build()` 会调 `RequestTitleList()`，`OnFrameworkUpdate` 每 3 秒补刷一次（仅在窗口打开时）。若解锁数仍为 0，需要等服务器回包。
2. **称号总数 888**：Title 表里空名行被过滤；数量级正常（国服约 1000 行）。
3. **获取方式准确性**：靠 `Achievement.Title` 反查（`RowRef<Title>`）。同一称号若有多个成就会取第一条；个别称号可能无关联成就，界面会显示「数据表里没有关联成就，可能是特殊来源」。
4. **窗口内容边界**：用 `OmniWindowChrome.GetContentBounds` 计算，不同 DPI 下可能偏差 1–2 px；`AddRectFilled` 已兜底铺底。
5. **弹层颜色**（搜索框候选/右键菜单）仍走 `PopupBg`，玻璃主题下可能偏亮 —— 未实测。

---

## 6. 开发环境（换机器时照抄）

```
游戏插件根目录   D:\XIVLauncherCN\XIVLauncherCN\
OmniToolbox      installedPlugins\OmniToolbox\1.1.7.8\      （开发时引用这份）
Dalamud 开发 DLL addon\Hooks\dev\                            （Dalamud / ImGui / CS / Lumina）
模块源码缓存     pluginConfigs\OmniToolbox\TreeHouseOnline\<repo>\<module>\module.cs
模块配置         pluginConfigs\OmniToolbox\TreeHouse\LKami9TitleHelper.json
仓库列表         pluginConfigs\OmniToolbox\TreeHouseOnline\repositories.json
```

编译（`LKami9TitleHelper.csproj` 已按 1.1.7.8 配好引用）：

```bash
cd _splatoon/lk_title && dotnet build -v q
```

辅助工具：
- `ilspycmd`（`~/.dotnet/tools/ilspycmd`）反编译 .NET 程序集
- **卫月内置 MCP**（开发模式下 `http://127.0.0.1:39217/mcp`，Streamable HTTP）：`data`/`capture`/`mem_read`/`service_list` 等 14 个工具，可直接查游戏数据表与截图
- 第三方 `DalamudMCP`（`http://127.0.0.1:61016/mcp`）：本机**未安装**，装了可读背包/坐标/界面操作

---

## 7. 发布流程（固定动作，别再跳步）

```bash
cd _splatoon/lk_title
# 1) 改代码 → 编译
dotnet build -v q

# 2) 确认 LF + 更新清单哈希与版本号
python - <<'PY'  # （脚本见下方“哈希脚本”）
PY

# 3) 提交推送
git add -A && git commit -m "vX.Y.Z：说明" && git push origin main

# 4) 清 CDN 缓存并**用 .NET 抓取**验证哈希
curl -s -o /dev/null "https://purge.jsdelivr.net/gh/LKami9/LKami9-chenghao-zhushou@main/TreeHouseModules.json"
Invoke-WebRequest https://raw.githubusercontent.com/LKami9/LKami9-chenghao-zhushou/main/TreeHouseModules.json
```

哈希脚本要点：`\r\n → \n` 规范化 → `sha256` → 写回 `TreeHouseModules.json`（`newline="\n"`，版本号同步改）。

发给用户的更新步骤（必须原样交代）：

1. 停用模块
2. **重载 OmniToolbox**（或重启游戏）
3. 刷新仓库 → 更新
4. 启用 → 开窗口

---

## 8. 后续可做（按优先级）

1. **用 MCP 核对数据准确性**：`data` 工具逐条比对 `Title ↔ Achievement` 映射，修掉错配/漏配。
2. **收藏栏**：常用称号置顶（配置里存 ID 列表）。
3. **按分类筛选**：竞技场/PvP/副本/任务/活动 分 Tab。
4. **显示成就进度**：`Achievement.Instance()->IsComplete` + 进度条（需要 `RequestAchievementProgress`）。
5. **搜索框候选弹层配色**：验证玻璃主题下 `PopupBg` 是否发亮。
6. **称号性别式**：目前优先显示男式名，可加「按角色性别显示」选项。

---

## 9. 给接手者的三句话

1. **样式栈是雷区**：任何 `PushStyleColor/PushStyleVar` 一律用 `OmniThemeScope`（或等价 RAII），禁止手写弹出数量。
2. **发布前必验三件**：LF 行尾、清单 SHA 与线上一致、对 1.1.7.8 编译通过。
3. **改完必须让用户重载插件**，否则他看到的是旧实例的行为。
