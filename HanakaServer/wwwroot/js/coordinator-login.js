(() => {
    "use strict";
    const form = document.getElementById("coordinator-login");
    let busy = false;
    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (busy || !form.reportValidity()) return;
        busy = true;
        const button = form.querySelector("button[type=submit]");
        const error = document.getElementById("login-error");
        button.disabled = true;
        button.textContent = "Đang đăng nhập…";
        error.textContent = "";
        try {
            await window.HanakaCoordinatorHttp.request("/api/coordinator-portal/login", {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/json", RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value },
                validate: body => body?.redirectUrl === "/CoordinatorPortal/Matches",
                body: JSON.stringify({ account: document.getElementById("account").value.trim(),
                    password: document.getElementById("password").value, rememberMe: document.getElementById("remember").checked })
            });
            window.location.assign("/CoordinatorPortal/Matches");
        } catch (ex) { error.textContent = ex.message || "Không thể kết nối máy chủ."; }
        finally { busy = false; button.disabled = false; button.textContent = "Đăng nhập điều phối"; }
    });
})();
