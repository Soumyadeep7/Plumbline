module App

open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Feliz

// ---------------------------------------------------------------------------------------------
// Data returned by the API (field names match the JSON)
// ---------------------------------------------------------------------------------------------

type ProjectDraftDto =
    { draftTitle: string
      draftRequirements: string
      draftPrice: string
      draftNotes: string }

type MeDto =
    { loggedIn: bool
      name: string
      email: string
      loginAvailable: bool }

type TotalsDto =
    { awaitingCount: int
      awaitingAmount: float
      escrowCount: int
      escrowAmount: float
      releasedCount: int
      releasedAmount: float
      refundedCount: int
      refundedAmount: float }

type ProjectRowDto =
    { id: string
      title: string
      priceDisplay: string
      status: string
      assetKind: string
      createdUtc: string
      verdict: string option
      matchScore: int option }

type ActivityDto =
    { at: string
      projectId: string
      projectTitle: string
      kind: string
      message: string }

type IntegrationsDto =
    { paypalConfigured: bool
      claudeConfigured: bool }

type DashboardDto =
    { totals: TotalsDto
      projects: ProjectRowDto[]
      activity: ActivityDto[]
      integrations: IntegrationsDto }

type VerdictDto =
    { matchScore: int
      verdict: string
      reason: string }

type ProjectDto =
    { id: string
      title: string
      priceDisplay: string
      currency: string
      status: string
      assetKind: string
      fileName: string option
      unlocked: bool
      content: string
      previewNotice: string option
      requirementsText: string
      requirementsSummary: string[]
      summarySource: string
      reviewDeadlineUtc: string option
      paypalCaptureId: string option
      paypalRefundId: string option
      complaintReason: string option
      verdict: VerdictDto option
      uploadedBy: string
      paidBy: string }

type AppConfig =
    { paypalClientId: string
      currency: string
      paypalConfigured: bool
      claudeConfigured: bool }

// ---------------------------------------------------------------------------------------------
// JavaScript interop
// ---------------------------------------------------------------------------------------------

[<Import("apiGet", "./interop.js")>]
let private apiGet (path: string) : JS.Promise<obj> = jsNative

[<Import("apiPostJson", "./interop.js")>]
let private apiPostJson (path: string, body: obj) : JS.Promise<obj> = jsNative

[<Import("uploadProject", "./interop.js")>]
let private uploadProject (fields: obj, file: obj) : JS.Promise<obj> = jsNative

// React Bits components (https://reactbits.dev), copied into src/reactbits as that library intends.
[<ImportDefault("./reactbits/BlurText.jsx")>]
let private blurTextComponent: obj = jsNative

[<ImportDefault("./reactbits/CountUp.jsx")>]
let private countUpComponent: obj = jsNative

[<ImportDefault("./reactbits/DecryptedText.jsx")>]
let private decryptedTextComponent: obj = jsNative

[<ImportAll("react")>]
let private reactLibrary: obj = jsNative

[<Emit("$0.createElement($1, $2)")>]
let private createJsElement (react: obj) (comp: obj) (props: obj) : ReactElement = jsNative

/// Renders a JavaScript React component with the given props.
let private reactBit (comp: obj) (props: (string * obj) list) : ReactElement =
    createJsElement reactLibrary comp (createObj props)

[<Import("mountPayPalButtons", "./interop.js")>]
let private mountPayPalButtons
    (containerId: string, clientId: string, currency: string, projectId: string, onCaptured: obj -> unit, onError: string -> unit)
    : unit =
    jsNative

/// Reads text aloud with the browser's native speech synthesis, replacing anything still being spoken.
[<Emit("(function (t) { if (typeof window !== 'undefined' && 'speechSynthesis' in window) { window.speechSynthesis.cancel(); var u = new SpeechSynthesisUtterance(t); u.rate = 1; u.pitch = 1; window.speechSynthesis.speak(u); } })($0)")>]
let private speak (text: string) : unit = jsNative

[<Emit("(function () { if (typeof window !== 'undefined' && 'speechSynthesis' in window) { window.speechSynthesis.cancel(); } })()")>]
let private stopSpeaking () : unit = jsNative

[<Emit("($0.target.files && $0.target.files.length > 0 ? $0.target.files[0] : null)")>]
let private pickedFile (ev: obj) : obj = jsNative

[<Emit("$0.name")>]
let private fileNameOf (file: obj) : string = jsNative

/// Switches the colour theme on the root element and remembers the choice for the next visit.
[<Emit("(function (t) { document.documentElement.setAttribute('data-theme', t); try { window.localStorage.setItem('plumbline-theme', t); } catch (e) { } })($0)")>]
let private applyTheme (theme: string) : unit = jsNative

[<Emit("(function () { try { return window.localStorage.getItem('plumbline-theme') === 'light'; } catch (e) { return false; } })()")>]
let private savedLightTheme () : bool = jsNative

[<Emit("new Date($0).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })")>]
let private localDate (isoDate: string) : string = jsNative

[<Emit("window.location.assign($0)")>]
let private goTo (url: string) : unit = jsNative

/// Reads one value from the page address, such as app=1 after a PayPal login.
[<Emit("(new URLSearchParams(window.location.search).get($0) || '')")>]
let private queryValue (name: string) : string = jsNative

[<Emit("window.history.replaceState(null, '', window.location.pathname)")>]
let private clearQuery () : unit = jsNative

[<Emit("new Date($0).toLocaleString()")>]
let private localTime (isoDate: string) : string = jsNative

// ---------------------------------------------------------------------------------------------
// Model
// ---------------------------------------------------------------------------------------------

type Screen =
    | Overview
    | Projects
    | Freelancer
    | Client
    | Dispute
    | Activity
    | Settings

type BannerKind =
    | Success
    | Danger
    | Warning
    | Info

type Banner = { Kind: BannerKind; Text: string }

type Model =
    { InApp: bool
      Me: MeDto option
      Screen: Screen
      Config: AppConfig option
      Dashboard: DashboardDto option
      StatusFilter: string
      Search: string
      SpeechOn: bool
      Light: bool
      Busy: string option
      Banner: Banner option
      Announcement: string
      // Freelancer upload form
      DraftSource: string
      DraftNotes: string
      Title: string
      Requirements: string
      Price: string
      AssetText: string
      AssetFile: obj option
      AssetFileName: string
      // Client and dispute
      ProjectIdInput: string
      Project: ProjectDto option
      Complaint: string }

type Msg =
    | SetScreen of Screen
    | EnterApp of Screen
    | MeLoaded of MeDto
    | LogOut
    | LoggedOut
    | LeaveApp
    | RefreshDashboard
    | DashboardLoaded of DashboardDto
    | SetStatusFilter of string
    | SetSearch of string
    | OpenProject of string
    | ToggleSpeech
    | ToggleTheme
    | ConfigLoaded of AppConfig
    | SetDraftSource of string
    | DraftProject
    | ProjectDrafted of ProjectDraftDto
    | SetTitle of string
    | SetRequirements of string
    | SetPrice of string
    | SetAssetText of string
    | FilePicked of obj
    | SubmitUpload
    | Uploaded of ProjectDto
    | SetProjectIdInput of string
    | LoadProject
    | ProjectLoaded of ProjectDto
    | PaymentCaptured of ProjectDto
    | SetComplaint of string
    | SubmitDispute
    | DisputeResolved of ProjectDto
    | Failed of string

let private paypalContainerId = "paypal-buttons"

let private errorText (ex: exn) : string =
    if isNull (box ex) || System.String.IsNullOrEmpty ex.Message then
        "Something went wrong. Check that the API is running."
    else
        ex.Message

