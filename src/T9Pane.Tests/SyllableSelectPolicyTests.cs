using T9Pane.Services;

namespace T9Pane.Tests;

public class SyllableSelectPolicyTests
{
    [Fact]
    public void Confirming_first_syllable_lists_the_second()
    {
        var confirmed = SyllableSelectPolicy.Confirm([], "ni");
        Assert.Equal(["ni"], confirmed);
        Assert.Equal(
            ["hao", "men"],
            SyllableSelectPolicy.Rail(
                ["ni hao", "ni men", "li kai"],
                confirmed));
        Assert.True(SyllableSelectPolicy.Matches("ni hao", confirmed));
        Assert.False(SyllableSelectPolicy.Matches("li kai", confirmed));
    }

    [Fact]
    public void Repeated_syllables_can_be_confirmed_twice()
    {
        var once = SyllableSelectPolicy.Confirm([], "ma");
        var twice = SyllableSelectPolicy.Confirm(once, "ma");
        Assert.Equal(["ma", "ma"], twice);
        Assert.True(SyllableSelectPolicy.Matches("ma ma", twice));
    }

    [Fact]
    public void Backspace_pops_the_last_confirmed_syllable()
    {
        var confirmed = SyllableSelectPolicy.Confirm(["ni"], "hao");
        Assert.Equal(["ni"], SyllableSelectPolicy.Pop(confirmed));
        Assert.Empty(SyllableSelectPolicy.Pop(["ni"]));
    }

    [Fact]
    public void Display_keeps_typed_preview_when_it_already_includes_the_head()
    {
        Assert.Equal("ni'h", SyllableSelectPolicy.Display(["ni"], "ni'h"));
        Assert.Equal("ni", SyllableSelectPolicy.Display(["ni"], ""));
        Assert.Equal("ni'hao", SyllableSelectPolicy.Display(["ni", "hao"], "wo"));
    }
}

public class CompositionConsumePolicyTests
{
    [Fact]
    public void Choosing_a_short_word_keeps_the_remaining_digits()
    {
        var consumed = CompositionConsumePolicy.ConsumeDigits("组词", "ni", "64426");
        Assert.Equal(2, consumed);
        Assert.True(CompositionConsumePolicy.HasRemainder(consumed, 5));
        Assert.Equal("426", "64426"[consumed..]);
    }

    [Fact]
    public void Choosing_the_whole_phrase_commits_everything()
    {
        Assert.Equal(5, CompositionConsumePolicy.ConsumeDigits("全拼", "ni hao", "64426"));
        Assert.False(CompositionConsumePolicy.HasRemainder(5, 5));
    }

    [Fact]
    public void Letter_board_can_top_the_first_character()
    {
        var consumed = CompositionConsumePolicy.ConsumeLetters("组词", "ni", "nihao");
        Assert.Equal(2, consumed);
        Assert.Equal("hao", "nihao"[consumed..]);
    }

    [Theory]
    [InlineData("zi yuan", "zi", "yuan")]
    [InlineData("lü-se", "lv", "se")]
    [InlineData("ZHONG'GUO", "zhong", "guo")]
    public void Syllables_split_on_common_separators(string pinyin, string first, string second)
    {
        var parts = T9Engine.Syllables(pinyin);
        Assert.Equal(first, parts[0]);
        Assert.Equal(second, parts[1]);
        Assert.Equal(first, T9Engine.FirstSyllable(pinyin));
        Assert.Equal(second, T9Engine.SyllableAt(pinyin, 1));
    }
}

public class T9SyllableTableTests
{
    [Fact]
    public void Current_syllables_are_completions_not_shorter_heads()
    {
        var table = new T9SyllableTable();
        foreach (var syllable in new[] { "ni", "mi", "niao", "miao", "nie", "hao", "o" })
        {
            table.Add(syllable);
        }

        var current = table.MatchCurrent("64");
        Assert.Equal(["mi", "ni", "nie", "miao", "niao"], current);
        Assert.DoesNotContain("hao", current);
        Assert.DoesNotContain("o", current);
    }

    [Fact]
    public void Remaining_digits_advance_after_a_confirmed_syllable()
    {
        Assert.Equal("426", T9SyllableTable.RemainingDigits("64426", ["ni"]));
        Assert.Equal("hao", T9SyllableTable.RemainingLetters("nihao", ["ni"]));
    }
}
