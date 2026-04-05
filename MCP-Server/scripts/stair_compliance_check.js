import { RevitSocketClient } from '../build/socket.js';
import fs from 'fs';
import path from 'path';

/**
 * 樓梯法規自動化檢核腳本
 * 根據 domain/stair-compliance-check.md 流程實作
 */

async function runStairCompliance() {
    const client = new RevitSocketClient('localhost', 8964);
    
    // 使用者提供的參數
    const config = {
        usage: "一般住宅",
        areaTrigger: true, // 地上各層居室 > 200m2
        classification: {
            field: "類型標記",
            accessible: "A",
            general: "B"
        },
        thresholds: {
            general: { width: 1200, riser: 200, tread: 240 },
            accessible: { riser: 180, tread: 240, ratioMin: 550, ratioMax: 650 }
        },
        markerMode: "dimensionText"
    };

    try {
        console.log('🔌 正在連線至 Revit...');
        await client.connect();

        // 步驟 0: 取得當前視圖
        const activeViewRes = await client.sendCommand('get_active_view', {});
        if (!activeViewRes.success) throw new Error("無法取得當前視圖");
        const viewId = activeViewRes.data.ElementId;
        const viewName = activeViewRes.data.Name;
        console.log(`📍 檢查範圍：作用視圖 [${viewName}] (ID: ${viewId})`);

        // 步驟 1: 取得樓梯欄位名稱，確保不猜測參數
        console.log('🔍 正在取得樓梯品類欄位...');
        const fieldsRes = await client.sendCommand('get_category_fields', { category: "Stairs" });
        const fields = fieldsRes.data.Fields || [];
        
        // 尋找對應的參數名 (Revit 不同版本或語系可能不同)
        const riserKeywords = ["實際級高", "實際豎板高度", "Actual Riser Height", "實際豎板"];
        const treadKeywords = ["實際級深", "實際踏面板深度", "Actual Tread Depth", "實際踏面"];
        
        const riserField = fields.find(f => riserKeywords.some(k => f.includes(k))) || "實際級高";
        const treadField = fields.find(f => treadKeywords.some(k => f.includes(k))) || "實際級深";
        const typeMarkField = config.classification.field;

        // 步驟 2: 查詢視圖中的樓梯
        console.log(`🔍 使用參數：級高 [${riserField}]、級深 [${treadField}]`);
        console.log('📦 正在收集視圖中的樓梯...');
        const stairsRes = await client.sendCommand('query_elements', {
            category: "Stairs",
            viewId: viewId,
            returnFields: [typeMarkField, riserField, treadField, "Base Level", "Top Level"]
        });

        const stairs = stairsRes.data.Elements || [];
        console.log(`✅ 找到 ${stairs.length} 座樓梯。`);

        const report = {
            total: stairs.length,
            passed: 0,
            failed: 0,
            manual: 0,
            violations: []
        };

        /**
         * 單位自動校正助手 (Revit 參數可能回傳 cm 或 mm)
         * 一般級深約 200-300mm，級高約 150-200mm。
         * 若數值 < 100，極高機率其單位為 cm。
         */
        const normalizeToMm = (val) => {
            const num = parseFloat(val);
            if (isNaN(num)) return 0;
            if (num > 0 && num < 100) return num * 10; // cm -> mm
            return num;
        };

        for (const stair of stairs) {
            const stairId = stair.ElementId;
            const typeMark = stair[typeMarkField] || "";
            let category = "Unknown";
            
            if (typeMark === config.classification.accessible) category = "Accessible";
            else if (typeMark === config.classification.general) category = "General";
            else {
                console.warn(`⚠️ 樓梯 ${stairId} 類型標記 [${typeMark}] 無法辨識，列為人工確認。`);
                report.manual++;
                continue;
            }

            console.log(`\n📐 正在檢核樓梯 ${stairId} (${category})...`);
            
            // A. 寬度檢查 (取得實測值)
            const widthRes = await client.sendCommand('get_stair_actual_width', { stairId });
            const actualWidth = widthRes.data.MinActualWidth;
            
            // B. 級高/級深讀取並標準化為 mm
            const rawRiser = stair[riserField];
            const rawTread = stair[treadField];
            const actualRiser = normalizeToMm(rawRiser);
            const actualTread = normalizeToMm(rawTread);

            const issues = [];
            
            if (category === "General") {
                const limit = config.thresholds.general;
                if (actualWidth < limit.width) issues.push(`淨寬不足: 實測 ${actualWidth}mm < 應有 ${limit.width}mm`);
                if (actualRiser > limit.riser) issues.push(`級高超限: 實測 ${actualRiser}mm > 應有 ${limit.riser}mm`);
                if (actualTread < limit.tread) issues.push(`級深不足: 實測 ${actualTread}mm < 應有 ${limit.tread}mm`);
            } else {
                const limit = config.thresholds.accessible;
                if (actualRiser > limit.riser) issues.push(`[無障礙] 級高超限: 實測 ${actualRiser}mm > 應有 ${limit.riser}mm`);
                if (actualTread < limit.tread) issues.push(`[無障礙] 級深不足: 實測 ${actualTread}mm < 應有 ${limit.tread}mm`);
                
                const ratio = 2 * actualRiser + actualTread;
                if (ratio < limit.ratioMin || ratio > limit.ratioMax) {
                    issues.push(`[無障礙] 比例不符: 2R+T = ${ratio} (應界於 ${limit.ratioMin}~${limit.ratioMax})`);
                }
            }

            // C. 淨高檢查
            console.log(`  🔍 執行淨高碰撞檢核 (190cm)...`);
            const headroomRes = await client.sendCommand('check_stair_headroom', { 
                stairId, 
                headroomLimitCm: 190,
                finishThicknessCm: 0
            });
            if (headroomRes.data.Failures > 0) {
                issues.push(`淨高不足: 發現 ${headroomRes.data.Failures} 處碰撞點`);
            }

            if (issues.length > 0) {
                report.failed++;
                console.log(`❌ 不合格: \n  - ${issues.join('\n  - ')}`);
                report.violations.push({ id: stairId, category, issues });

                // 步驟 5: 如果不合格且模式為尺寸+文字，寫入參數 (目前先寫入註解欄位作為回饋)
                await client.sendCommand('modify_element_parameter', {
                    elementId: stairId,
                    parameterName: "樓梯檢查成果", // 建議參數
                    value: `不合格: ${issues.join('; ')}`
                }).catch(() => {
                    // 若無此參數則嘗試寫入「註釋」
                    client.sendCommand('modify_element_parameter', {
                        elementId: stairId,
                        parameterName: "註釋",
                        value: `【樓梯檢核】不合格: ${issues.join('; ')}`
                    });
                });
            } else {
                report.passed++;
                console.log(`✅ 合格`);
                await client.sendCommand('modify_element_parameter', {
                    elementId: stairId,
                    parameterName: "樓梯檢查成果",
                    value: "合格"
                }).catch(() => {});
            }
        }

        // 步驟 6: 輸出總結報告
        console.log('\n' + '='.repeat(40));
        console.log('       樓梯法規整合檢查報告');
        console.log('='.repeat(40));
        console.log(`檢查日期: ${new Date().toLocaleString()}`);
        console.log(`總樓梯數: ${report.total}`);
        console.log(`✅ 通過: ${report.passed}`);
        console.log(`❌ 不合格: ${report.failed}`);
        console.log(`⚠️ 待確認: ${report.manual}`);
        console.log('='.repeat(40));
        
        if (report.violations.length > 0) {
            console.log('\n缺失詳細清單:');
            report.violations.forEach(v => {
                console.log(`- Stair ${v.id} (${v.category}):`);
                v.issues.forEach(i => console.log(`  * ${i}`));
            });
        }

    } catch (error) {
        console.error('❌ 執行出錯:', error.message);
    } finally {
        client.disconnect();
    }
}

runStairCompliance();
