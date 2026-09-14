namespace QueryWatt.Cli;

internal static class ScaffoldFiles
{
    public static IReadOnlyDictionary<string, string> Create(string root) =>
        new Dictionary<string, string>
        {
            [Path.Combine(root, "querywatt.yml")] = """
                schemaVersion: 1
                baselineFile: querywatt-baseline.json

                connection:
                  environmentVariable: QUERYWATT_CONNECTION_STRING

                environment:
                  containerImageTag: null
                  seedScripts:
                    - seed/setup.sql
                  tables:
                    - dbo.QueryWattExample

                measurement:
                  warmupRuns: 3
                  measuredRuns: 20
                  commandTimeoutSeconds: 60

                thresholds:
                  logicalReads:
                    percent: 25
                    absolute: 1000

                energy:
                  enabled: false
                  wattsPerBusyCore: null

                queries:
                  - name: example-by-id
                    file: queries/example-by-id.sql
                    commandType: text
                    parameters:
                      - name: Id
                        type: int32
                        value: "1"
                """ + Environment.NewLine,
            [Path.Combine(root, "queries", "example-by-id.sql")] = """
                SELECT
                    Id,
                    Value
                FROM dbo.QueryWattExample
                WHERE Id = @Id;
                """ + Environment.NewLine,
            [Path.Combine(root, "seed", "setup.sql")] = """
                IF OBJECT_ID(N'dbo.QueryWattExample', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.QueryWattExample
                    (
                        Id INT NOT NULL,
                        Value NVARCHAR(100) NOT NULL,
                        CONSTRAINT PK_QueryWattExample PRIMARY KEY CLUSTERED (Id)
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM dbo.QueryWattExample WHERE Id = 1)
                BEGIN
                    INSERT INTO dbo.QueryWattExample (Id, Value)
                    VALUES (1, N'synthetic-example');
                END;
                """ + Environment.NewLine
        };
}
