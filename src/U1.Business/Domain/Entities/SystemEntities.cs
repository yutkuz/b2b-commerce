namespace U1.Business.Domain;

public sealed class SchemaVersion
{
    public int Version { get; set; }
}

public sealed class DemoSetup
{
    public string Component { get; set; } = "";
    public string Status { get; set; } = "";
}
