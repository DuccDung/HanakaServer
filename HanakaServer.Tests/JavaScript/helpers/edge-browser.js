"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// A real browser clock is required for scroll/IntersectionObserver tests. Chromium's
// virtual-time dump-dom mode can skip animation frames between programmatic scrolls.
async function openEdge(browser, directory, extraArgs = []) {
    const profile = path.join(directory, "profile");
    const child = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
        "--remote-debugging-port=0", `--user-data-dir=${profile}`, ...extraArgs, "about:blank"], {windowsHide: true});
    child.stdout.resume(); child.stderr.resume();
    let processError;
    child.on("error", error => processError = error);
    const exited = new Promise(resolve => child.once("exit", resolve));
    let socket;
    const pending = new Map();
    let sequence = 0;
    const command = (method, params = {}, timeoutMs = 15000) => new Promise((resolve, reject) => {
        const id = ++sequence;
        const timeout = setTimeout(() => { pending.delete(id); reject(new Error("CDP timeout: " + method)); }, timeoutMs);
        pending.set(id, {resolve, reject, timeout});
        socket.send(JSON.stringify({id, method, params}));
    });
    const close = async () => {
        if(socket?.readyState === WebSocket.OPEN) {
            await command("Browser.close").catch(() => {});
            socket.close();
        }
        await Promise.race([exited, delay(2000)]);
        if(child.exitCode === null) { child.kill(); await Promise.race([exited, delay(2000)]); }
        for(const item of pending.values()) { clearTimeout(item.timeout); item.reject(new Error("Browser closed")); }
        pending.clear();
    };
    try {
        const portFile = path.join(profile, "DevToolsActivePort");
        let port;
        for(let attempt = 0; !port; attempt++) {
            if(processError) throw processError;
            if(attempt >= 400 || child.exitCode !== null) throw new Error("Browser did not start");
            try {
                const firstLine = fs.readFileSync(portFile, "utf8").split(/\r?\n/)[0];
                if (/^\d+$/.test(firstLine)) port = firstLine;
            } catch (error) {
                // Edge can briefly hold its discovery file open while writing it on Windows.
                if (!["ENOENT", "EBUSY", "EACCES", "EPERM"].includes(error.code)) throw error;
            }
            if (!port) await delay(25);
        }
        const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then(response => response.json());
        socket = new WebSocket(targets.find(target => target.type === "page").webSocketDebuggerUrl);
        await new Promise((resolve, reject) => { socket.addEventListener("open", resolve, {once: true}); socket.addEventListener("error", reject, {once: true}); });
        socket.addEventListener("message", event => {
            const result = JSON.parse(event.data), item = pending.get(result.id);
            if(!item) return;
            pending.delete(result.id); clearTimeout(item.timeout);
            if(result.error) item.reject(new Error(result.error.message)); else item.resolve(result.result);
        });
        await command("Page.enable");
        return {
            command, close,
            evaluate: async (expression, timeoutMs) => {
                const response = await command("Runtime.evaluate", {expression, returnByValue: true, awaitPromise: true}, timeoutMs);
                if(response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description || response.exceptionDetails.text);
                return response.result.value;
            }
        };
    } catch(error) { await close(); throw error; }
}
module.exports = {openEdge, delay};
