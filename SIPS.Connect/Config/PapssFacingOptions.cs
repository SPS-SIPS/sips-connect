namespace SIPS.Connect.Config;

public sealed class PapssFacingOptions
{
    public const string SectionName = "PapssFacing";
    public bool Enabled { get; set; }
    public string IsoIngressUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string RemoteWpSipsIdentity { get; set; } = string.Empty;
    public string SecurityProfile { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaximumResponseBytes { get; set; } = 2_000_000;
    public string[] AllowedHosts { get; set; } = [];
    public PapssSpsPolicy SpsPolicy { get; set; } = new();
    public int ReadinessStaleSeconds { get; set; } = 300;
    public string LocalCountry { get; set; } = string.Empty;
    public string[] SendingCurrencies { get; set; } = [];
    public string? CallbackMappingProfile { get; set; }
    public string? CallbackUrl { get; set; }
    public PapssResponderTrust ResponderTrust { get; set; } = new();
    public PapssInboundOptions Inbound { get; set; } = new();
    public PapssOutboundOptions Outbound { get; set; } = new();
    public PapssDeliveryOptions Delivery { get; set; } = new();
    public PapssLookupOptions Lookup { get; set; } = new();
    public PapssStoreOptions Store { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// PAPSS operation store settings. Every value below is optional. Defaults preserve the behaviour
// that existed before the store was introduced; none of them is a PAPSS-published protocol value.
//
// Unresolved PAPSS behaviour (do NOT invent values or protocol behaviour for these; see
// docs/PAPSS_OPERATION_STORE_CONFIG.md "Unresolved PAPSS behaviour"):
//  * the authoritative clock for inbound response deadlines (source CreDt vs. receipt time);
//  * per-message-type timeout values (none established for acmt.023; pacs.008 material conflicts);
//  * whether a participant may send a negative acmt.024 when its core bank times out, and with which
//    Rsn code (PAPSS material contradicts itself: numeric 1000-1010 vs ISO MS03/AC01);
//  * whether a reason is mandatory for Vrfctn=false;
//  * whether late responses must be suppressed.
// Consequently no automatic negative/timeout reply exists, and the deadline is record-only.
// ---------------------------------------------------------------------------------------------

public sealed class PapssInboundOptions
{
    public PapssAcmt023Options Acmt023 { get; set; } = new();
    /// <summary>What to do with a reply that is ready after its recorded deadline. Default Submit (unchanged behaviour).</summary>
    public PapssLateResponsePolicy LateResponsePolicy { get; set; } = PapssLateResponsePolicy.Submit;
}

public sealed class PapssAcmt023Options
{
    /// <summary>UNRESOLVED PAPSS value. Null (default) = no deadline is computed.</summary>
    public int? ResponseDeadlineSeconds { get; set; }
    /// <summary>UNRESOLVED PAPSS value. Null (default) = no deadline is computed.</summary>
    public PapssDeadlineClock? DeadlineClock { get; set; }
}

public enum PapssDeadlineClock
{
    /// <summary>AppHdr CreDt of the inbound message.</summary>
    SourceCreationTime,
    /// <summary>The time SIPS Connect received the inbound message.</summary>
    ReceivedAt
}

public enum PapssLateResponsePolicy
{
    /// <summary>Submit the reply regardless of the deadline (a warning is logged when late).</summary>
    Submit,
    /// <summary>Do not submit a reply that is already past its deadline; it is kept as HELD for operators.</summary>
    Hold
}

public sealed class PapssOutboundOptions
{
    /// <summary>Null (default) = a pending outbound verification never auto-expires; lookups report its age.</summary>
    public int? VerificationResultExpirySeconds { get; set; }
}

/// <summary>SPS-internal operational defaults for the bank push and gateway reply outboxes (not PAPSS rules).</summary>
public sealed class PapssDeliveryOptions
{
    public int MaxAttempts { get; set; } = 10;
    public int InitialBackoffSeconds { get; set; } = 5;
    public int MaxBackoffSeconds { get; set; } = 300;
    public int PollIntervalSeconds { get; set; } = 5;
    /// <summary>How long a claimed item is reserved for one worker before another instance may retry it.</summary>
    public int ClaimLeaseSeconds { get; set; } = 120;

    public TimeSpan Backoff(int attempts)
    {
        var initial = Math.Max(1, InitialBackoffSeconds);
        var max = Math.Max(initial, MaxBackoffSeconds);
        var exponent = Math.Clamp(attempts - 1, 0, 20);
        return TimeSpan.FromSeconds(Math.Min(max, initial * Math.Pow(2, exponent)));
    }
}

public sealed class PapssLookupOptions
{
    /// <summary>Maximum ?waitSeconds long-poll on the lookup API. 0 (default) disables long-polling.</summary>
    public int MaxWaitSeconds { get; set; }
}

public sealed class PapssStoreOptions
{
    /// <summary>Null (default) = never purge. When set, completed operations/events older than this are deleted.</summary>
    public int? RetentionDays { get; set; }
    public int PurgeIntervalMinutes { get; set; } = 60;
}

public sealed class PapssResponderTrust
{
    public string CertificateSha256 { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string TrustProfileVersion { get; set; } = string.Empty;
    public string RequiredExtendedKeyUsageOid { get; set; } = string.Empty;
}

public sealed class PapssSpsPolicy
{
    public string[] AllowedLocalInstruments { get; set; } = [];
}
