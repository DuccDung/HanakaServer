"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs"), os = require("node:os"), path = require("node:path");
const { spawn } = require("node:child_process");
const { pathToFileURL } = require("node:url");
const test = require("node:test");
const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("relay registration forms keep reserves optional and separate on mobile and desktop", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const read = file => fs.readFileSync(path.join(root, file), "utf8");
    let pages = read("wwwroot/pickleball-web/js/pages.js");
    let end = pages.lastIndexOf("})();");
    pages = pages.slice(0, end) + `requestJson = (...args) => window.__request(...args);
        window.__pages = { renderTournamentRelayRegistrationForm, initTournamentRelayRegisterPageInteractions,
            buildTournamentRegistrationPageItems, renderTournamentRelayRegistrationRow, isCurrentUserRegistration };` + pages.slice(end);
    const view = read("Views/Registrations/Index.cshtml");
    let admin = [...view.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(x => x[1]).find(x => x.includes("const TOURNAMENT_ID"));
    end = admin.lastIndexOf("})();");
    admin = admin.slice(0, end) + `IS_RELAY = true; RELAY_TEAM_SIZE = 4; RELAY_PAIR_COUNT = 2;
        window.__admin = { renderRelayMemberEditors, renderRelayReserveEditors, appendRelayMembers, relayTeamCardHtml };` + admin.slice(end);
    const data = { pages, admin, css: read("wwwroot/pickleball-web/css/pages.css"),
        adminCss: view.match(/<style>([\s\S]*?)<\/style>/)[1].replace(/@@media/g, "@media") };

    const scenario = async data => {
        let checks = 0;
        const failures = [];
        const check = (ok, message) => { checks++; if (!ok) failures.push(message); };
        const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
        const wait = async (fn, message) => {
            for (let i = 0; i < 100; i++) { if (fn()) return; await pause(15); }
            throw new Error("Timeout " + message);
        };
        let node;
        try {
            node = document.createElement("iframe");
            node.style.cssText = "width:390px;height:900px;border:0";
            document.body.appendChild(node);
            const win = node.contentWindow, doc = node.contentDocument;
            doc.head.innerHTML = '<meta charset="utf-8"><style>*{box-sizing:border-box}body{margin:0;font:16px Arial}button,input{font:inherit}' + data.css + '</style>';
            win.eval(data.pages);
            const api = win.__pages;
            const me = { userId: 1, fullName: "Đội trưởng", ratingDouble: 3 };
            for (const size of [4, 6, 8]) {
                doc.body.innerHTML = api.renderTournamentRelayRegistrationForm(me, size);
                check(doc.querySelectorAll("[data-relay-member-search]").length === size - 1 + 4, "Correct main/reserve input count " + size);
                check(!doc.querySelector("details").open, "Reserves start collapsed " + size);
                check(!doc.querySelector("details input").required, "Reserves are optional " + size);
            }
            doc.body.innerHTML = '<div id="test-page" data-tournament-id="37" data-relay-team-size="4">' + api.renderTournamentRelayRegistrationForm(me, 4) + '</div>';
            doc.querySelector("details").open = true;
            const posts = [], confirmations = [];
            win.confirm = text => { confirmations.push(text); return true; };
            win.__request = async (url, options) => {
                if (url.includes("member-search")) {
                    const id = Number(new URL(url, "https://test.invalid").searchParams.get("query"));
                    return { items: [{ userId: id, fullName: "Vận động viên " + id, canSelect: true, ratingDouble: 3.5 }] };
                }
                posts.push(JSON.parse(options.body));
                throw new Error("Test captured submission");
            };
            api.initTournamentRelayRegisterPageInteractions(doc.getElementById("test-page"));
            doc.querySelector("[data-relay-team-name]").value = "Đội thử nghiệm";
            const search = async (key, id) => {
                const input = doc.querySelector(`[data-relay-member-search="${key}"]`);
                input.value = String(id); input.dispatchEvent(new win.Event("input", { bubbles: true }));
                await wait(() => doc.querySelector(`[data-relay-member-position="${key}"][data-relay-member-pick="${id}"]`), "search result");
                return doc.querySelector(`[data-relay-member-position="${key}"][data-relay-member-pick="${id}"]`);
            };
            const submit = async () => { doc.querySelector("form").dispatchEvent(new win.Event("submit", { bubbles: true, cancelable: true })); await pause(20); };
            for (let key = 2; key <= 4; key++) (await search(key, key)).click();
            await submit();
            check(posts.length === 1 && posts[0].members.length === 3 && posts[0].reserveMembers.length === 0, "No reserves remains a valid submission");
            check((await search(5, 2)).disabled, "Main member cannot be selected as reserve");
            for (let key = 5; key <= 8; key++) (await search(key, key)).click();
            await submit();
            check(posts.length === 2 && posts[1].members.length === 3 && posts[1].reserveMembers.length === 4, "Four reserves stay separate in payload");
            check(posts[1].reserveMembers.map(x => x.position).join() === "1,2,3,4", "Reserve positions restart at 1");
            check(confirmations.at(-1).includes("4 thành viên chính thức và 4 thành viên dự bị"), "Confirmation reports both counts");
            doc.querySelector('[data-relay-member-clear="5"]').click();
            await submit();
            check(posts.at(-1).reserveMembers.length === 3, "Clearing a reserve excludes it from submission");
            doc.querySelector('[data-relay-member-search="5"]').value = "Chưa chọn tài khoản";
            const before = posts.length;
            await submit();
            check(posts.length === before && doc.querySelector("[data-register-message]").textContent.includes("dự bị 1"), "Unresolved reserve search is not silently discarded");
            doc.querySelector('[data-relay-member-search="5"]').value = "";
            doc.querySelector('[data-relay-member-clear="2"]').click();
            await submit();
            check(posts.length === before, "Reserves cannot compensate for missing main member");

            const raw = { tournament: { isRelay: true, teamSize: 4 }, successItems: [{ registrationId: 1, regIndex: 1, isRelay: true,
                teamSize: 4, teamName: "Đội kiểm thử", regCode: "37-0001", isReady: true,
                members: Array.from({length: 4}, (_, i) => ({position: i + 1, pairNumber: Math.ceil((i + 1)/2), userId: i + 1, name: "Chính thức " + i})),
                reserveMembers: [{ position: 1, userId: 8, name: "Dự bị tên dài Nguyễn Văn Kiểm Thử Giao Diện", level: 3.5 }, { position: 4, name: "<script>alert(1)<" + "/script>" }] }] };
            const item = api.buildTournamentRegistrationPageItems(raw)[0];
            check(item.members.length === 4 && item.reserveMembers.length === 2, "Public normalization preserves separate arrays");
            check(api.isCurrentUserRegistration(item, { session: { isAuthenticated: true, user: { userId: 8 } } }), "Reserve recognizes own team");
            for (const width of [320, 390, 768, 1280]) {
                node.style.width = width + "px";
                doc.body.innerHTML = api.renderTournamentRelayRegistrationRow(item, {});
                check(doc.querySelectorAll(".tournament-registration-page__relay-pair").length === 2, "Reserves do not add pairs at " + width);
                check(doc.querySelectorAll(".tournament-relay-reserves .tournament-registration-page__relay-member").length === 2, "Public reserve cards at " + width);
                check(!doc.querySelector("script"), "Reserve names are escaped at " + width);
                check(doc.documentElement.scrollWidth <= width + 2, "Public card fits width " + width);
            }

            doc.head.innerHTML += '<style>' + data.adminCss + '</style>';
            doc.body.innerHTML = '<div id="relayCreateMembers"></div><details id="relayCreateReservesBlock"><div id="relayCreateReserves"></div></details>';
            win.eval(data.admin);
            const admin = win.__admin;
            const members = Array.from({length: 4}, (_, i) => ({position: i + 1, userId: i + 1, displayName: "Chính thức " + i}));
            admin.renderRelayMemberEditors("relayCreateMembers", "create", members, false);
            admin.renderRelayReserveEditors("Create", []);
            let fd = new win.FormData(); admin.appendRelayMembers(fd, "create");
            check(fd.get("relayReserveMembersIncluded") === "true" && !fd.has("relayReserveMembers[0].position"), "Admin empty reserves explicitly clear via multipart flag");
            admin.renderRelayReserveEditors("Create", [{ position: 4, userId: 8, displayName: "Dự bị" }]);
            fd = new win.FormData(); admin.appendRelayMembers(fd, "create");
            check(fd.get("relayReserveMembers[0].position") === "4" && fd.get("relayReserveMembers[0].userId") === "8", "Admin submits optional sparse position");
            doc.getElementById("createReserveRelayUser4").value = "1";
            let duplicateBlocked = false;
            try { admin.appendRelayMembers(new win.FormData(), "create"); } catch { duplicateBlocked = true; }
            check(duplicateBlocked, "Admin blocks duplicate across main and reserve");
            for (const width of [320, 390, 768, 1280]) {
                node.style.width = width + "px";
                doc.body.innerHTML = admin.relayTeamCardHtml({ registrationId: 1, relayTeamName: "Đội kiểm thử", relayMembers: members,
                    relayMemberCount: 4, relayRosterComplete: true, relayReserveMembers: [{position: 4, displayName: "Thành viên dự bị có tên dài Nguyễn Văn Kiểm Thử"}] });
                check(doc.querySelectorAll(".relay-reserve-list .relay-pair-member").length === 1, "Admin displays reserve at " + width);
                check(doc.documentElement.scrollWidth <= width + 2, "Admin card fits width " + width);
            }
            document.getElementById("result").textContent = failures.length ? "FAIL: " + failures.join(" | ") : "PASS: " + checks + " relay reserve browser assertions";
        } catch (error) { document.getElementById("result").textContent = "FAIL: " + error.stack + " | " + failures.join(" | "); }
    };
    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-relay-reserve-browser-"));
    try {
        const file = path.join(temp, "test.html");
        fs.writeFileSync(file, '<!doctype html><meta charset="utf-8"><pre id="result">RUNNING</pre><script>window.addEventListener("load",()=>(' + scenario.toString() + ')(' + JSON.stringify(data).replace(/<\/script/gi, "<\\/script") + '));<\/script>');
        const result = await new Promise((resolve, reject) => {
            const child = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", `--user-data-dir=${path.join(temp,"profile")}`,
                "--window-size=1400,1000", "--virtual-time-budget=18000", "--dump-dom", pathToFileURL(file).href], { windowsHide: true, timeout: 45000 });
            let stdout = "";
            child.stdout.on("data", data => stdout += data); child.stderr.resume();
            child.on("error", reject); child.on("close", code => resolve({ code, stdout }));
        });
        const status = result.stdout.match(/<pre id="result"[^>]*>([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(result.code, 0);
        assert.match(status || "Missing browser result", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-relay-reserve-browser-"));
        fs.rmSync(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    }
});
