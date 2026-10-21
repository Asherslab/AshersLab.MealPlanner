using System.IO.Compression;
using AshersLab.MealPlanner.Shared.Domain.Import;

namespace AshersLab.MealPlanner.Tests.Import;

public class SpreadsheetLayoutTests
{
    private static readonly string[] Stores = ["Costco", "Woolworths", "Woolies"];

    private static ParsedSheet Parsed() => SpreadsheetLayout.Parse(SyntheticSheet.Both(), Stores);

    [Fact]
    public void Finds_the_plan_weeks_with_their_slots_and_choices()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal([1, 2], sheet.Plan.Select(w => w.Number));
        PlanWeek one = sheet.Plan[0];
        Assert.Equal(["Monday", "Tuesday"], one.Days);
        Assert.Equal(["Breakfast", "Lunch", "Dinner"], one.Rows.Select(r => r.Slot));
        Assert.Equal("Lunch (at work)", one.Rows[1].Label);
        Assert.Equal([["Lamb korma"], ["soup"]], one.Rows[1].Days[0]!.Choices);
        // A blank plan cell is no entry, not an empty one.
        Assert.Null(sheet.Plan[1].Rows[0].Days[1]);
    }

    [Fact]
    public void Reads_meal_ideas_under_their_group_headings()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal(["Beef", "Chicken/egg", "Sides", "Fruit"], sheet.MealHeaders);
        Assert.Equal(
            ["Shepherd pie", "Lamb korma", "Garlic naan", "Pears", "Cottage pie", "Omelette", "Chips", "Shepherd pie"],
            sheet.MealIdeas.Select(m => m.Idea.Name));
        Assert.Equal("Chicken/egg", sheet.MealIdeas[1].Header);
        Assert.Equal(new SheetPlace("Plan", "C11"), sheet.MealIdeas[1].Place);
    }

    [Fact]
    public void Reads_costs_by_category_with_a_store_column_belonging_to_the_category_on_its_left()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal([("Meat", false), ("Dairy", false), ("Fruit", false), ("Pantry", false), ("Non-food etc", true)], sheet.CostHeaders);
        CostItemCell lamb = sheet.Costs.Single(c => c.Name == "Lamb mince");
        Assert.Equal("Meat", lamb.Header);
        Assert.Equal(new SheetPrice(2499, "2kg", "Costco"), lamb.Prices.Single().Price);
        Assert.Equal(new SheetPlace("Plan", "C16"), lamb.Prices.Single().Place);

        // A bare number in a store column is a price with no pack.
        Assert.Equal(new SheetPrice(1700, null, "Costco"), sheet.Costs.Single(c => c.Name == "Chicken thighs").Prices.Single().Price);
        Assert.Empty(sheet.Costs.Single(c => c.Name == "Beef mince").Prices);
    }

    [Fact]
    public void Reads_prices_in_the_cell_and_the_usual_store_beside_a_name()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal(new SheetPrice(950, "1kg", null), sheet.Costs.Single(c => c.Name == "Cheddar cheese").Prices.Single().Price);
        Assert.Equal(new SheetPrice(420, "500g", "Woolies"), sheet.Costs.Single(c => c.Name == "Korma paste").Prices.Single().Price);
        Assert.Equal("Woolies", sheet.Costs.Single(c => c.Name == "Pears").UsualStore);
        Assert.Equal("Eggs", sheet.Costs.Single(c => c.Header == "Dairy" && c.Name.StartsWith('E')).Name);
    }

    [Fact]
    public void The_non_food_column_is_household_items()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal(["Toilet rolls", "Paper towel"], sheet.Costs.Where(c => c.Household).Select(c => c.Name));
        Assert.Equal(new SheetPrice(1450, "24", null), sheet.Costs.Single(c => c.Name == "Toilet rolls").Prices.Single().Price);
    }

    [Fact]
    public void A_sheet_with_no_headings_is_a_flat_list_with_prices_beside_names()
    {
        ParsedSheet sheet = Parsed();

        Assert.Equal(["Eggs", "Peas", "Peas", "Potatoes", "Chives", "Chips", "Rice"], sheet.Flat.Select(f => f.Name));
        Assert.Equal(new SheetPrice(350, "1kg", null), sheet.Flat.Single(f => f.Name == "Rice").Prices.Single().Price);
    }

    [Fact]
    public void A_cell_that_cannot_be_placed_is_reported_never_dropped()
    {
        ParsedSheet sheet = SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "MEAL IDEAS|Beef",
            "|Tacos (beef)|stray",
            "COSTS|Meat|Price",
            "|Beef|twelve",
            "||9.99")], Stores);

        Assert.Equal(
            [("C2", "It is not under a meal group's heading."),
             ("C4", "Not a price this can read, for Beef."),
             ("C5", "A price with no name beside it.")],
            sheet.Unread.Select(u => (u.Place.Address, u.Why)));
        Assert.Single(sheet.Costs);
    }

    [Fact]
    public void Headings_are_found_wherever_they_are_and_in_any_case()
    {
        ParsedSheet sheet = SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "",
            "|meal ideas|Fish",
            "||Fish pie (fish)",
            "|Costs|Freezer",
            "||Fish fingers")], Stores);

        Assert.Equal("Fish pie", sheet.MealIdeas.Single().Idea.Name);
        Assert.Equal("Fish fingers", sheet.Costs.Single().Name);
        Assert.Empty(sheet.Flat);
    }
}

