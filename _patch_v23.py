import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 配置版本 + 旧配置迁移（老配置里存着 DockToOmni=true / PageSize=100）
s = s.replace('''    public int  PageSize   { get; set; } = 40;''',
              '''    public int  ConfigVersion { get; set; }
    public int  PageSize   { get; set; } = 40;''')

# OnEnable 里做迁移
old_onenable = '''        if (this.config.ScanOnOpen || !this.built) this.Build();'''
new_onenable = '''        this.MigrateConfig();

        if (this.config.ScanOnOpen || !this.built) this.Build();'''
assert old_onenable in s
s = s.replace(old_onenable, new_onenable, 1)

# 迁移方法
anchor = "    private void ToggleWindow(bool open)"
migrate = '''    /// <summary>旧配置迁移：v2.3 起默认窗口自由拖动、每页 40 条。</summary>
    private void MigrateConfig()
    {
        if (this.config.ConfigVersion >= 2) return;

        this.config.DockToOmni  = false;
        if (this.config.PageSize > 60) this.config.PageSize = 40;
        this.config.ConfigVersion = 2;
    }

'''
assert anchor in s
s = s.replace(anchor, migrate + anchor, 1)

# 2) 贴靠状态下，用户按住标题栏拖动即解锁（否则每帧被强制位置，永远拖不动）
old_drag = '''            if (chrome.CloseClicked)'''
new_drag = '''            if (this.config.DockToOmni)
            {
                // 贴靠是每帧强制位置；只要用户在标题栏按下并拖动，就自动解除贴靠
                var titleHeight = OmniTheme.TitleBarHeight();
                var inTitle = ImGui.IsWindowHovered()
                           && ImGui.GetIO().MousePos.Y < framePos.Y + titleHeight;

                if (inTitle && ImGui.IsMouseDragging(ImGuiMouseButton.Left, OmniTheme.Scale(3f)))
                    this.config.DockToOmni = false;
            }

            if (chrome.CloseClicked)'''
assert old_drag in s
s = s.replace(old_drag, new_drag, 1)

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.3.0")
