using System.Collections.Generic;

public class PlayerRecord
{
    public string playerId { get; set; } = string.Empty;
    public string playerName { get; set; } = string.Empty;
    public List<string> knownNames { get; set; } = new List<string>();
    public string playerAlliance { get; set; } = string.Empty;
    public List<string> knownAlliances { get; set; } = new List<string>();
    public bool redFlag { get; set; }   
    public string redFlagReason { get; set; } = string.Empty;
    public List<string> offenseIds { get; set; } = new List<string>();
}
