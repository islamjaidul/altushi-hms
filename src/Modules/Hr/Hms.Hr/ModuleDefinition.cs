namespace Hms.Hr;

/// <summary>Module marker consumed by host composition and architecture tests (ADR-0003).</summary>
public sealed class HrModule
{
    public const string Name = "Hr";

    /// <summary>
    /// Route prefix the entitlement convention binds to (ADR-0026 choke point 2). Every page under
    /// it is refused when the licence does not name this module.
    /// </summary>
    public const string RoutePrefix = "/hr";
}
