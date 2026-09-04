namespace T9Pane.Services;

/// <summary>
/// 搜索框走官方 ITfFnSearchCandidateProvider。
/// 组词包只含拼音；候选另走 KindSearchCandidates，不能塞进上屏串，
/// 否则普通输入框会把分隔符和汉字画成乱码。
/// </summary>
internal static class SearchCandidatePayload
{
    public const char Separator = '\u001e';
    public const int MaxWords = 12;

    public static string Encode(string preview, IEnumerable<string> words)
    {
        var text = preview ?? "";
        if (words is null)
        {
            return text;
        }

        var packed = new System.Text.StringBuilder(text);
        var count = 0;
        foreach (var word in words)
        {
            if (count >= MaxWords || string.IsNullOrEmpty(word))
            {
                continue;
            }

            packed.Append(Separator).Append(word);
            count++;
        }

        return packed.ToString();
    }

    public static string ComposeText(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return "";
        }

        var split = payload.IndexOf(Separator);
        return split < 0 ? payload : payload[..split];
    }
}
