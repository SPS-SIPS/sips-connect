namespace SIPS.Connect;

public static class KnownRoles
{
    public const string ManageMassages = "iso_messages";
    public const string ManageTransactions = "transactions";
    public const string Admin = "configuration";
    public const string Gateway = "gateway";
    public const string QR = "som_qr";
    public const string Recon = "recon";
    public const string Dashboard = "dashboard";
    public const string Logs = "logs";
    /// <summary>
    /// Narrow, machine-only capability: read a PAPSS recall and manually close one stuck open
    /// (POST Recall/{recallId}/Close). Granted per API key via ApiKey.Roles in configuration, never
    /// automatically to every API key. Distinct from <see cref="Recon"/> (the human/operator role, which also
    /// covers Retry and the other reconciliation endpoints): an API party gets only this one capability, not the
    /// broader operator surface.
    /// </summary>
    public const string RecallClose = "papss_recall_close";
}