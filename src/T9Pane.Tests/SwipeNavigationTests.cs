using T9Pane.Services;

namespace T9Pane.Tests;

public class SwipeNavigationTests
{
    [Theory]
    [InlineData(-100, 20, "Left")]
    [InlineData(100, 20, "Right")]
    public void Horizontal_swipe_selects_candidate_page(double endX, double endY, string expected)
    {
        Assert.Equal(expected, SwipeNavigation.Detect(0, 0, endX, endY).ToString());
    }

    [Fact]
    public void Short_motion_remains_a_tap()
    {
        Assert.Equal(SwipeDirection.None, SwipeNavigation.Detect(10, 10, 25, 20));
    }

    [Fact]
    public void Page_navigation_wraps_in_both_directions()
    {
        Assert.Equal(0, SwipeNavigation.MovePage(2, 3, true));
        Assert.Equal(2, SwipeNavigation.MovePage(0, 3, false));
    }

    [Fact]
    public void Forward_page_enters_from_right_and_previous_enters_from_left()
    {
        Assert.Equal(320, SwipeNavigation.InitialOffset(320, true));
        Assert.Equal(-320, SwipeNavigation.InitialOffset(320, false));
    }

    [Theory]
    [InlineData("zi yuan", "zi")]
    [InlineData("lü-se", "lv")]
    [InlineData("ZHONG'GUO", "zhong")]
    public void Pinyin_rail_uses_first_syllable(string pinyin, string expected)
    {
        Assert.Equal(expected, T9Engine.FirstSyllable(pinyin));
    }
}
