import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 面板色：确保比窗口底色「略深」，读起来像一块底板（Surface 在浅色主题里偏白，混多了反而更亮）
old_panel = '''    private static System.Numerics.Vector4 PanelColor(OmniToolbox.UI.Theme.ThemeTokens t)
    {
        // 取「背景」与「表面」的中点：既不与窗口同色发亮，也不会显脏
        return System.Numerics.Vector4.Lerp(Opaque(t.Background), Opaque(t.Surface), 0.5f);
    }'''
new_panel = '''    private static System.Numerics.Vector4 PanelColor(OmniToolbox.UI.Theme.ThemeTokens t)
    {
        // 底板要比窗口底色略深一点，才看得出"这是一块板"；再掺一点 Surface 保持主题感
        var bg     = Opaque(t.Background);
        var darker = new System.Numerics.Vector4(bg.X * 0.94f, bg.Y * 0.94f, bg.Z * 0.92f, 1f);
        return System.Numerics.Vector4.Lerp(darker, Opaque(t.Surface), 0.2f);
    }'''
assert old_panel in s
s = s.replace(old_panel, new_panel, 1)

# 2) 设置页：底板铺满整个模块区域（高度取剩余空间），仍无滚动条
old_child = '''        ImGui.BeginChild("###LKami9TitleSettingsPanel", System.Numerics.Vector2.Zero, true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize);'''
new_child = '''        ImGui.BeginChild("###LKami9TitleSettingsPanel",
            new System.Numerics.Vector2(0f, MathF.Max(OmniTheme.Scale(160f), ImGui.GetContentRegionAvail().Y)),
            true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);'''
assert old_child in s
s = s.replace(old_child, new_child, 1)

# 3) 独立窗口的列表：用 ChildBg 铺满（不再靠 FillCurrentWindow 手绘）
old_list = '''        ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);
        FillCurrentWindow(panel, OmniTheme.Scale(tokens.BorderRadius));'''
new_list = '''        using (var listScope = new OmniStyleScope())
        {
            listScope.Color(ImGuiCol.ChildBg, panel);
            listScope.Color(ImGuiCol.Border, tokens.Border);
            listScope.Var(ImGuiStyleVar.ChildRounding, OmniTheme.Scale(tokens.BorderRadius));
            listScope.Var(ImGuiStyleVar.ChildBorderSize, OmniTheme.BorderThickness());

            ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), true,
                ImGuiWindowFlags.None);'''
assert old_list in s
s = s.replace(old_list, new_list, 1)

# 对应的 EndChild 需要收在 using 里：把列表的 EndChild 包进作用域
old_end = '''        ImGui.EndChild();

        // Omni 原生分页器'''
new_end = '''            ImGui.EndChild();
        }

        // Omni 原生分页器'''
assert old_end in s
s = s.replace(old_end, new_end, 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.7.0")
