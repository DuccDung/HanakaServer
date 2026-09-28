"use strict";
const test = require("node:test");
const { assert, fs, path, artifacts, manifest, delay } = require("./helpers/public-schedule-live");
const minutes = Math.max(1, Math.min(30, Number(process.env.HANAKA_PUBLIC_POLL_MINUTES) || 10));

test("200 anonymous viewers poll a 2000-match public schedule every 30 seconds without stale state or failed requests", {
    skip: process.env.HANAKA_PUBLIC_POLL_LOAD !== "1", timeout: (minutes + 3) * 60000
}, async () => {
    const host = manifest(), fixture = host.tournaments[2], started = Date.now(), samples = [], latencies = [], failures = [];
    const versions = Array.from({ length: 200 }, () => new Map());
    const watched = new Set(fixture.matches.slice(0, 20));
    const percentile = (values, p) => { const sorted = [...values].sort((a, b) => a - b); return sorted[Math.max(0, Math.ceil(sorted.length * p) - 1)]; };
    const file = path.join(artifacts, "public-poll-load-" + new Date().toISOString().replace(/[:.]/g, "-"));
    let cycle = 0;
    try {
        while (Date.now() - started < minutes * 60000) {
            const cycleStarted = Date.now(), measurements = [];
            let next = 0;
            // Bounded waves represent staggered viewers and avoid an artificial single-tick connection storm.
            const outcomes = await Promise.allSettled(Array.from({ length: 10 }, async () => {
                while (next < 200) {
                    const viewer = next++;
                    const permission = await fetch(`${host.url}/api/coordination/tournaments/${fixture.id}/permissions`, { signal: AbortSignal.timeout(20000) });
                    await permission.arrayBuffer(); assert.equal(permission.status, 401, "Anonymous viewer obtained operator access");
                    const before = performance.now();
                    const response = await fetch(`${host.url}/api/tournaments/${fixture.id}/rounds-with-matches`, { signal: AbortSignal.timeout(20000) });
                    assert.ok(response.ok, `Schedule returned ${response.status}`);
                    const rows = (await response.json()).rounds.flatMap(r => r.groups.flatMap(g => g.matches));
                    measurements.push(performance.now() - before);
                    assert.equal(rows.length, 2000);
                    for (const row of rows.filter(row => watched.has(row.matchId))) {
                        assert.ok(row.stateVersion >= (versions[viewer].get(row.matchId) || 0), "REST version moved backwards");
                        assert.ok(["NOT_STARTED", "PREPARING", "IN_PROGRESS", "COMPLETED"].includes(row.matchStatus));
                        assert.equal(row.isCompleted, row.matchStatus === "COMPLETED");
                        versions[viewer].set(row.matchId, row.stateVersion);
                    }
                }
            }));
            const failed = outcomes.filter(outcome => outcome.status === "rejected");
            if (failed.length) throw new AggregateError(failed.map(outcome => outcome.reason), "Public polling workers failed");
            cycle++; latencies.push(...measurements);
            const sample = { cycle, elapsedSeconds: (Date.now() - started) / 1000, viewers: 200, matches: 2000,
                requests: measurements.length, waveMs: Date.now() - cycleStarted, p95Ms: percentile(measurements, .95), p99Ms: percentile(measurements, .99) };
            samples.push(sample); fs.appendFileSync(file + ".jsonl", JSON.stringify(sample) + "\n");
            console.log(`Public polling load: cycle ${cycle}, 200 viewers, p95 ${Math.round(sample.p95Ms)}ms, wave ${sample.waveMs}ms`);
            assert.ok(sample.waveMs < 30000, "Viewer polling did not finish before the next 30-second interval");
            const nextCycle = Math.min(started + minutes * 60000, cycleStarted + 30000);
            while (Date.now() < nextCycle) await delay(Math.min(5000, nextCycle - Date.now()));
        }
        assert.ok(percentile(latencies, .95) < 2000, "Public polling read p95 exceeded two seconds");
    } catch (error) { failures.push(error.stack); throw error; }
    finally {
        fs.writeFileSync(file + ".json", JSON.stringify({ requestedMinutes: minutes, elapsedSeconds: (Date.now() - started) / 1000,
            passed: failures.length === 0 && Date.now() - started >= minutes * 60000, samples, failures,
            requests: latencies.length, permissionRequests: latencies.length, p95Ms: percentile(latencies, .95), p99Ms: percentile(latencies, .99) }, null, 2));
    }
});
