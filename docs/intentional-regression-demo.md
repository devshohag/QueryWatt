# Intentional regression pull request

Run this only after the Week 5 infrastructure is merged to the default branch.
It creates a synthetic query regression suitable for the README GIF.

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

Open a pull request without merging it. The base query uses the
`IX_Ticket_CustomerId` seek. The proposed expression `CustomerId + 0` makes the
predicate non-sargable while returning the same rows. On the synthetic 50,000
row data set, it should exceed both the 25 percent and 50-page thresholds, make
the final check red with exit code 1, and produce a PR comment containing the
measured before/after values. The exact percentage is deliberately not claimed
in advance.

Record the GIF only after the real workflow finishes. Capture the red check,
expand the QueryWatt report, show the measured logical-read row and exit code,
then close the demo PR. Do not record a fabricated report.
