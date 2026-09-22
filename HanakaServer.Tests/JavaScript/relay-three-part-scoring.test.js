"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const http = require("node:http");
const test = require("node:test");
const {openEdge, delay} = require("./helpers/edge-browser");
const browser = process.env.HANAKA_TEST_BROWSER || ["C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", "C:/Program Files/Microsoft/Edge/Application/msedge.exe"].find(fs.existsSync);

async function cleanFixture(directory, prefix) {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(directory).startsWith(prefix));
    // Windows can retain a browser profile handle briefly after the browser has exited.
    await delay(250);
    try { await fs.promises.rm(directory, {recursive:true, force:true, maxRetries:10, retryDelay:200}); }
    catch (error) {
        if (error.code !== "EPERM" && error.code !== "EBUSY") throw error;
        console.warn("Browser profile cleanup deferred because Windows still holds a file handle.");
    }
}

test("three-part referee scoreboard saves, recovers and remains usable at mobile sizes", {skip: !browser, timeout: 90000}, async () => {
    const root = path.resolve(__dirname, "../..");
    const view = fs.readFileSync(path.join(root, "HanakaServer/Views/RefereePortal/Matches.cshtml"), "utf8");
    const css = view.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll("@@", "@");
    const markup = view.slice(view.indexOf("<body>") + 6, view.indexOf("<script src="));
    const script = view.match(/<script>\s*([\s\S]*?)<\/script>/)[1];
    const helper = fs.readFileSync(path.join(root, "HanakaServer/wwwroot/js/relay-scoreboard.js"), "utf8");
    const relayCss = fs.readFileSync(path.join(root, "HanakaServer/wwwroot/css/relay-scoreboard.css"), "utf8");
    function fixture() {
        const clone = x => JSON.parse(JSON.stringify(x));
        const response = data => ({data: clone(data), headers: {"content-type": "application/json"}});
        const listeners = [];
        const initial = {matchId: 764, tournamentId: 37, tournamentTitle: "GIẢI PICKLEBALL TEAM – DOANH NGHIỆP • NGHỆ SĨ • NHÀ TÀI TRỢ",
            roundLabel: "Vòng 1", groupName: "A", startAt: "2026-09-13T08:00:00", courtText: "Sân 1",
            team1Text: "TEAM VÂN VÁI", team2Text: "TEAM HANAKA", team1RegistrationId: 10, team2RegistrationId: 20,
            scoreTeam1: 0, scoreTeam2: 0, canEditScore: true, isRelay: true, isCompleted: false, scoreHistories: [],
            relayScores: {version: 0, requiresAllocation: false, parts: [1,2,3].map(partNumber => ({partNumber, scoreTeam1: 0, scoreTeam2: 0}))}};
        window.fixture = {
            server: clone(initial), writes: 0, pending: [], mode: "auto", errors: [],
            emit(type = "tournament.bracket.updated") { listeners.forEach(fn => fn({type, payload: clone(this.server)})); },
            reset(overrides = {}) { this.server = {...clone(initial), ...overrides}; this.emit(); },
            commit(data) {
                const relay = data.relay;
                if (relay.expectedVersion !== this.server.relayScores.version) throw {response: {status: 409, data: {message: "Phiên khác vừa cập nhật điểm"}}};
                const total = window.HanakaRelayScoreboard.totals(relay.parts);
                if (data.isCompleted && total.scoreTeam1 === total.scoreTeam2) throw {response: {status: 400, data: {message: "Tổng điểm đang hòa"}}};
                if (relay.allocateExisting && (total.scoreTeam1 !== this.server.scoreTeam1 || total.scoreTeam2 !== this.server.scoreTeam2)) throw {response: {status: 400, data: {message: "Phân bổ chưa khớp tổng điểm"}}};
                this.server = {...this.server, ...total, isCompleted: data.isCompleted,
                    relayScores: {version: relay.expectedVersion + 1, requiresAllocation: false, parts: clone(relay.parts)}};
                this.server.winnerTeam = data.isCompleted ? (total.scoreTeam1 > total.scoreTeam2 ? "1" : "2") : null;
                this.server.winnerRegistrationId = data.isCompleted ? (this.server.winnerTeam === "1" ? 10 : 20) : null;
                const history = {...total, scoreHistoryId: this.writes, refereeName: "Trọng tài", relayPartNumber: relay.changedPart,
                    relayPartsJson: JSON.stringify(relay.parts), createdAt: "2026-09-13T08:01:00"};
                this.server.scoreHistories.unshift(history);
                this.emit("tournament.match.score.updated");
                return response({...this.server, history});
            },
            accept() { const item = this.pending.shift(); try { item.resolve(this.commit(item.data)); } catch (error) { item.reject(error); } },
            fill(values) { document.querySelectorAll("[data-relay-input]").forEach((input, index) => {input.value = values[index]; input.dispatchEvent(new Event("input",{bubbles:true}));}); },
            plus(part = 1, side = 1) {document.querySelector(`[data-score-target="relayScore${part}_${side}"][data-score-step="1"]`).click();}
        };
        window.addEventListener("error", event => window.fixture.errors.push(event.message));
        window.HanakaPublicRealtime = {on(fn) {listeners.push(fn);return ()=>{};}, subscribeTournament(){},unsubscribeTournament(){}};
        window.axios = {
            async get(url) {return response(url.endsWith("/me") ? {fullName: "Trọng tài kiểm thử"} : {items: [window.fixture.server]});},
            async put(url, data) {
                const f=window.fixture; f.writes++;
                if (f.mode === "hold") return new Promise((resolve,reject)=>f.pending.push({data,resolve,reject}));
                const result=f.commit(data);
                if (f.mode === "timeoutAfterCommit") {f.mode="auto";throw {code:"ECONNABORTED"};}
                return result;
            }
        };
    }
    const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}\n${relayCss}\n.d-none{display:none!important}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden}body{font-family:Arial,sans-serif;line-height:1.5}.fas,.far{display:inline-block;width:1em}</style><body>${markup}<script>${helper}</script><script>(${fixture.toString()})();</script><script>${script}</script></body></html>`;
    const server = http.createServer((req,res)=>{res.setHeader("Content-Type","text/html; charset=utf-8");res.end(html);});
    await new Promise(resolve=>server.listen(0,"127.0.0.1",resolve));
    const temp=fs.mkdtempSync(path.join(os.tmpdir(),"hanaka-relay-score-"));
    const artifacts=path.join(root,"artifacts/relay-three-part-scoring"); fs.mkdirSync(artifacts,{recursive:true});
    let page; let checks=0;
    const check=async(expression,message)=>{assert.ok(await page.evaluate(expression),message);checks++;};
    const wait=async(expression)=>{for(let n=0;n<150;n++){if(await page.evaluate(expression))return;await delay(30);}throw Error("Timed out: "+expression);};
    const idle=()=>wait("document.getElementById('btnInlineSubmit') && !document.getElementById('btnInlineSubmit').disabled && fixture.pending.length === 0");
    try {
        page=await openEdge(browser,temp);
        await page.command("Emulation.setDeviceMetricsOverride",{width:390,height:844,deviceScaleFactor:1,mobile:true});
        await page.command("Page.navigate",{url:`http://127.0.0.1:${server.address().port}/RefereePortal/Matches/764`});
        await wait("document.querySelectorAll('[data-relay-input]').length === 6");
        await check("document.querySelectorAll('[data-relay-part]').length === 3 && document.querySelectorAll('[data-score-target]').length === 12","Three boards, six scores and twelve touch controls");
        await page.evaluate("window.originalForm=document.getElementById('inlineScoreForm');fixture.mode='hold';document.querySelector('.overlay-body').scrollTop=180;window.scrollBefore=document.querySelector('.overlay-body').scrollTop;fixture.plus()");
        await wait("fixture.pending.length === 1");
        await check("document.getElementById('relayScore1_1').value==='1' && document.querySelector('[data-relay-total=\"1\"]').textContent==='1'","The part and total both update immediately");
        await check("[...document.querySelectorAll('[data-score-target]')].every(x=>x.disabled)","Concurrent point writes are locked");
        await page.evaluate("fixture.plus();fixture.accept();fixture.mode='auto'"); await idle(); await delay(400);
        await check("fixture.writes===1 && originalForm===document.getElementById('inlineScoreForm') && document.querySelector('.overlay-body').scrollTop===scrollBefore","Repeated tap and realtime echo do not duplicate the write or rebuild/scroll the form");
        await page.evaluate("fixture.fill([21,18,15,21,21,20]);document.getElementById('inlineScoreForm').requestSubmit()"); await idle();
        await check("fixture.server.scoreTeam1===57 && fixture.server.scoreTeam2===59 && !fixture.server.isCompleted","The sum determines the live total; 21 does not finish the match");
        await page.evaluate("fixture.plus(3,1)");await idle();
        await check("fixture.server.relayScores.parts[2].scoreTeam1===22","A part can exceed 21");
        await page.evaluate("fixture.fill([0,18,15,21,22,20]);document.getElementById('inlineScoreForm').requestSubmit()");await idle();
        await check("fixture.server.scoreTeam1===37 && fixture.server.relayScores.parts[0].scoreTeam1===0","Earlier parts can be reduced to zero");
        await page.evaluate("fixture.fill([21,18,15,21,21,20]);document.getElementById('inlineScoreForm').requestSubmit()");await idle();

        for(const width of [320,360,390,430,768,1280]) {
            await page.command("Emulation.setDeviceMetricsOverride",{width,height:844,deviceScaleFactor:1,mobile:width<768});await delay(100);
            await check("document.documentElement.scrollWidth<=innerWidth && document.querySelector('.overlay-body').scrollWidth<=document.querySelector('.overlay-body').clientWidth+1",`No horizontal overflow at ${width}px`);
            await check("[...document.querySelectorAll('.relay-step')].every(x=>{const r=x.getBoundingClientRect();return r.width>=48&&r.height>=48})",`Touch controls are at least 48px at ${width}px`);
            await page.evaluate("document.querySelector('.overlay-body').scrollTop=0");
            const shot=await page.command("Page.captureScreenshot",{format:"png"});fs.writeFileSync(path.join(artifacts,`scoreboard-${width}.png`),Buffer.from(shot.data,"base64"));
        }
        await page.command("Emulation.setDeviceMetricsOverride",{width:390,height:844,deviceScaleFactor:1,mobile:true});
        await page.evaluate("document.querySelector('.overlay-body').scrollTop=360");await delay(100);
        await check("document.querySelector('.relay-totals').getBoundingClientRect().top>=document.querySelector('.overlay-body').getBoundingClientRect().top-1","Total score stays visible while scrolling through the parts");

        await page.evaluate("fixture.mode='timeoutAfterCommit';fixture.plus(2,1)");await idle();await delay(500);
        await check("document.getElementById('relayScore2_1').value==='16' && fixture.server.relayScores.parts[1].scoreTeam1===16","Lost save response recovers the committed score without undoing or replaying it");
        await page.evaluate("fixture.server.relayScores.version++;fixture.server.relayScores.parts[0].scoreTeam1=25;fixture.server.scoreTeam1=62;fixture.plus(3,2)");await idle();await delay(500);
        await check("document.getElementById('relayScore1_1').value==='25' && document.getElementById('relayScore3_2').value==='20'","A stale save recovers the other session's parts without overwriting them");

        await page.evaluate("fixture.fill([21,18,15,21,21,20]);document.getElementById('inlineScoreForm').requestSubmit()");await idle();
        await page.evaluate("document.getElementById('inlineIsCompleted').checked=true;document.getElementById('inlineScoreForm').requestSubmit()");
        await wait("document.getElementById('endMatchConfirm').classList.contains('show')");
        await check("document.getElementById('endMatchConfirmParts').textContent.includes('Đội con 3: 21 – 20') && document.getElementById('endMatchConfirmParts').textContent.includes('TEAM HANAKA')","Completion shows all part scores and the team winning by total");
        await page.evaluate("document.getElementById('btnEndMatchConfirm').click()");await idle();
        await check("fixture.server.isCompleted && fixture.server.winnerTeam==='2'","Team B wins 59–57 even though team A won two individual parts");

        await page.evaluate("fixture.server.team1Text='ĐỘI TIẾP SỨC '+ 'TÊN RẤT DÀI '.repeat(15);fixture.emit()");
        await wait("document.querySelector('.relay-team-name').textContent.includes('TÊN RẤT DÀI')");
        await check("document.querySelector('.overlay-body').scrollWidth<=document.querySelector('.overlay-body').clientWidth+1","Long team names wrap without shrinking or hiding the score controls");
        await page.command("Emulation.setDeviceMetricsOverride",{width:844,height:390,deviceScaleFactor:1,mobile:true});await delay(100);
        await check("getComputedStyle(document.querySelector('.relay-totals')).position==='static' && [...document.querySelectorAll('.relay-step')].every(x=>x.getBoundingClientRect().height>=48)","Landscape leaves room for scoring and keeps large touch targets");
        await page.command("Emulation.setDeviceMetricsOverride",{width:390,height:420,deviceScaleFactor:1,mobile:true});await delay(100);
        await check("document.querySelector('.overlay-body').scrollHeight>document.querySelector('.overlay-body').clientHeight && document.documentElement.scrollWidth<=innerWidth","A reduced mobile viewport remains scrollable when the keyboard uses screen space");
        await page.command("Emulation.setDeviceMetricsOverride",{width:390,height:844,deviceScaleFactor:1,mobile:true});

        await page.evaluate("fixture.reset({scoreTeam1:30,scoreTeam2:25,relayScores:{version:0,requiresAllocation:true,parts:[1,2,3].map(partNumber=>({partNumber,scoreTeam1:0,scoreTeam2:0}))}})");
        await wait("document.querySelector('.relay-allocation') !== null");
        await check("document.getElementById('inlineIsCompleted').disabled && document.getElementById('btnInlineSubmit').textContent.includes('Lưu phân bổ')","Old totals require an explicit allocation before scoring");
        await page.evaluate("fixture.fill([21,18,9,7,0,0]);document.getElementById('inlineScoreForm').requestSubmit()");await idle();
        await wait("document.querySelector('.relay-allocation') === null");
        await check("!document.getElementById('inlineIsCompleted').disabled && fixture.server.scoreTeam1===30","Matching allocation preserves totals and unlocks regular scoring");

        await page.evaluate("fixture.server.canEditScore=false;fixture.emit()");await wait("document.querySelectorAll('[data-relay-input]').length===0");
        await check("document.querySelectorAll('[data-relay-read]').length===6 && document.querySelector('.focus-lock')","Read-only matches still show all three saved parts");
        await check("fixture.errors.length===0","No browser runtime errors");
        fs.writeFileSync(path.join(artifacts,"browser-result.json"),JSON.stringify({checks,status:"passed"},null,2));
        console.log(`PASS: ${checks} relay scoring and mobile assertions`);
    } finally {
        await page?.close();await new Promise(resolve=>server.close(resolve));
        await cleanFixture(temp,"hanaka-relay-score-");
    }
});

