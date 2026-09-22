"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { openEdge, delay } = require("./helpers/edge-browser");
const test = require("node:test");
const browser = process.env.HANAKA_TEST_BROWSER || [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
].find(file => fs.existsSync(file));

test("tournament detail hides missing fields and floats two actions only outside their original position", {skip: !browser, timeout: 60000}, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer/wwwroot/pickleball-web");
    let script = fs.readFileSync(path.join(root, "js/pages.js"), "utf8");
    const end = script.lastIndexOf("})();");
    script = script.slice(0, end) + 'window.__detail = {renderTournamentNativeDetail, initTournamentDetailInteractions};' + script.slice(end);
    const data = {script, css: fs.readFileSync(path.join(root, "css/home.css"), "utf8") + fs.readFileSync(path.join(root, "css/pages.css"), "utf8")};
    const scenario = async data => {
        let checks = 0;
        const check = (value, label) => { checks++; if(!value) throw new Error(label); };
        const pause = () => new Promise(resolve => setTimeout(resolve, 30));
        const wait = async (predicate, label) => {
            for(let index = 0; index < 100; index++){ if(predicate()) return; await pause(); }
            throw new Error("Timeout: " + label);
        };
        try {
            const frame = document.createElement("iframe");
            frame.style.cssText = "width:390px;height:720px;border:0";
            document.body.appendChild(frame);
            const win = frame.contentWindow, doc = win.document;
            doc.head.innerHTML = '<meta name="viewport" content="width=device-width, initial-scale=1"><style>' + data.css + '</style>';
            doc.body.innerHTML = '<div class="mobile-web-screen detail-screen--tournament-native"><header class="page-hero" style="height:56px"></header><main class="page-shell"><div class="container mobile-web-screen__container" data-detail-body></div></main></div><div style="height:900px"></div>';
            win.eval(data.script);
            const root = doc.querySelector(".mobile-web-screen"), body = doc.querySelector("[data-detail-body]");
            const detail = {tournamentId: 37, title: "Hanaka", status: "OPEN", startTime: null, registerDeadline: null,
                locationText: "Nhà Thi Đấu Hanaka", playoffType: "-", formatText: " ", singleLimit: 0, doubleLimit: "0.00",
                creatorName: "Trung Kiên", organizer: "Hanaka", expectedTeams: 32, registeredCount: 0,
                content: Array.from({length: 25}, (_, i) => '<p>Nội dung giải đấu ' + i + '</p>').join("")};
            const render = value => {
                body.innerHTML = win.__detail.renderTournamentNativeDetail({detail: value});
                win.__detail.initTournamentDetailInteractions(root, {detail: value}, "tournament-detail");
            };
            render(detail);
            const labels = () => [...body.querySelectorAll("dt")].map(node => node.textContent);
            check(!body.querySelector(".tournament-native-detail__status") && !body.textContent.includes("OPEN"), "Status badge removed");
            check(!labels().includes("Giới hạn trình đơn tối đa") && !labels().includes("Cặp tối đa"), "Zero rating limits hidden");
            check(!labels().includes("Thể thức") && !labels().includes("Dạng") && !labels().includes("Ngày thi đấu"), "Missing fields and placeholders hidden");
            check(!labels().includes("Số trận thi đấu"), "Unknown count is not invented as zero");
            check(labels().includes("Thành viên đã đăng ký") && body.querySelector(".tournament-native-detail__stats").textContent.includes("0"), "Known zero registration count remains meaningful");
            check(labels().includes("Người tạo giải") && labels().includes("Địa điểm"), "Existing information preserved");
            check(![...body.querySelectorAll("dd")].some(node => node.textContent.trim() === "-"), "No empty placeholder rows");
            for(const [width, height] of [[320,720], [390,720], [768,720], [1280,720], [640,300]]){
                frame.style.width = width + "px"; frame.style.height = height + "px";
                win.scrollTo({top: 0, behavior: "instant"});
                const floating = body.querySelector("[data-tournament-floating-actions]");
                await wait(() => !floating.hidden, "Floating actions above originals at " + width);
                const links = [...floating.querySelectorAll("a")];
                const originals = [...body.querySelectorAll("[data-tournament-original-action]")];
                check(links.length === 2 && links[0].textContent.trim() === "Danh sách đăng ký" && links[1].textContent.trim() === "Lịch thi đấu", "Two actions in expected order " + width);
                check(links.every((link, index) => link.getAttribute("href") === originals[index].getAttribute("href")), "Floating destinations match originals " + width);
                const first = links[0].getBoundingClientRect(), second = links[1].getBoundingClientRect(), dock = floating.getBoundingClientRect();
                check(second.top >= first.bottom && Math.abs(first.width - second.width) < 1, "Actions occupy two rows " + width);
                check(dock.left >= 0 && dock.right <= width + 1 && Math.abs(dock.bottom - height) < 2, "Dock fits viewport bottom " + width);
                const detailRoot = body.querySelector(".tournament-native-detail");
                await wait(() => parseFloat(win.getComputedStyle(detailRoot).paddingBottom) >= dock.height, "Bottom space reserved");
                const reserved = win.getComputedStyle(detailRoot).paddingBottom;
                originals[0].scrollIntoView({block: "center", behavior: "instant"});
                await wait(() => floating.hidden, "Dock hides when both originals visible " + width + " scroll=" + win.scrollY + " positions=" + originals.map(link => link.getBoundingClientRect().top).join(","));
                check(win.getComputedStyle(floating).display === "none", "Hidden dock is not clickable " + width);
                check(win.getComputedStyle(detailRoot).paddingBottom === reserved, "Visibility does not change page height " + width);
                win.scrollBy({top: 1, behavior: "instant"}); await pause();
                check(floating.hidden, "No visibility flicker near original group " + width);
                win.scrollTo({top: doc.documentElement.scrollHeight, behavior: "instant"});
                await wait(() => !floating.hidden, "Dock returns after originals leave above viewport " + width + " scroll=" + win.scrollY + " doc=" + doc.documentElement.scrollHeight + " positions=" + originals.map(link => link.getBoundingClientRect().top).join(","));
                check(doc.documentElement.scrollWidth <= width + 1, "No horizontal overflow " + width);
            }
            render({...detail, singleLimit: 2.5, doubleLimit: 6, playoffType: "Loại trực tiếp"});
            check(labels().includes("Giới hạn trình đơn tối đa") && labels().includes("Cặp tối đa") && labels().includes("Thể thức"), "Nonzero limits and supplied format rendered");
            check(body.querySelectorAll("[data-tournament-floating-actions]").length === 1, "Rerender keeps one dock and reconnects observers");
            render({tournamentId: 38, title: "Empty"});
            check(!body.querySelector(".tournament-native-detail__info") && !body.querySelector(".tournament-native-detail__stats"), "Fully empty information sections omitted");
            // Verify the scroll fallback for clients without IntersectionObserver.
            root._tournamentFloatingActionsCleanup();
            delete win.IntersectionObserver;
            render(detail); win.scrollTo({top: 0, behavior: "instant"});
            const floating = body.querySelector("[data-tournament-floating-actions]");
            await wait(() => !floating.hidden, "Fallback shows dock");
            body.querySelector("[data-tournament-original-action]").scrollIntoView({block: "center", behavior: "instant"});
            await wait(() => floating.hidden, "Fallback hides dock");
            check(true, "Scroll fallback works");
            root._tournamentFloatingActionsCleanup();
            check(floating.hidden && !body.querySelector(".has-floating-actions"), "Cleanup hides dock and removes reserved space");
            document.getElementById("result").textContent = "PASS: " + checks + " tournament detail assertions";
        } catch(error) { document.getElementById("result").textContent = "FAIL after " + checks + ": " + error.stack; }
    };
    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-detail-actions-"));
    let session;
    try {
        const file = path.join(temp, "test.html");
        fs.writeFileSync(file, '<!doctype html><meta charset="utf-8"><pre id="result">RUNNING</pre><script>window.addEventListener("load",()=>(' + scenario.toString() + ')(' + JSON.stringify(data).replace(/<\/script/gi, "<\\/script") + '));<\/script>');
        session = await openEdge(browser, temp, ["--window-size=1500,1000"]);
        await session.command("Page.navigate", {url: pathToFileURL(file).href});
        let status;
        for(let attempt = 0; attempt < 400; attempt++) {
            status = await session.evaluate('document.getElementById("result")?.textContent');
            if(status && status !== "RUNNING") break;
            await delay(50);
        }
        assert.match(status || "Missing browser output", /^PASS:/, status);
        console.log(status);
    } finally {
        if(session) await session.close();
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-detail-actions-"));
        fs.rmSync(temp, {recursive: true, force: true, maxRetries: 10, retryDelay: 100});
    }
});
