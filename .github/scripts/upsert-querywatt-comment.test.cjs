const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const upsertQueryWattComment = require("./upsert-querywatt-comment.cjs");

function createFixture() {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "querywatt-comment-"));
    const reportPath = path.join(root, "report.md");
    fs.writeFileSync(reportPath, "## QueryWatt verification: PASSED\n", "utf8");
    return { root, reportPath };
}

function createContext() {
    return {
        payload: { pull_request: { number: 42 } },
        repo: { owner: "querywatt", repo: "querywatt" },
        issue: { number: 42 },
    };
}

test("creates one marker-owned comment when none exists", async () => {
    const fixture = createFixture();
    let created;
    const github = {
        paginate: async () => [],
        rest: {
            issues: {
                listComments: () => {},
                createComment: async parameters => { created = parameters; },
                updateComment: async () => assert.fail("update should not run"),
            },
        },
    };

    try {
        await upsertQueryWattComment({
            github,
            context: createContext(),
            reportPath: fixture.reportPath,
        });

        assert.equal(created.issue_number, 42);
        assert.match(created.body, /^<!-- querywatt-report -->/);
        assert.match(created.body, /verification: PASSED/);
    } finally {
        fs.rmSync(fixture.root, { recursive: true, force: true });
    }
});

test("updates the existing QueryWatt bot comment", async () => {
    const fixture = createFixture();
    let updated;
    const github = {
        paginate: async () => [{
            id: 99,
            user: { login: "github-actions[bot]" },
            body: "<!-- querywatt-report -->\nold",
        }],
        rest: {
            issues: {
                listComments: () => {},
                createComment: async () => assert.fail("create should not run"),
                updateComment: async parameters => { updated = parameters; },
            },
        },
    };

    try {
        await upsertQueryWattComment({
            github,
            context: createContext(),
            reportPath: fixture.reportPath,
        });

        assert.equal(updated.comment_id, 99);
        assert.match(updated.body, /verification: PASSED/);
    } finally {
        fs.rmSync(fixture.root, { recursive: true, force: true });
    }
});
