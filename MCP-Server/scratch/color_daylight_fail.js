import WebSocket from 'ws';

// 不合格的房間 IDs (from report)
const failRoomIds = [258443, 258446, 258449, 258452, 258636];

const ws = new WebSocket('ws://localhost:8964');
let idx = 0;

function colorNext() {
    if (idx >= failRoomIds.length) {
        console.log('✅ All rooms colored. Done.');
        ws.close();
        return;
    }
    const id = failRoomIds[idx];
    console.log(`Coloring room ${id} (${idx + 1}/${failRoomIds.length})...`);
    ws.send(JSON.stringify({
        CommandName: 'override_element_graphics',
        Parameters: {
            elementId: id,
            surfaceFillColor: { r: 255, g: 0, b: 0 },
            transparency: 50,
            patternMode: "surface"
        },
        RequestId: 'color_room_' + id
    }));
}

ws.on('open', () => {
    console.log('Connected');
    colorNext();
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (res.Success) {
        console.log(`  ✓ Room ${failRoomIds[idx]} colored`);
    } else {
        console.log(`  ✗ Room ${failRoomIds[idx]} failed: ${res.Error}`);
    }
    idx++;
    colorNext();
});

ws.on('error', (e) => { console.error('ERR:', e.message); process.exit(1); });
setTimeout(() => { ws.close(); process.exit(0); }, 30000);
