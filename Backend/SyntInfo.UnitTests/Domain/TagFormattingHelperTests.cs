using SyntInfo.Domain.Entities;

namespace SyntInfo.UnitTests.Domain;

public class TagFormattingHelperTests
{
    [Theory]
    [InlineData("Polityka & Gospodarka!", "politykagospodarka")]
    [InlineData("Świat 2026", "świat2026")]
    [InlineData("  #Technologia-AI  ", "technologiaai")]
    public void SanitizeTag_WhenValidText_RemovesSpecialCharactersAndLowercases(string input, string expected)
    {
        var result = TagFormattingHelper.SanitizeTag(input, "wiadomosci");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!###")]
    public void SanitizeTag_WhenNullOrNoAlphanumericChars_ReturnsFallback(string? input)
    {
        var result = TagFormattingHelper.SanitizeTag(input, "fallbacktag");
        result.Should().Be("fallbacktag");
    }
}

