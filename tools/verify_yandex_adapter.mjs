import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../Services/YandexAdapter.js', import.meta.url), 'utf8');
let requests = 0;
const plain = { toJSON: () => ({}), $treenode: { root: { environment: {} }, type: { create: () => ({
  async getLyrics(id) { this.currentTrackId = id; this.loadingState = 'RESOLVE'; this.lyrics = 'fixture text'; }
}) } } };
const readyModel = () => ({
  lines: [], currentTrackId: null, loadingState: 'IDLE', toJSON: () => ({}), getData() {},
  $treenode: { root: { environment: {} }, type: { create: () => ({
    async getData(id) { requests++; this.currentTrackId = id; this.loadingState = 'RESOLVE'; this.lines = [{ fromSec: 1, toSec: 3, text: 'fixture timed line' }]; }
  }) } }
});
const meta = { id: '100', title: 'fixture', artists: [{ name: 'fixture artist' }], hasLyrics: true, hasSyncLyrics: false };
const context = { sonataState: { entityMeta: meta }, trackLyrics: plain, fullscreenPlayer: { syncLyrics: {} } };
const element = { __reactFiberTest: { dependencies: { firstContext: { memoizedValue: context } } } };
const window = {};
assert.equal(vm.runInNewContext(source, { window, document: { querySelectorAll: () => [element] } }), true);
const first = await window.__islandYandex.read();
assert.equal(first.lines.length, 0);
assert.equal(first.plain, 'fixture text');
context.fullscreenPlayer.syncLyrics = readyModel();
const upgraded = await window.__islandYandex.read();
assert.equal(upgraded.lines.length, 1);
assert.equal(upgraded.lines[0].seconds, 1);
assert.equal(requests, 1);
await window.__islandYandex.read();
assert.equal(requests, 1);
meta.id = '200';
context.fullscreenPlayer.syncLyrics.currentTrackId = '200';
context.fullscreenPlayer.syncLyrics.loadingState = 'RESOLVE';
context.fullscreenPlayer.syncLyrics.lines = [{ fromSec: 2, toSec: 4, text: 'visible fixture line' }];
const visible = await window.__islandYandex.read();
assert.equal(visible.lines[0].seconds, 2);
assert.equal(requests, 1);
const forcedPlain = await window.__islandYandex.read(null, true);
assert.equal(forcedPlain.lines.length, 0);
assert.equal(forcedPlain.plain, 'fixture text');
console.log('Yandex adapter: plain cache upgrade, late model, missing flag, timed cache, visible model and plain-only checks passed.');
const calls = [];
context.sonataState.position = 30;
context.sonataState.volume = .41;
const inputs = ['1', '120'].map(max => ({ min: '0', max, value: '0', __reactPropsTest: {
  onMouseDown() { calls.push('down'); },
  onChange(e) { calls.push('change'); context.sonataState[max === '1' ? 'volume' : 'position'] = Number(e.target.value); },
  onMouseUp() { calls.push('up'); }
} }));
const controlsWindow = {};
const ratings = Object.fromEntries(['like', 'dislike'].map(kind => [kind, { pressed: false, disabled: false, clicks: 0,
  closest() { return this; }, getAttribute() { return String(this.pressed); }, click() { this.pressed = !this.pressed; this.clicks++; }
}]));
vm.runInNewContext(source, { window: controlsWindow, document: { querySelectorAll: selector => selector.includes('input') ? inputs : selector.includes('data-test-id=') ? [ratings[selector.includes('DISLIKE_BUTTON') ? 'dislike' : 'like']] : [element] }, MouseEvent: class {}, setTimeout });
const changedVolume = await controlsWindow.__islandYandex.control('volume', .25);
assert.equal(changedVolume.volume, .25);
assert.equal(changedVolume.position, 30);
const changedPosition = await controlsWindow.__islandYandex.control('seek', 80);
assert.equal(changedPosition.position, 80);
assert.equal(changedPosition.volume, .25);
assert.equal((await controlsWindow.__islandYandex.control('seek', 500)).position, 120);
assert.equal((await controlsWindow.__islandYandex.control('unknown', 10)).success, false);
assert.equal(calls.join(','), 'down,change,up,down,change,up,down,change,up');
console.log('Yandex controls: seek, volume, bounds and commit checks passed.');
assert.equal((await controlsWindow.__islandYandex.control('like', 0, 'wrong-track')).success, false);
assert.equal(ratings.like.clicks, 0);
assert.equal((await controlsWindow.__islandYandex.control('like', 0, '200')).liked, true);
assert.equal((await controlsWindow.__islandYandex.control('like', 0, '200')).liked, false);
assert.equal((await controlsWindow.__islandYandex.control('dislike', 0, '200')).disliked, true);
ratings.dislike.disabled = true;
assert.equal((await controlsWindow.__islandYandex.control('dislike', 0, '200')).success, false);
assert.equal(ratings.dislike.clicks, 1);
console.log('Yandex ratings: native click, toggle, stale track and disabled button checks passed (synthetic player).');
const refreshedState = { ...context.sonataState, position: 7, volume: .7 };
element.__reactFiberTest.dependencies.firstContext.memoizedValue = { ...context, sonataState: refreshedState };
assert.equal(controlsWindow.__islandYandex.playback().position, 7);
assert.equal(controlsWindow.__islandYandex.playback().volume, .7);
assert.equal(controlsWindow.__islandYandex.models.state, refreshedState);
element.__reactFiberTest.dependencies.firstContext.memoizedValue = context;
const originalChange = inputs[0].__reactPropsTest.onChange;
delete inputs[0].__reactPropsTest.onChange;
assert.equal((await controlsWindow.__islandYandex.control('volume', .9)).success, false);
inputs[0].__reactPropsTest.onChange = originalChange;
console.log('Yandex recovery: replaced React context refreshes and uncommitted slider changes are rejected.');
let coldRequests = 0;
const schema = { create(snapshot) {
  assert.equal(snapshot.isVisible, false);
  return { async getData(id) { coldRequests++; this.currentTrackId = id; this.loadingState = 'RESOLVE'; this.lines = [{ fromSec: 1, toSec: 4, text: 'cold fixture' }]; } };
} };
const coldMeta = { id: '300', title: 'Cold fixture', artists: [{ name: 'Synthetic' }], durationMs: 120000 };
const nextMeta = { ...coldMeta, id: '400', title: 'Next fixture' };
const coldPlain = { ...plain, $treenode: { ...plain.$treenode, root: { environment: {}, storedValue: { fullscreenPlayer: { $treenode: { type: { properties: { syncLyrics: schema } } } } } } } };
const coldContext = { sonataState: { entityMeta: coldMeta }, trackLyrics: coldPlain, fullscreenPlayer: { syncLyrics: {} } };
const engine = { playbackController: { activePlayback: { value: { playbackState: { queueState: { nextEntity: { value: { entity: { data: { meta: nextMeta } } } } } } } } } };
const coldElement = { __reactFiberTest: { dependencies: { firstContext: { memoizedValue: coldContext, next: { memoizedValue: engine } } } } };
const coldWindow = {};
vm.runInNewContext(source, { window: coldWindow, document: { querySelectorAll: () => [coldElement] } });
const coldAdapter = coldWindow.__islandYandex;
const cold = await Promise.all([coldAdapter.read(), coldAdapter.read()]);
assert.equal(cold[0].lines.length, 1);
assert.equal(coldRequests, 1);
assert.equal(Object.keys(coldContext.fullscreenPlayer.syncLyrics).length, 0);
assert.equal(coldAdapter.nextTrack().id, '400');
coldAdapter.warmNext(); await coldAdapter.warmJob;
assert.equal(coldRequests, 2);
assert.equal(coldAdapter.takePrefetch('fixture').result.lines.length, 1);
assert.equal(coldAdapter.takePrefetch('fixture'), null);
coldContext.sonataState.entityMeta = nextMeta;
assert.equal((await coldAdapter.read()).id, '400');
assert.equal(coldRequests, 2);
console.log('Yandex cold start: no lyrics screen required, concurrent requests shared, next queue track prefetched and reused on transition.');