test("admin scoreboard uses three parts and rejects stale displayed values after a background refresh", {skip: !browser, timeout: 30000}, async () => {
    const root=path.resolve(__dirname,"../..");
    const view=fs.readFileSync(path.join(root,"HanakaServer/Views/TournamentGroupMatches/Index.cshtml"),"utf8");
    const modal=view.slice(view.indexOf('<div class="modal fade" id="modalScore"'),view.indexOf("@section Scripts"));
    const state=view.slice(view.indexOf("            function captureScoreModalState()"),view.indexOf("            function applyRealtimeScore("));
    const open=view.slice(view.indexOf("            function renderAdminRelayScore("),view.indexOf('            $id("formMatch").addEventListener("submit"'));
    const save=view.slice(view.indexOf('            $id("formScore").addEventListener("submit"'),view.indexOf('            $id("btnOpenCreate").addEventListener'));
    const helper=fs.readFileSync(path.join(root,"HanakaServer/wwwroot/js/relay-scoreboard.js"),"utf8");
    const css=fs.readFileSync(path.join(root,"HanakaServer/wwwroot/css/relay-scoreboard.css"),"utf8");
    const bootstrap=`
        const $id=id=>document.getElementById(id), API='/api/admin/groups/1/matches';
        let SCORE_SAVE_BUSY=false,SCORE_MODAL_BASELINE=null;
        const CACHE={items:[{matchId:1,team1Text:'Đội A',team2Text:'Đội B',team1RegistrationId:10,team2RegistrationId:20,isRelay:true,isCompleted:false,scoreTeam1:0,scoreTeam2:0,
            relayScores:{version:0,requiresAllocation:false,parts:[1,2,3].map(partNumber=>({partNumber,scoreTeam1:0,scoreTeam2:0}))}}]};
        const copy=x=>JSON.parse(JSON.stringify(x));
        window.fixture={server:copy(CACHE.items[0]),writes:0,conflicts:0};
        window.$=selector=>({modal:action=>document.querySelector(selector).classList.toggle('show',action==='show'),hasClass:name=>document.querySelector(selector).classList.contains(name)});
        const render=()=>{},reloadBracketPopupIfOpen=()=>{};
        const hideFormErr=id=>$id(id).textContent='';
        const showFormErr=(message,id)=>$id(id).textContent=message;
        async function load(){CACHE.items=[copy(fixture.server)];}
        window.refreshCache=load;
        window.confirm=()=>true;
        window.axios={async put(url,data){
            fixture.writes++;
            if(data.relay.expectedVersion!==fixture.server.relayScores.version){fixture.conflicts++;throw {response:{status:409,data:{message:'Điểm đã thay đổi'}}};}
            const total=HanakaRelayScoreboard.totals(data.relay.parts);
            fixture.server={...fixture.server,...total,isCompleted:data.isCompleted,
                relayScores:{version:data.relay.expectedVersion+1,requiresAllocation:false,parts:copy(data.relay.parts)}};
            return {data:copy(fixture.server)};
        }};
    `;
    const html=`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}.modal{max-width:500px;margin:auto}body{font-family:Arial}</style>${modal}<script>${helper}</script><script>${bootstrap}\n${state}\n${open}\n${save}\nwindow.openScore(1);</script>`;
    const temp=fs.mkdtempSync(path.join(os.tmpdir(),"hanaka-relay-admin-"));
    const file=path.join(temp,"admin.html");fs.writeFileSync(file,html);
    let page;
    const wait=async expression=>{for(let i=0;i<100;i++){if(await page.evaluate(expression))return;await delay(30);}throw Error(expression);};
    try {
        page=await openEdge(browser,temp);await page.command("Emulation.setDeviceMetricsOverride",{width:390,height:844,deviceScaleFactor:1,mobile:true});
        await page.command("Page.navigate",{url:require("node:url").pathToFileURL(file).href});
        await wait("document.querySelectorAll('[data-relay-input]').length===6");
        assert.ok(await page.evaluate("document.getElementById('standardScoreFields').hidden && !document.getElementById('isCompleted').checked"));
        await page.evaluate("document.querySelector('[data-score-target=\"adminRelayScore1_1\"][data-score-step=\"1\"]').click()");
        await wait("fixture.server.scoreTeam1===1 && !document.querySelector('[data-score-target]').disabled");
        assert.ok(await page.evaluate("document.querySelector('[data-relay-total=\"1\"]').textContent==='1' && document.getElementById('modalScore').classList.contains('show')"));
        await page.evaluate("fixture.server.relayScores.version++;fixture.server.relayScores.parts[0].scoreTeam1=9;fixture.server.scoreTeam1=9;refreshCache()");
        await page.evaluate("document.querySelector('[data-score-target=\"adminRelayScore2_2\"][data-score-step=\"1\"]').click()");
        await wait("fixture.conflicts===1 && !document.querySelector('[data-score-target]').disabled");
        assert.ok(await page.evaluate("fixture.server.scoreTeam1===9 && document.getElementById('adminRelayScore1_1').value==='9' && document.getElementById('adminRelayScore2_2').value==='0'"));
        await page.evaluate("document.querySelector('[data-score-target=\"adminRelayScore2_2\"][data-score-step=\"1\"]').click()");
        await wait("fixture.server.scoreTeam2===1 && !document.querySelector('[data-score-target]').disabled");
        assert.equal(await page.evaluate("fixture.writes"),3);
    } finally {
        await page?.close();
        await cleanFixture(temp,"hanaka-relay-admin-");
    }
});
