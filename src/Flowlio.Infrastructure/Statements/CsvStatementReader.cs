using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Flowlio.Application.Statements;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Reads delimited (CSV) statements into a <see cref="RawStatement"/>. Skips any leading metadata preamble
/// some banks prepend, auto-detects the delimiter when not hinted, and decodes the bytes heuristically
/// (UTF-8 with BOM, then strict UTF-8, falling back to Windows-1250 used by many Czech bank exports).
/// </summary>
internal sealed class CsvStatementReader : IStatementReader
{
    static CsvStatementReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public ImportFormat Format => ImportFormat.Csv;

    public RawStatement Read(Stream content, string fileName, ReaderOptions options)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var text = Decode(ms.ToArray(), options.Encoding);

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var (headerLineIndex, delimiter) = LocateHeader(lines, options);
        if (headerLineIndex < 0)
        {
            return new RawStatement
            {
                Diagnostics = [new ParseDiagnostic
                {
                    Severity = ParseSeverity.Error,
                    Message = "Nepodařilo se najít řádek se záhlavím sloupců.",
                }],
            };
        }

        var tableText = string.Join('\n', lines[headerLineIndex..]);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = true,
            DetectColumnCountChanges = false,
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = null,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
        };

        using var csv = new CsvReader(new StringReader(tableText), config);
        if (!csv.Read() || !csv.ReadHeader())
            return new RawStatement();

        var headers = csv.HeaderRecord ?? [];
        var rows = new List<RawRow>();
        while (csv.Read())
        {
            var cells = new string[headers.Length];
            for (var c = 0; c < headers.Length; c++)
                cells[c] = csv.GetField(c) ?? string.Empty;

            // csv.Parser.Row is 1-based within tableText, which started at headerLineIndex.
            rows.Add(new RawRow { Cells = cells, SourceLine = headerLineIndex + csv.Parser.Row });
        }

        return new RawStatement { Headers = headers, Rows = rows };
    }

    /// <summary>Finds the header row and delimiter by scanning for the first line whose cells contain at
    /// least two recognised header tokens, skipping any account-metadata preamble.</summary>
    private static (int Line, char Delimiter) LocateHeader(string[] lines, ReaderOptions options)
    {
        char[] candidates = options.Delimiter is { } d ? [d] : [';', ',', '\t'];

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var delim in candidates)
            {
                var cells = lines[i].Split(delim);
                if (cells.Length < 2)
                    continue;

                var matches = cells.Count(c => options.KnownHeaderTokens.Contains(StatementText.Normalize(c)));
                if (matches >= 2)
                    return (i, delim);
            }
        }

        // Fallback for unrecognised header spellings: first line that splits into a stable column count.
        for (var i = 0; i < lines.Length - 1; i++)
        {
            foreach (var delim in candidates)
            {
                var count = lines[i].Split(delim).Length;
                if (count >= 2 && lines[i + 1].Split(delim).Length == count)
                    return (i, delim);
            }
        }

        return (-1, ';');
    }

    private static string Decode(byte[] bytes, Encoding? preferred)
    {
        if (preferred is not null)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), preferred, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250).GetString(bytes);
        }
    }
}
