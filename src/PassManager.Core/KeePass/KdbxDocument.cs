namespace PassManager.Core.KeePass;

public class KdbxGroup
{
    public string Name { get; set; } = "";
    public List<KdbxGroup> Groups { get; } = [];
    public List<KdbxEntry> Entries { get; } = [];
}

public class KdbxEntry
{
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? UserName { get; set; }
    public string Password { get; set; } = "";
    public string? Notes { get; set; }
}

public class KdbxDocument
{
    public KdbxGroup Root { get; set; } = new();
}
