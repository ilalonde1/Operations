/* KOR Remote: KOR's defaults on MeshCentral 1.2.5's signed-in page. Served from
   /opt/meshcentral/meshcentral-web/public/scripts/custom.js, which MeshCentral loads in the page head BEFORE its own
   script runs. Everything here only sets a default or fills an empty field: whoever is at the page can change it.
   If an upgrade renames a function this wraps, that piece simply stops applying; nothing breaks.

   1. A remote desktop opens fitted to the window, aspect kept -- never stretched. (MeshCentral remembers the view mode
      per browser; "stretch" squashed KOR-104N's screen on 2026-10-01.)
   2. A PC with several monitors opens on the first one, not "All displays" (an off monitor fills half the view).
   3. The Web-RDP sign-in box opens with Domain "KOR", user "Administrator" (Ian, 2026-10-01) and "Remember credentials"
      ticked. The PASSWORD IS NEVER HERE: this file is public (served before sign-in). It is typed once per machine and
      MeshCentral keeps it encrypted. */
(function () {
    'use strict';

    // 1. View mode: 0 = fit with aspect kept, 1 = actual size, 2 = stretch. The page reads this as its script starts.
    try {
        if (localStorage.getItem('deskAspectRatio') === '2') localStorage.setItem('deskAspectRatio', '0');
    } catch (e) { /* storage blocked: the page keeps its own default (0) */ }

    window.addEventListener('load', function () {
        // 4. The Home support download, one click from the header: a zip of the Assistant (wired to Home support, through
        //    remote.korstructural.com:4445) with a one-page note for the person. Served from public/downloads/ on the
        //    office/VPN-only site: Ian downloads it and sends the file.
        try {
            var right = document.querySelector('#masthead .masthead-right');
            if (right && !document.getElementById('korHelpDownload')) {
                var a = document.createElement('a');
                a.id = 'korHelpDownload';
                a.href = 'downloads/KOR-Remote-Help.zip';
                a.setAttribute('download', 'KOR-Remote-Help.zip');
                a.title = 'The zip to send someone at home: they run it and click Request Help (nothing stays installed).';
                a.textContent = 'Home help download';
                right.insertBefore(a, right.firstChild);
            }
        } catch (e) { /* header unchanged */ }

        // 2. First real display when a desktop first reports more than one.
        if (typeof window.deskDisplayInfo === 'function') {
            var originalInfo = window.deskDisplayInfo;
            window.deskDisplayInfo = function (sender, displays, selDisplay) {
                try {
                    if (sender && !sender.korPicked && displays) {
                        sender.korPicked = true;   // once per connection: a later choice of "All displays" stands
                        var keys = Object.keys(displays);
                        var real = keys.filter(function (k) { return String(displays[k]).indexOf('All') !== 0; });
                        var selected = displays[selDisplay];
                        if (real.length > 1 && selected && String(selected).indexOf('All') === 0) {
                            window.deskPreferedStickyDisplay = parseInt(real[0], 10);
                        }
                    }
                } catch (e) { /* leave the page's own choice */ }
                return originalInfo.apply(this, arguments);
            };
        }

        // 3. Web-RDP sign-in: fill only what is empty.
        if (typeof window.askRdpCredentials === 'function') {
            var originalAsk = window.askRdpCredentials;
            window.askRdpCredentials = function () {
                var result = originalAsk.apply(this, arguments);
                try {
                    var domain = document.getElementById('d2domain');
                    var user = document.getElementById('d2user');
                    var save = document.getElementById('d2savecred');
                    if (domain && !domain.value) domain.value = 'KOR';
                    if (user && !user.value) user.value = 'Administrator';
                    if (save) save.checked = true;
                    if (typeof window.askRdpCredentialsValidate === 'function') window.askRdpCredentialsValidate();
                    var pass = document.getElementById('d2pass');
                    if (pass) setTimeout(function () { pass.focus(); }, 300);
                } catch (e) { /* the box works as MeshCentral built it */ }
                return result;
            };
        }
    });
})();
