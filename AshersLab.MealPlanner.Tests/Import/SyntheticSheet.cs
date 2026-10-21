using System.IO.Compression;
using System.Security;
using System.Text;
using AshersLab.MealPlanner.Shared.Domain.Import;

namespace AshersLab.MealPlanner.Tests.Import;

/// <summary>
/// A made-up sheet laid out like the planner's: a plan, meal ideas and costs on the first sheet, a flat list on the
/// second. Written by hand to the real layout's shapes and traps; its cells are invented. The region headings the
/// parser looks for ("WEEK 1", "MEAL IDEAS", "COSTS"), the seeded category names and single grocery words ("Eggs",
/// "Rice") are the only text it shares with any real sheet - checked against the planner's on 2026-10-21.
/// </summary>
public static class SyntheticSheet
{
    public static SheetGrid Main() => SheetGrid.FromRows("Plan",
        "OUR GROCERIES",
        "WEEK 1|Monday|Tuesday|Week sum|Notes",
        "Breakfast|Toast, jam + fruit|Porridge|12.5|",
        "Lunch (at work)|Lamb korma/soup|Leftovers|200g lamb, 1 jar korma paste, |",
        "Dinner|Eating out|Shepherd pie",
        "",
        "WEEK 2|Monday|Tuesday",
        "Dinner|Lmab korma|",
        "",
        "MEAL IDEAS|Beef|Chicken+egg|Sides|Fruit",
        "|4x Shepherd pie (lamb mince, potatoes, peas, gravy)|6?x Lamb korma (chicken thighs, korma paste, rice, yoghurt|Garlic naan|Pears",
        "|Cottage pie (beef mince, potato)|2x Omelette (egg, cheese/ham, chives)|Popcorn?|",
        "|4x Shepherd pie (lamb mince)|||",
        "",
        "COSTS|Meat|Costco|Dairy|Fruit|Pantry|Household bits",
        "|Lamb mince|24.99/2kg|Swiss cheese $9.50/1kg|Pears (Woolies)|Korma paste 4.20/500g Woolies|Toilet rolls 14.50/24",
        "|Beef mince||Yoghurt||Rice|Paper towel",
        "|Chicken thighs|17|eggs 7.20/12||Gravy|",
        "|Ham||Shredded cheese||Popcorn|");

    public static SheetGrid List() => SheetGrid.FromRows("List",
        "Eggs",
        "peas",
        "Peas",
        "potatoes",
        "chives",
        "Popcorn?",
        "Rice|3.50/1kg");

    public static IReadOnlyList<SheetGrid> Both() => [Main(), List()];

    /// <summary>An .xlsx of <paramref name="sheets"/>, every string shared - as Excel writes it.</summary>
    public static byte[] Xlsx(IReadOnlyList<SheetGrid> sheets)
    {
        List<string> shared = [];
        int Share(string s)
        {
            int at = shared.IndexOf(s);
            if (at >= 0)
                return at;
            shared.Add(s);
            return shared.Count - 1;
        }

        MemoryStream file = new();
        using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder workbook = new("""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
            StringBuilder rels = new("""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
            for (int i = 0; i < sheets.Count; i++)
            {
                workbook.Append($"""<sheet name="{SecurityElement.Escape(sheets[i].Name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
                rels.Append($"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>""");

                StringBuilder sheet = new("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
                foreach (IGrouping<int, SheetCell> row in sheets[i].Cells.GroupBy(c => c.Row))
                {
                    sheet.Append($"""<row r="{row.Key}">""");
                    foreach (SheetCell cell in row)
                        sheet.Append(cell.IsNumber
                            ? $"""<c r="{cell.Address}"><v>{cell.Text}</v></c>"""
                            : $"""<c r="{cell.Address}" t="s"><v>{Share(cell.Text)}</v></c>""");
                    sheet.Append("</row>");
                }
                sheet.Append("</sheetData></worksheet>");
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", sheet.ToString());
            }
            Write(zip, "xl/workbook.xml", workbook.Append("</sheets></workbook>").ToString());
            Write(zip, "xl/_rels/workbook.xml.rels", rels.Append("</Relationships>").ToString());
            Write(zip, "xl/sharedStrings.xml",
                """<?xml version="1.0" encoding="UTF-8"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
                + string.Concat(shared.Select(s => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(s)}</t></si>"))
                + "</sst>");
        }
        return file.ToArray();
    }

    public static void Write(ZipArchive zip, string path, string text)
    {
        using StreamWriter writer = new(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
