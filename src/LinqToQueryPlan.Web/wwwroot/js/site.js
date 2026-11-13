// Small glue: theme, text size, notes toggle, chart rendering, syntax highlighting, copy buttons.
(function () {
    const root = document.documentElement;

    function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { } }

    function currentTheme() {
        if (root.dataset.theme) return root.dataset.theme;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    function syncHighlightTheme() {
        const dark = currentTheme() === 'dark';
        const l = document.getElementById('hl-light'), d = document.getElementById('hl-dark');
        if (l) l.disabled = dark;
        if (d) d.disabled = !dark;
    }

    function syncNotesToggle() {
        const open = root.dataset.notes !== 'hidden';
        document.querySelectorAll('[data-toggle="notes"]').forEach(function (b) { b.setAttribute('aria-expanded', open); });
    }

    document.addEventListener('click', function (e) {
        const btn = e.target.closest('[data-toggle]');
        if (btn) {
            const what = btn.dataset.toggle;
            if (what === 'theme') {
                root.dataset.theme = currentTheme() === 'dark' ? 'light' : 'dark';
                store('lab-theme', root.dataset.theme);
                syncHighlightTheme();
                lab.restyleCharts();
            } else if (what === 'size') {
                root.dataset.size = root.dataset.size === 'large' ? '' : 'large';
                store('lab-size', root.dataset.size);
            } else if (what === 'notes') {
                root.dataset.notes = root.dataset.notes === 'hidden' ? '' : 'hidden';
                store('lab-notes', root.dataset.notes);
                syncNotesToggle();
            }
            return;
        }

        const copy = e.target.closest('[data-copy]');
        if (copy) {
            const target = document.getElementById(copy.dataset.copy);
            if (target && navigator.clipboard) {
                navigator.clipboard.writeText(target.innerText).then(function () {
                    const old = copy.textContent; copy.textContent = 'Copied'; setTimeout(function () { copy.textContent = old; }, 1200);
                });
            }
            return;
        }

        const close = e.target.closest('[data-close]');
        if (close) {
            const card = close.closest('.result');
            if (card) { if (card.classList.contains('focused')) setFocus(null); card.remove(); }
            return;
        }

        const focus = e.target.closest('button[data-focus]');
        if (focus) {
            const card = focus.closest('.result');
            setFocus(card.classList.contains('focused') ? null : card);
            return;
        }

        const pin = e.target.closest('[data-pin]');
        if (pin) {
            const card = pin.closest('.result');
            setBaseline(card.classList.contains('is-baseline') ? null : card);
            return;
        }

        if (e.target.closest('[data-unpin]')) setBaseline(null);
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && root.dataset.focus !== undefined) setFocus(null);
    });

    // ---------- focus mode ----------
    function setFocus(card) {
        document.querySelectorAll('.result.focused').forEach(function (c) {
            c.classList.remove('focused');
            const b = c.querySelector('button[data-focus]'); if (b) b.textContent = 'Focus';
        });
        if (card) {
            card.classList.add('focused');
            card.querySelector('button[data-focus]').textContent = 'Exit focus';
            card.scrollTop = 0;
            root.dataset.focus = '';
        } else {
            delete root.dataset.focus;
        }
    }

    // ---------- baseline comparison ----------
    // The baseline is the numbers from one pinned run, kept per demo for this browser tab.
    function resultsEl() { return document.getElementById('results'); }
    function baselineKey() { const r = resultsEl(); return r ? 'lab-baseline:' + r.dataset.demo : null; }
    let baseline = null;

    function readStats(card) {
        const stats = {};
        card.querySelectorAll('.stat[data-metric]').forEach(function (t) { stats[t.dataset.metric] = Number(t.dataset.value); });
        return stats;
    }

    function setBaseline(card) {
        document.querySelectorAll('.result.is-baseline').forEach(function (c) { c.classList.remove('is-baseline'); });
        baseline = card ? { id: card.id, label: card.dataset.label, time: card.dataset.time, stats: readStats(card) } : null;
        const key = baselineKey();
        try { if (key) { if (baseline) sessionStorage.setItem(key, JSON.stringify(baseline)); else sessionStorage.removeItem(key); } } catch (e) { }
        applyBaseline();
    }

    function fmt(n) { return Math.round(n).toLocaleString(); }

    function describe(metric, base, cur) {
        if (metric === 'rows') {
            if (base === cur) return { cls: 'same', text: 'same rows' };
            return { cls: 'same', text: (cur > base ? '+' : '') + fmt(cur - base) };
        }
        if (base === cur) return { cls: 'same', text: 'no change' };
        const b = Math.max(base, 1), c = Math.max(cur, 1);
        const better = cur < base;
        const ratio = better ? b / c : c / b;
        const words = metric === 'ms' ? (better ? 'faster' : 'slower') : (better ? 'fewer' : 'more');
        let text;
        if (ratio >= 1.5) text = (ratio >= 10 ? fmt(ratio) : ratio.toFixed(1)) + '\u00d7 ' + words;
        else text = (better ? '\u2212' : '+') + Math.round(Math.abs(cur - base) / b * 100) + '% ' + (metric === 'ms' ? 'time' : 'reads');
        return { cls: better ? 'better' : 'worse', text: text };
    }

    function applyBaseline() {
        const chip = document.querySelector('.baseline-chip');
        if (chip) {
            chip.hidden = !baseline;
            if (baseline) chip.querySelector('.baseline-chip-text').textContent = 'Baseline: ' + baseline.label + ' (' + baseline.time + ')';
        }
        document.querySelectorAll('.result').forEach(function (card) {
            const isBase = !!baseline && card.id === baseline.id;
            card.classList.toggle('is-baseline', isBase);
            const pinBtn = card.querySelector('[data-pin]');
            if (pinBtn) pinBtn.textContent = isBase ? 'Unpin' : 'Pin as baseline';
            const note = card.querySelector('.compare-note');
            if (note) note.textContent = baseline && !isBase ? 'Compared with baseline: ' + baseline.label + ' (' + baseline.time + ')' : '';
            card.querySelectorAll('.stat[data-metric]').forEach(function (t) {
                const d = t.querySelector('.stat-delta');
                d.className = 'stat-delta'; d.textContent = '';
                if (!baseline || isBase) return;
                const base = baseline.stats[t.dataset.metric];
                if (base === undefined) return;
                const r = describe(t.dataset.metric, base, Number(t.dataset.value));
                d.classList.add(r.cls);
                d.textContent = r.text;
                const was = document.createElement('span'); was.className = 'was'; was.textContent = 'was ' + fmt(base);
                d.appendChild(was);
            });
        });
    }

    // Each Run starts with a clean results area, except the pinned baseline so the comparison stays on screen.
    document.addEventListener('htmx:beforeRequest', function (e) {
        const form = e.target;
        if (!form.matches || !form.matches('form[hx-target="#results"]')) return;
        document.querySelectorAll('#results > .result:not(.is-baseline)').forEach(function (c) { c.remove(); });
    });

    function loadBaseline() {
        const key = baselineKey();
        if (!key) return;
        try { const v = sessionStorage.getItem(key); baseline = v ? JSON.parse(v) : null; } catch (e) { baseline = null; }
        applyBaseline();
    }

    const charts = {};
    function palette() {
        const s = getComputedStyle(root);
        return {
            series: ['--c1', '--c2', '--c3', '--c4'].map(v => s.getPropertyValue(v).trim()),
            text: s.getPropertyValue('--text').trim(),
            grid: s.getPropertyValue('--border').trim(),
        };
    }

    function applyStyle(cfg) {
        const p = palette();
        cfg.data.datasets.forEach(function (ds, i) {
            const c = p.series[i % p.series.length];
            ds.backgroundColor = c;
            ds.borderColor = c;
            ds.borderWidth = 0;
            ds.borderRadius = 3;
        });
        cfg.options.color = p.text;
        cfg.options.plugins.title.color = p.text;
        cfg.options.plugins.title.font = { size: 14, weight: '600' };
        cfg.options.plugins.legend.labels = { color: p.text };
        cfg.options.scales.x = Object.assign(cfg.options.scales.x || {}, { ticks: { color: p.text }, grid: { color: p.grid } });
        cfg.options.scales.y.ticks = { color: p.text };
        cfg.options.scales.y.grid = { color: p.grid };
        cfg.options.scales.y.title.color = p.text;
        return cfg;
    }

    window.lab = {
        chart: function (id, cfg) {
            const el = document.getElementById(id);
            if (!el || !window.Chart) return;
            if (charts[id]) charts[id].destroy();
            charts[id] = new Chart(el, applyStyle(cfg));
            charts[id].$raw = cfg;
        },
        restyleCharts: function () {
            Object.keys(charts).forEach(function (id) {
                const c = charts[id];
                if (!document.getElementById(id)) { c.destroy(); delete charts[id]; return; }
                applyStyle(c.config);
                c.update();
            });
        },
    };

    function highlight(scope) {
        if (!window.hljs) return;
        scope.querySelectorAll('pre code:not(.hljs)').forEach(function (b) { hljs.highlightElement(b); });
    }

    syncHighlightTheme();
    document.addEventListener('DOMContentLoaded', function () { highlight(document); syncNotesToggle(); loadBaseline(); });
    document.addEventListener('htmx:load', function (e) { highlight(e.target); if (e.target.querySelector && (e.target.matches('.result') || e.target.querySelector('.result'))) applyBaseline(); });
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () { syncHighlightTheme(); lab.restyleCharts(); });
})();
