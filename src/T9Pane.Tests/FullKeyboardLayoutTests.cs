using System.Runtime.InteropServices;
using System.Windows.Media;
using T9Pane.Native;
using T9Pane.Overlay;
using T9Pane.Services;

namespace T9Pane.Tests;

public class FullKeyboardLayoutTests
{
    [Fact]
    public void Square_cells_use_the_tighter_side()
    {
        var t9 = FullKeyboardLayout.FitCells(468, 210, 3, 3);
        Assert.Equal(210, t9.Width, 3);
        Assert.Equal(210, t9.Height, 3);

        var full = FullKeyboardLayout.FitCells(800, 250, 16, 5);
        Assert.Equal(800, full.Width, 3);
        Assert.Equal(250, full.Height, 3);
    }

    [Fact]
    public void Every_full_keyboard_row_is_sixteen_square_units()
    {
        foreach (var latin in new[] { false, true })
        {
            foreach (var fn in new[] { false, true })
            {
                var rows = FullKeyboardLayout.Rows(latin, fn);
                Assert.Equal(FullKeyboardLayout.RowCount, rows.Count);
                foreach (var row in rows)
                {
                    Assert.Equal(FullKeyboardLayout.Units, FullKeyboardLayout.RowUnits(row), 3);
                }

                Assert.Equal(latin, rows[^1].Any(key => key.Action == FullKeyAction.Predict));
            }
        }
    }

    [Fact]
    public void Compact_english_board_has_twenty_six_letters()
    {
        Assert.Equal(26, EnglishKeyboardLayout.LetterCount);
        Assert.Equal(10, EnglishKeyboardLayout.Row1.Length);
        Assert.Equal(9, EnglishKeyboardLayout.Row2.Length);
        Assert.Equal(7, EnglishKeyboardLayout.Row3.Length);
        Assert.Equal("Q", EnglishKeyboardLayout.Face("q", shift: true));
        Assert.Equal("q", EnglishKeyboardLayout.Face("q", shift: false));
        var unit = KeyboardChromeSize.EnglishLetterUnit;
        Assert.True(unit >= 56);
        Assert.Equal(63.6 * KeyboardChromeSize.PadScale, unit, 3);
        Assert.Equal(unit * 10, KeyboardChromeSize.EnglishColumns().Board, 3);
        Assert.Equal(unit * 3, KeyboardChromeSize.EnglishBoardHeight, 3);
        Assert.Equal(unit, KeyboardChromeSize.EnglishBoardHeight / 3, 3);
    }

    [Fact]
    public void Press_feedback_is_uniform_shrink()
    {
        Assert.InRange(TouchKeyVisual.PressScale, 0.9, 0.97);
    }

    [Fact]
    public void Function_glyphs_are_icons_not_letters()
    {
        Assert.False(Geometry.Parse(KeyGlyphs.BackspacePath).Bounds.IsEmpty);
        Assert.False(Geometry.Parse(KeyGlyphs.EnterPath).Bounds.IsEmpty);
        Assert.False(Geometry.Parse(KeyGlyphs.CapsPath).Bounds.IsEmpty);
        Assert.False(Geometry.Parse(KeyGlyphs.ShiftPath).Bounds.IsEmpty);
    }

    [Fact]
    public void T9_pad_fits_inside_the_compact_window()
    {
        var used = KeyboardChromeSize.FramePad
            + KeyboardChromeSize.Title
            + KeyboardChromeSize.Candidate
            + KeyboardChromeSize.Function
            + KeyboardChromeSize.CompactColumns().Board;
        Assert.Equal(KeyboardChromeSize.CompactHeight, used, 3);
    }

