using System.Text;
using FluentAssertions;
using QualityLock.Shared.Input;

namespace QualityLock.Application.Tests;

/// <summary>Separacion de codigos del escaner por puerto COM (CR/LF como terminador).</summary>
public class ScanCodeSplitterTests
{
    [Fact]
    public void MixedTerminators_YieldEachCodeOnce()
        => ScanCodeSplitter.Take(new StringBuilder(), "A1\r\nB2\n\r")
            .Should().Equal("A1", "B2");

    [Fact]
    public void CodeSplitAcrossReads_IsJoined()
    {
        var buffer = new StringBuilder();
        ScanCodeSplitter.Take(buffer, "A").Should().BeEmpty();
        ScanCodeSplitter.Take(buffer, "1\r").Should().Equal("A1");
    }

    [Fact]
    public void Code_IsTrimmed()
        => ScanCodeSplitter.Take(new StringBuilder(), "  X1 \r").Should().Equal("X1");

    [Fact]
    public void BlankLines_AreDropped()
        => ScanCodeSplitter.Take(new StringBuilder(), " \r\n").Should().BeEmpty();
}
