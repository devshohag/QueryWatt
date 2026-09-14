using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using QueryWatt.Core;

namespace QueryWatt.SqlServer;

public static class ShowPlanParser
{
    private static readonly HashSet<string> VolatileAttributeNames = new(
        StringComparer.Ordinal)
    {
        "CachedPlanSize",
        "CompileCPU",
        "CompileMemory",
        "CompileTime",
        "QueryHash",
        "QueryPlanHash",
        "RetrievedFromCache",
        "StatementCompId",
        "StatementId"
    };

    public static IReadOnlyList<StatementPlanFingerprint> Parse(
        IReadOnlyList<string> xmlDocuments)
    {
        ArgumentNullException.ThrowIfNull(xmlDocuments);

        var fingerprints = new List<StatementPlanFingerprint>();
        foreach (var xml in xmlDocuments)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (Exception exception) when (exception is System.Xml.XmlException)
            {
                throw new MeasurementParseException(
                    $"SQL Server returned invalid SHOWPLAN XML: {exception.Message}");
            }

            foreach (var statement in document.Descendants()
                         .Where(element => element.Name.LocalName == "StmtSimple"))
            {
                var queryHash = ReadHash(statement, "QueryHash")
                    ?? CreateQueryFallback(statement);
                var queryPlanHash = ReadHash(statement, "QueryPlanHash")
                    ?? CreatePlanFallback(statement);

                fingerprints.Add(new StatementPlanFingerprint(
                    fingerprints.Count + 1,
                    queryHash,
                    queryPlanHash));
            }
        }

        return fingerprints;
    }

    private static string? ReadHash(XElement statement, string name)
    {
        var value = statement.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string CreateQueryFallback(XElement statement)
    {
        var statementText = statement.Attribute("StatementText")?.Value;
        var normalizedText = Regex.Replace(
            statementText ?? statement.Name.LocalName,
            @"\s+",
            " ").Trim();

        return "SHA256:" + CalculateSha256(normalizedText);
    }

    private static string CreatePlanFallback(XElement statement)
    {
        var plan = statement.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "QueryPlan")
            ?? statement;
        var canonicalPlan = new XElement(plan);

        foreach (var attribute in canonicalPlan
                     .DescendantsAndSelf()
                     .Attributes()
                     .Where(attribute => VolatileAttributeNames.Contains(
                         attribute.Name.LocalName))
                     .ToArray())
        {
            attribute.Remove();
        }

        return "SHA256:" + CalculateSha256(
            canonicalPlan.ToString(SaveOptions.DisableFormatting));
    }

    private static string CalculateSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

