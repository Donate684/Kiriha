using System;
using System.Collections.Generic;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

public class PlayerParser
{
    private enum State
    {
        ExpectPlayerName,
        ExpectSection,
        ExpectWindow,
        ExpectExecutable,
        ExpectStrategy,
        ExpectType,
        ExpectWindowTitle,
    }

    private static int GetIndentation(ReadOnlySpan<char> line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == '\t') count++;
            else break;
        }
        return count;
    }

    public static List<AnisthesiaPlayer> ParseData(string data)
    {
        var players = new List<AnisthesiaPlayer>();
        AnisthesiaPlayer? current = null;
        State state = State.ExpectPlayerName;

        foreach (var rawLine in data.AsSpan().EnumerateLines())
        {
            if (rawLine.IsWhiteSpace()) continue;
            int indent = GetIndentation(rawLine);
            var line = rawLine.Trim();
            if (line.StartsWith("#")) continue;

            if (indent == 0)
            {
                current = new AnisthesiaPlayer { Name = line.ToString() };
                players.Add(current);
                state = State.ExpectSection;
                continue;
            }

            if (current is null) continue;

            if (indent == 1)
            {
                line = line.TrimEnd(':');
                state = line switch
                {
                    "windows" => State.ExpectWindow,
                    "executables" => State.ExpectExecutable,
                    "strategies" => State.ExpectStrategy,
                    "type" => State.ExpectType,
                    _ => State.ExpectSection
                };
                continue;
            }

            if (indent == 2)
            {
                if (state == State.ExpectWindow) current.WindowClasses.Add(line.ToString());
                else if (state == State.ExpectExecutable) current.Executables.Add(line.ToString());
                else if (state == State.ExpectStrategy)
                {
                    line = line.TrimEnd(':');
                    if (line is "window_title")
                    {
                        current.Strategies.Add(StrategyType.WindowTitle);
                        state = State.ExpectWindowTitle;
                    }
                    else if (line is "open_files") current.Strategies.Add(StrategyType.OpenFiles);
                    else if (line is "ui_automation") current.Strategies.Add(StrategyType.UiAutomation);
                }
                else if (state == State.ExpectType)
                {
                    current.Type = line is "web_browser" ? PlayerType.WebBrowser : PlayerType.Default;
                }
                continue;
            }

            if (indent == 3 && state == State.ExpectWindowTitle)
            {
                current.WindowTitleFormat = line.ToString();
            }
        }
        return players;
    }
}

