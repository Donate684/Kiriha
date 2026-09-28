using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public static class DonghuaDetector
{
    private static readonly HashSet<string> ChineseStudios = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUILD DREAM", "Nice Boat Animation", "HMCH", "Big Firebird Culture", "Flying Fish Studio",
        "L²Studio", "L2Studio", "Bu Keneng de Shijie", "Haoliners Animation League", "Haoliners",
        "Foch Film", "Shanghai Foch Film", "Sparkly Key Animation", "Sparkly Key", "Colored-Pencil Animation Design",
        "Chosen", "Paper Plane Animation", "Paper Plane Animation Studio", "Rocen Entertainment", "Samsara",
        "Samsara Animation Studio", "Wulifang", "Tencent Penguin Pictures", "Tencent Video", "Tencent",
        "Bilibili", "B.CMAY PICTURES", "Studio LAN", "Thundray", "Shenman Entertainment", "Sun flowers",
        "Wonder Cat Animation", "Passion Paint Animation", "Original Force", "Foch", "Suoyi Technology",
        "Ruo Hong Culture", "Soyep", "Mili Pictures", "G-angle", "Year Young Culture", "Seven Stone Entertainment",
        "ASK Animation Studio", "Dangun Pictures", "Foch Film", "Beartoon", "Kung Fu Frog Animation"
    };

    private static readonly HashSet<string> KoreanStudios = new(StringComparer.OrdinalIgnoreCase)
    {
        "Studio Mir", "DR Movie", "Studio Animal", "Studio Gale", "Red Dog Culture House",
        "Studio PPURI", "Cocktail Media", "Studio Shelter", "Mirang Co., Ltd."
    };

    public static string? Detect(AnimeEntity anime)
    {
        if (anime == null) return null;

        // 1. Check Studios
        if (anime.Studios != null)
        {
            foreach (var studio in anime.Studios)
            {
                if (ChineseStudios.Contains(studio)) return "CN";
                if (KoreanStudios.Contains(studio)) return "KR";
            }
        }

        // 2. Check Synopsis for source / platform indicators
        if (!string.IsNullOrEmpty(anime.Synopsis))
        {
            string syn = anime.Synopsis;
            if (syn.Contains("Source: Bilibili", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: Tencent", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: iQIYI", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: Youku", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: WeTV", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: Mango TV", StringComparison.OrdinalIgnoreCase) ||
                syn.Contains("Source: Kuaikan", StringComparison.OrdinalIgnoreCase))
            {
                return "CN";
            }
        }

        // 3. Simplified Chinese characters check in JapaneseTitle (where MAL stores native Chinese titles)
        if (!string.IsNullOrEmpty(anime.JapaneseTitle))
        {
            bool hasKana = false;
            bool hasSimplifiedHanzi = false;
            foreach (char c in anime.JapaneseTitle)
            {
                // Hiragana: 0x3040-0x309F, Katakana: 0x30A0-0x30FF
                if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
                {
                    hasKana = true;
                    break;
                }

                // Simplified Chinese characters common in Chinese titles that differ from Japanese Kanji
                if ("镇饶罗战剧场说门国学记第发区总双体关见这".Contains(c))
                {
                    hasSimplifiedHanzi = true;
                }
            }

            if (!hasKana && hasSimplifiedHanzi)
            {
                return "CN";
            }
        }

        return null;
    }
}
