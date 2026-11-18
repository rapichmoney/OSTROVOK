import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const script = fs.readFileSync(new URL('../Services/YouTubeControls.js', import.meta.url), 'utf8');
const id = 'abcdefghijk';
function fixture({paused = false, url = `https://www.youtube.com/watch?v=${id}`, missing = false, reject = false} = {}) {
  const video = {paused, ended: false, currentTime: 12, duration: 100, plays: 0, pauses: 0,
    async play() { this.plays++; if (reject) throw Error('NotAllowedError'); this.paused = false; },
    pause() { this.pauses++; this.paused = true; }};
  const toggle = vm.runInNewContext(script, {URL, location: {href: url}, document: {querySelector: () => missing ? null : video}});
  return {video, toggle};
}
let f = fixture();
assert.equal((await f.toggle(id, false)).playing, false); assert.equal(f.video.pauses, 1);
assert.equal((await f.toggle(id, true)).playing, true); assert.equal(f.video.plays, 1);
assert.equal((await f.toggle(id, true)).playing, true); assert.equal(f.video.pauses, 1);
f = fixture({url: 'https://www.youtube.com/watch?v=other123456'});
assert.equal((await f.toggle(id)).ok, false); assert.equal(f.video.pauses + f.video.plays, 0);
f = fixture({url: `https://example.com/watch?v=${id}`});
assert.equal((await f.toggle(id)).ok, false); assert.equal(f.video.pauses, 0);
f = fixture({missing: true}); assert.equal((await f.toggle(id)).ok, false);
f = fixture({paused: true, reject: true}); assert.equal((await f.toggle(id, true)).ok, false);
f = fixture({paused: true, url: `https://www.youtube.com/shorts/${id}`}); assert.equal((await f.toggle(id, true)).playing, true);
console.log('YouTube controls: pause, resume, video identity, origin, missing player, rejected playback and Shorts passed.');
