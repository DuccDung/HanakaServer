(function () {
    var COMMUNITY_TERMS_KEY = "communityTermsState_v1";
    var COMMUNITY_TERMS_VERSION = "2026-04-09";
    var popupState = {
        shell: null,
        onConfirm: null,
        onSecondary: null,
        onDismiss: null,
        allowDismiss: true,
        busy: false
    };
    var popupCloseTimer = 0;

    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    function escapeHtml(value) {
        return trimToEmpty(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function isEmail(value) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimToEmpty(value));
    }

    function normalizePhone(value) {
        var raw = trimToEmpty(value);
        if (!raw) {
            return "";
        }

        var hasLeadingPlus = raw.charAt(0) === "+";
        var digits = raw.replace(/[^\d]/g, "");
        if (!digits) {
            return "";
        }

        return hasLeadingPlus ? "+" + digits : digits;
    }

    function isPhone(value) {
        var normalized = normalizePhone(value);
        if (!normalized) {
            return false;
        }

        var digitsLength = normalized.charAt(0) === "+" ? normalized.length - 1 : normalized.length;
        return digitsLength >= 9 && digitsLength <= 15;
    }

    function isSafeLocalUrl(value) {
        return value && value.charAt(0) === "/" && !value.startsWith("//");
    }

    function getReturnUrl(root) {
        var raw = trimToEmpty(root.getAttribute("data-return-url"));
        return isSafeLocalUrl(raw) ? raw : "/";
    }

    function buildLoginHref(root, email) {
        var url = new URL("/PickleballWeb/Login", window.location.origin);
        url.searchParams.set("returnUrl", getReturnUrl(root));

        var normalizedEmail = trimToEmpty(email);
        if (normalizedEmail) {
            url.searchParams.set("email", normalizedEmail);
        }

        return url.pathname + url.search;
    }

    function buildForgotPasswordHref(root, email) {
        var url = new URL("/PickleballWeb/ForgotPassword", window.location.origin);
        url.searchParams.set("returnUrl", getReturnUrl(root));

        var normalizedEmail = trimToEmpty(email);
        if (normalizedEmail) {
            url.searchParams.set("email", normalizedEmail);
        }

        return url.pathname + url.search;
    }

    function safeRedirect(url) {
        window.location.href = isSafeLocalUrl(url) ? url : "/";
    }

    function ensurePopup() {
        if (popupState.shell) {
            return popupState.shell;
        }

        var popup = document.createElement("div");
        popup.className = "auth-popup";
        popup.hidden = true;
        popup.setAttribute("data-auth-popup", "");
        popup.innerHTML = [
            '<div class="auth-popup__backdrop" data-auth-popup-dismiss></div>',
            '<div class="auth-popup__dialog" role="dialog" aria-modal="true" aria-labelledby="auth-popup-title" aria-describedby="auth-popup-message">',
            '    <button class="auth-popup__close" type="button" aria-label="Đóng popup" data-auth-popup-dismiss>',
            '        <ion-icon name="close-outline"></ion-icon>',
            "    </button>",
            '    <div class="auth-popup__badge">',
            '        <ion-icon data-auth-popup-icon name="notifications-outline"></ion-icon>',
            "    </div>",
            '    <div class="auth-popup__header">',
            '        <h3 class="auth-popup__title" id="auth-popup-title" data-auth-popup-title>Thông báo</h3>',
            '        <p class="auth-popup__message" id="auth-popup-message" data-auth-popup-message hidden></p>',
            "    </div>",
            '    <div class="auth-popup__content" data-auth-popup-content></div>',
            '    <div class="auth-popup__error" data-auth-popup-error hidden>',
            '        <p data-auth-popup-error-text></p>',
            "    </div>",
            '    <p class="auth-popup__note" data-auth-popup-note hidden></p>',
            '    <div class="auth-popup__actions">',
            '        <button class="auth-popup__button auth-popup__button--secondary" type="button" data-auth-popup-secondary hidden></button>',
            '        <button class="auth-popup__button" type="button" data-auth-popup-confirm>Đã hiểu</button>',
            "    </div>",
            "</div>"
        ].join("");

        document.body.appendChild(popup);

        qsa("[data-auth-popup-dismiss]", popup).forEach(function (button) {
            button.addEventListener("click", function () {
                handlePopupDismiss();
            });
        });

        qs("[data-auth-popup-confirm]", popup).addEventListener("click", function () {
            handlePopupAction("confirm");
        });

        qs("[data-auth-popup-secondary]", popup).addEventListener("click", function () {
            handlePopupAction("secondary");
        });

        popupState.shell = popup;
        return popup;
    }

    function handlePopupAction(kind) {
        if (!popupState.shell) {
            return;
        }

        var handler = kind === "secondary" ? popupState.onSecondary : popupState.onConfirm;
        if (typeof handler === "function") {
            handler();
        }
    }

    function handlePopupDismiss() {
        if (!popupState.shell || popupState.busy || popupState.allowDismiss === false) {
            return;
        }

        if (typeof popupState.onDismiss === "function") {
            popupState.onDismiss();
            return;
        }

        hidePopup();
    }

    function setPopupBusy(isBusy) {
        popupState.busy = !!isBusy;
        var popup = popupState.shell;
        if (!popup) {
            return;
        }

        popup.classList.toggle("is-busy", !!isBusy);
        var closeButton = qs("[data-auth-popup-dismiss]", popup);
        if (closeButton) {
            closeButton.disabled = !!isBusy;
        }
    }

    function clearPopupMessages() {
        setPopupError("");
        setPopupNote("");
    }

    function setPopupError(message) {
        var popup = popupState.shell;
        if (!popup) {
            return;
        }

        var box = qs("[data-auth-popup-error]", popup);
        var text = qs("[data-auth-popup-error-text]", popup);
        var normalized = trimToEmpty(message);

        if (!box || !text) {
            return;
        }

        text.textContent = normalized;
        box.hidden = !normalized;
    }

    function setPopupNote(message) {
        var popup = popupState.shell;
        if (!popup) {
            return;
        }

        var note = qs("[data-auth-popup-note]", popup);
        var normalized = trimToEmpty(message);

        if (!note) {
            return;
        }

        note.textContent = normalized;
        note.hidden = !normalized;
    }

    function setPopupButtonState(role, active, loading, idleText, loadingText) {
        var popup = popupState.shell;
        if (!popup) {
            return;
        }

        var selector = role === "secondary" ? "[data-auth-popup-secondary]" : "[data-auth-popup-confirm]";
        var button = qs(selector, popup);

        if (!button || button.hidden) {
            return;
        }

        button.disabled = !active || loading;
        button.classList.toggle("is-loading", !!loading);
        button.textContent = loading ? loadingText : idleText;
    }

    function showPopupFrame(options) {
        var popup = ensurePopup();
        var title = trimToEmpty(options && options.title) || "Thông báo";
        var message = trimToEmpty(options && options.message);
        var variant = trimToEmpty(options && options.variant) || "success";
        var icon = trimToEmpty(options && options.icon) ||
            (variant === "success" ? "checkmark-circle-outline" : "mail-open-outline");
        var contentHtml = options && typeof options.contentHtml === "string" ? options.contentHtml : "";
        var confirmText = trimToEmpty(options && options.confirmText) || "Đã hiểu";
        var secondaryText = trimToEmpty(options && options.secondaryText);
        var focusSelector = trimToEmpty(options && options.focusSelector);

        popup.classList.remove("auth-popup--success", "auth-popup--info");
        popup.classList.add(variant === "info" ? "auth-popup--info" : "auth-popup--success");

        qs("[data-auth-popup-title]", popup).textContent = title;
        qs("[data-auth-popup-icon]", popup).setAttribute("name", icon);

        var messageNode = qs("[data-auth-popup-message]", popup);
        if (messageNode) {
            messageNode.textContent = message;
            messageNode.hidden = !message;
        }

        var contentNode = qs("[data-auth-popup-content]", popup);
        if (contentNode) {
            contentNode.innerHTML = contentHtml;
        }

        clearPopupMessages();
        setPopupBusy(false);

        var confirmButton = qs("[data-auth-popup-confirm]", popup);
        var secondaryButton = qs("[data-auth-popup-secondary]", popup);

        if (confirmButton) {
            confirmButton.hidden = false;
            confirmButton.textContent = confirmText;
            confirmButton.disabled = !!options.confirmDisabled;
            confirmButton.classList.remove("is-loading");
        }

        if (secondaryButton) {
            secondaryButton.hidden = !secondaryText;
            secondaryButton.textContent = secondaryText;
            secondaryButton.disabled = false;
            secondaryButton.classList.remove("is-loading");
        }

        popupState.onConfirm = options && typeof options.onConfirm === "function" ? options.onConfirm : null;
        popupState.onSecondary = options && typeof options.onSecondary === "function" ? options.onSecondary : null;
        popupState.onDismiss = options && typeof options.onDismiss === "function" ? options.onDismiss : null;
        popupState.allowDismiss = !options || options.allowDismiss !== false;

        popup.hidden = false;
        document.body.classList.add("auth-popup-open");
        window.clearTimeout(popupCloseTimer);

        window.requestAnimationFrame(function () {
            popup.classList.add("is-visible");

            var target = focusSelector ? qs(focusSelector, popup) : null;
            if (!target) {
                target = confirmButton;
            }

            if (target && typeof target.focus === "function") {
                target.focus();
            }
        });

        return popup;
    }

    function hidePopup() {
        var popup = popupState.shell;
        if (!popup || popup.hidden) {
            return Promise.resolve();
        }

        popup.classList.remove("is-visible");
        document.body.classList.remove("auth-popup-open");
        popupState.onConfirm = null;
        popupState.onSecondary = null;
        popupState.onDismiss = null;
        popupState.allowDismiss = true;
        popupState.busy = false;

        window.clearTimeout(popupCloseTimer);

        return new Promise(function (resolve) {
            popupCloseTimer = window.setTimeout(function () {
                popup.hidden = true;
                var contentNode = qs("[data-auth-popup-content]", popup);
                if (contentNode) {
                    contentNode.innerHTML = "";
                }
                clearPopupMessages();
                resolve();
            }, 220);
        });
    }

    function showMessagePopup(options) {
        return new Promise(function (resolve) {
            showPopupFrame({
                title: options && options.title,
                message: options && options.message,
                confirmText: options && options.confirmText,
                variant: options && options.variant,
                icon: options && options.icon,
                allowDismiss: options && options.allowDismiss,
                onConfirm: async function () {
                    await hidePopup();
                    resolve("confirm");
                },
                onDismiss: async function () {
                    await hidePopup();
                    resolve("dismiss");
                }
            });
        });
    }

    async function parseResponsePayload(response) {
        var contentType = response.headers.get("content-type") || "";

        if (contentType.indexOf("application/json") >= 0) {
            return response.json().catch(function () { return null; });
        }

        return response.text().catch(function () { return ""; });
    }

    function getErrorMessage(payload, fallback) {
        if (!payload) {
            return fallback;
        }

        if (typeof payload === "string") {
            return payload;
        }

        if (typeof payload.message === "string" && payload.message) {
            return payload.message;
        }

        if (typeof payload.title === "string" && payload.title) {
            return payload.title;
        }

        return fallback;
    }

    async function requestJson(url, options) {
        var requestOptions = Object.assign({
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json"
            },
            credentials: "same-origin"
        }, options || {});

        if (requestOptions.headers && options && options.headers) {
            requestOptions.headers = Object.assign({}, requestOptions.headers, options.headers);
        }

        var response = await fetch(url, requestOptions);
        var payload = await parseResponsePayload(response);

        if (!response.ok) {
            throw new Error(getErrorMessage(payload, "Yêu cầu không thành công."));
        }

        return payload;
    }

    function setError(root, message) {
        var box = qs("[data-auth-error]", root);
        var text = qs("[data-auth-error-text]", root);

        if (!box || !text) {
            return;
        }

        text.textContent = trimToEmpty(message);
        box.hidden = !trimToEmpty(message);
    }

    function setSubmitState(root, active, loading, idleText, loadingText) {
        var button = qs("[data-auth-submit]", root);
        var text = qs("[data-auth-submit-text]", root);

        if (!button || !text) {
            return;
        }

        button.disabled = !active || loading;
        button.classList.toggle("is-active", active || loading);
        button.classList.toggle("is-loading", !!loading);
        text.textContent = loading ? loadingText : idleText;
    }

    function bindPasswordToggles(root) {
        qsa("[data-auth-toggle-password]", root).forEach(function (button) {
            var wrap = button.closest(".auth-input-wrap");
            var input = qs('input[type="password"], input[type="text"]', wrap);
            var icon = qs("ion-icon", button);

            if (!input || !icon) {
                return;
            }

            button.addEventListener("click", function () {
                var showing = input.type === "text";
                input.type = showing ? "password" : "text";
                icon.setAttribute("name", showing ? "eye-outline" : "eye-off-outline");
                button.setAttribute("aria-label", showing ? "Hiện mật khẩu" : "Ẩn mật khẩu");
            });
        });
    }

    async function getSession() {
        try {
            return await requestJson("/api/web-auth/me", {
                method: "GET",
                headers: { Accept: "application/json" }
            });
        } catch (error) {
            return { isAuthenticated: false };
        }
    }

    async function redirectIfAuthenticated(root) {
        var session = await getSession();
        if (session && session.isAuthenticated) {
            safeRedirect(getReturnUrl(root));
            return true;
        }

        return false;
    }

    function persistCommunityTerms() {
        try {
            localStorage.setItem(COMMUNITY_TERMS_KEY, JSON.stringify({
                version: COMMUNITY_TERMS_VERSION,
                acceptedAt: new Date().toISOString(),
                accepted: true,
                source: "web_register_otp"
            }));
        } catch (error) {
            // Ignore storage failures on the web shell.
        }
    }

    async function showForgotPasswordOtpPopup(root, email) {
        return new Promise(function (resolve) {
            showPopupFrame({
                title: "Xác thực OTP",
                message: "Nhập mã OTP vừa được gửi tới email của bạn để tiếp tục.",
                confirmText: "Xác nhận OTP",
                secondaryText: "Gửi lại OTP",
                variant: "info",
                icon: "mail-unread-outline",
                focusSelector: "[data-auth-reset-otp]",
                contentHtml: [
                    '<form class="auth-popup__form" data-auth-popup-form="forgot-otp" novalidate>',
                    '    <p class="auth-popup__email-pill">' + escapeHtml(email) + "</p>",
                    '    <div class="auth-field-block">',
                    '        <div class="auth-label-row">',
                    '            <label class="auth-label" for="auth-popup-reset-otp">Mã OTP</label>',
                    "        </div>",
                    '        <div class="auth-input-wrap">',
                    '            <input id="auth-popup-reset-otp"',
                    '                   class="auth-input auth-input--otp"',
                    '                   type="text"',
                    '                   inputmode="numeric"',
                    '                   maxlength="6"',
                    '                   placeholder="Nhập 6 số"',
                    '                   autocomplete="one-time-code"',
                    '                   data-auth-reset-otp />',
                    "        </div>",
                    "    </div>",
                    '    <p class="auth-popup__footnote">Nếu chưa thấy email, hãy kiểm tra mục thư rác hoặc gửi lại mã OTP.</p>',
                    "</form>"
                ].join("")
            });

            var popup = ensurePopup();
            var form = qs('[data-auth-popup-form="forgot-otp"]', popup);
            var otpInput = qs("[data-auth-reset-otp]", popup);
            var verifying = false;
            var resending = false;

            function syncButton() {
                var canSubmit = trimToEmpty(otpInput && otpInput.value).length === 6;
                setPopupButtonState("confirm", canSubmit && !resending, verifying, "Xác nhận OTP", "Đang xác nhận...");
                setPopupButtonState("secondary", !verifying, resending, "Gửi lại OTP", "Đang gửi lại...");
                return canSubmit;
            }

            popupState.onDismiss = async function () {
                await hidePopup();
                resolve(null);
            };

            popupState.onSecondary = async function () {
                if (resending || verifying) {
                    return;
                }

                resending = true;
                clearPopupMessages();
                syncButton();

                try {
                    var payload = await requestJson("/api/web-auth/forgot-password", {
                        method: "POST",
                        body: JSON.stringify({
                            email: email
                        })
                    });

                    setPopupNote(payload && payload.message
                        ? payload.message
                        : "Nếu email tồn tại và tài khoản đã kích hoạt, mã OTP đã được gửi về hộp thư của bạn.");
                } catch (error) {
                    setPopupError(error.message || "Không thể gửi lại OTP lúc này.");
                } finally {
                    resending = false;
                    syncButton();
                }
            };

            popupState.onConfirm = function () {
                if (form) {
                    form.requestSubmit();
                }
            };

            if (otpInput) {
                otpInput.addEventListener("input", function () {
                    otpInput.value = trimToEmpty(otpInput.value).replace(/[^0-9]/g, "").slice(0, 6);
                    setPopupError("");
                    syncButton();
                });
            }

            if (form) {
                form.addEventListener("submit", async function (event) {
                    event.preventDefault();
                    if (verifying || !syncButton()) {
                        return;
                    }

                    verifying = true;
                    setPopupBusy(true);
                    clearPopupMessages();
                    syncButton();

                    try {
                        var otp = trimToEmpty(otpInput.value);
                        await requestJson("/api/web-auth/forgot-password/verify-otp", {
                            method: "POST",
                            body: JSON.stringify({
                                email: email,
                                otp: otp
                            })
                        });

                        await hidePopup();
                        resolve(otp);
                    } catch (error) {
                        setPopupError(error.message || "OTP không đúng hoặc đã hết hạn.");
                    } finally {
                        verifying = false;
                        setPopupBusy(false);
                        if (popupState.shell && !popupState.shell.hidden) {
                            syncButton();
                        }
                    }
                });
            }

            syncButton();
        });
    }

    async function showForgotPasswordResetPopup(root, email, otp) {
        return new Promise(function (resolve) {
            showPopupFrame({
                title: "Đặt mật khẩu mới",
                message: "OTP đã hợp lệ. Nhập mật khẩu mới để hoàn tất và đăng nhập ngay.",
                confirmText: "Đổi mật khẩu",
                secondaryText: "Hủy",
                variant: "success",
                icon: "key-outline",
                focusSelector: "[data-auth-reset-password]",
                contentHtml: [
                    '<form class="auth-popup__form" data-auth-popup-form="forgot-reset" novalidate>',
                    '    <div class="auth-field-block">',
                    '        <div class="auth-label-row">',
                    '            <label class="auth-label" for="auth-popup-reset-password">Mật khẩu mới</label>',
                    "        </div>",
                    '        <div class="auth-input-wrap auth-input-wrap--password">',
                    '            <input id="auth-popup-reset-password"',
                    '                   class="auth-input"',
                    '                   type="password"',
                    '                   placeholder="Tối thiểu 6 ký tự"',
                    '                   autocomplete="new-password"',
                    '                   data-auth-reset-password />',
                    '            <button class="auth-eye-btn" type="button" aria-label="Hiện mật khẩu" data-auth-toggle-password>',
                    '                <ion-icon name="eye-outline"></ion-icon>',
                    "            </button>",
                    "        </div>",
                    "    </div>",
                    '    <div class="auth-field-block">',
                    '        <div class="auth-label-row">',
                    '            <label class="auth-label" for="auth-popup-reset-password-confirm">Nhập lại mật khẩu mới</label>',
                    "        </div>",
                    '        <div class="auth-input-wrap auth-input-wrap--password">',
                    '            <input id="auth-popup-reset-password-confirm"',
                    '                   class="auth-input"',
                    '                   type="password"',
                    '                   placeholder="Nhập lại để tránh nhầm"',
                    '                   autocomplete="new-password"',
                    '                   data-auth-reset-password-confirm />',
                    '            <button class="auth-eye-btn" type="button" aria-label="Hiện mật khẩu" data-auth-toggle-password>',
                    '                <ion-icon name="eye-outline"></ion-icon>',
                    "            </button>",
                    "        </div>",
                    "    </div>",
                    '    <p class="auth-popup__footnote">Mật khẩu phải có ít nhất 6 ký tự.</p>',
                    "</form>"
                ].join("")
            });

            var popup = ensurePopup();
            var form = qs('[data-auth-popup-form="forgot-reset"]', popup);
            var newPasswordInput = qs("[data-auth-reset-password]", popup);
            var confirmPasswordInput = qs("[data-auth-reset-password-confirm]", popup);
            var resetting = false;

            bindPasswordToggles(qs("[data-auth-popup-content]", popup));

            function validate() {
                var newPassword = trimToEmpty(newPasswordInput && newPasswordInput.value);
                var confirmPassword = trimToEmpty(confirmPasswordInput && confirmPasswordInput.value);

                if (newPassword.length < 6) {
                    return "Mật khẩu mới phải có ít nhất 6 ký tự.";
                }

                if (newPassword !== confirmPassword) {
                    return "Mật khẩu nhập lại không khớp.";
                }

                return "";
            }

            function syncButton() {
                var canSubmit = trimToEmpty(newPasswordInput && newPasswordInput.value).length >= 6 &&
                    trimToEmpty(confirmPasswordInput && confirmPasswordInput.value).length >= 6;

                setPopupButtonState("confirm", canSubmit, resetting, "Đổi mật khẩu", "Đang cập nhật...");
                setPopupButtonState("secondary", !resetting, false, "Hủy", "Hủy");
                return canSubmit;
            }

            popupState.onDismiss = async function () {
                if (resetting) {
                    return;
                }

                await hidePopup();
                resolve(false);
            };

            popupState.onSecondary = async function () {
                if (resetting) {
                    return;
                }

                await hidePopup();
                resolve(false);
            };

            popupState.onConfirm = function () {
                if (form) {
                    form.requestSubmit();
                }
            };

            [newPasswordInput, confirmPasswordInput].forEach(function (input) {
                if (!input) {
                    return;
                }

                input.addEventListener("input", function () {
                    setPopupError("");
                    syncButton();
                });
            });

            if (form) {
                form.addEventListener("submit", async function (event) {
                    event.preventDefault();
                    if (resetting || !syncButton()) {
                        return;
                    }

                    var validationError = validate();
                    if (validationError) {
                        setPopupError(validationError);
                        syncButton();
                        return;
                    }

                    resetting = true;
                    setPopupBusy(true);
                    clearPopupMessages();
                    syncButton();

                    try {
                        await requestJson("/api/web-auth/forgot-password/reset", {
                            method: "POST",
                            body: JSON.stringify({
                                email: email,
                                otp: otp,
                                newPassword: trimToEmpty(newPasswordInput.value),
                                confirmPassword: trimToEmpty(confirmPasswordInput.value)
                            })
                        });

                        await hidePopup();
                        resolve(true);
                    } catch (error) {
                        setPopupError(error.message || "Không thể cập nhật mật khẩu lúc này.");
                    } finally {
                        resetting = false;
                        setPopupBusy(false);
                        if (popupState.shell && !popupState.shell.hidden) {
                            syncButton();
                        }
                    }
                });
            }

            syncButton();
        });
    }

    function initLoginPage(root) {
        var form = qs('[data-auth-form="login"]', root);
        var emailInput = qs("[data-auth-email]", root);
        var passwordInput = qs("[data-auth-password]", root);
        var forgotLink = qs("[data-auth-forgot-link]", root);
        var loading = false;

        function syncButton() {
            var canSubmit = isEmail(emailInput && emailInput.value) &&
                trimToEmpty(passwordInput && passwordInput.value).length >= 6;

            setSubmitState(root, canSubmit, loading, "Đăng nhập", "Đang đăng nhập...");
            if (forgotLink) {
                forgotLink.href = buildForgotPasswordHref(root, emailInput && emailInput.value);
            }

            return canSubmit;
        }

        [emailInput, passwordInput].forEach(function (input) {
            if (!input) {
                return;
            }

            input.addEventListener("input", function () {
                setError(root, "");
                syncButton();
            });
        });

        form.addEventListener("submit", async function (event) {
            event.preventDefault();
            if (loading || !syncButton()) {
                return;
            }

            loading = true;
            setError(root, "");
            syncButton();

            try {
                await requestJson("/api/web-auth/login", {
                    method: "POST",
                    body: JSON.stringify({
                        identifier: trimToEmpty(emailInput.value),
                        password: passwordInput.value || ""
                    })
                });

                safeRedirect(getReturnUrl(root));
            } catch (error) {
                setError(root, error.message || "Sai thông tin đăng nhập.");
            } finally {
                loading = false;
                syncButton();
            }
        });

        bindPasswordToggles(root);
        syncButton();
    }

    function initForgotPasswordPage(root) {
        var form = qs('[data-auth-form="forgot-password"]', root);
        var emailInput = qs("[data-auth-email]", root);
        var loading = false;

        function syncButton() {
            var canSubmit = isEmail(emailInput && emailInput.value);
            setSubmitState(root, canSubmit, loading, "Gửi mã OTP", "Đang gửi email...");
            return canSubmit;
        }

        emailInput.addEventListener("input", function () {
            setError(root, "");
            syncButton();
        });

        form.addEventListener("submit", async function (event) {
            event.preventDefault();
            if (loading || !syncButton()) {
                return;
            }

            loading = true;
            setError(root, "");
            syncButton();

            try {
                var email = trimToEmpty(emailInput.value);
                await requestJson("/api/web-auth/forgot-password", {
                    method: "POST",
                    body: JSON.stringify({
                        email: email
                    })
                });

                var otp = await showForgotPasswordOtpPopup(root, email);
                if (!otp) {
                    return;
                }

                var resetDone = await showForgotPasswordResetPopup(root, email, otp);
                if (!resetDone) {
                    return;
                }

                await showMessagePopup({
                    title: "Đổi mật khẩu thành công",
                    message: "Mật khẩu đã được cập nhật và bạn đã được đăng nhập tự động.",
                    confirmText: "Tiếp tục",
                    variant: "success",
                    icon: "checkmark-circle-outline"
                });

                safeRedirect(getReturnUrl(root));
            } catch (error) {
                setError(root, error.message || "Không thể gửi OTP lúc này.");
            } finally {
                loading = false;
                syncButton();
            }
        });

        syncButton();
    }

    function initRegisterPage(root) {
        var form = qs('[data-auth-form="register"]', root);
        var fullNameInput = qs("[data-auth-fullname]", root);
        var emailInput = qs("[data-auth-email]", root);
        var phoneInput = qs("[data-auth-phone]", root);
        var passwordInput = qs("[data-auth-password]", root);
        var genderButtons = qsa("[data-auth-gender-option]", root);
        var termsButton = qs("[data-auth-toggle-terms]", root);
        var loading = false;
        var selectedGender = "";
        var agreedToTerms = false;

        function syncGenderButtons() {
            genderButtons.forEach(function (button) {
                var active = button.getAttribute("data-auth-gender-option") === selectedGender;
                var icon = qs("ion-icon", button);

                button.classList.toggle("is-active", active);
                if (icon) {
                    icon.setAttribute("name", active ? "radio-button-on" : "radio-button-off");
                }
            });
        }

        function syncTerms() {
            if (termsButton) {
                termsButton.classList.toggle("is-active", agreedToTerms);
            }
        }

        function syncButton() {
            var canSubmit = trimToEmpty(fullNameInput && fullNameInput.value).length >= 2 &&
                isEmail(emailInput && emailInput.value) &&
                isPhone(phoneInput && phoneInput.value) &&
                trimToEmpty(selectedGender).length > 0 &&
                trimToEmpty(passwordInput && passwordInput.value).length >= 6 &&
                agreedToTerms;

            setSubmitState(root, canSubmit, loading, "Đăng ký", "Đang đăng ký...");
            return canSubmit;
        }

        function validateBeforeSubmit() {
            var fullName = trimToEmpty(fullNameInput.value);
            var email = trimToEmpty(emailInput.value);
            var phone = normalizePhone(phoneInput.value);
            var password = trimToEmpty(passwordInput.value);

            var phoneValidationError = !phone
                ? "Vui lòng nhập số điện thoại."
                : (!isPhone(phone) ? "Số điện thoại không hợp lệ." : "");

            if (!fullName) {
                return "Vui lòng nhập họ và tên.";
            }

            if (fullName.length < 2) {
                return "Họ và tên phải có ít nhất 2 ký tự.";
            }

            if (!isEmail(email)) {
                return "Email không hợp lệ.";
            }

            if (phoneValidationError) {
                return phoneValidationError;
            }

            if (!selectedGender) {
                return "Vui lòng chọn giới tính.";
            }

            if (password.length < 6) {
                return "Mật khẩu tối thiểu 6 ký tự.";
            }

            if (!agreedToTerms) {
                return "Bạn cần đồng ý Điều khoản sử dụng và Tiêu chuẩn cộng đồng trước khi tạo tài khoản.";
            }

            return "";
        }

        genderButtons.forEach(function (button) {
            button.addEventListener("click", function () {
                var value = button.getAttribute("data-auth-gender-option") || "";
                selectedGender = value;
                setError(root, "");
                syncGenderButtons();
                syncButton();
            });
        });

        if (termsButton) {
            termsButton.addEventListener("click", function () {
                agreedToTerms = !agreedToTerms;
                syncTerms();
                syncButton();
            });
        }

        [fullNameInput, emailInput, phoneInput, passwordInput].forEach(function (input) {
            if (!input) {
                return;
            }

            input.addEventListener("input", function () {
                setError(root, "");
                syncButton();
            });
        });

        if (phoneInput) {
            phoneInput.addEventListener("blur", function () {
                phoneInput.value = normalizePhone(phoneInput.value);
                syncButton();
            });
        }

        bindPasswordToggles(root);
        syncGenderButtons();
        syncTerms();
        syncButton();

        form.addEventListener("submit", async function (event) {
            event.preventDefault();
            if (loading) {
                return;
            }

            var validationError = validateBeforeSubmit();
            if (!validationError) {
                var normalizedPhone = normalizePhone(phoneInput.value);
                validationError = !normalizedPhone
                    ? "Vui lòng nhập số điện thoại."
                    : (!isPhone(normalizedPhone) ? "Số điện thoại không hợp lệ." : "");
            }
            if (validationError) {
                setError(root, validationError);
                syncButton();
                return;
            }

            loading = true;
            setError(root, "");
            syncButton();

            try {
                var payload = await requestJson("/api/web-auth/register", {
                    method: "POST",
                    body: JSON.stringify({
                        fullName: trimToEmpty(fullNameInput.value),
                        email: trimToEmpty(emailInput.value),
                        phone: normalizePhone(phoneInput.value),
                        password: passwordInput.value || "",
                        gender: selectedGender || null
                    })
                });

                await showMessagePopup({
                    title: "OTP đã được gửi",
                    message: payload && payload.message
                        ? payload.message
                        : "Vui lòng kiểm tra email để lấy mã OTP xác thực tài khoản.",
                    confirmText: "Nhập OTP",
                    variant: "info",
                    icon: "mail-outline"
                });

                var otpUrl = new URL("/PickleballWeb/RegisterOtp", window.location.origin);
                otpUrl.searchParams.set("email", trimToEmpty(emailInput.value));
                otpUrl.searchParams.set("fullName", trimToEmpty(fullNameInput.value));
                otpUrl.searchParams.set("agreedToTerms", agreedToTerms ? "true" : "false");
                otpUrl.searchParams.set("returnUrl", getReturnUrl(root));

                safeRedirect(otpUrl.pathname + otpUrl.search);
            } catch (error) {
                setError(root, error.message || "Đăng ký thất bại.");
            } finally {
                loading = false;
                syncButton();
            }
        });
    }

    function initOtpPage(root) {
        var form = qs('[data-auth-form="otp"]', root);
        var otpInput = qs("[data-auth-otp]", root);
        var resendButton = qs("[data-auth-resend]", root);
        var email = trimToEmpty(root.getAttribute("data-auth-email"));
        var fullName = trimToEmpty(root.getAttribute("data-auth-fullname"));
        var agreedToTerms = trimToEmpty(root.getAttribute("data-auth-terms")) === "true";
        var loading = false;
        var resending = false;

        function syncButton() {
            var canSubmit = trimToEmpty(otpInput && otpInput.value).length === 6;
            setSubmitState(root, canSubmit, loading, "Xác nhận OTP", "Đang xác nhận...");

            if (resendButton) {
                resendButton.disabled = resending || loading || !email;
                resendButton.textContent = resending ? "Đang gửi lại..." : "Gửi lại OTP";
            }

            return canSubmit;
        }

        if (qs("[data-auth-email-text]", root) && email) {
            qs("[data-auth-email-text]", root).textContent = email;
        }

        if (qs("[data-auth-fullname-text]", root) && fullName) {
            qs("[data-auth-fullname-text]", root).textContent = fullName;
        }

        otpInput.addEventListener("input", function () {
            var onlyDigits = trimToEmpty(otpInput.value).replace(/[^0-9]/g, "").slice(0, 6);
            otpInput.value = onlyDigits;
            setError(root, "");
            syncButton();
        });

        form.addEventListener("submit", async function (event) {
            event.preventDefault();
            if (loading || !syncButton() || !email) {
                return;
            }

            loading = true;
            setError(root, "");
            syncButton();

            try {
                await requestJson("/api/web-auth/confirm-otp", {
                    method: "POST",
                    body: JSON.stringify({
                        email: email,
                        otp: trimToEmpty(otpInput.value)
                    })
                });

                if (agreedToTerms) {
                    persistCommunityTerms();
                }

                await showMessagePopup({
                    title: "Xác thực thành công",
                    message: "Tài khoản của bạn đã được kích hoạt. Hệ thống sẽ chuyển bạn về trang trước đó.",
                    confirmText: "Tiếp tục",
                    variant: "success",
                    icon: "checkmark-circle-outline"
                });

                safeRedirect(getReturnUrl(root));
            } catch (error) {
                setError(root, error.message || "Xác thực OTP thất bại.");
            } finally {
                loading = false;
                syncButton();
            }
        });

        if (resendButton) {
            resendButton.addEventListener("click", async function () {
                if (resending || loading || !email) {
                    return;
                }

                resending = true;
                setError(root, "");
                syncButton();

                try {
                    var payload = await requestJson("/api/web-auth/resend-otp", {
                        method: "POST",
                        body: JSON.stringify({
                            email: email
                        })
                    });

                    await showMessagePopup({
                        title: "Đã gửi lại OTP",
                        message: payload && payload.message
                            ? payload.message
                            : "Mã OTP mới đã được gửi về email của bạn.",
                        confirmText: "Đã hiểu",
                        variant: "info",
                        icon: "mail-outline"
                    });
                } catch (error) {
                    setError(root, error.message || "Gửi lại OTP thất bại.");
                } finally {
                    resending = false;
                    syncButton();
                }
            });
        }

        syncButton();
    }

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            handlePopupDismiss();
        }
    });

    document.addEventListener("DOMContentLoaded", async function () {
        var root = qs("[data-auth-page]");
        if (!root) {
            return;
        }

        var redirected = await redirectIfAuthenticated(root);
        if (redirected) {
            return;
        }

        var kind = trimToEmpty(root.getAttribute("data-auth-page"));

        if (kind === "login") {
            initLoginPage(root);
            return;
        }

        if (kind === "forgot-password") {
            initForgotPasswordPage(root);
            return;
        }

        if (kind === "register") {
            initRegisterPage(root);
            return;
        }

        if (kind === "otp") {
            initOtpPage(root);
        }
    });
})();
