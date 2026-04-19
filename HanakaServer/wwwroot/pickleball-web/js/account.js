(function () {
    var GENDERS = ["Nam", "Nữ", "Khác"];
    var PROVINCES = ["Bắc Giang", "Hà Nội", "TP. Hồ Chí Minh", "Đà Nẵng"];
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

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function safeLocalRedirect(url) {
        var href = trimToEmpty(url);
        window.location.href = href && href.charAt(0) === "/" && !href.startsWith("//")
            ? href
            : "/";
    }

    function formatDateDDMMYYYY(value) {
        if (!value) {
            return "";
        }

        var date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return "";
        }

        var dd = String(date.getDate()).padStart(2, "0");
        var mm = String(date.getMonth() + 1).padStart(2, "0");
        var yyyy = date.getFullYear();

        return dd + "/" + mm + "/" + yyyy;
    }

    function normalizeDateOnly(value) {
        var raw = trimToEmpty(value);
        if (!raw) {
            return "";
        }

        if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
            return raw;
        }

        if (/^\d{4}-\d{2}-\d{2}T/.test(raw)) {
            return raw.slice(0, 10);
        }

        var date = new Date(raw);
        if (Number.isNaN(date.getTime())) {
            return "";
        }

        var yyyy = date.getFullYear();
        var mm = String(date.getMonth() + 1).padStart(2, "0");
        var dd = String(date.getDate()).padStart(2, "0");

        return yyyy + "-" + mm + "-" + dd;
    }

    function normalizeAvatarUrl(value) {
        var normalized = trimToEmpty(value);
        if (!normalized || normalized === "null" || normalized === "undefined") {
            return "";
        }

        try {
            var resolved = new URL(normalized, window.location.origin);
            var isLocalPreviewHost = /^(localhost|127\.0\.0\.1)$/i.test(window.location.hostname);
            var isUploadAsset = resolved.pathname.toLowerCase().startsWith("/uploads/");

            if (isLocalPreviewHost && isUploadAsset && resolved.origin !== window.location.origin) {
                return window.location.origin + resolved.pathname + resolved.search;
            }

            return resolved.href;
        } catch (error) {
            return normalized;
        }
    }

    function createEmptyForm() {
        return {
            userId: "",
            fullName: "",
            phone: "",
            email: "",
            gender: "",
            province: "",
            bio: "",
            birthOfDate: "",
            avatarUrl: "",
            verified: false
        };
    }

    function normalizeProfile(value) {
        if (!value) {
            return createEmptyForm();
        }

        return {
            userId: value.userId != null ? String(value.userId) : "",
            fullName: trimToEmpty(value.fullName),
            phone: trimToEmpty(value.phone),
            email: trimToEmpty(value.email),
            gender: trimToEmpty(value.gender),
            province: trimToEmpty(value.city),
            bio: trimToEmpty(value.bio),
            birthOfDate: normalizeDateOnly(value.birthOfDate),
            avatarUrl: normalizeAvatarUrl(value.avatarUrl),
            verified: !!value.verified
        };
    }

    function cloneForm(value) {
        return {
            userId: value.userId,
            fullName: value.fullName,
            phone: value.phone,
            email: value.email,
            gender: value.gender,
            province: value.province,
            bio: value.bio,
            birthOfDate: value.birthOfDate,
            avatarUrl: value.avatarUrl,
            verified: value.verified
        };
    }

    function readCommunityTermsState() {
        try {
            var raw = localStorage.getItem(COMMUNITY_TERMS_KEY);
            if (!raw) {
                return {
                    accepted: false,
                    acceptedAt: null
                };
            }

            var payload = JSON.parse(raw);
            var isAccepted = !!payload &&
                payload.version === COMMUNITY_TERMS_VERSION &&
                !!(payload.accepted || payload.acceptedAt);

            return {
                accepted: isAccepted,
                acceptedAt: isAccepted ? trimToEmpty(payload.acceptedAt) : null
            };
        } catch (error) {
            return {
                accepted: false,
                acceptedAt: null
            };
        }
    }

    function getCommunityStateText() {
        var state = readCommunityTermsState();
        if (!state.accepted) {
            return "Chưa đồng ý điều khoản cộng đồng";
        }

        if (!state.acceptedAt) {
            return "Đã đồng ý điều khoản cộng đồng";
        }

        return "Đã đồng ý từ " + new Date(state.acceptedAt).toLocaleDateString("vi-VN");
    }

    async function parseResponsePayload(response) {
        var contentType = response.headers.get("content-type") || "";
        if (contentType.indexOf("application/json") >= 0) {
            return response.json().catch(function () { return null; });
        }

        return response.text().catch(function () { return ""; });
    }

    function responseMessage(payload, fallback) {
        if (!payload) {
            return fallback;
        }

        if (typeof payload === "string" && payload) {
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
        var response = await fetch(url, Object.assign({
            credentials: "same-origin"
        }, options || {}));
        var payload = await parseResponsePayload(response);

        if (!response.ok) {
            throw new Error(responseMessage(payload, "Yêu cầu không thành công."));
        }

        return payload;
    }

    async function requestForm(url, formData) {
        var response = await fetch(url, {
            method: "POST",
            body: formData,
            credentials: "same-origin"
        });
        var payload = await parseResponsePayload(response);

        if (!response.ok) {
            throw new Error(responseMessage(payload, "Upload thất bại."));
        }

        return payload;
    }

    function syncFieldValue(field, value) {
        if (!field) {
            return;
        }

        var nextValue = String(value ?? "");
        if (field.value !== nextValue) {
            field.value = nextValue;
        }
    }

    function setDisabled(node, disabled) {
        if (!node) {
            return;
        }

        if ("disabled" in node) {
            node.disabled = !!disabled;
        }

        node.classList.toggle("is-disabled", !!disabled);
    }

    function renderSelectOptions(listNode, options, selectedValue, onSelect) {
        if (!listNode) {
            return;
        }

        listNode.innerHTML = options.map(function (item) {
            var active = item === selectedValue;
            return [
                '<button class="account-modal__item" type="button" data-account-option="', escapeHtml(item), '">',
                "<span>", escapeHtml(item), "</span>",
                active ? '<ion-icon name="checkmark"></ion-icon>' : "",
                "</button>"
            ].join("");
        }).join("");

        qsa("[data-account-option]", listNode).forEach(function (button) {
            button.addEventListener("click", function () {
                onSelect(button.getAttribute("data-account-option") || "");
            });
        });
    }

    function toggleModal(modal, open) {
        if (!modal) {
            return;
        }

        modal.hidden = !open;
        document.documentElement.style.overflow = open ? "hidden" : "";
    }

    function initAccountPage(root) {
        var state = {
            booting: true,
            isAuthenticated: false,
            loading: false,
            avatarUploading: false,
            deleting: false,
            avatarRenderFailed: false,
            profile: null,
            form: createEmptyForm()
        };

        var refs = {
            avatarTrigger: qs("[data-account-avatar-trigger]", root),
            avatarInput: qs("[data-account-avatar-input]", root),
            avatar: qs("[data-account-avatar]", root),
            avatarImage: qs("[data-account-avatar-image]", root),
            avatarFallback: qs("[data-account-avatar-fallback]", root),
            cameraBadge: qs("[data-account-camera-badge]", root),
            avatarHint: qs("[data-account-avatar-hint]", root),
            verifiedText: qs("[data-account-verified-text]", root),
            communityText: qs("[data-account-community-text]", root),
            userId: qs("[data-account-user-id]", root),
            fullName: qs("[data-account-full-name]", root),
            phone: qs("[data-account-phone]", root),
            email: qs("[data-account-email]", root),
            dateTrigger: qs("[data-account-date-trigger]", root),
            dateText: qs("[data-account-date-text]", root),
            dateInput: qs("[data-account-date-input]", root),
            genderTrigger: qs("[data-account-gender-trigger]", root),
            genderText: qs("[data-account-gender-text]", root),
            provinceTrigger: qs("[data-account-province-trigger]", root),
            provinceText: qs("[data-account-province-text]", root),
            bio: qs("[data-account-bio]", root),
            error: qs("[data-account-error]", root),
            errorText: qs("[data-account-error-text]", root),
            updateButton: qs("[data-account-update]", root),
            updateText: qs("[data-account-update-text]", root),
            changePassword: qs("[data-account-change-password]", root),
            deleteButton: qs("[data-account-delete]", root),
            deleteText: qs("[data-account-delete-text]", root),
            logoutButton: qs("[data-account-logout]", root),
            genderModal: qs('[data-account-modal="gender"]', root),
            provinceModal: qs('[data-account-modal="province"]', root),
            genderList: qs("[data-account-gender-list]", root),
            provinceList: qs("[data-account-province-list]", root)
        };

        function showError(message) {
            if (!refs.error || !refs.errorText) {
                return;
            }

            var text = trimToEmpty(message);
            refs.error.hidden = !text;
            refs.errorText.textContent = text;
        }

        function accountLoginUrl() {
            return "/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/Account");
        }

        function changePasswordLoginUrl() {
            return "/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/ChangePassword");
        }

        function applyProfile(profile) {
            if (!profile) {
                state.avatarRenderFailed = false;
                state.profile = null;
                state.form = createEmptyForm();
                return;
            }

            state.profile = normalizeProfile(profile);
            state.form = cloneForm(state.profile);
            state.avatarRenderFailed = false;
        }

        function setFormField(name, value) {
            if (!state.form || !(name in state.form)) {
                return;
            }

            state.form[name] = value;
        }

        function verifiedStatusText() {
            if (state.booting && !state.profile) {
                return "Đang tải";
            }

            if (!state.isAuthenticated) {
                return "Chưa đăng nhập";
            }

            return state.form.verified ? "Đã xác thực" : "Chờ xác thực";
        }

        function requireLogin(message, redirectUrl) {
            if (state.isAuthenticated) {
                return false;
            }

            window.alert(message || "Vui lòng đăng nhập để tiếp tục.");
            safeLocalRedirect(redirectUrl || accountLoginUrl());
            return true;
        }

        function render() {
            var canInteract = state.isAuthenticated &&
                !state.booting &&
                !state.loading &&
                !state.avatarUploading &&
                !state.deleting;

            refs.verifiedText.textContent = verifiedStatusText();
            refs.communityText.textContent = getCommunityStateText();

            syncFieldValue(refs.userId, state.form.userId);
            syncFieldValue(refs.fullName, state.form.fullName);
            syncFieldValue(refs.phone, state.form.phone);
            syncFieldValue(refs.email, state.form.email);
            syncFieldValue(refs.bio, state.form.bio);
            syncFieldValue(refs.dateInput, state.form.birthOfDate);

            refs.dateText.textContent = state.form.birthOfDate
                ? formatDateDDMMYYYY(state.form.birthOfDate)
                : "Chọn ngày sinh";
            refs.genderText.textContent = state.form.gender || "Chọn giới tính";
            refs.provinceText.textContent = state.form.province || "Chọn tỉnh/thành";
            refs.avatarHint.textContent = state.isAuthenticated
                ? "Chạm để đổi ảnh đại diện"
                : "Đăng nhập để cập nhật ảnh";

            var hasRenderableAvatar = !!state.form.avatarUrl && !state.avatarRenderFailed;
            refs.avatar.hidden = !hasRenderableAvatar;
            refs.avatarFallback.hidden = hasRenderableAvatar;
            if (hasRenderableAvatar) {
                if (refs.avatarImage.getAttribute("src") !== state.form.avatarUrl) {
                    refs.avatarImage.src = state.form.avatarUrl;
                }
            } else {
                refs.avatarImage.removeAttribute("src");
            }

            refs.cameraBadge.classList.toggle("is-loading", state.avatarUploading);
            refs.cameraBadge.innerHTML = state.avatarUploading
                ? '<ion-icon name="sync-outline"></ion-icon>'
                : '<ion-icon name="camera"></ion-icon>';

            setDisabled(refs.avatarTrigger, !canInteract);
            setDisabled(refs.fullName, !canInteract);
            setDisabled(refs.phone, !canInteract);
            setDisabled(refs.bio, !canInteract);
            setDisabled(refs.dateTrigger, !canInteract);
            setDisabled(refs.genderTrigger, !canInteract);
            setDisabled(refs.provinceTrigger, !canInteract);

            refs.updateText.textContent = state.loading ? "Đang lưu..." : "Cập nhật thông tin";
            setDisabled(refs.updateButton, !state.isAuthenticated || state.booting || state.loading);

            refs.deleteText.textContent = state.deleting ? "Đang xóa tài khoản..." : "Xóa tài khoản";
            setDisabled(refs.deleteButton, !state.isAuthenticated || state.booting || state.deleting);
            setDisabled(refs.logoutButton, !state.isAuthenticated || state.booting);
            refs.changePassword.classList.toggle("is-disabled", !state.isAuthenticated || state.booting);

            renderSelectOptions(refs.genderList, GENDERS, state.form.gender, function (value) {
                state.form.gender = value;
                toggleModal(refs.genderModal, false);
                render();
            });

            renderSelectOptions(refs.provinceList, PROVINCES, state.form.province, function (value) {
                state.form.province = value;
                toggleModal(refs.provinceModal, false);
                render();
            });
        }

        async function loadProfile() {
            state.booting = true;
            showError("");
            render();

            var session = null;

            try {
                session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: {
                        Accept: "application/json"
                    }
                });
            } catch (error) {
                session = {
                    isAuthenticated: false
                };
            }

            state.isAuthenticated = !!(session && session.isAuthenticated);

            if (!state.isAuthenticated) {
                applyProfile(null);
                state.booting = false;
                render();
                return;
            }

            if (session.user) {
                applyProfile(session.user);
                render();
            }

            try {
                var profile = await requestJson("/api/users/me", {
                    method: "GET",
                    headers: {
                        Accept: "application/json"
                    }
                });

                applyProfile(profile);
            } catch (error) {
                if (!state.profile) {
                    showError(error.message || "Không lấy được thông tin tài khoản.");
                }
            } finally {
                state.booting = false;
                render();
            }
        }

        async function uploadAvatar(file) {
            if (requireLogin("Vui lòng đăng nhập để cập nhật ảnh đại diện.", accountLoginUrl())) {
                return;
            }

            if (!file) {
                return;
            }

            state.avatarUploading = true;
            showError("");
            render();

            try {
                var formData = new FormData();
                formData.append("file", file, file.name || "avatar.jpg");

                var payload = await requestForm("/api/users/me/avatar", formData);
                var nextAvatarUrl = normalizeAvatarUrl(payload && payload.avatarUrl);

                state.form.avatarUrl = nextAvatarUrl;
                state.avatarRenderFailed = false;
                if (state.profile) {
                    state.profile.avatarUrl = nextAvatarUrl;
                }

                window.alert("Đã cập nhật ảnh đại diện.");
            } catch (error) {
                showError(error.message || "Upload ảnh thất bại.");
            } finally {
                state.avatarUploading = false;
                render();
            }
        }

        async function updateProfile() {
            if (requireLogin("Vui lòng đăng nhập để cập nhật thông tin tài khoản.", accountLoginUrl())) {
                return;
            }

            state.loading = true;
            showError("");
            render();

            try {
                var payload = await requestJson("/api/users/me", {
                    method: "PUT",
                    headers: {
                        Accept: "application/json",
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({
                        fullName: trimToEmpty(state.form.fullName),
                        phone: trimToEmpty(state.form.phone),
                        gender: trimToEmpty(state.form.gender) || null,
                        city: trimToEmpty(state.form.province) || null,
                        bio: trimToEmpty(state.form.bio) || null,
                        birthOfDate: trimToEmpty(state.form.birthOfDate) || null,
                        avatarUrl: trimToEmpty(state.form.avatarUrl) || null
                    })
                });

                applyProfile(payload);
                window.alert("Cập nhật thông tin thành công.");
            } catch (error) {
                showError(error.message || "Cập nhật thất bại.");
            } finally {
                state.loading = false;
                render();
            }
        }

        async function logout() {
            try {
                await requestJson("/api/web-auth/logout", {
                    method: "POST",
                    headers: {
                        Accept: "application/json",
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({})
                });
            } catch (error) {
                // Ignore logout request failures and still clear the UI flow.
            }

            safeLocalRedirect("/");
        }

        async function deleteAccount() {
            if (requireLogin("Vui lòng đăng nhập để xóa tài khoản.", accountLoginUrl())) {
                return;
            }

            if (!window.confirm("Tài khoản sẽ bị vô hiệu hóa và bạn sẽ bị đăng xuất. Bạn có chắc chắn muốn tiếp tục?")) {
                return;
            }

            state.deleting = true;
            showError("");
            render();

            try {
                await requestJson("/api/users/me", {
                    method: "DELETE",
                    headers: {
                        Accept: "application/json"
                    }
                });

                window.alert("Tài khoản của bạn đã được xóa.");
                await logout();
            } catch (error) {
                showError(error.message || "Xóa tài khoản thất bại.");
            } finally {
                state.deleting = false;
                render();
            }
        }

        refs.fullName.addEventListener("input", function () {
            setFormField("fullName", refs.fullName.value);
            showError("");
        });

        refs.phone.addEventListener("input", function () {
            setFormField("phone", refs.phone.value);
            showError("");
        });

        refs.bio.addEventListener("input", function () {
            setFormField("bio", refs.bio.value);
            showError("");
        });

        refs.avatarTrigger.addEventListener("click", function () {
            if (requireLogin("Vui lòng đăng nhập để cập nhật ảnh đại diện.", accountLoginUrl())) {
                return;
            }

            if (!state.avatarUploading && !state.loading && !state.deleting) {
                refs.avatarInput.click();
            }
        });

        refs.avatarInput.addEventListener("change", function () {
            var file = refs.avatarInput.files && refs.avatarInput.files[0];
            uploadAvatar(file);
            refs.avatarInput.value = "";
        });

        refs.avatarImage.addEventListener("load", function () {
            if (state.avatarRenderFailed) {
                state.avatarRenderFailed = false;
                render();
            }
        });

        refs.avatarImage.addEventListener("error", function () {
            if (!state.avatarRenderFailed) {
                state.avatarRenderFailed = true;
                render();
            }
        });

        refs.dateTrigger.addEventListener("click", function () {
            if (requireLogin("Vui lòng đăng nhập để cập nhật ngày sinh.", accountLoginUrl())) {
                return;
            }

            if (typeof refs.dateInput.showPicker === "function") {
                refs.dateInput.showPicker();
                return;
            }

            refs.dateInput.click();
        });

        refs.dateInput.addEventListener("change", function () {
            setFormField("birthOfDate", normalizeDateOnly(refs.dateInput.value));
            render();
        });

        refs.genderTrigger.addEventListener("click", function () {
            if (requireLogin("Vui lòng đăng nhập để cập nhật giới tính.", accountLoginUrl())) {
                return;
            }

            toggleModal(refs.genderModal, true);
        });

        refs.provinceTrigger.addEventListener("click", function () {
            if (requireLogin("Vui lòng đăng nhập để cập nhật tỉnh/thành.", accountLoginUrl())) {
                return;
            }

            toggleModal(refs.provinceModal, true);
        });

        qsa("[data-account-close-modal]", root).forEach(function (button) {
            button.addEventListener("click", function () {
                toggleModal(refs.genderModal, false);
                toggleModal(refs.provinceModal, false);
            });
        });

        refs.updateButton.addEventListener("click", function () {
            if (!state.loading) {
                updateProfile();
            }
        });

        refs.deleteButton.addEventListener("click", function () {
            if (!state.deleting) {
                deleteAccount();
            }
        });

        refs.logoutButton.addEventListener("click", function () {
            if (requireLogin("Vui lòng đăng nhập để sử dụng tài khoản.", accountLoginUrl())) {
                return;
            }

            if (window.confirm("Bạn chắc chắn muốn đăng xuất?")) {
                logout();
            }
        });

        refs.changePassword.addEventListener("click", function (event) {
            if (state.isAuthenticated) {
                return;
            }

            event.preventDefault();
            window.alert("Vui lòng đăng nhập để đổi mật khẩu.");
            safeLocalRedirect(changePasswordLoginUrl());
        });

        render();
        loadProfile();
    }

    function initChangePasswordPage(root) {
        var refs = {
            current: qs("[data-change-current-password]", root),
            next: qs("[data-change-new-password]", root),
            confirm: qs("[data-change-confirm-password]", root),
            error: qs("[data-change-error]", root),
            errorText: qs("[data-change-error-text]", root),
            submit: qs("[data-change-submit]", root),
            submitText: qs("[data-change-submit-text]", root),
            ruleIcon: qs("[data-change-rule-icon]", root)
        };

        var state = {
            checking: true,
            authenticated: false,
            submitting: false
        };

        function showError(message) {
            var text = trimToEmpty(message);
            refs.error.hidden = !text;
            refs.errorText.textContent = text;
        }

        function syncButton() {
            var minLenOk = trimToEmpty(refs.next.value).length >= 8;
            var canSubmit = state.authenticated &&
                !state.checking &&
                trimToEmpty(refs.current.value).length > 0 &&
                minLenOk &&
                trimToEmpty(refs.confirm.value).length > 0 &&
                refs.next.value === refs.confirm.value;

            refs.ruleIcon.setAttribute("name", minLenOk ? "checkmark-circle" : "ellipse-outline");
            refs.ruleIcon.classList.toggle("is-valid", minLenOk);

            refs.submit.disabled = !canSubmit || state.submitting;
            refs.submit.classList.toggle("is-active", canSubmit || state.submitting);
            refs.submit.classList.toggle("is-disabled", !canSubmit && !state.submitting);
            refs.submitText.textContent = state.submitting ? "Đang đổi..." : "Thay đổi";
        }

        function redirectToLogin() {
            safeLocalRedirect("/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/ChangePassword"));
        }

        qsa("[data-change-toggle-password]", root).forEach(function (button) {
            button.addEventListener("click", function () {
                var kind = button.getAttribute("data-change-toggle-password");
                var input = kind === "current"
                    ? refs.current
                    : kind === "new"
                        ? refs.next
                        : refs.confirm;
                var icon = qs("ion-icon", button);
                var showing = input.type === "text";

                input.type = showing ? "password" : "text";
                icon.setAttribute("name", showing ? "eye-outline" : "eye-off-outline");
            });
        });

        [refs.current, refs.next, refs.confirm].forEach(function (input) {
            input.addEventListener("input", function () {
                showError("");
                syncButton();
            });
        });

        refs.submit.addEventListener("click", async function () {
            if (refs.submit.disabled || state.submitting) {
                return;
            }

            state.submitting = true;
            showError("");
            syncButton();

            try {
                var payload = await requestJson("/api/users/me/change-password", {
                    method: "POST",
                    headers: {
                        Accept: "application/json",
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({
                        currentPassword: refs.current.value,
                        newPassword: refs.next.value,
                        confirmPassword: refs.confirm.value
                    })
                });

                window.alert(responseMessage(payload, "Đổi mật khẩu thành công."));
                safeLocalRedirect("/PickleballWeb/Account");
            } catch (error) {
                showError(error.message || "Đổi mật khẩu thất bại.");
            } finally {
                state.submitting = false;
                syncButton();
            }
        });

        requestJson("/api/web-auth/me", {
            method: "GET",
            headers: {
                Accept: "application/json"
            }
        }).then(function (session) {
            state.authenticated = !!(session && session.isAuthenticated);
            state.checking = false;

            if (!state.authenticated) {
                window.alert("Vui lòng đăng nhập để đổi mật khẩu.");
                redirectToLogin();
                return;
            }

            syncButton();
        }).catch(function () {
            state.checking = false;
            window.alert("Vui lòng đăng nhập để đổi mật khẩu.");
            redirectToLogin();
        });

        syncButton();
    }

    document.addEventListener("DOMContentLoaded", function () {
        var root = qs("[data-account-page]");
        if (!root) {
            return;
        }

        var kind = trimToEmpty(root.getAttribute("data-account-page"));
        if (kind === "account") {
            initAccountPage(root);
            return;
        }

        if (kind === "change-password") {
            initChangePasswordPage(root);
        }
    });
})();
