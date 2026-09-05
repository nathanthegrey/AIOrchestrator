namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>The plan as it reads after one approved request was written into it.</summary>
/// <param name="PlanText">The whole file's new text. Equal to the input when nothing was written.</param>
/// <param name="LedgerRowRef">The ledger line's text — the handle everything downstream uses.</param>
/// <param name="OwnerRequestNumber">The row number given in the OWNER REQUESTS table, or 0 when no row was added.</param>
/// <param name="Changed">False when the request was already in the plan and nothing needed writing.</param>
public readonly record struct PlanRequestWrite(string PlanText, string LedgerRowRef, int OwnerRequestNumber, bool Changed);

/// <summary>
/// Writes ONE approved upstream request into a PLAN.md, exactly where a supervisor would have written
/// it by hand: a row in the <c>## OWNER REQUESTS</c> table, and a line at the end of the ledger.
///
/// <para>
/// BOTH, AND THE PAIR IS THE POINT. The table is the record of the ASK — append-only, in the owner's
/// own words, never renumbered — and it is deliberately invisible to <see cref="PlanLedger_Parser"/>,
/// so writing one cannot move the progress bar. But a request that only ever exists as a table row can
/// never be reported closed either: nothing about a prose "status" cell is machine-readable, and the
/// one closure signal this codebase has is a ledger marker turning <c>x</c>. So the ledger line is
/// what makes the round trip possible, and it is legitimate work in the denominator by construction —
/// decision 22's rule is that a ledger line must trace to an owner request, and this one traces to the
/// row written beside it in the same breath.
/// </para>
/// <para>
/// A PURE FUNCTION OVER THE FILE'S TEXT. It reads and returns strings and touches no disk, so every
/// shape a hand-written plan can be in — no OWNER REQUESTS section, a section with no table, a table
/// whose numbers skip, a plan that is nothing but a seed — is a test rather than a field report.
/// </para>
/// <para>
/// IT NEVER RENUMBERS AND NEVER DELETES. The next row number is one past the highest already there,
/// so a table whose rows were hand-edited keeps every reference other text makes to "#4".
/// </para>
/// </summary>
public static class PlanRequest_Writer
{
    /// <summary>The heading written when a plan has no OWNER REQUESTS section yet — the seed's own wording.</summary>
    public const string OWNER_REQUESTS_HEADING = "## OWNER REQUESTS — written the moment they arrive, in arrival order, never deleted";

    public const string TABLE_HEADER_ROW = "| # | when | what they asked for | status |";
    public const string TABLE_SEPARATOR_ROW = "|---|---|---|---|";

    /// <summary>
    /// The status a freshly-ingested request carries. It says what is TRUE FOR THE OWNER — the thing
    /// is in the plan and nobody has started it — rather than anything about a branch, which is the
    /// table's own rule. Whoever runs the orchestration edits it from here on.
    /// </summary>
    public static string Describe_InitialStatus(string requestId)
    {
        return $"approved upstream ({requestId}) — in the ledger, not started";
    }

    public static PlanRequestWrite Write_Request(string planText, ApprovedPlanRequest request, DateTime whenLocal)
    {
        var ledgerRowRef = Flatten(request.Title);

        if (ledgerRowRef.Length == 0)
            return new PlanRequestWrite(planText, ledgerRowRef, 0, Changed: false);

        List<string> lines = [.. (planText ?? string.Empty).Split('\n')];

        // ALREADY THERE means already written by an earlier tick whose state file never landed, or by
        // a supervisor who typed the same line. Either way the request is in the plan and adding a
        // second copy of it would be the one damage this class can do.
        var alreadyInLedger = Has_LedgerLine(lines, ledgerRowRef);

        var ownerRequestNumber = 0;

        if (!Has_OwnerRequestRow(lines, request.RequestId))
            ownerRequestNumber = Append_OwnerRequestRow(lines, request, whenLocal);

        if (!alreadyInLedger)
            Insert_LedgerLine(lines, ledgerRowRef);

        var changed = ownerRequestNumber > 0 || !alreadyInLedger;

        return new PlanRequestWrite(
            changed ? string.Join("\n", lines) : planText ?? string.Empty,
            ledgerRowRef,
            ownerRequestNumber,
            changed);
    }

    /// <summary>
    /// One line, one space between words. A ledger line that carried a newline would split into two
    /// lines the moment it was written, and the second half would not be a task line at all.
    /// </summary>
    public static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A pipe inside a cell ends the cell. Escaped, so the owner's own words survive the table.</summary>
    static string Escape_Cell(string text)
    {
        return Flatten(text).Replace("|", "\\|");
    }

    static bool Has_LedgerLine(IReadOnlyList<string> lines, string ledgerRowRef)
    {
        var progress = PlanLedger_Parser.Parse_OrNull(string.Join("\n", lines));

        return progress != null && progress.Lines.Any(line => line.Text == ledgerRowRef);
    }

