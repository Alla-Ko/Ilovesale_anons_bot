namespace Announcement.Services;

public enum CollageCaptionStatus
{
    /// <summary>Немає колажів з валютним підписом — індикатор не показуємо.</summary>
    None,

    /// <summary>Є колажі з валютним підписом без гривневого.</summary>
    Partial,

    /// <summary>У всіх колажів з валютним підписом є гривневий.</summary>
    Complete
}

public static class CollageCaptionStatusHelper
{
    public static CollageCaptionStatus Compute(IEnumerable<(string? Caption1, string? Caption2)> collages)
    {
        var withCap1 = collages.Where(c => !string.IsNullOrWhiteSpace(c.Caption1)).ToList();
        if (withCap1.Count == 0)
            return CollageCaptionStatus.None;

        return withCap1.Any(c => string.IsNullOrWhiteSpace(c.Caption2))
            ? CollageCaptionStatus.Partial
            : CollageCaptionStatus.Complete;
    }
}
