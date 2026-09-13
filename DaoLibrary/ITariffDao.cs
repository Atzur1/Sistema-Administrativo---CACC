namespace DaoLibrary;

using EntityLibrary;

public interface ITariffDao
{
    // The tariff in force for the given branch on the given date, or null if
    // none was scheduled yet for that period.
    Tariff? GetCurrentTariffByBranch(string branch, DateTime asOfDate);

    // Every tariff ever scheduled, across both branches, newest first.
    List<Tariff> GetTariffHistory();

    // Schedules a new tariff for its branch. If that branch already has an
    // open-ended tariff, it is closed the day before the new one starts;
    // fees already issued under it keep their persisted amount untouched.
    Tariff ScheduleTariff(Tariff newTariff);
}
