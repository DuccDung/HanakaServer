"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { openEdge, delay } = require("./edge-browser");
const artifacts = path.resolve(__dirname, "../../../artifacts/public-coordination-stability");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);
function manifest() {
    const host = JSON.parse(fs.readFileSync(path.resolve(__dirname, "../../../artifacts/coordination-stability/active-host.json"), "utf8"));
    assert.ok(!host.stopped && host.url, "Start the isolated CoordinatorSoakTests host before running live browser tests");
    assert.equal(new URL(host.url).hostname, "127.0.0.1");
    assert.equal(new URL(host.url).protocol, "http:");
    assert.equal(host.password, "Coordinator-stability-test-only!");
    assert.ok(Date.now() - Date.parse(host.startedAt) < 4 * 60 * 60 * 1000, "Test host manifest is stale");
    fs.mkdirSync(artifacts, { recursive: true });
    return host;
}
async function wait(predicate, label, milliseconds = 15000) {
    const until = Date.now() + milliseconds;
    while (Date.now() < until) { if (await predicate()) return; await delay(50); }
    throw Error("Timeout: " + label);
}
async function openPage() {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-public-live-"));
    let page;
    try {
        page = await openEdge(browser, directory);
        await page.command("Network.enable");
        await page.command("Network.setBlockedURLs", { urls: ["*fonts.googleapis.com*", "*fonts.gstatic.com*", "*unpkg.com*"] });
        const close = page.close;
        page.close = async () => {
            await close();
            assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
            assert.ok(path.basename(directory).startsWith("hanaka-public-live-"));
            await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
        };
        return page;
    } catch (error) {
        if (page) await page.close();
        throw error;
    }
}
async function navigate(page, host, fixture) {
    await page.command("Page.navigate", { url: `${host.url}/PickleballWeb/Tournament/${fixture.id}/Schedule` });
    await wait(() => page.evaluate(`document.querySelectorAll('[data-schedule-match-id]').length === ${fixture.matches.length}`).catch(() => false), "real public schedule");
}
async function login(page, host, index = 0) {
    assert.equal(await page.evaluate(`(async () => {
        const response = await fetch('/api/web-auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ identifier: ${JSON.stringify(`coordinator${index}@example.test`)}, password: ${JSON.stringify(host.password)} }) });
        window.dispatchEvent(new Event('focus')); return response.status;
    })()`), 200);
}
async function openEditor(page, id) {
    await wait(() => page.evaluate(`!!document.querySelector('[data-coordinate-match="${id}"]:not([hidden])')`), "coordinate button");
    await page.evaluate(`document.querySelector('[data-coordinate-match="${id}"]').click()`);
    await wait(() => page.evaluate("document.querySelector('.schedule-coordination').open"), "coordination dialog");
}
async function saveEditor(page, court, preparing = true) {
    await page.evaluate(`(() => { const form = document.querySelector('.schedule-coordination form');
        form.elements.court.value = ${JSON.stringify(court)}; form.elements.preparing.checked = ${preparing}; form.querySelector('[data-save]').click(); })()`);
}
const cardStatus = (page, id, status) => page.evaluate(`document.querySelector('[data-schedule-match-id="${id}"]')?.dataset.matchStatus === ${JSON.stringify(status)}`);
module.exports = { assert, fs, path, artifacts, browser, manifest, wait, delay, openPage, navigate, login, openEditor, saveEditor, cardStatus };
