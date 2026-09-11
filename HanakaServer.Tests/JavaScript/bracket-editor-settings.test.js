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
].find(p => fs.existsSync(p));

test("editor loads and saves draft settings while published settings remain immutable", { skip: !browser, timeout: 60000 }, async () => {
    const repository = path.resolve(__dirname, "../..");
    const view = fs.readFileSync(path.join(repository, "HanakaServer/Views/BracketTemplates/Editor.cshtml"), "utf8");
    const markup = view.slice(view.indexOf("<main"), view.indexOf("@section Scripts"))
        .replaceAll("@templateId", "8").replaceAll("@versionId", "20").replaceAll("@participantMode", "RELAY_TEAM")
        .replaceAll("@section Modals {", "");
    const script = fs.readFileSync(path.join(repository, "HanakaServer/wwwroot/js/admin-bracket-template-editor.js"), "utf8");
    const scenario = async (markup, script) => {
        const result = document.getElementById("result");
        let checks = 0;
        const check = (value, message) => { if (!value) throw Error(message); checks++; };
        const wait = async fn => {
            for (let n = 0; n < 150; n++) { if (fn()) return; await new Promise(r => setTimeout(r, 10)); }
            throw Error("Timed out: " + document.querySelector("[data-editor-error]")?.textContent);
        };
        try {
            for (const status of ["DRAFT", "PUBLISHED"]) {
                localStorage.clear();
                document.getElementById("fixture").innerHTML = markup;
                let graph = { bracketTemplateId: 8, bracketTemplateVersionId: 20, versionNumber: 2, status,
                    minimumTeams: 2, seedCapacity: 8, allowBye: true, defaultSeedingMethod: "REGISTRATION_ORDER", rowVersion: "AAAA", rounds: [] };
                const calls = [];
                window.jQuery = () => { const chain = { on: () => chain, one: () => chain, modal: () => chain }; return chain; };
                window.fetch = async (url, options = {}) => {
                    const body = options.body ? JSON.parse(options.body) : null;
                    calls.push({ url, method: options.method, body });
                    if (url.endsWith("/validate-draft")) return new Response(JSON.stringify({ data: { isValid: true, errorCount: 0, warningCount: 0, issues: [] } }));
                    if (options.method === "PUT") { graph = { ...graph, ...body, rowVersion: "BBBB" }; return new Response(JSON.stringify({ data: graph })); }
                    return new Response(JSON.stringify(graph));
                };
                (0, eval)(script);
                const root = document.getElementById("bracketTemplateEditor");
                const capacity = document.getElementById("bteSeedCapacity");
                await wait(() => capacity.value === "8" && !root.querySelector("[data-action='validate']").disabled);
                check(!root.querySelector("[data-version-settings]").classList.contains("d-none"), "Settings are visible");
                check(document.getElementById("bteMinimumTeams").value === "2", "Minimum teams loaded from graph");
                check(capacity.disabled === (status === "PUBLISHED"), "Published capacity is locked");
                if (status === "DRAFT") {
                    capacity.value = "16";
                    document.getElementById("bteMinimumTeams").value = "8";
                    document.getElementById("bteSeedingMethod").value = "RANDOM";
                    document.getElementById("bteAllowBye").checked = false;
                    capacity.dispatchEvent(new Event("input", { bubbles: true }));
                    root.querySelector("[data-action='save']").click();
                    await wait(() => calls.some(x => x.method === "PUT"));
                    const saved = calls.find(x => x.method === "PUT").body;
                    check(saved.seedCapacity === 16 && saved.minimumTeams === 8, "Changed range reaches save API");
                    check(saved.defaultSeedingMethod === "RANDOM" && saved.allowBye === false, "Seeding and BYE choices reach save API");
                    await wait(() => root.querySelector("[data-save-state]").textContent.includes("Đã lưu"));
                    check(capacity.value === "16", "Saved capacity survives rendering");
                } else {
                    capacity.value = "16"; // Simulate altering a disabled input with developer tools.
                    root.querySelector("[data-action='validate']").click();
                    await wait(() => calls.filter(x => x.url.endsWith("/validate-draft")).length > 1);
                    check(calls.filter(x => x.url.endsWith("/validate-draft")).at(-1).body.seedCapacity === 8, "Published graph ignores altered controls");
                }
            }
            result.textContent = `PASS: ${checks} editor assertions`;
        } catch (error) { result.textContent = `FAIL: ${error.stack}`; }
    };
    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-bracket-editor-"));
    try {
        const file = path.join(temp, "test.html");
        fs.writeFileSync(file, `<!doctype html><meta charset="utf-8"><style>.d-none{display:none}</style><pre id="result">RUNNING</pre><div id="fixture"></div><script>(${scenario.toString()})(${JSON.stringify(markup)},${JSON.stringify(script)});</script>`);
        const output = await new Promise((resolve, reject) => {
            const process = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                `--user-data-dir=${path.join(temp, "profile")}`, "--virtual-time-budget=10000", "--dump-dom", pathToFileURL(file).href], { windowsHide: true, timeout: 45000 });
            let stdout = "";
            process.stdout.on("data", d => stdout += d);
            process.stderr.resume();
            process.on("error", reject);
            process.on("close", code => resolve({ code, stdout }));
        });
        const status = output.stdout.match(/<pre id="result">([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(output.code, 0);
        assert.match(status || "No browser result", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-bracket-editor-"));
        fs.rmSync(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    }
});
