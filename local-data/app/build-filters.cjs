// Build-time only: download public filter data, not browsing data, then bundle it.
const fs = require('node:fs');
const path = require('node:path');
const { FiltersEngine } = require('@ghostery/adblocker');
(async () => {
  const sourceDir = path.join(__dirname, 'filter-sources');
  fs.mkdirSync(sourceDir, { recursive: true });
  const sources = new Map();
  const fetchList = async url => {
    const response = await fetch(url, { signal: AbortSignal.timeout(30000) });
    if (!response.ok) throw new Error('Filter download failed: ' + response.status + ' ' + url);
    const filename = new URL(url).pathname.split('/').pop();
    fs.writeFileSync(path.join(sourceDir, filename), await response.clone().text());
    sources.set(filename, url);
    return response;
  };
  const blocker = await FiltersEngine.fromPrebuiltAdsAndTracking(fetchList);
  fs.writeFileSync(path.join(__dirname, 'filters.bin'), blocker.serialize());
  fs.writeFileSync(path.join(sourceDir, 'sources.json'), JSON.stringify(Object.fromEntries(sources), null, 2));
  console.log('Bundled ads/tracking filters and scriptlets:', blocker.getFilters().networkFilters.length, 'network rules');
})().catch(e => { console.error(e); process.exitCode = 1; });
