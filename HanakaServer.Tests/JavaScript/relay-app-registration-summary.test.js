"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { createRequire } = require("node:module");
const test = require("node:test");
const vm = require("node:vm");

function registrations(size = 6, full = false, paid = false) {
    return {
        tournament: { tournamentId: 37, title: "Relay", gameType: "DOUBLE", tournamentTypeCode: full ? "RELAY_TEAM" : "SINGLE", isRelay: true, teamSize: size },
        counts: { success: 1, waiting: 0, paid: paid ? 1 : 0, capacityLeft: 15 },
        successItems: [{ registrationId: 501, regIndex: 1, regCode: "37-0001", teamName: "Hanaka A", teamSize: size,
            isRelay: true, isReady: true, success: true, waitingPair: false, paid, points: full ? 0 : size * 3,
            player1: { name: full ? "Current 1" : "Hanaka A", level: full ? 3 : size * 3, verified: !full, userId: full ? 1 : null, avatar: null, isGuest: false },
            player2: full ? { name: "Current 2", level: 3, userId: 2 } : null,
            members: Array.from({ length: size }, (_, i) => ({ position: i + 1, pairNumber: Math.floor(i / 2) + 1, userId: i + 1, name: `Current ${i + 1}`, level: 3 })),
            reserveMembers: [{ position: 1, userId: size + 1, name: "Reserve", level: 99 }] }],
        waitingItems: []
    };
}

test("web list, detail and App WebView explicitly request full registration data", async () => {
    const requests = [];
    const window = {
        location: new URL("https://hanaka.test/PickleballWeb/App/Tournament/37/Registration/501/Payment"),
        HanakaWebSession: { read: async () => ({ isAuthenticated: false }), isAborted: () => false }
    };
    const context = vm.createContext({ window, URL, URLSearchParams, Response,
        document: { addEventListener() {} },
        fetch: async (url, options) => {
            requests.push({ url, options });
            const parsed = new URL(url, window.location.origin);
            let body = {};
            if (parsed.pathname.endsWith("/registrations")) {
                assert.equal(parsed.searchParams.get("view"), "full");
                body = registrations(6, true);
            } else if (parsed.pathname.endsWith("/app-webview-checkout")) {
                body = { tournamentId: 37, registrationId: 501, teamName: "Hanaka A", transactionCode: "TEST501" };
            } else if (parsed.pathname === "/api/public/tournaments/37") {
                body = { tournamentId: 37, title: "Relay" };
            }
            return new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } });
        }
    });
    const source = fs.readFileSync(path.resolve(__dirname, "../../HanakaServer/wwwroot/pickleball-web/js/pages.js"), "utf8");
    const end = source.lastIndexOf("})();");
    vm.runInContext(source.slice(0, end) + "window.__pages = { loadTournamentRegistrationsPage, loadTournamentAppPaymentPage, detailConfigs, renderTournamentRegistrationsPage };" + source.slice(end), context);
    const api = window.__pages;
    const list = await api.loadTournamentRegistrationsPage(37);
    const html = api.renderTournamentRegistrationsPage(list);
    assert.match(html, /Hanaka A/);
    assert.match(html, /Current 6/);
    assert.match(html, /Reserve/);
    assert.equal((html.match(/<section class="tournament-registration-page__relay-pair">/g) || []).length, 3);
    await api.detailConfigs["tournament-detail"].load(37);
    const checkout = await api.loadTournamentAppPaymentPage(37);
    assert.equal(checkout.registration.registrationId, 501);
    assert.equal(checkout.registration.teamName, "Hanaka A");
    assert.equal(checkout.registration.members.length, 6);
    assert.equal(checkout.payment.registrationId, 501);
    // The active native detail page only loads tournament metadata; list and WebView load registrations.
    assert.equal(requests.filter(request => new URL(request.url, window.location.origin).pathname.endsWith("/registrations")).length, 2);
});

// Execute the existing mobile screen without editing its repository. Native components and hooks are
// replaced only at the test boundary; its mapping, layout choice, labels, search and actions are real code.
const mobileRoot = process.env.HANAKA_MOBILE_ROOT || path.resolve(__dirname, "../../../hanaka-sport");
const mobileScreen = path.join(mobileRoot, "src/screens/Tournament/RegistrationListScreen.js");
const hasMobile = fs.existsSync(mobileScreen) && fs.existsSync(path.join(mobileRoot, "node_modules/@babel/core"));

