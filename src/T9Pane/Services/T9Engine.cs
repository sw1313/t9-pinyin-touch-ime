using System.IO;
using System.Text;

namespace T9Pane.Services;

internal sealed class T9Candidate
{
    public required string Word { get; init; }
    public required string Pinyin { get; init; }
    public required int Frequency { get; init; }
    public required string MatchKind { get; init; }
}

internal sealed class T9Entry
{
    public required string Word { get; init; }
    public required string Pinyin { get; init; }
    public required string FullDigits { get; init; }
    public required string InitialDigits { get; init; }
    public required int Frequency { get; init; }
}

internal sealed class T9Engine
{
    private const int PrefixWordsPerSyllable = 80;
    private const int JianpinLimit = 150;
    private const int ComposeTailLimit = 8;

    private readonly List<T9Entry> _entries = [];
    private readonly T9SyllableTable _syllables = new();
    private readonly Dictionary<string, List<int>> _exactFull = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _byFirstSyllable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _initialPrefix = new(StringComparer.Ordinal);
    private readonly List<T9Entry> _latinEntries = [];
    private readonly Dictionary<string, List<int>> _latinPrefixIndex = new(StringComparer.Ordinal);
    private string _rankedKey = "";
    private IReadOnlyList<T9Candidate> _ranked = [];
    private static readonly Dictionary<char, char> LetterMap = new()
    {
        ['a'] = '2', ['b'] = '2', ['c'] = '2',
        ['d'] = '3', ['e'] = '3', ['f'] = '3',
        ['g'] = '4', ['h'] = '4', ['i'] = '4',
        ['j'] = '5', ['k'] = '5', ['l'] = '5',
        ['m'] = '6', ['n'] = '6', ['o'] = '6',
        ['p'] = '7', ['q'] = '7', ['r'] = '7', ['s'] = '7',
        ['t'] = '8', ['u'] = '8', ['v'] = '8',
        ['w'] = '9', ['x'] = '9', ['y'] = '9', ['z'] = '9'
    };

    public int Count => _entries.Count;
    public string SourceDescription { get; private set; } = "未加载词库";

    public void Load(ImeCatalog catalog, IEnumerable<string>? extraRoots = null)
    {
        _entries.Clear();
        _syllables.Clear();
        _exactFull.Clear();
        _byFirstSyllable.Clear();
        _initialPrefix.Clear();
        _latinEntries.Clear();
        _latinPrefixIndex.Clear();
        _rankedKey = "";
        _ranked = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var loadedFiles = new List<string>();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateBundledXiaobaiDicts().Concat(EnumerateBundledLatinDicts()))
        {
            Enqueue(queue, visited, file);
        }

