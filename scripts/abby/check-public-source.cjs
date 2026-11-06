const fs=require('node:fs'),cp=require('node:child_process'),crypto=require('node:crypto');
const imported=JSON.parse(fs.readFileSync('scripts/abby/public-import-exceptions.json','utf8')).Files;
const paths=cp.execFileSync('git',['ls-files','-z'],{encoding:'utf8'}).split('\0').filter(Boolean),problems=[];
const forbidden=p=>/^(?:\.codex|\.agents|log|output|tmp|vault|\.obsidian|node_modules)(?:\/|$)/.test(p)||/^(?:\.mcp\.json|\.claude\/settings(?:\.local)?\.json|\.vscode\/(?:settings|mcp)\.json)$/.test(p)||/(?:^|\/)(?:bin|obj|node_modules)\//.test(p)||/\.(?:rvt|rfa|rte|rft|dwg|dxf|ifc|nwd|nwc|pdf|dll|pdb|exe|token|pat)$/i.test(p)||/(?:^|\/)\.env(?:\.|$)/.test(p)&&!p.endsWith('.env.example');
function inspect(p,read,context=''){
 if(forbidden(p)){problems.push(context+p+': excluded file type/location');return;}
 if(!/\.(?:cs|ts|mjs|cjs|ps1|cmd|md|json|ya?ml|py|html|css|toml|xml|txt|csv)$/i.test(p))return;
 const s=read().toString('utf8');
 if(/(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-[A-Za-z0-9_-]{32,})/.test(s))problems.push(context+p+': credential');
 const unchanged=imported[p]===crypto.createHash('sha256').update(s.replace(/\r\n/g,'\n')).digest('hex');
 if(!unchanged)for(const m of s.matchAll(/[A-Z]:[\\/]+Users[\\/]+([^\\/\s"'<>]+)/gi))if(!/^(?:Public|Default|USERNAME|YOUR_USER|YourName|xxx|user)$/i.test(m[1]))problems.push(context+p+': hard-coded user profile');
}
for(const p of paths)inspect(p,()=>fs.readFileSync(p));
const at=process.argv.indexOf('--range');let commits=[];
if(at>=0){
 const range=process.argv[at+1];if(!range||range.startsWith('-'))throw Error('A Git revision range is required');
 const git=args=>cp.execFileSync('git',args,{maxBuffer:50e6});
 commits=git(['rev-list','--max-count=201',range,'--']).toString('utf8').trim().split('\n').filter(Boolean);
 if(commits.length>200)throw Error('More than 200 new commits require separate publication review');
 for(const commit of commits){
  const changed=new Set(git(['diff-tree','--root','-m','-r','--no-commit-id','--name-only','-z','--diff-filter=ACM',commit]).toString('utf8').split('\0').filter(Boolean));
  for(const p of changed)inspect(p,()=>git(['show',commit+':'+p]),commit.slice(0,12)+': ');
 }
}
if(problems.length){console.error(problems.join('\n'));process.exit(1);}console.log('PASS public source: '+paths.length+' tracked paths; no excluded models/local settings/private markers.');
module.exports={forbidden};
