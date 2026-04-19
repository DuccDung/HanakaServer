(function () {
    var COMMUNITY_TERMS_KEY = "communityTermsState_v1";
    var COMMUNITY_TERMS_VERSION = "2026-04-09";

    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    function isEmail(value) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimToEmpty(value));
    }

    function isSafeLocalUrl(value) {
        return value && value.charAt(0) === "/" && !value.startsWith("//");
    }

    function getReturnUrl(root) {
        var raw = trimToEmpty(root.getAttribute("data-return-url"));
        return isSafeLocalUrl(raw) ? raw : "/";
    }

    function safeRedirect(url) {
        window.location.href = isSafeLocalUrl(url) ? url : "/";
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

    function initLoginPage(root) {
        var form = qs('[data-auth-form="login"]', root);
        var emailInput = qs("[data-auth-email]", root);
        var passwordInput = qs("[data-auth-password]", root);
        var loading = false;

        function syncButton() {
            var canSubmit = isEmail(emailInput && emailInput.value) &&
                trimToEmpty(passwordInput && passwordInput.value).length >= 6;

            setSubmitState(root, canSubmit, loading, "Đăng Nhập", "Đang đăng nhập...");
            return canSubmit;
        }

        function bindEvents() {
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
                    setError(root, error.message || "Sai thông tin đăng nhập!");
                } finally {
                    loading = false;
                    syncButton();
                }
            });
        }

        bindPasswordToggles(root);
        bindEvents();
        syncButton();
    }

    function initRegisterPage(root) {
        var form = qs('[data-auth-form="register"]', root);
        var fullNameInput = qs("[data-auth-fullname]", root);
        var emailInput = qs("[data-auth-email]", root);
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
                trimToEmpty(passwordInput && passwordInput.value).length >= 6 &&
                agreedToTerms;

            setSubmitState(root, canSubmit, loading, "Đăng ký", "Đang đăng ký...");
            return canSubmit;
        }

        function validateBeforeSubmit() {
            var fullName = trimToEmpty(fullNameInput.value);
            var email = trimToEmpty(emailInput.value);
            var password = trimToEmpty(passwordInput.value);

            if (!fullName) {
                return "Vui lòng nhập họ và tên.";
            }

            if (fullName.length < 2) {
                return "Họ và tên phải có ít nhất 2 ký tự.";
            }

            if (!isEmail(email)) {
                return "Email không hợp lệ.";
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
                selectedGender = selectedGender === value ? "" : value;
                syncGenderButtons();
            });
        });

        if (termsButton) {
            termsButton.addEventListener("click", function () {
                agreedToTerms = !agreedToTerms;
                syncTerms();
                syncButton();
            });
        }

        [fullNameInput, emailInput, passwordInput].forEach(function (input) {
            if (!input) {
                return;
            }

            input.addEventListener("input", function () {
                setError(root, "");
                syncButton();
            });
        });

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
                        password: passwordInput.value || "",
                        gender: selectedGender || null
                    })
                });

                window.alert(payload && payload.message
                    ? payload.message
                    : "OTP đã được gửi tới email.");

                var otpUrl = new URL("/PickleballWeb/RegisterOtp", window.location.origin);
                otpUrl.searchParams.set("email", trimToEmpty(emailInput.value));
                otpUrl.searchParams.set("fullName", trimToEmpty(fullNameInput.value));
                otpUrl.searchParams.set("agreedToTerms", agreedToTerms ? "true" : "false");
                otpUrl.searchParams.set("returnUrl", getReturnUrl(root));

                safeRedirect(otpUrl.pathname + otpUrl.search);
            } catch (error) {
                setError(root, error.message || "Đăng ký thất bại");
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

                window.alert("Xác thực OTP thành công.");
                safeRedirect(getReturnUrl(root));
            } catch (error) {
                setError(root, error.message || "Xác thực OTP thất bại");
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

                    window.alert(payload && payload.message
                        ? payload.message
                        : "OTP mới đã được gửi.");
                } catch (error) {
                    setError(root, error.message || "Gửi lại OTP thất bại");
                } finally {
                    resending = false;
                    syncButton();
                }
            });
        }

        syncButton();
    }

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

        if (kind === "register") {
            initRegisterPage(root);
            return;
        }

        if (kind === "otp") {
            initOtpPage(root);
        }
    });
})();
