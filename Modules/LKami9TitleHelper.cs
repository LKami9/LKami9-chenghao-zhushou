using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using Lumina.Excel.Sheets;

using AchRow = Lumina.Excel.Sheets.Achievement;

using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace LKami9.TitleHelper;

/// <summary>
/// LKami9 称号助手（在线模块版 · 独立窗口）
///
/// 独立窗口 + Omni 本体主题（OmniTheme.Tokens / OmniWindowChrome），不占用模块设置页。
/// 功能：
///   · 检索全部称号（称号名 / 成就名 / 成就描述 / 分类 / ID）
///   · 区分已解锁 / 未解锁；已解锁一键佩戴
///   · 未解锁显示获取方式（关联成就、成就分类、点数、描述）
///
/// 接口（全公开，不写内存）：
///   UIState.Instance()->TitleList.IsTitleUnlocked(ushort)
///   UIState.Instance()->TitleController.SendTitleIdUpdate(ushort)
///   UIState.Instance()->TitleList.RequestTitleList()
/// </summary>
public sealed class LKami9TitleHelper : ModuleBase
{
    private const string WindowId      = "###LKami9TitleMainWindow";
    private const string CollapseId    = "###LKami9TitleCollapse";
    private const string CloseId       = "###LKami9TitleClose";

    public override ModuleInfo Info { get; } = new()
    {
        Title       = "LKami9称号助手",
        Description = "独立窗口：检索称号、一键佩戴、查看未解锁称号的获取方式。界面与配色跟随 Omni 本体主题。",
        Category    = ModuleCategory.Interface,
        Author      = "LKami9",
        Commands =
        [
            new("打开 / 关闭窗口", "/omni LKami9TitleHelper window"),
            new("重新扫描称号",   "/omni LKami9TitleHelper refresh"),
            new("按关键词搜索",   "/omni LKami9TitleHelper search 关键字"),
            new("按 ID 佩戴称号", "/omni LKami9TitleHelper equip 123"),
        ],
    };

    public override bool HasSettings => true;

    private LKami9TitleHelperConfig config = new();

    private sealed class TitleEntry
    {
        public uint   Id;
        public string Name    = string.Empty;
        public string NameF   = string.Empty;
        public bool   IsPrefix;
        public ushort Order;
        public string AchName = string.Empty;
        public string AchDesc = string.Empty;
        public string AchCat  = string.Empty;
        public byte   AchPoints;
    }

    private readonly List<TitleEntry> all  = [];
    private readonly List<TitleEntry> view = [];
    private readonly Dictionary<uint, bool> unlocked = [];

    private bool   built;
    private string search  = string.Empty;
    private string status  = "尚未载入。";
    private string lastEquipped = string.Empty;

    private bool    unlockedOnly;
    private bool    lockedOnly;
    private bool    prefixOnly;
    private bool    suffixOnly;
    private int     displayLimit = 300;
    private int     unlockedCount;

    private bool                 collapsed;
    private bool                 resizePending;
    private System.Numerics.Vector2 expandedSize = new(820f, 560f);

    #region 生命周期

    protected override void OnEnable()
    {
        DalamudServices.PluginInterface.UiBuilder.Draw += this.DrawWindow;
        DalamudServices.Framework.Update                += this.OnFrameworkUpdate;

        if (this.config.ScanOnOpen || !this.built) this.Build();
    }

    protected override void OnDisable()
    {
        DalamudServices.PluginInterface.UiBuilder.Draw -= this.DrawWindow;
        DalamudServices.Framework.Update                -= this.OnFrameworkUpdate;
    }

    public override bool ResetSettings()
    {
        this.config.WindowOpen = false;
        this.config.Ascending  = true;
        this.config.ScanOnOpen = true;
        this.collapsed         = false;
        this.resizePending     = true;
        return true;
    }

    public override bool TryHandleCommand(string arguments)
    {
        var text = (arguments ?? string.Empty).Trim();
        if (text.Length == 0) { this.ToggleWindow(true); return true; }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd   = parts[0].ToLowerInvariant();
        var arg   = parts.Length > 1 ? parts[1] : string.Empty;

        switch (cmd)
        {
            case "window":
                this.ToggleWindow(!this.config.WindowOpen);
                return true;

            case "refresh":
                this.Build();
                this.ToggleWindow(true);
                return true;

            case "search":
                this.search = arg;
                this.Rebuild();
                this.ToggleWindow(true);
                this.status = $"搜索「{arg}」，命中 {this.view.Count} 条。";
                return true;

            case "equip":
                if (!uint.TryParse(arg, out var id)) { this.status = "用法：/omni LKami9TitleHelper equip <称号ID>"; return true; }
                this.Equip(id);
                return true;

            default:
                return false;
        }
    }

