namespace QueryWatt.Testing;

/// <summary>
/// Finds the committed baseline from inside a test run.
/// </summary>
/// <remarks>
/// Tests execute from <c>bin/Debug/netX/</c>, so a relative path would resolve into the build output
/// and a baseline written there would be thrown away by the next clean. The baseline belongs beside
/// the source it guards, so this walks up looking for an existing baseline first, then for the
/// repository root.
/// </remarks>
public static class BaselineLocator
{
    /// <summary>Resolves a baseline path to an absolute one.</summary>
    /// <param name="path">An absolute path, or a file name to search for.</param>
    /// <param name="startDirectory">Where to start searching; defaults to the test binaries.</param>
    /// <returns>The absolute path to use.</returns>
    public static string Resolve(string path, string? startDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var start = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        // An existing baseline wins: if a repository already has one, that is the file this run
        // must judge against, wherever it sits.
        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return Path.Combine(directory.FullName, path);
            }
        }

        return Path.GetFullPath(path);
    }
}
