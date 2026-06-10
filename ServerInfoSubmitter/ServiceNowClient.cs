using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ServerInfoSubmitter
{
    // Minimal record used internally when only sys_id is needed
    public class IncidentRecord
    {
        public string SysId            { get; set; } = string.Empty;
        public string Number           { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
    }

    public class IncidentDetails
    {
        public string SysId            { get; set; } = string.Empty;
        public string Number           { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Description      { get; set; } = string.Empty;
        public string State            { get; set; } = string.Empty;
        public string Priority         { get; set; } = string.Empty;
        public string Severity         { get; set; } = string.Empty;
        public string Urgency          { get; set; } = string.Empty;
        public string Impact           { get; set; } = string.Empty;
        public string AssignedTo       { get; set; } = string.Empty;
        public string AssignmentGroup  { get; set; } = string.Empty;
        public string CallerName       { get; set; } = string.Empty;
        public string Category         { get; set; } = string.Empty;
        public string Subcategory      { get; set; } = string.Empty;
        public string OpenedAt         { get; set; } = string.Empty;
        public string ResolvedAt       { get; set; } = string.Empty;
        public string CmdbCiSysId      { get; set; } = string.Empty;  // raw value for lookups
        public string CmdbCiName       { get; set; } = string.Empty;  // display name
    }

    public class CIRecord
    {
        public string SysId             { get; set; } = string.Empty;
        public string Name              { get; set; } = string.Empty;
        public string Class             { get; set; } = string.Empty;
        public string Manufacturer      { get; set; } = string.Empty;
        public string Model             { get; set; } = string.Empty;
        public string OperatingSystem   { get; set; } = string.Empty;
        public string IPAddress         { get; set; } = string.Empty;
        public string SerialNumber      { get; set; } = string.Empty;
        public string AssetTag          { get; set; } = string.Empty;
        public string OperationalStatus { get; set; } = string.Empty;
        public string InstallStatus     { get; set; } = string.Empty;
        public string Location          { get; set; } = string.Empty;
        public string Department        { get; set; } = string.Empty;
    }

    public class CIIncidentHistory
    {
        public string Number           { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string State            { get; set; } = string.Empty;
        public string Priority         { get; set; } = string.Empty;
        public string OpenedAt         { get; set; } = string.Empty;
        public string ResolvedAt       { get; set; } = string.Empty;
    }

    public class CIChange
    {
        public string Number           { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Type             { get; set; } = string.Empty;
        public string State            { get; set; } = string.Empty;
        public string Risk             { get; set; } = string.Empty;
        public string StartDate        { get; set; } = string.Empty;
        public string EndDate          { get; set; } = string.Empty;
        public string AssignmentGroup  { get; set; } = string.Empty;
    }

    public static class ServiceNowClient
    {
        // Returns the raw value for object-type fields (sys_id, codes)
        private static string FieldValue(JToken? token, string key)
        {
            JToken? t = token?[key];
            if (t == null) return string.Empty;
            return t.Type == JTokenType.Object
                ? t["value"]?.ToString() ?? string.Empty
                : t.ToString();
        }

        // Returns the display_value for object-type fields (human-readable labels)
        private static string FieldDisplay(JToken? token, string key)
        {
            JToken? t = token?[key];
            if (t == null) return string.Empty;
            if (t.Type == JTokenType.Object)
                return t["display_value"]?.ToString() ?? t["value"]?.ToString() ?? string.Empty;
            return t.ToString();
        }

        private static HttpClient BuildClient(string token)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return http;
        }

        private static void EnsureTls12()
            => ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        // Lightweight lookup used by the work-note submission path when no full load has run
        public static async Task<IncidentRecord> GetIncidentAsync(
            string instanceUrl, string token, string incidentNumber)
        {
            EnsureTls12();
            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident"
                + "?sysparm_query=number="          + Uri.EscapeDataString(incidentNumber)
                + "&sysparm_fields=sys_id,number,short_description"
                + "&sysparm_limit=1"
                + "&sysparm_display_value=false";

            using (HttpClient http = BuildClient(token))
            {
                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"Incident lookup failed ({(int)resp.StatusCode}):\n{json}");

                JObject jObj  = JObject.Parse(json);
                JToken? first = jObj["result"]?[0];
                if (first == null)
                    throw new Exception($"Incident {incidentNumber} was not found in ServiceNow.");

                return new IncidentRecord
                {
                    SysId            = FieldValue(first, "sys_id"),
                    Number           = FieldValue(first, "number"),
                    ShortDescription = FieldValue(first, "short_description")
                };
            }
        }

        // Full incident details with display values for all readable fields
        public static async Task<IncidentDetails> GetIncidentDetailsAsync(
            string instanceUrl, string token, string incidentNumber)
        {
            EnsureTls12();
            const string fields =
                "sys_id,number,short_description,description,state,priority,severity," +
                "urgency,impact,assignment_group,assigned_to,caller_id,category," +
                "subcategory,opened_at,resolved_at,cmdb_ci";

            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident"
                + "?sysparm_query=number="    + Uri.EscapeDataString(incidentNumber)
                + "&sysparm_fields="          + fields
                + "&sysparm_limit=1"
                + "&sysparm_display_value=all";  // returns both value and display_value per field

            using (HttpClient http = BuildClient(token))
            {
                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"Incident lookup failed ({(int)resp.StatusCode}):\n{json}");

                JObject jObj  = JObject.Parse(json);
                JToken? first = jObj["result"]?[0];
                if (first == null)
                    throw new Exception($"Incident {incidentNumber} was not found in ServiceNow.");

                return new IncidentDetails
                {
                    SysId            = FieldValue(first,   "sys_id"),
                    Number           = FieldDisplay(first,  "number"),
                    ShortDescription = FieldDisplay(first,  "short_description"),
                    Description      = FieldDisplay(first,  "description"),
                    State            = FieldDisplay(first,  "state"),
                    Priority         = FieldDisplay(first,  "priority"),
                    Severity         = FieldDisplay(first,  "severity"),
                    Urgency          = FieldDisplay(first,  "urgency"),
                    Impact           = FieldDisplay(first,  "impact"),
                    AssignedTo       = FieldDisplay(first,  "assigned_to"),
                    AssignmentGroup  = FieldDisplay(first,  "assignment_group"),
                    CallerName       = FieldDisplay(first,  "caller_id"),
                    Category         = FieldDisplay(first,  "category"),
                    Subcategory      = FieldDisplay(first,  "subcategory"),
                    OpenedAt         = FieldDisplay(first,  "opened_at"),
                    ResolvedAt       = FieldDisplay(first,  "resolved_at"),
                    CmdbCiSysId      = FieldValue(first,   "cmdb_ci"),
                    CmdbCiName       = FieldDisplay(first,  "cmdb_ci")
                };
            }
        }

        // Configuration item details from the CMDB base class
        public static async Task<CIRecord> GetCIAsync(
            string instanceUrl, string token, string ciSysId)
        {
            EnsureTls12();
            const string fields =
                "sys_id,name,sys_class_name,manufacturer,model_id,os," +
                "ip_address,serial_number,asset_tag,operational_status," +
                "install_status,location,department";

            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/cmdb_ci/" + ciSysId
                + "?sysparm_fields="      + fields
                + "&sysparm_display_value=true";

            using (HttpClient http = BuildClient(token))
            {
                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"CI lookup failed ({(int)resp.StatusCode}):\n{json}");

                JToken? result = JObject.Parse(json)["result"];
                if (result == null)
                    throw new Exception("CI not found.");

                return new CIRecord
                {
                    SysId             = ciSysId,
                    Name              = result["name"]?.ToString()              ?? string.Empty,
                    Class             = result["sys_class_name"]?.ToString()    ?? string.Empty,
                    Manufacturer      = result["manufacturer"]?.ToString()      ?? string.Empty,
                    Model             = result["model_id"]?.ToString()          ?? string.Empty,
                    OperatingSystem   = result["os"]?.ToString()                ?? string.Empty,
                    IPAddress         = result["ip_address"]?.ToString()        ?? string.Empty,
                    SerialNumber      = result["serial_number"]?.ToString()     ?? string.Empty,
                    AssetTag          = result["asset_tag"]?.ToString()         ?? string.Empty,
                    OperationalStatus = result["operational_status"]?.ToString() ?? string.Empty,
                    InstallStatus     = result["install_status"]?.ToString()    ?? string.Empty,
                    Location          = result["location"]?.ToString()          ?? string.Empty,
                    Department        = result["department"]?.ToString()        ?? string.Empty
                };
            }
        }

        // Last 25 incidents linked to a CI, newest first
        public static async Task<List<CIIncidentHistory>> GetCIIncidentHistoryAsync(
            string instanceUrl, string token, string ciSysId)
        {
            EnsureTls12();
            const string fields = "number,short_description,state,priority,opened_at,resolved_at";
            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident"
                + "?sysparm_query=cmdb_ci=" + ciSysId + "^ORDERBYDESCopened_at"
                + "&sysparm_fields="        + fields
                + "&sysparm_limit=25"
                + "&sysparm_display_value=true";

            using (HttpClient http = BuildClient(token))
            {
                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"CI incident history failed ({(int)resp.StatusCode}):\n{json}");

                var items = new List<CIIncidentHistory>();
                JArray? results = JObject.Parse(json)["result"] as JArray;
                if (results == null) return items;

                foreach (JToken r in results)
                {
                    items.Add(new CIIncidentHistory
                    {
                        Number           = r["number"]?          .ToString() ?? string.Empty,
                        ShortDescription = r["short_description"]?.ToString() ?? string.Empty,
                        State            = r["state"]?           .ToString() ?? string.Empty,
                        Priority         = r["priority"]?        .ToString() ?? string.Empty,
                        OpenedAt         = r["opened_at"]?       .ToString() ?? string.Empty,
                        ResolvedAt       = r["resolved_at"]?     .ToString() ?? string.Empty
                    });
                }
                return items;
            }
        }

        // Last 25 change requests linked to a CI, newest first
        public static async Task<List<CIChange>> GetCIChangesAsync(
            string instanceUrl, string token, string ciSysId)
        {
            EnsureTls12();
            const string fields =
                "number,short_description,type,state,risk,start_date,end_date,assignment_group";
            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/change_request"
                + "?sysparm_query=cmdb_ci=" + ciSysId + "^ORDERBYDESCstart_date"
                + "&sysparm_fields="        + fields
                + "&sysparm_limit=25"
                + "&sysparm_display_value=true";

            using (HttpClient http = BuildClient(token))
            {
                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"CI change history failed ({(int)resp.StatusCode}):\n{json}");

                var items = new List<CIChange>();
                JArray? results = JObject.Parse(json)["result"] as JArray;
                if (results == null) return items;

                foreach (JToken r in results)
                {
                    items.Add(new CIChange
                    {
                        Number           = r["number"]?          .ToString() ?? string.Empty,
                        ShortDescription = r["short_description"]?.ToString() ?? string.Empty,
                        Type             = r["type"]?            .ToString() ?? string.Empty,
                        State            = r["state"]?           .ToString() ?? string.Empty,
                        Risk             = r["risk"]?            .ToString() ?? string.Empty,
                        StartDate        = r["start_date"]?      .ToString() ?? string.Empty,
                        EndDate          = r["end_date"]?        .ToString() ?? string.Empty,
                        AssignmentGroup  = r["assignment_group"]?.ToString() ?? string.Empty
                    });
                }
                return items;
            }
        }

        public static async Task PostWorkNoteAsync(
            string instanceUrl, string token, string sysId, string note)
        {
            EnsureTls12();
            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident/" + sysId;

            using (HttpClient http = BuildClient(token))
            {
                string body    = new JObject { ["work_notes"] = note }.ToString();
                var    content = new StringContent(body, Encoding.UTF8, "application/json");

                // HttpClient on .NET Framework 4.x has no PatchAsync
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), uri)
                {
                    Content = content
                };

                HttpResponseMessage resp = await http.SendAsync(request);
                string respBody = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"Failed to post work note ({(int)resp.StatusCode}):\n{respBody}");
            }
        }
    }
}