    [Fact]
    public void Language_and_back_return_to_the_board_you_left()
    {
        Assert.True(BoardNavigation.UpdatesHome(KeyboardSurface.Pinyin));
        Assert.True(BoardNavigation.UpdatesHome(KeyboardSurface.Pinyin26));
        Assert.True(BoardNavigation.UpdatesHome(KeyboardSurface.English));
        Assert.True(BoardNavigation.UpdatesHome(KeyboardSurface.Full));
        Assert.False(BoardNavigation.UpdatesHome(KeyboardSurface.Number));
        Assert.False(BoardNavigation.UpdatesHome(KeyboardSurface.SymbolCn));
        Assert.Equal(
            KeyboardSurface.English,
            BoardNavigation.LanguageOrHome(KeyboardSurface.Pinyin, KeyboardSurface.Pinyin));
        Assert.Equal(
            KeyboardSurface.English,
            BoardNavigation.LanguageOrHome(KeyboardSurface.Pinyin26, KeyboardSurface.Pinyin26));
        Assert.Equal(
            KeyboardSurface.Pinyin26,
            BoardNavigation.LanguageOrHome(KeyboardSurface.English, KeyboardSurface.Pinyin26));
        Assert.Equal(
            KeyboardSurface.Pinyin,
            BoardNavigation.LanguageOrHome(KeyboardSurface.English, KeyboardSurface.English));
        Assert.Equal(
            KeyboardSurface.Full,
            BoardNavigation.LanguageOrHome(KeyboardSurface.Number, KeyboardSurface.Full));
        Assert.Equal(
            KeyboardSurface.English,
            BoardNavigation.LanguageOrHome(KeyboardSurface.Number, KeyboardSurface.English));
        Assert.Equal(
            KeyboardSurface.Pinyin,
            BoardNavigation.LanguageOrHome(KeyboardSurface.SymbolEn, KeyboardSurface.Pinyin));
        Assert.Equal(
            KeyboardSurface.Full,
            BoardNavigation.BackFromTool(KeyboardSurface.Full));
        Assert.Equal(
            KeyboardSurface.English,
            BoardNavigation.BackFromTool(KeyboardSurface.English));
        Assert.Equal(
            KeyboardSurface.Pinyin,
            BoardNavigation.BackFromTool(KeyboardSurface.Pinyin));
    }

    [Fact]
    public void Left_rail_keeps_five_slots_and_pads_empties()
    {
        var slots = LeftRailSlots.Page(["wo", "yo"], 0);
        Assert.Equal(5, slots.Count);
        Assert.Equal("wo", slots[0]);
        Assert.Equal("yo", slots[1]);
        Assert.Null(slots[2]);
        Assert.Null(slots[4]);
        Assert.Equal("zhi", LeftRailSlots.Page(["a", "b", "c", "d", "e", "zhi"], 1)[0]);
    }

    [Fact]
    public void Symbol_pick_returns_home_unless_locked()
    {
        Assert.False(SymbolPanelPolicy.StayAfterPick(false));
        Assert.True(SymbolPanelPolicy.StayAfterPick(true));
        Assert.False(SymbolPanelPolicy.ClearsOnLeave);
        Assert.False(SymbolPanelPolicy.ClearsOnPick);
        Assert.False(T9KeyFace.SymbolSemiBold);
        Assert.True(KeyboardChromeSize.CandidateCell < KeyboardChromeSize.CompactColumns().Board / 3);
        Assert.True(KeyboardChromeSize.CandidateButton <= KeyboardChromeSize.Candidate);
        Assert.True(KeyboardChromeSize.CandidateButton >= 24);
        var recent = SymbolPanelPolicy.Remember(["。", "，"], "！");
        Assert.Equal(["！", "。", "，"], recent);
        Assert.True(SymbolCatalog.Names.Length >= 18);
        Assert.Contains(SymbolCatalog.Chinese, SymbolCatalog.Names);
        Assert.Contains(SymbolCatalog.Hiragana, SymbolCatalog.Names);
        Assert.Contains(SymbolCatalog.Katakana, SymbolCatalog.Names);
        Assert.Contains(SymbolCatalog.Greek, SymbolCatalog.Names);
        Assert.Contains(SymbolCatalog.Russian, SymbolCatalog.Names);
        Assert.Contains(SymbolCatalog.Radical, SymbolCatalog.Names);
        Assert.Equal(SymbolCatalog.English, SymbolCatalog.DefaultName(english: true));
        Assert.Contains("あ", SymbolCatalog.Marks(SymbolCatalog.Hiragana, []));
        Assert.Contains("ア", SymbolCatalog.Marks(SymbolCatalog.Katakana, []));
        Assert.Contains("α", SymbolCatalog.Marks(SymbolCatalog.Greek, []));
        var (symbolRail, symbolBoard) = KeyboardChromeSize.SymbolColumns();
        Assert.True(symbolRail < KeyboardChromeSize.CompactColumns().Rail);
        Assert.True(symbolBoard > KeyboardChromeSize.CompactColumns().Board);
        Assert.Equal(
            KeyboardChromeSize.CompactWidth,
            KeyboardChromeSize.FramePad + symbolRail + symbolBoard,
            3);
        Assert.True(T9KeyFace.SymbolFontSize > T9KeyFace.FontSize);
        Assert.True(T9KeyFace.EnglishFontSize > T9KeyFace.FontSize);
        var resumed = BoardPlaceResume.At(
            new NativeRect { Left = 100, Top = 200, Right = 500, Bottom = 560 },
            620,
            280);
        Assert.Equal(100, resumed.Left);
        Assert.Equal(200, resumed.Top);
        Assert.Equal(720, resumed.Right);
        Assert.Equal(480, resumed.Bottom);
        Assert.True(BoardPlaceResume.ShouldKeepPlace(
            KeyboardSurface.Pinyin26,
            KeyboardSurface.Pinyin26));
        Assert.False(BoardPlaceResume.ShouldKeepPlace(
            KeyboardSurface.Pinyin26,
            KeyboardSurface.Pinyin));
        Assert.True(BoardPlaceResume.ShouldKeepPlace(
            KeyboardSurface.Pinyin,
            KeyboardSurface.Pinyin));
        var resized = BoardPlaceResume.ResizePinnedBottom(
            new NativeRect { Left = 100, Top = 200, Right = 500, Bottom = 560 },
            400,
            300);
        Assert.Equal(100, resized.Left);
        Assert.Equal(260, resized.Top);
        Assert.Equal(500, resized.Right);
        Assert.Equal(560, resized.Bottom);
        Assert.False(BoardPlaceResume.ShouldKeepPlace(
            KeyboardSurface.English,
            KeyboardSurface.Pinyin));
    }

