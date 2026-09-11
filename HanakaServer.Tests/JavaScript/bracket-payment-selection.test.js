"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { spawn } = require("node:child_process");
const test = require("node:test");

const repositoryRoot = path.resolve(__dirname, "../..");
const browserPath = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Google/Chrome/Application/chrome.exe"
].find(candidate => fs.existsSync(candidate));

test("bracket payment selection works in the browser from list through preview and apply", {
    skip: !browserPath && "Set HANAKA_TEST_BROWSER to a Chromium browser executable.",
    timeout: 60000
}, async () => {
    const tempDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-bracket-payment-"));
    try {
        const view = fs.readFileSync(path.join(repositoryRoot, "HanakaServer/Views/TournamentBracketSetup/Index.cshtml"), "utf8");
        const markup = view.slice(view.indexOf("<main"), view.indexOf("@section Scripts"))
            .replace('@(((bool)ViewBag.RegistrationLocked).ToString().ToLowerInvariant())', "true")
            .replaceAll("@tournamentId", "37").replaceAll("@ViewBag.TournamentTitle", "Giải thử đội tiếp sức")
            .replaceAll("@tournamentStartAtValue", "2026-12-01T08:00")
            .replaceAll("@tournamentLocation", "Sân thử")
            .replaceAll("@section Modals {", "").replace(/^}\s*$/gm, "");
        const script = fs.readFileSync(path.join(repositoryRoot, "HanakaServer/wwwroot/js/admin-tournament-bracket-setup.js"), "utf8");
        const browserTest = async function (markup, script) {
            const output = document.getElementById("result");
            let checks = 0;
            const check = (condition, message) => { if (!condition) throw Error(message); checks++; };
            const waitFor = async predicate => {
                for (let i = 0; i < 100; i++) {
                    if (predicate()) return;
                    await new Promise(resolve => setTimeout(resolve, 10));
                }
                throw Error("Timed out waiting for UI");
            };
            try {
                async function mount({ fee = 100, count = 10, paid = 3, mode = "RELAY_TEAM", active = null, invalidPreview = false, unresolved = false } = {}) {
                    document.getElementById("fixture").innerHTML = markup;
                    const calls = [];
                    let failTemplates = false;
                    let failPreview = false;
                    const teams = Array.from({ length: count }, (_, i) => ({
                        registrationId: i + 1, teamName: `Đội tiếp sức ${i + 1}`, regCode: `37-${i + 1}`,
                        paid: i < paid, player1Name: `VĐV ${i + 1}`, player2Name: "Bạn đánh",
                        registeredAt: "2026-09-06T14:18:00", points: 0,
                        ...(mode === "RELAY_TEAM" ? { relay: { teamSize: 6, members: Array.from({ length: 6 }, (_, p) => ({ position: p + 1, displayName: `VĐV ${i + 1}.${p + 1}` })) } } : {})
                    }));
                    const template = { currentPublishedVersionId: 7, templateName: "Mẫu thử", templateCode: "TEST", currentVersionNumber: 1,
                        minimumTeams: 2, seedCapacity: 16, roundCount: 4, groupCount: 4, matchCount: 15, participantMode: mode, defaultSeedingMethod: "REGISTRATION_ORDER" };
                    window.jQuery = () => {
                        const chain = { on: () => chain, one: () => chain, modal: () => chain };
                        return chain;
                    };
                    window.fetch = async (url, options = {}) => {
                        const route = new URL(url, "https://hanaka.test");
                        const body = options.body ? JSON.parse(options.body) : null;
                        calls.push({ path: route.pathname, search: route.search, body });
                        let payload;
                        if (route.pathname.endsWith("/eligible-registrations")) payload = { data: teams, registrationFeeAmount: fee };
                        else if (route.pathname.endsWith("/templates")) {
                            if (failTemplates) return new Response(JSON.stringify({ message: "Lỗi tải template" }), { status: 503 });
                            const selected = teams.filter(team => route.searchParams.get("excludeUnpaidTeams") !== "true" || fee === 0 || team.paid);
                            payload = { items: [{ ...template, eligibleTeamCount: selected.length, isApplicable: selected.length >= 2 && selected.length <= 16 }] };
                        } else if (route.pathname.endsWith("/application-history")) payload = { items: [] };
                        else if (route.pathname.endsWith("/application")) payload = { item: active };
                        else if (route.pathname.endsWith("/preview")) {
                            if (failPreview) return new Response(JSON.stringify({ code: "GRAPH_INVALID", message: "Mẫu lỗi",
                                issues: [{ severity: "ERROR", message: "Vòng 1, Trận 2, Đội 1: chưa gán số vị trí đội đầu vào." }] }), { status: 400 });
                            const selected = teams.filter(team => !body.excludeUnpaidTeams || fee === 0 || team.paid);
                            payload = { data: { bracketTemplateVersionId: 7, templateName: "Mẫu thử", versionNumber: 1,
                                participantMode: mode, seedingMethod: body.seedingMethod, randomSeed: 123,
                                excludeUnpaidTeams: body.excludeUnpaidTeams, excludedUnpaidRegistrationCount: count - selected.length,
                                eligibleRegistrationCount: selected.length, seedCapacity: 16, byeCount: body.fillMissingWithVirtualTeams ? 0 : 16 - selected.length,
                                virtualTeamCount: body.fillMissingWithVirtualTeams ? 16 - selected.length : 0,
                                previewHash: "PREVIEW", matchCount: 15, roundCount: 4, groupCount: 4,
                                validation: { issues: [], errorCount: 0 }, seeds: selected.map((team, i) => ({ ...team, seedNumber: i + 1 })), rounds: [] } };
                            if (invalidPreview) payload.data.validation = { isValid: false, errorCount: 1, issues: [
                                { severity: "ERROR", code: "INITIAL_POSITION_CAPACITY_MISMATCH", message: "Mẫu khai báo 8 đội nhưng có 16 vị trí." }] };
                            if (unresolved) payload.data.rounds = [{ roundLabel: "Vòng 1", groups: [{ groupName: "Nhánh 1", matches: [
                                { matchKey: "M1", slots: [{ sourceType: "SEED", seedNumber: null, registrationId: null, displayText: "Chưa xác định" }] }] }] }];
                        } else if (route.pathname.includes("/api/admin/referees/")) payload = { userId: 90, fullName: "Trọng tài", refereeType: "MAIN" };
                        else if (route.pathname.endsWith("/apply")) payload = { data: { templateName: "Mẫu thử", versionNumber: 1,
                            seeds: teams.filter(team => !body.excludeUnpaidTeams || fee === 0 || team.paid), eligibleRegistrationCount: 3,
                            generatedRoundCount: 4, generatedGroupCount: 4, generatedMatchCount: 15 } };
                        else throw Error(`Unexpected API: ${url}`);
                        return new Response(JSON.stringify(payload), { headers: { "Content-Type": "application/json" } });
                    };
                    (0, eval)(script);
                    const root = document.getElementById("tournamentBracketSetup");
                    const option = document.getElementById("excludeUnpaidTeams");
                    const rows = () => root.querySelectorAll("[data-eligible-list] .tbs-team-row");
                    const ready = () => root.querySelector("[data-setup-loading]").classList.contains("d-none");
                    await waitFor(() => ready() && rows().length === count);
                    const changeFilter = async value => {
                        option.checked = value;
                        option.dispatchEvent(new Event("change", { bubbles: true }));
                        await waitFor(ready);
                    };
                    const preview = async () => {
                        document.querySelector("#teamPlacementMethodForm input[value='REGISTRATION_ORDER']").click();
                        document.getElementById("teamPlacementConfirmForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
                        await waitFor(() => !root.querySelector("[data-action='show-preview']").disabled && root.querySelectorAll(".tbs-seed-row").length > 0);
                    };
                    return { root, option, rows, calls, changeFilter, preview, failTemplates: () => { failTemplates = true; }, failPreview: () => { failPreview = true; } };
                }

                let ui = await mount();
                check(!ui.option.checked, "Payment exclusion defaults off");
                check(ui.rows().length === 10, "All ten paid/unpaid teams are listed");
                check(ui.root.querySelectorAll(".tbs-paid.is-unpaid").length === 7, "Seven unpaid badges are visible");
                check(ui.root.querySelector("[data-eligible-count]").textContent === "10", "Default selection contains all ten teams");
                await ui.preview();
                check(ui.calls.find(call => call.path.endsWith("/preview")).body.excludeUnpaidTeams === false, "Default preview includes unpaid teams");
                document.getElementById("confirmBracketApply").checked = true;
                await ui.changeFilter(true);
                check(ui.rows().length === 10, "Excluded teams remain visible for review");
                check(ui.root.querySelectorAll(".tbs-team-row.is-excluded").length === 7, "Only unpaid teams are excluded");
                check(ui.root.querySelector("[data-eligible-count]").textContent === "3", "Three paid teams selected");
                check(!document.getElementById("confirmBracketApply").checked, "Changing filter clears confirmation");
                check(ui.root.querySelectorAll(".tbs-seed-row").length === 0, "Changing filter clears old placements");
                check(ui.root.querySelector("[data-action='apply']").disabled, "Old preview cannot be applied");
                check(ui.calls.some(call => call.path.endsWith("/templates") && call.search.includes("excludeUnpaidTeams=true")), "Template capacity uses payment filter");
                await ui.changeFilter(false);
                check(ui.root.querySelector("[data-eligible-count]").textContent === "10", "Unchecking restores all ten teams");
                await ui.changeFilter(true);
                await ui.preview();
                const previews = ui.calls.filter(call => call.path.endsWith("/preview"));
                check(previews.at(-1).body.excludeUnpaidTeams === true, "Filtered preview explicitly excludes unpaid teams");
                ui.root.querySelector("[data-action='show-preview']").click();
                check(ui.root.querySelector("[data-preview-payment-summary]").textContent.includes("7 đội bị loại"), "Confirmation explains excluded count");
                document.getElementById("applyRefereeUserId").value = "90";
                document.getElementById("applyMatchStartAt").value = "2026-12-01T08:00";
                document.getElementById("applyMatchAddress").value = "Sân thử";
                document.getElementById("applyBracketDetailsForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
                await waitFor(() => ui.calls.some(call => call.path.endsWith("/apply")));
                check(ui.calls.find(call => call.path.endsWith("/apply")).body.excludeUnpaidTeams === true, "Apply keeps preview's payment choice");
                await waitFor(() => !ui.root.querySelector("[data-active-application]").classList.contains("d-none"));
                check(!ui.root.querySelector("[data-active-new-registrations]").textContent.includes("đăng ký mới"), "Intentionally excluded teams are not mislabeled new registrations");

                ui = await mount({ fee: 0, paid: 0 });
                check(ui.option.disabled, "Payment filter is disabled for free tournaments");
                check(ui.root.querySelector("[data-eligible-count]").textContent === "10", "Free tournament keeps all teams");
                check(ui.root.querySelectorAll(".tbs-paid.is-unpaid").length === 0, "Free teams are not labeled unpaid");

                ui = await mount({ mode: "STANDARD" });
                await ui.changeFilter(true);
                await ui.preview();
                document.getElementById("createVirtualBracketTeams").click();
                await waitFor(() => ui.calls.some(call => call.body?.fillMissingWithVirtualTeams));
                check(ui.calls.find(call => call.body?.fillMissingWithVirtualTeams).body.excludeUnpaidTeams === true, "Virtual-team preview retains payment choice");
                await waitFor(() => !document.getElementById("createVirtualBracketTeams").disabled);
                ui.failTemplates();
                await ui.changeFilter(false);
                check(ui.root.querySelector("[data-action='next']").disabled, "Failed reload prevents use of stale template data");

                for (const options of [{ invalidPreview: true }, { unresolved: true }]) {
                    ui = await mount(options);
                    await ui.preview();
                    ui.root.querySelector("[data-action='show-preview']").click();
                    const confirmation = document.getElementById("confirmBracketApply");
                    check(confirmation.disabled, "Invalid graph cannot be confirmed");
                    confirmation.checked = true;
                    confirmation.dispatchEvent(new Event("change", { bubbles: true }));
                    check(ui.root.querySelector("[data-action='apply']").disabled, "Forced confirmation cannot enable invalid preview");
                    check(!ui.root.querySelector("[data-preview-validation]").classList.contains("d-none"), "Invalid graph has a visible explanation");
                    document.getElementById("applyRefereeUserId").value = "90";
                    document.getElementById("applyMatchStartAt").value = "2026-12-01T08:00";
                    document.getElementById("applyMatchAddress").value = "Sân thử";
                    document.getElementById("applyBracketDetailsForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
                    await waitFor(() => !document.querySelector("[data-apply-modal-error]").classList.contains("d-none"));
                    check(!ui.calls.some(call => call.path.endsWith("/apply")), "Direct form submission never sends invalid graph to apply");
                    check(ui.root.querySelector("[data-action='apply']").disabled, "Busy cleanup keeps invalid preview disabled");
                }
                ui = await mount();
                await ui.preview();
                ui.root.querySelector("[data-action='show-preview']").click();
                ui.failPreview();
                document.querySelector("#teamPlacementMethodForm input[value='REGISTRATION_ORDER']").click();
                document.getElementById("teamPlacementConfirmForm").dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
                await waitFor(() => !ui.root.querySelector("[data-setup-error]").classList.contains("d-none"));
                check(ui.root.querySelector("[data-setup-error]").textContent.includes("Trận 2, Đội 1"), "API errors identify the missing team position");
                check(!ui.root.querySelector("[data-preview-board]").children.length, "Failed regeneration clears the old board");
                check(ui.root.querySelector("[data-action='apply']").disabled, "Failed regeneration cannot apply a stale preview");
                output.textContent = `PASS: ${checks} browser assertions`;
            } catch (error) {
                output.textContent = `FAIL: ${error.stack}`;
            }
        };
        const html = `<!doctype html><meta charset="utf-8"><style>.d-none{display:none}</style><pre id="result">RUNNING</pre><div id="fixture"></div><script>(${browserTest.toString()})(${JSON.stringify(markup)},${JSON.stringify(script)});</script>`;
        const fixturePath = path.join(tempDirectory, "fixture.html");
        fs.writeFileSync(fixturePath, html);
        const result = await new Promise((resolve, reject) => {
            const child = spawn(browserPath, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                `--user-data-dir=${path.join(tempDirectory, "profile")}`, "--virtual-time-budget=10000", "--dump-dom", pathToFileURL(fixturePath).href], { windowsHide: true, timeout: 45000 });
            let stdout = "";
            child.stdout.on("data", data => { stdout += data; });
            child.stderr.resume();
            child.on("error", reject);
            child.on("close", code => resolve({ code, stdout }));
        });
        const status = result.stdout.match(/<pre id="result">([\s\S]*?)<\/pre>/)?.[1];
        assert.equal(result.code, 0);
        assert.match(status || "No browser result", /^PASS:/, status);
        console.log(status);
    } finally {
        assert.equal(path.dirname(path.resolve(tempDirectory)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(tempDirectory).startsWith("hanaka-bracket-payment-"));
        fs.rmSync(tempDirectory, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    }
});