    private void ToggleWindow(bool open)
    {
        this.config.WindowOpen = open;
        if (open && this.collapsed) { this.collapsed = false; this.resizePending = true; }
    }

    #endregion

    #region 数据

    private void Build()
    {
        this.all.Clear();
        this.unlocked.Clear();

        try
        {
            var achievements = DalamudServices.DataManager.GetExcelSheet<AchRow>();
            var titles       = DalamudServices.DataManager.GetExcelSheet<Title>();
            if (achievements == null || titles == null) { this.status = "取不到称号 / 成就数据表。"; return; }

            var byTitle = new Dictionary<uint, (string Name, string Desc, string Cat, byte Points)>();
            foreach (var ach in achievements)
            {
                try
                {
                    var tid = ach.Title.RowId;
                    if (tid == 0 || byTitle.ContainsKey(tid)) continue;

                    var cat = string.Empty;
                    try { cat = ach.AchievementCategory.ValueNullable?.Name.ToString() ?? string.Empty; } catch { }
                    byTitle[tid] = (ach.Name.ToString(), Condense(ach.Description.ToString()), cat, ach.Points);
                }
                catch { }
            }

            unsafe
            {
                var ui = UIState.Instance();
                if (ui != null && !ui->TitleList.DataReceived) ui->TitleList.RequestTitleList();
            }

            foreach (var t in titles)
            {
                try
                {
                    var name = t.Masculine.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var e = new TitleEntry
                    {
                        Id       = t.RowId,
                        Name     = name,
                        NameF    = t.Feminine.ToString(),
                        IsPrefix = t.IsPrefix,
                        Order    = t.Order,
                    };

                    if (byTitle.TryGetValue(t.RowId, out var a))
                    {
                        e.AchName   = a.Name;
                        e.AchDesc   = a.Desc;
                        e.AchCat    = a.Cat;
                        e.AchPoints = a.Points;
                    }

                    this.all.Add(e);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            this.status = "载入失败：" + ex.Message;
            DalamudServices.PluginLog.Warning(ex, "[LKami9TitleHelper] build failed");
            return;
        }

        this.built = true;
        this.RefreshUnlocked();
        this.Rebuild();
        this.status = $"已载入 {this.all.Count} 个称号 · 已解锁 {this.unlockedCount}";
    }

    private void RefreshUnlocked()
    {
        this.unlocked.Clear();
        this.unlockedCount = 0;

        unsafe
        {
            var ui = UIState.Instance();
            if (ui == null) return;

            foreach (var e in this.all)
            {
                bool ok;
                try { ok = ui->TitleList.IsTitleUnlocked((ushort)e.Id); }
                catch { ok = false; }

                this.unlocked[e.Id] = ok;
                if (ok) this.unlockedCount++;
            }
        }
    }

    private bool IsUnlocked(uint id) => this.unlocked.TryGetValue(id, out var v) && v;

    private static string Condense(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    private unsafe void Equip(uint id)
    {
        try
        {
            var ui = UIState.Instance();
            if (ui == null) { this.status = "还没进入游戏世界。"; return; }

            if (!ui->TitleList.IsTitleUnlocked((ushort)id))
            {
                this.status = $"称号 {id} 未解锁，无法佩戴。";
                return;
            }

            ui->TitleController.SendTitleIdUpdate((ushort)id);
            var e = this.all.FirstOrDefault(x => x.Id == id);
            this.lastEquipped = e != null ? $"{e.Name}（{id}）" : id.ToString();
            this.status       = "已佩戴：" + this.lastEquipped;
        }
        catch (Exception ex)
        {
            this.status = "佩戴失败：" + ex.Message;
        }
    }

    private void Rebuild()
    {
        this.view.Clear();

        var kw = this.search.Trim();
        foreach (var e in this.all)
        {
            var ok = this.IsUnlocked(e.Id);

            if (this.unlockedOnly && !ok) continue;
            if (this.lockedOnly && ok) continue;
            if (this.prefixOnly && !e.IsPrefix) continue;
            if (this.suffixOnly && e.IsPrefix) continue;

            if (kw.Length > 0)
            {
                var hit = e.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || e.NameF.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || e.AchName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || e.AchDesc.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || e.AchCat.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || e.Id.ToString().Contains(kw, StringComparison.Ordinal);
                if (!hit) continue;
            }

            this.view.Add(e);
        }

        this.view.Sort((a, b) => this.config.Ascending ? a.Order.CompareTo(b.Order) : b.Order.CompareTo(a.Order));
    }

    private DateTime nextUnlockRefresh = DateTime.MinValue;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.built) return;
        if (!this.config.WindowOpen) return;
        if (DateTime.UtcNow < this.nextUnlockRefresh) return;

        this.nextUnlockRefresh = DateTime.UtcNow.AddSeconds(3);

        unsafe
        {
            var ui = UIState.Instance();
            if (ui == null || !ui->TitleList.DataReceived) return;
        }

        var before = this.unlockedCount;
        this.RefreshUnlocked();
        if (before != this.unlockedCount) this.Rebuild();
    }

    #endregion

    #region 窗口

    private unsafe void DrawWindow()
    {
        if (!this.config.WindowOpen) return;

        var viewport = ImGui.GetMainViewport();
        var open     = this.config.WindowOpen;

        var targetSize = this.collapsed
            ? new System.Numerics.Vector2(OmniTheme.Scale(420f), OmniTheme.TitleBarHeight() + OmniTheme.Scale(14f))
            : OmniTheme.ClampWindowSize(this.expandedSize);

        ImGui.SetNextWindowSizeConstraints(OmniTheme.MainWindowMinSize(), viewport.WorkSize);

        if (this.resizePending)
        {
            ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always);
            this.resizePending = false;
        }
        else if (!this.collapsed)
        {
            ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(this.expandedSize), ImGuiCond.FirstUseEver);
        }

