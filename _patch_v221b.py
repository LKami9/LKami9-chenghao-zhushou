import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) MaterialPainter 是 internal，退回纯色填充
old = '''
            // 与 Omni 本体一致：优先用它的材质绘制（玻璃/高光/描边），失败再退回纯色
            var fillColor = Opaque(tokens.Background);
            try
            {
                MaterialPainter.Draw(draw, framePos, frameSize, fillColor, radius, 1f,
                    false, ImDrawFlags.RoundCornersAll);
            }
            catch
            {
                draw.AddRectFilled(framePos, framePos + frameSize,
                    ImGui.GetColorU32(fillColor), radius);
            }
'''
new = '''
            draw.AddRectFilled(framePos, framePos + frameSize,
                ImGui.GetColorU32(Opaque(tokens.Background)), radius);
'''
assert old in s
s = s.replace(old, new, 1)

# 2) 间距/内边距改用 Omni 主题里的值（照抄它的设置）
s = s.replace(
    '        style.Var(ImGuiStyleVar.FramePadding, OmniTheme.Scale(new System.Numerics.Vector2(6f, 4f)));',
    '        style.Var(ImGuiStyleVar.FramePadding, OmniTheme.Scale(t.FramePadding));\n'
    '        style.Var(ImGuiStyleVar.ItemSpacing, OmniTheme.Scale(t.ItemSpacing));')

# 3) 内容内边距用 Omni 的 WindowInset / ContentGap
s = s.replace('        var pad     = OmniTheme.Scale(12f);',
              '        var pad     = MathF.Max(OmniTheme.Scale(8f), OmniTheme.WindowInset());')
s = s.replace('        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.Scale(6f)));',
              '        ImGui.Dummy(new System.Numerics.Vector2(0f, OmniTheme.ContentGap()));')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.2.1b")
