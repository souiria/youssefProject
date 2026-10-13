using System.Data;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using System.Net.Sockets;

namespace HarnessTraceability.Web.Services;
public sealed class TraceabilityDataService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _clients;
    // Keep both the legacy authentication site codes and the friendly names used by older forms.
    private static readonly IReadOnlyDictionary<string,string> Servers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["K1"]="MOKEMLS001", ["MOKE"]="MOKEMLS001", ["K2"]="MOASMLS001", ["MOAS"]="MOASMLS001",
        ["MFZ"]="MOFZMLS001", ["MOFZ"]="MOFZMLS001", ["AA"]="MOAAMLS001", ["MOAA"]="MOAAMLS001",
        ["Skirat"]="MOSKMLS001", ["SKHIRAT"]="MOSKMLS001", ["MOSK"]="MOSKMLS001",
        ["PortSaid"]="EGPSMLS001", ["Port Said"]="EGPSMLS001", ["EGPS"]="EGPSMLS001",
        ["TenthRamadan"]="EGTRMLS001", ["10 Ramadan"]="EGTRMLS001", ["EGTR"]="EGTRMLS001",
        ["SixthOctober"]="EGSOMLS001", ["6 October"]="EGSOMLS001", ["EGSO"]="EGSOMLS001",
        ["Alba"]="ROAIMLS001", ["ROAI"]="ROAIMLS001", ["Deva"]="RODVMLS001", ["RODV"]="RODVMLS001",
        ["Monastir"]="TNMOMLS001", ["TNMO"]="TNMOMLS001"
    };
    public TraceabilityDataService(IConfiguration configuration, IHttpClientFactory clients) { _configuration=configuration; _clients=clients; }
    public IReadOnlyList<string> SiteNames => Servers.Keys.ToList();
    public string? ServerFor(string site)
    {
        if (string.IsNullOrWhiteSpace(site)) return null;
        var key = site.Trim();
        if (Servers.TryGetValue(key, out var server)) return server;
        var compact = key.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return Servers.TryGetValue(compact, out server) ? server : null;
    }
    private string Connection(string site)
    {
        var server = ServerFor(site) ?? throw new InvalidOperationException($"Unknown site '{site}'.");
        var template = _configuration.GetConnectionString("Wtr");
        if (string.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("ConnectionStrings:Wtr is not configured.");
        // The selected site is resolved for every operation; changing site cannot reuse the previous server.
        var connection = new SqlConnectionStringBuilder(template.Replace("{server}", server, StringComparison.OrdinalIgnoreCase));
        // The legacy desktop app used named-pipe fallback. The web app must use TCP explicitly.
        if (!connection.DataSource.Contains(',')) connection.DataSource = $"tcp:{connection.DataSource},{_configuration["SqlServer:Port"] ?? "1433"}";
        connection.ConnectTimeout = int.TryParse(_configuration["SqlServer:ConnectTimeoutSeconds"], out var timeout) ? timeout : 15;
        connection.ApplicationName = "Harness Traceability Blazor";
        return connection.ConnectionString;
    }
    private async Task<DataTable> QueryAsync(string site, string from, string fields, string where, CancellationToken token=default)
    {
        await using var conn = new SqlConnection(Connection(site));
        await OpenAsync(conn, site, token);
        await using var cmd = new SqlCommand($"SELECT {fields} FROM {from} WHERE {where}", conn) { CommandTimeout=120 };
        using var reader = await cmd.ExecuteReaderAsync(token); var table = new DataTable(); table.Load(reader); return table;
    }
    private async Task OpenAsync(SqlConnection connection, string site, CancellationToken token)
    {
        try { await connection.OpenAsync(token); }
        catch (SqlException ex) when (ex.Number == 53 || ex.Number == 40 || ex.InnerException is SocketException)
        {
            var server = ServerFor(site) ?? site;
            throw new InvalidOperationException($"Cannot resolve or reach SQL Server '{server}' for selected site '{site}'. Run the Blazor server on the SEWS-E corporate network/VPN, verify corporate DNS, and allow TCP port 1433. Original error: {ex.Message}", ex);
        }
    }
    public async Task<AuthResult> AuthenticateAsync(string username, CancellationToken token=default)
    {
        var url = _configuration["Authentication:Url"]; if (string.IsNullOrWhiteSpace(url)) return AuthResult.Fail("Authentication endpoint is not configured.");
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(username), "username");
        var response = await _clients.CreateClient("authentication").PostAsync(url, form, token); if (!response.IsSuccessStatusCode) return AuthResult.Fail($"Authentication failed: {response.StatusCode}");
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(token)); var sites = xml.Root?.Element("Sites")?.Elements().Where(x=>string.Equals(x.Value,"True",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Name.LocalName).ToList() ?? new();
        return sites.Count == 0 ? AuthResult.Fail("User does not exist or does not have access.") : AuthResult.Ok(sites);
    }
    public Task<DataTable> TraceabilityAsync(string site, string label, bool byFinalLabel, CancellationToken token=default) => QueryAsync(site,"correlations_trial","DISTINCT Ref AS Reference, BC AS [Travel Ticket Label], Test AS Station, IN_Barcode_1 AS [2nd Label], IN_Barcode_2 AS [3rd Travel Ticket Label], IN_Barcode_3 AS [4th Travel Ticket Label], OUT_Barcode AS [Final Label], DateEnd AS [Test Date], Rework AS Reworked, Rework_ID AS [Rework ID], Hostname",$"{(byFinalLabel ? "OUT_Barcode" : "BC")} = @label",token);
    public async Task<DataTable> SearchTraceabilityAsync(string site,string label,bool byFinalLabel,CancellationToken token=default)
    { await using var conn=new SqlConnection(Connection(site)); await OpenAsync(conn, site, token); await using var cmd=new SqlCommand("SELECT DISTINCT Ref AS Reference, BC AS [Travel Ticket Label], Test AS Station, IN_Barcode_1 AS [2nd Label], IN_Barcode_2 AS [3rd Travel Ticket Label], IN_Barcode_3 AS [4th Travel Ticket Label], OUT_Barcode AS [Final Label], DateEnd AS [Test Date], Rework AS Reworked, Rework_ID AS [Rework ID], Hostname FROM correlations_trial WHERE "+(byFinalLabel?"OUT_Barcode":"BC")+"=@label",conn); cmd.Parameters.AddWithValue("@label",label); using var r=await cmd.ExecuteReaderAsync(token); var t=new DataTable(); t.Load(r); return t; }
    public Task<DataTable> HarnessDetailsAsync(string site,string harnessId,string host,CancellationToken token=default) => ParameterizedAsync(site,"SELECT Hostname, Bank AS Family, Ref AS Reference, Batch, Part AS [Travel Ticket], Worker, DateBegin AS [Start Test], DateEnd AS [End Test], TimeU, State, Incident, Type, Counterpart1, Counterpart2, Object, Wire1, Wire2, Way FROM dbo.tests WHERE Hostname=@host AND Part=@harness",new(){["@host"]=host,["@harness"]=harnessId},token);
    public async Task<DataTable> StatisticsAsync(string site,string host,DateTime start,DateTime end,CancellationToken token=default) => await ParameterizedAsync(site,"SELECT Ref AS [Reference], BC AS [Travel Ticket Label], Test AS Station, IN_Barcode_1 AS [2nd Label], IN_Barcode_2 AS [3rd Travel Ticket Label], IN_Barcode_3 AS [4th Travel Ticket Label], OUT_Barcode AS [Final Label], DateEnd AS [Test Date], Rework AS Reworked, Rework_ID AS [Rework ID], Hostname FROM correlations_trial WHERE Hostname LIKE @host AND DateEnd >= @start AND DateEnd < @end", new(){["@host"]=host,["@start"]=start,["@end"]=end},token);
    public async Task<DataTable> DefectedAsync(string site,string host,DateTime start,DateTime end,CancellationToken token=default) => await ParameterizedAsync(site,"SELECT DISTINCT se.Hostname, ct.Ref AS Reference, se.Part AS [TT Label], se.Type AS [Error Code], ct.OUT_Barcode AS [Harness S/N] FROM wtr.dbo.solvederrors se WITH (NOLOCK) LEFT JOIN wtr.dbo.correlations_trial ct WITH (NOLOCK) ON se.Hostname=ct.Hostname AND ct.BC=se.Part WHERE se.Hostname=@host AND se.DateBegin BETWEEN @start AND @end",new(){["@host"]=host,["@start"]=start,["@end"]=end},token);
    private async Task<DataTable> ParameterizedAsync(string site,string sql,Dictionary<string,object> p,CancellationToken token){await using var c=new SqlConnection(Connection(site));await OpenAsync(c, site, token);await using var cmd=new SqlCommand(sql,c);foreach(var x in p)cmd.Parameters.AddWithValue(x.Key,x.Value);using var r=await cmd.ExecuteReaderAsync(token);var t=new DataTable();t.Load(r);return t;}
}
public sealed record AuthResult(bool Success,List<string> Sites,string? Error){public static AuthResult Ok(List<string> s)=>new(true,s,null);public static AuthResult Fail(string e)=>new(false,new(),e);}