    [Fact]
    public void Waterfall_scrolls_by_pixels_not_pages()
    {
        Assert.Equal(5, FallFlow.Columns);
        Assert.Equal(4, FallFlow.RowCount(20));
        Assert.Equal(80, FallFlow.Shift(50, 30, contentHeight: 400, viewportHeight: 200));
        Assert.Equal(200, FallFlow.Clamp(999, contentHeight: 400, viewportHeight: 200));
        Assert.Equal(0, FallFlow.Shift(10, -40, contentHeight: 400, viewportHeight: 200));
        Assert.True(FallFlow.Fits(180, 200));
        Assert.False(FallFlow.Fits(400, 200));
        Assert.Equal(0, FallFlow.Clamp(80, contentHeight: 180, viewportHeight: 200));
        Assert.Equal(48, FallFlow.Wheel(-120, 48));
        Assert.Equal(36, FallFlow.Shift(12, 24, contentHeight: 240, viewportHeight: 80));
    }

    [Fact]
    public void Waterfall_fling_goes_farther_when_faster_and_eases_out()
    {
        Assert.Equal(0, FallInertia.FlingDistance(100));
        var slow = FallInertia.FlingDistance(600);
        var fast = FallInertia.FlingDistance(2400);
        Assert.True(slow > 0);
        Assert.True(fast > slow * 2);
        Assert.True(FallInertia.FlingDuration(2400) > FallInertia.FlingDuration(600));
        Assert.Equal(10, FallInertia.FlingOffset(10, 80, 0), 3);
        Assert.Equal(90, FallInertia.FlingOffset(10, 80, 1), 3);
        Assert.True(FallInertia.FlingOffset(0, 100, 0.5) > 55);
        Assert.True(FallInertia.Rubber(40, 200) < 40);
        Assert.True(FallInertia.Rubber(800, 200) < 200);
        var (scroll, rubber) = FallInertia.Project(-30, 400, 200);
        Assert.Equal(0, scroll);
        Assert.True(rubber > 0);
        var samples = new (double Time, double Position)[] { (0, 0), (0.05, 40) };
        Assert.InRange(FallInertia.Velocity(samples), 700, 900);
        Assert.True(FallInertia.BlendVelocity(1000, 1000) > 1000);
        var spring = FallRun.Release(-24, 0, 400, 200, 0);
        Assert.NotNull(spring);
        Assert.Equal(FallRunKind.Spring, spring!.Kind);
        Assert.Equal(0, spring.SpringTarget);
        Assert.True(FallInertia.SpringSettled(-24, 0, 0, 1));
        Assert.Equal(0, FallInertia.Tick(0, 10));
        Assert.Equal(FallInertia.MaxTick, FallInertia.Tick(1, 2));
        Assert.InRange(FallInertia.Tick(1, 1.02), 0.019, 0.021);
        Assert.Equal(FallInertia.MinFlingVelocity, FallInertia.EnsureFling(180, 20));
        Assert.True(FallInertia.EnsureFling(600, 40) >= 600);
        var coast = FallRun.Release(0, 100, 400, 200, 0);
        Assert.NotNull(coast);
        Assert.Equal(FallRunKind.Fling, coast!.Kind);
        Assert.True(Math.Abs(coast.Distance) > 0);
        Assert.Null(FallRun.Release(0, 10, 400, 200, 0));
        Assert.True(CandidateFallPolicy.ShowsPinyinRail(true, true));
        Assert.False(CandidateFallPolicy.ShowsPinyinRail(false, true));
        Assert.True(CandidateFallPolicy.ShowsPinyinRail(true, CandidateFallPolicy.ComposingChinese(false, true, false)));
        Assert.True(CandidateFallPolicy.CanExpand(pinyinBoard: true, fullBoard: false, latin: false));
        Assert.True(CandidateFallPolicy.CanExpand(pinyinBoard: false, fullBoard: true, latin: false));
        Assert.True(CandidateFallPolicy.CanExpand(false, false, false, pinyin26: true));
        Assert.True(CandidateFallPolicy.CanExpand(pinyinBoard: false, fullBoard: true, latin: true));
        Assert.True(CandidateFallPolicy.CanExpand(false, false, false, englishBoard: true));
        Assert.True(CandidateFallPolicy.CanExpand(KeyboardSurface.English));
        Assert.True(CandidateFallPolicy.CanExpand(KeyboardSurface.Full));
        Assert.False(CandidateFallPolicy.CanExpand(pinyinBoard: false, fullBoard: false, latin: false));
        Assert.False(CandidateFallPolicy.CanExpand(KeyboardSurface.Number));
        Assert.True(CandidateFallPolicy.UsesLatinMarks(englishBoard: true, latin: false));
        Assert.True(CandidateFallPolicy.UsesLatinMarks(englishBoard: false, latin: true));
        Assert.False(CandidateFallPolicy.UsesLatinMarks(englishBoard: false, latin: false));
        Assert.Equal("ni", CandidateFallPolicy.ToggleSyllable(null, "ni"));
        Assert.Null(CandidateFallPolicy.ToggleSyllable("ni", "ni"));
        Assert.Equal("mi", CandidateFallPolicy.ToggleSyllable("ni", "mi"));
        Assert.True(CandidateFallPolicy.RebuildHomeAfterCommit(true));
        Assert.False(CandidateFallPolicy.RebuildHomeAfterCommit(false));
        Assert.Equal(16, CandidateBarSlots.QueryTake(expanded: false));
        Assert.Equal(120, CandidateBarSlots.QueryTake(expanded: true));
        Assert.Equal(10, CandidateBarSlots.PaintCount(80, expanded: false));
        Assert.Equal(80, CandidateBarSlots.PaintCount(80, expanded: true));
        Assert.True(CandidateBarSlots.ShowsMore(11));
        Assert.False(CandidateBarSlots.ShowsMore(10));
        Assert.True(KeyFeedbackPolicy.InstantPress);
        Assert.True(KeyFeedbackPolicy.ComposeBeforeCandidates);
        Assert.False(KeyFeedbackPolicy.CanRebuildFaces(hostPressed: true, localPressed: false));
        Assert.False(KeyFeedbackPolicy.CanRebuildFaces(hostPressed: false, localPressed: true));
        Assert.True(KeyFeedbackPolicy.CanRebuildFaces(hostPressed: false, localPressed: false));
    }

