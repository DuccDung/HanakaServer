(() => {
    "use strict";
    async function request(path, { timeoutMs = 20000, validate, signal, ...options } = {}) {
        const controller = new AbortController();
        const cancel = () => controller.abort();
        if (signal?.aborted) controller.abort();
        else signal?.addEventListener("abort", cancel, { once: true });
        let timedOut = false, status;
        const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
        try {
            const response = await fetch(path, { credentials: "same-origin", cache: "no-store", ...options, signal: controller.signal });
            status = response.status;
            let body;
            try { body = await response.json(); }
            catch (error) {
                if (controller.signal.aborted) throw error;
                if (response.ok) throw new Error("Máy chủ trả dữ liệu không hợp lệ. Vui lòng tải lại để kiểm tra.");
                body = {};
            }
            if (!response.ok) {
                const error = new Error(body?.message || (status === 400
                    ? "Yêu cầu không hợp lệ hoặc phiên xác nhận đã hết hạn. Vui lòng tải lại trang."
                    : "Không tải được dữ liệu. Vui lòng thử lại."));
                error.status = status; throw error;
            }
            if (validate && !validate(body)) throw new Error("Dữ liệu trả về chưa đầy đủ. Vui lòng tải lại để kiểm tra.");
            return body;
        } catch (cause) {
            const error = timedOut ? new Error("Máy chủ phản hồi quá lâu. Vui lòng tải lại để kiểm tra kết quả.") : cause;
            // A failed response does not prove a write failed. Never automatically replay it.
            error.uncertain = options.method && !["GET", "HEAD"].includes(options.method.toUpperCase())
                && (status === undefined || status >= 500 || (status >= 200 && status < 300));
            throw error;
        } finally {
            clearTimeout(timer); signal?.removeEventListener("abort", cancel);
        }
    }
    window.HanakaCoordinatorHttp = Object.freeze({ request });
})();
