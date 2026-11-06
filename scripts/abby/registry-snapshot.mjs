import fs from 'node:fs';import {registerRevitTools} from '../../MCP-Server/build/tools/index.js';
process.env.MCP_PROFILE='full';const tools=registerRevitTools();const names=tools.map(t=>t.name);if(new Set(names).size!==names.length)throw new Error('Duplicate tool names');
fs.writeFileSync(process.argv[2],JSON.stringify({ToolCount:names.length,ToolNames:names},null,2));
