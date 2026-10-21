import io, re

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 缺字形符号 ● / ○ 换成文字标记（ImGui 无字体回退，缺字会渲染成 "="）
s = s.replace('(ok ? "●" : "○")', '(ok ? "[已解锁]" : "[未解锁]")')
s = s.replace('string.Format("{0}  {1}    ({2} · {3})", ok ? "●" : "○", e.Name, kind, e.Id)',
              'string.Format("{0}  {1}    ({2} · {3})", ok ? "[已解锁]" : "[未解锁]", e.Name, kind, e.Id)')
s = re.sub(r'ok \? "●" : "○"', 'ok ? "[已解锁]" : "[未解锁]"', s)
s = s.replace('"●"', '"[已解锁]"').replace('"○"', '"[未解锁]"')

# 2) 列表不再出现滚动条；每页默认调小，靠分页器翻页
s = s.replace('ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);',
              'ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false,\n            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);')
s = s.replace('public int  PageSize   { get; set; } = 100;',
              'public int  PageSize   { get; set; } = 40;')

# 3) 默认不贴靠 Omni，可自由拖动；层级只在实际贴靠时才排到 Omni 后面
s = s.replace('public bool DockToOmni { get; set; } = true;',
              'public bool DockToOmni { get; set; } = false;')

s = s.replace('''        if (!self.IsNull)
        {
            if (!omni.IsNull) ImGuiP.BringWindowToDisplayBehind(self, omni);
            else              ImGuiP.BringWindowToDisplayBack(self);
        }''',
'''        if (!self.IsNull && this.config.DockToOmni && !omni.IsNull)
            ImGuiP.BringWindowToDisplayBehind(self, omni);''')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)

print("risky glyphs left:", [c for c in ("●", "○") if c in s])
print("NoScrollWithMouse:", s.count("NoScrollWithMouse"), "| PageSize 默认 40:", "= 40;" in s)
print("DockToOmni 默认:", "DockToOmni { get; set; } = false;" in s)
