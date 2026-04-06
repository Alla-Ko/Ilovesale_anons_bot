namespace Announcement.Models;

public enum Country
{
    Us = 0,
    Uk = 1,
    Sp = 2,
    Pl = 3,
    De = 4,
    It = 5,
    Fr = 6,
    Pt = 7,
    Ua = 8
}

public static class CountryLabels
{
    public static readonly IReadOnlyDictionary<Country, string> Labels = new Dictionary<Country, string>
    {
        [Country.Us] = "США",
        [Country.Uk] = "Англія",
        [Country.Sp] = "Іспанія",
        [Country.Pl] = "Польща",
        [Country.De] = "Німеччина",
        [Country.It] = "Італія",
        [Country.Fr] = "Франція",
        [Country.Pt] = "Португалія",
        [Country.Ua] = "Україна"
    };

    public static IEnumerable<Country> All => Enum.GetValues<Country>();
}
