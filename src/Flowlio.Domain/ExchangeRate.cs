using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// A daily foreign-exchange rate to Czech koruna, as published by the Czech National Bank (ČNB).
/// Stored normalized to <see cref="CzkPerUnit"/> = CZK for one unit of <see cref="Currency"/>
/// (ČNB quotes e.g. "100 HUF = 6.5 CZK"; we divide out the amount). CZK itself is implicitly 1 and
/// is never stored. Conversion between two non-CZK currencies pivots through CZK.
/// </summary>
public class ExchangeRate : Entity
{
    /// <summary>ISO 4217 code of the foreign currency (e.g. "EUR"), upper-case.</summary>
    public required string Currency { get; set; }

    /// <summary>The fixing date the rate applies to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>How many CZK equal one unit of <see cref="Currency"/> on <see cref="Date"/>.</summary>
    public decimal CzkPerUnit { get; set; }
}
