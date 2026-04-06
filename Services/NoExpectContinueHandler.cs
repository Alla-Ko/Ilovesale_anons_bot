public class NoExpectContinueHandler : DelegatingHandler
{
	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		request.Headers.ExpectContinue = false; // ← ось це справді вимикає
		return base.SendAsync(request, cancellationToken);
	}
}