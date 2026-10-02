using System.Globalization;
using System.Text;

namespace PrimeScore.Modules.Catalysts.Sources;

/// <summary>One property line of a VEVENT, unfolded.</summary>
/// <param name="Name">Upper case (<c>DTSTART</c>).</param>
/// <param name="Parameters">The text between the name and the colon, without the leading semicolon (<c>VALUE=DATE-TIME</c>); empty when none.</param>
/// <param name="RawValue">After the colon, still escaped.</param>
internal sealed record IcsProperty(string Name, string Parameters, string RawValue);

/// <summary>One VEVENT: its properties by name (the first occurrence of each), nested components left out.</summary>
internal sealed class IcsEvent(IReadOnlyDictionary<string, IcsProperty> properties)
{
    public IReadOnlyDictionary<string, IcsProperty> Properties => properties;

    /// <summary>A TEXT property unescaped; null when the event has none.</summary>
    public string? Text(string name) => properties.TryGetValue(name, out var property) ? Ics.Unescape(property.RawValue) : null;

    /// <summary>
    /// DTSTART when it is a UTC date-time (<c>20261029T123000Z</c>, with or without
    /// <c>VALUE=DATE-TIME</c>); null otherwise. A floating time, a <c>TZID</c> time or a date is not
    /// read, so an instant is never guessed.
    /// </summary>
    public DateTimeOffset? StartUtc
    {
        get
        {
            if (!properties.TryGetValue("DTSTART", out var start)
                || start.Parameters.Split(';').Any(parameter => parameter.Length > 0 && !parameter.Equals("VALUE=DATE-TIME", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return DateTimeOffset.TryParseExact(
                start.RawValue, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
                ? instant
                : null;
        }
    }
}

/// <summary>
/// The subset of RFC 5545 the BEA release calendar uses: CRLF (or LF) lines, continuation lines
/// that start with one space or tab and are joined before anything else is read (an escape can be
/// split across a fold), VEVENT components and TEXT escapes (<c>\,</c> <c>\;</c> <c>\\</c> <c>\n</c>).
/// Everything else (time zones, recurrence, alarms) is ignored; an event that would need it has no
/// <see cref="IcsEvent.StartUtc"/> and is rejected by its parser.
/// </summary>
internal static class Ics
{
    /// <summary>The VEVENTs in file order.</summary>
    /// <exception cref="InvalidOperationException">The text is not an iCalendar file (layout not recognised).</exception>
    public static IReadOnlyList<IcsEvent> ReadEvents(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = Unfold(text);
        if (!lines.Any(line => line.Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
        {
            throw CalendarText.LayoutNotRecognised("not an iCalendar file (no BEGIN:VCALENDAR)");
        }

        var events = new List<IcsEvent>();
        Dictionary<string, IcsProperty>? current = null;
        var nested = 0;
        foreach (var line in lines)
        {
            var property = Property(line);
            if (property is null)
            {
                continue;
            }

            if (property.Name == "BEGIN")
            {
                if (current is not null)
                {
                    nested++;
                }
                else if (property.RawValue.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    current = new Dictionary<string, IcsProperty>(StringComparer.Ordinal);
                }
            }
            else if (property.Name == "END" && current is not null)
            {
                if (nested > 0)
                {
                    nested--;
                }
                else
                {
                    events.Add(new IcsEvent(current));
                    current = null;
                }
            }
            else if (current is not null && nested == 0)
            {
                current.TryAdd(property.Name, property);
            }
        }

        return events;
    }

    /// <summary>RFC 5545 TEXT unescaping: <c>\,</c> <c>\;</c> <c>\\</c>, and <c>\n</c> or <c>\N</c> as a line break.</summary>
    public static string Unescape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                var next = value[++index];
                text.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                text.Append(value[index]);
            }
        }

        return text.ToString();
    }

    /// <summary>Logical lines: a line starting with a space or tab continues the previous one without that character.</summary>
    public static IReadOnlyList<string> Unfold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        foreach (var physical in text.TrimStart('﻿').Split('\n'))
        {
            var line = physical.TrimEnd('\r');
            if (line.Length > 0 && line[0] is ' ' or '\t' && lines.Count > 0)
            {
                lines[^1] += line[1..];
            }
            else
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary><c>NAME;PARAM=V:value</c>; null for a line without a colon. A colon inside a quoted parameter value is not the separator.</summary>
    private static IcsProperty? Property(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ':' && !quoted)
            {
                var head = line[..index];
                var semicolon = head.IndexOf(';', StringComparison.Ordinal);
                var name = (semicolon < 0 ? head : head[..semicolon]).Trim().ToUpperInvariant();
                return name.Length == 0 ? null : new IcsProperty(name, semicolon < 0 ? "" : head[(semicolon + 1)..], line[(index + 1)..]);
            }
        }

        return null;
    }
}