/// Shows a colour-coded banner, updates the ARIA live region and, if enabled, speaks the text.
let private notify (kind: BannerKind) (text: string) (model: Model) : Model * Cmd<Msg> =
    let next =
        { model with
            Banner = Some { Kind = kind; Text = text }
            Announcement = text }

    let cmd =
        if model.SpeechOn then
            Cmd.ofEffect (fun _ -> speak text)
        else
            Cmd.none

    next, cmd

let private mountPayPalCmd (model: Model) : Cmd<Msg> =
    match model.Screen, model.Config, model.Project with
    | Client, Some cfg, Some p when cfg.paypalConfigured && p.status = "AWAITING_PAYMENT" ->
        Cmd.ofEffect (fun dispatch ->
            mountPayPalButtons (
                paypalContainerId,
                cfg.paypalClientId,
                p.currency,
                p.id,
                (fun o -> dispatch (PaymentCaptured(unbox<ProjectDto> o))),
                (fun message -> dispatch (Failed message))
            ))
    | _ -> Cmd.none

let private loadDashboardCmd : Cmd<Msg> =
    Cmd.OfPromise.either
        (fun () -> apiGet "/api/dashboard")
        ()
        (fun o -> DashboardLoaded(unbox<DashboardDto> o))
        (fun ex -> Failed(errorText ex))

/// What has to happen when a screen is shown: fresh numbers, or PayPal's buttons.
let private screenCmd (model: Model) : Cmd<Msg> =
    match model.Screen with
    | Overview
    | Projects
    | Activity
    | Settings -> loadDashboardCmd
    | _ -> mountPayPalCmd model

let init () : Model * Cmd<Msg> =
    let light = savedLightTheme ()

    let model =
        { InApp = false
          Me = None
          Screen = Overview
          Config = None
          Dashboard = None
          StatusFilter = "ALL"
          Search = ""
          SpeechOn = true
          Light = light
          Busy = None
          Banner = None
          Announcement = ""
          DraftSource = ""
          DraftNotes = ""
          Title = ""
          Requirements = ""
          Price = "25.00"
          AssetText = ""
          AssetFile = None
          AssetFileName = ""
          ProjectIdInput = ""
          Project = None
          Complaint = "" }

    let loadConfig =
        Cmd.OfPromise.either
            (fun () -> apiGet "/api/config")
            ()
            (fun o -> ConfigLoaded(unbox<AppConfig> o))
            (fun ex -> Failed(errorText ex))

    let loadMe =
        Cmd.OfPromise.either
            (fun () -> apiGet "/api/auth/me")
            ()
            (fun o -> MeLoaded(unbox<MeDto> o))
            (fun ex -> Failed(errorText ex))

    model, Cmd.batch [ loadConfig; loadMe; Cmd.ofEffect (fun _ -> applyTheme (if light then "light" else "dark")) ]

