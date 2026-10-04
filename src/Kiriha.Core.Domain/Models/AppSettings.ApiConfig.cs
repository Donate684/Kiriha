using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Domain.Models;

public partial class AppSettings
{
    public class ApiConfig
    {
        [JsonPropertyName("Accounts")]
        public List<TrackerAccount> Accounts { get; set; } = new();

        [JsonPropertyName("PrimaryTrackerId")]
        public string PrimaryTrackerId { get; set; } = TrackerConstants.Ids.Mal;

        [JsonPropertyName("ShikiMirror")]
        public ShikiMirror ShikiMirror { get; set; } = ShikiMirror.One;

        /// <summary>
        /// Legacy field for backward compatibility with existing auth.json files.
        /// </summary>
        [JsonPropertyName("Mal")]
        public MalTokens? LegacyMal { get; set; }

        /// <summary>
        /// Legacy field for backward compatibility with existing auth.json files.
        /// </summary>
        [JsonPropertyName("Shiki")]
        public ShikiTokens? LegacyShiki { get; set; }

        [JsonIgnore]
        public MalTokens? Mal
        {
            get => Accounts.FirstOrDefault(a => a.TrackerId == TrackerConstants.Ids.Mal && a.IsEnabled)?.Tokens as MalTokens
                   ?? LegacyMal;
            set
            {
                LegacyMal = value;
                var acc = Accounts.FirstOrDefault(a => a.TrackerId == TrackerConstants.Ids.Mal);
                if (value == null)
                {
                    if (acc != null) Accounts.Remove(acc);
                }
                else
                {
                    if (acc == null)
                    {
                        acc = new TrackerAccount
                        {
                            TrackerId = TrackerConstants.Ids.Mal,
                            IsEnabled = true,
                            IsPrimary = Accounts.Count == 0,
                            IsMirror = true
                        };
                        Accounts.Add(acc);
                    }
                    acc.Tokens = value;
                    acc.IsEnabled = true;
                }
                EnsurePrimaryIntegrity();
            }
        }

        [JsonIgnore]
        public ShikiTokens? Shiki
        {
            get
            {
                var mirrorTrackerId = ShikiMirror == ShikiMirror.Net ? TrackerConstants.Ids.ShikiFork : TrackerConstants.Ids.ShikiOrig;
                return (Accounts.FirstOrDefault(a => a.TrackerId == mirrorTrackerId && a.IsEnabled)?.Tokens as ShikiTokens)
                    ?? (Accounts.FirstOrDefault(a => (a.TrackerId == TrackerConstants.Ids.ShikiOrig || a.TrackerId == TrackerConstants.Ids.ShikiFork) && a.IsEnabled)?.Tokens as ShikiTokens)
                    ?? LegacyShiki;
            }
            set
            {
                LegacyShiki = value;
                var mirrorTrackerId = (value?.Mirror ?? ShikiMirror) == ShikiMirror.Net
                    ? TrackerConstants.Ids.ShikiFork
                    : TrackerConstants.Ids.ShikiOrig;

                var acc = Accounts.FirstOrDefault(a => a.TrackerId == mirrorTrackerId)
                       ?? Accounts.FirstOrDefault(a => a.TrackerId == TrackerConstants.Ids.ShikiOrig || a.TrackerId == TrackerConstants.Ids.ShikiFork);

                if (value == null)
                {
                    if (acc != null) Accounts.Remove(acc);
                }
                else
                {
                    if (acc == null)
                    {
                        acc = new TrackerAccount
                        {
                            TrackerId = mirrorTrackerId,
                            IsEnabled = true,
                            IsPrimary = Accounts.Count == 0,
                            IsMirror = true
                        };
                        Accounts.Add(acc);
                    }
                    else
                    {
                        acc.TrackerId = mirrorTrackerId;
                    }
                    acc.Tokens = value;
                    acc.IsEnabled = true;
                }
                EnsurePrimaryIntegrity();
            }
        }

        public TrackerAccount? GetAccount(string trackerId) =>
            Accounts.FirstOrDefault(a => string.Equals(a.TrackerId, trackerId, StringComparison.OrdinalIgnoreCase));

