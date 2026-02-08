namespace SIPS.Core.Interfaces;

public interface IOutgoing
{
    ValueTask<string> Handle(string isoMessage, CancellationToken ct);
}