    [Fact]
    public void Waterfall_does_not_follow_a_hovering_mouse()
    {
        Assert.False(FallDragPolicy.Follows(false));
        Assert.True(FallDragPolicy.Follows(true));
        Assert.True(FallDragPolicy.ShouldDrop(pressed: false, tracking: true));
        Assert.False(FallDragPolicy.ShouldDrop(pressed: true, tracking: true));
        Assert.False(FallDragPolicy.ShouldDrop(pressed: false, tracking: false));
        Assert.True(FallDragPolicy.FlingAfterDrop(wasDragging: true, inertiaRunning: false));
        Assert.False(FallDragPolicy.FlingAfterDrop(wasDragging: true, inertiaRunning: true));
        Assert.False(FallDragPolicy.FlingAfterDrop(wasDragging: false, inertiaRunning: false));
        Assert.True(FallDragPolicy.IgnorePromotedTouch(true));
        Assert.False(FallDragPolicy.IgnorePromotedTouch(false));
    }

    [Fact]
    public void Bottom_row_has_a_windows_key_action()
    {
        Assert.Equal(
            1,
            FullKeyboardLayout.Rows(latin: true, fn: false)[^1]
                .Count(key => key.Action == FullKeyAction.Win));
    }

    [Fact]
    public void Fn_only_replaces_the_number_row_with_function_keys()
    {
        var normal = FullKeyboardLayout.Rows(latin: true, fn: false)[0]
            .Select(key => key.Label)
            .ToArray();
        var fn = FullKeyboardLayout.Rows(latin: true, fn: true)[0]
            .Select(key => key.Label)
            .ToArray();
        Assert.Contains("1", normal);
        Assert.DoesNotContain("F1", normal);
        Assert.Equal("F1", fn[2]);
        Assert.Equal("F12", fn[^2]);
        Assert.Equal("⌫", fn[^1]);
        Assert.Equal(
            FullKeyboardLayout.Rows(latin: true, fn: false)[1].Select(key => key.Label),
            FullKeyboardLayout.Rows(latin: true, fn: true)[1].Select(key => key.Label));
    }

