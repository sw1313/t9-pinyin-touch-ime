using T9Pane.Services;

namespace T9Pane.Tests;

public class LogRetentionPolicyTests
{
    [Fact]
    public void Rotates_only_when_the_file_reaches_the_cap()
    {
        Assert.False(LogRetentionPolicy.ShouldRotate(0));
        Assert.False(LogRetentionPolicy.ShouldRotate(LogRetentionPolicy.MaxBytes - 1));
        Assert.True(LogRetentionPolicy.ShouldRotate(LogRetentionPolicy.MaxBytes));
        Assert.True(LogRetentionPolicy.ShouldRotate(LogRetentionPolicy.MaxBytes + 1));
    }

    [Fact]
    public void Backup_sits_next_to_the_live_log()
    {
        Assert.Equal(@"C:\a\t9pane.log.old", LogRetentionPolicy.BackupPath(@"C:\a\t9pane.log"));
    }
}
