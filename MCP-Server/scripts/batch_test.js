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

  const view = await send('get_active_view');
  const selection = await send('get_selected_elements');
  const scan = await send('scan_penetrated_beams_in_view');

  const fullReport = { view, selection, scan };
  fs.writeFileSync('test_report.json', JSON.stringify(fullReport, null, 2));
  console.log('Report saved to test_report.json');
  ws.close();
  process.exit(0);
});
