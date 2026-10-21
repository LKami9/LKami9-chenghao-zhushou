import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 统一的“面板色”：比窗口底色略深，形成背景板（浅色主题下不再整片发亮）
helper = '''    /// <summary>面板色：比主题背景略深一档，用来做背景板，避免整片同色发亮。</summary>
    private static System.Numerics.Vector4 PanelColor(OmniToolbox.UI.Theme.ThemeTokens t)
    {
        var bg = t.Background;
        var dark = new System.Numerics.Vector4(bg.X * 0.90f, bg.Y * 0.90f, bg.Z * 0.88f, 1f);
        return System.Numerics.Vector4.Lerp(dark, System.Numerics.Vector4.One, 0.06f);
    }

'''
anchor = "    private void DrawRow("
assert anchor in s
s = s.replace(anchor, helper + anchor, 1)

# 2) 独立窗口的内容区：改用面板色
s = s.replace("                    var panel = Opaque(tokens.Background);",
              "                    var panel = PanelColor(tokens);")

# 3) 列表内部填充也用面板色（原来用 tokens.Background）
s = s.replace("        FillCurrentWindow(panel, OmniTheme.Scale(tokens.BorderRadius));",
              "        FillCurrentWindow(panel, OmniTheme.Scale(tokens.BorderRadius));")

# 4) 设置页：整体套一层背景板（child + 边框），文字用主题前景色
old_settings_head = '''    public override bool DrawSettings()
    {'''
new_settings_head = '''    public override bool DrawSettings()
    {'''
assert old_settings_head in s

# 在 DrawSettings 内，把内容包进带背景板的 child
old_body = '''        var tokens = OmniTheme.Tokens;
        var back   = Opaque(tokens.Background);

        ImGui.TextColored(Readable(tokens.Accent, back), "LKami9 称号助手");'''
new_body = '''        var tokens = OmniTheme.Tokens;
        var panel  = PanelColor(tokens);
        var cText  = Opaque(tokens.Text);
        var cAccent = Opaque(tokens.Accent);
        var cDim   = Opaque(tokens.Secondary);

        // 设置页背景板（否则铺在 Dalamud 的浅色底上会整片刺眼）
        ImGui.PushStyleColor(ImGuiCol.ChildBg, panel);
        ImGui.PushStyleColor(ImGuiCol.Border, tokens.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, OmniTheme.Scale(tokens.BorderRadius));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, OmniTheme.BorderThickness());

        var settingsChanged = false;

        ImGui.BeginChild("###LKami9TitleSettingsPanel",
            new System.Numerics.Vector2(0f, MathF.Max(OmniTheme.Scale(120f), ImGui.GetContentRegionAvail().Y)),
            true, ImGuiWindowFlags.None);

        ImGui.TextColored(cAccent, "LKami9 称号助手");'''
assert old_body in s
s = s.replace(old_body, new_body, 1)

# 设置页结尾：收尾 + 返回 changed
old_tail = '''        ImGui.Spacing();
        ImGui.TextColored(Readable(tokens.Secondary, back), this.status);

        // 按官方规范：只有配置真的变了才返回 true，让宿主保存
        return changed;'''
new_tail = '''        ImGui.Spacing();
        ImGui.TextColored(cDim, this.status);

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // 按官方规范：只有配置真的变了才返回 true，让宿主保存
        return changed || settingsChanged;'''
assert old_tail in s
s = s.replace(old_tail, new_tail, 1)

# 设置页里正文说明文字改用 cText
s = s.replace('''        ImGui.TextWrapped("主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");''',
              '''        ImGui.TextColored(cText, "主界面在独立窗口中，配色与窗口样式跟随 Omni 本体主题。");''')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.5.0")
