using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DragNWash.Installer
{
    // Why a download did not give the file, in the terms the failure window explains.
    internal enum DownloadProblem
    {
        Offline,  // the name did not resolve or nothing answered: no internet
        Busy,     // a 5xx answer, no answer in 30 seconds, or a download that broke off
        Limited,  // 403 or 429: GitHub is limiting downloads
        NotFound, // 404: the release is not published (a draft, or deleted)
        Size,     // not the size the manifest or the installer records
        Tls,      // the secure connection could not be confirmed; there is no way around it
        Other,    // any other answer
    }

    internal sealed class DownloadFailure : Exception
    {
        internal readonly DownloadProblem Problem;

        // Limited: in how many minutes GitHub says to try again; 0 when it did not say.
        internal readonly int Minutes;

        internal DownloadFailure(DownloadProblem problem, string message, Exception inner = null, int minutes = 0) : base(message, inner)
        {
            Problem = problem;
            Minutes = minutes;
        }
    }

    // Downloads for Install: plain HTTPS GETs of fixed github.com addresses, no sign-in,
    // no cookies, only the User-Agent below. The file goes to a temp folder of the
    // run's own, never into the game folder.
    internal static class Downloader
    {
        // For the answer's headers and for each wait on the next bytes.
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        // DragNWash.Installer/1.1.0: the installer's own version, which changes only with its code.
        internal static readonly string UserAgent = "DragNWash.Installer/" + typeof(Downloader).Assembly.GetName().Version.ToString(3);

        // url into file, which must end up exactly size bytes: a bigger one is stopped
        // as soon as it passes that. Tried once more, after two seconds, when GitHub
        // failed on its side or did not answer in time. bytes hears how much has come.
        internal static void Fetch(string url, string file, long size, Action<long> bytes, Action<string> log, string name, CancellationToken cancel)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    FetchOnce(url, file, size, bytes, cancel);
                    return;
                }
                catch (DownloadFailure ex) when (attempt == 1 && ex.Problem == DownloadProblem.Busy)
                {
                    TryDelete(file);
                    log($"{name}: {ex.Message}; trying once more");
                    if (cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        cancel.ThrowIfCancellationRequested();
                    }
                }
                catch (Exception)
                {
                    TryDelete(file);
                    throw;
                }
            }
        }

        private static void FetchOnce(string url, string file, long size, Action<long> bytes, CancellationToken cancel)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Redirects are followed: github.com sends the request on to release-assets.githubusercontent.com.
            using (var client = new HttpClient { Timeout = Timeout })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                HttpResponseMessage response;
                try
                {
                    response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult();
                }
                catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
                {
                    // HttpClient reports its own time-out as a cancellation.
                    throw new DownloadFailure(DownloadProblem.Busy, $"no answer within {Timeout.TotalSeconds:0} seconds", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw Classify(ex);
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw Refused(response);
                    }
                    long? length = response.Content.Headers.ContentLength;
                    if (length != null && length.Value != size)
                    {
                        throw new DownloadFailure(DownloadProblem.Size, $"the server offers {length.Value} bytes, {size} expected");
                    }
                    using (Stream from = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var idle = new CancellationTokenSource())
                    using (cancel.Register(from.Dispose)) // a read that waits on the network ends at once
                    using (idle.Token.Register(from.Dispose))
                    using (FileStream to = File.Create(file))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        while (true)
                        {
                            int read;
                            idle.CancelAfter(Timeout);
                            try
                            {
                                read = from.Read(buffer, 0, buffer.Length);
                            }
                            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is WebException)
                            {
                                cancel.ThrowIfCancellationRequested();
                                throw idle.IsCancellationRequested
                                    ? new DownloadFailure(DownloadProblem.Busy, $"no data for {Timeout.TotalSeconds:0} seconds", ex)
                                    : new DownloadFailure(DownloadProblem.Busy, "the download broke off: " + ex.Message, ex);
                            }
                            if (read == 0)
                            {
                                break;
                            }
                            done += read;
                            if (done > size)
                            {
                                throw new DownloadFailure(DownloadProblem.Size, $"more than the {size} bytes expected");
                            }
                            to.Write(buffer, 0, read);
                            bytes?.Invoke(done);
                        }
                        if (done != size)
                        {
                            // The length was announced and matched: the connection ended early.
                            throw length != null
                                ? new DownloadFailure(DownloadProblem.Busy, $"the download broke off after {done} of {size} bytes")
                                : new DownloadFailure(DownloadProblem.Size, $"{done} bytes, {size} expected");
                        }
                    }
                }
            }
        }

        private static DownloadFailure Classify(HttpRequestException ex)
        {
            for (Exception e = ex.InnerException; e != null; e = e.InnerException)
            {
                if (e is AuthenticationException)
                {
                    return new DownloadFailure(DownloadProblem.Tls, "the secure connection could not be confirmed: " + e.Message, ex);
                }
                if (e is WebException web)
                {
                    switch (web.Status)
                    {
                        case WebExceptionStatus.NameResolutionFailure:
                        case WebExceptionStatus.ProxyNameResolutionFailure:
                        case WebExceptionStatus.ConnectFailure:
                            return new DownloadFailure(DownloadProblem.Offline, $"no connection ({web.Status}): {web.Message}", ex);
                        case WebExceptionStatus.TrustFailure:
                        case WebExceptionStatus.SecureChannelFailure:
                            return new DownloadFailure(DownloadProblem.Tls, $"the secure connection could not be confirmed ({web.Status}): {web.Message}", ex);
                        default:
                            return new DownloadFailure(DownloadProblem.Busy, $"the connection failed ({web.Status}): {web.Message}", ex);
                    }
                }
            }
            return new DownloadFailure(DownloadProblem.Busy, "the connection failed: " + ex.Message, ex);
        }

        private static DownloadFailure Refused(HttpResponseMessage response)
        {
            int status = (int)response.StatusCode;
            string answered = $"GitHub answered {status} {response.ReasonPhrase}";
            if (status >= 500)
            {
                return new DownloadFailure(DownloadProblem.Busy, answered);
            }
            if (status == 404)
            {
                return new DownloadFailure(DownloadProblem.NotFound, answered);
            }
            if (status == 403 || status == 429)
            {
                TimeSpan? wait = null;
                RetryConditionHeaderValue retry = response.Headers.RetryAfter;
                if (retry?.Delta != null)
                {
                    wait = retry.Delta.Value;
                }
                else if (retry?.Date != null)
                {
                    wait = retry.Date.Value - DateTimeOffset.UtcNow;
                }
                else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out long reset))
                {
                    wait = DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow;
                }
                int minutes = wait == null ? 0 : Math.Max(1, (int)Math.Ceiling(wait.Value.TotalMinutes));
                return new DownloadFailure(DownloadProblem.Limited, answered + (minutes > 0 ? $", try again in {minutes} minutes" : ""), minutes: minutes);
            }
            return new DownloadFailure(DownloadProblem.Other, answered);
        }

        internal static string Sha256Of(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // The run's temp folder goes as a whole afterwards.
            }
        }
    }
}
