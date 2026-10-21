import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 滚动条：保留列表的，去掉内容容器的那个
s = s.replace(
    'ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false,\n            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);',
    'ImGui.BeginChild("###LKami9TitleList", new System.Numerics.Vector2(0f, listHeight), false, ImGuiWindowFlags.None);')

s = s.replace(
    'ImGui.BeginChild("###LKami9TitleContent", contentSize, false, ImGuiWindowFlags.None);',
    'ImGui.BeginChild("###LKami9TitleContent", contentSize, false,\n                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);')

# 2) 窗口底：用 Omni 的材质绘制（MaterialPainter），失败则退回纯色填充
old = '''            var draw = ImGui.GetWindowDrawList();
'''
new = '''            var draw = ImGui.GetWindowDrawList();

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
assert old in s
s = s.replace(old, new, 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.2.1")
