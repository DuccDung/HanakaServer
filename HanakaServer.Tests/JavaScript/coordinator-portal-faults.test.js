"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { openEdge } = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);

test("portal recovers from timeout and incomplete responses, and ignores late data after changing tournament", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const view = fs.readFileSync(path.join(root, "Views/CoordinatorPortal/Matches.cshtml"), "utf8");
    const markup = view.slice(view.indexOf('<div id="coordinator-portal">'), view.indexOf('@section Scripts'))
        .replace('@Html.AntiForgeryToken()', '<input type="hidden" name="__RequestVerificationToken" value="fault-test" />');
    const script = ["coordinator-http.js", "coordinator-portal.js"].map(name => fs.readFileSync(path.join(root, "wwwroot/js", name), "utf8")).join('\n');
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-coordinator-faults-"));
    let page;
    try {
        page = await openEdge(browser, directory);
        await page.command("Page.setDocumentContent", { frameId: (await page.command("Page.getFrameTree")).frameTree.frame.id, html: '<!doctype html><meta charset="utf-8">' + markup });
        const scenario = async script => {
            const $ = id => document.getElementById(id);
            const nativeTimeout = window.setTimeout.bind(window);
            // Accelerate only the production 20-second request deadline, preserving all other browser timers.
            window.setTimeout = (callback, ms, ...args) => nativeTimeout(callback, ms === 20000 ? 80 : ms, ...args);
            const wait = async (predicate, label) => { for (let i = 0; i < 150; i++) { if (predicate()) return; await new Promise(r => nativeTimeout(r, 10)); } throw Error("Timeout: " + label); };
            const check = (ok, text) => { if (!ok) throw Error(text); };
            let fault = '', writes = 0, listener, releaseRead;
            const row = id => ({ matchId: id * 10, tournamentId: id, stateVersion: 1, matchStatus: 'NOT_STARTED',
                team1RegistrationId: 1, team2RegistrationId: 2, team1: { displayName: 'Đội A' }, team2: { displayName: 'Đội B' }, courtText: 'Sân ' + id });
            const rows = { 1: row(1), 2: row(2) };
            const response = body => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
            window.HanakaPublicRealtime = { on: fn => listener = fn, subscribeTournament: () => {}, unsubscribeTournament: () => {} };
            window.fetch = async (url, options = {}) => {
                if (options.method === 'PUT') {
                    writes++;
                    if (fault === 'hang') return new Promise((_resolve, reject) => options.signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError'))));
                    if (fault === 'bad-write') { rows[1].stateVersion++; rows[1].courtText = 'Sân server đã lưu'; return response({}); }
                }
                if (url.endsWith('/session')) return response(fault === 'bad-session' ? {} : { userId: 9, fullName: 'Điều phối', tournaments: [{ tournamentId: 1, title: 'Giải A' }, { tournamentId: 2, title: 'Giải B' }] });
                const id = Number(url.split('/')[3]);
                if (fault === 'bad-schedule') return response({});
                if (fault === 'late' && id === 1) { fault = ''; await new Promise(resolve => releaseRead = resolve); }
                return response({ rounds: [{ tournamentRoundMapId: id, roundLabel: 'Vòng 1', groups: [{ groupName: 'A', matches: [structuredClone(rows[id])] }] }] });
            };
            window.eval(script);
            await wait(() => document.querySelector('[data-match-id="10"]'), 'initial schedule');
            fault = 'bad-session'; $('refresh').click();
            await wait(() => !$('refresh').disabled && !$('page-error').hidden, 'incomplete session');
            check(document.querySelector('[data-match-id="10"]'), 'Incomplete session cleared current schedule');
            fault = 'bad-schedule'; $('refresh').click(); await wait(() => !$('refresh').disabled, 'incomplete schedule');
            check(document.querySelector('[data-match-id="10"]'), 'Incomplete schedule cleared current data');
            fault = ''; document.querySelector('[data-match-id="10"] button').click();
            $('edit-court').value = 'Sân bản nháp'; $('edit-preparing').checked = true;
            fault = 'hang'; $('save-coordination').click();
            await wait(() => !$('close-editor').disabled && $('edit-error').textContent.includes('quá lâu'), 'timeout releases busy state');
            check(writes === 1 && $('save-coordination').disabled && $('edit-court').value === 'Sân bản nháp', 'Timeout replayed or lost draft');
            fault = ''; $('reload-editor').click(); await wait(() => !$('save-coordination').disabled, 'reload after timeout');
            fault = 'bad-write'; $('edit-court').value = 'Sân server đã lưu'; $('save-coordination').click();
            await wait(() => !$('close-editor').disabled && $('edit-error').textContent.includes('Chưa xác nhận'), 'incomplete success');
            check($('coordination-dialog').open && $('toast').hidden && $('save-coordination').disabled, 'Incomplete success falsely completed save');
            fault = ''; $('reload-editor').click(); await wait(() => !$('save-coordination').disabled, 'reconcile committed update');
            check($('edit-court').value === 'Sân server đã lưu' && writes === 2, 'Lost response was replayed');
            $('close-editor').click(); await new Promise(r => nativeTimeout(r, 20));
            fault = 'late'; $('refresh').click(); await wait(() => releaseRead, 'delayed old tournament response');
            $('tournament').value = '2'; $('tournament').dispatchEvent(new Event('change'));
            await wait(() => document.querySelector('[data-match-id="20"]'), 'new tournament');
            releaseRead(); await new Promise(r => nativeTimeout(r, 30));
            listener({ type: 'tournament.match.coordination.updated', payload: { ...rows[1], stateVersion: 99, courtText: 'Wrong tournament' } });
            check(document.querySelector('[data-match-id="20"]') && !document.querySelector('[data-match-id="10"]'), 'Late data leaked across tournaments');
            check($('tournament').value === '2' && writes === 2, 'Selection changed or write replayed');
            return true;
        };
        assert.equal(await page.evaluate(`(${scenario.toString()})(${JSON.stringify(script)})`), true);
    } finally {
        if (page) await page.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith('hanaka-coordinator-faults-'));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
});
