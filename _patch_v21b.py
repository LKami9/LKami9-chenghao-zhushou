import io, re

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# --- 1) 用 OmniStyleScope 取代 ref 计数器 ---
start = s.index("    /// <summary>Omni 本体外观：32 个颜色 + 8 个样式变量")
end = s.index("    private void DrawContent()", start)

helper = '''    /// <summary>Omni 外观作用域：自计数 push，Dispose 按计数 pop —— 结构上不可能泄漏。</summary>
    private sealed class OmniStyleScope : IDisposable
    {
        private int colors;
        private int vars;

        public void Color(ImGuiCol idx, System.Numerics.Vector4 c)
        {
            ImGui.PushStyleColor(idx, c);
            this.colors++;
        }

        public void Var(ImGuiStyleVar v, System.Numerics.Vector2 val)
        {
            ImGui.PushStyleVar(v, val);
            this.vars++;
        }

        public void Var(ImGuiStyleVar v, float val)
        {
            ImGui.PushStyleVar(v, val);
            this.vars++;
        }

        public void Dispose()
        {
            if (this.vars   > 0) ImGui.PopStyleVar(this.vars);
            if (this.colors > 0) ImGui.PopStyleColor(this.colors);
            this.vars   = 0;
            this.colors = 0;
        }
    }

    /// <summary>Omni 本体外观：32 个颜色 + 8 个样式变量（含滚动条），全部交给 scope 回收。</summary>
    private static void PushOmniStyle(OmniStyleScope style)
    {
        var t = OmniTheme.Tokens;

        var textDisabled    = t.Text with { W = t.Text.W * 0.55f };
        var titleBackground = System.Numerics.Vector4.Lerp(t.Background, t.Primary, 0.35f);
        var titleActive     = System.Numerics.Vector4.Lerp(t.Background, t.Accent,  0.65f);

        style.Color(ImGuiCol.WindowBg, Opaque(t.Background));
        style.Color(ImGuiCol.ChildBg, System.Numerics.Vector4.Zero);
        style.Color(ImGuiCol.PopupBg, Opaque(t.Background));
        style.Color(ImGuiCol.Border, t.Border);
        style.Color(ImGuiCol.Text, t.Text);
        style.Color(ImGuiCol.TextDisabled, textDisabled);
        style.Color(ImGuiCol.Button, t.Surface);
        style.Color(ImGuiCol.ButtonHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.ButtonActive, OmniTheme.ActiveBackground);
        style.Color(ImGuiCol.Header, t.Surface);
        style.Color(ImGuiCol.HeaderHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.HeaderActive, t.Accent);
        style.Color(ImGuiCol.FrameBg, t.Surface);
        style.Color(ImGuiCol.FrameBgHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.FrameBgActive, OmniTheme.ActiveBackground);
        style.Color(ImGuiCol.Tab, t.Surface);
        style.Color(ImGuiCol.TabHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.TabActive, t.Accent);
        style.Color(ImGuiCol.TitleBg, titleBackground);
        style.Color(ImGuiCol.TitleBgActive, titleActive);
        style.Color(ImGuiCol.TitleBgCollapsed, titleBackground);
        style.Color(ImGuiCol.ScrollbarBg, Opaque(t.Background));
        style.Color(ImGuiCol.ScrollbarGrab, t.Secondary);
        style.Color(ImGuiCol.ScrollbarGrabHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.ScrollbarGrabActive, t.Accent);
        style.Color(ImGuiCol.CheckMark, t.Success);
        style.Color(ImGuiCol.SliderGrab, t.Secondary);
        style.Color(ImGuiCol.SliderGrabActive, t.Accent);
        style.Color(ImGuiCol.Separator, t.Border);
        style.Color(ImGuiCol.ResizeGrip, t.Surface);
        style.Color(ImGuiCol.ResizeGripHovered, OmniTheme.HoverBackground);
        style.Color(ImGuiCol.ResizeGripActive, t.Accent);

        style.Var(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        style.Var(ImGuiStyleVar.WindowRounding, OmniTheme.Scale(t.BorderRadius));
        style.Var(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness());
        style.Var(ImGuiStyleVar.FrameRounding, OmniTheme.Scale(t.ButtonRadius));
        style.Var(ImGuiStyleVar.PopupRounding, OmniTheme.Scale(t.BorderRadius));
        style.Var(ImGuiStyleVar.ScrollbarRounding, OmniTheme.Scale(t.ButtonRadius));
        style.Var(ImGuiStyleVar.GrabRounding, OmniTheme.Scale(t.ButtonRadius));
        style.Var(ImGuiStyleVar.FramePadding, OmniTheme.Scale(new System.Numerics.Vector2(6f, 4f)));
    }

'''
s = s[:start] + helper + s[end:]

# --- 2) DrawWindow：用 using scope ---
old_open = '''        var self = ImGuiWindowPtr.Null;

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
new_open = '''        var self = ImGuiWindowPtr.Null;

        // Omni 字体 + 配色（含滚动条）：RAII 作用域，Dispose 必定按计数弹回
        using var style = new OmniStyleScope();
        PushOmniStyle(style);

        this.omniFont ??= OmniFonts.GetUIFont();
        IDisposable? fontScope = this.omniFont is { Available: true } ? this.omniFont.Push() : null;

        try
        {
        if (ImGui.Begin(WindowId, ref open,'''
assert old_open in s
s = s.replace(old_open, new_open, 1)

old_close = '''        ImGui.End();
        }
        finally
        {
            if (pushedVars   > 0) ImGui.PopStyleVar(pushedVars);
            if (pushedColors > 0) ImGui.PopStyleColor(pushedColors);
            fontScope?.Dispose();
        }'''
new_close = '''        ImGui.End();
        }
        finally
        {
            fontScope?.Dispose();
        }'''
assert old_close in s
s = s.replace(old_close, new_close, 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched: OmniStyleScope")
