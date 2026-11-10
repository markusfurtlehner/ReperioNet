namespace ReperioNet;

/// <summary>
/// Lists the groups of a <see cref="FieldKind.Keys"/> field that have at least one member matching
/// <see cref="Filter"/>, ordered by their newest matching member (PRD §4a). The field must be declared
/// with an <c>orderBy</c> number; the order is that number descending, then the member's internal row
/// descending, then the group key ascending (a total order, so cursors are exact).
/// </summary>
public sealed class GroupQuery
{
    /// <summary>The keys field whose values are the groups. Required.</summary>
    public required string GroupField { get; set; }

    /// <summary>Only members matching this filter count; <see langword="null"/> = all documents.</summary>
    public FieldFilter? Filter { get; set; }

    /// <summary>Groups per page (1..1000). Default: 50.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>The <see cref="GroupPage.Next"/> of the previous page; <see langword="null"/> = first page.</summary>
    public string? After { get; set; }

    /// <summary>Flag fields to count per group among the matching members (<see cref="GroupSummary.FlagCounts"/>).</summary>
    public IList<string> CountWhere { get; } = new List<string>();
}

/// <summary>One group of a <see cref="GroupPage"/>.</summary>
/// <param name="Key">The group key.</param>
/// <param name="NewestId">Id of the newest matching member.</param>
/// <param name="NewestOrder">The order number (e.g. date ticks) of the newest matching member.</param>
/// <param name="Count">Number of matching members.</param>
/// <param name="FlagCounts">Per requested flag field, the number of matching members where it is true.</param>
public sealed record GroupSummary(
    string Key,
    string NewestId,
    long NewestOrder,
    long Count,
    IReadOnlyDictionary<string, long> FlagCounts);

/// <summary>A page of groups.</summary>
/// <param name="Groups">The groups, newest first.</param>
/// <param name="Next">Opaque cursor for the next page; <see langword="null"/> when there is none.</param>
public sealed record GroupPage(IReadOnlyList<GroupSummary> Groups, string? Next);

/// <summary>Lists the members of one group key matching a filter, newest first (PRD §4a).</summary>
public sealed class MemberQuery
{
    /// <summary>The keys field. Required; declared with an <c>orderBy</c> number.</summary>
    public required string GroupField { get; set; }

    /// <summary>The group key. Required.</summary>
    public required string GroupKey { get; set; }

    /// <summary>Only members matching this filter are listed; <see langword="null"/> = all.</summary>
    public FieldFilter? Filter { get; set; }

    /// <summary>Members per page (1..1000). Default: 50.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>The <see cref="MemberPage{TMeta}.Next"/> of the previous page; <see langword="null"/> = first page.</summary>
    public string? After { get; set; }
}

/// <summary>One member of a <see cref="MemberPage{TMeta}"/>.</summary>
/// <param name="Id">The document id.</param>
/// <param name="Metadata">The stored metadata.</param>
/// <param name="Order">The order number (e.g. date ticks).</param>
public sealed record MemberHit<TMeta>(string Id, TMeta Metadata, long Order);

/// <summary>A page of group members.</summary>
/// <param name="Items">The members, newest first.</param>
/// <param name="Next">Opaque cursor for the next page; <see langword="null"/> when there is none.</param>
public sealed record MemberPage<TMeta>(IReadOnlyList<MemberHit<TMeta>> Items, string? Next);