    [Fact]
    public void Shift_swaps_the_primary_face_on_number_keys()
    {
        var one = new FullKeySpec("1", 1, FullKeyAction.Text, "!", "1");
        Assert.Equal("1", FullKeyboardLayout.Face(one, shift: false, caps: false).Primary);
        Assert.Equal("!", FullKeyboardLayout.Face(one, shift: true, caps: false).Primary);
        Assert.Equal("Q", FullKeyboardLayout.Face(
            new FullKeySpec("q", 1, FullKeyAction.Letter, Payload: "q"),
            shift: true,
            caps: false).Primary);
    }

    [Fact]
    public void Language_key_shows_simplified_or_english()
    {
        var cn = FullKeyboardLayout.Rows(latin: false)[^1].Single(key => key.Action == FullKeyAction.Lang);
        var en = FullKeyboardLayout.Rows(latin: true)[^1];
        Assert.Equal("简体", cn.Label);
        Assert.Equal("EN", en.Single(key => key.Action == FullKeyAction.Lang).Label);
        Assert.Equal(EnglishPredictPolicy.Label, en[^1].Label);
        Assert.Equal(FullKeyAction.Predict, en[^1].Action);
    }

    [Theory]
    [InlineData("ni hao", "nihao")]
    [InlineData("lü se", "lvse")]
    [InlineData("ZHONG'GUO", "zhongguo")]
    public void Compact_letters_drop_separators(string pinyin, string expected)
    {
        Assert.Equal(expected, T9Engine.CompactLetters(pinyin));
    }

    [Theory]
    [InlineData("ni hao", "nh")]
    [InlineData("zhong'guo", "zg")]
    public void Initials_take_the_first_letter_of_each_syllable(string pinyin, string expected)
    {
        Assert.Equal(expected, T9Engine.SyllableInitials(pinyin));
    }

    [Theory]
    [InlineData("ni hao", "nihao", "全拼")]
    [InlineData("ni hao", "ni", "全拼前缀")]
    [InlineData("ni", "nihao", "组词")]
    [InlineData("ni hao", "nh", "简拼")]
    [InlineData("ni hao", "wo", null)]
    public void Letter_match_prefers_full_pinyin_over_initials(string pinyin, string typed, string? kind)
    {
        Assert.Equal(kind, T9Engine.ClassifyLetterMatch(pinyin, typed));
    }

    [Fact]
    public void T9_ranks_full_pinyin_prefix_above_four_char_jianpin()
    {
        Assert.True(T9MatchRank.Kind("短语") > T9MatchRank.Kind("全拼"));
        Assert.True(T9MatchRank.Kind("短语前缀") > T9MatchRank.Kind("全拼"));
        Assert.True(T9MatchRank.Kind("全拼前缀") > T9MatchRank.Kind("简拼"));
        Assert.Equal("组词", T9DigitMatch.Classify("546", "j", "54685426"));
        Assert.Equal("全拼前缀", T9DigitMatch.Classify("54678", "jr", "5467"));
        Assert.True(T9DigitMatch.CanLeadPhrase("组词", 2, 5));
        Assert.False(T9DigitMatch.CanLeadPhrase("全拼前缀", 2, 5));
        Assert.Equal(2, T9MatchRank.Leftover("组词", 5, 7));
        Assert.Equal(1, T9MatchRank.Leftover("全拼前缀", 5, 4));
        var hits = new T9Candidate[]
        {
            new()
            {
                Word = "客观描述",
                Pinyin = "ke guan miao shu",
                Frequency = 81,
                MatchKind = "简拼"
            },
            new()
            {
                Word = "进入",
                Pinyin = "jin ru",
                Frequency = 835,
                MatchKind = "全拼前缀"
            },
            new()
            {
                Word = "今日",
                Pinyin = "jin ri",
                Frequency = 763,
                MatchKind = "全拼前缀"
            }
        };

        var top = T9MatchRank.Order(hits, typedLength: 4, take: 8);
        Assert.Equal("进入", top[0].Word);
        Assert.DoesNotContain(top.Take(2), hit => hit.Word == "客观描述");

        var longHits = new T9Candidate[]
        {
            new()
            {
                Word = "进",
                Pinyin = "jin",
                Frequency = 900,
                MatchKind = "组词"
            },
            new()
            {
                Word = "今天好",
                Pinyin = "jin tian hao",
                Frequency = 80,
                MatchKind = "组句"
            }
        };
        Assert.Equal("今天好", T9MatchRank.Order(longHits, typedLength: 9, take: 4)[0].Word);
    }

