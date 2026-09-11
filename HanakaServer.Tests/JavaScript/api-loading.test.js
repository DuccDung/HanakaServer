"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

class FakeClassList {
    constructor() {
        this.values = new Set();
    }

    add(value) { this.values.add(value); }
    remove(value) { this.values.delete(value); }
    contains(value) { return this.values.has(value); }
}

class FakeElement {
    constructor() {
        this.nodeType = 1;
        this.hidden = false;
        this.disabled = false;
        this.isConnected = false;
        this.className = "";
        this.classList = new FakeClassList();
        this.attributes = new Map();
        this.children = [];
        this.textContent = "";
        this._parts = new Map();
    }

    set innerHTML(value) {
        this._innerHTML = value;
        [
            ".hanaka-api-overlay__message",
            ".hanaka-api-overlay__hint",
            ".hanaka-api-section-overlay__message"
        ].forEach(selector => this._parts.set(selector, new FakeElement()));
    }

    get innerHTML() { return this._innerHTML || ""; }

    appendChild(child) {
        child.isConnected = true;
        this.children.push(child);
        return child;
    }

    querySelector(selector) { return this._parts.get(selector) || null; }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.has(name) ? this.attributes.get(name) : null; }
    removeAttribute(name) { this.attributes.delete(name); }
    closest() { return this; }
}

function createRuntime() {
    const documentEvents = Object.create(null);
    const windowEvents = Object.create(null);
    const nativeRequests = [];
    const body = new FakeElement();
    body.isConnected = true;
    const documentElement = new FakeElement();
    documentElement.isConnected = true;

    const document = {
        body,
        documentElement,
        createElement() { return new FakeElement(); },
        querySelector() { return null; },
        addEventListener(type, handler) { (documentEvents[type] ||= []).push(handler); },
        dispatchEvent() { return true; }
    };

    let timerSequence = 0;
    const window = {
        document,
        fetch(input, init) {
            let resolve;
            let reject;
            const promise = new Promise((resolvePromise, rejectPromise) => {
                resolve = resolvePromise;
                reject = rejectPromise;
            });
            nativeRequests.push({ input, init, resolve, reject, promise });
            return promise;
        },
        setTimeout(handler) {
            const id = ++timerSequence;
            handler();
            return id;
        },
        clearTimeout() {},
        setInterval() { return ++timerSequence; },
        clearInterval() {},
        addEventListener(type, handler) { (windowEvents[type] ||= []).push(handler); }
    };

    const context = vm.createContext({
        window,
        document,
        CustomEvent: class CustomEvent {
            constructor(type, options) {
                this.type = type;
                this.detail = options && options.detail;
            }
        },
        Promise,
        Map,
        WeakMap,
        Set,
        Date,
        Array,
        Object,
        String,
        Math
    });

    const sourcePath = path.resolve(__dirname, "../../HanakaServer/wwwroot/js/api-loading.js");
    vm.runInContext(fs.readFileSync(sourcePath, "utf8"), context, { filename: sourcePath });
    return { window, document, nativeRequests, documentEvents, windowEvents };
}

test("fetch wrapper tracks concurrent foreground requests and always cleans up", async () => {
    const runtime = createRuntime();
    const first = runtime.window.fetch("/api/first");
    const second = runtime.window.fetch("/api/second", { method: "POST" });

    assert.equal(runtime.window.HanakaApiLoading.activeCount, 2);
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 2);

    runtime.nativeRequests[0].resolve({ ok: true });
    await first;
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 1);

    runtime.nativeRequests[1].reject(new Error("network"));
    await assert.rejects(second, /network/);
    assert.equal(runtime.window.HanakaApiLoading.activeCount, 0);
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);
});

test("silent fetch is counted for lifecycle but never opens the blocking overlay", async () => {
    const runtime = createRuntime();
    const request = runtime.window.fetch("/api/payment/status", {
        method: "GET",
        hanakaLoading: "silent"
    });

    assert.equal(runtime.window.HanakaApiLoading.activeCount, 1);
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);
    assert.equal(Object.prototype.hasOwnProperty.call(runtime.nativeRequests[0].init, "hanakaLoading"), false);

    runtime.nativeRequests[0].resolve({ ok: true });
    await request;
    assert.equal(runtime.window.HanakaApiLoading.activeCount, 0);
});

test("button mode is exclusive and restores the original button state", async () => {
    const runtime = createRuntime();
    const button = new FakeElement();
    const request = runtime.window.fetch("/api/save", {
        method: "POST",
        hanakaLoading: { mode: "button", button }
    });

    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);
    assert.equal(button.disabled, true);
    assert.equal(button.classList.contains("hanaka-api-button-busy"), true);

    runtime.nativeRequests[0].resolve({ ok: true });
    await request;

    assert.equal(button.disabled, false);
    assert.equal(button.classList.contains("hanaka-api-button-busy"), false);
    assert.equal(button.getAttribute("aria-busy"), null);
});

