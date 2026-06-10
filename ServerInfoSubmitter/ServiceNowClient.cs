using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ServerInfoSubmitter
{
    public class IncidentRecord
    {
        public string SysId            { get; set; } = string.Empty;
        public string Number           { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
    }

    public static class ServiceNowClient
    {
        // SN sometimes wraps fields as {"value":"...","display_value":"..."} depending on API settings
        private static string Field(JToken? token, string key)
        {
            JToken? t = token?[key];
            if (t == null) return string.Empty;
            return t.Type == JTokenType.Object
                ? t["value"]?.ToString() ?? string.Empty
                : t.ToString();
        }

        public static async Task<IncidentRecord> GetIncidentAsync(
            string instanceUrl, string token, string incidentNumber)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident"
                + "?sysparm_query=number="         + Uri.EscapeDataString(incidentNumber)
                + "&sysparm_fields=sys_id,number,short_description"
                + "&sysparm_limit=1"
                + "&sysparm_display_value=false";

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage resp = await http.GetAsync(uri);
                string json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"Incident lookup failed ({(int)resp.StatusCode}):\n{json}");

                JObject jObj   = JObject.Parse(json);
                JToken?  first = jObj["result"]?[0];
                if (first == null)
                    throw new Exception($"Incident {incidentNumber} was not found in ServiceNow.");

                return new IncidentRecord
                {
                    SysId            = Field(first, "sys_id"),
                    Number           = Field(first, "number"),
                    ShortDescription = Field(first, "short_description")
                };
            }
        }

        public static async Task PostWorkNoteAsync(
            string instanceUrl, string token, string sysId, string note)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string uri = instanceUrl.TrimEnd('/') + "/api/now/table/incident/" + sysId;

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                string body    = new JObject { ["work_notes"] = note }.ToString();
                var    content = new StringContent(body, Encoding.UTF8, "application/json");

                // HttpClient on .NET Framework 4.x does not have PatchAsync -- use SendAsync
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
