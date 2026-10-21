import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

start = s.index("    private void DrawContent()")
end = s.index("\n    #endregion", start)

new_method = r'''    private void DrawContent()
    {
        var tokens  = OmniTheme.Tokens;
        var pad     = OmniTheme.Scale(12f);
        var back    = Opaque(tokens.Background);
        var panel   = Opaque(tokens.Background);
        var cText   = Readable(tokens.Text, back);
        var cDim    = Readable(tokens.Secondary, back);
        var cAccent = Readable(tokens.Accent, back);
        var cOk     = Readable(tokens.Success, back);

        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.Scale(6f)));
        ImGui.Indent(pad);

        // 工具条：搜索 + 分类（Omni 控件）
        var sText = this.search;
        if (OmniControls.InputTextWithHint("###titlesearch", "搜索称号 / 成就 / 分类 / ID",
                ref sText, 120, OmniTheme.Scale(230f)))
        {
            this.search = sText;
            this.page   = 1;
            this.Rebuild();
        }

        ImGui.SameLine();
        var preview = this.config.CategoryFilter.Length == 0 ? "全部分类" : this.config.CategoryFilter;
        if (OmniControls.BeginCombo("###titlecat", preview, OmniTheme.Scale(150f)))
        {
            if (OmniControls.RoundedMenuItem("全部分类", true))
            {
                this.config.CategoryFilter = string.Empty;
                this.page = 1;
                this.Rebuild();
            }

            foreach (var cat in this.categories)
            {
                if (!OmniControls.RoundedMenuItem(cat, true)) continue;
                this.config.CategoryFilter = cat;
                this.page = 1;
                this.Rebuild();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton("刷新", false)) this.Build();

        ImGui.SameLine();
        ImGui.BeginDisabled(this.search.Length == 0 && this.config.CategoryFilter.Length == 0);
        if (OmniControls.SmallButton("清空", false))
        {
            this.search = string.Empty;
            this.config.CategoryFilter = string.Empty;
            this.page = 1;
            this.Rebuild();
        }
        ImGui.EndDisabled();

        // 过滤
        ImGui.Spacing();
        var uo = this.unlockedOnly;
        if (OmniControls.Checkbox("仅已解锁", ref uo)) { this.unlockedOnly = uo; if (uo) this.lockedOnly = false; this.page = 1; this.Rebuild(); }
        ImGui.SameLine();
        var lo = this.lockedOnly;
        if (OmniControls.Checkbox("仅未解锁", ref lo)) { this.lockedOnly = lo; if (lo) this.unlockedOnly = false; this.page = 1; this.Rebuild(); }
        ImGui.SameLine();
        var po = this.prefixOnly;
        if (OmniControls.Checkbox("仅前缀", ref po)) { this.prefixOnly = po; if (po) this.suffixOnly = false; this.page = 1; this.Rebuild(); }
        ImGui.SameLine();
        var so = this.suffixOnly;
        if (OmniControls.Checkbox("仅后缀", ref so)) { this.suffixOnly = so; if (so) this.prefixOnly = false; this.page = 1; this.Rebuild(); }
        ImGui.SameLine();
        var asc = this.config.Ascending;
        if (OmniControls.Checkbox("正序", ref asc)) { this.config.Ascending = asc; this.Rebuild(); }

        // 摘要
        ImGui.Spacing();
        ImGui.TextColored(cAccent,
            string.Format("共 {0} 个称号 · 已解锁 {1} · 当前 {2} 条", this.all.Count, this.unlockedCount, this.view.Count));
        if (this.lastEquipped.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(cOk, "· 上次佩戴：" + this.lastEquipped);
        }

        DrawThemedRule(tokens.Border);

        // 列表（分页）
        var pagerHeight = OmniTheme.Scale(34f);
        var listHeight  = MathF.Max(OmniTheme.Scale(160f), ImGui.GetContentRegionAvail().Y - pagerHeight);
        var pageSize    = Math.Max(20, this.config.PageSize);
        var pageCount   = Math.Max(1, (this.view.Count + pageSize - 1) / pageSize);
        if (this.page > pageCount) this.page = pageCount;
        if (this.page < 1) this.page = 1;

        ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);
        FillCurrentWindow(panel, OmniTheme.Scale(tokens.BorderRadius));

        var from = (this.page - 1) * pageSize;
        var to   = Math.Min(this.view.Count, from + pageSize);
        for (var i = from; i < to; i++)
            this.DrawRow(this.view[i], cDim, cOk);

        ImGui.EndChild();

        // Omni 原生分页器
        var pageLocal = this.page;
        var jumpLocal = this.jumpPage;
        OmniPager.Draw("###LKami9TitlePager", ref pageLocal, ref jumpLocal, this.view.Count, pageSize);
        this.page     = pageLocal;
        this.jumpPage = jumpLocal;

        ImGui.TextColored(cDim, this.status);
        ImGui.Unindent(pad);
    }

    private void DrawRow(TitleEntry e,
                         System.Numerics.Vector4 cDim,
                         System.Numerics.Vector4 cOk)
    {
        var ok      = this.IsUnlocked(e.Id);
        var kind    = e.IsPrefix ? "前缀" : "后缀";
        var starred = this.config.Favorites.Contains(e.Id);
        var tokens  = OmniTheme.Tokens;

        ImGui.PushID((int)e.Id);

        if (OmniControls.FavoriteButton("###fav", starred, starred ? "取消收藏" : "收藏并置顶"))
        {
            if (starred) this.config.Favorites.Remove(e.Id);
            else         this.config.Favorites.Add(e.Id);
            this.Rebuild();
        }
        ImGui.SameLine();

        var btnWidth = OmniTheme.Scale(54f);
        var rowWidth = MathF.Max(OmniTheme.Scale(120f),
            ImGui.GetContentRegionAvail().X - btnWidth - OmniTheme.Scale(8f));
        var label = string.Format("{0}  {1}    ({2} · {3})", ok ? "●" : "○", e.Name, kind, e.Id);
        if (OmniControls.RoundedSelectable(label, this.expanded.Contains(e.Id),
                ImGuiSelectableFlags.None, new System.Numerics.Vector2(rowWidth, OmniTheme.Scale(24f))))
        {
            if (!this.expanded.Remove(e.Id)) this.expanded.Add(e.Id);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ok)
            this.Equip(e.Id);

        if (ImGui.BeginPopupContextItem("###ctx"))
        {
            OmniControls.DrawPopupBackground();

            if (ok && OmniControls.RoundedMenuItem("佩戴")) this.Equip(e.Id);
            if (e.AchId is > 0 and <= ushort.MaxValue)
            {
                var watched = IsWatched(e.AchId);
                if (OmniControls.RoundedMenuItem(watched ? "移出成就监视" : "加入成就监视"))
                    ToggleWatch(e.AchId);
            }
            if (OmniControls.RoundedMenuItem("复制称号名")) ImGui.SetClipboardText(e.Name);
            if (OmniControls.RoundedMenuItem(starred ? "取消收藏" : "收藏"))
            {
                if (starred) this.config.Favorites.Remove(e.Id);
                else         this.config.Favorites.Add(e.Id);
                this.Rebuild();
            }

            ImGui.EndPopup();
        }

        if (ok)
        {
            ImGui.SameLine();
            if (OmniControls.SmallButton("佩戴", false,
                    new System.Numerics.Vector2(btnWidth, OmniTheme.Scale(22f))))
                this.Equip(e.Id);
        }

        var showDetail = this.expanded.Contains(e.Id) || !ok;
        if (showDetail)
        {
            ImGui.Indent(OmniTheme.Scale(26f));

            if (e.AchName.Length > 0 || e.AchDesc.Length > 0)
            {
                var line = "获取：";
                if (e.AchName.Length > 0) line += "【" + e.AchName + "】";
                if (e.AchCat.Length  > 0) line += "   " + e.AchCat;
                if (e.AchPoints      > 0) line += string.Format("   {0} 点", e.AchPoints);
                ImGui.TextColored(cDim, line);

                if (e.AchDesc.Length > 0) WrappedTextColored(cDim, e.AchDesc);
            }
            else
            {
                ImGui.TextColored(cDim, "（数据表里没有关联成就，可能是特殊来源）");
            }

            if (!ok && e.AchId is > 0 and <= ushort.MaxValue)
            {
                if (this.TryGetProgress(e.AchId, out var cur, out var max))
                {
                    ImGui.TextColored(cDim, string.Format("进度 {0} / {1}", cur, max));
                    ImGui.SameLine();
                    var frac = max > 0 ? Math.Clamp((float)cur / max, 0f, 1f) : 0f;
                    ImGui.ProgressBar(frac,
                        new System.Numerics.Vector2(OmniTheme.Scale(150f), OmniTheme.Scale(8f)), string.Empty);
                }
                else if (OmniControls.SmallButton("查询进度", false))
                {
                    this.RequestProgress(e.AchId);
                }

                if (IsWatched(e.AchId)) { ImGui.SameLine(); ImGui.TextColored(cOk, "· 已监视"); }
            }

            ImGui.Unindent(OmniTheme.Scale(26f));
        }

        DrawThemedRule(tokens.Border);
        ImGui.PopID();
    }
'''

s = s[:start] + new_method + s[end:]

anchor = "    private bool   built;"
add = '''    private int page = 1;
    private int jumpPage = 1;
    private readonly List<uint> expanded = [];
    private readonly List<string> categories = [];

'''
if anchor in s and "private int page = 1;" not in s:
    s = s.replace(anchor, add + anchor, 1)

s = s.replace('''public sealed class LKami9TitleHelperConfig
{
    public bool WindowOpen { get; set; }
    public bool Ascending  { get; set; } = true;
    public bool ScanOnOpen { get; set; } = true;
    public bool DockToOmni { get; set; } = true;
}''',
'''public sealed class LKami9TitleHelperConfig
{
    public bool WindowOpen { get; set; }
    public bool Ascending  { get; set; } = true;
    public bool ScanOnOpen { get; set; } = true;
    public bool DockToOmni { get; set; } = true;
    public int  PageSize   { get; set; } = 100;
    public string CategoryFilter { get; set; } = string.Empty;
    public List<uint> Favorites { get; set; } = [];
}''')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched: DrawContent + DrawRow + fields + config")
