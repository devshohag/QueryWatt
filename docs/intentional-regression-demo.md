# Intentional regression pull request

This is the project's proof: a public pull request where QueryWatt turns a check
red on a change that is functionally correct and measurably more expensive. Open
it only after the action fixes are merged to the default branch, so the run is
real and repeatable.

```powershell
Set-Location "D:\QueryWatt"
git switch main
git pull --ff-only origin main
git switch -c demo/querywatt-detects-non-sargable-seek

Copy-Item `
    -LiteralPath ".\samples\regressions\customer-seek-non-sargable.sql" `
    -Destination ".\samples\TicketingDatabase\queries\seek-parameterized.sql" `
    -Force

git add .\samples\TicketingDatabase\queries\seek-parameterized.sql
git diff --cached --check
git commit -m "demo: introduce non-sargable customer lookup"
git push -u origin demo/querywatt-detects-non-sargable-seek
```

Open the pull request. Do not merge it.

## What the run should show

The base query uses the `IX_Ticket_CustomerId` seek. The proposed expression
`CustomerId + 0` makes the predicate non-sargable while returning exactly the
same rows, so correctness tests cannot catch it. On the synthetic 50,000-row
data set it should exceed both the 25 percent and the 50-page thresholds and
return exit code 1. The exact percentage is deliberately not claimed in advance.

In the report, three things should line up:

- `logicalReads` regressed, with the baseline and current medians side by side;
- **query text changed: yes** — expected, this pull request rewrote the query;
- **estimated plan shape changed: yes** — the seek became a scan. This is the
  signal that carries information, which is why it is reported separately from
  the text change.

## Recording it

Record only after the real workflow finishes. Capture the red check, expand the
QueryWatt report, show the measured logical-read row and the exit code, then
close the demo pull request. The recording goes in the README.

Do not record a fabricated report. The whole value of this artefact is that it
is a real run on a public repository that anyone can re-execute.
