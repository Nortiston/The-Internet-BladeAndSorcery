'use strict';
// The pinned lockfile restores dependencies. This selects only their production closure.
const fs=require('node:fs'),path=require('node:path');
const seen=new Set();
function visit(name){
  if(seen.has(name))return;
  seen.add(name);
  const pkg=JSON.parse(fs.readFileSync(path.join(__dirname,'node_modules',name,'package.json'),'utf8'));
  for(const dependency of Object.keys(pkg.dependencies||{}))visit(dependency);
  if(Object.keys(pkg.optionalDependencies||{}).length)throw Error('Review new optional dependencies before packaging '+name);
}
for(const name of Object.keys(require('./package.json').dependencies))visit(name);
process.stdout.write(JSON.stringify([...seen].sort()));