// ---------------------------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------------------------

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SetScreen screen ->
        let next = { model with Screen = screen; Banner = None }
        next, screenCmd next

    | EnterApp screen ->
        match model.Me with
        | Some me when me.loggedIn ->
            let next =
                { model with
                    InApp = true
                    Screen = screen
                    Banner = None }

            next, screenCmd next
        | Some me when not me.loginAvailable ->
            notify
                Danger
                "PayPal login is not set up on the API. Add PAYPAL_CLIENT_ID and PAYPAL_SECRET, then restart it."
                model
        | _ ->
            // Not signed in yet: PayPal's login page, then back to the dashboard.
            model, Cmd.ofEffect (fun _ -> goTo "/api/auth/login")

    | MeLoaded me ->
        let next = { model with Me = Some me }
        let login = queryValue "login"
        let wantsApp = queryValue "app" = "1"

        if login <> "" || wantsApp then
            clearQuery ()

        if login = "failed" then
            notify Danger "The PayPal login did not complete. Try again." next
        elif login = "unavailable" then
            notify Danger "PayPal login is not set up on the API." next
        elif me.loggedIn && wantsApp then
            let inApp =
                { next with
                    InApp = true
                    Screen = Overview }

            let notified, speakCmd = notify Success ("Signed in with PayPal as " + me.name + ".") inApp
            notified, Cmd.batch [ speakCmd; screenCmd notified ]
        else
            next, Cmd.none

    | LogOut ->
        model,
        Cmd.OfPromise.either
            (fun () -> apiPostJson ("/api/auth/logout", createObj []))
            ()
            (fun _ -> LoggedOut)
            (fun ex -> Failed(errorText ex))

    | LoggedOut ->
        let next =
            { model with
                InApp = false
                Dashboard = None
                Project = None
                Me =
                    (match model.Me with
                     | Some me -> Some { me with loggedIn = false; name = ""; email = "" }
                     | None -> None) }

        notify Info "You are signed out." next

    | LeaveApp -> { model with InApp = false; Banner = None }, Cmd.none

    | RefreshDashboard -> model, loadDashboardCmd

    | DashboardLoaded dashboard -> { model with Dashboard = Some dashboard }, Cmd.none

    | SetStatusFilter value -> { model with StatusFilter = value }, Cmd.none
    | SetSearch value -> { model with Search = value }, Cmd.none

    | OpenProject projectId ->
        { model with
            InApp = true
            Screen = Client
            ProjectIdInput = projectId },
        Cmd.ofMsg LoadProject

    | ToggleTheme ->
        let light = not model.Light

        let text =
            if light then
                "Light theme on."
            else
                "Dark theme on."

        let next =
            { model with
                Light = light
                Announcement = text }

        next, Cmd.ofEffect (fun _ -> applyTheme (if light then "light" else "dark"))

    | ToggleSpeech ->
        let on = not model.SpeechOn

        let text =
            if on then
                "Voice announcements on."
            else
                "Voice announcements off."

        let next =
            { model with
                SpeechOn = on
                Announcement = text }

        next, Cmd.ofEffect (fun _ -> if on then speak text else stopSpeaking ())

    | ConfigLoaded cfg ->
        let next = { model with Config = Some cfg }

        let missing =
            [ if not cfg.paypalConfigured then
                  "PAYPAL_CLIENT_ID and PAYPAL_SECRET"
              if not cfg.claudeConfigured then
                  "ANTHROPIC_API_KEY" ]

        if List.isEmpty missing then
            next, mountPayPalCmd next
        else
            let text =
                "Setup incomplete. The API is missing "
                + String.concat " and " missing
                + ". Set them and restart the API."

            notify Warning text next

    | SetDraftSource value -> { model with DraftSource = value }, Cmd.none

    | DraftProject ->
        if model.DraftSource.Trim() = "" then
            notify Danger "Paste the client's message or describe the job first." model
        else
            let body = createObj [ "text" ==> model.DraftSource ]

            { model with
                Busy = Some "Claude is filling in the form"
                Banner = None },
            Cmd.OfPromise.either
                (fun () -> apiPostJson ("/api/projects/draft", body))
                ()
                (fun o -> ProjectDrafted(unbox<ProjectDraftDto> o))
                (fun ex -> Failed(errorText ex))

    | ProjectDrafted draft ->
        let next =
            { model with
                Busy = None
                Title = (if draft.draftTitle = "" then model.Title else draft.draftTitle)
                Requirements = draft.draftRequirements
                Price = (if draft.draftPrice = "" then model.Price else draft.draftPrice)
                DraftNotes = draft.draftNotes }

        let text =
            if draft.draftPrice = "" then
                "Claude filled in the title and the requirements. No price was stated, so enter it yourself. Check everything, add the asset, then upload."
            else
                "Claude filled in the title, the requirements and the price. Check everything, add the asset, then upload."

        notify Success text next

    | SetTitle value -> { model with Title = value }, Cmd.none
    | SetRequirements value -> { model with Requirements = value }, Cmd.none
    | SetPrice value -> { model with Price = value }, Cmd.none
    | SetAssetText value -> { model with AssetText = value }, Cmd.none

    | FilePicked file ->
        if isNull file then
            { model with
                AssetFile = None
                AssetFileName = "" },
            Cmd.none
        else
            { model with
                AssetFile = Some file
                AssetFileName = fileNameOf file },
            Cmd.none

    | SubmitUpload ->
        if model.Requirements.Trim() = "" then
            notify Danger "Add the client's original requirements before uploading." model
        elif model.AssetFile.IsNone && model.AssetText.Trim() = "" then
            notify Danger "Add the asset: paste text or code, or choose a file." model
        else
            let fields =
                createObj
                    [ "title" ==> model.Title
                      "requirementsText" ==> model.Requirements
                      "priceUsd" ==> model.Price
                      "assetText" ==> model.AssetText ]

            let file =
                match model.AssetFile with
                | Some f -> f
                | None -> null

            { model with
                Busy = Some "Uploading and protecting the asset"
                Banner = None },
            Cmd.OfPromise.either
                (fun () -> uploadProject (fields, file))
                ()
                (fun o -> Uploaded(unbox<ProjectDto> o))
                (fun ex -> Failed(errorText ex))

    | Uploaded project ->
        let next =
            { model with
                Busy = None
                Project = Some project
                ProjectIdInput = project.id
                DraftSource = ""
                DraftNotes = ""
                Title = ""
                Requirements = ""
                AssetText = ""
                AssetFile = None
                AssetFileName = ""
                Complaint = "" }

        notify
            Success
            "Project created. The asset is protected. Share the project ID shown below with your client."
            next

    | SetProjectIdInput value -> { model with ProjectIdInput = value }, Cmd.none

    | LoadProject ->
        let id = model.ProjectIdInput.Trim()

        if id = "" then
            notify Danger "Enter a project ID first." model
        else
            { model with
                Busy = Some "Loading project and asking Claude for a plain-language summary"
                Banner = None },
            Cmd.OfPromise.either
                (fun () -> apiGet ("/api/projects/" + JS.encodeURIComponent id))
                ()
                (fun o -> ProjectLoaded(unbox<ProjectDto> o))
                (fun ex -> Failed(errorText ex))

    | ProjectLoaded project ->
        let next =
            { model with
                Busy = None
                Project = Some project }

        let text =
            match project.status with
            | "AWAITING_PAYMENT" ->
                "Project loaded: "
                + project.title
                + ". Price "
                + project.priceDisplay
                + " US dollars. You are seeing a protected preview. Pay with PayPal to unlock the full asset."
            | "ESCROW_HELD" ->
                "Project loaded: "
                + project.title
                + ". Payment is held in escrow and the full asset is unlocked for review."
            | "COMPLETED" ->
                "Project loaded: "
                + project.title
                + ". This project is completed and the funds were released to the freelancer."
            | "REFUNDED" -> "Project loaded: " + project.title + ". This project was refunded to the client."
            | other -> "Project loaded: " + project.title + ". Status " + other + "."

        let kind =
            match project.status with
            | "ESCROW_HELD" -> Warning
            | "COMPLETED" -> Success
            | "REFUNDED" -> Danger
            | _ -> Info

        let notified, speakCmd = notify kind text next
        notified, Cmd.batch [ speakCmd; mountPayPalCmd notified ]

    | PaymentCaptured project ->
        let next =
            { model with
                Busy = None
                Project = Some project }

        notify
            Success
            ("Payment confirmed. "
             + project.priceDisplay
             + " US dollars are held in escrow. The full asset is unlocked. You have 24 hours to review it or open a dispute.")
            next

    | SetComplaint value -> { model with Complaint = value }, Cmd.none

    | SubmitDispute ->
        match model.Project with
        | None -> notify Danger "Load a project before opening a dispute." model
        | Some project when project.status <> "ESCROW_HELD" ->
            notify Danger "Only projects with payment held in escrow can be disputed." model
        | Some _ when model.Complaint.Trim() = "" -> notify Danger "Describe what is wrong with the delivery." model
        | Some project ->
            let waiting, speakCmd =
                notify
                    Warning
                    "Dispute submitted. The Claude arbitrator is auditing the delivery against the requirements. This can take up to a minute."
                    { model with Busy = Some "Claude arbitrator is auditing the delivery" }

            let body = createObj [ "complaintReason" ==> model.Complaint ]

            waiting,
            Cmd.batch
                [ speakCmd
                  Cmd.OfPromise.either
                      (fun () -> apiPostJson ("/api/disputes/resolve/" + project.id, body))
                      ()
                      (fun o -> DisputeResolved(unbox<ProjectDto> o))
                      (fun ex -> Failed(errorText ex)) ]

    | DisputeResolved project ->
        let next =
            { model with
                Busy = None
                Project = Some project }

        match project.verdict with
        | Some v when v.verdict = "RELEASE" ->
            notify
                Success
                ("Verdict: release. Match score "
                 + string v.matchScore
                 + " out of 100. The delivery meets the requirements, so the funds are released to the freelancer. "
                 + v.reason)
                next
        | Some v ->
            notify
                Danger
                ("Verdict: refund. Match score "
                 + string v.matchScore
                 + " out of 100. The delivery does not meet the requirements, so the payment has been refunded to the client. "
                 + v.reason)
                next
        | None -> notify Info "The dispute was processed." next

    | Failed message -> notify Danger message { model with Busy = None }

// ---------------------------------------------------------------------------------------------
// View
// ---------------------------------------------------------------------------------------------

let private aria (name: string) (value: string) = prop.custom ("aria-" + name, value)

let private field (id: string) (label: string) (hint: string) (control: ReactElement) =
    Html.div
        [ prop.className "field"
          prop.children
              [ Html.label [ prop.htmlFor id; prop.text label ]
                (if hint = "" then
                     Html.none
                 else
                     Html.p [ prop.className "hint"; prop.id (id + "-hint"); prop.text hint ])
                control ] ]

let private bannerView (banner: Banner option) =
    match banner with
    | None -> Html.none
    | Some b ->
        let cls, tag =
            match b.Kind with
            | Success -> "banner banner-success", "Done."
            | Danger -> "banner banner-danger", "Problem."
            | Warning -> "banner banner-warning", "Working."
            | Info -> "banner banner-info", "Note."

        Html.div
            [ prop.className cls
              prop.children
                  [ Html.strong [ prop.className "banner-tag"; prop.text tag ]
                    Html.span [ prop.text b.Text ] ] ]

let private statusPill (status: string) =
    let cls, label =
        match status with
        | "AWAITING_PAYMENT" -> "pill pill-info", "Awaiting payment, preview only"
        | "ESCROW_HELD" -> "pill pill-warning", "Held in escrow, in review"
        | "COMPLETED" -> "pill pill-success", "Completed, funds released"
        | "REFUNDED" -> "pill pill-danger", "Refunded to the client"
        | other -> "pill pill-info", other

    Html.span [ prop.className cls; prop.text label ]

let private summaryView (p: ProjectDto) =
    Html.section
        [ prop.className "panel"
          aria "labelledby" "summary-heading"
          prop.children
              [ Html.h3 [ prop.id "summary-heading"; prop.text "Requirements in plain language" ]
                Html.p
                    [ prop.className "hint"
                      prop.text (
                          if p.summarySource = "claude" then
                              "Summarised by Claude Fable 5.2."
                          else
                              "Claude is unavailable, so this is a simple split of the original text."
                      ) ]
                Html.ul
                    [ prop.className "summary"
                      prop.children
                          [ for i, bullet in Array.indexed p.requirementsSummary ->
                                Html.li [ prop.key (string i); prop.text bullet ] ] ]
                Html.details
                    [ prop.children
                          [ Html.summary [ prop.text "Show the original requirements" ]
                            Html.pre [ prop.className "plain"; prop.text p.requirementsText ] ] ] ] ]

