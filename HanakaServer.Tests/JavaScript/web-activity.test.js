"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const {openEdge, delay} = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);

test("public web loading covers response bodies, regions, navigation, cancellation and browser restoration", {skip: !browser, timeout: 60000}, async () => {
    const root = path.resolve(__dirname, "../../HanakaServer/wwwroot");
    const routes = new Map([
        ["/loader.js", ["text/javascript", fs.readFileSync(path.join(root, "js/api-loading.js"))]],
        ["/activity.js", ["text/javascript", fs.readFileSync(path.join(root, "pickleball-web/js/web-activity.js"))]],
        ["/loader.css", ["text/css", fs.readFileSync(path.join(root, "css/api-loading.css"))]],
        ["/activity.css", ["text/css", fs.readFileSync(path.join(root, "pickleball-web/css/web-activity.css"))]]
    ]);
    for(const [url,file,type] of [["/auth.js","js/auth.js","text/javascript"],["/session.js","js/web-session.js","text/javascript"],["/auth.css","css/auth.css","text/css"]]) {
        routes.set(url,[type,fs.readFileSync(path.join(root,"pickleball-web",file))]);
    }
    const loginView = fs.readFileSync(path.join(root,"../Views/PickleballWeb/Login.cshtml"),"utf8");
    const loginMarkup = loginView.slice(loginView.indexOf('<div class="auth-screen'),loginView.indexOf('@section Scripts'))
        .replaceAll('@Model.ReturnUrl','/PickleballWeb/b').replaceAll('@Model.Identifier','').replaceAll('@forgotPasswordHref','/PickleballWeb/ForgotPassword').replaceAll('@encodedReturnUrl','');
    let loginSuccess = false;
    let postCount = 0;
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, "http://localhost");
        if(routes.has(url.pathname)) {
            const [type, content] = routes.get(url.pathname);
            response.writeHead(200, {"Content-Type": type}); response.end(content); return;
        }
        if(url.pathname === '/PickleballWeb/Login') {
            response.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
            response.end('<!doctype html><html data-web-activity><head><link rel="stylesheet" href="/loader.css"><link rel="stylesheet" href="/activity.css"><link rel="stylesheet" href="/auth.css"><script src="/loader.js"></script><script src="/activity.js"></script></head><body>'+loginMarkup+'<script src="/session.js"></script><script src="/auth.js"></script></body></html>');return;
        }
        if(url.pathname === '/api/web-auth/me') {
            response.writeHead(200,{'Content-Type':'application/json'});response.end('{"isAuthenticated":false}');return;
        }
        if(url.pathname === '/api/web-auth/login') {
            postCount++;
            const timer=setTimeout(()=>{response.writeHead(loginSuccess?200:401,{'Content-Type':'application/json'});response.end(loginSuccess?'{}':'{"message":"Thông tin đăng nhập chưa đúng"}');},600);
            response.on('close',()=>clearTimeout(timer));return;
        }
        if(url.pathname === "/cover.svg") {
            const timer = setTimeout(() => { response.writeHead(200, {"Content-Type":"image/svg+xml"}); response.end('<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900"><rect width="600" height="900" fill="navy"/></svg>'); }, 900);
            response.on("close",()=>clearTimeout(timer)); return;
        }
        if(url.pathname.startsWith("/api/")) {
            if(request.method === "POST") postCount++;
            if(url.pathname.endsWith("/never")) return;
            if(url.pathname.endsWith("/empty")) { response.writeHead(204); response.end(); return; }
            response.writeHead(url.pathname.endsWith("/error") ? 503 : 200, {"Content-Type": "application/json"});
            response.write('{"value":'); // Headers arrive before the body is ready.
            const timer = setTimeout(() => response.end(url.pathname.endsWith("/invalid") ? "broken" : '42}'), Number(url.searchParams.get("ms")) || 500);
            response.on("close", () => clearTimeout(timer));
            return;
        }
        if(url.pathname.endsWith("/blocked")) return;
        response.writeHead(200, {"Content-Type": "text/html; charset=utf-8"});
        response.end(`<!doctype html><html data-web-activity><head><meta name="viewport" content="width=device-width,initial-scale=1">
          <link rel="stylesheet" href="/loader.css"><link rel="stylesheet" href="/activity.css">
          ${url.searchParams.has("timeouts") ? '<script>const nativeTimeout = window.setTimeout; window.setTimeout = (fn, ms, ...args) => nativeTimeout(fn, ms === 30000 ? 500 : ms === 20000 ? 900 : ms, ...args);</script>' : ''}
          <script src="/loader.js"></script><script src="/activity.js"></script></head><body>
          <main id="main" style="min-height:1600px">${url.searchParams.has("image") ? '<img data-page-critical-image src="/cover.svg" style="width:100%;height:auto">' : ''}<div id="region" style="position:relative;min-height:160px">
          <form id="form"><input id="field" value="keep this value"><button id="save">Save</button></form></div>
          <a id="next" href="/PickleballWeb/b">Next</a><a id="hash" href="#section">Hash</a>
          <a id="newtab" target="_blank" href="/PickleballWeb/b">New tab</a>
          <a id="cancelled" href="/PickleballWeb/b">Cancelled</a><a id="external" href="https://example.com/">External</a>
          <a id="download" href="/PickleballWeb/b" download>Download</a><div id="data"></div><div id="section">Section</div></main>
          <script>
          document.getElementById('cancelled').onclick = e => e.preventDefault();
          document.getElementById('form').onsubmit = async e => { e.preventDefault(); window.submits=(window.submits||0)+1; await fetch('/api/save?ms=650',{method:'POST'}).then(r=>r.json()); };
          window.boot = fetch('/api/boot?ms=${url.pathname.endsWith("/b") ? 650 : 100}', {hanakaLoading:'silent'}).then(r=>r.json()).then(data=>document.getElementById('data').textContent=String(data.value));
          </script></body></html>`);
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const origin = "http://127.0.0.1:" + server.address().port;
    const temp = fs.mkdtempSync(path.join(os.tmpdir(), "hanaka-web-activity-"));
    let session, checks = 0;
    try {
        session = await openEdge(browser, temp, ["--window-size=1100,900"]);
        const evaluate = expression => session.evaluate(expression);
        const execute = expression => evaluate(expression + ";void 0;");
        const wait = async (expression, label) => {
            for(let attempt=0; attempt<150; attempt++) { if(await evaluate(expression)) return; await delay(30); }
            throw new Error("Timeout: " + label + " " + expression);
        };
        const check = async (expression, label) => { checks++; assert.ok(await evaluate(expression), label); };
        const idle = () => wait('document.readyState !== "loading" && window.HanakaWebActivity && HanakaWebActivity.activeCount === 0 && HanakaApiLoading.activeCount === 0 && !document.documentElement.classList.contains("hanaka-api-busy")', "idle");
        await session.command("Page.navigate", {url: origin + "/PickleballWeb/a"});
        await idle();
        await check('document.getElementById("data").textContent === "42"', "Initial data rendered before loading ends");
        await check('!document.getElementById("main").inert', "Initial interaction restored");

        await session.command("Page.navigate", {url: origin + "/PickleballWeb/a?image=1"});
        await wait('window.HanakaWebActivity && document.getElementById("data")?.textContent === "42" && document.documentElement.classList.contains("hanaka-api-busy")', "cover loading");
        await check('!document.querySelector("[data-page-critical-image]").complete', "Page remains loading until the main image dimensions are ready");
        await idle();
        await check('document.querySelector("[data-page-critical-image]").naturalHeight===900', "Cover keeps its original aspect ratio");
        await session.command("Page.navigate", {url: origin + "/PickleballWeb/a"}); await idle();

        await execute(`window.done=false; window.work=fetch('/api/stream?ms=850').then(r=>{window.headersReceived=true; return r.json()}).then(()=>{document.getElementById('data').textContent='rendered';window.done=true});`);
        await wait('window.headersReceived', "headers");
        await wait('document.documentElement.classList.contains("hanaka-api-busy")', "stream overlay");
        await check('!window.done && HanakaWebActivity.activeCount === 1', "Overlay waits for the response body");
        await check('document.getElementById("main").inert', "Overlay blocks keyboard and pointer interaction");
        await idle();
        await check('document.getElementById("data").textContent === "rendered" && !document.getElementById("main").inert', "Rendered result visible and interaction restored");

        await execute(`window.first=false; fetch('/api/one?ms=350').then(r=>r.json()).then(()=>window.first=true); fetch('/api/two?ms=1100').then(r=>r.json());`);
        await wait('window.first && HanakaWebActivity.activeCount === 1', "first request done");
        await check('document.documentElement.classList.contains("hanaka-api-busy")', "Concurrent request keeps overlay open");
        await idle();
        await execute(`fetch('/api/background?ms=600',{hanakaLoading:'silent'}).then(r=>r.json());`);
        await delay(400);
        await check('!document.documentElement.classList.contains("hanaka-api-busy")', "Background API stays silent");
        await idle();
        await execute(`document.getElementById('save').click(); document.getElementById('save').click();`);
        await idle();
        await check('window.submits === 1', "Repeated form submit blocked immediately");
        assert.equal(postCount, 1); checks++;
        await execute(`document.getElementById('save').click();`);
        await idle();
        assert.equal(postCount, 2); checks++;

        await execute(`window.controller=new AbortController();window.aborted=false;fetch('/api/never',{signal:controller.signal}).catch(e=>window.aborted=e.name==='AbortError');`);
        await delay(350);
        await evaluate('controller.abort()'); await idle();
        await check('window.aborted && !document.getElementById("main").inert', "Abort releases overlay");
        await execute(`window.bad=false;fetch('/api/invalid?ms=350').then(r=>r.json()).catch(()=>window.bad=true);`);
        await idle(); await check('window.bad', "Malformed body rejects and releases loading");
        await execute(`window.errorStatus=0;fetch('/api/error?ms=350').then(async r=>{window.errorStatus=r.status;await r.json()});`);
        await idle(); await check('window.errorStatus===503', "HTTP errors preserve status and release loading");
        await execute(`fetch('/api/empty');`); await idle();
        await check('HanakaApiLoading.activeCount === 0', "Responses without a body release all activity");

        await execute(`window.region=document.getElementById('region');HanakaWebActivity.setRegionBusy(region,region,true);`);
        await delay(250);
        await check('region.getAttribute("aria-busy")==="true" && !document.documentElement.classList.contains("hanaka-api-busy")', "Region loading does not block the full page");
        await evaluate('HanakaWebActivity.setRegionBusy(region,region,false)'); await delay(260);
        await check('!region.hasAttribute("data-web-region-busy") && !region.hasAttribute("aria-busy")', "Region busy state cleans up");

        await execute(`document.getElementById('cancelled').click(); document.getElementById('hash').click();`);
        await delay(400);
        await check('!document.documentElement.classList.contains("hanaka-api-busy") && !sessionStorage.getItem("hanaka.web.navigation")', "Cancelled and hash navigation do not open overlay");
        await execute(`document.getElementById('newtab').dispatchEvent(new MouseEvent('click',{bubbles:true,ctrlKey:true,cancelable:true}));`);
        await check('!sessionStorage.getItem("hanaka.web.navigation")', "Modified/new-tab click does not mark navigation");

        await execute(`document.getElementById('next').click();`);
        await wait('location.pathname === "/PickleballWeb/b" && !!window.HanakaWebActivity', "destination");
        await check('document.documentElement.classList.contains("hanaka-api-busy")', "Destination resumes navigation overlay before API completes");
        await idle();
        await check('document.getElementById("data").textContent === "42" && !sessionStorage.getItem("hanaka.web.navigation")', "Destination ready clears navigation marker");
        await evaluate('history.back()');
        await wait('location.pathname === "/PickleballWeb/a" && !!window.HanakaWebActivity', "back");
        await idle();
        await check('!document.getElementById("main").inert && document.getElementById("field").value === "keep this value"', "Back restores interactive page and input");
        await evaluate('history.forward()');
        await wait('location.pathname === "/PickleballWeb/b" && !!window.HanakaWebActivity', "forward");
        await idle();

        await execute(`fetch('/api/never').catch(()=>{}); window.dispatchEvent(new PageTransitionEvent('pagehide',{persisted:true}));window.dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true}));`);
        await idle();
        await check('!document.getElementById("main").inert && HanakaApiLoading.activeCount===0', "Restoration while requests are pending clears stale state");

        await session.command("Page.navigate", {url: origin + "/PickleballWeb/Login"}); await idle();
        await execute(`const identifier=document.querySelector('[data-auth-identifier]'),password=document.querySelector('[data-auth-password]');identifier.value='demo@example.invalid';password.value='test-password';identifier.dispatchEvent(new Event('input',{bubbles:true}));password.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('form').requestSubmit();document.querySelector('form').requestSubmit();`);
        await wait('document.querySelector("form").getAttribute("aria-busy")==="true"', "real login region overlay");
        await check('!document.documentElement.classList.contains("hanaka-api-busy") && document.querySelectorAll(".hanaka-api-section-overlay:not([hidden])").length===1', "Real auth form owns one loading overlay");
        await idle();
        await check('!document.querySelector("[data-auth-error]").hidden && !document.querySelector("[data-auth-submit]").disabled', "Failed login unlocks form and preserves error feedback");
        assert.equal(postCount,3); checks++;
        loginSuccess=true;
        await execute(`document.querySelector('form').requestSubmit();`);
        await wait('location.pathname === "/PickleballWeb/b" && !!window.HanakaWebActivity', "login redirect"); await idle();
        await check('document.getElementById("data").textContent==="42"', "Successful real login hands off to navigation loading");

        await session.command("Page.navigate", {url: origin + "/PickleballWeb/a?timeouts=1"}); await idle();
        await execute(`window.timedOut=false;fetch('/api/never').catch(e=>window.timedOut=e.name==='TimeoutError');`);
        await idle(); await check('window.timedOut', "Request timeout releases loading and rejects");
        await execute(`setTimeout(()=>HanakaWebActivity.navigate('/PickleballWeb/blocked'), 0);`);
        await delay(1400);
        await session.command("Page.stopLoading");
        await wait('!!document.querySelector("[data-web-activity-notice]") && !document.querySelector("[data-web-activity-notice]").hidden', "navigation timeout recovery");
        await check('!document.documentElement.classList.contains("hanaka-api-busy") && !sessionStorage.getItem("hanaka.web.navigation")', "Failed navigation unlocks page without retrying a submission");
        await session.command("Page.stopLoading");

        for(const width of [320,390,768,1440]) {
            await session.command("Emulation.setDeviceMetricsOverride", {width, height: 844, deviceScaleFactor: 1, mobile: width < 768});
            await execute(`window.token=HanakaApiLoading.begin({mode:'global',immediate:true});`);
            await check('document.documentElement.scrollWidth <= innerWidth && document.querySelector(".hanaka-api-overlay__panel").getBoundingClientRect().width <= innerWidth', "Overlay fits viewport " + width);
            await evaluate('HanakaApiLoading.end(window.token)'); await delay(260);
        }
        console.log("PASS: " + checks + " public web activity browser assertions");
    } finally {
        if(session) await session.close();
        server.closeAllConnections(); await new Promise(resolve=>server.close(resolve));
        assert.equal(path.dirname(path.resolve(temp)), path.resolve(os.tmpdir()));
        assert.ok(path.basename(temp).startsWith("hanaka-web-activity-"));
        fs.rmSync(temp, {recursive:true,force:true,maxRetries:10,retryDelay:100});
    }
});
