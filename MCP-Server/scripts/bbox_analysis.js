import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', async () => {
  console.log('Connected');
  
  const send = (cmd, params = {}) => new Promise(res => {
    const id = 'req-' + Date.now();
    ws.once('message', data => res(JSON.parse(data.toString())));
    ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
  });

  const sleeveId = 14314654;

  // Get BoundingBox for sleeve center
  const sleeveBBox = await send('query_elements', { 
    category: '管附件',
    filters: [{ field: 'ID', operator: 'equals', value: sleeveId.toString() }],
    returnFields: ['BoundingBox']
  });

  fs.writeFileSync('bbox_analysis.json', JSON.stringify(sleeveBBox, null, 2));
  ws.close();
  process.exit(0);
});
