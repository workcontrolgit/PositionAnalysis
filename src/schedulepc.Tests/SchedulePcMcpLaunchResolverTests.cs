using SchedulePC;
using Xunit;

namespace SchedulePC.Tests;

public class SchedulePcMcpLaunchResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_PrefersPublishedExecutableAlongsideWorker()
    {
        var mcpDirectory = Path.Combine(_root, "SchedulePCMcp");
        Directory.CreateDirectory(mcpDirectory);
        var executablePath = Path.Combine(mcpDirectory, "SchedulePCMcp.exe");
        File.WriteAllText(executablePath, string.Empty);

        var launchCommand = SchedulePcMcpLaunchResolver.Resolve(_root);

        Assert.Equal(executablePath, launchCommand.Command);
        Assert.Equal(string.Empty, launchCommand.Arguments);
        Assert.Equal(mcpDirectory, launchCommand.WorkingDirectory);
    }

    [Fact]
    public void Resolve_UsesPublishedDllWhenNoWindowsExecutableExists()
    {
        var mcpDirectory = Path.Combine(_root, "SchedulePCMcp");
        Directory.CreateDirectory(mcpDirectory);
        var assemblyPath = Path.Combine(mcpDirectory, "SchedulePCMcp.dll");
        File.WriteAllText(assemblyPath, string.Empty);

        var launchCommand = SchedulePcMcpLaunchResolver.Resolve(_root);

        Assert.Equal("dotnet", launchCommand.Command);
        Assert.Equal($"\"{assemblyPath}\"", launchCommand.Arguments);
    }

    [Fact]
    public void Resolve_FallsBackToSourceProjectForDeveloperRuns()
    {
        var applicationBaseDirectory = Path.Combine(_root, "src", "schedulepc", "bin", "Debug", "net10.0");
        var projectDirectory = Path.Combine(_root, "src", "SchedulePCMcp");
        Directory.CreateDirectory(applicationBaseDirectory);
        Directory.CreateDirectory(projectDirectory);
        var projectPath = Path.Combine(projectDirectory, "SchedulePCMcp.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var launchCommand = SchedulePcMcpLaunchResolver.Resolve(applicationBaseDirectory);

        Assert.Equal("dotnet", launchCommand.Command);
        Assert.Equal($"run --project \"{projectPath}\"", launchCommand.Arguments);
        Assert.Equal(projectDirectory, launchCommand.WorkingDirectory);
    }

    [Fact]
    public void Resolve_ThrowsClearExceptionWhenNoPublishedOrSourceTargetExists()
    {
        var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            SchedulePcMcpLaunchResolver.Resolve(_root));

        Assert.Contains("SchedulePCMcp", exception.Message, StringComparison.Ordinal);
        Assert.Contains(_root, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}