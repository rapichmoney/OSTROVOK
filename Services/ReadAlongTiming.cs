using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DesktopIsland.Services;

internal sealed record ReadAlongFrame(int Start, int Length, int ReadThrough, double Duration, bool FromSource)
{
    internal bool Active => Start >= 0 && Length > 0;
    internal double Position => Active ? Start + Math.Max(0, Length - .001) : -1;
}

internal static class ReadAlongTiming
{
    private sealed record Plan(double End, LyricWord[] Words, bool FromSource);
    private static readonly ConditionalWeakTable<LyricLine, Plan> plans = new();
    private static readonly Regex tokens = new(@"\S+", RegexOptions.Compiled);
    internal static ReadAlongFrame At(LyricLine line, double end, double time)
    {
        end = Math.Max(line.Seconds, end);
        if (!plans.TryGetValue(line, out var plan) || Math.Abs(plan.End - end) > .001)
        {
            plans.Remove(line); plan = Build(line, end); plans.Add(line, plan);
        }
        int read = 0;
        foreach (var word in plan.Words)
        {
            if (time < word.Seconds) break;
            double finish = Math.Min(end, word.EndSeconds ?? end);
            if (time < finish)
                return new(word.Start, word.Length, word.Start, Math.Max(.02, finish - word.Seconds), plan.FromSource);
            read = word.Start + word.Length;
        }
        return new(-1, 0, read, 0, plan.FromSource);
    }
    private static Plan Build(LyricLine line, double end)
    {
        if (line.Words is { Length: > 0 } original)
        {
            var words = original.Select((word, i) => word with
            {
                EndSeconds = Math.Min(end, Math.Min(word.EndSeconds ?? double.MaxValue, i + 1 < original.Length ? original[i + 1].Seconds : end))
            }).ToArray();
            return new(end, words, true);
        }
        var spans = tokens.Matches(line.Text).Cast<Match>().Where(span => span.Value.Any(char.IsLetterOrDigit)).ToArray();
        // Line-only timestamps cannot identify vocal onsets. Estimate by syllable
        // weight and punctuation, and keep that mode separate from source timings.
        double[] weights = spans.Select(span =>
        {
            int vowels = span.Value.Count(ch => "аеёиоуыэюяaeiouyАЕЁИОУЫЭЮЯAEIOUY".Contains(ch));
            int letters = span.Value.Count(char.IsLetterOrDigit);
            return Math.Max(.45, vowels + Math.Min(letters, 10) * .12) + (span.Value.EndsWith(',') || span.Value.EndsWith(';') ? .35 : span.Value.EndsWith('.') || span.Value.EndsWith('!') || span.Value.EndsWith('?') ? .55 : 0);
        }).ToArray();
        double total = weights.Sum(), cursor = line.Seconds;
        var estimated = new LyricWord[spans.Length];
        for (int i = 0; i < spans.Length; i++)
        {
            double finish = i == spans.Length - 1 ? end : cursor + (end - line.Seconds) * weights[i] / Math.Max(.001, total);
            estimated[i] = new LyricWord(cursor, spans[i].Index, spans[i].Length, finish); cursor = finish;
        }
        return new(end, estimated, false);
    }
}
