import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 不再用 NoBackground（让 ImGui 用我们推的 WindowBg 画底）
s = s.replace(
    "ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus))",
    "ImGuiWindowFlags.NoBringToFrontOnFocus))")

# 2) 去掉窗口整体手绘底（由 WindowBg 负责）
old_frame_fill = '''            var draw      = ImGui.GetWindowDrawList();

            draw.AddRectFilled(framePos, framePos + frameSize,
                ImGui.GetColorU32(tokens.Background with { W = 1f }), radius);
'''
new_frame_fill = '''            var draw = ImGui.GetWindowDrawList();
'''
assert old_frame_fill in s
s = s.replace(old_frame_fill, new_frame_fill, 1)

# 3) 去掉内容区手绘底（由 ChildBg / FillCurrentWindow 负责）
old_panel = '''                    var panel = Opaque(tokens.Background);
                    draw.AddRectFilled(contentPos, contentPos + contentSize,
                        ImGui.GetColorU32(panel), radius);
'''
assert old_panel in s
s = s.replace(old_panel, "                    var panel = Opaque(tokens.Background);\n", 1)

# 4) 在 Begin 之前插入「Omni 字体 + Omni 配色/滚动条」作用域
old_try = '''        var self = ImGuiWindowPtr.Null;
        if (ImGui.Begin(WindowId, ref open,'''
new_try = '''        var self = ImGuiWindowPtr.Null;

        // Omni 字体 + 配色/滚动条：按计数 push，finally 按同一计数 pop（结构上不可能泄漏）
        var pushedColors = 0;
        var pushedVars   = 0;
        IDisposable? fontScope = null;

        try
        {
            this.omniFont ??= OmniFonts.GetUIFont();
            if (this.omniFont is { Available: true })
                fontScope = this.omniFont.Push();

            PushOmniStyle(ref pushedColors, ref pushedVars);

        if (ImGui.Begin(WindowId, ref open,'''
assert old_try in s
s = s.replace(old_try, new_try, 1)

# 5) End() 之后关闭作用域
old_end = '''        ImGui.End();

        if (!self.IsNull)'''
new_end = '''        ImGui.End();
        }
        finally
        {
            if (pushedVars   > 0) ImGui.PopStyleVar(pushedVars);
            if (pushedColors > 0) ImGui.PopStyleColor(pushedColors);
            fontScope?.Dispose();
        }

        if (!self.IsNull)'''
assert old_end in s
s = s.replace(old_end, new_end, 1)

# 6) 字段：字体句柄
anchor = "    private int page = 1;"
if "omniFont" not in s.split("private int page = 1;")[0]:
    s = s.replace(anchor, "    private Dalamud.Interface.ManagedFontAtlas.IFontHandle? omniFont;\n" + anchor, 1)

# 7) 新增 PushOmniStyle（带计数），插在 DrawContent 之前
helper = '''    /// <summary>Omni 本体外观：32 个颜色 + 8 个样式变量，按计数推送，调用方负责按同一计数弹出。</summary>
    private static void PushOmniStyle(ref int colors, ref int vars)
    {
        var t = OmniTheme.Tokens;

        var textDisabled    = t.Text with { W = t.Text.W * 0.55f };
        var titleBackground = System.Numerics.Vector4.Lerp(t.Background, t.Primary, 0.35f);
        var titleActive     = System.Numerics.Vector4.Lerp(t.Background, t.Accent,  0.65f);

        void C(ImGuiCol idx, System.Numerics.Vector4 c)
        {
            ImGui.PushStyleColor(idx, c);
            colors++;
        }

        void V(ImGuiStyleVar v, System.Numerics.Vector2 val)
        {
            ImGui.PushStyleVar(v, val);
            vars++;
        }

        void VF(ImGuiStyleVar v, float val)
        {
            ImGui.PushStyleVar(v, val);
            vars++;
        }

        C(ImGuiCol.WindowBg, Opaque(t.Background));
        C(ImGuiCol.ChildBg, System.Numerics.Vector4.Zero);
        C(ImGuiCol.PopupBg, Opaque(t.Background));
        C(ImGuiCol.Border, t.Border);
        C(ImGuiCol.Text, t.Text);
        C(ImGuiCol.TextDisabled, textDisabled);
        C(ImGuiCol.Button, t.Surface);
        C(ImGuiCol.ButtonHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.ButtonActive, OmniTheme.ActiveBackground);
        C(ImGuiCol.Header, t.Surface);
        C(ImGuiCol.HeaderHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.HeaderActive, t.Accent);
        C(ImGuiCol.FrameBg, t.Surface);
        C(ImGuiCol.FrameBgHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.FrameBgActive, OmniTheme.ActiveBackground);
        C(ImGuiCol.Tab, t.Surface);
        C(ImGuiCol.TabHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.TabActive, t.Accent);
        C(ImGuiCol.TitleBg, titleBackground);
        C(ImGuiCol.TitleBgActive, titleActive);
        C(ImGuiCol.TitleBgCollapsed, titleBackground);
        C(ImGuiCol.ScrollbarBg, Opaque(t.Background));
        C(ImGuiCol.ScrollbarGrab, t.Secondary);
        C(ImGuiCol.ScrollbarGrabHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.ScrollbarGrabActive, t.Accent);
        C(ImGuiCol.CheckMark, t.Success);
        C(ImGuiCol.SliderGrab, t.Secondary);
        C(ImGuiCol.SliderGrabActive, t.Accent);
        C(ImGuiCol.Separator, t.Border);
        C(ImGuiCol.ResizeGrip, t.Surface);
        C(ImGuiCol.ResizeGripHovered, OmniTheme.HoverBackground);
        C(ImGuiCol.ResizeGripActive, t.Accent);

        V(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        VF(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(t.BorderRadius));
        VF(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness());
        VF(ImGuiStyleVar.FrameRounding, OmniTheme.Scale(t.ButtonRadius));
        VF(ImGuiStyleVar.PopupRounding, OmniTheme.Scale(t.BorderRadius));
        VF(ImGuiStyleVar.ScrollbarRounding, OmniTheme.Scale(t.ButtonRadius));
        VF(ImGuiStyleVar.GrabRounding, OmniTheme.Scale(t.ButtonRadius));
        V(ImGuiStyleVar.FramePadding, OmniTheme.Scale(new System.Numerics.Vector2(6f, 4f)));
    }

'''
anchor2 = "    private void DrawContent()"
assert anchor2 in s
s = s.replace(anchor2, helper + anchor2, 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.1.0")
