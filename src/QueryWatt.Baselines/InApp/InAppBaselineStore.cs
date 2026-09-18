using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryWatt.Baselines.InApp;

/// <summary>Reads and writes the in-app baseline file.</summary>
/// <remarks>
/// Indented, stable ordering, no derived values: the file is committed, and a reviewer should be
/// able to read the diff and see exactly which scenario moved and by how much.
/// </remarks>
public static class InAppBaselineStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Reads a baseline, or returns null when the file does not exist.</summary>
    /// <param name="path">Path to the baseline file.</param>
    /// <returns>The document, or null.</returns>
    /// <exception cref="InAppBaselineException">The file exists but cannot be used.</exception>
    public static InAppBaselineDocument? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        InAppBaselineDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<InAppBaselineDocument>(File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            throw new InAppBaselineException($"The baseline at '{path}' is not readable JSON.", exception);
        }

        if (document is null)
        {
            throw new InAppBaselineException($"The baseline at '{path}' is empty.");
        }

        if (document.SchemaVersion != InAppBaselineContract.SchemaVersion)
        {
            throw new InAppBaselineException(
                $"The baseline at '{path}' uses schema {document.SchemaVersion}, and this tool "
                + $"writes schema {InAppBaselineContract.SchemaVersion}. Re-accept it rather than "
                + "comparing across schemas.");
        }

        return document;
    }

    /// <summary>Writes a baseline, creating the directory when needed.</summary>
    /// <param name="path">Path to write to.</param>
    /// <param name="document">The document to write.</param>
    public static void Write(string path, InAppBaselineDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(document, Options) + Environment.NewLine);
    }
}

/// <summary>Raised when a baseline file exists but cannot be used as one.</summary>
public sealed class InAppBaselineException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the file.</param>
    public InAppBaselineException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the file.</param>
    /// <param name="innerException">The underlying failure.</param>
    public InAppBaselineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