        public TrackerAccount? GetPrimaryAccount() =>
            Accounts.FirstOrDefault(a => a.IsPrimary && a.IsEnabled)
            ?? Accounts.FirstOrDefault(a => string.Equals(a.TrackerId, PrimaryTrackerId, StringComparison.OrdinalIgnoreCase) && a.IsEnabled)
            ?? Accounts.FirstOrDefault(a => a.IsEnabled);

        public IEnumerable<TrackerAccount> GetMirrorAccounts() =>
            Accounts.Where(a => a.IsEnabled && a.IsMirror);

        public void SetPrimaryAccount(string trackerId)
        {
            PrimaryTrackerId = trackerId;
            foreach (var acc in Accounts)
            {
                acc.IsPrimary = string.Equals(acc.TrackerId, trackerId, StringComparison.OrdinalIgnoreCase);
            }
        }

        public void EnsureMigrated()
        {
            if (Accounts.Count == 0)
            {
                if (LegacyMal != null && !string.IsNullOrEmpty(LegacyMal.AccessToken))
                {
                    Accounts.Add(new TrackerAccount
                    {
                        TrackerId = TrackerConstants.Ids.Mal,
                        IsEnabled = true,
                        IsPrimary = true,
                        IsMirror = true,
                        Tokens = LegacyMal
                    });
                }

                if (LegacyShiki != null && !string.IsNullOrEmpty(LegacyShiki.AccessToken))
                {
                    var trackerId = LegacyShiki.Mirror == ShikiMirror.Net
                        ? TrackerConstants.Ids.ShikiFork
                        : TrackerConstants.Ids.ShikiOrig;

                    Accounts.Add(new TrackerAccount
                    {
                        TrackerId = trackerId,
                        IsEnabled = true,
                        IsPrimary = Accounts.Count == 0,
                        IsMirror = true,
                        Tokens = LegacyShiki
                    });
                }
            }

            EnsurePrimaryIntegrity();
            SyncLegacyFromAccounts();
        }

        public void EnsurePrimaryIntegrity()
        {
            if (Accounts.Count == 0)
            {
                if (string.IsNullOrEmpty(PrimaryTrackerId))
                {
                    PrimaryTrackerId = TrackerConstants.Ids.Mal;
                }
                return;
            }

            var primary = Accounts.FirstOrDefault(a => a.IsPrimary);
            if (primary != null)
            {
                PrimaryTrackerId = primary.TrackerId;
            }
            else if (!string.IsNullOrEmpty(PrimaryTrackerId))
            {
                var target = Accounts.FirstOrDefault(a => string.Equals(a.TrackerId, PrimaryTrackerId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    target.IsPrimary = true;
                }
                else
                {
                    Accounts[0].IsPrimary = true;
                    PrimaryTrackerId = Accounts[0].TrackerId;
                }
            }
            else
            {
                Accounts[0].IsPrimary = true;
                PrimaryTrackerId = Accounts[0].TrackerId;
            }
        }

        public void SyncLegacyFromAccounts()
        {
            LegacyMal = Accounts.FirstOrDefault(a => a.TrackerId == TrackerConstants.Ids.Mal)?.Tokens as MalTokens;

            var mirrorTrackerId = ShikiMirror == ShikiMirror.Net ? TrackerConstants.Ids.ShikiFork : TrackerConstants.Ids.ShikiOrig;
            LegacyShiki = (Accounts.FirstOrDefault(a => a.TrackerId == mirrorTrackerId)?.Tokens as ShikiTokens)
                       ?? (Accounts.FirstOrDefault(a => a.TrackerId == TrackerConstants.Ids.ShikiOrig || a.TrackerId == TrackerConstants.Ids.ShikiFork)?.Tokens as ShikiTokens);
        }

        public ApiConfig Clone() => new()
        {
            Accounts = Accounts.Select(a => a.Clone()).ToList(),
            PrimaryTrackerId = PrimaryTrackerId,
            ShikiMirror = ShikiMirror,
            LegacyMal = LegacyMal?.Clone(),
            LegacyShiki = LegacyShiki?.Clone()
        };
    }
}