test("unchanged mobile screen shows one verified team, searches its name and opens its registration payment", { skip: !hasMobile }, async () => {
    const mobileRequire = createRequire(path.join(mobileRoot, "package.json"));
    const compiled = mobileRequire("@babel/core").transformSync(fs.readFileSync(mobileScreen, "utf8"), {
        filename: mobileScreen, babelrc: false, configFile: false,
        plugins: [mobileRequire.resolve("@babel/plugin-transform-react-jsx"), mobileRequire.resolve("@babel/plugin-transform-modules-commonjs")]
    }).code;

    for (const size of [4, 6, 8]) {
        for (const paid of [false, true]) {
            const state = [], effects = [], navigations = [], alerts = [];
            let cursor = 0, mounted = false;
            const React = {
                createElement: (type, props, ...children) => ({ type, props: { ...props, children: children.flat() } }),
                useState: initial => {
                    const index = cursor++;
                    if (!(index in state)) state[index] = initial;
                    return [state[index], value => { state[index] = typeof value === "function" ? value(state[index]) : value; }];
                },
                useRef: initial => React.useState({ current: initial })[0],
                useEffect: effect => { if (!mounted) effects.push(effect); },
                useMemo: factory => factory(),
                useCallback: callback => callback
            };
            const response = registrations(size, false, paid);
            const modules = {
                react: React,
                "react-native": { ...Object.fromEntries(["View", "Text", "StatusBar", "Pressable", "TextInput", "FlatList", "Image", "ActivityIndicator", "RefreshControl"].map(name => [name, name])),
                    Keyboard: { dismiss() {} }, Linking: { openURL() {} }, Alert: { alert: (...args) => alerts.push(args) } },
                "react-native-safe-area-context": { SafeAreaView: "SafeAreaView" },
                "@expo/vector-icons": { Ionicons: "Icon" },
                "./registrationListStyles": { styles: {} },
                "../../constants/config": { API_BASE_URL: "https://hanaka.test" },
                "../../services/tournamentService": {
                    publicListTournamentRegistrations: async () => response,
                    publicGetTournamentDetail: async () => ({ tournamentId: 37, gameType: "DOUBLE" }),
                    getMyTournamentRegistrationState: async () => ({ me: { userId: size + 1 }, existingRegistration: { registrationId: 501 } })
                }
            };
            const sandbox = { exports: {}, console, setTimeout, clearTimeout, require: name => {
                assert.ok(name in modules, "Unexpected mobile dependency: " + name);
                return modules[name];
            } };
            vm.runInNewContext(compiled, sandbox, { filename: mobileScreen });
            const Component = sandbox.exports.default;
            const props = { route: { params: { tournamentId: 37 } }, navigation: {
                addListener: () => () => {}, navigate: (...args) => navigations.push(args), goBack() {}
            } };
            const render = () => { cursor = 0; return Component(props); };
            const nodes = node => !node || typeof node !== "object" ? [] : [node, ...(node.props?.children || []).flatMap(nodes)];
            const text = node => typeof node === "string" || typeof node === "number" ? String(node) : (node?.props?.children || []).map(text).join("");
            render();
            mounted = true;
            effects.forEach(effect => effect());
            await new Promise(resolve => setImmediate(resolve));
            let screen = render();
            const list = nodes(screen).find(node => node.type === "FlatList");
            assert.equal(list.props.data.length, 1);
            const card = list.props.renderItem({ item: list.props.data[0] });
            assert.ok(text(card).includes("Hanaka A"));
            assert.ok(text(card).includes("Pick Single: " + size * 3));
            assert.ok(text(card).includes("Đã xác thực"));
            assert.ok(!text(card).includes("Current 1") && !text(card).includes("Chờ ghép"));
            assert.ok(!text(screen).includes("VĐV1") && !text(screen).includes("VĐV2"));

            const search = nodes(screen).find(node => node.type === "TextInput");
            search.props.onChangeText("hanaka");
            assert.equal(nodes(render()).find(node => node.type === "FlatList").props.data.length, 1);
            search.props.onChangeText("unknown team");
            assert.equal(nodes(render()).find(node => node.type === "FlatList").props.data.length, 0);
            search.props.onChangeText("");
            screen = render();
            await nodes(screen).find(node => node.type === "Pressable" && text(node).trim() === "Đăng ký").props.onPress();
            assert.equal(alerts.at(-1)[1], "Bạn đã đăng kí"); // Also works for the reserve member returned by /me.

            const pay = nodes(card).find(node => node.type === "Pressable" && ["Mở", "Mo"].includes(node.props.accessibilityLabel));
            if (paid) assert.equal(pay, undefined);
            else {
                await pay.props.onPress();
                assert.equal(navigations.at(-1)[0], "AppWebView");
                assert.equal(navigations.at(-1)[1].url, "https://hanaka.test/PickleballWeb/App/Tournament/37/Registration/501/Payment");
            }
        }
    }
});
