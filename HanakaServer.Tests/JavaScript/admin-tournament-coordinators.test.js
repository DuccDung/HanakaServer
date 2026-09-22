"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const {openEdge} = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("admin assigns and revokes the chosen account without duplicate writes or HTML injection", {skip: !browser, timeout: 60000}, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const view = fs.readFileSync(path.join(root, "Views/TournamentCoordinators/Index.cshtml"), "utf8");
    const markup = view.slice(view.indexOf('<main'), view.indexOf('</main>') + 7).replace('@ViewBag.TournamentId', '37');
    const script = fs.readFileSync(path.join(root, "wwwroot/js/admin-tournament-coordinators.js"), "utf8");
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-coordination-"));
    let session;
    try {
        session = await openEdge(browser, directory);
        await session.command("Page.setDocumentContent", {frameId: (await session.command("Page.getFrameTree")).frameTree.frame.id,
            html: '<!doctype html><meta charset="utf-8">' + markup});
        const scenario = async ({script}) => {
            const wait = async predicate => { for(let i=0; i<100; i++) { if(predicate()) return; await new Promise(r=>setTimeout(r, 10)); } throw Error('Timeout'); };
            const user = {userId: 9, fullName: '<img src=x onerror=alert(1)> Nguyễn A', phone: '0900000000'};
            let assigned = false, release;
            const writes = [];
            window.confirm = () => true;
            window.fetch = async (url, options = {}) => {
                let body;
                if(options.method === 'PUT' || options.method === 'DELETE') {
                    writes.push({url, method: options.method});
                    if(options.method === 'PUT') await new Promise(resolve => release = resolve);
                    assigned = options.method === 'PUT'; body = {ok: true};
                } else body = url.includes('lookup') ? {items: [user]} : assigned ? [user] : [];
                return new Response(JSON.stringify(body), {headers: {'content-type':'application/json'}});
            };
            window.eval(script);
            await wait(() => document.getElementById('coordinator-list').textContent.includes('Chưa có'));
            document.getElementById('coordinator-query').value = '0900000000';
            document.getElementById('coordinator-search').dispatchEvent(new Event('submit', {cancelable: true}));
            await wait(() => document.querySelector('#coordinator-results button'));
            if(document.querySelector('#coordinator-results img')) throw Error('Unsafe account rendering');
            const button = document.querySelector('#coordinator-results button'); button.click(); button.click();
            await wait(() => release); release();
            await wait(() => document.querySelector('#coordinator-list button'));
            if(writes.length !== 1 || writes[0].url !== '/api/admin/tournaments/37/coordinators/9') throw Error('Duplicate or incorrect assignment');
            document.querySelector('#coordinator-list button').click();
            await wait(() => document.getElementById('coordinator-list').textContent.includes('Chưa có'));
            if(writes.length !== 2 || writes[1].method !== 'DELETE') throw Error('Revoke failed');
            return true;
        };
        assert.equal(await session.evaluate(`(${scenario.toString()})(${JSON.stringify({script})})`), true);
    } finally {
        if(session) await session.close();
        assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(directory).startsWith('hanaka-coordination-'));
        await fs.promises.rm(directory, {recursive: true, force: true, maxRetries: 20, retryDelay: 100});
    }
});
