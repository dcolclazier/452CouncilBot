using System;
using System.Collections.Generic;

public class OffenseReport
{
    public string reportId { get; set; }
    public string playerId { get; set; }
    public string playerName { get; set; }
    public string playerAlliance { get; set; }
    public string offenseType { get; set; }
    public DateTime date { get; set; }
    public List<string> evidenceUrls { get; set; }
    public string reportDetails { get; set; }
}