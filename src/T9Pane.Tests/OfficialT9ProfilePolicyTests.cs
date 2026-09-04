using T9Pane.Services;

namespace T9Pane.Tests;

public class OfficialT9ProfilePolicyTests
{
    private static readonly Guid MicrosoftPinyin = new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    private static readonly Guid MicrosoftPinyinProfile = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    [Fact]
    public void Language_bar_t9_is_the_t9_clsid_and_profile()
    {
        Assert.True(OfficialT9ProfilePolicy.IsT9Layout(
            OfficialT9ProfilePolicy.T9Clsid,
            OfficialT9ProfilePolicy.T9Profile));
    }

    [Fact]
    public void Microsoft_pinyin_on_the_language_bar_is_not_t9()
    {
        Assert.False(OfficialT9ProfilePolicy.IsT9Layout(MicrosoftPinyin, MicrosoftPinyinProfile));
    }

    [Fact]
    public void Leftover_t9_threads_do_not_count_as_language_bar_selection()
    {
        Assert.False(OfficialT9ProfilePolicy.IsT9Layout(MicrosoftPinyin, Guid.Empty));
        Assert.False(OfficialT9ProfilePolicy.IsCurrentSelection(
            getActiveSucceeded: false,
            isT9Layout: true));
        Assert.True(OfficialT9ProfilePolicy.IsCurrentSelection(
            getActiveSucceeded: true,
            isT9Layout: true));
        Assert.False(OfficialT9ProfilePolicy.IsCurrentSelection(
            getActiveSucceeded: true,
            isT9Layout: false));
    }

    [Fact]
    public void Official_get_active_profile_is_a_keyboard_tip()
    {
        var active = TsfLayoutSelection.TryGetActive(out var clsid, out var profile);
        var fallback = TsfLayoutSelection.TryReadAssembly(
            OfficialT9ProfilePolicy.SimplifiedChinese,
            out var assemblyClsid,
            out _);
        Assert.True(active || fallback);
        if (active)
        {
            Assert.NotEqual(Guid.Empty, clsid);
            Assert.NotEqual(Guid.Empty, profile);
        }
        else
        {
            Assert.NotEqual(Guid.Empty, assemblyClsid);
        }
    }

    [Fact]
    public void Assembly_text_from_ctf_parses()
    {
        Assert.True(OfficialT9ProfilePolicy.TryParseAssembly(
            "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}",
            "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002}",
            out var clsid,
            out var profile));
        Assert.True(OfficialT9ProfilePolicy.IsT9Layout(clsid, profile));
    }

    [Fact]
    public void Assembly_key_matches_language_bar_store()
    {
        Assert.Equal(
            @"Software\Microsoft\CTF\Assemblies\0x00000804\{34745C63-B2F0-4784-8B67-5E12C8701A31}",
            OfficialT9ProfilePolicy.AssemblyKey(0x0804));
    }

    [Fact]
    public void Hkl_low_word_is_the_langid()
    {
        Assert.Equal((ushort)0x0804, OfficialT9ProfilePolicy.LangidFromHkl(new IntPtr(0x08040804)));
    }
}
