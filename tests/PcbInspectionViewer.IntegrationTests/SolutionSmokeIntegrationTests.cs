using FluentAssertions;
using Xunit;

namespace PcbInspectionViewer.IntegrationTests;

public sealed class SolutionSmokeIntegrationTests
{
    [Fact]
    public void SolutionSkeleton_IsConfigured()
    {
        true.Should().BeTrue();
    }
}