let private assetView (p: ProjectDto) =
    let caption =
        match p.previewNotice with
        | Some notice -> notice
        | None -> "Full, unaltered asset."

    Html.section
        [ prop.className "panel"
          aria "labelledby" "asset-heading"
          prop.children
              [ Html.h3
                    [ prop.id "asset-heading"
                      prop.text (if p.unlocked then "Delivered asset" else "Delivered asset (protected preview)") ]
                Html.p [ prop.className "hint"; prop.text caption ]
                Html.div
                    [ prop.className (if p.unlocked then "asset" else "asset asset-locked")
                      prop.children
                          [ (if p.assetKind = "image" then
                                 Html.img
                                     [ prop.className "asset-img"
                                       prop.src p.content
                                       prop.alt (
                                           if p.unlocked then
                                               "Delivered image: " + defaultArg p.fileName "untitled"
                                           else
                                               "Blurred, watermarked preview of the delivered image"
                                       ) ]
                             else
                                 Html.pre [ prop.className "asset-text"; prop.tabIndex 0; prop.text p.content ])
                            (if p.unlocked then
                                 Html.none
                             else
                                 Html.div
                                     [ prop.className "watermark"
                                       aria "hidden" "true"
                                       prop.children
                                           [ reactBit
                                                 decryptedTextComponent
                                                 [ "text" ==> "Protected preview"
                                                   "animateOn" ==> "view"
                                                   "sequential" ==> true
                                                   "speed" ==> 60 ] ] ]) ] ] ] ]

let private verdictView (p: ProjectDto) =
    match p.verdict with
    | None -> Html.none
    | Some v ->
        let release = v.verdict = "RELEASE"

        Html.section
            [ prop.className (if release then "panel verdict verdict-release" else "panel verdict verdict-refund")
              aria "labelledby" "verdict-heading"
              prop.children
                  [ Html.h3 [ prop.id "verdict-heading"; prop.text "Arbitrator verdict" ]
                    Html.p
                        [ prop.className "verdict-line"
                          prop.text (
                              if release then
                                  "Release: the funds go to the freelancer"
                              else
                                  "Refund: the payment goes back to the client"
                          ) ]
                    Html.p
                        [ prop.children
                              [ Html.text "Match score: "
                                reactBit
                                    countUpComponent
                                    [ "to" ==> v.matchScore; "duration" ==> 1.2; "trigger" ==> "load" ]
                                Html.text " / 100" ] ]
                    Html.div
                        [ prop.className "score-track"
                          aria "hidden" "true"
                          prop.children
                              [ Html.div
                                    [ prop.className "score-fill"
                                      prop.style [ style.width (length.percent v.matchScore) ] ] ] ]
                    Html.p [ prop.text v.reason ]
                    (match p.complaintReason with
                     | Some complaint -> Html.p [ prop.className "hint"; prop.text ("Complaint: " + complaint) ]
                     | None -> Html.none)
                    (match p.paypalRefundId with
                     | Some refundId -> Html.p [ prop.className "hint"; prop.text ("PayPal refund ID: " + refundId) ]
                     | None -> Html.none) ] ]

let private freelancerView (model: Model) (dispatch: Msg -> unit) =
    let busy = model.Busy.IsSome

    Html.section
        [ aria "labelledby" "freelancer-heading"
          prop.children
              [ Html.h2 [ prop.id "freelancer-heading"; prop.className "sr-only"; prop.text "New project" ]
                Html.p
                    [ prop.text
                          "Upload your finished work. The client sees only a protected preview until they pay into escrow." ]
                Html.div
                    [ prop.className "panel"
                      prop.children
                          [ Html.h3 [ prop.text "Let Claude fill in the form" ]
                            field
                                "draft-source"
                                "Paste the client's message, or describe the job"
                                "Claude pulls out a title, the requirements in the client's own words, and the price if one is stated. You check everything before uploading."
                                (Html.textarea
                                    [ prop.id "draft-source"
                                      prop.rows 5
                                      aria "describedby" "draft-source-hint"
                                      prop.value model.DraftSource
                                      prop.onChange (fun (v: string) -> dispatch (SetDraftSource v)) ])
                            Html.button
                                [ prop.className "btn"
                                  prop.disabled busy
                                  prop.onClick (fun _ -> dispatch DraftProject)
                                  prop.text "Fill in the form with Claude" ]
                            (if model.DraftNotes = "" then
                                 Html.none
                             else
                                 Html.p [ prop.className "hint"; prop.text ("Claude's note: " + model.DraftNotes) ]) ] ]
                field
                    "title"
                    "Project title"
                    ""
                    (Html.input
                        [ prop.id "title"
                          prop.custom ("type", "text")
                          prop.value model.Title
                          prop.onChange (fun (v: string) -> dispatch (SetTitle v)) ])
                field
                    "requirements"
                    "Client's original requirements"
                    "Paste them exactly as the client wrote them. The arbitrator judges the delivery against this text."
                    (Html.textarea
                        [ prop.id "requirements"
                          prop.rows 6
                          aria "describedby" "requirements-hint"
                          prop.value model.Requirements
                          prop.onChange (fun (v: string) -> dispatch (SetRequirements v)) ])
                field
                    "price"
                    "Price in US dollars"
                    "Between 1.00 and 10000.00."
                    (Html.input
                        [ prop.id "price"
                          prop.custom ("type", "text")
                          prop.custom ("inputMode", "decimal")
                          aria "describedby" "price-hint"
                          prop.value model.Price
                          prop.onChange (fun (v: string) -> dispatch (SetPrice v)) ])
                field
                    "asset-text"
                    "Asset: paste text or code"
                    "Ignored if you choose a file below."
                    (Html.textarea
                        [ prop.id "asset-text"
                          prop.rows 10
                          aria "describedby" "asset-text-hint"
                          prop.disabled model.AssetFile.IsSome
                          prop.value model.AssetText
                          prop.onChange (fun (v: string) -> dispatch (SetAssetText v)) ])
                field
                    "asset-file"
                    "Asset: or choose a file"
                    "A text or code file, or a PNG, JPEG, GIF or WebP image, up to 3.5 MB."
                    (Html.input
                        [ prop.id "asset-file"
                          prop.custom ("type", "file")
                          aria "describedby" "asset-file-hint"
                          prop.onChange (fun (ev: Browser.Types.Event) -> dispatch (FilePicked(pickedFile (box ev)))) ])
                (if model.AssetFileName = "" then
                     Html.none
                 else
                     Html.p [ prop.className "hint"; prop.text ("Selected file: " + model.AssetFileName) ])
                Html.button
                    [ prop.className "btn btn-primary"
                      prop.disabled busy
                      prop.onClick (fun _ -> dispatch SubmitUpload)
                      prop.text "Upload and protect asset" ]
                (match model.Project with
                 | Some p ->
                     Html.div
                         [ prop.className "panel"
                           prop.children
                               [ Html.h3 [ prop.text "Current project" ]
                                 Html.p [ prop.text (p.title + " - $" + p.priceDisplay + " " + p.currency) ]
                                 Html.p [ prop.text "Project ID to share with your client:" ]
                                 Html.pre [ prop.className "plain"; prop.tabIndex 0; prop.text p.id ]
                                 statusPill p.status
                                 Html.button
                                     [ prop.className "btn"
                                       prop.onClick (fun _ -> dispatch (SetScreen Client))
                                       prop.text "Open the client view for this project" ] ] ]
                 | None -> Html.none) ] ]

