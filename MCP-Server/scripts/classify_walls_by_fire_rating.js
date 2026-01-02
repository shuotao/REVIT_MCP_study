/**
 * 牆體防火防煙性能視覺化腳本
 * 
 * 此腳本會：
 * 1. 取得當前視圖
 * 2. 查詢所有牆體
 * 3. 分析防火防煙性能參數
 * 4. 根據參數值應用不同顏色
 * 5. 產生統計報告
 */

// ============================================================================
// 顏色映射配置
// ============================================================================

const COLOR_MAP = {
    "2小時": { color: { r: 0, g: 180, b: 0 }, transparency: 20, label: "🟢 2小時防火" },
    "1.5小時": { color: { r: 100, g: 220, b: 100 }, transparency: 30, label: "🟢 1.5小時防火" },
    "1小時": { color: { r: 255, g: 255, b: 0 }, transparency: 30, label: "🟡 1小時防火" },
    "0.5小時": { color: { r: 255, g: 165, b: 0 }, transparency: 30, label: "🟠 0.5小時防火" },
    "無防火": { color: { r: 100, g: 150, b: 255 }, transparency: 40, label: "🔵 無防火" },
    "未設定": { color: { r: 200, g: 0, b: 200 }, transparency: 50, label: "🟣 未設定" }
};

// 可能的參數名稱（按優先順序）
const PARAMETER_NAMES = [
    "防火防煙性能",
    "防火時效",
    "Fire Rating",
    "FireRating",
    "防火性能"
];

// ============================================================================
// 步驟 1: 取得當前視圖
// ============================================================================

console.log("步驟 1: 取得當前視圖...");
const currentView = await get_active_view();
console.log(`✓ 當前視圖: ${currentView.Name} (ID: ${currentView.Id})`);

// ============================================================================
// 步驟 2: 查詢所有牆體
// ============================================================================

console.log("\n步驟 2: 查詢視圖中的所有牆體...");
const wallsResult = await query_elements({
    category: "Walls",
    viewId: currentView.Id
});

console.log(`✓ 找到 ${wallsResult.TotalFound} 面牆`);

if (wallsResult.TotalFound === 0) {
    console.log("❌ 當前視圖中沒有牆體元素");
    throw new Error("沒有找到牆體");
}

// ============================================================================
// 步驟 3: 分析防火防煙性能參數
// ============================================================================

console.log("\n步驟 3: 分析防火防煙性能參數...");

const wallData = [];
const parameterValueDistribution = {};

for (const wall of wallsResult.Elements) {
    console.log(`  分析牆體 ID: ${wall.ElementId}...`);

    // 取得牆體詳細資訊
    const wallInfo = await get_element_info({ elementId: wall.ElementId });

    // 嘗試找到防火防煙性能參數
    let fireRatingParam = null;
    let fireRatingValue = "未設定";

    for (const paramName of PARAMETER_NAMES) {
        fireRatingParam = wallInfo.Parameters.find(p => p.Name === paramName);
        if (fireRatingParam && fireRatingParam.Value) {
            fireRatingValue = fireRatingParam.Value.trim();
            break;
        }
    }

    // 記錄資料
    wallData.push({
        elementId: wall.ElementId,
        name: wallInfo.Name || "未命名",
        fireRating: fireRatingValue,
        parameterName: fireRatingParam ? fireRatingParam.Name : "未找到"
    });

    // 統計分布
    if (!parameterValueDistribution[fireRatingValue]) {
        parameterValueDistribution[fireRatingValue] = 0;
    }
    parameterValueDistribution[fireRatingValue]++;
}

console.log("\n✓ 參數分析完成");
console.log("參數值分布:");
for (const [value, count] of Object.entries(parameterValueDistribution)) {
    console.log(`  - ${value}: ${count} 面牆`);
}

// ============================================================================
// 步驟 4: 動態建立顏色映射（如果需要）
// ============================================================================

console.log("\n步驟 4: 準備顏色映射...");

// 取得所有唯一的參數值
const uniqueValues = Object.keys(parameterValueDistribution);
const finalColorMap = {};

// 使用預定義的顏色映射
for (const value of uniqueValues) {
    if (COLOR_MAP[value]) {
        finalColorMap[value] = COLOR_MAP[value];
    } else {
        // 如果參數值不在預設映射中，使用動態分配
        // 使用灰色系列作為備用
        finalColorMap[value] = {
            color: { r: 150, g: 150, b: 150 },
            transparency: 40,
            label: `⚪ ${value}`
        };
    }
}

console.log("✓ 顏色映射表:");
for (const [value, config] of Object.entries(finalColorMap)) {
    console.log(`  ${config.label}: RGB(${config.color.r}, ${config.color.g}, ${config.color.b})`);
}

// ============================================================================
// 步驟 5: 應用圖形覆寫
// ============================================================================

console.log("\n步驟 5: 應用顏色覆寫...");

let successCount = 0;
let failedCount = 0;

for (const wall of wallData) {
    try {
        const colorConfig = finalColorMap[wall.fireRating];

        await override_element_graphics({
            elementId: wall.elementId,
            viewId: currentView.Id,
            surfaceFillColor: colorConfig.color,
            transparency: colorConfig.transparency
        });

        successCount++;
        console.log(`  ✓ 已覆寫 ID ${wall.elementId} (${wall.fireRating})`);
    } catch (error) {
        failedCount++;
        console.log(`  ❌ 失敗 ID ${wall.elementId}: ${error.message}`);
    }
}

console.log(`\n✓ 覆寫完成: ${successCount} 成功, ${failedCount} 失敗`);

// ============================================================================
// 步驟 6: 產生最終報告
// ============================================================================

console.log("\n" + "=".repeat(70));
console.log("牆體防火防煙性能視覺化報告");
console.log("=".repeat(70));

console.log(`\n視圖: ${currentView.Name} (ID: ${currentView.Id})`);
console.log(`總牆體數量: ${wallsResult.TotalFound} 面`);

console.log("\n防火性能分布:");
for (const [value, count] of Object.entries(parameterValueDistribution)) {
    const config = finalColorMap[value];
    const percentage = ((count / wallsResult.TotalFound) * 100).toFixed(1);
    console.log(`  ${config.label}: ${count} 面 (${percentage}%)`);
}

console.log("\n顏色映射表:");
for (const [value, config] of Object.entries(finalColorMap)) {
    console.log(`  ${config.label}`);
    console.log(`    RGB: (${config.color.r}, ${config.color.g}, ${config.color.b})`);
    console.log(`    透明度: ${config.transparency}%`);
}

console.log("\n清除顏色覆寫指令:");
const allWallIds = wallData.map(w => w.elementId);
console.log(`clear_element_override({ elementIds: [${allWallIds.join(', ')}], viewId: ${currentView.Id} })`);

console.log("\n" + "=".repeat(70));
console.log("✓ 執行完成！請檢查 Revit 視圖中的顏色標記。");
console.log("=".repeat(70));

// 回傳完整資料供參考
return {
    view: currentView,
    totalWalls: wallsResult.TotalFound,
    distribution: parameterValueDistribution,
    colorMap: finalColorMap,
    wallData: wallData,
    successCount: successCount,
    failedCount: failedCount,
    clearCommand: `clear_element_override({ elementIds: [${allWallIds.join(', ')}], viewId: ${currentView.Id} })`
};
