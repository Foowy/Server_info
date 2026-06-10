using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ServerInfoSubmitter
{
    public static class OAuthHelper
    {
        private const int    CallbackPort = 9875;
        private const string RedirectUri  = "http://localhost:9875/callback";

        public static async Task<string> GetAccessTokenAsync(AppConfig config, IProgress<string>? progress = null)
        {
            string verifier   = GenerateVerifier();
            string challenge  = GenerateChallenge(verifier);
            string state      = Guid.NewGuid().ToString("N");

            string authUrl = config.InstanceUrl.TrimEnd('/') + "/oauth_auth.do"
                + "?response_type=code"
                + "&client_id="          + Uri.EscapeDataString(config.ClientId)
                + "&redirect_uri="       + Uri.EscapeDataString(RedirectUri)
                + "&code_challenge="     + challenge
                + "&code_challenge_method=S256"
                + "&state="              + state;

            using (var listener = new HttpListener())
            {
                // HttpListener prefix must end with / -- listen on root, filter by path in handler
                listener.Prefixes.Add($"http://localhost:{CallbackPort}/");
                try { listener.Start(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Could not start OAuth callback listener on port {CallbackPort}.\n" +
                        $"Check that no other process is using that port.\n\n{ex.Message}");
                }

                progress?.Report("Opening browser for ServiceNow / Okta login...");
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                progress?.Report("Waiting for authentication (2-minute timeout)...");

                // Wait for the callback with a 2-minute timeout without blocking the thread
                var getCtxTask  = listener.GetContextAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                if (await Task.WhenAny(getCtxTask, timeoutTask) == timeoutTask)
                {
                    listener.Stop();
                    throw new TimeoutException("Authentication timed out. No callback received within 2 minutes.");
                }

                HttpListenerContext ctx = await getCtxTask;

                Uri? callbackUrl = ctx.Request.Url;

                // Only handle /callback -- redirect anything else to a simple error page
                if (callbackUrl?.AbsolutePath != "/callback")
                {
                    SendHtmlResponse(ctx, "<h2>Unexpected path</h2><p>Close this window.</p>");
                    listener.Stop();
                    throw new Exception("OAuth callback arrived at unexpected path: " + callbackUrl?.AbsolutePath);
                }

                SendHtmlResponse(ctx,
                    "<h2 style='color:#2e7d32'>Authentication complete</h2>" +
                    "<p>You may close this browser tab.</p>");
                listener.Stop();

                Dictionary<string, string> parms = ParseQuery(callbackUrl!.Query);

                if (parms.TryGetValue("error", out string? error))
                {
                    parms.TryGetValue("error_description", out string? desc);
                    throw new Exception($"OAuth error: {error} -- {desc}");
                }

                if (!parms.TryGetValue("state", out string? returnedState) || returnedState != state)
                    throw new Exception("OAuth state mismatch -- possible CSRF. Aborting.");

                if (!parms.TryGetValue("code", out string? code) || string.IsNullOrEmpty(code))
                    throw new Exception("No authorization code received in OAuth callback.");

                progress?.Report("Exchanging authorization code for access token...");
                return await ExchangeCodeAsync(config, code, verifier);
            }
        }

        private static async Task<string> ExchangeCodeAsync(AppConfig config, string code, string verifier)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var http = new HttpClient())
            {
                var body = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                    new KeyValuePair<string,string>("client_id",     config.ClientId),
                    new KeyValuePair<string,string>("code",          code),
                    new KeyValuePair<string,string>("code_verifier", verifier),
                    new KeyValuePair<string,string>("redirect_uri",  RedirectUri),
                });

                HttpResponseMessage resp = await http.PostAsync(
                    config.InstanceUrl.TrimEnd('/') + "/oauth_token.do", body);
                string json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"Token endpoint returned {(int)resp.StatusCode}:\n{json}");

                // Parse access_token without a full JSON deserializer
                Match m = Regex.Match(json, "\"access_token\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success)
                    throw new Exception($"No access_token in token response:\n{json}");

                return m.Groups[1].Value;
            }
        }

        private static void SendHtmlResponse(HttpListenerContext ctx, string bodyContent)
        {
            string html = $"<html><body style='font-family:sans-serif;text-align:center;padding:60px'>{bodyContent}</body></html>";
            byte[] buf  = Encoding.UTF8.GetBytes(html);
            try
            {
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        private static Dictionary<string, string> ParseQuery(string? query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;
            foreach (string part in query!.TrimStart('?').Split('&'))
            {
                int idx = part.IndexOf('=');
                if (idx > 0)
                    result[Uri.UnescapeDataString(part.Substring(0, idx))] =
                        Uri.UnescapeDataString(part.Substring(idx + 1));
            }
            return result;
        }

        private static string GenerateVerifier()
        {
            byte[] bytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GenerateChallenge(string verifier)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }
    }
}
