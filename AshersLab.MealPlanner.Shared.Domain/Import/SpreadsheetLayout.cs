using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>Where a value came from, for the review: "Sheet1 B34".</summary>
public sealed record SheetPlace(string Sheet, string Address)
{
    public override string ToString() => $"{Sheet} {Address}";
}

/// <summary>A meal idea, under its column's heading (the meal group).</summary>
public sealed record MealIdeaCell(SheetPlace Place, string Header, MealIdea Idea);

/// <summary>
/// A price beside an ingredient - in its cell, or in a price column next to it. <paramref name="Id"/> is stable for one
/// sheet ("Sheet1 J46 0"), so the review's answers about it survive re-reading the database.
/// </summary>
public sealed record PriceCell(string Id, SheetPlace Place, SheetPrice Price);

/// <summary>A shopping-category cell. <paramref name="Household"/> for the non-food column: a product, not an ingredient.</summary>
public sealed record CostItemCell(SheetPlace Place, string Header, bool Household, string Name, IReadOnlyList<PriceCell> Prices, string? UsualStore);

/// <summary>A name from the flat ingredient list (the second sheet), with any prices on its row.</summary>
public sealed record FlatItemCell(SheetPlace Place, string Name, IReadOnlyList<PriceCell> Prices);

public sealed record PlanRow(string Label, string Slot, IReadOnlyList<PlanCell?> Days);

public sealed record PlanWeek(int Number, IReadOnlyList<string> Days, IReadOnlyList<PlanRow> Rows);

/// <summary>A cell the import could not place. Shown in the review, never dropped silently.</summary>
public sealed record UnreadCell(SheetPlace Place, string Text, string Why);

/// <summary>Everything the planner's sheet holds, as read - before it is compared with anything in the app.</summary>
public sealed record ParsedSheet(
    IReadOnlyList<string> MealHeaders,
    IReadOnlyList<MealIdeaCell> MealIdeas,
    IReadOnlyList<(string Header, bool Household)> CostHeaders,
    IReadOnlyList<CostItemCell> Costs,
    IReadOnlyList<FlatItemCell> Flat,
    IReadOnlyList<PlanWeek> Plan,
    IReadOnlyList<UnreadCell> Unread)
{
    public bool IsEmpty => MealIdeas.Count == 0 && Costs.Count == 0 && Flat.Count == 0 && Plan.Count == 0;
}

