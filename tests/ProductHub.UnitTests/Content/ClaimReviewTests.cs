using ProductHub.Application.Content;

namespace ProductHub.UnitTests.Content;

public sealed class ClaimReviewTests
{
    private static ProductContentContext Context(bool includeFacts = true) =>
        new(
            "B-1",
            "Bottle",
            "Acme",
            [new("Capacity", "750", "ml")],
            [new("manual.md#chunk-0", "Clean with warm water.")],
            includeFacts
        );

    private static ReviewedClaim Check(string text, bool facts = true) =>
        ClaimReview
            .Assess(new(text, "Short.", "Long.", ["One", "Two", "Three"]), Context(facts))
            .Claims.First();

    [Fact]
    public void IdentityOnlySparseOutput_RequiresExplicitWarningAcknowledgement()
    {
        var content = new GeneratedProductContent("Bottle", "Bottle", "Bottle", []);
        GeneratedContentContract.Validate(content);
        var report = ClaimReview.Assess(content, new("SKU", "Bottle", "Acme", [], [], false));
        Assert.All(report.Claims, c => Assert.Equal("Supported", c.Status));
        Assert.True(report.RequiresAcknowledgement);
        Assert.Equal(2, report.Warnings!.Count);
        Assert.False(report.WarningsAcknowledged);
    }

    [Fact]
    public void ExactFact_IsSupported()
    {
        var claim = Check("Capacity: 750 ml");
        Assert.Equal("Supported", claim.Status);
        Assert.Equal("750 ml", Assert.Single(claim.Evidence).Text);
    }

    [Fact]
    public void ExtraClaim_IsNotVerifiedByMentioningFact() =>
        Assert.Equal("Unsupported", Check("Certified bottle with 750 ml capacity.").Status);

    [Fact]
    public void WrongQuantity_IsConflict() =>
        Assert.Equal("Conflict", Check("Capacity 1000 ml").Status);

    [Fact]
    public void FactsOff_CannotEstablishSpecifications()
    {
        var claim = Check("750 ml", false);
        Assert.Equal("Unsupported", claim.Status);
        Assert.DoesNotContain(claim.Evidence, x => x.Kind == "Fact");
    }

    [Fact]
    public void ExactGuidance_IsAdvisory() =>
        Assert.Equal("Advisory", Check("Clean with warm water.").Status);

    [Fact]
    public void UnrecognizedClaims_RemainUnverified() =>
        Assert.Equal("Unverified", Check("Exceptional construction.").Status);

    [Fact]
    public void Identifiers_AreStableAndChangeWithText()
    {
        Assert.Equal(Check("Bottle").Id, Check("Bottle").Id);
        Assert.NotEqual(Check("Bottle").Id, Check("Bottle new").Id);
    }

    [Fact]
    public void Report_RetainsOriginalSnapshot()
    {
        var report = ClaimReview.Assess(
            new("Bottle", "Short.", "Long.", ["One", "Two", "Three"]),
            Context()
        );
        Assert.Equal("750", report.Context.Attributes[0].Value);
        Assert.Equal("claim-review-v2", report.Version);
    }
}
