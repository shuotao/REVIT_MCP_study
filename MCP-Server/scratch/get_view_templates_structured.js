import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Connected to Revit...');
    const command = {
        CommandName: 'get_view_templates',
        Parameters: { includeDetails: true },
        RequestId: 'view_templates_003'
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());

    if (response.Success && response.Data) {
        const result = response.Data;
        const projectName = result.ProjectName || 'Unknown Project';
        const templates = result.ViewTemplates || [];
        const gepTemplates = templates.filter(t => t.Name && t.Name.startsWith('GEP'));

        const now = new Date();
        const dateStr = now.toISOString().split('T')[0];

        // Header
        let md = `# 視圖樣版完整報告\n\n`;
        md += `**專案名稱**: ${projectName}\n\n`;
        md += `**匯出日期**: ${dateStr}\n\n`;
        md += `**視圖樣版總數**: ${templates.length} 個（其中 GEP 開頭: ${gepTemplates.length} 個）\n\n`;
        md += `---\n\n`;

        // ========================================
        // SECTION 1: GEP templates - DIFFERENCES ONLY (最重要)
        // ========================================
        md += `# 第一區塊：GEP 視圖樣版差異分析\n\n`;
        md += `> 此區塊僅顯示 GEP 視圖樣版之間**有差異的設定**，幫助聚焦關鍵差別。\n\n`;

        if (gepTemplates.length > 0) {
            // Find common values for each property
            const properties = [
                { key: 'DetailLevel', label: '詳細等級' },
                { key: 'DisplayStyle', label: '視覺樣式' },
                { key: 'Scale', label: '比例尺' },
                { key: 'CropBoxActive', label: '裁剪區域啟用', format: v => v ? '是' : '否' },
                { key: 'CropBoxVisible', label: '裁剪區域可見', format: v => v ? '是' : '否' },
                { key: 'SupportsUnderlay', label: '支援底層', format: v => v ? '是' : '否' },
                { key: 'FilterCount', label: '篩選器數量' },
                { key: 'HiddenCategoryCount', label: '隱藏類別數' },
            ];

            // Calculate common values
            const commonValues = {};
            properties.forEach(prop => {
                const values = gepTemplates.map(t => {
                    let val = t[prop.key];
                    if (prop.format) val = prop.format(val);
                    return val;
                });
                const uniqueValues = [...new Set(values)];
                if (uniqueValues.length === 1) {
                    commonValues[prop.key] = uniqueValues[0];
                }
            });

            // Show common settings
            md += `## 共同設定 (所有 GEP 樣版皆相同)\n\n`;
            md += `| 設定項目 | 共同值 |\n`;
            md += `|----------|--------|\n`;

            let hasCommon = false;
            properties.forEach(prop => {
                if (commonValues[prop.key] !== undefined) {
                    md += `| ${prop.label} | ${commonValues[prop.key]} |\n`;
                    hasCommon = true;
                }
            });

            if (!hasCommon) {
                md += `| (無共同設定) | - |\n`;
            }

            md += `\n`;

            // Show differences only
            md += `## 差異設定 (僅列出不同的值)\n\n`;

            // Find properties with differences
            const diffProperties = properties.filter(prop => commonValues[prop.key] === undefined);

            if (diffProperties.length > 0) {
                // Build difference table
                let header = `| 樣版名稱 |`;
                let separator = `|----------|`;
                diffProperties.forEach(prop => {
                    header += ` ${prop.label} |`;
                    separator += `----------|`;
                });
                md += header + `\n`;
                md += separator + `\n`;

                gepTemplates.forEach(t => {
                    let row = `| ${t.Name} |`;
                    diffProperties.forEach(prop => {
                        let val = t[prop.key];
                        if (prop.format) val = prop.format(val);
                        row += ` ${val || 'N/A'} |`;
                    });
                    md += row + `\n`;
                });

                md += `\n`;
            }

            // Filters comparison
            md += `## 篩選器差異\n\n`;
            md += `| 樣版名稱 | 篩選器 |\n`;
            md += `|----------|--------|\n`;

            gepTemplates.forEach(t => {
                const filters = t.Filters && t.Filters.length > 0 ? t.Filters.join(', ') : '(無)';
                md += `| ${t.Name} | ${filters} |\n`;
            });

            md += `\n`;

            // Hidden categories comparison - only show unique ones
            md += `## 隱藏類別差異\n\n`;
            md += `> 僅列出各樣版特有的隱藏類別（排除所有樣版都隱藏的類別）\n\n`;

            // Find categories hidden in ALL templates
            const allHiddenArrays = gepTemplates.map(t => t.HiddenCategories || []);
            const commonHidden = allHiddenArrays.length > 0
                ? allHiddenArrays.reduce((a, b) => a.filter(c => b.includes(c)))
                : [];

            if (commonHidden.length > 0) {
                md += `### 所有 GEP 樣版皆隱藏的類別 (${commonHidden.length} 個)\n\n`;
                md += `${commonHidden.join(', ')}\n\n`;
            }

            md += `### 各樣版特有的隱藏類別\n\n`;
            md += `| 樣版名稱 | 特有隱藏類別 |\n`;
            md += `|----------|---------------|\n`;

            gepTemplates.forEach(t => {
                const hidden = t.HiddenCategories || [];
                const unique = hidden.filter(c => !commonHidden.includes(c));
                md += `| ${t.Name} | ${unique.length > 0 ? unique.join(', ') : '(無特有隱藏)'} |\n`;
            });

            md += `\n`;

            // Group GEP templates by naming pattern
            md += `## GEP 樣版命名分類\n\n`;

            const gepGroups = {
                'GEP_Drawing': gepTemplates.filter(t => t.Name.startsWith('GEP_Drawing')),
                'GEP_Modeling': gepTemplates.filter(t => t.Name.startsWith('GEP_Modeling')),
                'GEP_Review': gepTemplates.filter(t => t.Name.startsWith('GEP_Review')),
                'GEP-': gepTemplates.filter(t => t.Name.startsWith('GEP-')),
            };

            for (const [prefix, items] of Object.entries(gepGroups)) {
                if (items.length > 0) {
                    md += `### ${prefix} 系列 (${items.length} 個)\n\n`;
                    items.forEach(t => {
                        md += `- **${t.Name}**: ${t.ViewType}, ${t.DetailLevel}, ${t.DisplayStyle}, ${t.Scale}\n`;
                    });
                    md += `\n`;
                }
            }
        }

        // ========================================
        // SECTION 2: GEP templates overview table
        // ========================================
        md += `---\n\n`;
        md += `# 第二區塊：GEP 開頭的視圖樣版總覽\n\n`;
        md += `> 此區塊列出所有名稱以 "GEP" 開頭的視圖樣版總覽表。\n\n`;

        md += `**GEP 視圖樣版數量**: ${gepTemplates.length}\n\n`;

        // Quick summary table
        md += `| 名稱 | 視圖類型 | 詳細等級 | 視覺樣式 | 比例尺 | 篩選器 | 隱藏類別數 |\n`;
        md += `|------|----------|----------|----------|--------|--------|------------|\n`;

        gepTemplates.forEach(t => {
            md += `| ${t.Name} | ${t.ViewType} | ${t.DetailLevel || 'N/A'} | ${t.DisplayStyle || 'N/A'} | ${t.Scale || 'N/A'} | ${t.FilterCount || 0} | ${t.HiddenCategoryCount || 0} |\n`;
        });

        md += `\n`;

        // ========================================
        // SECTION 3: GEP + Chinese templates with full details
        // ========================================
        md += `---\n\n`;
        md += `# 第三區塊：GEP 與中文視圖樣版完整資訊\n\n`;

        // Filter: GEP templates + Chinese named templates
        const hasChinese = (str) => /[\u4e00-\u9fff]/.test(str);
        const filteredTemplates = templates.filter(t =>
            t.Name && (t.Name.startsWith('GEP') || hasChinese(t.Name))
        );

        md += `> 此區塊列出 GEP 開頭及中文名稱的視圖樣版（共 ${filteredTemplates.length} 個）。\n\n`;

        // Group by ViewType
        const grouped = {};
        filteredTemplates.forEach(t => {
            const type = t.ViewType || 'Other';
            if (!grouped[type]) grouped[type] = [];
            grouped[type].push(t);
        });

        for (const [viewType, items] of Object.entries(grouped)) {
            md += `## ${viewType} (${items.length} 個)\n\n`;

            items.forEach(t => {
                md += `### ${t.Name}\n\n`;
                md += `| 設定項目 | 值 |\n`;
                md += `|----------|----|\n`;
                md += `| Element ID | ${t.ElementId} |\n`;
                md += `| 視圖類型 | ${t.ViewType} |\n`;
                md += `| 詳細等級 | ${t.DetailLevel || 'N/A'} |\n`;
                md += `| 視覺樣式 | ${t.DisplayStyle || 'N/A'} |\n`;
                md += `| 比例尺 | ${t.Scale || 'N/A'} |\n`;
                md += `| 控制參數數 | ${t.ControlledParameterCount || 0} / ${t.TotalParameterCount || 0} |\n`;
                md += `| 裁剪區域啟用 | ${t.CropBoxActive ? '是' : '否'} |\n`;
                md += `| 裁剪區域可見 | ${t.CropBoxVisible ? '是' : '否'} |\n`;
                md += `| 支援底層 | ${t.SupportsUnderlay ? '是' : '否'} |\n`;
                md += `| 篩選器數量 | ${t.FilterCount || 0} |\n`;

                if (t.Filters && t.Filters.length > 0) {
                    md += `| 篩選器 | ${t.Filters.join(', ')} |\n`;
                }

                md += `| 隱藏類別數 | ${t.HiddenCategoryCount || 0} |\n`;

                if (t.HiddenCategories && t.HiddenCategories.length > 0) {
                    md += `| 隱藏類別 | ${t.HiddenCategories.join(', ')} |\n`;
                }

                md += `\n`;
            });
        }

        // Save file
        const safeProjectName = projectName.replace(/[<>:"/\\|?*]/g, '_');
        const filename = `ViewTemplates_${safeProjectName}_${dateStr}_structured.md`;
        const outputPath = `C:/Project/REVIT_MCP_study/docs/${filename}`;

        fs.writeFileSync(outputPath, md, 'utf8');

        console.log(`\n✅ 結構化報告已產生！`);
        console.log(`📄 檔案: ${outputPath}`);
        console.log(`📊 共 ${templates.length} 個視圖樣版，其中 ${gepTemplates.length} 個為 GEP 開頭`);
    } else {
        console.error('❌ 錯誤:', response.Error || '未知錯誤');
    }

    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('❌ 連線錯誤:', err.message);
    process.exit(1);
});

setTimeout(() => {
    console.error('⌛ 連線逾時');
    process.exit(1);
}, 15000);
