using Dedsec.Monitor;
using Xunit;

namespace Dedsec.WindowsTests;

public sealed class HardwareReadingTests
{
    [Fact]
    public void HybridCoreClocksAreIncludedAndBusClocksAreExcluded()
    {
        Assert.Equal(2000d, HardwareService.AverageCoreClock([
            ("P-Core #1", 3000), ("E-Core #1", 1000), ("Bus Speed", 100),
            ("P-Core #2", null), ("E-Core #2", float.NaN)]));
        Assert.Equal(3000d, HardwareService.AverageCoreClock([("CPU Core #1", 2500), ("Core #2", 3500)]));
        Assert.Null(HardwareService.AverageCoreClock([("Bus Speed", 100), ("CPU Core #1", 0)]));
    }
}
