import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        const resp = JSON.parse(data.toString());
        if (resp.Success && resp.Data.Success) {
            const results = resp.Data.Results;
            let md = "# 穿梁套管檢核報告\n\n";
            md += `### 檢核摘要: ${resp.Data.Summary}\n\n`;
            md += "| 套管 ID | 套管樓層 | 穿過的梁 ID | 梁名稱 | 狀態 | 違規原因 |\n";
            md += "| :--- | :--- | :--- | :--- | :--- | :--- |\n";
            
            results.forEach(r => {
                const statusEmoji = r.Status === 'PASS' ? '✅' : '❌';
                md += `| ${r.SleeveId} | ${r.SleeveLevel} | ${r.BeamId} | ${r.BeamName} | ${statusEmoji} ${r.Status} | ${r.Reason || '-'} |\n`;
            });

            console.log('--- REPORT START ---');
            console.log(md);
            console.log('--- REPORT END ---');
        } else {
            console.log('Error:', resp.Error || resp.Data.Error);
        }
        ws.close();
        process.exit(0);
    });
    ws.send(JSON.stringify({CommandName: 'advanced_analyze', Parameters: {}, RequestId: 'rep-' + Date.now()}));
});
setTimeout(() => { process.exit(1); }, 60000);
