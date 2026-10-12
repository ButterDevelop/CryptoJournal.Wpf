using CryptoJournal.Wpf.Services.Updates;

namespace CryptoJournal.Wpf.Tests.Updates;

[TestFixture]
internal sealed class ReleaseVersionTests
{
    [TestCase("v1.2.3", 1, 2, 3)]
    [TestCase("1.2.3+build", 1, 2, 3)]
    [TestCase("1.2.3.0", 1, 2, 3)]
    public void TryParse_ValidVersion_ReturnsThreeComponents(
        string value,
        int expectedMajor,
        int expectedMinor,
        int expectedPatch)
    {
        var parsed = ReleaseVersion.TryParse(value, out var version);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(version.Major, Is.EqualTo(expectedMajor));
            Assert.That(version.Minor, Is.EqualTo(expectedMinor));
            Assert.That(version.Patch, Is.EqualTo(expectedPatch));
        }
    }

    [TestCase(1, 10, 0, 1, 9, 9)]
    [TestCase(2, 0, 0, 1, 99, 99)]
    [TestCase(1, 0, 1, 1, 0, 0)]
    public void CompareTo_NewerVersion_ReturnsPositive(
        int major,
        int minor,
        int patch,
        int oldMajor,
        int oldMinor,
        int oldPatch)
    {
        var current = new ReleaseVersion(major, minor, patch);
        var old     = new ReleaseVersion(oldMajor, oldMinor, oldPatch);

        Assert.That(current.CompareTo(old), Is.GreaterThan(0));
    }

    [TestCase("")]
    [TestCase("latest")]
    [TestCase("1")]
    [TestCase("1.2.invalid")]
    public void TryParse_InvalidVersion_ReturnsFalse(string value)
    {
        Assert.That(ReleaseVersion.TryParse(value, out _), Is.False);
    }
}