    /// <summary>
    /// Keyed on the REQUEST ID, which the status cell carries — the words can be edited by whoever
    /// runs the orchestration (and are meant to be, as the status changes), so matching on them would
    /// re-add a row the first time somebody touched it.
    /// </summary>
    static bool Has_OwnerRequestRow(IReadOnlyList<string> lines, string requestId)
    {
        var (start, end) = Find_OwnerRequestsSection(lines);

        if (start < 0)
            return false;

        for (var index = start; index < end; index++)
        {
            if (Is_TableRow(lines[index]) && lines[index].Contains(requestId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Returns the row number written, and appends the section itself when the plan has none.</summary>
    static int Append_OwnerRequestRow(List<string> lines, ApprovedPlanRequest request, DateTime whenLocal)
    {
        var (start, end) = Find_OwnerRequestsSection(lines);

        if (start < 0)
        {
            Ensure_TrailingBlankLine(lines);
            lines.Add(OWNER_REQUESTS_HEADING);
            lines.Add(string.Empty);
            lines.Add(TABLE_HEADER_ROW);
            lines.Add(TABLE_SEPARATOR_ROW);
            lines.Add(Build_Row(1, request, whenLocal));

            return 1;
        }

        var lastRowIndex = -1;
        var highestNumber = 0;

        for (var index = start; index < end; index++)
        {
            if (!Is_TableRow(lines[index]))
                continue;

            lastRowIndex = index;
            highestNumber = Math.Max(highestNumber, Read_RowNumber(lines[index]));
        }

        var number = highestNumber + 1;

        if (lastRowIndex < 0)
        {
            // A section with no table yet: give it one, at the end of the section's own block.
            var insertAt = Skip_TrailingBlankLines(lines, start, end);

            lines.Insert(insertAt, TABLE_HEADER_ROW);
            lines.Insert(insertAt + 1, TABLE_SEPARATOR_ROW);
            lines.Insert(insertAt + 2, Build_Row(number, request, whenLocal));

            return number;
        }

        lines.Insert(lastRowIndex + 1, Build_Row(number, request, whenLocal));

        return number;
    }

    static string Build_Row(int number, ApprovedPlanRequest request, DateTime whenLocal)
    {
        return $"| {number} | {whenLocal:HH:mm} | {Escape_Cell(request.Words)} | {Describe_InitialStatus(request.RequestId)} |";
    }

    /// <summary>
    /// At the END OF THE LEDGER, which is the last line before the first non-ledger heading — not the
    /// end of the file, where it would land inside PARKED or OWNER REQUESTS and be invisible to the
    /// bar. That stranding is a defect <see cref="PlanShape_Validator"/> exists to complain about; a
    /// writer that produced it would be filing complaints against itself.
    /// </summary>
    static void Insert_LedgerLine(List<string> lines, string ledgerRowRef)
    {
        var taskLine = $"- [ ] {ledgerRowRef}";

        for (var index = 0; index < lines.Count; index++)
        {
            if (!PlanLedger_Sections.Opens_NonLedgerSection(lines[index]))
                continue;

            var insertAt = index;

            // Keep the blank line that separates the ledger from the heading below it.
            while (insertAt > 0 && lines[insertAt - 1].Trim().Length == 0)
                insertAt--;

            lines.Insert(insertAt, taskLine);
            return;
        }

        Ensure_TrailingBlankLine(lines);
        lines.Add(taskLine);
    }

    /// <summary>The section's line range as [start, end) — start is the heading line, end the next heading or EOF.</summary>
    static (int Start, int End) Find_OwnerRequestsSection(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var title = PlanLedger_Sections.Read_HeadingTitle_OrNull(lines[index]);

            if (title == null || !title.StartsWith(PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX, StringComparison.OrdinalIgnoreCase))
                continue;

            for (var end = index + 1; end < lines.Count; end++)
            {
                if (PlanLedger_Sections.Read_HeadingTitle_OrNull(lines[end]) != null)
                    return (index, end);
            }

            return (index, lines.Count);
        }

        return (-1, -1);
    }

    static bool Is_TableRow(string line)
    {
        return line.TrimStart().StartsWith('|');
    }

    /// <summary>The leading cell as a number, or 0 — a hand-written "| — |" must not stop the count.</summary>
    static int Read_RowNumber(string line)
    {
        var cells = line.Trim().Trim('|').Split('|');

        return cells.Length > 0 && int.TryParse(cells[0].Trim(), out var number) ? number : 0;
    }

    static int Skip_TrailingBlankLines(IReadOnlyList<string> lines, int start, int end)
    {
        var index = end;

        while (index > start + 1 && lines[index - 1].Trim().Length == 0)
            index--;

        return index;
    }

    static void Ensure_TrailingBlankLine(List<string> lines)
    {
        if (lines.Count > 0 && lines[^1].Trim().Length != 0)
            lines.Add(string.Empty);
    }
}
