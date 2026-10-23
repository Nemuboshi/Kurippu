namespace Kurippu.Desktop.Services;

public sealed record SourceChoices(string Url, string Title, IReadOnlyList<FormatChoice> Videos,
    IReadOnlyList<FormatChoice> Audios, IReadOnlyList<FormatChoice> Subtitles, string? SaveAs = null)
{
    public int DefaultSubtitleIndex
    {
        get
        {
            for (var i = 0; i < Subtitles.Count; i++)
                if (Subtitles[i].Id is "en" or "eng" or "en-US" or "en_US") return i;
            for (var i = 0; i < Subtitles.Count; i++)
                if (string.Equals(Subtitles[i].Id, "none", StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
