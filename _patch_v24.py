import io

p = "Modules/LKami9TitleHelper.cs"
s = io.open(p, encoding="utf-8").read()

# 1) 工具栏：清空后面加「随机称号」按钮（就是用户框出来的那块空白）
old_btn = '''        ImGui.SameLine();
        ImGui.BeginDisabled(this.search.Length == 0 && this.config.CategoryFilter.Length == 0);
        if (OmniControls.SmallButton("清空", false))
        {
            this.search = string.Empty;
            this.config.CategoryFilter = string.Empty;
            this.page = 1;
            this.Rebuild();
        }
        ImGui.EndDisabled();
'''
new_btn = old_btn + '''
        ImGui.SameLine();
        if (OmniControls.SmallButton("随机称号", false,
                new System.Numerics.Vector2(OmniTheme.Scale(92f), OmniTheme.Scale(24f))))
            this.EquipRandom();
'''
assert old_btn in s
s = s.replace(old_btn, new_btn, 1)

# 2) 随机佩戴实现（避开上一次抽到的那个）
anchor = "    private unsafe void Equip(uint id)"
impl = '''    private uint lastRandomId;

    private void EquipRandom()
    {
        var pool = this.all.Where(x => this.IsUnlocked(x.Id) && x.Id != this.lastRandomId).ToList();
        if (pool.Count == 0)
        {
            this.status = "没有可用的已解锁称号。";
            return;
        }

        var pick = pool[Random.Shared.Next(pool.Count)];
        this.lastRandomId = pick.Id;
        this.Equip(pick.Id);
        this.status = string.Format("随机称号：{0}（{1}）", pick.Name, pick.Id);
    }

'''
assert anchor in s
s = s.replace(anchor, impl + anchor, 1)

# 3) 命令支持
s = s.replace('''            case "equip":''',
'''            case "random":
                this.EquipRandom();
                this.ToggleWindow(true);
                return true;

            case "equip":''')

s = s.replace('''            new("按 ID 佩戴称号", "/omni LKami9TitleHelper equip 123"),''',
'''            new("按 ID 佩戴称号", "/omni LKami9TitleHelper equip 123"),
            new("随机佩戴已解锁称号", "/omni LKami9TitleHelper random"),''')

io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("patched v2.4.0")
