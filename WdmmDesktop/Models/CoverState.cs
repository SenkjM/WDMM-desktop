namespace WdmmDesktop.Models
{
    /// <summary>
    /// Persisted cover availability (matches Core wdmm-storage schema v10).
    /// Local thumb path is always derived from identity; only this state is SoT.
    /// 0 = unknown (fill allowed), 1 = none (fill forbidden), 2 = present (fill if missing).
    /// </summary>
    public enum CoverState : int
    {
        Unknown = 0,
        None = 1,
        Present = 2,
    }
}
