using System.Collections.Frozen;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public static class DonghuaDetector
{
    private static readonly FrozenSet<string> ChineseStudios = new[]
    {
        "BUILD DREAM", "Nice Boat Animation", "HMCH", "Big Firebird Culture", "Big Firebird", "Flying Fish Studio",
        "L²Studio", "L2Studio", "Bu Keneng de Shijie", "Haoliners Animation League", "Haoliners", "Haoliners Animation",
        "Foch Film", "Shanghai Foch Film", "Foch", "Sparkly Key Animation", "Sparkly Key", "Colored-Pencil Animation Design",
        "Colored-Pencil", "Chosen", "Paper Plane Animation", "Paper Plane Animation Studio", "Paper Plane", "Rocen Entertainment", "Rocen", "Samsara",
        "Samsara Animation Studio", "Wulifang", "Tencent Penguin Pictures", "Tencent Video", "Tencent",
        "Bilibili", "B.CMAY PICTURES", "B.CMAY", "Studio LAN", "Thundray", "Shenman Entertainment", "Sun flowers",
        "Wonder Cat Animation", "Passion Paint Animation", "Original Force", "Suoyi Technology", "Suoyi Animation",
        "Ruo Hong Culture", "Soyep", "Mili Pictures", "G-angle", "Year Young Culture", "Seven Stone Entertainment",
        "ASK Animation Studio", "Dangun Pictures", "Beartoon", "Kung Fu Frog Animation", "BYMENT",
        "Djinn Power", "Base FX", "Light Chaser Animation", "Pearl Studio", "Fantawild Animation",
        "October Media", "Vasoon Animation", "Choice Animation", "Poplar Animation", "Qingxiang Culture",
        "All-Suns Culture", "Lead Culture", "Gravity Well", "Tang Space", "Fenz", "Tianmi",
        "Bochuang Animation", "Grip Animation", "Huachen Animation", "Huashi Animation", "June Animation",
        "Kaixuan Culture", "KJJ Animation", "Kuiba", "Lianhuan Animation", "Lingyu Animation",
        "Miao Film", "Milkyway Animation", "One Animation", "Painter Animation", "Pochacco",
        "Qingwa Animation", "Ruishi Animation", "Shanghai Animation Film Studio", "SAFS",
        "Shanghai Hippo Animation", "Shenying Animation", "Shilian Culture", "Taomee", "Tianhe Animation",
        "Vaca", "Wawayu Animation", "Wenhua Media", "Xiaoming Taiji", "Xinchuan Animation", "Xingyi Animation",
        "Xiron Animation", "Yinman Culture", "Yinshun Culture", "Yishan Animation", "Yongtai Media",
        "Yuanmeng Culture", "Yuanqi Animation", "Yuchuan Animation", "Yunfan Animation", "Zhonghe Animation",
        "Zhenyou Animation", "Zhiying Culture", "Zhongnan Animation", "Zhongrun Animation", "2:10 AM Animation",
        "YHKT Entertainment", "YHKT", "Shanghai Motion Magic", "Motion Magic", "Hunan Golden Eagle Cartoon",
        "Alpha Animation", "Alpha Group", "Winsing Animation", "Starry Sky Animation", "Chuanxiang Culture",
        "Taikong Animation", "Chongqing Panda", "CGCG Studio", "iQIYI", "Youku", "Mango TV"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> KoreanStudios = new[]
    {
        "Studio Mir", "DR Movie", "Studio Animal", "Studio Gale", "Red Dog Culture House",
        "Studio PPURI", "Cocktail Media", "Studio Shelter", "Mirang Co., Ltd.", "Roi Visual",
        "Iconix Entertainment", "Dong Woo Animation", "Dongwoo A&E", "Sunwoo Entertainment",
        "Hanho Heung-Up", "AKOM", "Heewon Entertainment", "Saerom Animation", "Rough Draft Korea",
        "Daewon Media", "SamG Animation", "SAMG Entertainment", "Studio EEK", "Locus Animation",
        "Sidus Animation", "Studio Bazooka", "CJ ENM", "Studio N", "Tinto Animation", "Studio Pivote"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ChineseStudioStems = new[]
    {
        "Sparkly Key", "Bilibili", "Tencent", "iQIYI", "Youku", "Foch", "Haoliners", "Big Firebird",
        "Paper Plane", "Ruo Hong", "Suoyi", "Soyep", "Shenman", "Samsara", "YHKT", "B.CMAY",
        "Studio LAN", "BUILD DREAM", "HMCH", "Rocen", "Nice Boat", "Colored-Pencil", "Motion Magic",
        "Light Chaser", "Original Force", "Dangun", "ASK Animation", "Seven Stone", "Thundray",
        "Wonder Cat", "Passion Paint", "Kung Fu Frog", "Beartoon", "Djinn Power", "Base FX",
        "Fantawild", "October Media", "Vasoon", "Poplar Animation", "Qingxiang", "2:10 AM",
        "Alpha Animation", "Winsing", "Chuanxiang", "Mango TV"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> KoreanStudioStems = new[]
    {
        "Studio Mir", "DR Movie", "Studio Animal", "Studio Gale", "Red Dog", "Studio PPURI",
        "Cocktail Media", "Studio Shelter", "Mirang", "Roi Visual", "Iconix", "Dong Woo",
        "Dongwoo", "Sunwoo", "Hanho", "AKOM", "Heewon", "Saerom", "Rough Draft Korea",
        "Daewon", "SamG", "SAMG", "Studio EEK", "Locus", "Studio Bazooka", "Studio N"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ChineseTitleKeywords = new[]
    {
        "xian", "ni", "shixiong", "zuizhong", "changan", "douluo", "dalu", "doupo", "cangqiong",
        "tunshi", "xingkong", "xianxia", "wuxia", "xuanhuan", "quanzhi", "wushen", "zhuzai",
        "bailian", "chengshen", "yinian", "yongheng", "wanjie", "shenzhu", "shiguang", "dailiren",
        "huoyuan", "feiren", "wangzuo", "xiuzhen", "xiuxian", "guoman", "donghua", "tianguan",
        "cifu", "modao", "zushi", "luoyang", "moxiang", "wushan", "wuxing", "shanhe", "jianxin",
        "zijiu", "zhinan", "mengqi", "shishen", "daomubiji", "linglong", "lingjian", "dubu",
        "xiaoyao", "nitian", "xieshen", "yuanzun", "jiangu", "zhendaoge", "dawang", "raoming",
        "biaoren", "chuanqi", "fashi", "bilibili", "fanren", "cangyuan", "qiankun"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<char> SimplifiedChineseCharacters =
        "门罗陆车长风飞鸟马鱼说话记让时间书买卖传伤剑剧办动漫图灵笼苍绝镇饶圣炼岁纪录兽异续齐单严视觉项顺须顾题颜类龙业东丝两为丽义乌乐习乡亲仅从众优伞价伦伪侧侦偿储兰关兴养军农创务勋胜势沧沟渊渐渔洁洒涛涨涩滨滩溃润满滥澜灿烂热爱统绪维铁银钱铸链锁镜钟陈险隐难雾霸风饰饭馆驴驻骑鲜鸟鸭鸿鹤鹰龙师终录谭"
        .ToFrozenSet();

    private static readonly char[] TitleSeparators = [' ', '-', ':', '_', '/', '(', ')', '[', ']', '!', '?', ',', '.', '\''];

    public static string? Detect(AnimeEntity anime)
    {
        if (anime == null) return null;

        // 1. Hangul in any title / synonym -> Definitely Korean (KR)
        if (ContainsHangul(anime.JapaneseTitle) ||
            ContainsHangul(anime.Title) ||
            ContainsHangul(anime.EnglishTitle) ||
            (anime.AlternativeTitles != null && anime.AlternativeTitles.Any(ContainsHangul)))
        {
            return "KR";
        }

        // 2. Korean Studio Check (full name or studio stem)
        if (anime.Studios != null)
        {
            foreach (var studio in anime.Studios)
            {
                if (IsKoreanStudio(studio)) return "KR";
            }
        }

        // 3. Korean Synopsis / Source Check
        if (IsKoreanSource(anime.Synopsis) || IsKoreanSource(anime.RussianSynopsis))
        {
            return "KR";
        }

        // 4. Chinese Studio Check (full name or studio stem)
        if (anime.Studios != null)
        {
            foreach (var studio in anime.Studios)
            {
                if (IsChineseStudio(studio)) return "CN";
            }
        }

        // 5. Chinese Platform / Source in Synopsis
        if (IsChineseSource(anime.Synopsis) || IsChineseSource(anime.RussianSynopsis))
        {
            return "CN";
        }

        // 6. Native Script Analysis: Check JapaneseTitle and Synonyms for Simplified Hanzi (with no Kana)
        if (ContainsSimplifiedHanziWithoutKana(anime.JapaneseTitle))
        {
            return "CN";
        }
        if (anime.AlternativeTitles != null)
        {
            foreach (var alt in anime.AlternativeTitles)
            {
                if (ContainsSimplifiedHanziWithoutKana(alt))
                {
                    return "CN";
                }
            }
        }

        // 7. Check Chinese Pinyin keywords in Romaji / English Title and Alternative Titles
        // Only if JapaneseTitle does not have Japanese kana
        bool titleHasKana = !string.IsNullOrEmpty(anime.JapaneseTitle) && ContainsKana(anime.JapaneseTitle);
        if (!titleHasKana)
        {
            if (ContainsChineseTitleKeywords(anime.Title) ||
                ContainsChineseTitleKeywords(anime.EnglishTitle) ||
                (anime.AlternativeTitles != null && anime.AlternativeTitles.Any(ContainsChineseTitleKeywords)))
            {
                return "CN";
            }
        }

        return null;
    }

    private static bool IsChineseStudio(string studio)
    {
        if (string.IsNullOrWhiteSpace(studio)) return false;
        if (ChineseStudios.Contains(studio)) return true;
        foreach (var stem in ChineseStudioStems)
        {
            if (studio.Contains(stem, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsKoreanStudio(string studio)
    {
        if (string.IsNullOrWhiteSpace(studio)) return false;
        if (KoreanStudios.Contains(studio)) return true;
        foreach (var stem in KoreanStudioStems)
        {
            if (studio.Contains(stem, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool ContainsSimplifiedHanziWithoutKana(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (ContainsKana(text)) return false;

        foreach (char c in text)
        {
            if (SimplifiedChineseCharacters.Contains(c))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsHangul(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if ((c >= 0xAC00 && c <= 0xD7A3) ||
                (c >= 0x1100 && c <= 0x11FF) ||
                (c >= 0x3130 && c <= 0x318F))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsKana(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsChineseTitleKeywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = text.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var word in words)
        {
            if (ChineseTitleKeywords.Contains(word))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsChineseSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return text.Contains("Source: Bilibili", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Tencent", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: iQIYI", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Youku", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: WeTV", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Mango TV", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Kuaikan", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: AcFun", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Webnovel", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Qidian", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Manhua", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Chinese", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Donghua", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Bangumi", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Bangumi", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Chang'an", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Chinese animated", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Chinese animation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Chinese donghua", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("donghua", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKoreanSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return text.Contains("Source: Webtoon", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Naver", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Kakao", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Daum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Lezhin", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Source: Manhwa", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("manhwa", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Korean animated", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Korean animation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("aeni", StringComparison.OrdinalIgnoreCase);
    }
}
