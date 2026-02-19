using System;

namespace SIPS.Emv.Models;

/// <summary>
/// Exception thrown when an error occurs during EMV QR decoding.
/// </summary>
public class QRDecodingException : Exception
{
    public string? Tag { get; }
    public int? Position { get; }
    public string? RawData { get; }

    public QRDecodingException(string message) : base(message)
    {
    }

    public QRDecodingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public QRDecodingException(string message, string? tag = null, int? position = null, string? rawData = null) 
        : base(BuildMessage(message, tag, position))
    {
        Tag = tag;
        Position = position;
        RawData = rawData;
    }

    private static string BuildMessage(string message, string? tag, int? position)
    {
        var details = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(tag)) details.Add($"Tag: {tag}");
        if (position.HasValue) details.Add($"Position: {position}");

        if (details.Count > 0)
        {
            return $"{message} ({string.Join(", ", details)})";
        }

        return message;
    }
}