public class XlsxReaderTests
{
    [Fact]
    public void Reads_back_a_sheet_written_with_shared_strings_and_numbers()
    {
        SheetGrid written = new("Plan", [
            new SheetCell(1, 1, "COSTS", false), new SheetCell(1, 2, "Meat", false), new SheetCell(1, 3, "Costco", false),
            new SheetCell(2, 2, "Lamb mince", false), new SheetCell(2, 3, "24.99", true),
            new SheetCell(28, 27, "far away & <odd>", false),
        ]);

        IReadOnlyList<SheetGrid> read = XlsxReader.Read(SyntheticSheet.Xlsx([written, SyntheticSheet.List()]));

        Assert.Equal(["Plan", "List"], read.Select(s => s.Name));
        Assert.Equal(written.Cells, read[0].Cells);
        Assert.Equal("AA28", read[0].At(28, 27)!.Address);
        Assert.True(read[0].At(2, 3)!.IsNumber);
        Assert.Equal(SyntheticSheet.List().Cells, read[1].Cells);
    }

    [Fact]
    public void The_whole_synthetic_sheet_parses_the_same_from_an_xlsx_as_from_the_grid()
    {
        string[] stores = ["Costco", "Woolworths", "Woolies"];
        ParsedSheet fromGrid = SpreadsheetLayout.Parse(SyntheticSheet.Both(), stores);
        ParsedSheet fromFile = SpreadsheetLayout.Parse(XlsxReader.Read(SyntheticSheet.Xlsx(SyntheticSheet.Both())), stores);

        static string Shown(MealIdeaCell m) =>
            $"{m.Place} {m.Header} {m.Idea with { Ingredients = null }} {string.Join(",", (m.Idea.Ingredients ?? []).Select(i => i.Name + "/" + string.Join("/", i.Alternatives)))}";
        Assert.Equal(fromGrid.MealIdeas.Select(Shown), fromFile.MealIdeas.Select(Shown));
        Assert.Equal(8, fromFile.MealIdeas.Count);
        Assert.Equal(fromGrid.Costs.Select(c => (c.Name, c.Header, c.UsualStore, string.Join(",", c.Prices))),
            fromFile.Costs.Select(c => (c.Name, c.Header, c.UsualStore, string.Join(",", c.Prices))));
    }

    [Fact]
    public void Reads_rich_text_runs_inline_strings_booleans_and_formula_results_but_not_phonetic_hints()
    {
        MemoryStream file = new();
        using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            SyntheticSheet.Write(zip, "xl/workbook.xml",
                $"""<workbook xmlns="{main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="One" sheetId="1" r:id="rIdA"/></sheets></workbook>""");
            // Google's download names its parts with absolute paths.
            SyntheticSheet.Write(zip, "xl/_rels/workbook.xml.rels",
                """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdA" Type="worksheet" Target="/xl/worksheets/one.xml"/></Relationships>""");
            SyntheticSheet.Write(zip, "xl/sharedStrings.xml",
                $"""<sst xmlns="{main}"><si><r><rPr><b/></rPr><t>Lamb </t></r><r><t>mince</t></r><rPh><t>ignored</t></rPh></si></sst>""");
            SyntheticSheet.Write(zip, "xl/worksheets/one.xml",
                $"""<worksheet xmlns="{main}"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>Eggs 7.20/12</t></is></c><c r="C1" t="b"><v>1</v></c><c r="D1" t="str"><f>A1</f><v>Lamb mince</v></c><c r="E1"><f>1+2</f><v>3</v></c><c r="F1" t="e"><v>#DIV/0!</v></c><c r="G1" s="3"/></row></sheetData></worksheet>""");
        }

        SheetGrid sheet = XlsxReader.Read(file.ToArray()).Single();

        Assert.Equal(["Lamb mince", "Eggs 7.20/12", "TRUE", "Lamb mince", "3"], sheet.Cells.Select(c => c.Text));
        Assert.True(sheet.At(1, 5)!.IsNumber);
        Assert.Null(sheet.At(1, 6));
    }

    [Fact]
    public void Something_that_is_not_an_xlsx_is_refused_with_a_message()
    {
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => XlsxReader.Read("not a spreadsheet"u8.ToArray()));
        Assert.Contains(".xlsx", refused.Message);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("Z", 26)]
    [InlineData("AA", 27)]
    [InlineData("AZ", 52)]
    public void Column_letters_and_numbers_round_trip(string letters, int column)
    {
        Assert.Equal(column, SheetGrid.ColumnNumber(letters));
        Assert.Equal(letters, SheetGrid.ColumnName(column));
    }
}
