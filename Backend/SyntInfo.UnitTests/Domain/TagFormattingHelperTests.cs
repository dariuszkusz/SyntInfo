using SyntInfo.Domain.Common;
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
    [InlineData("Zażółć gęślą jaźń", "zazolcgeslajazn")]
    [InlineData("ŚWIAT & Łódź", "swiatlodz")]
    public void NormalizeAsciiTag_WhenPolishDiacritics_ConvertsToAsciiLowercase(string input, string expected)
    {
        var result = TagFormattingHelper.NormalizeAsciiTag(input, "wiadomosci");
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

    [Fact]
    public void BuildHashtagsForArticle_WhenArticleIsNull_ThrowsArgumentNullException()
    {
        var act = () => TagFormattingHelper.BuildHashtagsForArticle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHashtagsForArticle_WhenPositiveRegion_NeverIncludesPolitykaEvenIfInCategoryOrAiTags()
    {
        var article = new NewsArticle
        {
            Title = "Przełom w medycynie",
            Region = SourceRegion.Positive,
            Category = new NewsCategory { Name = "Polityka" },
            Tags = new List<string> { "polityka", "wojna", "medycyna", "nauka" }
        };

        var hashtags = TagFormattingHelper.BuildHashtagsForArticle(article);

        hashtags.Should().Contain("#wiadomosci");
        hashtags.Should().Contain("#infoskrot");
        hashtags.Should().Contain("#dobrewiadomosci");
        hashtags.Should().Contain("#pozytywnie");
        hashtags.Should().Contain("#medycyna");
        hashtags.Should().Contain("#nauka");
        hashtags.Should().NotContain("#polityka");
        hashtags.Should().NotContain("#wojna");
    }

    [Fact]
    public void BuildHashtagsForArticle_WhenPolandAndTechCategory_DoesNotIncludePolityka()
    {
        var article = new NewsArticle
        {
            Title = "Nowe centrum AI w Warszawie",
            Region = SourceRegion.Poland,
            Category = new NewsCategory { Name = "TECH" },
            Tags = new List<string> { "sztucznainteligencja", "inwestycje" }
        };

        var hashtags = TagFormattingHelper.BuildHashtagsForArticle(article);

        hashtags.Should().Contain("#wiadomosci");
        hashtags.Should().Contain("#infoskrot");
        hashtags.Should().Contain("#polska");
        hashtags.Should().Contain("#technologia");
        hashtags.Should().Contain("#sztucznainteligencja");
        hashtags.Should().NotContain("#polityka");
    }

    [Fact]
    public void BuildHashtagsForArticle_WhenWorldAndPolitykaCategory_IncludesPolitykaAndWorldTags()
    {
        var article = new NewsArticle
        {
            Title = "Szczyt dyplomatyczny w Brukseli",
            Region = SourceRegion.World,
            Category = new NewsCategory { Name = "POLITYKA" },
            Tags = new List<string> { "unia", "dyplomacja" }
        };

        var hashtags = TagFormattingHelper.BuildHashtagsForArticle(article);

        hashtags.Should().Contain("#wiadomosci");
        hashtags.Should().Contain("#infoskrot");
        hashtags.Should().Contain("#swiat");
        hashtags.Should().Contain("#polityka");
        hashtags.Should().Contain("#unia");
    }

    [Fact]
    public void BuildHashtagsForDigest_WhenNoPoliticalArticles_DoesNotIncludePolityka()
    {
        var articles = new List<NewsArticle>
        {
            new()
            {
                Region = SourceRegion.Poland,
                Category = new NewsCategory { Name = "GOSPODARKA" },
                Tags = new List<string> { "energetyka" }
            },
            new()
            {
                Region = SourceRegion.Positive,
                Category = new NewsCategory { Name = "NAUKA" },
                Tags = new List<string> { "kosmos" }
            }
        };

        var hashtags = TagFormattingHelper.BuildHashtagsForDigest(articles);

        hashtags.Should().Contain("#wiadomosci");
        hashtags.Should().Contain("#infoskrot");
        hashtags.Should().Contain("#polska");
        hashtags.Should().Contain("#dobrewiadomosci");
        hashtags.Should().Contain("#gospodarka");
        hashtags.Should().NotContain("#polityka");
    }
}
