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

namespace LKami9.TitleHelper;

/// <summary>
/// LKami9 称号助手（在线模块版）
///
/// 功能：
///   · 检索全部称号（按称号名 / 成就名 / 分类 搜索）
///   · 区分「已解锁 / 未解锁」，已解锁的一键佩戴
///   · 未解锁的显示获取方式（关联成就、成就描述、成就分类、成就点数）
///
/// 数据源与接口（全部为公开接口，不写内存）：
///   Title 表       Lumina.Excel.Sheets.Title       （Masculine / Feminine / IsPrefix / Order）
///   Achievement 表 Lumina.Excel.Sheets.Achievement （Name / Description / Title / AchievementCategory / Points）
///   解锁状态       UIState.Instance()->TitleList.IsTitleUnlocked(ushort)
///   佩戴称号       UIState.Instance()->TitleController.SendTitleIdUpdate(ushort)
/// </summary>
public sealed class LKami9TitleHelper : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "LKami9称号助手",
        Description = "检索全部称号，区分已解锁/未解锁；已解锁的一键佩戴，未解锁的显示获取方式（关联成就与描述）。",
        Category    = ModuleCategory.Interface,
        Author      = "LKami9",
        Commands =
        [
            new("重新扫描称号",         "/omni LKami9TitleHelper refresh"),
            new("按关键词搜索",         "/omni LKami9TitleHelper search 关键字"),
            new("按 ID 佩戴称号",       "/omni LKami9TitleHelper equip 123"),
        ],
    };

    public override bool HasSettings => true;

    // 宿主按「以 Config 结尾的成员」绑定已保存的配置
    private LKami9TitleHelperConfig config = new();

    private sealed class TitleEntry
    {
        public uint   Id;
        public string Name    = string.Empty;
        public string NameF   = string.Empty;
        public bool   IsPrefix;
        public ushort Order;
        public string AchName  = string.Empty;
        public string AchDesc  = string.Empty;
        public string AchCat   = string.Empty;
        public byte   AchPoints;
    }

    private readonly List<TitleEntry> all = [];
    private readonly List<TitleEntry> view = [];

    private bool   built;
    private bool   unlockedOnly = false;
    private bool   lockedOnly   = false;
    private bool   prefixOnly   = false;
    private bool   suffixOnly   = false;
    private string search       = string.Empty;
    private string status       = "尚未载入。";
    private int    unlockedCount;
    private int    displayLimit = 200;
    private string lastEquipped = string.Empty;

    #region 生命周期

    protected override void OnEnable()
    {
        DalamudServices.Framework.Update += this.OnFrameworkUpdate;
        this.Build();
    }

    protected override void OnDisable()
    {
        DalamudServices.Framework.Update -= this.OnFrameworkUpdate;
    }

    public override bool ResetSettings()
    {
        this.config.Ascending   = true;
        this.config.ScanOnOpen  = true;
        this.config.HideUnused  = true;
        return true;
    }

    public override bool TryHandleCommand(string arguments)
    {
        var text = (arguments ?? string.Empty).Trim();
        if (text.Length == 0) { this.Build(); return true; }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd   = parts[0].ToLowerInvariant();
        var arg   = parts.Length > 1 ? parts[1] : string.Empty;

        switch (cmd)
        {
            case "refresh":
                this.Build();
                return true;

            case "search":
                this.search = arg;
                this.Rebuild();
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

    #endregion

    #region 数据

    private void Build()
    {
        this.all.Clear();
        this.unlockedCount = 0;

        try
        {
            var achievements = DalamudServices.DataManager.GetExcelSheet<AchRow>();
            var titles       = DalamudServices.DataManager.GetExcelSheet<Title>();
            if (achievements == null || titles == null) { this.status = "取不到称号/成就数据表。"; return; }

            // 成就 → 称号 反查表（成就名 / 描述 / 分类 / 点数）
            var byTitle = new Dictionary<uint, (string Name, string Desc, string Cat, byte Points)>();
            foreach (var ach in achievements)
            {
                try
                {
                    var tid = ach.Title.RowId;
                    if (tid == 0 || byTitle.ContainsKey(tid)) continue;

                    var cat = string.Empty;
                    try { cat = ach.AchievementCategory.ValueNullable?.Name.ToString() ?? string.Empty; } catch { }
                    byTitle[tid] = (ach.Name.ToString(), ach.Description.ToString(), cat, ach.Points);
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

                    var entry = new TitleEntry
                    {
                        Id       = t.RowId,
                        Name     = name,
                        NameF    = t.Feminine.ToString(),
                        IsPrefix = t.IsPrefix,
                        Order    = t.Order,
                    };

                    if (byTitle.TryGetValue(t.RowId, out var a))
                    {
                        entry.AchName   = a.Name;
                        entry.AchDesc   = Condense(a.Desc);
                        entry.AchCat    = a.Cat;
                        entry.AchPoints = a.Points;
                    }

                    this.all.Add(entry);
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
        this.Rebuild();
        this.status = $"已载入 {this.all.Count} 个称号（已解锁 {this.unlockedCount}）。";
    }

    private static string Condense(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 160 ? s[..160] + "…" : s;
    }

    private unsafe bool IsUnlocked(uint id)
    {
        try
        {
            var ui = UIState.Instance();
            if (ui == null) return false;
            return ui->TitleList.IsTitleUnlocked((ushort)id);
        }
        catch { return false; }
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
            this.status = "已佩戴：" + this.lastEquipped;
        }
        catch (Exception ex)
        {
            this.status = "佩戴失败：" + ex.Message;
        }
    }

    private void Rebuild()
    {
        this.view.Clear();
        this.unlockedCount = 0;

        var kw = this.search.Trim();
        foreach (var e in this.all)
        {
            var unlocked = this.IsUnlocked(e.Id);
            if (unlocked) this.unlockedCount++;

            if (this.unlockedOnly && !unlocked) continue;
            if (this.lockedOnly && unlocked) continue;
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

    private void OnFrameworkUpdate(IFramework framework)
    {
        // 称号列表是进游戏后异步拉取的：拿到后自动补一次解锁状态
        if (!this.built) return;
        if (this.unlockedCount > 0) return;

        unsafe
        {
            var ui = UIState.Instance();
            if (ui == null) return;
            if (!ui->TitleList.DataReceived) return;
        }

        this.Rebuild();
    }

    #endregion

    #region 界面

    public override bool DrawSettings()
    {
        if (!this.built)
        {
            if (ImGui.Button("载入称号数据")) this.Build();
            ImGui.SameLine();
            ImGui.Text(this.status);
            return true;
        }

        ImGui.SetNextItemWidth(320f);
        var s = this.search;
        if (ImGui.InputTextWithHint("##search", "搜索称号名 / 成就名 / 分类 / ID", ref s, 120))
        {
            this.search = s;
            this.Rebuild();
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新")) this.Build();
        ImGui.SameLine();
        if (ImGui.Button("清空搜索")) { this.search = string.Empty; this.Rebuild(); }

        ImGui.Spacing();

        if (ImGui.Checkbox("仅已解锁", ref this.unlockedOnly)) { if (this.unlockedOnly) this.lockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        if (ImGui.Checkbox("仅未解锁", ref this.lockedOnly)) { if (this.lockedOnly) this.unlockedOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        if (ImGui.Checkbox("仅前缀", ref this.prefixOnly)) { if (this.prefixOnly) this.suffixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        if (ImGui.Checkbox("仅后缀", ref this.suffixOnly)) { if (this.suffixOnly) this.prefixOnly = false; this.Rebuild(); }
        ImGui.SameLine();
        var asc = this.config.Ascending;
        if (ImGui.Checkbox("正序", ref asc)) { this.config.Ascending = asc; this.Rebuild(); }

        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.8f, 1f, 1f),
            $"共 {this.all.Count} 个称号 · 已解锁 {this.unlockedCount} · 当前列表 {this.view.Count} 条");
        if (!string.IsNullOrEmpty(this.lastEquipped))
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(0.6f, 1f, 0.6f, 1f), "上次佩戴：" + this.lastEquipped);
        }

        ImGui.Separator();

        if (ImGui.BeginChild("##titlelist", new System.Numerics.Vector2(0, -1), true))
        {
            var shown = 0;
            foreach (var e in this.view)
            {
                if (shown >= this.displayLimit) break;
                shown++;

                var unlocked = this.IsUnlocked(e.Id);
                var mark     = unlocked ? "[已解锁]" : "[未解锁]";
                var kind     = e.IsPrefix ? "前缀" : "后缀";

                ImGui.PushID((int)e.Id);
                ImGui.TextColored(
                    unlocked ? new System.Numerics.Vector4(0.6f, 1f, 0.6f, 1f) : new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1f),
                    $"{mark} {e.Name}");

                ImGui.SameLine();
                ImGui.TextDisabled($"({kind} · ID {e.Id})");

                if (unlocked)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("佩戴")) this.Equip(e.Id);
                }

                // 获取方式（对未解锁的尤其重要，已解锁也保留方便查来源）
                if (!string.IsNullOrEmpty(e.AchName) || !string.IsNullOrEmpty(e.AchDesc))
                {
                    ImGui.Indent(12f);
                    var line = "获取：";
                    if (!string.IsNullOrEmpty(e.AchName)) line += "【" + e.AchName + "】";
                    if (!string.IsNullOrEmpty(e.AchCat)) line += "  分类：" + e.AchCat;
                    if (e.AchPoints > 0) line += $"  {e.AchPoints} 点";
                    ImGui.TextWrapped(line);

                    if (!string.IsNullOrEmpty(e.AchDesc))
                        ImGui.TextWrapped("   " + e.AchDesc);
                    ImGui.Unindent(12f);
                }
                else if (!unlocked)
                {
                    ImGui.Indent(12f);
                    ImGui.TextDisabled("   （数据表中没有关联成就，可能是特殊来源）");
                    ImGui.Unindent(12f);
                }

                ImGui.Separator();
                ImGui.PopID();
            }

            if (this.view.Count > this.displayLimit)
            {
                if (ImGui.Button($"显示更多（还有 {this.view.Count - this.displayLimit} 条）"))
                    this.displayLimit += 200;
            }
            else if (this.displayLimit > 200 && this.view.Count <= this.displayLimit)
            {
                this.displayLimit = 200;
            }

            ImGui.EndChild();
        }

        ImGui.Text(this.status);
        return true;
    }

    #endregion
}

public sealed class LKami9TitleHelperConfig
{
    public bool Ascending  { get; set; } = true;
    public bool ScanOnOpen { get; set; } = true;
    public bool HideUnused { get; set; } = true;
}
