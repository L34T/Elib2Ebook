/* Elib2Ebook — Apple-style fluid motion.
 *
 * A tiny damped-spring engine for interruptible UI motion, in the spirit of
 * the "Designing Fluid Interfaces" approach: every animation starts from the
 * current on-screen value, inherits velocity, and can be grabbed and reversed
 * at any instant. Springs are inherently interruptible and velocity-aware.
 *
 * We map Apple's two parameters directly:
 *   damping  = damping ratio (1.0 critically damped, <1.0 bounce)
 *   response = time to reach the target in seconds (lower = snappier)
 */
(function () {
    const state = new WeakMap();
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const reducedTransparency = window.matchMedia('(prefers-reduced-transparency: reduce)');

    // Critically/under-damped spring -> stiffness + damping coefficient.
    function springParams(damping, response) {
        const w0 = (2 * Math.PI) / response; // natural angular frequency
        const stiffness = w0 * w0;
        const dampingC = 2 * damping * w0;   // zeta * 2 * sqrt(k)
        return { stiffness, dampingC };
    }

    // Current numeric value of a CSS length property ("0px" -> 0).
    function read(el, prop) {
        return parseFloat(getComputedStyle(el)[prop]) || 0;
    }

    /**
     * Animate a numeric CSS property with a damped spring to `target`.
     * Interruptible: calling `go` again on the same element re-targets from the
     * live on-screen value and carries the current velocity (no "brick wall").
     *
     * @param {HTMLElement} el
     * @param {string} prop       e.g. 'left', 'top'
     * @param {number} target     final value in `unit`
     * @param {object} [opts]     { damping, response, unit, onDone }
     */
    function go(el, prop, target, opts = {}) {
        const damping = opts.damping ?? 1.0;
        const response = opts.response ?? 0.32;
        const unit = opts.unit ?? 'px';
        const { stiffness, dampingC } = springParams(damping, response);

        // Reduced motion: no springing — just land. Feedback, not vestibular motion.
        if (reduced.matches) {
            el.style[prop] = target + unit;
            opts.onDone && opts.onDone();
            return;
        }

        let s = state.get(el);
        if (!s || s.raf === null) {
            s = { x: read(el, prop), v: 0 };
        }
        if (s.raf) cancelAnimationFrame(s.raf);

        const set = (v) => { el.style[prop] = v + unit; };
        let prev = performance.now();

        function frame(now) {
            const dt = Math.min((now - prev) / 1000, 1 / 30);
            prev = now;
            const a = -stiffness * (s.x - target) - dampingC * s.v;
            s.v += a * dt;
            s.x += s.v * dt;
            set(s.x);

            if (Math.abs(s.x - target) < 0.3 && Math.abs(s.v) < 0.5) {
                set(target);
                s.raf = null;
                opts.onDone && opts.onDone();
                return;
            }
            s.raf = requestAnimationFrame(frame);
        }
        s.raf = requestAnimationFrame(frame);
        state.set(el, s);
    }

    // Read the current translateX of an element (fallback to 0).
    function readTranslateX(el) {
        const t = getComputedStyle(el).transform;
        if (!t || t === 'none') return 0;
        const m = t.match(/matrix\(([^)]+)\)/);
        if (m) {
            const parts = m[1].split(', ').map(Number);
            return parts[4] || 0;
        }
        return 0;
    }

    // Spring a transform: translateX from current live value to `target` px.
    function springX(el, target, opts = {}) {
        const damping = opts.damping ?? 1.0;
        const response = opts.response ?? 0.32;
        const { stiffness, dampingC } = springParams(damping, response);

        if (reduced.matches) {
            el.style.transform = 'translateX(' + target + 'px)';
            opts.onDone && opts.onDone();
            return;
        }

        let s = state.get(el);
        if (!s || s.raf === null) {
            s = { x: readTranslateX(el), v: 0 };
        }
        if (s.raf) cancelAnimationFrame(s.raf);

        let prev = performance.now();
        function frame(now) {
            const dt = Math.min((now - prev) / 1000, 1 / 30);
            prev = now;
            const a = -stiffness * (s.x - target) - dampingC * s.v;
            s.v += a * dt;
            s.x += s.v * dt;
            el.style.transform = 'translateX(' + s.x + 'px)';
            if (Math.abs(s.x - target) < 0.3 && Math.abs(s.v) < 0.5) {
                el.style.transform = 'translateX(' + target + 'px)';
                s.raf = null;
                opts.onDone && opts.onDone();
                return;
            }
            s.raf = requestAnimationFrame(frame);
        }
        s.raf = requestAnimationFrame(frame);
        state.set(el, s);
    }

    // --- Mobile drawer (interruptible spring slide) -------------------------
    // The #app-nav element is the same element as the desktop row; on mobile
    // it becomes a fixed drawer parked off-screen (translateX(-width)). We
    // spring the transform between closed (-width px) and open (0px).
    function drawer(open, width = 280) {
        const el = document.getElementById('app-nav');
        if (!el) return;
        const target = open ? 0 : -width;
        springX(el, target, { damping: 1.0, response: 0.32 });
        el.classList.toggle('open', open);
        // Overlay
        const overlay = el.parentElement && el.parentElement.querySelector('.mobile-overlay');
        if (overlay) overlay.classList.toggle('show', open);
    }

    // --- Theme (light/dark) ----------------------------------------------
    // "light" | "dark" | "system". An explicit user choice is persisted in
    // localStorage (elib-theme). While "system" (default), the <html>
    // data-theme attribute follows prefers-color-scheme; a manual choice pins
    // it. Setting data-theme on <html> is read by the CSS variables in app.css
    // and by the default <meta name="theme-color"> overridden in the head.
    const THEME_KEY = 'elib-theme';
    function currentSystemDark() {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
    function applyTheme(pref) {
        const theme = pref === 'light' || pref === 'dark' ? pref : (currentSystemDark() ? 'dark' : 'light');
        const root = document.documentElement;
        root.setAttribute('data-theme', theme);
        // Keep native controls (scrollbars, inputs) matching the palette.
        root.style.colorScheme = theme;
    }
    function initTheme() {
        const saved = (() => { try { return localStorage.getItem(THEME_KEY); } catch (e) { return null; } })();
        applyTheme(saved === 'light' || saved === 'dark' ? saved : 'system');
        if (saved === 'light' || saved === 'dark') return; // pinned by the user
        // No explicit choice → follow the system, live.
        const mq = window.matchMedia('(prefers-color-scheme: dark)');
        const onChange = () => applyTheme('system');
        if (mq.addEventListener) mq.addEventListener('change', onChange);
        else mq.addListener(onChange);
    }
    // Called by Blazor (MainLayout).
    function setThemeMode(mode) {
        if (mode === 'light' || mode === 'dark') {
            try { localStorage.setItem(THEME_KEY, mode); } catch (e) {}
            applyTheme(mode);
        } else {
            try { localStorage.removeItem(THEME_KEY); } catch (e) {}
            applyTheme('system');
        }
    }
    // Let Blazor query the current effective theme if needed.
    function getThemeMode() { return document.documentElement.getAttribute('data-theme'); }

    window.elibMotion = { go, drawer, springX, setThemeMode, getThemeMode };
    initTheme();
})();
