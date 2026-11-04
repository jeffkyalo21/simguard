using System.Text;

namespace SimGuard.Api.Imports;

// A problem with the file as a whole (encoding, binary content, quoting,
// ragged rows, size). The import endpoint turns it into a 400.
public sealed class CsvFormatException(string message) : Exception(message);

// One data row. RowNumber counts records from the header (row 1), so the
// first data row is row 2, as in a spreadsheet.
public sealed record CsvRecord(int RowNumber, string[] Values);

// Streaming RFC 4180 reader: comma separated, fields optionally in double
// quotes ("" inside quotes is a quote), quoted fields may contain commas and
// line breaks, records end in CRLF, LF or CR. The input must be UTF-8 (a
// BOM is allowed). Cells are returned as plain text, trimmed; nothing is
// ever interpreted (no formulas, no type guessing). Empty lines are skipped.
public sealed class CsvReader
{
    public const int MaxCellLength = 4096;
    public const int MaxFieldsPerRecord = 64;

    private readonly TextReader _reader;
    private readonly char[] _buffer = new char[64 * 1024];
    private int _length;
    private int _position;
    private bool _atStart = true;
    private bool _endOfFile;
    private int _recordNumber;

    private CsvReader(TextReader reader)
    {
        _reader = reader;
    }

    // Strict UTF-8: invalid bytes throw instead of becoming U+FFFD.
    public static CsvReader Open(Stream stream) =>
        new(new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024));

    // Returns the next non-empty record, or null at the end of the file.
    public async Task<CsvRecord?> ReadRecordAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var fields = await ReadFieldsAsync(cancellationToken);

            if (fields is null)
            {
                return null;
            }

            _recordNumber++;

            // A line with nothing on it.
            if (fields.Count == 1 && fields[0].Length == 0)
            {
                continue;
            }

            return new CsvRecord(_recordNumber, fields.Select(f => f.Trim()).ToArray());
        }
    }

    private async Task<List<string>?> ReadFieldsAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureDataAsync(cancellationToken))
        {
            return null;
        }

        var fields = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var afterQuote = false;
        var row = _recordNumber + 1;

        while (true)
        {
            if (!await EnsureDataAsync(cancellationToken))
            {
                if (inQuotes)
                {
                    throw new CsvFormatException($"Row {row}: a quoted value is not closed before the end of the file.");
                }

                AddField(fields, cell, row);
                return fields;
            }

            var c = _buffer[_position++];

            if (c == '\0')
            {
                throw new CsvFormatException("The file contains binary data (a NUL character). Upload a UTF-8 CSV file.");
            }

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (await EnsureDataAsync(cancellationToken) && _buffer[_position] == '"')
                    {
                        _position++;
                        Append(cell, '"', row);
                    }
                    else
                    {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                else
                {
                    Append(cell, c, row);
                }

                continue;
            }

            switch (c)
            {
                case ',':
                    AddField(fields, cell, row);
                    afterQuote = false;
                    break;
                case '\r':
                    if (await EnsureDataAsync(cancellationToken) && _buffer[_position] == '\n')
                    {
                        _position++;
                    }

                    AddField(fields, cell, row);
                    return fields;
                case '\n':
                    AddField(fields, cell, row);
                    return fields;
                case '"' when !afterQuote && cell.ToString().Trim().Length == 0:
                    // Spaces before an opening quote are dropped.
                    cell.Clear();
                    inQuotes = true;
                    break;
                default:
                    if (afterQuote)
                    {
                        if (char.IsWhiteSpace(c))
                        {
                            break;
                        }

                        throw new CsvFormatException(
                            $"Row {row}: unexpected text after a closing quote. Put quotes around the whole value and write a quote inside it as \"\".");
                    }

                    if (c == '"')
                    {
                        throw new CsvFormatException(
                            $"Row {row}: a quote inside an unquoted value. Put quotes around the whole value and write a quote inside it as \"\".");
                    }

                    Append(cell, c, row);
                    break;
            }
        }
    }

    private static void Append(StringBuilder cell, char c, int row)
    {
        if (cell.Length >= MaxCellLength)
        {
            throw new CsvFormatException($"Row {row}: a value is longer than {MaxCellLength} characters.");
        }

        cell.Append(c);
    }

    private static void AddField(List<string> fields, StringBuilder cell, int row)
    {
        if (fields.Count >= MaxFieldsPerRecord)
        {
            throw new CsvFormatException($"Row {row}: more than {MaxFieldsPerRecord} values.");
        }

        fields.Add(cell.ToString());
        cell.Clear();
    }

    private async ValueTask<bool> EnsureDataAsync(CancellationToken cancellationToken)
    {
        if (_position < _length)
        {
            return true;
        }

        if (_endOfFile)
        {
            return false;
        }

        try
        {
            _length = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new CsvFormatException("The file is not valid UTF-8 text. Save it as CSV UTF-8 and upload it again.");
        }

        _position = 0;

        if (_length == 0)
        {
            _endOfFile = true;
            return false;
        }

        if (_atStart)
        {
            _atStart = false;

            if (_buffer[0] == '﻿')
            {
                _position = 1;
                return await EnsureDataAsync(cancellationToken);
            }
        }

        return true;
    }
}