let private clientView (model: Model) (dispatch: Msg -> unit) =
    let busy = model.Busy.IsSome

    Html.section
        [ aria "labelledby" "client-heading"
          prop.children
              [ Html.h2 [ prop.id "client-heading"; prop.className "sr-only"; prop.text "Review and pay" ]
                field
                    "project-id"
                    "Project ID"
                    "The freelancer gives you this ID."
                    (Html.input
                        [ prop.id "project-id"
                          prop.custom ("type", "text")
                          aria "describedby" "project-id-hint"
                          prop.value model.ProjectIdInput
                          prop.onChange (fun (v: string) -> dispatch (SetProjectIdInput v)) ])
                Html.button
                    [ prop.className "btn btn-primary"
                      prop.disabled busy
                      prop.onClick (fun _ -> dispatch LoadProject)
                      prop.text "Load project" ]
                (match model.Project with
                 | None -> Html.none
                 | Some p ->
                     Html.div
                         [ prop.children
                               [ Html.div
                                     [ prop.className "panel"
                                       prop.children
                                           [ Html.h3 [ prop.text p.title ]
                                             Html.p [ prop.text ("Price: $" + p.priceDisplay + " " + p.currency) ]
                                             (if p.uploadedBy = "" then
                                                  Html.none
                                              else
                                                  Html.p [ prop.className "hint"; prop.text ("Uploaded by " + p.uploadedBy) ])
                                             (if p.paidBy = "" then
                                                  Html.none
                                              else
                                                  Html.p [ prop.className "hint"; prop.text ("Paid by " + p.paidBy) ])
                                             statusPill p.status
                                             (match p.reviewDeadlineUtc with
                                              | Some deadline when p.status = "ESCROW_HELD" ->
                                                  Html.p
                                                      [ prop.text (
                                                            "Review window closes "
                                                            + localTime deadline
                                                            + ". After that the funds are released to the freelancer."
                                                        ) ]
                                              | _ -> Html.none) ] ]
                                 summaryView p
                                 assetView p
                                 (match p.status with
                                  | "AWAITING_PAYMENT" ->
                                      Html.section
                                          [ prop.className "panel"
                                            aria "labelledby" "pay-heading"
                                            prop.children
                                                [ Html.h3 [ prop.id "pay-heading"; prop.text "Pay into escrow" ]
                                                  Html.p
                                                      [ prop.text (
                                                            "Pay $"
                                                            + p.priceDisplay
                                                            + " with PayPal. The money is held in escrow, the full asset unlocks, and you get 24 hours to review it."
                                                        ) ]
                                                  (match model.Config with
                                                   | Some cfg when cfg.paypalConfigured ->
                                                       Html.div
                                                           [ prop.key ("paypal-" + p.id)
                                                             prop.id paypalContainerId
                                                             prop.className "paypal-box" ]
                                                   | _ ->
                                                       Html.p
                                                           [ prop.className "hint"
                                                             prop.text
                                                                 "PayPal is not configured on the API, so checkout is unavailable." ]) ] ]
                                  | "ESCROW_HELD" ->
                                      Html.button
                                          [ prop.className "btn"
                                            prop.onClick (fun _ -> dispatch (SetScreen Dispute))
                                            prop.text "Something wrong? Open a dispute" ]
                                  | _ -> Html.none)
                                 verdictView p ] ]) ] ]

let private disputeView (model: Model) (dispatch: Msg -> unit) =
    let busy = model.Busy.IsSome

    Html.section
        [ aria "labelledby" "dispute-heading"
          prop.children
              [ Html.h2 [ prop.id "dispute-heading"; prop.className "sr-only"; prop.text "Disputes" ]
                Html.p
                    [ prop.text
                          "Claude Fable 5.2 compares the delivered asset with the original requirements. If the delivery matches, the funds are released to the freelancer. If it does not, PayPal refunds the client." ]
                (match model.Project with
                 | None ->
                     Html.p
                         [ prop.className "hint"
                           prop.text "No project loaded. Open Review and pay, or pick a project from the Projects list." ]
                 | Some p ->
                     Html.div
                         [ prop.children
                               [ Html.div
                                     [ prop.className "panel"
                                       prop.children
                                           [ Html.h3 [ prop.text p.title ]
                                             statusPill p.status
                                             Html.ul
                                                 [ prop.className "summary"
                                                   prop.children
                                                       [ for i, bullet in Array.indexed p.requirementsSummary ->
                                                             Html.li [ prop.key (string i); prop.text bullet ] ] ] ] ]
                                 (if p.status = "ESCROW_HELD" then
                                      Html.div
                                          [ prop.children
                                                [ field
                                                      "complaint"
                                                      "What is wrong with the delivery?"
                                                      "Be specific: say which requirement was not met."
                                                      (Html.textarea
                                                          [ prop.id "complaint"
                                                            prop.rows 5
                                                            aria "describedby" "complaint-hint"
                                                            prop.value model.Complaint
                                                            prop.onChange (fun (v: string) -> dispatch (SetComplaint v)) ])
                                                  Html.button
                                                      [ prop.className "btn btn-danger"
                                                        prop.disabled busy
                                                        prop.onClick (fun _ -> dispatch SubmitDispute)
                                                        prop.text "Submit dispute to the arbitrator" ] ] ]
                                  elif p.verdict.IsNone then
                                      Html.p
                                          [ prop.className "hint"
                                            prop.text "Only projects with payment held in escrow can be disputed." ]
                                  else
                                      Html.none)
                                 verdictView p ] ]) ] ]

// ---------------------------------------------------------------------------------------------
// Dashboard pieces
// ---------------------------------------------------------------------------------------------

let private headerCell (text: string) =
    Html.th [ prop.custom ("scope", "col"); prop.text text ]

let private money (amount: float) : ReactElement =
    reactBit
        countUpComponent
        [ "to" ==> amount
          "prefix" ==> "$"
          "decimals" ==> 2
          "separator" ==> ","
          "duration" ==> 0.9
          "trigger" ==> "load" ]

let private statTile (label: string) (amount: float) (count: int) (note: string) =
    Html.div
        [ prop.className "tile"
          prop.children
              [ Html.p [ prop.className "tile-label"; prop.text label ]
                Html.p [ prop.className "tile-value"; prop.children [ money amount ] ]
                Html.p
                    [ prop.className "tile-note"
                      prop.text (string count + (if count = 1 then " project. " else " projects. ") + note) ] ] ]

let private statusPillShort (status: string) =
    let cls, label =
        match status with
        | "AWAITING_PAYMENT" -> "pill pill-info", "Awaiting payment"
        | "ESCROW_HELD" -> "pill pill-warning", "In escrow"
        | "COMPLETED" -> "pill pill-success", "Completed"
        | "REFUNDED" -> "pill pill-danger", "Refunded"
        | other -> "pill pill-info", other

    Html.span [ prop.className cls; prop.text label ]

/// compact leaves out the Created and Verdict columns, for the narrow panel on the overview.
let private projectsTable (caption: string) (compact: bool) (rows: ProjectRowDto[]) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "table-wrap"
          prop.children
              [ Html.table
                    [ prop.className "data-table"
                      prop.children
                          [ Html.caption [ prop.className "sr-only"; prop.text caption ]
                            Html.thead
                                [ prop.children
                                      [ Html.tr
                                            [ prop.children
                                                  [ headerCell "Project"
                                                    headerCell "Price"
                                                    headerCell "Status"
                                                    if not compact then
                                                        headerCell "Created"
                                                        headerCell "Verdict"
                                                    headerCell "Open" ] ] ] ]
                            Html.tbody
                                [ prop.children
                                      [ for row in rows ->
                                            Html.tr
                                                [ prop.key row.id
                                                  prop.children
                                                      [ Html.td
                                                            [ prop.children
                                                                  [ Html.span [ prop.className "cell-title"; prop.text row.title ]
                                                                    Html.span
                                                                        [ prop.className "cell-sub"
                                                                          prop.text (
                                                                              if row.assetKind = "image" then
                                                                                  "Image"
                                                                              else
                                                                                  "Text or code"
                                                                          ) ] ] ]
                                                        Html.td [ prop.className "num"; prop.text ("$" + row.priceDisplay) ]
                                                        Html.td [ statusPillShort row.status ]
                                                        if not compact then
                                                            Html.td [ prop.className "num"; prop.text (localDate row.createdUtc) ]

                                                            Html.td
                                                                [ prop.text (
                                                                      match row.verdict, row.matchScore with
                                                                      | Some "RELEASE", Some score ->
                                                                          "Release, " + string score + "/100"
                                                                      | Some _, Some score -> "Refund, " + string score + "/100"
                                                                      | _ -> "None"
                                                                  ) ]
                                                        Html.td
                                                            [ Html.button
                                                                  [ prop.className "btn btn-small"
                                                                    aria "label" ("Open " + row.title)
                                                                    prop.onClick (fun _ -> dispatch (OpenProject row.id))
                                                                    prop.text "Open" ] ] ] ] ] ] ] ] ] ]

