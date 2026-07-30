// AgoraVoiceBridge.jslib — place in Assets/Plugins/WebGL/
// Thin voice-only bridge to the OFFICIAL Agora Web SDK (loaded via <script>
// tag in the WebGL template). No community plugin, no video, no token server
// (App-ID testing mode; add token auth before real classes go public).
//
// States: 0 idle · 1 joining · 2 joined+mic live · 3 joined listen-only
//         (mic denied/unavailable) · 4 error (SDK missing / join failed)

mergeInto(LibraryManager.library, {

  AgoraVoice_Init: function (appIdPtr) {
    var appId = UTF8ToString(appIdPtr);
    var S = window._agoraVoice = window._agoraVoice || {};
    S.state = 0;
    S.pendingMute = false;
    if (typeof AgoraRTC === 'undefined') {
      console.error('[AgoraVoice] AgoraRTC not found - is the SDK <script> tag in the WebGL template index.html?');
      S.state = 4;
      return;
    }
    S.appId = appId;
    AgoraRTC.setLogLevel(2); // warnings+errors only
    S.client = AgoraRTC.createClient({ mode: 'rtc', codec: 'vp8' });

    // Auto-subscribe and play every remote user's audio (mixed, non-spatial).
    S.client.on('user-published', function (user, mediaType) {
      S.client.subscribe(user, mediaType).then(function () {
        if (mediaType === 'audio' && user.audioTrack) {
          user.audioTrack.play();
          console.log('[AgoraVoice] Playing remote user ' + user.uid);
        }
      }).catch(function (err) {
        console.error('[AgoraVoice] Subscribe failed', err);
      });
    });
  },

  AgoraVoice_Join: function (channelPtr) {
    var channel = UTF8ToString(channelPtr);
    var S = window._agoraVoice;
    if (!S || !S.client || S.state === 1 || S.state === 2 || S.state === 3) return;
    S.state = 1;
    console.log('[AgoraVoice] Joining channel "' + channel + '"');

    S.client.join(S.appId, channel, null, null).then(function () {
      // Joined. Now request the microphone (this is what triggers the
      // browser permission prompt). Denial = stay joined, listen-only.
      return AgoraRTC.createMicrophoneAudioTrack().then(function (track) {
        S.micTrack = track;
        if (S.pendingMute) track.setMuted(true);
        return S.client.publish([track]).then(function () {
          S.state = 2;
          console.log('[AgoraVoice] Mic published - voice is LIVE');
        });
      }).catch(function (err) {
        console.warn('[AgoraVoice] Mic denied/unavailable - listen-only mode', err);
        S.state = 3;
      });
    }).catch(function (err) {
      console.error('[AgoraVoice] Join failed', err);
      S.state = 4;
    });
  },

  AgoraVoice_SetMute: function (muted) {
    var S = window._agoraVoice;
    if (!S) return;
    S.pendingMute = !!muted;                    // honored if mic arrives later
    if (S.micTrack) S.micTrack.setMuted(!!muted);
  },

  AgoraVoice_GetState: function () {
    var S = window._agoraVoice;
    return S ? S.state : 0;
  },

  AgoraVoice_Leave: function () {
    var S = window._agoraVoice;
    if (!S || !S.client) return;
    try { if (S.micTrack) { S.micTrack.close(); S.micTrack = null; } } catch (e) {}
    S.client.leave();
    S.state = 0;
    console.log('[AgoraVoice] Left channel');
  }
});
