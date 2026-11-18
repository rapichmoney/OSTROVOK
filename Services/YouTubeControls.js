(async expectedId => {
  const page = new URL(location.href);
  const id = page.searchParams.get('v') || page.pathname.match(/^\/shorts\/([\w-]{11})/)?.[1];
  if (!['www.youtube.com', 'youtube.com', 'm.youtube.com'].includes(page.hostname) || id !== expectedId) return {ok: false};
  const video = document.querySelector('video.html5-main-video');
  if (!video) return {ok: false};
  try {
    if (video.paused || video.ended) await video.play();
    else video.pause();
  } catch { return {ok: false}; }
  return {ok: true, playing: !video.paused && !video.ended,
    position: video.currentTime, duration: Number.isFinite(video.duration) ? video.duration : 0};
})
