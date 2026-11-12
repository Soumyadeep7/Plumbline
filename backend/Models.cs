using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    /// <summary>The readable ID people see and share, such as BAKERY-LANDING-7K3Q. Set by ProjectStore.Add.</summary>
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

    // The freelancer account that uploaded the work, and the client account that paid for it.
    public Guid? UploadedById { get; set; }
    public string UploadedBy { get; set; } = "";
    public Guid? PaidById { get; set; }
    public string PaidBy { get; set; } = "";
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

/// <summary>The two kinds of account. Freelancers upload work; clients pay and dispute.</summary>
public static class Roles
{
    public const string Freelancer = "FREELANCER";
    public const string Client = "CLIENT";
}

public sealed class UserAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = Roles.Freelancer;
    public byte[] Salt { get; init; } = Array.Empty<byte>();
    public byte[] PasswordHash { get; init; } = Array.Empty<byte>();
}

public sealed record SignUpRequest(string? Name, string? Email, string? Password, string? Role);
public sealed record LogInRequest(string? Email, string? Password, string? Record);
public sealed record RememberedRequest(string? Record);

/// <summary>A saved account as the browser keeps it: everything needed to log in again after a restart.</summary>
public sealed record SignedAccountRecord(string Kind, Guid Id, string Name, string Email, string Role, string Salt, string Hash, long Exp);

/// <summary>What the session cookie carries, so a sign-in survives server restarts.</summary>
public sealed record SignedSession(string Kind, Guid Id, string Name, string Email, string Role, long Exp);

