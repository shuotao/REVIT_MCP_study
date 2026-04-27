import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Connected to Revit...');
    const command = {
        CommandName: 'get_view_templates',
        Parameters: { includeDetails: true },
        RequestId: 'view_templates_001'
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());

    if (response.Success && response.Data) {
        const result = response.Data;
        const projectName = result.ProjectName || 'Unknown Project';
        const templates = result.ViewTemplates || [];
        const count = result.Count || templates.length;

        // Generate date
        const now = new Date();
        const dateStr = now.toISOString().split('T')[0]; // YYYY-MM-DD
        const timeStr = now.toTimeString().split(' ')[0].replace(/:/g, ''); // HHMMSS

        // Build markdown content
        let md = `# 視圖樣版清單\n\n`;
        md += `**專案名稱**: ${projectName}\n\n`;
        md += `**匯出日期**: ${dateStr}\n\n`;
        md += `**視圖樣版總數**: ${count}\n\n`;
        md += `---\n\n`;

        // Group by ViewType
        const grouped = {};
        templates.forEach(t => {
            const type = t.ViewType || 'Other';
            if (!grouped[type]) grouped[type] = [];
            grouped[type].push(t);
        });

        // Generate table for each group
        for (const [viewType, items] of Object.entries(grouped)) {
            md += `## ${viewType} 視圖樣版 (${items.length} 個)\n\n`;
            md += `| 名稱 | 詳細等級 | 視覺樣式 | 比例尺 | 篩選器 | 隱藏類別數 |\n`;
            md += `|------|----------|----------|--------|--------|------------|\n`;

            items.forEach(t => {
                const name = t.Name || 'N/A';
                const detail = t.DetailLevel || 'N/A';
                const display = t.DisplayStyle || 'N/A';
                const scale = t.Scale || 'N/A';
                const filterCount = t.FilterCount ?? 0;
                const hiddenCount = t.HiddenCategoryCount ?? 0;

                md += `| ${name} | ${detail} | ${display} | ${scale} | ${filterCount} | ${hiddenCount} |\n`;
            });

            md += `\n`;

            // Add details for each template
            md += `<details>\n<summary>詳細資訊</summary>\n\n`;
            items.forEach(t => {
                md += `### ${t.Name}\n\n`;
                md += `- **Element ID**: ${t.ElementId}\n`;
                md += `- **詳細等級**: ${t.DetailLevel || 'N/A'}\n`;
                md += `- **視覺樣式**: ${t.DisplayStyle || 'N/A'}\n`;
                md += `- **比例尺**: ${t.Scale || 'N/A'}\n`;
                md += `- **控制參數數**: ${t.ControlledParameterCount || 0} / ${t.TotalParameterCount || 0}\n`;
                md += `- **裁剪區域**: ${t.CropBoxActive ? '啟用' : '停用'}${t.CropBoxVisible ? ' (可見)' : ''}\n`;
                md += `- **支援底層**: ${t.SupportsUnderlay ? '是' : '否'}\n`;

                if (t.Filters && t.Filters.length > 0) {
                    md += `- **篩選器**: ${t.Filters.join(', ')}\n`;
                }

                if (t.HiddenCategories && t.HiddenCategories.length > 0) {
                    md += `- **隱藏類別** (前10個): ${t.HiddenCategories.join(', ')}\n`;
                }

                md += `\n`;
            });
            md += `</details>\n\n`;
        }

        // Save to file
        const safeProjectName = projectName.replace(/[<>:"/\\|?*]/g, '_');
        const filename = `ViewTemplates_${safeProjectName}_${dateStr}.md`;
        const outputPath = `C:/Project/REVIT_MCP_study/docs/${filename}`;

        fs.writeFileSync(outputPath, md, 'utf8');

        console.log(`\n✅ 報告已產生！`);
        console.log(`📄 檔案: ${outputPath}`);
        console.log(`📊 共 ${count} 個視圖樣版`);

        // Also output JSON for reference
        console.log('\n完整資料:');
        console.log(JSON.stringify(result, null, 2));
    } else {
        console.error('❌ 錯誤:', response.Error || '未知錯誤');
    }

    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('❌ 連線錯誤:', err.message);
    console.error('請確認 Revit 已開啟並啟動 MCP 服務');
    process.exit(1);
});

setTimeout(() => {
    console.error('⌛ 連線逾時');
    process.exit(1);
}, 10000);
