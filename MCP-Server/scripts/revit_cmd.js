import WebSocket from 'ws';
import fs from 'fs';

const commandName = process.argv[2] || 'get_selected_elements';
const params = process.argv[3] ? JSON.parse(process.argv[3]) : {};
const outputFile = process.argv[4] || 'result.json';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', function open() {
  const command = {
    CommandName: commandName,
    Parameters: params,
    RequestId: 'req-' + Date.now()
  };
  ws.send(JSON.stringify(command));
});

ws.on('message', function message(data) {
  const response = data.toString();
  console.log('Received response');
  fs.writeFileSync(outputFile, response, 'utf8');
  ws.close();
  process.exit(0);
});

ws.on('error', function error(err) {
  console.error('Error:', err.message);
  process.exit(1);
});
