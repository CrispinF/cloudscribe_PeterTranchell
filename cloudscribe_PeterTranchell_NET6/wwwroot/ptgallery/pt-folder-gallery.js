/* Folder-based image gallery: lazy thumbnail grid plus an accessible, zoomable,
   full-screen-capable image viewer. No dependencies. */
(function () {
    'use strict';

    var MIN_SCALE = 0.05;
    var MAX_SCALE = 12;
    var ZOOM_STEP = 1.4;
    /* An Esc that leaves full screen must not also close the viewer, but the
       browser's own Esc handling is inconsistent, so ignore close requests
       arriving immediately after a full screen exit. */
    var FULLSCREEN_EXIT_GRACE_MS = 400;

    var FOCUSABLE = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(', ');

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function currentFullscreenElement() {
        return document.fullscreenElement || document.webkitFullscreenElement || null;
    }

    function requestFullscreen(element) {
        var fn = element.requestFullscreen || element.webkitRequestFullscreen
            || element.mozRequestFullScreen || element.msRequestFullscreen;
        if (!fn) { return; }
        try {
            var result = fn.call(element);
            if (result && typeof result.catch === 'function') { result.catch(noop); }
        } catch (e) { /* full screen unavailable */ }
    }

    function exitFullscreen() {
        var fn = document.exitFullscreen || document.webkitExitFullscreen
            || document.mozCancelFullScreen || document.msExitFullscreen;
        if (!fn) { return; }
        try {
            var result = fn.call(document);
            if (result && typeof result.catch === 'function') { result.catch(noop); }
        } catch (e) { /* full screen unavailable */ }
    }

    function noop() { }

    /* ------------------------------------------------------------------ */

    function GalleryViewer(root) {
        this.root = root;
        this.viewer = root.querySelector('[data-pt-gallery-viewer]');
        this.dialog = root.querySelector('[data-pt-gallery-dialog]');
        this.stage = root.querySelector('[data-pt-gallery-stage]');
        this.canvas = root.querySelector('[data-pt-gallery-canvas]');
        this.image = root.querySelector('[data-pt-gallery-image]');
        this.caption = root.querySelector('[data-pt-gallery-caption]');
        this.positionLabel = root.querySelector('[data-pt-gallery-position]');
        this.zoomLevelLabel = root.querySelector('[data-pt-gallery-zoom-level]');
        this.fullscreenLabel = root.querySelector('[data-pt-gallery-fullscreen] .visually-hidden');
        this.prevButton = root.querySelector('[data-pt-gallery-prev]');
        this.nextButton = root.querySelector('[data-pt-gallery-next]');

        this.openers = Array.prototype.slice.call(root.querySelectorAll('[data-pt-gallery-open]'));
        this.items = this.openers
            .map(function (opener) {
                var img = opener.querySelector('img');
                if (!img) { return null; }
                // The grid shows a generated thumbnail; the viewer opens the
                // original from the companion attribute, falling back to the
                // thumbnail when no separate full-size image is offered.
                var full = img.getAttribute('data-pt-gallery-full');
                return {
                    src: full || img.getAttribute('src'),
                    alt: img.getAttribute('alt') || ''
                };
            })
            .filter(Boolean)
            .filter(function (item) { return !!item.src; });

        this.index = -1;
        this.scale = 1;
        this.naturalWidth = 0;
        this.naturalHeight = 0;
        this.isOpen = false;
        this.lastFocused = null;
        this.wasFullscreen = false;
        this.fullscreenExitedAt = 0;
        this.hiddenElements = [];

        if (!this.items.length) { return; }

        this.bind();
    }

    GalleryViewer.prototype.bind = function () {
        var self = this;

        this.openers.forEach(function (opener, index) {
            opener.addEventListener('click', function () { self.open(index); });
        });

        this.onClick('[data-pt-gallery-close]', function () { self.close(); });
        this.onClick('[data-pt-gallery-backdrop]', function () { self.close(); });
        this.onClick('[data-pt-gallery-prev]', function () { self.go(-1); });
        this.onClick('[data-pt-gallery-next]', function () { self.go(1); });
        this.onClick('[data-pt-gallery-zoom-in]', function () { self.zoomBy(ZOOM_STEP); });
        this.onClick('[data-pt-gallery-zoom-out]', function () { self.zoomBy(1 / ZOOM_STEP); });
        this.onClick('[data-pt-gallery-zoom-reset]', function () { self.fit(); });
        this.onClick('[data-pt-gallery-fullscreen]', function () { self.toggleFullscreen(); });

        this.image.addEventListener('load', function () {
            self.naturalWidth = self.image.naturalWidth || 0;
            self.naturalHeight = self.image.naturalHeight || 0;
            self.fit();
        });

        document.addEventListener('fullscreenchange', function () { self.onFullscreenChange(); });
        document.addEventListener('webkitfullscreenchange', function () { self.onFullscreenChange(); });
        document.addEventListener('keydown', function (e) { self.onKeydown(e); });

        this.stage.addEventListener('wheel', function (e) {
            if (!e.ctrlKey) { return; }
            e.preventDefault();
            self.zoomAt(e.clientX, e.clientY, e.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP);
        }, { passive: false });

        this.stage.addEventListener('dblclick', function () {
            if (self.isZoomed()) { self.fit(); } else { self.zoomBy(2); }
        });

        this.enableDragToPan();
    };

    GalleryViewer.prototype.onClick = function (selector, handler) {
        var el = this.root.querySelector(selector);
        if (el) { el.addEventListener('click', handler); }
    };

    /* ---------------- open / close ---------------- */

    GalleryViewer.prototype.open = function (index) {
        if (index < 0 || index >= this.items.length) { return; }

        this.lastFocused = this.openers[index] || null;
        this.index = index;
        this.isOpen = true;

        this.viewer.hidden = false;
        this.viewer.classList.add('is-open');
        document.documentElement.classList.add('pt-viewer-open');
        this.hidePageBehind();

        this.render();

        var target = this.root.querySelector('[data-pt-gallery-close]') || this.dialog;
        if (target && target.focus) { target.focus(); }
    };

    GalleryViewer.prototype.close = function () {
        if (!this.isOpen) { return; }
        this.isOpen = false;

        if (currentFullscreenElement()) { exitFullscreen(); }

        this.viewer.classList.remove('is-open');
        this.viewer.hidden = true;
        document.documentElement.classList.remove('pt-viewer-open');
        this.showPageBehind();
        this.image.removeAttribute('src');

        var opener = this.lastFocused;
        this.lastFocused = null;
        if (opener && document.contains(opener)) {
            if (opener.focus) { opener.focus(); }
            if (opener.scrollIntoView) { opener.scrollIntoView({ block: 'nearest' }); }
        }
    };

    GalleryViewer.prototype.go = function (delta) {
        var next = this.index + delta;
        if (next < 0 || next >= this.items.length) { return; }
        this.index = next;
        this.render();
    };

    GalleryViewer.prototype.render = function () {
        var item = this.items[this.index];

        if (this.image.getAttribute('src') !== item.src) {
            this.image.setAttribute('src', item.src);
        }
        this.caption.textContent = item.alt;

        this.positionLabel.textContent = (this.index + 1) + ' of ' + this.items.length;
        this.setDisabled(this.prevButton, this.index === 0);
        this.setDisabled(this.nextButton, this.index === this.items.length - 1);

        this.applyScale(this.fitScale());
    };

    GalleryViewer.prototype.setDisabled = function (button, disabled) {
        if (!button) { return; }
        button.disabled = disabled;
        button.setAttribute('aria-disabled', disabled ? 'true' : 'false');
    };

    /* ---------------- zoom ---------------- */

    GalleryViewer.prototype.isZoomed = function () {
        return this.scale > this.fitScale() + 0.001;
    };

    GalleryViewer.prototype.availableSpace = function () {
        var styles = window.getComputedStyle(this.stage);
        return {
            width: Math.max(this.stage.clientWidth
                - (parseFloat(styles.paddingLeft) || 0)
                - (parseFloat(styles.paddingRight) || 0), 1),
            height: Math.max(this.stage.clientHeight
                - (parseFloat(styles.paddingTop) || 0)
                - (parseFloat(styles.paddingBottom) || 0), 1)
        };
    };

    GalleryViewer.prototype.fitScale = function () {
        if (!this.naturalWidth || !this.naturalHeight) { return 1; }
        var space = this.availableSpace();
        return Math.min(space.width / this.naturalWidth, space.height / this.naturalHeight, 1);
    };

    GalleryViewer.prototype.fit = function () {
        this.applyScale(this.fitScale());
    };

    GalleryViewer.prototype.zoomBy = function (factor) {
        var rect = this.stage.getBoundingClientRect();
        this.zoomAt(rect.left + rect.width / 2, rect.top + rect.height / 2, factor);
    };

    /* Scale by `factor`, keeping the viewport point under (clientX, clientY) anchored. */
    GalleryViewer.prototype.zoomAt = function (clientX, clientY, factor) {
        var previous = this.scale;
        var next = clamp(previous * factor, MIN_SCALE, MAX_SCALE);
        if (next === previous) { return; }

        var rect = this.stage.getBoundingClientRect();
        var offsetX = clientX - rect.left;
        var offsetY = clientY - rect.top;
        var anchorX = this.stage.scrollLeft + offsetX;
        var anchorY = this.stage.scrollTop + offsetY;

        this.applyScale(next);

        this.stage.scrollLeft = anchorX * (next / previous) - offsetX;
        this.stage.scrollTop = anchorY * (next / previous) - offsetY;
    };

    GalleryViewer.prototype.applyScale = function (scale) {
        this.scale = clamp(scale, MIN_SCALE, MAX_SCALE);

        if (this.naturalWidth && this.naturalHeight) {
            this.canvas.style.width = Math.round(this.naturalWidth * this.scale) + 'px';
            this.canvas.style.height = Math.round(this.naturalHeight * this.scale) + 'px';
        }

        if (this.zoomLevelLabel) {
            this.zoomLevelLabel.textContent = Math.round(this.scale * 100) + '%';
        }
    };

    GalleryViewer.prototype.enableDragToPan = function () {
        var self = this;
        var dragging = false;
        var startX = 0;
        var startY = 0;
        var startScrollLeft = 0;
        var startScrollTop = 0;

        this.stage.addEventListener('pointerdown', function (e) {
            if (e.button !== 0 || !self.isZoomed()) { return; }
            dragging = true;
            startX = e.clientX;
            startY = e.clientY;
            startScrollLeft = self.stage.scrollLeft;
            startScrollTop = self.stage.scrollTop;
            self.stage.classList.add('is-panning');
            if (self.stage.setPointerCapture) {
                try { self.stage.setPointerCapture(e.pointerId); } catch (err) { /* ignore */ }
            }
        });

        this.stage.addEventListener('pointermove', function (e) {
            if (!dragging) { return; }
            e.preventDefault();
            self.stage.scrollLeft = startScrollLeft - (e.clientX - startX);
            self.stage.scrollTop = startScrollTop - (e.clientY - startY);
        });

        var endDrag = function (e) {
            if (!dragging) { return; }
            dragging = false;
            self.stage.classList.remove('is-panning');
            if (self.stage.releasePointerCapture && e.pointerId !== undefined) {
                try { self.stage.releasePointerCapture(e.pointerId); } catch (err) { /* ignore */ }
            }
        };

        this.stage.addEventListener('pointerup', endDrag);
        this.stage.addEventListener('pointercancel', endDrag);
    };

    /* ---------------- full screen ---------------- */

    GalleryViewer.prototype.toggleFullscreen = function () {
        if (currentFullscreenElement()) {
            exitFullscreen();
        } else {
            requestFullscreen(this.viewer);
        }
    };

    GalleryViewer.prototype.onFullscreenChange = function () {
        var now = !!currentFullscreenElement();

        this.viewer.classList.toggle('is-fullscreen', now);
        if (!now && this.wasFullscreen) { this.fullscreenExitedAt = Date.now(); }
        this.wasFullscreen = now;

        if (this.fullscreenLabel) {
            this.fullscreenLabel.textContent = now ? 'Exit full screen' : 'Enter full screen';
        }

        if (this.isOpen) { this.applyScale(this.scale); }
    };

    /* ---------------- keyboard and focus ---------------- */

    GalleryViewer.prototype.onKeydown = function (e) {
        if (!this.isOpen) { return; }

        switch (e.key) {
            case 'Escape':
                if (currentFullscreenElement()) {
                    e.preventDefault();
                    exitFullscreen();
                } else if (Date.now() - this.fullscreenExitedAt >= FULLSCREEN_EXIT_GRACE_MS) {
                    e.preventDefault();
                    this.close();
                }
                return;
            case 'ArrowLeft':
                e.preventDefault();
                this.go(-1);
                return;
            case 'ArrowRight':
                e.preventDefault();
                this.go(1);
                return;
            case '+':
            case '=':
                e.preventDefault();
                this.zoomBy(ZOOM_STEP);
                return;
            case '-':
            case '_':
                e.preventDefault();
                this.zoomBy(1 / ZOOM_STEP);
                return;
            case '0':
                e.preventDefault();
                this.fit();
                return;
            case 'f':
            case 'F':
                e.preventDefault();
                this.toggleFullscreen();
                return;
            case 'Tab':
                this.trapFocus(e);
                return;
            default:
        }
    };

    GalleryViewer.prototype.trapFocus = function (e) {
        var focusable = Array.prototype.filter.call(
            this.dialog.querySelectorAll(FOCUSABLE),
            function (el) { return !el.disabled && el.offsetParent !== null; }
        );

        if (!focusable.length) {
            e.preventDefault();
            return;
        }

        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        var active = document.activeElement;

        if (e.shiftKey && (active === first || !this.dialog.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && active === last) {
            e.preventDefault();
            first.focus();
        }
    };

    // A content template renders deep inside the site layout (usually within
    // <main>), so hiding the direct children of <body> would leave the whole
    // page exposed to assistive technology. Walk up from the viewer and hide
    // every sibling on the way, recording each element's previous aria-hidden
    // value so it can be restored exactly.
    GalleryViewer.prototype.hidePageBehind = function () {
        var node = this.viewer;
        while (node && node !== document.body) {
            var parent = node.parentNode;
            if (!parent || !parent.children) { break; }
            var children = parent.children;
            for (var i = 0; i < children.length; i++) {
                var sibling = children[i];
                if (sibling === node || sibling.tagName === 'SCRIPT') { continue; }
                if (sibling.getAttribute('aria-hidden') === 'true') { continue; }
                this.hiddenElements.push({ el: sibling, previous: sibling.getAttribute('aria-hidden') });
                sibling.setAttribute('aria-hidden', 'true');
            }
            node = parent;
        }
    };

    GalleryViewer.prototype.showPageBehind = function () {
        this.hiddenElements.forEach(function (entry) {
            if (entry.previous === null) { entry.el.removeAttribute('aria-hidden'); }
            else { entry.el.setAttribute('aria-hidden', entry.previous); }
        });
        this.hiddenElements = [];
    };

    /* ------------------------------------------------------------------ */

    function init() {
        var roots = document.querySelectorAll('[data-pt-gallery-root]');
        Array.prototype.forEach.call(roots, function (root) {
            if (root.getAttribute('data-pt-gallery-ready') === 'true') { return; }
            root.setAttribute('data-pt-gallery-ready', 'true');
            /* eslint-disable no-new */
            new GalleryViewer(root);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
