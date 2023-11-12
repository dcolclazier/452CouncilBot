using System;
using System.Collections.Generic;

public class OffenseReport
{
    public string reportId { get; set; } = string.Empty;
    public string playerId { get; set; } = string.Empty;
    public string playerName { get; set; } = string.Empty;
    public string playerAlliance { get; set; } = string.Empty;
    public string offenseType { get; set; } = string.Empty;
    public DateTime date { get; set; } = DateTime.MinValue;
    public List<string> evidenceUrls { get; set; } = new List<string>();
    public string reportDetails { get; set; } = string.Empty;
}