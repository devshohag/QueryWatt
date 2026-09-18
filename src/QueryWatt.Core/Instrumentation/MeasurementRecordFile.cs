using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// The bridge between the application process, which produces measurements, and the CLI, which
/// reports on them: one JSON object per line, appended as scopes close.
/// </summary>
/// <remarks>
/// JSON Lines rather than one JSON document, because a run that crashes half way through still
/// leaves a readable file, and because appending never has to re-read what is already written.
/// </remarks>
public static class MeasurementRecordFile
{
    /// <summary>The serializer settings both ends of the bridge use.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropDerivedProperties }
        }
    };

    /// <summary>
    /// Keeps derived values such as <c>isBaselineEligible</c> and <c>totalLogicalReads</c> out of
    /// the file. They are recomputed on read, so writing them would only invite a file whose
    /// summary disagrees with its own data.
    /// </summary>
    private static void DropDerivedProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            if (typeInfo.Properties[index].Set is null)
            {
                typeInfo.Properties.RemoveAt(index);
            }
        }
    }

    /// <summary>Serializes one record as a single line.</summary>
    /// <param name="record">The record to serialize.</param>
    /// <returns>One line of JSON, without a trailing newline.</returns>
    public static string ToLine(MeasurementRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, SerializerOptions);
    }

    /// <summary>Reads back every record a run wrote, skipping any line left torn by a crash.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The records, in the order the scopes closed.</returns>
    public static IReadOnlyList<MeasurementRecord> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var records = new List<MeasurementRecord>();

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            MeasurementRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<MeasurementRecord>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // A process killed mid-write leaves one incomplete line. Everything before it is
                // still good, and reporting on it beats refusing to report at all.
                continue;
            }

            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }
}

/// <summary>
/// A sink that appends every completed root scope to a JSON Lines file, so a CLI run afterwards can
/// report on what the application did.
/// </summary>
/// <remarks>
/// Only root scopes are written: a nested scope already travels inside its parent's
/// <see cref="MeasurementRecord.Children"/>, so writing both would double-count it.
/// </remarks>
public sealed class JsonLinesMeasurementSink : IMeasurementSink, IDisposable
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _writer;

    /// <summary>Opens (or creates) the file this sink appends to.</summary>
    /// <param name="path">The file to append to.</param>
    /// <param name="append">False to start a fresh file, true to add to an existing one.</param>
    public JsonLinesMeasurementSink(string path, bool append = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Path = path;
        _writer = new StreamWriter(path, append) { AutoFlush = true };
    }

    /// <summary>The file being written.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public void Publish(MeasurementRecord record)
    {
        if (record is null || record.Depth > 0)
        {
            return;
        }

        try
        {
            var line = MeasurementRecordFile.ToLine(record);

            lock (_gate)
            {
                _writer.WriteLine(line);
            }
        }
        catch (Exception)
        {
            // A sink may never throw into the application's call. Contract v2 §3.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
