using System.Collections.Generic;

namespace Kiriha.Core.Domain.Models;

public class AppConfigFile
{
    public AppSettings.UiConfig UI { get; set; } = new();
    public AppSettings.SystemConfig System { get; set; } = new();
    public List<CustomShareLink> CustomLinks { get; set; } = new();
}
