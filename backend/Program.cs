using System.Globalization;
using System.Text;
using Plumbline.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient("paypal", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("claude", client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<PayPalClient>();
builder.Services.AddSingleton<ClaudeClient>();

var app = builder.Build();
app.UseCors();

var activityLog = app.Services.GetRequiredService<ProjectStore>();

var supportedImageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "image/png", "image/jpeg", "image/gif", "image/webp"
};

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

IResult Fail(int statusCode, string message) =>
    Results.Json(new { error = message }, statusCode: statusCode);

// Once the 24-hour review window passes without a dispute, escrow is released to the freelancer.
void ExpireReviewWindow(Project p)
{
    if (p.Status == ProjectStatus.EscrowHeld &&
        p.ReviewDeadlineUtc is { } deadline &&
        DateTimeOffset.UtcNow >= deadline)
    {
        p.Status = ProjectStatus.Completed;
        activityLog.Log(p, ActivityKinds.Released, "The 24-hour review closed with no dispute. Funds released to the freelancer.");
    }
}

// Used only when Claude is unavailable, and labelled as such in the response.
List<string> FallbackSummary(string requirements) =>
    requirements
        .Split(new[] { '\n', '.', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.TrimStart('-', '*', '•', ' ').Trim())
        .Where(part => part.Length > 2)
        .Take(8)
        .ToList();

// The only shape that ever leaves the server. The full asset is included only when unlocked.
object ToDto(Project p)
{
    var unlocked = p.Status is ProjectStatus.EscrowHeld or ProjectStatus.Completed;

    string content;
    if (!unlocked)
        content = p.PreviewContent;
    else if (p.AssetKind == AssetKinds.Image)
        content = PreviewBuilder.ToDataUrl(p.AssetMediaType ?? "image/png", p.AssetBytes ?? Array.Empty<byte>());
    else
        content = p.AssetText ?? "";

    return new
    {
        id = p.Id,
        title = p.Title,
        priceUsd = p.PriceUsd,
        priceDisplay = p.PriceUsd.ToString("0.00", CultureInfo.InvariantCulture),
        currency = "USD",
        status = p.Status,
        assetKind = p.AssetKind,
        fileName = p.FileName,
        unlocked,
        content,
        previewNotice = unlocked
            ? null
            : "Protected preview. Pay into escrow to unlock the full, unaltered asset.",
        requirementsText = p.RequirementsText,
        requirementsSummary = p.RequirementsSummary ?? FallbackSummary(p.RequirementsText),
        summarySource = p.RequirementsSummary is null ? "fallback" : "claude",
        reviewDeadlineUtc = p.ReviewDeadlineUtc,
        paypalCaptureId = p.PayPalCaptureId,
        paypalRefundId = p.PayPalRefundId,
        complaintReason = p.ComplaintReason,
        verdict = p.Verdict is null
            ? null
            : new { matchScore = p.Verdict.MatchScore, verdict = p.Verdict.Verdict, reason = p.Verdict.Reason }
    };
}

// ---------------------------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------------------------

app.MapGet("/", () => Results.Text("Plumbline API is running. The UI is served by the frontend dev server."));

// Public settings the browser needs. Secrets never leave the server.
app.MapGet("/api/config", (PayPalClient paypal, ClaudeClient claude) => Results.Ok(new
{
    paypalClientId = paypal.ClientId,
    currency = "USD",
    paypalConfigured = paypal.IsConfigured,
    claudeConfigured = claude.IsConfigured
}));

// 1. Freelancer uploads the asset and the client's original requirements.
//    multipart/form-data fields: title, requirementsText, priceUsd, assetText | assetFile, previewImage (optional).
app.MapPost("/api/projects/upload", async (HttpRequest request, ProjectStore store, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Fail(400, "Send the upload as multipart/form-data.");

    var form = await request.ReadFormAsync(ct);

    var title = form["title"].ToString().Trim();
    if (title.Length == 0) title = "Untitled project";
    if (title.Length > 120) title = title[..120];

    var requirements = form["requirementsText"].ToString().Trim();
    if (requirements.Length == 0)
        return Fail(400, "requirementsText is required.");
    if (requirements.Length > Limits.MaxRequirementsChars)
        return Fail(413, $"requirementsText is longer than {Limits.MaxRequirementsChars} characters.");

    if (!decimal.TryParse(form["priceUsd"].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        return Fail(400, "priceUsd must be a number, for example 25.00.");
    price = decimal.Round(price, 2);
    if (price < 1m || price > 10_000m)
        return Fail(400, "priceUsd must be between 1.00 and 10000.00.");

    var project = new Project
    {
        Title = title,
        RequirementsText = requirements,
        PriceUsd = price
    };

    var assetFile = form.Files.GetFile("assetFile");
    var assetText = form["assetText"].ToString();

    if (assetFile is { Length: > 0 })
    {
        if (assetFile.Length > Limits.MaxAssetBytes)
            return Fail(413, "The asset file is larger than 3.5 MB.");

        using var buffer = new MemoryStream();
        await assetFile.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        project.FileName = Path.GetFileName(assetFile.FileName);

        if (supportedImageTypes.Contains(assetFile.ContentType ?? ""))
        {
            project.AssetKind = AssetKinds.Image;
            project.AssetBytes = bytes;
            project.AssetMediaType = assetFile.ContentType!.ToLowerInvariant();

            // The uploader's browser sends a blurred, watermarked rendition alongside the original.
            var previewFile = form.Files.GetFile("previewImage");
            var previewType = previewFile?.ContentType ?? "";
            if (previewFile is { Length: > 0 } &&
                previewFile.Length <= Limits.MaxPreviewBytes &&
                (previewType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                 previewType.Equals("image/png", StringComparison.OrdinalIgnoreCase)))
            {
                using var previewBuffer = new MemoryStream();
                await previewFile.CopyToAsync(previewBuffer, ct);
                project.PreviewContent = PreviewBuilder.ToDataUrl(previewType.ToLowerInvariant(), previewBuffer.ToArray());
            }
            else
            {
                project.PreviewContent = PreviewBuilder.LockedImagePlaceholder(project.FileName, bytes.LongLength);
            }
        }
        else
        {
            if (Array.IndexOf(bytes, (byte)0) >= 0)
                return Fail(415, "Unsupported file type. Upload text or code, or a PNG, JPEG, GIF or WebP image.");

            var text = Encoding.UTF8.GetString(bytes);
            if (text.Length > Limits.MaxTextChars)
                return Fail(413, $"The asset is longer than {Limits.MaxTextChars} characters.");

            project.AssetKind = AssetKinds.Text;
            project.AssetText = text;
            project.PreviewContent = PreviewBuilder.MaskText(text);
        }
    }
    else if (!string.IsNullOrWhiteSpace(assetText))
    {
        if (assetText.Length > Limits.MaxTextChars)
            return Fail(413, $"The asset is longer than {Limits.MaxTextChars} characters.");

        project.AssetKind = AssetKinds.Text;
        project.AssetText = assetText;
        project.PreviewContent = PreviewBuilder.MaskText(assetText);
    }
    else
    {
        return Fail(400, "Provide the asset as assetText or as an assetFile.");
    }

    store.Add(project);
    store.Log(project, ActivityKinds.Created,
        $"Project created at ${price.ToString("0.00", CultureInfo.InvariantCulture)}. The asset is stored and only a protected preview is visible.");
    return Results.Ok(ToDto(project));
});

// 2. Protected preview while unpaid; full asset once paid. Includes Claude's accessible summary.
app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectStore store, ClaudeClient claude, ILogger<Program> log, CancellationToken ct) =>
{
    if (!store.TryGet(id, out var project))
        return Fail(404, "No project with that ID. Projects are kept in memory and are cleared when the API restarts.");

    await project.Gate.WaitAsync(ct);
    try
    {
        ExpireReviewWindow(project);

        if (project.RequirementsSummary is null && claude.IsConfigured)
        {
            try
            {
                project.RequirementsSummary = await claude.SummarizeRequirementsAsync(project.RequirementsText, ct);
            }
            catch (ClaudeException ex)
            {
                // The page still works with the plain split of the requirements; retried on the next load.
                log.LogWarning("Requirement summary failed for {ProjectId}: {Message}", id, ex.Message);
            }
        }

        return Results.Ok(ToDto(project));
    }
    finally
    {
        project.Gate.Release();
    }
});

// Dashboard: totals by escrow state, every project (newest first), recent activity and
// which integrations are configured. Never includes asset content.
app.MapGet("/api/dashboard", (ProjectStore store, PayPalClient paypal, ClaudeClient claude) =>
{
    var projects = store.All.OrderByDescending(p => p.CreatedUtc).ToList();

    foreach (var p in projects)
    {
        // Close expired review windows, skipping any project that is mid-payment or mid-dispute.
        if (!p.Gate.Wait(0)) continue;
        try { ExpireReviewWindow(p); }
        finally { p.Gate.Release(); }
    }

    decimal Sum(string status) => projects.Where(p => p.Status == status).Sum(p => p.PriceUsd);
    int Count(string status) => projects.Count(p => p.Status == status);

    return Results.Ok(new
    {
        totals = new
        {
            awaitingCount = Count(ProjectStatus.AwaitingPayment),
            awaitingAmount = Sum(ProjectStatus.AwaitingPayment),
            escrowCount = Count(ProjectStatus.EscrowHeld),
            escrowAmount = Sum(ProjectStatus.EscrowHeld),
            releasedCount = Count(ProjectStatus.Completed),
            releasedAmount = Sum(ProjectStatus.Completed),
            refundedCount = Count(ProjectStatus.Refunded),
            refundedAmount = Sum(ProjectStatus.Refunded)
        },
        projects = projects.Select(p => new
        {
            id = p.Id,
            title = p.Title,
            priceDisplay = p.PriceUsd.ToString("0.00", CultureInfo.InvariantCulture),
            status = p.Status,
            assetKind = p.AssetKind,
            createdUtc = p.CreatedUtc,
            verdict = p.Verdict?.Verdict,
            matchScore = p.Verdict?.MatchScore
        }).ToList(),
        activity = store.RecentActivity(50).Select(entry => new
        {
            at = entry.At,
            projectId = entry.ProjectId,
            projectTitle = entry.ProjectTitle,
            kind = entry.Kind,
            message = entry.Message
        }).ToList(),
        integrations = new
        {
            paypalConfigured = paypal.IsConfigured,
            claudeConfigured = claude.IsConfigured
        }
    });
});

// 3. Create a PayPal Sandbox order with intent CAPTURE.
app.MapPost("/api/payments/create-order/{projectId:guid}", async (Guid projectId, ProjectStore store, PayPalClient paypal, CancellationToken ct) =>
{
    if (!store.TryGet(projectId, out var project))
        return Fail(404, "No project with that ID.");

    await project.Gate.WaitAsync(ct);
    try
    {
        if (project.Status != ProjectStatus.AwaitingPayment)
            return Fail(409, $"This project is {project.Status}; it cannot be paid for again.");

        var orderId = await paypal.CreateOrderAsync(project, ct);
        project.PayPalOrderIds.Add(orderId);
        store.Log(project, ActivityKinds.CheckoutStarted, $"PayPal order {orderId} created.");
        return Results.Ok(new { orderId });
    }
    catch (PayPalException ex)
    {
        return Fail(502, ex.Message);
    }
    finally
    {
        project.Gate.Release();
    }
});

// 4. Capture the approved order, hold the funds in escrow and open the 24-hour review window.
app.MapPost("/api/payments/capture-order/{projectId:guid}/{orderId}", async (Guid projectId, string orderId, ProjectStore store, PayPalClient paypal, CancellationToken ct) =>
{
    if (!store.TryGet(projectId, out var project))
        return Fail(404, "No project with that ID.");

    await project.Gate.WaitAsync(ct);
    try
    {
        if (project.Status != ProjectStatus.AwaitingPayment)
            return Fail(409, $"This project is {project.Status}; there is nothing to capture.");
        if (!project.PayPalOrderIds.Contains(orderId))
            return Fail(400, "That PayPal order was not created for this project.");

        project.PayPalCaptureId = await paypal.CaptureOrderAsync(orderId, ct);
        project.Status = ProjectStatus.EscrowHeld;
        project.ReviewDeadlineUtc = DateTimeOffset.UtcNow.Add(Limits.ReviewWindow);
        store.Log(project, ActivityKinds.Paid,
            $"Payment captured (PayPal capture {project.PayPalCaptureId}). Held in escrow for a 24-hour review.");
        return Results.Ok(ToDto(project));
    }
    catch (PayPalException ex)
    {
        return Fail(502, ex.Message);
    }
    finally
    {
        project.Gate.Release();
    }
});

// 5. Claude Fable 5.2 Arbitrator Protocol: audit the delivery, then release or refund.
app.MapPost("/api/disputes/resolve/{projectId:guid}", async (Guid projectId, DisputeRequest body, ProjectStore store, ClaudeClient claude, PayPalClient paypal, CancellationToken ct) =>
{
    if (!store.TryGet(projectId, out var project))
        return Fail(404, "No project with that ID.");

    var complaint = (body.ComplaintReason ?? "").Trim();
    if (complaint.Length == 0)
        return Fail(400, "complaintReason is required.");
    if (complaint.Length > Limits.MaxComplaintChars)
        return Fail(413, $"complaintReason is longer than {Limits.MaxComplaintChars} characters.");

    await project.Gate.WaitAsync(ct);
    try
    {
        ExpireReviewWindow(project);

        if (project.Status != ProjectStatus.EscrowHeld)
            return Fail(409, project.Status switch
            {
                ProjectStatus.AwaitingPayment => "This project has not been paid for, so there is nothing to dispute.",
                ProjectStatus.Completed => "The review window has closed and the funds were released to the freelancer.",
                ProjectStatus.Refunded => "This project has already been refunded.",
                _ => $"This project is {project.Status} and cannot be disputed."
            });
        if (project.PayPalCaptureId is null)
            return Fail(409, "This project has no captured payment to act on.");

        // No verdict means no money moves: any Claude failure leaves the funds in escrow.
        var verdict = await claude.ArbitrateAsync(project, complaint, ct);
        project.ComplaintReason = complaint;
        project.Verdict = verdict;

        if (verdict.Verdict == DisputeVerdict.Release)
        {
            project.Status = ProjectStatus.Completed;
            store.Log(project, ActivityKinds.Released,
                $"The arbitrator ruled release with a match score of {verdict.MatchScore}. Funds released to the freelancer.");
        }
        else
        {
            // If the refund fails, the catch below reports it and the project stays in escrow for a retry.
            project.PayPalRefundId = await paypal.RefundCaptureAsync(project.PayPalCaptureId, ct);
            project.Status = ProjectStatus.Refunded;
            store.Log(project, ActivityKinds.Refunded,
                $"The arbitrator ruled refund with a match score of {verdict.MatchScore}. PayPal refund {project.PayPalRefundId} sent to the client.");
        }

        return Results.Ok(ToDto(project));
    }
    catch (ClaudeException ex)
    {
        return Fail(502, "The arbitrator could not reach a verdict, so the funds stay in escrow. " + ex.Message);
    }
    catch (PayPalException ex)
    {
        return Fail(502, "The arbitrator ruled REFUND but PayPal did not complete it; the funds stay in escrow. " + ex.Message);
    }
    finally
    {
        project.Gate.Release();
    }
});

app.Run("http://localhost:5080");
