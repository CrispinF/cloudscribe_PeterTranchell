(function () {
    'use strict';

    var STORAGE_KEY = 'pt-chat-transcript';
    var OPEN_KEY = 'pt-chat-open';
    var MAX_STORED = 40;
    var MAX_SENT = 8;

    var panel = document.getElementById('ptChatPanel');
    if (!panel) return;

    var apiUrl = '/api/chat';
    var messagesEl = document.getElementById('pt-chat-messages');
    var form = document.getElementById('pt-chat-form');
    var input = document.getElementById('pt-chat-input');
    var sendButton = document.getElementById('pt-chat-send');
    var clearButton = document.getElementById('pt-chat-clear');

    var transcript = [];
    var busy = false;
    var recaptchaPromise = null;
    var offcanvas = null;

    function getOffcanvas() {
        if (!offcanvas && window.bootstrap && window.bootstrap.Offcanvas) {
            offcanvas = window.bootstrap.Offcanvas.getOrCreateInstance(panel);
        }
        return offcanvas;
    }

    function loadTranscript() {
        try {
            var raw = sessionStorage.getItem(STORAGE_KEY);
            if (raw) transcript = JSON.parse(raw) || [];
        } catch (e) { transcript = []; }
        if (!Array.isArray(transcript)) transcript = [];
    }

    function saveTranscript() {
        try {
            if (transcript.length > MAX_STORED) transcript = transcript.slice(-MAX_STORED);
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(transcript));
        } catch (e) { /* storage unavailable */ }
    }

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function safeUrl(url) {
        if (/^https?:\/\//i.test(url)) return url;
        if (/^\//.test(url)) return url;
        return null;
    }

    function renderMarkdown(text) {
        var html = escapeHtml(text);

        html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+|\/[^)\s]*)\)/g, function (m, label, url) {
            return '<a href="' + url + '">' + label + '</a>';
        });

        html = html.replace(/(^|[\s(])((?:https?:\/\/)[^\s<)]+)/g, function (m, pre, url) {
            return pre + '<a href="' + url + '">' + url + '</a>';
        });

        html = html.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
        html = html.replace(/(^|[^*])\*([^*\n]+)\*(?!\w)/g, '$1<em>$2</em>');
        html = html.replace(/\n/g, '<br>');
        return html;
    }

    function appendMessage(role, content, isError) {
        var wrapper = document.createElement('div');
        wrapper.className = 'pt-chat-msg pt-chat-msg-' + role + (isError ? ' pt-chat-error' : '');
        wrapper.innerHTML = renderMarkdown(content);
        messagesEl.appendChild(wrapper);
        messagesEl.scrollTop = messagesEl.scrollHeight;
        return wrapper;
    }

    function appendSources(sources) {
        if (!sources || !sources.length) return;
        var el = document.createElement('div');
        el.className = 'pt-chat-sources';
        var links = [];
        for (var i = 0; i < sources.length; i++) {
            var s = sources[i];
            var url = safeUrl(s.url);
            if (!url) continue;
            links.push('<a href="' + url + '">' + escapeHtml(s.title || s.url) + '</a>');
        }
        if (!links.length) return;
        el.innerHTML = 'Sources: ' + links.join(' &middot; ');
        messagesEl.appendChild(el);
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    function setBusy(state) {
        busy = state;
        sendButton.disabled = state;
        input.disabled = state;
        sendButton.innerHTML = state
            ? '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span>'
            : '<span class="fas fa-paper-plane" aria-hidden="true"></span>';
    }

    function loadRecaptchaScript(url) {
        // grecaptcha already available - nothing to load
        if (typeof window.grecaptcha !== 'undefined') {
            return Promise.resolve();
        }
        if (!recaptchaPromise) {
            var existing = document.querySelector('script[src^="https://www.google.com/recaptcha/api.js"]');
            if (existing) {
                // api.js is already on the page (script tag in <head>) - never
                // inject a second copy (double execution orphans widgets).
                // Wait for it to define window.grecaptcha instead.
                recaptchaPromise = new Promise(function (resolve, reject) {
                    var waited = 0;
                    var check = function () {
                        if (typeof window.grecaptcha !== 'undefined') {
                            resolve();
                            return;
                        }
                        if (waited >= 5000) {
                            reject(new Error('grecaptcha not available'));
                            return;
                        }
                        waited += 100;
                        setTimeout(check, 100);
                    };
                    check();
                });
            } else {
                recaptchaPromise = new Promise(function (resolve, reject) {
                    var script = document.createElement('script');
                    script.src = url;
                    script.async = true;
                    script.onload = function () { resolve(); };
                    script.onerror = function () { reject(new Error('recaptcha script load failed')); };
                    document.head.appendChild(script);
                });
            }
            // If loading fails, allow a later send to retry from scratch
            recaptchaPromise.catch(function () {
                recaptchaPromise = null;
            });
        }
        return recaptchaPromise;
    }

    var v2WidgetId = null;

    function getV2Token(siteKey) {
        // Discard a widget whose grecaptcha instance has been replaced
        // (e.g. api.js executed twice) and start over with a fresh render.
        function recoverWidget() {
            var holder = document.getElementById('pt-recaptcha-v2');
            if (holder) holder.innerHTML = '';
            v2WidgetId = null;
        }

        function doTokenFlow(resolve, isRetry) {
            try {
                if (v2WidgetId === null) {
                    var holder = document.getElementById('pt-recaptcha-v2');
                    if (holder) {
                        v2WidgetId = grecaptcha.render(holder, {
                            sitekey: siteKey,
                            size: 'invisible'
                        });
                    }
                }
                if (v2WidgetId === null) {
                    resolve('');
                    return;
                }

                try {
                    grecaptcha.reset(v2WidgetId);
                } catch (e) {
                    // Orphaned widget id - re-render once, then give up
                    if (!isRetry) {
                        recoverWidget();
                        doTokenFlow(resolve, true);
                        return;
                    }
                    resolve('');
                    return;
                }

                var done = false;
                var finish = function (token) {
                    if (done) return;
                    done = true;
                    resolve(token || '');
                };

                // Path A: native promise from execute()
                var promise;
                try {
                    promise = grecaptcha.execute(v2WidgetId);
                } catch (e) {
                    if (!isRetry) {
                        recoverWidget();
                        doTokenFlow(resolve, true);
                        return;
                    }
                    resolve('');
                    return;
                }
                if (promise && typeof promise.then === 'function') {
                    promise.then(function (token) {
                        if (token) finish(token);
                    }).catch(function () {});
                }

                // Path B: getResponse polling (60s timeout for visual challenges)
                var start = Date.now();
                var poll = function () {
                    if (done) return;
                    try {
                        var token = grecaptcha.getResponse(v2WidgetId);
                        if (token) {
                            finish(token);
                            return;
                        }
                        if (Date.now() - start > 60000) {
                            finish('');
                            return;
                        }
                    } catch (e) {
                        // getResponse threw (invalid widget) - fail this send
                        finish('');
                        return;
                    }
                    setTimeout(poll, 100);
                };
                poll();
            } catch (e) {
                resolve('');
            }
        }

        // Wait for grecaptcha.ready(), but never hang if it fails to fire:
        // proceed after 3s regardless. doTokenFlow is defensive on its own.
        function whenReady(resolve) {
            var started = false;
            var go = function () {
                if (started) return;
                started = true;
                doTokenFlow(resolve, false);
            };
            var safety = setTimeout(go, 3000);
            try {
                grecaptcha.ready(function () {
                    clearTimeout(safety);
                    go();
                });
            } catch (e) {
                clearTimeout(safety);
                go();
            }
        }

        return new Promise(function (resolve) {
            loadRecaptchaScript('https://www.google.com/recaptcha/api.js')
                .then(function () { whenReady(resolve); })
                .catch(function () { resolve(''); });
        });
    }

    function getV3Token(siteKey) {
        return loadRecaptchaScript('https://www.google.com/recaptcha/api.js?render=' + siteKey)
            .then(function () {
                return window.grecaptcha.execute(siteKey, { action: 'chat' });
            })
            .catch(function () { return ''; });
    }

    function ensureRecaptcha(siteKey, mode) {
        if (!siteKey) {
            return Promise.resolve('');
        }
        // getV2Token/getV3Token handle script loading themselves
        return mode === 'v2' ? getV2Token(siteKey) : getV3Token(siteKey);
    }

    function sendMessage(question) {
        appendMessage('user', question);
        transcript.push({ role: 'user', content: question });

        setBusy(true);
        var thinking = appendMessage('assistant', 'Thinking...');

        var payload = {
            messages: transcript.slice(-MAX_SENT).map(function (m) {
                return { role: m.role, content: m.content };
            }),
            captchaToken: ''
        };

        var siteKey = panel.getAttribute('data-recaptcha-sitekey') || '';
        var recaptchaMode = panel.getAttribute('data-recaptcha-mode') || '';
        ensureRecaptcha(siteKey, recaptchaMode).then(function (token) {
            payload.captchaToken = token || '';
            return fetch(apiUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
        }).then(function (response) {
            return response.json().then(function (data) {
                return { ok: response.ok, status: response.status, data: data };
            });
        }).then(function (result) {
            thinking.remove();
            var reply;
            if (result.ok && result.data && result.data.reply) {
                reply = result.data.reply;
                appendMessage('assistant', reply);
                appendSources(result.data.sources);
                transcript.push({ role: 'assistant', content: reply });
            } else {
                reply = (result.data && result.data.error) ||
                    'Sorry, something went wrong. Please try again in a moment.';
                appendMessage('assistant', reply, true);
            }
            saveTranscript();
            setBusy(false);
            input.focus();
        }).catch(function () {
            thinking.remove();
            appendMessage('assistant', 'Sorry, I could not reach the server. Please check your connection and try again.', true);
            setBusy(false);
            input.focus();
        });
    }

    function renderWelcome() {
        messagesEl.innerHTML = '';
        var welcome = "Hello! I can help you find your way around this site about the composer " +
            "Peter Tranchell - his music, writings and life. Try asking me something like " +
            "\"Did Tranchell write any carols?\"";
        appendMessage('assistant', welcome);
    }

    function restoreMessages() {
        renderWelcome();
        for (var i = 0; i < transcript.length; i++) {
            appendMessage(transcript[i].role, transcript[i].content, false);
        }
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        if (busy) return;
        var question = (input.value || '').trim();
        if (!question) return;
        input.value = '';
        sendMessage(question);
    });

    clearButton.addEventListener('click', function () {
        transcript = [];
        saveTranscript();
        renderWelcome();
    });

    document.getElementById('pt-chat-open-btn').addEventListener('click', function () {
        var oc = getOffcanvas();
        if (oc) oc.show();
    });

    panel.addEventListener('shown.bs.offcanvas', function () {
        try { sessionStorage.setItem(OPEN_KEY, '1'); } catch (e) {}
        input.focus();
    });

    panel.addEventListener('hidden.bs.offcanvas', function () {
        try { sessionStorage.removeItem(OPEN_KEY); } catch (e) {}
    });

    loadTranscript();
    restoreMessages();

    document.addEventListener('DOMContentLoaded', function () {
        var openFlag = null;
        try { openFlag = sessionStorage.getItem(OPEN_KEY); } catch (e) {}
        if (openFlag === '1') {
            var oc = getOffcanvas();
            if (oc) oc.show();
        }
    });
})();
