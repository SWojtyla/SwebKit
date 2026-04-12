namespace SwebKit.App.Services;

public static class SearchScoring
{
    public static int FuzzyScore(string query, string label)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(label))
        {
            return 0;
        }

        var q = query.Trim().ToLowerInvariant();
        var l = label.ToLowerInvariant();
        var qi = 0;
        var score = 0;
        var lastMatch = -1;
        var firstMatchSeen = false;

        for (var i = 0; i < l.Length && qi < q.Length; i++)
        {
            if (l[i] != q[qi])
            {
                continue;
            }

            var bonus = i == lastMatch + 1 ? 2 : 1;
            if (!firstMatchSeen && i == 0)
            {
                bonus += 3;
            }

            firstMatchSeen = true;
            score += bonus;
            lastMatch = i;
            qi++;
        }

        return qi == q.Length ? score : 0;
    }
}