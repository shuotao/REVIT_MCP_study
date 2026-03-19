---
name: curtain-wall
description: "帷幕牆面板配置：設計帷幕牆面板排列模式並透過網頁預覽確認後套用到 Revit。觸發條件：使用者提到帷幕牆、curtain wall、面板、panel pattern、立面設計、facade design、玻璃面板、curtain grid、面板排列。工具：get_curtain_wall_info、get_curtain_panel_types、apply_panel_pattern。"
---

# 帷幕牆面板配置

## Workflow

1. **檢視**：`get_curtain_wall_info` → 取得網格結構、面板數量、尺寸
2. **瀏覽類型**：`get_curtain_panel_types` → 列出可用面板類型（含顏色/透明度）
3. **設計排列模式**：定義矩陣排列（列 × 欄）指定面板類型
4. **預覽**（選用）：使用網頁預覽工具確認效果
5. **套用**：`apply_panel_pattern` → 將矩陣套用到實際帷幕牆

## Pattern Matrix Format

```json
{
  "wallId": 12345,
  "pattern": [
    ["Type A", "Type B", "Type A"],
    ["Type B", "Type A", "Type B"],
    ["Type A", "Type B", "Type A"]
  ],
  "repeat": true
}
```

`repeat: true` 時，排列模式會在帷幕牆網格上重複展開。

## Design Tips

- 先用 `get_curtain_wall_info` 取得實際網格尺寸
- 矩陣大小應匹配網格的列數與欄數
- 使用對比色面板類型增加視覺層次
- 考慮日照方位決定透明與不透明面板的配置

## Reference

詳見 `domain/curtain-wall-pattern.md`。
