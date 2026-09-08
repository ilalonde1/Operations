using System;
using System.IO;
using System.Threading.Tasks;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers timeout withdrawing this client's request while retaining unrelated inbox commands, and
/// reporting a request already moved to done. Does not run Revit or simulate SMB permissions.
/// A same-class fault this would not catch: accepting a malformed successful reply in ParseReply.
/// </summary>
public sealed class DrafterBridgeTimeoutTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kor-bridge-timeout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Timeout_withdraws_only_its_own_command()
    {
        var inbox = Path.Combine(_folder, "inbox");
        Directory.CreateDirectory(inbox);
        var unrelated = Path.Combine(inbox, "unrelated.json");
        await File.WriteAllTextAsync(unrelated, "{}");
        var bridge = new DrafterBridgeClient(_folder);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => bridge.SendAsync(new { verb = "ping" }, TimeSpan.FromMilliseconds(1)));

        Assert.Contains("withdrawn", error.Message);
        Assert.Equal(new[] { unrelated }, Directory.GetFiles(inbox, "*.json"));
    }

    [Fact]
    public async Task Timeout_reports_a_request_already_picked_up()
    {
        var inbox = Path.Combine(_folder, "inbox");
        var done = Path.Combine(inbox, "done");
        Directory.CreateDirectory(done);
        var bridge = new DrafterBridgeClient(_folder);
        var request = bridge.SendAsync(new { verb = "ping" }, TimeSpan.FromSeconds(2));
        string[] queued;
        var deadline = DateTime.UtcNow.AddSeconds(1);
        do
        {
            queued = Directory.GetFiles(inbox, "*.json");
            if (queued.Length > 0) break;
            await Task.Delay(10);
        }
        while (DateTime.UtcNow < deadline);

        var command = Assert.Single(queued);
        var pickedUp = Path.Combine(done, Path.GetFileName(command));
        File.Move(command, pickedUp);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => request);

        Assert.Contains("already picked up", error.Message);
        Assert.True(File.Exists(pickedUp));
        Assert.Empty(Directory.GetFiles(inbox, "*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
