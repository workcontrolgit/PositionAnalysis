namespace SchedulePC;

public sealed record SchedulePcMcpLaunchCommand(string Command, string Arguments, string WorkingDirectory);

public static class SchedulePcMcpLaunchResolver
{
    public static SchedulePcMcpLaunchCommand Resolve(string applicationBaseDirectory)
    {
        var publishedDirectory = Path.Combine(applicationBaseDirectory, "SchedulePCMcp");
        var executablePath = Path.Combine(publishedDirectory, "SchedulePCMcp.exe");
        if (File.Exists(executablePath))
            return new SchedulePcMcpLaunchCommand(executablePath, string.Empty, publishedDirectory);

        var assemblyPath = Path.Combine(publishedDirectory, "SchedulePCMcp.dll");
        if (File.Exists(assemblyPath))
            return new SchedulePcMcpLaunchCommand("dotnet", $"\"{assemblyPath}\"", publishedDirectory);

        var projectPath = Path.GetFullPath(
            Path.Combine(applicationBaseDirectory, "..", "..", "..", "..", "SchedulePCMcp", "SchedulePCMcp.csproj"));
        if (File.Exists(projectPath))
            return new SchedulePcMcpLaunchCommand(
                "dotnet",
                $"run --project \"{projectPath}\"",
                Path.GetDirectoryName(projectPath)!);

        throw new DirectoryNotFoundException(
            $"Could not locate a published or source SchedulePCMcp launch target from: {applicationBaseDirectory}");
    }
}