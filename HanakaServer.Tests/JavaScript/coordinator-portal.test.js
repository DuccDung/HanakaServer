"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { openEdge } = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("coordinator portal filters, saves, preserves drafts and respects scoring, stale reads and revocation", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const view = fs.readFileSync(path.join(root, "Views/CoordinatorPortal/Matches.cshtml"), "utf8");
    const markup = view.slice(view.indexOf('<div id="coordinator-portal">'), view.indexOf('@section Scripts'))
        .replace('@Html.AntiForgeryToken()', '<input type="hidden" name="__RequestVerificationToken" value="test-csrf" />');
    const script = fs.readFileSync(path.join(root, "wwwroot/js/coordinator-http.js"), "utf8") + "\n" + fs.readFileSync(path.join(root, "wwwroot/js/coordinator-portal.js"), "utf8");
    const css = fs.readFileSync(path.join(root, "wwwroot/css/coordinator-portal.css"), "utf8");
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-coordinator-portal-"));
    let session;
    try {
        session = await openEdge(browser, directory);
        await session.command("Page.setDocumentContent", { frameId: (await session.command("Page.getFrameTree")).frameTree.frame.id,
            html: '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><style>' + css + '</style><body>' + markup + '</body></html>' });
        const setup = async script => {
            const $ = id => document.getElementById(id);
            const wait = async predicate => { for (let i = 0; i < 180; i++) { if (predicate()) return; await new Promise(r => setTimeout(r, 10)); } throw Error("Timeout waiting for portal UI"); };
            const check = (condition, message) => { if (!condition) throw Error(message); };
            let listener, mode = "ok", releaseWrite, releaseRead, deferRead = false, assignments = true;
            const writes = [], subscriptions = [];
            const row = (id, matchStatus = "NOT_STARTED") => ({ matchId: id, tournamentId: 37, stateVersion: 1, matchStatus,
                team1RegistrationId: 10 + id, team2RegistrationId: 20 + id, team1: { displayName: "TEAM VĂN VẢI VÀ NHỮNG NGƯỜI BẠN" },
                team2: { displayName: "TEAM HANAKA" }, scoreTeam1: 0, scoreTeam2: 0, courtText: "Sân 1", startAt: "2026-09-22T08:00:00" });
            const rows = [row(1), row(2, "PREPARING"), row(3, "IN_PROGRESS"), row(4, "COMPLETED"), row(5)];
            rows[3].isCompleted = true; rows[3].scoreTeam1 = 63; rows[3].scoreTeam2 = 43; rows[3].winnerRegistrationId = rows[3].team1RegistrationId;
            rows[4].team2RegistrationId = null; rows[4].courtText = null; rows[4].team1.displayName = '<img src=x onerror=alert(1)> Đội chờ';
            const schedule = () => ({ rounds: [{ tournamentRoundMapId: 7, roundKey: "R1", roundLabel: "Vòng 1", groups: [{ groupName: "Bảng Nhánh 1", matches: structuredClone(rows) }] }] });
            const response = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
            window.HanakaPublicRealtime = { on: callback => listener = callback, subscribeTournament: id => subscriptions.push(id), unsubscribeTournament: () => {} };
            window.fetch = async (url, options = {}) => {
                if (options.method === "PUT") {
                    writes.push({ url, body: JSON.parse(options.body), token: options.headers.RequestVerificationToken });
                    if (mode === "fail") throw Error("Mất kết nối thử nghiệm");
                    const target = rows.find(r => r.matchId === Number(url.split('/').pop()));
                    if (mode === "conflict") {
                        target.stateVersion++; target.matchStatus = "IN_PROGRESS";
                        return response({ message: "Trận đã được trọng tài cập nhật." }, 409);
                    }
                    if (mode === "defer") await new Promise(resolve => releaseWrite = resolve);
                    const payload = JSON.parse(options.body);
                    check(payload.expectedVersion === target.stateVersion, "Wrong expected version sent");
                    target.stateVersion++; target.courtText = payload.courtText; target.matchStatus = payload.preparing ? "PREPARING" : "NOT_STARTED";
                    return response(structuredClone(target));
                }
                if (url.includes("/session")) return response({ userId: 9, fullName: "Điều phối Nguyễn Văn An", tournaments: assignments ? [{ tournamentId: 37, title: "Giải Pickleball Hanaka mở rộng 2026" }] : [] });
                const data = schedule();
                if (deferRead) { deferRead = false; await new Promise(resolve => releaseRead = resolve); }
                return response(data);
            };
            window.eval(script);
            await wait(() => document.querySelectorAll('.match-card').length === 5);
            check(subscriptions.includes(37), "Did not subscribe to assigned tournament");
            check(!document.querySelector('#matches img'), "Team names allowed HTML injection");
            check(document.querySelectorAll('.match-footer button').length === 3, "Started/completed matches are editable");
            document.querySelector('[data-status="PREPARING"]').click();
            check(document.querySelectorAll('.match-card').length === 1 && $('status-filter').value === 'PREPARING', "Status filter failed");
            $('clear-filters').click(); $('court-filter').value = '__unassigned'; $('court-filter').dispatchEvent(new Event('change'));
            check(document.querySelectorAll('.match-card').length === 1, "Unassigned court filter failed");
            $('clear-filters').click(); $('search').value = 'HANAKA'; $('search').dispatchEvent(new Event('input'));
            check(document.querySelectorAll('.match-card').length === 5, "Team search failed"); $('clear-filters').click();
            window.portalTest = { $, wait, check, rows, writes, open: id => document.querySelector(`[data-match-id="${id}"] button`).click(),
                mode: value => mode = value, releaseWrite: () => releaseWrite(), releaseRead: () => releaseRead(),
                hasWrite: () => !!releaseWrite, hasRead: () => !!releaseRead, deferRead: () => deferRead = true,
                revoke: () => assignments = false, emit: patch => listener({ type: 'tournament.match.score.updated', payload: patch }) };
            return true;
        };
        assert.equal(await session.evaluate(`(${setup.toString()})(${JSON.stringify(script)})`), true);
        const artifacts = path.resolve(__dirname, "../../artifacts/coordinator-portal");
        fs.mkdirSync(artifacts, { recursive: true });
        for (const width of [320, 390, 768, 1280]) {
            await session.command("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: false });
            assert.equal(await session.evaluate("document.documentElement.scrollWidth <= innerWidth"), true, `Horizontal overflow at ${width}px`);
            if (width === 390 || width === 1280) {
                const shot = await session.command("Page.captureScreenshot", { format: "png", captureBeyondViewport: true });
                fs.writeFileSync(path.join(artifacts, `matches-${width}.png`), Buffer.from(shot.data, "base64"));
            }
        }
        const interactions = async () => {
            const t = window.portalTest, { $, check, wait } = t;
            const submit = () => $('coordination-form').dispatchEvent(new Event('submit', { cancelable: true }));
            t.open(1); $('edit-court').value = 'Sân 2'; $('edit-preparing').checked = true;
            t.mode('fail'); submit(); await wait(() => $('edit-error').textContent.includes('Mất kết nối'));
            check($('edit-court').value === 'Sân 2' && $('coordination-dialog').open, 'Draft lost after network failure');
            await wait(() => !$('reload-editor').disabled);
            check($('save-coordination').disabled, 'Unknown write result must be reconciled before retry');
            $('reload-editor').click(); await wait(() => !$('save-coordination').disabled);
            $('edit-court').value = 'Sân 2'; $('edit-preparing').checked = true;
            t.mode('defer'); submit(); submit(); await wait(t.hasWrite);
            check(t.writes.length === 2, 'Duplicate save while request in flight'); t.releaseWrite();
            await wait(() => !$('coordination-dialog').open);
            check(t.writes[1].token === 'test-csrf', 'Write lacks anti-forgery token');
            check(t.rows[0].matchStatus === 'PREPARING' && t.rows[0].courtText === 'Sân 2', 'Preparation not persisted');
            t.open(1); $('edit-court').value = 'Sân bản nháp';
            const untouched = document.querySelector('[data-match-id="5"]');
            t.rows[0].stateVersion++; t.rows[0].courtText = 'Sân 9'; t.emit(structuredClone(t.rows[0]));
            check(document.querySelector('[data-match-id="5"]') === untouched, 'Realtime recreated an unchanged card');
            check($('save-coordination').disabled && !$('reload-editor').hidden, 'Concurrent edit did not require reload');
            check($('edit-court').value === 'Sân bản nháp', 'Realtime overwrote draft');
            $('reload-editor').click(); await wait(() => $('edit-court').value === 'Sân 9');
            check(!$('save-coordination').disabled, 'Reload did not restore editable version');
            $('edit-court').value = 'Sân 10'; t.mode('conflict'); submit();
            await wait(() => $('edit-warning').textContent.includes('Trận đã bắt đầu'));
            check($('save-coordination').disabled && $('edit-court').value === 'Sân 10', '409 lost draft or allowed overwrite');
            await wait(() => !$('close-editor').disabled); $('close-editor').click();
            await new Promise(resolve => setTimeout(resolve, 20));
            t.open(2); t.deferRead(); $('refresh').click(); await wait(t.hasRead);
            t.rows[1].stateVersion++; t.rows[1].matchStatus = 'IN_PROGRESS'; t.rows[1].scoreTeam1 = 1; t.emit(structuredClone(t.rows[1]));
            check($('save-coordination').disabled, 'Referee start did not lock editor immediately');
            t.releaseRead(); await wait(() => !$('refresh').disabled);
            check(document.querySelector('[data-match-id="2"]').classList.contains('IN_PROGRESS'), 'Late REST overwrote newer socket state');
            t.emit({ ...t.rows[1], stateVersion: 1, matchStatus: 'NOT_STARTED' });
            check(document.querySelector('[data-match-id="2"]').classList.contains('IN_PROGRESS'), 'Old socket event overwrote current state');
            $('close-editor').click(); await new Promise(resolve => setTimeout(resolve, 20));
            const beforeFilter = document.querySelector('[data-match-id="5"]');
            $('status-filter').value = 'NOT_STARTED'; $('status-filter').dispatchEvent(new Event('change'));
            check([...document.querySelectorAll('.match-card')].map(n => n.dataset.matchId).join() === '5', 'Status filter kept a changed card');
            t.rows[1].stateVersion++; t.rows[1].matchStatus = 'NOT_STARTED'; t.emit(structuredClone(t.rows[1]));
            check([...document.querySelectorAll('.match-card')].map(n => n.dataset.matchId).join() === '2,5', 'Realtime did not insert a newly matching card in schedule order');
            check(document.querySelector('[data-match-id="5"]') === beforeFilter, 'Filtering recreated an unchanged card');
            $('clear-filters').click();
            check([...document.querySelectorAll('.match-card')].map(n => n.dataset.matchId).join() === '1,2,3,4,5', 'Clearing filters changed schedule order');
            t.open(5); check($('edit-preparing').disabled && !$('edit-court').disabled, 'Missing teams should only prevent preparation');
            t.revoke(); $('refresh').click(); await wait(() => document.querySelectorAll('.match-card').length === 0);
            check(!$('coordination-dialog').open && $('tournament').disabled, 'Revocation did not clear operator controls');
            return true;
        };
        assert.equal(await session.evaluate(`(${interactions.toString()})()`), true);
    } finally {
        if (session) await session.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith('hanaka-coordinator-portal-'));
        await fs.promises.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
    }
});
