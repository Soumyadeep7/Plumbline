using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Plumbline.Api;

/// <summary>Lifecycle states of an escrow project.</summary>
public static class ProjectStatus
{
    public const string AwaitingPayment = "AWAITING_PAYMENT";
    public const string EscrowHeld = "ESCROW_HELD";
    public const string Completed = "COMPLETED";
    public const string Refunded = "REFUNDED";
}

public static class AssetKinds
{
    public const string Text = "text";
    public const string Image = "image";
}

public static class Limits
{
    public const int MaxTextChars = 150_000;
    public const int MaxRequirementsChars = 20_000;
    public const int MaxComplaintChars = 5_000;
    public const long MaxAssetBytes = 3_500_000;   // keeps images inside Claude's per-image size limit
    public const long MaxPreviewBytes = 400_000;
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromHours(24);
}

/// <summary>
/// One freelance delivery held by the platform. The unaltered asset stays in server memory;
/// clients only ever receive <see cref="PreviewContent"/> until payment is captured.
/// </summary>
public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The readable ID people see and share, such as BAKERY-HEADLINE-7K3Q. Set by ProjectStore.Add.</summary>
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string RequirementsText { get; set; } = "";
    public decimal PriceUsd { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    // The unaltered asset.
    public string AssetKind { get; set; } = AssetKinds.Text;
    public string? FileName { get; set; }
    public string? AssetText { get; set; }
    public byte[]? AssetBytes { get; set; }
    public string? AssetMediaType { get; set; }

    // Masked text, or a data URL holding the blurred and watermarked image.
    public string PreviewContent { get; set; } = "";

    // Escrow state.
    public string Status { get; set; } = ProjectStatus.AwaitingPayment;
    public List<string> PayPalOrderIds { get; } = new();
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset? ReviewDeadlineUtc { get; set; }

    // Claude output.
    public List<string>? RequirementsSummary { get; set; }
    public string? ComplaintReason { get; set; }
    public DisputeVerdict? Verdict { get; set; }

    /// <summary>Serialises every state change on this project (payment, dispute, refund).</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>The arbitrator's decision, parsed from Claude's JSON reply.</summary>
public sealed record DisputeVerdict(int MatchScore, string Verdict, string Reason)
{
    public const string Release = "RELEASE";
    public const string Refund = "REFUND";
}

/// <summary>Body of POST /api/projects/draft.</summary>
public sealed record ProjectDraftRequest(string? Text);

/// <summary>What Claude pulled out of a client's message. Unknown fields are empty strings.</summary>
public sealed record ProjectDraft(string Title, string Requirements, string Price, string Notes);

/// <summary>Body of POST /api/disputes/resolve/{projectId}.</summary>
public sealed record DisputeRequest(string? ComplaintReason);

/// <summary>One line of the activity log shown on the dashboard.</summary>
public sealed record ActivityEntry(DateTimeOffset At, string ProjectId, string ProjectTitle, string Kind, string Message);

public static class ActivityKinds
{
    public const string Created = "CREATED";
    public const string CheckoutStarted = "CHECKOUT_STARTED";
    public const string Paid = "PAID";
    public const string Released = "RELEASED";
    public const string Refunded = "REFUNDED";
}

/// <summary>Thread-safe in-memory project store and activity log. Data is lost when the API restarts.</summary>
public sealed class ProjectStore
{
    private const int MaxActivity = 200;
    private readonly ConcurrentDictionary<Guid, Project> _projects = new();
    private readonly ConcurrentQueue<ActivityEntry> _activity = new();

    public IEnumerable<Project> All => _projects.Values;

    public void Log(Project project, string kind, string message)
    {
        _activity.Enqueue(new ActivityEntry(DateTimeOffset.UtcNow, project.Code, project.Title, kind, message));
        while (_activity.Count > MaxActivity && _activity.TryDequeue(out _)) { }
    }

    /// <summary>Newest first.</summary>
    public List<ActivityEntry> RecentActivity(int count) =>
        _activity.Reverse().Take(count).ToList();

    // Letters and digits that cannot be confused when read aloud or retyped (no 0/O, 1/I/L).
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private static readonly HashSet<string> CodeStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "AN", "THE", "AND", "OR", "FOR", "OF", "TO", "IN", "ON", "WITH", "MY", "YOUR", "OUR", "NEW"
    };

    private readonly ConcurrentDictionary<string, Project> _byCode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a readable project ID from the project title, such as BAKERY-HEADLINE-7K3Q:
    /// up to two words from the title plus four random characters so it stays unique.
    /// Titles with no Latin letters or digits get PROJECT-XXXX.
    /// </summary>
    public static string MakeCode(string title)
    {
        var words = Regex.Matches(title.ToUpperInvariant(), "[A-Z0-9]+")
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !CodeStopWords.Contains(w))
            .Take(2)
            .Select(w => w.Length > 10 ? w[..10] : w)
            .ToList();

        var slug = words.Count == 0 ? "PROJECT" : string.Join("-", words);
        var suffix = string.Concat(Enumerable.Range(0, 4).Select(_ =>
            CodeAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
        return slug + "-" + suffix;
    }

    private static bool IsValidCode(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= 30 && Regex.IsMatch(code, "^[A-Z0-9]+(-[A-Z0-9]+)+$");

    /// <summary>
    /// Stores the project under a unique readable ID. A preferred ID (the one shown when Claude
    /// filled in the form) is kept when it is valid and still free; otherwise a new one is made
    /// from the title.
    /// </summary>
    public void Add(Project project, string? preferredCode = null)
    {
        var preferred = preferredCode?.Trim().ToUpperInvariant();
        if (IsValidCode(preferred) && _byCode.TryAdd(preferred!, project))
        {
            project.Code = preferred!;
        }
        else
        {
            while (true)
            {
                var code = MakeCode(project.Title);
                if (_byCode.TryAdd(code, project))
                {
                    project.Code = code;
                    break;
                }
            }
        }

        _projects[project.Id] = project;
    }

    /// <summary>Finds a project by its ID, in any letter case.</summary>
    public bool TryGet(string? key, [NotNullWhen(true)] out Project? project) =>
        _byCode.TryGetValue((key ?? "").Trim(), out project);
}
