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
            if (card) card.remove();
        }
    });

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
    document.addEventListener('DOMContentLoaded', function () { highlight(document); syncNotesToggle(); });
    document.addEventListener('htmx:load', function (e) { highlight(e.target); });
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () { syncHighlightTheme(); lab.restyleCharts(); });
})();
