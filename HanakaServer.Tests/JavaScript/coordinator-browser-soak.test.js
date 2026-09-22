"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { openEdge, delay } = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);
const minutes = Math.max(1, Math.min(120, Number(process.env.HANAKA_COORDINATION_BROWSER_SOAK_MINUTES) || 30));

test("long-lived real coordinator browser keeps cards, polling and retained memory stable through tournament changes", {
    skip: process.env.HANAKA_COORDINATION_BROWSER_SOAK !== "1" || !browser, timeout: (minutes + 3) * 60000
}, async () => {
    const artifacts = path.resolve(__dirname, "../../artifacts/coordination-stability");
    const host = JSON.parse(fs.readFileSync(path.join(artifacts, "active-host.json"), "utf8"));
    assert.equal(new URL(host.url).hostname, "127.0.0.1");
    assert.equal(host.password, "Coordinator-stability-test-only!");
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-browser-soak-"));
    const samples = [], failures = [], phaseBaselines = new Map();
    let page, activePhase = -1;
    const started = Date.now(), deadline = started + minutes * 60000;
    const log = path.join(artifacts, "browser-soak-" + new Date().toISOString().replace(/[:.]/g, "-"));
    const wait = async (predicate, label) => {
        for (let i = 0; i < 200; i++) { if (await predicate()) return; await delay(50); }
        throw Error("Timeout: " + label);
    };
    try {
        page = await openEdge(browser, directory);
        await page.command("Performance.enable");
        await page.command("Page.navigate", { url: host.url + "/CoordinatorPortal/Login" });
        await wait(() => page.evaluate("!!document.getElementById('account')").catch(() => false), "login page");
        await page.evaluate(`document.getElementById('account').value=${JSON.stringify(host.coordinator)};document.getElementById('password').value=${JSON.stringify(host.password)};document.querySelector('#coordinator-login button').click()`);
        await wait(() => page.evaluate("!!document.querySelector('.match-card')").catch(() => false), "authenticated schedule");
        while (Date.now() < deadline) {
            // Follow the HTTP/SQL soak's 100, 500 and 2000 match phases.
            const phase = Math.min(2, Math.max(0, Math.floor((Date.now() - Date.parse(host.startedAt)) / (host.minutes * 60000 / 3))));
            const fixture = host.tournaments[phase];
            if (activePhase !== phase) {
                await page.evaluate(`document.getElementById('tournament').value=${JSON.stringify(String(fixture.id))};document.getElementById('tournament').dispatchEvent(new Event('change'))`);
                activePhase = phase;
            }
            await wait(() => page.evaluate(`document.querySelectorAll('.match-card').length === ${fixture.matches.length} && !document.getElementById('refresh').disabled`), "schedule phase " + phase);
            const ui = await page.evaluate(`(() => {
                const $ = id => document.getElementById(id);
                return { cards: document.querySelectorAll('.match-card').length,
                    counters: ['NOT_STARTED','PREPARING','IN_PROGRESS','COMPLETED'].map(s => Number($('count-' + s).textContent)),
                    error: $('page-error').hidden ? null : $('page-error').textContent,
                    sync: $('sync-state').textContent,
                    firstCourt: document.querySelector('.match-card .court-line')?.textContent };
            })()`);
            assert.equal(ui.error, null); assert.equal(ui.cards, fixture.matches.length);
            assert.equal(ui.counters.reduce((a, b) => a + b), fixture.matches.length);
            // Full GC here measures retained objects, avoiding false leak claims from
            // normal garbage waiting to be collected after the 30-second REST poll.
            await page.command("HeapProfiler.collectGarbage");
            const metrics = Object.fromEntries((await page.command("Performance.getMetrics")).metrics.map(m => [m.name, m.value]));
            const sample = { elapsedSeconds: Math.round((Date.now() - started) / 1000), phase, matches: fixture.matches.length,
                heapBytes: metrics.JSHeapUsedSize, domNodes: metrics.Nodes, listeners: metrics.JSEventListeners, ...ui };
            if (!phaseBaselines.has(phase)) phaseBaselines.set(phase, sample);
            const baseline = phaseBaselines.get(phase);
            assert.ok(sample.heapBytes < baseline.heapBytes * 2 + 10 * 1024 * 1024, "Retained JS heap grew beyond the per-phase guard");
            assert.ok(sample.domNodes <= baseline.domNodes * 1.2 + 1000, "Detached DOM accumulated across refreshes");
            samples.push(sample);
            fs.appendFileSync(log + ".jsonl", JSON.stringify(sample) + "\n");
            console.log(`Browser soak ${sample.elapsedSeconds}s: ${ui.cards} cards, retained heap ${Math.round(sample.heapBytes / 1024)} KiB, ${sample.domNodes} nodes`);
            // Short waits keep the run interruptible; the parent monitors progress separately.
            for (let i = 0; i < 12 && Date.now() < deadline; i++) await delay(Math.min(5000, deadline - Date.now()));
        }
    } catch (error) { failures.push(error.stack); throw error; }
    finally {
        fs.writeFileSync(log + ".json", JSON.stringify({ requestedMinutes: minutes, elapsedSeconds: (Date.now() - started) / 1000,
            passed: failures.length === 0 && Date.now() >= deadline, failures, samples }, null, 2));
        if (page) await page.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith("hanaka-browser-soak-"));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
});
