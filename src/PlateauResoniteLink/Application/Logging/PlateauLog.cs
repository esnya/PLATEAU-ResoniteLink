using System;

namespace PlateauResoniteLink.Application.Logging;

public enum PlateauLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public readonly record struct PlateauLogEntry(
    string Scope,
    PlateauLogLevel Level,
    string Message)
{
    public string LevelToken => GetLevelToken(Level);

    public override string ToString()
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"[{Scope}][{LevelToken}] {Message}");
    }

    public static bool TryParse(string value, out PlateauLogEntry entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(value) || value[0] != '[')
        {
            return false;
        }

        int scopeEnd = value.IndexOf(']');
        if (scopeEnd <= 1 || scopeEnd + 1 >= value.Length || value[scopeEnd + 1] != '[')
        {
            return false;
        }

        int levelStart = scopeEnd + 2;
        int levelEnd = value.IndexOf(']', levelStart);
        if (levelEnd <= levelStart || levelEnd + 2 > value.Length || value[levelEnd + 1] != ' ')
        {
            return false;
        }

        if (!TryParseLevelToken(value[levelStart..levelEnd], out PlateauLogLevel level))
        {
            return false;
        }

        entry = new PlateauLogEntry(
            value[1..scopeEnd],
            level,
            value[(levelEnd + 2)..]);
        return true;
    }

    private static string GetLevelToken(PlateauLogLevel level)
    {
        return level switch
        {
            PlateauLogLevel.Debug => "debug",
            PlateauLogLevel.Info => "info",
            PlateauLogLevel.Warning => "warn",
            PlateauLogLevel.Error => "error",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
    }

    private static bool TryParseLevelToken(string token, out PlateauLogLevel level)
    {
        switch (token)
        {
            case "debug":
                level = PlateauLogLevel.Debug;
                return true;
            case "info":
                level = PlateauLogLevel.Info;
                return true;
            case "warn":
                level = PlateauLogLevel.Warning;
                return true;
            case "error":
                level = PlateauLogLevel.Error;
                return true;
            default:
                level = default;
                return false;
        }
    }
}

public static class PlateauLog
{
    public static PlateauLogLevel InferLegacyDefaultLevel(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (TryParseLegacyScopeAndLevel(message, out _, out PlateauLogLevel level, out _))
        {
            return level;
        }

        return message.StartsWith("[live]", StringComparison.Ordinal)
            ? PlateauLogLevel.Debug
            : PlateauLogLevel.Info;
    }

    public static string NormalizeLegacyMessage(string message, PlateauLogLevel defaultLevel = PlateauLogLevel.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (PlateauLogEntry.TryParse(message, out _))
        {
            return message;
        }

        if (TryParseLegacyScopeAndLevel(message, out string? legacyScope, out PlateauLogLevel level, out string? legacyBody))
        {
            return new PlateauLogEntry(legacyScope, level, legacyBody).ToString();
        }

        if (message[0] != '[')
        {
            return new PlateauLogEntry("app", defaultLevel, message).ToString();
        }

        int scopeEnd = message.IndexOf(']');
        if (scopeEnd <= 1 || scopeEnd + 2 > message.Length || message[scopeEnd + 1] != ' ')
        {
            return new PlateauLogEntry("app", defaultLevel, message).ToString();
        }

        string scope = message[1..scopeEnd];
        string body = message[(scopeEnd + 2)..];
        return new PlateauLogEntry(scope, defaultLevel, body).ToString();
    }

    private static bool TryParseLegacyScopeAndLevel(
        string message,
        out string scope,
        out PlateauLogLevel level,
        out string body)
    {
        scope = string.Empty;
        level = default;
        body = string.Empty;

        if (string.IsNullOrWhiteSpace(message) || message[0] != '[')
        {
            return false;
        }

        int scopeEnd = message.IndexOf(']');
        if (scopeEnd <= 1
            || scopeEnd + 2 >= message.Length
            || message[scopeEnd + 1] != '[')
        {
            return false;
        }

        int levelStart = scopeEnd + 2;
        int levelEnd = message.IndexOf(']', levelStart);
        if (levelEnd <= levelStart
            || levelEnd + 2 > message.Length
            || message[levelEnd + 1] != ' ')
        {
            return false;
        }

        if (!PlateauLogEntry.TryParse($"[app][{message[levelStart..levelEnd]}] x", out PlateauLogEntry parsed))
        {
            return false;
        }

        scope = message[1..scopeEnd];
        level = parsed.Level;
        body = message[(levelEnd + 2)..];
        return true;
    }

    public static string Debug(string scope, string message)
    {
        return new PlateauLogEntry(scope, PlateauLogLevel.Debug, message).ToString();
    }

    public static string Info(string scope, string message)
    {
        return new PlateauLogEntry(scope, PlateauLogLevel.Info, message).ToString();
    }

    public static string Warning(string scope, string message)
    {
        return new PlateauLogEntry(scope, PlateauLogLevel.Warning, message).ToString();
    }

    public static string Error(string scope, string message)
    {
        return new PlateauLogEntry(scope, PlateauLogLevel.Error, message).ToString();
    }
}
