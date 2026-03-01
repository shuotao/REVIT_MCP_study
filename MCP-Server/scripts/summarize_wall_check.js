import fs from 'fs';

try {
    const data = fs.readFileSync('exterior_wall_check_report.json', 'utf8');
    const report = JSON.parse(data);

    console.log('### 外牆開口檢討報告');
    console.log(`- **檢查總數**: ${report.summary?.totalOpenings} 個開口`);
    console.log(`- **違規 (Fail)**: ${report.summary?.violations}`);
    console.log(`- **警告 (Warning)**: ${report.summary?.warnings}`);
    console.log(`- **符合 (Pass)**: ${report.summary?.passed}`);
    console.log('\n--- 詳細列表 ---');

    if (report.details && report.details.length > 0) {
        let count = 0;
        report.details.forEach((detail) => {
            let hasIssue = false;
            let msg = [`**[ID: ${detail.openingId}] ${detail.openingType}**`];

            // Status: 0=Pass, 1=Warning, 2=Fail (inferred)
            const s45 = detail.article45?.OverallStatus;
            const s110 = detail.article110?.OverallStatus;

            if (s45 === 2) {
                msg.push(`- ❌ [第45條] ${detail.article45.BoundaryMessage} (距離: ${detail.article45.DistanceToBoundary?.toFixed(2)}m)`);
                hasIssue = true;
            } else if (s45 === 1) {
                msg.push(`- ⚠️ [第45條] ${detail.article45.BoundaryMessage}`);
                hasIssue = true;
            }

            if (s110 === 2) {
                msg.push(`- ❌ [第110條] ${detail.article110.BoundaryFireMessage}`);
                hasIssue = true;
            } else if (s110 === 1) {
                // Determine if this is a "check" warning or a "violation" warning
                // For Article 110, usually it says "Requires X hr fire rating". 
                // If the element doesn't have it, it's a violation. 
                // But the checker might just be flagging "Needs check" as warning if it can't verify the parameter.
                msg.push(`- ⚠️ [第110條] ${detail.article110.BoundaryFireMessage}`);
                hasIssue = true;
            }

            if (hasIssue) {
                console.log(msg.join('\n'));
                console.log(''); // Empty line
                count++;
            }
        });

        if (count === 0) {
            console.log('未發現違規或警告項目。');
        }
    } else {
        console.log('無詳細資料。');
    }

} catch (err) {
    console.error('Error reading/parsing report:', err);
}
