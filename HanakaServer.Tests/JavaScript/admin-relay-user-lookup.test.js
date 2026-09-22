"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { pathToFileURL } = require("node:url");
const test = require("node:test");

const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("admin relay lookup resolves identities, handles cancellation, and submits with stable loading", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const read = file => fs.readFileSync(path.join(root, file), "utf8");
    const view = read("Views/Registrations/Index.cshtml");
    let script = [...view.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(x => x[1]).find(x => x.includes("const TOURNAMENT_ID"));
    const end = script.lastIndexOf("})();");
    script = script.slice(0, end) + `IS_RELAY = true; RELAY_TEAM_SIZE = 4; RELAY_PAIR_COUNT = 2;
        EDITING_REG = {registrationId: 123, relayVersion: 7};
        load = async () => {};
        window.__lookup = {renderRelayMemberEditors, renderRelayReserveEditors, appendRelayMembers,
            checkRelayUser, resolveRelayFormUsers, initializeRelayRegistrationInteractions,
            cancelRegistrationForm, submitRegistrationForm,
            useStandardMode: () => { IS_RELAY = false; TOURNAMENT_GAME_TYPE = "SINGLE"; }};` + script.slice(end);
    const create = view.slice(view.indexOf('<!-- Modal Create -->'), view.indexOf('<!-- Modal Delete Registration -->'));
    const editStart = view.lastIndexOf('<div class="modal fade"', view.indexOf('id="modalEditTeam"'));
    const edit = view.slice(editStart, view.indexOf('id="formPair"'));
    // Close trailing markup from the next modal; only the edit form is needed.
    const editForm = edit.slice(edit.indexOf('<form id="formEditTeam"'), edit.indexOf('</form>') + 7);
    const data = {
        script, markup: create + '<div id="modalEditTeam">' + editForm + '</div>',
        css: read("wwwroot/css/sb-admin-2.min.css") + view.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll("@@", "@") + read("wwwroot/css/api-loading.css"),
        loader: read("wwwroot/js/api-loading.js"), jquery: read("wwwroot/vendor/jquery/jquery.min.js"),
        bootstrap: read("wwwroot/vendor/bootstrap/js/bootstrap.bundle.min.js")
    };
    const scenario = async data => {
        let checks = 0;
        const check = (value, message) => { checks++; if(!value) throw new Error(message); };
        const pause = () => new Promise(resolve => setTimeout(resolve, 15));
        const wait = async (predicate, message) => {
            for(let i = 0; i < 100; i++){ if(predicate()) return; await pause(); }
            throw new Error("Timeout: " + message);
        };
        try {
            const iframe = document.createElement("iframe");
            iframe.style.cssText = "width:1100px;height:1000px;border:0";
            document.body.appendChild(iframe);
            const win = iframe.contentWindow, doc = win.document;
            doc.head.innerHTML = '<meta name="viewport" content="width=device-width, initial-scale=1"><style>' + data.css + '</style>';
            doc.body.innerHTML = '<div id="pageRoot" data-tournament-id="37"></div>' + data.markup;
            win.eval(data.jquery); win.eval(data.bootstrap);
            const requests = [], writes = [];
            const pending = (target, props) => new Promise((resolve, reject) => target.push({...props, resolve, reject}));
            win.axios = {
                get: (url, config) => pending(requests, {url, config}),
                post: (url, body, config) => pending(writes, {url, body, config, method: "POST"}),
                put: (url, body, config) => pending(writes, {url, body, config, method: "PUT"})
            };
            win.eval(data.loader); win.eval(data.script);
            const api = win.__lookup, byId = id => doc.getElementById(id);
            api.initializeRelayRegistrationInteractions();
            const members = () => Array.from({length: 4}, (_, index) => ({position: index + 1, userId: index + 1, displayName: "Member " + (index + 1)}));
            const render = (mode = "create", list = members(), reserves = []) => {
                const title = mode === "create" ? "Create" : "Edit";
                api.renderRelayMemberEditors('relay' + title + 'Members', mode, list, false);
                api.renderRelayReserveEditors(title, reserves);
                byId('relay' + title + 'TeamName').value = "Lookup test team";
                byId('relay' + title + 'CaptainId').value = "";
                if(mode === "edit") byId("editRegistrationId").value = "123";
            };
            const input = (prefix, pos) => byId(`${prefix}RelayUser${pos}`);
            const button = (prefix, pos) => byId(`${prefix}RelayUserCheck${pos}`);
            const status = (prefix, pos) => byId(`${prefix}RelayUserInfo${pos}`);
            const type = (prefix, pos, value) => {
                input(prefix, pos).value = value;
                input(prefix, pos).dispatchEvent(new win.Event("input", {bubbles: true}));
            };
            const lookup = (prefix, pos, query) => {
                type(prefix, pos, query); button(prefix, pos).click(); return requests.at(-1);
            };
            const user = (id, name = "Player " + id) => ({userId: id, fullName: name, ratingSingle: 3, ratingDouble: 3.5, phone: "0912345678"});
            const answer = async (request, items) => { request.resolve({data: {items}}); await pause(); };
            const submit = mode => byId(mode === "edit" ? "formEditTeam" : "formCreate").dispatchEvent(new win.Event("submit", {bubbles: true, cancelable: true}));
            const fd = prefix => { const form = new win.FormData(); api.appendRelayMembers(form, prefix); return form; };
            render(); render("edit");
            // Make the real create form visible without triggering unrelated page loading.
            byId("modalCreate").classList.add("show"); byId("modalCreate").style.display = "block";
            check(input("create", 1).type === "text", "Phone input preserves + and leading zero");
            check(fd("edit").get("relayMembers[0].userId") === "1", "Existing edit selection is ready without lookup");

            const width = button("create", 1).getBoundingClientRect().width;
            let request = lookup("create", 1, "+84 912.345.678");
            check(button("create", 1).disabled && button("create", 1).classList.contains("hanaka-api-button-busy"), "Lookup uses real shared button spinner");
            check(!button("create", 2).disabled, "Other player buttons stay available");
            check(status("create", 1).textContent.includes("Đang tìm"), "Loading has visible text");
            check(win.HanakaApiLoading.foregroundCount === 0, "No global lookup overlay");
            check(Math.abs(button("create", 1).getBoundingClientRect().width - width) < 1, "Lookup button keeps its width");
            check(request.config.params.query === "+84 912.345.678" && request.config.timeout === 15000, "Phone is sent intact with a timeout");
            input("create", 1).dispatchEvent(new win.KeyboardEvent("keydown", {key: "Enter", bubbles: true, cancelable: true}));
            check(requests.length === 1 && writes.length === 0, "Enter reuses pending lookup and does not submit form");
            await answer(request, [user(42)]);
            check(fd("create").get("relayMembers[0].userId") === "42", "Phone resolves to User ID in multipart payload");
            check(input("create", 1).value === "+84 912.345.678", "Typed phone remains visible after selection");
            check(status("create", 1).textContent.includes("ID 42") && byId("createRelayName1").disabled, "Selected identity is clear and guest field cannot overwrite it");
            check(!button("create", 1).disabled && win.HanakaApiLoading.activeCount === 0, "Lookup completes loading");

            const old = lookup("create", 2, "0911111111");
            const fresh = lookup("create", 2, "0922222222");
            check(old.config.signal.aborted, "Changing text aborts old request");
            await answer(old, [user(90)]);
            check(button("create", 2).disabled && status("create", 2).textContent.includes("Đang tìm"), "Old response cannot end fresh loading or select an old account");
            await answer(fresh, [user(43)]);
            check(fd("create").get("relayMembers[1].userId") === "43", "Newest response selects account");

            request = lookup("create", 3, "42"); await answer(request, [user(42)]);
            let blocked = false; try { fd("create"); } catch(error) { blocked = error.message.includes("trùng"); }
            check(blocked, "ID and phone cannot register the same account twice");
            render();
            request = lookup("createReserve", 1, "0912345678"); await answer(request, [user(1)]);
            blocked = false; try { fd("create"); } catch(error) { blocked = error.message.includes("trùng"); }
            check(blocked, "Duplicate identity across main and reserve is blocked");

            request = lookup("create", 1, "0999999999"); await answer(request, []);
            check(status("create", 1).textContent.includes("Không tìm thấy") && !button("create", 1).disabled, "No-match error releases button");
            request = lookup("create", 1, "0999999999"); request.reject({code: "ECONNABORTED"}); await pause();
            check(status("create", 1).textContent.includes("quá lâu") && win.HanakaApiLoading.activeCount === 0, "Timeout releases loading and offers retry");
            const count = requests.length; lookup("create", 1, "invalid");
            check(requests.length === count && status("create", 1).textContent.includes("hợp lệ"), "Invalid query is rejected before request");

            render();
            request = lookup("create", 1, "0912345678");
            await answer(request, [user(42, '<img src=x onerror="window.bad=true">'), user(43)]);
            check(status("create", 1).querySelectorAll("[data-relay-user-pick]").length === 2 && !status("create", 1).querySelector("img"), "Ambiguous results are escaped and require selection");
            const beforeWrites = writes.length;
            submit("create"); await pause();
            check(writes.length === beforeWrites && byId("createErr").textContent.includes("chọn"), "Unselected ambiguity blocks registration");
            status("create", 1).querySelector('[data-relay-user-pick="1"]').click();
            check(fd("create").get("relayMembers[0].userId") === "43", "Explicit choice submits selected ID");

            render();
            type("create", 1, "0912345678");
            const priorRequests = requests.length;
            submit("create"); submit("create"); await pause();
            check(requests.length === priorRequests + 1 && button("create", 1).disabled, "Submit automatically resolves pending identity once");
            check(byId("btnCreateSubmit").disabled && byId("createSubmitStatus").textContent.includes("Đang kiểm tra"), "Submit displays checking loading");
            request = requests.at(-1);
            await answer(request, [user(42)]);
            await wait(() => writes.length > beforeWrites, "Create POST");
            let write = writes.at(-1);
            check(write.body.get("relayMembers[0].userId") === "42" && !write.body.has("relayCaptainUserId"), "Auto lookup sends resolved account and captain stays optional");
            check(byId("createSubmitStatus").textContent.includes("Đang lưu") && input("create", 1).disabled, "Saving freezes submitted data with visible status");
            const closing = win.jQuery.Event("hide.bs.modal"); win.jQuery("#modalCreate").trigger(closing);
            check(closing.isDefaultPrevented(), "Modal cannot close while saving");
            submit("create"); check(writes.at(-1) === write, "Duplicate submission cannot issue another write");
            const conflictMessage = 'Các tài khoản đã thuộc đội khác trong giải:\n• Nguyễn Văn A (User ID #42) — đội “Team A”; đội hình chính, vị trí 1.\n• Nguyễn Văn B (User ID #43) — đội “<img src=x onerror=window.bad=true>”; dự bị, vị trí 3.';
            write.reject({response: {data: {message: conflictMessage}}}); await pause();
            check(!byId("btnCreateSubmit").disabled && !input("create", 1).disabled && byId("createErr").textContent === conflictMessage, "Save error restores controls and lists every conflicting player and team");
            check(!byId("createErr").querySelector("img") && !win.bad, "Conflict details render names as text, never HTML");
            check(["createErr", "editErr"].every(id => win.getComputedStyle(byId(id)).whiteSpace === "pre-line" && win.getComputedStyle(byId(id)).overflowWrap === "anywhere"), "Both admin forms preserve conflict lines and wrap long names");
            check(input("create", 1).value === "0912345678" && win.HanakaApiLoading.activeCount === 0, "Failed save preserves phone and releases loading");

            render(); type("create", 1, "0912345678"); submit("create"); await pause();
            request = requests.at(-1); const writeCount = writes.length;
            win.jQuery("#modalCreate").trigger("hide.bs.modal");
            check(request.config.signal.aborted && !byId("btnCreateSubmit").disabled, "Close during lookup cancels work and submit loading immediately");
            render(); const reopened = lookup("create", 1, "0988888888");
            await answer(request, [user(90)]);
            check(writes.length === writeCount && button("create", 1).disabled, "Closed-form result cannot write or disturb reopened form");
            await answer(reopened, [user(45)]);
            check(fd("create").get("relayMembers[0].userId") === "45", "Reopened form keeps new selection");

            render("edit", members(), [{position: 4, displayName: "Reserve guest"}]);
            type("editReserve", 4, "+84912345678"); submit("edit"); await pause();
            await answer(requests.at(-1), [user(46)]);
            await wait(() => writes.length > writeCount, "Edit PUT"); write = writes.at(-1);
            check(write.method === "PUT" && write.url.endsWith("/123/players"), "Edit uses existing update endpoint");
            check(write.body.get("relayReserveMembers[0].userId") === "46" && write.body.get("relayReserveMembers[0].position") === "4", "Reserve phone is mapped without changing its position");
            check(write.body.get("relayExpectedVersion") === "7" && write.body.get("relayReserveMembersIncluded") === "true", "Edit preserves concurrency and reserve replacement contract");
            write.resolve({data: {}}); await pause();
            check(!byId("btnEditSubmit").disabled && win.HanakaApiLoading.activeCount === 0, "Successful edit releases all loading");

            const guests = members().map(member => ({position: member.position, displayName: "Guest " + member.position}));
            render("create", guests);
            const guestRequests = requests.length;
            submit("create"); await pause();
            write = writes.at(-1);
            check(requests.length === guestRequests && write.body.get("relayMembers[0].displayName") === "Guest 1" && !write.body.has("relayMembers[0].userId"), "Guest registration does not require lookup");
            write.reject(new Error("Guest save test")); await pause();
            for(const width of [320, 390, 768, 1280]){
                iframe.style.width = width + "px";
                const card = byId("createRelayUser1").closest(".relay-athlete-editor");
                check(card.scrollWidth <= card.clientWidth + 2, "Lookup editor fits at " + width);
            }
            request = lookup("create", 1, "0912345678");
            win.dispatchEvent(new win.Event("pagehide"));
            check(request.config.signal.aborted && win.HanakaApiLoading.activeCount === 0, "Page exit cancels lookup and clears loading");

            // The save handler is shared with standard registrations: their payload
            // must not acquire the relay lookup requirement.
            api.useStandardMode();
            byId("gameType").value = "SINGLE";
            byId("p1Type").value = "GUEST";
            byId("p1GuestName").value = "Standard guest";
            byId("p1GuestLevel").value = "2.5";
            const standardRequests = requests.length, standardWrites = writes.length;
            submit("create"); await pause();
            check(writes.length === standardWrites + 1 && requests.length === standardRequests, "Standard registration submits without relay lookup");
            write = writes.at(-1);
            check(write.body.get("gameType") === "SINGLE" && write.body.get("player1Name") === "Standard guest" && !write.body.has("relayMembers[0].userId"), "Standard registration keeps its existing contract");
            write.reject(new Error("Standard save test")); await pause();
            check(!byId("btnCreateSubmit").disabled && win.HanakaApiLoading.activeCount === 0, "Standard save also restores loading");
            document.getElementById("result").textContent = "PASS: " + checks + " admin lookup browser assertions";
        } catch(error) { document.getElementById("result").textContent = "FAIL after " + checks + ": " + error.stack; }
    };
    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-admin-lookup-"));
    try {
        const file = path.join(temp, "test.html");
        const json = JSON.stringify(data).replace(/<\/script/gi, "<\\/script");
        fs.writeFileSync(file, '<!doctype html><meta charset="utf-8"><pre id="result">RUNNING</pre><script>window.addEventListener("load",()=>(' + scenario.toString() + ')(' + json + '));<\/script>');
        const result = await new Promise((resolve, reject) => {
            const child = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", `--user-data-dir=${path.join(temp,"profile")}`,
                "--window-size=1400,1100", "--virtual-time-budget=18000", "--dump-dom", pathToFileURL(file).href], {windowsHide: true, timeout: 45000});
            let stdout = "";
            child.stdout.on("data", chunk => stdout += chunk); child.stderr.resume();
            child.on("error", reject); child.on("close", code => resolve({code, stdout}));
        });
        const status = result.stdout.match(/<pre id="result"[^>]*>([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(result.code, 0);
        assert.match(status || "Missing browser output", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-admin-lookup-"));
        fs.rmSync(temp, {recursive: true, force: true, maxRetries: 10, retryDelay: 100});
    }
});
