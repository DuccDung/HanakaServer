(function (window, document) {
    "use strict";
    const loading = window.HanakaApiLoading;
    if (!loading || window.HanakaWebActivity) return;

    const NAV_KEY = "hanaka.web.navigation";
    const REQUEST_TIMEOUT = 30000;
    const NAV_TIMEOUT = 20000;
    const requests = new Set();
    const regions = new Map();
    const busyControls = new Map();
    const inertElements = new Map();
    const imageWaits = new Map();
    const observedImages = new WeakSet();
    let trigger = null;
    let bootToken = null;
    let bootTimer = 0;
    let quietTimer = 0;
    let navigation = null;
    let ready = document.readyState !== "loading";
    let suspended = false;
    let previousFocus = null;

    function readNavigation() {
        try { return JSON.parse(sessionStorage.getItem(NAV_KEY) || "null"); } catch (_) { return null; }
    }
    function clearNavigation() {
        try { sessionStorage.removeItem(NAV_KEY); } catch (_) { /* Storage may be unavailable in a webview. */ }
    }
    function afterPaint(callback) {
        // The timeout also completes work in hidden tabs, where rAF is suspended.
        let first, second;
        const finish = function () { clearTimeout(timer); cancelAnimationFrame(first); cancelAnimationFrame(second); callback(); };
        const timer = setTimeout(finish, 120);
        first = requestAnimationFrame(function () { second = requestAnimationFrame(finish); });
    }
    function notice(message) {
        let box = document.querySelector("[data-web-activity-notice]");
        if (!box) {
            box = document.createElement("div");
            box.className = "web-activity-notice";
            box.setAttribute("data-web-activity-notice", "");
            box.setAttribute("role", "status");
            const text = document.createElement("span");
            const close = document.createElement("button");
            close.type = "button";
            close.textContent = "Đóng";
            close.onclick = function () { box.hidden = true; };
            box.append(text, close);
            (document.body || document.documentElement).appendChild(box);
        }
        box.firstElementChild.textContent = message;
        box.hidden = false;
    }
    function finishBoot() {
        clearTimeout(bootTimer);
        clearTimeout(quietTimer);
        if (bootToken) loading.end(bootToken);
        bootToken = null;
        imageWaits.forEach(cancel => cancel());
        imageWaits.clear();
    }
    function settleBoot() {
        clearTimeout(quietTimer);
        if (!bootToken || !ready || requests.size || regions.size) return;
        // Wait briefly for the main cover's dimensions; avatars and off-screen
        // images must never hold up the page. Loading still has a bounded lifetime.
        document.querySelectorAll("img[data-page-critical-image]").forEach(function (img) {
            if (img.complete || observedImages.has(img)) return;
            observedImages.add(img);
            const complete = function () {
                cancel();
                imageWaits.delete(img);
                settleBoot();
            };
            const timer = setTimeout(complete, 2500);
            const cancel = function () {
                clearTimeout(timer);
                img.removeEventListener("load", complete);
                img.removeEventListener("error", complete);
            };
            imageWaits.set(img, cancel);
            img.addEventListener("load", complete, {once: true});
            img.addEventListener("error", complete, {once: true});
        });
        if (imageWaits.size) return;
        quietTimer = setTimeout(function () {
            afterPaint(function () {
                if (!requests.size && !regions.size && !imageWaits.size) finishBoot();
            });
        }, 80);
    }
    function startVisual(options, method) {
        const control = options?.button || trigger;
        const inBusyRegion = control && [...regions.values()].some(entry => entry.target.contains(control));
        return bootToken || navigation || suspended || inBusyRegion ? null : loading.begin(options, method);
    }
    function setRegionBusy(key, target, busy, message) {
        if (!target) return;
        if (busy && !regions.has(key)) {
            regions.set(key, { token: startVisual({mode: "section", target, message: message || "Đang tải dữ liệu..."}), target });
            target.setAttribute("data-web-region-busy", "");
        } else if (!busy && regions.has(key)) {
            const entry = regions.get(key);
            regions.delete(key);
            afterPaint(function () {
                loading.end(entry.token);
                if (![...regions.values()].some(value => value.target === target)) target.removeAttribute("data-web-region-busy");
                settleBoot();
            });
        }
        settleBoot();
    }
    function lockControl(control) {
        if (!control) return function () {};
        const state = busyControls.get(control) || {count: 0};
        state.count++;
        busyControls.set(control, state);
        return function () {
            if (busyControls.get(control) !== state) return;
            if (--state.count <= 0) busyControls.delete(control);
        };
    }

    // Public API responses keep their native identity, status, headers and body.
    // Track body consumers too: fetch alone resolves as soon as headers arrive.
    const originalFetch = window.fetch.bind(window);
    window.fetch = function (input, init) {
        const source = init || {};
        let url;
        try { url = new URL(typeof input === "string" || input instanceof URL ? input : input.url, window.location.href); }
        catch (_) { return originalFetch(input, init); }
        if (url.origin !== location.origin || !url.pathname.startsWith("/api/")) return originalFetch(input, init);

        const method = String(source.method || input?.method || "GET").toUpperCase();
        const options = source.hanakaLoading === undefined ? {mode: "global"} : source.hanakaLoading;
        const control = (options && options.button) || trigger;
        const unlock = options === "silent" || options === false ? function () {} : lockControl(control);
        const token = startVisual(options, method);
        const controller = new AbortController();
        const callerSignal = source.signal || input?.signal;
        const abort = function () { controller.abort(callerSignal.reason); };
        if (callerSignal?.aborted) abort(); else callerSignal?.addEventListener("abort", abort, {once: true});
        let finished = false;
        let consuming = 0;
        let fallbackTimer;
        const entry = {cancel: function () { controller.abort(); finish(); }};
        requests.add(entry);
        clearTimeout(quietTimer);
        const timeout = setTimeout(function () {
            controller.abort(new DOMException(method === "GET" || method === "HEAD"
                ? "Tải dữ liệu quá lâu. Vui lòng kiểm tra kết nối và thử lại."
                : "Chưa nhận được kết quả xử lý. Vui lòng kiểm tra trạng thái trước khi gửi lại.", "TimeoutError"));
            finish();
        }, REQUEST_TIMEOUT);
        function finish() {
            if (finished) return;
            finished = true;
            clearTimeout(timeout);
            clearTimeout(fallbackTimer);
            callerSignal?.removeEventListener("abort", abort);
            // Restore a button before the caller's finally block restores its own state.
            if (token?.mode === "button") loading.end(token);
            afterPaint(function () {
                loading.end(token);
                unlock();
                requests.delete(entry);
                settleBoot();
            });
        }
        const requestInit = Object.assign({}, source, {signal: controller.signal, hanakaLoading: "silent"});
        return originalFetch(input, requestInit).then(function (response) {
            ["json", "text", "blob", "arrayBuffer", "formData"].forEach(function (methodName) {
                const consume = response[methodName];
                if (typeof consume !== "function") return;
                Object.defineProperty(response, methodName, {configurable: true, value: function () {
                    clearTimeout(fallbackTimer);
                    consuming++;
                    return Promise.resolve().then(function () { return consume.call(response); }).finally(function () {
                        if (--consuming === 0) finish();
                    });
                }});
            });
            // HEAD/204 and callers that only inspect status must also release loading.
            fallbackTimer = setTimeout(function () { if (!consuming) finish(); }, 0);
            return response;
        }).catch(function (error) { finish(); throw error; });
    };

    function isWebUrl(url) {
        return url.origin === location.origin && (url.pathname === "/" || /^\/PickleballWeb(?:\/|$)/i.test(url.pathname));
    }
    function endNavigation() {
        if (!navigation) return;
        clearTimeout(navigation.timer);
        loading.end(navigation.token);
        navigation = null;
        clearNavigation();
    }
    function beginNavigation(url) {
        if (navigation || !isWebUrl(url)) return;
        try { sessionStorage.setItem(NAV_KEY, JSON.stringify({path: url.pathname, at: Date.now()})); } catch (_) {}
        navigation = {token: loading.begin({mode: "global", message: "Đang chuyển trang..."})};
        navigation.timer = setTimeout(function () {
            endNavigation();
            notice("Trang chưa chuyển được. Bạn có thể thử lại hoặc tiếp tục sử dụng trang hiện tại.");
        }, NAV_TIMEOUT);
    }
    function navigate(url, options) {
        const target = new URL(url, location.href);
        if (!/^https?:$/.test(target.protocol)) return false;
        if (navigation) return false;
        const sameDocumentHash = target.pathname === location.pathname && target.search === location.search && target.hash;
        if (!sameDocumentHash) beginNavigation(target);
        try {
            if (options?.reload) location.reload();
            else if (options?.replace) location.replace(target.href);
            else location.assign(target.href);
        } catch (error) { endNavigation(); throw error; }
        return true;
    }
    function blockBusy(event) {
        const control = event.type === "submit" ? event.target : event.target.closest?.("a, button, input[type=submit]");
        if (navigation || busyControls.has(control) || busyControls.has(control?.form)
            || [...regions.values()].some(entry => entry.target.contains(control))) {
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
        trigger = control;
        setTimeout(function () { if (trigger === control) trigger = null; }, 0);
    }
    document.addEventListener("click", blockBusy, true);
    document.addEventListener("submit", blockBusy, true);
    document.addEventListener("click", function (event) {
        const link = event.target.closest?.("a[href]");
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || link.hasAttribute("download") || (link.target && link.target !== "_self")
            || link.closest("[data-no-page-loading]")) return;
        const url = new URL(link.href, location.href);
        if (!isWebUrl(url) || (url.pathname === location.pathname && url.search === location.search && url.hash)) return;
        queueMicrotask(function () { if (!event.defaultPrevented) beginNavigation(url); });
    });
    document.addEventListener("submit", function (event) {
        const form = event.target;
        if ((form.target && form.target !== "_self") || form.method === "dialog") return;
        queueMicrotask(function () {
            if (!event.defaultPrevented) beginNavigation(new URL(event.submitter?.formAction || form.action, location.href));
        });
    });

    function restoreInteraction() {
        inertElements.forEach(function (wasInert, element) { element.inert = wasInert; });
        inertElements.clear();
        if (!suspended && previousFocus?.isConnected && !previousFocus.disabled && document.activeElement === document.body) {
            previousFocus.focus({preventScroll: true});
        }
        previousFocus = null;
    }
    function syncInteraction(visible) {
        if (!visible) { restoreInteraction(); return; }
        if (!previousFocus) previousFocus = document.activeElement;
        Array.from(document.body?.children || []).forEach(function (element) {
            if (element.matches(".hanaka-api-overlay, [data-web-activity-notice], script, link, style") || inertElements.has(element)) return;
            inertElements.set(element, element.inert);
            element.inert = true;
        });
    }
    document.addEventListener("hanaka:api-loading-visibility", function (event) { syncInteraction(event.detail.visible); });
    const bodyObserver = new MutationObserver(function () {
        if (document.documentElement.classList.contains("hanaka-api-busy")) syncInteraction(true);
    });
    document.addEventListener("DOMContentLoaded", function () {
        bodyObserver.observe(document.body, {childList: true});
        if (document.documentElement.classList.contains("hanaka-api-busy")) syncInteraction(true);
    }, {once: true});

    const pending = readNavigation();
    clearNavigation();
    bootToken = loading.begin({mode: "global", message: "Đang tải trang...", immediate: !!pending && pending.path === location.pathname && Date.now() - pending.at < NAV_TIMEOUT});
    bootTimer = setTimeout(function () {
        finishBoot();
        if (requests.size || regions.size) notice("Dữ liệu đang tải lâu hơn dự kiến. Bạn có thể chờ thêm hoặc tải lại trang.");
    }, NAV_TIMEOUT);
    document.addEventListener("DOMContentLoaded", function () { ready = true; settleBoot(); }, {once: true});
    if (ready) settleBoot();
    window.addEventListener("pagehide", function () {
        suspended = true;
        clearTimeout(navigation?.timer);
        navigation = null; // Keep only the destination path for the next document.
        finishBoot();
        requests.forEach(function (entry) { entry.cancel(); });
        regions.forEach(function (entry) { loading.end(entry.token); entry.target.removeAttribute("data-web-region-busy"); });
        regions.clear();
        busyControls.clear();
        loading.reset();
        restoreInteraction();
    });
    window.addEventListener("pageshow", function (event) {
        suspended = false;
        if (event.persisted) {
            clearNavigation();
            loading.reset();
            restoreInteraction();
            ready = true;
        }
    });
    window.HanakaWebActivity = {navigate, setRegionBusy, notice, get activeCount() { return requests.size; }};
})(window, document);
