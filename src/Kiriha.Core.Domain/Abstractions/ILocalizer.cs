using System.Globalization;
using System.Text;

namespace Kiriha.Core.Abstractions.Services;

public interface ILocalizer
{
    public static ILocalizer Passthrough { get; } = new PassthroughLocalizer();

    string GetLoc(string key);
    string GetLoc(string key, params object?[] args);
    CultureInfo CurrentCulture => CultureInfo.CurrentCulture;

    private sealed class PassthroughLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key;

        public string GetLoc(string key, params object?[] args)
        {
            if (args is null || args.Length == 0)
                return key;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(key), args);
            }
            catch
            {
                return key;
            }
        }
    }
}
