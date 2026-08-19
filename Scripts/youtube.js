(function () {
  'use strict';

  var DEBUG = false;

  var L = window.__LUCENT_NS__;
  if (!L) return;
  if (!L.hostMatches('youtube.com') &&
      !L.hostMatches('youtube-nocookie.com') &&
      !L.hostMatches('youtu.be')) return;

  var AD_KEYS = [
    'adPlacements',
    'playerAds',
    'adSlots',
    'adBreakHeartbeatParams',
    'adServingDataEntity',
    'playerAdParams',
    'enforcementMessageViewModel'
  ];

  var AD_KEY_SET = Object.create(null);
  for (var i = 0; i < AD_KEYS.length; i++) AD_KEY_SET[AD_KEYS[i]] = true;

  var MARKER = new RegExp('"(' + AD_KEYS.join('|') + ')"');

  function prune(value, depth) {
    if (value === null || typeof value !== 'object' || depth > 16) return value;

    if (Array.isArray(value)) {
      for (var i = 0; i < value.length; i++) prune(value[i], depth + 1);
      return value;
    }

    var keys = Object.keys(value);
    for (var k = 0; k < keys.length; k++) {
      var key = keys[k];
      if (AD_KEY_SET[key]) {
        try { delete value[key]; } catch (e) { }
        continue;
      }
      prune(value[key], depth + 1);
    }
    return value;
  }

  var nativeParse = JSON.parse;
  JSON.parse = function (text, reviver) {
    var result = nativeParse.call(this, text, reviver);
    if (typeof text === 'string' && MARKER.test(text)) prune(result, 0);
    return result;
  };

  var nativeJson = Response.prototype.json;
  Response.prototype.json = function () {
    var url = this.url || '';
    var self = this;
    return nativeJson.call(self).then(function (data) {
      return url.indexOf('/youtubei/') !== -1 ? prune(data, 0) : data;
    });
  };

  function trapGlobal(name) {
    var stash;
    try {
      Object.defineProperty(window, name, {
        configurable: true,
        get: function () { return stash; },
        set: function (value) { stash = prune(value, 0); }
      });
    } catch (e) { }
  }
  trapGlobal('ytInitialPlayerResponse');
  trapGlobal('ytInitialData');

  L.css([
    'ytd-rich-item-renderer:has(ytd-ad-slot-renderer) { display: none !important; }',
    'ytd-rich-item-renderer:has(ytd-in-feed-ad-layout-renderer) { display: none !important; }',
    'ytd-rich-item-renderer:has(ytd-display-ad-renderer) { display: none !important; }',
    'ytd-rich-item-renderer:has(ytd-promoted-sparkles-web-renderer) { display: none !important; }',
    'ytd-rich-item-renderer:has(ytd-promoted-video-renderer) { display: none !important; }',
    'ytd-rich-section-renderer:has(ytd-statement-banner-renderer) { display: none !important; }',

    'ytd-ad-slot-renderer { display: none !important; }',
    'ytd-promoted-sparkles-web-renderer { display: none !important; }',
    'ytd-promoted-video-renderer { display: none !important; }',
    'ytd-display-ad-renderer { display: none !important; }',
    'ytd-companion-slot-renderer { display: none !important; }',
    'ytd-action-companion-ad-renderer { display: none !important; }',
    'ytd-in-feed-ad-layout-renderer { display: none !important; }',
    'ytd-banner-promo-renderer { display: none !important; }',
    'ytd-statement-banner-renderer { display: none !important; }',
    'ytm-promoted-video-renderer { display: none !important; }',
    '#player-ads { display: none !important; }',
    '#masthead-ad { display: none !important; }',
    '.ytp-ad-overlay-container { display: none !important; }',
    '.ytp-ad-progress-list { display: none !important; }',

    'ytd-rich-section-renderer:has(ytd-rich-shelf-renderer[is-shorts]) { display: none !important; }',
    'ytd-rich-shelf-renderer[is-shorts] { display: none !important; }',
    'ytd-reel-shelf-renderer { display: none !important; }',
    'ytd-rich-item-renderer:has(ytm-shorts-lockup-view-model) { display: none !important; }',
    'grid-shelf-view-model { display: none !important; }',

    'ytd-rich-section-renderer:has(ytd-mini-game-card-view-model) { display: none !important; }',
    'ytd-rich-item-renderer:has(ytd-mini-game-card-view-model) { display: none !important; }',
    'ytd-mini-game-card-view-model { display: none !important; }',
    'mini-game-card-view-model { display: none !important; }'
  ].join('\n'));

  if (L.hostMatches('youtube.com')) {
    var A_YEAR = 60 * 60 * 24 * 365;

    var readWide = function () {
      var found = /(?:^|;\s*)wide=([^;]*)/.exec(document.cookie);
      return found ? found[1] : null;
    };

    var written = null;

    var persist = function () {
      var value = readWide();
      if (value === null || value === written) return;

      document.cookie = 'wide=' + value + '; domain=.youtube.com; path=/; secure' +
                        '; max-age=' + A_YEAR;
      written = value;

      if (DEBUG) console.log('[Lucent] theatre: made wide =', value, 'persistent');
    };

    persist();
    setInterval(persist, 2000);

    try { localStorage.removeItem('lucent.wide'); } catch (e) { }

    if (DEBUG) watchForTheatreState();
  }

  function watchForTheatreState() {
    function layout() {
      var flexy = document.querySelector('ytd-watch-flexy');
      if (!flexy) return 'not a watch page';
      return flexy.hasAttribute('theater') ? 'THEATRE' : 'default';
    }

    console.log('[Lucent] theatre at document-created: wide cookie =', readWide(),
                '| layout =', layout());

    var lastWide = readWide();
    var lastLayout = layout();

    setInterval(function () {
      var nextWide = readWide();
      var nextLayout = layout();

      if (nextWide !== lastWide) console.log('[Lucent] wide cookie:', lastWide, '->', nextWide);

      if (nextLayout !== lastLayout) {
        console.log('[Lucent] layout:', lastLayout, '->', nextLayout,
                    '| wide =', nextWide);
      }

      lastWide = nextWide;
      lastLayout = nextLayout;
    }, 250);
  }

  var player = null;
  setInterval(function () {
    var skip = document.querySelector(
      '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button');
    if (skip) { skip.click(); return; }

    player = player || document.querySelector('.html5-video-player');
    if (!player || !player.classList.contains('ad-showing')) return;

    var video = document.querySelector('video.html5-main-video');
    if (video && isFinite(video.duration) && video.duration > 0) {
      video.currentTime = video.duration;
    }
  }, 700);
})();
