/*
 * Copyright (c) 2014-2017, Eren Okka
 * Copyright (c) 2016-2017, Paul Miller
 * Copyright (c) 2017-2018, Tyler Bratton
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;

namespace AnitomySharp
{

    /// <summary>
    /// A class to manager the list of known anime keywords. This class is analogous to <code>keyword.cpp</code> of Anitomy, and <code>KeywordManager.java</code> of AnitomyJ
    /// </summary>
    public static class KeywordManager
    {
        private static readonly FrozenDictionary<string, Keyword> Keys;
        private static readonly FrozenDictionary<string, Keyword> Extensions;
        private static readonly (Element.ElementCategory Category, string[] Keywords)[] PeekEntries =
        [
            (Element.ElementCategory.ElementAudioTerm, ["Dual Audio"]),
            (Element.ElementCategory.ElementVideoTerm, ["H264", "H.264", "h264", "h.264"]),
            (Element.ElementCategory.ElementVideoResolution, ["480p", "720p", "1080p"]),
            (Element.ElementCategory.ElementSource, ["Blu-Ray"])
        ];

        static KeywordManager()
        {
            var optionsDefault = new KeywordOptions();
            var optionsInvalid = new KeywordOptions(true, true, false);
            var optionsUnidentifiable = new KeywordOptions(false, true, true);
            var optionsUnidentifiableInvalid = new KeywordOptions(false, true, false);
            var optionsUnidentifiableUnsearchable = new KeywordOptions(false, false, true);

            var keys = new Dictionary<string, Keyword>();
            var extensions = new Dictionary<string, Keyword>();

            void Add(Element.ElementCategory category, KeywordOptions options, ReadOnlySpan<string> keywords)
            {
                var dict = category == Element.ElementCategory.ElementFileExtension ? extensions : keys;
                foreach (var key in keywords)
                {
                    if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                    {
                        dict[key] = new Keyword(category, options);
                    }
                }
            }

            Add(Element.ElementCategory.ElementAnimeSeasonPrefix,
              optionsUnidentifiable,
              ["SAISON", "SEASON"]);

            Add(Element.ElementCategory.ElementAnimeType,
              optionsUnidentifiable,
              ["GEKIJOUBAN", "MOVIE", "OAD", "OAV", "ONA", "OVA", "SPECIAL", "SPECIALS", "TV"]);

            Add(Element.ElementCategory.ElementAnimeType,
              optionsUnidentifiableUnsearchable,
              ["SP"]); // e.g. "Yumeiro Patissiere SP Professional"

            Add(Element.ElementCategory.ElementAnimeType,
              optionsUnidentifiableInvalid,
              ["ED", "ENDING", "NCED", "NCOP", "OP", "OPENING", "PREVIEW", "PV"]);

            Add(Element.ElementCategory.ElementAudioTerm,
              optionsDefault,
              [
                // Audio channels
                "2.0CH", "2CH", "5.1", "5.1CH", "DTS", "DTS-ES", "DTS5.1",
                "TRUEHD5.1",
                // Audio codec
                "AAC", "AACX2", "AACX3", "AACX4", "AC3", "EAC3", "E-AC-3",
                "FLAC", "FLACX2", "FLACX3", "FLACX4", "LOSSLESS", "MP3", "OGG", "VORBIS",
                // Audio language
                "DUALAUDIO", "DUAL AUDIO"
              ]);

            Add(Element.ElementCategory.ElementDeviceCompatibility,
              optionsDefault,
              ["IPAD3", "IPHONE5", "IPOD", "PS3", "XBOX", "XBOX360"]);

            Add(Element.ElementCategory.ElementDeviceCompatibility,
              optionsUnidentifiable,
              ["ANDROID"]);

            Add(Element.ElementCategory.ElementEpisodePrefix,
              optionsDefault,
              ["EP", "EP.", "EPS", "EPS.", "EPISODE", "EPISODE.", "EPISODES", "CAPITULO", "EPISODIO", "FOLGE"]);

            Add(Element.ElementCategory.ElementEpisodePrefix,
              optionsInvalid,
              ["E", "\\x7B2C"]); // single-letter episode keywords are not valid tokens

            Add(Element.ElementCategory.ElementFileExtension,
              optionsDefault,
              ["3GP", "AVI", "DIVX", "FLV", "M2TS", "MKV", "MOV", "MP4", "MPG", "OGM", "RM", "RMVB", "TS", "WEBM", "WMV"]);

            Add(Element.ElementCategory.ElementFileExtension,
              optionsInvalid,
              ["AAC", "AIFF", "FLAC", "M4A", "MP3", "MKA", "OGG", "WAV", "WMA", "7Z", "RAR", "ZIP", "ASS", "SRT"]);

            Add(Element.ElementCategory.ElementLanguage,
              optionsDefault,
              ["ENG", "ENGLISH", "ESPANO", "JAP", "PT-BR", "SPANISH", "VOSTFR"]);

            Add(Element.ElementCategory.ElementLanguage,
              optionsUnidentifiable,
              ["ESP", "ITA"]); // e.g. "Tokyo ESP:, "Bokura ga Ita"

            Add(Element.ElementCategory.ElementOther,
              optionsDefault,
              ["REMASTER", "REMASTERED", "UNCENSORED", "UNCUT", "TS", "VFR", "WIDESCREEN", "WS"]);

            Add(Element.ElementCategory.ElementReleaseGroup,
              optionsDefault,
              ["THORA"]);

            Add(Element.ElementCategory.ElementReleaseInformation,
              optionsDefault,
              ["BATCH", "COMPLETE", "PATCH", "REMUX"]);

            Add(Element.ElementCategory.ElementReleaseInformation,
              optionsUnidentifiable,
              ["END", "FINAL"]); // e.g. "The End of Evangelion", 'Final Approach"

            Add(Element.ElementCategory.ElementReleaseVersion,
              optionsDefault,
              ["V0", "V1", "V2", "V3", "V4"]);

            Add(Element.ElementCategory.ElementSource,
              optionsDefault,
              ["BD", "BDRIP", "BLURAY", "BLU-RAY", "DVD", "DVD5", "DVD9", "DVD-R2J", "DVDRIP", "DVD-RIP", "R2DVD", "R2J", "R2JDVD", "R2JDVDRIP", "HDTV", "HDTVRIP", "TVRIP", "TV-RIP", "WEBCAST", "WEBRIP"]);

            Add(Element.ElementCategory.ElementSubtitles,
              optionsDefault,
              ["ASS", "BIG5", "DUB", "DUBBED", "HARDSUB", "HARDSUBS", "RAW", "SOFTSUB", "SOFTSUBS", "SUB", "SUBBED", "SUBTITLED"]);

            Add(Element.ElementCategory.ElementVideoTerm,
              optionsDefault,
              [
                // Frame rate
                "23.976FPS", "24FPS", "29.97FPS", "30FPS", "60FPS", "120FPS",
                // Video codec
                "8BIT", "8-BIT", "10BIT", "10BITS", "10-BIT", "10-BITS",
                "HI10", "HI10P", "HI444", "HI444P", "HI444PP",
                "H264", "H265", "H.264", "H.265", "X264", "X265", "X.264",
                "AVC", "HEVC", "HEVC2", "DIVX", "DIVX5", "DIVX6", "XVID",
                // Video format
                "AVI", "RMVB", "WMV", "WMV3", "WMV9",
                // Video quality
                "HQ", "LQ",
                // Video resolution
                "HD", "SD"
              ]);

            Add(Element.ElementCategory.ElementVolumePrefix,
              optionsDefault,
              ["VOL", "VOL.", "VOLUME"]);

            Keys = keys.ToFrozenDictionary();
            Extensions = extensions.ToFrozenDictionary();
        }

        public static string Normalize(string word)
        {
            return string.IsNullOrEmpty(word) ? word : word.ToUpperInvariant();
        }

        public static bool Contains(Element.ElementCategory category, string keyword)
        {
            var keys = GetKeywordContainer(category);
            if (keys.TryGetValue(keyword, out var foundEntry))
            {
                return foundEntry.Category == category;
            }

            return false;
        }

        /// <summary>
        /// Finds a particular <code>keyword</code>. If found sets <code>category</code> and <code>options</code> to the found search result.
        /// </summary>
        /// <param name="keyword">the keyword to search for</param>
        /// <param name="category">the reference that will be set/changed to the found keyword category</param>
        /// <param name="options">the reference that will be set/changed to the found keyword options</param>
        /// <returns>if the keyword was found</returns>
        public static bool FindAndSet(string keyword, ref Element.ElementCategory category, ref KeywordOptions options)
        {
            var keys = GetKeywordContainer(category);
            if (!keys.TryGetValue(keyword, out var foundEntry))
            {
                return false;
            }

            if (category == Element.ElementCategory.ElementUnknown)
            {
                category = foundEntry.Category;
            }
            else if (foundEntry.Category != category)
            {
                return false;
            }
            options = foundEntry.Options;
            return true;
        }

        /// <summary>
        /// Given a particular <code>filename</code> and <code>range</code> attempt to preidentify the token before we attempt the main parsing logic
        /// </summary>
        /// <param name="filename">the filename</param>
        /// <param name="range">the search range</param>
        /// <param name="elements">elements array that any pre-identified elements will be added to</param>
        /// <param name="preidentifiedTokens">elements array that any pre-identified token ranges will be added to</param>
        public static void PeekAndAdd(string filename, TokenRange range, List<Element> elements, List<TokenRange> preidentifiedTokens)
        {
            var endR = range.Offset + range.Size;
            var search = filename.Substring(range.Offset, endR > filename.Length ? filename.Length - range.Offset : endR - range.Offset);
            foreach (var entry in PeekEntries)
            {
                foreach (var keyword in entry.Keywords)
                {
                    var foundIdx = search.IndexOf(keyword, StringComparison.CurrentCulture);
                    if (foundIdx == -1) continue;
                    foundIdx += range.Offset;
                    elements.Add(new Element(entry.Category, keyword));
                    preidentifiedTokens.Add(new TokenRange(foundIdx, keyword.Length));
                }
            }
        }

        // Private API

        /** Returns the appropriate keyword container. */
        private static FrozenDictionary<string, Keyword> GetKeywordContainer(Element.ElementCategory category)
        {
            return category == Element.ElementCategory.ElementFileExtension ? Extensions : Keys;
        }
    }

    /// <summary>
    /// Keyword options for a particular keyword.
    /// </summary>
    public class KeywordOptions
    {
        public bool Identifiable { get; }
        public bool Searchable { get; }
        public bool Valid { get; }

        public KeywordOptions() : this(true, true, true) { }

        /// <summary>
        /// Constructs a new keyword options
        /// </summary>
        /// <param name="identifiable">if the token is identifiable</param>
        /// <param name="searchable">if the token is searchable</param>
        /// <param name="valid">if the token is valid</param>
        public KeywordOptions(bool identifiable, bool searchable, bool valid)
        {
            Identifiable = identifiable;
            Searchable = searchable;
            Valid = valid;
        }

    }

    /// <summary>
    /// A Keyword
    /// </summary>
    public struct Keyword
    {
        public readonly Element.ElementCategory Category;
        public readonly KeywordOptions Options;

        /// <summary>
        /// Constructs a new Keyword
        /// </summary>
        /// <param name="category">the category of the keyword</param>
        /// <param name="options">the keyword's options</param>
        public Keyword(Element.ElementCategory category, KeywordOptions options)
        {
            Category = category;
            Options = options;
        }
    }
}