    [Fact]
    public void Latin_shortcuts_and_english_words_come_from_letter_query()
    {
        Assert.Equal("短语", T9Latin.Kind("http://", "h", "h"));
        Assert.Equal("短语", T9Latin.Kind("https://", "h", "h"));
        Assert.Equal("短语前缀", T9Latin.Kind("www.", "www", "w"));
        Assert.Equal("全拼前缀", T9Latin.Kind("hello", "hello", "hel"));
        Assert.True(T9Latin.IsLatinWord("hello"));
        Assert.True(T9Latin.IsLatinWord("http://"));
        Assert.False(T9Latin.IsLatinWord("你好"));
        Assert.True(EnglishPredictPolicy.Composes(true, true, false));
        Assert.False(EnglishPredictPolicy.Composes(false, true, false));
        Assert.True(EnglishPredictPolicy.ShowsUnderline(true));
        Assert.False(EnglishPredictPolicy.ShowsAccent(false));
        Assert.Equal(
            "hello",
            EnterCommitPolicy.LatinText(
                true,
                "hel",
                "hel",
                [
                    new T9Candidate
                    {
                        Word = "和了",
                        Pinyin = "he le",
                        Frequency = 800,
                        MatchKind = "全拼前缀"
                    },
                    new T9Candidate
                    {
                        Word = "hello",
                        Pinyin = "hello",
                        Frequency = 1000,
                        MatchKind = "全拼前缀"
                    }
                ]));
        Assert.Equal(
            "nihao",
            EnterCommitPolicy.LatinText(true, "nihao", "ni'hao", []));
        Assert.Null(EnterCommitPolicy.LatinText(false, "hel", "hel", []));
        Assert.True(T9Engine.IsLatinDict(@"D:\T9\src\T9Pane\Data\xiaobai-t9\word.dict.yaml"));
        Assert.True(T9Engine.IsLatinDict(@"D:\T9\src\T9Pane\Data\xiaobai-t9\en.dict.yaml"));
        Assert.True(T9Engine.IsLatinDict(@"D:\T9\src\T9Pane\Data\xiaobai-t9\en_ext.dict.yaml"));
        Assert.True(T9Engine.IsLatinDict(@"D:\T9\src\T9Pane\Data\xiaobai-t9\shortcuts.dict.yaml"));
        Assert.False(T9Engine.IsLatinDict(@"D:\T9\src\T9Pane\Data\xiaobai-t9\jichu.dict.yaml"));

        var engine = new T9Engine();
        engine.Load(new ImeCatalog());
        var letters = engine.QueryLetters("h", 20);
        Assert.Contains(letters, hit => hit.Word == "http://");
        Assert.Contains(letters, hit => hit.Word == "https://");
        Assert.Equal("http://", letters[0].Word);
        Assert.DoesNotContain(engine.Query("4", 40), hit => hit.Word.Contains("http", StringComparison.Ordinal));

        var hello = engine.QueryLetters("hel", 40);
        Assert.Contains(hello, hit => hit.Word.Equals("hello", StringComparison.OrdinalIgnoreCase));

        var dee = engine.QueryLatin("d", 40);
        Assert.True(dee.Count >= 20);
        Assert.Contains(dee, hit => hit.Word.Equals("do", StringComparison.OrdinalIgnoreCase));
        var da = engine.QueryLatin("da", 40);
        Assert.Contains(da, hit => hit.Word.Equals("day", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(da, hit => hit.Word.Equals("data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Full_board_height_is_exactly_five_square_rows()
    {
        var size = KeyboardChromeSize.ForBoard(true);
        var expected = KeyboardChromeSize.FittedHeight(
            KeyboardChromeSize.FullWidth,
            FullKeyboardLayout.Units,
            FullKeyboardLayout.RowCount,
            functionBar: false);
        Assert.Equal(expected, size.Height, 3);
    }

    [Theory]
    [InlineData(0, false, 1, false)]
    [InlineData(1, false, 0, false)]
    [InlineData(2, false, 0, false)]
    [InlineData(1, true, 0, true)]
    public void Touch_modifiers_latch_on_first_tap_and_release_on_second(
        int current,
        bool windowsKey,
        int next,
        bool firesWin)
    {
        var now = (TouchModifierPhase)current;
        Assert.Equal((TouchModifierPhase)next, TouchModifierPolicy.Tap(now, windowsKey));
        Assert.Equal(firesWin, TouchModifierPolicy.SecondTapFiresKey(now, windowsKey));
        Assert.Equal(
            now == TouchModifierPhase.Held ? TouchModifierPhase.Off : now,
            TouchModifierPolicy.Consume(now));
    }

    [Fact]
    public void SendInput_struct_is_forty_bytes_on_x64()
    {
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(40, Marshal.SizeOf<INPUT>());
    }

    [Fact]
    public void Alt_tab_keeps_alt_held_for_the_switcher()
    {
        Assert.True(ShellCommands.KeepAltForSwitcher(0x09));
        Assert.True(ShellCommands.KeepAltForSwitcher(0x25));
        Assert.False(ShellCommands.KeepAltForSwitcher(0x43));
    }

    [Fact]
    public void Hide_releases_fn_and_modifiers_but_keeps_caps()
    {
        var dismissed = HeldSurfacePolicy.Dismiss(new HeldSurfaceSnapshot(
            TouchModifierPhase.Held,
            TouchModifierPhase.Held,
            TouchModifierPhase.Held,
            TouchModifierPhase.Held,
            Fn: true,
            Caps: true));
        Assert.Equal(TouchModifierPhase.Off, dismissed.Shift);
        Assert.Equal(TouchModifierPhase.Off, dismissed.Ctrl);
        Assert.Equal(TouchModifierPhase.Off, dismissed.Alt);
        Assert.Equal(TouchModifierPhase.Off, dismissed.Win);
        Assert.False(dismissed.Fn);
        Assert.True(dismissed.Caps);
        Assert.Contains(
            "1",
            FullKeyboardLayout.Rows(latin: true, fn: dismissed.Fn)[0].Select(key => key.Label));
        Assert.DoesNotContain(
            "F1",
            FullKeyboardLayout.Rows(latin: true, fn: dismissed.Fn)[0].Select(key => key.Label));
    }

    [Fact]
    public void Hosted_hide_must_publish_even_when_the_wpf_window_is_hidden()
    {
        Assert.False(HeldSurfacePolicy.MustPublishHostBeforeHide(true));
        Assert.False(HeldSurfacePolicy.MustPublishHostBeforeHide(false));
        Assert.Equal(KeyboardSkinPolicy.Compact, KeyboardSkinPolicy.Key(false, false));
        Assert.Equal(KeyboardSkinPolicy.English, KeyboardSkinPolicy.Key(false, true));
        Assert.Equal(KeyboardSkinPolicy.Full, KeyboardSkinPolicy.Key(true, false));
        Assert.Equal(0.25, KeyboardSkinPolicy.ClampOverlay(0));
        Assert.Equal(1, KeyboardSkinPolicy.ClampOverlay(2));
        Assert.Equal(0.05, KeyboardSkinPolicy.ClampImage(0));
        Assert.True(TrayFocusPolicy.IgnoreOwnProcess(12, 12));
        Assert.False(TrayFocusPolicy.IgnoreOwnProcess(12, 34));
        Assert.True(TrayMenuPolicy.ShouldDismissOnDeactivate(pointerOverMenu: false));
        Assert.False(TrayMenuPolicy.ShouldDismissOnDeactivate(pointerOverMenu: true));
        var placed = TrayMenuPolicy.Place(100, 800, 180, 260, 0, 0, 1920, 1040);
        Assert.Equal(100, placed.Left);
        Assert.Equal(540, placed.Top);
    }

    [Fact]
    public void Traditional_layout_never_enters_shift_lock()
    {
        var afterFirst = TouchModifierPolicy.Tap(TouchModifierPhase.Off, windowsKey: false);
        var afterSecond = TouchModifierPolicy.Tap(afterFirst, windowsKey: false);
        var afterRapid = TouchModifierPolicy.Tap(afterSecond, windowsKey: false);
        Assert.Equal(TouchModifierPhase.Held, afterFirst);
        Assert.Equal(TouchModifierPhase.Off, afterSecond);
        Assert.Equal(TouchModifierPhase.Held, afterRapid);
        Assert.NotEqual(TouchModifierPhase.Locked, afterFirst);
        Assert.NotEqual(TouchModifierPhase.Locked, afterSecond);
        Assert.True(TouchModifierPolicy.SecondTapFiresKey(TouchModifierPhase.Held, windowsKey: true));
        Assert.False(TouchModifierPolicy.SecondTapFiresKey(TouchModifierPhase.Held, windowsKey: false));
    }

    [Fact]
    public void Nine_key_chrome_is_narrower_than_the_full_board()
    {
        var compact = KeyboardChromeSize.ForBoard(false);
        var english = KeyboardChromeSize.ForBoard(false, english: true);
        var full = KeyboardChromeSize.ForBoard(true);
        Assert.True(compact.Width < english.Width);
        Assert.True(english.Width < full.Width);
        Assert.True(compact.Width < full.Width);
        Assert.Equal(KeyboardChromeSize.CompactWidth, compact.Width);
        Assert.Equal(KeyboardChromeSize.EnglishWidth, english.Width);
        Assert.Equal(KeyboardChromeSize.EnglishHeight, english.Height);
        Assert.True(english.Height < KeyboardChromeSize.CompactHeight);
        Assert.Equal(KeyboardChromeSize.FullWidth, full.Width);
        var number = KeyboardChromeSize.ForBoard(false, numberPad: true);
        Assert.Equal(KeyboardChromeSize.CompactWidth, number.Width);
        Assert.Equal(KeyboardChromeSize.NumberWidth, number.Width);
        Assert.Equal(
            KeyboardChromeSize.FramePad
            + KeyboardChromeSize.Title
            + KeyboardChromeSize.Candidate
            + KeyboardChromeSize.Function
            + KeyboardChromeSize.CompactUnit * KeyboardChromeSize.NumberRows,
            number.Height,
            3);
        Assert.True(number.Height > compact.Height);
        Assert.True(
            number.Height
            < KeyboardChromeSize.FittedHeight(
                KeyboardChromeSize.CompactWidth,
                KeyboardChromeSize.NumberColumns,
                KeyboardChromeSize.NumberRows,
                functionBar: true));
    }

    [Fact]
    public void T9_faces_share_one_size_that_fits_the_longest_label()
    {
        Assert.Contains("WXYZ", T9KeyFace.Labels);
        Assert.Contains("PQRS", T9KeyFace.Labels);
        Assert.Equal(T9KeyFace.FaceSizeFor("ABC"), T9KeyFace.FaceSizeFor("WXYZ"));
        Assert.Equal(T9KeyFace.FaceSizeFor("分词"), T9KeyFace.FaceSizeFor("MNO"));
        Assert.Equal(4, T9KeyFace.Labels.Max(label => label.Length));
    }

    [Fact]
    public void Number_pad_keeps_the_unscaled_square_unit()
    {
        Assert.Equal(12, NumberPadLayout.Keys.Length);
        Assert.Equal("X", NumberPadLayout.Keys[9]);
        Assert.True(TitleBarLayout.CloseReserved(KeyboardChromeSize.NumberWidth, 80));
        Assert.Equal(36, TitleBarLayout.CloseWidth);
        Assert.True(ToolBarPolicy.BackspaceInsteadOfEnter(true, false));
        Assert.True(ToolBarPolicy.BackspaceInsteadOfEnter(false, true));
        Assert.False(ToolBarPolicy.BackspaceInsteadOfEnter(false, false));
        Assert.True(ToolBarPolicy.BackspaceInsteadOfEnter(false, false, candidatesExpanded: true));
        Assert.Equal(KeyboardChromeSize.CompactBoard / 3, KeyboardChromeSize.CompactUnit, 3);
        Assert.Equal(
            KeyboardChromeSize.T9Board,
            KeyboardChromeSize.CompactColumns().Board,
            3);
        Assert.True(KeyboardChromeSize.T9Board > KeyboardChromeSize.CompactBoard);
    }

    [Fact]
    public void Compact_columns_hug_the_square_pad_without_a_gap()
    {
        var (rail, board) = KeyboardChromeSize.CompactColumns();
        Assert.True(board > 0);
        Assert.Equal(
            KeyboardChromeSize.CompactWidth,
            KeyboardChromeSize.FramePad + rail + board + rail,
            3);
    }

    [Fact]
    public void Function_and_letter_keys_map_to_virtual_keys()
    {
        Assert.Equal((ushort)0x74, FullKeyVirtuals.NamedVk("F5"));
        Assert.Equal((ushort)'C', FullKeyVirtuals.LetterVk("c"));
        Assert.Equal(
            new ushort[] { 0x11, 0x10 },
            FullKeyVirtuals.StickyModifiers(ctrl: true, alt: false, shift: true, win: false));
    }

    [Fact]
    public void Shifted_dual_key_emits_the_upper_symbol()
    {
        var key = new FullKeySpec("1", 1, FullKeyAction.Text, "!", "1");
        Assert.Equal("1", key.Emit(false));
        Assert.Equal("!", key.Emit(true));
    }
}
