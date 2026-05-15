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
  const beamId = 12703392;
  const linkId = 13671635;

  // Get detailed info for both
  const sleeveInfo = await send('get_element_info', { elementId: sleeveId });
  const beamInfo = await send('get_element_info', { elementId: beamId, linkInstanceId: linkId });
  
  // Get geometry for calculation
  const sleeveGeom = await send('get_element_geometry', { elementId: sleeveId });
  const beamGeom = await send('get_element_geometry', { elementId: beamId, linkInstanceId: linkId });

  const detailReport = { sleeveInfo, beamInfo, sleeveGeom, beamGeom };
  fs.writeFileSync('detail_analysis.json', JSON.stringify(detailReport, null, 2));
  ws.close();
  process.exit(0);
});
