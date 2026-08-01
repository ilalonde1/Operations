#nullable enable
namespace Kor.Operations.FileSync.Service.Alerting;

internal interface IAlertNotifier
{
    Task SendAlertAsync(string subject, string body, CancellationToken ct);
}
