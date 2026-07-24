using FlareQuotes.App.ViewModels;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class FireplaceQuoteDraftTests
{
    [Fact]
    public void HasClassicMedia_IsFalseForNoneSelected()
    {
        var draft = new FireplaceQuoteDraft { ClassicMediaSummary = "None selected" };

        Assert.False(draft.HasClassicMedia);
    }

    [Fact]
    public void HasClassicMedia_IsTrueForSelectedMedia()
    {
        var draft = new FireplaceQuoteDraft { ClassicMediaSummary = "Black Reflective Glass" };

        Assert.True(draft.HasClassicMedia);
    }
}