        OmniWindowChrome.SetNextWindowPosition(WindowId);

        var pushedColors = 0;
        var pushedVars = 0;
        PushOmniWindowStyle(ref pushedColors, ref pushedVars);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        pushedVars++;

        try
        {
            if (ImGui.Begin(WindowId, ref open,
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoDocking  | ImGuiWindowFlags.NoScrollbar))
            {
                var framePos  = ImGui.GetWindowPos();
                var frameSize = ImGui.GetWindowSize();

                var chrome = OmniWindowChrome.Draw(
                    framePos, frameSize, this.collapsed,
                    "LKami9 称号助手", CollapseId, CloseId, true);

                if (chrome.CloseClicked)
                {
                    this.config.WindowOpen = false;
                    this.collapsed         = false;
                    this.resizePending     = true;
                }

                if (chrome.ToggleCollapse)
                {
                    if (this.collapsed)
                    {
                        this.collapsed     = false;
                        this.resizePending = true;
                    }
                    else
                    {
                        this.expandedSize  = frameSize;
                        this.collapsed     = true;
                        this.resizePending = true;
                    }
                }
                else if (!this.collapsed)
                {
                    this.expandedSize = frameSize;
                }

                if (!this.collapsed)
                {
                    var (contentPos, contentSize) = OmniWindowChrome.GetContentBounds(framePos, frameSize);
                    if (contentSize.X > 1f && contentSize.Y > 1f)
                    {
                        // 玻璃/半透明主题下内容区会透出游戏画面，这里先铺一层不透明底
                        var t     = OmniTheme.Tokens;
                        var solid = new System.Numerics.Vector4(t.Background.X, t.Background.Y, t.Background.Z, 1f);
                        var draw  = ImGui.GetWindowDrawList();
                        draw.AddRectFilled(contentPos, contentPos + contentSize,
                            ImGui.GetColorU32(solid), OmniTheme.Scale(t.BorderRadius));

                        ImGui.SetCursorScreenPos(contentPos);
                        ImGui.BeginChild("###LKami9TitleContent", contentSize, false, ImGuiWindowFlags.None);
                        this.DrawContent();
                        ImGui.EndChild();
                    }
                }
            }

            ImGui.End();
        }
        finally
        {
            if (pushedVars > 0) ImGui.PopStyleVar(pushedVars);
            if (pushedColors > 0) ImGui.PopStyleColor(pushedColors);
        }

