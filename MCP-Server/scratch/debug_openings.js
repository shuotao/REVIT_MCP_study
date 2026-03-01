import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    ws.send(JSON.stringify({
        CommandName: 'check_exterior_wall_openings',
        Parameters: {
            colorizeViolations: true,
            checkArticle45: true,
            checkArticle110: true
        },
        RequestId: 'dbg_' + Date.now()
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (res.Success && res.Data) {
        const lines = [];
        lines.push('Summary: ' + JSON.stringify(res.Data.summary));
        lines.push('---');
        res.Data.details.forEach(d => {
            const a45s = d.article45 ? d.article45.OverallStatus : 'null';
            const a110s = d.article110 ? d.article110.OverallStatus : 'null';
            const distB = d.article45?.DistanceToBoundary?.toFixed(2) ?? 'N/A';
            const distBldg = d.article45?.DistanceToBuilding?.toFixed(2) ?? 'N/A';
            lines.push(`ID:${d.openingId} type:${d.openingType} a45:${a45s} a110:${a110s} distB:${distB}m distBldg:${distBldg}m`);
        });
        const output = lines.join('\n');
        fs.writeFileSync('scratch/debug_output.txt', output, 'utf8');
        console.log('Output written to scratch/debug_output.txt');
    }
    ws.close();
});

ws.on('error', (err) => {
    console.error('FAIL:', err.message);
    process.exit(1);
});

setTimeout(() => { ws.close(); process.exit(1); }, 60000);
