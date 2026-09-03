using T9Pane.Services;

namespace T9Pane.Tests;

public class PinyinPreviewPolicyTests
{
    [Fact]
    public void Stops_at_typed_keys_even_if_first_candidate_is_complete()
    {
        // 84267 = t i a n q。天气是 tian qi，但不能把没按下的 i 写进组字栏。
        Assert.Equal("tian'q", PinyinPreviewPolicy.FromTypedDigits("84267", "tian qi"));
        Assert.Equal("tian'qi", PinyinPreviewPolicy.FromTypedDigits("842674", "tian qi"));
        Assert.Equal("tian", PinyinPreviewPolicy.FromTypedDigits("8426", "tian qi"));
    }

    [Fact]
    public void Keeps_syllable_mark_only_after_a_finished_syllable()
    {
        Assert.Equal("ni'h", PinyinPreviewPolicy.FromTypedDigits("644", "ni hao"));
        Assert.Equal("ni", PinyinPreviewPolicy.FromTypedDigits("64", "ni hao"));
    }

    [Fact]
    public void Without_a_candidate_uses_the_first_letter_on_each_key()
    {
        Assert.Equal("tgamp", PinyinPreviewPolicy.FromTypedDigits("84267", null));
        Assert.Equal("", PinyinPreviewPolicy.FromTypedDigits("", "tian qi"));
    }
}
