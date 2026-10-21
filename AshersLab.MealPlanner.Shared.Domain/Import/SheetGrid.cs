using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>One non-empty cell: 1-based row and column, its text, and whether the sheet stored it as a number.</summary>
public sealed record SheetCell(int Row, int Column, string Text, bool IsNumber)
{
    /// <summary>"B34" - what the review shows so the planner can find it in the sheet.</summary>
    public string Address => $"{SheetGrid.ColumnName(Column)}{Row}";
}

/// <summary>One worksheet as text. Formatting, formulas and merged cells are not read - only what each cell shows.</summary>
public sealed class SheetGrid
{
    private readonly Dictionary<(int Row, int Column), SheetCell> _cells;

    public SheetGrid(string name, IEnumerable<SheetCell> cells)
    {
        Name   = name;
        _cells = cells.Where(c => c.Text.Trim().Length > 0).ToDictionary(c => (c.Row, c.Column));
    }

    public string Name { get; }

    public IEnumerable<SheetCell> Cells => _cells.Values.OrderBy(c => c.Row).ThenBy(c => c.Column);

    public int LastRow => _cells.Count == 0 ? 0 : _cells.Keys.Max(k => k.Row);

    public SheetCell? At(int row, int column) => _cells.GetValueOrDefault((row, column));

    /// <summary>The cells of one row, left to right.</summary>
    public IEnumerable<SheetCell> Row(int row) => Cells.Where(c => c.Row == row);

    /// <summary>"A1"-style address to a 1-based column: "B" is 2, "AA" is 27.</summary>
    public static int ColumnNumber(string letters)
    {
        int column = 0;
        foreach (char c in letters)
            column = column * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return column;
    }

    public static string ColumnName(int column)
    {
        string name = "";
        for (; column > 0; column = (column - 1) / 26)
            name = (char)('A' + (column - 1) % 26) + name;
        return name;
    }

    /// <summary>A grid written in a test: one string per row, cells separated by '|'. Empty cells are empty strings.</summary>
    public static SheetGrid FromRows(string name, params string[] rows) =>
        new(name, rows.SelectMany((line, r) => line.Split('|').Select((text, c) => new SheetCell(r + 1, c + 1, text, false))));
}

