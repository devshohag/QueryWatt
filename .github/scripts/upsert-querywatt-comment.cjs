const fs = require("fs");

const marker = "<!-- querywatt-report -->";
const maximumBodyLength = 60_000;

module.exports = async function upsertQueryWattComment({
    github,
    context,
    reportPath,
}) {
    if (!context.payload.pull_request) {
        return;
    }

    let report = fs.readFileSync(reportPath, "utf8").trim();
    if (report.length > maximumBodyLength) {
        report = `${report.slice(0, maximumBodyLength)}\n\n_Report truncated; download the workflow artifact for the full output._`;
    }

    const body = `${marker}\n${report}`;
    const issue = {
        owner: context.repo.owner,
        repo: context.repo.repo,
        issue_number: context.issue.number,
    };
    const comments = await github.paginate(
        github.rest.issues.listComments,
        { ...issue, per_page: 100 },
    );
    const existing = comments.find(comment =>
        comment.user?.login === "github-actions[bot]"
        && comment.body?.includes(marker));

    if (existing) {
        await github.rest.issues.updateComment({
            owner: issue.owner,
            repo: issue.repo,
            comment_id: existing.id,
            body,
        });
        return;
    }

    await github.rest.issues.createComment({
        ...issue,
        body,
    });
};
