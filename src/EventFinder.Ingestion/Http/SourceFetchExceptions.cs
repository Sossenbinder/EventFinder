using System.Net;

namespace EventFinder.Ingestion.Http;

// Thrown by PoliteHttpClient when the request never got an HTTP response at
// all (DNS failure, connection refused, timed out after retries). Distinct
// from SourceHttpErrorException so SourceVerifier can report "reachable:
// no" instead of "reachable: yes, bad status".
public sealed class SourceUnreachableException(string message, Exception? inner = null)
    : Exception(message, inner);

// Thrown when the server responded but with a non-success, non-304 status.
// Carries the status so SourceVerifier can surface it without re-parsing.
public sealed class SourceHttpErrorException(HttpStatusCode statusCode, string requestUri)
    : Exception($"{(int)statusCode} {statusCode} fetching {requestUri}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

// Thrown by HtmlSource when robots.txt disallows the requested path. Caught
// by IngestionRunner like any other per-source failure and recorded on
// SourceStatus, never allowed to abort a run.
public sealed class RobotsDisallowedException(string url)
    : Exception($"robots.txt disallows fetching {url}");