        if (!open) this.config.WindowOpen = false;
    }

    private void DrawContent()
    {
        var tokens = OmniTheme.Tokens;
        var pad    = OmniTheme.Scale(12f);

        // 主题可能是浅色/玻璃主题，直接用 Tokens 当文字色会看不清：
        // 统一按背景亮度把文字色往黑/白方向推，保证对比度。
        var back = Opaque(tokens.Background);
        var cText = Readable(tokens.Text, back);
        var cDim = Readable(tokens.Secondary, back);
        var cAccent = Readable(tokens.Accent, back);
        var cOk = Readable(tokens.Success, back);

        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.Scale(6f)));
        ImGui.Indent(pad);

        // ── 工具条 ──
        ImGui.SetNextItemWidth(OmniTheme.Scale(260f));
        var s = this.search;
        if (ImGui.InputTextWithHint("###titlesearch", "搜索称号 / 成就 / 分类 / ID", ref s, 120))
        {
            this.search = s;
            this.Rebuild();
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新")) { this.Build(); }

        ImGui.SameLine();
        ImGui.BeginDisabled(this.search.Length == 0);
        if (ImGui.Button("清空")) { this.search = string.Empty; this.Rebuild(); }
        ImGui.EndDisabled();

        // ── 过滤 ──
        ImGui.Spacing();
        var uo = this.unlockedOnly;
        if (ImGui.Checkbox("仅已解锁", ref uo)) { this.unlockedOnly = uo; if (uo) this.lockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var lo = this.lockedOnly;
        if (ImGui.Checkbox("仅未解锁", ref lo)) { this.lockedOnly = lo; if (lo) this.unlockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var po = this.prefixOnly;
        if (ImGui.Checkbox("仅前缀", ref po)) { this.prefixOnly = po; if (po) this.suffixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var so = this.suffixOnly;
        if (ImGui.Checkbox("仅后缀", ref so)) { this.suffixOnly = so; if (so) this.prefixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var asc = this.config.Ascending;
        if (ImGui.Checkbox("正序", ref asc)) { this.config.Ascending = asc; this.Rebuild(); }

        // ── 摘要 ──
        ImGui.Spacing();
        ImGui.TextColored(cAccent,
            $"共 {this.all.Count} 个称号 · 已解锁 {this.unlockedCount} · 当前列表 {this.view.Count} 条");
        if (this.lastEquipped.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(cOk, "· 上次佩戴：" + this.lastEquipped);
        }

        ImGui.Separator();

        // ── 列表 ──
        var listHeight = ImGui.GetContentRegionAvail().Y - OmniTheme.Scale(26f);
        if (listHeight < OmniTheme.Scale(120f)) listHeight = OmniTheme.Scale(120f);

        ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);

        var shown = 0;
        foreach (var e in this.view)
        {
            if (shown >= this.displayLimit) break;
            shown++;

            var ok   = this.IsUnlocked(e.Id);
            var kind = e.IsPrefix ? "前缀" : "后缀";

            ImGui.PushID((int)e.Id);

            ImGui.TextColored(ok ? cOk : cDim, ok ? "●" : "○");
            ImGui.SameLine();

            ImGui.TextColored(ok ? cText : cDim, e.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"({kind} · {e.Id})");

            if (ok)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("佩戴")) this.Equip(e.Id);
            }

            if (e.AchName.Length > 0 || e.AchDesc.Length > 0)
            {
                ImGui.Indent(OmniTheme.Scale(14f));
                var line = "获取：";
                if (e.AchName.Length > 0) line += "【" + e.AchName + "】";
                if (e.AchCat.Length  > 0) line += "   " + e.AchCat;
                if (e.AchPoints      > 0) line += $"   {e.AchPoints} 点";
                ImGui.TextColored(cDim, line);

                if (e.AchDesc.Length > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, cDim);
                    ImGui.TextWrapped(e.AchDesc);
                    ImGui.PopStyleColor();
                }
                ImGui.Unindent(OmniTheme.Scale(14f));
            }
            else if (!ok)
            {
                ImGui.Indent(OmniTheme.Scale(14f));
                ImGui.TextDisabled("（数据表里没有关联成就，可能是特殊来源）");
                ImGui.Unindent(OmniTheme.Scale(14f));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (this.view.Count > this.displayLimit)
        {
            if (ImGui.Button($"显示更多（还有 {this.view.Count - this.displayLimit} 条）"))
                this.displayLimit += 300;
        }
        else if (this.displayLimit > 300 && this.view.Count <= this.displayLimit)
        {
            this.displayLimit = 300;
        }

        ImGui.EndChild();

        ImGui.TextDisabled(this.status);
        ImGui.Unindent(pad);
    }

    #endregion

    #region 设置页（极简）

    public override bool DrawSettings()
    {
        var tokens = OmniTheme.Tokens;
        var back   = Opaque(tokens.Background);

        ImGui.TextColored(Readable(tokens.Accent, back), "LKami9 称号助手");
        ImGui.TextWrapped("主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");

        ImGui.Spacing();
        if (ImGui.Button(this.config.WindowOpen ? "关闭窗口" : "打开窗口"))
            this.ToggleWindow(!this.config.WindowOpen);

        ImGui.SameLine();
        if (ImGui.Button("重新扫描")) this.Build();

        ImGui.Spacing();
        var scan = this.config.ScanOnOpen;
        if (ImGui.Checkbox("启用时自动扫描称号", ref scan)) this.config.ScanOnOpen = scan;

        ImGui.Spacing();
        ImGui.TextDisabled(this.status);
        return true;
    }


    private static System.Numerics.Vector4 Opaque(System.Numerics.Vector4 c) => c with { W = 1f };

    private static float Lum(System.Numerics.Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    private static float Contrast(System.Numerics.Vector4 a, System.Numerics.Vector4 b)
    {
        var la = Lum(a) + 0.05f;
        var lb = Lum(b) + 0.05f;
        return la > lb ? la / lb : lb / la;
    }

    /// <summary>把前景色推到与背景对比度 ≥ 4.5（浅底推暗、深底推亮）。</summary>
    private static System.Numerics.Vector4 Readable(System.Numerics.Vector4 fg, System.Numerics.Vector4 bg)
    {
        fg = Opaque(fg);
        var lightBg = Lum(bg) > 0.5f;

        for (var i = 0; i < 24 && Contrast(fg, bg) < 4.5f; i++)
        {
            fg = lightBg
                ? new System.Numerics.Vector4(fg.X * 0.82f, fg.Y * 0.82f, fg.Z * 0.82f, 1f)
                : System.Numerics.Vector4.Lerp(fg, System.Numerics.Vector4.One, 0.18f);
        }

        return fg;
    }

    private static void PushOmniWindowStyle(ref int colors, ref int vars)
    {
        var tokens         = OmniTheme.Tokens;
        var textDisabled   = tokens.Text with { W = tokens.Text.W * 0.55f };
        var titleBackground = System.Numerics.Vector4.Lerp(tokens.Background, tokens.Primary, 0.35f);
        var titleActive     = System.Numerics.Vector4.Lerp(tokens.Background, tokens.Accent,  0.65f);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, tokens.Background with { W = 1f }); colors++;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(tokens.Background.X, tokens.Background.Y, tokens.Background.Z, 1f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, tokens.Background with { W = 1f }); colors++;
        ImGui.PushStyleColor(ImGuiCol.Border, tokens.Border); colors++;
        ImGui.PushStyleColor(ImGuiCol.Text, tokens.Text); colors++;
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, textDisabled); colors++;
        ImGui.PushStyleColor(ImGuiCol.Button, tokens.Surface); colors++;
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, OmniTheme.ActiveBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.Header, tokens.Surface); colors++;
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, tokens.Accent); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, tokens.Surface); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, OmniTheme.ActiveBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.Tab, tokens.Surface); colors++;
        ImGui.PushStyleColor(ImGuiCol.TabHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.TabActive, tokens.Accent); colors++;
        ImGui.PushStyleColor(ImGuiCol.TitleBg, titleBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, titleActive); colors++;
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, titleBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, tokens.Background); colors++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, tokens.Secondary); colors++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, tokens.Accent); colors++;
        ImGui.PushStyleColor(ImGuiCol.CheckMark, tokens.Success); colors++;
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, tokens.Secondary); colors++;
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, tokens.Accent); colors++;
        ImGui.PushStyleColor(ImGuiCol.Separator, tokens.Border); colors++;
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, tokens.Surface); colors++;
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, OmniTheme.HoverBackground); colors++;
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, tokens.Accent); colors++;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, OmniTheme.PopupPadding()); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(tokens.BorderRadius)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness()); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, OmniTheme.Scale(tokens.ButtonRadius)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, OmniTheme.Scale(tokens.BorderRadius)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, OmniTheme.Scale(tokens.ButtonRadius)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, OmniTheme.Scale(tokens.ButtonRadius)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, OmniTheme.Scale(new System.Numerics.Vector2(6f, 4f))); vars++;
    }


    #endregion
}

public sealed class LKami9TitleHelperConfig
{
    public bool WindowOpen { get; set; }
    public bool Ascending  { get; set; } = true;
    public bool ScanOnOpen { get; set; } = true;
}
