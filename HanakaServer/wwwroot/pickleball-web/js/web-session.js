(function () {
    "use strict";

    const unavailableMessage = "Chưa kiểm tra được phiên đăng nhập. Vui lòng thử lại hoặc tải lại trang.";

    function isAborted(error) {
        return !!error && (error.name === "AbortError" || error.status === 499);
    }

    async function fetchSession(options) {
        const response = await window.fetch("/api/web-auth/me", Object.assign({
            cache: "no-store",
            credentials: "same-origin",
            headers: { Accept: "application/json" }
        }, options, { method: "GET" }));

        if (response.status === 401) {
            return { isAuthenticated: false };
        }

        if (!response.ok) {
            const error = new Error(unavailableMessage);
            error.status = response.status;
            if (response.status === 499) error.name = "AbortError";
            throw error;
        }

        const session = await response.json();
        if (!session || typeof session.isAuthenticated !== "boolean" ||
            (session.isAuthenticated && !session.user)) {
            throw new Error(unavailableMessage);
        }

        return session;
    }

    async function read(options) {
        try {
            return await fetchSession(options);
        } catch (error) {
            if (isAborted(error)) throw error;
            const unavailable = new Error(unavailableMessage);
            unavailable.isSessionUnavailable = true;
            unavailable.status = error && error.status;
            throw unavailable;
        }
    }

    // Callers only replace their last confirmed session after a successful read.
    // Errors and cancellation must not be converted into a signed-out session.
    window.HanakaWebSession = { read, isAborted, unavailableMessage };
})();
