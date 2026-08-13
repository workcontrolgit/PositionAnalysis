using System.Linq;

namespace PositionAnalysis.Cli;

public sealed record SchedulePcMcpLaunchCommand(string Command, string Arguments, string WorkingDirectory);

public static class SchedulePcMcpLaunchResolver
{
    public static SchedulePcMcpLaunchCommand Resolve(string applicationBaseDirectory)
    {
        var publishedDirectory = Path.Combine(applicationBaseDirectory, "PositionAnalysis.Mcp");
        var executablePath = Path.Combine(publishedDirectory, "PositionAnalysis.Mcp.exe");
        if (File.Exists(executablePath))
            return new SchedulePcMcpLaunchCommand(executablePath, string.Empty, publishedDirectory);

        var assemblyPath = Path.Combine(publishedDirectory, "PositionAnalysis.Mcp.dll");
        if (File.Exists(assemblyPath))
            return new SchedulePcMcpLaunchCommand("dotnet", $"\"{assemblyPath}\"", publishedDirectory);

        var projectDirectory = Path.GetFullPath(
            Path.Combine(applicationBaseDirectory, "..", "..", "..", "..", "PositionAnalysis.Mcp"));
        var projectPath = Path.Combine(projectDirectory, "PositionAnalysis.Mcp.csproj");
        if (File.Exists(projectPath))
        {
            // Prefer launching the already-built DLL directly instead of "dotnet run --project".
            // "dotnet run" spawns a nested grandchild process (and may trigger a build), which
            // can bypass this process's stdio redirection and leak the server's console output
            // to the terminal instead of being captured by StdioMcpClient.
            var builtDllPath = FindNewestBuiltAssembly(projectDirectory);
            if (builtDllPath != null)
                return new SchedulePcMcpLaunchCommand(
                    "dotnet",
                    $"exec \"{builtDllPath}\"",
                    Path.GetDirectoryName(builtDllPath)!);

            return new SchedulePcMcpLaunchCommand(
                "dotnet",
                $"run --project \"{projectPath}\"",
                projectDirectory);
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a published or source PositionAnalysis.Mcp launch target from: {applicationBaseDirectory}");
    }

    private static string? FindNewestBuiltAssembly(string projectDirectory)
    {
        var binDirectory = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binDirectory))
            return null;

        return Directory.EnumerateFiles(binDirectory, "PositionAnalysis.Mcp.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
