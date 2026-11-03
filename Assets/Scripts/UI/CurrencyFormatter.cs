using System.Globalization;

/// <summary>
/// Universal money formatting: $950, $1.25k, $4.50M, $10.20B, $12.50T.
/// Below $1,000 shows whole dollars (cents are kept for values under $10 so tiny bounce payouts stay readable).
/// </summary>
public static class CurrencyFormatter
{
    public static string Format(decimal value)
    {
        bool negative = value < 0m;
        decimal abs = negative ? -value : value;
        string body;

        if (abs >= 1_000_000_000_000m) body = (abs / 1_000_000_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "T";
        else if (abs >= 1_000_000_000m) body = (abs / 1_000_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "B";
        else if (abs >= 1_000_000m) body = (abs / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "M";
        else if (abs >= 1_000m) body = (abs / 1_000m).ToString("0.00", CultureInfo.InvariantCulture) + "k";
        else if (abs >= 10m) body = decimal.Round(abs, 0, System.MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        else body = abs.ToString("0.00", CultureInfo.InvariantCulture);

        return (negative ? "-$" : "$") + body;
    }

    public static string Format(float value) => Format((decimal)value);

    public static string Format(double value) => Format((decimal)value);
}