test("a request started by a click infers button mode instead of stacking a global overlay", async () => {
    const runtime = createRuntime();
    const button = new FakeElement();
    runtime.window.setTimeout = () => 1;
    runtime.documentEvents.click[0]({ target: button });

    const request = runtime.window.fetch("/api/save", { method: "POST" });
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);
    assert.equal(button.classList.contains("hanaka-api-button-busy"), true);

    runtime.nativeRequests[0].resolve({ ok: true });
    await request;
    assert.equal(button.classList.contains("hanaka-api-button-busy"), false);
});

test("global mode never injects a second loading state into its trigger button", async () => {
    const runtime = createRuntime();
    const button = new FakeElement();
    const request = runtime.window.fetch("/api/save", {
        method: "POST",
        hanakaLoading: { mode: "global", button }
    });

    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 1);
    assert.equal(button.disabled, false);
    assert.equal(button.classList.contains("hanaka-api-button-busy"), false);

    runtime.nativeRequests[0].resolve({ ok: true });
    await request;
});

test("button spinner is absolutely positioned so it cannot resize button content", () => {
    const repositoryRoot = path.resolve(__dirname, "..", "..");
    const css = fs.readFileSync(
        path.join(repositoryRoot, "HanakaServer", "wwwroot", "css", "api-loading.css"),
        "utf8"
    );

    assert.match(css, /button\.hanaka-api-button-busy::after\s*\{[^}]*position:\s*absolute/s);
    assert.doesNotMatch(css, /hanaka-api-button-busy::(?:before|after)\s*\{[^}]*margin-right/s);
});

test("axios interceptor closes activity for success and failure responses", async () => {
    const runtime = createRuntime();
    const handlers = {};
    const axios = {
        interceptors: {
            request: { use(handler) { handlers.request = handler; } },
            response: {
                use(success, failure) {
                    handlers.success = success;
                    handlers.failure = failure;
                }
            }
        }
    };

    assert.equal(runtime.window.HanakaApiLoading.installAxios(axios), true);
    assert.equal(runtime.window.HanakaApiLoading.installAxios(axios), false);

    const successConfig = handlers.request({ method: "post" });
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 1);
    handlers.success({ config: successConfig, data: {} });
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);

    const failureConfig = handlers.request({ method: "get", hanakaLoading: "silent" });
    assert.equal(runtime.window.HanakaApiLoading.foregroundCount, 0);
    await assert.rejects(handlers.failure({ config: failureConfig }), error => error.config === failureConfig);
    assert.equal(runtime.window.HanakaApiLoading.activeCount, 0);
});

test("the two shared layouts load the common activity assets", () => {
    const repositoryRoot = path.resolve(__dirname, "..", "..");
    const adminLayout = fs.readFileSync(
        path.join(repositoryRoot, "HanakaServer", "Views", "Shared", "_AdminLayout.cshtml"),
        "utf8"
    );
    const publicLayout = fs.readFileSync(
        path.join(repositoryRoot, "HanakaServer", "Views", "Shared", "_PickleballWebLayout.cshtml"),
        "utf8"
    );

    for (const layout of [adminLayout, publicLayout]) {
        assert.match(layout, /css\/api-loading\.css/);
        assert.match(layout, /js\/api-loading\.js/);
    }
});

test("every Razor view that loads Axios also installs the common tracker", () => {
    const repositoryRoot = path.resolve(__dirname, "..", "..");
    const viewsRoot = path.join(repositoryRoot, "HanakaServer", "Views");

    function visit(directory) {
        return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
            const fullPath = path.join(directory, entry.name);
            if (entry.isDirectory()) return visit(fullPath);
            return entry.name.endsWith(".cshtml") ? [fullPath] : [];
        });
    }

    const axiosViews = visit(viewsRoot).filter(file => /axios.*\.js/i.test(fs.readFileSync(file, "utf8")));
    assert.ok(axiosViews.length > 0, "The coverage check must discover Axios views.");

    axiosViews.forEach(file => {
        const source = fs.readFileSync(file, "utf8");
        const coveredByAdminLayout = /Layout\s*=\s*"_AdminLayout"/.test(source)
            && /HanakaApiLoading\?\.installAxios\(window\.axios\)/.test(source);
        const coveredStandalone = /js\/api-loading\.js/.test(source);
        assert.ok(
            coveredByAdminLayout || coveredStandalone,
            path.relative(repositoryRoot, file) + " does not initialize API loading."
        );
    });
});
