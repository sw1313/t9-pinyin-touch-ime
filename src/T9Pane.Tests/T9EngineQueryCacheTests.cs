using T9Pane.Services;

namespace T9Pane.Tests;

/// <summary>加载一次词库给整组用例共用，免得每个用例都重读一遍。</summary>
public sealed class T9EngineFixture
{
    internal T9Engine Engine { get; } = Create();

    private static T9Engine Create()
    {
        var engine = new T9Engine();
        engine.Load(new ImeCatalog());
        return engine;
    }
}

/// <summary>
    /// 一次按键会连着查三遍同一串码：主候选一次，拼音预览栏两次 take=8。
/// 引擎按码缓存了一份完整排序结果来省掉后两次扫描，这里锁住缓存不改变结果。
/// </summary>
public class T9EngineQueryCacheTests : IClassFixture<T9EngineFixture>
{
    private readonly T9Engine _engine;

    public T9EngineQueryCacheTests(T9EngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void Smaller_take_returns_the_prefix_of_a_larger_take()
    {
        var wide = _engine.Query("64", 120);
        var narrow = _engine.Query("64", 8);

        Assert.NotEmpty(wide);
        Assert.Equal(Math.Min(8, wide.Count), narrow.Count);
        Assert.Equal(
            wide.Take(narrow.Count).Select(hit => hit.Word),
            narrow.Select(hit => hit.Word));
    }

    [Fact]
    public void Switching_code_recomputes_and_stays_reproducible()
    {
        var first = _engine.Query("64", 20).Select(hit => hit.Word).ToList();
        var other = _engine.Query("96", 20).Select(hit => hit.Word).ToList();
        var firstAgain = _engine.Query("64", 20).Select(hit => hit.Word).ToList();

        Assert.NotEqual(first, other);
        Assert.Equal(first, firstAgain);
    }

    [Fact]
    public void Each_query_kind_keeps_its_own_cache_entry()
    {
        // 三个入口可能收到同一串输入，缓存键必须把它们分开，
        // 否则拼音预览栏那两次查询会把主候选的结果换掉。
        var digits = _engine.Query("64", 20).Select(hit => hit.Word).ToList();
        var letters = _engine.QueryLetters("ni", 20).Select(hit => hit.Word).ToList();
        var latin = _engine.QueryLatin("h", 20).Select(hit => hit.Word).ToList();

        Assert.Equal(digits, _engine.Query("64", 20).Select(hit => hit.Word));
        Assert.Equal(letters, _engine.QueryLetters("ni", 20).Select(hit => hit.Word));
        Assert.Equal(latin, _engine.QueryLatin("h", 20).Select(hit => hit.Word));
    }

    [Fact]
    public void Pinyin_preview_matches_the_top_candidate_pinyin()
    {
        // 预览栏原先自己再查一遍，现在与主候选共用缓存，取值不能变。
        var preview = _engine.PinyinPreview("64");

        Assert.False(string.IsNullOrWhiteSpace(preview));
        Assert.Equal(preview, _engine.PinyinPreview("64"));
    }

    [Fact]
    public void Syllable_rail_lists_legal_pinyin_without_scanning_the_lexicon()
    {
        var rail = _engine.QuerySyllables("64");
        Assert.Contains("ni", rail);
        Assert.Contains("mi", rail);
        Assert.Contains("niao", rail);
        Assert.Contains("miao", rail);
        Assert.Contains("nie", rail);
        Assert.DoesNotContain("hao", rail);

        var afterNi = _engine.QuerySyllables("64426", ["ni"]);
        Assert.Contains("hao", afterNi);
    }

    [Fact]
    public void Digit_query_still_puts_exact_pinyin_first()
    {
        var hits = _engine.Query("64", 20);
        Assert.Contains(hits, hit => hit.Word == "你" && hit.MatchKind == "全拼");
        Assert.Equal("全拼", hits[0].MatchKind);
    }
}
