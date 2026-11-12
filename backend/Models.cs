using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

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
public sealed record ActivityEntry(DateTimeOffset At, Guid ProjectId, string ProjectTitle, string Kind, string Message);

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
        _activity.Enqueue(new ActivityEntry(DateTimeOffset.UtcNow, project.Id, project.Title, kind, message));
        while (_activity.Count > MaxActivity && _activity.TryDequeue(out _)) { }
    }

    /// <summary>Newest first.</summary>
    public List<ActivityEntry> RecentActivity(int count) =>
        _activity.Reverse().Take(count).ToList();

    public void Add(Project project) => _projects[project.Id] = project;

    public bool TryGet(Guid id, [NotNullWhen(true)] out Project? project) =>
        _projects.TryGetValue(id, out project);
}
