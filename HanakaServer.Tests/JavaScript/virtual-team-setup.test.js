const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const repositoryRoot = path.resolve(__dirname, "..", "..");
const script = fs.readFileSync(
    path.join(repositoryRoot, "HanakaServer", "wwwroot", "js", "admin-tournament-bracket-setup.js"),
    "utf8"
);
const view = fs.readFileSync(
    path.join(repositoryRoot, "HanakaServer", "Views", "TournamentBracketSetup", "Index.cshtml"),
    "utf8"
);

test("virtual-team confirmation modal exposes create, keep-BYE and cancel choices", () => {
    assert.match(view, /id="virtualTeamConfirmModal"/);
    assert.match(view, /id="createVirtualBracketTeams"/);
    assert.match(view, /id="keepBracketBye"/);
    assert.match(view, />Hủy</);
});

test("virtual-team preview and apply both send the server-owned fill flag", () => {
    assert.match(script, /fillMissingWithVirtualTeams:\s*true/);
    assert.match(script, /createPlacementPreview\(state\.pendingVirtualTeamMethod,\s*true\)/);
    assert.match(script, /fillMissingWithVirtualTeams:\s*\(state\.preview\.virtualTeamCount \|\| 0\) > 0/);
    assert.match(script, /virtualTeamChoiceMade/);
});

test("double-BYE placement failure is recovered through the virtual-team modal", () => {
    assert.match(script, /error\?\.code === "DOUBLE_BYE_MATCH" && hasMissingTeams/);
    assert.match(script, /state\.pendingVirtualTeamMethod = state\.pendingPlacementMethod/);
    assert.match(script, /confirmationModal\.one\("hidden\.bs\.modal", openVirtualTeamConfirmModal\)/);
    assert.match(script, /keepByeButton\.disabled = Boolean\(state\.pendingVirtualTeamMethod\)/);
});

test("admin preview explains that public clients receive a neutral placeholder", () => {
    assert.match(view, /Chờ cập nhật/);
    assert.match(script, /Không liên kết user/);
});
