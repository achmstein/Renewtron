namespace Renewtron.Data;

public enum SearchSource
{
    /// <summary>Live ASIC Connect search: names carry ASIC account numbers.</summary>
    Asic = 0,

    /// <summary>
    /// Our copy of the public register. Shown straight away, then checked against ASIC in
    /// the background (<see cref="SearchLog.VerifiedAt"/>) before anything can be paid for.
    /// </summary>
    Local = 1,
}
