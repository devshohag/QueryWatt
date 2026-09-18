using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryWatt.Reporting;

/// <summary>
/// Renders the report as one self-contained HTML file: no network, no assets, no build step.
/// </summary>
/// <remarks>
/// This is the answer for a team without pull requests. A page that only exists behind a CI run is
/// a page nobody reads, so this one is a file — it opens from disk, survives being emailed or
/// dropped in a chat, and works the same on a machine that has never heard of QueryWatt.
/// </remarks>
public static class HtmlReportRenderer
{
    private static readonly JsonSerializerOptions DataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The JSON lives inside a <script> element, so anything that could close it early is escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Renders the page.</summary>
    /// <param name="model">The model to render.</param>
    /// <returns>A complete HTML document.</returns>
    public static string Render(HtmlReportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var json = JsonSerializer
            .Serialize(model, DataOptions)
            .Replace("<", "\\u003c", StringComparison.Ordinal)
            .Replace(">", "\\u003e", StringComparison.Ordinal)
            .Replace("&", "\\u0026", StringComparison.Ordinal);

        return Template.Replace("__QUERYWATT_DATA__", json, StringComparison.Ordinal);
    }

    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>QueryWatt report</title>
        <style>
          :root {
            color-scheme: light;
            --page:        #f9f9f7;
            --surface:     #fcfcfb;
            --ink:         #0b0b0b;
            --ink-2:       #52514e;
            --muted:       #898781;
            --rule:        #e1e0d9;
            --ring:        rgba(11,11,11,0.10);
            --bar:         #2a78d6;
            --bar-soft:    #cde2fb;
            --good:        #0ca30c;
            --warning:     #fab219;
            --serious:     #ec835a;
            --critical:    #d03b3b;
          }
          @media (prefers-color-scheme: dark) {
            :root:not([data-theme="light"]) {
              color-scheme: dark;
              --page:     #0d0d0d;
              --surface:  #1a1a19;
              --ink:      #ffffff;
              --ink-2:    #c3c2b7;
              --muted:    #898781;
              --rule:     #2c2c2a;
              --ring:     rgba(255,255,255,0.10);
              --bar:      #3987e5;
              --bar-soft: #184f95;
            }
          }
          :root[data-theme="dark"] {
            color-scheme: dark;
            --page:     #0d0d0d;
            --surface:  #1a1a19;
            --ink:      #ffffff;
            --ink-2:    #c3c2b7;
            --muted:    #898781;
            --rule:     #2c2c2a;
            --ring:     rgba(255,255,255,0.10);
            --bar:      #3987e5;
            --bar-soft: #184f95;
          }
          * { box-sizing: border-box; }
          body {
            margin: 0;
            background: var(--page);
            color: var(--ink);
            font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
          }
          .wrap { max-width: 1120px; margin: 0 auto; padding: 32px 16px 64px; }
          header { display: flex; align-items: flex-start; gap: 16px; flex-wrap: wrap; }
          .brand { font-weight: 650; letter-spacing: -0.01em; font-size: 15px; }
          .brand span { color: var(--muted); font-weight: 400; }
          h1 { font-size: 27px; line-height: 1.25; margin: 4px 0 0; letter-spacing: -0.02em; }
          .meta { color: var(--muted); font-size: 13px; margin-top: 6px; }
          .spacer { flex: 1 1 auto; }
          button {
            font: inherit; color: var(--ink-2); background: var(--surface);
            border: 1px solid var(--ring); border-radius: 8px; padding: 6px 12px; cursor: pointer;
          }
          button:hover { color: var(--ink); }
          .tiles {
            display: grid; gap: 12px; margin: 24px 0 8px;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
          }
          .tile {
            background: var(--surface); border: 1px solid var(--ring);
            border-radius: 12px; padding: 14px 16px;
          }
          .tile .label { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
          .tile .value { font-size: 25px; margin-top: 4px; letter-spacing: -0.02em; }
          section { margin-top: 32px; }
          h2 { font-size: 17px; margin: 0 0 12px; letter-spacing: -0.01em; }
          .filters { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 12px; }
          .chip {
            font-size: 13px; padding: 4px 10px; border-radius: 999px;
            border: 1px solid var(--ring); background: var(--surface); color: var(--ink-2); cursor: pointer;
          }
          .chip[aria-pressed="true"] { color: var(--ink); border-color: var(--ink-2); }
          table { width: 100%; border-collapse: collapse; background: var(--surface);
                  border: 1px solid var(--ring); border-radius: 12px; overflow: hidden; }
          th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--rule); vertical-align: top; }
          th { font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em; font-weight: 500; }
          tbody tr:last-child td { border-bottom: 0; }
          td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
          .qid { font-weight: 550; }
          .scenario { color: var(--muted); font-size: 12px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
          .verdict { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; white-space: nowrap; }
          .verdict .mark { font-weight: 700; }
          .v-good .mark     { color: var(--good); }
          .v-warning .mark  { color: var(--warning); }
          .v-critical .mark { color: var(--critical); }
          .v-info .mark     { color: var(--bar); }
          .v-muted .mark    { color: var(--muted); }
          .bar { height: 6px; border-radius: 3px; background: var(--bar-soft); margin-top: 6px; }
          .bar > i { display: block; height: 100%; border-radius: 3px; background: var(--bar); }
          .detail { color: var(--ink-2); font-size: 13px; }
          .detail code, .cmd {
            font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px;
            background: var(--page); border: 1px solid var(--rule); border-radius: 6px;
            padding: 2px 6px; display: inline-block;
          }
          .cmd { display: block; padding: 8px 10px; margin-top: 6px; white-space: pre-wrap; word-break: break-word; }
          .note { color: var(--ink-2); font-size: 13px; background: var(--surface);
                  border: 1px solid var(--ring); border-left: 3px solid var(--warning);
                  border-radius: 8px; padding: 10px 12px; margin-top: 8px; }
          .empty { color: var(--muted); }
          footer { margin-top: 40px; color: var(--muted); font-size: 12px; }
          @media (max-width: 640px) {
            .hide-narrow { display: none; }
            h1 { font-size: 23px; }
          }
        </style>
        </head>
        <body>
        <div class="wrap">
          <header>
            <div>
              <div class="brand">QueryWatt <span>report</span></div>
              <h1 id="headline"></h1>
              <div class="meta" id="meta"></div>
            </div>
            <div class="spacer"></div>
            <button id="theme" type="button">Theme</button>
          </header>

          <div class="tiles" id="tiles"></div>

          <section>
            <h2>Queries</h2>
            <div class="filters" id="filters"></div>
            <table>
              <thead>
                <tr>
                  <th>Query</th>
                  <th id="verdictHead">Verdict</th>
                  <th class="num">Reads</th>
                  <th class="num hide-narrow">Rows</th>
                  <th class="num">Reads/row</th>
                  <th class="num hide-narrow">ms</th>
                  <th class="hide-narrow">Source</th>
                </tr>
              </thead>
              <tbody id="rows"></tbody>
            </table>
          </section>

          <section id="repetitionSection">
            <h2>Repeated commands</h2>
            <p class="detail">One statement, many executions inside a single scope. Each execution is
              cheap on its own; the count is the finding.</p>
            <table>
              <thead>
                <tr>
                  <th>Scope</th>
                  <th class="num">Executions</th>
                  <th class="num">Reads</th>
                  <th class="num hide-narrow">ms</th>
                </tr>
              </thead>
              <tbody id="repetitions"></tbody>
            </table>
          </section>

          <section id="noteSection">
            <h2>Notes</h2>
            <div id="notes"></div>
          </section>

          <footer id="footer"></footer>
        </div>

        <script type="application/json" id="qw-data">__QUERYWATT_DATA__</script>
        <script>
        (function () {
          var data = JSON.parse(document.getElementById('qw-data').textContent);
          var filter = 'all';

          var VERDICTS = {
            Unchanged:      { cls: 'v-good',     mark: '✓' },
            Improved:       { cls: 'v-good',     mark: '✓' },
            Regression:     { cls: 'v-critical', mark: '✕' },
            PlanRegression: { cls: 'v-critical', mark: '✕' },
            Suspect:        { cls: 'v-warning',  mark: '!' },
            DataChange:     { cls: 'v-muted',    mark: '~' },
            NewScenario:    { cls: 'v-info',     mark: '+' },
            Incomparable:   { cls: 'v-muted',    mark: '?' }
          };

          function num(value) {
            return value === null || value === undefined
              ? '—'
              : value.toLocaleString('en-US');
          }

          function ratio(value) {
            return value === null || value === undefined ? '—' : String(value);
          }

          function text(node, value) { node.textContent = value; return node; }

          function el(tag, className) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            return node;
          }

          document.getElementById('headline').textContent = data.headline;
          document.getElementById('meta').textContent =
            new Date(data.generatedUtc).toLocaleString() + ' · QueryWatt ' + data.toolVersion;
          document.getElementById('footer').textContent =
            'Generated by QueryWatt ' + data.toolVersion + '. This file is self-contained: it makes no '
            + 'network requests and reads nothing from the machine it is opened on.';

          var tiles = [
            ['Scopes', num(data.scopeCount)],
            ['Commands', num(data.commandCount)],
            ['Logical reads', num(data.totalLogicalReads)],
            ['Queries', num(data.queryCount)]
          ];
          if (data.hasBaseline) { tiles.push(['Regressions', num(data.failureCount)]); }

          var tileHost = document.getElementById('tiles');
          tiles.forEach(function (tile) {
            var box = el('div', 'tile');
            box.appendChild(text(el('div', 'label'), tile[0]));
            box.appendChild(text(el('div', 'value'), tile[1]));
            tileHost.appendChild(box);
          });

          var maxReads = data.rows.reduce(function (most, row) {
            return Math.max(most, row.logicalReads || 0);
          }, 0);

          function renderRows() {
            var host = document.getElementById('rows');
            host.textContent = '';

            var rows = data.rows.filter(function (row) {
              return filter === 'all' || row.verdict === filter;
            });

            if (rows.length === 0) {
              var empty = el('tr');
              var cell = el('td');
              cell.colSpan = 7;
              cell.className = 'empty';
              cell.textContent = 'Nothing matches this filter.';
              empty.appendChild(cell);
              host.appendChild(empty);
              return;
            }

            rows.forEach(function (row) {
              var tr = el('tr');

              var name = el('td');
              name.appendChild(text(el('div', 'qid'), row.queryId));
              name.appendChild(text(el('div', 'scenario'), row.scenarioKey));
              if (maxReads > 0 && row.logicalReads) {
                var bar = el('div', 'bar');
                var fill = el('i');
                fill.style.width = Math.max(2, Math.round((row.logicalReads / maxReads) * 100)) + '%';
                bar.appendChild(fill);
                name.appendChild(bar);
              }
              tr.appendChild(name);

              var verdictCell = el('td');
              if (row.verdict) {
                var style = VERDICTS[row.verdict] || VERDICTS.Incomparable;
                var chip = el('span', 'verdict ' + style.cls);
                chip.appendChild(text(el('span', 'mark'), style.mark));
                chip.appendChild(text(el('span'), row.verdict));
                verdictCell.appendChild(chip);
              } else {
                verdictCell.className = 'empty';
                verdictCell.textContent = '—';
              }
              tr.appendChild(verdictCell);

              var reads = el('td', 'num');
              reads.textContent = row.baselineLogicalReads !== undefined && row.baselineLogicalReads !== null
                ? num(row.baselineLogicalReads) + ' → ' + num(row.logicalReads)
                : num(row.logicalReads);
              tr.appendChild(reads);

              tr.appendChild(text(el('td', 'num hide-narrow'), num(row.rowsReturned)));

              var perRow = el('td', 'num');
              perRow.textContent = row.baselineReadsPerRow !== undefined && row.baselineReadsPerRow !== null
                ? ratio(row.baselineReadsPerRow) + ' → ' + ratio(row.readsPerRow)
                : ratio(row.readsPerRow);
              tr.appendChild(perRow);

              tr.appendChild(text(el('td', 'num hide-narrow'), String(row.durationMilliseconds)));
              tr.appendChild(text(el('td', 'hide-narrow'), row.source));

              host.appendChild(tr);

              if (row.reason || (row.diagnostics && row.diagnostics.length)) {
                var detailRow = el('tr');
                var detail = el('td', 'detail');
                detail.colSpan = 7;
                if (row.reason) { detail.appendChild(text(el('div'), row.reason)); }
                if (row.diagnostics && row.diagnostics.length) {
                  detail.appendChild(text(el('div'), 'Diagnostics: ' + row.diagnostics.join(', ')));
                }
                if (row.verdict === 'NewScenario' || row.verdict === 'Regression'
                    || row.verdict === 'PlanRegression') {
                  detail.appendChild(text(el('code'), 'QUERYWATT_ACCEPT=1 dotnet test'));
                }
                detailRow.appendChild(detail);
                host.appendChild(detailRow);
              }
            });
          }

          if (data.hasBaseline) {
            var present = ['all'].concat(data.rows.map(function (row) { return row.verdict; })
              .filter(function (verdict, index, all) {
                return verdict && all.indexOf(verdict) === index;
              }));

            var filterHost = document.getElementById('filters');
            present.forEach(function (value) {
              var chip = el('button', 'chip');
              chip.type = 'button';
              chip.textContent = value === 'all' ? 'All' : value;
              chip.setAttribute('aria-pressed', value === 'all' ? 'true' : 'false');
              chip.addEventListener('click', function () {
                filter = value;
                Array.prototype.forEach.call(filterHost.children, function (other) {
                  other.setAttribute('aria-pressed', other === chip ? 'true' : 'false');
                });
                renderRows();
              });
              filterHost.appendChild(chip);
            });
          } else {
            document.getElementById('verdictHead').textContent = 'Verdict';
          }

          renderRows();

          var repetitionHost = document.getElementById('repetitions');
          if (data.repetitions.length === 0) {
            document.getElementById('repetitionSection').style.display = 'none';
          } else {
            data.repetitions.forEach(function (repetition) {
              var tr = el('tr');
              var scope = el('td');
              scope.appendChild(text(el('div', 'qid'), repetition.queryId));
              scope.appendChild(text(el('div', 'cmd'), repetition.commandText));
              tr.appendChild(scope);
              tr.appendChild(text(el('td', 'num'), '×' + repetition.repetitions));
              tr.appendChild(text(el('td', 'num'), num(repetition.totalLogicalReads)));
              tr.appendChild(text(el('td', 'num hide-narrow'),
                String(Math.round(repetition.totalDurationMilliseconds))));
              repetitionHost.appendChild(tr);
            });
          }

          var noteHost = document.getElementById('notes');
          if (data.notes.length === 0) {
            document.getElementById('noteSection').style.display = 'none';
          } else {
            data.notes.forEach(function (note) {
              noteHost.appendChild(text(el('div', 'note'), note));
            });
          }

          document.getElementById('theme').addEventListener('click', function () {
            var root = document.documentElement;
            var dark = getComputedStyle(root).colorScheme === 'dark';
            root.setAttribute('data-theme', dark ? 'light' : 'dark');
          });
        })();
        </script>
        </body>
        </html>
        """;
}
