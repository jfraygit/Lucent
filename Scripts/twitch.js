(function () {
  'use strict';

  var L = window.__LUCENT_NS__;
  if (!L || !L.hostMatches('twitch.tv')) return;

  var workerSrc = L.workerSrc;

  if (workerSrc && typeof Worker !== 'undefined') {
    var RealWorker = Worker;

    var Patched = function (url, options) {
      try {
        if (typeof url === 'string' && url.lastIndexOf('blob:', 0) === 0) {
          var shim = workerSrc + '\nimportScripts(' + JSON.stringify(url) + ');';
          var blob = new Blob([shim], { type: 'application/javascript' });
          return new RealWorker(URL.createObjectURL(blob), options);
        }
      } catch (e) {
      }
      return new RealWorker(url, options);
    };

    Patched.prototype = RealWorker.prototype;
    window.Worker = Patched;
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
