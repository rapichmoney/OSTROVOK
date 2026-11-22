(() => {
  if (window.__islandYandex?.version === 9 && window.__islandYandex.refresh()) return true;
  function findModels() {
    const candidates = [...document.querySelectorAll('input[type="range"],button[data-test-id="LIKE_BUTTON"],button[data-test-id="PLAY_BUTTON"]'), ...document.querySelectorAll('button,main,section,[data-test-id]')].slice(0, 400);
    const seen = new Set();
    for (const element of candidates) {
      let fiber = element[Object.keys(element).find(k => k.startsWith('__reactFiber'))];
      for (let depth = 0; fiber && depth < 45 && !seen.has(fiber); depth++, fiber = fiber.return) {
        seen.add(fiber);
        for (let context = fiber.dependencies?.firstContext; context; context = context.next) {
          const value = context.memoizedValue;
          if (value?.sonataState && value?.trackLyrics) return { state: value.sonataState, plain: value.trackLyrics, sync: value.fullscreenPlayer?.syncLyrics };
        }
      }
    }
    return null;
  }
  let models = findModels();
  if (!models) return false;
  const cache = new Map();
  const pending = new Map(), delivered = new Map();
  const metadataOf = meta => meta?.id ? ({ id: String(meta.realId || meta.id).split(':')[0], title: meta.title || '', artist: (meta.artists || []).map(a => a.name).filter(Boolean).join(', '), duration: Number(meta.durationMs || 0) / 1000, hasLyrics: !!meta.hasLyrics, hasSyncLyrics: !!meta.hasSyncLyrics }) : null;
  const adapter = {
    version: 9, get models() { return models; }, syncFactory: null, warmId: '', warmAt: 0, warmJob: null,
    refresh() { const fresh = findModels(); if (!fresh) return false; models = fresh; return true; },
    ratingButton(kind) {
      const buttons = Array.from(document.querySelectorAll('button[data-test-id="' + (kind === 'like' ? 'LIKE_BUTTON' : 'DISLIKE_BUTTON') + '"]'));
      return buttons.find(e => e.closest('[class*="PlayerBar"]')) || (buttons.length === 1 ? buttons[0] : null);
    },
    playback() {
      this.refresh();
      const like = this.ratingButton('like'), dislike = this.ratingButton('dislike');
      const meta = this.metadata();
      return { position: Number(models.state.position || 0), volume: Number(models.state.volume ?? 1),
        title: meta?.title || '', artist: meta?.artist || '', duration: meta?.duration || 0, playing: models.state.status === 'playing' || !!document.querySelector?.('[class*="PlayerBar"] button[data-test-id="PAUSE_BUTTON"]'),
        artworkUrl: typeof models.state.entityMeta?.coverUri === 'string' ? (models.state.entityMeta.coverUri.startsWith('https://') ? '' : 'https://') + models.state.entityMeta.coverUri.replace('%%', '200x200') : '',
        trackId: meta?.id || '', liked: like?.getAttribute('aria-pressed') === 'true', disliked: dislike?.getAttribute('aria-pressed') === 'true',
        canLike: !!like && !like.disabled, canDislike: !!dislike && !dislike.disabled };
    },
    async control(kind, value, expectedTrackId) {
      this.refresh();
      if (['toggle', 'next', 'previous'].includes(kind)) {
        const ids = kind === 'toggle' ? ['PLAY_BUTTON','PAUSE_BUTTON'] : [kind === 'next' ? 'NEXT_TRACK_BUTTON' : 'PREVIOUS_TRACK_BUTTON'];
        const button = ids.flatMap(id => Array.from(document.querySelectorAll('button[data-test-id="' + id + '"]'))).find(e => e.closest('[class*="PlayerBar"]'));
        if (!button || button.disabled) return { success: false };
        button.click(); await new Promise(resolve => setTimeout(resolve, 100)); return { success: true, ...this.playback() };
      }
      if (kind === 'like' || kind === 'dislike') {
        const button = this.ratingButton(kind);
        if (!expectedTrackId || this.metadata()?.id !== expectedTrackId || !button || button.disabled) return { success: false };
        button.click();
        await new Promise(resolve => setTimeout(resolve, 200));
        return { success: true, ...this.playback() };
      }
      if (!['seek', 'volume'].includes(kind) || !Number.isFinite(value)) return { success: false };
      const find = () => Array.from(document.querySelectorAll('input[type="range"]')).find(e => kind === 'volume' ? Number(e.max) === 1 : Number(e.max) > 1);
      const input = find();
      if (!input) return { success: false };
      const props = e => e?.[Object.keys(e).find(k => k.startsWith('__reactProps'))];
      const event = type => ({ target: input, currentTarget: input, nativeEvent: new MouseEvent(type, { button: 0 }), stopPropagation() {}, preventDefault() {} });
      value = Math.max(Number(input.min || 0), Math.min(Number(input.max), value));
      props(input)?.onMouseDown?.(event('mousedown'));
      input.value = String(value);
      props(input)?.onChange?.(event('change'));
      await new Promise(resolve => setTimeout(resolve, 30));
      props(find())?.onMouseUp?.(event('mouseup'));
      await new Promise(resolve => setTimeout(resolve, 30));
      const state = this.playback();
      return { success: Math.abs((kind === 'volume' ? state.volume : state.position) - value) < (kind === 'volume' ? .015 : 1.5), ...state };
    },
    captureSync(model) {
      if (!model?.$treenode || !Array.isArray(model.lines) || typeof model.getData !== 'function') return false;
      const node = model.$treenode;
      this.syncFactory = { type: node.type, environment: node.root.environment,
        snapshot: { ...model.toJSON(), lines: [], currentTrackId: null, loadingState: 'IDLE' } };
      return true;
    },
    metadata() {
      const meta = models.state.entityMeta;
      if (!meta?.id) return null;
      return { id: String(meta.realId || meta.id).split(':')[0], title: meta.title || '',
        artist: (meta.artists || []).map(a => a.name).filter(Boolean).join(', '),
        duration: Number(meta.durationMs || 0) / 1000,
        hasLyrics: !!meta.hasLyrics, hasSyncLyrics: !!meta.hasSyncLyrics };
    },
    prepare() {
      const root = models.plain?.$treenode?.root;
      const type = root?.storedValue?.fullscreenPlayer?.$treenode?.type?.properties?.syncLyrics;
      if (typeof type?.create === 'function') {
        this.syncFactory = { type, environment: root.environment, snapshot: { loadingState: 'IDLE', lines: [], currentTrackId: null, isVisible: false } };
        return true;
      }
      return this.captureSync(findModels()?.sync) || this.captureSync(models.sync) || !!this.syncFactory;
    },
    nextTrack() {
      for (const el of document.querySelectorAll('input[type="range"]')) {
        let fiber = el[Object.keys(el).find(k => k.startsWith('__reactFiber'))];
        for (let depth = 0; fiber && depth < 45; depth++, fiber = fiber.return)
          for (let context = fiber.dependencies?.firstContext; context; context = context.next) {
            const playback = context.memoizedValue?.playbackController?.activePlayback?.value;
            const next = playback?.playbackState?.queueState?.nextEntity;
            const item = next?.value ?? next;
            const meta = metadataOf(item?.entity?.data?.meta);
            if (meta && meta.id !== this.metadata()?.id) return meta;
          }
      }
      return null;
    },
    warmNext() {
      const next = this.nextTrack();
      if (!next || this.warmJob) return;
      const cached = cache.get(next.id + ':auto');
      if (cached?.lines.length || (this.warmId === next.id && Date.now() - this.warmAt < 15000)) return;
      this.warmId = next.id; this.warmAt = Date.now();
      this.warmJob = this.read(next.id).catch(() => null).finally(() => { this.warmJob = null; });
    },
    takePrefetch(client) {
      const track = this.nextTrack(), result = track ? cache.get(track.id + ':auto') : null;
      if (!track || result?.state !== 'ready') return null;
      const signature = track.id + ':' + result.lines.length + ':' + result.plain.length;
      if (delivered.get(client) === signature) return null;
      if (delivered.size >= 8) delivered.delete(delivered.keys().next().value);
      delivered.set(client, signature); return { track, result };
    },
    async read(requestedId, plainOnly = false) {
      const id = String(requestedId || this.metadata()?.id || ''), key = id + (plainOnly ? ':plain' : ':auto');
      if (pending.has(key)) return pending.get(key);
      const job = this.load(requestedId, plainOnly).finally(() => pending.delete(key));
      pending.set(key, job); return job;
    },
    timedLine(line) {
      const text = String(line.text || '');
      let cursor = 0;
      const words = Array.from(line.words || []).flatMap(word => {
        const token = String(word.text || '');
        const start = text.indexOf(token, cursor), seconds = Number(word.fromSec), endSeconds = Number(word.toSec);
        if (!token || start < 0 || !Number.isFinite(seconds) || seconds < 0) return [];
        cursor = start + token.length;
        return [{ seconds, start, length: token.length, ...(Number.isFinite(endSeconds) && endSeconds > seconds ? { endSeconds } : {}) }];
      });
      return { seconds: Number(line.fromSec), endSeconds: Number(line.toSec), text, ...(words.length ? { words } : {}) };
    },
    async load(requestedId, plainOnly = false) {
      const meta = this.metadata();
      const id = String(requestedId || meta?.id || '');
      if (!/^\d+$/.test(id)) return { state: 'no-track' };
      const key = id + (plainOnly ? ':plain' : ':auto');
      const cached = cache.get(key);
      if (cached && (plainOnly || cached.lines.length)) return cached;
      this.prepare();
      let lines = [], plain = '';
      const visibleSync = findModels()?.sync || models.sync;
      if (!plainOnly && String(visibleSync?.currentTrackId || '').split(':')[0] === id && visibleSync?.loadingState === 'RESOLVE')
        lines = Array.from(visibleSync.lines || []).map(l => this.timedLine(l))
          .filter(l => Number.isFinite(l.seconds) && l.seconds >= 0);
      if (!plainOnly && !lines.length && this.syncFactory) {
        const f = this.syncFactory;
        const model = f.type.create(f.snapshot, f.environment);
        await model.getData(id);
        if (model.loadingState === 'RESOLVE' && String(model.currentTrackId) === id)
          lines = Array.from(model.lines).map(l => this.timedLine(l))
            .filter(l => Number.isFinite(l.seconds) && l.seconds >= 0);
      }
      if (!lines.length && cached) return cached;
      if (!lines.length && (requestedId || meta?.hasLyrics)) {
        const original = models.plain, node = original.$treenode;
        const snapshot = { ...original.toJSON(), lyrics: null, currentTrackId: null, loadingState: 'IDLE' };
        const model = node.type.create(snapshot, node.root.environment);
        await model.getLyrics(id);
        if (model.loadingState === 'RESOLVE' && String(model.currentTrackId) === id) plain = typeof model.lyrics === 'string' ? model.lyrics : '';
      }
      const result = { state: lines.length || plain ? 'ready' : 'missing', id, lines, plain };
      // Only lyrics and track identifiers are cached; application auth stays inside Yandex Music.
      if (cache.size >= 64) cache.delete(cache.keys().next().value);
      if (result.state === 'ready') cache.set(key, result);
      return result;
    }
  };
  adapter.prepare();
  window.__islandYandex = adapter;
  return true;
})()
