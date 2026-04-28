using CSharPers.LPG;

namespace CSharPers.Extractor;

public interface IGraphExtractor
{
    Task<Graph> ExtractAsync();
}