let private activityLabel (kind: string) =
    match kind with
    | "CREATED" -> "pill pill-info", "Created"
    | "CHECKOUT_STARTED" -> "pill pill-info", "Checkout started"
    | "PAID" -> "pill pill-warning", "Paid into escrow"
    | "RELEASED" -> "pill pill-success", "Released"
    | "REFUNDED" -> "pill pill-danger", "Refunded"
    | other -> "pill pill-info", other

let private activityList (entries: ActivityDto[]) =
    Html.ol
        [ prop.className "activity"
          prop.children
              [ for i, entry in Array.indexed entries do
                    let cls, label = activityLabel entry.kind

                    Html.li
                        [ prop.key (string i)
                          prop.children
                              [ Html.p
                                    [ prop.className "activity-head"
                                      prop.children
                                          [ Html.span [ prop.className cls; prop.text label ]
                                            Html.span [ prop.className "activity-title"; prop.text entry.projectTitle ] ] ]
                                Html.p [ prop.text entry.message ]
                                Html.p [ prop.className "hint"; prop.text (localTime entry.at) ] ] ] ] ]

let private emptyState (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "panel empty"
          prop.children
              [ Html.h3 [ prop.text "No projects yet" ]
                Html.p [ prop.text "Upload a finished piece of work and it appears here with its escrow status." ]
                Html.button
                    [ prop.className "btn btn-primary"
                      prop.onClick (fun _ -> dispatch (SetScreen Freelancer))
                      prop.text "Upload finished work" ] ] ]

let private overviewView (model: Model) (dispatch: Msg -> unit) =
    match model.Dashboard with
    | None -> Html.p [ prop.className "hint"; prop.text "Loading the dashboard." ]
    | Some d ->
        Html.div
            [ prop.children
                  [ Html.section
                        [ prop.className "tiles"
                          aria "label" "Money by escrow state"
                          prop.children
                              [ statTile "Awaiting payment" d.totals.awaitingAmount d.totals.awaitingCount "Clients see a preview only."
                                statTile "Held in escrow" d.totals.escrowAmount d.totals.escrowCount "In the 24-hour review."
                                statTile "Released to freelancers" d.totals.releasedAmount d.totals.releasedCount "Approved or review ended."
                                statTile "Refunded to clients" d.totals.refundedAmount d.totals.refundedCount "Arbitrator ruled refund." ] ]
                    (if d.projects.Length = 0 then
                         emptyState dispatch
                     else
                         Html.div
                             [ prop.className "columns"
                               prop.children
                                   [ Html.section
                                         [ prop.className "panel"
                                           aria "labelledby" "recent-projects"
                                           prop.children
                                               [ Html.div
                                                     [ prop.className "panel-head"
                                                       prop.children
                                                           [ Html.h3 [ prop.id "recent-projects"; prop.text "Recent projects" ]
                                                             Html.button
                                                                 [ prop.className "btn btn-small"
                                                                   prop.onClick (fun _ -> dispatch (SetScreen Projects))
                                                                   prop.text "See all projects" ] ] ]
                                                 projectsTable "The five newest projects" true (Array.truncate 5 d.projects) dispatch ] ]
                                     Html.section
                                         [ prop.className "panel"
                                           aria "labelledby" "recent-activity"
                                           prop.children
                                               [ Html.div
                                                     [ prop.className "panel-head"
                                                       prop.children
                                                           [ Html.h3 [ prop.id "recent-activity"; prop.text "Recent activity" ]
                                                             Html.button
                                                                 [ prop.className "btn btn-small"
                                                                   prop.onClick (fun _ -> dispatch (SetScreen Activity))
                                                                   prop.text "See all activity" ] ] ]
                                                 activityList (Array.truncate 5 d.activity) ] ] ] ]) ] ]

let private projectsView (model: Model) (dispatch: Msg -> unit) =
    match model.Dashboard with
    | None -> Html.p [ prop.className "hint"; prop.text "Loading projects." ]
    | Some d when d.projects.Length = 0 -> emptyState dispatch
    | Some d ->
        let search = model.Search.Trim().ToLower()

        let rows =
            d.projects
            |> Array.filter (fun (row: ProjectRowDto) ->
                (model.StatusFilter = "ALL" || row.status = model.StatusFilter)
                && (search = "" || row.title.ToLower().Contains(search)))

        Html.div
            [ prop.children
                  [ Html.div
                        [ prop.className "filters"
                          prop.children
                              [ Html.div
                                    [ prop.className "filter"
                                      prop.children
                                          [ Html.label [ prop.htmlFor "search"; prop.text "Search by title" ]
                                            Html.input
                                                [ prop.id "search"
                                                  prop.custom ("type", "text")
                                                  prop.value model.Search
                                                  prop.onChange (fun (v: string) -> dispatch (SetSearch v)) ] ] ]
                                Html.div
                                    [ prop.className "filter"
                                      prop.children
                                          [ Html.label [ prop.htmlFor "status-filter"; prop.text "Status" ]
                                            Html.select
                                                [ prop.id "status-filter"
                                                  prop.value model.StatusFilter
                                                  prop.onChange (fun (v: string) -> dispatch (SetStatusFilter v))
                                                  prop.children
                                                      [ Html.option [ prop.value "ALL"; prop.text "All statuses" ]
                                                        Html.option [ prop.value "AWAITING_PAYMENT"; prop.text "Awaiting payment" ]
                                                        Html.option [ prop.value "ESCROW_HELD"; prop.text "Held in escrow" ]
                                                        Html.option [ prop.value "COMPLETED"; prop.text "Completed" ]
                                                        Html.option [ prop.value "REFUNDED"; prop.text "Refunded" ] ] ] ] ] ] ]
                    Html.p
                        [ prop.className "hint"
                          prop.text (string rows.Length + " of " + string d.projects.Length + " projects shown.") ]
                    (if rows.Length = 0 then
                         Html.p [ prop.text "No project matches. Clear the search or choose All statuses." ]
                     else
                         Html.div [ prop.className "panel"; prop.children [ projectsTable "All projects, newest first" false rows dispatch ] ]) ] ]

let private activityView (model: Model) =
    match model.Dashboard with
    | None -> Html.p [ prop.className "hint"; prop.text "Loading activity." ]
    | Some d when d.activity.Length = 0 ->
        Html.p [ prop.text "Nothing has happened yet. Uploads, payments, releases and refunds are listed here." ]
    | Some d -> Html.div [ prop.className "panel"; prop.children [ activityList d.activity ] ]

/// The theme switch: the knob sits on the left with a sun for the light theme and slides to the
/// right with a moon for the night theme. Screen readers hear a switch named "Night theme".
let private themeToggle (model: Model) (dispatch: Msg -> unit) =
    Html.button
        [ prop.className "theme-switch"
          prop.custom ("role", "switch")
          aria "checked" (if model.Light then "false" else "true")
          aria "label" "Night theme"
          prop.custom ("title", "Left is the light theme, right is the night theme")
          prop.onClick (fun _ -> dispatch ToggleTheme)
          prop.children
              [ Html.span [ prop.className "icon icon-sun"; aria "hidden" "true" ]
                Html.span [ prop.className "icon icon-moon"; aria "hidden" "true" ]
                Html.span
                    [ prop.className "switch-knob"
                      aria "hidden" "true"
                      prop.children
                          [ Html.span [ prop.className (if model.Light then "icon icon-sun" else "icon icon-moon") ] ] ] ] ]

