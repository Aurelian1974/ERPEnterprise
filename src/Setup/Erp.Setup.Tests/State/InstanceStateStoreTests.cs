using Erp.Setup.Core.State;

namespace Erp.Setup.Tests.State;

public sealed class InstanceStateStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "erp-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void State_is_written_atomically_with_documented_names_and_read_back()
    {
        var path = InstanceStateStore.PathFor(root, "PROD");
        var state = new InstanceState
        {
            Instance = "PROD",
            InstallId = Guid.NewGuid(),
            State = InstanceLifecycle.Installing,
            InstallRoot = @"C:\Program Files\Erp",
            DataRoot = @"C:\ProgramData\Erp",
            DatabaseName = "ERP_PROD",
            CompletedSteps = ["I-01", "I-02"],
        };

        InstanceStateStore.Write(path, state);
        InstanceStateStore.Write(path, state with { State = InstanceLifecycle.Installed, CurrentVersion = "2027.1.0" });

        var json = File.ReadAllText(path);
        Assert.Contains("\"state\": \"Installed\"", json);
        Assert.Contains("\"completedSteps\": [", json);
        Assert.Contains("\"currentVersion\": \"2027.1.0\"", json);
        Assert.False(File.Exists(path + ".tmp"));

        var read = InstanceStateStore.Read(path)!;
        Assert.Equal(InstanceLifecycle.Installed, read.State);
        Assert.Equal(state.InstallId, read.InstallId);
        Assert.Equal(["I-01", "I-02"], read.CompletedSteps);
    }

    [Fact]
    public void Missing_state_file_reads_as_null()
    {
        Assert.Null(InstanceStateStore.Read(InstanceStateStore.PathFor(root, "TEST")));
    }
}