/// <summary>
/// Email-and-password accounts. The server keeps them in memory, which the free host wipes on
/// every restart, so each browser also keeps a signed record of the accounts used in it. The
/// record is signed with a key derived from a server secret, so the browser cannot alter it, and
/// it lets that browser log in again (or switch with one click) after a restart. Passwords are
/// stored only as salted PBKDF2 hashes. The session cookie is signed the same way.
/// </summary>
public sealed class AccountStore
{
    public const string CookieName = "plumbline_session";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan RememberLifetime = TimeSpan.FromDays(30);
    private const int Iterations = 100_000;
    private const string AccountKind = "account";
    private const string SessionKind = "session";
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);

    private readonly ConcurrentDictionary<string, UserAccount> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _key;

    public AccountStore()
    {
        // The signing key must stay the same across restarts. ACCOUNT_SECRET is used when set;
        // otherwise the key is derived from a secret the app already has. Changing that secret
        // signs everyone out and makes saved accounts unusable.
        var secret = new[] { "ACCOUNT_SECRET", "PAYPAL_SECRET", "ANTHROPIC_API_KEY" }
            .Select(name => Environment.GetEnvironmentVariable(name))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        _key = secret is null
            ? RandomNumberGenerator.GetBytes(32)
            : SHA256.HashData(Encoding.UTF8.GetBytes("plumbline-accounts-v1|" + secret));
    }

    /// <summary>Creates an account. Returns the account, or null and a message saying what to fix.</summary>
    public (UserAccount? Account, string Error) SignUp(string? name, string? email, string? password, string? role)
    {
        name = (name ?? "").Trim();
        email = (email ?? "").Trim();
        password ??= "";
        role = (role ?? "").Trim().ToUpperInvariant();

        if (name.Length == 0 || name.Length > 60)
            return (null, "Enter your name (up to 60 characters).");
        if (email.Length > 200 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return (null, "Enter a valid email address.");
        if (password.Length < 8 || password.Length > 200)
            return (null, "Choose a password of at least 8 characters.");
        if (role != Roles.Freelancer && role != Roles.Client)
            return (null, "Choose whether this is a freelancer or a client account.");

        var salt = RandomNumberGenerator.GetBytes(16);
        var account = new UserAccount
        {
            Name = name,
            Email = email,
            Role = role,
            Salt = salt,
            PasswordHash = Hash(password, salt)
        };

        if (!_byEmail.TryAdd(email, account))
            return (null, "An account with this email already exists. Log in instead.");

        return (account, "");
    }

    /// <summary>
    /// Checks the email and password. When the server has forgotten the account since a restart,
    /// the browser's saved record for that email is used to restore it first.
    /// </summary>
    public UserAccount? LogIn(string? email, string? password, string? record)
    {
        email = (email ?? "").Trim();
        password ??= "";

        UserAccount? account = null;
        if (_byEmail.TryGetValue(email, out var known))
        {
            account = known;
        }
        else
        {
            var restored = FromRecord(record);
            if (restored is not null && string.Equals(restored.Email, email, StringComparison.OrdinalIgnoreCase))
                account = restored;
        }

        if (account is null)
        {
            // Spend the same time as a real check, so response times do not reveal which emails exist.
            Hash(password, DummySalt);
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Hash(password, account.Salt), account.PasswordHash)
            ? account
            : null;
    }

    /// <summary>A signed record of the account for the browser to keep. Valid for 30 days.</summary>
    public string IssueRecord(UserAccount account) =>
        Sign(new SignedAccountRecord(
            AccountKind,
            account.Id,
            account.Name,
            account.Email,
            account.Role,
            Convert.ToBase64String(account.Salt),
            Convert.ToBase64String(account.PasswordHash),
            DateTimeOffset.UtcNow.Add(RememberLifetime).ToUnixTimeSeconds()));

    /// <summary>
    /// Reads a saved record and puts the account back in memory if the server has forgotten it.
    /// Returns null when the record is altered, expired, or its email now belongs to another account.
    /// </summary>
    public UserAccount? FromRecord(string? record)
    {
        var saved = Verify<SignedAccountRecord>(record);
        if (saved is null ||
            saved.Kind != AccountKind ||
            saved.Exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            (saved.Role != Roles.Freelancer && saved.Role != Roles.Client) ||
            string.IsNullOrEmpty(saved.Email) ||
            string.IsNullOrEmpty(saved.Salt) ||
            string.IsNullOrEmpty(saved.Hash))
        {
            return null;
        }

        UserAccount restored;
        try
        {
            restored = new UserAccount
            {
                Id = saved.Id,
                Name = saved.Name ?? "",
                Email = saved.Email,
                Role = saved.Role,
                Salt = Convert.FromBase64String(saved.Salt),
                PasswordHash = Convert.FromBase64String(saved.Hash)
            };
        }
        catch (FormatException)
        {
            return null;
        }

        var account = _byEmail.GetOrAdd(saved.Email, restored);
        return account.Id == saved.Id ? account : null;
    }

    /// <summary>The value of the session cookie: who is signed in, signed and valid for 12 hours.</summary>
    public string StartSession(UserAccount account) =>
        Sign(new SignedSession(
            SessionKind,
            account.Id,
            account.Name,
            account.Email,
            account.Role,
            DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds()));

    public UserAccount? FindSession(string? token)
    {
        var session = Verify<SignedSession>(token);
        if (session is null ||
            session.Kind != SessionKind ||
            session.Exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            (session.Role != Roles.Freelancer && session.Role != Roles.Client))
        {
            return null;
        }

        return new UserAccount
        {
            Id = session.Id,
            Name = session.Name ?? "",
            Email = session.Email ?? "",
            Role = session.Role
        };
    }

    private string Sign<T>(T payload)
    {
        var body = ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = ToBase64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body)));
        return body + "." + signature;
    }

    private T? Verify<T>(string? token) where T : class
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4000) return null;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        var body = token[..dot];
        try
        {
            var given = FromBase64Url(token[(dot + 1)..]);
            var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
            if (!CryptographicOperations.FixedTimeEquals(given, expected)) return null;
            return JsonSerializer.Deserialize<T>(FromBase64Url(body));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => ""
        };
        return Convert.FromBase64String(padded);
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}

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
    /// Builds a readable project ID from the project title, such as BAKERY-LANDING-7K3Q:
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
