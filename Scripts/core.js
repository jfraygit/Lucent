(function () {
  'use strict';

  function css(rules) {
    var apply = function () {
      var style = document.createElement('style');
      style.textContent = rules;
      (document.head || document.documentElement).appendChild(style);
    };
    if (document.documentElement) apply();
    else document.addEventListener('DOMContentLoaded', apply, { once: true });
  }

  css(
    '::-webkit-scrollbar { width: 12px; height: 12px; background: transparent; }' +
    '::-webkit-scrollbar-track { background: transparent; }' +
    '::-webkit-scrollbar-corner { background: transparent; }' +
    '::-webkit-scrollbar-button { display: none; }' +
    '::-webkit-scrollbar-thumb {' +
    '  background-color: rgba(124, 92, 214, .55);' +
    '  border: 3px solid transparent;' +
    '  background-clip: padding-box;' +
    '  border-radius: 999px;' +
    '}' +
    '::-webkit-scrollbar-thumb:hover {' +
    '  background-color: #7C5CD6;' +
    '  box-shadow: 0 0 8px rgba(124, 92, 214, .7);' +
    '}' +
    '::-webkit-scrollbar-thumb:active {' +
    '  background-color: #8E72E4;' +
    '  box-shadow: 0 0 10px rgba(124, 92, 214, .9);' +
    '}');

  var ALLOWED = __LUCENT_HOSTS__;
  var host = location.hostname;
  var permitted = false;

  for (var i = 0; i < ALLOWED.length; i++) {
    if (host === ALLOWED[i] || host.endsWith('.' + ALLOWED[i])) { permitted = true; break; }
  }
  if (!permitted) return;

  if (window.__LUCENT_NS__) return;

  function hostMatches(suffix) {
    var h = location.hostname;
    return h === suffix || h.endsWith('.' + suffix);
  }

  window.__LUCENT_NS__ = { css: css, hostMatches: hostMatches };
})();
