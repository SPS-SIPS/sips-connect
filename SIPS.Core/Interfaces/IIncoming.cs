namespace SIPS.Core.Interfaces;

public interface IIncoming
{
    ValueTask<string> Handle(string isoMessage, CancellationToken ct);
}
