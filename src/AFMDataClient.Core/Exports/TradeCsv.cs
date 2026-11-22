using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>One trade as a row of the trades CSV. Time is UTC.</summary>
public sealed record TradeCsvRow(
    Guid Id,
    string Server,
    string Player,
    DateTime Time,
    TradeType Type,
    TradeOperation Operation,
    byte Quality,
    string ItemId,
    string ItemName,
    string Location,
    string FullLocation,
    int Amount,
    double UnitSilver,
    double TotalSilver,
    int? OrderAmount);

/// <summary>
/// The trades CSV of the desktop and mobile clients, so a file from either reads the same. The first 13 columns
/// are the desktop's original ones; new columns go at the end so spreadsheets built on older files keep working.
/// </summary>
public static class TradeCsv
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "Server", "Player", "Received", "Type", "Operation", "Quality",
        "Item", "Location", "Full Location", "Amount", "Unit Silver", "Total Silver", "Order Amount",
        "Trade ID", "Item ID", "Time (UTC)"
    ];

    public static string Header(string delimiter) => string.Join(delimiter, Columns);

    /// <summary>One row. <paramref name="culture"/> comes from <see cref="CsvExportOptions.CreateFormattingCulture"/>.</summary>
    public static string Line(TradeCsvRow row, string delimiter, CultureInfo culture)
    {
        return string.Join(delimiter, new[]
        {
            Escape(row.Server, delimiter),
            Escape(row.Player, delimiter),
            // Received keeps its original look: the UTC time in the culture's format, without a zone.
            Escape(row.Time.ToString(culture), delimiter),
            Escape(TypeText(row.Type), delimiter),
            Escape(OperationText(row.Operation), delimiter),
            Escape(ItemQuality.Format(row.Quality), delimiter),
            Escape(row.ItemName, delimiter),
            Escape(row.Location, delimiter),
            Escape(row.FullLocation, delimiter),
            row.Amount.ToString(culture),
            row.Amount == 0 && row.UnitSilver == 0 ? "" : row.UnitSilver.ToString("F2", culture),
            row.TotalSilver.ToString("F0", culture),
            row.OrderAmount?.ToString(culture) ?? "",
            row.Id == Guid.Empty ? "" : row.Id.ToString("D"),
            Escape(row.ItemId, delimiter),
            FormatUtc(row.Time)
        });
    }

    /// <summary>Writes the header and the rows as UTF-8 with a byte order mark, which spreadsheet apps read as UTF-8.</summary>
    public static async Task WriteAsync(Stream stream, IEnumerable<TradeCsvRow> rows, CsvExportOptions options, CancellationToken cancellationToken = default)
    {
        var culture = options.CreateFormattingCulture();
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(Header(options.Delimiter));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Line(row, options.Delimiter, culture));
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Quotes a field that holds the delimiter, a quote or a line break.</summary>
    public static string Escape(string? value, string delimiter)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    /// <summary>ISO 8601 UTC to the second, such as 2026-11-21T18:42:10Z. Times without a kind are UTC.</summary>
    public static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string TypeText(TradeType type) => type switch
    {
        TradeType.Instant => "Instant",
        TradeType.Order => "Order",
        _ => "Unknown"
    };

    private static string OperationText(TradeOperation operation) => operation switch
    {
        TradeOperation.Buy => "Buy",
        TradeOperation.Sell => "Sell",
        _ => "Unknown"
    };
}
