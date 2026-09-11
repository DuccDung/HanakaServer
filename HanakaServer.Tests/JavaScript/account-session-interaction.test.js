"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { spawn } = require("node:child_process");
const { pathToFileURL } = require("node:url");

const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("account and password screens preserve identity when session requests fail or abort", { skip: !browser, timeout: 60000 }, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer");
    const read = file => fs.readFileSync(path.join(root, file), "utf8");
    const markup = name => {
        const view = read(`Views/PickleballWeb/${name}.cshtml`);
        return view.slice(view.indexOf('<div class="'), view.indexOf("@section Scripts"));
    };
    const data = {
        account: markup("Account"), password: markup("ChangePassword"),
        script: read("wwwroot/pickleball-web/js/account.js"),
        session: read("wwwroot/pickleball-web/js/web-session.js"),
        css: read("wwwroot/pickleball-web/css/account.css")
    };

    const scenario = async data => {
        const failures = [];
        let checks = 0;
        const check = (ok, message) => { checks++; if (!ok) failures.push(message); };
        const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
        const wait = async (predicate, message) => {
            for (let i = 0; i < 150; i++) { if (predicate()) return; await pause(10); }
            throw new Error("Timeout: " + message);
        };
        const install = function (mode) {
            window.__mode = mode;
            window.__reads = 0;
            window.__pending = [];
            window.__alerts = [];
            window.__errors = [];
            window.alert = message => window.__alerts.push(message);
            window.addEventListener("error", event => window.__errors.push(event.message));
            window.addEventListener("unhandledrejection", event => window.__errors.push(String(event.reason)));
            const user = { userId: 7, fullName: "Người kiểm thử", email: "test@example.invalid", phone: "0900000000", verified: false, ratingSingle: 3.25, ratingDouble: 3.75 };
            window.__user = user;
            const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
            window.fetch = async url => {
                if (String(url) === "/api/web-auth/me") {
                    window.__reads++;
                    if (window.__mode === "failure") return json({}, 500);
                    if (window.__mode === "abort") throw new DOMException("Aborted", "AbortError");
                    if (window.__mode === "guest") return json({ isAuthenticated: false });
                    if (window.__mode === "hold") return new Promise(resolve => window.__pending.push(body => resolve(json(body))));
                    return json({ isAuthenticated: true, user });
                }
                if (String(url) === "/api/users/me") return json(user);
                return json({ items: [] });
            };
        };

        async function frame(kind, mode) {
            const node = document.createElement("iframe");
            node.style.cssText = "width:390px;height:900px;border:0";
            node.srcdoc = '<!doctype html><meta charset="utf-8"><style>' + data.css + '</style>' + data[kind] +
                '<script>(' + install.toString() + ')(' + JSON.stringify(mode) + ');' + data.session + '\n' + data.script + '<\/script>';
            document.body.appendChild(node);
            await wait(() => node.contentWindow && node.contentWindow.__reads > 0, "initial session request");
            return { node, win: node.contentWindow, doc: node.contentDocument, q: selector => node.contentDocument.querySelector(selector) };
        }

        try {
            let current = await frame("account", "failure");
            await wait(() => !current.q("[data-account-error]").hidden, "account error feedback");
            check(current.q("[data-account-verified-text]").textContent.includes("Chưa kiểm tra"), "Initial account failure displays unknown session");
            check(current.q("[data-account-user-id]").value === "", "Unknown session has no invented identity");
            check(current.q("[data-account-update]").disabled, "Unknown session cannot update account");
            check(!current.win.__alerts.length && !current.win.__errors.length, "Initial account failure has no login prompt or unhandled rejection");
            current.node.remove();

            current = await frame("password", "failure");
            await wait(() => !current.q("[data-change-error]").hidden, "password session error feedback");
            check(current.q("[data-change-profile-name]").textContent === "Chưa tải được tài khoản", "Password screen does not call a failed session signed out");
            check(current.q("[data-change-submit]").disabled, "Unknown session cannot submit a password change");
            check(!current.win.__alerts.length && !current.win.__errors.length, "Password failure does not redirect or throw");
            current.node.remove();

            current = await frame("account", "authenticated");
            await wait(() => !current.q("[data-account-update]").disabled, "authenticated account");
            const field = current.q("[data-account-full-name]");
            field.value = "Bản nháp đang sửa";
            field.dispatchEvent(new current.win.Event("input", { bubbles: true }));
            current.win.__mode = "failure";
            current.win.dispatchEvent(new current.win.Event("focus"));
            await wait(() => !current.q("[data-account-error]").hidden, "refresh failure feedback");
            check(current.q("[data-account-user-id]").value === "7", "A failed refresh preserves the confirmed identity");
            check(field.value === "Bản nháp đang sửa", "A failed refresh preserves the edited form");
            check(current.q("[data-account-verified-text]").textContent !== "Chưa đăng nhập", "A failed refresh does not show signed out");

            current.win.__mode = "abort";
            const beforeAbort = current.win.__reads;
            current.win.dispatchEvent(new current.win.Event("focus"));
            await wait(() => current.win.__reads > beforeAbort, "aborted refresh");
            await pause(20);
            check(current.q("[data-account-user-id]").value === "7" && field.value === "Bản nháp đang sửa", "An aborted refresh preserves identity and draft");
            check(!current.win.__alerts.length && !current.win.__errors.length, "Abort causes no login prompt or unhandled rejection");

            current.win.__mode = "authenticated";
            current.win.dispatchEvent(new current.win.Event("focus"));
            await wait(() => current.q("[data-account-error]").hidden, "successful refresh clears feedback");
            check(current.q("[data-account-user-id]").value === "7", "Account recovers on the next successful request");

            current.win.__mode = "hold";
            current.win.dispatchEvent(new current.win.Event("focus"));
            current.win.dispatchEvent(new current.win.Event("focus"));
            await wait(() => current.win.__pending.length === 2, "overlapping refreshes");
            current.win.__pending[1]({ isAuthenticated: false });
            await wait(() => current.q("[data-account-user-id]").value === "", "confirmed session expiry");
            current.win.__pending[0]({ isAuthenticated: true, user: current.win.__user });
            await pause(20);
            check(current.q("[data-account-verified-text]").textContent === "Chưa đăng nhập", "Confirmed expiry clears authentication");
            check(current.q("[data-account-user-id]").value === "" && current.q("[data-account-update]").disabled, "An older authenticated response cannot reverse a newer logout");
            check(!current.win.__errors.length, "Overlapping requests leave no unhandled rejection");
            current.node.remove();

            current = await frame("password", "authenticated");
            await wait(() => current.q("[data-change-profile-name]").textContent === "Người kiểm thử", "authenticated password screen");
            current.q("[data-change-new-password]").value = "TemporaryDraft123!";
            current.win.__mode = "failure";
            current.win.dispatchEvent(new current.win.Event("focus"));
            await wait(() => !current.q("[data-change-error]").hidden, "password refresh failure");
            check(current.q("[data-change-profile-name]").textContent === "Người kiểm thử", "Password refresh failure preserves identity");
            check(current.q("[data-change-new-password]").value === "TemporaryDraft123!", "Password refresh failure preserves the form");
            check(!current.win.__alerts.length && !current.win.__errors.length, "Password refresh failure has no login redirect or unhandled rejection");
            current.node.remove();

            document.getElementById("result").textContent = failures.length ? "FAIL: " + failures.join(" | ") : "PASS: " + checks + " account/session browser assertions";
        } catch (error) {
            document.getElementById("result").textContent = "FAIL: " + error.stack + " | " + failures.join(" | ");
        }
    };

    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-account-session-"));
    try {
        const file = path.join(temp, "test.html");
        fs.writeFileSync(file, '<!doctype html><meta charset="utf-8"><pre id="result">RUNNING</pre><script>window.addEventListener("load",()=>(' +
            scenario.toString() + ')(' + JSON.stringify(data).replace(/<\/script/gi, "<\\/script") + '));<\/script>');
        const result = await new Promise((resolve, reject) => {
            const child = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                `--user-data-dir=${path.join(temp, "profile")}`, "--window-size=430,1000", "--virtual-time-budget=15000", "--dump-dom", pathToFileURL(file).href],
            { windowsHide: true, timeout: 45000 });
            let stdout = "";
            child.stdout.on("data", data => stdout += data);
            child.stderr.resume();
            child.on("error", reject);
            child.on("close", code => resolve({ code, stdout }));
        });
        const status = result.stdout.match(/<pre id="result"[^>]*>([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(result.code, 0);
        assert.match(status || "Missing browser result", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-account-session-"));
        fs.rmSync(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    }
});
