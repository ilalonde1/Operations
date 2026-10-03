// KOR Remote viewer bridge -- runs inside MeshCentral's own page, in the app's remote-control window
// (KorRemoteViewerWindow). Ian, 2026-10-02: a ScreenConnect-style window, "with controls", instead of MeshCentral's page.
//
// It does two things and nothing else:
//   1. REPORTS the page's state to the window (window.chrome.webview.postMessage) whenever it changes: login page or
//      desktop page, the device, the connection state, the monitors, the key combinations, which page functions are
//      missing (a MeshCentral update that renames one shows here, not as a dead button).
//   2. CARRIES OUT the toolbar's commands (window.__korRemote.*) by calling the page's OWN functions -- the same ones its
//      buttons call -- so what a button does is exactly what MeshCentral's button does.
// Read against MeshCentral 1.2.5 views/default.handlebars. Every function used is in NEED; KorRemoteBridge.cs holds the
// same list and a test keeps the two equal.
(function () {
    if (window.__korRemote) return;

    var NEED = ['connectDesktop', 'sendCAD', 'deskSaveImage', 'deskToggleFull', 'deskSendKeys', 'deskSetDisplay', 'deviceChat', 'deviceLockFunction', 'go'];

    // deskToggleFull(e) reads e.shiftKey (shift = browser full screen too): called from here, an event with shift up.
    var NO_SHIFT = { shiftKey: false };

    function post(m) { try { window.chrome.webview.postMessage(m); } catch (e) { } }
    function has(name) { return typeof window[name] === 'function'; }
    function el(id) { return document.getElementById(id); }
    function shown(id) { var e = el(id); return !!e && e.style.display !== 'none'; }
    function isFull() { return document.body && document.body.classList.contains('fulldesk'); }

    // The page's desktop bars are replaced by the window's toolbar; the remote screen gets all the room.
    function addStyle() {
        if (el('korRemoteStyle')) return;
        var s = document.createElement('style');
        s.id = 'korRemoteStyle';
        s.textContent = '.fulldesk #deskarea1, .fulldesk #deskarea4 { display: none !important; }'
            + ' .fulldesk #deskarea3x { height: 100% !important; max-height: 100% !important; }';
        document.head.appendChild(s);
    }

    var autoConnected = false;
    var wantFiles = false;
    var last = '';

    function read() {
        var onDesktopPage = has('connectDesktop');
        if (!onDesktopPage) {
            var login = !!(document.querySelector('input[type=password]') || el('username'));
            return { page: login ? 'login' : 'other' };
        }
        var node = (typeof currentNode !== 'undefined' && currentNode) ? currentNode.name : null;
        var state = (typeof desktop !== 'undefined' && desktop) ? desktop.State : 0;
        var keys = [];
        var k = el('deskkeys');
        if (k) for (var i = 0; i < k.options.length; i++) keys.push({ value: k.options[i].value, text: k.options[i].text });
        var displays = [];
        var imgs = document.querySelectorAll('[id^=DeskMonitorSelectionX]');
        for (var j = 0; j < imgs.length; j++)
            displays.push({ number: parseInt(imgs[j].id.substring('DeskMonitorSelectionX'.length), 10), name: imgs[j].title || '', selected: !imgs[j].classList.contains('gray') });
        return {
            page: 'desktop',
            missing: NEED.filter(function (n) { return !has(n); }),
            node: node,
            state: state,
            keys: keys,
            displays: displays,
            files: shown('p13'),
        };
    }

    function tick() {
        var s = read();
        if (s.page === 'desktop' && s.node && s.missing.length === 0) {
            addStyle();
            // The desktop full-window (the page's own full-screen mode), unless the files page is showing.
            if (!wantFiles && shown('p11') && !isFull()) deskToggleFull(NO_SHIFT);
            // Connect once, as the page's Connect button does; after that the user decides.
            if (!autoConnected && shown('p11')) { autoConnected = true; if (s.state === 0) connectDesktop(null, 3); }
        }
        var json = JSON.stringify(s);
        if (json !== last) { last = json; post(s); }
    }

    window.__korRemote = {
        connect: function () { connectDesktop(null, 3); },
        disconnect: function () { connectDesktop(null, 0); },
        cad: function () { sendCAD(); },
        keys: function (value) { var k = el('deskkeys'); if (!k) return; k.value = String(value); deskSendKeys(); },
        lock: function () { deviceLockFunction(); },
        screenshot: function () { deskSaveImage(); },
        chat: function () { deviceChat(null); },
        display: function (n) { deskSetDisplay(n); },
        // The files page and back: the page's own full-screen mode covers only the desktop, so it is left first.
        files: function (on) {
            wantFiles = !!on;
            if (wantFiles) { if (isFull()) deskToggleFull(NO_SHIFT); go(13); }
            else { go(11); }
        },
        state: function () { last = ''; tick(); },
    };

    setInterval(tick, 500);
    tick();
})();
