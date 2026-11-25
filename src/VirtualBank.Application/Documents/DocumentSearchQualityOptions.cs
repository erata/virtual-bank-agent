namespace VirtualBank.Application.Documents;

/// <summary>
/// Conservative retrieval cut-off. A score threshold does not establish semantic truth; it merely removes
/// clearly weak cosine matches. Calibrate with a cross-lingual labelled evaluation set for production.
/// </summary>
public sealed record DocumentSearchQualityOptions(double MinimumCosineSimilarity = 0.45)
{
    public void Validate()
    {
        if (double.IsNaN(MinimumCosineSimilarity) || MinimumCosineSimilarity is < -1 or > 1)
            throw new InvalidOperationException("Documents:MinimumCosineSimilarity must be between -1 and 1.");
    }
}
