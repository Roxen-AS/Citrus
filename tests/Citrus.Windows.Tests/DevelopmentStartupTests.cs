namespace Citrus.Windows.Tests;

public sealed class DevelopmentStartupTests
{
    [Fact]
    public void Development_identity_passes_real_single_instance_validation_and_is_separate_from_normal_startup()
    {
        var development = App.CreateStartupOptions(["Citrus.Windows.exe", "--development"]);
        var normal = App.CreateStartupOptions(["Citrus.Windows.exe"]);
        Assert.True(Guid.TryParseExact(development.InstanceSuffix, "N", out _));
        Assert.NotNull(development.SettingsRoot);
        Assert.Null(normal.InstanceSuffix); Assert.Null(normal.SettingsRoot);
        using var instance = new SingleInstance(development.InstanceSuffix);
        Assert.Contains(development.InstanceSuffix!, instance.Identity);
    }
}
