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
using OmniToolbox.UI.Controls;
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
    private System.Numerics.Vector2 lastPos;

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
        this.config.DockToOmni = true;
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

    /// <summary>扫当前 ImGui 上下文，取名字含 Omni、本帧活跃、面积最大的根窗口。</summary>
    private static unsafe ImGuiWindowPtr FindOmniMainWindow()
    {
        var ctx = ImGui.GetCurrentContext();
        if (ctx.Handle == null) return ImGuiWindowPtr.Null;

        var list  = ctx.Handle->Windows;
        var frame = ctx.Handle->FrameCount;
        var best  = ImGuiWindowPtr.Null;

        for (var i = 0; i < list.Size; i++)
        {
            var w = list[i];
            if (w.IsNull) continue;
            if (w.Handle->Hidden != 0 || w.Handle->IsFallbackWindow != 0) continue;
            if (w.Handle->ParentWindow != null) continue;
            if (w.Handle->LastFrameActive != frame) continue;

            var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)w.Handle->Name) ?? string.Empty;
            if (!name.Contains("Omni", StringComparison.OrdinalIgnoreCase)) continue;

            if (best.IsNull ||
                w.Handle->Size.X * w.Handle->Size.Y > best.Handle->Size.X * best.Handle->Size.Y)
                best = w;
        }

        return best;
    }

    private unsafe System.Numerics.Vector2 ComputeDockedPosition(
        ImGuiWindowPtr omni, System.Numerics.Vector2 mySize)
    {
        var vp       = ImGui.GetMainViewport();
        var gap      = OmniTheme.Scale(12f);
        var fallback = OmniTheme.ClampWindowPosition(
            vp.WorkPos + vp.WorkSize - mySize - new System.Numerics.Vector2(gap, gap), mySize);

        if (omni.IsNull) return fallback;

        var oPos = omni.Handle->Pos;
        var oSz  = omni.Handle->Size;
        var candidates = new[]
        {
            new System.Numerics.Vector2(oPos.X + oSz.X + gap, oPos.Y),
            new System.Numerics.Vector2(oPos.X - mySize.X - gap, oPos.Y),
            new System.Numerics.Vector2(oPos.X, oPos.Y + oSz.Y + gap),
        };

        foreach (var cand in candidates)
        {
            var clamped = OmniTheme.ClampWindowPosition(cand, mySize);
            if (MathF.Abs(clamped.X - cand.X) < 1f &&
                MathF.Abs(clamped.Y - cand.Y) < 1f &&
                !Overlaps(clamped, mySize, oPos, oSz))
                return clamped;
        }

        return fallback;
    }

    private static bool Overlaps(
        System.Numerics.Vector2 aPos, System.Numerics.Vector2 aSize,
        System.Numerics.Vector2 bPos, System.Numerics.Vector2 bSize) =>
        aPos.X < bPos.X + bSize.X && aPos.X + aSize.X > bPos.X &&
        aPos.Y < bPos.Y + bSize.Y && aPos.Y + aSize.Y > bPos.Y;

    private static void DrawThemedRule(System.Numerics.Vector4 color)
    {
        var pos   = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var y     = pos.Y + OmniTheme.Scale(1f);
        ImGui.GetWindowDrawList().AddLine(pos with { Y = y }, new System.Numerics.Vector2(pos.X + width, y),
            ImGui.GetColorU32(color));
        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.Scale(6f)));
    }

    private static void WrappedTextColored(System.Numerics.Vector4 color, string text)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    private static void FillCurrentWindow(System.Numerics.Vector4 color, float radius)
    {
        var pos  = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size,
            ImGui.GetColorU32(Opaque(color)), radius);
    }

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

        var omni = FindOmniMainWindow();
        var pos = this.config.DockToOmni
            ? this.ComputeDockedPosition(omni, targetSize)
            : OmniTheme.ClampWindowPosition(
                this.lastPos == System.Numerics.Vector2.Zero
                    ? viewport.WorkPos + viewport.WorkSize - targetSize
                    : this.lastPos,
                targetSize);

        OmniWindowChrome.SetNextWindowPosition(WindowId, pos);
        ImGui.SetNextWindowPos(pos, this.config.DockToOmni ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        this.lastPos = pos;

        var self = ImGuiWindowPtr.Null;
        if (ImGui.Begin(WindowId, ref open,
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoDocking  | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            self = ImGuiP.GetCurrentWindow();

            var framePos  = ImGui.GetWindowPos();
            var frameSize = ImGui.GetWindowSize();
            var tokens    = OmniTheme.Tokens;
            var radius    = OmniTheme.Scale(tokens.BorderRadius);
            var draw      = ImGui.GetWindowDrawList();

            draw.AddRectFilled(framePos, framePos + frameSize,
                ImGui.GetColorU32(tokens.Background with { W = 1f }), radius);

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
                    var panel = Opaque(tokens.Background);
                    draw.AddRectFilled(contentPos, contentPos + contentSize,
                        ImGui.GetColorU32(panel), radius);

                    ImGui.SetCursorScreenPos(contentPos);
                    ImGui.BeginChild("###LKami9TitleContent", contentSize, false, ImGuiWindowFlags.None);
                    FillCurrentWindow(panel, radius);
                    this.DrawContent();
                    ImGui.EndChild();
                }
            }
        }

        ImGui.End();

        if (!self.IsNull)
        {
            if (!omni.IsNull) ImGuiP.BringWindowToDisplayBehind(self, omni);
            else              ImGuiP.BringWindowToDisplayBack(self);
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
        var panel = Opaque(tokens.Background);
        var cText = Readable(tokens.Text, back);
        var cDim = Readable(tokens.Secondary, back);
        var cAccent = Readable(tokens.Accent, back);
        var cOk = Readable(tokens.Success, back);

        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.Scale(6f)));
        ImGui.Indent(pad);

        // ── 工具条 ──
        var s = this.search;
        if (OmniControls.InputTextWithHint("###titlesearch", "搜索称号 / 成就 / 分类 / ID",
                ref s, 120, OmniTheme.Scale(260f)))
        {
            this.search = s;
            this.Rebuild();
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton("刷新", false)) { this.Build(); }

        ImGui.SameLine();
        ImGui.BeginDisabled(this.search.Length == 0);
        if (OmniControls.SmallButton("清空", false)) { this.search = string.Empty; this.Rebuild(); }
        ImGui.EndDisabled();

        // ── 过滤 ──
        ImGui.Spacing();
        var uo = this.unlockedOnly;
        if (OmniControls.Checkbox("仅已解锁", ref uo)) { this.unlockedOnly = uo; if (uo) this.lockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var lo = this.lockedOnly;
        if (OmniControls.Checkbox("仅未解锁", ref lo)) { this.lockedOnly = lo; if (lo) this.unlockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var po = this.prefixOnly;
        if (OmniControls.Checkbox("仅前缀", ref po)) { this.prefixOnly = po; if (po) this.suffixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var so = this.suffixOnly;
        if (OmniControls.Checkbox("仅后缀", ref so)) { this.suffixOnly = so; if (so) this.prefixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var asc = this.config.Ascending;
        if (OmniControls.Checkbox("正序", ref asc)) { this.config.Ascending = asc; this.Rebuild(); }

        // ── 摘要 ──
        ImGui.Spacing();
        ImGui.TextColored(cAccent,
            $"共 {this.all.Count} 个称号 · 已解锁 {this.unlockedCount} · 当前列表 {this.view.Count} 条");
        if (this.lastEquipped.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(cOk, "· 上次佩戴：" + this.lastEquipped);
        }

        DrawThemedRule(tokens.Border);

        // ── 列表 ──
        var listHeight = ImGui.GetContentRegionAvail().Y - OmniTheme.Scale(26f);
        if (listHeight < OmniTheme.Scale(120f)) listHeight = OmniTheme.Scale(120f);

        ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);
        FillCurrentWindow(panel, OmniTheme.Scale(tokens.BorderRadius));

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
            ImGui.TextColored(cDim, $"({kind} · {e.Id})");

            if (ok)
            {
                ImGui.SameLine();
                if (OmniControls.SmallButton("佩戴", false)) this.Equip(e.Id);
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
                    WrappedTextColored(cDim, e.AchDesc);
                }
                ImGui.Unindent(OmniTheme.Scale(14f));
            }
            else if (!ok)
            {
                ImGui.Indent(OmniTheme.Scale(14f));
                ImGui.TextColored(cDim, "（数据表里没有关联成就，可能是特殊来源）");
                ImGui.Unindent(OmniTheme.Scale(14f));
            }

            DrawThemedRule(tokens.Border);
            ImGui.PopID();
        }

        if (this.view.Count > this.displayLimit)
        {
            if (OmniControls.SmallButton($"显示更多（还有 {this.view.Count - this.displayLimit} 条）", false))
                this.displayLimit += 300;
        }
        else if (this.displayLimit > 300 && this.view.Count <= this.displayLimit)
        {
            this.displayLimit = 300;
        }

        ImGui.EndChild();

        ImGui.TextColored(cDim, this.status);
        ImGui.Unindent(pad);
    }

    #endregion

    #region 设置页（极简）


    public override bool DrawSettings()
    {
        var tokens = OmniTheme.Tokens;
        var back   = Opaque(tokens.Background);

        ImGui.TextColored(Readable(tokens.Accent, back), "LKami9 称号助手");
        WrappedTextColored(Readable(tokens.Secondary, back),
            "主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");

        ImGui.Spacing();
        if (OmniControls.SmallButton(this.config.WindowOpen ? "关闭窗口" : "打开窗口", false))
            this.ToggleWindow(!this.config.WindowOpen);

        ImGui.SameLine();
        if (OmniControls.SmallButton("重新扫描", false)) this.Build();

        ImGui.Spacing();
        var scan = this.config.ScanOnOpen;
        if (OmniControls.Checkbox("启用时自动扫描称号", ref scan)) this.config.ScanOnOpen = scan;

        var dock = this.config.DockToOmni;
        if (OmniControls.Checkbox("窗口贴靠 Omni 主窗口", ref dock)) this.config.DockToOmni = dock;

        ImGui.Spacing();
        ImGui.TextColored(Readable(tokens.Secondary, back), this.status);
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



    #endregion
}

public sealed class LKami9TitleHelperConfig
{
    public bool WindowOpen { get; set; }
    public bool Ascending  { get; set; } = true;
    public bool ScanOnOpen { get; set; } = true;
    public bool DockToOmni { get; set; } = true;
}
