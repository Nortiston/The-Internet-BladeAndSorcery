const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { FiltersEngine, Request } = require('@ghostery/adblocker');
const blocker = FiltersEngine.deserialize(fs.readFileSync(path.join(__dirname, 'filters.bin')));
function matches(url) {
  const result = blocker.match(Request.fromRawDetails({ url, sourceUrl: 'https://www.youtube.com/watch?v=test', type: 'xmlhttprequest' }));
  return result.match || !!result.redirect;
}
test('bundled filters block ad/analytics endpoints while preserving video delivery', () => {
  for (const url of ['https://googleads.g.doubleclick.net/pagead/ads?x=1', 'https://www.google-analytics.com/analytics.js', 'https://www.youtube.com/api/stats/ads?ver=2']) assert.ok(matches(url), url);
  for (const url of ['https://www.youtube.com/youtubei/v1/player', 'https://rr1---sn.googlevideo.com/videoplayback?id=test']) assert.equal(matches(url), false, url);
});
test('YouTube cosmetic filters and scriptlets are bundled', () => {
  const cosmetics = blocker.getCosmeticsFilters({ domain: 'youtube.com', hostname: 'www.youtube.com', url: 'https://www.youtube.com/watch?v=test', getBaseRules: true, getInjectionRules: true, getRulesFromHostname: true });
  assert.ok(cosmetics.styles.length > 0);
  assert.ok(cosmetics.scripts.length > 0);
});
