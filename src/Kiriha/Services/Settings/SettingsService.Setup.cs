using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    public bool NeedsFirstStartup()
    {
        string[] required = ["language"];
        return Read(settings => required.Any(step => !settings.System.CompletedSetupSteps.Contains(step)));
    }

    public void CompleteSetupStep(string key)
    {
        var changed = false;
        Update(settings =>
        {
            if (!settings.System.CompletedSetupSteps.Contains(key))
            {
                settings.System.CompletedSetupSteps.Add(key);
                changed = true;
            }
        }, SettingsSection.System, save: false);

        if (changed) Save();
    }
}
