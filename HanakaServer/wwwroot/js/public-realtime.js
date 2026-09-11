(function (window, document) {
    "use strict";

    if (window.HanakaPublicRealtime) return;

    var socket = null;
    var retryTimer = 0;
    var heartbeatTimer = 0;
    var retryAttempt = 0;
    var everOpened = false;
    var paused = false;
    var listeners = new Set();
    var tournaments = new Set();
    var matches = new Set();
    var payments = new Set();
    var videosSubscribed = false;
    var seenEventIds = new Set();
    var seenEventQueue = [];

    function socketUrl() {
        var scheme = window.location.protocol === "https:" ? "wss:" : "ws:";
        return scheme + "//" + window.location.host + "/ws-public";
    }

    function emit(message) {
        listeners.forEach(function (listener) {
            try {
                listener(message);
            } catch (error) {
                if (window.console && typeof window.console.error === "function") {
                    window.console.error("Public realtime listener failed.", error);
                }
            }
        });
    }

    function rememberEvent(eventId) {
        if (!eventId) return true;
        var key = String(eventId);
        if (seenEventIds.has(key)) return false;
        seenEventIds.add(key);
        seenEventQueue.push(key);
        if (seenEventQueue.length > 256) {
            seenEventIds.delete(seenEventQueue.shift());
        }
        return true;
    }

    function send(message) {
        if (!socket || socket.readyState !== window.WebSocket.OPEN) return false;
        try {
            socket.send(JSON.stringify(message));
            return true;
        } catch (_) {
            return false;
        }
    }

    function flushSubscriptions() {
        tournaments.forEach(function (id) {
            send({ type: "tournament.subscribe", tournamentId: id });
        });
        matches.forEach(function (id) {
            send({ type: "match.subscribe", matchId: id });
        });
        payments.forEach(function (code) {
            send({ type: "payment.subscribe", transactionCode: code });
        });
        if (videosSubscribed) send({ type: "videos.subscribe" });
    }

    function clearTimers() {
        if (retryTimer) window.clearTimeout(retryTimer);
        if (heartbeatTimer) window.clearInterval(heartbeatTimer);
        retryTimer = 0;
        heartbeatTimer = 0;
    }

    function hasDemand() {
        return listeners.size > 0 || tournaments.size > 0 || matches.size > 0
            || payments.size > 0 || videosSubscribed;
    }

    function scheduleReconnect() {
        if (paused || retryTimer || !hasDemand()) return;
        var base = Math.min(30000, 750 * Math.pow(2, Math.min(retryAttempt, 5)));
        var delay = Math.round(base * (0.8 + Math.random() * 0.4));
        retryAttempt += 1;
        retryTimer = window.setTimeout(function () {
            retryTimer = 0;
            connect();
        }, delay);
    }

    function connect() {
        if (paused || !window.WebSocket) return;
        if (socket && (socket.readyState === window.WebSocket.OPEN
            || socket.readyState === window.WebSocket.CONNECTING)) return;

        clearTimers();
        var connectingSocket;
        try {
            connectingSocket = new window.WebSocket(socketUrl());
            socket = connectingSocket;
        } catch (_) {
            socket = null;
            scheduleReconnect();
            return;
        }

        connectingSocket.addEventListener("open", function () {
            if (socket !== connectingSocket) return;
            var reconnected = everOpened;
            everOpened = true;
            retryAttempt = 0;
            flushSubscriptions();
            heartbeatTimer = window.setInterval(function () {
                send({ type: "ping" });
            }, 25000);
            emit({ type: "__public_socket_open__", reconnected: reconnected });
        });

        connectingSocket.addEventListener("message", function (event) {
            if (socket !== connectingSocket) return;
            var message;
            try {
                message = JSON.parse(event.data);
            } catch (_) {
                return;
            }
            if (!rememberEvent(message && message.eventId)) return;
            emit(message);
        });

        connectingSocket.addEventListener("close", function () {
            if (socket !== connectingSocket) return;
            clearTimers();
            socket = null;
            emit({ type: "__public_socket_close__" });
            scheduleReconnect();
        });

        connectingSocket.addEventListener("error", function () {
            if (socket !== connectingSocket) return;
            emit({ type: "__public_socket_error__" });
        });
    }

    function positiveId(value) {
        var id = Number(value || 0);
        return Number.isFinite(id) && id > 0 ? id : 0;
    }

    function paymentCode(value) {
        return String(value || "").trim();
    }

    var api = {
        connect: connect,
        send: send,
        on: function (listener) {
            if (typeof listener !== "function") return function () { };
            listeners.add(listener);
            connect();
            return function () { listeners.delete(listener); };
        },
        subscribeTournament: function (value) {
            var id = positiveId(value);
            if (!id) return;
            tournaments.add(id);
            connect();
            send({ type: "tournament.subscribe", tournamentId: id });
        },
        unsubscribeTournament: function (value) {
            var id = positiveId(value);
            if (!id) return;
            tournaments.delete(id);
            send({ type: "tournament.unsubscribe", tournamentId: id });
        },
        subscribeMatch: function (value) {
            var id = positiveId(value);
            if (!id) return;
            matches.add(id);
            connect();
            send({ type: "match.subscribe", matchId: id });
        },
        unsubscribeMatch: function (value) {
            var id = positiveId(value);
            if (!id) return;
            matches.delete(id);
            send({ type: "match.unsubscribe", matchId: id });
        },
        subscribePayment: function (value) {
            var code = paymentCode(value);
            if (!code) return;
            payments.add(code);
            connect();
            send({ type: "payment.subscribe", transactionCode: code });
        },
        unsubscribePayment: function (value) {
            var code = paymentCode(value);
            if (!code) return;
            payments.delete(code);
            send({ type: "payment.unsubscribe", transactionCode: code });
        },
        subscribeVideos: function () {
            videosSubscribed = true;
            connect();
            send({ type: "videos.subscribe" });
        },
        unsubscribeVideos: function () {
            videosSubscribed = false;
            send({ type: "videos.unsubscribe" });
        }
    };

    window.HanakaPublicRealtime = api;

    window.addEventListener("online", connect);
    window.addEventListener("pagehide", function () {
        paused = true;
        clearTimers();
        if (socket) {
            try { socket.close(1000, "pagehide"); } catch (_) { }
        }
        socket = null;
    });
    window.addEventListener("pageshow", function () {
        paused = false;
        if (hasDemand()) connect();
    });
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden && hasDemand()) connect();
    });
})(window, document);
