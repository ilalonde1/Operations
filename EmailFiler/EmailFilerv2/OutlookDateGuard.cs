using System;

namespace EmailFilerv2
{
    internal static class OutlookDateGuard
    {
        private static readonly DateTime EarliestPlausibleSentOn = new DateTime(1990, 1, 1);

        internal static DateTime GetPlausibleSentOnOrNow(DateTime sentOn)
        {
            DateTime now = DateTime.Now;
            return IsPlausibleSentOn(sentOn, now) ? sentOn : now;
        }

        internal static bool IsPlausibleSentOn(DateTime sentOn, DateTime now)
        {
            if (sentOn == DateTime.MinValue || sentOn == DateTime.MaxValue)
                return false;

            DateTime latestPlausible = now.AddDays(1);
            return sentOn >= EarliestPlausibleSentOn && sentOn <= latestPlausible;
        }
    }
}
