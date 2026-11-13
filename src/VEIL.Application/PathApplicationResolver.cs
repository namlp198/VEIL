using VEIL.Platform.Abstractions;

namespace VEIL.Application;

/// <summary>
/// Portable resolver: accepts a path to an existing program file. The Windows adapter replaces it with one that also
/// follows shortcuts (.lnk) and reads the product name from the executable.
/// </summary>
public sealed class PathApplicationResolver : IApplicationResolver
{
    public ResolveResult Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ResolveResult.Fail("Nothing was dropped.");
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return ResolveResult.Fail("That is not a usable path."); }

        if (Directory.Exists(full)) return ResolveResult.Fail("That is a folder, not an application.");
        if (!File.Exists(full)) return ResolveResult.Fail("That file does not exist.");
        if (OperatingSystem.IsWindows() && !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return ResolveResult.Fail("Only programs (.exe) and shortcuts to programs can be added.");
        if (full.Length > 1024) return ResolveResult.Fail("That path is too long.");
        return ResolveResult.Ok(new ResolvedApplication(full.ToLowerInvariant(), Path.GetFileNameWithoutExtension(full)));
    }
}
