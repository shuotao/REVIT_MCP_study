import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
ws.on('open', () => {
    ws.once('message', data => {
        const resp = JSON.parse(data.toString());
        if (resp.Success && resp.Data.Success) {
            const results = resp.Data.Results;
            let md = "# 穿梁套管檢核報告 (AI 動態檢核版)\n\n";
            md += `### 檢核參數: 品類=管附件, 名稱=圓形開口, 柱邊距>=1.0H, 孔徑<=1/3H\n`;
            md += `### 檢核摘要: ${resp.Data.Summary}\n\n`;
            md += "| 套管 ID | 樓層 | 穿過的梁 ID | 梁名稱 | 狀態 | 違規原因 |\n";
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
    
    // 傳送動態參數
    ws.send(JSON.stringify({
        CommandName: 'advanced_analyze', 
        Parameters: { 
            Keyword: '圓形開口',
            MaxDiameterRatio: 0.3333,
            MinDistanceRatio: 1.0
        }, 
        RequestId: 'adv-' + Date.now()
    }));
});
setTimeout(() => { process.exit(1); }, 60000);