/// <summary>
/// Reads the text of every cell of every sheet of an <c>.xlsx</c> - Excel's or a Google Sheets download. In the
/// browser, from the file the planner picked: the sheet is personal data, so it is never sent to the server.
/// </summary>
/// <remarks>
/// Only what the import needs: shared and inline strings (rich-text runs joined), numbers as the sheet stores them
/// (invariant culture - "14.5", not the "$14.50" a format would show), booleans and formula results. No package:
/// an <c>.xlsx</c> is a zip of XML, and both are in the base library every phone already has.
/// </remarks>
public static class XlsxReader
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Refuses anything bigger: the planner's sheet is tens of kilobytes, and a zip can claim gigabytes.</summary>
    public const long MaxPartBytes = 20 * 1024 * 1024;

    /// <summary>Every sheet in workbook order. Throws <see cref="InvalidDataException"/> when it is not an .xlsx.</summary>
    public static IReadOnlyList<SheetGrid> Read(byte[] file)
    {
        try
        {
            using ZipArchive zip = new(new MemoryStream(file, writable: false), ZipArchiveMode.Read);

            List<string> shared = zip.GetEntry("xl/sharedStrings.xml") is { } strings ? SharedStrings(strings) : [];
            Dictionary<string, string> targets = Relationships(zip.GetEntry("xl/_rels/workbook.xml.rels"));

            List<SheetGrid> sheets = [];
            foreach ((string name, string relId) in Sheets(Entry(zip, "xl/workbook.xml")))
            {
                if (!targets.TryGetValue(relId, out string? target))
                    continue;
                string path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                if (zip.GetEntry(path) is { } sheet)
                    sheets.Add(new SheetGrid(name, Cells(sheet, shared)));
            }
            return sheets;
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or FormatException)
        {
            throw new InvalidDataException("That is not a spreadsheet this can read. Download it as .xlsx and try again.", e);
        }
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string path) =>
        zip.GetEntry(path) ?? throw new InvalidDataException($"The file has no {path}.");

    private static XmlReader Open(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
            throw new InvalidDataException("The spreadsheet is too big.");
        return XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false });
    }

    private static List<string> SharedStrings(ZipArchiveEntry entry)
    {
        List<string> strings = [];
        using XmlReader xml = Open(entry);
        while (xml.ReadToFollowing("si", Main))
            strings.Add(StringItem(xml.ReadSubtree()));
        return strings;
    }

    /// <summary>The text of an <c>si</c> or <c>is</c>: its <c>t</c>, or its runs' <c>t</c>s joined - never the phonetic hints.</summary>
    private static string StringItem(XmlReader item)
    {
        using (item)
        {
            System.Text.StringBuilder text = new();
            int phonetic = -1;
            while (item.Read())
            {
                if (item.NodeType == XmlNodeType.Element && item.LocalName == "rPh")
                    phonetic = item.Depth;
                else if (item.NodeType == XmlNodeType.EndElement && item.LocalName == "rPh")
                    phonetic = -1;
                else if (phonetic < 0 && item.NodeType == XmlNodeType.Element && item.LocalName == "t" && item.NamespaceURI == Main)
                    text.Append(item.ReadElementContentAsString());
            }
            return text.ToString();
        }
    }

    private static Dictionary<string, string> Relationships(ZipArchiveEntry? entry)
    {
        Dictionary<string, string> targets = [];
        if (entry is null)
            return targets;
        using XmlReader xml = Open(entry);
        while (xml.ReadToFollowing("Relationship", PackageRels))
            if (xml.GetAttribute("Id") is { } id && xml.GetAttribute("Target") is { } target)
                targets[id] = target;
        return targets;
    }

    private static List<(string Name, string RelId)> Sheets(ZipArchiveEntry workbook)
    {
        List<(string, string)> sheets = [];
        using XmlReader xml = Open(workbook);
        while (xml.ReadToFollowing("sheet", Main))
            if (xml.GetAttribute("name") is { } name && xml.GetAttribute("id", Rels) is { } relId)
                sheets.Add((name, relId));
        return sheets;
    }

    private static List<SheetCell> Cells(ZipArchiveEntry sheet, List<string> shared)
    {
        List<SheetCell> cells = [];
        using XmlReader xml = Open(sheet);
        while (xml.ReadToFollowing("c", Main))
        {
            string? reference = xml.GetAttribute("r");
            string  type      = xml.GetAttribute("t") ?? "n";
            string? value     = null;
            string? inline    = null;

            using (XmlReader cell = xml.ReadSubtree())
            {
                cell.Read();
                while (cell.Read())
                {
                    if (cell.NodeType != XmlNodeType.Element)
                        continue;
                    if (cell.LocalName == "v")
                        value = cell.ReadElementContentAsString();
                    else if (cell.LocalName == "is")
                        inline = StringItem(cell.ReadSubtree());
                }
            }

            if (reference is null || Address(reference) is not { } at)
                continue;

            (string? text, bool number) = type switch
            {
                "s"         => (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < shared.Count ? shared[i] : null, false),
                "inlineStr" => (inline, false),
                "str"       => (value, false),
                "b"         => (value == "1" ? "TRUE" : "FALSE", false),
                "e"         => (null, false),
                _           => (value, value is not null),
            };
            if (text is not null)
                cells.Add(new SheetCell(at.Row, at.Column, text, number));
        }
        return cells;
    }

    private static (int Row, int Column)? Address(string reference)
    {
        int split = 0;
        while (split < reference.Length && char.IsAsciiLetter(reference[split]))
            split++;
        return split > 0 && int.TryParse(reference.AsSpan(split), NumberStyles.None, CultureInfo.InvariantCulture, out int row)
            ? (row, SheetGrid.ColumnNumber(reference[..split]))
            : null;
    }
}
