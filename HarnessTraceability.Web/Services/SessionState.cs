namespace HarnessTraceability.Web.Services;
public sealed class SessionState
{
    public bool IsAuthenticated { get; set; }
    public string UserName { get; set; } = "";
    public string UserMail { get; set; } = "";
    public List<string> Sites { get; set; } = new();
    public string? SelectedSite { get; set; }
}
