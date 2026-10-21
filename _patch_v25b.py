import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 面板色 helper
helper = '''    /// <summary>面板色：比主题背景略深一档，用来做背景板，避免整片同色发亮。</summary>
    private static System.Numerics.Vector4 PanelColor(OmniToolbox.UI.Theme.ThemeTokens t)
    {
        var bg   = t.Background;
        var dark = new System.Numerics.Vector4(bg.X * 0.90f, bg.Y * 0.90f, bg.Z * 0.88f, 1f);
        return System.Numerics.Vector4.Lerp(dark, System.Numerics.Vector4.One, 0.06f);
    }

'''
anchor = "    private void DrawRow("
assert anchor in s
s = s.replace(anchor, helper + anchor, 1)

# 2) 独立窗口内容区用面板色
s = s.replace("                    var panel = Opaque(tokens.Background);",
              "                    var panel = PanelColor(tokens);")

# 3) 设置页：包一层背景板
old_head = '''        var tokens = OmniTheme.Tokens;
        var back   = Opaque(tokens.Background);

        ImGui.TextColored(Opaque(tokens.Accent), "LKami9 称号助手");
        WrappedTextColored(Opaque(tokens.Secondary),
            "主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");

        var changed = false;'''
new_head = '''        var tokens  = OmniTheme.Tokens;
        var panel   = PanelColor(tokens);
        var cAccent = Opaque(tokens.Accent);
        var cText   = Opaque(tokens.Text);
        var cDim    = Opaque(tokens.Secondary);

        // 背景板：否则内容直接铺在 Dalamud 的浅色底上，整片发亮刺眼
        ImGui.PushStyleColor(ImGuiCol.ChildBg, panel);
        ImGui.PushStyleColor(ImGuiCol.Border, tokens.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, OmniTheme.Scale(tokens.BorderRadius));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, OmniTheme.BorderThickness());

        ImGui.BeginChild("###LKami9TitleSettingsPanel",
            new System.Numerics.Vector2(0f, MathF.Max(OmniTheme.Scale(140f), ImGui.GetContentRegionAvail().Y)),
            true, ImGuiWindowFlags.None);

        ImGui.TextColored(cAccent, "LKami9 称号助手");
        WrappedTextColored(cText,
            "主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");

        var changed = false;'''
assert old_head in s
s = s.replace(old_head, new_head, 1)

# 4) 设置页结尾收尾
old_tail = '''        ImGui.Spacing();
        ImGui.TextColored(Readable(tokens.Secondary, back), this.status);

        // 按官方规范：只有配置真的变了才返回 true，让宿主保存
        return changed;'''
if old_tail in s:
    new_tail = '''        ImGui.Spacing();
        ImGui.TextColored(cDim, this.status);

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // 按官方规范：只有配置真的变了才返回 true，让宿主保存
        return changed;'''
    s = s.replace(old_tail, new_tail, 1)
else:
    # 兜底：定位最后的状态文字行
    old2 = '''        ImGui.TextColored(Opaque(tokens.Secondary), this.status);
        return changed;'''
    assert old2 in s, "找不到设置页结尾"
    s = s.replace(old2, '''        ImGui.TextColored(cDim, this.status);

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        return changed;''', 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.5.0")