let private integrationRow (name: string) (detail: string) (ready: bool) =
    Html.div
        [ prop.className "integration"
          prop.children
              [ Html.div
                    [ prop.children
                          [ Html.p [ prop.className "integration-name"; prop.text name ]
                            Html.p [ prop.className "hint"; prop.text detail ] ] ]
                Html.span
                    [ prop.className (if ready then "pill pill-success" else "pill pill-danger")
                      prop.text (if ready then "Connected" else "Not configured") ] ] ]

let private settingsView (model: Model) (dispatch: Msg -> unit) =
    let paypalReady, claudeReady =
        match model.Dashboard, model.Config with
        | Some d, _ -> d.integrations.paypalConfigured, d.integrations.claudeConfigured
        | None, Some cfg -> cfg.paypalConfigured, cfg.claudeConfigured
        | None, None -> false, false

    Html.div
        [ prop.children
              [ Html.section
                    [ prop.className "panel"
                      aria "labelledby" "integrations-heading"
                      prop.children
                          [ Html.h3 [ prop.id "integrations-heading"; prop.text "Integrations" ]
                            integrationRow
                                "PayPal Sandbox"
                                "Orders, captures and refunds. Needs PAYPAL_CLIENT_ID and PAYPAL_SECRET on the API."
                                paypalReady
                            integrationRow
                                "Claude Fable 5.2"
                                "Plain-language summaries and the dispute arbitrator. Needs ANTHROPIC_API_KEY on the API."
                                claudeReady ] ]
                Html.section
                    [ prop.className "panel"
                      aria "labelledby" "display-heading"
                      prop.children
                          [ Html.h3 [ prop.id "display-heading"; prop.text "Display and voice" ]
                            Html.p
                                [ prop.text
                                      "Voice reads payment confirmations and verdicts aloud. Turn it off if you already use a screen reader, which announces the same messages." ]
                            Html.button
                                [ prop.className "btn"
                                  aria "pressed" (if model.SpeechOn then "true" else "false")
                                  prop.onClick (fun _ -> dispatch ToggleSpeech)
                                  prop.text (if model.SpeechOn then "Voice on" else "Voice off") ]
                            themeToggle model dispatch ] ]
                Html.section
                    [ prop.className "panel"
                      aria "labelledby" "data-heading"
                      prop.children
                          [ Html.h3 [ prop.id "data-heading"; prop.text "About this demo" ]
                            Html.p
                                [ prop.text
                                      "Projects and activity are kept in the API's memory and are cleared when it restarts. Payments run in the PayPal Sandbox, so no real money moves. There is no sign-in: anyone who can open this page can see every project." ] ] ] ] ]

// ---------------------------------------------------------------------------------------------
// App shell: sidebar, page header, current screen
// ---------------------------------------------------------------------------------------------

let private screenTitle (screen: Screen) =
    match screen with
    | Overview -> "Overview", "Where every payment stands right now."
    | Projects -> "Projects", "Every delivery, with its escrow status."
    | Freelancer -> "New project", "Upload finished work. The client sees only a protected preview until they pay."
    | Client -> "Review and pay", "Preview a delivery, pay into escrow with PayPal, then review the full work."
    | Dispute -> "Disputes", "Claude checks the delivery against the brief and rules release or refund."
    | Activity -> "Activity", "Uploads, payments, releases and refunds, newest first."
    | Settings -> "Settings and status", "Integrations, display and voice."

let private navItem (model: Model) (dispatch: Msg -> unit) (screen: Screen) (label: string) =
    let current = model.Screen = screen

    Html.li
        [ prop.children
              [ Html.button
                    [ prop.className (if current then "nav-item nav-current" else "nav-item")
                      aria "current" (if current then "page" else "false")
                      prop.onClick (fun _ -> dispatch (SetScreen screen))
                      prop.text label ] ] ]

let private appView (model: Model) (dispatch: Msg -> unit) =
    let title, subtitle = screenTitle model.Screen

    let paypalReady, claudeReady =
        match model.Config with
        | Some cfg -> cfg.paypalConfigured, cfg.claudeConfigured
        | None -> false, false

    let showsRefresh =
        match model.Screen with
        | Overview
        | Projects
        | Activity -> true
        | _ -> false

    Html.div
        [ prop.className "app"
          prop.children
              [ Html.a [ prop.className "skip-link"; prop.href "#workspace"; prop.text "Skip to the page content" ]
                Html.aside
                    [ prop.className "sidebar"
                      prop.children
                          [ Html.button
                                [ prop.className "brand brand-button"
                                  aria "label" "Plumbline, back to the website"
                                  prop.onClick (fun _ -> dispatch LeaveApp)
                                  prop.text "Plumbline" ]
                            Html.nav
                                [ aria "label" "App"
                                  prop.children
                                      [ Html.ul
                                            [ prop.className "nav-list"
                                              prop.children
                                                  [ navItem model dispatch Overview "Overview"
                                                    navItem model dispatch Projects "Projects"
                                                    navItem model dispatch Freelancer "New project"
                                                    navItem model dispatch Client "Review and pay"
                                                    navItem model dispatch Dispute "Disputes"
                                                    navItem model dispatch Activity "Activity"
                                                    navItem model dispatch Settings "Settings and status" ] ] ] ]
                            Html.div
                                [ prop.className "sidebar-user"
                                  prop.children
                                      [ Html.p
                                            [ prop.text (
                                                  match model.Me with
                                                  | Some me when me.loggedIn -> "Signed in with PayPal as " + me.name
                                                  | _ -> "Not signed in"
                                              ) ]
                                        Html.button
                                            [ prop.className "btn btn-small"
                                              prop.onClick (fun _ -> dispatch LogOut)
                                              prop.text "Log out" ] ] ]
                            Html.div
                                [ prop.className "sidebar-status"
                                  prop.children
                                      [ Html.p
                                            [ prop.text (
                                                  if paypalReady then
                                                      "PayPal Sandbox: connected"
                                                  else
                                                      "PayPal Sandbox: not configured"
                                              ) ]
                                        Html.p
                                            [ prop.text (
                                                  if claudeReady then
                                                      "Claude Fable 5.2: connected"
                                                  else
                                                      "Claude Fable 5.2: not configured"
                                              ) ] ] ] ] ]
                Html.div
                    [ prop.className "app-main"
                      prop.children
                          [ Html.header
                                [ prop.className "page-head"
                                  prop.children
                                      [ Html.div
                                            [ prop.children
                                                  [ Html.h1 [ prop.className "page-title"; prop.text title ]
                                                    Html.p [ prop.className "hint"; prop.text subtitle ] ] ]
                                        Html.div
                                            [ prop.className "toggles"
                                              prop.children
                                                  [ (if showsRefresh then
                                                         Html.button
                                                             [ prop.className "btn btn-small"
                                                               prop.onClick (fun _ -> dispatch RefreshDashboard)
                                                               prop.text "Refresh" ]
                                                     else
                                                         Html.none)
                                                    themeToggle model dispatch ] ] ] ]
                            Html.main
                                [ prop.id "workspace"
                                  prop.className "workspace"
                                  prop.tabIndex (-1)
                                  prop.children
                                      [ bannerView model.Banner
                                        (match model.Busy with
                                         | Some text -> Html.p [ prop.className "busy"; prop.text (text + "...") ]
                                         | None -> Html.none)
                                        (match model.Screen with
                                         | Overview -> overviewView model dispatch
                                         | Projects -> projectsView model dispatch
                                         | Freelancer -> freelancerView model dispatch
                                         | Client -> clientView model dispatch
                                         | Dispute -> disputeView model dispatch
                                         | Activity -> activityView model
                                         | Settings -> settingsView model dispatch) ] ] ] ] ] ]

