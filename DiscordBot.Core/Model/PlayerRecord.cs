using System.Collections.Generic;

public class PlayerRecord
{
    public string playerId { get; set; }
    public string playerName { get; set; }
    public List<string> knownNames { get; set; }
    public string playerAlliance { get; set; }
    public List<string> knownAlliances { get; set; }
    public bool redFlag { get; set; }
    public string redFlagReason { get; set; }
    public List<string> offenseIds { get; set; }
}
