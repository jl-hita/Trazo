using Microsoft.Extensions.Configuration;
using Trazo.Api.Support;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>What the backup panel may ask for (a copy now, a restore) and what lands in the request directory.</summary>
public sealed class BackupActionsTests : IDisposable
{
    private const string Key = "AGE-SECRET-KEY-1QQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQ";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private const string Copy = "trazo-20261123T033800Z.tar.age";
    private readonly string _status = Path.Combine(Path.GetTempPath(), $"trazo-ba-status-{Guid.NewGuid():N}");
    private readonly string _requests = Path.Combine(Path.GetTempPath(), $"trazo-ba-req-{Guid.NewGuid():N}");
    private readonly BackupActions _actions;

    public BackupActionsTests()
    {
        Directory.CreateDirectory(_status);
        Directory.CreateDirectory(_requests);
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.StatusFile),
            $$"""{"checkedAt":"{{Now:O}}","remote":"r","maxAgeHours":36,"copies":[{"Name":"{{Copy}}","Size":10,"ModTime":"{{Now:O}}"}]}""");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backups:StatusPath"] = _status, ["Backups:RequestPath"] = _requests
        }).Build();
        _actions = new BackupActions(config, new FixedTime(Now));
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _status, _requests })
            try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private string Req(string name) => Path.Combine(_requests, name);

    [Fact]
    public void BackupNow_DropsTheRequest_AndASecondOneWaits()
    {
        Assert.True(_actions.RequestBackup().Ok);
        Assert.True(File.Exists(Req(BackupActions.BackupNowFile)));
        Assert.Equal(409, _actions.RequestBackup().StatusCode);
    }

    [Fact]
    public void BackupNow_IsRefusedWhileOneIsRunning()
    {
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.RunningFile), "");
        Assert.Equal(409, _actions.RequestBackup().StatusCode);
    }

    [Fact]
    public void WithoutTheRequestDirectory_ThePanelSaysItIsNotAvailable()
    {
        Directory.Delete(_requests);
        Assert.Equal(503, _actions.RequestBackup().StatusCode);
    }

    [Fact]
    public void Restore_WritesCopyModeAndOnlyTheKeyLine()
    {
        var result = _actions.RequestRestore(Copy, "test", $"# created: x\n# public key: age1abc\n{Key}\n", null);

        Assert.True(result.Ok);
        Assert.Equal($"copy={Copy}\nmode=test\n", File.ReadAllText(Req(BackupActions.RestoreRequestFile)));
        Assert.Equal(Key + "\n", File.ReadAllText(Req(BackupActions.RestoreKeyFile)));
        Assert.DoesNotContain("AGE-SECRET", result.Message);
    }

    [Fact]
    public void ProductionRestore_NeedsTheTypedConfirmation()
    {
        Assert.Equal(400, _actions.RequestRestore(Copy, "production", Key, null).StatusCode);
        Assert.Equal(400, _actions.RequestRestore(Copy, "production", Key, "restaurar").StatusCode);
        Assert.False(File.Exists(Req(BackupActions.RestoreKeyFile)));
        Assert.True(_actions.RequestRestore(Copy, "production", Key, BackupActions.ProductionConfirmation).Ok);
    }

    [Theory]
    [InlineData("../../etc/passwd", "test", Key)]
    [InlineData("trazo-20200101T000000Z.tar.age", "test", Key)]   // not in the Drive list
    [InlineData(Copy, "everything", Key)]
    [InlineData(Copy, "test", "not a key")]
    [InlineData(Copy, "test", "AGE-SECRET-KEY-1ABC\nAGE-SECRET-KEY-1DEF")]
    [InlineData(Copy, "test", null)]
    public void Restore_RejectsBadInput_AndWritesNothing(string copy, string mode, string? key)
    {
        Assert.Equal(400, _actions.RequestRestore(copy, mode, key, BackupActions.ProductionConfirmation).StatusCode);
        Assert.Empty(Directory.GetFiles(_requests));
    }

    [Fact]
    public void Restore_IsRefusedWhileAnotherIsRunning()
    {
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.RestoreFile), """{"state":"running","mode":"production","copy":"x"}""");
        Assert.Equal(409, _actions.RequestRestore(Copy, "test", Key, null).StatusCode);
    }

    [Fact]
    public void RestoreStatus_IsReadFromItsFile()
    {
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.RestoreFile),
            """{"state":"failed","mode":"test","copy":"c","startedAt":"2026-11-23T10:00:00Z","finishedAt":"2026-11-23T10:01:00Z","message":"boom"}""");
        var restore = BackupStatusReader.Read(_status, Now).Restore!;
        Assert.Equal("failed", restore.State);
        Assert.Equal("boom", restore.Message);
    }
}