        foreach (var dir in catalog.LexiconDirectories.Concat(extraRoots ?? []))
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in EnumeratePreferredDicts(dir))
            {
                Enqueue(queue, visited, file);
            }
        }

        if (File.Exists(AppSettings.UserLexiconPath))
        {
            Enqueue(queue, visited, AppSettings.UserLexiconPath);
        }

        while (queue.Count > 0 && _entries.Count < 2_000_000)
        {
            var path = queue.Dequeue();
            var latin = IsLatinDict(path);
            foreach (var imported in LoadRimeOrPlainFile(path, seen, latin ? _latinEntries : null))
            {
                var resolved = ResolveImport(path, imported);
                if (resolved is not null)
                {
                    Enqueue(queue, visited, resolved);
                }
            }

            loadedFiles.Add(path);
        }

        BuildIndex();
        BuildLatinIndex();
        SourceDescription = loadedFiles.Count == 0
            ? "未找到小白T9 词库。请确认 Data\\xiaobai-t9 已随程序复制。"
            : $"已加载小白T9 开源词库 {loadedFiles.Count} 个文件，{_entries.Count} 条，合法音节 {_syllables.Count} 个，英文 {_latinEntries.Count} 条";
        Log.Info(SourceDescription);
    }

    public IReadOnlyList<T9Candidate> Query(string digits, int take = 40)
    {
        if (string.IsNullOrEmpty(digits) || digits.Any(ch => ch is < '2' or > '9'))
        {
            return [];
        }

        return Ranked($"9\n{digits}", digits.Length, take, () => CollectDigits(digits));
    }

    public IReadOnlyList<string> QuerySyllables(string digits, IReadOnlyList<string>? confirmed = null)
    {
        return _syllables.MatchCurrent(T9SyllableTable.RemainingDigits(digits, confirmed));
    }

    public IReadOnlyList<string> QueryLetterSyllables(string letters, IReadOnlyList<string>? confirmed = null)
    {
        return _syllables.MatchCurrentLetters(T9SyllableTable.RemainingLetters(letters, confirmed));
    }

    private List<T9Candidate> CollectDigits(string digits)
    {
        var hits = new List<T9Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var heads = new List<T9Entry>();

        void Add(T9Entry entry, string kind)
        {
            if (!seen.Add(entry.Word))
            {
                return;
            }

            hits.Add(new T9Candidate
            {
                Word = entry.Word,
                Pinyin = entry.Pinyin,
                Frequency = entry.Frequency,
                MatchKind = kind
            });
            if (T9DigitMatch.CanLeadPhrase(kind, entry.Word.Length, entry.FullDigits.Length))
            {
                heads.Add(entry);
            }
        }

        if (_exactFull.TryGetValue(digits, out var exact))
        {
            foreach (var index in exact)
            {
                Add(_entries[index], "全拼");
            }
        }

        for (var length = 2; length < digits.Length; length++)
        {
            if (!_exactFull.TryGetValue(digits[..length], out var prefixes))
            {
                continue;
            }

            foreach (var index in prefixes)
            {
                Add(_entries[index], "组词");
            }
        }

        foreach (var syllable in _syllables.MatchForWords(digits))
        {
            if (!_byFirstSyllable.TryGetValue(syllable, out var bucket))
            {
                continue;
            }

            var taken = 0;
            foreach (var index in bucket)
            {
                var entry = _entries[index];
                if (entry.FullDigits.Length <= digits.Length
                    || !entry.FullDigits.StartsWith(digits, StringComparison.Ordinal))
                {
                    continue;
                }

                Add(entry, "全拼前缀");
                if (++taken >= PrefixWordsPerSyllable)
                {
                    break;
                }
            }
        }

        var initialKey = digits.Length >= 2 ? digits[..2] : digits;
        if (_initialPrefix.TryGetValue(initialKey, out var initials))
        {
            var taken = 0;
            foreach (var index in initials)
            {
                var entry = _entries[index];
                var kind = T9DigitMatch.Classify(entry.FullDigits, entry.InitialDigits, digits);
                if (kind is not ("简拼" or "简拼前缀" or "简拼组词"))
                {
                    continue;
                }

                Add(entry, kind);
                if (++taken >= JianpinLimit)
                {
                    break;
                }
            }
        }

        foreach (var composed in ComposePhrases(digits, heads))
        {
            if (seen.Add(composed.Word))
            {
                hits.Add(composed);
            }
        }

        return hits;
    }

    /// <summary>
    /// 一次按键会连着查三遍同一串码：主候选一次，拼音预览栏两次。
    ///
    /// Order 只是全排序后截断，所以小 take 的结果就是大 take 结果的前缀。
    /// 这里按码记住一份完整排序结果，后两次直接切片，省掉两次全量扫描——
    /// 词库大或索引未命中时那两次各自都要扫一遍 _entries。
    /// </summary>
    private IReadOnlyList<T9Candidate> Ranked(
        string key,
        int typedLength,
        int take,
        Func<List<T9Candidate>> collect)
    {
        if (!string.Equals(_rankedKey, key, StringComparison.Ordinal))
        {
            _ranked = T9MatchRank.Order(collect(), typedLength, int.MaxValue);
            _rankedKey = key;
        }

        if (take >= _ranked.Count)
        {
            return _ranked;
        }

        var slice = new T9Candidate[Math.Max(0, take)];
        for (var i = 0; i < slice.Length; i++)
        {
            slice[i] = _ranked[i];
        }

        return slice;
    }

    public string PinyinPreview(string digits)
    {
        if (string.IsNullOrEmpty(digits))
        {
            return "";
        }

        string? pinyin = null;
        foreach (var hit in Query(digits, 8))
        {
            if (string.IsNullOrWhiteSpace(hit.Pinyin))
            {
                continue;
            }

            pinyin ??= hit.Pinyin;
            if (CompactLetters(hit.Pinyin).Length >= digits.Length)
            {
                pinyin = hit.Pinyin;
                break;
            }
        }

        return PinyinPreviewPolicy.FromTypedDigits(digits, pinyin);
    }

    public static string LettersForKey(char digit) => digit switch
    {
        '2' => "abc",
        '3' => "def",
        '4' => "ghi",
        '5' => "jkl",
        '6' => "mno",
        '7' => "pqrs",
        '8' => "tuv",
        '9' => "wxyz",
        _ => ""
    };

    public static string ToDigits(string pinyin)
    {
        var sb = new StringBuilder(pinyin.Length);
        foreach (var raw in pinyin)
        {
            var ch = FoldPinyin(raw);
            if (LetterMap.TryGetValue(ch, out var digit))
            {
                sb.Append(digit);
            }
            else if (ch is >= '2' and <= '9')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public IReadOnlyList<T9Candidate> QueryLetters(string letters, int take = 40)
    {
        var typed = CompactLetters(letters);
        if (typed.Length == 0)
        {
            return [];
        }

        var digits = ToDigits(typed);
        if (digits.Length == 0)
        {
            return [];
        }

        return Ranked($"L\n{typed}", digits.Length, take, () => CollectLetters(typed, digits));
    }

    private List<T9Candidate> CollectLetters(string typed, string _)
    {
        var hits = new List<T9Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var syllable in _syllables.MatchLettersForWords(typed))
        {
            if (!_byFirstSyllable.TryGetValue(syllable, out var bucket))
            {
                continue;
            }

            var taken = 0;
            foreach (var index in bucket)
            {
                var entry = _entries[index];
                var kind = ClassifyLetterMatch(entry.Pinyin, typed);
                if (kind is null || !seen.Add(entry.Word))
                {
                    continue;
                }

                hits.Add(new T9Candidate
                {
                    Word = entry.Word,
                    Pinyin = entry.Pinyin,
                    Frequency = entry.Frequency,
                    MatchKind = kind
                });
                if (++taken >= PrefixWordsPerSyllable)
                {
                    break;
                }
            }
        }

        foreach (var index in LatinIndexes(typed))
        {
            var entry = _latinEntries[index];
            var kind = T9Latin.Kind(entry.Word, entry.Pinyin, typed);
            if (kind is null || !seen.Add(entry.Word))
            {
                continue;
            }

            hits.Add(new T9Candidate
            {
                Word = entry.Word,
                Pinyin = entry.Pinyin,
                Frequency = entry.Frequency,
                MatchKind = kind
            });
        }

        return hits;
    }

    public IReadOnlyList<T9Candidate> QueryLatin(string letters, int take = 40)
    {
        var typed = CompactLetters(letters);
        if (typed.Length == 0)
        {
            return [];
        }

        return Ranked($"E\n{typed}", typed.Length, take, () => CollectLatin(typed));
    }

    private List<T9Candidate> CollectLatin(string typed)
    {
        var hits = new List<T9Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in LatinIndexes(typed))
        {
            var entry = _latinEntries[index];
            var kind = T9Latin.Kind(entry.Word, entry.Pinyin, typed);
            if (kind is null || !seen.Add(entry.Word))
            {
                continue;
            }

            hits.Add(new T9Candidate
            {
                Word = entry.Word,
                Pinyin = entry.Pinyin,
                Frequency = entry.Frequency,
                MatchKind = kind
            });
        }

        return hits;
    }

    public static string CompactLetters(string pinyin)
    {
        var sb = new StringBuilder(pinyin?.Length ?? 0);
        foreach (var raw in pinyin ?? "")
        {
            var ch = FoldPinyin(raw);
            if (ch is >= 'a' and <= 'z' or 'v')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static string SyllableInitials(string pinyin)
    {
        var sb = new StringBuilder();
        var atStart = true;
        foreach (var raw in pinyin ?? "")
        {
            if (raw is ' ' or '\'' or '’' or '-')
            {
                atStart = true;
                continue;
            }

            var ch = FoldPinyin(raw);
            if (ch is >= 'a' and <= 'z' or 'v')
            {
                if (atStart)
                {
                    sb.Append(ch);
                    atStart = false;
                }
            }
        }

        return sb.ToString();
    }

    public static string? ClassifyLetterMatch(string pinyin, string typed)
    {
        typed = CompactLetters(typed);
        if (typed.Length == 0)
        {
            return null;
        }

        var compact = CompactLetters(pinyin);
        if (compact.StartsWith(typed, StringComparison.Ordinal))
        {
            return compact.Length == typed.Length ? "全拼" : "全拼前缀";
        }

        if (typed.StartsWith(compact, StringComparison.Ordinal) && compact.Length >= 2)
        {
            return "组词";
        }

        var initials = SyllableInitials(pinyin);
        if (initials.StartsWith(typed, StringComparison.Ordinal))
        {
            return initials.Length == typed.Length ? "简拼" : "简拼前缀";
        }

        if (typed.StartsWith(initials, StringComparison.Ordinal) && initials.Length >= 2)
        {
            return "简拼组词";
        }

        return null;
    }

    public static IReadOnlyList<string> Syllables(string pinyin)
    {
        var parts = new List<string>();
        var syllable = new StringBuilder();
        void Flush()
        {
            if (syllable.Length > 0)
            {
                parts.Add(syllable.ToString());
                syllable.Clear();
            }
        }

        foreach (var raw in pinyin ?? "")
        {
            if (raw is ' ' or '\'' or '’' or '-')
            {
                Flush();
                continue;
            }

            var ch = FoldPinyin(raw);
            if (ch is >= 'a' and <= 'z' or 'v')
            {
                syllable.Append(ch);
            }
        }

        Flush();
        return parts;
    }

    public static string FirstSyllable(string pinyin)
    {
        var parts = Syllables(pinyin);
        return parts.Count == 0 ? "" : parts[0];
    }

    public static string SyllableAt(string pinyin, int index)
    {
        var parts = Syllables(pinyin);
        return index >= 0 && index < parts.Count ? parts[index] : "";
    }

    private IEnumerable<T9Candidate> ComposePhrases(string digits, List<T9Entry> heads)
    {
        foreach (var head in heads
            .OrderByDescending(entry => entry.FullDigits.Length)
            .ThenByDescending(entry => entry.Frequency)
            .Take(12))
        {
            if (head.FullDigits.Length >= digits.Length)
            {
                continue;
            }

            var rest = digits[head.FullDigits.Length..];
            if (rest.Length < 2)
            {
                continue;
            }

            var tails = new List<T9Entry>();
            var seenTails = new HashSet<string>(StringComparer.Ordinal);
            if (_exactFull.TryGetValue(rest, out var exactTails))
            {
                foreach (var index in exactTails)
                {
                    var tail = _entries[index];
                    if (seenTails.Add(tail.Word))
                    {
                        tails.Add(tail);
                    }
                }
            }

            foreach (var syllable in _syllables.MatchCurrent(rest))
            {
                if (!_byFirstSyllable.TryGetValue(syllable, out var bucket))
                {
                    continue;
                }

                var taken = 0;
                foreach (var index in bucket)
                {
                    var tail = _entries[index];
                    var kind = T9DigitMatch.Classify(tail.FullDigits, tail.InitialDigits, rest);
                    if (kind is not ("全拼" or "全拼前缀" or "组词") || !seenTails.Add(tail.Word))
                    {
                        continue;
                    }

                    tails.Add(tail);
                    if (++taken >= ComposeTailLimit)
                    {
                        break;
                    }
                }
            }

            foreach (var tail in tails
                .OrderByDescending(entry => Math.Min(entry.FullDigits.Length, rest.Length))
                .ThenByDescending(entry => entry.Frequency)
                .Take(4))
            {
                yield return new T9Candidate
                {
                    Word = head.Word + tail.Word,
                    Pinyin = head.Pinyin + " " + tail.Pinyin,
                    Frequency = head.Frequency + tail.Frequency,
                    MatchKind = "组句"
                };
            }
        }
    }

    private IEnumerable<int> LatinIndexes(string typed)
    {
        var key = typed.Length >= 2 ? typed[..2] : typed;
        return _latinPrefixIndex.TryGetValue(key, out var list) ? list : [];
    }

    private void BuildIndex()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            AddPosting(_exactFull, entry.FullDigits, i);
            AddPrefix(_initialPrefix, entry.InitialDigits, i);
            foreach (var syllable in Syllables(entry.Pinyin))
            {
                _syllables.Add(syllable);
            }

            var first = FirstSyllable(entry.Pinyin);
            if (first.Length > 0)
            {
                AddPosting(_byFirstSyllable, first, i);
            }
        }

        SortPostings(_exactFull);
        SortPostings(_byFirstSyllable);
        SortPostings(_initialPrefix);
    }

    private void BuildLatinIndex()
    {
        for (var i = 0; i < _latinEntries.Count; i++)
        {
            var key = CompactLetters(_latinEntries[i].Pinyin);
            if (key.Length == 0)
            {
                continue;
            }

            AddLatinIndex(key[..1], i);
            if (key.Length >= 2)
            {
                AddLatinIndex(key[..2], i);
            }
        }
    }

    private void AddLatinIndex(string key, int index)
    {
        if (!_latinPrefixIndex.TryGetValue(key, out var list))
        {
            list = [];
            _latinPrefixIndex[key] = list;
        }

        list.Add(index);
    }

    private static void AddPosting(Dictionary<string, List<int>> map, string key, int index)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(index);
    }

    private static void AddPrefix(Dictionary<string, List<int>> map, string digits, int index)
    {
        if (digits.Length == 0)
        {
            return;
        }

        AddPosting(map, digits.Length >= 2 ? digits[..2] : digits, index);
    }

    private void SortPostings(Dictionary<string, List<int>> map)
    {
        foreach (var list in map.Values)
        {
            list.Sort((left, right) => _entries[right].Frequency.CompareTo(_entries[left].Frequency));
        }
    }

    private IEnumerable<string> LoadRimeOrPlainFile(string path, HashSet<string> seen, List<T9Entry>? into = null)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        var imports = new List<string>();
        var inHeader = true;
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("...") || line.StartsWith("---"))
            {
                inHeader = line.StartsWith("---");
                continue;
            }

            if (inHeader && line.TrimStart().StartsWith("- "))
            {
                var imported = line.Trim()[2..].Trim().Trim('"', '\'');
                if (imported.Length > 0)
                {
                    imports.Add(imported);
                }

                continue;
            }

            if (line.Contains(':') && !line.Contains('\t'))
            {
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//"))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            var word = parts[0].Trim();
            var code = parts[1].Trim().ToLowerInvariant();
            if (word.Length == 0 || code.Length == 0 || word.StartsWith('#'))
            {
                continue;
            }

            var freq = 1000;
            if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out var parsed))
            {
                freq = parsed;
            }

            var key = word + "|" + code;
            if (!seen.Add(key))
            {
                continue;
            }

            var syllables = code.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = syllables.Length > 0 && syllables.All(s => s.Any(char.IsLetter))
                ? new string(syllables.Select(s => s[0]).ToArray())
                : "";

            (into ?? _entries).Add(new T9Entry
            {
                Word = word,
                Pinyin = code,
                FullDigits = ToDigits(code),
                InitialDigits = ToDigits(initials),
                Frequency = freq
            });
        }

        foreach (var imported in imports)
        {
            yield return imported;
        }
    }

    internal static bool IsLatinDict(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("word.dict.yaml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("shortcuts.dict.yaml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("en.dict.yaml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("en_ext.dict.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lower = name.ToLowerInvariant();
        return lower.Contains("english")
               || lower.Contains(".en.")
               || lower.Contains("shortcut");
    }

    private static IEnumerable<string> EnumerateBundledXiaobaiDicts()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "xiaobai-t9");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var name in new[]
                 {
                     "jichu.dict.yaml",
                     "zi.dict.yaml",
                     "pinyin_simp_8105.dict.yaml",
                     "duoyin.dict.yaml",
                     "punctuation.dict.yaml"
                 })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateBundledLatinDicts()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "xiaobai-t9");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var name in new[]
                 {
                     "en.dict.yaml",
                     "en_ext.dict.yaml",
                     "word.dict.yaml",
                     "shortcuts.dict.yaml"
                 })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static char FoldPinyin(char ch) => ch switch
    {
        'ā' or 'á' or 'ǎ' or 'à' => 'a',
        'ō' or 'ó' or 'ǒ' or 'ò' => 'o',
        'ē' or 'é' or 'ě' or 'è' or 'ê' => 'e',
        'ī' or 'í' or 'ǐ' or 'ì' => 'i',
        'ū' or 'ú' or 'ǔ' or 'ù' => 'u',
        'ü' or 'ǖ' or 'ǘ' or 'ǚ' or 'ǜ' or 'v' => 'v',
        _ => char.ToLowerInvariant(ch)
    };

    private static IEnumerable<string> EnumeratePreferredDicts(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*.dict.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(dir, "*.txt", SearchOption.TopDirectoryOnly))
            .ToList();

        return files.OrderBy(path =>
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (name.Contains("xiaobai"))
            {
                return 0;
            }

            if (name.Contains("pinyin") || name.Contains("luna") || name.Contains("ice") || name.Contains("base"))
            {
                return 1;
            }

            if (name.Contains("emoji") || name.Contains("english") || name.Contains(".en."))
            {
                return 8;
            }

            return 5;
        });
    }

    private static void Enqueue(Queue<string> queue, HashSet<string> visited, string path)
    {
        if (visited.Add(path) && File.Exists(path))
        {
            queue.Enqueue(path);
        }
    }

    private static string? ResolveImport(string fromFile, string imported)
    {
        var dir = Path.GetDirectoryName(fromFile) ?? "";
        var name = imported.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(dir, name + ".dict.yaml"),
            Path.Combine(dir, name),
            Path.Combine(dir, Path.GetFileName(name) + ".dict.yaml")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

}