// ---------------------------------------------------------------------------------------------
// Website: the public landing page
// ---------------------------------------------------------------------------------------------

let private step (number: string) (title: string) (detail: string) =
    Html.li
        [ prop.className "step"
          prop.children
              [ Html.span [ prop.className "step-number"; aria "hidden" "true"; prop.text number ]
                Html.h3 [ prop.className "step-title"; prop.text title ]
                Html.p [ prop.className "step-detail"; prop.text detail ] ] ]

let private reason (title: string) (detail: string) =
    Html.div
        [ prop.className "reason"
          prop.children [ Html.h3 [ prop.text title ]; Html.p [ prop.text detail ] ] ]

let private siteView (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.className "shell"
          prop.children
              [ Html.a [ prop.className "skip-link"; prop.href "#how"; prop.text "Skip to how it works" ]
                Html.header
                    [ prop.className "topbar"
                      prop.children
                          [ Html.p [ prop.className "brand"; prop.text "Plumbline" ]
                            Html.nav
                                [ prop.className "site-nav"
                                  aria "label" "Website"
                                  prop.children
                                      [ Html.a [ prop.href "#how"; prop.text "How it works" ]
                                        Html.a [ prop.href "#fair"; prop.text "Why it is fair" ]
                                        Html.a [ prop.href "#built"; prop.text "Built with" ] ] ]
                            Html.div
                                [ prop.className "toggles"
                                  prop.children
                                      [ themeToggle model dispatch
                                        Html.button
                                            [ prop.className "btn btn-small btn-primary"
                                              prop.onClick (fun _ -> dispatch (EnterApp Overview))
                                              prop.text (
                                                  match model.Me with
                                                  | Some me when me.loggedIn -> "Open the dashboard"
                                                  | _ -> "Log in with PayPal"
                                              ) ] ] ] ] ]
                bannerView model.Banner
                Html.main
                    [ prop.children
                          [ Html.section
                                [ prop.className "hero"
                                  aria "labelledby" "tagline"
                                  prop.children
                                      [ // The plumb line: a string and a weight that settles once when the page loads.
                                        Html.div
                                            [ prop.className "plumb"
                                              aria "hidden" "true"
                                              prop.children
                                                  [ Html.div [ prop.className "plumb-string" ]
                                                    Html.div [ prop.className "plumb-bob" ] ] ]
                                        // BlurText keeps a screen-reader copy of the sentence and honours reduced motion.
                                        Html.div
                                            [ prop.id "tagline"
                                              prop.className "hero-title"
                                              prop.custom ("role", "heading")
                                              prop.custom ("aria-level", "1")
                                              prop.children
                                                  [ reactBit
                                                        blurTextComponent
                                                        [ "text" ==> "Get paid when the work is true."
                                                          "trigger" ==> "load"
                                                          "animateBy" ==> "words"
                                                          "direction" ==> "top"
                                                          "delay" ==> 90 ] ] ]
                                        Html.p
                                            [ prop.className "lede"
                                              prop.text
                                                  "Plumbline holds the client's PayPal payment in escrow while they review the delivery. If they dispute it, Claude checks the work against the original brief, and the money goes where the work says it should." ]
                                        Html.div
                                            [ prop.className "hero-actions"
                                              prop.children
                                                  [ Html.button
                                                        [ prop.className "btn btn-primary"
                                                          prop.onClick (fun _ -> dispatch (EnterApp Freelancer))
                                                          prop.text "Upload finished work" ]
                                                    Html.button
                                                        [ prop.className "btn"
                                                          prop.onClick (fun _ -> dispatch (EnterApp Client))
                                                          prop.text "Review a delivery" ] ] ] ] ]
                            Html.section
                                [ prop.id "how"
                                  prop.className "band"
                                  aria "labelledby" "how-heading"
                                  prop.children
                                      [ Html.h2 [ prop.id "how-heading"; prop.text "How it works" ]
                                        Html.ol
                                            [ prop.className "steps"
                                              prop.children
                                                  [ step
                                                        "1"
                                                        "The freelancer uploads"
                                                        "Finished work goes in with the client's original brief. The client can open a masked or blurred preview, never the real file."
                                                    step
                                                        "2"
                                                        "The client pays into escrow"
                                                        "PayPal takes the payment. The full work unlocks for a 24-hour review, and Claude turns the brief into a short plain-language list."
                                                    step
                                                        "3"
                                                        "Claude settles any dispute"
                                                        "The arbitrator scores the delivery against the brief. A match releases the money to the freelancer; a miss refunds the client through PayPal." ] ] ] ]
                            Html.section
                                [ prop.id "fair"
                                  prop.className "band"
                                  aria "labelledby" "fair-heading"
                                  prop.children
                                      [ Html.h2 [ prop.id "fair-heading"; prop.text "Why it is fair" ]
                                        Html.div
                                            [ prop.className "reasons"
                                              prop.children
                                                  [ reason
                                                        "For freelancers"
                                                        "Nobody gets your work before they pay. A client who complains about something the brief never asked for does not get a refund."
                                                    reason
                                                        "For clients"
                                                        "You see a preview before paying and the full work before the money is released. If the delivery misses the brief, PayPal sends your payment back."
                                                    reason
                                                        "For people who cannot see or hear the screen"
                                                        "Payments and verdicts are read aloud and announced to screen readers. Every status is written out in words, with colour as a second signal." ] ] ] ]
                            Html.section
                                [ prop.id "built"
                                  prop.className "band"
                                  aria "labelledby" "built-heading"
                                  prop.children
                                      [ Html.h2 [ prop.id "built-heading"; prop.text "Built with" ]
                                        Html.dl
                                            [ prop.className "stack"
                                              prop.children
                                                  [ Html.dt [ prop.text "PayPal Sandbox" ]
                                                    Html.dd [ prop.text "Orders, captures and refunds through the REST API." ]
                                                    Html.dt [ prop.text "Claude Fable 5.2" ]
                                                    Html.dd [ prop.text "Low effort for summaries, high effort for the arbitrator." ]
                                                    Html.dt [ prop.text ".NET 8 minimal API" ]
                                                    Html.dd [ prop.text "Holds the real file and only ever sends out what has been paid for." ]
                                                    Html.dt [ prop.text "Fable, Feliz and React Bits" ]
                                                    Html.dd [ prop.text "An F# front end with animated React components." ] ] ] ] ]
                            Html.section
                                [ prop.className "band closing"
                                  aria "labelledby" "closing-heading"
                                  prop.children
                                      [ Html.h2 [ prop.id "closing-heading"; prop.text "See where every payment stands." ]
                                        Html.button
                                            [ prop.className "btn btn-primary"
                                              prop.onClick (fun _ -> dispatch (EnterApp Overview))
                                              prop.text "Open the dashboard" ] ] ] ] ]
                Html.footer
                    [ prop.children
                          [ Html.p
                                [ prop.text
                                      "Plumbline is a sandbox demo for the PayPal hackathon. Payments use PayPal Sandbox accounts, so no real money moves." ] ] ] ] ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div
        [ prop.children
              [ // Screen readers announce every status change from this region.
                Html.div
                    [ prop.className "sr-only"
                      prop.custom ("role", "status")
                      aria "live" "polite"
                      aria "atomic" "true"
                      prop.text model.Announcement ]
                (if model.InApp then
                     appView model dispatch
                 else
                     siteView model dispatch) ] ]

Program.mkProgram init update view
|> Program.withReactSynchronous "root"
|> Program.run
