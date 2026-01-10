using System;

namespace ValyanERP.Web.Infrastructure.Time;

public static class TimeUtils
{
    private static TimeZoneInfo? _bucharestTz;

    private static TimeZoneInfo BucharestTimeZone
    {
        get
        {
            if (_bucharestTz != null) return _bucharestTz;

            try
            {
                // Windows ID
                _bucharestTz = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
            }
            catch
            {
                // Linux/Containers ID
                _bucharestTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
            }

            return _bucharestTz!;
        }
    }

    public static DateTime ToRomania(this DateTime utcOrLocal)
    {
        DateTime utc = utcOrLocal;
        if (utc.Kind == DateTimeKind.Unspecified)
        {
            // assume UTC when unspecified
            utc = DateTime.SpecifyKind(utcOrLocal, DateTimeKind.Utc);
        }
        else if (utc.Kind == DateTimeKind.Local)
        {
            utc = utc.ToUniversalTime();
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utc, BucharestTimeZone);
    }

    public static string ToRomaniaString(this DateTime dt, string format = "g")
    {
        var local = ToRomania(dt);
        return local.ToString(format);
    }

    public static DateTime NowRomania()
    {
        return TimeZoneInfo.ConvertTime(DateTime.UtcNow, BucharestTimeZone);
    }
}