(function () {
  'use strict';

  var L = window.__LUCENT_NS__;
  if (!L || !L.hostMatches('twitch.tv')) return;

  var DEBUG = false;

  var workerSrc = L.workerSrc;

  if (DEBUG && !workerSrc) console.log('[Lucent] worker source missing; the worker cannot be patched');

  var NOT_CHANNELS = [
    'directory', 'videos', 'settings', 'u', 'popout', 'moderator', 'subs', 'friends',
    'drops', 'search', 'downloads', 'store', 'prime', 'turbo', 'jobs', 'p', 'legal',
    'collections', 'team', 'following', 'wallet', 'inventory', 'payments'
  ];

  function currentChannel() {
    var first = location.pathname.split('/')[1] || '';
    first = first.toLowerCase();

    if (!first) return null;
    if (NOT_CHANNELS.indexOf(first) !== -1) return null;
    if (!/^[a-z0-9_]{1,25}$/.test(first)) return null;

    return first;
  }

  var live = [];

  function announceChannel() {
    var channel = currentChannel();
    if (!channel) return;

    for (var i = 0; i < live.length; i++) {
      try { live[i].postMessage({ __lucent: { channel: channel } }); } catch (e) { }
    }
  }

  if (workerSrc && typeof Worker !== 'undefined') {
    var RealWorker = Worker;

    var Patched = function (url, options) {
      try {
        if (DEBUG) {
          console.log('[Lucent] Worker(', typeof url, String(url).slice(0, 60),
                      ') type=', (options && options.type) || 'classic');
        }

        if (typeof url === 'string' && url.lastIndexOf('blob:', 0) === 0) {
          var channel = currentChannel();
          var seed = 'self.__LUCENT_CHANNEL__ = ' + JSON.stringify(channel) + ';\n';
          var shim = seed + workerSrc + '\nimportScripts(' + JSON.stringify(url) + ');';
          var blob = new Blob([shim], { type: 'application/javascript' });

          if (DEBUG) console.log('[Lucent] patched this worker, channel =', channel);

          var created = new RealWorker(URL.createObjectURL(blob), options);
          live.push(created);
          return created;
        }

        if (DEBUG) console.log('[Lucent] NOT patched: not a blob: string URL');
      } catch (e) {
        if (DEBUG) console.log('[Lucent] worker patch threw:', e && e.message);
      }
      return new RealWorker(url, options);
    };

    Patched.prototype = RealWorker.prototype;
    window.Worker = Patched;

    ['pushState', 'replaceState'].forEach(function (name) {
      var original = history[name];
      if (typeof original !== 'function') return;

      history[name] = function () {
        var result = original.apply(this, arguments);
        try { announceChannel(); } catch (e) { }
        return result;
      };
    });

    window.addEventListener('popstate', announceChannel);
  }

  L.css([
    '[data-a-target="video-ad-label"],',
    '[data-a-target="video-ad-countdown"],',
    '[data-test-selector="sad-overlay"],',
    '.video-player__ad-info-container,',
    '.player-ad-notice,',
    '.promoted-content-card,',
    '[data-a-target="ax-overlay"]',
    '{ display: none !important; }'
  ].join(''));
})();
