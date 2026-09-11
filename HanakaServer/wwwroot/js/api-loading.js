(function (window, document) {
    "use strict";

    if (window.HanakaApiLoading) {
        window.HanakaApiLoading.installAxios(window.axios);
        return;
    }

    // Keep short requests invisible. Showing a blocking layer for a request that
    // is already about to finish feels like a flash rather than useful feedback.
    const SHOW_DELAY_MS = 320;
    const MIN_VISIBLE_MS = 220;
    const SECTION_SHOW_DELAY_MS = 180;
    const SECTION_MIN_VISIBLE_MS = 180;
    const SLOW_REQUEST_MS = 8000;
    const AXIOS_TOKEN_KEY = "__hanakaApiLoadingToken";
    const entries = new Map();
    const sectionStates = new WeakMap();
    const buttonStates = new WeakMap();
    const triggerRects = new WeakMap();
    let sequence = 0;
    let currentTrigger = null;

    const globalState = {
        count: 0,
        element: null,
        messageElement: null,
        hintElement: null,
        showTimer: 0,
        hideTimer: 0,
        slowTimer: 0,
        shownAt: 0,
        messages: new Map()
    };

    function defaultMessage(method) {
        const normalized = String(method || "GET").toUpperCase();
        if (normalized === "GET" || normalized === "HEAD") return "Đang tải dữ liệu...";
        if (normalized === "DELETE") return "Đang xóa dữ liệu...";
        return "Đang xử lý...";
    }

    function normalizeOptions(value, method) {
        if (value === false || value === "silent") {
            return { mode: "silent", message: defaultMessage(method) };
        }

        const source = typeof value === "string"
            ? { message: value }
            : (value && typeof value === "object" ? value : {});
        const target = typeof source.target === "string"
            ? document.querySelector(source.target)
            : source.target;
        const trigger = source.button || currentTrigger;
        const hasExplicitMode = Object.prototype.hasOwnProperty.call(source, "mode");
        const requestedMode = String(hasExplicitMode ? source.mode : (trigger ? "button" : "global")).toLowerCase();
        let mode = requestedMode === "silent" || requestedMode === "none"
            ? "silent"
            : (requestedMode === "section" || requestedMode === "button" ? requestedMode : "global");

        // A button loader without a concrete button would otherwise provide no
        // visual feedback. Fall back to the global layer in that edge case.
        if (mode === "button" && !(trigger && trigger.nodeType === 1)) {
            mode = "global";
        }

        return {
            mode: mode,
            message: String(source.message || defaultMessage(method)),
            slowMessage: String(source.slowMessage || "Hệ thống đang xử lý lâu hơn dự kiến..."),
            hint: String(source.hint || "Vui lòng chờ trong giây lát"),
            target: target && target.nodeType === 1 ? target : null,
            button: trigger && trigger.nodeType === 1 ? trigger : null
        };
    }

    function createGlobalOverlay() {
        if (globalState.element && globalState.element.isConnected) return globalState.element;

        const overlay = document.createElement("div");
        overlay.className = "hanaka-api-overlay";
        overlay.hidden = true;
        overlay.setAttribute("aria-hidden", "true");
        overlay.innerHTML = [
            '<div class="hanaka-api-overlay__panel" role="status" aria-live="polite" aria-atomic="true">',
            '  <span class="hanaka-api-overlay__spinner" aria-hidden="true"></span>',
            '  <span class="hanaka-api-overlay__content">',
            '    <strong class="hanaka-api-overlay__message">Đang xử lý...</strong>',
            '    <small class="hanaka-api-overlay__hint">Vui lòng chờ trong giây lát</small>',
            "  </span>",
            "</div>"
        ].join("");

        (document.body || document.documentElement).appendChild(overlay);
        globalState.element = overlay;
        globalState.messageElement = overlay.querySelector(".hanaka-api-overlay__message");
        globalState.hintElement = overlay.querySelector(".hanaka-api-overlay__hint");
        return overlay;
    }

    function latestGlobalEntry() {
        const values = Array.from(globalState.messages.values());
        return values.length ? values[values.length - 1] : null;
    }

    function renderGlobalMessage(slow) {
        const active = latestGlobalEntry();
        if (!active) return;
        const overlay = createGlobalOverlay();
        const message = slow ? active.slowMessage : active.message;
        globalState.messageElement.textContent = message;
        globalState.hintElement.textContent = active.hint;
        overlay.setAttribute("aria-label", message);
    }

    function showGlobal() {
        globalState.showTimer = 0;
        if (globalState.count <= 0) return;

        const overlay = createGlobalOverlay();
        renderGlobalMessage(false);
        overlay.hidden = false;
        overlay.setAttribute("aria-hidden", "false");
        document.documentElement.classList.add("hanaka-api-busy");
        document.documentElement.setAttribute("aria-busy", "true");
        globalState.shownAt = Date.now();
        globalState.slowTimer = window.setTimeout(function () {
            globalState.slowTimer = 0;
            if (globalState.count > 0) renderGlobalMessage(true);
        }, SLOW_REQUEST_MS);
    }

    function hideGlobalNow() {
        window.clearTimeout(globalState.showTimer);
        window.clearTimeout(globalState.hideTimer);
        window.clearTimeout(globalState.slowTimer);
        globalState.showTimer = 0;
        globalState.hideTimer = 0;
        globalState.slowTimer = 0;
        globalState.shownAt = 0;
        if (globalState.element) {
            globalState.element.hidden = true;
            globalState.element.setAttribute("aria-hidden", "true");
        }
        document.documentElement.classList.remove("hanaka-api-busy");
        document.documentElement.removeAttribute("aria-busy");
    }

    function queueGlobalHide() {
        if (globalState.count > 0) return;
        window.clearTimeout(globalState.showTimer);
        globalState.showTimer = 0;

        if (!globalState.shownAt) {
            hideGlobalNow();
            return;
        }

        const remaining = Math.max(0, MIN_VISIBLE_MS - (Date.now() - globalState.shownAt));
        window.clearTimeout(globalState.hideTimer);
        globalState.hideTimer = window.setTimeout(hideGlobalNow, remaining);
    }

    function startGlobal(token, options) {
        window.clearTimeout(globalState.hideTimer);
        globalState.hideTimer = 0;
        globalState.count += 1;
        globalState.messages.set(token.id, options);
        if (globalState.shownAt) {
            renderGlobalMessage(false);
        } else if (!globalState.showTimer) {
            globalState.showTimer = window.setTimeout(showGlobal, SHOW_DELAY_MS);
        }
    }

    function stopGlobal(token) {
        globalState.messages.delete(token.id);
        globalState.count = Math.max(0, globalState.count - 1);
        if (globalState.count > 0) renderGlobalMessage(false);
        else queueGlobalHide();
    }

    function createSectionOverlay(target) {
        const overlay = document.createElement("div");
        overlay.className = "hanaka-api-section-overlay";
        overlay.hidden = true;
        overlay.setAttribute("aria-hidden", "true");
        overlay.innerHTML = [
            '<div class="hanaka-api-section-overlay__panel" role="status" aria-live="polite">',
            '  <span class="hanaka-api-section-overlay__spinner" aria-hidden="true"></span>',
            '  <span class="hanaka-api-section-overlay__content">',
            '    <strong class="hanaka-api-section-overlay__message">Đang tải dữ liệu...</strong>',
            '    <small class="hanaka-api-section-overlay__hint">Vui lòng chờ trong giây lát</small>',
            "  </span>",
            "</div>"
        ].join("");
        target.classList.add("hanaka-api-section-host");
        target.appendChild(overlay);
        return overlay;
    }

    function startSection(token, options) {
        if (!options.target) {
            token.mode = "global";
            startGlobal(token, options);
            return;
        }

        let state = sectionStates.get(options.target);
        if (!state) {
            const overlay = createSectionOverlay(options.target);
            state = {
                count: 0,
                overlay: overlay,
                message: overlay.querySelector(".hanaka-api-section-overlay__message"),
                showTimer: 0,
                hideTimer: 0,
                shownAt: 0
            };
            sectionStates.set(options.target, state);
        }

        window.clearTimeout(state.hideTimer);
        state.hideTimer = 0;
        state.count += 1;
        state.message.textContent = options.message;
        if (state.shownAt) {
            return;
        }

        if (!state.showTimer) {
            state.showTimer = window.setTimeout(function () {
                state.showTimer = 0;
                if (state.count <= 0) {
                    return;
                }

                state.overlay.hidden = false;
                state.overlay.setAttribute("aria-hidden", "false");
                options.target.setAttribute("aria-busy", "true");
                state.shownAt = Date.now();
            }, SECTION_SHOW_DELAY_MS);
        }
    }

    function stopSection(options) {
        if (!options.target) return;
        const state = sectionStates.get(options.target);
        if (!state) return;
        state.count = Math.max(0, state.count - 1);
        if (state.count > 0) return;

        window.clearTimeout(state.showTimer);
        state.showTimer = 0;
        const hide = function () {
            state.hideTimer = 0;
            state.shownAt = 0;
            state.overlay.hidden = true;
            state.overlay.setAttribute("aria-hidden", "true");
            options.target.removeAttribute("aria-busy");
        };

        if (!state.shownAt) {
            hide();
            return;
        }

        const remaining = Math.max(0, SECTION_MIN_VISIBLE_MS - (Date.now() - state.shownAt));
        window.clearTimeout(state.hideTimer);
        state.hideTimer = window.setTimeout(hide, remaining);
    }

    function startButton(options) {
        const button = options.button;
        if (!button || typeof button.disabled === "undefined") return;
        let state = buttonStates.get(button);
        if (!state) {
            const rect = triggerRects.get(button) || (typeof button.getBoundingClientRect === "function"
                ? button.getBoundingClientRect()
                : null);
            state = {
                count: 0,
                disabled: Boolean(button.disabled),
                ariaBusy: button.getAttribute("aria-busy"),
                inlineWidth: button.style ? button.style.width : "",
                inlineHeight: button.style ? button.style.height : "",
                inlineSpinnerColor: button.style
                    ? button.style.getPropertyValue("--hanaka-api-button-spinner-color")
                    : ""
            };
            buttonStates.set(button, state);

            // Freeze the original click-time box while the label/spinner state
            // changes. This prevents auto-width buttons from growing/shrinking.
            if (button.style && rect && rect.width > 0 && rect.height > 0) {
                button.style.width = `${rect.width}px`;
                button.style.height = `${rect.height}px`;
            }

            if (button.style && typeof window.getComputedStyle === "function") {
                const computedColor = window.getComputedStyle(button).color;
                if (computedColor) {
                    button.style.setProperty("--hanaka-api-button-spinner-color", computedColor);
                }
            }
        }
        state.count += 1;
        button.disabled = true;
        button.classList.add("hanaka-api-button-busy");
        button.setAttribute("aria-busy", "true");
    }

    function stopButton(options) {
        const button = options.button;
        if (!button) return;
        const state = buttonStates.get(button);
        if (!state) return;
        state.count = Math.max(0, state.count - 1);
        if (state.count > 0) return;
        button.disabled = state.disabled;
        button.classList.remove("hanaka-api-button-busy");
        if (button.style) {
            button.style.width = state.inlineWidth;
            button.style.height = state.inlineHeight;
            if (state.inlineSpinnerColor) {
                button.style.setProperty("--hanaka-api-button-spinner-color", state.inlineSpinnerColor);
            } else {
                button.style.removeProperty("--hanaka-api-button-spinner-color");
            }
        }
        if (state.ariaBusy === null) button.removeAttribute("aria-busy");
        else button.setAttribute("aria-busy", state.ariaBusy);
        buttonStates.delete(button);
    }

    function begin(value, method) {
        const options = normalizeOptions(value, method);
        const token = {
            id: ++sequence,
            mode: options.mode,
            options: options,
            ended: false
        };
        entries.set(token.id, token);

        if (options.mode !== "silent") {
            // One request owns one visual loading mode. In particular, a
            // global/section overlay must not also inject a spinner into the
            // button that happened to start the request.
            if (options.mode === "button") startButton(options);
            else if (options.mode === "section") startSection(token, options);
            else startGlobal(token, options);
        }

        document.dispatchEvent(new CustomEvent("hanaka:api-loading-start", {
            detail: { id: token.id, mode: token.mode, message: options.message }
        }));
        return token;
    }

    function end(token) {
        if (!token || token.ended) return;
        token.ended = true;
        entries.delete(token.id);

        if (token.mode !== "silent") {
            stopButton(token.options);
            if (token.mode === "global") stopGlobal(token);
            else if (token.mode === "section") stopSection(token.options);
        }

        document.dispatchEvent(new CustomEvent("hanaka:api-loading-end", {
            detail: { id: token.id, mode: token.mode }
        }));
    }

    function track(work, options, method) {
        const token = begin(options, method);
        let result;
        try {
            result = typeof work === "function" ? work() : work;
        } catch (error) {
            end(token);
            throw error;
        }
        return Promise.resolve(result).finally(function () { end(token); });
    }

    function reset() {
        Array.from(entries.values()).forEach(end);
        globalState.count = 0;
        globalState.messages.clear();
        hideGlobalNow();
    }

    const nativeFetch = typeof window.fetch === "function" ? window.fetch.bind(window) : null;
    if (nativeFetch && !window.fetch.__hanakaApiLoadingWrapped) {
        const wrappedFetch = function (input, init) {
            const sourceInit = init || {};
            const hasLoadingOption = Object.prototype.hasOwnProperty.call(sourceInit, "hanakaLoading");
            const loading = hasLoadingOption ? sourceInit.hanakaLoading : undefined;
            const requestInit = hasLoadingOption ? Object.assign({}, sourceInit) : sourceInit;
            if (hasLoadingOption) delete requestInit.hanakaLoading;
            const method = requestInit.method || (input && input.method) || "GET";
            return track(function () {
                return nativeFetch(input, requestInit);
            }, loading, method);
        };
        wrappedFetch.__hanakaApiLoadingWrapped = true;
        wrappedFetch.__hanakaNativeFetch = nativeFetch;
        window.fetch = wrappedFetch;
    }

    function installAxios(instance) {
        if (!instance || !instance.interceptors || instance.__hanakaApiLoadingInstalled) return false;
        Object.defineProperty(instance, "__hanakaApiLoadingInstalled", {
            configurable: false,
            enumerable: false,
            value: true
        });

        instance.interceptors.request.use(function (config) {
            try {
                config[AXIOS_TOKEN_KEY] = begin(config.hanakaLoading, config.method);
            } catch (_error) {
                // Loading must never prevent the underlying API request.
            }
            return config;
        });

        instance.interceptors.response.use(function (response) {
            end(response && response.config && response.config[AXIOS_TOKEN_KEY]);
            return response;
        }, function (error) {
            end(error && error.config && error.config[AXIOS_TOKEN_KEY]);
            return Promise.reject(error);
        });
        return true;
    }

    document.addEventListener("click", function (event) {
        const button = event.target && event.target.closest
            ? event.target.closest("button, input[type='submit'], input[type='button']")
            : null;
        if (!button) return;
        currentTrigger = button;
        if (typeof button.getBoundingClientRect === "function") {
            const rect = button.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                triggerRects.set(button, { width: rect.width, height: rect.height });
            }
        }
        window.setTimeout(function () {
            if (currentTrigger === button) currentTrigger = null;
            triggerRects.delete(button);
        }, 0);
    }, true);

    document.addEventListener("submit", function (event) {
        const button = event.submitter || (event.target && event.target.querySelector
            ? event.target.querySelector("button[type='submit'], input[type='submit']")
            : null);
        if (!button) return;
        currentTrigger = button;
        if (typeof button.getBoundingClientRect === "function") {
            const rect = button.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                triggerRects.set(button, { width: rect.width, height: rect.height });
            }
        }
        window.setTimeout(function () {
            if (currentTrigger === button) currentTrigger = null;
            triggerRects.delete(button);
        }, 0);
    }, true);

    window.addEventListener("pagehide", reset);
    window.addEventListener("pageshow", function (event) {
        if (event.persisted) reset();
    });

    const api = {
        begin: begin,
        end: end,
        track: track,
        reset: reset,
        installAxios: installAxios,
        get activeCount() { return entries.size; },
        get foregroundCount() { return globalState.count; }
    };
    window.HanakaApiLoading = api;
    installAxios(window.axios);
})(window, document);
