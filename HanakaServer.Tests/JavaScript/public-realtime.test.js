"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const windowEvents = Object.create(null);
const documentEvents = Object.create(null);

class MockWebSocket {
    static OPEN = 1;
    static CONNECTING = 0;
    static instances = [];

    constructor(url) {
        this.url = url;
        this.readyState = MockWebSocket.CONNECTING;
        this.sent = [];
        this.listeners = Object.create(null);
        MockWebSocket.instances.push(this);
    }

    addEventListener(type, listener) {
        (this.listeners[type] ||= []).push(listener);
    }

    fire(type, event = {}) {
        (this.listeners[type] || []).forEach(listener => listener(event));
    }

    open() {
        this.readyState = MockWebSocket.OPEN;
        this.fire("open");
    }

    message(value) {
        this.fire("message", { data: JSON.stringify(value) });
    }

    send(value) {
        this.sent.push(JSON.parse(value));
    }

    close() {
        this.readyState = 3;
        this.fire("close");
    }
}

const documentMock = {
    hidden: false,
    addEventListener(type, listener) {
        (documentEvents[type] ||= []).push(listener);
    }
};

const windowMock = {
    location: { protocol: "https:", host: "hanaka.test" },
    WebSocket: MockWebSocket,
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval,
    console,
    addEventListener(type, listener) {
        (windowEvents[type] ||= []).push(listener);
    }
};

const context = vm.createContext({
    window: windowMock,
    document: documentMock,
    console,
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval
});

const sourcePath = path.resolve(__dirname, "../../HanakaServer/wwwroot/js/public-realtime.js");
vm.runInContext(fs.readFileSync(sourcePath, "utf8"), context, { filename: sourcePath });

const realtime = windowMock.HanakaPublicRealtime;
assert(realtime, "The shared realtime client must be exported.");

const received = [];
const removeListener = realtime.on(event => received.push(event));
realtime.subscribeTournament(7);
realtime.subscribeMatch(9);
realtime.subscribePayment(" QA-123 ");
realtime.subscribeVideos();

assert.equal(MockWebSocket.instances.length, 1, "One page must use one public WebSocket.");
const firstSocket = MockWebSocket.instances[0];
assert.equal(firstSocket.url, "wss://hanaka.test/ws-public");
firstSocket.open();

assert(firstSocket.sent.some(item => item.type === "tournament.subscribe" && item.tournamentId === 7));
assert(firstSocket.sent.some(item => item.type === "match.subscribe" && item.matchId === 9));
assert(firstSocket.sent.some(item => item.type === "payment.subscribe" && item.transactionCode === "QA-123"));
assert(firstSocket.sent.some(item => item.type === "videos.subscribe"));

const duplicatedEvent = {
    type: "tournament.match.score.updated",
    eventId: "duplicate-event",
    payload: { tournamentId: 7, matchId: 9, scoreTeam1: 5, scoreTeam2: 3 }
};
firstSocket.message(duplicatedEvent);
firstSocket.message(duplicatedEvent);
assert.equal(
    received.filter(event => event.eventId === "duplicate-event").length,
    1,
    "Duplicate eventIds must only be emitted once."
);

firstSocket.close();
realtime.connect();
assert.equal(MockWebSocket.instances.length, 2, "Reconnect must create a replacement socket.");
const secondSocket = MockWebSocket.instances[1];
secondSocket.open();
assert(secondSocket.sent.some(item => item.type === "tournament.subscribe" && item.tournamentId === 7));
assert(secondSocket.sent.some(item => item.type === "match.subscribe" && item.matchId === 9));
assert(received.some(event => event.type === "__public_socket_open__" && event.reconnected === true));

(windowEvents.pagehide || []).forEach(listener => listener({ persisted: true }));
(windowEvents.pageshow || []).forEach(listener => listener({ persisted: true }));
assert.equal(MockWebSocket.instances.length, 3, "BFCache restore must reconnect the socket.");
const restoredSocket = MockWebSocket.instances[2];
restoredSocket.open();
assert(restoredSocket.sent.some(item => item.type === "tournament.subscribe" && item.tournamentId === 7));
assert(restoredSocket.sent.some(item => item.type === "videos.subscribe"));

realtime.unsubscribeTournament(7);
realtime.unsubscribeMatch(9);
realtime.unsubscribePayment("QA-123");
realtime.unsubscribeVideos();
assert(restoredSocket.sent.some(item => item.type === "tournament.unsubscribe" && item.tournamentId === 7));
assert(restoredSocket.sent.some(item => item.type === "match.unsubscribe" && item.matchId === 9));
assert(restoredSocket.sent.some(item => item.type === "payment.unsubscribe" && item.transactionCode === "QA-123"));
assert(restoredSocket.sent.some(item => item.type === "videos.unsubscribe"));

removeListener();
(windowEvents.pagehide || []).forEach(listener => listener({ persisted: false }));

console.log("public-realtime client tests passed");
