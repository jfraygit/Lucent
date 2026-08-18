(function () {
  'use strict';

  var ALLOWED = __LUCENT_HOSTS__;
  var host = location.hostname;
  var permitted = false;

  for (var i = 0; i < ALLOWED.length; i++) {
    if (host === ALLOWED[i] || host.endsWith('.' + ALLOWED[i])) { permitted = true; break; }
  }
  if (!permitted) return;

  if (window.__LUCENT_NS__) return;

  function css(rules) {
    var apply = function () {
      var style = document.createElement('style');
      style.textContent = rules;
      (document.head || document.documentElement).appendChild(style);
    };
    if (document.documentElement) apply();
    else document.addEventListener('DOMContentLoaded', apply, { once: true });
  }

  function hostMatches(suffix) {
    var h = location.hostname;
    return h === suffix || h.endsWith('.' + suffix);
  }

  window.__LUCENT_NS__ = { css: css, hostMatches: hostMatches };
})();
