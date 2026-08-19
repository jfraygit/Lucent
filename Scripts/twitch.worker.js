(function () {
  'use strict';
  if (self.__LUCENT_NS__) return;
  self.__LUCENT_NS__ = true;

  var DEBUG = false;
  function log() {
    if (!DEBUG) return;
    try { console.log.apply(console, ['[Lucent worker]'].concat([].slice.call(arguments))); } catch (e) { }
  }


  var CLIENT_ID = 'kimne78kx3ncx6brgo4mv6wki5h1ko';
  var AD_MARKER = 'twitch-stitched-ad';
  var BACKUP_TYPES = ['embed', 'thunderdome', 'picture-by-picture'];
  var BACKUP_TTL_MS = 4 * 60 * 1000;

  var PQ_HASH = '0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc2200705b0712';

  var realFetch = self.fetch;
  var backups = new Map();
  var channelByPlaylist = new Map();

  var lastChannel = self.__LUCENT_CHANNEL__ || null;

  log('installed, channel =', lastChannel);

  self.addEventListener('message', function (event) {
    try {
      var mine = event && event.data && event.data.__lucent;
      if (!mine || !mine.channel) return;
      if (mine.channel === lastChannel) return;

      log('channel changed', lastChannel, '->', mine.channel);
      lastChannel = mine.channel;
      backups.clear();
      channelByPlaylist.clear();
    } catch (e) { }
  });

  function stripQuery(url) {
    var q = url.indexOf('?');
    return q === -1 ? url : url.slice(0, q);
  }

  function channelFromUsher(url) {
    var m = /\/api\/channel\/hls\/([^./?]+)\.m3u8/.exec(url);
    return m ? m[1].toLowerCase() : null;
  }

  async function accessToken(channel, playerType) {
    var res = await realFetch('https://gql.twitch.tv/gql', {
      method: 'POST',
      headers: { 'Client-ID': CLIENT_ID, 'Content-Type': 'text/plain;charset=UTF-8' },
      body: JSON.stringify({
        operationName: 'PlaybackAccessToken',
        variables: {
          isLive: true, login: channel, isVod: false, vodID: '', playerType: playerType
        },
        extensions: { persistedQuery: { version: 1, sha256Hash: PQ_HASH } }
      })
    });
    if (!res.ok) return null;
    var json = await res.json();
    return (json && json.data && json.data.streamPlaybackAccessToken) || null;
  }

  async function isAdFree(url) {
    try {
      var res = await realFetch(url);
      if (!res.ok) return false;
      return (await res.text()).indexOf(AD_MARKER) === -1;
    } catch (e) {
      return false;
    }
  }

  async function variantFor(channel, playerType) {
    var token = await accessToken(channel, playerType);
    if (!token || !token.value || !token.signature) return null;

    var url = 'https://usher.ttvnw.net/api/channel/hls/' + channel + '.m3u8'
          + '?allow_source=true&fast_bread=true&player_backend=mediaplayer'
          + '&playlist_include_framerate=true&reassignments_supported=true'
          + '&supported_codecs=avc1&transcode_mode=cbr_v1'
          + '&p=' + Math.floor(Math.random() * 1e7)
          + '&play_session_id=' + Math.random().toString(16).slice(2)
          + '&sig=' + encodeURIComponent(token.signature)
          + '&token=' + encodeURIComponent(token.value);

    var res = await realFetch(url);
    if (!res.ok) return null;

    var lines = (await res.text()).split('\n');
    var variants = [];
    for (var n = 0; n < lines.length; n++) {
      var line = lines[n].trim();
      if (line.lastIndexOf('http', 0) === 0) variants.push(line);
    }

    return variants.length ? variants[variants.length - 1] : null;
  }

  async function fetchBackupVariant(channel) {
    for (var i = 0; i < BACKUP_TYPES.length; i++) {
      try {
        var url = await variantFor(channel, BACKUP_TYPES[i]);
        if (!url) continue;

        if (await isAdFree(url)) return url;
      } catch (e) { }
    }
    return null;
  }

  function ensureBackup(channel) {
    var now = Date.now();
    var hit = backups.get(channel);
    if (hit && now - hit.at < BACKUP_TTL_MS) return hit.promise;

    var entry = { at: now, promise: fetchBackupVariant(channel) };
    backups.set(channel, entry);
    return entry.promise;
  }

  self.fetch = async function (input, init) {
    var url = typeof input === 'string' ? input : (input && input.url) || '';

    try {
      if (url.indexOf('usher.ttvnw.net/api/channel/hls/') !== -1) {
        var channel = channelFromUsher(url);
        log('master playlist for', channel);
        var master = await realFetch(input, init);
        if (channel && master.ok) {
          lastChannel = channel;
          var body = await master.clone().text();
          var rows = body.split('\n');
          for (var i = 0; i < rows.length; i++) {
            var row = rows[i].trim();
            if (row.lastIndexOf('http', 0) === 0) {
              channelByPlaylist.set(stripQuery(row), channel);
            }
          }
          ensureBackup(channel);
        }
        return master;
      }

      if (url.indexOf('.m3u8') !== -1) {
        var res = await realFetch(input, init);
        if (!res.ok) return res;

        var text = await res.clone().text();
        if (text.indexOf(AD_MARKER) === -1) return res;

        log('ad break seen in', stripQuery(url).slice(-60));

        var ch = channelByPlaylist.get(stripQuery(url)) || lastChannel;
        if (!ch) { log('no channel known for this playlist; cannot swap'); return res; }

        var backupUrl = await ensureBackup(ch);
        if (!backupUrl) { log('no ad-free backup available for', ch); return res; }

        var backup = await realFetch(backupUrl);
        if (!backup.ok) return res;

        var backupText = await backup.text();

        if (backupText.indexOf(AD_MARKER) !== -1) {
          backups.delete(ch);

          var freshUrl = await ensureBackup(ch);
          if (!freshUrl) return res;

          var fresh = await realFetch(freshUrl);
          if (!fresh.ok) return res;

          backupText = await fresh.text();
          if (backupText.indexOf(AD_MARKER) !== -1) {
            log('re-selected backup ALSO carries ads; serving the real playlist');
            return res;
          }
        }

        log('swapped in ad-free backup for', ch);

        return new Response(backupText, {
          status: 200,
          statusText: 'OK',
          headers: { 'Content-Type': 'application/vnd.apple.mpegurl' }
        });
      }
    } catch (e) {
    }

    return realFetch(input, init);
  };
})();