/// <summary>
/// Finds the planner's regions in the sheet by their headings, wherever they are: "WEEK 1".."WEEK n" (the plan),
/// "MEAL IDEAS" (one column per meal group) and "COSTS" (one column per shopping category). A region runs from its
/// heading to the next heading. A sheet with none of them is a flat list of ingredients, one per row.
/// </summary>
/// <remarks>
/// <b>Prices</b> can be in three places, all read: after the name in its cell ("Toilet rolls 14.50/24"); in a column
/// beside a category whose heading is a store ("Costco", "Woolies") or "Price"/"$" - each such column belongs to the
/// category column to its left; or in the cells after a name on the flat list's row. A price column whose heading is a
/// store gives its prices that store; otherwise a store word in the price itself does ("4.50 Costco"), or the review
/// asks.
/// </remarks>
public static partial class SpreadsheetLayout
{
    [GeneratedRegex(@"^week\s*(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeekHeading();

    private enum Region { Week, MealIdeas, Costs }

    private static (Region Region, int Week)? Heading(string text)
    {
        string normal = Names.Normalise(text);
        if (WeekHeading().Match(normal) is { Success: true } week)
            return (Region.Week, int.Parse(week.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture));
        return ImportNames.Key(normal) switch
        {
            "meal idea" or "meal ideas" => (Region.MealIdeas, 0),
            "cost" or "costs"           => (Region.Costs, 0),
            _                           => null,
        };
    }

    private static readonly HashSet<string> PriceHeadings = new(StringComparer.Ordinal) { "price", "cost", "", "each", "pack price" };

    /// <summary>
    /// Reads every sheet. <paramref name="storeWords"/> are the words a price or a heading may use for a store - the
    /// app's store names and <see cref="SheetText.StoreAliases"/>.
    /// </summary>
    public static ParsedSheet Parse(IReadOnlyList<SheetGrid> sheets, IReadOnlyCollection<string> storeWords)
    {
        List<string>                     mealHeaders = [];
        List<MealIdeaCell>               ideas       = [];
        List<(string, bool)>             costHeaders = [];
        List<CostItemCell>               costs       = [];
        List<FlatItemCell>               flat        = [];
        List<PlanWeek>                   plan        = [];
        List<UnreadCell>                 unread      = [];

        foreach (SheetGrid sheet in sheets)
        {
            List<(SheetCell Cell, Region Region, int Week)> headings =
            [
                .. sheet.Cells
                    .Select(c => (Cell: c, Found: c.IsNumber ? null : Heading(c.Text)))
                    .Where(h => h.Found is not null)
                    .Select(h => (h.Cell, h.Found!.Value.Region, h.Found.Value.Week)),
            ];

            if (headings.Count == 0)
            {
                ReadFlat(sheet, storeWords, flat, unread);
                continue;
            }

            for (int i = 0; i < headings.Count; i++)
            {
                (SheetCell at, Region region, int week) = headings[i];
                int last = i + 1 < headings.Count ? headings[i + 1].Cell.Row - 1 : sheet.LastRow;
                switch (region)
                {
                    case Region.Week:
                        plan.Add(ReadWeek(sheet, at, week, last));
                        break;
                    case Region.MealIdeas:
                        ReadMealIdeas(sheet, at, last, mealHeaders, ideas, unread);
                        break;
                    case Region.Costs:
                        ReadCosts(sheet, at, last, storeWords, costHeaders, costs, unread);
                        break;
                }
            }
        }

        return new ParsedSheet(mealHeaders, ideas, costHeaders, costs, flat, plan, unread);
    }

    private static SheetPlace Place(SheetGrid sheet, SheetCell cell) => new(sheet.Name, cell.Address);

    private static PlanWeek ReadWeek(SheetGrid sheet, SheetCell heading, int number, int last)
    {
        List<SheetCell> days = [.. sheet.Row(heading.Row).Where(c => c.Column > heading.Column)];
        List<PlanRow>   rows = [];
        for (int row = heading.Row + 1; row <= last; row++)
        {
            if (sheet.At(row, heading.Column) is not { } label)
                continue;
            rows.Add(new PlanRow(
                Names.Normalise(label.Text),
                SheetText.SlotName(label.Text),
                [.. days.Select(d => sheet.At(row, d.Column) is { } cell ? SheetText.PlanCell(cell.Text) : null)]));
        }
        return new PlanWeek(number, [.. days.Select(d => Names.Normalise(d.Text))], rows);
    }

    private static void ReadMealIdeas(SheetGrid sheet, SheetCell heading, int last, List<string> headers, List<MealIdeaCell> ideas, List<UnreadCell> unread)
    {
        Dictionary<int, string> columns = sheet.Row(heading.Row)
            .Where(c => c.Column > heading.Column)
            .ToDictionary(c => c.Column, c => Names.Normalise(c.Text));
        headers.AddRange(columns.Values.Where(h => !headers.Any(known => Names.Same(known, h))));

        foreach (SheetCell cell in sheet.Cells.Where(c => c.Row > heading.Row && c.Row <= last && c.Column >= heading.Column))
        {
            if (!columns.TryGetValue(cell.Column, out string? header))
            {
                unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "It is not under a meal group's heading."));
                continue;
            }

            MealIdea idea = SheetText.MealIdea(cell.Text);
            if (idea.Name.Length == 0)
                unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "It has no meal name."));
            else
                ideas.Add(new MealIdeaCell(Place(sheet, cell), header, idea));
        }
    }

    private static bool IsHousehold(string header)
    {
        string key = ImportNames.Key(header);
        return key.Contains("non food", StringComparison.Ordinal) || key.Contains("nonfood", StringComparison.Ordinal)
               || key.Contains("household", StringComparison.Ordinal);
    }

    /// <summary>The store a price column's heading names - "Costco", "Woolies price" - or null.</summary>
    private static string? HeadingStore(string header, IReadOnlyCollection<string> storeWords) =>
        storeWords
            .OrderByDescending(w => w.Length)
            .FirstOrDefault(w => (" " + ImportNames.Key(header) + " ").Contains(" " + ImportNames.Key(w) + " ", StringComparison.Ordinal));

    private static void ReadCosts(
        SheetGrid sheet, SheetCell heading, int last, IReadOnlyCollection<string> storeWords,
        List<(string, bool)> headers, List<CostItemCell> costs, List<UnreadCell> unread)
    {
        // Each column right of the heading is a category, or a price column belonging to the category on its left.
        int lastColumn = sheet.Cells.Where(c => c.Row >= heading.Row && c.Row <= last).Select(c => c.Column).DefaultIfEmpty(0).Max();
        Dictionary<int, (string Header, bool Household)> categories = [];
        Dictionary<int, (int Owner, string? Store)>      priceColumns = [];
        int owner = 0;
        for (int column = heading.Column + 1; column <= lastColumn; column++)
        {
            string header = Names.Normalise(sheet.At(heading.Row, column)?.Text);
            string? store = header.Length > 0 ? HeadingStore(header, storeWords) : null;
            if (store is not null || PriceHeadings.Contains(ImportNames.Key(header)) || header == "$")
            {
                if (owner > 0)
                    priceColumns[column] = (owner, store);
                continue;
            }

            categories[column] = (header, IsHousehold(header));
            owner = column;
            if (!headers.Any(h => Names.Same(h.Item1, header)))
                headers.Add((header, IsHousehold(header)));
        }

        for (int row = heading.Row + 1; row <= last; row++)
        {
            foreach (SheetCell cell in sheet.Row(row).Where(c => c.Column > heading.Column))
            {
                if (priceColumns.TryGetValue(cell.Column, out (int Owner, string? Store) priceColumn))
                {
                    if (sheet.At(row, priceColumn.Owner) is null)
                        unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "A price with no name beside it."));
                    continue;
                }
                if (!categories.TryGetValue(cell.Column, out (string Header, bool Household) category))
                {
                    unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "It is not under a category's heading."));
                    continue;
                }
                if (cell.IsNumber)
                {
                    unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "A number where a name should be."));
                    continue;
                }

                (string name, SheetPrice? inline, string? usualStore) = SheetText.CostItem(cell.Text, storeWords);
                if (name.Length == 0)
                {
                    unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "It has no name."));
                    continue;
                }

                List<PriceCell> prices = [];
                SheetPlace      place  = Place(sheet, cell);
                if (inline is not null)
                    prices.Add(new PriceCell($"{place} 0", place, inline));

                foreach ((int column, (int priceOwner, string? store)) in priceColumns.Where(p => p.Value.Owner == cell.Column).OrderBy(p => p.Key))
                {
                    if (sheet.At(row, column) is not { } priceCell)
                        continue;
                    SheetPlace pricePlace = Place(sheet, priceCell);
                    if (SheetText.Price(priceCell.Text, storeWords, alone: true) is { } price)
                        prices.Add(new PriceCell($"{pricePlace} 0", pricePlace, store is null ? price : price with { Store = store }));
                    else
                        unread.Add(new UnreadCell(pricePlace, priceCell.Text, $"Not a price this can read, for {name}."));
                }

                costs.Add(new CostItemCell(place, category.Header, category.Household, name, prices, usualStore));
            }
        }
    }

    private static void ReadFlat(SheetGrid sheet, IReadOnlyCollection<string> storeWords, List<FlatItemCell> flat, List<UnreadCell> unread)
    {
        foreach (IGrouping<int, SheetCell> row in sheet.Cells.GroupBy(c => c.Row))
        {
            SheetCell? first = row.FirstOrDefault(c => !c.IsNumber);
            if (first is null)
            {
                foreach (SheetCell cell in row)
                    unread.Add(new UnreadCell(Place(sheet, cell), cell.Text, "A number with no name beside it."));
                continue;
            }

            (string name, SheetPrice? inline, _) = SheetText.CostItem(first.Text, storeWords);
            SheetPlace      place  = Place(sheet, first);
            List<PriceCell> prices = inline is null ? [] : [new PriceCell($"{place} 0", place, inline)];
            foreach (SheetCell cell in row.Where(c => c.Column != first.Column))
            {
                SheetPlace at = Place(sheet, cell);
                if (cell.Column > first.Column && SheetText.Price(cell.Text, storeWords, alone: true) is { } price)
                    prices.Add(new PriceCell($"{at} 0", at, price));
                else
                    unread.Add(new UnreadCell(at, cell.Text, $"Not a price this can read, for {name}."));
            }

            if (name.Length > 0)
                flat.Add(new FlatItemCell(place, name, prices));
        }
    }
}
