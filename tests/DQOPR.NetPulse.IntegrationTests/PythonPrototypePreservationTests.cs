namespace DQOPR.NetPulse.IntegrationTests;

public sealed class PythonPrototypePreservationTests
{
    [Fact]
    public void PythonPrototypeRemainsAvailableDuringRewrite()
    {
        var repositoryRoot = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(repositoryRoot, "src", "dqopr_netpulse", "__init__.py")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "pyproject.toml")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DQOPR.NetPulse.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
