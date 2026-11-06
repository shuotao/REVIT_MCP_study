import {writeFileSync} from 'node:fs';
import {registerRevitTools} from '../build/tools/index.js';
process.env.MCP_PROFILE='full';
const descriptions=Object.fromEntries(registerRevitTools().map(t=>[t.name,typeof t.description==='string'?t.description:null]));
if(!process.argv[2])throw new Error('Output JSON file required');
writeFileSync(process.argv[2],JSON.stringify(descriptions),'utf8');
